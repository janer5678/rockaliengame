using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// MARS: red dust under a butterscotch sky (two little moons), dunes, craters, rock spires and boulders, an old rover
    /// and a habitat dome in each team's part, flat-topped mesas on the horizon - and a volcano in the middle: the ball
    /// drops into its crater (the flat ball zone). Its rim is too steep to climb except at the low passes cut through it
    /// (a walkable path over the rim on each side), and a TUNNEL SYSTEM runs under it: a square ring of tunnels under the
    /// rim (4.4 m below ground, its own floor, walls and roof - real colliders), a tunnel from it straight up into the
    /// crater through a portal in the crater wall for each team, and two long tunnels out from its corners to covered
    /// ramps up to the surface near each base (roomy: 3.8 m high, 4.8 m wide, nothing hanging low; a climbing tunnel stays
    /// level until it's out from under the one it leaves). Orange lamps, steel ribs and cyan guide strips inside.
    /// The crater floor has cover round the ball (BuildCentre); the horses are six-legged Dust Striders. Glowing lava seams
    /// round the crater floor and down the slopes (just to look at), smoke rising from vents on the rim.
    /// </summary>
    public class MarsMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Mars;
        public override string Label => "Mars";
        public override string Blurb => "Red dust and craters round a volcano - the ball drops into its crater. Get in over the passes in the rim, or through the tunnels under it.";
        public override bool Mountains => false;
        public override float MaxSpotHeight => 3f;

        static float Half => Cfg.MapHalf;
        // (the tunnels are roomy: 3.8 m floor to roof, 4.8 m wall to wall - you used to catch on the roof where a tunnel
        // climbed out from under another one)
        const float CraterR = 17f, Df = 4.4f, Inner = 3.8f, RoofT = 0.5f, HalfW = 2.4f, OuterW = 4.4f, RampLen = 12f, PassFrac = 0.15f;
        /// <summary>A climbing tunnel stays level this far out of the tunnel it leaves (so you're clear of that one's roof),
        /// and a ramp this far.</summary>
        const float ClimbFlat = OuterW + 0.4f, RampFlat = 2.8f;
        static float RimPeakR => CraterR + 3.5f;
        static float RimH => Cfg.SmallMap ? 6.5f : Half >= 150f ? 12f : 10f;
        static float BaseIn => Cfg.BaseCenter[0].magnitude - Cfg.BaseHalf;
        static float RimOut => Mathf.Clamp(Mathf.Min(Half * 0.42f, BaseIn - 4f), RimPeakR + 5f, 52f);
        /// <summary>The tunnel ring's half size (even: the tunnels line up with the ground's 2 m grid).</summary>
        static float Rq => Cfg.SmallMap ? 24f : Half >= 150f ? 32f : 30f;
        /// <summary>How far from the middle the tunnel up into the crater comes out (in the crater, through its wall).</summary>
        static float CraterExit => Mathf.Min(CraterR - 1f, Rq - 12f);

        static readonly Color k_Rock = new Color(0.52f, 0.27f, 0.18f), k_Rock2 = new Color(0.45f, 0.23f, 0.16f), k_Floor = new Color(0.33f, 0.24f, 0.21f);
        static readonly Color k_Metal = new Color(0.27f, 0.26f, 0.28f), k_Lava = new Color(1f, 0.42f, 0.1f), k_Lamp = new Color(1f, 0.62f, 0.28f), k_Guide = new Color(0.3f, 0.85f, 1f);

        // ================================================================ the layout (tunnels, craters, passes)
        /// <summary>A straight tunnel from A to B. Kind 0: level, 4.4 m down; 1: up into the crater (open at B); 2: a ramp up to the
        /// surface (open at B, roofed only while it's deep).</summary>
        class Seg
        {
            public Vector2 A, B, D, S;
            public float L;
            public int Kind;
            public bool OpenA, OpenB;
            public float Floor(float s)
            {
                if (Kind == 0) return -Df;
                if (Kind == 1) return -Df * (1f - Mathf.Clamp01((s - ClimbFlat) / Mathf.Max(0.1f, L - 1.2f - ClimbFlat)));
                return -Df * (1f - Mathf.Clamp01((s - RampFlat) / Mathf.Max(0.1f, L - RampFlat)));
            }
            public bool Roofed(float s) => Kind != 2 || Floor(s) < -1.8f;
            public void Local(float x, float z, out float s, out float lat)
            {
                float px = x - A.x, pz = z - A.y;
                s = px * D.x + pz * D.y;
                lat = px * S.x + pz * S.y;
            }
            public Vector3 P(float s, float lat, float y) { var q = A + D * s + S * lat; return new Vector3(q.x, y, q.y); }
            public bool Inside(Vector2 q)
            {
                Local(q.x, q.y, out float s, out float lat);
                return Mathf.Abs(lat) < HalfW - 0.05f && s > -HalfW && s < L + HalfW;
            }
        }

        static readonly List<Seg> s_Segs = new List<Seg>();
        static readonly List<(Vector2 c, float r, float d)> s_Craters = new List<(Vector2, float, float)>();
        static readonly List<float> s_Passes = new List<float>();
        static int s_Key = int.MinValue;

        static void Layout()
        {
            int key = Cfg.MapSeed * 7919 + (int)Cfg.Size * 131 + Cfg.TeamCount * 17 + (int)Cfg.BaseCenter[0].z;
            if (key == s_Key) return;
            s_Key = key;
            s_Segs.Clear(); s_Craters.Clear(); s_Passes.Clear();
            float R = Rq;
            float zEnd = (BaseIn + RimOut) * 0.5f + 4f;
            float l1 = Mathf.Max(2f, Mathf.Round((zEnd - R - RampLen) / 2f) * 2f);
            var first = new List<(Vector2 a, Vector2 b, int kind)>
            {
                (new Vector2(-R, -R), new Vector2(R, -R), 0),
                (new Vector2(0, -R), new Vector2(0, -CraterExit), 1),
            };
            if (!Cfg.FourWay) { first.Add((new Vector2(R, -R), new Vector2(R, 0), 0)); first.Add((new Vector2(-R, -R), new Vector2(-R, 0), 0)); }
            for (int sx = -1; sx <= 1; sx += 2)
            {
                first.Add((new Vector2(sx * R, -R), new Vector2(sx * R, -R - l1), 0));
                first.Add((new Vector2(sx * R, -R - l1), new Vector2(sx * R, -R - l1 - RampLen), 2));
            }
            for (int m = 0; m < Cfg.Copies; m++)
                foreach (var f in first)
                {
                    var a3 = Cfg.Copy(new Vector3(f.a.x, 0, f.a.y), m); var b3 = Cfg.Copy(new Vector3(f.b.x, 0, f.b.y), m);
                    var g = new Seg { A = new Vector2(Mathf.Round(a3.x), Mathf.Round(a3.z)), B = new Vector2(Mathf.Round(b3.x), Mathf.Round(b3.z)), Kind = f.kind };
                    var d = g.B - g.A;
                    g.L = d.magnitude;
                    g.D = d / g.L;
                    g.S = new Vector2(g.D.y, -g.D.x);
                    g.OpenB = f.kind != 0;
                    s_Segs.Add(g);
                }
            // the passes over the rim
            float th0 = Mathf.Atan2(Cfg.BaseCenter[0].z, Cfg.BaseCenter[0].x) * Mathf.Rad2Deg, span = 360f / Cfg.Copies;
            foreach (float off in Cfg.FourWay ? new[] { -25f, 25f } : new[] { -50f, 50f })
                for (int m = 0; m < Cfg.Copies; m++) s_Passes.Add(th0 + off + m * span);
            // craters out in the wild (clear of the tunnels and the bases)
            var rng = new System.Random(Cfg.MapSeed * 13 + 5);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float sc = Mathf.Max(0.7f, Half / 100f);
            int want = Mathf.Clamp(Mathf.RoundToInt(5f * sc * sc), 2, 14);
            var bc = Cfg.BaseCenter[0];
            for (int tries = 0, made = 0; tries < want * 40 && made < want; tries++)
            {
                var p = new Vector2(Rn(-Half + 16f, Half - 16f), Rn(-Half + 16f, -8f));
                if (!Cfg.InFirstSector(new Vector3(p.x, 0, p.y), 8f)) continue;
                float cr = Rn(5f, 12f) * Mathf.Min(1.3f, sc), depth = cr * Rn(0.12f, 0.2f);
                if (p.magnitude < RimOut + cr + 4f) continue;
                if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + cr + 6f && Mathf.Abs(p.y - bc.z) < Cfg.BaseHalf + cr + 6f) continue;
                if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > Half - 16f - cr) continue;
                bool bad = false;
                foreach (var g in s_Segs)
                {
                    g.Local(p.x, p.y, out float s, out float lat);
                    if (s > -cr * 1.5f - 6f && s < g.L + cr * 1.5f + 6f && Mathf.Abs(lat) < cr * 1.5f + 7f) { bad = true; break; }
                }
                foreach (var c in s_Craters) if ((c.c - p).magnitude < c.r + cr + 4f) bad = true;
                if (bad) continue;
                made++;
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var q = Cfg.Copy(new Vector3(p.x, 0, p.y), m);
                    s_Craters.Add((new Vector2(q.x, q.z), cr, depth));
                }
            }
        }

        // ================================================================ height
        static float BaseMask(float x, float z)
        {
            float dBase = float.MaxValue;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var c = Cfg.BaseCenter[t];
                float dx = Mathf.Max(0, Mathf.Abs(x - c.x) - Cfg.BaseHalf), dz = Mathf.Max(0, Mathf.Abs(z - c.z) - Cfg.BaseHalf);
                dBase = Mathf.Min(dBase, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return ThemeMaps.SmoothStepP(2f, 14f, dBase);
        }

        /// <summary>The volcano's cone: 0 in the crater, a steep inner wall up to the rim, a long slope down outside; low at the passes.</summary>
        static float Volcano(float x, float z, float r)
        {
            if (r <= CraterR) return 0f;
            float inner = ThemeMaps.SmoothStepP(CraterR, RimPeakR, r);
            float outer = 1f - ThemeMaps.SmoothStepP(RimPeakR, RimOut, r);
            float prof = Mathf.Min(inner, Mathf.Pow(outer, 1.3f));
            if (prof <= 0f) return 0f;
            float ang = Mathf.Atan2(z, x) * Mathf.Rad2Deg, pass = 1f;
            foreach (float pa in s_Passes)
            {
                float da = Mathf.Abs(Mathf.DeltaAngle(ang, pa));
                if (da > 80f) continue;
                float lat = r * Mathf.Sin(da * Mathf.Deg2Rad);
                pass = Mathf.Min(pass, Mathf.Lerp(PassFrac, 1f, ThemeMaps.SmoothStepP(3.5f, 9f, lat)));
            }
            float rough = 1f + (ThemeMaps.SymNoiseP(x, z, 0.18f, ThemeMaps.SeedP + 5f) - 0.5f) * 0.35f;
            return RimH * prof * pass * rough;
        }

        public override float Height(float x, float z)
        {
            Layout();
            float r = Mathf.Sqrt(x * x + z * z);
            float h = (ThemeMaps.SymNoiseP(x, z, 0.022f, ThemeMaps.SeedP) - 0.5f) * 3.4f + (ThemeMaps.SymNoiseP(x, z, 0.085f, ThemeMaps.SeedP + 20f) - 0.5f) * 0.9f;
            foreach (var c in s_Craters)
            {
                float dx = x - c.c.x, dz = z - c.c.y;
                float d2 = (dx * dx + dz * dz) / (c.r * c.r);
                if (d2 > 3f) continue;
                float d = Mathf.Sqrt(d2);
                float rim = 0.35f * Mathf.Exp(-((d - 1f) / 0.25f) * ((d - 1f) / 0.25f));
                h += (d < 1f ? -(1f - d2) + rim : rim) * c.d;
            }
            float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (Half - 16f);
            if (e > 0) h += e * 1.2f;
            h *= ThemeMaps.SmoothStepP(CraterR, CraterR + 6f, r) * ThemeMaps.MaskP(x, z);
            h += Volcano(x, z, r) * BaseMask(x, z);
            // over the tunnels the ground never dips into them; round the ramps it's flat
            foreach (var g in s_Segs)
            {
                g.Local(x, z, out float s, out float lat);
                if (s < -7f || s > g.L + 7f) continue;
                float al = Mathf.Abs(lat);
                if (g.Kind == 2) { if (al < 10f) h = Mathf.Lerp(0f, h, ThemeMaps.SmoothStepP(5f, 10f, al)); }
                else if (al < 7f) h = Mathf.Max(h, 0f);
            }
            return h;
        }

        static bool TunnelNear(float x, float z, float pad)
        {
            foreach (var g in s_Segs)
            {
                g.Local(x, z, out float s, out float lat);
                if (s > -OuterW - pad && s < g.L + OuterW + pad && Mathf.Abs(lat) < OuterW + pad) return true;
            }
            return false;
        }

        public override bool SpotOk(Vector3 p)
        {
            Layout();
            return new Vector2(p.x, p.z).magnitude > RimOut - 1f && !TunnelNear(p.x, p.z, 3f);
        }

        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 0.8f : kind == ResourceNode.Bush ? 0.5f : 1.4f;
        public override Color LeafTint(Color leaf) => new Color(0.75f, 0.25f, 0.32f);

        // ================================================================ the ground (cut open where the tunnels come up)
        /// <summary>Is this 2 m ground square (centre cx, cz) left out, for a tunnel coming up through it?</summary>
        bool CellCut(float cx, float cz)
        {
            foreach (var g in s_Segs)
            {
                g.Local(cx, cz, out float s, out float lat);
                if (Mathf.Abs(lat) >= OuterW) continue;
                float sMin = g.OpenA ? 0f : -4f, sMax = g.OpenB ? g.L : g.L + 4f;
                if (s < sMin || s > sMax) continue;
                float sc = Mathf.Clamp(s, 0f, g.L);
                if (!g.Roofed(sc)) return true;
                float roofTop = g.Floor(sc) + Inner + RoofT;
                float hmin = Mathf.Min(Mathf.Min(Height(cx - 1f, cz - 1f), Height(cx + 1f, cz - 1f)), Mathf.Min(Height(cx - 1f, cz + 1f), Height(cx + 1f, cz + 1f)));
                hmin = Mathf.Min(hmin, Height(cx, cz));
                if (roofTop > hmin - 0.05f) return true;
            }
            return false;
        }

        bool PieceCut(Seg g, float s0, float s1)
        {
            float[] ss = { s0 + 0.2f, (s0 + s1) * 0.5f, s1 - 0.2f };
            float[] ls = { -3f, -1f, 1f, 3f };
            foreach (float s in ss)
                foreach (float l in ls)
                {
                    var p = g.P(s, l, 0);
                    float cx = Mathf.Floor(p.x / 2f) * 2f + 1f, cz = Mathf.Floor(p.z / 2f) * 2f + 1f;
                    if (CellCut(cx, cz)) return true;
                }
            return false;
        }

        float HMax(Seg g, float s0, float s1)
        {
            float m = float.MinValue;
            foreach (float s in new[] { s0, (s0 + s1) * 0.5f, s1 })
                foreach (float l in new[] { -4.4f, 0f, 4.4f })
                {
                    var p = g.P(s, l, 0);
                    m = Mathf.Max(m, Height(p.x, p.z));
                }
            return m;
        }

        static Color GroundColour(Vector3 c, float ny)
        {
            float r = new Vector2(c.x, c.z).magnitude;
            if (ny < 0.72f) return Color.Lerp(new Color(0.46f, 0.22f, 0.15f), new Color(0.36f, 0.18f, 0.14f), r < RimOut ? 0.7f : 0f);
            if (r < CraterR) return new Color(0.36f, 0.21f, 0.17f);
            float n = ThemeMaps.SymNoiseP(c.x, c.z, 0.05f, ThemeMaps.SeedP + 3f);
            var dust = Color.Lerp(new Color(0.78f, 0.42f, 0.26f), new Color(0.68f, 0.34f, 0.21f), n);
            if (ThemeMaps.SymNoiseP(c.x, c.z, 0.13f, ThemeMaps.SeedP + 33f) > 0.64f) dust = Color.Lerp(dust, new Color(0.88f, 0.58f, 0.38f), 0.6f);
            if (c.y < -0.6f) dust = Color.Lerp(dust, new Color(0.5f, 0.26f, 0.17f), 0.6f);
            if (r < RimOut) dust = Color.Lerp(dust, new Color(0.38f, 0.19f, 0.15f), Mathf.Clamp01(c.y / RimH) * 0.8f + 0.2f);
            return dust;
        }

        public override void BuildGround(Transform root)
        {
            Layout();
            float ext = Mathf.Ceil((Half + 30f) / 2f) * 2f;
            ThemeKitB.Ground(root, "Ground", -ext, ext, 2f, (float x, float z, out Vector3 p) => { p = new Vector3(x, Height(x, z), z); return 0; }, GroundColour, CellCut);
            BuildTunnels(root);
        }

        // ================================================================ the tunnels
        static void Slab(MeshKit k, Transform cols, Seg g, float s0, float s1, float top0, float top1, float thick, float lat, float width, Color col)
        {
            Vector3 p0 = g.P(s0, lat, top0 - thick * 0.5f), p1 = g.P(s1, lat, top1 - thick * 0.5f);
            var d = p1 - p0;
            if (d.sqrMagnitude < 1e-6f) return;
            var rot = Quaternion.LookRotation(d, Vector3.up);
            var size = new Vector3(width, thick, d.magnitude + 0.04f);
            var c = (p0 + p1) * 0.5f;
            k.Box(c, size, rot, col, MeshKit.All);
            if (cols != null) ThemeKitB.BoxCol(cols, c, size, rot);
        }

        static void Block(MeshKit k, Transform cols, Seg g, float s0, float s1, float lat, float width, float bottom, float top, Color col)
        {
            if (top - bottom < 0.01f || s1 - s0 < 0.01f) return;
            var c = g.P((s0 + s1) * 0.5f, lat, (bottom + top) * 0.5f);
            var rot = Quaternion.LookRotation(new Vector3(g.D.x, 0, g.D.y));
            var size = new Vector3(width, top - bottom, s1 - s0 + 0.02f);
            k.Box(c, size, rot, col, MeshKit.All);
            if (cols != null) ThemeKitB.BoxCol(cols, c, size, rot);
        }

        bool InsideOther(Seg g, Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            foreach (var o in s_Segs) if (o != g && o.Inside(q)) return true;
            return false;
        }

        /// <summary>How high the walls (and the roof) reach over this piece.</summary>
        float TopAt(Seg g, float s0, float s1)
        {
            if (g.Kind == 0) return -Df + Inner + RoofT;
            float a = Mathf.Clamp(s0, 0f, g.L), b = Mathf.Clamp(s1, 0f, g.L);
            bool roofed = g.Roofed((a + b) * 0.5f);
            bool cut = PieceCut(g, s0, s1);
            if (roofed)
            {
                float c = Mathf.Max(g.Floor(a), g.Floor(b)) + Inner + RoofT;
                return cut ? Mathf.Max(c, HMax(g, s0, s1) + 0.25f) : c;
            }
            return Mathf.Max(0.5f, HMax(g, s0, s1) + 0.25f);
        }

        void BuildTunnels(Transform root)
        {
            var k = new MeshKit(); var glow = new MeshKit(); var lamps = new MeshKit();
            var wallCols = new GameObject("tunnel walls").transform; wallCols.SetParent(root, false);
            var floorCols = new GameObject("tunnel floors").transform; floorCols.SetParent(root, false);
            floorCols.gameObject.AddComponent<GroundMarker>();
            foreach (var g in s_Segs)
            {
                float L = g.L;
                float fa = g.OpenA ? 0f : -HalfW, fb = g.OpenB ? L : L + HalfW;
                float wa = g.OpenA ? 0f : -OuterW, wb = g.OpenB ? L : L + OuterW;
                // ---- the floor and the roof
                if (g.Kind == 0)
                {
                    Slab(k, floorCols, g, fa, fb, -Df, -Df, 1f, 0f, OuterW * 2f, k_Floor);
                    Slab(k, wallCols, g, fa, fb, -Df + Inner + RoofT, -Df + Inner + RoofT, RoofT, 0f, OuterW * 2f, k_Rock2);
                }
                else
                {
                    for (float s0 = fa; s0 < fb - 0.01f; s0 += 1f)
                    {
                        float s1 = Mathf.Min(s0 + 1f, fb);
                        float a = Mathf.Clamp(s0, 0f, L), b = Mathf.Clamp(s1, 0f, L);
                        Slab(k, floorCols, g, s0, s1, g.Floor(a), g.Floor(b), 1f, 0f, OuterW * 2f, k_Floor);
                        if (!g.Roofed((a + b) * 0.5f)) continue;
                        float c0 = g.Floor(a) + Inner, c1 = g.Floor(b) + Inner;
                        if (!PieceCut(g, s0, s1)) Slab(k, wallCols, g, s0, s1, c0 + RoofT, c1 + RoofT, RoofT, 0f, OuterW * 2f, k_Rock2);
                        else Block(k, wallCols, g, s0, s1, 0f, OuterW * 2f, Mathf.Min(c0, c1), TopAt(g, s0, s1), k_Rock2);
                    }
                }
                // ---- the walls, in runs (gaps where another tunnel joins)
                for (int side = -1; side <= 1; side += 2)
                {
                    float runA = 0f, runTop = 0f, runBot = 0f; bool run = false;
                    float lat = side * (HalfW + OuterW) * 0.5f;
                    for (float s0 = wa; s0 < wb - 0.01f; s0 += 1f)
                    {
                        float s1 = Mathf.Min(s0 + 1f, wb);
                        bool skip = InsideOther(g, g.P((s0 + s1) * 0.5f, lat, 0f));
                        float a = Mathf.Clamp(s0, 0f, L), b = Mathf.Clamp(s1, 0f, L);
                        float bot = Mathf.Min(g.Floor(a), g.Floor(b)) - 1f;
                        float top = skip ? 0f : TopAt(g, s0, s1);
                        if (run && (skip || Mathf.Abs(top - runTop) > 0.01f || Mathf.Abs(bot - runBot) > 0.01f))
                        {
                            Block(k, wallCols, g, runA, s0, lat, OuterW - HalfW, runBot, runTop, side < 0 ? k_Rock : k_Rock2);
                            run = false;
                        }
                        if (skip) continue;
                        if (!run) { run = true; runA = s0; runTop = top; runBot = bot; }
                    }
                    if (run) Block(k, wallCols, g, runA, wb, lat, OuterW - HalfW, runBot, runTop, side < 0 ? k_Rock : k_Rock2);
                }
                // ---- end walls where it doesn't join anything
                if (!g.OpenA && !InsideOther(g, g.P(-(HalfW + OuterW) * 0.5f, 0f, 0f)))
                    Block(k, wallCols, g, -OuterW, -HalfW, 0f, OuterW * 2f, g.Floor(0f) - 1f, TopAt(g, -OuterW, 0f), k_Rock);
                if (!g.OpenB && !InsideOther(g, g.P(L + (HalfW + OuterW) * 0.5f, 0f, 0f)))
                    Block(k, wallCols, g, L + HalfW, L + OuterW, 0f, OuterW * 2f, g.Floor(L) - 1f, TopAt(g, L, L + OuterW), k_Rock);
                // ---- steel ribs, lamps and guide strips
                int n = 0;
                for (float s = 2.5f; s < L - 1f; s += 5f, n++)
                {
                    if (!g.Roofed(s)) continue;
                    float f = g.Floor(s), c = f + Inner;
                    float rl = HalfW - 0.12f;
                    var pL = g.P(s, -rl, 0); var pR = g.P(s, rl, 0);
                    if (InsideOther(g, pL) || InsideOther(g, pR)) continue;
                    Block(k, null, g, s - 0.17f, s + 0.17f, -rl, 0.24f, f, c, k_Metal);
                    Block(k, null, g, s - 0.17f, s + 0.17f, rl, 0.24f, f, c, k_Metal);
                    Block(k, null, g, s - 0.17f, s + 0.17f, 0f, HalfW * 2f - 0.1f, c - 0.16f, c, k_Metal);
                    if (n % 2 == 0)
                    {
                        float ls = (n / 2) % 2 == 0 ? -(HalfW - 0.05f) : HalfW - 0.05f;
                        Block(lamps, null, g, s + 1.2f, s + 2f, ls, 0.1f, f + 2.6f, f + 2.95f, Color.white);
                    }
                }
                for (float s0 = Mathf.Max(0f, fa); s0 < Mathf.Min(L, fb) - 0.01f; s0 += 2f)
                {
                    float s1 = Mathf.Min(s0 + 2f, L);
                    for (int side = -1; side <= 1; side += 2)
                        Slab(glow, null, g, s0, s1, g.Floor(s0) + 0.02f, g.Floor(s1) + 0.02f, 0.03f, side * (HalfW - 0.3f), 0.1f, Color.white);
                }
                // ---- the portals: a steel frame with hazard stripes where it opens out
                if (g.Kind == 1) Portal(k, lamps, g, L - 0.15f, true);
                if (g.Kind == 2)
                {
                    float sr = 0f;
                    for (float s = 0f; s < L; s += 0.25f) if (g.Roofed(s)) sr = s;
                    Portal(k, lamps, g, Mathf.Ceil(sr) + 0.25f, false);
                }
            }
            ThemeKitB.Spawn(root, "tunnels", k, null, true);
            ThemeKitB.Spawn(root, "tunnel guide lights", glow, ThemeKitB.Glow(k_Guide, 1.6f), false);
            ThemeKitB.Spawn(root, "tunnel lamps", lamps, ThemeKitB.Glow(k_Lamp, 2.6f), false);
        }

        void Portal(MeshKit k, MeshKit glow, Seg g, float s, bool crater)
        {
            float f = g.Floor(Mathf.Clamp(s, 0f, g.L)), c = f + Inner;
            var hazardY = new Color(0.95f, 0.75f, 0.15f);
            float px = HalfW + 0.22f; // (the posts stand just outside the tunnel's width: nothing to catch on)
            for (int side = -1; side <= 1; side += 2)
            {
                Block(k, null, g, s - 0.25f, s + 0.25f, side * px, 0.44f, f - 0.2f, c + 0.6f, k_Metal);
                for (int i = 0; i < 4; i++) Block(k, null, g, s + 0.22f, s + 0.27f, side * px, 0.46f, f + 0.4f + i * 0.85f, f + 0.82f + i * 0.85f, i % 2 == 0 ? hazardY : new Color(0.12f, 0.12f, 0.12f));
            }
            Block(k, null, g, s - 0.25f, s + 0.25f, 0f, px * 2f + 0.44f, c, c + 0.6f, k_Metal);
            for (int i = 0; i < 7; i++) Block(k, null, g, s + 0.22f, s + 0.27f, -px + i * px / 3f, px / 3f, c + 0.1f, c + 0.5f, i % 2 == 0 ? hazardY : new Color(0.12f, 0.12f, 0.12f));
            Block(glow, null, g, s - 0.3f, s - 0.25f, 0f, HalfW * 2f - 0.4f, c + 0.12f, c + 0.42f, Color.white); // (a light strip on the beam's inside face)
            if (!crater)
            {
                // a beacon on top, so you can spot the way in
                Block(k, null, g, s - 0.15f, s + 0.15f, px, 0.2f, c + 0.6f, c + 2.2f, k_Metal);
                Block(glow, null, g, s - 0.2f, s + 0.2f, px, 0.4f, c + 2.2f, c + 2.6f, Color.white);
            }
        }

        // ================================================================ props
        readonly List<(Transform t, Vector3 vent, float ph)> m_Puffs = new List<(Transform, Vector3, float)>();
        Transform m_Sky;

        public override void BuildProps(Transform root)
        {
            Layout();
            m_Puffs.Clear();
            var rng = new System.Random(Cfg.MapSeed + 6262);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var k = new MeshKit(); var lava = new MeshKit();
            var cols = new GameObject("mars colliders").transform;
            cols.SetParent(root, false);
            float sc = Half / 100f;
            float th0 = Mathf.Atan2(Cfg.BaseCenter[0].z, Cfg.BaseCenter[0].x) * Mathf.Rad2Deg, span = 360f / Cfg.Copies;
            var bc = Cfg.BaseCenter[0];
            var placed = new List<(Vector2 p, float r)>();

            bool Spot(float clear, float minR, out Vector3 p)
            {
                for (int tries = 0; tries < 80; tries++)
                {
                    p = new Vector3(R(-Half + 10f, Half - 10f), 0, R(-Half + 10f, -6f));
                    if (!Cfg.InFirstSector(p, 4f + clear)) continue;
                    float r = new Vector2(p.x, p.z).magnitude;
                    if (r < minR) continue;
                    if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 4f + clear && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 4f + clear) continue;
                    if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) > Half - 17f - clear || TunnelNear(p.x, p.z, clear + 2f)) continue;
                    bool hit = false;
                    foreach (var q in placed) if ((q.p - new Vector2(p.x, p.z)).magnitude < q.r + clear + 1f) { hit = true; break; }
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
                    make(q, yaw + m * span);
                }
            }
            int N(float n) => Mathf.Max(1, Mathf.RoundToInt(n * sc * sc));
            float wild = RimOut + 3f;

            if (Spot(5f, wild, out var hp)) Each(hp, R(0, 360), (q, y) => Habitat(k, lava, cols, q, y));
            if (Spot(4f, wild, out var rp)) { var yaw = R(0, 360); Each(rp, yaw, (q, y) => Rover(k, cols, q, y)); }
            for (int i = 0; i < N(6); i++) if (Spot(3f, wild, out var p)) { int sd = rng.Next(); Each(p, R(0, 360), (q, y) => Spire(k, cols, q, y, sd)); }
            for (int i = 0; i < N(26); i++) if (Spot(1.5f, wild, out var p)) { int sd = rng.Next(); float s = R(0.5f, 2.2f); Each(p, R(0, 360), (q, y) => Boulder(k, cols, q, y, s, sd)); }
            // pebbles (just to look at)
            int pebbles = Mathf.RoundToInt(500 * sc * sc);
            for (int i = 0; i < pebbles; i++)
            {
                float x = R(-Half + 4f, Half - 4f), z = R(-Half + 4f, Half - 4f);
                if (new Vector2(x, z).magnitude < CraterR + 2f || TunnelNear(x, z, 0.5f)) continue;
                float s = R(0.12f, 0.4f);
                ThemeKitB.Ball(k, new Vector3(x, Height(x, z) + s * 0.3f, z), new Vector3(s, s * 0.7f, s), Quaternion.Euler(0, R(0, 360), 0), Color.Lerp(k_Rock, new Color(0.3f, 0.16f, 0.13f), R(0f, 1f)), 0, 0.2f, i);
            }

            // ---- lava: a glowing seam round the crater floor (not at the portals and passes), rivulets down the slopes
            const int segs = 120;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var m0 = (d0 + d1).normalized * (CraterR + 0.6f);
                if (TunnelNear(m0.x, m0.z, 1.5f)) continue;
                float ang = Mathf.Atan2(m0.z, m0.x) * Mathf.Rad2Deg;
                bool nearPass = false;
                foreach (float pa in s_Passes) if (Mathf.Abs(Mathf.DeltaAngle(ang, pa)) < 16f) nearPass = true;
                if (nearPass) continue;
                float w = 0.25f + 0.15f * Mathf.Sin(i * 1.7f);
                Vector3 P(Vector3 d, float rr) { var q = d * rr; q.y = Height(q.x, q.z) + 0.06f; return q; }
                ThemeKitB.Quad(lava, P(d0, CraterR + 0.6f - w), P(d1, CraterR + 0.6f - w), P(d1, CraterR + 0.6f + w), P(d0, CraterR + 0.6f + w), Color.white, Vector3.up);
            }
            float[] rivs = Cfg.FourWay ? new[] { -40f, 0f, 40f } : new[] { -78f, -24f, 24f, 78f };
            foreach (float off in rivs)
            {
                float baseA = (th0 + off) * Mathf.Deg2Rad;
                var pts = new List<Vector3>();
                for (float rr = RimPeakR + 0.3f; rr < RimOut * 0.88f; rr += 1.6f)
                {
                    float a = baseA + Mathf.Sin(rr * 0.45f + off) * 0.035f;
                    pts.Add(new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr));
                }
                float w0 = 0.42f;
                for (int m = 0; m < Cfg.Copies; m++)
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        var p0 = Cfg.Copy(pts[i], m); var p1 = Cfg.Copy(pts[i + 1], m);
                        if (TunnelNear(p0.x, p0.z, 0.5f)) continue;
                        var dir = (p1 - p0).normalized; var sd = new Vector3(dir.z, 0, -dir.x);
                        float w = w0 * (1f - i / (float)pts.Count * 0.7f);
                        Vector3 Dr(Vector3 v) { v.y = Height(v.x, v.z) + 0.07f; return v; }
                        ThemeKitB.Quad(lava, Dr(p0 + sd * w), Dr(p1 + sd * w * 0.9f), Dr(p1 - sd * w * 0.9f), Dr(p0 - sd * w), Color.white, Vector3.up);
                    }
            }
            // smoke from vents on the rim (glowing at the bottom)
            float[] vents = Cfg.FourWay ? new[] { -40f, 0f, 40f } : new[] { -80f, -20f, 20f, 80f };
            var puffKit = new MeshKit();
            ThemeKitB.Ball(puffKit, Vector3.zero, Vector3.one, Quaternion.identity, new Color(0.34f, 0.29f, 0.28f), 1, 0.15f, 3);
            var puffMesh = puffKit.ToMesh("smoke puff");
            var puffs = new GameObject("smoke");
            puffs.transform.SetParent(root, false);
            puffs.AddComponent<OwnedMesh>().Mesh = puffMesh;
            int pi = 0;
            foreach (float off in vents)
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    float a = (th0 + off + m * span) * Mathf.Deg2Rad;
                    var v = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (RimPeakR + 0.4f);
                    v.y = Height(v.x, v.z);
                    ThemeKitB.Ball(lava, v + Vector3.up * 0.1f, new Vector3(1.1f, 0.25f, 1.1f), Quaternion.identity, Color.white, 0);
                    for (int j = 0; j < 3; j++)
                    {
                        var go = new GameObject("puff");
                        go.transform.SetParent(puffs.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = puffMesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = ThemeKitB.Painted();
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        m_Puffs.Add((go.transform, v, j / 3f + (pi++ % 7) * 0.091f));
                    }
                }
            // markers either side of each pass, at its foot
            foreach (float pa in s_Passes)
            {
                float a = pa * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var sd = new Vector3(-d.z, 0, d.x);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    var q = d * (RimOut * 0.72f) + sd * sg * 4.2f;
                    q.y = Height(q.x, q.z);
                    ThemeKitB.Cyl(k, q, q + Vector3.up * 2.2f, 0.14f, 0.12f, 6, k_Metal);
                    ThemeKitB.Ball(lava, q + Vector3.up * 2.35f, Vector3.one * 0.22f, Quaternion.identity, Color.white, 0);
                    ThemeKitB.CapCol(cols, q, q + Vector3.up * 2.2f, 0.18f);
                }
            }
            ThemeKitB.Spawn(root, "mars props", k, null, true);
            ThemeKitB.Spawn(root, "lava", lava, ThemeKitB.Glow(k_Lava, 2.2f), false);
            BuildHorizon(root);
            BuildSky(root);
        }

        static void Boulder(MeshKit k, Transform cols, Vector3 p, float yaw, float s, int seed)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var col = Color.Lerp(new Color(0.48f, 0.24f, 0.17f), new Color(0.6f, 0.33f, 0.22f), (seed & 255) / 255f);
            var c = p + Vector3.up * s * 0.35f;
            ThemeKitB.Ball(k, c, new Vector3(s, s * 0.72f, s * 0.9f), rot, col, 0, 0.28f, seed & 1023);
            if (s > 0.8f && cols != null)
            {
                var go = new GameObject("col");
                go.transform.SetParent(cols, false);
                go.transform.localPosition = c;
                go.AddComponent<SphereCollider>().radius = s * 0.78f;
            }
        }

        static void Spire(MeshKit k, Transform cols, Vector3 p, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float h = R(6f, 13f), r = R(1.4f, 2.4f);
            int layers = 3 + rng.Next(2);
            float y = -0.5f;
            var at = p;
            Color[] cs = { new Color(0.6f, 0.3f, 0.2f), new Color(0.72f, 0.42f, 0.28f), new Color(0.5f, 0.25f, 0.17f) };
            for (int i = 0; i < layers; i++)
            {
                float lh = (h + 0.5f) / layers;
                float r0 = r * Mathf.Lerp(1f, 0.55f, i / (float)layers), r1 = r0 * R(0.75f, 0.95f);
                var next = at + new Vector3(R(-0.3f, 0.3f), 0, R(-0.3f, 0.3f));
                ThemeKitB.Cyl(k, new Vector3(at.x, p.y + y, at.z), new Vector3(next.x, p.y + y + lh, next.z), r0, r1, 6, cs[i % cs.Length], false, true, yaw * Mathf.Deg2Rad + i);
                at = next; y += lh;
            }
            // a cap stone, wider than the top
            ThemeKitB.Cyl(k, new Vector3(at.x, p.y + y, at.z), new Vector3(at.x, p.y + y + 0.7f, at.z), r * 0.8f, r * 0.7f, 6, cs[2], true, true, yaw);
            ThemeKitB.CapCol(cols, p, p + Vector3.up * h, r * 0.75f);
        }

        static void Rover(MeshKit k, Transform cols, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector3 L(float x, float y, float z) => p + rot * new Vector3(x, y, z);
            var white = new Color(0.86f, 0.85f, 0.82f); var dark = new Color(0.17f, 0.16f, 0.16f);
            k.Box(L(0, 1.15f, 0), new Vector3(2f, 0.7f, 3f), rot, white, MeshKit.All);
            k.Box(L(0, 1.55f, 0), new Vector3(1.8f, 0.15f, 2.8f), rot, k_Metal, MeshKit.All);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int iz = -1; iz <= 1; iz++)
            {
                var c = L(sx * 1.25f, 0.45f, iz * 1.1f);
                var ax = rot * Vector3.right * 0.18f;
                ThemeKitB.Cyl(k, c - ax, c + ax, 0.45f, 0.45f, 10, dark);
                k.Box(L(sx * 1.05f, 0.75f, iz * 1.1f), new Vector3(0.4f, 0.12f, 0.12f), rot, k_Metal, MeshKit.All);
            }
            k.Box(L(0, 1.8f, -0.4f), new Vector3(2.8f, 0.06f, 1.6f), rot * Quaternion.Euler(-8f, 0, 0), new Color(0.14f, 0.2f, 0.42f), MeshKit.All);
            ThemeKitB.Cyl(k, L(0.5f, 1.6f, 1.1f), L(0.5f, 2.9f, 1.1f), 0.07f, 0.07f, 6, white);
            k.Box(L(0.5f, 3.0f, 1.15f), new Vector3(0.5f, 0.28f, 0.3f), rot, white, MeshKit.All);
            k.Cone(L(-0.6f, 1.65f, 1.0f), 0.55f, 0.3f, 8, 0f, new Color(0.8f, 0.8f, 0.8f), new Color(0.6f, 0.6f, 0.6f));
            ThemeKitB.BoxCol(cols, L(0, 1f, 0), new Vector3(2.7f, 2f, 3.2f), rot);
            // its tracks, running back the way it came
            var trk = new Color(0.55f, 0.28f, 0.18f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int i = 0; i < 14; i++)
                {
                    Vector3 a = L(sx * 1.25f, 0, -2f - i * 1.2f), b = L(sx * 1.25f, 0, -3.1f - i * 1.2f);
                    var side = rot * Vector3.right * 0.22f;
                    Vector3 Dr(Vector3 v) { v.y = MarsH(v.x, v.z) + 0.04f; return v; }
                    ThemeKitB.Quad(k, Dr(a - side), Dr(a + side), Dr(b + side), Dr(b - side), trk, Vector3.up);
                }
        }

        static System.Func<float, float, float> s_H;
        static float MarsH(float x, float z) => s_H != null ? s_H(x, z) : 0f;

        static void Habitat(MeshKit k, MeshKit glow, Transform cols, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector3 L(float x, float y, float z) => p + rot * new Vector3(x, y, z);
            var white = new Color(0.9f, 0.89f, 0.86f); var orange = new Color(0.92f, 0.45f, 0.18f);
            ThemeKitB.Cyl(k, L(0, -0.3f, 0), L(0, 0.9f, 0), 4.2f, 4.2f, 16, white, false, false);
            ThemeKitB.Cyl(k, L(0, 0.6f, 0), L(0, 1.0f, 0), 4.26f, 4.26f, 16, orange, false, false);
            ThemeKitB.Ball(k, L(0, 0.9f, 0), new Vector3(4.2f, 3.2f, 4.2f), rot, white, 1);
            // the airlock
            ThemeKitB.Cyl(k, L(0, 1.3f, 3.6f), L(0, 1.3f, 6.2f), 1.3f, 1.3f, 10, white, false, true);
            k.Box(L(0, 1.2f, 6.25f), new Vector3(1.2f, 1.9f, 0.08f), rot, k_Metal, MeshKit.All);
            k.Box(L(0, 2.4f, 6.27f), new Vector3(0.8f, 0.12f, 0.05f), rot, orange, MeshKit.All);
            // windows round the dome
            for (int i = 0; i < 6; i++)
            {
                var q = rot * Quaternion.Euler(0, 30f + i * 60f, 0);
                var at = p + q * new Vector3(0, 2.4f, 3.55f);
                k.Box(at, new Vector3(0.9f, 0.5f, 0.1f), q * Quaternion.Euler(-35f, 0, 0), new Color(0.2f, 0.3f, 0.45f), MeshKit.All);
            }
            // solar panels and an antenna with a blinking light
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var c = L(sx * 7.5f, 0, -1f);
                ThemeKitB.Cyl(k, c, c + Vector3.up * 1.4f, 0.08f, 0.08f, 6, k_Metal);
                k.Box(c + Vector3.up * 1.5f, new Vector3(2.4f, 0.06f, 3.4f), rot * Quaternion.Euler(-20f, 0, 0), new Color(0.14f, 0.2f, 0.42f), MeshKit.All);
                ThemeKitB.CapCol(cols, c, c + Vector3.up * 1.4f, 0.3f);
            }
            var mast = L(-3f, 0, -3.5f);
            ThemeKitB.Cyl(k, mast, mast + Vector3.up * 7f, 0.12f, 0.08f, 6, k_Metal);
            k.Cone(mast + Vector3.up * 5.2f, 0.9f, 0.45f, 10, 0f, white, new Color(0.7f, 0.7f, 0.7f));
            ThemeKitB.Ball(glow, mast + Vector3.up * 7.1f, Vector3.one * 0.2f, Quaternion.identity, Color.white, 0);
            ThemeKitB.BoxCol(cols, L(0, 1.5f, 0), new Vector3(7.4f, 3f, 7.4f), rot);
            ThemeKitB.BoxCol(cols, L(0, 1.5f, 0), new Vector3(7.4f, 3f, 7.4f), rot * Quaternion.Euler(0, 45f, 0));
            ThemeKitB.BoxCol(cols, L(0, 1.3f, 4.9f), new Vector3(2.6f, 2.6f, 2.6f), rot);
            ThemeKitB.CapCol(cols, mast, mast + Vector3.up * 7f, 0.2f);
        }

        /// <summary>Flat-topped mesas round the horizon, out past the walls (nothing solid).</summary>
        void BuildHorizon(Transform root)
        {
            var rng = new System.Random(Cfg.MapSeed + 77);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var k = new MeshKit();
            float s = Mathf.Max(0.6f, Half / 100f);
            Color[] cs = { new Color(0.62f, 0.3f, 0.2f), new Color(0.74f, 0.42f, 0.28f), new Color(0.55f, 0.27f, 0.18f), new Color(0.8f, 0.5f, 0.34f) };
            for (int i = 0; i < 30; i++)
            {
                float a = i / 30f * Mathf.PI * 2f + R(-0.06f, 0.06f), d = Half * R(1.4f, 1.95f) + 20f;
                var c = new Vector3(Mathf.Cos(a) * d, -6f, Mathf.Sin(a) * d);
                float r = R(18f, 42f) * s, h = R(16f, 48f) * s;
                int layers = 3;
                float y = c.y;
                for (int l = 0; l < layers; l++)
                {
                    float lh = (h + 6f) / layers, r0 = r * (1f - l * 0.06f), r1 = r0 * 0.94f;
                    ThemeKitB.Cyl(k, new Vector3(c.x, y, c.z), new Vector3(c.x, y + lh, c.z), r0, r1, 7, cs[(i + l) % cs.Length], false, l == layers - 1, a);
                    y += lh;
                }
                // a scree slope round its foot
                ThemeKitB.Cyl(k, new Vector3(c.x, c.y, c.z), new Vector3(c.x, c.y + h * 0.35f, c.z), r * 1.35f, r * 0.98f, 7, cs[2], false, false, a + 0.2f);
            }
            ThemeKitB.Spawn(root, "mesas", k, null, false);
        }

        void BuildSky(Transform root)
        {
            m_Sky = new GameObject("mars sky").transform;
            m_Sky.SetParent(root, false);
            var k = new MeshKit();
            ThemeKitB.Ball(k, new Vector3(-0.5f, 0.42f, 0.75f).normalized * 950f, new Vector3(26f, 19f, 22f), Quaternion.Euler(20f, 40f, 0f), new Color(0.62f, 0.55f, 0.52f), 1, 0.25f, 9);
            ThemeKitB.Ball(k, new Vector3(0.7f, 0.55f, -0.45f).normalized * 1000f, new Vector3(10f, 8f, 9f), Quaternion.Euler(0f, 10f, 30f), new Color(0.7f, 0.64f, 0.6f), 0, 0.2f, 4);
            ThemeKitB.Spawn(m_Sky, "moons", k, ThemeKitB.Glow(new Color(0.78f, 0.7f, 0.66f), 0.9f), false);
        }

        // ================================================================ the crater floor: cover round the ball
        /// <summary>The crater floor round the ball: clusters of boulders, stubby basalt columns across the way in from each
        /// team's tunnel, and a smoking lava vent - the same in every team's part, all clear of the ball's few metres, the
        /// tunnel mouths and the sign.</summary>
        public override bool BuildCentre(Transform root)
        {
            Layout();
            var t = new GameObject("crater cover").transform;
            t.SetParent(root, false);
            var cols = new GameObject("crater cover colliders").transform;
            cols.SetParent(t, false);
            var k = new MeshKit(); var lava = new MeshKit();
            var rng = new System.Random(Cfg.MapSeed + 3131);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float span = 360f / Cfg.Copies;
            float th0 = Mathf.Atan2(Cfg.BaseCenter[0].z, Cfg.BaseCenter[0].x) * Mathf.Rad2Deg;
            Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            Color[] rock = { new Color(0.42f, 0.2f, 0.15f), new Color(0.5f, 0.25f, 0.17f), new Color(0.34f, 0.17f, 0.14f) };
            var basalt = new Color(0.22f, 0.16f, 0.16f);
            float wallR = Mathf.Min(10.5f, CraterExit - 4.5f);

            // the layout of one part (random once, the same for every team)
            var boulders = new List<(float a, float r, float s, int seed)>();
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                float a = sgn * span * R(0.22f, 0.3f), r = R(8.5f, 10.5f);
                boulders.Add((a, r, R(1.25f, 1.6f), rng.Next()));
                boulders.Add((a + sgn * R(5f, 9f), r + R(1.2f, 2f), R(0.8f, 1.1f), rng.Next()));
            }
            var columns = new List<(float lat, float h, float rad)>();
            for (int i = 0; i < 5; i++) columns.Add(((i - 2) * 0.78f + R(-0.1f, 0.1f), R(1.3f, 2.3f) * (i == 0 || i == 4 ? 0.75f : 1f), R(0.42f, 0.5f)));
            float ventA = span * 0.5f, ventR = 9.5f;

            for (int m = 0; m < Cfg.Copies; m++)
            {
                float baseA = th0 + m * span;
                // boulder clusters
                foreach (var b in boulders)
                {
                    var c = Dir(baseA + b.a) * b.r;
                    var rot = Quaternion.Euler(0, b.seed % 360, 0);
                    var radii = new Vector3(b.s, b.s * 0.8f, b.s * 0.9f);
                    ThemeKitB.Ball(k, c + Vector3.up * b.s * 0.55f, radii, rot, rock[b.seed & 1], 1, 0.22f, b.seed & 1023);
                    ThemeKitB.Ball(k, c + rot * new Vector3(b.s * 0.9f, 0.2f, b.s * 0.4f), radii * 0.45f, rot, rock[2], 0, 0.25f, (b.seed >> 3) & 1023);
                    ThemeKitB.BoxCol(cols, c + Vector3.up * b.s * 0.55f, radii * 1.5f, rot);
                }
                // basalt columns across the way in from this team's tunnel (a gap to go round on each side)
                {
                    var o = Dir(baseA); var sd = new Vector3(-o.z, 0, o.x);
                    var c = o * wallR;
                    foreach (var col in columns)
                    {
                        var p = c + sd * col.lat;
                        ThemeKitB.Cyl(k, p + Vector3.down * 0.2f, p + Vector3.up * col.h, col.rad, col.rad * 0.92f, 6, basalt, false, true, col.lat);
                        ThemeKitB.CapCol(cols, p, p + Vector3.up * col.h, col.rad * 0.9f);
                    }
                    k.Box(c + sd * 2.2f + Vector3.up * 0.15f, new Vector3(1.1f, 0.3f, 0.7f), Quaternion.LookRotation(o), basalt * 1.2f, MeshKit.All); // (a fallen piece)
                }
                // a lava vent: a low ring of rock (you can hop it) round a glowing pool, smoke rising
                {
                    var c = Dir(baseA + ventA) * ventR;
                    ThemeKitB.Cyl(k, c + Vector3.down * 0.1f, c + Vector3.up * 0.75f, 1.6f, 1.15f, 9, rock[2], false, false, m);
                    ThemeKitB.Cyl(k, c + Vector3.up * 0.75f, c + Vector3.up * 0.55f, 1.15f, 0.85f, 9, rock[0], false, false, m, true);
                    ThemeKitB.Cyl(lava, c + Vector3.up * 0.5f, c + Vector3.up * 0.56f, 0.9f, 0.9f, 9, Color.white, false, true, m);
                    ThemeKitB.BoxCol(cols, c + Vector3.up * 0.35f, new Vector3(2.4f, 0.75f, 2.4f), Quaternion.identity);
                    ThemeKitB.BoxCol(cols, c + Vector3.up * 0.35f, new Vector3(2.4f, 0.75f, 2.4f), Quaternion.Euler(0, 45f, 0));
                    m_Vents.Add(c + Vector3.up * 0.4f);
                }
            }
            // smoke over the vents (same as the rim's)
            var puffKit = new MeshKit();
            ThemeKitB.Ball(puffKit, Vector3.zero, Vector3.one, Quaternion.identity, new Color(0.34f, 0.29f, 0.28f), 1, 0.15f, 3);
            var puffMesh = puffKit.ToMesh("vent puff");
            var puffs = new GameObject("vent smoke");
            puffs.transform.SetParent(t, false);
            puffs.AddComponent<OwnedMesh>().Mesh = puffMesh;
            int pi = 0;
            foreach (var v in m_Vents)
                for (int j = 0; j < 2; j++)
                {
                    var go = new GameObject("puff");
                    go.transform.SetParent(puffs.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = puffMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = ThemeKitB.Painted();
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    m_Puffs.Add((go.transform, v, j / 2f + (pi++ % 5) * 0.13f));
                }
            m_Vents.Clear();
            ThemeKitB.Spawn(t, "crater rocks", k, null, true);
            ThemeKitB.Spawn(t, "crater lava", lava, ThemeKitB.Glow(k_Lava, 2.2f), false);
            return true;
        }

        readonly List<Vector3> m_Vents = new List<Vector3>();

        // ================================================================ the Dust Strider (the horse here) and the glow-pod bush
        public override string MountName => "Dust Strider";

        /// <summary>A six-legged Martian beast: rust-red hide, armour plates down its back, a wide flat head with four glowing
        /// eyes and two feelers, a spiked tail (a unicorn: a pale one with a gold horn).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var hide = unicorn ? new Color(0.92f, 0.88f, 0.84f) : new Color(0.62f, 0.3f, 0.2f);
            var plate = unicorn ? new Color(0.8f, 0.74f, 0.7f) : new Color(0.38f, 0.18f, 0.14f);
            var under = unicorn ? new Color(1f, 0.95f, 0.9f) : new Color(0.78f, 0.52f, 0.36f);
            var eye = ThemeKitB.Glow(unicorn ? new Color(1f, 0.82f, 0.35f) : new Color(0.35f, 1f, 0.85f), 2.4f);
            // body: long and low, plates down the back
            ThemeKitB.MHit(Art.Box(t, hide, new Vector3(0, 1.12f, -0.05f), new Vector3(0.7f, 0.55f, 1.65f)), ghost);
            Art.Box(t, under, new Vector3(0, 0.82f, -0.05f), new Vector3(0.56f, 0.1f, 1.45f));
            for (int i = 0; i < 4; i++)
                Art.Box(t, plate, new Vector3(0, 1.4f, -0.72f + i * 0.4f + (i >= 2 ? 0.25f : 0f)), new Vector3(0.62f, 0.1f, 0.34f), new Vector3(-8f, 0, 0));
            for (int i = 0; i < 3; i++)
                Art.Part(t, Art.Cone, plate, new Vector3(0, 1.44f, -0.75f + i * 0.2f), new Vector3(0.1f, 0.16f + i * 0.02f, 0.14f)); // (spines behind the seat)
            // head: wide, flat, four eyes, feelers, mandibles (pivots to "graze")
            var neck = ThemeKitB.MPivot(t, "neck", new Vector3(0, 1.22f, 0.8f));
            ThemeKitB.MHit(Art.Box(neck, hide, new Vector3(0, 0.05f, 0.06f), new Vector3(0.46f, 0.36f, 0.3f), new Vector3(10, 0, 0)), ghost);
            ThemeKitB.MHead(neck, ghost, hide, new Vector3(0, 0.16f, 0.36f), new Vector3(0.6f, 0.26f, 0.46f));
            Art.Box(neck, plate, new Vector3(0, 0.31f, 0.33f), new Vector3(0.62f, 0.06f, 0.42f));
            for (int s = -1; s <= 1; s += 2)
            {
                for (int e = 0; e < 2; e++)
                    Art.Part(neck, Art.Sphere, Color.white, new Vector3(s * (0.12f + e * 0.13f), 0.22f - e * 0.03f, 0.59f - e * 0.04f), Vector3.one * (0.09f - e * 0.02f), default, false, eye);
                Art.Box(neck, plate, new Vector3(s * 0.12f, 0.02f, 0.62f), new Vector3(0.06f, 0.06f, 0.22f), new Vector3(0, -s * 25f, 0)); // mandibles
                var feeler = Art.Box(neck, plate, new Vector3(s * 0.16f, 0.5f, 0.42f), new Vector3(0.03f, 0.42f, 0.03f), new Vector3(30f, 0, s * -18f));
                Art.Part(neck, Art.Sphere, Color.white, new Vector3(s * 0.23f, 0.68f, 0.54f), Vector3.one * 0.07f, default, false, eye);
                feeler.name = "feeler";
            }
            if (unicorn) ThemeKitB.MHorn(neck, new Vector3(0, 0.34f, 0.5f));
            head = neck;
            // a segmented tail with a spike
            var tl = ThemeKitB.MPivot(t, "tail", new Vector3(0, 1.25f, -0.86f));
            var at = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                var next = at + new Vector3(0, 0.02f + i * 0.04f, -0.2f);
                var seg = Art.Box(tl, i % 2 == 0 ? hide : plate, (at + next) * 0.5f, new Vector3(0.24f - i * 0.04f, 0.2f - i * 0.03f, 0.22f), new Vector3(-i * 10f, 0, 0));
                if (i < 2) ThemeKitB.MHit(seg, ghost);
                at = next;
            }
            Art.Part(tl, Art.Cone, under, at + new Vector3(0, 0.02f, -0.02f), new Vector3(0.1f, 0.24f, 0.1f), new Vector3(-70f, 0, 0));
            tail = tl;
            // six legs: thigh out to the side, shin down to a pointed foot (the middle pair isn't in `legs`' trot pairs, so it
            // swings with the back right: close enough)
            float[] zs = { 0.55f, 0.55f, -0.6f, -0.6f, -0.02f, -0.02f };
            for (int i = 0; i < 6; i++)
            {
                float s = i % 2 == 0 ? -1f : 1f;
                var leg = ThemeKitB.MPivot(t, "leg", new Vector3(s * 0.3f, 0.95f, zs[i]));
                ThemeKitB.MHit(Art.Box(leg, hide, new Vector3(s * 0.12f, -0.08f, 0), new Vector3(0.3f, 0.16f, 0.16f), new Vector3(0, 0, s * 25f)), ghost);
                ThemeKitB.MHit(Art.Box(leg, plate, new Vector3(s * 0.22f, -0.5f, 0), new Vector3(0.13f, 0.72f, 0.13f), new Vector3(0, 0, s * -6f)), ghost);
                Art.Part(leg, Art.Cone, under, new Vector3(s * 0.25f, -0.76f, 0), new Vector3(0.14f, 0.2f, 0.14f), new Vector3(180f, 0, 0));
                legs?.Add(leg);
            }
            saddle = ThemeKitB.MSaddle(t, ghost, new Color(0.3f, 0.3f, 0.32f), 0.7f, 1.45f);
            return true;
        }

        /// <summary>The game's own berry bush in Mars colours: dusty crimson leaves, glowing-green berries (the food).</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.52f, 0.17f, 0.2f), new Color(0.4f, 1f, 0.55f));
            return true;
        }

        // ================================================================ trees: tall alien stalks with puffy red crowns and glowing spores
        /// <summary>The stalk's radius where the X goes (it's straight and this thick from the ground to 3 m).</summary>
        const float TrunkR = 0.36f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 31);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var k = new MeshKit(); var g = new MeshKit();
            Color[] stalk = { new Color(0.38f, 0.14f, 0.16f), new Color(0.3f, 0.1f, 0.13f) };
            Color[] crown = { new Color(0.78f, 0.22f, 0.35f), new Color(0.92f, 0.42f, 0.25f), new Color(0.62f, 0.18f, 0.42f) };
            // the foot of the stalk: dead straight, round and TrunkR thick up to 3 m (where the X goes)
            const float Straight = 3f;
            ThemeKitB.Cyl(k, Vector3.zero, Vector3.up * Straight, TrunkR, TrunkR, 14, stalk[0], true, false);
            var at = Vector3.up * Straight;
            ThemeKitB.Ball(k, at + Vector3.up * 0.16f, new Vector3(0.4f, 0.16f, 0.4f), Quaternion.identity, stalk[1], 0);
            // then it wobbles and narrows the rest of the way up
            float upper = Mathf.Max(1.8f, h - Straight); // (the crown stays clear over the 3 m of bare stalk)
            const int segs = 2;
            for (int i = 0; i < segs; i++)
            {
                var next = Vector3.up * (Straight + upper * (i + 1) / segs) + new Vector3(R(-0.35f, 0.35f), 0, R(-0.35f, 0.35f));
                ThemeKitB.Cyl(k, at, next, Mathf.Lerp(TrunkR, 0.2f, i / (float)segs), Mathf.Lerp(TrunkR, 0.2f, (i + 1f) / segs), 9, stalk[(i + 1) % 2], false, false);
                // a ring of little nodules where the segments meet
                ThemeKitB.Ball(k, next, new Vector3(0.32f, 0.16f, 0.32f), Quaternion.identity, stalk[1], 0);
                at = next;
            }
            float big = R(1.3f, 1.8f);
            ThemeKitB.Ball(k, at + Vector3.up * big * 0.6f, new Vector3(big, big * 0.85f, big), Quaternion.Euler(0, R(0, 360), 0), crown[rng.Next(crown.Length)], 1, 0.15f, seed & 255);
            int bulbs = 2 + rng.Next(3);
            for (int i = 0; i < bulbs; i++)
            {
                var off = Quaternion.Euler(0, i * 360f / bulbs + R(-20f, 20f), 0) * new Vector3(big * 0.95f, R(-0.5f, 0.4f), 0);
                ThemeKitB.Ball(k, at + off, Vector3.one * R(0.6f, 1f), Quaternion.identity, crown[rng.Next(crown.Length)], 1);
            }
            for (int i = 0; i < 7; i++)
            {
                var d = Quaternion.Euler(R(-60f, 30f), R(0, 360), 0) * Vector3.forward;
                ThemeKitB.Ball(g, at + Vector3.up * big * 0.5f + d * big * R(1.1f, 1.6f), Vector3.one * R(0.08f, 0.14f), Quaternion.identity, Color.white, 0);
            }
            ThemeKitB.Spawn(tr, "mars tree", k, null, true);
            ThemeKitB.Spawn(tr, "mars spores", g, ThemeKitB.Glow(new Color(0.4f, 0.95f, 0.8f), 2f), false);
            return true;
        }

        // ================================================================ every frame
        public override void ClientTick()
        {
            float t = Time.time;
            foreach (var p in m_Puffs)
            {
                if (p.t == null) continue;
                float life = Mathf.Repeat(t / 7f + p.ph, 1f);
                float s = Mathf.Sin(life * Mathf.PI) * (1.2f + life * 3.2f);
                p.t.position = p.vent + new Vector3(Mathf.Sin(p.ph * 9f) * life * 4f, 1f + life * 16f, Mathf.Cos(p.ph * 7f) * life * 4f);
                p.t.localScale = Vector3.one * Mathf.Max(0.01f, s);
            }
            var cam = Camera.main;
            if (m_Sky != null)
            {
                bool hide = ThemeKitB.SkyHidden(cam);
                if (m_Sky.gameObject.activeSelf == hide) m_Sky.gameObject.SetActive(!hide);
                if (!hide) m_Sky.position = cam.transform.position;
            }
            ThemeKitB.Keep(cam);
        }

        public override void ApplySky()
        {
            ThemeKitB.Begin();
            ThemeKitB.Skybox(new Color(1f, 0.62f, 0.4f), new Color(0.45f, 0.25f, 0.16f), 1.15f, 2.2f);
            // (less orange ambient and later fog than at first: everything was one flat orange, with no shading to read the ground by)
            ThemeKitB.Fog(new Color(0.86f, 0.62f, 0.48f), 80f, Mathf.Max(340f, Half * 3.4f));
            ThemeKitB.Lighting(new Color(1f, 0.93f, 0.84f), new Color(0.62f, 0.52f, 0.48f), new Color(0.52f, 0.4f, 0.33f), new Color(0.28f, 0.2f, 0.16f));
        }

        public override void Cleanup() => ThemeKitB.End();

        public MarsMap() { s_H = (x, z) => Height(x, z); }
    }
}
