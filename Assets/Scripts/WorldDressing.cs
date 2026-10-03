using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Normal graphics dressing for Plains and Highlands: the bases' waving team flags and drifting low-poly clouds
    /// (the sun is SkySun). Merged vertex-coloured meshes drawn with RockGame/Painted. (The castle walls that used to
    /// stand round the map are gone: the plain boundary walls are what you see again.)
    /// </summary>
    public static class WorldDressing
    {
        // =====================================================================
        // Flags
        // =====================================================================

        /// <summary>A waving cloth flag, all in the team colour, from `pole` (its top inner corner) out along +x. Null if no shader.</summary>
        public static Renderer Flag(Transform parent, Vector3 pole, Color team, float w, float h)
        {
            var mat = WorldLook.Flag;
            if (mat == null) return null;
            var kit = new MeshKit();
            const int nx = 12, ny = 5;
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                float x0 = w * i / nx, x1 = w * (i + 1) / nx, y0 = -h * j / ny, y1 = -h * (j + 1) / ny;
                // all in the team colour (no stripe)
                var col = team;
                Vector2 U(float x) => new Vector2(x / w, 0);
                var a = new Vector3(x0, y0, 0); var b = new Vector3(x1, y0, 0); var c = new Vector3(x1, y1, 0); var d = new Vector3(x0, y1, 0);
                kit.Tri(a, b, c, col, col, col, U(x0), U(x1), U(x1));
                kit.Tri(a, c, d, col, col, col, U(x0), U(x1), U(x0));
            }
            var go = kit.Spawn(parent, "flag", mat);
            go.transform.position = pole;
            return go.GetComponent<Renderer>();
        }
    }

    /// <summary>Normal graphics: puffy low-poly clouds drifting slowly high over the map (a handful of merged meshes,
    /// smooth-shaded, no shadows). Hidden in PSX / AI PSX and in the sudden death arena in space.
    /// They drift over a square far bigger than the map and wrap round at its edge - but never pop: a cloud shrinks
    /// away to nothing as it nears the far edge (far out over the mountains, low on the horizon) and grows back out of
    /// nothing on the other side, so it's already gone when it jumps.</summary>
    public class CloudLayer : MonoBehaviour
    {
        readonly List<Transform> m_Clouds = new List<Transform>();
        readonly List<float> m_Scale = new List<float>();
        readonly List<Renderer> m_Rs = new List<Renderer>();
        readonly List<Mesh> m_Meshes = new List<Mesh>();
        float m_Extent;
        bool m_Shown = true;
        static readonly Vector3 k_Wind = new Vector3(1.6f, 0, 0.6f);

        public static CloudLayer Build(Transform root)
        {
            var mat = WorldLook.Cloud;
            if (mat == null) return null;
            var go = new GameObject("Clouds");
            go.transform.SetParent(root, false);
            var cl = go.AddComponent<CloudLayer>();
            cl.Make(mat);
            return cl;
        }

        public int Count => m_Clouds.Count;
        public bool Shown => m_Shown;

        void Make(Material mat)
        {
            var rng = new System.Random(Cfg.MapSeed * 5 + 3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            m_Extent = Cfg.MapHalf * 3.2f + 120f;
            for (int v = 0; v < 6; v++) m_Meshes.Add(CloudMesh(rng));
            int n = Mathf.RoundToInt(26 * Mathf.Max(1f, Cfg.MapHalf / 100f));
            for (int i = 0; i < n; i++)
            {
                var c = new GameObject("cloud");
                c.transform.SetParent(transform, false);
                c.transform.localPosition = new Vector3(R(-m_Extent, m_Extent), R(105f, 160f), R(-m_Extent, m_Extent));
                c.transform.localRotation = Quaternion.Euler(0, R(-25f, 25f), 0);
                float sc = R(2.2f, 3.8f); // (big: 2-3x what they were)
                m_Scale.Add(sc);
                c.transform.localScale = Vector3.one * sc * Fade(c.transform.localPosition);
                c.AddComponent<MeshFilter>().sharedMesh = m_Meshes[i % m_Meshes.Count];
                var mr = c.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                m_Clouds.Add(c.transform);
                m_Rs.Add(mr);
            }
        }

        void OnDestroy() { foreach (var m in m_Meshes) if (m) Destroy(m); }

        /// <summary>One cloud: overlapping puffs (bigger in the middle), flat underneath, white on top shading to grey-blue below.</summary>
        static Mesh CloudMesh(System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var kit = new MeshKit();
            int puffs = rng.Next(5, 9);
            float len = R(26f, 42f);
            var top = new Color(1f, 1f, 1f);
            var bottom = new Color(0.74f, 0.77f, 0.86f);
            for (int p = 0; p < puffs; p++)
            {
                float t = puffs == 1 ? 0.5f : p / (puffs - 1f);
                float big = Mathf.Sin(t * Mathf.PI);
                float r = Mathf.Lerp(4.5f, 9.5f, big) * R(0.85f, 1.15f);
                var c = new Vector3((t - 0.5f) * len, r * 0.35f + R(-0.5f, 1.2f), R(-4f, 4f) * (1f - big * 0.5f));
                Puff(kit, c, r, R(0.85f, 1.15f), rng, top, bottom);
            }
            kit.SmoothNormals(65f); // (smooth-shaded puffs; the flat bottom keeps its edge)
            return kit.ToMesh("cloud");
        }

        static void Puff(MeshKit kit, Vector3 c, float r, float squash, System.Random rng, Color top, Color bottom)
        {
            MeshKit.Ico320(out var icoV, out var icoF);
            var pts = new Vector3[icoV.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var p = icoV[i] * r * (1f + ((float)rng.NextDouble() - 0.5f) * 0.05f);
                p.y *= 0.72f * squash;
                p += c;
                p.y = Mathf.Max(p.y, 0f); // flat bottom
                pts[i] = p;
            }
            Color Col(Vector3 p) => Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.y / 7f)));
            for (int i = 0; i < icoF.Length; i += 3)
            {
                Vector3 a = pts[icoF[i]], b = pts[icoF[i + 1]], d = pts[icoF[i + 2]];
                if (a.y <= 0.001f && b.y <= 0.001f && d.y <= 0.001f && Vector3.Cross(b - a, d - a).y < 0f) { kit.Tri(a, b, d, bottom * 0.95f); continue; }
                kit.Tri(a, b, d, Col(a), Col(b), Col(d));
            }
        }

        void Update()
        {
            var cam = Camera.main;
            bool show = GameSettings.GraphicsMode == 0 && (cam == null || !SpaceArena.NearArena(cam.transform.position));
            if (show != m_Shown)
            {
                m_Shown = show;
                foreach (var r in m_Rs) if (r) r.enabled = show;
            }
            if (!show) return;
            var step = k_Wind * Time.deltaTime;
            for (int i = 0; i < m_Clouds.Count; i++)
            {
                var c = m_Clouds[i];
                var p = c.localPosition + step;
                if (p.x > m_Extent) p.x -= 2f * m_Extent;
                if (p.z > m_Extent) p.z -= 2f * m_Extent;
                c.localPosition = p;
                c.localScale = Vector3.one * Mathf.Max(0.0001f, m_Scale[i] * Fade(p));
            }
        }

        /// <summary>How big a cloud is here, 0-1: full size over most of the square, shrinking smoothly to nothing over the
        /// last stretch before its edge (where it wraps round to the other side), so it never pops in or out.</summary>
        public float Fade(Vector3 p)
        {
            float band = m_Extent * 0.22f;
            float e = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
            float k = Mathf.Clamp01((m_Extent - e) / band);
            return k * k * (3f - 2f * k);
        }

        /// <summary>The clouds (tests).</summary>
        public IReadOnlyList<Transform> Clouds => m_Clouds;
        public float Extent => m_Extent;
    }
}
