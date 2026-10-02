using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The big glass dome over the whole map (it replaced the grey boundary walls you used to see), in the same glass and
    /// frame lines as the glass wall that drops. The map is square, so the dome is a rounded square: at the ground it hugs
    /// the edge of the map (a superellipse just outside the old walls' inner faces, so the area you can play in is the same),
    /// goes straight up to a shoulder a little higher than the ground anywhere along the edge, then curves over to a peak
    /// in the middle (getting rounder as it goes up). Peak 48-68 m: the clouds (80 m+) and the sun are outside it; the ball
    /// still drops in from 40 m inside it; the airdrop ship hovers over it and beams the crate down through the glass
    /// (AirdropShip uses HighestOver).
    /// Solid: one non-convex mesh collider (its faces point in), so nothing gets out over the top - the old boundary walls
    /// are still there too, as invisible colliders (drawn only in PSX / AI PSX, which keep their concrete walls and don't
    /// show the dome). Its glass and frame take the "Map dome" colour in Settings > Display (ColorSlots.MapWalls).
    /// </summary>
    public static class MapDome
    {
        /// <summary>Segments round, and rings from the shoulder up to the peak.</summary>
        const int K = 128, J = 14;
        /// <summary>How square the dome is at the ground (a superellipse's exponent) and at the top.</summary>
        const float NBase = 16f, NTop = 2.2f;
        /// <summary>How far down into the ground the straight sides reach.</summary>
        const float Bottom = -22f;
        public const float PeakMin = 48f, PeakMax = 68f;

        static readonly Color k_Glass = new Color(0.6f, 0.9f, 1f, 0.12f);
        static Color LineColor => new Color(0.75f, 0.95f, 1f, 0.45f); // (= the MapWalls slot's default, so the colour picked lands on it exactly)

        static float s_A, s_Hv, s_Hp;
        static readonly float[] s_RingA = new float[J + 1], s_RingN = new float[J + 1], s_RingY = new float[J + 1];
        static Material s_GlassMat, s_LineMat;
        static readonly List<Renderer> s_Rs = new List<Renderer>();

        public static bool Built { get; private set; }
        /// <summary>The half size of the dome's footprint (across the middle of a side), the shoulder and the peak height.</summary>
        public static float HalfSize => s_A;
        public static float Shoulder => s_Hv;
        public static float Peak => s_Hp;
        public static GameObject Root { get; private set; }
        public static MeshCollider Collider { get; private set; }
        public static IReadOnlyList<Renderer> Renderers => s_Rs;
        /// <summary>Is the dome drawn (Normal graphics)?</summary>
        public static bool Shown => Built && s_Rs.Count > 0 && s_Rs[0] != null && s_Rs[0].enabled;

        /// <summary>Distance from the middle to ring j's edge, in direction (c, s) = (cos, sin).</summary>
        static float RingR(int j, float c, float s)
        {
            float a = s_RingA[j];
            if (a <= 0f) return 0f;
            float n = s_RingN[j];
            return a / Mathf.Pow(Mathf.Pow(Mathf.Abs(c), n) + Mathf.Pow(Mathf.Abs(s), n), 1f / n);
        }

        static Vector3 P(int j, int k)
        {
            float th = k * Mathf.PI * 2f / K;
            float c = Mathf.Cos(th), s = Mathf.Sin(th);
            float r = RingR(j, c, s);
            return new Vector3(c * r, s_RingY[j], s * r);
        }

        /// <summary>Where the dome's rings are along a flat direction from the middle (the shoulder first, 0 at the peak):
        /// between them its glass is straight along that direction, so a wall cut to these points meets it exactly.</summary>
        public static void RingsAlong(Vector3 dir, List<float> into)
        {
            var d = new Vector2(dir.x, dir.z).normalized;
            for (int j = 0; j <= J; j++) into.Add(RingR(j, d.x, d.y));
        }

        /// <summary>How far the dome reaches from the middle along a flat direction (at the ground).</summary>
        public static float ExtentAlong(Vector3 dir)
        {
            var d = new Vector2(dir.x, dir.z).normalized;
            return RingR(0, d.x, d.y);
        }

        /// <summary>The height of the dome's glass over (x, z) (float.MinValue outside its footprint).</summary>
        public static float HeightAt(float x, float z)
        {
            if (!Built) return float.MinValue;
            float r = Mathf.Sqrt(x * x + z * z);
            if (r < 1e-4f) return s_Hp;
            float c = x / r, s = z / r;
            float r0 = RingR(0, c, s);
            if (r > r0) return float.MinValue;
            for (int j = 0; j < J; j++)
            {
                float ra = j == 0 ? r0 : RingR(j, c, s), rb = RingR(j + 1, c, s);
                if (r >= rb)
                {
                    float t = ra - rb > 1e-5f ? (ra - r) / (ra - rb) : 1f;
                    return Mathf.Lerp(s_RingY[j], s_RingY[j + 1], t);
                }
            }
            return s_Hp;
        }

        /// <summary>The highest the glass gets over a disc of this radius round (x, z) (float.MinValue: no dome there).</summary>
        public static float HighestOver(float x, float z, float radius)
        {
            float best = HeightAt(x, z);
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f, rr = radius * ring * 0.5f;
                    best = Mathf.Max(best, HeightAt(x + Mathf.Cos(a) * rr, z + Mathf.Sin(a) * rr));
                }
            return best;
        }

        /// <summary>The way out of the glass at the point over (x, z) (up when there's no dome there).</summary>
        public static Vector3 NormalAt(float x, float z)
        {
            const float e = 1.5f;
            float h = HeightAt(x, z), hx = HeightAt(x + e, z), hz = HeightAt(x, z + e);
            if (h == float.MinValue || hx == float.MinValue || hz == float.MinValue) return Vector3.up;
            return Vector3.Cross(new Vector3(0, hz - h, e), new Vector3(e, hx - h, 0)).normalized;
        }

        /// <summary>Builds the dome under the world root (MapBuilder.Build). Its renderers only show in Normal graphics.</summary>
        public static void Build(Transform root, NormalLook look)
        {
            Built = false;
            s_Rs.Clear();
            float half = Cfg.MapHalf;
            s_A = half + 0.05f; // (just outside the old walls' inner faces: you're stopped right where the glass is)
            // the shoulder: well above the ground anywhere along the edge (Highlands and the theme maps rise there)
            float edge = 0f;
            for (int k = 0; k < 256; k++)
            {
                float th = k * Mathf.PI * 2f / 256f;
                float c = Mathf.Cos(th), s = Mathf.Sin(th);
                float r = s_A / Mathf.Pow(Mathf.Pow(Mathf.Abs(c), NBase) + Mathf.Pow(Mathf.Abs(s), NBase), 1f / NBase);
                for (float inset = 0f; inset <= 6f; inset += 2f)
                    edge = Mathf.Max(edge, MapBuilder.Height(c * (r - inset), s * (r - inset)));
            }
            s_Hv = Mathf.Max(12f, edge + 9f);
            s_Hp = Mathf.Clamp(s_Hv + half * 0.42f, PeakMin, PeakMax);
            if (s_Hp < s_Hv + 14f) s_Hp = Mathf.Min(s_Hv + 14f, PeakMax + 12f);
            for (int j = 0; j <= J; j++)
            {
                float u = j / (float)J, phi = u * Mathf.PI * 0.5f;
                s_RingA[j] = j == J ? 0f : s_A * Mathf.Cos(phi);
                s_RingN[j] = Mathf.Lerp(NBase, NTop, Mathf.Pow(u, 1.2f));
                s_RingY[j] = s_Hv + (s_Hp - s_Hv) * Mathf.Sin(phi);
            }
            Built = true;

            var go = new GameObject("MapDome");
            go.transform.SetParent(root, false);
            Root = go;
            var c0 = new Vector3(0, s_Hv * 0.5f, 0);

            // ---- the glass: both sides (it's seen from inside, and from outside on the way in), plus the collider (inside faces) ----
            var glass = new MeshBatch();
            var col = new List<Vector3>();
            var colT = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                glass.Tri(a, b, c, outward);
                glass.Tri(a, b, c, -outward);
                // the collider's triangle faces in
                var n = Vector3.Cross(b - a, c - a);
                int i = col.Count;
                col.Add(a);
                if (Vector3.Dot(n, outward) > 0) { col.Add(c); col.Add(b); } else { col.Add(b); col.Add(c); }
                colT.Add(i); colT.Add(i + 1); colT.Add(i + 2);
            }
            for (int k = 0; k < K; k++)
            {
                // the straight sides, from deep in the ground up to the shoulder
                Vector3 a = P(0, k), b = P(0, k + 1);
                var down = Vector3.up * (s_Hv - Bottom);
                var outw = (a + b) * 0.5f;
                outw.y = 0;
                Face(a - down, b - down, b, outw);
                Face(a - down, b, a, outw);
                for (int j = 0; j < J; j++)
                {
                    Vector3 p00 = P(j, k), p01 = P(j, k + 1), p10 = P(j + 1, k), p11 = P(j + 1, k + 1);
                    var o = (p00 + p11) * 0.5f - c0;
                    if (j == J - 1) Face(p00, p01, p10, o); // (the top ring closes to the peak)
                    else { Face(p00, p01, p11, o); Face(p00, p11, p10, o); }
                }
            }
            s_GlassMat = new Material(Art.Ghost(k_Glass)) { name = "map dome glass" };
            s_GlassMat.renderQueue = 2990; // (drawn before every other see-through thing: they're all inside it)
            var g = glass.Build(go.transform, "map dome glass", s_GlassMat, false);
            g.AddComponent<OwnedMesh>().Mesh = g.GetComponent<MeshFilter>().sharedMesh;
            var mr = g.GetComponent<MeshRenderer>();
            mr.receiveShadows = false;
            s_Rs.Add(mr);

            var cm = new Mesh { name = "map dome collider", indexFormat = col.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            cm.SetVertices(col);
            cm.SetTriangles(colT, 0);
            cm.RecalculateBounds();
            var cgo = new GameObject("map dome");
            cgo.transform.SetParent(go.transform, false);
            cgo.AddComponent<OwnedMesh>().Mesh = cm;
            Collider = cgo.AddComponent<MeshCollider>();
            Collider.sharedMesh = cm;

            // ---- the frame, like the glass wall's: bands round it every 4 m up the straight sides and on every other
            // ring above, and ribs up every fourth segment to the top ----
            float w = 0.25f + half * 0.002f;
            var lines = new MeshBatch();
            Vector3 In(Vector3 p, Vector3 n) => p - n * 0.12f;
            for (int k = 0; k < K; k++)
            {
                Vector3 a = P(0, k), b = P(0, k + 1);
                var n0 = (a + b) * 0.5f;
                n0.y = 0;
                n0.Normalize();
                for (float y = 0.6f; y < s_Hv - 1f; y += 4f)
                {
                    var ya = new Vector3(a.x, y, a.z);
                    var yb = new Vector3(b.x, y, b.z);
                    // (only where it's above the ground)
                    if (Mathf.Max(MapBuilder.Height(a.x, a.z), MapBuilder.Height(b.x, b.z)) > y - 0.3f) continue;
                    lines.Line(In(ya, n0), In(yb, n0), w, 0.06f, -n0);
                }
                lines.Line(In(new Vector3(a.x, s_Hv, a.z), n0), In(new Vector3(b.x, s_Hv, b.z), n0), w * 1.4f, 0.08f, -n0);
                for (int j = 2; j < J - 1; j += 2)
                {
                    Vector3 p = P(j, k), q = P(j, k + 1);
                    var n = ((p + q) * 0.5f - c0).normalized;
                    lines.Line(In(p, n), In(q, n), w, 0.06f, -n);
                }
                if (k % 4 != 0) continue;
                float gnd = Mathf.Max(MapBuilder.Height(a.x, a.z), Bottom + 1f);
                lines.Line(In(new Vector3(a.x, gnd - 0.5f, a.z), n0), In(new Vector3(a.x, s_Hv, a.z), n0), w, 0.06f, -n0);
                for (int j = 0; j < J - 1; j++)
                {
                    Vector3 p = P(j, k), q = P(j + 1, k);
                    var n = ((p + q) * 0.5f - c0).normalized;
                    lines.Line(In(p, n), In(q, n), w, 0.06f, -n);
                }
            }
            s_LineMat = new Material(Art.Ghost(LineColor)) { name = "map dome frame" };
            s_LineMat.renderQueue = 2991;
            var l = lines.Build(go.transform, "map dome frame", s_LineMat, false);
            l.AddComponent<OwnedMesh>().Mesh = l.GetComponent<MeshFilter>().sharedMesh;
            s_Rs.Add(l.GetComponent<MeshRenderer>());

            foreach (var r in s_Rs) look.Normal.Add(r); // (PSX / AI PSX: their concrete walls instead)
            go.AddComponent<MapDomeTint>();
            Retint();
            Debug.Log($"[MapDome] half {s_A:0.0} m, shoulder {s_Hv:0.0} m (edge ground up to {edge:0.0} m), peak {s_Hp:0.0} m");
        }

        /// <summary>The glass and frame in the "Map dome" colour (Normal graphics only).</summary>
        public static void Retint()
        {
            if (s_LineMat == null || s_GlassMat == null) return;
            var slot = ColorSlots.MapWalls;
            var line = ColorSlots.Tinted(slot, LineColor);
            line.a = LineColor.a;
            s_LineMat.SetColor("_BaseColor", line);
            s_LineMat.color = line;
            var g = ColorSlots.Tinted(slot, k_Glass);
            g.a = k_Glass.a;
            s_GlassMat.SetColor("_BaseColor", g);
            s_GlassMat.color = g;
        }

        public static Material GlassMaterial => s_GlassMat;
        public static Material FrameMaterial => s_LineMat;
    }

    /// <summary>Keeps the map dome in its colour when it's changed in Settings > Display.</summary>
    public class MapDomeTint : MonoBehaviour
    {
        void OnEnable() { GameSettings.WorldLookChanged += MapDome.Retint; GameSettings.GraphicsChanged += MapDome.Retint; }
        void OnDisable() { GameSettings.WorldLookChanged -= MapDome.Retint; GameSettings.GraphicsChanged -= MapDome.Retint; }
    }
}
