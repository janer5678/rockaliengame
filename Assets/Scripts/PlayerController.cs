using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace RockGame
{
    /// <summary>Local (owner-only) input, first-person camera, movement, item use, interaction and placement ghosts.</summary>
    public partial class PlayerController : NetworkBehaviour
    {
        public static PlayerController Local;

        public bool MenuOpen, Paused;
        /// <summary>Crafting is available anywhere inside your own base.</summary>
        public bool CraftOpen => m_Net != null && m_Net.CanCraftHere;
        /// <summary>Eating berries: 0..1 while it's being eaten.</summary>
        public float EatProgress => m_EatStart >= 0 ? Mathf.Clamp01((Time.time - m_EatStart) / Mathf.Max(0.1f, EatTime)) : 0f;
        /// <summary>Berries are quick, horse meat takes a while (it heals you fully).</summary>
        float EatTime => m_Net != null && m_Net.HeldItem == Item.Meat ? Cfg.MeatEatTime : Cfg.BerryEatTime;
        float m_EatStart = -1f, m_Third;
        static float Smooth01(float t) => t * t * (3f - 2f * t);
        /// <summary>The camera is behind you (tree camo).</summary>
        public bool ThirdPerson => m_Third > 0.5f;
        int m_EatBites;
        public Container LootTarget;
        /// <summary>The menu is the UPGRADES screen (E on your own upgrade station) instead of crafting - Hud.Upgrades.cs.</summary>
        public bool UpgradesOpen;
        /// <summary>What you're looking at (E uses it), as of the last frame.</summary>
        public Interactable Target;
        public PieceType BuildPiece = PieceType.Foundation;
        /// <summary>Building plan: the Rust-style wheel (hold RMB) is open, and demolish mode picked on it.</summary>
        public bool WheelOpen, DemolishMode, UpgradeMode;
        /// <summary>The build wheel slice that's selected (always one; the foundation to start with).</summary>
        public int WheelIndex;
        public bool CrossbowAiming { get; private set; }

        /// <summary>The build wheel's slices, clockwise from the top (demolish sits at the bottom, like Rust).</summary>
        public static readonly (string Label, PieceType Piece, bool Demolish, bool Upgrade)[] WheelOptions =
        {
            // clockwise from the top: the wall sits on the right, demolish at the bottom
            ("Foundation", PieceType.Foundation, false, false), ("Ceiling", PieceType.Floor, false, false), ("Wall", PieceType.Wall, false, false),
            ("Window", PieceType.Window, false, false), ("Demolish", PieceType.Wall, true, false), ("Stairs", PieceType.Stairs, false, false),
            ("Doorway", PieceType.Doorway, false, false), ("Upgrade", PieceType.Wall, false, true),
        };
        public string AimText = "", BuildHint = "";
        public float DrawAmount { get; private set; } // bow draw / spear wind-up, 0..1
        public float RamCharge { get; private set; }  // ram wind-up, 0..1
        public bool Crouching { get; private set; }

        static readonly Color k_GhostOk = new Color(0.3f, 1f, 0.45f, 0.4f), k_GhostBad = new Color(1f, 0.3f, 0.3f, 0.4f);

        PlayerNet m_Net;
        CharacterController m_CC;
        NetworkTransform m_NT;
        Camera m_Cam;
        ViewModel m_VM;

        float m_Yaw, m_Pitch, m_VelY, m_Bob, m_Eye = Cfg.EyeHeight, m_LastStep, m_Speed;
        float m_NextSwing, m_ImpactAt = -1f, m_NextUpgrade, m_DrawStart = -1f, m_NextEat, m_NextBallThrow, m_LastHealth, m_NextSaw;
        bool m_Grounded = true, m_Sprinting;
        // crossbow: one shared crank for all your crossbows (fire one and every crossbow waits for the reload), and what we
        // last told the server, so a reload or shot isn't done twice while the server's inventory update is on its way
        float m_XbowBusyUntil = -1f, m_XbowSentLoad = -1f, m_XbowSentFire = -1f;
        int m_XbowSentSlot = -1;
        bool m_XbowLoading;
        Vector3 m_Push;
        Item m_ImpactItem;
        int m_RotOffset;
        bool m_WasDead, m_Placed;
        float m_InputLockUntil;
        Vector2 m_LookDelta;

        GameObject m_Ghost;
        int m_GhostId = -1;
        bool m_GhostOk;
        /// <summary>The building ghost is showing green (for tests).</summary>
        public bool GhostOk => m_GhostOk;
        public PieceKey GhostKey => m_GhostKey;
        readonly List<MeshRenderer> m_GhostRenderers = new List<MeshRenderer>();
        PieceKey m_GhostKey;
        Vector3 m_GhostPos;
        float m_GhostYaw;
        readonly HashSet<PieceKey> m_ClientKeys = new HashSet<PieceKey>();

        public override void OnNetworkSpawn()
        {
            m_Net = GetComponent<PlayerNet>();
            m_CC = GetComponent<CharacterController>();
            m_NT = GetComponent<NetworkTransform>();
            if (!IsOwner) { enabled = false; return; }
            Local = this;
            m_Cam = Camera.main;
            m_VM = new ViewModel(m_Cam.transform, Cfg.TeamColor[Mathf.Clamp(m_Net.Team.Value, 0, 3)]);
            m_LastHealth = m_Net.Health.Value;
            TryPlace();
        }

        /// <summary>Put the player on their bedrock once the host's map is built here.</summary>
        void TryPlace()
        {
            var game = NetGame.Instance;
            if (game == null || !game.IsSpawned || !MapBuilder.IsBuilt(game.MapKey.Value, game.MapSeed.Value)) return;
            m_Placed = true;
            // while waiting for players you're in the stadium, brawling with rocks
            bool arena = game.S == GameState.SuddenDeath || game.S == GameState.Waiting;
            NetGame.SpawnPoint(m_Net.Team.Value, arena, m_Net.Slot.Value, out var pos, out var yaw);
            LocalTeleport(pos, yaw);
            if (!arena) WakeUp();
        }

        /// <summary>(Re)spawned: a quick flash, and the rock back in your hands.</summary>
        void WakeUp()
        {
            Hud.Wake();
            m_InputLockUntil = Time.time + 0.3f;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            if (m_Ghost) Destroy(m_Ghost);
            m_VM?.Destroy();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (m_Cam) m_Cam.fieldOfView = 70f;
        }

        public void LocalTeleport(Vector3 pos, float yaw)
        {
            bool was = m_CC.enabled;
            m_CC.enabled = false;
            var rot = Quaternion.Euler(0, yaw, 0);
            transform.SetPositionAndRotation(pos, rot);
            if (m_NT != null && m_NT.IsSpawned) m_NT.Teleport(pos, rot, transform.localScale);
            m_CC.enabled = was;
            m_Yaw = yaw;
            m_Pitch = 0;
            m_VelY = 0;
            Physics.SyncTransforms();
        }

        /// <summary>Used by AutoTest to aim the camera.</summary>
        public void SetLook(float yaw, float pitch)
        {
            m_Yaw = yaw;
            m_Pitch = pitch;
        }

        /// <summary>AutoTest screenshots: force a draw amount (-1 = off) / trigger a swing animation.</summary>
        public float DebugDraw = -1f;
        public void DebugSwing(float dur) => m_VM.Swing(dur);
        public void DebugImpact(bool hit) => m_VM.Impact(hit);

        public void CloseMenu()
        {
            MenuOpen = false;
            LootTarget = null;
            UpgradesOpen = false;
        }

        /// <summary>E on your own upgrade station (UpgradeStation.cs): the UPGRADES screen (in the modes that have base upgrades - Upgrades.cs).</summary>
        public void OpenUpgrades()
        {
            LootTarget = null;
            UpgradesOpen = true;
            MenuOpen = true;
            WheelOpen = false;
            Sfx.Play2D(Sfx.Place, 0.4f);
        }

        /// <summary>Death screen choice (after the wall is down): back to your bedrock, or a random spot in the enemy's half.</summary>
        public void ChooseRespawn(bool wild)
        {
            if (m_Net.ChoosingRespawn) m_Net.RespawnChoiceRpc(wild);
        }

        /// <summary>A building piece or chest just appeared: if we're standing inside it, pop out on top of it (or to the side of a wall).</summary>
        public void ResolveOverlap(Transform piece)
        {
            if (m_CC == null || !m_CC.enabled || m_Net.Dead.Value) return;
            var cols = piece.GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return;
            Physics.SyncTransforms();
            var pos = transform.position;
            float top = float.MinValue;
            bool any = false;
            foreach (var c in cols)
            {
                if (c.isTrigger || !c.enabled) continue;
                if (!Physics.ComputePenetration(m_CC, pos, transform.rotation, c, c.transform.position, c.transform.rotation, out _, out _)) continue;
                any = true;
                top = Mathf.Max(top, c.bounds.max.y);
            }
            if (!any) return;
            // standing on/inside it with our feet below its top: step up onto it if that's not too far, otherwise get pushed out sideways
            if (top - pos.y < 2.2f) pos.y = top + 0.02f;
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    bool moved = false;
                    foreach (var c in cols)
                    {
                        if (c.isTrigger || !c.enabled) continue;
                        if (!Physics.ComputePenetration(m_CC, pos, transform.rotation, c, c.transform.position, c.transform.rotation, out var dir, out var dist)) continue;
                        dir.y = 0;
                        if (dir.sqrMagnitude < 0.001f) dir = -transform.forward;
                        pos += dir.normalized * (dist + 0.05f);
                        moved = true;
                    }
                    if (!moved) break;
                }
            }
            bool was = m_CC.enabled;
            m_CC.enabled = false;
            transform.position = pos;
            if (m_NT != null && m_NT.IsSpawned) m_NT.Teleport(pos, transform.rotation, transform.localScale);
            m_CC.enabled = was;
            m_VelY = 0;
            Physics.SyncTransforms();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            if (!m_Placed) { TryPlace(); if (!m_Placed) return; }
            var game = NetGame.Instance;
            bool dead = m_Net.Dead.Value;
            bool gameOver = game != null && game.S == GameState.GameOver;
            bool cutscene = VictoryCutscene.Active; // the victory cutscene: everything locked until the victory screen
            bool sd = game != null && game.S == GameState.SuddenDeath;
            bool carrying = m_Net.CarryingBall;
            bool riding = m_Net.Riding;
            Tutorial.Tick(this, m_Net);

            // ---- menus ----
            // Enter: type in the chat (Enter again sends it, Esc cancels)
            if (cutscene)
            {
                CloseMenu();
                Paused = false;
                WheelOpen = false;
                AirstrikeMapOpen = false;
                if (Chat.Open) Chat.Close();
            }
            if (!cutscene && !Chat.Open && !Paused && !MenuOpen && !Hud.Rebinding && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) Chat.Begin(false); // global chat
            else if (!cutscene && !Chat.Open && !Paused && !MenuOpen && !Hud.Rebinding && !Hud.Typing && Input.GetKeyDown(KeyCode.T)) Chat.Begin(true); // team chat
            if (!cutscene && Input.GetKeyDown(KeyCode.Escape) && !Chat.Open && !Hud.BackOut())
            {
                if (WheelOpen) WheelOpen = false;
                else if (AirstrikeMapOpen) CloseAirstrikeMap();
                else if (MenuOpen) CloseMenu();
                else Paused = !Paused;
            }
            if (Binds.Down(Bind.Inventory) && !Chat.Open && !dead && !gameOver && !sd && !Hud.Rebinding)
            {
                if (MenuOpen) CloseMenu(); else MenuOpen = true;
                Paused = false;
                WheelOpen = false;
            }
            if (sd || dead) CloseMenu();
            if (LootTarget != null && (!LootTarget.IsSpawned || !LootTarget.InReach(m_Net.EyePos))) LootTarget = null;
            // the UPGRADES screen goes with the menu, and closes if you walk away from your upgrade station
            if (UpgradesOpen && (!MenuOpen || !Cfg.AtOwnStation(m_Net.Team.Value, transform.position))) { if (MenuOpen) MenuOpen = false; UpgradesOpen = false; }
            if (Paused || MenuOpen || dead) WheelOpen = false;
            if (dead || m_Net.HeldItem != Item.Airstrike) AirstrikeMapOpen = false;

            bool choosing = m_Net.ChoosingRespawn;
            if (choosing && !Paused)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseRespawn(false);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseRespawn(true);
            }

            bool cursorFree = (MenuOpen || Paused || gameOver || choosing || WheelOpen || AirstrikeMapOpen) && !cutscene;
            Cursor.lockState = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = cursorFree && !WheelOpen;
            bool input = !cursorFree && !dead && !Hud.Rebinding && !Chat.Open && !cutscene;
            // you can keep walking, jumping and crouching with the inventory open
            bool frozen = game != null && game.FightFrozen;
            bool move = !dead && !Paused && !gameOver && !frozen && !AirstrikeMapOpen && !Chat.Open;
            bool locked = Time.time < m_InputLockUntil;

            // ---- damage / respawn feedback ----
            if (m_Net.Health.Value < m_LastHealth - 0.5f && !dead)
            {
                Fx.Shake(0.25f + (m_LastHealth - m_Net.Health.Value) / 100f);
                Fx.Kick(-2.5f);
                Sfx.Play2D(Sfx.Hurt, 0.8f);
            }
            m_LastHealth = m_Net.Health.Value;
            if (m_WasDead && !dead && (game == null || game.S != GameState.SuddenDeath)) WakeUp();
            m_WasDead = dead;

            // ---- look ----
            m_LookDelta = Vector2.zero;
            if (input)
            {
                m_LookDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * GameSettings.MouseSensitivity * (CrossbowAiming ? 0.6f : 1f) * (Scoped ? 0.35f : 1f);
                m_Yaw += m_LookDelta.x;
                m_Pitch = Mathf.Clamp(m_Pitch - m_LookDelta.y, -89f, 89f);
            }
            transform.rotation = Quaternion.Euler(0, m_Yaw, 0);
            if (Mathf.Abs(m_Net.Pitch.Value - m_Pitch) > 1f) m_Net.Pitch.Value = m_Pitch;

            var held = SelectItem(input);

            // ---- crouch ----
            // Ctrl (Crouch) only crouches; C (Slide) slides when you're running, and crouches when you aren't
            bool slideKey = move && !riding && Binds.Held(Bind.Slide);
            bool wantCrouch = move && !riding && (Binds.Held(Bind.Crouch) || slideKey);
            if (!wantCrouch && Crouching && !riding && !HeadroomToStand()) wantCrouch = true;
            if (dead) wantCrouch = false;
            if (slideKey && !dead && Binds.Down(Bind.Slide) && !m_SlideOn)
            {
                // the slide key while running (sprint held, or already moving fast): slide. Pressed in the air it waits for
                // the landing (a short buffer), so it works every time.
                // Or pressed on a slope while moving: you slide down it (no boost), like Apex.
                // (any sprinting counts, or moving at about a run - so it starts the moment you press it, every time)
                bool moving = Binds.Axis(Bind.Forward, Bind.Back) != 0f || Binds.Axis(Bind.Right, Bind.Left) != 0f;
                bool running = (Binds.Held(Bind.Sprint) && moving) || m_LastPlanar.magnitude > Cfg.WalkSpeed * 0.9f;
                bool downhill = m_LastPlanar.magnitude > 1f && SlopeDownhill(out var dh) && Vector3.Dot(dh, m_LastPlanar.normalized) > 0.3f;
                bool airborne = m_CC.enabled && !m_CC.isGrounded && Time.time - m_GroundedAt >= 0.2f;
                // in the air: it waits for the landing (however long the fall) and then slides off with the speed you had
                if ((running || downhill) && !m_Net.TreeCamo) m_SlideQueued = airborne ? float.MaxValue : Mathf.Max(Time.time, m_NextSlide) + 0.35f;
                else if (m_Net.TreeCamo) SlidesRefusedAsTree++;
            }
            // a tree can't slide: becoming one calls off a slide (or one waiting for the landing)
            if (m_Net.TreeCamo)
            {
                m_SlideQueued = -1f;
                if (m_SlideOn) { m_SlideVel = Vector3.zero; EndSlide(); }
            }
            if (m_SlideQueued == float.MaxValue && !slideKey) m_SlideQueued = -1f; // let go of C before landing: no slide
            if (wantCrouch != Crouching)
            {
                Crouching = wantCrouch;
                m_Net.Crouch.Value = wantCrouch;
            }
            if (dead || riding) { m_SlideOn = false; m_SlideQueued = -1f; }
            // the crouch key (Ctrl) pressed mid-slide: drop straight into a crouch - the slide stops dead, no glide-out
            // (and a slide waiting for the landing is called off)
            if (move && !riding && Binds.Down(Bind.Crouch) && (m_SlideOn || m_SlideQueued >= 0f))
            {
                m_SlideQueued = -1f;
                if (m_SlideOn) { m_SlideVel = Vector3.zero; EndSlide(); m_Push = Vector3.zero; SlideCrouches++; }
            }
            // start it right away if we're on (or only just left) the ground; in the air it waits for the landing
            bool onGround = m_CC.enabled && (m_CC.isGrounded || Time.time - m_GroundedAt < 0.2f);
            if (m_SlideQueued > Time.time && !m_SlideOn && slideKey && onGround && Time.time >= m_NextSlide && !TreeLocked && !m_Net.TreeCamo) { m_SlideQueued = -1f; StartSlide(); }

            // ---- move ----
            m_Speed = 0;
            TreeLocked = false;
            if (riding)
            {
                // the vehicle does the moving (Vehicle.Drive reads GetDriveInput); we just sit on it
                FollowSeat();
                m_VelY = 0;
                m_Push = Vector3.zero;
                m_Grounded = true;
                m_Sprinting = false;
                var v = RidingVehicle;
                var vcc = v != null ? v.GetComponent<CharacterController>() : null;
                if (vcc != null) m_Speed = Mathf.Min(6f, vcc.velocity.magnitude) * 0.3f;
            }
            else if (!dead && m_CC.enabled)
            {
                Vector3 wish = Vector3.zero;
                float fwdInput = 0;
                // tree camo: hold LMB or RMB to root yourself to the spot (you can still look around)
                TreeLocked = move && input && m_Net.TreeCamo && (Binds.Held(Bind.Attack) || Binds.Held(Bind.Aim));
                if (TreeLocked && m_SlideOn) { m_SlideVel = Vector3.zero; EndSlide(); }
                if (move && !TreeLocked)
                {
                    float h = Binds.Axis(Bind.Right, Bind.Left);
                    fwdInput = Binds.Axis(Bind.Forward, Bind.Back);
                    wish = transform.right * h + transform.forward * fwdInput;
                    if (wish.sqrMagnitude > 1f) wish.Normalize();
                }
                // crouching (or pressing slide) in the air doesn't brake you: you keep the speed you took off with
                bool airCrouch = Crouching && !m_SlideOn && !m_CC.isGrounded;
                bool sprint = move && Binds.Held(Bind.Sprint) && fwdInput > 0 && m_DrawStart < 0 && RamCharge <= 0 && (!Crouching || airCrouch) && !CrossbowAiming;
                float speed = Crouching ? Cfg.CrouchSpeed : sprint ? Cfg.SprintSpeed : Cfg.WalkSpeed;
                if (carrying) speed *= Cfg.BallCarrySpeedMul;
                if (m_DrawStart >= 0 || CrossbowAiming) speed *= 0.6f;
                if (held == Item.Ram && !carrying) speed *= Cfg.RamMoveMul;
                speed *= ThemeMaps.SpeedMul(transform.position); // THEME MAPS
                if (airCrouch) speed = Mathf.Max(speed, m_TakeoffSpeed);

                bool grounded = m_CC.isGrounded;
                if (grounded) m_GroundedAt = Time.time;
                bool ladder = OnLadder();
                // jump buffering: a press shortly before you land still jumps the moment you touch down
                if (move && !TreeLocked && Binds.Down(Bind.Jump)) m_JumpPressedAt = Time.time;
                bool jumpWanted = move && !TreeLocked && Time.time - m_JumpPressedAt <= Mathf.Max(Time.deltaTime, Cfg.JumpBuffer);
                if (grounded)
                {
                    if (m_VelY <= 0f) m_JumpedSinceGround = false;
                    // sliding: hug the ground going downhill instead of skipping off it
                    if (m_VelY < 0) m_VelY = m_SlideOn ? -(2f + m_SlideVel.magnitude * 0.8f) : -2f;
                }
                // coyote time: just walked off an edge, you can still jump for a moment (once - not a double jump)
                bool coyote = !grounded && !ladder && !m_JumpedSinceGround && m_VelY <= 0f && Time.time - m_GroundedAt <= Cfg.CoyoteTime;
                // you can jump out of a slide and keep all that speed
                if ((grounded || coyote) && jumpWanted && (!Crouching || m_SlideOn))
                {
                    if (grounded && Time.time - m_JumpPressedAt > Time.deltaTime + 0.001f) BufferedJumps++;
                    if (coyote) CoyoteJumps++;
                    Jumps++;
                    m_VelY = Cfg.JumpSpeed;
                    m_JumpPressedAt = -10f;
                    m_JumpedSinceGround = true;
                }
                if (ladder)
                {
                    // climb: W up, S down, nothing = hang on
                    bool up = move && (fwdInput > 0 || Binds.Held(Bind.Jump));
                    m_VelY = up ? 3.6f : move && fwdInput < 0 ? -3.6f : 0f;
                    // at the top: step off forwards onto the floor
                    var ld = CurrentLadder;
                    if (ld != null && transform.position.y > ld.TopWorldY - 0.15f && up) { m_Push = ld.ExitDir * 3f; m_VelY = Mathf.Max(m_VelY, 1.5f); }
                    if (move && Binds.Down(Bind.Jump)) { m_VelY = 4f; m_Push = -transform.forward * 3f; m_JumpPressedAt = -10f; m_JumpedSinceGround = true; }
                }
                else m_VelY -= Cfg.Gravity * Time.deltaTime;
                m_Grounded = grounded || ladder;
                m_Sprinting = sprint && wish.sqrMagnitude > 0.1f;
                // knockback (hit by a car) fades out, faster on the ground
                m_Push = Vector3.MoveTowards(m_Push, Vector3.zero, (grounded ? 14f : 3f) * Time.deltaTime);
                var planar = wish * speed;
                if (m_SlideOn) planar = TickSlide(wish, grounded, move);
                else planar = ThemeGround(planar, grounded); // THEME MAPS
                var before = transform.position;
                var flags = m_CC.Move((planar + m_Push + Vector3.up * m_VelY) * Time.deltaTime);
                if ((flags & CollisionFlags.Above) != 0 && m_VelY > 0) m_VelY = 0;
                // how fast we really moved (walls stop a slide dead, and a slide needs a run-up)
                var moved = transform.position - before;
                moved.y = 0;
                m_LastPlanar = moved / Mathf.Max(0.0001f, Time.deltaTime);
                if (m_SlideOn && m_LastPlanar.magnitude < m_SlideVel.magnitude * 0.85f && (flags & CollisionFlags.Sides) != 0)
                    m_SlideVel = m_SlideVel.normalized * m_LastPlanar.magnitude;
                m_Speed = m_SlideOn ? 0f : wish.magnitude * speed;
                // the speed you leave the ground with is what you keep if you crouch in the air
                if (grounded && !m_SlideOn) m_TakeoffSpeed = speed;
                else if (!grounded && m_SlideOn) m_TakeoffSpeed = m_SlideVel.magnitude;
                TickSlideSound();
                m_Bob += m_Speed * Time.deltaTime;
                if (grounded && m_Speed > 0.5f && m_Bob - m_LastStep > (Crouching ? 2.6f : 1.9f))
                {
                    m_LastStep = m_Bob;
                    Sfx.Play2D(Sfx.Step, Crouching ? 0.12f : 0.3f, 0.2f);
                }

                // (sudden death: falling off the platform is a death - the server sees it; this is only a safety net)
                if (transform.position.y < (sd ? Cfg.ArenaCenter.y - 150f : -30f))
                {
                    NetGame.SpawnPoint(m_Net.Team.Value, sd || (game != null && game.S == GameState.Waiting), m_Net.Slot.Value, out var p, out var y);
                    LocalTeleport(p, y);
                }
            }

            // ---- actions ----
            if (m_ImpactAt >= 0 && Time.time >= m_ImpactAt) DoImpact();
            TickCrossbowReload(held);
            TickPistolReload(held);
            TickShotgunReload(held);
            TickPortals(dead);
            TickJetpack(held, move && !dead);
            if (input && !gameOver && !locked && !frozen)
            {
                if (carrying) HandleBall();
                else
                {
                    switch (held)
                    {
                        case Item.Spear: HandleSpear(); break;
                        case Item.Bow: HandleBow(); break;
                        case Item.Crossbow: HandleCrossbow(); break;
                        case Item.Pistol:
                        case Item.Revolver: HandlePistol(held); break;
                        case Item.Shotgun: HandleShotgun(); break;
                        case Item.BuildingPlan: HandleBuildInput(); break;
                        case Item.Ram: HandleRam(); break;
                        case Item.Chest:
                        case Item.Barrier:
                        case Item.Car:
                        case Item.Workbench:
                        case Item.Workbench2:
                        case Item.Boat: /* THEME MAPS */ HandleDeploy(held); break;
                        case Item.Berry:
                        case Item.Meat: HandleBerry(); break;
                        case Item.Sniper: HandleSniper(); break;
                        case Item.PortalGun: HandlePortalGun(); break;
                        case Item.SlenderEgg:
                        case Item.BuildEgg:
                        case Item.BombBush:
                        case Item.EnderPearl:
                        case Item.RocketLauncher: HandleLootThrow(held); break;
                        case Item.GiantStaff: HandleOnce(held, () => m_Net.GiantStaffRpc()); break;
                        case Item.Airstrike: if (Binds.Down(Bind.Attack)) OpenAirstrikeMap(); break;
                        case Item.C4: HandleThrow(Item.C4); break;
                        case Item.FortTower: HandleThrow(Item.FortTower); break;
                        case Item.DeathWand: HandleWand(); break;
                        case Item.Helmet:
                        case Item.Armor:
                        case Item.HeavyArmor:
                        case Item.InvisPotion: HandleUseItem(held); break;
                        default: if (Cfg.IsMelee(held)) HandleMelee(held); break;
                    }
                }
            }
            if (held == Item.BuildingPlan && WheelOpen && !Binds.Held(Bind.Aim)) CloseWheel();
            bool drawing = input && !carrying && !gameOver && (held == Item.Bow || held == Item.Spear);
            if (!drawing) m_DrawStart = -1f;
            if (!drawing || held != Item.Spear) { m_SpearReleaseAt = -1f; m_SpearBufferUntil = -1f; }
            if ((held != Item.Berry && held != Item.Meat) || m_Net.Dead.Value || !input) m_EatStart = -1f;
            float drawTime = held == Item.Spear ? Cfg.SpearDrawTime : Cfg.BowDrawTime;
            DrawAmount = m_DrawStart >= 0 ? Mathf.Clamp01((Time.time - m_DrawStart) / Mathf.Max(0.05f, drawTime)) : 0f;
            if (!input || carrying || gameOver || held != Item.Ram || !Binds.Held(Bind.Attack)) RamCharge = 0f;
            CrossbowAiming = input && !carrying && held == Item.Crossbow && Binds.Held(Bind.Aim) && Time.time >= m_XbowBusyUntil;
            Scoped = input && !carrying && held == Item.Sniper && Binds.Held(Bind.Aim);

            PublishAction(held, dead, carrying);

            var target = FindInteract();
            Target = target;
            if (input) HandleInteract(target, carrying);
            else { m_PackStart = -1f; m_PackObj = null; }
            UpdateGhost(input && !carrying && !gameOver && !riding && !DemolishMode && !UpgradeMode ? held : Item.None);
            UpdateAimText(target);
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || m_Cam == null) return;
            // the victory cutscene has the camera (and your hands are put away)
            if (VictoryCutscene.CameraPose(out var cutPos, out var cutRot, out var cutFov))
            {
                m_Cam.transform.SetPositionAndRotation(cutPos, cutRot);
                m_Cam.fieldOfView = cutFov;
                m_VM.Update(new ViewModel.State { Item = m_Net.HeldItem, Visible = false, Visible2 = false });
                return;
            }
            if (m_Net.Riding) FollowSeat();
            Fx.TickCamera(Time.deltaTime);
            var rv = m_Net.Riding ? RidingVehicle : null;
            float eye = rv != null ? (rv.IsHorse ? Cfg.EyeHeight + 0.75f : Cfg.EyeHeight - 0.35f) : m_Net.EyeHeight; // high up on a horse so you can see ahead
            m_Eye = Mathf.Lerp(m_Eye, eye, Time.deltaTime * 12f);
            var rot = Quaternion.Euler(m_Pitch, m_Yaw, 0) * Quaternion.Euler(Fx.ShakeEuler());
            // holding the tree camo: the camera pulls back behind you so you can see the tree you've become
            m_Third = Mathf.MoveTowards(m_Third, m_Net.TreeCamo ? 1f : 0f, Time.deltaTime * 4f);
            var camPos = transform.position + Vector3.up * m_Eye;
            if (m_Third > 0f)
            {
                var eyePos = camPos;
                // up high and well back, so the camera clears things behind you and you see the whole tree
                var back = (Quaternion.Euler(0, m_Yaw, 0) * new Vector3(0.6f, 0f, -7f) + Vector3.up * 3.2f + rot * Vector3.back * 1.5f) * Smooth01(m_Third);
                float len = back.magnitude;
                if (len > 0.01f)
                {
                    var dir = back / len;
                    if (Physics.SphereCast(eyePos, 0.25f, dir, out var hit, len, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)
                        && !hit.collider.transform.IsChildOf(transform))
                        len = Mathf.Max(0.2f, hit.distance - 0.1f);
                    camPos = eyePos + dir * len;
                }
            }
            m_Cam.transform.SetPositionAndRotation(camPos, rot);
            float fov = 70f - DrawAmount * 18f + Fx.FovPunch;
            if (CrossbowAiming) fov = Cfg.CrossbowZoomFov + Fx.FovPunch;
            if (Scoped) fov = 15f;
            m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, fov, Time.deltaTime * 12f);

            var hs = m_Net.HeldStack;
            m_VM.Update(new ViewModel.State
            {
                Firing = m_Net.HeldItem == Item.Chainsaw && Binds.Held(Bind.Attack) && !MenuOpen && !Paused,
                Sprint = m_Sprinting,
                Grounded = m_Grounded,
                VelY = m_VelY,
                Item = m_Net.HeldItem,
                Ball = m_Net.CarryingBall,
                Visible = !m_Net.Dead.Value && m_Third < 0.5f,
                SpearAim = (m_DrawStart >= 0 || DebugDraw >= 0) && m_Net.HeldItem == Item.Spear,
                HasArrow = m_Net.Count(Item.Arrow) > 0,
                Draw = DebugDraw >= 0 ? DebugDraw : DrawAmount,
                RamCharge = RamCharge,
                Loaded = hs.Id == Item.Crossbow ? XbowLoaded(hs) : hs.Id == Item.Shotgun ? hs.Data > 0 : (hs.Id == Item.Sniper || hs.Id == Item.PortalGun) && hs.Data > 0,
                Aim = CrossbowAiming || Scoped,
                Visible2 = !Scoped,
                Reload = hs.Id == Item.Crossbow && Time.time < m_XbowBusyUntil ? 1f - (m_XbowBusyUntil - Time.time) / Mathf.Max(0.1f, Cfg.CrossbowReload) : hs.Id == Item.Shotgun ? ShotgunReloadProgress : -1f,
                Bob = m_Bob,
                Speed = m_Speed,
                Look = m_LookDelta,
                Crouch = Crouching,
            });
        }

        // ------------------------------------------------------------------ riding / ladders / knockback

        Vehicle RidingVehicle
        {
            get
            {
                if (!m_Net.Riding) return null;
                return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(m_Net.RidingId.Value, out var no) ? no.GetComponent<Vehicle>() : null;
            }
        }

        /// <summary>Stay glued to the seat of whatever we're riding.</summary>
        void FollowSeat()
        {
            var v = RidingVehicle;
            if (v == null) return;
            transform.position = v.SeatWorld;
        }

        /// <summary>Input for the vehicle we're driving (nothing while a menu is open).</summary>
        public void GetDriveInput(out float fwd, out float side, out bool jump, out bool sprint, out float lookYaw)
        {
            fwd = side = 0f;
            jump = sprint = false;
            lookYaw = m_Yaw;
            if (MenuOpen || Paused || WheelOpen || m_Net.Dead.Value) return;
            fwd = Binds.Axis(Bind.Forward, Bind.Back);
            side = Binds.Axis(Bind.Right, Bind.Left);
            jump = Binds.Down(Bind.Jump);
            sprint = Binds.Held(Bind.Sprint);
        }

        Ladder CurrentLadder;

        bool OnLadder()
        {
            var p = transform.position;
            CurrentLadder = null;
            foreach (var c in Physics.OverlapCapsule(p + Vector3.up * 0.3f, p + Vector3.up * 1.4f, 0.45f, ~0, QueryTriggerInteraction.Collide))
                if (c.isTrigger && c.TryGetComponent(out Ladder l)) { CurrentLadder = l; return true; }
            return false;
        }

        /// <summary>Hit by a car: thrown back (and up).</summary>
        public void Knockback(Vector3 v)
        {
            m_Push = new Vector3(v.x, 0, v.z);
            m_VelY = Mathf.Max(m_VelY, v.y);
            Fx.Shake(0.5f);
        }

        // ------------------------------------------------------------------ sliding (Apex / Titanfall style)

        bool m_SlideOn;
        Vector3 m_SlideVel, m_LastPlanar;
        public bool Sliding => m_SlideOn;
        public bool Sprinting => m_Sprinting;
        public bool Grounded => m_Grounded;
        float m_NextSlide;
        AudioSource m_SlideSound;

        /// <summary>Tell everyone what we're doing so our body shows it (BodyAnimator.Act).</summary>
        float m_EatFlashUntil;

        void PublishAction(Item held, bool dead, bool carrying)
        {
            var a = BodyAnimator.Act.None;
            if (!dead && !m_Net.Riding)
            {
                if (m_SlideOn) a = BodyAnimator.Act.Slide;
                else if (carrying) a = BodyAnimator.Act.None;
                else if (m_DrawStart >= 0 && held == Item.Bow) a = BodyAnimator.Act.BowDraw;
                else if (m_DrawStart >= 0 && held == Item.Spear) a = BodyAnimator.Act.SpearAim;
                else if (CrossbowAiming || Scoped) a = BodyAnimator.Act.Aim;
                else if (m_EatStart >= 0 || Time.time < m_EatFlashUntil) a = BodyAnimator.Act.Eat;
                else if (RamCharge > 0f) a = BodyAnimator.Act.Ram;
                else if (held == Item.Chainsaw && Binds.Held(Bind.Attack) && !MenuOpen && !Paused) a = BodyAnimator.Act.Saw;
            }
            if (m_Net.Action.Value != (byte)a) m_Net.Action.Value = (byte)a;
        }

        float m_SlideQueued = -1f, m_GroundedAt = -10f, m_NextSlideBoost, m_TakeoffSpeed;
        /// <summary>Tree camo with LMB / RMB held: rooted to the spot, only the view turns.</summary>
        public bool TreeLocked { get; private set; }
        /// <summary>For the tests: how many slides ended because you pushed the other way (into a crouch).</summary>
        public int SlideBrakes { get; private set; }
        /// <summary>For the tests: how many slides the crouch key turned straight into a crouch.</summary>
        public int SlideCrouches { get; private set; }
        /// <summary>For the tests: slide presses ignored because you're a tree (tree camo can't slide).</summary>
        public int SlidesRefusedAsTree { get; private set; }
        /// <summary>For the tests: jumps that came from a press buffered before landing / from coyote time after leaving an edge.</summary>
        public int BufferedJumps { get; private set; }
        public int CoyoteJumps { get; private set; }
        public int Jumps { get; private set; }
        public float VelY => m_VelY;
        float m_JumpPressedAt = -10f;
        bool m_JumpedSinceGround;

        /// <summary>The ground under you: which way is downhill (flat, unit length) and how steep (sine of the slope).</summary>
        bool SlopeDownhill(out Vector3 downhill) => SlopeDownhill(out downhill, out _);

        bool SlopeDownhill(out Vector3 downhill, out float steep)
        {
            downhill = Vector3.zero;
            steep = 0f;
            if (!Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var gh, 1.2f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) return false;
            var d = Vector3.ProjectOnPlane(Vector3.down, gh.normal); // length = sine of the slope
            steep = d.magnitude;
            d.y = 0;
            if (steep < 0.08f || d.sqrMagnitude < 1e-6f) return false; // flat enough (under ~5 degrees)
            downhill = d.normalized;
            return true;
        }

        /// <summary>
        /// Like Apex / Titanfall: you slide off with the speed you had (at least sprint speed) where you're looking, plus a
        /// boost - but only once the boost cooldown is over, so spamming slide never adds speed. Nothing can push a slide
        /// past Slide Max Speed.
        /// </summary>
        void StartSlide()
        {
            // the way you're pushing (W, A, S, D or a mix) - forward if you're not pushing any
            var dir = transform.right * Binds.Axis(Bind.Right, Bind.Left) + transform.forward * Binds.Axis(Bind.Forward, Bind.Back);
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.y = 0;
            dir.Normalize();
            float along = Mathf.Max(0f, Vector3.Dot(m_LastPlanar, dir));
            bool boost = Time.time >= m_NextSlideBoost;
            bool onSlope = SlopeDownhill(out var dh) && Vector3.Dot(dh, dir) > 0.3f;
            float start = Mathf.Max(along, onSlope && !Binds.Held(Bind.Sprint) ? along : Cfg.SprintSpeed);
            if (boost) { start += Cfg.SlideBoost; m_NextSlideBoost = Time.time + Mathf.Max(0f, Cfg.SlideBoostCooldown); }
            m_SlideVel = dir * Mathf.Min(start, Mathf.Max(Cfg.SprintSpeed, Cfg.SlideMaxSpeed));
            m_SlideOn = true;
            Fx.Punch(boost ? 4f : 2f);
            Sfx.Play2D(Sfx.Throw, boost ? 0.4f : 0.25f, 0.1f);
        }

        /// <summary>
        /// One frame of sliding: you keep your momentum and friction bleeds it away on the flat (Slide Slipperiness 0 =
        /// grippy, 10 = ice). Going down a slope speeds you up (Slide Slope Accel, more the steeper it is) and going up one
        /// slows you down fast, and you can steer a little. Jumping keeps the speed; landing with crouch still held carries
        /// on the slide. Capped at Slide Max Speed.
        /// </summary>
        Vector3 TickSlide(Vector3 wish, bool grounded, bool move)
        {
            float dt = Time.deltaTime;
            float slip = Mathf.Clamp(Cfg.SlideSlipperiness, 0f, 10f) / 10f;
            if (grounded)
            {
                float friction = Mathf.Lerp(16f, 0.2f, Mathf.Pow(slip, 0.7f)); // default 7.5: about 3 m/s² - a long, glidey slide
                bool slope = SlopeDownhill(out var dh, out float steep);
                // a steep enough slope cancels the friction going down it, so you keep sliding all the way down
                if (slope && Vector3.Dot(dh, m_SlideVel) > 0f) friction *= Mathf.Clamp01(1f - steep * 3f);
                float sp = Mathf.MoveTowards(m_SlideVel.magnitude, 0f, friction * dt);
                m_SlideVel = m_SlideVel.sqrMagnitude > 0.0001f ? m_SlideVel.normalized * sp : Vector3.zero;
                // downhill pulls you along, uphill holds you back - harder (sin 45 deg = 0.71 gets the full Slide Slope Accel)
                if (slope)
                {
                    bool up = Vector3.Dot(dh, m_SlideVel) < 0f;
                    m_SlideVel += dh * (steep / 0.71f) * Cfg.SlideSlopeAccel * (up ? Cfg.SlideUphillMul : 1f) * dt;
                }
                if (!Crouching || !move || m_SlideVel.magnitude < Cfg.SlideMinSpeed) { EndSlide(); return m_SlideVel; }
                // pushing back against the slide: stop it and stay crouched (no leftover push)
                if (wish.sqrMagnitude > 0.25f && m_SlideVel.sqrMagnitude > 0.01f && Vector3.Dot(wish.normalized, m_SlideVel.normalized) < -0.5f)
                {
                    m_SlideVel = Vector3.zero;
                    EndSlide();
                    SlideBrakes++;
                    return wish * Cfg.CrouchSpeed;
                }
            }
            else m_SlideVel = Vector3.MoveTowards(m_SlideVel, Vector3.zero, 0.4f * dt); // a little air drag
            // steer towards where you're pushing
            if (wish.sqrMagnitude > 0.1f && m_SlideVel.sqrMagnitude > 0.01f)
                m_SlideVel = Vector3.RotateTowards(m_SlideVel, wish.normalized * m_SlideVel.magnitude, Cfg.SlideSteer * Mathf.Deg2Rad * dt, 0f);
            m_SlideVel = Vector3.ClampMagnitude(m_SlideVel, Mathf.Max(Cfg.SprintSpeed, Cfg.SlideMaxSpeed));
            return m_SlideVel;
        }

        void EndSlide()
        {
            if (!m_SlideOn) return;
            m_SlideOn = false;
            m_NextSlide = Time.time + 0.2f;
            // what's left of the speed above a run fades out instead of stopping dead (only the extra: so ending and
            // restarting a slide can't stack it up)
            float extra = Mathf.Max(0f, m_SlideVel.magnitude - Cfg.SprintSpeed);
            if (extra > 0f) m_Push += m_SlideVel.normalized * Mathf.Min(extra, 2.5f) * 0.6f;
        }

        void TickSlideSound()
        {
            float want = m_SlideOn && m_Grounded ? Mathf.Clamp01(m_SlideVel.magnitude / 10f) * 0.5f * GameSettings.SfxVolume : 0f;
            if (m_SlideSound == null)
            {
                if (want <= 0f) return;
                m_SlideSound = gameObject.AddComponent<AudioSource>();
                m_SlideSound.clip = Sfx.Slide;
                m_SlideSound.loop = true;
                m_SlideSound.spatialBlend = 0f;
                m_SlideSound.volume = 0f;
                m_SlideSound.Play();
            }
            m_SlideSound.volume = Mathf.MoveTowards(m_SlideSound.volume, want, Time.deltaTime * 3f);
        }

        bool HeadroomToStand()
        {
            var p = transform.position;
            var hits = Physics.OverlapCapsule(p + Vector3.up * (Cfg.CrouchHeight + 0.05f), p + Vector3.up * (Cfg.StandHeight - 0.4f), 0.36f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!h.transform.IsChildOf(transform)) return false;
            return true;
        }

        // ------------------------------------------------------------------ items

        /// <summary>Any hotbar slot can be selected; an empty one means you're holding your rock.</summary>
        Item SelectItem(bool input)
        {
            int cur = m_Net.HeldSlot.Value;
            int want = cur;
            if (input && !WheelOpen)
            {
                for (int k = 0; k < Cfg.HotbarSize; k++)
                    if (Binds.Down(Bind.Hotbar1 + k)) want = k;
                float scroll = Tutorial.Allows(TutFeature.Hotbar) ? Input.mouseScrollDelta.y : 0f; // (the tutorial unlocks the hotbar later)
                if (scroll != 0) want = ((want + (scroll < 0 ? 1 : -1)) % Cfg.HotbarSize + Cfg.HotbarSize) % Cfg.HotbarSize;
            }
            if (want != cur)
            {
                m_Net.HeldSlot.Value = (byte)want;
                m_ImpactAt = -1f;
                m_DrawStart = -1f;
                RamCharge = 0f;
                WheelOpen = false;
            }
            return m_Net.HeldItem;
        }

        /// <summary>Where a projectile starts: a little in front of the eye, but never on the far side of something
        /// right in front of you (so you can't shoot through the glass wall by standing against it).</summary>
        Vector3 SafeOrigin(Ray ray, float ahead)
        {
            float d = ahead;
            if (Aim(ray, ahead + 0.1f, out var hit)) d = Mathf.Max(0f, hit.distance - 0.15f);
            return ray.origin + ray.direction * d;
        }

        public Ray CenterRay() => new Ray(transform.position + Vector3.up * m_Eye, Quaternion.Euler(m_Pitch, m_Yaw, 0) * Vector3.forward);

        /// <summary>
        /// Aim assist like most melee games: if the exact ray misses a player but a fat sphere along it
        /// touches one (and nothing solid is in front), count it as hitting that player.
        /// </summary>
        bool AimWithAssist(Ray ray, float range, float radius, out RaycastHit best)
        {
            bool exact = Aim(ray, range, out best);
            var ep = exact ? best.collider.GetComponentInParent<PlayerNet>() : null;
            if (ep != null) return !PlayerNet.GlassBetween(transform.position, ep.transform.position); // not through the glass wall
            if (radius <= 0f) return exact;
            float limit = exact ? best.distance + 0.05f : range;
            var hits = Physics.SphereCastAll(ray, radius, range, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool found = false;
            RaycastHit ph = default;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform) || h.distance <= 0f) continue;
                var p = h.collider.GetComponentInParent<PlayerNet>();
                if (p == null || p.Dead.Value || h.distance > limit || PlayerNet.GlassBetween(transform.position, p.transform.position)) continue;
                if (h.distance < bd) { bd = h.distance; ph = h; found = true; }
            }
            if (found) { best = ph; return true; }
            return exact;
        }

        bool Aim(Ray ray, float range, out RaycastHit best)
        {
            best = default;
            bool found = false;
            float bestDist = float.MaxValue;
            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        // ------------------------------------------------------------------ melee (hit lands at the swing's impact frame)

        void HandleMelee(Item held)
        {
            if (!Binds.Held(Bind.Attack) || Time.time < m_NextSwing) return;
            var st = Cfg.Melee(held);
            m_NextSwing = Time.time + st.Cooldown;
            if (held == Item.Chainsaw)
            {
                // continuous: no wind-up, the cut lands right away
                if (Time.time >= m_NextSaw) { m_NextSaw = Time.time + 0.18f; Sfx.Play2D(Sfx.Saw, 0.35f, 0.05f); }
                m_ImpactItem = held;
                DoImpact();
                return;
            }
            // the sword winds up for longer (it scales with its swing time)
            float impact = held == Item.Sword ? Mathf.Clamp(Cfg.SwordSwingTime * 0.3f, ViewModel.ImpactTime, 0.6f) : ViewModel.ImpactTime;
            m_VM.Swing(st.Cooldown, impact);
            Sfx.Play2D(Sfx.Swing, 0.35f, 0.15f);
            m_ImpactAt = Time.time + impact;
            m_ImpactItem = held;
        }

        void DoImpact()
        {
            m_ImpactAt = -1f;
            if (m_Net.HeldItem != m_ImpactItem || m_Net.CarryingBall || m_Net.Dead.Value) return;
            var st = Cfg.Melee(m_ImpactItem);
            var ray = CenterRay();
            bool saw = m_ImpactItem == Item.Chainsaw;
            Fx.Kick(saw ? 0.15f : 0.8f);
            if (!AimWithAssist(ray, st.Range, Cfg.MeleeAssist, out var hit))
            {
                if (!saw) m_VM.Impact(false);
                m_Net.MeleeRpc(false, default, Vector3.zero, false);
                return;
            }
            if (!saw) m_VM.Impact(true);

            var no = hit.collider.GetComponentInParent<NetworkObject>();
            bool weak = false;
            if (no != null && no.TryGetComponent(out PlayerNet p) && p != m_Net && !p.Dead.Value)
            {
                bool head = p.IsHeadshot(hit.point);
                float dmg = Cfg.MeleePlayerDamage(m_ImpactItem, head);
                Fx.Blood(hit.point, ray.direction, head);
                Fx.DamageNumber(hit.point, dmg, head);
                Hud.HitMarker(false, head);
                Fx.Shake(head ? 0.3f : 0.18f);
                Fx.Punch(head ? -3f : -1.5f);
            }
            else if (no != null && no.TryGetComponent(out ResourceNode n) && !n.IsBush)
            {
                weak = n.IsWeakSpotAimed(ray, hit.point, 0.45f);
                Fx.Play(n.IsWood ? FxKind.WoodChips : FxKind.StoneChips, hit.point, hit.normal);
                if (weak) { Fx.Play(n.IsWood ? FxKind.WeakSpotTree : FxKind.WeakSpot, hit.point, hit.normal); Fx.Punch(-1.5f); Tutorial.WeakHits++; }
                Fx.Shake(0.08f);
            }
            else if (no != null && (no.GetComponent<Structure>() != null || no.GetComponent<Container>() != null))
            {
                Fx.Play(FxKind.StructureHit, hit.point, hit.normal);
                Fx.Shake(0.1f);
            }
            else
            {
                Fx.Chips(hit.point, hit.normal, new Color(0.35f, 0.3f, 0.22f), 5, 2f);
                Sfx.Play(Sfx.Thud, hit.point, 0.4f);
            }

            if (no != null) m_Net.MeleeRpc(true, no, hit.point, weak);
            else m_Net.MeleeRpc(false, default, hit.point, false);
        }

        /// <summary>Rust style: LMB stabs; hold RMB to wind up, then LMB throws. Releasing RMB cancels.</summary>
        void HandleSpear()
        {
            // hold RMB to ready it (LMB throws); let go and it's a normal spear again (the tutorial: once it's taught)
            // Timed like the throws in other games: RMB raises it over the shoulder (the wind-up only starts once a poke has
            // recovered), it can't be let go before Spear Min Windup, a click is remembered for Spear Input Buffer (clicked
            // during the wind-up or the recovery it goes the moment it can), and once the arm starts coming through the throw
            // can't be called off. After it there's a recovery before the next poke or wind-up, and lowering it takes a moment.
            bool canThrow = Tutorial.Allows(TutFeature.Throw);
            float now = Time.time;
            if (m_SpearReleaseAt >= 0f)
            {
                // committed: the arm is coming through
                if (now >= m_SpearReleaseAt) ReleaseSpear();
                return;
            }
            bool aimHeld = canThrow && Binds.Held(Bind.Aim);
            if (aimHeld && Binds.Down(Bind.Attack))
            {
                // remembered until a little after the soonest it could go (the end of any recovery, then the minimum wind-up)
                float soonest = (m_DrawStart >= 0f ? m_DrawStart : Mathf.Max(now, m_NextSwing)) + Mathf.Max(0f, Cfg.SpearMinWindup);
                m_SpearBufferUntil = Mathf.Max(now, soonest) + Mathf.Max(0.05f, Cfg.SpearInputBuffer);
            }
            if (!aimHeld)
            {
                m_SpearBufferUntil = -1f;
                // letting go of RMB lowers it again - that takes a moment before it can poke
                if (m_DrawStart >= 0f) { m_DrawStart = -1f; m_NextSwing = Mathf.Max(m_NextSwing, now + Mathf.Max(0f, Cfg.SpearLowerTime)); }
                HandleMelee(Item.Spear);
                return;
            }
            // RMB held: the wind-up starts as soon as any poke / throw has recovered
            if (m_DrawStart < 0f)
            {
                if (now < m_NextSwing || m_ImpactAt >= 0f) return;
                m_DrawStart = now;
            }
            if (m_SpearBufferUntil >= now && now - m_DrawStart >= Mathf.Max(0f, Cfg.SpearMinWindup))
            {
                m_SpearBufferUntil = -1f;
                // a quick throw still goes a good way; holding longer throws it harder
                m_SpearPower = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01((now - m_DrawStart) / Mathf.Max(0.05f, Cfg.SpearDrawTime)));
                m_SpearReleaseAt = now + Mathf.Max(0f, Cfg.SpearReleaseTime);
                m_VM.Throw();
                Sfx.Play2D(Sfx.Swing, 0.4f, 0.1f);
                SpearCommits++;
                if (m_SpearReleaseAt <= now) ReleaseSpear();
            }
        }

        float m_SpearReleaseAt = -1f, m_SpearBufferUntil = -1f, m_SpearPower = 1f;
        /// <summary>For the tests: throws started (the arm coming through) and let go.</summary>
        public int SpearCommits { get; private set; }
        public int SpearReleases { get; private set; }
        /// <summary>For the tests: the spear's wind-up has started (RMB held, past any recovery).</summary>
        public bool SpearWindingUp => m_DrawStart >= 0f && m_Net != null && m_Net.HeldItem == Item.Spear;
        public bool SpearThrowCommitted => m_SpearReleaseAt >= 0f;

        /// <summary>The spear leaves the hand: thrown where you look, as hard as the wind-up was long.</summary>
        void ReleaseSpear()
        {
            m_SpearReleaseAt = -1f;
            m_DrawStart = -1f;
            m_NextSwing = Time.time + Mathf.Max(0.1f, Cfg.SpearThrowRecovery);
            if (m_Net.HeldItem != Item.Spear || m_Net.Dead.Value || m_Net.CarryingBall) return;
            var ray = CenterRay();
            Vector3 origin = SafeOrigin(ray, 0.8f);
            Vector3 vel = ray.direction * Cfg.SpearThrowSpeed * m_SpearPower;
            ArrowProjectile.SpawnSpear(origin, vel, m_Net, true);
            m_Net.ThrowSpearRpc(origin, vel);
            Tutorial.SpearThrows++;
            SpearReleases++;
            Sfx.Play2D(Sfx.Throw, 0.6f);
            Fx.Kick(2f);
        }

        void HandleBow()
        {
            if (Binds.Down(Bind.Attack))
            {
                if (m_Net.Count(Item.Arrow) > 0) m_DrawStart = Time.time;
                else Hud.Push("No arrows - craft some in your base (TAB)");
            }
            if (Binds.Down(Bind.Aim)) m_DrawStart = -1f;
            if (Binds.Up(Bind.Attack) && m_DrawStart >= 0)
            {
                float t = Time.time - m_DrawStart;
                m_DrawStart = -1f;
                if (m_Net.Count(Item.Arrow) <= 0) return;
                // fires straight away; the longer the draw, the faster (further) and harder it flies
                float power = Mathf.Lerp(Cfg.BowMinSpeed, 1f, Mathf.Clamp01(t / Mathf.Max(0.05f, Cfg.BowDrawTime)));
                var ray = CenterRay();
                Vector3 origin = SafeOrigin(ray, 0.6f);
                Vector3 vel = ray.direction * Cfg.ArrowSpeed * power;
                ArrowProjectile.Spawn(origin, vel, m_Net, true);
                m_Net.FireArrowRpc(origin, vel);
                Sfx.Play2D(Sfx.Twang, 0.6f);
                Fx.Kick(1.2f);
            }
        }

        /// <summary>Hold LMB to wind the ram up; at full charge it slams the enemy piece you're looking at.</summary>
        void HandleRam()
        {
            if (!Binds.Held(Bind.Attack)) return;
            RamCharge = Mathf.Min(1f, RamCharge + Time.deltaTime / Mathf.Max(0.05f, Cfg.RamWindup));
            if (RamCharge < 1f) return;

            RamCharge = 0f;
            m_VM.Use();
            Fx.Kick(2f);
            if (Aim(CenterRay(), Cfg.RamRange, out var hit))
            {
                var no = hit.collider.GetComponentInParent<NetworkObject>();
                var s = no != null ? no.GetComponent<Structure>() : null;
                var c = no != null ? no.GetComponent<Container>() : null;
                int team = s != null ? s.Team.Value : c != null && c.Breakable ? c.Team.Value : -1;
                if (team >= 0) // your own pieces too
                {
                    m_Net.RamStrikeRpc(no, hit.point);
                    Fx.Play(FxKind.Smash, hit.point, hit.normal);
                    Fx.Shake(0.55f);
                    return;
                }
                // a portal on what you hit: the ram smashes it (raiding breaks portals)
                if (NetGame.Instance != null && NetGame.Instance.PortalNear(hit.point, PlayerNet.RamPortalReach))
                {
                    m_Net.RamPortalRpc(hit.point);
                    Fx.Shake(0.55f);
                    return;
                }
            }
            Sfx.Play(Sfx.Thud, transform.position + transform.forward, 0.5f);
            Hud.Push("The ram only works on buildings - get right up to one");
        }

        /// <summary>Builder: put the ball down on whatever is just in front of you (ground, floor, a roof...).</summary>
        void PlantBall()
        {
            var fwd = transform.forward;
            fwd.y = 0;
            var from = transform.position + fwd.normalized * 1.8f + Vector3.up * 2.2f;
            var hits = Physics.RaycastAll(from, Vector3.down, 8f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Ball>() != null || h.collider.transform.IsChildOf(transform)) continue;
                m_Net.PlantBallRpc(h.point);
                Sfx.Play2D(Sfx.Place, 0.7f);
                return;
            }
            Hud.Push("No ground to put the ball down on here");
        }

        void HandleBall()
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextBallThrow) return;
            // Builder: no throwing - LMB puts the ball down on its block instead
            if (Cfg.Builder) { m_NextBallThrow = Time.time + 0.5f; PlantBall(); return; }
            m_NextBallThrow = Time.time + 0.5f;
            m_NextSwing = Time.time + 0.7f; // no instant swing with whatever comes back into your hands
            var rv = m_Net.Riding ? RidingVehicle : null;
            var rcc = rv != null ? rv.GetComponent<CharacterController>() : m_CC;
            var ray = CenterRay();
            m_Net.ThrowBallRpc(ray.origin, ray.direction, rcc != null && rcc.enabled ? rcc.velocity : Vector3.zero);
            // on our own screen the ball mustn't bump into us (or our horse) on its way out either
            if (Ball.Instance != null) Ball.Instance.IgnoreLocal(m_CC, rcc != m_CC ? rcc : null, 1f);
            m_VM.Throw();
            Sfx.Play2D(Sfx.Throw, 0.7f);
            Fx.Kick(2.5f);
        }

        void HandleBerry()
        {
            if (!Tutorial.Allows(TutFeature.Eat)) return;
            if (m_EatStart >= 0)
            {
                // food takes a moment to eat (berries 1.5 s, horse meat 3 s), a bite every ~0.75 s; switching away cancels it
                int bites = Mathf.FloorToInt(EatProgress * EatTime / 0.75f);
                if (bites > m_EatBites && EatProgress < 1f) { m_EatBites = bites; m_VM.Eat(); Sfx.Play2D(Sfx.Eat, 0.7f); }
                if (EatProgress < 1f) return;
                m_EatStart = -1f;
                m_Net.EatRpc();
                return;
            }
            // LMB with berries on a horse: feed it one (it heals; you don't eat it)
            if (m_Net.HeldItem == Item.Berry && Binds.Down(Bind.Attack) && Time.time >= m_NextSwing)
            {
                m_NextSwing = Time.time + 0.35f;
                var horse = HorseAimed();
                if (horse == null) Hud.Push("Berries: RMB to eat - or LMB on a horse to feed it");
                else if (horse.Hp.Value >= horse.MaxHp - 0.5f) Hud.Push("That horse is already at full health");
                else { m_Net.FeedHorseRpc(horse.NetworkObject); m_VM.Use(); Sfx.Play2D(Sfx.Eat, 0.6f); }
                return;
            }
            if (!Binds.Down(Bind.Aim) || Time.time < m_NextEat) return;
            if (m_Net.Health.Value >= Cfg.MaxHealth) { Hud.Push("You're already at full health"); return; }
            m_VM.Eat();
            Sfx.Play2D(Sfx.Eat, 0.7f);
            m_EatStart = Time.time;
            m_EatBites = 0;
            m_NextEat = Time.time + EatTime + 0.1f;
        }

        /// <summary>The horse under the crosshair, close enough to feed (null if there isn't one).</summary>
        Vehicle HorseAimed()
        {
            if (!Aim(CenterRay(), Cfg.InteractRange + 2f, out var hit)) return null;
            var v = hit.collider.GetComponentInParent<Vehicle>();
            return v != null && v.IsHorse && v.IsSpawned ? v : null;
        }

        /// <summary>C4 or the fort tower: LMB lobs it where you look.</summary>
        void HandleThrow(Item kind)
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextSwing) return;
            m_NextSwing = Time.time + 0.6f;
            var ray = CenterRay();
            Vector3 origin = SafeOrigin(ray, 0.7f);
            Vector3 vel = ray.direction * 16f + Vector3.up * 2.5f;
            ArrowProjectile.SpawnThrown(kind, origin, vel, m_Net, true);
            if (kind == Item.C4) m_Net.ThrowC4Rpc(origin, vel);
            else m_Net.ThrowFortRpc(origin, vel);
            m_VM.Throw();
            Sfx.Play2D(Sfx.Throw, 0.6f);
        }

        /// <summary>Rust crossbow: LMB fires the loaded bolt (flat and hard hitting), RMB aims down the sights,
        /// and it reloads by itself afterwards if you have an arrow.</summary>
        void HandleCrossbow()
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_XbowBusyUntil) return;
            var st = m_Net.HeldStack;
            if (!XbowLoaded(st))
            {
                if (m_Net.Count(Item.Arrow) == 0) Hud.Push("No arrows to load - craft some in your base (TAB)");
                return;
            }
            var ray = CenterRay();
            Vector3 origin = SafeOrigin(ray, 0.5f);
            Vector3 vel = ray.direction * Cfg.CrossbowSpeed;
            ArrowProjectile.Spawn(origin, vel, m_Net, true, Cfg.CrossbowDamage);
            m_Net.FireCrossbowRpc(origin, vel);
            m_XbowSentFire = Time.time;
            m_XbowSentSlot = m_Net.HeldSlot.Value;
            m_XbowSentLoad = -1f;
            // the crank starts now - and it's the same crank for every crossbow you have
            m_XbowBusyUntil = Time.time + Cfg.CrossbowReload;
            m_XbowLoading = true;
            Sfx.Play2D(Sfx.Clink, 0.4f);
            m_VM.Use();
            Sfx.Play2D(Sfx.Twang, 0.8f, 0.02f);
            Fx.Kick(2.5f);
        }

        /// <summary>The held crossbow is loaded, counting a load / shot we sent that the server hasn't confirmed yet.</summary>
        bool XbowLoaded(ItemStack st)
        {
            bool sameSlot = m_XbowSentSlot == m_Net.HeldSlot.Value;
            if (sameSlot && m_XbowSentFire >= 0 && Time.time - m_XbowSentFire < 1f && st.Data > 0) return false;
            if (sameSlot && m_XbowSentLoad >= 0 && Time.time - m_XbowSentLoad < 1f && st.Data == 0) return true;
            return st.Data > 0;
        }

        /// <summary>
        /// The crossbow loads itself if you have an arrow. The reload is one crank shared by all your crossbows: swapping to
        /// another crossbow doesn't skip it, and a second, loaded crossbow can't fire until it's done.
        /// </summary>
        void TickCrossbowReload(Item held)
        {
            if (m_Net.Dead.Value) { m_XbowBusyUntil = -1f; m_XbowLoading = false; return; }
            if (held != Item.Crossbow) return; // the crank keeps going while you hold something else
            var st = m_Net.HeldStack;
            if (XbowLoaded(st)) { if (Time.time >= m_XbowBusyUntil) m_XbowLoading = false; return; }
            if (m_Net.Count(Item.Arrow) <= 0 || MenuOpen) return;
            if (!m_XbowLoading)
            {
                m_XbowLoading = true;
                m_XbowBusyUntil = Time.time + Cfg.CrossbowReload;
                Sfx.Play2D(Sfx.Clink, 0.4f);
                return;
            }
            if (Time.time < m_XbowBusyUntil) return;
            m_XbowLoading = false;
            m_XbowSentLoad = Time.time;
            m_XbowSentFire = -1f;
            m_XbowSentSlot = m_Net.HeldSlot.Value;
            m_Net.ReloadCrossbowRpc();
            Sfx.Play2D(Sfx.Clink, 0.6f);
        }

        void HandleWand()
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextSwing) return;
            m_NextSwing = Time.time + 0.6f;
            m_Net.WandRpc(CenterRay().direction);
            m_VM.Use();
            Fx.Kick(3f);
            Fx.Shake(0.2f);
        }

        /// <summary>Helmet / armour: LMB/RMB puts it on. Potion: LMB/RMB drinks it.</summary>
        void HandleUseItem(Item held)
        {
            if (!(Binds.Down(Bind.Attack) || Binds.Down(Bind.Aim)) || Time.time < m_NextEat) return;
            m_NextEat = Time.time + 0.8f;
            m_Net.UseItemRpc();
            if (held == Item.InvisPotion) { m_VM.Eat(); Sfx.Play2D(Sfx.Eat, 0.6f); m_EatFlashUntil = Time.time + 0.9f; }
            else { m_VM.Use(); Sfx.Play2D(Sfx.Clink, 0.6f); }
        }

        void HandleDeploy(Item held)
        {
            if (!Binds.Down(Bind.Attack) || !Tutorial.Allows(TutFeature.Deploy)) return;
            if (m_GhostOk)
            {
                m_Net.PlaceDeployableRpc((byte)held, m_GhostPos, m_GhostYaw);
                m_VM.Use();
                Sfx.Play(Sfx.Place, m_GhostPos, 0.8f);
            }
            else if (!string.IsNullOrEmpty(BuildHint)) Hud.Push(BuildHint);
        }

        /// <summary>Released RMB: take whatever slice of the wheel the mouse is over.</summary>
        void CloseWheel()
        {
            WheelOpen = false;
            SelectWheel(Hud.WheelHover >= 0 ? Hud.WheelHover : WheelIndex);
            Sfx.Play2D(Sfx.Pop, 0.4f);
        }

        public void SelectWheel(int i)
        {
            WheelIndex = Mathf.Clamp(i, 0, WheelOptions.Length - 1);
            var o = WheelOptions[WheelIndex];
            DemolishMode = o.Demolish;
            UpgradeMode = o.Upgrade;
            if (!o.Demolish && !o.Upgrade) BuildPiece = o.Piece;
        }

        void TryUpgrade()
        {
            if (Cfg.WoodMode) { Hud.Push("Wood mode: no stone upgrades"); return; }
            if (!Aim(CenterRay(), Cfg.BuildRange, out var hit)) return;
            var s = hit.collider.GetComponentInParent<Structure>();
            if (s == null || !s.Upgradable || s.Team.Value != m_Net.Team.Value) return;
            m_Net.UpgradeRpc(s.NetworkObject);
            m_VM.Use();
            Sfx.Play(Sfx.Clink, hit.point, 0.8f);
        }

        void TryDemolish()
        {
            if (!Aim(CenterRay(), Cfg.BuildRange, out var dh)) return;
            var no = dh.collider.GetComponentInParent<NetworkObject>();
            var s = no != null ? no.GetComponent<Structure>() : null;
            var c = no != null ? no.GetComponent<Container>() : null;
            int team = s != null ? s.Team.Value : c != null && c.Breakable ? c.Team.Value : -1;
            if (team == m_Net.Team.Value)
            {
                m_Net.DemolishRpc(no);
                m_VM.Use();
                Fx.Play(FxKind.Break, dh.point, dh.normal);
            }
            else if (team >= 0) Hud.Push("You can only demolish your own buildings");
        }

        void HandleBuildInput()
        {
            if (!Tutorial.Allows(TutFeature.Build)) return; // (the tutorial: you get the plan a step before you place with it)
            // hold RMB: Rust-style wheel of building pieces (+ demolish); let go over the one you want
            if (Binds.Down(Bind.Aim)) { WheelOpen = true; Hud.WheelOpened(); }
            if (WheelOpen) return;
            if (Binds.Down(Bind.Rotate)) m_RotOffset = (m_RotOffset + 1) & 3;

            if (Binds.Down(Bind.Attack) && DemolishMode) { if (Tutorial.Allows(TutFeature.Demolish)) TryDemolish(); return; }
            if (Binds.Down(Bind.Attack) && UpgradeMode) { TryUpgrade(); return; }
            if (Binds.Down(Bind.Attack))
            {
                if (m_GhostOk)
                {
                    m_Net.PlaceRpc((byte)BuildPiece, m_GhostKey.I, m_GhostKey.J, m_GhostKey.L, m_GhostKey.D);
                    m_VM.Use();
                    BuildGrid.Pose(BuildPiece, m_GhostKey, out var pos, out _);
                    Sfx.Play(Sfx.Place, pos, 0.8f);
                }
                else if (!string.IsNullOrEmpty(BuildHint)) Hud.Push(BuildHint);
            }

            // X: demolish one of your own pieces (barriers and chests too)
            if (Binds.Down(Bind.Demolish)) TryDemolish();

            if (Binds.Down(Bind.Upgrade) && Time.time >= m_NextUpgrade && !Cfg.WoodMode)
            {
                if (Aim(CenterRay(), Cfg.BuildRange, out var hit))
                {
                    var s = hit.collider.GetComponentInParent<Structure>();
                    if (s != null && s.Upgradable)
                    {
                        m_NextUpgrade = Time.time + Cfg.UpgradeCooldown;
                        m_Net.UpgradeRpc(s.NetworkObject);
                        m_VM.Use();
                        Sfx.Play(Sfx.Clink, hit.point, 0.8f);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ interaction (whatever you're looking at)

        public enum TargetKind { None, Ball, Door, Container, Bush, PlayerSpear, WorldItem, SelfSpear, Machine, Vehicle, UpgradeStation }

        public struct Interactable
        {
            public TargetKind Kind;
            public NetworkObject Obj;
            public int ItemId;
            public ItemStack Stack;
            public int MachineTeam; // (Machine and UpgradeStation: whose it is)
        }

        Interactable FindInteract()
        {
            var result = new Interactable();
            if (m_Cam == null || m_Net.Dead.Value) return result;
            var ray = CenterRay();
            float range = Cfg.InteractRange + 2f;
            float found = range;

            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var mount = m_Net.Riding ? RidingVehicle : null;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (mount != null && h.collider.transform.IsChildOf(mount.transform)) continue; // the horse you're sitting on
                var machine = h.collider.GetComponentInParent<Machine>();
                if (machine != null && h.distance <= Cfg.InteractRange + 1.5f)
                {
                    result.Kind = TargetKind.Machine;
                    result.MachineTeam = machine.Team;
                    found = h.distance;
                    break;
                }
                var station = h.collider.GetComponentInParent<UpgradeStation>();
                if (station != null && h.distance <= Cfg.InteractRange + 1f)
                {
                    result.Kind = TargetKind.UpgradeStation;
                    result.MachineTeam = station.Team;
                    found = h.distance;
                    break;
                }
                var kind = Classify(h.collider, out var obj);
                if (kind != TargetKind.None && h.distance <= Cfg.InteractRange + 0.5f) { result.Kind = kind; result.Obj = obj; found = h.distance; break; }
                if (!h.collider.isTrigger) { found = h.distance; break; } // walls block interaction
            }

            // items in the world have no collider: pick the one closest to the crosshair
            var game = NetGame.Instance;
            if (game != null)
            {
                // (from a horse you sit high up, so it reaches a bit further down: loot comes before getting off)
                float bestPerp = mount != null ? 0.65f : 0.5f, reach = Cfg.InteractRange + (mount != null ? 1.8f : 1f);
                foreach (var it in game.Items)
                {
                    var mid = it.Center;
                    float t = Vector3.Dot(mid - ray.origin, ray.direction);
                    if (t < 0 || t > found + 0.4f || t > reach) continue;
                    float perp = Vector3.Distance(ray.origin + ray.direction * t, mid);
                    if (perp < bestPerp) { bestPerp = perp; result = new Interactable { Kind = TargetKind.WorldItem, ItemId = it.Id, Stack = it.Stack }; }
                }
            }

            // forgiving fallback for the ball: roughly looking at it while standing next to it
            var ball = Ball.Instance;
            if (result.Kind == TargetKind.None && ball != null && !ball.IsCarried)
            {
                // from a horse you sit high up, so it's a bit more forgiving
                bool up = mount != null;
                var bp = ball.transform.position;
                float t = Vector3.Dot(bp - ray.origin, ray.direction);
                if (t > 0 && t < found + 0.5f && t < Cfg.InteractRange + (up ? 1.8f : 0.5f) && Vector3.Distance(ray.origin + ray.direction * t, bp) < (up ? 1.4f : 0.9f))
                    result = new Interactable { Kind = TargetKind.Ball, Obj = ball.NetworkObject };
            }

            if (result.Kind == TargetKind.None && m_Net.StuckSpears.Value > 0) result.Kind = TargetKind.SelfSpear;
            return result;
        }

        TargetKind Classify(Collider col, out NetworkObject obj)
        {
            obj = col.GetComponentInParent<NetworkObject>();
            if (obj == null) return TargetKind.None;
            if (obj.GetComponent<Ball>() != null) return TargetKind.Ball;
            if (obj.TryGetComponent(out Structure s)) return s.PType == PieceType.Doorway ? TargetKind.Door : TargetKind.None;
            if (obj.GetComponent<Container>() != null) return TargetKind.Container;
            if (obj.TryGetComponent(out ResourceNode n)) return n.IsBush && n.Amount.Value > 0 ? TargetKind.Bush : TargetKind.None;
            if (obj.TryGetComponent(out PlayerNet p) && p != m_Net && p.StuckSpears.Value > 0) return TargetKind.PlayerSpear;
            if (obj.TryGetComponent(out Vehicle v) && !v.HasDriver) return TargetKind.Vehicle;
            return TargetKind.None;
        }

        // ---- hold E: pick an empty chest / workbench of yours back up ----
        NetworkObject m_PackObj;
        float m_PackStart = -1f;
        /// <summary>How far through holding E to pick up a chest / workbench (0..1; 0 when not holding). Drawn round the crosshair.</summary>
        public float PackUpProgress => m_PackStart < 0f ? 0f : Mathf.Clamp01((Time.time - m_PackStart) / Mathf.Max(0.05f, Cfg.PackUpHoldTime));
        /// <summary>What's being picked up (for the HUD's label).</summary>
        public string PackUpName { get; private set; }

        /// <summary>The thing in the crosshair can be picked back up by holding E (an empty chest / workbench of your team's).</summary>
        bool CanPackUp(Interactable t, bool carrying)
        {
            if (carrying || t.Kind != TargetKind.Container || t.Obj == null || !Tutorial.Allows(TutFeature.Deploy)) return false;
            var c = t.Obj.GetComponent<Container>();
            return c != null && m_Net.PackUpProblem(c) == null;
        }

        /// <summary>A tap on a chest / workbench (let go before the hold finished): what E did before - open the chest,
        /// or say what the workbench does.</summary>
        void TapContainer(Container box)
        {
            if (box == null || !box.IsSpawned) return;
            if (box.IsWorkbench)
            {
                // nothing is made AT a bench: it unlocks its tier in the TAB crafting list anywhere in your base
                Hud.Push(box.Team.Value != m_Net.Team.Value ? $"That's the enemy's {box.DisplayName}"
                    : $"Your {box.DisplayName}: its items are in your crafting list ({Binds.Name(Bind.Inventory)}) anywhere in your base");
                return;
            }
            LootTarget = box;
            MenuOpen = true;
            Sfx.Play2D(Sfx.Place, 0.4f);
        }

        /// <summary>Holding E on something to pick up: true while it's being held (or was just let go / finished).</summary>
        bool TickPackUp(Interactable t)
        {
            if (m_PackStart < 0f) return false;
            var obj = m_PackObj;
            bool still = obj != null && obj.IsSpawned && t.Kind == TargetKind.Container && t.Obj == obj && !m_Net.Dead.Value;
            if (!Binds.Held(Bind.Interact))
            {
                // let go early: a tap
                m_PackStart = -1f; m_PackObj = null;
                if (still) TapContainer(obj.GetComponent<Container>());
                return true;
            }
            if (!still) { m_PackStart = -1f; m_PackObj = null; return false; } // looked away / it's gone: called off
            if (PackUpProgress >= 1f)
            {
                m_PackStart = -1f; m_PackObj = null;
                m_Net.PackUpRpc(obj);
                m_VM.Use();
                Sfx.Play2D(Sfx.Pop, 0.6f);
                PackUps++;
            }
            return true;
        }

        /// <summary>For the tests: chests / workbenches this player has asked to pick up by holding E.</summary>
        public int PackUps { get; private set; }

        /// <summary>Riding: E on these does them (loot first); anywhere else it gets you off.</summary>
        static bool LootableFromSaddle(Interactable t, bool carrying)
        {
            if (t.Kind == TargetKind.Ball) return !carrying;
            if (carrying) return false;
            if (t.Kind == TargetKind.WorldItem) return true;
            if (t.Kind == TargetKind.Container && t.Obj != null)
            {
                var c = t.Obj.GetComponent<Container>();
                return c != null && !c.IsWorkbench;
            }
            return false;
        }

        void HandleInteract(Interactable t, bool carrying)
        {
            if (TickPackUp(t)) return;
            if (!Binds.Down(Bind.Interact)) return;
            // Builder: E puts the ball down right in front of you - it's your team's until someone picks it up
            if (carrying && Cfg.Builder) { PlantBall(); return; }
            // on a horse, E loots what you're looking at (items, chests, bags, the ball) first; otherwise it gets you off
            if (m_Net.Riding && !LootableFromSaddle(t, carrying)) { m_Net.DismountRpc(); Sfx.Play2D(Sfx.Pop, 0.4f); return; }
            if (carrying && t.Kind != TargetKind.Door && t.Kind != TargetKind.Machine && t.Kind != TargetKind.Vehicle) return; // hands are full (but you can get on a horse)
            switch (t.Kind)
            {
                case TargetKind.Ball: m_Net.PickupBallRpc(); Sfx.Play2D(Sfx.Pop, 0.5f); break;
                case TargetKind.Door: m_Net.ToggleDoorRpc(t.Obj); break;
                case TargetKind.Container:
                {
                    // an empty chest / workbench of yours: hold E to pick it up (a tap still opens it - on letting go)
                    if (!m_Net.Riding && CanPackUp(t, carrying))
                    {
                        m_PackObj = t.Obj;
                        m_PackStart = Time.time;
                        PackUpName = t.Obj.GetComponent<Container>().DisplayName;
                        break;
                    }
                    TapContainer(t.Obj.GetComponent<Container>());
                    break;
                }
                case TargetKind.Bush: m_Net.PickBerriesRpc(t.Obj); m_VM.Use(); Sfx.Play2D(Sfx.Pop, 0.5f); break;
                case TargetKind.PlayerSpear: m_Net.PullSpearRpc(t.Obj); m_VM.Use(); break;
                case TargetKind.WorldItem: m_Net.PickupItemRpc(t.ItemId); m_VM.Use(); break;
                case TargetKind.SelfSpear: m_Net.PullSpearRpc(m_Net.NetworkObject); Sfx.Play2D(Sfx.Flesh, 0.6f); break;
                case TargetKind.Machine:
                    if (t.MachineTeam != m_Net.Team.Value) { Hud.Push(carrying ? "Put the ball in YOUR machine to win" : "That's the enemy's machine"); break; }
                    Hud.Push(carrying ? "Throw the ball (LMB) into the socket - it snaps in when it gets close" : "Throw the ball into this socket to win. (Craft anywhere in your base with TAB"
                        + (Cfg.HasBaseUpgrades ? "; upgrades are at the upgrade station to the left of it)" : ")"));
                    break;
                case TargetKind.UpgradeStation:
                    // your own upgrade station: the UPGRADES screen (fortify, the wood gen)
                    if (t.MachineTeam != m_Net.Team.Value) { Hud.Push("That's the enemy's upgrade station"); break; }
                    if (!carrying) OpenUpgrades();
                    break;
                case TargetKind.Vehicle:
                    m_Net.MountRpc(t.Obj);
                    Sfx.Play2D(Sfx.Pop, 0.5f);
                    break;
            }
        }

        // ------------------------------------------------------------------ ghosts

        void UpdateGhost(Item held)
        {
            int want = -1;
            if (held == Item.BuildingPlan) want = (int)BuildPiece;
            else if (held == Item.Barrier) want = 100;
            else if (held == Item.Chest) want = 101;
            else if (held == Item.Car) want = 102;
            else if (held == Item.Boat) want = 103; // THEME MAPS
            else if (held == Item.Workbench) want = 104;
            else if (held == Item.Workbench2) want = 105;

            if (want != m_GhostId)
            {
                if (m_Ghost) Destroy(m_Ghost);
                m_Ghost = null;
                m_GhostRenderers.Clear();
                m_GhostId = want;
                if (want >= 0)
                {
                    m_Ghost = new GameObject("Ghost");
                    if (want == 101) Container.CreateVisual(Container.Chest, m_Net.Team.Value, m_Ghost.transform, Art.Ghost(k_GhostOk));
                    else if (want == 104 || want == 105) Container.CreateVisual(want == 105 ? Container.Workbench2 : Container.Workbench, m_Net.Team.Value, m_Ghost.transform, Art.Ghost(k_GhostOk));
                    else if (want == 103) Vehicle.CreateVisual(Vehicle.Boat, m_Ghost.transform, Art.Ghost(k_GhostOk), out _, out _, out _, out _, null, null); // THEME MAPS
                    else if (want == 102) Vehicle.CreateVisual(Vehicle.Car, m_Ghost.transform, Art.Ghost(k_GhostOk), out _, out _, out _, out _, null, null);
                    else Structure.CreateVisual(want == 100 ? PieceType.Barrier : (PieceType)want, 0, m_Ghost.transform, false, Art.Ghost(k_GhostOk), out _);
                    m_Ghost.GetComponentsInChildren(m_GhostRenderers);
                }
            }
            m_GhostOk = false;
            BuildHint = "";
            if (m_Ghost == null) return;

            var ray = CenterRay();
            bool hasHit = Aim(ray, Cfg.BuildRange + 3f, out var hit);
            bool visible;
            string reason = null;
            int team = m_Net.Team.Value;

            if (want >= 100)
            {
                var kind = want == 100 ? Item.Barrier : want == 101 ? Item.Chest : want == 104 ? Item.Workbench : want == 105 ? Item.Workbench2 : want == 103 ? Item.Boat /* THEME MAPS */ : Item.Car;
                visible = hasHit && hit.distance <= Cfg.DeployRange && hit.normal.y > 0.7f;
                if (!visible) reason = "Aim at flat ground nearby";
                else
                {
                    m_GhostPos = hit.point;
                    m_GhostYaw = m_Yaw + (kind == Item.Chest || Workbench.IsBench(kind) ? 180f : 0f); // (their fronts face you)
                    if (kind == Item.Boat) m_GhostPos.y = ThemeMaps.WaterY - 0.1f; // THEME MAPS
                    PlayerNet.FindDeploySpot(kind, team, ref m_GhostPos, m_GhostYaw, out reason);
                    m_Ghost.transform.SetPositionAndRotation(m_GhostPos, Quaternion.Euler(0, m_GhostYaw, 0));
                }
            }
            else
            {
                var t = (PieceType)want;
                int rot = (Mathf.RoundToInt(m_Yaw / 90f) + m_RotOffset) & 3;
                RefreshClientKeys();
                visible = BuildGrid.ComputePlacement(t, ray, hit, hasHit, transform.position.y, rot, out m_GhostKey);
                // walls/doorways/windows and foundations: don't make people aim at the exact bottom edge - look along the
                // ray (eye height or above works) and prefer the nearest spot where the piece can actually go
                if ((BuildGrid.KindOf(t) == PieceKey.KEdge || t == PieceType.Foundation) && (!visible || PlacementProblem(t, m_GhostKey) != null)
                    && FindSmartPlacement(t, ray, hasHit ? hit.distance : Cfg.BuildRange, out var smart))
                {
                    m_GhostKey = smart;
                    visible = true;
                }
                if (t == PieceType.Floor && (!visible || PlacementProblem(t, m_GhostKey) != null) && FindFloorAnyLevel(ray, out var fk))
                {
                    m_GhostKey = fk;
                    visible = true;
                }
                if (!visible) reason = t == PieceType.Floor ? "Look up at where the floor/ceiling should go" : "Aim at the ground or your building";
                else
                {
                    BuildGrid.Pose(t, m_GhostKey, out var pos, out var prot);
                    m_Ghost.transform.SetPositionAndRotation(pos, prot);
                    reason = PlacementProblem(t, m_GhostKey);
                }
            }

            m_GhostOk = visible && reason == null;
            BuildHint = reason ?? "";
            if (m_Ghost.activeSelf != visible) m_Ghost.SetActive(visible);
            var mat = Art.Ghost(m_GhostOk ? k_GhostOk : k_GhostBad);
            foreach (var r in m_GhostRenderers) r.sharedMaterial = mat;
        }

        /// <summary>
        /// Floors / ceilings: try every level the aim ray crosses within reach (not just the one your feet are on), so from
        /// part way up the stairs you can already see the ceiling go green where you're pointing. Nearest valid one wins.
        /// </summary>
        bool FindFloorAnyLevel(Ray ray, out PieceKey key)
        {
            key = default;
            float best = float.MaxValue;
            if (Mathf.Abs(ray.direction.y) < 0.01f) return false;
            for (int l = 1; l <= Cfg.MaxLevel; l++)
            {
                float d = (BuildGrid.LevelY(l) - ray.origin.y) / ray.direction.y;
                if (d < 0.5f || d > Cfg.BuildRange + 1.5f || d >= best) continue;
                var p = ray.origin + ray.direction * d;
                var k = new PieceKey(PieceKey.KFloor, BuildGrid.CellOf(p.x), BuildGrid.CellOf(p.z), l, 0);
                if (PlacementProblem(PieceType.Floor, k) != null) continue;
                best = d;
                key = k;
            }
            return best < float.MaxValue;
        }

        /// <summary>Why a grid piece can't go at `key` (null = it can), judged from what this client can see.</summary>
        string PlacementProblem(PieceType t, PieceKey key)
        {
            int team = m_Net.Team.Value;
            if (!BuildGrid.CanBuildAt(team, key)) return Cfg.Builder ? "You can't build in the enemy base" : "You can only build inside your base area";
            if (BuildGrid.OnBedrock(key)) return "The bedrock is already a foundation";
            if (BuildGrid.IsOccupied(key, m_ClientKeys.Contains)) return "Something is already built there";
            if (!BuildGrid.IsSupported(key, m_ClientKeys.Contains))
                return t == PieceType.Floor ? "Floors need a wall below or a floor next to them" : "Needs a foundation or floor underneath";
            if (m_Net.Count(Cfg.CurrencyItem) < Cfg.PieceWood(t)) return $"Need {Cfg.PieceWood(t)} {Cfg.CurrencyName}";
            return null;
        }

        /// <summary>Walk back along the aim ray from where you're looking and take the first spot where the piece fits:
        /// so it goes where you're pointing (the farthest valid spot), not right next to you. For walls: cell edges the ray
        /// passes over, at the level of that point; for foundations: the cell under the ray.</summary>
        bool FindSmartPlacement(PieceType t, Ray ray, float maxDist, out PieceKey key)
        {
            key = default;
            maxDist = Mathf.Min(maxDist + 0.3f, Cfg.BuildRange);
            var tried = new HashSet<PieceKey>();
            for (float d = maxDist; d >= 0.6f; d -= 0.15f)
            {
                var p = ray.GetPoint(d);
                PieceKey k;
                if (t == PieceType.Foundation)
                {
                    if (p.y > Cfg.BaseY + 3f) break;
                    k = new PieceKey(PieceKey.KFoundation, BuildGrid.CellOf(p.x), BuildGrid.CellOf(p.z), 0, 0);
                }
                else
                {
                    int level = Mathf.Clamp(Mathf.FloorToInt((p.y - Cfg.BaseY + 0.3f) / Cfg.LevelH), 0, Cfg.MaxLevel);
                    k = BuildGrid.NearestEdge(p, level);
                    if (BuildGrid.DistToEdge(p, k) > 0.45f) continue; // only where the ray actually crosses an edge
                }
                if (!tried.Add(k)) continue;
                if (PlacementProblem(t, k) == null) { key = k; return true; }
            }
            return false;
        }

        void RefreshClientKeys()
        {
            m_ClientKeys.Clear();
            foreach (var s in Structure.All)
                if (BuildGrid.TryKeyFromTransform(s.PType, s.transform, out var k)) m_ClientKeys.Add(k);
        }

        // ------------------------------------------------------------------ HUD info

        void UpdateAimText(Interactable t)
        {
            AimText = "";
            if (m_Net.Dead.Value) return;
            if (m_Net.CarryingBall && Cfg.Builder)
            {
                AimText = $"Carrying the ball!  LMB or {Binds.Name(Bind.Interact)}: put it down (it becomes your team's)";
                return;
            }
            if (m_Net.CarryingBall)
            {
                AimText = t.Kind == TargetKind.Machine && t.MachineTeam == m_Net.Team.Value
                    ? "<color=#77ff77>LMB: throw it into the socket!</color>"
                    : "Carrying the ball!  LMB: throw it - it snaps into your machine's socket when it gets close";
                return;
            }
            switch (t.Kind)
            {
                case TargetKind.Ball: AimText = "E: pick up the ball"; return;
                case TargetKind.Door:
                {
                    var s = t.Obj.GetComponent<Structure>();
                    AimText = $"{s.DisplayName} ({Cfg.TeamName[s.Team.Value]})  {s.Health.Value:0}/{s.MaxHp:0}   " + (s.Team.Value == m_Net.Team.Value ? "E: open/close" : "locked");
                    return;
                }
                case TargetKind.Container:
                {
                    var c = t.Obj.GetComponent<Container>();
                    if (c.IsAirdrop) AimText = "<color=#c98bff>Alien Airdrop</color>   E: open";
                    else if (c.IsGamble) AimText = "<color=#7dffb0>Gambling Machine</color>   E: bet DNA - double it or lose it";
                    else if (c.IsWorkbench) AimText = c.Team.Value == m_Net.Team.Value ? $"<color=#8dff9a>{c.DisplayName}</color>   its items are in your crafting list ({Binds.Name(Bind.Inventory)}) in your base" : $"{Cfg.TeamLabel(c.Team.Value)}'s {c.DisplayName}";
                    else if (c.IsBag) AimText = $"{c.DisplayName}   E: open";
                    else AimText = $"Storage Chest ({Cfg.TeamLabel(c.Team.Value)})  {c.Health.Value:0}/{Cfg.ChestHp:0}   E: open"
                        + (m_Net.HeldItem == Item.BuildingPlan && c.Team.Value == m_Net.Team.Value ? "   X: demolish" : "");
                    if (m_PackStart >= 0f) AimText = $"Picking up your {c.DisplayName}...";
                    else if (!m_Net.Riding && CanPackUp(t, m_Net.CarryingBall)) AimText += $"   hold {Binds.Name(Bind.Interact)}: pick up";
                    return;
                }
                case TargetKind.Machine:
                    if (t.MachineTeam != m_Net.Team.Value) AimText = $"{Cfg.TeamName[t.MachineTeam]} alien machine";
                    else AimText = "Your alien machine - the ball goes in the socket" + (Ball.Instance != null && Ball.Instance.SocketTeam.Value == m_Net.Team.Value ? "   <color=#77ff77>(the ball is in!)</color>" : "");
                    return;
                case TargetKind.UpgradeStation:
                    AimText = t.MachineTeam != m_Net.Team.Value ? $"{Cfg.TeamLabel(t.MachineTeam)}'s upgrade station"
                        : $"<color=#b4ffbc><b>UPGRADE STATION</b></color> — {Binds.Name(Bind.Interact)} to open";
                    return;
                case TargetKind.Vehicle:
                {
                    var v = t.Obj.GetComponent<Vehicle>();
                    // a horse's HP only shows once it has been hurt (or while you're holding berries to feed it)
                    bool feeding = v.IsHorse && m_Net.HeldItem == Item.Berry;
                    string hp = v.Hp.Value < v.MaxHp - 0.5f || feeding ? $"  {v.Hp.Value:0}/{v.MaxHp:0} HP" : "";
                    string e = Binds.Name(Bind.Interact);
                    AimText = v.IsHorse ? (v.Saddled.Value ? $"{(v.IsUnicorn ? "Unicorn" : "Horse")}{hp}   {e}: ride" : $"{(v.IsUnicorn ? "Wild Unicorn" : "Wild horse")}{hp}   {e}: saddle and ride ({(m_Net.Count(Item.Saddle) > 0 ? "uses your saddle" : "<color=#ff8888>needs a saddle</color>")})") : $"Wooden car   {e}: drive";
                    if (feeding) AimText += FeedHint(v);
                    return;
                }
                case TargetKind.Bush: AimText = "Berry Bush   E: pick it"; return;
                case TargetKind.PlayerSpear: AimText = "E: pull the spear out"; return;
                case TargetKind.WorldItem: AimText = $"E: pick up {Cfg.ItemName(t.Stack.Id)}" + (t.Stack.Count > 1 ? $" x{t.Stack.Count}" : ""); return;
            }
            if (m_Net.Riding)
            {
                var rv = RidingVehicle;
                AimText = rv != null && rv.IsHorse ? "Riding  ·  WASD + look to steer, Shift gallop, Space jump, E get off" : "Driving  ·  W/S gas & brake, A/D steer, E get out";
                return;
            }
            if (m_Net.HeldItem == Item.BuildingPlan && DemolishMode) AimText = "<color=#ff9f5a>DEMOLISH</color>  LMB on your own piece  ·  hold RMB to change";
            string self = m_Net.StuckSpears.Value > 0 ? $"<color=#ff8888>{m_Net.StuckSpears.Value} spear(s) stuck in you - E: pull out</color>" : "";
            if (!(m_Net.HeldItem == Item.BuildingPlan && DemolishMode)) AimText = self;
            if (!Aim(CenterRay(), 6f, out var hit)) return;
            var no = hit.collider.GetComponentInParent<NetworkObject>();
            if (no == null) return;
            if (no.TryGetComponent(out Structure st))
            {
                AimText = $"{st.DisplayName} ({Cfg.TeamName[st.Team.Value]})  {st.Health.Value:0}/{st.MaxHp:0}";
                if (m_Net.HeldItem == Item.BuildingPlan && st.Team.Value == m_Net.Team.Value)
                {
                    if (st.Tier.Value == 0 && st.Upgradable && !Cfg.WoodMode) AimText += $"   F: upgrade to stone ({Cfg.UpgradeCost(st.PType)} {Cfg.UpgradeName})";
                    AimText += "   X: demolish";
                }
                if (m_Net.HeldItem == Item.Ram && hit.distance <= Cfg.RamRange)
                    AimText += st.Tier.Value >= 1 && st.PType != PieceType.Barrier ? $"   hold LMB: ram down to {Cfg.TierName(st.Tier.Value - 1).ToLower()}" : "   hold LMB: ram to smash";
            }
            // trees and fallen logs show nothing when you point at them (and neither does a player dressed up as one)
            else if (no.TryGetComponent(out ResourceNode n))
            {
                if (!n.IsWood || n.IsBush)
                    AimText = n.IsBush ? "Berry Bush (empty)" : $"{n.DisplayName}  ({n.Amount.Value} stone left)";
            }
            else if (no.TryGetComponent(out PlayerNet p) && p != m_Net)
            {
                if (!p.TreeCamo) AimText = $"{Cfg.TeamName[p.Team.Value]} player";
            }
            else if (no.TryGetComponent(out Vehicle hv) && !hv.IsCar)
            {
                bool feeding = hv.IsHorse && m_Net.HeldItem == Item.Berry && hit.distance <= Cfg.InteractRange + 2f;
                AimText = hv.Hp.Value < hv.MaxHp - 0.5f || hv.IsSlender || feeding ? $"{hv.DisplayName}  {hv.Hp.Value:0}/{hv.MaxHp:0} HP" : hv.DisplayName;
                if (feeding) AimText += FeedHint(hv);
            }
            if (AimText == "") AimText = self;
        }

        /// <summary>What LMB does with berries on this horse.</summary>
        static string FeedHint(Vehicle v) => v.Hp.Value < v.MaxHp - 0.5f
            ? $"   <color=#8dff9a>LMB: feed it a berry (+{Cfg.HorseBerryHeal:0} HP)</color>" : "   <color=#bbbbbb>(full health)</color>";
    }
}
