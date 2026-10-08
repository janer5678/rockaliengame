using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// JUNGLE: mossy forest floor closed in by tall grey rock cliffs. Lots of tall thin leafy trees. The middle round the
    /// ball is open (no big trees): instead a ring of plank walkways 5.5 m up on round post platforms circles it, with
    /// walkways branching out from it to the giant climbing trees further out - each with plank decks one above another
    /// (5.5 m a level, up to 16.5 m), ladders from deck to deck (climbable from the front only, each topping out right
    /// at a deck's open edge) and plank bridges between the trees - high ones too; a few lower ones out by the bases (with
    /// a ramp). Mossy jungle rocks all over the floor, a red stilt treehouse and a roofed hut out to the sides, a ruined
    /// stone shrine-wall and boulders round the ball. Banana plants for food, jaguars to ride. Light green haze.
    /// </summary>
    public class JungleMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Jungle;
        public override string Label => "Jungle";
        public override string Blurb => "Thick jungle walled in by rock cliffs. Walkways high over the middle branch out to giant climbing trees - ladders up to decks three storeys high, bridges between them. Mossy rocks for cover below.";
        public override bool Mountains => false;
        public override float MaxSpotHeight => 3f;
        public override string MountName => "Jaguar";

        const float DeckY = 5.5f;
        /// <summary>Each deck level's half size (the higher, the smaller: the ladder up to the next one stands on the one
        /// below, with 1.2 m of floor in front of it to stand on).</summary>
        static readonly float[] Halfs = { 4.4f, 3.2f, 2.0f };
        /// <summary>The round walkway platforms (on posts) round the middle: radius, and how far from the ball.</summary>
        const float HubR = 2.6f, HubDist = 15f;
        /// <summary>No giant trees nearer the ball than this (the middle is open: walkways only).</summary>
        const float TreeMinR = 30f;
        /// <summary>No ordinary trees (or bushes) nearer the ball than this.</summary>
        const float MidClear = 22f;

        /// <summary>The walkway platforms round the middle, in blue's sector (two teams: two, either side; four: one).</summary>
        static Vector3[] HubSpots() => Cfg.FourWay
            ? new[] { TmKit.CentreSpot(0f, HubDist) }
            : new[] { TmKit.CentreSpot(-0.25f, HubDist), TmKit.CentreSpot(0.25f, HubDist) };

        /// <summary>Everything up in the air (walkways, bridges, decks: every team's copy) as flat segments with a
        /// half-width - no tree grows up through them (SpotOk). Filled in by BuildProps.</summary>
        static readonly List<(Vector2 a, Vector2 b, float r)> s_Lanes = new List<(Vector2, Vector2, float)>();

        public override bool SpotOk(Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            if (q.magnitude < MidClear) return false;
            foreach (var (a, b, r) in s_Lanes)
            {
                var ab = b - a;
                float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                if ((a + ab * t - q).magnitude < r) return false;
            }
            return true;
        }
        static float LevelY(int lv) => DeckY * (lv + 1);

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

        /// <summary>Tall thin trees on buttress roots, branches reaching up with clumps of leaves along them, vines hanging.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 31);
            float R() => (float)rng.NextDouble();
            var bark = TmKit.Shade(Color.Lerp(Bark, new Color(0.5f, 0.42f, 0.3f), R()), 1f);
            var leaf = TmKit.Shade(Color.Lerp(Leaf, Leaf2, R()), 1f);
            float H = h * (1.6f + R() * 0.5f);
            Art.Part(tr, Art.Cylinder, bark, new Vector3(0, H * 0.3f, 0), new Vector3(0.7f, H * 0.3f, 0.7f));
            Art.Part(tr, Art.Cylinder, TmKit.Shade(bark, 0.95f), new Vector3(0, H * 0.78f, 0), new Vector3(0.48f, H * 0.2f, 0.48f));
            // buttress roots: thin fins flaring out at the foot
            float r0 = R() * 360f;
            for (int k = 0; k < 4; k++)
            {
                var o = Quaternion.Euler(0, r0 + k * 90f + R() * 20f, 0);
                Art.Box(tr, TmKit.Shade(bark, 0.9f), o * new Vector3(0, 0.55f, 0.55f), new Vector3(0.12f, 1.1f, 0.9f), (o * Quaternion.Euler(-20f, 0, 0)).eulerAngles);
            }
            int branches = 4 + rng.Next(2);
            float yaw0 = R() * 360f;
            for (int b = 0; b < branches; b++)
            {
                float y = H * Mathf.Lerp(0.42f, 0.92f, b / (float)(branches - 1));
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

        /// <summary>A banana plant: a short green stem, big paddle leaves arching out and a hanging bunch of bananas.</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            var rng = new System.Random(seed * 31 + 11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var stem = new Color(0.42f, 0.5f, 0.24f);
            var leafA = new Color(0.25f, 0.56f, 0.2f);
            var leafB = new Color(0.34f, 0.64f, 0.24f);
            void Plant(Vector3 at, float s, int leaves, bool fruit)
            {
                float sh = 1.05f * s;
                Art.Part(tr, Art.Cylinder, stem, at + Vector3.up * sh * 0.5f, new Vector3(0.36f * s, sh * 0.5f, 0.36f * s));
                var top = at + Vector3.up * sh;
                float spin = R(0, 360);
                for (int i = 0; i < leaves; i++)
                {
                    var o = Quaternion.Euler(0, spin + i * 360f / leaves + R(-15f, 15f), 0);
                    var mid = top + o * new Vector3(0, R(0.35f, 0.6f) * s, R(0.75f, 0.95f) * s);
                    var tip = mid + o * new Vector3(0, R(-0.45f, -0.25f) * s, R(0.6f, 0.8f) * s);
                    var c = TmKit.Shade(i % 2 == 0 ? leafA : leafB, R(0.92f, 1.08f));
                    TmKit.Rod(tr, c, top, mid, 0.04f, 0.55f * s);
                    TmKit.Rod(tr, c, mid, tip, 0.04f, 0.5f * s);
                }
                if (!fruit) return;
                // the bunch: a stalk with three tiers of bananas pointing up round it, a purple flower at the bottom
                var stalkTop = top + Vector3.down * 0.05f + Quaternion.Euler(0, spin + 20f, 0) * new Vector3(0, 0, 0.2f);
                var stalkBot = stalkTop + Vector3.down * 0.75f + Quaternion.Euler(0, spin + 20f, 0) * new Vector3(0, 0, 0.18f);
                TmKit.Rod(tr, stem, stalkTop, stalkBot, 0.07f, 0.07f);
                var yellow = new Color(0.98f, 0.85f, 0.25f);
                for (int t = 0; t < 3; t++)
                {
                    var c = Vector3.Lerp(stalkTop, stalkBot, 0.2f + t * 0.25f);
                    for (int k = 0; k < 6; k++)
                    {
                        var o = Quaternion.Euler(0, k * 60f + t * 30f, 0);
                        var a = c + o * new Vector3(0, 0, 0.08f);
                        TmKit.Rod(tr, k % 3 == 0 ? TmKit.Shade(yellow, 0.92f) : yellow, a, a + o * new Vector3(0, 0.22f, 0.13f), 0.08f, 0.08f);
                    }
                }
                Art.Part(tr, Art.Cone, new Color(0.55f, 0.18f, 0.32f), stalkBot + Vector3.up * 0.05f, new Vector3(0.18f, 0.3f, 0.18f), new Vector3(180f, 0, 0));
            }
            Plant(Vector3.zero, 1f, 6, true);
            Plant(new Vector3(R(-0.6f, 0.6f), 0, R(0.5f, 0.7f)), 0.65f, 4, false);
            return true;
        }

        /// <summary>The jaguar: a long golden spotted cat with a long tail, round ears and green eyes.</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var coat = unicorn ? new Color(0.92f, 0.92f, 0.95f) : new Color(0.86f, 0.6f, 0.25f); // (now and then a rare white one)
            var belly = unicorn ? Color.white : new Color(0.97f, 0.88f, 0.7f);
            var spot = unicorn ? new Color(0.3f, 0.3f, 0.35f) : new Color(0.16f, 0.1f, 0.06f);
            TmKit.Hit(Art.Part(t, Art.Sphere, coat, new Vector3(0, 1.12f, 0), new Vector3(0.62f, 0.6f, 1.65f)), ghost);
            Art.Part(t, Art.Sphere, belly, new Vector3(0, 0.97f, 0.05f), new Vector3(0.5f, 0.36f, 1.35f));
            // rosettes along the back and sides
            for (int i = 0; i < 12; i++)
            {
                float z = -0.6f + (i % 6) * 0.24f, side = i < 6 ? -1f : 1f;
                float up = (i % 2 == 0) ? 0.24f : 0.12f;
                Art.Part(t, Art.Sphere, spot, new Vector3(side * (0.29f - up * 0.4f), 1.12f + up, z), new Vector3(0.1f, 0.06f, 0.11f), new Vector3(0, 0, side * (40f + up * 100f)));
            }
            // neck and head (it pivots to sniff about)
            var neck = TmKit.Pivot(t, "neck", new Vector3(0, 1.28f, 0.72f));
            TmKit.Hit(Art.Box(neck, coat, new Vector3(0, 0.08f, 0.12f), new Vector3(0.36f, 0.4f, 0.4f), new Vector3(-20f, 0, 0)), ghost);
            TmKit.HeadBox(neck, coat, new Vector3(0, 0.22f, 0.42f), new Vector3(0.44f, 0.38f, 0.44f), ghost);
            Art.Box(neck, belly, new Vector3(0, 0.12f, 0.68f), new Vector3(0.26f, 0.18f, 0.16f));
            Art.Box(neck, new Color(0.2f, 0.12f, 0.1f), new Vector3(0, 0.2f, 0.77f), new Vector3(0.1f, 0.06f, 0.03f));
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Part(neck, Art.Sphere, coat, new Vector3(s * 0.16f, 0.45f, 0.36f), new Vector3(0.13f, 0.13f, 0.06f));
                Art.Box(neck, new Color(0.55f, 0.75f, 0.2f), new Vector3(s * 0.12f, 0.3f, 0.645f), new Vector3(0.08f, 0.05f, 0.02f));
                Art.Box(neck, Color.black, new Vector3(s * 0.12f, 0.3f, 0.655f), new Vector3(0.02f, 0.05f, 0.02f));
                Art.Part(neck, Art.Sphere, spot, new Vector3(s * 0.18f, 0.3f, 0.5f), new Vector3(0.06f, 0.06f, 0.05f));
            }
            head = neck;
            // the long tail, black at the tip
            var tl = TmKit.Pivot(t, "tail", new Vector3(0, 1.25f, -0.8f));
            TmKit.Hit(Art.Box(tl, coat, new Vector3(0, -0.42f, -0.12f), new Vector3(0.1f, 0.85f, 0.1f), new Vector3(-15f, 0, 0)), ghost);
            Art.Box(tl, spot, new Vector3(0, -0.9f, -0.26f), new Vector3(0.11f, 0.2f, 0.11f), new Vector3(-15f, 0, 0));
            Art.Box(tl, spot, new Vector3(0, -0.4f, -0.12f), new Vector3(0.11f, 0.06f, 0.11f), new Vector3(-15f, 0, 0));
            tail = tl;
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.19f : 0.19f, z = i < 2 ? 0.55f : -0.55f;
                var leg = TmKit.Leg(t, new Vector3(x, 0.92f, z), 0.86f, 0.17f, coat, belly, ghost, legs);
                Art.Part(leg, Art.Sphere, spot, new Vector3(x * 0.4f, -0.35f, 0.07f), new Vector3(0.07f, 0.08f, 0.04f));
            }
            saddle = TmKit.Saddle(t, ghost, 1.46f, 0.6f);
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

            // ---- the walkways and climbing trees. The middle round the ball is open: a ring of round post platforms
            //      (HubDist out) joined by walkways circles it, and walkways branch out from it to the giant climbing trees
            //      (three decks high, TreeMinR+ out), on to a second ring (two decks) and the low ones by the bases ----
            bool Ok(Vector3 p) => TmKit.FreeSpot(p, Halfs[0] + 1.5f) && ThemeMaps.Height(p.x, p.z) < 3.2f && p.magnitude >= TreeMinR;
            var nodes = new List<Vector3>();
            var lvls = new List<int>();
            var hub = new List<bool>();
            int Add(Vector3 p, int lv, bool isHub = false)
            {
                for (int i = 0; i < nodes.Count; i++) if ((nodes[i] - p).sqrMagnitude < 1f) { lvls[i] = Mathf.Max(lvls[i], lv); return i; }
                nodes.Add(p); lvls.Add(lv); hub.Add(isHub);
                return nodes.Count - 1;
            }
            int Tower(Vector3 p, int lv) => Ok(p) ? Add(p, lv) : -1;
            float Hf(int i, int lv) => hub[i] ? HubR : Halfs[lv];
            // (a = this sector's node, c = the other end; ext: c's copy in team k's sector)
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
            bool SpanOk(Vector3 pa, Vector3 pb, int a, int c, int lv)
            {
                float len = (pa - pb).magnitude, span = len - Hf(a, lv) - Hf(c, lv);
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
                if (!SpanOk(pa, pb, a, b, lv)) return;
                links.Add((a, b, lv, 0));
            }
            // the nearest copy (another team's) of node c to point p
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
                if (!SpanOk(nodes[a], Cfg.Copy(nodes[c], k), a, c, lv)) return;
                links.Add((a, c, lv, k));
            }
            int Nearest(Vector3 p, params int[] of)
            {
                int best = -1;
                foreach (int i in of) if (i >= 0 && (best < 0 || (nodes[i] - p).sqrMagnitude < (nodes[best] - p).sqrMagnitude)) best = i;
                return best;
            }

            var hubPos = HubSpots();
            var hubs = new int[hubPos.Length];
            for (int i = 0; i < hubPos.Length; i++) hubs[i] = Add(hubPos[i], 1, true);
            float rT = TreeMinR + 4f, rM = Mathf.Max(rT + 14f, (rT + d - bh) * 0.55f);
            int sideL = Tower(new Vector3(-(bh + 11f), 0, -d + 4f), 1), sideR = Tower(new Vector3(bh + 11f, 0, -d + 4f), 1);
            if (two)
            {
                int hA = hubs[0], hB = hubs[1];
                int tA = Tower(TmKit.CentreSpot(-0.25f, rT), 3), tB = Tower(TmKit.CentreSpot(0.25f, rT), 3);
                int mC = Tower(TmKit.CentreSpot(0f, rM), 2);
                int cA = Tower(TmKit.CentreSpot(-0.42f, rM), 2), cB = Tower(TmKit.CentreSpot(0.42f, rM), 2);
                Link(hA, hB, 0);                    // the walkway across in front of the ball...
                LinkExt(hB, hA, 0);                 // ...and on round it, behind the other team's side: a ring
                Link(hA, tA, 0); Link(hB, tB, 0);   // branching out to the climbing trees
                if (tA < 0) Link(hA, Nearest(hubPos[0], cA, mC), 0);
                if (tB < 0) Link(hB, Nearest(hubPos[1], cB, mC), 0);
                Link(tA, mC, 0); Link(tB, mC, 0);
                Link(tA, cA, 1); Link(tB, cB, 1);
                LinkExt(cB, cA, 1);                 // high across to the other team's side
                Link(sideL, Nearest(nodes.Count > 0 && sideL >= 0 ? nodes[sideL] : Vector3.zero, cA, cB, mC), 0);
                Link(sideR, Nearest(nodes.Count > 0 && sideR >= 0 ? nodes[sideR] : Vector3.zero, cA, cB, mC), 0);
            }
            else
            {
                int h0 = hubs[0];
                int tA = Tower(TmKit.CentreSpot(0f, rT), 3);
                int cA = Tower(TmKit.CentreSpot(-0.35f, rM), 2), cB = Tower(TmKit.CentreSpot(0.35f, rM), 2);
                LinkExt(h0, h0, 0);                 // the ring of walkways round the ball (one platform per team)
                Link(h0, tA, 0);                    // branching out to the climbing tree...
                if (tA < 0) { Link(h0, cA, 0); Link(h0, cB, 0); }
                Link(tA, cA, 0); Link(tA, cB, 0);   // ...and on to the outer ones
                LinkExt(cB, cA, 1);                 // high round to the next team's side
                Link(sideL, Nearest(sideL >= 0 ? nodes[sideL] : Vector3.zero, cA, cB), 0);
                Link(sideR, Nearest(sideR >= 0 ? nodes[sideR] : Vector3.zero, cA, cB), 0);
            }

            // the bridges, and what's up in the air for SpotOk (every team's copy)
            s_Lanes.Clear();
            void Lane(Vector3 a, Vector3 b, float r)
            {
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var qa = Cfg.Copy(a, m); var qb = Cfg.Copy(b, m);
                    s_Lanes.Add((new Vector2(qa.x, qa.z), new Vector2(qb.x, qb.z), r));
                }
            }
            var used = new List<Vector3>[nodes.Count, 3];
            for (int i = 0; i < nodes.Count; i++) for (int l = 0; l < 3; l++) used[i, l] = new List<Vector3>();
            Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }
            foreach (var (a, c, lv, k) in links)
            {
                var pa = nodes[a];
                var pb = k == 0 ? nodes[c] : Cfg.Copy(nodes[c], k);
                var dir = Flat(pb - pa);
                Bridge(sec, pa + dir * Hf(a, lv), pb - dir * Hf(c, lv), LevelY(lv));
                Lane(pa, pb, 3.5f);
                used[a, lv].Add(dir);
                // (an ext bridge's copy turned into blue's sector arrives at c from a's copy there)
                used[c, lv].Add(k == 0 ? -dir : Flat(Cfg.Copy(pa, Cfg.Copies - k) - nodes[c]));
            }
            for (int i = 0; i < nodes.Count; i++) Lane(nodes[i], nodes[i], (hub[i] ? HubR : Halfs[0]) + 2f);
            var dirs = new Vector3[8];
            for (int i = 0; i < 8; i++) dirs[i] = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
            var feet = new List<Vector3>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var p = nodes[i];
                int L = lvls[i];
                if (hub[i]) HubDeck(sec, p);
                else DeckTree(sec, p, L, rng);
                bool Free(Vector3 dv, int lv, Vector3 avoid)
                {
                    foreach (var u in used[i, lv]) if (Vector3.Dot(u, dv) > 0.75f) return false;
                    if (lv > 0) foreach (var u in used[i, lv - 1]) if (Vector3.Dot(u, dv) > 0.75f) return false; // (not in the way of a bridge onto the deck it stands on)
                    return Vector3.Dot(avoid, dv) < 0.6f;
                }
                // the square decks' ladders go up the middle of a side (straight onto the deck's edge); round platforms any way
                int step = hub[i] ? 1 : 2;
                int start = hub[i] ? rng.Next(8) : rng.Next(4) * 2;
                // up from the ground: a ramp too on the low trees by the bases
                if (!hub[i] && Mathf.Abs(p.z) > d * 0.8f)
                    for (int k = 0; k < 8; k += 2)
                    {
                        var dv = dirs[(start + k) % 8];
                        if (!Free(dv, 0, Vector3.zero)) continue;
                        float len = DeckY / Mathf.Tan(24f * Mathf.Deg2Rad);
                        var top = p + dv * Halfs[0];
                        var foot = top + dv * len;
                        if (!TmKit.FreeSpot(foot, 1f) || ThemeMaps.Height(foot.x, foot.z) > 2f) continue;
                        Ramp(sec, top, foot);
                        Lane(top, foot, 2f);
                        used[i, 0].Add(dv);
                        feet.Add(foot);
                        break;
                    }
                // a ladder up to every deck (from the ground, then from the deck below), each on its own side
                Vector3 last = Vector3.zero;
                for (int lv = 0; lv < L; lv++)
                {
                    // round platforms: the ladder on the side away from the ball if it's free
                    if (hub[i]) start = Mathf.RoundToInt(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg / 45f + 8f) % 8;
                    for (int k = 0; k < 8; k += step)
                    {
                        var dv = dirs[(start + (hub[i] ? (k % 2 == 0 ? k / 2 : 8 - (k + 1) / 2) : k * 3)) % 8];
                        if (!Free(dv, lv, last)) continue;
                        var foot = p + dv * Hf(i, lv);
                        foot.y = lv == 0 ? ThemeMaps.Height(foot.x, foot.z) : LevelY(lv - 1);
                        if (lv == 0 && !hub[i] && foot.y > DeckY - 2f) continue;
                        TmKit.Ladder(sec, foot, LevelY(lv), dv, PlankDark);
                        used[i, lv].Add(dv);
                        if (lv == 0) feet.Add(p + dv * (Hf(i, 0) + 1.5f));
                        last = dv;
                        break;
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
            // (more spots than get used: the ones by the walkway platforms are left out)
            var spots = new (float f, float r)[] { (-0.3f, 7.5f), (0.27f, 8.5f), (0f, 12f), (-0.15f, 15f), (0.38f, 13f), (-0.4f, 11.5f), (0.15f, 16.5f),
                (0.06f, 7f), (-0.47f, 15f), (0.46f, 9f), (-0.06f, 17f), (0.2f, 6.8f) };
            var hubs = HubSpots();
            bool ByHub(Vector3 q)
            {
                for (int m = 0; m < Cfg.Copies; m++)
                    foreach (var h in hubs)
                    {
                        var c = Cfg.Copy(h, m);
                        if (new Vector2(c.x - q.x, c.z - q.z).magnitude < HubR + 4.5f) return true; // (clear of the walkway platforms and their ladders)
                    }
                return false;
            }
            TmKit.CentreLayout(root, "Jungle centre", 6250, spots, 17f, 5f, (sec, p, yaw, i, rng) =>
            {
                switch (i % 7)
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
            }, ByHub);
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
                Art.Box(sec, PlankDark, new Vector3(p.x, y - 0.4f, p.z), new Vector3(hh * 2f - 0.3f, 0.14f, hh * 2f - 0.3f)); // (inside the edge: clear of the ladders)
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
                var c = p + Quaternion.Euler(0, v * 90 + 20, 0) * new Vector3(0, 0, Halfs[0] - 0.1f);
                float len = 1.2f + (float)rng.NextDouble() * 1.6f;
                Art.Box(sec, DarkMoss, new Vector3(c.x, y0 - 0.3f - len * 0.5f, c.z), new Vector3(0.1f, len, 0.1f));
            }
        }

        /// <summary>A round plank walkway platform 5.5 m up on four posts (round, so a ladder up any side tops out right at
        /// its edge), a dark rim under it and a lantern on a post.</summary>
        static void HubDeck(Transform sec, Vector3 p)
        {
            float y = DeckY;
            var deck = TmKit.Stone(sec, new Vector3(p.x, y, p.z), HubR, PlankC, 0.36f);
            deck.name = "walkway platform";
            Art.Part(sec, Art.Cylinder, PlankDark, new Vector3(p.x, y - 0.42f, p.z), new Vector3(HubR * 2f - 0.4f, 0.07f, HubR * 2f - 0.4f));
            for (int i = 0; i < 4; i++)
            {
                var c = p + Quaternion.Euler(0, 45 + i * 90, 0) * new Vector3(0, 0, HubR - 0.6f);
                float g = ThemeMaps.Height(c.x, c.z);
                Art.Part(sec, Art.Cylinder, Bark, new Vector3(c.x, (g + y) * 0.5f - 0.2f, c.z), new Vector3(0.36f, (y - g) * 0.5f, 0.36f), default, true);
            }
            // cross slats on top for the plank look
            for (int k = -2; k <= 2; k++)
                Art.Box(sec, PlankDark, new Vector3(p.x + k * 0.95f, y + 0.005f, p.z), new Vector3(0.1f, 0.02f, 2f * Mathf.Sqrt(Mathf.Max(0.1f, HubR * HubR - k * 0.95f * k * 0.95f)) - 0.2f));
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

        /// <summary>The red treehouse on stilts (like the picture): floor 3 m up, a door and a window, a ladder.</summary>
        static void StiltHouse(Transform sec, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var e = new Vector3(0, yaw, 0);
            float fy = p.y + 3f, w = 3.6f, wallH = 2.6f;
            Vector3 L(float x, float y, float z) => p + rot * new Vector3(x, 0, z) + Vector3.up * y;
            for (int i = 0; i < 4; i++)
            {
                var c = L((i % 2 == 0 ? -1 : 1) * (w * 0.5f - 0.2f), 0, (i < 2 ? -1 : 1) * (w * 0.5f - 0.2f));
                float g = ThemeMaps.Height(c.x, c.z);
                Art.Box(sec, TmKit.Shade(RedWood, 0.8f), new Vector3(c.x, (g + fy) * 0.5f - 0.2f, c.z), new Vector3(0.25f, fy - g + 0.2f, 0.25f), e, true);
            }
            Art.Box(sec, TmKit.Shade(RedWood, 0.85f), L(0, fy - 0.12f - p.y, 0), new Vector3(w + 0.6f, 0.24f, w + 0.6f), e, true);
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
            var foot = p + face * (w * 0.5f + 0.3f);
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
