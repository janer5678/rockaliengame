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
            using (ColorSlots.Use(ColorSlots.Mountains))
                for (int layer = 0; layer < k_Ranges.Length; layer++)
                {
                    var L = k_Ranges[layer];
                    float rc = half * 1.45f + L.dist * k, depth = L.depth * k, H = L.height * hk;
                    var rock = new MeshBatch();
                    var snow = new MeshBatch();
                    Range(rock, layer == k_Ranges.Length - 1 ? snow : null, Cfg.MapSeed * 31 + layer * 977, rc, depth, H, L.peaks);
                    var r = Own(rock.Build(go.transform, "range " + layer, Art.Mat(L.col), false));
                    Ranges.Add(r.GetComponent<Renderer>());
                    Psx(r, "cobble_12");
                    if (layer == k_Ranges.Length - 1) Psx(Own(snow.Build(go.transform, "range snow " + layer, Art.Mat(k_Snow), false)), "concrete_00");
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
        static void Range(MeshBatch rock, MeshBatch snow, int seed, float rc, float depth, float H, float peaks)
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
        /// Big boulders about the map: a group of one big chiselled rock (2.5-6 m across, sunk into the ground, leaning a
        /// little) and one to three smaller ones round its foot. Spots are picked in one team's part of the map and copied
        /// round to the others (same rocks, turned), so every team gets the same. They're solid (a convex collider each;
        /// the trees, bushes, horses and airdrops are put down clear of them) but not minable. Colour: Big rocks.
        /// PSX graphics: the big PSX terrain rocks; AI PSX: its rock.
        /// </summary>
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
                float r = R(1.5f, 3.2f);
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
