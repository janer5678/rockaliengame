using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Procedural low-poly stand-in art: colored primitives, cones, faceted rocks.</summary>
    public static class Art
    {
        static readonly Dictionary<Color, Material> s_Mats = new Dictionary<Color, Material>();
        /// <summary>Every colour material Mat() made, with the exact colour it was made with (reading a colour back from a material isn't exact).</summary>
        static readonly Dictionary<Material, Color> s_MatColors = new Dictionary<Material, Color>();
        static readonly Dictionary<Color, Material> s_Ghosts = new Dictionary<Color, Material>();
        static Mesh s_Cube, s_Cyl, s_Sphere, s_Capsule, s_Cone, s_Ico;

        public static readonly Color Wood = new Color(0.55f, 0.37f, 0.2f);
        public static readonly Color DarkWood = new Color(0.36f, 0.23f, 0.12f);
        public static readonly Color Stone = new Color(0.56f, 0.56f, 0.6f);
        public static readonly Color Leaves = new Color(0.2f, 0.45f, 0.18f);
        public static readonly Color Metal = new Color(0.35f, 0.36f, 0.4f);

        public static Mesh Cube => s_Cube ? s_Cube : (s_Cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
        public static Mesh Cylinder => s_Cyl ? s_Cyl : (s_Cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"));
        public static Mesh Sphere => s_Sphere ? s_Sphere : (s_Sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"));
        public static Mesh Capsule => s_Capsule ? s_Capsule : (s_Capsule = Resources.GetBuiltinResource<Mesh>("Capsule.fbx"));
        public static Mesh Cone => s_Cone ? s_Cone : (s_Cone = MakeCone(7));
        public static Mesh Ico => s_Ico ? s_Ico : (s_Ico = MakeRock(0, 0f));

        public static Material Mat(Color c)
        {
            if (s_Mats.TryGetValue(c, out var m) && m) return m;
            var src = Bootstrap.I != null ? Bootstrap.I.baseMaterial : null;
            m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            s_Mats[c] = m;
            s_MatColors[m] = c;
            return m;
        }

        /// <summary>Count another material as the plain colour c (so the AI PSX mode re-skins it like one).</summary>
        public static void Register(Material m, Color c) { if (m != null) s_MatColors[m] = c; }

        /// <summary>A plain colour material made by Mat() (the AI PSX mode re-skins these).</summary>
        public static bool IsArtMat(Material m, out Color c)
        {
            c = default;
            return m != null && s_MatColors.TryGetValue(m, out c);
        }

        public static Material Ghost(Color c)
        {
            if (s_Ghosts.TryGetValue(c, out var m) && m) return m;
            var src = Bootstrap.I != null ? Bootstrap.I.ghostMaterial : null;
            m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.color = c;
            s_Ghosts[c] = m;
            return m;
        }

        /// <summary>Create a child mesh object. localEuler in degrees.</summary>
        public static GameObject Part(Transform parent, Mesh mesh, Color color, Vector3 localPos, Vector3 localScale,
            Vector3 localEuler = default, bool collider = false, Material overrideMat = null, string name = "part")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = overrideMat != null ? overrideMat : Mat(color);
            if (collider)
            {
                if (mesh == Cube) go.AddComponent<BoxCollider>();
                else if (mesh == Sphere || mesh == Ico) go.AddComponent<SphereCollider>().radius = mesh == Ico ? 0.95f : 0.5f;
                else if (mesh == Cylinder || mesh == Capsule) go.AddComponent<CapsuleCollider>();
                else { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true; }
            }
            return go;
        }

        public static GameObject Box(Transform parent, Color color, Vector3 pos, Vector3 scale, Vector3 euler = default, bool collider = false, Material overrideMat = null)
            => Part(parent, Cube, color, pos, scale, euler, collider, overrideMat, "box");

        public static void SetLayerShadowsOnly(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        static Mesh MakeCone(int sides)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 apex = new Vector3(0, 1, 0);
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2 / sides, a1 = (s + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                int b = verts.Count;
                verts.Add(p0); verts.Add(apex); verts.Add(p1);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                b = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(Vector3.zero);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            var m = new Mesh { name = "Cone" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat-shaded icosahedron with optional random jitter -> low poly rock/ball.</summary>
        public static Mesh MakeRock(int seed, float jitter)
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
            var rng = new System.Random(seed);
            for (int i = 0; i < v.Count; i++)
            {
                float k = 1f + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                v[i] = v[i].normalized * k;
            }
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < f.Length; i++) { verts.Add(v[f[i]]); tris.Add(i); }
            var m = new Mesh { name = "Rock" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
