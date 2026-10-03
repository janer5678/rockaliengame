using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > WORLD: the grass's render distance, density and far thickness, and the world's
    /// colours (all of them are slots in ColorSlots; the six below are the original ones, kept for older callers).
    /// Saved in PlayerPrefs; changes apply live (WorldLookChanged).</summary>
    public static partial class GameSettings
    {
        public enum WorldColor { Ground, Rock, Grass, Leaves, Clouds, Sky }
        public static readonly string[] WorldColorNames = { "Ground", "Rock", "Grass", "Tree leaves", "Clouds", "Sky" };

        static ColorSlots.Slot Slot(WorldColor w) => ColorSlots.All[(int)w];

        /// <summary>The Normal look's colours (what "Reset" goes back to).</summary>
        public static readonly Color[] WorldColorDefaults =
        {
            ColorSlots.Ground.Default, ColorSlots.Rock.Default, ColorSlots.Grass.Default,
            ColorSlots.Leaves.Default, ColorSlots.Clouds.Default, ColorSlots.Sky.Default,
        };

        /// <summary>A few ready-made colours for each (the first is the default).</summary>
        public static readonly Color[][] WorldColorPresets =
        {
            ColorSlots.Ground.Presets, ColorSlots.Rock.Presets, ColorSlots.Grass.Presets,
            ColorSlots.Leaves.Presets, ColorSlots.Clouds.Presets, ColorSlots.Sky.Presets,
        };

        public const float GrassDistanceMin = 25f, GrassDistanceMax = 270f, GrassDistanceDefault = 60f;
        public const float GrassDensityMin = 0.2f, GrassDensityDefault = 1f;
        /// <summary>How fast the grass thins out with distance: density = (11 m / distance) ^ falloff.</summary>
        public const float GrassFalloffMin = 1f, GrassFalloffMax = 2.5f, GrassFalloffDefault = 1.35f;

        static bool s_WorldLoaded;
        static float s_GrassDistance = GrassDistanceDefault, s_GrassDensity = GrassDensityDefault, s_GrassFalloff = GrassFalloffDefault;

        /// <summary>Fired when a world colour or the grass settings change.</summary>
        public static event System.Action WorldLookChanged;
        internal static void FireWorldLookChanged() => WorldLookChanged?.Invoke();

        static void LoadWorld()
        {
            if (s_WorldLoaded) return;
            s_WorldLoaded = true;
            s_GrassDistance = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.GrassDistance", GrassDistanceDefault), GrassDistanceMin, GrassDistanceMax);
            s_GrassDensity = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.GrassDensity", GrassDensityDefault), GrassDensityMin, 1f);
            s_GrassFalloff = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.GrassFalloff", GrassFalloffDefault), GrassFalloffMin, GrassFalloffMax);
        }

        public static Color GetWorldColor(WorldColor w) => Slot(w).Value;

        /// <summary>The colour as a multiplier of the default one (what the shaders tint by).</summary>
        public static Color WorldTint(WorldColor w) => ColorSlots.Ratio(Slot(w));

        public static void SetWorldColor(WorldColor w, Color c, bool save = true) => ColorSlots.Set(Slot(w), c, save);

        /// <summary>The grass is drawn out to this far (m).</summary>
        public static float GrassDistance { get { LoadWorld(); return s_GrassDistance; } }
        /// <summary>How many of the grass blades are drawn (1 = all).</summary>
        public static float GrassDensity { get { LoadWorld(); return s_GrassDensity; } }
        /// <summary>How fast the grass thins out past 11 m (the power it falls off by; lower = thicker far away).</summary>
        public static float GrassFalloff { get { LoadWorld(); return s_GrassFalloff; } }

        public static void SetGrass(float distance, float density, bool save = true) => SetGrass(distance, density, GrassFalloff, save);

        public static void SetGrass(float distance, float density, float falloff, bool save = true)
        {
            LoadWorld();
            distance = Mathf.Clamp(distance, GrassDistanceMin, GrassDistanceMax);
            density = Mathf.Clamp(density, GrassDensityMin, 1f);
            falloff = Mathf.Clamp(falloff, GrassFalloffMin, GrassFalloffMax);
            if (Mathf.Approximately(distance, s_GrassDistance) && Mathf.Approximately(density, s_GrassDensity) && Mathf.Approximately(falloff, s_GrassFalloff)) return;
            s_GrassDistance = distance;
            s_GrassDensity = density;
            s_GrassFalloff = falloff;
            if (save)
            {
                PlayerPrefs.SetFloat("RockGame.GrassDistance", distance);
                PlayerPrefs.SetFloat("RockGame.GrassDensity", density);
                PlayerPrefs.SetFloat("RockGame.GrassFalloff", falloff);
                PlayerPrefs.Save();
            }
            WorldLookChanged?.Invoke();
        }

        /// <summary>Every world colour back to the defaults.</summary>
        public static void ResetWorldColours(bool save = true)
        {
            ColorSlots.ResetAll(save);
            if (save) PlayerPrefs.Save();
            WorldLookChanged?.Invoke();
        }

        /// <summary>Every world colour and the grass back to the defaults.</summary>
        public static void ResetWorldLook(bool save = true)
        {
            LoadWorld();
            ColorSlots.ResetAll(save);
            s_GrassDistance = GrassDistanceDefault;
            s_GrassDensity = GrassDensityDefault;
            s_GrassFalloff = GrassFalloffDefault;
            if (save) { PlayerPrefs.DeleteKey("RockGame.GrassDistance"); PlayerPrefs.DeleteKey("RockGame.GrassDensity"); PlayerPrefs.DeleteKey("RockGame.GrassFalloff"); PlayerPrefs.Save(); }
            WorldLookChanged?.Invoke();
        }
    }

    /// <summary>
    /// The Normal look's shared world materials (flat-colour ground and rock; the RockGame/Painted vertex-colour meshes:
    /// pine foliage, flags, clouds) and the sky, kept in step with the colours picked in the settings.
    /// </summary>
    public static class WorldLook
    {
        /// <summary>The colours the AI PSX look classifies the ground and rock by (the old flat colours, so it's unchanged).</summary>
        public static readonly Color ArtGrass = new Color(0.36f, 0.56f, 0.3f), ArtRock = new Color(0.47f, 0.45f, 0.42f);

        static Material s_Ground, s_Rock, s_Foliage, s_Flag, s_Cloud, s_Bark, s_BushLeaves, s_Berries;
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
        public static Material Flag => PaintedMat(ref s_Flag, "flag cloth", 2f, true, 0.15f);
        public static Material Cloud => PaintedMat(ref s_Cloud, "clouds", 0f, false, 0.45f);
        /// <summary>The pines' trunks (they sway with the needles), tinted by the tree trunk colour.</summary>
        public static Material Bark => PaintedMat(ref s_Bark, "pine bark", 1f, false, 0f);
        /// <summary>The berry bushes' leaves and berries (they sway like the pines), tinted by their colours.</summary>
        public static Material BushLeaves => PaintedMat(ref s_BushLeaves, "bush leaves", 1f, false, 0f);
        public static Material Berries => PaintedMat(ref s_Berries, "bush berries", 1f, false, 0f);

        /// <summary>(tests) Where the Painted shader's foliage wind (_Wind 1) moves a vertex at world position ws (uv0 = its
        /// sway and flutter weights; root = the object's origin) at shader time t. Keep in step with Painted.shader.</summary>
        public static Vector3 PaintedWind(Vector3 root, Vector3 ws, Vector2 uv, float t)
        {
            float gust = Mathf.Sin(t * 0.9f + root.x * 0.06f + root.z * 0.045f) * 0.5f + 0.5f;
            var wind = new Vector2(0.07f, 0.035f) * 2.2f * gust
                     + new Vector2(Mathf.Sin(t * 1.3f + root.x * 0.35f + root.z * 0.2f), Mathf.Cos(t * 1.1f + root.z * 0.3f - root.x * 0.13f)) * 0.05f;
            float flutter = Mathf.Sin(t * 3.7f + Vector3.Dot(ws, new Vector3(1.3f, 0.7f, 1.1f))) * 0.025f * uv.y;
            ws.x += wind.x * uv.x + flutter;
            ws.z += wind.y * uv.x + flutter;
            ws.y += flutter * 0.5f;
            return ws;
        }

        /// <summary>Push the picked colours into the materials, the grass and the sky.</summary>
        public static void Apply()
        {
            if (s_Ground) { var c = GameSettings.GetWorldColor(GameSettings.WorldColor.Ground); s_Ground.SetColor("_BaseColor", c); s_Ground.color = c; }
            if (s_Rock) { var c = GameSettings.GetWorldColor(GameSettings.WorldColor.Rock); s_Rock.SetColor("_BaseColor", c); s_Rock.color = c; }
            if (s_Foliage) s_Foliage.SetColor("_Tint", GameSettings.WorldTint(GameSettings.WorldColor.Leaves));
            if (s_Cloud) s_Cloud.SetColor("_Tint", GameSettings.GetWorldColor(GameSettings.WorldColor.Clouds));
            if (s_Bark) s_Bark.SetColor("_Tint", ColorSlots.Ratio(ColorSlots.TreeTrunks));
            if (s_BushLeaves) s_BushLeaves.SetColor("_Tint", ColorSlots.Ratio(ColorSlots.Bushes));
            if (s_Berries) s_Berries.SetColor("_Tint", ColorSlots.Ratio(ColorSlots.Berries));
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
            if (s_Sky.HasProperty("_SunSize"))
            {
                if (s_SunSizeOrig < 0f) s_SunSizeOrig = s_Sky.GetFloat("_SunSize");
                s_Sky.SetFloat("_SunSize", s_HideSkySun ? 0f : s_SunSizeOrig);
            }
        }

        static float s_SunSizeOrig = -1f;
        static bool s_HideSkySun;

        /// <summary>The low-poly sun (SkySun) is up: the skybox's own sun disc goes (and comes back when it goes).</summary>
        public static void HideSkyboxSun(bool hide)
        {
            if (s_HideSkySun == hide) return;
            s_HideSkySun = hide;
            ApplySky();
        }
    }

    /// <summary>Two looks for one thing: `normal` renderers show in Normal graphics, `other` ones in PSX and AI PSX
    /// (e.g. the waving cloth flags in Normal, the old flat flags otherwise).</summary>
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

    /// <summary>Builds vertex-coloured meshes (for the RockGame/Painted shader) out of boxes, prisms and cones: flat-shaded,
    /// or smooth-shaded after SmoothNormals.</summary>
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

        /// <summary>
        /// Smooth shading: every corner's normal becomes the average of the faces that meet at that spot (weighted by
        /// their angle there) - only faces bent less than `crease` degrees from its own, so sharp edges (a tier's rim
        /// over its underside, a cloud's flat bottom) stay sharp. The corners keep their own colours; nothing is merged.
        /// </summary>
        public void SmoothNormals(float crease = 70f)
        {
            int tris = T.Count / 3;
            var fn = new Vector3[tris];
            var at = new Dictionary<Vector3Int, List<int>>(); // (corners at each spot, as indices into T)
            for (int f = 0; f < tris; f++)
            {
                Vector3 a = V[T[f * 3]], b = V[T[f * 3 + 1]], c = V[T[f * 3 + 2]];
                fn[f] = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    var p = V[T[f * 3 + k]];
                    var key = new Vector3Int(Mathf.RoundToInt(p.x * 500f), Mathf.RoundToInt(p.y * 500f), Mathf.RoundToInt(p.z * 500f));
                    if (!at.TryGetValue(key, out var l)) at[key] = l = new List<int>(6);
                    l.Add(f * 3 + k);
                }
            }
            float AngleAt(int corner)
            {
                int f = corner / 3, k = corner % 3;
                Vector3 p = V[T[corner]], q = V[T[f * 3 + (k + 1) % 3]], r = V[T[f * 3 + (k + 2) % 3]];
                return Vector3.Angle(q - p, r - p);
            }
            float cos = Mathf.Cos(crease * Mathf.Deg2Rad);
            var done = new Vector3[V.Count];
            foreach (var l in at.Values)
                foreach (int corner in l)
                {
                    var own = fn[corner / 3];
                    if (own == Vector3.zero) continue;
                    var sum = Vector3.zero;
                    foreach (int o in l)
                    {
                        var on = fn[o / 3];
                        if (Vector3.Dot(own, on) >= cos) sum += on * AngleAt(o);
                    }
                    done[T[corner]] = sum.sqrMagnitude > 1e-12f ? sum.normalized : own;
                }
            for (int i = 0; i < V.Count; i++) if (done[i] != Vector3.zero) N[i] = done[i];
        }

        /// <summary>Several kits as the submeshes of one mesh (one draw each, with their own materials).</summary>
        public static Mesh ToMesh(string name, params MeshKit[] kits)
        {
            int n = 0;
            foreach (var k in kits) n += k.Count;
            var m = new Mesh { name = name, indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            var v = new List<Vector3>(n); var nr = new List<Vector3>(n); var c = new List<Color>(n); var u = new List<Vector2>(n);
            foreach (var k in kits) { v.AddRange(k.V); nr.AddRange(k.N); c.AddRange(k.C); u.AddRange(k.U); }
            m.SetVertices(v); m.SetNormals(nr); m.SetColors(c); m.SetUVs(0, u);
            m.subMeshCount = kits.Length;
            int at = 0;
            for (int i = 0; i < kits.Length; i++)
            {
                var t = new List<int>(kits[i].T.Count);
                foreach (int x in kits[i].T) t.Add(x + at);
                m.SetTriangles(t, i);
                at += kits[i].Count;
            }
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A child object drawing these kits as submeshes, each with its own material.</summary>
        public static GameObject Spawn(Transform parent, string name, Material[] mats, bool shadows, params MeshKit[] kits)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = ToMesh(name, kits);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<OwnedMesh>().Mesh = mesh;
            return go;
        }

        static Vector3[] s_IcoV;
        static int[] s_IcoF;

        /// <summary>A unit icosphere subdivided once (42 corners, 80 faces): round enough to read as a puff or a clump of
        /// leaves, still chunky low-poly.</summary>
        public static void Ico80(out Vector3[] verts, out int[] faces)
        {
            if (s_IcoV == null)
            {
                float t = (1f + Mathf.Sqrt(5f)) / 2f;
                var v = new List<Vector3>
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
                for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
                s_IcoF = Subdivide(v, f);
                s_IcoV = v.ToArray();
            }
            verts = s_IcoV;
            faces = s_IcoF;
        }

        static Vector3[] s_Ico320V;
        static int[] s_Ico320F;

        /// <summary>The same sphere subdivided once more (162 corners, 320 faces): rounder, for smooth-shaded puffs.</summary>
        public static void Ico320(out Vector3[] verts, out int[] faces)
        {
            if (s_Ico320V == null)
            {
                Ico80(out var v80, out var f80);
                var v = new List<Vector3>(v80);
                s_Ico320F = Subdivide(v, f80);
                s_Ico320V = v.ToArray();
            }
            verts = s_Ico320V;
            faces = s_Ico320F;
        }

        /// <summary>Splits every face of a unit sphere into four (new corners on the sphere, added to v); returns the faces.</summary>
        static int[] Subdivide(List<Vector3> v, int[] f)
        {
            var mids = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (mids.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                return mids[key] = v.Count - 1;
            }
            var faces2 = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                faces2.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            return faces2.ToArray();
        }

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
