using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The first-person alien hands (Settings > Display > Alien hands; off brings back the old square ones): kept VERY
    /// simple - a few big, chunky rounded blocks, like a clay toy's or a cartoon glove: a fist, a thumb and a thick forearm,
    /// in your alien's colour (HandColorHook.AlienShade), the fist's knuckle face a shade darker so it reads. No fingers.
    /// The fist sits where each item's pose puts the hand (round the handle), and each forearm turns towards your side
    /// every frame so the arms come in from the bottom corners of the screen.
    ///
    /// The hand's own frame (the RIGHT hand; the left is its mirror image): the fist at the origin, the knuckles along +Z,
    /// the palm facing -Y, the thumb on the -X side.
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>Where each forearm heads, in the view (right hand; the left's is its mirror): down by your side.</summary>
        static readonly Vector3 k_ElbowAnchor = new Vector3(0.36f, -0.66f, -0.02f);

        /// <summary>Builds the simple hand under `arm` ("alien": hidden while the square hands are picked).</summary>
        static void BuildSimpleHand(Transform arm, HandColorHook hook, bool right)
        {
            float sx = right ? 1f : -1f;
            var skin = HandColorHook.Shade(Color.white, HandColorHook.AlienSkin); // (the hook gives each part its real colour)
            var root = new GameObject("alien").transform;
            root.SetParent(arm, false);
            void Block(Transform t, Vector3 pos, Vector3 size, float round, Vector3 euler, string name, int part = HandColorHook.AlienSkin)
                => hook.Track(Art.Part(t, RoundedBox(size, round), skin, pos, Vector3.one, euler, false, null, name), part);
            // the fist: one big rounded block; its knuckle face a shade darker
            Block(root, new Vector3(0f, 0f, 0.01f), new Vector3(0.115f, 0.098f, 0.125f), 0.034f, Vector3.zero, "fist");
            Block(root, new Vector3(0f, 0.004f, 0.06f), new Vector3(0.112f, 0.086f, 0.036f), 0.02f, Vector3.zero, "knuckles", HandColorHook.AlienPad);
            // the thumb: a chunky rounded block along the fist's inner side, angled in over the front
            Block(root, new Vector3(-0.056f * sx, 0.026f, 0.034f), new Vector3(0.044f, 0.042f, 0.078f), 0.019f, new Vector3(0f, 22f * sx, 0f), "thumb");
            // the forearm: a thick rounded block from inside the fist back off the screen, a touch narrower at the wrist
            var fore = new GameObject("forearm").transform;
            fore.SetParent(root, false);
            fore.localPosition = new Vector3(0f, 0f, -0.04f);
            Block(fore, new Vector3(0f, 0f, -0.07f), new Vector3(0.086f, 0.078f, 0.16f), 0.03f, Vector3.zero, "wrist");
            Block(fore, new Vector3(0f, 0f, -0.37f), new Vector3(0.096f, 0.088f, 0.5f), 0.034f, Vector3.zero, "arm");
        }

        /// <summary>Each forearm heads from the wrist down towards your side (k_ElbowAnchor), bent a little towards
        /// straight on from the fist so the wrist never kinks; a hand put away below the view hangs its arm on down.</summary>
        void AimForearms()
        {
            foreach (var arm in new[] { m_R, m_L })
            {
                var fore = arm != null ? arm.Find("alien/forearm") : null;
                if (fore == null || !fore.gameObject.activeInHierarchy) continue;
                float sx = arm == m_R ? 1f : -1f;
                var anchor = m_Root.TransformPoint(new Vector3(k_ElbowAnchor.x * sx, k_ElbowAnchor.y, k_ElbowAnchor.z));
                var toAnchor = anchor - fore.position;
                if (toAnchor.sqrMagnitude < 1e-6f) continue;
                var back = Vector3.Lerp(toAnchor.normalized, -arm.forward, 0.3f);
                float low = Mathf.InverseLerp(k_ElbowAnchor.y + 0.18f, k_ElbowAnchor.y + 0.02f, m_Root.InverseTransformPoint(fore.position).y);
                if (low > 0f) back = Vector3.Lerp(back, m_Root.TransformDirection(new Vector3(0.25f * sx, -1f, -0.35f)).normalized, low);
                if (back.sqrMagnitude < 1e-6f) continue;
                fore.rotation = Quaternion.LookRotation(-back.normalized, arm.up);
            }
        }

        static readonly Dictionary<(Vector3, float), Mesh> s_Rounded = new Dictionary<(Vector3, float), Mesh>();

        /// <summary>A box `size` across with its edges and corners rounded off by `round`, smoothly shaded (a subdivided cube
        /// with every point pulled in onto the rounded shape).</summary>
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
                // each face of a cube, an (n+1) x (n+1) grid - the points bunched towards its edges, where the rounding is
                var u = f.x != 0f ? Vector3.forward : Vector3.right;
                var v = Vector3.Cross(f, u);
                int start = verts.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        float a = Ease(i / (float)n) * 2f - 1f, b = Ease(j / (float)n) * 2f - 1f;
                        var q = Vector3.Scale(f + u * a + v * b, h); // (on the box)
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

        /// <summary>A triangle wound to face away from the middle.</summary>
        static void AddOut(List<Vector3> v, List<int> tris, int a, int b, int c)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), v[a] + v[b] + v[c]) < 0f) { int s = b; b = c; c = s; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        static float Ease(float t) => 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
    }
}
