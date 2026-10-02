using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>A building piece (foundation, wall, doorway, window, floor, stairs), a free-standing barrier or a thrown fort tower.</summary>
    public class Structure : NetworkBehaviour
    {
        public static readonly List<Structure> All = new List<Structure>();

        public readonly NetworkVariable<byte> Type = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Tier = new NetworkVariable<byte>();   // 0 wood, 1 stone, 2 metal, 3 refined
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>();
        public readonly NetworkVariable<bool> DoorOpen = new NetworkVariable<bool>();

        [NonSerialized] public PieceKey Key;
        [NonSerialized] public bool HasKey;

        Transform m_Visual, m_Hinge;
        float m_Rise = 1f, m_DoorAngle;

        public PieceType PType => (PieceType)Type.Value;
        public float MaxHp => Cfg.PieceHp(PType, Tier.Value);
        public string DisplayName => PType == PieceType.Tower ? "Fort Tower" : PType == PieceType.Barrier ? "High External Wall" : PType == PieceType.EggBlock ? "Egg Block" : Cfg.TierName(Tier.Value) + " " + Cfg.PieceName(PType);
        public bool Upgradable => Cfg.IsGridPiece(PType);

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Rebuild();
            m_Rise = 0f;
            Tier.OnValueChanged += OnTierChanged;
            ClearGrass();
            // placed right where someone stands: pop them out on top instead of trapping them inside
            if (PlayerController.Local != null) PlayerController.Local.ResolveOverlap(transform);
        }

        /// <summary>No grass poking up through what's been built.</summary>
        void ClearGrass()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center = new Vector3(b.center.x, transform.position.y + b.extents.y, b.center.z); // (it's still rising into place)
            GrassField.ClearUnder(b);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Tier.OnValueChanged -= OnTierChanged;
            if (IsServer && HasKey && BuildGrid.Registry.TryGetValue(Key, out var s) && s == this)
                BuildGrid.Registry.Remove(Key);
        }

        void OnTierChanged(byte prev, byte cur)
        {
            Rebuild();
            m_Rise = 0.4f; // little "rebuild" pop
        }

        void Rebuild()
        {
            if (m_Visual) Destroy(m_Visual.gameObject);
            m_Visual = CreateVisual(PType, Tier.Value, transform, true, null, out m_Hinge).transform;
            if (PType == PieceType.EggBlock)
                foreach (var r in m_Visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Art.Mat(Color.Lerp(Color.white, Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, Cfg.TeamColor.Length - 1)], 0.6f));
            m_DoorAngle = DoorOpen.Value ? 100f : 0f;
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
                float target = DoorOpen.Value ? 100f : 0f;
                m_DoorAngle = Mathf.MoveTowards(m_DoorAngle, target, 300f * Time.deltaTime);
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
            Key = key;
            HasKey = hasKey;
        }

        public void ServerDamage(float dmg) => ServerDamage(dmg, true);

        /// <summary>collapse = check whether other pieces lost their support (explosions do it once at the end).</summary>
        public void ServerDamage(float dmg, bool collapse)
        {
            if (!IsServer || !IsSpawned || dmg <= 0) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0)
            {
                NetworkObject.Despawn(true);
                if (collapse && NetGame.Instance) NetGame.Instance.ServerCollapseCheck();
            }
        }

        public void ServerUpgrade(int tier = 1)
        {
            Tier.Value = (byte)tier;
            Health.Value = MaxHp;
        }

        /// <summary>Battering ram hit: one tier down at full health (refined to metal, metal to stone, stone to wood).</summary>
        public void ServerDowngrade()
        {
            Tier.Value = (byte)Mathf.Max(0, Tier.Value - 1);
            Health.Value = MaxHp;
        }

        // ---------------- Visuals (also used for placement ghosts) ----------------

        public static GameObject CreateVisual(PieceType t, int tier, Transform parent, bool colliders, Material ghost, out Transform hinge)
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
            Color trim = refined ? new Color(0.8f, 0.64f, 0.28f) : sheet ? new Color(0.42f, 0.24f, 0.13f) : stone ? new Color(0.42f, 0.42f, 0.46f) : Art.DarkWood;
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
                    // a wall with a square opening at chest height, crossed by two bars (like Rust)
                    Art.Box(tr, c, new Vector3(-0.975f, 1.5f, 0), new Vector3(1.05f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(0.975f, 1.5f, 0), new Vector3(1.05f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(0, 0.55f, 0), new Vector3(0.9f, 1.1f, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(0, 2.55f, 0), new Vector3(0.9f, 0.9f, 0.3f), default, col);
                    Art.Box(tr, trim, new Vector3(0, 1.12f, 0), new Vector3(1.0f, 0.08f, 0.36f));
                    Art.Box(tr, trim, new Vector3(0, 2.08f, 0), new Vector3(1.0f, 0.08f, 0.36f));
                    Art.Box(tr, stone ? Art.Metal : trim, new Vector3(-0.2f, 1.6f, 0), new Vector3(0.05f, 0.95f, 0.05f));
                    Art.Box(tr, stone ? Art.Metal : trim, new Vector3(0.2f, 1.6f, 0), new Vector3(0.05f, 0.95f, 0.05f));
                    if (!stone)
                    {
                        Art.Box(tr, trim, new Vector3(-1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                        Art.Box(tr, trim, new Vector3(1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                    }
                    break;
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
                case PieceType.Barrier:
                {
                    // a high external wall like Rust's: a row of big sharpened logs, 4 m wide and about 5.5 m tall,
                    // tied together with two cross beams and propped up by braces on the back
                    const int logs = 10;
                    for (int k = 0; k < logs; k++)
                    {
                        float x = -1.8f + k * 0.4f;
                        float h = 5f + ((k * 37) % 5) * 0.08f;
                        Art.Box(tr, k % 2 == 0 ? Art.Wood : Art.Wood * 0.9f, new Vector3(x, h * 0.5f, 0), new Vector3(0.42f, h, 0.42f), new Vector3(0, k * 13f, 0));
                        Art.Part(tr, Art.Cone, Art.Wood * 1.05f, new Vector3(x, h, 0), new Vector3(0.42f, 0.55f, 0.42f));
                    }
                    for (int k = 0; k < 2; k++)
                        Art.Box(tr, Art.DarkWood, new Vector3(0, 1.2f + k * 2.6f, 0.26f), new Vector3(4.1f, 0.22f, 0.12f));
                    for (int k = -1; k <= 1; k += 2)
                        Art.Box(tr, Art.DarkWood, new Vector3(k * 1.3f, 1.6f, 0.95f), new Vector3(0.18f, 3.6f, 0.18f), new Vector3(-28f, 0, 0));
                    if (col)
                    {
                        var bc = root.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 2.6f, 0);
                        bc.size = new Vector3(4f, 5.2f, 0.45f);
                    }
                    break;
                }
            }

            if (sheet) RustSheets(tr, t, c, trim);

            // Builder: walls built straight on the ground (no foundation) reach down into it, so nothing rolls out underneath
            if (Cfg.Builder && (t == PieceType.Wall || t == PieceType.Doorway || t == PieceType.Window) && parent.position.y < Cfg.BaseY + 0.5f)
                Art.Box(tr, trim, new Vector3(0, -0.6f, 0), new Vector3(3f, 1.2f, 0.3f), default, col);

            if (ghost == null) ApplyPsxLook(root, t, tier, c, trim, hinge);

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
    }
}
