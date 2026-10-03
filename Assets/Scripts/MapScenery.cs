using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Scenery round the Plains and Highlands maps (built the same on every peer by MapBuilder.Build, from the map seed):
    /// layers of mountain ranges out past the ring of mountains round the map, each one further out, taller and paler
    /// (the furthest with snow on its peaks), and big decorative boulders standing about the map (not minable: the
    /// stone nodes are separate). Plus the chiselled low-poly rock the crash site's rubble is made of.
    /// </summary>
    public static class MapScenery
    {
        // =====================================================================
        // Rocks
        // =====================================================================

        static readonly Vector3[] k_IcoV;
        static readonly int[] k_IcoF =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };

        static MapScenery()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            k_IcoV = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < k_IcoV.Length; i++) k_IcoV[i] = k_IcoV[i].normalized;
        }

        /// <summary>
        /// The corners of a chiselled rock round the origin (about 1 across each way): a ball of faces with its corners
        /// pushed in and out, then a few flat cuts sliced off it (the corners past each cut are pressed onto it), so it
        /// has the broad flat faces and sharp edges of broken stone rather than a lumpy ball. `fine` uses the 80-face ball.
        /// </summary>
        static void RockShape(int seed, float jitter, bool fine, out Vector3[] verts, out int[] faces)
        {
            if (fine)
            {
                MeshKit.Ico80(out var v80, out faces);
                verts = (Vector3[])v80.Clone();
            }
            else
            {
                verts = (Vector3[])k_IcoV.Clone();
                faces = k_IcoF;
            }
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < verts.Length; i++) verts[i] *= 1f + R(-jitter, jitter);
            int cuts = fine ? 5 : 2;
            for (int c = 0; c < cuts; c++)
            {
                var n = new Vector3(R(-1f, 1f), R(-0.2f, 1f), R(-1f, 1f)).normalized;
                float d = R(0.55f, 0.8f);
                for (int i = 0; i < verts.Length; i++)
                {
                    float k = Vector3.Dot(verts[i], n);
                    if (k > d) verts[i] -= n * (k - d);
                }
            }
        }

        /// <summary>A chiselled rock as a flat-shaded mesh of its own (radius about 1; for colliders and boulders).</summary>
        public static Mesh RockMesh(int seed, float jitter = 0.18f)
        {
            RockShape(seed, jitter, true, out var v, out var f);
            var verts = new List<Vector3>(f.Length);
            var tris = new List<int>(f.Length);
            for (int i = 0; i < f.Length; i++) { verts.Add(v[f[i]]); tris.Add(i); }
            var m = new Mesh { name = "boulder" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A small chiselled rock added to a merged mesh: centre c, turned by rot, `size` across each way.</summary>
        public static void AddRock(MeshBatch mb, Vector3 c, Quaternion rot, Vector3 size, int seed, float jitter = 0.2f)
        {
            RockShape(seed, jitter, false, out var v, out var f);
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = c + rot * Vector3.Scale(v[f[i]], size * 0.5f), b = c + rot * Vector3.Scale(v[f[i + 1]], size * 0.5f), d = c + rot * Vector3.Scale(v[f[i + 2]], size * 0.5f);
                mb.Tri(a, b, d, (a + b + d) / 3f - c, 2f);
            }
        }

        static GameObject Own(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) go.AddComponent<OwnedMesh>().Mesh = mf.sharedMesh;
            return go;
        }

        public static void Build(Transform root)
        {
            if (Cfg.Map != MapKind.Plains && Cfg.Map != MapKind.Highlands) return;
            BuildRanges(root);
            BuildBoulders(root);
            BuildOutskirts(root);
        }

        // =====================================================================
        // Mountain ranges
        // =====================================================================

        /// <summary>The layers, nearest first: how far out past the map's ring of mountains (m, before scaling), how deep
        /// across, how high the highest peaks are, how many peaks round the ring, and its colour (paler and bluer further
        /// out: the air between).</summary>
        static readonly (float dist, float depth, float height, float peaks, Color col)[] k_Ranges =
        {
            (70f, 60f, 92f, 26f, new Color(0.5f, 0.52f, 0.54f)),
            (155f, 75f, 140f, 20f, new Color(0.57f, 0.6f, 0.64f)),
            (255f, 95f, 200f, 15f, new Color(0.66f, 0.7f, 0.75f)),
        };
        static readonly Color k_Snow = new Color(0.93f, 0.95f, 0.98f);

        /// <summary>How many ranges there are (tests).</summary>
        public static int RangeCount => k_Ranges.Length;
        /// <summary>The ranges built (nearest first; tests): the rock mesh's renderer of each.</summary>
        public static readonly List<Renderer> Ranges = new List<Renderer>();

        /// <summary>
        /// Layers of mountain ranges all the way round, out past the ring of mountain rocks: each a band of faceted peaks
        /// (a ring of ridges, one merged mesh in the Mountains colour - one draw, plus one for the snow on the furthest),
        /// further out, taller and paler than the one in front, so the horizon has depth. No colliders and no shadows
        /// (nobody can get out there). PSX graphics: cobbled rock (the snow concrete); AI PSX: its rock and snow.
        /// They stay well clear of the space arena's shell (sudden death is 1000 m away).
        /// </summary>
        static void BuildRanges(Transform root)
        {
            Ranges.Clear();
            float half = Cfg.MapHalf;
            float k = Mathf.Clamp(half / 100f, 0.6f, 1f);           // (how far out: the bigger maps don't push them into the arena)
            float hk = Mathf.Max(0.75f, Mathf.Sqrt(half / 100f));  // (how tall)
            var go = new GameObject("Mountain ranges");
            go.transform.SetParent(root, false);
            RangeTrees.Clear();
            var allSpots = new List<PineSpot>();
            using (ColorSlots.Use(ColorSlots.Mountains))
                for (int layer = 0; layer < k_Ranges.Length; layer++)
                {
                    var L = k_Ranges[layer];
                    float rc = half * 1.45f + L.dist * k, depth = L.depth * k, H = L.height * hk;
                    var rock = new MeshBatch();
                    var snow = new MeshBatch();
                    var trees = layer < k_TreeCol.Length ? new List<PineSpot>() : null;
                    Range(rock, layer == k_Ranges.Length - 1 ? snow : null, trees, layer, Cfg.MapSeed * 31 + layer * 977, rc, depth, H, L.peaks);
                    var r = Own(rock.Build(go.transform, "range " + layer, Art.Mat(L.col), false));
                    Ranges.Add(r.GetComponent<Renderer>());
                    Psx(r, "cobble_12");
                    if (layer == k_Ranges.Length - 1) Psx(Own(snow.Build(go.transform, "range snow " + layer, Art.Mat(k_Snow), false)), "concrete_00");
                    if (trees != null)
                    {
                        // the same pines as the trees on the map (a little hazier further out), or the old cones without the shader
                        RangeTrees.AddRange(BuildPines(go.transform, "range trees " + layer, trees, true, k_RangeHaze[layer], k_TreeCol[layer]));
                        allSpots.AddRange(trees);
                    }
                }
            PsxPines.Add(go, allSpots, RangeTrees, 2);
        }

        /// <summary>The forests on the nearer ranges (tests): one merged mesh per range, nearest first.</summary>
        public static readonly List<Renderer> RangeTrees = new List<Renderer>();
        /// <summary>The forests' colour on each range that has them (darker and bluer further out: the air between) - only
        /// for the old plain cones (no Painted shader); the real pines are hazed by k_RangeHaze instead.</summary>
        static readonly Color[] k_TreeCol = { new Color(0.2f, 0.33f, 0.19f), new Color(0.3f, 0.4f, 0.34f) };
        /// <summary>How far the pines on each range are faded towards the blue-grey of the air between (0 = as on the map).</summary>
        static readonly float[] k_RangeHaze = { 0.12f, 0.26f };

        /// <summary>A low-poly pine for the far scenery added to a merged mesh: tiers of six-sided cones (no trunk on the
        /// far ones - nobody sees it from there), `h` tall, standing on `foot`.</summary>
        static void AddPine(MeshBatch mb, MeshBatch trunk, Vector3 foot, float h, float yaw, int tiers)
        {
            const int n = 6;
            float w = h * 0.36f;
            if (trunk != null)
            {
                float tr = h * 0.045f, th = h * 0.3f;
                for (int i = 0; i < 4; i++)
                {
                    float a0 = yaw + i * Mathf.PI * 0.5f, a1 = a0 + Mathf.PI * 0.5f;
                    var o0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * tr;
                    var o1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * tr;
                    trunk.Quad(foot + o0 - Vector3.up * 0.5f, foot + o1 - Vector3.up * 0.5f, foot + o1 + Vector3.up * th, foot + o0 + Vector3.up * th, o0 + o1, 2f);
                }
            }
            for (int t = 0; t < tiers; t++)
            {
                float k = tiers == 1 ? 0f : t / (tiers - 1f);
                float y0 = h * Mathf.Lerp(0.2f, 0.52f, k), y1 = h * Mathf.Lerp(tiers == 1 ? 1f : 0.62f, 1f, k);
                float r = w * Mathf.Lerp(1f, 0.58f, k);
                var top = foot + Vector3.up * y1;
                for (int i = 0; i < n; i++)
                {
                    float a0 = yaw + t * 0.5f + i * Mathf.PI * 2f / n, a1 = a0 + Mathf.PI * 2f / n;
                    var p0 = foot + new Vector3(Mathf.Cos(a0) * r, y0, Mathf.Sin(a0) * r);
                    var p1 = foot + new Vector3(Mathf.Cos(a1) * r, y0, Mathf.Sin(a1) * r);
                    var mid = (p0 + p1) * 0.5f - foot;
                    mb.Tri(p0, p1, top, new Vector3(mid.x, r * 0.6f, mid.z), 4f);
                    mb.Tri(p0, p1, foot + Vector3.up * (y0 + 0.05f), Vector3.down, 4f); // (the underside of the tier)
                }
            }
        }

        /// <summary>PSX graphics: a tiling PSX surface instead of the flat colour (one tile every 12 m, the mesh's UVs).</summary>
        static void Psx(GameObject go, string tile)
        {
            var mr = go.GetComponent<MeshRenderer>();
            Material saved = null;
            PsxModels.Look(go, () => { saved = mr.sharedMaterial; mr.sharedMaterial = PsxModels.Tiled(tile, 1f, 1f); }, () => { if (saved != null) mr.sharedMaterial = saved; });
        }

        /// <summary>
        /// One range: a ring `depth` wide round radius rc, cut into slices round it and five corners across each. The
        /// ridge line's height round the ring comes from noise sampled round a circle (so it joins up seamlessly),
        /// sharpened into peaks; across the band it rises from below the ground to a crest that wanders from side to
        /// side. Every corner is nudged a little so the facets aren't regular. Faces high up and facing up go in `snow`.
        /// </summary>
        static void Range(MeshBatch rock, MeshBatch snow, List<PineSpot> trees, int layer, int seed, float rc, float depth, float H, float peaks)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int segs = Mathf.Clamp(Mathf.RoundToInt(peaks * 4.5f), 48, 140);
            const int across = 5;
            float ox = R(0f, 500f), oz = R(0f, 500f), ox2 = R(0f, 500f), oz2 = R(0f, 500f);
            float ring = peaks / (Mathf.PI * 2f); // (noise units round the circle: about one bump per peak)
            var p = new Vector3[segs, across];
            for (int i = 0; i < segs; i++)
            {
                float a = (i + R(-0.3f, 0.3f)) / segs * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                float n = Mathf.PerlinNoise(ox + cx * ring, oz + sz * ring) * 0.65f + Mathf.PerlinNoise(ox2 + cx * ring * 2.3f, oz2 + sz * ring * 2.3f) * 0.35f;
                n = Mathf.Pow(Mathf.Clamp01((n - 0.25f) / 0.55f), 1.5f);
                float ridge = H * (0.3f + 0.7f * n);
                float crest = 0.5f + (Mathf.PerlinNoise(oz + cx * ring * 0.7f, ox + sz * ring * 0.7f) - 0.5f) * 0.5f;
                for (int j = 0; j < across; j++)
                {
                    float u = j / (across - 1f);
                    float shape = u <= crest ? u / crest : (1f - u) / (1f - crest);
                    shape = Mathf.Pow(Mathf.Clamp01(shape), 0.85f);
                    float y = j == 0 || j == across - 1 ? -12f : ridge * shape * R(0.9f, 1.08f) - 4f;
                    float rr = rc + (u - 0.5f) * depth + (j > 0 && j < across - 1 ? R(-0.08f, 0.08f) * depth : 0f);
                    float aj = a + (j > 0 && j < across - 1 ? R(-0.35f, 0.35f) / segs * Mathf.PI * 2f : 0f);
                    p[i, j] = new Vector3(Mathf.Cos(aj) * rr, y, Mathf.Sin(aj) * rr);
                }
            }
            float snowLine = H * 0.66f;
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var mid = (a + b + c) / 3f;
                var outward = Vector3.Cross(b - a, c - a);
                if (outward.y < 0f) outward = -outward; // (all the faces look up and out: the sides of the peaks)
                bool white = snow != null && mid.y > snowLine && outward.normalized.y > 0.3f;
                (white ? snow : rock).Tri(a, b, c, outward, 12f);
            }
            for (int i = 0; i < segs; i++)
            {
                int i1 = (i + 1) % segs;
                for (int j = 0; j < across - 1; j++)
                {
                    Vector3 a = p[i, j], b = p[i1, j], c = p[i1, j + 1], d = p[i, j + 1];
                    if (((i + j) & 1) == 0) { Tri(a, b, c); Tri(a, c, d); }
                    else { Tri(a, b, d); Tri(b, c, d); }
                }
            }
            if (trees == null) return;
            // forests on the lower slopes facing the map (thinning out higher up, none near the snow), in clumps
            var trng = new System.Random(seed + 5);
            float T(float a, float b) => a + (float)trng.NextDouble() * (b - a);
            float treeH = layer == 0 ? 11f : 17f;
            float treeLine = H * (layer == 0 ? 0.5f : 0.42f);
            for (int i = 0; i < segs; i++)
            {
                int i1 = (i + 1) % segs;
                // (clumps: some stretches of the range are thick with trees, others bare)
                float a = i / (float)segs * Mathf.PI * 2f;
                float clump = Mathf.PerlinNoise(seed * 0.01f + Mathf.Cos(a) * 3f, Mathf.Sin(a) * 3f + 7f);
                int per = Mathf.RoundToInt(Mathf.Lerp(0f, layer == 0 ? 14f : 9f, Mathf.InverseLerp(0.3f, 0.7f, clump)));
                for (int t = 0; t < per; t++)
                {
                    // a spot on the inner two rows of the band (the side that faces the map)
                    float u = T(0f, 1f), v = T(0f, 1f);
                    int j = trng.NextDouble() < 0.6 ? 0 : 1;
                    var q0 = Vector3.Lerp(p[i, j], p[i1, j], u);
                    var q1 = Vector3.Lerp(p[i, j + 1], p[i1, j + 1], u);
                    var at = Vector3.Lerp(q0, q1, v);
                    if (at.y < -1f || at.y > treeLine * T(0.6f, 1f)) continue;
                    trees.Add(new PineSpot { Foot = at - Vector3.up * 0.8f, Height = treeH * T(0.7f, 1.3f), Yaw = T(0f, 360f), Pick = trng.Next() });
                }
            }
        }

        // =====================================================================
        // Outskirts: rocks and trees out past the edge of the map
        // =====================================================================

        /// <summary>The outskirts' merged meshes (tests): the rocks and the trees.</summary>
        public static readonly List<Renderer> Outskirts = new List<Renderer>();
        /// <summary>How many rocks and trees went out there (tests).</summary>
        public static int OutskirtRocks, OutskirtTrees;

        /// <summary>
        /// Decoration out past the edge of the map, outside the glass dome (nobody can get there): a band of rocks of all
        /// sizes and clumps of pines on the ground all the way round (on Highlands, up the rocky rise at the edge), so the
        /// view out through the glass isn't bare. Merged meshes (a few draws), no colliders, no shadows.
        /// </summary>
        static void BuildOutskirts(Transform root)
        {
            Outskirts.Clear();
            OutskirtRocks = OutskirtTrees = 0;
            float half = Cfg.MapHalf;
            var rng = new System.Random(Cfg.MapSeed * 13 + 77);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var rocks = new MeshBatch();
            var rocksDark = new MeshBatch();
            var pines = new List<PineSpot>();
            float inner = half + 6f, outer = half + 27f; // (the ground reaches half + 30)
            // (a pine's reach round its trunk: its lowest tier, at the biggest it's drawn)
            float pineReach = PineReach * 1.2f;
            int n = Mathf.RoundToInt(260 * half / 100f);
            for (int i = 0; i < n; i++)
            {
                // a spot in the band round the square map
                float m = R(inner, outer), along = R(-m, m);
                int side = rng.Next(4);
                float x = side < 2 ? along : (side == 2 ? m : -m), z = side < 2 ? (side == 0 ? m : -m) : along;
                float out01 = Mathf.InverseLerp(inner, outer, m);
                float clump = Mathf.PerlinNoise(x * 0.04f + 17.3f, z * 0.04f + 5.1f);
                bool tree = clump > 0.48f && rng.NextDouble() < 0.75;
                float gy = MapBuilder.Height(x, z);
                if (tree)
                {
                    TryPine(pines, x, z, m, R(0.8f, 1.15f) * (0.9f + out01 * 0.3f), R(0f, 360f), rng.Next(), half, pineReach);
                }
                else
                {
                    float sz = rng.NextDouble() < 0.18 ? R(3f, 7.5f) : R(0.8f, 2.6f);
                    var size = new Vector3(sz * R(0.9f, 1.4f), sz * R(0.5f, 0.85f), sz * R(0.8f, 1.2f));
                    // (a big one is pushed out until none of it reaches back in towards the dome)
                    float reach = Mathf.Max(size.x, size.z) * 0.62f;
                    if (m - reach < half + 4f)
                    {
                        float k = (half + 4f + reach) / m;
                        x *= k; z *= k; // (m is the larger of |x| and |z|, so this moves it straight out)
                        gy = MapBuilder.Height(x, z);
                    }
                    AddRock(i % 3 == 0 ? rocksDark : rocks, new Vector3(x, gy + size.y * 0.2f, z), Quaternion.Euler(R(-15f, 15f), R(0f, 360f), R(-15f, 15f)), size, rng.Next());
                    OutskirtRocks++;
                }
            }
            // more pines: a second sweep round the band that only plants trees (thick in the clumps, a few between them)
            int extra = Mathf.RoundToInt(300 * half / 100f);
            for (int i = 0; i < extra; i++)
            {
                float m = R(inner, outer), along = R(-m, m);
                int side = rng.Next(4);
                float x = side < 2 ? along : (side == 2 ? m : -m), z = side < 2 ? (side == 0 ? m : -m) : along;
                float clump = Mathf.PerlinNoise(x * 0.04f + 17.3f, z * 0.04f + 5.1f);
                if (rng.NextDouble() > Mathf.Lerp(0.15f, 0.95f, Mathf.InverseLerp(0.3f, 0.6f, clump))) continue;
                float out01 = Mathf.InverseLerp(inner, outer, m);
                TryPine(pines, x, z, m, R(0.8f, 1.15f) * (0.9f + out01 * 0.3f), R(0f, 360f), rng.Next(), half, pineReach);
            }
            OutskirtTrees = pines.Count;
            var go = new GameObject("Outskirts");
            go.transform.SetParent(root, false);
            void Add(GameObject g) { Outskirts.Add(g.GetComponent<Renderer>()); g.GetComponent<MeshRenderer>().receiveShadows = false; }
            using (ColorSlots.Use(ColorSlots.Boulders))
            {
                Add(Own(rocks.Build(go.transform, "outskirt rocks", Art.Mat(new Color(0.54f, 0.53f, 0.5f)), false)));
                Add(Own(rocksDark.Build(go.transform, "outskirt rocks dark", Art.Mat(new Color(0.43f, 0.42f, 0.4f)), false)));
            }
            // the pines: the same as the trees on the map (PSX graphics: the PSX trees, like the map's)
            var pineRs = BuildPines(go.transform, "outskirt pines", pines, false, 0f, new Color(0.22f, 0.4f, 0.17f));
            Outskirts.AddRange(pineRs);
            PsxPines.Add(go, pines, pineRs, 1);
        }

        /// <summary>A pine out past the edge at (x, z) (m: how far out it is, the larger of |x| and |z|), pushed straight out
        /// until none of it reaches back in towards the dome, and not right on top of another one.</summary>
        static void TryPine(List<PineSpot> pines, float x, float z, float m, float scale, float yaw, int pick, float half, float reach)
        {
            float r = reach * scale / 1.2f;
            if (m - r < half + 4f)
            {
                float k = (half + 4f + r) / m;
                x *= k; z *= k; // (m is the larger of |x| and |z|, so this moves it straight out)
            }
            foreach (var o in pines)
                if (new Vector2(o.Foot.x - x, o.Foot.z - z).sqrMagnitude < 2.6f * 2.6f) return;
            var tpl = Pines()[(pick & 0x7fffffff) % Pines().Count];
            pines.Add(new PineSpot { Foot = new Vector3(x, MapBuilder.Height(x, z) - 0.15f, z), Height = tpl.Top * scale, Yaw = yaw, Pick = pick });
        }

        // =====================================================================
        // Scenery pines: the same pines as the trees on the map
        // =====================================================================

        /// <summary>Where a scenery pine stands: its foot, how tall, turned how far (degrees), and which pine (any number:
        /// it picks one of the templates).</summary>
        public struct PineSpot { public Vector3 Foot; public float Height, Yaw; public int Pick; }

        /// <summary>One of the map's pines (by a tree seed), ready to be copied: its needles exactly as on the tree and a
        /// simple trunk; and for the far ranges (a hundred metres and more away) the same pine with five branch tips a tier
        /// instead of 7-10, no undersides (nobody sees under them from the map) and a four-sided stub of a trunk - about a
        /// third of the faces; plus how tall it is and how far its lowest tier reaches.</summary>
        sealed class PineTemplate { public MeshKit Needles, Trunk, NeedlesFar, TrunkFar; public float Top, Reach; }

        static List<PineTemplate> s_Pines;
        const int PineTemplateCount = 20;

        /// <summary>The templates: four of each of the five kinds of pine on the map (classic, spruce, fir, droopy, scraggly).</summary>
        static List<PineTemplate> Pines()
        {
            if (s_Pines != null) return s_Pines;
            s_Pines = new List<PineTemplate>();
            int seed = 7001;
            for (int i = 0; i < PineTemplateCount; i++)
            {
                // (a seed that gives the next kind in turn)
                while (ResourceNode.PineVariant(seed) != i % ResourceNode.PineVariants) seed++;
                var needles = new MeshKit();
                var shape = ResourceNode.PineNeedles(needles, seed);
                seed++;
                needles.SmoothNormals(65f); // (as on the tree)
                var lod = new MeshKit();
                ResourceNode.PineNeedles(lod, seed - 1, 5);
                lod.SmoothNormals(65f);
                var far = new MeshKit();
                for (int t = 0; t < lod.T.Count; t += 3)
                {
                    int a = lod.T[t], b = lod.T[t + 1], c = lod.T[t + 2];
                    var n = Vector3.Cross(lod.V[b] - lod.V[a], lod.V[c] - lod.V[a]);
                    if (n.y < -0.3f * n.magnitude) continue; // (an underside)
                    int k = far.V.Count;
                    foreach (int v in new[] { a, b, c }) { far.V.Add(lod.V[v]); far.N.Add(lod.N[v]); far.C.Add(lod.C[v]); far.U.Add(lod.U[v]); }
                    far.T.Add(k); far.T.Add(k + 1); far.T.Add(k + 2);
                }
                var trunk = new MeshKit();
                TrunkLite(trunk, shape, 8, false);
                var trunkFar = new MeshKit();
                TrunkLite(trunkFar, shape, 4, true);
                float reach = 0f;
                foreach (var r in shape.R) reach = Mathf.Max(reach, r);
                s_Pines.Add(new PineTemplate { Needles = needles, NeedlesFar = far, Trunk = trunk, TrunkFar = trunkFar, Top = shape.Top, Reach = reach });
            }
            return s_Pines;
        }

        /// <summary>The furthest any template pine's needles reach out from its trunk (m, at its own size).</summary>
        static float PineReach { get { float r = 0f; foreach (var p in Pines()) r = Mathf.Max(r, p.Reach); return r; } }

        /// <summary>A plain trunk for a scenery pine: eight smooth sides from under the ground up into the needles, as thick as
        /// the tree's own trunk at each height (and coloured like it). The trees' full trunk has hundreds of faces for its
        /// weak spot and the bark marks; from out past the edge nobody can tell.</summary>
        static void TrunkLite(MeshKit kit, ResourceNode.PineShape p, int sides, bool stub)
        {
            float top = Mathf.Min(p.TrunkTop, p.UnderY(0) + 0.6f);
            float[] ys = stub ? new[] { -0.6f, top } : new[] { -0.4f, 0.3f, 1.8f, (1.8f + top) * 0.5f, top };
            int k0 = kit.V.Count;
            for (int i = 0; i < ys.Length; i++)
            {
                float y = ys[i], r = p.TrunkRadius(Mathf.Max(0f, y));
                var c = p.Bark * Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(y / 2.5f));
                c.a = 1f;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    kit.V.Add(d * r + Vector3.up * y);
                    kit.N.Add(d);
                    kit.C.Add(c.linear);
                    kit.U.Add(new Vector2(p.Sway(y), 0f));
                }
            }
            for (int i = 0; i + 1 < ys.Length; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = k0 + i * (sides + 1) + s, b = a + 1, d = a + sides + 1, e = d + 1;
                    kit.T.Add(a); kit.T.Add(d); kit.T.Add(b);
                    kit.T.Add(b); kit.T.Add(d); kit.T.Add(e);
                }
        }

        /// <summary>Copies a template's kit into a merged one: turned by `rot`, scaled by `s`, standing at `at`, its colours
        /// faded `haze` of the way towards the blue-grey of the air.</summary>
        static void Put(MeshKit dst, MeshKit src, Vector3 at, Quaternion rot, float s, float haze)
        {
            int o = dst.V.Count;
            var air = new Color(0.52f, 0.6f, 0.68f).linear;
            for (int i = 0; i < src.V.Count; i++)
            {
                dst.V.Add(at + rot * (src.V[i] * s));
                dst.N.Add(rot * src.N[i]);
                dst.C.Add(haze > 0f ? Color.Lerp(src.C[i], air, haze) : src.C[i]);
                dst.U.Add(new Vector2(src.U[i].x * s, src.U[i].y));
            }
            foreach (int t in src.T) dst.T.Add(t + o);
        }

        /// <summary>
        /// The scenery pines at these spots, as the map's own pines (their needles and colours, swaying in the same wind,
        /// tinted by the Pine needles and Tree trunks colours): merged meshes in pieces round the map (two draws each, so the
        /// ones behind you aren't drawn; near ones swap to the far pines further off), no colliders, no shadows. `far`: without the tiers' undersides. Without
        /// the Painted shader: the old plain low-poly cones in `fallback`.
        /// </summary>
        static List<Renderer> BuildPines(Transform parent, string name, List<PineSpot> spots, bool far, float haze, Color fallback)
        {
            var list = new List<Renderer>();
            if (spots.Count == 0) return list;
            var foliage = WorldLook.Foliage;
            var bark = WorldLook.Bark;
            if (foliage == null || bark == null)
            {
                var mb = new MeshBatch();
                var tb = new MeshBatch();
                foreach (var sp in spots) AddPine(mb, far ? null : tb, sp.Foot, sp.Height, sp.Yaw * Mathf.Deg2Rad, far ? 2 : 3);
                using (ColorSlots.Use(ColorSlots.Leaves))
                    list.Add(Own(mb.Build(parent, name, Art.Mat(fallback), false)).GetComponent<Renderer>());
                if (!far)
                    using (ColorSlots.Use(ColorSlots.TreeTrunks))
                        list.Add(Own(tb.Build(parent, name + " trunks", Art.Mat(new Color(0.36f, 0.23f, 0.12f)), false)).GetComponent<Renderer>());
                foreach (var r in list) ((MeshRenderer)r).receiveShadows = false;
                return list;
            }
            var tpl = Pines();
            // far ones (the ranges): four quarters round the map. Near ones (the outskirts): 16 pieces round the band, each
            // drawn in full close up and as the far pines (a third of the faces) from about 120 m away (a LODGroup)
            int pieces = far ? 4 : 16;
            var needles = new MeshKit[pieces];
            var trunks = new MeshKit[pieces];
            var needlesLo = far ? null : new MeshKit[pieces];
            var trunksLo = far ? null : new MeshKit[pieces];
            for (int q = 0; q < pieces; q++)
            {
                needles[q] = new MeshKit(); trunks[q] = new MeshKit();
                if (!far) { needlesLo[q] = new MeshKit(); trunksLo[q] = new MeshKit(); }
            }
            foreach (var sp in spots)
            {
                var t = tpl[(sp.Pick & 0x7fffffff) % tpl.Count];
                // (the piece round the middle it's in)
                int q = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(sp.Foot.z, sp.Foot.x) + Mathf.PI) / (Mathf.PI * 2f) * pieces), 0, pieces - 1);
                float s = sp.Height / Mathf.Max(1f, t.Top);
                var rot = Quaternion.Euler(0f, sp.Yaw, 0f);
                Put(needles[q], far ? t.NeedlesFar : t.Needles, sp.Foot, rot, s, haze);
                Put(trunks[q], far ? t.TrunkFar : t.Trunk, sp.Foot, rot, s, haze);
                if (!far)
                {
                    Put(needlesLo[q], t.NeedlesFar, sp.Foot, rot, s, haze);
                    Put(trunksLo[q], t.TrunkFar, sp.Foot, rot, s, haze);
                }
            }
            for (int q = 0; q < pieces; q++)
            {
                if (needles[q].Count == 0) continue;
                var go = MeshKit.Spawn(parent, name + " " + q, new[] { foliage, bark }, false, needles[q], trunks[q]);
                var mr = go.GetComponent<MeshRenderer>();
                mr.receiveShadows = false;
                list.Add(mr);
                if (far) continue;
                var lo = MeshKit.Spawn(go.transform, name + " " + q + " far", new[] { foliage, bark }, false, needlesLo[q], trunksLo[q]);
                var lr = lo.GetComponent<MeshRenderer>();
                lr.receiveShadows = false;
                list.Add(lr);
                // (switches by how much of the screen the piece fills: a piece is ~60 m along, so 0.4 is about 120 m away)
                var lg = go.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.4f, new Renderer[] { mr }), new LOD(0f, new Renderer[] { lr }) });
                lg.RecalculateBounds();
            }
            return list;
        }

        /// <summary>
        /// PSX (and the AI PSX test): the map's trees are the PSX trees, so the scenery pines are too. The first time the
        /// look is on, a PSX tree goes up at every `every`th spot (the far ranges: every other one - there are hundreds);
        /// the Normal pines are hidden while it's on, and come back when it's off.
        /// </summary>
        class PsxPines : MonoBehaviour
        {
            List<PineSpot> m_Spots;
            List<Renderer> m_Normal;
            int m_Every;
            GameObject m_Psx;
            bool m_On;

            public static void Add(GameObject owner, List<PineSpot> spots, List<Renderer> normal, int every)
            {
                if (spots.Count == 0) return;
                var p = owner.AddComponent<PsxPines>();
                p.m_Spots = spots;
                p.m_Normal = new List<Renderer>(normal);
                p.m_Every = Mathf.Max(1, every);
                GameSettings.GraphicsChanged += p.Refresh;
                p.Refresh();
            }

            void OnDestroy() => GameSettings.GraphicsChanged -= Refresh;

            /// <summary>(tests) PSX trees shown.</summary>
            public int PsxCount => m_Psx != null && m_Psx.activeSelf ? m_Psx.transform.childCount : 0;

            void Refresh()
            {
                if (this == null) return;
                bool on = PsxArt.On || AiPsxArt.On;
                if (on == m_On) return;
                m_On = on;
                if (on && m_Psx == null)
                {
                    m_Psx = new GameObject("psx trees");
                    m_Psx.transform.SetParent(transform, false);
                    for (int i = 0; i < m_Spots.Count; i += m_Every)
                    {
                        var sp = m_Spots[i];
                        var holder = new GameObject("psx pine").transform;
                        holder.SetParent(m_Psx.transform, false);
                        holder.position = sp.Foot;
                        if (!PsxArt.BuildTree(holder, sp.Pick, sp.Height, out _, out _, out _)) { Destroy(holder.gameObject); break; }
                        foreach (var r in holder.GetComponentsInChildren<MeshRenderer>())
                        {
                            r.shadowCastingMode = ShadowCastingMode.Off;
                            r.receiveShadows = false;
                        }
                    }
                    if (AiPsxArt.On) AiPsxArt.Apply(m_Psx.transform);
                }
                if (m_Psx != null) m_Psx.SetActive(on);
                foreach (var r in m_Normal) if (r) r.enabled = !on;
            }
        }

        // =====================================================================
        // Boulders
        // =====================================================================

        /// <summary>Where the boulders are (the middle of each group, every copy; tests).</summary>
        public static readonly List<Vector3> Boulders = new List<Vector3>();
        /// <summary>How big each one is round (its biggest rock's radius; same order as Boulders).</summary>
        public static readonly List<float> BoulderRadius = new List<float>();

        /// <summary>
        /// Is this a good spot for a boulder of radius r? Not in or next to a base (or its spawn), the crash site in the
        /// middle, the straight way from any base to the middle (or, with 3-4 bases, to the bases either side), the glass
        /// walls between the halves, near the edge of the map or on a stone node's spot.
        /// </summary>
        public static bool BoulderSpotOk(Vector3 p, float r)
        {
            float half = Cfg.MapHalf;
            if (!Cfg.InFirstSector(p, r + 6f)) return false;
            if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) > half - r - 7f) return false;
            var flat = new Vector2(p.x, p.z);
            if (flat.magnitude < 27f + r) return false;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var c = Cfg.BaseCenter[t];
                float dx = Mathf.Max(0f, Mathf.Abs(p.x - c.x) - Cfg.BaseHalf), dz = Mathf.Max(0f, Mathf.Abs(p.z - c.z) - Cfg.BaseHalf);
                if (Mathf.Sqrt(dx * dx + dz * dz) < r + 9f) return false;
                var bc = new Vector2(c.x, c.z);
                if (SegDist(flat, bc, Vector2.zero) < r + 9f) return false;
                if (Cfg.FourWay)
                {
                    var n = Cfg.BaseCenter[(t + 1) % Cfg.TeamCount];
                    if (SegDist(flat, bc, new Vector2(n.x, n.z)) < r + 7f) return false;
                }
            }
            foreach (var w in MapBuilder.WildRocks) if ((new Vector2(w.x, w.z) - flat).magnitude < r + 4f) return false;
            return true;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        /// <summary>
        /// Big boulders about the map: a group of one big chiselled rock (about 5-10 m across, sunk into the ground, leaning a
        /// little) and one to three smaller ones round its foot. Spots are picked in one team's part of the map and copied
        /// round to the others (same rocks, turned), so every team gets the same. They're solid (a convex collider each;
        /// the trees, bushes, horses and airdrops are put down clear of them) but not minable. Colour: Big rocks.
        /// PSX graphics: the big PSX terrain rocks; AI PSX: its rock.
        /// </summary>
        /// <summary>The big boulders are this much bigger than they first were (3 - 6.4 m across then; now about 5 - 10 m).</summary>
        public const float BoulderScale = 1.6f;

        static void BuildBoulders(Transform root)
        {
            Boulders.Clear();
            BoulderRadius.Clear();
            float half = Cfg.MapHalf;
            var rng = new System.Random(Cfg.MapSeed * 7 + 41);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float area = half / 100f;
            area *= area * 2f / Cfg.Copies;
            int want = Mathf.Max(4, Mathf.RoundToInt(11 * area));
            var spots = new List<(Vector3 p, float r)>();
            for (int tries = 0; tries < want * 60 && spots.Count < want; tries++)
            {
                // (BoulderScale times as big as they were - a little less late on, if the big ones won't all fit: small maps)
                float r = R(1.5f, 3.2f) * Mathf.Lerp(BoulderScale, 1.15f, Mathf.Clamp01(tries / (want * 60f) * 2f - 1f));
                var p = new Vector3(R(-half + 6f, half - 6f), 0, R(-half + 6f, -6f));
                if (!BoulderSpotOk(p, r)) continue;
                bool close = false;
                foreach (var s in spots) if ((s.p - p).magnitude < 14f + r + s.r) { close = true; break; }
                if (close) continue;
                // not hanging off a steep slope (Highlands)
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    float h = MapBuilder.Height(p.x + Mathf.Cos(a) * r, p.z + Mathf.Sin(a) * r);
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
                if (hi - lo > r * 0.9f) continue;
                spots.Add((p, r));
            }
            var holder = new GameObject("Boulders").transform;
            holder.SetParent(root, false);
            var meshes = new Dictionary<int, Mesh>();
            Mesh MeshOf(int seed)
            {
                if (meshes.TryGetValue(seed, out var m)) return m;
                m = RockMesh(seed);
                holder.gameObject.AddComponent<OwnedMesh>().Mesh = m;
                return meshes[seed] = m;
            }
            using (ColorSlots.Use(ColorSlots.Boulders))
                for (int si = 0; si < spots.Count; si++)
                {
                    var (p0, r) = spots[si];
                    int seed = rng.Next();
                    float yaw = R(0f, 360f);
                    // the group, in its own space (the same for every copy)
                    var shrng = new System.Random(seed);
                    float S(float a, float b) => a + (float)shrng.NextDouble() * (b - a);
                    var col = Color.Lerp(new Color(0.47f, 0.46f, 0.43f), new Color(0.6f, 0.6f, 0.62f), S(0f, 1f));
                    float hgt = S(0.6f, 1f);
                    var mainScale = new Vector3(r * S(0.9f, 1.15f), r * hgt, r * S(0.85f, 1.1f));
                    var mainTilt = new Vector3(S(-10f, 10f), S(0f, 360f), S(-10f, 10f));
                    int small = 1 + shrng.Next(3);
                    var smalls = new List<(Vector3 at, Vector3 scale, Vector3 euler, Color col, int seed)>();
                    for (int k = 0; k < small; k++)
                    {
                        float a = S(0f, Mathf.PI * 2f), rr = r * S(0.3f, 0.55f);
                        var at = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r * S(0.85f, 1.1f) + rr * 0.6f);
                        smalls.Add((at, new Vector3(rr * S(0.9f, 1.2f), rr * S(0.6f, 0.9f), rr), new Vector3(S(-20f, 20f), S(0f, 360f), S(-20f, 20f)), col * S(0.88f, 1.05f), seed + 1 + k));
                    }
                    for (int m = 0; m < Cfg.Copies; m++)
                    {
                        var p = Cfg.Copy(p0, m);
                        var g = new GameObject("boulder").transform;
                        g.gameObject.AddComponent<MapSceneryMarker>();
                        g.SetParent(holder, false);
                        // sunk to the lowest ground under it, so no side floats
                        float y = float.MaxValue;
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI / 4f;
                            y = Mathf.Min(y, MapBuilder.Height(p.x + Mathf.Cos(a) * r * 0.8f, p.z + Mathf.Sin(a) * r * 0.8f));
                        }
                        y = Mathf.Min(y, MapBuilder.Height(p.x, p.z));
                        g.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Euler(0, yaw + m * 360f / Cfg.Copies, 0));
                        Art.Part(g, MeshOf(seed), col, new Vector3(0, mainScale.y * 0.42f, 0), mainScale, mainTilt, true, null, "rock");
                        foreach (var s in smalls)
                        {
                            var w = g.TransformPoint(s.at);
                            float gy = MapBuilder.Height(w.x, w.z) - y;
                            Art.Part(g, MeshOf(s.seed), s.col, s.at + Vector3.up * (gy + s.scale.y * 0.35f), s.scale, s.euler, s.scale.x > 0.55f, null, "rock");
                        }
                        foreach (var mr in g.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.On;
                        PsxModels.Replace(g, "bigrock" + ((seed & 0x7fffffff) % 6), PsxModels.Fit.Uniform);
                        Boulders.Add(new Vector3(p.x, y, p.z));
                        BoulderRadius.Add(r);
                    }
                }
        }
    }

    /// <summary>Marks a big boulder (MapScenery).</summary>
    public class MapSceneryMarker : MonoBehaviour { }
}
