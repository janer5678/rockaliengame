using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > WORLD: the grass's render distance and density, and the colours of the Normal look
    /// (ground, rock, grass, pine leaves, clouds, sky). Saved in PlayerPrefs; changes apply live (WorldLookChanged).</summary>
    public static partial class GameSettings
    {
        public enum WorldColor { Ground, Rock, Grass, Leaves, Clouds, Sky }
        public static readonly string[] WorldColorNames = { "Ground", "Rock", "Grass", "Tree leaves", "Clouds", "Sky" };

        /// <summary>The Normal look's colours (what "Reset" goes back to).</summary>
        public static readonly Color[] WorldColorDefaults =
        {
            new Color(0.44f, 0.64f, 0.31f),  // ground: a touch lighter than the old flat green
            new Color(0.58f, 0.56f, 0.52f),  // Highlands rock: a touch lighter than the old grey
            new Color(0.36f, 0.6f, 0.16f),   // grass blades (the average of their tips)
            new Color(0.29f, 0.55f, 0.15f),   // pine needles
            new Color(0.97f, 0.97f, 1f),     // clouds
            new Color(0.45f, 0.65f, 0.95f),  // sky
        };

        /// <summary>A few ready-made colours for each (the first is the default).</summary>
        public static readonly Color[][] WorldColorPresets =
        {
            new[] { WorldColorDefaults[0], new Color(0.34f, 0.52f, 0.25f), new Color(0.55f, 0.62f, 0.3f), new Color(0.66f, 0.6f, 0.36f), new Color(0.5f, 0.38f, 0.25f), new Color(0.88f, 0.9f, 0.93f), new Color(0.3f, 0.55f, 0.45f) },
            new[] { WorldColorDefaults[1], new Color(0.45f, 0.43f, 0.4f), new Color(0.7f, 0.68f, 0.64f), new Color(0.62f, 0.5f, 0.38f), new Color(0.5f, 0.52f, 0.6f), new Color(0.35f, 0.33f, 0.36f), new Color(0.75f, 0.62f, 0.5f) },
            new[] { WorldColorDefaults[2], new Color(0.25f, 0.5f, 0.15f), new Color(0.48f, 0.66f, 0.18f), new Color(0.66f, 0.62f, 0.25f), new Color(0.2f, 0.45f, 0.3f), new Color(0.75f, 0.45f, 0.2f), new Color(0.45f, 0.35f, 0.65f) },
            new[] { WorldColorDefaults[3], new Color(0.16f, 0.36f, 0.14f), new Color(0.36f, 0.58f, 0.18f), new Color(0.2f, 0.42f, 0.32f), new Color(0.7f, 0.42f, 0.15f), new Color(0.72f, 0.25f, 0.18f), new Color(0.85f, 0.9f, 0.92f) },
            new[] { WorldColorDefaults[4], new Color(1f, 0.93f, 0.85f), new Color(1f, 0.8f, 0.85f), new Color(0.8f, 0.83f, 0.9f), new Color(0.55f, 0.57f, 0.62f), new Color(1f, 0.75f, 0.5f), new Color(0.8f, 0.7f, 1f) },
            new[] { WorldColorDefaults[5], new Color(0.3f, 0.5f, 0.95f), new Color(0.6f, 0.75f, 0.95f), new Color(0.95f, 0.6f, 0.45f), new Color(0.75f, 0.5f, 0.9f), new Color(0.55f, 0.6f, 0.65f), new Color(0.4f, 0.85f, 0.8f) },
        };

        public const float GrassDistanceMin = 25f, GrassDistanceMax = 90f, GrassDistanceDefault = 60f;
        public const float GrassDensityMin = 0.2f, GrassDensityDefault = 1f;

        static Color[] s_WorldColors;
        static float s_GrassDistance = GrassDistanceDefault, s_GrassDensity = GrassDensityDefault;

        /// <summary>Fired when a world colour or the grass settings change.</summary>
        public static event System.Action WorldLookChanged;

        static void LoadWorld()
        {
            if (s_WorldColors != null) return;
            s_WorldColors = new Color[WorldColorDefaults.Length];
            for (int i = 0; i < s_WorldColors.Length; i++)
            {
                s_WorldColors[i] = WorldColorDefaults[i];
                var hex = PlayerPrefs.GetString("RockGame.World." + (WorldColor)i, "");
                if (hex.Length > 0 && ColorUtility.TryParseHtmlString("#" + hex, out var c)) s_WorldColors[i] = c;
            }
            s_GrassDistance = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.GrassDistance", GrassDistanceDefault), GrassDistanceMin, GrassDistanceMax);
            s_GrassDensity = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.GrassDensity", GrassDensityDefault), GrassDensityMin, 1f);
        }

        public static Color GetWorldColor(WorldColor w) { LoadWorld(); return s_WorldColors[(int)w]; }

        /// <summary>The colour as a multiplier of the default one (what the shaders tint by).</summary>
        public static Color WorldTint(WorldColor w)
        {
            var c = GetWorldColor(w);
            var d = WorldColorDefaults[(int)w];
            return new Color(c.r / Mathf.Max(0.01f, d.r), c.g / Mathf.Max(0.01f, d.g), c.b / Mathf.Max(0.01f, d.b), 1f);
        }

        public static void SetWorldColor(WorldColor w, Color c, bool save = true)
        {
            LoadWorld();
            c.a = 1f;
            if (s_WorldColors[(int)w] == c) return;
            s_WorldColors[(int)w] = c;
            if (save) { PlayerPrefs.SetString("RockGame.World." + w, ColorUtility.ToHtmlStringRGB(c)); PlayerPrefs.Save(); }
            WorldLookChanged?.Invoke();
        }

        /// <summary>The grass is drawn out to this far (m).</summary>
        public static float GrassDistance { get { LoadWorld(); return s_GrassDistance; } }
        /// <summary>How many of the grass blades are drawn (1 = all).</summary>
        public static float GrassDensity { get { LoadWorld(); return s_GrassDensity; } }

        public static void SetGrass(float distance, float density, bool save = true)
        {
            LoadWorld();
            distance = Mathf.Clamp(distance, GrassDistanceMin, GrassDistanceMax);
            density = Mathf.Clamp(density, GrassDensityMin, 1f);
            if (Mathf.Approximately(distance, s_GrassDistance) && Mathf.Approximately(density, s_GrassDensity)) return;
            s_GrassDistance = distance;
            s_GrassDensity = density;
            if (save) { PlayerPrefs.SetFloat("RockGame.GrassDistance", distance); PlayerPrefs.SetFloat("RockGame.GrassDensity", density); PlayerPrefs.Save(); }
            WorldLookChanged?.Invoke();
        }

        /// <summary>Every world colour and the grass back to the defaults.</summary>
        public static void ResetWorldLook(bool save = true)
        {
            LoadWorld();
            for (int i = 0; i < s_WorldColors.Length; i++)
            {
                s_WorldColors[i] = WorldColorDefaults[i];
                if (save) PlayerPrefs.DeleteKey("RockGame.World." + (WorldColor)i);
            }
            s_GrassDistance = GrassDistanceDefault;
            s_GrassDensity = GrassDensityDefault;
            if (save) { PlayerPrefs.DeleteKey("RockGame.GrassDistance"); PlayerPrefs.DeleteKey("RockGame.GrassDensity"); PlayerPrefs.Save(); }
            WorldLookChanged?.Invoke();
        }
    }

    /// <summary>
    /// The Normal look's shared world materials (flat-colour ground and rock; the RockGame/Painted vertex-colour meshes:
    /// castle walls, pine foliage, flags, clouds) and the sky, kept in step with the colours picked in the settings.
    /// </summary>
    public static class WorldLook
    {
        /// <summary>The colours the AI PSX look classifies the ground and rock by (the old flat colours, so it's unchanged).</summary>
        public static readonly Color ArtGrass = new Color(0.36f, 0.56f, 0.3f), ArtRock = new Color(0.47f, 0.45f, 0.42f);

        static Material s_Ground, s_Rock, s_Foliage, s_Castle, s_Flag, s_Cloud;
        static Shader s_Painted;
        static bool s_ShaderTried;
        static Material s_SkyOrig, s_Sky;
        static Color s_SkyTintOrig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            GameSettings.WorldLookChanged += Apply;
            GameSettings.GraphicsChanged += Apply;
            Apply();
        }

        static Shader Painted
        {
            get
            {
                if (!s_ShaderTried)
                {
                    s_ShaderTried = true;
                    var sh = Resources.Load<Shader>("World/Painted");
                    s_Painted = sh != null && sh.isSupported ? sh : null;
                    if (s_Painted == null) Debug.LogWarning("[RockGame] Painted shader missing or not supported");
                }
                return s_Painted;
            }
        }

        public static bool HasPainted => Painted != null;

        static Material LitMat(Color c, string name)
        {
            var src = Bootstrap.I != null ? Bootstrap.I.baseMaterial : null;
            var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = name;
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        /// <summary>The flat-colour ground (grass) / Highlands rock material. Counts as the old Art colour, so the AI PSX look re-skins it as before.</summary>
        public static Material GroundMaterial(bool rock)
        {
            ref var m = ref (rock ? ref s_Rock : ref s_Ground);
            if (m == null)
            {
                m = LitMat(GameSettings.GetWorldColor(rock ? GameSettings.WorldColor.Rock : GameSettings.WorldColor.Ground), rock ? "ground rock" : "ground grass");
                Art.Register(m, rock ? ArtRock : ArtGrass);
            }
            return m;
        }

        static Material PaintedMat(ref Material m, string name, float wind, bool twoSided, float glow)
        {
            if (m != null) return m;
            if (Painted == null) return null;
            m = new Material(Painted) { name = name };
            m.SetFloat("_Wind", wind);
            m.SetFloat("_Glow", glow);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.SetColor("_Tint", Color.white);
            Apply();
            return m;
        }

        /// <summary>Pine needles (sway in the wind). Null if the shader isn't there (callers fall back to plain colours).</summary>
        public static Material Foliage => PaintedMat(ref s_Foliage, "pine foliage", 1f, false, 0f);
        public static Material Castle => PaintedMat(ref s_Castle, "castle", 0f, false, 0f);
        public static Material Flag => PaintedMat(ref s_Flag, "flag cloth", 2f, true, 0.15f);
        public static Material Cloud => PaintedMat(ref s_Cloud, "clouds", 0f, false, 0.45f);

        /// <summary>Push the picked colours into the materials, the grass and the sky.</summary>
        public static void Apply()
        {
            if (s_Ground) { var c = GameSettings.GetWorldColor(GameSettings.WorldColor.Ground); s_Ground.SetColor("_BaseColor", c); s_Ground.color = c; }
            if (s_Rock) { var c = GameSettings.GetWorldColor(GameSettings.WorldColor.Rock); s_Rock.SetColor("_BaseColor", c); s_Rock.color = c; }
            if (s_Foliage) s_Foliage.SetColor("_Tint", GameSettings.WorldTint(GameSettings.WorldColor.Leaves));
            if (s_Cloud) s_Cloud.SetColor("_Tint", GameSettings.GetWorldColor(GameSettings.WorldColor.Clouds));
            var g = GameSettings.WorldTint(GameSettings.WorldColor.Grass);
            Shader.SetGlobalVector("_GrassTint", new Vector4(Mathf.GammaToLinearSpace(g.r), Mathf.GammaToLinearSpace(g.g), Mathf.GammaToLinearSpace(g.b), 1f));
            ApplySky();
        }

        /// <summary>The sky's tint (Normal graphics only: PSX and AI PSX keep the sky they had).</summary>
        static void ApplySky()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) return;
            if (s_Sky == null)
            {
                if (!sky.HasProperty("_SkyTint")) return;
                s_SkyOrig = sky;
                s_SkyTintOrig = sky.GetColor("_SkyTint");
                s_Sky = new Material(sky) { name = sky.name + " (tinted)" };
                RenderSettings.skybox = s_Sky;
            }
            else if (sky != s_Sky) return; // (someone else's sky is up)
            var t = GameSettings.GraphicsMode == 0 ? GameSettings.WorldTint(GameSettings.WorldColor.Sky) : Color.white;
            s_Sky.SetColor("_SkyTint", new Color(Mathf.Clamp01(s_SkyTintOrig.r * t.r), Mathf.Clamp01(s_SkyTintOrig.g * t.g), Mathf.Clamp01(s_SkyTintOrig.b * t.b), 1f));
        }
    }

    /// <summary>Two looks for one thing: `normal` renderers show in Normal graphics, `other` ones in PSX and AI PSX
    /// (e.g. the castle walls in Normal, the old plain walls - which PSX re-textures - otherwise).</summary>
    public class NormalLook : MonoBehaviour
    {
        public readonly List<Renderer> Normal = new List<Renderer>(), Other = new List<Renderer>();
        int m_Mode = -1;

        void OnEnable() { GameSettings.GraphicsChanged += Refresh; m_Mode = -1; Refresh(); }
        void OnDisable() => GameSettings.GraphicsChanged -= Refresh;

        /// <summary>Call once the lists are filled.</summary>
        public void Done() { m_Mode = -1; Refresh(); }

        public void Refresh()
        {
            if (this == null) return;
            int mode = GameSettings.GraphicsMode == 0 ? 0 : 1;
            if (mode == m_Mode) return;
            m_Mode = mode;
            foreach (var r in Normal) if (r) r.enabled = mode == 0;
            foreach (var r in Other) if (r) r.enabled = mode != 0;
        }

        public static NormalLook On(GameObject go) { var l = go.GetComponent<NormalLook>(); return l != null ? l : go.AddComponent<NormalLook>(); }
    }

    /// <summary>Destroys a mesh made at runtime along with the object that shows it.</summary>
    public class OwnedMesh : MonoBehaviour
    {
        public Mesh Mesh;
        void OnDestroy() { if (Mesh) Destroy(Mesh); }
    }

    /// <summary>Builds flat-shaded, vertex-coloured meshes (for the RockGame/Painted shader) out of boxes, prisms and cones.</summary>
    public class MeshKit
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> U = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        public int Count => V.Count;

        /// <summary>A flat-shaded triangle (colour in sRGB; it's turned linear for the vertex colour).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, Vector2 ua = default, Vector2 ub = default, Vector2 uc = default)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int k = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(n); N.Add(n); N.Add(n);
            C.Add(ca.linear); C.Add(cb.linear); C.Add(cc.linear);
            U.Add(ua); U.Add(ub); U.Add(uc);
            T.Add(k); T.Add(k + 1); T.Add(k + 2);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col) => Tri(a, b, c, col, col, col);

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            // (Box lists each face's corners so that this normal points out)
            var n = Vector3.Cross(c - a, b - a).normalized;
            int k = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (int i = 0; i < 4; i++) { N.Add(n); C.Add(col.linear); U.Add(Vector2.zero); }
            T.Add(k); T.Add(k + 2); T.Add(k + 1);
            T.Add(k); T.Add(k + 3); T.Add(k + 2);
        }

        public const int PX = 1, NX = 2, PZ = 4, NZ = 8, Top = 16, Bottom = 32, Sides = 15, NoBottom = 31, All = 63;

        /// <summary>A box (centre, size, turned by rot); faces: which sides to make (PX | NZ | Top ...).</summary>
        public void Box(Vector3 c, Vector3 size, Quaternion rot, Color col, int faces = NoBottom)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => c + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            if ((faces & PX) != 0) Quad(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), col * 0.95f);
            if ((faces & NX) != 0) Quad(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), col * 0.95f);
            if ((faces & PZ) != 0) Quad(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), col);
            if ((faces & NZ) != 0) Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), col);
            if ((faces & Top) != 0) Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), col * 1.05f);
            if ((faces & Bottom) != 0) Quad(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), col * 0.8f);
        }

        public void Box(Vector3 c, Vector3 size, Color col, int faces = NoBottom) => Box(c, size, Quaternion.identity, col, faces);

        /// <summary>A pyramid / cone roof: `sides` faces from a ring of radius r at base up to the apex.</summary>
        public void Cone(Vector3 basePos, float r, float height, int sides, float spin, Color col, Color tip)
        {
            var apex = basePos + Vector3.up * height;
            for (int s = 0; s < sides; s++)
            {
                float a0 = spin + s * Mathf.PI * 2f / sides, a1 = spin + (s + 1) * Mathf.PI * 2f / sides;
                var p0 = basePos + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r;
                var p1 = basePos + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
                Tri(p0, apex, p1, col, tip, col);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetUVs(0, U); m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }

        public void Clear() { V.Clear(); N.Clear(); C.Clear(); U.Clear(); T.Clear(); }

        /// <summary>A child object drawing this mesh with mat (the mesh goes when the object does).</summary>
        public GameObject Spawn(Transform parent, string name, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<OwnedMesh>().Mesh = mesh;
            return go;
        }
    }
}
