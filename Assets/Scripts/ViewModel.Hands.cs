using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The alien first-person arms (Settings > Display > Alien hands; off brings back the old square ones): modelled on the
    /// player alien (Tools/Alien2) - a slender tapering forearm with no wrist band, a narrow palm and long thin fingers with
    /// round pads on their tips - but with four fingers and a thumb instead of its three. Every finger has three jointed
    /// pieces (one transform per joint, so each curls from its own knuckle) and the thumb a fleshy base and two pieces.
    /// Low-poly like the rest of the game's art: octagonal finger pieces with blunt faceted ends, a faceted slab of a palm
    /// and a ten-sided forearm that swells towards the elbow, built with flat facets (Settings > Display > SHADING smooths
    /// them like everything else held). Chunky and stylised (thick fingers, a broad palm, a sturdy forearm) so they read
    /// boldly on screen, in the world's own matte material in your alien's colour (HandColorHook) - no plastic sheen.
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
        const float PalmHalf = 0.0145f;
        /// <summary>How far in front of the wrist a handle held in the fist runs (under the palm's end, so the fingers wrap it from the knuckles).</summary>
        const float GripZ = 0.063f;
        /// <summary>The forearm runs this far back from the wrist - off the screen, like the block arms did.</summary>
        const float ForearmLen = 1f;
        /// <summary>The fingers' and thumb's sides (octagonal) and the forearm's - low-poly, like the world.</summary>
        const int FingerSides = 8, ArmSides = 10;
        /// <summary>Every finger a touch thicker than the alien's own (chunky, so the facets read).</summary>
        const float FingerThick = 1.12f;
        /// <summary>Where each forearm heads, in the view (right hand; the left's is its mirror): down by your side, so the
        /// arms come in from the bottom corners of the screen like an FPS rig's instead of reaching across it.</summary>
        static readonly Vector3 k_ElbowAnchor = new Vector3(0.36f, -0.66f, -0.02f);
        /// <summary>The hand (everything from the wrist out, measured below at the alien's own size) is drawn this much bigger,
        /// so it reads at first-person distance as clearly as the old block fist did.</summary>
        const float HandScale = 1.3f;

        /// <summary>The knuckle (MCP) of each finger - index, middle, ring, little - and the thumb's root deep in the palm.</summary>
        static readonly Vector3[] s_Base =
        {
            new Vector3(-0.0265f, 0.0015f, 0.075f),
            new Vector3(-0.0088f, 0.0020f, 0.079f),
            new Vector3(0.0088f, 0.0015f, 0.0765f),
            new Vector3(0.0252f, -0.0005f, 0.0705f),
            new Vector3(-0.0235f, -0.0085f, 0.016f),
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
        /// <summary>Each digit's radius at its root, its two joints and its tip (chunky, gently tapering).</summary>
        static readonly float[] s_Rad =
        {
            0.0092f, 0.0081f, 0.0071f, 0.0076f,
            0.0095f, 0.0083f, 0.0072f, 0.0078f,
            0.0090f, 0.0079f, 0.0069f, 0.0074f,
            0.0080f, 0.0071f, 0.0062f, 0.0067f,
            0.0142f, 0.0112f, 0.0096f, 0.0100f, // (each tip flares a little: the alien's round finger pads)
        };
        static float Len(int d, int j) => s_Len[d * 3 + j];
        static float Rad(int d, int j) => s_Rad[d * 4 + j] * FingerThick;

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
                => hook.Track(Art.Part(t, mesh, c, pos, scale, euler, false, null, part == HandColorHook.Skin ? "skin" : "pad"), part == HandColorHook.Skin ? HandColorHook.AlienSkin : HandColorHook.AlienPad); // (the alien hands: their own dark blue, polished)

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
            // it's laid along -Z from the wrist), a sturdy wrist filling out towards the elbow, slightly oval
            a.Forearm = Node(a.Root, "forearm", Vector3.zero);
            P(a.Forearm, ArmMesh(), skin, Vector3.zero, new Vector3(1.1f, 0.92f, 1f), new Vector3(0f, 180f, 0f));
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

            // the forearm heads from the wrist down to your side (k_ElbowAnchor), so it leaves the screen by its bottom corner
            // the way an FPS arm does - bent a little towards straight on from the hand (ElbowK) so the wrist never kinks
            var zh = p.Rot * Vector3.forward;
            var yh = p.Rot * Vector3.up;
            var anchor = arm.InverseTransformPoint(m_Root.TransformPoint(new Vector3(k_ElbowAnchor.x * sx, k_ElbowAnchor.y, k_ElbowAnchor.z)));
            var toAnchor = anchor - p.Pos;
            if (toAnchor.sqrMagnitude < 1e-4f) toAnchor = Vector3.back;
            var back = Vector3.Lerp(toAnchor.normalized, -zh, p.ElbowK * 0.45f);
            if (back.sqrMagnitude < 1e-6f) back = Vector3.back;
            back.Normalize();
            a.Forearm.localPosition = p.Pos;
            a.Forearm.localRotation = Quaternion.LookRotation(-back, yh);
        }

        // ---- meshes ----

        static readonly Dictionary<long, Mesh> s_Lathes = new Dictionary<long, Mesh>();

        /// <summary>A blunt, faceted tapering rod along +Z (FingerSides round): radius r0 at z = 0, r1 at z = len, each end
        /// a low dome of two rings (so pieces laid end to end at joints overlap into chunky knuckles).</summary>
        static Mesh Lathe(float r0, float r1, float len)
        {
            long key = ((long)Mathf.RoundToInt(r0 * 1e5f) << 42) ^ ((long)Mathf.RoundToInt(r1 * 1e5f) << 21) ^ Mathf.RoundToInt(len * 1e5f);
            if (s_Lathes.TryGetValue(key, out var cached) && cached != null) return cached;
            var prof = new List<Vector2> // (z, r)
            {
                new Vector2(-r0 * 0.85f, 0f), new Vector2(-r0 * 0.6f, r0 * 0.72f), new Vector2(0f, r0),
                new Vector2(len, r1), new Vector2(len + r1 * 0.6f, r1 * 0.72f), new Vector2(len + r1 * 0.85f, 0f),
            };
            var m = Faceted(prof, FingerSides, "alien limb");
            s_Lathes[key] = m;
            return m;
        }

        static Mesh s_Arm;

        /// <summary>The forearm (ArmSides round, along +Z from the wrist): a sturdy wrist swelling out to the muscle near the
        /// elbow, then on off the screen.</summary>
        static Mesh ArmMesh()
        {
            if (s_Arm != null) return s_Arm;
            var prof = new List<Vector2>
            {
                new Vector2(-0.02f, 0f), new Vector2(-0.012f, 0.022f), new Vector2(0f, 0.029f), new Vector2(0.05f, 0.031f),
                new Vector2(0.2f, 0.043f), new Vector2(0.34f, 0.05f), new Vector2(0.5f, 0.048f), new Vector2(ForearmLen, 0.052f), new Vector2(ForearmLen + 0.02f, 0f),
            };
            s_Arm = Faceted(prof, ArmSides, "alien forearm");
            return s_Arm;
        }

        /// <summary>A lathed mesh with flat facets (every face its own corners): the profile's (z, r) points turned round Z,
        /// a flat side on top.</summary>
        static Mesh Faceted(List<Vector2> prof, int sides, string name)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            Vector3 At(int r, int sd)
            {
                float an = Mathf.PI * 2f * sd / sides + Mathf.PI / sides, c = Mathf.Cos(an), sn = Mathf.Sin(an);
                return new Vector3(prof[r].y * c, prof[r].y * sn, prof[r].x);
            }
            for (int r = 0; r < prof.Count - 1; r++)
                for (int sd = 0; sd < sides; sd++)
                {
                    int sn = (sd + 1) % sides;
                    if (prof[r].y > 1e-6f) Tri(v, tri, At(r, sd), At(r, sn), At(r + 1, sd));
                    if (prof[r + 1].y > 1e-6f) Tri(v, tri, At(r, sn), At(r + 1, sn), At(r + 1, sd));
                }
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>One flat triangle (its own three corners, so it's lit as a facet).</summary>
        static void Tri(List<Vector3> v, List<int> tri, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
        }

        static readonly Mesh[] s_Palms = new Mesh[2];

        /// <summary>The palm (one for each hand - the thumb's swell is on its own side): a faceted loft of rounded-rectangle
        /// sections from the wrist (z 0, round like the forearm) to the knuckles (z 0.08, wide and flat), blunt at both ends.</summary>
        static Mesh PalmMesh(bool right)
        {
            int idx = right ? 0 : 1;
            if (s_Palms[idx] != null) return s_Palms[idx];
            float sx = right ? 1f : -1f;
            const int sides = 12, along = 5;
            var ring = new List<Vector4>(); // (z, half width, half thickness, how far along the palm 0..1)
            const float z0 = -0.004f, z1 = 0.08f, cap = 0.011f;
            float W(float t) => Mathf.Lerp(0.0215f, 0.0375f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f)));
            float T(float t) => Mathf.Lerp(0.0175f, 0.0135f, Mathf.SmoothStep(0f, 1f, t));
            ring.Add(new Vector4(z0 - cap * 0.7f, W(0f) * 0.7f, T(0f) * 0.7f, 0f));
            for (int k = 0; k <= along; k++)
            {
                float t = k / (float)along;
                ring.Add(new Vector4(Mathf.Lerp(z0, z1, t), W(t), T(t), t));
            }
            ring.Add(new Vector4(z1 + cap * 0.75f, W(1f) * 0.72f, T(1f) * 0.72f, 1f));
            Vector3 At(Vector4 r, int s)
            {
                float an = Mathf.PI * 2f * s / sides + Mathf.PI / sides, c = Mathf.Cos(an), sn = Mathf.Sin(an);
                // a rounded rectangle (superellipse) section
                float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.6f) * r.y;
                float y = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.6f) * r.z;
                float t = r.w;
                // the swell under the thumb (palm side, thumb side, the back half) and the heel (palm side, little finger side)
                float under = Mathf.Max(0f, -sn);
                float thumbSide = Mathf.Max(0f, -c), littleSide = Mathf.Max(0f, c);
                float thenar = Mathf.Sin(Mathf.Clamp01(t / 0.65f) * Mathf.PI) * under * thumbSide * 0.0075f;
                float heel = Mathf.Sin(Mathf.Clamp01(t / 0.55f) * Mathf.PI) * under * littleSide * 0.0045f;
                x += -thenar * 0.6f + heel * 0.4f;
                y -= thenar + heel;
                return new Vector3(x * sx, y, r.x);
            }
            var v = new List<Vector3>();
            var tri = new List<int>();
            // (the left palm is mirrored in x: its triangles wind the other way to face out)
            void Add(Vector3 a, Vector3 b, Vector3 c) { if (right) Tri(v, tri, a, b, c); else Tri(v, tri, a, c, b); }
            var first = new Vector3(0f, 0f, z0 - cap);
            var last = new Vector3(0f, 0f, z1 + cap);
            int n = ring.Count;
            for (int r = 0; r < n - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    Add(At(ring[r], s), At(ring[r], s1), At(ring[r + 1], s));
                    Add(At(ring[r], s1), At(ring[r + 1], s1), At(ring[r + 1], s));
                }
            for (int s = 0; s < sides; s++)
            {
                int s1 = (s + 1) % sides;
                Add(first, At(ring[0], s1), At(ring[0], s));
                Add(At(ring[n - 1], s), At(ring[n - 1], s1), last);
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
