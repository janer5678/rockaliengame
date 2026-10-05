using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Things lying on the ground glint when you're near them, so they're easier to spot in the grass: every couple of
    /// seconds a little four-pointed star flashes just above the item - it swells, turns a touch and is gone in under half
    /// a second. Only within Cfg.ItemGlintRange of this screen's player, each item at its own moment (a pile doesn't blink
    /// as one), not while it's still in the air. Just on this screen: nothing is sent, and it costs one small mesh per
    /// item near you (made the first time it glints, hidden between flashes).
    /// </summary>
    public partial class NetGame
    {
        readonly Dictionary<int, Transform> m_Glints = new Dictionary<int, Transform>();
        static Mesh s_GlintMesh;
        static Material s_GlintMat;
        static int s_GlintFrame = -1;
        static Transform s_GlintCam;
        static Vector3 s_GlintMe;
        static bool s_GlintOn;

        const float GlintTime = 0.4f, GlintSize = 0.3f;

        /// <summary>For the tests: how many glints have flashed on this machine, and how many items are showing one right now.</summary>
        public static int GlintsShown { get; private set; }
        public static int GlintsNow { get; private set; }

        /// <summary>One world item's glint this frame. `flying`: it's still being tossed to where it rests.</summary>
        void ItemGlint(DroppedItem it, Transform visual, bool flying)
        {
            if (s_GlintFrame != Time.frameCount)
            {
                // once a frame: where this screen's player and camera are
                s_GlintFrame = Time.frameCount;
                GlintsNow = 0;
                var pc = PlayerController.Local;
                var cam = Camera.main;
                s_GlintOn = pc != null && cam != null && Cfg.ItemGlintRange > 0f;
                if (s_GlintOn) { s_GlintMe = pc.transform.position; s_GlintCam = cam.transform; }
            }
            m_Glints.TryGetValue(it.Id, out var g);
            float range = Cfg.ItemGlintRange;
            bool near = s_GlintOn && !flying && (it.Pos - s_GlintMe).sqrMagnitude <= range * range;
            float u = 2f;
            if (near)
            {
                float every = Mathf.Max(GlintTime + 0.2f, Cfg.ItemGlintEvery);
                // (each item has its own beat, a little longer or shorter than the next one's)
                float mine = every * (0.85f + 0.3f * Frac(it.Id * 0.754877f));
                u = Mathf.Repeat(Time.time + Frac(it.Id * 0.618034f) * mine, mine) / GlintTime;
            }
            if (u >= 1f)
            {
                if (g && g.gameObject.activeSelf) g.gameObject.SetActive(false);
                return;
            }
            if (!g)
            {
                g = NewGlint(visual);
                m_Glints[it.Id] = g;
            }
            if (!g.gameObject.activeSelf) { g.gameObject.SetActive(true); GlintsShown++; }
            GlintsNow++;
            // just above the item, a touch toward the camera (over the grass blades round it), facing the camera
            var at = it.Pos + Vector3.up * 0.38f;
            var toCam = s_GlintCam.position - at;
            float dist = toCam.magnitude;
            if (dist > 0.01f) at += toCam / dist * Mathf.Min(0.25f, dist * 0.5f);
            float s = Mathf.Sin(u * Mathf.PI);
            g.SetPositionAndRotation(at, s_GlintCam.rotation * Quaternion.Euler(0, 0, 20f + u * 70f));
            // (the item's own scale - its bounce - mustn't squash the star)
            var ps = visual.lossyScale;
            float k = GlintSize * s * (0.8f + 0.4f * Frac(it.Id * 0.3819f));
            g.localScale = new Vector3(k / Mathf.Max(0.01f, ps.x), k / Mathf.Max(0.01f, ps.y), k / Mathf.Max(0.01f, ps.z));
        }

        void ForgetItemGlint(int id) => m_Glints.Remove(id); // (the star hangs off the item's visual and goes with it)

        static float Frac(float v) => v - Mathf.Floor(v);

        static Transform NewGlint(Transform parent)
        {
            if (s_GlintMesh == null) s_GlintMesh = GlintMesh();
            if (s_GlintMat == null)
            {
                // unlit (RockGame/Glow, like the tree X): always the same bright light colour, in the shade of a tree or a
                // wall just as in the sun - a lit one went grey in shadow. HDR, so it blooms with post processing on
                var c = new Color(1f, 0.97f, 0.82f);
                var sh = Resources.Load<Shader>("World/Glow");
                if (sh != null && sh.isSupported)
                {
                    s_GlintMat = new Material(sh) { name = "item glint" };
                    s_GlintMat.SetColor("_Color", c);
                    s_GlintMat.SetFloat("_Intensity", 1.8f);
                }
                else
                {
                    // (no shader: a plain bright material that glows whatever the light)
                    s_GlintMat = Art.NewMat(c);
                    s_GlintMat.name = "item glint";
                    if (s_GlintMat.HasProperty("_EmissionColor"))
                    {
                        s_GlintMat.EnableKeyword("_EMISSION");
                        s_GlintMat.SetColor("_EmissionColor", c * 2.2f);
                    }
                }
            }
            var go = new GameObject("glint");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = s_GlintMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s_GlintMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>A flat four-pointed star (long thin points up, down, left and right), seen from both sides.</summary>
        static Mesh GlintMesh()
        {
            const float w = 0.1f;
            var v = new List<Vector3> { Vector3.zero };
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f, r = i % 2 == 0 ? 1f : w * 1.4142f;
                v.Add(new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0));
            }
            var tris = new List<int>();
            for (int i = 0; i < 8; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % 8;
                tris.Add(0); tris.Add(a); tris.Add(b);
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            var normals = new Vector3[v.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            var m = new Mesh { name = "glint" };
            m.SetVertices(v);
            m.SetTriangles(tris, 0);
            m.normals = normals;
            m.RecalculateBounds();
            return m;
        }
    }
}
