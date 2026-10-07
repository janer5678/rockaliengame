using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Hats (the lobby's CUSTOMISE ALIEN: Hud.Lobby.cs): a few silly things to wear on your alien's head, picked in the
    /// lobby, saved on this PC and synced (PlayerNet.Hat) so everyone sees them in the lobby and in the game. Each is
    /// made for the head it goes on: the alien's own skull is read off its posed mesh (HeadMesh) - hair and the beanie
    /// are a smooth cap cast onto the skull itself (Cap: each point reaches out to the head's surface, then a little more), stopping above the brow, so they wrap round the head
    /// exactly; hard hats sit where the skull is as wide as their crown (SeatY). Everything's shaded smooth like the
    /// alien. The hat follows the head bone after the animation (HatFollow).
    /// </summary>
    public static class Cosmetics
    {
        /// <summary>Hats (and CUSTOMISE ALIEN) are switched off for now: nobody wears one, in the lobby or a match.</summary>
        public const bool HatsOn = false;

        public static readonly string[] HatNames =
        {
            "No hat", "Anime hair", "Paper bag", "Cowboy hat", "Top hat", "Bieber hair", "Punk mohawk", "Long wig", "Beanie", "Party hat",
        };
        public static int HatCount => HatNames.Length;

        const string HatKey = "RockGame.Hat";
        /// <summary>The hat picked on this PC (0 = none).</summary>
        public static int MyHat
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(HatKey, 0), 0, HatCount - 1);
            set { PlayerPrefs.SetInt(HatKey, Mathf.Clamp(value, 0, HatCount - 1)); PlayerPrefs.Save(); }
        }

        /// <summary>The skull, in "head space" (from the head bone, axes the character's: x right, y up, z forward): its
        /// middle, size, top and front, and its surface (points, normals, triangles) to build hair on.</summary>
        public class Head
        {
            public Vector3 Centre = new Vector3(0, 0.16f, 0.02f), Size = new Vector3(0.36f, 0.34f, 0.36f);
            public float Top = 0.33f, Front = 0.2f;
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<int> T = new List<int>();
        }

        /// <summary>Reads the skull off the posed mesh: everything above the head bone (the cranium and the face).</summary>
        public static Head HeadMesh(Transform root, Transform headBone, Quaternion frame)
        {
            var h = new Head();
            if (root == null || headBone == null) return h;
            var inv = Quaternion.Inverse(frame);
            Vector3 lo = Vector3.one * 99f, hi = -Vector3.one * 99f;
            // (the baked points are unscaled: the model's own width - Cfg.ModelWidth on x and z - goes back on, in the root's space)
            var model = root.Find("alien");
            var ms = model != null ? model.localScale : Vector3.one;
            var rootRot = root.rotation;
            var mesh = new Mesh();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(mesh, false); // (unscaled: the model carries an import scale of ~100 - the baked points are already in metres)
                var v = mesh.vertices;
                var nrm = mesh.normals;
                var t = smr.transform;
                var map = new int[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    map[i] = -1;
                    var off = Quaternion.Inverse(rootRot) * (t.rotation * v[i]);
                    off = rootRot * new Vector3(off.x * ms.x, off.y * ms.y, off.z * ms.z);
                    var l = inv * (t.position + off - headBone.position);
                    if (l.y < 0.02f || Mathf.Abs(l.x) > 0.4f || Mathf.Abs(l.z) > 0.4f) continue; // (the skull: above the neck, not the shoulders)
                    lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l);
                    map[i] = h.V.Count;
                    h.V.Add(l);
                    h.N.Add(i < nrm.Length ? (inv * (t.rotation * nrm[i])).normalized : l.normalized);
                }
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        int a = map[tris[i]], b = map[tris[i + 1]], c = map[tris[i + 2]];
                        if (a < 0 || b < 0 || c < 0) continue;
                        h.T.Add(a); h.T.Add(b); h.T.Add(c);
                    }
                }
            }
            Object.Destroy(mesh);
            if (h.V.Count < 20) return h;
            h.Centre = (lo + hi) * 0.5f;
            h.Size = hi - lo;
            h.Top = hi.y;
            h.Front = hi.z;
            return h;
        }

        /// <summary>Puts hat `id` on: a holder under `parent` that follows the head bone (null for no hat).</summary>
        public static GameObject Wear(int id, Transform parent, BodyAnimator anim, bool shadowsOnly = false)
        {
            if (id <= 0 || id >= HatCount || parent == null || anim == null || anim.HeadBone == null) return null;
            var headBone = anim.HeadBone;
            var head = HeadMesh(anim.VisualRoot, headBone, headBone.rotation * anim.HeadFrameOffset);
            var go = new GameObject("hat " + HatNames[id]);
            go.transform.SetParent(parent, false);
            var follow = go.AddComponent<HatFollow>();
            follow.Bone = headBone;
            follow.Offset = anim.HeadFrameOffset; // (the head's own frame: the hat stays put on it whatever the pose)
            Build(id, go.transform, head);
            SmoothShadeHook.Add(go, true); // (shaded smooth, like the alien)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = shadowsOnly ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            follow.LateUpdate();
            return go;
        }

        static float CR => 2f * Art.Cylinder.bounds.extents.x;
        static float CH => 2f * Art.Cylinder.bounds.extents.y;
        static float SR => 2f * Art.Sphere.bounds.extents.x;

        static GameObject Cyl(Transform t, Color c, Vector3 at, float diameter, float height, Vector3 euler = default) =>
            Art.Part(t, Art.Cylinder, c, at, new Vector3(diameter / CR, height / CH, diameter / CR), euler);
        static GameObject Ball(Transform t, Color c, Vector3 at, Vector3 size, Vector3 euler = default) =>
            Art.Part(t, Art.Sphere, c, at, new Vector3(size.x / SR, size.y / SR, size.z / SR), euler);
        static GameObject Spike(Transform t, Color c, Vector3 at, Vector3 dir, float width, float length) =>
            Art.Part(t, Art.Cone, c, at, new Vector3(width, length, width), Quaternion.FromToRotation(Vector3.up, dir.normalized).eulerAngles);

        /// <summary>Is this point of the skull on the face (the front, below the brow)? Hair and beanies leave it bare.</summary>
        static bool Face(Head h, Vector3 p, float brow) =>
            p.z > h.Centre.z + h.Size.z * 0.05f && p.y < h.Centre.y + h.Size.y * brow;

        /// <summary>How far out from `o` along `dir` the skull's surface is (its outermost crossing; -1: it misses).</summary>
        static float SurfaceDist(Head h, Vector3 o, Vector3 dir)
        {
            float best = -1f;
            for (int i = 0; i + 2 < h.T.Count; i += 3)
            {
                Vector3 a = h.V[h.T[i]], b = h.V[h.T[i + 1]], c = h.V[h.T[i + 2]];
                Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-7f) continue;
                float inv = 1f / det;
                Vector3 s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(dir, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                float t = Vector3.Dot(e2, q) * inv;
                if (t > best) best = t;
            }
            return best;
        }

        /// <summary>
        /// A smooth cap that wraps the skull: a dome of points, each cast out from the skull's middle to where the head's
        /// surface is and pushed out by `push` (plus `puff` more towards the crown, for volume). It reaches down to
        /// `lowFront` degrees at the front (above the brow: the face stays bare) and `lowBack` at the back (lower, round the
        /// nape); `band` > 0 keeps just a band that high above its bottom edge (a beanie's cuff).
        /// </summary>
        static GameObject Cap(Transform t, Head h, Color col, float lowFront, float lowBack, float push, float puff, string name, float band = 0f)
        {
            const int Az = 28, El = 10;
            var o = h.Centre;
            var fallback = new Vector3(h.Size.x * 0.5f, h.Size.y * 0.5f, h.Size.z * 0.5f);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            for (int i = 0; i <= Az; i++)
            {
                float az = i * Mathf.PI * 2f / Az;
                float low = Mathf.Lerp(lowFront, lowBack, (1f - Mathf.Cos(az)) * 0.5f);
                float top = band > 0f ? low + band : 90f;
                for (int j = 0; j <= El; j++)
                {
                    float el = Mathf.Lerp(low, top, j / (float)El) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
                    float d = SurfaceDist(h, o, dir);
                    if (d <= 0f) d = Vector3.Scale(dir, fallback).magnitude;
                    float up = Mathf.Clamp01(Mathf.Sin(el));
                    verts.Add(o + dir * (d + push + puff * up * up));
                    norms.Add(dir);
                }
            }
            var tris = new List<int>();
            // (one side only - the outside: the winding that faces out along the cap's own normals)
            var n0 = Vector3.Cross(verts[El + 1] - verts[0], verts[1] - verts[0]);
            bool flip = Vector3.Dot(n0, norms[0]) < 0f;
            for (int i = 0; i < Az; i++)
                for (int j = 0; j < El; j++)
                {
                    int a = i * (El + 1) + j, b = (i + 1) * (El + 1) + j;
                    if (!flip) { tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1); tris.Add(a + 1); }
                    else { tris.Add(a); tris.Add(a + 1); tris.Add(b); tris.Add(b); tris.Add(a + 1); tris.Add(b + 1); }
                }
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(t, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = Art.Mat(col);
            return go;
        }

        /// <summary>The height a hat with a round crown `radius` wide rests at: where the skull is that wide.</summary>
        static float SeatY(Head h, float radius)
        {
            float y = h.Top - h.Size.y * 0.3f;
            bool any = false;
            foreach (var p in h.V)
            {
                float r = new Vector2(p.x - h.Centre.x, p.z - h.Centre.z).magnitude;
                if (r < radius * 0.97f) continue;
                if (!any || p.y > y) { y = p.y; any = true; }
            }
            return Mathf.Min(y, h.Top - h.Size.y * 0.08f);
        }

        /// <summary>Points on the skull's surface near the crown, for hair to grow from (in a ring band, `n` of them).</summary>
        static IEnumerable<(Vector3 p, Vector3 n)> Crown(Head h, int n, float fromTop, float toTop, int seed)
        {
            var rng = new System.Random(seed);
            if (h.V.Count == 0) yield break;
            for (int k = 0, tries = 0; k < n && tries < n * 40; tries++)
            {
                int i = rng.Next(h.V.Count);
                var p = h.V[i];
                float dt = (h.Top - p.y) / Mathf.Max(0.01f, h.Size.y);
                if (dt < fromTop || dt > toTop || Face(h, p, 0.3f)) continue;
                k++;
                yield return (p, h.N[i]);
            }
        }

        /// <summary>Each hat, made to the head (w: its width, d: its depth, c: its middle).</summary>
        static void Build(int id, Transform t, Head h)
        {
            float w = h.Size.x, d = h.Size.z, hh = h.Size.y;
            var c = h.Centre;
            var rng = new System.Random(id * 97 + 5);
            float R() => (float)rng.NextDouble();
            switch (id)
            {
                case 1: // anime hair: a hot pink spiky mop - a snug layer of hair and two rings of spikes sweeping up and back, a fringe
                {
                    var hair = new Color(1f, 0.38f, 0.72f);
                    Cap(t, h, hair, 22f, -35f, 0.02f, 0.04f, "hair");
                    foreach (var (p, n) in Crown(h, 14, 0f, 0.25f, 11))
                        Spike(t, hair * (0.92f + R() * 0.12f), p + n * 0.01f, (n + Vector3.up * 0.6f + Vector3.back * 0.35f).normalized, w * 0.2f, w * (0.3f + R() * 0.18f));
                    foreach (var (p, n) in Crown(h, 12, 0.25f, 0.5f, 12))
                        Spike(t, hair * (0.85f + R() * 0.15f), p + n * 0.01f, (n + Vector3.back * 0.5f + Vector3.up * 0.2f).normalized, w * 0.16f, w * (0.22f + R() * 0.12f));
                    for (int i = 0; i < 5; i++) // (the fringe over the brow)
                        Spike(t, hair, new Vector3(c.x + (i - 2f) * w * 0.13f, c.y + hh * 0.3f, h.Front - d * 0.12f), new Vector3((i - 2f) * 0.15f, -0.7f, 0.9f), w * 0.12f, w * 0.24f);
                    break;
                }
                case 2: // a paper bag over the whole head, eye holes cut in it, the top crumpled
                {
                    var paper = new Color(0.72f, 0.56f, 0.36f);
                    float bw = w * 1.06f, bd = d * 1.06f, bh = hh * 1.02f;
                    var mid = new Vector3(c.x, h.Top - bh * 0.5f + 0.01f, c.z);
                    Art.Box(t, paper, mid, new Vector3(bw, bh, bd));
                    for (int i = 0; i < 6; i++) Art.Box(t, paper * (0.88f + R() * 0.12f), mid + new Vector3((i - 2.5f) * bw * 0.17f, bh * 0.5f + 0.012f, 0), new Vector3(bw * 0.16f, 0.03f, bd * 0.95f), new Vector3(R() * 20f - 10f, 0, R() * 16f - 8f));
                    foreach (float s in new[] { -1f, 1f })
                        Art.Box(t, new Color(0.05f, 0.04f, 0.03f), new Vector3(c.x + s * bw * 0.2f, mid.y - bh * 0.02f, c.z + bd * 0.5f + 0.004f), new Vector3(bw * 0.15f, bh * 0.11f, 0.01f));
                    Art.Box(t, new Color(0.3f, 0.12f, 0.1f), new Vector3(c.x, mid.y - bh * 0.24f, c.z + bd * 0.5f + 0.004f), new Vector3(bw * 0.3f, 0.02f, 0.01f)); // (a drawn-on mouth)
                    break;
                }
                case 3: // a cowboy hat: a crown resting on the head, a brim curled up at the sides, a band
                {
                    var leather = new Color(0.45f, 0.28f, 0.14f);
                    float crown = w * 0.42f;
                    float y = SeatY(h, crown) - 0.01f;
                    Cyl(t, leather, new Vector3(c.x, y, c.z), w * 1.45f, 0.022f);
                    foreach (float s in new[] { -1f, 1f }) Art.Box(t, leather, new Vector3(c.x + s * w * 0.66f, y + 0.03f, c.z), new Vector3(w * 0.16f, 0.018f, d * 1.0f), new Vector3(0, 0, s * 28f));
                    Cyl(t, leather * 1.08f, new Vector3(c.x, y + hh * 0.17f, c.z), crown * 2f, hh * 0.34f);
                    Art.Box(t, leather * 0.85f, new Vector3(c.x, y + hh * 0.34f, c.z), new Vector3(w * 0.1f, 0.03f, d * 0.5f)); // (the dent along the top)
                    Cyl(t, new Color(0.15f, 0.1f, 0.07f), new Vector3(c.x, y + hh * 0.05f, c.z), crown * 2.04f, 0.035f);
                    break;
                }
                case 4: // a tall black top hat with a red band, resting on the head
                {
                    var black = new Color(0.07f, 0.07f, 0.08f);
                    float crown = w * 0.34f;
                    float y = SeatY(h, crown) - 0.01f;
                    Cyl(t, black, new Vector3(c.x, y, c.z), w * 1.0f, 0.018f);
                    Cyl(t, black, new Vector3(c.x, y + hh * 0.36f, c.z), crown * 2f, hh * 0.72f);
                    Cyl(t, new Color(0.65f, 0.08f, 0.1f), new Vector3(c.x, y + hh * 0.08f, c.z), crown * 2.04f, 0.045f);
                    break;
                }
                case 5: // a Bieber cut: smooth brown hair close round the head, a long swept fringe and side-swept strands
                {
                    var hair = new Color(0.42f, 0.27f, 0.13f);
                    Cap(t, h, hair, 26f, -28f, 0.016f, 0.032f, "hair");
                    for (int i = 0; i < 7; i++) // (the fringe: strands swept to one side)
                    {
                        float f = i / 6f;
                        var at = new Vector3(c.x + Mathf.Lerp(-0.3f, 0.32f, f) * w, c.y + hh * (0.38f - f * 0.06f), h.Front - d * (0.16f + Mathf.Abs(f - 0.5f) * 0.1f));
                        Ball(t, hair * (1.04f + R() * 0.08f), at, new Vector3(w * 0.28f, hh * 0.07f, d * 0.16f), new Vector3(-24f, 0, -18f - f * 10f));
                    }
                    break;
                }
                case 6: // a punk mohawk: a shaved dark stubble, and a tall row of hot pink and green spikes front to back
                {
                    Cap(t, h, new Color(0.18f, 0.16f, 0.15f), 30f, -20f, 0.006f, 0f, "stubble");
                    for (int i = 0; i < 9; i++)
                    {
                        float f = i / 8f;
                        float z = Mathf.Lerp(h.Front - d * 0.18f, c.z - d * 0.46f, f);
                        float y = h.Top - hh * (0.03f + Mathf.Pow(Mathf.Abs(f - 0.35f), 2f) * 0.5f);
                        Spike(t, i % 2 == 0 ? new Color(1f, 0.2f, 0.65f) : new Color(0.3f, 1f, 0.4f), new Vector3(c.x, y, z), new Vector3(0, 1f, 0.2f - f * 0.6f), w * 0.16f, w * (0.5f - Mathf.Abs(f - 0.35f) * 0.3f));
                    }
                    break;
                }
                case 7: // a long blonde wig: hair close round the head, a parting, and long locks down the back and sides
                {
                    var hair = new Color(0.98f, 0.84f, 0.45f);
                    Cap(t, h, hair, 22f, -50f, 0.022f, 0.035f, "hair");
                    Art.Box(t, hair * 0.8f, new Vector3(c.x - w * 0.08f, h.Top + 0.012f, c.z), new Vector3(0.012f, 0.008f, d * 0.6f)); // (the parting)
                    for (int i = 0; i < 11; i++) // (long locks round the back and sides, each a little different)
                    {
                        float a = Mathf.Lerp(-120f, 120f, i / 10f) * Mathf.Deg2Rad;
                        var dir = new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));
                        var at = new Vector3(c.x + dir.x * w * 0.47f, c.y - hh * 0.15f, c.z + dir.z * d * 0.47f);
                        float len = hh * (0.85f + R() * 0.2f);
                        Art.Box(t, hair * (0.9f + R() * 0.12f), at + Vector3.down * len * 0.35f + dir * 0.015f, new Vector3(w * 0.15f, len, 0.03f), new Vector3(-6f, a * Mathf.Rad2Deg, 0));
                    }
                    Art.Box(t, new Color(1f, 0.5f, 0.75f), new Vector3(c.x + w * 0.32f, h.Top - hh * 0.12f, c.z), new Vector3(w * 0.16f, 0.05f, 0.05f), new Vector3(0, 30f, 18f)); // (a bow)
                    break;
                }
                case 8: // a knitted beanie: snug over the top of the head, a ribbed cuff, a bobble
                {
                    var red = new Color(0.8f, 0.15f, 0.15f);
                    Cap(t, h, red, 14f, -6f, 0.016f, 0.02f, "beanie");
                    Cap(t, h, red * 0.82f, 14f, -6f, 0.03f, 0f, "cuff", 12f); // (the rolled-up cuff round the bottom)
                    Ball(t, Color.white, new Vector3(c.x, h.Top + 0.03f, c.z - d * 0.05f), Vector3.one * w * 0.18f);
                    break;
                }
                default: // a striped party hat, tipped a little, with a pompom
                {
                    float y = h.Top - hh * 0.06f;
                    var tipAt = new Vector3(c.x + w * 0.04f, y, c.z);
                    var cone = Art.Part(t, Art.Cone, new Color(0.3f, 0.6f, 1f), tipAt, new Vector3(w * 0.55f, hh * 0.75f, w * 0.55f), new Vector3(0, 0, -10f));
                    for (int i = 0; i < 3; i++)
                        Art.Part(cone.transform, Art.Cone, new Color(1f, 0.85f, 0.2f), new Vector3(0, 0.2f + i * 0.25f, 0), new Vector3(1.04f - i * 0.25f, 0.08f, 1.04f - i * 0.25f));
                    Ball(t, new Color(1f, 0.3f, 0.6f), tipAt + Quaternion.Euler(0, 0, -10f) * new Vector3(0, hh * 0.75f, 0), Vector3.one * w * 0.13f);
                    break;
                }
            }
        }
    }

    /// <summary>Keeps a hat on its head: after the animation each frame it takes the head bone's place and turn.</summary>
    [DefaultExecutionOrder(3000)]
    public class HatFollow : MonoBehaviour
    {
        public Transform Bone;
        public Quaternion Offset = Quaternion.identity;

        public void LateUpdate()
        {
            if (Bone == null) return;
            transform.SetPositionAndRotation(Bone.position, Bone.rotation * Offset);
        }
    }

    public partial class PlayerNet
    {
        /// <summary>The hat this player wears (Cosmetics.HatNames; 0 = none): picked in the lobby, synced to everyone.</summary>
        public readonly NetworkVariable<byte> Hat = new NetworkVariable<byte>();
        GameObject m_HatGo;
        int m_HatShown = -1;

        /// <summary>Every screen, the moment this player dies: their alien drops as a ragdoll (Ragdoll.cs), knocked away
        /// from whoever killed them.</summary>
        void SpawnRagdoll()
        {
            if (m_Anim == null || m_VisualRoot == null || PsxModels.On || !m_VisualRoot.gameObject.activeSelf) return;
            var model = m_VisualRoot.Find("alien");
            if (model == null) return;
            var push = -transform.forward * 2f + Vector3.up * 1.5f;
            if (KilledBy.Value != 0 && NetworkManager != null && NetworkManager.SpawnManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(KilledBy.Value, out var k) && k != null)
            {
                var d = transform.position - k.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) push = d.normalized * 4.5f + Vector3.up * 2f;
            }
            Ragdoll.Spawn(model.gameObject, m_HatGo, push, NetworkObjectId);
        }

        [Rpc(SendTo.Server)]
        public void SetHatRpc(byte hat) => Hat.Value = (byte)Mathf.Clamp(hat, 0, Cosmetics.HatCount - 1);

        /// <summary>Every frame: the hat on this player's alien is the one they picked (yours only shows in your shadow).</summary>
        void TickHat()
        {
            int hat = Cosmetics.HatsOn ? Hat.Value : 0; // (hats are off for now)
            if (m_HatShown == hat || m_Anim == null || m_Anim.HeadBone == null) return;
            m_HatShown = hat;
            if (m_HatGo) Destroy(m_HatGo);
            m_HatGo = Cosmetics.Wear(hat, m_VisualRoot, m_Anim, IsOwner);
        }
    }
}
