using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace RockGame
{
    /// <summary>Local (owner-only) input, first-person camera, movement, item use, interaction and placement ghosts.</summary>
    public class PlayerController : NetworkBehaviour
    {
        public static PlayerController Local;

        public bool MenuOpen, Paused;
        public Container LootTarget;
        public PieceType BuildPiece = PieceType.Foundation;
        public string AimText = "", BuildHint = "";
        public float DrawAmount { get; private set; } // bow draw / spear wind-up, 0..1
        public float RamCharge { get; private set; }  // ram wind-up, 0..1
        public bool Crouching { get; private set; }

        const float k_Sensitivity = 2f;
        static readonly PieceType[] k_Pieces = { PieceType.Foundation, PieceType.Wall, PieceType.Doorway, PieceType.Floor, PieceType.Stairs };
        static readonly Color k_GhostOk = new Color(0.3f, 1f, 0.45f, 0.4f), k_GhostBad = new Color(1f, 0.3f, 0.3f, 0.4f);

        PlayerNet m_Net;
        CharacterController m_CC;
        NetworkTransform m_NT;
        Camera m_Cam;
        ViewModel m_VM;

        float m_Yaw, m_Pitch, m_VelY, m_Bob, m_Eye = Cfg.EyeHeight, m_LastStep, m_Speed;
        float m_NextSwing, m_ImpactAt = -1f, m_NextBuild, m_NextUpgrade, m_DrawStart = -1f, m_NextEat, m_NextBallThrow, m_LastHealth;
        Item m_ImpactItem;
        int m_RotOffset;
        bool m_WasDead, m_Placed;
        float m_InputLockUntil;
        Vector2 m_LookDelta;

        GameObject m_Ghost;
        int m_GhostId = -1;
        bool m_GhostOk;
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
            m_VM = new ViewModel(m_Cam.transform, Cfg.TeamColor[Mathf.Clamp(m_Net.Team.Value, 0, 1)]);
            m_LastHealth = m_Net.Health.Value;
            TryPlace();
        }

        /// <summary>Put the player in their cryo chamber once the host's map is built here.</summary>
        void TryPlace()
        {
            var game = NetGame.Instance;
            if (game == null || !game.IsSpawned || !MapBuilder.IsBuilt(game.MapKey.Value, game.MapSeed.Value)) return;
            m_Placed = true;
            bool sd = game.S == GameState.SuddenDeath;
            NetGame.SpawnPoint(m_Net.Team.Value, sd, out var pos, out var yaw);
            LocalTeleport(pos, yaw);
            if (!sd) WakeUp();
        }

        /// <summary>Bioshock-style: the chamber glass slides up in a burst of steam and you step out.</summary>
        void WakeUp()
        {
            Cryo.Open(m_Net.Team.Value);
            Hud.Wake();
            m_InputLockUntil = Time.time + 0.6f;
            SelectSlotOf(Item.Rock);
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
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            if (!m_Placed) { TryPlace(); if (!m_Placed) return; }
            var game = NetGame.Instance;
            bool dead = m_Net.Dead.Value;
            bool gameOver = game != null && game.S == GameState.GameOver;
            bool sd = game != null && game.S == GameState.SuddenDeath;
            bool carrying = m_Net.CarryingBall;

            // ---- menus ----
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (MenuOpen) CloseMenu();
                else Paused = !Paused;
            }
            if (Input.GetKeyDown(KeyCode.Tab) && !dead && !gameOver && !sd)
            {
                if (MenuOpen) CloseMenu(); else MenuOpen = true;
                Paused = false;
            }
            if (sd || dead) CloseMenu();
            if (LootTarget != null && (!LootTarget.IsSpawned || !LootTarget.InReach(m_Net.EyePos))) LootTarget = null;
            if (Paused && Input.GetMouseButtonDown(0) && !Hud.MouseOverUI) Paused = false;

            bool cursorFree = MenuOpen || Paused || gameOver;
            Cursor.lockState = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = cursorFree;
            bool input = !cursorFree && !dead;
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
                m_LookDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * k_Sensitivity;
                m_Yaw += m_LookDelta.x;
                m_Pitch = Mathf.Clamp(m_Pitch - m_LookDelta.y, -89f, 89f);
            }
            transform.rotation = Quaternion.Euler(0, m_Yaw, 0);
            if (Mathf.Abs(m_Net.Pitch.Value - m_Pitch) > 1f) m_Net.Pitch.Value = m_Pitch;

            var held = SelectItem(input, sd);

            // ---- crouch ----
            bool wantCrouch = input && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C));
            if (!wantCrouch && Crouching && !HeadroomToStand()) wantCrouch = true;
            if (dead) wantCrouch = false;
            if (wantCrouch != Crouching)
            {
                Crouching = wantCrouch;
                m_Net.Crouch.Value = wantCrouch;
            }

            // ---- move ----
            m_Speed = 0;
            if (!dead && m_CC.enabled)
            {
                Vector3 wish = Vector3.zero;
                float fwdInput = 0;
                if (input)
                {
                    float h = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
                    fwdInput = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
                    wish = transform.right * h + transform.forward * fwdInput;
                    if (wish.sqrMagnitude > 1f) wish.Normalize();
                }
                bool sprint = input && Input.GetKey(KeyCode.LeftShift) && fwdInput > 0 && m_DrawStart < 0 && RamCharge <= 0 && !Crouching;
                float speed = Crouching ? Cfg.CrouchSpeed : sprint ? Cfg.SprintSpeed : Cfg.WalkSpeed;
                if (carrying) speed *= Cfg.BallCarrySpeedMul;
                if (m_DrawStart >= 0) speed *= 0.6f;
                if (held == Item.Ram && !carrying) speed *= Cfg.RamMoveMul;

                bool grounded = m_CC.isGrounded;
                if (grounded)
                {
                    if (m_VelY < 0) m_VelY = -2f;
                    if (input && Input.GetKeyDown(KeyCode.Space) && !Crouching) m_VelY = Cfg.JumpSpeed;
                }
                m_VelY -= Cfg.Gravity * Time.deltaTime;
                var flags = m_CC.Move((wish * speed + Vector3.up * m_VelY) * Time.deltaTime);
                if ((flags & CollisionFlags.Above) != 0 && m_VelY > 0) m_VelY = 0;
                m_Speed = wish.magnitude * speed;
                m_Bob += m_Speed * Time.deltaTime;
                if (grounded && m_Speed > 0.5f && m_Bob - m_LastStep > (Crouching ? 2.6f : 1.9f))
                {
                    m_LastStep = m_Bob;
                    Sfx.Play2D(Sfx.Step, Crouching ? 0.12f : 0.3f, 0.2f);
                }

                if (transform.position.y < -30f)
                {
                    NetGame.SpawnPoint(m_Net.Team.Value, sd, out var p, out var y);
                    LocalTeleport(p, y);
                }
            }

            // ---- actions ----
            if (m_ImpactAt >= 0 && Time.time >= m_ImpactAt) DoImpact();
            if (input && !gameOver && !locked)
            {
                if (carrying) HandleBall();
                else
                {
                    switch (held)
                    {
                        case Item.Spear: HandleSpear(); break;
                        case Item.Bow: HandleBow(); break;
                        case Item.BuildingPlan: HandleBuildInput(); break;
                        case Item.Ram: HandleRam(); break;
                        case Item.Chest:
                        case Item.Barrier: HandleDeploy(held); break;
                        case Item.Berry: HandleBerry(); break;
                        default: if (Cfg.IsMelee(held)) HandleMelee(held); break;
                    }
                }
            }
            bool drawing = input && !carrying && !gameOver && (held == Item.Bow || held == Item.Spear);
            if (!drawing) m_DrawStart = -1f;
            float drawTime = held == Item.Spear ? Cfg.SpearDrawTime : Cfg.BowDrawTime;
            DrawAmount = m_DrawStart >= 0 ? Mathf.Clamp01((Time.time - m_DrawStart) / Mathf.Max(0.05f, drawTime)) : 0f;
            if (!input || carrying || gameOver || held != Item.Ram || !Input.GetMouseButton(0)) RamCharge = 0f;

            var target = FindInteract();
            if (input) HandleInteract(target, carrying);
            UpdateGhost(input && !carrying && !gameOver ? held : Item.None);
            UpdateAimText(target);
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || m_Cam == null) return;
            Fx.TickCamera(Time.deltaTime);
            m_Eye = Mathf.Lerp(m_Eye, m_Net.EyeHeight, Time.deltaTime * 12f);
            var rot = Quaternion.Euler(m_Pitch, m_Yaw, 0) * Quaternion.Euler(Fx.ShakeEuler());
            m_Cam.transform.SetPositionAndRotation(transform.position + Vector3.up * m_Eye, rot);
            m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, 70f - DrawAmount * 18f + Fx.FovPunch, Time.deltaTime * 12f);

            m_VM.Update(new ViewModel.State
            {
                Item = m_Net.HeldItem,
                Ball = m_Net.CarryingBall,
                Visible = !m_Net.Dead.Value,
                SpearAim = (m_DrawStart >= 0 || DebugDraw >= 0) && m_Net.HeldItem == Item.Spear,
                HasArrow = m_Net.Count(Item.Arrow) > 0,
                Draw = DebugDraw >= 0 ? DebugDraw : DrawAmount,
                RamCharge = RamCharge,
                Bob = m_Bob,
                Speed = m_Speed,
                Look = m_LookDelta,
                Crouch = Crouching,
            });
        }

        bool HeadroomToStand()
        {
            var p = transform.position;
            var hits = Physics.OverlapCapsule(p + Vector3.up * (Cfg.CrouchHeight + 0.05f), p + Vector3.up * (Cfg.StandHeight - 0.4f), 0.36f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!h.transform.IsChildOf(transform)) return false;
            return true;
        }

        // ------------------------------------------------------------------ items

        void SelectSlotOf(Item id)
        {
            int s = m_Net.HotbarSlotOf(id);
            if (s >= 0) m_Net.HeldSlot.Value = (byte)s;
        }

        /// <summary>Hotbar selection skips empty slots - you always hold something (at least the rock).</summary>
        Item SelectItem(bool input, bool sd)
        {
            int cur = m_Net.HeldSlot.Value;
            int want = cur;
            if (input)
            {
                for (int k = 0; k < Cfg.HotbarSize; k++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + k) && !m_Net.SlotAt(k).Empty) want = k;
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0)
                {
                    int dir = scroll < 0 ? 1 : -1;
                    for (int n = 1; n <= Cfg.HotbarSize; n++)
                    {
                        int i = ((want + dir * n) % Cfg.HotbarSize + Cfg.HotbarSize) % Cfg.HotbarSize;
                        if (!m_Net.SlotAt(i).Empty) { want = i; break; }
                    }
                }
            }
            if (m_Net.SlotAt(want).Empty || (sd && m_Net.SlotAt(want).Id != Item.Rock))
            {
                int r = m_Net.HotbarSlotOf(Item.Rock);
                if (r < 0) for (int i = 0; i < Cfg.HotbarSize && r < 0; i++) if (!m_Net.SlotAt(i).Empty) r = i;
                if (r >= 0) want = r;
            }
            if (want != cur)
            {
                m_Net.HeldSlot.Value = (byte)want;
                m_ImpactAt = -1f;
                m_DrawStart = -1f;
                RamCharge = 0f;
            }
            return m_Net.SlotAt(want).Id;
        }

        Ray CenterRay() => new Ray(transform.position + Vector3.up * m_Eye, Quaternion.Euler(m_Pitch, m_Yaw, 0) * Vector3.forward);

        /// <summary>
        /// Aim assist like most melee games: if the exact ray misses a player but a fat sphere along it
        /// touches one (and nothing solid is in front), count it as hitting that player.
        /// </summary>
        bool AimWithAssist(Ray ray, float range, float radius, out RaycastHit best)
        {
            bool exact = Aim(ray, range, out best);
            if (exact && best.collider.GetComponentInParent<PlayerNet>() != null) return true;
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
                if (p == null || p.Dead.Value || h.distance > limit) continue;
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
            if (!Input.GetMouseButton(0) || Time.time < m_NextSwing) return;
            var st = Cfg.Melee(held);
            m_NextSwing = Time.time + st.Cooldown;
            m_VM.Swing(st.Cooldown);
            Sfx.Play2D(Sfx.Swing, 0.35f, 0.15f);
            m_ImpactAt = Time.time + ViewModel.ImpactTime;
            m_ImpactItem = held;
        }

        void DoImpact()
        {
            m_ImpactAt = -1f;
            if (m_Net.HeldItem != m_ImpactItem || m_Net.CarryingBall || m_Net.Dead.Value) return;
            var st = Cfg.Melee(m_ImpactItem);
            var ray = CenterRay();
            Fx.Kick(0.8f);
            if (!AimWithAssist(ray, st.Range, Cfg.MeleeAssist, out var hit))
            {
                m_VM.Impact(false);
                m_Net.MeleeRpc(false, default, Vector3.zero, false);
                return;
            }
            m_VM.Impact(true);

            var no = hit.collider.GetComponentInParent<NetworkObject>();
            bool weak = false;
            if (no != null && no.TryGetComponent(out PlayerNet p) && p != m_Net && !p.Dead.Value)
            {
                bool head = p.IsHeadshot(hit.point);
                float dmg = st.PlayerDamage * (head ? Cfg.HeadshotMul : 1f);
                Fx.Blood(hit.point, ray.direction, head);
                Fx.DamageNumber(hit.point, dmg, head);
                Hud.HitMarker(false, head);
                Fx.Shake(head ? 0.3f : 0.18f);
                Fx.Punch(head ? -3f : -1.5f);
            }
            else if (no != null && no.TryGetComponent(out ResourceNode n) && !n.IsBush)
            {
                weak = n.IsWeakSpotHit(hit.point, 0.45f);
                Fx.Play(n.Kind.Value == ResourceNode.Tree ? FxKind.WoodChips : FxKind.StoneChips, hit.point, hit.normal);
                if (weak) { Fx.Play(FxKind.WeakSpot, hit.point, hit.normal); Fx.Punch(-1.5f); }
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
            if (Input.GetMouseButtonDown(1)) m_DrawStart = Time.time;
            if (!Input.GetMouseButton(1)) m_DrawStart = -1f;
            if (m_DrawStart < 0) { HandleMelee(Item.Spear); return; }

            if (Input.GetMouseButtonDown(0) && Time.time >= m_NextSwing)
            {
                float t = Time.time - m_DrawStart;
                if (t < Cfg.SpearMinDraw) return;
                float power = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(t / Mathf.Max(0.05f, Cfg.SpearDrawTime)));
                var ray = CenterRay();
                Vector3 origin = ray.origin + ray.direction * 0.8f;
                Vector3 vel = ray.direction * Cfg.SpearThrowSpeed * power;
                ArrowProjectile.SpawnSpear(origin, vel, m_Net, true);
                m_Net.ThrowSpearRpc(origin, vel);
                m_DrawStart = -1f;
                m_NextSwing = Time.time + 0.6f;
                m_VM.Throw();
                Sfx.Play2D(Sfx.Throw, 0.6f);
                Fx.Kick(2f);
            }
        }

        void HandleBow()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (m_Net.Count(Item.Arrow) > 0) m_DrawStart = Time.time;
                else Hud.Push("No arrows - craft some (TAB)");
            }
            if (Input.GetMouseButtonDown(1)) m_DrawStart = -1f;
            if (Input.GetMouseButtonUp(0) && m_DrawStart >= 0)
            {
                float t = Time.time - m_DrawStart;
                m_DrawStart = -1f;
                if (t < Cfg.BowMinDraw || m_Net.Count(Item.Arrow) <= 0) return;
                float power = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(t / Mathf.Max(0.05f, Cfg.BowDrawTime)));
                var ray = CenterRay();
                Vector3 origin = ray.origin + ray.direction * 0.6f;
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
            if (!Input.GetMouseButton(0)) return;
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
                int team = s != null ? s.Team.Value : c != null && !c.IsBag ? c.Team.Value : -1;
                if (team >= 0 && team != m_Net.Team.Value)
                {
                    m_Net.RamStrikeRpc(no, hit.point);
                    Fx.Play(FxKind.Smash, hit.point, hit.normal);
                    Fx.Shake(0.55f);
                    return;
                }
                if (team == m_Net.Team.Value) { Hud.Push("That's your own building"); return; }
            }
            Sfx.Play(Sfx.Thud, transform.position + transform.forward, 0.5f);
            Hud.Push("The ram only works on enemy buildings - get right up to one");
        }

        void HandleBall()
        {
            if (!Input.GetMouseButtonDown(0) || Time.time < m_NextBallThrow) return;
            m_NextBallThrow = Time.time + 0.5f;
            m_NextSwing = Time.time + 0.7f; // no instant swing with whatever comes back into your hands
            m_Net.ThrowBallRpc(CenterRay().direction);
            m_VM.Throw();
            Sfx.Play2D(Sfx.Throw, 0.7f);
            Fx.Kick(2.5f);
        }

        void HandleBerry()
        {
            if (!Input.GetMouseButtonDown(1) || Time.time < m_NextEat) return;
            if (m_Net.Health.Value >= Cfg.MaxHealth) { Hud.Push("You're already at full health"); return; }
            m_NextEat = Time.time + 0.8f;
            m_Net.EatRpc();
            m_VM.Eat();
            Sfx.Play2D(Sfx.Eat, 0.7f);
        }

        void HandleDeploy(Item held)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (m_GhostOk)
            {
                m_Net.PlaceDeployableRpc((byte)held, m_GhostPos, m_GhostYaw);
                m_VM.Use();
                Sfx.Play(Sfx.Place, m_GhostPos, 0.8f);
            }
            else if (!string.IsNullOrEmpty(BuildHint)) Hud.Push(BuildHint);
        }

        void HandleBuildInput()
        {
            if (Input.GetMouseButtonDown(1))
            {
                int idx = System.Array.IndexOf(k_Pieces, BuildPiece);
                BuildPiece = k_Pieces[(idx + 1) % k_Pieces.Length];
            }
            if (Input.GetKeyDown(KeyCode.R)) m_RotOffset = (m_RotOffset + 1) & 3;

            if (Input.GetMouseButtonDown(0))
            {
                if (Time.time < m_NextBuild) Hud.Push("Building... wait a moment");
                else if (m_GhostOk)
                {
                    m_NextBuild = Time.time + Cfg.BuildCooldown;
                    m_Net.PlaceRpc((byte)BuildPiece, m_GhostKey.I, m_GhostKey.J, m_GhostKey.L, m_GhostKey.D);
                    m_VM.Use();
                    BuildGrid.Pose(BuildPiece, m_GhostKey, out var pos, out _);
                    Sfx.Play(Sfx.Place, pos, 0.8f);
                }
                else if (!string.IsNullOrEmpty(BuildHint)) Hud.Push(BuildHint);
            }

            if (Input.GetKeyDown(KeyCode.F) && Time.time >= m_NextUpgrade)
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

        public enum TargetKind { None, Ball, Door, Container, Bush, PlayerSpear, WorldItem, SelfSpear }

        public struct Interactable
        {
            public TargetKind Kind;
            public NetworkObject Obj;
            public int ItemId;
            public ItemStack Stack;
        }

        Interactable FindInteract()
        {
            var result = new Interactable();
            if (m_Cam == null || m_Net.Dead.Value) return result;
            var ray = CenterRay();
            float range = Cfg.InteractRange + 0.5f;
            float found = range;

            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                var kind = Classify(h.collider, out var obj);
                if (kind != TargetKind.None) { result.Kind = kind; result.Obj = obj; found = h.distance; break; }
                if (!h.collider.isTrigger) { found = h.distance; break; } // walls block interaction
            }

            // items in the world have no collider: pick the one closest to the crosshair
            var game = NetGame.Instance;
            if (game != null)
            {
                float bestPerp = 0.5f;
                foreach (var it in game.Items)
                {
                    var mid = it.Center;
                    float t = Vector3.Dot(mid - ray.origin, ray.direction);
                    if (t < 0 || t > found + 0.4f || t > range + 0.5f) continue;
                    float perp = Vector3.Distance(ray.origin + ray.direction * t, mid);
                    if (perp < bestPerp) { bestPerp = perp; result = new Interactable { Kind = TargetKind.WorldItem, ItemId = it.Id, Stack = it.Stack }; }
                }
            }

            // forgiving fallback for the ball: roughly looking at it while standing next to it
            var ball = Ball.Instance;
            if (result.Kind == TargetKind.None && ball != null && !ball.IsCarried)
            {
                var bp = ball.transform.position;
                float t = Vector3.Dot(bp - ray.origin, ray.direction);
                if (t > 0 && t < found + 0.5f && t < range && Vector3.Distance(ray.origin + ray.direction * t, bp) < 0.9f)
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
            return TargetKind.None;
        }

        void HandleInteract(Interactable t, bool carrying)
        {
            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (carrying && t.Kind != TargetKind.Door) return; // hands are full
            switch (t.Kind)
            {
                case TargetKind.Ball: m_Net.PickupBallRpc(); Sfx.Play2D(Sfx.Pop, 0.5f); break;
                case TargetKind.Door: m_Net.ToggleDoorRpc(t.Obj); break;
                case TargetKind.Container:
                    LootTarget = t.Obj.GetComponent<Container>();
                    MenuOpen = true;
                    Sfx.Play2D(Sfx.Place, 0.4f);
                    break;
                case TargetKind.Bush: m_Net.PickBerriesRpc(t.Obj); m_VM.Use(); Sfx.Play2D(Sfx.Pop, 0.5f); break;
                case TargetKind.PlayerSpear: m_Net.PullSpearRpc(t.Obj); m_VM.Use(); break;
                case TargetKind.WorldItem: m_Net.PickupItemRpc(t.ItemId); m_VM.Use(); break;
                case TargetKind.SelfSpear: m_Net.PullSpearRpc(m_Net.NetworkObject); Sfx.Play2D(Sfx.Flesh, 0.6f); break;
            }
        }

        // ------------------------------------------------------------------ ghosts

        void UpdateGhost(Item held)
        {
            int want = -1;
            if (held == Item.BuildingPlan) want = (int)BuildPiece;
            else if (held == Item.Barrier) want = 100;
            else if (held == Item.Chest) want = 101;

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
                var kind = want == 100 ? Item.Barrier : Item.Chest;
                visible = hasHit && hit.distance <= Cfg.DeployRange && hit.normal.y > 0.7f;
                if (!visible) reason = "Aim at flat ground nearby";
                else
                {
                    m_GhostPos = hit.point;
                    m_GhostYaw = m_Yaw + (kind == Item.Chest ? 180f : 0f);
                    reason = PlayerNet.DeployProblem(kind, team, m_GhostPos, m_GhostYaw);
                    m_Ghost.transform.SetPositionAndRotation(m_GhostPos, Quaternion.Euler(0, m_GhostYaw, 0));
                }
            }
            else
            {
                var t = (PieceType)want;
                int rot = (Mathf.RoundToInt(m_Yaw / 90f) + m_RotOffset) & 3;
                visible = BuildGrid.ComputePlacement(t, ray, hit, hasHit, transform.position.y, rot, out m_GhostKey);
                if (!visible) reason = t == PieceType.Floor ? "Look up at where the floor/ceiling should go" : "Aim at the ground or your building";
                else
                {
                    BuildGrid.Pose(t, m_GhostKey, out var pos, out var prot);
                    m_Ghost.transform.SetPositionAndRotation(pos, prot);
                    RefreshClientKeys();
                    if (!BuildGrid.InTeamBase(team, m_GhostKey)) reason = "You can only build inside your base area";
                    else if (BuildGrid.OnUfo(m_GhostKey)) reason = "You can't build on the crashed UFO or block its door";
                    else if (BuildGrid.IsOccupied(m_GhostKey, m_ClientKeys.Contains)) reason = "Something is already built there";
                    else if (!BuildGrid.IsSupported(m_GhostKey, m_ClientKeys.Contains))
                        reason = t == PieceType.Floor ? "Floors need a wall below or a floor next to them" : "Needs a foundation or floor underneath";
                    else if (m_Net.Count(Item.Wood) < Cfg.PieceWood(t)) reason = $"Need {Cfg.PieceWood(t)} wood";
                }
            }

            m_GhostOk = visible && reason == null;
            BuildHint = reason ?? "";
            if (m_Ghost.activeSelf != visible) m_Ghost.SetActive(visible);
            var mat = Art.Ghost(m_GhostOk ? k_GhostOk : k_GhostBad);
            foreach (var r in m_GhostRenderers) r.sharedMaterial = mat;
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
            if (m_Net.CarryingBall) { AimText = "Carrying the ball!  LMB: throw it where you're looking"; return; }
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
                    AimText = $"Storage Chest ({Cfg.TeamName[c.Team.Value]})  {c.Health.Value:0}/{Cfg.ChestHp:0}   E: open";
                    return;
                }
                case TargetKind.Bush: AimText = "Berry Bush   E: pick berries"; return;
                case TargetKind.PlayerSpear: AimText = "E: pull the spear out"; return;
                case TargetKind.WorldItem: AimText = $"E: pick up {Cfg.ItemName(t.Stack.Id)}" + (t.Stack.Count > 1 ? $" x{t.Stack.Count}" : ""); return;
            }
            string self = m_Net.StuckSpears.Value > 0 ? $"<color=#ff8888>{m_Net.StuckSpears.Value} spear(s) stuck in you - E: pull out</color>" : "";
            AimText = self;
            if (!Aim(CenterRay(), 6f, out var hit)) return;
            var no = hit.collider.GetComponentInParent<NetworkObject>();
            if (no == null) return;
            if (no.TryGetComponent(out Structure st))
            {
                AimText = $"{st.DisplayName} ({Cfg.TeamName[st.Team.Value]})  {st.Health.Value:0}/{st.MaxHp:0}";
                if (m_Net.HeldItem == Item.BuildingPlan && st.Team.Value == m_Net.Team.Value && st.Tier.Value == 0 && st.Upgradable)
                    AimText += $"   F: upgrade to stone ({Cfg.PieceUpgradeStone(st.PType)} stone)";
                if (m_Net.HeldItem == Item.Ram && st.Team.Value != m_Net.Team.Value && hit.distance <= Cfg.RamRange)
                    AimText += st.Tier.Value == 1 ? "   hold LMB: ram down to wood" : "   hold LMB: ram to smash";
            }
            else if (no.TryGetComponent(out ResourceNode n))
                AimText = n.IsBush ? "Berry Bush (empty)" : $"{n.DisplayName}  ({n.Amount.Value} {(n.Kind.Value == ResourceNode.Tree ? "wood" : "stone")} left)";
            else if (no.TryGetComponent(out PlayerNet p) && p != m_Net)
                AimText = $"{Cfg.TeamName[p.Team.Value]} player";
            if (AimText == "") AimText = self;
        }
    }
}
