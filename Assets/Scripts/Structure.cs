using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>A building piece (foundation, wall, doorway, window, floor, stairs), a free-standing barrier or a thrown fort tower.</summary>
    public partial class Structure : NetworkBehaviour
    {
        public static readonly List<Structure> All = new List<Structure>();

        public readonly NetworkVariable<byte> Type = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Tier = new NetworkVariable<byte>();   // 0 wood, 1 stone, 2 metal, 3 refined
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>();
        public readonly NetworkVariable<bool> DoorOpen = new NetworkVariable<bool>();
        /// <summary>A doorway's door leaf has health of its own (Cfg.DoorLeafHp: less than the frame's). At 0 the door is
        /// broken off: the frame stays standing, open to everyone.</summary>
        public readonly NetworkVariable<float> DoorHealth = new NetworkVariable<float>();

        /// <summary>A doorway whose door is still on its hinges.</summary>
        public bool HasDoor => (PType == PieceType.Doorway || PType == PieceType.Gate) && DoorHealth.Value > 0f; // (a large gate's leaf too)
        /// <summary>The large gate's shape: its posts' x, the hinge's x, how many logs wide its leaf is and how tall (m).</summary>
        public const float GatePostX = 2.45f, GateHingeX = -2.2f, GateLeafH = 4.05f;
        public const int GateLeafLogs = 10;
        /// <summary>How far a door swings open (degrees). A doorway's door swings one way; a large gate always swings
        /// outwards - away from its team's base - whichever way round it was put up.</summary>
        public float OpenAngle
        {
            get
            {
                if (PType != PieceType.Gate) return 100f;
                var toBase = Cfg.BaseCenter[Mathf.Clamp(Team.Value, 0, Cfg.BaseCenter.Length - 1)] - transform.position;
                toBase.y = 0f;
                // (+angle swings the leaf to the gate's -z side: that's inwards if the base is that side)
                return Vector3.Dot(-transform.forward, toBase) > 0f ? -100f : 100f;
            }
        }
        /// <summary>A window whose bars are still in (they share the door's health: DoorHealth, Cfg.DoorLeafHp).</summary>
        public bool HasBars => PType == PieceType.Window && DoorHealth.Value > 0f;
        /// <summary>The window bars' object (its one collider covers the opening).</summary>
        public const string BarsName = "bars";
        Transform m_Bars;
        public float DoorMaxHp => PType == PieceType.Gate ? Cfg.BarrierHp : Cfg.DoorLeafHp(Tier.Value);
        /// <summary>For the tests: doors broken off their frames on this screen.</summary>
        public static int DoorsBroken;

        [NonSerialized] public PieceKey Key;
        [NonSerialized] public bool HasKey;

        Transform m_Visual, m_Hinge;
        float m_Rise = 1f, m_DoorAngle;

        public PieceType PType => (PieceType)Type.Value;
        public float MaxHp => Cfg.PieceHp(PType, Tier.Value);
        public string DisplayName => PType == PieceType.Tower ? "Fort Tower" : PType == PieceType.Barrier ? "Large Wall" : PType == PieceType.Gate ? "Large Gate" : PType == PieceType.EggBlock ? "Egg Block" : Cfg.TierName(Tier.Value) + " " + Cfg.PieceName(PType);
        public bool Upgradable => Cfg.IsGridPiece(PType);

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Rebuild();
            m_Rise = 0f;
            Tier.OnValueChanged += OnTierChanged;
            DoorOpen.OnValueChanged += OnDoorChanged;
            DoorHealth.OnValueChanged += OnDoorHealthChanged;
            ClearGrass();
            // placed right where someone stands: pop them out on top instead of trapping them inside
            if (PlayerController.Local != null) PlayerController.Local.ResolveOverlap(transform);
        }

        /// <summary>No grass poking up through what's been built (it grows back when the piece is gone).</summary>
        void ClearGrass() => GrassField.BlockRenderers(GetInstanceID(), transform);

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            GrassField.Unblock(GetInstanceID());
            Tier.OnValueChanged -= OnTierChanged;
            DoorOpen.OnValueChanged -= OnDoorChanged;
            DoorHealth.OnValueChanged -= OnDoorHealthChanged;
            if (IsServer && HasKey && BuildGrid.Registry.TryGetValue(Key, out var s) && s == this)
                BuildGrid.Registry.Remove(Key);
        }

        /// <summary>The tier a door sounds like: its base's current fortify / upgrade tier (or its own, if that's higher) -
        /// wood creaks and knocks, stone grinds and thumps, metal squeals and clangs, refined hisses and locks.</summary>
        public int DoorSoundTier => Mathf.Clamp(Mathf.Max(Tier.Value, Cfg.FortifyLevel(Team.Value)), 0, 3);

        /// <summary>For the tests: door sounds played on this screen, and the last one's tier / open-or-shut.</summary>
        public static int DoorSounds, LastDoorSoundTier = -1;
        public static bool LastDoorSoundOpen;

        void OnDoorChanged(bool was, bool open)
        {
            if (was == open) return;
            int tier = DoorSoundTier;
            DoorSounds++;
            LastDoorSoundTier = tier;
            LastDoorSoundOpen = open;
            var at = transform.position + transform.rotation * new Vector3(0f, 1.2f, 0f);
            Sfx.Play(Sfx.DoorSound(tier, open), at, 0.85f, 0.05f, 45f);
        }

        /// <summary>The door leaf was broken off (every screen): it goes, with a burst of splinters; the frame stays.</summary>
        void OnDoorHealthChanged(float was, float now)
        {
            if (PType == PieceType.Window)
            {
                // the bars were broken out: they go, with a burst; the opening's free to climb through
                if (was > 0f && now <= 0f) { RemoveBars(); Fx.Play(FxKind.Break, transform.position + transform.rotation * new Vector3(0f, 1.65f, 0f), Vector3.up); }
                else if (was <= 0f && now > 0f && m_Bars == null) Rebuild();
                return;
            }
            if (PType != PieceType.Doorway && PType != PieceType.Gate) return;
            if (was > 0f && now <= 0f)
            {
                RemoveDoorLeaf();
                DoorsBroken++;
                Fx.Play(FxKind.Break, transform.position + transform.rotation * new Vector3(0f, 1.2f, 0f), Vector3.up);
            }
            else if (was <= 0f && now > 0f && m_Hinge == null) Rebuild(); // (a door again)
        }

        void RemoveBars()
        {
            if (m_Bars == null) return;
            m_Bars.gameObject.SetActive(false); // (its collider goes now)
            Destroy(m_Bars.gameObject);
            m_Bars = null;
        }

        void RemoveDoorLeaf()
        {
            if (m_Hinge == null) return;
            m_Hinge.gameObject.SetActive(false); // (its collider goes now, not at the end of the frame)
            Destroy(m_Hinge.gameObject);
            m_Hinge = null;
        }

        /// <summary>Whether a hit at this point in the world landed on the door leaf (not the frame round it).</summary>
        public bool IsDoorHit(Vector3 point)
        {
            if (HasBars)
            {
                // the bars fill the window's opening
                var w = transform.InverseTransformPoint(point);
                return Mathf.Abs(w.x) < 0.98f && w.y > 0.88f && w.y < 2.42f && Mathf.Abs(w.z) < 0.2f;
            }
            if (!HasDoor) return false;
            if (PType == PieceType.Gate)
            {
                // a gate's leaf hangs from its hinge, GateLeafLogs logs wide and GateLeafH tall
                var gl = transform.InverseTransformPoint(point) - new Vector3(GateHingeX, 0f, 0f);
                gl = Quaternion.Inverse(Quaternion.Euler(0f, DoorOpen.Value ? OpenAngle : 0f, 0f)) * gl;
                return gl.x > -0.1f && gl.x < GateLeafLogs * 0.43f + 0.1f && gl.y > -0.1f && gl.y < GateLeafH + 0.4f && Mathf.Abs(gl.z) < 0.55f;
            }
            // into the leaf's own space: it hangs from x = -0.6 and swings 100 degrees open
            var l = transform.InverseTransformPoint(point) - new Vector3(-0.6f, 0f, 0f);
            l = Quaternion.Inverse(Quaternion.Euler(0f, DoorOpen.Value ? 100f : 0f, 0f)) * l;
            return l.x > -0.05f && l.x < 1.25f && l.y > -0.1f && l.y < 2.42f && Mathf.Abs(l.z) < 0.12f;
        }

        /// <summary>A collider that's part of a door leaf (it swings: nothing should stick to it in mid-air).</summary>
        public static bool IsDoorLeaf(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (t.name == "hinge") return true;
                if (t.GetComponent<Structure>() != null) return false;
            }
            return false;
        }

        void OnTierChanged(byte prev, byte cur)
        {
            Rebuild();
            m_Rise = 0.4f; // little "rebuild" pop
        }

        void Rebuild()
        {
            if (m_Visual) Destroy(m_Visual.gameObject);
            m_Visual = CreateVisual(PType, Tier.Value, transform, true, null, out m_Hinge, Team.Value).transform;
            if (PType == PieceType.EggBlock)
                foreach (var r in m_Visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Art.Mat(Color.Lerp(Color.white, Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, Cfg.TeamColor.Length - 1)], 0.6f));
            if (m_Hinge != null) ColourLock(m_Hinge, Team.Value);
            m_DoorAngle = DoorOpen.Value ? OpenAngle : 0f;
            m_Bars = PType == PieceType.Window && m_Visual != null ? m_Visual.Find(BarsName) : null;
            if ((PType == PieceType.Doorway || PType == PieceType.Gate) && !HasDoor) RemoveDoorLeaf(); // (its door was broken off)
            if (PType == PieceType.Window && !HasBars) RemoveBars(); // (its bars were broken out)
        }

        /// <summary>A door's padlocks are in the colour of the team it belongs to (the only team that can open it).</summary>
        static void ColourLock(Transform hinge, int team)
        {
            var c = Cfg.TeamColor[Mathf.Clamp(team, 0, Cfg.TeamColor.Length - 1)];
            var band = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 1f);
            foreach (var r in hinge.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "lock body") r.sharedMaterial = Art.Mat(c);
                else if (r.name == "lock band") r.sharedMaterial = Art.Mat(band);
            }
        }

        void Update()
        {
            if (m_Visual && m_Rise < 1f)
            {
                m_Rise = Mathf.Min(1f, m_Rise + Time.deltaTime / (PType == PieceType.Tower ? 0.8f : 0.25f));
                float e = 1f - (1f - m_Rise) * (1f - m_Rise);
                m_Visual.localScale = new Vector3(1f, Mathf.Lerp(0.05f, 1f, e), 1f);
            }
            if (m_Hinge)
            {
                float target = DoorOpen.Value ? OpenAngle : 0f;
                // (a large gate is heavy: it swings slowly, easing in and out)
                if (PType == PieceType.Gate) m_DoorAngle = Mathf.MoveTowards(m_DoorAngle, target, Mathf.Lerp(28f, 85f, Mathf.Sin(Mathf.Clamp01(Mathf.Abs(m_DoorAngle) / 100f) * Mathf.PI)) * Time.deltaTime);
                else m_DoorAngle = Mathf.MoveTowards(m_DoorAngle, target, 300f * Time.deltaTime);
                m_Hinge.localRotation = Quaternion.Euler(0, m_DoorAngle, 0);
            }
        }

        // ---------------- Server ----------------

        public void ServerInit(PieceType t, int team, PieceKey key, bool hasKey)
        {
            Type.Value = (byte)t;
            Team.Value = (byte)team;
            Tier.Value = 0;
            Health.Value = Cfg.PieceHp(t, 0);
            DoorHealth.Value = t == PieceType.Doorway || t == PieceType.Window ? Cfg.DoorLeafHp(0) : t == PieceType.Gate ? Cfg.BarrierHp : 0f; // (a window's bars and a gate's leaf too)
            Key = key;
            HasKey = hasKey;
        }

        public void ServerDamage(float dmg) => ServerDamage(dmg, true);

        /// <summary>A hit that landed at `point`: on a doorway's door leaf it hurts just the door (which breaks off on its
        /// own, long before the frame would); anywhere else, the piece.</summary>
        public void ServerDamageAt(float dmg, Vector3 point)
        {
            if (IsDoorHit(point)) ServerDamageDoor(dmg);
            else ServerDamage(dmg);
        }

        /// <summary>Server: damage to the door leaf alone. At 0 it's broken off and the doorway stands open.</summary>
        public void ServerDamageDoor(float dmg)
        {
            if (!IsServer || !IsSpawned || dmg <= 0 || !(HasDoor || HasBars)) return; // (a window's bars the same way)
            DoorHealth.Value = Mathf.Max(0f, DoorHealth.Value - dmg);
            if (DoorHealth.Value <= 0f) DoorOpen.Value = false;
        }

        /// <summary>Where a piece of a base was destroyed, and when (Time.time) - on the server, and on every client (told by
        /// NetGame.PieceBrokenRpc, so the placement ghost can show it).</summary>
        static readonly Dictionary<PieceKey, float> s_BrokenAt = new Dictionary<PieceKey, float>();

        /// <summary>How long until something can be built again where a piece was just destroyed (0 = now).</summary>
        public static float RebuildWait(PieceKey key)
        {
            if (key.Kind == PieceKey.KStairs)
            {
                // (stairs: whichever way the ones that stood in that cell faced)
                float most = 0f;
                for (int d = 0; d < 4; d++) most = Mathf.Max(most, RebuildWaitAt(new PieceKey(PieceKey.KStairs, key.I, key.J, key.L, d)));
                return most;
            }
            return RebuildWaitAt(key);
        }

        static float RebuildWaitAt(PieceKey key)
        {
            if (!s_BrokenAt.TryGetValue(key, out var at)) return 0f;
            float w = at + Cfg.WallRebuildCooldown - Time.time;
            if (w <= 0f || w > Cfg.WallRebuildCooldown) { s_BrokenAt.Remove(key); return 0f; } // (over, or left from another match)
            return w;
        }

        /// <summary>What the player is told (the ghost's hint, and the server's refusal).</summary>
        public static string RebuildWaitText(float wait) => $"Destroyed here - you can rebuild in this spot in {Mathf.CeilToInt(wait)} s";

        /// <summary>A piece was destroyed at this slot just now (every peer): nothing can be rebuilt there for a while.</summary>
        public static void NoteBroken(PieceKey key) => s_BrokenAt[key] = Time.time;

        /// <summary>Server: this piece is being destroyed (broken down, blown up, or collapsed with what held it up) - its
        /// slot can't be rebuilt in for Cfg.WallRebuildCooldown seconds, and every client is told (PlayerNet.PlaceRpc
        /// refuses; the ghost goes red with the seconds left).</summary>
        public void ServerNoteDestroyed()
        {
            if (!HasKey || Cfg.WallRebuildCooldown <= 0f) return;
            NoteBroken(Key);
            if (NetGame.Instance != null && NetGame.Instance.IsSpawned) NetGame.Instance.PieceBrokenRpc(Key.Kind, Key.I, Key.J, Key.L, Key.D);
        }

        /// <summary>collapse = check whether other pieces lost their support (explosions do it once at the end).</summary>
        public void ServerDamage(float dmg, bool collapse)
        {
            if (!IsServer || !IsSpawned || dmg <= 0) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0)
            {
                // a piece broken down: nobody can build in the same spot again for a while (PlayerNet.PlaceRpc)
                ServerNoteDestroyed();
                ServerBreakLeaning();
                NetworkObject.Despawn(true);
                if (collapse && NetGame.Instance) NetGame.Instance.ServerCollapseCheck();
            }
        }

        /// <summary>Server, as this piece breaks: a ladder leaning on it and a workbench standing on it break with it.</summary>
        void ServerBreakLeaning()
        {
            bool any = false;
            Bounds b = default;
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (any) b.Encapsulate(c.bounds); else { b = c.bounds; any = true; }
            }
            if (!any) return;
            b.Expand(0.6f);
            var gone = new System.Collections.Generic.List<Container>();
            foreach (var c in Container.All)
            {
                if (c == null || !c.IsSpawned) continue;
                var p = c.transform.position;
                if (c.Kind.Value == Container.Ladder)
                {
                    // (a ladder against this wall: its rails touch the wall somewhere up its height)
                    var top = p + Vector3.up * Deployables.LadderHeight;
                    if (b.Contains(p + Vector3.up * 0.5f) || b.Contains((p + top) * 0.5f) || b.Contains(top - Vector3.up * 0.3f)) gone.Add(c);
                }
                else if (c.IsWorkbench && b.Contains(p + Vector3.down * 0.1f) && (PType == PieceType.Floor || PType == PieceType.Foundation)) gone.Add(c);
            }
            foreach (var c in gone) c.ServerBreakWithSupport();
        }

        public void ServerUpgrade(int tier = 1)
        {
            Tier.Value = (byte)tier;
            Health.Value = MaxHp;
            if (HasDoor || HasBars) DoorHealth.Value = DoorMaxHp; // (a door or bars that were broken off stay off)
        }

        /// <summary>Battering ram hit: one tier down at full health (refined to metal, metal to stone, stone to wood).</summary>
        public void ServerDowngrade()
        {
            Tier.Value = (byte)Mathf.Max(0, Tier.Value - 1);
            Health.Value = MaxHp;
            if (HasDoor || HasBars) DoorHealth.Value = DoorMaxHp;
        }

        // ---------------- Visuals (also used for placement ghosts) ----------------

        public static GameObject CreateVisual(PieceType t, int tier, Transform parent, bool colliders, Material ghost, out Transform hinge, int team = -1)
        {
            hinge = null;
            var root = new GameObject("visual");
            root.transform.SetParent(parent, false);
            var tr = root.transform;
            bool stone = tier >= 1;                 // stone and metal share their shapes
            bool metal = tier >= 2;
            bool refined = tier >= 3;               // Rust's armoured: dark plate with brass trim
            bool sheet = metal && !refined;          // Rust's sheet metal: rusty corrugated sheets, nothing like the grey stone
            Color c = refined ? new Color(0.27f, 0.29f, 0.34f) : sheet ? new Color(0.5f, 0.46f, 0.42f) : stone ? Art.Stone : Art.Wood;
            // (the last tier's accent - its trims - is in the team's colour once it's built: brass on a ghost)
            Color trim = refined ? (team >= 0 ? Color.Lerp(Cfg.TeamColor[Mathf.Clamp(team, 0, Cfg.TeamColor.Length - 1)], Color.white, 0.1f) : new Color(0.8f, 0.64f, 0.28f)) : sheet ? new Color(0.42f, 0.24f, 0.13f) : stone ? new Color(0.42f, 0.42f, 0.46f) : Art.DarkWood;
            bool col = colliders;
            // (Settings > Display colours: each tier has its own colour)
            using var tint = ColorSlots.Use(refined ? ColorSlots.BuildRefined : sheet ? ColorSlots.BuildSheetMetal : stone ? ColorSlots.BuildStone : ColorSlots.BuildWood);

            switch (t)
            {
                case PieceType.Foundation:
                    Art.Box(tr, c, new Vector3(0, 0.5f, 0), new Vector3(3, 1, 3), default, col);
                    Art.Box(tr, trim, new Vector3(0, 0.85f, 0), new Vector3(3.02f, 0.12f, 3.02f));
                    Art.Box(tr, trim, new Vector3(0, 0.3f, 0), new Vector3(3.02f, 0.12f, 3.02f));
                    break;
                case PieceType.Wall:
                    Art.Box(tr, c, new Vector3(0, 1.5f, 0), new Vector3(3, 3, 0.3f), default, col);
                    for (int k = 1; k < 4; k++)
                        Art.Box(tr, trim, new Vector3(0, k * 0.75f, 0), new Vector3(2.98f, 0.07f, 0.34f));
                    if (!stone)
                    {
                        Art.Box(tr, trim, new Vector3(-1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                        Art.Box(tr, trim, new Vector3(1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                    }
                    break;
                case PieceType.Doorway:
                {
                    Art.Box(tr, c, new Vector3(-1.05f, 1.5f, 0), new Vector3(0.9f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(1.05f, 1.5f, 0), new Vector3(0.9f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(0, 2.7f, 0), new Vector3(1.2f, 0.6f, 0.3f), default, col);
                    Art.Box(tr, trim, new Vector3(0, 2.42f, 0), new Vector3(1.3f, 0.08f, 0.34f));
                    var h = new GameObject("hinge").transform;
                    h.SetParent(tr, false);
                    h.localPosition = new Vector3(-0.6f, 0, 0);
                    Art.Box(h, stone ? Art.Metal : Art.DarkWood, new Vector3(0.6f, 1.2f, 0), new Vector3(1.18f, 2.38f, 0.12f), default, col);
                    Art.Box(h, Art.Metal, new Vector3(1.0f, 1.1f, 0.1f), new Vector3(0.08f, 0.2f, 0.08f));
                    Art.Box(h, Art.Metal, new Vector3(1.0f, 1.1f, -0.1f), new Vector3(0.08f, 0.2f, 0.08f));
                    hinge = h;
                    break;
                }
                case PieceType.Window:
                {
                    // a wall with a big opening (1.9 m wide, 1.5 m tall, from waist height up), barred: three bars down it
                    // and one across. The bars are one solid part with health of its own, like a door (Cfg.DoorLeafHp): you
                    // can't climb or crouch through until they're broken out, then the opening's free
                    Art.Box(tr, c, new Vector3(-1.225f, 1.5f, 0), new Vector3(0.55f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(1.225f, 1.5f, 0), new Vector3(0.55f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(0, 0.45f, 0), new Vector3(1.9f, 0.9f, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(0, 2.7f, 0), new Vector3(1.9f, 0.6f, 0.3f), default, col);
                    Art.Box(tr, trim, new Vector3(0, 0.92f, 0), new Vector3(2.0f, 0.08f, 0.36f));
                    Art.Box(tr, trim, new Vector3(0, 2.38f, 0), new Vector3(2.0f, 0.08f, 0.36f));
                    var bars = new GameObject(BarsName).transform;
                    bars.SetParent(tr, false);
                    var bc = stone ? Art.Metal : trim;
                    foreach (float x in new[] { -0.45f, 0f, 0.45f })
                        Art.Box(bars, bc, new Vector3(x, 1.65f, 0), new Vector3(0.05f, 1.45f, 0.05f));
                    Art.Box(bars, bc, new Vector3(0f, 1.65f, 0), new Vector3(1.9f, 0.05f, 0.05f));
                    if (col)
                    {
                        // the one collider for all of them: the whole opening (nothing gets through, crouching or not)
                        var box = bars.gameObject.AddComponent<BoxCollider>();
                        box.center = new Vector3(0f, 1.65f, 0f);
                        box.size = new Vector3(1.9f, 1.46f, 0.12f);
                    }
                    if (!stone)
                    {
                        Art.Box(tr, trim, new Vector3(-1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                        Art.Box(tr, trim, new Vector3(1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                    }
                    break;
                }
                case PieceType.Tower:
                    BuildTower(tr, col);
                    break;
                case PieceType.EggBlock:
                    // build egg slab (like Bedwars wool), tinted in the team colour after it's built
                    Art.Box(tr, new Color(0.95f, 0.95f, 0.92f), new Vector3(0, 0.2f, 0), new Vector3(1.15f, 0.4f, 0.8f), default, col).name = "wool";
                    break;
                case PieceType.Floor:
                    Art.Box(tr, c, new Vector3(0, -0.125f, 0), new Vector3(3, 0.25f, 3), default, col);
                    Art.Box(tr, trim, new Vector3(0, -0.2f, 0), new Vector3(3.02f, 0.1f, 0.2f));
                    Art.Box(tr, trim, new Vector3(0, -0.2f, 0), new Vector3(0.2f, 0.1f, 3.02f));
                    break;
                case PieceType.Stairs:
                {
                    float len = Mathf.Sqrt(18f);
                    Art.Box(tr, c, new Vector3(0, 1.5f, 0), new Vector3(2.6f, 0.25f, len), new Vector3(-45, 0, 0), col);
                    Art.Box(tr, trim, new Vector3(-1.35f, 1.5f, 0), new Vector3(0.15f, 0.5f, len), new Vector3(-45, 0, 0));
                    Art.Box(tr, trim, new Vector3(1.35f, 1.5f, 0), new Vector3(0.15f, 0.5f, len), new Vector3(-45, 0, 0));
                    for (int k = 0; k < 6; k++)
                    {
                        float f = (k + 0.5f) / 6f;
                        Art.Box(tr, trim, new Vector3(0, f * 3f + 0.12f, -1.5f + f * 3f), new Vector3(2.5f, 0.06f, 0.1f));
                    }
                    break;
                }
                case PieceType.Gate:
                {
                    // a large gate (like Rust's external gate): two big log posts and a beam over the top, a leaf of
                    // sharpened logs a little narrower and taller than the large wall, on a hinge at the left post - it
                    // swings open slowly like a heavy gate, always outwards (away from its team's base: Structure.OpenAngle;
                    // E, your team only, from a good way off), with its own health; broken off, the gap's open. A big
                    // padlock in the team's colour hangs on each side of it near the free end, clear of the logs
                    float px = GatePostX;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Art.Box(tr, Art.DarkWood, new Vector3(k * px, 2.45f, 0), new Vector3(0.5f, 4.9f, 0.5f), default, col);
                        Art.Part(tr, Art.Cone, Art.Wood * 1.05f, new Vector3(k * px, 4.9f, 0), new Vector3(0.5f, 0.6f, 0.5f));
                    }
                    Art.Box(tr, Art.DarkWood, new Vector3(0, 4.45f, 0), new Vector3(px * 2f + 0.5f, 0.35f, 0.42f), default, col);
                    var gh = new GameObject("hinge").transform;
                    gh.SetParent(tr, false);
                    gh.localPosition = new Vector3(GateHingeX, 0, 0);
                    float leaf = GateLeafLogs * 0.43f;
                    for (int k = 0; k < GateLeafLogs; k++)
                    {
                        float x = 0.215f + k * 0.43f, h = GateLeafH - 0.12f + ((k * 29) % 4) * 0.06f;
                        Art.Box(gh, k % 2 == 0 ? Art.Wood : Art.Wood * 0.9f, new Vector3(x, h * 0.5f + 0.05f, 0), new Vector3(0.43f, h, 0.34f), new Vector3(0, k * 11f, 0), col);
                        Art.Part(gh, Art.Cone, Art.Wood * 1.05f, new Vector3(x, h + 0.05f, 0), new Vector3(0.43f, 0.4f, 0.34f));
                    }
                    for (int k = 0; k < 2; k++) Art.Box(gh, Art.DarkWood, new Vector3(leaf * 0.5f, 0.9f + k * 2.1f, 0.24f), new Vector3(leaf, 0.2f, 0.1f));
                    Art.Box(gh, Art.DarkWood, new Vector3(leaf * 0.5f, 1.95f, 0.26f), new Vector3(0.18f, 2.9f, 0.08f), new Vector3(0, 0, 50f));
                    foreach (float side in new[] { -1f, 1f })
                    {
                        // the hasp plate, the lock's body and its shackle, standing proud of the logs
                        float z = side * 0.42f, lx = leaf - 0.4f;
                        Art.Box(gh, Art.Metal, new Vector3(lx, 1.85f, side * 0.31f), new Vector3(0.5f, 0.16f, 0.06f));
                        Art.Box(gh, Color.white, new Vector3(lx, 1.55f, z), new Vector3(0.42f, 0.46f, 0.14f)).name = "lock body";
                        Art.Box(gh, Color.white, new Vector3(lx, 1.55f, z + side * 0.075f), new Vector3(0.3f, 0.08f, 0.02f)).name = "lock band";
                        Art.Box(gh, Art.Metal, new Vector3(lx - 0.13f, 1.9f, z), new Vector3(0.06f, 0.26f, 0.06f));
                        Art.Box(gh, Art.Metal, new Vector3(lx + 0.13f, 1.9f, z), new Vector3(0.06f, 0.26f, 0.06f));
                        Art.Box(gh, Art.Metal, new Vector3(lx, 2.02f, z), new Vector3(0.32f, 0.06f, 0.06f));
                    }
                    hinge = gh;
                    break;
                }
                case PieceType.Barrier:
                {
                    // a large wall like Rust's high external wall, but lower and wider: a row of big sharpened logs about
                    // 5.5 m wide and 4 m tall, tied together with two cross beams and propped up by braces on the back
                    const int logs = 13;
                    for (int k = 0; k < logs; k++)
                    {
                        float x = -2.52f + k * 0.42f;
                        float h = 3.8f + ((k * 37) % 5) * 0.07f;
                        Art.Box(tr, k % 2 == 0 ? Art.Wood : Art.Wood * 0.9f, new Vector3(x, h * 0.5f, 0), new Vector3(0.44f, h, 0.44f), new Vector3(0, k * 13f, 0));
                        Art.Part(tr, Art.Cone, Art.Wood * 1.05f, new Vector3(x, h, 0), new Vector3(0.44f, 0.5f, 0.44f));
                    }
                    for (int k = 0; k < 2; k++)
                        Art.Box(tr, Art.DarkWood, new Vector3(0, 1.0f + k * 1.9f, 0.26f), new Vector3(5.5f, 0.22f, 0.12f));
                    for (int k = -1; k <= 1; k += 2)
                        Art.Box(tr, Art.DarkWood, new Vector3(k * 1.8f, 1.25f, 0.8f), new Vector3(0.18f, 2.8f, 0.18f), new Vector3(-28f, 0, 0));
                    if (col)
                    {
                        var bc = root.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 2.0f, 0);
                        bc.size = new Vector3(5.5f, 4.0f, 0.46f);
                    }
                    break;
                }
            }

            if (sheet) RustSheets(tr, t, c, trim);
            // (no extra plank seams / mortar lines / rivets merged on any more: the pieces are back to their original detail)

            // Builder: walls built straight on the ground (no foundation) reach down into it, so nothing rolls out underneath
            if (Cfg.Builder && (t == PieceType.Wall || t == PieceType.Doorway || t == PieceType.Window) && parent.position.y < Cfg.BaseY + 0.5f)
                Art.Box(tr, trim, new Vector3(0, -0.6f, 0), new Vector3(3f, 1.2f, 0.3f), default, col);

            if (ghost == null) ApplyPsxLook(root, t, tier, c, trim, hinge);
            // every door wears a padlock (both faces): only the team that built it can open it. (Put on after the PSX
            // look, so it shows on the PSX door model as well.)
            if (hinge != null && t != PieceType.Gate) DoorLock(hinge); // (the gate has its own big padlocks)

            if (ghost != null)
            {
                foreach (var l in root.GetComponentsInChildren<Ladder>()) Destroy(l.gameObject);
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = ghost;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            return root;
        }

        /// <summary>A big padlock under the handle on each face of a door leaf: body, steel shackle and a dark keyhole. (Brass
        /// here - on a placement ghost; a placed door's lock is turned its team's colour: ColourLock.)</summary>
        static void DoorLock(Transform hinge)
        {
            var brass = new Color(0.86f, 0.68f, 0.2f);
            var hole = new Color(0.08f, 0.07f, 0.06f);
            var holder = new GameObject("lock").transform;
            holder.SetParent(hinge, false);
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * 0.105f;
                Art.Box(holder, brass, new Vector3(1.0f, 0.76f, z), new Vector3(0.26f, 0.22f, 0.09f)).name = "lock body";
                Art.Box(holder, brass * 0.8f, new Vector3(1.0f, 0.76f, z), new Vector3(0.28f, 0.05f, 0.095f)).name = "lock band";
                // the shackle: two posts and the bar across the top
                Art.Box(holder, Art.Metal, new Vector3(0.93f, 0.92f, z), new Vector3(0.035f, 0.13f, 0.035f)).name = "lock shackle";
                Art.Box(holder, Art.Metal, new Vector3(1.07f, 0.92f, z), new Vector3(0.035f, 0.13f, 0.035f)).name = "lock shackle";
                Art.Box(holder, Art.Metal, new Vector3(1.0f, 0.985f, z), new Vector3(0.175f, 0.035f, 0.035f)).name = "lock shackle";
                // keyhole
                Art.Box(holder, hole, new Vector3(1.0f, 0.78f, z + side * 0.046f), new Vector3(0.05f, 0.05f, 0.01f)).name = "lock keyhole";
                Art.Box(holder, hole, new Vector3(1.0f, 0.73f, z + side * 0.046f), new Vector3(0.025f, 0.07f, 0.01f)).name = "lock keyhole";
            }
        }

        /// <summary>
        /// PSX graphics: the log wall and the door are the PSX models; everything else wears the PSX surfaces - planks for
        /// wood, bricks for stone, rusty corrugated sheets for sheet metal, diamond plate for refined.
        /// </summary>
        static void ApplyPsxLook(GameObject root, PieceType t, int tier, Color main, Color trim, Transform hinge)
        {
            if (t == PieceType.Barrier) { PsxModels.Replace(root.transform, "barrier", PsxModels.Fit.Stretch); return; }
            if (t == PieceType.EggBlock) return;
            if (hinge != null) PsxModels.Replace(hinge, "door", PsxModels.Fit.Stretch);
            string body = tier >= 3 ? "metal_12" : tier == 2 ? "metal_21" : tier == 1 ? "cobble_21" : "wood_11"; // (warm brown planks, like the Normal wood)
            string edge = tier >= 3 ? "metal_10" : tier == 2 ? "metal_01" : tier == 1 ? "cobble_20" : "wood_20";
            var rs = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if ((hinge == null || !r.transform.IsChildOf(hinge)) && r.GetComponentInParent<Ladder>() == null) rs.Add(r);
            PsxModels.Retexture(root, rs, r =>
            {
                if (!Art.IsArtMat(r.sharedMaterial, out var col)) return null;
                if (tier == 2 && r.transform.localScale.z < 0.035f && r.transform.localScale.x < 0.12f) return null; // (the sheet's own ridges and bolts)
                return Near(col, trim) ? edge : body;
            }, 1.5f);
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.06f;

        /// <summary>Sheet metal: corrugated ridges on the walls and foundations, and rust patches and bolts on every piece.</summary>
        static void RustSheets(Transform tr, PieceType t, Color c, Color trim)
        {
            var light = new Color(0.64f, 0.62f, 0.58f);   // weathered galvanised steel
            var rust = new Color(0.6f, 0.28f, 0.1f);
            var steel = new Color(0.5f, 0.48f, 0.45f);
            bool wallish = t == PieceType.Wall || t == PieceType.Doorway || t == PieceType.Window;
            if (t == PieceType.Wall)
                for (int k = 0; k < 10; k++)
                {
                    float x = -1.35f + k * 0.3f;
                    for (int s = -1; s <= 1; s += 2)
                        Art.Box(tr, k % 2 == 0 ? light : c * 0.85f, new Vector3(x, 1.5f, s * 0.165f), new Vector3(0.1f, 2.96f, 0.03f));
                }
            if (t == PieceType.Foundation)
                for (int k = 0; k < 10; k++)
                {
                    float x = -1.35f + k * 0.3f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Art.Box(tr, k % 2 == 0 ? light : c * 0.85f, new Vector3(x, 0.5f, s * 1.515f), new Vector3(0.1f, 0.96f, 0.03f));
                        Art.Box(tr, k % 2 == 0 ? light : c * 0.85f, new Vector3(s * 1.515f, 0.5f, x), new Vector3(0.03f, 0.96f, 0.1f));
                    }
                }
            if (t == PieceType.Wall)
            {
                // big rust blotches across both faces
                var spots = new[] { new Vector3(-0.8f, 0.6f, 0.7f), new Vector3(0.5f, 1.9f, 0.5f), new Vector3(0.9f, 0.4f, 0.4f), new Vector3(-0.3f, 2.5f, 0.45f), new Vector3(0.1f, 1.1f, 0.3f) };
                for (int side = -1; side <= 1; side += 2)
                    foreach (var sp in spots)
                    {
                        Art.Box(tr, rust, new Vector3(sp.x * side, sp.y, side * 0.185f), new Vector3(sp.z, sp.z * 0.8f, 0.01f), new Vector3(0, 0, sp.x * 30f));
                        Art.Box(tr, rust * 0.75f, new Vector3(sp.x * side + 0.05f, sp.y - sp.z * 0.5f, side * 0.188f), new Vector3(0.06f, sp.z * 0.9f, 0.01f)); // a streak running down
                    }
            }
            if (wallish)
            {
                // rust streaks and bolts down the sides (clear of any doorway / window gap)
                for (int s = -1; s <= 1; s += 2)
                for (int side = -1; side <= 1; side += 2)
                {
                    Art.Box(tr, rust, new Vector3(s * 1.22f, 0.7f + (s + 1) * 0.35f, side * 0.185f), new Vector3(0.32f, 0.55f, 0.01f), new Vector3(0, 0, s * 8f));
                    Art.Box(tr, rust * 1.2f, new Vector3(s * 1.1f, 2.3f - (s + 1) * 0.2f, side * 0.185f), new Vector3(0.2f, 0.35f, 0.01f));
                    for (int b = 0; b < 3; b++)
                        Art.Part(tr, Art.Sphere, steel, new Vector3(s * 1.42f, 0.4f + b * 1.1f, side * 0.19f), Vector3.one * 0.06f);
                }
            }
            else if (t == PieceType.Floor || t == PieceType.Foundation)
            {
                Art.Box(tr, rust, new Vector3(0.6f, t == PieceType.Floor ? 0.01f : 1.01f, -0.4f), new Vector3(0.7f, 0.01f, 0.5f), new Vector3(0, 20f, 0));
                Art.Box(tr, rust * 1.2f, new Vector3(-0.7f, t == PieceType.Floor ? 0.01f : 1.01f, 0.6f), new Vector3(0.45f, 0.01f, 0.6f), new Vector3(0, -15f, 0));
            }
        }

        /// <summary>
        /// Fort tower: an enclosed wooden tower. Walls all round the bottom with a doorway facing whoever threw it, a ladder up
        /// the back wall through a wide hatch, and a walled lookout with window gaps under a roof.
        /// </summary>
        static void BuildTower(Transform tr, bool col)
        {
            const float h = 4.5f, r = 1.4f, t = 0.12f;
            var wood = Art.Wood;
            var dark = Art.DarkWood;
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Art.Box(tr, dark, new Vector3(x * r, (h + 2.4f) * 0.5f - 0.3f, z * r), new Vector3(0.26f, h + 2.7f, 0.26f), default, col);
            // bottom walls; doorway in the front (+z)
            Art.Box(tr, wood, new Vector3(0, h * 0.5f, -r), new Vector3(2 * r, h, t), default, col);
            Art.Box(tr, wood, new Vector3(r, h * 0.5f, 0), new Vector3(t, h, 2 * r), default, col);
            Art.Box(tr, wood, new Vector3(-r, h * 0.5f, 0), new Vector3(t, h, 2 * r), default, col);
            Art.Box(tr, wood, new Vector3(-(r + 0.55f) * 0.5f, h * 0.5f, r), new Vector3(r - 0.55f, h, t), default, col);
            Art.Box(tr, wood, new Vector3((r + 0.55f) * 0.5f, h * 0.5f, r), new Vector3(r - 0.55f, h, t), default, col);
            Art.Box(tr, wood, new Vector3(0, (h + 2.2f) * 0.5f, r), new Vector3(1.1f, h - 2.2f, t), default, col);
            for (int k = 1; k < 4; k++)
            {
                Art.Box(tr, dark, new Vector3(0, k * 1.1f, -r - 0.07f), new Vector3(2 * r, 0.08f, 0.04f));
                Art.Box(tr, dark, new Vector3(r + 0.07f, k * 1.1f, 0), new Vector3(0.04f, 0.08f, 2 * r));
                Art.Box(tr, dark, new Vector3(-r - 0.07f, k * 1.1f, 0), new Vector3(0.04f, 0.08f, 2 * r));
            }
            // lookout floor with a wide hatch over the ladder (at the back)
            Art.Box(tr, wood, new Vector3(0, h - 0.1f, 0.6f), new Vector3(2 * r, 0.2f, 1.6f), default, col);
            Art.Box(tr, wood, new Vector3(-1.025f, h - 0.1f, -0.8f), new Vector3(0.75f, 0.2f, 1.2f), default, col);
            Art.Box(tr, wood, new Vector3(1.025f, h - 0.1f, -0.8f), new Vector3(0.75f, 0.2f, 1.2f), default, col);
            // lookout: chest-high walls all round, a window band, then the roof
            Art.Box(tr, wood, new Vector3(0, h + 0.55f, r), new Vector3(2 * r, 1.1f, t), default, col);
            Art.Box(tr, wood, new Vector3(0, h + 0.55f, -r), new Vector3(2 * r, 1.1f, t), default, col);
            Art.Box(tr, wood, new Vector3(r, h + 0.55f, 0), new Vector3(t, 1.1f, 2 * r), default, col);
            Art.Box(tr, wood, new Vector3(-r, h + 0.55f, 0), new Vector3(t, 1.1f, 2 * r), default, col);
            Art.Box(tr, dark, new Vector3(0, h + 2.15f, 0), new Vector3(2 * r + 0.3f, 0.12f, 2 * r + 0.3f), default, col);
            Art.Part(tr, Art.Cone, new Color(0.45f, 0.28f, 0.14f), new Vector3(0, h + 2.2f, 0), new Vector3(2 * r + 1.4f, 1.2f, 2 * r + 1.4f), new Vector3(0, 45f, 0));
            // ladder up the back wall
            float lz = -r + 0.12f;
            Art.Box(tr, dark, new Vector3(-0.3f, (h + 1f) * 0.5f, lz), new Vector3(0.07f, h + 1f, 0.07f));
            Art.Box(tr, dark, new Vector3(0.3f, (h + 1f) * 0.5f, lz), new Vector3(0.07f, h + 1f, 0.07f));
            for (float y = 0.3f; y < h + 0.8f; y += 0.35f)
                Art.Box(tr, wood, new Vector3(0, y, lz), new Vector3(0.6f, 0.05f, 0.05f));
            if (col)
            {
                var lg = new GameObject("ladder");
                lg.transform.SetParent(tr, false);
                lg.transform.localPosition = new Vector3(0, (h + 1.3f) * 0.5f, -r + 0.55f);
                var bc = lg.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = new Vector3(1.1f, h + 1.3f, 0.9f);
                var ladder = lg.AddComponent<Ladder>();
                ladder.TopLocalY = h; // at the top you're pushed forward (+z) onto the floor
            }
        }
    }

    /// <summary>A climbable ladder volume: walk into it and hold W (or Space) to climb, S to go down.</summary>
    public class Ladder : MonoBehaviour
    {
        /// <summary>Height of the floor at the top (in the tower's space); once your feet reach it you step off forwards.</summary>
        public float TopLocalY;
        public float TopWorldY => transform.parent != null ? transform.parent.TransformPoint(new Vector3(0, TopLocalY, 0)).y : TopLocalY;
        public Vector3 ExitDir => transform.forward;
        /// <summary>How hard you hop off the top (up, m/s).</summary>
        public float ExitHop = 1.5f;
        /// <summary>A placed ladder (Container.Deployables.cs), not the fort tower's own.</summary>
        public bool Deployed;

        Structure m_Support;
        float m_SupportAt = -10f;
        /// <summary>A placed ladder: the building piece it leans on (looked up twice a second).</summary>
        public Structure Support
        {
            get
            {
                if (!Deployed) return null;
                if (Time.time - m_SupportAt > 0.5f || (m_Support != null && !m_Support.IsSpawned))
                {
                    m_SupportAt = Time.time;
                    var root = transform.parent != null && transform.parent.parent != null ? transform.parent.parent : transform;
                    m_Support = Deployables.LadderSupport(root.position, root.rotation);
                }
                return m_Support;
            }
        }
        /// <summary>Leaning on a large wall or gate: going over the top is slow and the spikes hurt (like Rust's).</summary>
        public bool HighWall { get { var s = Support; return s != null && (s.PType == PieceType.Barrier || s.PType == PieceType.Gate); } }
        /// <summary>The top of the piece it leans on (world y).</summary>
        public float SupportTop
        {
            get
            {
                var s = Support;
                if (s == null) return float.MinValue;
                float top = float.MinValue;
                foreach (var col in s.GetComponentsInChildren<Collider>()) if (!col.isTrigger) top = Mathf.Max(top, col.bounds.max.y);
                return top;
            }
        }
        /// <summary>High enough up a large wall / gate that you can reach over its top from the ladder.</summary>
        public bool CanGoOver => HighWall && SupportTop - TopWorldY < 2.3f;
    }
}
