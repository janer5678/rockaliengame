using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A tree (wood), fallen log (wood), boulder (stone) or berry bush. Trees, logs and boulders have a Rust-style weak
    /// spot (a glowing X on trees and logs, a sparkle on rocks) that appears after the first hit, gives bonus resources
    /// and jumps somewhere else when hit. Hits leave marks on the bark. Depleted nodes regrow later (a felled tree
    /// leaves its stump until then). A berry bush is picked whole (E): it disappears and a new one grows up out of the
    /// ground somewhere else in the same half a while later.
    /// </summary>
    public class ResourceNode : NetworkBehaviour
    {
        public const byte Tree = 0, Boulder = 1, Bush = 2, Log = 3;
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
        /// <summary>Trees: how many birds are sitting in it (0 = none). A flock lands here after flying out of a tree that
        /// was hit, and flies on to another tree when this one is hit (Birds.cs).</summary>
        public readonly NetworkVariable<byte> Birds = new NetworkVariable<byte>();

        /// <summary>Every node in the match (to find the tree a hit landed on, and to swap graphics modes).</summary>
        public static readonly System.Collections.Generic.List<ResourceNode> All = new System.Collections.Generic.List<ResourceNode>();

        GameObject m_Visual;
        Transform m_Marker;
        /// <summary>A PSX trunk can be thicker or thinner than the (unchanged) trunk collider: the X moves out / in by this much, onto the bark you see.</summary>
        float m_MarkerOut;
        PsxArt.Trunk m_PsxTrunk;
        /// <summary>Normal pines: the bark you see round the weak spots' heights (triangles, in the visual's space) - the X
        /// goes on these, in front of the bark's ten corners, not on the round collider inside them.</summary>
        List<Vector3> m_BarkTris;
        int m_BarkSpotFor = -1;             // (which spot m_BarkSpotAt was worked out for)
        bool m_BarkSpotOk;
        Vector3 m_BarkSpotAt;
        /// <summary>Normal pines: the tree's shape (where leaves fall from when it's hit).</summary>
        PineShape m_Pine;
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
        /// <summary>A felled tree's stump (shown until the tree grows back).</summary>
        GameObject m_Stump;
        /// <summary>Fallen logs: the log itself (in the visual, tipped to lie along the ground), its length and radius.</summary>
        Transform m_LogT;
        float m_LogLen, m_LogR;
        /// <summary>The marks hits have left on the bark (trees and logs; children of the visual, so they shake with it).</summary>
        readonly List<GameObject> m_Marks = new List<GameObject>();
        public int HitMarkCount => m_Marks.Count;
        /// <summary>(tests) the newest hit mark.</summary>
        public Transform LastHitMark => m_Marks.Count > 0 && m_Marks[m_Marks.Count - 1] != null ? m_Marks[m_Marks.Count - 1].transform : null;
        public GameObject Stump => m_Stump;
        public Transform LogTransform => m_LogT;

        public bool IsBush => Kind.Value == Bush;
        /// <summary>A tree or a fallen log: gives wood, has the X, chips and hit marks.</summary>
        public bool IsWood => Kind.Value == Tree || Kind.Value == Log;
        public static bool WoodKind(byte k) => k == Tree || k == Log;
        public int MaxAmount => AmountOf(Kind.Value);
        static int AmountOf(byte k) => k == Tree ? Cfg.TreeAmount : k == Log ? Mathf.Max(1, Cfg.TreeAmount / 2) : k == Boulder ? Cfg.StoneAmount : 1;
        public string DisplayName => Kind.Value == Tree ? "Tree" : Kind.Value == Log ? "Fallen Log" : Kind.Value == Boulder ? "Stone" : "Berry Bush";
        public Item Yield => IsWood ? Item.Wood : Kind.Value == Boulder ? Item.Stone : Item.Berry;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            GameSettings.GraphicsChanged += OnGraphicsChanged;
            BuildVisual();
            if (IsBush) m_Grow = 0f; // bushes come up out of the ground
            Amount.OnValueChanged += OnAmountChanged;
            Spot.OnValueChanged += OnSpotChanged;
            Birds.OnValueChanged += OnBirdsChanged;
            RefreshState();
            PlaceMarker();
            if (Birds.Value > 0) BirdFlock.Perch(this, Birds.Value); // (joined while birds sat in it)
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            GameSettings.GraphicsChanged -= OnGraphicsChanged;
            Amount.OnValueChanged -= OnAmountChanged;
            Spot.OnValueChanged -= OnSpotChanged;
            Birds.OnValueChanged -= OnBirdsChanged;
            GrassField.Unblock(GetInstanceID());
            BirdFlock.NodeGone(this);
        }

        void OnBirdsChanged(byte prev, byte cur)
        {
            if (cur > 0) BirdFlock.Perch(this, cur); // (nothing if the flock flying here is already on its way)
            else BirdFlock.Orphan(this);              // (they fly off: the take-off message says where to)
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
            if (Kind.Value == Log) LogGrass(alive);
            if (!alive) ClearHitMarks();
            if (m_Stump != null && m_Stump.activeSelf == alive) m_Stump.SetActive(!alive);
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
            if (!IsWood) { if (AiPsxArt.On) AiPsxArt.Apply(m_Visual.transform); return; }
            var old = m_Visual;
            old.SetActive(false);
            Destroy(old);
            m_Marks.Clear(); // (they went with the old visual)
            if (m_Stump != null) { Destroy(m_Stump); m_Stump = null; }
            BuildVisual();
            RefreshState();
            PlaceMarker();
            // (the birds sitting in it move to the new tree's branches)
            if (Birds.Value > 0) { BirdFlock.NodeGone(this); BirdFlock.Perch(this, Birds.Value); }
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
                var needles = tr.Find("needles");
                m_Pine = needles != null ? PineShapeOf(Seed.Value) : null;
                m_BarkTris = needles != null ? BarkAround(needles, 0.2f, MarkTop + 0.2f) : null;
                m_BarkSpotFor = -1;
                // the X (a chunky pixel-art one on PSX trees)
                m_Marker = new GameObject("x").transform;
                m_Marker.SetParent(tr, false);
                if ((PsxArt.On || AiPsxArt.On) && trunk.GetComponent<MeshRenderer>() != null && !trunk.GetComponent<MeshRenderer>().enabled) PsxArt.PixelX(m_Marker);
                else GlowX(m_Marker);
                // the stump it leaves when it's felled (hidden while the tree stands)
                m_Stump = BuildStump(transform, Seed.Value, m_PsxTrunk != null || m_Pine == null ? Mathf.Clamp(trunkR, 0.2f, 1.2f) : TrunkR, m_Pine != null ? m_Pine.Bark : Bark, m_Pine != null && !PsxArt.On && !AiPsxArt.On);
            }
            else if (Kind.Value == Log)
            {
                BuildLog(tr);
                m_BarkSpotFor = -1;
                m_Marker = new GameObject("x").transform;
                m_Marker.SetParent(tr, false);
                if (PsxArt.On || AiPsxArt.On) PsxArt.PixelX(m_Marker);
                else GlowX(m_Marker);
            }
            else if (Kind.Value == Boulder)
            {
                var mesh = Art.MakeRock(Seed.Value, 0.28f);
                Color c = Color.Lerp(Art.Stone, new Color(0.45f, 0.44f, 0.42f), r());
                var stoneTint = ColorSlots.Use(ColorSlots.StoneNodes);
                const float S = BoulderSize; // (bigger than they were: they read as rocks across the meadow)
                var main = Art.Part(tr, mesh, c, new Vector3(0, 0.6f * S, 0), new Vector3(1.6f, 1.2f, 1.4f) * S, new Vector3(r() * 30, r() * 360, r() * 20), false, null, "rock");
                var mc = main.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
                m_SpotCollider = mc;
                // (the little rock beside it is solid too - you used to be able to walk through it)
                Art.Part(tr, Art.MakeRock(Seed.Value + 7, 0.3f), c * 0.9f, new Vector3(0.9f, 0.3f, 0.5f) * S, Vector3.one * 0.6f * S, new Vector3(0, r() * 360, 0), true);
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
                    sc.center = new Vector3(0, 0.5f * BushSize, 0);
                    sc.radius = 0.8f * BushSize;
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
                    m_Berries[i] = Art.Part(tr, Art.Sphere, i % 4 == 0 ? ItemModels.Berry * 0.8f : ItemModels.Berry, p, Vector3.one * (0.12f + r() * 0.05f) * BerrySize);
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
        /// Normal graphics' berry bush: a rounded clump of leafy blobs (round balls, darker underneath and lighter on
        /// top, each a slightly different green) with clusters of berries sitting on them. One mesh, two draws (leaves,
        /// berries), swaying in the same gusts as the pines and the grass (more at the top, the outside fluttering).
        /// Smooth-shaded (the light rolls round each blob; the flat bottoms keep their edge) and BushSize times as big
        /// as the old bush.
        /// </summary>
        static void BuildBush(Transform tr, int seed, Color leaf)
        {
            var rng = new System.Random(seed * 31 + 7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            MeshKit.Ico320(out var icoV, out var icoF);
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
                // a gently lumpy ball, flat where it meets the ground, its own shade of green - coloured per corner (by
                // which way that corner faces and how high it is), so the shading and the colour both run smoothly over it
                Color.RGBToHSV(leaf, out float hue, out float sat, out float val);
                var tint = Color.HSVToRGB(Mathf.Repeat(hue + R(-0.025f, 0.025f), 1f), sat * R(0.9f, 1.08f), val * R(0.88f, 1.12f));
                var pts = new Vector3[icoV.Length];
                var cols = new Color[icoV.Length];
                var fl = new float[icoV.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    var q = icoV[i] * r * R(0.95f, 1.05f);
                    q.y *= squash;
                    q += c;
                    q.y = Mathf.Max(q.y, ground);
                    pts[i] = q;
                    float ny = icoV[i].y;
                    var col = tint * (Mathf.Lerp(0.62f, 1.12f, ny * 0.5f + 0.5f) * Mathf.Lerp(0.8f, 1.05f, Mathf.Clamp01(q.y / 0.9f)));
                    col.a = 1f;
                    cols[i] = col;
                    fl[i] = ny < -0.5f ? 0f : 0.6f;
                }
                for (int i = 0; i < icoF.Length; i += 3)
                {
                    int ia = icoF[i], ib = icoF[i + 1], id = icoF[i + 2];
                    leaves.Tri(pts[ia], pts[ib], pts[id], cols[ia], cols[ib], cols[id], Sway(pts[ia], fl[ia]), Sway(pts[ib], fl[ib]), Sway(pts[id], fl[id]));
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
            // (bigger berries than they were - BerrySize - and a few fewer of them)
            int want = 15 + rng.Next(5), made = 0;
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
                    float br = R(0.065f, 0.085f) * BerrySize;
                    var bc = at + dir * br * 0.55f + (side * R(-1f, 1f) + up * R(-1f, 1f)) * 0.06f * BerrySize * (k == 0 ? 0f : 1f);
                    var berry = k == 0 && rng.NextDouble() < 0.3 ? ItemModels.Berry * 0.8f : ItemModels.Berry;
                    berry.a = 1f;
                    Berry(berries, bc, br, berry, Sway(bc, 0.6f));
                    made++;
                }
            }
            // smooth-shaded (the flat bottoms stay a crisp edge), and a bit bigger than the old bush
            leaves.SmoothNormals(70f);
            berries.SmoothNormals(80f);
            for (int i = 0; i < leaves.V.Count; i++) leaves.V[i] *= BushSize;
            for (int i = 0; i < berries.V.Count; i++) berries.V[i] *= BushSize;
            MeshKit.Spawn(tr, "bush", new[] { WorldLook.BushLeaves, WorldLook.Berries }, true, leaves, berries);
        }

        /// <summary>The Normal berry bush is this much bigger than the old one (about 2.2 m across, 1.4 m high).</summary>
        public const float BushSize = 1.25f;
        /// <summary>The berries on the bushes are this much bigger than they first were (radius 6.5 .. 8.5 cm then).</summary>
        public const float BerrySize = 1.45f;

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
                // (out to the bark's ten corners and a little past: the X sits on them, and aimed at side on it has to hit)
                trunkCap.radius = (TrunkR / Mathf.Cos(Mathf.PI / TrunkSides) + 0.02f) / 0.6f;
                trunkCap.height = CylinderHalfHeight * 2f;
            }
            var needles = new MeshKit();
            var barkKit = new MeshKit();
            var spinRng = new System.Random(seed ^ 0x3c6ef372);
            for (int k = 0; k < shape.Tiers; k++) PineTier(needles, shape, k, (float)spinRng.NextDouble() * Mathf.PI * 2f);
            trunk.GetComponent<MeshRenderer>().enabled = false;
            PineTrunk(barkKit, shape, (float)spinRng.NextDouble() * Mathf.PI * 2f);
            if (shape.Variant == 4) DeadBranches(barkKit, shape, spinRng);
            // smooth-shaded (a tier's rim over its underside, the trunk's top and the dead branches' edges stay sharp)
            needles.SmoothNormals(65f);
            barkKit.SmoothNormals(80f);
            MeshKit.Spawn(tr, "needles", new[] { foliage, barkMat }, true, needles, barkKit);
            trunkRadius = 0.3f; // (the X goes on the bark mesh itself: BarkSpot)
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

        /// <summary>The needles of the pine a tree with this seed gets (every tier, exactly as on the tree), added to `kit` in
        /// the tree's own space (0 = the ground): the scenery pines out past the map are these (MapScenery). `sides`: fewer
        /// branch tips round each tier (the far ones: the same shape with fewer faces; 0 = as on the tree).</summary>
        public static PineShape PineNeedles(MeshKit kit, int seed, int sides = 0)
        {
            var shape = PineShapeOf(seed);
            if (sides > 0) shape.Sides = Mathf.Min(shape.Sides, sides);
            var spinRng = new System.Random(seed ^ 0x3c6ef372);
            for (int k = 0; k < shape.Tiers; k++) PineTier(kit, shape, k, (float)spinRng.NextDouble() * Mathf.PI * 2f);
            return shape;
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

        /// <summary>The trunk: a ten-sided, smooth-shaded log from just under the ground up into the top tier, swaying with
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
            // bark: streaks of slightly different shades up and round it (blended corner to corner), darker at the foot
            Color C(int i, int s)
            {
                var c = p.Bark * (Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(ys[i] / 2.5f)) * (0.9f + 0.1f * (((s % TrunkSides) * 7 + i * 3) % 3)));
                c.a = 1f;
                return c;
            }
            for (int i = 0; i + 1 < ys.Count; i++)
                for (int s = 0; s < TrunkSides; s++)
                {
                    Vector3 a = P(i, s), b = P(i, s + 1), cc = P(i + 1, s + 1), d = P(i + 1, s);
                    Color ca = C(i, s), cb = C(i, s + 1), ccc = C(i + 1, s + 1), cd = C(i + 1, s);
                    kit.Tri(a, d, b, ca, cd, cb, W(a), W(d), W(b));
                    kit.Tri(b, d, cc, cb, cd, ccc, W(b), W(d), W(cc));
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

        // =====================================================================
        // The glowing X, stumps, fallen logs and the marks hits leave
        // =====================================================================

        static Material s_GlowMat, s_HaloMat;
        static Mesh s_HaloMesh;
        static bool s_GlowTried;
        /// <summary>The X's colour (sRGB) and how bright it glows (over 1 is HDR: past the bloom threshold).</summary>
        public static readonly Color XColour = new Color(1f, 0.42f, 0.08f);
        public const float XGlow = 1.6f;

        /// <summary>The X's glowing material (RockGame/Glow: unlit, HDR orange - as bright in the shade as in the sun, and
        /// it blooms with post processing on). Null if the shader isn't there (the X is then a plain orange).</summary>
        public static Material GlowMat
        {
            get
            {
                if (!s_GlowTried)
                {
                    s_GlowTried = true;
                    var sh = Resources.Load<Shader>("World/Glow");
                    if (sh != null && sh.isSupported)
                    {
                        s_GlowMat = new Material(sh) { name = "x glow" };
                        s_GlowMat.SetColor("_Color", GameSettings.TreeXColour);
                        s_GlowMat.SetFloat("_Intensity", GameSettings.TreeXGlow);
                    }
                    else Debug.LogWarning("[RockGame] World/Glow shader missing: the X won't glow");
                    var sun = Resources.Load<Shader>("World/Sun");
                    if (sun != null && sun.isSupported)
                    {
                        s_HaloMat = new Material(sun) { name = "x halo" };
                        s_HaloMat.SetColor("_Tint", Color.white);
                    }
                    GameSettings.TreeXChanged += ApplyXLook;
                }
                return s_GlowMat;
            }
        }

        /// <summary>Settings > Display > TREE X changed: the glow material, the halo and every X's place (its size).</summary>
        static void ApplyXLook()
        {
            if (s_GlowMat != null)
            {
                s_GlowMat.SetColor("_Color", GameSettings.TreeXColour);
                s_GlowMat.SetFloat("_Intensity", GameSettings.TreeXGlow);
            }
            if (s_HaloMesh != null) FillHalo(s_HaloMesh);
            foreach (var n in All)
                if (n != null && n.IsWood) { n.m_BarkSpotFor = -1; n.PlaceMarker(); }
        }

        /// <summary>The Normal X is drawn this many times its made size (Settings > Display > TREE X; PSX X's stay as they are).</summary>
        static float XScale => PsxArt.On || AiPsxArt.On ? 1f : GameSettings.TreeXSize;
        /// <summary>The X's half size right now (bigger X's are kept clear of the bark over more).</summary>
        static float XHalfNow => XHalf * XScale;

        /// <summary>The Normal X: two glowing bars and a soft orange halo round them on the bark (the halo shows as a glow
        /// even with post processing off).</summary>
        static void GlowX(Transform marker)
        {
            var glow = GlowMat;
            foreach (float z in new[] { 45f, -45f })
            {
                var bar = Art.Box(marker, GameSettings.TreeXColour, Vector3.zero, new Vector3(0.34f, 0.06f, 0.02f), new Vector3(0, 0, z), false, glow);
                bar.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (s_HaloMat == null) return;
            if (s_HaloMesh == null) s_HaloMesh = HaloMesh();
            var h = new GameObject("x halo");
            h.transform.SetParent(marker, false);
            h.transform.localPosition = new Vector3(0, 0, 0.012f); // (behind the bars, in front of the bark)
            h.AddComponent<MeshFilter>().sharedMesh = s_HaloMesh;
            var mr = h.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s_HaloMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>A soft round glow: bright in the middle fading out to nothing (vertex colours, linear; the Sun shader),
        /// in the X's colour. Shared by every X; FillHalo redoes it when the settings change.</summary>
        static Mesh HaloMesh()
        {
            var m = new Mesh { name = "x halo" };
            FillHalo(m);
            return m;
        }

        /// <summary>How far the halo reaches round the X at the default halo size (m; it was 0.3).</summary>
        public const float HaloRadius = 0.52f;

        static void FillHalo(Mesh m)
        {
            const int N = 24;
            float[] rad = { 0f, 0.29f, 0.6f, 1f };
            float[] alpha = { 0.62f, 0.44f, 0.18f, 0f };
            float size = GameSettings.TreeXHalo * HaloRadius;
            // (the glow amount makes the halo stronger / fainter too; size 0 = no halo)
            float amount = Mathf.Clamp(GameSettings.TreeXGlow / GameSettings.TreeXGlowDefault, 0.25f, 1.5f) * (size < 0.01f ? 0f : 1f);
            size = Mathf.Max(size, 0.001f);
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var lin = GameSettings.TreeXColour.linear;
            Color A(int ring) => new Color(lin.r, lin.g, lin.b, Mathf.Min(0.95f, alpha[ring] * amount));
            v.Add(Vector3.zero); c.Add(A(0));
            for (int ring = 1; ring < rad.Length; ring++)
                for (int k = 0; k < N; k++)
                {
                    float a = k * Mathf.PI * 2f / N;
                    v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * rad[ring] * size);
                    c.Add(A(ring));
                }
            int R(int ring, int k) => 1 + (ring - 1) * N + (k % N);
            for (int k = 0; k < N; k++) { t.Add(0); t.Add(R(1, k + 1)); t.Add(R(1, k)); }
            for (int ring = 1; ring + 1 < rad.Length; ring++)
                for (int k = 0; k < N; k++)
                {
                    t.Add(R(ring, k)); t.Add(R(ring, k + 1)); t.Add(R(ring + 1, k + 1));
                    t.Add(R(ring, k)); t.Add(R(ring + 1, k + 1)); t.Add(R(ring + 1, k));
                }
            m.Clear();
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
        }

        /// <summary>Puts a triangle in the kit facing `outward` (whichever way round its corners were given).</summary>
        static void TriOut(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) k.Tri(a, c, b, ca, cc, cb);
            else k.Tri(a, b, c, ca, cb, cc);
        }

        static readonly Color k_CutPale = new Color(0.84f, 0.68f, 0.46f), k_CutRing = new Color(0.68f, 0.52f, 0.32f), k_CutHeart = new Color(0.58f, 0.42f, 0.25f);

        /// <summary>
        /// A felled tree's stump: a short, flared, ten-sided log (as thick as the trunk was) with a pale, slightly ragged
        /// cut top showing its rings, and a few splinters sticking up where it broke. Solid. Hidden until the tree is felled
        /// (it's shown while the tree grows back). painted: Normal graphics (the bark material; otherwise plain colours).
        /// </summary>
        static GameObject BuildStump(Transform parent, int seed, float r, Color bark, bool painted)
        {
            var rng = new System.Random(seed ^ 0x5f3759d);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var go = new GameObject("stump");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(0, R(0, 360), 0);
            const int S = TrunkSides;
            float h = R(0.42f, 0.6f), spin = R(0, Mathf.PI * 2f);
            float[] ys = { -0.3f, 0f, 0.2f, h };
            float[] rs = { r * 1.2f, r * 1.16f, r * 1.04f, r };
            var rimUp = new float[S];
            for (int s = 0; s < S; s++) rimUp[s] = R(-0.035f, 0.045f);
            Vector3 P(int i, int s)
            {
                float a = spin + (s % S) * Mathf.PI * 2f / S;
                return new Vector3(Mathf.Cos(a) * rs[i], ys[i] + (i == ys.Length - 1 ? rimUp[s % S] : 0f), Mathf.Sin(a) * rs[i]);
            }
            Color C(int i, int s)
            {
                var c = bark * (Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(ys[i] / 0.6f)) * (0.9f + 0.1f * (((s % S) * 7 + i * 3) % 3)));
                c.a = 1f;
                return c;
            }
            var side = new MeshKit();
            var cut = new MeshKit();
            for (int i = 0; i + 1 < ys.Length; i++)
                for (int s = 0; s < S; s++)
                {
                    Vector3 a = P(i, s), b = P(i, s + 1), cc = P(i + 1, s + 1), d = P(i + 1, s);
                    var o = (a + b + cc + d) * 0.25f; o.y = 0f;
                    TriOut(side, a, d, b, C(i, s), C(i + 1, s), C(i, s + 1), o);
                    TriOut(side, b, d, cc, C(i, s + 1), C(i + 1, s), C(i + 1, s + 1), o);
                }
            // the cut top: the bark's edge, pale sapwood, a darker ring, the heart
            int top = ys.Length - 1;
            var centre = new Vector3(0, h - 0.01f, 0);
            var edge = bark * 0.8f; edge.a = 1f;
            for (int s = 0; s < S; s++)
            {
                Vector3 r0 = P(top, s), r1 = P(top, s + 1);
                Vector3 e0 = Vector3.Lerp(centre, r0, 0.9f), e1 = Vector3.Lerp(centre, r1, 0.9f);
                Vector3 m0 = Vector3.Lerp(centre, r0, 0.55f), m1 = Vector3.Lerp(centre, r1, 0.55f);
                Vector3 i0 = Vector3.Lerp(centre, r0, 0.2f), i1 = Vector3.Lerp(centre, r1, 0.2f);
                TriOut(cut, r0, r1, e1, edge, edge, edge, Vector3.up); TriOut(cut, r0, e1, e0, edge, edge, edge, Vector3.up);
                TriOut(cut, e0, e1, m1, k_CutPale, k_CutPale, k_CutRing, Vector3.up); TriOut(cut, e0, m1, m0, k_CutPale, k_CutRing, k_CutRing, Vector3.up);
                TriOut(cut, m0, m1, i1, k_CutRing, k_CutRing, k_CutPale, Vector3.up); TriOut(cut, m0, i1, i0, k_CutRing, k_CutPale, k_CutPale, Vector3.up);
                TriOut(cut, i0, i1, centre, k_CutPale, k_CutPale, k_CutHeart, Vector3.up);
            }
            // a few splinters standing up on one side, where it broke
            int at = rng.Next(S);
            for (int k = 0; k < 3; k++)
            {
                int s = (at + k) % S;
                var b0 = Vector3.Lerp(P(top, s), P(top, s + 1), R(0.1f, 0.4f));
                var b1 = Vector3.Lerp(P(top, s), P(top, s + 1), R(0.6f, 0.9f));
                var mid = (b0 + b1) * 0.5f;
                var tip = Vector3.Lerp(mid, centre, R(0.15f, 0.3f)) + Vector3.up * R(0.1f, 0.24f);
                var inner = Vector3.Lerp(mid, centre, 0.25f);
                var o = mid; o.y = 0f;
                TriOut(side, b0, b1, tip, C(top, s), C(top, s), C(top, s), o);
                TriOut(cut, b0, inner, tip, k_CutRing, k_CutRing, k_CutPale, -o + Vector3.Cross(Vector3.up, o));
                TriOut(cut, inner, b1, tip, k_CutRing, k_CutRing, k_CutPale, -o - Vector3.Cross(Vector3.up, o));
            }
            side.SmoothNormals(60f);
            Material[] mats;
            if (painted && WorldLook.Bark != null) mats = new[] { WorldLook.Bark, WorldLook.Bark };
            else
            {
                Material bm, cm;
                using (ColorSlots.Use(ColorSlots.TreeTrunks)) bm = Art.Mat(bark);
                cm = Art.Mat(k_CutPale);
                mats = new[] { bm, cm };
            }
            var mgo = MeshKit.Spawn(go.transform, "stump mesh", mats, true, side, cut);
            var mc = mgo.AddComponent<MeshCollider>();
            mc.sharedMesh = mgo.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
            if (AiPsxArt.On) AiPsxArt.Apply(go.transform);
            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// A fallen log (Normal graphics: the painted bark, otherwise plain colours): a twelve-sided, smooth-shaded log 3.6
        /// to 5.2 m long, a little thinner at one end, mossy on top, with pale cut ends showing their rings, lying along
        /// the ground (tipped to the slope under its two ends). Solid (a convex collider round it). Its bark triangles are
        /// kept (in the visual's space) for the X and the hit marks, which sit on the bark you see.
        /// </summary>
        void BuildLog(Transform tr)
        {
            var rng = new System.Random(Seed.Value * 13 + 3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float len = R(3.6f, 5.2f), rad = R(0.36f, 0.47f);
            m_LogLen = len; m_LogR = rad;
            // lying along the ground: tipped to the slope between its two ends
            var pos = transform.position;
            var right = transform.right;
            float yc = MapBuilder.Height(pos.x, pos.z);
            var pl = pos - right * len * 0.42f;
            var pr = pos + right * len * 0.42f;
            float yl = MapBuilder.Height(pl.x, pl.z), yr = MapBuilder.Height(pr.x, pr.z);
            float tilt = Mathf.Atan2(yr - yl, len * 0.84f) * Mathf.Rad2Deg;
            var lt = new GameObject("log").transform;
            lt.SetParent(tr, false);
            // (the node itself stands a little under the ground: SpawnNode)
            lt.localPosition = new Vector3(0, (yl + yr) * 0.5f - pos.y + rad * 0.82f, 0);
            lt.localRotation = Quaternion.Euler(0, 0, tilt);
            m_LogT = lt;
            const int S = 12, Segs = 6;
            var barkC = Color.Lerp(Art.DarkWood, Art.Wood, R(0.1f, 0.3f)) * R(0.9f, 1.04f);
            barkC.a = 1f;
            Bark = barkC;
            Leaf = Art.Leaves;
            float spin = R(0, Mathf.PI * 2f);
            var radii = new float[Segs + 1];
            for (int i = 0; i <= Segs; i++) radii[i] = rad * Mathf.Lerp(1f, 0.84f, i / (float)Segs) * R(0.97f, 1.03f);
            Vector3 P(int i, int s)
            {
                float a = spin + (s % S) * Mathf.PI * 2f / S;
                return new Vector3(-len * 0.5f + len * i / Segs, Mathf.Cos(a) * radii[i], Mathf.Sin(a) * radii[i]);
            }
            var moss = new Color(0.33f, 0.45f, 0.2f);
            Color C(int i, int s)
            {
                float a = spin + (s % S) * Mathf.PI * 2f / S, up = Mathf.Cos(a);
                var c = barkC * ((0.9f + 0.1f * (((s % S) * 7 + i * 3) % 3)) * Mathf.Lerp(0.78f, 1.05f, up * 0.5f + 0.5f));
                if (up > 0.5f) c = Color.Lerp(c, moss, 0.3f * (up - 0.5f) / 0.5f);
                c.a = 1f;
                return c;
            }
            var bark = new MeshKit();
            var cut = new MeshKit();
            for (int i = 0; i < Segs; i++)
                for (int s = 0; s < S; s++)
                {
                    Vector3 a = P(i, s), b = P(i, s + 1), cc = P(i + 1, s + 1), d = P(i + 1, s);
                    var o = (a + b + cc + d) * 0.25f; o.x = 0f;
                    TriOut(bark, a, d, b, C(i, s), C(i + 1, s), C(i, s + 1), o);
                    TriOut(bark, b, d, cc, C(i, s + 1), C(i + 1, s), C(i + 1, s + 1), o);
                }
            // the cut ends: the bark's edge, pale sapwood, a darker ring and the heart
            var edge = barkC * 0.8f; edge.a = 1f;
            foreach (int e in new[] { 0, Segs })
            {
                var outward = e == 0 ? Vector3.left : Vector3.right;
                var centre = new Vector3(P(e, 0).x, 0, 0);
                for (int s = 0; s < S; s++)
                {
                    Vector3 r0 = P(e, s), r1 = P(e, s + 1);
                    Vector3 e0 = Vector3.Lerp(centre, r0, 0.88f), e1 = Vector3.Lerp(centre, r1, 0.88f);
                    Vector3 m0 = Vector3.Lerp(centre, r0, 0.55f), m1 = Vector3.Lerp(centre, r1, 0.55f);
                    Vector3 i0 = Vector3.Lerp(centre, r0, 0.2f), i1 = Vector3.Lerp(centre, r1, 0.2f);
                    TriOut(cut, r0, r1, e1, edge, edge, edge, outward); TriOut(cut, r0, e1, e0, edge, edge, edge, outward);
                    TriOut(cut, e0, e1, m1, k_CutPale, k_CutPale, k_CutRing, outward); TriOut(cut, e0, m1, m0, k_CutPale, k_CutRing, k_CutRing, outward);
                    TriOut(cut, m0, m1, i1, k_CutRing, k_CutRing, k_CutPale, outward); TriOut(cut, m0, i1, i0, k_CutRing, k_CutPale, k_CutPale, outward);
                    TriOut(cut, i0, i1, centre, k_CutPale, k_CutPale, k_CutHeart, outward);
                }
            }
            bark.SmoothNormals(50f);
            bool painted = !PsxArt.On && !AiPsxArt.On && WorldLook.Bark != null;
            Material[] mats;
            if (painted) mats = new[] { WorldLook.Bark, WorldLook.Bark };
            else
            {
                Material bm, cm;
                using (ColorSlots.Use(ColorSlots.TreeTrunks)) bm = Art.Mat(barkC);
                cm = Art.Mat(k_CutPale);
                mats = new[] { bm, cm };
            }
            var go = MeshKit.Spawn(lt, "log mesh", mats, true, bark, cut);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
            m_SpotCollider = mc;
            // the bark, in the visual's space (the X and the hit marks go on it)
            var m = Matrix4x4.TRS(lt.localPosition, lt.localRotation, Vector3.one);
            m_BarkTris = new List<Vector3>(bark.T.Count);
            foreach (int k in bark.T) m_BarkTris.Add(m.MultiplyPoint3x4(bark.V[k]));
        }

        /// <summary>Fallen logs: weak spot i, in the visual's space - four along the log, each on the top or one of the two
        /// upper sides (LogSpotSide round from the top: never underneath, where it faces the ground).</summary>
        void LogSpot(int i, out Vector3 centre, out Vector3 dir, out Vector3 along)
        {
            int a = i % 4, b = (i / 4) % 3;
            float t = (a - 1.5f) / 1.5f * m_LogLen * 0.3f;
            float th = (b - 1) * LogSpotSide * Mathf.Deg2Rad;
            var q = m_LogT.localRotation;
            centre = m_LogT.localPosition + q * new Vector3(t, 0, 0);
            dir = q * new Vector3(0, Mathf.Cos(th), Mathf.Sin(th));
            along = q * Vector3.right;
        }

        /// <summary>How far round from the top of a log its side weak spots are (degrees).</summary>
        public const float LogSpotSide = 62f;

        /// <summary>A fallen log lying here (not chopped up): its two ends (on its axis) and its radius. The grass round it
        /// lies flat (GrassField's trample map) and none grows up through it (GrassField.Block) while it's there.</summary>
        public bool LogLying(out Vector3 a, out Vector3 b, out float r)
        {
            a = b = default;
            r = m_LogR;
            if (Kind.Value != Log || m_LogT == null || Amount.Value <= 0 || !IsSpawned) return false;
            var half = m_LogT.right * (m_LogLen * 0.5f);
            var c = m_LogT.position;
            a = c - half;
            b = c + half;
            return true;
        }

        public float LogLength => m_LogLen;
        public float LogRadius => m_LogR;

        /// <summary>How far round a log's footprint no grass grows (m; further out it's only flattened).</summary>
        public const float LogGrassPad = 0.45f;

        void LogGrass(bool lying)
        {
            if (lying && m_LogT != null)
                GrassField.Block(GetInstanceID(), m_LogT.position, m_LogT.right, new Vector2(m_LogLen * 0.5f, m_LogR * 0.85f), LogGrassPad, m_LogT.position.y - m_LogR * 1.2f);
            else GrassField.Unblock(GetInstanceID());
        }

        /// <summary>A new weak spot on a log: one facing the hitter (on their side of it, or the top), never the same one.</summary>
        byte PickLogSpot(Vector3 hitterPos)
        {
            var vt = m_Visual.transform;
            var eye = hitterPos + Vector3.up * 1.5f;
            var face = new float[SpotCount];
            var dist = new float[SpotCount];
            float most = -1f;
            for (int i = 0; i < SpotCount; i++)
            {
                LogSpot(i, out var c, out var d, out _);
                var wp = vt.TransformPoint(c + d * m_LogR);
                var to = eye - wp;
                dist[i] = to.magnitude;
                face[i] = Vector3.Dot(vt.TransformDirection(d).normalized, to / Mathf.Max(0.001f, dist[i]));
                if (i != Spot.Value) most = Mathf.Max(most, face[i]);
            }
            // one of the spots facing them well (the best there are, on a log tipped away from them), nearer their end
            float enough = Mathf.Min(0.3f, most - 0.05f);
            int best = 0;
            float bestScore = float.MinValue;
            for (int i = 0; i < SpotCount; i++)
            {
                if (i == Spot.Value) continue;
                float score = face[i] + Random.Range(0f, 0.5f) - dist[i] * 0.05f - (face[i] < enough ? 3f : 0f);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return (byte)best;
        }

        /// <summary>(tests) the spot a hit from `hitterPos` would move the weak spot to.</summary>
        public byte TestPickSpot(Vector3 hitterPos) => PickSpotFacing(hitterPos);

        /// <summary>Trees: the bark is kept up to this high for the hit marks (and the X).</summary>
        public const float MarkTop = 3f;
        const int MaxMarks = 10;
        /// <summary>Half the size of a hit mark (it's kept clear of the bark over this much).</summary>
        public const float MarkHalf = 0.11f;
        static readonly Color k_MarkDark = new Color(0.17f, 0.1f, 0.05f), k_MarkPale = new Color(0.86f, 0.71f, 0.48f);

        /// <summary>
        /// A hit at `point` leaves a mark on the bark you see - a dark gash with pale wood showing in it - sitting on the
        /// bark (in front of it, never inside): found the same way as the X (rays onto the trunk's / log's own triangles,
        /// as far out as the bark comes anywhere behind the mark). The point a hit reports is on the collider, which is
        /// round, round a ten-sided trunk, so anything put there was partly inside the bark (or floating off it). Children
        /// of the visual (they shake with it); the oldest go past ten; all gone when it's felled. Only looks (every peer
        /// that sees the hit).
        /// </summary>
        public bool AddHitMark(Vector3 point)
        {
            if (!IsWood || Amount.Value <= 0 || m_Visual == null || !m_Visual.activeInHierarchy) return false;
            var vt = m_Visual.transform;
            var local = vt.InverseTransformPoint(point);
            Vector3 pos = default, n = default, up = Vector3.up;
            bool ok = false;
            if (Kind.Value == Log && m_LogT != null && m_BarkTris != null)
            {
                var q = m_LogT.localRotation;
                var inLog = Quaternion.Inverse(q) * (local - m_LogT.localPosition);
                float x = Mathf.Clamp(inLog.x, -m_LogLen * 0.5f + 0.2f, m_LogLen * 0.5f - 0.2f);
                var radial = new Vector3(0, inLog.y, inLog.z);
                if (radial.sqrMagnitude < 1e-4f) radial = Vector3.up;
                n = q * radial.normalized;
                up = q * Vector3.right;
                ok = BarkSpot(m_LogT.localPosition + q * new Vector3(x, 0, 0), n, up, MarkHalf, out pos);
            }
            else if (m_BarkTris != null && m_Pine != null)
            {
                var d = new Vector3(local.x, 0, local.z);
                if (d.sqrMagnitude < 1e-4f) return false;
                n = d.normalized;
                ok = BarkSpot(new Vector3(0, Mathf.Clamp(local.y, 0.35f, MarkTop - 0.15f), 0), n, Vector3.up, MarkHalf, out pos);
            }
            else if (m_PsxTrunk != null)
            {
                var wd = point - transform.position;
                wd.y = 0f;
                if (wd.sqrMagnitude > 1e-4f && m_PsxTrunk.Hit(point.y, wd.normalized, out var wp, out var wn))
                {
                    pos = vt.InverseTransformPoint(wp);
                    n = vt.InverseTransformDirection(wn);
                    ok = true;
                }
            }
            if (!ok && m_SpotCollider != null)
            {
                // (any other tree: on its collider, towards its middle)
                var c = m_SpotCollider.bounds.center;
                var wd = point - new Vector3(c.x, point.y, c.z);
                if (wd.sqrMagnitude > 1e-4f && m_SpotCollider.Raycast(new Ray(point + wd.normalized * 2f, -wd.normalized), out var hit, 4f))
                {
                    pos = vt.InverseTransformPoint(hit.point);
                    n = vt.InverseTransformDirection(hit.normal);
                    ok = true;
                }
            }
            if (!ok || n.sqrMagnitude < 1e-6f) return false;
            n.Normalize();
            if (Mathf.Abs(Vector3.Dot(n, up)) > 0.95f) up = Vector3.Cross(n, Vector3.right);
            var g = new GameObject("hit mark");
            g.transform.SetParent(vt, false);
            g.transform.localPosition = pos + n * 0.012f; // (its back 9 mm off the bark: BarkSpot's rays are 5.5 cm apart, and between them a corner of the bark can stand out that much)
            g.transform.localRotation = Quaternion.LookRotation(-n, up) * Quaternion.Euler(0, 0, Random.Range(-35f, 35f));
            float w = Random.Range(0.14f, 0.19f);
            var dark = Art.Box(g.transform, k_MarkDark, Vector3.zero, new Vector3(w, w * 0.3f, 0.006f));
            var pale = Art.Box(g.transform, k_MarkPale, new Vector3(0, w * 0.03f, -0.0042f), new Vector3(w * 0.7f, w * 0.1f, 0.002f));
            dark.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pale.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Marks.Add(g);
            while (m_Marks.Count > MaxMarks) { if (m_Marks[0] != null) Destroy(m_Marks[0]); m_Marks.RemoveAt(0); }
            return true;
        }

        void ClearHitMarks()
        {
            foreach (var g in m_Marks) if (g != null) Destroy(g);
            m_Marks.Clear();
        }

        /// <summary>The tree or log whose collider a hit at `pos` is on (within `range`), or null.</summary>
        public static ResourceNode WoodAt(Vector3 pos, float range = 0.6f)
        {
            ResourceNode best = null;
            float bd = range;
            foreach (var n in All)
            {
                if (n == null || !n.IsWood || n.Amount.Value <= 0 || n.m_SpotCollider == null || !n.m_SpotCollider.enabled || !n.m_SpotCollider.gameObject.activeInHierarchy) continue;
                if ((n.transform.position - pos).sqrMagnitude > 100f) continue;
                float d = Vector3.Distance(n.m_SpotCollider.ClosestPoint(pos), pos);
                if (d < bd) { bd = d; best = n; }
            }
            return best;
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

        /// <summary>
        /// Somewhere for a leaf shaken loose by a hit to start falling from: just under the branch tips of the lowest
        /// tiers (Normal pines), or low in the crown (PSX trees, palms), mostly on the side of `towards` (the hitter) so
        /// they come down where you can see them. Also the leaf colour to drop.
        /// </summary>
        public bool LeafFrom(Vector3 towards, out Vector3 pos, out Color colour)
        {
            pos = default;
            colour = Leaf;
            if (Kind.Value != Tree || Amount.Value <= 0 || m_Visual == null || !m_Visual.activeInHierarchy) return false;
            var vt = m_Visual.transform;
            var to = vt.InverseTransformPoint(towards);
            float face = Mathf.Atan2(to.z, to.x) + Random.Range(-1.9f, 1.9f);
            var dir = new Vector3(Mathf.Cos(face), 0, Mathf.Sin(face));
            if (m_Pine != null)
            {
                int k = Random.value < 0.7f ? 0 : Mathf.Min(1, m_Pine.Tiers - 1);
                pos = vt.TransformPoint(dir * m_Pine.R[k] * Random.Range(0.45f, 0.92f) + Vector3.up * (m_Pine.Y[k] - Random.Range(0.05f, 0.3f)));
                colour = m_Pine.Needles * GameSettings.WorldTint(GameSettings.WorldColor.Leaves);
                colour.a = 1f;
                return true;
            }
            // (other trees: low in the crown, from what's drawn)
            bool any = false;
            var b = default(Bounds);
            foreach (var r in m_Visual.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || (m_Marker != null && r.transform.IsChildOf(m_Marker))) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any || b.size.y < 1f) return false;
            float rad = Mathf.Min(b.extents.x, b.extents.z) * Random.Range(0.3f, 0.75f);
            var c = new Vector3(transform.position.x, 0, transform.position.z);
            pos = c + vt.TransformDirection(dir) * rad + Vector3.up * Mathf.Lerp(b.min.y, b.max.y, Random.Range(0.5f, 0.68f));
            return true;
        }

        /// <summary>Airstrike: flattened, regrows later like any empty node.</summary>
        public void ServerDeplete()
        {
            if (IsBush) { NetworkObject.Despawn(true); return; }
            if (Amount.Value <= 0) return;
            Amount.Value = 0;
            m_RespawnAt = Time.time + Cfg.NodeRespawnTime;
            if (Birds.Value > 0) ServerBirdsTakeOff(Birds.Value);
        }

        // =====================================================================
        // Birds: a flock now and then flies out of a tree that's hit, and on to another tree whenever theirs is hit
        // =====================================================================

        /// <summary>The chance a hit on a tree with no birds in it sends a new flock flying out of it.</summary>
        public const float BirdChance = 0.15f;
        /// <summary>At most this many flocks about the map per team.</summary>
        public const int FlocksPerTeam = 2;
        int m_BirdsIncoming;
        float m_BirdsLandAt;

        /// <summary>(server) How many flocks are about (sitting in a tree or on their way to one).</summary>
        public static int ServerFlockCount()
        {
            int n = 0;
            foreach (var t in All) if (t != null && (t.Birds.Value > 0 || t.m_BirdsIncoming > 0)) n++;
            return n;
        }

        /// <summary>(server) A tree was hit: its birds fly to another tree - or, now and then, a new flock flies out.</summary>
        void ServerBirdsOnHit(bool force = false)
        {
            if (Kind.Value != Tree) return;
            byte n = Birds.Value;
            if (n == 0)
            {
                if (m_BirdsIncoming > 0) return; // (a flock's on its way here: it lands in it anyway)
                if (!force && (Random.value >= BirdChance || ServerFlockCount() >= FlocksPerTeam * Cfg.Copies)) return;
                n = (byte)Random.Range(5, 10);
            }
            ServerBirdsTakeOff(n);
        }

        /// <summary>(tests) A flock flies out of this tree now (as if a hit had sent one up).</summary>
        public void ServerSendBirds() => ServerBirdsOnHit(true);

        /// <summary>(server) The n birds in (or on their way to) this tree fly off to another one (every peer sees the same
        /// flight: BirdsFlyRpc); they've landed there when the flight's time is up (Birds on that tree).</summary>
        void ServerBirdsTakeOff(byte n)
        {
            Birds.Value = 0;
            var to = ServerPickBirdTree();
            float dur = to != null ? BirdFlock.FlightTime(transform.position, to.transform.position) : 6f;
            if (to != null) { to.m_BirdsIncoming = n; to.m_BirdsLandAt = Time.time + dur; }
            BirdsFlyRpc(to != null ? to.NetworkObjectId : ulong.MaxValue, n, Random.Range(0, int.MaxValue), dur);
        }

        /// <summary>Another standing tree for the birds to land in: 15 - 70 m away if there is one, no birds in it already.</summary>
        ResourceNode ServerPickBirdTree()
        {
            var near = new List<ResourceNode>();
            ResourceNode any = null;
            float anyD = float.MaxValue;
            foreach (var t in All)
            {
                if (t == null || t == this || t.Kind.Value != Tree || t.Amount.Value <= 0 || t.Birds.Value > 0 || t.m_BirdsIncoming > 0) continue;
                var d = t.transform.position - transform.position;
                d.y = 0f;
                float m = d.magnitude;
                if (m >= 15f && m <= 70f) near.Add(t);
                else if (m >= 8f && m < anyD) { anyD = m; any = t; }
            }
            return near.Count > 0 ? near[Random.Range(0, near.Count)] : any;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BirdsFlyRpc(ulong toId, byte count, int seed, float duration)
        {
            ResourceNode to = null;
            if (toId != ulong.MaxValue && NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(toId, out var no)) to = no.GetComponent<ResourceNode>();
            BirdFlock.TakeOff(this, to, count, seed, duration);
        }

        void ServerBirdsUpdate()
        {
            if (m_BirdsIncoming <= 0 || Time.time < m_BirdsLandAt) return;
            byte n = (byte)m_BirdsIncoming;
            m_BirdsIncoming = 0;
            if (Kind.Value == Tree && Amount.Value > 0) Birds.Value = n;
            else ServerBirdsTakeOff(n); // (felled while they were on their way: on to another)
        }

        /// <summary>Where bird i of a flock sits in this tree (world space): on the branch tips of a pine's lower tiers
        /// (from its seed, so every peer puts them in the same places), or out in the crown of any other tree.</summary>
        public Vector3 BirdPerch(int i)
        {
            var rng = new System.Random(Seed.Value * 97 + i * 7919 + 3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float ang = R(0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            var vt = m_Visual != null ? m_Visual.transform : transform;
            if (m_Pine != null)
            {
                int k = Mathf.Min(m_Pine.Tiers - 1, rng.Next(Mathf.Max(1, m_Pine.Tiers - 1)));
                float rr = m_Pine.R[k] * R(0.6f, 0.85f);
                // on the skirt's upper side: from the branch tips (Y, R) rising to the ring part way up (MidY, MidR)
                float t = Mathf.InverseLerp(m_Pine.R[k], m_Pine.R[k] * m_Pine.MidR, rr);
                float y = Mathf.Lerp(m_Pine.Y[k] + m_Pine.H[k] * m_Pine.NotchUp, m_Pine.MidY(k), t) + 0.02f;
                return vt.TransformPoint(dir * rr + Vector3.up * y);
            }
            bool any = false;
            var b = default(Bounds);
            if (m_Visual != null)
                foreach (var r in m_Visual.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || (m_Marker != null && r.transform.IsChildOf(m_Marker))) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            if (!any || b.size.y < 1f) return transform.position + dir * 1.5f + Vector3.up * 7f;
            float rad = Mathf.Min(b.extents.x, b.extents.z) * R(0.45f, 0.8f);
            return new Vector3(transform.position.x, 0, transform.position.z) + dir * rad + Vector3.up * Mathf.Lerp(b.min.y, b.max.y, R(0.55f, 0.8f));
        }

        /// <summary>Local-space direction and height of weak spot `i` (a ring around the trunk / rock).</summary>
        static void SpotDir(byte kind, int i, out Vector3 dir, out float y)
        {
            float a = i * (360f / SpotCount) * Mathf.Deg2Rad;
            dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            y = kind == Tree ? 0.8f + (i % 3) * 0.35f : (0.45f + (i % 3) * 0.2f) * BoulderSize;
        }

        /// <summary>How much bigger the stone nodes are than they first were (1.6 x 1.2 x 1.4 m of rock then).</summary>
        public const float BoulderSize = 1.4f;

        /// <summary>The bark triangles (submesh 1 of a pine's mesh) between heights y0 and y1, in the visual's space.</summary>
        static List<Vector3> BarkAround(Transform needles, float y0, float y1)
        {
            var mf = needles.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || mesh.subMeshCount < 2) return null;
            var v = mesh.vertices;
            var t = mesh.GetTriangles(1);
            var m = Matrix4x4.TRS(needles.localPosition, needles.localRotation, needles.localScale);
            var l = new List<Vector3>();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < y0 || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > y1) continue;
                l.Add(a); l.Add(b); l.Add(c);
            }
            return l.Count > 0 ? l : null;
        }

        /// <summary>How far a ray from `from` going along -dir travels before it first meets the bark (-1: it misses).</summary>
        static float BarkHit(List<Vector3> tris, Vector3 from, Vector3 dir)
        {
            float best = float.MaxValue;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = tris[i], e1 = tris[i + 1] - a, e2 = tris[i + 2] - a;
                var pv = Vector3.Cross(-dir, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                var tv = from - a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < 0f || u > 1f) continue;
                var qv = Vector3.Cross(tv, e1);
                float w = Vector3.Dot(-dir, qv) * inv;
                if (w < 0f || u + w > 1f) continue;
                float t = Vector3.Dot(e2, qv) * inv;
                if (t > 0f && t < best) best = t;
            }
            return best == float.MaxValue ? -1f : best;
        }

        /// <summary>The X's half size (its arms reach this far from its middle, pulsing included).</summary>
        public const float XHalf = 0.14f;

        /// <summary>Normal pines: where the X goes on the bark you see, in the visual's space - facing straight out along
        /// dir, as far out as the bark comes anywhere behind the X (the trunk has ten flat sides: in front of a corner the
        /// bark stands out past the round collider, and a card on the collider was partly inside it).</summary>
        bool BarkSpot(Vector3 dir, float y, out Vector3 pos) => BarkSpot(new Vector3(0, y, 0), dir, Vector3.up, XHalfNow, out pos);

        /// <summary>The same for any axis: from `centre` (on the trunk's / log's axis) out along `dir`, the spot on the bark
        /// that a card `half` across (its sides along `up` and across it) sits in front of without touching.</summary>
        bool BarkSpot(Vector3 centre, Vector3 dir, Vector3 up, float half, out Vector3 pos)
        {
            pos = default;
            if (m_BarkTris == null) return false;
            const float Out = 3f;
            var side = Vector3.Cross(up, dir).normalized;
            up = Vector3.Cross(dir, side).normalized;
            float depth = -1f;
            for (int a = -2; a <= 2; a++)
            for (int b = -2; b <= 2; b++)
            {
                var from = centre + dir * Out + side * (a * half * 0.5f) + up * (b * half * 0.5f);
                float t = BarkHit(m_BarkTris, from, dir);
                if (t >= 0f) depth = Mathf.Max(depth, Out - t);
            }
            if (depth <= 0f) return false;
            pos = centre + dir * depth;
            return true;
        }

        /// <summary>World position/normal of the current weak spot (on the actual surface).</summary>
        public bool TryGetSpot(out Vector3 pos, out Vector3 normal) => TryGetSpot(out pos, out normal, out _);

        /// <summary>onVisual: the point is on the PSX trunk you see (not the plain cylinder), so the X needs no extra offset.</summary>
        bool TryGetSpot(out Vector3 pos, out Vector3 normal, out bool onVisual)
        {
            pos = normal = default;
            onVisual = false;
            if (IsBush || m_SpotCollider == null || Amount.Value <= 0 || Spot.Value == NoSpot) return false;
            // fallen logs: on the bark you see, on the top or the sides (never underneath)
            if (Kind.Value == Log)
            {
                if (m_LogT == null || m_BarkTris == null) return false;
                var vt = m_Visual.transform;
                if (m_BarkSpotFor != Spot.Value)
                {
                    LogSpot(Spot.Value, out var lc, out var ld, out var la);
                    m_BarkSpotOk = BarkSpot(lc, ld, la, XHalfNow, out m_BarkSpotAt);
                    m_BarkSpotFor = Spot.Value;
                }
                if (!m_BarkSpotOk) return false;
                LogSpot(Spot.Value, out _, out var nd, out _);
                pos = vt.TransformPoint(m_BarkSpotAt);
                normal = vt.TransformDirection(nd).normalized;
                return true;
            }
            SpotDir(Kind.Value, Spot.Value, out var dir, out var y);
            var worldDir = transform.rotation * dir;
            // PSX trees: right on the trunk model's surface (it can be thinner, thicker or off to one side of the cylinder)
            if (m_PsxTrunk != null && m_PsxTrunk.Hit(transform.position.y + y, worldDir, out pos, out normal)) { onVisual = true; return true; }
            // Normal pines: on the bark you see (worked out in the visual's own space, so it shakes with it exactly)
            if (m_BarkTris != null && Kind.Value == Tree)
            {
                var vt = m_Visual.transform;
                var localDir = vt.InverseTransformDirection(worldDir);
                localDir.y = 0f;
                localDir.Normalize();
                if (m_BarkSpotFor != Spot.Value) { m_BarkSpotOk = BarkSpot(localDir, y, out m_BarkSpotAt); m_BarkSpotFor = Spot.Value; }
                if (m_BarkSpotOk)
                {
                    pos = vt.TransformPoint(m_BarkSpotAt);
                    normal = vt.TransformDirection(localDir);
                    return true;
                }
            }
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
            m_Marker.position = p + n * (onVisual ? 0.03f : m_BarkTris != null ? 0.025f : 0.015f + m_MarkerOut);
            m_Marker.rotation = Quaternion.LookRotation(-n, Mathf.Abs(n.y) > 0.9f ? transform.forward : Vector3.up);
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
            if (IsServer && m_BirdsIncoming > 0) ServerBirdsUpdate();
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
                m_Marker.localScale = Vector3.one * pop * pulse * (IsWood && m_Decal == null ? XScale : 1f);
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
            Amount.Value = AmountOf(kind);
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
            if (Kind.Value == Tree) ServerBirdsOnHit();
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
            if (Kind.Value == Log && m_LogT != null) return PickLogSpot(hitterPos);
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
