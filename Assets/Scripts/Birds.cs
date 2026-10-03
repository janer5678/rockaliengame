using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A flock of little dark birds (every peer's own copy). Now and then a hit on a tree sends a flock flapping up out
    /// of it (ResourceNode.BirdChance); it flies in an arc to another tree and settles on its branches. When that tree is
    /// hit, they fly on to another, and so on. The server picks the trees and the flight's length and tells everyone
    /// (ResourceNode.BirdsFlyRpc: from, to, how many, a seed); each peer flies its own birds along the same paths, so
    /// everyone sees the same thing without anything being sent while they fly. Sitting birds are kept by the tree
    /// (ResourceNode.Birds) so someone joining later sees them too. Each bird is a tiny body and two flapping wings
    /// (three renderers, no shadows); a burst of wing flaps and chirps as they go, a chirp or two as they land.
    /// </summary>
    public class BirdFlock : MonoBehaviour
    {
        class Bird
        {
            public Transform T, WingL, WingR;
            public Vector3 From, Ctrl, To, Pos, Vel;
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
            return Mathf.Clamp(Vector3.Distance(a, b) / 9f, 2.2f, 8.5f) + 0.6f;
        }

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
            f.Fly(from.transform.position, to, seed, duration);
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
                b.T.position = b.Pos;
                b.T.rotation = Quaternion.Euler(0, b.Look, 0);
                Fold(b, 0f);
                b.NextLook = Time.time + Random.Range(1f, 5f);
            }
        }

        void Fly(Vector3 fromTree, ResourceNode to, int seed, float duration)
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
            for (int i = 0; i < m_Birds.Count; i++)
            {
                var b = m_Birds[i];
                b.From = b.T ? b.T.position : b.Pos;
                b.To = to != null ? to.BirdPerch(i) : fromTree + awayDir * 90f + Vector3.up * 45f + new Vector3(R(-4f, 4f), R(-3f, 3f), R(-4f, 4f));
                var mid = (b.From + b.To) * 0.5f;
                float dist = Vector3.Distance(new Vector3(b.From.x, 0, b.From.z), new Vector3(b.To.x, 0, b.To.z));
                var side = Vector3.Cross(Vector3.up, (b.To - b.From).normalized);
                b.Ctrl = mid + Vector3.up * (5f + dist * 0.22f + R(-1f, 1.5f)) + side * R(-3f, 3f);
                b.Delay = R(0f, 0.35f);
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
                    Fly(at, null, Random.Range(0, int.MaxValue), 6f);
                    return;
                }
                // sitting: now and then one turns round or hops a little
                foreach (var b in m_Birds)
                {
                    if (Time.time < b.NextLook) continue;
                    b.NextLook = Time.time + Random.Range(1.5f, 6f);
                    b.Look += Random.Range(-120f, 120f);
                    b.T.rotation = Quaternion.Euler(Random.Range(-12f, 6f), b.Look, 0);
                }
                return;
            }
            m_T += Time.deltaTime;
            bool all = true;
            float now = Time.time;
            foreach (var b in m_Birds)
            {
                float span = m_Dur - 0.4f;
                float t = Mathf.Clamp01((m_T - b.Delay) / Mathf.Max(0.5f, span));
                if (t < 1f) all = false;
                float s = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI); // (speeding up out of the tree, slowing down to land)
                var p = Bezier(b.From, b.Ctrl, b.To, s);
                // a little flutter of their own (none at either end)
                float w = Mathf.Sin(t * Mathf.PI);
                p += new Vector3(Mathf.Sin(now * 2.7f + b.Phase), Mathf.Sin(now * 3.4f + b.Phase * 1.7f) * 0.6f, Mathf.Cos(now * 2.2f + b.Phase)) * (0.7f * w);
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
                    float beat = t < 0.25f || (t > 0.45f && t < 0.75f) ? 1f : 0.35f;
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
            foreach (var b in m_Birds) { b.Pos = b.To; b.Look = b.T.eulerAngles.y; }
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
            b.WingR.localRotation = Quaternion.Euler(0, 8f, -8f);
            b.WingL.localRotation = Quaternion.Euler(0, -8f, 8f);
        }

        // ------------------------------------------------------------------ looks

        static Mesh s_Body, s_Wing;
        static Material s_BodyMat, s_WingMat;
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
            b.WingR = Part(b.T, s_Wing, s_WingMat, new Vector3(0.03f, 0.02f, 0.02f), Vector3.one);
            b.WingL = Part(b.T, s_Wing, s_WingMat, new Vector3(-0.03f, 0.02f, 0.02f), new Vector3(-1f, 1f, 1f));
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

        static void BuildMeshes()
        {
            s_BodyMat = Art.NewMat(new Color(0.13f, 0.12f, 0.13f));
            s_WingMat = Art.NewMat(new Color(0.08f, 0.08f, 0.1f));
            // body: a little faceted spindle, beak forward (+z), a fanned tail
            var nose = new Vector3(0, 0.025f, 0.16f);
            var head = new Vector3(0, 0.06f, 0.08f);
            var top = new Vector3(0, 0.055f, -0.01f);
            var bottom = new Vector3(0, -0.035f, 0.01f);
            var l = new Vector3(-0.045f, 0.01f, 0.02f);
            var r = new Vector3(0.045f, 0.01f, 0.02f);
            var tail = new Vector3(0, 0.02f, -0.1f);
            var tl = new Vector3(-0.05f, 0.015f, -0.2f);
            var tr = new Vector3(0.05f, 0.015f, -0.2f);
            var v = new List<Vector3>();
            void T(Vector3 a, Vector3 b, Vector3 c) { v.Add(a); v.Add(b); v.Add(c); v.Add(a); v.Add(c); v.Add(b); } // (both sides)
            T(nose, head, r); T(nose, l, head); T(nose, r, bottom); T(nose, bottom, l);
            T(head, top, r); T(head, l, top);
            T(top, tail, r); T(top, l, tail); T(bottom, r, tail); T(bottom, tail, l);
            T(tail, tl, tr);
            s_Body = ToMesh("bird body", v);
            // wing: out along +x from the shoulder, swept back
            v.Clear();
            var w0 = new Vector3(0f, 0f, 0.05f);
            var w1 = new Vector3(0f, 0f, -0.06f);
            var w2 = new Vector3(0.17f, 0f, 0.03f);
            var w3 = new Vector3(0.32f, 0f, -0.05f);
            var w4 = new Vector3(0.15f, 0f, -0.1f);
            T(w0, w2, w1); T(w1, w2, w4); T(w2, w3, w4);
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
