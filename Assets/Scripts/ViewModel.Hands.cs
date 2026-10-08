using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The alien first-person arms (Settings > Display > Alien hands; off brings back the old square ones): modelled on the
    /// player alien (Tools/Alien2) - a slender tapering forearm with no wrist band, a narrow palm and long thin fingers with
    /// round pads on their tips - but with four fingers and a thumb instead of its three. Every finger has three jointed
    /// pieces (one transform per joint, so each curls from its own knuckle) and the thumb a fleshy base and two pieces.
    /// It's all smooth: lathed, rounded, tapering finger and arm pieces, a soft rounded-slab palm, ellipsoid pads.
    ///
    /// The hand's own frame (the RIGHT hand; the left is its mirror image): the wrist at the origin, the fingers along +Z,
    /// the palm facing -Y, the thumb on the -X side. A finger curls with +X rotations (towards the palm), spreads with Y.
    /// Everything stays under the arm (m_R / m_L) so IsHand() and the post-fx layers see it as hand.
    /// The poses (what each finger does with each item) are in ViewModel.Fingers.cs.
    /// </summary>
    public partial class ViewModel
    {
        // ---- measurements (metres) ----
        /// <summary>Half the palm's thickness.</summary>
        const float PalmHalf = 0.0115f;
        /// <summary>How far in front of the wrist a handle held in the fist runs (under the palm's end, so the fingers wrap it from the knuckles).</summary>
        const float GripZ = 0.063f;
        /// <summary>The forearm runs this far back from the wrist - off the screen, like the block arms did.</summary>
        const float ForearmLen = 1f;
        /// <summary>The hand (everything from the wrist out, measured below at the alien's own size) is drawn this much bigger,
        /// so it reads at first-person distance as clearly as the old block fist did.</summary>
        const float HandScale = 1.3f;

        /// <summary>The knuckle (MCP) of each finger - index, middle, ring, little - and the thumb's root deep in the palm.</summary>
        static readonly Vector3[] s_Base =
        {
            new Vector3(-0.0195f, 0.0015f, 0.075f),
            new Vector3(-0.0065f, 0.0020f, 0.079f),
            new Vector3(0.0065f, 0.0015f, 0.0765f),
            new Vector3(0.0185f, -0.0005f, 0.0705f),
            new Vector3(-0.0185f, -0.0070f, 0.016f),
        };
        /// <summary>Each digit's three bones (the alien's fingers are long: the middle one is nearly 10 cm).</summary>
        static readonly float[] s_Len =
        {
            0.040f, 0.026f, 0.021f,
            0.044f, 0.029f, 0.022f,
            0.041f, 0.027f, 0.021f,
            0.033f, 0.021f, 0.018f,
            0.034f, 0.029f, 0.024f, // (the thumb: its fleshy base, then two pieces)
        };
        /// <summary>Each digit's radius at its root, its two joints and its tip (thin, tapering).</summary>
        static readonly float[] s_Rad =
        {
            0.0070f, 0.0061f, 0.0053f, 0.0057f,
            0.0072f, 0.0063f, 0.0054f, 0.0058f,
            0.0068f, 0.0060f, 0.0052f, 0.0056f,
            0.0060f, 0.0053f, 0.0046f, 0.0050f,
            0.0110f, 0.0085f, 0.0072f, 0.0076f, // (each tip flares a little: the alien's round finger pads)
        };
        static float Len(int d, int j) => s_Len[d * 3 + j];
        static float Rad(int d, int j) => s_Rad[d * 4 + j];

        sealed class AlienArm
        {
            public bool Right;
            public Transform Root, Hand, Forearm;
            /// <summary>[digit, joint]: 0-3 the fingers (index..little), 4 the thumb; joint 0 is the knuckle.</summary>
            public readonly Transform[,] J = new Transform[5, 3];
        }

        AlienArm m_AR, m_AL;

        static Transform Node(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        /// <summary>Builds the alien arm under `arm` (hidden while the square hands are picked).</summary>
        static AlienArm BuildAlienArm(Transform arm, HandColorHook hook, Color skin, Color dark, bool right)
        {
            float sx = right ? 1f : -1f;
            Vector3 M(float x, float y, float z) => new Vector3(x * sx, y, z);
            Vector3 ME(float x, float y, float z) => new Vector3(x, y * sx, z * sx);
            void P(Transform t, Mesh mesh, Color c, Vector3 pos, Vector3 scale, Vector3 euler, int part = HandColorHook.Skin)
                => hook.Track(Art.Part(t, mesh, c, pos, scale, euler, false, null, part == HandColorHook.Skin ? "skin" : "pad"), part);

            var a = new AlienArm { Right = right };
            a.Root = Node(arm, "alien", Vector3.zero);
            a.Hand = Node(a.Root, "hand", Vector3.zero);
            a.Hand.localScale = Vector3.one * HandScale;
            // the palm: one smooth piece from the wrist (as round as the forearm it grows out of) widening and flattening to
            // the knuckles, the fleshy swell under the thumb and the heel of the hand moulded in
            P(a.Hand, PalmMesh(right), skin, Vector3.zero, Vector3.one, Vector3.zero);
            // the four fingers and the thumb: a joint transform per bone, each bone a tapering rounded piece starting at its
            // joint (so the rounded ends overlap into smooth knuckles), the last one flaring very slightly to the alien's
            // round fingertip
            for (int d = 0; d < 5; d++)
            {
                var j0 = Node(a.Hand, d < 4 ? "finger" + d : "thumb", M(s_Base[d].x, s_Base[d].y, s_Base[d].z));
                var j1 = Node(j0, "j1", new Vector3(0f, 0f, Len(d, 0)));
                var j2 = Node(j1, "j2", new Vector3(0f, 0f, Len(d, 1)));
                a.J[d, 0] = j0; a.J[d, 1] = j1; a.J[d, 2] = j2;
                P(j0, Lathe(Rad(d, 0), Rad(d, 1), Len(d, 0)), skin, Vector3.zero, d == 4 ? new Vector3(1.15f, 0.92f, 1f) : Vector3.one, Vector3.zero);
                P(j1, Lathe(Rad(d, 1), Rad(d, 2), Len(d, 1)), skin, Vector3.zero, Vector3.one, Vector3.zero);
                P(j2, Lathe(Rad(d, 2), Rad(d, 3), Len(d, 2) - Rad(d, 3) * 0.6f), skin, Vector3.zero, Vector3.one, Vector3.zero);
            }
            // the forearm: one smooth tapering limb from the wrist back off the screen (its +Z points along it to the wrist;
            // it's laid along -Z from the wrist), slim at the wrist, filling out a little, slightly oval
            a.Forearm = Node(a.Root, "forearm", Vector3.zero);
            P(a.Forearm, Lathe(0.0195f, 0.034f, ForearmLen), skin, Vector3.zero, new Vector3(1.12f, 0.9f, 1f), new Vector3(0f, 180f, 0f));
            return a;
        }

        static Quaternion Mirror(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        /// <summary>Puts the arm into a pose: the hand, every joint, then the forearm from the wrist back off the screen.</summary>
        void ApplyAlien(AlienArm a, Transform arm, JointPose p)
        {
            if (a == null || !a.Root.gameObject.activeInHierarchy) return;
            float sx = a.Right ? 1f : -1f;
            a.Hand.localPosition = p.Pos;
            a.Hand.localRotation = p.Rot;
            for (int d = 0; d < 4; d++)
            {
                a.J[d, 0].localRotation = Quaternion.Euler(0f, p.Spread[d] * sx, 0f) * Quaternion.Euler(p.A[d * 3], 0f, 0f);
                a.J[d, 1].localRotation = Quaternion.Euler(p.A[d * 3 + 1], 0f, 0f);
                a.J[d, 2].localRotation = Quaternion.Euler(p.A[d * 3 + 2], 0f, 0f);
            }
            a.J[4, 0].localRotation = a.Right ? p.Thumb : Mirror(p.Thumb);
            a.J[4, 1].localRotation = Quaternion.Euler(p.A[13], 0f, 0f);
            a.J[4, 2].localRotation = Quaternion.Euler(p.A[14], 0f, 0f);

            // the forearm leaves the wrist part way between the arm's old line (back along -Z) and straight on from the hand,
            // dropping and swinging out a little so it heads off the bottom corner of the screen
            var zh = p.Rot * Vector3.forward;
            var yh = p.Rot * Vector3.up;
            var outward = arm.InverseTransformDirection(m_Root.TransformDirection(new Vector3(0.16f * sx, -0.3f, 0f)));
            var back = Vector3.Lerp(Vector3.back, -zh, p.ElbowK) + outward * 0.6f;
            if (back.sqrMagnitude < 1e-6f) back = Vector3.back;
            back.Normalize();
            a.Forearm.localPosition = p.Pos;
            a.Forearm.localRotation = Quaternion.LookRotation(-back, yh);
        }

        // ---- meshes ----

        static readonly Dictionary<long, Mesh> s_Lathes = new Dictionary<long, Mesh>();

        /// <summary>A rounded tapering rod along +Z: radius r0 at z = 0, r1 at z = len, each end a half sphere of its own radius
        /// (so pieces laid end to end at joints overlap into round knuckles).</summary>
        static Mesh Lathe(float r0, float r1, float len)
        {
            long key = ((long)Mathf.RoundToInt(r0 * 1e5f) << 42) ^ ((long)Mathf.RoundToInt(r1 * 1e5f) << 21) ^ Mathf.RoundToInt(len * 1e5f);
            if (s_Lathes.TryGetValue(key, out var cached) && cached != null) return cached;
            const int sides = 14, cap = 5;
            var prof = new List<Vector4>(); // (z, r, normal z, normal r)
            var body = new Vector2(-(r1 - r0) / Mathf.Max(1e-4f, len), 1f).normalized;
            for (int i = 0; i <= cap; i++)
            {
                float ph = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * i / cap;
                var n = i == cap ? body : new Vector2(Mathf.Sin(ph), Mathf.Cos(ph));
                prof.Add(new Vector4(r0 * Mathf.Sin(ph), r0 * Mathf.Cos(ph), n.x, n.y));
            }
            for (int i = 0; i <= cap; i++)
            {
                float ph = Mathf.PI * 0.5f * i / cap;
                var n = i == 0 ? body : new Vector2(Mathf.Sin(ph), Mathf.Cos(ph));
                prof.Add(new Vector4(len + r1 * Mathf.Sin(ph), r1 * Mathf.Cos(ph), n.x, n.y));
            }
            int rings = prof.Count;
            var v = new Vector3[rings * sides];
            var nn = new Vector3[rings * sides];
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    float an = Mathf.PI * 2f * s / sides, c = Mathf.Cos(an), sn = Mathf.Sin(an);
                    var pr = prof[r];
                    v[r * sides + s] = new Vector3(pr.y * c, pr.y * sn, pr.x);
                    nn[r * sides + s] = new Vector3(pr.w * c, pr.w * sn, pr.z).normalized;
                }
            var tris = new List<int>();
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int i0 = r * sides + s, i1 = r * sides + (s + 1) % sides, i2 = i0 + sides, i3 = i1 + sides;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i1); tris.Add(i3); tris.Add(i2);
                }
            var m = new Mesh { name = "alien limb" };
            m.vertices = v;
            m.normals = nn;
            m.triangles = tris.ToArray();
            m.RecalculateBounds();
            s_Lathes[key] = m;
            return m;
        }

        static readonly Mesh[] s_Palms = new Mesh[2];

        /// <summary>The palm (one for each hand - the thumb's swell is on its own side): a loft of rounded-rectangle sections
        /// from the wrist (z 0, round like the forearm) to the knuckles (z 0.08, wide and flat), domed shut at both ends.</summary>
        static Mesh PalmMesh(bool right)
        {
            int idx = right ? 0 : 1;
            if (s_Palms[idx] != null) return s_Palms[idx];
            float sx = right ? 1f : -1f;
            const int sides = 24;
            var ring = new List<Vector4>(); // (z, half width, half thickness, how far along the palm 0..1)
            const float z0 = -0.004f, z1 = 0.08f, cap = 0.009f;
            float W(float t) => Mathf.Lerp(0.0165f, 0.0285f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f)));
            float T(float t) => Mathf.Lerp(0.0135f, 0.0098f, Mathf.SmoothStep(0f, 1f, t));
            for (int k = 4; k >= 1; k--) // (the dome inside the wrist)
            {
                float ph = Mathf.PI * 0.5f * k / 4f;
                ring.Add(new Vector4(z0 - cap * Mathf.Sin(ph), W(0f) * Mathf.Cos(ph), T(0f) * Mathf.Cos(ph), 0f));
            }
            const int along = 14;
            for (int k = 0; k <= along; k++)
            {
                float t = k / (float)along;
                ring.Add(new Vector4(Mathf.Lerp(z0, z1, t), W(t), T(t), t));
            }
            for (int k = 1; k <= 5; k++) // (the dome over the knuckles)
            {
                float ph = Mathf.PI * 0.5f * k / 5f;
                ring.Add(new Vector4(z1 + cap * Mathf.Sin(ph), W(1f) * Mathf.Cos(ph), T(1f) * Mathf.Cos(ph), 1f));
            }
            var v = new List<Vector3>();
            foreach (var r in ring)
                for (int s = 0; s < sides; s++)
                {
                    float an = Mathf.PI * 2f * s / sides, c = Mathf.Cos(an), sn = Mathf.Sin(an);
                    // a rounded rectangle (superellipse) section
                    float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.6f) * r.y;
                    float y = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.6f) * r.z;
                    float t = r.w;
                    // the swell under the thumb (palm side, thumb side, the back half) and the heel (palm side, little finger side)
                    float under = Mathf.Max(0f, -sn);
                    float thumbSide = Mathf.Max(0f, -c), littleSide = Mathf.Max(0f, c);
                    float thenar = Mathf.Sin(Mathf.Clamp01(t / 0.65f) * Mathf.PI) * under * thumbSide * 0.0055f;
                    float heel = Mathf.Sin(Mathf.Clamp01(t / 0.55f) * Mathf.PI) * under * littleSide * 0.003f;
                    x += -thenar * 0.6f + heel * 0.4f;
                    y -= thenar + heel;
                    v.Add(new Vector3(x * sx, y, r.x));
                }
            int first = v.Count; v.Add(new Vector3(0f, 0f, z0 - cap));
            int last = v.Count; v.Add(new Vector3(0f, 0f, z1 + cap));
            var tri = new List<int>();
            int rings = ring.Count;
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a0 = r * sides + s, a1 = r * sides + (s + 1) % sides, b0 = a0 + sides, b1 = a1 + sides;
                    if (right) { tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0); }
                    else { tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1); }
                }
            for (int s = 0; s < sides; s++)
            {
                int a0 = s, a1 = (s + 1) % sides, b0 = (rings - 1) * sides + s, b1 = (rings - 1) * sides + (s + 1) % sides;
                if (right) { tri.Add(first); tri.Add(a1); tri.Add(a0); tri.Add(b0); tri.Add(b1); tri.Add(last); }
                else { tri.Add(first); tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(last); tri.Add(b1); }
            }
            var m = new Mesh { name = "alien palm" };
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            s_Palms[idx] = m;
            return m;
        }
    }
}
