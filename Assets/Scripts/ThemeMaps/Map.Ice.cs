using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// ICE: snowy cliff plateaus in terraces (3.2 m a step) over a lowland of sheet ice and frozen turquoise lakes.
    /// The ice is slippery (most of the open lowland); snow patches, the plateau tops, the bases and the middle are not.
    /// Wooden ramps up the cliffs, frozen waterfalls down them, snowy pines, carved stone crates and huts, and a
    /// minecart line across the ice in every team's part. Frost-berry bushes, shaggy snow ponies to ride, snow drifts, ice walls
    /// and a snowman round the ball.
    /// </summary>
    public class IceMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Ice;
        public override string Label => "Ice";
        public override string Blurb => "Snowy cliff plateaus over sheets of VERY SLIPPERY ice and frozen lakes. Snow gives you grip - on the ice you glide, and it takes a moment to turn or stop. Wooden ramps lead up the cliffs. Ride a snow pony.";
        public override bool Mountains => true;
        public override float MaxSpotHeight => 3.8f;

        /// <summary>Minecraft-like ice: pushing a direction you speed up / turn at IceSteer (a little resistance, never
        /// fighting you), and when you let go you glide on, slowing at IceGrip (a few seconds to stop from a run).</summary>
        public override float IceSteer => 6f;
        public override float IceGrip => 1f;
        public override string MountName => "Snow Pony";

        /// <summary>The game's berry bush, frosty blue-green with bright red berries (press E for food).</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.5f, 0.68f, 0.64f), new Color(0.9f, 0.12f, 0.18f));
            return true;
        }

        /// <summary>The snow pony: the game's horse, pale grey-white with a long shaggy pale mane and forelock, a shaggy
        /// winter coat hanging under its belly and thick feathered hooves (now and then a rare dark one).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var coat = unicorn ? new Color(0.3f, 0.32f, 0.38f) : new Color(0.86f, 0.87f, 0.9f);
            var mane = unicorn ? new Color(0.85f, 0.9f, 1f) : new Color(0.68f, 0.66f, 0.62f);
            var shag = TmKit.Shade(coat, 0.88f);
            var hoof = new Color(0.32f, 0.3f, 0.3f);
            var neck = TmKit.Horse(t, ghost, coat, mane, hoof, out saddle, out head, out tail, legs, out var legT, new Color(0.45f, 0.44f, 0.46f), 0.12f);
            // the shaggy winter coat: tufts hanging under the belly and down the chest
            for (int i = 0; i < 5; i++)
                for (int s = -1; s <= 1; s += 2)
                    Art.Box(t, shag, new Vector3(s * 0.27f, 0.83f, -0.56f + i * 0.28f), new Vector3(0.08f, 0.14f + (i % 2) * 0.05f, 0.24f), new Vector3(0, 0, s * 8f));
            Art.Box(t, shag, new Vector3(0, 0.84f, 0.66f), new Vector3(0.5f, 0.16f, 0.12f));
            // a long mane falling down one side of the neck, and a forelock over the face
            var up = Quaternion.Euler(25, 0, 0) * Vector3.up;
            for (int i = 0; i < 4; i++)
                Art.Box(neck, mane, new Vector3(0.1f, 0.3f, 0.02f) + up * (-0.22f + i * 0.15f) + Vector3.down * 0.06f, new Vector3(0.06f, 0.24f, 0.13f), new Vector3(25, 0, 10f));
            Art.Box(neck, mane, new Vector3(0, 0.74f, 0.34f), new Vector3(0.16f, 0.1f, 0.16f), new Vector3(30f, 0, 0));
            // a full tail
            Art.Box(tail, mane, new Vector3(0, -0.45f, -0.07f), new Vector3(0.2f, 0.45f, 0.16f));
            // feathered hooves: a fringe of long hair round each foot
            foreach (var leg in legT)
                Art.Box(leg, shag, new Vector3(0, -0.72f, 0), new Vector3(0.22f, 0.16f, 0.22f));
            return true;
        }

        /// <summary>The middle: snow drifts, carved stone crates, ice-block walls, ice crystals and a snowman round the ball.</summary>
        public override bool BuildCentre(Transform root)
        {
            var spots = new (float f, float r)[] { (-0.3f, 7.5f), (0.28f, 8f), (0f, 12f), (-0.15f, 15f), (0.38f, 13f), (-0.4f, 11.5f), (0.15f, 16.5f) };
            TmKit.CentreLayout(root, "Ice centre", 8450, spots, 17f, 5f, (sec, p, yaw, i, rng) =>
            {
                float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                switch (i)
                {
                    case 0: case 5: // a snow drift with a block of ice half buried in it
                    {
                        float sc = R(1.6f, 2f);
                        Art.Part(sec, TmKit.Blob(i), Snow, p + Vector3.up * sc * 0.3f, new Vector3(sc * 1.6f, sc * 0.8f, sc * 1.1f), new Vector3(0, yaw + 90f, 0), true);
                        Art.Box(sec, IceBlue, p + Quaternion.Euler(0, yaw, 0) * new Vector3(0.8f, 0.8f, 0.3f), new Vector3(1.2f, 1.2f, 1.2f), new Vector3(R(-10f, 10f), yaw + 30f, R(-10f, 10f)), true);
                        break;
                    }
                    case 1: case 6: CrateStack(sec, p, yaw, rng); break;
                    case 2: // a wall of ice blocks across the way in, its top broken
                    {
                        var o = Quaternion.Euler(0, yaw + 90f, 0);
                        float[] hs = { 1.1f, 1.7f, 1.4f, 0.8f };
                        for (int k = 0; k < 4; k++)
                            Art.Box(sec, k % 2 == 0 ? IceBlue : Color.Lerp(IceBlue, Color.white, 0.35f), p + o * new Vector3(0, hs[k] * 0.5f, (k - 1.5f) * 1.05f), new Vector3(0.9f, hs[k], 1f), new Vector3(0, yaw + 90f + R(-4f, 4f), 0), true);
                        Art.Part(sec, TmKit.Blob(3), Snow, p + Vector3.up * 1.75f + o * new Vector3(0, 0, -0.5f), new Vector3(0.9f, 0.25f, 1f), new Vector3(0, yaw, 0));
                        break;
                    }
                    case 3: // a snowman
                    {
                        Art.Part(sec, Art.Sphere, Snow, p + Vector3.up * 0.6f, Vector3.one * 1.3f, default, true);
                        Art.Part(sec, Art.Sphere, Snow, p + Vector3.up * 1.5f, Vector3.one * 0.9f, default, true);
                        Art.Part(sec, Art.Sphere, Snow, p + Vector3.up * 2.15f, Vector3.one * 0.6f);
                        var fwd = Quaternion.Euler(0, yaw + 180f, 0) * Vector3.forward;
                        Art.Part(sec, Art.Cone, new Color(0.95f, 0.5f, 0.1f), p + Vector3.up * 2.15f + fwd * 0.28f, new Vector3(0.1f, 0.3f, 0.1f), Quaternion.FromToRotation(Vector3.up, fwd).eulerAngles);
                        for (int s = -1; s <= 1; s += 2)
                            Art.Part(sec, Art.Sphere, Color.black, p + Vector3.up * 2.25f + fwd * 0.26f + Quaternion.Euler(0, yaw, 0) * new Vector3(s * 0.11f, 0, 0), Vector3.one * 0.07f);
                        Art.Box(sec, new Color(0.7f, 0.15f, 0.15f), p + Vector3.up * 1.9f, new Vector3(0.72f, 0.14f, 0.72f), new Vector3(0, yaw, 0)); // a scarf
                        break;
                    }
                    default: Crystals(sec, p, rng); Crystals(sec, p + Quaternion.Euler(0, yaw, 0) * new Vector3(1.6f, 0, 0.5f), rng); break;
                }
            });
            return true;
        }

        const float Step = 3.2f;
        static readonly Color Snow = new Color(0.93f, 0.95f, 0.98f), Rock = new Color(0.55f, 0.62f, 0.72f), IceSheet = new Color(0.72f, 0.88f, 0.94f), Lake = new Color(0.38f, 0.78f, 0.86f);
        static readonly Color Wood = new Color(0.5f, 0.34f, 0.2f), WoodDark = new Color(0.36f, 0.24f, 0.14f), Carved = new Color(0.36f, 0.38f, 0.26f), Carved2 = new Color(0.5f, 0.5f, 0.36f);
        static readonly Color StoneC = new Color(0.52f, 0.55f, 0.58f), Pine = new Color(0.16f, 0.36f, 0.26f), IceBlue = new Color(0.7f, 0.92f, 1f);

        static float S => ThemeMaps.SeedP;

        /// <summary>The terraced plateaus and the lakes, before the base / middle mask.</summary>
        static float Raw(float x, float z, out float lake)
        {
            float n = TmKit.SymN(x, z, 0.028f, S);
            float raw = Mathf.Max(0f, (n - 0.44f) * 32f) / Step;
            float level = Mathf.Min(Mathf.Floor(raw), 2f), fr = raw - level;
            float h = level >= 2f ? Step * 2f + Mathf.Min(fr, 1f) * 0.6f : Step * (level + ThemeMaps.SmoothStepP(0.55f, 1f, fr));
            float n2 = TmKit.SymN(x, z, 0.05f, S + 30f);
            lake = ThemeMaps.SmoothStepP(0.55f, 0.6f, n2) * (1f - ThemeMaps.SmoothStepP(0.2f, 1.2f, h));
            h += (ThemeMaps.SymNoiseP(x, z, 0.14f, S + 5f) - 0.5f) * 0.35f * (1f - lake);
            return Mathf.Lerp(h, -0.35f, lake);
        }

        public override float Height(float x, float z) => Raw(x, z, out _) * ThemeMaps.MaskP(x, z);

        /// <summary>Is the ground here ice (not snow)? The low ground away from the bases and the middle, but for snow patches.</summary>
        static bool IceAt(float x, float z, float h)
        {
            if (h > 0.9f || ThemeMaps.MaskP(x, z) < 0.6f) return false;
            Raw(x, z, out float lake);
            return lake > 0.3f || TmKit.SymN(x, z, 0.045f, S + 60f) > 0.35f;
        }
        static bool LakeAt(float x, float z) { Raw(x, z, out float lake); return lake > 0.5f; }

        public override bool Slippery(Vector3 p)
        {
            float h = ThemeMaps.Height(p.x, p.z);
            return p.y < h + 0.5f && IceAt(p.x, p.z, h);
        }

        public override bool SpotOk(Vector3 p) => !IceAt(p.x, p.z, p.y);
        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1.1f : kind == ResourceNode.Bush ? 0.5f : 1f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.85f, 0.92f, 0.95f), 0.4f);

        public override Color[] Palette => new[] { Snow, Rock, IceSheet, Lake };
        public override int ColourAt(Vector3 c, float slopeY)
        {
            if (slopeY < 0.72f) return 1;
            if (IceAt(c.x, c.z, c.y)) return LakeAt(c.x, c.z) ? 3 : 2;
            return 0;
        }

        /// <summary>The pines' trunk radius from the ground to 3.2 m (where the X goes: no branches below that).</summary>
        const float TrunkR = 0.3f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        /// <summary>Snowy pines: a straight bare trunk to 3.2 m (the weak spot X sits on it), then stacked dark cones,
        /// each with a cap of snow.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 41);
            float R() => (float)rng.NextDouble();
            var pine = TmKit.Shade(Pine, 0.9f + R() * 0.25f);
            const float Low = 3.2f; // (the lowest branches)
            float H = Mathf.Max(h * 1.35f + 2f, Low + 5f);
            // the trunk: one straight round cylinder, exactly TrunkR, from the ground up into the cones
            float th0 = Low + (H - Low) * 0.5f;
            Art.Part(tr, Art.Cylinder, WoodDark, new Vector3(0, th0 * 0.5f, 0), new Vector3(TrunkR * 2f, th0 * 0.5f, TrunkR * 2f));
            Art.Part(tr, TmKit.Blob(seed), Snow, new Vector3(0, 0.02f, 0), new Vector3(0.9f, 0.15f, 0.9f), new Vector3(0, R() * 360f, 0)); // (snow round its foot, under 0.3 m)
            int tiers = 4;
            float w0 = 4f + R() * 0.8f;
            for (int k = 0; k < tiers; k++)
            {
                float y = Low + k * (H - Low) / tiers * 0.85f;
                float w = w0 * (1f - k * 0.2f), th = 2.6f - k * 0.25f;
                float spin = R() * 60f;
                Art.Part(tr, Art.Cone, pine, new Vector3(0, y, 0), new Vector3(w, th, w), new Vector3(0, spin, 0));
                Art.Part(tr, Art.Cone, Snow, new Vector3(0, y + th * 0.42f, 0), new Vector3(w * 0.62f, th * 0.6f, w * 0.62f), new Vector3(0, spin + 25f, 0));
            }
            return true;
        }

        // =====================================================================

        readonly TmSky m_Sky = new TmSky();
        public override void ApplySky() => m_Sky.Apply(new Color(0.8f, 0.87f, 0.95f), 0.0045f,
            new Color(0.75f, 0.84f, 0.95f), new Color(0.6f, 0.68f, 0.78f), new Color(0.45f, 0.5f, 0.58f), new Color(0.95f, 0.97f, 1f), 1.05f,
            new Color(0.45f, 0.6f, 0.78f), 1.05f);
        public override void Cleanup() => m_Sky.Restore();

        public override void BuildProps(Transform root)
        {
            var rng = TmKit.Rng(8400);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var sec = TmKit.Sector(root, "Ice props");
            float half = Cfg.MapHalf, d = Mathf.Abs(Cfg.BaseCenter[0].z);
            float H(Vector3 p) => ThemeMaps.Height(p.x, p.z);
            var taken = new List<Vector3>();
            bool Clear(Vector3 p, float r) { foreach (var q in taken) if ((q - p).sqrMagnitude < r * r) return false; return true; }
            var dirs = new Vector3[8];
            for (int i = 0; i < 8; i++) dirs[i] = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;

            // a cliff edge: on a plateau top, with a drop of at least a step within 4 m that way
            bool Edge(Vector3 p, out Vector3 dir, out Vector3 edge)
            {
                dir = default; edge = default;
                float h = H(p);
                if (h < Step * 0.8f || Mathf.Abs(H(p + Vector3.right) - h) > 0.3f || Mathf.Abs(H(p + Vector3.forward) - h) > 0.3f) return false;
                int start = rng.Next(8);
                for (int k = 0; k < 8; k++)
                {
                    var dv = dirs[(start + k) % 8];
                    if (H(p + dv * 4f) > h - Step * 0.8f) continue;
                    float t = 0f;
                    while (t < 4f && H(p + dv * t) > h - 0.3f) t += 0.25f;
                    edge = p + dv * t;
                    edge.y = h;
                    dir = dv;
                    return true;
                }
                return false;
            }

            // ---- wooden ramps up the cliffs ----
            int ramps = Mathf.RoundToInt(6 * half / 100f);
            for (int i = 0, made = 0; i < ramps * 120 && made < ramps; i++)
            {
                var p = new Vector3(Rn(-half + 10f, half - 10f), 0, Rn(-half + 10f, -6f));
                if (!TmKit.FreeSpot(p, 4f) || !Clear(p, 16f) || !Edge(p, out var dv, out var e)) continue;
                float drop = e.y - H(e + dv * 9f);
                float len = Mathf.Max(4f, drop / Mathf.Tan(25f * Mathf.Deg2Rad));
                var foot = e + dv * len;
                if (!TmKit.FreeSpot(foot, 2f)) continue;
                foot.y = H(foot);
                if (e.y - foot.y < Step * 0.7f) continue;
                made++;
                taken.Add(p);
                taken.Add(foot);
                Ramp(sec, e - dv * 0.6f, foot);
            }
            // ---- frozen waterfalls down the cliffs, a frozen pool at the bottom ----
            int falls = Mathf.RoundToInt(5 * half / 100f);
            for (int i = 0, made = 0; i < falls * 120 && made < falls; i++)
            {
                var p = new Vector3(Rn(-half + 10f, half - 10f), 0, Rn(-half + 10f, -6f));
                if (!TmKit.FreeSpot(p, 4f) || !Clear(p, 12f) || !Edge(p, out var dv, out var e)) continue;
                made++;
                taken.Add(p);
                Icefall(sec, e, dv, rng);
            }
            // ---- a minecart line across the ice ----
            for (int tries = 0; tries < 200; tries++)
            {
                var a = new Vector3(Rn(-half + 10f, half - 10f), 0, Rn(-d, -24f));
                var dv = Quaternion.Euler(0, Rn(0, 360), 0) * Vector3.forward;
                float len = Mathf.Min(40f, half * 0.4f);
                bool ok = true;
                for (float t = 0; t <= len && ok; t += 2f)
                {
                    var q = a + dv * t;
                    float hq = H(q);
                    ok = TmKit.FreeSpot(q, 3f) && hq < 1f && hq > -0.5f && Clear(q, 6f);
                }
                if (!ok) continue;
                Rails(sec, a, a + dv * len, rng);
                for (float t = 0; t <= len; t += 4f) taken.Add(a + dv * t);
                break;
            }
            // ---- carved stone crates (snow on top), stone huts, ice crystals, snowballs ----
            int want = Mathf.RoundToInt(10 * half / 100f);
            for (int i = 0, made = 0; i < want * 50 && made < want; i++)
            {
                var p = new Vector3(Rn(-half + 8f, half - 8f), 0, Rn(-half + 8f, -5f));
                if (!TmKit.FreeSpot(p, 4f) || !Clear(p, 7f) || H(p) > 7.5f) continue;
                if (Mathf.Abs(H(p + Vector3.right * 2f) - H(p)) > 0.4f || Mathf.Abs(H(p + Vector3.forward * 2f) - H(p)) > 0.4f) continue;
                taken.Add(p);
                p.y = H(p);
                float yaw = Rn(0, 360);
                switch (made++ % 4)
                {
                    case 0: CrateStack(sec, p, yaw, rng); break;
                    case 1: StoneHut(sec, p, yaw); break;
                    case 2: Crystals(sec, p, rng); break;
                    default:
                        float sc = Rn(1.4f, 2.4f);
                        Art.Part(sec, TmKit.Blob(made), Snow, p + Vector3.up * sc * 0.35f, new Vector3(sc * 1.2f, sc * 0.8f, sc), new Vector3(0, yaw, 0), true);
                        Art.Part(sec, TmKit.Blob(made + 2), Snow, p + new Vector3(sc * 0.8f, sc * 0.2f, 0.3f), Vector3.one * sc * 0.5f, new Vector3(0, yaw, 0), true);
                        break;
                }
            }
            TmKit.CopyRound(sec);
        }

        static void Ramp(Transform sec, Vector3 top, Vector3 foot)
        {
            var dir = foot - top;
            dir.y = 0;
            dir.Normalize();
            TmKit.Plank(sec, top, foot + dir * 0.6f, 2.4f, 0.3f, Wood, true);
            var side = new Vector3(dir.z, 0, -dir.x);
            float len = new Vector2(foot.x - top.x, foot.z - top.z).magnitude;
            for (float t = 1.5f; t < len; t += 1.2f)
            {
                var c = top + dir * t;
                c.y = Mathf.Lerp(top.y, foot.y, t / len) + 0.02f;
                Art.Box(sec, WoodDark, c, new Vector3(2.5f, 0.06f, 0.18f), new Vector3(Mathf.Atan2(top.y - foot.y, len) * Mathf.Rad2Deg, Quaternion.LookRotation(dir).eulerAngles.y, 0));
            }
            for (float t = 2f; t < len - 0.5f; t += 3f)
                for (int s = -1; s <= 1; s += 2)
                {
                    var c = top + dir * t + side * (s * 1.1f);
                    float y = Mathf.Lerp(top.y, foot.y, t / len), g = ThemeMaps.Height(c.x, c.z);
                    if (y - g < 0.3f) continue;
                    Art.Box(sec, WoodDark, new Vector3(c.x, (y + g) * 0.5f - 0.2f, c.z), new Vector3(0.22f, y - g, 0.22f));
                }
        }

        static void Icefall(Transform sec, Vector3 edge, Vector3 dir, System.Random rng)
        {
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var side = new Vector3(dir.z, 0, -dir.x);
            // down the face: from the edge to where the cliff bottoms out
            float tb = 0.5f, top = edge.y;
            while (tb < 6f && ThemeMaps.Height(edge.x + dir.x * tb, edge.z + dir.z * tb) > top - Step * 0.85f) tb += 0.25f;
            var foot = edge + dir * (tb + 1.5f);
            float g = ThemeMaps.Height(foot.x, foot.z);
            foot.y = g;
            for (int i = -2; i <= 2; i++)
            {
                var off = side * (i * 0.7f + Rn(-0.15f, 0.15f));
                var a = edge + off + dir * 0.2f + Vector3.up * 0.3f;
                var b = edge + off + dir * (tb + Rn(0.2f, 0.8f));
                b.y = ThemeMaps.Height(b.x, b.z) + 0.2f;
                var dd = b - a;
                Art.Part(sec, Art.Cylinder, i % 2 == 0 ? IceBlue : Color.Lerp(IceBlue, Color.white, 0.4f), (a + b) * 0.5f + Vector3.up * 0.35f, new Vector3(0.75f, dd.magnitude * 0.5f + 0.3f, 0.6f), Quaternion.FromToRotation(Vector3.up, dd).eulerAngles);
            }
            Art.Part(sec, TmKit.Blob(3), Snow, edge + Vector3.up * 0.2f + dir * 0.3f, new Vector3(4.2f, 0.7f, 1.6f), Quaternion.LookRotation(dir).eulerAngles); // the snow lip
            var pool = Art.Part(sec, Art.Cylinder, Lake, new Vector3(foot.x, g + 0.03f, foot.z), new Vector3(5f, 0.04f, 4f), Quaternion.LookRotation(dir).eulerAngles);
            pool.name = "frozen pool";
            for (int k = 0; k < 3; k++)
                Art.Part(sec, Art.Cone, Color.Lerp(IceBlue, Color.white, 0.25f), foot + side * Rn(-2f, 2f) + dir * Rn(-0.5f, 1.5f) + Vector3.down * 0.1f, new Vector3(0.5f, Rn(0.8f, 1.6f), 0.5f), new Vector3(Rn(-15, 15), 0, Rn(-15, 15)));
        }

        static void Rails(Transform sec, Vector3 a, Vector3 b, System.Random rng)
        {
            var dir = (b - a).normalized;
            var side = new Vector3(dir.z, 0, -dir.x);
            float len = (b - a).magnitude, yaw = Quaternion.LookRotation(dir).eulerAngles.y;
            var steel = new Color(0.4f, 0.4f, 0.44f);
            float segL = 4f;
            for (float t = 0; t < len - 0.01f; t += segL)
            {
                var p0 = a + dir * t; var p1 = a + dir * Mathf.Min(len, t + segL);
                p0.y = ThemeMaps.Height(p0.x, p0.z) + 0.25f; p1.y = ThemeMaps.Height(p1.x, p1.z) + 0.25f;
                for (int s = -1; s <= 1; s += 2)
                    TmKit.Plank(sec, p0 + side * (s * 0.55f), p1 + side * (s * 0.55f), 0.1f, 0.12f, steel, false);
                for (float u = 0.6f; u < segL && t + u < len; u += 1.3f)
                {
                    var c = a + dir * (t + u);
                    c.y = ThemeMaps.Height(c.x, c.z) + 0.08f;
                    Art.Box(sec, WoodDark, c, new Vector3(1.6f, 0.14f, 0.3f), new Vector3(0, yaw, 0));
                }
            }
            // a minecart parked on it (solid: cover)
            var m = a + dir * len * (0.3f + (float)rng.NextDouble() * 0.4f);
            m.y = ThemeMaps.Height(m.x, m.z);
            Art.Box(sec, new Color(0.45f, 0.3f, 0.18f), m + Vector3.up * 0.95f, new Vector3(1.4f, 0.9f, 2f), new Vector3(0, yaw, 0), true);
            Art.Box(sec, steel, m + Vector3.up * 1.42f, new Vector3(1.5f, 0.1f, 2.1f), new Vector3(0, yaw, 0));
            Art.Part(sec, TmKit.Blob(1), Snow, m + Vector3.up * 1.5f, new Vector3(1.1f, 0.4f, 1.6f), new Vector3(0, yaw, 0));
            for (int i = 0; i < 4; i++)
            {
                var w = m + side * ((i % 2 == 0 ? -1 : 1) * 0.6f) + dir * ((i < 2 ? -1 : 1) * 0.6f) + Vector3.up * 0.45f;
                Art.Part(sec, Art.Cylinder, new Color(0.2f, 0.2f, 0.22f), w, new Vector3(0.6f, 0.06f, 0.6f), new Vector3(0, yaw, 90));
            }
        }

        /// <summary>Carved stone crates (like the temple blocks in the picture), stacked, with snow on top.</summary>
        static void CrateStack(Transform sec, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            void Block(Vector3 local)
            {
                var c = p + rot * local;
                Art.Box(sec, Carved, c, new Vector3(1.6f, 1.6f, 1.6f), new Vector3(0, yaw, 0), true);
                Art.Box(sec, Carved2, c, new Vector3(1.66f, 0.2f, 1.66f), new Vector3(0, yaw, 0));
                Art.Box(sec, Carved2, c, new Vector3(0.4f, 0.4f, 1.68f), new Vector3(0, yaw, 0));
            }
            Block(new Vector3(0, 0.75f, 0));
            Block(new Vector3(1.6f, 0.75f, 0));
            Block(new Vector3(1.6f, 2.35f, 0));
            if (rng.NextDouble() < 0.5) Block(new Vector3(0, 0.75f, 1.6f));
            Art.Part(sec, TmKit.Blob(4), Snow, p + rot * new Vector3(1.6f, 3.2f, 0), new Vector3(1.5f, 0.45f, 1.5f), new Vector3(0, yaw, 0));
            Art.Part(sec, TmKit.Blob(5), Snow, p + rot * new Vector3(0, 1.6f, 0), new Vector3(1.4f, 0.4f, 1.4f), new Vector3(0, yaw, 0));
        }

        /// <summary>A little A-frame stone hut with a dark doorway and a wooden lintel, snow on the roof.</summary>
        static void StoneHut(Transform sec, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Box(sec, StoneC, p + rot * new Vector3(s * 0.95f, 1.15f, 0), new Vector3(0.5f, 2.9f, 3.4f), new Vector3(0, yaw, s * 35f), true);
                Art.Box(sec, Snow, p + rot * new Vector3(s * 1.15f, 1.4f, 0), new Vector3(0.25f, 2.6f, 3.5f), new Vector3(0, yaw, s * 35f));
            }
            Art.Box(sec, StoneC, p + rot * new Vector3(0, 1f, 1.5f), new Vector3(1.2f, 2f, 0.4f), new Vector3(0, yaw, 0), true); // back
            Art.Box(sec, new Color(0.08f, 0.08f, 0.1f), p + rot * new Vector3(0, 0.7f, -1.5f), new Vector3(0.9f, 1.4f, 0.1f), new Vector3(0, yaw, 0));
            Art.Box(sec, Wood, p + rot * new Vector3(0, 1.5f, -1.6f), new Vector3(1.8f, 0.25f, 0.25f), new Vector3(0, yaw, 0));
        }

        static void Crystals(Transform sec, Vector3 p, System.Random rng)
        {
            for (int k = 0; k < 5; k++)
            {
                float hgt = 1f + (float)rng.NextDouble() * 1.8f;
                var at = p + new Vector3((float)rng.NextDouble() * 2f - 1f, 0, (float)rng.NextDouble() * 2f - 1f);
                Art.Part(sec, Art.Cone, k % 2 == 0 ? IceBlue : Color.Lerp(IceBlue, new Color(0.5f, 0.8f, 1f), 0.5f), at, new Vector3(0.7f, hgt, 0.7f), new Vector3((float)rng.NextDouble() * 30f - 15f, k * 70f, (float)rng.NextDouble() * 30f - 15f), k == 0);
            }
        }
    }
}
