using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Little butterflies fluttering about the map (decoration only: every peer's own, nothing networked, no colliders, no
    /// shadows). Each one has a home spot out in the wild (from the map's seed: not in a base, not in the crash in the
    /// middle) and wanders round it in lazy loops a metre or two over the ground, bobbing as it flaps, turning the way
    /// it flies. Simple shapes, shaded smooth: a small rounded body and two wings (a fore and a hind lobe each, one
    /// normal over the whole wing, so the light runs evenly over it) in one of a few bright colours. Only the ones near
    /// the camera are switched on and moved (ShowDist), so a map full of them costs next to nothing.
    /// </summary>
    public class Butterflies : MonoBehaviour
    {
        class Fly
        {
            public Transform T, WingL, WingR;
            public Vector3 Home, Last;
            public float Radius, Height, Speed, Flap, P1, P2, Yaw;
            public bool Shown;
        }

        /// <summary>How many there are on a 200 m map (more on a bigger one), how near the camera they're drawn (m), how
        /// big one is (its wings about 0.26 m across).</summary>
        public const int PerMap = 150;
        public const float ShowDist = 55f, Size = 1f;

        public static Butterflies Current { get; private set; }
        readonly List<Fly> m_Flies = new List<Fly>();

        public int Count => m_Flies.Count;
        /// <summary>(tests) How many are switched on (near the camera) right now.</summary>
        public int ShownCount { get; private set; }
        /// <summary>(tests) Butterfly i: where it is, how far up its right wing is (degrees), whether it's switched on.</summary>
        public bool Get(int i, out Vector3 pos, out float wing)
        {
            pos = default; wing = 0f;
            if (i < 0 || i >= m_Flies.Count) return false;
            var f = m_Flies[i];
            pos = f.T.position;
            wing = Mathf.DeltaAngle(0f, f.WingR.localEulerAngles.z);
            return f.Shown;
        }
        /// <summary>(tests) Butterfly i's home spot.</summary>
        public Vector3 HomeOf(int i) => m_Flies[i].Home;

        public static Butterflies Build(Transform root)
        {
            var go = new GameObject("Butterflies");
            go.transform.SetParent(root, false);
            var b = go.AddComponent<Butterflies>();
            b.Make();
            Current = b;
            return b;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Make()
        {
            if (s_WingR == null) BuildMeshes();
            var rng = new System.Random(Cfg.MapSeed * 13 + 71);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // (they stay on the map's own ground: well inside the glass, off the rocky rise at the edge of Highlands - it
            // starts 14 m in - and clear of the ring of mountain rocks, the nearest of which poke in over the edge)
            float half = Cfg.MapHalf - EdgeMargin;
            s_Edge = half;
            int want = Mathf.RoundToInt(PerMap * Mathf.Clamp(Cfg.MapHalf * Cfg.MapHalf / 10000f, 0.4f, 2.5f));
            for (int tries = 0; tries < want * 4 && m_Flies.Count < want; tries++)
            {
                float radius = R(2.5f, 6f);
                // (the whole loop inside the edge, not just its middle)
                var home = new Vector3(R(-half + radius, half - radius), 0f, R(-half + radius, half - radius));
                if (NearMountain(home, radius)) continue;
                // (out in the wild: their loops stay out of the bases and out of the burning crash in the middle)
                if (new Vector2(home.x, home.z).magnitude < 16f + radius) continue;
                bool inBase = false;
                for (int k = 0; k < 4 && !inBase; k++)
                    inBase = Cfg.BaseTeamAt(home + new Vector3(k < 2 ? radius * (k * 2 - 1) : 0f, 0f, k >= 2 ? radius * (k * 2 - 5) : 0f)) >= 0;
                if (inBase || Cfg.BaseTeamAt(home) >= 0) continue;
                var f = new Fly
                {
                    Home = home, Radius = radius, Height = R(0.7f, 2.2f), Speed = R(1.2f, 2.2f) / radius, Flap = R(15f, 21f),
                    P1 = R(0f, 100f), P2 = R(0f, 100f),
                };
                var t = new GameObject("butterfly").transform;
                t.SetParent(transform, false);
                t.localScale = Vector3.one * Size * R(0.85f, 1.15f);
                f.T = t;
                var wing = s_WingMats[rng.Next(s_WingMats.Length)];
                Part(t, Art.Sphere, s_BodyMat, "body").localScale = new Vector3(0.022f, 0.022f, 0.1f) / Mathf.Max(0.01f, Art.Sphere.bounds.extents.x * 2f);
                f.WingR = Part(t, s_WingR, wing, "wing R");
                f.WingL = Part(t, s_WingL, wing, "wing L");
                t.gameObject.SetActive(false);
                m_Flies.Add(f);
            }
        }

        /// <summary>How far in from the edge of the map their loops stay (m).</summary>
        public static float EdgeMargin => Cfg.IsHighlands ? 17f : 7f;
        /// <summary>They keep this far from the mountain rocks round the map (m).</summary>
        public const float MountainClear = 4f;
        static float s_Edge = 1e6f;

        /// <summary>Would a loop of this radius round `home` come near one of the mountain rocks round the map?</summary>
        static bool NearMountain(Vector3 home, float radius)
        {
            for (int k = 0; k < 9; k++)
            {
                float a = k * Mathf.PI / 4f, r = k == 8 ? 0f : radius;
                var p = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = MapBuilder.Height(p.x, p.z) + 1.5f;
                if (MapBuilder.InMountain(p, MountainClear)) return true;
            }
            return false;
        }

        static Transform Part(Transform parent, Mesh mesh, Material mat, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return g.transform;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var cp = cam.transform.position;
            float now = Time.time, dt = Time.deltaTime;
            int shown = 0;
            foreach (var f in m_Flies)
            {
                // (measured from its home, with a little slack, so one on the edge doesn't blink on and off)
                float d2 = (new Vector3(f.Home.x, cp.y, f.Home.z) - cp).sqrMagnitude, lim = f.Shown ? ShowDist + 4f : ShowDist;
                bool show = d2 < lim * lim && Mathf.Abs(cp.y - MapBuilder.Height(f.Home.x, f.Home.z)) < 60f;
                if (show != f.Shown)
                {
                    f.Shown = show;
                    f.T.gameObject.SetActive(show);
                    if (show) { f.Last = At(f, now - 0.05f); f.Yaw = float.NaN; }
                }
                if (!show) continue;
                shown++;
                var p = At(f, now);
                var v = p - f.Last;
                f.Last = p;
                // it turns the way it flies (smoothly), nose up a little as it climbs
                var flat = new Vector3(v.x, 0f, v.z);
                if (flat.sqrMagnitude > 1e-8f)
                {
                    float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    f.Yaw = float.IsNaN(f.Yaw) ? yaw : Mathf.LerpAngle(f.Yaw, yaw, 1f - Mathf.Exp(-dt * 8f));
                }
                float pitch = dt > 0f ? Mathf.Clamp(-v.y / dt * 12f, -25f, 25f) : 0f;
                f.T.SetPositionAndRotation(p, Quaternion.Euler(pitch, float.IsNaN(f.Yaw) ? 0f : f.Yaw, 0f));
                // the wings: up to nearly touching over its back, down to a little under level
                float ang = 32f + 48f * Mathf.Sin(now * f.Flap + f.P1);
                f.WingR.localRotation = Quaternion.Euler(0f, 0f, ang);
                f.WingL.localRotation = Quaternion.Euler(0f, 0f, -ang);
            }
            ShownCount = shown;
        }

        /// <summary>Where it is at time t: lazy loops round its home, a metre or two over the ground there, rising and
        /// sinking slowly and dipping a little with every beat of its wings.</summary>
        static Vector3 At(Fly f, float t)
        {
            float a = t * f.Speed;
            float x = f.Home.x + (Mathf.Sin(a + f.P1) * 0.62f + Mathf.Sin(a * 2.3f + f.P2) * 0.38f) * f.Radius;
            float z = f.Home.z + (Mathf.Cos(a * 0.83f + f.P2) * 0.62f + Mathf.Sin(a * 1.9f + f.P1) * 0.38f) * f.Radius;
            // (never out over the edge, whatever its loop does)
            x = Mathf.Clamp(x, -s_Edge, s_Edge);
            z = Mathf.Clamp(z, -s_Edge, s_Edge);
            float y = MapBuilder.Height(x, z) + f.Height + Mathf.Sin(t * 0.9f + f.P2) * 0.4f - Mathf.Sin(t * f.Flap + f.P1) * 0.035f;
            return new Vector3(x, y, z);
        }

        // ------------------------------------------------------------------ looks

        static Mesh s_WingR, s_WingL;
        static Material s_BodyMat;
        static Material[] s_WingMats;
        static readonly Color[] k_Wings =
        {
            new Color(1f, 0.62f, 0.12f), new Color(1f, 0.9f, 0.3f), new Color(0.96f, 0.96f, 0.98f),
            new Color(0.35f, 0.62f, 1f), new Color(1f, 0.5f, 0.75f), new Color(0.7f, 0.45f, 0.95f),
        };

        static void BuildMeshes()
        {
            s_BodyMat = Art.NewMat(new Color(0.16f, 0.13f, 0.12f));
            s_BodyMat.name = "butterfly body";
            s_WingMats = new Material[k_Wings.Length];
            for (int i = 0; i < k_Wings.Length; i++)
            {
                s_WingMats[i] = Art.NewMat(k_Wings[i]);
                s_WingMats[i].name = "butterfly wing";
                if (s_WingMats[i].HasProperty("_Smoothness")) s_WingMats[i].SetFloat("_Smoothness", 0f);
            }
            s_WingR = Wing(1f);
            s_WingL = Wing(-1f);
        }

        /// <summary>One wing, out along +x (side 1) or -x (side -1) from the body: a rounded fore wing and a smaller hind
        /// wing, flat, both faces - every corner of a face has the same normal, so it's shaded smooth.</summary>
        static Mesh Wing(float side)
        {
            // the outline, from the front of the wing root round its edge to the back of the root
            var o = new[]
            {
                new Vector2(0f, 0.035f), new Vector2(0.05f, 0.085f), new Vector2(0.105f, 0.09f), new Vector2(0.13f, 0.055f), new Vector2(0.115f, 0.01f),
                new Vector2(0.08f, -0.012f), new Vector2(0.1f, -0.05f), new Vector2(0.075f, -0.09f), new Vector2(0.03f, -0.085f), new Vector2(0f, -0.04f),
            };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            foreach (float face in new[] { 1f, -1f })
            {
                int k0 = v.Count;
                v.Add(new Vector3(side * 0.012f, 0f, 0f)); n.Add(Vector3.up * face);
                foreach (var p in o) { v.Add(new Vector3(side * (p.x + 0.012f), 0f, p.y)); n.Add(Vector3.up * face); }
                for (int i = 0; i + 1 < o.Length; i++)
                {
                    int a = k0, b = k0 + 1 + i, c = k0 + 2 + i;
                    // (wound so the face looks along its normal)
                    if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), Vector3.up * face) < 0f) (b, c) = (c, b);
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
            }
            var m = new Mesh { name = side > 0f ? "butterfly wing R" : "butterfly wing L" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
