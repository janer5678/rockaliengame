using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// ISLANDS: every team's base sits on its own sand island, and the ball drops on a sand island in the middle.
    /// Shallow sea in between (wading is slow; boats can be crafted). A pier and stepping stones lead from each base
    /// island towards the middle. Mini-Monke style props in sand colours: stacked sandstone blocks with orange ladders
    /// (real, climbable), wooden platforms on posts, crates, sea-glass blocks, low-poly blob trees.
    /// (Three teams: only three islands - the empty fourth spot is open sea, with nothing on it.)
    /// </summary>
    public class IslandsMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Islands;
        public override string Label => "Islands";
        public override string Blurb => "A sand island for every team and one in the middle with the ball. Wade the shallow sea (slow), hop the stepping stones, or craft a BOAT (1500 wood).";
        public override bool HasWater => true;
        public override bool Mountains => false;

        static float D => Mathf.Abs(Cfg.BaseCenter[0].z);
        static float CenterR => Mathf.Max(24f, D * 0.44f);
        const float SeaFloor = -1.6f;

        static readonly Color Sand = new Color(0.93f, 0.85f, 0.62f), WetSand = new Color(0.8f, 0.7f, 0.5f), SeaBed = new Color(0.62f, 0.66f, 0.55f);
        static readonly Color Sandstone = new Color(0.86f, 0.74f, 0.52f), Sandstone2 = new Color(0.78f, 0.66f, 0.45f);
        static readonly Color Plank = new Color(0.66f, 0.5f, 0.32f), PlankDark = new Color(0.5f, 0.37f, 0.22f);
        static readonly Color LadderOrange = new Color(0.95f, 0.45f, 0.12f), SeaGlass = new Color(0.55f, 0.85f, 0.9f);

        /// <summary>Which quarter-turn takes blue's island to island k (k = the Cfg.Copy index).</summary>
        static int Quarter(int k) => k * (4 / Cfg.Copies);

        /// <summary>Is there a team in the k-th copy's spot? (Three teams: the fourth spot is open sea - no island, no props, no trees.)</summary>
        public static bool IslandUsed(int k)
        {
            var at = Cfg.Copy(Cfg.BaseCenter[0], k);
            for (int t = 0; t < Cfg.TeamCount; t++) if ((Cfg.BaseCenter[t] - at).sqrMagnitude < 4f) return true;
            return false;
        }

        /// <summary>The island-local coords (u across, v away from the middle) of a point, for island k.</summary>
        static Vector2 Local(float x, float z, int k)
        {
            var p = Cfg.Rotate(new Vector3(x, 0, z), 4 - Quarter(k) & 3); // back into blue's frame
            return new Vector2(p.x, -(p.z + D)); // v > 0: out past the base, away from the middle
        }

        public override float Height(float x, float z)
        {
            float s = ThemeMaps.SeedP;
            // the middle island
            float r = Mathf.Sqrt(x * x + z * z);
            float wob = (ThemeMaps.SymNoiseP(x, z, 0.05f, s + 11f) - 0.5f) * 10f;
            float land = 1f - ThemeMaps.SmoothStepP(CenterR - 4f, CenterR + 3f, r + wob);
            // one island round every base - only as many as there are teams (three teams: the fourth spot is sea)
            for (int k = 0; k < Cfg.Copies && land < 1f; k++)
            {
                if (!IslandUsed(k)) continue;
                var l = Local(x, z, k);
                float dx = Mathf.Max(0f, Mathf.Abs(l.x) - Cfg.BaseHalf), dz = Mathf.Max(0f, Mathf.Abs(l.y) - Cfg.BaseHalf);
                float dB = Mathf.Sqrt(dx * dx + dz * dz);
                float reach = 14f + (Mathf.PerlinNoise(l.x * 0.05f + s + 40f, l.y * 0.05f + s * 0.5f + 70f) - 0.5f) * 10f;
                land = Mathf.Max(land, 1f - ThemeMaps.SmoothStepP(reach - 5f, reach + 3f, dB));
            }
            float dune = 0.25f + (ThemeMaps.SymNoiseP(x, z, 0.08f, s + 3f) - 0.5f) * 0.7f;
            return Mathf.Lerp(SeaFloor, dune, land) * ThemeMaps.MaskP(x, z);
        }

        public override Color[] Palette => new[] { Sand, WetSand, SeaBed };
        public override int ColourAt(Vector3 c, float slopeY) => c.y > -0.2f ? 0 : c.y > -0.9f ? 1 : 2;

        public override bool SpotOk(Vector3 p) => p.y > 0.02f;
        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 0.9f : kind == ResourceNode.Bush ? 0.8f : 0.6f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.35f, 0.75f, 0.3f), 0.5f);

        /// <summary>Mini-Monke trees: a chunky trunk forking into a few branches, each topped with a faceted green blob.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 77);
            float R() => (float)rng.NextDouble();
            var bark = TmKit.Shade(Color.Lerp(new Color(0.5f, 0.36f, 0.24f), new Color(0.6f, 0.45f, 0.3f), R()), 1f);
            var leaf = TmKit.Shade(Color.Lerp(new Color(0.33f, 0.78f, 0.3f), new Color(0.45f, 0.85f, 0.35f), R()), 1f);
            float th = h * 0.75f;
            Art.Part(tr, Art.Cylinder, bark, new Vector3(0, th * 0.5f, 0), new Vector3(0.7f, th * 0.5f, 0.7f));
            Art.Part(tr, Art.Cylinder, bark, new Vector3(0, 0.25f, 0), new Vector3(1.1f, 0.25f, 1.1f)); // root flare
            int branches = 2 + rng.Next(2);
            float yaw0 = R() * 360f;
            for (int b = 0; b < branches; b++)
            {
                float yaw = yaw0 + b * 360f / branches + R() * 30f, tilt = 30f + R() * 20f, len = 2.2f + R() * 1.4f;
                var dir = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(tilt, 0, 0) * Vector3.up;
                var a = new Vector3(0, th * (0.75f + R() * 0.2f), 0);
                var e = a + dir * len;
                Art.Part(tr, Art.Cylinder, bark, (a + e) * 0.5f, new Vector3(0.32f, len * 0.5f, 0.32f), Quaternion.FromToRotation(Vector3.up, dir).eulerAngles);
                float bs = 2.2f + R() * 1.2f;
                Art.Part(tr, TmKit.Blob(seed + b), TmKit.Shade(leaf, (0.9f + R() * 0.15f)), e + Vector3.up * bs * 0.25f, new Vector3(bs, bs * 0.85f, bs), new Vector3(0, R() * 360f, 0));
            }
            float top = 3f + R();
            Art.Part(tr, TmKit.Blob(seed + 9), leaf, new Vector3(0, th + top * 0.45f, 0), new Vector3(top, top * 0.9f, top), new Vector3(0, R() * 360f, 0));
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

            // ---- the pier and the stepping stones from blue's island to the middle (off to one side of the base's axis) ----
            float laneX = Mathf.Min(9f, Cfg.BaseHalf - 4f);
            float zShore = float.NaN, zFar = float.NaN;
            for (float z = bc.z + Cfg.BaseHalf; z < -CenterR + 6f; z += 0.5f)
            {
                float hh = ThemeMaps.Height(laneX, z);
                if (float.IsNaN(zShore) && hh < -0.5f) zShore = z;
                if (!float.IsNaN(zShore) && hh > -0.4f) { zFar = z; break; }
            }
            if (!float.IsNaN(zShore) && !float.IsNaN(zFar) && zFar - zShore > 5f)
            {
                float zEnd = zShore + (zFar - zShore) * 0.5f;
                Pier(sec, new Vector3(laneX, 0, zShore - 3f), new Vector3(laneX, 0, zEnd));
                for (float z = zEnd + 2.6f; z < zFar - 0.5f; z += 2.7f)
                    TmKit.Stone(sec, new Vector3(laneX + Rn(-0.8f, 0.8f), 0.12f, z), Rn(0.75f, 0.95f), Sandstone2);
            }
            // a second, all-stepping-stone route on the other side (more of a jump)
            for (float z = (float.IsNaN(zShore) ? 0 : zShore) + 1f; !float.IsNaN(zShore) && !float.IsNaN(zFar) && z < zFar - 0.5f; z += 3.1f)
            {
                var q = new Vector3(-laneX - 4f + Rn(-1f, 1f), 0.12f, z);
                if (ThemeMaps.Height(q.x, q.z) < -0.3f) TmKit.Stone(sec, q, Rn(0.7f, 0.9f), Sandstone2);
            }

            // ---- the middle island: obstacles round the ball (the middle itself stays clear) ----
            float ring = Mathf.Max(23f, CenterR - 7f);
            var spots = new List<(float ang, float r, int kind)>
            {
                (-24f, ring, 0), (22f, ring + 1f, 1), (0f, ring + 3f, 2), (-33f, ring - 1f, 3), (33f, ring - 1f, 2), (8f, ring - 1.5f, 3),
            };
            if (!Cfg.FourWay) { spots.Add((-70f, ring, 1)); spots.Add((70f, ring, 0)); spots.Add((-80f, ring + 1f, 3)); spots.Add((80f, ring + 1f, 2)); spots.Add((-55f, ring + 2f, 2)); spots.Add((55f, ring + 2f, 3)); }
            foreach (var (ang, r, kind) in spots)
            {
                var p = Quaternion.Euler(0, ang, 0) * new Vector3(0, 0, -r);
                if (!TmKit.FreeSpot(p, 3f) || ThemeMaps.Height(p.x, p.z) < -0.1f) continue;
                p.y = ThemeMaps.Height(p.x, p.z);
                float yaw = ang + Rn(-15f, 15f);
                switch (kind)
                {
                    case 0: BlockStack(sec, p, yaw, rng); break;
                    case 1: Platform(sec, p, yaw, rng, 2.4f); break;
                    case 2: Crates(sec, p, yaw, rng); break;
                    default: GlassBlock(sec, p, yaw, rng); break;
                }
            }

            // ---- blue's island: a platform and crates out past the base edges ----
            var local = new[] { new Vector3(Cfg.BaseHalf + 7f, 0, 0), new Vector3(-Cfg.BaseHalf - 7f, 0, -6f), new Vector3(Cfg.BaseHalf + 6f, 0, -10f), new Vector3(-Cfg.BaseHalf - 6f, 0, 8f) };
            for (int i = 0; i < local.Length; i++)
            {
                var p = bc + local[i];
                if (!Cfg.InFirstSector(p, 3f) || Mathf.Abs(p.x) > Cfg.MapHalf - 4f || ThemeMaps.Height(p.x, p.z) < 0.02f) continue;
                p.y = ThemeMaps.Height(p.x, p.z);
                if (i == 0) Platform(sec, p, Rn(0, 360), rng, 2.2f);
                else if (i == 1) BlockStack(sec, p, Rn(0, 360), rng, small: true);
                else Crates(sec, p, Rn(0, 360), rng);
            }
            // driftwood and shells along the beaches
            for (int i = 0, made = 0; i < 400 && made < Mathf.RoundToInt(8 * Cfg.MapHalf / 100f); i++)
            {
                var p = new Vector3(Rn(-Cfg.MapHalf + 6f, Cfg.MapHalf - 6f), 0, Rn(-Cfg.MapHalf + 6f, -4f));
                if (!TmKit.FreeSpot(p, 2f)) continue;
                float hh = ThemeMaps.Height(p.x, p.z);
                if (hh < -0.4f || hh > 0.1f) continue;
                p.y = hh;
                made++;
                if (made % 2 == 0) Art.Part(sec, Art.Cylinder, new Color(0.7f, 0.6f, 0.45f), p + Vector3.up * 0.2f, new Vector3(0.35f, 1.4f, 0.35f), new Vector3(90, Rn(0, 360), 0), true);
                else Art.Part(sec, Art.Sphere, new Color(0.98f, 0.82f, 0.75f), p + Vector3.up * 0.05f, new Vector3(0.35f, 0.15f, 0.3f), new Vector3(0, Rn(0, 360), 0));
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

        static void Pier(Transform sec, Vector3 a, Vector3 b)
        {
            const float top = 0.45f;
            a.y = b.y = top;
            TmKit.Plank(sec, a, b, 2.4f, 0.25f, Plank, true);
            var dir = (b - a).normalized;
            var side = new Vector3(dir.z, 0, -dir.x);
            float len = (b - a).magnitude;
            for (float t = 0; t <= len; t += 3f)
                for (int s = -1; s <= 1; s += 2)
                {
                    var p = a + dir * t + side * (s * 1.1f);
                    float floor = ThemeMaps.Height(p.x, p.z);
                    float hgt = top - floor + 0.6f;
                    Art.Part(sec, Art.Cylinder, PlankDark, new Vector3(p.x, floor + hgt * 0.5f - 0.2f, p.z), new Vector3(0.28f, hgt * 0.5f, 0.28f));
                }
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

        /// <summary>In blue's sector, away from its base, the middle and the edge.</summary>
        public static bool FreeSpot(Vector3 p, float clear)
        {
            if (!Cfg.InFirstSector(p, clear)) return false;
            var bc = Cfg.BaseCenter[0];
            if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + clear && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + clear) return false;
            if (new Vector2(p.x, p.z).magnitude < 20f + clear) return false;
            return Mathf.Abs(p.x) < Cfg.MapHalf - clear && Mathf.Abs(p.z) < Cfg.MapHalf - clear;
        }

        static readonly Mesh[] s_Blobs = new Mesh[8];
        /// <summary>A faceted low-poly blob (shared meshes).</summary>
        public static Mesh Blob(int i)
        {
            i = ((i % 8) + 8) % 8;
            if (s_Blobs[i] == null) s_Blobs[i] = Art.MakeRock(900 + i, 0.22f);
            return s_Blobs[i];
        }

        /// <summary>A box whose top face runs from a to b (walkways, ramps, bridges).</summary>
        public static GameObject Plank(Transform parent, Vector3 a, Vector3 b, float width, float thick, Color c, bool collider)
        {
            var d = b - a;
            var rot = Quaternion.LookRotation(d.normalized, Vector3.up);
            var mid = (a + b) * 0.5f - rot * Vector3.up * (thick * 0.5f);
            return Art.Box(parent, c, mid, new Vector3(width, thick, d.magnitude), rot.eulerAngles, collider);
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
        /// topY: the floor you step off onto at the top. Parent must be at the world origin, only turned about y (a Sector).</summary>
        public static void Ladder(Transform parent, Vector3 foot, float topY, Vector3 faceOut, Color c)
        {
            faceOut.y = 0;
            faceOut.Normalize();
            float len = topY - foot.y + 0.9f;
            var rot = Quaternion.LookRotation(-faceOut);
            var right = rot * Vector3.right;
            var e = rot.eulerAngles;
            var b = foot + faceOut * 0.12f;
            Art.Box(parent, c, b + right * 0.36f + Vector3.up * (len * 0.5f), new Vector3(0.09f, len, 0.09f), e);
            Art.Box(parent, c, b - right * 0.36f + Vector3.up * (len * 0.5f), new Vector3(0.09f, len, 0.09f), e);
            for (float y = 0.35f; y < len - 0.1f; y += 0.45f)
                Art.Box(parent, c, b + Vector3.up * y, new Vector3(0.72f, 0.07f, 0.07f), e);
            var go = new GameObject("ladder");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = foot + faceOut * 0.5f + Vector3.up * (len * 0.5f);
            go.transform.localRotation = rot;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1.1f, len, 0.9f);
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
