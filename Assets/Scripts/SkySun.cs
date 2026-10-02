using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Normal graphics: a stylised low-poly sun in the sky, where the main (directional) light comes from - a faceted
    /// disc with a darker rim, a ring of pointed rays and a soft two-step halo, in flat colours like the rest of the
    /// world (shader RockGame/Sun, unlit, no fog). It sits 1100 m out along the light's direction from the camera every
    /// frame, so like the sky it never gets closer; it turns very slowly. Its colour is the Sun slot in Settings >
    /// Display. The procedural skybox's own sun disc is switched off while it shows. Hidden in PSX / AI PSX and in the
    /// sudden death arena in space.
    /// </summary>
    public class SkySun : MonoBehaviour
    {
        const float Dist = 1100f, Radius = 52f;
        static Shader s_Shader;
        static bool s_Tried;
        public static SkySun Current { get; private set; }
        public bool Shown { get; private set; }
        Renderer m_R;
        Material m_Mat;
        Mesh m_Mesh;

        public static SkySun Build(Transform root)
        {
            if (!s_Tried)
            {
                s_Tried = true;
                var sh = Resources.Load<Shader>("World/Sun");
                s_Shader = sh != null && sh.isSupported ? sh : null;
                if (s_Shader == null) Debug.LogWarning("[RockGame] Sun shader missing or not supported");
            }
            if (s_Shader == null) return null;
            var go = new GameObject("Sun (sky)");
            go.transform.SetParent(root, false);
            var s = go.AddComponent<SkySun>();
            s.m_Mat = new Material(s_Shader) { name = "sun" };
            s.m_Mesh = MakeMesh();
            go.AddComponent<MeshFilter>().sharedMesh = s.m_Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s.m_Mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            s.m_R = mr;
            s.ApplyColour();
            Current = s;
            return s;
        }

        void OnEnable() => GameSettings.WorldLookChanged += ApplyColour;
        void OnDisable() => GameSettings.WorldLookChanged -= ApplyColour;

        void OnDestroy()
        {
            if (Current == this) { Current = null; WorldLook.HideSkyboxSun(false); }
            if (m_Mesh) Destroy(m_Mesh);
            if (m_Mat) Destroy(m_Mat);
        }

        void ApplyColour()
        {
            if (m_Mat) m_Mat.SetColor("_Tint", ColorSlots.Ratio(ColorSlots.Sun));
        }

        /// <summary>Where the sunlight comes from (unit vector towards the sun).</summary>
        public static Vector3 Direction
        {
            get
            {
                var sun = RenderSettings.sun;
                if (sun == null)
                    foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional && l.enabled) { sun = l; break; }
                return sun != null ? -sun.transform.forward : new Vector3(0.37f, 0.77f, -0.52f).normalized;
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            bool show = cam != null && GameSettings.GraphicsMode == 0 && !SpaceArena.NearArena(cam.transform.position);
            if (show != Shown)
            {
                Shown = show;
                m_R.enabled = show;
                WorldLook.HideSkyboxSun(show);
            }
            if (!show) return;
            var dir = Direction;
            float d = Mathf.Min(Dist, cam.farClipPlane * 0.85f);
            transform.position = cam.transform.position + dir * d;
            // the disc faces the camera, turning very slowly
            transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 0, Time.time * 1.5f);
            transform.localScale = Vector3.one * (Radius * d / Dist);
        }

        /// <summary>The sun in the XY plane, radius 1 for the disc. Colours are sRGB here (MeshKit makes them linear).</summary>
        static Mesh MakeMesh()
        {
            var kit = new MeshKit();
            const int n = 16;
            Vector3 P(float a, float r) => new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            float step = Mathf.PI * 2f / n;
            Color A(Color c, float a) { c.a = a; return c; }
            var halo = new Color(1f, 0.9f, 0.62f);
            var ray = new Color(1f, 0.8f, 0.34f);
            var rim = new Color(1f, 0.74f, 0.28f);
            var face = new Color(1f, 0.88f, 0.48f);
            var face2 = new Color(1f, 0.92f, 0.6f);
            var core = new Color(1f, 0.97f, 0.82f);
            // (drawn in this order: halo, rays, rim, face)
            // the halo: two soft steps
            for (int i = 0; i < n; i++)
            {
                float a0 = i * step, a1 = a0 + step;
                Ring(kit, P(a0, 1.05f), P(a1, 1.05f), P(a1, 1.9f), P(a0, 1.9f), A(halo, 0.3f), A(halo, 0.16f));
                Ring(kit, P(a0, 1.9f), P(a1, 1.9f), P(a1, 3.0f), P(a0, 3.0f), A(halo, 0.16f), A(halo, 0f));
            }
            // the rays: pointed, long and short in turn, between the facets
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / 12f;
                float len = i % 2 == 0 ? 1.85f : 1.5f, w = Mathf.PI * 2f / 12f * 0.32f;
                kit.Tri(P(a - w, 1.02f), P(a + w, 1.02f), P(a, len), A(ray, 0.95f), A(ray, 0.95f), A(ray, 0.55f));
            }
            // the disc: a darker rim, then facets in two tones round a pale middle
            for (int i = 0; i < n; i++)
            {
                float a0 = i * step, a1 = a0 + step;
                Ring(kit, P(a0, 0.84f), P(a1, 0.84f), P(a1, 1.06f), P(a0, 1.06f), A(rim, 1f), A(rim, 1f));
                // between the outer 16-gon and an inner one turned half a step: facets pointing in and out in turn
                var f = i % 2 == 0 ? face : face2;
                var g = Color.Lerp(i % 2 == 0 ? face2 : face, core, 0.45f);
                Vector3 i0 = P(a0 + step * 0.5f, 0.45f), i1 = P(a1 + step * 0.5f, 0.45f);
                kit.Tri(P(a0, 0.84f), P(a1, 0.84f), i0, f, f, f);
                kit.Tri(i0, P(a1, 0.84f), i1, g, g, g);
                kit.Tri(Vector3.zero, i0, i1, core, core, core);
            }
            var m = kit.ToMesh("sun");
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 7f);
            return m;
        }

        static void Ring(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color inner, Color outer)
        {
            kit.Tri(a, b, c, inner, inner, outer);
            kit.Tri(a, c, d, inner, outer, outer);
        }
    }
}
