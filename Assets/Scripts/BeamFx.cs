using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Glowing additive light (World/Beam.shader): the airdrop ship's beam and the glow under it, the ball's beacon, the
    /// victory UFO's beam and the arena's light shafts. Columns are the built-in cylinder (object y -1..1) or a smooth
    /// open cone (Shaft: y 0 at the narrow end .. 1 at the wide end); discs are a flat quad with a soft round glow.
    /// Falls back to the plain see-through ghost material if the shader isn't there.
    /// </summary>
    public static class BeamFx
    {
        static Shader s_Shader;
        static Mesh s_Quad, s_Shaft;
        static readonly int k_Color = Shader.PropertyToID("_Color"), k_Intensity = Shader.PropertyToID("_Intensity"), k_Base = Shader.PropertyToID("_BaseColor");

        static Shader Sh
        {
            get
            {
                if (s_Shader == null) s_Shader = Resources.Load<Shader>("World/Beam");
                return s_Shader != null && s_Shader.isSupported ? s_Shader : null;
            }
        }

        /// <summary>Is the real beam shader in use (not the ghost fallback)?</summary>
        public static bool Real => Sh != null;

        /// <summary>A new beam material: colour, brightness (HDR above 1), how soft its edges are (higher = a thinner
        /// bright core), how strong its moving bands are and how fast they run (+ up), how far its ends fade.</summary>
        public static Material Column(Color c, float intensity, float edge = 1.5f, float bands = 0.25f, float scroll = 1f, float fadeBottom = 0.02f, float fadeTop = 0.1f, float bandScale = 0.35f)
        {
            var sh = Sh;
            if (sh == null)
            {
                var g = new Material(Art.Ghost(new Color(c.r, c.g, c.b, 0.3f))) { name = "beam (ghost)" };
                Set(g, c, intensity);
                return g;
            }
            var m = new Material(sh) { name = "beam" };
            m.SetColor(k_Color, c);
            m.SetFloat(k_Intensity, intensity);
            m.SetFloat("_Edge", edge);
            m.SetFloat("_Bands", bands);
            m.SetFloat("_Scroll", scroll);
            m.SetFloat("_BandScale", bandScale);
            m.SetFloat("_FadeBottom", fadeBottom);
            m.SetFloat("_FadeTop", fadeTop);
            m.SetFloat("_Mode", 0f);
            return m;
        }

        /// <summary>A soft round glow for a Disc.</summary>
        public static Material Glow(Color c, float intensity, float edge = 2f)
        {
            var m = Column(c, intensity, edge);
            if (Real) m.SetFloat("_Mode", 1f);
            return m;
        }

        /// <summary>Change a beam material's colour and brightness (0 = off).</summary>
        public static void Set(Material m, Color c, float intensity)
        {
            if (m == null) return;
            if (m.HasProperty(k_Intensity))
            {
                m.SetColor(k_Color, c);
                m.SetFloat(k_Intensity, intensity);
            }
            else
            {
                var g = c;
                g.a = Mathf.Clamp01(0.15f * intensity);
                m.SetColor(k_Base, g);
            }
        }

        /// <summary>Its brightness right now.</summary>
        public static float Intensity(Material m) => m == null ? 0f : m.HasProperty(k_Intensity) ? m.GetFloat(k_Intensity) : m.GetColor(k_Base).a / 0.15f;

        static GameObject Part(Transform parent, Mesh mesh, Material m, string name)
        {
            var go = Art.Part(parent, mesh, Color.white, Vector3.zero, Vector3.one, default, false, m, name);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>A column (the built-in cylinder: 1 m across and 2 m tall at scale 1, centred).</summary>
        public static GameObject Cylinder(Transform parent, Material m, string name = "beam") => Part(parent, Art.Cylinder, m, name);

        /// <summary>A flat round glow lying on the ground (1 m across at scale 1).</summary>
        public static GameObject Disc(Transform parent, Material m, string name = "glow")
        {
            if (s_Quad == null) s_Quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var go = Part(parent, s_Quad, m, name);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return go;
        }

        /// <summary>A light shaft: an open cone, 0 across at y = 0 widening to 1 across at y = 1 (point it with its
        /// transform: the narrow end at the lamp). Its material wants _YRange (0, 1).</summary>
        public static GameObject Shaft(Transform parent, Material m, string name = "light shaft")
        {
            if (s_Shaft == null) s_Shaft = MakeShaft(24);
            if (m != null && m.HasProperty("_YRange")) m.SetVector("_YRange", new Vector4(0f, 1f, 0f, 0f));
            return Part(parent, s_Shaft, m, name);
        }

        /// <summary>The open cone for Shaft, smooth-sided (so it fades to soft edges), with a little width at its tip.</summary>
        static Mesh MakeShaft(int sides)
        {
            var v = new Vector3[(sides + 1) * 2];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var t = new int[sides * 6];
            const float tip = 0.06f;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d * tip * 0.5f;
                v[i * 2 + 1] = d * 0.5f + Vector3.up;
                var nn = (d - Vector3.up * (0.5f - tip * 0.5f)).normalized;
                n[i * 2] = nn;
                n[i * 2 + 1] = nn;
                uv[i * 2] = new Vector2((float)i / sides, 0f);
                uv[i * 2 + 1] = new Vector2((float)i / sides, 1f);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                t[i * 6] = a; t[i * 6 + 1] = b; t[i * 6 + 2] = c;
                t[i * 6 + 3] = c; t[i * 6 + 4] = b; t[i * 6 + 5] = d;
            }
            var mesh = new Mesh { name = "light shaft" };
            mesh.vertices = v;
            mesh.normals = n;
            mesh.uv = uv;
            mesh.triangles = t;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
