using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A tree (wood), boulder (stone) or berry bush. Trees and boulders have a Rust-style weak spot
    /// (an X on trees, a sparkle on rocks) that appears after the first hit, gives bonus resources and jumps
    /// somewhere else when hit. Depleted nodes regrow later. A berry bush is picked whole (E): it disappears and a new
    /// one grows up out of the ground somewhere else in the same half a while later.
    /// </summary>
    public class ResourceNode : NetworkBehaviour
    {
        public const byte Tree = 0, Boulder = 1, Bush = 2;
        const int SpotCount = 12;
        /// <summary>Spot value while the node hasn't been hit yet: no weak spot is shown.</summary>
        public const byte NoSpot = 255;

        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> Amount = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Seed = new NetworkVariable<int>();
        public readonly NetworkVariable<byte> Spot = new NetworkVariable<byte>();
        /// <summary>Fake bomb bush: the team that threw it (NoTrap = a real bush). Looks exactly the same.</summary>
        public readonly NetworkVariable<byte> TrapTeam = new NetworkVariable<byte>(NoTrap);
        public const byte NoTrap = 255;

        /// <summary>Every node in the match (to find the tree a hit landed on, and to swap graphics modes).</summary>
        public static readonly System.Collections.Generic.List<ResourceNode> All = new System.Collections.Generic.List<ResourceNode>();

        GameObject m_Visual;
        Transform m_Marker;
        /// <summary>A PSX trunk can be thicker or thinner than the (unchanged) trunk collider: the X moves out / in by this much, onto the bark you see.</summary>
        float m_MarkerOut;
        PsxArt.Trunk m_PsxTrunk;
        MeshFilter m_Decal;
        float m_TrunkR = 0.3f;
        /// <summary>This tree's bark and leaf colours (for the chips and leaves that fly off it).</summary>
        public Color Bark = Art.Wood, Leaf = Art.Leaves;
        Collider m_SpotCollider;
        GameObject[] m_Berries;
        float m_RespawnAt, m_Shake, m_MarkerPop = 1f, m_Grow = 1f;
        /// <summary>This bush is the Normal graphics' swaying one (not the old one PSX / AI PSX use).</summary>
        bool m_PaintedBush;
        static bool PaintedBush => GameSettings.GraphicsMode == 0 && WorldLook.BushLeaves != null && WorldLook.Berries != null;
        Vector3 m_VisualBase;

        public bool IsBush => Kind.Value == Bush;
        public int MaxAmount => Kind.Value == Tree ? Cfg.TreeAmount : Kind.Value == Boulder ? Cfg.StoneAmount : 1;
        public string DisplayName => Kind.Value == Tree ? "Tree" : Kind.Value == Boulder ? "Stone" : "Berry Bush";
        public Item Yield => Kind.Value == Tree ? Item.Wood : Kind.Value == Boulder ? Item.Stone : Item.Berry;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            GameSettings.GraphicsChanged += OnGraphicsChanged;
            BuildVisual();
            if (IsBush) m_Grow = 0f; // bushes come up out of the ground
            Amount.OnValueChanged += OnAmountChanged;
            Spot.OnValueChanged += OnSpotChanged;
            RefreshState();
            PlaceMarker();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            GameSettings.GraphicsChanged -= OnGraphicsChanged;
            Amount.OnValueChanged -= OnAmountChanged;
            Spot.OnValueChanged -= OnSpotChanged;
        }

        void OnAmountChanged(int prev, int cur)
        {
            if (cur < prev && !IsBush) m_Shake = 0.3f;
            RefreshState();
        }

        void OnSpotChanged(byte prev, byte cur)
        {
            m_MarkerPop = 0f;
            PlaceMarker();
        }

        void RefreshState()
        {
            bool alive = Amount.Value > 0;
            if (IsBush)
            {
                // picked = the whole bush is gone
                if (m_Visual.activeSelf != alive) m_Visual.SetActive(alive);
                return;
            }
            if (m_Visual.activeSelf != alive) m_Visual.SetActive(alive);
            if (alive && Kind.Value == Boulder)
            {
                float s = Mathf.Lerp(0.55f, 1f, Amount.Value / (float)MaxAmount);
                m_Visual.transform.localScale = Vector3.one * s;
                PlaceMarker();
            }
        }

        /// <summary>Normal / PSX graphics switched: rebuild the tree's looks (nothing else about it changes).</summary>
        void OnGraphicsChanged()
        {
            if (m_Visual == null) return;
            if (Kind.Value == Bush && PaintedBush != m_PaintedBush)
            {
                // the Normal bush and the PSX / AI PSX one are built differently: build the other one
                var was = m_Visual;
                var pos = was.transform.localPosition;
                var scale = was.transform.localScale;
                bool shown = was.activeSelf;
                was.SetActive(false);
                Destroy(was);
                BuildVisual();
                m_Visual.transform.localPosition = pos;
                m_Visual.transform.localScale = scale;
                m_Visual.SetActive(shown);
                RefreshState();
                return;
            }
            if (Kind.Value != Tree) { if (AiPsxArt.On) AiPsxArt.Apply(m_Visual.transform); return; }
            var old = m_Visual;
            old.SetActive(false);
            Destroy(old);
            BuildVisual();
            RefreshState();
            PlaceMarker();
        }

        void BuildVisual()
        {
            var rng = new System.Random(Seed.Value);
            float r() => (float)rng.NextDouble();
            m_Visual = new GameObject("visual");
            m_Visual.transform.SetParent(transform, false);
            var tr = m_Visual.transform;
            if (Kind.Value == Tree)
            {
                var trunk = BuildTreeVisual(tr, Seed.Value, true, out float trunkR, out Bark, out Leaf);
                m_SpotCollider = trunk.GetComponent<Collider>();
                m_MarkerOut = trunkR - 0.3f; // can be negative: a thin PSX trunk has the X further in than the collider
                m_PsxTrunk = tr.GetComponentInChildren<PsxArt.Trunk>();
                if (PsxArt.On && m_PsxTrunk != null) m_MarkerOut = 0f; // (the trunk collider wraps the PSX trunk there)
                m_TrunkR = trunkR;
                m_Decal = null;
                // the X (a chunky pixel-art one on PSX trees)
                m_Marker = new GameObject("x").transform;
                m_Marker.SetParent(tr, false);
                if ((PsxArt.On || AiPsxArt.On) && trunk.GetComponent<MeshRenderer>() != null && !trunk.GetComponent<MeshRenderer>().enabled) PsxArt.PixelX(m_Marker);
                else
                {
                    var xc = new Color(1f, 0.45f, 0.1f);
                    Art.Box(m_Marker, xc, Vector3.zero, new Vector3(0.34f, 0.06f, 0.02f), new Vector3(0, 0, 45));
                    Art.Box(m_Marker, xc, Vector3.zero, new Vector3(0.34f, 0.06f, 0.02f), new Vector3(0, 0, -45));
                }
            }
            else if (Kind.Value == Boulder)
            {
                var mesh = Art.MakeRock(Seed.Value, 0.28f);
                Color c = Color.Lerp(Art.Stone, new Color(0.45f, 0.44f, 0.42f), r());
                var stoneTint = ColorSlots.Use(ColorSlots.StoneNodes);
                var main = Art.Part(tr, mesh, c, new Vector3(0, 0.6f, 0), new Vector3(1.6f, 1.2f, 1.4f), new Vector3(r() * 30, r() * 360, r() * 20), false, null, "rock");
                var mc = main.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
                m_SpotCollider = mc;
                // (the little rock beside it is solid too - you used to be able to walk through it)
                Art.Part(tr, Art.MakeRock(Seed.Value + 7, 0.3f), c * 0.9f, new Vector3(0.9f, 0.3f, 0.5f), Vector3.one * 0.6f, new Vector3(0, r() * 360, 0), true);
                stoneTint.Dispose();
                // the sparkle star
                m_Marker = new GameObject("star").transform;
                m_Marker.SetParent(tr, false);
                var sc = new Color(1f, 0.95f, 0.55f);
                for (int k = 0; k < 4; k++)
                    Art.Box(m_Marker, sc, Vector3.zero, new Vector3(k % 2 == 0 ? 0.34f : 0.2f, 0.04f, 0.02f), new Vector3(0, 0, k * 45f));
                Art.Part(m_Marker, Art.Ico, Color.white, Vector3.zero, Vector3.one * 0.05f);
            }
            else if (PaintedBush)
            {
                // Normal graphics: a round clump of leafy blobs with berries on it, swaying in the wind like the pines
                Color leaf = Color.Lerp(ItemModels.Leaf, new Color(0.18f, 0.4f, 0.16f), r());
                BuildBush(tr, Seed.Value, leaf);
                m_PaintedBush = true;
                if (GetComponent<SphereCollider>() == null)
                {
                    // interaction only (you walk through bushes)
                    var sc = gameObject.AddComponent<SphereCollider>();
                    sc.isTrigger = true;
                    sc.center = new Vector3(0, 0.5f, 0);
                    sc.radius = 0.8f;
                }
            }
            else
            {
                // (PSX / AI PSX: the old bush, which the PSX bush is fitted to and the AI PSX look re-skins)
                m_PaintedBush = false;
                Color leaf = Color.Lerp(ItemModels.Leaf, new Color(0.18f, 0.4f, 0.16f), r());
                var bushTint = ColorSlots.Use(ColorSlots.Bushes);
                Art.Part(tr, Art.MakeRock(Seed.Value, 0.2f), leaf, new Vector3(0, 0.45f, 0), new Vector3(0.75f, 0.5f, 0.7f), new Vector3(0, r() * 360, 0));
                Art.Part(tr, Art.MakeRock(Seed.Value + 1, 0.2f), leaf * 0.9f, new Vector3(0.4f, 0.35f, 0.2f), new Vector3(0.45f, 0.38f, 0.45f));
                Art.Part(tr, Art.MakeRock(Seed.Value + 2, 0.2f), leaf * 1.1f, new Vector3(-0.35f, 0.3f, -0.2f), new Vector3(0.45f, 0.35f, 0.4f));
                bushTint.Dispose();
                // loaded with berries
                m_Berries = new GameObject[14];
                for (int i = 0; i < m_Berries.Length; i++)
                {
                    float a = i * 0.9f + r();
                    float y = 0.3f + r() * 0.6f;
                    float rad = 0.45f + r() * 0.3f;
                    var p = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad * 0.95f);
                    using (ColorSlots.Use(ColorSlots.Berries))
                    m_Berries[i] = Art.Part(tr, Art.Sphere, i % 4 == 0 ? ItemModels.Berry * 0.8f : ItemModels.Berry, p, Vector3.one * (0.12f + r() * 0.05f));
                }
                if (GetComponent<SphereCollider>() == null)
                {
                    // interaction only (you walk through bushes)
                    var sc = gameObject.AddComponent<SphereCollider>();
                    sc.isTrigger = true;
                    sc.center = new Vector3(0, 0.5f, 0);
                    sc.radius = 0.8f;
                }
            }
            m_VisualBase = tr.localPosition;
            if (Kind.Value == Boulder) PsxModels.Replace(tr, "rock" + (1 + (Seed.Value & 0x7fffffff) % 6), PsxModels.Fit.Uniform, new Vector3(0, (Seed.Value % 360), 0), 1f, m_Marker);
            else if (Kind.Value == Bush && !m_PaintedBush) PsxModels.Replace(tr, "bush", PsxModels.Fit.Ground, new Vector3(0, (Seed.Value % 360), 0));
            if (AiPsxArt.On) AiPsxArt.Apply(tr);
        }

        /// <summary>
        /// Normal graphics' berry bush: a rounded clump of leafy blobs (faceted balls, darker underneath and lighter on
        /// top, each a slightly different green) with clusters of berries sitting on them. One mesh, two draws (leaves,
        /// berries), swaying in the same gusts as the pines and the grass (more at the top, the outside fluttering).
        /// About as big as the old bush.
        /// </summary>
        static void BuildBush(Transform tr, int seed, Color leaf)
        {
            var rng = new System.Random(seed * 31 + 7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            MeshKit.Ico80(out var icoV, out var icoF);
            var blobs = new List<(Vector3 c, float r, float squash)>();
            blobs.Add((new Vector3(0, 0.5f, 0), R(0.5f, 0.56f), 0.82f));
            int ring = 5 + rng.Next(2);
            float spin = R(0, Mathf.PI * 2f);
            for (int i = 0; i < ring; i++)
            {
                float a = spin + i * Mathf.PI * 2f / ring + R(-0.25f, 0.25f), d = R(0.4f, 0.5f);
                blobs.Add((new Vector3(Mathf.Cos(a) * d, R(0.3f, 0.42f), Mathf.Sin(a) * d), R(0.3f, 0.38f), R(0.8f, 0.95f)));
            }
            int tops = 1 + rng.Next(2);
            for (int i = 0; i < tops; i++)
                blobs.Add((new Vector3(R(-0.18f, 0.18f), R(0.78f, 0.86f), R(-0.18f, 0.18f)), R(0.26f, 0.32f), 0.85f));
            const float ground = 0.06f;
            Vector2 Sway(Vector3 p, float flutter) => new Vector2(0.28f * Mathf.Clamp01(p.y / 1.05f), flutter);
            var leaves = new MeshKit();
            foreach (var (c, r, squash) in blobs)
            {
                // a lumpy ball, flat where it meets the ground, its own shade of green
                Color.RGBToHSV(leaf, out float hue, out float sat, out float val);
                var tint = Color.HSVToRGB(Mathf.Repeat(hue + R(-0.025f, 0.025f), 1f), sat * R(0.9f, 1.08f), val * R(0.88f, 1.12f));
                var pts = new Vector3[icoV.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    var q = icoV[i] * r * R(0.9f, 1.1f);
                    q.y *= squash;
                    q += c;
                    q.y = Mathf.Max(q.y, ground);
                    pts[i] = q;
                }
                for (int i = 0; i < icoF.Length; i += 3)
                {
                    Vector3 a = pts[icoF[i]], b = pts[icoF[i + 1]], d = pts[icoF[i + 2]];
                    var n = Vector3.Cross(b - a, d - a).normalized;
                    float y = (a.y + b.y + d.y) / 3f;
                    var col = tint * (Mathf.Lerp(0.62f, 1.12f, n.y * 0.5f + 0.5f) * Mathf.Lerp(0.8f, 1.05f, Mathf.Clamp01(y / 0.9f)));
                    col.a = 1f;
                    float fl = n.y < -0.5f ? 0f : 0.6f;
                    leaves.Tri(a, b, d, col, col, col, Sway(a, fl), Sway(b, fl), Sway(d, fl));
                }
            }
            // berries in little clusters on the outside of the blobs (not buried in the next one)
            var berries = new MeshKit();
            bool Buried(Vector3 p, int self)
            {
                for (int i = 0; i < blobs.Count; i++)
                {
                    if (i == self) continue;
                    var d = p - blobs[i].c;
                    d.y /= blobs[i].squash;
                    if (d.magnitude < blobs[i].r * 0.98f) return true;
                }
                return false;
            }
            int want = 18 + rng.Next(6), made = 0;
            for (int tries = 0; tries < 300 && made < want; tries++)
            {
                int bi = 1 + rng.Next(blobs.Count - 1);
                var (c, r, squash) = blobs[bi];
                var dir = new Vector3(R(-1f, 1f), R(-0.15f, 0.9f), R(-1f, 1f));
                var outward = new Vector3(c.x, 0, c.z);
                if (outward.sqrMagnitude > 0.001f && Vector3.Dot(new Vector3(dir.x, 0, dir.z).normalized, outward.normalized) < 0.1f) continue;
                dir.Normalize();
                var at = c + new Vector3(dir.x * r, dir.y * r * squash, dir.z * r);
                if (at.y < 0.2f || Buried(at, bi)) continue;
                int cluster = 1 + rng.Next(3);
                var side = Vector3.Cross(dir, Vector3.up).normalized;
                var up = Vector3.Cross(side, dir);
                for (int k = 0; k < cluster && made < want; k++)
                {
                    float br = R(0.065f, 0.085f);
                    var bc = at + dir * br * 0.55f + (side * R(-1f, 1f) + up * R(-1f, 1f)) * 0.06f * (k == 0 ? 0f : 1f);
                    var berry = k == 0 && rng.NextDouble() < 0.3 ? ItemModels.Berry * 0.8f : ItemModels.Berry;
                    berry.a = 1f;
                    Berry(berries, bc, br, berry, Sway(bc, 0.6f));
                    made++;
                }
            }
            MeshKit.Spawn(tr, "bush", new[] { WorldLook.BushLeaves, WorldLook.Berries }, true, leaves, berries);
        }

        /// <summary>One berry: a little faceted ball, shiny on top.</summary>
        static void Berry(MeshKit kit, Vector3 c, float r, Color col, Vector2 sway)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int i = 0; i < v.Length; i++) v[i] = c + v[i].normalized * r;
            var hi = Color.Lerp(col, Color.white, 0.35f); hi.a = 1f;
            var lo = col * 0.7f; lo.a = 1f;
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], d = v[f[i + 2]];
                float ny = Vector3.Cross(b - a, d - a).normalized.y;
                var cc = ny > 0.6f ? hi : ny < -0.3f ? lo : col;
                kit.Tri(a, b, d, cc, cc, cc, sway, sway, sway);
            }
        }

        /// <summary>A tree exactly like the map's trees (also used for the tree camo costume). Returns the trunk.</summary>
        public static GameObject BuildTreeVisual(Transform tr, int seed, bool collider) => BuildTreeVisual(tr, seed, collider, out _, out _, out _);

        /// <summary>
        /// The trunk (with the collider when asked) is always the same cylinder, so trees play the same in both graphics
        /// modes. Normal mode draws it with cone leaves; PSX mode hides it and puts one of the PSX tree models there instead,
        /// as tall as the normal tree. trunkRadius: how thick the visible trunk is where the weak spot goes.
        /// </summary>
        public static GameObject BuildTreeVisual(Transform tr, int seed, bool collider, out float trunkRadius, out Color bark, out Color leafColor)
        {
            var rng = new System.Random(seed);
            float r() => (float)rng.NextDouble();
            float h = 4.5f + r() * 2.5f;
            GameObject trunk;
            using (ColorSlots.Use(ColorSlots.TreeTrunks))
                trunk = Art.Part(tr, Art.Cylinder, Art.DarkWood, new Vector3(0, h * 0.5f, 0), new Vector3(0.6f, h * 0.5f, 0.6f), default, collider, null, "trunk");
            float leafR = r();
            Color leaf = Color.Lerp(Art.Leaves, new Color(0.3f, 0.55f, 0.2f), leafR);
            leaf = ThemeMaps.LeafTint(leaf); // THEME MAPS
            trunkRadius = 0.3f;
            bark = Art.Wood;
            leafColor = leaf;
            if (ThemeMaps.BuildPalm(tr, seed, h, trunk)) return trunk; // THEME MAPS
            // (PSX graphics: the trees are twice the size; the AI PSX test keeps them as they were)
            if ((PsxArt.On || AiPsxArt.On) && PsxArt.BuildTree(tr, seed, (h * 0.45f + 5.2f + (r() - 0.5f) * 1.2f) * (PsxArt.On ? 2f : 1f), out var pr, out var pb, out var pl))
            {
                trunk.GetComponent<MeshRenderer>().enabled = false;
                trunkRadius = pr;
                bark = pb;
                leafColor = pl;
                // PSX: the bigger trunk is what you hit (and what stops you) - the collider wraps the trunk you see
                var cap = trunk.GetComponent<CapsuleCollider>();
                var model = tr.GetComponentInChildren<PsxArt.Trunk>();
                if (PsxArt.On && cap != null && model != null && model.Model != null)
                {
                    var ts = trunk.transform.localScale;
                    var mid = trunk.transform.InverseTransformPoint(model.transform.TransformPoint(model.Model.TrunkCenter));
                    cap.radius = Mathf.Clamp(pr, 0.15f, 1.5f) / Mathf.Max(0.01f, ts.x);
                    cap.center = new Vector3(mid.x, 0f, mid.z);
                }
                return trunk;
            }
            var foliage = WorldLook.Foliage;
            if (foliage == null || WorldLook.Bark == null)
            {
                // (no Painted shader: the old plain cones)
                for (int k = 0; k < 3; k++)
                {
                    float y = h * 0.45f + k * 1.4f;
                    float w = 3.6f - k * 0.9f;
                    Art.Part(tr, Art.Cone, leaf, new Vector3(0, y, 0), new Vector3(w, 2.4f, w), new Vector3(0, r() * 60f, 0));
                }
                return trunk;
            }
            // Normal graphics: a tall pine (one of five kinds, picked by the seed so every peer builds the same one) - its
            // tiers of needles and the trunk in one mesh (two draws: needles, bark), greens to go with the grass (dark
            // underneath like the grass roots, bright lime towards the tips), swaying in the same gusts as the grass. The
            // trunk is part of the swaying mesh too (it bends with the needles, so they never slide off it in the wind),
            // and inside the crown it's never thicker than the needles round it can hide: it can't show through them.
            var shape = PineShapeOf(seed, h, leafR);
            var barkMat = WorldLook.Bark;
            // the trunk collider: as tall as the trunk inside the needles, as thick as the bark where you hit it
            trunk.transform.localPosition = new Vector3(0, shape.TrunkTop * 0.5f, 0);
            trunk.transform.localScale = new Vector3(0.6f, shape.TrunkTop * 0.5f / CylinderHalfHeight, 0.6f);
            var trunkCap = trunk.GetComponent<CapsuleCollider>();
            if (trunkCap != null)
            {
                trunkCap.direction = 1;
                trunkCap.center = Vector3.zero;
                trunkCap.radius = TrunkR / 0.6f;
                trunkCap.height = CylinderHalfHeight * 2f;
            }
            var needles = new MeshKit();
            var barkKit = new MeshKit();
            var spinRng = new System.Random(seed ^ 0x3c6ef372);
            for (int k = 0; k < shape.Tiers; k++) PineTier(needles, shape, k, (float)spinRng.NextDouble() * Mathf.PI * 2f);
            trunk.GetComponent<MeshRenderer>().enabled = false;
            PineTrunk(barkKit, shape, (float)spinRng.NextDouble() * Mathf.PI * 2f);
            if (shape.Variant == 4) DeadBranches(barkKit, shape, spinRng);
            MeshKit.Spawn(tr, "needles", new[] { foliage, barkMat }, true, needles, barkKit);
            trunkRadius = 0.3f; // (the X goes on the collider, which is the bark you see)
            leafColor = shape.Needles * GameSettings.WorldTint(GameSettings.WorldColor.Leaves);
            leafColor.a = 1f;
            return trunk;
        }

        /// <summary>The built-in cylinder's half height (the trunk collider's mesh).</summary>
        static float CylinderHalfHeight => Mathf.Max(0.01f, Art.Cylinder.bounds.extents.y);

        /// <summary>How thick the pines' trunks are where you hit them (and their collider): 1.2 m across, as before.</summary>
        public const float TrunkR = 0.6f;
        const int TrunkSides = 10;

        // =====================================================================
        // Normal graphics: the pines (five kinds) and the trunk inside them
        // =====================================================================

        public const int PineVariants = 5;
        static readonly string[] k_PineNames = { "classic", "spruce", "fir", "droopy", "scraggly" };
        public static string PineVariantName(int k) => k >= 0 && k < k_PineNames.Length ? k_PineNames[k] : "?";

        /// <summary>Which kind of pine a tree is: from its seed alone, so every peer builds the same one.</summary>
        public static int PineVariant(int seed)
        {
            uint x = (uint)seed * 2654435761u;
            x ^= x >> 15;
            x *= 0x2c1b3c6du;
            x ^= x >> 12;
            return (int)(x % PineVariants);
        }

        /// <summary>
        /// One Normal pine, in the tree's own space (0 = the ground): its tiers of needles - each a closed shell: a
        /// star-shaped skirt of branch tips, a ring part way up and the tip, with a shallow cone underneath - and the trunk.
        /// </summary>
        public class PineShape
        {
            public int Variant, Tiers, Sides;
            public float Top, CanopyBottom, TrunkTop, SwayAmp;
            /// <summary>The notches between the branch tips (radius, as a share of the tier's; and how far up they are, of its height).</summary>
            public float Notch, NotchUp;
            /// <summary>The ring part way up (its height and radius as shares of the tier's) and the underside's centre (height share).</summary>
            public float MidT, MidR, Under;
            /// <summary>Each tier: the branch tips' height, the height from there to its tip, its radius.</summary>
            public float[] Y, H, R;
            public Color Needles, Bark;

            public float Apex(int k) => Y[k] + H[k];
            public float UnderY(int k) => Y[k] + H[k] * Under;
            public float MidY(int k) => Y[k] + H[k] * MidT;
            /// <summary>(tests) where the trunk goes up into the top tier.</summary>
            public float JunctionY => UnderY(Tiers - 1);

            /// <summary>How much a vertex at height y sways (uv0.x for the Painted shader's foliage wind): nothing below
            /// the needles, the most at the top (taller trees a little more). The trunk and the needles use the same.</summary>
            public float Sway(float y) => Mathf.Pow(Mathf.Clamp01((y - CanopyBottom) / (Top - CanopyBottom)), 1.6f) * SwayAmp;

            /// <summary>The largest circle inside the skirt's star of branch tips (radius 1) and notches.</summary>
            public float StarIn
            {
                get
                {
                    float a = Mathf.PI / Sides;
                    var p = new Vector2(1f, 0f);
                    var q = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Notch;
                    var d = q - p;
                    float t = Mathf.Clamp01(-Vector2.Dot(p, d) / d.sqrMagnitude);
                    return (p + d * t).magnitude;
                }
            }

            /// <summary>The radius of a circle round the axis at height y that's inside tier k's solid part
            /// (-1: y isn't in it - below the centre of its underside or above its tip).</summary>
            public float Cover(int k, float y)
            {
                if (y < UnderY(k) + 0.07f || y > Apex(k)) return -1f;
                float mid = MidR * R[k] * Mathf.Cos(Mathf.PI / Sides);
                if (y <= MidY(k)) return Mathf.Min(StarIn * R[k], mid);
                return mid * (Apex(k) - y) / Mathf.Max(0.01f, Apex(k) - MidY(k));
            }

            /// <summary>The trunk's radius at height y: a flared foot, TrunkR where you hit it, tapering going up - and
            /// inside the crown never thicker than the needles round it can hide (less a margin for the branch tips'
            /// flutter), so it can't show through them.</summary>
            public float TrunkRadius(float y)
            {
                float r = y < 0.3f ? Mathf.Lerp(TrunkR * 1.18f, TrunkR, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((y + 0.3f) / 0.6f)))
                        : y < 1.8f ? TrunkR
                        : TrunkR * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((y - 1.8f) / Mathf.Max(1f, Top - 1.8f)));
                if (y < UnderY(0) + 0.07f) return r;
                float cover = -1f;
                for (int k = 0; k < Tiers; k++) cover = Mathf.Max(cover, Cover(k, y));
                return Mathf.Clamp(cover - 0.11f, 0.05f, r);
            }
        }

        /// <summary>The pine a tree with this seed gets in Normal graphics.</summary>
        public static PineShape PineShapeOf(int seed)
        {
            var rng = new System.Random(seed);
            float h = 4.5f + (float)rng.NextDouble() * 2.5f;
            float leafR = (float)rng.NextDouble();
            return PineShapeOf(seed, h, leafR);
        }

        /// <summary>h: the tree's old trunk height (4.5 .. 7 m, from its seed), leafR: its old leaf colour pick.</summary>
        static PineShape PineShapeOf(int seed, float h, float leafR)
        {
            int v = PineVariant(seed);
            var rng = new System.Random(seed * 7919 + 17); // (numbers of its own: the tree's old ones stay as they were for PSX)
            float r() => (float)rng.NextDouble();
            float u = Mathf.InverseLerp(4.5f, 7f, h);
            var p = new PineShape { Variant = v };
            // 10 .. 16 m (they were 7 .. 11)
            float top = Mathf.Lerp(10f, 16f, u);
            float cb, rad, radStep, hf, hStep;
            Color c0, c1;
            switch (v)
            {
                default: // classic: four even tiers
                    p.Tiers = 4; p.Sides = 8; cb = 2.7f + top * 0.06f; rad = 0.19f; radStep = 0.17f; hf = 0.45f; hStep = 0.03f;
                    p.MidT = 0.28f; p.MidR = 0.7f; p.Notch = 0.8f; p.NotchUp = 0.05f; p.Under = 0.1f;
                    c0 = new Color(0.24f, 0.5f, 0.13f); c1 = new Color(0.33f, 0.6f, 0.16f);
                    break;
                case 1: // spruce: slim and tall, six narrow tiers, a little bluer
                    top *= 1.04f;
                    p.Tiers = 6; p.Sides = 9; cb = 2.9f + top * 0.07f; rad = 0.13f; radStep = 0.11f; hf = 0.3f; hStep = 0.012f;
                    p.MidT = 0.3f; p.MidR = 0.72f; p.Notch = 0.8f; p.NotchUp = 0.05f; p.Under = 0.1f;
                    c0 = new Color(0.16f, 0.4f, 0.2f); c1 = new Color(0.22f, 0.48f, 0.24f);
                    break;
                case 2: // fir: full and bushy, five wide flared tiers starting low
                    top *= 0.95f;
                    p.Tiers = 5; p.Sides = 10; cb = 2.4f + top * 0.04f; rad = 0.23f; radStep = 0.14f; hf = 0.37f; hStep = 0.02f;
                    p.MidT = 0.24f; p.MidR = 0.62f; p.Notch = 0.82f; p.NotchUp = 0.04f; p.Under = 0.1f;
                    c0 = new Color(0.22f, 0.48f, 0.15f); c1 = new Color(0.3f, 0.57f, 0.18f);
                    break;
                case 3: // droopy: the branch tips hang down (a bell-shaped skirt)
                    p.Tiers = 4; p.Sides = 10; cb = 2.8f + top * 0.05f; rad = 0.2f; radStep = 0.16f; hf = 0.44f; hStep = 0.03f;
                    p.MidT = 0.34f; p.MidR = 0.8f; p.Notch = 0.87f; p.NotchUp = 0.08f; p.Under = 0.14f;
                    c0 = new Color(0.2f, 0.44f, 0.14f); c1 = new Color(0.28f, 0.53f, 0.17f);
                    break;
                case 4: // scraggly: sparser, uneven tiers higher up an older trunk with dead branches, olive needles
                    top *= 0.97f;
                    p.Tiers = 4; p.Sides = 7; cb = 3.4f + top * 0.09f; rad = 0.155f; radStep = 0.13f; hf = 0.38f; hStep = 0.02f;
                    p.MidT = 0.28f; p.MidR = 0.68f; p.Notch = 0.74f; p.NotchUp = 0.05f; p.Under = 0.1f;
                    c0 = new Color(0.3f, 0.45f, 0.15f); c1 = new Color(0.38f, 0.5f, 0.18f);
                    break;
            }
            p.Top = Mathf.Min(top, 16.5f);
            p.CanopyBottom = cb;
            p.SwayAmp = p.Top / 11f;
            int T = p.Tiers, last = T - 1;
            p.Y = new float[T]; p.H = new float[T]; p.R = new float[T];
            float span = p.Top - cb;
            for (int k = 0; k < T; k++)
            {
                p.H[k] = span * (hf - hStep * k);
                p.R[k] = p.Top * rad * (1f - radStep * k) * (v == 4 ? Mathf.Lerp(0.85f, 1.15f, r()) : 1f);
            }
            p.Y[last] = p.Top - p.H[last];
            for (int k = 0; k < last; k++) p.Y[k] = Mathf.Lerp(cb, p.Y[last], k / (float)last);
            // every tier reaches well up into the next one: still wide enough at the next one's underside to hide the
            // trunk going up between them (taller where it isn't)
            for (int k = last - 1; k >= 0; k--)
                for (int it = 0; it < 40 && p.Cover(k, p.UnderY(k + 1) + 0.05f) < 0.3f; it++) p.H[k] *= 1.04f;
            // the trunk stops a little way up inside the top tier
            p.TrunkTop = p.UnderY(last) + (p.Apex(last) - p.UnderY(last)) * 0.3f;
            // a touch of colour of its own
            var pine = Color.Lerp(c0, c1, leafR);
            Color.RGBToHSV(pine, out float hue, out float sat, out float val);
            pine = Color.HSVToRGB(Mathf.Repeat(hue + (r() - 0.5f) * 0.03f, 1f), sat * Mathf.Lerp(0.92f, 1.06f, r()), val * Mathf.Lerp(0.9f, 1.08f, r()));
            pine.a = 1f;
            p.Needles = ThemeMaps.LeafTint(pine); // THEME MAPS (tint)
            p.Bark = Color.Lerp(Art.DarkWood, Art.Wood, v == 4 ? 0.05f : 0.15f) * Mathf.Lerp(0.92f, 1.06f, r());
            p.Bark.a = 1f;
            return p;
        }

        /// <summary>Tier k of a pine: a star of branch tips and notches, a ring part way up and the tip, and a shallow
        /// cone underneath (a closed shell). uv0: x how much it sways, y how much the branch tips flutter.</summary>
        static void PineTier(MeshKit kit, PineShape p, int k, float spin)
        {
            int S = p.Sides;
            float y0 = p.Y[k], H = p.H[k], R = p.R[k];
            float shade = Mathf.Lerp(0.88f, 1.06f, k / (float)Mathf.Max(1, p.Tiers - 1));
            var pine = p.Needles;
            Color rim = pine * (0.8f * shade), mid = pine * (0.95f * shade), tip = Color.Lerp(pine * 1.2f, new Color(0.62f, 0.8f, 0.3f), 0.18f) * shade, under = pine * 0.62f;
            rim.a = mid.a = tip.a = under.a = 1f;
            var apex = new Vector3(0, p.Apex(k), 0);
            var uc = new Vector3(0, p.UnderY(k), 0);
            Vector2 W(Vector3 v, float edge) => new Vector2(p.Sway(v.y), edge);
            Vector3 At(float a, float r, float y) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            for (int s = 0; s < S; s++)
            {
                float a0 = spin + s * Mathf.PI * 2f / S, a1 = a0 + Mathf.PI * 2f / S, am = a0 + Mathf.PI / S;
                var t0 = At(a0, R, y0);
                var t1 = At(a1, R, y0);
                var n0 = At(am, R * p.Notch, y0 + H * p.NotchUp);
                var m0 = At(a0, R * p.MidR, p.MidY(k));
                var m1 = At(a1, R * p.MidR, p.MidY(k));
                kit.Tri(t0, m0, n0, rim, mid, rim * 1.04f, W(t0, 1f), W(m0, 0.3f), W(n0, 0.7f));
                kit.Tri(n0, m0, m1, rim * 1.04f, mid, mid, W(n0, 0.7f), W(m0, 0.3f), W(m1, 0.3f));
                kit.Tri(n0, m1, t1, rim * 1.04f, mid, rim, W(n0, 0.7f), W(m1, 0.3f), W(t1, 1f));
                kit.Tri(m0, apex, m1, mid, tip, mid, W(m0, 0.3f), W(apex, 0f), W(m1, 0.3f));
                // underneath
                kit.Tri(n0, uc, t0, under, under, under, W(n0, 0.7f), W(uc, 0f), W(t0, 1f));
                kit.Tri(t1, uc, n0, under, under, under, W(t1, 1f), W(uc, 0f), W(n0, 0.7f));
            }
        }

        /// <summary>The trunk: a ten-sided, flat-shaded log from just under the ground up into the top tier, swaying with
        /// the needles. Each ring is no thicker than the trunk may be anywhere between its neighbours, so the straight
        /// sides between rings stay inside the needles too.</summary>
        static void PineTrunk(MeshKit kit, PineShape p, float spin)
        {
            var ys = new List<float> { -0.3f, 0f, 0.3f, 1.8f, p.CanopyBottom * 0.6f };
            for (float y = 2.4f; y < p.TrunkTop; y += 0.5f) ys.Add(y);
            for (int k = 0; k < p.Tiers; k++) { ys.Add(p.UnderY(k) + 0.06f); ys.Add(p.UnderY(k) + 0.075f); }
            ys.Add(p.TrunkTop);
            ys.RemoveAll(y => y > p.TrunkTop + 1e-4f);
            ys.Sort();
            for (int i = ys.Count - 1; i > 0; i--) if (ys[i] - ys[i - 1] < 0.004f) ys.RemoveAt(i);
            var rs = new float[ys.Count];
            for (int i = 0; i < ys.Count; i++)
            {
                float lo = i > 0 ? ys[i - 1] : ys[i], hi = i + 1 < ys.Count ? ys[i + 1] : ys[i];
                float r = p.TrunkRadius(ys[i]);
                for (int s = 0; s <= 12; s++) r = Mathf.Min(r, p.TrunkRadius(Mathf.Lerp(lo, hi, s / 12f)));
                // (down to the ground it's TrunkR or more, so the X and the collider sit on it)
                rs[i] = ys[i] < 1.8f ? p.TrunkRadius(ys[i]) : r;
            }
            float circ = 1f / Mathf.Cos(Mathf.PI / TrunkSides); // (flat sides where the collider is)
            Vector3 P(int i, int s) { float a = spin + s * Mathf.PI * 2f / TrunkSides; return new Vector3(Mathf.Cos(a) * rs[i] * circ, ys[i], Mathf.Sin(a) * rs[i] * circ); }
            Vector2 W(Vector3 v) => new Vector2(p.Sway(v.y), 0f);
            for (int i = 0; i + 1 < ys.Count; i++)
            {
                float dark = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(ys[i] / 2.5f));
                for (int s = 0; s < TrunkSides; s++)
                {
                    // bark: each side a slightly different shade, darker at the foot
                    var c = p.Bark * (dark * (0.9f + 0.1f * ((s * 7 + i * 3) % 3)));
                    c.a = 1f;
                    Vector3 a = P(i, s), b = P(i, s + 1), cc = P(i + 1, s + 1), d = P(i + 1, s);
                    kit.Tri(a, d, b, c, c, c, W(a), W(d), W(b));
                    kit.Tri(b, d, cc, c, c, c, W(b), W(d), W(cc));
                }
            }
            // the top, inside the needles
            int top = ys.Count - 1;
            var centre = new Vector3(0, ys[top], 0);
            for (int s = 0; s < TrunkSides; s++) kit.Tri(P(top, s), centre, P(top, s + 1), p.Bark, p.Bark, p.Bark, W(P(top, s)), W(centre), W(P(top, s + 1)));
        }

        /// <summary>The scraggly pine's bare, broken-off branches on the trunk below the needles.</summary>
        static void DeadBranches(MeshKit kit, PineShape p, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var dead = new Color(0.42f, 0.36f, 0.3f);
            int n = 3 + rng.Next(2);
            float yMax = p.Y[0] - 0.5f;
            for (int i = 0; i < n; i++)
            {
                float y = Mathf.Lerp(2.3f, yMax, (i + R() * 0.6f) / n);
                if (y > yMax) continue;
                float a = R() * Mathf.PI * 2f, el = Mathf.Lerp(12f, 32f, R()) * Mathf.Deg2Rad;
                float len = Mathf.Lerp(0.7f, 1.3f, R());
                var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
                // (well under the lowest tier)
                len = Mathf.Min(len, (yMax + 0.3f - y) / Mathf.Max(0.05f, dir.y));
                var root = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * p.TrunkRadius(y) * 0.8f + Vector3.up * y;
                var end = root + dir * len;
                var side = Vector3.Cross(dir, Vector3.up).normalized;
                var up = Vector3.Cross(side, dir).normalized;
                float w0 = 0.075f, w1 = 0.018f;
                var b = new[] { root + side * w0, root + up * w0, root - side * w0, root - up * w0 };
                var e = new[] { end + side * w1, end + up * w1, end - side * w1, end - up * w1 };
                for (int s = 0; s < 4; s++)
                {
                    int t = (s + 1) % 4;
                    var c = dead * (0.85f + 0.08f * s);
                    c.a = 1f;
                    kit.Tri(b[s], e[s], b[t], c, c, c, new Vector2(p.Sway(b[s].y), 0), new Vector2(p.Sway(e[s].y), 0), new Vector2(p.Sway(b[t].y), 0));
                    kit.Tri(b[t], e[s], e[t], c, c, c, new Vector2(p.Sway(b[t].y), 0), new Vector2(p.Sway(e[s].y), 0), new Vector2(p.Sway(e[t].y), 0));
                }
                for (int s = 1; s < 3; s++) kit.Tri(e[0], e[s + 1], e[s], dead, dead, dead, new Vector2(p.Sway(e[0].y), 0), new Vector2(p.Sway(e[s + 1].y), 0), new Vector2(p.Sway(e[s].y), 0));
            }
        }

        /// <summary>The tree a hit at `pos` landed on (for the chip colours), or null.</summary>
        public static ResourceNode TreeNear(Vector3 pos, float range = 4f)
        {
            ResourceNode best = null;
            float bd = range * range;
            foreach (var n in All)
            {
                if (n == null || n.Kind.Value != Tree) continue;
                var d = n.transform.position - pos;
                d.y = 0;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = n; }
            }
            return best;
        }

        /// <summary>Airstrike: flattened, regrows later like any empty node.</summary>
        public void ServerDeplete()
        {
            if (IsBush) { NetworkObject.Despawn(true); return; }
            if (Amount.Value <= 0) return;
            Amount.Value = 0;
            m_RespawnAt = Time.time + Cfg.NodeRespawnTime;
        }

        /// <summary>Local-space direction and height of weak spot `i` (a ring around the trunk / rock).</summary>
        static void SpotDir(byte kind, int i, out Vector3 dir, out float y)
        {
            float a = i * (360f / SpotCount) * Mathf.Deg2Rad;
            dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            y = kind == Tree ? 0.8f + (i % 3) * 0.35f : 0.45f + (i % 3) * 0.2f;
        }

        /// <summary>World position/normal of the current weak spot (on the actual surface).</summary>
        public bool TryGetSpot(out Vector3 pos, out Vector3 normal) => TryGetSpot(out pos, out normal, out _);

        /// <summary>onVisual: the point is on the PSX trunk you see (not the plain cylinder), so the X needs no extra offset.</summary>
        bool TryGetSpot(out Vector3 pos, out Vector3 normal, out bool onVisual)
        {
            pos = normal = default;
            onVisual = false;
            if (IsBush || m_SpotCollider == null || Amount.Value <= 0 || Spot.Value == NoSpot) return false;
            SpotDir(Kind.Value, Spot.Value, out var dir, out var y);
            var worldDir = transform.rotation * dir;
            // PSX trees: right on the trunk model's surface (it can be thinner, thicker or off to one side of the cylinder)
            if (m_PsxTrunk != null && m_PsxTrunk.Hit(transform.position.y + y, worldDir, out pos, out normal)) { onVisual = true; return true; }
            var center = transform.position + Vector3.up * (y * (Kind.Value == Boulder ? m_Visual.transform.localScale.y : 1f));
            var ray = new Ray(center + worldDir * 4f, -worldDir);
            if (!m_SpotCollider.Raycast(ray, out var hit, 8f)) return false;
            pos = hit.point;
            normal = hit.normal;
            return true;
        }

        /// <summary>For the tests: how the X is drawn on this tree.</summary>
        public string XDebug()
        {
            var tris = m_PsxTrunk != null && m_PsxTrunk.Model != null ? m_PsxTrunk.Model.TrunkTris.Count / 3 : -1;
            var dv = m_Decal != null && m_Decal.sharedMesh != null ? m_Decal.sharedMesh.vertexCount : -1;
            TryGetSpot(out var p, out var n, out bool vis);
            return $"model {(m_PsxTrunk != null && m_PsxTrunk.Model != null ? m_PsxTrunk.Model.Prefab.name : "none")} trunk tris {tris} onVisual {vis} decal verts {dv} decal active {(m_Decal != null && m_Decal.gameObject.activeInHierarchy)} marker active {(m_Marker != null && m_Marker.gameObject.activeInHierarchy)}";
        }

        public bool IsWeakSpotHit(Vector3 point, float tolerance)
        {
            return TryGetSpot(out var p, out _) && Vector3.Distance(p, point) <= tolerance;
        }

        /// <summary>A swing at this node counts on the X if it lands near it, or if you were aiming right at it (PSX trees: the X is on the trunk you see).</summary>
        public bool IsWeakSpotAimed(Ray aim, Vector3 point, float tolerance)
        {
            if (!TryGetSpot(out var p, out _)) return false;
            if (Vector3.Distance(p, point) <= tolerance) return true;
            var to = p - aim.origin;
            float along = Vector3.Dot(to, aim.direction);
            return along > 0f && (to - aim.direction * along).magnitude <= 0.3f && Vector3.Distance(p, point) <= 0.75f;
        }

        void PlaceMarker()
        {
            if (m_Marker == null) return;
            if (!TryGetSpot(out var p, out var n, out bool onVisual)) { m_Marker.gameObject.SetActive(false); return; }
            m_Marker.gameObject.SetActive(true);
            m_Marker.position = p + n * (onVisual ? 0.03f : 0.015f + m_MarkerOut);
            m_Marker.rotation = Quaternion.LookRotation(-n);
            // PSX trunks: the X is printed onto the bark (a decal following the trunk), not a card in front of it
            if (onVisual && m_PsxTrunk != null)
            {
                m_Marker.position = p;
                if (m_Decal == null)
                {
                    var dg = new GameObject("x decal");
                    dg.transform.SetParent(m_Marker, false);
                    dg.AddComponent<MeshFilter>();
                    var dr = dg.AddComponent<MeshRenderer>();
                    dr.sharedMaterial = PsxArt.XMaterial;
                    dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    m_Decal = dg.GetComponent<MeshFilter>();
                }
                // as big as the trunk is wide (a big PSX tree gets a big X)
                float size = Mathf.Clamp(m_TrunkR * 1.5f, 0.4f, 1.1f);
                var mesh = m_PsxTrunk.Decal(p, n, size, m_Decal.transform, m_Decal.sharedMesh);
                m_Decal.sharedMesh = mesh;
                foreach (Transform c in m_Marker) if (c != m_Decal.transform) c.gameObject.SetActive(mesh == null);
                m_Decal.gameObject.SetActive(mesh != null);
            }
        }

        void Update()
        {
            if (m_Grow < 1f && m_Visual)
            {
                m_Grow = Mathf.Min(1f, m_Grow + Time.deltaTime / 1.6f);
                float e = 1f - (1f - m_Grow) * (1f - m_Grow) * (1f - m_Grow);
                m_Visual.transform.localPosition = m_VisualBase + Vector3.down * (1f - e) * 1.1f;
                m_Visual.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, e);
                if (m_Grow < 0.35f && Random.value < 0.3f) FxParticle.Spawn(transform.position + Random.insideUnitSphere * 0.5f, Vector3.up * 2f + Random.insideUnitSphere, new Color(0.35f, 0.26f, 0.16f), 0.08f, 0.6f, 12f, true);
                return;
            }
            if (m_Shake > 0 && m_Visual)
            {
                m_Shake -= Time.deltaTime;
                float k = Mathf.Max(0, m_Shake) * 0.4f;
                m_Visual.transform.localPosition = m_VisualBase + new Vector3(Mathf.Sin(Time.time * 70f) * k, 0, Mathf.Cos(Time.time * 55f) * k);
                PlaceMarker();
            }
            else if (m_Visual && m_Visual.transform.localPosition != m_VisualBase)
            {
                m_Visual.transform.localPosition = m_VisualBase;
                PlaceMarker();
            }

            if (m_Marker != null && m_Marker.gameObject.activeSelf)
            {
                // pop in when it moves, then pulse (X) / twinkle (star)
                m_MarkerPop = Mathf.Min(1f, m_MarkerPop + Time.deltaTime * 5f);
                float pop = 1f + (1f - m_MarkerPop) * 1.2f;
                float pulse = Kind.Value == Tree ? 1f + Mathf.Sin(Time.time * 6f) * 0.08f : 0.85f + Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 0.35f;
                m_Marker.localScale = Vector3.one * pop * pulse;
                if (Kind.Value == Boulder) m_Marker.rotation *= Quaternion.Euler(0, 0, Time.deltaTime * 90f);
            }

            if (IsServer && !IsBush && Amount.Value <= 0 && Time.time >= m_RespawnAt)
            {
                Amount.Value = MaxAmount;
                Spot.Value = NoSpot;
            }
        }

        // ---------------- Server ----------------
        public void ServerInit(byte kind, int seed)
        {
            Kind.Value = kind;
            Seed.Value = seed;
            Spot.Value = NoSpot;
            Amount.Value = kind == Tree ? Cfg.TreeAmount : kind == Boulder ? Cfg.StoneAmount : 1;
        }

        /// <summary>Dev setting: full again right now.</summary>
        public void ServerRegrow()
        {
            Amount.Value = MaxAmount;
            Spot.Value = NoSpot;
        }

        /// <summary>Returns how much was actually harvested. The first hit reveals the weak spot; a weak-spot hit multiplies the yield and moves it.</summary>
        public int ServerHarvest(int want, bool weak, Vector3 hitterPos)
        {
            if (Amount.Value <= 0) return 0;
            bool first = Spot.Value == NoSpot;
            if (first) weak = false;
            if (weak) want = Mathf.RoundToInt(want * Cfg.WeakSpotMul);
            int got = Mathf.Min(want, Amount.Value);
            Amount.Value -= got;
            if (Amount.Value <= 0) m_RespawnAt = Time.time + Cfg.NodeRespawnTime;
            if (IsBush && Amount.Value <= 0)
            {
                // a new bush grows somewhere else in this half of the map later
                if (NetGame.Instance != null && TrapTeam.Value == NoTrap) NetGame.Instance.ServerScheduleBush(transform.position);
                NetworkObject.Despawn(true);
                return got;
            }
            if ((weak || first) && !IsBush && Amount.Value > 0) Spot.Value = PickSpotFacing(hitterPos);
            return got;
        }

        /// <summary>New weak spot on the side facing the player (like Rust), but never the same one.</summary>
        byte PickSpotFacing(Vector3 hitterPos)
        {
            var toHitter = Quaternion.Inverse(transform.rotation) * (hitterPos - transform.position);
            toHitter.y = 0;
            float baseAng = Mathf.Atan2(toHitter.z, toHitter.x) * Mathf.Rad2Deg;
            for (int tries = 0; tries < 10; tries++)
            {
                float a = baseAng + Random.Range(-70f, 70f);
                int i = Mathf.RoundToInt(Mathf.Repeat(a, 360f) / (360f / SpotCount)) % SpotCount;
                if (i != Spot.Value) return (byte)i;
            }
            return (byte)((Spot.Value + 1) % SpotCount);
        }
    }
}
