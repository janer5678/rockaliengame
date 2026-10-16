using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The first-person alien hands (Settings > Display > Alien hands; off brings back the old square ones): kept really
    /// simple - a smooth round mitten of a fist with a thumb, a wrist and a plain round forearm, in your alien's colour
    /// (HandColorHook.AlienShade). No fingers to pose: the fist sits where the block-hand poses put it (round the handle
    /// of whatever's held), and each forearm turns towards your side every frame so the arms come in from the bottom
    /// corners of the screen.
    ///
    /// The hand's own frame (the RIGHT hand; the left is its mirror image): the fist at the origin, the knuckles along +Z,
    /// the palm facing -Y, the thumb on the -X side.
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>Where each forearm heads, in the view (right hand; the left's is its mirror): down by your side.</summary>
        static readonly Vector3 k_ElbowAnchor = new Vector3(0.36f, -0.66f, -0.02f);

        /// <summary>Builds the simple hand under `arm` ("alien": hidden while the square hands are picked).</summary>
        static void BuildSimpleHand(Transform arm, HandColorHook hook, bool right)
        {
            float sx = right ? 1f : -1f;
            var skin = HandColorHook.Shade(Color.white, HandColorHook.AlienSkin); // (the hook gives it the real colour)
            var root = new GameObject("alien").transform;
            root.SetParent(arm, false);
            // (each part by its real size in metres: the built-in meshes aren't one unit across)
            void Part(Transform t, Mesh mesh, Vector3 pos, Vector3 size, Vector3 euler, string name)
            {
                var e = mesh.bounds.extents * 2f;
                var scale = new Vector3(size.x / Mathf.Max(1e-4f, e.x), size.y / Mathf.Max(1e-4f, e.y), size.z / Mathf.Max(1e-4f, e.z));
                hook.Track(Art.Part(t, mesh, skin, pos, scale, euler, false, null, name), HandColorHook.AlienSkin);
            }
            // the fist: a smooth round mitten, a little longer than it's wide; the thumb lying along its inner side
            Part(root, Art.Sphere, new Vector3(0f, 0f, 0.012f), new Vector3(0.082f, 0.072f, 0.1f), Vector3.zero, "fist");
            Part(root, Art.Sphere, new Vector3(-0.034f * sx, 0.014f, 0.024f), new Vector3(0.034f, 0.032f, 0.046f), new Vector3(0f, 18f * sx, 0f), "thumb"); // (a round nub, not a spike)
            Part(root, Art.Sphere, new Vector3(0f, -0.002f, -0.042f), new Vector3(0.056f, 0.052f, 0.06f), Vector3.zero, "wrist");
            // the forearm: a plain round arm from the wrist back off the screen (turned each frame: AimForearms)
            var fore = new GameObject("forearm").transform;
            fore.SetParent(root, false);
            fore.localPosition = new Vector3(0f, 0f, -0.04f);
            Part(fore, Art.Capsule, new Vector3(0f, 0f, -0.3f), new Vector3(0.052f, 0.64f, 0.048f), new Vector3(90f, 0f, 0f), "arm"); // (0.64 long: from the wrist back off the screen)
        }

        /// <summary>Each forearm heads from the wrist down towards your side (k_ElbowAnchor), bent a little towards
        /// straight on from the fist so the wrist never kinks.</summary>
        void AimForearms()
        {
            foreach (var arm in new[] { m_R, m_L })
            {
                var fore = arm != null ? arm.Find("alien/forearm") : null;
                if (fore == null || !fore.gameObject.activeInHierarchy) continue;
                float sx = arm == m_R ? 1f : -1f;
                var anchor = m_Root.TransformPoint(new Vector3(k_ElbowAnchor.x * sx, k_ElbowAnchor.y, k_ElbowAnchor.z));
                var toAnchor = anchor - fore.position;
                if (toAnchor.sqrMagnitude < 1e-6f) continue;
                var back = Vector3.Lerp(toAnchor.normalized, -arm.forward, 0.3f);
                // a hand put away down below the view: its arm hangs on down (towards the anchor it'd point up into the view)
                float low = Mathf.InverseLerp(k_ElbowAnchor.y + 0.18f, k_ElbowAnchor.y + 0.02f, m_Root.InverseTransformPoint(fore.position).y);
                if (low > 0f) back = Vector3.Lerp(back, m_Root.TransformDirection(new Vector3(0.25f * sx, -1f, -0.35f)).normalized, low);
                if (back.sqrMagnitude < 1e-6f) continue;
                fore.rotation = Quaternion.LookRotation(-back.normalized, arm.up);
            }
        }
    }
}
