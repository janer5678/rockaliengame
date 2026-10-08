using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// JUNGLE: mossy forest floor closed in by tall grey rock cliffs. Lots of tall thin leafy trees. In every team's
    /// part of the map a canopy walkway: big trees with plank decks 5.5 m up, joined by plank bridges, reached by
    /// ramps and ladders. A red stilt treehouse and a roofed hut in each part too. Light green haze.
    /// </summary>
    public class JungleMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Jungle;
        public override string Label => "Jungle";
        public override string Blurb => "Thick jungle walled in by rock cliffs. Plank walkways up in the trees - take the ramps and ladders up and cross over the fights below.";
        public override bool Mountains => false;
        public override float MaxSpotHeight => 3f;

        const float DeckY = 5.5f, DeckHalf = 3.4f;
        static readonly Color Moss = new Color(0.3f, 0.46f, 0.2f), Dirt = new Color(0.43f, 0.35f, 0.23f), Cliff = new Color(0.52f, 0.5f, 0.47f), DarkMoss = new Color(0.24f, 0.38f, 0.16f);
        static readonly Color PlankC = new Color(0.66f, 0.42f, 0.3f), PlankDark = new Color(0.48f, 0.3f, 0.2f), Bark = new Color(0.4f, 0.32f, 0.22f);
        static readonly Color Leaf = new Color(0.16f, 0.42f, 0.16f), Leaf2 = new Color(0.22f, 0.5f, 0.18f), RedWood = new Color(0.82f, 0.3f, 0.2f);

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

        /// <summary>Tall thin trees, branches reaching up with clumps of leaves along them.</summary>
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
            }
            Art.Part(tr, TmKit.Blob(seed + 7), leaf, new Vector3(0, H + 0.4f, 0), new Vector3(2.2f, 1.3f, 2.2f), new Vector3(0, R() * 360f, 0));
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

            // ---- the canopy walkway: platforms (mirror-symmetric in blue's sector), bridges, ramps, ladders ----
            bool Ok(Vector3 p) => TmKit.FreeSpot(p, DeckHalf + 1.5f) && ThemeMaps.Height(p.x, p.z) < 3.2f;
            var nodes = new List<Vector3>();
            var links = new List<(int a, int b)>();
            int Add(Vector3 p) { for (int i = 0; i < nodes.Count; i++) if ((nodes[i] - p).sqrMagnitude < 1f) return i; nodes.Add(p); return nodes.Count - 1; }
            void Link(Vector3 a, Vector3 b, int depth)
            {
                if (!Ok(a) || !Ok(b)) return;
                if ((a - b).magnitude > 34f && depth < 3)
                {
                    var m = (a + b) * 0.5f;
                    if (Ok(m)) { Link(a, m, depth + 1); Link(m, b, depth + 1); return; }
                }
                links.Add((Add(a), Add(b)));
            }
            var mid = new Vector3(0, 0, -d * 0.45f);
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(s * (bh + 11f), 0, -d + 4f);
                var b = new Vector3(s * Mathf.Max(bh + 10f, d * 0.36f), 0, -d * 0.62f);
                Link(a, b, 0);
                if (Ok(mid)) Link(b, mid, 0);
                else if (Ok(b)) Add(b);
                if (Ok(a)) Add(a);
            }
            if (Ok(mid)) Add(mid);
            var used = new List<Vector3>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) used[i] = new List<Vector3>();
            foreach (var (a, b) in links)
            {
                var dir = nodes[b] - nodes[a];
                dir.y = 0;
                dir.Normalize();
                Bridge(sec, nodes[a] + dir * DeckHalf, nodes[b] - dir * DeckHalf);
                used[a].Add(dir);
                used[b].Add(-dir);
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                var p = nodes[i];
                DeckTree(sec, p, rng);
                // a ladder, and a ramp on the platforms by the base, on sides no bridge uses
                var dirs = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
                bool ramp = Mathf.Abs(p.z) > d * 0.8f, ladder = false;
                foreach (var dv in dirs)
                {
                    bool taken = false;
                    foreach (var u in used[i]) if (Vector3.Dot(u, dv) > 0.5f) taken = true;
                    if (taken) continue;
                    if (ramp)
                    {
                        float len = DeckY / Mathf.Tan(24f * Mathf.Deg2Rad);
                        var top = p + dv * DeckHalf;
                        var foot = top + dv * len;
                        if (!TmKit.FreeSpot(foot, 1f) || ThemeMaps.Height(foot.x, foot.z) > 2f) continue;
                        Ramp(sec, top, foot);
                        used[i].Add(dv);
                        ramp = false;
                        continue;
                    }
                    if (!ladder)
                    {
                        var foot = p + dv * DeckHalf;
                        foot.y = ThemeMaps.Height(foot.x, foot.z);
                        TmKit.Ladder(sec, foot, DeckY, dv, PlankDark);
                        used[i].Add(dv);
                        ladder = true;
                    }
                }
            }

            // ---- a red treehouse on stilts, and a roofed hut, in every part ----
            for (int k = 0; k < 2; k++)
            {
                for (int tries = 0; tries < 80; tries++)
                {
                    var p = new Vector3(Rn(-half + 12f, half - 12f), 0, Rn(-d * 1.1f, -24f));
                    if (!TmKit.FreeSpot(p, 6f) || ThemeMaps.Height(p.x, p.z) > 2.5f) continue;
                    bool near = false;
                    foreach (var n in nodes) if ((n - p).magnitude < 12f) near = true;
                    if (near) continue;
                    p.y = ThemeMaps.Height(p.x, p.z);
                    if (k == 0) StiltHouse(sec, p, Rn(0, 360));
                    else Hut(sec, p, Rn(0, 360));
                    nodes.Add(p);
                    break;
                }
            }

            // ---- mossy boulders for cover ----
            int want = Mathf.RoundToInt(10 * half / 100f);
            for (int i = 0, made = 0; i < want * 40 && made < want; i++)
            {
                var p = new Vector3(Rn(-half + 8f, half - 8f), 0, Rn(-half + 8f, -4f));
                if (!TmKit.FreeSpot(p, 4f) || ThemeMaps.Height(p.x, p.z) > 3f) continue;
                bool near = false;
                foreach (var n in nodes) if ((n - p).magnitude < 8f) near = true;
                if (near) continue;
                made++;
                p.y = ThemeMaps.Height(p.x, p.z);
                float sc = Rn(1.4f, 2.6f);
                Art.Part(sec, Art.MakeRock(rng.Next(1000), 0.3f), TmKit.Shade(Cliff, Rn(0.9f, 1.1f)), p + Vector3.up * sc * 0.4f, new Vector3(sc * 1.3f, sc, sc * 1.1f), new Vector3(0, Rn(0, 360), 0), true);
                Art.Part(sec, TmKit.Blob(i), DarkMoss, p + Vector3.up * sc * 1.25f, new Vector3(sc * 1.1f, sc * 0.35f, sc * 0.9f), new Vector3(0, Rn(0, 360), 0));
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

        /// <summary>A big tree through a plank deck at DeckY, with its canopy above and vines hanging off the deck.</summary>
        static void DeckTree(Transform sec, Vector3 p, System.Random rng)
        {
            float g = ThemeMaps.Height(p.x, p.z);
            float top = DeckY + 7f;
            Art.Part(sec, Art.Cylinder, Bark, new Vector3(p.x, (g - 0.5f + top) * 0.5f, p.z), new Vector3(2.0f, (top - g + 0.5f) * 0.5f, 2.0f), default, true);
            Art.Part(sec, Art.Cylinder, TmKit.Shade(Bark, 0.9f), new Vector3(p.x, g + 0.4f, p.z), new Vector3(3f, 0.5f, 3f));
            // the deck, with a darker rim under it
            Art.Box(sec, PlankC, new Vector3(p.x, DeckY - 0.18f, p.z), new Vector3(DeckHalf * 2f, 0.36f, DeckHalf * 2f), default, true);
            Art.Box(sec, PlankDark, new Vector3(p.x, DeckY - 0.4f, p.z), new Vector3(DeckHalf * 2f + 0.2f, 0.14f, DeckHalf * 2f + 0.2f));
            for (int i = 0; i < 4; i++)
            {
                var c = p + Quaternion.Euler(0, 45 + i * 90, 0) * new Vector3(0, 0, DeckHalf * 1.15f);
                float cg = ThemeMaps.Height(c.x, c.z);
                Art.Part(sec, Art.Cylinder, PlankDark, new Vector3(c.x, (cg + DeckY) * 0.5f - 0.2f, c.z), new Vector3(0.3f, (DeckY - cg) * 0.5f, 0.3f), new Vector3(8f * Mathf.Cos(i), 0, 8f * Mathf.Sin(i))); // braces
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
                var c = p + Quaternion.Euler(0, v * 90 + 20, 0) * new Vector3(0, 0, DeckHalf - 0.1f);
                float len = 1.2f + (float)rng.NextDouble() * 1.6f;
                Art.Box(sec, DarkMoss, new Vector3(c.x, DeckY - 0.3f - len * 0.5f, c.z), new Vector3(0.1f, len, 0.1f));
            }
        }

        /// <summary>A flat plank bridge between two decks, with rope rails.</summary>
        static void Bridge(Transform sec, Vector3 a, Vector3 b)
        {
            a.y = b.y = DeckY;
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
