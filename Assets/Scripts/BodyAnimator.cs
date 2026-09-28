using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Procedural animation of the rigged PSX alien (Resources/Alien/AlienRigged, humanoid bone names):
    /// walk / sprint / crouch / jump cycles from the player's real movement, head look, item holding,
    /// weapon swings (one or two handed), ball carrying and a death fall. Runs on every peer.
    /// Rotations are given in character space (x right, y up, z forward); negative X swings a limb forward.
    /// </summary>
    public class BodyAnimator
    {
        public struct Pose
        {
            public bool Crouch, Dead, Carrying, TwoHanded, Holding;
            public float Pitch, Swing, DeadTime;
        }

        readonly Transform m_Root;   // character space (unscaled visual root)
        readonly Transform m_Model;  // scaled alien
        readonly Dictionary<Transform, (Quaternion local, Quaternion rel)> m_Rest = new Dictionary<Transform, (Quaternion, Quaternion)>();
        readonly Transform m_Hips, m_Spine, m_Chest, m_Neck, m_Head, m_LUp, m_LLo, m_RUp, m_RLo, m_LArm, m_LFore, m_RArm, m_RFore, m_LHand, m_RHand;
        readonly Vector3 m_ModelBase;
        Vector3 m_LastPos;
        float m_Phase, m_Speed, m_Crouch, m_Air, m_VelY, m_Hold, m_Carry;
        bool m_First = true;

        public Transform RightHand => m_RHand;
        public Transform LeftHand => m_LHand;

        public static BodyAnimator TryCreate(Transform visualRoot, float width, out GameObject model)
        {
            model = null;
            var prefab = Resources.Load<GameObject>("Alien/AlienRigged");
            if (prefab == null) return null;
            model = Object.Instantiate(prefab, visualRoot, false);
            model.name = "alien";
            model.transform.localScale = new Vector3(width, 1f, width);
            var a = new BodyAnimator(visualRoot, model.transform);
            return a.m_Hips != null && a.m_RHand != null ? a : null;
        }

        BodyAnimator(Transform root, Transform model)
        {
            m_Root = root;
            m_Model = model;
            m_ModelBase = model.localPosition;
            var map = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) map[t.name] = t;
            Transform B(string n) => map.TryGetValue(n, out var t) ? t : null;
            m_Hips = B("Hips"); m_Spine = B("Spine"); m_Chest = B("Chest"); m_Neck = B("Neck"); m_Head = B("Head");
            m_LUp = B("LeftUpperLeg"); m_LLo = B("LeftLowerLeg"); m_RUp = B("RightUpperLeg"); m_RLo = B("RightLowerLeg");
            m_LArm = B("LeftUpperArm"); m_LFore = B("LeftLowerArm"); m_LHand = B("LeftHand");
            m_RArm = B("RightUpperArm"); m_RFore = B("RightLowerArm"); m_RHand = B("RightHand");
            foreach (var b in new[] { m_Hips, m_Spine, m_Chest, m_Neck, m_Head, m_LUp, m_LLo, m_RUp, m_RLo, m_LArm, m_LFore, m_RArm, m_RFore, m_LHand, m_RHand })
                if (b != null) m_Rest[b] = (b.localRotation, Quaternion.Inverse(root.rotation) * b.rotation);
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.updateWhenOffscreen = true;
                r.localBounds = new Bounds(new Vector3(0, 0.9f, 0), new Vector3(2.5f, 2.5f, 2.5f));
            }
        }

        void Rot(Transform b, Vector3 euler)
        {
            if (b == null || !m_Rest.TryGetValue(b, out var r)) return;
            b.localRotation = r.local * (Quaternion.Inverse(r.rel) * Quaternion.Euler(euler) * r.rel);
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        public void Tick(Pose p, float dt)
        {
            if (dt <= 0) return;
            var pos = m_Root.position;
            if (m_First) { m_LastPos = pos; m_First = false; }
            var v = (pos - m_LastPos) / dt;
            m_LastPos = pos;
            if (v.sqrMagnitude > 400f) v = Vector3.zero; // teleport
            Animate(p, dt, v);
        }

        /// <summary>Drive the animation from a known velocity (used by the screenshot demo).</summary>
        public void TickWithVelocity(Pose p, float dt, Vector3 v)
        {
            m_LastPos = m_Root.position;
            m_First = false;
            Animate(p, dt, v);
        }

        void Animate(Pose p, float dt, Vector3 v)
        {
            var pos = m_Root.position;
            float hs = new Vector2(v.x, v.z).magnitude;
            m_Speed = Mathf.Lerp(m_Speed, hs, dt * 10f);
            m_VelY = Mathf.Lerp(m_VelY, v.y, dt * 10f);
            bool grounded = Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, 0.55f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore);
            m_Air = Mathf.MoveTowards(m_Air, grounded ? 0f : 1f, dt * 6f);
            m_Crouch = Mathf.MoveTowards(m_Crouch, p.Crouch ? 1f : 0f, dt * 6f);
            m_Hold = Mathf.MoveTowards(m_Hold, p.Holding ? 1f : 0f, dt * 5f);
            m_Carry = Mathf.MoveTowards(m_Carry, p.Carrying ? 1f : 0f, dt * 6f);

            // ---- gait ----
            float sprint = Mathf.Clamp01((m_Speed - 5.5f) / 2f);
            m_Phase += dt * m_Speed / 1.15f * Mathf.PI * (m_Crouch > 0.5f ? 1.25f : 1f);
            float move = Mathf.Clamp01(m_Speed / 2.5f) * (1f - m_Air);
            float amp = (32f + 16f * sprint) * move;
            float s = Mathf.Sin(m_Phase), sb = Mathf.Sin(m_Phase + Mathf.PI);
            float lean = 14f * sprint * move + 28f * m_Crouch;

            float thighC = -75f * m_Crouch, kneeC = 115f * m_Crouch;
            float thighAir = -40f * m_Air, kneeAir = 70f * m_Air;
            Rot(m_LUp, new Vector3(-s * amp + thighC + thighAir, 0, 0));
            Rot(m_RUp, new Vector3(-sb * amp + thighC + thighAir, 0, 0));
            Rot(m_LLo, new Vector3(Mathf.Max(0f, Mathf.Sin(m_Phase - 1.2f)) * amp * 1.4f + kneeC + kneeAir, 0, 0));
            Rot(m_RLo, new Vector3(Mathf.Max(0f, Mathf.Sin(m_Phase + Mathf.PI - 1.2f)) * amp * 1.4f + kneeC + kneeAir, 0, 0));
            float bounce = Mathf.Abs(Mathf.Cos(m_Phase)) * 0.04f * move;
            m_Model.localPosition = m_ModelBase + new Vector3(0, bounce - 0.36f * m_Crouch, 0);

            // ---- torso / head ----
            Rot(m_Spine, new Vector3(lean * 0.6f, Mathf.Sin(m_Phase) * 5f * move, 0));
            Rot(m_Chest, new Vector3(lean * 0.4f + p.Pitch * 0.15f, 0, 0));
            Rot(m_Neck, new Vector3(p.Pitch * 0.25f - lean * 0.3f, 0, 0));
            Rot(m_Head, new Vector3(p.Pitch * 0.45f - lean * 0.3f, 0, 0));

            // ---- arms ----
            float armSwing = amp * 0.8f;
            var lArm = new Vector3(sb * armSwing * -1f, 0, -8f - 35f * m_Air);
            var rArm = new Vector3(s * armSwing * -1f, 0, 8f + 35f * m_Air);
            var lFore = new Vector3(-15f * move - 20f * sprint, 0, 0);
            var rFore = lFore;

            // holding something: right hand forward
            rArm = Vector3.Lerp(rArm, new Vector3(-35f, 0, 10f), m_Hold);
            rFore = Vector3.Lerp(rFore, new Vector3(-45f, 0, 0), m_Hold);

            // swing: raise over the shoulder, slam down, recover (both arms for two-handed weapons)
            if (p.Swing > 0f)
            {
                float u = 1f - p.Swing;
                float x = u < 0.25f ? Mathf.Lerp(-35f, -165f, Smooth(u / 0.25f))
                        : u < 0.45f ? Mathf.Lerp(-165f, -15f, Smooth((u - 0.25f) / 0.2f))
                        : Mathf.Lerp(-15f, -35f, Smooth((u - 0.45f) / 0.55f));
                float fore = u < 0.25f ? -70f : u < 0.45f ? Mathf.Lerp(-70f, -10f, (u - 0.25f) / 0.2f) : -35f;
                rArm = new Vector3(x, 0, 8f);
                rFore = new Vector3(fore, 0, 0);
                if (p.TwoHanded) { lArm = new Vector3(x, 0, -8f); lFore = rFore; }
            }
            if (m_Carry > 0f)
            {
                lArm = Vector3.Lerp(lArm, new Vector3(-75f, 0, 18f), m_Carry);
                rArm = Vector3.Lerp(rArm, new Vector3(-75f, 0, -18f), m_Carry);
                lFore = Vector3.Lerp(lFore, new Vector3(-35f, 0, 0), m_Carry);
                rFore = Vector3.Lerp(rFore, new Vector3(-35f, 0, 0), m_Carry);
            }
            Rot(m_LArm, lArm);
            Rot(m_RArm, rArm);
            Rot(m_LFore, lFore);
            Rot(m_RFore, rFore);

            // ---- death: topple backwards ----
            float d = p.Dead ? Smooth(p.DeadTime / 0.5f) : 0f;
            m_Model.localRotation = Quaternion.Euler(-88f * d, 0, 0);
            if (d > 0) m_Model.localPosition += new Vector3(0, 0.25f * d, -0.15f * d);
        }

        /// <summary>World pose for a held item: in the right fist, handle pointing forward.</summary>
        public void GripPose(out Vector3 pos, out Quaternion rot)
        {
            var down = m_RFore != null ? (m_RHand.position - m_RFore.position).normalized : -m_Root.up;
            pos = m_RHand.position + down * 0.12f;
            // item +Z along the forearm, item +Y (the tool head) perpendicular to it in the swing plane
            rot = Quaternion.LookRotation(down, Vector3.Cross(down, m_Root.right));
        }
    }
}
