using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// CHERRY BLOSSOM (the second version - the first is kept as CherryBlossomV1Map in Map.CherryV1.cs: set UseNew to false
    /// to play that one again): dusk-blue sky, a moon, purple grass, blocky pink cherry trees, petals on the ground and
    /// drifting down, stone canals with red bridges and glowing stone lanterns - and a huge round
    /// bottomless hole in the middle (terraced earth walls dropping into a dark void, a glowing orange rim). The ball's
    /// island stands in the middle of it on a rock pillar, and on it a giant HOLLOW CHERRY TREE: a round bark wall with a
    /// doorway high up on every high route, a canopy of blossom over it (open in the middle, so the ball drops in) and the
    /// ball on its floor among blossom hedges. Every chunk of earth in the hole is one you can stand on:
    ///  - HIGH ROUTES from the rim up to the doorways: hops, jumps up onto higher chunks, long running jumps and long drops,
    ///    zigzagging; some chunks bob up and down or drift from side to side (they carry you; same time on every peer).
    ///  - THE WAY DOWN: no route from the rim - two chunks low in the pit on each team's side, that you can only reach by
    ///    dropping off a high route, then a hop onto the ledge at a cave mouth in the pillar: THE HOLLOW, a cave under the
    ///    ball's floor (lanterns, glowing mushrooms, root columns, a little shack in the middle you can go into), with a
    ///    ladder up through a fenced hatch into the tree.
    /// All the chunks dip, tilt and wobble when someone lands on them. Falling in is a long drop into the void before you
    /// die (KillY), and you're back a bit sooner (FallRespawnMul); the ball falls past -20 and comes back from the sky.
    /// </summary>
    public class CherryBlossomMap : ThemeMap
    {
        /// <summary>true: this version is played; false: the one before (CherryBlossomV1Map, Map.CherryV1.cs).</summary>
        public const bool UseNew = true;
        public override bool Enabled => UseNew;
        public override MapKind Kind => MapKind.CherryBlossom;
        public override string Label => "Cherry Blossom";
        public override string Blurb => "Cherry trees at dusk round a bottomless glowing hole. The ball is inside a giant hollow cherry tree in the middle: leap the floating chunks of earth up to its doorways - or down into the hollow under it and climb up through a hatch. Fall and you're gone.";
        public override float KillY => -26f;
        public override float FallRespawnMul => 0.6f;
        public override float MaxSpotHeight => 5f;

        static float Half => Cfg.MapHalf;
        static float IslandR => Cfg.SmallMap ? 15f : 17f;
        static float PitOut
        {
            get
            {
                float baseIn = Cfg.BaseCenter[0].magnitude - Cfg.BaseHalf;
                return Mathf.Max(IslandR + 6f, Mathf.Min(Mathf.Min(Half * 0.44f, 54f), baseIn - 6f));
            }
        }
        const float PitFloor = -46f, PitDeep = -95f, CanalHalfW = 2.2f, CanalY = -1.1f, WaterTop = -0.4f;
        /// <summary>The hollow tree: its trunk's outside, its wall's thickness, the porch out of each doorway.</summary>
        static float TreeOut => IslandR - 0.4f;
        const float TreeWall = 1.6f, PorchLen = 1.7f, DoorW = 3.8f, DoorH = 3.4f;
        static float TreeIn => TreeOut - TreeWall;
        /// <summary>How high the doorways are (at most: a route that can't climb that far gets a lower one).</summary>
        static float DoorYWant => Cfg.SmallMap ? 2.6f : Half >= 150f ? 6.2f : 5f;
        static float WallH => DoorYWant + DoorH + 3.2f;
        /// <summary>The hollow under the ball's floor: its floor, its ceiling, its radius; the cave mouths in the pillar (from
        /// MouthBot up to MouthTop, MouthW wide) and the ledge sticking out under each.</summary>
        const float ChamberFloor = -8.5f, ChamberTop = -1f, MouthTop = -3f, MouthBot = -9f, MouthW = 5f;
        static float ChamberR => IslandR - 2.6f;
        static float LedgeR => IslandR + 1.4f;

        /// <summary>The grass: a dusky purple (it goes with the dusk-blue sky and the pink blossom).</summary>
        static readonly Color k_Grass = new Color(0.5f, 0.39f, 0.62f), k_Grass2 = new Color(0.42f, 0.32f, 0.55f);
        static readonly Color k_Dirt = new Color(0.5f, 0.33f, 0.24f), k_Petal = new Color(0.9f, 0.62f, 0.76f);
        static readonly Color k_CanalBed = new Color(0.3f, 0.28f, 0.36f), k_Stone = new Color(0.6f, 0.58f, 0.6f);
        static readonly Color k_Rim = new Color(1f, 0.58f, 0.18f);

        // ---------------------------------------------------------------- the canals (along the lines between the teams)
        static Vector3 CanalDir0 => Cfg.FourWay ? new Vector3(1f, 0f, -1f).normalized : Vector3.right;
        static float CanalStart => PitOut - 1.5f;
        static float CanalEnd => Half - 6f;

        /// <summary>How far (x, z) is from the nearest canal's middle line (big if none is near).</summary>
        static float CanalLat(float x, float z)
        {
            float best = 999f;
            for (int k = 0; k < Cfg.Copies; k++)
            {
                var d = Cfg.Copy(CanalDir0, k);
                float a = x * d.x + z * d.z;
                if (a < CanalStart || a > CanalEnd) continue;
                best = Mathf.Min(best, Mathf.Abs(x * d.z - z * d.x));
            }
            return best;
        }

        static float Terrace(float h)
        {
            const float s = 0.85f;
            float f = h / s, fl = Mathf.Floor(f);
            return (fl + ThemeMaps.SmoothStepP(0.72f, 1f, f - fl)) * s;
        }

        public override float Height(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r <= IslandR) return 0f;
            if (r < PitOut) return PitFloor;
            float n = ThemeMaps.SymNoiseP(x, z, 0.028f, ThemeMaps.SeedP);
            float h = Mathf.Max(0f, (n - 0.42f) * 10f);
            float e = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (Half - 16f);
            if (e > 0) h += e * 0.8f;
            h *= ThemeMaps.SmoothStepP(PitOut + 2f, PitOut + 12f, r);
            float lat = CanalLat(x, z);
            h *= ThemeMaps.SmoothStepP(CanalHalfW + 1.5f, CanalHalfW + 7f, lat);
            h = Terrace(h);
            h = Mathf.Lerp(h, CanalY, 1f - ThemeMaps.SmoothStepP(CanalHalfW, CanalHalfW + 1.3f, lat));
            return h * ThemeMaps.MaskP(x, z);
        }

        public override bool SpotOk(Vector3 p)
        {
            float r = new Vector2(p.x, p.z).magnitude;
            return r > PitOut + 4f && CanalLat(p.x, p.z) > CanalHalfW + 3.5f;
        }

        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1.25f : kind == ResourceNode.Bush ? 0.8f : 0.7f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.95f, 0.6f, 0.78f), 0.85f);

        // ---------------------------------------------------------------- the ground (a hole cut out of it)
        int Vert(float x, float z, out Vector3 p)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r > IslandR && r < PitOut)
            {
                bool inner = r - IslandR < PitOut - r;
                float k = (inner ? IslandR : PitOut) / r;
                float sx = x * k, sz = z * k;
                p = new Vector3(sx, inner ? 0f : Height(sx * 1.003f, sz * 1.003f), sz);
                return inner ? 1 : 2;
            }
            p = new Vector3(x, Height(x, z), z);
            return 0;
        }

        static Color GroundColour(Vector3 c, float ny)
        {
            float r = new Vector2(c.x, c.z).magnitude;
            if (c.y < -0.45f && r > PitOut) return k_CanalBed;
            if (ny < 0.9f) return k_Dirt;
            float pn = ThemeMaps.SymNoiseP(c.x, c.z, 0.08f, ThemeMaps.SeedP + 30f);
            if (pn > 0.6f) return Color.Lerp(k_Petal, new Color(0.97f, 0.78f, 0.86f), (pn - 0.6f) * 4f);
            return Color.Lerp(k_Grass, k_Grass2, ThemeMaps.SymNoiseP(c.x, c.z, 0.035f, ThemeMaps.SeedP + 60f));
        }

        public override void BuildGround(Transform root)
        {
            m_Chunks.Clear();
            float ext = Mathf.Ceil((Half + 30f) / 2f) * 2f;
            ThemeKitB.Ground(root, "Ground", -ext, ext, 2f, Vert, GroundColour, HatchCell); // (the hatches down to the hollow)
            BuildPit(root);
        }

        /// <summary>The hole: terraced earth walls with a glowing rim, the island's rock pillar with roots under it, a dark floor.</summary>
        void BuildPit(Transform root)
        {
            const int K = 120;
            float pitOut = PitOut, isl = IslandR;
            float lip = Mathf.Clamp((pitOut - isl) * 0.05f, 0.35f, 1.4f);
            var rock = new MeshKit();
            var glow = new MeshKit();
            var deco = new MeshKit();
            Vector3 Dir(int s) { float a = s * Mathf.PI * 2f / K; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            float Jit(int s, int i) => (Mathf.PerlinNoise((s % K) * 0.41f + 3.1f, i * 1.7f + 0.3f) - 0.5f) * 0.7f;

            // ---- the outer wall (faces the middle), down in ledges
            var prof = new List<Vector2> { new Vector2(0, 0), new Vector2(0, -1f), new Vector2(0, -9f) };
            float y = -9f;
            for (int step = 1; step <= 4; step++)
            {
                prof.Add(new Vector2(lip * step, y));
                y -= step < 4 ? 7f : 0f;
                if (step < 4) prof.Add(new Vector2(lip * step, y));
            }
            prof.Add(new Vector2(lip * 4, PitFloor));
            prof.Add(new Vector2(lip * 4 + 0.6f, PitDeep)); // (on down into the void: no floor)
            var wallA = new Color(0.5f, 0.28f, 0.17f); var wallB = new Color(0.42f, 0.22f, 0.14f); var ledge = new Color(0.64f, 0.4f, 0.23f);
            var top = new float[K + 1];
            for (int s = 0; s <= K; s++) { var d = Dir(s); top[s] = Mathf.Min(0f, Height(d.x * (pitOut + 0.05f), d.z * (pitOut + 0.05f))); }
            Vector3 OuterP(int s, int i)
            {
                var d = Dir(s);
                float yy = i == 0 ? top[s] : i == 1 ? top[s] - 1f : prof[i].y;
                float rr = pitOut - prof[i].x - (i >= 2 ? Mathf.Max(0f, Jit(s, i)) : 0f);
                return d * rr + Vector3.up * yy;
            }
            for (int s = 0; s < K; s++)
            {
                var mid = Dir(s) + Dir(s + 1);
                for (int i = 0; i + 1 < prof.Count; i++)
                {
                    Vector3 a = OuterP(s, i), b = OuterP(s + 1, i), c = OuterP(s + 1, i + 1), d = OuterP(s, i + 1);
                    bool flat = Mathf.Abs(prof[i].y - prof[i + 1].y) < 0.01f && i > 0;
                    if (i == 0) { ThemeKitB.Quad(glow, a, b, c, d, k_Rim, -mid); continue; }
                    var col = flat ? ledge : (i % 4 < 2 ? wallA : wallB);
                    col *= 0.92f + 0.16f * Mathf.PerlinNoise(s * 0.3f, i * 0.9f);
                    // (darker and darker the deeper it goes, into the void)
                    col = Color.Lerp(col, new Color(0.05f, 0.03f, 0.06f), Mathf.Clamp01((-Mathf.Min(a.y, c.y) - 18f) / 50f));
                    col.a = 1f;
                    ThemeKitB.Quad(rock, a, b, c, d, col, flat ? Vector3.up : -mid);
                }
                // a warm glowing band on the grass round the rim
                if (top[s] > -0.3f && top[s + 1] > -0.3f)
                {
                    Vector3 i0 = Dir(s) * pitOut + Vector3.up * (top[s] + 0.03f), i1 = Dir(s + 1) * pitOut + Vector3.up * (top[s + 1] + 0.03f);
                    ThemeKitB.Quad(deco, i0, i1, i1 + Dir(s + 1) * 0.9f, i0 + Dir(s) * 0.9f, new Color(0.98f, 0.74f, 0.4f), Vector3.up);
                }
            }
            // ---- the island's pillar (faces out), narrowing to a point far down
            // (its rings 2 and 3 are the cave mouths' top and bottom: no jitter there, the mouths' edges have to meet the tunnels)
            var iprof = new[] { new Vector2(isl, 0f), new Vector2(isl, -1f), new Vector2(isl - 0.2f, MouthTop), new Vector2(isl - 0.35f, MouthBot),
                new Vector2(isl * 0.8f, -14f), new Vector2(isl * 0.62f, -22f), new Vector2(isl * 0.4f, -31f), new Vector2(isl * 0.2f, -40f), new Vector2(0.4f, -48f) };
            var pil = new[] { new Color(0.46f, 0.3f, 0.3f), new Color(0.38f, 0.25f, 0.27f), new Color(0.52f, 0.34f, 0.3f) };
            Vector3 IslP(int s, int i) => Dir(s) * (iprof[i].x + (i >= 4 ? Jit(s, i + 10) * 1.4f : 0f)) + Vector3.up * iprof[i].y;
            for (int s = 0; s < K; s++)
            {
                var mid = Dir(s) + Dir(s + 1);
                for (int i = 0; i + 1 < iprof.Length; i++)
                {
                    Vector3 a = IslP(s, i), b = IslP(s + 1, i), c = IslP(s + 1, i + 1), d = IslP(s, i + 1);
                    if (i == 0) { ThemeKitB.Quad(glow, a, b, c, d, k_Rim, mid); continue; }
                    if (i == 2 && MouthSeg(s)) continue; // (a cave mouth into the hollow)
                    ThemeKitB.Quad(rock, a, b, c, d, pil[(i + s / 7) % pil.Length], mid - Vector3.up * 0.3f);
                }
                Vector3 j0 = Dir(s) * isl + Vector3.up * 0.03f, j1 = Dir(s + 1) * isl + Vector3.up * 0.03f;
                ThemeKitB.Quad(deco, j0, j1, j1 - Dir(s + 1) * 0.9f, j0 - Dir(s) * 0.9f, new Color(0.98f, 0.74f, 0.4f), Vector3.up);
            }
            // ---- no floor: a black void far, far down (unlit, so it never shows a floor)
            var voidK = new MeshKit();
            for (int s = 0; s < K; s++)
                ThemeKitB.Tri(voidK, Vector3.up * PitDeep, Dir(s) * (pitOut + 2f) + Vector3.up * PitDeep, Dir(s + 1) * (pitOut + 2f) + Vector3.up * PitDeep, Color.white, Vector3.up);
            ThemeKitB.Spawn(root, "pit void", voidK, ThemeKitB.Glow(new Color(0.03f, 0.015f, 0.05f), 1f), false);
            // ---- roots hanging under the island's lip and down the outer wall
            var rng = new System.Random(Cfg.MapSeed + 811);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var rootCol = new Color(0.26f, 0.16f, 0.12f);
            for (int i = 0; i < 40; i++)
            {
                int seg = rng.Next(K);
                var d = Dir(seg); d = Quaternion.Euler(0, R(-1.5f, 1.5f), 0) * d;
                var a = d * (isl - R(0.2f, 0.9f)) + Vector3.up * R(-1.4f, -3.5f);
                var b = a + d * R(0.2f, 1.4f) + Vector3.down * R(2.5f, 7f);
                if (MouthSeg(seg) || MouthSeg(seg - 1) || MouthSeg(seg + 1)) continue; // (not hanging over a cave mouth)
                ThemeKitB.Cyl(deco, a, b, R(0.08f, 0.18f), 0.03f, 5, rootCol, false, false);
            }
            int wallRoots = Mathf.RoundToInt(pitOut * 1.6f);
            for (int i = 0; i < wallRoots; i++)
            {
                var d = Dir(rng.Next(K));
                var a = d * (pitOut + 0.05f) + Vector3.up * R(-1.1f, -2f);
                var b = d * (pitOut - R(0.3f, 0.9f)) + Vector3.up * (a.y - R(1.5f, 5f));
                ThemeKitB.Cyl(deco, a, b, R(0.06f, 0.14f), 0.03f, 5, rootCol, false, false);
            }
            ThemeKitB.Spawn(root, "pit walls", rock, null, true, true).AddComponent<GroundMarker>();
            ThemeKitB.Spawn(root, "pit rim glow", glow, ThemeKitB.Glow(k_Rim, 1.7f), false);
            ThemeKitB.Spawn(root, "pit dressing", deco, null, false);

            // ---- the canals' waterfalls into the hole
            var fall = new MeshKit();
            for (int k = 0; k < Cfg.Copies; k++)
            {
                var d = Cfg.Copy(CanalDir0, k);
                var side = new Vector3(d.z, 0, -d.x);
                for (int band = 0; band < 3; band++)
                {
                    float w = CanalHalfW - 0.2f - band * 0.35f, r0 = pitOut - 0.15f - band * 0.12f;
                    Vector3 a = d * r0 + side * w + Vector3.up * WaterTop, b = d * r0 - side * w + Vector3.up * WaterTop;
                    Vector3 c = d * (r0 - 0.8f - band * 0.4f) - side * w + Vector3.up * -28f, e = d * (r0 - 0.8f - band * 0.4f) + side * w + Vector3.up * -28f;
                    ThemeKitB.Quad(fall, a, b, c, e, Color.white, -d);
                }
            }
            ThemeKitB.Spawn(root, "waterfalls", fall, ThemeKitB.Glow(new Color(0.62f, 0.72f, 0.98f), 0.85f), false);
        }

        // ---------------------------------------------------------------- the floating chunks
        class Chunk
        {
            public Transform T;
            public Vector3 Pos, Drift;
            public Quaternion Rot;
            public float R, Phase, Sink, Kick, Amp, Spd, MPh;
            public byte Move; // 0 still, 1 bobbing up and down, 2 drifting from side to side
            public Vector3 Tilt;
            public bool On;
        }
        readonly List<Chunk> m_Chunks = new List<Chunk>();
        readonly List<Vector3> m_Feet = new List<Vector3>();
        readonly List<Vector2> m_RouteStarts = new List<Vector2>();

        /// <summary>A chunk of earth you can stand on (grass on top, roots under it). lower: one of the chunks down in the
        /// pit (darker, mossy, a few glowing mushrooms on it to light the way).</summary>
        Chunk MakeChunk(Transform parent, Vector3 pos, float yaw, float pr, float depth, int seed, bool lower)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var go = new GameObject(lower ? "parkour chunk (low)" : "parkour chunk");
            go.transform.SetParent(parent, false);
            var rot = Quaternion.Euler(0, yaw, 0);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            const int n = 9;
            var jr = new float[n];
            for (int i = 0; i < n; i++) jr[i] = R(0.9f, 1.08f);
            float[] ry = { 0f, -0.3f, -1.2f, -depth * 0.55f, -depth };
            float[] rs = { 1f, 1.04f, 0.88f, 0.52f, 0.12f };
            var grassTop = lower ? Color.Lerp(new Color(0.3f, 0.45f, 0.3f), new Color(0.55f, 0.42f, 0.55f), R(0f, 0.5f))
                                 : Color.Lerp(k_Grass, new Color(0.84f, 0.6f, 0.72f), R(0f, 0.4f));
            Color[] rc = { lower ? new Color(0.3f, 0.42f, 0.28f) : new Color(0.42f, 0.32f, 0.52f), new Color(0.44f, 0.32f, 0.36f), new Color(0.37f, 0.26f, 0.31f), new Color(0.31f, 0.21f, 0.26f) };
            Vector3 P(int ring, int i)
            {
                float a = (i % n) * Mathf.PI * 2f / n;
                float rr = pr * jr[i % n] * rs[ring];
                return new Vector3(Mathf.Cos(a) * rr, ry[ring], Mathf.Sin(a) * rr);
            }
            var kit = new MeshKit();
            var colKit = new MeshKit();
            var glow = new MeshKit();
            for (int i = 0; i < n; i++)
            {
                ThemeKitB.Tri(kit, new Vector3(0, 0.02f, 0), P(0, i), P(0, i + 1), grassTop, Vector3.up);
                ThemeKitB.Tri(colKit, new Vector3(0, 0.0f, 0), P(0, i), P(0, i + 1), grassTop, Vector3.up);
                for (int ring = 0; ring + 1 < ry.Length; ring++)
                {
                    var mid = P(0, i) + P(0, i + 1); mid.y = 0;
                    ThemeKitB.Quad(kit, P(ring, i), P(ring, i + 1), P(ring + 1, i + 1), P(ring + 1, i), rc[ring], mid);
                }
                ThemeKitB.Tri(kit, P(4, i), P(4, i + 1), new Vector3(0, -depth - 0.5f, 0), rc[3], Vector3.down);
                var m2 = P(0, i) + P(0, i + 1); m2.y = 0;
                ThemeKitB.Quad(colKit, P(0, i), P(0, i + 1), P(2, i + 1), P(2, i), Color.white, m2);
                ThemeKitB.Tri(colKit, P(2, i), P(2, i + 1), new Vector3(0, -depth, 0), Color.white, Vector3.down);
            }
            for (int i = 0; i < 6; i++)
            {
                var c = new Vector3(R(-0.6f, 0.6f), 0.04f, R(-0.6f, 0.6f)) * pr;
                float s = R(0.12f, 0.2f);
                ThemeKitB.Quad(kit, c + new Vector3(-s, 0, -s), c + new Vector3(s, 0, -s), c + new Vector3(s, 0, s), c + new Vector3(-s, 0, s), new Color(0.98f, 0.78f, 0.88f), Vector3.up);
            }
            int roots = 3 + rng.Next(4);
            for (int i = 0; i < roots; i++)
            {
                var a = P(2, rng.Next(n)) * 0.8f; a.y = -1.4f - R(0f, 1f);
                var b = a + new Vector3(R(-0.5f, 0.5f), -R(1.2f, 3.2f), R(-0.5f, 0.5f));
                ThemeKitB.Cyl(kit, a, b, R(0.06f, 0.12f), 0.025f, 5, new Color(0.24f, 0.15f, 0.12f), false, false);
            }
            if (lower)
            {
                // little glowing mushrooms at the edge (you can see the way down in the gloom)
                int shrooms = 2 + rng.Next(2);
                for (int i = 0; i < shrooms; i++)
                {
                    float a = R(0f, 6.28f), d = pr * R(0.62f, 0.82f);
                    var b = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    float hh = R(0.14f, 0.26f);
                    ThemeKitB.Cyl(kit, b, b + Vector3.up * hh, 0.035f, 0.03f, 5, new Color(0.9f, 0.86f, 0.8f), false, false);
                    ThemeKitB.Ball(glow, b + Vector3.up * hh, new Vector3(0.12f, 0.06f, 0.12f), Quaternion.identity, Color.white, 0);
                }
            }
            ThemeKitB.Spawn(go.transform, "chunk", kit, null, true);
            if (glow.Count > 0) ThemeKitB.Spawn(go.transform, "chunk glow", glow, ThemeKitB.Glow(new Color(0.55f, 0.95f, 0.85f), 2f), false);
            var mesh = colKit.ToMesh("chunk collider");
            go.AddComponent<OwnedMesh>().Mesh = mesh;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var ch = new Chunk { T = go.transform, Pos = pos, Rot = rot, R = pr, Phase = (seed & 1023) * 0.37f };
            m_Chunks.Add(ch);
            return ch;
        }

        // ---------------------------------------------------------------- the parkour routes (the chunks and the tree's doorways)
        class RouteStep { public Vector2 P, Drift; public float Top, R, Amp, Spd, Ph; public byte Move; }
        /// <summary>PorchSide: which way (along the porch, +1 = the doorway's right seen from inside) the porch is made longer -
        /// toward where the route's last chunk is; PorchExt: by how much (it never reaches over a chunk).</summary>
        class Route { public float Ang, DoorY, PorchSide = 1f, PorchExt; public bool Lower; public readonly List<RouteStep> Steps = new List<RouteStep>(); }
        static readonly List<Route> s_Routes = new List<Route>();
        static int s_RouteKey = int.MinValue;

        /// <summary>How much higher you can land on something this far away (edge to edge) - with a sprint (Cfg.SprintSpeed,
        /// JumpSpeed, Gravity): up to 2.4 m it's nearly a full jump up, 3.5-4 m is a long running jump (about level), and
        /// past 4 m you have to drop to make it. Each still leaves the best part of a metre spare: hard, never impossible.</summary>
        static float MaxRise(float gap) => gap <= 2.4f ? 0.95f : gap <= 3.0f ? 0.8f : gap <= 3.5f ? 0.55f : gap <= 4.0f ? 0.15f : gap <= 4.6f ? -0.7f : -99f;

        static Vector2 Rot2(Vector2 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
        static Vector2 Copy2(Vector2 p, int m) { var q = Cfg.Copy(new Vector3(p.x, 0, p.y), m); return new Vector2(q.x, q.z); }
        static Vector2 Dir2(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

        // ---- the pillar's cave mouths (where the low routes go in), on the pit's own 120 segments so the copies line up
        const int PitK = 120;
        /// <summary>One cave mouth per team, between two high routes (the low chunks under them lead to it).</summary>
        static float[] MouthOffs => Cfg.FourWay ? new[] { 0f } : new[] { 25f };
        static int MouthN => Mathf.CeilToInt(MouthW / (IslandR * 2f * Mathf.PI / PitK));
        static int MouthS0(float centreDeg) => Mathf.RoundToInt(Mathf.Repeat(centreDeg, 360f) / (360f / PitK) - MouthN * 0.5f);
        static float MouthCentreDeg(int s0) => (s0 + MouthN * 0.5f) * 360f / PitK;
        static float Th0Deg => Mathf.Atan2(Cfg.BaseCenter[0].z, Cfg.BaseCenter[0].x) * Mathf.Rad2Deg;
        /// <summary>Is segment s (of PitK round the pillar) a cave mouth (any team's)?</summary>
        static bool MouthSeg(int s)
        {
            foreach (float off in MouthOffs)
            {
                int s0 = MouthS0(Th0Deg + off);
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    int d = ((s - (s0 + m * PitK / Cfg.Copies)) % PitK + PitK) % PitK;
                    if (d < MouthN) return true;
                }
            }
            return false;
        }

        // ---- the hatches in the ball's floor (one per team, on its side), on the ground's 2 m grid: a cell's middle
        static List<Vector3> Hatches()
        {
            var list = new List<Vector3>();
            float th0 = Th0Deg * Mathf.Deg2Rad;
            var p = new Vector3(Mathf.Cos(th0), 0, Mathf.Sin(th0)) * 9.3f;
            p.x = Mathf.Floor(p.x / 2f) * 2f + 1f;
            p.z = Mathf.Floor(p.z / 2f) * 2f + 1f;
            for (int m = 0; m < Cfg.Copies; m++) list.Add(Cfg.Copy(p, m));
            return list;
        }
        static bool HatchCell(float x, float z)
        {
            foreach (var h in Hatches()) if (Mathf.Abs(x - h.x) < 0.5f && Mathf.Abs(z - h.z) < 0.5f) return true;
            return false;
        }
        /// <summary>Along the grid, the way from a hatch toward the middle of the island.</summary>
        static Vector3 HatchIn(Vector3 h) => Mathf.Abs(h.x) > Mathf.Abs(h.z) ? new Vector3(-Mathf.Sign(h.x), 0, 0) : new Vector3(0, 0, -Mathf.Sign(h.z));

        /// <summary>One route of chunks from `start` (on the rim) to the edge at `end` (a doorway's porch, a cave mouth's
        /// ledge): a mix of hops, jumps up onto higher chunks, long running jumps and long drops, zigzagging (and sometimes
        /// swinging right out sideways) inside its corridor, never too near another route's chunks. Some bob up and down
        /// or drift from side to side. Upper: climbing to endY (a little over, then wavering); lower: down to endY.</summary>
        static void Walk(Route rt, System.Random rng, Vector2 start, Vector2 end, float endY, bool lower, float corridor, List<(Vector2 p, float r)> avoid)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            bool small = Cfg.SmallMap;
            float pitOut = PitOut;
            var line = end - start;
            var lineDir = line.normalized;
            var lineSide = new Vector2(-lineDir.y, lineDir.x);
            float lineLen = line.magnitude;
            var pos = start;
            float prevR = 0f, y = 0f, sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
            for (int i = 0; i < 28; i++)
            {
                float gEnd = (end - pos).magnitude - prevR;
                if (i > 0 && gEnd <= 2.7f) break;
                // (smaller chunks than before: harder)
                float pr = lower ? (small ? R(1.2f, 1.5f) : R(1.15f, 1.55f)) : small ? R(1.0f, 1.3f) : R(0.95f, 1.3f);
                float stepsLeft = Mathf.Max(1f, gEnd / 3.7f);
                float want = (endY - y) / stepsLeft;
                float roll = (float)rng.NextDouble();
                int type; // 0 hop, 1 up onto a higher one, 2 long running jump, 3 long drop
                if (i == 0) type = lower ? 3 : 0;
                else if (lower) type = roll < 0.38f ? 0 : roll < 0.5f && y < -2.2f ? 1 : roll < 0.74f ? 2 : 3;
                // (high routes: mostly full jumps up and long running jumps, few easy hops)
                else type = roll < 0.2f ? 0 : roll < 0.52f ? 1 : roll < 0.84f ? 2 : (y > 1.8f && want < 0.4f ? 3 : 1);
                if (type == 1 && !lower && want < 0.2f) type = 2;
                if (!lower && i > 0 && want > 0.6f) type = roll < 0.7f ? 1 : 0; // (falling behind: climb)
                // (a long jump wants room for a run-up: a bit bigger chunk to take off from... and land on)
                if (type == 2) pr = lower ? (small ? R(1.35f, 1.6f) : R(1.4f, 1.7f)) : small ? R(1.15f, 1.4f) : R(1.15f, 1.45f);
                RouteStep best = null;
                // nearly there: one more chunk, about halfway to the end, then the last jump
                bool approach = i > 0 && gEnd - 2f * pr < 5.2f;
                for (int tries = 0; tries < 16 && best == null; tries++)
                {
                    float k = tries / 15f;
                    int ty = approach ? 0 : tries < 10 ? type : 0;
                    float g = lower ? (ty == 0 ? R(1.6f, 2.6f) : ty == 1 ? R(1.3f, 2.0f) : ty == 2 ? R(3.5f, 4.0f) : R(4.1f, 4.6f))
                                    : ty == 0 ? R(2.2f, 2.9f) : ty == 1 ? R(1.7f, 2.35f) : ty == 2 ? R(3.6f, 4.0f) : R(4.1f, 4.6f);
                    if (i == 0) g = lower ? R(1.3f, 2.2f) : R(1.5f, 2.3f);
                    if (approach) g = Mathf.Max(0.8f, (gEnd - 2f * pr) * R(0.38f, 0.62f));
                    float dy;
                    if (i == 0) dy = lower ? -R(2.2f, 3f) : R(0.3f, 0.85f);
                    else if (ty == 1) dy = MaxRise(g);
                    else if (ty == 3) dy = -R(0.8f, 1.5f);
                    else dy = Mathf.Clamp(want + R(-0.5f, 0.45f), -1.2f, MaxRise(g));
                    // stay in the band: upper up to a little over the doorway, lower between the rim and the ledge
                    if (!lower) dy = Mathf.Min(dy, endY + 0.4f - y);
                    else dy = Mathf.Clamp(dy, endY - 0.3f - y, -1.8f - y);
                    if (dy > MaxRise(g)) { g = Mathf.Min(g, 2.2f); dy = Mathf.Min(dy, MaxRise(g)); }
                    // which way: toward the end, swung out to one side and back (bigger swings on the short hops)
                    var toEnd = (end - pos).normalized;
                    float swing = (approach ? R(0f, 22f) : ty >= 2 ? R(0f, 30f) : R(15f, 62f)) * (1f - k * 0.7f);
                    var dir = Rot2(toEnd, swing * sgn);
                    var p = pos + dir * (prevR + g + pr);
                    // checks: inside the corridor and the pit, not past the end, clear of every other chunk
                    float lat = Vector2.Dot(p - start, lineSide);
                    if (Mathf.Abs(lat) > corridor - pr * 0.4f) { sgn = -Mathf.Sign(lat); continue; }
                    if (p.magnitude + pr > pitOut - 0.6f) continue;
                    if (p.magnitude - pr < IslandR + 0.8f) continue; // (not into the island's pillar)
                    if ((end - p).magnitude - pr < 0.8f) continue;
                    if (Vector2.Dot(p - start, lineDir) > lineLen) continue;
                    bool clash = false;
                    foreach (var a in avoid) if ((a.p - p).magnitude < a.r + pr + 1.3f) { clash = true; break; }
                    for (int j = 0; j + 1 < rt.Steps.Count && !clash; j++) if ((rt.Steps[j].P - p).magnitude < rt.Steps[j].R + pr + 1.0f) clash = true;
                    if (clash) continue;
                    best = new RouteStep { P = p, Top = y + dy, R = pr };
                    if (i > 0 && rng.NextDouble() < (lower ? 0.28 : 0.42))
                    {
                        if (rng.NextDouble() < 0.5) { best.Move = 1; best.Amp = R(0.4f, 0.6f); best.Spd = R(0.85f, 1.25f); }
                        else { best.Move = 2; best.Amp = R(0.9f, 1.5f); best.Spd = R(0.45f, 0.7f); best.Drift = new Vector2(-dir.y, dir.x); }
                        best.Ph = R(0f, 6.28f);
                        // (drifting: keep clear of the others at both ends of its swing)
                        if (best.Move == 2)
                            foreach (var a in avoid) if ((a.p - p).magnitude < a.r + pr + best.Amp + 1.3f) { best.Move = 0; break; }
                    }
                }
                if (best == null) break;
                rt.Steps.Add(best);
                pos = best.P; prevR = best.R; y = best.Top;
                if (rng.NextDouble() < 0.7) sgn = -sgn;
            }
            // still too far (boxed in)? straight on, in short hops
            for (int guard = 0; guard < 8; guard++)
            {
                float gEnd = (end - pos).magnitude - prevR;
                if (gEnd <= 3.0f) break;
                var toEnd = (end - pos).normalized;
                float pr = 1.25f, g = Mathf.Min(2.2f, (gEnd - 2f * pr) * 0.5f);
                if (g < 0.6f) { pr = Mathf.Max(0.8f, (gEnd - 1.2f) * 0.5f); g = 0.6f; }
                float dy = lower ? Mathf.Clamp((endY - y) * 0.5f, -1.5f, MaxRise(g)) : Mathf.Clamp((endY - y) * 0.6f, -1.2f, MaxRise(g));
                var p = pos + toEnd * (prevR + g + pr);
                rt.Steps.Add(new RouteStep { P = p, Top = y + dy, R = pr });
                pos = p; prevR = pr; y += dy;
            }
            if (rt.Steps.Count > 0) rt.Steps[rt.Steps.Count - 1].Move = 0; // (the last one, by the doorway / the ledge, keeps still)
            if (!lower)
            {
                float toPorch = Mathf.Max(0.3f, (end - pos).magnitude - prevR);
                rt.DoorY = Mathf.Min(DoorYWant, y + Mathf.Min(0.85f, MaxRise(toPorch)));
                // the porch is made longer on the side the last chunk is on (an easier landing off it) - as long as that
                // never reaches over any of the route's chunks
                var od = end.normalized;
                var right = new Vector2(od.y, -od.x);
                rt.PorchSide = Vector2.Dot(pos, right) < -0.3f ? -1f : 1f;
                float porchMid = TreeOut + PorchLen * 0.5f - 0.2f, porchHalfR = (PorchLen + 0.4f) * 0.5f;
                for (float ext = 1.5f; ext >= 0f; ext -= 0.25f)
                {
                    float lo = -(DoorW + 0.6f) * 0.5f, hi = (DoorW + 0.6f) * 0.5f;
                    if (rt.PorchSide > 0f) hi += ext; else lo -= ext;
                    bool clear = true;
                    foreach (var st in rt.Steps)
                    {
                        float a = Vector2.Dot(st.P, right), r = Vector2.Dot(st.P, od);
                        float dx = Mathf.Max(0f, Mathf.Max(lo - a, a - hi)), dr = Mathf.Max(0f, Mathf.Abs(r - porchMid) - porchHalfR);
                        if (Mathf.Sqrt(dx * dx + dr * dr) < st.R + (st.Move == 2 ? st.Amp : 0f) + 0.6f) { clear = false; break; }
                    }
                    if (clear) { rt.PorchExt = ext; break; }
                }
            }
            foreach (var s in rt.Steps)
                for (int m = 0; m < Cfg.Copies; m++) avoid.Add((Copy2(s.P, m), s.R + (s.Move == 2 ? s.Amp : 0f)));
        }

        /// <summary>The routes in the first team's part (copied round for the others). Upper: from the rim up to a doorway in
        /// the tree. Lower: from the rim down into the pit, to a cave mouth in the island's pillar (the hollow under the ball).</summary>
        static List<Route> Routes()
        {
            int key = Cfg.MapSeed * 7919 + (int)Cfg.Size * 131 + Cfg.TeamCount * 17 + (int)Cfg.BaseCenter[0].z;
            if (key == s_RouteKey && s_Routes.Count > 0) return s_Routes;
            s_RouteKey = key;
            s_Routes.Clear();
            var rng = new System.Random(Cfg.MapSeed * 7 + 7101);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float th0 = Th0Deg;
            float[] offs = Cfg.FourWay ? new[] { -22f, 22f } : new[] { -50f, 0f, 50f };
            float porchOut = TreeOut + PorchLen, pitOut = PitOut;
            var avoid = new List<(Vector2 p, float r)>();
            // the doorways' porches are kept clear
            foreach (float off in offs)
                for (int m = 0; m < Cfg.Copies; m++) avoid.Add((Copy2(Dir2(th0 + off) * (porchOut - 0.6f), m), 1.2f));
            // ...and the high routes keep out of the way in front of the cave mouths (the low routes come in there)
            var mouths = new List<(Vector2 p, float r)>();
            foreach (float off in MouthOffs)
                for (int m = 0; m < Cfg.Copies; m++) mouths.Add((Copy2(Dir2(MouthCentreDeg(MouthS0(th0 + off))) * (LedgeR + 3f), m), 2.4f));
            avoid.AddRange(mouths);
            foreach (float off in offs)
            {
                var rt = new Route { Ang = th0 + off };
                float sAng = rt.Ang + R(-12f, 12f);
                Walk(rt, rng, Dir2(sAng) * pitOut, Dir2(rt.Ang) * porchOut, DoorYWant, false, Cfg.FourWay ? 3.8f : 5f, avoid);
                s_Routes.Add(rt);
            }
            // the way down to the hollow: no route from the rim - two chunks low in the pit (per team), the first one only
            // reachable by dropping off a high route, the second a jump on from it, then a hop onto the cave mouth's ledge
            foreach (float off in MouthOffs)
            {
                float mAng = MouthCentreDeg(MouthS0(th0 + off));
                var rt = new Route { Ang = mAng, Lower = true };
                LowChunks(rt, rng, mAng);
                s_Routes.Add(rt);
            }
            return s_Routes;
        }

        /// <summary>The two low chunks in front of a cave mouth (at mAng): the second just off the ledge (a short hop down onto
        /// it), the first between it and the nearest high chunk - clear of every high chunk (nothing over it), a drop of
        /// 0.8-4 m sideways off that high chunk and a jump of 1.2-3.6 m (down a little) on to the second.</summary>
        static void LowChunks(Route rt, System.Random rng, float mAng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var md = Dir2(mAng);
            var ledge = md * LedgeR;
            float pr2 = R(1.2f, 1.4f), g2 = R(1.4f, 1.9f);
            var p2 = md * (LedgeR + g2 + pr2);
            float top2 = ChamberFloor + R(0.25f, 0.6f);
            // every high chunk (all the teams' copies), and the nearest one to drop off (one that keeps still if it can)
            var highs = new List<(Vector2 p, float r, float top, byte move)>();
            foreach (var up in s_Routes)
                if (!up.Lower)
                    foreach (var st in up.Steps)
                        for (int m = 0; m < Cfg.Copies; m++) highs.Add((Copy2(st.P, m), st.R + (st.Move == 2 ? st.Amp : 0f), st.Top, st.Move));
            int from = -1; float bestD = 1e9f;
            for (int i = 0; i < highs.Count; i++)
            {
                float d = (highs[i].p - p2).magnitude - highs[i].r - pr2 + (highs[i].move != 0 ? 3f : 0f);
                if (d < bestD) { bestD = d; from = i; }
            }
            float pr1 = R(1.15f, 1.35f);
            Vector2 p1 = (ledge + p2) * 0.5f;
            bool found = false;
            if (from >= 0)
            {
                var fp = highs[from].p; float fr = highs[from].r;
                float bestScore = 1e9f;
                for (int t = 0; t < 600; t++)
                {
                    var c = Vector2.Lerp(fp, p2, R(0.1f, 0.9f)) + new Vector2(R(-4f, 4f), R(-4f, 4f));
                    float g1 = (c - fp).magnitude - fr - pr1, gg = (p2 - c).magnitude - pr2 - pr1;
                    if (g1 < 0.8f || g1 > 4f || gg < 1.2f || gg > 3.6f) continue;
                    float r = c.magnitude;
                    if (r - pr1 < IslandR + 0.8f || r + pr1 > PitOut - 0.6f) continue;
                    if ((c - ledge).magnitude - pr1 < 1.6f) continue;
                    bool clash = false;
                    foreach (var h in highs) if ((h.p - c).magnitude < h.r + pr1 + 0.8f) { clash = true; break; }
                    if (clash) continue;
                    float score = Mathf.Abs(g1 - 2f) + Mathf.Abs(gg - 2.2f);
                    if (score < bestScore) { bestScore = score; p1 = c; found = true; }
                }
            }
            // (nothing fits: a jump out from the second one toward that high chunk - a long drop off it still gets you there)
            if (!found) p1 = p2 + (from >= 0 ? (highs[from].p - p2).normalized : Rot2(md, 90f)) * (pr1 + pr2 + 2.4f);
            float gap12 = (p2 - p1).magnitude - pr1 - pr2;
            float top1 = top2 + (gap12 > 3f ? R(0.9f, 1.4f) : R(0.5f, 1.1f));
            rt.Steps.Add(new RouteStep { P = p1, Top = top1, R = pr1 });
            rt.Steps.Add(new RouteStep { P = p2, Top = top2, R = pr2 });
        }

        void BuildChunks(Transform root)
        {
            m_Chunks.Clear();
            m_RouteStarts.Clear();
            var rng = new System.Random(Cfg.MapSeed * 7 + 7002);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float pitOut = PitOut;
            var parent = new GameObject("parkour").transform;
            parent.SetParent(root, false);
            foreach (var rt in Routes())
            {
                if (rt.Steps.Count > 0 && !rt.Lower)
                {
                    var s0 = rt.Steps[0].P.normalized * pitOut;
                    for (int m = 0; m < Cfg.Copies; m++) m_RouteStarts.Add(Copy2(s0, m));
                }
                foreach (var st in rt.Steps)
                {
                    float depth = rt.Lower ? R(2.4f, 4.2f) : R(3.2f, 5.6f) + Mathf.Max(0f, st.Top) * 0.35f;
                    int seed = rng.Next();
                    float yaw = R(0f, 360f);
                    for (int m = 0; m < Cfg.Copies; m++)
                    {
                        var c = MakeChunk(parent, Cfg.Copy(new Vector3(st.P.x, st.Top, st.P.y), m), yaw + m * 360f / Cfg.Copies, st.R, depth, seed, rt.Lower);
                        c.Move = st.Move; c.Amp = st.Amp; c.Spd = st.Spd; c.MPh = st.Ph;
                        c.Drift = Cfg.Copy(new Vector3(st.Drift.x, 0f, st.Drift.y), m);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- props
        public override void BuildProps(Transform root)
        {
            BuildChunks(root);
            var rng = new System.Random(Cfg.MapSeed + 4242);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var stone = new MeshKit();
            var glow = new MeshKit();
            var trees = new MeshKit();
            var cols = new GameObject("cherry colliders").transform;
            cols.SetParent(root, false);
            float pitOut = PitOut;
            float th0 = Mathf.Atan2(Cfg.BaseCenter[0].z, Cfg.BaseCenter[0].x) * Mathf.Rad2Deg;
            float span = 360f / Cfg.Copies;

            bool NearBase(Vector3 p, float pad)
            {
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    var c = Cfg.BaseCenter[t];
                    if (Mathf.Abs(p.x - c.x) < Cfg.BaseHalf + pad && Mathf.Abs(p.z - c.z) < Cfg.BaseHalf + pad) return true;
                }
                return false;
            }

            // ---- stone lanterns round the rim (not where a parkour route starts)
            float rr = pitOut + 2.4f;
            float stepDeg = 9.5f / rr * Mathf.Rad2Deg;
            for (float a = -span * 0.5f + stepDeg * 0.5f; a < span * 0.5f; a += stepDeg)
            {
                float ang = (th0 + a) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(ang) * rr, 0, Mathf.Sin(ang) * rr);
                bool nearRoute = false;
                foreach (var s in m_RouteStarts) if ((new Vector2(p.x, p.z) - s).magnitude < 5.5f) nearRoute = true;
                if (nearRoute || CanalLat(p.x, p.z) < CanalHalfW + 3f || NearBase(p, 1f)) continue;
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var q = Cfg.Copy(p, m);
                    q.y = Height(q.x, q.z);
                    Lantern(stone, glow, cols, q, ang * Mathf.Rad2Deg + m * span);
                }
            }

            // ---- the canals: water, stone edges, red bridges, lanterns and cherry trees arching over them
            var water = new Material(Art.Mat(new Color(0.16f, 0.17f, 0.34f))) { name = "canal water" };
            if (water.HasProperty("_Smoothness")) water.SetFloat("_Smoothness", 0.9f);
            float c0 = CanalStart, c1 = CanalEnd, len = c1 - c0;
            var red = new Color(0.78f, 0.17f, 0.14f);
            for (int k = 0; k < Cfg.Copies; k++)
            {
                var d = Cfg.Copy(CanalDir0, k);
                var side = new Vector3(d.z, 0, -d.x);
                var look = Quaternion.LookRotation(d);
                var midP = d * (c0 + len * 0.5f);
                var w = Art.Box(root, Color.white, midP + Vector3.up * (WaterTop - 0.05f), new Vector3(CanalHalfW * 2f + 0.4f, 0.1f, len), look.eulerAngles, false, water);
                w.name = "canal water";
                w.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    stone.Box(midP + side * sgn * (CanalHalfW + 0.3f) + Vector3.up * -0.5f, new Vector3(0.7f, 1.3f, len), look, k_Stone, MeshKit.All);
                // bridges
                foreach (float f in new[] { 0.36f, 0.76f })
                {
                    var bp = d * (c0 + len * f);
                    var across = Quaternion.LookRotation(side);
                    float bl = CanalHalfW * 2f + 3.4f;
                    stone.Box(bp + Vector3.up * 0.25f, new Vector3(2.6f, 0.3f, bl), across, new Color(0.62f, 0.2f, 0.16f), MeshKit.All);
                    ThemeKitB.BoxCol(cols, bp + Vector3.up * 0.25f, new Vector3(2.6f, 0.3f, bl), across);
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var rail = bp + d * sgn * 1.2f;
                        stone.Box(rail + Vector3.up * 1.05f, new Vector3(0.12f, 0.12f, bl), across, red, MeshKit.All);
                        stone.Box(rail + Vector3.up * 0.75f, new Vector3(0.08f, 0.08f, bl), across, red, MeshKit.All);
                        for (int post = -1; post <= 1; post++)
                            stone.Box(rail + side * post * (bl * 0.5f - 0.2f) + Vector3.up * 0.65f, new Vector3(0.18f, 0.9f, 0.18f), across, red, MeshKit.All);
                        ThemeKitB.BoxCol(cols, rail + Vector3.up * 0.75f, new Vector3(0.2f, 0.9f, bl), across);
                    }
                }
                // lanterns and trees along both banks
                int seedBase = 900;
                for (float a = c0 + 7f; a < c1 - 4f; a += 9f)
                {
                    seedBase++;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var tp = d * a + side * sgn * (CanalHalfW + 3.6f);
                        if (NearBase(tp, 3f)) continue;
                        tp.y = Height(tp.x, tp.z);
                        float h = 5f + (seedBase * 7 % 10) * 0.2f;
                        // (leaning over the water: the tree's +x points at the canal)
                        var rot = Quaternion.LookRotation(Vector3.Cross(-side * sgn, Vector3.up), Vector3.up);
                        CherryTree(trees, tp, rot, seedBase * 31 + (sgn > 0 ? 1 : 0) + k * 0, h, 2.6f, 1.15f, 9f);
                        ThemeKitB.CapCol(cols, tp, tp + Vector3.up * h * 0.6f, 0.4f);
                        var lp = d * (a + 4.5f) + side * sgn * (CanalHalfW + 1.5f);
                        if (a + 4.5f < c1 - 3f && !NearBase(lp, 2f)) { lp.y = Height(lp.x, lp.z); Lantern(stone, glow, cols, lp, k * span); }
                    }
                }
            }
            ThemeKitB.Spawn(root, "stone and bridges", stone, null, true);
            ThemeKitB.Spawn(root, "lantern light", glow, ThemeKitB.Glow(new Color(1f, 0.72f, 0.38f), 2.4f), false);
            ThemeKitB.Spawn(root, "canal cherry trees", trees, null, true);

            // ---- petals everywhere on the ground (and floating on the canals)
            var pk = new MeshKit();
            int petals = Mathf.RoundToInt(3600 * (Half / 100f) * (Half / 100f));
            Color[] pc = { new Color(0.98f, 0.76f, 0.86f), new Color(0.95f, 0.64f, 0.8f), new Color(1f, 0.88f, 0.93f), new Color(0.9f, 0.55f, 0.72f) };
            for (int i = 0; i < petals; i++)
            {
                float x = R(-Half + 2f, Half - 2f), z = R(-Half + 2f, Half - 2f);
                float r = Mathf.Sqrt(x * x + z * z);
                if (r < 13.5f || (r > IslandR - 0.6f && r < pitOut + 0.4f)) continue;
                var p = new Vector3(x, 0, z);
                if (NearBase(p, 0.5f)) continue;
                float y = Height(x, z);
                y = y < -0.5f ? WaterTop + 0.03f : y + 0.06f;
                float s = R(0.13f, 0.24f), yaw = R(0f, 6.28f);
                var u = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * s; var v = new Vector3(-u.z, 0, u.x) * 0.7f;
                p.y = y;
                ThemeKitB.Quad(pk, p - u - v, p + u - v, p + u + v, p - u + v, pc[rng.Next(pc.Length)], Vector3.up);
            }
            ThemeKitB.Spawn(root, "petals", pk, null, false);

            // ---- the sky: a big moon and a few stars (they keep their place in the sky round the camera), petals drifting down
            m_Sky = new GameObject("cherry sky").transform;
            m_Sky.SetParent(root, false);
            var moonDir = new Vector3(-0.42f, 0.36f, 0.83f).normalized;
            var moon = Art.Part(m_Sky, Art.Sphere, Color.white, moonDir * 950f, Vector3.one * 64f, default, false, ThemeKitB.Glow(new Color(0.97f, 0.94f, 0.86f), 1.15f), "moon");
            moon.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var halo = Art.Part(m_Sky, Art.Sphere, Color.white, moonDir * 990f, Vector3.one * 110f, default, false, ThemeKitB.Glow(new Color(0.42f, 0.45f, 0.78f), 0.75f), "moon halo");
            halo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var stars = new MeshBatch { Colored = true };
            var srng = new System.Random(77);
            for (int i = 0; i < 260; i++)
            {
                Vector3 dir;
                do dir = new Vector3((float)srng.NextDouble() * 2 - 1, (float)srng.NextDouble(), (float)srng.NextDouble() * 2 - 1); while (dir.sqrMagnitude > 1f || dir.normalized.y < 0.3f);
                dir.Normalize();
                ThemeKitB.Star(stars, dir * 1050f, (float)srng.NextDouble() * 1.8f + 1.2f, new Color(0.9f, 0.9f, 1f).linear * ((float)srng.NextDouble() * 0.5f + 0.5f), (float)srng.NextDouble());
            }
            stars.Build(m_Sky, "stars", ThemeKitB.SkyMat(Color.white, CullMode.Off), false);
            m_Drift = new PetalDrift(root, 180);
        }

        Transform m_Sky;
        PetalDrift m_Drift;

        /// <summary>A Japanese stone lantern (its light box glows).</summary>
        static void Lantern(MeshKit stone, MeshKit glow, Transform cols, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector3 W(float x, float y, float z) => p + rot * new Vector3(x, y, z);
            var s = new Color(0.64f, 0.62f, 0.6f); var sd = new Color(0.48f, 0.46f, 0.45f);
            stone.Box(W(0, 0.12f, 0), new Vector3(0.95f, 0.3f, 0.95f), rot, sd, MeshKit.All);
            stone.Box(W(0, 0.75f, 0), new Vector3(0.34f, 1.0f, 0.34f), rot, s, MeshKit.All);
            stone.Box(W(0, 1.31f, 0), new Vector3(0.82f, 0.13f, 0.82f), rot, sd, MeshKit.All);
            for (int cx = -1; cx <= 1; cx += 2)
            for (int cz = -1; cz <= 1; cz += 2)
                stone.Box(W(cx * 0.3f, 1.63f, cz * 0.3f), new Vector3(0.12f, 0.52f, 0.12f), rot, s, MeshKit.All);
            glow.Box(W(0, 1.63f, 0), new Vector3(0.46f, 0.42f, 0.46f), rot, Color.white, MeshKit.All);
            stone.Box(W(0, 1.92f, 0), new Vector3(1.05f, 0.08f, 1.05f), rot, sd, MeshKit.All);
            stone.Cone(W(0, 1.95f, 0), 0.78f, 0.48f, 4, (yaw + 45f) * Mathf.Deg2Rad, s, s);
            stone.Box(W(0, 2.48f, 0), new Vector3(0.16f, 0.16f, 0.16f), rot, sd, MeshKit.All);
            ThemeKitB.BoxCol(cols, W(0, 1.2f, 0), new Vector3(0.8f, 2.4f, 0.8f), rot);
        }

        static readonly Color[] k_Pinks = { new Color(0.97f, 0.72f, 0.84f), new Color(0.93f, 0.58f, 0.76f), new Color(0.99f, 0.84f, 0.9f), new Color(0.88f, 0.5f, 0.7f) };
        static readonly Color k_Under = new Color(0.78f, 0.42f, 0.6f);

        /// <summary>The trunk's radius where the X goes (it's straight and round from the ground to 3 m).</summary>
        const float TrunkR = 0.34f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        /// <summary>A blocky cherry tree (a dark trunk - round and dead straight up to 3 m, where the X goes - square branches
        /// above that, stacked pink canopy blocks, a few hanging bits) into kit at `at`, turned by rot; reach: an extra long
        /// branch out along +x (arching over a canal), lean: the tree leans that many degrees toward +x.</summary>
        static void CherryTree(MeshKit k, Vector3 at, Quaternion rot, int seed, float h, float reach, float scale, float lean)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var bark = Color.Lerp(new Color(0.22f, 0.11f, 0.13f), new Color(0.31f, 0.16f, 0.17f), R(0f, 1f));
            var pinks = k_Pinks;
            var under = new Color(0.76f, 0.4f, 0.57f);
            var leanQ = Quaternion.Euler(0, 0, -lean);
            Vector3 W(Vector3 local) => at + rot * (leanQ * local) * scale;
            Quaternion Q(Quaternion local) => rot * leanQ * local;
            // the trunk: round and straight to 3 m, then on up (a little jog) to where the branches start
            const float Straight = 3f;
            float th = Mathf.Max(Straight + 0.4f, h * 0.62f);
            var jog = new Vector3(R(-0.25f, 0.25f), 0, R(-0.25f, 0.25f));
            float spin = R(0f, 6.28f);
            ThemeKitB.Cyl(k, W(Vector3.zero), W(Vector3.up * Straight), TrunkR * scale, TrunkR * scale, 16, bark, true, false, spin);
            var T = jog + Vector3.up * th;
            ThemeKitB.Cyl(k, W(Vector3.up * Straight), W(T + Vector3.up * 0.2f), TrunkR * scale, TrunkR * 0.88f * scale, 16, bark, false, true, spin);
            // (blossom a quarter bigger than it was)
            const float Big = 1.25f;
            void Blob(Vector3 c, float size)
            {
                var col = pinks[rng.Next(pinks.Length)];
                float sx = R(2.8f, 3.8f) * size * Big, sz = R(2.6f, 3.6f) * size * Big, sy = R(1.0f, 1.35f) * 1.2f;
                var q = Q(Quaternion.Euler(0, R(0f, 90f), 0));
                k.Box(W(c), new Vector3(sx, sy, sz) * scale, q, col, MeshKit.All);
                k.Box(W(c + Vector3.down * (sy * 0.5f + 0.02f)), new Vector3(sx * 0.92f, 0.05f, sz * 0.92f) * scale, q, under, MeshKit.All);
                k.Box(W(c + new Vector3(R(-0.4f, 0.4f), sy * 0.5f + 0.48f, R(-0.4f, 0.4f))), new Vector3(sx * 0.66f, 1.0f, sz * 0.66f) * scale, q, pinks[rng.Next(pinks.Length)], MeshKit.All);
                if (rng.NextDouble() < 0.6) k.Box(W(c + new Vector3(R(-0.3f, 0.3f), sy * 0.5f + 1.3f, R(-0.3f, 0.3f))), new Vector3(sx * 0.36f, 0.7f, sz * 0.36f) * scale, q, pinks[rng.Next(pinks.Length)], MeshKit.All);
                int hang = 2 + rng.Next(3);
                for (int i = 0; i < hang; i++)
                {
                    var hp = c + new Vector3(R(-sx, sx) * 0.42f, -sy * 0.5f - 0.35f, R(-sz, sz) * 0.42f);
                    k.Box(W(hp), new Vector3(0.6f, R(0.5f, 0.9f), 0.6f) * scale, q, pinks[rng.Next(pinks.Length)], MeshKit.All);
                }
            }
            int nb = 2 + rng.Next(2);
            float bspin = R(0f, 360f);
            for (int b = 0; b < nb + (reach > 0f ? 1 : 0); b++)
            {
                bool longB = b == nb;
                float ang = longB ? 0f : bspin + b * 360f / nb + R(-25f, 25f);
                float outR = longB ? reach + 1.6f : R(1.4f, 2.4f);
                var dir = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0, Mathf.Sin(ang * Mathf.Deg2Rad));
                var end = T + dir * outR + Vector3.up * (longB ? R(1.2f, 1.8f) : R(1.0f, 1.8f));
                var mid = (T + end) * 0.5f;
                k.Box(W(mid), new Vector3(0.42f, 0.42f, (end - T).magnitude + 0.3f) * scale, Q(Quaternion.LookRotation(end - T)), bark, MeshKit.All);
                Blob(end + Vector3.up * 0.6f, longB ? 1.15f : 1f);
            }
            Blob(T + Vector3.up * (1.8f + R(0f, 0.6f)), 1.2f);
        }

        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var k = new MeshKit();
            CherryTree(k, Vector3.zero, Quaternion.identity, seed + 5, h, 0f, 1f, 0f);
            // a carpet of fallen petals round its foot
            var rng = new System.Random(seed + 9);
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rng.NextDouble() * 6.28f, d = 0.5f + (float)rng.NextDouble() * 2.8f, s = 0.12f + (float)rng.NextDouble() * 0.12f;
                var c = new Vector3(Mathf.Cos(a) * d, 0.16f, Mathf.Sin(a) * d);
                ThemeKitB.Quad(k, c + new Vector3(-s, 0, -s), c + new Vector3(s, 0, -s), c + new Vector3(s, 0, s), c + new Vector3(-s, 0, s), new Color(0.98f, 0.76f, 0.87f), Vector3.up);
            }
            ThemeKitB.Spawn(tr, "cherry", k, null, true);
            return true;
        }

        // ---------------------------------------------------------------- the hollow cherry tree in the middle
        /// <summary>A giant hollow cherry tree on the island: a round wall of bark with a doorway (and a porch) where every
        /// high route arrives, a ramp of root down the inside from each doorway to the floor, a pink canopy of round blossom
        /// clouds over it all (open in the middle and not solid, so the ball drops straight in), paper lanterns hanging
        /// inside, blossom hedges round the ball on the floor (cover) - and under the floor, the hollow (BuildHollow).</summary>
        public override bool BuildCentre(Transform root)
        {
            var routes = Routes();
            var tree = new GameObject("hollow cherry tree").transform;
            tree.SetParent(root, false);
            var cols = new GameObject("tree colliders").transform;
            cols.SetParent(tree, false);
            var k = new MeshKit(); var canopy = new MeshKit(); var glow = new MeshKit();
            var rng = new System.Random(Cfg.MapSeed + 5151);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float span = 360f / Cfg.Copies;
            float th0 = Th0Deg;
            float rOut = TreeOut, rIn = TreeIn, rMid = (rOut + rIn) * 0.5f, wallH = WallH;
            var barkA = new Color(0.27f, 0.14f, 0.15f); var barkB = new Color(0.34f, 0.18f, 0.17f); var barkC = new Color(0.22f, 0.11f, 0.12f);
            var pinks = k_Pinks;
            Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            Quaternion Face(float deg) => Quaternion.LookRotation(Dir(deg), Vector3.up); // (z out from the middle, x round the ring)

            // ---- the doorways, round the ring in order
            var doors = new List<(float ang, float y, float side, float ext)>();
            foreach (var rt in routes)
                if (!rt.Lower)
                    for (int m = 0; m < Cfg.Copies; m++) doors.Add((Mathf.Repeat(rt.Ang + m * span, 360f), rt.DoorY, rt.PorchSide, rt.PorchExt));
            doors.Sort((x, y) => x.ang.CompareTo(y.ang));
            float doorHalf = DoorW * 0.5f / rMid * Mathf.Rad2Deg;

            // ---- the trunk's wall: round pieces (curved, smooth) round the ring, a box collider each; a doorway's piece is
            // a sill under it and a lintel over it
            void Ring(float a0, float a1, float y0, float y1, Color col)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 4f));
                for (int j = 0; j < n; j++)
                {
                    float b0 = Mathf.Lerp(a0, a1, j / (float)n), b1 = Mathf.Lerp(a0, a1, (j + 1f) / n);
                    Vector3 o0 = Dir(b0), o1 = Dir(b1), mid = o0 + o1;
                    var c = col * (0.94f + 0.1f * Mathf.PerlinNoise(b0 * 0.11f, y1 * 0.3f)); c.a = 1f;
                    ThemeKitB.Quad(k, o0 * rOut + Vector3.up * y0, o1 * rOut + Vector3.up * y0, o1 * rOut + Vector3.up * y1, o0 * rOut + Vector3.up * y1, c, mid);
                    ThemeKitB.Quad(k, o0 * rIn + Vector3.up * y0, o1 * rIn + Vector3.up * y0, o1 * rIn + Vector3.up * y1, o0 * rIn + Vector3.up * y1, c * 0.85f, -mid);
                    ThemeKitB.Quad(k, o0 * rIn + Vector3.up * y1, o1 * rIn + Vector3.up * y1, o1 * rOut + Vector3.up * y1, o0 * rOut + Vector3.up * y1, c, Vector3.up);
                    ThemeKitB.Quad(k, o0 * rIn + Vector3.up * y0, o1 * rIn + Vector3.up * y0, o1 * rOut + Vector3.up * y0, o0 * rOut + Vector3.up * y0, c, Vector3.down);
                }
                // its ends (the doorways' sides)
                foreach (float e in new[] { a0, a1 })
                {
                    var o = Dir(e); var tng = Vector3.Cross(Vector3.up, o) * (e == a0 ? 1f : -1f);
                    ThemeKitB.Quad(k, o * rIn + Vector3.up * y0, o * rOut + Vector3.up * y0, o * rOut + Vector3.up * y1, o * rIn + Vector3.up * y1, col * 0.8f, tng);
                }
            }
            void Piece(float a0, float a1, float top, float doorY)
            {
                float am = (a0 + a1) * 0.5f, half = (a1 - a0) * 0.5f * Mathf.Deg2Rad;
                float len = 2f * rOut * Mathf.Sin(half) + 0.35f;
                var rot = Face(am);
                var c = Dir(am) * rMid * Mathf.Cos(half);
                var col = Color.Lerp(barkA, barkB, R(0f, 1f));
                void Part(float y0, float y1)
                {
                    if (y1 - y0 < 0.05f) return;
                    Ring(a0, a1, y0, y1, col);
                    ThemeKitB.BoxCol(cols, c + Vector3.up * ((y0 + y1) * 0.5f), new Vector3(len, y1 - y0, TreeWall), rot);
                }
                if (doorY < 0f) Part(-0.5f, top);
                else { Part(-0.5f, doorY); Part(doorY + DoorH, top); }
                // bark ridges up the outside: round, twisting a little (just to look at)
                int ridges = doorY < 0f ? 2 : 0;
                for (int i = 0; i < ridges; i++)
                {
                    float ra = Mathf.Lerp(a0, a1, R(0.15f, 0.85f)), tw = R(-3f, 3f);
                    float h = top * R(0.55f, 1.0f);
                    ThemeKitB.Cyl(k, Dir(ra) * (rOut + 0.05f) + Vector3.down * 0.4f, Dir(ra + tw) * (rOut + 0.05f) + Vector3.up * h, R(0.28f, 0.42f), 0.12f, 6, Color.Lerp(barkC, barkA, R(0f, 0.5f)), false, true);
                }
            }
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors[i];
                var next = doors[(i + 1) % doors.Count];
                float a0 = d.ang + doorHalf, a1 = next.ang - doorHalf;
                if (i == doors.Count - 1) a1 += 360f;
                Piece(d.ang - doorHalf, d.ang + doorHalf, wallH + R(-0.6f, 0.8f), d.y);
                int n = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) * Mathf.Deg2Rad * rMid / 2.2f));
                for (int j = 0; j < n; j++)
                    Piece(Mathf.Lerp(a0, a1, j / (float)n), Mathf.Lerp(a0, a1, (j + 1f) / n), wallH + R(-1.4f, 1.4f), -1f);
            }

            // ---- each doorway: a frame of roots, a porch out over the hole, a landing inside and a ramp of root down to the floor
            const float rampSlope = 0.5f, rampW = 2.4f, rampT = 0.8f, LandExt = 1.5f;
            float rRamp = rIn - rampW * 0.5f;
            foreach (var d in doors)
            {
                var o = Dir(d.ang); var rot = Face(d.ang);
                var tan = rot * Vector3.right;
                // (longer on the side the route's last chunk is on: an easier landing)
                var porchC = o * (rOut + PorchLen * 0.5f - 0.2f) + tan * (d.side * d.ext * 0.5f) + Vector3.up * (d.y - 0.3f);
                var porchS = new Vector3(DoorW + 0.6f + d.ext, 0.6f, PorchLen + 0.4f);
                k.Box(porchC, porchS, rot, barkB, MeshKit.All);
                ThemeKitB.BoxCol(cols, porchC, porchS, rot);
                for (int s = -1; s <= 1; s += 2)
                    ThemeKitB.Cyl(k, o * (rOut + PorchLen - 0.3f) + tan * s * (DoorW * 0.4f) + Vector3.up * (d.y - 0.5f),
                        o * (rOut - 0.2f) + tan * s * (DoorW * 0.55f) + Vector3.up * (d.y - 4.5f), 0.32f, 0.18f, 7, barkC, false, false);
                // the frame: two round root posts and an arched root over the doorway, blossom on it
                for (int s = -1; s <= 1; s += 2)
                    ThemeKitB.Cyl(k, o * (rOut + 0.15f) + tan * s * (DoorW * 0.5f + 0.15f) + Vector3.up * (d.y - 0.1f),
                        o * (rOut + 0.15f) + tan * s * (DoorW * 0.5f + 0.05f) + Vector3.up * (d.y + DoorH + 0.2f), 0.3f, 0.24f, 7, barkC, false, true);
                var arcPrev = o * (rOut + 0.15f) - tan * (DoorW * 0.5f + 0.05f) + Vector3.up * (d.y + DoorH + 0.2f);
                for (int j = 1; j <= 6; j++)
                {
                    float a = j / 6f * Mathf.PI;
                    var p = o * (rOut + 0.15f) - tan * Mathf.Cos(a) * (DoorW * 0.5f + 0.05f) + Vector3.up * (d.y + DoorH + 0.2f + Mathf.Sin(a) * 0.55f);
                    ThemeKitB.Cyl(k, arcPrev, p, 0.25f, 0.25f, 7, barkC, false, false);
                    ThemeKitB.Ball(k, p, Vector3.one * 0.25f, Quaternion.identity, barkC, 0);
                    arcPrev = p;
                }
                for (int b = 0; b < 5; b++)
                    ThemeKitB.Ball(canopy, o * (rOut + 0.4f) + tan * R(-DoorW * 0.6f, DoorW * 0.6f) + Vector3.up * (d.y + DoorH + R(0.5f, 0.95f)),
                        new Vector3(R(0.45f, 0.7f), R(0.35f, 0.5f), R(0.45f, 0.6f)), rot, pinks[rng.Next(pinks.Length)], 0);
                // a round paper lantern hanging in the doorway (it marks the way in)
                ThemeKitB.Ball(glow, o * (rOut + 0.3f) + Vector3.up * (d.y + DoorH - 0.45f), new Vector3(0.22f, 0.28f, 0.22f), rot, Color.white, 1);
                if (d.y < 0.6f) continue;
                // a landing inside, as wide as the doorway and LandExt more to its left (seen from inside, facing the doorway),
                // where the ramp goes down from - its top end is that much further round
                var landC = o * (rIn - rampW * 0.5f + 0.1f) - tan * (LandExt * 0.5f) + Vector3.up * (d.y - rampT * 0.5f);
                var landS = new Vector3(DoorW + 0.4f + LandExt, rampT, rampW + 0.2f);
                k.Box(landC, landS, rot, barkB, MeshKit.All);
                ThemeKitB.BoxCol(cols, landC, landS, rot);
                // the ramp: round the inside of the trunk (anticlockwise), down to the floor
                float startArc = DoorW * 0.5f + 0.2f + LandExt, rampLen = d.y / rampSlope;
                int pieces = Mathf.Max(1, Mathf.CeilToInt(rampLen / 1.2f));
                for (int j = 0; j < pieces; j++)
                {
                    float s0 = rampLen * j / pieces, s1 = rampLen * (j + 1f) / pieces;
                    float ang0 = d.ang + (startArc + s0) / rRamp * Mathf.Rad2Deg - (j == 0 ? 0.6f : 0f), ang1 = d.ang + (startArc + s1) / rRamp * Mathf.Rad2Deg;
                    var p0 = Dir(ang0) * rRamp + Vector3.up * (d.y - s0 * rampSlope);
                    var p1 = Dir(ang1) * rRamp + Vector3.up * (d.y - s1 * rampSlope);
                    var along = p1 - p0;
                    var q = Quaternion.LookRotation(along, Vector3.up);
                    var c = (p0 + p1) * 0.5f - q * Vector3.up * (rampT * 0.5f);
                    var size = new Vector3(rampW, rampT, along.magnitude + 0.12f);
                    k.Box(c, size, q, j % 2 == 0 ? barkA : barkB, MeshKit.All);
                    ThemeKitB.BoxCol(cols, c, size, q);
                }
            }

            // ---- roots flaring out at the foot of the trunk, down over the island's edge (not over a cave mouth)
            for (int i = 0; i < 26; i++)
            {
                float a = R(0f, 360f);
                bool nearDoor = false;
                foreach (var d in doors) if (Mathf.Abs(Mathf.DeltaAngle(a, d.ang)) < doorHalf + 4f) nearDoor = true;
                if (nearDoor || MouthSeg(Mathf.FloorToInt(Mathf.Repeat(a, 360f) / (360f / PitK)))) continue;
                var o = Dir(a);
                ThemeKitB.Cyl(k, o * (rOut - 0.2f) + Vector3.up * R(1.2f, 2.6f), o * (IslandR + R(0.3f, 0.9f)) + Vector3.up * R(-2.6f, -1.2f), R(0.45f, 0.7f), 0.15f, 7, barkC, false, false);
            }

            // ---- the canopy: big branches out of the top of the trunk, round blossom clouds over them (open in the middle)
            int branches = Mathf.RoundToInt(rMid * 0.8f);
            for (int i = 0; i < branches; i++)
            {
                float a = i * 360f / branches + R(-8f, 8f);
                var o = Dir(a);
                var from = o * rMid + Vector3.up * (wallH - 0.8f);
                bool inward = i % 3 == 0;
                var to = o * (inward ? R(8.5f, 10.5f) : rMid + R(2.5f, 6f)) + Vector3.up * (wallH + R(2.5f, 4.5f));
                var mid = Vector3.Lerp(from, to, 0.5f) + Vector3.up * 1.2f;
                ThemeKitB.Cyl(k, from, mid, 0.75f, 0.58f, 8, barkA, false, false);
                ThemeKitB.Ball(k, mid, Vector3.one * 0.58f, Quaternion.identity, barkA, 0);
                ThemeKitB.Cyl(k, mid, to, 0.58f, 0.38f, 8, barkA, false, true);
                ThemeKitB.Blossom(canopy, to + Vector3.up * 0.8f, (inward ? 1.5f : 1.7f), rng, pinks, k_Under, !inward);
            }
            int ring = Mathf.RoundToInt(rMid * 1.3f);
            for (int i = 0; i < ring; i++)
            {
                float a = i * 360f / ring + R(-6f, 6f), rr = R(9f, rOut + 6f);
                float y = wallH + 2.2f + 3.2f * Mathf.Clamp01(1f - Mathf.Abs(rr - rMid) / 9f) + R(-0.6f, 1f);
                ThemeKitB.Blossom(canopy, Dir(a) * rr + Vector3.up * y, R(1.3f, 1.75f), rng, pinks, k_Under, rr > rMid + 2f);
            }
            // round paper lanterns hanging inside (warm light in the shade of the canopy)
            int lanterns = Mathf.Max(6, Cfg.Copies * 3);
            var lid = new Color(0.12f, 0.1f, 0.1f);
            for (int i = 0; i < lanterns; i++)
            {
                float a = th0 + (i + 0.5f) * 360f / lanterns;
                var at = Dir(a) * R(8.5f, 10.5f) + Vector3.up * (wallH + R(-1.2f, 0.4f));
                float drop = R(1.5f, 3f);
                var lq = Quaternion.Euler(0, a, 0);
                k.Box(at + Vector3.up * (drop * 0.5f + 0.4f), new Vector3(0.04f, drop, 0.04f), Quaternion.identity, barkC, MeshKit.All);
                ThemeKitB.Ball(glow, at, new Vector3(0.36f, 0.44f, 0.36f), lq, Color.white, 1);
                ThemeKitB.Cyl(k, at + Vector3.up * 0.36f, at + Vector3.up * 0.46f, 0.22f, 0.18f, 8, lid);
                ThemeKitB.Cyl(k, at - Vector3.up * 0.46f, at - Vector3.up * 0.36f, 0.18f, 0.22f, 8, lid);
            }

            // ---- blossom hedges round the ball (cover), the same in every team's part (clear of the hatch, out on its side)
            float hr = Mathf.Min(10f, rIn - 4.2f);
            var layout = new List<(float a, float r, float len, bool radial)>
            {
                (0f, 4.7f, 3.6f, false),
                (-span * 0.3f, hr, 3.2f, true),
                (span * 0.3f, hr, 3.2f, true),
                (span * 0.5f, Mathf.Min(9.4f, hr), 3.8f, false),
            };
            var leaf = new Color(0.27f, 0.45f, 0.26f); var leaf2 = new Color(0.32f, 0.52f, 0.3f);
            for (int m = 0; m < Cfg.Copies; m++)
                for (int hi = 0; hi < layout.Count; hi++)
                {
                    var h = layout[hi];
                    float a = th0 + m * span + h.a;
                    var c = Dir(a) * h.r;
                    var rot = h.radial ? Face(a) * Quaternion.Euler(0, 90f, 0) : Face(a);
                    var size = new Vector3(h.len, 1.7f, 1.1f);
                    k.Box(c + Vector3.up * 0.85f, size, rot, (hi & 1) == 0 ? leaf : leaf2, MeshKit.All);
                    ThemeKitB.BoxCol(cols, c + Vector3.up * 0.85f, size, rot);
                    var hr2 = new System.Random(Cfg.MapSeed + 77 + hi * 31); // (the same blossom on every team's copy)
                    float H(float lo, float hi2) => lo + (float)hr2.NextDouble() * (hi2 - lo);
                    int bl = Mathf.RoundToInt(h.len * 1.6f);
                    for (int i = 0; i < bl; i++)
                        ThemeKitB.Ball(canopy, c + rot * new Vector3(H(-h.len * 0.45f, h.len * 0.45f), H(1.7f, 1.85f), H(-0.3f, 0.3f)), new Vector3(H(0.3f, 0.48f), H(0.18f, 0.26f), H(0.3f, 0.42f)),
                            rot * Quaternion.Euler(0, H(-25f, 25f), 0), pinks[hr2.Next(pinks.Length)], 0);
                    for (int i = 0; i < bl; i++)
                        ThemeKitB.Ball(canopy, c + rot * new Vector3(H(-h.len * 0.45f, h.len * 0.45f), H(0.4f, 1.5f), (hr2.NextDouble() < 0.5 ? -1f : 1f) * 0.55f), Vector3.one * H(0.12f, 0.2f),
                            rot, pinks[hr2.Next(pinks.Length)], 0);
                }

            // ---- fallen petals on the floor inside (not over the hatches)
            var hatches = Hatches();
            for (int i = 0; i < 320; i++)
            {
                float a = R(0f, 6.28f), d = Mathf.Sqrt(R(0.02f, 1f)) * (rIn - 0.4f);
                var c = new Vector3(Mathf.Cos(a) * d, 0.05f, Mathf.Sin(a) * d);
                float s = R(0.13f, 0.24f);
                bool overHatch = false;
                foreach (var hc in hatches) if (Mathf.Abs(c.x - hc.x) < 1.6f && Mathf.Abs(c.z - hc.z) < 1.6f) overHatch = true;
                if (overHatch) continue;
                ThemeKitB.Quad(canopy, c + new Vector3(-s, 0, -s * 0.7f), c + new Vector3(s, 0, -s * 0.7f), c + new Vector3(s, 0, s * 0.7f), c + new Vector3(-s, 0, s * 0.7f), pinks[rng.Next(pinks.Length)], Vector3.up);
            }

            k.SmoothNormals(40f);
            canopy.SmoothNormals(80f);
            ThemeKitB.Spawn(tree, "hollow tree", k, null, true);
            ThemeKitB.Spawn(tree, "hollow tree blossom", canopy, null, true);
            ThemeKitB.Spawn(tree, "hollow tree lanterns", glow, ThemeKitB.Glow(new Color(1f, 0.6f, 0.32f), 2.4f), false);
            BuildHollow(root);
            return true;
        }

        /// <summary>THE HOLLOW under the ball's floor: a round cave in the island's pillar, reached by the low routes through
        /// a cave mouth in the pillar's side (a ledge sticks out under each), lit by lanterns and glowing mushrooms, roots
        /// hanging from its ceiling, a few root columns for cover - and from it a ladder (one per team) up through a hatch in
        /// the ball's floor (a low fence round it, so the ball and nobody walking past drop in by accident).</summary>
        void BuildHollow(Transform root)
        {
            var hol = new GameObject("the hollow").transform; // (at the world's origin: the ladders need that)
            hol.SetParent(root, false);
            var solid = new MeshKit(); var deco = new MeshKit(); var glow = new MeshKit();
            var cols = new GameObject("hollow colliders").transform;
            cols.SetParent(hol, false);
            var rng = new System.Random(Cfg.MapSeed + 6262);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float cr = ChamberR, isl = IslandR, fy = ChamberFloor, cy = ChamberTop;
            float th0 = Th0Deg, span = 360f / Cfg.Copies;
            Vector3 Dir(int s) { float a = s * Mathf.PI * 2f / PitK; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            Vector3 DirD(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            var moss = new Color(0.27f, 0.33f, 0.22f); var moss2 = new Color(0.36f, 0.3f, 0.3f);
            var earth = new Color(0.33f, 0.21f, 0.18f); var earth2 = new Color(0.27f, 0.17f, 0.16f); var roof = new Color(0.22f, 0.14f, 0.13f);
            var up = Vector3.up;

            for (int s = 0; s < PitK; s++)
            {
                Vector3 o0 = Dir(s), o1 = Dir(s + 1), mid = o0 + o1;
                bool mouth = MouthSeg(s);
                // the floor (a fan from the middle) and, at a mouth, on out through the tunnel to the ledge outside
                var fc = Color.Lerp(moss, moss2, Mathf.PerlinNoise(s * 0.37f, 2.1f));
                ThemeKitB.Tri(solid, up * fy, o0 * cr + up * fy, o1 * cr + up * fy, fc, up);
                if (mouth)
                {
                    ThemeKitB.Quad(solid, o0 * cr + up * fy, o1 * cr + up * fy, o1 * LedgeR + up * fy, o0 * LedgeR + up * fy, fc, up);
                    ThemeKitB.Quad(solid, o0 * LedgeR + up * fy, o1 * LedgeR + up * fy, o1 * LedgeR + up * (fy - 0.9f), o0 * LedgeR + up * (fy - 0.9f), earth2, mid);
                    ThemeKitB.Quad(solid, o0 * LedgeR + up * (fy - 0.9f), o1 * LedgeR + up * (fy - 0.9f), o1 * (isl - 0.6f) + up * (MouthBot - 0.4f), o0 * (isl - 0.6f) + up * (MouthBot - 0.4f), earth2, Vector3.down);
                    // the tunnel's roof and the wall over it, inside
                    ThemeKitB.Quad(solid, o0 * cr + up * MouthTop, o1 * cr + up * MouthTop, o1 * (isl - 0.05f) + up * MouthTop, o0 * (isl - 0.05f) + up * MouthTop, roof, Vector3.down);
                    ThemeKitB.Quad(solid, o0 * cr + up * MouthTop, o1 * cr + up * MouthTop, o1 * cr + up * cy, o0 * cr + up * cy, earth, -mid);
                    // its sides (where the next segment isn't a mouth)
                    if (!MouthSeg(s - 1)) ThemeKitB.Quad(solid, o0 * cr + up * fy, o0 * (isl - 0.05f) + up * fy, o0 * (isl - 0.05f) + up * MouthTop, o0 * cr + up * MouthTop, earth2, -Vector3.Cross(up, o0));
                    if (!MouthSeg(s + 1)) ThemeKitB.Quad(solid, o1 * cr + up * fy, o1 * (isl - 0.05f) + up * fy, o1 * (isl - 0.05f) + up * MouthTop, o1 * cr + up * MouthTop, earth2, Vector3.Cross(up, o1));
                }
                else
                {
                    // the wall (facing in), banded
                    for (int band = 0; band < 3; band++)
                    {
                        float y0 = Mathf.Lerp(fy, cy, band / 3f), y1 = Mathf.Lerp(fy, cy, (band + 1) / 3f);
                        var wc = Color.Lerp(earth, earth2, Mathf.PerlinNoise(s * 0.29f, band * 1.7f)) * (band == 0 ? 0.9f : 1f); wc.a = 1f;
                        ThemeKitB.Quad(solid, o0 * cr + up * y0, o1 * cr + up * y0, o1 * cr + up * y1, o0 * cr + up * y1, wc, -mid);
                    }
                }
            }
            // the ceiling (the underside of the ball's floor), on the ground's grid so the hatches line up
            var hatches = Hatches();
            for (float x = -isl - 1f; x <= isl + 1f; x += 2f)
            for (float z = -isl - 1f; z <= isl + 1f; z += 2f)
            {
                float cx = Mathf.Floor(x / 2f) * 2f + 1f, cz = Mathf.Floor(z / 2f) * 2f + 1f;
                if (cx * cx + cz * cz > (cr + 1.5f) * (cr + 1.5f) || HatchCell(cx, cz)) continue;
                var c = new Vector3(cx, cy, cz);
                ThemeKitB.Quad(solid, c + new Vector3(-1, 0, -1), c + new Vector3(1, 0, -1), c + new Vector3(1, 0, 1), c + new Vector3(-1, 0, 1), roof * (0.9f + 0.2f * Mathf.PerlinNoise(cx * 0.21f, cz * 0.21f)), Vector3.down);
            }
            var spawned = ThemeKitB.Spawn(hol, "hollow", solid, null, true, true);
            spawned.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---- each hatch: the shaft up through the floor, a low fence round it, the ladder (climb out toward the middle)
            var wood = new Color(0.4f, 0.24f, 0.16f); var ladderC = new Color(0.55f, 0.36f, 0.22f);
            foreach (var hc in hatches)
            {
                var inw = HatchIn(hc);
                var side = Vector3.Cross(up, inw);
                foreach (var n in new[] { inw, -inw, side, -side })
                {
                    var t = Vector3.Cross(up, n);
                    var e = hc + n * 1f;
                    ThemeKitB.Quad(deco, e - t + up * cy, e + t + up * cy, e + t, e - t, earth2, -n);
                    ThemeKitB.BoxCol(cols, e + n * 0.15f + up * ((cy - 0.02f) * 0.5f), new Vector3(2.6f, -cy - 0.02f, 0.3f), Quaternion.LookRotation(n));
                    // the fence: two rails between posts, a box to stop you (and the ball)
                    var f = hc + n * 1.12f;
                    ThemeKitB.Cyl(deco, f - t * 1.12f + up * 0.32f, f + t * 1.12f + up * 0.32f, 0.07f, 0.07f, 6, wood, true, true);
                    ThemeKitB.Cyl(deco, f - t * 1.12f + up * 0.66f, f + t * 1.12f + up * 0.66f, 0.07f, 0.07f, 6, wood, true, true);
                    ThemeKitB.Cyl(deco, f - t * 1.12f, f - t * 1.12f + up * 0.82f, 0.1f, 0.09f, 6, wood * 0.85f, false, true);
                    ThemeKitB.BoxCol(cols, f + up * 0.375f, new Vector3(2.5f, 0.75f, 0.24f), Quaternion.LookRotation(n));
                }
                // a ring of blossom on the fence posts (you can spot the hatch across the floor)
                for (int i = 0; i < 4; i++)
                {
                    var corner = hc + (inw + side) * 1.12f;
                    corner = hc + Quaternion.Euler(0, 90f * i, 0) * (corner - hc);
                    ThemeKitB.Ball(deco, corner + up * 0.92f, new Vector3(0.24f, 0.18f, 0.24f), Quaternion.identity, k_Pinks[i % k_Pinks.Length], 0);
                }
                ThemeKitB.Ladder(hol, deco, hc + inw * 1f + up * fy, 0.75f, -inw, ladderC);
                // a lantern by the ladder's foot
                ThemeKitB.Ball(glow, hc + side * 0.9f + up * (fy + 2.4f), new Vector3(0.22f, 0.28f, 0.22f), Quaternion.identity, Color.white, 1);
                ThemeKitB.Cyl(deco, hc + side * 0.9f + up * (fy + 2.68f), hc + side * 0.9f + up * cy, 0.02f, 0.02f, 4, wood, false, false);
            }

            // ---- root columns (cover, and something to see), roots hanging from the ceiling, glowing mushrooms, petals
            var rootC = new Color(0.26f, 0.15f, 0.12f);
            foreach (var (off, rf) in new[] { (span * 0.3f, 0.62f), (-span * 0.3f, 0.62f), (span * 0.5f, 0.38f) })
                for (int m = 0; m < Cfg.Copies; m++)
                {
                    var p = DirD(th0 + off + m * span) * cr * rf;
                    p.y = fy;
                    float r0 = R(0.55f, 0.75f);
                    ThemeKitB.Cyl(deco, p + up * -0.1f, p + up * (cy - fy) * 0.5f + new Vector3(R(-0.3f, 0.3f), 0, R(-0.3f, 0.3f)), r0 * 1.3f, r0, 8, rootC, false, false);
                    ThemeKitB.Cyl(deco, p + up * (cy - fy) * 0.5f, p + up * (cy - fy + 0.05f), r0, r0 * 1.5f, 8, rootC, false, false);
                    ThemeKitB.CapCol(cols, p, p + up * (cy - fy), r0);
                }
            BuildShack(deco, glow, cols, fy, th0);
            int hang = Mathf.RoundToInt(cr * 4f);
            for (int i = 0; i < hang; i++)
            {
                float a = R(0f, 6.28f), d = Mathf.Sqrt(R(0.03f, 1f)) * (cr - 0.3f);
                var p = new Vector3(Mathf.Cos(a) * d, cy, Mathf.Sin(a) * d);
                bool nearHatch = false;
                foreach (var hc in hatches) if (Mathf.Abs(p.x - hc.x) < 2f && Mathf.Abs(p.z - hc.z) < 2f) nearHatch = true;
                if (nearHatch) continue;
                ThemeKitB.Cyl(deco, p, p + new Vector3(R(-0.3f, 0.3f), -R(0.5f, 2.6f), R(-0.3f, 0.3f)), R(0.05f, 0.12f), 0.02f, 5, rootC, false, false);
            }
            int shrooms = Mathf.RoundToInt(cr * 3f);
            for (int i = 0; i < shrooms; i++)
            {
                int s = rng.Next(PitK);
                var o = Dir(s) * (cr - R(0.3f, 1.2f));
                float hh = R(0.15f, 0.45f), rr = hh * R(0.5f, 0.8f);
                var b = o + up * fy;
                ThemeKitB.Cyl(deco, b, b + up * hh, rr * 0.22f, rr * 0.18f, 5, new Color(0.88f, 0.85f, 0.8f), false, false);
                ThemeKitB.Ball(glow, b + up * hh, new Vector3(rr, rr * 0.45f, rr), Quaternion.identity, Color.white, 0);
            }
            for (int i = 0; i < 160; i++)
            {
                float a = R(0f, 6.28f), d = Mathf.Sqrt(R(0.02f, 1f)) * (cr - 0.4f);
                var c = new Vector3(Mathf.Cos(a) * d, fy + 0.04f, Mathf.Sin(a) * d);
                float s = R(0.12f, 0.22f);
                ThemeKitB.Quad(deco, c + new Vector3(-s, 0, -s * 0.7f), c + new Vector3(s, 0, -s * 0.7f), c + new Vector3(s, 0, s * 0.7f), c + new Vector3(-s, 0, s * 0.7f), k_Pinks[rng.Next(k_Pinks.Length)], up);
            }
            // paper lanterns hanging over each cave mouth's inner end and round the middle
            for (int m = 0; m < Cfg.Copies; m++)
            {
                foreach (float off in MouthOffs)
                {
                    var o = DirD(MouthCentreDeg(MouthS0(th0 + off)) + m * span * -1f);
                    ThemeKitB.Ball(glow, o * (cr - 0.8f) + up * (MouthTop - 0.2f), new Vector3(0.26f, 0.32f, 0.26f), Quaternion.identity, Color.white, 1);
                }
                var mp = DirD(th0 + m * span + span * 0.5f) * cr * 0.5f;
                ThemeKitB.Ball(glow, mp + up * (cy - 1.4f), new Vector3(0.3f, 0.38f, 0.3f), Quaternion.identity, Color.white, 1);
                ThemeKitB.Cyl(deco, mp + up * (cy - 1.05f), mp + up * cy, 0.02f, 0.02f, 4, wood, false, false);
            }
            ThemeKitB.Spawn(hol, "hollow dressing", deco, null, false);
            ThemeKitB.Spawn(hol, "hollow lights", glow, ThemeKitB.Glow(new Color(1f, 0.66f, 0.38f), 2.2f), false);
        }

        /// <summary>A little wooden shack in the middle of the hollow (on its floor at fy): a ring of plank walls with a
        /// doorway toward every team's side, a pointed roof (solid), a lantern inside. You can go in.</summary>
        static void BuildShack(MeshKit deco, MeshKit glow, Transform cols, float fy, float th0)
        {
            int copies = Mathf.Max(1, Cfg.Copies);
            int sides = copies * 2 < 6 ? 6 : copies * 2;
            if (sides % copies != 0) sides = copies * 2;
            const float ap = 2.2f, wallHt = 2.7f, wt = 0.22f, doorW = 1.4f, doorHt = 2.2f;
            float edge = 2f * ap * Mathf.Tan(Mathf.PI / sides);
            var plank = new Color(0.45f, 0.29f, 0.19f); var plank2 = new Color(0.38f, 0.24f, 0.16f); var beam = new Color(0.26f, 0.16f, 0.11f);
            var roofC = new Color(0.55f, 0.3f, 0.38f);
            var up = Vector3.up;
            for (int i = 0; i < sides; i++)
            {
                float a = (th0 + i * 360f / sides) * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var q = Quaternion.LookRotation(n);
                var t = q * Vector3.right;
                var c = n * (ap - wt * 0.5f) + up * fy;
                bool door = i % (sides / copies) == 0;
                var col = i % 2 == 0 ? plank : plank2;
                float hw = edge * 0.5f + 0.06f;
                void Wall(float x0, float x1, float y0, float y1)
                {
                    if (x1 - x0 < 0.02f || y1 - y0 < 0.02f) return;
                    var wc = c + t * ((x0 + x1) * 0.5f) + up * ((y0 + y1) * 0.5f);
                    var ws = new Vector3(x1 - x0, y1 - y0, wt);
                    deco.Box(wc, ws, q, col, MeshKit.All);
                    ThemeKitB.BoxCol(cols, wc, ws, q);
                }
                if (door)
                {
                    Wall(-hw, -doorW * 0.5f, 0f, wallHt);
                    Wall(doorW * 0.5f, hw, 0f, wallHt);
                    Wall(-doorW * 0.5f, doorW * 0.5f, doorHt, wallHt);
                    // the door frame
                    for (int sg = -1; sg <= 1; sg += 2)
                        deco.Box(c + n * 0.03f + t * (sg * (doorW * 0.5f + 0.06f)) + up * (doorHt * 0.5f), new Vector3(0.12f, doorHt, wt + 0.08f), q, beam, MeshKit.All);
                    deco.Box(c + n * 0.03f + up * (doorHt + 0.06f), new Vector3(doorW + 0.24f, 0.12f, wt + 0.08f), q, beam, MeshKit.All);
                }
                else Wall(-hw, hw, 0f, wallHt);
                // a plank line or two across it (outside), and a post at the corner
                if (!door)
                    for (int b = 1; b <= 2; b++)
                        deco.Box(c + n * (wt * 0.5f + 0.01f) + up * (wallHt * b / 3f), new Vector3(edge, 0.05f, 0.02f), q, beam, MeshKit.All);
                float ca = a + Mathf.PI / sides;
                var corner = new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca)) * (ap / Mathf.Cos(Mathf.PI / sides));
                ThemeKitB.Cyl(deco, corner + up * fy, corner + up * (fy + wallHt), 0.12f, 0.12f, 6, beam, false, true);
            }
            // the roof: a low pointed one with an overhang (solid, so you can stand on it too)
            var roof = new MeshKit();
            float b0 = (th0 + 180f / sides) * Mathf.Deg2Rad;
            float rr = ap / Mathf.Cos(Mathf.PI / sides) + 0.45f;
            ThemeKitB.Cyl(roof, up * (fy + wallHt), up * (fy + wallHt + 1.4f), rr, 0.02f, sides, roofC, true, false, -(b0 + Mathf.PI * 0.5f));
            ThemeKitB.Spawn(cols, "shack roof", roof, null, true, true);
            // a lantern hanging inside
            ThemeKitB.Cyl(deco, up * (fy + wallHt - 0.05f), up * (fy + wallHt - 0.55f), 0.02f, 0.02f, 4, beam, false, false);
            ThemeKitB.Ball(glow, up * (fy + wallHt - 0.8f), new Vector3(0.22f, 0.28f, 0.22f), Quaternion.identity, Color.white, 1);
            // a crate to sit behind
            var crate = new Vector3(Mathf.Cos(b0), 0f, Mathf.Sin(b0)) * (ap * 0.45f) + up * (fy + 0.35f);
            deco.Box(crate, Vector3.one * 0.7f, Quaternion.Euler(0, th0 + 20f, 0), plank2, MeshKit.All);
            ThemeKitB.BoxCol(cols, crate, Vector3.one * 0.7f, Quaternion.Euler(0, th0 + 20f, 0));
        }

        // ---------------------------------------------------------------- the blossom deer (the horse here) and the cherry bush
        public override string MountName => "Blossom Deer";

        /// <summary>A spotted deer with blossom in its antlers (a unicorn: a white one with golden antlers and a horn).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var coat = unicorn ? new Color(0.96f, 0.95f, 0.97f) : new Color(0.7f, 0.46f, 0.3f);
            var belly = unicorn ? new Color(1f, 0.92f, 0.96f) : new Color(0.93f, 0.85f, 0.74f);
            var dark = unicorn ? new Color(0.86f, 0.72f, 0.96f) : new Color(0.28f, 0.17f, 0.12f);
            var antler = unicorn ? new Color(1f, 0.85f, 0.42f) : new Color(0.86f, 0.8f, 0.68f);
            var pink = new Color(0.97f, 0.66f, 0.8f); var pink2 = new Color(0.99f, 0.84f, 0.9f);
            // body: a deer is slimmer than a horse, white underneath, spots along its back
            ThemeKitB.MHit(Art.Box(t, coat, new Vector3(0, 1.17f, 0), new Vector3(0.52f, 0.56f, 1.45f)), ghost);
            Art.Box(t, belly, new Vector3(0, 0.93f, 0.02f), new Vector3(0.46f, 0.1f, 1.25f));
            for (int i = 0; i < 8; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 0.262f, z = -0.55f + i * 0.15f, y = 1.28f + (i % 3) * 0.06f;
                Art.Box(t, belly, new Vector3(x, y, z), new Vector3(0.02f, 0.09f, 0.09f));
            }
            // neck + head (the head pivots to graze)
            var neck = ThemeKitB.MPivot(t, "neck", new Vector3(0, 1.36f, 0.62f));
            ThemeKitB.MHit(Art.Box(neck, coat, new Vector3(0, 0.32f, 0.1f), new Vector3(0.26f, 0.72f, 0.3f), new Vector3(20, 0, 0)), ghost);
            Art.Box(neck, belly, new Vector3(0, 0.26f, 0.22f), new Vector3(0.2f, 0.4f, 0.1f), new Vector3(20, 0, 0));
            ThemeKitB.MHead(neck, ghost, coat, new Vector3(0, 0.68f, 0.32f), new Vector3(0.26f, 0.26f, 0.48f));
            Art.Box(neck, belly, new Vector3(0, 0.62f, 0.55f), new Vector3(0.18f, 0.14f, 0.12f));
            Art.Box(neck, Color.black, new Vector3(0, 0.66f, 0.62f), new Vector3(0.1f, 0.06f, 0.03f));       // nose
            Art.Box(neck, Color.black, new Vector3(0.135f, 0.74f, 0.4f), new Vector3(0.02f, 0.07f, 0.07f));  // eyes
            Art.Box(neck, Color.black, new Vector3(-0.135f, 0.74f, 0.4f), new Vector3(0.02f, 0.07f, 0.07f));
            for (int s = -1; s <= 1; s += 2)
            {
                // big soft ears out to the sides
                Art.Box(neck, coat, new Vector3(s * 0.2f, 0.84f, 0.2f), new Vector3(0.22f, 0.09f, 0.12f), new Vector3(0, 0, s * 25f));
                Art.Box(neck, pink2, new Vector3(s * 0.205f, 0.83f, 0.205f), new Vector3(0.16f, 0.05f, 0.09f), new Vector3(0, 0, s * 25f));
                // antlers: a beam up and back, two tines, blossom on the tips
                var b0 = new Vector3(s * 0.08f, 0.82f, 0.26f);
                Art.Box(neck, antler, b0 + new Vector3(s * 0.06f, 0.18f, -0.03f), new Vector3(0.05f, 0.4f, 0.05f), new Vector3(-10f, 0, -s * 20f));
                Art.Box(neck, antler, b0 + new Vector3(s * 0.15f, 0.42f, -0.1f), new Vector3(0.05f, 0.3f, 0.05f), new Vector3(-30f, 0, -s * 40f));
                Art.Box(neck, antler, b0 + new Vector3(s * 0.07f, 0.4f, 0.06f), new Vector3(0.04f, 0.2f, 0.04f), new Vector3(25f, 0, -s * 5f));
                Art.Box(neck, pink, b0 + new Vector3(s * 0.24f, 0.56f, -0.16f), new Vector3(0.16f, 0.12f, 0.16f), new Vector3(0, 30f, 0));
                Art.Box(neck, pink2, b0 + new Vector3(s * 0.08f, 0.52f, 0.1f), new Vector3(0.12f, 0.1f, 0.12f), new Vector3(0, 15f, 0));
                Art.Box(neck, pink, b0 + new Vector3(s * 0.13f, 0.3f, -0.02f), new Vector3(0.1f, 0.08f, 0.1f), new Vector3(0, 45f, 0));
            }
            if (unicorn) ThemeKitB.MHorn(neck, new Vector3(0, 0.84f, 0.42f));
            // a garland of blossom round its neck
            for (int i = 0; i < 5; i++)
                Art.Box(neck, i % 2 == 0 ? pink : pink2, new Vector3((i - 2) * 0.07f, 0.05f + Mathf.Abs(i - 2) * 0.04f, 0.27f - Mathf.Abs(i - 2) * 0.05f), new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0, i * 20f, 0));
            head = neck;
            // a short white tail
            var tl = ThemeKitB.MPivot(t, "tail", new Vector3(0, 1.38f, -0.74f));
            ThemeKitB.MHit(Art.Box(tl, belly, new Vector3(0, -0.1f, -0.06f), new Vector3(0.16f, 0.24f, 0.1f)), ghost);
            Art.Box(tl, coat, new Vector3(0, -0.04f, -0.1f), new Vector3(0.13f, 0.14f, 0.05f));
            tail = tl;
            // slim legs, dark hooves
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.17f : 0.17f, z = i < 2 ? 0.55f : -0.55f;
                var leg = ThemeKitB.MPivot(t, "leg", new Vector3(x, 0.9f, z));
                ThemeKitB.MHit(Art.Box(leg, coat, new Vector3(0, -0.22f, 0), new Vector3(0.15f, 0.44f, 0.16f)), ghost);
                ThemeKitB.MHit(Art.Box(leg, coat, new Vector3(0, -0.62f, 0), new Vector3(0.1f, 0.4f, 0.1f)), ghost);
                Art.Box(leg, unicorn ? antler : dark, new Vector3(0, -0.85f, 0.01f), new Vector3(0.12f, 0.1f, 0.14f));
                legs?.Add(leg);
            }
            // a red lacquered saddle
            saddle = ThemeKitB.MSaddle(t, ghost, new Color(0.55f, 0.1f, 0.09f), 0.52f, 1.47f);
            return true;
        }

        /// <summary>The game's own berry bush in Cherry Blossom's colours: green leaves, cherry-red berries.</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.3f, 0.5f, 0.28f), new Color(0.82f, 0.08f, 0.14f));
            return true;
        }

        // ---------------------------------------------------------------- every frame
        public override void ClientTick()
        {
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            float nt = ThemeKitB.NetTime; // (the moving chunks keep the same time on every peer)
            m_Feet.Clear();
            foreach (var p in PlayerNet.All) if (p != null && !p.Dead.Value) m_Feet.Add(p.transform.position);
            var local = PlayerController.Local;
            bool carryOk = local != null && local.Grounded;
            var lp = local != null ? local.transform.position : Vector3.one * 99999f;
            bool carried = false;
            foreach (var c in m_Chunks)
            {
                if (c.T == null) continue;
                bool on = false;
                var off = Vector3.zero;
                int n = 0;
                var top = c.T.position;
                float reach = (c.R + 0.45f) * (c.R + 0.45f);
                foreach (var f in m_Feet)
                {
                    var d = f - top;
                    if (d.y < -0.8f || d.y > 1.1f) continue;
                    d.y = 0f;
                    if (d.sqrMagnitude > reach) continue;
                    on = true; off += d; n++;
                }
                bool localOn = false;
                if (carryOk && !carried)
                {
                    var d = lp - top;
                    if (d.y > -0.5f && d.y < 0.6f) { d.y = 0f; localOn = d.sqrMagnitude <= reach; }
                }
                if (on && !c.On) c.Kick = 1f;
                c.On = on;
                c.Sink = Mathf.MoveTowards(c.Sink, on ? 1f : 0f, dt * (on ? 2.4f : 0.8f));
                var tiltT = on ? Vector3.ClampMagnitude(off / Mathf.Max(1, n) / c.R, 1f) : Vector3.zero;
                c.Tilt = Vector3.Lerp(c.Tilt, tiltT, 1f - Mathf.Exp(-dt * 4f));
                c.Kick = Mathf.Max(0f, c.Kick - dt * 1.3f);
                float s = c.Sink * c.Sink * (3f - 2f * c.Sink);
                var basePos = c.Pos;
                if (c.Move == 1) basePos += Vector3.up * (Mathf.Sin(nt * c.Spd + c.MPh) * c.Amp);
                else if (c.Move == 2) basePos += c.Drift * (Mathf.Sin(nt * c.Spd + c.MPh) * c.Amp);
                float y = basePos.y + Mathf.Sin(t * 0.7f + c.Phase) * 0.07f - s * 0.15f - Mathf.Sin(t * 10f + c.Phase) * c.Kick * 0.06f;
                var axis = new Vector3(c.Tilt.z, 0f, -c.Tilt.x);
                var tilt = axis.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(c.Tilt.magnitude * 7f, axis.normalized) : Quaternion.identity;
                var wob = Quaternion.Euler(Mathf.Sin(t * 0.53f + c.Phase) * 0.9f + Mathf.Sin(t * 9f + c.Phase) * c.Kick * 3f, 0f,
                    Mathf.Cos(t * 0.61f + c.Phase * 1.3f) * 0.9f + Mathf.Cos(t * 8f + c.Phase) * c.Kick * 3f);
                var newPos = new Vector3(basePos.x, y, basePos.z);
                // standing on a moving one: it takes you with it (you first, so you're never inside it)
                if (localOn && c.Move != 0) { local.Carry(newPos - top); carried = true; }
                c.T.SetPositionAndRotation(newPos, tilt * wob * c.Rot);
            }
            var cam = Camera.main;
            bool hide = ThemeKitB.SkyHidden(cam);
            if (m_Sky != null)
            {
                if (m_Sky.gameObject.activeSelf == hide) m_Sky.gameObject.SetActive(!hide);
                if (!hide) m_Sky.position = cam.transform.position;
            }
            m_Drift?.Tick(cam, hide);
            ThemeKitB.Keep(cam);
        }

        public override void ApplySky()
        {
            ThemeKitB.Begin();
            ThemeKitB.Skybox(new Color(0.26f, 0.34f, 0.85f), new Color(0.2f, 0.16f, 0.26f), 0.62f, 0.7f);
            // (lighter, warmer light than at first: the purple light and fog turned the grass and the blossom all one lavender)
            ThemeKitB.Fog(new Color(0.55f, 0.5f, 0.72f), 70f, Mathf.Max(320f, Half * 3.4f));
            ThemeKitB.Lighting(new Color(1f, 0.95f, 0.97f), new Color(0.72f, 0.72f, 0.86f), new Color(0.7f, 0.62f, 0.68f), new Color(0.42f, 0.38f, 0.36f));
        }

        public override void Cleanup() => ThemeKitB.End();

        /// <summary>Petals drifting down round the camera (one mesh, moved on the CPU: a couple of hundred quads).</summary>
        class PetalDrift
        {
            readonly Mesh m_Mesh;
            readonly MeshRenderer m_R;
            readonly Vector3[] m_V, m_P, m_Vel;
            readonly float[] m_S, m_Ph;
            const float Box = 24f;
            bool m_Placed;

            public PetalDrift(Transform root, int n)
            {
                var rng = new System.Random(5150);
                float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                m_V = new Vector3[n * 4]; m_P = new Vector3[n]; m_Vel = new Vector3[n]; m_S = new float[n]; m_Ph = new float[n];
                var cols = new Color[n * 4]; var nr = new Vector3[n * 4]; var tris = new int[n * 6];
                Color[] pc = { new Color(0.98f, 0.76f, 0.86f), new Color(0.95f, 0.64f, 0.8f), new Color(1f, 0.88f, 0.93f) };
                for (int i = 0; i < n; i++)
                {
                    m_P[i] = new Vector3(R(-Box, Box), R(-4f, 16f), R(-Box, Box));
                    m_Vel[i] = new Vector3(R(0.3f, 0.9f), -R(0.5f, 1.1f), R(-0.3f, 0.4f));
                    m_S[i] = R(0.07f, 0.12f); m_Ph[i] = R(0f, 6.28f);
                    var c = pc[rng.Next(pc.Length)].linear;
                    for (int k = 0; k < 4; k++) { cols[i * 4 + k] = c; nr[i * 4 + k] = Vector3.up; }
                    tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 1; tris[i * 6 + 2] = i * 4 + 2;
                    tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 2; tris[i * 6 + 5] = i * 4 + 3;
                }
                m_Mesh = new Mesh { name = "drifting petals" };
                m_Mesh.MarkDynamic();
                m_Mesh.vertices = m_V; m_Mesh.normals = nr; m_Mesh.colors = cols; m_Mesh.triangles = tris;
                var go = new GameObject("drifting petals");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
                go.AddComponent<OwnedMesh>().Mesh = m_Mesh;
                m_R = go.AddComponent<MeshRenderer>();
                m_R.sharedMaterial = ThemeKitB.Painted(true);
                m_R.shadowCastingMode = ShadowCastingMode.Off;
            }

            public void Tick(Camera cam, bool hide)
            {
                if (m_R == null) return;
                m_R.enabled = !hide;
                if (hide) return;
                var c = cam.transform.position;
                float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
                for (int i = 0; i < m_P.Length; i++)
                {
                    var p = m_P[i];
                    if (!m_Placed) p += c;
                    p += (m_Vel[i] + new Vector3(Mathf.Sin(t * 1.3f + m_Ph[i]) * 0.5f, Mathf.Sin(t * 2.1f + m_Ph[i]) * 0.25f, Mathf.Cos(t * 1.1f + m_Ph[i]) * 0.4f)) * dt;
                    var rel = p - c;
                    if (rel.x > Box) p.x -= 2 * Box; else if (rel.x < -Box) p.x += 2 * Box;
                    if (rel.z > Box) p.z -= 2 * Box; else if (rel.z < -Box) p.z += 2 * Box;
                    if (rel.y < -5f) p.y += 22f; else if (rel.y > 17f) p.y -= 22f;
                    m_P[i] = p;
                    var q = Quaternion.Euler(t * 70f + m_Ph[i] * 50f, t * 40f + m_Ph[i] * 30f, m_Ph[i] * 57f);
                    var u = q * Vector3.right * m_S[i]; var v = q * Vector3.forward * (m_S[i] * 0.75f);
                    m_V[i * 4] = p - u - v; m_V[i * 4 + 1] = p + u - v; m_V[i * 4 + 2] = p + u + v; m_V[i * 4 + 3] = p - u + v;
                }
                m_Placed = true;
                m_Mesh.vertices = m_V;
                m_Mesh.bounds = new Bounds(c, Vector3.one * 70f);
            }
        }
    }

    // =====================================================================================================================
    // ThemeKitB: little helpers shared by the Cherry Blossom, Wonderland, Cube and Mars maps (Map.Cherry/Wonderland/Cube/Mars.cs)
    // =====================================================================================================================
    public static class ThemeKitB
    {
        // ---------------------------------------------------------------- materials
        static Material s_Painted, s_Painted2;
        static readonly Dictionary<Color, Material> s_Glow = new Dictionary<Color, Material>();

        /// <summary>Lit vertex-coloured (RockGame/Painted, still), the look of the trees and bushes.</summary>
        public static Material Painted(bool twoSided = false)
        {
            ref var m = ref (twoSided ? ref s_Painted2 : ref s_Painted);
            if (m != null) return m;
            var sh = Resources.Load<Shader>("World/Painted");
            if (sh == null || !sh.isSupported) return m = Art.Mat(new Color(0.6f, 0.6f, 0.6f));
            m = new Material(sh) { name = twoSided ? "theme painted 2 sided" : "theme painted" };
            m.SetFloat("_Wind", 0f);
            m.SetFloat("_Glow", 0f);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.SetColor("_Tint", Color.white);
            return m;
        }

        /// <summary>A flat glowing colour (RockGame/Glow: unlit, no fog, over 1 it blooms).</summary>
        public static Material Glow(Color c, float intensity)
        {
            var key = new Color(c.r, c.g, c.b, intensity);
            if (s_Glow.TryGetValue(key, out var m) && m) return m;
            var sh = Resources.Load<Shader>("World/Glow");
            if (sh != null && sh.isSupported)
            {
                m = new Material(sh) { name = "theme glow" };
                m.SetColor("_Color", c);
                m.SetFloat("_Intensity", intensity);
            }
            else
            {
                m = new Material(Art.Mat(c)) { name = "theme glow" };
                if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * intensity); }
            }
            s_Glow[key] = m;
            return m;
        }

        /// <summary>Unlit vertex colours (linear) x tint, no fog (the arena's sky shader): stars, planets, space.</summary>
        public static Material SkyMat(Color tint, CullMode cull)
        {
            var sh = Resources.Load<Shader>("SpaceArena/SpaceSky");
            if (sh == null || !sh.isSupported) return Glow(tint, 1f);
            var m = new Material(sh) { name = "theme sky" };
            m.SetColor("_Color", tint);
            m.SetFloat("_Cull", (float)cull);
            return m;
        }

        // ---------------------------------------------------------------- building meshes
        /// <summary>A triangle facing `outward` (whichever way round the corners come).</summary>
        public static void Tri(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0f) (b, c) = (c, b);
            k.Tri(a, b, c, col);
        }

        public static void Quad(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 outward)
        {
            Tri(k, a, b, c, col, outward);
            Tri(k, a, c, d, col, outward);
        }

        /// <summary>A cylinder / truncated cone from a (radius ra) to b (radius rb), flat-shaded sides.</summary>
        public static void Cyl(MeshKit k, Vector3 a, Vector3 b, float ra, float rb, int sides, Color col, bool capA = true, bool capB = true, float spin = 0f, bool inward = false)
        {
            var axis = b - a;
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(axis, u);
            for (int s = 0; s < sides; s++)
            {
                float a0 = spin + s * Mathf.PI * 2f / sides, a1 = spin + (s + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                var o = (d0 + d1) * (inward ? -1f : 1f);
                Quad(k, a + d0 * ra, a + d1 * ra, b + d1 * rb, b + d0 * rb, col, o);
                if (capA && ra > 0.001f) Tri(k, a, a + d0 * ra, a + d1 * ra, col * 0.9f, -axis);
                if (capB && rb > 0.001f) Tri(k, b, b + d0 * rb, b + d1 * rb, col, axis);
            }
        }

        static readonly Vector3[] s_Ico20V;
        static readonly int[] s_Ico20F = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                          3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        static ThemeKitB()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            s_Ico20V = new[] { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            for (int i = 0; i < s_Ico20V.Length; i++) s_Ico20V[i] = s_Ico20V[i].normalized;
        }

        /// <summary>A faceted ball (detail 0: 20 faces, 1: 80) with radii, turned by rot; jitter pushes its corners in and out.</summary>
        public static void Ball(MeshKit k, Vector3 c, Vector3 radii, Quaternion rot, Color col, int detail = 1, float jitter = 0f, int seed = 0)
        {
            Vector3[] vs; int[] fs;
            if (detail <= 0) { vs = s_Ico20V; fs = s_Ico20F; }
            else MeshKit.Ico80(out vs, out fs);
            var p = new Vector3[vs.Length];
            for (int i = 0; i < vs.Length; i++)
            {
                float j = jitter > 0f ? 1f + (Mathf.PerlinNoise(vs[i].x * 3.1f + seed * 0.37f, vs[i].z * 3.1f + vs[i].y * 2.3f) - 0.5f) * 2f * jitter : 1f;
                p[i] = c + rot * Vector3.Scale(vs[i] * j, radii);
            }
            for (int i = 0; i < fs.Length; i += 3)
            {
                Vector3 a = p[fs[i]], b = p[fs[i + 1]], d = p[fs[i + 2]];
                var shade = col * (0.94f + 0.12f * Vector3.Dot((a + b + d) / 3f - c, rot * Vector3.up) / Mathf.Max(0.01f, radii.y));
                shade.a = 1f;
                Tri(k, a, b, d, shade, (a + b + d) / 3f - c);
            }
        }

        /// <summary>A little four-pointed star facing the sky's middle (for MeshBatch skies; colour linear).</summary>
        public static void Star(MeshBatch mb, Vector3 c, float size, Color col, float twinkle)
        {
            var n = -c.normalized;
            var u = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(n, u);
            mb.Tint = col;
            mb.Extra = twinkle > 0.6f ? new Vector4(twinkle * 9f, 0.45f, twinkle * 2f, 0) : Vector4.zero;
            Vector3 p0 = c + u * size, p1 = c + v * size * 0.45f, p2 = c - u * size, p3 = c - v * size * 0.45f;
            Vector3 q0 = c + v * size, q1 = c + u * size * 0.45f, q2 = c - v * size, q3 = c - u * size * 0.45f;
            mb.Tri(p0, p1, p2, n); mb.Tri(p0, p2, p3, n);
            mb.Tri(q0, q1, q2, n); mb.Tri(q0, q2, q3, n);
        }

        /// <summary>The kit as a child mesh object (Painted unless mat is given).</summary>
        public static GameObject Spawn(Transform parent, string name, MeshKit kit, Material mat, bool shadows, bool collider = false)
        {
            if (kit == null || kit.Count == 0) { var empty = new GameObject(name); empty.transform.SetParent(parent, false); return empty; }
            var go = MeshKit.Spawn(parent, name, new[] { mat != null ? mat : Painted() }, shadows, kit);
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go;
        }

        public static BoxCollider BoxCol(Transform parent, Vector3 c, Vector3 size, Quaternion rot)
        {
            if (parent == null) return null;
            var go = new GameObject("col");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = c;
            go.transform.localRotation = rot;
            var b = go.AddComponent<BoxCollider>();
            b.size = size;
            return b;
        }

        public static CapsuleCollider CapCol(Transform parent, Vector3 a, Vector3 b, float r)
        {
            if (parent == null) return null;
            var go = new GameObject("col");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (a + b) * 0.5f;
            var d = b - a;
            go.transform.localRotation = d.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.up, d) : Quaternion.identity;
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = r;
            c.height = d.magnitude + 2f * r;
            return c;
        }

        // ---------------------------------------------------------------- the ground
        /// <summary>Where a ground grid corner goes (it may be moved, e.g. onto the edge of a hole); the code: 0 a plain corner,
        /// anything else a moved one - a square whose corners are all moved, or moved two different ways, is left out.</summary>
        public delegate int GridVert(float x, float z, out Vector3 p);

        /// <summary>A flat-shaded vertex-coloured ground over [lo, hi]² (step metres a square), with a collider; cut: squares left out.</summary>
        public static GameObject Ground(Transform root, string name, float lo, float hi, float step, GridVert vert, System.Func<Vector3, float, Color> colour,
            System.Func<float, float, bool> cut = null, bool shadows = true)
        {
            int n = Mathf.RoundToInt((hi - lo) / step);
            var pos = new Vector3[n + 1, n + 1];
            var code = new int[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                code[i, j] = vert(lo + i * step, lo + j * step, out pos[i, j]);
            var kit = new MeshKit();
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                int c0 = code[i, j], c1 = code[i + 1, j], c2 = code[i + 1, j + 1], c3 = code[i, j + 1];
                if (c0 != 0 && c1 != 0 && c2 != 0 && c3 != 0) continue;
                int mn = int.MaxValue, mx = 0;
                foreach (int c in new[] { c0, c1, c2, c3 }) if (c != 0) { mn = Mathf.Min(mn, c); mx = Mathf.Max(mx, c); }
                if (mx != 0 && mn != mx) continue;
                if (cut != null && cut(lo + (i + 0.5f) * step, lo + (j + 0.5f) * step)) continue;
                Vector3 a = pos[i, j], b = pos[i + 1, j], cc = pos[i + 1, j + 1], d = pos[i, j + 1];
                if (((i + j) & 1) == 0) { GroundTri(kit, a, b, cc, colour); GroundTri(kit, a, cc, d, colour); }
                else { GroundTri(kit, a, b, d, colour); GroundTri(kit, b, cc, d, colour); }
            }
            var go = Spawn(root, name, kit, null, shadows, true);
            go.AddComponent<GroundMarker>();
            return go;
        }

        static void GroundTri(MeshKit k, Vector3 a, Vector3 b, Vector3 c, System.Func<Vector3, float, Color> colour)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            if (n.y < 0f) { (b, c) = (c, b); n = -n; }
            n.Normalize();
            k.Tri(a, b, c, colour((a + b + c) / 3f, n.y));
        }

        // ---------------------------------------------------------------- sky, fog and light (put back as they were after)
        static bool s_Saved;
        static int s_ApplyFrame = -1;
        static Material s_SkyWas;
        static bool s_FogWas;
        static FogMode s_FogModeWas;
        static Color s_FogColWas, s_AmbSkyWas, s_AmbEqWas, s_AmbGroundWas, s_SunColWas;
        static float s_FogStartWas, s_FogEndWas, s_FogDensityWas;
        static AmbientMode s_AmbModeWas;
        static Light s_Sun;

        public static Light Sun()
        {
            var sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.enabled) { sun = l; break; }
            return sun;
        }

        /// <summary>A theme map's sky is going up: remember how things were (once).</summary>
        public static void Begin()
        {
            s_ApplyFrame = Time.frameCount;
            if (s_Saved) return;
            s_Saved = true;
            s_SkyWas = RenderSettings.skybox;
            s_FogWas = RenderSettings.fog; s_FogModeWas = RenderSettings.fogMode; s_FogColWas = RenderSettings.fogColor;
            s_FogStartWas = RenderSettings.fogStartDistance; s_FogEndWas = RenderSettings.fogEndDistance; s_FogDensityWas = RenderSettings.fogDensity;
            s_AmbModeWas = RenderSettings.ambientMode; s_AmbSkyWas = RenderSettings.ambientSkyColor; s_AmbEqWas = RenderSettings.ambientEquatorColor; s_AmbGroundWas = RenderSettings.ambientGroundColor;
            s_Sun = Sun();
            s_SunColWas = s_Sun != null ? s_Sun.color : Color.white;
        }

        /// <summary>The map is going: put it all back (unless another theme map's sky went up this same frame).</summary>
        public static void End()
        {
            if (!s_Saved || Time.frameCount == s_ApplyFrame) return;
            s_Saved = false;
            s_WantLight = false;
            RenderSettings.skybox = s_SkyWas;
            RenderSettings.fog = s_FogWas; RenderSettings.fogMode = s_FogModeWas; RenderSettings.fogColor = s_FogColWas;
            RenderSettings.fogStartDistance = s_FogStartWas; RenderSettings.fogEndDistance = s_FogEndWas; RenderSettings.fogDensity = s_FogDensityWas;
            RenderSettings.ambientMode = s_AmbModeWas; RenderSettings.ambientSkyColor = s_AmbSkyWas; RenderSettings.ambientEquatorColor = s_AmbEqWas; RenderSettings.ambientGroundColor = s_AmbGroundWas;
            if (s_Sun != null) s_Sun.color = s_SunColWas;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>The procedural sky, tinted (a copy: the original is put back in End).</summary>
        public static void Skybox(Color tint, Color ground, float exposure, float thickness)
        {
            var src = s_SkyWas != null ? s_SkyWas : RenderSettings.skybox;
            if (src == null) return;
            var m = new Material(src) { name = "theme sky" };
            if (m.HasProperty("_SkyTint")) m.SetColor("_SkyTint", tint);
            if (m.HasProperty("_GroundColor")) m.SetColor("_GroundColor", ground);
            if (m.HasProperty("_Exposure")) m.SetFloat("_Exposure", exposure);
            if (m.HasProperty("_AtmosphereThickness")) m.SetFloat("_AtmosphereThickness", thickness);
            RenderSettings.skybox = m;
        }

        // what the map wants (Keep puts it back if something else changed it: the menu's space scenes, the lobby)
        static bool s_WantFog;
        static Color s_WantFogC, s_WantSun, s_WantSky, s_WantEq, s_WantGround;
        static float s_WantFogS, s_WantFogE;
        static bool s_WantLight;

        public static void Fog(Color c, float start, float end)
        {
            s_WantFog = true; s_WantFogC = c; s_WantFogS = start; s_WantFogE = end;
            RenderSettings.fog = true;
            // squared exponential, not linear: the trees' Painted shader works its fog out from the fragment's depth, which
            // in linear mode fogged every painted thing (the ground, the props) solid fog colour
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = c;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = end;
        }

        /// <summary>The fog's density: about half-hidden at the far distance asked for.</summary>
        static float FogDensity => 0.85f / Mathf.Max(50f, s_WantFogE);

        public static void NoFog() { s_WantFog = false; RenderSettings.fog = false; }

        public static void Lighting(Color sun, Color ambSky, Color ambEq, Color ambGround)
        {
            s_WantLight = true; s_WantSun = sun; s_WantSky = ambSky; s_WantEq = ambEq; s_WantGround = ambGround;
            var l = s_Sun != null ? s_Sun : Sun();
            if (l != null) l.color = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambSky;
            RenderSettings.ambientEquatorColor = ambEq;
            RenderSettings.ambientGroundColor = ambGround;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Every frame (the maps' ClientTick): put the map's fog and light back if something else changed them
        /// (not while the menu's space scenes or the sudden death arena are up, or the lobby has the lights dimmed).</summary>
        public static void Keep(Camera cam)
        {
            if (!s_Saved || SkyHidden(cam)) return;
            if (RenderSettings.fog != s_WantFog) RenderSettings.fog = s_WantFog;
            if (s_WantFog && (RenderSettings.fogColor != s_WantFogC || RenderSettings.fogMode != FogMode.ExponentialSquared || RenderSettings.fogDensity != FogDensity))
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogColor = s_WantFogC; RenderSettings.fogDensity = FogDensity;
                RenderSettings.fogStartDistance = s_WantFogS; RenderSettings.fogEndDistance = s_WantFogE;
            }
            if (!s_WantLight || ShipLobby.Active) return;
            if (s_Sun != null && s_Sun.color != s_WantSun) s_Sun.color = s_WantSun;
            if (RenderSettings.ambientMode != AmbientMode.Trilight || RenderSettings.ambientSkyColor != s_WantSky || RenderSettings.ambientEquatorColor != s_WantEq || RenderSettings.ambientGroundColor != s_WantGround)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = s_WantSky; RenderSettings.ambientEquatorColor = s_WantEq; RenderSettings.ambientGroundColor = s_WantGround;
                DynamicGI.UpdateEnvironment();
            }
        }

        // ---------------------------------------------------------------- rideable creatures (ThemeMap.BuildMount) and bushes
        /// <summary>A body part shots can hit: a box collider on the players' hitbox layer (it bumps nothing). Not on a ghost.</summary>
        public static GameObject MHit(GameObject g, Material ghost)
        {
            if (ghost != null) return g;
            g.AddComponent<BoxCollider>();
            g.layer = PlayerNet.HitboxLayer;
            return g;
        }

        /// <summary>An empty transform (a pivot: a leg's hip, the neck, the tail's root).</summary>
        public static Transform MPivot(Transform parent, string name, Vector3 at)
        {
            var p = new GameObject(name).transform;
            p.SetParent(parent, false);
            p.localPosition = at;
            return p;
        }

        /// <summary>The head: its own solid box named "horse head" (a hit there does double damage), like the horse's.</summary>
        public static GameObject MHead(Transform neck, Material ghost, Color c, Vector3 at, Vector3 size, Vector3 euler = default)
        {
            var h = Art.Box(neck, c, at, size, euler, ghost == null);
            h.name = "horse head";
            return h;
        }

        /// <summary>The saddle, shown once it's saddled (seat on top at seatY, like the horse's 1.47 m), with its "blanket"
        /// (tinted the saddler's team colour) hanging down both sides of a body `bodyW` wide.</summary>
        public static Transform MSaddle(Transform t, Material ghost, Color leather, float bodyW = 0.6f, float seatY = 1.47f, float z = -0.05f)
        {
            var sd = MPivot(t, "saddle", Vector3.zero);
            MHit(Art.Box(sd, leather, new Vector3(0, seatY, z), new Vector3(bodyW + 0.04f, 0.08f, 0.55f)), ghost);
            Art.Box(sd, leather, new Vector3(0, seatY + 0.08f, z + 0.25f), new Vector3(0.3f, 0.12f, 0.08f));
            var blanket = Art.Box(sd, new Color(0.8f, 0.2f, 0.15f), new Vector3(0, seatY - 0.27f, z), new Vector3(bodyW + 0.06f, 0.5f, 0.45f));
            blanket.name = "blanket";
            return sd;
        }

        /// <summary>A unicorn's horn (gold, spiralled) on a head at `at`, leaning forward.</summary>
        public static void MHorn(Transform head, Vector3 at, float len = 0.42f)
        {
            var gold = new Color(1f, 0.85f, 0.42f);
            var h = Art.Part(head, Art.Cone, gold, at, new Vector3(0.11f, len, 0.11f), new Vector3(30f, 0, 0), false, null, "horn");
            Art.Part(h.transform, Art.Cylinder, gold * 1.1f, new Vector3(0, 0.25f, 0), new Vector3(1.15f, 0.04f, 1.15f), default, false, null, "horn ring");
        }

        /// <summary>A bunch of `n` round berries (a little faceted ball each) on the outside of a clump at c (radii rad).</summary>
        public static void Berries(MeshKit k, Vector3 c, Vector3 rad, int n, float size, Color col, System.Random rng, float minUp = -0.1f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * 6.283f, el = minUp + (float)rng.NextDouble() * (1.2f - minUp);
                var d = new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
                var at = c + Vector3.Scale(d, rad) * 1.02f;
                float s = size * (0.85f + (float)rng.NextDouble() * 0.3f);
                var cc = (float)rng.NextDouble() < 0.25f ? col * 0.8f : col; cc.a = 1f;
                Ball(k, at, Vector3.one * s, Quaternion.identity, cc, 0);
                Ball(k, at + d * s * 0.55f + Vector3.up * s * 0.3f, Vector3.one * s * 0.32f, Quaternion.identity, Color.Lerp(cc, Color.white, 0.55f), 0); // (a shine)
            }
        }

        // ---------------------------------------------------------------- moving things, launching, climbing
        /// <summary>A clock that is the same on every peer (the server's time once connected, else the local one), for things
        /// that move by themselves and that players stand on or time a jump by.</summary>
        public static float NetTime
        {
            get
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm != null && nm.IsListening) return (float)(nm.ServerTime.Time % 86400.0);
                return Time.time;
            }
        }

        /// <summary>The velocity for PlayerController.Launch that lands you at `to` from `from` after `flight` seconds (the
        /// push along the ground fades at 3 m/s² in the air, gravity is Cfg.Gravity).</summary>
        public static Vector3 LaunchVelocity(Vector3 from, Vector3 to, float flight)
        {
            var d = to - from;
            var flat = new Vector3(d.x, 0f, d.z);
            float dist = flat.magnitude;
            float vh = (dist + 1.5f * flight * flight) / flight;
            float vy = (d.y + 0.5f * Cfg.Gravity * flight * flight) / flight;
            return (dist > 0.01f ? flat / dist * vh : Vector3.zero) + Vector3.up * vy;
        }

        /// <summary>A climbable ladder (the game's Ladder): rails and rungs into `k`, a solid backing behind them, and the
        /// climbing volume in front. foot: at its bottom, on the face it stands against; faceOut: away from that face (where
        /// you climb); topY: the floor you get off onto at the top (you're pushed toward the face, over it). The parent must
        /// sit at the world's origin, unturned.</summary>
        public static void Ladder(Transform parent, MeshKit k, Vector3 foot, float topY, Vector3 faceOut, Color c)
        {
            faceOut.y = 0f;
            faceOut.Normalize();
            float len = topY - foot.y + 0.9f;
            var rot = Quaternion.LookRotation(-faceOut);
            var right = rot * Vector3.right;
            var b = foot + faceOut * 0.14f;
            for (int s = -1; s <= 1; s += 2)
                k.Box(b + right * (0.36f * s) + Vector3.up * (len * 0.5f), new Vector3(0.09f, len, 0.09f), rot, c, MeshKit.All);
            for (float y = 0.35f; y < len - 0.1f; y += 0.42f)
                k.Box(b + Vector3.up * y, new Vector3(0.72f, 0.07f, 0.07f), rot, c * 0.85f, MeshKit.All);
            // the backing (solid: you lean on it), up to just under the floor at the top
            float sh = Mathf.Max(0.2f, topY - foot.y - 0.05f);
            k.Box(foot + faceOut * 0.05f + Vector3.up * (sh * 0.5f), new Vector3(0.9f, sh, 0.1f), rot, c * 0.6f, MeshKit.All);
            var body = new GameObject("ladder body");
            body.transform.SetParent(parent, false);
            body.transform.localPosition = foot + faceOut * 0.06f + Vector3.up * (sh * 0.5f);
            body.transform.localRotation = rot;
            body.AddComponent<BoxCollider>().size = new Vector3(0.9f, sh, 0.16f);
            var go = new GameObject("ladder");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = foot + faceOut * 0.5f + Vector3.up * (len * 0.5f);
            go.transform.localRotation = rot;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1.1f, len, 0.9f);
            go.AddComponent<Ladder>().TopLocalY = topY;
        }

        /// <summary>A soft round cloud of blossom (cherry, peach...): a big ball, smaller ones bulging out round it, a darker
        /// flatter one under it and a few drooping clumps (droop: more of them, longer - a weeping branch). Smooth its kit's
        /// normals afterwards (MeshKit.SmoothNormals) and it reads as round, not faceted.</summary>
        public static void Blossom(MeshKit k, Vector3 c, float s, System.Random rng, Color[] cols, Color under, bool droop = false)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Ball(k, c, new Vector3(1.5f, 1.05f, 1.5f) * s, Quaternion.Euler(0, R(0f, 360f), 0), cols[rng.Next(cols.Length)], 1, 0.1f, rng.Next(256));
            Ball(k, c + Vector3.down * 0.42f * s, new Vector3(1.3f, 0.55f, 1.3f) * s, Quaternion.identity, under, 0);
            int n = 5 + rng.Next(3);
            float spin = R(0f, 6.28f);
            for (int i = 0; i < n; i++)
            {
                float a = spin + i * 6.283f / n + R(-0.3f, 0.3f);
                var o = new Vector3(Mathf.Cos(a) * R(1.0f, 1.3f), R(-0.25f, 0.55f), Mathf.Sin(a) * R(1.0f, 1.3f)) * s;
                float r = R(0.6f, 0.9f) * s;
                Ball(k, c + o, new Vector3(r, r * 0.82f, r), Quaternion.Euler(0, R(0f, 360f), 0), cols[rng.Next(cols.Length)], 0, 0.08f, rng.Next(256));
            }
            int hang = (droop ? 4 : 2) + rng.Next(2);
            for (int i = 0; i < hang; i++)
            {
                float a = R(0f, 6.28f), d = R(0.5f, 1.3f) * s;
                var at = c + new Vector3(Mathf.Cos(a) * d, -R(0.55f, droop ? 1.6f : 0.9f) * s, Mathf.Sin(a) * d);
                float r = R(0.28f, 0.42f) * s;
                Ball(k, at, new Vector3(r, r * 1.25f, r), Quaternion.identity, cols[rng.Next(cols.Length)], 0);
            }
        }

        /// <summary>Sky dressing (moons, stars, space) is hidden while the menu's space scenes or the sudden death arena are up.</summary>
        public static bool SkyHidden(Camera cam) => cam == null || MenuSpace.Showing || MenuSpace.CrashUp || SpaceArena.NearArena(cam.transform.position);
    }
}
