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

        /// <summary>A waving cloth flag in the team colour, from `pole` (its top inner corner) out along +x. Null if no shader.</summary>
        public static Renderer Flag(Transform parent, Vector3 pole, Color team, float w, float h)
        {
            var mat = WorldLook.Flag;
            if (mat == null) return null;
            var kit = new MeshKit();
            const int nx = 12, ny = 5;
            var stripe = Color.Lerp(team, Color.white, 0.75f);
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                float x0 = w * i / nx, x1 = w * (i + 1) / nx, y0 = -h * j / ny, y1 = -h * (j + 1) / ny;
                // a pale stripe across the middle
                var col = j == ny / 2 ? stripe : team;
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
    /// no shadows). Hidden in PSX / AI PSX and in the sudden death arena in space.</summary>
    public class CloudLayer : MonoBehaviour
    {
        readonly List<Transform> m_Clouds = new List<Transform>();
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
            m_Extent = Cfg.MapHalf * 2.4f + 60f;
            for (int v = 0; v < 6; v++) m_Meshes.Add(CloudMesh(rng));
            int n = Mathf.RoundToInt(16 * Mathf.Max(1f, Cfg.MapHalf / 100f));
            for (int i = 0; i < n; i++)
            {
                var c = new GameObject("cloud");
                c.transform.SetParent(transform, false);
                c.transform.localPosition = new Vector3(R(-m_Extent, m_Extent), R(80f, 120f), R(-m_Extent, m_Extent));
                c.transform.localRotation = Quaternion.Euler(0, R(-25f, 25f), 0);
                c.transform.localScale = Vector3.one * R(0.8f, 1.7f);
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
            return kit.ToMesh("cloud");
        }

        static readonly Vector3[] k_IcoV;
        static readonly int[] k_IcoF;

        static CloudLayer()
        {
            // icosphere, subdivided once (80 faces): round enough to read as a puff, still chunky low-poly
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
            var mids = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (mids.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                return mids[key] = v.Count - 1;
            }
            var faces = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                faces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            k_IcoV = v.ToArray();
            k_IcoF = faces.ToArray();
        }

        static void Puff(MeshKit kit, Vector3 c, float r, float squash, System.Random rng, Color top, Color bottom)
        {
            var pts = new Vector3[k_IcoV.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var p = k_IcoV[i] * r * (1f + ((float)rng.NextDouble() - 0.5f) * 0.12f);
                p.y *= 0.72f * squash;
                p += c;
                p.y = Mathf.Max(p.y, 0f); // flat bottom
                pts[i] = p;
            }
            Color Col(Vector3 p) => Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.y / 7f)));
            for (int i = 0; i < k_IcoF.Length; i += 3)
            {
                Vector3 a = pts[k_IcoF[i]], b = pts[k_IcoF[i + 1]], d = pts[k_IcoF[i + 2]];
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
            foreach (var c in m_Clouds)
            {
                var p = c.localPosition + step;
                if (p.x > m_Extent) p.x -= 2f * m_Extent;
                if (p.z > m_Extent) p.z -= 2f * m_Extent;
                c.localPosition = p;
            }
        }
    }
}
