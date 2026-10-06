using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A storage chest placed in a base. Anyone who gets to it can use it (so it can be raided);
    /// breaking it spills everything onto the ground. Also used for airdrop crates (one OP item, take only, unbreakable).
    /// (Kind/Bag support is kept for old saves of the prefab only.)
    /// </summary>
    public partial class Container : NetworkBehaviour
    {
        public const byte Chest = 0, Bag = 1, Airdrop = 2, Gamble = 3, Workbench = 4, Workbench2 = 5; // Gamble: DNA mode's gambling machine (GambleMachine.cs); Workbench / Workbench2: the T1 / T2 benches (Workbench.cs)
        /// <summary>The Trade Station's placeables and the Advanced one's auto turret (Deployables.cs): a sleeping bag (respawn
        /// at it), a bear trap, a ladder and the turret (two slots: a weapon, its ammo).</summary>
        public const byte SleepBag = 6, Trap = 7, Ladder = 8, Turret = 9;
        /// <summary>A bear trap: 0 armed, 1 sprung. (Deployables.cs)</summary>
        public readonly NetworkVariable<byte> Flag = new NetworkVariable<byte>();
        /// <summary>An auto turret's aim (yaw, pitch in degrees, its own space), for every screen to pan it.</summary>
        public readonly NetworkVariable<Vector2> Aim = new NetworkVariable<Vector2>();
        /// <summary>A sleeping bag: when you can next respawn at it (server time).</summary>
        public readonly NetworkVariable<double> ReadyAt = new NetworkVariable<double>();
        public bool IsDeployable => Kind.Value >= SleepBag && Kind.Value <= Turret;
        /// <summary>The item a placed thing comes back as when it's picked up.</summary>
        public static Item ItemOf(byte kind) => kind == SleepBag ? Item.SleepingBag : kind == Trap ? Item.BearTrap : kind == Ladder ? Item.Ladder : kind == Turret ? Item.AutoTurret
            : kind == Workbench2 ? Item.Workbench2 : kind == Workbench ? Item.Workbench : Item.Chest;
        public static readonly List<Container> All = new List<Container>();

        public readonly NetworkList<ItemStack> Slots = new NetworkList<ItemStack>();
        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>();

        public bool IsBag => Kind.Value == Bag;
        public bool IsAirdrop => Kind.Value == Airdrop;
        public bool IsGamble => Kind.Value == Gamble;
        /// <summary>A team's workbench, T1 or T2 (no slots, unbreakable): its tier's items show in the TAB list in your base.</summary>
        public bool IsWorkbench => Kind.Value == Workbench || Kind.Value == Workbench2;
        /// <summary>A workbench's tier (1 or 2; 0 for anything else).</summary>
        public int BenchTier => Kind.Value == Workbench2 ? 2 : Kind.Value == Workbench ? 1 : 0;
        /// <summary>Chests can be damaged and rammed; bags and airdrops can't.</summary>
        public bool Breakable => Kind.Value == Chest || IsDeployable;
        public bool TakeOnly => IsBag || IsAirdrop;
        public string DisplayName => IsDeployable ? Cfg.ItemName(ItemOf(Kind.Value)) : IsWorkbench ? (BenchTier == 2 ? "Advanced Trade Station" : "Trade Station") : IsGamble ? "Gambling Machine" : IsBag ? $"{Cfg.TeamName[Mathf.Clamp(Team.Value, 0, 3)]}'s loot bag" : IsAirdrop ? "Alien Airdrop" : "Storage Chest";
        public Vector3 Center => transform.position + Vector3.up * (IsDeployable ? Deployables.CenterUp(Kind.Value) : IsBag ? 0.3f : IsAirdrop ? 0.6f : IsGamble ? 1f : IsWorkbench ? 0.9f : 0.4f);
        public bool Empty
        {
            get
            {
                for (int i = 0; i < Slots.Count; i++) if (!Slots[i].Empty) return false;
                return true;
            }
        }

        readonly List<ItemStack> m_Pending = new List<ItemStack>();
        int m_PendingSize;
        float m_Pop = 1f;
        Transform m_Visual, m_Crystal, m_Beacon;
        /// <summary>An airdrop crate's beacon: how tall it is right now (it stops under a ship that's still over it), -1 not set yet.</summary>
        float m_BeaconH = -1f;
        /// <summary>The beacon's full height (CreateVisual), and how fast it shoots up once the ship over it has gone (m/s).</summary>
        public const float BeaconHeight = 200f, BeaconGrow = 160f;
        /// <summary>Test hook: how tall this crate's beacon is right now (metres).</summary>
        public float BeaconTall => m_BeaconH;

        // ---------------- Server setup (before Spawn) ----------------
        public void ServerInit(byte kind, int team, int size, List<ItemStack> contents)
        {
            Kind.Value = kind;
            Team.Value = (byte)team;
            Health.Value = kind == Chest ? Cfg.ChestHp : kind >= SleepBag && kind <= Turret ? Deployables.MaxHp(kind) : 1f;
            m_PendingSize = size;
            m_Pending.Clear();
            if (contents != null) m_Pending.AddRange(contents);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                for (int i = 0; i < m_PendingSize; i++) Slots.Add(i < m_Pending.Count ? m_Pending[i] : default);
            }
            m_Visual = CreateVisual(Kind.Value, Team.Value, transform, null).transform;
            if (Kind.Value == Chest) PsxModels.Replace(m_Visual, "chest", PsxModels.Fit.Stretch); // PSX graphics: the cardboard box
            var bc = gameObject.AddComponent<BoxCollider>();
            if (IsBag) { bc.center = new Vector3(0, 0.3f, 0); bc.size = new Vector3(0.7f, 0.6f, 0.7f); }
            else if (IsAirdrop) { bc.center = new Vector3(0, 0.6f, 0); bc.size = new Vector3(1.4f, 1.2f, 1.4f); }
            else if (IsGamble) GambleMachine.Setup(this, m_Visual, bc);
            else if (IsWorkbench) RockGame.Workbench.Setup(this, m_Visual, bc);
            else if (IsDeployable) Deployables.Setup(this, m_Visual, bc); // (the bag, trap, ladder and turret: Deployables.cs)
            else { bc.center = new Vector3(0, 0.33f, 0); bc.size = new Vector3(1.1f, 0.66f, 0.62f); }
            m_Pop = 0f;
            // no grass through a chest, a workbench or a gamble machine (it grows back when it's gone; bags and airdrops lie in it)
            if (!IsBag && !IsAirdrop) GrassField.BlockRenderers(GetInstanceID(), transform);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            GrassField.Unblock(GetInstanceID());
            Deployables.Forget(this);
        }

        static Material s_BeaconCore, s_BeaconHalo;

        /// <summary>The landed airdrop crates' beacon (shared by them all): a purple beam as bright as the ball's from far
        /// away (its core, or the soft halo round it).</summary>
        static Material CrateBeacon(bool halo)
        {
            var glow = new Color(0.75f, 0.35f, 1f);
            if (halo) return s_BeaconHalo != null ? s_BeaconHalo : s_BeaconHalo = BeamFx.AsBeam(BeamFx.Column(glow, 0.45f, 2.2f, 0f, 0f, 0f, 0.55f));
            return s_BeaconCore != null ? s_BeaconCore : s_BeaconCore = BeamFx.AsBeam(BeamFx.Column(Color.Lerp(glow, Color.white, 0.25f), 2f, 1.1f, 0.25f, 0.6f, 0f, 0.3f, 0.12f));
        }

        public static GameObject CreateVisual(byte kind, int team, Transform parent, Material ghost)
        {
            var root = new GameObject("visual");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            if (kind >= SleepBag && kind <= Turret) { Deployables.Build(kind, team, t, ghost); return root; }
            if (kind == Bag)
            {
                var sack = new Color(0.45f, 0.36f, 0.24f);
                Art.Part(t, Art.Sphere, sack, new Vector3(0, 0.28f, 0), new Vector3(0.7f, 0.55f, 0.62f));
                Art.Part(t, Art.Sphere, sack * 0.9f, new Vector3(0, 0.55f, 0), new Vector3(0.28f, 0.22f, 0.28f));
                Art.Part(t, Art.Cylinder, Cfg.TeamColor[Mathf.Clamp(team, 0, 3)], new Vector3(0, 0.5f, 0), new Vector3(0.24f, 0.03f, 0.24f));
            }
            else if (kind == Airdrop)
            {
                // alien crate: dark metal with glowing seams, a floating crystal and a beacon so you can find it
                var metal = new Color(0.22f, 0.24f, 0.28f);
                var glow = new Color(0.75f, 0.35f, 1f);
                Art.Box(t, metal, new Vector3(0, 0.6f, 0), new Vector3(1.3f, 1.2f, 1.3f));
                Art.Box(t, metal * 1.3f, new Vector3(0, 1.22f, 0), new Vector3(1.38f, 0.08f, 1.38f));
                Art.Box(t, metal * 1.3f, new Vector3(0, 0.04f, 0), new Vector3(1.38f, 0.08f, 1.38f));
                for (int k = 0; k < 4; k++)
                {
                    var rot = Quaternion.Euler(0, k * 90f, 0);
                    Art.Box(t, glow, rot * new Vector3(0, 0.6f, 0.655f), (k % 2 == 0) ? new Vector3(0.9f, 0.06f, 0.02f) : new Vector3(0.02f, 0.06f, 0.9f));
                    Art.Box(t, glow, rot * new Vector3(0, 0.6f, 0.655f), (k % 2 == 0) ? new Vector3(0.06f, 0.9f, 0.02f) : new Vector3(0.02f, 0.9f, 0.06f));
                }
                Art.Part(t, Art.Ico, glow, new Vector3(0, 1.6f, 0), new Vector3(0.22f, 0.35f, 0.22f), new Vector3(0, 30, 0), false, null, "crystal");
                if (ghost == null)
                {
                    // its beacon: a beam of light like the ball's (BeamFx: as bright from far away, fading down to a
                    // faint purple column as you come up to it - Settings > Display > BEAMS)
                    var beacon = new GameObject("beacon");
                    beacon.transform.SetParent(t, false);
                    float ch = Art.Cylinder.bounds.extents.y, cr = Art.Cylinder.bounds.extents.x;
                    var core = BeamFx.Cylinder(beacon.transform, CrateBeacon(false), "beacon core");
                    core.transform.localPosition = new Vector3(0, 100f, 0);
                    core.transform.localScale = new Vector3(0.45f / cr, 100f / ch, 0.45f / cr);
                    var halo = BeamFx.Cylinder(beacon.transform, CrateBeacon(true), "beacon halo");
                    halo.transform.localPosition = new Vector3(0, 100f, 0);
                    halo.transform.localScale = new Vector3(1.5f / cr, 100f / ch, 1.5f / cr);
                    var lg = new GameObject("glow");
                    lg.transform.SetParent(t, false);
                    lg.transform.localPosition = new Vector3(0, 1.8f, 0);
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = glow;
                    l.range = 8f;
                    l.intensity = 3f;
                }
            }
            else if (kind == Gamble) { } // built by GambleMachine.Setup
            else if (kind == Workbench || kind == Workbench2) RockGame.Workbench.BuildModel(t, team, ghost == null, kind == Workbench2 ? 2 : 1);
            else
            {
                using var tint = ColorSlots.Use(ColorSlots.Chests); // (Settings > Display colours)
                Art.Box(t, Art.Wood, new Vector3(0, 0.25f, 0), new Vector3(1.05f, 0.5f, 0.58f));
                Art.Box(t, Art.DarkWood, new Vector3(0, 0.56f, 0), new Vector3(1.1f, 0.14f, 0.62f)); // lid
                Art.Box(t, Art.Metal, new Vector3(-0.35f, 0.33f, 0), new Vector3(0.06f, 0.68f, 0.64f));
                Art.Box(t, Art.Metal, new Vector3(0.35f, 0.33f, 0), new Vector3(0.06f, 0.68f, 0.64f));
                Art.Box(t, new Color(0.85f, 0.7f, 0.25f), new Vector3(0, 0.45f, 0.31f), new Vector3(0.12f, 0.14f, 0.04f)); // latch
            }
            if (ghost != null)
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = ghost;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            return root;
        }

        void Update()
        {
            if (IsAirdrop && m_Visual)
            {
                if (m_Crystal == null) m_Crystal = m_Visual.Find("crystal");
                if (m_Crystal) m_Crystal.Rotate(0, 90f * Time.deltaTime, 0, Space.World); // the crystal on top spins
                // its beacon stops under the belly of the ship that dropped it until that's flown off (it used to run
                // straight up through the UFO), then shoots up to its full height
                if (m_Beacon == null) m_Beacon = m_Visual.Find("beacon");
                if (m_Beacon)
                {
                    float want = Mathf.Clamp(AirdropShip.BeaconCeiling(transform.position) - transform.position.y, 0.5f, BeaconHeight);
                    m_BeaconH = m_BeaconH < 0f || want < m_BeaconH ? want : Mathf.MoveTowards(m_BeaconH, want, BeaconGrow * Time.deltaTime);
                    m_Beacon.localScale = new Vector3(1f, m_BeaconH / BeaconHeight, 1f);
                }
            }
            if (IsDeployable && m_Visual) Deployables.Tick(this, m_Visual); // (the trap's jaws, the turret panning and firing)
            if (m_Pop < 1f && m_Visual)
            {
                m_Pop = Mathf.Min(1f, m_Pop + Time.deltaTime * 4f);
                float s = 1f + Mathf.Sin(m_Pop * Mathf.PI) * 0.25f;
                m_Visual.localScale = new Vector3(s, Mathf.Lerp(0.3f, 1f, m_Pop) * s, s);
            }
        }

        public bool InReach(Vector3 eye) => Vector3.Distance(eye, Center) <= Cfg.LootRange + 1.5f;

        // ---------------- Server ----------------

        public void ServerDamage(float dmg)
        {
            if (!IsServer || !IsSpawned || !Breakable || dmg <= 0) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0) ServerBreak();
        }

        /// <summary>A destroyed chest spills its contents on the ground.</summary>
        public void ServerBreak()
        {
            var items = new List<ItemStack>();
            for (int i = 0; i < Slots.Count; i++) if (!Slots[i].Empty) items.Add(Slots[i]);
            if (NetGame.Instance != null) NetGame.Instance.ServerScatter(items, transform.position + Vector3.up * 0.5f);
            Fx.Server(FxKind.Break, transform.position + Vector3.up * 0.4f, Vector3.up);
            NetworkObject.Despawn(true);
        }

        /// <summary>
        /// A workbench blown up by C4 stuck right onto it (the only thing that can): it drops as its item where it stood
        /// (anyone can pick it up and put it down again), and its team's slot is free - they can put that one or a new one down.
        /// </summary>
        public void ServerBreakBench()
        {
            if (!IsServer || !IsSpawned || !IsWorkbench) return;
            var item = BenchTier == 2 ? Item.Workbench2 : Item.Workbench;
            var g = NetGame.Instance;
            if (g != null)
            {
                g.ServerDropItem(ItemStack.Of(item, 1), transform.position + Vector3.up * 0.3f, transform.forward, transform.position + Vector3.up * 0.9f);
                g.Broadcast($"{Cfg.TeamLabel(Team.Value)}'s {Cfg.ItemName(item)} was blown up - it dropped on the ground");
            }
            Fx.Server(FxKind.Break, transform.position + Vector3.up * 0.6f, Vector3.up);
            NetworkObject.Despawn(true);
        }
    }
}
