using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The crashed UFO in the middle of the map, round the ball (in the ball zone, under the glass dome until the walls
    /// drop, and still there after): a low-poly flying saucer nose-down in the ground off to one side of the ball -
    /// tilted, half buried, its glass cockpit cracked and broken open towards the ball, a torn gash in each side of its
    /// hull - in a scorched crater with a skid furrow behind it, on a wide patch of churned-up dirt (where the yellow
    /// circle used to be: about 12 m round, its edge ragged, lumps of it out in the grass) with burn marks, slabs of
    /// ground pushed up round the crater, rubble (stones, clods), hull plates and scraps strewn out to ~20 m, little
    /// fires burning (animated low-poly flames), smoke and sparks. The ball sits in the open in the crater in front of it, in plain sight
    /// from every base: the saucer lies off the lines from the bases to the ball (2 teams: off to the side, along x; 3: on
    /// the side with no base; 4: on a diagonal, between two bases), and it's low.
    /// Only the saucer is solid: one convex lump that reaches down into the ground (nothing can roll or crawl under it, so
    /// the ball can't get stuck there; you can walk up onto it). The dirt, rubble, debris, scorch marks, flames and smoke
    /// have no colliders, and nothing bigger than gravel lies between a base and the ball.
    /// Local visuals, built the same on every peer (MapBuilder.Build). The fire keeps burning all match.
    /// </summary>
    public class CrashSite : MonoBehaviour
    {
        public static CrashSite Current;

        /// <summary>The saucer's radius, how far its centre is from the ball and how steeply it's nose-down.</summary>
        public const float SaucerR = 4f, SaucerDist = 6f, Tilt = 32f;
        /// <summary>How deep the lowest point of the saucer's near edge is driven into the ground.</summary>
        const float Bury = 0.9f;
        /// <summary>How high the soil is heaped up against the buried near edge, in front (it thins out round the sides).</summary>
        const float HeapH = 1.65f;

        /// <summary>Which way the saucer lies from the middle of the map (flat, unit length).</summary>
        public static Vector3 Dir => Cfg.TeamCount >= 4 ? new Vector3(1f, 0, 1f).normalized : Cfg.TeamCount == 3 ? Vector3.left : Vector3.right;

        static readonly Color k_Hull = new Color(0.62f, 0.64f, 0.68f), k_HullDark = new Color(0.36f, 0.38f, 0.42f), k_Rim = new Color(0.5f, 0.52f, 0.56f);
        static readonly Color k_Dark = new Color(0.08f, 0.09f, 0.11f), k_Char = new Color(0.1f, 0.09f, 0.08f), k_Alien = new Color(0.45f, 0.95f, 0.55f);
        static readonly Color k_Dirt = new Color(0.42f, 0.3f, 0.18f), k_Scorch = new Color(0.2f, 0.16f, 0.12f), k_Burnt = new Color(0.13f, 0.11f, 0.1f);
        /// <summary>The crash's dirt over the ball zone (= the "Crash dirt" colour's default, so a picked colour lands on it exactly).</summary>
        static readonly Color k_CrashDirt = new Color(0.45f, 0.34f, 0.23f);
        static readonly Color k_Stone = new Color(0.54f, 0.53f, 0.5f), k_StoneDark = new Color(0.4f, 0.39f, 0.37f);

        /// <summary>How far out the crash's dirt reaches round the middle (its edge wobbles; the grass starts at about 12 m).</summary>
        public const float DirtR = 12.4f;

        struct Flame { public Transform T; public Vector3 Scale; public float Seed; }
        struct Puff { public Transform T; public Vector3 Vel; public float Age, Life, Size; }

        readonly List<Flame> m_Flames = new List<Flame>();
        readonly List<Vector3> m_SmokeAt = new List<Vector3>();   // (world)
        readonly List<Vector3> m_SparkAt = new List<Vector3>();   // (world)
        readonly List<Puff> m_Puffs = new List<Puff>();
        readonly List<Material> m_OwnMats = new List<Material>();
        Transform m_Saucer, m_Blink;
        Light m_Light;
        float m_NextPuff, m_NextSpark, m_LightBase = 2f;
        int m_NextPuffSlot;

        /// <summary>Where the saucer is (its centre, world).</summary>
        public Vector3 SaucerCentre => m_Saucer != null ? m_Saucer.position : transform.position;
        /// <summary>The saucer's solid lump (null if none).</summary>
        public MeshCollider Collider { get; private set; }
        public int FlameCount => m_Flames.Count;

        public static CrashSite Build(Transform root)
        {
            float gy = MapBuilder.Height(0, 0);
            var go = new GameObject("CrashSite");
            go.transform.SetParent(root, false);
            var d = Dir;
            go.transform.SetPositionAndRotation(new Vector3(0, gy, 0), Quaternion.Euler(0, -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg, 0)); // (local +x = Dir)
            var cs = go.AddComponent<CrashSite>();
            cs.Make();
            Current = cs;
            if (AiPsxArt.On) AiPsxArt.Apply(go.transform);
            return cs;
        }

        void OnDestroy()
        {
            foreach (var m in m_OwnMats) if (m) Destroy(m);
            if (Current == this) Current = null;
        }

        /// <summary>How far this spot (this object's space) is from the nearest line from a base to the ball (flat).</summary>
        float SightDist(Vector3 p)
        {
            float best = 999f;
            for (int team = 0; team < Cfg.TeamCount; team++)
            {
                var d = transform.InverseTransformDirection(new Vector3(Cfg.BaseCenter[team].x, 0, Cfg.BaseCenter[team].z).normalized);
                if (Vector3.Dot(d, p) <= -1f) continue;
                best = Mathf.Min(best, Vector3.Cross(d, new Vector3(p.x, 0, p.z)).magnitude);
            }
            return best;
        }

        /// <summary>Is this spot (in this object's space) near the line from some base to the ball? Nothing that sticks up
        /// goes there, so every base sees the ball.</summary>
        bool OnSightline(Vector3 p, float margin = 2.2f)
        {
            for (int team = 0; team < Cfg.TeamCount; team++)
            {
                var d = transform.InverseTransformDirection(new Vector3(Cfg.BaseCenter[team].x, 0, Cfg.BaseCenter[team].z).normalized);
                float along = Vector3.Dot(d, p);
                float side = Vector3.Cross(d, new Vector3(p.x, 0, p.z)).magnitude;
                if (along > -1f && side < margin) return true;
            }
            return false;
        }

        /// <summary>The ground's height at a point given in this object's space (relative to the middle of the zone).</summary>
        float G(float x, float z)
        {
            var w = transform.TransformPoint(new Vector3(x, 0, z));
            return MapBuilder.Height(w.x, w.z) - transform.position.y;
        }

        Material Emissive(Color c, float k)
        {
            var m = Art.NewMat(c); // (not an Art colour: AI PSX leaves the fire alone)
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * k);
            }
            m_OwnMats.Add(m);
            return m;
        }

        void Make()
        {
            var t = transform;
            var rng = new System.Random(1234);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // ---------------- the ground: churned-up dirt over the whole ball zone (where the yellow circle was), a scorched
            // crater round the ball, burn marks, a skid furrow behind the saucer ----------------
            // (the patches lie flat on the ground one over another: every one that overlaps another is on its own level,
            // PatchStep apart - PatchLift - with the big scorch, the crater and the furrow above them all. They used to be
            // on the same level, or half a centimetre apart, and flickered through each other.)
            m_Patches.Clear();
            using (ColorSlots.Use(ColorSlots.BallZone)) // ("Crash dirt" in Settings > Display colours)
            {
                Blob(t, new Vector2(0.8f, 0f), DirtR, 0.12f, rng, k_CrashDirt, DirtLift, "ground dirt");
                // patches of darker and lighter soil turned over in it
                for (int i = 0; i < 9; i++)
                {
                    float a = R(0f, Mathf.PI * 2f), rr = R(3.5f, DirtR - 2.5f), r = R(1.2f, 2.6f);
                    var c = new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
                    Blob(t, c, r, 0.3f, rng, i % 3 == 0 ? k_CrashDirt * 1.14f : k_CrashDirt * 0.84f, PatchLift(c, r), "ground soil");
                }
                // and lumps of it thrown out past its edge, into the grass
                for (int i = 0; i < 14; i++)
                {
                    float a = R(0f, Mathf.PI * 2f), rr = R(DirtR - 0.5f, DirtR + 4.5f), r = R(0.6f, 1.5f);
                    var c = new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
                    Blob(t, c, r, 0.3f, rng, k_CrashDirt * R(0.85f, 1.05f), PatchLift(c, r), "ground dirt");
                }
            }
            // burn marks where burning bits landed
            for (int i = 0; i < 6; i++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = R(6.5f, 11f), r = R(0.7f, 1.6f);
                var c = new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
                Blob(t, c, r, 0.3f, rng, k_Scorch, PatchLift(c, r), "ground burn");
            }
            Blob(t, new Vector2(1.4f, 0f), 5.6f, 0.22f, rng, k_Scorch, ScorchLift, "scorch");
            Blob(t, new Vector2(0.5f, 0f), 3.0f, 0.18f, rng, k_Burnt, CraterLift, "crater");
            {
                var mb = new MeshBatch();
                const int n = 10;
                for (int i = 0; i < n; i++)
                {
                    float x0 = 5f + i * 0.75f, x1 = x0 + 0.75f;
                    float w0 = Mathf.Lerp(2.1f, 1.3f, i / (float)n), w1 = Mathf.Lerp(2.1f, 1.3f, (i + 1f) / n);
                    Vector3 P(float x, float z) => new Vector3(x, G(x, z) + CraterLift, z); // (it doesn't reach the crater: the same level, over the scorch)
                    mb.Quad(P(x0, -w0), P(x1, -w1), P(x1, w1), P(x0, w0), Vector3.up);
                }
                Own(mb.Build(t, "furrow", Art.Mat(new Color(0.27f, 0.19f, 0.12f)), false));
            }
            // dirt thrown up along the furrow (low: nothing to trip the ball)
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(R(6.5f, 12f), 0, (i % 2 == 0 ? 1 : -1) * R(1.6f, 2.3f));
                p.y = G(p.x, p.z);
                float s = R(0.25f, 0.5f);
                Art.Part(t, Art.MakeRock(36 + i, 0.35f), Color.Lerp(k_Dirt, k_Scorch, R(0f, 0.5f)), p, new Vector3(s * 1.3f, s * 0.45f, s), new Vector3(0, R(0, 360), 0));
            }
            // the soil the saucer ploughed up as it drove in nose first: a big heap banked up against the buried edge
            BuildSoilHeap(t, rng);

            using (ColorSlots.Use(ColorSlots.CrashSite)) // (Settings > Display colours)
            {
                BuildSaucer(t, rng);
                BuildDebris(t, rng);
            }
            BuildRubble(t, rng);
            BuildFires(t, rng);

            // the fire's light
            var lg = new GameObject("fire light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(SaucerDist - 1f, 2.2f, 0);
            m_Light = lg.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.color = new Color(1f, 0.55f, 0.2f);
            m_Light.range = 11f;
            m_Light.intensity = m_LightBase;
            m_Light.shadows = LightShadows.None;

            // a pool of smoke puffs (re-used: nothing is made or destroyed while it burns)
            var smoke = Art.Ghost(new Color(0.42f, 0.41f, 0.4f, 0.32f));
            for (int i = 0; i < 18; i++)
            {
                var p = Art.Part(t, Art.Ico, Color.white, Vector3.zero, Vector3.one * 0.001f, default, false, smoke, "smoke");
                p.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p.SetActive(false);
                m_Puffs.Add(new Puff { T = p.transform });
            }
        }

        /// <summary>How far over the ground the flat patches lie (m): the wide dirt lowest; the small patches (soil, lumps,
        /// burn marks) on levels PatchStep apart from PatchBase up, each on the lowest level none of the ones it overlaps
        /// is on; the big scorch over all of those, the crater (and the furrow) over the scorch.</summary>
        public const float DirtLift = 0.035f, PatchBase = 0.05f, PatchStep = 0.012f, ScorchLift = PatchBase + PatchLevels * PatchStep, CraterLift = ScorchLift + 0.015f;
        const int PatchLevels = 5;
        /// <summary>(tests) the small patches put down: where, how far out they can reach, and the level each is on.</summary>
        public IReadOnlyList<(Vector2 c, float r, int level)> Patches => m_Patches;
        readonly List<(Vector2 c, float r, int level)> m_Patches = new List<(Vector2, float, int)>();

        /// <summary>The lift for a small patch at c, radius r: the lowest level that no patch it overlaps is on.</summary>
        float PatchLift(Vector2 c, float r)
        {
            float reach = r * 1.3f + 0.1f; // (its ragged edge wobbles out up to 30%)
            int used = 0;
            foreach (var p in m_Patches) if ((p.c - c).magnitude < p.r + reach) used |= 1 << p.level;
            int level = 0;
            while (level < PatchLevels - 1 && (used & (1 << level)) != 0) level++;
            m_Patches.Add((c, reach, level));
            return PatchBase + level * PatchStep;
        }

        GameObject Own(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) go.AddComponent<OwnedMesh>().Mesh = mf.sharedMesh;
            return go;
        }

        /// <summary>A flat, ragged, roughly round patch on the ground: rings of corners 1-1.25 m apart, each one on the
        /// ground (so a big patch follows the ground's rise and fall too), the edge wobbling in and out.</summary>
        void Blob(Transform t, Vector2 c, float r, float jitter, System.Random rng, Color col, float lift, string name)
        {
            var mb = new MeshBatch();
            int n = r > 4f ? 64 : r > 2f ? 24 : 12, rings = Mathf.Max(1, Mathf.CeilToInt(r / 1.25f));
            float o = (float)rng.NextDouble() * 100f;
            var rim = new float[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float wob = (Mathf.PerlinNoise(o + Mathf.Cos(a) * 1.6f, o + Mathf.Sin(a) * 1.6f) - 0.5f) * 1.4f + ((float)rng.NextDouble() * 2f - 1f) * 0.3f;
                rim[i] = r * (1f + wob * jitter);
            }
            Vector3 P(int ring, int i)
            {
                float a = (i % n) * Mathf.PI * 2f / n, rr = rim[i % n] * ring / rings;
                float x = c.x + Mathf.Cos(a) * rr, z = c.y + Mathf.Sin(a) * rr;
                return new Vector3(x, G(x, z) + lift, z);
            }
            var mid = new Vector3(c.x, G(c.x, c.y) + lift, c.y);
            for (int i = 0; i < n; i++)
            {
                mb.Tri(mid, P(1, i), P(1, i + 1), Vector3.up);
                for (int ring = 1; ring < rings; ring++) mb.Quad(P(ring, i), P(ring + 1, i), P(ring + 1, i + 1), P(ring, i + 1), Vector3.up);
            }
            var go = mb.Build(t, name, Art.Mat(col), false);
            Own(go);
            go.AddComponent<GroundMarker>(); // (no collider: just so the AI PSX look skins it as ground - dirt, not wood)
        }

        // ---------------- the soil heaped up at the point of impact ----------------

        /// <summary>The saucer's footprint seen from above (an ellipse: tilted, it's foreshortened along x), in "rim
        /// units": 1 on the rim, and the angle round it (pi = the near side, towards the ball).</summary>
        static void Footprint(float x, float z, out float e, out float ang)
        {
            float u = (x - SaucerDist) / (SaucerR * Mathf.Cos(Tilt * Mathf.Deg2Rad)), v = z / SaucerR;
            e = Mathf.Sqrt(u * u + v * v);
            ang = Mathf.Atan2(v, u);
        }

        /// <summary>How high the heap of ploughed-up soil stands over the ground here (this object's space): a bank pushed
        /// up in front of the buried near edge like a bow wave, thinning round the sides, sloping up onto the hull and
        /// down into the crater, lumpy; kept down near the lines from the bases to the ball.</summary>
        float SoilH(float x, float z)
        {
            Footprint(x, z, out float e, out float ang);
            float c = Mathf.Cos(ang);
            float front = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, -0.25f, c)) * (0.3f + 0.7f * Mathf.Max(0f, -c));
            if (front <= 0f) return 0f;
            float prof = e < 1.08f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1.08f, e)) : Mathf.Exp(-Mathf.Pow((e - 1.08f) / 0.24f, 2f));
            float lumps = 0.75f + 0.5f * Mathf.PerlinNoise(x * 0.9f + 31.7f, z * 0.9f + 12.3f);
            float sight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.4f, 2.5f, SightDist(new Vector3(x, 0, z))));
            return HeapH * front * prof * lumps * sight;
        }

        /// <summary>The heap (merged faceted meshes in the crash dirt colour, no collider of its own - its core is part of
        /// the saucer's solid lump), with clods and lumps of earth tumbled over it.</summary>
        void BuildSoilHeap(Transform t, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var heap = new MeshBatch();
            var heapDark = new MeshBatch();
            const int segs = 44, rings = 12;
            const float a0 = 50f * Mathf.Deg2Rad, a1 = 310f * Mathf.Deg2Rad, e0 = 0.55f, e1 = 1.62f;
            float cx = SaucerR * Mathf.Cos(Tilt * Mathf.Deg2Rad);
            var grid = new Vector3[segs + 1, rings + 1];
            for (int i = 0; i <= segs; i++)
                for (int k = 0; k <= rings; k++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)segs), e = Mathf.Lerp(e0, e1, k / (float)rings);
                    // (a little jitter so the facets aren't a neat grid)
                    if (i > 0 && i < segs && k > 0 && k < rings) { a += R(-0.03f, 0.03f); e += R(-0.03f, 0.03f); }
                    float x = SaucerDist + Mathf.Cos(a) * e * cx, z = Mathf.Sin(a) * e * SaucerR;
                    float h = SoilH(x, z);
                    // (where there's no heap it sinks just under the ground, so the heap grows out of the dirt)
                    grid[i, k] = new Vector3(x, G(x, z) + (h > 0.02f ? h : -0.06f), z);
                }
            for (int i = 0; i < segs; i++)
                for (int k = 0; k < rings; k++)
                {
                    var mb = (i * 7 + k * 3) % 5 == 0 ? heapDark : heap;
                    mb.Quad(grid[i, k], grid[i + 1, k], grid[i + 1, k + 1], grid[i, k + 1], Vector3.up);
                }
            // clods and lumps of earth tumbled over it, thickest along its crest
            var clods = new MeshBatch();
            for (int i = 0; i < 34; i++)
            {
                float a = R(80f, 280f) * Mathf.Deg2Rad, e = R(0.85f, 1.45f);
                float x = SaucerDist + Mathf.Cos(a) * e * cx, z = Mathf.Sin(a) * e * SaucerR;
                float h = SoilH(x, z);
                if (h < 0.15f) continue;
                float s = R(0.25f, 0.6f);
                var size = new Vector3(s * R(1f, 1.5f), s * R(0.45f, 0.7f), s);
                MapScenery.AddRock(clods, new Vector3(x, G(x, z) + h - size.y * 0.2f, z), Quaternion.Euler(R(-15f, 15f), R(0f, 360f), R(-15f, 15f)), size, rng.Next(), 0.3f);
            }
            using (ColorSlots.Use(ColorSlots.BallZone))
            {
                Own(heap.Build(t, "rubble soil heap", Art.Mat(k_CrashDirt * 0.92f), true)).AddComponent<GroundMarker>();
                Own(heapDark.Build(t, "rubble soil heap dark", Art.Mat(k_CrashDirt * 0.74f), true)).AddComponent<GroundMarker>();
                Own(clods.Build(t, "rubble soil clods", Art.Mat(k_CrashDirt * 0.82f), true)).AddComponent<GroundMarker>();
            }
        }

        // ---------------- the saucer ----------------

        /// <summary>The upper hull: an ellipsoid of these radii, centred this high in the saucer's space.</summary>
        const float UpA = SaucerR * 0.84f, UpB = 0.82f, UpY = 0.1f, LowB = 0.6f;

        /// <summary>A point on the upper hull above (x, z) in the saucer's space, and the way out of the hull there.</summary>
        static Vector3 HullPoint(float x, float z, out Vector3 normal)
        {
            float k = 1f - (x * x + z * z) / (UpA * UpA);
            float y = UpY + UpB * Mathf.Sqrt(Mathf.Max(0f, k));
            normal = new Vector3(x / (UpA * UpA), (y - UpY) / (UpB * UpB), z / (UpA * UpA)).normalized;
            return new Vector3(x, y, z);
        }

        void BuildSaucer(Transform t, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // nose-down and steeply upturned: its near edge (towards the ball) driven ~0.9 m into the ground under a heap of
            // soil, the far edge reared up in the air
            float tilt = Tilt * Mathf.Deg2Rad;
            float yc = -Bury + SaucerR * Mathf.Sin(tilt) + LowB * Mathf.Cos(tilt) + G(SaucerDist - SaucerR * Mathf.Cos(tilt), 0);
            m_Saucer = new GameObject("saucer").transform;
            m_Saucer.SetParent(t, false);
            m_Saucer.localPosition = new Vector3(SaucerDist, yc, 0);
            m_Saucer.localRotation = Quaternion.Euler(0, 0, Tilt) * Quaternion.Euler(5f, 0, 0) * Quaternion.Euler(0, 20f, 0);
            var s = m_Saucer;

            // the hull: a dark lower half, a silver upper half and a rim band with lights (most dead, one blinking)
            Workbench.Ball(s, k_HullDark, new Vector3(0, -0.05f, 0), new Vector3(SaucerR, LowB, SaucerR));
            Workbench.Ball(s, k_Hull, new Vector3(0, UpY, 0), new Vector3(UpA, UpB, UpA));
            Workbench.Cyl(s, k_Rim, new Vector3(0, 0.02f, 0), SaucerR * 1.02f, 0.16f);
            Workbench.Cyl(s, k_HullDark, new Vector3(0, -0.07f, 0), SaucerR * 0.98f, 0.05f);
            var gAlien = Workbench.Glow(k_Alien, 1.8f);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                bool lit = i % 5 == 1, blink = i == 8;
                var p = new Vector3(Mathf.Cos(a) * SaucerR * 1.03f, 0.02f, Mathf.Sin(a) * SaucerR * 1.03f);
                var l = Art.Part(s, Art.Ico, lit || blink ? k_Alien : k_Dark, p, Vector3.one * 0.11f, default, false, lit || blink ? gAlien : null, "rim light");
                if (blink) m_Blink = l.transform;
            }
            // panel seams on the upper hull
            var seams = new MeshBatch();
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / 8f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector3 prev = default;
                for (int k = 0; k <= 4; k++)
                {
                    float rr = Mathf.Lerp(1.75f, UpA * 0.97f, k / 4f);
                    var p = HullPoint(d.x * rr, d.y * rr, out var nrm) + nrm * 0.015f;
                    if (k > 0) seams.Line(prev, p, 0.04f, 0.02f, nrm);
                    prev = p;
                }
            }
            Own(seams.Build(s, "seams", Art.Mat(k_HullDark), false));
            // landing pods underneath (one is torn off - it's lying out in the debris)
            for (int i = 0; i < 2; i++)
            {
                float a = (i == 0 ? 60f : 180f) * Mathf.Deg2Rad;
                Workbench.Ball(s, k_HullDark, new Vector3(Mathf.Cos(a) * 2.3f, -0.5f, Mathf.Sin(a) * 2.3f), new Vector3(0.45f, 0.22f, 0.45f));
            }

            // the cockpit: a glass dome on a ring, cracked, broken open on the side towards the ball
            float cy = UpY + UpB - 0.05f;
            Workbench.Cyl(s, k_Rim, new Vector3(0, cy, 0), 1.72f, 0.16f);
            Workbench.Cyl(s, k_Dark, new Vector3(0, cy + 0.08f, 0), 1.55f, 0.02f); // (the floor inside)
            Art.Box(s, k_HullDark, new Vector3(0.35f, cy + 0.35f, 0), new Vector3(0.5f, 0.55f, 0.6f), new Vector3(0, 0, -12f)); // a seat
            Art.Box(s, k_Alien, new Vector3(-0.75f, cy + 0.3f, 0), new Vector3(0.12f, 0.35f, 0.9f), new Vector3(0, 0, 20f), false, gAlien); // its console, still lit
            var glass = new MeshBatch();
            var cracks = new MeshBatch();
            const int rings = 4, segs = 12;
            const float DR = 1.6f, DH = 1.35f;
            Vector3 D(int ring, int seg)
            {
                float lat = Mathf.PI * 0.5f * ring / rings, lon = Mathf.PI * 2f * seg / segs;
                return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * DR, cy + 0.07f + Mathf.Sin(lat) * DH, Mathf.Cos(lat) * Mathf.Sin(lon) * DR);
            }
            var dc = new Vector3(0, cy, 0);
            // the hole faces local -x (towards the ball): seg 6 is -x
            bool Broken(int ring, int seg) => (seg >= 5 && seg <= 7 && ring >= 0 && ring <= 2) || (seg == 6 && ring == 3) || (seg == 4 && ring == 1) || (seg == 8 && ring == 2);
            for (int g = 0; g < segs; g++)
                for (int r = 0; r < rings; r++)
                {
                    if (Broken(r, g)) continue;
                    Vector3 p00 = D(r, g), p01 = D(r, g + 1), p10 = D(r + 1, g), p11 = D(r + 1, g + 1);
                    var outw = (p00 + p11) * 0.5f - dc;
                    if (r == rings - 1) { glass.Tri(p00, p01, p10, outw); glass.Tri(p00, p01, p10, -outw); }
                    else { glass.Quad(p00, p01, p11, p10, outw); glass.Quad(p00, p01, p11, p10, -outw); }
                }
            // jagged shards still standing round the hole, bent outwards
            for (int k = 0; k < 6; k++)
            {
                int g = 4 + k % 5, r = k < 3 ? 0 : 1;
                var a = D(r, g);
                var b = D(r, g + 1);
                var tip = Vector3.Lerp(a, b, R(0.3f, 0.7f)) + (Vector3.Lerp(a, b, 0.5f) - dc).normalized * R(0.15f, 0.35f) + Vector3.up * R(0.25f, 0.55f);
                glass.Tri(a, b, tip, Vector3.Lerp(a, b, 0.5f) - dc);
                glass.Tri(a, b, tip, dc - Vector3.Lerp(a, b, 0.5f));
            }
            // cracks running over what's left of it
            for (int k = 0; k < 7; k++)
            {
                int g = (k * 5 + 1) % segs;
                if (g >= 4 && g <= 8) g = (g + 6) % segs;
                var p = D(0, g);
                for (int step = 0; step < 3; step++)
                {
                    var q = D(Mathf.Min(rings, step + 1), g + (step % 2 == 0 ? 1 : 0));
                    q = Vector3.Lerp(p, q, 0.85f);
                    cracks.Line(p, q, 0.025f, 0.02f, (p + q) * 0.5f - dc);
                    p = q;
                }
            }
            Own(glass.Build(s, "cockpit glass", Art.Ghost(new Color(0.6f, 0.9f, 1f, 0.28f)), false));
            Own(cracks.Build(s, "cockpit cracks", Art.Ghost(new Color(0.9f, 0.97f, 1f, 0.7f)), false));
            // a bent antenna
            Workbench.Rod(s, k_Rim, new Vector3(1.0f, cy + 0.05f, -1.1f), new Vector3(1.6f, cy + 0.9f, -1.5f), 0.035f);
            Workbench.Rod(s, k_Rim, new Vector3(1.6f, cy + 0.9f, -1.5f), new Vector3(2.3f, cy + 1.0f, -1.3f), 0.03f);
            Art.Part(s, Art.Ico, k_Dark, new Vector3(2.32f, cy + 1.0f, -1.3f), Vector3.one * 0.08f);

            // a torn gash in each side of the hull: charred, plates bent out, wires hanging
            Gash(s, new Vector2(0.6f, 2.35f), 1f, rng);
            Gash(s, new Vector2(1.4f, -2.3f), 0.75f, rng);
            Gash(s, new Vector2(2.9f, 0.6f), 0.6f, rng);

            // the solid lump: the hull and cockpit, and straight down from them into the ground
            BuildCollider(t);
        }

        void Gash(Transform s, Vector2 at, float size, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var p = HullPoint(at.x, at.y, out var n);
            var rot = Quaternion.FromToRotation(Vector3.up, n);
            // a black hole torn in the skin, with a charred ring round it
            Art.Part(s, Art.MakeRock(7, 0.3f), k_Char, p - n * 0.02f, new Vector3(0.75f, 0.12f, 0.5f) * size, rot.eulerAngles);
            Art.Part(s, Art.MakeRock(8, 0.25f), k_Dark, p + n * 0.01f, new Vector3(0.5f, 0.1f, 0.3f) * size, rot.eulerAngles);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + R(-0.4f, 0.4f);
                var o = rot * new Vector3(Mathf.Cos(a) * 0.55f, 0, Mathf.Sin(a) * 0.38f) * size;
                var bend = rot * Quaternion.AngleAxis(R(35f, 70f), Vector3.Cross(Vector3.up, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)))) ;
                Art.Box(s, i % 2 == 0 ? k_Hull : k_HullDark, p + o + n * 0.12f * size, new Vector3(0.45f, 0.03f, 0.3f) * size, bend.eulerAngles);
            }
            for (int i = 0; i < 3; i++)
            {
                var a = p + rot * new Vector3(R(-0.2f, 0.2f), 0, R(-0.15f, 0.15f)) * size;
                var b = a + n * R(0.2f, 0.45f) * size + rot * new Vector3(R(-0.4f, 0.4f), 0, R(-0.4f, 0.4f)) * size;
                Workbench.Rod(s, i == 0 ? new Color(0.85f, 0.3f, 0.15f) : i == 1 ? new Color(0.9f, 0.8f, 0.2f) : k_Dark, a, b, 0.018f);
            }
            var world = s.TransformPoint(p + n * 0.1f);
            m_SmokeAt.Add(world);
            m_SparkAt.Add(world);
            m_GashFires.Add((world, size));
        }

        readonly List<(Vector3 at, float size)> m_GashFires = new List<(Vector3, float)>();

        /// <summary>One convex collider: points round the hull and the cockpit, plus the same points dropped into the ground.</summary>
        void BuildCollider(Transform t)
        {
            var pts = new List<Vector3>();
            const int n = 14;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                pts.Add(m_Saucer.localRotation * new Vector3(c * SaucerR * 1.02f, 0.02f, sn * SaucerR * 1.02f) + m_Saucer.localPosition);
                pts.Add(m_Saucer.localRotation * new Vector3(c * 1.7f, UpY + UpB + 0.95f, sn * 1.7f) + m_Saucer.localPosition);
            }
            pts.Add(m_Saucer.localRotation * new Vector3(0, UpY + UpB + 1.35f, 0) + m_Saucer.localPosition);
            // the core of the soil heap banked up against the buried edge, so you climb it instead of wading through it
            float cx = SaucerR * Mathf.Cos(Tilt * Mathf.Deg2Rad);
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.Lerp(100f, 260f, i / 16f) * Mathf.Deg2Rad;
                float x = SaucerDist + Mathf.Cos(a) * 1.08f * cx, z = Mathf.Sin(a) * 1.08f * SaucerR;
                float h = SoilH(x, z);
                if (h > 0.45f) pts.Add(new Vector3(x, G(x, z) + h - 0.2f, z));
            }
            int top = pts.Count;
            for (int i = 0; i < top; i++)
            {
                var p = pts[i];
                pts.Add(new Vector3(p.x, G(p.x, p.z) - 0.6f, p.z));
            }
            var tris = new List<int>();
            for (int i = 1; i + 1 < pts.Count; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            var mesh = new Mesh { name = "crash collider" };
            mesh.SetVertices(pts);
            mesh.SetTriangles(tris, 0);
            var go = new GameObject("saucer collider");
            go.transform.SetParent(t, false);
            go.AddComponent<OwnedMesh>().Mesh = mesh;
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = mesh;
            Collider = mc;
        }

        // ---------------- debris ----------------

        void BuildDebris(Transform t, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // bent hull plates half stuck in the ground, a torn-off landing pod, a broken rim piece, a cracked green power cell
            var spots = new[]
            {
                new Vector2(3.6f, 2.0f), new Vector2(3.4f, -2.2f), new Vector2(-4.2f, 5.6f), new Vector2(-3.4f, -3.2f),
                new Vector2(-6.2f, 1.2f), new Vector2(-5.0f, -5.6f), new Vector2(7.6f, 4.9f), new Vector2(8.4f, -4.6f),
                new Vector2(3.0f, 6.9f), new Vector2(-6.6f, -4.0f), new Vector2(10.4f, 1.8f), new Vector2(-7.8f, -2.4f),
                new Vector2(5.4f, -6.6f), new Vector2(-2.0f, 7.8f),
                // further out, flung out over the grass
                new Vector2(12.5f, 6.5f), new Vector2(14.0f, -3.0f), new Vector2(-11.0f, 7.5f), new Vector2(-13.5f, -6.0f),
                new Vector2(9.0f, -11.0f), new Vector2(-8.5f, 12.0f), new Vector2(16.5f, 9.0f), new Vector2(-17.0f, 2.5f),
                new Vector2(6.0f, 15.5f), new Vector2(-4.0f, -15.0f), new Vector2(18.0f, -8.0f), new Vector2(-15.0f, -11.0f),
                new Vector2(11.0f, 13.0f), new Vector2(3.5f, -17.5f), new Vector2(-12.0f, 15.0f), new Vector2(20.0f, 2.0f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var sp = spots[i];
                var p = new Vector3(sp.x, G(sp.x, sp.y), sp.y);
                if (OnSightline(p, 1.6f)) continue; // (low as they are, nothing goes between a base and the ball)
                int kind = i % 4;
                if (kind == 0 || kind == 3)
                {
                    // a bent plate, one edge dug in
                    var plate = new GameObject("plate").transform;
                    plate.SetParent(t, false);
                    plate.localPosition = p + Vector3.up * 0.08f;
                    plate.localRotation = Quaternion.Euler(R(-25f, 25f), R(0, 360f), R(8f, 28f));
                    float w = R(0.7f, 1.3f);
                    Art.Box(plate, kind == 0 ? k_Hull : k_HullDark, new Vector3(0, 0, 0), new Vector3(w, 0.04f, w * 0.6f));
                    Art.Box(plate, kind == 0 ? k_Hull : k_HullDark, new Vector3(w * 0.62f, 0.09f, 0), new Vector3(w * 0.3f, 0.04f, w * 0.6f), new Vector3(0, 0, 25f));
                    Art.Box(plate, k_Char, new Vector3(-w * 0.2f, 0.025f, 0.05f), new Vector3(w * 0.4f, 0.01f, w * 0.3f));
                }
                else if (kind == 1)
                {
                    // a chunk of the rim with two dead lights
                    var rim = new GameObject("rim piece").transform;
                    rim.SetParent(t, false);
                    rim.localPosition = p + Vector3.up * 0.1f;
                    rim.localRotation = Quaternion.Euler(R(-10f, 10f), R(0, 360f), R(-12f, 12f));
                    Art.Box(rim, k_Rim, Vector3.zero, new Vector3(1.4f, 0.16f, 0.22f));
                    Art.Box(rim, k_HullDark, new Vector3(0, -0.08f, -0.05f), new Vector3(1.3f, 0.06f, 0.3f));
                    for (int k = -1; k <= 1; k += 2) Art.Part(rim, Art.Ico, k_Dark, new Vector3(k * 0.35f, 0, 0.12f), Vector3.one * 0.09f);
                }
                else
                {
                    // the torn-off landing pod, or a cracked power cell leaking green light
                    if (i % 8 == 2)
                    {
                        Workbench.Ball(t, k_HullDark, p + Vector3.up * 0.12f, new Vector3(0.45f, 0.22f, 0.45f));
                        Workbench.Rod(t, k_Rim, p + Vector3.up * 0.2f, p + new Vector3(0.3f, 0.65f, 0.2f), 0.05f);
                    }
                    else
                    {
                        var cell = new GameObject("power cell").transform;
                        cell.SetParent(t, false);
                        cell.localPosition = p + Vector3.up * 0.18f;
                        cell.localRotation = Quaternion.Euler(0, R(0, 360f), 80f);
                        Workbench.Cyl(cell, k_Rim, new Vector3(0, -0.28f, 0), 0.22f, 0.08f);
                        Workbench.Cyl(cell, k_Rim, new Vector3(0, 0.28f, 0), 0.22f, 0.08f);
                        Workbench.Cyl(cell, k_Alien, Vector3.zero, 0.16f, 0.5f, default, Workbench.Glow(k_Alien, 2.2f));
                        Art.Part(t, Art.Ico, k_Alien, new Vector3(p.x + 0.4f, p.y + 0.03f, p.z), new Vector3(0.5f, 0.02f, 0.35f), default, false, Workbench.Glow(k_Alien, 1.2f)); // (a puddle of it)
                    }
                }
            }
            // glass from the cockpit, scattered towards the ball
            var shards = new MeshBatch();
            for (int i = 0; i < 16; i++)
            {
                float x = R(1.2f, 3.4f), z = R(-2.4f, 2.4f);
                if (new Vector2(x, z).magnitude < 1.1f) continue;
                var c = new Vector3(x, G(x, z) + 0.1f, z);
                float a = R(0, Mathf.PI * 2f), sz = R(0.12f, 0.3f);
                var p0 = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * sz;
                var p1 = c + new Vector3(Mathf.Cos(a + 2.2f), R(0.02f, 0.12f), Mathf.Sin(a + 2.2f)) * sz;
                var p2 = c + new Vector3(Mathf.Cos(a + 4.1f), 0, Mathf.Sin(a + 4.1f)) * sz * 0.7f;
                shards.Tri(p0, p1, p2, Vector3.up);
            }
            Own(shards.Build(t, "glass shards", Art.Ghost(new Color(0.75f, 0.95f, 1f, 0.55f)), false));
            // torn scraps of hull all over (flat bits, two merged meshes)
            var scrap = new MeshBatch();
            var scrapDark = new MeshBatch();
            for (int i = 0; i < 46; i++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = R(2.6f, 20f);
                var c = new Vector3(Mathf.Cos(a) * rr + 1f, 0, Mathf.Sin(a) * rr);
                if (InSaucer(c)) continue;
                float w = R(0.12f, 0.38f);
                c.y = G(c.x, c.z) + 0.04f;
                (i % 3 == 0 ? scrapDark : scrap).Box(c, Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)), new Vector3(w, 0.03f, w * R(0.4f, 0.9f)));
            }
            Own(scrap.Build(t, "rubble scrap", Art.Mat(k_Hull), false));
            Own(scrapDark.Build(t, "rubble scrap dark", Art.Mat(k_HullDark), false));
        }

        /// <summary>Is this spot (this object's space) under the saucer?</summary>
        bool InSaucer(Vector3 p, float margin = 0.6f) => new Vector2(p.x - SaucerDist, p.z).magnitude < SaucerR + margin;

        // ---------------- rubble ----------------

        /// <summary>
        /// Rubble thrown out by the impact (no colliders, like the debris; merged meshes, a handful of draws): slabs of
        /// ground broken and pushed up round the crater's rim (a few still with grass on top), stones and lumps of rock -
        /// thickest round the crater and along the furrow, thinning out into the grass - and clods of dirt. Nothing
        /// bigger than gravel lies between a base and the ball.
        /// </summary>
        void BuildRubble(Transform t, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var sod = new MeshBatch();
            var sodTop = new MeshBatch();
            var clods = new MeshBatch();
            var stone = new MeshBatch();
            var stoneDark = new MeshBatch();
            // slabs of ground pushed up round the crater, their inner edge up towards it
            for (int i = 0; i < 18; i++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = R(4.3f, 7.8f);
                var outDir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var c = outDir * rr + new Vector3(0.6f, 0, 0);
                if (OnSightline(c, 2.3f) || InSaucer(c, 1f)) continue;
                float w = R(0.8f, 1.7f), d = R(0.55f, 1.1f), th = R(0.2f, 0.32f), tilt = R(16f, 48f);
                var rot = Quaternion.LookRotation(outDir) * Quaternion.Euler(tilt, R(-25f, 25f), R(-10f, 10f));
                c.y = G(c.x, c.z) + Mathf.Sin(tilt * Mathf.Deg2Rad) * d * 0.25f;
                sod.Box(c, rot, new Vector3(w, th, d));
                // (a few still have their grass on: most are dirt all over - green tops in the dirt read as holes in it)
                if (i % 5 == 1) sodTop.Box(c + rot * Vector3.up * (th * 0.5f + 0.02f), rot, new Vector3(w * 0.97f, 0.04f, d * 0.97f));
            }
            // stones and lumps of rock: the big ones first, then smaller and smaller, further out
            for (int i = 0; i < 110; i++)
            {
                bool near = i < 60;
                float a = R(0f, Mathf.PI * 2f), rr = near ? R(2.8f, 9.5f) : R(9f, 19f);
                var c = new Vector3(Mathf.Cos(a) * rr + 0.8f, 0, Mathf.Sin(a) * rr);
                if (new Vector2(c.x, c.z).magnitude < 2.4f || InSaucer(c)) continue;
                float s = i < 22 ? R(0.35f, 0.75f) : R(0.1f, 0.32f);
                if (s > 0.2f && OnSightline(c, 1.8f + s)) continue; // (only gravel between a base and the ball)
                var size = new Vector3(s * R(0.9f, 1.4f), s * R(0.5f, 0.85f), s * R(0.8f, 1.2f));
                c.y = G(c.x, c.z) + size.y * 0.25f;
                MapScenery.AddRock(i % 3 == 0 ? stoneDark : stone, c, Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-20f, 20f)), size, rng.Next());
            }
            // along the furrow: stones heaped on both sides
            for (int i = 0; i < 16; i++)
            {
                var c = new Vector3(R(SaucerDist + SaucerR - 0.5f, 13.5f), 0, (i % 2 == 0 ? 1f : -1f) * R(1.4f, 2.9f));
                float s = R(0.15f, 0.45f);
                var size = new Vector3(s * R(0.9f, 1.4f), s * R(0.5f, 0.8f), s);
                c.y = G(c.x, c.z) + 0.09f + size.y * 0.2f;
                MapScenery.AddRock(i % 2 == 0 ? stone : stoneDark, c, Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-20f, 20f)), size, rng.Next());
            }
            // clods of dirt
            for (int i = 0; i < 46; i++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = R(3.5f, DirtR + 3f);
                var c = new Vector3(Mathf.Cos(a) * rr + 0.8f, 0, Mathf.Sin(a) * rr);
                if (InSaucer(c)) continue;
                float s = R(0.14f, 0.42f);
                if (s > 0.22f && OnSightline(c, 1.8f + s)) continue;
                var size = new Vector3(s * R(1f, 1.5f), s * R(0.4f, 0.6f), s);
                c.y = G(c.x, c.z) + size.y * 0.2f;
                MapScenery.AddRock(clods, c, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), size, rng.Next(), 0.3f);
            }
            using (ColorSlots.Use(ColorSlots.BallZone))
            {
                // (marked as ground, no collider: the AI PSX look skins them as dirt)
                Own(sod.Build(t, "rubble slabs", Art.Mat(k_CrashDirt * 0.9f), true)).AddComponent<GroundMarker>();
                Own(clods.Build(t, "rubble clods", Art.Mat(k_CrashDirt * 0.95f), true)).AddComponent<GroundMarker>();
            }
            // (grass on the slabs: the map's own ground colour; the theme maps' ground isn't grass)
            if (!ThemeMaps.IsTheme) Own(sodTop.Build(t, "rubble slab grass", WorldLook.GroundMaterial(false), false)); // THEME MAPS
            using (ColorSlots.Use(ColorSlots.Boulders))
            {
                Own(stone.Build(t, "rubble stones", Art.Mat(k_Stone), true));
                Own(stoneDark.Build(t, "rubble stones dark", Art.Mat(k_StoneDark), true));
            }
        }

        // ---------------- fire, smoke and sparks ----------------

        static Mesh s_FlameMesh;

        /// <summary>One flame tongue: a six-sided low-poly spindle, widest a fifth of the way up, a long point on top, a short
        /// one underneath (so it reads as a flame, not a cone). Its tip leans a little.</summary>
        static Mesh FlameMesh()
        {
            if (s_FlameMesh != null) return s_FlameMesh;
            var mb = new MeshBatch();
            const int n = 6;
            var top = new Vector3(0.12f, 1f, 0.05f);
            var bottom = new Vector3(0, -0.22f, 0);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0.12f, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0.12f, Mathf.Sin(a1) * 0.5f);
                var mid = (p0 + p1) * 0.5f;
                mb.Tri(p0, p1, top, new Vector3(mid.x, 0.4f, mid.z));
                mb.Tri(p0, p1, bottom, new Vector3(mid.x, -0.6f, mid.z));
            }
            var go = mb.Build(null, "flame mesh", null, false);
            s_FlameMesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return s_FlameMesh;
        }

        /// <summary>A flat, bright, unlit fire colour (lit flames get a dark side; that doesn't look like fire).</summary>
        Material FireMat(Color c, float glow)
        {
            var sh = Resources.Load<Shader>("SpaceArena/SpaceGlow");
            if (sh == null || !sh.isSupported) return Emissive(c, glow);
            var m = new Material(sh) { name = "fire" };
            m.SetColor("_Color", c);
            m_OwnMats.Add(m);
            return m;
        }

        void BuildFires(Transform t, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mats = new[] { FireMat(new Color(0.95f, 0.22f, 0.05f), 1.6f), FireMat(new Color(1f, 0.45f, 0.06f), 1.8f), FireMat(new Color(1f, 0.68f, 0.12f), 2f), FireMat(new Color(1f, 0.9f, 0.4f), 2.2f) };
            var ember = FireMat(new Color(0.55f, 0.08f, 0.02f), 1.2f);
            var flame = FlameMesh();
            void Fire(Vector3 world, float size)
            {
                var root = new GameObject("fire").transform;
                root.SetParent(t, false);
                root.position = world;
                // glowing embers underneath
                Art.Part(root, Art.Ico, Color.white, Vector3.zero, new Vector3(0.42f, 0.1f, 0.42f) * size, new Vector3(0, R(0, 360f), 0), false, ember, "embers");
                // tongues of flame round it: the tall red-orange ones out round the edge, shorter yellow ones in the
                // middle and in front (each one is thin, so the yellow ones aren't hidden inside the red ones)
                int n = size > 0.75f ? 9 : 6;
                for (int i = 0; i < n; i++)
                {
                    float k = i / (float)(n - 1);
                    int ci = Mathf.Clamp(Mathf.FloorToInt(k * 4f), 0, 3);
                    float ring = Mathf.Lerp(0.3f, 0.05f, k) * size;
                    float a = i * 2.4f + R(-0.3f, 0.3f);
                    float w = Mathf.Lerp(0.34f, 0.2f, k) * size, h = Mathf.Lerp(1.15f, 0.55f, k) * size * R(0.8f, 1.2f);
                    var f = Art.Part(root, flame, Color.white, new Vector3(Mathf.Cos(a) * ring, 0.02f, Mathf.Sin(a) * ring), new Vector3(w, h, w), new Vector3(0, R(0, 360f), 0), false, mats[ci], "flame");
                    f.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    m_Flames.Add(new Flame { T = f.transform, Scale = f.transform.localScale, Seed = R(0f, 100f) });
                }
            }
            foreach (var gash in m_GashFires) Fire(gash.at, gash.size);
            // on the ground: by the buried nose, in the furrow, on bits of debris (never between a base and the ball)
            var ground = new[] { new Vector3(2.7f, 0, 2.4f), new Vector3(9.6f, 0, -0.7f), new Vector3(3.6f, 0, 2.0f), new Vector3(-3.4f, 0, -3.2f), new Vector3(3.4f, 0, -2.2f), new Vector3(-6.2f, 0, 1.2f) };
            float[] sizes = { 0.8f, 0.9f, 0.55f, 0.5f, 0.45f, 0.5f };
            for (int i = 0; i < ground.Length; i++)
            {
                var g = ground[i];
                if (OnSightline(g, 1.6f)) continue;
                g.y = G(g.x, g.z) + SoilH(g.x, g.z) + 0.05f;
                var w = t.TransformPoint(g);
                Fire(w, sizes[i]);
                if (i < 2) m_SmokeAt.Add(w + Vector3.up * 0.3f);
            }
        }

        void Update()
        {
            float time = Time.time, dt = Time.deltaTime;
            // flickering flames
            for (int i = 0; i < m_Flames.Count; i++)
            {
                var f = m_Flames[i];
                if (f.T == null) continue;
                float n1 = Mathf.PerlinNoise(time * 5.2f, f.Seed), n2 = Mathf.PerlinNoise(f.Seed, time * 6.1f);
                f.T.localScale = new Vector3(f.Scale.x * (0.8f + 0.35f * n2), f.Scale.y * (0.55f + 0.85f * n1), f.Scale.z * (0.8f + 0.35f * n2));
                f.T.localRotation = Quaternion.Euler((n2 - 0.5f) * 22f, f.Seed * 7f + time * 70f, (n1 - 0.5f) * 22f);
            }
            if (m_Light) m_Light.intensity = m_LightBase * (0.7f + 0.6f * Mathf.PerlinNoise(time * 6f, 3.3f));
            if (m_Blink) m_Blink.gameObject.SetActive(Mathf.Repeat(time, 1.7f) < 0.25f || Mathf.Repeat(time + 0.4f, 2.9f) < 0.1f);

            // smoke: a puff every so often from one of the fires, rising, drifting, growing, then shrinking away
            if (m_SmokeAt.Count > 0 && time > m_NextPuff && m_Puffs.Count > 0)
            {
                m_NextPuff = time + 0.28f;
                var pf = m_Puffs[m_NextPuffSlot];
                m_NextPuffSlot = (m_NextPuffSlot + 1) % m_Puffs.Count;
                var from = m_SmokeAt[Random.Range(0, m_SmokeAt.Count)];
                pf.T.position = from + Random.insideUnitSphere * 0.2f + Vector3.up * 0.4f;
                pf.Vel = new Vector3(0.35f, 1.25f, 0.15f) + Random.insideUnitSphere * 0.2f;
                pf.Age = 0f;
                pf.Life = Random.Range(3.2f, 4.4f);
                pf.Size = Random.Range(0.4f, 0.65f);
                pf.T.rotation = Random.rotation;
                pf.T.gameObject.SetActive(true);
                m_Puffs[(m_NextPuffSlot + m_Puffs.Count - 1) % m_Puffs.Count] = pf;
            }
            for (int i = 0; i < m_Puffs.Count; i++)
            {
                var pf = m_Puffs[i];
                if (pf.T == null || !pf.T.gameObject.activeSelf) continue;
                pf.Age += dt;
                if (pf.Age >= pf.Life) { pf.T.gameObject.SetActive(false); m_Puffs[i] = pf; continue; }
                float k = pf.Age / pf.Life;
                pf.T.position += pf.Vel * dt;
                pf.Vel *= 1f - 0.12f * dt;
                float grow = Mathf.Lerp(0.35f, 1.7f, Mathf.Sqrt(k)) * (k > 0.7f ? 1f - (k - 0.7f) / 0.3f : 1f);
                pf.T.localScale = Vector3.one * pf.Size * Mathf.Max(0.001f, grow);
                m_Puffs[i] = pf;
            }

            // sparks spitting out of the torn hull (only when someone's close enough to see them)
            if (m_SparkAt.Count > 0 && time > m_NextSpark)
            {
                m_NextSpark = time + Random.Range(0.18f, 0.7f);
                var cam = Camera.main;
                if (cam != null && (cam.transform.position - transform.position).sqrMagnitude < 110f * 110f)
                {
                    var at = m_SparkAt[Random.Range(0, m_SparkAt.Count)];
                    int n = Random.Range(2, 5);
                    for (int i = 0; i < n; i++)
                        FxParticle.Spawn(at, Random.insideUnitSphere * 2.2f + Vector3.up * 2.4f, new Color(1f, 0.85f, 0.4f), 0.045f, Random.Range(0.4f, 0.75f), 9f, false);
                }
            }
        }
    }
}
