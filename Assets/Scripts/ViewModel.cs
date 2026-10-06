using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// First-person hands: the block hands (a box fist, a darker knuckle row, a thumb, a team-coloured wrist band and a
    /// square forearm). Every item has a hand pose. The rock is held in both hands; melee swings raise,
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
            /// <summary>The revolver: how many rounds this reload puts in (just the fired ones).</summary>
            public int Rounds;
            public Vector2 Look;
        }

        struct KP
        {
            public Vector3 Pos; public float HandX, ItemX;
            public KP(Vector3 p, float hx, float ix) { Pos = p; HandX = hx; ItemX = ix; }
            public static KP Lerp(KP a, KP b, float t) => new KP(Vector3.LerpUnclamped(a.Pos, b.Pos, t), Mathf.LerpUnclamped(a.HandX, b.HandX, t), Mathf.LerpUnclamped(a.ItemX, b.ItemX, t));
        }

        readonly Transform m_Root, m_R, m_L, m_ItemHolder;
        GameObject m_Item, m_Arrow, m_Ball, m_StringA, m_StringB;
        Item m_ItemId = (Item)255;
        bool m_WasBall;
        float m_SwingStart = -10f, m_SwingDur = 0.6f, m_ThrowStart = -10f, m_EatStart = -10f, m_UseStart = -10f, m_EquipStart = -10f;
        bool m_Hit, m_ImpactKnown;
        float m_FreezeUntil = -10f, m_FreezeE;
        Vector2 m_Sway;
        float m_CrouchK, m_SprintK, m_CarryK, m_Land, m_AirVel, m_Jump, m_BobPhase, m_AimK;
        bool m_WasGrounded = true;

        // ---- for the hands autotest ----
        /// <summary>When set (>= 0), the swing / throw / eat / use animations are held at this many seconds in.</summary>
        public static float DebugSwingE = -1f, DebugThrowE = -1f, DebugEatE = -1f, DebugUseE = -1f;
        public static bool DebugAim, DebugBall;

        /// <summary>The newest view model (for the hands autotest).</summary>
        public static ViewModel Last;
        /// <summary>The hands' root (under the camera): everything first-person is under it.</summary>
        public Transform Root => m_Root;

        public Transform DebugHand(bool right) => right ? m_R : m_L;
        public Transform DebugRoot => m_Root;

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
            m_R = MakeArm(m_Root, team, true);
            m_L = MakeArm(m_Root, team, false);
            m_ItemHolder = new GameObject("item").transform;
            m_ItemHolder.SetParent(m_Root, false);
            GameSettings.GraphicsChanged += OnGraphicsChanged;
            // the hands and what's held are shaded smooth or flat as Settings > Display > SHADING says
            m_Smooth = SmoothShadeHook.Add(m_Root.gameObject, false);
        }

        readonly SmoothShadeHook m_Smooth;

        /// <summary>The PSX look of what's held comes and goes with the graphics setting: shaded smooth or flat like the rest.</summary>
        void OnGraphicsChanged() => m_Smooth?.Refresh(true);

        public void Destroy()
        {
            GameSettings.GraphicsChanged -= OnGraphicsChanged;
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
        /// <summary>Where the held gun's muzzle is in the world (the wand's tip, the launcher's mouth): its flash goes here.</summary>
        public Vector3 Muzzle()
        {
            switch (m_ItemId)
            {
                case Item.Revolver: return m_ItemHolder.TransformPoint(new Vector3(0, 0.05f, 0.3f));
                case Item.Pistol: return m_ItemHolder.TransformPoint(new Vector3(0, 0.035f, 0.25f));
                case Item.Shotgun: return m_ItemHolder.TransformPoint(new Vector3(0, 0.04f, 0.56f));
                case Item.Sniper: return m_ItemHolder.TransformPoint(new Vector3(0, 0.02f, 0.9f));
                case Item.RocketLauncher: return m_ItemHolder.TransformPoint(new Vector3(0, 0.02f, 0.68f));
                case Item.PortalGun: return m_ItemHolder.TransformPoint(new Vector3(0, 0.02f, 0.34f));
                case Item.DeathWand: return m_ItemHolder.TransformPoint(new Vector3(0, 0.58f, 0));
                default: return m_ItemHolder.TransformPoint(new Vector3(0, 0, 0.3f));
            }
        }

        /// <summary>
        /// A shot's kick, 0..1 by how long ago it was fired: it snaps to full in the first moments, then springs back and
        /// settles (dipping a touch past rest on the way). `dur`: how long the whole thing takes - a heavier gun's is longer.
        /// </summary>
        float Recoil(float dur)
        {
            float t = Time.time - m_UseStart;
            if (t < 0f || t >= dur) return 0f;
            float k = t / dur;
            if (k < 0.1f) return Smooth(k / 0.1f);
            float u = (k - 0.1f) / 0.9f;
            return Mathf.Exp(-u * 4.5f) * Mathf.Cos(u * 5.2f) * (1f - u);
        }

        public void Throw() => m_ThrowStart = Time.time;
        public void Eat() => m_EatStart = Time.time;
        public void Use() => m_UseStart = Time.time;

        static Transform MakeArm(Transform parent, Color team, bool right)
        {
            var hand = new GameObject(right ? "R" : "L").transform;
            hand.SetParent(parent, false);
            float side = right ? 1f : -1f;
            // the skin in the team's shade (or the colour picked in Settings > Display > World colours), kept up to date
            var hook = HandColorHook.Add(hand, team);
            var skin = HandColorHook.Shade(team, HandColorHook.Skin);
            var dark = HandColorHook.Shade(team, HandColorHook.Knuckles);
            var band = HandColorHook.Shade(team, HandColorHook.Band);
            hook.Track(Art.Box(hand, skin, Vector3.zero, new Vector3(0.085f, 0.09f, 0.1f)), HandColorHook.Skin);                                  // fist
            hook.Track(Art.Box(hand, dark, new Vector3(0, 0.005f, 0.055f), new Vector3(0.09f, 0.075f, 0.035f)), HandColorHook.Knuckles);         // knuckles
            hook.Track(Art.Box(hand, skin, new Vector3(-0.045f * side, 0.025f, 0.035f), new Vector3(0.03f, 0.03f, 0.065f), new Vector3(0, 20 * side, 0)), HandColorHook.Skin); // thumb
            hook.Track(Art.Box(hand, band, new Vector3(0, 0, -0.075f), new Vector3(0.082f, 0.082f, 0.035f)), HandColorHook.Band);                // wrist band
            hook.Track(Art.Box(hand, skin, new Vector3(0, 0, -0.32f), new Vector3(0.072f, 0.072f, 0.46f)), HandColorHook.Skin);                  // forearm
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
                m_Smooth?.Refresh(true);
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
                    m_Smooth?.Refresh(true);
                }
            }
            bool wantArrow = !s.Ball && s.Item == Item.Bow && s.HasArrow;
            if (wantArrow != (m_Arrow != null))
            {
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

            if (s.Ball) { PoseBall(shared, sharedRot); return; }
            if (m_Item && !m_Item.activeSelf && s.Item != Item.Spear) m_Item.SetActive(true);

            switch (s.Item)
            {
                case Item.Rock: PoseRock(shared, sharedRot, swinging, swingE); break;
                case Item.Sword: PoseSword(shared, sharedRot, swinging, swingE); break;
                case Item.Hatchet:
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
        KP Chop(KP idle, KP raised, KP slam, KP recoil, KP follow, bool swinging, float e, float upShare = 1f, float followTime = 0.08f)
        {
            if (!swinging) return idle;
            float up = m_Up * upShare, down = m_Down;
            float end = Mathf.Max(0.45f, m_SwingDur * 0.9f);
            if (e < up) return KP.Lerp(idle, raised, Smooth(e / up));
            if (e < down) return KP.Lerp(raised, slam, Smooth((e - up) / (down - up)));
            if (m_ImpactKnown && m_Hit)
            {
                if (e < down + 0.1f) return KP.Lerp(slam, recoil, Smooth((e - down) / 0.1f));
                return KP.Lerp(recoil, idle, Smooth((e - down - 0.1f) / Mathf.Max(0.1f, end - down - 0.1f)));
            }
            if (e < down + followTime) return KP.Lerp(slam, follow, Smooth((e - down) / followTime));
            if (e < down + followTime + 0.08f) return follow;
            return KP.Lerp(follow, idle, Smooth((e - down - followTime - 0.08f) / Mathf.Max(0.1f, end - down - followTime - 0.08f)));
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

        /// <summary>
        /// The sword: a slash from the side. It's drawn back out to the right with the blade laid over, then swept flat
        /// across in a long, slow arc - the blade crossing the crosshair as it lands - and on through to the left on a
        /// miss (a hit stops it there and it comes back). The wind-up is the shorter part; most of the time before the
        /// hit is the arc itself.
        /// </summary>
        void PoseSword(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            // (Pos, the hand's yaw, the blade's yaw across the view: + is out to the right, -90 straight ahead)
            var k = Chop(new KP(new Vector3(0.27f, -0.3f, 0.5f), -18f, -20f), new KP(new Vector3(0.44f, -0.2f, 0.36f), 14f, 15f),
                new KP(new Vector3(0.1f, -0.25f, 0.6f), -35f, -92f), new KP(new Vector3(0.18f, -0.25f, 0.55f), -25f, -55f),
                new KP(new Vector3(-0.12f, -0.33f, 0.52f), -55f, -155f), swinging, e, 0.7f, 0.2f);
            // (-, how far the blade is laid over on its side, how far it leans forward at rest)
            var b = Chop(new KP(Vector3.zero, -12f, 20f), new KP(Vector3.zero, -70f, 0f), new KP(Vector3.zero, -82f, 0f), new KP(Vector3.zero, -70f, 0f),
                new KP(Vector3.zero, -88f, 0f), swinging, e, 0.7f, 0.2f);
            Set(m_R, shared + k.Pos, sharedRot * Quaternion.Euler(-8f, k.HandX, b.HandX));
            var blade = sharedRot * Quaternion.Euler(0f, k.ItemX, 0f) * Quaternion.Euler(b.ItemX, 0f, b.HandX);
            // the grip (0.06 up the hilt) stays in the fist whichever way the blade points
            AttachItemToRoot(m_R.localPosition - blade * new Vector3(0f, 0.06f, 0f), blade);
            HideLeft();
        }

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
            // after a throw the next spear rises back into the hands
            float back = Smooth((Time.time - m_ThrowStart - 0.25f) / 0.3f);
            var low = shared + Vector3.down * (1f - back) * 0.4f;
            PoseSpearIdle(low, sharedRot, swinging, e);
            if (k <= 0.001f) return;
            // part way up / down: between the carry pose and the wound-up one
            var hp = m_ItemHolder.localPosition; var hr = m_ItemHolder.localRotation;
            var rp = m_R.localPosition; var rr = m_R.localRotation;
            var lp = m_L.localPosition; var lr = m_L.localRotation;
            PoseSpearAim(s, shared, sharedRot, 1f, false);
            Set(m_ItemHolder, Vector3.Lerp(hp, m_ItemHolder.localPosition, k), Quaternion.Slerp(hr, m_ItemHolder.localRotation, k));
            Set(m_R, Vector3.Lerp(rp, m_R.localPosition, k), Quaternion.Slerp(rr, m_R.localRotation, k));
            Set(m_L, Vector3.Lerp(lp, m_L.localPosition, k), Quaternion.Slerp(lr, m_L.localRotation, k));
        }

        float m_SpearAimK;

        void PoseSpearAim(State s, Vector3 shared, Quaternion sharedRot, float throwK, bool throwing)
        {
            // the spear lives on the rig; both hands grab the shaft with natural wrist angles
            float d = throwing ? 0f : s.Draw;
            Vector3 pos = new Vector3(0.4f, 0.17f, 0.15f - d * 0.25f);
            if (throwing) pos = Vector3.Lerp(pos, new Vector3(0.15f, -0.02f, 0.8f), Smooth(throwK));
            var rot = sharedRot * Quaternion.Euler(86f, -6f, 0);
            AttachItemToRoot(shared + pos, rot);
            if (m_Item) m_Item.SetActive(!throwing || throwK < 0.4f);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.02f, 0.15f, -0.03f)));
            m_R.localRotation = sharedRot * Quaternion.Euler(-55f, -30f, -20f);
            Set(m_L, shared + new Vector3(-0.16f, -0.14f, 0.55f), sharedRot * Quaternion.Euler(-25f, 25f, 25f));
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
            var spearRot = sharedRot * Quaternion.Euler(80f, -5f, 0);
            AttachItemToRoot(shared + new Vector3(0.22f, -0.25f, 0.08f + thrust * 0.4f), spearRot);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.03f, 0.12f, 0)));
            m_R.localRotation = sharedRot * Quaternion.Euler(-25f, -28f, -15f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.03f, 0.62f, 0)));
            m_L.localRotation = sharedRot * Quaternion.Euler(-30f, 22f, 15f);
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
            // (the bow hand is held well out in front - it used to sit 0.1 nearer, cramped against the view)
            var grip = shared + Vector3.Lerp(new Vector3(0.2f, -0.22f, 0.72f), new Vector3(0.045f, -0.12f, 0.69f), e);
            var bowRot = sharedRot * Quaternion.Euler(Mathf.Lerp(-6f, 0f, e), Mathf.Lerp(-12f, -4f, e), Mathf.Lerp(-14f, -26f, e));
            AttachItemToRoot(grip, bowRot, 1f);

            // bow hand: the fist round the grip itself (the bow's wood is 0.06 in front of the model's origin - the string
            // side), on its lower half so the arrow has the top of the fist to rest on; the forearm reaches back to the
            // lower left
            var shoulder = new Vector3(-0.3f, -0.55f, -0.1f);
            var hold = grip + bowRot * new Vector3(0f, -0.035f, 0.06f);
            var armDir = (hold - shoulder).normalized;
            m_L.localPosition = hold;
            m_L.localRotation = Quaternion.LookRotation(armDir, bowRot * Vector3.up) * Quaternion.Euler(0, 0, 70f);

            // arrow: always runs through the rest - a point on the bow just above the fist and beside the wood - so it never
            // passes through the hand, at rest or anywhere in the draw. Idle it points ahead and a bit left with most of
            // the shaft out in front; drawing slides it back through the rest and swings it straight along the view, so it
            // lines up with the crosshair and the fletching ends up by your face, out of view
            var rest = grip + bowRot * new Vector3(-0.032f, 0.062f, 0.06f);
            var dirIdle = (bowRot * new Vector3(-0.08f, 0.06f, 1f)).normalized;
            var dir = Vector3.Slerp(dirIdle, Vector3.forward, e).normalized;
            var tip = rest + dir * Mathf.Lerp(0.22f, 0.07f, e);
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
            // the shot's kick, each gun its own: the revolver snaps up at the wrist, the shotgun shoves back hard into the
            // shoulder and bucks, the rocket launcher lurches back and takes a while to settle, the portal gun gives a
            // soft pulse (the rest: the short knock back they always had)
            var kickPos = new Vector3(0, 0.02f, -0.07f);
            var kickRot = new Vector3(-8f, 0, 0);
            float kick;
            switch (s.Item)
            {
                case Item.Revolver: kick = Recoil(0.42f); kickPos = new Vector3(0, 0.045f, -0.06f); kickRot = new Vector3(-30f, 2f, -5f); break;
                case Item.Shotgun: kick = Recoil(0.55f); kickPos = new Vector3(0.01f, 0.035f, -0.17f); kickRot = new Vector3(-15f, -2f, 4f); break;
                case Item.RocketLauncher: kick = Recoil(0.8f); kickPos = new Vector3(0, -0.02f, -0.2f); kickRot = new Vector3(-7f, 3f, -7f); break;
                case Item.PortalGun: kick = Recoil(0.4f); kickPos = new Vector3(0, 0.01f, -0.08f); kickRot = new Vector3(-5f, 0, 0); break;
                default: kick = Mathf.Clamp01(1f - (Time.time - m_UseStart) / 0.25f); break;
            }
            kick *= 1f - 0.35f * aim; // (steadier down the sights)
            // the sights at the eye: the revolver's sit higher above its grip than the crossbow's
            var aimed = s.Item == Item.Revolver ? new Vector3(0f, -0.104f, 0.38f) : new Vector3(0f, -0.075f, 0.3f); // (the revolver: the eye just over its notch, the drum and barrel low under the line)
            var pos = shared + Vector3.Lerp(new Vector3(0.2f, -0.2f, 0.42f), aimed, Smooth(aim)) + kickPos * kick;
            var rot = sharedRot * Quaternion.Euler(kickRot.x * kick, Mathf.Lerp(-6f, 0f, aim) + kickRot.y * kick, kickRot.z * kick);
            float r = s.Reload >= 0f ? Mathf.Sin(Mathf.Clamp01(s.Reload) * Mathf.PI) : 0f;
            bool revReload = s.Item == Item.Revolver && s.Reload >= 0f;
            float rp = Mathf.Clamp01(s.Reload);
            if (revReload)
            {
                // the revolver's reload: roll it over to the left with the muzzle up, the drum swings out, a flick shakes
                // the empties out, the thumb feeds rounds in one by one, then a flick of the wrist snaps it shut with a spin
                float open = Smooth(Mathf.InverseLerp(0f, 0.18f, rp)) * (1f - Smooth(Mathf.InverseLerp(0.82f, 0.95f, rp)));
                float eject = Mathf.InverseLerp(0.18f, 0.32f, rp);
                float flick = eject > 0f && eject < 1f ? Mathf.Sin(eject * Mathf.PI) : 0f;
                float snap = Mathf.InverseLerp(0.82f, 0.95f, rp);
                float snapKick = snap > 0f && snap < 1f ? Mathf.Sin(snap * Mathf.PI) : 0f;
                pos += new Vector3(-0.1f, -0.03f + 0.05f * flick, -0.04f) * open + new Vector3(0.02f, 0.03f, 0f) * snapKick;
                rot *= Quaternion.Euler(-30f * open - 25f * flick + 10f * snapKick, 15f * open, 70f * open - 30f * snapKick);
                r = 0f; // (not the generic tip-down)
            }
            else if (r > 0f)
            {
                // tip it down and pull the string back with the left hand
                pos += new Vector3(-0.05f, -0.08f, 0) * r;
                rot *= Quaternion.Euler(35f * r, 10f * r, -15f * r);
            }
            AttachItemToRoot(pos, rot, s.Item == Item.Sniper ? 0.75f : 1f);
            if (m_Item && s.Item == Item.Revolver) PoseDrum(rp, revReload, Mathf.Clamp(s.Rounds, 1, 6));
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
            if (s.Item == Item.Revolver)
            {
                // the revolver: one hand (the right, round the grip) from the hip; the left only comes up to cup the grip
                // from below while you aim down the sights
                var cupPos = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.035f, -0.135f, -0.045f)));
                var cupRot = rot * Quaternion.Euler(-25f, 30f, 55f);
                HideLeft();
                float two = Smooth(aim);
                Set(m_L, Vector3.Lerp(m_L.localPosition, cupPos, two), Quaternion.Slerp(m_L.localRotation, cupRot, two));
                if (revReload)
                {
                    // reloading: the left hand comes over to the open drum and thumbs a round in for each sixth of the load
                    float inK = Smooth(Mathf.InverseLerp(0.15f, 0.3f, rp)) * (1f - Smooth(Mathf.InverseLerp(0.8f, 0.9f, rp)));
                    float load = Mathf.InverseLerp(0.32f, 0.8f, rp) * Mathf.Clamp(s.Rounds, 1, 6); // (a thumb-in per round going in)
                    float thumb = rp > 0.32f && rp < 0.8f ? Mathf.Sin((load - Mathf.Floor(load)) * Mathf.PI) : 0f;
                    var at = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.09f, 0.05f + 0.025f * thumb, -0.02f - 0.02f * thumb)));
                    Set(m_L, Vector3.Lerp(m_L.localPosition, at, inK), Quaternion.Slerp(m_L.localRotation, rot * Quaternion.Euler(-40f, 40f, 80f), inK));
                }
            }
        }

        int m_DrumShown = 6;
        bool m_DrumSnapped;
        /// <summary>The revolver's drum through a reload (p 0..1): it swings out to the left, the six rounds go as the empties
        /// are flicked out, come back one by one as they're thumbed in (a click each, the drum turning a sixth), then it
        /// snaps shut with a spin and a clack.</summary>
        void PoseDrum(float p, bool reloading, int n)
        {
            var d = m_Item.transform.Find("drum");
            if (d == null) return;
            float open = reloading ? Smooth(Mathf.InverseLerp(0f, 0.18f, p)) * (1f - Smooth(Mathf.InverseLerp(0.82f, 0.95f, p))) : 0f;
            float load = Mathf.InverseLerp(0.32f, 0.8f, p);
            // (only the n fired ones are flicked out and thumbed back in)
            int rounds = !reloading || p < 0.24f ? 6 : p < 0.32f ? 6 - n : 6 - n + Mathf.Clamp(Mathf.FloorToInt(load * n) + 1, 0, n);
            float spin = reloading ? Mathf.Min(n - 1, Mathf.FloorToInt(load * n)) * 60f + Smooth(Mathf.InverseLerp(0.82f, 1f, p)) * 720f : 0f;
            d.localPosition = new Vector3(-0.05f * open, 0.03f - 0.012f * open, 0.01f);
            d.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, spin, 0f);
            int i = 0;
            foreach (Transform c in d) if (c.name == "round") c.gameObject.SetActive(i++ < rounds);
            bool snapped = reloading && p > 0.86f;
            if (reloading && p > 0.3f && rounds > m_DrumShown) Sfx.Play2D(Sfx.Clink, 0.35f, 0.08f); // (a round in)
            if (snapped && !m_DrumSnapped) Sfx.Play2D(Sfx.Clink, 0.65f, 0.02f);           // (snapped shut)
            m_DrumSnapped = snapped;
            m_DrumShown = rounds;
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
        }

        void PoseHeld(Item item, Vector3 shared, Quaternion sharedRot)
        {
            float eatK = Mathf.Clamp01((Time.time - m_EatStart) / 0.6f);
            float toMouth = eatK < 1f ? Mathf.Sin(eatK * Mathf.PI) : 0f;
            float useK = Mathf.Clamp01((Time.time - m_UseStart) / 0.3f);
            float use = useK < 1f ? Mathf.Sin(useK * Mathf.PI) : 0f;
            if (item == Item.DeathWand) use = Recoil(0.5f) * 1.5f; // the cast: flicked out hard, springing back
            Vector3 idle = new Vector3(0.26f, -0.27f, 0.45f);
            // a box (the chest, and the workbenches held like it): the hand under it further forward and a touch lower
            if (item == Item.Chest || item == Item.Workbench || item == Item.Workbench2) idle += new Vector3(0f, -0.03f, 0.09f);
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
                case Item.InvisPotion: case Item.SpeedJuice: AttachItemToRight(new Vector3(0, 0.03f, 0.05f), new Vector3(toMouth * -70f, 0, 0), 1.1f); break;
                case Item.Armor: AttachItemToRight(new Vector3(0, 0.02f, 0.08f), new Vector3(0, 160, 0), 0.8f); break;
                case Item.FortTower: AttachItemToRight(new Vector3(0, 0.02f, 0.06f), new Vector3(0, 20, 0), 1f); break;
                case Item.Car: AttachItemToRight(new Vector3(0, 0.02f, 0.08f), new Vector3(0, 20, 0), 0.9f); break;
                case Item.Saddle: AttachItemToRight(new Vector3(0, 0.02f, 0.06f), new Vector3(0, 20, 0), 0.9f); break;
                // (the plan's board is modelled leaning 30 degrees back towards you: tipped 60 forward here, it leans away instead)
                case Item.BuildingPlan: AttachItemToRight(new Vector3(0, 0.03f, 0.05f), new Vector3(60, 0, 0), 0.55f); break;
                case Item.Chest: AttachItemToRight(new Vector3(0, 0.06f, 0.08f), new Vector3(0, 20, 0), 0.9f); break;
                // (the workbenches came later: held like the chest, turned so their front faces you)
                case Item.Workbench:
                case Item.Workbench2: AttachItemToRight(new Vector3(0, 0.04f, 0.08f), new Vector3(0, 200, 0), 0.8f); break;
                case Item.Barrier: AttachItemToRight(new Vector3(0, 0.04f, 0.06f), new Vector3(0, 20, 0), 0.8f); break;
                case Item.Arrow: AttachItemToRight(new Vector3(0, -0.1f, 0.02f), new Vector3(-30, 0, 0), 0.8f); break;
                default: AttachItemToRight(new Vector3(0, 0.05f, 0.07f), Vector3.zero, 1f); break;
            }
            HideLeft();
        }
    }
}
