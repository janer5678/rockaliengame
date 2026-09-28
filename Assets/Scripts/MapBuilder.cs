using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Builds the static (non-networked) world identically on every peer from (map, size, seed):
    /// flat Plains or hilly Highlands terrain, the two bases with their crashed UFO spawns, and the sudden death arena.
    /// </summary>
    public static class MapBuilder
    {
        static readonly Color k_Grass = new Color(0.36f, 0.56f, 0.3f), k_Rock = new Color(0.47f, 0.45f, 0.42f);
        static Transform s_Root;
        public static int BuiltKey = -1, BuiltSeed;

        public static bool IsBuilt(int key, int seed) => s_Root != null && BuiltKey == key && BuiltSeed == seed;

        public static void Build()
        {
            if (s_Root) Object.Destroy(s_Root.gameObject);
            Cryo.All.Clear();
            s_Root = new GameObject("World").transform;
            BuiltKey = Cfg.MapKey;
            BuiltSeed = Cfg.MapSeed;
            var root = s_Root;
            var rng = new System.Random(99 + Cfg.MapSeed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = Cfg.MapHalf;

            // ---------- ground ----------
            if (Cfg.Map == MapKind.Highlands) BuildTerrain(root);
            else
            {
                float size = half * 2f + 60f;
                var ground = Art.Box(root, k_Grass, new Vector3(0, -0.5f, 0), new Vector3(size, 1, size), default, true);
                ground.name = "Ground";
                ground.AddComponent<GroundMarker>();
                int patches = Mathf.RoundToInt(70 * (half / 100f) * (half / 100f));
                for (int i = 0; i < patches; i++)
                {
                    var c = Color.Lerp(k_Grass, new Color(0.3f, 0.48f, 0.24f), R(0.3f, 1f));
                    float w = R(4f, 14f);
                    Art.Box(root, c, new Vector3(R(-half + 5, half - 5), 0.005f, R(-half + 5, half - 5)), new Vector3(w, 0.01f, w * R(0.5f, 1.5f)), new Vector3(0, R(0, 90), 0));
                }
            }

            // ---------- bases ----------
            for (int t = 0; t < 2; t++)
            {
                var c = Cfg.BaseCenter[t];
                var team = Cfg.TeamColor[t];
                var pad = Color.Lerp(new Color(0.5f, 0.45f, 0.35f), team, 0.35f);
                var line = Color.Lerp(pad, Color.white, 0.25f);
                float s = Cfg.BaseHalf * 2f;
                Art.Box(root, pad, c + new Vector3(0, 0.02f, 0), new Vector3(s, 0.03f, s));
                int cells = Mathf.RoundToInt(s / Cfg.Cell);
                for (int k = 0; k <= cells; k++)
                {
                    float o = -Cfg.BaseHalf + k * Cfg.Cell;
                    Art.Box(root, line, c + new Vector3(0, 0.04f, o), new Vector3(s, 0.01f, 0.06f));
                    Art.Box(root, line, c + new Vector3(o, 0.04f, 0), new Vector3(0.06f, 0.01f, s));
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
                    Art.Box(root, team, p + new Vector3(0.7f, 6.3f, 0), new Vector3(1.4f, 0.9f, 0.05f));
                }
                BuildUfo(root, t);
            }

            // ---------- centre ball drop zone ----------
            Art.Part(root, Art.Cylinder, new Color(0.85f, 0.75f, 0.3f), new Vector3(0, 0.02f, 0), new Vector3(12f, 0.02f, 12f));
            Art.Part(root, Art.Cylinder, new Color(0.95f, 0.88f, 0.45f), new Vector3(0, 0.03f, 0), new Vector3(9f, 0.02f, 9f));
            Art.Part(root, Art.Cylinder, new Color(0.85f, 0.75f, 0.3f), new Vector3(0, 0.04f, 0), new Vector3(2f, 0.02f, 2f));

            // ---------- map boundary ----------
            var wallC = new Color(0.45f, 0.43f, 0.4f);
            float wh = Cfg.Map == MapKind.Highlands ? 40f : 5f;
            Art.Box(root, wallC, new Vector3(0, wh / 2 - (wh > 5 ? 15 : 0), half + 1), new Vector3(2 * half + 4, wh, 2), default, true);
            Art.Box(root, wallC, new Vector3(0, wh / 2 - (wh > 5 ? 15 : 0), -half - 1), new Vector3(2 * half + 4, wh, 2), default, true);
            Art.Box(root, wallC, new Vector3(half + 1, wh / 2 - (wh > 5 ? 15 : 0), 0), new Vector3(2, wh, 2 * half + 4), default, true);
            Art.Box(root, wallC, new Vector3(-half - 1, wh / 2 - (wh > 5 ? 15 : 0), 0), new Vector3(2, wh, 2 * half + 4), default, true);

            // distant low-poly mountains for a horizon
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f + R(-0.05f, 0.05f);
                float d = R(half * 1.5f, half * 1.9f);
                float sc = R(20f, 45f) * Mathf.Max(0.6f, half / 100f);
                var m = Art.Part(root, Art.MakeRock(i, 0.35f), Color.Lerp(new Color(0.42f, 0.45f, 0.42f), new Color(0.55f, 0.55f, 0.6f), R(0, 1)),
                    new Vector3(Mathf.Cos(a) * d, sc * 0.2f, Mathf.Sin(a) * d), new Vector3(sc, sc * R(0.6f, 1.1f), sc), new Vector3(0, R(0, 360), 0));
                m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            BuildArena(root);
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

        /// <summary>Ground height at (x, z). Bases and the ball zone are flat (y = 0) so building and the drop work the same.</summary>
        public static float Height(float x, float z)
        {
            if (Cfg.Map != MapKind.Highlands) return 0f;
            float h = 0.5f * (Raw(x, z) + Raw(-x, -z)); // point-symmetric: both teams get the same terrain
            float dBase = float.MaxValue;
            for (int t = 0; t < 2; t++)
            {
                var c = Cfg.BaseCenter[t];
                float dx = Mathf.Max(0, Mathf.Abs(x - c.x) - Cfg.BaseHalf), dz = Mathf.Max(0, Mathf.Abs(z - c.z) - Cfg.BaseHalf);
                dBase = Mathf.Min(dBase, Mathf.Sqrt(dx * dx + dz * dz));
            }
            float mask = Mathf.Min(SmoothStep(2f, 16f, dBase), SmoothStep(9f, 24f, new Vector2(x, z).magnitude));
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (Cfg.MapHalf - 14f);
            if (edge > 0) h += edge * 0.9f;
            return Mathf.Max(h, -2.5f) * mask;
        }

        static void BuildTerrain(Transform root)
        {
            float half = Cfg.MapHalf + 30f;
            const float step = 2f;
            int n = Mathf.CeilToInt(half * 2f / step);
            var hs = new float[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                hs[i, j] = Height(-half + i * step, -half + j * step);

            // flat-shaded low-poly mesh: grass on gentle slopes, rock on steep ones
            var verts = new List<Vector3>();
            var grass = new List<int>();
            var rock = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var nrm = Vector3.Cross(b - a, c - a).normalized;
                var list = nrm.y < 0.8f ? rock : grass;
                int k = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                list.Add(k); list.Add(k + 1); list.Add(k + 2);
            }
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var p00 = new Vector3(-half + i * step, hs[i, j], -half + j * step);
                var p10 = new Vector3(-half + (i + 1) * step, hs[i + 1, j], -half + j * step);
                var p01 = new Vector3(-half + i * step, hs[i, j + 1], -half + (j + 1) * step);
                var p11 = new Vector3(-half + (i + 1) * step, hs[i + 1, j + 1], -half + (j + 1) * step);
                if (((i + j) & 1) == 0) { Tri(p00, p01, p11); Tri(p00, p11, p10); }
                else { Tri(p00, p01, p10); Tri(p10, p01, p11); }
            }
            var mesh = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(grass, 0);
            mesh.SetTriangles(rock, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { Art.Mat(k_Grass), Art.Mat(k_Rock) };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<GroundMarker>();

            // scattered boulders on the hills for cover
            var rng = new System.Random(Cfg.MapSeed + 3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int count = Mathf.RoundToInt(18 * (Cfg.MapHalf / 100f));
            for (int k = 0; k < count; k++)
            {
                var p = new Vector3(R(-Cfg.MapHalf + 10, Cfg.MapHalf - 10), 0, R(-Cfg.MapHalf + 10, -12));
                if (Height(p.x, p.z) < 2f) continue;
                for (int m = 0; m < 2; m++)
                {
                    var q = m == 0 ? p : -p;
                    float sc = R(1.5f, 3.2f);
                    Art.Part(root, Art.MakeRock(k * 7 + 1, 0.3f), k_Rock, new Vector3(q.x, Height(q.x, q.z) + sc * 0.2f, q.z), new Vector3(sc, sc * 0.8f, sc), new Vector3(0, R(0, 360), 0), true);
                }
            }
        }

        // =====================================================================
        // Crashed UFO spawn
        // =====================================================================

        static void BuildUfo(Transform root, int team)
        {
            var go = new GameObject("UFO " + Cfg.TeamName[team]);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(Cfg.UfoCenter(team), Quaternion.LookRotation(Cfg.UfoForward(team)));
            var t = go.transform;
            var hull = new Color(0.52f, 0.56f, 0.58f);
            var dark = new Color(0.28f, 0.3f, 0.33f);
            var tc = Cfg.TeamColor[team];
            var glow = Color.Lerp(tc, Color.white, 0.4f);

            // scorched crater + dirt thrown up behind the crash
            Art.Part(t, Art.Cylinder, new Color(0.22f, 0.2f, 0.17f), new Vector3(0, 0.015f, 0.6f), new Vector3(11.5f, 0.01f, 12.5f));
            Art.Part(t, Art.MakeRock(team + 40, 0.3f), new Color(0.4f, 0.32f, 0.22f), new Vector3(0, 0.2f, -5.2f), new Vector3(4.5f, 1.4f, 1.6f));
            Art.Part(t, Art.MakeRock(team + 41, 0.3f), new Color(0.36f, 0.3f, 0.2f), new Vector3(-3.6f, 0.2f, -3.8f), new Vector3(1.8f, 0.9f, 1.8f));
            Art.Part(t, Art.MakeRock(team + 42, 0.3f), new Color(0.36f, 0.3f, 0.2f), new Vector3(3.6f, 0.2f, -3.8f), new Vector3(1.8f, 1f, 1.8f));

            // floor (walkable) + ramp out of the door
            Art.Part(t, Art.Cylinder, dark, new Vector3(0, 0.15f, 0), new Vector3(9f, 0.15f, 9f));
            var floor = new GameObject("floorCol");
            floor.transform.SetParent(t, false);
            var fc = floor.AddComponent<BoxCollider>();
            fc.center = new Vector3(0, 0.15f, 0);
            fc.size = new Vector3(6.6f, 0.3f, 6.6f);
            Art.Box(t, dark, new Vector3(0, 0.15f, 4.7f), new Vector3(3.2f, 0.3f, 1.6f), default, true);
            Art.Box(t, hull, new Vector3(0, 0.1f, 5.9f), new Vector3(3f, 0.08f, 1.4f), new Vector3(8, 0, 0), true); // ramp

            // hull wall ring with a big open door at +z (the door can never be blocked: see Cfg.CellBlocked)
            const int segs = 18;
            const float r = 4.3f;
            for (int i = 0; i < segs; i++)
            {
                float a = i * 360f / segs;
                if (Mathf.Abs(Mathf.DeltaAngle(a, 0f)) < 25f) continue;
                var rad = a * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(rad) * r, 1.65f, Mathf.Cos(rad) * r);
                Art.Box(t, i % 2 == 0 ? hull : hull * 0.93f, p, new Vector3(2f * Mathf.PI * r / segs + 0.12f, 2.7f, 0.35f), new Vector3(0, a, 0), true);
                if (i % 3 == 0) Art.Box(t, glow, new Vector3(Mathf.Sin(rad) * (r + 0.19f), 2.2f, Mathf.Cos(rad) * (r + 0.19f)), new Vector3(0.35f, 0.12f, 0.03f), new Vector3(0, a, 0));
            }
            // door frame
            Art.Box(t, glow, new Vector3(-1.85f, 1.65f, 4.05f), new Vector3(0.15f, 2.7f, 0.15f));
            Art.Box(t, glow, new Vector3(1.85f, 1.65f, 4.05f), new Vector3(0.15f, 2.7f, 0.15f));
            Art.Box(t, glow, new Vector3(0, 3.0f, 4.05f), new Vector3(3.85f, 0.15f, 0.15f));

            // roof, tilted saucer rim and dome (the crash tilt is only on the rim so the floor stays level)
            Art.Part(t, Art.Cylinder, hull, new Vector3(0, 3.1f, 0), new Vector3(9.8f, 0.12f, 9.8f));
            var roof = new GameObject("roofCol");
            roof.transform.SetParent(t, false);
            var rc = roof.AddComponent<BoxCollider>();
            rc.center = new Vector3(0, 3.1f, 0);
            rc.size = new Vector3(7f, 0.3f, 7f);
            var rim = new GameObject("rim").transform;
            rim.SetParent(t, false);
            rim.localPosition = new Vector3(0, 2.95f, 0);
            rim.localRotation = Quaternion.Euler(7f, 0, 4f);
            Art.Part(rim, Art.Cylinder, hull * 0.9f, Vector3.zero, new Vector3(12.5f, 0.1f, 12.5f));
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Art.Box(rim, i % 2 == 0 ? glow : new Color(1f, 0.85f, 0.4f), new Vector3(Mathf.Sin(a) * 6.1f, 0.05f, Mathf.Cos(a) * 6.1f), new Vector3(0.3f, 0.14f, 0.3f));
            }
            Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 3.3f, 0), new Vector3(5f, 2.4f, 5f), default, false, Art.Ghost(new Color(0.5f, 0.9f, 1f, 0.55f)));
            Art.Part(t, Art.Cylinder, dark, new Vector3(0, 3.25f, 0), new Vector3(5.2f, 0.08f, 5.2f));
            // bent hull plate and debris
            Art.Box(t, hull * 0.8f, new Vector3(5.6f, 0.3f, 1.5f), new Vector3(1.6f, 0.08f, 1.1f), new Vector3(20, 35, 12));
            Art.Box(t, hull * 0.8f, new Vector3(-5.2f, 0.25f, 2.6f), new Vector3(1.1f, 0.08f, 0.9f), new Vector3(-15, -20, 25));

            // interior light
            var lg = new GameObject("ufoLight");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, 2.4f, -0.5f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = glow;
            l.range = 8f;
            l.intensity = 2.2f;

            BuildChamber(t, team, glow);
        }

        static void BuildChamber(Transform ufo, int team, Color glow)
        {
            var ch = new GameObject("CryoChamber").transform;
            ch.SetParent(ufo, false);
            ch.localPosition = new Vector3(0, 0.3f, -3.0f);
            var metal = new Color(0.4f, 0.43f, 0.47f);
            Art.Part(ch, Art.Cylinder, metal, new Vector3(0, 0.03f, 0), new Vector3(1.6f, 0.03f, 1.6f));
            Art.Part(ch, Art.Cylinder, glow, new Vector3(0, 0.07f, 0), new Vector3(1.25f, 0.01f, 1.25f));
            Art.Part(ch, Art.Cylinder, metal, new Vector3(0, 2.45f, 0), new Vector3(1.7f, 0.12f, 1.7f));
            // back shell (solid)
            for (int i = 0; i < 5; i++)
            {
                float a = (110f + i * 35f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 0.78f, 1.2f, Mathf.Cos(a) * 0.78f);
                Art.Box(ch, i % 2 == 0 ? metal : metal * 0.85f, p, new Vector3(0.5f, 2.4f, 0.12f), new Vector3(0, a * Mathf.Rad2Deg, 0), true);
            }
            // pipes into the hull
            Art.Box(ch, metal * 0.7f, new Vector3(0.35f, 1.9f, -0.9f), new Vector3(0.12f, 0.12f, 0.5f));
            Art.Box(ch, metal * 0.7f, new Vector3(-0.35f, 0.6f, -0.9f), new Vector3(0.12f, 0.12f, 0.5f));
            // glass front that slides up when you wake
            var glass = new GameObject("glass").transform;
            glass.SetParent(ch, false);
            var gm = Art.Ghost(new Color(0.55f, 0.95f, 1f, 0.35f));
            for (int i = 0; i < 4; i++)
            {
                float a = (-52f + i * 35f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 0.78f, 1.2f, Mathf.Cos(a) * 0.78f);
                var g = Art.Part(glass, Art.Cube, Color.white, p, new Vector3(0.5f, 2.35f, 0.04f), new Vector3(0, a * Mathf.Rad2Deg, 0), false, gm);
                g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            var lg = new GameObject("cryoLight");
            lg.transform.SetParent(ch, false);
            lg.transform.localPosition = new Vector3(0, 2.1f, 0.2f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.6f, 0.95f, 1f);
            l.range = 3f;
            l.intensity = 2f;
            var cryo = ch.gameObject.AddComponent<Cryo>();
            cryo.Init(team, glass, l);
        }

        // =====================================================================

        static void BuildArena(Transform root)
        {
            var c = Cfg.ArenaCenter;
            float s = Cfg.ArenaHalf;
            var sand = new Color(0.78f, 0.68f, 0.5f);
            var floor = Art.Box(root, sand, c + new Vector3(0, -0.5f, 0), new Vector3(2 * s + 4, 1, 2 * s + 4), default, true);
            floor.name = "ArenaFloor";
            floor.AddComponent<GroundMarker>();
            Art.Part(root, Art.Cylinder, new Color(0.7f, 0.3f, 0.25f), c + new Vector3(0, 0.02f, 0), new Vector3(10f, 0.02f, 10f));
            Art.Box(root, Cfg.TeamColor[0], c + new Vector3(0, 0.02f, -14f), new Vector3(4, 0.02f, 4));
            Art.Box(root, Cfg.TeamColor[1], c + new Vector3(0, 0.02f, 14f), new Vector3(4, 0.02f, 4));

            var wallC = new Color(0.5f, 0.4f, 0.32f);
            Art.Box(root, wallC, c + new Vector3(0, 3f, s + 1), new Vector3(2 * s + 4, 6, 2), default, true);
            Art.Box(root, wallC, c + new Vector3(0, 3f, -s - 1), new Vector3(2 * s + 4, 6, 2), default, true);
            Art.Box(root, wallC, c + new Vector3(s + 1, 3f, 0), new Vector3(2, 6, 2 * s + 4), default, true);
            Art.Box(root, wallC, c + new Vector3(-s - 1, 3f, 0), new Vector3(2, 6, 2 * s + 4), default, true);

            var pillar = new Color(0.6f, 0.55f, 0.5f);
            Vector3[] ps = { new Vector3(-7, 0, -5), new Vector3(7, 0, 5), new Vector3(-7, 0, 6), new Vector3(7, 0, -6) };
            foreach (var p in ps)
                Art.Box(root, pillar, c + p + Vector3.up * 2f, new Vector3(2f, 4f, 2f), new Vector3(0, 20, 0), true);
            for (int i = 0; i < 4; i++)
            {
                var lg = new GameObject("arenaLight");
                lg.transform.SetParent(root, false);
                lg.transform.position = c + new Vector3(i < 2 ? -s + 1 : s - 1, 5f, i % 2 == 0 ? -s + 1 : s - 1);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.55f, 0.25f);
                l.range = 25f;
                l.intensity = 2f;
            }
        }
    }

    /// <summary>Marks static ground colliders so placement/overlap tests can ignore them.</summary>
    public class GroundMarker : MonoBehaviour { }

    /// <summary>The cryo chamber you wake up in: the glass slides up with a burst of steam.</summary>
    public class Cryo : MonoBehaviour
    {
        public static readonly List<Cryo> All = new List<Cryo>();
        public int Team;
        Transform m_Glass;
        Light m_Light;
        float m_OpenAt = -10f;

        public void Init(int team, Transform glass, Light light)
        {
            Team = team;
            m_Glass = glass;
            m_Light = light;
            All.Add(this);
        }

        void OnDestroy() => All.Remove(this);

        public static void Open(int team)
        {
            foreach (var c in All) if (c.Team == team) c.DoOpen();
        }

        void DoOpen()
        {
            m_OpenAt = Time.time;
            Sfx.Play(Sfx.Hiss, transform.position + Vector3.up, 0.9f);
            for (int i = 0; i < 6; i++)
                FxParticle.Puff(transform.position + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.2f, 1.8f), Random.Range(0.2f, 0.9f)), new Color(0.9f, 0.97f, 1f, 0.6f), Random.Range(0.6f, 1.1f));
        }

        void Update()
        {
            // closed -> (0.5s) slides up -> stays open 2.5s -> slides back down
            float e = Time.time - m_OpenAt;
            float k = e < 0.35f ? 0f : e < 0.9f ? (e - 0.35f) / 0.55f : e < 3.4f ? 1f : e < 4.2f ? 1f - (e - 3.4f) / 0.8f : 0f;
            k = k * k * (3 - 2 * k);
            m_Glass.localPosition = new Vector3(0, k * 2.3f, 0);
            if (m_Light) m_Light.intensity = 2f + (e < 1.2f ? (1.2f - e) * 6f : 0f);
        }
    }
}
