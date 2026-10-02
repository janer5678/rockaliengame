using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Normal graphics: a thick, stylised meadow on Plains and Highlands - tall blades of grass that are dark at the
    /// root and bright at the tip, sway in the wind and part round players, horses and dropped things; patches of
    /// golden wheat, clearings of white daisies, purple lupins and clumps of broad leaves - over the flat-colour ground.
    ///
    /// Staying fast: one 8 m patch of blades is drawn instanced on every 8 m square near the camera (only the ones in
    /// view), with fewer, wider blades further out and none past the grass distance picked in Settings > Display (60 m to start with); the density setting
    /// draws only the first share of each patch's blades (they're in random order, so it thins evenly).
    /// Where each blade grows comes from the "grass field" texture (1 texel a metre): R the ground height, G how much
    /// grass (none on the bases, the ball drop zone, steep rock, or under anything built), B wheat, A short grass.
    /// The flowers are a few static meshes. Hidden in the PSX and AI PSX graphics (they have their own looks).
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        const float Tile = 8f;              // the patch of blades drawn on every square
        const float BladesPerM2 = 55f;
        const float Near = 11f;
        // how far the grass goes and how many blades are drawn come from Settings > Display (GameSettings.GrassDistance / GrassDensity)
        static float FadeEnd => GameSettings.GrassDistance;
        static float FadeStart => FadeEnd * 0.8f;
        static float DecorFadeEnd => FadeEnd + 2f;
        static float DecorFadeStart => FadeEnd - 12f;
        static float Density => GameSettings.GrassDensity;
        const float DecorChunk = 32f;
        const int MaxPush = 16;
        static readonly float[] k_LodFrac = { 1f, 0.5f, 0.25f, 0.125f, 0.0625f, 0.03f };

        static GrassField s_I;
        /// <summary>(tests) the field is up, how many patches were drawn last frame, the flower clearings.</summary>
        public static GrassField Current => s_I;
        public int DrawnPatches { get; private set; }
        public int DrawnTriangles { get; private set; }
        public readonly List<Vector2> ClearingSpots = new List<Vector2>();
        public bool ForceOff;
        public float CoverAt(float x, float z) => Field(x, z).g;
        public float WheatAt(float x, float z) => Field(x, z).b;
        static Shader s_GrassShader;

        Texture2D m_Field;
        Color[] m_Px;
        int m_N;
        float m_Min;                        // world x/z of texel 0's centre
        Mesh[] m_Lods;
        Material m_Mat, m_DecorMat;
        int m_T0, m_TN;                     // tiles: index i covers world [i*Tile, (i+1)*Tile)
        bool[] m_TileHas;
        Vector2[] m_TileY;                  // lowest / highest ground in each tile
        readonly List<Vector4>[] m_Batches = new List<Vector4>[k_LodFrac.Length];
        readonly Vector4[] m_Tiles = new Vector4[400];
        readonly int[] m_Starts = new int[k_LodFrac.Length];
        readonly MaterialPropertyBlock[] m_Props = new MaterialPropertyBlock[k_LodFrac.Length];
        readonly List<(Renderer r, Vector2 c)> m_Decor = new List<(Renderer, Vector2)>();
        readonly Plane[] m_Planes = new Plane[6];
        readonly Vector4[] m_Push = new Vector4[MaxPush];
        readonly List<(float d, Vector4 p)> m_PushList = new List<(float, Vector4)>();
        bool m_On;

        static readonly int k_Field = Shader.PropertyToID("_GrassField"), k_Rect = Shader.PropertyToID("_GrassFieldRect"),
            k_PushArr = Shader.PropertyToID("_GrassPush"), k_PushCount = Shader.PropertyToID("_GrassPushCount"),
            k_Tiles = Shader.PropertyToID("_GrassTiles"), k_TileStart = Shader.PropertyToID("_TileStart");

        public static void Build(Transform root)
        {
            if (ThemeMaps.IsTheme) return; // THEME MAPS (they have their own ground)
            if (s_GrassShader == null) s_GrassShader = Resources.Load<Shader>("Grass/Grass");
            if (s_GrassShader == null || !s_GrassShader.isSupported) return;
            var go = new GameObject("Grass");
            go.transform.SetParent(root, false);
            var g = go.AddComponent<GrassField>();
            s_I = g;
            g.Generate();
            g.Refresh();
            GameSettings.GraphicsChanged += g.Refresh;
        }

        void OnDestroy()
        {
            GameSettings.GraphicsChanged -= Refresh;
            if (s_I == this) s_I = null;
            if (m_Field) Destroy(m_Field);
            if (m_Lods != null) foreach (var m in m_Lods) if (m) Destroy(m);
            foreach (var (r, _) in m_Decor) if (r) { var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) Destroy(mf.sharedMesh); }
            if (m_Mat) Destroy(m_Mat);
            if (m_DecorMat) Destroy(m_DecorMat);
        }

        public void Refresh()
        {
            if (this == null) return;
            m_On = !GameSettings.PsxGraphics && !GameSettings.AiPsx && !ForceOff;
            foreach (var (r, _) in m_Decor) if (r) r.gameObject.SetActive(m_On);
        }

        // =====================================================================
        // The grass field: where grass grows
        // =====================================================================

        void Generate()
        {
            float half = Cfg.MapHalf;
            m_Min = -half - 2f;
            m_N = Mathf.CeilToInt(half * 2f + 4f) + 1;
            m_Px = new Color[m_N * m_N];
            var rng = new System.Random(Cfg.MapSeed * 13 + 5);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float wx = R(0, 500), wz = R(0, 500);

            for (int j = 0; j < m_N; j++)
            for (int i = 0; i < m_N; i++)
            {
                float x = m_Min + i, z = m_Min + j;
                float y = MapBuilder.Height(x, z);
                float cover = 1f;
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > half - 1.5f) cover = 0f;           // off the map
                else if (Cfg.BaseTeamAt(new Vector3(x, 0, z)) >= 0) cover = 0f;                  // the bases (you build there)
                else
                {
                    cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 14.5f, new Vector2(x, z).magnitude)); // the ball drop zone
                    if (Cfg.Map == MapKind.Highlands)
                    {
                        // grass only where it's gentle (the steep bits are rock)
                        float dx = MapBuilder.Height(x + 1f, z) - y, dz = MapBuilder.Height(x, z + 1f) - y;
                        if (new Vector3(-dx, 1f, -dz).normalized.y < 0.82f) cover = 0f;
                    }
                }
                float wheat = Mathf.InverseLerp(0.66f, 0.74f, Mathf.PerlinNoise(wx + x * 0.032f, wz + z * 0.032f));
                m_Px[j * m_N + i] = new Color(y, cover, wheat, 0f);
            }

            // flower clearings: short grass with daisies
            var clearings = new List<(Vector2 c, float r)>();
            int nClear = Mathf.RoundToInt(half * half * 4f / 1300f);
            for (int k = 0, tries = 0; k < nClear && tries < nClear * 10; tries++)
            {
                var c = new Vector2(R(-half, half), R(-half, half));
                float r = R(1.4f, 2.6f);
                if (Field(c.x, c.y).g < 0.99f || Field(c.x, c.y).b > 0.1f) continue;
                clearings.Add((c, r));
                ClearingSpots.Add(c);
                k++;
                for (int j = Idx(c.y - r - 1); j <= Idx(c.y + r + 1); j++)
                for (int i = Idx(c.x - r - 1); i <= Idx(c.x + r + 1); i++)
                {
                    float d = Vector2.Distance(new Vector2(m_Min + i, m_Min + j), c);
                    ref var px = ref m_Px[j * m_N + i];
                    px.a = Mathf.Max(px.a, Mathf.InverseLerp(r + 0.8f, r - 0.4f, d));
                }
            }

            m_Field = new Texture2D(m_N, m_N, TextureFormat.RGBAFloat, false, true) { name = "grass field", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            m_Field.SetPixels(m_Px);
            m_Field.Apply(false);
            // (texel i's centre is at m_Min + i: uv = (world - (m_Min - 0.5)) / m_N)
            Shader.SetGlobalTexture(k_Field, m_Field);
            Shader.SetGlobalVector(k_Rect, new Vector4(m_Min - 0.5f, m_Min - 0.5f, 1f / m_N, 1f / m_N));
            SetSettings();
            Shader.SetGlobalFloat(k_PushCount, 0);
            Shader.SetGlobalVectorArray(k_PushArr, m_Push);

            // which squares have any grass, and how high their ground goes
            m_T0 = Mathf.FloorToInt(m_Min / Tile);
            m_TN = Mathf.CeilToInt((m_Min + m_N) / Tile) - m_T0;
            m_TileHas = new bool[m_TN * m_TN];
            m_TileY = new Vector2[m_TN * m_TN];
            for (int tj = 0; tj < m_TN; tj++)
            for (int ti = 0; ti < m_TN; ti++)
            {
                float x0 = (m_T0 + ti) * Tile, z0 = (m_T0 + tj) * Tile;
                bool has = false;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int j = Idx(z0 - 1); j <= Idx(z0 + Tile + 1); j++)
                for (int i = Idx(x0 - 1); i <= Idx(x0 + Tile + 1); i++)
                {
                    var p = m_Px[j * m_N + i];
                    if (p.g > 0.02f) has = true;
                    lo = Mathf.Min(lo, p.r);
                    hi = Mathf.Max(hi, p.r);
                }
                m_TileHas[tj * m_TN + ti] = has;
                m_TileY[tj * m_TN + ti] = new Vector2(lo, hi);
            }

            m_Mat = new Material(s_GrassShader) { name = "grass blades" };
            m_DecorMat = new Material(s_GrassShader) { name = "grass flowers" };
            for (int i = 0; i < m_Batches.Length; i++) { m_Batches[i] = new List<Vector4>(); m_Props[i] = new MaterialPropertyBlock(); }
            Shader.SetGlobalVectorArray(k_Tiles, m_Tiles);

            BuildPatch(new System.Random(77));
            BuildDecor(rng, clearings);
        }

        static void SetSettings()
        {
            Shader.SetGlobalFloat("_GrassNear", Near);
            Shader.SetGlobalVector("_GrassFade", new Vector4(FadeStart, FadeEnd, DecorFadeStart, DecorFadeEnd));
            Shader.SetGlobalVector("_GrassWheat", new Color(0.74f, 0.6f, 0.28f).linear);
            Shader.SetGlobalFloat("_GrassWind", 1f);
            Shader.SetGlobalFloat("_GrassDensity", Density);
            var g = GameSettings.WorldTint(GameSettings.WorldColor.Grass);
            Shader.SetGlobalVector("_GrassTint", new Vector4(Mathf.GammaToLinearSpace(g.r), Mathf.GammaToLinearSpace(g.g), Mathf.GammaToLinearSpace(g.b), 1f));
        }

        int Idx(float w) => Mathf.Clamp(Mathf.RoundToInt(w - m_Min), 0, m_N - 1);
        Color Field(float x, float z) => m_Px[Idx(z) * m_N + Idx(x)];

        /// <summary>Nothing grows under something built (called when a building piece appears).</summary>
        public static void ClearUnder(Bounds b)
        {
            var g = s_I;
            if (g == null || g.m_Field == null) return;
            int i0 = g.Idx(b.min.x - 0.3f), i1 = g.Idx(b.max.x + 0.3f), j0 = g.Idx(b.min.z - 0.3f), j1 = g.Idx(b.max.z + 0.3f);
            bool any = false;
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                ref var px = ref g.m_Px[j * g.m_N + i];
                if (px.g <= 0f || b.min.y > px.r + 1.2f) continue; // (not if it's up in the air)
                px.g = 0f;
                g.m_Field.SetPixel(i, j, px);
                any = true;
            }
            if (any) g.m_Field.Apply(false);
        }

        // =====================================================================
        // The blades: one 8 m patch, with fewer-blade copies for far away
        // =====================================================================

        struct MeshData
        {
            public bool Decor;             // (flowers: rank -1 tells the shader)
            public List<Vector3> V; public List<Color> C; public List<Vector4> U0; public List<Vector2> U1; public List<int> T;
            public static MeshData New() => new MeshData { V = new List<Vector3>(), C = new List<Color>(), U0 = new List<Vector4>(), U1 = new List<Vector2>(), T = new List<int>() };
            public int Count => V.Count;
            public void Add(Vector3 off, Color tip, float bend, Vector2 root, float rank, float rnd, Vector2 lean)
            {
                tip = tip.linear; // (vertex colours aren't converted like material ones)
                tip.a = bend;
                V.Add(off); C.Add(tip); U0.Add(new Vector4(root.x, root.y, Decor ? -1f : rank, rnd)); U1.Add(lean);
            }
            public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
            public Mesh ToMesh(string name, Bounds bounds)
            {
                var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(V); m.SetColors(C); m.SetUVs(0, U0); m.SetUVs(1, U1); m.SetTriangles(T, 0);
                m.bounds = bounds;
                return m;
            }
        }

        static readonly Color[] k_Tips =
        {
            new Color(0.34f, 0.6f, 0.15f), new Color(0.38f, 0.64f, 0.17f), new Color(0.3f, 0.56f, 0.13f),
            new Color(0.42f, 0.66f, 0.19f), new Color(0.33f, 0.58f, 0.16f),
        };

        void BuildPatch(System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int n = Mathf.RoundToInt(BladesPerM2 * Tile * Tile);
            var md = MeshData.New();
            var lodVerts = new int[k_LodFrac.Length];
            // blades in random order, so the first k of them are an even spread (that's what far squares draw)
            int lod = k_LodFrac.Length - 1;
            for (int b = 0; b < n; b++)
            {
                float rank = b / (float)n;
                var root = new Vector2(R(0, Tile), R(0, Tile));
                float h = R(0.26f, 0.5f), w = R(0.065f, 0.11f), yaw = R(0, Mathf.PI);
                var across = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
                float la = R(0, Mathf.PI * 2f), lm = R(0.03f, 0.16f) * h / 0.4f;
                var lean = new Vector2(Mathf.Cos(la), Mathf.Sin(la)) * lm;
                var tip = rng.NextDouble() < 0.07 ? new Color(0.5f, 0.62f, 0.2f) : k_Tips[rng.Next(k_Tips.Length)];
                tip *= R(0.92f, 1.08f);
                float rnd = R(0, 1);
                int v = md.Count;
                // a pointed blade: two at the root, two half way up, the tip
                md.Add(-across * w * 0.5f, tip, 0f, root, rank, rnd, lean);
                md.Add(across * w * 0.5f, tip, 0f, root, rank, rnd, lean);
                md.Add(-across * w * 0.42f + Vector3.up * h * 0.5f, tip, 0.5f, root, rank, rnd, lean);
                md.Add(across * w * 0.42f + Vector3.up * h * 0.5f, tip, 0.5f, root, rank, rnd, lean);
                md.Add(Vector3.up * h, tip, 1f, root, rank, rnd, lean);
                md.Tri(v, v + 2, v + 1); md.Tri(v + 1, v + 2, v + 3); md.Tri(v + 2, v + 4, v + 3);
                while (lod >= 0 && b + 1 >= Mathf.CeilToInt(k_LodFrac[lod] * n)) { lodVerts[lod] = md.T.Count; lod--; }
            }
            var bounds = new Bounds(new Vector3(Tile * 0.5f, 0.5f, Tile * 0.5f), new Vector3(Tile + 2f, 3f, Tile + 2f));
            m_Lods = new Mesh[k_LodFrac.Length];
            var full = md.ToMesh("grass patch", bounds);
            m_Lods[0] = full;
            for (int i = 1; i < k_LodFrac.Length; i++)
            {
                var m = Instantiate(full);
                m.name = "grass patch lod" + i;
                if (i < 2) m.SetTriangles(md.T.GetRange(0, lodVerts[i]), 0);
                else
                {
                    // far away: each blade is a single triangle (root corners and the tip; the middle two aren't used)
                    int blades = lodVerts[i] / 9;
                    var tris = new List<int>(blades * 3);
                    for (int k = 0; k < blades; k++) { int v = k * 5; tris.Add(v); tris.Add(v + 4); tris.Add(v + 1); }
                    m.SetTriangles(tris, 0);
                }
                m.bounds = bounds;
                m_Lods[i] = m;
            }
        }

        // =====================================================================
        // Flowers: daisies in the clearings, lupins, clumps of broad leaves
        // =====================================================================

        void BuildDecor(System.Random rng, List<(Vector2 c, float r)> clearings)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = Cfg.MapHalf - 2f;
            var chunks = new Dictionary<(int, int), MeshData>();
            MeshData Chunk(Vector2 p)
            {
                var key = (Mathf.FloorToInt(p.x / DecorChunk), Mathf.FloorToInt(p.y / DecorChunk));
                if (!chunks.TryGetValue(key, out var md)) { md = MeshData.New(); md.Decor = true; chunks[key] = md; }
                return md;
            }
            bool Grows(Vector2 p) { var f = Field(p.x, p.y); return f.g > 0.95f && f.b < 0.2f; }

            // daisies
            var white = new Color(0.97f, 0.97f, 0.94f);
            var yellow = new Color(1f, 0.78f, 0.15f);
            foreach (var (c, r) in clearings)
            {
                int count = Mathf.RoundToInt(r * r * 22f);
                for (int k = 0; k < count; k++)
                {
                    float a = R(0, Mathf.PI * 2f), d = Mathf.Sqrt(R(0, 1)) * r;
                    var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    if (!Grows(p)) continue;
                    var md = Chunk(p);
                    float s = R(0.12f, 0.18f), size = R(1.6f, 2.1f), spin = R(0, Mathf.PI);
                    var lean = new Vector2(R(-0.02f, 0.02f), R(-0.02f, 0.02f));
                    var top = new Vector3(0, s, 0);
                    int petals = 7;
                    for (int q = 0; q < petals; q++)
                    {
                        float pa = spin + q * Mathf.PI * 2f / petals;
                        var dir = new Vector3(Mathf.Cos(pa), 0.18f, Mathf.Sin(pa));
                        var perp = new Vector3(-Mathf.Sin(pa), 0, Mathf.Cos(pa));
                        int v = md.Count;
                        md.Add(top, white, 1f, p, 0, 0, lean);
                        md.Add(top + (dir * 0.022f + perp * 0.013f) * size, white, 1f, p, 0, 0, lean);
                        md.Add(top + dir * 0.048f * size, white, 1f, p, 0, 0, lean);
                        md.Add(top + (dir * 0.022f - perp * 0.013f) * size, white, 1f, p, 0, 0, lean);
                        md.Tri(v, v + 1, v + 2); md.Tri(v, v + 2, v + 3);
                    }
                    int cv = md.Count;
                    md.Add(top + Vector3.up * 0.006f, yellow, 1f, p, 0, 0, lean);
                    for (int q = 0; q < 6; q++)
                    {
                        float pa = q * Mathf.PI / 3f;
                        md.Add(top + new Vector3(Mathf.Cos(pa) * 0.014f, 0.004f, Mathf.Sin(pa) * 0.014f) * size, yellow, 1f, p, 0, 0, lean);
                    }
                    for (int q = 0; q < 6; q++) md.Tri(cv, cv + 1 + (q + 1) % 6, cv + 1 + q);
                }
            }

            // lupins, in little groups
            float area = half * half * 4f;
            int groups = Mathf.RoundToInt(area / 140f);
            for (int g = 0; g < groups; g++)
            {
                var gc = new Vector2(R(-half, half), R(-half, half));
                var hue = rng.NextDouble() < 0.75 ? new Color(0.6f, 0.42f, 0.86f) : rng.NextDouble() < 0.5 ? new Color(0.86f, 0.5f, 0.78f) : new Color(0.95f, 0.95f, 0.98f);
                int inGroup = rng.Next(1, 5);
                for (int k = 0; k < inGroup; k++)
                {
                    var p = gc + new Vector2(R(-1.2f, 1.2f), R(-1.2f, 1.2f));
                    if (!Grows(p) || Field(p.x, p.y).a > 0.1f) continue;
                    var md = Chunk(p);
                    float H = R(0.5f, 0.8f), spin = R(0, Mathf.PI);
                    var lean = new Vector2(R(-0.05f, 0.05f), R(-0.05f, 0.05f));
                    var stem = new Color(0.3f, 0.55f, 0.18f);
                    // stem
                    {
                        var a = new Vector3(Mathf.Cos(spin), 0, Mathf.Sin(spin)) * 0.012f;
                        int v = md.Count;
                        md.Add(-a, stem, 0f, p, 0, 0, lean); md.Add(a, stem, 0f, p, 0, 0, lean);
                        md.Add(-a + Vector3.up * H * 0.4f, stem, 0.4f, p, 0, 0, lean); md.Add(a + Vector3.up * H * 0.4f, stem, 0.4f, p, 0, 0, lean);
                        md.Tri(v, v + 2, v + 1); md.Tri(v + 1, v + 2, v + 3);
                    }
                    // the flower spike: two crossed knobbly cards, lighter towards the top
                    float[] widths = { 0.05f, 0.075f, 0.05f, 0.07f, 0.045f, 0.06f, 0.035f, 0.04f, 0f };
                    for (int card = 0; card < 2; card++)
                    {
                        float ca = spin + card * Mathf.PI * 0.5f;
                        var a = new Vector3(Mathf.Cos(ca), 0, Mathf.Sin(ca));
                        int v0 = md.Count;
                        for (int s = 0; s < widths.Length; s++)
                        {
                            float t = s / (float)(widths.Length - 1);
                            float y = Mathf.Lerp(0.38f, 1f, t) * H;
                            var col = Color.Lerp(hue * 0.8f, Color.Lerp(hue, Color.white, 0.3f), t);
                            md.Add(-a * widths[s] + Vector3.up * y, col, Mathf.Lerp(0.38f, 1f, t), p, 0, 0, lean);
                            md.Add(a * widths[s] + Vector3.up * y, col, Mathf.Lerp(0.38f, 1f, t), p, 0, 0, lean);
                        }
                        for (int s = 0; s < widths.Length - 1; s++)
                        {
                            int v = v0 + s * 2;
                            md.Tri(v, v + 2, v + 1); md.Tri(v + 1, v + 2, v + 3);
                        }
                    }
                    // a few leaves at the bottom
                    for (int l = 0; l < 4; l++)
                        Leaf(md, p, spin + l * Mathf.PI * 0.5f + R(-0.3f, 0.3f), R(0.18f, 0.26f), 0.05f, stem, R(0.25f, 0.45f));
                }
            }

            // clumps of broad leaves
            int clumps = Mathf.RoundToInt(area / 260f);
            for (int g = 0; g < clumps; g++)
            {
                var p = new Vector2(R(-half, half), R(-half, half));
                if (!Grows(p) || Field(p.x, p.y).a > 0.1f) continue;
                var md = Chunk(p);
                int leaves = rng.Next(5, 9);
                float spin = R(0, Mathf.PI * 2f), size = R(0.8f, 1.25f);
                var col = Color.Lerp(new Color(0.24f, 0.5f, 0.16f), new Color(0.34f, 0.62f, 0.2f), R(0, 1));
                for (int l = 0; l < leaves; l++)
                    Leaf(md, p, spin + l * Mathf.PI * 2f / leaves + R(-0.25f, 0.25f), R(0.45f, 0.7f) * size, R(0.1f, 0.15f) * size, col, R(0.45f, 0.8f));
            }

            foreach (var kv in chunks)
            {
                var md = kv.Value;
                if (md.Count == 0) continue;
                var c = new Vector2((kv.Key.Item1 + 0.5f) * DecorChunk, (kv.Key.Item2 + 0.5f) * DecorChunk);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var u in md.U0) { float y = Field(u.x, u.y).r; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                var bounds = new Bounds(new Vector3(c.x, (lo + hi) * 0.5f + 0.5f, c.y), new Vector3(DecorChunk + 3f, hi - lo + 3f, DecorChunk + 3f));
                var go = new GameObject("flowers " + kv.Key);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = md.ToMesh("flowers", bounds);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m_DecorMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                m_Decor.Add((mr, c));
            }
        }

        /// <summary>A broad leaf arching out from the root: up, out and back down at the tip.</summary>
        static void Leaf(MeshData md, Vector2 root, float angle, float length, float width, Color col, float rise)
        {
            var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var perp = new Vector3(-dir.z, 0, dir.x);
            float[] ts = { 0f, 0.3f, 0.6f, 0.85f, 1f };
            float[] ws = { 0.15f, 0.9f, 1f, 0.6f, 0f };
            int v0 = md.Count;
            for (int s = 0; s < ts.Length; s++)
            {
                float t = ts[s];
                var c = dir * length * t + Vector3.up * length * rise * Mathf.Sin(t * Mathf.PI * 0.85f);
                var tint = Color.Lerp(col * 0.85f, Color.Lerp(col, new Color(0.7f, 0.9f, 0.4f), 0.25f), t);
                // a slight fold down the middle so it catches the light
                md.Add(c - perp * width * ws[s] * 0.5f - Vector3.up * width * ws[s] * 0.12f, tint, t * 0.7f, root, 0, 0, Vector2.zero);
                md.Add(c + Vector3.up * 0.005f, tint * 1.08f, t * 0.7f, root, 0, 0, Vector2.zero);
                md.Add(c + perp * width * ws[s] * 0.5f - Vector3.up * width * ws[s] * 0.12f, tint, t * 0.7f, root, 0, 0, Vector2.zero);
            }
            for (int s = 0; s < ts.Length - 1; s++)
            {
                int a = v0 + s * 3, b = a + 3;
                md.Tri(a, b, a + 1); md.Tri(a + 1, b, b + 1);
                md.Tri(a + 1, b + 1, a + 2); md.Tri(a + 2, b + 1, b + 2);
            }
        }

        // =====================================================================
        // Each frame: draw the patches near the camera, and what's pushing the grass aside
        // =====================================================================

        void Update()
        {
            DrawnPatches = 0;
            DrawnTriangles = 0;
            if (!m_On || m_Lods == null || s_I != this) return;
            // (the field is shared by every grass material; re-bind it in case another map's field was up)
            Shader.SetGlobalTexture(k_Field, m_Field);
            Shader.SetGlobalVector(k_Rect, new Vector4(m_Min - 0.5f, m_Min - 0.5f, 1f / m_N, 1f / m_N));
            SetSettings();
            var cam = Camera.main;
            if (cam == null) return;
            var cp = cam.transform.position;

            // flowers: only the chunks close enough to see
            foreach (var (r, c) in m_Decor)
                if (r) r.enabled = Vector2.Distance(c, new Vector2(cp.x, cp.z)) < DecorFadeEnd + DecorChunk * 0.75f;

            UpdatePush(cp);

            GeometryUtility.CalculateFrustumPlanes(cam, m_Planes);
            foreach (var b in m_Batches) b.Clear();
            int r0i = Mathf.FloorToInt((cp.x - FadeEnd) / Tile) - m_T0, r1i = Mathf.FloorToInt((cp.x + FadeEnd) / Tile) - m_T0;
            int r0j = Mathf.FloorToInt((cp.z - FadeEnd) / Tile) - m_T0, r1j = Mathf.FloorToInt((cp.z + FadeEnd) / Tile) - m_T0;
            for (int tj = Mathf.Max(0, r0j); tj <= Mathf.Min(m_TN - 1, r1j); tj++)
            for (int ti = Mathf.Max(0, r0i); ti <= Mathf.Min(m_TN - 1, r1i); ti++)
            {
                int t = tj * m_TN + ti;
                if (!m_TileHas[t]) continue;
                float x0 = (m_T0 + ti) * Tile, z0 = (m_T0 + tj) * Tile;
                float nx = Mathf.Clamp(cp.x, x0, x0 + Tile) - cp.x, nz = Mathf.Clamp(cp.z, z0, z0 + Tile) - cp.z;
                float near = Mathf.Sqrt(nx * nx + nz * nz);
                if (near > FadeEnd) continue;
                var ty = m_TileY[t];
                var b = new Bounds(new Vector3(x0 + Tile * 0.5f, (ty.x + ty.y) * 0.5f + 0.4f, z0 + Tile * 0.5f), new Vector3(Tile + 1f, ty.y - ty.x + 1.6f, Tile + 1f));
                if (!GeometryUtility.TestPlanesAABB(m_Planes, b)) continue;
                float dens = (near < Near ? 1f : Mathf.Pow(Near / near, 1.35f)) * Density;
                int lod = 0;
                while (lod + 1 < k_LodFrac.Length && k_LodFrac[lod + 1] >= dens) lod++;
                m_Batches[lod].Add(new Vector4(x0, 0, z0, 0));
                DrawnPatches++;
            }
            // every patch's corner goes in one array; each level of detail is one draw of its own run of it
            // (procedural instancing: the shader picks its patch by instance number - no instancing variants needed)
            int at = 0;
            var starts = m_Starts;
            for (int i = 0; i < m_Batches.Length; i++)
            {
                starts[i] = at;
                foreach (var t in m_Batches[i]) if (at < m_Tiles.Length) m_Tiles[at++] = t;
            }
            Shader.SetGlobalVectorArray(k_Tiles, m_Tiles);
            for (int i = 0; i < m_Batches.Length; i++)
            {
                int n = Mathf.Min(m_Batches[i].Count, m_Tiles.Length - starts[i]);
                if (n <= 0) continue;
                m_Props[i].SetFloat(k_TileStart, starts[i]);
                var rp = new RenderParams(m_Mat)
                {
                    camera = cam,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = new Bounds(cp, Vector3.one * (FadeEnd * 2f + 20f)),
                    layer = gameObject.layer,
                    matProps = m_Props[i],
                };
                Graphics.RenderMeshPrimitives(rp, m_Lods[i], 0, n);
                DrawnTriangles += (int)m_Lods[i].GetIndexCount(0) / 3 * n;
            }
        }

        void UpdatePush(Vector3 cp)
        {
            m_PushList.Clear();
            void Add(Vector3 p, float r)
            {
                float d = (p - cp).sqrMagnitude;
                if (d < 40f * 40f) m_PushList.Add((d, new Vector4(p.x, p.y, p.z, r)));
            }
            foreach (var p in PlayerNet.All) if (p != null) Add(p.transform.position, 0.8f);
            foreach (var v in Vehicle.All) if (v != null) Add(v.transform.position, v.IsHorse ? 1.3f : 2f);
            foreach (var c in Container.All) if (c != null) Add(c.transform.position, 0.9f);
            var ng = NetGame.Instance;
            if (ng != null && ng.IsSpawned)
                for (int i = 0; i < ng.Items.Count; i++) Add(ng.Items[i].Pos, 0.55f);
            if (Ball.Instance != null) Add(Ball.Instance.transform.position, 1f);
            m_PushList.Sort((a, b) => a.d.CompareTo(b.d));
            int n = Mathf.Min(MaxPush, m_PushList.Count);
            for (int i = 0; i < n; i++) m_Push[i] = m_PushList[i].p;
            Shader.SetGlobalVectorArray(k_PushArr, m_Push);
            Shader.SetGlobalFloat(k_PushCount, n);
        }
    }
}
