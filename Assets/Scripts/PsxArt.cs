using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// PSX graphics mode (main menu > Graphics): the 3D models are swapped for low-res PSX-style ones. Only the looks change
    /// - colliders, sizes, weak spots and everything else in the game stay exactly the same, so new features work in both
    /// modes. So far: trees (Resources/PsxTrees, 36 of them). Each tree is a trunk plus alpha-cutout foliage cards sharing one
    /// 128x128 texture (trunk on the left of it, leaves on the right).
    /// </summary>
    public static class PsxArt
    {
        public static bool On => GameSettings.PsxGraphics;

        /// <summary>What we know about one PSX tree model (measured once).</summary>
        public class TreeModel
        {
            public GameObject Prefab;
            public Material Mat;
            public float Height, Bottom;       // model space
            public float TrunkRadius;          // model space, at weak-spot height
            public Color Bark, Leaf;
        }

        static List<TreeModel> s_Trees;
        static Material s_Cutout, s_XMat;

        static Material Cutout
        {
            get
            {
                if (s_Cutout == null) s_Cutout = Resources.Load<Material>("PsxTrees/PsxCutout");
                return s_Cutout;
            }
        }

        static List<TreeModel> Trees
        {
            get
            {
                if (s_Trees != null) return s_Trees;
                s_Trees = new List<TreeModel>();
                if (Cutout == null) { Debug.LogWarning("[RockGame] PSX cutout material missing"); return s_Trees; }
                for (int i = 1; i <= 99; i++)
                {
                    string n = "PsxTrees/tree" + i.ToString("00");
                    var prefab = Resources.Load<GameObject>(n);
                    var tex = Resources.Load<Texture2D>(n);
                    if (prefab == null || tex == null) { if (i > 36) break; continue; }
                    var m = new Material(Cutout) { name = "psx tree" + i };
                    m.SetTexture("_BaseMap", tex);
                    m.mainTexture = tex;
                    var t = new TreeModel { Prefab = prefab, Mat = m };
                    Measure(t, tex);
                    s_Trees.Add(t);
                }
                return s_Trees;
            }
        }

        public static int TreeCount => Trees.Count;

        /// <summary>
        /// Height, trunk thickness (vertices textured from the trunk strip, the left of the texture, around weak-spot
        /// height) and the bark / leaf colours (averaged from the texture).
        /// </summary>
        static void Measure(TreeModel t, Texture2D tex)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var all = new List<(Vector3 p, Vector2 uv)>();
            foreach (var mf in t.Prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                mesh.GetVertices(verts);
                mesh.GetUVs(0, uvs);
                var toRoot = t.Prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < verts.Count; i++)
                {
                    var p = toRoot.MultiplyPoint3x4(verts[i]);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                    all.Add((p, i < uvs.Count ? uvs[i] : Vector2.one));
                }
            }
            if (all.Count == 0) { t.Height = 1f; t.TrunkRadius = 0.1f; t.Bark = Art.DarkWood; t.Leaf = Art.Leaves; return; }
            t.Bottom = minY;
            t.Height = Mathf.Max(0.1f, maxY - minY);
            float r = 0f;
            foreach (var (p, uv) in all)
            {
                float h = (p.y - minY) / t.Height;
                // the trunk strip is u 0.05-0.2; the foliage cards start at u 0.27
                if (uv.x < 0.22f && h > 0.05f && h < 0.35f) r = Mathf.Max(r, new Vector2(p.x, p.z).magnitude);
            }
            t.TrunkRadius = r > 0f ? r : t.Height * 0.04f;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-psxdebug") >= 0)
            {
                var sb = new System.Text.StringBuilder($"[PSX] {t.Prefab.name} h={t.Height:0.00} bottom={t.Bottom:0.00} r={t.TrunkRadius:0.00} verts={all.Count} band: ");
                foreach (var (p, uv) in all)
                {
                    float h = (p.y - minY) / t.Height;
                    if (h > 0.05f && h < 0.35f) sb.Append($"[u{uv.x:0.00} v{uv.y:0.00} d{new Vector2(p.x, p.z).magnitude:0.00} h{h:0.00}]");
                }
                Debug.Log(sb.ToString());
            }
            // colours for the chips and leaves that fly off it
            Color bark = Color.black, leaf = Color.black;
            int nb = 0, nl = 0;
            try
            {
                var px = tex.GetPixels32();
                int w = tex.width, hgt = tex.height;
                for (int y = 0; y < hgt; y += 2)
                for (int x = 0; x < w; x += 2)
                {
                    var c = px[y * w + x];
                    if (c.a < 128) continue;
                    if (x < w * 0.25f) { bark += (Color)c; nb++; }
                    else if (x > w * 0.32f) { leaf += (Color)c; nl++; }
                }
            }
            catch (UnityException) { }
            t.Bark = nb > 0 ? bark / nb : Art.DarkWood;
            t.Leaf = nl > 0 ? leaf / nl : Art.Leaves;
            t.Bark.a = t.Leaf.a = 1f;
        }

        /// <summary>
        /// Puts a PSX tree under `parent` (standing at its origin), about `height` metres tall, picked and turned by `seed`.
        /// trunkRadius: how thick the trunk is where the weak spot goes (world metres).
        /// </summary>
        public static bool BuildTree(Transform parent, int seed, float height, out float trunkRadius, out Color bark, out Color leaf)
        {
            trunkRadius = 0f;
            bark = Art.DarkWood;
            leaf = Art.Leaves;
            var trees = Trees;
            if (trees.Count == 0) return false;
            var rng = new System.Random(seed * 31 + 7);
            var t = trees[rng.Next(trees.Count)];
            float scale = height / t.Height;
            var go = Object.Instantiate(t.Prefab, parent, false);
            go.name = "psxTree";
            go.transform.localScale = t.Prefab.transform.localScale * scale;
            go.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0) * t.Prefab.transform.localRotation;
            go.transform.localPosition = new Vector3(0, -t.Bottom * scale, 0);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = t.Mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            trunkRadius = t.TrunkRadius * scale;
            bark = t.Bark;
            leaf = t.Leaf;
            return true;
        }

        /// <summary>The weak-spot X, PSX style: a chunky pixel-art X (16x16, orange with a dark outline) on a little card.</summary>
        public static GameObject PixelX(Transform parent)
        {
            if (s_XMat == null && Cutout != null)
            {
                const int S = 16;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[S * S];
                var fill = new Color32(255, 120, 20, 255);
                var edge = new Color32(90, 25, 5, 255);
                bool OnX(int x, int y) => x >= 2 && x <= 13 && y >= 2 && y <= 13 && (Mathf.Abs(x - y) <= 1 || Mathf.Abs(x + y - 15) <= 1);
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    Color32 c = new Color32(0, 0, 0, 0);
                    if (OnX(x, y)) c = fill;
                    else
                        for (int dy = -1; dy <= 1 && c.a == 0; dy++)
                        for (int dx = -1; dx <= 1 && c.a == 0; dx++)
                            if (OnX(x + dx, y + dy)) c = edge;
                    px[y * S + x] = c;
                }
                tex.SetPixels32(px);
                tex.Apply();
                s_XMat = new Material(Cutout) { name = "psx x" };
                s_XMat.SetTexture("_BaseMap", tex);
                s_XMat.mainTexture = tex;
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "pixelX";
            q.transform.SetParent(parent, false);
            q.transform.localScale = Vector3.one * 0.42f;
            var mr = q.GetComponent<MeshRenderer>();
            if (s_XMat != null) mr.sharedMaterial = s_XMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
    }
}
