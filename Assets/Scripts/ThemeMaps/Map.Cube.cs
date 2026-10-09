using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// THE CUBE: one huge empty room the size of the map - a grey tiled floor (3 m tiles: the building grid), grey
    /// grid-tiled walls and ceiling, glowing green strips along the top edges (and faintly along the floor and up the
    /// corners) - with synthetic trees in it (blocky brown trunks and branches, green cube crowns) and nothing else: no
    /// mountains, no boulders, no bushes, no rocks. Nothing in the middle but the ball (and the sign). The bases are the
    /// game's own; the horses are Cube Walkers (blocky robots).
    /// The ceiling is lower than where the airdrop ships hover, so they stay out of sight above it (their beam comes
    /// down through it). Walls and ceiling cast no shadows, so the sun still lights the room.
    /// The trees are synthetic and it shows, just a little: now and then a piece of one (a trunk segment, a branch's
    /// clump of cubes, the crown) flickers see-through for a moment and twitches a few centimetres, and turning your
    /// camera fast can set a piece of a near tree flickering too. Only how they look - their colliders never move.
    /// </summary>
    public class CubeMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Cube;
        public override string Label => "Cube";
        public override string Blurb => "One giant empty tiled room with glowing edges - and synthetic trees. Nothing else.";
        public override bool Mountains => false;
        public override bool Dome => false;

        static float Half => Cfg.MapHalf;
        /// <summary>The ceiling: over the ball's drop (40 m), under the airdrop ships (they hover 70 m up or more).</summary>
        const float Ceiling = 48f;
        const float Tile = 3f;

        static readonly Color k_Floor = new Color(0.56f, 0.57f, 0.58f), k_Wall = new Color(0.68f, 0.68f, 0.69f), k_Roof = new Color(0.62f, 0.62f, 0.63f);
        static readonly Color k_Neon = new Color(0.45f, 1f, 0.5f);

        public override float Height(float x, float z) => 0f;
        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1.3f : 0f;
        public override Color LeafTint(Color leaf) => new Color(0.36f, 0.66f, 0.3f);
        public override string MountName => "Cube Walker";
        /// <summary>Nothing in the middle: just the ball.</summary>
        public override bool BuildCentre(Transform root) => true;

        static Texture2D s_Grid;

        /// <summary>A white tile with a dark line along two of its edges (repeated, that's the grid).</summary>
        static Texture2D Grid
        {
            get
            {
                if (s_Grid != null) return s_Grid;
                const int n = 128, w = 3;
                s_Grid = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "cube grid", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool line = x < w || y < w;
                    byte v = line ? (byte)62 : (byte)255;
                    if (!line && (x < w + 2 || y < w + 2)) v = 200; // (a soft edge)
                    px[y * n + x] = new Color32(v, v, v, 255);
                }
                s_Grid.SetPixels32(px);
                s_Grid.Apply(true);
                return s_Grid;
            }
        }

        static Material GridMat(Color c, float smooth)
        {
            var m = Art.NewMat(c);
            m.name = "cube grid";
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Grid);
            m.mainTexture = Grid;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        /// <summary>A quad with UVs in tiles (u along `along`, v along `up` from corner a), facing `outward`.</summary>
        static void Face(List<Vector3> v, List<Vector3> nr, List<Vector2> uv, List<int> t, Vector3 a, Vector3 along, Vector3 up, Vector3 outward, Vector2 uvAt)
        {
            int k = v.Count;
            Vector3 b = a + along, c = a + along + up, d = a + up;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int i = 0; i < 4; i++) nr.Add(outward.normalized);
            float lu = along.magnitude / Tile, lv = up.magnitude / Tile;
            uv.Add(uvAt); uv.Add(uvAt + new Vector2(lu, 0)); uv.Add(uvAt + new Vector2(lu, lv)); uv.Add(uvAt + new Vector2(0, lv));
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f) { t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3); }
            else { t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2); }
        }

        static GameObject MeshObj(Transform root, string name, List<Vector3> v, List<Vector3> nr, List<Vector2> uv, List<int> t, Material mat)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetNormals(nr); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<OwnedMesh>().Mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        public override void BuildGround(Transform root)
        {
            m_Trees.Clear();
            float half = Half;
            // the floor: tiles lined up with the building grid (and a solid slab under it)
            float s = Mathf.Ceil((half + 2f) / Tile) * Tile;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Face(v, nr, uv, t, new Vector3(-s, 0, -s), new Vector3(2 * s, 0, 0), new Vector3(0, 0, 2 * s), Vector3.up, new Vector2(-s / Tile, -s / Tile));
            var floor = MeshObj(root, "Ground", v, nr, uv, t, GridMat(k_Floor, 0.45f));
            var col = floor.AddComponent<BoxCollider>();
            col.center = new Vector3(0, -0.5f, 0);
            col.size = new Vector3(2 * s, 1f, 2 * s);
            floor.AddComponent<GroundMarker>();
            floor.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // the walls (their faces where the map's edge is) and the ceiling
            v.Clear(); nr.Clear(); uv.Clear(); t.Clear();
            for (int side = 0; side < 4; side++)
            {
                var q = Quaternion.Euler(0, 90f * side, 0);
                var inward = q * Vector3.forward;               // side 0: the -z wall, facing +z
                var a = q * new Vector3(-half, 0, -half);
                Face(v, nr, uv, t, a, q * new Vector3(2 * half, 0, 0), Vector3.up * Ceiling, inward, Vector2.zero);
            }
            var walls = MeshObj(root, "cube walls", v, nr, uv, t, GridMat(k_Wall, 0.1f));
            walls.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            v.Clear(); nr.Clear(); uv.Clear(); t.Clear();
            Face(v, nr, uv, t, new Vector3(-half, Ceiling, -half), new Vector3(2 * half, 0, 0), new Vector3(0, 0, 2 * half), Vector3.down, Vector2.zero);
            var roof = MeshObj(root, "cube ceiling", v, nr, uv, t, GridMat(k_Roof, 0.1f));
            roof.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // the glowing strips: right round the top, faint along the floor and up the corners
            var top = new MeshKit(); var low = new MeshKit();
            for (int side = 0; side < 4; side++)
            {
                var q = Quaternion.Euler(0, 90f * side, 0);
                top.Box(q * new Vector3(0, Ceiling - 0.5f, -half + 0.5f), new Vector3(2 * half, 1f, 1f), q, Color.white, MeshKit.All);
                low.Box(q * new Vector3(0, 0.12f, -half + 0.12f), new Vector3(2 * half, 0.24f, 0.24f), q, Color.white, MeshKit.All);
                top.Box(q * new Vector3(-half + 0.3f, Ceiling * 0.5f, -half + 0.3f), new Vector3(0.6f, Ceiling, 0.6f), q, Color.white, MeshKit.All);
            }
            ThemeKitB.Spawn(root, "neon top", top, ThemeKitB.Glow(k_Neon, 2.4f), false);
            ThemeKitB.Spawn(root, "neon floor", low, ThemeKitB.Glow(k_Neon, 1.3f), false);
        }

        static readonly Color[] k_Bark = { new Color(0.42f, 0.27f, 0.16f), new Color(0.35f, 0.22f, 0.13f), new Color(0.49f, 0.32f, 0.19f) };
        static readonly Color[] k_Green = { new Color(0.3f, 0.58f, 0.24f), new Color(0.38f, 0.68f, 0.28f), new Color(0.24f, 0.48f, 0.2f), new Color(0.46f, 0.74f, 0.32f) };

        /// <summary>A rounded clump of cubes (a voxel ball: the middle, its faces and edges - no corners), each its own green.</summary>
        static void VoxelClump(MeshKit k, Vector3 c, float cs, Quaternion yaw, System.Random rng, bool flatBottom)
        {
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                int far = Mathf.Abs(x) + Mathf.Abs(y) + Mathf.Abs(z);
                if (far == 3) continue;                      // (no corners: it reads round)
                if (flatBottom && y < 0 && far > 1) continue; // (a flatter underside)
                if (far == 2 && rng.NextDouble() < 0.2) continue;
                float s = cs * (far == 0 ? 1.05f : 0.92f + (float)rng.NextDouble() * 0.12f);
                k.Box(c + yaw * (new Vector3(x, y * 0.85f, z) * cs), Vector3.one * s, yaw, k_Green[rng.Next(k_Green.Length)], MeshKit.All);
            }
        }

        /// <summary>The trunk's radius where the X goes: round, straight and this thick from the ground to 3 m.</summary>
        const float TrunkR = 0.34f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        /// <summary>A synthetic tree: a round segmented brown trunk (straight to 3 m) on flat root blocks, square branches above
        /// that, crowns of green cubes. Each piece is its own renderer so a piece can glitch on its own.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 404);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var look = new GameObject("synthetic look").transform;
            look.SetParent(tr, false);
            var parts = new List<Part>();
            void Add(MeshKit kit, string name, Color tint, Material mat = null, bool shadows = true)
            {
                var r = ThemeKitB.Spawn(look, name, kit, mat, shadows).GetComponent<Renderer>();
                if (r == null) return;
                tint.a = 0.32f;
                parts.Add(new Part { T = r.transform, R = r, Mat = r.sharedMaterial, See = Art.Ghost(tint), Glow = mat != null });
            }
            var yaw = Quaternion.Euler(0, R(0f, 90f), 0);
            // flat root blocks splayed round the foot (no higher than 0.25 m: the trunk is bare where the X goes)
            var roots = new MeshKit();
            for (int i = 0; i < 4; i++)
            {
                var q = yaw * Quaternion.Euler(0, i * 90f + R(-12f, 12f), 0);
                roots.Box(q * new Vector3(0, 0.12f, 0.5f), new Vector3(0.34f, 0.24f, 0.62f), q, k_Bark[1], MeshKit.All);
            }
            Add(roots, "synthetic roots", k_Bark[1]);
            // the trunk: round stacked segments - two of them straight and TrunkR thick up to 3 m, then narrowing to the top
            const float Straight = 3f;
            float spin = R(0f, 6.28f);
            float top = Mathf.Max(h, Straight + 1.2f);
            float[] ys = { 0f, 1.5f, Straight, Straight + (top - Straight) * 0.5f, top };
            for (int i = 0; i < 4; i++)
            {
                var seg = new MeshKit();
                float r0 = i < 2 ? TrunkR : Mathf.Lerp(TrunkR, 0.22f, (i - 2) / 2f), r1 = i < 2 ? TrunkR : Mathf.Lerp(TrunkR, 0.22f, (i - 1) / 2f);
                ThemeKitB.Cyl(seg, Vector3.up * ys[i], Vector3.up * ys[i + 1], r0, r1, 12, k_Bark[i % k_Bark.Length], i == 0, true, spin);
                Add(seg, "synthetic trunk " + i, k_Bark[i % k_Bark.Length]);
            }
            // branches: square arms out and up from the trunk above 3 m, a clump of cubes on each
            int nb = 2 + rng.Next(2);
            float bspin = R(0f, 360f);
            for (int b = 0; b < nb; b++)
            {
                var k = new MeshKit();
                var dir = Quaternion.Euler(0, bspin + b * 360f / nb + R(-20f, 20f), 0) * Vector3.forward;
                var from = Vector3.up * Mathf.Min(top - 0.3f, Mathf.Max(Straight + 0.25f, top * R(0.6f, 0.85f)));
                var to = from + dir * R(1.3f, 1.9f) + Vector3.up * R(0.7f, 1.2f);
                var along = to - from;
                k.Box((from + to) * 0.5f, new Vector3(0.3f, 0.3f, along.magnitude + 0.2f), Quaternion.LookRotation(along), k_Bark[2], MeshKit.All);
                VoxelClump(k, to + Vector3.up * 0.45f, R(0.62f, 0.78f), yaw * Quaternion.Euler(0, b * 25f, 0), rng, true);
                Add(k, "synthetic branch " + b, k_Green[1]);
            }
            // the crown on top
            var crown = new MeshKit(); var g = new MeshKit();
            float cs = R(0.95f, 1.15f);
            var ct = Vector3.up * (top + cs * 0.55f);
            VoxelClump(crown, ct, cs, yaw, rng, false);
            Add(crown, "synthetic crown", k_Green[0]);
            var cap = new MeshKit();
            cap.Box(ct + Vector3.up * cs * 1.5f, Vector3.one * cs * 0.85f, yaw * Quaternion.Euler(0, 45f, 0), k_Green[3], MeshKit.All);
            Add(cap, "synthetic crown top", k_Green[3]);
            // two little glowing cubes in the leaves (it's synthetic)
            for (int i = 0; i < 2; i++)
                g.Box(ct + yaw * new Vector3((i == 0 ? 1f : -1f) * cs * 1.42f, R(-0.3f, 0.4f) * cs, R(-0.5f, 0.5f) * cs), Vector3.one * 0.22f, yaw * Quaternion.Euler(30f, 45f, 0), Color.white, MeshKit.All);
            Add(g, "synthetic glow", k_Neon, ThemeKitB.Glow(k_Neon, 1.8f), false);
            m_Trees.Add(new Glitchy { Look = look, Parts = parts.ToArray(), Next = Time.time + Random.Range(6f, 40f), BasePos = look.localPosition, BaseRot = look.localRotation, BaseScale = look.localScale });
            return true;
        }

        /// <summary>The game's own berry bush in the Cube's colours: grey-green leaves, cyan berries.</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.42f, 0.5f, 0.44f), new Color(0.2f, 0.9f, 0.95f));
            return true;
        }

        /// <summary>The Cube Walker (the horse here): a blocky grey robot on four square legs, a cube head with a glowing visor
        /// (a unicorn: a white one with a gold horn).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var hull = unicorn ? new Color(0.93f, 0.93f, 0.95f) : new Color(0.5f, 0.51f, 0.53f);
            var hull2 = unicorn ? new Color(0.82f, 0.82f, 0.86f) : new Color(0.36f, 0.37f, 0.39f);
            var joint = new Color(0.2f, 0.2f, 0.22f);
            var eye = ThemeKitB.Glow(unicorn ? new Color(1f, 0.8f, 0.35f) : k_Neon, 2.2f);
            ThemeKitB.MHit(Art.Box(t, hull, new Vector3(0, 1.15f, 0), new Vector3(0.62f, 0.56f, 1.45f)), ghost);
            Art.Box(t, hull2, new Vector3(0, 0.84f, 0), new Vector3(0.5f, 0.1f, 1.3f));
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Box(t, hull2, new Vector3(s * 0.315f, 1.18f, 0.35f), new Vector3(0.02f, 0.3f, 0.5f));
                Art.Box(t, hull2, new Vector3(s * 0.315f, 1.18f, -0.35f), new Vector3(0.02f, 0.3f, 0.5f));
                Art.Box(t, Color.white, new Vector3(s * 0.32f, 1.18f, 0f), new Vector3(0.02f, 0.06f, 1.2f), default, false, eye); // (glow strips)
            }
            // neck + cube head (pivots to "graze")
            var neck = ThemeKitB.MPivot(t, "neck", new Vector3(0, 1.36f, 0.66f));
            ThemeKitB.MHit(Art.Box(neck, joint, new Vector3(0, 0.25f, 0.06f), new Vector3(0.22f, 0.55f, 0.22f), new Vector3(18, 0, 0)), ghost);
            ThemeKitB.MHead(neck, ghost, hull, new Vector3(0, 0.6f, 0.3f), new Vector3(0.42f, 0.38f, 0.5f));
            Art.Box(neck, Color.white, new Vector3(0, 0.64f, 0.556f), new Vector3(0.34f, 0.08f, 0.02f), default, false, eye); // the visor
            Art.Box(neck, hull2, new Vector3(0, 0.46f, 0.5f), new Vector3(0.3f, 0.08f, 0.14f));
            Art.Box(neck, joint, new Vector3(0.12f, 0.88f, 0.22f), new Vector3(0.03f, 0.22f, 0.03f));
            Art.Box(neck, Color.white, new Vector3(0.12f, 1.0f, 0.22f), new Vector3(0.07f, 0.07f, 0.07f), default, false, eye);
            for (int s = -1; s <= 1; s += 2) Art.Box(neck, hull2, new Vector3(s * 0.2f, 0.82f, 0.18f), new Vector3(0.08f, 0.14f, 0.08f));
            if (unicorn) ThemeKitB.MHorn(neck, new Vector3(0, 0.8f, 0.42f));
            head = neck;
            // a cable tail with a cube on the end
            var tl = ThemeKitB.MPivot(t, "tail", new Vector3(0, 1.3f, -0.74f));
            ThemeKitB.MHit(Art.Box(tl, joint, new Vector3(0, -0.22f, -0.05f), new Vector3(0.07f, 0.45f, 0.07f)), ghost);
            Art.Box(tl, Color.white, new Vector3(0, -0.48f, -0.05f), new Vector3(0.14f, 0.14f, 0.14f), new Vector3(0, 45f, 0), false, eye);
            tail = tl;
            // square legs with a knee block and a flat foot
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.22f : 0.22f, z = i < 2 ? 0.55f : -0.55f;
                var leg = ThemeKitB.MPivot(t, "leg", new Vector3(x, 0.9f, z));
                ThemeKitB.MHit(Art.Box(leg, hull2, new Vector3(0, -0.2f, 0), new Vector3(0.18f, 0.4f, 0.18f)), ghost);
                Art.Box(leg, joint, new Vector3(0, -0.43f, 0), new Vector3(0.2f, 0.1f, 0.2f));
                ThemeKitB.MHit(Art.Box(leg, hull, new Vector3(0, -0.64f, 0), new Vector3(0.14f, 0.36f, 0.14f)), ghost);
                Art.Box(leg, joint, new Vector3(0, -0.86f, 0.03f), new Vector3(0.22f, 0.08f, 0.26f));
                legs?.Add(leg);
            }
            saddle = ThemeKitB.MSaddle(t, ghost, new Color(0.18f, 0.18f, 0.2f), 0.62f, 1.47f);
            return true;
        }

        public override void ApplySky()
        {
            ThemeKitB.Begin();
            ThemeKitB.Skybox(new Color(0.78f, 0.78f, 0.8f), new Color(0.6f, 0.6f, 0.61f), 1.1f, 0.6f);
            ThemeKitB.Fog(new Color(0.8f, 0.8f, 0.81f), 70f, Mathf.Max(280f, Half * 3.4f));
            ThemeKitB.Lighting(Color.white, new Color(0.86f, 0.86f, 0.87f), new Color(0.77f, 0.77f, 0.78f), new Color(0.62f, 0.62f, 0.63f));
        }

        // ---------------------------------------------------------------- the glitches (how the trees look only; subtle)
        /// <summary>One piece of a tree: its renderer, its own material and its see-through look.</summary>
        class Part
        {
            public Transform T;
            public Renderer R;
            public Material Mat, See;
            public bool Glow;
            public bool On;
            public float Until, NextFlick;
            public Vector3 Lag; // (local: a tiny smear behind a fast camera turn)
        }
        class Glitchy
        {
            public Transform Look;
            public Part[] Parts;
            public float Next;
            /// <summary>A hard glitch (the tree was just hit) runs until this time (0 = none); the look's own transform to go back to.</summary>
            public float HardUntil, HardNext;
            public Vector3 BasePos, BaseScale;
            public Quaternion BaseRot;
        }
        readonly List<Glitchy> m_Trees = new List<Glitchy>();
        int m_Scan;
        bool m_HaveCam;
        Quaternion m_CamRot;
        Vector3 m_CamPos;
        float m_NextTurnGlitch;

        static void StartGlitch(Part p, float seconds, Vector3 lag)
        {
            if (p == null || p.T == null) return;
            float t = Time.time;
            p.Until = Mathf.Max(p.Until, t + seconds);
            if (lag.sqrMagnitude > p.Lag.sqrMagnitude) p.Lag = lag;
            if (p.On) return;
            p.On = true; p.NextFlick = 0f;
        }

        /// <summary>How long a tree glitches hard after a hit.</summary>
        const float HitGlitch = 0.42f;
        static Material s_SplitA, s_SplitB;

        /// <summary>(every peer: the tree's wood went down) That tree glitches out hard for a moment.</summary>
        public override void TreeHit(Transform visual)
        {
            if (visual == null) return;
            foreach (var g in m_Trees)
            {
                if (g.Look == null || g.Look.parent != visual) continue;
                if (g.HardUntil <= 0f)
                    foreach (var p in g.Parts) if (p.On) EndGlitch(p); // (a little flicker in progress: start clean)
                g.HardUntil = Time.time + HitGlitch;
                g.HardNext = 0f;
                return;
            }
        }

        /// <summary>The hit glitch is over: the tree exactly as it was built.</summary>
        static void EndHard(Glitchy g)
        {
            g.HardUntil = 0f;
            if (g.Look != null)
            {
                g.Look.localPosition = g.BasePos;
                g.Look.localRotation = g.BaseRot;
                g.Look.localScale = g.BaseScale;
            }
            foreach (var p in g.Parts) EndGlitch(p);
        }

        /// <summary>One frame of the hit glitch: the whole tree stutters in scale and jumps a little, every piece is torn
        /// sideways like a slipped slice, flickers see-through / gone, or flashes in a split colour (cyan or magenta).</summary>
        static void HardFrame(Glitchy g, float t)
        {
            if (t < g.HardNext || g.Look == null) return;
            g.HardNext = t + Random.Range(0.025f, 0.05f);
            float left = Mathf.Clamp01((g.HardUntil - t) / HitGlitch); // (1 at the hit, fading to 0: it settles)
            float amp = 0.35f + 0.65f * left;
            if (s_SplitA == null) s_SplitA = Art.Ghost(new Color(0.1f, 1f, 1f, 0.55f));
            if (s_SplitB == null) s_SplitB = Art.Ghost(new Color(1f, 0.15f, 0.85f, 0.55f));
            // the whole tree: a scale stutter and a jolt
            g.Look.localScale = Vector3.Scale(g.BaseScale, Random.value < 0.35f ? Vector3.one
                : new Vector3(1f + Random.Range(-0.14f, 0.18f) * amp, 1f + Random.Range(-0.1f, 0.12f) * amp, 1f + Random.Range(-0.14f, 0.18f) * amp));
            g.Look.localPosition = g.BasePos + new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.12f, 0.12f)) * amp;
            g.Look.localRotation = g.BaseRot * Quaternion.Euler(0f, Random.Range(-6f, 6f) * amp, 0f);
            // every piece: a slice torn sideways, and a look
            float split = Random.Range(0.08f, 0.22f) * amp;
            var axis = Random.value < 0.5f ? Vector3.right : Vector3.forward;
            foreach (var p in g.Parts)
            {
                if (p.T == null) continue;
                p.On = true;
                p.Until = g.HardUntil;
                float k = Random.value;
                p.R.enabled = k > 0.12f;
                if (p.Glow) p.R.sharedMaterial = p.Mat;
                else p.R.sharedMaterial = k < 0.3f ? p.See : k < 0.45f ? s_SplitA : k < 0.6f ? s_SplitB : p.Mat;
                float side = p.R.sharedMaterial == s_SplitA ? -1f : p.R.sharedMaterial == s_SplitB ? 1f : Random.Range(-1f, 1f);
                p.T.localPosition = Random.value < 0.25f ? Vector3.zero
                    : axis * side * (split + Random.Range(0f, 0.18f) * amp) + new Vector3(0f, Random.Range(-0.06f, 0.06f) * amp, 0f);
            }
        }

        static void EndGlitch(Part p)
        {
            p.On = false; p.Lag = Vector3.zero;
            if (p.T == null) return;
            p.T.localPosition = Vector3.zero;
            p.R.sharedMaterial = p.Mat;
            p.R.enabled = true;
        }

        /// <summary>One or two pieces of the tree (rarely the glow cubes) flicker for a moment.</summary>
        static void GlitchSome(Glitchy g, float seconds, Vector3 lag, int count)
        {
            if (g.Parts.Length == 0) return;
            for (int n = 0; n < count; n++)
            {
                var p = g.Parts[Random.Range(0, g.Parts.Length)];
                if (p.Glow) p = g.Parts[Random.Range(0, g.Parts.Length)];
                StartGlitch(p, seconds * Random.Range(0.7f, 1.2f), lag);
            }
        }

        public override void ClientTick()
        {
            var cam = Camera.main;
            ThemeKitB.Keep(cam);
            if (cam == null || m_Trees.Count == 0) return;
            float t = Time.time, dt = Mathf.Max(Time.deltaTime, 1e-4f);
            for (int i = m_Trees.Count - 1; i >= 0; i--) if (m_Trees[i].Look == null) m_Trees.RemoveAt(i); // (chopped down)
            if (m_Trees.Count == 0) return;

            // a tree just hit: glitching out hard (then put back exactly as it was)
            foreach (var g in m_Trees)
            {
                if (g.HardUntil <= 0f) continue;
                if (t >= g.HardUntil) EndHard(g);
                else HardFrame(g, t);
            }

            // the camera turning really fast: now and then a piece of a near tree in view flickers, smeared a touch behind
            var ct = cam.transform;
            var cr = ct.rotation; var cp = ct.position;
            if (m_HaveCam && (cp - m_CamPos).magnitude < 8f) // (not on a respawn / teleport)
            {
                float turn = Quaternion.Angle(m_CamRot, cr) / dt;
                float yaw = Mathf.DeltaAngle(m_CamRot.eulerAngles.y, cr.eulerAngles.y);
                if (turn > 320f && turn < 4000f && t >= m_NextTurnGlitch)
                {
                    m_NextTurnGlitch = t + 0.35f;
                    var smear = ct.right * -Mathf.Sign(yaw) * 0.08f;
                    var fwd = ct.forward;
                    foreach (var g in m_Trees)
                    {
                        var d = g.Look.position - cp;
                        float dist = d.magnitude;
                        if (dist > 40f || Vector3.Dot(fwd, d / Mathf.Max(dist, 0.01f)) < 0.5f) continue;
                        if (Random.value > 0.12f) continue;
                        var lag = g.Look.parent != null ? g.Look.parent.InverseTransformVector(smear) : smear;
                        GlitchSome(g, Random.Range(0.08f, 0.18f), lag, 1);
                    }
                }
            }
            m_HaveCam = true; m_CamRot = cr; m_CamPos = cp;

            // now and then, one tree by itself (a few looked at each frame): a piece or two flickers for a moment
            int scan = Mathf.Min(4, m_Trees.Count);
            for (int n = 0; n < scan; n++)
            {
                m_Scan = (m_Scan + 1) % m_Trees.Count;
                var g = m_Trees[m_Scan];
                if (t < g.Next) continue;
                g.Next = t + Random.Range(12f, 45f);
                if (Random.value < 0.5f) GlitchSome(g, Random.Range(0.12f, 0.4f), Vector3.zero, Random.value < 0.3f ? 2 : 1);
            }

            // the flickering pieces: every few hundredths of a second see-through or solid (once in a while gone for a
            // frame), twitching a few centimetres
            foreach (var g in m_Trees)
            {
            if (g.HardUntil > 0f) continue; // (glitching out from a hit: above)
            foreach (var p in g.Parts)
            {
                if (!p.On || p.T == null) continue;
                if (t >= p.Until) { EndGlitch(p); continue; }
                if (t < p.NextFlick) continue;
                p.NextFlick = t + Random.Range(0.03f, 0.07f);
                float k = Random.value;
                p.R.enabled = k > 0.08f;
                p.R.sharedMaterial = k < 0.6f && !p.Glow ? p.See : p.Mat;
                p.T.localPosition = p.Lag + (Random.value < 0.5f ? new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(-0.02f, 0.03f), Random.Range(-0.05f, 0.05f)) : Vector3.zero);
            }
            }
        }

        public override void Cleanup() => ThemeKitB.End();
    }
}
