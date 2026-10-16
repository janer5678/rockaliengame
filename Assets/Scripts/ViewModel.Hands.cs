using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The first-person arms (Settings > Display > Alien hands; off brings back the old square ones): the arms model
    /// (ViewModel.Rig.cs - a real rigged pair of arms, three fingers and a thumb, in your team's colour) driven by the pose
    /// skeleton built here. If the model can't be loaded, the skeleton wears the built mesh below instead: ONE smoothly
    /// shaded skinned mesh per arm (forearm, wrist, palm, fingers and thumb flowing into each other).
    ///
    /// The skeleton: the hand (scaled HandScale), each finger's three joints and the thumb's (one transform each, so they
    /// curl from their own knuckles - ViewModel.Fingers.cs poses them), and the forearm (from the wrist back towards the
    /// elbow). Digits 0-2 are the fingers (index, middle, outer), 4 the thumb; 3 is unused.
    ///
    /// The hand's own frame (the RIGHT hand; the left is its mirror image): the wrist at the origin, the fingers along +Z,
    /// the palm facing -Y, the thumb on the -X side. A finger curls with +X rotations (towards the palm), spreads with Y.
    /// Everything stays under the arm (m_R / m_L) so IsHand() and the post-fx layers see it as hand.
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>The fingers (digits 0..FingerCount-1); the thumb is digit 4.</summary>
        const int FingerCount = 3;

        // ---- measurements (metres; taken from the arms model when it's there - MeasureRig) ----
        /// <summary>Half the palm's thickness (the palm's underside is this far under the wrist's line).</summary>
        static float PalmHalf = 0.013f;
        /// <summary>How far in front of the wrist a handle held in the fist runs (under the palm's end, so the fingers wrap it from the knuckles).</summary>
        static float GripZ = 0.062f;
        /// <summary>About where the fingers' knuckles are, in front of the wrist.</summary>
        static float KnuckleZ = 0.077f;
        /// <summary>How big this hand is next to the one the grips were first worked out for (knuckles 7.85 cm out).</summary>
        static float HS => KnuckleZ / 0.0785f;
        /// <summary>Where each forearm heads, in the view (right hand; the left's is its mirror): down by your side, so the
        /// arms come in from the bottom corners of the screen like an FPS rig's instead of reaching across it.</summary>
        static readonly Vector3 k_ElbowAnchor = new Vector3(0.36f, -0.66f, -0.02f);
        /// <summary>The hand (everything from the wrist out, measured below) is drawn this much bigger.</summary>
        const float HandScale = 1f;

        /// <summary>The knuckle of each finger - index, middle, outer - (slot 3 unused) and the thumb's root deep in the palm.</summary>
        static readonly Vector3[] s_Base =
        {
            new Vector3(-0.0186f, 0.0008f, 0.0765f),
            new Vector3(0.0000f, 0.0012f, 0.0800f),
            new Vector3(0.0186f, 0.0000f, 0.0760f),
            new Vector3(0.0186f, 0.0000f, 0.0760f),
            new Vector3(-0.0205f, -0.0075f, 0.0170f),
        };
        /// <summary>Each digit's three bones: human proportions (the alien's own are long and claw-like).</summary>
        static readonly float[] s_Len =
        {
            0.034f, 0.021f, 0.017f,
            0.037f, 0.023f, 0.018f,
            0.034f, 0.021f, 0.017f,
            0.034f, 0.021f, 0.017f,
            0.030f, 0.026f, 0.021f, // (the thumb: its fleshy base, then two pieces)
        };
        /// <summary>Each digit's radius at its root, its two joints and its tip.</summary>
        static readonly float[] s_Rad =
        {
            0.0084f, 0.0078f, 0.0071f, 0.0067f,
            0.0087f, 0.0080f, 0.0073f, 0.0069f,
            0.0083f, 0.0077f, 0.0070f, 0.0066f,
            0.0083f, 0.0077f, 0.0070f, 0.0066f,
            0.0135f, 0.0103f, 0.0090f, 0.0085f,
        };
        static float Len(int d, int j) => s_Len[d * 3 + j];
        static float Rad(int d, int j) => s_Rad[d * 4 + j];

        sealed class AlienArm
        {
            public bool Right;
            public Transform Root, Hand, Forearm;
            /// <summary>The arms model on this skeleton (null: the built mesh).</summary>
            public ArmRig Rig;
            /// <summary>[digit, joint]: 0-2 the fingers (index, middle, outer), 4 the thumb (3: none); joint 0 is the knuckle.</summary>
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

        /// <summary>Every digit there is: the fingers, then the thumb.</summary>
        static readonly int[] s_Digits = { 0, 1, 2, 4 };

        /// <summary>Builds the arm under `arm` (hidden while the square hands are picked): its skeleton and the skin on it.</summary>
        static AlienArm BuildAlienArm(Transform arm, HandColorHook hook, bool right)
        {
            MeasureRig(); // (the skeleton takes the model's measurements)
            float sx = right ? 1f : -1f;
            var a = new AlienArm { Right = right };
            a.Root = Node(arm, "alien", Vector3.zero);
            a.Hand = Node(a.Root, "hand", Vector3.zero);
            a.Hand.localScale = Vector3.one * HandScale;
            foreach (int d in s_Digits)
            {
                var j0 = Node(a.Hand, d < 4 ? "finger" + d : "thumb", new Vector3(s_Base[d].x * sx, s_Base[d].y, s_Base[d].z));
                var j1 = Node(j0, "j1", new Vector3(0f, 0f, Len(d, 0)));
                var j2 = Node(j1, "j2", new Vector3(0f, 0f, Len(d, 1)));
                a.J[d, 0] = j0; a.J[d, 1] = j1; a.J[d, 2] = j2;
            }
            a.Forearm = Node(a.Root, "forearm", Vector3.zero);
            a.Rig = BuildRig(a.Root, hook, right);
            if (a.Rig != null) return a;

            // (no model:) the skin: one mesh on the whole skeleton (bound in this rest pose: everything straight, the forearm behind the wrist)
            var bones = new Transform[BoneCount];
            bones[BoneHand] = a.Hand;
            bones[BoneArm] = a.Forearm;
            foreach (int d in s_Digits) for (int j = 0; j < 3; j++) bones[BoneJ(d, j)] = a.J[d, j];
            var skin = new GameObject("skin");
            skin.transform.SetParent(a.Root, false);
            var smr = skin.AddComponent<SkinnedMeshRenderer>();
            var mesh = SkinMesh(right);
            if (mesh.bindposes == null || mesh.bindposes.Length != bones.Length)
            {
                var bind = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * skin.transform.localToWorldMatrix;
                mesh.bindposes = bind;
            }
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = a.Hand;
            smr.quality = SkinQuality.Bone2;
            smr.updateWhenOffscreen = true; // (it swings all over the view: never culled on a stale box)
            hook.Track(skin, HandColorHook.AlienSkin);
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
            for (int d = 0; d < FingerCount; d++)
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
            if (a.Rig != null) ApplyRig(a, a.Rig);
        }

        // ======== the mesh ========

        const int BoneHand = 0, BoneArm = 1, BoneCount = 2 + 4 * 3;
        /// <summary>A digit's joint's bone (the thumb's after the three fingers').</summary>
        static int BoneJ(int d, int j) => 2 + (d == 4 ? 3 : d) * 3 + j;

        /// <summary>A vertex's two bones: all of a, or blended towards b by t.</summary>
        static BoneWeight BW(int a, int b = -1, float t = 0f)
        {
            if (b < 0 || t <= 0f) return new BoneWeight { boneIndex0 = a, weight0 = 1f };
            if (t >= 1f) return new BoneWeight { boneIndex0 = b, weight0 = 1f };
            // (the heavier bone first)
            return t <= 0.5f
                ? new BoneWeight { boneIndex0 = a, weight0 = 1f - t, boneIndex1 = b, weight1 = t }
                : new BoneWeight { boneIndex0 = b, weight0 = t, boneIndex1 = a, weight1 = 1f - t };
        }

        /// <summary>A mesh being built: triangles, each corner with its bones; made with smooth normals (averaged over every
        /// corner at the same place, across the pieces too), so the whole arm is shaded as one seamless surface.</summary>
        sealed class SkinBuild
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<BoneWeight> W = new List<BoneWeight>();
            public readonly List<int> T = new List<int>();

            void Add(Vector3 p, BoneWeight w) { T.Add(V.Count); V.Add(p); W.Add(w); }
            public void Tri(Vector3 a, BoneWeight wa, Vector3 b, BoneWeight wb, Vector3 c, BoneWeight wc) { Add(a, wa); Add(b, wb); Add(c, wc); }

            /// <summary>Rings of the same number of points (each made by Ring, in its plane's x and y) joined into a tube
            /// running along x × y, each end closed onto a point; it faces out.</summary>
            public void Loft(List<Vector3[]> rings, List<BoneWeight> ws, Vector3 start, Vector3 end)
            {
                int n = rings[0].Length;
                for (int r = 0; r + 1 < rings.Count; r++)
                    for (int s = 0; s < n; s++)
                    {
                        int s1 = (s + 1) % n;
                        Tri(rings[r][s], ws[r], rings[r][s1], ws[r], rings[r + 1][s], ws[r + 1]);
                        Tri(rings[r][s1], ws[r], rings[r + 1][s1], ws[r + 1], rings[r + 1][s], ws[r + 1]);
                    }
                int last = rings.Count - 1;
                for (int s = 0; s < n; s++)
                {
                    int s1 = (s + 1) % n;
                    Tri(start, ws[0], rings[0][s1], ws[0], rings[0][s], ws[0]);
                    Tri(end, ws[last], rings[last][s], ws[last], rings[last][s1], ws[last]);
                }
            }

            public Mesh Make(string name, bool mirror)
            {
                var v = new List<Vector3>(V);
                var t = new List<int>(T);
                if (mirror)
                {
                    for (int i = 0; i < v.Count; i++) v[i] = new Vector3(-v[i].x, v[i].y, v[i].z);
                    for (int i = 0; i + 2 < t.Count; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                }
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetTriangles(t, 0);
                m.boneWeights = W.ToArray();
                m.normals = SmoothShade.SmoothNormals(v.ToArray(), t.ToArray());
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>A ring of `sides` points round c in the plane of x and y (anticlockwise looking along x × y): a
        /// superellipse (pow 1: an ellipse; under 1: squarer) rx by ry, swelling by `top` on the +y side and `under` on the
        /// -y side.</summary>
        static Vector3[] Ring(Vector3 c, Vector3 x, Vector3 y, float rx, float ry, int sides, float pow = 1f, float top = 0f, float under = 0f)
        {
            var pts = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float an = Mathf.PI * 2f * s / sides + Mathf.PI / sides, cs = Mathf.Cos(an), sn = Mathf.Sin(an);
                float px = Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), pow) * rx;
                float py = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), pow) * ry;
                py += sn > 0f ? top * ry * sn * sn : -under * ry * sn * sn;
                pts[s] = c + x * px + y * py;
            }
            return pts;
        }

        static readonly Mesh[] s_Skins = new Mesh[2];

        /// <summary>The arm's skin (the right arm's, or its mirror for the left), in the rest pose, in metres: the forearm
        /// along -Z behind the wrist, the hand (HandScale bigger) in front of it.</summary>
        static Mesh SkinMesh(bool right)
        {
            int idx = right ? 0 : 1;
            if (s_Skins[idx] != null) return s_Skins[idx];
            var b = new SkinBuild();
            Palm(b);
            foreach (int d in s_Digits) Digit(b, d);
            var m = b.Make("alien arm skin", !right);
            s_Skins[idx] = m;
            return m;
        }

        /// <summary>The bare forearm, the wrist and the palm: one loft from off the screen to the knuckles - the alien's slender
        /// forearm, a narrow wrist, then the palm widening and flattening (its back gently domed, the knuckle line curved) -
        /// weighted from the forearm over to the hand across the wrist.</summary>
        static void Palm(SkinBuild b)
        {
            const int sides = 18;
            var rings = new List<Vector3[]>();
            var ws = new List<BoneWeight>();
            // the forearm to the wrist (metres): (z, half width, half thickness, the hand's share)
            var arm = new (float z, float w, float t, float h)[]
            {
                (-0.9f, 0.036f, 0.031f, 0f), (-0.55f, 0.034f, 0.029f, 0f), (-0.32f, 0.0345f, 0.0285f, 0f), (-0.2f, 0.0335f, 0.027f, 0f),
                (-0.12f, 0.0305f, 0.0245f, 0f), (-0.07f, 0.0278f, 0.0215f, 0f), (-0.035f, 0.0262f, 0.0196f, 0.12f),
                (-0.016f, 0.0256f, 0.0188f, 0.32f), (0f, 0.0254f, 0.0184f, 0.55f),
            };
            foreach (var r in arm)
            {
                rings.Add(Ring(new Vector3(0f, 0f, r.z), Vector3.right, Vector3.up, r.w, r.t, sides, 0.85f));
                ws.Add(BW(BoneArm, BoneHand, r.h));
            }
            // the hand (its own units, drawn HandScale bigger)
            float W(float z) => Mathf.Lerp(0.0212f, 0.0318f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(z / 0.05f)));
            float T(float z) => Mathf.Lerp(0.0152f, PalmHalf, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(z / 0.06f)));
            var zs = new[] { 0.012f, 0.024f, 0.036f, 0.048f, 0.06f, 0.07f, 0.078f, 0.0845f, 0.089f };
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i];
                float w = W(z), t = T(z);
                if (i == zs.Length - 2) { w *= 0.88f; t *= 0.84f; }
                if (i == zs.Length - 1) { w *= 0.62f; t *= 0.55f; }
                // the back of the hand domes a little; the heel of the hand is fuller underneath near the wrist
                float heel = Mathf.Sin(Mathf.Clamp01(z / 0.05f) * Mathf.PI) * 0.15f;
                var pts = Ring(new Vector3(0f, 0f, z), Vector3.right, Vector3.up, w, t, sides, 0.66f, 0.1f, heel);
                // the knuckle line curves back to either side of the middle finger
                float bend = Mathf.Clamp01((z - 0.055f) / 0.035f) * 0.0065f;
                for (int s = 0; s < sides; s++)
                {
                    float u = pts[s].x / Mathf.Max(1e-4f, w);
                    pts[s] = new Vector3(pts[s].x, pts[s].y, pts[s].z - bend * u * u) * HandScale;
                }
                rings.Add(pts);
                ws.Add(BW(BoneArm, BoneHand, i == 0 ? 0.8f : 1f));
            }
            b.Loft(rings, ws, new Vector3(0f, 0f, -0.91f), new Vector3(0f, 0f, 0.0915f * HandScale));
        }

        /// <summary>A finger (0-2) or the thumb (4): a rounded tube from inside the palm to its pad, a touch fuller at each
        /// knuckle, the tip a soft dome; each joint's ring shared half and half by the bones either side so it bends smoothly.</summary>
        static void Digit(SkinBuild b, int d)
        {
            const int sides = 12;
            bool thumb = d == 4;
            float L0 = Len(d, 0), L1 = Len(d, 1), L2 = Len(d, 2);
            float r0 = Rad(d, 0), r1 = Rad(d, 1), r2 = Rad(d, 2), r3 = Rad(d, 3);
            int j0 = BoneJ(d, 0), j1 = BoneJ(d, 1), j2 = BoneJ(d, 2);
            // (z along the digit from its root, radius, knuckle swell, the bones)
            var list = new List<(float z, float r, float bump, BoneWeight w)>
            {
                (thumb ? -0.012f : -0.016f, r0 * (thumb ? 0.9f : 1.1f), 0f, BW(BoneHand)),
                (-0.006f, r0 * 1.08f, 0f, BW(BoneHand)),
                (0f, r0 * 1.05f, thumb ? 0f : 0.07f, BW(BoneHand, j0, 0.5f)),
                (0.006f, r0, 0.03f, BW(j0)),
                (L0 * 0.55f, Mathf.Lerp(r0, r1, 0.55f) * (thumb ? 1f : 0.96f), 0f, BW(j0)),
                (L0 - 0.0045f, r1 * 0.99f, 0f, BW(j0)),
                (L0, r1 * 1.04f, 0.06f, BW(j0, j1, 0.5f)),
                (L0 + 0.0045f, r1, 0.02f, BW(j1)),
                (L0 + L1 * 0.5f, Mathf.Lerp(r1, r2, 0.5f) * 0.96f, 0f, BW(j1)),
                (L0 + L1 - 0.004f, r2 * 0.99f, 0f, BW(j1)),
                (L0 + L1, r2 * 1.03f, 0.05f, BW(j1, j2, 0.5f)),
                (L0 + L1 + 0.004f, r2, 0.01f, BW(j2)),
                (L0 + L1 + L2 * 0.45f, Mathf.Lerp(r2, r3, 0.5f) * 0.98f, 0f, BW(j2)),
            };
            float tip = L0 + L1 + L2 - r3 * 0.35f;
            list.Add((tip, r3 * 1.02f, 0f, BW(j2)));
            list.Add((tip + r3 * 0.45f, r3 * 0.87f, 0f, BW(j2)));
            list.Add((tip + r3 * 0.75f, r3 * 0.6f, 0f, BW(j2)));
            var root = s_Base[d];
            var rings = new List<Vector3[]>();
            var ws = new List<BoneWeight>();
            foreach (var e in list)
            {
                // a little wider than deep; the pad of the tip fuller underneath
                bool pad = e.z > tip - 0.001f;
                var c = root + new Vector3(0f, 0f, e.z);
                var pts = Ring(c, Vector3.right, Vector3.up, e.r * 1.04f, e.r * (thumb ? 0.9f : 0.93f), sides, 1f, e.bump, pad ? 0.1f : 0.03f);
                for (int s = 0; s < sides; s++) pts[s] *= HandScale;
                rings.Add(pts);
                ws.Add(e.w);
            }
            b.Loft(rings, ws, (root + new Vector3(0f, 0f, list[0].z - r0 * 0.5f)) * HandScale, (root + new Vector3(0f, 0f, tip + r3 * 0.95f)) * HandScale);
        }
    }
}
