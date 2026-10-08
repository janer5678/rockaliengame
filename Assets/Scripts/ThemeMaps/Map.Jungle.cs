using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// JUNGLE: mossy forest floor closed in by tall grey rock cliffs. Lots of tall thin leafy trees. Round the middle a
    /// ring of giant climbing trees, each with plank decks one above another (5.5 m a level, up to 16.5 m), solid ladders
    /// from deck to deck and plank bridges between the trees - high ones too; a few lower ones out by the bases (with a
    /// ramp). From the top decks of the trees nearest the middle, high walkways run in to a plank ring 16.5 m up right
    /// over the ball (well clear of its glass dome; the ball drops through the open middle). Every ladder is climbable
    /// from the front only and tops out right at a deck's open edge. Mossy jungle rocks all over the floor, a red stilt
    /// treehouse (with a porch at the ladder) and a roofed hut out to the sides, a ruined stone shrine-wall and boulders
    /// round the ball. Berry bushes, okapis to ride. Light green haze.
    /// </summary>
    public class JungleMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Jungle;
        public override string Label => "Jungle";
        public override string Blurb => "Thick jungle walled in by rock cliffs. Giant climbing trees round the middle - ladders up to decks three storeys high, bridges between them, and a sky walkway right over the ball. Mossy rocks for cover below.";
        public override bool Mountains => false;
        public override float MaxSpotHeight => 3f;
        public override string MountName => "Okapi";

        const float DeckY = 5.5f;
        /// <summary>Each deck level's half size (the higher, the smaller: the ladder up to the next one stands on the one
        /// below, with 1.2 m of floor in front of it to stand on).</summary>
        static readonly float[] Halfs = { 4.4f, 3.2f, 2.0f };
        static float LevelY(int lv) => DeckY * (lv + 1);
        /// <summary>The sky ring over the ball: its radius (to the middle of the planks), width, and height (the top decks'
        /// level - far above the ball's glass dome, MapBuilder.DomeRadius 11 m).</summary>
        const float RingR = 8f, RingW = 2.4f;
        static float RingY => LevelY(2);

        /// <summary>Everything up in the air (decks, bridges, ramps, the sky ring, the stilt house: every team's copy) as
        /// flat segments with a half-width - no tree grows up through them (SpotOk). Filled in by BuildProps.</summary>
        static readonly List<(Vector2 a, Vector2 b, float r)> s_Lanes = new List<(Vector2, Vector2, float)>();

        public override bool SpotOk(Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            foreach (var (a, b, r) in s_Lanes)
                if (SegDist(q, a, b) < r) return false;
            return true;
        }

        /// <summary>How far point q is from the segment a-b (flat).</summary>
        static float SegDist(Vector2 q, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - q).magnitude;
        }

        static readonly Color Moss = new Color(0.3f, 0.46f, 0.2f), Dirt = new Color(0.43f, 0.35f, 0.23f), Cliff = new Color(0.52f, 0.5f, 0.47f), DarkMoss = new Color(0.24f, 0.38f, 0.16f);
        static readonly Color PlankC = new Color(0.66f, 0.42f, 0.3f), PlankDark = new Color(0.48f, 0.3f, 0.2f), Bark = new Color(0.4f, 0.32f, 0.22f);
        static readonly Color Leaf = new Color(0.16f, 0.42f, 0.16f), Leaf2 = new Color(0.22f, 0.5f, 0.18f), RedWood = new Color(0.82f, 0.3f, 0.2f);
        static readonly Color RuinStone = new Color(0.58f, 0.57f, 0.5f), Fern = new Color(0.26f, 0.52f, 0.2f);

        public override float Height(float x, float z)
        {
            float s = ThemeMaps.SeedP, half = Cfg.MapHalf;
            float hills = Mathf.Max(0f, (TmKit.SymN(x, z, 0.035f, s) - 0.42f) * 6f);
            float bumps = (ThemeMaps.SymNoiseP(x, z, 0.12f, s + 20f) - 0.5f) * 0.6f;
            float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float cliff = ThemeMaps.SmoothStepP(half - 16f, half - 5f, e) * (14f + ThemeMaps.SymNoiseP(x, z, 0.07f, s + 40f) * 12f);
            return (hills + bumps + cliff) * ThemeMaps.MaskP(x, z);
        }

        public override Color[] Palette => new[] { Moss, Dirt, Cliff, DarkMoss };
        public override int ColourAt(Vector3 c, float slopeY)
        {
            if (slopeY < 0.72f || c.y > 5f) return 2;
            float n = ThemeMaps.SymNoiseP(c.x, c.z, 0.09f, ThemeMaps.SeedP + 7f);
            return n > 0.62f ? 1 : n < 0.4f ? 3 : 0;
        }

        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1.45f : kind == ResourceNode.Bush ? 1.3f : 0.8f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.15f, 0.42f, 0.15f), 0.5f);

        /// <summary>The trees' trunk radius from the ground up past 3 m (straight and round there: the weak spot X sits on it).</summary>
        const float TrunkR = 0.35f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        /// <summary>Tall thin trees: a straight round trunk (exactly TrunkR, no branches below 3 m) on low flared roots,
        /// branches reaching up with clumps of leaves along them, vines hanging.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 31);
            float R() => (float)rng.NextDouble();
            var bark = TmKit.Shade(Color.Lerp(Bark, new Color(0.5f, 0.42f, 0.3f), R()), 1f);
            var leaf = TmKit.Shade(Color.Lerp(Leaf, Leaf2, R()), 1f);
            float H = Mathf.Max(h * (1.6f + R() * 0.5f), 7.2f);
            Art.Part(tr, Art.Cylinder, bark, new Vector3(0, H * 0.3f, 0), new Vector3(TrunkR * 2f, H * 0.3f, TrunkR * 2f));
            Art.Part(tr, Art.Cylinder, TmKit.Shade(bark, 0.95f), new Vector3(0, H * 0.78f, 0), new Vector3(0.48f, H * 0.2f, 0.48f));
            // roots: low fins flaring out over the ground from the trunk's foot (all under 0.3 m: clear of the X)
            float r0 = R() * 360f;
            for (int k = 0; k < 4; k++)
            {
                var o = Quaternion.Euler(0, r0 + k * 90f + R() * 20f, 0);
                Art.Box(tr, TmKit.Shade(bark, 0.9f), o * new Vector3(0, 0.12f, TrunkR + 0.35f), new Vector3(0.14f, 0.24f, 0.8f), o.eulerAngles);
            }
            int branches = 4 + rng.Next(2);
            float yaw0 = R() * 360f;
            for (int b = 0; b < branches; b++)
            {
                float y = Mathf.Max(3.2f, H * Mathf.Lerp(0.42f, 0.92f, b / (float)(branches - 1))); // (no branch below 3.2 m)
                float yaw = yaw0 + b * 137f, tilt = 50f + R() * 22f, len = 2f + R() * 1.6f;
                var dir = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(tilt, 0, 0) * Vector3.up;
                var a = new Vector3(0, y, 0);
                var e = a + dir * len;
                Art.Part(tr, Art.Cylinder, bark, (a + e) * 0.5f, new Vector3(0.18f, len * 0.5f, 0.18f), Quaternion.FromToRotation(Vector3.up, dir).eulerAngles);
                Art.Part(tr, TmKit.Blob(seed + b), TmKit.Shade(leaf, (0.85f + R() * 0.25f)), e, new Vector3(1.9f, 0.7f, 1.5f), new Vector3(R() * 20f, yaw, R() * 20f));
                if (R() < 0.6f) Art.Part(tr, TmKit.Blob(seed + b + 3), leaf, a + dir * len * 0.55f + Vector3.up * 0.2f, new Vector3(1.2f, 0.5f, 1.1f), new Vector3(0, yaw + 40f, 0));
                if (b % 2 == 0) { float vl = 1.5f + R() * 2f; Art.Box(tr, DarkMoss, e + Vector3.down * (vl * 0.5f + 0.2f), new Vector3(0.08f, vl, 0.08f)); } // a vine
            }
            Art.Part(tr, TmKit.Blob(seed + 7), leaf, new Vector3(0, H + 0.4f, 0), new Vector3(2.2f, 1.3f, 2.2f), new Vector3(0, R() * 360f, 0));
            return true;
        }

        /// <summary>The game's berry bush, deep jungle green with yellow fruit (press E for food).</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.16f, 0.42f, 0.16f), new Color(0.98f, 0.84f, 0.2f));
            return true;
        }

        /// <summary>The okapi: the game's horse, dark chocolate-maroon, with white zebra stripes on its rump and back legs,
        /// white stockings, a pale cream face, big ears and two little furry horns (now and then a rare golden one).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var coat = unicorn ? new Color(0.85f, 0.6f, 0.25f) : new Color(0.3f, 0.16f, 0.12f);
            var white = new Color(0.95f, 0.93f, 0.88f);
            var cream = new Color(0.86f, 0.78f, 0.64f);
            var dark = TmKit.Shade(coat, 0.6f);
            var neck = TmKit.Horse(t, ghost, coat, dark, new Color(0.12f, 0.08f, 0.06f), out saddle, out head, out tail, legs, out var legT, new Color(0.15f, 0.1f, 0.08f), 0.22f);
            // the pale cream face (the cheeks, over the head box), big ears and the two little horns
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Box(neck, cream, new Vector3(s * 0.152f, 0.58f, 0.5f), new Vector3(0.01f, 0.22f, 0.34f));
                Art.Box(neck, coat, new Vector3(s * 0.15f, 0.82f, 0.25f), new Vector3(0.14f, 0.2f, 0.04f), new Vector3(0, 0, s * -30f)); // (an ear)
                Art.Box(neck, dark, new Vector3(s * 0.06f, 0.83f, 0.34f), new Vector3(0.05f, 0.14f, 0.05f));
            }
            // white stripes across the rump and round the back legs, white stockings on all four
            for (int k = 0; k < 4; k++)
                for (int s = -1; s <= 1; s += 2)
                    Art.Box(t, white, new Vector3(s * 0.303f, 0.95f + k * 0.11f, -0.58f), new Vector3(0.01f, 0.045f, 0.32f));
            for (int i = 0; i < 4; i++)
            {
                if (i >= 2)
                    for (int k = 0; k < 3; k++)
                        Art.Box(legT[i], white, new Vector3(0, -0.12f - k * 0.12f, 0), new Vector3(0.17f, 0.05f, 0.17f));
                Art.Box(legT[i], white, new Vector3(0, -0.66f, 0), new Vector3(0.17f, 0.2f, 0.17f));
            }
            // the thin tail with a dark tuft
            Art.Box(tail, dark, new Vector3(0, -0.62f, -0.06f), new Vector3(0.16f, 0.2f, 0.16f));
            return true;
        }

        // =====================================================================

        readonly TmSky m_Sky = new TmSky();
        public override void ApplySky() => m_Sky.Apply(new Color(0.55f, 0.66f, 0.5f), 0.0065f,
            new Color(0.55f, 0.66f, 0.55f), new Color(0.4f, 0.48f, 0.36f), new Color(0.22f, 0.26f, 0.18f), new Color(1f, 0.95f, 0.82f), 0.95f,
            new Color(0.42f, 0.55f, 0.42f), 1f);
        public override void Cleanup() => m_Sky.Restore();

        public override void BuildProps(Transform root)
        {
            var rng = TmKit.Rng(6200);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var sec = TmKit.Sector(root, "Jungle props");
            float d = Mathf.Abs(Cfg.BaseCenter[0].z), bh = Cfg.BaseHalf, half = Cfg.MapHalf;
            bool two = !Cfg.FourWay;

            // ---- the climbing trees: most in a ring round the middle (three decks high), a second ring a bit further
            //      out (two decks), and one low one by each side of the base ----
            bool Ok(Vector3 p) => TmKit.FreeSpot(p, Halfs[0] + 1.5f) && ThemeMaps.Height(p.x, p.z) < 3.2f;
            var nodes = new List<Vector3>();
            var lvls = new List<int>();
            int Add(Vector3 p, int lv)
            {
                for (int i = 0; i < nodes.Count; i++) if ((nodes[i] - p).sqrMagnitude < 1f) { lvls[i] = Mathf.Max(lvls[i], lv); return i; }
                nodes.Add(p); lvls.Add(lv);
                return nodes.Count - 1;
            }
            int Tower(Vector3 p, int lv) => Ok(p) ? Add(p, lv) : -1;
            // (a = this sector's tree, c = the other end; k: c's copy in team k's sector, 0 = this one)
            var links = new List<(int a, int c, int lv, int k)>();
            // (no bridge over a base)
            bool OverBase(Vector3 a, Vector3 b)
            {
                var bc = Cfg.BaseCenter[0];
                for (float t = 0f; t <= 1f; t += 0.04f)
                {
                    var q = Vector3.Lerp(a, b, t);
                    if (Mathf.Abs(q.x - bc.x) < bh + 3f && Mathf.Abs(q.z - bc.z) < bh + 3f) return true;
                }
                return false;
            }
            // a bridge's open span between the two decks' edges: long enough to be a bridge, short enough to cross
            bool SpanOk(Vector3 pa, Vector3 pb, int lv)
            {
                float span = (pa - pb).magnitude - Halfs[lv] * 2f;
                return span > 2.5f && span < 28f;
            }
            void Link(int a, int b, int lv, int depth = 0)
            {
                if (a < 0 || b < 0 || a == b) return;
                if (lv >= Mathf.Min(lvls[a], lvls[b])) return;
                var pa = nodes[a]; var pb = nodes[b];
                if (OverBase(pa, pb)) return;
                if ((pa - pb).magnitude > 34f && depth < 3 && lv == 0)
                {
                    var m = (pa + pb) * 0.5f;
                    if (Ok(m)) { int mi = Add(m, 1); Link(a, mi, 0, depth + 1); Link(mi, b, 0, depth + 1); return; }
                }
                if (!SpanOk(pa, pb, lv)) return;
                links.Add((a, b, lv, 0));
            }
            // which other team's copy of tree c is nearest point p
            int NearK(int c, Vector3 p)
            {
                int best = 1;
                float bd = float.MaxValue;
                for (int k = 1; k < Cfg.Copies; k++)
                {
                    float dd = (Cfg.Copy(nodes[c], k) - p).sqrMagnitude;
                    if (dd < bd - 0.01f) { bd = dd; best = k; }
                }
                return best;
            }
            void LinkExt(int a, int c, int lv)
            {
                if (a < 0 || c < 0 || lv >= Mathf.Min(lvls[a], lvls[c])) return;
                int k = NearK(c, nodes[a]);
                if (!SpanOk(nodes[a], Cfg.Copy(nodes[c], k), lv)) return;
                links.Add((a, c, lv, k));
            }

            float rIn = 27f, rMid = Mathf.Max(rIn + 14f, (rIn + d - bh) * 0.55f);
            int inL = Tower(TmKit.CentreSpot(-0.17f, rIn), 3), inR = Tower(TmKit.CentreSpot(0.17f, rIn), 3);
            int midC = Tower(TmKit.CentreSpot(0f, rMid), 2);
            int outL = -1, outR = -1, midL = -1, midR = -1;
            if (two)
            {
                outL = Tower(TmKit.CentreSpot(-0.4f, rIn), 3); outR = Tower(TmKit.CentreSpot(0.4f, rIn), 3);
                midL = Tower(TmKit.CentreSpot(-0.3f, rMid), 2); midR = Tower(TmKit.CentreSpot(0.3f, rMid), 2);
            }
            int sideL = Tower(new Vector3(-(bh + 11f), 0, -d + 4f), 1), sideR = Tower(new Vector3(bh + 11f, 0, -d + 4f), 1);
            Link(inL, inR, 1);                      // the high bridge across in front of the ball
            Link(inL, midC, 0); Link(inR, midC, 0);
            if (two)
            {
                Link(inL, outL, 0); Link(inR, outR, 0);
                LinkExt(outR, outL, 1);             // high across to the other team's side
                Link(midL, outL, 1); Link(midR, outR, 1);
                Link(sideL, midL, 0); Link(sideR, midR, 0);
            }
            else
            {
                LinkExt(inR, inL, 0);               // round to the next team's ring
                Link(sideL, midC, 0); Link(sideR, midC, 0);
            }

            // what's up in the air, for SpotOk (every team's copy)
            s_Lanes.Clear();
            void Lane(Vector3 a, Vector3 b, float r)
            {
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var qa = Cfg.Copy(a, m); var qb = Cfg.Copy(b, m);
                    s_Lanes.Add((new Vector2(qa.x, qa.z), new Vector2(qb.x, qb.z), r));
                }
            }
            Vector2 V2(Vector3 v) => new Vector2(v.x, v.z);
            // every bridge / walkway / ramp leaving each tree, per deck level, as a flat segment from the tree's middle out
            // (the ladders keep well clear of them)
            var segs = new List<(Vector2 a, Vector2 b)>[nodes.Count, 3];
            for (int i = 0; i < nodes.Count; i++) for (int l = 0; l < 3; l++) segs[i, l] = new List<(Vector2, Vector2)>();
            Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }
            foreach (var (a, c, lv, k) in links)
            {
                var pa = nodes[a];
                var pb = k == 0 ? nodes[c] : Cfg.Copy(nodes[c], k);
                var dir = Flat(pb - pa);
                Bridge(sec, pa + dir * Halfs[lv], pb - dir * Halfs[lv], LevelY(lv));
                Lane(pa, pb, 3.5f);
                segs[a, lv].Add((V2(pa), V2(pb)));
                // (an ext bridge's copy turned into blue's sector arrives at c from a's copy there)
                var src = k == 0 ? pa : Cfg.Copy(pa, Cfg.Copies - k);
                segs[c, lv].Add((V2(nodes[c]), V2(src)));
            }

            // ---- the sky ring over the ball, and walkways in to it from the top decks of the nearest trees ----
            var spokes = new List<int>();
            foreach (int t in new[] { inL, inR }) if (t >= 0 && lvls[t] >= 3) spokes.Add(t);
            if (spokes.Count > 0)
            {
                SkyRing(sec);
                for (int n = 0; n < 16; n++)
                {
                    float a0 = n * 22.5f, a1 = a0 + 22.5f;
                    var p0 = RingPoint(a0, RingR); var p1 = RingPoint(a1, RingR);
                    if (Cfg.InFirstSector((p0 + p1) * 0.5f, 0f)) Lane(p0, p1, RingW * 0.5f + 1.5f);
                }
                foreach (int t in spokes)
                {
                    var pt = nodes[t];
                    var dir = Flat(-pt);
                    var end = new Vector3(0, 0, 0) - dir * (RingR + RingW * 0.5f - 0.3f);
                    Bridge(sec, pt + dir * Halfs[2], end, RingY);
                    Lane(pt, end, 3.5f);
                    segs[t, 2].Add((V2(pt), V2(end)));
                }
            }

            var dirs = new Vector3[4];
            for (int i = 0; i < 4; i++) dirs[i] = Quaternion.Euler(0, i * 90f, 0) * Vector3.forward;
            var feet = new List<Vector3>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var p = nodes[i];
                int L = lvls[i];
                DeckTree(sec, p, L, rng);
                Lane(p, p, Halfs[0] + 2f);
                // a spot is clear of every bridge / walkway / ramp at these deck levels (by `gap` from its middle line)
                bool Clear(Vector2 q, int lv, float gap)
                {
                    for (int l = Mathf.Max(0, lv - 1); l <= lv; l++)
                        foreach (var (a, b) in segs[i, l]) if (SegDist(q, a, b) < gap) return false;
                    return true;
                }
                int start = rng.Next(4);
                // up from the ground: a ramp too on the low trees by the bases (straight out of the middle of a side)
                if (Mathf.Abs(p.z) > d * 0.8f)
                    for (int k = 0; k < 4; k++)
                    {
                        var dv = dirs[(start + k) % 4];
                        var top = p + dv * Halfs[0];
                        if (!Clear(V2(top + dv * 1.5f), 0, 3.5f)) continue;
                        float len = DeckY / Mathf.Tan(24f * Mathf.Deg2Rad);
                        var foot = top + dv * len;
                        if (!TmKit.FreeSpot(foot, 1f) || ThemeMaps.Height(foot.x, foot.z) > 2f) continue;
                        Ramp(sec, top, foot);
                        Lane(top, foot, 2f);
                        segs[i, 0].Add((V2(p), V2(foot)));
                        feet.Add(foot);
                        break;
                    }
                // a ladder up to every deck (from the ground, then from the deck below): always up the face of a deck
                // (square to its side, never at a corner, so its top is right at the deck's open edge), clear of every
                // bridge and walkway there, and never where the ladder below tops out (so you step off onto open deck)
                var lastTop = new Vector2(float.MaxValue, float.MaxValue);
                for (int lv = 0; lv < L; lv++)
                {
                    float H = Halfs[lv];
                    bool done = false;
                    for (int pass = 0; pass < 2 && !done; pass++)
                    {
                        float gap = pass == 0 ? 2.2f : 1.6f;
                        for (int k = 0; k < 12 && !done; k++)
                        {
                            var dv = dirs[(start + k / 3) % 4];
                            var side = new Vector3(dv.z, 0, -dv.x);
                            float off = (k % 3 == 0 ? 0f : k % 3 == 1 ? 0.5f : -0.5f) * H;
                            var foot = p + dv * H + side * off;
                            var climb = V2(foot + dv * 0.62f);
                            if (!Clear(climb, lv, gap) || !Clear(V2(foot), lv, gap)) continue;
                            if (pass == 0 && (V2(foot) - lastTop).magnitude < 2.4f) continue;
                            foot.y = lv == 0 ? ThemeMaps.Height(foot.x, foot.z) : LevelY(lv - 1);
                            if (lv == 0 && (foot.y > DeckY - 2f || ThemeMaps.Height(climb.x, climb.y) > foot.y + 0.6f)) continue;
                            TmKit.Ladder(sec, foot, LevelY(lv), dv, PlankDark);
                            segs[i, lv].Add((V2(foot), V2(foot + dv * 1.2f))); // (the ladder above keeps off it too)
                            if (lv == 0) feet.Add(foot + dv * 1.5f);
                            lastTop = V2(foot);
                            start = (start + k / 3 + 1) % 4; // (the next one round the next side if it can)
                            done = true;
                        }
                    }
                }
            }

            // ---- a red treehouse on stilts, and a roofed hut, out to the sides ----
            var spots = new List<Vector3>(nodes);
            for (int k = 0; k < 2; k++)
            {
                for (int tries = 0; tries < 80; tries++)
                {
                    var p = new Vector3(Rn(-half + 12f, half - 12f), 0, Rn(-d * 1.1f, -30f));
                    if (!TmKit.FreeSpot(p, 6f) || ThemeMaps.Height(p.x, p.z) > 2.5f || !SpotOk(p)) continue; // (not under a walkway)
                    bool near = false;
                    foreach (var n in spots) if ((n - p).magnitude < 14f) near = true;
                    if (near) continue;
                    p.y = ThemeMaps.Height(p.x, p.z);
                    if (k == 0) StiltHouse(sec, p, Rn(0, 360));
                    else Hut(sec, p, Rn(0, 360));
                    spots.Add(p);
                    Lane(p, p, 5f);
                    break;
                }
            }

            // ---- mossy jungle rocks all over the floor (thicker towards the middle), alone or in twos and threes ----
            int want = Mathf.RoundToInt(16 * half / 100f);
            for (int i = 0, made = 0; i < want * 40 && made < want; i++)
            {
                float r = Mathf.Lerp(23f, d + bh, Mathf.Pow(Rn(0f, 1f), 1.4f));
                var p = TmKit.CentreSpot(Rn(-0.48f, 0.48f), r);
                if (!TmKit.FreeSpot(p, 3f) || ThemeMaps.Height(p.x, p.z) > 3f) continue;
                bool near = false;
                foreach (var n in spots) if ((n - p).magnitude < Halfs[0] + 4.5f) near = true;
                foreach (var f in feet) if ((f - p).magnitude < 3.5f) near = true;
                if (near) continue;
                made++;
                spots.Add(p);
                int n2 = 1 + rng.Next(3);
                for (int k = 0; k < n2; k++)
                {
                    var q = p + (k == 0 ? Vector3.zero : Quaternion.Euler(0, Rn(0, 360), 0) * new Vector3(0, 0, Rn(1.6f, 2.4f)));
                    q.y = ThemeMaps.Height(q.x, q.z);
                    MossRock(sec, q, k == 0 ? Rn(1.4f, 2.8f) : Rn(0.8f, 1.4f), rng);
                }
            }
            TmKit.CopyRound(sec);

            // ---- the cliff walls closing the jungle in (outside the edge, so nothing to collide with) ----
            for (int side = 0; side < 4; side++)
            {
                int n = Mathf.CeilToInt(2f * half / 13f) + 2;
                for (int i = 0; i < n; i++)
                {
                    float along = -half - 13f + i * 13f + Rn(-3f, 3f), out_ = half + Rn(7f, 14f);
                    float w = Rn(14f, 22f), hgt = Rn(26f, 40f) * Mathf.Max(0.8f, half / 100f);
                    var at = side == 0 ? new Vector3(along, 0, out_) : side == 1 ? new Vector3(along, 0, -out_) : side == 2 ? new Vector3(out_, 0, along) : new Vector3(-out_, 0, along);
                    at.y = hgt * 0.3f;
                    var rk = Art.Part(root, TmKit.Blob(i + side * 3), Color.Lerp(Cliff, new Color(0.44f, 0.42f, 0.4f), Rn(0, 1)), at, new Vector3(w, hgt, w * 0.8f), new Vector3(0, Rn(0, 360), 0));
                    rk.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    if (i % 3 == 0)
                    {
                        var moss = Art.Part(root, TmKit.Blob(i + 5), DarkMoss, at + Vector3.up * hgt * 0.45f, new Vector3(w * 0.7f, hgt * 0.15f, w * 0.6f));
                        moss.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
            }
        }

        /// <summary>A mossy jungle rock (solid cover), a cap of moss on top and now and then a fern at its foot.</summary>
        static void MossRock(Transform sec, Vector3 p, float sc, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            int m = rng.Next(8);
            Art.Part(sec, TmKit.Rock(m), TmKit.Shade(Cliff, 0.88f + R() * 0.2f), p + Vector3.up * sc * 0.38f, new Vector3(sc * 1.3f, sc, sc * 1.1f), new Vector3(0, R() * 360f, 0), true);
            Art.Part(sec, TmKit.Blob(m + 1), DarkMoss, p + Vector3.up * sc * 0.82f, new Vector3(sc * 1.05f, sc * 0.32f, sc * 0.85f), new Vector3(0, R() * 360f, 0));
            if (R() < 0.5f) FernClump(sec, p + Quaternion.Euler(0, R() * 360f, 0) * new Vector3(0, 0, sc * 0.75f), rng);
        }

        /// <summary>A few fern fronds fanning out of the ground (no collider).</summary>
        static void FernClump(Transform sec, Vector3 p, System.Random rng)
        {
            float a0 = (float)rng.NextDouble() * 360f;
            for (int k = 0; k < 5; k++)
            {
                var o = Quaternion.Euler(0, a0 + k * 72f, 0);
                TmKit.Rod(sec, k % 2 == 0 ? Fern : TmKit.Shade(Fern, 1.15f), p, p + o * new Vector3(0, 0.55f, 0.75f), 0.3f, 0.03f);
            }
        }

        /// <summary>The middle: mossy boulders, a fallen log, a stump with ferns and a crumbling carved stone wall round the ball.</summary>
        public override bool BuildCentre(Transform root)
        {
            var spots = new (float f, float r)[] { (-0.3f, 7.5f), (0.27f, 8.5f), (0f, 12f), (-0.15f, 15f), (0.38f, 13f), (-0.4f, 11.5f), (0.15f, 16.5f) };
            TmKit.CentreLayout(root, "Jungle centre", 6250, spots, 17f, 5f, (sec, p, yaw, i, rng) =>
            {
                switch (i)
                {
                    case 0: case 4:
                        MossRock(sec, p, 1.8f + (float)rng.NextDouble() * 0.5f, rng);
                        MossRock(sec, p + Quaternion.Euler(0, yaw + 90f, 0) * new Vector3(0, 0, 1.9f), 1.1f, rng);
                        break;
                    case 1: case 6: FallenLog(sec, p, yaw + 90f); break;
                    case 2: RuinWall(sec, p, yaw); break;
                    case 3:
                        Art.Part(sec, Art.Cylinder, Bark, p + Vector3.up * 0.55f, new Vector3(1.4f, 0.65f, 1.4f), default, true);
                        Art.Part(sec, TmKit.Blob(2), DarkMoss, p + Vector3.up * 1.2f, new Vector3(1.4f, 0.25f, 1.4f));
                        FernClump(sec, p + Quaternion.Euler(0, yaw, 0) * new Vector3(1.1f, 0, 0), rng);
                        FernClump(sec, p + Quaternion.Euler(0, yaw, 0) * new Vector3(-0.9f, 0, 0.6f), rng);
                        break;
                    default: // a pair of broken carved pillars
                        for (int s = -1; s <= 1; s += 2)
                        {
                            var c = p + Quaternion.Euler(0, yaw + 90f, 0) * new Vector3(0, 0, s * 1.4f);
                            float ph = s < 0 ? 2.6f : 1.5f;
                            Art.Box(sec, RuinStone, c + Vector3.up * (ph * 0.5f - 0.1f), new Vector3(1f, ph, 1f), new Vector3(0, yaw + s * 6f, 0), true);
                            Art.Box(sec, TmKit.Shade(RuinStone, 0.85f), c + Vector3.up * (ph - 0.05f), new Vector3(1.15f, 0.2f, 1.15f), new Vector3(0, yaw, 0));
                            Art.Box(sec, DarkMoss, c + Vector3.up * (ph + 0.08f), new Vector3(0.9f, 0.1f, 0.9f), new Vector3(0, yaw + 20f, 0));
                        }
                        break;
                }
            });
            return true;
        }

        static void FallenLog(Transform sec, Vector3 p, float yaw)
        {
            var o = Quaternion.Euler(0, yaw, 0);
            var a = p + o * new Vector3(0, 0, -2.6f); var b = p + o * new Vector3(0, 0, 2.6f);
            a.y = ThemeMaps.Height(a.x, a.z) + 0.5f; b.y = ThemeMaps.Height(b.x, b.z) + 0.5f;
            var log = TmKit.Rod(sec, Bark, a, b, 1f, 1f, false, Art.Cylinder);
            log.AddComponent<BoxCollider>().size = new Vector3(0.95f, 2f, 0.95f);
            Art.Part(sec, TmKit.Blob(4), DarkMoss, (a + b) * 0.5f + Vector3.up * 0.45f, new Vector3(1.6f, 0.25f, 0.8f), new Vector3(0, yaw + 90f, 0));
            Art.Part(sec, Art.Cylinder, TmKit.Shade(Bark, 1.25f), b + o * new Vector3(0, 0, 0.02f), new Vector3(0.85f, 0.02f, 0.85f), (o * Quaternion.Euler(90f, 0, 0)).eulerAngles); // the cut end
        }

        /// <summary>A crumbling carved stone wall: a high middle, broken lower ends, moss and vines on it.</summary>
        static void RuinWall(Transform sec, Vector3 p, float yaw)
        {
            var o = Quaternion.Euler(0, yaw + 90f, 0);
            float[] hs = { 1.3f, 2.4f, 2.1f, 1.0f };
            for (int k = 0; k < 4; k++)
            {
                var c = p + o * new Vector3(0, 0, (k - 1.5f) * 1.1f);
                Art.Box(sec, k % 2 == 0 ? RuinStone : TmKit.Shade(RuinStone, 0.9f), c + Vector3.up * (hs[k] * 0.5f - 0.1f), new Vector3(0.8f, hs[k], 1.1f), new Vector3(0, yaw + 90f + (k - 1.5f) * 3f, 0), true);
                Art.Box(sec, TmKit.Shade(RuinStone, 0.75f), c + Vector3.up * (hs[k] * 0.55f) + o * new Vector3(0.41f, 0, 0), new Vector3(0.04f, 0.3f, 0.8f), new Vector3(0, yaw + 90f, 0)); // a carved band
            }
            Art.Part(sec, TmKit.Blob(5), DarkMoss, p + Vector3.up * 2.35f + o * new Vector3(0, 0, -0.5f), new Vector3(1.2f, 0.3f, 1.4f), new Vector3(0, yaw, 0));
            for (int k = 0; k < 3; k++)
            {
                float vl = 0.8f + k * 0.5f;
                Art.Box(sec, DarkMoss, p + o * new Vector3(0.43f, 0, -1f + k * 0.8f) + Vector3.up * (2.1f - vl * 0.5f), new Vector3(0.06f, vl, 0.06f), new Vector3(0, yaw, 0));
            }
        }

        /// <summary>A giant tree through `levels` plank decks (5.5 m apart, each smaller than the one below), its canopy
        /// above the top one and vines hanging off the lowest.</summary>
        static void DeckTree(Transform sec, Vector3 p, int levels, System.Random rng)
        {
            float g = ThemeMaps.Height(p.x, p.z);
            float y0 = LevelY(0), topDeck = LevelY(levels - 1), top = topDeck + 6.5f;
            Art.Part(sec, Art.Cylinder, Bark, new Vector3(p.x, (g - 0.5f + y0) * 0.5f, p.z), new Vector3(2.0f, (y0 - g + 0.5f) * 0.5f, 2.0f), default, true);
            Art.Part(sec, Art.Cylinder, TmKit.Shade(Bark, 0.92f), new Vector3(p.x, (y0 + top) * 0.5f, p.z), new Vector3(1.4f, (top - y0) * 0.5f, 1.4f), default, true);
            Art.Part(sec, Art.Cylinder, TmKit.Shade(Bark, 0.9f), new Vector3(p.x, g + 0.4f, p.z), new Vector3(3f, 0.5f, 3f));
            for (int lv = 0; lv < levels; lv++)
            {
                float y = LevelY(lv), hh = Halfs[lv];
                // the deck, with a darker rim under it
                Art.Box(sec, lv % 2 == 0 ? PlankC : TmKit.Shade(PlankC, 0.94f), new Vector3(p.x, y - 0.18f, p.z), new Vector3(hh * 2f, 0.36f, hh * 2f), default, true);
                Art.Box(sec, PlankDark, new Vector3(p.x, y - 0.4f, p.z), new Vector3(hh * 2f + 0.2f, 0.14f, hh * 2f + 0.2f));
                if (lv == 0)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var c = p + Quaternion.Euler(0, 45 + i * 90, 0) * new Vector3(0, 0, hh * 1.15f);
                        float cg = ThemeMaps.Height(c.x, c.z);
                        Art.Part(sec, Art.Cylinder, PlankDark, new Vector3(c.x, (cg + y) * 0.5f - 0.2f, c.z), new Vector3(0.3f, (y - cg) * 0.5f, 0.3f), new Vector3(8f * Mathf.Cos(i), 0, 8f * Mathf.Sin(i))); // braces
                    }
                }
                else
                {
                    // struts from the trunk out under the deck's corners
                    for (int i = 0; i < 4; i++)
                    {
                        var o = Quaternion.Euler(0, 45 + i * 90, 0);
                        var a = new Vector3(p.x, y - 2f, p.z) + o * new Vector3(0, 0, 0.6f);
                        var b = new Vector3(p.x, y - 0.45f, p.z) + o * new Vector3(0, 0, hh * 1.25f);
                        TmKit.Rod(sec, PlankDark, a, b, 0.2f, 0.2f);
                    }
                }
            }
            // canopy and branches
            for (int b = 0; b < 5; b++)
            {
                float yaw = b * 72f + (float)rng.NextDouble() * 30f;
                var dir = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(55f, 0, 0) * Vector3.up;
                var a = new Vector3(p.x, top - 2.5f + b * 0.4f, p.z);
                var e = a + dir * 4.5f;
                Art.Part(sec, Art.Cylinder, Bark, (a + e) * 0.5f, new Vector3(0.45f, 2.25f, 0.45f), Quaternion.FromToRotation(Vector3.up, dir).eulerAngles);
                Art.Part(sec, TmKit.Blob(b), b % 2 == 0 ? Leaf : Leaf2, e + Vector3.up * 0.4f, new Vector3(4.2f, 1.8f, 3.6f), new Vector3(0, yaw, 0));
            }
            Art.Part(sec, TmKit.Blob(6), Leaf2, new Vector3(p.x, top + 1.2f, p.z), new Vector3(5.5f, 2.6f, 5.5f));
            // vines
            for (int v = 0; v < 4; v++)
            {
                var c = p + Quaternion.Euler(0, v * 90 + 45, 0) * new Vector3(0, 0, Halfs[0] * 1.3f); // (at the corners, clear of the ladders)
                float len = 1.2f + (float)rng.NextDouble() * 1.6f;
                Art.Box(sec, DarkMoss, new Vector3(c.x, y0 - 0.3f - len * 0.5f, c.z), new Vector3(0.1f, len, 0.1f));
            }
        }

        /// <summary>A flat plank bridge between two decks at height y, with rope rails.</summary>
        static void Bridge(Transform sec, Vector3 a, Vector3 b, float y)
        {
            a.y = b.y = y;
            var dir = (b - a).normalized;
            a -= dir * 0.4f; b += dir * 0.4f;
            TmKit.Plank(sec, a, b, 2.2f, 0.25f, PlankC, true);
            var side = new Vector3(dir.z, 0, -dir.x);
            float len = (b - a).magnitude;
            for (int s = -1; s <= 1; s += 2)
            {
                TmKit.Plank(sec, a + side * (s * 1.05f) + Vector3.up * 1.0f, b + side * (s * 1.05f) + Vector3.up * 1.0f, 0.06f, 0.06f, new Color(0.75f, 0.68f, 0.5f), false);
                for (float t = 0; t <= len + 0.01f; t += Mathf.Max(4f, len / Mathf.Ceil(len / 5f)))
                    Art.Box(sec, PlankDark, a + dir * t + side * (s * 1.05f) + Vector3.up * 0.5f, new Vector3(0.14f, 1.1f, 0.14f));
            }
            // cross slats for the plank look
            for (float t = 0.8f; t < len; t += 2.4f)
                Art.Box(sec, PlankDark, a + dir * t + Vector3.up * 0.005f, new Vector3(2.2f, 0.02f, 0.12f), new Vector3(0, Quaternion.LookRotation(dir).eulerAngles.y, 0));
        }

        /// <summary>A point on the sky ring: `deg` round from blue's side (-z), r from the middle, at the ring's height.</summary>
        static Vector3 RingPoint(float deg, float r)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * r, RingY, -Mathf.Cos(a) * r);
        }

        /// <summary>The sky ring right over the ball: sixteen plank sections in a circle RingR out, RingY up (the top decks'
        /// height - far above the ball's glass dome), open in the middle so the ball drops straight through, a rope rail
        /// round its inside edge and vines hanging off it. Built in blue's sector (the sections whose middles are in it);
        /// the copies make the rest. The walkways from the trees join its outside edge.</summary>
        static void SkyRing(Transform sec)
        {
            for (int n = 0; n < 16; n++)
            {
                float a0 = n * 22.5f, a1 = a0 + 22.5f;
                var p0 = RingPoint(a0, RingR); var p1 = RingPoint(a1, RingR);
                var mid = (p0 + p1) * 0.5f;
                if (!Cfg.InFirstSector(mid, 0f)) continue;
                var dir = (p1 - p0).normalized;
                // (each section a little longer than its chord, so the corners meet without a gap)
                TmKit.Plank(sec, p0 - dir * 0.32f, p1 + dir * 0.32f, RingW, 0.3f, PlankC, true);
                var underA = p0 - dir * 0.32f + Vector3.down * 0.3f; var underB = p1 + dir * 0.32f + Vector3.down * 0.3f;
                TmKit.Plank(sec, underA, underB, RingW + 0.15f, 0.12f, PlankDark, false);
                var inward = new Vector3(-mid.x, 0, -mid.z).normalized;
                Art.Box(sec, PlankDark, mid + Vector3.up * 0.005f, new Vector3(RingW, 0.02f, 0.12f), new Vector3(0, Quaternion.LookRotation(dir).eulerAngles.y + 90f, 0));
                // the rope rail round the inside edge (no collider: like the bridges' rails)
                var ri = inward * (RingW * 0.5f - 0.1f);
                TmKit.Plank(sec, p0 + ri + Vector3.up * 1f, p1 + ri + Vector3.up * 1f, 0.06f, 0.06f, new Color(0.75f, 0.68f, 0.5f), false);
                Art.Box(sec, PlankDark, p0 + ri + Vector3.up * 0.5f, new Vector3(0.14f, 1.1f, 0.14f));
                // a vine or two hanging off its outside edge
                if (n % 2 == 0)
                {
                    float vl = 2f + (n % 3) * 0.8f;
                    Art.Box(sec, DarkMoss, mid - inward * (RingW * 0.5f - 0.15f) + Vector3.down * (0.35f + vl * 0.5f), new Vector3(0.1f, vl, 0.1f));
                }
            }
        }

        /// <summary>A plank ramp from a deck edge (top) down to the ground (foot), on posts.</summary>
        static void Ramp(Transform sec, Vector3 top, Vector3 foot)
        {
            top.y = DeckY;
            foot.y = ThemeMaps.Height(foot.x, foot.z) - 0.05f;
            var dir = (foot - top);
            dir.y = 0;
            dir.Normalize();
            TmKit.Plank(sec, top - dir * 0.4f, foot + dir * 0.6f, 2.2f, 0.3f, PlankC, true);
            float len = new Vector2(foot.x - top.x, foot.z - top.z).magnitude;
            for (float t = 2.5f; t < len - 1f; t += 3f)
            {
                var c = top + dir * t;
                float y = Mathf.Lerp(top.y, foot.y, t / len), g = ThemeMaps.Height(c.x, c.z);
                if (y - g < 0.4f) continue;
                Art.Box(sec, PlankDark, new Vector3(c.x, (y + g) * 0.5f - 0.2f, c.z), new Vector3(0.25f, y - g, 0.25f));
            }
        }

        /// <summary>The red treehouse on stilts (like the picture): floor 3 m up, a door and a window, a porch in front of
        /// the door and a ladder up to the porch's open edge.</summary>
        static void StiltHouse(Transform sec, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var e = new Vector3(0, yaw, 0);
            float fy = p.y + 3f, w = 3.6f, wallH = 2.6f, porch = 1.4f;
            Vector3 L(float x, float y, float z) => p + rot * new Vector3(x, 0, z) + Vector3.up * y;
            for (int i = 0; i < 6; i++)
            {
                float sz = i < 2 ? -(w * 0.5f + porch) : i < 4 ? -(w * 0.5f - 0.2f) : w * 0.5f - 0.2f;
                var c = L((i % 2 == 0 ? -1 : 1) * (w * 0.5f - 0.2f), 0, sz + (i < 2 ? 0.25f : 0f));
                float g = ThemeMaps.Height(c.x, c.z);
                Art.Box(sec, TmKit.Shade(RedWood, 0.8f), new Vector3(c.x, (g + fy) * 0.5f - 0.2f, c.z), new Vector3(0.25f, fy - g + 0.2f, 0.25f), e, true);
            }
            // the floor, running on out in front of the door as the porch
            float front = w * 0.5f + 0.3f + porch, back = w * 0.5f + 0.3f;
            Art.Box(sec, TmKit.Shade(RedWood, 0.85f), L(0, fy - 0.12f - p.y, (back - front) * 0.5f), new Vector3(w + 0.6f, 0.24f, front + back), e, true);
            // walls: back, two sides with windows, the front split round a door
            Art.Box(sec, RedWood, L(0, fy + wallH * 0.5f - p.y, w * 0.5f), new Vector3(w, wallH, 0.15f), e, true);
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Box(sec, RedWood, L(s * w * 0.5f, fy + 0.5f - p.y, 0), new Vector3(0.15f, 1f, w), e, true);
                Art.Box(sec, RedWood, L(s * w * 0.5f, fy + wallH - 0.35f - p.y, 0), new Vector3(0.15f, 0.7f, w), e, true);
                Art.Box(sec, RedWood, L(s * w * 0.5f, fy + 1.5f - p.y, s * w * 0.32f), new Vector3(0.15f, 1f, w * 0.36f), e, true);
                Art.Box(sec, RedWood, L(s * w * 0.5f, fy + 1.5f - p.y, -s * w * 0.32f), new Vector3(0.15f, 1f, w * 0.36f), e, true);
                Art.Box(sec, RedWood, L(s * (0.6f + (w * 0.5f - 0.6f) * 0.5f), fy + wallH * 0.5f - p.y, -w * 0.5f), new Vector3(w * 0.5f - 0.6f, wallH, 0.15f), e, true);
                // the roof
                Art.Box(sec, TmKit.Shade(RedWood, 0.7f), L(s * w * 0.27f, fy + wallH + 0.55f - p.y, 0), new Vector3(w * 0.62f, 0.15f, w + 0.6f), new Vector3(0, yaw, -s * 32f), true);
            }
            Art.Box(sec, RedWood, L(0, fy + wallH - 0.25f - p.y, -w * 0.5f), new Vector3(1.2f, 0.5f, 0.15f), e, true); // over the door
            var face = rot * Vector3.back;
            var foot = p + face * front;
            foot.y = ThemeMaps.Height(foot.x, foot.z);
            TmKit.Ladder(sec, foot, fy, face, TmKit.Shade(RedWood, 0.8f));
        }

        /// <summary>A roofed hut: a stone floor, four posts and a tiled roof (cover from above).</summary>
        static void Hut(Transform sec, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var e = new Vector3(0, yaw, 0);
            var stone = new Color(0.82f, 0.8f, 0.76f);
            var tile = new Color(0.55f, 0.42f, 0.42f);
            Art.Box(sec, stone, p + Vector3.up * 0.1f, new Vector3(5f, 0.4f, 6f), e, true);
            for (int i = 0; i < 4; i++)
                Art.Part(sec, Art.Cylinder, stone, p + rot * new Vector3((i % 2 == 0 ? -1 : 1) * 2.1f, 1.5f, (i < 2 ? -1 : 1) * 2.6f), new Vector3(0.3f, 1.3f, 0.3f), e, true);
            for (int s = -1; s <= 1; s += 2)
                Art.Box(sec, tile, p + rot * new Vector3(s * 1.4f, 3.3f, 0), new Vector3(3.4f, 0.18f, 6.8f), new Vector3(0, yaw, -s * 28f), true);
            Art.Box(sec, PlankDark, p + Vector3.up * 4.05f, new Vector3(0.25f, 0.25f, 6.9f), e);
        }
    }
}
