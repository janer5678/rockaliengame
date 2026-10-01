using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Normal graphics: little tufts of grass all over the grass (Plains and Highlands), in the game's low-poly style -
    /// each tuft is a pair of crossed cards with a few pointed blades, in three shades of green, merged into a handful of
    /// big meshes so they cost next to nothing. Not on bases, the ball drop zone or steep rock; hidden in the PSX and AI
    /// PSX graphics (those have their own looks).
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        static Material[] s_Mats;

        public static void Build(Transform root)
        {
            if (ThemeMaps.IsTheme) return; // THEME MAPS (they have their own ground)
            var go = new GameObject("Grass");
            go.transform.SetParent(root, false);
            var g = go.AddComponent<GrassField>();
            g.Generate();
            g.Refresh();
            GameSettings.GraphicsChanged += g.Refresh;
        }

        void OnDestroy() => GameSettings.GraphicsChanged -= Refresh;

        void Refresh()
        {
            if (this == null) return;
            bool on = !GameSettings.PsxGraphics && !GameSettings.AiPsx;
            foreach (Transform c in transform) c.gameObject.SetActive(on);
        }

        static Material[] Mats
        {
            get
            {
                if (s_Mats != null && s_Mats[0] != null) return s_Mats;
                // a few pointed blades on a clear card
                const int W = 32, H = 32;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "grass blades" };
                var px = new Color32[W * H];
                var rng = new System.Random(7);
                var blades = new List<(float x, float h, float lean, float w)>();
                for (int i = 0; i < 7; i++) blades.Add((3f + i * 4.2f + (float)rng.NextDouble() * 2f, 0.55f + (float)rng.NextDouble() * 0.45f, ((float)rng.NextDouble() - 0.5f) * 6f, 1.6f + (float)rng.NextDouble()));
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fy = y / (float)(H - 1);
                    byte a = 0;
                    float shade = 0f;
                    foreach (var b in blades)
                    {
                        if (fy > b.h) continue;
                        float t = fy / b.h;
                        float cx = b.x + b.lean * t * t;
                        float half = b.w * (1f - t);
                        if (Mathf.Abs(x - cx) <= half) { a = 255; shade = Mathf.Max(shade, 0.55f + 0.45f * t); }
                    }
                    byte v = (byte)(255 * shade);
                    px[y * W + x] = new Color32(v, v, v, a);
                }
                tex.SetPixels32(px);
                tex.Apply();
                var baseMat = Resources.Load<Material>("PsxTrees/PsxCutout");
                var greens = new[] { new Color(0.36f, 0.62f, 0.26f), new Color(0.3f, 0.55f, 0.22f), new Color(0.44f, 0.68f, 0.3f) };
                s_Mats = new Material[greens.Length];
                for (int i = 0; i < greens.Length; i++)
                {
                    var m = baseMat != null ? new Material(baseMat) : new Material(Art.Mat(greens[i]));
                    m.name = "grass tufts " + i;
                    m.SetTexture("_BaseMap", tex);
                    m.mainTexture = tex;
                    m.SetColor("_BaseColor", greens[i]);
                    m.color = greens[i];
                    s_Mats[i] = m;
                }
                return s_Mats;
            }
        }

        void Generate()
        {
            float half = Cfg.MapHalf - 1.5f;
            var rng = new System.Random(Cfg.MapSeed * 13 + 5);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int shades = Mats.Length;
            const float cell = 40f;
            int cells = Mathf.CeilToInt(half * 2f / cell);
            // one mesh per shade per 40 m square (so far-off squares can be culled)
            var verts = new Dictionary<(int, int, int), List<Vector3>>();
            var uvs = new Dictionary<(int, int, int), List<Vector2>>();
            int count = Mathf.RoundToInt(half * half * 4f * 0.55f); // about one tuft every 2 square metres
            for (int i = 0; i < count; i++)
            {
                float x = R(-half, half), z = R(-half, half);
                var p = new Vector3(x, 0, z);
                if (Cfg.BaseTeamAt(p) >= 0) continue;                       // not on the bases (you build there)
                if (new Vector2(x, z).magnitude < 13f) continue;            // nor the ball drop zone
                float y = MapBuilder.Height(x, z);
                if (Cfg.Map == MapKind.Highlands)
                {
                    // grass only where it's gentle (the steep bits are rock)
                    float dx = MapBuilder.Height(x + 1f, z) - y, dz = MapBuilder.Height(x, z + 1f) - y;
                    if (new Vector3(-dx, 1f, -dz).normalized.y < 0.82f) continue;
                }
                int shade = rng.Next(shades);
                int ci = Mathf.Clamp(Mathf.FloorToInt((x + half) / cell), 0, cells - 1), cj = Mathf.Clamp(Mathf.FloorToInt((z + half) / cell), 0, cells - 1);
                var key = (ci, cj, shade);
                if (!verts.TryGetValue(key, out var vl)) { verts[key] = vl = new List<Vector3>(); uvs[key] = new List<Vector2>(); }
                var ul = uvs[key];
                float h = R(0.22f, 0.42f), w = R(0.3f, 0.45f), yaw = R(0f, 180f);
                for (int c = 0; c < 2; c++)
                {
                    var dir = Quaternion.Euler(0, yaw + c * 90f, 0) * Vector3.right * (w * 0.5f);
                    var b = new Vector3(x, y - 0.02f, z);
                    vl.Add(b - dir); vl.Add(b + dir); vl.Add(b + dir + Vector3.up * h); vl.Add(b - dir + Vector3.up * h);
                    ul.Add(new Vector2(0, 0)); ul.Add(new Vector2(1, 0)); ul.Add(new Vector2(1, 1)); ul.Add(new Vector2(0, 1));
                }
            }
            foreach (var kv in verts)
            {
                var vl = kv.Value;
                var tris = new int[vl.Count / 4 * 6];
                var normals = new Vector3[vl.Count];
                for (int q = 0, t = 0; q < vl.Count; q += 4)
                {
                    tris[t++] = q; tris[t++] = q + 2; tris[t++] = q + 1;
                    tris[t++] = q; tris[t++] = q + 3; tris[t++] = q + 2;
                    for (int k = 0; k < 4; k++) normals[q + k] = Vector3.up; // lit like the ground under it
                }
                var mesh = new Mesh { name = "grass", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(vl);
                mesh.SetUVs(0, uvs[kv.Key]);
                mesh.SetNormals(normals);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                var part = new GameObject("grass " + kv.Key);
                part.transform.SetParent(transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = part.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mats[kv.Key.Item3];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        }
    }
}
