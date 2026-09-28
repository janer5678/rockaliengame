using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Networked player state + all server-side validation of player actions.
    /// Movement is owner-authoritative (NetworkTransform in Owner mode), everything else is server-authoritative.
    /// Inventory: slots 0..6 are the hotbar, 7..27 the main inventory.
    /// </summary>
    public class PlayerNet : NetworkBehaviour
    {
        public static PlayerNet Local;
        public static readonly List<PlayerNet> All = new List<PlayerNet>();
        /// <summary>Hit capsules live on their own layer: hit by raycasts, but they never block movement or physics.</summary>
        public const int HitboxLayer = 30;
        static bool s_LayersSet;

        // ---- server-written state ----
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(Cfg.MaxHealth);
        public readonly NetworkVariable<bool> Dead = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> RespawnAt = new NetworkVariable<double>();
        public readonly NetworkVariable<int> StuckSpears = new NetworkVariable<int>(); // thrown spears stuck in this player
        public readonly NetworkList<ItemStack> Inv = new NetworkList<ItemStack>();

        // ---- owner-written state ----
        public readonly NetworkVariable<byte> HeldSlot = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Pitch = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> Crouch = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public ItemStack SlotAt(int i) => i >= 0 && i < Inv.Count ? Inv[i] : default;
        public ItemStack HeldStack => SlotAt(HeldSlot.Value);
        public Item HeldItem => HeldStack.Id; // None = empty hands (fists)
        public int Count(Item id) => Inv.Count > 0 ? InvOps.Count(Inv, id) : 0;
        public bool CarryingBall => Ball.Instance != null && Ball.Instance.CarrierId.Value == NetworkObjectId;
        public float EyeHeight => Crouch.Value ? Cfg.CrouchEyeHeight : Cfg.EyeHeight;
        public Vector3 EyePos => transform.position + Vector3.up * EyeHeight;
        public bool IsHeadshot(Vector3 point) => point.y > transform.position.y + (Crouch.Value ? 0.88f : 1.24f);

        /// <summary>First hotbar slot holding this item, or -1.</summary>
        public int HotbarSlotOf(Item id)
        {
            for (int i = 0; i < Cfg.HotbarSize; i++) if (SlotAt(i).Id == id) return i;
            return -1;
        }

        // ---- visuals ----
        CharacterController m_CC;
        Transform m_VisualRoot, m_Head, m_Hand;
        GameObject m_HandItem;
        Item m_HandItemId = (Item)255;
        BodyAnimator m_Anim;
        CapsuleCollider m_Hitbox;
        float m_DeadSince = -10f;
        bool m_WasDead;
        readonly List<Renderer> m_TeamRenderers = new List<Renderer>();
        readonly List<Material> m_TeamMats = new List<Material>();
        readonly List<GameObject> m_StuckVisuals = new List<GameObject>();
        float m_Swing, m_CrouchVis;

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
                Team.Value = (byte)(OwnerClientId == NetworkManager.ServerClientId ? 0 : 1);
                Health.Value = Cfg.MaxHealth;
                for (int i = 0; i < Cfg.PlayerSlots; i++) Inv.Add(default);
                ServerGive(Item.Rock, 1);
                Fx.Server(FxKind.Chamber, Cfg.ChamberPos(Team.Value), new Vector3(Team.Value, 0, 0));
            }

            BuildBody();
            Team.OnValueChanged += OnTeamChanged;
            if (IsOwner)
            {
                Local = this;
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
            var bodyTex = Resources.Load<Texture2D>("Alien/Alien_Body");
            var headTex = Resources.Load<Texture2D>("Alien/Alien_Head");
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    bool head = mats[i] != null && mats[i].name.Contains("Head");
                    var m = new Material(Art.Mat(Color.white));
                    var tex = head ? headTex : bodyTex;
                    if (tex != null)
                    {
                        tex.filterMode = FilterMode.Point;
                        m.SetTexture("_BaseMap", tex);
                        m.mainTexture = tex;
                    }
                    if (!head) m_TeamMats.Add(m);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
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
            var c = Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, 1)];
            foreach (var r in m_TeamRenderers) r.sharedMaterial = Art.Mat(c);
            var tint = Color.Lerp(Color.white, c, 0.6f);
            foreach (var m in m_TeamMats)
            {
                m.SetColor("_BaseColor", tint);
                m.color = tint;
            }
        }

        static readonly Vector3[] k_StuckPos = { new Vector3(0.1f, 1.15f, 0.05f), new Vector3(-0.15f, 0.9f, 0.05f), new Vector3(0.05f, 1.35f, -0.05f), new Vector3(-0.05f, 0.7f, 0f) };
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

        void RebuildHandItem(Item item)
        {
            if (m_HandItem) Destroy(m_HandItem);
            m_HandItemId = item;
            m_HandItem = ItemModels.Create(item, m_Hand);
            m_HandItem.transform.localRotation = m_Anim != null ? Quaternion.identity : Quaternion.Euler(20, 0, 0);
            if (item == Item.Rock) m_HandItem.transform.localScale = Vector3.one * 1.3f;
            if (IsOwner) Art.SetLayerShadowsOnly(m_HandItem);
        }

        void Update()
        {
            bool dead = Dead.Value;
            if (dead && !m_WasDead) m_DeadSince = Time.time;
            m_WasDead = dead;
            // the body topples over and stays a moment before disappearing
            bool showBody = !dead || Time.time - m_DeadSince < 2.5f;
            if (m_VisualRoot.gameObject.activeSelf != showBody) m_VisualRoot.gameObject.SetActive(showBody);
            if (m_CC.enabled != !dead) m_CC.enabled = !dead;
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

            var held = CarryingBall || dead ? Item.None : HeldItem;
            if (held != m_HandItemId) RebuildHandItem(held);
            if (m_StuckVisuals.Count != Mathf.Min(StuckSpears.Value, k_StuckPos.Length)) RebuildStuckSpears();
            if (m_Swing > 0) m_Swing = Mathf.Max(0, m_Swing - Time.deltaTime / 0.55f);
            if (m_Anim == null && !IsOwner) m_Head.localRotation = Quaternion.Euler(Pitch.Value * 0.6f, 0, 0);
            if (m_Anim == null && m_Swing > 0) m_Hand.localRotation = Quaternion.Euler(Mathf.Sin(m_Swing * Mathf.PI) * 80f, 0, 0);

            if (IsServer && Dead.Value && NetGame.Instance != null && NetworkManager.ServerTime.Time >= RespawnAt.Value)
            {
                var s = NetGame.Instance.S;
                if (s != GameState.SuddenDeath && s != GameState.GameOver) ServerRespawn();
            }
        }

        void LateUpdate()
        {
            if (m_Anim == null || !m_VisualRoot.gameObject.activeSelf) return;
            var item = m_HandItemId;
            m_Anim.Tick(new BodyAnimator.Pose
            {
                Crouch = Crouch.Value,
                Dead = Dead.Value,
                DeadTime = Time.time - m_DeadSince,
                Carrying = CarryingBall,
                Holding = item != Item.None,
                TwoHanded = item == Item.Rock || item == Item.Spear || item == Item.Ram,
                Pitch = Pitch.Value,
                Swing = m_Swing,
            }, Time.deltaTime);
            m_Anim.GripPose(out var gp, out var gr);
            m_Hand.SetPositionAndRotation(gp, gr);
            if (m_HandItem) m_HandItem.transform.localRotation = Quaternion.identity;
        }

        // =====================================================================
        // Server-side logic
        // =====================================================================

        float m_NextMelee, m_NextShot, m_NextBuild, m_NextUpgrade, m_NextThrow, m_NextRam, m_NextEat, m_NextFullMsg;
        readonly Queue<float> m_PendingArrows = new Queue<float>();
        readonly Queue<float> m_PendingSpears = new Queue<float>();

        bool GameAllowsCombat => NetGame.Instance == null || NetGame.Instance.S != GameState.GameOver;
        bool InSuddenDeath => NetGame.Instance != null && NetGame.Instance.S == GameState.SuddenDeath;

        /// <summary>Server: give items; returns how many didn't fit.</summary>
        public int ServerGive(Item id, int count, int data = 0)
        {
            int left = InvOps.Add(Inv, id, count, data, true);
            if (left > 0 && Time.time >= m_NextFullMsg)
            {
                m_NextFullMsg = Time.time + 2f;
                Notify("Inventory full!");
            }
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

        public void ServerDamage(float dmg, PlayerNet attacker)
        {
            if (Dead.Value || !GameAllowsCombat) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0) ServerDie(attacker);
        }

        void ServerDie(PlayerNet killer)
        {
            Dead.Value = true;
            Health.Value = 0;
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position + Vector3.up * 1.5f, Vector3.up * 3f);

            // everything spills out of the body (except the rock, which you always keep)
            var items = new List<ItemStack>();
            for (int i = 0; i < Inv.Count; i++)
            {
                if (Inv[i].Empty || Inv[i].Id == Item.Rock) continue;
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
                NetGame.Instance.Broadcast(killer != null && killer != this ? $"{Cfg.TeamName[killer.Team.Value]} killed {victimName}" : $"{victimName} died");
                NetGame.Instance.ServerOnPlayerKilled(this, killer);
            }
        }

        public void ServerRespawn()
        {
            Health.Value = Cfg.MaxHealth;
            if (Count(Item.Rock) == 0) ServerGive(Item.Rock, 1);
            NetGame.SpawnPoint(Team.Value, false, out var pos, out var yaw);
            TeleportRpc(pos, yaw);
            Dead.Value = false;
            Fx.Server(FxKind.Chamber, pos, new Vector3(Team.Value, 0, 0));
        }

        public void ServerEnterArena()
        {
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position, Vector3.zero);
            if (StuckSpears.Value > 0) { ServerGive(Item.Spear, StuckSpears.Value); StuckSpears.Value = 0; }
            if (Count(Item.Rock) == 0 && ServerGive(Item.Rock, 1) > 0) Inv[0] = ItemStack.Of(Item.Rock, 1); // rocks only - always have one
            Health.Value = Cfg.MaxHealth;
            Dead.Value = false;
            NetGame.SpawnPoint(Team.Value, true, out var pos, out var yaw);
            TeleportRpc(pos, yaw);
        }

        void Notify(string msg) => NotifyRpc(new FixedString128Bytes(msg.Length > 120 ? msg.Substring(0, 120) : msg));

        // ---------------- combat ----------------

        void ServerHitPlayer(PlayerNet p, float baseDamage, Vector3 point, Vector3 dir)
        {
            bool head = p.IsHeadshot(point);
            p.ServerDamage(baseDamage * (head ? Cfg.HeadshotMul : 1f), this);
            Fx.Server(head ? FxKind.BloodHead : FxKind.Blood, point, dir, OwnerClientId);
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

            if (!hasTarget || !target.TryGet(out var no)) return;
            if (Vector3.Distance(EyePos, point) > st.Range + 2f) return;
            var dir = (point - EyePos).normalized;

            if (no.TryGetComponent(out PlayerNet p))
            {
                if (p == this || p.Dead.Value || !GameAllowsCombat) return;
                ServerHitPlayer(p, st.PlayerDamage, point, dir);
            }
            else if (no.TryGetComponent(out ResourceNode n))
            {
                if (n.IsBush) return;
                weak = weak && n.IsWeakSpotHit(point, 0.8f);
                bool tree = n.Kind.Value == ResourceNode.Tree;
                int got = n.ServerHarvest(Mathf.RoundToInt(tree ? st.WoodGather : st.StoneGather), weak, transform.position);
                if (got > 0) ServerGive(n.Yield, got);
                Fx.Server(tree ? FxKind.WoodChips : FxKind.StoneChips, point, -dir, OwnerClientId);
                if (weak) Fx.Server(FxKind.WeakSpot, point, -dir, OwnerClientId);
            }
            else if (no.TryGetComponent(out Structure s))
            {
                if (s.Team.Value == Team.Value || !GameAllowsCombat) return;
                float dmg = st.StructureDamage * (s.Tier.Value == 1 ? Cfg.StoneStructureMeleeMul : 1f);
                s.ServerDamage(dmg);
                Fx.Server(FxKind.StructureHit, point, -dir, OwnerClientId);
            }
            else if (no.TryGetComponent(out Container c))
            {
                if (c.IsBag || c.Team.Value == Team.Value || !GameAllowsCombat) return;
                c.ServerDamage(st.StructureDamage);
                Fx.Server(FxKind.StructureHit, point, -dir, OwnerClientId);
            }
        }

        [Rpc(SendTo.Server)]
        public void FireArrowRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Bow) return;
            if (Time.time < m_NextShot || !InvOps.Remove(Inv, Item.Arrow, 1)) return;
            m_NextShot = Time.time + 0.3f;
            float power = Mathf.Clamp01(velocity.magnitude / Cfg.ArrowSpeed);
            m_PendingArrows.Enqueue(power);
            while (m_PendingArrows.Count > 6) m_PendingArrows.Dequeue();
            ArrowVisualRpc(origin, velocity);
        }

        [Rpc(SendTo.Server)]
        public void ArrowHitRpc(NetworkObjectReference target, Vector3 point, Vector3 dir)
        {
            if (m_PendingArrows.Count == 0) return;
            float power = m_PendingArrows.Dequeue();
            if (!target.TryGet(out var no) || !GameAllowsCombat) return;
            if (no.TryGetComponent(out PlayerNet p))
            {
                if (p == this || p.Dead.Value) return;
                ServerHitPlayer(p, Cfg.ArrowPlayerDamage * power, point, dir);
            }
            else if (no.TryGetComponent(out Structure s))
            {
                if (s.Team.Value == Team.Value || s.Tier.Value == 1) return;
                s.ServerDamage(Cfg.ArrowWoodStructureDamage);
            }
        }

        // ---------------- spear throwing ----------------

        [Rpc(SendTo.Server)]
        public void ThrowSpearRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Spear) return;
            if (Time.time < m_NextThrow) return;
            m_NextThrow = Time.time + 0.5f;
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

            if (hasTarget && target.TryGet(out var no) && GameAllowsCombat)
            {
                if (no.TryGetComponent(out PlayerNet p) && p != this && !p.Dead.Value)
                {
                    p.StuckSpears.Value++; // stuck first so it drops into the bag if this kills them
                    ServerHitPlayer(p, Cfg.SpearThrowDamage * power, point, dir);
                    if (!p.Dead.Value) p.Notify("A spear is stuck in you! Press E to pull it out");
                    return;
                }
                if (no.TryGetComponent(out Structure s) && s.Team.Value != Team.Value && s.Tier.Value == 0)
                    s.ServerDamage(Cfg.SpearThrowStructureDamage);
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
            if (st.Id == Item.Rock) { Notify("You can't throw your rock away"); return; }
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
            if (p != this) p.Notify($"{Cfg.TeamName[Team.Value]} pulled a spear out of you");
        }

        // ---------------- battering ram ----------------

        /// <summary>Hand-held ram strike after a full wind-up: destroys a wooden piece / chest, knocks a stone piece down to wood.</summary>
        [Rpc(SendTo.Server)]
        public void RamStrikeRpc(NetworkObjectReference target, Vector3 point)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Ram || !GameAllowsCombat) return;
            if (Time.time < m_NextRam) return;
            m_NextRam = Time.time + Cfg.RamWindup * 0.85f;
            SwingRpc();
            if (!target.TryGet(out var no)) return;
            if (Vector3.Distance(EyePos, point) > Cfg.RamRange + 2f) return;

            string msg;
            if (no.TryGetComponent(out Structure s))
            {
                if (s.Team.Value == Team.Value) return;
                bool stone = s.Tier.Value == 1;
                msg = stone ? $"Smashed the {s.DisplayName} down to wood" : $"Smashed the {s.DisplayName}!";
                if (stone) s.ServerDowngrade();
                else s.ServerDamage(s.Health.Value + 1f);
            }
            else if (no.TryGetComponent(out Container c) && !c.IsBag && c.Team.Value != Team.Value)
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
            if (Dead.Value || recipe < 0 || recipe >= Cfg.RecipeCount) return;
            if (InSuddenDeath || (NetGame.Instance != null && NetGame.Instance.S == GameState.GameOver)) return;
            var r = Cfg.GetRecipe(recipe);
            if (Count(Item.Wood) < r.Wood || Count(Item.Stone) < r.Stone) { Notify($"Not enough resources for {r.Name}"); return; }
            int data = r.Output == Item.Ram ? Mathf.Clamp(Cfg.RamUses, 1, 255) : 0;
            if (InvOps.Space(Inv, r.Output, data) < r.Count && !InvOps.HasEmpty(Inv)) { Notify("Inventory full!"); return; }

            InvOps.Remove(Inv, Item.Wood, r.Wood);
            InvOps.Remove(Inv, Item.Stone, r.Stone);
            ServerGive(r.Output, r.Count, data);
            CraftedRpc((byte)r.Output);
        }

        // ---------------- building ----------------

        [Rpc(SendTo.Server)]
        public void PlaceRpc(byte type, int i, int j, int l, int d)
        {
            var t = (PieceType)type;
            if (type > (byte)PieceType.Stairs) return;
            if (Dead.Value || CarryingBall || HeldItem != Item.BuildingPlan || InSuddenDeath) return;
            if (Time.time < m_NextBuild) return;

            var key = new PieceKey(BuildGrid.KindOf(t), i, j, l, t == PieceType.Stairs ? (d & 3) : (d & 1));
            if (key.L < 0 || key.L > Cfg.MaxLevel) return;
            if (!BuildGrid.InTeamBase(Team.Value, key)) { Notify("You can only build inside your own base area"); return; }
            if (BuildGrid.OnUfo(key)) { Notify("You can't build on the crashed UFO or block its door"); return; }
            BuildGrid.Pose(t, key, out var pos, out var rot);
            if (Vector3.Distance(pos, transform.position) > Cfg.BuildRange + 4f) { Notify("Too far away"); return; }
            if (BuildGrid.IsOccupied(key, BuildGrid.Registry.ContainsKey)) { Notify("Something is already built there"); return; }
            if (!BuildGrid.IsSupported(key, BuildGrid.Registry.ContainsKey))
            {
                Notify(t == PieceType.Floor ? "Floors need a wall below or a floor next to them" :
                       t == PieceType.Foundation ? "Foundations go on the ground" : "Needs a foundation or floor underneath");
                return;
            }
            if (!AreaClear(t, pos, rot)) { Notify("Placement blocked"); return; }
            int cost = Cfg.PieceWood(t);
            if (!InvOps.Remove(Inv, Item.Wood, cost)) { Notify($"Need {cost} wood"); return; }

            m_NextBuild = Time.time + Cfg.BuildCooldown * 0.85f;
            var go = Instantiate(Bootstrap.I.structurePrefab, pos, rot);
            var s = go.GetComponent<Structure>();
            s.ServerInit(t, Team.Value, key, true);
            BuildGrid.Registry[key] = s;
            go.GetComponent<NetworkObject>().Spawn(true);
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
            if (Time.time < m_NextUpgrade) return;
            if (!target.TryGet(out var no) || !no.TryGetComponent(out Structure s)) return;
            if (s.Team.Value != Team.Value || !s.HasKey || !s.Upgradable) return;
            if (s.Tier.Value == 1) { Notify("Already stone"); return; }
            if (Vector3.Distance(s.transform.position, transform.position) > Cfg.BuildRange + 4f) return;
            int cost = Cfg.PieceUpgradeStone(s.PType);
            if (!InvOps.Remove(Inv, Item.Stone, cost)) { Notify($"Need {cost} stone to upgrade"); return; }
            m_NextUpgrade = Time.time + Cfg.UpgradeCooldown * 0.85f;
            s.ServerUpgrade();
        }

        /// <summary>Client-side and server-side placement rules for chests and barriers. Returns null if OK, else the reason.</summary>
        public static string DeployProblem(Item kind, int team, Vector3 pos, float yaw)
        {
            int baseTeam = Cfg.BaseTeamAt(pos);
            if (kind == Item.Chest && baseTeam != team) return "Chests go inside your own base";
            if (Cfg.PointBlocked(pos)) return "Not on the crashed UFO or in front of its door";
            if (kind == Item.Barrier && baseTeam >= 0 && baseTeam != team) return "You can't put barriers in the enemy base";
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector3 c, half;
            if (kind == Item.Chest) { c = new Vector3(0, 0.36f, 0); half = new Vector3(0.5f, 0.3f, 0.27f); }
            else { c = new Vector3(0, 0.8f, 0); half = new Vector3(1.15f, 0.65f, 0.15f); }
            foreach (var h in Physics.OverlapBox(pos + rot * c, half, rot, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.GetComponentInParent<GroundMarker>() != null) continue;
                var s = h.GetComponentInParent<Structure>();
                if (s != null && (s.PType == PieceType.Foundation || s.PType == PieceType.Floor)) continue;
                return "Not enough room here";
            }
            return null;
        }

        [Rpc(SendTo.Server)]
        public void PlaceDeployableRpc(byte kindByte, Vector3 pos, float yaw)
        {
            var kind = (Item)kindByte;
            if (kind != Item.Chest && kind != Item.Barrier) return;
            if (Dead.Value || CarryingBall || HeldItem != kind || InSuddenDeath) return;
            if (Vector3.Distance(pos, transform.position) > Cfg.DeployRange + 3f) return;
            var problem = DeployProblem(kind, Team.Value, pos, yaw);
            if (problem != null) { Notify(problem); return; }
            ServerConsumeHeld();
            var rot = Quaternion.Euler(0, yaw, 0);
            if (kind == Item.Chest)
            {
                var go = Instantiate(Bootstrap.I.containerPrefab, pos, rot);
                go.GetComponent<Container>().ServerInit(Container.Chest, Team.Value, Cfg.ChestSlots, null);
                go.GetComponent<NetworkObject>().Spawn(true);
            }
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
            bool rockMoving = src[srcIdx].Id == Item.Rock;
            if (dstIdx == 255)
            {
                if (c == null || rockMoving) return; // the rock stays with you
                var other = srcKind == 1 ? Inv : c.Slots;
                if (other == c.Slots && c.TakeOnly) return;
                InvOps.QuickMove(src, srcIdx, other, other == Inv);
                return;
            }
            var dst = dstKind == 1 ? c.Slots : Inv;
            if (dstIdx >= dst.Count) return;
            // the rock can only live on your hotbar (never in a chest or the main inventory)
            if (rockMoving && !(dst == Inv && dstIdx < Cfg.HotbarSize)) return;
            if (dst[dstIdx].Id == Item.Rock && !(src == Inv && srcIdx < Cfg.HotbarSize)) return;
            InvOps.Move(src, srcIdx, dst, dstIdx, amount, srcKind == 1 && c.TakeOnly);
        }

        [Rpc(SendTo.Server)]
        public void PickBerriesRpc(NetworkObjectReference bushRef)
        {
            if (Dead.Value || !bushRef.TryGet(out var no) || !no.TryGetComponent(out ResourceNode n) || !n.IsBush) return;
            if (Vector3.Distance(n.transform.position, transform.position) > Cfg.InteractRange + 2f) return;
            if (InvOps.Space(Inv, Item.Berry) < 1) { Notify("Inventory full!"); return; }
            int got = n.ServerHarvest(Cfg.BerriesPerPick, false, transform.position);
            if (got > 0) ServerGive(Item.Berry, got);
        }

        [Rpc(SendTo.Server)]
        public void EatRpc()
        {
            if (Dead.Value || HeldItem != Item.Berry || Time.time < m_NextEat) return;
            if (Health.Value >= Cfg.MaxHealth) { Notify("You're already at full health"); return; }
            m_NextEat = Time.time + 0.8f;
            ServerConsumeHeld();
            Health.Value = Mathf.Min(Cfg.MaxHealth, Health.Value + Cfg.BerryHeal);
        }

        // ---------------- interaction ----------------

        [Rpc(SendTo.Server)]
        public void ToggleDoorRpc(NetworkObjectReference target)
        {
            if (Dead.Value || !target.TryGet(out var no) || !no.TryGetComponent(out Structure s)) return;
            if (s.PType != PieceType.Doorway) return;
            if (s.Team.Value != Team.Value) { Notify("This door is locked"); return; }
            if (Vector3.Distance(s.transform.position, transform.position) > Cfg.InteractRange + 3f) return;
            s.DoorOpen.Value = !s.DoorOpen.Value;
        }

        [Rpc(SendTo.Server)]
        public void PickupBallRpc()
        {
            var b = Ball.Instance;
            if (Dead.Value || b == null || b.IsCarried || InSuddenDeath) return;
            if (Vector3.Distance(b.transform.position, EyePos) > Cfg.InteractRange + 2f) return;
            if (b.ServerPickup(this) && NetGame.Instance != null)
                NetGame.Instance.Broadcast($"{Cfg.TeamName[Team.Value]} picked up the ball!");
        }

        /// <summary>LMB while carrying: the ball flies straight out from where it is held (lower middle of the screen) along the aim.</summary>
        [Rpc(SendTo.Server)]
        public void ThrowBallRpc(Vector3 dir)
        {
            if (!CarryingBall || dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            var eye = EyePos;
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            if (right.sqrMagnitude < 0.01f) right = transform.right;
            var camUp = Vector3.Cross(dir, right);
            var pos = eye + dir * 0.75f - camUp * 0.35f;
            if (Physics.Linecast(eye, pos, out var hit, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                pos = hit.point - dir * 0.7f;
            Ball.Instance.ServerThrow(this, pos, dir * Cfg.BallThrowSpeed + Vector3.up * 1f);
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

        [Rpc(SendTo.Owner)]
        public void CraftedRpc(byte item)
        {
            Hud.Push("Crafted " + Cfg.ItemName((Item)item));
            Sfx.Play2D(Sfx.Pop, 0.5f);
        }

        [Rpc(SendTo.NotOwner)]
        public void SwingRpc()
        {
            m_Swing = 1f;
            Sfx.Play(Sfx.Swing, transform.position + Vector3.up * 1.2f, 0.5f);
        }

        [Rpc(SendTo.NotOwner)]
        public void ArrowVisualRpc(Vector3 origin, Vector3 velocity)
        {
            ArrowProjectile.Spawn(origin, velocity, this, false);
            Sfx.Play(Sfx.Twang, origin, 0.6f);
        }

        [Rpc(SendTo.NotOwner)]
        public void SpearVisualRpc(Vector3 origin, Vector3 velocity)
        {
            ArrowProjectile.SpawnSpear(origin, velocity, this, false);
            Sfx.Play(Sfx.Throw, origin, 0.6f);
        }
    }
}
