using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// First-person hands: the alien arms with three jointed fingers and a thumb (ViewModel.Hands.cs builds them,
    /// ViewModel.Fingers.cs poses every finger on every item), or - Settings > Display > Alien hands off - the old block
    /// hands (a box fist, a darker knuckle row, a thumb, a team-coloured wrist band and a square forearm). Every item has a
    /// hand pose, and the holds and animations are modelled on Rust's (from footage of every Rust weapon): each item
    /// comes up from below with a twist and settles when you switch to it; the rock is cupped in both hands from below
    /// and smashed down from overhead; tools are held low in the middle in one fist and chopped down from a wind-up out to
    /// the right; the bow is held tilted well over until you draw; the spear is carried in one hand pointing up and away;
    /// C4 is held in both hands; thrown things are drawn back first.
    ///
    /// How it all moves (ViewModel.Anim.cs has the pieces): a swing is a short draw back, a strike that speeds up along a
    /// curve to land in view at the crosshair exactly when the hit does (m_Down), then either a bounce back off the hit
    /// (after a short hit-stop) or a follow-through that slows down still in view, and a way back to rest that swings a
    /// touch past and settles - never a straight line between poses, never off the screen. The hands have weight: they
    /// lag behind as you look round, lean into a strafe and dip on a landing, each on a spring. Locomotion: figure-8
    /// walk bob, a lowered and tilted sprint pose with the free hand pumping, idle breathing, jump lag.
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
            /// <summary>You're invisible (the Invisibility Potion): your own hands vanish - only what you hold still shows.</summary>
            public bool Ghost;
            public float Draw, RamCharge, Bob, Speed, VelY, Reload;
            /// <summary>The revolver: how many rounds this reload puts in (just the fired ones).</summary>
            public int Rounds;
            public Vector2 Look;
            /// <summary>How fast you're moving over the ground, your own way round: x to your right, y forwards (m/s).</summary>
            public Vector2 Move;
        }

        readonly Transform m_Root, m_R, m_L, m_ItemHolder;
        GameObject m_Item, m_Arrow, m_Ball, m_StringA, m_StringB;
        Item m_ItemId = (Item)255;
        bool m_WasBall;
        float m_SwingStart = -10f, m_SwingDur = 0.6f, m_ThrowStart = -10f, m_EatStart = -10f, m_UseStart = -10f, m_EquipStart = -10f, m_InjectStart = -10f;
        bool m_Hit, m_ImpactKnown;
        float m_FreezeUntil = -10f, m_FreezeE;
        Vector2 m_Sway;
        Spring m_SwayX, m_SwayY, m_LeanX, m_LeanZ, m_LandS;
        float m_CrouchK, m_SprintK, m_CarryK, m_AirVel, m_Jump, m_BobPhase, m_AimK;
        float m_BowDrawWas, m_BowRelAt = -10f, m_BowRelFrom;
        bool m_WasGrounded = true;

        // ---- for the hands autotest ----
        /// <summary>When set (>= 0), the swing / throw / eat / use animations are held at this many seconds in.</summary>
        public static float DebugSwingE = -1f, DebugThrowE = -1f, DebugEatE = -1f, DebugUseE = -1f, DebugEquipE = -1f;
        public static bool DebugAim, DebugBall;
        /// <summary>With DebugSwingE: the held swing is one that hit (it bounces back) rather than one that missed.</summary>
        public static bool DebugHit;
        /// <summary>When set (>= 0), the hands are in this team's colour whatever team you're on.</summary>
        public static int DebugTeam = -1;

        /// <summary>The newest view model (for the hands autotest).</summary>
        public static ViewModel Last;

        /// <summary>The time the animations run on: Time.time (-1, the default), or a clock of its own - the kill cam's
        /// replay (DeathReplay.cs) drives the killer's hands with the recording's time, so they slow down with it.</summary>
        public float Clock = -1f;
        float m_PrevClock = -1f, m_Dt;
        float Now => Clock >= 0f ? Clock : Time.time;
        /// <summary>The hands' root (under the camera): everything first-person is under it.</summary>
        public Transform Root => m_Root;
        /// <summary>Where the held item is on screen, in the world (the spear's throw starts here: Cfg.SpearFromHand).</summary>
        public Vector3 HeldItemPos => m_ItemHolder != null ? m_ItemHolder.position : m_Root.position;

        public Transform DebugHand(bool right) => right ? m_R : m_L;
        public Transform DebugRoot => m_Root;

        /// <summary>The renderer is part of a hand (an arm: fist, knuckles, thumb, wrist band, forearm) rather than what
        /// the hands hold (the item - even while it's carried in the right hand - the ball, an arrow, the bow's strings).
        /// Settings > Display > HANDS / TOOLS & WEAPONS: PostFx.cs puts each on its own layer.</summary>
        public bool IsHand(Renderer r)
        {
            if (r == null) return false;
            var t = r.transform;
            if (m_ItemHolder != null && t.IsChildOf(m_ItemHolder)) return false;
            return (m_R != null && t.IsChildOf(m_R)) || (m_L != null && t.IsChildOf(m_L));
        }

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
            m_R = MakeArm(m_Root, team, true, out m_AR, out m_SR);
            m_L = MakeArm(m_Root, team, false, out m_AL, out m_SL);
            m_ItemHolder = new GameObject("item").transform;
            m_ItemHolder.SetParent(m_Root, false);
            GameSettings.GraphicsChanged += OnGraphicsChanged;
            GameSettings.HandStyleChanged += OnHandStyle;
            // the hands and what's held are shaded smooth or flat as Settings > Display > SHADING says
            m_Smooth = SmoothShadeHook.Add(m_Root.gameObject, false);
        }

        readonly SmoothShadeHook m_Smooth;

        /// <summary>The PSX look of what's held comes and goes with the graphics setting: shaded smooth or flat like the rest.</summary>
        void OnGraphicsChanged() => m_Smooth?.Refresh(true);

        public void Destroy()
        {
            GameSettings.GraphicsChanged -= OnGraphicsChanged;
            GameSettings.HandStyleChanged -= OnHandStyle;
            if (m_Root) Object.Destroy(m_Root.gameObject);
        }

        public void Swing(float cooldown, float impact = ImpactTime)
        {
            m_SwingStart = Now; m_SwingDur = Mathf.Max(0.35f, cooldown); m_ImpactKnown = false; m_Hit = false;
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
            m_FreezeE = Now - m_SwingStart;
            m_FreezeUntil = Now + HitStop;
            m_SwingStart += HitStop; // the rest of the swing continues after the freeze
        }
        /// <summary>Where the held gun's muzzle is in the world (the wand's tip, the launcher's mouth): its flash goes here.</summary>
        public Vector3 Muzzle()
        {
            switch (m_ItemId)
            {
                case Item.Revolver: return m_ItemHolder.TransformPoint(new Vector3(0, 0.05f, 0.3f));
                case Item.AlienGun: return m_ItemHolder.TransformPoint(new Vector3(0, 0.045f, 0.21f));
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
            float t = Now - m_UseStart;
            if (t < 0f || t >= dur) return 0f;
            float k = t / dur;
            if (k < 0.1f) return Smooth(k / 0.1f);
            float u = (k - 0.1f) / 0.9f;
            return Mathf.Exp(-u * 4.5f) * Mathf.Cos(u * 5.2f) * (1f - u);
        }

        public void Throw() => m_ThrowStart = Now;
        public void Eat() => m_EatStart = Now;
        public void Use() => m_UseStart = Now;

        static Transform MakeArm(Transform parent, Color team, bool right, out AlienArm alien, out AlienArm smooth)
        {
            var hand = new GameObject(right ? "R" : "L").transform;
            hand.SetParent(parent, false);
            float side = right ? 1f : -1f;
            // the skin in the team's shade (or the colour picked in Settings > Display > World colours), kept up to date
            var hook = HandColorHook.Add(hand, team);
            var skin = HandColorHook.Shade(team, HandColorHook.Skin);
            var dark = HandColorHook.Shade(team, HandColorHook.Knuckles);
            var band = HandColorHook.Shade(team, HandColorHook.Band);
            // the old square hands (Settings > Display > Alien hands off brings them back)
            var square = new GameObject("square").transform;
            square.SetParent(hand, false);
            hook.Track(Art.Box(square, skin, Vector3.zero, new Vector3(0.085f, 0.09f, 0.1f)), HandColorHook.Skin);                                  // fist
            hook.Track(Art.Box(square, dark, new Vector3(0, 0.005f, 0.055f), new Vector3(0.09f, 0.075f, 0.035f)), HandColorHook.Knuckles);         // knuckles
            hook.Track(Art.Box(square, skin, new Vector3(-0.045f * side, 0.025f, 0.035f), new Vector3(0.03f, 0.03f, 0.065f), new Vector3(0, 20 * side, 0)), HandColorHook.Skin); // thumb
            hook.Track(Art.Box(square, band, new Vector3(0, 0, -0.075f), new Vector3(0.082f, 0.082f, 0.035f)), HandColorHook.Band);                // wrist band
            hook.Track(Art.Box(square, skin, new Vector3(0, 0, -0.32f), new Vector3(0.072f, 0.072f, 0.46f)), HandColorHook.Skin);                  // forearm
            // the same hands rounded off (Settings > Display > Hands: Round - ViewModel.RoundHands.cs)
            BuildRoundHand(hand, hook, skin, dark, band, side);
            // the alien arm (four fingers and a thumb, every joint its own transform: ViewModel.Hands.cs)
            alien = BuildAlienArm(hand, hook, right);
            // the smooth chunky hand: one sculpted skin on a skeleton of its own (ViewModel.Smooth.cs)
            smooth = BuildSmoothArm(hand, hook, right);
            ShowHandStyle(hand);
            foreach (var r in hand.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // (the hidden style too)
            return hand;
        }

        /// <summary>Square, round, alien or smooth hands, as Settings > Display > Hands says.</summary>
        static void ShowHandStyle(Transform hand)
        {
            if (hand == null) return;
            int style = GameSettings.HandStyle;
            var sq = hand.Find("square");
            var ro = hand.Find("round");
            var rd = hand.Find("alien");
            var sm = hand.Find("smooth");
            if (sq) sq.gameObject.SetActive(style == GameSettings.HandsSquare);
            if (ro) ro.gameObject.SetActive(style == GameSettings.HandsRound);
            if (rd) rd.gameObject.SetActive(style == GameSettings.HandsAlien);
            if (sm) sm.gameObject.SetActive(style == GameSettings.HandsSmooth);
        }

        void OnHandStyle() { ShowHandStyle(m_R); ShowHandStyle(m_L); }

        /// <summary>The hands take this team's colour (they're built in the team the player had then; a team change in the
        /// lobby recolours them).</summary>
        public void SetTeam(Color team)
        {
            if (DebugTeam >= 0) team = Cfg.TeamColor[Mathf.Clamp(DebugTeam, 0, 3)];
            if (m_R != null && m_R.TryGetComponent(out HandColorHook r)) r.SetTeam(team);
            if (m_L != null && m_L.TryGetComponent(out HandColorHook l)) l.SetTeam(team);
        }

        /// <summary>The invisible look on or off for one arm's alien skin.</summary>
        static void SetGhost(Transform arm, bool on)
        {
            var hook = arm != null ? arm.GetComponent<HandColorHook>() : null;
            if (hook != null) hook.Ghost = on;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        /// <summary>How far every resting hold sits up in the view with the alien arms.</summary>
        public static readonly Vector3 k_AlienHandsUp = new Vector3(-0.01f, 0.07f, 0f);

        public void Update(State s)
        {
            m_Dt = Clock >= 0f ? (m_PrevClock >= 0f ? Mathf.Clamp(Clock - m_PrevClock, 0f, 0.1f) : 0f) : Time.deltaTime;
            m_PrevClock = Clock;
            float dt = m_Dt;
            bool vis = s.Visible && s.Visible2; // hidden while looking through the sniper scope
            if (m_Root.gameObject.activeSelf != vis) m_Root.gameObject.SetActive(vis);
            if (!vis) return;
            SetGhost(m_R, s.Ghost);
            SetGhost(m_L, s.Ghost);
            if (DebugAim) s.Aim = true;
            if (DebugBall) s.Ball = true;
            if (DebugSwingE >= 0f) { m_SwingStart = Now - DebugSwingE; m_FreezeUntil = -10f; m_ImpactKnown = DebugHit; m_Hit = DebugHit; }
            if (DebugThrowE >= 0f) m_ThrowStart = Now - DebugThrowE;
            if (DebugEatE >= 0f) m_EatStart = Now - DebugEatE;
            if (DebugUseE >= 0f) m_UseStart = Now - DebugUseE;
            if (DebugEquipE >= 0f) m_EquipStart = Now - DebugEquipE;

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
                m_EquipStart = Now + (m_WasBall && !s.Ball ? 0.3f : 0f);
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
            // looking round: the hands lag behind the turn and swing a little past as they catch up
            var swayTo = Vector2.ClampMagnitude(-s.Look * 0.0035f, 0.05f);
            m_SwayX.Step(swayTo.x, 13f, 0.55f, dt);
            m_SwayY.Step(swayTo.y, 13f, 0.55f, dt);
            m_Sway = Vector2.ClampMagnitude(new Vector2(m_SwayX.X, m_SwayY.X), 0.06f);
            m_CrouchK = Mathf.MoveTowards(m_CrouchK, s.Crouch ? 1f : 0f, dt * 5f);

            float swingE = Now < m_FreezeUntil ? m_FreezeE : Now - m_SwingStart;
            bool swinging = swingE >= 0 && swingE < SwingEnd;
            bool busy = swinging || s.Draw > 0f || s.RamCharge > 0f || s.Firing || s.SpearAim || s.Aim || s.Reload >= 0f;

            // sprint: lower the item and tilt it in (not while attacking)
            m_SprintK = Mathf.MoveTowards(m_SprintK, s.Sprint && !busy ? 1f : 0f, dt * 6f);
            // the spear is always carried the way it is when sprinting (lowered and tilted in), walking or standing - only
            // the bigger bob and the free hand pumping are the sprint's own
            m_CarryK = Mathf.MoveTowards(m_CarryK, 0f, dt * 6f); // (Rust's spear is carried in one hand, pointing up and away - not lowered)
            float pose = Mathf.Max(Smooth(m_SprintK), m_CarryK);
            // moving: the hands lean into a strafe and trail a little behind a walk forwards or back
            m_LeanX.Step(Mathf.Clamp(s.Move.x, -7f, 7f), 9f, 0.75f, dt);
            m_LeanZ.Step(Mathf.Clamp(s.Move.y, -7f, 7f), 9f, 0.8f, dt);
            // landing: the hands dip (deeper the harder you land) and spring back up; in the air they lag the jump and the fall
            if (s.Grounded && !m_WasGrounded && m_AirVel < -3f) m_LandS.V -= Mathf.Clamp(-m_AirVel, 0f, 18f) * 0.085f;
            if (!s.Grounded) m_AirVel = s.VelY;
            m_WasGrounded = s.Grounded;
            m_LandS.Step(0f, 12f, 0.42f, dt);
            float landDip = -m_LandS.X;
            m_Jump = Mathf.Lerp(m_Jump, s.Grounded ? 0f : Mathf.Clamp(-s.VelY * 0.004f, -0.035f, 0.045f), dt * 8f);

            // figure-8 walk bob (a sway from side to side, a bounce at each step, a little to and fro), bigger and with a
            // roll when sprinting; standing still: slow breathing and a drift that never quite repeats
            float bobAmt = Mathf.Clamp01(s.Speed / 5f) * (s.Grounded ? 1f : 0.2f);
            m_BobPhase = s.Bob * 1.6f;
            float bx = Mathf.Sin(m_BobPhase), by = Mathf.Abs(Mathf.Cos(m_BobPhase));
            Vector3 bob = new Vector3(bx * 0.013f * (1f + m_SprintK), (by - 0.6f) * 0.017f * (1f + m_SprintK * 1.3f), Mathf.Cos(m_BobPhase * 2f) * 0.004f) * bobAmt;
            float still = 1f - bobAmt;
            float breathe = (Mathf.Sin(Now * 1.6f) * 0.0028f + Mathf.Sin(Now * 0.63f + 1.3f) * 0.0016f) * still;
            float drift = Mathf.Sin(Now * 0.51f + 0.4f) * 0.0018f * still;
            float idleRoll = (Mathf.Sin(Now * 0.9f) * 0.7f + Mathf.Sin(Now * 0.37f) * 0.5f) * still, idlePitch = Mathf.Sin(Now * 1.6f + 0.6f) * 0.55f * still;
            // switching to an item: it comes up fast from below and out to the right, tipped back and rolled over, slows as
            // it arrives, and its own weight carries it a touch too far before it settles (wob)
            float eq = Now - m_EquipStart;
            float away = 1f - EaseOut(eq / 0.3f);
            float wob = eq > 0f && eq < 0.9f ? Mathf.Exp(-eq * 8.5f) * Mathf.Sin(eq * 17f) : 0f;
            // the alien arms up in the view (the whole hand and wrist show, not just fingers behind the hotbar) - not while
            // aiming down the sights, drawing the bow or winding up the spear, which line up on the crosshair
            bool gunHeld = s.Item == Item.Crossbow || s.Item == Item.Sniper || s.Item == Item.Pistol || s.Item == Item.Revolver || s.Item == Item.AlienGun
                || s.Item == Item.Shotgun || s.Item == Item.PortalGun || s.Item == Item.RocketLauncher;
            float framed = !GameSettings.FingerHands ? 0f
                : s.Ball ? 1f : 1f - Mathf.Clamp01(Mathf.Max(gunHeld ? m_AimK : 0f, Mathf.Max(s.Item == Item.Bow ? s.Draw : 0f, s.Item == Item.Spear ? m_SpearAimK : 0f)));
            Vector3 shared = bob + new Vector3(m_Sway.x + pose * 0.03f - m_LeanX.X * 0.0035f + drift,
                    m_Sway.y - m_CrouchK * 0.02f + breathe + m_Jump - landDip - pose * 0.07f + wob * 0.012f,
                    -pose * 0.04f - m_LeanZ.X * 0.0028f)
                + new Vector3(0.1f, -0.46f, -0.1f) * away + k_AlienHandsUp * framed;
            Quaternion sharedRot = Quaternion.Euler(m_Sway.y * 300f + pose * 14f + landDip * 70f + idlePitch + 55f * away - wob * 7f,
                -m_Sway.x * 300f - pose * 22f - 18f * away,
                bx * bobAmt * (1.5f + 3f * m_SprintK) + pose * 12f + idleRoll - 40f * away - m_LeanX.X * 1.1f + wob * 4f);

            if (DebugShow >= 0) { PoseShow(); UpdateAlienHands(s, false, 0f); return; }
            if (s.Ball) { PoseBall(shared, sharedRot); UpdateAlienHands(s, swinging, swingE); return; }
            if (m_Item && !m_Item.activeSelf && s.Item != Item.Spear) m_Item.SetActive(true);

            switch (s.Item)
            {
                case Item.Rock: case Item.GoldenRock: PoseRock(shared, sharedRot, swinging, swingE); break;
                case Item.Sword: PoseSword(shared, sharedRot, swinging, swingE); break;
                case Item.Lightsaber: PoseSword(shared, sharedRot, swinging, swingE, 0f); break; // (swung like the sword, held by its hilt)
                case Item.Hammer: PoseHammer(shared, sharedRot, swinging, swingE); break;
                case Item.Hatchet:
                case Item.BetterHatchet:
                case Item.KnockStick:
                case Item.KnockStick1:
                case Item.TreeBreaker:
                case Item.TreeCracker:
                case Item.Pickaxe: PoseTool(shared, sharedRot, swinging, swingE); break;
                case Item.Spear: PoseSpear(s, shared, sharedRot, swinging, swingE); break;
                case Item.Bow: PoseBow(s, shared, sharedRot); break;
                case Item.Ram: case Item.HeavyRam: PoseRam(s, shared, sharedRot); break;
                case Item.Chainsaw: PoseChainsaw(s, shared, sharedRot); break;
                case Item.Crossbow:
                case Item.Sniper:
                case Item.Pistol:
                case Item.Revolver:
                case Item.AlienGun:
                case Item.Shotgun:
                case Item.PortalGun:
                case Item.RocketLauncher: PoseCrossbow(s, shared, sharedRot); break;
                case Item.MedPen: PoseMedPen(shared, sharedRot); break; // (jabbed into the left forearm: MedPen.cs)
                case Item.None: HideLeft(); Set(m_R, new Vector3(0.3f, -0.9f, 0.2f), Quaternion.identity); break;
                default: PoseHeld(s.Item, shared, sharedRot); break;
            }
            UpdateAlienHands(s, swinging, swingE);
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

        // ======== swings ========

        /// <summary>When a swing is over (seconds in) and the hands are back at rest.</summary>
        float SwingEnd => Mathf.Max(0.45f, m_SwingDur * 0.9f);

        /// <summary>
        /// A swing, e seconds in. The hit lands at m_Down whatever the weapon, so everything is timed round that:
        ///  - the draw back (the first drawShare of it): quick, slowing at its top;
        ///  - the strike: from the top round by `via` to `hit`, faster and faster, arriving exactly as the hit lands;
        ///  - it hit: (after the hit-stop) the tool bounces back off what it struck to `recoil`, then swings home;
        ///  - it missed: it carries on through to `follow`, slowing down, and comes home round by `viaBack`;
        ///  - home: eased, a touch past rest and back (InOutBack).
        /// </summary>
        Key SwingKey(Key idle, Key back, Vector3 via, Key hit, Key recoil, Key follow, Vector3 viaBack, bool swinging, float e, float drawShare = 0.4f, float followTime = 0.11f)
        {
            if (!swinging) return idle;
            float D = m_Down, A = D * drawShare, end = SwingEnd;
            if (e < A) return Mix(idle, back, EaseOut(e / A));
            if (e < D) return Arc(back, hit, via, EaseIn((e - A) / (D - A)));
            if (m_ImpactKnown && m_Hit)
            {
                const float r = 0.1f;
                if (e < D + r) return Mix(hit, recoil, EaseOut((e - D) / r));
                return Mix(recoil, idle, Settle((e - D - r) / Mathf.Max(0.1f, end - D - r)));
            }
            float f = Mathf.Min(followTime, (end - D) * 0.45f);
            if (e < D + f) return Mix(hit, follow, EaseOut((e - D) / f));
            return Arc(follow, idle, viaBack, Settle((e - D - f) / Mathf.Max(0.1f, end - D - f)));
        }

        // ---- the rock: cupped in both hands low in the middle; lifted up in front of your face and smashed down and out
        // at the crosshair (a hit knocks it back up; a miss carries it on down, still in view)
        public static readonly Key RockIdle = new Key(0.02f, -0.19f, 0.44f, -6f, -4f, 0f), RockBack = new Key(0.02f, -0.03f, 0.4f, -40f, -4f, 0f),
            RockHit = new Key(0.02f, -0.19f, 0.7f, 30f, -4f, 0f), RockRecoil = new Key(0.02f, -0.13f, 0.57f, -8f, -4f, 0f), RockFollow = new Key(0.02f, -0.27f, 0.68f, 46f, -4f, 0f);
        static readonly Vector3 k_RockVia = new Vector3(0.02f, 0.07f, 0.62f), k_RockViaBack = new Vector3(0.02f, -0.26f, 0.5f);

        void PoseRock(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            var k = SwingKey(RockIdle, RockBack, k_RockVia, RockHit, RockRecoil, RockFollow, k_RockViaBack, swinging, e);
            var rot = sharedRot * k.Q;
            AttachItemToRoot(shared + k.Pos, rot, 1f);
            if (GameSettings.HandStyle == GameSettings.HandsRound)
            {
                // the round hands: up the rock's sides, the knuckles curling up over it, palms in (Rust's)
                foreach (var (arm, sx) in new[] { (m_R, 1f), (m_L, -1f) })
                {
                    arm.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.088f * sx, -0.035f, -0.015f)));
                    var up = m_ItemHolder.TransformDirection(new Vector3(-0.32f * sx, 1f, 0.22f));
                    var outward = m_ItemHolder.TransformDirection(new Vector3(sx, 0.25f, 0f));
                    arm.localRotation = Quaternion.Inverse(m_Root.rotation) * Quaternion.LookRotation(up, outward);
                }
                return;
            }
            // a hand under either side of it, palms up
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.09f, -0.07f, -0.02f)));
            m_R.localRotation = rot * Quaternion.Euler(-30f, -20f, -40f);
            m_L.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(-0.09f, -0.07f, -0.02f)));
            m_L.localRotation = rot * Quaternion.Euler(-30f, 20f, 40f);
        }

        // ---- tools (hatchet, pickaxe...): held on the right, the fist turned in towards the middle so the back of the
        // hand and the knuckles show, the tool leaning in and a little forward. The chop: tipped back up by the right
        // shoulder, then down and in across the view - the head lands just under the crosshair
        public static readonly Key ToolIdle = new Key(0.24f, -0.32f, 0.52f, -6f, -30f, 8f, 22f), ToolBack = new Key(0.32f, -0.17f, 0.44f, -30f, -22f, -8f, -18f),
            ToolHit = new Key(0.13f, -0.27f, 0.64f, 24f, -34f, 10f, 48f), ToolRecoil = new Key(0.18f, -0.24f, 0.57f, 4f, -30f, 6f, 26f),
            ToolFollow = new Key(0.07f, -0.32f, 0.63f, 34f, -42f, 18f, 60f);
        static readonly Vector3 k_ToolVia = new Vector3(0.27f, -0.07f, 0.6f), k_ToolViaBack = new Vector3(0.2f, -0.36f, 0.52f);

        /// <summary>Hatchet / pickaxe: one hand; the tool pivots in the fist as well as the arm swinging it.</summary>
        void PoseTool(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            var k = SwingKey(ToolIdle, ToolBack, k_ToolVia, ToolHit, ToolRecoil, ToolFollow, k_ToolViaBack, swinging, e);
            Set(m_R, shared + k.Pos, sharedRot * k.Q);
            AttachItemToRight(new Vector3(0, -0.06f, 0.01f), new Vector3(k.Item, 0, 0));
            HideLeft();
        }

        /// <summary>The hammer: not a swing at all - a small, gentle tap forward, like fixing something (a lift of a few
        /// centimetres and a light knock), then back.</summary>
        void PoseHammer(Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            var idle = ToolIdle.Off(Vector3.zero, Vector3.zero, 14f);
            var back = idle.Off(new Vector3(0.012f, 0.04f, -0.035f), new Vector3(-15f, 2f, -2f), 4f);
            var tap = idle.Off(new Vector3(-0.015f, -0.012f, 0.075f), new Vector3(9f, -3f, 2f), 36f);
            var off = idle.Off(new Vector3(-0.005f, 0.004f, 0.03f), new Vector3(0f, -1f, 0f), 20f);
            var k = SwingKey(idle, back, Vector3.Lerp(back.Pos, tap.Pos, 0.5f) + new Vector3(0f, 0.015f, 0f), tap, off, tap.Off(new Vector3(0f, -0.006f, 0.01f), new Vector3(2f, 0f, 0f), 40f),
                Vector3.Lerp(tap.Pos, idle.Pos, 0.5f), swinging, e, 0.45f, 0.08f);
            Set(m_R, shared + k.Pos, sharedRot * k.Q);
            AttachItemToRight(new Vector3(0, -0.06f, 0.01f), new Vector3(k.Item, 0, 0));
            HideLeft();
        }

        /// <summary>A moment of the sword's slash: the hand's place, which way the blade points, which way its edge faces.</summary>
        struct Cut
        {
            public Vector3 Pos, Dir, Edge;
            public Cut(Vector3 pos, Vector3 dir, Vector3 edge) { Pos = pos; Dir = dir; Edge = edge; }
            /// <summary>(the sword's built along +Y, its flat faces across X and its edges along Z)</summary>
            public Quaternion Q
            {
                get
                {
                    var d = Dir.normalized;
                    var ed = Edge - d * Vector3.Dot(Edge, d);
                    if (ed.sqrMagnitude < 1e-6f) ed = Vector3.Cross(d, Vector3.right);
                    return Quaternion.LookRotation(ed.normalized, d);
                }
            }
        }

        // (rest: on the right, the blade up, leaning in and forward, turned so its flat shows; drawn back: laid over the
        // right shoulder; the hit: flat across the view, the point at the crosshair; through: out to the left)
        public static readonly Vector3 SwordIdlePos = new Vector3(0.25f, -0.34f, 0.5f);
        static readonly Cut k_SwIdle = new Cut(SwordIdlePos, new Vector3(-0.28f, 0.88f, 0.38f), new Vector3(-0.6f, -0.1f, 0.8f)),
            k_SwBack = new Cut(new Vector3(0.36f, -0.22f, 0.44f), new Vector3(0.5f, 0.82f, -0.28f), new Vector3(-0.5f, -0.1f, 0.85f)),
            k_SwHit = new Cut(new Vector3(0.1f, -0.27f, 0.6f), new Vector3(-0.26f, 0.2f, 0.94f), new Vector3(-1f, 0f, -0.25f)),
            k_SwRecoil = new Cut(new Vector3(0.17f, -0.27f, 0.57f), new Vector3(0.05f, 0.3f, 0.95f), new Vector3(-1f, 0f, 0.05f)),
            k_SwFollow = new Cut(new Vector3(-0.13f, -0.25f, 0.54f), new Vector3(-0.95f, 0.12f, 0.28f), new Vector3(-0.3f, -0.1f, -0.95f));
        static readonly Vector3 k_SwVia = new Vector3(0.36f, -0.2f, 0.68f), k_SwViaBack = new Vector3(0.06f, -0.38f, 0.46f);

        /// <summary>
        /// The sword: a slash from the side, and a slow, heavy one (its hit lands half a second in). It's drawn back out
        /// to the right and laid over the shoulder, then swept flat across in a long arc - the blade crossing the
        /// crosshair as it lands - and on through to the left on a miss (a hit knocks it back). Most of the time before
        /// the hit is the draw back and the arc itself; the way home afterwards is slower still.
        /// </summary>
        void PoseSword(Vector3 shared, Quaternion sharedRot, bool swinging, float e, float gripY = 0.06f)
        {
            Vector3 pos = k_SwIdle.Pos;
            Quaternion q = k_SwIdle.Q;
            if (swinging)
            {
                float D = m_Down, A = D * 0.48f, end = SwingEnd;
                if (e < A) { float t = Smooth(e / A); pos = Vector3.Lerp(k_SwIdle.Pos, k_SwBack.Pos, t); q = Quaternion.Slerp(k_SwIdle.Q, k_SwBack.Q, t); }
                else if (e < D) { float t = EaseIn((e - A) / (D - A)); pos = Curve(k_SwBack.Pos, k_SwVia, k_SwHit.Pos, t); q = Quaternion.Slerp(k_SwBack.Q, k_SwHit.Q, t); }
                else if (m_ImpactKnown && m_Hit)
                {
                    const float r = 0.12f;
                    if (e < D + r) { float t = EaseOut((e - D) / r); pos = Vector3.Lerp(k_SwHit.Pos, k_SwRecoil.Pos, t); q = Quaternion.Slerp(k_SwHit.Q, k_SwRecoil.Q, t); }
                    else { float t = Settle((e - D - r) / Mathf.Max(0.1f, end - D - r)); pos = Vector3.LerpUnclamped(k_SwRecoil.Pos, k_SwIdle.Pos, t); q = Quaternion.SlerpUnclamped(k_SwRecoil.Q, k_SwIdle.Q, t); }
                }
                else
                {
                    float f = Mathf.Min(0.2f, (end - D) * 0.45f);
                    if (e < D + f) { float t = EaseOut((e - D) / f); pos = Vector3.Lerp(k_SwHit.Pos, k_SwFollow.Pos, t); q = Quaternion.Slerp(k_SwHit.Q, k_SwFollow.Q, t); }
                    else { float t = Settle((e - D - f) / Mathf.Max(0.1f, end - D - f)); pos = Curve(k_SwFollow.Pos, k_SwViaBack, k_SwIdle.Pos, t); q = Quaternion.SlerpUnclamped(k_SwFollow.Q, k_SwIdle.Q, t); }
                }
            }
            var blade = sharedRot * q;
            Set(m_R, shared + pos, blade);
            // the grip (0.06 up the sword's hilt; the lightsaber's at its middle) stays in the fist whichever way the blade points
            AttachItemToRoot(m_R.localPosition - blade * new Vector3(0f, gripY, 0f), blade);
            HideLeft();
        }

        /// <summary>
        /// The spear: carried low like the sprint pose; RMB raises it over the shoulder (blended in over a moment, not
        /// snapped), the throw drives it forward and lets go, and the next spear (if you have one) comes back up from below
        /// while the throw recovers. Letting go of RMB lowers it back down the same way.
        /// </summary>
        void PoseSpear(State s, Vector3 shared, Quaternion sharedRot, bool swinging, float e)
        {
            float throwK = Mathf.Clamp01((Now - m_ThrowStart) / 0.25f);
            bool throwing = throwK < 1f;
            float dt = m_Dt;
            if (throwing) m_SpearAimK = 1f;
            else if (Now - m_ThrowStart < 0.3f) m_SpearAimK = 0f; // (just thrown: the next one comes up from below)
            else m_SpearAimK = Mathf.MoveTowards(m_SpearAimK, s.SpearAim ? 1f : 0f, dt / (s.SpearAim ? 0.2f : 0.16f));
            float k = Smooth(m_SpearAimK);
            if (k >= 0.999f || throwing) { PoseSpearAim(s, shared, sharedRot, throwK, throwing); return; }
            // after a throw the next spear rises back into the hands
            float back = EaseOut((Now - m_ThrowStart - 0.25f) / 0.32f);
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

        /// <summary>How far ahead (m, in the view) a poke's spear point and a slash's blade meet the crosshair.</summary>
        const float SpearPokeAimDepth = 2.2f;

        /// <summary>Turns an item (it points along its own +Y) held at `from` (view space) so it points at the crosshair
        /// `depth` ahead - all of the way at w = 1.</summary>
        static Quaternion AimAtCrosshair(Quaternion rot, Vector3 from, float depth, float w)
        {
            var cur = rot * Vector3.up;
            var want = new Vector3(0f, 0f, depth) - from;
            if (want.sqrMagnitude < 1e-4f) return rot;
            return Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, want.normalized), Mathf.Clamp01(w)) * rot;
        }

        void PoseSpearAim(State s, Vector3 shared, Quaternion sharedRot, float throwK, bool throwing)
        {
            // the spear lives on the rig; both hands grab the shaft with natural wrist angles
            float d = throwing ? 0f : s.Draw;
            Vector3 pos = new Vector3(0.3f, 0.07f, 0.24f - d * 0.2f); // (over the shoulder, the shaft in view at the top right)
            if (throwing) pos = Vector3.Lerp(pos, new Vector3(0.15f, -0.02f, 0.8f), EaseIn(throwK));
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
            // the poke: a short pull back, a drive forward that's at full reach exactly as the hit lands; a hit stops it
            // dead and it comes back fast, a miss carries it a little further before it's drawn back
            float thrust = 0;
            if (swinging)
            {
                float D = ImpactTime, pull = D * 0.34f, end = SwingEnd;
                if (e < pull) thrust = -0.2f * EaseOut(e / pull);
                else if (e < D) thrust = Mathf.LerpUnclamped(-0.2f, 1f, EaseIn((e - pull) / (D - pull)));
                else if (m_ImpactKnown && m_Hit)
                {
                    float u = e - D;
                    thrust = u < 0.09f ? Mathf.Lerp(1f, 0.4f, EaseOut(u / 0.09f)) : 0.4f * (1f - InOutBack((u - 0.09f) / Mathf.Max(0.15f, end - D - 0.09f)));
                }
                else
                {
                    float u = e - D;
                    thrust = u < 0.1f ? Mathf.Lerp(1f, 1.13f, EaseOut(u / 0.1f)) : 1.13f * (1f - InOutBack((u - 0.1f) / Mathf.Max(0.15f, end - D - 0.1f)));
                }
            }
            var spearRot = sharedRot * Quaternion.Euler(48f, -6f, 4f);
            // the poke drives the spear in towards the crosshair (its point lands on the middle of the screen), not
            // straight out past the right of it
            var carry = new Vector3(0.3f, -0.32f, 0.36f);
            var toMiddle = (new Vector3(0f, 0f, SpearPokeAimDepth) - carry).normalized;
            float aim = Mathf.Clamp01(thrust * 1.6f);
            if (aim > 0f) spearRot = AimAtCrosshair(spearRot, carry, SpearPokeAimDepth, aim);
            AttachItemToRoot(shared + carry + toMiddle * (thrust * 0.42f), spearRot);
            m_R.localPosition = m_Root.InverseTransformPoint(m_ItemHolder.TransformPoint(new Vector3(0.03f, 0.2f, 0)));
            m_R.localRotation = sharedRot * Quaternion.Euler(-25f, -18f, -10f);
            HideLeft(); // (one hand, like Rust's)
        }

        /// <summary>
        /// Rust hunting bow (copied from Rust footage): the LEFT arm comes in from the lower left and holds the bow right of
        /// centre, tilted clockwise, with the arrow nocked and the right hand on the string at the lower right. Drawing
        /// brings the grip in to just below/right of the crosshair, the string hand back to your face, and the arrow
        /// lines up to point straight at the crosshair.
        /// </summary>
        void PoseBow(State s, Vector3 shared, Quaternion sharedRot)
        {
            // letting go: the string's back in a blink (and the hand off it), but the bow itself jumps forward off the
            // shot and swings back down to rest over a moment - it used to snap from drawn to rest in one frame
            if (m_BowDrawWas > 0.2f && s.Draw <= 0.001f) { m_BowRelAt = Now; m_BowRelFrom = m_BowDrawWas; }
            m_BowDrawWas = s.Draw;
            float rel = Now - m_BowRelAt;
            bool let = rel >= 0f && rel < 0.6f;
            float e = Smooth(Mathf.Max(s.Draw, let ? m_BowRelFrom * (1f - Smooth(rel / 0.28f)) : 0f));    // (the bow)
            float es = Smooth(Mathf.Max(s.Draw, let ? m_BowRelFrom * (1f - EaseOut(rel / 0.05f)) : 0f));  // (the string, the arrow, the hand on it)
            float kick = let ? Mathf.Exp(-rel * 11f) * Mathf.Cos(rel * 26f) * m_BowRelFrom : 0f;
            if (m_Item && m_Item.transform.childCount > 3) m_Item.transform.GetChild(3).gameObject.SetActive(false); // we draw the string ourselves
            // (the bow hand is held well out in front - it used to sit 0.1 nearer, cramped against the view)
            // (at rest, like Rust's: held low, the bow laid well over with its top limb out to the left; drawing stands it
            // up in front of you)
            var grip = shared + Vector3.Lerp(new Vector3(0.12f, -0.26f, 0.62f), new Vector3(0.045f, -0.12f, 0.69f), e) + new Vector3(0f, 0.006f, 0.04f) * kick;
            var bowRot = sharedRot * Quaternion.Euler(Mathf.Lerp(-4f, 0f, e) - 6f * kick, Mathf.Lerp(-14f, -4f, e), Mathf.Lerp(52f, -14f, e) + 5f * kick);
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
            var tip = rest + dir * Mathf.Lerp(0.4f, 0.25f, es); // (far enough through the rest that the fletching stays well out in front of your eye, never a slab across the view)
            const float arrowLen = 0.72f;
            var nock = tip - dir * arrowLen;
            if (m_Arrow)
            {
                // the arrow model runs along +Y from -0.15 (fletching) to 0.6 (tip)
                m_Arrow.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0, 0);
                m_Arrow.transform.localPosition = tip - dir * 0.6f;
                // drawn, the fletching's back by your eye: out of the view (like Rust's), not a slab across it
                for (int c = 2; c < m_Arrow.transform.childCount && c < 4; c++) { var f = m_Arrow.transform.GetChild(c).gameObject; if (f.activeSelf != es < 0.3f) f.SetActive(es < 0.3f); }
            }

            // string hand holds the nock at the lower right; drawn, it's back by your face (out of view)
            var stringRest = grip + bowRot * new Vector3(0, 0, -0.03f);
            var pull = Vector3.Lerp(m_Arrow ? nock : stringRest, grip + new Vector3(0.015f, -0.015f, -0.47f), es);
            pull.z = Mathf.Max(pull.z, 0.12f); // keep the string in front of the camera
            // (for the alien hand's fingers hooked on the string)
            m_BowString = Vector3.Lerp(stringRest, pull, Mathf.Max(es, 0.05f));
            m_BowString.z = Mathf.Max(m_BowString.z, 0.12f);
            m_BowUp = bowRot * Vector3.up;
            if (m_Item) m_BowLow = m_Root.InverseTransformPoint(m_Item.transform.TransformPoint(new Vector3(0, -0.375f, -0.02f)));
            m_BowDir = dir;
            m_R.localPosition = Vector3.Lerp(pull + new Vector3(0.015f, -0.03f, -0.03f), new Vector3(0.3f, -0.5f, -0.3f), es);
            m_R.localRotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-10f, 0, -80f);

            // the string runs from both limb tips to the nock
            if (m_StringA && m_Item)
            {
                var top = m_Root.InverseTransformPoint(m_Item.transform.TransformPoint(new Vector3(0, 0.375f, -0.02f)));
                var bottom = m_Root.InverseTransformPoint(m_Item.transform.TransformPoint(new Vector3(0, -0.375f, -0.02f)));
                var mid = Vector3.Lerp(stringRest, pull, Mathf.Max(es, 0.05f));
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
            float aim = m_AimK = Mathf.MoveTowards(m_AimK, s.Aim ? 1f : 0f, m_Dt * 7f);
            // the shot's kick, each gun its own: the revolver snaps up at the wrist, the shotgun shoves back hard into the
            // shoulder and bucks, the rocket launcher lurches back and takes a while to settle, the portal gun gives a
            // soft pulse (the rest: the short knock back they always had)
            var kickPos = new Vector3(0, 0.02f, -0.07f);
            var kickRot = new Vector3(-8f, 0, 0);
            float kick;
            switch (s.Item)
            {
                case Item.Revolver: case Item.AlienGun: kick = Recoil(0.42f); kickPos = new Vector3(0, 0.045f, -0.06f); kickRot = new Vector3(-30f, 2f, -5f); break;
                case Item.Shotgun: kick = Recoil(0.55f); kickPos = new Vector3(0.01f, 0.035f, -0.17f); kickRot = new Vector3(-15f, -2f, 4f); break;
                case Item.RocketLauncher: kick = Recoil(0.8f); kickPos = new Vector3(0, -0.02f, -0.2f); kickRot = new Vector3(-7f, 3f, -7f); break;
                case Item.PortalGun: kick = Recoil(0.4f); kickPos = new Vector3(0, 0.01f, -0.08f); kickRot = new Vector3(-5f, 0, 0); break;
                case Item.Pistol: kick = Recoil(0.32f); kickPos = new Vector3(0, 0.03f, -0.05f); kickRot = new Vector3(-17f, 1f, -3f); break;
                case Item.Sniper: kick = Recoil(0.6f); kickPos = new Vector3(0, 0.03f, -0.14f); kickRot = new Vector3(-11f, 1f, 2f); break;
                default: kick = Recoil(0.36f); kickPos = new Vector3(0, 0.015f, -0.08f); kickRot = new Vector3(-7f, 0f, 0f); break; // (the crossbow)
            }
            kick *= 1f - 0.35f * aim; // (steadier down the sights)
            // the sights at the eye: the revolver's sit higher above its grip than the crossbow's
            var aimed = s.Item == Item.Revolver ? new Vector3(0f, -0.104f, 0.38f) : new Vector3(0f, -0.08f, 0.52f); // (the long guns held out far enough that their stocks don't fill the view) // (the revolver: the eye just over its notch, the drum and barrel low under the line)
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
            float strikeK = Mathf.Clamp01((Now - m_UseStart) / 0.45f);
            float thrust = strikeK < 1f ? (strikeK < 0.3f ? EaseIn(strikeK / 0.3f) : 1f - Settle((strikeK - 0.3f) / 0.7f)) : 0f;
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
            float throwK = Mathf.Clamp01((Now - m_ThrowStart) / 0.3f);
            bool throwing = throwK < 1f;
            var ballPos = shared + new Vector3(0, -0.62f, 0.75f);
            if (throwing) ballPos += new Vector3(0, 0.1f, 1.4f) * Smooth(throwK); // (pushed away from you, not up into your face)
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
            // eating: up to the mouth, a bite (a quick nod at it), back down
            float eatK = Mathf.Clamp01((Now - m_EatStart) / 0.6f);
            float toMouth = eatK < 1f ? Smooth(eatK / 0.3f) * (1f - Smooth((eatK - 0.6f) / 0.4f)) : 0f;
            float bite = eatK < 1f ? Bump(eatK, 0.26f, 0.52f) : 0f;
            // using it: a quick push out, a slower way back
            float useK = Mathf.Clamp01((Now - m_UseStart) / 0.3f);
            float use = useK < 1f ? (useK < 0.3f ? EaseOut(useK / 0.3f) : 1f - Smooth((useK - 0.3f) / 0.7f)) : 0f;
            if (item == Item.DeathWand) use = Recoil(0.5f) * 1.5f; // the cast: flicked out hard, springing back
            // (small things in the right hand at the lower right, a little in from the edge, like Rust's; C4 in both hands,
            // low in the middle, like Rust's explosive charge)
            Vector3 idle = item == Item.C4 ? new Vector3(0.05f, -0.27f, 0.44f) : new Vector3(0.21f, -0.27f, 0.45f);
            // a box (the chest, and the workbenches held like it): the hand under it further forward and a touch lower
            if (item == Item.Chest || item == Item.Workbench || item == Item.Workbench2) idle += new Vector3(0.05f, -0.03f, 0.09f);
            Vector3 pos = Vector3.Lerp(idle, new Vector3(0.05f, -0.13f, 0.23f), toMouth) + new Vector3(0f, -0.012f, -0.02f) * bite + new Vector3(0, -0.05f, 0.12f) * use;
            // throwing (Rust's): drawn back up by the shoulder, flung out forwards - the thing leaves the hand as the arm
            // comes over - and the arm follows through down in front before it comes back
            float th = Now - m_ThrowStart;
            float throwK = Mathf.Clamp01(th / 0.35f); // (the thing itself: gone 0.14 s in, the next one in the hand at 0.35)
            float pitch = 0f, yaw = 0f;
            if (th >= 0f && th < 0.55f)
            {
                float back = Smooth(th / 0.11f) * (1f - Smooth((th - 0.11f) / 0.07f));
                float fling = th < 0.11f ? 0f : EaseOut((th - 0.11f) / 0.1f) * (1f - Smooth((th - 0.24f) / 0.31f));
                pos += new Vector3(0.1f, 0.13f, -0.15f) * back + new Vector3(-0.1f, 0.03f, 0.4f) * fling;
                pitch = -32f * back + 42f * fling;
                yaw = 8f * back - 10f * fling;
            }
            Set(m_R, shared + pos, sharedRot * Quaternion.Euler(-12f - toMouth * 22f - bite * 9f - use * 25f + pitch, -14f + yaw, -4f));
            if (m_Item) m_Item.SetActive(throwK >= 1f || throwK < 0.4f);
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
                case Item.Barricade: AttachItemToRight(new Vector3(0, 0.03f, 0.07f), new Vector3(0, 20, 0), 0.7f); break;
                case Item.Arrow: AttachItemToRight(new Vector3(0, -0.1f, 0.02f), new Vector3(-30, 0, 0), 0.8f); break;
                case Item.Emerald: AttachItemToRight(new Vector3(0, 0.03f, 0.06f), new Vector3(-15, 0, 0), 0.45f); break; // (a gem, held up small in the fingers - not a boulder filling the view)
                default: AttachItemToRight(new Vector3(0, 0.05f, 0.07f), Vector3.zero, 1f); break;
            }
            HideLeft();
        }
    }
}
