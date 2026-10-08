using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// SWAMP: murky shallow water (slow to wade) between mud islands, golden fog. Lily pads you can hop across are laid
    /// out in trails - from each base towards the middle and out to the sides - and in clusters round the islands,
    /// so the quick way about is parkour. Twisted black leafless trees with roots and hanging moss, stumps and fallen
    /// logs, and an old ruined shrine glowing in the fog in every team's part.
    /// </summary>
    public class SwampMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Swamp;
        public override string Label => "Swamp";
        public override string Blurb => "Murky water (slow to wade) between mud islands, lost in golden fog. Hop the lily pad trails to get about fast - or craft a BOAT (1500 wood).";
        public override bool HasWater => true;
        public override bool Mountains => false;

        const float PadTop = ThemeMaps.WaterY + 0.12f;
        static readonly Color Mud = new Color(0.27f, 0.24f, 0.17f), MossGround = new Color(0.25f, 0.29f, 0.17f), Bed = new Color(0.17f, 0.17f, 0.12f);
        static readonly Color BarkC = new Color(0.13f, 0.12f, 0.1f), HangMoss = new Color(0.33f, 0.37f, 0.27f);
        static readonly Color Pad = new Color(0.3f, 0.48f, 0.2f), Pad2 = new Color(0.38f, 0.55f, 0.24f), Stone = new Color(0.42f, 0.41f, 0.36f);

        public override float Height(float x, float z)
        {
            float s = ThemeMaps.SeedP;
            float n = TmKit.SymN(x, z, 0.04f, s);
            float land = ThemeMaps.SmoothStepP(0.5f, 0.55f, n);
            float h = Mathf.Lerp(-1.05f, 0.22f + (n - 0.55f) * 2f, land);
            h += (ThemeMaps.SymNoiseP(x, z, 0.15f, s + 9f) - 0.5f) * 0.3f;
            return h * ThemeMaps.MaskP(x, z);
        }

        public override Color[] Palette => new[] { Mud, MossGround, Bed };
        public override int ColourAt(Vector3 c, float slopeY) => c.y < -0.3f ? 2 : ThemeMaps.SymNoiseP(c.x, c.z, 0.1f, ThemeMaps.SeedP + 3f) > 0.5f ? 1 : 0;

        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1f : kind == ResourceNode.Bush ? 0.9f : 0.6f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.3f, 0.36f, 0.2f), 0.55f);

        /// <summary>Twisted black trees, no leaves: a crooked trunk on arching roots, gnarled branches, hanging moss.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 13);
            float R() => (float)rng.NextDouble();
            float Rs() => R() * 2f - 1f;
            var bark = TmKit.Shade(BarkC, 0.9f + R() * 0.3f);
            float H = h * (1.15f + R() * 0.35f);
            // the trunk: bends more the higher it goes
            var pos = Vector3.zero;
            var dir = Vector3.up;
            int segs = 4;
            float segL = H / segs;
            for (int i = 0; i < segs; i++)
            {
                float bend = i == 0 ? 6f : 18f + i * 8f;
                dir = (Quaternion.Euler(Rs() * bend, 0, Rs() * bend) * dir).normalized;
                if (dir.y < 0.45f) { dir.y = 0.45f; dir.Normalize(); }
                float w = Mathf.Lerp(0.75f, 0.32f, i / (float)segs);
                Seg(tr, bark, pos, pos + dir * segL, w);
                pos += dir * segL;
            }
            // roots arching out of the ground
            int roots = 3 + rng.Next(2);
            for (int k = 0; k < roots; k++)
            {
                var o = Quaternion.Euler(0, k * 360f / roots + Rs() * 25f, 0) * Vector3.forward;
                var a = Vector3.up * (0.7f + R() * 0.5f);
                var m = o * (1.1f + R() * 0.5f) + Vector3.up * (0.55f + R() * 0.3f);
                var e = o * (2f + R() * 0.8f) + Vector3.down * 0.3f;
                Seg(tr, bark, a, m, 0.3f);
                Seg(tr, bark, m, e, 0.22f);
            }
            // gnarled branches, moss hanging off them
            int branches = 3;
            for (int k = 0; k < branches; k++)
            {
                var a = Vector3.Lerp(Vector3.zero, pos, 0.55f + k * 0.15f) + Vector3.up * 0.2f;
                var o = Quaternion.Euler(0, k * 120f + Rs() * 30f, 0) * Vector3.forward;
                var m = a + (o * 1.6f + Vector3.up * (0.9f + R() * 0.6f));
                var e = m + (Quaternion.Euler(0, Rs() * 50f, 0) * o) * 1.5f + Vector3.up * (Rs() * 0.6f);
                Seg(tr, bark, a, m, 0.2f);
                Seg(tr, bark, m, e, 0.13f);
                float ml = 0.8f + R() * 1.4f;
                Art.Box(tr, HangMoss, e + Vector3.down * ml * 0.5f, new Vector3(0.12f, ml, 0.12f), new Vector3(0, R() * 90f, 0));
            }
            float tl = 1.2f + R() * 1.2f;
            Art.Box(tr, HangMoss, pos + Vector3.down * tl * 0.5f + Vector3.right * 0.2f, new Vector3(0.14f, tl, 0.14f));
            return true;
        }

        /// <summary>A cylinder from a to b, width w.</summary>
        static void Seg(Transform tr, Color c, Vector3 a, Vector3 b, float w)
        {
            var d = b - a;
            Art.Part(tr, Art.Cylinder, c, (a + b) * 0.5f, new Vector3(w, d.magnitude * 0.5f + w * 0.25f, w), Quaternion.FromToRotation(Vector3.up, d).eulerAngles);
        }

        // =====================================================================

        readonly TmSky m_Sky = new TmSky();
        public override void ApplySky() => m_Sky.Apply(new Color(0.6f, 0.58f, 0.44f), 0.0135f,
            new Color(0.5f, 0.5f, 0.4f), new Color(0.38f, 0.37f, 0.28f), new Color(0.16f, 0.16f, 0.11f), new Color(1f, 0.8f, 0.5f), 0.75f,
            new Color(0.62f, 0.55f, 0.34f), 0.9f);
        public override void Cleanup() => m_Sky.Restore();

        static bool Water(Vector3 p, float margin = 0f) => ThemeMaps.Height(p.x, p.z) < ThemeMaps.WaterY - 0.15f - margin;

        public override void BuildProps(Transform root)
        {
            TmKit.TintWater(root, new Color(0.24f, 0.29f, 0.2f), 0.92f);
            var rng = TmKit.Rng(7300);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var sec = TmKit.Sector(root, "Swamp props");
            float d = Mathf.Abs(Cfg.BaseCenter[0].z), bh = Cfg.BaseHalf, half = Cfg.MapHalf;
            var pads = new List<Vector3>();

            bool Fits(Vector3 p, float r)
            {
                if (!TmKit.FreeSpot(p, r + 0.5f) || !Water(p)) return false;
                foreach (var q in pads) if ((q - p).sqrMagnitude < (r + q.y + 0.5f) * (r + q.y + 0.5f)) return false; // (q.y holds that pad's radius)
                return true;
            }
            void PadAt(Vector3 p, float r)
            {
                pads.Add(new Vector3(p.x, r, p.z));
                var go = TmKit.Stone(sec, new Vector3(p.x, PadTop, p.z), r, rng.NextDouble() < 0.5 ? Pad : Pad2);
                go.name = "lily pad";
                // a notch cut in the pad (a dark wedge on top), and now and then a flower
                float a = Rn(0, 360);
                Art.Box(sec, Color.Lerp(Pad, Color.black, 0.45f), new Vector3(p.x, PadTop + 0.005f, p.z) + Quaternion.Euler(0, a, 0) * new Vector3(0, 0, r * 0.55f), new Vector3(0.1f, 0.02f, r * 0.9f), new Vector3(0, a, 0));
                if (rng.NextDouble() < 0.22)
                {
                    var fc = rng.NextDouble() < 0.5 ? new Color(0.95f, 0.7f, 0.8f) : new Color(0.95f, 0.93f, 0.85f);
                    var fp = new Vector3(p.x, PadTop + 0.15f, p.z) + Quaternion.Euler(0, a + 180f, 0) * new Vector3(0, 0, r * 0.35f);
                    Art.Part(sec, Art.Cone, fc, fp, new Vector3(0.45f, 0.35f, 0.45f), new Vector3(180f, 0, 0));
                    Art.Part(sec, Art.Sphere, new Color(1f, 0.85f, 0.3f), fp + Vector3.up * 0.05f, Vector3.one * 0.14f);
                }
            }
            // a trail of pads along a curve from a to b (bulging sideways by `bend`), skipping over land
            void Trail(Vector3 a, Vector3 b, float bend)
            {
                var side = Vector3.Cross(Vector3.up, (b - a).normalized);
                var c = (a + b) * 0.5f + side * bend;
                float len = (b - a).magnitude + Mathf.Abs(bend);
                float t = 0f;
                while (t <= 1f)
                {
                    var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b + side * Rn(-0.6f, 0.6f);
                    float r = Rn(0.85f, 1.25f);
                    if (Fits(p, r)) PadAt(p, r);
                    t += Rn(2.8f, 3.4f) / len;
                }
            }

            // ---- the trails: straight from the base to the middle, two curving round, two out to the sides ----
            float zIn = -d + bh + 4f;
            Trail(new Vector3(0, 0, zIn), new Vector3(0, 0, -19f), 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                var toMid = Quaternion.Euler(0, s * 28f, 0) * new Vector3(0, 0, -21f);
                Trail(new Vector3(s * (bh - 4f), 0, zIn), toMid, s * Mathf.Max(6f, d * 0.12f));
                var outer = new Vector3(s * Mathf.Min(half - 8f, d * 0.75f), 0, -d * (Cfg.FourWay ? 0.82f : 0.5f));
                Trail(new Vector3(s * (bh + 3f), 0, -d), outer, -s * Mathf.Max(5f, d * 0.1f));
                // and across the front of the middle, from trail to trail
                Trail(Quaternion.Euler(0, s * 6f, 0) * new Vector3(0, 0, -26f), Quaternion.Euler(0, s * 40f, 0) * new Vector3(0, 0, -27f), -s * 2f);
            }
            // ---- clusters of pads round the water's edge ----
            int clusters = Mathf.RoundToInt(9 * half / 100f);
            for (int i = 0, made = 0; i < clusters * 40 && made < clusters; i++)
            {
                var c = new Vector3(Rn(-half + 8f, half - 8f), 0, Rn(-half + 8f, -6f));
                if (!Fits(c, 1.8f)) continue;
                made++;
                float big = Rn(1.5f, 2.1f);
                PadAt(c, big);
                int n = 3 + rng.Next(4);
                float a0 = Rn(0, 360);
                for (int k = 0; k < n; k++)
                {
                    float r = Rn(0.8f, 1.2f);
                    var p = c + Quaternion.Euler(0, a0 + k * 360f / n + Rn(-15f, 15f), 0) * new Vector3(0, 0, big + r + Rn(0.7f, 1.5f));
                    if (Fits(p, r)) PadAt(p, r);
                }
            }

            // ---- stumps and snags in the water (things to stand on), fallen logs, reeds ----
            int bits = Mathf.RoundToInt(14 * half / 100f);
            for (int i = 0, made = 0; i < bits * 40 && made < bits; i++)
            {
                var p = new Vector3(Rn(-half + 6f, half - 6f), 0, Rn(-half + 6f, -4f));
                if (!Fits(p, 1f)) continue;
                made++;
                pads.Add(new Vector3(p.x, 1f, p.z));
                float g = ThemeMaps.Height(p.x, p.z);
                switch (made % 3)
                {
                    case 0: // a stump, its top a step up from the pads
                    {
                        float top = PadTop + Rn(0.5f, 1.1f), r = Rn(0.5f, 0.75f);
                        Art.Part(sec, Art.Cylinder, TmKit.Shade(BarkC, 1.3f), new Vector3(p.x, (g + top) * 0.5f, p.z), new Vector3(r * 2f, (top - g) * 0.5f, r * 2f), new Vector3(Rn(-4, 4), 0, Rn(-4, 4)), true);
                        break;
                    }
                    case 1: // a snag: a dead spike sticking out of the water at a lean
                        Seg(sec, BarkC, new Vector3(p.x, g, p.z), new Vector3(p.x + Rn(-1f, 1f), PadTop + Rn(1.5f, 3f), p.z + Rn(-1f, 1f)), 0.25f);
                        break;
                    default: // reeds
                        for (int k = 0; k < 6; k++)
                        {
                            float rh = Rn(1f, 1.9f);
                            var q = p + new Vector3(Rn(-0.8f, 0.8f), 0, Rn(-0.8f, 0.8f));
                            Art.Box(sec, new Color(0.32f, 0.38f, 0.2f), new Vector3(q.x, ThemeMaps.WaterY + rh * 0.5f, q.z), new Vector3(0.06f, rh, 0.06f), new Vector3(Rn(-10, 10), Rn(0, 90), Rn(-10, 10)));
                        }
                        break;
                }
            }
            int logs = Mathf.RoundToInt(4 * half / 100f);
            for (int i = 0, made = 0; i < logs * 60 && made < logs; i++)
            {
                var a = new Vector3(Rn(-half + 8f, half - 8f), 0, Rn(-half + 8f, -6f));
                var dirL = Quaternion.Euler(0, Rn(0, 360), 0) * Vector3.forward;
                var b = a + dirL * Rn(6f, 9f);
                if (!TmKit.FreeSpot(a, 2f) || !TmKit.FreeSpot(b, 2f) || !Water((a + b) * 0.5f, 0.2f)) continue;
                float ha = ThemeMaps.Height(a.x, a.z), hb = ThemeMaps.Height(b.x, b.z);
                if (ha < -0.1f || hb < -0.1f) continue; // land at both ends: a log bridge over the water
                made++;
                a.y = ha + 0.25f; b.y = hb + 0.25f;
                var dd = b - a;
                var log = Art.Part(sec, Art.Cylinder, new Color(0.24f, 0.2f, 0.15f), (a + b) * 0.5f, new Vector3(0.7f, dd.magnitude * 0.5f, 0.7f), Quaternion.FromToRotation(Vector3.up, dd).eulerAngles);
                var bc = log.AddComponent<BoxCollider>(); // (a flat top to walk along)
                bc.size = new Vector3(1f, 2f, 0.8f);
            }

            // ---- the ruined shrine, glowing in the fog ----
            var spot = new Vector3(Mathf.Max(bh + 14f, d * 0.42f), 0, -d * 0.58f);
            for (int tries = 0; tries < 80 && !TmKit.FreeSpot(spot, 8f); tries++)
                spot = new Vector3(Rn(-half + 12f, half - 12f), 0, Rn(-d, -26f));
            if (TmKit.FreeSpot(spot, 8f)) Shrine(sec, spot, Mathf.Atan2(-spot.x, -spot.z) * Mathf.Rad2Deg, rng);
            TmKit.CopyRound(sec);
        }

        /// <summary>A crumbling stone shrine on a plinth, a dark doorway with a golden light in it, broken pillars round it.</summary>
        static void Shrine(Transform sec, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var e = new Vector3(0, yaw, 0);
            float g = Mathf.Min(ThemeMaps.Height(p.x, p.z), 0f);
            Vector3 L(float x, float y, float z) => new Vector3(p.x, 0, p.z) + rot * new Vector3(x, 0, z) + Vector3.up * y;
            float plinth = 0.9f;
            Art.Box(sec, TmKit.Shade(Stone, 0.85f), L(0, (g - 0.5f + plinth) * 0.5f, 0), new Vector3(10f, plinth - g + 0.5f, 10f), e, true);
            // steps up from the water at the front (towards the middle)
            for (int i = 0; i < 3; i++)
                Art.Box(sec, TmKit.Shade(Stone, 0.8f), L(0, (g - 0.5f + plinth * (i + 1) / 4f) * 0.5f, 5.4f + (2 - i) * 0.7f), new Vector3(4f, plinth * (i + 1) / 4f - g + 0.5f, 0.75f), e, true);
            float y0 = plinth;
            // the shrine itself: walls round a dark doorway, then stepped roof blocks
            var dark = new Color(0.05f, 0.05f, 0.04f);
            Art.Box(sec, Stone, L(0, y0 + 1.6f, -0.6f), new Vector3(5f, 3.2f, 3.8f), e, true);
            Art.Box(sec, Stone, L(-1.8f, y0 + 1.6f, 1.6f), new Vector3(1.4f, 3.2f, 0.8f), e, true);
            Art.Box(sec, Stone, L(1.8f, y0 + 1.6f, 1.6f), new Vector3(1.4f, 3.2f, 0.8f), e, true);
            Art.Box(sec, Stone, L(0, y0 + 2.9f, 1.6f), new Vector3(2.4f, 0.6f, 0.8f), e, true);
            Art.Box(sec, dark, L(0, y0 + 1.3f, 1.34f), new Vector3(2.2f, 2.6f, 0.06f), e);
            Art.Box(sec, TmKit.Shade(Stone, 1.1f), L(0, y0 + 3.5f, 0), new Vector3(5.8f, 0.6f, 5f), e, true);
            Art.Box(sec, Stone, L(0, y0 + 4.1f, 0), new Vector3(4.2f, 0.6f, 3.6f), e, true);
            Art.Box(sec, TmKit.Shade(Stone, 0.9f), L(0.3f, y0 + 4.7f, -0.2f), new Vector3(2.6f, 0.6f, 2.2f), new Vector3(0, yaw + 8f, 4f), true);
            Art.Box(sec, Stone, L(-0.2f, y0 + 5.4f, 0.1f), new Vector3(1.2f, 0.9f, 1.2f), new Vector3(0, yaw - 10f, -6f));
            // the golden glow in the doorway
            var gold = new Color(1f, 0.78f, 0.35f);
            var glowMat = TmKit.Glow(gold, 2.5f);
            Art.Part(sec, Art.Sphere, gold, L(0, y0 + 1.2f, 1.45f), new Vector3(0.9f, 1.2f, 0.3f), e, false, glowMat);
            var lightGo = new GameObject("shrine light");
            lightGo.transform.SetParent(sec, false);
            lightGo.transform.localPosition = L(0, y0 + 1.8f, 3f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = gold;
            l.range = 16f;
            l.intensity = 2.2f;
            l.shadows = LightShadows.None;
            // lanterns on the front corners
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Box(sec, TmKit.Shade(Stone, 0.7f), L(s * 4.2f, y0 + 0.7f, 4.2f), new Vector3(0.5f, 1.4f, 0.5f), e, true);
                Art.Part(sec, Art.Sphere, gold, L(s * 4.2f, y0 + 1.6f, 4.2f), Vector3.one * 0.45f, default, false, glowMat);
            }
            // broken pillars round it
            for (int k = 0; k < 5; k++)
            {
                float a = -150f + k * 75f + (float)rng.NextDouble() * 20f;
                var c = L(0, 0, 0) + Quaternion.Euler(0, yaw + a, 0) * new Vector3(0, 0, 7.5f);
                float cg = ThemeMaps.Height(c.x, c.z), ph = 1.2f + (float)rng.NextDouble() * 2.5f;
                Art.Part(sec, Art.Cylinder, TmKit.Shade(Stone, 0.95f), new Vector3(c.x, cg + ph * 0.5f - 0.3f, c.z), new Vector3(0.8f, ph * 0.5f, 0.8f), new Vector3((float)rng.NextDouble() * 8f, 0, (float)rng.NextDouble() * 8f), true);
                Art.Box(sec, HangMoss, new Vector3(c.x, cg + ph - 0.3f, c.z), new Vector3(0.85f, 0.1f, 0.85f), new Vector3(0, a, 0));
            }
        }
    }
}
