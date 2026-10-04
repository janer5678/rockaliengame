using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public static partial class GameSettings
    {
        /// <summary>Settings > Display > SHADING: the first-person hands and everything held in them shaded smooth (off: flat facets, the normal look).</summary>
        public static readonly DisplayPref.Bool SmoothHands = new("shade.smooth.hands", "SHADING", DisplayDefaults.SmoothHands);
        /// <summary>Settings > Display > SHADING: the alien players and the stadium crowd shaded smooth (off: flat facets, the normal look).</summary>
        public static readonly DisplayPref.Bool SmoothAliens = new("shade.smooth.aliens", "SHADING", DisplayDefaults.SmoothAliens);
    }

    /// <summary>
    /// "Shade smooth" (like Blender's): a copy of a mesh whose normals are averaged across every corner at the same place,
    /// so the light runs smoothly over the facets instead of each face being lit flat. The copies are made once per mesh
    /// and swapped in and out of the MeshFilters / SkinnedMeshRenderers under a root (Apply), so turning the setting off
    /// puts the very same original meshes back. A mesh that isn't readable is left as it is.
    /// </summary>
    public static class SmoothShade
    {
        static readonly Dictionary<Mesh, Mesh> s_Smooth = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<Mesh, Mesh> s_Orig = new Dictionary<Mesh, Mesh>();

        /// <summary>(tests) How many meshes were swapped by the last Apply.</summary>
        public static int LastSwapped;

        /// <summary>Is this one of the smooth copies?</summary>
        public static bool IsSmooth(Mesh m) => m != null && s_Orig.ContainsKey(m);

        /// <summary>The smooth copy of a mesh (made the first time), or null if it can't be made.</summary>
        public static Mesh SmoothOf(Mesh m)
        {
            if (m == null) return null;
            if (s_Orig.ContainsKey(m)) return m;
            if (s_Smooth.TryGetValue(m, out var s)) return s;
            if (!m.isReadable) { s_Smooth[m] = null; return null; }
            s = Object.Instantiate(m);
            s.name = m.name + " (smooth)";
            var v = m.vertices;
            var n = new Vector3[v.Length];
            for (int sm = 0; sm < m.subMeshCount; sm++)
            {
                if (m.GetTopology(sm) != MeshTopology.Triangles) continue;
                var t = m.GetTriangles(sm);
                AccumulateFaces(v, t, n);
            }
            WeldAverage(v, n);
            s.normals = n;
            s_Smooth[m] = s;
            s_Orig[s] = m;
            return s;
        }

        /// <summary>Smooth normals for a triangle list: each corner gets the (area weighted) average of the faces round its position.</summary>
        public static Vector3[] SmoothNormals(Vector3[] v, int[] tris)
        {
            var n = new Vector3[v.Length];
            AccumulateFaces(v, tris, n);
            WeldAverage(v, n);
            return n;
        }

        static void AccumulateFaces(Vector3[] v, int[] t, Vector3[] n)
        {
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                var f = Vector3.Cross(v[b] - v[a], v[c] - v[a]); // (its length is twice the area: bigger faces count more)
                n[a] += f; n[b] += f; n[c] += f;
            }
        }

        /// <summary>Adds up the normals of every corner at the same place (to 0.1 mm) and gives each of them the total, normalised.</summary>
        static void WeldAverage(Vector3[] v, Vector3[] n)
        {
            var sum = new Dictionary<Vector3Int, Vector3>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 10000f);
                sum[k] = sum.TryGetValue(k, out var s) ? s + n[i] : n[i];
            }
            for (int i = 0; i < v.Length; i++)
            {
                var s = sum[Vector3Int.RoundToInt(v[i] * 10000f)];
                n[i] = s.sqrMagnitude > 1e-20f ? s.normalized : Vector3.up;
            }
        }

        /// <summary>Swaps every mesh under root for its smooth copy (on) or back to the original (off).</summary>
        public static void Apply(GameObject root, bool on)
        {
            LastSwapped = 0;
            if (root == null) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = Swap(mf.sharedMesh, on);
                if (m != null) { mf.sharedMesh = m; LastSwapped++; }
            }
            foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = Swap(sk.sharedMesh, on);
                if (m != null) { sk.sharedMesh = m; LastSwapped++; }
            }
        }

        /// <summary>What a mesh should be swapped for (null: leave it).</summary>
        static Mesh Swap(Mesh m, bool on)
        {
            if (m == null || Own(m)) return null;
            if (on) { if (s_Orig.ContainsKey(m)) return null; return SmoothOf(m); }
            return s_Orig.TryGetValue(m, out var o) ? o : null;
        }

        /// <summary>The hands' own meshes, which shade themselves (they're re-shaped every time the claws move).</summary>
        static bool Own(Mesh m) => m.name == ViewModel.BlockyMeshName || m.name == "alien arm (posed)";
    }

    /// <summary>Keeps the meshes under its object smooth or flat as the setting says (Settings > Display > SHADING), live.</summary>
    public class SmoothShadeHook : MonoBehaviour
    {
        /// <summary>true: follows "aliens & crowd"; false: "hands & held items".</summary>
        public bool Aliens;
        bool m_Done, m_On;

        public static SmoothShadeHook Add(GameObject go, bool aliens)
        {
            var h = go.GetComponent<SmoothShadeHook>();
            if (h == null) h = go.AddComponent<SmoothShadeHook>();
            h.Aliens = aliens;
            h.Refresh(true);
            return h;
        }

        bool Want => Aliens ? GameSettings.SmoothAliens.Value : GameSettings.SmoothHands.Value;

        void OnEnable() { DisplayPref.Changed += OnChanged; Refresh(false); }
        void OnDisable() => DisplayPref.Changed -= OnChanged;
        void OnChanged() => Refresh(false);

        /// <summary>Applies the setting (force: even if it hasn't changed, e.g. new meshes were added under it).</summary>
        public void Refresh(bool force)
        {
            if (this == null) return;
            bool on = Want;
            if (!force && m_Done && on == m_On) return;
            m_Done = true; m_On = on;
            // (off and never smoothed: nothing to put back)
            SmoothShade.Apply(gameObject, on);
        }
    }
}
