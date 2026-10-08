using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// THEME MAPS: five extra maps (Beach, Canyon, Frostlake, Volcano, Ruins). Everything about them lives in this folder;
    /// the rest of the game only calls in here from lines marked "// THEME MAPS". To remove them all: delete the
    /// Assets/Scripts/ThemeMaps folder, then delete every line / block marked "// THEME MAPS" (see the README).
    ///
    /// Beach:     a palm-tree island in the middle ringed by shallow water, channels down the sides - buy a boat (1500 wood) and sail round.
    /// Canyon:    red desert with tall mesas that split the map into lanes; lots of stone.
    /// Frostlake: snowy hills round a frozen lake in the middle - the ice is slippery.
    /// Volcano:   black ash, a ring of lava round the middle (it burns) with land bridges; a volcano smokes on the horizon.
    /// Ruins:     overgrown ancient ruins - broken walls and pillars for cover, extra stone.
    /// </summary>
    public static partial class ThemeMaps
    {
        public const float WaterY = -0.45f, LavaY = -0.35f;
        public const int BoatWood = 250;
        public const float BoatSpeed = 9f, LavaDps = 22f; // (the boat: 250 wood, and slower than it was - 15)

        public static readonly MapKind[] Kinds = { MapKind.Beach, MapKind.Canyon, MapKind.Frostlake, MapKind.Volcano, MapKind.Ruins,
            MapKind.Islands, MapKind.Jungle, MapKind.Ice, MapKind.CherryBlossom, MapKind.Wonderland, MapKind.Swamp, MapKind.Cube, MapKind.Mars };

        public static bool IsTheme => Cfg.Map >= MapKind.Beach && Cfg.Map != MapKind.HighlandsJonah; // (the Jonah Highlands is the Highlands, not a theme map)
        public static bool HasWater => Cfg.Map == MapKind.Beach || (Custom != null && Custom.HasWater);

        public static string Label(MapKind k)
        {
            switch (k)
            {
                case MapKind.Beach: return "Beach";
                case MapKind.Canyon: return "Canyon";
                case MapKind.Frostlake: return "Frostlake";
                case MapKind.Volcano: return "Volcano";
                case MapKind.Ruins: return "Ruins";
                case MapKind.HighlandsJonah: return "Highlands Jonah";
                default: return CustomFor(k)?.Label ?? k.ToString();
            }
        }

        public static string Blurb(MapKind k)
        {
            switch (k)
            {
                case MapKind.Beach: return "Palm island in the middle, shallow water round it (slow to wade) and channels down the sides. Craft a BOAT (1500 wood) to sail round.";
                case MapKind.Canyon: return "Red desert. Tall mesas split the map into lanes - lots of stone, fewer trees.";
                case MapKind.Frostlake: return "Snowy hills round a frozen lake in the middle. The ice is slippery!";
                case MapKind.Volcano: return "Black ash and a ring of LAVA round the middle - it burns. Cross on the land bridges.";
                case MapKind.Ruins: return "Overgrown ancient ruins: broken walls and pillars for cover, extra stone.";
                default: return CustomFor(k)?.Blurb ?? "";
            }
        }

        // =====================================================================
        // Terrain height
        // =====================================================================

        static float Half => Cfg.MapHalf;
        static float SmoothStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        static float Noise(float x, float z, float f, float o) => Mathf.PerlinNoise(x * f + o + 300f, z * f + o * 0.7f + 200f);

        /// <summary>Noise the same for every team (point mirror, or four ways round), like the Highlands.</summary>
        static float SymNoise(float x, float z, float f, float o) => Cfg.FourWay
            ? 0.25f * (Noise(x, z, f, o) + Noise(-z, x, f, o) + Noise(-x, -z, f, o) + Noise(z, -x, f, o))
            : 0.5f * (Noise(x, z, f, o) + Noise(-x, -z, f, o));

        /// <summary>1 out in the wild, 0 on the bases and the ball drop zone (they stay flat at y = 0).</summary>
        static float Mask(float x, float z)
        {
            float dBase = float.MaxValue;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var c = Cfg.BaseCenter[t];
                float dx = Mathf.Max(0, Mathf.Abs(x - c.x) - Cfg.BaseHalf), dz = Mathf.Max(0, Mathf.Abs(z - c.z) - Cfg.BaseHalf);
                dBase = Mathf.Min(dBase, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return Mathf.Min(SmoothStep(2f, 14f, dBase), SmoothStep(9f, 20f, new Vector2(x, z).magnitude));
        }

        static float Seed => (Cfg.MapSeed % 997) * 0.37f;

        static float MoatIn => Mathf.Max(26f, Half * 0.22f);
        static float MoatOut => MoatIn + Mathf.Max(10f, Half * 0.12f);
        static float LavaRingR => Mathf.Max(28f, Half * 0.42f);

        public static float Height(float x, float z)
        {
            float r = new Vector2(x, z).magnitude;
            float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float edge = e - (Half - 14f);
            float h;
            switch (Cfg.Map)
            {
                case MapKind.Beach:
                {
                    h = (SymNoise(x, z, 0.05f, Seed) - 0.45f) * 1.2f;         // little dunes
                    float moat = SmoothStep(MoatIn - 3f, MoatIn + 2f, r) * (1f - SmoothStep(MoatOut - 2f, MoatOut + 3f, r));
                    float side = SmoothStep(Half - 20f, Half - 13f, e);
                    // canals from the moat out to the side channels (two teams: along the x axis, away from both bases)
                    float canal = !Cfg.FourWay && r > MoatIn ? 1f - SmoothStep(3f, 6f, Mathf.Abs(z)) : 0f;
                    float water = Mathf.Max(moat, Mathf.Max(side, canal));
                    return Mathf.Lerp(h, -1.0f, water) * Mask(x, z);
                }
                case MapKind.Canyon:
                {
                    float n = SymNoise(x, z, 0.03f, Seed);
                    h = SmoothStep(0.56f, 0.6f, n) * (9f + (n - 0.56f) * 30f);  // steep-sided mesas
                    h += (SymNoise(x, z, 0.12f, Seed + 50f) - 0.5f) * 0.6f;
                    break;
                }
                case MapKind.Frostlake:
                {
                    h = Mathf.Max(0f, (SymNoise(x, z, 0.035f, Seed) - 0.42f) * 14f);
                    h *= SmoothStep(Half * 0.36f, Half * 0.46f, r);              // flat frozen lake in the middle
                    break;
                }
                case MapKind.Volcano:
                {
                    h = (SymNoise(x, z, 0.04f, Seed) - 0.45f) * 6f;
                    if (h < 0f) h *= 0.3f;
                    h = Mathf.Max(h, -0.2f);
                    // the lava ring, broken by land bridges every 45 degrees
                    float ang = Mathf.Atan2(z, x) * Mathf.Rad2Deg;
                    float toBridge = Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 45f) * 45f));
                    float ring = (1f - SmoothStep(2f, 4.5f, Mathf.Abs(r - LavaRingR))) * SmoothStep(5f, 9f, toBridge);
                    // and a few lava pools
                    float pool = SmoothStep(0.66f, 0.7f, SymNoise(x, z, 0.06f, Seed + 90f));
                    float lava = Mathf.Max(ring, pool);
                    h = Mathf.Lerp(h, -0.9f, lava);
                    break;
                }
                case MapKind.Ruins:
                    h = (SymNoise(x, z, 0.045f, Seed) - 0.4f) * 3f;
                    h = Mathf.Max(h, 0f);
                    break;
                default:
                    return Custom != null ? Custom.Height(x, z) : 0f;
            }
            if (edge > 0) h += edge * 0.9f;
            return h * Mask(x, z);
        }

        // =====================================================================
        // Zones
        // =====================================================================

        /// <summary>Beach: open water here (deep enough for a boat).</summary>
        public static bool WaterAt(float x, float z) => HasWater && Height(x, z) < WaterY - 0.25f;

        public static bool InWater(Vector3 p) => HasWater && Height(p.x, p.z) < WaterY - 0.05f && p.y < WaterY + 0.2f;

        static bool IceAt(float x, float z)
        {
            if (Custom != null) return Custom.Slippery(new Vector3(x, Height(x, z), z));
            if (Cfg.Map != MapKind.Frostlake) return false;
            float r = new Vector2(x, z).magnitude;
            return r > 13f && r < Half * 0.38f && Mask(x, z) > 0.5f;
        }

        public static bool OnIce(Vector3 p) => Custom != null ? Custom.Slippery(p) : IceAt(p.x, p.z) && p.y < 0.6f;

        public static bool LavaAt(float x, float z) => Cfg.Map == MapKind.Volcano && Height(x, z) < LavaY - 0.1f;

        public static bool InLava(Vector3 p) => LavaAt(p.x, p.z) && p.y < LavaY + 0.4f;

        /// <summary>How fast you can walk here (wading, lava).</summary>
        public static float SpeedMul(Vector3 p) => !IsTheme ? 1f : InWater(p) ? (Custom != null ? Custom.WaterSpeed : 0.6f) : InLava(p) ? 0.7f : Custom != null ? Custom.SpeedMul(p) : 1f;

        /// <summary>Somewhere a tree / rock / airdrop can go (not in water or lava, not up on a mesa).</summary>
        public static bool SpotOk(Vector3 p)
        {
            if (!IsTheme) return true;
            float h = Height(p.x, p.z);
            var cm = Custom;
            if (cm != null) return h >= -0.15f && h <= cm.MaxSpotHeight && cm.SpotOk(new Vector3(p.x, h, p.z));
            if (h < -0.15f || h > 3f) return false;
            if (IceAt(p.x, p.z)) return false;
            return true;
        }

        /// <summary>More or fewer trees / rocks / bushes than usual.</summary>
        public static float NodeMul(byte kind)
        {
            bool tree = kind == ResourceNode.Tree, bush = kind == ResourceNode.Bush;
            switch (Cfg.Map)
            {
                case MapKind.Beach: return tree ? 1.1f : bush ? 1f : 0.6f;
                case MapKind.Canyon: return tree ? 0.5f : bush ? 0.6f : 1.6f;
                case MapKind.Frostlake: return tree ? 1.2f : bush ? 0.5f : 0.9f;
                case MapKind.Volcano: return tree ? 0.6f : bush ? 0.4f : 1.5f;
                case MapKind.Ruins: return tree ? 0.8f : bush ? 0.8f : 1.4f;
                default: return Custom != null ? Custom.NodeMul(kind) : 1f;
            }
        }

        /// <summary>Server: lava burns anyone standing in it.</summary>
        static float s_NextBurn;
        public static void ServerTick()
        {
            ServerTickCustom();
            if (Cfg.Map != MapKind.Volcano || Time.time < s_NextBurn) return;
            s_NextBurn = Time.time + 0.5f;
            foreach (var p in PlayerNet.All)
            {
                if (p.Dead.Value || p.Riding || !InLava(p.transform.position)) continue;
                p.ServerDamage(LavaDps * 0.5f, null, KillCause.Lava);
            }
        }

        // =====================================================================
        // Trees
        // =====================================================================

        /// <summary>Leaf colours to suit the map.</summary>
        public static Color LeafTint(Color leaf)
        {
            switch (Cfg.Map)
            {
                case MapKind.Canyon: return Color.Lerp(leaf, new Color(0.55f, 0.55f, 0.28f), 0.6f);
                case MapKind.Frostlake: return Color.Lerp(leaf, new Color(0.85f, 0.92f, 0.95f), 0.45f);
                case MapKind.Volcano: return Color.Lerp(leaf, new Color(0.28f, 0.24f, 0.18f), 0.65f);
                case MapKind.Ruins: return Color.Lerp(leaf, new Color(0.3f, 0.45f, 0.18f), 0.3f);
                default: return Custom != null ? Custom.LeafTint(leaf) : leaf;
            }
        }

        /// <summary>Beach: palm trees instead of the cone trees (the trunk collider and weak spots stay the same).</summary>
        public static bool BuildPalm(Transform tr, int seed, float h, GameObject trunk)
        {
            if (Custom != null) return Custom.BuildTree(tr, seed, h, trunk);
            if (Cfg.Map != MapKind.Beach) return false;
            var rng = new System.Random(seed + 5);
            float r() => (float)rng.NextDouble();
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var bark = new Color(0.62f, 0.48f, 0.3f);
            var ring = new Color(0.5f, 0.37f, 0.22f);
            float lean = 6f + r() * 10f, yaw = r() * 360f;
            var root = new GameObject("palm").transform;
            root.SetParent(tr, false);
            root.localRotation = Quaternion.Euler(0, yaw, 0);
            // a bendy trunk: straight at the bottom (where the weak spots are), leaning more further up
            int segs = 6;
            float segH = (h + 1.5f) / segs;
            var pos = Vector3.zero;
            var rot = Quaternion.identity;
            for (int i = 0; i < segs; i++)
            {
                if (i >= 2) rot = Quaternion.Euler(lean * (i - 1) / segs * 2f, 0, 0);
                var up = rot * Vector3.up;
                float w = Mathf.Lerp(0.62f, 0.42f, i / (float)segs);
                Art.Part(root, Art.Cylinder, i % 2 == 0 ? bark : ring, pos + up * segH * 0.5f, new Vector3(w, segH * 0.5f, w), rot.eulerAngles);
                pos += up * segH;
            }
            // fronds drooping out from the top, and coconuts
            var leaf = Color.Lerp(new Color(0.25f, 0.6f, 0.2f), new Color(0.4f, 0.7f, 0.25f), r());
            for (int k = 0; k < 7; k++)
            {
                var f = new GameObject("frond").transform;
                f.SetParent(root, false);
                f.localPosition = pos;
                f.localRotation = Quaternion.Euler(0, k * (360f / 7f) + r() * 20f, 0);
                Art.Box(f, leaf, new Vector3(0, -0.15f, 1.3f), new Vector3(0.9f, 0.06f, 2.8f), new Vector3(22f + r() * 12f, 0, 0));
                Art.Box(f, Color.Lerp(leaf, Color.black, 0.2f), new Vector3(0, -0.9f, 2.9f), new Vector3(0.7f, 0.05f, 1.4f), new Vector3(48f, 0, 0));
            }
            for (int k = 0; k < 3; k++)
                Art.Part(root, Art.Sphere, new Color(0.35f, 0.25f, 0.12f), pos + Quaternion.Euler(0, k * 120f, 0) * new Vector3(0.3f, -0.35f, 0), Vector3.one * 0.32f);
            return true;
        }

        // =====================================================================
        // Building the map (every peer, from the seed)
        // =====================================================================

        static Color[] Palette()
        {
            if (Custom != null) return Custom.Palette;
            switch (Cfg.Map)
            {
                case MapKind.Beach: return new[] { new Color(0.88f, 0.8f, 0.56f), new Color(0.72f, 0.64f, 0.44f), new Color(0.55f, 0.52f, 0.47f) };
                case MapKind.Canyon: return new[] { new Color(0.84f, 0.6f, 0.38f), new Color(0.68f, 0.34f, 0.2f), new Color(0.78f, 0.5f, 0.3f) };
                case MapKind.Frostlake: return new[] { new Color(0.92f, 0.94f, 0.97f), new Color(0.58f, 0.62f, 0.68f), new Color(0.66f, 0.84f, 0.95f) };
                case MapKind.Volcano: return new[] { new Color(0.24f, 0.22f, 0.21f), new Color(0.36f, 0.3f, 0.28f), new Color(0.4f, 0.16f, 0.08f) };
                default: return new[] { new Color(0.4f, 0.55f, 0.28f), new Color(0.48f, 0.4f, 0.3f), new Color(0.33f, 0.47f, 0.25f) };
            }
        }

        /// <summary>Which palette colour a ground triangle gets.</summary>
        static int ColourAt(Vector3 c, float slopeY)
        {
            if (Custom != null) return Custom.ColourAt(c, slopeY);
            switch (Cfg.Map)
            {
                case MapKind.Beach: return slopeY < 0.75f ? 2 : c.y < -0.25f ? 1 : 0;
                case MapKind.Canyon: return slopeY < 0.8f ? 1 : c.y > 6f ? 2 : 0;
                case MapKind.Frostlake: return IceAt(c.x, c.z) ? 2 : slopeY < 0.78f ? 1 : 0;
                case MapKind.Volcano: return c.y < LavaY ? 2 : slopeY < 0.8f ? 1 : 0;
                default: return slopeY < 0.8f ? 1 : SymNoise(c.x, c.z, 0.08f, Seed + 7f) > 0.6f ? 2 : 0;
            }
        }

        /// <summary>The ground (a smooth-shaded mesh like the Highlands), coloured for the map.</summary>
        public static void BuildGround(Transform root)
        {
            if (Custom != null) { Custom.BuildGround(root); if (Custom.HasWater) Surface(root, "Water", new Color(0.2f, 0.52f, 0.78f), WaterY, Cfg.MapHalf * 2f + 60f, false); return; }
            float half = Cfg.MapHalf + 30f;
            const float step = 2f;
            int n = Mathf.CeilToInt(half * 2f / step);
            var hs = new float[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                hs[i, j] = Height(-half + i * step, -half + j * step);
            var pal = Palette();
            // smooth-shaded like the Highlands (each triangle coloured for where it is and how steep)
            var mesh = MapBuilder.SmoothGround("ThemeTerrain", hs, half, step, pal.Length, (c, ny) => ColourAt(c, ny));
            var mats = new Material[pal.Length];
            for (int s = 0; s < pal.Length; s++) mats[s] = Art.Mat(pal[s]);
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<GroundMarker>();

            // water / lava surfaces (no collider: you wade through water, and sink into lava)
            float size = Cfg.MapHalf * 2f + 60f;
            if (Cfg.Map == MapKind.Beach) Surface(root, "Water", new Color(0.2f, 0.52f, 0.78f), WaterY, size, false);
            if (Cfg.Map == MapKind.Volcano) Surface(root, "Lava", new Color(1f, 0.42f, 0.08f), LavaY, size, true);
        }

        public static void Surface(Transform root, string name, Color c, float y, float size, bool glow)
        {
            var mat = new Material(Art.Mat(c)) { name = name };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", glow ? 0.2f : 0.85f);
            if (glow && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c * 1.6f);
            }
            var go = Art.Box(root, c, new Vector3(0, y - 0.05f, 0), new Vector3(size, 0.1f, size), default, false, mat);
            go.name = name;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Scenery for the map, the same in every team's part of it.</summary>
        public static void BuildProps(Transform root)
        {
            var rng = new System.Random(4242 + Cfg.MapSeed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = Cfg.MapHalf;
            int want = Mathf.RoundToInt(14 * half / 100f);
            if (Custom != null) { Custom.BuildProps(root); return; }

            // spots in the first team's part of the map, away from the bases and the middle, copied round for every team
            IEnumerable<(Vector3 p, float yaw)> Spots(int count, float clearance)
            {
                int made = 0;
                for (int tries = 0; tries < count * 30 && made < count; tries++)
                {
                    var p = new Vector3(R(-half + 8, half - 8), 0, R(-half + 8, -6f));
                    if (!Cfg.InFirstSector(p, 5f)) continue;
                    var bc = Cfg.BaseCenter[0];
                    if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + clearance && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + clearance) continue;
                    if (new Vector2(p.x, p.z).magnitude < 22f) continue;
                    if (!SpotOk(p)) continue;
                    made++;
                    float yaw = R(0, 360);
                    for (int m = 0; m < Cfg.Copies; m++)
                    {
                        var q = Cfg.Copy(p, m);
                        q.y = Height(q.x, q.z);
                        yield return (q, yaw + m * 360f / Cfg.Copies);
                    }
                }
            }

            switch (Cfg.Map)
            {
                case MapKind.Beach:
                    foreach (var (p, yaw) in Spots(want / 2, 6f)) Umbrella(root, p, yaw, rng);
                    foreach (var (p, yaw) in Spots(want / 2, 4f))
                        Art.Part(root, Art.Cylinder, new Color(0.55f, 0.45f, 0.32f), p + Vector3.up * 0.2f, new Vector3(0.35f, 1.3f, 0.35f), new Vector3(90, yaw, 0), true); // driftwood
                    break;
                case MapKind.Canyon:
                    foreach (var (p, yaw) in Spots(want, 4f)) Cactus(root, p, yaw, rng);
                    break;
                case MapKind.Frostlake:
                    foreach (var (p, yaw) in Spots(want / 2, 6f)) Snowman(root, p, yaw);
                    foreach (var (p, yaw) in Spots(want, 3f))
                        Art.Part(root, Art.Cone, new Color(0.75f, 0.9f, 1f), p + Vector3.up * 0.9f, new Vector3(0.7f, 1.8f, 0.7f), new Vector3(R(-12, 12), yaw, R(-12, 12)), true); // ice spikes
                    break;
                case MapKind.Volcano:
                    foreach (var (p, yaw) in Spots(want, 4f))
                    {
                        float hgt = 1.5f + (float)rng.NextDouble() * 3f;
                        Art.Part(root, Art.Cylinder, new Color(0.17f, 0.16f, 0.16f), p + Vector3.up * hgt * 0.5f, new Vector3(1.2f, hgt * 0.5f, 1.2f), new Vector3(0, yaw, 0), true); // basalt pillars
                    }
                    Volcano(root, half);
                    break;
                case MapKind.Ruins:
                    foreach (var (p, yaw) in Spots(want, 6f)) RuinWall(root, p, yaw, rng);
                    foreach (var (p, yaw) in Spots(want, 3f)) Pillar(root, p, yaw, rng);
                    break;
            }
        }

        static void Umbrella(Transform root, Vector3 p, float yaw, System.Random rng)
        {
            var cols = new[] { new Color(0.9f, 0.3f, 0.3f), new Color(0.3f, 0.55f, 0.9f), new Color(0.95f, 0.8f, 0.25f) };
            var c = cols[rng.Next(cols.Length)];
            Art.Part(root, Art.Cylinder, Color.white, p + Vector3.up * 1.2f, new Vector3(0.08f, 1.2f, 0.08f), new Vector3(8, yaw, 0), true);
            Art.Part(root, Art.Cone, c, p + Vector3.up * 2.4f, new Vector3(3f, 0.6f, 3f), new Vector3(8, yaw, 0));
            Art.Box(root, c, p + Quaternion.Euler(0, yaw, 0) * new Vector3(1.2f, 0.03f, 0.3f), new Vector3(0.9f, 0.03f, 1.9f), new Vector3(0, yaw, 0)); // towel
        }

        static void Cactus(Transform root, Vector3 p, float yaw, System.Random rng)
        {
            var g = new Color(0.3f, 0.52f, 0.28f);
            float h = 2f + (float)rng.NextDouble() * 1.5f;
            Art.Part(root, Art.Capsule, g, p + Vector3.up * h * 0.5f, new Vector3(0.55f, h * 0.5f, 0.55f), new Vector3(0, yaw, 0), true);
            var side = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            Art.Part(root, Art.Capsule, g, p + Vector3.up * h * 0.55f + side * 0.45f, new Vector3(0.32f, 0.5f, 0.32f), new Vector3(0, yaw, -60));
            Art.Part(root, Art.Capsule, g, p + Vector3.up * h * 0.7f - side * 0.4f, new Vector3(0.28f, 0.4f, 0.28f), new Vector3(0, yaw, 55));
        }

        static void Snowman(Transform root, Vector3 p, float yaw)
        {
            var w = new Color(0.96f, 0.97f, 1f);
            Art.Part(root, Art.Sphere, w, p + Vector3.up * 0.5f, Vector3.one * 1.1f, default, true);
            Art.Part(root, Art.Sphere, w, p + Vector3.up * 1.35f, Vector3.one * 0.8f);
            Art.Part(root, Art.Sphere, w, p + Vector3.up * 1.95f, Vector3.one * 0.55f);
            var f = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            Art.Part(root, Art.Cone, new Color(1f, 0.5f, 0.1f), p + Vector3.up * 1.95f + f * 0.35f, new Vector3(0.1f, 0.25f, 0.1f), Quaternion.LookRotation(Vector3.up, -f).eulerAngles + new Vector3(90, 0, 0));
            Art.Part(root, Art.Cylinder, new Color(0.1f, 0.1f, 0.1f), p + Vector3.up * 2.3f, new Vector3(0.38f, 0.18f, 0.38f), new Vector3(0, yaw, 0));
        }

        static void Volcano(Transform root, float half)
        {
            // far off on the horizon, smoking and glowing at the top
            var at = new Vector3(half * 1.55f, 0, half * 0.9f);
            float s = Mathf.Max(60f, half * 0.8f);
            var cone = Art.Part(root, Art.Cone, new Color(0.2f, 0.18f, 0.17f), at + Vector3.up * s * 0.35f, new Vector3(s * 1.4f, s * 0.7f, s * 1.4f));
            cone.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var mat = new Material(Art.Mat(new Color(1f, 0.4f, 0.05f)));
            if (mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(2f, 0.7f, 0.1f)); }
            Art.Part(root, Art.Sphere, Color.white, at + Vector3.up * s * 0.68f, new Vector3(s * 0.25f, s * 0.06f, s * 0.25f), default, false, mat);
            for (int k = 0; k < 6; k++)
            {
                var puff = Art.Part(root, Art.Sphere, new Color(0.35f, 0.33f, 0.33f), at + new Vector3(k * 3f, s * 0.75f + k * s * 0.09f, k * 2f), Vector3.one * (s * 0.12f + k * s * 0.03f));
                puff.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static void RuinWall(Transform root, Vector3 p, float yaw, System.Random rng)
        {
            var stone = Color.Lerp(new Color(0.62f, 0.6f, 0.54f), new Color(0.5f, 0.5f, 0.46f), (float)rng.NextDouble());
            var moss = new Color(0.32f, 0.45f, 0.22f);
            var rot = Quaternion.Euler(0, yaw, 0);
            int blocks = 3 + rng.Next(3);
            for (int b = 0; b < blocks; b++)
            {
                float h = 1f + (float)rng.NextDouble() * 2.6f;   // broken, uneven top
                var at = p + rot * new Vector3((b - blocks * 0.5f) * 1.6f, h * 0.5f - 0.2f, 0);
                Art.Box(root, stone, at, new Vector3(1.6f, h, 0.7f), new Vector3(0, yaw, 0), true);
                if (rng.NextDouble() < 0.5) Art.Box(root, moss, at + Vector3.up * (h * 0.5f + 0.03f), new Vector3(1.5f, 0.08f, 0.72f), new Vector3(0, yaw, 0));
            }
            // fallen blocks
            for (int b = 0; b < 2; b++)
                Art.Box(root, stone, p + rot * new Vector3((float)rng.NextDouble() * 4f - 2f, 0.3f, 1.4f + (float)rng.NextDouble()), new Vector3(1f, 0.6f, 0.7f), new Vector3(0, yaw + rng.Next(60), 8), true);
        }

        static void Pillar(Transform root, Vector3 p, float yaw, System.Random rng)
        {
            var stone = new Color(0.7f, 0.68f, 0.6f);
            float h = 2.5f + (float)rng.NextDouble() * 3f;
            Art.Box(root, stone, p + Vector3.up * 0.2f, new Vector3(1.5f, 0.4f, 1.5f), new Vector3(0, yaw, 0), true);
            Art.Part(root, Art.Cylinder, stone, p + Vector3.up * (0.4f + h * 0.5f), new Vector3(0.9f, h * 0.5f, 0.9f), new Vector3(0, yaw, 0), true);
            if (rng.NextDouble() < 0.5) Art.Box(root, stone, p + Vector3.up * (0.5f + h), new Vector3(1.4f, 0.3f, 1.4f), new Vector3(0, yaw, 0), true);
        }

        // =====================================================================
        // Boat recipe (Beach)
        // =====================================================================

        public static Recipe BoatRecipe => new Recipe { Output = Item.Boat, Count = 1, Wood = BoatWood };
    }
}
