using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The first-person arms (Settings > Display > Alien hands; off brings back the old square ones), built from scratch
    /// as ONE skinned mesh per arm like a real FPS rig: the forearm, the wrist, a broad palm, four fingers and a thumb all
    /// flow into each other (no separate tubes with seams at every joint), and each knuckle bends the skin round it. Your
    /// alien's skin in your team's colour, sturdy stylised proportions (thick, rounded fingers with knuckle bumps and
    /// soft pads, a domed back of the hand, a thumb with a fleshy base), and a dark suit sleeve with a cuff in your team's
    /// colour covering the forearm, so the arms come in from the bottom corners of the screen cleanly.
    ///
    /// The skeleton: the hand (scaled HandScale), each finger's three joints and the thumb's (one transform each, so they
    /// curl from their own knuckles - ViewModel.Fingers.cs poses them), and the forearm (from the wrist back towards the
    /// elbow; the sleeve rides on it). The skin is weighted half and half across every joint, and over a few centimetres at
    /// the wrist, so bends stay smooth. Settings > Display > SHADING smooths it (off: flat low-poly facets).
    ///
    /// The hand's own frame (the RIGHT hand; the left is its mirror image): the wrist at the origin, the fingers along +Z,
    /// the palm facing -Y, the thumb on the -X side. A finger curls with +X rotations (towards the palm), spreads with Y.
    /// Everything stays under the arm (m_R / m_L) so IsHand() and the post-fx layers see it as hand.
    /// </summary>
    public partial class ViewModel
    {
        // ---- measurements (metres, in the hand's own size - it's drawn HandScale bigger) ----
        /// <summary>Half the palm's thickness.</summary>
        const float PalmHalf = 0.0145f;
        /// <summary>How far in front of the wrist a handle held in the fist runs (under the palm's end, so the fingers wrap it from the knuckles).</summary>
        const float GripZ = 0.063f;
        /// <summary>Where each forearm heads, in the view (right hand; the left's is its mirror): down by your side, so the
        /// arms come in from the bottom corners of the screen like an FPS rig's instead of reaching across it.</summary>
        static readonly Vector3 k_ElbowAnchor = new Vector3(0.36f, -0.66f, -0.02f);
        /// <summary>The hand (everything from the wrist out, measured below) is drawn this much bigger, so it reads at
        /// first-person distance.</summary>
        const float HandScale = 1.3f;

        /// <summary>The knuckle (MCP) of each finger - index, middle, ring, little - and the thumb's root deep in the palm.</summary>
        static readonly Vector3[] s_Base =
        {
            new Vector3(-0.0262f, 0.0010f, 0.0745f),
            new Vector3(-0.0088f, 0.0016f, 0.0785f),
            new Vector3(0.0088f, 0.0010f, 0.0760f),
            new Vector3(0.0250f, -0.0010f, 0.0700f),
            new Vector3(-0.0230f, -0.0080f, 0.0170f),
        };
        /// <summary>Each digit's three bones (stylised: a little shorter and chunkier than real fingers).</summary>
        static readonly float[] s_Len =
        {
            0.035f, 0.022f, 0.018f,
            0.038f, 0.024f, 0.019f,
            0.036f, 0.023f, 0.018f,
            0.029f, 0.018f, 0.016f,
            0.032f, 0.027f, 0.022f, // (the thumb: its fleshy base, then two pieces)
        };
        /// <summary>Each digit's radius at its root, its two joints and its tip.</summary>
        static readonly float[] s_Rad =
        {
            0.0099f, 0.0091f, 0.0083f, 0.0079f,
            0.0102f, 0.0094f, 0.0085f, 0.0081f,
            0.0097f, 0.0089f, 0.0081f, 0.0077f,
            0.0086f, 0.0079f, 0.0072f, 0.0068f,
            0.0150f, 0.0118f, 0.0103f, 0.0097f,
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

        /// <summary>Builds the arm under `arm` (hidden while the square hands are picked): its skeleton, the skinned skin
        /// on it, and the sleeve and cuff on the forearm.</summary>
        static AlienArm BuildAlienArm(Transform arm, HandColorHook hook, bool right)
        {
            float sx = right ? 1f : -1f;
            var a = new AlienArm { Right = right };
            a.Root = Node(arm, "alien", Vector3.zero);
            a.Hand = Node(a.Root, "hand", Vector3.zero);
            a.Hand.localScale = Vector3.one * HandScale;
            for (int d = 0; d < 5; d++)
            {
                var j0 = Node(a.Hand, d < 4 ? "finger" + d : "thumb", new Vector3(s_Base[d].x * sx, s_Base[d].y, s_Base[d].z));
                var j1 = Node(j0, "j1", new Vector3(0f, 0f, Len(d, 0)));
                var j2 = Node(j1, "j2", new Vector3(0f, 0f, Len(d, 1)));
                a.J[d, 0] = j0; a.J[d, 1] = j1; a.J[d, 2] = j2;
            }
            a.Forearm = Node(a.Root, "forearm", Vector3.zero);

            // the skin: one mesh on the whole skeleton (bound in this rest pose: everything straight, the forearm behind the wrist)
            var bones = new Transform[2 + 15];
            bones[BoneHand] = a.Hand;
            bones[BoneArm] = a.Forearm;
            for (int d = 0; d < 5; d++) for (int j = 0; j < 3; j++) bones[BoneJ(d, j)] = a.J[d, j];
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

            // the sleeve and its cuff, riding on the forearm (slightly oval like the arm in it)
            var sleeve = Art.Part(a.Forearm, SleeveMesh(), Color.white, Vector3.zero, new Vector3(1.08f, 0.93f, 1f), Vector3.zero, false, null, "sleeve");
            hook.Track(sleeve, HandColorHook.Sleeve);
            var cuff = Art.Part(a.Forearm, CuffMesh(), Color.white, Vector3.zero, new Vector3(1.08f, 0.93f, 1f), Vector3.zero, false, null, "cuff");
            hook.Track(cuff, HandColorHook.Cuff);
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

        // ======== the meshes ========

        const int BoneHand = 0, BoneArm = 1;
        static int BoneJ(int d, int j) => 2 + d * 3 + j;

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

        /// <summary>A mesh being built: flat triangles (each its own three corners, so it's lit as low-poly facets until
        /// SHADING smooths it), each corner with its bones.</summary>
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
                m.RecalculateNormals();
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
            for (int d = 0; d < 5; d++) Digit(b, d);
            var m = b.Make("alien arm skin", !right);
            s_Skins[idx] = m;
            return m;
        }

        /// <summary>The forearm, wrist and palm: one loft from inside the sleeve to the knuckles, rounded-box sections
        /// (round at the wrist, wide and flat over the palm, the back of the hand domed and the knuckle line curved), weighted
        /// from the forearm over to the hand across the wrist.</summary>
        static void Palm(SkinBuild b)
        {
            const int sides = 16;
            var rings = new List<Vector3[]>();
            var ws = new List<BoneWeight>();
            // inside the sleeve to the wrist (metres): (z, half width, half thickness, the hand's share)
            var arm = new (float z, float w, float t, float h)[]
            {
                (-0.14f, 0.036f, 0.029f, 0f), (-0.09f, 0.034f, 0.027f, 0f), (-0.05f, 0.0322f, 0.0245f, 0f),
                (-0.03f, 0.031f, 0.0232f, 0.15f), (-0.014f, 0.0303f, 0.0222f, 0.35f), (0f, 0.0302f, 0.0212f, 0.55f),
            };
            foreach (var r in arm)
            {
                rings.Add(Ring(new Vector3(0f, 0f, r.z), Vector3.right, Vector3.up, r.w, r.t, sides, 0.8f));
                ws.Add(BW(BoneArm, BoneHand, r.h));
            }
            // the hand (its own units, drawn HandScale bigger)
            float W(float z) => Mathf.Lerp(0.0235f, 0.0385f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(z / 0.05f)));
            float T(float z) => Mathf.Lerp(0.0162f, 0.0138f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(z / 0.08f)));
            var zs = new[] { 0.012f, 0.024f, 0.036f, 0.048f, 0.06f, 0.07f, 0.078f, 0.0845f, 0.089f };
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i];
                float w = W(z), t = T(z);
                if (i == zs.Length - 2) { w *= 0.9f; t *= 0.84f; }
                if (i == zs.Length - 1) { w *= 0.66f; t *= 0.55f; }
                // the back of the hand domes; the heel of the hand is fuller underneath near the wrist
                float heel = Mathf.Sin(Mathf.Clamp01(z / 0.05f) * Mathf.PI) * 0.18f;
                var pts = Ring(new Vector3(0f, 0f, z), Vector3.right, Vector3.up, w, t, sides, 0.62f, 0.12f, heel);
                // the knuckle line curves back towards the little finger and the index (the middle finger's knuckle leads)
                float bend = Mathf.Clamp01((z - 0.055f) / 0.035f) * 0.0075f;
                for (int s = 0; s < sides; s++)
                {
                    float u = pts[s].x / Mathf.Max(1e-4f, w);
                    float lean = u > 0f ? u * u * 1.3f : u * u * 0.8f; // (the little finger's side sits further back)
                    pts[s] = new Vector3(pts[s].x, pts[s].y, pts[s].z - bend * lean) * HandScale;
                }
                rings.Add(pts);
                ws.Add(BW(BoneArm, BoneHand, i == 0 ? 0.8f : 1f));
            }
            b.Loft(rings, ws, new Vector3(0f, 0f, -0.145f), new Vector3(0f, 0f, 0.0915f * HandScale));
        }

        /// <summary>A finger (0-3) or the thumb (4): a rounded tube from inside the palm to its pad, swelling a little at
        /// each knuckle, thinner between, the tip a soft dome; each joint's ring shared half and half by the bones either
        /// side of it so it bends smoothly.</summary>
        static void Digit(SkinBuild b, int d)
        {
            const int sides = 10;
            bool thumb = d == 4;
            float L0 = Len(d, 0), L1 = Len(d, 1), L2 = Len(d, 2);
            float r0 = Rad(d, 0), r1 = Rad(d, 1), r2 = Rad(d, 2), r3 = Rad(d, 3);
            int j0 = BoneJ(d, 0), j1 = BoneJ(d, 1), j2 = BoneJ(d, 2);
            // (z along the digit from its root, radius, knuckle bump, the bones)
            var list = new List<(float z, float r, float bump, BoneWeight w)>
            {
                (thumb ? -0.012f : -0.016f, r0 * (thumb ? 0.9f : 1.12f), 0f, BW(BoneHand)),
                (-0.006f, r0 * 1.1f, 0f, BW(BoneHand)),
                (0f, r0 * 1.08f, thumb ? 0f : 0.14f, BW(BoneHand, j0, 0.5f)),
                (0.006f, r0, 0.05f, BW(j0)),
                (L0 * 0.55f, Mathf.Lerp(r0, r1, 0.55f) * (thumb ? 1f : 0.94f), 0f, BW(j0)),
                (L0 - 0.0045f, r1 * 0.98f, 0f, BW(j0)),
                (L0, r1 * 1.07f, 0.12f, BW(j0, j1, 0.5f)),
                (L0 + 0.0045f, r1, 0.03f, BW(j1)),
                (L0 + L1 * 0.5f, Mathf.Lerp(r1, r2, 0.5f) * 0.94f, 0f, BW(j1)),
                (L0 + L1 - 0.004f, r2 * 0.98f, 0f, BW(j1)),
                (L0 + L1, r2 * 1.06f, 0.1f, BW(j1, j2, 0.5f)),
                (L0 + L1 + 0.004f, r2, 0.02f, BW(j2)),
                (L0 + L1 + L2 * 0.45f, Mathf.Lerp(r2, r3, 0.5f) * 0.97f, 0f, BW(j2)),
            };
            float tip = L0 + L1 + L2 - r3 * 0.35f;
            list.Add((tip, r3 * 1.02f, 0f, BW(j2)));
            list.Add((tip + r3 * 0.45f, r3 * 0.86f, 0f, BW(j2)));
            list.Add((tip + r3 * 0.75f, r3 * 0.58f, 0f, BW(j2)));
            var root = s_Base[d];
            var rings = new List<Vector3[]>();
            var ws = new List<BoneWeight>();
            foreach (var e in list)
            {
                // a little wider than deep; the pad of the tip fuller underneath
                bool pad = e.z > tip - 0.001f;
                var c = root + new Vector3(0f, 0f, e.z);
                var pts = Ring(c, Vector3.right, Vector3.up, e.r * 1.04f, e.r * (thumb ? 0.9f : 0.93f), sides, 1f, e.bump, pad ? 0.12f : 0.04f);
                for (int s = 0; s < sides; s++) pts[s] *= HandScale;
                rings.Add(pts);
                ws.Add(e.w);
            }
            b.Loft(rings, ws, (root + new Vector3(0f, 0f, list[0].z - r0 * 0.5f)) * HandScale, (root + new Vector3(0f, 0f, tip + r3 * 0.95f)) * HandScale);
        }

        static Mesh s_Sleeve, s_Cuff;

        /// <summary>The suit's sleeve (on the forearm, along -Z from just behind the wrist, off the screen): loose fabric
        /// with a few soft folds, widening towards the elbow.</summary>
        static Mesh SleeveMesh()
        {
            if (s_Sleeve != null) return s_Sleeve;
            const int sides = 14;
            var b = new SkinBuild();
            var rings = new List<Vector3[]>();
            var ws = new List<BoneWeight>();
            var prof = new (float z, float r)[] { (-0.062f, 0.0425f), (-0.1f, 0.0445f), (-0.14f, 0.0455f), (-0.18f, 0.046f), (-0.24f, 0.0475f), (-0.32f, 0.0495f), (-0.45f, 0.052f), (-0.7f, 0.054f), (-1.05f, 0.055f) };
            for (int i = prof.Length - 1; i >= 0; i--) // (from the elbow end forwards, so the loft faces out)
            {
                var pts = Ring(new Vector3(0f, 0f, prof[i].z), Vector3.right, Vector3.up, prof[i].r, prof[i].r, sides);
                // soft folds: the fabric bunches a little, differently at each ring
                for (int s = 0; s < sides; s++)
                {
                    float an = Mathf.PI * 2f * s / sides;
                    float fold = i == 0 ? 0f : Mathf.Sin(an * 3f + i * 1.7f) * 0.05f + Mathf.Sin(an * 5f + i * 2.9f) * 0.025f;
                    var p = pts[s];
                    pts[s] = new Vector3(p.x * (1f + fold), p.y * (1f + fold), p.z + (i == 0 ? 0f : Mathf.Sin(an * 2f + i) * 0.006f));
                }
                rings.Add(pts);
                ws.Add(BW(0));
            }
            b.Loft(rings, ws, new Vector3(0f, 0f, -1.06f), new Vector3(0f, 0f, -0.062f));
            s_Sleeve = b.Make("sleeve", false);
            return s_Sleeve;
        }

        /// <summary>The cuff at the sleeve's end (the team's colour): a thick band with a rolled lip, open at the wrist end
        /// round the arm.</summary>
        static Mesh CuffMesh()
        {
            if (s_Cuff != null) return s_Cuff;
            const int sides = 14;
            var b = new SkinBuild();
            // a lathe round the arm from the inside lip, over the rolled edge, along the band and back into the sleeve
            var prof = new (float z, float r)[] { (-0.042f, 0.0335f), (-0.036f, 0.0385f), (-0.035f, 0.0438f), (-0.039f, 0.047f), (-0.047f, 0.0477f), (-0.064f, 0.0474f), (-0.069f, 0.0442f), (-0.071f, 0.0405f) };
            var rings = new List<Vector3[]>();
            foreach (var p in prof) rings.Add(Ring(new Vector3(0f, 0f, p.z), Vector3.right, Vector3.up, p.r, p.r, sides));
            var w = BW(0);
            for (int r = 0; r + 1 < rings.Count; r++)
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    // (the profile runs forwards then back: wound so its outside faces out)
                    b.Tri(rings[r][s], w, rings[r + 1][s], w, rings[r][s1], w);
                    b.Tri(rings[r][s1], w, rings[r + 1][s], w, rings[r + 1][s1], w);
                }
            s_Cuff = b.Make("cuff", false);
            return s_Cuff;
        }
    }
}
