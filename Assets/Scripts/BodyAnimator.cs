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
            /// <summary>What's in the hands (the rock is held in both; the spear is carried like when sprinting).</summary>
            public Item Item;
            /// <summary>1 right after a throw, falling to 0 (overhand throw).</summary>
            public float Throw;
        }

        readonly Transform m_Root;   // character space (unscaled visual root)
        readonly Transform m_Model;  // scaled alien
        readonly Dictionary<Transform, (Quaternion local, Quaternion rel)> m_Rest = new Dictionary<Transform, (Quaternion, Quaternion)>();
        readonly Transform m_Hips, m_Spine, m_Chest, m_Neck, m_Head, m_LUp, m_LLo, m_RUp, m_RLo, m_LArm, m_LFore, m_RArm, m_RFore, m_LHand, m_RHand, m_LFoot, m_RFoot;
        readonly Vector3 m_ModelBase;
        Vector3 m_LastPos;
        float m_Phase, m_Speed, m_Crouch, m_Air, m_VelY, m_Hold, m_Carry, m_Fwd = 1f, m_Side, m_Land, m_AirVel;
        // each action blends in and out (0..1); m_Draw fills up while a bow / spear is held drawn
        float m_Slide, m_Bow, m_SpearAim, m_Aim, m_Eat, m_Ram, m_Saw, m_Draw;
        float m_SlideYaw, m_Rock;
        // while aiming, the held item points where the player looks instead of along the forearm
        float m_GripW;
        Quaternion m_GripRot = Quaternion.identity;
        bool m_First = true;

        public Transform RightHand => m_RHand;
        public Transform LeftHand => m_LHand;
        public Transform RightFore => m_RFore;
        public Transform LeftFore => m_LFore;
        /// <summary>The hips' height over the root (the lobby puts each seat under them).</summary>
        public float HipsHeight => m_Hips != null ? m_Root.InverseTransformPoint(m_Hips.position).y : 0.45f;
        public Transform HeadBone => m_Head;
        /// <summary>The visual root the alien hangs under (character space).</summary>
        public Transform VisualRoot => m_Root;
        /// <summary>head bone rotation x this = the head's own frame in character axes (x right, y up, z forward), whatever the pose.</summary>
        public Quaternion HeadFrameOffset => m_Head != null && m_Rest.TryGetValue(m_Head, out var r) ? Quaternion.Inverse(r.rel) : Quaternion.identity;
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
            m_LFoot = B("LeftFoot"); m_RFoot = B("RightFoot");
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

        /// <summary>
        /// The ship lobby (ShipLobby.cs): sitting about, chilling, in one of seven poses (variant) - leaning back with the
        /// arms crossed, elbows on the knees, one leg crossed over with an arm along the seat back, chatting with a hand
        /// going, smoking (a drag every few seconds), drinking a beer (a swig now and then, head back) or on their phone
        /// (head down, thumbs going, a laugh now and then) - breathing and looking round on their own (t: seconds; each
        /// alien has its own offset). The feet are put on the floor whatever the pose (the hips go where the legs say).
        /// </summary>
        public void Lounge(int variant, float t)
        {
            float br = Mathf.Sin(t * 1.6f) * 2.5f;                                   // breathing
            float look = Mathf.Sin(t * 0.37f + variant * 1.7f) * 28f + Mathf.Sin(t * 0.13f) * 12f; // looking round
            // turning to look at something (a neighbour, the telly): the neck takes some of it, the head the rest
            float tw = Mathf.Clamp01(TurnW);
            look = Mathf.Lerp(look, TurnYaw * 0.65f, tw);
            Rot(m_Hips, Vector3.zero); Rot(m_LHand, Vector3.zero); Rot(m_RHand, Vector3.zero);
            Rot(m_Neck, new Vector3(0f, TurnYaw * 0.35f * tw, 0f));
            // sitting: thighs forward, knees bent, the body down on the seat
            Rot(m_LUp, new Vector3(-84f, 0, -9f));
            Rot(m_RUp, new Vector3(-84f, 0, 9f));
            Rot(m_LLo, new Vector3(86f, 0, 0));
            Rot(m_RLo, new Vector3(86f, 0, 0));
            switch (((variant % LoungeVariants) + LoungeVariants) % LoungeVariants)
            {
                case 0: // leaning back, arms crossed
                    Rot(m_Spine, new Vector3(10f + br, 0, 0));
                    Rot(m_Chest, new Vector3(4f, 0, 0));
                    Rot(m_Head, new Vector3(-6f, look, 0));
                    Rot(m_LArm, new Vector3(-38f, 0, -8f));
                    Rot(m_LFore, new Vector3(-100f, 45f, 0));
                    Rot(m_RArm, new Vector3(-38f, 0, 8f));
                    Rot(m_RFore, new Vector3(-100f, -45f, 0));
                    break;
                case 1: // elbows on the knees, leaning in, head bobbing along to something
                    Rot(m_Spine, new Vector3(-26f, 0, 0));
                    Rot(m_Chest, new Vector3(-10f + br, 0, 0));
                    Rot(m_Head, new Vector3(-12f + Mathf.Sin(t * 2.2f) * 7f, look * 0.5f, 0));
                    Rot(m_LArm, new Vector3(-58f, 0, -4f));
                    Rot(m_LFore, new Vector3(-72f, 0, 0));
                    Rot(m_RArm, new Vector3(-58f, 0, 4f));
                    Rot(m_RFore, new Vector3(-72f, 0, 0));
                    break;
                case 2: // one leg crossed over, an arm along the back of the seat
                    Rot(m_RUp, new Vector3(-92f, 0, -22f));
                    Rot(m_RLo, new Vector3(58f, 0, 0));
                    Rot(m_Spine, new Vector3(6f + br, 0, 0));
                    Rot(m_Chest, new Vector3(2f, 0, 0));
                    Rot(m_Head, new Vector3(-2f, look, 0));
                    Rot(m_LArm, new Vector3(18f, 0, -32f));
                    Rot(m_LFore, new Vector3(-22f, 0, 0));
                    Rot(m_RArm, new Vector3(-28f, 0, 10f));
                    Rot(m_RFore, new Vector3(-62f, 0, 0));
                    break;
                case 3: // chatting: a hand going, nodding, turning to the others
                    Rot(m_Spine, new Vector3(br, Mathf.Sin(t * 0.5f) * 12f, 0));
                    Rot(m_Chest, new Vector3(-4f, 0, 0));
                    Rot(m_Head, new Vector3(Mathf.Sin(t * 2.6f) * 7f, look, 0));
                    Rot(m_RArm, new Vector3(-36f + Mathf.Sin(t * 3f) * 10f, 0, 16f));
                    Rot(m_RFore, new Vector3(-78f + Mathf.Sin(t * 3.5f) * 18f, 0, 0));
                    Rot(m_LArm, new Vector3(-30f, 0, -10f));
                    Rot(m_LFore, new Vector3(-72f, 0, 0));
                    break;
                case 4: // smoking: leaning back, legs crossed, a drag every few seconds (the left arm across the belly)
                {
                    float d = Drag(t);
                    Rot(m_RUp, new Vector3(-92f, 0, -22f));
                    Rot(m_RLo, new Vector3(58f, 0, 0));
                    Rot(m_Spine, new Vector3(12f + br, 0, 0));
                    Rot(m_Chest, new Vector3(4f - d * 4f, 0, 0));
                    Rot(m_Head, new Vector3(-4f - d * 10f, look * (1f - d), 0)); // (+x looks down, -x tips the head back)
                    Rot(m_LArm, new Vector3(-30f, 0, -6f));
                    Rot(m_LFore, new Vector3(-95f, 50f, 0));
                    Rot(m_RArm, new Vector3(Mathf.Lerp(-30f, -80f, d), 0, Mathf.Lerp(-2f, 14f, d)));
                    Rot(m_RFore, new Vector3(Mathf.Lerp(-80f, -142f, d), Mathf.Lerp(-30f, -36f, d), 0));
                    break;
                }
                case 5: // a beer: slouched, the can on the knee, a swig now and then with the head back
                {
                    float d = Swig(t);
                    Rot(m_Spine, new Vector3(16f + br - d * 6f, 0, 0));
                    Rot(m_Chest, new Vector3(2f, 0, 0));
                    Rot(m_Head, new Vector3(-4f - d * 26f, look * (1f - d), 0)); // (tipped back for the swig)
                    Rot(m_LArm, new Vector3(-30f, 0, 2f));
                    Rot(m_LFore, new Vector3(-72f, 28f, 0));
                    Rot(m_RArm, new Vector3(Mathf.Lerp(-30f, -78f, d), 0, Mathf.Lerp(-2f, 16f, d)));
                    Rot(m_RFore, new Vector3(Mathf.Lerp(-70f, -136f, d), Mathf.Lerp(-20f, -36f, d), 0));
                    break;
                }
                case 7: // playing the telly's game (ShipLobby / LobbyArcade): leaning in, a controller in both hands, thumbs
                        // mashing, body english on the big moments, eyes on the screen (TurnYaw points the head at it)
                {
                    float mash = Mathf.Sin(t * 17f) * 3f, mash2 = Mathf.Sin(t * 13f + 1f) * 3f;
                    float sway = Mathf.Sin(t * 0.9f) * 5f * Mathf.Max(0f, Mathf.Sin(t * 0.31f));
                    Rot(m_Spine, new Vector3(-16f + br * 0.5f, 0, sway));
                    Rot(m_Chest, new Vector3(-6f, 0, sway * 0.5f));
                    Rot(m_Head, new Vector3(8f + Mathf.Sin(t * 2.1f) * 2f, look, 0));
                    Rot(m_LArm, new Vector3(-36f, 0, -6f));
                    Rot(m_LFore, new Vector3(-80f + mash, 42f, 0));
                    Rot(m_RArm, new Vector3(-36f, 0, 6f));
                    Rot(m_RFore, new Vector3(-80f + mash2, -42f, 0));
                    break;
                }
                default: // on the phone: head down, both hands up in front, thumbs going, a laugh now and then
                {
                    float laugh = Mathf.Max(0f, Mathf.Sin(t * 0.45f + 1.3f) - 0.8f) * 5f;
                    float shake = Mathf.Sin(t * 22f) * 3f * laugh;
                    Rot(m_Spine, new Vector3(-10f + br + shake, 0, 0));
                    Rot(m_Chest, new Vector3(-6f + shake, 0, 0));
                    Rot(m_Head, new Vector3(34f - laugh * 24f, look * 0.12f, 0)); // (looking down at the phone; a laugh lifts it)
                    Rot(m_LArm, new Vector3(-26f, 0, -4f));
                    Rot(m_LFore, new Vector3(-88f + Mathf.Sin(t * 9f) * 3f, 38f, 0));
                    Rot(m_RArm, new Vector3(-26f, 0, 4f));
                    Rot(m_RFore, new Vector3(-88f + Mathf.Sin(t * 11f + 1f) * 3f, -38f, 0));
                    break;
                }
            }
            // the feet on the floor: the lower ankle just over the root (the seat goes under the hips: ShipLobby)
            m_Model.localPosition = m_ModelBase;
            float low = Mathf.Min(LocalY(m_LFoot ?? m_LLo), LocalY(m_RFoot ?? m_RLo));
            if (low < 50f) m_Model.localPosition = m_ModelBase + new Vector3(0f, k_Ankle - low, 0f);
        }

        /// <summary>How many lounge poses there are (Lounge's variant; 7 is playing the telly's game).</summary>
        public const int LoungeVariants = 8;
        /// <summary>Lounge: the variant that's playing the telly's game with a controller.</summary>
        public const int LoungeGaming = 7;

        /// <summary>Lounge: turn the head this way (degrees, character space: + to the right) with this weight (0..1) -
        /// looking at a neighbour or the telly (ShipLobby sets them each frame before Lounge).</summary>
        public float TurnYaw, TurnW;

        /// <summary>
        /// After Lounge: bends the right arm (shoulder and elbow, two-bone IK keeping the elbow on the side it's on) so the
        /// hand's grip point - `grip` past the wrist along the forearm - goes to `palm`, `w` of the way there from where the
        /// pose put it. The lobby's smoker brings the cigarette to their lips this way, and the drinker the can.
        /// </summary>
        public void ReachRight(Vector3 palm, float w, float grip)
        {
            if (w <= 0.001f || m_RArm == null || m_RFore == null || m_RHand == null) return;
            w = Mathf.Clamp01(w);
            var start = m_RHand.position + (m_RHand.position - m_RFore.position).normalized * grip;
            var goal = Vector3.Lerp(start, palm, w);
            for (int pass = 0; pass < 2; pass++) // (the second pass mops up what the first one's guess of the forearm missed)
            {
                var e = m_RFore.position;
                var reach = goal - e;
                if (reach.sqrMagnitude < 1e-6f) return;
                TwoBone(m_RArm, m_RFore, m_RHand, goal - reach.normalized * grip);
            }
        }

        void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target)
        {
            Vector3 s = upper.position, e = lower.position, h = end.position;
            float a = (e - s).magnitude, b = (h - e).magnitude;
            var toT = target - s;
            float d = toT.magnitude;
            if (a < 1e-4f || b < 1e-4f || d < 1e-4f) return;
            var dir = toT / d;
            d = Mathf.Clamp(d, Mathf.Abs(a - b) + 0.002f, a + b - 0.002f);
            // the elbow stays bent the way it is (out and down), just as much as the reach needs
            var pole = Vector3.ProjectOnPlane(e - s, dir);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(-m_Root.up, dir);
            pole.Normalize();
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            var elbow = s + (dir * cosA + pole * sinA) * a;
            upper.rotation = Quaternion.FromToRotation(e - s, elbow - s) * upper.rotation;
            e = lower.position; h = end.position;
            var wrist = s + dir * d;
            lower.rotation = Quaternion.FromToRotation(h - e, wrist - e) * lower.rotation;
        }
        /// <summary>An ankle bone's height over the sole.</summary>
        const float k_Ankle = 0.07f;
        float LocalY(Transform b) => b != null ? m_Root.InverseTransformPoint(b.position).y : 99f;

        /// <summary>The smoker's hand at the mouth (0..1): a drag every 6 s, held there a moment.</summary>
        public static float Drag(float t) => Raise(t, 6f, 2.2f);
        /// <summary>The beer at the mouth (0..1): a swig every 8 s.</summary>
        public static float Swig(float t) => Raise(t, 8f, 2.4f);
        static float Raise(float t, float period, float len)
        {
            float u = Mathf.Repeat(t, period);
            if (u > len) return 0f;
            const float up = 0.55f;
            return u < up ? Smooth(u / up) : u > len - up ? Smooth((len - u) / up) : 1f;
        }

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

            // holding something: right hand forward, still bobbing a little with the stride (the spear is always carried
            // the way it is at a sprint)
            float holdSprint = p.Item == Item.Spear ? 1f : sprint;
            rArm = Vector3.Lerp(rArm, new Vector3(-35f + s * armSwing * -0.25f - 15f * holdSprint, 0, 10f), m_Hold);
            rFore = Vector3.Lerp(rFore, new Vector3(-45f - 20f * holdSprint, 0, 0), m_Hold);
            m_Rock = Mathf.MoveTowards(m_Rock, p.Item == Item.Rock && p.Holding && !p.Carrying ? 1f : 0f, dt * 8f);
            if (m_Rock > 0f)
            {
                // the rock in both hands in front of the belly: both arms forward and in, the elbows bent, the hands
                // either side of it (it sits between them - GripPose), bobbing a little with the stride
                float bobR = s * armSwing * -0.12f;
                rArm = Vector3.Lerp(rArm, new Vector3(-24f + bobR, 0, -2f), m_Rock);
                rFore = Vector3.Lerp(rFore, new Vector3(-88f, 0, 0), m_Rock);
                lArm = Vector3.Lerp(lArm, new Vector3(-24f + bobR, 0, 30f), m_Rock);
                lFore = Vector3.Lerp(lFore, new Vector3(-88f, 0, 0), m_Rock);
            }
            else if (p.TwoHanded && p.Holding)
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
            if (m_Rock > 0f && m_LHand != null)
            {
                // the rock: held between the two palms (a little below and in front of the wrists), facing ahead
                var mid = (m_RHand.position + m_LHand.position) * 0.5f;
                var fwdR = (Vector3.ProjectOnPlane(down, m_Root.right).normalized + m_Root.forward).normalized;
                float w = Smooth(m_Rock);
                pos = Vector3.Lerp(pos, mid + fwdR * 0.07f - m_Root.up * 0.03f, w);
                rot = Quaternion.Slerp(rot, Quaternion.LookRotation(m_Root.forward, m_Root.up), w);
            }
        }
    }
}
