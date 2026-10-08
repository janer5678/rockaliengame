using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The alien hands' poses (ViewModel.Hands.cs builds them). The arms (m_R / m_L) still go exactly where the block-hand
    /// poses put them; on top of that, each frame:
    ///  1. every item says where each hand takes hold of it, in the item's own space (a fist round a handle - its axis, the
    ///     side the knuckles face, its thickness - or a palm resting on a surface), so the hand sits on the item whatever
    ///     the swing, draw or recoil is doing with it;
    ///  2. the fingers close on the item's real shape: each joint, knuckle first, curls until the finger would touch one of
    ///     the item's parts (its boxes, spheres and cylinders, read from the model), so they wrap round handles, cup round
    ///     the rock and ball and lie flat under a chest. The thumb closes the same way, over the curled fingers;
    ///  3. the actions add to it: a tighter grip as a swing lands, the trigger finger pulling on a shot, the string hand
    ///     springing open on release, the hand opening as a throw lets go, a squeeze while eating, a slow idle breath;
    ///  4. switching items blends every joint over 0.12 s.
    /// The solved grips are cached until the hand moves on the item (most frames are just the cached pose).
    /// </summary>
    public partial class ViewModel
    {
        // ======== poses ========

        sealed class JointPose
        {
            /// <summary>The hand (its wrist) in the arm's space.</summary>
            public Vector3 Pos;
            public Quaternion Rot = Quaternion.identity, Thumb = Quaternion.identity;
            /// <summary>[digit * 3 + joint] curl in degrees; digit 4 is the thumb (its joint 0 is Thumb, the base's turn).</summary>
            public readonly float[] A = new float[15];
            public readonly float[] Spread = new float[4];
            /// <summary>How far the forearm follows the hand's own direction (0: the arm's old line).</summary>
            public float ElbowK = 0.45f;

            public void Copy(JointPose o)
            {
                Pos = o.Pos; Rot = o.Rot; Thumb = o.Thumb; ElbowK = o.ElbowK;
                System.Array.Copy(o.A, A, 15);
                System.Array.Copy(o.Spread, Spread, 4);
            }

            public void Lerp(JointPose a, JointPose b, float t)
            {
                Pos = Vector3.Lerp(a.Pos, b.Pos, t);
                Rot = Quaternion.Slerp(a.Rot, b.Rot, t);
                Thumb = Quaternion.Slerp(a.Thumb, b.Thumb, t);
                ElbowK = Mathf.Lerp(a.ElbowK, b.ElbowK, t);
                for (int i = 0; i < 15; i++) A[i] = Mathf.Lerp(a.A[i], b.A[i], t);
                for (int i = 0; i < 4; i++) Spread[i] = Mathf.Lerp(a.Spread[i], b.Spread[i], t);
            }
        }

        /// <summary>What the fingers do: fixed shapes, or closing on the held thing between two curls per joint.</summary>
        enum Fp : byte { Relaxed, Fist, LooseFist, Open, Cup, Support, Hook, Pinch, Point, Paper }
        /// <summary>What the thumb does (its base's turn, then its two joints - fixed or closing).</summary>
        enum Tp : byte { Relaxed, Wrap, Pinch, Spread, Support, Tuck, Open }

        sealed class FingerSet { public float[] Min, Max, Spread; public bool Solve; }
        sealed class ThumbSet { public Quaternion Base; public float Min1, Min2, Max1, Max2; public bool Solve; }

        static FingerSet Fixed(float[] a, float[] spread) => new FingerSet { Min = a, Max = a, Spread = spread };
        static FingerSet Closing(float[] min, float[] max, float[] spread) => new FingerSet { Min = min, Max = max, Spread = spread, Solve = true };
        static float[] Each(float a, float b, float c) => new[] { a, b, c, a, b, c, a, b, c, a, b, c };

        // (index, middle, ring, little: knuckle, middle joint, end joint; spreads index..little, + towards the little finger)
        static readonly FingerSet[] s_Fingers =
        {
            /* Relaxed   */ Fixed(new float[] { 14, 20, 10, 18, 24, 12, 22, 28, 14, 26, 32, 16 }, new float[] { -6, -1, 4, 9 }),
            /* Fist      */ Closing(Each(6, 10, 5), Each(92, 105, 80), new float[] { -2, -0.5f, 1, 2.5f }),
            /* LooseFist */ Fixed(new float[] { 55, 75, 45, 60, 80, 50, 64, 84, 52, 68, 88, 55 }, new float[] { -2, 0, 2, 4 }),
            /* Open      */ Fixed(new float[] { 2, 3, 2, 2, 3, 2, 3, 4, 2, 4, 5, 3 }, new float[] { -12, -3, 6, 15 }),
            /* Cup       */ Closing(Each(4, 6, 3), Each(55, 60, 45), new float[] { -8, -2, 4, 10 }),
            /* Support   */ Closing(Each(4, 6, 3), Each(70, 75, 55), new float[] { -5, -1, 3, 7 }),
            /* Hook      */ Fixed(new float[] { 8, 70, 40, 10, 80, 45, 14, 82, 45, 55, 85, 55 }, new float[] { -3, -1, 1, 4 }),
            /* Pinch     */ Fixed(new float[] { 45, 50, 25, 50, 60, 32, 58, 68, 40, 64, 74, 45 }, new float[] { -4, -1, 2, 5 }),
            /* Point     */ Fixed(new float[] { 5, 6, 3, 8, 10, 5, 12, 14, 7, 16, 18, 9 }, new float[] { -8, -2, 4, 10 }),
            /* Paper     */ Closing(Each(5, 8, 4), Each(55, 60, 40), new float[] { -3, -1, 1, 3 }),
        };
        /// <summary>The trigger finger: the index lies curled round the trigger (closing onto it if the gun has one).</summary>
        static readonly float[] s_TriggerMin = { 5, 30, 12 }, s_TriggerMax = { 32, 72, 42 };

        /// <summary>A thumb base's turn: the direction it points from its root and which way its nail faces (it curls the other way).</summary>
        static Quaternion TB(float dx, float dy, float dz, float nx, float ny, float nz) => Quaternion.LookRotation(new Vector3(dx, dy, dz).normalized, new Vector3(nx, ny, nz));

        static readonly ThumbSet[] s_Thumbs =
        {
            /* Relaxed */ new ThumbSet { Base = TB(-0.55f, -0.3f, 0.78f, -0.6f, 0.75f, 0f), Min1 = 18, Min2 = 15, Max1 = 18, Max2 = 15 },
            // round a handle: down its back, then curling forward under it and over the curled fingers
            /* Wrap    */ new ThumbSet { Base = TB(0.4f, -0.85f, 0.3f, -0.35f, 0.05f, -0.93f), Min1 = 30, Min2 = 45, Max1 = 75, Max2 = 85, Solve = true },
            /* Pinch   */ new ThumbSet { Base = TB(-0.01f, -0.65f, 0.76f, -1f, 0.2f, 0.1f), Min1 = 8, Min2 = 8, Max1 = 8, Max2 = 8 },
            /* Spread  */ new ThumbSet { Base = TB(-0.7f, -0.35f, 0.62f, -0.5f, 0.85f, 0.1f), Min1 = 8, Min2 = 8, Max1 = 70, Max2 = 70, Solve = true },
            /* Support */ new ThumbSet { Base = TB(-0.45f, -0.25f, 0.86f, -0.85f, 0.5f, 0.1f), Min1 = 5, Min2 = 5, Max1 = 40, Max2 = 45, Solve = true },
            /* Tuck    */ new ThumbSet { Base = TB(-0.1f, -0.8f, 0.6f, -0.5f, 0.3f, -0.8f), Min1 = 35, Min2 = 30, Max1 = 35, Max2 = 30 },
            /* Open    */ new ThumbSet { Base = TB(-0.88f, 0f, 0.48f, -0.3f, 0.95f, 0.1f), Min1 = 3, Min2 = 2, Max1 = 3, Max2 = 2 },
        };

        // ======== what the fingers close on ========

        struct Shape
        {
            /// <summary>0 box, 1 ellipsoid, 2 cylinder (along its own Y), 3 capsule (A to B).</summary>
            public byte Kind;
            public Vector3 C, H, A, B;
            public Quaternion Inv;
            public float R, Bound;
        }

        sealed class ShapeSet
        {
            public Transform Space;
            public readonly List<Shape> List = new List<Shape>();
            /// <summary>All of it, in the space's axes.</summary>
            public Bounds Box;
            public int Version;
            public Object For;
        }

        readonly ShapeSet m_IS = new ShapeSet(), m_BS = new ShapeSet();
        static int s_ShapeVersion;

        /// <summary>Every part of a model as a solid in `space`'s coordinates: boxes for cubes, cylinders for cylinders, an
        /// ellipsoid round anything else (spheres, rocks, cones...).</summary>
        static void BuildShapes(ShapeSet set, Transform space, GameObject root)
        {
            set.List.Clear();
            set.Space = space;
            set.For = root;
            set.Version = ++s_ShapeVersion & 0x1FFFFF;
            set.Box = new Bounds(Vector3.zero, Vector3.zero);
            if (root == null || space == null) return;
            var w2s = space.worldToLocalMatrix;
            bool first = true;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var rr = mf.GetComponent<Renderer>();
                if (mesh == null || rr == null || !rr.enabled) continue;
                var m = w2s * mf.transform.localToWorldMatrix;
                Vector3 cx = m.GetColumn(0), cy = m.GetColumn(1), cz = m.GetColumn(2);
                float lx = cx.magnitude, ly = cy.magnitude, lz = cz.magnitude;
                if (lx < 1e-5f || ly < 1e-5f || lz < 1e-5f) continue;
                var rot = Quaternion.LookRotation(cz / lz, cy / ly);
                var b = mesh.bounds;
                var h = Vector3.Scale(b.extents, new Vector3(lx, ly, lz));
                if (Mathf.Max(h.x, Mathf.Max(h.y, h.z)) < 0.004f) continue;
                h = Vector3.Max(h, Vector3.one * 0.001f);
                byte kind = mesh == Art.Cube || mesh == SmoothShade.SmoothOf(Art.Cube) ? (byte)0
                    : mesh == Art.Cylinder || mesh == SmoothShade.SmoothOf(Art.Cylinder) ? (byte)2 : (byte)1;
                var sh = new Shape { Kind = kind, C = m.MultiplyPoint3x4(b.center), H = h, Inv = Quaternion.Inverse(rot), R = (h.x + h.z) * 0.5f, Bound = h.magnitude };
                set.List.Add(sh);
                Vector3 ax = rot * Vector3.right, ay = rot * Vector3.up, az = rot * Vector3.forward;
                var e = new Vector3(Mathf.Abs(ax.x) * h.x + Mathf.Abs(ay.x) * h.y + Mathf.Abs(az.x) * h.z,
                                    Mathf.Abs(ax.y) * h.x + Mathf.Abs(ay.y) * h.y + Mathf.Abs(az.y) * h.z,
                                    Mathf.Abs(ax.z) * h.x + Mathf.Abs(ay.z) * h.y + Mathf.Abs(az.z) * h.z);
                var bb = new Bounds(sh.C, e * 2f);
                if (first) { set.Box = bb; first = false; }
                else set.Box.Encapsulate(bb);
            }
        }

        static float Sdf(in Shape s, Vector3 p)
        {
            switch (s.Kind)
            {
                case 0:
                {
                    var q = s.Inv * (p - s.C);
                    q = new Vector3(Mathf.Abs(q.x) - s.H.x, Mathf.Abs(q.y) - s.H.y, Mathf.Abs(q.z) - s.H.z);
                    var o = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f));
                    return o.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
                }
                case 1:
                {
                    var q = s.Inv * (p - s.C);
                    var a = new Vector3(q.x / s.H.x, q.y / s.H.y, q.z / s.H.z);
                    var b = new Vector3(a.x / s.H.x, a.y / s.H.y, a.z / s.H.z);
                    float k0 = a.magnitude, k1 = b.magnitude;
                    return k1 < 1e-6f ? -Mathf.Min(s.H.x, Mathf.Min(s.H.y, s.H.z)) : k0 * (k0 - 1f) / k1;
                }
                case 2:
                {
                    var q = s.Inv * (p - s.C);
                    float dx = new Vector2(q.x, q.z).magnitude - s.R, dy = Mathf.Abs(q.y) - s.H.y;
                    return Mathf.Min(Mathf.Max(dx, dy), 0f) + new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
                }
                default:
                {
                    var pa = p - s.A; var ba = s.B - s.A;
                    float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Mathf.Max(1e-10f, ba.sqrMagnitude));
                    return (pa - ba * h).magnitude - s.R;
                }
            }
        }

        /// <summary>Where a line from `from` along `dir` (in the set's space) first meets the held thing (sphere tracing);
        /// `from` itself if it never does.</summary>
        static Vector3 Trace(ShapeSet set, Vector3 from, Vector3 dir)
        {
            if (set == null || set.List.Count == 0) return from;
            dir.Normalize();
            float t = 0f;
            for (int it = 0; it < 96; it++)
            {
                var p = from + dir * t;
                float d = 1e9f;
                foreach (var sh in set.List) d = Mathf.Min(d, Sdf(sh, p));
                if (d < 0.0005f) return p;
                t += Mathf.Max(d * 0.9f, 0.0005f);
                if (t > 3f) break;
            }
            return from;
        }

        // ---- the solver (in the right hand's own frame; the left is mirrored into it) ----
        static readonly List<Shape> s_Near = new List<Shape>();
        static Matrix4x4 s_M = Matrix4x4.identity;
        static float s_Scale = 1f;
        static readonly Vector3[] s_Pts = new Vector3[4];
        static readonly float[] s_Ang = new float[3], s_Lo = new float[3], s_Hi = new float[3];

        /// <summary>How far a point of the hand is from the nearest thing it can touch (metres; negative inside).</summary>
        static float Field(Vector3 native)
        {
            var p = s_M.MultiplyPoint3x4(native);
            float d = 1e9f;
            for (int i = 0; i < s_Near.Count; i++) { float x = Sdf(s_Near[i], p); if (x < d) d = x; }
            return d * s_Scale;
        }

        /// <summary>A digit's joints (s_Pts: root, two joints, tip) for these curls.</summary>
        static void Chain(int d, float a0, float a1, float a2, float spread, Quaternion thumb)
        {
            var q = d < 4 ? Quaternion.Euler(0f, spread, 0f) * Quaternion.Euler(a0, 0f, 0f) : thumb;
            var p = s_Base[d];
            s_Pts[0] = p;
            p += q * new Vector3(0f, 0f, Len(d, 0)); s_Pts[1] = p;
            q *= Quaternion.Euler(a1, 0f, 0f); p += q * new Vector3(0f, 0f, Len(d, 1)); s_Pts[2] = p;
            q *= Quaternion.Euler(a2, 0f, 0f); p += q * new Vector3(0f, 0f, Len(d, 2)); s_Pts[3] = p;
        }

        /// <summary>How clear bone k of the digit (s_Pts) is of everything (metres; negative: into something).</summary>
        static float SegClear(int d, int k)
        {
            float c = 1e9f;
            for (int t = 1; t <= 3; t++)
            {
                float u = t / 3f;
                var p = Vector3.Lerp(s_Pts[k], s_Pts[k + 1], u);
                float r = Mathf.Lerp(Rad(d, k), Rad(d, k + 1), u) * (k == 2 && t == 3 ? 1.15f : 0.9f) * HandScale;
                c = Mathf.Min(c, Field(p) - r);
            }
            return c;
        }

        /// <summary>Bone k touches (or, if it started out touching, presses further in).</summary>
        static bool SegHits(int d, int k) => SegClear(d, k) < Mathf.Min(0f, s_Clear0[k]) - 0.001f;
        static readonly float[] s_Clear0 = new float[3];

        /// <summary>How fast each joint curls as a finger closes: the knuckle leads, so the finger's base meets a handle
        /// first and the rest then wraps round it (all together, the tip would touch first and the finger stay open).</summary>
        static readonly float[] s_Rate = { 1f, 0.35f, 0.2f };
        static readonly float[] s_Try = new float[3], s_Free = new float[3];

        /// <summary>Closes a digit like a real grasp: its joints curl together from their least towards their most; when a
        /// bone touches something, it and the joints behind it stop there and the joints past it carry on curling round.
        /// s_Ang gets the curls (from joint `first`: the thumb's base doesn't curl).</summary>
        static void SolveDigit(int d, int first, float spread, Quaternion thumb)
        {
            for (int j = 0; j < 3; j++) s_Ang[j] = s_Lo[j];
            int frozen = first - 1;
            Chain(d, s_Ang[0], s_Ang[1], s_Ang[2], spread, thumb);
            for (int k = 0; k < 3; k++) s_Clear0[k] = k < first ? 1f : SegClear(d, k); // (a bone touching already may still close, just not further in)
            const float step = 5f;
            for (float t = step; frozen < 2 && t < 600f; t += step)
            {
                bool moved = false;
                for (int j = 0; j < 3; j++)
                {
                    s_Try[j] = s_Ang[j];
                    if (j <= frozen) continue;
                    float v = Mathf.Min(s_Hi[j], s_Lo[j] + s_Rate[j] * t);
                    if (v != s_Ang[j]) moved = true;
                    s_Try[j] = v;
                }
                if (!moved) break;
                Chain(d, s_Try[0], s_Try[1], s_Try[2], spread, thumb);
                int hit = -1;
                for (int k = frozen + 1; k < 3 && hit < 0; k++) if (SegHits(d, k)) hit = k;
                if (hit < 0) { for (int j = 0; j < 3; j++) s_Ang[j] = s_Try[j]; continue; }
                // the moment of touching, more closely
                for (int j = 0; j < 3; j++) s_Free[j] = s_Ang[j];
                for (int it = 0; it < 4; it++)
                {
                    for (int j = 0; j < 3; j++) s_Ang[j] = (s_Free[j] + s_Try[j]) * 0.5f;
                    Chain(d, s_Ang[0], s_Ang[1], s_Ang[2], spread, thumb);
                    bool h = false;
                    for (int k = frozen + 1; k <= hit && !h; k++) h = SegHits(d, k);
                    for (int j = 0; j < 3; j++) { if (h) s_Try[j] = s_Ang[j]; else s_Free[j] = s_Ang[j]; }
                }
                for (int j = 0; j < 3; j++) s_Ang[j] = s_Free[j];
                frozen = hit;
            }
        }

        static Shape Capsule(Vector3 a, Vector3 b, float r) => new Shape { Kind = 3, A = a, B = b, R = r, C = (a + b) * 0.5f, Bound = (b - a).magnitude * 0.5f + r };

        static bool Close(Matrix4x4 a, Matrix4x4 b)
        {
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                    if (Mathf.Abs(a[r, c] - b[r, c]) > 3e-4f) return false;
            return true;
        }

        sealed class SolveCache { public int Key = -1; public Matrix4x4 M; public readonly float[] A = new float[15]; public float Seat; }
        readonly SolveCache[] m_CacheR = { new SolveCache(), new SolveCache(), new SolveCache() };
        readonly SolveCache[] m_CacheL = { new SolveCache(), new SolveCache(), new SolveCache() };

        // ======== goals: where a hand takes hold ========

        struct HandGoal
        {
            /// <summary>The hand (its wrist) in its arm's space.</summary>
            public Vector3 Pos;
            public Quaternion Rot;
            public Fp Fingers;
            public Tp Thumb;
            /// <summary>The index finger on the trigger.</summary>
            public bool Trigger;
            /// <summary>Rest the palm right on the surface below it.</summary>
            public bool Seat;
            /// <summary>What the fingers close on (null: nothing - the preset's own curls).</summary>
            public ShapeSet Shapes;
            /// <summary>More to close on, in the shapes' space (the other hand's fingers).</summary>
            public List<Shape> Extra;
            public float ElbowK;
            public HandGoal With(Fp f, Tp t) { Fingers = f; Thumb = t; return this; }
        }

        static Vector3 PowerG(float r) => new Vector3(0f, -(PalmHalf + r / HandScale), GripZ);
        /// <summary>Where a bowstring sits in the hooked fingers.</summary>
        static readonly Vector3 HookG = new Vector3(0f, -0.022f, 0.112f);

        /// <summary>A fist round a handle: c on its axis, a along it from the thumb's side to the little finger's, the
        /// knuckles facing f, r its thickness (all in `space`); diag turns the hand so the handle runs across the palm
        /// diagonally, as it does holding a long shaft.</summary>
        HandGoal Grip(bool right, ShapeSet set, Transform space, Vector3 c, Vector3 a, Vector3 f, float r, float diag = 0f)
            => GripAt(right, set, space, c, a, f, PowerG(r * space.lossyScale.x), diag);

        HandGoal GripAt(bool right, ShapeSet set, Transform space, Vector3 c, Vector3 a, Vector3 f, Vector3 gNative, float diag = 0f)
        {
            var arm = right ? m_R : m_L;
            var cA = arm.InverseTransformPoint(space.TransformPoint(c));
            var x = arm.InverseTransformDirection(space.TransformDirection(a)).normalized;
            if (!right) x = -x;
            var fA = arm.InverseTransformDirection(space.TransformDirection(f));
            var z = fA - Vector3.Dot(fA, x) * x;
            if (z.sqrMagnitude < 1e-8f) z = Vector3.Cross(x, Vector3.up);
            z.Normalize();
            var rot = Quaternion.LookRotation(z, Vector3.Cross(z, x)) * Quaternion.Euler(0f, -diag * (right ? 1f : -1f), 0f);
            return new HandGoal { Pos = cA - rot * (gNative * HandScale), Rot = rot, Shapes = set, ElbowK = 0.45f, Fingers = Fp.Fist, Thumb = Tp.Wrap };
        }

        /// <summary>The palm flat on a surface: c the point on it, n its outward normal (towards the palm), the fingers
        /// pointing along f.</summary>
        HandGoal PalmOn(bool right, ShapeSet set, Transform space, Vector3 c, Vector3 n, Vector3 f)
        {
            var arm = right ? m_R : m_L;
            var cA = arm.InverseTransformPoint(space.TransformPoint(c));
            var y = arm.InverseTransformDirection(space.TransformDirection(n)).normalized;
            var fA = arm.InverseTransformDirection(space.TransformDirection(f));
            var z = fA - Vector3.Dot(fA, y) * y;
            if (z.sqrMagnitude < 1e-8f) z = Vector3.Cross(Vector3.right, y);
            z.Normalize();
            var rot = Quaternion.LookRotation(z, y);
            return new HandGoal { Pos = cA - rot * (new Vector3(0f, -PalmHalf, 0.046f) * HandScale), Rot = rot, Shapes = set, Seat = set != null, ElbowK = 0.45f, Fingers = Fp.Support, Thumb = Tp.Support };
        }

        /// <summary>A hand holding nothing, its palm where the block fist was.</summary>
        static HandGoal Free(Quaternion rot, Fp f, Tp t) => new HandGoal { Rot = rot, Pos = -(rot * (new Vector3(0f, -0.004f, 0.045f) * HandScale)), Fingers = f, Thumb = t, ElbowK = 0.3f };

        /// <summary>The free left hand: out of view and relaxed, or a loose fist pumping as you sprint.</summary>
        HandGoal LeftFree() => m_SprintK > 0.01f ? Free(Quaternion.identity, Fp.LooseFist, Tp.Tuck) : Free(Quaternion.identity, Fp.Relaxed, Tp.Relaxed);

        HandGoal SupportUnder(bool right, ShapeSet set, Vector3 c)
            => PalmOn(right, set, m_ItemHolder, c, Vector3.down, new Vector3(right ? -0.3f : 0.3f, 0f, 1f));

        struct GunGrip { public Vector3 C, Up; public float R, SupZ, SupY; }

        /// <summary>Each gun's grip (from its model): where on it the fist goes (so the index finger is level with the trigger,
        /// just under the body), which way it runs, how thick it is; and where the other hand holds it underneath.</summary>
        static GunGrip Gun(Item it)
        {
            GunGrip G(Vector3 centre, float tilt, float t, float r, float supZ, float supY)
            {
                float a = tilt * Mathf.Deg2Rad;
                var u = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                return new GunGrip { C = centre + u * t, Up = u, R = r, SupZ = supZ, SupY = supY };
            }
            switch (it)
            {
                case Item.Pistol: return G(new Vector3(0f, -0.06f, -0.01f), -12f, 0.021f, 0.026f, 0.15f, 0f);
                case Item.Revolver: return G(new Vector3(0f, -0.05f, -0.07f), -18f, 0.016f, 0.024f, 0.15f, 0.01f);
                case Item.Shotgun: return G(new Vector3(0f, -0.08f, -0.02f), -15f, 0.021f, 0.024f, 0.2f, -0.03f);
                case Item.Sniper: return G(new Vector3(0f, -0.07f, 0.02f), -15f, 0.01f, 0.021f, 0.18f, -0.03f);
                case Item.PortalGun: return G(new Vector3(0f, -0.07f, -0.05f), 0f, -0.005f, 0.027f, 0.19f, -0.02f);
                case Item.RocketLauncher: return G(new Vector3(0f, -0.1f, 0.05f), -10f, 0.025f, 0.023f, 0.25f, -0.045f);
                default: return G(new Vector3(0f, -0.08f, -0.02f), -15f, 0.012f, 0.022f, 0.2f, -0.035f); // the crossbow
            }
        }

        // ======== each frame ========

        readonly JointPose m_TR = new JointPose(), m_TL = new JointPose(), m_ShowR = new JointPose(), m_ShowL = new JointPose(),
            m_FromR = new JointPose(), m_FromL = new JointPose(), m_PA = new JointPose(), m_PB = new JointPose(), m_PC = new JointPose();
        readonly List<Shape> m_Extra = new List<Shape>();
        static readonly Vector3[] s_Caps = new Vector3[4];
        Item m_HandsItem = (Item)253;
        float m_HandsSwitch = -10f, m_PrevDraw, m_BowRelease = -10f;
        bool m_HandsShown;
        /// <summary>The bow (PoseBow, in the view model's space): the string's middle where it's drawn, the bow's up, the arrow's line.</summary>
        Vector3 m_BowString, m_BowLow, m_BowUp = Vector3.up, m_BowDir = Vector3.forward;

        static float Bump(float x, float a, float b) => x <= a || x >= b ? 0f : Mathf.Sin((x - a) / (b - a) * Mathf.PI);

        void Pose(JointPose into, HandGoal g, bool right, SolveCache cache)
        {
            var arm = right ? m_R : m_L;
            var fs = s_Fingers[(int)g.Fingers];
            var ts = s_Thumbs[(int)g.Thumb];
            into.Pos = g.Pos; into.Rot = g.Rot; into.ElbowK = g.ElbowK; into.Thumb = ts.Base;
            for (int d = 0; d < 4; d++)
            {
                into.Spread[d] = fs.Spread[d];
                for (int j = 0; j < 3; j++) into.A[d * 3 + j] = d == 0 && g.Trigger ? s_TriggerMin[j] : fs.Min[d * 3 + j];
            }
            into.A[12] = 0f; into.A[13] = ts.Min1; into.A[14] = ts.Min2;
            var set = g.Shapes;
            if (set == null || set.Space == null || (set.List.Count == 0 && g.Extra == null)) return;
            if (!(fs.Solve || ts.Solve || g.Trigger || g.Seat)) return;

            var M = set.Space.worldToLocalMatrix * arm.localToWorldMatrix * Matrix4x4.TRS(g.Pos, g.Rot, Vector3.one * HandScale);
            if (!right) M *= Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            int key = (int)g.Fingers | ((int)g.Thumb << 4) | (g.Trigger ? 1 << 8 : 0) | (g.Seat ? 1 << 9 : 0) | (set.Version << 10);
            if (g.Extra == null && cache.Key == key && Close(cache.M, M))
            {
                System.Array.Copy(cache.A, into.A, 15);
                into.Pos += into.Rot * new Vector3(0f, -cache.Seat, 0f);
                return;
            }
            s_Scale = Mathf.Max(1e-4f, set.Space.lossyScale.x);
            s_M = M;
            // just what's in reach of this hand
            var pc = M.MultiplyPoint3x4(new Vector3(0f, -0.012f, 0.05f));
            float reach = 0.17f / s_Scale;
            s_Near.Clear();
            foreach (var sh in set.List) if ((sh.C - pc).magnitude - sh.Bound < reach) s_Near.Add(sh);
            if (g.Extra != null) s_Near.AddRange(g.Extra);
            float seat = 0f;
            if (g.Seat && s_Near.Count > 0)
            {
                // the palm comes to rest right on the surface (not floating off it, not sunk into it)
                seat = Mathf.Min(Mathf.Min(Field(new Vector3(0f, -PalmHalf, 0.03f)), Field(new Vector3(0f, -PalmHalf, 0.058f))),
                    Mathf.Min(Field(new Vector3(0.012f, -PalmHalf, 0.045f)), Field(new Vector3(-0.012f, -PalmHalf, 0.045f))));
                seat = Mathf.Clamp(seat, -0.05f, 0.05f);
                s_M = M * Matrix4x4.Translate(new Vector3(0f, -seat / HandScale, 0f));
                into.Pos += into.Rot * new Vector3(0f, -seat, 0f);
            }
            if (s_Near.Count > 0)
            {
                for (int d = 0; d < 4; d++)
                {
                    bool trig = d == 0 && g.Trigger;
                    if (!fs.Solve && !trig) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        s_Lo[j] = trig ? s_TriggerMin[j] : fs.Min[d * 3 + j];
                        s_Hi[j] = trig ? s_TriggerMax[j] : fs.Max[d * 3 + j];
                    }
                    SolveDigit(d, 0, fs.Spread[d], ts.Base);
                    for (int j = 0; j < 3; j++) into.A[d * 3 + j] = s_Ang[j];
                }
                if (ts.Solve)
                {
                    int before = s_Near.Count;
                    s_Lo[0] = s_Hi[0] = 0f;
                    s_Lo[1] = ts.Min1; s_Hi[1] = ts.Max1;
                    s_Lo[2] = ts.Min2; s_Hi[2] = ts.Max2;
                    SolveDigit(4, 1, 0f, ts.Base);
                    into.A[13] = s_Ang[1]; into.A[14] = s_Ang[2];
                    s_Near.RemoveRange(before, s_Near.Count - before);
                }
            }
            cache.Key = key; cache.M = M; cache.Seat = seat;
            System.Array.Copy(into.A, cache.A, 15);
        }

        void One(JointPose into, HandGoal g, bool right, int slot) => Pose(into, g, right, (right ? m_CacheR : m_CacheL)[slot]);

        /// <summary>Part way from one hold to another (each solved on its own, then every joint blended).</summary>
        void Blend(JointPose into, HandGoal a, HandGoal b, float t, bool right)
        {
            if (t <= 0.001f) { One(into, a, right, 0); return; }
            if (t >= 0.999f) { One(into, b, right, 1); return; }
            One(m_PA, a, right, 0);
            One(m_PB, b, right, 1);
            into.Lerp(m_PA, m_PB, t);
        }

        /// <summary>A solved hand's fingers as capsules in `space` (for the other hand to close over).</summary>
        List<Shape> FingerCaps(JointPose p, bool right, Transform space)
        {
            m_Extra.Clear();
            var arm = right ? m_R : m_L;
            float s = Mathf.Max(1e-4f, space.lossyScale.x);
            for (int d = 0; d < 4; d++)
            {
                Chain(d, p.A[d * 3], p.A[d * 3 + 1], p.A[d * 3 + 2], p.Spread[d], p.Thumb);
                var w = s_Caps;
                for (int k = 0; k < 4; k++)
                {
                    var n = s_Pts[k];
                    if (!right) n.x = -n.x;
                    w[k] = space.InverseTransformPoint(arm.TransformPoint(p.Pos + p.Rot * (n * HandScale)));
                }
                for (int k = 0; k < 3; k++) m_Extra.Add(Capsule(w[k], w[k + 1], Rad(d, k) * HandScale / s));
            }
            return m_Extra;
        }

        // ---- what the actions do to the fingers ----
        static void Squeeze(JointPose p, float deg)
        {
            if (deg == 0f) return;
            for (int d = 0; d < 4; d++) { p.A[d * 3] += deg * 0.4f; p.A[d * 3 + 1] += deg; p.A[d * 3 + 2] += deg * 0.7f; }
            p.A[13] += deg * 0.5f; p.A[14] += deg * 0.6f;
        }

        static void OpenUp(JointPose p, float k)
        {
            if (k <= 0f) return;
            k = Mathf.Clamp01(k);
            var o = s_Fingers[(int)Fp.Open];
            var ot = s_Thumbs[(int)Tp.Open];
            for (int d = 0; d < 4; d++)
            {
                for (int j = 0; j < 3; j++) p.A[d * 3 + j] = Mathf.Lerp(p.A[d * 3 + j], o.Min[d * 3 + j], k);
                p.Spread[d] = Mathf.Lerp(p.Spread[d], o.Spread[d], k);
            }
            p.Thumb = Quaternion.Slerp(p.Thumb, ot.Base, k);
            p.A[13] = Mathf.Lerp(p.A[13], ot.Min1, k);
            p.A[14] = Mathf.Lerp(p.A[14], ot.Min2, k);
        }

        void Breathe(JointPose p, float phase)
        {
            float t = Now * 1.5f + phase;
            for (int d = 0; d < 4; d++)
            {
                float w = Mathf.Sin(t + d * 0.8f);
                p.A[d * 3] += w * 0.6f; p.A[d * 3 + 1] += w * 1.3f; p.A[d * 3 + 2] += w * 0.8f;
            }
            p.A[14] += Mathf.Sin(t + 2.6f) * 1f;
        }

        /// <summary>The alien hands' pass, after the block-hand poses have put the arms and the item in place.</summary>
        void UpdateAlienHands(State s, bool swinging, float swingE)
        {
            if (m_PrevDraw > 0.25f && s.Draw <= 0.001f) m_BowRelease = Now;
            m_PrevDraw = s.Draw;
            if (m_AR == null || m_AL == null || !GameSettings.RoundHands) { m_HandsShown = false; return; }
            if (m_Item != m_IS.For) BuildShapes(m_IS, m_ItemHolder, m_Item);
            if (m_Ball != m_BS.For) BuildShapes(m_BS, m_Ball ? m_Ball.transform : null, m_Ball);

            AlienGoals(s, swinging, swingE);

            var want = s.Ball ? (Item)254 : s.Item;
            if (!m_HandsShown || want != m_HandsItem)
            {
                if (m_HandsShown) { m_FromR.Copy(m_ShowR); m_FromL.Copy(m_ShowL); m_HandsSwitch = Now; }
                else m_HandsSwitch = -10f;
                m_HandsItem = want;
                m_HandsShown = true;
            }
            float k = Smooth((Now - m_HandsSwitch) / 0.12f);
            if (k >= 1f) { m_ShowR.Copy(m_TR); m_ShowL.Copy(m_TL); }
            else { m_ShowR.Lerp(m_FromR, m_TR, k); m_ShowL.Lerp(m_FromL, m_TL, k); }
            ApplyAlien(m_AR, m_R, m_ShowR);
            ApplyAlien(m_AL, m_L, m_ShowL);
        }

        /// <summary>Every item's hold for both hands (into m_TR / m_TL), and what its actions do to the fingers.</summary>
        void AlienGoals(State s, bool swinging, float e)
        {
            var H = m_ItemHolder;
            var hold = m_IS.List.Count > 0 ? m_IS : null;
            float now = Now;
            float sqR = 0f, sqL = 0f, openR = 0f, openL = 0f, pull = 0f;
            // a swing's grip tightens as it lands
            float strike = swinging ? Bump(e, m_Down - 0.05f, m_Down + 0.16f) : 0f;
            var down = Vector3.down;
            var fwd = Vector3.forward;

            if (s.Ball)
            {
                if (m_Ball == null) { One(m_TR, Free(Quaternion.identity, Fp.Open, Tp.Open), true, 0); One(m_TL, Free(Quaternion.identity, Fp.Open, Tp.Open), false, 0); }
                else
                {
                    // both hands spread flat on either side of the ball, fingers fanned forward over it, thumbs up on top; they open as it's thrown
                    var bs = m_BS.List.Count > 0 ? m_BS : null;
                    float rad = 0.6f;
                    if (bs != null) foreach (var sh in bs.List) if (sh.Kind == 1) rad = Mathf.Max(sh.H.x, Mathf.Max(sh.H.y, sh.H.z));
                    var bt = m_Ball.transform;
                    var nR = new Vector3(0.92f, 0.3f, -0.25f).normalized; // (above the band round its middle)
                    var nL = new Vector3(-nR.x, nR.y, nR.z);
                    One(m_TR, PalmOn(true, bs, bt, nR * rad, nR, new Vector3(-0.2f, 0.5f, 1f)).With(Fp.Cup, Tp.Spread), true, 0);
                    One(m_TL, PalmOn(false, bs, bt, nL * rad, nL, new Vector3(0.2f, 0.5f, 1f)).With(Fp.Cup, Tp.Spread), false, 0);
                    float throwK = Mathf.Clamp01((now - m_ThrowStart) / 0.3f);
                    if (throwK < 1f) openR = openL = Smooth((throwK - 0.3f) / 0.35f);
                    for (int d = 0; d < 4; d++) { float sp = (d - 1.2f) * 4f; m_TR.Spread[d] += sp; m_TL.Spread[d] += sp; }
                }
            }
            else
            {
                switch (s.Item)
                {
                    case Item.Rock:
                    {
                        // cupped round the rock from either side, fingers over its front, thumbs on top
                        var b = hold != null ? hold.Box : new Bounds(new Vector3(0f, 0.02f, 0.04f), new Vector3(0.15f, 0.12f, 0.14f));
                        var c = b.center; var ex = b.extents;
                        One(m_TR, PalmOn(true, hold, H, c + new Vector3(ex.x, -ex.y * 0.2f, -ex.z * 0.25f), new Vector3(1f, -0.25f, -0.2f), new Vector3(-0.1f, 0.35f, 1f)).With(Fp.Cup, Tp.Spread), true, 0);
                        One(m_TL, PalmOn(false, hold, H, c + new Vector3(-ex.x, -ex.y * 0.2f, -ex.z * 0.25f), new Vector3(-1f, -0.25f, -0.2f), new Vector3(0.1f, 0.35f, 1f)).With(Fp.Cup, Tp.Spread), false, 0);
                        sqR = sqL = 8f * strike;
                        break;
                    }
                    case Item.Hatchet:
                    case Item.Pickaxe:
                    case Item.TreeCracker:
                    {
                        // a fist round the handle (a little way up it, so it's well in view), thumb on top, the knuckles
                        // facing the way the head points
                        float r = s.Item == Item.TreeCracker ? 0.035f : s.Item == Item.Hatchet ? 0.03f : 0.027f;
                        var gt = Grip(true, hold, H, new Vector3(0f, 0.1f, 0f), down, fwd, r);
                        gt.ElbowK = 0.3f;
                        One(m_TR, gt, true, 0);
                        One(m_TL, LeftFree(), false, 0);
                        sqR = 10f * strike;
                        break;
                    }
                    case Item.Sword:
                        // round the leather grip, the index just under the crossguard
                        One(m_TR, Grip(true, hold, H, new Vector3(0f, 0.085f, 0f), down, fwd, 0.024f), true, 0);
                        One(m_TL, LeftFree(), false, 0);
                        sqR = 10f * strike;
                        break;
                    case Item.Spear:
                    {
                        // carried low: both hands on the shaft from above, palms in, the shaft running diagonally across them;
                        // raised to throw: the right hand under the shaft (palm up, fingers over it), the left reaching ahead
                        float k = Smooth(m_SpearAimK);
                        var idleR = Grip(true, hold, H, new Vector3(0f, 0.12f, 0f), down, fwd, 0.022f, 35f);
                        var idleL = Grip(false, hold, H, new Vector3(0f, 0.62f, 0f), down, fwd, 0.022f, 35f);
                        idleR.ElbowK = idleL.ElbowK = 0.1f; // (the hands hang over the shaft: the arms come from behind, not above)
                        var aimR = Grip(true, hold, H, new Vector3(0f, 0.15f, 0f), down, Vector3.left, 0.022f, 45f);
                        var aimL = Free(Quaternion.identity, Fp.Point, Tp.Relaxed);
                        Blend(m_TR, idleR, aimR, k, true);
                        Blend(m_TL, idleL, aimL, k, false);
                        float since = now - m_ThrowStart;
                        openR = since < 0.08f ? 0f : since < 0.3f ? Smooth((since - 0.08f) / 0.07f) : 1f - Smooth((since - 0.3f) / 0.25f);
                        sqR = sqL = 7f * strike;
                        break;
                    }
                    case Item.Bow:
                    {
                        // the bow hand's fist round the grip, the arrow resting on top of it; the string hand's first three
                        // fingers hooked on the string (index above the arrow), the little finger tucked - and springing open
                        // when the arrow goes
                        float de = Smooth(s.Draw);
                        var gl = Grip(false, hold, H, new Vector3(0f, 0.012f, 0.06f), down, new Vector3(0.25f, 0f, 1f), 0.026f);
                        gl.ElbowK = 0.25f;
                        One(m_TL, gl, false, 0);
                        // (drawn, the fingers take the string a little under the nock, so the hand shows below the fletching)
                        var hookAt = Vector3.Lerp(m_BowString, m_BowLow, 0.3f * de);
                        var hook = GripAt(true, null, m_Root, hookAt, Vector3.Lerp(-m_BowUp, (m_BowLow - m_BowString).normalized, de), m_BowDir, HookG).With(Fp.Hook, Tp.Tuck);
                        hook.ElbowK = 0.7f;
                        One(m_TR, hook, true, 0);
                        float t = now - m_BowRelease;
                        openR = t < 0f || t > 0.45f ? 0f : t < 0.05f ? t / 0.05f : 1f - Smooth((t - 0.05f) / 0.4f);
                        sqL = 5f * de;
                        break;
                    }
                    case Item.Crossbow:
                    case Item.Sniper:
                    case Item.Pistol:
                    case Item.Revolver:
                    case Item.Shotgun:
                    case Item.PortalGun:
                    case Item.RocketLauncher:
                        GunGoals(s);
                        float ft = now - m_UseStart;
                        pull = ft >= 0f && ft < 0.2f ? Bump(ft, 0f, 0.2f) : 0f;
                        break;
                    case Item.Ram:
                    case Item.HeavyRam:
                    {
                        // a hand on top of the log on either side, fingers curled over it; the grip tightens as it's
                        // charged and slammed
                        // (on top of the log, where they show: the right hand at the back, the left further along)
                        var nR = new Vector3(0.45f, 0.9f, 0f).normalized;
                        var nL = new Vector3(-nR.x, nR.y, 0f);
                        var cR = Trace(hold, nR + new Vector3(0f, 0f, -0.15f), -nR);
                        var cL = Trace(hold, nL + new Vector3(0f, 0f, 0.2f), -nL);
                        var gR = PalmOn(true, hold, H, cR, nR, new Vector3(-1f, 0f, 0.35f));
                        var gL = PalmOn(false, hold, H, cL, nL, new Vector3(1f, 0f, 0.35f));
                        gR.ElbowK = gL.ElbowK = 0.2f;
                        One(m_TR, gR, true, 0);
                        One(m_TL, gL, false, 0);
                        float hit = Bump((now - m_UseStart) / 0.45f, 0f, 1f);
                        sqR = sqL = 3f + s.RamCharge * 7f + hit * 5f;
                        break;
                    }
                    case Item.Chainsaw:
                    {
                        // right fist on the rear handle with a finger on the throttle, left over the top bar
                        var gr = Grip(true, hold, H, new Vector3(0f, 0.08f, -0.2f), down, fwd, 0.027f);
                        gr.Trigger = true;
                        One(m_TR, gr, true, 0);
                        One(m_TL, Grip(false, hold, H, new Vector3(0f, 0.13f, 0.04f), fwd, Vector3.right, 0.027f, -25f), false, 0);
                        pull = s.Firing ? 1f : 0f;
                        sqL = s.Firing ? 5f : 0f;
                        break;
                    }
                    case Item.None:
                        One(m_TR, Free(Quaternion.identity, Fp.Relaxed, Tp.Relaxed), true, 0);
                        One(m_TL, LeftFree(), false, 0);
                        break;
                    default:
                        HeldGoals(s.Item, hold, ref sqR, ref openR);
                        break;
                }
            }

            Squeeze(m_TR, sqR); Squeeze(m_TL, sqL);
            if (pull > 0f) { m_TR.A[0] += 6f * pull; m_TR.A[1] += 18f * pull; m_TR.A[2] += 10f * pull; }
            OpenUp(m_TR, openR); OpenUp(m_TL, openL);
            Breathe(m_TR, 0f); Breathe(m_TL, 1.7f);
        }

        void GunGoals(State s)
        {
            var H = m_ItemHolder;
            var hold = m_IS.List.Count > 0 ? m_IS : null;
            var gg = Gun(s.Item);
            // the firing hand round the grip, the index finger on the trigger
            var gr = Grip(true, hold, H, gg.C, -gg.Up, Vector3.forward, gg.R);
            gr.Trigger = true;
            One(m_TR, gr, true, 0);
            if (s.Item == Item.Pistol || s.Item == Item.Revolver)
            {
                // two hands round the grip: the left's fingers wrap over the right's (the revolver: only while aiming down
                // the sights; reloading, the left thumbs rounds into the drum)
                var cup = Grip(false, hold, H, gg.C + new Vector3(0f, -0.012f, 0.014f), -gg.Up, Vector3.forward, gg.R + 0.016f);
                cup.Extra = FingerCaps(m_TR, true, H);
                if (s.Item == Item.Pistol) { One(m_TL, cup, false, 0); return; }
                Blend(m_TL, LeftFree(), cup, Smooth(m_AimK), false);
                if (s.Reload >= 0f)
                {
                    float rp = Mathf.Clamp01(s.Reload);
                    float inK = Smooth(Mathf.InverseLerp(0.15f, 0.3f, rp)) * (1f - Smooth(Mathf.InverseLerp(0.8f, 0.9f, rp)));
                    float load = Mathf.InverseLerp(0.32f, 0.8f, rp) * Mathf.Clamp(s.Rounds, 1, 6);
                    float thumb = rp > 0.32f && rp < 0.8f ? Mathf.Sin((load - Mathf.Floor(load)) * Mathf.PI) : 0f;
                    One(m_PC, Free(Quaternion.identity, Fp.Pinch, Tp.Pinch), false, 2);
                    m_PC.A[13] += thumb * 20f; m_PC.A[14] += thumb * 15f;
                    m_TL.Lerp(m_TL, m_PC, inK);
                }
                return;
            }
            // the other hand palm up under the fore-end, fingers curled up its far side (the crossbow's crank: it hooks
            // the string back)
            float r = s.Reload >= 0f ? Mathf.Sin(Mathf.Clamp01(s.Reload) * Mathf.PI) : 0f;
            var under = Trace(hold, new Vector3(0f, -0.6f, gg.SupZ), Vector3.up); // (the underside of the fore-end, from the model)
            var sup = PalmOn(false, hold, H, under, new Vector3(-0.35f, -1f, 0f), new Vector3(1f, 0.3f, 0.35f));
            Blend(m_TL, sup, Free(Quaternion.identity, Fp.Hook, Tp.Tuck), r, false);
        }

        /// <summary>Everything else held in the right hand: a fist round the things with a handle, the plan held by its
        /// bottom edge, small things cupped in the palm, and anything big carried on both palms from underneath.</summary>
        void HeldGoals(Item item, ShapeSet hold, ref float sq, ref float open)
        {
            var H = m_ItemHolder;
            float now = Now;
            var down = Vector3.down;
            var fwd = Vector3.forward;
            float eatK = Mathf.Clamp01((now - m_EatStart) / 0.6f);
            float toMouth = eatK < 1f ? Mathf.Sin(eatK * Mathf.PI) : 0f;
            float throwK = Mathf.Clamp01((now - m_ThrowStart) / 0.35f);
            float useK = Mathf.Clamp01((now - m_UseStart) / 0.3f);
            sq += 5f * toMouth + (useK < 1f ? 4f * Mathf.Sin(useK * Mathf.PI) : 0f);
            // thrown things (C4, the ender pearl, the fort): gripped hard, then let go as they leave the hand
            if (throwK < 1f)
            {
                if (throwK < 0.42f) sq += 6f * Smooth(throwK / 0.42f);
                else open = throwK < 0.7f ? Smooth((throwK - 0.42f) / 0.08f) : 1f - Smooth((throwK - 0.7f) / 0.3f);
            }
            if (hold == null) { One(m_TR, Free(Quaternion.identity, Fp.LooseFist, Tp.Tuck), true, 0); One(m_TL, LeftFree(), false, 0); return; }
            var b = hold.Box;
            HandGoal R;
            bool both = false;
            switch (item)
            {
                case Item.Meat: R = Grip(true, hold, H, new Vector3(0f, 0.05f, -0.13f), Vector3.back, Vector3.left, 0.0175f, 35f); break; // by the bone, palm up
                case Item.InvisPotion: R = Grip(true, hold, H, new Vector3(0f, 0.08f, 0f), down, fwd, 0.075f); break;           // round the round flask
                case Item.SpeedJuice: R = Grip(true, hold, H, new Vector3(0f, 0.09f, 0f), down, fwd, 0.043f); break;            // round the can
                case Item.DeathWand: R = Grip(true, hold, H, Vector3.zero, down, fwd, 0.0225f); sq += 6f * Mathf.Clamp01(Recoil(0.5f)); break;
                case Item.GiantStaff: R = Grip(true, hold, H, new Vector3(0f, -0.05f, 0f), down, fwd, 0.02f); break;
                case Item.Arrow: R = Grip(true, hold, H, new Vector3(0f, 0.1f, 0f), down, fwd, 0.009f); break;
                case Item.Airstrike: R = Grip(true, hold, H, new Vector3(0f, 0.07f, 0f), down, fwd, 0.05f); break;
                case Item.BuildingPlan:
                    // the board by its bottom edge: palm under it, fingers round onto its far face, thumb on the near one
                    R = Grip(true, hold, H, new Vector3(0f, -0.082f, 0.155f), Vector3.left, new Vector3(0f, 0.5f, 0.866f), 0.01f).With(Fp.Paper, Tp.Wrap);
                    break;
                case Item.Berry:
                case Item.EnderPearl:
                    R = SupportUnder(true, hold, new Vector3(b.center.x, b.min.y, b.center.z - b.extents.z * 0.2f)).With(Fp.Cup, Tp.Support);
                    break;
                default:
                    both = b.size.x * H.lossyScale.x > 0.17f;
                    R = both ? Side(true, hold, b) : SupportUnder(true, hold, new Vector3(b.center.x, b.min.y, b.center.z - b.extents.z * 0.2f));
                    break;
            }
            One(m_TR, R, true, 0);
            if (!both) { One(m_TL, LeftFree(), false, 0); return; }
            // big things (chests, workbenches, ladders, walls, armour...): the left hand comes up under the other side
            m_L.localPosition = m_Root.InverseTransformPoint(H.TransformPoint(new Vector3(b.min.x, b.min.y + b.size.y * 0.3f, b.center.z)));
            m_L.localRotation = Mirror(m_R.localRotation);
            One(m_TL, Side(false, hold, b), false, 0);
        }

        /// <summary>A big thing carried in both hands: each palm flat on its side, low down, fingers along it to the front.</summary>
        HandGoal Side(bool right, ShapeSet hold, Bounds b)
        {
            float sx = right ? 1f : -1f;
            var c = new Vector3(right ? b.max.x : b.min.x, b.min.y + b.size.y * 0.3f, b.center.z - b.extents.z * 0.25f);
            var g = PalmOn(right, hold, m_ItemHolder, c, new Vector3(sx, 0f, 0f), new Vector3(0f, -0.35f, 1f));
            g.ElbowK = 0.15f;
            return g;
        }
    }
}
