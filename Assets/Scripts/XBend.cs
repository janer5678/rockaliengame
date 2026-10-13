using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The tree X (its two glowing bars and its halo) bent round the trunk, so it lies on the bark from every side. It used
    /// to be a flat card touching the trunk only in its middle: from the side its ends and its glow stuck out past the
    /// trunk's edge into the air. Every part is bent onto a cylinder round the trunk's axis (radius: how far the X is from
    /// the axis, ResourceNode.PlaceMarker) - the bars are drawn with a box cut into many pieces along its length (BarMesh)
    /// so they can curve.
    /// </summary>
    public class XBend : MonoBehaviour
    {
        readonly Dictionary<MeshFilter, (Mesh src, Mesh bent)> m_Parts = new Dictionary<MeshFilter, (Mesh, Mesh)>();
        float m_Radius = -1f;

        /// <summary>The radius it's bent to now, in the marker's own units (-1: flat).</summary>
        public float Radius => m_Radius;

        static Mesh s_Bar;

        /// <summary>A unit box (like the built-in cube: -0.5..0.5) cut into 16 pieces along x, so it bends smoothly.</summary>
        public static Mesh BarMesh
        {
            get
            {
                if (s_Bar != null) return s_Bar;
                const int N = 16;
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var t = new List<int>();
                // the four long faces (front, back, top, bottom), each a strip of N quads
                void Strip(Vector3 a0, Vector3 a1, Vector3 normal)
                {
                    int b = v.Count;
                    for (int i = 0; i <= N; i++)
                    {
                        float x = -0.5f + i / (float)N;
                        v.Add(new Vector3(x, a0.y, a0.z)); n.Add(normal);
                        v.Add(new Vector3(x, a1.y, a1.z)); n.Add(normal);
                    }
                    for (int i = 0; i < N; i++)
                    {
                        int p = b + i * 2;
                        var cross = Vector3.Cross(v[p + 2] - v[p], v[p + 1] - v[p]);
                        if (Vector3.Dot(cross, normal) >= 0f) { t.Add(p); t.Add(p + 2); t.Add(p + 1); t.Add(p + 1); t.Add(p + 2); t.Add(p + 3); }
                        else { t.Add(p); t.Add(p + 1); t.Add(p + 2); t.Add(p + 1); t.Add(p + 3); t.Add(p + 2); }
                    }
                }
                Strip(new Vector3(0, -0.5f, -0.5f), new Vector3(0, 0.5f, -0.5f), Vector3.back);
                Strip(new Vector3(0, -0.5f, 0.5f), new Vector3(0, 0.5f, 0.5f), Vector3.forward);
                Strip(new Vector3(0, 0.5f, -0.5f), new Vector3(0, 0.5f, 0.5f), Vector3.up);
                Strip(new Vector3(0, -0.5f, -0.5f), new Vector3(0, -0.5f, 0.5f), Vector3.down);
                // the two ends
                foreach (float x in new[] { -0.5f, 0.5f })
                {
                    int b = v.Count;
                    var nn = new Vector3(Mathf.Sign(x), 0, 0);
                    v.Add(new Vector3(x, -0.5f, -0.5f)); v.Add(new Vector3(x, 0.5f, -0.5f)); v.Add(new Vector3(x, 0.5f, 0.5f)); v.Add(new Vector3(x, -0.5f, 0.5f));
                    for (int i = 0; i < 4; i++) n.Add(nn);
                    if (x > 0) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                    else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
                }
                s_Bar = new Mesh { name = "x bar" };
                s_Bar.SetVertices(v); s_Bar.SetNormals(n); s_Bar.SetTriangles(t, 0);
                s_Bar.RecalculateBounds();
                return s_Bar;
            }
        }

        /// <summary>Bend every part of the X round a trunk `radius` (in the marker's own units) behind it. force: bend
        /// again even at the same radius (the halo's shape changed in the settings).</summary>
        public void Bend(float radius, bool force = false)
        {
            if (radius < 0.05f) radius = 0.05f;
            if (!force && Mathf.Abs(radius - m_Radius) < 0.002f) return;
            m_Radius = radius;
            float R = radius;
            foreach (Transform c in transform)
            {
                var mf = c.GetComponent<MeshFilter>();
                if (mf == null || c.name == "x decal") continue;
                if (!m_Parts.TryGetValue(mf, out var p)) { p = (mf.sharedMesh, null); if (p.src == null) continue; }
                if (p.bent == null) p.bent = new Mesh { name = p.src.name + " (bent)" };
                // the glow round the X never reaches more than about a third of the way round the trunk: on a thin trunk
                // (the cherry trees) it was wider than the trunk itself and the whole X read as a big red blob
                if (c.name == "x halo") c.localScale = Vector3.one * Mathf.Min(1f, R * 1.0f / Mathf.Max(0.001f, p.src.bounds.extents.x));
                var to = Matrix4x4.TRS(c.localPosition, c.localRotation, c.localScale);
                var back = to.inverse;
                var sv = p.src.vertices;
                for (int i = 0; i < sv.Length; i++)
                {
                    // the card's x runs round the trunk (an arc as long as it was), its z (into the bark) towards the axis
                    var m = to.MultiplyPoint3x4(sv[i]);
                    if (c.name == "x halo") m.z = Mathf.Min(m.z, 0f); // (the glow level with the bars' middle, not tucked in behind them)
                    float rho = R - m.z, phi = m.x / R;
                    sv[i] = back.MultiplyPoint3x4(new Vector3(rho * Mathf.Sin(phi), m.y, R - rho * Mathf.Cos(phi)));
                }
                p.bent.Clear();
                p.bent.vertices = sv;
                if (p.src.colors.Length == sv.Length) p.bent.colors = p.src.colors;
                p.bent.triangles = p.src.triangles;
                p.bent.RecalculateNormals();
                p.bent.RecalculateBounds();
                mf.sharedMesh = p.bent;
                m_Parts[mf] = p;
            }
        }

        void OnDestroy()
        {
            foreach (var p in m_Parts.Values) if (p.bent != null) Destroy(p.bent);
        }
    }
}
