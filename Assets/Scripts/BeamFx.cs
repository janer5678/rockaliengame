using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Glowing additive light (World/Beam.shader): the airdrop ship's beam and the glow under it, the ball's beacon, the
    /// landed airdrop crates' beacons, the victory UFO's beam, the arena's light shafts and the explosions' fireballs.
    /// Beams (AsBeam) are brightest from far away and fade down as you come up to them (Settings > Display > BEAMS). Columns are the built-in cylinder (object y -1..1) or a smooth
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

        static readonly int k_Strength = Shader.PropertyToID("_BeamStrength"), k_Falloff = Shader.PropertyToID("_BeamFalloff"), k_NearK = Shader.PropertyToID("_BeamNearK");
        static readonly int[] k_Clear = { Shader.PropertyToID("_Clear0"), Shader.PropertyToID("_Clear1"), Shader.PropertyToID("_Clear2"), Shader.PropertyToID("_Clear3") };
        static bool s_Hooked;

        /// <summary>
        /// How bright a beam is right next to it, as a fraction of its full (far away) brightness: about what the landed
        /// airdrop crate's beacon used to be (a faint see-through purple column) next to the ball's bright beacon.
        /// </summary>
        public const float NearK = 0.035f;

        /// <summary>The beam settings (Settings > Display > BEAMS) into the shader's globals. Done before any beam is made,
        /// and again whenever they change.</summary>
        public static void ApplySettings()
        {
            if (!s_Hooked) { s_Hooked = true; GameSettings.BeamsChanged += ApplySettings; }
            Shader.SetGlobalFloat(k_Strength, GameSettings.BeamStrength);
            Shader.SetGlobalFloat(k_Falloff, GameSettings.BeamFalloff);
            Shader.SetGlobalFloat(k_NearK, NearK);
        }

        /// <summary>
        /// What a beam's material gives seen from `cam` (what the shader does), as a fraction of its own brightness: the
        /// strength setting times NearK right next to it, rising to 1 at the falloff distance. at: a point on its axis.
        /// </summary>
        public static float Seen(Vector3 at, Vector3 cam)
        {
            float d = new Vector2(cam.x - at.x, cam.z - at.z).magnitude;
            float f = GameSettings.BeamFalloff;
            float ramp = f > 0.01f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, Mathf.Max(2.5f, f), d)) : 1f;
            return Mathf.Lerp(NearK, 1f, ramp) * Mathf.Max(0f, GameSettings.BeamStrength);
        }

        /// <summary>Make a column material a beam: brightest from far away, fading as you come up to it (Settings > Display > BEAMS).</summary>
        public static Material AsBeam(Material m)
        {
            if (m != null && m.HasProperty("_DistFade")) m.SetFloat("_DistFade", 1f);
            return m;
        }

        /// <summary>Make a beam see-through round a point (slot 0-3: the crate coming down it, the winners going up it), so
        /// it shows inside the light; radius 0 clears the slot.</summary>
        public static void SetClear(Material m, int slot, Vector3 at, float radius)
        {
            if (m == null || slot < 0 || slot > 3 || !m.HasProperty(k_Clear[slot])) return;
            m.SetVector(k_Clear[slot], new Vector4(at.x, at.y, at.z, Mathf.Max(0f, radius)));
        }

        /// <summary>A glowing ball (an explosion's fireball): bright through its middle with soft edges, on any closed mesh.</summary>
        public static Material Ball(Color c, float intensity, float edge = 1.4f)
        {
            var m = Column(c, intensity, edge, 0f, 0f);
            if (Real) m.SetFloat("_Mode", 2f);
            return m;
        }

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
            ApplySettings();
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

    /// <summary>Settings > Display > BEAMS: how strong the beams are (the ball's beacon, the airdrop beams and the landed
    /// crates' beacons, the victory UFO's beam) and over how many metres they fade down as you come up to them. Saved in
    /// PlayerPrefs like the other display settings ("RockGame.BeamStrength", "RockGame.BeamFalloff"); changes apply live
    /// (BeamsChanged).</summary>
    public static partial class GameSettings
    {
        public const float BeamStrengthMin = 0.2f, BeamStrengthMax = 2f, BeamStrengthDefault = 1f;
        public const float BeamFalloffMin = 0f, BeamFalloffMax = 200f, BeamFalloffDefault = 70f;
        static bool s_BeamsLoaded;
        static float s_BeamStrength = BeamStrengthDefault, s_BeamFalloff = BeamFalloffDefault;

        /// <summary>Fired when the beam settings change.</summary>
        public static event System.Action BeamsChanged;

        static void LoadBeams()
        {
            if (s_BeamsLoaded) return;
            s_BeamsLoaded = true;
            s_BeamStrength = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.BeamStrength", BeamStrengthDefault), BeamStrengthMin, BeamStrengthMax);
            s_BeamFalloff = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.BeamFalloff", BeamFalloffDefault), BeamFalloffMin, BeamFalloffMax);
        }

        /// <summary>How bright the beams are from far away (1 = as made).</summary>
        public static float BeamStrength { get { LoadBeams(); return s_BeamStrength; } }
        /// <summary>Beams fade down as you come up to them, over this many metres (0 = they don't).</summary>
        public static float BeamFalloff { get { LoadBeams(); return s_BeamFalloff; } }

        public static void SetBeams(float strength, float falloff, bool save = true)
        {
            LoadBeams();
            strength = Mathf.Clamp(strength, BeamStrengthMin, BeamStrengthMax);
            falloff = Mathf.Clamp(falloff, BeamFalloffMin, BeamFalloffMax);
            if (Mathf.Approximately(strength, s_BeamStrength) && Mathf.Approximately(falloff, s_BeamFalloff)) return;
            s_BeamStrength = strength;
            s_BeamFalloff = falloff;
            if (save)
            {
                PlayerPrefs.SetFloat("RockGame.BeamStrength", strength);
                PlayerPrefs.SetFloat("RockGame.BeamFalloff", falloff);
                PlayerPrefs.Save();
            }
            BeamsChanged?.Invoke();
        }

        /// <summary>Both beam settings back to the defaults.</summary>
        public static void ResetBeams(bool save = true)
        {
            LoadBeams();
            s_BeamStrength = BeamStrengthDefault;
            s_BeamFalloff = BeamFalloffDefault;
            if (save) { PlayerPrefs.DeleteKey("RockGame.BeamStrength"); PlayerPrefs.DeleteKey("RockGame.BeamFalloff"); PlayerPrefs.Save(); }
            BeamsChanged?.Invoke();
        }
    }
}
