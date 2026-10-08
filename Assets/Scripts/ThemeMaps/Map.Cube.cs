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

        /// <summary>A synthetic tree: a blocky brown trunk on splayed root blocks, square branches, round crowns of green cubes.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 404);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var k = new MeshKit(); var g = new MeshKit();
            var yaw = Quaternion.Euler(0, R(0f, 90f), 0);
            // root blocks splayed round the foot
            for (int i = 0; i < 4; i++)
            {
                var q = yaw * Quaternion.Euler(0, i * 90f + R(-12f, 12f), 0);
                k.Box(q * new Vector3(0, 0.2f, 0.5f), new Vector3(0.34f, 0.4f, 0.62f), q * Quaternion.Euler(-12f, 0, 0), k_Bark[1], MeshKit.All);
            }
            // the trunk: stacked square blocks, narrowing, each turned a little
            const int segs = 4;
            for (int i = 0; i < segs; i++)
            {
                float y0 = h * i / segs, y1 = h * (i + 1) / segs, w = Mathf.Lerp(0.72f, 0.48f, i / (segs - 1f));
                k.Box(new Vector3(0, (y0 + y1) * 0.5f, 0), new Vector3(w, y1 - y0 + 0.05f, w), yaw * Quaternion.Euler(0, i * 7f, 0), k_Bark[i % k_Bark.Length], MeshKit.All);
            }
            // branches: square arms out and up from the upper trunk, a clump of cubes on each
            int nb = 2 + rng.Next(2);
            float spin = R(0f, 360f);
            for (int b = 0; b < nb; b++)
            {
                var dir = Quaternion.Euler(0, spin + b * 360f / nb + R(-20f, 20f), 0) * Vector3.forward;
                var from = Vector3.up * (h * R(0.55f, 0.78f));
                var to = from + dir * R(1.3f, 1.9f) + Vector3.up * R(0.7f, 1.2f);
                var along = to - from;
                k.Box((from + to) * 0.5f, new Vector3(0.3f, 0.3f, along.magnitude + 0.2f), Quaternion.LookRotation(along), k_Bark[2], MeshKit.All);
                VoxelClump(k, to + Vector3.up * 0.45f, R(0.62f, 0.78f), yaw * Quaternion.Euler(0, b * 25f, 0), rng, true);
            }
            // the crown on top
            float cs = R(0.95f, 1.15f);
            var top = Vector3.up * (h + cs * 0.55f);
            VoxelClump(k, top, cs, yaw, rng, false);
            k.Box(top + Vector3.up * cs * 1.5f, Vector3.one * cs * 0.85f, yaw * Quaternion.Euler(0, 45f, 0), k_Green[3], MeshKit.All);
            // two little glowing cubes in the leaves (it's synthetic)
            for (int i = 0; i < 2; i++)
                g.Box(top + yaw * new Vector3((i == 0 ? 1f : -1f) * cs * 1.42f, R(-0.3f, 0.4f) * cs, R(-0.5f, 0.5f) * cs), Vector3.one * 0.22f, yaw * Quaternion.Euler(30f, 45f, 0), Color.white, MeshKit.All);
            ThemeKitB.Spawn(tr, "synthetic tree", k, null, true);
            ThemeKitB.Spawn(tr, "synthetic glow", g, ThemeKitB.Glow(k_Neon, 1.8f), false);
            return true;
        }

        /// <summary>A bush of green cubes with red cube berries on it (there are no bushes here unless that changes).</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            var rng = new System.Random(seed * 31 + 5);
            var k = new MeshKit();
            const float S = ResourceNode.BushSize;
            var yaw = Quaternion.Euler(0, (float)rng.NextDouble() * 90f, 0);
            VoxelClump(k, Vector3.up * 0.42f * S, 0.36f * S, yaw, rng, true);
            var red = new Color(0.85f, 0.1f, 0.16f);
            for (int i = 0; i < 22; i++)
            {
                int face = rng.Next(5);
                var n = face == 0 ? Vector3.right : face == 1 ? Vector3.left : face == 2 ? Vector3.forward : face == 3 ? Vector3.back : Vector3.up;
                var t = Vector3.Cross(n, Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up);
                var b = Vector3.Cross(n, t);
                var p = n * 0.58f + t * ((float)rng.NextDouble() - 0.5f) * 0.9f + b * ((float)rng.NextDouble() - 0.5f) * 0.9f;
                float s = 0.13f + (float)rng.NextDouble() * 0.04f;
                k.Box(Vector3.up * 0.42f * S + yaw * (p * S), Vector3.one * s * S, yaw * Quaternion.Euler(0, 45f, 0), red, MeshKit.All);
            }
            ThemeKitB.Spawn(tr, "cube bush", k, null, true);
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

        public override void ClientTick() => ThemeKitB.Keep(Camera.main);

        public override void Cleanup() => ThemeKitB.End();
    }
}
