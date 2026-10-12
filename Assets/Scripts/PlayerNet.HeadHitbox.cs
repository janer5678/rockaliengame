using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The head hitbox: a box fitted round the alien's real head (measured once from the rigged model's head vertices),
    /// following the head bone as it's animated (crouching, looking up and down, swinging) - only a shot that really goes
    /// into the head is a headshot. The body capsule stops at the neck, under it. It lives under the player, not the body
    /// (an invisible or tree-camo player still has a head to hit), and keeps the normal size on a giant (like the body).
    /// </summary>
    public partial class PlayerNet
    {
        BoxCollider m_HeadBox;
        /// <summary>The head box's pose in the player's own space (unscaled), from the last frame the body was animated.</summary>
        Vector3 m_HeadLocal = new Vector3(0f, 1.62f, 0.02f);
        Quaternion m_HeadRot = Quaternion.identity;

        /// <summary>The head's size round the head bone, in character space at rest (measured once: HeadBounds).</summary>
        static Bounds s_HeadBounds;
        static bool s_HeadMeasured, s_HeadOk;
        /// <summary>No rigged model to measure: a box roughly the alien's head (just over the bone at the jaw).</summary>
        static readonly Bounds k_FallbackHead = new Bounds(new Vector3(0f, 0.22f, 0.02f), new Vector3(0.4f, 0.46f, 0.4f));

        /// <summary>A hit this far outside the head box (m) still counts (the shooter saw the head a frame earlier).</summary>
        const float HeadSlack = 0.06f;

        void BuildHeadHitbox()
        {
            var go = new GameObject("head hitbox") { layer = HitboxLayer };
            go.transform.SetParent(transform, false);
            m_HeadBox = go.AddComponent<BoxCollider>();
            if (!s_HeadMeasured && m_Anim != null && m_Anim.HeadBone != null) MeasureHead();
            var b = s_HeadOk ? s_HeadBounds : k_FallbackHead;
            m_HeadBox.size = b.size;
            m_HeadBox.center = Vector3.zero;
            m_HeadLocal = new Vector3(0f, (Crouch.Value ? 1.02f : 1.4f), 0f) + b.center;
            PlaceHeadBox();
        }

        /// <summary>The rigged alien's head (every vertex skinned mostly to the Head bone or a bone under it), measured in
        /// character space round the head bone, as the model stands at rest. Once, from the first body built.</summary>
        void MeasureHead()
        {
            s_HeadMeasured = true;
            var head = m_Anim.HeadBone;
            var root = m_VisualRoot;
            var origin = root.InverseTransformPoint(head.position);
            bool any = false;
            var bounds = new Bounds();
            try
            {
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src = smr.sharedMesh;
                    var bones = smr.bones;
                    if (src == null || !src.isReadable || bones == null || bones.Length == 0) continue;
                    var isHead = new bool[bones.Length];
                    bool hasHead = false;
                    for (int i = 0; i < bones.Length; i++) { isHead[i] = bones[i] != null && (bones[i] == head || bones[i].IsChildOf(head)); hasHead |= isHead[i]; }
                    if (!hasHead) continue;
                    var bw = src.boneWeights;
                    var bind = src.bindposes;
                    var v = src.vertices;
                    if (bw.Length != v.Length) continue;
                    var skin = new Matrix4x4[bones.Length];
                    for (int i = 0; i < bones.Length; i++) skin[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;
                    for (int i = 0; i < v.Length; i++)
                    {
                        var w = bw[i];
                        float hw = (isHead[w.boneIndex0] ? w.weight0 : 0f) + (isHead[w.boneIndex1] ? w.weight1 : 0f) + (isHead[w.boneIndex2] ? w.weight2 : 0f) + (isHead[w.boneIndex3] ? w.weight3 : 0f);
                        if (hw < 0.5f) continue;
                        var p = Vector3.zero;
                        if (w.weight0 > 0f) p += skin[w.boneIndex0].MultiplyPoint3x4(v[i]) * w.weight0;
                        if (w.weight1 > 0f) p += skin[w.boneIndex1].MultiplyPoint3x4(v[i]) * w.weight1;
                        if (w.weight2 > 0f) p += skin[w.boneIndex2].MultiplyPoint3x4(v[i]) * w.weight2;
                        if (w.weight3 > 0f) p += skin[w.boneIndex3].MultiplyPoint3x4(v[i]) * w.weight3;
                        var c = root.InverseTransformPoint(p) - origin;
                        if (!any) { bounds = new Bounds(c, Vector3.zero); any = true; }
                        else bounds.Encapsulate(c);
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[RockGame] couldn't measure the alien's head: " + e.Message); any = false; }
            // (a little bigger than the mesh, so a shot grazing the skull still counts; never something silly)
            if (any && bounds.size.y > 0.15f && bounds.size.y < 1.2f && bounds.size.x > 0.1f && bounds.size.x < 1.2f)
            {
                bounds.Expand(0.02f);
                s_HeadBounds = bounds;
                s_HeadOk = true;
            }
        }

        /// <summary>LateUpdate, after the body is posed: the head box follows the head bone (in the player's unscaled
        /// space, so a giant's head box stays where a normal head would be).</summary>
        void TickHeadHitbox()
        {
            if (m_HeadBox == null) return;
            var b = s_HeadOk ? s_HeadBounds : k_FallbackHead;
            if (IsBear)
            {
                // a bear's head is out in front of it (PlayerNet.WackyLooks)
                m_HeadRot = Quaternion.identity;
                m_HeadLocal = new Vector3(0f, 1.3f, 1.05f);
            }
            else if (m_Anim != null && m_Anim.HeadBone != null && m_VisualRoot.gameObject.activeInHierarchy)
            {
                var head = m_Anim.HeadBone;
                // the head's frame in character axes (identity at rest), and where the bone is, unscaled by a giant's size
                var rot = Quaternion.Inverse(m_VisualRoot.rotation) * head.rotation * m_Anim.HeadFrameOffset;
                var bone = m_VisualRoot.InverseTransformPoint(head.position); // (character space: the visual root's scale taken out)
                // (turned over by the gravity staff: the body's upside down in the player's space)
                var vr = m_VisualRoot.localRotation;
                m_HeadRot = vr * rot;
                m_HeadLocal = m_VisualRoot.localPosition + vr * (bone + rot * b.center);
            }
            else if (m_Anim == null)
            {
                // the old alien / block body: a head over the neck (lower crouching)
                m_HeadRot = Quaternion.identity;
                m_HeadLocal = new Vector3(0f, Crouch.Value ? 1.02f : 1.4f, 0f) + b.center;
            }
            PlaceHeadBox();
        }

        void PlaceHeadBox()
        {
            if (m_HeadBox == null) return;
            m_HeadBox.transform.localPosition = m_HeadLocal;
            m_HeadBox.transform.localRotation = m_HeadRot;
            m_HeadBox.transform.localScale = Vector3.one;
        }

        /// <summary>Where the bottom of the head box is over the feet (the body capsule stops there).</summary>
        float HeadBottomY => m_HeadLocal.y - HeadHalfY;

        /// <summary>Half the head box's height, as it's turned.</summary>
        float HeadHalfY
        {
            get
            {
                var b = s_HeadOk ? s_HeadBounds : k_FallbackHead;
                return b.extents.y * Mathf.Abs((m_HeadRot * Vector3.up).y) + b.extents.z * Mathf.Abs((m_HeadRot * Vector3.forward).y) + b.extents.x * Mathf.Abs((m_HeadRot * Vector3.right).y);
            }
        }

        /// <summary>Update: the head box is on while the body is, and the body capsule reaches up to the neck only.</summary>
        void TickBodyHitbox(bool dead, float standHeight)
        {
            if (m_HeadBox != null) m_HeadBox.enabled = !dead;
            m_Hitbox.radius = IsBear ? 0.75f : Cfg.HitboxRadius; // (a bear's a bigger target)
            if (m_HeadBox != null && m_HeadLocal.y < standHeight * 0.5f)
            {
                // upside down (the gravity staff): the head's at the bottom, the body above it
                float bottom = Mathf.Clamp(m_HeadLocal.y + HeadHalfY - 0.03f, 0f, standHeight - m_Hitbox.radius * 2f);
                m_Hitbox.height = Mathf.Max(standHeight + 0.05f - bottom, m_Hitbox.radius * 2f);
                m_Hitbox.center = new Vector3(0, bottom + m_Hitbox.height * 0.5f, 0);
                return;
            }
            float top = m_HeadBox != null ? HeadBottomY + 0.03f : standHeight + 0.05f;
            top = Mathf.Clamp(top, m_Hitbox.radius * 2f, standHeight + 0.05f);
            m_Hitbox.height = top;
            m_Hitbox.center = new Vector3(0, top * 0.5f, 0);
        }

        /// <summary>Was a hit at this point (on a hitbox surface) on the head? Inside the head box, give or take HeadSlack -
        /// and not below its bottom (that's the top of the body capsule, at the neck).</summary>
        public bool IsHeadshot(Vector3 point)
        {
            if (m_HeadBox == null) return point.y > transform.position.y + (Crouch.Value ? 1.09f : 1.45f);
            var b = s_HeadOk ? s_HeadBounds : k_FallbackHead;
            var local = Quaternion.Inverse(m_HeadBox.transform.rotation) * (point - m_HeadBox.transform.position);
            var e = b.extents;
            bool upside = m_HeadLocal.y < (Crouch.Value ? Cfg.CrouchHeight : Cfg.StandHeight) * 0.5f;
            if (upside) return Mathf.Abs(local.x) <= e.x + HeadSlack && Mathf.Abs(local.z) <= e.z + HeadSlack && Mathf.Abs(local.y) <= e.y + HeadSlack;
            return Mathf.Abs(local.x) <= e.x + HeadSlack && Mathf.Abs(local.z) <= e.z + HeadSlack
                && local.y <= e.y + HeadSlack && local.y >= -e.y - 0.01f;
        }

        /// <summary>The middle of the head (bots aim here; tests).</summary>
        public Vector3 HeadCenter => m_HeadBox != null ? m_HeadBox.transform.position : EyePos;
    }
}
