using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The SMOOTH first-person arm (Settings > Display > Hands: Smooth - the default): the player aliens' own hand, as ONE
    /// continuous surface from the fingertips to the elbow. The aliens are lanky, flat-sided and long-fingered, in one deep
    /// colour, so that is what this is: a flat block of a palm, three long fingers and a long thumb that are each a single
    /// straight bar with flat sides and softened edges tapering to a blunt point (no balls at the joints, no bulges), and
    /// a four-sided forearm with rounded corners. It is sculpted as a signed-distance field, each part blended a little
    /// into the next (webbing at the fingers' roots, the fingers running back into the hand as low ridges), then turned
    /// into a mesh once, in the background as the game starts (about a second):
    ///  - surface nets over a 1.9 mm grid (long cells down the forearm), every point then pulled onto the true surface;
    ///  - the normals are the field's own gradient, so flat faces shade flat and edges turn cleanly whatever the triangles are;
    ///  - baked into the vertex colours (HandSkin.shader reads them): how enclosed each point is (creases darker), how
    ///    the surface curves (edges a touch brighter, hollows darker), which side is the palm side (lighter);
    ///  - skinned to the pose skeleton ViewModel.Fingers.cs already drives (so every item's grip works as it does for the
    ///    Alien arms), with three in-between bones per joint that each take a share of the joint's turn: a bent finger
    ///    keeps its thickness at the joint instead of pinching flat the way plain two-bone skinning does.
    /// The measurements below are also the pose skeleton's while this style is picked (ViewModel.UseProfile).
    /// The hand's frame is ViewModel.Hands.cs's: the RIGHT hand, wrist at the origin, fingers along +Z, the back of the
    /// hand +Y, the thumb on the -X side; the left hand is the mirror image.
    /// </summary>
    public static class SmoothHand
    {
        // ======== tuning (for developing the shape: -handtune FILE, lines of "name = value") ========
        static readonly Dictionary<string, float> s_Tune = LoadTune();

        static Dictionary<string, float> LoadTune()
        {
            var d = new Dictionary<string, float>();
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] != "-handtune" || !System.IO.File.Exists(args[i + 1])) continue;
                    foreach (var line in System.IO.File.ReadAllLines(args[i + 1]))
                    {
                        var p = line.Split('=');
                        if (p.Length == 2 && float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) d[p[0].Trim()] = v;
                    }
                }
            }
            catch (Exception) { /* (no file, no tuning) */ }
            return d;
        }

        /// <summary>A number of the shape or the look: its default, or the -handtune file's.</summary>
        public static float P(string key, float def) => s_Tune.TryGetValue(key, out float v) ? v : def;

        // ======== the skeleton's measurements (metres) ========
        /// <summary>The whole arm is sculpted at about life size (the numbers below), then made this much bigger: the
        /// game's tools are chunky toys with handles 5-6 cm thick, and a life-size hand looks lost on them.</summary>
        public static readonly float Size = P("size", 1.15f);
        static readonly float FL = P("finger.len", 1f), FW = P("finger.wide", 1f);
        static readonly float Gap = P("finger.gap", 0.0235f);

        static Vector3[] Scaled(Vector3[] a) { var r = new Vector3[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] * Size; return r; }
        static float[] Scaled(float[] a, float k = 1f) { var r = new float[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] * k; return r; }

        static readonly Vector3[] BaseU =
        {
            new Vector3(-Gap, 0.0005f, 0.0840f),
            new Vector3(0f, 0.0010f, 0.0880f),
            new Vector3(Gap, 0.0000f, 0.0820f),
            new Vector3(Gap, 0.0000f, 0.0820f),
            new Vector3(P("thumb.x", -0.0225f), P("thumb.y", -0.0065f), P("thumb.z", 0.0190f)),
        };
        // (long alien fingers: each longer than the palm)
        static readonly float[] LenU =
        {
            0.0360f * FL, 0.0260f * FL, 0.0220f * FL,
            0.0390f * FL, 0.0280f * FL, 0.0230f * FL,
            0.0340f * FL, 0.0250f * FL, 0.0210f * FL,
            0.0340f * FL, 0.0250f * FL, 0.0210f * FL,
            0.0340f, 0.0280f, 0.0240f,
        };
        /// <summary>Half of each digit's width (across the hand) at its root, its two joints and where its tip starts.</summary>
        static readonly float[] WideU = Scaled(new[]
        {
            0.0105f, 0.0096f, 0.0086f, 0.0070f,
            0.0108f, 0.0098f, 0.0088f, 0.0072f,
            0.0102f, 0.0093f, 0.0084f, 0.0068f,
            0.0102f, 0.0093f, 0.0084f, 0.0068f,
            0.0125f, 0.0106f, 0.0093f, 0.0077f,
        }, FW);
        /// <summary>Half of each digit's depth (pad to back) at the same places: what the grip solver takes as its radius.</summary>
        static readonly float[] RadU = Scaled(new[]
        {
            0.0096f, 0.0087f, 0.0077f, 0.0061f,
            0.0099f, 0.0089f, 0.0079f, 0.0063f,
            0.0093f, 0.0084f, 0.0075f, 0.0059f,
            0.0093f, 0.0084f, 0.0075f, 0.0059f,
            0.0114f, 0.0096f, 0.0083f, 0.0067f,
        }, FW);
        static readonly float PalmHalfU = P("palm.half", 0.0125f);
        /// <summary>How far past its last bone a digit's tip runs (narrowing to a blunt point).</summary>
        const float TipU = 0.0070f;

        /// <summary>The knuckle of each finger - index, middle, outer - (slot 3 unused) and the thumb's root low in the palm.</summary>
        public static readonly Vector3[] Base = Scaled(BaseU);
        /// <summary>Each digit's three bones.</summary>
        public static readonly float[] Len = Scaled(LenU, Size);
        /// <summary>Each digit's radius (half its depth) at its root, its two joints and its tip.</summary>
        public static readonly float[] Rad = Scaled(RadU, Size);
        /// <summary>Half the palm's thickness, where the middle knuckle is, and where a handle in the fist runs.</summary>
        public static readonly float PalmHalf = PalmHalfU * Size, KnuckleZ = Base[1].z, GripZ = Base[1].z * 0.8f;

        /// <summary>The pose the mesh is made (and bound) in: the fingers fanned apart, so each is its own shape with
        /// webbing only at its root, and the thumb down and out to its side.</summary>
        public static readonly float[] RestSpread = { -P("rest.spread", 12f), 0f, P("rest.spread", 12f), 0f, 0f };
        public static readonly Quaternion RestThumb = Quaternion.LookRotation(new Vector3(-0.42f, -0.50f, 0.76f).normalized, new Vector3(-0.6f, 0.62f, -0.5f));

        // ---- the bones (ViewModel.Smooth.cs builds them in this order) ----
        /// <summary>The hand, the forearm, three in-between bones across the wrist, then for each digit (the fingers, then
        /// the thumb) and each of its joints: three in-between bones and the joint itself.</summary>
        public const int BoneHand = 0, BoneArm = 1, BoneWrist = 2, BoneDigits = 5, BoneCount = BoneDigits + 4 * 3 * 4;
        /// <summary>A digit's slot (0-2 the fingers, 3 the thumb - digit 4).</summary>
        public static int Slot(int d) => d == 4 ? 3 : d;
        /// <summary>The first of joint j's four bones: its in-between bones at +0..+2 (a quarter, half, three quarters of
        /// its turn), the joint itself at +3.</summary>
        public static int JointBase(int slot, int j) => BoneDigits + (slot * 3 + j) * 4;

        // ======== the field ========

        /// <summary>A digit: one long tapering bar with flat sides and softened edges - a little wider underneath (the pad)
        /// than on its back - narrowing to a blunt point. No balls at the joints, no bulges: the player aliens' fingers are
        /// plain tapered prisms, and these are the same idea with the corners taken off.</summary>
        sealed class Bar
        {
            public Vector3 P0, X, Y, Z;
            /// <summary>Stations along it (from just inside the palm to the tip): half its width and half its depth there.</summary>
            public float[] T, A, B;
            /// <summary>How much of the corners is rounded off (a share of the smaller half size).</summary>
            public float Edge = 0.36f;
            /// <summary>The back is this much narrower than the middle, the pad this much wider.</summary>
            public float Wedge = 0.13f;
            /// <summary>Every edge is rounded by at least this (the tip's too).</summary>
            const float Soft = 0.0016f;

            public float D(float px, float py, float pz)
            {
                float rx = px - P0.x, ry = py - P0.y, rz = pz - P0.z;
                float t = rx * Z.x + ry * Z.y + rz * Z.z, x = rx * X.x + ry * X.y + rz * X.z, y = rx * Y.x + ry * Y.y + rz * Y.z;
                int n = T.Length;
                float tc = t < T[0] ? T[0] : t > T[n - 1] ? T[n - 1] : t;
                int i = 0;
                while (i < n - 2 && tc > T[i + 1]) i++;
                float u = (tc - T[i]) / (T[i + 1] - T[i]);
                float a = A[i] + (A[i + 1] - A[i]) * u - Soft, b = B[i] + (B[i + 1] - B[i]) * u - Soft;
                float yn = y / b; yn = yn < -1f ? -1f : yn > 1f ? 1f : yn;
                float w = 1f - Wedge * yn;
                a *= w;
                float rr = Edge * Math.Min(a, b);
                float qx = Math.Abs(x) - (a - rr), qy = Math.Abs(y) - (b - rr);
                float ox = qx > 0f ? qx : 0f, oy = qy > 0f ? qy : 0f;
                float d2 = (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - rr;
                float dz = Math.Max(T[0] - t, t - (T[n - 1] - Soft));
                float d = dz <= 0f ? Math.Max(d2, dz) : d2 > 0f ? (float)Math.Sqrt(d2 * d2 + dz * dz) : dz;
                return d - Soft;
            }
        }

        /// <summary>The smaller of two distances, rounded off where they meet (over k): shapes melt into each other.</summary>
        static float Smin(float a, float b, float k)
        {
            float h = k - Math.Abs(a - b);
            if (h <= 0f) return a < b ? a : b;
            h /= k;
            return (a < b ? a : b) - h * h * h * k * (1f / 6f);
        }
        static float S01(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }
        static float S(float x, float a, float b) => S01((x - a) / (b - a));

        /// <summary>A digit's frame in the rest pose: its root, its line, the way its back faces, its side.</summary>
        sealed class DigitRest
        {
            public Vector3 P0, Z, Y, X;
            public float L0, L1, L2, R0, R1, R2, R3, Tip;
            public float Total => L0 + L1 + L2;
        }

        sealed class Field
        {
            /// <summary>The digits as the mesh has them (the size the arm's drawn).</summary>
            public readonly DigitRest[] Rest = new DigitRest[4];
            readonly Bar[] m_Digit = new Bar[4];
            readonly Vector3[] m_DC = new Vector3[4];
            readonly float[] m_DR = new float[4];
            // the palm: a flat block, a trapezoid seen from above (narrow at the wrist, wide at the knuckles), thicker
            // at the heel than at the knuckles, its edges bevelled
            readonly float m_Zc, m_He, m_W1, m_W2, m_TopW, m_TopK, m_Re, m_Rc, m_Z0, m_Z1;
            // the wrist and forearm: a squared-off tube, (z, half width, half thickness)
            readonly float[] m_Tz, m_Ta, m_Tb, m_Ma, m_Mb;
            readonly float m_Square = P("arm.square", 4f);
            /// <summary>Behind this the arm is only the forearm tube (nothing of the hand reaches it).</summary>
            public const float ZFast = -0.075f;
            public const float ZEnd = -0.9f;
            /// <summary>Where the forearm's tube ends: thin by then, and deep inside the palm.</summary>
            const float TubeFront = 0.062f;
            static readonly float KWeb = P("k.web", 0.003f), KRoot = P("k.root", 0.0045f), KThumb = P("k.thumb", 0.007f), KWrist = P("k.wrist", 0.009f);

            public Field()
            {
                for (int q = 0; q < 4; q++)
                {
                    int d = q == 3 ? 4 : q;
                    var rot = d < 4 ? Quaternion.Euler(0f, RestSpread[d], 0f) : RestThumb;
                    Rest[q] = new DigitRest
                    {
                        P0 = Base[d], Z = rot * Vector3.forward, Y = rot * Vector3.up, X = rot * Vector3.right,
                        L0 = Len[d * 3], L1 = Len[d * 3 + 1], L2 = Len[d * 3 + 2],
                        R0 = Rad[d * 4], R1 = Rad[d * 4 + 1], R2 = Rad[d * 4 + 2], R3 = Rad[d * 4 + 3], Tip = TipU * Size,
                    };
                    // (the shape itself is sculpted at life size: Eval scales it up)
                    float l0 = LenU[d * 3], l1 = LenU[d * 3 + 1], l2 = LenU[d * 3 + 2], tot = l0 + l1 + l2;
                    float w0 = WideU[d * 4], w1 = WideU[d * 4 + 1], w2 = WideU[d * 4 + 2], w3 = WideU[d * 4 + 3];
                    float h0 = RadU[d * 4], h1 = RadU[d * 4 + 1], h2 = RadU[d * 4 + 2], h3 = RadU[d * 4 + 3];
                    m_Digit[q] = new Bar
                    {
                        P0 = BaseU[d], Z = rot * Vector3.forward, Y = rot * Vector3.up, X = rot * Vector3.right,
                        T = new[] { d < 4 ? -0.042f : -0.016f, 0f, l0, l0 + l1, tot, tot + TipU },
                        A = new[] { d < 4 ? w0 * 0.78f : w0, w0, w1, w2, w3, w3 * 0.6f },
                        B = new[] { d < 4 ? h0 * 0.62f : h0, h0, h1, h2, h3, h3 * 0.58f },
                    };
                    m_DC[q] = BaseU[d] + rot * Vector3.forward * (tot * 0.5f);
                    m_DR[q] = tot * 0.5f + TipU + w0 + 0.006f;
                }
                m_Z0 = 0.0020f; m_Z1 = BaseU[1].z + 0.0005f;
                m_Zc = (m_Z0 + m_Z1) * 0.5f; m_He = (m_Z1 - m_Z0) * 0.5f;
                m_W1 = P("palm.w1", 0.0270f); m_W2 = Gap + WideU[0] + 0.0012f;
                m_TopW = P("palm.top.wrist", 0.0138f); m_TopK = P("palm.top.knuckle", 0.0092f);
                m_Re = P("palm.bevel", 0.0044f); m_Rc = 0.0085f;
                float aw = P("arm.w", 1f), at = P("arm.t", 1f);
                m_Tz = new[] { ZEnd - 0.05f, -0.300f, -0.130f, -0.060f, -0.022f, 0.000f, 0.030f, TubeFront };
                m_Ta = new[] { 0.0385f * aw, 0.0370f * aw, 0.0345f * aw, 0.0292f, 0.0260f, 0.0258f, 0.0200f, 0.0110f };
                m_Tb = new[] { 0.0335f * at, 0.0322f * at, 0.0292f * at, 0.0228f, 0.0188f, 0.0168f, 0.0100f, 0.0040f };
                int n = m_Tz.Length;
                m_Ma = new float[n]; m_Mb = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                    m_Ma[i] = (m_Ta[b] - m_Ta[a]) / (m_Tz[b] - m_Tz[a]);
                    m_Mb[i] = (m_Tb[b] - m_Tb[a]) / (m_Tz[b] - m_Tz[a]);
                }
            }

            /// <summary>The forearm's half width and half thickness at z (a smooth curve through the table).</summary>
            void TubeAt(float z, out float a, out float b)
            {
                int n = m_Tz.Length;
                if (z <= m_Tz[0]) { a = m_Ta[0]; b = m_Tb[0]; return; }
                if (z >= m_Tz[n - 1]) { a = m_Ta[n - 1]; b = m_Tb[n - 1]; return; }
                int i = 0;
                while (i < n - 2 && z > m_Tz[i + 1]) i++;
                float h = m_Tz[i + 1] - m_Tz[i], t = (z - m_Tz[i]) / h;
                float t2 = t * t, t3 = t2 * t;
                float h00 = 2f * t3 - 3f * t2 + 1f, h10 = t3 - 2f * t2 + t, h01 = -2f * t3 + 3f * t2, h11 = t3 - t2;
                a = h00 * m_Ta[i] + h10 * h * m_Ma[i] + h01 * m_Ta[i + 1] + h11 * h * m_Ma[i + 1];
                b = h00 * m_Tb[i] + h10 * h * m_Mb[i] + h01 * m_Tb[i + 1] + h11 * h * m_Mb[i + 1];
            }

            /// <summary>The forearm: its section a square with well-rounded corners (a superellipse), like the player
            /// aliens' four-sided arms.</summary>
            float Tube(float x, float y, float z)
            {
                TubeAt(z, out float a, out float b);
                double xa = Math.Abs(x) / a, yb = Math.Abs(y) / b, n = m_Square;
                double xn = Math.Pow(xa, n), yn = Math.Pow(yb, n);
                double q = Math.Pow(xn + yn, 1.0 / n);
                float d;
                if (q < 1e-4) d = -Math.Min(a, b);
                else
                {
                    // (the distance, near enough: how far past 1 the section's own measure is, over how fast that grows)
                    double gx = (xa > 1e-9 ? xn / xa : 0.0) / a, gy = (yb > 1e-9 ? yn / yb : 0.0) / b;
                    double g = Math.Sqrt(gx * gx + gy * gy) * Math.Pow(q, 1.0 - n);
                    d = (float)((q - 1.0) / Math.Max(g, 1e-9));
                }
                float front = z - TubeFront; // (it stops inside the palm)
                if (front > d) d = front;
                float back = ZEnd + 0.014f - z; // (and is closed off at the elbow end, far out of sight)
                return back > d ? back : d;
            }

            float Slab(float x, float y, float z)
            {
                // the outline: a trapezoid with rounded corners, narrow at the wrist and wide at the knuckles
                float r1 = m_W1 - m_Rc, r2 = m_W2 - m_Rc, he = m_He - m_Rc;
                float px = Math.Abs(x), py = z - m_Zc;
                float k2x = r2 - r1, k2y = 2f * he;
                float cax = px - Math.Min(px, py < 0f ? r1 : r2), cay = Math.Abs(py) - he;
                float t = ((r2 - px) * k2x + (he - py) * k2y) / (k2x * k2x + k2y * k2y);
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float cbx = px - r2 + k2x * t, cby = py - he + k2y * t;
                float s = cbx < 0f && cay < 0f ? -1f : 1f;
                float d2 = s * (float)Math.Sqrt(Math.Min(cax * cax + cay * cay, cbx * cbx + cby * cby)) - m_Rc;
                // its thickness: flat underneath (where things are held against it), the back sloping down from the heel
                // to the knuckles; the edges bevelled
                float k = (z - m_Z0) / (m_Z1 - m_Z0); k = k < 0f ? 0f : k > 1f ? 1f : k;
                float top = m_TopW + (m_TopK - m_TopW) * k;
                float yc = (top - PalmHalfU) * 0.5f, hy = (top + PalmHalfU) * 0.5f;
                float wx = d2 + m_Re, wy = Math.Abs(y - yc) - hy + m_Re;
                float ox = wx > 0f ? wx : 0f, oy = wy > 0f ? wy : 0f;
                return Math.Min(Math.Max(wx, wy), 0f) + (float)Math.Sqrt(ox * ox + oy * oy) - m_Re;
            }

            /// <summary>The distance to the arm's surface (negative inside). g, if given: the distance to each part on its
            /// own - the body (palm, wrist, forearm), the three fingers, the thumb.</summary>
            public float Eval(float x, float y, float z, float[] g)
            {
                float inv = 1f / Size;
                float d = EvalU(x * inv, y * inv, z * inv, g) * Size;
                if (g != null) for (int i = 0; i < 5; i++) g[i] *= Size;
                return d;
            }

            /// <summary>The same at life size (the units the arm's sculpted in).</summary>
            float EvalU(float x, float y, float z, float[] g)
            {
                float tube = Tube(x, y, z);
                if (z < ZFast)
                {
                    if (g != null) { g[0] = tube; g[1] = g[2] = g[3] = g[4] = 1f; }
                    return tube;
                }
                float body = Smin(Slab(x, y, z), tube, KWrist);
                if (g != null) g[0] = body;
                float all = body, fing = 1e9f;
                for (int q = 0; q < 4; q++)
                {
                    float dx = x - m_DC[q].x, dy = y - m_DC[q].y, dz = z - m_DC[q].z;
                    float dd = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) - m_DR[q];
                    if (dd < 0.03f) dd = m_Digit[q].D(x, y, z);
                    if (g != null) g[1 + q] = dd;
                    if (q < 3) fing = Smin(fing, dd, KWeb);
                    else { all = Smin(all, fing, KRoot); all = Smin(all, dd, KThumb); }
                }
                return all;
            }

            /// <summary>The field's slope at p (pointing out of the surface) and, in d, its value there.</summary>
            public Vector3 Grad(Vector3 p, out float d)
            {
                const float e = 0.0003f;
                float a = Eval(p.x + e, p.y - e, p.z - e, null), b = Eval(p.x - e, p.y - e, p.z + e, null),
                      c = Eval(p.x - e, p.y + e, p.z - e, null), f = Eval(p.x + e, p.y + e, p.z + e, null);
                d = (a + b + c + f) * 0.25f;
                return new Vector3(a - b - c + f, -a - b + c + f, -a + b - c + f) * (0.25f / e);
            }
        }

        // ======== the mesh ========

        sealed class Raw
        {
            public Vector3[] V, N;
            public Color32[] C;
            public Vector2[] UV;
            public BoneWeight[] W;
            public int[] T;
            public float Ms;
        }

        static Task<Raw> s_Task;
        static readonly Mesh[] s_Mesh = new Mesh[2];
        static bool s_Failed;

        /// <summary>Starts sculpting the arm in the background (it takes a moment; the game starts it as it loads).</summary>
        public static void Prewarm()
        {
            if (s_Task == null) s_Task = Task.Run(Build);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() => Prewarm();

        /// <summary>The arm's skin for one side in the rest pose (null if it couldn't be made).</summary>
        public static Mesh Skin(bool right)
        {
            int idx = right ? 0 : 1;
            if (s_Mesh[idx] != null || s_Failed) return s_Mesh[idx];
            Prewarm();
            Raw raw;
            try { raw = s_Task.Result; }
            catch (Exception e) { s_Failed = true; Debug.LogError("[hands] the smooth arm couldn't be made: " + e); return null; }
            for (int side = 0; side < 2; side++)
            {
                bool mirror = side == 1;
                int n = raw.V.Length;
                var v = new Vector3[n]; var nr = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    v[i] = mirror ? new Vector3(-raw.V[i].x, raw.V[i].y, raw.V[i].z) : raw.V[i];
                    nr[i] = mirror ? new Vector3(-raw.N[i].x, raw.N[i].y, raw.N[i].z) : raw.N[i];
                }
                var t = (int[])raw.T.Clone();
                if (mirror) for (int i = 0; i + 2 < t.Length; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                // ("ArmR" / "ArmL": SmoothShade leaves a mesh of that name alone - it's smooth already)
                var m = new Mesh { name = mirror ? "ArmL smooth" : "ArmR smooth" };
                if (n > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.vertices = v;
                m.normals = nr;
                m.colors32 = raw.C;
                m.uv = raw.UV;
                m.boneWeights = raw.W;
                m.triangles = t;
                m.RecalculateBounds();
                s_Mesh[side] = m;
            }
            Debug.Log($"[hands] the smooth arm: {raw.V.Length} points, {raw.T.Length / 3} triangles, sculpted in {raw.Ms:0} ms");
            return s_Mesh[idx];
        }

        static float[] Axis(float lo, float hi, float h)
        {
            int n = (int)Math.Ceiling((hi - lo) / h);
            var a = new float[n + 1];
            for (int i = 0; i <= n; i++) a[i] = lo + i * h;
            return a;
        }

        static Raw Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var f = new Field();
            float h = P("cell", 0.0019f);
            float sz = Size;

            // ---- the grid: fine over the hand, long cells down the forearm ----
            float xLo = -0.05f * sz, xHi = 0.05f * sz, yLo = -0.045f * sz, yHi = 0.045f * sz, zHi = 0.1f * sz;
            for (int q = 0; q < 4; q++)
            {
                var r = f.Rest[q];
                var tip = r.P0 + r.Z * (r.Total + r.Tip);
                foreach (var p in new[] { r.P0, tip })
                {
                    xLo = Math.Min(xLo, p.x - r.R0 - 0.004f); xHi = Math.Max(xHi, p.x + r.R0 + 0.004f);
                    yLo = Math.Min(yLo, p.y - r.R0 - 0.004f); yHi = Math.Max(yHi, p.y + r.R0 + 0.006f);
                    zHi = Math.Max(zHi, p.z + r.R0 + 0.004f);
                }
            }
            var xs = Axis(xLo, xHi, h);
            var ys = Axis(yLo, yHi, h);
            var zl = new List<float>();
            float zz = Field.ZEnd * sz, step = 0.012f;
            while (zz < -0.11f * sz) { zl.Add(zz); zz += step; }
            while (step > h * 1.2f) { zl.Add(zz); zz += step; step *= 0.72f; }
            while (zz < zHi) { zl.Add(zz); zz += h; }
            zl.Add(zz);
            var zs = zl.ToArray();
            int NX = xs.Length, NY = ys.Length, NZ = zs.Length;
            int Id(int i, int j, int k) => (k * NY + j) * NX + i;

            // ---- the field at every grid point (along each row, a point far from the surface settles the points next
            // to it: only the points near the surface are worked out in full) ----
            var val = new float[NX * NY * NZ];
            for (int k = 0; k < NZ; k++)
                for (int j = 0; j < NY; j++)
                {
                    int i = 0;
                    while (i < NX)
                    {
                        float d = f.Eval(xs[i], ys[j], zs[k], null);
                        val[Id(i, j, k)] = d;
                        float ad = Math.Abs(d);
                        int skip = (int)(ad * 0.6f / h);
                        for (int m = 1; m <= skip && i + m < NX; m++) val[Id(i + m, j, k)] = d > 0f ? ad - m * h : -(ad - m * h);
                        i += skip + 1;
                    }
                }

            // ---- surface nets: a point in every cell the surface passes through... ----
            int CX = NX - 1, CY = NY - 1, CZ = NZ - 1;
            var cell = new int[CX * CY * CZ];
            var V = new List<Vector3>(40000);
            var c8 = new float[8];
            var cp = new Vector3[8];
            int[] ea = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 }, eb = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };
            for (int k = 0; k < CZ; k++)
                for (int j = 0; j < CY; j++)
                    for (int i = 0; i < CX; i++)
                    {
                        int ci = (k * CY + j) * CX + i;
                        cell[ci] = -1;
                        int neg = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            float v = val[Id(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1))];
                            c8[c] = v;
                            if (v < 0f) neg++;
                        }
                        if (neg == 0 || neg == 8) continue;
                        for (int c = 0; c < 8; c++) cp[c] = new Vector3(xs[i + (c & 1)], ys[j + ((c >> 1) & 1)], zs[k + ((c >> 2) & 1)]);
                        var sum = Vector3.zero; int cnt = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            float va = c8[ea[e]], vb = c8[eb[e]];
                            if ((va < 0f) == (vb < 0f)) continue;
                            sum += Vector3.Lerp(cp[ea[e]], cp[eb[e]], va / (va - vb));
                            cnt++;
                        }
                        cell[ci] = V.Count;
                        V.Add(sum / cnt);
                    }

            // ---- ...pulled onto the true surface, with the field's own slope as its normal ----
            int n = V.Count;
            var verts = V.ToArray();
            var norms = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var p = verts[i];
                float lim = p.z < -0.1f * sz ? 0.004f : h * 0.7f;
                for (int it = 0; it < 2; it++)
                {
                    var g = f.Grad(p, out float d);
                    float g2 = g.sqrMagnitude;
                    if (g2 < 1e-6f) break;
                    var mv = g * (-d / g2);
                    float ml = mv.magnitude;
                    if (ml > lim) mv *= lim / ml;
                    p += mv;
                }
                var gn = f.Grad(p, out _);
                verts[i] = p;
                norms[i] = gn.sqrMagnitude > 1e-10f ? gn.normalized : Vector3.up;
            }

            // ---- ...and a quad across every grid edge the surface cuts, between the four cells round it ----
            var T = new List<int>(n * 6);
            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                var nn = norms[a] + norms[b] + norms[c] + norms[d];
                if (Vector3.Dot(Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]) + Vector3.Cross(verts[c] - verts[a], verts[d] - verts[a]), nn) < 0f) { int s = b; b = d; d = s; }
                // (split along the shorter diagonal)
                if ((verts[a] - verts[c]).sqrMagnitude <= (verts[b] - verts[d]).sqrMagnitude) { T.Add(a); T.Add(b); T.Add(c); T.Add(a); T.Add(c); T.Add(d); }
                else { T.Add(a); T.Add(b); T.Add(d); T.Add(b); T.Add(c); T.Add(d); }
            }
            int Cell(int i, int j, int k) => i < 0 || j < 0 || k < 0 || i >= CX || j >= CY || k >= CZ ? -1 : cell[(k * CY + j) * CX + i];
            for (int k = 0; k < NZ; k++)
                for (int j = 0; j < NY; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        bool s0 = val[Id(i, j, k)] < 0f;
                        if (i + 1 < NX && s0 != (val[Id(i + 1, j, k)] < 0f)) Quad(Cell(i, j - 1, k - 1), Cell(i, j, k - 1), Cell(i, j, k), Cell(i, j - 1, k));
                        if (j + 1 < NY && s0 != (val[Id(i, j + 1, k)] < 0f)) Quad(Cell(i - 1, j, k - 1), Cell(i, j, k - 1), Cell(i, j, k), Cell(i - 1, j, k));
                        if (k + 1 < NZ && s0 != (val[Id(i, j, k + 1)] < 0f)) Quad(Cell(i - 1, j - 1, k), Cell(i, j - 1, k), Cell(i, j, k), Cell(i - 1, j, k));
                    }

            // ---- each point's bones, and what's baked into its colour ----
            var cols = new Color32[n];
            var uvs = new Vector2[n];
            var bw = new BoneWeight[n];
            var grp = new float[5];
            var fq = new float[4];
            var wb = new int[12];
            var ww = new float[12];
            int wn = 0;
            void Add(int bone, float w)
            {
                if (w <= 1e-5f) return;
                for (int i = 0; i < wn; i++) if (wb[i] == bone) { ww[i] += w; return; }
                wb[wn] = bone; ww[wn] = w; wn++;
            }
            // a share of a joint's turn: between the two in-between bones either side of t (0: all the parent, 1: the joint)
            void Pair(int parent, int first, int end, float t, float share, ref float body)
            {
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float ft = t * 4f;
                int k = Math.Min(3, (int)ft);
                float fr = ft - k;
                int lo = k == 0 ? parent : first + k - 1, hi = k == 3 ? end : first + k;
                if (lo == BoneHand && parent == BoneHand && first != BoneWrist) body += share * (1f - fr); // (the hand's share bends with the wrist)
                else Add(lo, share * (1f - fr));
                Add(hi, share * fr);
            }
            float sigma = 0.002f * sz;
            float aoGain = P("ao.gain", 1.8f), sideOcc = P("ao.side", 0.38f), cavGain = P("cavity.gain", 0.0038f);
            float ce = P("cavity.reach", 0.0034f) * sz;
            float[] aoH = { 0.002f * sz, 0.005f * sz, 0.010f * sz, 0.018f * sz }, aoW = { 0.34f, 0.28f, 0.22f, 0.16f };
            for (int i = 0; i < n; i++)
            {
                var p = verts[i]; var nr = norms[i];
                f.Eval(p.x, p.y, p.z, grp);
                float sumF = 0f;
                for (int q = 0; q < 4; q++) { fq[q] = grp[1 + q] < 0.01f * sz ? (float)Math.Exp(-Math.Max(grp[1 + q], 0f) / sigma) : 0f; sumF += fq[q]; }
                float norm = sumF > 1f ? 1f / sumF : 1f;
                float body = 1f - sumF * norm;
                float palm = body * S(-nr.y, 0.15f, 0.75f) * Mathf.Lerp(P("arm.under", 0.5f), 1f, S(p.z / sz, -0.06f, 0.005f));
                float side = 0f, best = 0f, nailU = 0f, nailV = -5f, dorsal = 0f;
                wn = 0;
                for (int q = 0; q < 4; q++)
                {
                    float share = fq[q] * norm;
                    if (share <= 1e-4f) continue;
                    var r = f.Rest[q];
                    var rel = p - r.P0;
                    float a = Vector3.Dot(rel, r.Z), lx = Vector3.Dot(rel, r.X), ny = Vector3.Dot(nr, r.Y), nx = Vector3.Dot(nr, r.X);
                    float z1 = r.L0 - 0.6f * r.R1, z2 = r.L0 + r.L1 - 0.6f * r.R2;
                    int j0 = JointBase(q, 0), j1 = JointBase(q, 1), j2 = JointBase(q, 2);
                    if (a < z1) Pair(BoneHand, j0, j0 + 3, q < 3 ? S(a, -r.R0, 0.55f * r.R0) : S(a, -0.6f * r.R0, 0.6f * r.L0), share, ref body);
                    else if (a < z2) Pair(j0 + 3, j1, j1 + 3, S(a, z1, r.L0 + 0.6f * r.R1), share, ref body);
                    else Pair(j1 + 3, j2, j2 + 3, S(a, z2, r.L0 + r.L1 + 0.6f * r.R2), share, ref body);
                    palm += share * S(-ny, 0.15f, 0.75f);
                    if (share > best)
                    {
                        // where this point is on the digit's nail: across it (-1..1) and along it (0 at its base, 1 at its end)
                        best = share;
                        float n0 = r.L0 + r.L1 + r.L2 * 0.3f, n1 = r.Total + r.Tip * 0.7f;
                        nailU = lx / (r.R3 * 0.74f);
                        nailV = (a - n0) / (n1 - n0);
                        dorsal = ny;
                    }
                    if (q < 3)
                    {
                        // the sides of the fingers that face each other are shut in when they close up
                        float facing = q == 0 ? Math.Max(nx, 0f) : q == 2 ? Math.Max(-nx, 0f) : Math.Abs(nx);
                        side += share * facing * (0.35f + 0.65f * facing) * sideOcc * S(a, 0.002f * sz, 0.014f * sz) * (1f - 0.5f * S(a, r.Total * 0.7f, r.Total + r.R3));
                    }
                }
                // the body: the hand, turning into the forearm across the wrist
                {
                    float t = S01((0.014f - p.z / sz) / 0.064f);
                    float ft = t * 4f;
                    int k = Math.Min(3, (int)ft);
                    float fr = ft - k;
                    Add(k == 0 ? BoneHand : BoneWrist + k - 1, body * (1f - fr));
                    Add(k == 3 ? BoneArm : BoneWrist + k, body * fr);
                }
                // (the four heaviest)
                for (int a = 0; a < wn; a++)
                    for (int b = a + 1; b < wn; b++)
                        if (ww[b] > ww[a]) { float tw = ww[a]; ww[a] = ww[b]; ww[b] = tw; int tb = wb[a]; wb[a] = wb[b]; wb[b] = tb; }
                int cnt = Math.Min(4, wn);
                float tot = 0f;
                for (int a = 0; a < cnt; a++) tot += ww[a];
                var w = new BoneWeight();
                if (cnt > 0) { w.boneIndex0 = wb[0]; w.weight0 = ww[0] / tot; }
                if (cnt > 1) { w.boneIndex1 = wb[1]; w.weight1 = ww[1] / tot; }
                if (cnt > 2) { w.boneIndex2 = wb[2]; w.weight2 = ww[2] / tot; }
                if (cnt > 3) { w.boneIndex3 = wb[3]; w.weight3 = ww[3] / tot; }
                bw[i] = w;

                // how shut in the point is: the field a little way out along its normal, at a few distances
                float occ = 0f;
                if (p.z > -0.09f * sz)
                    for (int s = 0; s < aoH.Length; s++)
                    {
                        float d = f.Eval(p.x + nr.x * aoH[s], p.y + nr.y * aoH[s], p.z + nr.z * aoH[s], null);
                        occ += Math.Max(0f, aoH[s] - d) / aoH[s] * aoW[s];
                    }
                float ao = (1f - Mathf.Clamp01(occ * aoGain)) * (1f - Mathf.Clamp01(side));
                // how the surface curves here: a bump (a knuckle, a fingertip) is lit a touch brighter, a hollow (between
                // the knuckles, round the base of the thumb) a touch darker - the form reads even in flat light
                float cav = 0f;
                if (p.z > -0.09f * sz)
                {
                    float lap = f.Eval(p.x + ce, p.y, p.z, null) + f.Eval(p.x - ce, p.y, p.z, null) + f.Eval(p.x, p.y + ce, p.z, null)
                              + f.Eval(p.x, p.y - ce, p.z, null) + f.Eval(p.x, p.y, p.z + ce, null) + f.Eval(p.x, p.y, p.z - ce, null) - 6f * f.Eval(p.x, p.y, p.z, null);
                    cav = Mathf.Clamp(lap / (ce * ce) * cavGain * sz, -1f, 1f);
                    if (cav > 0f) cav *= 0.6f;
                }
                cols[i] = new Color32((byte)(Mathf.Clamp01(ao) * 255f), (byte)(Mathf.Clamp01(dorsal * 0.5f + 0.5f) * 255f), (byte)(Mathf.Clamp01(palm) * 255f), (byte)(Mathf.Clamp01(cav * 0.5f + 0.5f) * 255f));
                uvs[i] = new Vector2(Mathf.Clamp(nailU, -8f, 8f), Mathf.Clamp(nailV, -8f, 8f));
            }
            sw.Stop();
            return new Raw { V = verts, N = norms, C = cols, UV = uvs, W = bw, T = T.ToArray(), Ms = (float)sw.Elapsed.TotalMilliseconds };
        }
    }
}
