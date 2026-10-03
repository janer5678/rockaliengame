using System.Collections.Generic;
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

    /// <summary>
    /// Highlands, Normal graphics: a few big planets hanging in the sky - a banded gas giant with a ring, a blue-green
    /// world and a small pale moon - smooth-shaded (a fine sphere with round normals, vertex-coloured, lit by the sun
    /// through RockGame/Painted with a little glow so the night side isn't black). Like the sun they're sky, not things
    /// in the world: each sits 1150-1250 m out along its own fixed direction from the camera every frame (wholly inside
    /// the far plane, ring and all: it's pushed out to FarPlane while they're up), high above the mountains and away from the sun, so nothing in the world - the airdrop ships coming
    /// in, hovering and leaving (all within ~600 m of anyone), the clouds, the dome - ever reaches them or passes behind
    /// them, and they have no colliders. Hidden in PSX / AI PSX and in the sudden death arena in space.
    /// </summary>
    public class SkyPlanets : MonoBehaviour
    {
        struct Planet { public Transform T; public Vector3 Dir; public float Dist, Spin, Radius, Reach; }
        /// <summary>The camera's far plane while the planets are up (m): far enough for the whole gas giant and its ring
        /// (1250 m out, the ring reaching 430 m round it) - at the old 1500 m the far side of the ring was cut off. Nothing
        /// else in the world is out past 1500 m, so this costs nothing.</summary>
        public const float FarPlane = 1750f;
        /// <summary>(tests) The furthest any part of a planet (its ring too) is from the camera, and the far plane then.</summary>
        public float FurthestReach { get; private set; }
        public float FarNow { get; private set; }
        float m_OldFar = -1f;
        Camera m_FarCam;
        readonly List<Planet> m_Planets = new List<Planet>();
        readonly List<Renderer> m_Rs = new List<Renderer>();
        readonly List<Object> m_Owned = new List<Object>();
        bool m_Shown = true;
        public static SkyPlanets Current { get; private set; }
        public bool Shown => m_Shown;
        public int Count => m_Planets.Count;
        /// <summary>The closest any part of a planet (the ring too) ever comes to the camera, m (tests).</summary>
        public float NearestReach { get; private set; } = float.MaxValue;
        public IReadOnlyList<Renderer> Renderers => m_Rs;
        /// <summary>Which way each planet is in the sky (tests).</summary>
        public Vector3 DirOf(int i) => m_Planets[i].Dir;

        public static SkyPlanets Build(Transform root)
        {
            var sh = Resources.Load<Shader>("World/Painted");
            if (sh == null || !sh.isSupported) { Debug.LogWarning("[RockGame] Painted shader missing: no planets"); return null; }
            var go = new GameObject("Planets (sky)");
            go.transform.SetParent(root, false);
            var s = go.AddComponent<SkyPlanets>();
            s.Make(sh);
            Current = s;
            return s;
        }

        void OnDestroy()
        {
            foreach (var o in m_Owned) if (o) Destroy(o);
            if (Current == this) Current = null;
        }

        Material Mat(Shader sh, string name, float glow, bool twoSided)
        {
            var m = new Material(sh) { name = name };
            m.SetFloat("_Wind", 0f);
            m.SetFloat("_Glow", glow);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.SetColor("_Tint", Color.white);
            m_Owned.Add(m);
            return m;
        }

        void Make(Shader sh)
        {
            var rng = new System.Random(Cfg.MapSeed * 3 + 11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var body = Mat(sh, "planet", 0.32f, false);
            var ringMat = Mat(sh, "planet ring", 0.5f, true);
            // the sun's way round the sky: the planets keep well away from it
            var sun = SkySun.Direction;
            float sunAz = Mathf.Atan2(sun.z, sun.x) * Mathf.Rad2Deg;
            float baseAz = sunAz + 180f + R(-30f, 30f);
            // (azimuth from the side away from the sun, elevation, angular radius, distance)
            var specs = new[]
            {
                (az: baseAz - 25f, el: 31f, ang: 8.5f, dist: 1250f, kind: 0),
                (az: baseAz + 62f, el: 44f, ang: 4.2f, dist: 1200f, kind: 1),
                (az: baseAz + 18f, el: 23f, ang: 2.2f, dist: 1150f, kind: 2),
            };
            foreach (var p in specs)
            {
                float az = p.az * Mathf.Deg2Rad, el = p.el * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
                // (and never right by the sun)
                if (Vector3.Angle(dir, sun) < 40f) dir = Quaternion.AngleAxis(60f, Vector3.up) * dir;
                float r = p.dist * Mathf.Tan(p.ang * Mathf.Deg2Rad);
                var go = new GameObject(p.kind == 0 ? "gas giant" : p.kind == 1 ? "blue planet" : "moon");
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * r;
                var mesh = Sphere(p.kind, rng);
                m_Owned.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = body;
                Quiet(mr);
                float reach = p.dist - r;
                if (p.kind == 0)
                {
                    // a ring round the gas giant, tilted
                    var ring = new GameObject("ring");
                    ring.transform.SetParent(go.transform, false);
                    ring.transform.localRotation = Quaternion.Euler(R(14f, 24f), 0, R(-12f, 12f));
                    var rm = Ring();
                    m_Owned.Add(rm);
                    ring.AddComponent<MeshFilter>().sharedMesh = rm;
                    var rr = ring.AddComponent<MeshRenderer>();
                    rr.sharedMaterial = ringMat;
                    Quiet(rr);
                    reach = p.dist - r * 2.3f;
                }
                go.transform.localRotation = Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-25f, 25f));
                NearestReach = Mathf.Min(NearestReach, reach);
                m_Planets.Add(new Planet { T = go.transform, Dir = dir.normalized, Dist = p.dist, Spin = p.kind == 2 ? 0f : R(0.4f, 1.2f), Radius = r, Reach = p.dist - reach });
            }
        }

        void Quiet(MeshRenderer mr)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            m_Rs.Add(mr);
        }

        /// <summary>A round sphere (radius 1): rings of corners shared by the faces round them, each with its normal
        /// straight out from the middle - smooth-shaded. Coloured in bands by latitude (the gas giant), seas and land (the
        /// blue one) or pale with darker seas (the moon).</summary>
        static Mesh Sphere(int kind, System.Random rng)
        {
            const int lat = 28, lon = 48;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            float o = (float)rng.NextDouble() * 100f;
            for (int i = 0; i <= lat; i++)
            {
                float a = Mathf.PI * i / lat - Mathf.PI * 0.5f;
                for (int j = 0; j <= lon; j++)
                {
                    float b = Mathf.PI * 2f * j / lon;
                    var p = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b));
                    v.Add(p);
                    c.Add(PlanetColour(kind, p, o).linear);
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a0 = i * (lon + 1) + j, a1 = a0 + 1, b0 = a0 + lon + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1);
                    t.Add(a1); t.Add(b0); t.Add(b1);
                }
            var m = new Mesh { name = "planet" };
            m.SetVertices(v);
            m.SetNormals(v); // (round: straight out from the middle)
            m.SetColors(c);
            m.SetUVs(0, new List<Vector2>(new Vector2[v.Count]));
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static Color PlanetColour(int kind, Vector3 p, float o)
        {
            float wob = Mathf.PerlinNoise(p.x * 2.2f + o, p.z * 2.2f + o) - 0.5f;
            if (kind == 0)
            {
                // a gas giant: soft bands of cream, tan and rust, wobbling a little
                float band = Mathf.Sin(p.y * 13f + wob * 2.4f) * 0.5f + 0.5f, band2 = Mathf.Sin(p.y * 5f + 1.3f + wob) * 0.5f + 0.5f;
                var cream = new Color(0.93f, 0.85f, 0.68f); var tan = new Color(0.8f, 0.6f, 0.4f); var rust = new Color(0.66f, 0.4f, 0.28f);
                return Color.Lerp(Color.Lerp(cream, tan, band), rust, band2 * 0.45f);
            }
            if (kind == 1)
            {
                // seas and land, white at the poles
                float land = Mathf.PerlinNoise(p.x * 1.6f + o + p.y, p.z * 1.6f - p.y * 1.3f + o);
                var sea = new Color(0.26f, 0.5f, 0.78f); var shore = new Color(0.36f, 0.66f, 0.62f); var green = new Color(0.42f, 0.62f, 0.38f);
                var col = land > 0.56f ? Color.Lerp(shore, green, Mathf.InverseLerp(0.56f, 0.7f, land)) : sea;
                return Color.Lerp(col, new Color(0.94f, 0.96f, 1f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.9f, Mathf.Abs(p.y))));
            }
            // a small pale moon with darker seas
            float mare = Mathf.PerlinNoise(p.x * 2.6f + o, p.y * 2.6f + p.z * 1.7f + o);
            return Color.Lerp(new Color(0.86f, 0.84f, 0.9f), new Color(0.62f, 0.6f, 0.68f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.68f, mare)));
        }

        /// <summary>A flat ring round the gas giant (1.35 to 2.3 planet radii), in a few soft bands.</summary>
        static Mesh Ring()
        {
            const int n = 72;
            float[] radii = { 1.35f, 1.6f, 1.75f, 2.05f, 2.3f };
            Color[] cols = { new Color(0.78f, 0.7f, 0.58f), new Color(0.9f, 0.83f, 0.7f), new Color(0.7f, 0.6f, 0.5f), new Color(0.86f, 0.8f, 0.68f) };
            var v = new List<Vector3>();
            var nr = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            for (int b = 0; b < cols.Length; b++)
                for (int i = 0; i < n; i++)
                {
                    float a0 = Mathf.PI * 2f * i / n, a1 = Mathf.PI * 2f * (i + 1) / n;
                    int k = v.Count;
                    v.Add(new Vector3(Mathf.Cos(a0) * radii[b], 0, Mathf.Sin(a0) * radii[b]));
                    v.Add(new Vector3(Mathf.Cos(a1) * radii[b], 0, Mathf.Sin(a1) * radii[b]));
                    v.Add(new Vector3(Mathf.Cos(a1) * radii[b + 1], 0, Mathf.Sin(a1) * radii[b + 1]));
                    v.Add(new Vector3(Mathf.Cos(a0) * radii[b + 1], 0, Mathf.Sin(a0) * radii[b + 1]));
                    for (int q = 0; q < 4; q++) { nr.Add(Vector3.up); c.Add(cols[b].linear); }
                    t.Add(k); t.Add(k + 2); t.Add(k + 1);
                    t.Add(k); t.Add(k + 3); t.Add(k + 2);
                }
            var m = new Mesh { name = "planet ring" };
            m.SetVertices(v);
            m.SetNormals(nr);
            m.SetColors(c);
            m.SetUVs(0, new List<Vector2>(new Vector2[v.Count]));
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            bool show = cam != null && GameSettings.GraphicsMode == 0 && !SpaceArena.NearArena(cam.transform.position);
            if (show != m_Shown)
            {
                m_Shown = show;
                foreach (var r in m_Rs) if (r) r.enabled = show;
            }
            if (!show) return;
            // the whole planet and its ring inside the far plane (it used to cut the ring off): push the far plane out to
            // FarPlane, and if a camera's is still too near, bring the planet in and shrink it to match - the same size in
            // the sky either way
            if (cam.farClipPlane < FarPlane)
            {
                if (m_FarCam != cam) { m_FarCam = cam; m_OldFar = cam.farClipPlane; }
                cam.farClipPlane = FarPlane;
            }
            float fit = cam.farClipPlane * 0.97f;
            FarNow = cam.farClipPlane;
            float furthest = 0f, nearest = float.MaxValue;
            foreach (var p in m_Planets)
            {
                // (fixed in the sky like the sun: always the same way from wherever you are, never any nearer)
                float k = Mathf.Min(1f, fit / (p.Dist + p.Reach));
                p.T.position = cam.transform.position + p.Dir * (p.Dist * k);
                p.T.localScale = Vector3.one * (p.Radius * k);
                furthest = Mathf.Max(furthest, (p.Dist + p.Reach) * k);
                nearest = Mathf.Min(nearest, (p.Dist - p.Reach) * k);
                if (p.Spin > 0f) p.T.Rotate(0f, p.Spin * Time.deltaTime, 0f, Space.Self);
            }
            FurthestReach = furthest;
            NearestReach = nearest;
        }

        void OnDisable()
        {
            // (the far plane back as it was once the planets are gone)
            if (m_FarCam != null && m_OldFar > 0f && Mathf.Approximately(m_FarCam.farClipPlane, FarPlane)) m_FarCam.farClipPlane = m_OldFar;
            m_FarCam = null;
        }
    }
}
