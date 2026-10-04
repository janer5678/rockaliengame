using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The first-person alien hands: how each one sits on its hand transform and how its claws close round what it holds.
    /// Every frame something moved, each claw and the thumb curl joint by joint (knuckle first) until they touch the item:
    /// the item's meshes are cut through along each digit's own plane (the slab the digit is as wide as), so the claws
    /// wrap the real shape (a handle's square or round section, the bow's grip, a gun's grip, the sides of a C4 brick,
    /// a chest, a potion...) and stop just above its surface instead of going into it. The palm goes onto the item first
    /// (against a handle, under something cradled), and nothing curls into the palm or the claws.
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>
        /// How an alien hand sits on its hand transform. The hand transform is where the old box fist was: its origin is
        /// the grip (items are held by it), +Z runs from the wrist to the knuckles, and the thumb was on its +Y side.
        /// Fist: the hand closes round whatever passes through the origin (a handle), the thumb towards `Dir` (a direction
        /// in view model space; by default the hand's own +Y, like the box thumb). Palm: a more open hand whose palm faces
        /// the point `Dir` (view model space); with `Contact` the palm goes onto the surface in front of it. Either way the
        /// claws close (at most `Curl` of a fist) until they touch what's held. With `HasArm` the wrist bends so the
        /// forearm runs off towards `Arm` (a direction in view model space, from the wrist back towards the elbow)
        /// instead of straight on from the hand: a fist can then sit square round a handle while the arm comes up into
        /// it from below the screen, like a real cocked wrist.
        /// </summary>
        struct HandPose
        {
            public bool Palm, HasDir, Cradle, Contact, HasArm;
            public Vector3 Dir, Arm;
            public float Curl, Radius;
            public HandPose WithArm(Vector3 dir) { HasArm = true; Arm = dir; return this; }
            public static HandPose Fist => new HandPose { Curl = 1f };
            public static HandPose FistThumb(Vector3 dir, float curl = 1f) => new HandPose { HasDir = true, Dir = dir, Curl = curl };
            public static HandPose PalmAt(Vector3 point, float curl, bool contact = false) => new HandPose { Palm = true, HasDir = true, Dir = point, Curl = curl, Contact = contact };
            /// <summary>
            /// Holding something that sits on the hand (food, C4, a chest...): the palm under it, facing its middle `point`
            /// (view model space), on its surface (`radius` is a first guess of how far that is), and the claws curled up
            /// round its sides.
            /// </summary>
            public static HandPose CradleAt(Vector3 point, float radius, float curl = 1f) => new HandPose { Palm = true, Cradle = true, Contact = true, HasDir = true, Dir = point, Radius = radius, Curl = curl };
        }

        /// <summary>One piece of what the hands hold: its transform and its mesh's triangles (shared, cached per mesh).</summary>
        struct HeldPart
        {
            public Transform T;
            public Renderer Rend;
            public Vector3[] V;
            public int[] Tri;
            public Vector3 C;
            public float R;
        }

        static readonly Dictionary<Mesh, List<(Vector3[] v, int[] t)>> s_MeshData = new Dictionary<Mesh, List<(Vector3[], int[])>>();

        /// <summary>
        /// A mesh's shape for the grip, in its separate (unconnected) pieces - a PSX model is often a handle, a head and
        /// a grip made as separate pieces, each near enough convex - and Unity's finely divided sphere as a quicker shell.
        /// </summary>
        /// <summary>For the hands autotest: a mesh's separate pieces, as the grip sees them.</summary>
        public static List<(Vector3[] v, int[] t)> MeshPieces(Mesh m)
        {
            if (m == Art.Sphere) return new List<(Vector3[], int[])> { (m.vertices, m.triangles) }; // (the real sphere, not the grip's shell)
            if (!s_MeshData.TryGetValue(m, out var data)) s_MeshData[m] = data = Pieces(m);
            return data;
        }

        static List<(Vector3[], int[])> Pieces(Mesh m)
        {
            var list = new List<(Vector3[], int[])>();
            if (m == Art.Sphere) { list.Add(Proxy(m)); return list; }
            var v = m.vertices;
            var t = m.triangles;
            // weld by position, then group the triangles by what they're joined to
            var weld = new int[v.Length];
            var seen = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 20000f);
                if (!seen.TryGetValue(k, out int w)) seen[k] = w = i;
                weld[i] = w;
            }
            var par = new int[v.Length];
            for (int i = 0; i < v.Length; i++) par[i] = i;
            int Find(int a) { while (par[a] != a) a = par[a] = par[par[a]]; return a; }
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                par[Find(weld[t[i]])] = Find(weld[t[i + 1]]);
                par[Find(weld[t[i + 1]])] = Find(weld[t[i + 2]]);
            }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int r = Find(weld[t[i]]);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(t[i]); g.Add(t[i + 1]); g.Add(t[i + 2]);
            }
            if (groups.Count <= 1) { list.Add((v, t)); return list; }
            foreach (var g in groups.Values)
            {
                var map = new Dictionary<int, int>();
                var pv = new List<Vector3>();
                var pt = new int[g.Count];
                for (int i = 0; i < g.Count; i++)
                {
                    if (!map.TryGetValue(g[i], out int k)) { map[g[i]] = k = pv.Count; pv.Add(v[g[i]]); }
                    pt[i] = k;
                }
                list.Add((pv.ToArray(), pt));
            }
            return list;
        }

        /// <summary>
        /// A mesh's shape for the grip: itself, or for a finely divided one (Unity's sphere has 768 triangles) a 320-face
        /// shell round it - each corner pushed out to the mesh's furthest extent that way, the whole a little bigger so
        /// its faces are outside the mesh - which is cut through far quicker.
        /// </summary>
        static (Vector3[], int[]) Proxy(Mesh m)
        {
            var v = m.vertices;
            var c = m.bounds.center;
            var (dirs, tris) = IcoSphere(2);
            var pv = new Vector3[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                float most = 0f;
                foreach (var q in v) most = Mathf.Max(most, Vector3.Dot(q - c, dirs[i]));
                pv[i] = c + dirs[i] * most * 1.02f;
            }
            return (pv, tris);
        }

        static (Vector3[], int[]) IcoSphere(int subdivisions)
        {
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var vs = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            for (int i = 0; i < vs.Count; i++) vs[i] = vs[i].normalized;
            var fs = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int sdiv = 0; sdiv < subdivisions; sdiv++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int k)) return k;
                    vs.Add(((vs[a] + vs[b]) * 0.5f).normalized);
                    return mid[key] = vs.Count - 1;
                }
                var nf = new List<int>();
                for (int f = 0; f < fs.Count; f += 3)
                {
                    int a = fs[f], b = fs[f + 1], c = fs[f + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                fs = nf;
            }
            return (vs.ToArray(), fs.ToArray());
        }

        /// <summary>
        /// Everything the claws can touch: the item's own pieces (whichever look is showing: the PSX model's pieces in PSX
        /// graphics), the ball, the nocked arrow and the bowstring.
        /// </summary>
        void GatherParts()
        {
            m_Parts.Clear();
            foreach (var go in new[] { m_Item, m_Ball, m_Arrow, m_StringA, m_StringB })
            {
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    var rend = mf.GetComponent<Renderer>();
                    if (m == null || !m.isReadable || rend == null) continue;
                    if (!s_MeshData.TryGetValue(m, out var data)) s_MeshData[m] = data = Pieces(m);
                    foreach (var piece in data)
                    {
                        var b = new Bounds(piece.v[0], Vector3.zero);
                        foreach (var q in piece.v) b.Encapsulate(q);
                        m_Parts.Add(new HeldPart { T = mf.transform, Rend = rend, V = piece.v, Tri = piece.t, C = b.center, R = b.extents.magnitude });
                    }
                }
            }
        }

        /// <summary>
        /// The alien's own forearm and clawed hand (cut from the rigged player model by Tools/psx_convert.py), tinted in
        /// the team colour like the body. The mesh: hand along +Z, wrist at z = 0, knuckles at z = 0.077, claw tips up to
        /// z = 0.226, elbow at z = -0.22; the right palm faces (0.96, 0.29) in XY and its thumb (0.29, -0.96) (the left
        /// arm is its mirror image). Rather than being squeezed into the old box (which put the hand where the forearm
        /// was), it's fitted by its hand: the claws curl round the grip (the hand's origin, where the box fist was), the
        /// wrist ends up where the box wrist band was, and the forearm is stretched back so it runs off the edge of the
        /// screen like a real arm. The model's claws are very long (over twice the palm), far too long to close round a
        /// handle, so they're shortened to `Length` of it.
        /// </summary>
        class AlienArm
        {
            public Transform Fit;
            Transform m_MeshT, m_ModelT;
            Mesh m_Mesh;
            Vector3[] m_Base, m_Work, m_U, m_Posed;
            int[] m_Tris;
            Matrix4x4 m_ToModel, m_ToMesh;
            Vector2 m_Thumb, m_PalmN;
            Vector3 m_PalmN3, m_FistAnchor = new Vector3(0, 0, Knuckle);
            /// <summary>How the forearm turns at the wrist (round m_WristPivot), and what the mesh shows now.</summary>
            Quaternion m_WristQ = Quaternion.identity, m_ShownWristQ = Quaternion.identity;
            Vector3 m_WristPivot;

            public const float Scale = 1.65f; // (a hand as broad as a person's next to the items, so a fist covers a handle)
            const float Knuckle = 0.09f;    // where the claws start (model z), roughly
            const float DigitZ = 0.08f;     // past this (model z) the mesh is three separate digits: two claws and the thumb
            const float ArmStretch = 0.3f;  // the elbow end is moved back this much (model units) so the arm reaches off screen
            const float ElbowWiden = 1.15f;
            /// <summary>The wrist: the forearm turns fully past WristLo (model z), the band up to WristHi stretching over the bend.</summary>
            const float WristHi = -0.015f, WristLo = -0.06f;
            /// <summary>The furthest the wrist bends (degrees).</summary>
            const float MaxWrist = 75f;
            /// <summary>How much of each digit's length is kept (upper claw, long claw, thumb), from its knuckle out.</summary>
            static readonly float[] Length = { 0.64f, 0.64f, 0.82f };
            /// <summary>How far (model units) the claws stop above a surface.</summary>
            const float Gap = 0.0016f;

            /// <summary>
            /// A digit's joint: the vertices past `Hi` (model z, before bending) turn rigidly by `Angle` round `Axis`
            /// through `Pivot` (on the palm side of the joint); between `Lo` and `Hi` is the joint's own short band,
            /// which stretches over it like a knuckle. (Lo = Hi = 0: the whole digit turns, i.e. the knuckle on the palm.)
            /// </summary>
            struct Joint { public float Lo, Hi; public Vector3 Pivot, Axis; }
            /// <summary>Per vertex: -1 the palm and arm, 0 the upper claw, 1 the long claw, 2 the thumb.</summary>
            int[] m_Digit;
            /// <summary>Per vertex: which segment of its digit (0 from the knuckle, 1, 2 the tip); -1 the palm and arm.</summary>
            int[] m_Seg;
            /// <summary>Per vertex: the digit whose knuckle ring (on the palm, where it starts) this is, or -1.</summary>
            int[] m_Ring;
            Joint[][] m_Joints;
            // the joint bands of each digit (model z, from the mesh: each segment is a box with a short band at each joint),
            // the knuckle's z and how far (degrees) each joint bends in a full fist
            static readonly float[][] Bands =
            {
                new[] { 0.077f, 0.1425f, 0.1452f, 0.1728f, 0.1753f }, // upper claw: knuckle, middle joint, last joint
                new[] { 0.078f, 0.1345f, 0.1388f, 0.1840f, 0.1864f }, // long claw
                new[] { 0.072f, 0.0956f, 0.0985f, 0.1268f, 0.1298f }, // thumb
            };
            static readonly float[][] FistAngles =
            {
                new[] { 90f, 95f, 85f },
                new[] { 90f, 90f, 85f },
                new[] { 40f, 60f, 55f },
                new[] { 80f, 85f, 75f }, // the thumb wrapped round a handle alongside the claws
            };

            /// <summary>Each digit as the grip sees it: the plane it curls in and its segments' outlines in that plane.</summary>
            class Digit
            {
                public Vector3 O, A, E2;          // origin (the knuckle's pivot), curl axis, curl direction (the plane's x is +Z)
                public float AMin, AMax;          // the digit's width along A (the slab cut out of the item)
                public Vector2[][] Rest = new Vector2[3][]; // each segment's points (2D, unbent)
                public Vector2[] Ring;            // the knuckle's ring on the palm (stays put: segment 0 stretches from it)
                public Vector2[][] Hull = new Vector2[3][]; // segments 1, 2: their outlines (counter-clockwise)
                public Vector2[] Piv = new Vector2[3];
                public float[] Max = new float[3];
                public Poly Palm;                 // the palm's outline in this plane
                public Poly[] Straight = new Poly[3]; // each segment's outline, straight out
                public int[][] SegTris = new int[3][]; // each segment's triangles (vertex indices), to cut it into the thumb's plane
            }

            /// <summary>A convex outline in a digit's plane, with a bounding circle.</summary>
            class Poly
            {
                public Vector2[] P = new Vector2[8];
                public int N;
                public Vector2 C;
                public float R;
                public void Bound()
                {
                    C = Vector2.zero;
                    for (int i = 0; i < N; i++) C += P[i];
                    if (N > 0) C /= N;
                    R = 0f;
                    for (int i = 0; i < N; i++) R = Mathf.Max(R, (P[i] - C).magnitude);
                }
                public void Move(Vector2 d)
                {
                    for (int i = 0; i < N; i++) P[i] += d;
                    C += d;
                }
            }

            Digit[] m_D;
            /// <summary>The whole palm (its full width, wrist to knuckles) in the claws' plane: where it goes onto things.</summary>
            Digit m_PalmF;
            /// <summary>The palm in strips across its width (each with its own outline), so a handle across it is met where it really is.</summary>
            Digit[] m_PalmS;
            /// <summary>Each joint's angle (degrees; digit * 3 + joint) and what the mesh shows now.</summary>
            readonly float[] m_Ang = new float[9], m_Shown = new float[9];
            bool m_Bent, m_Wrap, m_ShownWrap, m_SolvedWrap;
            float m_SolveMs;
            readonly Dictionary<long, (float[] ang, Vector3 shift, bool wrap)> m_Known = new Dictionary<long, (float[], Vector3, bool)>();
            // the last solve: what it was for, and where it put the hand (model units, from the anchor)
            Vector4 m_Sig;
            int m_SigN = -1;
            Vector3 m_Shift;
            // scratch
            readonly List<Vector3[]> m_ItemV = new List<Vector3[]>();
            readonly List<int[]> m_ItemT = new List<int[]>();
            readonly List<Vector4> m_ItemS = new List<Vector4>(); // each piece's bounding sphere (model space: centre, radius)
            readonly List<Poly> m_PolyPool = new List<Poly>();
            int m_PolyUsed;
            readonly List<Poly>[] m_Obs = { new List<Poly>(), new List<Poly>(), new List<Poly>(), new List<Poly>(), new List<Poly>(), new List<Poly>(), new List<Poly>(), new List<Poly>() };
            const int Strips = 5; // the palm, cut lengthways into strips (lists 3.. of m_Obs)
            static readonly List<Vector2> s_Pts = new List<Vector2>(512);
            static readonly List<Vector2> s_Pts2 = new List<Vector2>(512);
            static Vector2[] s_Hull = new Vector2[64];
            static float[] s_A = new float[256];
            static Vector2[] s_P = new Vector2[256];

            /// <summary>
            /// An arm on its hand transform. blocky: the game's square block hand (Blockify: the alien arm's palm,
            /// forearm, claws and thumb each turned into square blocks, which bend and close round things exactly like
            /// the claws did); otherwise the alien's own clawed arm (the look before; kept for the hands autotest).
            /// </summary>
            public static AlienArm Make(Transform hand, bool right, Color tint, Color team, bool blocky)
            {
                var fit = new GameObject("alien fit").transform;
                fit.SetParent(hand, false);
                var model = PsxModels.Spawn(right ? "alienarm_r" : "alienarm_l", fit);
                var mf = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) { Object.Destroy(fit.gameObject); return null; }
                if (blocky) model.name = right ? "blocky arm_r" : "blocky arm_l";
                var a = new AlienArm { Fit = fit, m_MeshT = mf.transform, m_ModelT = model.transform };
                a.m_Mesh = Object.Instantiate(mf.sharedMesh);
                a.m_Mesh.name = "alien arm (posed)";
                mf.sharedMesh = a.m_Mesh;
                a.m_Base = a.m_Mesh.isReadable ? a.m_Mesh.vertices : new Vector3[0]; // (Read/Write is on for it: PsxImport)
                a.m_Tris = a.m_Mesh.isReadable ? a.m_Mesh.triangles : new int[0];
                a.m_Work = new Vector3[a.m_Base.Length];
                a.m_Posed = new Vector3[a.m_Base.Length];
                a.m_ToModel = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                a.m_ToMesh = a.m_ToModel.inverse;
                a.m_PalmN = new Vector2(right ? 0.957f : -0.957f, 0.29f);
                a.m_Thumb = new Vector2(right ? 0.29f : -0.29f, -0.957f);
                a.m_PalmN3 = new Vector3(a.m_PalmN.x, a.m_PalmN.y, 0f).normalized;
                a.FindDigits();
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.materials; // own copies, tinted
                    foreach (var mt in mats) { mt.SetColor("_BaseColor", tint); mt.color = tint; }
                    r.materials = mats;
                }
                if (blocky)
                {
                    if (a.m_D != null && a.Blockify())
                    {
                        var r = mf.GetComponent<Renderer>();
                        if (r != null)
                        {
                            var cols = BlockyColours(tint, team);
                            var mats = new Material[3];
                            for (int i = 0; i < 3; i++) mats[i] = Art.NewMat(cols[i]);
                            r.sharedMaterials = mats;
                        }
                    }
                    else Debug.LogWarning("[RockGame] blocky hands: the alien arm couldn't be cut into blocks; showing its claws");
                }
                for (int d = 0; d < 3; d++) for (int j = 0; j < 3; j++) a.m_Ang[d * 3 + j] = FistAngles[d][j];
                a.m_Wrap = false;
                a.Bend();
                return a;
            }

            /// <summary>
            /// Turns the alien arm into the square block hand (like the old box fist and forearm: flat-coloured boxes with
            /// a darker knuckle row and a band round the wrist), in place: every piece of the alien hand - the palm, the
            /// forearm, each claw and the thumb segment by segment - becomes a square block over the same bones (the
            /// digits a square tube with a joint at each of the claw's joints), and the hand's own data (which digit and
            /// segment each corner belongs to, the grip's outlines) is worked out again from the blocks. So the blocks
            /// bend at the claws' joints and close round what's held - the palm against a handle, the fingers stopping on
            /// its surface - exactly as the claws did: every grip pose stays as it was, only the shape is square.
            /// </summary>
            bool Blockify()
            {
                int n0 = m_U.Length;
                if (n0 == 0 || m_D == null) return false;
                var verts = new List<Vector3>();
                var dig = new List<int>(); var seg = new List<int>(); var ring = new List<int>();
                var subs = new[] { new List<int>(), new List<int>(), new List<int>() };

                // the ring of four corners round c (square across axes a, e: half-widths ha, he)
                Vector3[] Ring(Vector3 c, Vector3 a, Vector3 e, float ha, float he)
                    => new[] { c - a * ha - e * he, c + a * ha - e * he, c + a * ha + e * he, c - a * ha + e * he };
                // a face: four corners (any winding: turned to face away from `inside`), all tagged alike per corner
                void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, (int d, int s, int r) t0, (int d, int s, int r) t1, (int d, int s, int r) t2, (int d, int s, int r) t3, Vector3 inside, int mat)
                {
                    var nrm = Vector3.Cross(p1 - p0, p2 - p0) + Vector3.Cross(p2 - p0, p3 - p0);
                    var mid = (p0 + p1 + p2 + p3) * 0.25f;
                    bool flip = Vector3.Dot(nrm, mid - inside) < 0f;
                    int b = verts.Count;
                    foreach (var (p, t) in new[] { (p0, t0), (p1, t1), (p2, t2), (p3, t3) }) { verts.Add(p); dig.Add(t.d); seg.Add(t.s); ring.Add(t.r); }
                    // (Unity: a triangle faces the way of the cross product of its first two edges)
                    if (!flip) subs[mat].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    else subs[mat].AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
                // a block between two rings (the four sides; caps where asked), corners tagged by their ring
                void Prism(Vector3[] r0, Vector3[] r1, (int d, int s, int r) t0, (int d, int s, int r) t1, int mat, bool cap0, bool cap1)
                {
                    var inside = (r0[0] + r0[2] + r1[0] + r1[2]) * 0.25f;
                    for (int i = 0; i < 4; i++)
                    {
                        int j = (i + 1) % 4;
                        Quad(r0[i], r0[j], r1[j], r1[i], t0, t0, t1, t1, inside, mat);
                    }
                    if (cap0) Quad(r0[0], r0[1], r0[2], r0[3], t0, t0, t0, t0, inside, mat);
                    if (cap1) Quad(r1[0], r1[1], r1[2], r1[3], t1, t1, t1, t1, inside, mat);
                }

                // ---- the digits: a square tube each, from inside the palm out past the knuckle to a square tip ----
                for (int d = 0; d < 3; d++)
                {
                    var g = m_D[d];
                    // the claw's own middle line (its knuckle ring and each segment's middle) and thickness
                    var mids = new List<Vector3>();
                    Vector3 cr = Vector3.zero; int crn = 0;
                    for (int i = 0; i < n0; i++) if (m_Ring[i] == d) { cr += m_U[i]; crn++; }
                    if (crn == 0) return false;
                    mids.Add(cr / crn);
                    float aLo = 9f, aHi = -9f, eLo = 9f, eHi = -9f, tip = -9f;
                    for (int s = 0; s < 3; s++)
                    {
                        Vector3 cs = Vector3.zero; int cn = 0;
                        for (int i = 0; i < n0; i++)
                        {
                            if (m_Digit[i] != d) continue;
                            tip = Mathf.Max(tip, m_U[i].z);
                            if (m_Seg[i] != s) continue;
                            cs += m_U[i]; cn++;
                            if (s < 2)
                            {
                                float av = Vector3.Dot(m_U[i], g.A), ev = Vector3.Dot(m_U[i], g.E2);
                                aLo = Mathf.Min(aLo, av); aHi = Mathf.Max(aHi, av); eLo = Mathf.Min(eLo, ev); eHi = Mathf.Max(eHi, ev);
                            }
                        }
                        if (cn == 0) return false;
                        mids.Add(cs / cn);
                    }
                    // square: as thick as the claw's width and depth on average
                    float h = Mathf.Max(0.004f, ((aHi - aLo) + (eHi - eLo)) * 0.25f);
                    Vector3 Mid(float z)
                    {
                        // along the middle line (straight on past its ends)
                        int k = 0;
                        while (k < mids.Count - 2 && z > mids[k + 1].z) k++;
                        var p = mids[k]; var q = mids[k + 1];
                        float t = Mathf.Abs(q.z - p.z) > 1e-5f ? (z - p.z) / (q.z - p.z) : 0f;
                        var m = Vector3.LerpUnclamped(p, q, t);
                        m.z = z;
                        return m;
                    }
                    var b = Bands[d];
                    float lo1 = Short(d, b[1]), hi1 = Short(d, b[2]), lo2 = Short(d, b[3]), hi2 = Short(d, b[4]);
                    float tipZ = hi2 + (tip - hi2) * 0.8f; // (the claw's point is long: the square tip stops short of it)
                    float[] zs = { 0.066f, b[0], lo1, hi1, lo2, hi2, tipZ };
                    // each ring's tags: the two inside the palm stay put (the knuckle ring); the rest are the digit's,
                    // segment 0 up to the first joint, then 1, then 2
                    (int, int, int)[] tags = { (-1, -1, d), (-1, -1, d), (d, 0, -1), (d, 1, -1), (d, 1, -1), (d, 2, -1), (d, 2, -1) };
                    var rings = new Vector3[zs.Length][];
                    for (int k = 0; k < zs.Length; k++) rings[k] = Ring(Mid(zs[k]), g.A, g.E2, h, h);
                    for (int k = 0; k + 1 < zs.Length; k++)
                        Prism(rings[k], rings[k + 1], tags[k], tags[k + 1], k <= 1 ? 1 : 0, k == 0, k == zs.Length - 2);
                }

                // ---- the palm, the wrist band and the forearm (the palm's own frame) ----
                var en = m_PalmN3;
                var ap = Vector3.Cross(Vector3.forward, en).normalized;
                // the middle and half-widths of the alien's palm / arm between two depths (not the digits or their rings)
                bool Section(float zLo, float zHi, out Vector3 c, out float ha, out float he)
                {
                    c = Vector3.zero; ha = he = 0f;
                    float a0 = 9f, a1 = -9f, e0 = 9f, e1 = -9f; int cn = 0;
                    for (int i = 0; i < n0; i++)
                    {
                        if (m_Digit[i] >= 0 || m_Ring[i] >= 0 || m_U[i].z < zLo || m_U[i].z > zHi) continue;
                        float av = Vector3.Dot(m_U[i], ap), ev = Vector3.Dot(m_U[i], en);
                        a0 = Mathf.Min(a0, av); a1 = Mathf.Max(a1, av); e0 = Mathf.Min(e0, ev); e1 = Mathf.Max(e1, ev); cn++;
                    }
                    if (cn == 0) return false;
                    ha = (a1 - a0) * 0.5f; he = (e1 - e0) * 0.5f;
                    c = ap * ((a0 + a1) * 0.5f) + en * ((e0 + e1) * 0.5f);
                    return true;
                }
                (int, int, int) still = (-1, -1, -1);
                if (!Section(0f, 0.066f, out var pc, out var pa, out var pe)) return false;
                Prism(Ring(pc, ap, en, pa, pe), Ring(pc + Vector3.forward * 0.065f, ap, en, pa, pe), still, still, 0, true, true);
                float armLo = 9f;
                for (int i = 0; i < n0; i++) if (m_Digit[i] < 0) armLo = Mathf.Min(armLo, m_U[i].z);
                if (!Section(-0.11f, -0.03f, out var wc, out var wa, out var we)) { wc = pc; wa = pa * 0.8f; we = pe * 0.8f; }
                if (!Section(armLo, armLo + 0.04f, out var ec, out var ea, out var ee)) { ec = wc; ea = wa; ee = we; }
                // (the forearm square, as thick as it is at the wrist, the elbow end only a little bigger)
                float arm = (wa + we) * 0.5f, elbow = Mathf.Min((ea + ee) * 0.5f, arm * 1.25f);
                wc.z = WristLo; ec.z = armLo;
                Prism(Ring(ec, ap, en, elbow, elbow), Ring(wc, ap, en, arm, arm), still, still, 0, true, false);
                // the wrist band (team coloured, like the old box arm's): from the forearm to the palm, over the bend
                var bc = pc; bc.z = 0.004f;
                Prism(Ring(wc, ap, en, arm * 1.12f, arm * 1.12f), Ring(bc, ap, en, Mathf.Max(pa, arm) * 1.06f, Mathf.Max(pe, arm) * 1.06f), still, still, 2, true, true);

                // ---- swap the arm over to the blocks ----
                int n = verts.Count;
                m_U = verts.ToArray();
                m_Digit = dig.ToArray(); m_Seg = seg.ToArray(); m_Ring = ring.ToArray();
                var all = new List<int>();
                foreach (var sl in subs) all.AddRange(sl);
                m_Tris = all.ToArray();
                m_Base = new Vector3[n];
                m_Work = new Vector3[n];
                m_Posed = new Vector3[n];
                // corners at the same place (to smooth the shading over, when that's on)
                m_Weld = new int[n];
                var seen = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < n; i++)
                {
                    var k = Vector3Int.RoundToInt(m_U[i] * 100000f);
                    if (!seen.TryGetValue(k, out int w)) seen[k] = w = seen.Count;
                    m_Weld[i] = w;
                }
                m_WeldCount = seen.Count;
                var mesh = new Mesh { name = BlockyMeshName };
                var mv = new Vector3[n];
                for (int i = 0; i < n; i++) mv[i] = m_ToMesh.MultiplyPoint3x4(m_U[i]);
                mesh.vertices = mv;
                mesh.subMeshCount = 3;
                for (int s = 0; s < 3; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.MarkDynamic();
                if (m_Mesh) Object.Destroy(m_Mesh);
                m_Mesh = mesh;
                m_MeshT.GetComponent<MeshFilter>().sharedMesh = mesh;
                m_Blocky = true;
                m_Bent = false;
                BuildDigits(m_ThumbIn);
                return true;
            }

            bool m_Blocky;
            /// <summary>The block hand's corners grouped by place (for smooth shading), and how many places.</summary>
            int[] m_Weld;
            int m_WeldCount;
            Vector3 m_ThumbIn;
            bool m_ShownSmooth;
            /// <summary>(tests) This is the block hand and its corners are lit smoothly now.</summary>
            public bool ShownSmooth => m_Blocky && m_ShownSmooth;
            public bool IsBlocky => m_Blocky;

            /// <summary>
            /// Splits the hand past the knuckles into its three digits (the mesh's separate pieces there: the claw that ends
            /// furthest out is the long claw, the one that starts nearest the wrist the thumb), puts a joint at each of
            /// their knuckles, shortens them, and works out each one's plane for the grip.
            /// </summary>
            void FindDigits()
            {
                int n = m_Base.Length;
                m_Digit = new int[n];
                m_Seg = new int[n];
                m_Ring = new int[n];
                m_U = new Vector3[n];
                for (int i = 0; i < n; i++) { m_Digit[i] = -1; m_Seg[i] = -1; m_Ring[i] = -1; }
                if (n == 0) return;
                var u = new Vector3[n];
                for (int i = 0; i < n; i++) u[i] = m_ToModel.MultiplyPoint3x4(m_Base[i]);
                for (int i = 0; i < n; i++) m_U[i] = Stretch(u[i]);
                // the wrist's pivot: the middle of the arm where the hand starts
                Vector3 wc = Vector3.zero; int wn = 0;
                for (int i = 0; i < n; i++) if (Mathf.Abs(u[i].z) < 0.012f) { wc += u[i]; wn++; }
                m_WristPivot = wn > 0 ? new Vector3(wc.x / wn, wc.y / wn, 0f) : Vector3.zero;
                // weld the split vertices by position, then join everything connected past the knuckles
                var weld = new int[n];
                var seen = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < n; i++)
                {
                    var k = Vector3Int.RoundToInt(u[i] * 10000f);
                    if (!seen.TryGetValue(k, out int w)) seen[k] = w = i;
                    weld[i] = w;
                }
                var par = new int[n];
                for (int i = 0; i < n; i++) par[i] = i;
                int Find(int a) { while (par[a] != a) a = par[a] = par[par[a]]; return a; }
                for (int t = 0; t < m_Tris.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = m_Tris[t + e], b = m_Tris[t + (e + 1) % 3];
                        if (u[a].z > DigitZ && u[b].z > DigitZ) par[Find(weld[a])] = Find(weld[b]);
                    }
                var islands = new Dictionary<int, Vector2>(); // root -> (min z, max z)
                for (int i = 0; i < n; i++)
                {
                    if (u[i].z <= DigitZ) continue;
                    int r = Find(weld[i]);
                    var mm = islands.TryGetValue(r, out var v) ? v : new Vector2(9f, -9f);
                    islands[r] = new Vector2(Mathf.Min(mm.x, u[i].z), Mathf.Max(mm.y, u[i].z));
                }
                if (islands.Count != 3) { Debug.LogWarning($"alien arm: expected 3 digits past the knuckles, found {islands.Count}; the claws won't bend"); return; }
                int thumb = -1, longest = -1;
                foreach (var kv in islands)
                {
                    if (thumb < 0 || kv.Value.x < islands[thumb].x) thumb = kv.Key;
                    if (longest < 0 || kv.Value.y > islands[longest].y) longest = kv.Key;
                }
                for (int i = 0; i < n; i++)
                    if (u[i].z > DigitZ)
                    {
                        int r = Find(weld[i]);
                        int d = m_Digit[i] = r == thumb ? 2 : r == longest ? 1 : 0;
                        var b = Bands[d];
                        m_Seg[i] = u[i].z < (b[1] + b[2]) * 0.5f ? 0 : u[i].z < (b[3] + b[4]) * 0.5f ? 1 : 2;
                    }

                // the joints: each digit curls towards the palm (the thumb in towards the claws as well), round a pivot on
                // the palm side of the joint
                var palm = m_PalmN3;
                var thumbIn = (palm * 1.5f - new Vector3(m_Thumb.x, m_Thumb.y, 0f)).normalized; // (over the curled claws in a fist)
                m_ThumbIn = thumbIn;
                m_Joints = new Joint[4][];
                var ringC = new Vector2[3];
                for (int f = 0; f < 4; f++)
                {
                    int d = f == 3 ? 2 : f;
                    var dir = f == 2 ? thumbIn : palm;
                    var axis = Vector3.Cross(Vector3.forward, dir);
                    float first = 9f;
                    for (int i = 0; i < n; i++) if (m_Digit[i] == d) first = Mathf.Min(first, u[i].z);
                    m_Joints[f] = new Joint[3];
                    for (int j = 0; j < 3; j++)
                    {
                        float lo = j == 0 ? 0f : Bands[d][j * 2 - 1], hi = j == 0 ? 0f : Bands[d][j * 2];
                        float z = j == 0 ? Bands[d][0] : (lo + hi) * 0.5f;
                        // the digit's cross-section at the joint (the knuckle: its first ring)
                        float zA = j == 0 ? first - 0.001f : lo - 0.008f, zB = j == 0 ? first + 0.008f : hi + 0.008f;
                        Vector2 c = Vector2.zero; int cnt = 0; float most = -9f;
                        for (int i = 0; i < n; i++)
                        {
                            if (m_Digit[i] != d || u[i].z < zA || u[i].z > zB) continue;
                            c += new Vector2(u[i].x, u[i].y); cnt++;
                            most = Mathf.Max(most, Vector3.Dot(new Vector3(u[i].x, u[i].y, 0f), dir));
                        }
                        if (cnt > 0) c /= cnt;
                        if (j == 0 && f < 3) ringC[d] = c;
                        var cv = new Vector3(c.x, c.y, 0f);
                        var pivot = cv + dir * (cnt > 0 ? most - Vector3.Dot(cv, dir) : 0f);
                        pivot.z = Short(d, z);
                        m_Joints[f][j] = new Joint { Lo = Short(d, lo), Hi = Short(d, hi), Pivot = pivot, Axis = axis };
                    }
                }
                // the knuckle rings: the palm's vertices where each digit starts (segment 0 stretches from them)
                for (int i = 0; i < n; i++)
                {
                    if (m_Digit[i] >= 0 || u[i].z < 0.066f || u[i].z > DigitZ) continue;
                    int best = -1; float bd = 0.022f;
                    for (int d = 0; d < 3; d++)
                    {
                        float dd = (new Vector2(u[i].x, u[i].y) - ringC[d]).magnitude;
                        if (dd < bd) { bd = dd; best = d; }
                    }
                    m_Ring[i] = best;
                }
                // shorten the digits (from their knuckles out)
                for (int i = 0; i < n; i++)
                    if (m_Digit[i] >= 0) m_U[i].z = Short(m_Digit[i], m_U[i].z);
                BuildDigits(thumbIn);
            }

            static float Short(int d, float z) => z <= Bands[d][0] ? z : Bands[d][0] + (z - Bands[d][0]) * Length[d];

            static Vector3 Stretch(Vector3 u)
            {
                if (u.z >= -0.1f) return u;
                // the elbow end: further back, a little thicker
                return new Vector3(u.x * ElbowWiden, u.y * ElbowWiden, u.z - ArmStretch);
            }

            void BuildDigits(Vector3 thumbIn)
            {
                int n = m_U.Length;
                m_D = new Digit[4];
                for (int f = 0; f < 4; f++)
                {
                    int d = f == 3 ? 2 : f;
                    var g = new Digit { E2 = f == 2 ? thumbIn : m_PalmN3 };
                    g.A = Vector3.Cross(Vector3.forward, g.E2);
                    g.O = m_Joints[f][0].Pivot;
                    float amin = 9f, amax = -9f;
                    var segs = new[] { new List<Vector2>(), new List<Vector2>(), new List<Vector2>() };
                    var ring = new List<Vector2>();
                    for (int i = 0; i < n; i++)
                    {
                        bool mine = m_Digit[i] == d, r = m_Ring[i] == d;
                        if (!mine && !r) continue;
                        float a = Vector3.Dot(m_U[i] - g.O, g.A);
                        amin = Mathf.Min(amin, a); amax = Mathf.Max(amax, a);
                        if (mine) segs[m_Seg[i]].Add(P2(g, m_U[i]));
                        else ring.Add(P2(g, m_U[i]));
                    }
                    g.AMin = amin - 0.001f;
                    g.AMax = amax + 0.001f;
                    for (int s = 0; s < 3; s++)
                    {
                        g.Rest[s] = segs[s].ToArray();
                        g.Piv[s] = P2(g, m_Joints[f][s].Pivot);
                        g.Max[s] = FistAngles[f][s];
                        if (s > 0)
                        {
                            s_Pts.Clear(); s_Pts.AddRange(segs[s]);
                            int hn = Hull(s_Pts, ref s_Hull);
                            g.Hull[s] = new Vector2[hn];
                            System.Array.Copy(s_Hull, g.Hull[s], hn);
                        }
                        var tl = new List<int>();
                        for (int t = 0; t < m_Tris.Length; t += 3)
                        {
                            bool all = true;
                            for (int e = 0; e < 3 && all; e++)
                            {
                                int v = m_Tris[t + e];
                                all = (m_Digit[v] == d && m_Seg[v] == s) || (s == 0 && m_Ring[v] == d);
                            }
                            if (all) { tl.Add(m_Tris[t]); tl.Add(m_Tris[t + 1]); tl.Add(m_Tris[t + 2]); }
                        }
                        g.SegTris[s] = tl.ToArray();
                    }
                    g.Ring = ring.ToArray();
                    // the palm in this plane (what the digit mustn't curl into)
                    var pt = new List<int>();
                    for (int t = 0; t < m_Tris.Length; t += 3)
                    {
                        bool all = true;
                        for (int e = 0; e < 3 && all; e++) { int v = m_Tris[t + e]; all = m_Digit[v] < 0 && m_U[v].z >= 0f; }
                        if (all) { pt.Add(m_Tris[t]); pt.Add(m_Tris[t + 1]); pt.Add(m_Tris[t + 2]); }
                    }
                    s_Pts.Clear();
                    Slice(g, m_U, pt.ToArray(), Vector3.zero, s_Pts);
                    g.Palm = new Poly();
                    int pn = Hull(s_Pts, ref s_Hull);
                    g.Palm.P = new Vector2[Mathf.Max(1, pn)];
                    System.Array.Copy(s_Hull, g.Palm.P, pn);
                    g.Palm.N = pn;
                    g.Palm.Bound();
                    // each segment straight out (where an open hand starts from)
                    for (int s = 0; s < 3; s++)
                    {
                        s_Pts.Clear();
                        s_Pts.AddRange(g.Rest[s]);
                        if (s == 0) s_Pts.AddRange(g.Ring);
                        int sn = Hull(s_Pts, ref s_Hull);
                        g.Straight[s] = new Poly { P = new Vector2[Mathf.Max(1, sn)], N = sn };
                        System.Array.Copy(s_Hull, g.Straight[s].P, sn);
                        g.Straight[s].Bound();
                    }
                    m_D[f] = g;
                }
                {
                    var g = m_PalmF = new Digit { E2 = m_PalmN3, A = Vector3.Cross(Vector3.forward, m_PalmN3), O = m_Joints[1][0].Pivot };
                    float amin = 9f, amax = -9f;
                    for (int i = 0; i < n; i++)
                    {
                        if (m_Digit[i] >= 0 || m_U[i].z < -0.03f) continue;
                        float a = Vector3.Dot(m_U[i] - g.O, g.A);
                        amin = Mathf.Min(amin, a); amax = Mathf.Max(amax, a);
                    }
                    g.AMin = amin; g.AMax = amax;
                    var pt = new List<int>();
                    for (int t = 0; t < m_Tris.Length; t += 3)
                    {
                        bool all = true;
                        for (int e = 0; e < 3 && all; e++) { int v = m_Tris[t + e]; all = m_Digit[v] < 0 && m_U[v].z >= -0.03f; }
                        if (all) { pt.Add(m_Tris[t]); pt.Add(m_Tris[t + 1]); pt.Add(m_Tris[t + 2]); }
                    }
                    s_Pts.Clear();
                    Slice(g, m_U, pt.ToArray(), Vector3.zero, s_Pts);
                    int pn = Hull(s_Pts, ref s_Hull);
                    g.Palm = new Poly { P = new Vector2[Mathf.Max(1, pn)], N = pn };
                    System.Array.Copy(s_Hull, g.Palm.P, pn);
                    g.Palm.Bound();
                    m_PalmS = new Digit[Strips];
                    var ptris = pt.ToArray();
                    for (int k = 0; k < Strips; k++)
                    {
                        var st = m_PalmS[k] = new Digit { E2 = g.E2, A = g.A, O = g.O, AMin = Mathf.Lerp(amin, amax, k / (float)Strips), AMax = Mathf.Lerp(amin, amax, (k + 1) / (float)Strips) };
                        s_Pts.Clear();
                        Slice(st, m_U, ptris, Vector3.zero, s_Pts);
                        int sn = Hull(s_Pts, ref s_Hull);
                        st.Palm = new Poly { P = new Vector2[Mathf.Max(1, sn)], N = sn };
                        System.Array.Copy(s_Hull, st.Palm.P, sn);
                        st.Palm.Bound();
                    }
                }
                // a fist closes round a handle just behind the knuckles, between the two claws, against the palm
                var k0 = m_Joints[0][0].Pivot; var k1 = m_Joints[1][0].Pivot;
                m_FistAnchor = (k0 + k1) * 0.5f + m_PalmN3 * 0.016f - Vector3.forward * 0.016f;
            }

            static Vector2 P2(Digit g, Vector3 p) => new Vector2(p.z - g.O.z, Vector3.Dot(p - g.O, g.E2));

            public void Free() { if (m_Mesh) Object.Destroy(m_Mesh); }

            /// <summary>
            /// Re-shapes the mesh to the joint angles (m_Ang): each segment turning rigidly at its joint (so the low-poly
            /// claws keep their shape), the claws shortened and the forearm stretched.
            /// </summary>
            void Bend()
            {
                if (m_Base.Length == 0) return;
                bool smooth = m_Blocky && GameSettings.SmoothHands.Value;
                bool same = m_Bent && m_Wrap == m_ShownWrap && smooth == m_ShownSmooth && Quaternion.Angle(m_WristQ, m_ShownWristQ) < 0.05f;
                for (int k = 0; k < 9 && same; k++) same = Mathf.Abs(m_Ang[k] - m_Shown[k]) < 0.05f;
                if (same) return;
                m_Bent = true;
                m_ShownWrap = m_Wrap;
                m_ShownWristQ = m_WristQ;
                m_ShownSmooth = smooth;
                System.Array.Copy(m_Ang, m_Shown, 9);
                BendCount++;
                for (int i = 0; i < m_Base.Length; i++) m_Work[i] = m_ToMesh.MultiplyPoint3x4(PoseVertex(i));
                m_Mesh.vertices = m_Work;
                m_Mesh.RecalculateNormals();
                if (smooth && m_Weld != null)
                {
                    // shade smooth (Settings > Display > SHADING): each corner gets the average of the faces round it
                    var nn = m_Mesh.normals;
                    var acc = new Vector3[m_WeldCount];
                    for (int i = 0; i < nn.Length; i++) acc[m_Weld[i]] += nn[i];
                    for (int i = 0; i < nn.Length; i++) nn[i] = acc[m_Weld[i]].sqrMagnitude > 1e-12f ? acc[m_Weld[i]].normalized : nn[i];
                    m_Mesh.normals = nn;
                }
                m_Mesh.RecalculateBounds();
            }

            /// <summary>Where vertex i is (model space) with the joints at m_Ang.</summary>
            Vector3 PoseVertex(int i)
            {
                var u = m_U[i];
                int d = m_Digit[i];
                if (d < 0)
                {
                    // the forearm turns at the wrist (the band between WristHi and WristLo stretching over the bend)
                    if (u.z >= WristHi) return u;
                    float w = Mathf.Clamp01((WristHi - u.z) / (WristHi - WristLo));
                    return m_WristPivot + Quaternion.Slerp(Quaternion.identity, m_WristQ, w) * (u - m_WristPivot);
                }
                if (m_Joints == null) return u;
                float z0 = u.z;
                // from the tip in: each joint turns everything past it (already turned by the joints further out)
                var js = m_Joints[d == 2 && m_Wrap ? 3 : d];
                for (int j = js.Length - 1; j >= 0; j--)
                {
                    float w = js[j].Hi <= js[j].Lo ? 1f : Mathf.Clamp01((z0 - js[j].Lo) / (js[j].Hi - js[j].Lo));
                    if (w <= 0f) continue;
                    u = js[j].Pivot + Quaternion.AngleAxis(m_Ang[d * 3 + j] * w, js[j].Axis) * (u - js[j].Pivot);
                }
                return u;
            }

            /// <summary>
            /// For the hands autotest: the posed hand cut into its rigid pieces (the palm, the forearm and each claw / thumb
            /// segment, the first one stretching from its knuckle ring), each as world-space points and the triangles
            /// between them.
            /// </summary>
            public void DebugPieces(string side, List<(string name, Vector3[] pts, int[] tris)> list)
            {
                if (m_Base.Length == 0 || m_MeshT == null) return;
                string[] names = { "upper", "long", "thumb" };
                var toWorld = m_MeshT.localToWorldMatrix;
                var groups = new Dictionary<string, List<int>>();
                void Add(string k, int i)
                {
                    if (!groups.TryGetValue(k, out var g)) groups[k] = g = new List<int>();
                    g.Add(i);
                }
                for (int i = 0; i < m_Base.Length; i++)
                {
                    int d = m_Digit[i];
                    if (d >= 0) { Add(names[d] + m_Seg[i], i); continue; }
                    Add(m_U[i].z < -0.02f ? "arm" : "palm", i);
                    if (m_Ring[i] >= 0) Add(names[m_Ring[i]] + "0", i);
                }
                foreach (var kv in groups)
                {
                    var map = new Dictionary<int, int>();
                    var pts = new Vector3[kv.Value.Count];
                    for (int k = 0; k < kv.Value.Count; k++) { map[kv.Value[k]] = k; pts[k] = toWorld.MultiplyPoint3x4(m_Work[kv.Value[k]]); }
                    var tl = new List<int>();
                    for (int t = 0; t < m_Tris.Length; t += 3)
                        if (map.TryGetValue(m_Tris[t], out int a) && map.TryGetValue(m_Tris[t + 1], out int b) && map.TryGetValue(m_Tris[t + 2], out int c)) { tl.Add(a); tl.Add(b); tl.Add(c); }
                    list.Add((side + "." + kv.Key, pts, tl.ToArray()));
                }
            }

            /// <summary>For the hands autotest: each piece's extent in the model's own space, unbent (to see what it's made of).</summary>
            public string DebugRest()
            {
                var sb = new System.Text.StringBuilder();
                string[] names = { "upper", "long", "thumb" };
                for (int d = -1; d < 3; d++)
                {
                    Vector3 lo = Vector3.one * 9f, hi = -Vector3.one * 9f; int cnt = 0, ring = 0;
                    for (int i = 0; i < m_U.Length; i++)
                    {
                        if (d >= 0 && m_Ring[i] == d) ring++;
                        if (m_Digit[i] != d || (d < 0 && m_U[i].z < 0f)) continue;
                        lo = Vector3.Min(lo, m_U[i]); hi = Vector3.Max(hi, m_U[i]); cnt++;
                    }
                    sb.Append($"{(d < 0 ? "palm" : names[d])}: {cnt} verts (+{ring} on its knuckle ring), min {lo.ToString("F4")} max {hi.ToString("F4")}\n");
                    if (d >= 0 && m_D != null)
                    {
                        var g = m_D[d];
                        sb.Append($"   plane: origin {g.O.ToString("F4")} axis {g.A.ToString("F3")} curl {g.E2.ToString("F3")} width {g.AMin:F4}..{g.AMax:F4}, palm outline {g.Palm.N} points\n");
                        for (int j = 0; j < 3; j++)
                            sb.Append($"   joint {j}: pivot {m_Joints[d][j].Pivot.ToString("F4")} band {m_Joints[d][j].Lo:F4}-{m_Joints[d][j].Hi:F4}, segment {g.Rest[j].Length} points\n");
                    }
                }
                sb.Append($"fist anchor {m_FistAnchor.ToString("F4")}; model root under the fit: {m_ModelT.localPosition.ToString("F4")} {m_ModelT.localEulerAngles.ToString("F1")} {m_ModelT.localScale.ToString("F3")}");
                return sb.ToString();
            }

            /// <summary>For the hands autotest: the last solve (joint angles, where it moved the hand, and a check of it).</summary>
            public string DebugSolve()
            {
                if (m_D == null) return "no digits";
                var sb = new System.Text.StringBuilder("angles");
                for (int k = 0; k < 9; k++) sb.Append(k % 3 == 0 ? " " : "/").Append(m_Ang[k].ToString("0"));
                sb.Append($", solved in {m_SolveMs:0.00} ms, hand moved {(m_Shift * Scale * 1000f).ToString("F1")} mm, outlines {m_Obs[0].Count}/{m_Obs[1].Count}/{m_Obs[2].Count}, clear ");
                for (int d = 0; d < 3; d++) for (int j = 0; j < 3; j++) sb.Append(DebugClear(d, j) ? "+" : "-");
                // the posed digits' own vertices against the outlines (they should all be outside)
                int bad = 0; float worst = 0f;
                for (int i = 0; i < m_U.Length; i++)
                {
                    int d = m_Digit[i];
                    if (d < 0) continue;
                    var g = m_D[Frame(d)];
                    var p = PoseVertex(i);
                    float a = Vector3.Dot(p - g.O, g.A);
                    if (a < g.AMin || a > g.AMax) { bad += 1000; continue; }
                    var q = P2(g, p);
                    foreach (var o in m_Obs[d]) { float dd = Dist(o, q); if (dd < 0f) { bad++; worst = Mathf.Min(worst, dd); } }
                }
                sb.Append($", digit vertices inside outlines: {bad} (deepest {worst * Scale * 1000f:0.0} mm)");
                return sb.ToString();
            }

            /// <summary>For the hands autotest: the last solve's outlines in each digit's plane (what the claws close onto), as JSON.</summary>
            public string DebugOutlines()
            {
                if (m_D == null) return "{}";
                var sb = new System.Text.StringBuilder("{");
                void Pts(Vector2[] p, int n)
                {
                    sb.Append('[');
                    for (int i = 0; i < n; i++) sb.Append(i > 0 ? "," : "").Append($"[{p[i].x * 1000f:0.0},{p[i].y * 1000f:0.0}]");
                    sb.Append(']');
                }
                for (int d = 0; d < 3; d++)
                {
                    var g = m_D[Frame(d)];
                    sb.Append(d > 0 ? "," : "").Append($"\"d{d}\":{{\"obs\":[");
                    for (int k = 0; k < m_Obs[d].Count; k++) { if (k > 0) sb.Append(','); Pts(m_Obs[d][k].P, m_Obs[d][k].N); }
                    sb.Append("],\"palm\":"); Pts(g.Palm.P, g.Palm.N);
                    sb.Append(",\"segs\":[");
                    for (int j = 0; j < 3; j++)
                    {
                        DebugClear(d, j);
                        if (j > 0) sb.Append(',');
                        Pts(s_Seg.P, s_Seg.N);
                    }
                    sb.Append("],\"piv\":"); Pts(g.Piv, 3);
                    sb.Append($",\"ang\":[{m_Ang[d * 3]:0.0},{m_Ang[d * 3 + 1]:0.0},{m_Ang[d * 3 + 2]:0.0}]}}");
                }
                return sb.Append('}').ToString();
            }

            /// <summary>Puts the arm on its hand transform for this frame and closes its digits onto what's held.</summary>
            public void Pose(HandPose p, Transform hand, Transform root, List<HeldPart> parts)
            {
                float curl = Mathf.Clamp01(p.Curl);
                // turn the arm round its length so the thumb / the palm points the right way
                Vector2 want = Vector2.up; // the thumb where the box fist had it
                if (p.HasDir)
                {
                    var d = p.Palm ? hand.InverseTransformPoint(root.TransformPoint(p.Dir)) : hand.InverseTransformDirection(root.TransformDirection(p.Dir));
                    if (new Vector2(d.x, d.y).sqrMagnitude > 1e-6f) want = new Vector2(d.x, d.y);
                }
                var have = p.Palm ? m_PalmN : m_Thumb;
                float roll = (Mathf.Atan2(want.y, want.x) - Mathf.Atan2(have.y, have.x)) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0, 0, roll);
                Fit.localRotation = rot;
                Fit.localScale = Vector3.one * Scale;
                // a fist goes round the grip (the handle against the palm just behind the knuckles); an open hand puts its
                // knuckles there; a cradled thing rests on the middle of the palm
                Fit.localPosition = -(rot * ((p.Palm ? new Vector3(0, 0, Knuckle) : m_FistAnchor) * Scale));
                if (p.Cradle)
                {
                    var palmUp = rot * m_PalmN3;
                    var palmMid = new Vector3(0, 0, 0.05f) + m_PalmN3 * 0.012f;
                    Fit.localPosition = hand.InverseTransformPoint(root.TransformPoint(p.Dir)) - palmUp * p.Radius - rot * (palmMid * Scale);
                }
                // the wrist: the forearm turned from straight on (model -Z) towards where it should run, as far as it goes
                m_WristQ = Quaternion.identity;
                if (p.HasArm)
                {
                    var a = m_ModelT.InverseTransformDirection(root.TransformDirection(p.Arm));
                    if (a.sqrMagnitude > 1e-6f)
                    {
                        var q = Quaternion.FromToRotation(Vector3.back, a.normalized);
                        float ang = Quaternion.Angle(Quaternion.identity, q);
                        m_WristQ = ang > MaxWrist ? Quaternion.Slerp(Quaternion.identity, q, MaxWrist / ang) : q;
                    }
                }
                m_Wrap = !p.Palm && m_D != null;
                if (m_D == null)
                {
                    for (int d = 0; d < 3; d++) for (int j = 0; j < 3; j++) m_Ang[d * 3 + j] = FistAngles[d][j] * curl;
                    Bend();
                    return;
                }
                // nothing moved since the last solve (relative to the hand): the same grip
                var sig = new Vector4(Fit.localPosition.x + rot.z * 3.1f, Fit.localPosition.y + rot.w * 1.7f, Fit.localPosition.z + curl * 0.37f, (p.Palm ? 1f : 0f) + (p.Contact ? 2f : 0f));
                int n = 0;
                foreach (var part in parts)
                {
                    if (part.T == null || !part.T.gameObject.activeInHierarchy || !part.Rend.enabled) continue;
                    n++;
                    var lp = hand.InverseTransformPoint(part.T.position);
                    var lu = hand.InverseTransformDirection(part.T.up);
                    var lf = hand.InverseTransformDirection(part.T.forward);
                    var ls = part.T.lossyScale;
                    float k = 1f + n * 0.173f;
                    sig += new Vector4(lp.x + lu.y * 0.31f, lp.y + lf.z * 0.29f, lp.z + lu.x * 0.23f + ls.x * 0.11f, lf.x * 0.19f + ls.y * 0.07f) * k;
                }
                if (n != m_SigN || (sig - m_Sig).sqrMagnitude > 1e-11f)
                {
                    m_Sig = sig;
                    m_SigN = n;
                    // the same grip as one worked out before (picking the same item up again): no need to work it out again
                    long key = n * 1000003L;
                    for (int i = 0; i < 4; i++) key = key * 7919L + Mathf.RoundToInt(sig[i] * 20000f);
                    if (m_Known.TryGetValue(key, out var known))
                    {
                        System.Array.Copy(known.ang, m_Ang, 9);
                        m_Shift = known.shift;
                        m_SolvedWrap = known.wrap;
                        m_SolveMs = 0f;
                    }
                    else
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Solve(p, curl, parts);
                        m_SolveMs = (float)sw.Elapsed.TotalMilliseconds;
                        SolveCount++;
                        SolveMsTotal += m_SolveMs;
                        m_SolvedWrap = m_Wrap;
                        if (m_Known.Count > 400) m_Known.Clear();
                        m_Known[key] = ((float[])m_Ang.Clone(), m_Shift, m_Wrap);
                    }
                }
                m_Wrap = m_SolvedWrap;
                Fit.localPosition += rot * (m_Shift * Scale);
                Bend();
            }

            /// <summary>
            /// Closes the digits onto what's held: cuts the item's pieces through along each digit's plane, puts the palm
            /// on it (round a handle: the palm against it, the knuckles just past it; an open hand: onto the surface in
            /// front of it, without the straight claws going in), then closes each claw - all its joints together; a
            /// segment that touches stops, with the joints before it, and the rest carry on curling round - then the thumb
            /// (which mustn't go into the claws either). In a fist the thumb wraps round the handle alongside the claws;
            /// otherwise it closes in towards them.
            /// </summary>
            void Solve(HandPose p, float curl, List<HeldPart> parts)
            {
                // the item's pieces near enough to matter, in model space
                var w2m = m_ModelT.worldToLocalMatrix;
                int used = 0;
                foreach (var part in parts)
                {
                    if (part.T == null || !part.T.gameObject.activeInHierarchy || !part.Rend.enabled) continue;
                    var m = w2m * part.T.localToWorldMatrix;
                    float sc = Mathf.Max(m.GetColumn(0).magnitude, Mathf.Max(m.GetColumn(1).magnitude, m.GetColumn(2).magnitude));
                    var c = m.MultiplyPoint3x4(part.C);
                    if ((c - new Vector3(0, 0, Knuckle)).magnitude > part.R * sc + 0.32f) continue;
                    if (used == m_ItemV.Count) { m_ItemV.Add(null); m_ItemT.Add(null); m_ItemS.Add(default); }
                    var arr = m_ItemV[used];
                    if (arr == null || arr.Length < part.V.Length) m_ItemV[used] = arr = new Vector3[Mathf.NextPowerOfTwo(part.V.Length)];
                    for (int i = 0; i < part.V.Length; i++) arr[i] = m.MultiplyPoint3x4(part.V[i]);
                    m_ItemT[used] = part.Tri;
                    m_ItemS[used] = new Vector4(c.x, c.y, c.z, part.R * sc * 1.05f + 0.002f);
                    used++;
                }
                // a fist that can't close cleanly (part of the hand would be in the item, e.g. a gun's body over its
                // grip) slides along the handle to where it can
                float bestBad = float.MaxValue, bestOff = 0f, lastOff = 0f;
                foreach (float off in p.Palm ? s_NoSlide : s_Slide)
                {
                    lastOff = off;
                    float bad = Attempt(p, curl, used, off);
                    if (bad < bestBad - 1e-6f) { bestBad = bad; bestOff = off; }
                    if (bestBad <= 0f) break;
                }
                // (then a finer step either side of the best)
                if (bestBad > 0f && !p.Palm)
                    foreach (float off in new[] { bestOff + 0.012f, bestOff - 0.012f })
                    {
                        lastOff = off;
                        float bad = Attempt(p, curl, used, off);
                        if (bad < bestBad - 1e-6f) { bestBad = bad; bestOff = off; }
                        if (bestBad <= 0f) break;
                    }
                if (lastOff != bestOff) Attempt(p, curl, used, bestOff);
            }

            /// <summary>Closes the thumb (in the plane m_Wrap picks) onto the item, moved by `offset`, and the claws as they are now.</summary>
            void SolveThumb(int used, Vector3 offset, float curl)
            {
                var tg = m_D[Frame(2)];
                SliceItem(2, tg, used, offset);
                for (int d = 0; d < 2; d++)
                    for (int s = 1; s < 3; s++)
                    {
                        var tris = m_D[d].SegTris[s];
                        for (int t = 0; t < tris.Length; t++) m_Posed[tris[t]] = PoseVertex(tris[t]);
                        s_Pts.Clear();
                        Slice(tg, m_Posed, tris, Vector3.zero, s_Pts);
                        AddPoly(m_Obs[2], s_Pts);
                    }
                SolveDigit(2, tg, m_Obs[2], curl);
            }

            static readonly float[] s_NoSlide = { 0f }, s_Slide = { 0f, 0.024f, -0.024f, 0.048f, -0.048f };

            /// <summary>One go at the grip with the item slid `slide` along the handle (model units); how much is still in it.</summary>
            float Attempt(HandPose p, float curl, int used, float slide)
            {
                m_Wrap = !p.Palm;
                var off = m_PalmF.A * slide;
                m_PolyUsed = 0;
                SliceItem(0, m_D[0], used, off);
                SliceItem(1, m_D[1], used, off);
                for (int k = 0; k < Strips; k++) SliceItem(3 + k, m_PalmS[k], used, off, 1);
                if (m_Wrap || p.Contact) SliceItem(2, m_D[3], used, off); // (the thumb laid along the claws' plane)

                // where the palm goes (the item's shift relative to the hand; the hand moves the other way)
                var shift = Vector3.zero;
                if (!p.Palm) shift = FistShift();
                else if (p.Contact) shift = PalmShift();
                var s2 = new Vector2(shift.z, Vector3.Dot(shift, m_PalmN3));
                if (s2.sqrMagnitude > 0f) for (int k = 0; k < 3 + Strips; k++) if (k != 2 || m_Wrap) foreach (var o in m_Obs[k]) o.Move(s2);

                SolveDigit(0, m_D[0], m_Obs[0], curl);
                SolveDigit(1, m_D[1], m_Obs[1], curl);
                // the thumb: round the item and clear of the claws (in a fist wrapped round the handle with them, unless
                // there's no room for that - e.g. a gun's body right over its grip - when it closes in towards them instead)
                bool fist = m_Wrap;
                SolveThumb(used, shift + off, curl);
                if (fist && (s_Stuck[0] || s_Stuck[1] || s_Stuck[2]))
                {
                    m_Wrap = false;
                    SolveThumb(used, shift + off, curl);
                    if (s_Stuck[0] || s_Stuck[1] || s_Stuck[2]) { m_Wrap = true; SolveThumb(used, shift + off, curl); }
                }
                m_Shift = -(shift + off);
                // what's still in the item: every segment, and the palm
                float bad = 0f;
                for (int d = 0; d < 3; d++)
                    for (int j = 0; j < 3; j++)
                        if (!DebugClear(d, j))
                            foreach (var o in m_Obs[d]) bad += Overlap(s_Seg, o);
                for (int k = 0; k < Strips; k++) foreach (var o in m_Obs[3 + k]) bad += Overlap(m_PalmS[k].Palm, o);
                return bad;
            }

            /// <summary>Which plane digit d curls in (the thumb: towards the claws, or round a handle with them).</summary>
            int Frame(int d) => d == 2 && m_Wrap ? 3 : d;

            /// <summary>
            /// Cuts the item's pieces by a digit's slab into outlines in its plane, the slab in `strips` thinner slices (a
            /// handle at a slant across a wide claw is then met where it really is, not smeared across the claw's width).
            /// </summary>
            void SliceItem(int list, Digit frame, int used, Vector3 offset, int strips = 3)
            {
                var obs = m_Obs[list];
                obs.Clear();
                for (int k = 0; k < used; k++)
                {
                    // (a piece nowhere near this slab, or out of the digit's reach, is skipped)
                    var sp = m_ItemS[k];
                    var c = new Vector3(sp.x, sp.y, sp.z) + offset;
                    float ca = Vector3.Dot(c - frame.O, frame.A);
                    if (ca + sp.w < frame.AMin || ca - sp.w > frame.AMax || (c - frame.O).magnitude - sp.w > 0.2f) continue;
                    for (int st = 0; st < strips; st++)
                    {
                        float lo = Mathf.Lerp(frame.AMin, frame.AMax, st / (float)strips), hi = Mathf.Lerp(frame.AMin, frame.AMax, (st + 1) / (float)strips);
                        if (ca + sp.w < lo || ca - sp.w > hi) continue;
                        s_Pts.Clear();
                        Slice(frame, m_ItemV[k], m_ItemT[k], offset, s_Pts, lo, hi);
                        AddPoly(obs, s_Pts);
                    }
                }
            }

            void AddPoly(List<Poly> obs, List<Vector2> pts)
            {
                if (pts.Count == 0) return;
                int hn = Hull(pts, ref s_Hull);
                if (hn == 0) return;
                // (a round piece cuts into a many-sided outline: down to 24 corners, dropping the flattest first)
                while (hn > 24)
                {
                    int drop = -1; float least = float.MaxValue;
                    for (int i = 0; i < hn; i++)
                    {
                        float area = Mathf.Abs(Cross(s_Hull[(i + hn - 1) % hn], s_Hull[i], s_Hull[(i + 1) % hn]));
                        if (area < least) { least = area; drop = i; }
                    }
                    for (int i = drop; i < hn - 1; i++) s_Hull[i] = s_Hull[i + 1];
                    hn--;
                }
                if (m_PolyUsed == m_PolyPool.Count) m_PolyPool.Add(new Poly());
                var poly = m_PolyPool[m_PolyUsed++];
                if (poly.P.Length < hn) poly.P = new Vector2[Mathf.NextPowerOfTwo(hn)];
                System.Array.Copy(s_Hull, poly.P, hn);
                poly.N = hn;
                poly.Bound();
                obs.Add(poly);
            }

            /// <summary>
            /// A fist: finds the handle (what the grip point is in, or the nearest thing to it) in the claws' (and the
            /// wrapped thumb's) planes and moves it so it lies against the palm with its front just behind the knuckles,
            /// where they close round it.
            /// </summary>
            Vector3 FistShift()
            {
                float dxNeed = 9f, dyNeed = -9f;
                bool any = false;
                for (int k = 0; k < 3 + Strips; k++)
                {
                    if (k == 2 && !m_Wrap) continue;
                    var g = k >= 3 ? m_PalmS[k - 3] : m_D[k == 2 ? 3 : k];
                    var a = P2(g, m_FistAnchor);
                    Poly near = null; float nd = 0.03f;
                    bool inside = false;
                    float xHi = -9f, yLo = 9f;
                    foreach (var o in m_Obs[k])
                    {
                        float dist = Dist(o, a);
                        if (dist <= 0f) { inside = true; Extent(o, ref xHi, ref yLo); }
                        else if (dist < nd) { nd = dist; near = o; }
                    }
                    if (!inside && near != null) Extent(near, ref xHi, ref yLo);
                    if (!inside && near == null) continue;
                    any = true;
                    float palmTop = -9f;
                    for (int i = 0; i < g.Palm.N; i++) palmTop = Mathf.Max(palmTop, g.Palm.P[i].y);
                    dyNeed = Mathf.Max(dyNeed, palmTop + Gap - yLo);
                    if (k < 3) dxNeed = Mathf.Min(dxNeed, -Gap - xHi);
                }
                if (!any) return Vector3.zero;
                if (dxNeed > 8f) dxNeed = 0f;
                float dx = Mathf.Clamp(dxNeed, -0.03f, 0.03f), dy = Mathf.Clamp(dyNeed, -0.03f, 0.05f);
                return Vector3.forward * dx + m_PalmN3 * dy;
            }

            static void Extent(Poly o, ref float xHi, ref float yLo)
            {
                for (int i = 0; i < o.N; i++) { xHi = Mathf.Max(xHi, o.P[i].x); yLo = Mathf.Min(yLo, o.P[i].y); }
            }

            /// <summary>
            /// An open hand with contact: slides the hand along the palm's normal until the palm - or a straight claw,
            /// whichever comes first - touches the surface in front of it (or, if it's in it, back out).
            /// </summary>
            Vector3 PalmShift()
            {
                float s0 = 9f;
                for (int k = 0; k < Strips; k++)
                    foreach (var o in m_Obs[3 + k])
                        if (DirContact(m_PalmS[k].Palm, o, out float lo)) s0 = Mathf.Min(s0, lo);
                for (int k = 0; k < 3; k++)
                    foreach (var o in m_Obs[k])
                        foreach (var sp in m_D[k == 2 ? 3 : k].Straight)
                            if (sp.N > 0 && DirContact(sp, o, out float lo)) s0 = Mathf.Min(s0, lo);
                if (s0 > 0.1f || s0 < -0.12f) return Vector3.zero;
                return -m_PalmN3 * (s0 - Gap);
            }

            static readonly float[] s_Base = new float[3], s_Hi = new float[3], s_Cur = new float[3];
            static readonly bool[] s_Stuck = new bool[3];

            /// <summary>
            /// Curls one digit onto what's there. A segment that starts inside something first opens (bends back) until
            /// it's out (or as far out as it gets); then all the joints close together, and when a segment touches it stops
            /// along with the joints before it while the ones after it carry on, so the digit wraps round the shape and
            /// ends just above its surface. At most `curl` of a fist; nothing curls into the palm.
            /// </summary>
            void SolveDigit(int d, Digit g, List<Poly> obs, float curl)
            {
                var a = s_Cur;
                for (int j = 0; j < 3; j++) { a[j] = 0f; s_Hi[j] = g.Max[j] * curl; s_Stuck[j] = false; }
                // anything that starts inside the item opens (bends back) until it's out: the joints after the knuckle
                // first, then the knuckle too if that's not enough
                float lo0 = d == 2 ? -60f : -25f;
                bool open = false;
                for (float a0 = 0f; a0 >= lo0 - 0.01f && !open; a0 -= 6f)
                {
                    a[0] = a0; a[1] = 0f; a[2] = 0f;
                    open = ClearSeg(g, 0, a, obs) && OpenJoint(g, 1, obs) && OpenJoint(g, 2, obs);
                }
                if (!open)
                {
                    // stuck in it whatever it does (the hand can't be put anywhere better): each segment as little in there
                    // as it can be, and it stays put
                    for (int j = 0; j < 3; j++)
                    {
                        if (ClearSeg(g, j, a, obs) || OpenJoint(g, j, obs)) continue;
                        float lo = j == 0 ? lo0 : -45f, best = 0f, least = float.MaxValue;
                        for (float t = lo; t <= 0.01f; t += 6f)
                        {
                            a[j] = t;
                            ClearSeg(g, j, a, obs);
                            float sum = 0f;
                            foreach (var o in obs) sum += Overlap(s_Seg, o);
                            if (sum < least - 1e-7f) { least = sum; best = t; }
                        }
                        a[j] = best;
                        s_Stuck[j] = true;
                    }
                }
                for (int j = 0; j < 3; j++)
                {
                    s_Base[j] = a[j];
                    if (s_Hi[j] < s_Base[j] || s_Stuck[j]) s_Hi[j] = s_Base[j];
                }
                int free = 0;
                float s = 0f;
                while (free < 3 && s < 1f)
                {
                    float sn = Mathf.Min(1f, s + 1f / 15f);
                    int hit = HitAt(g, obs, free, sn);
                    if (hit < 0) { s = sn; continue; }
                    float lo = s, hi = sn;
                    for (int k = 0; k < 6; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        int h = HitAt(g, obs, free, mid);
                        if (h < 0) lo = mid; else { hi = mid; hit = h; }
                    }
                    s = lo;
                    SetAt(free, s);
                    free = hit + 1;
                }
                SetAt(free, s);
                for (int j = 0; j < 3; j++) m_Ang[d * 3 + j] = a[j];
            }

            /// <summary>Opens joint j (from where it is, back to -45 degrees) until its segment is clear; false if it never is.</summary>
            bool OpenJoint(Digit g, int j, List<Poly> obs)
            {
                var a = s_Cur;
                if (ClearSeg(g, j, a, obs)) return true;
                float start = a[j], ok = float.NaN;
                for (float t = start - 6f; t >= -45.01f; t -= 6f) { a[j] = t; if (ClearSeg(g, j, a, obs)) { ok = t; break; } }
                if (float.IsNaN(ok)) { a[j] = start; return false; }
                float bad = ok + 6f;
                for (int k = 0; k < 6; k++)
                {
                    a[j] = (ok + bad) * 0.5f;
                    if (ClearSeg(g, j, a, obs)) ok = a[j]; else bad = a[j];
                }
                a[j] = ok;
                return true;
            }

            static void SetAt(int free, float s)
            {
                for (int j = free; j < 3; j++) s_Cur[j] = s_Base[j] + s * (s_Hi[j] - s_Base[j]);
            }

            /// <summary>With the free joints closed to `s`: the first segment (from `free` on) that touches something, or -1.</summary>
            int HitAt(Digit g, List<Poly> obs, int free, float s)
            {
                SetAt(free, s);
                for (int k = free; k < 3; k++)
                    if (!s_Stuck[k] && !ClearSeg(g, k, s_Cur, obs)) return k;
                return -1;
            }

            /// <summary>How far two convex outlines are into each other (the shortest push apart along an edge normal; 0 if they're apart).</summary>
            static float Overlap(Poly a, Poly b)
            {
                float least = float.MaxValue;
                for (int pass = 0; pass < 2; pass++)
                {
                    var p = pass == 0 ? a : b; var q = pass == 0 ? b : a;
                    if (p.N < 2) continue;
                    for (int i = 0; i < p.N; i++)
                    {
                        var e = p.P[(i + 1) % p.N] - p.P[i];
                        float len = e.magnitude;
                        if (len < 1e-7f) continue;
                        var nrm = new Vector2(e.y, -e.x) / len;
                        float pMax = -9f, qMin = 9f;
                        for (int k = 0; k < p.N; k++) pMax = Mathf.Max(pMax, Vector2.Dot(p.P[k], nrm));
                        for (int k = 0; k < q.N; k++) qMin = Mathf.Min(qMin, Vector2.Dot(q.P[k], nrm));
                        float o = pMax - qMin;
                        if (o <= 0f) return 0f;
                        least = Mathf.Min(least, o);
                    }
                }
                return least == float.MaxValue ? 0f : least;
            }

            static readonly Poly s_Seg = new Poly { P = new Vector2[64] };
            static readonly float[] s_Dbg = new float[3];

            /// <summary>For the debug output: is segment j of digit d clear where the last solve left it?</summary>
            bool DebugClear(int d, int j)
            {
                for (int k = 0; k < 3; k++) s_Dbg[k] = m_Ang[d * 3 + k];
                return ClearSeg(m_D[Frame(d)], j, s_Dbg, m_Obs[d]);
            }

            /// <summary>Is segment j, with the joints at `ang` (degrees), clear of everything? (Its outline is left in s_Seg.)</summary>
            bool ClearSeg(Digit g, int j, float[] ang, List<Poly> obs)
            {
                var seg = s_Seg;
                if (j == 0)
                {
                    s_Pts2.Clear();
                    if (g != m_D[2]) s_Pts2.AddRange(g.Ring); // (the opposed thumb's base is part of the palm, which is placed on its own)
                    foreach (var q in g.Rest[0]) s_Pts2.Add(Rot(q, g.Piv[0], ang[0]));
                    seg.N = Hull(s_Pts2, ref seg.P);
                }
                else
                {
                    var h = g.Hull[j];
                    if (seg.P.Length < h.Length) seg.P = new Vector2[h.Length];
                    for (int i = 0; i < h.Length; i++)
                    {
                        var q = Rot(h[i], g.Piv[j], ang[j]);
                        for (int jj = j - 1; jj >= 0; jj--) q = Rot(q, g.Piv[jj], ang[jj]);
                        seg.P[i] = q;
                    }
                    seg.N = h.Length;
                }
                seg.Bound();
                foreach (var o in obs)
                {
                    if ((o.C - seg.C).magnitude > o.R + seg.R + Gap) continue;
                    if (!Apart(seg, o, Gap)) return false;
                }
                if (j > 0 && (g.Palm.C - seg.C).magnitude <= g.Palm.R + seg.R + Gap && !Apart(seg, g.Palm, Gap * 0.5f)) return false;
                return true;
            }

            static Vector2 Rot(Vector2 q, Vector2 piv, float deg)
            {
                float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
                var v = q - piv;
                return piv + new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
            }

            /// <summary>Cuts triangles (model-space vertices, moved by `offset`) by the digit's slab; adds the 2D points of what's inside it.</summary>
            static void Slice(Digit g, Vector3[] v, int[] tris, Vector3 offset, List<Vector2> outPts, float lo = float.NaN, float hi = float.NaN)
            {
                if (float.IsNaN(lo)) { lo = g.AMin; hi = g.AMax; }
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    var p0 = v[tris[t]] + offset; var p1 = v[tris[t + 1]] + offset; var p2 = v[tris[t + 2]] + offset;
                    float a0 = Vector3.Dot(p0 - g.O, g.A), a1 = Vector3.Dot(p1 - g.O, g.A), a2 = Vector3.Dot(p2 - g.O, g.A);
                    if ((a0 < lo && a1 < lo && a2 < lo) || (a0 > hi && a1 > hi && a2 > hi)) continue;
                    // the triangle clipped to AMin <= a <= AMax (Sutherland-Hodgman, twice)
                    int n = 3;
                    s_A[0] = a0; s_A[1] = a1; s_A[2] = a2;
                    s_P[0] = P2(g, p0); s_P[1] = P2(g, p1); s_P[2] = P2(g, p2);
                    n = ClipSide(n, 0, 8, lo, 1f);
                    n = ClipSide(n, 8, 0, hi, -1f);
                    for (int i = 0; i < n; i++) outPts.Add(s_P[i]);
                }
            }

            /// <summary>Clips the polygon in s_A / s_P[from..] to sign * (a - limit) >= 0, into [to..]; returns its size.</summary>
            static int ClipSide(int n, int from, int to, float limit, float sign)
            {
                int m = 0;
                for (int i = 0; i < n; i++)
                {
                    int ci = from + i, ni = from + (i + 1) % n;
                    float dc = sign * (s_A[ci] - limit), dn = sign * (s_A[ni] - limit);
                    if (dc >= 0f) { s_A[to + m] = s_A[ci]; s_P[to + m] = s_P[ci]; m++; }
                    if ((dc >= 0f) != (dn >= 0f))
                    {
                        float k = dc / (dc - dn);
                        s_A[to + m] = Mathf.Lerp(s_A[ci], s_A[ni], k);
                        s_P[to + m] = Vector2.Lerp(s_P[ci], s_P[ni], k);
                        m++;
                    }
                }
                return m;
            }

            /// <summary>Convex hull (counter-clockwise) of the points into `hull`; returns its size. (Reorders `pts`.)</summary>
            static int Hull(List<Vector2> pts, ref Vector2[] hull)
            {
                int n = pts.Count;
                if (n == 0) return 0;
                pts.Sort(s_ByX);
                if (hull.Length < 2 * n + 1) hull = new Vector2[Mathf.NextPowerOfTwo(2 * n + 1)];
                int k = 0;
                for (int i = 0; i < n; i++)
                {
                    while (k >= 2 && Cross(hull[k - 2], hull[k - 1], pts[i]) <= 1e-10f) k--;
                    hull[k++] = pts[i];
                }
                for (int i = n - 2, lower = k + 1; i >= 0; i--)
                {
                    while (k >= lower && Cross(hull[k - 2], hull[k - 1], pts[i]) <= 1e-10f) k--;
                    hull[k++] = pts[i];
                }
                return Mathf.Max(1, k - 1);
            }

            static readonly System.Comparison<Vector2> s_ByX = (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

            static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

            /// <summary>Are two convex outlines at least `margin` apart along one of their edges' normals?</summary>
            static bool Apart(Poly a, Poly b, float margin) => Axis(a, b, margin) || Axis(b, a, margin);

            static bool Axis(Poly a, Poly b, float margin)
            {
                if (a.N < 2) return false;
                for (int i = 0; i < a.N; i++)
                {
                    var p = a.P[i];
                    var e = a.P[(i + 1) % a.N] - p;
                    float len = e.magnitude;
                    if (len < 1e-7f) continue;
                    var nrm = new Vector2(e.y, -e.x) / len; // outward (counter-clockwise)
                    float lim = Vector2.Dot(p, nrm) + margin;
                    bool all = true;
                    for (int k = 0; k < b.N && all; k++) all = Vector2.Dot(b.P[k], nrm) >= lim;
                    if (all) return true;
                }
                return false;
            }

            /// <summary>How far outside the outline the point is (negative / zero: inside).</summary>
            static float Dist(Poly o, Vector2 q)
            {
                if (o.N < 3) return o.N == 0 ? 9f : (o.P[0] - q).magnitude;
                float outside = -9f, best = 9f;
                for (int i = 0; i < o.N; i++)
                {
                    var p = o.P[i];
                    var e = o.P[(i + 1) % o.N] - p;
                    float len = e.magnitude;
                    if (len < 1e-7f) continue;
                    var nrm = new Vector2(e.y, -e.x) / len;
                    outside = Mathf.Max(outside, Vector2.Dot(q - p, nrm));
                    float k = Mathf.Clamp01(Vector2.Dot(q - p, e) / (len * len));
                    best = Mathf.Min(best, (p + e * k - q).magnitude);
                }
                return outside <= 0f ? outside : best;
            }

            static Vector2[] s_Diff = new Vector2[64];

            /// <summary>
            /// Moving `p` along +y (the palm towards what's in front of it): the first offset where it touches `q`
            /// (negative: it's already in it, and that far back clears it). False if they never meet.
            /// </summary>
            static bool DirContact(Poly p, Poly q, out float first)
            {
                first = 0f;
                // (side by side, they never meet moving along y)
                float pl = 9f, ph = -9f, ql = 9f, qh = -9f;
                for (int i = 0; i < p.N; i++) { pl = Mathf.Min(pl, p.P[i].x); ph = Mathf.Max(ph, p.P[i].x); }
                for (int i = 0; i < q.N; i++) { ql = Mathf.Min(ql, q.P[i].x); qh = Mathf.Max(qh, q.P[i].x); }
                if (ph < ql || qh < pl) return false;
                s_Pts2.Clear();
                for (int i = 0; i < q.N; i++) for (int k = 0; k < p.N; k++) s_Pts2.Add(q.P[i] - p.P[k]);
                int n = Hull(s_Pts2, ref s_Diff);
                if (n < 3) return false;
                float lo = 9f;
                bool hit = false;
                for (int i = 0; i < n; i++)
                {
                    var a = s_Diff[i]; var b = s_Diff[(i + 1) % n];
                    if ((a.x > 0f) == (b.x > 0f) && a.x != 0f) continue;
                    float y = Mathf.Abs(b.x - a.x) < 1e-9f ? Mathf.Min(a.y, b.y) : Mathf.Lerp(a.y, b.y, -a.x / (b.x - a.x));
                    lo = Mathf.Min(lo, y);
                    hit = true;
                }
                first = lo;
                return hit;
            }
        }
    }
}
