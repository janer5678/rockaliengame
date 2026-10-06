using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Hats (the lobby's CUSTOMISE ALIEN: Hud.Lobby.cs): a few silly things to wear on your alien's head, picked in the
    /// lobby, saved on this PC and synced (PlayerNet.Hat) so everyone sees them in the lobby and in the game. Each is
    /// built to the head it goes on: the skull is measured off the alien's own mesh (HeadShape), and every hat is sized
    /// from that, so nothing floats or swamps the head. The hat follows the head bone after the animation (HatFollow).
    /// </summary>
    public static class Cosmetics
    {
        public static readonly string[] HatNames =
        {
            "No hat", "Anime hair", "Paper bag", "Cowboy hat", "Top hat & monocle", "Bieber hair", "Punk mohawk", "Long wig", "Beanie", "Party hat",
        };
        public static int HatCount => HatNames.Length;

        const string HatKey = "RockGame.Hat";
        /// <summary>The hat picked on this PC (0 = none).</summary>
        public static int MyHat
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(HatKey, 0), 0, HatCount - 1);
            set { PlayerPrefs.SetInt(HatKey, Mathf.Clamp(value, 0, HatCount - 1)); PlayerPrefs.Save(); }
        }

        /// <summary>The skull, in "head space" (from the head bone, axes the character's: x right, y up, z forward):
        /// its middle, its size and its top.</summary>
        public struct Head { public Vector3 Centre, Size; public float Top, Front; }

        /// <summary>Measures the skull off the posed mesh: everything above the head bone (the cranium and face).</summary>
        public static Head HeadShape(Transform root, Transform headBone, Quaternion frame)
        {
            var h = new Head { Centre = new Vector3(0, 0.16f, 0.02f), Size = new Vector3(0.36f, 0.34f, 0.36f), Top = 0.33f, Front = 0.2f };
            if (root == null || headBone == null) return h;
            var inv = Quaternion.Inverse(frame);
            Vector3 lo = Vector3.one * 99f, hi = -Vector3.one * 99f;
            int n = 0;
            var mesh = new Mesh();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(mesh, true);
                var v = mesh.vertices;
                var t = smr.transform;
                for (int i = 0; i < v.Length; i++)
                {
                    var w = t.position + t.rotation * v[i];
                    var l = inv * (w - headBone.position);
                    if (l.y < 0.02f || Mathf.Abs(l.x) > 0.4f || Mathf.Abs(l.z) > 0.4f) continue; // (the skull: above the neck, not the shoulders)
                    lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); n++;
                }
            }
            Object.Destroy(mesh);
            if (n < 20) return h;
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
            var shape = HeadShape(anim.VisualRoot, headBone, headBone.rotation * anim.HeadFrameOffset);
            var go = new GameObject("hat " + HatNames[id]);
            go.transform.SetParent(parent, false);
            var follow = go.AddComponent<HatFollow>();
            follow.Bone = headBone;
            follow.Offset = anim.HeadFrameOffset; // (the head's own frame: the hat stays put on it whatever the pose)
            Build(id, go.transform, shape);
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

        /// <summary>Each hat, sized from the skull (w: its width, d: its depth, top: its top, c: its middle).</summary>
        static void Build(int id, Transform t, Head h)
        {
            float w = h.Size.x, d = h.Size.z, top = h.Top;
            var c = h.Centre;
            var rng = new System.Random(id * 97 + 5);
            float R() => (float)rng.NextDouble();
            switch (id)
            {
                case 1: // anime hair: a spiky golden mop, the spikes sweeping up and back
                {
                    var hair = new Color(1f, 0.78f, 0.25f);
                    Ball(t, hair, new Vector3(c.x, top - h.Size.y * 0.3f, c.z - d * 0.04f), new Vector3(w * 1.06f, h.Size.y * 0.6f, d * 1.06f));
                    for (int i = 0; i < 16; i++)
                    {
                        float a = i / 16f * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Sin(a) * 0.8f, 0.55f + R() * 0.3f, Mathf.Cos(a) * 0.6f - 0.35f);
                        var at = new Vector3(c.x + Mathf.Sin(a) * w * 0.32f, top - h.Size.y * 0.2f, c.z + Mathf.Cos(a) * d * 0.3f);
                        Spike(t, hair * (0.9f + R() * 0.15f), at, dir, w * 0.22f, w * (0.35f + R() * 0.25f));
                    }
                    for (int i = 0; i < 4; i++) // (a fringe over the brow)
                        Spike(t, hair, new Vector3(c.x + (i - 1.5f) * w * 0.16f, top - h.Size.y * 0.3f, h.Front - d * 0.06f), new Vector3((i - 1.5f) * 0.2f, -0.6f, 1f), w * 0.15f, w * 0.3f);
                    break;
                }
                case 2: // a paper bag over the whole head, eye holes cut in it, the top crumpled
                {
                    var paper = new Color(0.72f, 0.56f, 0.36f);
                    float bw = w * 1.12f, bd = d * 1.12f, bh = h.Size.y * 1.08f;
                    var mid = new Vector3(c.x, top - bh * 0.5f + 0.02f, c.z);
                    Art.Box(t, paper, mid, new Vector3(bw, bh, bd));
                    for (int i = 0; i < 6; i++) Art.Box(t, paper * (0.88f + R() * 0.12f), mid + new Vector3((i - 2.5f) * bw * 0.17f, bh * 0.5f + 0.015f, 0), new Vector3(bw * 0.16f, 0.04f, bd * 0.95f), new Vector3(R() * 20f - 10f, 0, R() * 16f - 8f));
                    foreach (float s in new[] { -1f, 1f })
                        Art.Box(t, new Color(0.05f, 0.04f, 0.03f), new Vector3(c.x + s * bw * 0.2f, mid.y + bh * 0.08f, c.z + bd * 0.5f + 0.004f), new Vector3(bw * 0.16f, bh * 0.12f, 0.01f));
                    Art.Box(t, new Color(0.3f, 0.12f, 0.1f), new Vector3(c.x, mid.y - bh * 0.18f, c.z + bd * 0.5f + 0.004f), new Vector3(bw * 0.3f, 0.02f, 0.01f)); // (a drawn-on mouth)
                    break;
                }
                case 3: // a cowboy hat: a wide brim curled up at the sides, a dented crown, a band
                {
                    var leather = new Color(0.45f, 0.28f, 0.14f);
                    float y = top - h.Size.y * 0.32f; // (pulled down onto the head)
                    Cyl(t, leather, new Vector3(c.x, y, c.z), w * 1.75f, 0.025f);
                    foreach (float s in new[] { -1f, 1f }) Art.Box(t, leather, new Vector3(c.x + s * w * 0.78f, y + 0.035f, c.z), new Vector3(w * 0.2f, 0.02f, d * 1.2f), new Vector3(0, 0, s * 28f));
                    Cyl(t, leather * 1.08f, new Vector3(c.x, y + h.Size.y * 0.22f, c.z), w * 0.86f, h.Size.y * 0.42f);
                    Art.Box(t, leather * 0.85f, new Vector3(c.x, y + h.Size.y * 0.44f, c.z), new Vector3(w * 0.12f, 0.04f, d * 0.6f)); // (the dent along the top)
                    Cyl(t, new Color(0.15f, 0.1f, 0.07f), new Vector3(c.x, y + h.Size.y * 0.06f, c.z), w * 0.88f, 0.04f);
                    break;
                }
                case 4: // a tall black top hat with a red band, and a gold monocle on the right eye with its chain
                {
                    var black = new Color(0.07f, 0.07f, 0.08f);
                    float y = top - h.Size.y * 0.3f;
                    Cyl(t, black, new Vector3(c.x, y, c.z), w * 1.2f, 0.02f);
                    Cyl(t, black, new Vector3(c.x, y + h.Size.y * 0.42f, c.z), w * 0.72f, h.Size.y * 0.84f);
                    Cyl(t, new Color(0.65f, 0.08f, 0.1f), new Vector3(c.x, y + h.Size.y * 0.09f, c.z), w * 0.74f, 0.05f);
                    var gold = new Color(1f, 0.78f, 0.3f);
                    var eye = new Vector3(c.x - w * 0.17f, c.y - h.Size.y * 0.12f, h.Front - d * 0.03f); // (on the right eye)
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i / 14f * Mathf.PI * 2f;
                        Art.Box(t, gold, eye + new Vector3(Mathf.Cos(a) * w * 0.11f, Mathf.Sin(a) * w * 0.11f, 0), new Vector3(0.018f, w * 0.05f, 0.012f), new Vector3(0, 0, a * Mathf.Rad2Deg));
                    }
                    Cyl(t, Color.white, eye, w * 0.2f, 0.004f, new Vector3(90, 0, 0)).GetComponent<Renderer>().sharedMaterial = Art.Ghost(new Color(0.8f, 0.95f, 1f, 0.35f));
                    for (int i = 1; i < 8; i++) Ball(t, gold, eye + new Vector3(-w * 0.1f - i * 0.012f, -w * 0.11f - i * 0.03f, -i * 0.012f), Vector3.one * 0.012f);
                    break;
                }
                case 5: // a Bieber cut: smooth brown hair with a long fringe swept to the side
                {
                    var hair = new Color(0.42f, 0.27f, 0.13f);
                    Ball(t, hair, new Vector3(c.x, top - h.Size.y * 0.2f, c.z - d * 0.05f), new Vector3(w * 1.05f, h.Size.y * 0.5f, d * 1.02f));
                    Ball(t, hair * 1.08f, new Vector3(c.x + w * 0.12f, top - h.Size.y * 0.3f, h.Front - d * 0.12f), new Vector3(w * 0.8f, h.Size.y * 0.16f, d * 0.4f), new Vector3(-20f, 0, -14f));
                    Ball(t, hair * 1.12f, new Vector3(c.x + w * 0.32f, top - h.Size.y * 0.36f, h.Front - d * 0.1f), new Vector3(w * 0.36f, h.Size.y * 0.12f, d * 0.26f), new Vector3(-10f, 0, -30f));
                    break;
                }
                case 6: // a punk mohawk: tall hot pink spikes along the middle, front to back
                {
                    var pink = new Color(1f, 0.2f, 0.65f);
                    for (int i = 0; i < 7; i++)
                    {
                        float f = i / 6f;
                        float z = Mathf.Lerp(h.Front - d * 0.15f, c.z - d * 0.45f, f);
                        float y = top - h.Size.y * (0.04f + Mathf.Abs(f - 0.4f) * 0.25f);
                        Spike(t, i % 2 == 0 ? pink : new Color(0.3f, 1f, 0.4f), new Vector3(c.x, y, z), new Vector3(0, 1f, 0.15f - f * 0.5f), w * 0.2f, w * (0.55f - Mathf.Abs(f - 0.4f) * 0.3f));
                    }
                    break;
                }
                case 7: // a long wig: a dark red cap of hair, long locks down the back and the sides, a fringe
                {
                    var hair = new Color(0.55f, 0.16f, 0.12f);
                    Ball(t, hair, new Vector3(c.x, top - h.Size.y * 0.2f, c.z - d * 0.04f), new Vector3(w * 1.08f, h.Size.y * 0.55f, d * 1.08f));
                    Art.Box(t, hair, new Vector3(c.x, top - h.Size.y * 0.75f, c.z - d * 0.38f), new Vector3(w * 0.95f, h.Size.y * 1.1f, d * 0.22f), new Vector3(-6f, 0, 0));
                    foreach (float s in new[] { -1f, 1f })
                        Art.Box(t, hair * 0.95f, new Vector3(c.x + s * w * 0.48f, top - h.Size.y * 0.65f, c.z - d * 0.05f), new Vector3(w * 0.14f, h.Size.y * 0.9f, d * 0.6f), new Vector3(0, 0, s * 4f));
                    Ball(t, hair * 1.08f, new Vector3(c.x, top - h.Size.y * 0.28f, h.Front - d * 0.1f), new Vector3(w * 0.85f, h.Size.y * 0.14f, d * 0.3f));
                    Art.Box(t, new Color(1f, 0.5f, 0.75f), new Vector3(c.x + w * 0.3f, top - h.Size.y * 0.08f, c.z), new Vector3(w * 0.2f, 0.06f, 0.06f), new Vector3(0, 30f, 0)); // (a bow)
                    break;
                }
                case 8: // a knitted beanie: a snug red dome with a rolled cuff and a bobble
                {
                    var red = new Color(0.8f, 0.15f, 0.15f);
                    Ball(t, red, new Vector3(c.x, top - h.Size.y * 0.24f, c.z - d * 0.03f), new Vector3(w * 1.08f, h.Size.y * 0.75f, d * 1.08f));
                    Cyl(t, red * 0.85f, new Vector3(c.x, top - h.Size.y * 0.4f, c.z - d * 0.03f), w * 1.1f, h.Size.y * 0.14f);
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i / 12f * Mathf.PI * 2f;
                        Art.Box(t, red * 0.7f, new Vector3(c.x + Mathf.Sin(a) * w * 0.55f, top - h.Size.y * 0.4f, c.z - d * 0.03f + Mathf.Cos(a) * d * 0.55f), new Vector3(0.012f, h.Size.y * 0.13f, 0.012f), new Vector3(0, a * Mathf.Rad2Deg, 0));
                    }
                    Ball(t, Color.white, new Vector3(c.x, top + h.Size.y * 0.1f, c.z - d * 0.03f), Vector3.one * w * 0.26f);
                    break;
                }
                default: // a striped party hat, tipped a little, with a pompom
                {
                    float y = top - h.Size.y * 0.18f;
                    var tipAt = new Vector3(c.x + w * 0.05f, y, c.z);
                    var cone = Art.Part(t, Art.Cone, new Color(0.3f, 0.6f, 1f), tipAt, new Vector3(w * 0.75f, h.Size.y * 0.95f, w * 0.75f), new Vector3(0, 0, -10f));
                    for (int i = 0; i < 3; i++)
                        Art.Part(cone.transform, Art.Cone, new Color(1f, 0.85f, 0.2f), new Vector3(0, 0.2f + i * 0.25f, 0), new Vector3(1.04f - i * 0.25f, 0.08f, 1.04f - i * 0.25f));
                    Ball(t, new Color(1f, 0.3f, 0.6f), tipAt + Quaternion.Euler(0, 0, -10f) * new Vector3(0, h.Size.y * 0.95f, 0), Vector3.one * w * 0.16f);
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
            Ragdoll.Spawn(model.gameObject, m_HatGo, push);
        }

        [Rpc(SendTo.Server)]
        public void SetHatRpc(byte hat) => Hat.Value = (byte)Mathf.Clamp(hat, 0, Cosmetics.HatCount - 1);

        /// <summary>Every frame: the hat on this player's alien is the one they picked (yours only shows in your shadow).</summary>
        void TickHat()
        {
            if (m_HatShown == Hat.Value || m_Anim == null || m_Anim.HeadBone == null) return;
            m_HatShown = Hat.Value;
            if (m_HatGo) Destroy(m_HatGo);
            m_HatGo = Cosmetics.Wear(Hat.Value, m_VisualRoot, m_Anim, IsOwner);
        }
    }
}
