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
    /// view); further out the same patch is stretched over 16 m and then 32 m squares (where only a few of its blades
    /// are drawn anyway), with fewer, wider blades the further out (how fast it thins is the "far grass thickness"
    /// setting) and none past the grass distance picked in Settings > Display (60 m to start with, up to 270 m); the
    /// density setting draws only the first share of each patch's blades (they're in random order, so it thins evenly).
    /// Where each blade grows comes from the "grass field" texture (1 texel a metre): R the ground height, G how much
    /// grass (none on the bases, the ball drop zone, steep rock, or under anything built), B tall wheat (head high, you
    /// can hide in it; kept away from the bases and the ball zone), A short grass.
    /// Walked-on grass lies flat and stands back up over ~15 s (the trample map, below); the tall wheat doesn't. The grass
    /// round a fallen log lies flat while it's there. What's built (and a fallen log) keeps the grass off its footprint
    /// until it's gone (Block / Unblock).
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
        static float DecorFadeEnd => Mathf.Min(FadeEnd, 90f) + 2f;   // (the flowers stop at 90 m even when the grass goes further)
        static float DecorFadeStart => DecorFadeEnd - 14f;
        static float Density => GameSettings.GrassDensity;
        static float Falloff => GameSettings.GrassFalloff;
        const int Levels = 3;               // squares of 8, 16 and 32 m (the far ones are one patch stretched)
        const float DecorChunk = 32f;
        const int MaxPush = 16;
        /// <summary>Blades this close to the camera aren't drawn, and fade in (dithered) out to NearFadeTo (m; height counts
        /// double): in the grass it doesn't cover your screen. The tall grass in a patch fades much more (NearFadeTall*:
        /// further out, and see-through for longer). Only for this camera - others still see you hidden.</summary>
        public const float NearFadeFrom = 0.3f, NearFadeTo = 1.7f, NearFadeTallFrom = 0.8f, NearFadeTallTo = 3.4f;
        /// <summary>(tests) Draw the blades at the camera like any others, to compare.</summary>
        public static bool NearFadeOff;
        /// <summary>(tests) Let the outlines draw on the faded grass round the camera again, to compare.</summary>
        public static bool OutlineMaskOff;
        static readonly float[] k_LodFrac = { 1f, 0.5f, 0.25f, 0.125f, 0.0625f, 0.03f };

        static GrassField s_I;
        /// <summary>(tests) the field is up, how many patches were drawn last frame, the flower clearings.</summary>
        public static GrassField Current => s_I;
        public int DrawnPatches { get; private set; }
        public int DrawnTriangles { get; private set; }
        public readonly List<Vector2> ClearingSpots = new List<Vector2>();
        public bool ForceOff;
        /// <summary>How much grass grows at (x, z), as drawn: the field, and on Highlands no blade on the rock (RockKeep).</summary>
        public float CoverAt(float x, float z) => Field(x, z).g * (Cfg.Map == MapKind.Highlands ? RockKeep(MapBuilder.RockField(x, z)) : 1f);
        /// <summary>Each blade's own check against the rock line where it stands (Grass.shader does the same with the
        /// terrain's rock field grid): none from just short of the line.</summary>
        public static float RockKeep(float rockField) => Mathf.Clamp01((-0.004f - rockField) * 60f);
        /// <summary>How far into a tall grass patch (0 outside, under 0.5 the knee-high rim, 0.5 .. 1 the tall middle).</summary>
        public float WheatAt(float x, float z) => Field(x, z).b;
        /// <summary>(tests) Where a blade's root goes at (x, z): the field's height read like the shader reads it (bilinear).</summary>
        public float RootAt(float x, float z)
        {
            float fx = Mathf.Clamp(x - m_Min, 0f, m_N - 1.001f), fz = Mathf.Clamp(z - m_Min, 0f, m_N - 1.001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            float a = m_Px[j * m_N + i].r, b = m_Px[j * m_N + i + 1].r, c = m_Px[(j + 1) * m_N + i].r, d = m_Px[(j + 1) * m_N + i + 1].r;
            if (m_Ground != null) return MapBuilder.GroundHeight(x, z) - 0.04f; // (the shader reads the terrain's own grid)
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) - 0.04f;
        }
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
        readonly Vector4[] m_Tiles = new Vector4[MaxTiles];
        /// <summary>How many patches can be drawn (the shader's _GrassTiles array is this long).</summary>
        const int MaxTiles = 1024;
        /// <summary>A 16 m / 32 m square further out than this isn't split into smaller ones just to draw its blades more
        /// finely (it draws up to all of its stretched patch's blades instead): with the far grass very thick (a low falloff)
        /// everything out to the render distance would otherwise be 8 m squares - far more than MaxTiles.</summary>
        static readonly float[] k_SplitWithin = { 0f, 100f, 190f };
        readonly int[] m_LvN = new int[Levels];
        readonly bool[][] m_LvHas = new bool[Levels][];
        readonly Vector2[][] m_LvY = new Vector2[Levels][];
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
            if (m_TrTex) Destroy(m_TrTex);
            if (m_Ground) Destroy(m_Ground);
            foreach (var st in m_Stages) if (st) Destroy(st);
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
                float y = MapBuilder.GroundHeight(x, z); // (the ground as it's drawn: the blades stand on it, not under it)
                float cover = 1f;
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > half - 1.5f) cover = 0f;           // off the map
                else if (Cfg.BaseTeamAt(new Vector3(x, 0, z)) >= 0) cover = 0f;                  // the bases (you build there)
                else
                {
                    cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 14.5f, new Vector2(x, z).magnitude)); // the ball drop zone
                    // Highlands: grass on all the green ground, right up to the rock faces (thinning out only over the last
                    // metre or so). It used to thin out from well before the rock (-0.18 down to -0.05), which on the gentler
                    // hillsides left wide bands of green ground with no grass on them.
                    // (and each blade checks the rock line itself, exactly where it stands: RockKeep, in the shader too -
                    // the shader blends the 1 m texels round a blade, which would put a few blades on the rock's edge)
                    if (Cfg.Map == MapKind.Highlands)
                        cover *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RockEdge, RockFull, MapBuilder.RockField(x, z)));
                }
                // tall grass patches: the tops of a slow noise (fewer, further apart than they were)
                bool wheat = Mathf.PerlinNoise(wx + x * WheatFreq, wz + z * WheatFreq) > WheatLevel && Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < half - 1.5f;
                // (the tall wheat stays clear of the bases and the ball zone: you see who's coming there)
                if (wheat)
                {
                    float dBase = float.MaxValue;
                    for (int t = 0; t < Cfg.TeamCount && !Cfg.Builder; t++)
                    {
                        var bc = Cfg.BaseCenter[t];
                        float bx = Mathf.Max(0, Mathf.Abs(x - bc.x) - Cfg.BaseHalf), bz = Mathf.Max(0, Mathf.Abs(z - bc.z) - Cfg.BaseHalf);
                        dBase = Mathf.Min(dBase, Mathf.Sqrt(bx * bx + bz * bz));
                    }
                    wheat = dBase > 9f && new Vector2(x, z).magnitude > 23f;
                }
                m_Px[j * m_N + i] = new Color(y, cover, wheat ? 1f : 0f, 0f);
            }
            ShapeWheat();
            Debug.Log($"[RockGame] tall grass: {WheatPatches} patches ({WheatPatchesRaw - WheatPatches} too small dropped), {WheatArea:F0} m² ({WheatCore:F0} m² tall middles)");

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
            // (the grass as generated, for when something built on it goes; and anything already standing on it)
            m_Cover0 = new float[m_Px.Length];
            for (int k = 0; k < m_Px.Length; k++) m_Cover0[k] = m_Px[k].g;
            if (s_Blocks.Count > 0) Recompute(0, m_N - 1, 0, m_N - 1);
            m_FieldDirty = false;
            m_Field.Apply(false);
            // (texel i's centre is at m_Min + i: uv = (world - (m_Min - 0.5)) / m_N)
            Shader.SetGlobalTexture(k_Field, m_Field);
            Shader.SetGlobalVector(k_Rect, new Vector4(m_Min - 0.5f, m_Min - 0.5f, 1f / m_N, 1f / m_N));
            BuildGround();
            BindGround();
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
                    if (m_Cover0[j * m_N + i] > 0.02f) has = true; // (as generated: what's built on it can go)
                    lo = Mathf.Min(lo, p.r);
                    hi = Mathf.Max(hi, p.r);
                }
                m_TileHas[tj * m_TN + ti] = has;
                m_TileY[tj * m_TN + ti] = new Vector2(lo, hi);
            }
            // the same for 16 m and 32 m squares (2x2 / 4x4 of them)
            m_LvN[0] = m_TN; m_LvHas[0] = m_TileHas; m_LvY[0] = m_TileY;
            for (int L = 1; L < Levels; L++)
            {
                int pn = m_LvN[L - 1], n = (pn + 1) / 2;
                m_LvN[L] = n;
                m_LvHas[L] = new bool[n * n];
                m_LvY[L] = new Vector2[n * n];
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    bool has = false;
                    var y = new Vector2(float.MaxValue, float.MinValue);
                    for (int b = 0; b < 2; b++)
                    for (int a = 0; a < 2; a++)
                    {
                        int ci = i * 2 + a, cj = j * 2 + b;
                        if (ci >= pn || cj >= pn) continue;
                        int c = cj * pn + ci;
                        has |= m_LvHas[L - 1][c];
                        y = new Vector2(Mathf.Min(y.x, m_LvY[L - 1][c].x), Mathf.Max(y.y, m_LvY[L - 1][c].y));
                    }
                    m_LvHas[L][j * n + i] = has;
                    m_LvY[L][j * n + i] = y;
                }
            }
            InitTrample();

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
            Shader.SetGlobalVector("_GrassWheat", ColorSlots.Wheat.Value.linear);
            Shader.SetGlobalFloat("_GrassFalloff", Falloff);
            Shader.SetGlobalVector("_GrassDaisyTint", ColorSlots.LinearRatio(ColorSlots.Daisies));
            Shader.SetGlobalVector("_GrassLupinTint", ColorSlots.LinearRatio(ColorSlots.Lupins));
            Shader.SetGlobalFloat("_GrassWind", 1f);
            Shader.SetGlobalVector("_GrassNearFade", NearFadeOff ? Vector4.zero : new Vector4(NearFadeFrom, NearFadeTo, NearFadeTallFrom, NearFadeTallTo));
            Shader.SetGlobalFloat("_GrassDensity", Density);
            Shader.SetGlobalFloat("_GrassHeight", GameSettings.GrassHeight);
            Shader.SetGlobalFloat("_GrassNoInk", OutlineMaskOff ? 0f : 1f);
            var g = GameSettings.WorldTint(GameSettings.WorldColor.Grass);
            Shader.SetGlobalVector("_GrassTint", new Vector4(Mathf.GammaToLinearSpace(g.r), Mathf.GammaToLinearSpace(g.g), Mathf.GammaToLinearSpace(g.b), 1f));
        }

        /// <summary>Highlands: no grass where the rock field (MapBuilder.RockField) is over RockEdge, all of it under RockFull.</summary>
        public const float RockEdge = -0.012f, RockFull = -0.055f;

        // the tall grass patches: where a slow noise (WheatFreq per metre) is over WheatLevel
        const float WheatFreq = 0.026f, WheatLevel = 0.72f;
        /// <summary>A patch has to reach this far in from its edge (m) to stay: room for a tall middle.</summary>
        const float WheatMinDepth = 2.2f;
        /// <summary>(tests) Tall grass patches on the map (and before the small ones were dropped), their area and the tall middles' (m²).</summary>
        public int WheatPatches { get; private set; }
        public int WheatPatchesRaw { get; private set; }
        public float WheatArea { get; private set; }
        public float WheatCore { get; private set; }

        /// <summary>
        /// Gives each tall grass patch its shape: B (1 where it's in a patch) becomes how far in from the patch's edge
        /// that spot is - 0 at the edge, rising to 0.5 across a rim (a third of the way in, at most 3 m) and on to 1 at the
        /// very middle. The shader makes the rim knee-high grass and steps up to the head-high tall grass at 0.5, so the
        /// tallest grass is always in the middle and there's a clear line where it starts. Patches too small to have a
        /// middle are dropped.
        /// </summary>
        void ShapeWheat()
        {
            int n = m_N;
            var d = new float[n * n];
            for (int k = 0; k < d.Length; k++) d[k] = m_Px[k].b > 0.5f ? 1e6f : 0f;
            // the distance to the nearest spot outside (two sweeps, straight steps 1 m and diagonal ones 1.41 m)
            const float D = 1.4142f;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                if (d[k] == 0f) continue;
                float v = d[k];
                if (i > 0) v = Mathf.Min(v, d[k - 1] + 1f);
                if (j > 0)
                {
                    v = Mathf.Min(v, d[k - n] + 1f);
                    if (i > 0) v = Mathf.Min(v, d[k - n - 1] + D);
                    if (i < n - 1) v = Mathf.Min(v, d[k - n + 1] + D);
                }
                d[k] = v;
            }
            for (int j = n - 1; j >= 0; j--)
            for (int i = n - 1; i >= 0; i--)
            {
                int k = j * n + i;
                if (d[k] == 0f) continue;
                float v = d[k];
                if (i < n - 1) v = Mathf.Min(v, d[k + 1] + 1f);
                if (j < n - 1)
                {
                    v = Mathf.Min(v, d[k + n] + 1f);
                    if (i < n - 1) v = Mathf.Min(v, d[k + n + 1] + D);
                    if (i > 0) v = Mathf.Min(v, d[k + n - 1] + D);
                }
                d[k] = v;
            }
            for (int k = 0; k < d.Length; k++) if (d[k] > 0f) d[k] -= 0.5f; // (the edge is half way to the spot outside)
            // each patch on its own (joined side to side), measured by how far in its middle is
            var seen = new bool[n * n];
            var patch = new List<int>();
            var stack = new Stack<int>();
            int kept = 0, raw = 0, area = 0, core = 0;
            for (int s = 0; s < d.Length; s++)
            {
                if (d[s] <= 0f || seen[s]) continue;
                patch.Clear();
                stack.Push(s);
                seen[s] = true;
                float deep = 0f;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    patch.Add(k);
                    deep = Mathf.Max(deep, d[k]);
                    int i = k % n, j = k / n;
                    void Next(int q) { if (d[q] > 0f && !seen[q]) { seen[q] = true; stack.Push(q); } }
                    if (i > 0) Next(k - 1);
                    if (i < n - 1) Next(k + 1);
                    if (j > 0) Next(k - n);
                    if (j < n - 1) Next(k + n);
                }
                raw++;
                bool keep = deep >= WheatMinDepth;
                if (keep) kept++;
                float rim = Mathf.Clamp(deep * 0.36f, 1.2f, 3f);
                foreach (int k in patch)
                {
                    float b = 0f;
                    if (keep)
                    {
                        b = d[k] < rim ? 0.5f * d[k] / rim : 0.5f + 0.5f * Mathf.Clamp01((d[k] - rim) / Mathf.Max(0.5f, deep - rim));
                        area++;
                        if (b >= 0.5f) core++;
                    }
                    m_Px[k].b = b;
                }
            }
            WheatPatches = kept;
            WheatPatchesRaw = raw;
            WheatArea = area;
            WheatCore = core;
        }

        // Highlands: the terrain's own corner heights (2 m grid), so the shader plants every blade on the ground exactly as
        // the mesh draws it (straight across its triangles) - the field's 1 m height, read bilinearly, is a little off it over
        // the bumps (it buried the blades there by up to ~10 cm; planted on MapBuilder.Height it was ~15 cm).
        Texture2D m_Ground;
        Vector4 m_GroundRect;
        static readonly int k_Ground = Shader.PropertyToID("_GrassGround"), k_GroundRect = Shader.PropertyToID("_GrassGroundRect");

        void BuildGround()
        {
            m_GroundRect = Vector4.zero;
            if (!MapBuilder.GroundGrid(out var hs, out var rock, out float half, out float step)) return;
            int n = hs.GetLength(0);
            // (r: the height, g: the rock field - which side of the rock line the corner is on)
            var px = new Vector2[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++) px[j * n + i] = new Vector2(hs[i, j], rock[i, j]);
            m_Ground = new Texture2D(n, n, TextureFormat.RGFloat, false, true) { name = "grass ground", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            m_Ground.SetPixelData(px, 0);
            m_Ground.Apply(false, true);
            m_GroundRect = new Vector4(half, step, n - 1, 1f);
        }

        void BindGround()
        {
            if (m_Ground != null) Shader.SetGlobalTexture(k_Ground, m_Ground);
            else Shader.SetGlobalTexture(k_Ground, Texture2D.blackTexture);
            Shader.SetGlobalVector(k_GroundRect, m_GroundRect);
        }

        int Idx(float w) => Mathf.Clamp(Mathf.RoundToInt(w - m_Min), 0, m_N - 1);
        Color Field(float x, float z) => m_Px[Idx(z) * m_N + Idx(x)];

        /// <summary>Nothing ever grows here again (a box: tests; what's built uses Block / Unblock, which put it back).</summary>
        public static void ClearUnder(Bounds b)
        {
            var g = s_I;
            if (g == null || g.m_Field == null) return;
            int i0 = g.Idx(b.min.x - 0.3f), i1 = g.Idx(b.max.x + 0.3f), j0 = g.Idx(b.min.z - 0.3f), j1 = g.Idx(b.max.z + 0.3f);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int k = j * g.m_N + i;
                if (b.min.y > g.m_Px[k].r + 1.2f) continue; // (not if it's up in the air)
                g.m_Cover0[k] = 0f;
            }
            g.Recompute(i0, i1, j0, j1);
        }

        // =====================================================================
        // Kept clear: no blades through what's built (building pieces, chests, workbenches...) or a fallen log
        // =====================================================================

        // Each thing registers its footprint on the ground (a rectangle turned with it, in metres) while it's there. The
        // field's G under it (and out to Pad round it: the shader blends the 1 m texels round each blade, so a texel next
        // to the footprint would still grow blades through its edge) is 0 while it stands, and back to what was generated
        // when it goes. Only the texels under the one that changed are worked out again, and the texture is uploaded once
        // a frame at most.
        struct Blocker
        {
            public float cx, cz, rx, rz, hx, hz, pad, yMin;
            public bool Covers(float x, float z, float ground)
            {
                if (yMin > ground + 1.2f) return false; // (up in the air: an upper floor)
                float dx = x - cx, dz = z - cz;
                float lx = Mathf.Abs(dx * rx + dz * rz) - hx, lz = Mathf.Abs(-dx * rz + dz * rx) - hz;
                lx = Mathf.Max(0f, lx); lz = Mathf.Max(0f, lz);
                return lx * lx + lz * lz < pad * pad;
            }
            public float Reach => Mathf.Sqrt(hx * hx + hz * hz) + pad;
        }
        static readonly Dictionary<int, Blocker> s_Blocks = new Dictionary<int, Blocker>();
        float[] m_Cover0;
        bool m_FieldDirty;
        /// <summary>How far round a building's footprint the grass is kept clear (m).</summary>
        public const float BuildPad = 0.9f;

        /// <summary>(tests) How many things are keeping the grass clear under them.</summary>
        public static int BlockCount => s_Blocks.Count;

        /// <summary>Keeps the grass clear under a footprint (centre, the way its x side runs, half sizes; while it's there).</summary>
        public static void Block(int id, Vector3 centre, Vector3 right, Vector2 half, float pad, float yMin)
        {
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            if (s_Blocks.ContainsKey(id)) Unblock(id);
            var b = new Blocker { cx = centre.x, cz = centre.z, rx = right.x, rz = right.z, hx = Mathf.Max(0f, half.x), hz = Mathf.Max(0f, half.y), pad = pad, yMin = yMin };
            s_Blocks[id] = b;
            if (s_I != null) s_I.RecomputeAround(b);
        }

        /// <summary>Keeps the grass clear under everything `t` draws (its footprint turned with it), plus BuildPad round it.</summary>
        public static void BlockRenderers(int id, Transform t)
        {
            var frame = Quaternion.Euler(0, t.eulerAngles.y, 0);
            var inv = Quaternion.Inverse(frame);
            var o = t.position;
            bool any = false;
            Vector3 lo = default, hi = default;
            float yMin = o.y;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
            {
                var lb = r.localBounds;
                var m = r.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var w = m.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    yMin = Mathf.Min(yMin, w.y);
                    var l = inv * (w - o);
                    if (!any) { lo = hi = l; any = true; }
                    else { lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); }
                }
            }
            if (!any) return;
            var mid = (lo + hi) * 0.5f;
            Block(id, o + frame * mid, frame * Vector3.right, new Vector2((hi.x - lo.x) * 0.5f, (hi.z - lo.z) * 0.5f), BuildPad, yMin);
        }

        /// <summary>It's gone: the grass grows there again (unless something else still stands on it).</summary>
        public static void Unblock(int id)
        {
            if (!s_Blocks.TryGetValue(id, out var b)) return;
            s_Blocks.Remove(id);
            if (s_I != null) s_I.RecomputeAround(b);
        }

        void RecomputeAround(Blocker b)
        {
            if (m_Field == null || m_Cover0 == null) return;
            float r = b.Reach + 0.5f;
            Recompute(Mathf.Clamp(Mathf.FloorToInt(b.cx - r - m_Min), 0, m_N - 1), Mathf.Clamp(Mathf.CeilToInt(b.cx + r - m_Min), 0, m_N - 1),
                      Mathf.Clamp(Mathf.FloorToInt(b.cz - r - m_Min), 0, m_N - 1), Mathf.Clamp(Mathf.CeilToInt(b.cz + r - m_Min), 0, m_N - 1));
        }

        readonly List<Blocker> m_Near = new List<Blocker>();

        /// <summary>Texels i0..i1 x j0..j1: what was generated there, cleared where anything stands on it.</summary>
        void Recompute(int i0, int i1, int j0, int j1)
        {
            if (m_Field == null || m_Cover0 == null) return;
            float x0 = m_Min + i0, x1 = m_Min + i1, z0 = m_Min + j0, z1 = m_Min + j1;
            m_Near.Clear();
            foreach (var b in s_Blocks.Values)
            {
                float r = b.Reach;
                if (b.cx + r < x0 || b.cx - r > x1 || b.cz + r < z0 || b.cz - r > z1) continue;
                m_Near.Add(b);
            }
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int k = j * m_N + i;
                ref var px = ref m_Px[k];
                float g = m_Cover0[k];
                if (g > 0f)
                    foreach (var b in m_Near)
                        if (b.Covers(m_Min + i, m_Min + j, px.r)) { g = 0f; break; }
                if (g == px.g) continue;
                px.g = g;
                m_Field.SetPixel(i, j, px);
                m_FieldDirty = true;
            }
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
            bool Grows(Vector2 p) { var f = Field(p.x, p.y); return f.g > 0.95f && f.b < 0.05f; }

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
                        md.Add(top, white, 1f, p, 0, KDaisy, lean);
                        md.Add(top + (dir * 0.022f + perp * 0.013f) * size, white, 1f, p, 0, KDaisy, lean);
                        md.Add(top + dir * 0.048f * size, white, 1f, p, 0, KDaisy, lean);
                        md.Add(top + (dir * 0.022f - perp * 0.013f) * size, white, 1f, p, 0, KDaisy, lean);
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
                        md.Add(-a, stem, 0f, p, 0, KGreen, lean); md.Add(a, stem, 0f, p, 0, KGreen, lean);
                        md.Add(-a + Vector3.up * H * 0.4f, stem, 0.4f, p, 0, KGreen, lean); md.Add(a + Vector3.up * H * 0.4f, stem, 0.4f, p, 0, KGreen, lean);
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
                            md.Add(-a * widths[s] + Vector3.up * y, col, Mathf.Lerp(0.38f, 1f, t), p, 0, KLupin, lean);
                            md.Add(a * widths[s] + Vector3.up * y, col, Mathf.Lerp(0.38f, 1f, t), p, 0, KLupin, lean);
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

        // what each flower vertex is, for the colour settings (uv0.w; the shader tints by it)
        const float KDaisyMiddle = 0f, KDaisy = 1f, KLupin = 2f, KGreen = 3f;

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
                md.Add(c - perp * width * ws[s] * 0.5f - Vector3.up * width * ws[s] * 0.12f, tint, t * 0.7f, root, 0, KGreen, Vector2.zero);
                md.Add(c + Vector3.up * 0.005f, tint * 1.08f, t * 0.7f, root, 0, KGreen, Vector2.zero);
                md.Add(c + perp * width * ws[s] * 0.5f - Vector3.up * width * ws[s] * 0.12f, tint, t * 0.7f, root, 0, KGreen, Vector2.zero);
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
            if (m_FieldDirty && m_Field != null) { m_FieldDirty = false; m_Field.Apply(false); } // (what's built / gone this frame)
            if (!m_On || m_Lods == null || s_I != this) return;
            // (the field is shared by every grass material; re-bind it in case another map's field was up)
            Shader.SetGlobalTexture(k_Field, m_Field);
            Shader.SetGlobalVector(k_Rect, new Vector4(m_Min - 0.5f, m_Min - 0.5f, 1f / m_N, 1f / m_N));
            BindGround();
            SetSettings();
            UpdateTrample();
            var cam = Camera.main;
            if (cam == null) return;
            var cp = cam.transform.position;

            // flowers: only the chunks close enough to see
            foreach (var (r, c) in m_Decor)
                if (r) r.enabled = Vector2.Distance(c, new Vector2(cp.x, cp.z)) < DecorFadeEnd + DecorChunk * 0.75f;

            UpdatePush(cp);

            GeometryUtility.CalculateFrustumPlanes(cam, m_Planes);
            foreach (var b in m_Batches) b.Clear();
            // the squares, coarsest first: a 32 m square far away is one patch stretched 4x (its blades 4x further
            // apart - out there only a few are drawn anyway), nearer ones split into 16 m and then 8 m squares
            int top = Levels - 1, topN = m_LvN[top];
            int r0i = (Mathf.FloorToInt((cp.x - FadeEnd) / Tile) - m_T0) >> top, r1i = (Mathf.FloorToInt((cp.x + FadeEnd) / Tile) - m_T0) >> top;
            int r0j = (Mathf.FloorToInt((cp.z - FadeEnd) / Tile) - m_T0) >> top, r1j = (Mathf.FloorToInt((cp.z + FadeEnd) / Tile) - m_T0) >> top;
            for (int tj = Mathf.Max(0, r0j); tj <= Mathf.Min(topN - 1, r1j); tj++)
            for (int ti = Mathf.Max(0, r0i); ti <= Mathf.Min(topN - 1, r1i); ti++)
                Visit(top, ti, tj, cp);
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

        static float DensityAt(float d) => (d < Near ? 1f : Mathf.Pow(Near / d, Falloff)) * Density;

        /// <summary>One square of level L (8 m << L): drawn as one (stretched) patch, or split into four if it needs more blades than that gives.</summary>
        void Visit(int L, int i, int j, Vector3 cp)
        {
            int n = m_LvN[L];
            if (i >= n || j >= n) return;
            int t = j * n + i;
            if (!m_LvHas[L][t]) return;
            int s = 1 << L;
            float size = Tile * s;
            float x0 = (m_T0 + (i << L)) * Tile, z0 = (m_T0 + (j << L)) * Tile;
            float nx = Mathf.Clamp(cp.x, x0, x0 + size) - cp.x, nz = Mathf.Clamp(cp.z, z0, z0 + size) - cp.z;
            float near = Mathf.Sqrt(nx * nx + nz * nz);
            if (near > FadeEnd) return;
            var ty = m_LvY[L][t];
            // (up to 4.5 m: the tall wheat)
            var b = new Bounds(new Vector3(x0 + size * 0.5f, (ty.x + ty.y) * 0.5f + 2f, z0 + size * 0.5f), new Vector3(size + 1f, ty.y - ty.x + 5.2f, size + 1f));
            if (!GeometryUtility.TestPlanesAABB(m_Planes, b)) return;
            float need = DensityAt(near) * s * s; // (a patch stretched s times has 1/s² the blades per m²)
            if (L > 0 && need > 0.25f && near < k_SplitWithin[L])
            {
                // too close for this size (it would need the full-detail blades): the four smaller squares instead
                Visit(L - 1, i * 2, j * 2, cp); Visit(L - 1, i * 2 + 1, j * 2, cp);
                Visit(L - 1, i * 2, j * 2 + 1, cp); Visit(L - 1, i * 2 + 1, j * 2 + 1, cp);
                return;
            }
            need = Mathf.Min(1f, need);
            int lod = 0;
            while (lod + 1 < k_LodFrac.Length && k_LodFrac[lod + 1] >= need) lod++;
            m_Batches[lod].Add(new Vector4(x0, s, z0, 0));
            DrawnPatches++;
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
    
        // =====================================================================
        // The trample map: where the grass has been walked flat
        // =====================================================================

        // Every 0.5 m square of the map remembers when something last stood on it (seconds, Time.time). The shader
        // lays a blade flat while that was under TrampleHold seconds ago and stands it back up by TrampleBack, so
        // nothing has to be updated while the grass recovers. Ten times a second, whatever moved (players on the
        // ground, horses and cars, the ball) stamps the strip it went over since the last time, and things lying
        // still (dropped items, chests) stamp their spot every couple of seconds; only the small squares that changed
        // are copied to the texture (a 16x16 staging texture per block, Graphics.CopyTexture into the big one).
        const float TrampleRes = 0.5f, TrampleHold = 4f, TrampleBack = 15f, TrampleTick = 0.1f;
        /// <summary>How far past a fallen log's side the grass round it is flattened (m).</summary>
        public const float LogFlatten = 0.8f;
        const int Stage = 16;
        float[] m_Tr;
        int m_TrN;
        float m_TrMin;
        Texture2D m_TrTex;
        bool m_TrCopy;
        readonly List<Texture2D> m_Stages = new List<Texture2D>();
        readonly float[] m_StageBuf = new float[Stage * Stage];
        readonly List<RectInt> m_Dirty = new List<RectInt>();
        readonly Dictionary<int, (Vector3 p, float t)> m_Last = new Dictionary<int, (Vector3, float)>();
        float m_NextTick, m_NextStill;
        static readonly int k_Trample = Shader.PropertyToID("_GrassTrample"), k_TrampleRect = Shader.PropertyToID("_GrassTrampleRect"),
            k_TrampleTime = Shader.PropertyToID("_GrassTrampleTime");

        static readonly bool s_TrampleDebug = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-trample-debug") >= 0;

        void InitTrample()
        {
            m_TrMin = m_Min - 0.5f; // (the same square as the grass field)
            m_TrN = Mathf.CeilToInt(m_N / TrampleRes);
            m_Tr = new float[m_TrN * m_TrN];
            for (int i = 0; i < m_Tr.Length; i++) m_Tr[i] = -1e5f; // (never)
            m_TrCopy = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-trample-full") < 0;
            Debug.Log($"[RockGame] trample map {m_TrN}x{m_TrN}, block copies {m_TrCopy} ({SystemInfo.graphicsDeviceType}, {SystemInfo.copyTextureSupport})");
            m_TrTex = new Texture2D(m_TrN, m_TrN, TextureFormat.RFloat, false, true) { name = "grass trample", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            m_TrTex.SetPixelData(m_Tr, 0);
            m_TrTex.Apply(false, m_TrCopy); // (only the GPU copy is needed when the blocks can be copied in)
            BindTrample();
        }

        void BindTrample()
        {
            Shader.SetGlobalTexture(k_Trample, m_TrTex);
            float size = m_TrN * TrampleRes;
            Shader.SetGlobalVector(k_TrampleRect, new Vector4(m_TrMin, m_TrMin, 1f / size, 1f / size));
            Shader.SetGlobalVector(k_TrampleTime, new Vector4(Time.time, TrampleHold, TrampleBack, s_TrampleDebug ? 1 : 0));
        }

        /// <summary>(tests) How flat the grass is here right now: 1 just walked on, 0 standing.</summary>
        public float FlatAt(float x, float z)
        {
            if (m_Tr == null) return 0f;
            int i = Mathf.Clamp(Mathf.FloorToInt((x - m_TrMin) / TrampleRes), 0, m_TrN - 1), j = Mathf.Clamp(Mathf.FloorToInt((z - m_TrMin) / TrampleRes), 0, m_TrN - 1);
            float age = Time.time - m_Tr[j * m_TrN + i];
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TrampleHold, TrampleBack, age));
        }

        void UpdateTrample()
        {
            BindTrample();
            float now = Time.time;
            if (now < m_NextTick || m_Tr == null) return;
            m_NextTick = now + TrampleTick;
            m_Dirty.Clear();
            foreach (var p in PlayerNet.All) if (p != null) Step(p.GetInstanceID(), p.transform.position, 0.75f, now);
            foreach (var v in Vehicle.All) if (v != null) Step(v.GetInstanceID(), v.transform.position, v.IsHorse ? 0.8f : 1.2f, now);
            if (Ball.Instance != null) Step(Ball.Instance.GetInstanceID(), Ball.Instance.transform.position, 0.45f, now);
            if (now >= m_NextStill)
            {
                // things lying still keep their spot flat
                m_NextStill = now + 2f;
                foreach (var c in Container.All) if (c != null) { var p = c.transform.position; if (OnGround(p)) Stamp(p, p, 0.8f, now); }
                var ng = NetGame.Instance;
                if (ng != null && ng.IsSpawned)
                    for (int i = 0; i < ng.Items.Count; i++) { var p = ng.Items[i].Pos; if (OnGround(p)) Stamp(p, p, 0.4f, now); }
                // fallen logs: the grass round them lies flat while they're there (and stands back up once one's chopped up)
                var cam = Camera.main;
                float near = FadeEnd + 25f;
                foreach (var n in ResourceNode.All)
                    if (n != null && cam != null && n.LogLying(out var a, out var b, out float r) && (n.transform.position - cam.transform.position).sqrMagnitude < near * near)
                        Stamp(a, b, r + LogFlatten, now);
                if (m_Last.Count > 256) m_Last.Clear();
            }
            Upload();
        }

        bool OnGround(Vector3 p)
        {
            float gy = Field(p.x, p.z).r;
            return p.y - gy < 0.5f && p.y - gy > -1.5f;
        }

        /// <summary>Something that moves: stamps the strip it went over since last time (on the ground only).</summary>
        void Step(int key, Vector3 p, float r, float now)
        {
            if (!OnGround(p)) { m_Last.Remove(key); return; }
            if (m_Last.TryGetValue(key, out var last))
            {
                var d = p - last.p;
                d.y = 0;
                bool moved = d.sqrMagnitude > 0.05f * 0.05f;
                if (!moved && now - last.t < 1.5f) return; // (standing still: now and then is enough)
                Stamp(d.sqrMagnitude < 25f ? last.p : p, p, r, now); // (not a long streak after a teleport)
            }
            else Stamp(p, p, r, now);
            m_Last[key] = (p, now);
        }

        /// <summary>Flattens the strip from a to b, radius r (the edges a little less: they stand up again sooner).</summary>
        void Stamp(Vector3 a, Vector3 b, float r, float now)
        {
            float inv = 1f / TrampleRes;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - r - m_TrMin) * inv)), i1 = Mathf.Min(m_TrN - 1, Mathf.FloorToInt((Mathf.Max(a.x, b.x) + r - m_TrMin) * inv));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - r - m_TrMin) * inv)), j1 = Mathf.Min(m_TrN - 1, Mathf.FloorToInt((Mathf.Max(a.z, b.z) + r - m_TrMin) * inv));
            if (i1 < i0 || j1 < j0) return;
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            float len2 = ab.sqrMagnitude;
            bool any = false;
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                var q = new Vector2(m_TrMin + (i + 0.5f) * TrampleRes - a.x, m_TrMin + (j + 0.5f) * TrampleRes - a.z);
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q, ab) / len2) : 0f;
                float e = (q - ab * t).magnitude / r;
                if (e >= 1f) continue;
                float edge = Mathf.Clamp01((e - 0.45f) / 0.55f); // (flat out to about half way, then less and less)
                float v = now - edge * edge * (TrampleBack * 0.8f);
                ref float cur = ref m_Tr[j * m_TrN + i];
                if (v <= cur) continue;
                cur = v;
                any = true;
            }
            if (any) m_Dirty.Add(new RectInt(i0, j0, i1 - i0 + 1, j1 - j0 + 1));
        }

        /// <summary>Copies the squares that changed into the texture.</summary>
        void Upload()
        {
            if (m_Dirty.Count == 0) return;
            if (!m_TrCopy)
            {
                m_TrTex.SetPixelData(m_Tr, 0);
                m_TrTex.Apply(false);
                return;
            }
            int used = 0;
            foreach (var rc in m_Dirty)
            for (int by = rc.yMin; by < rc.yMax; by += Stage)
            for (int bx = rc.xMin; bx < rc.xMax; bx += Stage)
            {
                int w = Mathf.Min(Stage, rc.xMax - bx), h = Mathf.Min(Stage, rc.yMax - by);
                if (used == m_Stages.Count)
                    m_Stages.Add(new Texture2D(Stage, Stage, TextureFormat.RFloat, false, true) { name = "trample stage", filterMode = FilterMode.Point });
                var st = m_Stages[used++];
                for (int y = 0; y < h; y++)
                    System.Array.Copy(m_Tr, (by + y) * m_TrN + bx, m_StageBuf, y * Stage, w);
                st.SetPixelData(m_StageBuf, 0);
                st.Apply(false);
                Graphics.CopyTexture(st, 0, 0, 0, 0, w, h, m_TrTex, 0, 0, bx, by);
            }
        }
    }
}
