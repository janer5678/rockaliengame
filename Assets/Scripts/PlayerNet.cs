using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Networked player state + all server-side validation of player actions.
    /// Movement is owner-authoritative (NetworkTransform in Owner mode), everything else is server-authoritative.
    /// Inventory: slots 0..5 are the hotbar, 6..23 the main inventory (Cfg.HotbarSize / MainSize). There is no rock item: an empty hotbar slot
    /// in your hand means you hold your rock. (More in PlayerNet.Extras.cs: vehicles, armour, crossbow, dev tools, voice.)
    /// </summary>
    public partial class PlayerNet : NetworkBehaviour
    {
        public static PlayerNet Local;
        public static readonly List<PlayerNet> All = new List<PlayerNet>();
        /// <summary>Hit capsules live on their own layer: hit by raycasts, but they never block movement or physics.</summary>
        public const int HitboxLayer = 30;
        static bool s_LayersSet;

        // ---- server-written state ----
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Slot = new NetworkVariable<byte>(); // which of the team's players (2v2: 0 or 1)
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(Cfg.MaxHealth);
        public readonly NetworkVariable<bool> Dead = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> RespawnAt = new NetworkVariable<double>();
        public readonly NetworkVariable<int> StuckSpears = new NetworkVariable<int>(); // thrown spears stuck in this player
        public readonly NetworkVariable<byte> HelmetHp = new NetworkVariable<byte>();  // 1 = wearing the headshot helmet
        public readonly NetworkVariable<byte> ArmorHp = new NetworkVariable<byte>();   // wooden armour: second health bar
        public readonly NetworkVariable<ulong> RidingId = new NetworkVariable<ulong>(); // vehicle / horse we sit on (0 = none)
        public readonly NetworkVariable<double> GiantUntil = new NetworkVariable<double>(-1); // staff of the giant
        public readonly NetworkVariable<double> InvisUntil = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> RevealUntil = new NetworkVariable<double>(-1); // attacking shows you for a moment
        public readonly NetworkList<ItemStack> Inv = new NetworkList<ItemStack>();

        // ---- owner-written state ----
        public readonly NetworkVariable<byte> HeldSlot = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Pitch = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> Crouch = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> Jetting = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>What the player is doing (sliding, drawing a bow, aiming, eating...) so everyone sees it on their body (BodyAnimator.Act).</summary>
        public readonly NetworkVariable<byte> Action = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public ItemStack SlotAt(int i) => i >= 0 && i < Inv.Count ? Inv[i] : default;
        public ItemStack HeldStack => SlotAt(HeldSlot.Value);
        /// <summary>What's in your hand. An empty slot means the rock (and in sudden death it's always the rock).</summary>
        public Item HeldItem
        {
            get
            {
                if (NetGame.Instance != null && (NetGame.Instance.S == GameState.SuddenDeath || NetGame.Instance.S == GameState.Waiting)) return Item.Rock;
                var s = HeldStack;
                return s.Empty ? Item.Rock : s.Id;
            }
        }
        public bool Riding => RidingId.Value != 0;
        public int Count(Item id) => Inv.Count > 0 ? InvOps.Count(Inv, id) : 0;
        public bool CarryingBall => Ball.Instance != null && Ball.Instance.CarrierId.Value == NetworkObjectId;
        public bool Giant => GiantUntil.Value > (NetworkManager != null ? NetworkManager.ServerTime.Time : 0);
        /// <summary>Visual size (giants are huge but keep a normal-size hitbox).</summary>
        public float Scale => Giant ? Cfg.GiantScale : 1f;
        public float EyeHeight => (Crouch.Value ? Cfg.CrouchEyeHeight : Cfg.EyeHeight) * Scale;
        public bool TreeCamo => HeldItem == Item.TreeCamo && !Dead.Value && !CarryingBall && !Riding;
        public Vector3 EyePos => transform.position + Vector3.up * EyeHeight;
        public bool IsHeadshot(Vector3 point) => point.y > transform.position.y + (Crouch.Value ? 1.09f : 1.45f); // above the alien's neck
        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public bool Invisible => InvisUntil.Value > Now;
        /// <summary>Invisible and not currently attacking: other players can't see you.</summary>
        public bool Hidden => Invisible && RevealUntil.Value < Now;
        /// <summary>Dead, timer done and the ball is live: waiting for "respawn in base / in the wild".</summary>
        public bool ChoosingRespawn => Dead.Value && Now >= RespawnAt.Value && NetGame.Instance != null && NetGame.Instance.S == GameState.BallLive;

        /// <summary>First hotbar slot holding this item, or -1.</summary>
        public int HotbarSlotOf(Item id)
        {
            for (int i = 0; i < Cfg.HotbarSize; i++) if (SlotAt(i).Id == id) return i;
            return -1;
        }

        // ---- visuals ----
        CharacterController m_CC;
        Transform m_VisualRoot, m_Head, m_Hand;
        bool m_Lifted, m_SeeSelf;
        GameObject m_HandItem, m_Helmet, m_Armor, m_Tree, m_Flame;
        bool m_TreePsx;
        bool m_Esp;
        float m_VisScale = 1f;
        static Material s_EspMat;
        Item m_HandItemId = (Item)255;
        BodyAnimator m_Anim;
        CapsuleCollider m_Hitbox;
        float m_DeadSince = -10f;
        bool m_WasDead;
        readonly List<Renderer> m_TeamRenderers = new List<Renderer>();
        readonly List<Material> m_TeamMats = new List<Material>();
        readonly List<GameObject> m_StuckVisuals = new List<GameObject>();
        float m_Swing, m_CrouchVis, m_Throw;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            m_CC = GetComponent<CharacterController>();
            if (!s_LayersSet)
            {
                s_LayersSet = true;
                for (int l = 0; l < 32; l++) Physics.IgnoreLayerCollision(HitboxLayer, l, true);
            }
            var hb = new GameObject("hitbox") { layer = HitboxLayer };
            hb.transform.SetParent(transform, false);
            m_Hitbox = hb.AddComponent<CapsuleCollider>();
            if (IsServer)
            {
                // join the team with the fewest players (1v1 and free for all: everyone gets their own; 2v2: two each)
                int best = 0, bestCount = int.MaxValue;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int n = 0;
                    foreach (var p in All) if (p != this && p.Team.Value == t) n++;
                    if (n < bestCount) { bestCount = n; best = t; }
                }
                Team.Value = (byte)best;
                Slot.Value = (byte)bestCount;
                Health.Value = Cfg.MaxHealth;
                for (int i = 0; i < Cfg.PlayerSlots; i++) Inv.Add(default);
                Fx.Server(FxKind.Spawn, Cfg.SpawnPos(Team.Value), Vector3.up);
            }

            BuildBody();
            Team.OnValueChanged += OnTeamChanged;
            if (IsOwner)
            {
                Local = this;
                SendMyName(); // (the name typed on the main menu: PlayerNet.Identity.cs)
                Art.SetLayerShadowsOnly(m_VisualRoot.gameObject);
            }
            name = $"Player {OwnerClientId}";
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Team.OnValueChanged -= OnTeamChanged;
            if (Local == this) Local = null;
        }

        void OnTeamChanged(byte prev, byte cur) => Recolor();

        void BuildBody()
        {
            m_VisualRoot = new GameObject("body").transform;
            m_VisualRoot.SetParent(transform, false);
            m_Head = new GameObject("head").transform;
            m_Head.SetParent(m_VisualRoot, false);
            m_Head.localPosition = new Vector3(0, 1.6f, 0);
            m_Anim = BodyAnimator.TryCreate(m_VisualRoot, Cfg.ModelWidth, out var rigged);
            if (m_Anim != null) SkinAlien(rigged);
            else
            {
                if (rigged) Destroy(rigged);
                if (!BuildAlien()) BuildBlockBody();
            }
            m_Hand = new GameObject("hand").transform;
            m_Hand.SetParent(m_VisualRoot, false);
            m_Hand.localPosition = new Vector3(0.45f, 0.85f, 0.15f); // the alien's right hand
            Recolor();
        }

        /// <summary>PSX grey alien (Assets/Game/Resources/Alien, CC0 by surt). Body tinted in the team colour.</summary>
        bool BuildAlien()
        {
            var prefab = Resources.Load<GameObject>("Alien/Alien");
            if (prefab == null) return false;
            var model = Instantiate(prefab, m_VisualRoot, false);
            model.name = "alien";
            model.transform.localScale = new Vector3(Cfg.ModelWidth, 1f, Cfg.ModelWidth);
            SkinAlien(model);
            return true;
        }

        void SkinAlien(GameObject model)
        {
            SkinAlien(model, m_TeamMats);
            // shaded smooth or flat as Settings > Display > SHADING (aliens & crowd) says, live
            SmoothShadeHook.Add(model, true);
        }

        /// <summary>
        /// URP materials for the alien model. The rigged alien (PS1 grey alien, materials "Alien2" / "Alien2_Head") is
        /// tinted all over in the team colour (head a bit lighter, see TeamTint); the old fallback model only on its body.
        /// </summary>
        public static void SkinAlien(GameObject model, List<Material> teamMats)
        {
            var bodyTex = Resources.Load<Texture2D>("Alien/Alien_Body");
            var headTex = Resources.Load<Texture2D>("Alien/Alien_Head");
            var alien2Tex = Resources.Load<Texture2D>("Alien/Alien2");
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    bool alien2 = n.Contains("Alien2"), head = n.Contains("Head");
                    var m = new Material(Art.Mat(Color.white)) { name = n };
                    var tex = alien2 ? alien2Tex : head ? headTex : bodyTex;
                    if (tex != null)
                    {
                        tex.filterMode = FilterMode.Point;
                        m.SetTexture("_BaseMap", tex);
                        m.mainTexture = tex;
                    }
                    if (alien2) m.SetFloat("_Smoothness", 0f);
                    if (alien2 || !head) teamMats.Add(m);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Material colour for a team-tinted alien material (the grey texture times a bright team colour).</summary>
        public static Color TeamTint(Material m, int team)
        {
            var c = Cfg.TeamColor[Mathf.Clamp(team, 0, 3)];
            if (!m.name.Contains("Alien2")) return Color.Lerp(Color.white, c, 0.6f);
            // the PS1 alien's texture is a mid grey: push the colour above 1 so the body comes out a clear team colour
            // with the texture's shading kept; the head stays closer to alien grey so the face still reads
            var t = m.name.Contains("Head") ? Color.Lerp(Color.white, c, 0.55f) * 1.35f : c * 1.6f;
            t.a = 1f;
            return t;
        }

        void BuildBlockBody()
        {
            var torso = Art.Part(m_VisualRoot, Art.Capsule, Color.white, new Vector3(0, 0.85f, 0), new Vector3(0.75f, 0.8f, 0.6f));
            m_TeamRenderers.Add(torso.GetComponent<Renderer>());
            Art.Box(m_Head, new Color(0.93f, 0.76f, 0.6f), Vector3.zero, new Vector3(0.42f, 0.42f, 0.42f));
            var band = Art.Box(m_Head, Color.white, new Vector3(0, 0.17f, 0), new Vector3(0.45f, 0.1f, 0.45f));
            m_TeamRenderers.Add(band.GetComponent<Renderer>());
        }

        void Recolor()
        {
            var c = Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, 3)];
            foreach (var r in m_TeamRenderers) r.sharedMaterial = Art.Mat(c);
            foreach (var m in m_TeamMats)
            {
                var tint = TeamTint(m, Team.Value);
                m.SetColor("_BaseColor", tint);
                m.color = tint;
            }
        }

        static readonly Vector3[] k_StuckPos = { new Vector3(0.1f, 1.15f, 0.05f), new Vector3(-0.15f, 0.9f, 0.05f), new Vector3(0.05f, 1.35f, -0.05f), new Vector3(-0.14f, 0.78f, 0f) };
        static readonly Vector3[] k_StuckDir = { new Vector3(0.1f, -0.3f, -1f), new Vector3(-0.3f, -0.1f, -1f), new Vector3(0.2f, -0.5f, 1f), new Vector3(-0.1f, 0.2f, -1f) };

        void RebuildStuckSpears()
        {
            foreach (var g in m_StuckVisuals) if (g) Destroy(g);
            m_StuckVisuals.Clear();
            int n = Mathf.Min(StuckSpears.Value, k_StuckPos.Length);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("stuckSpear");
                go.transform.SetParent(m_VisualRoot, false);
                // tip sits inside the body, the shaft sticks out the way it came from
                go.transform.localPosition = k_StuckPos[i];
                go.transform.localRotation = Quaternion.LookRotation(k_StuckDir[i]);
                ItemModels.CreateSpearTipForward(go.transform);
                if (IsOwner) Art.SetLayerShadowsOnly(go);
                m_StuckVisuals.Add(go);
            }
        }

        void RebuildHelmet()
        {
            if (m_Helmet) { Destroy(m_Helmet); m_Helmet = null; }
            if (HelmetHp.Value == 0) return;
            var head = m_Anim != null && m_Anim.HeadBone != null ? m_Anim.HeadBone : m_Head;
            m_Helmet = ItemModels.Create(Item.Helmet, head);
            if (m_Anim != null && m_Anim.HeadBone != null) FitHelmet(m_Helmet, head, m_VisualRoot);
            else m_Helmet.transform.localPosition = new Vector3(0, 0.1f, 0);
            if (IsOwner) Art.SetLayerShadowsOnly(m_Helmet);
        }

        /// <summary>Sits a helmet (child of the Head bone, which is at the jaw) over the alien's head, upright in character space.</summary>
        public static void FitHelmet(GameObject helmet, Transform head, Transform root)
        {
            var t = helmet.transform;
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one;
            // sized from what it really measures on the (scaled) skeleton: a bit bigger than the head
            var b = new Bounds(t.position, Vector3.zero);
            foreach (var r in helmet.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            float w = (b.size.x + b.size.z) * 0.5f;
            if (w > 1e-4f) t.localScale = Vector3.one * (0.4f / w);
            t.rotation = root.rotation;
            t.position = head.position + root.up * 0.09f + root.forward * 0.02f;
        }

        void RebuildArmor()
        {
            if (m_Armor) { Destroy(m_Armor); m_Armor = null; }
            if (ArmorHp.Value == 0) return;
            var chest = m_Anim != null && m_Anim.ChestBone != null ? m_Anim.ChestBone : m_VisualRoot;
            m_Armor = new GameObject("armour");
            m_Armor.transform.SetParent(chest, false);
            m_Armor.transform.SetPositionAndRotation(m_Anim != null && m_Anim.ChestBone != null ? chest.position + m_VisualRoot.up * 0.07f : m_VisualRoot.position + Vector3.up * 1.1f, m_VisualRoot.rotation);
            m_Armor.transform.localScale = Vector3.one / Mathf.Max(0.01f, chest.lossyScale.y);
            var t = m_Armor.transform;
            float w = 0.34f * Cfg.ModelWidth;
            // wooden plates strapped on front and back, with shoulder pads
            Art.Box(t, Art.Wood, new Vector3(0, 0, 0.14f), new Vector3(w, 0.42f, 0.05f));
            Art.Box(t, Art.DarkWood, new Vector3(0, 0.1f, 0.17f), new Vector3(w + 0.02f, 0.05f, 0.02f));
            Art.Box(t, Art.DarkWood, new Vector3(0, -0.1f, 0.17f), new Vector3(w + 0.02f, 0.05f, 0.02f));
            Art.Box(t, Art.Wood, new Vector3(0, 0, -0.14f), new Vector3(w, 0.42f, 0.05f));
            Art.Box(t, Art.Wood, new Vector3(w * 0.55f, 0.2f, 0), new Vector3(0.14f, 0.06f, 0.3f), new Vector3(0, 0, -20));
            Art.Box(t, Art.Wood, new Vector3(-w * 0.55f, 0.2f, 0), new Vector3(0.14f, 0.06f, 0.3f), new Vector3(0, 0, 20));
            if (IsOwner) Art.SetLayerShadowsOnly(m_Armor);
        }

        GameObject m_Skeleton;

        void SetSkeleton(bool on)
        {
            if (m_Skeleton) Destroy(m_Skeleton);
            m_Skeleton = null;
            if (!on) return;
            m_Skeleton = PsxModels.Spawn("skeleton", transform);
            if (m_Skeleton == null) return;
            // lying on its back, head behind where they stood
            PsxModels.FitInto(m_Skeleton.transform, transform, new Bounds(new Vector3(0, 0.15f, -0.45f), new Vector3(0.7f, 0.3f, 1.8f)), PsxModels.Fit.Uniform, new Vector3(-90, 0, 0));
        }

        void RebuildTree(bool on)
        {
            bool had = m_Tree != null;
            // shaking the disguise off: the tree shrinks away where it stood in a puff of leaves (TreeCamoPoof)
            if (had && !on) { TreeCamoFx(false); TreeCamoPoof.Begin(m_Tree); m_Tree = null; }
            if (m_Tree) { Destroy(m_Tree); m_Tree = null; }
            if (!on) return;
            // becoming a tree: it springs up out of the ground (TreeHop grows it), leaves flying
            m_TreeGrow = had ? 1f : 0f;
            if (!had) TreeCamoFx(true);
            // everyone sees the tree - you too, from the third-person view you get while holding it
            m_TreePsx = GameSettings.PsxGraphics || GameSettings.AiPsx;
            m_Tree = new GameObject("treeCamo");
            m_Tree.transform.SetParent(transform, false);
            // the tree itself sits on a pivot at its foot, which hops it about while you walk (TreeHop)
            m_TreeHop = new GameObject("hop").transform;
            m_TreeHop.SetParent(m_Tree.transform, false);
            ResourceNode.BuildTreeVisual(m_TreeHop, (int)(NetworkObjectId * 7919 % 100000), false);
            m_Tree.transform.rotation = TreeCamoRotation;
            m_TreeLastPos = transform.position;
            m_TreeMove = m_TreePhase = 0f;
            m_Tree.transform.localScale = TreeGrowScale(m_TreeGrow);
        }

        /// <summary>Becoming a tree takes this long (it springs up); shaking it off is TreeCamoPoof.Time.</summary>
        public const float TreeGrowTime = 0.35f;
        float m_TreeGrow = 1f;
        /// <summary>For the tests: times this player's disguise went on / came off with its sound and leaves (this machine).</summary>
        public int TreeCamoOns { get; private set; }
        public int TreeCamoOffs { get; private set; }

        /// <summary>The tree springing up: thin and small at first, shooting up past its height and settling (0..1).</summary>
        static Vector3 TreeGrowScale(float k)
        {
            if (k >= 1f) return Vector3.one;
            float back = 1f + 2.6f * Mathf.Pow(k - 1f, 3f) + 1.6f * Mathf.Pow(k - 1f, 2f); // (ease out, a little past 1)
            return new Vector3(Mathf.Lerp(0.15f, 1f, k * k), Mathf.Max(0.02f, back), Mathf.Lerp(0.15f, 1f, k * k));
        }

        /// <summary>The disguise going on / coming off: its sound (heard from where the tree is; your own is in your ears)
        /// and a burst of leaves round the crown.</summary>
        void TreeCamoFx(bool on)
        {
            if (on) TreeCamoOns++; else TreeCamoOffs++;
            var p = transform.position;
            var clip = on ? Sfx.TreeOn : Sfx.TreeOff;
            if (IsOwner) Sfx.Play2D(clip, 0.5f);
            else Sfx.Play(clip, p + Vector3.up * 1.5f, 0.9f, 0.08f, 45f);
            var leaf = new Color(0.24f, 0.5f, 0.2f);
            for (int i = 0; i < 12; i++)
            {
                var o = Random.insideUnitSphere * 1.3f;
                FxLeaf.Spawn(p + new Vector3(o.x, 2.6f + o.y, o.z), leaf * Random.Range(0.8f, 1.2f), Random.Range(0f, 0.2f));
            }
        }

        /// <summary>How much faster than TreeHopStride's time the tree hops at this speed: quick little hops at a walk,
        /// easing back to 1 at a sprint (the sprint keeps its own time).</summary>
        public const float TreeWalkHopMul = 1.5f;
        public static float TreeHopRate(float speed) => Mathf.Lerp(TreeWalkHopMul, 1f, TreeSprintK(speed));
        /// <summary>0 at a walk .. 1 at a sprint.</summary>
        static float TreeSprintK(float speed) => Mathf.InverseLerp(Cfg.WalkSpeed * 1.1f, Cfg.SprintSpeed * 0.95f, speed);

        Transform m_TreeHop;
        Vector3 m_TreeLastPos;
        float m_TreeMove, m_TreePhase, m_TreeSquash;
        /// <summary>How high the camo tree is hopping right now (m; tests).</summary>
        public float TreeHopHeight => m_TreeHop ? m_TreeHop.localPosition.y : 0f;
        /// <summary>How far the camo tree travels in one hop (m): half the hop rate it used to have (1.3 m).</summary>
        public const float TreeHopStride = 2.6f;
        /// <summary>For the tests: how many hops the camo tree has finished.</summary>
        public int TreeHops { get; private set; }
        /// <summary>For the tests: how many of those landed with a sound.</summary>
        public int TreeHopSounds { get; private set; }
        /// <summary>Walking as a tree: how far it rocks from side to side and leans into the way it goes (degrees; a sprint is 6 and 9).</summary>
        public const float TreeWalkWobble = 2f, TreeWalkLean = 4f;
        /// <summary>Server, for the tests: walls refused because one was just broken in that spot.</summary>
        public static int WallRebuildRefusals;

        /// <summary>
        /// Tree camo on the move: the tree bounces along in little hops - up off the ground stretched tall, a squash as it
        /// lands, leaning into the way it's going and wobbling - and settles straight back to a still tree when you stop.
        /// Driven by how fast this player really moves, so every screen sees it.
        /// </summary>
        void TreeHop(float dt)
        {
            if (!m_TreeHop || dt <= 0f) return;
            if (m_TreeGrow < 1f)
            {
                m_TreeGrow = Mathf.MoveTowards(m_TreeGrow, 1f, dt / TreeGrowTime);
                m_Tree.transform.localScale = TreeGrowScale(m_TreeGrow);
            }
            var p = transform.position;
            var d = p - m_TreeLastPos;
            m_TreeLastPos = p;
            d.y = 0f;
            float speed = d.magnitude / dt;
            if (speed > 30f) speed = 0f; // teleport
            m_TreeMove = Mathf.MoveTowards(m_TreeMove, Mathf.Clamp01((speed - 0.4f) / 2.5f), dt * 5f);
            if (m_TreeMove <= 0.001f && m_TreePhase == 0f)
            {
                // settle back to a still tree
                float k = Mathf.Clamp01(dt * 14f);
                m_TreeSquash = Mathf.Lerp(m_TreeSquash, 0f, k);
                m_TreeHop.localPosition = Vector3.Lerp(m_TreeHop.localPosition, Vector3.zero, k);
                m_TreeHop.localRotation = Quaternion.Slerp(m_TreeHop.localRotation, Quaternion.identity, k);
                m_TreeHop.localScale = Vector3.Lerp(m_TreeHop.localScale, Vector3.one, k);
                return;
            }
            // a hop every ~2.6 m at a sprint, quicker little ones at a walk (TreeHopRate); finish the hop we're in before settling
            float before = m_TreePhase;
            float sprintK = TreeSprintK(speed);
            m_TreePhase += dt * Mathf.Max(speed, 2.5f) / TreeHopStride * Mathf.PI * TreeHopRate(speed);
            if (Mathf.Floor(before / Mathf.PI) != Mathf.Floor(m_TreePhase / Mathf.PI))
            {
                TreeHops++;
                // it lands with a rustle and a thump (louder at a run): heard from where the tree is; your own in your ears
                if (m_TreeMove > 0.2f)
                {
                    if (IsOwner) Sfx.Play2D(Sfx.TreeRustle, Mathf.Lerp(0.3f, 0.45f, sprintK), 0.12f);
                    else Sfx.Play(Sfx.TreeRustle, p, Mathf.Lerp(0.6f, 0.9f, sprintK), 0.12f, Mathf.Lerp(30f, 45f, sprintK));
                    TreeHopSounds++;
                }
            }
            if (m_TreeMove <= 0.001f && Mathf.Floor(before / Mathf.PI) != Mathf.Floor(m_TreePhase / Mathf.PI)) m_TreePhase = 0f;
            float arc = Mathf.Abs(Mathf.Sin(m_TreePhase));
            float hop = arc * (0.28f + 0.1f * Mathf.Clamp01(speed / 7f)) * Mathf.Max(m_TreeMove, m_TreePhase > 0f ? 0.4f : 0f);
            if (m_TreePhase == 0f) hop = 0f;
            // landing squash: squashes as it touches down, springs back up tall as it takes off
            float target = arc < 0.25f ? -0.16f * (1f - arc / 0.25f) : 0.07f * arc;
            m_TreeSquash = Mathf.Lerp(m_TreeSquash, target * Mathf.Max(m_TreeMove, 0.3f), dt * 18f);
            float sy = 1f + m_TreeSquash, sxz = 1f / Mathf.Sqrt(Mathf.Max(0.5f, sy));
            // lean into the way it's going (in the tree's own space: it never turns) and wobble from side to side
            var lean = Vector3.zero;
            if (d.sqrMagnitude > 1e-8f) lean = m_Tree.transform.InverseTransformDirection(d.normalized);
            // (at a walk it leans and rocks from side to side much less; the sprint is as it was)
            float wob = Mathf.Sin(m_TreePhase * 0.5f) * Mathf.Lerp(TreeWalkWobble, 6f, sprintK) * m_TreeMove;
            var tilt = Quaternion.AngleAxis(Mathf.Lerp(TreeWalkLean, 9f, sprintK) * m_TreeMove, Vector3.Cross(Vector3.up, lean).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, lean).normalized : Vector3.right)
                * Quaternion.Euler(0f, 0f, wob);
            m_TreeHop.localPosition = new Vector3(0f, hop, 0f);
            m_TreeHop.localRotation = Quaternion.Slerp(m_TreeHop.localRotation, tilt, dt * 10f);
            m_TreeHop.localScale = new Vector3(sxz, sy, sxz);
        }

        /// <summary>The camo tree's world facing: the same on every screen and it never turns with the player.</summary>
        public Quaternion TreeCamoRotation => Quaternion.Euler(0f, NetworkObjectId * 137 % 360, 0f);
        public Transform TreeCamoVisual => m_Tree ? m_Tree.transform : null;

        /// <summary>Wallhack glasses: an extra always-on-top red pass on this player's renderers.</summary>
        void SetEsp(bool on)
        {
            m_Esp = on;
            if (s_EspMat == null)
            {
                s_EspMat = new Material(Shader.Find("Hidden/Internal-Colored"));
                s_EspMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                s_EspMat.SetInt("_ZWrite", 0);
                s_EspMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_EspMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                s_EspMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                s_EspMat.color = new Color(1f, 0.15f, 0.1f, 0.45f);
                s_EspMat.renderQueue = 4000;
            }
            foreach (var r in m_VisualRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new List<Material>(r.sharedMaterials);
                mats.RemoveAll(m => m == s_EspMat);
                if (on) mats.Add(s_EspMat);
                r.sharedMaterials = mats.ToArray();
            }
        }

        // ---------------- test: alien outlines ----------------

        static readonly Material[] s_OutlineMats = new Material[4];
        static Shader s_OutlineShader;
        bool m_Outline;
        float m_NextOutlineRefresh;

        /// <summary>The team's glow material, kept in step with the strength / width settings.</summary>
        static Material OutlineMat(int team)
        {
            team = Mathf.Clamp(team, 0, 3);
            if (s_OutlineMats[team] == null)
            {
                if (s_OutlineShader == null) s_OutlineShader = Resources.Load<Shader>("AlienOutline");
                if (s_OutlineShader == null || !s_OutlineShader.isSupported) return null;
                s_OutlineMats[team] = new Material(s_OutlineShader) { name = "alien outline", renderQueue = 3100 };
            }
            var c = Color.Lerp(Cfg.TeamColor[team], Color.white, 0.2f);
            c.a = Mathf.Clamp01(Cfg.AlienOutlineStrength);
            s_OutlineMats[team].SetColor("_Color", c);
            s_OutlineMats[team].SetFloat("_Width", Mathf.Clamp(Cfg.AlienOutlineWidth, 0.002f, 0.3f));
            return s_OutlineMats[team];
        }

        readonly List<GameObject> m_OutlineCopies = new List<GameObject>();

        /// <summary>
        /// Single-part meshes get the glow as an extra material. Meshes made of several parts (the rigged alien body) get a
        /// copy drawn with the glow on every part - an extra material would only cover the last part.
        /// </summary>
        void SetOutline(bool on)
        {
            m_Outline = on;
            foreach (var c in m_OutlineCopies) if (c != null) Destroy(c);
            m_OutlineCopies.Clear();
            var mat = OutlineMat(Team.Value);
            if (mat == null) return;
            foreach (var r in m_VisualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r.name == "outline copy") continue;
                var mats = new List<Material>(r.sharedMaterials);
                mats.RemoveAll(m => m != null && m.name == "alien outline");
                if (on && r is SkinnedMeshRenderer smr && smr.sharedMesh != null && smr.sharedMesh.subMeshCount > 1)
                {
                    var go = new GameObject("outline copy");
                    go.transform.SetParent(smr.transform, false);
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = smr.sharedMesh;
                    copy.bones = smr.bones;
                    copy.rootBone = smr.rootBone;
                    copy.localBounds = smr.localBounds;
                    copy.updateWhenOffscreen = smr.updateWhenOffscreen;
                    copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var all = new Material[smr.sharedMesh.subMeshCount];
                    for (int i = 0; i < all.Length; i++) all[i] = mat;
                    copy.sharedMaterials = all;
                    m_OutlineCopies.Add(go);
                }
                else if (on) mats.Add(mat);
                r.sharedMaterials = mats.ToArray();
            }
        }

        void RebuildFlame(bool on)
        {
            if (m_Flame) { Destroy(m_Flame); m_Flame = null; }
            if (!on) return;
            m_Flame = new GameObject("jetFlame");
            m_Flame.transform.SetParent(m_VisualRoot, false);
            m_Flame.transform.localPosition = new Vector3(0, 0.75f, -0.35f);
            Art.Box(m_Flame.transform, new Color(0.3f, 0.3f, 0.32f), new Vector3(0, 0.3f, 0), new Vector3(0.45f, 0.5f, 0.2f));
            Art.Part(m_Flame.transform, Art.Cone, Color.white, new Vector3(0.12f, 0f, 0), new Vector3(0.14f, -0.5f, 0.14f), default, false, Art.Ghost(new Color(1f, 0.6f, 0.1f, 0.8f)));
            Art.Part(m_Flame.transform, Art.Cone, Color.white, new Vector3(-0.12f, 0f, 0), new Vector3(0.14f, -0.5f, 0.14f), default, false, Art.Ghost(new Color(1f, 0.6f, 0.1f, 0.8f)));
            if (IsOwner) Art.SetLayerShadowsOnly(m_Flame);
        }

        void RebuildHandItem(Item item)
        {
            if (m_HandItem) Destroy(m_HandItem);
            m_HandItemId = item;
            m_HandItem = ItemModels.Create(item, m_Hand);
            m_HandItem.transform.localRotation = m_Anim != null ? Quaternion.identity : Quaternion.Euler(20, 0, 0);
            if (item == Item.Rock) m_HandItem.transform.localScale = Vector3.one * 1.3f;
            if (IsOwner) Art.SetLayerShadowsOnly(m_HandItem);
        }

        Vector3 m_LastFootPos;
        float m_FootDist;
        /// <summary>When we last heard this (other) player's footstep (tests).</summary>
        public float LastStepAt { get; private set; } = -10f;
        AudioSource m_SlideSound;

        /// <summary>
        /// Other players are heard from where they are: footsteps (loud and positional - walking, louder sprinting, silent
        /// when crouch-walking)
        /// and the scrape of a slide.
        /// </summary>
        void RemoteSounds(bool dead)
        {
            var p = transform.position;
            var d = p - m_LastFootPos;
            d.y = 0;
            m_LastFootPos = p;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float dist = d.magnitude, speed = dist / dt;
            bool grounded = !dead && !Riding && dist < 3f && Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 0.6f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore);
            bool sliding = grounded && Crouch.Value && speed > Cfg.CrouchSpeed * 1.4f;
            float slideVol = sliding ? Mathf.Clamp01(speed / 10f) * 0.9f : 0f;
            if (m_SlideSound == null && sliding) m_SlideSound = Sfx.Loop(Sfx.Slide, transform, 0f, 1f, 45f);
            if (m_SlideSound != null) m_SlideSound.volume = Mathf.MoveTowards(m_SlideSound.volume, slideVol * GameSettings.SfxVolume, dt * 4f);
            if (!grounded || Crouch.Value || speed < 1.5f) return;
            m_FootDist += dist;
            bool sprint = speed > Cfg.WalkSpeed * 1.2f;
            if (m_FootDist < (sprint ? 2.2f : 1.8f)) return;
            m_FootDist = 0f;
            // loud, fully 3D thumps where their feet land, so you can hear which way they are and roughly how far
            // (crouch-walking makes no sound at all)
            Sfx.Play(Sfx.EnemyStep, p, sprint ? 1f : 0.8f, 0.15f, sprint ? Cfg.EnemyStepRangeSprint : Cfg.EnemyStepRange);
            LastStepAt = Time.time;
        }

        void Update()
        {
            bool dead = Dead.Value;
            if (!IsOwner) RemoteSounds(dead);
            if (IsServer) { ServerTickCraft(); ServerTickBaseRegen(); ServerTickBleed(); ServerTickPing(); }
            if (dead && !m_WasDead) m_DeadSince = Time.time;
            m_WasDead = dead;
            // the dead leave no body: it's gone the moment they die - just the gravestone where they fell (GraveFx)
            bool showBody = !dead;
            if (!IsOwner && Hidden && !dead) showBody = false; // invisibility potion
            // tree camo: everyone else sees a tree where you stand
            bool tree = TreeCamo;
            if (tree != (m_Tree != null) || (tree && m_TreePsx != (GameSettings.PsxGraphics || GameSettings.AiPsx))) RebuildTree(tree);
            if (tree) showBody = false;
            // PSX graphics: the dead are a skeleton on the ground (the PSX dead model) instead of the alien falling over
            // the victory cutscene: beamed up into the UFO (only the picture moves); once inside, gone
            bool lifted = VictoryCutscene.Lift(this, out float liftUp, out float liftSpin, out float liftScale, out bool liftGone);
            if (lifted && liftGone) showBody = false;
            bool bones = dead && showBody && PsxModels.On;
            if (bones) showBody = false;
            if (bones != (m_Skeleton != null)) SetSkeleton(bones);
            if (m_VisualRoot.gameObject.activeSelf != showBody) m_VisualRoot.gameObject.SetActive(showBody);
            bool cc = !dead && !Riding;
            if (m_CC.enabled != cc) m_CC.enabled = cc;
            m_Hitbox.enabled = !dead;

            // crouch: collider + hitbox (so hits line up on every peer)
            float h = Crouch.Value ? Cfg.CrouchHeight : Cfg.StandHeight;
            if (!Mathf.Approximately(m_CC.height, h))
            {
                m_CC.height = h;
                m_CC.center = new Vector3(0, h * 0.5f, 0);
            }
            m_Hitbox.radius = Cfg.HitboxRadius;
            m_Hitbox.height = Mathf.Max(h + 0.05f, Cfg.HitboxRadius * 2f);
            m_Hitbox.center = new Vector3(0, m_Hitbox.height * 0.5f, 0);
            if (m_Anim == null)
            {
                m_CrouchVis = Mathf.MoveTowards(m_CrouchVis, Crouch.Value ? 1f : 0f, Time.deltaTime * 6f);
                m_VisualRoot.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.68f, m_CrouchVis), 1f);
            }

            var held = CarryingBall || dead || tree ? Item.None : HeldItem;
            if (held != m_HandItemId) RebuildHandItem(held);
            if (m_StuckVisuals.Count != Mathf.Min(StuckSpears.Value, k_StuckPos.Length)) RebuildStuckSpears();
            if ((HelmetHp.Value > 0) != (m_Helmet != null)) RebuildHelmet();
            if ((ArmorHp.Value > 0) != (m_Armor != null)) RebuildArmor();
            // staff of the giant: grow (the hitbox stays normal size)
            m_VisScale = Mathf.MoveTowards(m_VisScale, Scale, Time.deltaTime * 3f);
            m_VisualRoot.localScale = Vector3.one * m_VisScale;
            if (lifted || m_Lifted)
            {
                m_VisualRoot.localPosition = lifted ? Vector3.up * liftUp : Vector3.zero;
                m_VisualRoot.localRotation = lifted ? Quaternion.Euler(0f, liftSpin, 0f) : Quaternion.identity;
                if (lifted) m_VisualRoot.localScale *= liftScale;
                m_Lifted = lifted;
            }
            // in the cutscene you see yourself (your own body is normally only a shadow)
            bool seeSelf = IsOwner && VictoryCutscene.Active;
            if (seeSelf != m_SeeSelf)
            {
                m_SeeSelf = seeSelf;
                foreach (var r in m_VisualRoot.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = seeSelf ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            // wallhack glasses: enemies glow through walls while you hold them
            var local = Local;
            bool esp = !IsOwner && local != null && local.HeldItem == Item.Wallhack && !local.Dead.Value && local.Team.Value != Team.Value && !dead;
            if (esp != m_Esp) SetEsp(esp);
            // test: alien outlines (enemies only, not while invisible)
            bool outline = Cfg.AlienOutlines && !IsOwner && local != null && local.Team.Value != Team.Value && !dead && !Hidden;
            if (outline != m_Outline) SetOutline(outline);
            else if (outline)
            {
                OutlineMat(Team.Value); // follows the strength / thickness sliders live
                // (held items and armour get swapped: give the new ones the glow too)
                if (Time.time >= m_NextOutlineRefresh) { m_NextOutlineRefresh = Time.time + 1f; SetOutline(true); }
            }
            TickTeamSee(local, dead); // your teammates show through walls (PlayerNet.TeamSee.cs)
            // jetpack flame
            bool flame = Jetting.Value && !dead;
            if (flame != (m_Flame != null)) RebuildFlame(flame);
            if (m_Flame) m_Flame.transform.localScale = new Vector3(1f, 0.8f + Random.value * 0.5f, 1f);
            if (m_Swing > 0) m_Swing = Mathf.Max(0, m_Swing - Time.deltaTime / 0.55f);
            if (m_Throw > 0) m_Throw = Mathf.Max(0, m_Throw - Time.deltaTime / 0.5f);
            if (m_Anim == null && !IsOwner) m_Head.localRotation = Quaternion.Euler(Pitch.Value * 0.6f, 0, 0);
            if (m_Anim == null && m_Swing > 0) m_Hand.localRotation = Quaternion.Euler(Mathf.Sin(m_Swing * Mathf.PI) * 80f, 0, 0);

            // before the wall drops you always come back in your base; after that you choose (RespawnChoiceRpc)
            if (IsServer && Dead.Value && NetGame.Instance != null && NetworkManager.ServerTime.Time >= RespawnAt.Value)
            {
                var s = NetGame.Instance.S;
                if (s == GameState.Waiting || s == GameState.PreBall) ServerRespawn(false); // the waiting stadium brings you straight back into the brawl
            }
        }

        void LateUpdate()
        {
            // tree camo: a real tree never turns, so the disguise keeps one fixed facing while you look around
            if (m_Tree) { m_Tree.transform.rotation = TreeCamoRotation; TreeHop(Time.deltaTime); }
            if (m_Anim == null || !m_VisualRoot.gameObject.activeSelf) return;
            var item = m_HandItemId;
            m_Anim.Tick(new BodyAnimator.Pose
            {
                Crouch = Crouch.Value,
                Dead = Dead.Value,
                DeadTime = Time.time - m_DeadSince,
                Carrying = CarryingBall,
                Riding = Riding,
                Holding = item != Item.None,
                TwoHanded = item == Item.Rock || item == Item.Spear || item == Item.Ram || item == Item.Chainsaw || item == Item.Crossbow || item == Item.Shotgun || item == Item.TreeCracker,
                Pitch = Pitch.Value,
                Swing = m_Swing,
                Action = (BodyAnimator.Act)Action.Value,
                Throw = m_Throw,
                Item = item,
            }, Time.deltaTime);
            m_Anim.GripPose(out var gp, out var gr);
            m_Hand.SetPositionAndRotation(gp, gr);
            if (m_HandItem) m_HandItem.transform.localRotation = Quaternion.identity;
        }

        // =====================================================================
        // Server-side logic
        // =====================================================================

        float m_NextMelee, m_NextShot, m_NextBuild, m_NextUpgrade, m_NextThrow, m_NextRam, m_NextEat, m_NextFullMsg, m_NextWand;
        readonly Queue<float> m_PendingArrows = new Queue<float>();
        readonly Queue<float> m_PendingSpears = new Queue<float>();
        int m_PendingC4;

        /// <summary>Attacking while invisible shows you for a moment.</summary>
        void Reveal()
        {
            if (Invisible) RevealUntil.Value = Now + Cfg.InvisRevealTime;
        }

        bool GameAllowsCombat => NetGame.Instance == null || NetGame.Instance.S != GameState.GameOver;
        /// <summary>Sudden death, or rock-brawling in the stadium while waiting for players: rocks only.</summary>
        bool InSuddenDeath => NetGame.Instance != null && (NetGame.Instance.S == GameState.SuddenDeath || NetGame.Instance.S == GameState.Waiting);
        bool InWaitingArena => NetGame.Instance != null && NetGame.Instance.S == GameState.Waiting;

        /// <summary>The match is starting: out of the waiting stadium and onto your bedrock.</summary>
        public void ServerSendHome()
        {
            Health.Value = Cfg.MaxHealth;
            Dead.Value = false;
            NetGame.SpawnPoint(Team.Value, false, Slot.Value, out var pos, out var yaw);
            TeleportRpc(pos, yaw);
            Fx.Server(FxKind.Spawn, pos, Vector3.up);
        }

        /// <summary>Server: give items; returns how many didn't fit. Materials keep clear of the empty slot your rock is in.</summary>
        public int ServerGive(Item id, int count, int data = 0)
        {
            int avoid = Cfg.IsMat(id) && HeldStack.Empty ? HeldSlot.Value : -1;
            int left = InvOps.Add(Inv, id, count, data, true, avoid);
            if (left > 0 && Time.time >= m_NextFullMsg)
            {
                m_NextFullMsg = Time.time + 2f;
                Notify("Inventory full!");
            }
            return left;
        }

        /// <summary>Server: give items that fill the hotbar from the right, like wood does (then the inventory). Returns how many didn't fit.</summary>
        public int ServerGiveFromRight(Item id, int count, int data = 0)
        {
            int avoid = HeldStack.Empty ? HeldSlot.Value : -1;
            int left = InvOps.Add(Inv, id, count, data, true, avoid, true);
            if (left > 0) Notify("Inventory full!");
            return left;
        }

        void ServerClearSlot(int i)
        {
            if (i >= 0 && i < Inv.Count) Inv[i] = default;
        }

        void ServerConsumeHeld(int n = 1)
        {
            var s = HeldStack;
            if (s.Empty) return;
            Inv[HeldSlot.Value] = s.WithCount(s.Count - n);
        }

        public void NotifyPublic(string msg) => Notify(msg);

        /// <summary>Instant death (sniper, airstrike, C4 right on top of you): armour doesn't help.</summary>
        /// <summary>`cause`: what did it, for the kill feed (KillCause: an item, or an explosion, lava...; 0 = what the
        /// attacker is holding).</summary>
        public void ServerKill(PlayerNet attacker, byte cause = 0)
        {
            if (Dead.Value || !GameAllowsCombat || m_God) return;
            ArmorHp.Value = 0;
            ServerDamage(Health.Value + 1f, attacker, cause);
        }

        public void ServerDamage(float dmg, PlayerNet attacker, byte cause = 0)
        {
            if (Dead.Value || !GameAllowsCombat || m_God) return;
            if (ArmorHp.Value > 0 && dmg > 0)
            {
                // the armour bar goes first
                int take = Mathf.Min(ArmorHp.Value, Mathf.CeilToInt(dmg));
                ArmorHp.Value = (byte)(ArmorHp.Value - take);
                dmg = Mathf.Max(0f, dmg - take);
                if (ArmorHp.Value == 0) { Notify("Your armour broke!"); Fx.Server(FxKind.Break, transform.position + Vector3.up * 1.2f, Vector3.up); }
            }
            float hp0 = Health.Value;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            // it bleeds where everyone can see: the weapon's own splash at the hit point if it made one this frame
            // (ServerBleed), otherwise a burst out of the body (explosions, falls...); a kill is a much bigger burst
            if (Health.Value < hp0 && !(attacker == null && cause == KillCause.Lava))
            {
                m_HurtFrame = Time.frameCount;
                m_HurtDir = attacker != null && attacker != this ? transform.position - attacker.transform.position : Vector3.up;
            }
            if (Health.Value <= 0) ServerDie(attacker, cause);
            m_HeadHit = false; // (ServerMarkHead is for one hit)
        }

        int m_HurtFrame = -1, m_BledFrame = -1;
        float m_NextBodyBleed;
        Vector3 m_HurtDir;
        /// <summary>Where blood comes out of the body when the hit has no point of its own (the chest).</summary>
        public Vector3 BleedPos => transform.position + Vector3.up * (Crouch.Value ? 0.8f : 1.1f);

        /// <summary>Server: a hit on this player sprays blood from `point` on every screen (except `skipClient`, who
        /// already drew it). Hits that call this don't also get the body burst ServerDamage would add.</summary>
        public void ServerBleed(bool head, Vector3 point, Vector3 dir, ulong skipClient = ulong.MaxValue)
        {
            m_BledFrame = Time.frameCount;
            Fx.Server(head ? FxKind.BloodHead : FxKind.Blood, point, dir, skipClient);
        }

        /// <summary>Server: damage with no blood of its own (explosions, falls, rockets...) - a burst out of the body, for
        /// everyone (not every frame: at most four a second).</summary>
        void ServerTickBleed()
        {
            if (m_HurtFrame < 0 || Time.frameCount <= m_HurtFrame) return;
            bool bled = m_BledFrame == m_HurtFrame;
            m_HurtFrame = -1;
            if (bled || Dead.Value || Time.time < m_NextBodyBleed) return;
            m_NextBodyBleed = Time.time + 0.25f;
            var d = m_HurtDir; d.y = Mathf.Max(d.y, 0f);
            Fx.Server(FxKind.Blood, BleedPos, d.sqrMagnitude > 0.01f ? d.normalized : Vector3.up);
        }

        void ServerDie(PlayerNet killer, byte cause = 0)
        {
            Dead.Value = true;
            Health.Value = 0;
            // a kill: a big burst of blood out of the body, on every screen
            {
                var d = killer != null && killer != this ? transform.position - killer.transform.position : Vector3.up;
                d.y = Mathf.Max(d.y, 0f);
                Fx.Server(FxKind.BloodKill, BleedPos, d.sqrMagnitude > 0.01f ? d.normalized : Vector3.up);
            }
            // a gravestone where you fell (stays for the rest of the match)
            if (NetGame.Instance != null) NetGame.Instance.ServerAddGrave(transform.position, transform.eulerAngles.y + 180f, Team.Value);
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position + Vector3.up * 1.5f, Vector3.up * 3f);
            InvisUntil.Value = -1;
            ServerDismount();

            // everything spills out of the body
            var items = new List<ItemStack>();
            // a helmet that wasn't shot off drops too, and so does armour with the health it had left
            if (HelmetHp.Value > 0) items.Add(ItemStack.Of(Item.Helmet, 1, 1));
            if (ArmorHp.Value > 0) items.Add(ItemStack.Of(Item.Armor, 1, ArmorHp.Value));
            HelmetHp.Value = 0;
            ArmorHp.Value = 0;
            for (int i = 0; i < Inv.Count; i++)
            {
                if (Inv[i].Empty) continue;
                items.Add(Inv[i]);
                Inv[i] = default;
            }
            for (int i = 0; i < StuckSpears.Value; i++) items.Add(ItemStack.Of(Item.Spear, 1));
            StuckSpears.Value = 0;
            if (items.Count > 0 && NetGame.Instance != null) NetGame.Instance.ServerScatter(items, transform.position + Vector3.up * 1.1f);

            RespawnAt.Value = NetworkManager.ServerTime.Time + Cfg.RespawnTime;
            var victimName = Cfg.TeamName[Team.Value];
            if (NetGame.Instance != null)
            {
                // the kill feed (top right on everyone's screen): who, with what, and who died
                // (with the names as they are now - whoever it was may have left by the time it's read - and whether it
                // was a headshot; the scoreboard's kills and deaths go up too)
                if (cause == 0) cause = killer != null && killer != this ? (byte)killer.HeldItem : KillCause.Died;
                bool byOther = killer != null && killer != this;
                Deaths.Value = (ushort)Mathf.Min(ushort.MaxValue, Deaths.Value + 1);
                if (byOther) killer.Kills.Value = (ushort)Mathf.Min(ushort.MaxValue, killer.Kills.Value + 1);
                NetGame.Instance.KillFeedRpc(byOther ? killer.Team.Value : (byte)255, killer != null ? killer.Slot.Value : (byte)0, Team.Value, Slot.Value, cause,
                    byOther && m_HeadHit, new Unity.Collections.FixedString32Bytes(byOther ? killer.DisplayName : ""), new Unity.Collections.FixedString32Bytes(DisplayName));
                NetGame.Instance.ServerOnPlayerKilled(this, killer);
            }
        }

        /// <summary>Back to life on your bedrock, or (wild) somewhere random in the enemy's half of the map.</summary>
        public void ServerRespawn(bool wild)
        {
            Health.Value = Cfg.MaxHealth;
            Vector3 pos;
            float yaw;
            if (wild) NetGame.WildSpawnPoint(Team.Value, out pos, out yaw);
            else NetGame.SpawnPoint(Team.Value, InWaitingArena, Slot.Value, out pos, out yaw);
            TeleportRpc(pos, yaw);
            Dead.Value = false;
            Fx.Server(FxKind.Spawn, pos, Vector3.up);
            // game option: come back with a random airdrop item
            if (Cfg.RespawnLoot && !InWaitingArena)
            {
                var loot = NetGame.RollAirdropLoot();
                ServerGive(loot.Id, loot.Count, loot.Data);
                Notify($"Respawn loot: {Cfg.ItemName(loot.Id)}");
            }
        }

        [Rpc(SendTo.Server)]
        public void RespawnChoiceRpc(bool wild)
        {
            var g = NetGame.Instance;
            if (!Dead.Value || g == null || NetworkManager.ServerTime.Time < RespawnAt.Value) return;
            if (g.S == GameState.SuddenDeath || g.S == GameState.GameOver) return;
            ServerRespawn(wild && g.S == GameState.BallLive); // dying behind the wall always brings you back home
        }

        public void ServerEnterArena()
        {
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position, Vector3.zero);
            if (StuckSpears.Value > 0) { ServerGive(Item.Spear, StuckSpears.Value); StuckSpears.Value = 0; }
            ServerDismount();
            // a fair duel: full health, empty pockets, all armour and helmets taken off, rocks only
            Health.Value = Cfg.MaxHealth;
            HelmetHp.Value = 0;
            ArmorHp.Value = 0;
            GiantUntil.Value = -1;
            for (int i = 0; i < Inv.Count; i++) Inv[i] = default;
            Dead.Value = false;
            InvisUntil.Value = -1;
            NetGame.SpawnPoint(Team.Value, true, Slot.Value, out var pos, out var yaw);
            TeleportRpc(pos, yaw);
        }

        float m_RegenCarry;

        /// <summary>Standing in your own base heals you slowly (not in sudden death; Builder has no bases).</summary>
        void ServerTickBaseRegen()
        {
            if (Dead.Value || InSuddenDeath || Cfg.Builder || Health.Value >= Cfg.MaxHealth || Cfg.BaseRegen <= 0f) return;
            if (Cfg.BaseTeamAt(transform.position) != Team.Value) { m_RegenCarry = 0f; return; }
            m_RegenCarry += Cfg.BaseRegen * Time.deltaTime;
            if (m_RegenCarry < 1f) return; // (whole HP at a time: fewer network updates)
            float add = Mathf.Floor(m_RegenCarry);
            m_RegenCarry -= add;
            Health.Value = Mathf.Min(Cfg.MaxHealth, Health.Value + add);
        }

        /// <summary>The ball is in your team's machine socket (Builder: planted for your team): gathering pays a bit more. Lying in your base doesn't count.</summary>
        public bool BallBuff
        {
            get
            {
                var b = Ball.Instance;
                if (b == null || !b.IsSpawned || b.IsCarried) return false;
                return b.SocketTeam.Value == Team.Value;
            }
        }

        void Notify(string msg) => NotifyRpc(new FixedString128Bytes(msg.Length > 120 ? msg.Substring(0, 120) : msg));

        // ---------------- combat ----------------

        /// <summary>While the glass wall is up nobody can hurt anyone on the other side of it (hitboxes poke through the glass).</summary>
        public static bool GlassBetween(Vector3 a, Vector3 b) => MapBuilder.GlassUp && Cfg.RegionOf(a) != Cfg.RegionOf(b);

        /// <summary>headDamage: a weapon's own headshot number (sword, guns); otherwise a headshot does the usual x2.</summary>
        void ServerHitPlayer(PlayerNet p, float baseDamage, Vector3 point, Vector3 dir, float headDamage = -1f, byte cause = 0)
        {
            if (GlassBetween(transform.position, p.transform.position)) return;
            bool head = p.IsHeadshot(point);
            float dmg = head ? (headDamage >= 0f ? headDamage : baseDamage * Cfg.HeadshotMul) : baseDamage;
            if (head && p.HelmetHp.Value > 0)
            {
                // the headshot helmet: it breaks on the first headshot, and that shot does no damage at all
                p.HelmetHp.Value = 0;
                Fx.Server(FxKind.HelmetBreak, p.EyePos, Vector3.up);
                p.Notify("Your helmet stopped a headshot and broke!");
                Notify("Their helmet stopped your headshot!");
                return;
            }
            p.ServerMarkHead(head);
            p.ServerDamage(dmg, this, cause);
            p.ServerBleed(head, point, dir, OwnerClientId);
            if (p.Dead.Value) KillConfirmRpc();
        }

        [Rpc(SendTo.Server)]
        public void MeleeRpc(bool hasTarget, NetworkObjectReference target, Vector3 point, bool weak)
        {
            if (Dead.Value || CarryingBall) return;
            var item = HeldItem;
            if (!Cfg.IsMelee(item)) return;
            if (InSuddenDeath && item != Item.Rock) return;
            var st = Cfg.Melee(item);
            if (Time.time < m_NextMelee) return;
            m_NextMelee = Time.time + st.Cooldown * 0.85f; // small tolerance for latency jitter
            SwingRpc();
            Reveal();

            if (!hasTarget || !target.TryGet(out var no)) return;
            if (item == Item.Chainsaw || item == Item.TreeCracker) WearChainsaw();
            if (Vector3.Distance(EyePos, point) > st.Range + 2f) return;
            var dir = (point - EyePos).normalized;

            if (no.TryGetComponent(out PlayerNet p))
            {
                if (p == this || p.Dead.Value || !GameAllowsCombat) return;
                ServerHitPlayer(p, st.PlayerDamage, point, dir, item == Item.Sword ? Cfg.SwordHeadDamage : -1f);
            }
            else if (no.TryGetComponent(out Vehicle v))
            {
                v.ServerDamage(st.PlayerDamage * v.HeadMul(point), this);
                if (!v.IsHorse && !v.IsDummy) Fx.Server(FxKind.Blood, point, dir, OwnerClientId); // (a training dummy doesn't bleed)
            }
            else if (no.TryGetComponent(out ResourceNode n))
            {
                if (n.IsBush) return;
                weak = weak && n.IsWeakSpotHit(point, 0.8f);
                bool tree = n.IsWood; // (a tree or a fallen log)
                // your team's ball in your base: everything you gather pays a bit more (DNA too)
                int got = n.ServerHarvest(Mathf.RoundToInt((tree ? st.WoodGather : st.StoneGather) * (BallBuff ? Cfg.BallGatherMul : 1f)), weak, transform.position);
                if (got > 0) ServerGive(Cfg.GatherItem(n.Yield), Cfg.GatherCount(n.Yield, got)); // DNA mode: DNA instead
                if (got > 0 && n.Kind.Value == ResourceNode.Tree && n.Amount.Value <= 0 && Cfg.TreeFellBonus > 0)
                {
                    // felling the whole tree pays out a bonus
                    ServerGive(Cfg.GatherItem(Item.Wood), Cfg.TreeFellBonus);
                    TimberRpc(Cfg.TreeFellBonus);
                    Fx.Server(FxKind.Timber, n.transform.position + Vector3.up * 2f, Vector3.up);
                }
                Fx.Server(tree ? FxKind.WoodChips : FxKind.StoneChips, point, -dir, OwnerClientId);
                if (weak) Fx.Server(tree ? FxKind.WeakSpotTree : FxKind.WeakSpot, point, -dir, OwnerClientId);
            }
            else if (no.TryGetComponent(out Structure s))
            {
                if (!GameAllowsCombat) return; // your own pieces too: anything an enemy can break, you can
                float dmg = st.StructureDamage * Cfg.TierMeleeMul(s.Tier.Value);
                s.ServerDamageAt(dmg, point); // (on a door leaf: the door's own, lower health)
                Fx.Server(FxKind.StructureHit, point, -dir, OwnerClientId);
            }
            else if (no.TryGetComponent(out Container c))
            {
                if (!c.Breakable || !GameAllowsCombat) return;
                c.ServerDamage(st.StructureDamage);
                Fx.Server(FxKind.StructureHit, point, -dir, OwnerClientId);
            }
        }

        /// <summary>The chainsaw wears out a little with every hit and breaks when it runs out.</summary>
        void WearChainsaw()
        {
            var st = HeldStack;
            if (st.Id != Item.Chainsaw && st.Id != Item.TreeCracker) return;
            int left = st.Data - 1;
            if (left <= 0)
            {
                ServerClearSlot(HeldSlot.Value);
                Notify($"Your {Cfg.ItemName(st.Id).ToLower()} broke!");
                Fx.Server(FxKind.Break, transform.position + Vector3.up * 1.2f, Vector3.up);
            }
            else Inv[HeldSlot.Value] = ItemStack.Of(st.Id, 1, left);
        }

        [Rpc(SendTo.Server)]
        public void FireArrowRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Bow) return;
            if (Time.time < m_NextShot || !InvOps.Remove(Inv, Item.Arrow, 1)) return;
            m_NextShot = Time.time + 0.25f;
            Reveal();
            float power = Mathf.Clamp01(velocity.magnitude / Cfg.ArrowSpeed);
            m_PendingArrows.Enqueue(Cfg.BowDamage(power));
            while (m_PendingArrows.Count > 6) m_PendingArrows.Dequeue();
            ArrowVisualRpc(origin, velocity, false);
        }

        [Rpc(SendTo.Server)]
        public void ArrowHitRpc(NetworkObjectReference target, Vector3 point, Vector3 dir)
        {
            if (m_PendingArrows.Count == 0) return;
            float damage = m_PendingArrows.Dequeue();
            if (target.TryGet(out var tree)) ResourceNode.ServerStruck(tree); // (an arrow in a tree sends its birds up, like a chop)
            if (!target.TryGet(out var no) || !GameAllowsCombat) return;
            if (no.TryGetComponent(out PlayerNet p))
            {
                if (p == this || p.Dead.Value) return;
                ServerHitPlayer(p, damage, point, dir);
                return; // arrows that hit someone are gone
            }
            if (no.TryGetComponent(out Vehicle v)) { v.ServerDamage(damage * v.HeadMul(point), this); return; }
            if (no.TryGetComponent(out Structure s) && s.Tier.Value == 0)
                s.ServerDamageAt(Cfg.ArrowWoodStructureDamage, point);
            DropSpentArrow(point, dir);
        }

        /// <summary>An arrow hit the ground or something static: it stays there and can be picked back up (E).</summary>
        [Rpc(SendTo.Server)]
        public void ArrowLandRpc(Vector3 point, Vector3 dir)
        {
            if (m_PendingArrows.Count == 0) return;
            m_PendingArrows.Dequeue();
            DropSpentArrow(point, dir);
        }

        void DropSpentArrow(Vector3 point, Vector3 dir)
        {
            if (NetGame.Instance == null || Vector3.Distance(point, transform.position) > 250f) return;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            NetGame.Instance.ServerDropItem(ItemStack.Of(Item.Arrow, 1), point + dir.normalized * 0.08f, dir, point, true);
        }

        // ---------------- spear throwing ----------------

        [Rpc(SendTo.Server)]
        public void ThrowSpearRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Spear) return;
            if (Time.time < m_NextThrow) return;
            m_NextThrow = Time.time + 0.5f;
            Reveal();
            ServerConsumeHeld();
            m_PendingSpears.Enqueue(Mathf.Clamp01(velocity.magnitude / Cfg.SpearThrowSpeed));
            while (m_PendingSpears.Count > 6) m_PendingSpears.Dequeue();
            SpearVisualRpc(origin, velocity);
        }

        /// <summary>Reported by the thrower's client when its spear hits something (client-side hit detection, like arrows).</summary>
        [Rpc(SendTo.Server)]
        public void SpearLandRpc(bool hasTarget, NetworkObjectReference target, Vector3 point, Vector3 dir)
        {
            if (m_PendingSpears.Count == 0) return;
            float power = m_PendingSpears.Dequeue();
            var game = NetGame.Instance;
            if (game == null) return;
            if (Vector3.Distance(point, transform.position) > 250f) point = transform.position + Vector3.up; // nonsense report
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            if (hasTarget && target.TryGet(out var tree)) ResourceNode.ServerStruck(tree); // (a spear in a tree sends its birds up)

            if (hasTarget && target.TryGet(out var no) && GameAllowsCombat)
            {
                if (no.TryGetComponent(out PlayerNet p) && p != this && !p.Dead.Value)
                {
                    p.StuckSpears.Value++; // stuck first so it drops into the bag if this kills them
                    ServerHitPlayer(p, Cfg.SpearThrowDamage * power, point, dir, -1f, (byte)Item.Spear);
                    if (!p.Dead.Value) p.Notify("A spear is stuck in you! Press E to pull it out");
                    return;
                }
                if (no.TryGetComponent(out Structure s) && s.Tier.Value == 0)
                    s.ServerDamageAt(Cfg.SpearThrowStructureDamage, point);
                if (no.TryGetComponent(out Vehicle v)) v.ServerDamage(Cfg.SpearThrowDamage * power * v.HeadMul(point), this);
            }
            game.ServerDropItem(ItemStack.Of(Item.Spear, 1), point, dir, point, true);
        }

        /// <summary>E on an item lying in the world.</summary>
        [Rpc(SendTo.Server)]
        public void PickupItemRpc(int id)
        {
            var g = NetGame.Instance;
            if (Dead.Value || g == null) return;
            DroppedItem it = default;
            bool found = false;
            foreach (var x in g.Items) if (x.Id == id) { it = x; found = true; break; }
            if (!found) return;
            int space = InvOps.Space(Inv, it.Stack.Id, it.Stack.Data);
            if (space <= 0) { Notify("Inventory full!"); return; }
            var taken = g.ServerTakeItem(id, EyePos, Cfg.InteractRange + 2.5f, space);
            if (!taken.Empty) { ServerGive(taken.Id, taken.Count, taken.Data); PickedUpRpc(); }
        }

        /// <summary>Dragged out of the inventory (or an open chest) onto the ground: thrown in front of you.</summary>
        [Rpc(SendTo.Server)]
        public void DropItemRpc(byte kind, byte idx, ushort amount, NetworkObjectReference containerRef)
        {
            if (Dead.Value || NetGame.Instance == null) return;
            NetworkList<ItemStack> list = Inv;
            if (kind == 1)
            {
                if (!containerRef.TryGet(out var no) || !no.TryGetComponent(out Container c) || !c.InReach(EyePos)) return;
                list = c.Slots;
            }
            if (idx >= list.Count) return;
            var st = list[idx];
            if (st.Empty) return;
            int n = Mathf.Clamp(amount, 1, st.Count);
            list[idx] = st.WithCount(st.Count - n);
            var fwd = Quaternion.Euler(0, transform.eulerAngles.y, 0) * Vector3.forward;
            NetGame.Instance.ServerDropItem(st.WithCount(n), transform.position + fwd * 1.3f + Vector3.up * 0.6f, fwd, EyePos - Vector3.up * 0.3f + fwd * 0.4f);
        }

        /// <summary>Pull a stuck spear out of a player (yourself or someone else) and keep it.</summary>
        [Rpc(SendTo.Server)]
        public void PullSpearRpc(NetworkObjectReference target)
        {
            if (Dead.Value || !target.TryGet(out var no) || !no.TryGetComponent(out PlayerNet p)) return;
            if (p.Dead.Value || p.StuckSpears.Value <= 0) return;
            if (p != this && Vector3.Distance(p.transform.position, transform.position) > Cfg.InteractRange + 1.5f) return;
            if (InvOps.Space(Inv, Item.Spear) < 1) { Notify("Inventory full!"); return; }
            p.StuckSpears.Value--;
            ServerGive(Item.Spear, 1);
            if (p != this) p.Notify($"{DisplayName} pulled a spear out of you");
        }

        // ---------------- battering ram ----------------

        /// <summary>
        /// The ram through a high external wall also smashes the ones stacked right behind it (in line with the hit, not
        /// the ones beside it) - up to 5 in all, so a row of spammed walls doesn't stop a ram. Returns how many went.
        /// </summary>
        int ServerRamBarrierChain(Structure first, Vector3 point)
        {
            var dir = first.transform.position - EyePos;
            dir.y = 0;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
            var cur = first;
            int n = 0;
            while (cur != null && n < 5)
            {
                var at = cur.transform.position;
                cur.ServerDamage(cur.Health.Value + 1f);
                n++;
                Structure next = null;
                float best = float.MaxValue;
                foreach (var s in Structure.All)
                {
                    if (s == null || !s.IsSpawned || s.PType != PieceType.Barrier || s.Team.Value != first.Team.Value) continue;
                    var d = s.transform.position - at;
                    d.y = 0;
                    float along = Vector3.Dot(d, dir);
                    float side = (d - dir * along).magnitude;
                    if (along < 0.05f || along > 2.5f || side > 1.2f || along >= best) continue;
                    best = along;
                    next = s;
                }
                cur = next;
            }
            return n;
        }

        /// <summary>Hand-held ram strike after a full wind-up: destroys a wooden piece / chest, knocks a stone piece down to wood.</summary>
        [Rpc(SendTo.Server)]
        public void RamStrikeRpc(NetworkObjectReference target, Vector3 point)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Ram || !GameAllowsCombat) return;
            if (Time.time < m_NextRam) return;
            m_NextRam = Time.time + Cfg.RamWindup * 0.85f;
            SwingRpc();
            Reveal();
            if (!target.TryGet(out var no)) return;
            if (Vector3.Distance(EyePos, point) > Cfg.RamRange + 2f) return;

            string msg;
            if (no.TryGetComponent(out Structure s))
            {
                // (your own pieces too)
                bool hard = s.Tier.Value >= 1 && s.PType != PieceType.Barrier; // the high external wall always goes in one hit
                msg = hard ? $"Smashed the {s.DisplayName} down to {Cfg.TierName(s.Tier.Value - 1).ToLower()}" : $"Smashed the {s.DisplayName}!";
                if (s.PType == PieceType.Barrier)
                {
                    int chain = ServerRamBarrierChain(s, point);
                    if (chain > 1) msg = $"Smashed {chain} high external walls in a row!";
                }
                else if (hard) s.ServerDowngrade();
                else s.ServerDamage(s.Health.Value + 1f);
            }
            else if (no.TryGetComponent(out Container c) && c.Breakable)
            {
                msg = "Smashed the chest open!";
                c.ServerBreak();
            }
            else return;

            var ram = HeldStack;
            int left = ram.Data - 1;
            if (left <= 0) ServerClearSlot(HeldSlot.Value);
            else Inv[HeldSlot.Value] = ItemStack.Of(Item.Ram, 1, left);
            Fx.Server(FxKind.Smash, point, (EyePos - point).normalized, OwnerClientId);
            Notify(msg + (left > 0 ? $"  ({left} ram hits left)" : "  - your ram broke"));
        }

        // ---------------- crafting ----------------

        [Rpc(SendTo.Server)]
        public void CraftRpc(int recipe)
        {
            bool power = recipe >= Cfg.PowerBase;
            if (Dead.Value || recipe < 0 || (!power && recipe >= Cfg.RecipeCount) || (power && recipe - Cfg.PowerBase >= Cfg.PowerCount)) return;
            if (InSuddenDeath || (NetGame.Instance != null && NetGame.Instance.S == GameState.GameOver)) return;
            var r = power ? Cfg.GetPowerRecipe(recipe - Cfg.PowerBase, Team.Value) : Cfg.GetRecipe(recipe);
            if (!Tutorial.AllowsItemFor(this, r.Output)) { Notify("Not yet - the tutorial gets to that soon"); return; }
            // the Workbench T1 is locked for everyone until a team (any team) has captured the ball (NetGame.Bench.cs)
            if (r.Output == Item.Workbench && !Cfg.BenchUnlocked(Team.Value))
            {
                Notify("You can only craft the Trade Station once the ball has been captured");
                return;
            }
            // tier 1 / 2 items need your team's workbench of that tier, and you in your base (Builder: anywhere)
            int tier = Cfg.CraftTier(r.Output);
            if (tier > 0)
            {
                if (Cfg.BenchTier(Team.Value) < tier) { Notify($"The {Cfg.ItemName(r.Output)} needs a {(tier == 2 ? "Trade Station 2" : "Trade Station")} in your base"); return; }
                if (!Cfg.CanCraftAt(Team.Value, transform.position)) { Notify($"{r.Name} can only be crafted inside your own base"); return; }
            }
            if (Workbench.IsBench(r.Output) && Workbench.ForTeam(Team.Value, Workbench.TierOfItem(r.Output)) != null) { Notify($"Your team already has a {Cfg.ItemName(r.Output)}"); return; }
            if (Cfg.Builder || power) { ServerCraftModes(r); return; } // (power items: armour-style ones just happen - PlayerNet.Modes.cs)
            if (!Cfg.CanCraftAt(Team.Value, transform.position, r.Output)) { Notify($"{r.Name} can only be crafted inside your own base"); return; }
            if (r.Output == Item.Armor && ArmorHp.Value >= Cfg.ArmorHp) { Notify("You're already wearing full armour"); return; }
            if (!CanAfford(r)) { Notify($"Not enough resources for {r.Name}"); return; }
            int data = r.Output == Item.Saddle ? Team.Value + 1 : Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255); // saddles are in your team colour
            if (r.Output == Item.Helmet && HelmetHp.Value > 0) { Notify("You're already wearing a helmet"); return; }
            bool wear = r.Output == Item.Armor || r.Output == Item.Helmet; // armour and the alien helmet go straight on
            if (!wear && InvOps.Space(Inv, r.Output, data) < r.Count && !InvOps.HasEmpty(Inv)) { Notify("Inventory full!"); return; }

            ServerPay(r);
            if (r.Output == Item.Helmet)
            {
                HelmetHp.Value = 1;
                Notify("Helmet on: it stops one headshot completely");
            }
            else if (wear)
            {
                ArmorHp.Value = (byte)Mathf.Clamp(Cfg.ArmorHp, 1, 255);
                Notify($"Armour on: {ArmorHp.Value} extra health, used up before your own");
            }
            else ServerGive(r.Output, r.Count, data);
            CraftedRpc((byte)r.Output);
            Fx.Server(FxKind.Craft, Cfg.MachinePos(Team.Value), new Vector3(Team.Value, 0, 0));
        }

        /// <summary>Crafting works anywhere inside your own base.</summary>
        public bool CanCraftHere => Cfg.CanCraftAt(Team.Value, transform.position);

        // ---------------- building ----------------

        [Rpc(SendTo.Server)]
        public void PlaceRpc(byte type, int i, int j, int l, int d)
        {
            var t = (PieceType)type;
            if (!Cfg.IsGridPiece(t) || type > (byte)PieceType.Tower) return;
            if (Dead.Value || CarryingBall || HeldItem != Item.BuildingPlan || InSuddenDeath) return;
            if (!Tutorial.AllowsFor(this, TutFeature.Build)) return;
            if (Time.time < m_NextBuild) return;

            var key = new PieceKey(BuildGrid.KindOf(t), i, j, l, t == PieceType.Stairs ? (d & 3) : (d & 1));
            if (key.L < 0 || key.L > Cfg.MaxLevel) return;
            string problem = ServerPlaceProblem(t, key, false);
            if (problem != null) { Notify(problem); return; }
            int cost = Cfg.PieceWood(t);
            if (Count(Cfg.CurrencyItem) < cost) { Notify($"Need {cost} {Cfg.CurrencyName}"); return; }
            ServerBuildPiece(t, key);
        }

        /// <summary>
        /// A wall, doorway or window aimed at bare ground in your base, where the missing foundation is the only thing in
        /// the way: the foundation (in cell fi, fj - one of the two the wall stands between) goes down first and the wall
        /// on it, both paid for. If you can't afford both, or the foundation can't go there, nothing is built.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void PlaceOnNewFoundationRpc(byte type, int i, int j, int d, int fi, int fj)
        {
            var t = (PieceType)type;
            if (BuildGrid.KindOf(t) != PieceKey.KEdge || !Cfg.IsGridPiece(t) || type > (byte)PieceType.Tower) return;
            if (Dead.Value || CarryingBall || HeldItem != Item.BuildingPlan || InSuddenDeath) return;
            if (!Tutorial.AllowsFor(this, TutFeature.Build)) return;
            if (Time.time < m_NextBuild) return;

            var key = new PieceKey(PieceKey.KEdge, i, j, 0, d & 1);
            var fkey = new PieceKey(PieceKey.KFoundation, fi, fj, 0, 0);
            int i2 = key.D == 0 ? i + 1 : i, j2 = key.D == 0 ? j : j + 1;
            if (!((fi == i && fj == j) || (fi == i2 && fj == j2))) return;
            int wallCost = Cfg.PieceWood(t), cost = wallCost;
            // (a foundation got there first - someone else's click, or yours twice: just the wall)
            bool needFoundation = !BuildGrid.IsSupported(key, BuildGrid.Registry.ContainsKey);
            string problem = needFoundation ? ServerPlaceProblem(PieceType.Foundation, fkey, false) : null;
            problem = problem ?? ServerPlaceProblem(t, key, needFoundation);
            if (problem != null) { Notify(problem); return; }
            if (needFoundation) cost += Cfg.PieceWood(PieceType.Foundation);
            if (Count(Cfg.CurrencyItem) < cost)
            {
                Notify(needFoundation ? $"Need {cost} {Cfg.CurrencyName} for the foundation and the {Cfg.PieceName(t).ToLower()}" : $"Need {cost} {Cfg.CurrencyName}");
                return;
            }
            if (needFoundation) { ServerBuildPiece(PieceType.Foundation, fkey); AutoFoundations++; }
            ServerBuildPiece(t, key);
        }

        /// <summary>Server, for the tests: foundations put down automatically under a wall aimed at bare ground.</summary>
        public static int AutoFoundations;

        /// <summary>Server: why this player can't build piece t at key (null = they can; the price isn't looked at here).
        /// unsupportedOk: don't mind that nothing holds it up yet (its foundation is about to be built).</summary>
        string ServerPlaceProblem(PieceType t, PieceKey key, bool unsupportedOk)
        {
            if (!BuildGrid.CanBuildAt(Team.Value, key)) return Cfg.Builder ? "You can't build in the enemy base" : "You can only build inside your own base area";
            if (BuildGrid.OnBedrock(key)) return "The bedrock is already a foundation";
            BuildGrid.Pose(t, key, out var pos, out var rot);
            if (Vector3.Distance(pos, transform.position) > Cfg.BuildRange + 4f) return "Too far away";
            if (BuildGrid.IsOccupied(key, BuildGrid.Registry.ContainsKey)) return "Something is already built there";
            // a piece was just destroyed here: you can't slap a new one straight back in
            float rebuild = Structure.RebuildWait(key);
            if (rebuild > 0f) { WallRebuildRefusals++; return Structure.RebuildWaitText(rebuild); }
            if (!unsupportedOk && !BuildGrid.IsSupported(key, BuildGrid.Registry.ContainsKey))
                return t == PieceType.Floor ? "Floors need a wall below or a floor next to them" :
                       t == PieceType.Foundation ? "Foundations go on the ground" : "Needs a foundation or floor underneath";
            if (!AreaClear(t, pos, rot)) return "Placement blocked";
            return null;
        }

        /// <summary>Server: pay for piece t and build it at key (checked with ServerPlaceProblem, and affordable).</summary>
        void ServerBuildPiece(PieceType t, PieceKey key)
        {
            int cost = Cfg.PieceWood(t);
            if (!InvOps.Remove(Inv, Cfg.CurrencyItem, cost)) return;
            SpentRpc((byte)Cfg.CurrencyItem, cost);
            BuildGrid.Pose(t, key, out var pos, out var rot);

            m_NextBuild = Time.time + Cfg.BuildCooldown * 0.85f;
            var go = Instantiate(Bootstrap.I.structurePrefab, pos, rot);
            var s = go.GetComponent<Structure>();
            s.ServerInit(t, Team.Value, key, true);
            BuildGrid.Registry[key] = s;
            go.GetComponent<NetworkObject>().Spawn(true);
            // Fortify All Walls covers what you build afterwards too: new pieces come out at your team's fortify level
            int fort = Cfg.FortifyLevel(Team.Value);
            if (fort > 0 && s.Upgradable) s.ServerUpgrade(fort);
        }

        static bool AreaClear(PieceType t, Vector3 pos, Quaternion rot)
        {
            BuildGrid.LocalBounds(t, out var c, out var size);
            var half = size * 0.5f - Vector3.one * 0.18f;
            half = Vector3.Max(half, Vector3.one * 0.02f);
            var hits = Physics.OverlapBox(pos + rot * c, half, rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<GroundMarker>() != null) continue;
                if (h.GetComponentInParent<PlayerNet>() != null) continue; // you can build right under yourself (you get lifted out)
                var s = h.GetComponentInParent<Structure>();
                if (s != null && s.HasKey) continue; // grid pieces are handled by the grid registry
                return false;
            }
            return true;
        }

        [Rpc(SendTo.Server)]
        public void UpgradeRpc(NetworkObjectReference target)
        {
            if (Dead.Value || HeldItem != Item.BuildingPlan || InSuddenDeath) return;
            if (Cfg.WoodMode) { Notify("Wood mode: no stone upgrades"); return; }
            if (!Tutorial.AllowsFor(this, TutFeature.Upgrade)) { Notify("Not yet - stone upgrades come after the tutorial"); return; }
            if (Time.time < m_NextUpgrade) return;
            if (!target.TryGet(out var no) || !no.TryGetComponent(out Structure s)) return;
            if (s.Team.Value != Team.Value || !s.HasKey || !s.Upgradable) return;
            if (s.Tier.Value >= 1) { Notify($"Already {Cfg.TierName(s.Tier.Value).ToLower()}"); return; }
            if (Vector3.Distance(s.transform.position, transform.position) > Cfg.BuildRange + 4f) return;
            int cost = Cfg.UpgradeCost(s.PType);
            if (!InvOps.Remove(Inv, Cfg.UpgradeItem, cost)) { Notify($"Need {cost} {Cfg.UpgradeName} to upgrade"); return; }
            SpentRpc((byte)Cfg.UpgradeItem, cost);
            m_NextUpgrade = Time.time + Cfg.UpgradeCooldown * 0.85f;
            s.ServerUpgrade();
        }

        /// <summary>Building plan with Demolish picked on the wheel + LMB: take down one of your own pieces (barriers and chests
        /// too) and get part of the wood back.</summary>
        [Rpc(SendTo.Server)]
        public void DemolishRpc(NetworkObjectReference target)
        {
            if (Dead.Value || HeldItem != Item.BuildingPlan || InSuddenDeath || !target.TryGet(out var no)) return;
            if (Vector3.Distance(no.transform.position, transform.position) > Cfg.BuildRange + 5f) return;
            if (no.TryGetComponent(out Structure s))
            {
                if (s.Team.Value != Team.Value) { Notify("You can only demolish your own buildings"); return; }
                int full = s.PType == PieceType.Barrier ? Cfg.BarrierWood : s.PType == PieceType.Tower ? Cfg.FortTowerWood : Cfg.PieceWood(s.PType);
                int wood = Mathf.FloorToInt(full * Cfg.DemolishRefund);
                int stone = s.Tier.Value >= 1 ? Mathf.FloorToInt(Cfg.UpgradeCost(s.PType) * Cfg.DemolishRefund) : 0;
                Fx.Server(FxKind.Break, s.transform.position + Vector3.up * 1.2f, Vector3.up);
                s.NetworkObject.Despawn(true);
                if (NetGame.Instance != null) NetGame.Instance.ServerCollapseCheck(false); // (your own choice: no rebuild wait)
                if (wood > 0) ServerGive(Cfg.CurrencyItem, wood);
                if (stone > 0) ServerGive(Cfg.WoodMode ? Item.Wood : Cfg.UpgradeItem, stone);
            }
            else if (no.TryGetComponent(out Container c) && c.Breakable)
            {
                if (c.Team.Value != Team.Value) { Notify("You can only demolish your own chests"); return; }
                c.ServerBreak();
                int wood = Mathf.FloorToInt(Cfg.ChestWood * Cfg.DemolishRefund);
                if (wood > 0) ServerGive(Cfg.CurrencyItem, wood);
            }
        }

        /// <summary>Client-side and server-side placement rules for chests and barriers. Returns null if OK, else the reason.</summary>
        public static string DeployProblem(Item kind, int team, Vector3 pos, float yaw)
        {
            int baseTeam = Cfg.BaseTeamAt(pos);
            if (kind == Item.Boat) return ThemeMaps.WaterAt(pos.x, pos.z) ? null : "Boats go on open water"; // THEME MAPS
            if (Workbench.IsBench(kind)) { var wp = Workbench.PlaceProblem(kind, team, pos, yaw); if (wp != null) return wp; }
            if (Cfg.Builder) baseTeam = -1; // Builder: no bases - put it anywhere
            if (kind == Item.Chest && baseTeam != team && !Cfg.Builder) return "Chests go inside your own base";
            // chests may go on the bedrock around the machine, just not on the spawn spot
            if (kind == Item.Chest && !Cfg.Builder && new Vector2(pos.x - Cfg.SpawnPos(team).x, pos.z - Cfg.SpawnPos(team).z).magnitude < 1.3f) return "Keep the spawn spot clear";
            // Auto Wood: the wood machine's spot stays free even before the machine is bought (it lands there later)
            if ((kind == Item.Chest || Workbench.IsBench(kind)) && Cfg.AutoWood && baseTeam == team
                && new Vector2(pos.x - Cfg.WoodMachinePos(team).x, pos.z - Cfg.WoodMachinePos(team).z).magnitude < 1.4f) return "Keep the wood machine's spot clear";
            if (kind != Item.Chest && !Workbench.IsBench(kind) && Cfg.PointBlocked(pos)) return "Not on the bedrock";
            if (kind != Item.Chest && baseTeam >= 0 && baseTeam != team) return "Not in the enemy base";
            var rot = Quaternion.Euler(0, yaw, 0);
            if (kind == Item.Barrier)
            {
                // the whole 4 m wall stays out of the enemy base (not just its middle)...
                if (!Cfg.Builder)
                    for (int e = -1; e <= 1; e += 2)
                    {
                        int bt = Cfg.BaseTeamAt(pos + rot * new Vector3(e * 2.1f, 0, 0));
                        if (bt >= 0 && bt != team) return "Not in the enemy base";
                    }
                // ...and away from the enemy's buildings, in every mode (no walling them in on their own foundations)
                foreach (var s in Structure.All)
                {
                    if (s == null || !s.IsSpawned || s.Team.Value == team || !Cfg.IsGridPiece(s.PType)) continue;
                    var d = s.transform.position - pos;
                    if (Mathf.Abs(d.y) < 8f && new Vector2(d.x, d.z).magnitude < 4.5f) return "Not on the enemy base";
                }
            }
            Vector3 c, half;
            if (kind == Item.Chest) { c = new Vector3(0, 0.36f, 0); half = new Vector3(0.5f, 0.3f, 0.27f); }
            else if (Workbench.IsBench(kind)) { c = new Vector3(0, 0.6f, 0); half = new Vector3(Workbench.HalfX - 0.03f, 0.48f, Workbench.HalfZ - 0.03f); }
            else if (kind == Item.Car) { c = new Vector3(0, 0.8f, 0); half = new Vector3(0.8f, 0.55f, 1.3f); }
            else { c = new Vector3(0, 2.7f, 0); half = new Vector3(1.95f, 2.45f, 0.2f); } // the high external wall
            foreach (var h in Physics.OverlapBox(pos + rot * c, half, rot, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.GetComponentInParent<GroundMarker>() != null || h.GetComponentInParent<PlayerNet>() != null) continue;
                var s = h.GetComponentInParent<Structure>();
                if (s != null && (s.PType == PieceType.Foundation || s.PType == PieceType.Floor)) continue;
                return "Not enough room here";
            }
            return null;
        }

        /// <summary>
        /// Chests and walls go right up against things (your walls, the machine): if the exact spot is a bit too tight,
        /// the nearest free spot within 0.7 m is used instead. Returns false (with the reason) if there's none.
        /// </summary>
        public static bool FindDeploySpot(Item kind, int team, ref Vector3 pos, float yaw, out string problem)
        {
            problem = DeployProblem(kind, team, pos, yaw);
            if (problem == null) return true;
            if (problem != "Not enough room here" || kind == Item.Boat) return false;
            for (float r = 0.15f; r <= 0.71f; r += 0.14f)
                for (int d = 0; d < 8; d++)
                {
                    var p = pos + Quaternion.Euler(0, d * 45f, 0) * Vector3.forward * r;
                    // stand it on whatever is under the nudged spot
                    if (!Physics.Raycast(p + Vector3.up * 0.8f, Vector3.down, out var hit, 1.6f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) continue;
                    p.y = hit.point.y;
                    if (DeployProblem(kind, team, p, yaw) != null) continue;
                    pos = p;
                    problem = null;
                    return true;
                }
            return false;
        }

        [Rpc(SendTo.Server)]
        public void PlaceDeployableRpc(byte kindByte, Vector3 pos, float yaw)
        {
            var kind = (Item)kindByte;
            if (kind != Item.Chest && kind != Item.Barrier && kind != Item.Car && !Workbench.IsBench(kind) && kind != Item.Boat /* THEME MAPS */) return;
            if (Dead.Value || CarryingBall || HeldItem != kind || InSuddenDeath) return;
            if (!Tutorial.AllowsFor(this, TutFeature.Deploy)) return;
            if (Vector3.Distance(pos, transform.position) > Cfg.DeployRange + 3f) return;
            if (!FindDeploySpot(kind, Team.Value, ref pos, yaw, out var problem)) { Notify(problem); return; }
            ServerConsumeHeld();
            var rot = Quaternion.Euler(0, yaw, 0);
            if (kind == Item.Chest)
            {
                var go = Instantiate(Bootstrap.I.containerPrefab, pos, rot);
                go.GetComponent<Container>().ServerInit(Container.Chest, Team.Value, Cfg.ChestSlots, null);
                go.GetComponent<NetworkObject>().Spawn(true);
            }
            else if (Workbench.IsBench(kind)) Workbench.ServerSpawn(Team.Value, pos, yaw, Workbench.TierOfItem(kind));
            else if (kind == Item.Car) Vehicle.ServerSpawn(Vehicle.Car, pos + Vector3.up * 0.1f, yaw);
            else if (kind == Item.Boat) Vehicle.ServerSpawn(Vehicle.Boat, new Vector3(pos.x, ThemeMaps.WaterY - 0.1f, pos.z), yaw); // THEME MAPS
            else
            {
                var go = Instantiate(Bootstrap.I.structurePrefab, pos, rot);
                go.GetComponent<Structure>().ServerInit(PieceType.Barrier, Team.Value, default, false);
                go.GetComponent<NetworkObject>().Spawn(true);
            }
        }

        // ---------------- inventory & containers ----------------

        /// <summary>
        /// Drag & drop between your inventory (kind 0) and an open container (kind 1).
        /// dstIdx 255 = shift-click quick move. Death bags are take-only.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void MoveItemRpc(byte srcKind, byte srcIdx, byte dstKind, byte dstIdx, ushort amount, NetworkObjectReference containerRef)
        {
            if (Dead.Value) return;
            Container c = null;
            if (srcKind == 1 || dstKind == 1)
            {
                if (!containerRef.TryGet(out var no) || !no.TryGetComponent(out c) || !c.InReach(EyePos)) return;
                if (dstKind == 1 && c.TakeOnly) return;
            }
            var src = srcKind == 1 ? c.Slots : Inv;
            if (srcIdx >= src.Count) return;
            if (c != null && c.IsGamble && !GambleMachine.ServerMoveOk(c, src, srcIdx, srcKind == 1 ? Inv : c.Slots, dstIdx)) { Notify("The gambling machine only takes DNA"); return; }
            if (dstIdx == 255)
            {
                if (c == null) return;
                var other = srcKind == 1 ? Inv : c.Slots;
                if (other == c.Slots && c.TakeOnly) return;
                // shift-clicked into a chest: it sorts itself as things go in
                if (InvOps.QuickMove(src, srcIdx, other, other == Inv) && other == c.Slots && !c.IsAirdrop) InvOps.Sort(c.Slots);
                return;
            }
            var dst = dstKind == 1 ? c.Slots : Inv;
            if (dstIdx >= dst.Count) return;
            InvOps.Move(src, srcIdx, dst, dstIdx, amount, srcKind == 1 && c.TakeOnly);
        }

        [Rpc(SendTo.Server)]
        public void PickBerriesRpc(NetworkObjectReference bushRef)
        {
            if (Dead.Value || !bushRef.TryGet(out var no) || !no.TryGetComponent(out ResourceNode n) || !n.IsBush) return;
            if (Vector3.Distance(n.transform.position, transform.position) > Cfg.InteractRange + 2f) return;
            if (InvOps.Space(Inv, Item.Berry) < 1) { Notify("Inventory full!"); return; }
            if (n.TrapTeam.Value != ResourceNode.NoTrap)
            {
                // fake bomb bush!
                var at = n.transform.position + Vector3.up * 0.6f;
                n.NetworkObject.Despawn(true);
                Notify("It was a fake bomb bush!");
                if (NetGame.Instance != null) NetGame.Instance.ServerBlast(at, 3f, -2, Cfg.BombBushDamage * 0.4f, 0f, 0f, null, false);
                ServerDamage(Cfg.BombBushDamage, null, (byte)Item.BombBush); // the one who picked it takes the full blast
                return;
            }
            // you pick the whole bush: it's gone, and a new one grows somewhere else on this side later
            if (n.ServerHarvest(1, false, transform.position) > 0) ServerGive(Item.Berry, 1);
        }

        [Rpc(SendTo.Server)]
        public void EatRpc()
        {
            var food = HeldItem;
            if (Dead.Value || (food != Item.Berry && food != Item.Meat) || Time.time < m_NextEat) return;
            if (Health.Value >= Cfg.MaxHealth) { Notify("You're already at full health"); return; }
            // berries take a while to eat (the client plays it out first)
            m_NextEat = Time.time + Mathf.Max(0.8f, (food == Item.Berry ? Cfg.BerryEatTime : Cfg.MeatEatTime) - 0.2f);
            ServerConsumeHeld();
            Health.Value = food == Item.Meat ? Cfg.MaxHealth : Mathf.Min(Cfg.MaxHealth, Health.Value + Cfg.BerryHeal); // meat heals you fully
        }

        /// <summary>Put on a helmet (the old one goes back in your inventory) or drink an invisibility potion.</summary>
        [Rpc(SendTo.Server)]
        public void UseItemRpc()
        {
            if (Dead.Value || CarryingBall || Time.time < m_NextEat) return;
            var st = HeldStack;
            if (st.Id == Item.Helmet)
            {
                if (HelmetHp.Value > 0) { Notify("You're already wearing a helmet"); return; }
                m_NextEat = Time.time + 0.5f;
                HelmetHp.Value = 1;
                ServerClearSlot(HeldSlot.Value);
                Notify("Helmet on: it stops one headshot completely");
            }
            else if (st.Id == Item.HeavyArmor)
            {
                // heavy armour: a double-strength armour bar; any wooden armour you had on breaks off
                m_NextEat = Time.time + 0.5f;
                bool had = ArmorHp.Value > 0;
                ArmorHp.Value = (byte)Mathf.Clamp(Cfg.HeavyArmorHp, 1, 255);
                ServerClearSlot(HeldSlot.Value);
                Notify($"Heavy armour on: {ArmorHp.Value} extra health" + (had ? " (your old armour broke off)" : ""));
            }
            else if (st.Id == Item.Armor)
            {
                m_NextEat = Time.time + 0.5f;
                int old = ArmorHp.Value;
                ArmorHp.Value = (byte)Mathf.Clamp(st.Data > 0 ? st.Data : Cfg.ArmorHp, 1, 255);
                ServerClearSlot(HeldSlot.Value);
                if (old > 0) ServerGive(Item.Armor, 1, old);
                Notify($"Armour on: {ArmorHp.Value} extra health, used up before your own");
            }
            else if (st.Id == Item.InvisPotion)
            {
                m_NextEat = Time.time + 0.8f;
                ServerConsumeHeld();
                InvisUntil.Value = Now + Cfg.InvisTime;
                RevealUntil.Value = -1;
                Fx.Server(FxKind.Drink, transform.position, Vector3.up, OwnerClientId);
                Notify($"You're invisible for {Cfg.InvisTime:0} seconds (attacking shows you)");
            }
        }

        /// <summary>C4 thrown: lands where the thrower's client reports (like spears), then goes off after the fuse.</summary>
        [Rpc(SendTo.Server)]
        public void ThrowC4Rpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.C4 || Time.time < m_NextThrow) return;
            m_NextThrow = Time.time + 0.5f;
            Reveal();
            ServerConsumeHeld();
            m_PendingC4 = Mathf.Min(m_PendingC4 + 1, 5);
            C4VisualRpc(origin, velocity);
        }

        [Rpc(SendTo.Server)]
        public void C4LandRpc(Vector3 point, Vector3 normal)
        {
            if (m_PendingC4 <= 0 || NetGame.Instance == null) return;
            m_PendingC4--;
            if (Vector3.Distance(point, transform.position) > 200f) point = transform.position + transform.forward;
            NetGame.Instance.ServerArmC4(point, normal, this);
        }

        /// <summary>The thrown C4 hit a player or a horse (any creature / car): it sticks to them, where it hit (`local`
        /// and `localNormal` are in the target's own space, so it rides along wherever they go), and goes off there.</summary>
        [Rpc(SendTo.Server)]
        public void C4StickRpc(NetworkObjectReference target, Vector3 local, Vector3 localNormal)
        {
            if (m_PendingC4 <= 0 || NetGame.Instance == null) return;
            m_PendingC4--;
            if (target.TryGet(out var no) && no != NetworkObject && (no.GetComponent<PlayerNet>() != null || no.GetComponent<Vehicle>() != null)
                && Vector3.Distance(no.transform.position, transform.position) < 200f)
            {
                // (kept on the target: never further out than something stuck to them could be)
                NetGame.Instance.ServerArmC4On(no, Vector3.ClampMagnitude(local, 4f), localNormal, this);
                return;
            }
            NetGame.Instance.ServerArmC4(transform.position + transform.forward, Vector3.up, this);
        }

        /// <summary>Death wand: one bolt along the aim, good for one of three things. Anyone it passes close to, or who is
        /// near where it hits, dies instantly. If it kills nobody and hits a standing tree, the whole tree is felled and
        /// everything in it (and the felling bonus) goes straight into your inventory. If it kills nobody and hits a
        /// building piece (anyone's, any strength - wood to metal), that one piece is destroyed.</summary>
        [Rpc(SendTo.Server)]
        public void WandRpc(Vector3 dir)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.DeathWand || Time.time < m_NextWand || dir.sqrMagnitude < 0.01f) return;
            m_NextWand = Time.time + 0.5f;
            Reveal();
            ServerConsumeHeld();
            dir.Normalize();
            var eye = EyePos;
            float dist = Cfg.WandRange;
            Collider struck = null;
            foreach (var h in Physics.RaycastAll(eye, dir, Cfg.WandRange, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && h.collider.GetComponentInParent<PlayerNet>() == null && h.distance < dist) { dist = h.distance; struck = h.collider; }
            var end = eye + dir * dist;
            Fx.Server(FxKind.WandBeam, eye + dir * 0.6f - Vector3.up * 0.2f, end);
            bool killed = false;
            foreach (var p in All)
            {
                if (p == this || p.Dead.Value) continue;
                var c = p.transform.position + Vector3.up * 1f;
                float t = Mathf.Clamp(Vector3.Dot(c - eye, dir), 0f, dist);
                float off = Vector3.Distance(eye + dir * t, c);
                bool nearImpact = Vector3.Distance(end, c) <= Cfg.WandRadius;
                if (off <= Cfg.WandRadius * 0.5f || nearImpact)
                {
                    p.ServerDamage(99999f, this, (byte)Item.DeathWand);
                    p.ServerBleed(true, c, dir);
                    if (p.Dead.Value) { KillConfirmRpc(); killed = true; }
                }
            }
            // horses (and Slenderman) it passes close to die too
            foreach (var v in Vehicle.All.ToArray())
            {
                if (v == null || !v.IsSpawned || v.IsCar || (Riding && v.NetworkObjectId == RidingId.Value)) continue; // (not the one you're on)
                var c = v.transform.position + Vector3.up * 1f;
                float t = Mathf.Clamp(Vector3.Dot(c - eye, dir), 0f, dist);
                if (Vector3.Distance(eye + dir * t, c) <= Cfg.WandRadius * 0.5f || Vector3.Distance(end, c) <= Cfg.WandRadius) { v.ServerDamage(99999f, this); killed = true; }
            }
            if (!killed && struck != null) ServerWandStrike(struck, end, dir);
        }

        /// <summary>The wand's bolt killed nobody and hit this: a standing tree is felled, all its wood (and the felling
        /// bonus) into your inventory; a building piece is destroyed, whatever it's made of.</summary>
        void ServerWandStrike(Collider struck, Vector3 point, Vector3 dir)
        {
            var n = struck.GetComponentInParent<ResourceNode>();
            if (n != null && n.IsSpawned && n.Kind.Value == ResourceNode.Tree && n.Amount.Value > 0)
            {
                int got = n.ServerHarvest(n.Amount.Value, false, transform.position);
                if (got <= 0) return;
                int wood = Cfg.GatherCount(n.Yield, got) + Mathf.Max(0, Cfg.TreeFellBonus);
                ServerGive(Cfg.GatherItem(n.Yield), wood);
                TimberRpc(wood);
                Fx.Server(FxKind.WoodChips, point, -dir);
                Fx.Server(FxKind.Timber, n.transform.position + Vector3.up * 2f, Vector3.up);
                return;
            }
            var s = struck.GetComponentInParent<Structure>();
            if (s != null && s.IsSpawned && GameAllowsCombat)
            {
                Fx.Server(FxKind.Smash, point, -dir);
                s.ServerDamage(s.Health.Value + 1f);
            }
        }

        // ---------------- interaction ----------------

        [Rpc(SendTo.Server)]
        public void ToggleDoorRpc(NetworkObjectReference target)
        {
            if (Dead.Value || !target.TryGet(out var no) || !no.TryGetComponent(out Structure s)) return;
            if (s.PType != PieceType.Doorway || !s.HasDoor) return; // (a door that was broken off: the doorway just stands open)
            if (s.Team.Value != Team.Value) { Notify("This door is locked"); return; }
            if (Vector3.Distance(s.transform.position, transform.position) > Cfg.InteractRange + 3f) return;
            s.DoorOpen.Value = !s.DoorOpen.Value;
        }

        [Rpc(SendTo.Server)]
        public void PickupBallRpc()
        {
            var b = Ball.Instance;
            if (Dead.Value || b == null || b.IsCarried || InSuddenDeath) return;
            if (NetGame.Instance != null && NetGame.Instance.WallUp) return; // under the glass dome until the wall drops
            if (Vector3.Distance(b.transform.position, EyePos) > Cfg.InteractRange + 2f) return;
            b.ServerPickup(this); // (the kill feed says who picked it up - no top-right message as well)
        }

        /// <summary>Builder: E while carrying the ball plants it on the ground in front of you - it's your team's ball then.</summary>
        [Rpc(SendTo.Server)]
        public void PlantBallRpc(Vector3 ground)
        {
            if (Dead.Value || !CarryingBall || !Cfg.Builder) return;
            if (Vector3.Distance(ground, transform.position) > 4.5f) return;
            // room for the block and the ball
            foreach (var h in Physics.OverlapBox(ground + Vector3.up * (Ball.PlinthH * 0.5f + 0.1f), new Vector3(0.55f, Ball.PlinthH * 0.5f, 0.55f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.GetComponentInParent<GroundMarker>() != null || h.GetComponentInParent<PlayerNet>() != null || h.GetComponentInParent<Ball>() != null) continue;
                Notify("No room to put the ball down there");
                return;
            }
            Ball.Instance.ServerPlant(this, ground);
        }

        /// <summary>E at your own machine while carrying the ball: put it in the socket.</summary>
        [Rpc(SendTo.Server)]
        public void InsertBallRpc()
        {
            if (Dead.Value || !CarryingBall || Cfg.Builder) return;
            if (Vector3.Distance(EyePos, Cfg.SocketPos(Team.Value)) > Cfg.MachineRange + 1.5f) return;
            Ball.Instance.ServerSocket(Team.Value);
        }

        /// <summary>LMB while carrying: the ball flies straight out from where it is held (lower middle of the screen) along the aim.</summary>
        [Rpc(SendTo.Server)]
        public void ThrowBallRpc(Vector3 eye, Vector3 dir, Vector3 runVel)
        {
            if (!CarryingBall || dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            // the thrower's own eye position: the server's copy of a running player lags behind, so a ball placed from
            // it came out behind them (they ran into it and got launched forward). Trust it if it's close to ours.
            if ((eye - EyePos).sqrMagnitude > 5f * 5f) eye = EyePos;
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            if (right.sqrMagnitude < 0.01f) right = transform.right;
            var camUp = Vector3.Cross(dir, right);
            var pos = eye + dir * 1.05f - camUp * 0.3f;
            if (Physics.Linecast(eye, pos, out var hit, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                pos = hit.point - dir * 0.7f;
            // it keeps the thrower's running (or galloping) speed, so it always leaves in front of them
            runVel.y = 0;
            var vel = dir * Cfg.BallThrowSpeed + Vector3.up * 1f;
            Collider mount = null;
            if (Riding && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(RidingId.Value, out var vno) && vno.TryGetComponent(out Vehicle horse))
            {
                mount = horse.GetComponent<CharacterController>();
                vel += Vector3.ClampMagnitude(runVel, Cfg.HorseSprint * 1.3f);
            }
            else vel += Vector3.ClampMagnitude(runVel, Cfg.SprintSpeed * 1.6f);
            Ball.Instance.ServerThrow(this, pos, vel, mount);
            ThrowAnimRpc();
        }

        // =====================================================================
        // Server -> client
        // =====================================================================

        [Rpc(SendTo.Owner)]
        public void TeleportRpc(Vector3 pos, float yaw)
        {
            var pc = GetComponent<PlayerController>();
            if (pc != null) pc.LocalTeleport(pos, yaw);
        }

        [Rpc(SendTo.Owner)]
        public void NotifyRpc(FixedString128Bytes msg) => Hud.Push(msg.ToString());

        [Rpc(SendTo.Owner)]
        public void KillConfirmRpc()
        {
            Hud.HitMarker(true, false);
            Sfx.Play2D(Sfx.Kill, 0.7f, 0f);
        }

        [Rpc(SendTo.Owner)]
        public void PickedUpRpc() => Sfx.Play2D(Sfx.Pop, 0.5f);

        /// <summary>Materials spent on building / crafting: shown in red in the pickup feed.</summary>
        [Rpc(SendTo.Owner)]
        public void SpentRpc(byte item, int amount) => Hud.Loss((Item)item, amount);

        [Rpc(SendTo.Owner)]
        void TimberRpc(int bonus)
        {
            Sfx.Play2D(Sfx.Smash, 0.8f);
        }

        [Rpc(SendTo.Owner)]
        public void CraftedRpc(byte item)
        {
            Hud.Push("Crafted " + Cfg.ItemName((Item)item));
            Sfx.Play2D(Sfx.Pop, 0.5f);
        }

        /// <summary>Everyone else sees this player throw (the ball; spears and C4 do it in their own visual RPCs).</summary>
        [Rpc(SendTo.NotOwner)]
        public void ThrowAnimRpc() => m_Throw = 1f;

        /// <summary>Owner side: the throw shows on your own body too (seen in third person / the tree camo view).</summary>
        public void LocalThrowAnim() => m_Throw = 1f;

        [Rpc(SendTo.NotOwner)]
        public void SwingRpc()
        {
            m_Swing = 1f;
            Sfx.Play(Sfx.Swing, transform.position + Vector3.up * 1.2f, 0.7f, 0.08f, 45f);
        }

        [Rpc(SendTo.NotOwner)]
        public void ArrowVisualRpc(Vector3 origin, Vector3 velocity, bool bolt)
        {
            ArrowProjectile.Spawn(origin, velocity, this, false, bolt ? Cfg.CrossbowDamage : -1f);
            Sfx.Play(Sfx.Twang, origin, 0.9f, 0.05f, 110f);
        }

        [Rpc(SendTo.NotOwner)]
        public void C4VisualRpc(Vector3 origin, Vector3 velocity)
        {
            ArrowProjectile.SpawnC4(origin, velocity, this, false);
            m_Throw = 1f;
            Sfx.Play(Sfx.Throw, origin, 0.9f, 0.08f, 90f);
        }

        [Rpc(SendTo.NotOwner)]
        public void SpearVisualRpc(Vector3 origin, Vector3 velocity)
        {
            ArrowProjectile.SpawnSpear(origin, velocity, this, false);
            m_Throw = 1f;
            Sfx.Play(Sfx.Throw, origin, 1f, 0.05f, 100f);
            Sfx.Play(Sfx.Swing, origin, 0.8f, 0.05f, 60f);
        }
    }
}
