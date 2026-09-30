using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RockGame
{
    /// <summary>
    /// AI PSX TEST graphics mode (main menu > Graphics): its own look, separate from Normal and PSX (neither of those
    /// changes). The PSX trees are the reference: everything else out in the wild map is re-skinned to match them -
    /// chunky low-res pixel textures (made here, point sampled, no mipmaps, posterised), and the RockGame/AiPsx shader
    /// (wobbly snapped vertices, warping affine textures, per-vertex lighting, 15-bit dithered colour). The trees are the
    /// PSX tree models themselves, there are grass tufts on the ground like the trees' foliage cards, the picture is
    /// rendered at half resolution with blocky upscaling, and there's distance fog.
    ///
    /// Only the looks change. It works by swapping the materials of the map, resource nodes and horses (the colours stay,
    /// the texture comes from what kind of thing it is) and swapping the originals back when you leave the mode.
    /// </summary>
    public static class AiPsxArt
    {
        public static bool On => GameSettings.AiPsx;

        enum Tex { Grass, Dirt, Rock, Bark, Leaf, Wood, Brick, Snow, Sand, Hide, Metal, Plain, GrassCard }

        static Shader s_Shader;
        static bool s_ShaderTried;
        static readonly Dictionary<Tex, Texture2D> s_Tex = new Dictionary<Tex, Texture2D>();
        static readonly Dictionary<(Tex, Color, int), Material> s_Mats = new Dictionary<(Tex, Color, int), Material>();
        static readonly List<(Renderer r, Material[] mats)> s_Swapped = new List<(Renderer, Material[])>();
        static readonly HashSet<Renderer> s_Done = new HashSet<Renderer>();
        static GameObject s_Grass;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            GameSettings.GraphicsChanged += OnChanged;
            Application.quitting += RenderSettingsOff;
            if (On) RenderSettingsOn();
        }

        static Shader Shader
        {
            get
            {
                if (!s_ShaderTried)
                {
                    s_ShaderTried = true;
                    var sh = Resources.Load<Shader>("AiPsx/AiPsx");
                    s_Shader = sh != null && sh.isSupported ? sh : null;
                    if (s_Shader == null) Debug.LogWarning("[RockGame] AI PSX shader missing or not supported");
                }
                return s_Shader;
            }
        }

        /// <summary>Graphics mode switched: skin everything that's out there, or put the original looks back.</summary>
        static void OnChanged()
        {
            if (On)
            {
                RenderSettingsOn();
                if (MapBuilder.Root != null) ApplyWorld(MapBuilder.Root);
                foreach (var v in Vehicle.All) if (v != null && v.IsHorse) ApplyAnimal(v.transform);
            }
            else Revert();
        }

        /// <summary>The map (ground, walls, towers, mountains, the stadium...) plus the grass tufts.</summary>
        public static void ApplyWorld(Transform root)
        {
            if (!On || root == null || Shader == null) return;
            Apply(root);
            if (s_Grass == null || s_Grass.transform.parent != root) BuildGrass(root);
        }

        /// <summary>Re-skin every Art-coloured part under root (resource nodes, the map).</summary>
        public static void Apply(Transform root) => Apply(root, false);

        /// <summary>Animals (horses): textures stick to the body instead of the world, and brown is fur.</summary>
        public static void ApplyAnimal(Transform root) => Apply(root, true);

        static void Apply(Transform root, bool animal)
        {
            if (!On || root == null || Shader == null) return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || s_Done.Contains(r)) continue;
                var mats = r.sharedMaterials;
                var skinned = new Material[mats.Length];
                bool any = false;
                bool ground = r.GetComponentInParent<GroundMarker>() != null;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!Art.IsArtMat(m, out var c)) { skinned[i] = m; continue; }
                    skinned[i] = MaterialFor(Classify(r, mesh, c, ground, animal), c, animal);
                    any = true;
                }
                if (!any) continue;
                s_Swapped.Add((r, mats));
                s_Done.Add(r);
                r.sharedMaterials = skinned;
            }
        }

        /// <summary>Put every swapped material back and switch the render settings off.</summary>
        public static void Revert()
        {
            foreach (var (r, mats) in s_Swapped) if (r != null) r.sharedMaterials = mats;
            s_Swapped.Clear();
            s_Done.Clear();
            if (s_Grass != null) Object.Destroy(s_Grass);
            s_Grass = null;
            RenderSettingsOff();
        }

        // =====================================================================
        // What each thing is made of
        // =====================================================================

        static Tex Classify(Renderer r, Mesh mesh, Color c, bool ground, bool animal)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            float hue = h * 360f;
            bool box = mesh == Art.Cube;
            bool flat = r.transform.lossyScale.y < 0.08f;
            if (s < 0.14f)
            {
                if (v > 0.84f) return Tex.Snow;
                return box && !ground ? Tex.Brick : Tex.Rock;
            }
            if (hue >= 65f && hue <= 170f) return ground || flat ? Tex.Grass : Tex.Leaf;
            if (flat && !ground) return Tex.Plain; // painted-on things (base pads, lines, the drop zone)
            if (hue >= 15f && hue <= 55f)
            {
                if (animal) return Tex.Hide;
                if (v > 0.7f && s < 0.6f) return ground || flat ? Tex.Sand : Tex.Plain;
                if (ground || flat) return Tex.Dirt;
                return mesh == Art.Cylinder ? Tex.Bark : Tex.Wood;
            }
            if (s < 0.3f && v < 0.45f) return Tex.Rock;
            return Tex.Plain;
        }

        static float TilingOf(Tex t)
        {
            switch (t)
            {
                case Tex.Grass: case Tex.Sand: case Tex.Snow: case Tex.Dirt: return 0.22f;
                case Tex.Rock: return 0.3f;
                case Tex.Brick: return 0.33f;
                case Tex.Wood: case Tex.Bark: return 0.5f;
                case Tex.Leaf: return 0.7f;
                case Tex.Hide: return 1.3f;
                default: return 0.5f;
            }
        }

        static Material MaterialFor(Tex t, Color c, bool objectSpace)
        {
            var key = (t, c, objectSpace ? 1 : 0);
            if (s_Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader) { name = "aipsx " + t };
            m.SetTexture("_MainTex", TexOf(t));
            // the textures average about 0.8 bright, so the colour is lifted to keep the map's palette
            var tint = new Color(Mathf.Min(1f, c.r * 1.22f), Mathf.Min(1f, c.g * 1.22f), Mathf.Min(1f, c.b * 1.22f), 1f);
            m.SetColor("_Color", tint);
            m.SetFloat("_Tiling", TilingOf(t));
            m.SetFloat("_UVMode", objectSpace ? 2f : 1f);
            m.SetFloat("_Cutoff", 0f);
            m.SetFloat("_Cull", 2f);
            s_Mats[key] = m;
            return m;
        }

        // =====================================================================
        // Pixel textures (32x32, 5-6 tones, point sampled, no mipmaps - PS1 style)
        // =====================================================================

        static Texture2D TexOf(Tex t)
        {
            if (s_Tex.TryGetValue(t, out var tex) && tex != null) return tex;
            tex = Make(t);
            s_Tex[t] = tex;
            return tex;
        }

        static float Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269504);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }

        /// <summary>Tileable value noise: `cells` lattice cells across the texture.</summary>
        static float Noise(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int W(int a) => ((a % cells) + cells) % cells;
            float a00 = Hash(W(x0), W(y0), seed), a10 = Hash(W(x0 + 1), W(y0), seed);
            float a01 = Hash(W(x0), W(y0 + 1), seed), a11 = Hash(W(x0 + 1), W(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
        }

        static float Fbm(float u, float v, int seed) => Noise(u, v, 4, seed) * 0.55f + Noise(u, v, 8, seed + 1) * 0.3f + Noise(u, v, 16, seed + 2) * 0.15f;

        static float Posterise(float l, int steps) => Mathf.Round(Mathf.Clamp01(l) * steps) / steps;

        static Texture2D Make(Tex t)
        {
            const int S = 32;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                Color c = Pixel(t, x, y, u, v, S);
                px[y * S + x] = c;
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = t == Tex.GrassCard ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
                name = "aipsx " + t,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Color Grey(float l, float r = 1f, float g = 1f, float b = 1f) => new Color(l * r, l * g, l * b, 1f);

        static Color Pixel(Tex t, int x, int y, float u, float v, int S)
        {
            switch (t)
            {
                case Tex.Grass:
                {
                    float l = 0.66f + 0.22f * Fbm(u, v, 11);
                    // little blades: bright and dark flecks in short vertical runs
                    float blade = Hash(x, y / 3, 12);
                    if (blade > 0.86f) l += 0.16f;
                    else if (blade < 0.1f) l -= 0.14f;
                    return Grey(Posterise(l, 6), 1.03f, 1f, 0.9f);
                }
                case Tex.Dirt:
                {
                    float l = 0.62f + 0.3f * Fbm(u, v, 21);
                    float h = Hash(x, y, 22);
                    if (h > 0.94f) l += 0.2f; // pebbles
                    else if (h < 0.05f) l -= 0.18f;
                    return Grey(Posterise(l, 6), 1.04f, 1f, 0.94f);
                }
                case Tex.Rock:
                {
                    float l = 0.64f + 0.3f * Fbm(u, v, 31);
                    float crack = Mathf.Abs(Noise(u, v, 6, 32) - 0.5f);
                    if (crack < 0.02f) l -= 0.14f;
                    if (Hash(x, y, 33) > 0.95f) l += 0.1f;
                    return Grey(Posterise(l, 5));
                }
                case Tex.Bark:
                {
                    float l = 0.58f + 0.28f * Noise(u * 4f, v * 0.5f, 8, 41);
                    if (Mathf.Abs(Noise(u, v * 0.3f, 8, 42) - 0.5f) < 0.05f) l = 0.4f; // deep grooves
                    return Grey(Posterise(l, 5), 1.05f, 1f, 0.92f);
                }
                case Tex.Leaf:
                {
                    float l = 0.52f + 0.38f * Noise(u, v, 10, 51);
                    if (Hash(x, y, 52) > 0.88f) l += 0.14f;
                    return Grey(Posterise(l, 5), 1f, 1.03f, 0.9f);
                }
                case Tex.Wood:
                {
                    int plank = y / 8;
                    float l = 0.72f + 0.12f * (Hash(plank, 0, 61) - 0.5f) + 0.14f * (Noise(u * 0.25f + plank * 0.37f, v * 4f, 8, 62) - 0.5f);
                    if (y % 8 == 0) l = 0.42f;                                       // gaps between planks
                    if ((x + plank * 11) % 16 == 3 && y % 8 == 4) l = 0.35f;          // nails
                    return Grey(Posterise(l, 6), 1.05f, 1f, 0.9f);
                }
                case Tex.Brick:
                {
                    int row = y / 8;
                    int bx = (x + (row % 2) * 8) / 16;
                    bool mortar = y % 8 == 0 || (x + (row % 2) * 8) % 16 == 0;
                    float l = mortar ? 0.52f : 0.74f + 0.16f * (Hash(bx, row, 71) - 0.5f) + 0.1f * (Fbm(u, v, 72) - 0.5f);
                    return Grey(Posterise(l, 6));
                }
                case Tex.Snow:
                {
                    float l = 0.9f + 0.08f * Fbm(u, v, 81);
                    var c = Grey(Posterise(l, 8));
                    if (Hash(x, y, 82) > 0.93f) c = new Color(0.82f, 0.88f, 1f, 1f);
                    return c;
                }
                case Tex.Sand:
                {
                    float l = 0.82f + 0.1f * Fbm(u, v, 91) + 0.05f * Mathf.Sin((x + Noise(u, v, 4, 92) * 8f) * 0.8f);
                    if (Hash(x, y, 93) > 0.9f) l -= 0.08f;
                    return Grey(Posterise(l, 7), 1.02f, 1f, 0.95f);
                }
                case Tex.Hide:
                {
                    float l = 0.68f + 0.24f * Noise(u * 2f, v * 0.5f, 8, 101);
                    if (Hash(x, y, 102) > 0.9f) l -= 0.1f;
                    return Grey(Posterise(l, 5));
                }
                case Tex.Metal:
                {
                    float l = 0.72f + 0.1f * Fbm(u, v, 111);
                    if (x % 16 == 0 || y % 16 == 0) l = 0.5f;
                    if (x % 16 == 3 && y % 16 == 3) l = 0.95f;
                    return Grey(Posterise(l, 6));
                }
                case Tex.GrassCard:
                {
                    // blades growing up from the bottom edge, dark at the root and light at the tip
                    for (int k = 0; k < 9; k++)
                    {
                        float bx = 2 + Hash(k, 0, 121) * (S - 4);
                        float top = S * (0.45f + Hash(k, 1, 122) * 0.5f);
                        float lean = (Hash(k, 2, 123) - 0.5f) * 0.6f;
                        float cx = bx + lean * y;
                        if (y <= top && Mathf.Abs(x - cx) <= (y < top * 0.6f ? 1.1f : 0.6f))
                        {
                            float l = Posterise(0.5f + 0.5f * (y / top), 4);
                            return new Color(l * 0.95f, l, l * 0.8f, 1f);
                        }
                    }
                    return new Color(0, 0, 0, 0);
                }
                default:
                {
                    float l = 0.74f + 0.16f * Fbm(u, v, 131);
                    return Grey(Posterise(l, 6));
                }
            }
        }

        // =====================================================================
        // Grass tufts (crossed cutout cards, like the PSX trees' foliage) - one mesh, one draw call
        // =====================================================================

        static void BuildGrass(Transform root)
        {
            if (s_Grass != null) Object.Destroy(s_Grass);
            s_Grass = null;
            Color tint;
            switch (Cfg.Map)
            {
                case MapKind.Plains: case MapKind.Highlands: case MapKind.Ruins: tint = new Color(0.42f, 0.62f, 0.3f); break;
                case MapKind.Canyon: tint = new Color(0.72f, 0.62f, 0.34f); break;       // THEME MAPS: dry grass
                default: return;                                                          // snow, sand, ash: no grass
            }
            var rng = new System.Random(7 + Cfg.MapSeed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = Cfg.MapHalf - 3f;
            int count = Mathf.RoundToInt(1100 * (half / 100f) * (half / 100f));
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            for (int n = 0; n < count; n++)
            {
                var p = new Vector3(R(-half, half), 0, R(-half, half));
                if (Cfg.BaseTeamAt(p) >= 0 && !Cfg.Builder) continue;
                if (new Vector2(p.x, p.z).magnitude < 9f) continue;
                if (!ThemeMaps.SpotOk(p)) continue; // THEME MAPS
                float y = MapBuilder.Height(p.x, p.z);
                // not on steep slopes
                if (Mathf.Abs(MapBuilder.Height(p.x + 1f, p.z) - y) > 0.7f || Mathf.Abs(MapBuilder.Height(p.x, p.z + 1f) - y) > 0.7f) continue;
                p.y = y - 0.03f;
                float w = R(0.7f, 1.2f), h = R(0.45f, 0.8f), yaw = R(0, 180);
                for (int q = 0; q < 2; q++)
                {
                    var right = Quaternion.Euler(0, yaw + q * 90f, 0) * Vector3.right * (w * 0.5f);
                    int b = verts.Count;
                    verts.Add(p - right); verts.Add(p + right); verts.Add(p + right + Vector3.up * h); verts.Add(p - right + Vector3.up * h);
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                    for (int k = 0; k < 4; k++) norms.Add(Vector3.up);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            }
            if (verts.Count == 0) return;
            var mesh = new Mesh { name = "aipsx grass", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var mat = new Material(Shader) { name = "aipsx grass" };
            mat.SetTexture("_MainTex", TexOf(Tex.GrassCard));
            mat.SetColor("_Color", tint * 1.2f);
            mat.SetFloat("_UVMode", 0f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_Cull", 0f);
            s_Grass = new GameObject("aipsx grass");
            s_Grass.transform.SetParent(root, false);
            s_Grass.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = s_Grass.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // =====================================================================
        // Half resolution with blocky upscaling, and distance fog
        // =====================================================================

        static bool s_RsOn;
        static float s_PrevScale = 1f;
        static UpscalingFilterSelection s_PrevFilter;
        static bool s_PrevFog;
        static FogMode s_PrevFogMode;
        static float s_PrevFogStart, s_PrevFogEnd;
        static Color s_PrevFogColor;

        static void RenderSettingsOn()
        {
            if (s_RsOn) return;
            s_RsOn = true;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                s_PrevScale = urp.renderScale;
                s_PrevFilter = urp.upscalingFilter;
                urp.renderScale = 0.5f;
                urp.upscalingFilter = UpscalingFilterSelection.Point;
            }
            s_PrevFog = RenderSettings.fog;
            s_PrevFogMode = RenderSettings.fogMode;
            s_PrevFogStart = RenderSettings.fogStartDistance;
            s_PrevFogEnd = RenderSettings.fogEndDistance;
            s_PrevFogColor = RenderSettings.fogColor;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 160f;
            RenderSettings.fogColor = new Color(0.6f, 0.66f, 0.72f);
        }

        static void RenderSettingsOff()
        {
            if (!s_RsOn) return;
            s_RsOn = false;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = s_PrevScale;
                urp.upscalingFilter = s_PrevFilter;
            }
            RenderSettings.fog = s_PrevFog;
            RenderSettings.fogMode = s_PrevFogMode;
            RenderSettings.fogStartDistance = s_PrevFogStart;
            RenderSettings.fogEndDistance = s_PrevFogEnd;
            RenderSettings.fogColor = s_PrevFogColor;
        }
    }
}
