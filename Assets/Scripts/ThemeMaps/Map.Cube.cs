using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// THE CUBE: one huge empty room the size of the map - a light blue tiled floor (3 m tiles: the building grid), white
    /// grid-tiled walls and ceiling, glowing cyan strips along the top edges (and faintly along the floor and up the
    /// corners) - with synthetic trees in it (white trunks with cyan rings, teal cube crowns) and nothing else: no
    /// mountains, no boulders, no bushes, no rocks. The bases, the crash site and the sign are the game's own.
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

        static readonly Color k_Floor = new Color(0.5f, 0.8f, 0.9f), k_Wall = new Color(0.9f, 0.91f, 0.93f), k_Roof = new Color(0.82f, 0.83f, 0.86f);
        static readonly Color k_Neon = new Color(0.3f, 0.92f, 1f);

        public override float Height(float x, float z) => 0f;
        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1.3f : 0f;
        public override Color LeafTint(Color leaf) => new Color(0.4f, 0.85f, 0.9f);

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
            ThemeKitB.Spawn(root, "neon top", top, ThemeKitB.Glow(k_Neon, 3f), false);
            ThemeKitB.Spawn(root, "neon floor", low, ThemeKitB.Glow(k_Neon, 1.6f), false);
        }

        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 404);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var k = new MeshKit(); var g = new MeshKit();
            var white = new Color(0.93f, 0.95f, 0.97f);
            Color[] teal = { new Color(0.35f, 0.82f, 0.88f), new Color(0.56f, 0.9f, 0.95f), new Color(0.25f, 0.66f, 0.8f), new Color(0.82f, 0.95f, 0.98f) };
            var yaw = Quaternion.Euler(0, R(0f, 90f), 0);
            // the base plate and the trunk: a white square column with glowing rings
            k.Box(new Vector3(0, 0.16f, 0), new Vector3(1.5f, 0.14f, 1.5f), yaw, new Color(0.2f, 0.45f, 0.55f), MeshKit.All);
            k.Box(new Vector3(0, h * 0.5f, 0), new Vector3(0.56f, h, 0.56f), yaw, white, MeshKit.All);
            for (float y = 1.2f; y < h - 0.4f; y += 1.4f) g.Box(new Vector3(0, y, 0), new Vector3(0.62f, 0.07f, 0.62f), yaw, Color.white, MeshKit.All);
            int kind = rng.Next(3);
            float cs = R(2.4f, 3.1f);
            var c = new Vector3(0, h + cs * 0.4f, 0);
            if (kind == 0)
            {
                // a big cube with smaller ones round it
                k.Box(c, Vector3.one * cs, yaw, teal[0], MeshKit.All);
                for (int i = 0; i < 4; i++)
                {
                    var q = yaw * Quaternion.Euler(0, i * 90f + 45f, 0);
                    float s2 = R(1f, 1.5f);
                    k.Box(c + q * new Vector3(0, R(-0.9f, 0.4f), cs * 0.62f), Vector3.one * s2, q, teal[1 + rng.Next(3)], MeshKit.All);
                }
            }
            else if (kind == 1)
            {
                // a cube standing on its corner
                var q = yaw * Quaternion.Euler(45f, 0, 35.26f);
                k.Box(c + Vector3.up * cs * 0.3f, Vector3.one * cs * 1.05f, q, teal[2], MeshKit.All);
                k.Box(c + Vector3.up * cs * 0.3f, Vector3.one * cs * 0.6f, q * Quaternion.Euler(0, 45f, 0), teal[3], MeshKit.All);
            }
            else
            {
                // a stack of shrinking cubes, each turned
                float y = h - 0.1f;
                for (int i = 0; i < 4; i++)
                {
                    float s2 = cs * (1f - i * 0.2f);
                    k.Box(new Vector3(0, y + s2 * 0.5f, 0), new Vector3(s2, s2 * 0.6f, s2), yaw * Quaternion.Euler(0, i * 22f, 0), teal[i % teal.Length], MeshKit.All);
                    y += s2 * 0.6f;
                }
                c = new Vector3(0, y, 0);
            }
            // a little glowing cube floating over the top
            g.Box(c + Vector3.up * (cs * 0.9f + 0.4f), Vector3.one * 0.45f, yaw * Quaternion.Euler(30f, 45f, 0), Color.white, MeshKit.All);
            ThemeKitB.Spawn(tr, "synthetic tree", k, null, true);
            ThemeKitB.Spawn(tr, "synthetic glow", g, ThemeKitB.Glow(k_Neon, 2.2f), false);
            return true;
        }

        public override void ApplySky()
        {
            ThemeKitB.Begin();
            ThemeKitB.Skybox(new Color(0.75f, 0.8f, 0.85f), new Color(0.6f, 0.62f, 0.65f), 1.1f, 0.6f);
            ThemeKitB.Fog(new Color(0.84f, 0.88f, 0.93f), 70f, Mathf.Max(280f, Half * 3.4f));
            ThemeKitB.Lighting(Color.white, new Color(0.86f, 0.89f, 0.93f), new Color(0.76f, 0.8f, 0.85f), new Color(0.62f, 0.66f, 0.7f));
        }

        public override void ClientTick() => ThemeKitB.Keep(Camera.main);

        public override void Cleanup() => ThemeKitB.End();
    }
}
