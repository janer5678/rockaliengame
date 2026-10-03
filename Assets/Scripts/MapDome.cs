using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The big glass dome over the whole map (it replaced the grey boundary walls you used to see), in the same glass and
    /// frame lines as the glass wall that drops. The map is square, so the dome is a rounded square: at the ground it hugs
    /// the edge of the map (a superellipse just outside the old walls' inner faces, so the area you can play in is the same),
    /// goes straight up to a tall shoulder well above the ground anywhere along the edge, then curves over and closes in a
    /// peak in the middle (getting rounder as it goes up): a crown band round the top, eight ribs over it to a small ring
    /// round the peak. Peak 48-68 m: the clouds (80 m+) and the sun are outside it; the ball still drops in from 40 m
    /// inside it. A trim line runs all the way round the bottom, where the glass meets the ground.
    /// The airdrop ship hovers over it and, when its beam comes down, cuts a round hole in the glass (SetHole: the glass and
    /// frame in it go, a rim in the frame's style goes round it) for the crate to come down through, then patches it.
    /// Solid: one non-convex mesh collider (its faces point in), so nothing gets out over the top - it stays whole while
    /// a hole is open (the falling crate is only a picture; the real one appears on the ground) - and the old boundary
    /// walls are still there too, as invisible colliders (drawn only in PSX / AI PSX, which keep their concrete walls and
    /// don't show the dome). Its glass and frame take the "Map dome" colour in Settings > Display (ColorSlots.MapWalls).
    /// </summary>
    public static class MapDome
    {
        /// <summary>Segments round, and rings from the shoulder up to the peak.</summary>
        const int K = 120, J = 14;
        /// <summary>The frame: a rib up every RibEvery-th segment, a band round every BandEvery-th ring and every BandStep
        /// metres up the straight sides.</summary>
        const int RibEvery = 6, BandEvery = 3;
        const float BandStep = 6f;
        /// <summary>The ring the crown band goes round (the ribs stop there; eight go on over the top), and the radius of
        /// the little ring round the peak.</summary>
        const int Crown = J - 2;
        const float HubR = 3f;
        /// <summary>How square the dome is at the ground (a superellipse's exponent) and at the top.</summary>
        const float NBase = 16f, NTop = 2.2f;
        /// <summary>How far down into the ground the straight sides reach.</summary>
        const float Bottom = -22f;
        public const float PeakMin = 48f, PeakMax = 68f;

        static readonly Color k_Glass = new Color(0.6f, 0.9f, 1f, 0.12f);
        static Color LineColor => new Color(0.75f, 0.95f, 1f, 0.45f); // (= the MapWalls slot's default, so the colour picked lands on it exactly)

        static float s_A, s_Hv, s_Hp, s_W;
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
        public static bool Shown => Built && s_Rs.Count > 0 && s_Rs[0] != null && s_Rs[0].enabled && s_Fade > 0.001f;
        /// <summary>Test hooks: how many frame lines there are, and the most the bottom trim is off the ground anywhere round the edge.</summary>
        public static int FrameLines { get; private set; }
        public static float TrimGap { get; private set; }

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
            s_Fade = 1f;
            s_Rs.Clear();
            s_Holes.Clear();
            s_Clear.Clear();
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
            s_Hv = Mathf.Max(22f, edge + 15f);
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
            // the hole mask: the glass and frame are see-through wherever it's clear (SetHole), so their UVs are the map's x, z
            if (s_Mask != null) Object.Destroy(s_Mask);
            s_MaskN = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(s_A * 2f / 0.5f)), 256, 512);
            s_Mask = new Texture2D(s_MaskN, s_MaskN, TextureFormat.RGBA32, false) { name = "map dome holes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            s_MaskPx = new Color32[s_MaskN * s_MaskN];
            for (int i = 0; i < s_MaskPx.Length; i++) s_MaskPx[i] = new Color32(255, 255, 255, 255);
            s_Mask.SetPixels32(s_MaskPx);
            s_Mask.Apply(false);
            s_MaskDirty = false;

            s_GlassMat = new Material(Art.Ghost(k_Glass)) { name = "map dome glass" };
            s_GlassMat.renderQueue = 2990; // (drawn before every other see-through thing: they're all inside it)
            Masked(s_GlassMat);
            var g = glass.Build(go.transform, "map dome glass", s_GlassMat, false);
            g.AddComponent<OwnedMesh>().Mesh = MapUVs(g);
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

            // ---- the frame, like the glass wall's: a trim along the bottom, bands round it every BandStep m up the straight
            // sides and on every BandEvery-th ring above, a crown band near the top and one more above it, ribs up every
            // RibEvery-th segment to the crown and eight on over the top to a little ring round the peak ----
            float w = s_W = 0.25f + half * 0.002f;
            var lines = new MeshBatch();
            int count = 0;
            float trimGap = 0f;
            Vector3 In(Vector3 p, Vector3 n) => p - n * 0.12f;
            void Line(Vector3 a, Vector3 b, Vector3 n, float width, float height) { lines.Line(In(a, n), In(b, n), width, height, -n); count++; }
            Vector3 Out(Vector3 p) => ((p - c0).normalized);
            for (int k = 0; k < K; k++)
            {
                Vector3 a = P(0, k), b = P(0, k + 1);
                var n0 = (a + b) * 0.5f;
                n0.y = 0;
                n0.Normalize();
                // the trim along the bottom, where the glass meets the ground: in short steps, so it follows the ground,
                // end to end (not overlapping like the other lines, so it's one even band)
                const int steps = 4;
                for (int s = 0; s < steps; s++)
                {
                    var pa = Vector3.Lerp(a, b, s / (float)steps);
                    var pb = Vector3.Lerp(a, b, (s + 1) / (float)steps);
                    var pm = (pa + pb) * 0.5f;
                    pa.y = MapBuilder.Height(pa.x, pa.z) + 0.25f;
                    pb.y = MapBuilder.Height(pb.x, pb.z) + 0.25f;
                    trimGap = Mathf.Max(trimGap, Mathf.Abs((pa.y + pb.y) * 0.5f - MapBuilder.Height(pm.x, pm.z) - 0.25f));
                    var d = pb - pa;
                    var side = Vector3.Cross(-n0, d).normalized;
                    lines.Box((In(pa, n0) + In(pb, n0)) * 0.5f, Quaternion.LookRotation(d, Vector3.Cross(d, side)), new Vector3(w * 1.1f, 0.1f, d.magnitude));
                    count++;
                }
                for (float y = BandStep; y < s_Hv - 2f; y += BandStep)
                {
                    var ya = new Vector3(a.x, y, a.z);
                    var yb = new Vector3(b.x, y, b.z);
                    // (only where it's above the ground)
                    if (Mathf.Max(MapBuilder.Height(a.x, a.z), MapBuilder.Height(b.x, b.z)) > y - 0.3f) continue;
                    Line(ya, yb, n0, w, 0.06f);
                }
                Line(new Vector3(a.x, s_Hv, a.z), new Vector3(b.x, s_Hv, b.z), n0, w * 1.4f, 0.08f);
                for (int j = BandEvery; j <= Crown; j += BandEvery)
                {
                    Vector3 p = P(j, k), q = P(j, k + 1);
                    Line(p, q, Out((p + q) * 0.5f), j == Crown ? w * 1.4f : w, j == Crown ? 0.08f : 0.06f);
                }
                {
                    // (and one more round the cap, between the crown and the peak)
                    Vector3 p = P(J - 1, k), q = P(J - 1, k + 1);
                    Line(p, q, Out((p + q) * 0.5f), w, 0.06f);
                }
                if (k % RibEvery == 0)
                {
                    float gnd = Mathf.Max(MapBuilder.Height(a.x, a.z), Bottom + 1f);
                    Line(new Vector3(a.x, gnd - 0.5f, a.z), new Vector3(a.x, s_Hv, a.z), n0, w, 0.06f);
                    for (int j = 0; j < Crown; j++)
                    {
                        Vector3 p = P(j, k), q = P(j + 1, k);
                        Line(p, q, Out((p + q) * 0.5f), w, 0.06f);
                    }
                }
                // over the top: eight ribs (along the middles of the sides and to the corners) go on up to the ring round the peak
                if (k % (K / 8) != 0) continue;
                float th = k * Mathf.PI * 2f / K;
                var hub = new Vector3(Mathf.Cos(th) * HubR, 0, Mathf.Sin(th) * HubR);
                hub.y = HeightAt(hub.x, hub.z);
                for (int j = Crown; j < J - 1; j++)
                {
                    Vector3 p = P(j, k), q = P(j + 1, k);
                    Line(p, q, Out((p + q) * 0.5f), w, 0.06f);
                }
                Line(P(J - 1, k), hub, Out((P(J - 1, k) + hub) * 0.5f), w, 0.06f);
            }
            // the little ring round the peak
            for (int i = 0; i < 16; i++)
            {
                float a0 = i * Mathf.PI * 2f / 16f, a1 = (i + 1) * Mathf.PI * 2f / 16f;
                var p = new Vector3(Mathf.Cos(a0) * HubR, 0, Mathf.Sin(a0) * HubR);
                var q = new Vector3(Mathf.Cos(a1) * HubR, 0, Mathf.Sin(a1) * HubR);
                p.y = HeightAt(p.x, p.z);
                q.y = HeightAt(q.x, q.z);
                Line(p, q, Vector3.up, w * 1.4f, 0.08f);
            }
            FrameLines = count;
            TrimGap = trimGap;
            s_LineMat = new Material(Art.Ghost(LineColor)) { name = "map dome frame" };
            s_LineMat.renderQueue = 2991;
            Masked(s_LineMat);
            var l = lines.Build(go.transform, "map dome frame", s_LineMat, false);
            l.AddComponent<OwnedMesh>().Mesh = MapUVs(l);
            s_Rs.Add(l.GetComponent<MeshRenderer>());

            foreach (var r in s_Rs) look.Normal.Add(r); // (PSX / AI PSX: their concrete walls instead)
            go.AddComponent<MapDomeTint>();
            Retint();
            Debug.Log($"[MapDome] half {s_A:0.0} m, shoulder {s_Hv:0.0} m (edge ground up to {edge:0.0} m), peak {s_Hp:0.0} m, {count} frame lines, hole mask {s_MaskN} px");
        }

        /// <summary>The glass and frame in the "Map dome" colour (Normal graphics only).</summary>
        public static void Retint()
        {
            if (s_LineMat == null || s_GlassMat == null) return;
            var slot = ColorSlots.MapWalls;
            var line = ColorSlots.Tinted(slot, LineColor);
            line.a = LineColor.a * s_Fade;
            s_LineMat.SetColor("_BaseColor", line);
            s_LineMat.color = line;
            var g = ColorSlots.Tinted(slot, k_Glass);
            g.a = k_Glass.a * s_Fade;
            s_GlassMat.SetColor("_BaseColor", g);
            s_GlassMat.color = g;
        }

        static float s_Fade = 1f;

        /// <summary>How much of the dome there is (the victory cutscene takes it away: 1 as normal .. 0 gone).</summary>
        public static float Fade => s_Fade;

        /// <summary>The dome fading away (the victory cutscene, so the camera and the UFO have the open sky): its glass and
        /// frame fade out, and at 0 it's switched off altogether (drawn and solid again at 1; a new map builds a new one).</summary>
        public static void SetFade(float k)
        {
            k = Mathf.Clamp01(k);
            if (!Built || Root == null || Mathf.Approximately(k, s_Fade)) return;
            s_Fade = k;
            bool on = k > 0.001f;
            if (Root.activeSelf != on) Root.SetActive(on);
            Retint();
        }

        public static Material GlassMaterial => s_GlassMat;
        public static Material FrameMaterial => s_LineMat;

        // ------------------------------------------------------------------ holes (the airdrop ship's)

        /// <summary>One round hole in the glass: where (x, z), how wide, how much its edge glows, and its rim.</summary>
        class Hole
        {
            public float X, Z, R, Glow;
            public RectInt Drawn;
            public bool Dirty;
            public GameObject Go;
            public MeshRenderer RimR, GlowR;
            public Mesh RimM, GlowM;
            public Material GlowMat;
        }
        static readonly Dictionary<int, Hole> s_Holes = new Dictionary<int, Hole>();
        /// <summary>Where removed holes were drawn into the mask (cleared on its next redraw).</summary>
        static readonly List<RectInt> s_Clear = new List<RectInt>();
        static Texture2D s_Mask;
        static Color32[] s_MaskPx;
        static int s_MaskN;
        static bool s_MaskDirty;
        static readonly Color k_HoleGlow = new Color(0.75f, 0.35f, 1f);

        /// <summary>
        /// Cuts (or moves, widens, closes) hole `id` in the glass: a round hole radius metres across over (x, z) - round
        /// seen from straight above, so a vertical beam that wide goes through clean. The glass and frame lines in it go;
        /// a rim in the frame's style runs round its edge, which glows purple by `glow` (0-1). radius 0 patches it up.
        /// Local only: whoever cuts it does so on every peer from the same synced clock (AirdropShip).
        /// </summary>
        public static void SetHole(int id, float x, float z, float radius, float glow)
        {
            if (!Built || Root == null) return;
            s_Holes.TryGetValue(id, out var h);
            if (radius <= 0.05f || HeightAt(x, z) == float.MinValue)
            {
                if (h == null) return;
                s_Clear.Add(h.Drawn);
                s_MaskDirty = true;
                if (h.Go) Object.Destroy(h.Go);
                if (h.RimM) Object.Destroy(h.RimM);
                if (h.GlowM) Object.Destroy(h.GlowM);
                if (h.GlowMat) Object.Destroy(h.GlowMat);
                s_Holes.Remove(id);
                return;
            }
            if (h == null)
            {
                h = new Hole { R = -1f, Glow = -1f };
                h.Go = new GameObject("map dome hole " + id);
                h.Go.transform.SetParent(Root.transform, false);
                h.RimM = new Mesh { name = "hole rim" };
                h.GlowM = new Mesh { name = "hole glow" };
                h.RimR = Part(h.Go.transform, "rim", h.RimM, s_LineMat);
                h.GlowMat = new Material(Art.Ghost(k_HoleGlow)) { name = "hole glow", renderQueue = 2992 };
                h.GlowR = Part(h.Go.transform, "glow", h.GlowM, h.GlowMat);
                s_Holes[id] = h;
            }
            if (h.X != x || h.Z != z || Mathf.Abs(h.R - radius) > 0.005f)
            {
                h.X = x; h.Z = z; h.R = radius;
                h.Dirty = true;
                s_MaskDirty = true;
            }
            if (Mathf.Abs(h.Glow - glow) > 0.005f)
            {
                h.Glow = glow;
                var c = k_HoleGlow;
                c.a = 0.85f * Mathf.Clamp01(glow);
                h.GlowMat.SetColor("_BaseColor", c);
                h.GlowMat.color = c;
            }
        }

        /// <summary>Test hooks: hole id's radius now (0: none), and how see-through the glass is over (x, z) (1 solid glass, 0 a hole).</summary>
        public static float HoleRadius(int id) => s_Holes.TryGetValue(id, out var h) ? h.R : 0f;
        public static float GlassAt(float x, float z)
        {
            if (s_MaskPx == null) return 1f;
            int i = Mathf.Clamp(Mathf.FloorToInt((x + s_A) / (2f * s_A) * s_MaskN), 0, s_MaskN - 1);
            int j = Mathf.Clamp(Mathf.FloorToInt((z + s_A) / (2f * s_A) * s_MaskN), 0, s_MaskN - 1);
            return s_MaskPx[j * s_MaskN + i].a / 255f;
        }
        /// <summary>Test hook: hole id's rim (null when there's no hole).</summary>
        public static MeshRenderer HoleRim(int id) => s_Holes.TryGetValue(id, out var h) ? h.RimR : null;

        /// <summary>Once a frame (MapDomeTint): rebuilds the rims of holes that changed, redraws the mask, shows them with the dome.</summary>
        internal static void LateTick()
        {
            bool shown = Shown;
            foreach (var h in s_Holes.Values)
            {
                if (h.Dirty) { BuildRim(h); h.Dirty = false; }
                if (h.RimR.enabled != shown) h.RimR.enabled = shown;
                bool glow = shown && h.Glow > 0.01f;
                if (h.GlowR.enabled != glow) h.GlowR.enabled = glow;
            }
            if (!s_MaskDirty || s_Mask == null) return;
            s_MaskDirty = false;
            foreach (var r in s_Clear) Fill(r);
            s_Clear.Clear();
            foreach (var h in s_Holes.Values) Fill(h.Drawn);
            float texel = 2f * s_A / s_MaskN;
            foreach (var h in s_Holes.Values)
            {
                int x0 = Mathf.FloorToInt((h.X - h.R + s_A) / texel) - 2, x1 = Mathf.CeilToInt((h.X + h.R + s_A) / texel) + 2;
                int z0 = Mathf.FloorToInt((h.Z - h.R + s_A) / texel) - 2, z1 = Mathf.CeilToInt((h.Z + h.R + s_A) / texel) + 2;
                x0 = Mathf.Max(0, x0); z0 = Mathf.Max(0, z0); x1 = Mathf.Min(s_MaskN, x1); z1 = Mathf.Min(s_MaskN, z1);
                h.Drawn = new RectInt(x0, z0, x1 - x0, z1 - z0);
                for (int j = z0; j < z1; j++)
                    for (int i = x0; i < x1; i++)
                    {
                        float px = (i + 0.5f) * texel - s_A, pz = (j + 0.5f) * texel - s_A;
                        float d = Mathf.Sqrt((px - h.X) * (px - h.X) + (pz - h.Z) * (pz - h.Z));
                        byte a = (byte)(Mathf.Clamp01((d - h.R) / texel + 0.5f) * 255f);
                        int at = j * s_MaskN + i;
                        if (a < s_MaskPx[at].a) s_MaskPx[at].a = a;
                    }
            }
            s_Mask.SetPixels32(s_MaskPx);
            s_Mask.Apply(false);
        }

        static void Fill(RectInt r)
        {
            for (int j = Mathf.Max(0, r.yMin); j < Mathf.Min(s_MaskN, r.yMax); j++)
                for (int i = Mathf.Max(0, r.xMin); i < Mathf.Min(s_MaskN, r.xMax); i++)
                    s_MaskPx[j * s_MaskN + i].a = 255;
        }

        static void Masked(Material m)
        {
            m.SetTexture("_BaseMap", s_Mask);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
        }

        /// <summary>Gives a built dome mesh UVs from the map's x, z (for the hole mask); returns the mesh.</summary>
        static Mesh MapUVs(GameObject go)
        {
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            var v = m.vertices;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) uv[i] = new Vector2((v[i].x + s_A) / (2f * s_A), (v[i].z + s_A) / (2f * s_A));
            m.uv = uv;
            return m;
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static readonly List<Vector3> s_V = new List<Vector3>(), s_N = new List<Vector3>();
        static readonly List<Vector2> s_U = new List<Vector2>();
        static readonly List<int> s_T = new List<int>();

        /// <summary>The hole's rim, like the frame's bands: a wide band right round the edge and a thin line just outside
        /// it, both through the glass (seen from both sides); and the glowing edge inside it.</summary>
        static void BuildRim(Hole h)
        {
            float band = Mathf.Min(s_W * 1.4f, h.R * 0.45f);
            s_V.Clear(); s_N.Clear(); s_T.Clear(); s_U.Clear();
            Ring(h.X, h.Z, Mathf.Max(0f, h.R - 0.3f), h.R - 0.3f + band, 0.18f);
            float r2 = h.R - 0.3f + band + Mathf.Min(s_W * 0.9f, h.R * 0.3f);
            Ring(h.X, h.Z, r2, r2 + Mathf.Min(s_W * 0.5f, h.R * 0.15f), 0.14f);
            Apply(h.RimM);
            s_V.Clear(); s_N.Clear(); s_T.Clear(); s_U.Clear();
            Ring(h.X, h.Z, Mathf.Max(0f, h.R - 0.55f), Mathf.Max(0.02f, h.R - 0.2f), 0.24f);
            Apply(h.GlowM);
        }

        static void Apply(Mesh m)
        {
            m.Clear();
            m.SetVertices(s_V);
            m.SetNormals(s_N);
            m.SetUVs(0, s_U);
            m.SetTriangles(s_T, 0);
            m.RecalculateBounds();
        }

        /// <summary>A flat ring on the glass round (cx, cz), from r0 to r1 out (seen from above), `thick` through it.</summary>
        static void Ring(float cx, float cz, float r0, float r1, float thick)
        {
            const int S = 40;
            int b = s_V.Count;
            float hh = thick * 0.5f;
            for (int i = 0; i <= S; i++)
            {
                float a = i * Mathf.PI * 2f / S, c = Mathf.Cos(a), s = Mathf.Sin(a);
                var p0 = new Vector3(cx + c * r0, 0, cz + s * r0);
                var p1 = new Vector3(cx + c * r1, 0, cz + s * r1);
                p0.y = HeightAt(p0.x, p0.z);
                p1.y = HeightAt(p1.x, p1.z);
                if (p0.y == float.MinValue || p1.y == float.MinValue) { p0.y = p1.y = HeightAt(cx, cz); }
                var n = NormalAt((p0.x + p1.x) * 0.5f, (p0.z + p1.z) * 0.5f);
                var rad = Vector3.ProjectOnPlane(p1 - p0, n).normalized;
                // top, bottom, inner side, outer side
                Vert(p0 + n * hh, n); Vert(p1 + n * hh, n);
                Vert(p0 - n * hh, -n); Vert(p1 - n * hh, -n);
                Vert(p0 + n * hh, -rad); Vert(p0 - n * hh, -rad);
                Vert(p1 + n * hh, rad); Vert(p1 - n * hh, rad);
            }
            for (int i = 0; i < S; i++)
            {
                int o = b + i * 8, q = o + 8;
                for (int f = 0; f < 4; f++) Quad(o + f * 2, o + f * 2 + 1, q + f * 2 + 1, q + f * 2);
            }
        }

        static void Vert(Vector3 p, Vector3 n)
        {
            s_V.Add(p);
            s_N.Add(n);
            s_U.Add(new Vector2(-1f, -1f)); // (off the hole mask's edge: clamped to the corner, which is always solid)
        }

        /// <summary>Two triangles facing the way their corners' normal points.</summary>
        static void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        static void Tri(int a, int b, int c)
        {
            var n = Vector3.Cross(s_V[b] - s_V[a], s_V[c] - s_V[a]);
            if (Vector3.Dot(n, s_N[a]) < 0) (b, c) = (c, b);
            s_T.Add(a); s_T.Add(b); s_T.Add(c);
        }
    }

    /// <summary>Keeps the map dome in its colour when it's changed in Settings > Display, and its holes up to date.</summary>
    public class MapDomeTint : MonoBehaviour
    {
        void OnEnable() { GameSettings.WorldLookChanged += MapDome.Retint; GameSettings.GraphicsChanged += MapDome.Retint; }
        void OnDisable() { GameSettings.WorldLookChanged -= MapDome.Retint; GameSettings.GraphicsChanged -= MapDome.Retint; }
        void LateUpdate() => MapDome.LateTick();
    }
}
