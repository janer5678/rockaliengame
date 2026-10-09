using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// ISLANDS: every team's base sits on its own small round sand island far out at sea (two teams: in opposite corners
    /// of the map, on the diagonal), and the ball drops on a small round island in the middle (the same size). Nothing
    /// joins them and the sea is deep: off a boat you swim, very slowly, at the surface (DeepWater), and every island has
    /// a gentle sandy slope up out of the water to climb out on. Boats can be crafted from the start. The middle island
    /// has sandstone blocks with ladders, a wooden deck, crates, sea-glass and a driftwood barricade to fight round.
    /// Palm trees, cactus-fruit bushes, zebras to ride.
    /// (Three teams: only three base islands - the empty fourth spot is open sea, with nothing on it.)
    /// </summary>
    public class IslandsMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Islands;
        public override string Label => "Islands";
        public override string Blurb => "A small round island for every team, far out at sea, and one in the middle with the ball. The sea is DEEP: without a boat you can only swim - very, very slowly. Craft a BOAT - you can make one from the start.";
        public override bool HasWater => true;
        /// <summary>Too deep to wade: off a boat you swim (very slowly, at the surface) and climb out up a beach.</summary>
        public override bool DeepWater => true;
        /// <summary>(Only a safety net: swimmers float at the surface, and the sea floor is at -14.)</summary>
        public override float KillY => -16f;
        /// <summary>Two teams: corner to corner, as far apart as the map allows.</summary>
        public override bool DiagonalBases => true;
        public override bool Mountains => false;

        /// <summary>Half the usual build space (24 x 24).</summary>
        public override float BaseHalfSize => 12f;
        /// <summary>Each round island's radius: just big enough round the base (its corners are 17 m out) for a beach.</summary>
        static float IslandR => Cfg.SmallMap ? 20f : 23f;
        /// <summary>The base islands right out near the edge of the map (a few metres of sea behind them). Two teams: this
        /// far along both axes (the corners); three or four: along their axis.</summary>
        public override float BaseDistance => Mathf.Floor((Cfg.MapHalf - IslandR - 4f) / 3f) * 3f;
        public override bool BoatsAnytime => true;
        public override string MountName => "Zebra";

        /// <summary>How far the base islands are from the middle (on the diagonal for two teams).</summary>
        static float D => new Vector2(Cfg.BaseCenter[0].x, Cfg.BaseCenter[0].z).magnitude;
        /// <summary>The middle island: the same size as the base islands (smaller on the small map, so there's still sea between).</summary>
        static float CenterR => Mathf.Clamp(D - IslandR - 12f, 12f, IslandR);
        /// <summary>The deep sea floor, and the bottom of the sloped shore round each island before it drops away: well
        /// under a swimmer's feet (WaterY - 1.25), so swimming in you meet a gentle sandy slope and walk up out of the sea.</summary>
        const float SeaFloor = -14f, ShoreFloor = -2.6f;

        static readonly Color Sand = new Color(0.93f, 0.85f, 0.62f), WetSand = new Color(0.8f, 0.7f, 0.5f), SeaBed = new Color(0.62f, 0.66f, 0.55f), Deep = new Color(0.16f, 0.32f, 0.36f);
        static readonly Color Sandstone = new Color(0.86f, 0.74f, 0.52f), Sandstone2 = new Color(0.78f, 0.66f, 0.45f);
        static readonly Color Plank = new Color(0.66f, 0.5f, 0.32f), PlankDark = new Color(0.5f, 0.37f, 0.22f);
        static readonly Color LadderOrange = new Color(0.95f, 0.45f, 0.12f), SeaGlass = new Color(0.55f, 0.85f, 0.9f);

        /// <summary>Is there a team in the k-th copy's spot? (Three teams: the fourth spot is open sea - no island, no props, no trees.)</summary>
        public static bool IslandUsed(int k)
        {
            var at = Cfg.Copy(Cfg.BaseCenter[0], k);
            for (int t = 0; t < Cfg.TeamCount; t++) if ((Cfg.BaseCenter[t] - at).sqrMagnitude < 4f) return true;
            return false;
        }

        public override float Height(float x, float z)
        {
            float s = ThemeMaps.SeedP;
            float wob = (ThemeMaps.SymNoiseP(x, z, 0.09f, s + 11f) - 0.5f) * 2f; // (a slightly wobbly shore)
            // how far out past the nearest island's shore (negative: on it) - the middle island, flat round the ball...
            float r = Mathf.Sqrt(x * x + z * z);
            float out_ = r + wob - CenterR;
            float flat = ThemeMaps.SmoothStepP(8f, 13f, r);
            // ...and one round island round every base - only as many as there are teams (three teams: the fourth spot is sea)
            for (int k = 0; k < Cfg.Copies; k++)
            {
                if (!IslandUsed(k)) continue;
                var c = Cfg.Copy(Cfg.BaseCenter[0], k);
                float dx = x - c.x, dz = z - c.z;
                out_ = Mathf.Min(out_, Mathf.Sqrt(dx * dx + dz * dz) + wob - IslandR);
                float bx = Mathf.Max(0f, Mathf.Abs(dx) - Cfg.BaseHalf), bz = Mathf.Max(0f, Mathf.Abs(dz) - Cfg.BaseHalf);
                flat = Mathf.Min(flat, ThemeMaps.SmoothStepP(0.5f, 5f, Mathf.Sqrt(bx * bx + bz * bz)));
            }
            float dune = 0.24f + (ThemeMaps.SymNoiseP(x, z, 0.08f, s + 3f) - 0.5f) * 0.5f;
            // the beach slopes gently down into the water (about 25 degrees: you can walk up it out of the sea), on down
            // to well under a swimmer's feet, then the sea floor drops away deep (no wading)
            float h = Mathf.Lerp(dune * flat, ShoreFloor, ThemeMaps.SmoothStepP(-2f, 5f, out_)); // (the bases and the middle stay flat at 0)
            return Mathf.Lerp(h, SeaFloor, ThemeMaps.SmoothStepP(5f, 10f, out_));
        }

        public override Color[] Palette => new[] { Sand, WetSand, SeaBed, Deep };
        public override int ColourAt(Vector3 c, float slopeY) => c.y > -0.2f ? 0 : c.y > -0.9f ? 1 : c.y > -4f ? 2 : 3;

        /// <summary>Only up on the sand (never in the sea, never on the wet shore).</summary>
        public override bool SpotOk(Vector3 p) => p.y > 0.02f && ThemeMaps.Height(p.x + 1.5f, p.z) > -0.3f && ThemeMaps.Height(p.x - 1.5f, p.z) > -0.3f
            && ThemeMaps.Height(p.x, p.z + 1.5f) > -0.3f && ThemeMaps.Height(p.x, p.z - 1.5f) > -0.3f;
        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1f : kind == ResourceNode.Bush ? 0.8f : 0.6f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.35f, 0.75f, 0.3f), 0.5f);

        /// <summary>The palms' trunk radius from the ground to 3.2 m (where the X goes).</summary>
        const float TrunkR = 0.3f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        /// <summary>Palm trees: a ringed trunk, straight low down (where the weak spots are) and leaning higher up, drooping
        /// fronds and coconuts.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 77);
            float R() => (float)rng.NextDouble();
            var bark = TmKit.Shade(Color.Lerp(new Color(0.62f, 0.48f, 0.3f), new Color(0.7f, 0.56f, 0.38f), R()), 1f);
            var ring = TmKit.Shade(bark, 0.8f);
            var root = new GameObject("palm").transform;
            root.SetParent(tr, false);
            root.localRotation = Quaternion.Euler(0, R() * 360f, 0);
            float lean = 8f + R() * 10f;
            // the bottom 3.2 m: straight up the middle, exactly TrunkR round (TreeTrunkRadius: the trunk collider and the
            // weak spot X are on a cylinder that thick, 0.3 - 3 m up) - so the X sits right on the bark you see
            const float StraightH = 3.2f, TrunkW = TrunkR * 2f;
            for (int i = 0; i < 2; i++)
                Art.Part(root, Art.Cylinder, i == 0 ? bark : ring, new Vector3(0, StraightH * (0.25f + i * 0.5f), 0), new Vector3(TrunkW, StraightH * 0.25f + 0.01f, TrunkW));
            // above that it thins and leans
            int segs = 5;
            float segH = Mathf.Max(0.6f, (h + 2.2f - StraightH) / segs);
            var pos = new Vector3(0, StraightH, 0);
            var rot = Quaternion.identity;
            for (int i = 0; i < segs; i++)
            {
                rot = Quaternion.Euler(lean * (i + 1) / segs * 1.6f, 0, 0);
                var up = rot * Vector3.up;
                float w = Mathf.Lerp(0.56f, 0.42f, i / (float)(segs - 1));
                Art.Part(root, Art.Cylinder, i % 2 == 0 ? ring : bark, pos + up * segH * 0.5f, new Vector3(w, segH * 0.5f + 0.02f, w), rot.eulerAngles);
                pos += up * segH;
            }
            Art.Part(root, Art.Cylinder, ring, new Vector3(0, 0.12f, 0), new Vector3(0.95f, 0.12f, 0.95f)); // root flare (under 0.3 m: clear of the X)
            var leaf = TmKit.Shade(Color.Lerp(new Color(0.25f, 0.62f, 0.2f), new Color(0.42f, 0.74f, 0.26f), R()), 1f);
            int fronds = 7 + rng.Next(2);
            for (int k = 0; k < fronds; k++)
            {
                var f = new GameObject("frond").transform;
                f.SetParent(root, false);
                f.localPosition = pos;
                f.localRotation = Quaternion.Euler(0, k * (360f / fronds) + R() * 20f, 0);
                float up = 14f + R() * 14f;
                Art.Box(f, leaf, new Vector3(0, 0.05f, 1.3f), new Vector3(1f, 0.06f, 2.8f), new Vector3(up, 0, 0));
                Art.Box(f, TmKit.Shade(leaf, 0.82f), new Vector3(0, -0.75f, 3.1f), new Vector3(0.8f, 0.05f, 1.7f), new Vector3(up + 30f, 0, 0));
            }
            Art.Part(root, Art.Sphere, TmKit.Shade(leaf, 0.9f), pos + Vector3.up * 0.1f, new Vector3(0.9f, 0.5f, 0.9f)); // the crown
            for (int k = 0; k < 4; k++)
                Art.Part(root, Art.Sphere, new Color(0.38f, 0.26f, 0.13f), pos + Quaternion.Euler(0, k * 90f + 20f, 0) * new Vector3(0.36f, -0.35f, 0), Vector3.one * 0.36f);
            return true;
        }

        /// <summary>The game's berry bush, cactus-green with pink-red cactus fruit (press E for food).</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.33f, 0.56f, 0.3f), new Color(0.93f, 0.26f, 0.42f));
            return true;
        }

        /// <summary>The zebra: the game's horse, white with black stripes, a black-and-white mane and a dark muzzle
        /// (now and then a rare golden one).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var coat = unicorn ? new Color(1f, 0.9f, 0.55f) : new Color(0.95f, 0.94f, 0.9f);
            var stripe = unicorn ? new Color(0.75f, 0.5f, 0.12f) : new Color(0.08f, 0.08f, 0.09f);
            var neck = TmKit.Horse(t, ghost, coat, stripe, stripe, out saddle, out head, out tail, legs, out var legT, stripe, 0.16f);
            // stripes round the body (over the top and down the sides), up the neck and round the legs
            for (int i = 0; i < 7; i++)
            {
                float z = -0.62f + i * 0.2f, w = i % 2 == 0 ? 0.07f : 0.05f;
                Art.Box(t, stripe, new Vector3(0, 1.15f, z), new Vector3(0.62f, 0.62f, w));
            }
            for (int i = 0; i < 4; i++)
                Art.Box(neck, stripe, new Vector3(0, 0.3f, 0.12f) + Quaternion.Euler(25, 0, 0) * Vector3.up * (-0.24f + i * 0.16f), new Vector3(0.34f, 0.045f, 0.37f), new Vector3(25, 0, 0));
            for (int i = 0; i < 3; i++)
                Art.Box(neck, stripe, new Vector3(0, 0.62f, 0.25f + i * 0.13f), new Vector3(0.31f, 0.31f, 0.035f));
            foreach (var leg in legT)
                for (int k = 0; k < 3; k++)
                    Art.Box(leg, stripe, new Vector3(0, -0.2f - k * 0.18f, 0), new Vector3(0.17f, 0.045f, 0.17f));
            return true;
        }

        // =====================================================================

        public override void BuildProps(Transform root)
        {
            TmKit.TintWater(root, new Color(0.18f, 0.68f, 0.82f));
            var rng = TmKit.Rng(5100);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var sec = TmKit.Sector(root, "Islands props");
            var bc = Cfg.BaseCenter[0];

            // ---- blue's island: only beach bits round the base (the island is all build space): driftwood, shells,
            //      starfish, and a few rocks out in the shallows ----
            for (int i = 0, made = 0; i < 400 && made < 12; i++)
            {
                float a = Rn(0, Mathf.PI * 2f), dist = Rn(Cfg.BaseHalf + 2f, IslandR + 2.5f);
                var p = bc + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * dist;
                if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 1.5f && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 1.5f) continue;
                if (!Cfg.InFirstSector(p, 2f) || Mathf.Abs(p.x) > Cfg.MapHalf - 3f || Mathf.Abs(p.z) > Cfg.MapHalf - 3f) continue;
                float hh = ThemeMaps.Height(p.x, p.z);
                p.y = hh;
                switch (made % 4)
                {
                    case 0: // driftwood on the sand
                        if (hh < -0.3f || hh > 0.3f) continue;
                        Art.Part(sec, Art.Cylinder, new Color(0.7f, 0.6f, 0.45f), p + Vector3.up * 0.15f, new Vector3(0.3f, Rn(0.9f, 1.4f), 0.3f), new Vector3(90, Rn(0, 360), 0));
                        break;
                    case 1: // a shell
                        if (hh < -0.3f) continue;
                        Art.Part(sec, Art.Sphere, new Color(0.98f, 0.82f, 0.75f), p + Vector3.up * 0.05f, new Vector3(0.35f, 0.15f, 0.3f), new Vector3(0, Rn(0, 360), 0));
                        break;
                    case 2: // a starfish
                    {
                        if (hh < -0.4f) continue;
                        float y0 = Rn(0, 72);
                        for (int k = 0; k < 5; k++)
                            Art.Box(sec, new Color(0.95f, 0.5f, 0.3f), p + Vector3.up * 0.03f + Quaternion.Euler(0, y0 + k * 72f, 0) * new Vector3(0, 0, 0.16f), new Vector3(0.12f, 0.05f, 0.32f), new Vector3(0, y0 + k * 72f, 0));
                        break;
                    }
                    default: // a rock in the shallows (on the sloped shore, not out over the deep water)
                    {
                        if (hh > -0.5f || hh < -1.2f) continue;
                        float sc = Rn(0.7f, 1.2f);
                        Art.Part(sec, TmKit.Rock(made), TmKit.Shade(Sandstone2, Rn(0.85f, 1f)), p + Vector3.up * sc * 0.35f, new Vector3(sc * 1.3f, sc, sc * 1.1f), new Vector3(0, Rn(0, 360), 0), true);
                        break;
                    }
                }
                made++;
            }
            TmKit.CopyRound(sec, IslandUsed);

            // far-off little islands on the horizon (scenery only)
            float half = Cfg.MapHalf;
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f + 0.3f, d = half * Rn(1.35f, 1.7f), s = Rn(12f, 26f) * Mathf.Max(0.7f, half / 100f);
                var at = new Vector3(Mathf.Cos(a) * d, ThemeMaps.WaterY - s * 0.15f, Mathf.Sin(a) * d);
                var isle = Art.Part(root, Art.Sphere, Sand, at, new Vector3(s * 2f, s * 0.45f, s * 1.6f), new Vector3(0, Rn(0, 360), 0));
                isle.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                var blob = Art.Part(root, TmKit.Blob(i), new Color(0.35f, 0.72f, 0.32f), at + Vector3.up * s * 0.3f, new Vector3(s * 0.8f, s * 0.6f, s * 0.8f));
                blob.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>The middle island: sandstone block stacks with ladders, a wooden deck, crates, sea-glass blocks and a
        /// driftwood barricade round the ball - things to duck behind and climb on.</summary>
        public override bool BuildCentre(Transform root)
        {
            var spots = new (float f, float r)[] { (-0.3f, 7.5f), (0.27f, 8f), (0f, 12.5f), (-0.15f, 16f), (0.36f, 13.5f), (-0.38f, 12f), (0.14f, 18f), (-0.05f, 19.5f) };
            TmKit.CentreLayout(root, "Islands centre", 5150, spots, CenterR - 3.5f, 5.5f, (sec, p, yaw, i, rng) =>
            {
                switch (i)
                {
                    case 0: case 6: Crates(sec, p, yaw, rng); break;
                    case 1: case 7: GlassBlock(sec, p, yaw, rng); break;
                    case 2: case 5: BlockStack(sec, p, yaw, rng, small: true); break;
                    case 3: Barricade(sec, p, yaw); break;
                    default: Platform(sec, p, yaw, rng, 2.4f); break;
                }
            });
            return true;
        }

        /// <summary>Stacked sandstone blocks (2-3 tiers), each with an orange ladder up its face; a crate on top.</summary>
        static void BlockStack(Transform sec, Vector3 p, float yaw, System.Random rng, bool small = false)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            int tiers = small ? 2 : 3;
            float y = p.y - 0.3f, size = small ? 4.5f : 6f;
            for (int t = 0; t < tiers; t++)
            {
                float hgt = 2.2f;
                var c = p;
                c.y = y + hgt * 0.5f;
                Art.Box(sec, t % 2 == 0 ? Sandstone : Sandstone2, c, new Vector3(size, hgt, size), new Vector3(0, yaw, 0), true);
                // the ladder up this tier, on the face towards -z local (alternating sides)
                var face = rot * (t % 2 == 0 ? Vector3.back : Vector3.right);
                var foot = c + face * (size * 0.5f) + Vector3.down * (hgt * 0.5f) + rot * (t % 2 == 0 ? Vector3.right : Vector3.forward) * (size * 0.2f);
                if (t > 0) foot.y = y; // standing on the tier below
                TmKit.Ladder(sec, foot, y + hgt, face, LadderOrange);
                y += hgt;
                size *= 0.62f;
            }
            var tc = p;
            tc.y = y + 0.55f;
            Crate(sec, tc, yaw + 20f);
        }

        /// <summary>A wooden deck on four posts (a table you can stand on) with a ladder, a crate on it.</summary>
        static void Platform(Transform sec, Vector3 p, float yaw, System.Random rng, float hgt)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            float w = 5f, top = p.y + hgt;
            Art.Box(sec, Plank, new Vector3(p.x, top - 0.15f, p.z), new Vector3(w, 0.3f, w), new Vector3(0, yaw, 0), true);
            Art.Box(sec, PlankDark, new Vector3(p.x, top - 0.33f, p.z), new Vector3(w - 0.3f, 0.08f, w - 0.3f), new Vector3(0, yaw, 0));
            for (int i = 0; i < 4; i++)
            {
                var c = p + rot * new Vector3((i % 2 == 0 ? -1 : 1) * (w * 0.5f - 0.3f), 0, (i < 2 ? -1 : 1) * (w * 0.5f - 0.3f));
                float fl = ThemeMaps.Height(c.x, c.z);
                float ph = top - fl + 0.3f;
                Art.Box(sec, PlankDark, new Vector3(c.x, fl - 0.3f + ph * 0.5f, c.z), new Vector3(0.35f, ph, 0.35f), new Vector3(0, yaw, 0), true);
            }
            var face = rot * Vector3.back;
            var foot = p + face * (w * 0.5f);
            foot.y = ThemeMaps.Height(foot.x, foot.z);
            TmKit.Ladder(sec, foot, top, face, LadderOrange);
            Crate(sec, p + rot * new Vector3(1.2f, 0, 1.1f) + Vector3.up * (hgt + 0.6f), yaw + 15f);
        }

        static void Crates(Transform sec, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            Crate(sec, p + rot * new Vector3(0, 0.6f, 0), yaw);
            Crate(sec, p + rot * new Vector3(1.3f, 0.6f, 0.2f), yaw + 8f);
            Crate(sec, p + rot * new Vector3(0.6f, 1.8f, 0.1f), yaw - 12f);
            if (rng.NextDouble() < 0.6) Crate(sec, p + rot * new Vector3(-0.4f, 0.6f, 1.4f), yaw + 30f);
        }

        static void Crate(Transform sec, Vector3 c, float yaw)
        {
            Art.Box(sec, new Color(0.72f, 0.56f, 0.36f), c, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(0, yaw, 0), true);
            Art.Box(sec, new Color(0.55f, 0.4f, 0.24f), c, new Vector3(1.24f, 0.18f, 1.24f), new Vector3(0, yaw, 0));
            Art.Box(sec, new Color(0.55f, 0.4f, 0.24f), c, new Vector3(0.18f, 1.24f, 1.24f), new Vector3(0, yaw, 0));
        }

        /// <summary>Sea-glass blocks: chunky pale turquoise slabs, the low one a step up to the high one.</summary>
        static void GlassBlock(Transform sec, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            Art.Box(sec, SeaGlass, p + Vector3.up * 0.5f, new Vector3(4.5f, 1.4f, 3.5f), new Vector3(0, yaw, 0), true);
            Art.Box(sec, Color.Lerp(SeaGlass, Color.white, 0.2f), p + rot * new Vector3(1.2f, 1.6f, 0.4f), new Vector3(2.6f, 1.3f, 2.6f), new Vector3(0, yaw + 12f, 0), true);
        }

        /// <summary>A driftwood barricade: three bleached logs stacked between posts, chest high.</summary>
        static void Barricade(Transform sec, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var drift = new Color(0.74f, 0.66f, 0.52f);
            for (int k = 0; k < 3; k++)
            {
                var c = p + Vector3.up * (0.25f + k * 0.42f) + rot * new Vector3(k == 1 ? 0.2f : 0f, 0, 0);
                float len = 2.1f - k * 0.2f;
                TmKit.Rod(sec, TmKit.Shade(drift, 1f - k * 0.06f), c - rot * Vector3.right * len, c + rot * Vector3.right * len, 0.45f, 0.45f, true, Art.Cylinder);
            }
            for (int s = -1; s <= 1; s += 2)
                Art.Part(sec, Art.Cylinder, PlankDark, p + rot * new Vector3(s * 1.5f, 0.75f, 0.32f), new Vector3(0.2f, 0.8f, 0.2f), default, true);
        }
    }

    /// <summary>Shared bits for the Islands / Jungle / Swamp / Ice maps.</summary>
    public static class TmKit
    {
        /// <summary>A colour darker / lighter by k (alpha stays 1).</summary>
        public static Color Shade(Color c, float k) => new Color(Q(c.r * k), Q(c.g * k), Q(c.b * k), 1f);
        /// <summary>(rounded to 1/40 steps, so random shades share a few materials instead of one each)</summary>
        static float Q(float v) => Mathf.Round(Mathf.Clamp01(v) * 40f) / 40f;

        /// <summary>ThemeMaps.SymNoiseP with the same spread for two teams and four (averaging four samples squashes it).</summary>
        public static float SymN(float x, float z, float f, float o) => 0.5f + (ThemeMaps.SymNoiseP(x, z, f, o) - 0.5f) * (Cfg.FourWay ? 1.55f : 1f);

        public static System.Random Rng(int salt) => new System.Random(salt + Cfg.MapSeed * 7919);

        /// <summary>A whole-number hash of (i, j) and the map seed, as 0..1 (the same on every peer).</summary>
        public static float Hash(int i, int j, int salt)
        {
            unchecked
            {
                uint h = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ (uint)((Cfg.MapSeed + salt) * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; h *= 0x27d4eb2d; h ^= h >> 16;
                return (h & 0xffffff) / (float)0x1000000;
            }
        }

        /// <summary>A group at the world origin for blue's sector: build into it, then CopyRound copies it to the others.</summary>
        public static Transform Sector(Transform root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            return go.transform;
        }

        /// <summary>Copies the sector group round to every other team's sector (Cfg.Copies, turned about the middle).</summary>
        public static void CopyRound(Transform sector, System.Func<int, bool> keep = null)
        {
            for (int m = 1; m < Cfg.Copies; m++)
            {
                if (keep != null && !keep(m)) continue;
                var c = Object.Instantiate(sector.gameObject, sector.parent);
                c.name = sector.name + " " + m;
                c.transform.position = Vector3.zero;
                c.transform.rotation = Quaternion.Euler(0, 90f * m * (4 / Cfg.Copies), 0);
            }
        }

        /// <summary>The point turned round into blue's sector (for things laid out once there and copied round).</summary>
        public static Vector3 ToFirstSector(Vector3 p)
        {
            for (int k = 0; k < Cfg.Copies; k++)
            {
                var q = Cfg.Copy(p, k);
                if (Cfg.InFirstSector(q, 0f)) return q;
            }
            return p;
        }

        /// <summary>In blue's sector, away from its base, the middle and the edge.</summary>
        public static bool FreeSpot(Vector3 p, float clear)
        {
            if (!Cfg.InFirstSector(p, clear)) return false;
            var bc = Cfg.BaseCenter[0];
            if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + clear && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + clear) return false;
            if (new Vector2(p.x, p.z).magnitude < 20f + clear) return false;
            return Mathf.Abs(p.x) < Cfg.MapHalf - clear && Mathf.Abs(p.z) < Cfg.MapHalf - clear;
        }

        static readonly Mesh[] s_Blobs = new Mesh[8], s_Rocks = new Mesh[8];
        /// <summary>A faceted low-poly blob (shared meshes).</summary>
        public static Mesh Blob(int i)
        {
            i = ((i % 8) + 8) % 8;
            if (s_Blobs[i] == null) s_Blobs[i] = Art.MakeRock(900 + i, 0.22f);
            return s_Blobs[i];
        }
        /// <summary>A craggier low-poly rock (shared meshes).</summary>
        public static Mesh Rock(int i)
        {
            i = ((i % 8) + 8) % 8;
            if (s_Rocks[i] == null) s_Rocks[i] = Art.MakeRock(950 + i, 0.32f);
            return s_Rocks[i];
        }

        /// <summary>A box whose top face runs from a to b (walkways, ramps, bridges).</summary>
        public static GameObject Plank(Transform parent, Vector3 a, Vector3 b, float width, float thick, Color c, bool collider)
        {
            var d = b - a;
            var rot = Quaternion.LookRotation(d.normalized, Vector3.up);
            var mid = (a + b) * 0.5f - rot * Vector3.up * (thick * 0.5f);
            return Art.Box(parent, c, mid, new Vector3(width, thick, d.magnitude), rot.eulerAngles, collider);
        }

        /// <summary>A bar from a to b (w wide, d deep): a box, or a cylinder when mesh = Art.Cylinder.</summary>
        public static GameObject Rod(Transform parent, Color c, Vector3 a, Vector3 b, float w, float d, bool collider = false, Mesh mesh = null)
        {
            var v = b - a;
            float len = v.magnitude;
            var rot = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, v / len) : Quaternion.identity;
            bool cyl = mesh == Art.Cylinder;
            return Art.Part(parent, mesh ?? Art.Cube, c, (a + b) * 0.5f, new Vector3(w, cyl ? len * 0.5f : len, d), rot.eulerAngles, collider);
        }

        /// <summary>A flat round stepping stone / pad (top at top.y) with an exact convex collider.</summary>
        public static GameObject Stone(Transform parent, Vector3 top, float radius, Color c, float depth = 3f)
        {
            float floor = ThemeMaps.Height(top.x, top.z);
            float hgt = Mathf.Max(0.3f, top.y - floor + 0.4f);
            hgt = Mathf.Min(hgt, depth);
            var go = Art.Part(parent, Art.Cylinder, c, new Vector3(top.x, top.y - hgt * 0.5f, top.z), new Vector3(radius * 2f, hgt * 0.5f, radius * 2f));
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = Art.Cylinder;
            mc.convex = true;
            return go;
        }

        /// <summary>A climbable ladder up a wall. foot: on the ground at the wall's face; faceOut: away from the wall.
        /// topY: the floor you step off onto at the top (its edge must be right at the wall's face, open - no railing).
        /// Parent must be at the world origin, only turned about y (a Sector).
        /// Climbable from the front only: the ladder itself is a solid board (you can't walk through it or climb it from
        /// behind or the sides) up to just under the floor at the top. The rungs are spread evenly so the last is just
        /// under the floor, and the rails stop 0.2 m above it (nothing sticking up over the deck). The climbing volume is
        /// a thin slab right on the board's front face (0.4 m deep, the ladder's width), so you only grab it when you're
        /// touching the ladder; it reaches 0.6 m above the floor at the top (invisible), so you climb until your feet
        /// clear the floor and simply walk forwards onto it.</summary>
        public static void Ladder(Transform parent, Vector3 foot, float topY, Vector3 faceOut, Color c, bool solid = true)
        {
            faceOut.y = 0;
            faceOut.Normalize();
            float h = topY - foot.y;
            var rot = Quaternion.LookRotation(-faceOut);
            var right = rot * Vector3.right;
            var e = rot.eulerAngles;
            var b = foot + faceOut * 0.12f;
            // rungs from 0.35 m up to just under the floor at the top, evenly spaced (about 0.45 m apart)
            const float first = 0.35f;
            float last = Mathf.Max(first, h - 0.1f);
            int gaps = Mathf.CeilToInt((last - first) / 0.45f - 0.001f);
            float step = gaps > 0 ? (last - first) / gaps : 0f;
            for (int i = 0; i <= gaps; i++)
                Art.Box(parent, c, b + Vector3.up * (first + i * step), new Vector3(0.72f, 0.07f, 0.07f), e);
            // the rails end just past the top rung
            float len = last + 0.2f;
            Art.Box(parent, c, b + right * 0.36f + Vector3.up * (len * 0.5f), new Vector3(0.09f, len, 0.09f), e);
            Art.Box(parent, c, b - right * 0.36f + Vector3.up * (len * 0.5f), new Vector3(0.09f, len, 0.09f), e);
            if (solid)
            {
                // one solid board round the rails and rungs, up to just under the floor at the top (so you step off over it)
                float sh = Mathf.Max(0.2f, h - 0.05f);
                var body = new GameObject("ladder body");
                body.transform.SetParent(parent, false);
                body.transform.localPosition = b + Vector3.up * (sh * 0.5f);
                body.transform.localRotation = rot;
                body.AddComponent<BoxCollider>().size = new Vector3(0.84f, sh, 0.2f);
            }
            // the climbing volume: a thin slab on the board's front face (0.22 - 0.62 m out from the wall), the ladder's
            // width + 0.1, up to 0.6 m over the floor at the top
            float vh = Mathf.Max(0.6f, h + 0.6f);
            var go = new GameObject("ladder");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = foot + faceOut * 0.42f + Vector3.up * (vh * 0.5f);
            go.transform.localRotation = rot;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(0.92f, vh, 0.4f);
            go.AddComponent<Ladder>().TopLocalY = topY;
        }

        /// <summary>Recolours the wadeable water surface ThemeMaps added (it is built before the props).</summary>
        public static void TintWater(Transform root, Color c, float smooth = 0.85f)
        {
            var w = root.Find("Water");
            if (w == null) return;
            var mr = w.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) return;
            var m = mr.sharedMaterial;
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        }

        /// <summary>A material that glows (lanterns, the shrine light).</summary>
        public static Material Glow(Color c, float power)
        {
            var mat = new Material(Art.Mat(c));
            if (mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", c * power); }
            return mat;
        }

        static readonly Dictionary<Color, Material> s_Glows = new Dictionary<Color, Material>();
        /// <summary>A glowing material, shared (for things built many times over: bushes, mounts).</summary>
        public static Material GlowShared(Color c, float power)
        {
            var key = new Color(c.r, c.g, c.b, power);
            if (s_Glows.TryGetValue(key, out var m) && m) return m;
            m = Glow(c, power);
            s_Glows[key] = m;
            return m;
        }

        // ---- the middle of the map (ThemeMap.BuildCentre) ----

        /// <summary>A spot in blue's part of the middle: f across the sector (-0.5 .. 0.5, 0 = straight towards blue's
        /// base), r metres from the ball.</summary>
        public static Vector3 CentreSpot(float f, float r) => Quaternion.Euler(0, f * 360f / Cfg.Copies, 0) * new Vector3(0, 0, -r);

        /// <summary>
        /// Lays out the middle round the ball: each spot (f, r: see CentreSpot; r capped at maxR, never under 6.5 so the
        /// ball's own few metres stay clear) gets build(sector, ground point, yaw facing the middle, index, rng) - skipped if
        /// it would be within `gap` of another (counting every team's copy) or of the signpost, or `avoid` says no (given
        /// each team's copy of the spot) - then copied round.
        /// </summary>
        public static void CentreLayout(Transform root, string name, int salt, (float f, float r)[] spots, float maxR, float gap,
            System.Action<Transform, Vector3, float, int, System.Random> build, System.Func<Vector3, bool> avoid = null)
        {
            var sec = Sector(root, name);
            var rng = Rng(salt);
            var placed = new List<Vector3>();
            var sign = CentreSign.Dir * CentreSign.Dist;
            for (int i = 0; i < spots.Length; i++)
            {
                float r = Mathf.Clamp(spots[i].r, 6.5f, Mathf.Max(6.5f, maxR));
                var p = CentreSpot(spots[i].f, r);
                if (!Cfg.InFirstSector(p, 1.5f)) continue;
                bool ok = true;
                for (int k = 0; k < Cfg.Copies && ok; k++)
                {
                    var q = Cfg.Copy(p, k);
                    if (new Vector2(q.x - sign.x, q.z - sign.z).magnitude < 3.2f) ok = false;
                    if (avoid != null && avoid(q)) ok = false;
                    foreach (var o in placed) if (new Vector2(q.x - o.x, q.z - o.z).magnitude < gap) { ok = false; break; }
                }
                if (!ok) continue;
                placed.Add(p);
                p.y = ThemeMaps.Height(p.x, p.z);
                float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg + (float)(rng.NextDouble() * 30.0 - 15.0);
                build(sec, p, yaw, i, rng);
            }
            CopyRound(sec);
        }

        // ---- rideable creatures (ThemeMap.BuildMount) ----

        /// <summary>A hit box on a body part (shots find it; it bumps nothing), as the horse has.</summary>
        public static GameObject Hit(GameObject g, Material ghost)
        {
            if (ghost != null) return g;
            g.AddComponent<BoxCollider>();
            g.layer = PlayerNet.HitboxLayer;
            return g;
        }

        public static Transform Pivot(Transform parent, string name, Vector3 at)
        {
            var p = new GameObject(name).transform;
            p.SetParent(parent, false);
            p.localPosition = at;
            return p;
        }

        /// <summary>The head's own solid box (a hit there does double damage), named like the horse's.</summary>
        public static GameObject HeadBox(Transform pivot, Color c, Vector3 pos, Vector3 size, Material ghost, Mesh mesh = null)
        {
            var g = Art.Part(pivot, mesh ?? Art.Cube, c, pos, size);
            if (ghost == null) g.AddComponent<BoxCollider>();
            g.name = "horse head";
            return g;
        }

        /// <summary>A straight leg swinging from the hip (len long, w thick), its foot a different colour.</summary>
        public static Transform Leg(Transform t, Vector3 hip, float len, float w, Color c, Color foot, Material ghost, List<Transform> legs)
        {
            var leg = Pivot(t, "leg", hip);
            Hit(Art.Box(leg, c, new Vector3(0, -len * 0.5f, 0), new Vector3(w, len, w)), ghost);
            Art.Box(leg, foot, new Vector3(0, -len + 0.06f, 0.03f), new Vector3(w * 1.15f, 0.12f, w * 1.3f));
            legs?.Add(leg);
            return leg;
        }

        /// <summary>
        /// The game's horse (Vehicle.CreateVisual), in a map's colours: the same box body, neck, mane, head, tail and four
        /// legs, the same hit boxes and saddle - so every map's mount is a real horse shape, just a different animal.
        /// Returns the neck pivot (the head: it bobs and grazes) for the map's own touches; legT: the four leg pivots.
        /// </summary>
        public static Transform Horse(Transform t, Material ghost, Color coat, Color mane, Color hoof, out Transform saddle, out Transform head,
            out Transform tail, List<Transform> legs, out Transform[] legT, Color? muzzle = null, float earLen = 0.14f)
        {
            Hit(Art.Box(t, coat, new Vector3(0, 1.15f, 0), new Vector3(0.6f, 0.6f, 1.5f)), ghost);
            var neck = Pivot(t, "neck", new Vector3(0, 1.35f, 0.65f));
            Hit(Art.Box(neck, coat, new Vector3(0, 0.3f, 0.12f), new Vector3(0.32f, 0.7f, 0.35f), new Vector3(25, 0, 0)), ghost);
            Hit(Art.Box(neck, mane, new Vector3(0, 0.35f, -0.02f), new Vector3(0.1f, 0.7f, 0.12f), new Vector3(25, 0, 0)), ghost);
            HeadBox(neck, coat, new Vector3(0, 0.62f, 0.42f), new Vector3(0.3f, 0.3f, 0.6f), ghost);
            Hit(Art.Box(neck, muzzle ?? Shade(mane, 1.5f), new Vector3(0, 0.56f, 0.7f), new Vector3(0.26f, 0.2f, 0.12f)), ghost);
            Art.Box(neck, Color.black, new Vector3(0.16f, 0.7f, 0.5f), new Vector3(0.02f, 0.06f, 0.06f));
            Art.Box(neck, Color.black, new Vector3(-0.16f, 0.7f, 0.5f), new Vector3(0.02f, 0.06f, 0.06f));
            Art.Box(neck, coat, new Vector3(0.09f, 0.75f + earLen * 0.5f, 0.25f), new Vector3(0.06f, earLen, 0.06f));
            Art.Box(neck, coat, new Vector3(-0.09f, 0.75f + earLen * 0.5f, 0.25f), new Vector3(0.06f, earLen, 0.06f));
            head = neck;
            var tl = Pivot(t, "tail", new Vector3(0, 1.3f, -0.76f));
            Hit(Art.Box(tl, mane, new Vector3(0, -0.3f, -0.05f), new Vector3(0.12f, 0.65f, 0.12f)), ghost);
            tail = tl;
            legT = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.2f : 0.2f, z = i < 2 ? 0.55f : -0.55f;
                var leg = Pivot(t, "leg", new Vector3(x, 0.9f, z));
                Hit(Art.Box(leg, coat, new Vector3(0, -0.4f, 0), new Vector3(0.16f, 0.8f, 0.16f)), ghost);
                Art.Box(leg, hoof, new Vector3(0, -0.85f, 0), new Vector3(0.18f, 0.12f, 0.18f));
                legs?.Add(leg);
                legT[i] = leg;
            }
            saddle = Saddle(t, ghost, 1.47f, 0.6f);
            return neck;
        }

        /// <summary>The saddle (shown once it's saddled): seat at seatY, with the team-coloured "blanket" under it.</summary>
        public static Transform Saddle(Transform t, Material ghost, float seatY, float width, float z = -0.05f, Vector3? blanketSize = null, float blanketDrop = 0.27f)
        {
            var sd = Pivot(t, "saddle", Vector3.zero);
            var leather = new Color(0.35f, 0.18f, 0.08f);
            Hit(Art.Box(sd, leather, new Vector3(0, seatY, z), new Vector3(width + 0.04f, 0.08f, 0.55f)), ghost);
            Art.Box(sd, leather, new Vector3(0, seatY + 0.08f, z + 0.25f), new Vector3(0.3f, 0.12f, 0.08f));
            var blanket = Art.Box(sd, new Color(0.8f, 0.2f, 0.15f), new Vector3(0, seatY - blanketDrop, z), blanketSize ?? new Vector3(width + 0.06f, 0.5f, 0.45f));
            blanket.name = "blanket";
            return sd;
        }
    }

    /// <summary>Fog, ambient light, sun and sky tint for a map; puts back exactly what was there.</summary>
    public class TmSky
    {
        bool m_On, m_Fog, m_HasTint, m_HasExp;
        FogMode m_FogMode;
        Color m_FogCol, m_ASky, m_AEq, m_AGround, m_ALight, m_SunCol, m_Tint;
        float m_Dens, m_Start, m_End, m_SunInt, m_Exp;
        AmbientMode m_AMode;
        Light m_Sun;
        Material m_Sky;

        static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.enabled) return l;
            return null;
        }

        public void Apply(Color fog, float density, Color ambSky, Color ambEq, Color ambGround, Color sunCol, float sunMul, Color skyTint, float exposureMul)
        {
            if (!m_On)
            {
                m_On = true;
                m_Fog = RenderSettings.fog; m_FogMode = RenderSettings.fogMode; m_FogCol = RenderSettings.fogColor;
                m_Dens = RenderSettings.fogDensity; m_Start = RenderSettings.fogStartDistance; m_End = RenderSettings.fogEndDistance;
                m_AMode = RenderSettings.ambientMode; m_ASky = RenderSettings.ambientSkyColor; m_AEq = RenderSettings.ambientEquatorColor;
                m_AGround = RenderSettings.ambientGroundColor; m_ALight = RenderSettings.ambientLight;
                m_Sun = FindSun();
                if (m_Sun != null) { m_SunCol = m_Sun.color; m_SunInt = m_Sun.intensity; }
                m_Sky = RenderSettings.skybox;
                m_HasTint = m_Sky != null && m_Sky.HasProperty("_SkyTint");
                m_HasExp = m_Sky != null && m_Sky.HasProperty("_Exposure");
                if (m_HasTint) m_Tint = m_Sky.GetColor("_SkyTint");
                if (m_HasExp) m_Exp = m_Sky.GetFloat("_Exposure");
            }
            if (density > 0f)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = density;
                RenderSettings.fogColor = fog;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambSky;
            RenderSettings.ambientEquatorColor = ambEq;
            RenderSettings.ambientGroundColor = ambGround;
            if (m_Sun != null) { m_Sun.color = sunCol; m_Sun.intensity = m_SunInt * sunMul; }
            if (m_HasTint) m_Sky.SetColor("_SkyTint", skyTint);
            if (m_HasExp) m_Sky.SetFloat("_Exposure", m_Exp * exposureMul);
        }

        public void Restore()
        {
            if (!m_On) return;
            m_On = false;
            RenderSettings.fog = m_Fog; RenderSettings.fogMode = m_FogMode; RenderSettings.fogColor = m_FogCol;
            RenderSettings.fogDensity = m_Dens; RenderSettings.fogStartDistance = m_Start; RenderSettings.fogEndDistance = m_End;
            RenderSettings.ambientMode = m_AMode; RenderSettings.ambientSkyColor = m_ASky; RenderSettings.ambientEquatorColor = m_AEq;
            RenderSettings.ambientGroundColor = m_AGround; RenderSettings.ambientLight = m_ALight;
            if (m_Sun != null) { m_Sun.color = m_SunCol; m_Sun.intensity = m_SunInt; }
            if (m_Sky != null)
            {
                if (m_HasTint) m_Sky.SetColor("_SkyTint", m_Tint);
                if (m_HasExp) m_Sky.SetFloat("_Exposure", m_Exp);
            }
        }
    }
}
