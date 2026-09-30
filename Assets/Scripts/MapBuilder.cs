using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Builds the static (non-networked) world identically on every peer from (map, size, seed):
    /// flat Plains or hilly Highlands terrain (with watch towers), the two bases with their bedrock spawn and alien machine,
    /// the glass wall between the halves and the sudden death arena.
    /// </summary>
    public static class MapBuilder
    {
        static readonly Color k_Grass = new Color(0.36f, 0.56f, 0.3f), k_Rock = new Color(0.47f, 0.45f, 0.42f);
        static Transform s_Root;
        public static int BuiltKey = -1, BuiltSeed;

        static GameObject s_Glass;
        /// <summary>Highlands: where the hill rocks go (one half; NetGame mirrors them). They are real stone nodes.</summary>
        public static readonly List<Vector3> WildRocks = new List<Vector3>();

        public static bool IsBuilt(int key, int seed) => s_Root != null && BuiltKey == key && BuiltSeed == seed;

        public static void Build()
        {
            if (s_Root) Object.Destroy(s_Root.gameObject);
            s_Glass = null;
            WildRocks.Clear();
            Cfg.Towers.Clear();
            s_Root = new GameObject("World").transform;
            BuiltKey = Cfg.MapKey;
            BuiltSeed = Cfg.MapSeed;
            var root = s_Root;
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
                for (int i = 0; i < patches; i++)
                {
                    var c = Color.Lerp(k_Grass, new Color(0.3f, 0.48f, 0.24f), R(0.3f, 1f));
                    float w = R(4f, 14f);
                    Art.Box(root, c, new Vector3(R(-half + 5, half - 5), 0.005f, R(-half + 5, half - 5)), new Vector3(w, 0.01f, w * R(0.5f, 1.5f)), new Vector3(0, R(0, 90), 0));
                }
            }

            // ---------- bases ----------
            for (int t = 0; t < Cfg.TeamCount; t++)
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
                BuildBedrock(root, t);
            }

            // ---------- glass wall between the halves (until the ball drops) ----------
            BuildGlassWall(root, half);

            // ---------- watch towers (wild map) ----------
            if (Cfg.Map == MapKind.Highlands) PlaceTowers(root);
            if (ThemeMaps.IsTheme) ThemeMaps.BuildProps(root); // THEME MAPS

            // ---------- centre ball drop zone ----------
            Art.Part(root, Art.Cylinder, new Color(0.85f, 0.75f, 0.3f), new Vector3(0, 0.02f, 0), new Vector3(12f, 0.02f, 12f));
            Art.Part(root, Art.Cylinder, new Color(0.95f, 0.88f, 0.45f), new Vector3(0, 0.03f, 0), new Vector3(9f, 0.02f, 9f));
            Art.Part(root, Art.Cylinder, new Color(0.85f, 0.75f, 0.3f), new Vector3(0, 0.04f, 0), new Vector3(2f, 0.02f, 2f));

            // ---------- map boundary ----------
            var wallC = new Color(0.45f, 0.43f, 0.4f);
            float wh = Cfg.Map == MapKind.Highlands || ThemeMaps.IsTheme /* THEME MAPS */ ? 40f : 5f;
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
            Art.Box(t, k_Silver, new Vector3(0, Cfg.BaseY * 0.5f, 0), new Vector3(s, Cfg.BaseY, s), default, true);
            Art.Box(t, k_SilverDark, new Vector3(0, Cfg.BaseY * 0.78f, 0), new Vector3(s + 0.04f, 0.1f, s + 0.04f));
            Art.Box(t, k_SilverDark, new Vector3(0, Cfg.BaseY * 0.25f, 0), new Vector3(s + 0.04f, 0.1f, s + 0.04f));
            // glowing seams and rivets on top
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, glow, new Vector3(k * 1.5f, Cfg.BaseY + 0.005f, 0), new Vector3(0.05f, 0.01f, s - 0.2f));
                Art.Box(t, glow, new Vector3(0, Cfg.BaseY + 0.005f, k * 1.5f), new Vector3(s - 0.2f, 0.01f, 0.05f));
            }
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Art.Part(t, Art.Cylinder, k_SilverDark, new Vector3(x * (Cfg.BedrockHalf - 0.25f), Cfg.BaseY + 0.01f, z * (Cfg.BedrockHalf - 0.25f)), new Vector3(0.22f, 0.02f, 0.22f));
            BuildMachine(root, team, glow);
        }

        static void BuildMachine(Transform root, int team, Color glow)
        {
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
        }

        // =====================================================================
        // Glass wall between the two halves
        // =====================================================================

        static void BuildGlassWall(Transform root, float half)
        {
            s_Glass = new GameObject("GlassWall");
            s_Glass.transform.SetParent(root, false);
            if (Cfg.FourWay)
            {
                // free for all: two diagonal walls in an X, one quarter of the map each
                for (int k = 0; k < 2; k++)
                {
                    var arm = new GameObject("glass" + k).transform;
                    arm.SetParent(s_Glass.transform, false);
                    arm.localRotation = Quaternion.Euler(0, 45f + 90f * k, 0);
                    BuildGlassPanel(arm, half * 1.415f);
                }
                return;
            }
            BuildGlassPanel(s_Glass.transform, half);
        }

        static void BuildGlassPanel(Transform t, float half)
        {
            var glass = Art.Part(t, Art.Cube, Color.white, new Vector3(0, 30f, 0), new Vector3(2 * half + 4, 100f, 0.3f), default, true, Art.Ghost(new Color(0.6f, 0.9f, 1f, 0.16f)), "glass");
            glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var line = Art.Ghost(new Color(0.75f, 0.95f, 1f, 0.45f));
            for (int k = 0; k < 8; k++)
            {
                var b = Art.Part(t, Art.Cube, Color.white, new Vector3(0, 0.6f + k * 4f, 0), new Vector3(2 * half + 4, 0.06f, 0.34f), default, false, line);
                b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            for (float x = -half; x <= half; x += 12f)
            {
                var b = Art.Part(t, Art.Cube, Color.white, new Vector3(x, 30f, 0), new Vector3(0.08f, 100f, 0.34f), default, false, line);
                b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        public static bool GlassUp => s_Glass != null && s_Glass.activeSelf;

        /// <summary>The wall is up until the ball drops (driven by the match state on every peer).</summary>
        public static void SetGlassWall(bool up)
        {
            if (s_Glass == null || s_Glass.activeSelf == up) return;
            s_Glass.SetActive(up);
            if (up) return;
            // shatter
            float half = Cfg.MapHalf;
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(Random.Range(-half, half), Random.Range(0.5f, 12f), 0);
                p.y += Height(p.x, p.z);
                FxParticle.Spawn(p, new Vector3(Random.Range(-2f, 2f), Random.Range(0f, 3f), Random.Range(-3f, 3f)), new Color(0.75f, 0.95f, 1f), Random.Range(0.08f, 0.2f), Random.Range(0.8f, 1.6f), 14f, false);
            }
            Sfx.Play2D(Sfx.Smash, 0.6f);
        }

        // =====================================================================
        // Watch towers (wild map)
        // =====================================================================

        const float TowerH = 6f, TowerRampAngle = 36f;
        static float RampRun => TowerH / Mathf.Tan(TowerRampAngle * Mathf.Deg2Rad);

        static void PlaceTowers(Transform root)
        {
            var rng = new System.Random(Cfg.MapSeed * 7 + 11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = Cfg.MapHalf;
            int want = Cfg.SmallMap ? 1 : Mathf.RoundToInt(2 * Cfg.SizeScale);
            var mine = new List<(Vector3 p, float yaw)>();
            for (int tries = 0; tries < 300 && mine.Count < want; tries++)
            {
                var p = new Vector3(R(-half + 16, half - 16), 0, R(-half + 16, -14));
                if (!Cfg.InFirstSector(p, 10f)) continue;
                var bc = Cfg.BaseCenter[0];
                if (Mathf.Max(Mathf.Abs(p.x - bc.x), Mathf.Abs(p.z - bc.z)) < Cfg.BaseHalf + 12f) continue;
                if (new Vector2(p.x, p.z).magnitude < 24f) continue;
                bool close = false;
                foreach (var q in mine) for (int m = 0; m < Cfg.Copies; m++) if ((Cfg.Copy(q.p, m) - p).magnitude < 35f) close = true;
                if (close) continue;
                float yaw = Mathf.Floor(R(0, 3.99f)) * 90f;
                var foot = p + Quaternion.Euler(0, yaw, 0) * Vector3.back * (1.9f + RampRun);
                float h0 = Height(p.x, p.z);
                bool flat = Mathf.Abs(Height(foot.x, foot.z) - h0) < 1.2f;
                for (int k = 0; k < 4 && flat; k++)
                {
                    var o = Quaternion.Euler(0, k * 90f + 45f, 0) * Vector3.forward * 2.5f;
                    if (Mathf.Abs(Height(p.x + o.x, p.z + o.z) - h0) > 1.5f) flat = false;
                }
                if (!flat && tries < 250) continue;
                mine.Add((p, yaw));
            }
            foreach (var (p, yaw) in mine)
            {
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var q = Cfg.Copy(p, m);
                    q.y = Height(q.x, q.z);
                    Cfg.Towers.Add(q);
                    BuildTower(root, q, yaw + m * 360f / Cfg.Copies);
                }
            }
        }

        /// <summary>A wooden watch tower: a platform 6 m up with railings and a roof, reached by a ramp.</summary>
        static void BuildTower(Transform root, Vector3 pos, float yaw)
        {
            var go = new GameObject("WatchTower");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var t = go.transform;
            float h = TowerH;
            const float r = 1.7f;
            float w = 2 * r + 0.5f;
            // posts (sunk into the ground) and cross braces
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Art.Box(t, Art.DarkWood, new Vector3(x * r, (h + 1.6f) * 0.5f, z * r), new Vector3(0.32f, h + 3.6f, 0.32f), default, true);
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, Art.Wood, new Vector3(r, h * 0.45f, 0), new Vector3(0.12f, 0.12f, 2f * r * 1.35f), new Vector3(k * 55f, 0, 0));
                Art.Box(t, Art.Wood, new Vector3(-r, h * 0.45f, 0), new Vector3(0.12f, 0.12f, 2f * r * 1.35f), new Vector3(k * 55f, 0, 0));
                Art.Box(t, Art.Wood, new Vector3(0, h * 0.45f, r), new Vector3(2f * r * 1.35f, 0.12f, 0.12f), new Vector3(0, 0, k * 55f));
            }
            // platform
            Art.Box(t, Art.Wood, new Vector3(0, h - 0.12f, 0), new Vector3(w, 0.25f, w), default, true);
            for (int k = -2; k <= 2; k++)
                Art.Box(t, Art.DarkWood, new Vector3(k * 0.75f, h + 0.005f, 0), new Vector3(0.05f, 0.01f, w - 0.05f));
            // railings on three sides, open on the ramp side (-z)
            Art.Box(t, Art.Wood, new Vector3(0, h + 0.55f, r + 0.1f), new Vector3(w, 1.1f, 0.08f), default, true);
            Art.Box(t, Art.Wood, new Vector3(r + 0.1f, h + 0.55f, 0), new Vector3(0.08f, 1.1f, w), default, true);
            Art.Box(t, Art.Wood, new Vector3(-(r + 0.1f), h + 0.55f, 0), new Vector3(0.08f, 1.1f, w), default, true);
            Art.Box(t, Art.DarkWood, new Vector3(0, h + 1.1f, r + 0.1f), new Vector3(w + 0.1f, 0.12f, 0.14f));
            Art.Box(t, Art.DarkWood, new Vector3(r + 0.1f, h + 1.1f, 0), new Vector3(0.14f, 0.12f, w + 0.1f));
            Art.Box(t, Art.DarkWood, new Vector3(-(r + 0.1f), h + 1.1f, 0), new Vector3(0.14f, 0.12f, w + 0.1f));
            // pyramid roof
            Art.Part(t, Art.Cone, new Color(0.45f, 0.28f, 0.14f), new Vector3(0, h + 2.55f, 0), new Vector3(w + 1.2f, 1.6f, w + 1.2f), new Vector3(0, 45f, 0));
            Art.Box(t, Art.DarkWood, new Vector3(0, h + 2.5f, 0), new Vector3(w + 0.1f, 0.12f, w + 0.1f));
            // ramp up to the open side
            float run = RampRun, len = h / Mathf.Sin(TowerRampAngle * Mathf.Deg2Rad);
            var rampC = new Vector3(0, h * 0.5f - 0.1f, -(r + 0.25f) - run * 0.5f);
            Art.Box(t, Art.Wood, rampC, new Vector3(1.5f, 0.2f, len + 0.3f), new Vector3(-TowerRampAngle, 0, 0), true);
            var dir = new Vector3(0, Mathf.Sin(TowerRampAngle * Mathf.Deg2Rad), Mathf.Cos(TowerRampAngle * Mathf.Deg2Rad));
            int steps = Mathf.RoundToInt(len / 0.45f);
            for (int k = 0; k < steps; k++)
            {
                var p = rampC + dir * ((k + 0.5f) / steps - 0.5f) * len + Vector3.up * 0.11f;
                Art.Box(t, Art.DarkWood, p, new Vector3(1.45f, 0.05f, 0.1f));
            }
            for (int k = -1; k <= 1; k += 2)
                Art.Box(t, Art.DarkWood, rampC + new Vector3(k * 0.78f, 0.45f, 0), new Vector3(0.08f, 0.08f, len), new Vector3(-TowerRampAngle, 0, 0));
        }

        // =====================================================================

        /// <summary>
        /// Sudden death: a massive round stadium. Sand floor, a padded wall, tiered stands packed with cheering spectators,
        /// floodlight towers and jumbotrons showing the countdown and the clock.
        /// </summary>
        static void BuildArena(Transform root)
        {
            var c = Cfg.ArenaCenter;
            float r = Cfg.ArenaHalf;
            var go = new GameObject("Stadium");
            go.transform.SetParent(root, false);
            go.transform.position = c;
            var t = go.transform;
            var sand = new Color(0.8f, 0.7f, 0.52f);
            var floor = Art.Part(t, Art.Cylinder, sand, new Vector3(0, -0.5f, 0), new Vector3(2 * r + 70f, 0.5f, 2 * r + 70f), default, false, null, "ArenaFloor");
            var fc = floor.AddComponent<BoxCollider>();
            fc.size = new Vector3(1f, 2f, 1f);
            floor.AddComponent<GroundMarker>();
            // centre logo and team spots
            Art.Part(t, Art.Cylinder, new Color(0.7f, 0.25f, 0.2f), new Vector3(0, 0.02f, 0), new Vector3(12f, 0.02f, 12f));
            Art.Part(t, Art.Cylinder, sand * 1.08f, new Vector3(0, 0.03f, 0), new Vector3(9f, 0.02f, 9f));
            Art.Part(t, Art.Ico, new Color(1f, 0.85f, 0.15f), new Vector3(0, 0.05f, 0), new Vector3(1.8f, 0.05f, 1.8f));
            for (int team = 0; team < Cfg.TeamCount; team++)
            {
                var d = Quaternion.Euler(0, team * 360f / Mathf.Max(2, Cfg.TeamCount), 0) * Vector3.back;
                Art.Part(t, Art.Cylinder, Cfg.TeamColor[team], d * 14f + Vector3.up * 0.02f, new Vector3(4f, 0.02f, 4f));
            }
            // padded wall around the pit (with an invisible barrier going up high)
            const int segs = 40;
            float wallR = r + 0.8f;
            for (int i = 0; i < segs; i++)
            {
                float a = i * 360f / segs;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                float w = 2f * Mathf.PI * wallR / segs + 0.15f;
                Art.Box(t, i % 2 == 0 ? new Color(0.2f, 0.25f, 0.55f) : new Color(0.75f, 0.2f, 0.2f), dir * wallR + Vector3.up * 1.6f, new Vector3(w, 3.2f, 0.8f), new Vector3(0, a, 0), true);
                var inv = new GameObject("barrier");
                inv.transform.SetParent(t, false);
                inv.transform.localPosition = dir * (wallR + 0.2f) + Vector3.up * 12f;
                inv.transform.localRotation = Quaternion.Euler(0, a, 0);
                inv.AddComponent<BoxCollider>().size = new Vector3(w, 20f, 0.6f);
            }
            // tiered stands with the crowd
            var stadium = go.AddComponent<Stadium>();
            var rng = new System.Random(4242);
            float R() => (float)rng.NextDouble();
            var concrete = new Color(0.55f, 0.55f, 0.58f);
            const int tiers = 7, seats = 44;
            for (int tier = 0; tier < tiers; tier++)
            {
                float tr = wallR + 2f + tier * 2.2f, ty = 3.2f + tier * 1.3f;
                for (int i = 0; i < seats; i++)
                {
                    float a = (i + (tier % 2) * 0.5f) * 360f / seats;
                    var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    float w = 2f * Mathf.PI * tr / seats + 0.2f;
                    var step = Art.Box(t, concrete * (0.9f + 0.1f * (tier % 2)), dir * tr + Vector3.up * (ty - 0.65f), new Vector3(w, 1.3f + tier * 0.02f, 2.3f), new Vector3(0, a, 0));
                    step.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    if (R() < 0.12f) continue; // a few empty seats
                    // a spectator: body, head, arms up when cheering
                    var fan = new GameObject("fan").transform;
                    fan.SetParent(t, false);
                    fan.localPosition = dir * (tr - 0.2f) + Vector3.up * ty;
                    fan.localRotation = Quaternion.LookRotation(-dir);
                    var shirt = R() < 0.5f ? Cfg.TeamColor[rng.Next(4)] : Color.HSVToRGB(R(), 0.5f, 0.85f);
                    var skin = Color.Lerp(new Color(0.55f, 0.6f, 0.52f), new Color(0.75f, 0.78f, 0.7f), R()); // they're aliens too
                    Art.Box(fan, shirt, new Vector3(0, 0.45f, 0), new Vector3(0.45f, 0.6f, 0.3f));
                    Art.Part(fan, Art.Sphere, skin, new Vector3(0, 0.95f, 0), new Vector3(0.36f, 0.42f, 0.36f));
                    Art.Box(fan, Color.black, new Vector3(0.08f, 0.98f, 0.16f), new Vector3(0.09f, 0.06f, 0.02f));
                    Art.Box(fan, Color.black, new Vector3(-0.08f, 0.98f, 0.16f), new Vector3(0.09f, 0.06f, 0.02f));
                    var arms = new GameObject("arms").transform;
                    arms.SetParent(fan, false);
                    arms.localPosition = new Vector3(0, 0.7f, 0);
                    Art.Box(arms, skin, new Vector3(0.28f, 0.25f, 0), new Vector3(0.1f, 0.5f, 0.1f), new Vector3(0, 0, -15));
                    Art.Box(arms, skin, new Vector3(-0.28f, 0.25f, 0), new Vector3(0.1f, 0.5f, 0.1f), new Vector3(0, 0, 15));
                    if (R() < 0.15f) Art.Box(arms, Cfg.TeamColor[rng.Next(4)], new Vector3(0.35f, 0.65f, 0), new Vector3(0.5f, 0.3f, 0.02f)); // a flag
                    foreach (var mr in fan.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;
                    stadium.AddFan(fan, arms, R() * 10f);
                }
            }
            // floodlights
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f);
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var pole = dir * (wallR + 2f + tiers * 2.2f + 2f);
                Art.Box(t, Art.Metal, pole + Vector3.up * 14f, new Vector3(0.6f, 28f, 0.6f));
                Art.Box(t, new Color(1f, 1f, 0.85f), pole + Vector3.up * 28f - dir * 0.4f, new Vector3(4f, 2.5f, 0.4f), new Vector3(0, a, 0));
                var lg = new GameObject("flood");
                lg.transform.SetParent(t, false);
                lg.transform.localPosition = pole + Vector3.up * 27f;
                lg.transform.LookAt(c);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 70f;
                l.range = 80f;
                l.intensity = 6f;
                l.color = new Color(1f, 0.97f, 0.9f);
            }
            // jumbotrons: big countdown / clock
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var pos = dir * (wallR + 2f + tiers * 2.2f + 1f) + Vector3.up * (3.2f + tiers * 1.3f + 5f);
                var screen = new GameObject("jumbotron").transform;
                screen.SetParent(t, false);
                screen.localPosition = pos;
                screen.localRotation = Quaternion.LookRotation(dir); // faces outward, so its back (-z) faces the pit
                Art.Box(screen, new Color(0.08f, 0.08f, 0.1f), Vector3.zero, new Vector3(10f, 5.5f, 0.5f));
                Art.Box(screen, Art.Metal, new Vector3(0, -5f, 0.3f), new Vector3(0.6f, 5f, 0.6f));
                var txt = new GameObject("text");
                txt.transform.SetParent(screen, false);
                txt.transform.localPosition = new Vector3(0, 0, -0.3f);
                txt.transform.localRotation = Quaternion.identity; // readable from the pit side
                var tm = txt.AddComponent<TextMesh>();
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.35f;
                tm.fontSize = 64;
                tm.color = new Color(1f, 0.85f, 0.2f);
                tm.text = "ROCK BRAWL";
                stadium.AddScreen(tm);
            }
        }
    }

    /// <summary>Marks static ground colliders so placement/overlap tests can ignore them.</summary>
    public class GroundMarker : MonoBehaviour { }

    /// <summary>Sudden death stadium: the crowd cheers (and goes wild on the countdown and kills); jumbotrons show the clock.</summary>
    public class Stadium : MonoBehaviour
    {
        public static Stadium Instance;
        readonly List<(Transform body, Transform arms, float phase)> m_Fans = new List<(Transform, Transform, float)>();
        readonly List<TextMesh> m_Screens = new List<TextMesh>();
        readonly List<Vector3> m_Base = new List<Vector3>();
        AudioSource m_Crowd;
        float m_Hype;
        Renderer[] m_Renderers;
        bool m_Shown = true;

        /// <summary>The stadium is far off the edge of the map: only draw it when you're near it (it showed up on the horizon).</summary>
        void ShowStadium(bool show)
        {
            if (m_Renderers == null) m_Renderers = GetComponentsInChildren<Renderer>(true);
            if (show == m_Shown) return;
            m_Shown = show;
            foreach (var r in m_Renderers) if (r) r.enabled = show;
            foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = show;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void AddFan(Transform body, Transform arms, float phase) { m_Fans.Add((body, arms, phase)); m_Base.Add(body.localPosition); }
        public void AddScreen(TextMesh tm) => m_Screens.Add(tm);

        /// <summary>Someone scored a kill / the fight started: the crowd goes wild for a moment.</summary>
        public static void Roar() { if (Instance != null) Instance.m_Hype = 1f; }

        void Update()
        {
            var g = NetGame.Instance;
            bool live = g != null && g.IsSpawned && g.S == GameState.SuddenDeath;
            var me = PlayerNet.Local;
            float camDist = Camera.main != null ? Vector3.Distance(Camera.main.transform.position, transform.position) : float.MaxValue;
            ShowStadium(camDist < 300f);
            bool near = camDist < 120f;
            if (!near) { if (m_Crowd) m_Crowd.volume = 0f; return; }
            m_Hype = Mathf.MoveTowards(m_Hype, 0f, Time.deltaTime * 0.3f);
            float excite = 0.35f + (live ? 0.3f : 0f) + m_Hype * 0.7f;
            float t = Time.time;
            for (int i = 0; i < m_Fans.Count; i++)
            {
                var (body, arms, ph) = m_Fans[i];
                float jump = Mathf.Max(0f, Mathf.Sin(t * (4f + excite * 3f) + ph)) * 0.25f * excite;
                body.localPosition = m_Base[i] + Vector3.up * jump;
                arms.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 5f + ph) * 25f * excite);
            }
            if (m_Crowd == null)
            {
                m_Crowd = gameObject.AddComponent<AudioSource>();
                m_Crowd.clip = Sfx.Crowd;
                m_Crowd.loop = true;
                m_Crowd.spatialBlend = 0f;
                m_Crowd.Play();
            }
            m_Crowd.volume = (0.12f + excite * 0.25f) * GameSettings.SfxVolume;
            string text = "ROCK BRAWL";
            if (g != null && g.IsSpawned)
            {
                if (g.S == GameState.SuddenDeath)
                {
                    double left = g.FightAt.Value - g.NetworkManager.ServerTime.Time;
                    if (left > 0) text = Mathf.CeilToInt((float)left).ToString();
                    else if (left > -1.5) text = "FIGHT!";
                    else { int s = Mathf.CeilToInt(g.TimeLeft); text = $"SUDDEN DEATH\n{s / 60}:{s % 60:00}"; }
                }
                else if (g.S == GameState.Waiting) text = "WAITING FOR\nPLAYERS";
                else if (g.S == GameState.GameOver) text = g.Winner.Value >= 0 ? $"{Cfg.TeamLabel(g.Winner.Value)}\nWINS!" : "DRAW";
            }
            foreach (var s in m_Screens) if (s.text != text) s.text = text;
        }
    }

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
