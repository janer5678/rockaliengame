using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// First-person hands. Every item has a hand pose. The rock is held in both hands; melee swings raise,
    /// slam down, then either bounce back up with a short hit-stop when they connect or follow through
    /// further down when they miss (Rust style). Two-handed items put the second hand on the item.
    /// Locomotion is animated like a Mixamo-style FPS rig: figure-8 walk bob, a lowered and tilted sprint pose with the
    /// free hand pumping, idle breathing, jump lag and a landing dip.
    /// </summary>
    public class ViewModel
    {
        /// <summary>Seconds from click to the moment the swing connects (the hit is applied then).</summary>
        public const float ImpactTime = 0.13f;
        const float HitStop = 0.07f;

        public struct State
        {
            public Item Item;
            public bool Ball, Visible, SpearAim, Crouch, HasArrow, Sprint, Grounded, Firing, Loaded, Aim, Visible2;
            public float Draw, RamCharge, Bob, Speed, VelY, Reload;
            public Vector2 Look;
        }

        struct KP
        {
            public Vector3 Pos; public float HandX, ItemX;
            public KP(Vector3 p, float hx, float ix) { Pos = p; HandX = hx; ItemX = ix; }
            public static KP Lerp(KP a, KP b, float t) => new KP(Vector3.LerpUnclamped(a.Pos, b.Pos, t), Mathf.LerpUnclamped(a.HandX, b.HandX, t), Mathf.LerpUnclamped(a.ItemX, b.ItemX, t));
        }

        readonly Transform m_Root, m_R, m_L, m_ItemHolder;
        readonly AlienArm m_AR, m_AL;
        /// <summary>How each alien hand sits on its hand transform this frame (set by the poses; a fist round the grip by default).</summary>
        HandPose m_RP, m_LP;
        GameObject m_Item, m_Arrow, m_Ball, m_StringA, m_StringB;
        Item m_ItemId = (Item)255;
        bool m_WasBall;
        float m_SwingStart = -10f, m_SwingDur = 0.6f, m_ThrowStart = -10f, m_EatStart = -10f, m_UseStart = -10f, m_EquipStart = -10f;
        bool m_Hit, m_ImpactKnown;
        float m_FreezeUntil = -10f, m_FreezeE;
        Vector2 m_Sway;
        float m_CrouchK, m_SprintK, m_Land, m_AirVel, m_Jump, m_BobPhase, m_AimK;
        bool m_WasGrounded = true;

        // ---- for the hands autotest ----
        /// <summary>0: the alien arms (the game), 1: the original box arms instead, 2: both at once (to line them up).</summary>
        public static int DebugArms;
        /// <summary>When set (>= 0), the swing / throw / eat / use animations are held at this many seconds in.</summary>
        public static float DebugSwingE = -1f, DebugThrowE = -1f, DebugEatE = -1f, DebugUseE = -1f;
        public static bool DebugAim, DebugBall;
        int m_ArmsShown = -1;
        readonly System.Collections.Generic.List<Renderer> m_BoxR = new System.Collections.Generic.List<Renderer>(), m_AlienR = new System.Collections.Generic.List<Renderer>();

        public ViewModel(Transform cam, Color team)
        {
            m_Root = new GameObject("ViewModel").transform;
            m_Root.SetParent(cam, false);
            var skin = new Color(0.6f, 0.64f, 0.58f);
            skin = Color.Lerp(skin, team, 0.25f);
            m_R = MakeArm(m_Root, skin, team, true, out m_AR);
            m_L = MakeArm(m_Root, skin, team, false, out m_AL);
            foreach (var r in m_Root.GetComponentsInChildren<Renderer>(true))
            {
                bool alien = false;
                for (var t = r.transform; t != null && t != m_Root; t = t.parent) alien |= t.name.StartsWith("psx alienarm");
                (alien ? m_AlienR : m_BoxR).Add(r);
            }
            m_ItemHolder = new GameObject("item").transform;
            m_ItemHolder.SetParent(m_Root, false);
        }

        public void Destroy()
        {
            m_AR?.Free(); m_AL?.Free();
            if (m_Root) Object.Destroy(m_Root.gameObject);
        }

        public void Swing(float cooldown, float impact = ImpactTime)
        {
            m_SwingStart = Time.time; m_SwingDur = Mathf.Max(0.35f, cooldown); m_ImpactKnown = false; m_Hit = false;
            // a slower weapon (the sword) winds up for longer before it comes down
            m_Down = Mathf.Max(0.14f, impact + 0.01f);
            m_Up = m_Down * 0.57f;
        }
        float m_Up = 0.08f, m_Down = 0.14f;
        /// <summary>Called at the impact frame: a hit bounces the swing back up (with hit-stop), a miss follows through.</summary>
        public void Impact(bool hit)
        {
            m_ImpactKnown = true;
            m_Hit = hit;
            if (!hit) return;
            m_FreezeE = Time.time - m_SwingStart;
            m_FreezeUntil = Time.time + HitStop;
            m_SwingStart += HitStop; // the rest of the swing continues after the freeze
        }
        public void Throw() => m_ThrowStart = Time.time;
        public void Eat() => m_EatStart = Time.time;
        public void Use() => m_UseStart = Time.time;

        static Transform MakeArm(Transform parent, Color skin, Color team, bool right, out AlienArm arm)
        {
            var hand = new GameObject(right ? "R" : "L").transform;
            hand.SetParent(parent, false);
            float side = right ? 1f : -1f;
            var dark = skin * 0.85f; dark.a = 1f;
            Art.Box(hand, skin, Vector3.zero, new Vector3(0.085f, 0.09f, 0.1f));                                  // fist
            Art.Box(hand, dark, new Vector3(0, 0.005f, 0.055f), new Vector3(0.09f, 0.075f, 0.035f));             // knuckles
            Art.Box(hand, skin, new Vector3(-0.045f * side, 0.025f, 0.035f), new Vector3(0.03f, 0.03f, 0.065f), new Vector3(0, 20 * side, 0)); // thumb
            Art.Box(hand, team, new Vector3(0, 0, -0.075f), new Vector3(0.082f, 0.082f, 0.035f));               // wrist band
            Art.Box(hand, skin, new Vector3(0, 0, -0.32f), new Vector3(0.072f, 0.072f, 0.46f));                  // forearm
            // the alien's own forearm and clawed hand in place of the blocks above (which stay as a fallback)
            var alien = AlienArm.Make(hand, right, HandColorHook.Tint(hand, team)); // (team tint, or the colour in Settings > Display)
            if (alien != null)
                foreach (var r in hand.GetComponentsInChildren<Renderer>()) if (!r.transform.IsChildOf(alien.Fit)) r.enabled = false;
            arm = alien;
            foreach (var r in hand.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return hand;
        }

        /// <summary>
        /// How an alien hand sits on its hand transform. The hand transform is where the old box fist was: its origin is
        /// the grip (items are held by it), +Z runs from the wrist to the knuckles, and the thumb was on its +Y side.
        /// Fist: the claws close round the origin, the thumb towards `Dir` (a direction in view model space; by default
        /// the hand's own +Y, like the box thumb). Palm: a more open hand whose palm faces the point `Dir` (view model space).
        /// </summary>
        struct HandPose
        {
            public bool Palm, HasDir;
            public Vector3 Dir;
            public float Curl;
            public static HandPose Fist => new HandPose { Curl = 1f };
            public static HandPose FistThumb(Vector3 dir, float curl = 1f) => new HandPose { HasDir = true, Dir = dir, Curl = curl };
            public static HandPose PalmAt(Vector3 point, float curl) => new HandPose { Palm = true, HasDir = true, Dir = point, Curl = curl };
        }

        /// <summary>
        /// The alien's own forearm and clawed hand (cut from the rigged player model by Tools/psx_convert.py), tinted in
        /// the team colour like the body. The mesh: hand along +Z, wrist at z = 0, knuckles at z = 0.09, claw tips up to
        /// z = 0.226, elbow at z = -0.22; the right palm faces (0.96, 0.29) in XY and its thumb (0.29, -0.96) (the left
        /// arm is its mirror image). Rather than being squeezed into the old box (which put the hand where the forearm
        /// was), it's fitted by its hand: the claws curl round the grip (the hand's origin, where the box fist was), the
        /// wrist ends up where the box wrist band was, and the forearm is stretched back so it runs off the edge of the
        /// screen like a real arm.
        /// </summary>
        class AlienArm
        {
            public Transform Fit;
            Mesh m_Mesh;
            Vector3[] m_Base, m_Work;
            Matrix4x4 m_ToModel, m_ToMesh;
            float m_Curl = -1f;
            Vector2 m_Thumb, m_PalmN;

            public const float Scale = 1.4f;
            const float Knuckle = 0.09f;    // where the claws start (model z)
            const float ClawLen = 0.136f;   // the longest claw, knuckle to tip
            const float FistTurn = 245f;    // how far (degrees) the longest claw curls round in a full fist
            const float ArmStretch = 0.3f;  // the elbow end is moved back this much (model units) so the arm reaches off screen
            const float ElbowWiden = 1.15f;

            public static AlienArm Make(Transform hand, bool right, Color tint)
            {
                var fit = new GameObject("alien fit").transform;
                fit.SetParent(hand, false);
                var model = PsxModels.Spawn(right ? "alienarm_r" : "alienarm_l", fit);
                var mf = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) { Object.Destroy(fit.gameObject); return null; }
                var a = new AlienArm { Fit = fit };
                a.m_Mesh = Object.Instantiate(mf.sharedMesh);
                a.m_Mesh.name = "alien arm (posed)";
                mf.sharedMesh = a.m_Mesh;
                a.m_Base = a.m_Mesh.isReadable ? a.m_Mesh.vertices : new Vector3[0]; // (Read/Write is on for it: PsxImport)
                a.m_Work = new Vector3[a.m_Base.Length];
                a.m_ToModel = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                a.m_ToMesh = a.m_ToModel.inverse;
                a.m_PalmN = new Vector2(right ? 0.957f : -0.957f, 0.29f);
                a.m_Thumb = new Vector2(right ? 0.29f : -0.29f, -0.957f);
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.materials; // own copies, tinted
                    foreach (var mt in mats) { mt.SetColor("_BaseColor", tint); mt.color = tint; }
                    r.materials = mats;
                }
                a.Bend(1f);
                return a;
            }

            /// <summary>Re-shapes the mesh: the claws curled by `curl` (0 open, 1 a fist) towards the palm, the forearm stretched.</summary>
            void Bend(float curl)
            {
                if (Mathf.Abs(curl - m_Curl) < 0.01f || m_Base.Length == 0) return;
                m_Curl = curl;
                var n = new Vector3(m_PalmN.x, m_PalmN.y, 0f).normalized;
                var z = Vector3.forward;
                var ax = Vector3.Cross(z, n);
                float k = curl * FistTurn * Mathf.Deg2Rad / ClawLen;
                for (int i = 0; i < m_Base.Length; i++)
                {
                    var u = m_ToModel.MultiplyPoint3x4(m_Base[i]);
                    if (u.z < -0.1f)
                    {
                        // the elbow end: further back, a little thicker
                        u.x *= ElbowWiden; u.y *= ElbowWiden;
                        u.z -= ArmStretch;
                    }
                    float d = u.z - Knuckle;
                    if (d > 0f && k > 1e-4f)
                    {
                        // bend the claws round an arc on the palm side (each point keeps its offset from the claw's centre line)
                        float pn = Vector3.Dot(u, n), pa = Vector3.Dot(u, ax);
                        float th = k * d;
                        var c = new Vector3(0, 0, Knuckle) + z * (Mathf.Sin(th) / k) + n * ((1f - Mathf.Cos(th)) / k);
                        var n2 = -z * Mathf.Sin(th) + n * Mathf.Cos(th);
                        u = c + n2 * pn + ax * pa;
                    }
                    m_Work[i] = m_ToMesh.MultiplyPoint3x4(u);
                }
                m_Mesh.vertices = m_Work;
                m_Mesh.RecalculateNormals();
                m_Mesh.RecalculateBounds();
            }

            public void Free() { if (m_Mesh) Object.Destroy(m_Mesh); }

            /// <summary>Puts the arm on its hand transform for this frame.</summary>
            public void Pose(HandPose p, Transform hand, Transform root)
            {
                float curl = Mathf.Clamp01(p.Curl);
                Bend(curl);
                // turn the arm round its length so the thumb / the palm points the right way
                Vector2 want = Vector2.up; // the thumb where the box fist had it
                if (p.HasDir)
                {
                    var d = p.Palm ? hand.InverseTransformPoint(root.TransformPoint(p.Dir)) : hand.InverseTransformDirection(root.TransformDirection(p.Dir));
                    if (new Vector2(d.x, d.y).sqrMagnitude > 1e-6f) want = new Vector2(d.x, d.y);
                }
                var have = p.Palm ? m_PalmN : m_Thumb;
                float roll = (Mathf.Atan2(want.y, want.x) - Mathf.Atan2(have.y, have.x)) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0, 0, roll);
                // a fist closes round the grip: the centre of the claws' curl goes on the hand's origin; an open hand
                // puts its knuckles there
                float r = ClawLen / (FistTurn * Mathf.Deg2Rad);
                var pivot = new Vector3(0, 0, Knuckle) + (Vector3)m_PalmN.normalized * (r * Mathf.Clamp01(curl * 1.5f - 0.5f));
                Fit.localRotation = rot;
                Fit.localScale = Vector3.one * Scale;
                Fit.localPosition = -(rot * (pivot * Scale));
            }
        }


        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        public void Update(State s)
        {
            float dt = Time.deltaTime;
            bool vis = s.Visible && s.Visible2; // hidden while looking through the sniper scope
            if (m_Root.gameObject.activeSelf != vis) m_Root.gameObject.SetActive(vis);
            if (!vis) return;
            if (DebugAim) s.Aim = true;
            if (DebugBall) s.Ball = true;
            if (DebugSwingE >= 0f) { m_SwingStart = Time.time - DebugSwingE; m_FreezeUntil = -10f; m_ImpactKnown = false; }
            if (DebugThrowE >= 0f) m_ThrowStart = Time.time - DebugThrowE;
            if (DebugEatE >= 0f) m_EatStart = Time.time - DebugEatE;
            if (DebugUseE >= 0f) m_UseStart = Time.time - DebugUseE;
            if (m_ArmsShown != DebugArms && m_AlienR.Count > 0)
            {
                m_ArmsShown = DebugArms;
                foreach (var r in m_BoxR) if (r) r.enabled = DebugArms != 0;
                foreach (var r in m_AlienR) if (r) r.enabled = DebugArms != 1;
            }

            // ---- models ----
            var want = s.Ball ? (Item)254 : s.Item;
            if (want != m_ItemId)
            {
                if (m_Item) Object.Destroy(m_Item);
                if (m_Ball) Object.Destroy(m_Ball);
                m_Item = null; m_Ball = null;
                if (s.Ball) m_Ball = ItemModels.CreateBall(m_Root, 1.3f);
                else if (s.Item != Item.None) m_Item = ItemModels.Create(s.Item, m_ItemHolder);
                foreach (var r in m_Root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // after throwing the ball the hands come back up a moment later instead of instantly
                m_EquipStart = Time.time + (m_WasBall && !s.Ball ? 0.3f : 0f);
                m_ItemId = want;
                m_WasBall = s.Ball;
            }
            bool wantString = !s.Ball && s.Item == Item.Bow;
            if (wantString != (m_StringA != null))
            {
                if (m_StringA) { Object.Destroy(m_StringA); Object.Destroy(m_StringB); m_StringA = m_StringB = null; }
                else
                {
                    // our own bowstring (two segments) so it can be pulled back to the nock
                    var sc = new Color(0.9f, 0.9f, 0.85f);
                    m_StringA = Art.Box(m_Root, sc, Vector3.zero, Vector3.one);
                    m_StringB = Art.Box(m_Root, sc, Vector3.zero, Vector3.one);
                    m_StringA.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    m_StringB.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (m_Item && m_Item.transform.childCount > 3) m_Item.transform.GetChild(3).gameObject.SetActive(false);
                }
            }
            bool wantArrow = !s.Ball && s.Item == Item.Bow && s.HasArrow;
            if (wantArrow != (m_Arrow != null))
            {
                if (m_Arrow) Object.Destroy(m_Arrow);
                else
                {
                    m_Arrow = ItemModels.Create(Item.Arrow, m_Root);
                    foreach (var r in m_Arrow.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // ---- shared motion ----
            m_Sway = Vector2.Lerp(m_Sway, -s.Look * 0.0035f, dt * 10f);
            m_Sway = Vector2.ClampMagnitude(m_Sway, 0.05f);
            m_CrouchK = Mathf.MoveTowards(m_CrouchK, s.Crouch ? 1f : 0f, dt * 5f);

            float swingE = Time.time < m_FreezeUntil ? m_FreezeE : Time.time - m_SwingStart;
            bool swinging = swingE >= 0 && swingE < Mathf.Max(0.45f, m_SwingDur * 0.9f);
            bool busy = swinging || s.Draw > 0f || s.RamCharge > 0f || s.Firing || s.SpearAim || s.Aim || s.Reload >= 0f;

            // sprint: lower the item and tilt it in (not while attacking)
            m_SprintK = Mathf.MoveTowards(m_SprintK, s.Sprint && !busy ? 1f : 0f, dt * 6f);
            // jump lag / landing dip
            if (s.Grounded && !m_WasGrounded && m_AirVel < -4f) m_Land = Mathf.Clamp01(-m_AirVel / 16f);
            if (!s.Grounded) m_AirVel = s.VelY;
            m_WasGrounded = s.Grounded;
            m_Land = Mathf.MoveTowards(m_Land, 0f, dt * 4f);
            m_Jump = Mathf.Lerp(m_Jump, s.Grounded ? 0f : Mathf.Clamp(-s.VelY * 0.004f, -0.035f, 0.045f), dt * 8f);

            // figure-8 walk bob, bigger and with a roll when sprinting; slow breathing when standing still
            float bobAmt = Mathf.Clamp01(s.Speed / 5f) * (s.Grounded ? 1f : 0.2f);
            m_BobPhase = s.Bob * 1.6f;
            float bx = Mathf.Sin(m_BobPhase), by = Mathf.Abs(Mathf.Cos(m_BobPhase));
            Vector3 bob = new Vector3(bx * 0.014f * (1f + m_SprintK), by * 0.018f * (1f + m_SprintK * 1.3f), 0) * bobAmt;
            float breathe = Mathf.Sin(Time.time * 1.7f) * 0.004f * (1f - bobAmt);
            float equip = Smooth((Time.time - m_EquipStart) / 0.25f);
            float landDip = Mathf.Sin(m_Land * Mathf.PI) * 0.06f;
            Vector3 shared = bob + new Vector3(m_Sway.x + m_SprintK * 0.03f, m_Sway.y - m_CrouchK * 0.02f + breathe + m_Jump - landDip - m_SprintK * 0.07f, -m_SprintK * 0.04f)
                + Vector3.down * (1f - equip) * 0.45f;
            Quaternion sharedRot = Quaternion.Euler(m_Sway.y * 300f + m_SprintK * 14f + landDip * 60f, -m_Sway.x * 300f - m_SprintK * 22f, bx * bobAmt * (1.5f + 3f * m_SprintK) + m_SprintK * 12f);

            m_RP = m_LP = HandPose.Fist;
            if (s.Ball) { PoseBall(shared, sharedRot); ApplyHands(); return; }
            if (m_Item && !m_Item.activeSelf && s.Item != Item.Spear) m_Item.SetActive(true);

            switch (s.Item)
            {
                case Item.Rock: PoseRock(shared, sharedRot, swinging, swingE); break;
                case Item.Hatchet:
                case Item.Sword:
                case Item.TreeCracker:
                case Item.Pickaxe: PoseTool(shared, sharedRot, swinging, swingE); break;
                case Item.Spear: PoseSpear(s, shared, sharedRot, swinging, swingE); break;
                case Item.Bow: PoseBow(s, shared, sharedRot); break;
                case Item.Ram: PoseRam(s, shared, sharedRot); break;
                case Item.Chainsaw: PoseChainsaw(s, shared, sharedRot); break;
                case Item.Crossbow:
                case Item.Sniper:
                case Item.Pistol:
                case Item.Revolver:
                case Item.Shotgun:
                case Item.PortalGun:
                case Item.RocketLauncher: PoseCrossbow(s, shared, sharedRot); break;
                case Item.None: HideLeft(); Set(m_R, new Vector3(0.3f, -0.9f, 0.2f), Quaternion.identity); break;
                default: PoseHeld(s.Item, shared, sharedRot); break;
            }
            ApplyHands();
        }

        void ApplyHands()
        {
            m_AR?.Pose(m_RP, m_R, m_Root);
            m_AL?.Pose(m_LP, m_L, m_Root);
        }

        void Set(Transform t, Vector3 pos, Quaternion rot) { t.localPosition = pos; t.localRotation = rot; }
        /// <summary>The free (left) hand: out of view, except when sprinting, where it pumps in the lower left like a run cycle.</summary>
        void HideLeft()
        {
            var hidden = new Vector3(-0.3f, -0.9f, 0.2f);
            if (m_SprintK <= 0.01f) { Set(m_L, hidden, Quaternion.identity); return; }
            float pump = Mathf.Sin(m_BobPhase + Mathf.PI * 0.5f);
            var run = new Vector3(-0.28f, -0.42f + pump * 0.07f, 0.38f + pump * 0.08f);
            Set(m_L, Vector3.Lerp(hidden, run, m_SprintK), Quaternion.Euler(-30f + pump * 25f, 20f, 60f));
        }

        void AttachItemToRight(Vector3 localPos, Vector3 localEuler, float scale = 1f)
        {
            m_ItemHolder.SetParent(m_R, false);
            Set(m_ItemHolder, localPos, Quaternion.Euler(localEuler));
            m_ItemHolder.localScale = Vector3.one * scale;
        }

        void AttachItemToRoot(Vector3 pos, Quaternion rot, float scale = 1f)
        {
            m_ItemHolder.SetParent(m_Root, false);
            Set(m_ItemHolder, pos, rot);
            m_ItemHolder.localScale = Vector3.one * scale;
        }

        /// <summary>Raise -> slam -> (hit: bounce back up | miss: follow through down) -> recover.</summary>
        KP Chop(KP idle, KP raised, KP slam, KP recoil, KP follow, bool swinging, float e)
        {
            if (!swinging) return idle;
            float up = m_Up, down = m_Down;
            float end = Mathf.Max(0.45f, m_SwingDur * 0.9f);
            if (e < up) return KP.Lerp(idle, raised, Smooth(e / up));
            if (e < down) return KP.Lerp(raised, slam, Smooth((e - up) / (down - up)));
            if (m_ImpactKnown && m_Hit)
            {
                if (e < down + 0.1f) return KP.Lerp(slam, recoil, Smooth((e - down) / 0.1f));
                return KP.Lerp(recoil, idle, Smooth((e - down - 0.1f) / Mathf.Max(0.1f, end - down - 0.1f)));
            }
            if (e < down + 0.08f) return KP.Lerp(slam, follow, Smooth((e - down) / 0.08f));
            if (e < down + 0.16f) return follow;
            return KP.Lerp(follow, idle, Smooth((e - down - 0.16f) / Mathf.Max(0.1f, end - down - 0.16f)));
        }

        /// <summary>A big rock held in both hands in front of you, brought down two-handed.</summary>
        void PoseRock(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            var idle = new KP(new Vector3(0.08f, -0.26f, 0.55f), -5f, 0);
            var raised = new KP(new Vector3(0.06f, 0.02f, 0.4f), -45f, 0);
            var slam = new KP(new Vector3(0.03f, -0.3f, 0.68f), 35f, 0);
            var recoil = new KP(new Vector3(0.05f, -0.1f, 0.55f), -15f, 0);
            var follow = new KP(new Vector3(0.02f, -0.5f, 0.6f), 60f, 0);
            var k = Chop(idle, raised, slam, recoil, follow, swinging, e);
            var rot = sharedRot * Quaternion.Euler(k.HandX, -6f, 0);
            AttachItemToRoot(shared + k.Pos, rot, 1f);
            // hands on either side of the rock, wrists turned in
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.13f, 0.0f, 0.0f)));
            m_R.localRotation = rot * Quaternion.Euler(-20f, -35f, -75f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.13f, 0.0f, 0.0f)));
            m_L.localRotation = rot * Quaternion.Euler(-20f, 35f, 75f);
            m_RP = m_LP = HandPose.PalmAt(m_ItemHolder.localPosition, 0.35f); // palms flat on the rock
        }

        /// <summary>Hatchet / pickaxe: one hand; the tool pivots in the fist so the forearm stays out of frame.</summary>
        void PoseTool(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            var idle = new KP(new Vector3(0.27f, -0.3f, 0.5f), -8f, 22f);
            var raised = new KP(new Vector3(0.3f, -0.1f, 0.38f), -30f, -18f);
            var slam = new KP(new Vector3(0.14f, -0.38f, 0.62f), 6f, 82f);
            var recoil = new KP(new Vector3(0.2f, -0.24f, 0.55f), -12f, 25f);
            var follow = new KP(new Vector3(0.12f, -0.48f, 0.58f), 18f, 100f);
            var k = Chop(idle, raised, slam, recoil, follow, swinging, e);
            float swingK = swinging ? 1f : 0f;
            Set(m_R, shared + k.Pos, sharedRot * Quaternion.Euler(k.HandX, -18f + swingK * 6f, -8f));
            AttachItemToRight(new Vector3(0, -0.06f, 0.01f), new Vector3(k.ItemX, 0, 0));
            HideLeft();
        }

        void PoseSpear(State s, Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            // the spear lives on the rig; both hands grab the shaft with natural wrist angles
            float throwK = Mathf.Clamp01((Time.time - m_ThrowStart) / 0.25f);
            bool throwing = throwK < 1f;
            if (s.SpearAim || throwing)
            {
                float d = throwing ? 0f : s.Draw;
                Vector3 pos = new Vector3(0.4f, 0.17f, 0.15f - d * 0.25f);
                if (throwing) pos = Vector3.Lerp(pos, new Vector3(0.15f, -0.02f, 0.8f), Smooth(throwK));
                var rot = sharedRot * Quaternion.Euler(86f, -6f, 0);
                AttachItemToRoot(shared + pos, rot);
                if (m_Item) m_Item.SetActive(!throwing || throwK < 0.4f);
                m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.02f, 0.15f, -0.03f)));
                m_R.localRotation = sharedRot * Quaternion.Euler(-55f, -30f, -20f);
                Set(m_L, shared + new Vector3(-0.16f, -0.14f, 0.55f), sharedRot * Quaternion.Euler(-25f, 25f, 25f));
                m_RP = HandPose.FistThumb(rot * Vector3.up);
                m_LP = HandPose.PalmAt(m_L.localPosition + sharedRot * new Vector3(0.1f, 0.05f, 0.1f), 0.45f); // the free hand points the way
                return;
            }
            if (m_Item && !m_Item.activeSelf) m_Item.SetActive(true);
            float thrust = 0;
            if (swinging)
            {
                if (e < ImpactTime) thrust = Smooth(e / ImpactTime);
                else if (m_ImpactKnown && m_Hit) thrust = 1f - Smooth((e - ImpactTime) / 0.1f) * 1.3f + Smooth((e - ImpactTime - 0.1f) / 0.3f) * 0.3f;
                else thrust = 1f + Smooth((e - ImpactTime) / 0.08f) * 0.15f - Smooth((e - ImpactTime - 0.1f) / Mathf.Max(0.15f, m_SwingDur * 0.6f)) * 1.15f;
            }
            var spearRot = sharedRot * Quaternion.Euler(80f, -5f, 0);
            AttachItemToRoot(shared + new Vector3(0.22f, -0.25f, 0.08f + thrust * 0.4f), spearRot);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.03f, 0.12f, 0)));
            m_R.localRotation = sharedRot * Quaternion.Euler(-25f, -28f, -15f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.03f, 0.62f, 0)));
            m_L.localRotation = sharedRot * Quaternion.Euler(-30f, 22f, 15f);
            m_RP = m_LP = HandPose.FistThumb(spearRot * Vector3.up);
        }

        /// <summary>
        /// Rust hunting bow (copied from Rust footage): the LEFT arm comes in from the lower left and holds the bow right of
        /// centre, tilted clockwise, with the arrow nocked and the right hand on the string at the lower right. Drawing
        /// brings the grip in to just below/right of the crosshair, the string hand back to your face, and the arrow
        /// lines up to point straight at the crosshair.
        /// </summary>
        void PoseBow(State s, Vector3 shared, Quaternion sharedRot)
        {
            float e = Smooth(s.Draw);
            if (m_Item && m_Item.transform.childCount > 3) m_Item.transform.GetChild(3).gameObject.SetActive(false); // we draw the string ourselves
            var grip = shared + Vector3.Lerp(new Vector3(0.2f, -0.22f, 0.62f), new Vector3(0.045f, -0.12f, 0.6f), e);
            var bowRot = sharedRot * Quaternion.Euler(Mathf.Lerp(-6f, 0f, e), Mathf.Lerp(-12f, -4f, e), Mathf.Lerp(-14f, -26f, e));
            AttachItemToRoot(grip, bowRot, 1f);

            // bow hand: fist on the grip, forearm reaching back to the lower left
            var shoulder = new Vector3(-0.3f, -0.55f, -0.1f);
            var armDir = (grip - shoulder).normalized;
            m_L.localPosition = grip + armDir * -0.02f + bowRot * new Vector3(-0.01f, -0.01f, 0f);
            m_L.localRotation = Quaternion.LookRotation(armDir, bowRot * Vector3.up) * Quaternion.Euler(0, 0, 70f);
            m_LP = HandPose.FistThumb(bowRot * Vector3.up);

            // arrow: rests on the grip. Idle it points ahead and a bit left; drawn it runs straight along the view, so it
            // lines up with the crosshair and the shaft reaches from the grip down towards the bottom of the screen
            var tipIdle = grip + bowRot * new Vector3(-0.02f, 0.01f, 0.28f);
            var dirIdle = (bowRot * new Vector3(-0.08f, 0.06f, 1f)).normalized;
            var tipDrawn = grip + new Vector3(-0.02f, 0.065f, 0.07f); // rests on top of the fist; the fletching ends up by your face, out of view
            var dir = Vector3.Slerp(dirIdle, Vector3.forward, e).normalized;
            var tip = Vector3.Lerp(tipIdle, tipDrawn, e);
            const float arrowLen = 0.72f;
            var nock = tip - dir * arrowLen;
            if (m_Arrow)
            {
                // the arrow model runs along +Y from -0.15 (fletching) to 0.6 (tip)
                m_Arrow.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0, 0);
                m_Arrow.transform.localPosition = tip - dir * 0.6f;
            }

            // string hand holds the nock at the lower right; drawn, it's back by your face (out of view)
            var stringRest = grip + bowRot * new Vector3(0, 0, -0.03f);
            var pull = Vector3.Lerp(m_Arrow ? nock : stringRest, grip + new Vector3(0.015f, -0.015f, -0.47f), e);
            pull.z = Mathf.Max(pull.z, 0.12f); // keep the string in front of the camera
            m_R.localPosition = Vector3.Lerp(pull + new Vector3(0.015f, -0.03f, -0.03f), new Vector3(0.3f, -0.5f, -0.3f), e);
            m_R.localRotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-10f, 0, -80f);
            m_RP = HandPose.FistThumb(Vector3.up, 0.6f); // pinching the nock

            // the string runs from both limb tips to the nock
            if (m_StringA && m_Item)
            {
                var top = m_Root.InverseTransformPoint(m_Item.transform.TransformPoint(new Vector3(0, 0.375f, -0.02f)));
                var bottom = m_Root.InverseTransformPoint(m_Item.transform.TransformPoint(new Vector3(0, -0.375f, -0.02f)));
                var mid = Vector3.Lerp(stringRest, pull, Mathf.Max(e, 0.05f));
                mid.z = Mathf.Max(mid.z, 0.12f);
                SetString(m_StringA.transform, top, mid);
                SetString(m_StringB.transform, bottom, mid);
            }
        }

        static void SetString(Transform t, Vector3 a, Vector3 b)
        {
            var d = b - a;
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d) : Quaternion.identity;
            t.localScale = new Vector3(0.004f, 0.004f, d.magnitude);
        }

        /// <summary>Rust crossbow: held like a rifle at the hip, RMB brings it up to the eye, and after a shot it's cranked back for the next bolt.</summary>
        void PoseCrossbow(State s, Vector3 shared, Quaternion sharedRot)
        {
            float aim = m_AimK = Mathf.MoveTowards(m_AimK, s.Aim ? 1f : 0f, Time.deltaTime * 7f);
            float kick = Mathf.Clamp01(1f - (Time.time - m_UseStart) / 0.25f);
            var pos = shared + Vector3.Lerp(new Vector3(0.2f, -0.2f, 0.42f), new Vector3(0f, -0.075f, 0.3f), Smooth(aim)) + new Vector3(0, 0.02f, -0.07f) * kick;
            var rot = sharedRot * Quaternion.Euler(Mathf.Lerp(0f, 0f, aim) - kick * 8f, Mathf.Lerp(-6f, 0f, aim), 0);
            float r = s.Reload >= 0f ? Mathf.Sin(Mathf.Clamp01(s.Reload) * Mathf.PI) : 0f;
            if (r > 0f)
            {
                // tip it down and pull the string back with the left hand
                pos += new Vector3(-0.05f, -0.08f, 0) * r;
                rot *= Quaternion.Euler(35f * r, 10f * r, -15f * r);
            }
            AttachItemToRoot(pos, rot, s.Item == Item.Sniper ? 0.75f : 1f);
            if (m_Item && s.Item == Item.Crossbow)
            {
                var bolt = m_Item.transform.Find("bolt");
                if (bolt) bolt.gameObject.SetActive(s.Loaded);
            }
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.02f, -0.1f, -0.06f)));
            m_R.localRotation = rot * Quaternion.Euler(-10f, -10f, -80f);
            var leftHold = new Vector3(-0.02f, -0.05f, 0.2f);
            var leftPull = new Vector3(-0.04f, 0.04f, -0.05f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(Vector3.Lerp(leftHold, leftPull, r)));
            m_L.localRotation = rot * Quaternion.Euler(-20f - 40f * r, 20f, 70f);
            // the trigger hand round the grip, thumb up; the other hand cradles the stock from below
            m_RP = HandPose.FistThumb(rot * Vector3.up);
            // (a pistol's support hand is a fist off to the side, as the box hand was; long guns are cradled)
            bool shortGun = s.Item == Item.Pistol || s.Item == Item.Revolver || s.Item == Item.PortalGun;
            if (!shortGun) m_LP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(Vector3.Lerp(leftHold, leftPull, r) + new Vector3(0.06f, -0.01f, 0f))), 0.35f); // palm against the side of the stock
        }

        void PoseRam(State s, Vector3 shared, Quaternion sharedRot)
        {
            float strikeK = Mathf.Clamp01((Time.time - m_UseStart) / 0.45f);
            float thrust = strikeK < 1f ? (strikeK < 0.3f ? Smooth(strikeK / 0.3f) : 1f - Smooth((strikeK - 0.3f) / 0.7f)) : 0f;
            var pos = shared + new Vector3(0.18f, -0.4f, 0.55f - s.RamCharge * 0.3f + thrust * 0.7f);
            var rot = sharedRot * Quaternion.Euler(-4f + s.RamCharge * 6f, -8f, 0);
            AttachItemToRoot(pos, rot, 0.75f);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.13f, -0.08f, -0.28f)));
            m_R.localRotation = rot * Quaternion.Euler(-10f, -10f, -60f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.13f, -0.08f, 0.22f)));
            m_L.localRotation = rot * Quaternion.Euler(-10f, 10f, 60f);
        }

        /// <summary>Chainsaw in both hands like the ram; it shakes while the trigger is held.</summary>
        void PoseChainsaw(State s, Vector3 shared, Quaternion sharedRot)
        {
            var buzz = s.Firing ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 0.006f : Vector3.zero;
            var pos = shared + new Vector3(0.22f, -0.36f, 0.5f + (s.Firing ? 0.06f : 0f)) + buzz;
            var rot = sharedRot * Quaternion.Euler(s.Firing ? 4f : -2f, -10f, 0);
            AttachItemToRoot(pos, rot, 1f);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0, 0.1f, -0.12f)));
            m_R.localRotation = rot * Quaternion.Euler(-20f, -10f, -70f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.02f, 0.14f, 0.06f)));
            m_L.localRotation = rot * Quaternion.Euler(-60f, 20f, 70f);
        }

        /// <summary>The ball, huge, in both hands: fills the bottom half of the screen, its top reaching the middle.</summary>
        void PoseBall(Vector3 shared, Quaternion sharedRot)
        {
            float throwK = Mathf.Clamp01((Time.time - m_ThrowStart) / 0.3f);
            bool throwing = throwK < 1f;
            var ballPos = shared + new Vector3(0, -0.62f, 0.75f);
            if (throwing) ballPos += new Vector3(0, 0.25f, 0.9f) * Smooth(throwK);
            if (m_Ball)
            {
                m_Ball.transform.localPosition = ballPos;
                m_Ball.transform.localRotation = sharedRot;
                m_Ball.SetActive(!throwing || throwK < 0.5f);
            }
            float push = throwing ? Smooth(Mathf.Min(1f, throwK * 2f)) * 0.3f : 0f;
            Set(m_R, ballPos + new Vector3(0.62f, 0.05f, -0.15f + push), sharedRot * Quaternion.Euler(-10f, -40f, -80f));
            Set(m_L, ballPos + new Vector3(-0.62f, 0.05f, -0.15f + push), sharedRot * Quaternion.Euler(-10f, 40f, 80f));
            m_RP = m_LP = HandPose.PalmAt(ballPos, 0.3f);
        }

        void PoseHeld(Item item, Vector3 shared, Quaternion sharedRot)
        {
            float eatK = Mathf.Clamp01((Time.time - m_EatStart) / 0.6f);
            float toMouth = eatK < 1f ? Mathf.Sin(eatK * Mathf.PI) : 0f;
            float useK = Mathf.Clamp01((Time.time - m_UseStart) / 0.3f);
            float use = useK < 1f ? Mathf.Sin(useK * Mathf.PI) : 0f;
            Vector3 idle = new Vector3(0.26f, -0.27f, 0.45f);
            Vector3 pos = Vector3.Lerp(idle, new Vector3(0.04f, -0.13f, 0.22f), toMouth) + new Vector3(0, -0.05f, 0.12f) * use;
            float throwK = Mathf.Clamp01((Time.time - m_ThrowStart) / 0.35f);
            if (throwK < 1f) pos += new Vector3(-0.05f, 0.12f, 0.3f) * Mathf.Sin(throwK * Mathf.PI); // lob (C4)
            Set(m_R, shared + pos, sharedRot * Quaternion.Euler(-12f - toMouth * 20f - use * 25f, -14f, -4f));
            if (m_Item) m_Item.SetActive(throwK >= 1f || throwK < 0.45f);
            switch (item)
            {
                case Item.C4: AttachItemToRight(new Vector3(0, 0.05f, 0.07f), new Vector3(-20, 10, 0), 0.65f); break;
                case Item.DeathWand: AttachItemToRight(new Vector3(0, 0.0f, 0.03f), new Vector3(35, 0, 0), 1f); break;
                case Item.Helmet: AttachItemToRight(new Vector3(0, 0.1f, 0.1f), new Vector3(0, 180, 0), 0.8f); break;
                case Item.InvisPotion: AttachItemToRight(new Vector3(0, 0.03f, 0.05f), new Vector3(toMouth * -70f, 0, 0), 1.1f); break;
                case Item.Armor: AttachItemToRight(new Vector3(0, 0.02f, 0.08f), new Vector3(0, 160, 0), 0.8f); break;
                case Item.FortTower: AttachItemToRight(new Vector3(0, 0.02f, 0.06f), new Vector3(0, 20, 0), 1f); break;
                case Item.Car: AttachItemToRight(new Vector3(0, 0.02f, 0.08f), new Vector3(0, 20, 0), 0.9f); break;
                case Item.Saddle: AttachItemToRight(new Vector3(0, 0.02f, 0.06f), new Vector3(0, 20, 0), 0.9f); break;
                case Item.BuildingPlan: AttachItemToRight(new Vector3(0, 0.03f, 0.05f), Vector3.zero, 0.55f); break;
                case Item.Chest: AttachItemToRight(new Vector3(0, 0.06f, 0.08f), new Vector3(0, 20, 0), 0.9f); break;
                case Item.Workbench: AttachItemToRight(new Vector3(0, 0.04f, 0.08f), new Vector3(0, 200, 0), 0.8f); break;
                case Item.Barrier: AttachItemToRight(new Vector3(0, 0.04f, 0.06f), new Vector3(0, 20, 0), 0.8f); break;
                case Item.Arrow: AttachItemToRight(new Vector3(0, -0.1f, 0.02f), new Vector3(-30, 0, 0), 0.8f); break;
                default: AttachItemToRight(new Vector3(0, 0.05f, 0.07f), Vector3.zero, 1f); break;
            }
            // things held by a handle get a fist; things that sit on the hand (food, C4, a chest...) are cradled, the claws
            // curling up round them
            if (item == Item.DeathWand || item == Item.Arrow) m_RP = HandPose.FistThumb(m_Root.InverseTransformDirection(m_ItemHolder.up)); // round the shaft
            else if (item != Item.InvisPotion && item != Item.BuildingPlan)
                m_RP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.position), 0.6f);
            HideLeft();
        }
    }
}
