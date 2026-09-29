using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Procedural animation of the rigged PSX alien (Resources/Alien/AlienRigged, humanoid bone names), in the style of
    /// the usual Mixamo locomotion set: idle breathing, walk / run / sprint forwards, backwards and strafing, crouch walk,
    /// jump (tuck on the way up, legs reaching down on the way down), landing dip, head look, item holding, weapon swings
    /// (one or two handed), ball carrying and a death fall. Driven by the player's real movement; runs on every peer.
    /// Rotations are given in character space (x right, y up, z forward); negative X swings a limb forward.
    /// </summary>
    public class BodyAnimator
    {
        public struct Pose
        {
            public bool Crouch, Dead, Carrying, TwoHanded, Holding, Riding;
            public float Pitch, Swing, DeadTime;
        }

        readonly Transform m_Root;   // character space (unscaled visual root)
        readonly Transform m_Model;  // scaled alien
        readonly Dictionary<Transform, (Quaternion local, Quaternion rel)> m_Rest = new Dictionary<Transform, (Quaternion, Quaternion)>();
        readonly Transform m_Hips, m_Spine, m_Chest, m_Neck, m_Head, m_LUp, m_LLo, m_RUp, m_RLo, m_LArm, m_LFore, m_RArm, m_RFore, m_LHand, m_RHand;
        readonly Vector3 m_ModelBase;
        Vector3 m_LastPos;
        float m_Phase, m_Speed, m_Crouch, m_Air, m_VelY, m_Hold, m_Carry, m_Fwd = 1f, m_Side, m_Land, m_AirVel;
        bool m_First = true;

        public Transform RightHand => m_RHand;
        public Transform LeftHand => m_LHand;
        public Transform HeadBone => m_Head;
        public Transform ChestBone => m_Chest != null ? m_Chest : m_Spine;

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
            // landing dip: a hard landing squashes the legs for a moment
            if (grounded && m_Air > 0.5f && m_AirVel < -4f) m_Land = Mathf.Clamp01(-m_AirVel / 14f);
            if (!grounded) m_AirVel = v.y;
            m_Land = Mathf.MoveTowards(m_Land, 0f, dt * 3.5f);
            m_Air = Mathf.MoveTowards(m_Air, grounded ? 0f : 1f, dt * 6f);
            m_Crouch = Mathf.MoveTowards(m_Crouch, p.Crouch ? 1f : 0f, dt * 6f);
            m_Hold = Mathf.MoveTowards(m_Hold, p.Holding ? 1f : 0f, dt * 5f);
            m_Carry = Mathf.MoveTowards(m_Carry, p.Carrying ? 1f : 0f, dt * 6f);

            // movement direction relative to where the body faces: forwards / backwards / strafing
            var local = Quaternion.Inverse(m_Root.rotation) * new Vector3(v.x, 0, v.z);
            if (hs > 0.3f)
            {
                m_Fwd = Mathf.Lerp(m_Fwd, local.z / hs, dt * 8f);
                m_Side = Mathf.Lerp(m_Side, local.x / hs, dt * 8f);
            }

            // ---- gait ----
            float sprint = Mathf.Clamp01((m_Speed - 5.5f) / 2f);
            float crouchWalk = m_Crouch;
            m_Phase += dt * m_Speed / 1.15f * Mathf.PI * (crouchWalk > 0.5f ? 1.25f : 1f);
            float move = Mathf.Clamp01(m_Speed / 2.5f) * (1f - m_Air);
            float amp = (32f + 18f * sprint - 10f * crouchWalk) * move;
            float s = Mathf.Sin(m_Phase), sb = Mathf.Sin(m_Phase + Mathf.PI);
            float fwd = Mathf.Clamp(m_Fwd, -1f, 1f), side = Mathf.Clamp(m_Side, -1f, 1f);
            float lean = (14f * sprint * move) * Mathf.Max(0f, fwd) - 6f * move * Mathf.Max(0f, -fwd) + 28f * m_Crouch;

            // crouch + landing squash
            float squash = Mathf.Max(m_Crouch, m_Land * 0.6f);
            float thighC = -75f * squash, kneeC = 115f * squash;
            // jump: tuck the knees on the way up, reach down with the legs on the way down
            float rising = Mathf.Clamp01(m_VelY / 5f) * m_Air, falling = Mathf.Clamp01(-m_VelY / 8f) * m_Air;
            float thighAir = -55f * rising - 18f * falling - 20f * m_Air * (1f - rising - falling);
            float kneeAir = 95f * rising + 25f * falling + 40f * m_Air * (1f - rising - falling);
            // legs swing along the direction of travel (strafing swings them sideways, backwards reverses the stride)
            float swingF = amp * fwd, swingS = amp * 0.55f * side;
            Rot(m_LUp, new Vector3(-s * swingF + thighC + thighAir, 0, s * swingS - 3f * (1f - m_Air)));
            Rot(m_RUp, new Vector3(-sb * swingF + thighC + thighAir * 0.8f, 0, sb * swingS + 3f * (1f - m_Air)));
            float kneeL = Mathf.Max(0f, Mathf.Sin(m_Phase - 1.2f * Mathf.Sign(fwd + 0.01f))) * amp * 1.4f;
            float kneeR = Mathf.Max(0f, Mathf.Sin(m_Phase + Mathf.PI - 1.2f * Mathf.Sign(fwd + 0.01f))) * amp * 1.4f;
            Rot(m_LLo, new Vector3(kneeL + kneeC + kneeAir, 0, 0));
            Rot(m_RLo, new Vector3(kneeR + kneeC + kneeAir * 1.15f, 0, 0));
            float bounce = Mathf.Abs(Mathf.Cos(m_Phase)) * (0.045f + 0.03f * sprint) * move;
            float breathe = Mathf.Sin(Time.time * 1.8f) * (1f - move) * (1f - m_Air);
            m_Model.localPosition = m_ModelBase + new Vector3(0, bounce - 0.36f * m_Crouch - 0.22f * m_Land, 0);

            // ---- hips / torso / head: hips twist with the stride, shoulders counter-twist ----
            float twist = s * 7f * move * (1f - 0.5f * Mathf.Abs(side));
            Rot(m_Hips, new Vector3(0, twist, -s * 2.5f * move));
            Rot(m_Spine, new Vector3(lean * 0.6f + breathe * 0.6f, -twist * 1.4f, s * 2f * move));
            Rot(m_Chest, new Vector3(lean * 0.4f + p.Pitch * 0.15f + breathe * 1.2f, -twist * 0.4f, 0));
            Rot(m_Neck, new Vector3(p.Pitch * 0.25f - lean * 0.3f, twist * 0.5f, 0));
            Rot(m_Head, new Vector3(p.Pitch * 0.45f - lean * 0.3f, twist * 0.5f, 0));

            // ---- arms: counter-swing to the legs; pumping with bent elbows when sprinting ----
            float armSwing = amp * (0.8f + 0.5f * sprint) * Mathf.Max(0.35f, Mathf.Abs(fwd));
            float airUp = 30f * m_Air + 25f * falling;
            var lArm = new Vector3(sb * armSwing * -1f, 0, -8f - airUp - 4f * breathe);
            var rArm = new Vector3(s * armSwing * -1f, 0, 8f + airUp + 4f * breathe);
            float elbow = -15f * move - 70f * sprint - 30f * m_Air;
            var lFore = new Vector3(elbow + Mathf.Min(0f, sb) * 25f * sprint, 0, 0);
            var rFore = new Vector3(elbow + Mathf.Min(0f, s) * 25f * sprint, 0, 0);

            // holding something: right hand forward, still bobbing a little with the stride
            rArm = Vector3.Lerp(rArm, new Vector3(-35f + s * armSwing * -0.25f - 15f * sprint, 0, 10f), m_Hold);
            rFore = Vector3.Lerp(rFore, new Vector3(-45f - 20f * sprint, 0, 0), m_Hold);
            if (p.TwoHanded && p.Holding)
            {
                // both hands on it
                lArm = Vector3.Lerp(lArm, new Vector3(-40f + sb * armSwing * -0.2f, 0, -18f), m_Hold);
                lFore = Vector3.Lerp(lFore, new Vector3(-60f, 0, 0), m_Hold);
            }

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
            // hands: loose and swinging when empty, gripping when holding
            Rot(m_LHand, new Vector3(-10f * move + sb * 8f * move, 0, 0));
            Rot(m_RHand, new Vector3((-10f * move + s * 8f * move) * (1f - m_Hold), 0, 0));

            // ---- sitting on a horse / in a car: thighs forward and apart, knees bent, no walk cycle ----
            if (p.Riding)
            {
                Rot(m_LUp, new Vector3(-75f, 0, -18f));
                Rot(m_RUp, new Vector3(-75f, 0, 18f));
                Rot(m_LLo, new Vector3(80f, 0, 0));
                Rot(m_RLo, new Vector3(80f, 0, 0));
                m_Model.localPosition = m_ModelBase + new Vector3(0, -0.45f, 0);
            }

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
