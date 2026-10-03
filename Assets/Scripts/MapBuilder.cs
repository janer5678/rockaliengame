using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Builds the static (non-networked) world identically on every peer from (map, size, seed):
    /// flat Plains or hilly Highlands terrain, the two bases with their bedrock spawn and alien machine,
    /// the glass wall between the halves and the sudden death arena.
    /// </summary>
    public static class MapBuilder
    {
        static readonly Color k_Grass = new Color(0.36f, 0.56f, 0.3f); // (the PSX patches; the ground itself is WorldLook.GroundMaterial)
        static Transform s_Root;
        public static int BuiltKey = -1, BuiltSeed;

        static GameObject s_Glass;
        /// <summary>Highlands: where the hill rocks go (one half; NetGame mirrors them). They are real stone nodes.</summary>
        public static readonly List<Vector3> WildRocks = new List<Vector3>();

        public static Transform Root => s_Root;

        public static bool IsBuilt(int key, int seed) => s_Root != null && BuiltKey == key && BuiltSeed == seed;

        public static void Build()
        {
            if (s_Root) Object.Destroy(s_Root.gameObject);
            s_Glass = null;
            s_Dropping = null;
            s_BaseFloors.Clear();
            WildRocks.Clear();
            s_Root = new GameObject("World").transform;
            BuiltKey = Cfg.MapKey;
            BuiltSeed = Cfg.MapSeed;
            var root = s_Root;
            // Normal graphics' own looks (castle walls, waving flags) vs the plain ones PSX / AI PSX keep
            var look = NormalLook.On(root.gameObject);
            var rng = new System.Random(99 + Cfg.MapSeed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = Cfg.MapHalf;

            // ---------- ground ----------
            if (Cfg.Map == MapKind.Highlands) BuildTerrain(root);
            else if (ThemeMaps.IsTheme) ThemeMaps.BuildGround(root); // THEME MAPS
            else
            {
                float size = half * 2f + 60f;
                var ground = Art.Box(root, k_Grass, new Vector3(0, -0.5f, 0), new Vector3(size, 1, size), default, true);
                ground.name = "Ground";
                ground.AddComponent<GroundMarker>();
                int patches = Mathf.RoundToInt(70 * (half / 100f) * (half / 100f));
                var patchRs = new List<Renderer>();
                for (int i = 0; i < patches; i++)
                {
                    var c = Color.Lerp(k_Grass, new Color(0.3f, 0.48f, 0.24f), R(0.3f, 1f));
                    float w = R(4f, 14f);
                    patchRs.Add(Art.Box(root, c, new Vector3(R(-half + 5, half - 5), 0.005f, R(-half + 5, half - 5)), new Vector3(w, 0.01f, w * R(0.5f, 1.5f)), new Vector3(0, R(0, 90), 0)).GetComponent<Renderer>());
                }
                // Normal graphics: one flat colour (Settings > Display picks it); the patches are only for PSX
                ground.GetComponent<Renderer>().sharedMaterial = WorldLook.GroundMaterial(false);
                foreach (var pr in patchRs) pr.enabled = false;
                // PSX graphics: PSX grass, with darker patches of it
                PsxModels.Retexture(ground, new[] { ground.GetComponent<Renderer>() }, r => "grass_21", 3f);
                PsxModels.Retexture(ground, patchRs, r => "grass_10", 3f);
                PsxModels.Look(ground, () => { foreach (var pr in patchRs) if (pr) pr.enabled = true; },
                    () => { foreach (var pr in patchRs) if (pr) pr.enabled = false; });
            }

            // ---------- bases ----------
            for (int t = 0; t < Cfg.TeamCount && !Cfg.Builder; t++) // Builder: no bases, no machines
            {
                var c = Cfg.BaseCenter[t];
                var team = Cfg.TeamColor[t];
                var pad = Color.Lerp(new Color(0.5f, 0.45f, 0.35f), team, 0.35f);
                var line = Color.Lerp(pad, Color.white, 0.25f);
                float s = Cfg.BaseHalf * 2f;
                int cells = Mathf.RoundToInt(s / Cfg.Cell);
                using (ColorSlots.Use(ColorSlots.BasePads))
                {
                    // (the tinted floor and its grid are only there while you build: once the wall drops they go, and the
                    // base is plain flat ground - grass, no blades - inside its team-colour border; SetGlassWall)
                    var floor = new GameObject("base floor " + t).transform;
                    floor.SetParent(root, false);
                    s_BaseFloors.Add(floor.gameObject);
                    Art.Box(floor, pad, c + new Vector3(0, 0.02f, 0), new Vector3(s, 0.03f, s));
                    for (int k = 0; k <= cells; k++)
                    {
                        float o = -Cfg.BaseHalf + k * Cfg.Cell;
                        Art.Box(floor, line, c + new Vector3(0, 0.04f, o), new Vector3(s, 0.01f, 0.06f));
                        Art.Box(floor, line, c + new Vector3(o, 0.04f, 0), new Vector3(0.06f, 0.01f, s));
                    }
                }
                for (int side = 0; side < 4; side++)
                {
                    bool alongX = side < 2;
                    float sign = side % 2 == 0 ? 1 : -1;
                    Vector3 mid = c + (alongX ? new Vector3(0, 0.05f, sign * Cfg.BaseHalf) : new Vector3(sign * Cfg.BaseHalf, 0.05f, 0));
                    Art.Box(root, team, mid, alongX ? new Vector3(s + 0.6f, 0.05f, 0.6f) : new Vector3(0.6f, 0.05f, s + 0.6f));
                    for (int k = 0; k <= cells; k += 2)
                    {
                        float o = -Cfg.BaseHalf + k * Cfg.Cell;
                        Vector3 p = c + (alongX ? new Vector3(o, 0.5f, sign * (Cfg.BaseHalf + 0.5f)) : new Vector3(sign * (Cfg.BaseHalf + 0.5f), 0.5f, o));
                        Art.Box(root, team, p, new Vector3(0.18f, 1f, 0.18f));
                    }
                }
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 p = c + new Vector3(sx * (Cfg.BaseHalf + 1f), 0, sz * (Cfg.BaseHalf + 1f));
                    Art.Box(root, Art.DarkWood, p + Vector3.up * 3.5f, new Vector3(0.2f, 7f, 0.2f), default, true);
                    var flagBox = Art.Box(root, team, p + new Vector3(0.7f, 6.3f, 0), new Vector3(1.4f, 0.9f, 0.05f)).GetComponent<Renderer>();
                    // Normal graphics: the flag is cloth that waves in the wind (PSX / AI PSX keep the plain one)
                    var cloth = WorldDressing.Flag(root, p + new Vector3(0.1f, 6.75f, 0), team, 1.4f, 0.9f);
                    if (cloth != null) { look.Normal.Add(cloth); look.Other.Add(flagBox); }
                }
                BuildBedrock(root, t);
            }

            // ---------- the glass dome over the whole map (the edge of the map; the walls below are invisible in Normal) ----------
            MapDome.Build(root, look);

            // ---------- glass wall between the halves (until the ball drops): it fills the dome's cross-section ----------
            BuildGlassWall(root, half);

            // (no watch towers any more)
            if (ThemeMaps.IsTheme) ThemeMaps.BuildProps(root); // THEME MAPS

            // ---------- the crashed UFO round the ball, in the ball zone (no yellow circle any more: the crash's dirt and rubble) ----------
            CrashSite.Build(root);

            // ---------- map boundary ----------
            // the walls are still what stops you at the edge, but in Normal graphics they're invisible: the glass dome over
            // the map (MapDome, built above) is what you see. PSX / AI PSX keep them, in concrete.
            var wallC = new Color(0.45f, 0.43f, 0.4f);
            float wh = Cfg.Map == MapKind.Highlands || ThemeMaps.IsTheme /* THEME MAPS */ ? 40f : 5f;
            {
                var walls = new List<Renderer>();
                for (int k = 0; k < 4; k++)
                {
                    bool alongX = k < 2;
                    float sign = k % 2 == 0 ? 1f : -1f;
                    var at = alongX ? new Vector3(0, wh / 2 - (wh > 5 ? 15 : 0), sign * (half + 1)) : new Vector3(sign * (half + 1), wh / 2 - (wh > 5 ? 15 : 0), 0);
                    var size = alongX ? new Vector3(2 * half + 4, wh, 2) : new Vector3(2, wh, 2 * half + 4);
                    var r = Art.Box(root, wallC, at, size, default, true).GetComponent<Renderer>();
                    r.name = "map wall";
                    walls.Add(r);
                    look.Other.Add(r);
                }
                // PSX graphics: the boundary walls are concrete
                PsxModels.Retexture(root.gameObject, walls, r => "concrete_01", 3f);
            }

            // distant low-poly mountains for a horizon (PSX graphics: the big PSX terrain rocks). Solid (a convex hull of the rock):
            // the nearest ones poke in past the boundary wall, and you used to walk straight through them
            using (ColorSlots.Use(ColorSlots.Mountains))
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f + R(-0.05f, 0.05f);
                float d = R(half * 1.5f, half * 1.9f);
                float sc = R(20f, 45f) * Mathf.Max(0.6f, half / 100f);
                var m = Art.Part(root, Art.MakeRock(i, 0.35f), Color.Lerp(new Color(0.42f, 0.45f, 0.42f), new Color(0.55f, 0.55f, 0.6f), R(0, 1)),
                    new Vector3(Mathf.Cos(a) * d, sc * 0.2f, Mathf.Sin(a) * d), new Vector3(sc, sc * R(0.6f, 1.1f), sc), new Vector3(0, R(0, 360), 0), true);
                m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                PsxModels.Replace(m.transform, "bigrock" + (i % 6), PsxModels.Fit.Uniform);
            }
            // further out, layers of mountain ranges, each taller and paler; and big boulders about the map (MapScenery)
            MapScenery.Build(root);

            BuildArena(root);
            if (Cfg.Map == MapKind.Plains || Cfg.Map == MapKind.Highlands)
            {
                GrassField.Build(root); // tufts of grass (Normal graphics)
                CloudLayer.Build(root);  // clouds drifting over (Normal graphics)
                SkySun.Build(root);      // the low-poly sun (Normal graphics)
                if (Cfg.Map == MapKind.Highlands) SkyPlanets.Build(root); // big planets in the sky (Normal graphics)
            }
            look.Done();
            if (AiPsxArt.On) AiPsxArt.ApplyWorld(root);
        }

        // =====================================================================
        // Highlands terrain
        // =====================================================================

        static float SmoothStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        static float Raw(float x, float z)
        {
            float ox = (Cfg.MapSeed % 1000) * 1.37f + 100f, oz = (Cfg.MapSeed / 1000 % 1000) * 0.91f + 50f;
            float f = 0, amp = 1, freq = 0.02f, norm = 0;
            for (int o = 0; o < 4; o++)
            {
                f += Mathf.PerlinNoise(x * freq + ox, z * freq + oz) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.1f;
            }
            f /= norm;
            float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.028f + oz, z * 0.028f + ox) * 2f - 1f);
            return (f - 0.42f) * 16f + ridge * ridge * 7f;
        }

        /// <summary>The craggy bumps on the rocky rise at the edge of the map (metres, about -4 to 4).</summary>
        static float Crag(float x, float z)
        {
            float o = (Cfg.MapSeed % 997) * 0.37f;
            float a = Mathf.PerlinNoise(x * 0.09f + o, z * 0.09f + 40f) - 0.5f;
            float b = Mathf.PerlinNoise(x * 0.23f + 13f, z * 0.23f + o) - 0.5f;
            return a * 9f + b * 4f;
        }

        /// <summary>Highlands: is this part of the ground the rocky rise at the edge of the map? (Drawn faceted - flat-shaded,
        /// like the mountain rocks - not smooth like the hills.)</summary>
        public static bool EdgeRise(float x, float z) => Cfg.Map == MapKind.Highlands && Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > Cfg.MapHalf - 12f;

        /// <summary>Ground height at (x, z). Bases and the ball zone are flat (y = 0) so building and the drop work the same.</summary>
        public static float Height(float x, float z) => Height(x, z, out _, out _);

        /// <summary>Height, and the two things it's made of: the hills as they'd be (`hills`) and how much of them there
        /// is here (`mask`: 0 on the bases and in the middle, which are flat, 1 out in the wild).</summary>
        static float Height(float x, float z, out float hills, out float mask)
        {
            hills = 0f;
            mask = 1f;
            if (ThemeMaps.IsTheme) return ThemeMaps.Height(x, z); // THEME MAPS
            if (Cfg.Map != MapKind.Highlands) return 0f;
            // symmetric: every team gets the same terrain (point mirror, or four ways round)
            float h = Cfg.FourWay ? 0.25f * (Raw(x, z) + Raw(-z, x) + Raw(-x, -z) + Raw(z, -x)) : 0.5f * (Raw(x, z) + Raw(-x, -z));
            float dBase = float.MaxValue;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var c = Cfg.BaseCenter[t];
                float dx = Mathf.Max(0, Mathf.Abs(x - c.x) - Cfg.BaseHalf), dz = Mathf.Max(0, Mathf.Abs(z - c.z) - Cfg.BaseHalf);
                dBase = Mathf.Min(dBase, Mathf.Sqrt(dx * dx + dz * dz));
            }
            // (flat out to 13 m round the middle: the crash site's dirt lies on it - CrashSite.DirtR)
            mask = Mathf.Min(SmoothStep(2f, 16f, dBase), SmoothStep(13f, 27f, new Vector2(x, z).magnitude));
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (Cfg.MapHalf - 14f);
            if (edge > 0)
            {
                h += edge * 0.9f;
                // out past the dome the rise is broken stone: crags and ledges (the same for every team), growing as it
                // climbs (none inside the dome - its foot and trim follow the ground there, and you walk on it)
                if (edge > 16f)
                {
                    float crag = Cfg.FourWay ? 0.25f * (Crag(x, z) + Crag(-z, x) + Crag(-x, -z) + Crag(z, -x)) : 0.5f * (Crag(x, z) + Crag(-x, -z));
                    h += crag * Mathf.Clamp01((edge - 16f) / 10f);
                }
            }
            hills = Mathf.Max(h, -2.5f);
            return hills * mask;
        }

        static void BuildTerrain(Transform root)
        {
            float half = Cfg.MapHalf + 30f;
            const float step = 2f;
            int n = Mathf.CeilToInt(half * 2f / step);
            var hs = new float[n + 1, n + 1];
            var hills = new float[n + 1, n + 1];
            var mask = new float[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                hs[i, j] = Height(-half + i * step, -half + j * step, out hills[i, j], out mask[i, j]);

            // smooth-shaded: grass on gentle slopes, rock on steep ones, split along one clean line (see RockFieldOf)
            s_RockField = RockFieldOf(hills, mask, half, step);
            s_FieldHalf = half;
            s_FieldStep = step;
            var mesh = SmoothGround("Terrain", hs, half, step, 2, null, s_RockField, c => EdgeRise(c.x, c.z));

            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { WorldLook.GroundMaterial(false), WorldLook.GroundMaterial(true) }; // (flat colours, picked in Settings > Display)
            {
                Material[] saved = null;
                PsxModels.Look(go, () => { saved = mr.sharedMaterials; mr.sharedMaterials = new[] { PsxModels.Tiled("grass_21", 1f / 3f, 1f / 3f), PsxModels.Tiled("cobble_12", 1f / 3f, 1f / 3f) }; },
                    () => { if (saved != null) mr.sharedMaterials = saved; });
            }
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<GroundMarker>();

            // spots for the stone nodes on the hills (NetGame spawns real, minable nodes there)
            var rng = new System.Random(Cfg.MapSeed + 3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int count = Mathf.RoundToInt(18 * (Cfg.MapHalf / 100f));
            for (int k = 0; k < count; k++)
            {
                var p = new Vector3(R(-Cfg.MapHalf + 10, Cfg.MapHalf - 10), 0, R(-Cfg.MapHalf + 10, -12));
                if (!Cfg.InFirstSector(p, 8f) || Height(p.x, p.z) < 2f) continue;
                WildRocks.Add(p);
            }
        }

        /// <summary>
        /// A smooth-shaded ground mesh from a grid of heights (hs[i, j] at x = -half + i * step, z = -half + j * step):
        /// every corner is shared by the triangles round it and its normal comes from the slope of the heights there, so
        /// the light changes smoothly over the hills instead of facet by facet. Each triangle goes in the submesh `sub`
        /// picks from its centre and how upright it is (its own normal's y), so the grass / rock split stays crisp.
        /// With `split` (a value per corner, Highlands: RockFieldOf) it's two submeshes instead - 0 where split &lt; 0, 1
        /// where it's &gt;= 0 - and the triangles the line crosses are cut along it (where split is 0 along each edge), so
        /// the edge of the rock is one clean, smooth line instead of following the triangles.
        /// World-space UVs (a metre each) for the PSX graphics' textures.
        /// </summary>
        public static Mesh SmoothGround(string name, float[,] hs, float half, float step, int subCount, System.Func<Vector3, float, int> sub, float[,] split = null, System.Func<Vector3, bool> faceted = null)
        {
            int n = hs.GetLength(0) - 1;
            int corners = (n + 1) * (n + 1);
            var verts = new List<Vector3>(corners);
            var normals = new List<Vector3>(corners);
            var uvs = new List<Vector2>(corners);
            var f = split != null ? new float[corners] : null;
            int I(int i, int j) => i * (n + 1) + j;
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                var v = new Vector3(-half + i * step, hs[i, j], -half + j * step);
                verts.Add(v);
                uvs.Add(new Vector2(v.x, v.z));
                int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(n, i + 1), j0 = Mathf.Max(0, j - 1), j1 = Mathf.Min(n, j + 1);
                float dx = (hs[i1, j] - hs[i0, j]) / ((i1 - i0) * step), dz = (hs[i, j1] - hs[i, j0]) / ((j1 - j0) * step);
                normals.Add(new Vector3(-dx, 1f, -dz).normalized);
                if (f != null) f[I(i, j)] = split[i, j];
            }
            if (f != null) subCount = 2;
            var subs = new List<int>[subCount];
            for (int s = 0; s < subCount; s++) subs[s] = new List<int>();
            // (split) where the line crosses an edge: one new corner there, shared by the triangles on both sides
            var cuts = new Dictionary<long, int>();
            int Cut(int a, int b)
            {
                long key = a < b ? (long)a * corners + b : (long)b * corners + a;
                if (cuts.TryGetValue(key, out int k)) return k;
                float t = f[a] / (f[a] - f[b]);
                verts.Add(Vector3.Lerp(verts[a], verts[b], t));
                normals.Add(Vector3.Lerp(normals[a], normals[b], t).normalized);
                uvs.Add(Vector2.Lerp(uvs[a], uvs[b], t));
                return cuts[key] = verts.Count - 1;
            }
            void Add(int s, int a, int b, int c)
            {
                if (faceted != null)
                {
                    // (faceted: the triangle gets corners of its own, all with its own face's normal - flat-shaded)
                    Vector3 pa = verts[a], pb = verts[b], pc = verts[c];
                    if (faceted((pa + pb + pc) / 3f))
                    {
                        var nrm = Vector3.Cross(pb - pa, pc - pa).normalized;
                        if (nrm.y < 0f) nrm = -nrm;
                        int k0 = verts.Count;
                        verts.Add(pa); verts.Add(pb); verts.Add(pc);
                        normals.Add(nrm); normals.Add(nrm); normals.Add(nrm);
                        uvs.Add(uvs[a]); uvs.Add(uvs[b]); uvs.Add(uvs[c]);
                        a = k0; b = k0 + 1; c = k0 + 2;
                    }
                }
                subs[s].Add(a); subs[s].Add(b); subs[s].Add(c);
            }
            void Tri(int a, int b, int c)
            {
                if (f == null)
                {
                    Vector3 pa = verts[a], pb = verts[b], pc = verts[c];
                    var nrm = Vector3.Cross(pb - pa, pc - pa).normalized;
                    Add(Mathf.Clamp(sub((pa + pb + pc) / 3f, nrm.y), 0, subCount - 1), a, b, c);
                    return;
                }
                bool ra = f[a] >= 0f, rb = f[b] >= 0f, rc = f[c] >= 0f;
                if (ra == rb && rb == rc) { Add(ra ? 1 : 0, a, b, c); return; }
                // turn it round (keeping the winding) so `a` is the corner on its own side of the line
                if (rb != ra && rb != rc) { (a, b, c) = (b, c, a); ra = rb; }
                else if (rc != ra && rc != rb) { (a, b, c) = (c, a, b); ra = rc; }
                int ab = Cut(a, b), ac = Cut(a, c);
                Add(ra ? 1 : 0, a, ab, ac);
                Add(ra ? 0 : 1, ab, b, c);
                Add(ra ? 0 : 1, ab, c, ac);
            }
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                int p00 = I(i, j), p10 = I(i + 1, j), p01 = I(i, j + 1), p11 = I(i + 1, j + 1);
                if (((i + j) & 1) == 0) { Tri(p00, p01, p11); Tri(p00, p11, p10); }
                else { Tri(p00, p01, p10); Tri(p10, p01, p11); }
            }
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- where the Highlands hills are rock ----

        /// <summary>How steep (rise over run) the ground has to be, smoothed over a few metres, to be rock (about 25 degrees).</summary>
        public const float RockSlope = 0.46f;
        static float[,] s_RockField;
        static float s_FieldHalf, s_FieldStep;

        /// <summary>
        /// Highlands: which side of the rock line (x, z) is on - 0 and up is rock, below 0 grass (about -0.15 is a few
        /// metres out onto the grass). The ground is drawn split along the 0 line and the grass thins out towards it.
        /// -1 (grass) on the other maps.
        /// </summary>
        public static float RockField(float x, float z)
        {
            var g = s_RockField;
            if (g == null || Cfg.Map != MapKind.Highlands) return -1f;
            int n = g.GetLength(0) - 1;
            float fx = Mathf.Clamp((x + s_FieldHalf) / s_FieldStep, 0f, n - 0.001f), fz = Mathf.Clamp((z + s_FieldHalf) / s_FieldStep, 0f, n - 0.001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            return Mathf.Lerp(Mathf.Lerp(g[i, j], g[i + 1, j], u), Mathf.Lerp(g[i, j + 1], g[i + 1, j + 1], u), v);
        }

        /// <summary>
        /// The rock / grass field over the terrain's grid (rock where it's 0 or more). It used to be each triangle on its
        /// own by its steepness, which on a 2 m grid over noisy hills came out patchy: odd rock triangles dotted over the
        /// grass, grass specks in the rock faces and a ragged zig-zag edge. Now it's the hills' own steepness at each corner (not
        /// where the ground just rises off a flat base or the middle to meet them), smoothed
        /// over about 6 m (so the little bumps the noise adds don't count, only the shape of the hill), less RockSlope;
        /// the steep rise at the edge of the map is all rock; rock patches too small to be a face (under about 50 m2) go
        /// back to grass and grass holes up to about 160 m2 in the rock are filled in; and the line between them is cut
        /// through the triangles (SmoothGround), so it's smooth.
        /// </summary>
        static float[,] RockFieldOf(float[,] hills, float[,] mask, float half, float step)
        {
            int n = hills.GetLength(0) - 1;
            var g = new float[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                // (the hills' own steepness, as much of it as there is here: where the ground just rises from a flat base
                // or the middle to meet the hills, that rise isn't a rock face)
                int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(n, i + 1), j0 = Mathf.Max(0, j - 1), j1 = Mathf.Min(n, j + 1);
                float dx = (hills[i1, j] - hills[i0, j]) / ((i1 - i0) * step), dz = (hills[i, j1] - hills[i, j0]) / ((j1 - j0) * step);
                g[i, j] = Mathf.Sqrt(dx * dx + dz * dz) * mask[i, j];
            }
            for (int pass = 0; pass < 2; pass++) g = Blur(g);
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                g[i, j] -= RockSlope;
                // (the rise at the edge, from 14 m in - see Height: rock all the way once it's under way)
                float edge = Mathf.Max(Mathf.Abs(-half + i * step), Mathf.Abs(-half + j * step)) - (Cfg.MapHalf - 14f);
                if (edge > 0f) g[i, j] += Mathf.Clamp01(edge / 3f) * 0.5f;
            }
            Tidy(g, true, 12); // (a corner is 4 m2)
            Tidy(g, false, 40);
            return g;
        }

        /// <summary>A 1-2-1 blur, along both axes.</summary>
        static float[,] Blur(float[,] g)
        {
            int n = g.GetLength(0) - 1;
            var t = new float[n + 1, n + 1];
            var o = new float[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                t[i, j] = (g[Mathf.Max(0, i - 1), j] + 2f * g[i, j] + g[Mathf.Min(n, i + 1), j]) * 0.25f;
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                o[i, j] = (t[i, Mathf.Max(0, j - 1)] + 2f * t[i, j] + t[i, Mathf.Min(n, j + 1)]) * 0.25f;
            return o;
        }

        /// <summary>Patches of rock (or of grass, away from the edge of the grid) with fewer than `min` corners are pushed
        /// just over to the other side of the line.</summary>
        static void Tidy(float[,] g, bool rock, int min)
        {
            int n = g.GetLength(0) - 1;
            var seen = new bool[n + 1, n + 1];
            var patch = new List<(int i, int j)>();
            var stack = new Stack<(int i, int j)>();
            bool Side(int i, int j) => (g[i, j] >= 0f) == rock;
            void Visit(int i, int j) { if (i >= 0 && j >= 0 && i <= n && j <= n && !seen[i, j] && Side(i, j)) { seen[i, j] = true; stack.Push((i, j)); } }
            for (int si = 0; si <= n; si++)
            for (int sj = 0; sj <= n; sj++)
            {
                if (seen[si, sj] || !Side(si, sj)) continue;
                patch.Clear();
                bool edge = false;
                Visit(si, sj);
                while (stack.Count > 0)
                {
                    var (i, j) = stack.Pop();
                    patch.Add((i, j));
                    if (i == 0 || j == 0 || i == n || j == n) edge = true;
                    Visit(i - 1, j); Visit(i + 1, j); Visit(i, j - 1); Visit(i, j + 1);
                }
                if (patch.Count >= min || (!rock && edge)) continue;
                foreach (var (i, j) in patch) g[i, j] = rock ? Mathf.Min(g[i, j], 0f) - 0.02f : Mathf.Max(g[i, j], 0f) + 0.02f;
            }
        }

        // =====================================================================
        // Bedrock spawn platform + alien machine
        // =====================================================================

        static readonly Color k_Silver = new Color(0.72f, 0.74f, 0.78f), k_SilverDark = new Color(0.5f, 0.52f, 0.56f);

        /// <summary>Unbreakable silver bedrock in the middle of the base (you spawn on it) with the alien machine on its back edge.</summary>
        static void BuildBedrock(Transform root, int team)
        {
            var c = Cfg.BedrockCenter(team);
            var tc = Cfg.TeamColor[team];
            var glow = Color.Lerp(tc, Color.white, 0.35f);
            float s = Cfg.BedrockHalf * 2f;
            var go = new GameObject("Bedrock " + Cfg.TeamName[team]);
            go.transform.SetParent(root, false);
            go.transform.position = c;
            var t = go.transform;
            // same height as a foundation (top at BaseY) so foundations and walls line up with it
            using (ColorSlots.Use(ColorSlots.Bedrock))
            {
                Art.Box(t, k_Silver, new Vector3(0, Cfg.BaseY * 0.5f, 0), new Vector3(s, Cfg.BaseY, s), default, true);
                Art.Box(t, k_SilverDark, new Vector3(0, Cfg.BaseY * 0.78f, 0), new Vector3(s + 0.04f, 0.1f, s + 0.04f));
                Art.Box(t, k_SilverDark, new Vector3(0, Cfg.BaseY * 0.25f, 0), new Vector3(s + 0.04f, 0.1f, s + 0.04f));
            }
            // glowing seams and rivets on top
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, glow, new Vector3(k * 1.5f, Cfg.BaseY + 0.005f, 0), new Vector3(0.05f, 0.01f, s - 0.2f));
                Art.Box(t, glow, new Vector3(0, Cfg.BaseY + 0.005f, k * 1.5f), new Vector3(s - 0.2f, 0.01f, 0.05f));
            }
            using (ColorSlots.Use(ColorSlots.Bedrock))
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Art.Part(t, Art.Cylinder, k_SilverDark, new Vector3(x * (Cfg.BedrockHalf - 0.25f), Cfg.BaseY + 0.01f, z * (Cfg.BedrockHalf - 0.25f)), new Vector3(0.22f, 0.02f, 0.22f));
            // PSX graphics: concrete
            PsxModels.Retexture(go, go.GetComponentsInChildren<Renderer>(), r => Art.IsArtMat(r.sharedMaterial, out var col) && (col == k_Silver || col == k_SilverDark) ? (col == k_Silver ? "concrete_00" : "concrete_10") : null, 2f);
            BuildMachine(root, team, glow);
            if (Cfg.AutoWood) WoodMachine.Create(root, team); // Auto Wood: the wood machine, right of the alien machine
        }

        static void BuildMachine(Transform root, int team, Color glow)
        {
            using var tint = ColorSlots.Use(ColorSlots.AlienMachine); // (Settings > Display colours)
            var go = new GameObject("AlienMachine " + Cfg.TeamName[team]);
            go.transform.SetParent(root, false);
            // local +z faces the spawn / the middle of the map
            go.transform.SetPositionAndRotation(Cfg.MachinePos(team), Quaternion.LookRotation(-Cfg.BackDir(team)));
            var t = go.transform;
            var metal = new Color(0.3f, 0.33f, 0.36f);
            var alien = new Color(0.45f, 0.95f, 0.55f);
            var screen = Art.Ghost(new Color(0.4f, 1f, 0.8f, 0.7f));

            // heavy base and the main column
            Art.Box(t, metal, new Vector3(0, 0.25f, 0), new Vector3(2.7f, 0.5f, 1.2f), default, true);
            Art.Box(t, k_SilverDark, new Vector3(0, 0.52f, 0), new Vector3(2.5f, 0.06f, 1.05f));
            Art.Part(t, Art.Cylinder, k_Silver, new Vector3(0, 1.45f, -0.05f), new Vector3(0.9f, 0.95f, 0.9f), default, true);
            Art.Part(t, Art.Cylinder, metal, new Vector3(0, 2.45f, -0.05f), new Vector3(1.15f, 0.08f, 1.15f));
            // side pylons with glowing tips
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, metal, new Vector3(k * 1.1f, 1.2f, -0.15f), new Vector3(0.28f, 1.4f, 0.28f), new Vector3(0, 0, -k * 8f), true);
                Art.Part(t, Art.Ico, glow, new Vector3(k * 1.2f, 2.05f, -0.15f), Vector3.one * 0.16f);
                Art.Part(t, Art.Cone, k_Silver, new Vector3(k * 1.2f, 2.12f, -0.15f), new Vector3(0.18f, 0.35f, 0.18f));
            }
            // slanted consoles with glowing screens facing you
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, metal, new Vector3(k * 0.75f, 0.85f, 0.45f), new Vector3(0.7f, 0.08f, 0.45f), new Vector3(-35, 0, 0), true);
                Art.Box(t, Color.white, new Vector3(k * 0.75f, 0.9f, 0.47f), new Vector3(0.6f, 0.02f, 0.36f), new Vector3(-35, 0, 0), false, screen);
            }
            // spinning rings around the column (animated by Machine)
            var rings = new GameObject("rings").transform;
            rings.SetParent(t, false);
            rings.localPosition = new Vector3(0, 1.55f, -0.05f);
            for (int r = 0; r < 2; r++)
            {
                var ring = new GameObject("ring").transform;
                ring.SetParent(rings, false);
                ring.localRotation = Quaternion.Euler(r == 0 ? 20f : -25f, 0, r == 0 ? 10f : -15f);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 30f * Mathf.Deg2Rad;
                    Art.Box(ring, i % 3 == 0 ? glow : k_Silver, new Vector3(Mathf.Sin(a) * 0.72f, 0, Mathf.Cos(a) * 0.72f), new Vector3(0.2f, 0.05f, 0.08f), new Vector3(0, i * 30f + 90f, 0));
                }
            }
            // floating orb on top
            var orb = Art.Part(t, Art.Ico, alien, new Vector3(0, 2.85f, -0.05f), Vector3.one * 0.32f).transform;
            Art.Part(orb, Art.Ico, Color.white, Vector3.zero, Vector3.one * 1.5f, default, false, Art.Ghost(new Color(0.5f, 1f, 0.6f, 0.3f)));

            // the socket: a cradle in front of the machine where the ball has to sit to win
            var socket = new GameObject("socket").transform;
            socket.SetParent(t, false);
            socket.position = Cfg.SocketPos(team) - Vector3.up * 0.64f; // on the bedrock
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f * Mathf.Deg2Rad;
                Art.Box(socket, i % 2 == 0 ? k_Silver : metal, new Vector3(Mathf.Sin(a) * 0.82f, 0.16f, Mathf.Cos(a) * 0.82f), new Vector3(0.5f, 0.32f, 0.14f), new Vector3(0, i * 36f, 0), true);
            }
            var pad = Art.Part(socket, Art.Cylinder, glow, new Vector3(0, 0.012f, 0), new Vector3(1.4f, 0.01f, 1.4f));
            // arch over the socket with an emitter pointing down into the hole
            for (int k = -1; k <= 1; k += 2)
                Art.Box(socket, metal, new Vector3(k * 1.05f, 1.0f, 0), new Vector3(0.14f, 2f, 0.14f), default, true);
            Art.Box(socket, metal, new Vector3(0, 2.02f, 0), new Vector3(2.24f, 0.16f, 0.2f));
            Art.Part(socket, Art.Cone, glow, new Vector3(0, 1.72f, 0), new Vector3(0.3f, 0.25f, 0.3f), new Vector3(180, 0, 0));
            var beam = Art.Part(socket, Art.Cylinder, Color.white, new Vector3(0, 1.0f, 0), new Vector3(0.9f, 0.95f, 0.9f), default, false, Art.Ghost(new Color(glow.r, glow.g, glow.b, 0.22f)));
            beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lg = new GameObject("machineLight");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, 2.2f, 0.6f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = alien;
            l.range = 7f;
            l.intensity = 1.8f;

            go.AddComponent<Machine>().Init(team, rings, orb, l, beam);
            // PSX graphics: the PSX alien machine stands where the machine was (the socket and its arch stay - that's where the ball goes)
            PsxModels.Replace(t, "machine", PsxModels.Fit.GroundTall, default, 1.5f, socket); // (a big beast, towering over the socket)
        }

        // =====================================================================
        // Glass wall between the two halves
        // =====================================================================

        /// <summary>Radius of the glass dome over the middle of the map (the ball waits under it until the walls drop).</summary>
        public const float DomeRadius = 11f;
        static readonly Color k_GlassColor = new Color(0.6f, 0.9f, 1f, 0.16f), k_GlassLine = new Color(0.75f, 0.95f, 1f, 0.45f);

        static void BuildGlassWall(Transform root, float half)
        {
            s_Glass = new GameObject("GlassWall");
            s_Glass.transform.SetParent(root, false);
            BuildGlassDome(s_Glass.transform);
            if (Cfg.FourWay)
            {
                // free for all: two diagonal walls in an X, one quarter of the map each
                for (int k = 0; k < 2; k++)
                {
                    var arm = new GameObject("glass" + k).transform;
                    arm.SetParent(s_Glass.transform, false);
                    arm.localRotation = Quaternion.Euler(0, 45f + 90f * k, 0);
                    BuildGlassPanel(arm, arm.localRotation * Vector3.right);
                }
                return;
            }
            BuildGlassPanel(s_Glass.transform, Vector3.right);
        }

        /// <summary>
        /// One glass wall right across the map (along local x, which is `dir` in the world), with a round notch in the middle
        /// where it meets the ball's dome: its bottom edge follows that dome over the top, so nothing crosses the middle and
        /// there's no gap to squeeze through. Its ends and top edge follow the map's glass dome (MapDome): it fills the
        /// dome's cross-section exactly, so nothing gets round or over it.
        /// </summary>
        static void BuildGlassPanel(Transform t, Vector3 dir)
        {
            float gy = Height(0, 0);
            float notch = DomeRadius - 0.25f; // (a hair inside the faceted dome: no gap where they meet)
            const float Bottom = -20f, Th = 0.15f;
            bool dome = MapDome.Built;
            float L = dome ? MapDome.ExtentAlong(dir) - 0.03f : Cfg.MapHalf + 2f;
            // the top edge: just under the map dome's glass (between its rings the glass is straight along here)
            float Top(float x)
            {
                float h = dome ? MapDome.HeightAt(dir.x * x, dir.z * x) : float.MinValue;
                return h == float.MinValue ? (dome ? MapDome.Shoulder : 80f) : h - 0.08f;
            }
            float Arc(float x) => gy + Mathf.Sqrt(Mathf.Max(0f, notch * notch - x * x));
            float Under(float x) => Mathf.Abs(x) < notch ? Arc(x) : Bottom;
            var xs = new List<float> { -L, L, 0f };
            const int arc = 20;
            for (int i = 0; i <= arc; i++) xs.Add(-notch + 2f * notch * i / arc);
            if (dome)
            {
                var rings = new List<float>();
                MapDome.RingsAlong(dir, rings);
                foreach (var r in rings) if (r < L - 0.01f && r > 0.01f) { xs.Add(r); xs.Add(-r); }
            }
            xs.Sort();
            for (int i = xs.Count - 1; i > 0; i--) if (xs[i] - xs[i - 1] < 0.01f) xs.RemoveAt(i);
            var mb = new MeshBatch();
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                // the outer pieces reach down into the ground; the ones in the middle sit on the ball's dome
                bool outer = Mathf.Abs((x0 + x1) * 0.5f) >= notch;
                float b0 = outer ? Bottom : Arc(x0), b1 = outer ? Bottom : Arc(x1);
                float t0 = Top(x0), t1 = Top(x1);
                foreach (float z in new[] { Th, -Th })
                    mb.Quad(new Vector3(x0, b0, z), new Vector3(x1, b1, z), new Vector3(x1, t1, z), new Vector3(x0, t0, z), new Vector3(0, 0, z), 4f);
                // the underside of the arch, following the ball's dome
                if (!outer)
                    mb.Quad(new Vector3(x0, b0, -Th), new Vector3(x1, b1, -Th), new Vector3(x1, b1, Th), new Vector3(x0, b0, Th), Vector3.down, 4f);
                // and the top edge, under the map dome
                mb.Quad(new Vector3(x0, t0, -Th), new Vector3(x1, t1, -Th), new Vector3(x1, t1, Th), new Vector3(x0, t0, Th), Vector3.up, 4f);
            }
            // the two ends, against the dome's straight sides
            foreach (float x in new[] { -L, L })
                mb.Quad(new Vector3(x, Bottom, -Th), new Vector3(x, Top(x), -Th), new Vector3(x, Top(x), Th), new Vector3(x, Bottom, Th), new Vector3(Mathf.Sign(x), 0, 0), 4f);
            var glass = mb.Build(t, "glass", Art.Ghost(k_GlassColor), false);
            var mc = glass.AddComponent<MeshCollider>();
            mc.sharedMesh = glass.GetComponent<MeshFilter>().sharedMesh;

            // how far out the wall still reaches at height y (its top edge comes down towards the ends)
            float Reach(float y)
            {
                float best = 0f;
                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    float x0 = xs[i], x1 = xs[i + 1];
                    if (x1 <= 0f) continue;
                    float a = Top(x0), b = Top(x1);
                    if (a >= y && b >= y) best = Mathf.Max(best, x1);
                    else if (a >= y && b < y) best = Mathf.Max(best, Mathf.Lerp(x0, x1, (a - y) / Mathf.Max(1e-4f, a - b)));
                }
                return best;
            }
            var line = Art.Ghost(k_GlassLine);
            var lines = new MeshBatch();
            float topMax = Top(0f);
            for (int k = 0; 0.6f + k * 4f < topMax - 0.5f; k++)
            {
                float y = 0.6f + k * 4f;
                float h = y - gy;
                float reach = Reach(y);
                if (reach < 0.5f) continue;
                float cut = h < notch ? Mathf.Sqrt(notch * notch - h * h) : 0f;
                if (cut <= 0f) lines.Box(new Vector3(0, y, 0), Quaternion.identity, new Vector3(2 * reach, 0.06f, 0.34f));
                else if (reach > cut)
                    for (int s = -1; s <= 1; s += 2)
                        lines.Box(new Vector3(s * (reach + cut) * 0.5f, y, 0), Quaternion.identity, new Vector3(reach - cut, 0.06f, 0.34f));
            }
            for (float x = -Mathf.Floor(L / 12f) * 12f; x <= L; x += 12f)
            {
                float b = Under(x), top = Top(x);
                if (top - b < 0.2f) continue;
                lines.Box(new Vector3(x, (b + top) * 0.5f, 0), Quaternion.identity, new Vector3(0.08f, top - b, 0.34f));
            }
            // and a frame line along its top edge, under the map dome
            for (int i = 0; i + 1 < xs.Count; i++)
                lines.Line(new Vector3(xs[i], Top(xs[i]) - 0.05f, 0), new Vector3(xs[i + 1], Top(xs[i + 1]) - 0.05f, 0), 0.34f, 0.08f, Vector3.up);
            lines.Build(t, "glass lines", line, false);
        }

        /// <summary>The glass dome over the middle of the map: a faceted, low-poly half sphere (solid), with a few ribs.</summary>
        static void BuildGlassDome(Transform parent)
        {
            float gy = Height(0, 0), R = DomeRadius;
            const int rings = 7, segs = 20;
            Vector3 P(int ring, int seg)
            {
                float lat = Mathf.PI * 0.5f * ring / rings, lon = Mathf.PI * 2f * seg / segs;
                return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * R, gy + Mathf.Sin(lat) * R, Mathf.Cos(lat) * Mathf.Sin(lon) * R);
            }
            var c = new Vector3(0, gy, 0);
            var mb = new MeshBatch();
            for (int s = 0; s < segs; s++)
            {
                // a short skirt into the ground, so there's never a gap under the edge
                Vector3 a = P(0, s), b = P(0, s + 1);
                mb.Quad(a + Vector3.down * 1.5f, b + Vector3.down * 1.5f, b, a, (a + b) * 0.5f - c, 4f);
                for (int r = 0; r < rings; r++)
                {
                    Vector3 p00 = P(r, s), p01 = P(r, s + 1), p10 = P(r + 1, s), p11 = P(r + 1, s + 1);
                    var outward = (p00 + p11) * 0.5f - c;
                    if (r == rings - 1) mb.Tri(p00, p01, p10, outward, 4f); // the top: a fan to the point
                    else mb.Quad(p00, p01, p11, p10, outward, 4f);
                }
            }
            var dome = mb.Build(parent, "glass dome", Art.Ghost(new Color(k_GlassColor.r, k_GlassColor.g, k_GlassColor.b, 0.2f)), false);
            var mc = dome.AddComponent<MeshCollider>();
            mc.sharedMesh = dome.GetComponent<MeshFilter>().sharedMesh;
            // ribs: a ring round the bottom and half way up, and every fourth meridian
            var ribs = new MeshBatch();
            for (int s = 0; s < segs; s++)
            {
                ribs.Line(P(0, s) + Vector3.up * 0.15f, P(0, s + 1) + Vector3.up * 0.15f, 0.22f, 0.06f, ((P(0, s) + P(0, s + 1)) * 0.5f - c).normalized);
                ribs.Line(P(3, s), P(3, s + 1), 0.1f, 0.05f, ((P(3, s) + P(3, s + 1)) * 0.5f - c).normalized);
                if (s % 4 != 0) continue;
                for (int r = 0; r < rings; r++)
                    ribs.Line(P(r, s), P(r + 1, s), 0.1f, 0.05f, ((P(r, s) + P(r + 1, s)) * 0.5f - c).normalized);
            }
            ribs.Build(parent, "glass dome ribs", Art.Ghost(k_GlassLine), false);
        }

        /// <summary>The wall (and the dome) still stands: up and not dropping. Everything that goes by the wall - hits across
        /// it, the ball under the dome, what's allowed where - takes it as down the moment it starts dropping.</summary>
        public static bool GlassUp => s_Glass != null && s_Glass.activeSelf && s_Dropping == null;
        /// <summary>The wall is sliding down into the ground right now (the picture only: it's already down for the game).</summary>
        public static bool GlassDropping => s_Dropping != null;
        /// <summary>The tinted base floors (with their build grid): only shown while the wall is up (tests).</summary>
        public static IReadOnlyList<GameObject> BaseFloors => s_BaseFloors;
        static readonly List<GameObject> s_BaseFloors = new List<GameObject>();
        static GlassWallDrop s_Dropping;

        /// <summary>The wall (and the dome) is up until the wall drops (driven by the match state on every peer, every frame).
        /// Down without DropGlassWall first (joining a match that's already past it, the fun modes): gone at once.</summary>
        public static void SetGlassWall(bool up)
        {
            // the bases' tinted build floors only while you build
            foreach (var f in s_BaseFloors) if (f != null && f.activeSelf != up) f.SetActive(up);
            if (s_Glass == null) return;
            if (up)
            {
                if (s_Dropping != null) { s_Dropping.Stop(); s_Dropping = null; }
                if (!s_Glass.activeSelf) s_Glass.SetActive(true);
                return;
            }
            if (s_Dropping == null && s_Glass.activeSelf) s_Glass.SetActive(false);
        }

        /// <summary>
        /// The build phase is over: the glass wall and the ball's dome slide down into the ground (every peer runs it from
        /// the moment its match state turns, so everyone sees the same thing). It counts as down straight away: its
        /// colliders go at once (the ball's free, nobody is stopped by glass that's on its way down), and the wall slides
        /// down - a shudder, then faster and faster, easing in at the bottom - with dust rolling out along its foot and a
        /// rumble, a crack of glass at the start and a thud at the end. Then it's switched off.
        /// </summary>
        public static void DropGlassWall()
        {
            if (s_Glass == null || !s_Glass.activeSelf || s_Dropping != null) return;
            foreach (var c in s_Glass.GetComponentsInChildren<Collider>()) c.enabled = false;
            s_Dropping = s_Glass.AddComponent<GlassWallDrop>();
        }

        /// <summary>Slides the glass wall down into the ground (MapBuilder.DropGlassWall), then switches it off.</summary>
        public class GlassWallDrop : MonoBehaviour
        {
            /// <summary>How long the slide takes (s), the shudder before it, and how far down it goes (its top edge, m).</summary>
            public const float Duration = 4.2f, Shudder = 0.45f;
            float m_T0, m_Depth, m_NextDust, m_NextRumble;
            Vector3 m_Start;
            readonly List<Vector3> m_Foot = new List<Vector3>();

            void Start()
            {
                m_T0 = Time.time;
                m_Start = transform.localPosition;
                float top = 0f;
                foreach (var r in GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
                m_Depth = top - Height(0, 0) + 2f;
                // where the dust comes up: along the wall's foot (and round the ball's dome)
                float half = Cfg.MapHalf;
                var dirs = new List<Vector3>();
                if (Cfg.FourWay) { dirs.Add(Quaternion.Euler(0, 45f, 0) * Vector3.right); dirs.Add(Quaternion.Euler(0, 135f, 0) * Vector3.right); }
                else dirs.Add(Vector3.right);
                foreach (var d in dirs)
                    for (float x = -half; x <= half; x += 3f)
                    {
                        if (Mathf.Abs(x) < DomeRadius) continue;
                        var p = d * x;
                        p.y = Height(p.x, p.z);
                        m_Foot.Add(p);
                    }
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI * 2f / 24f;
                    var p = new Vector3(Mathf.Cos(a) * DomeRadius, 0, Mathf.Sin(a) * DomeRadius);
                    p.y = Height(p.x, p.z);
                    m_Foot.Add(p);
                }
                Sfx.Play2D(Sfx.Glass, 0.5f);
                Sfx.Play2D(Sfx.StoneGrind, 0.55f);
                m_NextRumble = Time.time + 1.1f;
            }

            /// <summary>0..1: how far down it is at time t into the drop: still through the shudder, then faster and faster
            /// like something heavy giving way, easing in over the last quarter (the speed carries straight on between).</summary>
            public static float Progress(float t)
            {
                if (t <= Shudder) return 0f;
                float k = Mathf.Clamp01((t - Shudder) / (Duration - Shudder));
                const float s = 0.75f;
                if (k < s) return s * (k / s) * (k / s);
                float u = 1f - (k - s) / (1f - s);
                return s + (1f - s) * (1f - u * u);
            }

            void Update()
            {
                float t = Time.time - m_T0;
                float k = Progress(t);
                var shake = Vector3.zero;
                if (t < Duration)
                {
                    float amp = t < Shudder ? 0.06f : 0.025f * (1f - k);
                    shake = new Vector3(Mathf.Sin(t * 57f), 0, Mathf.Cos(t * 43f)) * amp;
                }
                transform.localPosition = m_Start + Vector3.down * (m_Depth * k) + shake;
                // dust rolling out along its foot while it goes
                if (t > Shudder * 0.6f && t < Duration && Time.time >= m_NextDust && m_Foot.Count > 0)
                {
                    m_NextDust = Time.time + 0.06f;
                    var cam = Camera.main;
                    for (int i = 0; i < 5; i++)
                    {
                        var p = m_Foot[Random.Range(0, m_Foot.Count)];
                        // (only where someone might see it)
                        if (cam != null && (p - cam.transform.position).sqrMagnitude > 90f * 90f) continue;
                        var col = Color.Lerp(new Color(0.55f, 0.48f, 0.38f), new Color(0.7f, 0.66f, 0.58f), Random.value);
                        FxParticle.Puff(p + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(0.1f, 0.8f), Random.Range(-1.5f, 1.5f)), col, Random.Range(1.4f, 2.6f));
                        FxParticle.Spawn(p + Vector3.up * 0.3f, new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(1f, 3.5f), Random.Range(-2.5f, 2.5f)), col * 0.85f, Random.Range(0.08f, 0.2f), Random.Range(0.6f, 1.2f), 9f, false);
                    }
                }
                if (t < Duration - 0.6f && Time.time >= m_NextRumble)
                {
                    m_NextRumble = Time.time + 1.1f;
                    Sfx.Play2D(Sfx.StoneGrind, 0.45f);
                }
                if (t >= Duration)
                {
                    Sfx.Play2D(Sfx.Thud, 0.8f);
                    Sfx.Play2D(Sfx.Boom, 0.25f);
                    Stop();
                    if (s_Dropping == this) s_Dropping = null;
                    gameObject.SetActive(false);
                }
            }

            /// <summary>Put the wall back where it was (solid again) and stop.</summary>
            public void Stop()
            {
                transform.localPosition = m_Start;
                foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
                Destroy(this);
            }
        }

        // =====================================================================

        /// <summary>Sudden death (and the waiting lobby): the Final Destination style platform in space, see SpaceArena.</summary>
        static void BuildArena(Transform root) => SpaceArena.Build(root);
    }

    /// <summary>Marks static ground colliders so placement/overlap tests can ignore them.</summary>
    public class GroundMarker : MonoBehaviour { }

    /// <summary>
    /// The alien machine on a base's bedrock: press E on it to craft; its socket is where the ball has to sit to win.
    /// Purely local visuals (the world is built identically on every peer).
    /// </summary>
    public class Machine : MonoBehaviour
    {
        public static readonly Machine[] ByTeam = new Machine[4];
        public int Team;
        Transform m_Rings, m_Orb;
        Light m_Light;
        GameObject m_Beam;
        Vector3 m_OrbBase;
        float m_Busy;

        public void Init(int team, Transform rings, Transform orb, Light light, GameObject beam)
        {
            Team = team;
            m_Rings = rings;
            m_Orb = orb;
            m_OrbBase = orb.localPosition;
            m_Light = light;
            m_Beam = beam;
            ByTeam[team] = this;
        }

        void OnDestroy()
        {
            if (ByTeam[Team] == this) ByTeam[Team] = null;
        }

        /// <summary>Something was crafted: spin up for a moment.</summary>
        public static void Pulse(int team)
        {
            var m = team >= 0 && team < 2 ? ByTeam[team] : null;
            if (m == null) return;
            m.m_Busy = 1f;
            Sfx.Play(Sfx.Zap, m.transform.position + Vector3.up * 1.5f, 0.5f);
            for (int i = 0; i < 5; i++)
                FxParticle.Puff(m.transform.position + Vector3.up * Random.Range(1f, 2.8f) + Random.insideUnitSphere * 0.4f, new Color(0.5f, 1f, 0.6f, 0.5f), Random.Range(0.3f, 0.6f));
        }

        void Update()
        {
            m_Busy = Mathf.Max(0f, m_Busy - Time.deltaTime);
            float spin = 40f + m_Busy * 600f;
            if (m_Rings)
                for (int i = 0; i < m_Rings.childCount; i++)
                    m_Rings.GetChild(i).Rotate(0, (i == 0 ? spin : -spin * 1.3f) * Time.deltaTime, 0, Space.Self);
            if (m_Orb) m_Orb.localPosition = m_OrbBase + Vector3.up * Mathf.Sin(Time.time * 2f) * 0.08f;
            var ball = Ball.Instance;
            bool socketed = ball != null && ball.IsSpawned && ball.SocketTeam.Value == Team;
            if (m_Beam && m_Beam.activeSelf == socketed) m_Beam.SetActive(!socketed);
            if (m_Light) m_Light.intensity = (socketed ? 4f : 1.8f) + m_Busy * 3f + Mathf.Sin(Time.time * 3f) * 0.2f;
        }
    }
}
