using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A flock of little dark birds (every peer's own copy). Flocks sit on the branches of a few trees (the server puts
    /// them there - ResourceNode.ServerFlocksTick - so you can always see beforehand that a tree has birds in it). The
    /// first hit on their tree sends them bursting out from under its leaves, all round it at once; they head straight
    /// off and draw together into one flock as they go, which flies in an arc to another tree, further away, and
    /// settles on its branches. When that tree is hit, they fly on to another, and so on. A tree with no birds in it
    /// never sends any up. The server picks the trees and the flight's length and tells everyone
    /// (ResourceNode.BirdsFlyRpc: from, to, how many, a seed); each peer flies its own birds along the same paths, so
    /// everyone sees the same thing without anything being sent while they fly. Sitting birds are kept by the tree
    /// (ResourceNode.Birds) so someone joining later sees them too. Each bird is a little low-poly model - a rounded
    /// body with a head and a fanned tail, a beak, and two wings that fold down its flanks when it sits (four renderers,
    /// no shadows); sitting, it nestles in the needles of a branch (ResourceNode.BirdPerch), chest up. A burst of wing
    /// flaps and chirps as they go, a chirp or two as they land.
    /// </summary>
    public class BirdFlock : MonoBehaviour
    {
        class Bird
        {
            public Transform T, WingL, WingR;
            public Vector3 From, Exit, Out, Swing, Gather, Ctrl, To, Pos, Vel;
            public float Delay, Phase, Look, NextLook;
        }

        /// <summary>Every flock (tests), and the flock sitting in / on its way to each tree.</summary>
        public static readonly List<BirdFlock> All = new List<BirdFlock>();
        static readonly Dictionary<ResourceNode, BirdFlock> s_At = new Dictionary<ResourceNode, BirdFlock>();

        readonly List<Bird> m_Birds = new List<Bird>();
        ResourceNode m_Node;      // the tree they sit in, or are flying to (null: flying off for good)
        bool m_Flying;
        float m_T, m_Dur, m_OrphanAt = -1f;
        bool m_Landed;

        public bool Flying => m_Flying;
        public int Count => m_Birds.Count;
        public ResourceNode Tree => m_Node;
        /// <summary>(tests) the birds' positions.</summary>
        public IEnumerable<Vector3> Positions { get { foreach (var b in m_Birds) if (b.T) yield return b.T.position; } }
        /// <summary>(tests) one bird's wing angle right now (degrees: how far up the right wing is).</summary>
        public float WingAngle => m_Birds.Count > 0 && m_Birds[0].WingR ? m_Birds[0].WingR.localEulerAngles.z : 0f;

        /// <summary>How long a flight from a to b takes (s; the server sends it, so every peer flies the same).</summary>
        public static float FlightTime(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Mathf.Clamp(Vector3.Distance(a, b) / 9f, 2.2f, 8.5f) + 0.6f + BurstTime + GatherTime;
        }

        /// <summary>Taking off (s): the burst out from under the leaves, all round the tree; then drawing together into one
        /// flock - on the way to the next tree (they're one flock 2.2 x GatherTime after the burst).</summary>
        public const float BurstTime = 0.7f, GatherTime = 1.1f;
        /// <summary>How far out from the leaves' edge the burst throws them (m), and how far from it they gather.</summary>
        public const float BurstOut = 4f, GatherOut = 5.5f;
        /// <summary>A sitting bird: its middle is this far over its feet (m), and its chest is up by this much (degrees).</summary>
        public const float SitUp = 0.065f, SitPitch = -22f;
        /// <summary>(tests) Where each bird came out from under the leaves on the last take-off, and where they gathered.</summary>
        public IEnumerable<Vector3> Exits { get { foreach (var b in m_Birds) yield return b.Exit; } }
        public Vector3 GatherPoint { get; private set; }

        // ------------------------------------------------------------------ what the tree / the server message say

        /// <summary>n birds sit in this tree (unless the flock flying here already is them).</summary>
        public static void Perch(ResourceNode node, int n)
        {
            if (node == null || n <= 0) return;
            if (s_At.TryGetValue(node, out var f) && f != null) { f.m_OrphanAt = -1f; return; }
            f = Make(n);
            f.m_Node = node;
            s_At[node] = f;
            for (int i = 0; i < f.m_Birds.Count; i++)
            {
                var b = f.m_Birds[i];
                b.Pos = node.BirdPerch(i);
                b.Look = Random.Range(0f, 360f);
            }
            f.Sit();
        }

        /// <summary>The birds have left this tree: unless the take-off message says where they went (it may come just
        /// after), they fly off and away.</summary>
        public static void Orphan(ResourceNode node)
        {
            if (node != null && s_At.TryGetValue(node, out var f) && f != null && !f.m_Flying) f.m_OrphanAt = Time.time + 0.8f;
        }

        public static void NodeGone(ResourceNode node)
        {
            if (node == null || !s_At.TryGetValue(node, out var f)) return;
            s_At.Remove(node);
            if (f != null && !f.m_Flying) Destroy(f.gameObject);
        }

        /// <summary>The birds in `from` (or a new flock of `count`, out of its branches) fly to `to` in `duration` seconds
        /// (null: off and away over the mountains).</summary>
        public static void TakeOff(ResourceNode from, ResourceNode to, int count, int seed, float duration)
        {
            if (from == null) return;
            BirdFlock f = null;
            if (s_At.TryGetValue(from, out var have) && have != null) { f = have; s_At.Remove(from); }
            if (f == null)
            {
                f = Make(count);
                for (int i = 0; i < f.m_Birds.Count; i++) f.m_Birds[i].T.position = f.m_Birds[i].Pos = from.BirdPerch(i);
            }
            if (to != null)
            {
                if (s_At.TryGetValue(to, out var old) && old != null && old != f) Destroy(old.gameObject);
                s_At[to] = f;
            }
            f.Fly(from, from.transform.position, to, seed, duration);
            Sfx.Play(TakeOffClip, from.transform.position + Vector3.up * 5f, 0.85f, 0.1f, 60f);
        }

        // ------------------------------------------------------------------ the flock itself

        static BirdFlock Make(int n)
        {
            var go = new GameObject("birds");
            var f = go.AddComponent<BirdFlock>();
            n = Mathf.Clamp(n, 1, 16);
            for (int i = 0; i < n; i++) f.m_Birds.Add(MakeBird(go.transform));
            All.Add(f);
            return f;
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (m_Node != null && s_At.TryGetValue(m_Node, out var f) && f == this) s_At.Remove(m_Node);
        }

        void Sit()
        {
            m_Flying = false;
            foreach (var b in m_Birds)
            {
                b.T.position = b.Pos + Vector3.up * SitUp;
                b.T.rotation = Quaternion.Euler(SitPitch, b.Look, 0);
                Fold(b, 0f);
                b.NextLook = Time.time + Random.Range(1f, 5f);
            }
        }

        /// <summary>
        /// Off they go, out of `from` (null: it's gone - from round fromTree): every bird dives out under the edge of the
        /// leaves at a spot of its own, evenly all the way round the tree (the nearest side to where it sat), and is thrown
        /// outwards; then each heads off for the next tree on its own line and merges into the flock as it goes (the
        /// flock's path starts at a spot beside the tree, on the side they're leaving by, above the lowest branches).
        /// </summary>
        void Fly(ResourceNode from, Vector3 fromTree, ResourceNode to, int seed, float duration)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            m_Node = to;
            m_Flying = true;
            m_Landed = false;
            m_OrphanAt = -1f;
            m_T = 0f;
            m_Dur = Mathf.Max(1f, duration);
            // away: off over the mountains, high up (and gone)
            var awayDir = Quaternion.Euler(0, R(0f, 360f), 0) * Vector3.forward;
            // the underside of the leaves they come out from under
            Vector3 under = fromTree + Vector3.up * 4f;
            float rad = 2f;
            if (from != null) from.BirdCanopy(out under, out rad);
            var going = to != null ? to.transform.position - fromTree : awayDir;
            going.y = 0f;
            going = going.sqrMagnitude > 0.01f ? going.normalized : awayDir;
            var gather = under + going * (rad + GatherOut) + Vector3.up * 4.5f;
            GatherPoint = gather;
            int n = m_Birds.Count;
            // all round the tree, evenly: in the order they sit round it, so each comes out on its own side
            var order = new List<int>();
            var sitAng = new float[n];
            for (int i = 0; i < n; i++)
            {
                var b = m_Birds[i];
                b.From = b.T ? b.T.position : b.Pos;
                sitAng[i] = Mathf.Atan2(b.From.z - under.z, b.From.x - under.x);
                order.Add(i);
            }
            order.Sort((x, y) => sitAng[x] != sitAng[y] ? sitAng[x].CompareTo(sitAng[y]) : x.CompareTo(y));
            float ang0 = n > 0 ? sitAng[order[0]] : 0f;
            for (int j = 0; j < n; j++)
            {
                int i = order[j];
                var b = m_Birds[i];
                float a = ang0 + j * Mathf.PI * 2f / n + R(-0.12f, 0.12f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Exit = under + dir * (rad * 0.82f) - Vector3.up * R(0.35f, 0.6f);
                b.Out = under + dir * (rad + BurstOut * R(0.8f, 1.2f)) + Vector3.up * R(0.8f, 2f);
                b.Gather = gather + new Vector3(R(-1.3f, 1.3f), R(-0.8f, 0.8f), R(-1.3f, 1.3f));
                b.To = to != null ? to.BirdPerch(i) + Vector3.up * SitUp : fromTree + awayDir * 90f + Vector3.up * 45f + new Vector3(R(-4f, 4f), R(-3f, 3f), R(-4f, 4f));
                // its own way there, straight from where the burst threw it: carrying on outwards a little and up (over the
                // top of the tree, from the far side of it), then turning for the next tree
                b.Swing = b.Out + dir * 2.5f + Vector3.up * 4.5f + going * Vector3.Distance(b.Out, b.To) * 0.2f;
                var mid = (b.Gather + b.To) * 0.5f;
                float dist = Vector3.Distance(new Vector3(b.Gather.x, 0, b.Gather.z), new Vector3(b.To.x, 0, b.To.z));
                var side = Vector3.Cross(Vector3.up, (b.To - b.Gather).normalized);
                b.Ctrl = mid + Vector3.up * (5f + dist * 0.22f + R(-0.6f, 1f)) + side * R(-1.5f, 1.5f);
                b.Delay = R(0f, 0.1f); // (all but at once: a burst)
                b.Phase = R(0f, 10f);
            }
        }

        void Update()
        {
            if (!m_Flying)
            {
                if (m_OrphanAt > 0f && Time.time >= m_OrphanAt)
                {
                    // the tree lost its birds and no one said where to: off they go
                    if (m_Node != null && s_At.TryGetValue(m_Node, out var f) && f == this) s_At.Remove(m_Node);
                    var at = m_Node != null ? m_Node.transform.position : transform.position;
                    Fly(m_Node, at, null, Random.Range(0, int.MaxValue), 6f);
                    return;
                }
                // sitting: now and then one turns round or hops a little
                foreach (var b in m_Birds)
                {
                    if (Time.time < b.NextLook) continue;
                    b.NextLook = Time.time + Random.Range(1.5f, 6f);
                    b.Look += Random.Range(-120f, 120f);
                    b.T.rotation = Quaternion.Euler(SitPitch + Random.Range(-8f, 8f), b.Look, 0);
                }
                return;
            }
            m_T += Time.deltaTime;
            bool all = true;
            float now = Time.time;
            foreach (var b in m_Birds)
            {
                float span = Mathf.Max(0.5f, m_Dur - 0.4f), own = m_T - b.Delay;
                // (a short flight keeps most of its time for the flight itself)
                float k = Mathf.Min(1f, span * 0.55f / (BurstTime + GatherTime)), burst = BurstTime * k, group = GatherTime * k;
                float t = Mathf.Clamp01(own / span);
                if (t < 1f) all = false;
                Vector3 p;
                float w; // (how much it flutters about on its own)
                bool taking = own < burst + group; // (still drawing in to the others: flapping hard)
                if (own <= 0f) { p = b.From; w = 0f; }
                else if (own < burst)
                {
                    // out from under the leaves: a quick dive down to the edge of them, then thrown outwards (fast, easing off)
                    float x = own / burst;
                    if (x < 0.28f) { float e = x / 0.28f; p = Vector3.Lerp(b.From, b.Exit, e * e); }
                    else { float e = (x - 0.28f) / 0.72f; p = Vector3.Lerp(b.Exit, b.Out, 1f - (1f - e) * (1f - e)); }
                    w = 0.15f * x;
                }
                else
                {
                    // on their way, merging as they go: each sets off on its own line for the next tree, straight from
                    // where the burst threw it, and drifts over onto its place in the flock's path (out of the gathering
                    // spot beside the tree, arcing over to the next one) a little more every moment, until they're one
                    // flock (2.2 x GatherTime after the burst). They used to draw together beside the tree first and
                    // only then set off
                    float f = Mathf.Clamp01((own - burst) / Mathf.Max(0.3f, span - burst));
                    float s = f * f * (3f - 2f * f); // (speeding up away from the tree, slowing down to land)
                    float m = Mathf.SmoothStep(0f, 1f, (own - burst) / Mathf.Max(0.3f, group * 2.2f));
                    p = Vector3.Lerp(Bezier(b.Out, b.Swing, b.To, s), Bezier(b.Gather, b.Ctrl, b.To, s), m);
                    // a little flutter of their own (none as they land)
                    w = Mathf.Lerp(0.3f, 0.7f, Mathf.Clamp01(f * 4f)) * Mathf.Clamp01((1f - f) * 3f);
                }
                p += new Vector3(Mathf.Sin(now * 2.7f + b.Phase), Mathf.Sin(now * 3.4f + b.Phase * 1.7f) * 0.6f, Mathf.Cos(now * 2.2f + b.Phase)) * w;
                var v = p - b.T.position;
                b.T.position = p;
                if (v.sqrMagnitude > 1e-6f)
                {
                    var flat = new Vector3(v.x, 0f, v.z);
                    float pitch = -Mathf.Atan2(v.y, Mathf.Max(0.001f, flat.magnitude)) * Mathf.Rad2Deg * 0.6f;
                    if (flat.sqrMagnitude > 1e-6f) b.Look = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    b.T.rotation = Quaternion.Euler(Mathf.Clamp(pitch, -40f, 40f), b.Look, 0f);
                }
                // wings: flapping hard on the way up, gliding (wings out) at the top and while coming in, folded once down
                if (t <= 0f || t >= 1f) Fold(b, t >= 1f ? 0f : 0.2f);
                else
                {
                    float beat = taking || t < 0.25f || (t > 0.45f && t < 0.75f) ? 1f : 0.35f;
                    float ang = Mathf.Sin(now * 22f + b.Phase) * 55f * beat + 10f;
                    Spread(b, ang);
                }
            }
            if (m_T > m_Dur * 0.92f && !m_Landed && m_Node != null)
            {
                m_Landed = true;
                Sfx.Play(LandClip, m_Node.transform.position + Vector3.up * 5f, 0.6f, 0.12f, 45f);
            }
            if (!all && m_T < m_Dur + 1f) return;
            if (m_Node == null) { Destroy(gameObject); return; }
            foreach (var b in m_Birds) { b.Pos = b.To - Vector3.up * SitUp; b.Look = b.T.eulerAngles.y; }
            Sit();
        }

        static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;

        static void Spread(Bird b, float ang)
        {
            b.WingR.localScale = b.WingL.localScale = Vector3.one;
            b.WingR.localRotation = Quaternion.Euler(0, 0, ang);
            b.WingL.localRotation = Quaternion.Euler(0, 0, -ang);
        }

        /// <summary>Wings folded along the body (open: 0 = tucked in, 1 = half out).</summary>
        static void Fold(Bird b, float open)
        {
            var s = new Vector3(Mathf.Lerp(0.32f, 0.7f, open), 1f, 1f);
            b.WingR.localScale = b.WingL.localScale = s;
            // (down along its flanks, the tips back towards the tail)
            float down = Mathf.Lerp(68f, 25f, open);
            b.WingR.localRotation = Quaternion.Euler(0, 8f, -down);
            b.WingL.localRotation = Quaternion.Euler(0, -8f, down);
        }

        // ------------------------------------------------------------------ looks

        static Mesh s_Body, s_Beak, s_Wing;
        static Material s_BodyMat, s_BeakMat, s_WingMat;
        /// <summary>A bird is this big (its wings about 0.75 m across).</summary>
        public const float Size = 1.25f;

        static Bird MakeBird(Transform parent)
        {
            if (s_Body == null) BuildMeshes();
            var b = new Bird();
            var go = new GameObject("bird");
            go.transform.SetParent(parent, false);
            b.T = go.transform;
            b.T.localScale = Vector3.one * Size;
            Part(b.T, s_Body, s_BodyMat, Vector3.zero, Vector3.one).name = "body";
            Part(b.T, s_Beak, s_BeakMat, Vector3.zero, Vector3.one).name = "beak";
            // (the wings: from the shoulders, on the body's sides)
            b.WingR = Part(b.T, s_Wing, s_WingMat, new Vector3(0.04f, 0.03f, 0.03f), Vector3.one);
            b.WingL = Part(b.T, s_Wing, s_WingMat, new Vector3(-0.04f, 0.03f, 0.03f), new Vector3(-1f, 1f, 1f));
            return b;
        }

        static Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var g = new GameObject("wing");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            var holder = g.transform;
            // (the mesh sits in a child so a mirrored scale stays separate from the flapping rotation)
            var m = new GameObject("mesh");
            m.transform.SetParent(holder, false);
            m.transform.localScale = scale;
            m.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = m.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return holder;
        }

        /// <summary>(tests) How big the body's model is (its own space, before Size): it has depth every way - a model,
        /// not a flat card.</summary>
        public static Vector3 BodyModelSize { get { if (s_Body == null) BuildMeshes(); return s_Body.bounds.size; } }

        static void BuildMeshes()
        {
            s_BodyMat = Art.NewMat(new Color(0.15f, 0.14f, 0.16f));
            s_BeakMat = Art.NewMat(new Color(0.95f, 0.62f, 0.12f));
            s_WingMat = Art.NewMat(new Color(0.08f, 0.08f, 0.1f));
            // body: a low-poly model, beak forward (+z) - six-sided rings from the rump to the face (a full round chest,
            // a neck, a head held a little higher), closed at both ends, with a fanned wedge of a tail. Flat-shaded.
            var v = new List<Vector3>();
            // (each face once, wound to look away from `inside`)
            void T(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) < 0f) (b, c) = (c, b);
                v.Add(a); v.Add(b); v.Add(c);
            }
            const int sides = 6;
            // (how far along, how high its middle is, half its width, half its height)
            var rings = new[]
            {
                new Vector4(-0.1f, 0.012f, 0.022f, 0.02f), new Vector4(-0.045f, 0.006f, 0.05f, 0.044f), new Vector4(0.03f, 0.008f, 0.056f, 0.05f),
                new Vector4(0.085f, 0.03f, 0.036f, 0.036f), new Vector4(0.12f, 0.052f, 0.04f, 0.038f), new Vector4(0.155f, 0.05f, 0.022f, 0.022f),
            };
            Vector3 Ring(int r, int s)
            {
                float a = (s + 0.5f) * Mathf.PI * 2f / sides;
                return new Vector3(Mathf.Cos(a) * rings[r].z, rings[r].y + Mathf.Sin(a) * rings[r].w, rings[r].x);
            }
            Vector3 Mid(int r) => new Vector3(0f, rings[r].y, rings[r].x);
            for (int r = 0; r + 1 < rings.Length; r++)
                for (int s = 0; s < sides; s++)
                {
                    Vector3 a = Ring(r, s), b = Ring(r, s + 1), c = Ring(r + 1, s + 1), d = Ring(r + 1, s);
                    var inside = (Mid(r) + Mid(r + 1)) * 0.5f;
                    T(a, b, c, inside); T(a, c, d, inside);
                }
            int last = rings.Length - 1;
            for (int s = 0; s < sides; s++)
            {
                T(Ring(0, s), Ring(0, s + 1), Mid(0) - Vector3.forward * 0.012f, Mid(1));
                T(Ring(last, s), Ring(last, s + 1), Mid(last) + Vector3.forward * 0.012f, Mid(last - 1));
            }
            // the tail: a wedge fanning out behind the rump, thick at the root
            {
                Vector3 r0 = new Vector3(-0.02f, 0.026f, -0.085f), r1 = new Vector3(0.02f, 0.026f, -0.085f), r2 = new Vector3(0f, 0.002f, -0.085f);
                Vector3 e0 = new Vector3(-0.05f, 0.012f, -0.215f), e1 = new Vector3(0.05f, 0.012f, -0.215f), e2 = new Vector3(0f, 0.002f, -0.2f);
                var inside = new Vector3(0f, 0.014f, -0.15f);
                T(r0, r1, e1, inside); T(r0, e1, e0, inside);       // top
                T(r0, e0, e2, inside); T(r0, e2, r2, inside);       // underneath, left
                T(r1, r2, e2, inside); T(r1, e2, e1, inside);       // underneath, right
                T(e0, e1, e2, inside);                              // the end
            }
            s_Body = ToMesh("bird body", v);
            // beak: a little four-sided spike on the face
            v.Clear();
            {
                var tip = new Vector3(0f, 0.04f, 0.205f);
                var inside = new Vector3(0f, 0.048f, 0.16f);
                var q = new[] { new Vector3(-0.013f, 0.05f, 0.152f), new Vector3(0f, 0.062f, 0.152f), new Vector3(0.013f, 0.05f, 0.152f), new Vector3(0f, 0.038f, 0.152f) };
                for (int i = 0; i < 4; i++) T(q[i], q[(i + 1) % 4], tip, inside);
            }
            s_Beak = ToMesh("bird beak", v);
            // wing: out along +x from the shoulder, swept back (a flat blade, both faces)
            v.Clear();
            void W(Vector3 a, Vector3 b, Vector3 c) { v.Add(a); v.Add(b); v.Add(c); v.Add(a); v.Add(c); v.Add(b); }
            var w0 = new Vector3(0f, 0f, 0.05f);
            var w1 = new Vector3(0f, 0f, -0.06f);
            var w2 = new Vector3(0.17f, 0f, 0.03f);
            var w3 = new Vector3(0.32f, 0f, -0.05f);
            var w4 = new Vector3(0.15f, 0f, -0.1f);
            W(w0, w2, w1); W(w1, w2, w4); W(w2, w3, w4);
            s_Wing = ToMesh("bird wing", v);
        }

        static Mesh ToMesh(string name, List<Vector3> v)
        {
            var t = new int[v.Count];
            for (int i = 0; i < t.Length; i++) t[i] = i;
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ sounds

        static AudioClip s_TakeOff, s_Land;
        /// <summary>A flock taking off: a burst of wing flaps fading out, with a few chirps.</summary>
        public static AudioClip TakeOffClip => s_TakeOff ? s_TakeOff : (s_TakeOff = Make("birds_takeoff", 1.3f, 21, true));
        /// <summary>Landing: a couple of chirps and a last few flaps.</summary>
        public static AudioClip LandClip => s_Land ? s_Land : (s_Land = Make("birds_land", 0.7f, 5, false));

        static AudioClip Make(string name, float dur, int seed, bool takeOff)
        {
            const int Rate = 44100;
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            var rng = new System.Random(seed);
            float N() => (float)rng.NextDouble() * 2f - 1f;
            // wing flaps: short soft noise bursts (several birds, so not quite in step)
            var flaps = new List<float>();
            float at = 0f;
            while (at < dur * (takeOff ? 0.9f : 0.5f)) { flaps.Add(at); at += (takeOff ? 0.035f : 0.06f) + (float)rng.NextDouble() * 0.05f * (1f + at); }
            // chirps: quick rising whistles
            var chirps = new List<(float t, float f)>();
            int nc = takeOff ? 4 : 2;
            for (int i = 0; i < nc; i++) chirps.Add(((float)rng.NextDouble() * dur * 0.7f, 2600f + (float)rng.NextDouble() * 1400f));
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                foreach (float f in flaps)
                {
                    float k = t - f;
                    if (k < 0f || k > 0.05f) continue;
                    s += N() * Mathf.Sin(k / 0.05f * Mathf.PI) * 0.55f * Mathf.Exp(-f * (takeOff ? 1.6f : 3f));
                }
                foreach (var (ct, cf) in chirps)
                {
                    float k = t - ct;
                    if (k < 0f || k > 0.09f) continue;
                    float env = Mathf.Sin(k / 0.09f * Mathf.PI);
                    s += Mathf.Sin(2f * Mathf.PI * (cf * k + 9000f * k * k)) * env * 0.22f;
                }
                y += (s - y) * 0.55f; // (a touch softer)
                data[i] = Mathf.Clamp(y, -1f, 1f) * Mathf.Clamp01((n - i) / 300f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
