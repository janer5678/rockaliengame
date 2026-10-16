using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The ROUND robot hands (Settings > Display > Hands: Round): exactly the square robot hands - the same five parts in
    /// the same places and sizes (a fist, a darker knuckle row, a thumb, a team-colour wrist band, a forearm) - but each
    /// part rounded off (a box with big round edges and corners), so they read circular instead of square. The one other
    /// difference: holding the rock, the hands go up its sides with the fist curling over it (Rust's), instead of cupping
    /// it from below (PoseRock).
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>Builds the round hand under `hand` ("round": hidden unless that style's picked).</summary>
        static void BuildRoundHand(Transform hand, HandColorHook hook, Color skin, Color dark, Color band, float side)
        {
            var round = new GameObject("round").transform;
            round.SetParent(hand, false);
            void Part(Color c, Vector3 pos, Vector3 size, float r, Vector3 euler, int part, string name)
                => hook.Track(Art.Part(round, RoundedBox(size, r), c, pos, Vector3.one, euler, false, null, name), part);
            Part(skin, Vector3.zero, new Vector3(0.085f, 0.09f, 0.1f), 0.038f, Vector3.zero, HandColorHook.Skin, "fist");
            Part(dark, new Vector3(0, 0.005f, 0.055f), new Vector3(0.09f, 0.075f, 0.035f), 0.017f, Vector3.zero, HandColorHook.Knuckles, "knuckles");
            Part(skin, new Vector3(-0.045f * side, 0.025f, 0.035f), new Vector3(0.03f, 0.03f, 0.065f), 0.0145f, new Vector3(0, 20 * side, 0), HandColorHook.Skin, "thumb");
            Part(band, new Vector3(0, 0, -0.075f), new Vector3(0.082f, 0.082f, 0.035f), 0.017f, Vector3.zero, HandColorHook.Band, "band");
            Part(skin, new Vector3(0, 0, -0.32f), new Vector3(0.072f, 0.072f, 0.46f), 0.035f, Vector3.zero, HandColorHook.Skin, "forearm");
        }

        static readonly Dictionary<(Vector3, float), Mesh> s_Rounded = new Dictionary<(Vector3, float), Mesh>();

        /// <summary>A box `size` across with its edges and corners rounded off by `round`, shaded smooth (a subdivided cube
        /// with every point pulled onto the rounded shape).</summary>
        static Mesh RoundedBox(Vector3 size, float round)
        {
            if (s_Rounded.TryGetValue((size, round), out var cached) && cached) return cached;
            var h = size * 0.5f;
            round = Mathf.Min(round, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.98f);
            var inner = new Vector3(h.x - round, h.y - round, h.z - round);
            const int n = 8;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var f in faces)
            {
                var u = f.x != 0f ? Vector3.forward : Vector3.right;
                var v = Vector3.Cross(f, u);
                int start = verts.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        float a = Ease(i / (float)n) * 2f - 1f, b = Ease(j / (float)n) * 2f - 1f;
                        var q = Vector3.Scale(f + u * a + v * b, h);
                        var c = new Vector3(Mathf.Clamp(q.x, -inner.x, inner.x), Mathf.Clamp(q.y, -inner.y, inner.y), Mathf.Clamp(q.z, -inner.z, inner.z));
                        var nrm = q - c;
                        nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : f;
                        verts.Add(c + nrm * round);
                        norms.Add(nrm);
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int k = start + j * (n + 1) + i;
                        AddOut(verts, tris, k, k + n + 1, k + 1);
                        AddOut(verts, tris, k + 1, k + n + 1, k + n + 2);
                    }
            }
            var m = new Mesh { name = "rounded box" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            s_Rounded[(size, round)] = m;
            return m;
        }

        static void AddOut(List<Vector3> v, List<int> tris, int a, int b, int c)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), v[a] + v[b] + v[c]) < 0f) { int s = b; b = c; c = s; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        static float Ease(float t) => 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
    }
}
