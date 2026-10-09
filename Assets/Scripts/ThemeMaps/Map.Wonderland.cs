using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// WONDERLAND (in space): the map is a square slab of Wonderland floating in black starry space - constellations, a
    /// big ringed planet and a little moon. Its edges are a stack of playing cards over a rocky underside; walk off one
    /// and you fall into space (KillY). Checkerboard plaza round the middle and checkerboard paths to the bases, giant
    /// mushrooms (stand on the caps), card soldiers and card houses, teacups and teapots, a mad tea party, pocket watches,
    /// topiary, painted roses, chess pieces, a sign pointing every way - and cards, cups and clocks tumbling past in space.
    /// The band in from the slab's edges gets its own extra helping of all that (it was bare next to round the bases).
    /// The horses are Cheshire Cats; the berry bushes are clipped rose bushes full of strawberries, a jam tart on top.
    /// LAUNCH PADS (shining gold mushroom caps on springs, glowing and sparkling, arrows on them) throw you across the map: from by your base to the
    /// edge of the middle, round from one flank into the next team's, and from the middle back out to your flank - a
    /// red and white target marks where each lands you. A lot of it moves: the teacups spin, the pocket watches and
    /// teapots rock, the big mushrooms squash and stretch, the card soldiers march on the spot, the topiary and signposts
    /// turn. And the ways from each base to the middle have plenty of cover along them (hedges, cards, rose bushes,
    /// toadstools) for sneaking past, and the open ground out by the slab's edges and corners has loose clusters of cover
    /// (hedges, cards, mushrooms, teapots, stacks of giant books, chess pieces).
    /// </summary>
    public class WonderlandMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Wonderland;
        public override string Label => "Wonderland";
        public override string Blurb => "A square slab of Wonderland floating in space: giant mushrooms, playing cards, teacups and checkerboards. The edges drop into space - don't fall off!";
        public override float KillY => -6f;
        public override bool Mountains => false;
        public override bool Dome => false;
        public override float MaxSpotHeight => 3f;

        static float Half => Cfg.MapHalf;
        /// <summary>The slab's plain edge (each side), and how far it reaches out round a base (so the base fits).</summary>
        static float Side => Half - Mathf.Clamp(Half * 0.05f, 3.5f, 8f);
        static float Notch => Mathf.Min(Half - 0.4f, Mathf.Max(Side, Cfg.BaseCenter[0].magnitude + Cfg.BaseHalf + 2.5f));
        static float NotchW => Cfg.BaseHalf + 5f;
        static bool NotchX => Cfg.FourWay;   // (the x sides only have bases in the 3/4 team modes)
        static float PlazaR => Cfg.SmallMap ? 20f : 30f;
        const float SlabDepth = 3.2f;

        static readonly Color k_White = new Color(0.93f, 0.91f, 0.87f), k_Black = new Color(0.13f, 0.11f, 0.15f), k_Red = new Color(0.82f, 0.16f, 0.2f);

        // ---------------------------------------------------------------- the slab
        static bool InSlab(float x, float z)
        {
            float s = Side + 0.001f, n = Notch + 0.001f, w = NotchW;
            if (Mathf.Abs(x) <= s && Mathf.Abs(z) <= s) return true;
            if (Mathf.Abs(x) <= w && Mathf.Abs(z) <= n) return true;
            if (NotchX && Mathf.Abs(z) <= w && Mathf.Abs(x) <= n) return true;
            return false;
        }

        /// <summary>The nearest point on the slab to (x, z).</summary>
        static Vector2 Clamp(float x, float z)
        {
            float s = Side, n = Notch, w = NotchW;
            var best = new Vector2(Mathf.Clamp(x, -s, s), Mathf.Clamp(z, -s, s));
            float bd = (best - new Vector2(x, z)).sqrMagnitude;
            void Try(Vector2 c) { float d = (c - new Vector2(x, z)).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            Try(new Vector2(Mathf.Clamp(x, -w, w), Mathf.Clamp(z, -n, n)));
            if (NotchX) Try(new Vector2(Mathf.Clamp(x, -n, n), Mathf.Clamp(z, -w, w)));
            return best;
        }

        static float PathLat(float x, float z)
        {
            float best = 999f;
            for (int k = 0; k < Cfg.Copies; k++)
            {
                var d = Cfg.Copy(Vector3.back, k);
                float a = x * d.x + z * d.z;
                if (a < 0f) continue;
                best = Mathf.Min(best, Mathf.Abs(x * d.z - z * d.x));
            }
            return best;
        }

        static float EdgeDist(float x, float z)
        {
            float m = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            return Side - m;
        }

        /// <summary>0 grass, 1 the black and white checkerboard, 2 the red and white path checkerboard.</summary>
        static int Zone(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r < PlazaR) return 1;
            if (EdgeDist(x, z) < 6f) return 1;
            if (PathLat(x, z) < 4.5f) return 2;
            return 0;
        }

        public override float Height(float x, float z)
        {
            if (!InSlab(x, z)) return -60f;
            float h = Mathf.Max(0f, (ThemeMaps.SymNoiseP(x, z, 0.03f, ThemeMaps.SeedP) - 0.47f) * 5f);
            float r = Mathf.Sqrt(x * x + z * z);
            h *= ThemeMaps.SmoothStepP(PlazaR, PlazaR + 6f, r) * ThemeMaps.SmoothStepP(6f, 12f, EdgeDist(x, z)) * ThemeMaps.SmoothStepP(4.5f, 9f, PathLat(x, z));
            return h * ThemeMaps.MaskP(x, z);
        }

        public override bool SpotOk(Vector3 p)
        {
            if (!InSlab(p.x, p.z) || EdgeDist(p.x, p.z) < 6f) return false;
            return new Vector2(p.x, p.z).magnitude > PlazaR + 2f && PathLat(p.x, p.z) > 6f;
        }

        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1f : kind == ResourceNode.Bush ? 1f : 0.8f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.95f, 0.45f, 0.72f), 0.7f);

        int Vert(float x, float z, out Vector3 p)
        {
            if (InSlab(x, z)) { p = new Vector3(x, Height(x, z), z); return 0; }
            var c = Clamp(x, z);
            p = new Vector3(c.x, Height(c.x * 0.999f, c.y * 0.999f), c.y);
            return 1;
        }

        static Color GroundColour(Vector3 c, float ny)
        {
            int zone = Zone(c.x, c.z);
            bool odd = ((Mathf.FloorToInt(c.x / 3f) + Mathf.FloorToInt(c.z / 3f)) & 1) == 1;
            if (zone == 1) return odd ? k_Black : k_White;
            if (zone == 2) return odd ? k_Red : k_White;
            if (ny < 0.85f) return new Color(0.26f, 0.5f, 0.3f);
            float n = ThemeMaps.SymNoiseP(c.x, c.z, 0.045f, ThemeMaps.SeedP + 11f);
            var g = Color.Lerp(new Color(0.42f, 0.74f, 0.36f), new Color(0.3f, 0.66f, 0.5f), n);
            float f = ThemeMaps.SymNoiseP(c.x, c.z, 0.11f, ThemeMaps.SeedP + 40f);
            if (f > 0.66f) g = Color.Lerp(g, new Color(0.95f, 0.62f, 0.82f), 0.75f);
            else if (f < 0.3f) g = Color.Lerp(g, new Color(0.98f, 0.9f, 0.45f), 0.5f);
            return g;
        }

        public override void BuildGround(Transform root)
        {
            float g = Mathf.Ceil(Half / 3f) * 3f;
            ThemeKitB.Ground(root, "Ground", -g, g, 3f, Vert, GroundColour);
            BuildUnderside(root);
        }

        /// <summary>The slab's edges (a stack of cards) and its rocky underside, narrowing to a point far below.</summary>
        void BuildUnderside(Transform root)
        {
            var kit = new MeshKit();
            float s = Side, n = Notch, w = NotchW;
            bool notchZ = n > s + 0.05f, notchX = NotchX && notchZ;
            // the outline, going round (one side at a time, from corner to corner)
            var outline = new List<Vector2>();
            for (int side = 0; side < 4; side++)
            {
                bool hasNotch = side % 2 == 0 ? notchZ : notchX;
                var pts = new List<Vector2> { new Vector2(-s, -s) };
                if (hasNotch) { pts.Add(new Vector2(-w, -s)); pts.Add(new Vector2(-w, -n)); pts.Add(new Vector2(w, -n)); pts.Add(new Vector2(w, -s)); }
                // (side 0: the -z side, then turned a quarter at a time: -z, +x, +z, -x)
                var q = Quaternion.Euler(0, -90f * side, 0);
                foreach (var p in pts)
                {
                    var v = q * new Vector3(p.x, 0, p.y);
                    outline.Add(new Vector2(v.x, v.z));
                }
            }
            Color[] layers = { new Color(0.38f, 0.66f, 0.32f), k_White, k_Red, k_White, k_Black, k_White, k_Red, k_White, new Color(0.4f, 0.3f, 0.45f) };
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                var mid = (a + b) * 0.5f;
                var o3 = new Vector3(b.y - a.y, 0, a.x - b.x);
                if (Vector3.Dot(o3, new Vector3(mid.x, 0, mid.y)) < 0f) o3 = -o3;
                float y0 = 0.02f;
                for (int l = 0; l < layers.Length; l++)
                {
                    float y1 = -SlabDepth * (l + 1) / layers.Length;
                    ThemeKitB.Quad(kit, new Vector3(a.x, y0, a.y), new Vector3(b.x, y0, b.y), new Vector3(b.x, y1, b.y), new Vector3(a.x, y1, a.y), layers[l], o3);
                    y0 = y1;
                }
            }
            // the notches' bottoms
            if (notchZ)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    float z0 = sg * s, z1 = sg * n, yb = -SlabDepth;
                    ThemeKitB.Quad(kit, new Vector3(-w, yb, z0), new Vector3(w, yb, z0), new Vector3(w, yb, z1), new Vector3(-w, yb, z1), new Color(0.35f, 0.26f, 0.4f), Vector3.down);
                    if (notchX) ThemeKitB.Quad(kit, new Vector3(z0, yb, -w), new Vector3(z0, yb, w), new Vector3(z1, yb, w), new Vector3(z1, yb, -w), new Color(0.35f, 0.26f, 0.4f), Vector3.down);
                }
            // the underside: rings of a squarish rock narrowing to a point (rough: each corner pushed in and out)
            float[] ry = { -SlabDepth, -12f, -24f, -38f };
            float[] rs = { 1f, 0.82f, 0.58f, 0.32f };
            float apex = -(18f + s * 0.45f);
            const int per = 12;
            int ringN = per * 4;
            Vector3 RingP(int ring, int i)
            {
                i = (i % ringN + ringN) % ringN;
                int sideI = i / per; float t = (i % per) / (float)per;
                var p = new Vector2(Mathf.Lerp(-1f, 1f, t), -1f);
                var q = Quaternion.Euler(0, -90f * sideI, 0) * new Vector3(p.x, 0, p.y);
                float j = ring == 0 ? 1f : 1f + (Mathf.PerlinNoise(i * 0.53f, ring * 1.9f) - 0.5f) * 0.22f;
                return new Vector3(q.x * s * rs[ring] * j, ry[ring] + (ring == 0 ? 0f : (Mathf.PerlinNoise(i * 0.31f + 7f, ring) - 0.5f) * 3f), q.z * s * rs[ring] * j);
            }
            Color[] under = { new Color(0.42f, 0.3f, 0.46f), new Color(0.34f, 0.24f, 0.4f), new Color(0.26f, 0.38f, 0.36f), new Color(0.3f, 0.22f, 0.36f) };
            for (int ring = 0; ring < ry.Length; ring++)
                for (int i = 0; i < ringN; i++)
                {
                    Vector3 a = RingP(ring, i), b = RingP(ring, i + 1);
                    var o = (a + b) * 0.5f; o.y = -o.magnitude * 0.3f;
                    var col = under[(ring + i / 5) % under.Length];
                    if (ring + 1 < ry.Length) ThemeKitB.Quad(kit, a, b, RingP(ring + 1, i + 1), RingP(ring + 1, i), col, o);
                    else ThemeKitB.Tri(kit, a, b, new Vector3(0, apex, 0), col, o);
                }
            // vines hanging over the edges, with little roses on them
            var rng = new System.Random(Cfg.MapSeed + 31);
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            int vines = Mathf.RoundToInt(s * 0.9f);
            for (int v = 0; v < vines; v++)
            {
                int idx = rng.Next(outline.Count);
                var a = outline[idx]; var b = outline[(idx + 1) % outline.Count];
                var p2 = Vector2.Lerp(a, b, R(0.1f, 0.9f));
                var o = new Vector2(b.y - a.y, a.x - b.x).normalized;
                if (Vector2.Dot(o, p2) < 0f) o = -o;
                var top = new Vector3(p2.x + o.x * 0.08f, 0.05f, p2.y + o.y * 0.08f);
                var bot = top + new Vector3(o.x * R(0.2f, 1f), -R(2.5f, 8f), o.y * R(0.2f, 1f));
                ThemeKitB.Cyl(kit, top, bot, 0.09f, 0.05f, 4, new Color(0.22f, 0.5f, 0.24f), false, false);
                if (rng.NextDouble() < 0.6) ThemeKitB.Ball(kit, Vector3.Lerp(top, bot, R(0.3f, 0.9f)), Vector3.one * 0.22f, Quaternion.identity, rng.NextDouble() < 0.5 ? k_Red : k_White, 0);
            }
            ThemeKitB.Spawn(root, "slab underside", kit, null, false);
        }

        // ---------------------------------------------------------------- props
        readonly List<(Transform t, Vector3 pos, Quaternion rot, Vector3 axis, float spin, float bob, float ph)> m_Float = new List<(Transform, Vector3, Quaternion, Vector3, float, float, float)>();
        Transform m_Sky;

        public override void BuildProps(Transform root)
        {
            m_Float.Clear();
            m_Movers.Clear();
            m_Pads.Clear();
            m_MoverRoot = new GameObject("moving props").transform;
            m_MoverRoot.SetParent(root, false);
            var rng = new System.Random(Cfg.MapSeed + 9090);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var k = new MeshKit();
            var cols = new GameObject("wonderland colliders").transform;
            cols.SetParent(root, false);
            float sc = Half / 100f;
            var placed = new List<(Vector2 p, float r)>();
            var bc = Cfg.BaseCenter[0];

            // a spot in the first team's part (clear of the base, the checkerboards and everything else), copied round
            bool Spot(float clear, float minR, float maxR, out Vector3 p)
            {
                for (int tries = 0; tries < 80; tries++)
                {
                    p = new Vector3(R(-Side + 8f, Side - 8f), 0, R(-Side + 8f, -6f));
                    if (!Cfg.InFirstSector(p, 5f + clear)) continue;
                    float r = new Vector2(p.x, p.z).magnitude;
                    if (r < minR || r > maxR) continue;
                    if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 4f + clear && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 4f + clear) continue;
                    if (EdgeDist(p.x, p.z) < 7f + clear || PathLat(p.x, p.z) < 6.5f + clear) continue;
                    bool hit = false;
                    foreach (var q in placed) if ((q.p - new Vector2(p.x, p.z)).magnitude < q.r + clear + 1.5f) { hit = true; break; }
                    if (hit) continue;
                    placed.Add((new Vector2(p.x, p.z), clear));
                    return true;
                }
                p = default;
                return false;
            }
            void Each(Vector3 p, float yaw, System.Action<Vector3, float> make)
            {
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var q = Cfg.Copy(p, m);
                    q.y = Height(q.x, q.z);
                    make(q, yaw + m * 360f / Cfg.Copies);
                }
            }
            int N(float n) => Mathf.Max(1, Mathf.RoundToInt(n * sc));
            float far = 9999f, wild = PlazaR + 4f;
            // the same, moving (see Mover): its look turns / rocks / squashes / marches, its colliders stay put
            void EachMove(Vector3 p, float yaw, byte mode, float spd, float amp, System.Action<MeshKit, Transform, Vector3, float> make)
            {
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var q = Cfg.Copy(p, m);
                    q.y = Height(q.x, q.z);
                    float y = yaw + m * 360f / Cfg.Copies;
                    // (spinning ones go either way; rocking ones rock in the plane they face)
                    float dir = ((Mathf.RoundToInt(Mathf.Abs(p.x * 7f + p.z * 13f)) & 1) == 0) ? 1f : -1f;
                    Moving(cols, q, y, mode, mode == 0 ? spd * dir : spd, amp, Yaw(y) * Vector3.forward, make);
                }
            }
            BuildPads(root, placed);

            // the mad tea party (one each)
            if (Spot(7f, wild, far, out var tp)) { int sd = rng.Next(); Each(tp, R(0, 360), (q, y) => TeaParty(k, cols, q, y, sd)); }
            for (int i = 0; i < N(5); i++) if (Spot(4.5f, wild, far, out var p)) { int sd = rng.Next(); float s = R(0.8f, 1.35f); EachMove(p, R(0, 360), 2, 1.3f, 0.045f, (kk, cc, q, y) => Mushroom(kk, cc, q, y, s, sd, true)); }
            for (int i = 0; i < N(6); i++) if (Spot(2f, wild, far, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => MushroomPatch(k, q, sd)); }
            for (int i = 0; i < N(2); i++) if (Spot(6f, wild, far, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 3, 4.2f, 0.16f, (kk, cc, q, y) => CardSoldiers(kk, cc, q, y, sd)); }
            for (int i = 0; i < N(1.5f); i++) if (Spot(3f, wild, far, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => CardHouse(k, cols, q, y, sd)); }
            for (int i = 0; i < N(2); i++) if (Spot(3.5f, wild, far, out var p)) { int sd = rng.Next(); float s = R(0.8f, 1.2f); EachMove(p, R(0, 360), 0, 0.45f, 0f, (kk, cc, q, y) => Teacup(kk, cc, q, y, s, sd)); }
            for (int i = 0; i < N(1); i++) if (Spot(3f, wild, far, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 1, 0.9f, 7f, (kk, cc, q, y) => Teapot(kk, cc, q, y, 1.2f, sd)); }
            for (int i = 0; i < N(2); i++) if (Spot(3f, wild, far, out var p)) { float tilt = R(-18f, 8f); EachMove(p, R(0, 360), 1, 1.5f, 11f, (kk, cc, q, y) => Watch(kk, cc, q, y, 1f, tilt)); }
            for (int i = 0; i < N(5); i++) if (Spot(2.5f, wild, far, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 0, 0.2f, 0f, (kk, cc, q, y) => Topiary(kk, cc, q, y, sd)); }
            for (int i = 0; i < N(3); i++) if (Spot(4f, wild, far, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => Hedge(k, cols, q, y, sd)); }
            for (int i = 0; i < N(5); i++) if (Spot(2f, wild, far, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => RoseBush(k, cols, q, y, sd)); }
            if (Spot(2f, wild, far, out var sp)) EachMove(sp, R(0, 360), 0, 0.35f, 0f, (kk, cc, q, y) => Signpost(kk, cc, q, y));
            if (Spot(2.5f, wild, far, out var bp)) Each(bp, R(0, 360), (q, y) => Bottle(k, cols, q, y));
            // chess pieces standing on the checkerboard round the middle (off the lines to the bases)
            float th0 = Mathf.Atan2(bc.z, bc.x);
            float chessR = Mathf.Max(21f, PlazaR - 5f);
            float[] chessA = Cfg.FourWay ? new[] { -26f, 26f } : new[] { -50f, 50f };
            var chessAt = new List<Vector2>();
            for (int i = 0; i < chessA.Length; i++)
            {
                float a = th0 + chessA[i] * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a) * chessR, 0, Mathf.Sin(a) * chessR);
                int kind = i;
                Each(p, 0f, (q, y) => Chess(k, cols, q, y, kind, (Mathf.RoundToInt(y / (360f / Cfg.Copies)) & 1) == 0));
                if (Cfg.InFirstSector(p, 0f)) chessAt.Add(new Vector2(p.x, p.z));
            }

            // ---- the sides: lots more of it out toward the slab's edges (not the middle) - as busy as round the bases
            float band = Mathf.Max(14f, Side * 0.26f);
            bool SideSpot(float clear, out Vector3 p)
            {
                for (int tries = 0; tries < 140; tries++)
                {
                    p = new Vector3(R(-Side + 7f, Side - 7f), 0, R(-Side + 7f, -4f));
                    if (!Cfg.InFirstSector(p, 4f + clear) || !InSlab(p.x, p.z)) continue;
                    float ed = EdgeDist(p.x, p.z);
                    if (ed < 6.5f + clear || ed > 6.5f + clear + band) continue;
                    if (new Vector2(p.x, p.z).magnitude < PlazaR + 8f) continue;
                    if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 3f + clear && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 3f + clear) continue;
                    if (PathLat(p.x, p.z) < 6f + clear) continue;
                    bool hit = false;
                    foreach (var q in placed) if ((q.p - new Vector2(p.x, p.z)).magnitude < q.r + clear + 1.2f) { hit = true; break; }
                    if (hit) continue;
                    placed.Add((new Vector2(p.x, p.z), clear));
                    return true;
                }
                p = default;
                return false;
            }
            for (int i = 0; i < N(6); i++) if (SideSpot(4f, out var p)) { int sd = rng.Next(); float s = R(0.65f, 1.25f); EachMove(p, R(0, 360), 2, 1.3f, 0.045f, (kk, cc, q, y) => Mushroom(kk, cc, q, y, s, sd, true)); }
            for (int i = 0; i < N(9); i++) if (SideSpot(2f, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => MushroomPatch(k, q, sd)); }
            for (int i = 0; i < N(6); i++) if (SideSpot(2.5f, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 0, 0.2f, 0f, (kk, cc, q, y) => Topiary(kk, cc, q, y, sd)); }
            for (int i = 0; i < N(6); i++) if (SideSpot(2f, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => RoseBush(k, cols, q, y, sd)); }
            for (int i = 0; i < N(8); i++) if (SideSpot(1.8f, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 1, 1.1f, 4f, (kk, cc, q, y) => LoneCard(kk, cc, q, y, sd)); }
            for (int i = 0; i < N(3); i++) if (SideSpot(3.5f, out var p)) { int sd = rng.Next(); float s = R(0.7f, 1.1f); EachMove(p, R(0, 360), 0, 0.45f, 0f, (kk, cc, q, y) => Teacup(kk, cc, q, y, s, sd)); }
            for (int i = 0; i < N(3); i++) if (SideSpot(4f, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => Hedge(k, cols, q, y, sd)); }
            for (int i = 0; i < N(2); i++) if (SideSpot(6f, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 3, 4.2f, 0.16f, (kk, cc, q, y) => CardSoldiers(kk, cc, q, y, sd)); }
            for (int i = 0; i < N(2); i++) if (SideSpot(3f, out var p)) { float tilt = R(-18f, 8f), ws = R(0.7f, 1f); EachMove(p, R(0, 360), 1, 1.5f, 11f, (kk, cc, q, y) => Watch(kk, cc, q, y, ws, tilt)); }
            for (int i = 0; i < N(2); i++) if (SideSpot(2.5f, out var p)) { int kind = rng.Next(2); bool white = rng.NextDouble() < 0.5; Each(p, 0f, (q, y) => Chess(k, cols, q, y, kind, white)); }
            for (int i = 0; i < N(1); i++) if (SideSpot(3f, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 1, 0.9f, 7f, (kk, cc, q, y) => Teapot(kk, cc, q, y, 1f, sd)); }
            for (int i = 0; i < N(1); i++) if (SideSpot(3f, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => CardHouse(k, cols, q, y, sd)); }
            if (SideSpot(2.5f, out var bp2)) Each(bp2, R(0, 360), (q, y) => Bottle(k, cols, q, y));

            // ---- clusters of cover out on the open ground by the slab's edges and corners (hedges, giant cards, mushrooms,
            // teapots, stacks of books, chess pieces), loose enough to slip between
            bool EdgeOk(Vector3 p, float clear, float gapMul)
            {
                if (!Cfg.InFirstSector(p, 2f + clear) || !InSlab(p.x, p.z)) return false;
                float ed = EdgeDist(p.x, p.z);
                if (ed < 7f + clear || ed > 7f + clear + band + 6f) return false;
                if (new Vector2(p.x, p.z).magnitude < PlazaR + 6f) return false;
                if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 4f + clear && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 4f + clear) return false;
                if (PathLat(p.x, p.z) < 5.5f + clear) return false;
                foreach (var q in placed) if ((q.p - new Vector2(p.x, p.z)).magnitude < q.r + clear + 1.4f * gapMul) return false;
                return true;
            }
            int clusters = N(9);
            for (int ci = 0; ci < clusters; ci++)
            {
                Vector3 c = default;
                bool found = false;
                for (int tries = 0; tries < 120 && !found; tries++)
                {
                    c = new Vector3(R(-Side + 7f, Side - 7f), 0, R(-Side + 7f, -3f));
                    found = EdgeOk(c, 3f, 1.5f);
                }
                if (!found) continue;
                int items = 4 + rng.Next(3);
                for (int j = 0; j < items; j++)
                {
                    int kind = rng.Next(8);
                    float clear = kind == 0 ? 3.8f : kind == 4 ? 2.2f : kind == 6 ? 3.2f : 2f;
                    Vector3 p = default;
                    bool ok = false;
                    for (int tries = 0; tries < 30 && !ok; tries++)
                    {
                        float a = R(0f, 6.283f), d = j == 0 ? 0f : R(2.5f, 7f);
                        p = c + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                        ok = EdgeOk(p, clear, 1f);
                    }
                    if (!ok) continue;
                    placed.Add((new Vector2(p.x, p.z), clear));
                    int sd = rng.Next();
                    float yaw = R(0f, 360f), s = R(0.75f, 1.1f);
                    switch (kind)
                    {
                        case 0: Each(p, yaw, (q, y) => Hedge(k, cols, q, y, sd)); break;
                        case 1: case 2: EachMove(p, yaw, 1, 1.1f, 4f, (kk, cc, q, y) => LoneCard(kk, cc, q, y, sd)); break;
                        case 3: EachMove(p, yaw, 2, 1.3f, 0.045f, (kk, cc, q, y) => Mushroom(kk, cc, q, y, s, sd, true)); break;
                        case 4: EachMove(p, yaw, 1, 0.9f, 7f, (kk, cc, q, y) => Teapot(kk, cc, q, y, 0.9f, sd)); break;
                        case 5: Each(p, yaw, (q, y) => Books(k, cols, q, y, sd)); break;
                        case 6: { int ck = rng.Next(2); bool white = rng.NextDouble() < 0.5; Each(p, 0f, (q, y) => Chess(k, cols, q, y, ck, white)); break; }
                        default: Each(p, yaw, (q, y) => RoseBush(k, cols, q, y, sd)); break;
                    }
                }
            }

            // ---- cover along the way from each base to the middle (either side of the path): sneak up behind it
            var toBase = new Vector3(bc.x, 0f, bc.z).normalized;
            var across = new Vector3(-toBase.z, 0f, toBase.x);
            float baseIn = new Vector2(bc.x, bc.z).magnitude - Cfg.BaseHalf;
            float pathYaw = Mathf.Atan2(-toBase.z, toBase.x) * Mathf.Rad2Deg; // (a hedge's length along the path)
            bool RouteSpot(float clear, out Vector3 p)
            {
                for (int tries = 0; tries < 160; tries++)
                {
                    float along = R(PlazaR - 3f, baseIn - 4f), lat = R(5f + clear, 17f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                    p = toBase * along + across * lat;
                    if (!Cfg.InFirstSector(p, 2f + clear) || !InSlab(p.x, p.z) || EdgeDist(p.x, p.z) < 7f + clear) continue;
                    if (new Vector2(p.x, p.z).magnitude < 13f + clear) continue;
                    if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 3f + clear && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 3f + clear) continue;
                    if (PathLat(p.x, p.z) < 4.8f + clear) continue;
                    bool hit = false;
                    foreach (var q in placed) if ((q.p - new Vector2(p.x, p.z)).magnitude < q.r + clear + 1.0f) { hit = true; break; }
                    if (hit) continue;
                    placed.Add((new Vector2(p.x, p.z), clear));
                    return true;
                }
                p = default;
                return false;
            }
            for (int i = 0; i < N(7); i++) if (RouteSpot(3.6f, out var p)) { int sd = rng.Next(); Each(p, pathYaw + R(-22f, 22f), (q, y) => Hedge(k, cols, q, y, sd)); }
            for (int i = 0; i < N(6); i++) if (RouteSpot(1.8f, out var p)) { int sd = rng.Next(); EachMove(p, pathYaw + R(-30f, 30f), 1, 1.1f, 4f, (kk, cc, q, y) => LoneCard(kk, cc, q, y, sd)); }
            for (int i = 0; i < N(5); i++) if (RouteSpot(2f, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => RoseBush(k, cols, q, y, sd)); }
            for (int i = 0; i < N(3); i++) if (RouteSpot(3.5f, out var p)) { int sd = rng.Next(); float s = R(0.7f, 1.05f); EachMove(p, R(0, 360), 2, 1.3f, 0.045f, (kk, cc, q, y) => Mushroom(kk, cc, q, y, s, sd, true)); }
            for (int i = 0; i < N(3); i++) if (RouteSpot(2.5f, out var p)) { int sd = rng.Next(); EachMove(p, R(0, 360), 0, 0.2f, 0f, (kk, cc, q, y) => Topiary(kk, cc, q, y, sd)); }
            for (int i = 0; i < N(2); i++) if (RouteSpot(3f, out var p)) { int sd = rng.Next(); Each(p, pathYaw + R(-20f, 20f), (q, y) => CardHouse(k, cols, q, y, sd)); }
            for (int i = 0; i < N(2); i++) if (RouteSpot(2f, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => MushroomPatch(k, q, sd)); }
            // ---- a little more toward the middle (just a few things - the ball's own few metres, the cover round it and
            // the paths stay clear): its own random numbers, so everything else stays where it was
            foreach (var c in chessAt) placed.Add((c, 2f));
            var mrng = new System.Random(Cfg.MapSeed + 9393);
            float MR(float a, float b) => a + (float)mrng.NextDouble() * (b - a);
            bool MidSpot(float clear, out Vector3 p)
            {
                for (int tries = 0; tries < 160; tries++)
                {
                    float r = MR(15f + clear, Mathf.Max(15f + clear + 0.5f, PlazaR + 3f - clear * 0.5f)), a = MR(0f, 6.283f);
                    p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (!Cfg.InFirstSector(p, 3f + clear) || !InSlab(p.x, p.z)) continue;
                    if (PathLat(p.x, p.z) < 5f + clear) continue;
                    bool hit = false;
                    foreach (var q in placed) if ((q.p - new Vector2(p.x, p.z)).magnitude < q.r + clear + 1.5f) { hit = true; break; }
                    if (hit) continue;
                    placed.Add((new Vector2(p.x, p.z), clear));
                    return true;
                }
                p = default;
                return false;
            }
            if (MidSpot(3.4f, out var mm)) { int sd = mrng.Next(); float s = MR(0.6f, 0.8f); EachMove(mm, MR(0, 360), 2, 1.3f, 0.045f, (kk, cc, q, y) => Mushroom(kk, cc, q, y, s, sd, true)); }
            if (MidSpot(3f, out var mt)) { int sd = mrng.Next(); float s = MR(0.65f, 0.8f); EachMove(mt, MR(0, 360), 0, 0.45f, 0f, (kk, cc, q, y) => Teacup(kk, cc, q, y, s, sd)); }
            for (int i = 0; i < 2; i++) if (MidSpot(2f, out var mr)) { int sd = mrng.Next(); Each(mr, MR(0, 360), (q, y) => RoseBush(k, cols, q, y, sd)); }
            if (MidSpot(1.8f, out var mc)) { int sd = mrng.Next(); EachMove(mc, MR(0, 360), 1, 1.1f, 4f, (kk, cc, q, y) => LoneCard(kk, cc, q, y, sd)); }
            if (MidSpot(2.2f, out var mb)) { int sd = mrng.Next(); Each(mb, MR(0, 360), (q, y) => Books(k, cols, q, y, sd)); }
            ThemeKitB.Spawn(root, "wonderland props", k, null, true);

            BuildFloaters(root);
            BuildSky(root);
        }

        // ---------------------------------------------------------------- launch pads (giant springy mushroom tops)
        class Pad { public Transform Top, Spin; public Vector3 Pos, Target; public float Flight, Squash, Ph; }
        readonly List<Pad> m_Pads = new List<Pad>();

        /// <summary>Things that move by themselves: 0 spin round (deg/s), 1 rock to and fro about Axis (deg), 2 squash and
        /// stretch (a fraction), 3 march (bounce up, metres). Only the look moves - the colliders stay where they were built.</summary>
        class Mover { public Transform T; public Vector3 Pos, Axis; public Quaternion Rot; public byte Mode; public float Spd, Amp, Ph; }
        readonly List<Mover> m_Movers = new List<Mover>();
        Transform m_MoverRoot;

        /// <summary>A prop that moves: its look built round its own pivot at `at` (make(kit, null, origin, yaw)), its colliders
        /// built where it stands (make(throwaway, cols, at, yaw)), animated in ClientTick.</summary>
        void Moving(Transform cols, Vector3 at, float yaw, byte mode, float spd, float amp, Vector3 axis, System.Action<MeshKit, Transform, Vector3, float> make)
        {
            var go = new GameObject("moving prop");
            go.transform.SetParent(m_MoverRoot, false);
            go.transform.localPosition = at;
            var kit = new MeshKit();
            make(kit, null, Vector3.zero, yaw);
            ThemeKitB.Spawn(go.transform, "prop", kit, null, true);
            make(new MeshKit(), cols, at, yaw);
            float ph = Mathf.Repeat(at.x * 0.37f + at.z * 0.61f, 6.283f); // (from where it is: the same on every peer, no rng used)
            m_Movers.Add(new Mover { T = go.transform, Pos = at, Rot = Quaternion.identity, Mode = mode, Spd = spd, Amp = amp, Ph = ph, Axis = axis });
        }

        /// <summary>The launch pads, laid out in the first team's part and copied round: by the base (out to the edge of the
        /// middle), out on each flank (round into the next team's flank) and at the edge of the middle (back out to your own
        /// flank). Their pads and landing spots are kept clear of props.</summary>
        void BuildPads(Transform root, List<(Vector2 p, float r)> placed)
        {
            var bc = Cfg.BaseCenter[0];
            float th0 = Mathf.Atan2(bc.z, bc.x) * Mathf.Rad2Deg, span = 360f / Cfg.Copies;
            float baseIn = new Vector2(bc.x, bc.z).magnitude - Cfg.BaseHalf;
            float mid = (PlazaR + baseIn) * 0.5f;
            Vector3 P(float deg, float r) => new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad) * r, 0f, Mathf.Sin(deg * Mathf.Deg2Rad) * r);
            bool Ok(Vector3 p, float pad)
            {
                if (!InSlab(p.x, p.z) || EdgeDist(p.x, p.z) < 8f + pad) return false;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    var c = Cfg.BaseCenter[t];
                    if (Mathf.Abs(p.x - c.x) < Cfg.BaseHalf + 3f + pad && Mathf.Abs(p.z - c.z) < Cfg.BaseHalf + 3f + pad) return false;
                }
                return new Vector2(p.x, p.z).magnitude > 13f;
            }
            // (a spot that isn't fine is pulled in toward the middle until it is)
            Vector3 Fit(float deg, float r, float pad)
            {
                for (int i = 0; i < 10; i++) { var p = P(deg, r - i * 3f); if (Ok(p, pad)) return p; }
                return Vector3.zero;
            }
            var list = new List<(Vector3 from, Vector3 to, float flight)>();
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float rb = Mathf.Max(PlazaR + 6f, baseIn - 6f);
                float latDeg = Mathf.Asin(Mathf.Clamp01(10f / rb)) * Mathf.Rad2Deg;
                list.Add((Fit(th0 + sg * latDeg, rb, 0f), Fit(th0 + sg * span * 0.16f, PlazaR + 3f, 1f), 2.2f));
                list.Add((Fit(th0 + sg * span * 0.3f, mid, 0f), Fit(th0 + sg * span * 0.7f, mid + 4f, 1f), 2.6f));
                list.Add((Fit(th0 + sg * span * 0.2f, PlazaR + 1.5f, 0f), Fit(th0 + sg * span * 0.42f, mid + 2f, 1f), 2.2f));
            }
            var parent = new GameObject("launch pads").transform;
            parent.SetParent(root, false);
            foreach (var l in list)
            {
                if (l.from == Vector3.zero || l.to == Vector3.zero || (l.to - l.from).magnitude < 12f) continue;
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var a = Cfg.Copy(l.from, m); var b = Cfg.Copy(l.to, m);
                    if (Cfg.InFirstSector(a, 0f)) placed.Add((new Vector2(a.x, a.z), 3.2f));
                    if (Cfg.InFirstSector(b, 0f)) placed.Add((new Vector2(b.x, b.z), 5f));
                }
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var a = Cfg.Copy(l.from, m); var b = Cfg.Copy(l.to, m);
                    a.y = Height(a.x, a.z); b.y = Height(b.x, b.z);
                    m_Pads.Add(MakePad(parent, a, b, l.flight));
                    Target(parent, b);
                }
            }
        }

        static readonly Color k_PadCap = new Color(1f, 0.76f, 0.24f), k_PadSpot = new Color(1f, 0.93f, 0.62f);
        static Material s_PadGold, s_PadGlow;

        /// <summary>The pads' gold: shiny, a little metallic, and glowing from inside (it shines even in the shade).</summary>
        static Material PadGold()
        {
            if (s_PadGold != null) return s_PadGold;
            var m = Art.NewMat(k_PadCap);
            m.name = "launch pad gold";
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.45f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(1f, 0.66f, 0.16f) * 0.85f); }
            return s_PadGold = m;
        }

        /// <summary>Light added on top (no depth writes, both sides; vertex colours, uv0 twinkles them - RockGame/SpaceSky):
        /// the pads' soft glow and their sparkles.</summary>
        static Material PadGlow()
        {
            if (s_PadGlow != null) return s_PadGlow;
            var sh = Resources.Load<Shader>("SpaceArena/SpaceSky");
            if (sh == null || !sh.isSupported) return s_PadGlow = ThemeKitB.Glow(new Color(1f, 0.8f, 0.35f), 1.6f);
            var m = new Material(sh) { name = "launch pad glow" };
            m.SetColor("_Color", new Color(1.6f, 1.35f, 0.9f, 1f));
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.Transparent;
            return s_PadGlow = m;
        }

        /// <summary>A twinkle of light: a four-pointed star in two upright planes and one flat one (seen from anywhere).</summary>
        static void Sparkle(MeshBatch mb, Vector3 c, float s)
        {
            var axes = new[] { (Vector3.right, Vector3.up), (Vector3.forward, Vector3.up), (Vector3.right, Vector3.forward) };
            foreach (var (u0, v0) in axes)
            {
                Vector3 u = u0 * s, v = v0 * s, n = Vector3.Cross(u0, v0);
                Vector3 p0 = c + v * 1.3f, p1 = c + u * 0.22f, p2 = c - v * 1.3f, p3 = c - u * 0.22f;
                Vector3 q0 = c + u, q1 = c + v * 0.22f, q2 = c - u, q3 = c - v * 0.22f;
                mb.Tri(p0, p1, p2, n); mb.Tri(p0, p2, p3, n);
                mb.Tri(q0, q1, q2, n); mb.Tri(q0, q2, q3, n);
            }
        }

        /// <summary>A launch pad: a gold ring on springs with a big bouncy shining GOLD mushroom cap set in it (it squashes
        /// when it throws you), glittering spots and glowing arrows on it pointing where it throws you, a soft golden glow
        /// round it and sparkles twinkling and circling over it.</summary>
        Pad MakePad(Transform parent, Vector3 at, Vector3 target, float flight)
        {
            var go = new GameObject("launch pad");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var flat = target - at; flat.y = 0f;
            var look = Quaternion.LookRotation(flat.normalized);
            var k = new MeshKit(); var rim = new MeshKit();
            ThemeKitB.Cyl(k, Vector3.down * 0.15f, Vector3.up * 0.14f, 2.05f, 1.95f, 22, k_Gold, false, true);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                var b = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.86f;
                for (int c = 0; c < 3; c++)
                    ThemeKitB.Cyl(k, b + Vector3.up * (0.14f + c * 0.07f), b + Vector3.up * (0.18f + c * 0.07f), 0.12f, 0.12f, 6, new Color(0.75f, 0.75f, 0.8f), false, true);
            }
            // a glowing gold band round the ring's top edge
            for (int i = 0; i < 32; i++)
            {
                float a0 = i * Mathf.PI * 2f / 32f, a1 = (i + 1) * Mathf.PI * 2f / 32f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                ThemeKitB.Quad(rim, d0 * 1.97f + Vector3.up * 0.15f, d1 * 1.97f + Vector3.up * 0.15f, d1 * 2.12f + Vector3.up * 0.15f, d0 * 2.12f + Vector3.up * 0.15f, Color.white, Vector3.up);
            }
            ThemeKitB.Spawn(go.transform, "pad ring", k, null, true);
            ThemeKitB.Spawn(go.transform, "pad ring glow", rim, ThemeKitB.Glow(new Color(1f, 0.78f, 0.3f), 2.2f), false);
            // (low enough to step up onto)
            ThemeKitB.BoxCol(go.transform, Vector3.up * 0.06f, new Vector3(2.5f, 0.42f, 2.5f), Quaternion.identity);
            ThemeKitB.BoxCol(go.transform, Vector3.up * 0.06f, new Vector3(2.5f, 0.42f, 2.5f), Quaternion.Euler(0, 45f, 0));
            var top = new GameObject("pad top").transform;
            top.SetParent(go.transform, false);
            top.localPosition = Vector3.up * 0.14f;
            top.localRotation = look;
            var tk = new MeshKit(); var gk = new MeshKit(); var sk = new MeshKit();
            ThemeKitB.Ball(tk, Vector3.zero, new Vector3(1.7f, 0.2f, 1.7f), Quaternion.identity, Color.white, 1);
            // glittering spots on the cap
            for (int i = 1; i < 7; i++)
            {
                float a = i * 0.9f + 0.4f, d = 1.05f;
                var c = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                float h = 0.2f * Mathf.Sqrt(Mathf.Max(0f, 1f - (d * d) / (1.7f * 1.7f)));
                ThemeKitB.Ball(sk, c + Vector3.up * (h - 0.02f), new Vector3(0.24f, 0.05f, 0.24f), Quaternion.identity, Color.white, 0);
            }
            // arrows (chevrons) along its top, pointing the way it throws you
            for (int i = 0; i < 3; i++)
            {
                float z = -0.6f + i * 0.55f, y = 0.215f;
                ThemeKitB.Tri(gk, new Vector3(-0.4f, y, z - 0.25f), new Vector3(0f, y, z + 0.15f), new Vector3(0f, y, z - 0.05f), Color.white, Vector3.up);
                ThemeKitB.Tri(gk, new Vector3(-0.4f, y, z - 0.25f), new Vector3(0f, y, z - 0.05f), new Vector3(-0.4f, y, z - 0.45f), Color.white, Vector3.up);
                ThemeKitB.Tri(gk, new Vector3(0.4f, y, z - 0.25f), new Vector3(0f, y, z - 0.05f), new Vector3(0f, y, z + 0.15f), Color.white, Vector3.up);
                ThemeKitB.Tri(gk, new Vector3(0.4f, y, z - 0.25f), new Vector3(0.4f, y, z - 0.45f), new Vector3(0f, y, z - 0.05f), Color.white, Vector3.up);
            }
            ThemeKitB.Spawn(top, "pad cap", tk, PadGold(), true);
            ThemeKitB.Spawn(top, "pad glitter", sk, ThemeKitB.Glow(k_PadSpot, 2.4f), false);
            ThemeKitB.Spawn(top, "pad arrows", gk, ThemeKitB.Glow(new Color(1f, 0.97f, 0.86f), 2.8f), false);
            float ph = Mathf.Repeat(at.x * 0.53f + at.z * 0.29f, 6.283f);
            // the soft glow: a pool of golden light on the ground round it and a haze rising off it, fading out
            var halo = new MeshBatch { Colored = true };
            var warm = new Color(0.62f, 0.4f, 0.1f);
            const int hs = 28;
            for (int i = 0; i < hs; i++)
            {
                float a0 = i * Mathf.PI * 2f / hs, a1 = (i + 1) * Mathf.PI * 2f / hs;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                halo.TriC(d0 * 2.1f + Vector3.up * 0.06f, d1 * 2.1f + Vector3.up * 0.06f, d1 * 3.6f + Vector3.up * 0.06f, warm, warm, Color.black);
                halo.TriC(d0 * 2.1f + Vector3.up * 0.06f, d1 * 3.6f + Vector3.up * 0.06f, d0 * 3.6f + Vector3.up * 0.06f, warm, Color.black, Color.black);
                var hz = warm * 0.45f;
                halo.TriC(d0 * 2.05f + Vector3.up * 0.15f, d1 * 2.05f + Vector3.up * 0.15f, d1 * 2.35f + Vector3.up * 2.2f, hz, hz, Color.black);
                halo.TriC(d0 * 2.05f + Vector3.up * 0.15f, d1 * 2.35f + Vector3.up * 2.2f, d0 * 2.35f + Vector3.up * 2.2f, hz, Color.black, Color.black);
            }
            var hg = halo.Build(go.transform, "pad halo", PadGlow(), false);
            hg.AddComponent<OwnedMesh>().Mesh = hg.GetComponent<MeshFilter>().sharedMesh;
            // sparkles over it, each twinkling in its own time (the shader does it), the lot slowly circling
            var spin = new GameObject("pad sparkles").transform;
            spin.SetParent(go.transform, false);
            var sp = new MeshBatch { Colored = true };
            var srng = new System.Random(Mathf.RoundToInt(at.x * 31f + at.z * 17f) & 0xffff);
            float S(float lo, float hi) => lo + (float)srng.NextDouble() * (hi - lo);
            for (int i = 0; i < 16; i++)
            {
                float a = S(0f, 6.283f), d = S(0.5f, 2.5f);
                var c = new Vector3(Mathf.Cos(a) * d, S(0.35f, 2.8f), Mathf.Sin(a) * d);
                sp.Tint = Color.Lerp(new Color(1f, 0.85f, 0.45f), Color.white, S(0f, 0.6f));
                sp.Extra = new Vector4(S(0f, 6.283f), 0.95f, S(1.5f, 4.5f), 0f);
                Sparkle(sp, c, S(0.1f, 0.24f));
            }
            var sg = sp.Build(spin, "sparkles", PadGlow(), false);
            sg.AddComponent<OwnedMesh>().Mesh = sg.GetComponent<MeshFilter>().sharedMesh;
            return new Pad { Top = top, Spin = spin, Pos = at, Target = target, Flight = flight, Ph = ph };
        }

        /// <summary>Where a pad lands you: a big red and white target painted on the ground.</summary>
        static void Target(Transform parent, Vector3 at)
        {
            var k = new MeshKit();
            for (int ring = 0; ring < 4; ring++)
            {
                float r = 2.6f - ring * 0.62f, y = 0.04f + ring * 0.01f;
                var col = ring % 2 == 0 ? k_Red : k_White;
                for (int i = 0; i < 24; i++)
                {
                    float a0 = i * Mathf.PI * 2f / 24f, a1 = (i + 1) * Mathf.PI * 2f / 24f;
                    ThemeKitB.Tri(k, at + Vector3.up * y, at + new Vector3(Mathf.Cos(a0) * r, y, Mathf.Sin(a0) * r), at + new Vector3(Mathf.Cos(a1) * r, y, Mathf.Sin(a1) * r), col, Vector3.up);
                }
            }
            var g = ThemeKitB.Spawn(parent, "pad target", k, null, false);
            g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- the props themselves
        static Quaternion Yaw(float y) => Quaternion.Euler(0, y, 0);

        static void OctCol(Transform cols, Vector3 c, float r, float h)
        {
            ThemeKitB.BoxCol(cols, c, new Vector3(r * 1.8f, h, r * 1.8f), Quaternion.identity);
            ThemeKitB.BoxCol(cols, c, new Vector3(r * 1.8f, h, r * 1.8f), Quaternion.Euler(0, 45f, 0));
        }

        static readonly Color[][] k_Caps =
        {
            new[] { new Color(0.86f, 0.16f, 0.2f), new Color(0.98f, 0.96f, 0.92f) },
            new[] { new Color(0.58f, 0.3f, 0.82f), new Color(1f, 0.86f, 0.35f) },
            new[] { new Color(0.2f, 0.72f, 0.74f), new Color(1f, 0.6f, 0.8f) },
            new[] { new Color(1f, 0.55f, 0.2f), new Color(1f, 0.95f, 0.7f) },
            new[] { new Color(0.95f, 0.45f, 0.72f), new Color(1f, 1f, 1f) },
        };

        static void Mushroom(MeshKit k, Transform cols, Vector3 p, float yaw, float s, int seed, bool collide)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var pal = k_Caps[rng.Next(k_Caps.Length)];
            float h = R(4f, 7f) * s, capR = R(2.4f, 3.4f) * s, stem = capR * 0.24f;
            var lean = Quaternion.Euler(R(-8f, 8f), yaw, R(-8f, 8f));
            var top = p + lean * Vector3.up * h;
            var cream = new Color(0.96f, 0.92f, 0.8f);
            ThemeKitB.Cyl(k, p + Vector3.down * 0.2f, top, stem * 1.35f, stem, 9, cream, false, false);
            ThemeKitB.Cyl(k, top + Vector3.down * 0.05f, top + Vector3.up * 0.08f, capR * 0.9f, capR * 0.9f, 16, new Color(0.88f, 0.78f, 0.7f), true, true);
            var capC = top + Vector3.up * 0.05f;
            var radii = new Vector3(capR, capR * 0.62f, capR);
            ThemeKitB.Ball(k, capC + Vector3.up * capR * 0.12f, radii, lean, pal[0], 1);
            int spots = 6 + rng.Next(5);
            for (int i = 0; i < spots; i++)
            {
                float a = R(0f, 6.28f), el = R(0.35f, 1.3f);
                var d = new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
                var at = capC + Vector3.up * capR * 0.12f + lean * Vector3.Scale(d, radii) * 1.0f;
                float ss = R(0.25f, 0.45f) * s;
                ThemeKitB.Ball(k, at, new Vector3(ss, ss * 0.35f, ss), Quaternion.FromToRotation(Vector3.up, lean * d), pal[1], 0);
            }
            if (!collide) return;
            ThemeKitB.CapCol(cols, p, top, stem * 1.1f);
            ThemeKitB.BoxCol(cols, capC + Vector3.up * capR * 0.3f, new Vector3(capR * 1.45f, capR * 0.6f, capR * 1.45f), Yaw(yaw));
        }

        static void MushroomPatch(MeshKit k, Vector3 p, int seed)
        {
            var rng = new System.Random(seed);
            int n = 4 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                var q = p + new Vector3((float)rng.NextDouble() * 4f - 2f, 0, (float)rng.NextDouble() * 4f - 2f);
                q.y = p.y;
                Mushroom(k, null, q, (float)rng.NextDouble() * 360f, 0.15f + (float)rng.NextDouble() * 0.12f, rng.Next(), false);
            }
        }

        /// <summary>A playing card (2.4 x 3.4 m) at c, turned by rot: white face with its suit's pips, a red back.</summary>
        static void Card(MeshKit k, Vector3 c, Quaternion rot, int suit, int pips)
        {
            var face = new Color(0.97f, 0.96f, 0.92f);
            k.Box(c, new Vector3(2.4f, 3.4f, 0.1f), rot, face, MeshKit.All);
            k.Box(c + rot * new Vector3(0, 0, 0.055f), new Vector3(2.15f, 3.15f, 0.02f), rot, k_Red, MeshKit.All);
            k.Box(c + rot * new Vector3(0, 0, 0.068f), new Vector3(1.75f, 2.75f, 0.01f), rot, new Color(0.62f, 0.1f, 0.14f), MeshKit.All);
            bool red = suit < 2;
            var col = red ? k_Red : k_Black;
            void Pip(Vector3 local, float s)
            {
                var at = c + rot * (local + new Vector3(0, 0, -0.07f));
                switch (suit)
                {
                    case 0: // diamond
                        k.Box(at, new Vector3(s * 0.75f, s * 0.75f, 0.03f), rot * Quaternion.Euler(0, 0, 45f), col, MeshKit.All);
                        break;
                    case 1: // heart
                        ThemeKitB.Ball(k, at + rot * new Vector3(-s * 0.24f, s * 0.12f, 0), new Vector3(s * 0.3f, s * 0.3f, 0.04f), rot, col, 0);
                        ThemeKitB.Ball(k, at + rot * new Vector3(s * 0.24f, s * 0.12f, 0), new Vector3(s * 0.3f, s * 0.3f, 0.04f), rot, col, 0);
                        k.Box(at + rot * new Vector3(0, -s * 0.12f, 0), new Vector3(s * 0.5f, s * 0.5f, 0.03f), rot * Quaternion.Euler(0, 0, 45f), col, MeshKit.All);
                        break;
                    case 2: // spade
                        ThemeKitB.Ball(k, at + rot * new Vector3(-s * 0.24f, -s * 0.08f, 0), new Vector3(s * 0.3f, s * 0.3f, 0.04f), rot, col, 0);
                        ThemeKitB.Ball(k, at + rot * new Vector3(s * 0.24f, -s * 0.08f, 0), new Vector3(s * 0.3f, s * 0.3f, 0.04f), rot, col, 0);
                        k.Box(at + rot * new Vector3(0, s * 0.14f, 0), new Vector3(s * 0.5f, s * 0.5f, 0.03f), rot * Quaternion.Euler(0, 0, 45f), col, MeshKit.All);
                        k.Box(at + rot * new Vector3(0, -s * 0.42f, 0), new Vector3(s * 0.12f, s * 0.4f, 0.03f), rot, col, MeshKit.All);
                        break;
                    default: // club
                        ThemeKitB.Ball(k, at + rot * new Vector3(0, s * 0.22f, 0), new Vector3(s * 0.26f, s * 0.26f, 0.04f), rot, col, 0);
                        ThemeKitB.Ball(k, at + rot * new Vector3(-s * 0.25f, -s * 0.08f, 0), new Vector3(s * 0.26f, s * 0.26f, 0.04f), rot, col, 0);
                        ThemeKitB.Ball(k, at + rot * new Vector3(s * 0.25f, -s * 0.08f, 0), new Vector3(s * 0.26f, s * 0.26f, 0.04f), rot, col, 0);
                        k.Box(at + rot * new Vector3(0, -s * 0.38f, 0), new Vector3(s * 0.12f, s * 0.4f, 0.03f), rot, col, MeshKit.All);
                        break;
                }
            }
            if (pips <= 1) Pip(Vector3.zero, 1.1f);
            else
                for (int i = 0; i < pips; i++)
                {
                    float fy = pips == 1 ? 0f : Mathf.Lerp(1.05f, -1.05f, i / (float)(pips - 1));
                    float fx = pips >= 4 ? (i % 2 == 0 ? -0.45f : 0.45f) : 0f;
                    Pip(new Vector3(fx, fy, 0), 0.5f);
                }
            Pip(new Vector3(-0.92f, 1.4f, 0), 0.26f);
            Pip(new Vector3(0.92f, -1.4f, 0), 0.26f);
        }

        static void CardSoldiers(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            int n = 3 + rng.Next(3);
            var rot = Yaw(yaw);
            int suit = rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                var at = p + rot * new Vector3((i - (n - 1) * 0.5f) * 3.1f, 0, 0);
                var r2 = rot * Quaternion.Euler(0, (float)rng.NextDouble() * 10f - 5f, 0);
                var c = at + Vector3.up * 2.75f;
                Card(k, c, r2, suit, 2 + i % 5);
                // legs, arms and a spear
                var dark = new Color(0.12f, 0.1f, 0.14f);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    k.Box(at + r2 * new Vector3(sg * 0.45f, 0.55f, 0), new Vector3(0.14f, 1.1f, 0.14f), r2, dark, MeshKit.All);
                    k.Box(at + r2 * new Vector3(sg * 1.45f, 2.6f, -0.1f), new Vector3(0.6f, 0.12f, 0.12f), r2 * Quaternion.Euler(0, 0, sg * -30f), dark, MeshKit.All);
                }
                var spear = at + r2 * new Vector3(1.75f, 0, -0.25f);
                ThemeKitB.Cyl(k, spear, spear + Vector3.up * 5.2f, 0.06f, 0.06f, 5, new Color(0.5f, 0.35f, 0.2f), false, true);
                k.Box(spear + Vector3.up * 5.45f, new Vector3(0.4f, 0.4f, 0.08f), r2 * Quaternion.Euler(0, 0, 45f), new Color(0.85f, 0.82f, 0.75f), MeshKit.All);
                ThemeKitB.BoxCol(cols, c + Vector3.down * 0.6f, new Vector3(2.4f, 4.6f, 0.4f), r2);
            }
        }

        static void CardHouse(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            var rot = Yaw(yaw);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var r2 = rot * Quaternion.Euler(sg * 22f, 0, 0);
                var c = p + rot * new Vector3(0, 1.58f, -sg * 0.66f);
                Card(k, c, r2, rng.Next(4), 1 + rng.Next(6));
                ThemeKitB.BoxCol(cols, c, new Vector3(2.4f, 3.4f, 0.2f), r2);
            }
            var topC = p + Vector3.up * 3.25f;
            Card(k, topC, rot * Quaternion.Euler(90f, 0, 0), rng.Next(4), 1);
            ThemeKitB.BoxCol(cols, topC, new Vector3(2.4f, 0.2f, 3.4f), rot);
            // and a second floor, leaning
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var r2 = rot * Quaternion.Euler(sg * 22f, 0, 0);
                var c = p + rot * new Vector3(0, 3.3f + 1.58f, -sg * 0.66f);
                Card(k, c, r2, rng.Next(4), 1 + rng.Next(6));
                ThemeKitB.BoxCol(cols, c, new Vector3(2.4f, 3.4f, 0.2f), r2);
            }
        }

        /// <summary>A giant playing card stuck in the ground, leaning.</summary>
        static void LoneCard(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            var rot = Yaw(yaw) * Quaternion.Euler((float)rng.NextDouble() * 16f - 8f, 0, (float)rng.NextDouble() * 24f - 12f);
            var c = p + rot * Vector3.up * 1.45f;
            Card(k, c, rot, rng.Next(4), 1 + rng.Next(9));
            ThemeKitB.BoxCol(cols, c, new Vector3(2.4f, 3.4f, 0.2f), rot);
        }

        static readonly Color[] k_China = { new Color(0.98f, 0.7f, 0.82f), new Color(0.55f, 0.82f, 0.9f), new Color(0.98f, 0.9f, 0.5f), new Color(0.8f, 0.68f, 0.95f), new Color(0.97f, 0.96f, 0.94f) };
        static readonly Color k_Gold = new Color(0.95f, 0.75f, 0.25f);

        static void Teacup(MeshKit k, Transform cols, Vector3 p, float yaw, float s, int seed)
        {
            var rng = new System.Random(seed);
            var col = k_China[rng.Next(k_China.Length)];
            var rot = Yaw(yaw);
            Vector3 U(float y) => p + Vector3.up * y * s;
            ThemeKitB.Cyl(k, U(0f), U(0.22f), 3.0f * s, 3.3f * s, 18, Color.Lerp(col, Color.white, 0.4f));
            ThemeKitB.Cyl(k, U(0.22f), U(2.6f), 1.5f * s, 2.3f * s, 18, col, true, false);
            ThemeKitB.Cyl(k, U(0.5f), U(2.6f), 1.3f * s, 2.1f * s, 18, col * 0.85f, false, false, 0f, true);
            for (int i = 0; i < 18; i++)
            {
                float a0 = i * Mathf.PI * 2f / 18f, a1 = (i + 1) * Mathf.PI * 2f / 18f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                ThemeKitB.Quad(k, U(2.6f) + d0 * 2.3f * s, U(2.6f) + d1 * 2.3f * s, U(2.6f) + d1 * 2.1f * s, U(2.6f) + d0 * 2.1f * s, k_Gold, Vector3.up);
            }
            ThemeKitB.Cyl(k, U(1.9f), U(1.95f), 1.9f * s, 1.9f * s, 18, new Color(0.55f, 0.32f, 0.16f), false, true);
            ThemeKitB.Cyl(k, U(2.15f), U(2.4f), 2.17f * s, 2.27f * s, 18, k_Gold, false, false);
            // the handle: an arc out on one side
            var side = rot * Vector3.right;
            Vector3 prev = U(2.15f) + side * 2.15f * s;
            for (int i = 1; i <= 6; i++)
            {
                float a = i / 6f * Mathf.PI;
                var pt = U(1.45f + Mathf.Cos(a) * 0.75f) + side * (1.95f + Mathf.Sin(a) * 1.05f) * s;
                ThemeKitB.Cyl(k, prev, pt, 0.2f * s, 0.2f * s, 6, col, false, false);
                prev = pt;
            }
            OctCol(cols, U(1.4f), 2.1f * s, 2.6f * s);
        }

        static void Teapot(MeshKit k, Transform cols, Vector3 p, float yaw, float s, int seed)
        {
            var rng = new System.Random(seed);
            var col = k_China[rng.Next(k_China.Length)];
            var rot = Yaw(yaw);
            var side = rot * Vector3.right;
            var c = p + Vector3.up * 1.6f * s;
            ThemeKitB.Ball(k, c, new Vector3(2f, 1.6f, 2f) * s, rot, col, 1);
            ThemeKitB.Cyl(k, c + Vector3.up * 1.3f * s, c + Vector3.up * 1.7f * s, 0.95f * s, 0.75f * s, 12, k_Gold);
            ThemeKitB.Ball(k, c + Vector3.up * 1.95f * s, Vector3.one * 0.3f * s, rot, col, 0);
            ThemeKitB.Cyl(k, c + side * 1.5f * s + Vector3.down * 0.3f * s, c + side * 3.1f * s + Vector3.up * 1.4f * s, 0.42f * s, 0.18f * s, 8, col, false, true);
            Vector3 prev = c - side * 1.75f * s + Vector3.up * 0.8f * s;
            for (int i = 1; i <= 6; i++)
            {
                float a = i / 6f * Mathf.PI;
                var pt = c + Vector3.up * (Mathf.Cos(a) * 0.8f) * s - side * (1.75f + Mathf.Sin(a) * 1.0f) * s;
                ThemeKitB.Cyl(k, prev, pt, 0.2f * s, 0.2f * s, 6, col, false, false);
                prev = pt;
            }
            k.Box(c + Vector3.up * 0.1f * s, new Vector3(4.05f, 0.25f, 4.05f) * s * 0.98f, rot * Quaternion.Euler(0, 45f, 0), k_Gold, MeshKit.All);
            OctCol(cols, c, 1.8f * s, 3.2f * s);
        }

        static void TeaParty(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            var rot = Yaw(yaw);
            Vector3 L(float x, float y, float z) => p + rot * new Vector3(x, y, z);
            var wood = new Color(0.5f, 0.3f, 0.2f);
            k.Box(L(0, 1.1f, 0), new Vector3(9f, 0.2f, 2.6f), rot, wood, MeshKit.All);
            k.Box(L(0, 1.23f, 0), new Vector3(9.2f, 0.06f, 2.8f), rot, k_White, MeshKit.All);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                k.Box(L(0, 0.98f, sg * 1.42f), new Vector3(9.2f, 0.5f, 0.04f), rot, new Color(0.95f, 0.55f, 0.72f), MeshKit.All);
                for (int e = -1; e <= 1; e += 2) k.Box(L(e * 4.1f, 0.5f, sg * 1.0f), new Vector3(0.2f, 1f, 0.2f), rot, wood, MeshKit.All);
            }
            ThemeKitB.BoxCol(cols, L(0, 0.65f, 0), new Vector3(9f, 1.3f, 2.6f), rot);
            // cups, pots and a cake on it
            for (int i = 0; i < 7; i++)
            {
                var at = L(-3.8f + i * 1.25f, 1.26f, (float)rng.NextDouble() * 1.4f - 0.7f);
                if (i == 3) { ThemeKitB.Cyl(k, at, at + Vector3.up * 0.55f, 0.6f, 0.6f, 12, new Color(0.98f, 0.75f, 0.85f)); ThemeKitB.Ball(k, at + Vector3.up * 0.7f, Vector3.one * 0.14f, rot, k_Red, 0); continue; }
                if (i == 1 || i == 5) { Teapot(k, null, at, yaw + i * 40f, 0.22f, rng.Next()); continue; }
                var cc = k_China[rng.Next(k_China.Length)];
                ThemeKitB.Cyl(k, at, at + Vector3.up * 0.04f, 0.32f, 0.34f, 10, Color.white);
                ThemeKitB.Cyl(k, at + Vector3.up * 0.04f, at + Vector3.up * 0.36f, 0.15f, 0.24f, 10, cc);
            }
            // chairs, all different, and a big armchair at the end
            Color[] chair = { new Color(0.75f, 0.2f, 0.25f), new Color(0.25f, 0.6f, 0.65f), new Color(0.95f, 0.78f, 0.3f), new Color(0.55f, 0.35f, 0.75f) };
            for (int i = 0; i < 6; i++)
            {
                int sg = i < 3 ? -1 : 1;
                float x = -3f + (i % 3) * 3f;
                float s = 0.9f + (float)rng.NextDouble() * 0.5f;
                var cr = rot * Quaternion.Euler(0, sg > 0 ? 180f : 0f, 0);
                var at = L(x, 0, sg * 2.3f);
                var c = chair[rng.Next(chair.Length)];
                k.Box(at + Vector3.up * 0.6f * s, new Vector3(1.2f, 0.16f, 1.2f) * s, cr, c, MeshKit.All);
                k.Box(at + cr * new Vector3(0, 1.4f, -0.55f) * s, new Vector3(1.2f, 1.6f, 0.14f) * s, cr, c * 0.85f, MeshKit.All);
                for (int lx = -1; lx <= 1; lx += 2) for (int lz = -1; lz <= 1; lz += 2)
                    k.Box(at + cr * new Vector3(lx * 0.5f, 0.3f, lz * 0.5f) * s, new Vector3(0.1f, 0.6f, 0.1f) * s, cr, wood, MeshKit.All);
                ThemeKitB.BoxCol(cols, at + Vector3.up * 0.4f * s, new Vector3(1.2f, 0.8f, 1.2f) * s, cr);
            }
            var arm = L(6.2f, 0, 0);
            var ar = rot * Quaternion.Euler(0, -90f, 0);
            var red = new Color(0.7f, 0.12f, 0.2f);
            k.Box(arm + Vector3.up * 0.7f, new Vector3(2.4f, 1.0f, 2.2f), ar, red, MeshKit.All);
            k.Box(arm + ar * new Vector3(0, 2.2f, -1.0f), new Vector3(2.4f, 3.0f, 0.4f), ar, red, MeshKit.All);
            for (int sg = -1; sg <= 1; sg += 2) k.Box(arm + ar * new Vector3(sg * 1.1f, 1.5f, 0), new Vector3(0.4f, 0.8f, 2.2f), ar, red * 0.85f, MeshKit.All);
            ThemeKitB.Ball(k, arm + ar * new Vector3(0, 3.8f, -1.0f), new Vector3(0.5f, 0.5f, 0.3f), ar, k_Gold, 0);
            ThemeKitB.BoxCol(cols, arm + Vector3.up * 1.6f, new Vector3(2.4f, 3.2f, 2.2f), ar);
        }

        static void Watch(MeshKit k, Transform cols, Vector3 p, float yaw, float s, float tilt)
        {
            var rot = Yaw(yaw) * Quaternion.Euler(tilt, 0, 0);
            float R = 2.2f * s;
            var c = p + rot * new Vector3(0, R + 0.1f, 0);
            var fwd = rot * Vector3.forward;
            ThemeKitB.Cyl(k, c - fwd * 0.3f * s, c + fwd * 0.3f * s, R, R, 22, k_Gold, true, true);
            ThemeKitB.Cyl(k, c - fwd * 0.33f * s, c - fwd * 0.3f * s, R * 0.86f, R * 0.86f, 22, new Color(0.98f, 0.97f, 0.92f), true, false);
            for (int h = 0; h < 12; h++)
            {
                var q = rot * Quaternion.Euler(0, 0, h * 30f);
                k.Box(c - fwd * 0.35f * s + q * new Vector3(0, R * 0.72f, 0), new Vector3(0.12f, h % 3 == 0 ? 0.42f : 0.24f, 0.03f) * s, q, k_Black, MeshKit.All);
            }
            var hq = rot * Quaternion.Euler(0, 0, -50f); var mq = rot * Quaternion.Euler(0, 0, 160f);
            k.Box(c - fwd * 0.37f * s + hq * new Vector3(0, R * 0.25f, 0), new Vector3(0.14f, R * 0.5f, 0.03f), hq, k_Black, MeshKit.All);
            k.Box(c - fwd * 0.39f * s + mq * new Vector3(0, R * 0.35f, 0), new Vector3(0.1f, R * 0.7f, 0.03f), mq, k_Red, MeshKit.All);
            var crown = c + rot * new Vector3(0, R + 0.25f * s, 0);
            ThemeKitB.Cyl(k, crown - rot * Vector3.up * 0.3f * s, crown + rot * Vector3.up * 0.2f * s, 0.3f * s, 0.3f * s, 8, k_Gold);
            ThemeKitB.Cyl(k, crown + rot * new Vector3(0, 0.45f, 0) * s - fwd * 0.08f * s, crown + rot * new Vector3(0, 0.45f, 0) * s + fwd * 0.08f * s, 0.45f * s, 0.45f * s, 10, k_Gold);
            ThemeKitB.BoxCol(cols, c, new Vector3(R * 2f, R * 2f, 0.6f * s), rot);
        }

        static readonly Color k_Leaf = new Color(0.22f, 0.55f, 0.27f), k_Leaf2 = new Color(0.3f, 0.66f, 0.33f);

        static void Topiary(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            var rot = Yaw(yaw);
            var pot = new Color(0.78f, 0.42f, 0.28f);
            ThemeKitB.Cyl(k, p, p + Vector3.up * 0.9f, 0.6f, 0.8f, 10, pot, false, true);
            ThemeKitB.Cyl(k, p + Vector3.up * 0.85f, p + Vector3.up * 1.05f, 0.9f, 0.9f, 10, pot * 0.9f);
            int kind = rng.Next(3);
            float topY;
            if (kind == 0)
            {
                ThemeKitB.Cyl(k, p + Vector3.up * 1f, p + Vector3.up * 2.6f, 0.12f, 0.1f, 5, new Color(0.4f, 0.27f, 0.17f));
                ThemeKitB.Ball(k, p + Vector3.up * 3.4f, Vector3.one * 1.15f, rot, k_Leaf, 1);
                ThemeKitB.Ball(k, p + Vector3.up * 1.9f, Vector3.one * 0.55f, rot, k_Leaf2, 1);
                topY = 4.5f;
            }
            else if (kind == 1)
            {
                // a curly spiral
                float y = 1.6f;
                for (int i = 0; i < 6; i++)
                {
                    float r = Mathf.Lerp(1.15f, 0.35f, i / 5f);
                    var off = rot * Quaternion.Euler(0, i * 72f, 0) * new Vector3(0.38f, 0, 0);
                    ThemeKitB.Ball(k, p + Vector3.up * y + off, new Vector3(r, r * 0.8f, r), rot, i % 2 == 0 ? k_Leaf : k_Leaf2, 1);
                    y += r * 1.25f;
                }
                ThemeKitB.Ball(k, p + Vector3.up * (y + 0.1f), Vector3.one * 0.25f, rot, k_Red, 0);
                topY = y;
            }
            else
            {
                // a heart
                ThemeKitB.Cyl(k, p + Vector3.up * 1f, p + Vector3.up * 2.2f, 0.12f, 0.1f, 5, new Color(0.4f, 0.27f, 0.17f));
                var side = rot * Vector3.right;
                ThemeKitB.Ball(k, p + Vector3.up * 3.6f + side * 0.6f, Vector3.one * 0.85f, rot, k_Leaf, 1);
                ThemeKitB.Ball(k, p + Vector3.up * 3.6f - side * 0.6f, Vector3.one * 0.85f, rot, k_Leaf, 1);
                k.Box(p + Vector3.up * 3.0f, new Vector3(1.55f, 1.55f, 1.2f), rot * Quaternion.Euler(0, 0, 45f), k_Leaf2, MeshKit.All);
                topY = 4.5f;
            }
            ThemeKitB.CapCol(cols, p, p + Vector3.up * topY, 0.85f);
        }

        static void Roses(MeshKit k, Vector3 c, Vector3 radii, Quaternion rot, int n, System.Random rng)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * 6.28f, el = (float)rng.NextDouble() * 1.2f - 0.1f;
                var d = new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
                var at = c + rot * Vector3.Scale(d, radii) * 1.02f;
                bool white = rng.NextDouble() < 0.45;
                ThemeKitB.Ball(k, at, Vector3.one * 0.2f, rot, white ? new Color(0.98f, 0.97f, 0.95f) : k_Red, 0);
                // (some of the white ones are half painted red)
                if (white && rng.NextDouble() < 0.6) ThemeKitB.Ball(k, at + rot * d * 0.08f + Vector3.up * 0.06f, new Vector3(0.18f, 0.12f, 0.18f), rot, k_Red, 0);
            }
        }

        static void Hedge(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            var rot = Yaw(yaw);
            float len = 5f + (float)rng.NextDouble() * 4f;
            k.Box(p + Vector3.up * 1.1f, new Vector3(len, 2.2f, 1.4f), rot, k_Leaf, MeshKit.All);
            k.Box(p + Vector3.up * 2.25f, new Vector3(len - 0.3f, 0.12f, 1.25f), rot, k_Leaf2, MeshKit.All);
            for (int i = 0; i < Mathf.RoundToInt(len * 1.6f); i++)
            {
                float x = ((float)rng.NextDouble() - 0.5f) * (len - 0.4f), y = 0.4f + (float)rng.NextDouble() * 1.7f;
                float z = rng.NextDouble() < 0.5 ? -0.72f : 0.72f;
                bool white = rng.NextDouble() < 0.4;
                ThemeKitB.Ball(k, p + rot * new Vector3(x, y, z), Vector3.one * 0.2f, rot, white ? new Color(0.98f, 0.97f, 0.95f) : k_Red, 0);
            }
            ThemeKitB.BoxCol(cols, p + Vector3.up * 1.1f, new Vector3(len, 2.2f, 1.4f), rot);
        }

        static void RoseBush(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            var rot = Yaw(yaw);
            var radii = new Vector3(1.4f, 1.0f, 1.3f);
            var c = p + Vector3.up * 0.8f;
            ThemeKitB.Ball(k, c, radii, rot, k_Leaf, 1, 0.12f, seed & 255);
            Roses(k, c, radii, rot, 10, rng);
            ThemeKitB.BoxCol(cols, c, new Vector3(2.2f, 1.7f, 2.1f), rot);
            // a paint bucket left beside it
            var b = p + rot * new Vector3(1.9f, 0, 0.3f);
            ThemeKitB.Cyl(k, b, b + Vector3.up * 0.55f, 0.32f, 0.36f, 10, new Color(0.7f, 0.7f, 0.74f), false, false);
            ThemeKitB.Cyl(k, b + Vector3.up * 0.45f, b + Vector3.up * 0.5f, 0.33f, 0.33f, 10, k_Red, false, true);
        }

        static void Chess(MeshKit k, Transform cols, Vector3 p, float yaw, int kind, bool white)
        {
            var col = white ? new Color(0.95f, 0.93f, 0.88f) : new Color(0.16f, 0.14f, 0.19f);
            var col2 = white ? new Color(0.85f, 0.82f, 0.76f) : new Color(0.26f, 0.22f, 0.3f);
            var rot = Yaw(yaw);
            Vector3 U(float y) => p + Vector3.up * y;
            ThemeKitB.Cyl(k, U(0f), U(0.5f), 1.6f, 1.5f, 16, col2);
            ThemeKitB.Cyl(k, U(0.5f), U(0.9f), 1.35f, 1.1f, 16, col);
            if (kind == 0)
            {
                // the king
                ThemeKitB.Cyl(k, U(0.9f), U(4.4f), 1.0f, 0.55f, 14, col);
                ThemeKitB.Cyl(k, U(4.4f), U(4.7f), 1.0f, 1.0f, 14, col2);
                ThemeKitB.Cyl(k, U(4.7f), U(5.6f), 0.75f, 1.05f, 14, col);
                k.Box(U(6.2f), new Vector3(0.3f, 1.2f, 0.3f), rot, col2, MeshKit.All);
                k.Box(U(6.35f), new Vector3(0.9f, 0.3f, 0.3f), rot, col2, MeshKit.All);
                ThemeKitB.CapCol(cols, p, U(5.6f), 1.1f);
            }
            else
            {
                // the rook
                ThemeKitB.Cyl(k, U(0.9f), U(3.6f), 1.05f, 0.8f, 14, col);
                ThemeKitB.Cyl(k, U(3.6f), U(4.4f), 1.15f, 1.15f, 14, col2);
                for (int i = 0; i < 6; i++)
                {
                    var q = rot * Quaternion.Euler(0, i * 60f, 0);
                    k.Box(U(4.7f) + q * new Vector3(0, 0, 0.9f), new Vector3(0.55f, 0.6f, 0.4f), q, col2, MeshKit.All);
                }
                ThemeKitB.CapCol(cols, p, U(4.4f), 1.2f);
            }
        }

        /// <summary>A stack of three or four giant books, each turned a little (good cover, about head high).</summary>
        static void Books(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            Color[] covers = { new Color(0.62f, 0.12f, 0.18f), new Color(0.18f, 0.32f, 0.6f), new Color(0.22f, 0.45f, 0.28f), new Color(0.5f, 0.28f, 0.6f), new Color(0.75f, 0.5f, 0.2f) };
            var pages = new Color(0.96f, 0.93f, 0.84f);
            int n = 3 + rng.Next(2);
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = 3.4f - i * 0.25f + (float)rng.NextDouble() * 0.3f, d = 2.4f - i * 0.15f, h = 0.5f + (float)rng.NextDouble() * 0.25f;
                var rot = Yaw(yaw + ((float)rng.NextDouble() * 30f - 15f));
                var c = p + Vector3.up * (y + h * 0.5f);
                var col = covers[rng.Next(covers.Length)];
                k.Box(c, new Vector3(w, h, d), rot, col, MeshKit.All);
                k.Box(c + rot * new Vector3(0.12f, 0f, 0f), new Vector3(w - 0.12f, h - 0.12f, d + 0.02f), rot, pages, MeshKit.All);
                k.Box(c + rot * new Vector3(-w * 0.5f, 0f, 0f), new Vector3(0.1f, h + 0.02f, d + 0.04f), rot, col * 0.8f, MeshKit.All);
                k.Box(c + rot * new Vector3(-w * 0.5f - 0.03f, 0f, 0f), new Vector3(0.06f, h * 0.3f, d * 0.6f), rot, k_Gold, MeshKit.All);
                ThemeKitB.BoxCol(cols, c, new Vector3(w, h, d), rot);
                y += h;
            }
        }

        static void Signpost(MeshKit k, Transform cols, Vector3 p, float yaw)
        {
            var wood = new Color(0.45f, 0.3f, 0.2f);
            ThemeKitB.Cyl(k, p, p + Vector3.up * 5.5f, 0.16f, 0.13f, 6, wood);
            Color[] boards = { new Color(0.95f, 0.5f, 0.72f), new Color(0.3f, 0.72f, 0.75f), new Color(0.98f, 0.82f, 0.3f), new Color(0.6f, 0.4f, 0.85f), new Color(1f, 0.55f, 0.25f) };
            for (int i = 0; i < 5; i++)
            {
                var q = Yaw(yaw + i * 77f) * Quaternion.Euler(0, 0, (i % 2 == 0 ? 6f : -8f));
                var at = p + Vector3.up * (2.2f + i * 0.7f) + q * new Vector3(0.9f, 0, 0);
                k.Box(at, new Vector3(1.8f, 0.4f, 0.08f), q, boards[i], MeshKit.All);
                k.Box(at + q * new Vector3(0.98f, 0, 0), new Vector3(0.3f, 0.3f, 0.08f), q * Quaternion.Euler(0, 0, 45f), boards[i], MeshKit.All);
            }
            ThemeKitB.CapCol(cols, p, p + Vector3.up * 5.5f, 0.25f);
        }

        static void Bottle(MeshKit k, Transform cols, Vector3 p, float yaw)
        {
            var glass = new Color(0.45f, 0.72f, 0.92f);
            var rot = Yaw(yaw);
            ThemeKitB.Cyl(k, p, p + Vector3.up * 2.4f, 1.1f, 1.1f, 14, glass, false, true);
            ThemeKitB.Cyl(k, p + Vector3.up * 2.4f, p + Vector3.up * 3.2f, 1.1f, 0.38f, 14, glass, false, false);
            ThemeKitB.Cyl(k, p + Vector3.up * 3.2f, p + Vector3.up * 3.9f, 0.38f, 0.38f, 10, glass, false, false);
            ThemeKitB.Cyl(k, p + Vector3.up * 3.8f, p + Vector3.up * 4.35f, 0.42f, 0.36f, 10, new Color(0.7f, 0.52f, 0.32f));
            // the "DRINK ME" label (a pink tag on a string)
            k.Box(p + Vector3.up * 1.4f + rot * new Vector3(0, 0, -1.12f), new Vector3(1.4f, 0.8f, 0.04f), rot, new Color(0.98f, 0.8f, 0.88f), MeshKit.All);
            for (int i = 0; i < 4; i++) k.Box(p + Vector3.up * (1.62f - (i / 2) * 0.36f) + rot * new Vector3(-0.35f + (i % 2) * 0.7f, 0, -1.15f), new Vector3(0.5f, 0.1f, 0.02f), rot, k_Black, MeshKit.All);
            ThemeKitB.CapCol(cols, p, p + Vector3.up * 3.6f, 1.1f);
        }

        // ---------------------------------------------------------------- Wonderland trees
        static readonly Color[][] k_Crowns =
        {
            new[] { new Color(0.98f, 0.55f, 0.75f), new Color(0.9f, 0.3f, 0.6f) },
            new[] { new Color(0.4f, 0.85f, 0.8f), new Color(0.6f, 0.95f, 0.7f) },
            new[] { new Color(1f, 0.62f, 0.25f), new Color(1f, 0.85f, 0.35f) },
            new[] { new Color(0.72f, 0.55f, 0.95f), new Color(0.52f, 0.35f, 0.82f) },
        };
        static readonly Color[][] k_Barks =
        {
            new[] { new Color(0.45f, 0.28f, 0.55f), new Color(0.82f, 0.7f, 0.9f) },
            new[] { new Color(0.25f, 0.55f, 0.55f), new Color(0.94f, 0.94f, 0.9f) },
            new[] { new Color(0.55f, 0.32f, 0.2f), new Color(0.95f, 0.75f, 0.5f) },
        };

        /// <summary>The trunk's radius where the X goes (0-3 m up: a straight round striped column, every tree the same).</summary>
        const float TrunkR = 0.32f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 77);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var k = new MeshKit();
            var bark = k_Barks[rng.Next(k_Barks.Length)];
            var crown = k_Crowns[rng.Next(k_Crowns.Length)];
            // a round striped trunk: dead straight and TrunkR thick up to 3 m (where the X goes), then leaning a little
            // and narrowing (topsy-turvy)
            float lean = R(-9f, 9f);
            var lq = Quaternion.Euler(lean, R(0, 360), 0);
            const float Straight = 3f;
            float spin = R(0f, 6.28f);
            const int low = 5;
            for (int i = 0; i < low; i++)
            {
                float y0 = Straight * i / low, y1 = Straight * (i + 1) / low;
                ThemeKitB.Cyl(k, Vector3.up * y0, Vector3.up * y1, TrunkR, TrunkR, 16, bark[i % 2], i == 0, false, spin);
            }
            float upper = Mathf.Max(1.8f, h - Straight); // (the crown stays clear over the 3 m of bare trunk)
            const int high = 3;
            var foot = Vector3.up * Straight;
            for (int i = 0; i < high; i++)
            {
                float r0 = Mathf.Lerp(TrunkR, TrunkR * 0.7f, i / (float)high), r1 = Mathf.Lerp(TrunkR, TrunkR * 0.7f, (i + 1f) / high);
                Vector3 a = foot + lq * Vector3.up * (upper * i / high), b = foot + lq * Vector3.up * (upper * (i + 1) / high);
                ThemeKitB.Cyl(k, a, b + lq * Vector3.up * 0.02f, r0, r1, 16, bark[(low + i) % 2], false, i == high - 1, spin);
            }
            var top = foot + lq * Vector3.up * upper;
            float big = R(1.9f, 2.5f);
            ThemeKitB.Ball(k, top + Vector3.up * (big * 0.6f), Vector3.one * big, lq, crown[0], 1);
            for (int i = 0; i < 3; i++)
            {
                var off = Quaternion.Euler(0, i * 120f + R(-20f, 20f), 0) * new Vector3(big * 0.85f, R(-0.3f, 0.3f), 0);
                ThemeKitB.Ball(k, top + off, Vector3.one * R(0.9f, 1.3f), lq, crown[1], 1);
            }
            // a curl on top
            var c = top + Vector3.up * (big * 1.5f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 70f * Mathf.Deg2Rad;
                ThemeKitB.Ball(k, c + new Vector3(Mathf.Cos(a) * 0.45f, i * 0.32f, Mathf.Sin(a) * 0.45f), Vector3.one * Mathf.Lerp(0.42f, 0.18f, i / 5f), Quaternion.identity, crown[i % 2], 0);
            }
            ThemeKitB.Spawn(tr, "wonder tree", k, null, true);
            return true;
        }

        // ---------------------------------------------------------------- the Cheshire Cat (the horse here) and the tart bush
        public override string MountName => "Cheshire Cat";

        /// <summary>A big pink and purple striped cat with that grin and a long curling striped tail (a unicorn: a blue and
        /// white striped one with a gold horn).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var furA = unicorn ? new Color(0.55f, 0.75f, 0.98f) : new Color(0.92f, 0.45f, 0.72f);
            var furB = unicorn ? new Color(0.95f, 0.96f, 1f) : new Color(0.46f, 0.22f, 0.62f);
            var eyeC = new Color(0.85f, 0.95f, 0.25f);
            // the body, striped across
            ThemeKitB.MHit(Art.Box(t, furA, new Vector3(0, 1.12f, 0), new Vector3(0.6f, 0.56f, 1.5f)), ghost);
            for (int i = 0; i < 5; i++)
                Art.Box(t, furB, new Vector3(0, 1.12f, -0.6f + i * 0.3f), new Vector3(0.62f, 0.58f, 0.12f));
            // neck + a big round head (pivots to "graze")
            var neck = ThemeKitB.MPivot(t, "neck", new Vector3(0, 1.3f, 0.7f));
            ThemeKitB.MHit(Art.Box(neck, furB, new Vector3(0, 0.15f, 0.05f), new Vector3(0.4f, 0.4f, 0.32f), new Vector3(15, 0, 0)), ghost);
            var hd = ThemeKitB.MHead(neck, ghost, furA, new Vector3(0, 0.45f, 0.3f), new Vector3(0.62f, 0.52f, 0.5f));
            Art.Box(neck, furB, new Vector3(0, 0.66f, 0.3f), new Vector3(0.64f, 0.1f, 0.52f));
            Art.Box(neck, furB, new Vector3(0, 0.45f, 0.08f), new Vector3(0.64f, 0.54f, 0.08f));
            // the grin: a wide white crescent full of teeth, right across the face
            for (int i = -3; i <= 3; i++)
            {
                float x = i * 0.075f, y = 0.33f + i * i * 0.008f;
                Art.Box(neck, Color.white, new Vector3(x, y, 0.556f), new Vector3(0.08f, 0.09f, 0.02f), new Vector3(0, 0, i * -7f));
                Art.Box(neck, new Color(0.3f, 0.05f, 0.15f), new Vector3(x, y, 0.565f), new Vector3(0.012f, 0.08f, 0.01f));
            }
            // big yellow-green eyes with slit pupils
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Part(neck, Art.Sphere, eyeC, new Vector3(s * 0.15f, 0.54f, 0.54f), new Vector3(0.15f, 0.13f, 0.06f));
                Art.Box(neck, Color.black, new Vector3(s * 0.15f, 0.54f, 0.572f), new Vector3(0.025f, 0.11f, 0.01f));
                // pointed ears
                Art.Part(neck, Art.Cone, furA, new Vector3(s * 0.2f, 0.69f, 0.28f), new Vector3(0.22f, 0.26f, 0.12f), new Vector3(0, 0, s * -12f));
                Art.Part(neck, Art.Cone, new Color(1f, 0.75f, 0.85f), new Vector3(s * 0.2f, 0.7f, 0.33f), new Vector3(0.13f, 0.18f, 0.05f), new Vector3(0, 0, s * -12f));
                // whiskers
                for (int w = 0; w < 2; w++)
                    Art.Box(neck, Color.white, new Vector3(s * 0.36f, 0.4f + w * 0.05f, 0.5f), new Vector3(0.22f, 0.012f, 0.012f), new Vector3(0, s * 10f, s * (w == 0 ? 8f : -8f)));
            }
            Art.Part(neck, Art.Sphere, new Color(1f, 0.55f, 0.7f), new Vector3(0, 0.45f, 0.56f), new Vector3(0.08f, 0.06f, 0.05f));
            if (unicorn) ThemeKitB.MHorn(neck, new Vector3(0, 0.72f, 0.42f));
            head = neck;
            // a long striped tail curling up at the end
            var tl = ThemeKitB.MPivot(t, "tail", new Vector3(0, 1.3f, -0.76f));
            var at = Vector3.zero;
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.32f;
                var next = at + new Vector3(0, -0.06f + i * 0.04f, -0.16f + i * 0.03f) + new Vector3(0, Mathf.Sin(a) * 0.05f, 0);
                var seg = Art.Box(tl, i % 2 == 0 ? furA : furB, (at + next) * 0.5f, new Vector3(0.13f, 0.13f, 0.2f), new Vector3(-i * 18f, 0, 0));
                if (i < 3) ThemeKitB.MHit(seg, ghost);
                at = next;
            }
            tail = tl;
            // striped legs, soft paws
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.2f : 0.2f, z = i < 2 ? 0.55f : -0.55f;
                var leg = ThemeKitB.MPivot(t, "leg", new Vector3(x, 0.88f, z));
                ThemeKitB.MHit(Art.Box(leg, furA, new Vector3(0, -0.38f, 0), new Vector3(0.18f, 0.76f, 0.18f)), ghost);
                Art.Box(leg, furB, new Vector3(0, -0.22f, 0), new Vector3(0.19f, 0.1f, 0.19f));
                Art.Box(leg, furB, new Vector3(0, -0.52f, 0), new Vector3(0.19f, 0.1f, 0.19f));
                Art.Box(leg, new Color(1f, 0.8f, 0.88f), new Vector3(0, -0.83f, 0.04f), new Vector3(0.22f, 0.1f, 0.26f));
                legs?.Add(leg);
            }
            // a top-hat-red saddle
            saddle = ThemeKitB.MSaddle(t, ghost, new Color(0.62f, 0.14f, 0.2f), 0.6f, 1.44f);
            return true;
        }

        /// <summary>The game's own berry bush in Wonderland's colours: rose-bush green, strawberry-red berries.</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.3f, 0.56f, 0.3f), new Color(0.92f, 0.12f, 0.2f));
            return true;
        }

        // ---------------------------------------------------------------- things tumbling past in space
        void BuildFloaters(Transform root)
        {
            var rng = new System.Random(Cfg.MapSeed + 515);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var parent = new GameObject("space floaters").transform;
            parent.SetParent(root, false);
            int n = Mathf.RoundToInt(26 * Mathf.Clamp(Half / 100f, 0.7f, 1.6f));
            float s = Side;
            for (int i = 0; i < n; i++)
            {
                // somewhere out past an edge, or under the slab
                Vector3 pos;
                if (i % 4 == 3) pos = new Vector3(R(-s * 0.8f, s * 0.8f), R(-60f, -25f), R(-s * 0.8f, s * 0.8f));
                else
                {
                    float a = R(0f, Mathf.PI * 2f), d = s * R(1.12f, 1.5f) + 8f;
                    pos = new Vector3(Mathf.Cos(a) * d, R(-30f, 18f), Mathf.Sin(a) * d);
                }
                var go = new GameObject("floater");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                var k = new MeshKit();
                int kind = rng.Next(5);
                float sc = R(0.9f, 1.8f);
                switch (kind)
                {
                    case 0: case 1: Card(k, Vector3.zero, Quaternion.identity, rng.Next(4), 1 + rng.Next(9)); break;
                    case 2: Teacup(k, null, Vector3.down * 1.3f, 0f, 0.7f, rng.Next()); break;
                    case 3: Watch(k, null, Vector3.down * 2.2f, 0f, 1f, 0f); break;
                    default: Teapot(k, null, Vector3.down * 1.6f, 0f, 0.8f, rng.Next()); break;
                }
                go.transform.localScale = Vector3.one * sc;
                ThemeKitB.Spawn(go.transform, "floater", k, null, false);
                var rot = Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360));
                go.transform.localRotation = rot;
                m_Float.Add((go.transform, pos, rot, Random3(rng), R(6f, 18f) * (rng.NextDouble() < 0.5 ? -1f : 1f), R(0.6f, 2.2f), R(0f, 6.28f)));
            }
        }

        static Vector3 Random3(System.Random rng)
        {
            var v = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
            return v.sqrMagnitude < 1e-4f ? Vector3.up : v.normalized;
        }

        // ---------------------------------------------------------------- space: stars, constellations, a ringed planet, a moon
        void BuildSky(Transform root)
        {
            m_Sky = new GameObject("space sky").transform;
            m_Sky.SetParent(root, false);
            const float Shell = 1400f;
            var black = Art.Part(m_Sky, Art.Sphere, Color.black, Vector3.zero, Vector3.one * Shell * 2f, default, false, ThemeKitB.SkyMat(Color.black, CullMode.Front), "space");
            var br = black.GetComponent<MeshRenderer>();
            br.shadowCastingMode = ShadowCastingMode.Off; br.receiveShadows = false;
            var rng = new System.Random(4711);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Vector3 Dir(float minY)
            {
                Vector3 d;
                do d = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)); while (d.sqrMagnitude > 1f || d.sqrMagnitude < 0.05f || d.normalized.y < minY);
                return d.normalized;
            }
            var mb = new MeshBatch { Colored = true };
            Color[] tints = { new Color(1f, 1f, 1f), new Color(0.8f, 0.88f, 1f), new Color(1f, 0.93f, 0.75f), new Color(1f, 0.8f, 0.9f) };
            for (int i = 0; i < 1500; i++)
            {
                var d = Dir(-0.95f);
                float dist = R(1050f, 1330f), big = R(0f, 1f);
                float size = (big > 0.97f ? R(4.5f, 7f) : big > 0.8f ? R(2.6f, 4f) : R(1.3f, 2.4f)) * dist / 1000f;
                ThemeKitB.Star(mb, d * dist, size, tints[rng.Next(tints.Length)].linear * R(0.55f, 1f), R(0f, 1f));
            }
            // a faint milky band right round the sky
            var nb = new Vector3(0.3f, 0.85f, 0.42f).normalized;
            var e1 = Vector3.Cross(nb, Vector3.right).normalized; var e2 = Vector3.Cross(nb, e1);
            for (int i = 0; i < 1600; i++)
            {
                float th = R(0f, Mathf.PI * 2f), lat = (R(0f, 1f) + R(0f, 1f) + R(0f, 1f) - 1.5f) * 0.12f;
                var d = ((e1 * Mathf.Cos(th) + e2 * Mathf.Sin(th)) * Mathf.Cos(lat) + nb * Mathf.Sin(lat)).normalized;
                ThemeKitB.Star(mb, d * 1300f, R(0.8f, 1.8f), Color.Lerp(new Color(0.6f, 0.65f, 1f), new Color(1f, 0.8f, 0.95f), R(0f, 1f)).linear * R(0.25f, 0.6f), 0f);
            }
            // constellations: bright stars joined by faint lines
            for (int c = 0; c < 8; c++)
            {
                var centre = Dir(0.1f);
                var u = Vector3.Cross(centre, Vector3.up).normalized; var v = Vector3.Cross(centre, u);
                int n = 5 + rng.Next(4);
                var pts = new List<Vector3>();
                var at = Vector2.zero;
                for (int i = 0; i < n; i++)
                {
                    at += new Vector2(R(-0.09f, 0.09f), R(-0.09f, 0.09f));
                    pts.Add((centre + u * at.x + v * at.y).normalized * 1150f);
                }
                for (int i = 0; i < n; i++)
                {
                    ThemeKitB.Star(mb, pts[i], R(4.5f, 7f), new Color(0.92f, 0.95f, 1f).linear, R(0f, 1f));
                    if (i == 0) continue;
                    var a = pts[rng.Next(i)];
                    var b = pts[i];
                    var dir = b - a;
                    var side = Vector3.Cross(dir, -a).normalized * 0.7f;
                    mb.Tint = new Color(0.45f, 0.55f, 0.9f).linear * 0.55f; mb.Extra = Vector4.zero;
                    var o = -a.normalized;
                    mb.Tri(a + side, b + side, b - side, o); mb.Tri(a + side, b - side, a - side, o);
                }
            }
            var sun = ThemeKitB.Sun();
            var L = sun != null ? -sun.transform.forward : new Vector3(0.4f, 0.8f, -0.4f).normalized;
            // the ringed planet (big, banded, its night side dark), a little grey moon and a far blue planet
            Planet(mb, new Vector3(0.62f, 0.42f, 0.66f).normalized * 900f, 150f, 0, L, true);
            Planet(mb, new Vector3(-0.66f, 0.34f, 0.48f).normalized * 1000f, 24f, 1, L, false);
            Planet(mb, new Vector3(0.15f, 0.55f, -0.82f).normalized * 1150f, 34f, 2, L, false);
            mb.Build(m_Sky, "space stars", ThemeKitB.SkyMat(Color.white, CullMode.Off), false).GetComponent<MeshRenderer>().receiveShadows = false;
        }

        static Color PlanetCol(int kind, Vector3 n)
        {
            float wob = Mathf.PerlinNoise(n.x * 2.4f + 3f, n.z * 2.4f + 7f) - 0.5f;
            if (kind == 0)
            {
                float band = Mathf.Sin(n.y * 14f + wob * 2f) * 0.5f + 0.5f, band2 = Mathf.Sin(n.y * 5f + 1.2f + wob) * 0.5f + 0.5f;
                return Color.Lerp(Color.Lerp(new Color(0.95f, 0.88f, 0.7f), new Color(0.82f, 0.66f, 0.45f), band), new Color(0.68f, 0.5f, 0.35f), band2 * 0.4f);
            }
            if (kind == 1)
            {
                float mare = Mathf.PerlinNoise(n.x * 2.8f + 1f, n.y * 2.8f + n.z * 1.5f);
                return Color.Lerp(new Color(0.85f, 0.84f, 0.86f), new Color(0.55f, 0.55f, 0.6f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.66f, mare)));
            }
            float land = Mathf.PerlinNoise(n.x * 1.7f + n.y, n.z * 1.7f - n.y);
            return land > 0.55f ? new Color(0.4f, 0.7f, 0.6f) : new Color(0.25f, 0.45f, 0.85f);
        }

        /// <summary>A smooth sphere (and ring) into the sky batch, its lit and night sides baked into the colours.</summary>
        static void Planet(MeshBatch mb, Vector3 c, float r, int kind, Vector3 L, bool ring)
        {
            const int lat = 22, lon = 36;
            var tilt = Quaternion.Euler(18f, 30f, -14f);
            Vector3 P(int i, int j)
            {
                float a = Mathf.PI * i / lat - Mathf.PI * 0.5f, b = Mathf.PI * 2f * j / lon;
                return new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b));
            }
            Color Col(Vector3 n)
            {
                var w = tilt * n;
                float lit = 0.08f + 0.92f * Mathf.Clamp01(Vector3.Dot(w, L) * 1.1f + 0.1f);
                var col = PlanetCol(kind, n) * lit; col.a = 1f;
                return col.linear;
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    Vector3 a = P(i, j), b = P(i + 1, j), d = P(i, j + 1), e = P(i + 1, j + 1);
                    mb.TriC(c + tilt * a * r, c + tilt * b * r, c + tilt * e * r, Col(a), Col(b), Col(e));
                    mb.TriC(c + tilt * a * r, c + tilt * e * r, c + tilt * d * r, Col(a), Col(e), Col(d));
                }
            if (!ring) return;
            float[] radii = { 1.35f, 1.62f, 1.78f, 2.05f, 2.3f };
            Color[] cols = { new Color(0.78f, 0.7f, 0.56f), new Color(0.92f, 0.85f, 0.7f), new Color(0.62f, 0.54f, 0.44f), new Color(0.86f, 0.8f, 0.66f) };
            const int ringSegs = 90;
            for (int bnd = 0; bnd < cols.Length; bnd++)
                for (int i = 0; i < ringSegs; i++)
                {
                    float a0 = Mathf.PI * 2f * i / ringSegs, a1 = Mathf.PI * 2f * (i + 1) / ringSegs;
                    Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    // (the part of the ring behind the planet, in its shadow, is darker)
                    float sh = Vector3.Dot(tilt * p0, L) < -0.2f ? 0.35f : 1f;
                    var col = (cols[bnd] * sh); col.a = 1f; col = col.linear;
                    Vector3 A = c + tilt * p0 * radii[bnd] * r, B = c + tilt * p1 * radii[bnd] * r, C = c + tilt * p1 * radii[bnd + 1] * r, D = c + tilt * p0 * radii[bnd + 1] * r;
                    mb.TriC(A, B, C, col, col, col); mb.TriC(A, C, D, col, col, col);
                }
        }

        // ---------------------------------------------------------------- every frame
        public override void ClientTick()
        {
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            foreach (var f in m_Float)
            {
                if (f.t == null) continue;
                f.t.localRotation = Quaternion.AngleAxis(t * f.spin, f.axis) * f.rot;
                f.t.localPosition = f.pos + Vector3.up * Mathf.Sin(t * 0.4f + f.ph) * f.bob;
            }
            // the props that move by themselves
            foreach (var m in m_Movers)
            {
                if (m.T == null) continue;
                float w = t * m.Spd + m.Ph;
                switch (m.Mode)
                {
                    case 0: m.T.localRotation = Quaternion.Euler(0f, w * Mathf.Rad2Deg, 0f) * Quaternion.AngleAxis(Mathf.Sin(w * 0.37f) * 2.5f, Vector3.right); break;
                    case 1: m.T.localRotation = Quaternion.AngleAxis(Mathf.Sin(w) * m.Amp, m.Axis); break;
                    case 2:
                    {
                        float s = Mathf.Sin(w) * m.Amp;
                        m.T.localScale = new Vector3(1f - s * 0.6f, 1f + s, 1f - s * 0.6f);
                        break;
                    }
                    default:
                        m.T.localPosition = m.Pos + Vector3.up * (Mathf.Abs(Mathf.Sin(w)) * m.Amp);
                        m.T.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(w) * 3f);
                        break;
                }
            }
            // the launch pads: step on one and it throws you (you only - every peer throws its own player)
            var me = PlayerController.Local;
            if (me != null) me.LaunchTick(); // (steering in the air after a pad, and stopping dead on landing)
            if (me != null && me.SinceLaunch > 0.8f && me.VelY < 1f)
            {
                var p = me.transform.position;
                foreach (var pad in m_Pads)
                {
                    var d = p - pad.Pos;
                    if (d.y < -0.5f || d.y > 1.0f) continue;
                    d.y = 0f;
                    if (d.sqrMagnitude > 1.9f * 1.9f) continue;
                    me.Launch(ThemeKitB.LaunchVelocity(p, pad.Target, pad.Flight));
                    pad.Squash = 1f;
                    Sfx.Play(Sfx.Twang, pad.Pos, 1f, 0.12f, 70f);
                    Fx.Shake(0.15f);
                    break;
                }
            }
            foreach (var pad in m_Pads)
            {
                if (pad.Top == null) continue;
                // (someone else bouncing off it: it squashes for them too)
                if (pad.Squash < 0.2f)
                    foreach (var pn in PlayerNet.All)
                    {
                        if (pn == null || pn.IsOwner) continue;
                        var d = pn.transform.position - pad.Pos;
                        if (d.y > 0.6f && d.y < 2.5f && new Vector2(d.x, d.z).sqrMagnitude < 4f) { pad.Squash = 0.8f; break; }
                    }
                pad.Squash = Mathf.Max(0f, pad.Squash - dt * 2.2f);
                float sq = pad.Squash * pad.Squash, idle = Mathf.Sin(t * 2.2f + pad.Ph) * 0.06f;
                float spring = Mathf.Sin((1f - pad.Squash) * 18f) * pad.Squash;
                pad.Top.localScale = new Vector3(1f + sq * 0.12f - idle * 0.3f, 1f + idle - spring * 0.8f, 1f + sq * 0.12f - idle * 0.3f);
                pad.Top.localPosition = Vector3.up * (0.14f + spring * 0.25f);
                // (its sparkles circle slowly, bob, and fling round when it throws someone)
                if (pad.Spin != null)
                {
                    pad.Spin.localRotation = Quaternion.Euler(0f, t * 16f + pad.Ph * 57f + sq * 70f, 0f);
                    pad.Spin.localPosition = Vector3.up * (Mathf.Sin(t * 0.9f + pad.Ph) * 0.12f + sq * 0.4f);
                }
            }
            var cam = Camera.main;
            if (m_Sky != null)
            {
                bool hide = ThemeKitB.SkyHidden(cam);
                if (m_Sky.gameObject.activeSelf == hide) m_Sky.gameObject.SetActive(!hide);
                if (!hide)
                {
                    m_Sky.position = cam.transform.position;
                    m_Sky.rotation = Quaternion.Euler(0f, t * 0.15f, 0f);
                }
            }
            ThemeKitB.Keep(cam);
        }

        public override void ApplySky()
        {
            ThemeKitB.Begin();
            ThemeKitB.Skybox(new Color(0.05f, 0.05f, 0.12f), new Color(0.02f, 0.02f, 0.04f), 0.15f, 0.2f);
            ThemeKitB.NoFog();
            ThemeKitB.Lighting(new Color(1f, 0.97f, 0.95f), new Color(0.56f, 0.5f, 0.82f), new Color(0.46f, 0.42f, 0.64f), new Color(0.3f, 0.26f, 0.46f));
        }

        public override void Cleanup() => ThemeKitB.End();
    }
}
