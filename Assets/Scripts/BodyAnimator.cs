using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Procedural animation of the rigged PSX alien (Resources/Alien/AlienRigged, humanoid bone names), in the style of
    /// the usual Mixamo locomotion set: idle breathing, walk / run / sprint forwards, backwards and strafing, crouch walk,
    /// jump (tuck on the way up, legs reaching down on the way down), landing dip, head look, item holding, weapon swings
    /// (one or two handed), ball carrying and a death fall, plus the actions other players need to see: sliding, drawing
    /// a bow, winding up a spear, aiming a crossbow / sniper, eating, charging the ram, running the chainsaw and throwing.
    /// Driven by the player's real movement and their synced action; runs on every peer.
    /// Rotations are given in character space (x right, y up, z forward); negative X swings a limb forward.
    /// </summary>
    public class BodyAnimator
    {
        /// <summary>What the player is doing with their hands / body right now (synced, see PlayerNet.Action).</summary>
        public enum Act : byte { None, Slide, BowDraw, SpearAim, Aim, Eat, Ram, Saw }

        public struct Pose
        {
            public bool Crouch, Dead, Carrying, TwoHanded, Holding, Riding;
            public float Pitch, Swing, DeadTime;
            public Act Action;
            /// <summary>1 right after a throw, falling to 0 (overhand throw).</summary>
            public float Throw;
        }

        readonly Transform m_Root;   // character space (unscaled visual root)
        readonly Transform m_Model;  // scaled alien
        readonly Dictionary<Transform, (Quaternion local, Quaternion rel)> m_Rest = new Dictionary<Transform, (Quaternion, Quaternion)>();
        readonly Transform m_Hips, m_Spine, m_Chest, m_Neck, m_Head, m_LUp, m_LLo, m_RUp, m_RLo, m_LArm, m_LFore, m_RArm, m_RFore, m_LHand, m_RHand;
        readonly Vector3 m_ModelBase;
        Vector3 m_LastPos;
        float m_Phase, m_Speed, m_Crouch, m_Air, m_VelY, m_Hold, m_Carry, m_Fwd = 1f, m_Side, m_Land, m_AirVel;
        // each action blends in and out (0..1); m_Draw fills up while a bow / spear is held drawn
        float m_Slide, m_Bow, m_SpearAim, m_Aim, m_Eat, m_Ram, m_Saw, m_Draw;
        float m_SlideYaw;
        // while aiming, the held item points where the player looks instead of along the forearm
        float m_GripW;
        Quaternion m_GripRot = Quaternion.identity;
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
            // the Generic rig import adds an Animator we don't use (the bones are posed in code)
            foreach (var an in model.GetComponentsInChildren<Animator>(true)) Object.Destroy(an);
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
            // The model is modelled in an A-pose (arms out at an angle). Treat "arms hanging at the sides, legs straight
            // down" as the rest pose instead, so every pose below means what it says (x swings a limb forward/back, z out).
            Straighten(root, m_LArm, m_LFore, new Vector3(-0.1f, -1f, 0f), m_LFore, m_LHand);
            Straighten(root, m_RArm, m_RFore, new Vector3(0.1f, -1f, 0f), m_RFore, m_RHand);
            Straighten(root, m_LUp, m_LLo, new Vector3(-0.04f, -1f, 0f), m_LLo);
            Straighten(root, m_RUp, m_RLo, new Vector3(0.04f, -1f, 0f), m_RLo);
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.updateWhenOffscreen = true;
                r.localBounds = new Bounds(new Vector3(0, 0.9f, 0), new Vector3(2.5f, 2.5f, 2.5f));
            }
        }

        /// <summary>
        /// Re-bases a limb: its rest direction (bone -> child joint) is turned to `want`, and the bones below it come along
        /// (their rest orientation is turned the same way, their local rest stays as it is).
        /// </summary>
        void Straighten(Transform root, Transform bone, Transform child, Vector3 want, params Transform[] below)
        {
            if (bone == null || child == null || !m_Rest.ContainsKey(bone)) return;
            var dir = Quaternion.Inverse(root.rotation) * (child.position - bone.position);
            if (dir.sqrMagnitude < 1e-6f) return;
            var fix = Quaternion.FromToRotation(dir.normalized, want.normalized);
            var r = m_Rest[bone];
            m_Rest[bone] = (r.local, fix * r.rel);
            foreach (var b in below)
                if (b != null && m_Rest.TryGetValue(b, out var rb)) m_Rest[b] = (rb.local, fix * rb.rel);
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
            m_Slide = Mathf.MoveTowards(m_Slide, p.Action == Act.Slide ? 1f : 0f, dt * 9f);
            m_Bow = Mathf.MoveTowards(m_Bow, p.Action == Act.BowDraw ? 1f : 0f, dt * 8f);
            m_SpearAim = Mathf.MoveTowards(m_SpearAim, p.Action == Act.SpearAim ? 1f : 0f, dt * 8f);
            m_Aim = Mathf.MoveTowards(m_Aim, p.Action == Act.Aim ? 1f : 0f, dt * 9f);
            m_Eat = Mathf.MoveTowards(m_Eat, p.Action == Act.Eat ? 1f : 0f, dt * 7f);
            m_Ram = Mathf.MoveTowards(m_Ram, p.Action == Act.Ram ? 1f : 0f, dt * 6f);
            m_Saw = Mathf.MoveTowards(m_Saw, p.Action == Act.Saw ? 1f : 0f, dt * 10f);
            bool drawing = p.Action == Act.BowDraw || p.Action == Act.SpearAim;
            m_Draw = drawing ? Mathf.MoveTowards(m_Draw, 1f, dt / 0.7f) : 0f;

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
            float move = Mathf.Clamp01(m_Speed / 2.5f) * (1f - m_Air) * (1f - m_Slide);
            float amp = (32f + 18f * sprint - 10f * crouchWalk) * move;
            float s = Mathf.Sin(m_Phase), sb = Mathf.Sin(m_Phase + Mathf.PI);
            float fwd = Mathf.Clamp(m_Fwd, -1f, 1f), side = Mathf.Clamp(m_Side, -1f, 1f);
            float lean = (14f * sprint * move) * Mathf.Max(0f, fwd) - 6f * move * Mathf.Max(0f, -fwd) + 28f * m_Crouch * (1f - m_Slide) + 22f * m_Ram;

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

            // ---- actions (the right hand holds the item; poses aim with the head's pitch) ----
            float pitch = Mathf.Clamp(p.Pitch, -60f, 60f);
            float torsoYaw = 0f;
            if (m_Bow > 0f)
            {
                // bow: the bow arm straight out at the target, the other hand draws the string back to the cheek
                float dr = Smooth(m_Draw);
                rArm = Vector3.Lerp(rArm, new Vector3(-88f + pitch, 0, 6f), m_Bow);
                rFore = Vector3.Lerp(rFore, new Vector3(-4f, 0, 0), m_Bow);
                lArm = Vector3.Lerp(lArm, new Vector3(Mathf.Lerp(-80f, -70f, dr) + pitch, 0, Mathf.Lerp(-20f, 40f, dr)), m_Bow);
                lFore = Vector3.Lerp(lFore, new Vector3(Mathf.Lerp(-40f, -140f, dr), 0, 0), m_Bow);
                torsoYaw += -25f * m_Bow;
            }
            if (m_SpearAim > 0f)
            {
                // spear: cocked back over the shoulder, the free arm pointing where it'll go; pulls further back as it winds up
                float dr = Smooth(m_Draw);
                rArm = Vector3.Lerp(rArm, new Vector3(Mathf.Lerp(-150f, -170f, dr) + pitch * 0.3f, 0, 28f), m_SpearAim);
                rFore = Vector3.Lerp(rFore, new Vector3(Mathf.Lerp(-55f, -95f, dr), 0, 0), m_SpearAim);
                lArm = Vector3.Lerp(lArm, new Vector3(-75f + pitch * 0.6f, 0, -12f), m_SpearAim);
                lFore = Vector3.Lerp(lFore, new Vector3(-10f, 0, 0), m_SpearAim);
                torsoYaw += 28f * m_SpearAim * (0.6f + 0.4f * dr);
            }
            if (m_Aim > 0f)
            {
                // crossbow / sniper to the shoulder: both hands on it, looking down the sights
                rArm = Vector3.Lerp(rArm, new Vector3(-72f + pitch, 0, 22f), m_Aim);
                rFore = Vector3.Lerp(rFore, new Vector3(-95f, 0, 0), m_Aim);
                lArm = Vector3.Lerp(lArm, new Vector3(-88f + pitch, 0, -28f), m_Aim);
                lFore = Vector3.Lerp(lFore, new Vector3(-30f, 0, 0), m_Aim);
                torsoYaw += -10f * m_Aim;
            }
            if (m_Eat > 0f)
            {
                // food up to the mouth, chewing
                float chew = Mathf.Sin(Time.time * 14f) * 6f;
                rArm = Vector3.Lerp(rArm, new Vector3(-55f + chew, 0, 32f), m_Eat);
                rFore = Vector3.Lerp(rFore, new Vector3(-135f, 0, 0), m_Eat);
            }
            if (m_Ram > 0f)
            {
                // ram: both hands on the log, held low, leaning in and shaking as it winds up
                float shake = Mathf.Sin(Time.time * 40f) * 3f * m_Ram;
                rArm = Vector3.Lerp(rArm, new Vector3(-35f + shake, 0, 12f), m_Ram);
                rFore = Vector3.Lerp(rFore, new Vector3(-55f, 0, 0), m_Ram);
                lArm = Vector3.Lerp(lArm, new Vector3(-45f - shake, 0, -20f), m_Ram);
                lFore = Vector3.Lerp(lFore, new Vector3(-65f, 0, 0), m_Ram);
            }
            if (m_Saw > 0f)
            {
                // chainsaw running: held out at waist height, buzzing
                float buzz = Mathf.Sin(Time.time * 60f) * 2.5f * m_Saw;
                rArm = Vector3.Lerp(rArm, new Vector3(-55f + buzz, 0, 14f), m_Saw);
                rFore = Vector3.Lerp(rFore, new Vector3(-40f, 0, 0), m_Saw);
                lArm = Vector3.Lerp(lArm, new Vector3(-60f - buzz, 0, -22f), m_Saw);
                lFore = Vector3.Lerp(lFore, new Vector3(-55f, 0, 0), m_Saw);
            }
            if (p.Throw > 0f)
            {
                // overhand throw: whips from behind the head through to a follow-through across the body
                float u = 1f - p.Throw;
                float x = u < 0.3f ? Mathf.Lerp(-160f, -150f, u / 0.3f) : Mathf.Lerp(-150f, -15f, Smooth((u - 0.3f) / 0.35f));
                float blend = Mathf.Clamp01(p.Throw * 3f);
                rArm = Vector3.Lerp(rArm, new Vector3(x, 0, 18f), blend);
                rFore = Vector3.Lerp(rFore, new Vector3(u < 0.35f ? -80f : -15f, 0, 0), blend);
                lArm = Vector3.Lerp(lArm, new Vector3(-50f, 0, -20f), blend);
                torsoYaw += Mathf.Lerp(30f, -25f, Smooth((u - 0.2f) / 0.5f)) * blend;
            }
            if (torsoYaw != 0f)
            {
                Rot(m_Spine, new Vector3(lean * 0.6f + breathe * 0.6f, -twist * 1.4f + torsoYaw * 0.6f, s * 2f * move));
                Rot(m_Chest, new Vector3(lean * 0.4f + p.Pitch * 0.15f + breathe * 1.2f, -twist * 0.4f + torsoYaw * 0.4f, 0));
                Rot(m_Neck, new Vector3(p.Pitch * 0.25f - lean * 0.3f, twist * 0.5f - torsoYaw * 0.5f, 0));
                Rot(m_Head, new Vector3(p.Pitch * 0.45f - lean * 0.3f, twist * 0.5f - torsoYaw * 0.5f, 0));
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

            // ---- slide: low, leaning back, the front leg stretched out, the back leg folded under, an arm out for balance ----
            if (m_Slide > 0f && !p.Riding)
            {
                float k = Smooth(m_Slide);
                // which way we're sliding relative to where the body faces (sliding sideways turns the legs into it)
                if (hs > 0.5f) m_SlideYaw = Mathf.LerpAngle(m_SlideYaw, Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, dt * 10f);
                float yaw = Mathf.Clamp(m_SlideYaw, -70f, 70f) * k;
                Rot(m_Hips, new Vector3(0, yaw, 0));
                Rot(m_LUp, Vector3.Lerp(Vector3.zero, new Vector3(-72f, 0, -6f), k));
                Rot(m_LLo, Vector3.Lerp(Vector3.zero, new Vector3(8f, 0, 0), k));
                Rot(m_RUp, Vector3.Lerp(Vector3.zero, new Vector3(-10f, 0, 22f), k));
                Rot(m_RLo, Vector3.Lerp(Vector3.zero, new Vector3(125f, 0, 0), k));
                Rot(m_Spine, new Vector3(-18f * k, -yaw * 0.5f, 0));
                Rot(m_Chest, new Vector3(-8f * k + p.Pitch * 0.15f, -yaw * 0.3f, 0));
                Rot(m_Head, new Vector3(p.Pitch * 0.45f + 22f * k, -yaw * 0.2f, 0));
                if (!p.Holding || !p.TwoHanded)
                {
                    // the free left arm trails out behind for balance
                    Rot(m_LArm, Vector3.Lerp(lArm, new Vector3(25f, 0, -55f), k));
                    Rot(m_LFore, Vector3.Lerp(lFore, new Vector3(-20f, 0, 0), k));
                }
                if (!p.Holding) { Rot(m_RArm, Vector3.Lerp(rArm, new Vector3(-40f, 0, 35f), k)); Rot(m_RFore, Vector3.Lerp(rFore, new Vector3(-30f, 0, 0), k)); }
                m_Model.localPosition = m_ModelBase + new Vector3(0, -0.62f * k, 0);
            }

            // ---- held item aiming: a wound-up / thrown spear points ahead over the shoulder, a drawn bow stands upright
            // facing the target, a crossbow / sniper / ram lines up with the look direction ----
            var aimDir = Quaternion.Euler(Mathf.Clamp(p.Pitch, -60f, 60f), 0, 0) * Vector3.forward;
            float wSpear = Mathf.Max(m_SpearAim, p.Throw > 0f && p.Holding ? Mathf.Clamp01(p.Throw * 2.5f) : 0f);
            float wLine = Mathf.Max(m_Bow, Mathf.Max(m_Aim, m_Ram));
            if (wSpear >= wLine && wSpear > 0f) m_GripRot = Quaternion.LookRotation(Vector3.up, aimDir);          // spear: +Y (the tip) forward
            else if (wLine > 0f) m_GripRot = Quaternion.LookRotation(aimDir, Vector3.up);                           // bow / crossbow / ram: +Z forward
            m_GripW = Mathf.Max(wSpear, wLine);

            // ---- death: topple backwards ----
            float d = p.Dead ? Smooth(p.DeadTime / 0.5f) : 0f;
            // leaning back into the slide (the whole body tips a little)
            m_Model.localRotation = Quaternion.Euler(-88f * d - 12f * Smooth(m_Slide) * (1f - d), 0, 0);
            if (d > 0) m_Model.localPosition += new Vector3(0, 0.25f * d, -0.15f * d);
        }

        /// <summary>World pose for a held item: in the right fist, handle pointing forward.</summary>
        public void GripPose(out Vector3 pos, out Quaternion rot)
        {
            var down = m_RFore != null ? (m_RHand.position - m_RFore.position).normalized : -m_Root.up;
            pos = m_RHand.position + down * 0.12f;
            // item +Z along the forearm, item +Y (the tool head) perpendicular to it in the swing plane
            rot = Quaternion.LookRotation(down, Vector3.Cross(down, m_Root.right));
            if (m_GripW > 0f) rot = Quaternion.Slerp(rot, m_Root.rotation * m_GripRot, Smooth(m_GripW));
        }
    }
}
