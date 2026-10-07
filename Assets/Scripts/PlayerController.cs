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
        public int WheelIndex = System.Array.FindIndex(WheelOptions, o => o.Piece == PieceType.Foundation && !o.Demolish); // (it starts on Foundation, like BuildPiece)
        public bool CrossbowAiming { get; private set; }
        /// <summary>Aiming down the revolver's sights (RMB, like the crossbow).</summary>
        public bool RevolverAiming { get; private set; }
        /// <summary>Down the sights of the crossbow or the revolver: slower to turn and to walk, no sprinting.</summary>
        public bool SightsUp => CrossbowAiming || RevolverAiming;

        /// <summary>The build wheel's slices, clockwise from just right of the top (see WheelAngle).</summary>
        public static readonly (string Label, PieceType Piece, bool Demolish, bool Upgrade)[] WheelOptions =
        {
            // clockwise: the ceiling top right, the wall straight out to the right, the stairs under it, DEMOLISH straight
            // down at the bottom (like Rust), the window bottom left, the doorway straight out to the left and the
            // foundation top left. Upgrades are at the upgrade station; they aren't a slice any more
            ("Ceiling", PieceType.Floor, false, false), ("Wall", PieceType.Wall, false, false), ("Stairs", PieceType.Stairs, false, false),
            ("Demolish", PieceType.Wall, true, false),
            ("Window", PieceType.Window, false, false), ("Doorway", PieceType.Doorway, false, false), ("Foundation", PieceType.Foundation, false, false),
        };
        /// <summary>Where a wheel slice's middle is, in degrees clockwise from straight up. The slices are turned half a
        /// slice so the middle one (demolish) sits exactly at the bottom; a slice covers WheelAngle +- half a slice.</summary>
        public static float WheelAngle(int i) => (i + 0.5f) * 360f / WheelOptions.Length;
        /// <summary>Holding the building plan with Demolish picked on the wheel (pieces you could take down light up red).</summary>
        public bool DemolishAiming => m_Net != null && m_Net.HeldItem == Item.BuildingPlan && DemolishMode;
        public string AimText = "", BuildHint = "";
        /// <summary>The scoreboard key is held: the scoreboard is up and the mouse is free.</summary>
        public bool ScoreboardOpen { get; private set; }
        float m_TabDownAt = -1f; // when the scoreboard key went down (a tap = the bag, a hold = the scoreboard)
        public float DrawAmount { get; private set; } // bow draw / spear wind-up, 0..1
        public float RamCharge { get; private set; }  // ram wind-up, 0..1
        public bool Crouching { get; private set; }

        static readonly Color k_GhostBad = new Color(1f, 0.3f, 0.3f, 0.4f);
        /// <summary>The placement preview's colour while it can go there: Settings > Display > World colours > Building plan
        /// preview (ColorSlots.BuildPlan), see-through.</summary>
        public static Color GhostOkColour { get { var c = ColorSlots.BuildPlan.Value; c.a = 0.4f; return c; } }

        PlayerNet m_Net;
        CharacterController m_CC;
        NetworkTransform m_NT;
        Camera m_Cam;
        ViewModel m_VM;

        int m_LookSkipUntil;
        float m_NextDrop;
        float m_HoldUntil;

        /// <summary>Hold the player still (no walking) for a moment: waking up in the Bedwars cryochamber until its doors open.</summary>
        public void HoldStill(float seconds) => m_HoldUntil = Time.time + seconds;
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
        /// <summary>The last two grid pieces this player put down (newest last): the ghost leans towards carrying them on.</summary>
        readonly List<PieceKey> m_RecentPieces = new List<PieceKey>();
        public IReadOnlyList<PieceKey> RecentPieces => m_RecentPieces;
        /// <summary>Remember a piece this player just put down (the placement bias: BuildGrid.PreferRecent).</summary>
        public void NoteBuilt(PieceKey k)
        {
            m_RecentPieces.Remove(k);
            m_RecentPieces.Add(k);
            while (m_RecentPieces.Count > 2) m_RecentPieces.RemoveAt(0);
        }
        /// <summary>For the tests: the ghost went to a slot carrying on your last pieces instead of the one nearest the aim.</summary>
        public bool GhostFromRecent { get; private set; }

        public override void OnNetworkSpawn()
        {
            m_Net = GetComponent<PlayerNet>();
            m_CC = GetComponent<CharacterController>();
            m_NT = GetComponent<NetworkTransform>();
            if (!IsOwner || m_Net.Bot.Value) { enabled = false; return; } // (a bot is run by its BotBrain, not by a player here)
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
            if (arena) m_Net.LobbyReadyRpc(); // in the stadium with the others: the start countdown can run (NetGame.Lobby.cs)
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
            if (m_FoundationGhost) Destroy(m_FoundationGhost);
            DestroyAimPreviews();
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
            if (ShipLobby.Active)
            {
                // the ship lobby (ShipLobby.cs): you sit there - no moving, the mouse free for its buttons
                CloseMenu();
                Paused = false;
                WheelOpen = false;
                ScoreboardOpen = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }
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
            // Tab (the scoreboard key) is two keys in one, like E on a chest: a TAP opens / closes the bag (on letting go,
            // before Cfg.TabHoldTime), HOLDING it past that shows the scoreboard. The inventory key (I) still opens the bag too
            bool tabTap = false;
            if (Binds.Down(Bind.Scoreboard)) m_TabDownAt = Time.time;
            bool tabHeld = m_TabDownAt >= 0f && Binds.Held(Bind.Scoreboard);
            if (m_TabDownAt >= 0f && !tabHeld)
            {
                // (let go - or muted by the chat opening, e.g. the scoreboard's MESSAGE button: that's no tap)
                tabTap = Time.time - m_TabDownAt < Cfg.TabHoldTime && !Binds.Muted && Tutorial.BindAllowed(Bind.Inventory);
                m_TabDownAt = -1f;
            }
            if ((Binds.Down(Bind.Inventory) || tabTap) && !Chat.Open && !dead && !gameOver && !sd && !Hud.Rebinding)
            {
                if (MenuOpen) CloseMenu(); else MenuOpen = true;
                Paused = false;
                WheelOpen = false;
            }
            // hold the scoreboard key (Tab): every player with their kills, deaths and ping, the mouse free for its MESSAGE
            // buttons (Hud.Scoreboard.cs). Typing mutes the key, so clicking MESSAGE (the chat opens) puts it away
            ScoreboardOpen = !cutscene && !Paused && !MenuOpen && !Hud.Rebinding && !AirstrikeMapOpen && tabHeld && Time.time - m_TabDownAt >= Cfg.TabHoldTime;
            if (sd || dead) CloseMenu();
            if (LootTarget != null && (!LootTarget.IsSpawned || !LootTarget.InReach(m_Net.EyePos))) LootTarget = null;
            // the UPGRADES screen goes with the menu, and closes if you walk away from your upgrade station
            if (UpgradesOpen && (!MenuOpen || !Cfg.AtOwnStation(m_Net.Team.Value, transform.position))) { if (MenuOpen) MenuOpen = false; UpgradesOpen = false; }
            if (Paused || MenuOpen || dead) WheelOpen = false;
            TickWatchSync(); // (PlayerController.Watch.cs: tells spectators which screen is open)
            if (dead || m_Net.HeldItem != Item.Airstrike) AirstrikeMapOpen = false;

            bool choosing = m_Net.ChoosingRespawn;
            if (choosing && !Paused)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseRespawn(false);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseRespawn(true);
            }

            // (the chat line frees the mouse too: a whisper in the log can be clicked to answer it)
            bool cursorFree = (MenuOpen || Paused || gameOver || choosing || WheelOpen || AirstrikeMapOpen || ScoreboardOpen || Chat.Open) && !cutscene;
            var lockNow = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
            if (lockNow == CursorLockMode.Locked && Cursor.lockState != CursorLockMode.Locked) m_LookSkipUntil = Time.frameCount + 3; // (re-locking the cursor jumps the mouse: not a look)
            Cursor.lockState = lockNow;
            Cursor.visible = cursorFree && !WheelOpen;
            bool input = !cursorFree && !dead && !Hud.Rebinding && !Chat.Open && !cutscene;
            // you can keep walking, jumping and crouching with the inventory open
            bool frozen = game != null && game.FightFrozen;
            bool move = !dead && !Paused && !gameOver && !frozen && !AirstrikeMapOpen && !Chat.Open && Time.time >= m_HoldUntil; // (held: waking in the cryochamber)
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
            // (in the ship lobby the mouse is for the buttons: your view stays level for when the match starts)
            bool lobby = ShipLobby.Active;
            if (lobby) m_Pitch = 0f;
            if (input && !lobby && Time.frameCount > m_LookSkipUntil)
            {
                m_LookDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * GameSettings.MouseSensitivity * (CrossbowAiming ? 0.6f : RevolverAiming ? 0.75f : 1f) * (Scoped ? 0.35f : 1f);
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
            if (dead || riding) { m_SlideOn = false; m_SlideQueued = -1f; m_SlideVel = Vector3.zero; TickSlideSound(); } // (the slide's hiss stops with it)
            // riding: the crouch keys (Ctrl / C) get you off - E stays free to do everything it does on foot
            // (the tutorial keeps Ctrl / C locked until its crouch and slide steps - but a rider can always get off)
            if (riding && input && !locked && (Binds.Down(Bind.Crouch) || Binds.Down(Bind.Slide) || (Tutorial.On && (Binds.RawDown(Bind.Crouch) || Binds.RawDown(Bind.Slide))))) { m_Net.DismountRpc(); Sfx.Play2D(Sfx.Pop, 0.4f); Dismounts++; }
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
                bool sprint = move && Binds.Held(Bind.Sprint) && fwdInput > 0 && m_DrawStart < 0 && RamCharge <= 0 && (!Crouching || airCrouch) && !SightsUp;
                float speed = Crouching ? Cfg.CrouchSpeed : sprint ? Cfg.SprintSpeed : Cfg.WalkSpeed;
                if (carrying) speed *= Cfg.BallCarrySpeedMul;
                if (m_DrawStart >= 0 || SightsUp) speed *= 0.6f;
                if (held == Item.Ram && !carrying) speed *= Cfg.RamMoveMul;
                speed *= ThemeMaps.SpeedMul(transform.position); // THEME MAPS
                if (m_Net.Juiced) speed *= Cfg.SpeedJuiceMul; // (Extreme Speed Juice)
                if (m_Net.Trapped) speed = 0f; // (caught in a bear trap: held where you stand)
                if (airCrouch) speed = Mathf.Max(speed, m_TakeoffSpeed);

                bool grounded = m_CC.isGrounded;
                if (grounded) m_GroundedAt = Time.time;
                bool ladder = OnLadder();
                // jump buffering: a press shortly before you land still jumps the moment you touch down
                if (move && !TreeLocked && Binds.Down(Bind.Jump)) m_JumpPressedAt = Time.time;
                bool jumpWanted = move && !TreeLocked && !m_Net.Trapped && Time.time - m_JumpPressedAt <= Mathf.Max(Time.deltaTime, Cfg.JumpBuffer);
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
                    if (ld != null && transform.position.y > ld.TopWorldY - 0.15f && up) { m_Push = ld.ExitDir * 3f; m_VelY = Mathf.Max(m_VelY, ld.ExitHop); }
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
                        case Item.SleepingBag:
                        case Item.BearTrap:
                        case Item.Ladder:
                        case Item.AutoTurret:
                        case Item.LargeGate:
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
                        case Item.InvisPotion: case Item.SpeedJuice: HandleUseItem(held); break;
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
            // the revolver comes up to the eye the same way (not while it's being reloaded)
            RevolverAiming = input && !carrying && held == Item.Revolver && Binds.Held(Bind.Aim) && m_PistolReloadStart < 0f;
            Scoped = input && !carrying && held == Item.Sniper && Binds.Held(Bind.Aim);

            PublishAction(held, dead, carrying);

            var target = FindInteract();
            Target = target;
            if (input) HandleInteract(target, carrying);
            else { m_PackStart = -1f; m_PackObj = null; }
            // (the plan's demolish / upgrade modes hide the plan's ghost only: they used to hide every ghost, so after
            // picking one of them you couldn't put down a chest or a station either until you picked a piece again)
            bool planMode = held == Item.BuildingPlan && (DemolishMode || UpgradeMode);
            UpdateGhost(input && !carrying && !gameOver && !riding && !planMode ? held : Item.None);
            UpdateDemolishHighlight(!dead && !gameOver && !cutscene && !riding && DemolishAiming); // (PlayerController.Preview.cs)
            UpdateAimPreviews(input && !carrying && !gameOver && !locked && !frozen ? held : Item.None); // (PlayerController.Preview.cs)
            UpdateAimText(target);
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || m_Cam == null) return;
            // the ship lobby has the camera (ShipLobby.cs) and your hands are put away
            if (ShipLobby.Active) { m_VM.Update(new ViewModel.State { Item = m_Net.HeldItem, Visible = false, Visible2 = false }); return; }
            if (TickKillCam()) { m_VM.Update(new ViewModel.State { Item = m_Net.HeldItem, Visible = false, Visible2 = false }); return; }
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
            float fov = 70f - DrawAmount * 18f + Fx.FovPunch + (m_Net.Juiced ? 14f : 0f); // (Extreme Speed Juice: the view stretches out)
            if (CrossbowAiming) fov = Cfg.CrossbowZoomFov + Fx.FovPunch;
            if (RevolverAiming) fov = Cfg.RevolverZoomFov + Fx.FovPunch;
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
                Aim = SightsUp || Scoped,
                Visible2 = !Scoped,
                Reload = hs.Id == Item.Crossbow && Time.time < m_XbowBusyUntil ? 1f - (m_XbowBusyUntil - Time.time) / Mathf.Max(0.1f, Cfg.CrossbowReload) : hs.Id == Item.Shotgun ? ShotgunReloadProgress : hs.Id == Item.Revolver ? PistolReloadProgress : -1f,
                Rounds = PistolReloadRounds,
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
                else if (SightsUp || Scoped) a = BodyAnimator.Act.Aim;
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
            // the drop key (Q): one of what you're holding goes on the ground in front of you (not the rock: that's your hand)
            if (input && !WheelOpen && Binds.Down(Bind.Drop) && !m_Net.HeldStack.Empty && Time.time >= m_NextDrop)
            {
                m_NextDrop = Time.time + 0.12f;
                m_Net.DropItemRpc(0, (byte)cur, 1, default);
                Sfx.Play2D(Sfx.Throw, 0.35f);
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
            // (a slash from the side: a short draw back, then a long arc across - it lands as the blade crosses the middle)
            float impact = held == Item.Sword ? Mathf.Clamp(Cfg.SwordSwingTime * 0.36f, ViewModel.ImpactTime, 0.75f) : ViewModel.ImpactTime;
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
            else if (no != null && no.TryGetComponent(out Vehicle hv) && (hv.IsHorse || hv.IsSlender))
            {
                // a horse (or Slenderman): a damage number and a hit marker too (the server does the same sum: PlayerNet.MeleeRpc)
                Hud.AnimalHit(hv, hit.point, st.PlayerDamage * hv.HeadMul(hit.point));
                Fx.Shake(0.15f);
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
            else if (EnergyWall.Hit(hit.collider, hit.point, hit.normal)) Fx.Shake(0.12f); // (the energy wall: a crackle of energy, not a thud)
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
                else Hud.Push($"No arrows - craft some in your base ({Binds.Name(Bind.Inventory)})");
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
                if (m_Net.Count(Item.Arrow) == 0) Hud.Push($"No arrows to load - craft some in your base ({Binds.Name(Bind.Inventory)})");
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
            // the cast: the wand flicks out and springs back, the view lurches in after the bolt (its crackle, boom and
            // green flash come with the beam itself - Fx.WandBeam, on every screen)
            m_VM.Use();
            Fx.Kick(3.5f);
            Fx.Shake(0.3f);
            Fx.Punch(-5f);
        }

        /// <summary>Helmet / armour: LMB/RMB puts it on. Potion: LMB/RMB drinks it.</summary>
        void HandleUseItem(Item held)
        {
            if (!(Binds.Down(Bind.Attack) || Binds.Down(Bind.Aim)) || Time.time < m_NextEat) return;
            m_NextEat = Time.time + 0.8f;
            m_Net.UseItemRpc();
            if (held == Item.InvisPotion || held == Item.SpeedJuice) { m_VM.Eat(); Sfx.Play2D(Sfx.Eat, 0.6f); m_EatFlashUntil = Time.time + 0.9f; }
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
            if (WheelOpen) Hud.WheelHover = WheelIndex; // (picked while the wheel is up: it's the slice letting go takes)
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
            // hold RMB: Rust-style wheel of building pieces (+ demolish at the bottom); let go over the one you want
            if (Binds.Down(Bind.Aim)) { WheelOpen = true; Hud.WheelOpened(); }
            if (WheelOpen) return;
            if (Binds.Down(Bind.Rotate)) m_RotOffset = (m_RotOffset + 1) & 3;

            if (Binds.Down(Bind.Attack) && DemolishMode) { if (Tutorial.Allows(TutFeature.Demolish)) TryDemolish(); return; }
            if (Binds.Down(Bind.Attack) && UpgradeMode) { TryUpgrade(); return; }
            if (Binds.Down(Bind.Attack))
            {
                if (m_GhostOk)
                {
                    // bare ground: the foundation first, then the wall on it (PlaceOnNewFoundationRpc charges for both)
                    if (GhostAutoFoundation)
                    {
                        m_Net.PlaceOnNewFoundationRpc((byte)BuildPiece, m_GhostKey.I, m_GhostKey.J, m_GhostKey.D, GhostFoundation.I, GhostFoundation.J);
                        Hud.Push($"Foundation put down under it  (-{Cfg.PieceWood(PieceType.Foundation)} {Cfg.CurrencyName})");
                    }
                    else m_Net.PlaceRpc((byte)BuildPiece, m_GhostKey.I, m_GhostKey.J, m_GhostKey.L, m_GhostKey.D);
                    NoteBuilt(m_GhostKey);
                    m_VM.Use();
                    BuildGrid.Pose(BuildPiece, m_GhostKey, out var pos, out _);
                    Sfx.Play(Sfx.Place, pos, 0.8f);
                }
                else if (!string.IsNullOrEmpty(BuildHint)) Hud.Push(BuildHint);
            }

            // (no X-to-demolish any more: Demolish is the slice at the bottom of the wheel, then LMB - above)

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

            var hits = Physics.RaycastAll(ray, Cfg.InteractRange + Cfg.GateReachExtra + 0.5f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var mount = m_Net.Riding ? RidingVehicle : null;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (mount != null && h.collider.transform.IsChildOf(mount.transform)) continue; // the horse you're sitting on
                var machine = h.collider.GetComponentInParent<Machine>();
                if (machine != null && h.distance <= Cfg.InteractRange + 1.5f)
                {
                    // the ball sitting in this machine's socket: aiming at it or just beside it on the cradle takes the
                    // ball (the machine's parts round the socket used to win, and you couldn't get it out)
                    var sb = Ball.Instance;
                    if (sb != null && !sb.IsCarried && sb.SocketTeam.Value == machine.Team)
                    {
                        var bp = sb.transform.position;
                        var sray = new Ray(m_Cam.transform.position, m_Cam.transform.forward);
                        float along = Vector3.Dot(bp - sray.origin, sray.direction);
                        if (along > 0f && along <= Cfg.InteractRange + 2f && Vector3.Cross(sray.direction, bp - sray.origin).magnitude < 1.1f)
                        {
                            result = new Interactable { Kind = TargetKind.Ball, Obj = sb.NetworkObject };
                            found = along;
                            break;
                        }
                    }
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
                bool gate = kind == TargetKind.Door && obj != null && obj.TryGetComponent(out Structure gs) && gs.PType == PieceType.Gate; // (a large gate opens from further off)
                if (kind != TargetKind.None && h.distance <= Cfg.InteractRange + (gate ? Cfg.GateReachExtra : 0.5f)) { result.Kind = kind; result.Obj = obj; found = h.distance; break; }
                if (h.distance > range) break;
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


        /// <summary>
        /// Large walls and gates like to join up: aimed near the end of one already standing (yours or anyone's), the new
        /// one snaps to carry on from it - in line with it, its end against that one's end - so a row of them makes a
        /// wall with no gaps. Ground under the snapped spot decides its height.
        /// </summary>
        void SnapBigWall(Item kind, ref Vector3 pos, ref float yaw)
        {
            float myHalf = HalfWidth(kind == Item.LargeGate ? PieceType.Gate : PieceType.Barrier);
            float best = Cfg.BigWallSnap;
            Vector3 bestPos = pos;
            float bestYaw = yaw;
            bool found = false;
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || (s.PType != PieceType.Barrier && s.PType != PieceType.Gate)) continue;
                var sp = s.transform.position;
                if (Mathf.Abs(sp.y - pos.y) > 3f || (new Vector2(sp.x - pos.x, sp.z - pos.z)).magnitude > myHalf + HalfWidth(s.PType) + Cfg.BigWallSnap) continue;
                var right = s.transform.right;
                right.y = 0f;
                right.Normalize();
                for (int side = -1; side <= 1; side += 2)
                {
                    var c = sp + right * side * (HalfWidth(s.PType) + myHalf);
                    float d = new Vector2(c.x - pos.x, c.z - pos.z).magnitude;
                    if (d >= best) continue;
                    // (there's no piece already in that spot)
                    bool taken = false;
                    foreach (var o in Structure.All)
                        if (o != null && o != s && (o.PType == PieceType.Barrier || o.PType == PieceType.Gate) && new Vector2(o.transform.position.x - c.x, o.transform.position.z - c.z).magnitude < 1f) { taken = true; break; }
                    if (taken) continue;
                    best = d; bestPos = c; bestYaw = s.transform.eulerAngles.y; found = true;
                }
            }
            if (!found) return;
            if (Physics.Raycast(bestPos + Vector3.up * 4f, Vector3.down, out var g, 10f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) bestPos.y = g.point.y;
            pos = bestPos;
            yaw = bestYaw;
        }

        /// <summary>Half a large wall's or gate's width, end post to end post (m).</summary>
        static float HalfWidth(PieceType t) => t == PieceType.Gate ? Structure.GatePostX + 0.25f : 2.75f;

        TargetKind Classify(Collider col, out NetworkObject obj)
        {
            obj = col.GetComponentInParent<NetworkObject>();
            if (obj == null) return TargetKind.None;
            if (obj.GetComponent<Ball>() != null) return TargetKind.Ball;
            if (obj.TryGetComponent(out Structure s)) return s.HasDoor ? TargetKind.Door : TargetKind.None; // (a doorway with its door broken off is just a hole)
            if (obj.GetComponent<Container>() != null) return TargetKind.Container;
            if (obj.TryGetComponent(out ResourceNode n)) return n.IsBush && n.Amount.Value > 0 ? TargetKind.Bush : TargetKind.None;
            if (obj.TryGetComponent(out PlayerNet p) && p != m_Net && p.StuckSpears.Value > 0) return TargetKind.PlayerSpear;
            if (obj.TryGetComponent(out Vehicle v) && !v.HasDriver && !v.IsDummy) return TargetKind.Vehicle; // (nobody rides a training dummy)
            return TargetKind.None;
        }

        // ---- hold E: pick an empty chest / workbench of yours back up ----
        NetworkObject m_PackObj;
        float m_PackStart = -1f;
        /// <summary>How far through holding E to pick up a chest / workbench (0..1; 0 when not holding). Drawn round the crosshair.
        /// It stays at 0 for the first Cfg.PackUpDelay seconds, so a normal tap of E never flashes the bar.</summary>
        public float PackUpProgress => m_PackStart < 0f ? 0f : Mathf.Clamp01((Time.time - m_PackStart - Cfg.PackUpDelay) / Mathf.Max(0.05f, Cfg.PackUpHoldTime));
        /// <summary>E has been held past the delay: the bar is up (and the aim text says "picking it up...").</summary>
        public bool PackingUp => m_PackStart >= 0f && Time.time - m_PackStart >= Cfg.PackUpDelay;
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
            // a bag, a trap or a ladder: nothing inside (hold E to pick yours up); a turret: only its own team gets at it
            if (box.IsDeployable && box.Kind.Value != Container.Turret) { if (box.Team.Value == m_Net.Team.Value) Hud.Push($"Hold {Binds.Name(Bind.Interact)} to pick up your {box.DisplayName.ToLower()}"); return; }
            if (box.Kind.Value == Container.Turret && box.Team.Value != m_Net.Team.Value) { Hud.Push("That's the enemy's auto turret"); return; }
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

        /// <summary>For the tests: times this player got off a horse / out of a car with the crouch keys.</summary>
        public int Dismounts { get; private set; }

        /// <summary>What to call the get-off keys in hints ("Ctrl / C").</summary>
        static string DismountKeys
        {
            get
            {
                string a = Binds.Name(Bind.Crouch), b = Binds.Name(Bind.Slide);
                return a == "-" ? b : b == "-" || a == b ? a : a + " / " + b;
            }
        }

        void HandleInteract(Interactable t, bool carrying)
        {
            if (TickPackUp(t)) return;
            if (!Binds.Down(Bind.Interact)) return;
            // Builder: E puts the ball down right in front of you - it's your team's until someone picks it up
            if (carrying && Cfg.Builder) { PlantBall(); return; }
            // (on a horse E does all it does on foot; Ctrl / C get you off - see Update)
            if (carrying && t.Kind != TargetKind.Door && t.Kind != TargetKind.Machine && t.Kind != TargetKind.Vehicle) return; // hands are full (but you can get on a horse)
            switch (t.Kind)
            {
                case TargetKind.Ball: m_Net.PickupBallRpc(); Sfx.Play2D(Sfx.Pop, 0.5f); break;
                case TargetKind.Door: m_Net.ToggleDoorRpc(t.Obj); break;
                case TargetKind.Container:
                {
                    // an empty chest / workbench of yours: hold E to pick it up (a tap still opens it - on letting go)
                    if (CanPackUp(t, carrying))
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
                    // Assassin: your own machine takes the skulls you're carrying (NetGame.GameModes.cs)
                    if (Cfg.Assassin && t.MachineTeam == m_Net.Team.Value) m_Net.DepositSkullsRpc();
                    break; // (no top-right message: the look-at line under the crosshair already says what to do with it)
                case TargetKind.UpgradeStation:
                    // your own upgrade station: the UPGRADES screen (fortify, the wood gen)
                    if (t.MachineTeam != m_Net.Team.Value) { Hud.Push("That's the enemy's upgrade station"); break; }
                    if (!Tutorial.Allows(TutFeature.Station)) { Hud.Push("Not yet - the tutorial gets to the upgrade station soon"); break; }
                    if (!carrying) OpenUpgrades();
                    break;
                case TargetKind.Vehicle:
                    m_Net.MountRpc(t.Obj);
                    Sfx.Play2D(Sfx.Pop, 0.5f);
                    break;
            }
        }

        // ------------------------------------------------------------------ ghosts

        /// <summary>The wall ghost is on bare ground and a foundation will be built under it first (in cell GhostFoundation).</summary>
        public bool GhostAutoFoundation { get; private set; }
        public PieceKey GhostFoundation { get; private set; }
        GameObject m_FoundationGhost;
        Color m_FoundationGhostColour;

        /// <summary>The second ghost: the foundation that goes down under a wall aimed at bare ground.</summary>
        void ShowFoundationGhost(bool show)
        {
            if (!show)
            {
                if (m_FoundationGhost != null && m_FoundationGhost.activeSelf) m_FoundationGhost.SetActive(false);
                return;
            }
            if (m_FoundationGhost == null)
            {
                m_FoundationGhost = new GameObject("FoundationGhost");
                Structure.CreateVisual(PieceType.Foundation, 0, m_FoundationGhost.transform, false, Art.Ghost(GhostOkColour), out _);
                m_FoundationGhostColour = GhostOkColour;
            }
            // (the preview colour was changed in Settings > Display since it was made: re-colour it)
            if (m_FoundationGhostColour != GhostOkColour)
            {
                m_FoundationGhostColour = GhostOkColour;
                foreach (var r in m_FoundationGhost.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = Art.Ghost(m_FoundationGhostColour);
            }
            BuildGrid.Pose(PieceType.Foundation, GhostFoundation, out var pos, out var rot);
            m_FoundationGhost.transform.SetPositionAndRotation(pos, rot);
            if (!m_FoundationGhost.activeSelf) m_FoundationGhost.SetActive(true);
        }

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
            else if (held == Item.Ladder) want = 106;
            else if (held == Item.BearTrap) want = 107;
            else if (held == Item.SleepingBag) want = 108;
            else if (held == Item.LargeGate) want = 109;
            else if (held == Item.AutoTurret) want = 110;

            if (want != m_GhostId)
            {
                if (m_Ghost) Destroy(m_Ghost);
                m_Ghost = null;
                m_GhostRenderers.Clear();
                m_GhostId = want;
                if (want >= 0)
                {
                    m_Ghost = new GameObject("Ghost");
                    if (want == 101) Container.CreateVisual(Container.Chest, m_Net.Team.Value, m_Ghost.transform, Art.Ghost(GhostOkColour));
                    else if (want == 104 || want == 105) Container.CreateVisual(want == 105 ? Container.Workbench2 : Container.Workbench, m_Net.Team.Value, m_Ghost.transform, Art.Ghost(GhostOkColour));
                    else if (want >= 106 && want <= 110 && want != 109) Container.CreateVisual(want == 106 ? Container.Ladder : want == 107 ? Container.Trap : want == 108 ? Container.SleepBag : Container.Turret, m_Net.Team.Value, m_Ghost.transform, Art.Ghost(GhostOkColour));
                    else if (want == 109) Structure.CreateVisual(PieceType.Gate, 0, m_Ghost.transform, false, Art.Ghost(GhostOkColour), out _);
                    else if (want == 103) Vehicle.CreateVisual(Vehicle.Boat, m_Ghost.transform, Art.Ghost(GhostOkColour), out _, out _, out _, out _, null, null); // THEME MAPS
                    else if (want == 102) Vehicle.CreateVisual(Vehicle.Car, m_Ghost.transform, Art.Ghost(GhostOkColour), out _, out _, out _, out _, null, null);
                    else Structure.CreateVisual(want == 100 ? PieceType.Barrier : (PieceType)want, 0, m_Ghost.transform, false, Art.Ghost(GhostOkColour), out _);
                    m_Ghost.GetComponentsInChildren(m_GhostRenderers);
                }
            }
            m_GhostOk = false;
            BuildHint = "";
            bool autoFoundation = false;
            if (m_Ghost == null) { GhostAutoFoundation = false; ShowFoundationGhost(false); return; }

            var ray = CenterRay();
            bool hasHit = Aim(ray, Cfg.BuildRange + 3f, out var hit);
            bool visible;
            string reason = null;
            int team = m_Net.Team.Value;

            if (want >= 100)
            {
                var kind = want == 100 ? Item.Barrier : want == 101 ? Item.Chest : want == 104 ? Item.Workbench : want == 105 ? Item.Workbench2 : want == 103 ? Item.Boat /* THEME MAPS */ : want == 106 ? Item.Ladder : want == 107 ? Item.BearTrap : want == 108 ? Item.SleepingBag : want == 109 ? Item.LargeGate : want == 110 ? Item.AutoTurret : Item.Car;
                bool bigWall = kind == Item.Barrier || kind == Item.LargeGate;
                visible = hasHit && hit.distance <= Cfg.DeployRange + (bigWall ? Cfg.BigWallReachExtra : 0f) && hit.normal.y > 0.7f; // (large walls and gates from a bit further off)
                // a ladder aimed at a wall (a building piece - yours or theirs - or a large wall): it stands straight up
                // against it, on whatever's at its foot, facing the wall
                if (kind == Item.Ladder && hasHit && hit.distance <= Cfg.DeployRange + 1f && Mathf.Abs(hit.normal.y) < 0.5f
                    && hit.collider.GetComponentInParent<Structure>() != null)
                {
                    var flatN = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    var foot = hit.point + flatN * 0.2f;
                    // as high up the wall as you aim (its middle on the crosshair), but never below whatever's under it
                    if (Physics.Raycast(foot + Vector3.up * 0.3f, Vector3.down, out var under, 9f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                    {
                        m_GhostPos = under.point;
                        m_GhostPos.y = Mathf.Max(under.point.y, hit.point.y - Deployables.LadderHeight * 0.5f);
                        // facing the wall - turned a little your way if you're not square on to it (like Rust's)
                        float wallYaw = Quaternion.LookRotation(-flatN).eulerAngles.y;
                        m_GhostYaw = wallYaw + Mathf.Clamp(Mathf.DeltaAngle(wallYaw, m_Yaw), -Deployables.LadderMaxTurn, Deployables.LadderMaxTurn);
                        PlayerNet.FindDeploySpot(kind, team, ref m_GhostPos, m_GhostYaw, out reason);
                        m_Ghost.transform.SetPositionAndRotation(m_GhostPos, Quaternion.Euler(0, m_GhostYaw, 0));
                        visible = true;
                        goto ladderPlaced;
                    }
                }
                if (!visible) reason = kind == Item.Ladder ? "Aim at a wall (or flat ground) nearby" : "Aim at flat ground nearby";
                else
                {
                    m_GhostPos = hit.point;
                    m_GhostYaw = m_Yaw + (kind == Item.Chest || Workbench.IsBench(kind) || kind == Item.SleepingBag ? 180f : 0f); // (their fronts face you; a ladder and a turret face away, towards the wall / where it watches)
                    if (kind == Item.Boat) m_GhostPos.y = ThemeMaps.WaterY - 0.1f; // THEME MAPS
                    if (bigWall) SnapBigWall(kind, ref m_GhostPos, ref m_GhostYaw); // (they like to join up end to end)
                    PlayerNet.FindDeploySpot(kind, team, ref m_GhostPos, m_GhostYaw, out reason);
                    m_Ghost.transform.SetPositionAndRotation(m_GhostPos, Quaternion.Euler(0, m_GhostYaw, 0));
                }
                ladderPlaced:;
            }
            else
            {
                var t = (PieceType)want;
                int rot = (Mathf.RoundToInt(m_Yaw / 90f) + m_RotOffset) & 3;
                RefreshClientKeys();
                visible = BuildGrid.ComputePlacement(t, ray, hit, hasHit, transform.position.y, rot, out m_GhostKey);
                // aimed right at a spot where a piece was just destroyed: the ghost stays on it (red, with the seconds left)
                bool cooling = visible && Cooling(t, m_GhostKey);
                // walls/doorways/windows and foundations: don't make people aim at the exact bottom edge - look along the
                // ray (eye height or above works) and prefer the nearest spot where the piece can actually go
                if (!cooling && (BuildGrid.KindOf(t) == PieceKey.KEdge || t == PieceType.Foundation) && (!visible || PlacementProblem(t, m_GhostKey) != null)
                    && FindSmartPlacement(t, ray, hasHit ? hit.distance : Cfg.BuildRange, out var smart))
                {
                    m_GhostKey = smart;
                    visible = true;
                }
                if (!cooling && t == PieceType.Floor && (!visible || PlacementProblem(t, m_GhostKey) != null) && FindFloorAnyLevel(ray, out var fk))
                {
                    m_GhostKey = fk;
                    visible = true;
                }
                // building in a line: near a slot that carries on from your last two pieces, that one wins
                GhostFromRecent = false;
                if (!cooling && PreferRecentSlot(t, ray, hasHit, hit, visible, out var rk))
                {
                    m_GhostKey = rk;
                    visible = true;
                    GhostFromRecent = true;
                }
                if (!visible) reason = t == PieceType.Floor ? "Look up at where the floor/ceiling should go" : "Aim at the ground or your building";
                else
                {
                    BuildGrid.Pose(t, m_GhostKey, out var pos, out var prot);
                    m_Ghost.transform.SetPositionAndRotation(pos, prot);
                    reason = PlacementProblem(t, m_GhostKey);
                    // bare ground in your base and only the foundation missing: it goes down first, then the wall (both
                    // paid for) - the ghost shows the foundation too
                    if (reason == k_NeedsFoundation && AutoFoundationFor(t, m_GhostKey, hasHit ? hit.point + hit.normal * 0.05f : pos, out var fkey, out reason))
                    {
                        autoFoundation = true;
                        GhostFoundation = fkey;
                    }
                }
            }
            GhostAutoFoundation = autoFoundation;
            ShowFoundationGhost(autoFoundation);

            m_GhostOk = visible && reason == null;
            BuildHint = reason ?? "";
            if (m_Ghost.activeSelf != visible) m_Ghost.SetActive(visible);
            var mat = Art.Ghost(m_GhostOk ? GhostOkColour : k_GhostBad);
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

        /// <summary>
        /// Most people build in a line: when what you aim at is near a slot that carries on one of your last two pieces
        /// (the ones still standing), take that one over the strictly nearest slot (BuildGrid.PreferRecent: a bias of
        /// RecentBias m, never past RecentReach m, so aiming clearly elsewhere still goes there). The aim point is where the
        /// aim ray hits (floors: where it crosses that floor's level, if nothing is in the way first).
        /// </summary>
        bool PreferRecentSlot(PieceType t, Ray ray, bool hasHit, RaycastHit hit, bool visible, out PieceKey key)
        {
            key = default;
            if (m_RecentPieces.Count == 0) return false;
            var recent = new List<PieceKey>(2);
            foreach (var r in m_RecentPieces) if (m_ClientKeys.Contains(r)) recent.Add(r);
            if (recent.Count == 0) return false;
            bool floor = t == PieceType.Floor;
            if (!floor && (!hasHit || hit.distance > Cfg.BuildRange + 0.5f)) return false;
            var feet = transform.position;
            // (a wall slot on bare ground counts as buildable when the auto foundation can go under it - carrying a wall
            // line on over open ground is the usual case)
            var aim = hasHit ? hit.point + hit.normal * 0.05f : ray.GetPoint(Cfg.BuildRange);
            bool Buildable(PieceKey k)
            {
                var p = PlacementProblem(t, k);
                return p == null || (p == k_NeedsFoundation && AutoFoundationFor(t, k, aim, out _, out _));
            }
            bool currentOk = visible && Buildable(m_GhostKey);
            return BuildGrid.PreferRecent(t, recent, m_GhostKey, currentOk, k =>
            {
                if (!floor) return (true, hit.point);
                if (Mathf.Abs(ray.direction.y) < 0.01f) return (false, default);
                float d = (BuildGrid.LevelY(k.L) - ray.origin.y) / ray.direction.y;
                if (d < 0.5f || d > Cfg.BuildRange + 1.5f) return (false, default);
                if (hasHit && hit.distance < d - 0.6f) return (false, default); // (something in the way first)
                return (true, ray.GetPoint(d));
            }, k => Vector3.Distance(BuildGrid.PieceCenter(k), feet) <= Cfg.BuildRange + 2.5f && Buildable(k), out key, ray.direction);
        }

        const string k_NeedsFoundation = "Needs a foundation or floor underneath";

        /// <summary>The aimed slot is where a piece was just destroyed: the ghost stays there (red, counting down) instead of
        /// hopping to some other slot nearby.</summary>
        bool Cooling(PieceType t, PieceKey key) =>
            BuildGrid.CanBuildAt(m_Net.Team.Value, key) && !BuildGrid.IsOccupied(key, m_ClientKeys.Contains) && Structure.RebuildWait(key) > 0f;

        /// <summary>
        /// A wall, doorway or window aimed at bare ground in your base with nothing wrong but the missing foundation: the
        /// cell a foundation can go in to carry it (the one you're aiming into if it can, else the other side of the wall).
        /// False if neither can take one, or you can't pay for both (`reason` says so then).
        /// </summary>
        bool AutoFoundationFor(PieceType t, PieceKey key, Vector3 aim, out PieceKey fkey, out string reason)
        {
            fkey = default;
            reason = k_NeedsFoundation;
            if (key.Kind != PieceKey.KEdge || key.L != 0 || Cfg.Builder) return false;
            int i2 = key.D == 0 ? key.I + 1 : key.I, j2 = key.D == 0 ? key.J : key.J + 1;
            var a = new PieceKey(PieceKey.KFoundation, key.I, key.J, 0, 0);
            var b = new PieceKey(PieceKey.KFoundation, i2, j2, 0, 0);
            if (BuildGrid.CellOf(aim.x) == i2 && BuildGrid.CellOf(aim.z) == j2) { var tmp = a; a = b; b = tmp; }
            int both = Cfg.PieceWood(PieceType.Foundation) + Cfg.PieceWood(t);
            foreach (var f in new[] { a, b })
            {
                int team = m_Net.Team.Value;
                if (!BuildGrid.CanBuildAt(team, f) || BuildGrid.OnBedrock(f) || BuildGrid.IsOccupied(f, m_ClientKeys.Contains) || Structure.RebuildWait(f) > 0f) continue;
                if (m_Net.Count(Cfg.CurrencyItem) < both)
                {
                    reason = $"Needs a foundation underneath - {both} {Cfg.CurrencyName} for the foundation and the {Cfg.PieceName(t).ToLower()}";
                    return false;
                }
                fkey = f;
                reason = null;
                return true;
            }
            return false;
        }

        /// <summary>Why a grid piece can't go at `key` (null = it can), judged from what this client can see.</summary>
        string PlacementProblem(PieceType t, PieceKey key)
        {
            int team = m_Net.Team.Value;
            if (!BuildGrid.CanBuildAt(team, key)) return Cfg.Builder ? "You can't build in the enemy base" : "You can only build inside your base area";
            if (BuildGrid.OnBedrock(key)) return "The bedrock is already a foundation";
            if (BuildGrid.IsOccupied(key, m_ClientKeys.Contains)) return "Something is already built there";
            // a piece was destroyed here a moment ago: not yet (the server says the same)
            float wait = Structure.RebuildWait(key);
            if (wait > 0f) return Structure.RebuildWaitText(wait);
            if (!BuildGrid.IsSupported(key, m_ClientKeys.Contains))
                return t == PieceType.Floor ? "Floors need a wall below or a floor next to them" : k_NeedsFoundation;
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
            // (carrying the ball: nothing here - the top of the screen already says what to do with it)
            if (m_Net.CarryingBall) return;
            switch (t.Kind)
            {
                // (every look-at line is Tip / TeamTip - PlayerController.Tips.cs: the name in capitals and a colour that
                // says what or whose it is, a dash, the details)
                case TargetKind.Ball: AimText = Tip("The ball", TipGold, $"{Binds.Name(Bind.Interact)}: pick it up"); return;
                case TargetKind.Door:
                {
                    var s = t.Obj.GetComponent<Structure>();
                    AimText = TeamTip(s.DisplayName, s.Team.Value, $"{s.Health.Value:0}/{s.MaxHp:0}  ·  door {s.DoorHealth.Value:0}/{s.DoorMaxHp:0}   "
                        + (s.Team.Value == m_Net.Team.Value ? "E: open/close" : "locked - only its team can open it (the door itself breaks easier than the frame)"));
                    return;
                }
                case TargetKind.Container:
                {
                    var c = t.Obj.GetComponent<Container>();
                    string detail;
                    if (c.IsAirdrop) detail = "E: open";
                    else if (c.IsGamble) detail = "E: bet DNA - double it or lose it";
                    else if (c.IsWorkbench) detail = c.Team.Value == m_Net.Team.Value ? $"its items are in your crafting list ({Binds.Name(Bind.Inventory)}) in your base" : "";
                    else if (c.IsBag) detail = "E: open";
                    // the placeables that don't open: just their health (the turret: its weapon and ammo)
                    else if (c.IsDeployable)
                        detail = $"{c.Health.Value:0}/{Deployables.MaxHp(c.Kind.Value):0}" + (c.Kind.Value == Container.Turret && c.Team.Value == m_Net.Team.Value ? $"   {Binds.Name(Bind.Interact)}: weapon and ammo" : "")
                            + (c.Kind.Value == Container.Trap && c.Flag.Value == 1 ? "   <color=#ff8a7a>sprung</color>" : "") + (DemolishAiming && c.Team.Value == m_Net.Team.Value ? "   <color=#ff8a7a>LMB: demolish</color>" : "");
                    else detail = $"{c.Health.Value:0}/{Cfg.ChestHp:0}   E: open" + (DemolishAiming && c.Team.Value == m_Net.Team.Value ? "   <color=#ff8a7a>LMB: demolish</color>" : "");
                    if (PackingUp) detail = "picking it up...";
                    else if (CanPackUp(t, m_Net.CarryingBall)) detail += $"   hold {Binds.Name(Bind.Interact)}: pick up";
                    // an airdrop is purple, the gambling machine its green; a chest, bench or loot bag is its team's colour
                    AimText = c.IsAirdrop ? Tip(c.DisplayName, TipAirdrop, detail) : c.IsGamble ? Tip(c.DisplayName, TipGamble, detail)
                        : TeamTip(c.IsBag ? "Loot bag" : c.DisplayName, c.Team.Value, detail);
                    return;
                }
                case TargetKind.Machine when Cfg.Bedwars || Cfg.Assassin:
                {
                    // Bedwars: its state (and smash the enemy's); Assassin: hand skulls in at yours
                    var g = NetGame.Instance;
                    bool mine = t.MachineTeam == m_Net.Team.Value;
                    string d = Cfg.Bedwars
                        ? (g != null && g.MachineDown(t.MachineTeam) ? "<color=#ff6666>destroyed</color>" : mine ? $"your cryochamber - {Cfg.MachineHitsToBreak - (g != null ? g.HitsOn(t.MachineTeam) : 0)} hits left" : $"smash it! {Cfg.MachineHitsToBreak - (g != null ? g.HitsOn(t.MachineTeam) : 0)} hits left")
                        : mine ? $"{Binds.Name(Bind.Interact)}: hand in your skulls ({m_Net.Count(Item.Skull)})   {(g != null ? g.SkullsOf(m_Net.Team.Value) : 0)} in" : "";
                    AimText = TeamTip("Alien machine", t.MachineTeam, d);
                    return;
                }
                case TargetKind.Machine:
                    AimText = TeamTip("Alien machine", t.MachineTeam, t.MachineTeam != m_Net.Team.Value ? ""
                        : "the ball goes in the socket" + (Ball.Instance != null && Ball.Instance.SocketTeam.Value == m_Net.Team.Value ? "   <color=#77ff77>(the ball is in!)</color>" : ""));
                    return;
                case TargetKind.UpgradeStation:
                    AimText = TeamTip("Upgrade station", t.MachineTeam, t.MachineTeam != m_Net.Team.Value ? "" : $"{Binds.Name(Bind.Interact)} to open");
                    return;
                case TargetKind.Vehicle:
                {
                    var v = t.Obj.GetComponent<Vehicle>();
                    // a horse's HP only shows once it has been hurt (or while you're holding berries to feed it)
                    bool feeding = v.IsHorse && m_Net.HeldItem == Item.Berry;
                    string hp = v.Hp.Value < v.MaxHp - 0.5f || feeding ? $"{v.Hp.Value:0}/{v.MaxHp:0} HP   " : "";
                    string e = Binds.Name(Bind.Interact);
                    AimText = v.IsHorse ? (v.Saddled.Value ? Tip(v.IsUnicorn ? "Unicorn" : "Horse", VehicleTipColor(v), $"{hp}{e}: ride")
                            : Tip(v.IsUnicorn ? "Wild Unicorn" : "Wild horse", VehicleTipColor(v), $"{hp}{e}: saddle and ride ({(m_Net.Count(Item.Saddle) > 0 ? "uses your saddle" : "<color=#ff8888>needs a saddle</color>")})"))
                        : Tip("Wooden car", VehicleTipColor(v), $"{e}: drive");
                    if (feeding) AimText += FeedHint(v);
                    return;
                }
                case TargetKind.Bush: AimText = Tip("Berry Bush", TipPlant, $"{Binds.Name(Bind.Interact)}: pick it"); return;
                case TargetKind.PlayerSpear: AimText = Tip("Stuck spear", TipPlain, $"{Binds.Name(Bind.Interact)}: pull it out"); return;
                case TargetKind.WorldItem: AimText = Tip(Cfg.ItemName(t.Stack.Id) + (t.Stack.Count > 1 ? $" x{t.Stack.Count}" : ""), TipPlain, $"{Binds.Name(Bind.Interact)}: pick up"); return;
            }
            if (m_Net.Riding)
            {
                var rv = RidingVehicle;
                AimText = rv != null && rv.IsHorse ? $"Riding  ·  WASD + look to steer, Shift gallop, Space jump, {DismountKeys} get off" : $"Driving  ·  W/S gas & brake, A/D steer, {DismountKeys} get out";
                return;
            }
            if (m_Net.HeldItem == Item.BuildingPlan && DemolishMode) AimText = "<color=#ff9f5a>DEMOLISH</color>  LMB on your own piece  ·  hold RMB to change";
            string self = m_Net.StuckSpears.Value > 0 ? $"<color=#ff8888>{m_Net.StuckSpears.Value} spear(s) stuck in you - E: pull out</color>" : "";
            if (!(m_Net.HeldItem == Item.BuildingPlan && DemolishMode)) AimText = self;
            if (!Aim(CenterRay(), 6f, out var hit)) return;
            // the wood machine (Auto Wood, once it's been bought): the rate it's making wood at, in the upgrade station's style
            var woodMachine = hit.collider.GetComponentInParent<WoodMachine>();
            if (woodMachine != null)
            {
                if (woodMachine.Built && string.IsNullOrEmpty(AimText)) AimText = woodMachine.AimLine(m_Net.Team.Value);
                return;
            }
            var no = hit.collider.GetComponentInParent<NetworkObject>();
            if (no == null) return;
            if (no.TryGetComponent(out Structure st))
            {
                // (WOODEN DOORWAY in its team's colour - not "Wooden Doorway (BLUE)")
                string detail = $"{st.Health.Value:0}/{st.MaxHp:0}" + (st.HasBars ? $"  ·  bars {st.DoorHealth.Value:0}/{st.DoorMaxHp:0}" : ""); // (a window: its bars have their own, like a door)
                if (m_Net.HeldItem == Item.BuildingPlan && st.Team.Value == m_Net.Team.Value)
                {
                    if (st.Tier.Value == 0 && st.Upgradable && !Cfg.WoodMode) detail += $"   F: upgrade to stone ({Cfg.UpgradeCost(st.PType)} {Cfg.UpgradeName})";
                    if (DemolishMode) detail += "   <color=#ff8a7a>LMB: demolish</color>";
                }
                if (m_Net.HeldItem == Item.Ram && hit.distance <= Cfg.RamRange)
                    detail += st.Tier.Value >= 1 && st.PType != PieceType.Barrier ? $"   hold LMB: ram down to {Cfg.TierName(st.Tier.Value - 1).ToLower()}" : "   hold LMB: ram to smash";
                AimText = TeamTip(st.DisplayName, st.Team.Value, detail);
            }
            // trees and fallen logs show nothing when you point at them (and neither does a player dressed up as one)
            else if (no.TryGetComponent(out ResourceNode n))
            {
                if (!n.IsWood || n.IsBush)
                    AimText = n.IsBush ? Tip("Berry Bush", TipPlant, "empty") : Tip(n.DisplayName, TipStone, $"{n.Amount.Value} stone left");
            }
            else if (no.TryGetComponent(out PlayerNet p) && p != m_Net)
            {
                if (!p.TreeCamo) AimText = TeamTip(p.DisplayName, p.Team.Value); // (their name, in their team's colour)
            }
            else if (no.TryGetComponent(out Vehicle hv) && !hv.IsCar)
            {
                bool feeding = hv.IsHorse && m_Net.HeldItem == Item.Berry && hit.distance <= Cfg.InteractRange + 2f;
                AimText = Tip(hv.DisplayName, VehicleTipColor(hv), hv.Hp.Value < hv.MaxHp - 0.5f || hv.IsSlender || feeding ? $"{hv.Hp.Value:0}/{hv.MaxHp:0} HP" : "");
                if (feeding) AimText += FeedHint(hv);
            }
            if (AimText == "") AimText = self;
        }

        /// <summary>What LMB does with berries on this horse.</summary>
        static string FeedHint(Vehicle v) => v.Hp.Value < v.MaxHp - 0.5f
            ? $"   <color=#8dff9a>LMB: feed it a berry (+{Cfg.HorseBerryHeal:0} HP)</color>" : "   <color=#bbbbbb>(full health)</color>";
    }
}
