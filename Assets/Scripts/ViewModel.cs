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
    public partial class ViewModel
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
            public Vector3 Pos; public float HandX, ItemX, Yaw, Twist;
            public KP(Vector3 p, float hx, float ix, float yaw = 0f, float twist = 0f) { Pos = p; HandX = hx; ItemX = ix; Yaw = yaw; Twist = twist; }
            public static KP Lerp(KP a, KP b, float t) => new KP(Vector3.LerpUnclamped(a.Pos, b.Pos, t), Mathf.LerpUnclamped(a.HandX, b.HandX, t), Mathf.LerpUnclamped(a.ItemX, b.ItemX, t),
                Mathf.LerpUnclamped(a.Yaw, b.Yaw, t), Mathf.LerpUnclamped(a.Twist, b.Twist, t));
        }

        readonly Transform m_Root, m_R, m_L, m_ItemHolder;
        /// <summary>The block hands the game shows (m_AR, m_AL), and the alien clawed hands they were before (m_XR,
        /// m_XL: hidden, only for the hands autotest's comparison shots).</summary>
        readonly AlienArm m_AR, m_AL, m_XR, m_XL;
        /// <summary>How each alien hand sits on its hand transform this frame (set by the poses; a fist round the grip by default).</summary>
        HandPose m_RP, m_LP;
        GameObject m_Item, m_Arrow, m_Ball, m_StringA, m_StringB;
        /// <summary>What the claws can touch (the item's pieces, the ball, the arrow and string), gathered when they change.</summary>
        readonly System.Collections.Generic.List<HeldPart> m_Parts = new System.Collections.Generic.List<HeldPart>();
        bool m_PartsDirty = true;
        Item m_ItemId = (Item)255;
        bool m_WasBall;
        float m_SwingStart = -10f, m_SwingDur = 0.6f, m_ThrowStart = -10f, m_EatStart = -10f, m_UseStart = -10f, m_EquipStart = -10f;
        bool m_Hit, m_ImpactKnown;
        float m_FreezeUntil = -10f, m_FreezeE;
        Vector2 m_Sway;
        float m_CrouchK, m_SprintK, m_CarryK, m_Land, m_AirVel, m_Jump, m_BobPhase, m_AimK;
        bool m_WasGrounded = true;
        GameObject m_CradleFor;
        Vector3 m_CradleMid;
        float m_CradleR;

        // ---- for the hands autotest ----
        /// <summary>0: the block hands (the game), 1: the original box arms instead, 2: the alien clawed hands (the look
        /// before the block hands), 3: the block hands and the box arms at once (to line them up).</summary>
        public static int DebugArms;
        /// <summary>When set (>= 0), the swing / throw / eat / use animations are held at this many seconds in.</summary>
        public static float DebugSwingE = -1f, DebugThrowE = -1f, DebugEatE = -1f, DebugUseE = -1f;
        public static bool DebugAim, DebugBall;
        int m_ArmsShown = -1;
        readonly System.Collections.Generic.List<Renderer> m_BoxR = new System.Collections.Generic.List<Renderer>(), m_AlienR = new System.Collections.Generic.List<Renderer>(),
            m_BlockR = new System.Collections.Generic.List<Renderer>();

        /// <summary>The block hands' mesh (each hand has its own, re-shaped as it bends).</summary>
        public const string BlockyMeshName = "blocky arm (posed)";

        /// <summary>The block hands' colours: the hand, the darker knuckle row, and the band round the wrist (in the team
        /// colour, like the old box arm's).</summary>
        public static Color[] BlockyColours(Color tint, Color team)
        {
            var dark = tint * 0.8f; dark.a = 1f;
            var band = team * 0.75f; band.a = 1f;
            return new[] { tint, dark, band };
        }

        /// <summary>The arm shown for this hand (DebugArms 2: the alien clawed one).</summary>
        AlienArm Arm(bool right) => DebugArms == 2 && (right ? m_XR : m_XL) != null ? (right ? m_XR : m_XL) : (right ? m_AR : m_AL);

        /// <summary>For the tests / profiling: how many times the claws were worked out afresh (and how long that took in
        /// all, ms), and how many times an arm's mesh was re-bent.</summary>
        public static int SolveCount, BendCount;
        public static double SolveMsTotal;

        /// <summary>The newest view model (for the hands autotest).</summary>
        public static ViewModel Last;

        /// <summary>For the hands autotest: the alien hands' rigid pieces (palm, forearm, each claw / thumb segment) in world space.</summary>
        public void DebugPieces(System.Collections.Generic.List<(string name, Vector3[] pts, int[] tris)> list)
        {
            if (Arm(true) != null && m_R.gameObject.activeInHierarchy) Arm(true).DebugPieces("R", list);
            if (Arm(false) != null && m_L.gameObject.activeInHierarchy) Arm(false).DebugPieces("L", list);
        }

        public string DebugRest() => Arm(true) != null ? Arm(true).DebugRest() : "no alien arm";
        public Transform DebugHand(bool right) => right ? m_R : m_L;
        /// <summary>(tests) The block hands are both lit smoothly now.</summary>
        public bool DebugBlockySmooth() => m_AR != null && m_AR.ShownSmooth && (m_AL == null || !m_L.gameObject.activeInHierarchy || m_AL.ShownSmooth);
        /// <summary>(tests) The hands shown are the block hands.</summary>
        public bool DebugIsBlocky => m_AR != null && m_AR.IsBlocky;
        public string DebugSolve(bool right) => Arm(right)?.DebugSolve() ?? "-";
        public string DebugOutlines(bool right) => Arm(right)?.DebugOutlines() ?? "{}";

        /// <summary>For the hands autotest: what the hands hold this frame (the item's shown renderers, the ball, the bow's arrow and string).</summary>
        public void DebugHeld(System.Collections.Generic.List<Renderer> list)
        {
            foreach (var go in new[] { m_Item, m_Ball, m_Arrow, m_StringA, m_StringB })
                if (go && go.activeInHierarchy)
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        if (r.enabled && r is MeshRenderer) list.Add(r);
        }

        public ViewModel(Transform cam, Color team)
        {
            Last = this;
            m_Root = new GameObject("ViewModel").transform;
            m_Root.SetParent(cam, false);
            var skin = new Color(0.6f, 0.64f, 0.58f);
            skin = Color.Lerp(skin, team, 0.25f);
            m_R = MakeArm(m_Root, skin, team, true, out m_AR, out m_XR);
            m_L = MakeArm(m_Root, skin, team, false, out m_AL, out m_XL);
            foreach (var r in m_Root.GetComponentsInChildren<Renderer>(true))
            {
                bool alien = false, block = false;
                for (var t = r.transform; t != null && t != m_Root; t = t.parent) { alien |= t.name.StartsWith("psx alienarm"); block |= t.name.StartsWith("blocky arm"); }
                (block ? m_BlockR : alien ? m_AlienR : m_BoxR).Add(r);
            }
            m_ItemHolder = new GameObject("item").transform;
            m_ItemHolder.SetParent(m_Root, false);
            GameSettings.GraphicsChanged += OnGraphicsChanged;
            // what's held is shaded smooth or flat as Settings > Display > SHADING says (the hands shade themselves)
            m_Smooth = SmoothShadeHook.Add(m_Root.gameObject, false);
        }

        readonly SmoothShadeHook m_Smooth;

        /// <summary>The PSX look of what's held comes and goes with the graphics setting: the claws close round whichever
        /// shows (and it's shaded smooth or flat like the rest).</summary>
        void OnGraphicsChanged() { m_PartsDirty = true; m_Smooth?.Refresh(true); }

        public void Destroy()
        {
            GameSettings.GraphicsChanged -= OnGraphicsChanged;
            m_AR?.Free(); m_AL?.Free(); m_XR?.Free(); m_XL?.Free();
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

        static Transform MakeArm(Transform parent, Color skin, Color team, bool right, out AlienArm arm, out AlienArm claws)
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
            // the block hand in place of the boxes above (which stay as a fallback): the alien arm's palm, forearm, claws
            // and thumb made into square blocks, which bend and close round what's held just like the claws did (team
            // tint, or the colour in Settings > Display)
            var tint = HandColorHook.Tint(hand, team);
            var block = AlienArm.Make(hand, right, tint, team, true);
            // (and the alien clawed hand they replaced, hidden: the hands autotest photographs it for comparison)
            claws = AlienArm.Make(hand, right, tint, team, false);
            if (block != null)
                foreach (var r in hand.GetComponentsInChildren<Renderer>()) if (!r.transform.IsChildOf(block.Fit)) r.enabled = false;
            arm = block;
            foreach (var r in hand.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return hand;
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
            if (m_ArmsShown != DebugArms && m_BlockR.Count > 0)
            {
                m_ArmsShown = DebugArms;
                foreach (var r in m_BoxR) if (r) r.enabled = DebugArms == 1 || DebugArms == 3;
                foreach (var r in m_BlockR) if (r) r.enabled = DebugArms == 0 || DebugArms == 3;
                foreach (var r in m_AlienR) if (r) r.enabled = DebugArms == 2;
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
                m_PartsDirty = true;
                m_Smooth?.Refresh(true);
            }
            bool wantString = !s.Ball && s.Item == Item.Bow;
            if (wantString != (m_StringA != null))
            {
                m_PartsDirty = true;
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
                m_PartsDirty = true;
                if (m_Arrow) { Object.Destroy(m_Arrow); m_Arrow = null; }
                else
                {
                    m_Arrow = ItemModels.Create(Item.Arrow, m_Root);
                    foreach (var r in m_Arrow.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    m_Smooth?.Refresh(true);
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
            // the spear is always carried the way it is when sprinting (lowered and tilted in), walking or standing - only
            // the bigger bob and the free hand pumping are the sprint's own
            m_CarryK = Mathf.MoveTowards(m_CarryK, s.Item == Item.Spear && !s.Ball && !busy ? 1f : 0f, dt * 6f);
            float pose = Mathf.Max(m_SprintK, m_CarryK);
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
            Vector3 shared = bob + new Vector3(m_Sway.x + pose * 0.03f, m_Sway.y - m_CrouchK * 0.02f + breathe + m_Jump - landDip - pose * 0.07f, -pose * 0.04f)
                + Vector3.down * (1f - equip) * 0.45f;
            Quaternion sharedRot = Quaternion.Euler(m_Sway.y * 300f + pose * 14f + landDip * 60f, -m_Sway.x * 300f - pose * 22f, bx * bobAmt * (1.5f + 3f * m_SprintK) + pose * 12f);

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
            if (m_PartsDirty) { GatherParts(); m_PartsDirty = false; }
            Arm(true)?.Pose(m_RP, m_R, m_Root, m_Parts);
            Arm(false)?.Pose(m_LP, m_L, m_Root, m_Parts);
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

        /// <summary>
        /// A big rock cupped in both hands in front of you (Rust's rock): it sits in the palms, low in the middle of the
        /// screen, the claws up its sides; brought down two-handed.
        /// </summary>
        void PoseRock(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            var idle = new KP(new Vector3(0.04f, -0.18f, 0.41f), -8f, 0);
            var raised = new KP(new Vector3(0.05f, 0.03f, 0.36f), -45f, 0);
            var slam = new KP(new Vector3(0.03f, -0.27f, 0.62f), 35f, 0);
            var recoil = new KP(new Vector3(0.05f, -0.09f, 0.5f), -15f, 0);
            var follow = new KP(new Vector3(0.02f, -0.48f, 0.56f), 60f, 0);
            var k = Chop(idle, raised, slam, recoil, follow, swinging, e);
            var rot = sharedRot * Quaternion.Euler(k.HandX, -6f, 0);
            AttachItemToRoot(shared + k.Pos, rot, 1f);
            // the rock sits in the two hands: each palm under its lower side, turned up and in, and the claws reaching up
            // its sides and curling in round it (the forearms run down and a little out, off the bottom of the screen).
            // The hands go by the size of the rock showing - the PSX rock is a narrower slab than the Normal one, and it
            // can turn up a frame or two after the graphics change - from its meshes' own bounds, so they hold still
            // through a swing.
            Vector3 lo = new Vector3(-0.12f, -0.1f, -0.08f), hi = new Vector3(0.12f, 0.14f, 0.16f);
            if (m_Item)
            {
                bool any = false;
                foreach (var mf in m_Item.GetComponentsInChildren<MeshFilter>())
                {
                    var r = mf.GetComponent<Renderer>();
                    if (r == null || !r.enabled || mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var q = m_ItemHolder.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        if (!any) { lo = hi = q; any = true; }
                        else { lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q); }
                    }
                }
            }
            var mid = (lo + hi) * 0.5f;
            var half = Vector3.Max((hi - lo) * 0.5f, Vector3.one * 0.03f);
            float hx = Mathf.Clamp(half.x, 0.04f, 0.15f);
            var centre = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(mid));
            // (the knuckles low on its sides and a little nearer you than its middle, the hands leaning in under it: the
            // palms face in and forwards, the claws reach up its sides and the thumbs curl round its front, as in Rust)
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(mid + new Vector3(hx * 0.72f, -half.y * 0.65f, -half.z * 0.7f)));
            m_R.localRotation = rot * Quaternion.LookRotation(new Vector3(-0.5f, 0.84f, 0.2f), Vector3.back);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(mid + new Vector3(-hx * 0.72f, -half.y * 0.65f, -half.z * 0.7f)));
            m_L.localRotation = rot * Quaternion.LookRotation(new Vector3(0.5f, 0.84f, 0.2f), Vector3.back);
            m_RP = m_LP = HandPose.PalmAt(centre, 1f, true);
        }

        /// <summary>
        /// Hatchet / pickaxe / sword (Rust's equip pose): one fist round the end of the handle at the bottom right, the
        /// forearm rising into it from below the screen (bent at the wrist), the handle standing up out of the fist with
        /// the head a little forward, its blade pointing left. HandX is the fist's pitch, ItemX how far the handle leans
        /// forward of square across it (kept small, so the claws close all the way round), Yaw which way the fist points:
        /// at rest out to the right, so you see the back of the fist and its knuckles round the handle, as in Rust (Twist
        /// turns the tool round its handle to put the blade to the left); the swing turns the fist and the blade to face
        /// forward, lays the tool back, then brings it down.
        /// </summary>
        void PoseTool(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            var idle = new KP(new Vector3(0.21f, -0.27f, 0.66f), -8f, 14f, 42f, -95f);
            var raised = new KP(new Vector3(0.27f, -0.13f, 0.52f), -60f, 10f, 0f, 0f);
            var slam = new KP(new Vector3(0.12f, -0.34f, 0.78f), 70f, 16f, -10f, 0f);
            var recoil = new KP(new Vector3(0.18f, -0.23f, 0.7f), 0f, 14f, 25f, -55f);
            var follow = new KP(new Vector3(0.1f, -0.44f, 0.74f), 95f, 17f, -10f, 0f);
            var k = Chop(idle, raised, slam, recoil, follow, swinging, e);
            Set(m_R, shared + k.Pos, sharedRot * Quaternion.Euler(k.HandX, k.Yaw, 4f));
            AttachItemToRight(new Vector3(0, -0.06f, 0.01f), new Vector3(k.ItemX, 0, 0));
            m_ItemHolder.localRotation *= Quaternion.Euler(0f, k.Twist, 0f);
            m_RP = HandPose.Fist.WithArm(ArmTo(m_R.localPosition, shared + RightElbow));
            HideLeft();
        }

        // where the elbows are (view model space, before the bob and sway): each forearm runs from its wrist towards
        // its elbow, so it comes up into the hand from below the edge of the screen wherever the hand goes
        static readonly Vector3 RightElbow = new Vector3(0.42f, -0.75f, 0.2f), LeftElbow = new Vector3(-0.42f, -0.75f, 0.2f);

        /// <summary>The way a forearm runs from the wrist at `hand` back towards the elbow at `elbow` (view model space).</summary>
        static Vector3 ArmTo(Vector3 hand, Vector3 elbow) => (elbow - hand).normalized;

        /// <summary>
        /// The spear: carried low like the sprint pose; RMB raises it over the shoulder (blended in over a moment, not
        /// snapped), the throw drives it forward and lets go, and the next spear (if you have one) comes back up from below
        /// while the throw recovers. Letting go of RMB lowers it back down the same way.
        /// </summary>
        void PoseSpear(State s, Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            float throwK = Mathf.Clamp01((Time.time - m_ThrowStart) / 0.25f);
            bool throwing = throwK < 1f;
            float dt = Time.deltaTime;
            if (throwing) m_SpearAimK = 1f;
            else if (Time.time - m_ThrowStart < 0.3f) m_SpearAimK = 0f; // (just thrown: the next one comes up from below)
            else m_SpearAimK = Mathf.MoveTowards(m_SpearAimK, s.SpearAim ? 1f : 0f, dt / (s.SpearAim ? 0.2f : 0.16f));
            float k = Smooth(m_SpearAimK);
            if (k >= 0.999f || throwing) { PoseSpearAim(s, shared, sharedRot, throwK, throwing); return; }
            // after a throw the next spear rises back into the hand
            float back = Smooth((Time.time - m_ThrowStart - 0.25f) / 0.3f);
            var low = shared + Vector3.down * (1f - back) * 0.4f;
            PoseSpearIdle(low, sharedRot, swinging, e);
            if (k <= 0.001f) return;
            // part way up / down: between the carry pose and the wound-up one
            var hp = m_ItemHolder.localPosition; var hr = m_ItemHolder.localRotation;
            var rp = m_R.localPosition; var rr = m_R.localRotation;
            var lp = m_L.localPosition; var lr = m_L.localRotation;
            var rpose = m_RP; var lpose = m_LP;
            PoseSpearAim(s, shared, sharedRot, 1f, false);
            Set(m_ItemHolder, Vector3.Lerp(hp, m_ItemHolder.localPosition, k), Quaternion.Slerp(hr, m_ItemHolder.localRotation, k));
            Set(m_R, Vector3.Lerp(rp, m_R.localPosition, k), Quaternion.Slerp(rr, m_R.localRotation, k));
            Set(m_L, Vector3.Lerp(lp, m_L.localPosition, k), Quaternion.Slerp(lr, m_L.localRotation, k));
            if (k < 0.5f) { m_RP = rpose; m_LP = lpose; }
        }

        float m_SpearAimK;

        void PoseSpearAim(State s, Vector3 shared, Quaternion sharedRot, float throwK, bool throwing)
        {
            // the spear lives on the rig; the hand grabs the shaft with a natural wrist angle
            {
                float d = throwing ? 0f : s.Draw;
                Vector3 pos = new Vector3(0.4f, 0.17f, 0.15f - d * 0.25f);
                if (throwing) pos = Vector3.Lerp(pos, new Vector3(0.15f, -0.02f, 0.8f), Smooth(throwK));
                var rot = sharedRot * Quaternion.Euler(86f, -6f, 0);
                AttachItemToRoot(shared + pos, rot);
                if (m_Item) m_Item.SetActive(!throwing || throwK < 0.4f);
                m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.02f, 0.15f, -0.03f)));
                // (the hand square across the shaft, so the claws can close round it)
                var spearAxis = rot * Vector3.up;
                var handZ = sharedRot * Quaternion.Euler(-55f, -30f, -20f) * Vector3.forward;
                m_R.localRotation = Quaternion.LookRotation((handZ - spearAxis * Vector3.Dot(handZ, spearAxis)).normalized, spearAxis);
                Set(m_L, shared + new Vector3(-0.16f, -0.14f, 0.55f), sharedRot * Quaternion.Euler(-25f, 25f, 25f));
                m_RP = HandPose.FistThumb(rot * Vector3.up).WithArm(ArmTo(m_R.localPosition, shared + new Vector3(0.5f, -0.35f, -0.1f))); // (the elbow cocked out to the side)
                m_LP = HandPose.PalmAt(m_L.localPosition + sharedRot * new Vector3(0.1f, 0.05f, 0.1f), 0.45f); // the free hand points the way
            }
        }

        void PoseSpearIdle(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            if (m_Item && !m_Item.activeSelf) m_Item.SetActive(true);
            float thrust = 0;
            if (swinging)
            {
                if (e < ImpactTime) thrust = Smooth(e / ImpactTime);
                else if (m_ImpactKnown && m_Hit) thrust = 1f - Smooth((e - ImpactTime) / 0.1f) * 1.3f + Smooth((e - ImpactTime - 0.1f) / 0.3f) * 0.3f;
                else thrust = 1f + Smooth((e - ImpactTime) / 0.08f) * 0.15f - Smooth((e - ImpactTime - 0.1f) / Mathf.Max(0.15f, m_SwingDur * 0.6f)) * 1.15f;
            }
            // (Rust's spear: one fist low on the right, the shaft standing up out of it with the tip forward and up
            // towards the middle; a thrust levels it out and drives it forward. The fist points out to the right like
            // the tools', square round the shaft, the back of it towards you, and the forearm comes up into it from
            // the bottom right, bent at the wrist.)
            float level = Mathf.Clamp01(thrust);
            var spearRot = sharedRot * Quaternion.Euler(Mathf.Lerp(40f, 84f, level), Mathf.Lerp(-20f, -5f, level), 0);
            var grip = shared + new Vector3(0.23f, -0.26f, 0.52f) + new Vector3(-0.06f, 0.1f, 0.3f) * thrust;
            AttachItemToRoot(grip - spearRot * new Vector3(0f, SpearGrip, 0f), spearRot);
            var shaft = spearRot * Vector3.up;
            var across = sharedRot * new Vector3(0.67f, 0f, 0.74f);
            m_R.localPosition = grip;
            m_R.localRotation = Quaternion.LookRotation((across - shaft * Vector3.Dot(across, shaft)).normalized, shaft);
            m_RP = HandPose.FistThumb(shaft).WithArm(ArmTo(grip, shared + RightElbow));
            HideLeft();
        }

        /// <summary>Where the spear is held (item +Y from its origin): about a third of the way up the shaft.</summary>
        const float SpearGrip = 0.08f;

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

            // bow hand: fist on the grip, forearm reaching back to the lower left. (The arm's line turns with the view's
            // sway and the sprint tilt like the bow does, so the hand sits still on the grip: worked out from a shoulder fixed
            // to the camera it slid round the grip with every step, and the claws had to be re-fitted every frame - that
            // was the bow's sprint glitch and its frame-rate drop.)
            var shoulder = new Vector3(-0.3f, -0.55f, -0.1f);
            var armDir = DebugLegacyBow ? (grip - shoulder).normalized : (sharedRot * (grip - shared - shoulder)).normalized;
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
            const float arrowLen = 0.765f; // (to the very end of the arrow, behind its fletching)
            var nock = tip - dir * arrowLen;
            if (m_Arrow)
            {
                // the arrow model runs along +Y from -0.15 (fletching) to 0.6 (tip)
                // (rolled with the bow, not kept upright: an arrow that turned against the bow at every step of a run made
                // the claws round it be re-fitted every frame)
                m_Arrow.transform.localRotation = Quaternion.LookRotation(dir, DebugLegacyBow ? Vector3.up : bowRot * Vector3.up) * Quaternion.Euler(90f, 0, 0);
                m_Arrow.transform.localPosition = tip - dir * 0.6f;
            }

            // string hand holds the nock at the lower right; drawn, it's back by your face (out of view)
            var stringRest = grip + bowRot * new Vector3(0, 0, -0.03f);
            var restNock = m_Arrow ? (DebugLegacyBow ? nock : grip + bowRot * BowRestNock) : stringRest;
            var pull = Vector3.Lerp(restNock, grip + new Vector3(0.015f, -0.015f, -0.47f), e);
            // keep the string in front of the camera. At rest the nock is kept on the bow (BowRestNock, worked out from
            // the bow's resting pose) so it moves with it: clamping it to the camera's depth there made it slide along the
            // string every step of a run (and the claws round it with it); drawn back it's clamped to the view.
            if (DebugLegacyBow) pull.z = Mathf.Max(pull.z, 0.12f);
            else pull.z = Mathf.Max(pull.z, Mathf.Lerp(-1f, 0.12f, e));
            // the claws hooked round the string from the right (an archer's draw): pointing left across it, palm back
            // towards you, so they curl round the string beside the nock
            var across = -Vector3.Cross(bowRot * Vector3.up, dir).normalized;
            m_R.localPosition = Vector3.Lerp(pull - across * 0.035f - bowRot * Vector3.up * 0.02f - dir * 0.07f, new Vector3(0.3f, -0.5f, -0.3f), e); // (just behind the nock, clear of the fletching)
            m_R.localRotation = Quaternion.LookRotation((across + dir * 0.3f).normalized, bowRot * Vector3.up);
            m_RP = HandPose.PalmAt(m_R.localPosition - dir * 0.2f, 0.85f);

            // the string runs from both limb tips to the nock
            if (m_StringA && m_Item)
            {
                var top = m_Root.InverseTransformPoint(m_Item.transform.TransformPoint(new Vector3(0, 0.375f, -0.02f)));
                var bottom = m_Root.InverseTransformPoint(m_Item.transform.TransformPoint(new Vector3(0, -0.375f, -0.02f)));
                var mid = Vector3.Lerp(stringRest, pull, Mathf.Max(e, 0.05f));
                mid.z = Mathf.Max(mid.z, DebugLegacyBow ? 0.12f : Mathf.Lerp(-1f, 0.12f, e));
                var sup = DebugLegacyBow ? Vector3.up : bowRot * Vector3.forward; // (the strings roll with the bow too)
                SetString(m_StringA.transform, top, mid, sup);
                SetString(m_StringB.transform, bottom, mid, sup);
            }
        }

        /// <summary>For the bow sprint profile: the old bow pose (arm from a fixed shoulder, the nock clamped to the view).</summary>
        public static bool DebugLegacyBow;

        /// <summary>
        /// Where the arrow's nock sits on the resting bow, in the bow's own space from its grip: the old resting pose (bow
        /// at rest, no sway or sprint) with the nock brought forward to 0.12 m in front of the camera, as it was drawn.
        /// </summary>
        static readonly Vector3 BowRestNock = ComputeBowRestNock();

        static Vector3 ComputeBowRestNock()
        {
            var grip = new Vector3(0.2f, -0.22f, 0.62f);
            var bowRot = Quaternion.Euler(-6f, -12f, -14f);
            var tip = grip + bowRot * new Vector3(-0.02f, 0.01f, 0.28f);
            var dir = (bowRot * new Vector3(-0.08f, 0.06f, 1f)).normalized;
            var nock = tip - dir * 0.765f;
            nock.z = Mathf.Max(nock.z, 0.12f);
            return Quaternion.Inverse(bowRot) * (nock - grip);
        }

        static void SetString(Transform t, Vector3 a, Vector3 b, Vector3 up)
        {
            var d = b - a;
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d, up) : Quaternion.identity;
            t.localScale = new Vector3(0.004f, 0.004f, d.magnitude);
        }

        /// <summary>
        /// Rust crossbow / guns: held low on the right pointing ahead, RMB brings it up to the eye, and after a shot it's
        /// cranked back for the next bolt. The trigger hand is a fist round the pistol grip, its forearm rising into it
        /// from below the bottom right of the screen; a long gun's fore-end rests in the other palm (the claws up its far
        /// side), that forearm coming in from the bottom left. A pistol is held in the one hand, like Rust's.
        /// </summary>
        void PoseCrossbow(State s, Vector3 shared, Quaternion sharedRot)
        {
            float aim = m_AimK = Mathf.MoveTowards(m_AimK, s.Aim ? 1f : 0f, Time.deltaTime * 7f);
            float kick = Mathf.Clamp01(1f - (Time.time - m_UseStart) / 0.25f);
            bool shortGun = s.Item == Item.Pistol || s.Item == Item.Revolver || s.Item == Item.PortalGun;
            var hip = shortGun ? new Vector3(0.17f, -0.15f, 0.42f) : new Vector3(0.2f, -0.19f, 0.47f);
            var pos = shared + Vector3.Lerp(hip, new Vector3(0f, -0.075f, 0.3f), Smooth(aim)) + new Vector3(0, 0.02f, -0.07f) * kick;
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
            // the trigger hand: a fist square round the grip, thumb up it, pointing ahead and out to the right like the
            // tools' (so you see the back of the fist round the grip); the forearm comes up into it from the bottom right,
            // bent at the wrist
            var gripAxis = rot * GunGripAxis(s.Item);
            var ahead = rot * new Vector3(0.55f, 0.1f, 0.83f);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(GunGrip(s.Item)));
            m_R.localRotation = Quaternion.LookRotation((ahead - gripAxis * Vector3.Dot(ahead, gripAxis)).normalized, gripAxis);
            m_RP = HandPose.FistThumb(gripAxis).WithArm(ArmTo(m_R.localPosition, shared + Vector3.Lerp(new Vector3(0.32f, -0.7f, 0f), new Vector3(0.14f, -0.6f, -0.1f), aim)));
            var leftHold = GunFore(s.Item);
            var leftPull = new Vector3(-0.04f, 0.04f, -0.05f);
            var hold = Vector3.Lerp(leftHold, leftPull, r);
            if (shortGun)
            {
                // one-handed (the other hand only comes up to tip the gun open for a reload): the palm on the side of
                // the gun, the claws over the top of it
                HideLeft();
                if (r > 0f)
                {
                    m_L.localPosition = Vector3.Lerp(m_L.localPosition, m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(hold + new Vector3(-0.05f, 0f, 0f))), Smooth(r * 3f));
                    m_L.localRotation = rot * Quaternion.Euler(-20f - 40f * r, 20f, 70f);
                }
                m_LP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, 0.03f, hold.z))), 1f, true);
            }
            else
            {
                // the fore-end rests in the palm, the hand square across under it and the claws curling up round its far
                // side; the forearm comes up into it from the bottom left, bent at the wrist (a reload pulls the string
                // back with this hand)
                m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(hold));
                m_L.localRotation = rot * Quaternion.LookRotation(Vector3.Lerp(new Vector3(1f, 0.3f, 0f), new Vector3(0.55f, 0.6f, 0.55f), r), Vector3.up);
                m_LP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, GunForeAxisY(s.Item), hold.z))), 1f, true)
                    .WithArm(ArmTo(m_L.localPosition, shared + new Vector3(-0.3f, -0.7f, 0.1f)));
            }
        }

        /// <summary>Which way each gun's grip runs (item space, up it): most rake back a little.</summary>
        static Vector3 GunGripAxis(Item item)
        {
            float rake;
            switch (item)
            {
                case Item.Pistol: rake = 12f; break;
                case Item.Revolver: rake = 18f; break;
                case Item.Sniper:
                case Item.Shotgun:
                case Item.Crossbow: rake = 15f; break;
                case Item.RocketLauncher: rake = 10f; break;
                default: rake = 0f; break;
            }
            return Quaternion.Euler(-rake, 0f, 0f) * Vector3.up;
        }

        /// <summary>
        /// Where a long gun's fore-end rests in the support hand (item space): the knuckles just past its right edge
        /// underneath, so the palm runs across under it and the claws reach up its far side.
        /// </summary>
        static Vector3 GunFore(Item item)
        {
            switch (item)
            {
                case Item.Sniper: return new Vector3(0.035f, -0.04f, 0.2f);
                case Item.Shotgun: return new Vector3(0.035f, -0.04f, 0.22f);
                case Item.RocketLauncher: return new Vector3(0.07f, -0.05f, 0.3f);
                default: return new Vector3(0.035f, -0.045f, 0.2f);
            }
        }

        /// <summary>The height (item y) of the middle of what the support hand holds, which its palm faces.</summary>
        static float GunForeAxisY(Item item) => item == Item.RocketLauncher ? 0.02f : 0.0f;

        /// <summary>Where each gun's grip is (item space): the trigger hand goes round it.</summary>
        static Vector3 GunGrip(Item item)
        {
            switch (item)
            {
                case Item.Crossbow: return new Vector3(0f, -0.085f, -0.03f);
                case Item.Pistol: return new Vector3(0f, -0.07f, -0.02f);
                case Item.Revolver: return new Vector3(0f, -0.06f, -0.075f);
                case Item.Shotgun: return new Vector3(0f, -0.085f, -0.03f);
                case Item.Sniper: return new Vector3(0f, -0.08f, 0.01f);
                case Item.PortalGun: return new Vector3(0f, -0.115f, -0.05f); // (low down: the body sits right on top of the grip)
                case Item.RocketLauncher: return new Vector3(0f, -0.12f, 0.045f);
                default: return new Vector3(0.02f, -0.1f, -0.06f);
            }
        }

        void PoseRam(State s, Vector3 shared, Quaternion sharedRot)
        {
            float strikeK = Mathf.Clamp01((Time.time - m_UseStart) / 0.45f);
            float thrust = strikeK < 1f ? (strikeK < 0.3f ? Smooth(strikeK / 0.3f) : 1f - Smooth((strikeK - 0.3f) / 0.7f)) : 0f;
            var pos = shared + new Vector3(0.18f, -0.4f, 0.55f - s.RamCharge * 0.3f + thrust * 0.7f);
            var rot = sharedRot * Quaternion.Euler(-4f + s.RamCharge * 6f, -8f, 0);
            AttachItemToRoot(pos, rot, 0.75f);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.13f, -0.08f, -0.28f)));
            m_R.localRotation = rot * Quaternion.LookRotation(new Vector3(0.45f, 0.85f, 0.25f), Vector3.left);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.13f, -0.08f, 0.22f)));
            m_L.localRotation = rot * Quaternion.LookRotation(new Vector3(-0.45f, 0.85f, 0.25f), Vector3.right);
            // palms on the log's sides (facing its middle), the claws reaching up round it and over the top
            m_RP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, 0f, -0.28f))), 1f, true).WithArm(ArmTo(m_R.localPosition, shared + RightElbow));
            m_LP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, 0f, 0.22f))), 1f, true).WithArm(ArmTo(m_L.localPosition, shared + LeftElbow));
        }

        /// <summary>Chainsaw in both hands like the ram; it shakes while the trigger is held.</summary>
        void PoseChainsaw(State s, Vector3 shared, Quaternion sharedRot)
        {
            var buzz = s.Firing ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 0.006f : Vector3.zero;
            var pos = shared + new Vector3(0.22f, -0.36f, 0.5f + (s.Firing ? 0.06f : 0f)) + buzz;
            var rot = sharedRot * Quaternion.Euler(s.Firing ? 4f : -2f, -10f, 0);
            AttachItemToRoot(pos, rot, 1f);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, 0.17f, -0.12f))); // (on top of the top handle)
            m_R.localRotation = rot * Quaternion.Euler(-20f, -10f, -70f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.01f, 0.17f, 0.06f)));
            m_L.localRotation = rot * Quaternion.Euler(-20f, 10f, 70f);
            // the top handle sits right on the body, so there's no getting round it: palms on top of it, the claws
            // curling down over its sides
            m_RP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, 0.13f, -0.12f))), 1f, true).WithArm(ArmTo(m_R.localPosition, shared + RightElbow));
            m_LP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, 0.13f, 0.06f))), 1f, true).WithArm(ArmTo(m_L.localPosition, shared + LeftElbow));
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
            Set(m_R, ballPos + new Vector3(0.62f, 0.05f, -0.15f + push), sharedRot * Quaternion.LookRotation(new Vector3(0.2f, 0.15f, 0.97f), Vector3.left));
            Set(m_L, ballPos + new Vector3(-0.62f, 0.05f, -0.15f + push), sharedRot * Quaternion.LookRotation(new Vector3(-0.2f, 0.15f, 0.97f), Vector3.right));
            m_RP = m_LP = HandPose.PalmAt(ballPos, 1f, true);
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
                case Item.BuildingPlan:
                    // (the board turned round so it leans away from you, its rolled-up top on the far side, held by its
                    // bottom edge: the hand a little forward, onto it)
                    m_R.localPosition += m_R.localRotation * new Vector3(0f, -0.015f, 0.085f);
                    AttachItemToRight(new Vector3(0, 0.045f, 0.135f), new Vector3(0, 180, 0), 0.55f); break;
                case Item.Chest: AttachItemToRight(new Vector3(0, 0.06f, 0.08f), new Vector3(0, 20, 0), 0.9f); break;
                case Item.Workbench:
                case Item.Workbench2: AttachItemToRight(new Vector3(0, 0.04f, 0.08f), new Vector3(0, 200, 0), 0.8f); break;
                case Item.Barrier: AttachItemToRight(new Vector3(0, 0.04f, 0.06f), new Vector3(0, 20, 0), 0.8f); break;
                case Item.Arrow: AttachItemToRight(new Vector3(0, -0.1f, 0.02f), new Vector3(-30, 0, 0), 0.8f); break;
                default: AttachItemToRight(new Vector3(0, 0.05f, 0.07f), Vector3.zero, 1f); break;
            }
            // things held by a handle get a fist; things that sit on the hand (food, C4, a chest...) are cradled, the claws
            // curling up round them
            if (item == Item.DeathWand || item == Item.Arrow) m_RP = HandPose.FistThumb(m_Root.InverseTransformDirection(m_ItemHolder.up)); // round the shaft
            else if (item == Item.BuildingPlan) m_RP = HandPose.PalmAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0f, -0.07f, 0.045f))), 1f, true); // the palm on the board's face towards you, just above its bottom edge (a point beyond it, in model space), the claws round it
            else
            {
                if (m_CradleFor != m_Item)
                {
                    // the size of the thing (once per item): its bounds round the item holder
                    m_CradleFor = m_Item;
                    Bounds b = default; bool any = false;
                    if (m_Item)
                        foreach (var r in m_Item.GetComponentsInChildren<Renderer>())
                        {
                            var rb = r.bounds;
                            rb.center = m_ItemHolder.InverseTransformPoint(rb.center);
                            if (any) b.Encapsulate(rb); else { b = rb; any = true; }
                        }
                    m_CradleMid = any ? b.center : Vector3.zero;
                    m_CradleR = any ? Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.y, b.extents.z), 0.02f, 0.1f) : -1f;
                }
                // (nothing to show, e.g. the airdrop signal: an empty fist, like the box hand)
                if (m_CradleR > 0f) m_RP = HandPose.CradleAt(m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(m_CradleMid)), m_CradleR);
            }
            m_RP = m_RP.WithArm(ArmTo(m_R.localPosition, shared + RightElbow)); // (the forearm up into the hand from below the screen)
            HideLeft();
        }
    }
}
