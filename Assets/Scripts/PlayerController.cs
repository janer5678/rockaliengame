using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace RockGame
{
    /// <summary>Local (owner-only) input, first-person camera, movement, tool use and build ghosts.</summary>
    public class PlayerController : NetworkBehaviour
    {
        public static PlayerController Local;

        public bool MenuOpen, Paused;
        public PieceType BuildPiece = PieceType.Foundation;
        public string AimText = "", BuildHint = "";
        public float DrawAmount { get; private set; } // bow draw / spear wind-up, 0..1
        public float RamCharge { get; private set; }  // ram wind-up, 0..1

        const float k_Sensitivity = 2f;
        static readonly PieceType[] k_Pieces = { PieceType.Foundation, PieceType.Wall, PieceType.Doorway, PieceType.Floor, PieceType.Stairs };
        static readonly Color k_GhostOk = new Color(0.3f, 1f, 0.45f, 0.4f), k_GhostBad = new Color(1f, 0.3f, 0.3f, 0.4f);

        PlayerNet m_Net;
        CharacterController m_CC;
        NetworkTransform m_NT;
        Camera m_Cam;

        float m_Yaw, m_Pitch, m_VelY, m_Bob;
        float m_NextSwing, m_SwingDur = 0.6f, m_ViewSwing, m_NextBuild, m_NextUpgrade, m_DrawStart = -1f;
        int m_RotOffset;

        Transform m_ViewRoot;
        GameObject m_ViewModel;
        int m_ViewItem = -1;

        GameObject m_Ghost;
        int m_GhostId = -1;
        bool m_GhostOk;
        readonly List<MeshRenderer> m_GhostRenderers = new List<MeshRenderer>();
        PieceKey m_GhostKey;
        readonly HashSet<PieceKey> m_ClientKeys = new HashSet<PieceKey>();

        public override void OnNetworkSpawn()
        {
            m_Net = GetComponent<PlayerNet>();
            m_CC = GetComponent<CharacterController>();
            m_NT = GetComponent<NetworkTransform>();
            if (!IsOwner) { enabled = false; return; }
            Local = this;
            m_Yaw = transform.eulerAngles.y;
            m_Cam = Camera.main;
            m_ViewRoot = new GameObject("ViewModel").transform;
            m_ViewRoot.SetParent(m_Cam.transform, false);
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            if (m_Ghost) Destroy(m_Ghost);
            if (m_ViewRoot) Destroy(m_ViewRoot.gameObject);
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

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var game = NetGame.Instance;
            bool dead = m_Net.Dead.Value;
            bool gameOver = game != null && game.S == GameState.GameOver;
            bool sd = game != null && game.S == GameState.SuddenDeath;
            bool carrying = m_Net.CarryingBall;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (MenuOpen) MenuOpen = false;
                else Paused = !Paused;
            }
            if (Input.GetKeyDown(KeyCode.Tab) && !dead && !gameOver && !sd)
            {
                MenuOpen = !MenuOpen;
                Paused = false;
            }
            if (sd) MenuOpen = false;
            if (Paused && Input.GetMouseButtonDown(0) && !Hud.MouseOverUI) Paused = false;

            bool cursorFree = MenuOpen || Paused || gameOver;
            Cursor.lockState = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = cursorFree;
            bool input = !cursorFree && !dead;

            // ---- look ----
            if (input)
            {
                m_Yaw += Input.GetAxisRaw("Mouse X") * k_Sensitivity;
                m_Pitch = Mathf.Clamp(m_Pitch - Input.GetAxisRaw("Mouse Y") * k_Sensitivity, -89f, 89f);
            }
            transform.rotation = Quaternion.Euler(0, m_Yaw, 0);
            if (Mathf.Abs(m_Net.Pitch.Value - m_Pitch) > 1f) m_Net.Pitch.Value = m_Pitch;

            // ---- move ----
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
                bool sprint = input && Input.GetKey(KeyCode.LeftShift) && fwdInput > 0 && m_DrawStart < 0 && RamCharge <= 0;
                float speed = sprint ? Cfg.SprintSpeed : Cfg.WalkSpeed;
                if (carrying) speed *= Cfg.BallCarrySpeedMul;
                if (m_DrawStart >= 0) speed *= 0.6f;
                if (m_Net.HeldItem == Item.Ram) speed *= Cfg.RamMoveMul;

                if (m_CC.isGrounded)
                {
                    if (m_VelY < 0) m_VelY = -2f;
                    if (input && Input.GetKeyDown(KeyCode.Space)) m_VelY = Cfg.JumpSpeed;
                }
                m_VelY -= Cfg.Gravity * Time.deltaTime;
                var flags = m_CC.Move((wish * speed + Vector3.up * m_VelY) * Time.deltaTime);
                if ((flags & CollisionFlags.Above) != 0 && m_VelY > 0) m_VelY = 0;
                m_Bob += wish.magnitude * speed * Time.deltaTime;

                if (transform.position.y < -30f)
                {
                    NetGame.SpawnPoint(m_Net.Team.Value, sd, out var p, out var y);
                    LocalTeleport(p, y);
                }
            }

            // ---- item selection ----
            var held = SelectItem(input, sd, carrying);

            // ---- actions ----
            if (input && !carrying && !gameOver)
            {
                if (held == Item.Spear) HandleSpear();
                else if (Cfg.IsMelee(held)) HandleMelee(held);
                else if (held == Item.Bow) HandleBow();
                else if (held == Item.BuildingPlan) HandleBuildInput();
                else if (held == Item.Ram) HandleRam();
            }
            bool drawing = input && !carrying && !gameOver && (held == Item.Bow || held == Item.Spear);
            if (!drawing) m_DrawStart = -1f;
            float drawTime = held == Item.Spear ? Cfg.SpearDrawTime : Cfg.BowDrawTime;
            DrawAmount = m_DrawStart >= 0 ? Mathf.Clamp01((Time.time - m_DrawStart) / drawTime) : 0f;
            if (!input || carrying || gameOver || held != Item.Ram || !Input.GetMouseButton(0)) RamCharge = 0f;

            HandleInteract(input, carrying);
            UpdateGhost(input && !carrying && !gameOver ? held : Item.Rock);
            UpdateAimText();
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || m_Cam == null) return;
            m_Cam.transform.SetPositionAndRotation(transform.position + Vector3.up * Cfg.EyeHeight, Quaternion.Euler(m_Pitch, m_Yaw, 0));
            m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, 70f - DrawAmount * 18f, Time.deltaTime * 10f);
            UpdateViewModel();
        }

        // ------------------------------------------------------------------ items

        Item SelectItem(bool input, bool sd, bool carrying)
        {
            Item cur = m_Net.HeldItem;
            Item want = cur;
            if (input)
            {
                for (int k = 0; k < Cfg.ItemCount; k++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + k) && m_Net.Owns((Item)k)) want = (Item)k;
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0)
                {
                    int dir = scroll < 0 ? 1 : -1;
                    int idx = (int)want;
                    for (int n = 0; n < Cfg.ItemCount; n++)
                    {
                        idx = (idx + dir + Cfg.ItemCount) % Cfg.ItemCount;
                        if (m_Net.Owns((Item)idx)) { want = (Item)idx; break; }
                    }
                }
            }
            if (!m_Net.Owns(want) || sd) want = Item.Rock;
            if (want != cur) m_Net.Held.Value = (byte)want;
            return want;
        }

        Ray CenterRay() => m_Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));

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

        void HandleMelee(Item held)
        {
            if (!Input.GetMouseButton(0) || Time.time < m_NextSwing) return;
            var st = Cfg.Melee(held);
            m_NextSwing = Time.time + st.Cooldown;
            m_SwingDur = st.Cooldown;
            m_ViewSwing = 1f;
            if (Aim(CenterRay(), st.Range, out var hit))
            {
                var no = hit.collider.GetComponentInParent<NetworkObject>();
                bool head = false;
                if (no != null && no.TryGetComponent(out PlayerNet p)) head = hit.point.y > p.transform.position.y + 1.4f;
                if (no != null) m_Net.MeleeRpc(true, no, hit.point, head);
                else m_Net.MeleeRpc(false, default, hit.point, false);
            }
            else m_Net.MeleeRpc(false, default, Vector3.zero, false);
        }

        void HandleBow()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (m_Net.Arrows.Value > 0) m_DrawStart = Time.time;
                else Hud.Push("No arrows - craft some (TAB)");
            }
            if (Input.GetMouseButtonDown(1)) m_DrawStart = -1f;
            if (Input.GetMouseButtonUp(0) && m_DrawStart >= 0)
            {
                float t = Time.time - m_DrawStart;
                m_DrawStart = -1f;
                if (t < Cfg.BowMinDraw || m_Net.Arrows.Value <= 0) return;
                float power = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(t / Cfg.BowDrawTime));
                var ray = CenterRay();
                Vector3 origin = ray.origin + ray.direction * 0.6f;
                Vector3 vel = ray.direction * Cfg.ArrowSpeed * power;
                ArrowProjectile.Spawn(origin, vel, m_Net, true);
                m_Net.FireArrowRpc(origin, vel);
            }
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
                float power = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(t / Cfg.SpearDrawTime));
                var ray = CenterRay();
                Vector3 origin = ray.origin + ray.direction * 0.8f;
                Vector3 vel = ray.direction * Cfg.SpearThrowSpeed * power;
                ArrowProjectile.SpawnSpear(origin, vel, m_Net, true);
                m_Net.ThrowSpearRpc(origin, vel);
                m_DrawStart = -1f;
                m_NextSwing = Time.time + 0.6f;
                m_SwingDur = 0.4f;
                m_ViewSwing = 1f;
            }
        }

        /// <summary>Hold LMB to wind the ram up; at full charge it slams the enemy piece you're looking at.</summary>
        void HandleRam()
        {
            if (!Input.GetMouseButton(0)) return;
            RamCharge = Mathf.Min(1f, RamCharge + Time.deltaTime / Cfg.RamWindup);
            if (RamCharge < 1f) return;

            RamCharge = 0f;
            m_SwingDur = 0.5f;
            m_ViewSwing = 1f;
            if (Aim(CenterRay(), Cfg.RamRange, out var hit))
            {
                var s = hit.collider.GetComponentInParent<Structure>();
                if (s != null && s.Team.Value != m_Net.Team.Value) { m_Net.RamStrikeRpc(s.NetworkObject, hit.point); return; }
                if (s != null) { Hud.Push("That's your own building"); return; }
            }
            Hud.Push("The ram only works on enemy buildings - get right up to one");
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
                }
                else if (!string.IsNullOrEmpty(BuildHint)) Hud.Push(BuildHint);
            }

            if (Input.GetKeyDown(KeyCode.F) && Time.time >= m_NextUpgrade)
            {
                if (Aim(CenterRay(), Cfg.BuildRange, out var hit))
                {
                    var s = hit.collider.GetComponentInParent<Structure>();
                    if (s != null)
                    {
                        m_NextUpgrade = Time.time + Cfg.UpgradeCooldown;
                        m_Net.UpgradeRpc(s.NetworkObject);
                    }
                }
            }
        }

        void HandleInteract(bool input, bool carrying)
        {
            if (!input) return;
            var ball = Ball.Instance;
            if (Input.GetKeyDown(KeyCode.G) && carrying)
            {
                m_Net.DropBallRpc(CenterRay().direction * 10f + Vector3.up * 3f);
                return;
            }
            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (carrying) { m_Net.DropBallRpc(CenterRay().direction * 3f + Vector3.up * 1f); return; }
            if (ball != null && !ball.IsCarried && Vector3.Distance(ball.transform.position, transform.position + Vector3.up) < Cfg.InteractRange + 0.5f)
            {
                m_Net.PickupBallRpc();
                return;
            }
            if (NetGame.Instance != null && NetGame.Instance.TryNearestSpear(transform.position + Vector3.up, Cfg.SpearPickupRange, out var sp))
            {
                m_Net.PickupSpearRpc(sp.Id);
                return;
            }
            if (Aim(CenterRay(), Cfg.InteractRange + 1f, out var hit))
            {
                var no = hit.collider.GetComponentInParent<NetworkObject>();
                if (no != null && no.TryGetComponent(out PlayerNet p) && p.StuckSpears.Value > 0)
                {
                    m_Net.PullSpearRpc(p.NetworkObject);
                    return;
                }
                var s = hit.collider.GetComponentInParent<Structure>();
                if (s != null && s.PType == PieceType.Doorway && hit.distance <= Cfg.InteractRange) { m_Net.ToggleDoorRpc(s.NetworkObject); return; }
            }
            if (m_Net.StuckSpears.Value > 0) m_Net.PullSpearRpc(m_Net.NetworkObject);
        }

        // ------------------------------------------------------------------ ghosts

        void UpdateGhost(Item held)
        {
            int want = -1;
            if (held == Item.BuildingPlan) want = (int)BuildPiece;

            if (want != m_GhostId)
            {
                if (m_Ghost) Destroy(m_Ghost);
                m_Ghost = null;
                m_GhostRenderers.Clear();
                m_GhostId = want;
                if (want >= 0)
                {
                    m_Ghost = new GameObject("Ghost");
                    Structure.CreateVisual((PieceType)want, 0, m_Ghost.transform, false, Art.Ghost(k_GhostOk), out _);
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
                else if (BuildGrid.IsOccupied(m_GhostKey, m_ClientKeys.Contains)) reason = "Something is already built there";
                else if (!BuildGrid.IsSupported(m_GhostKey, m_ClientKeys.Contains))
                    reason = t == PieceType.Floor ? "Floors need a wall below or a floor next to them" : "Needs a foundation or floor underneath";
                else if (m_Net.Wood.Value < Cfg.PieceWood(t)) reason = $"Need {Cfg.PieceWood(t)} wood";
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

        // ------------------------------------------------------------------ viewmodel & HUD info

        void UpdateViewModel()
        {
            var held = m_Net.HeldItem;
            bool show = !m_Net.Dead.Value && !m_Net.CarryingBall;
            if (m_ViewRoot.gameObject.activeSelf != show) m_ViewRoot.gameObject.SetActive(show);
            if ((int)held != m_ViewItem)
            {
                if (m_ViewModel) Destroy(m_ViewModel);
                m_ViewModel = ItemModels.Create(held, m_ViewRoot);
                foreach (var r in m_ViewModel.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_ViewItem = (int)held;
            }

            if (m_ViewSwing > 0) m_ViewSwing = Mathf.Max(0, m_ViewSwing - Time.deltaTime / Mathf.Max(0.2f, m_SwingDur * 0.8f));
            float s = Mathf.Sin((1f - m_ViewSwing) * Mathf.PI);
            if (m_ViewSwing <= 0) s = 0;
            Vector3 bob = new Vector3(Mathf.Sin(m_Bob * 1.6f) * 0.015f, Mathf.Abs(Mathf.Cos(m_Bob * 1.6f)) * 0.02f, 0);

            Vector3 pos; Quaternion rot;
            switch (held)
            {
                case Item.Spear:
                    if (m_DrawStart >= 0)
                    {
                        // wound up over the shoulder, pulled back as the throw charges
                        pos = new Vector3(0.32f, 0.02f, 0.05f - DrawAmount * 0.3f);
                        rot = Quaternion.Euler(88f, -4f, 0);
                    }
                    else
                    {
                        pos = new Vector3(0.3f, -0.35f, 0.3f + s * 0.6f);
                        rot = Quaternion.Euler(80f, -5f, 0);
                    }
                    break;
                case Item.Ram:
                    pos = new Vector3(0.4f, -0.45f, 0.75f - RamCharge * 0.3f + s * 0.7f);
                    rot = Quaternion.Euler(-4f + RamCharge * 6f, -10f, 0);
                    break;
                case Item.Bow:
                    pos = new Vector3(0.22f - DrawAmount * 0.12f, -0.18f, 0.55f);
                    rot = Quaternion.Euler(0, -5f, -15f + DrawAmount * 10f);
                    break;
                case Item.BuildingPlan:
                    pos = new Vector3(0.3f, -0.32f, 0.5f);
                    rot = Quaternion.Euler(-10f, -15f, 0);
                    break;
                default:
                    pos = new Vector3(0.32f, -0.4f, 0.55f);
                    rot = Quaternion.Euler(35f + s * 75f, -12f, s * -10f);
                    break;
            }
            float scale = held == Item.BuildingPlan ? 0.55f : held == Item.Bow ? 0.7f : held == Item.Ram ? 0.75f : 1f;
            if (held == Item.Bow) pos += new Vector3(0.08f, -0.02f, 0.05f);
            if (held == Item.BuildingPlan) pos += new Vector3(0.05f, -0.02f, 0.05f);
            m_ViewModel.transform.localPosition = pos + bob;
            m_ViewModel.transform.localRotation = rot;
            m_ViewModel.transform.localScale = Vector3.one * scale;
        }

        void UpdateAimText()
        {
            AimText = "";
            if (m_Net.Dead.Value) return;
            var ball = Ball.Instance;
            if (m_Net.CarryingBall) { AimText = "Carrying the ball!  E: drop   G: throw"; return; }
            if (ball != null && !ball.IsCarried && Vector3.Distance(ball.transform.position, transform.position + Vector3.up) < Cfg.InteractRange + 0.5f)
            {
                AimText = "E: pick up the ball";
                return;
            }
            if (NetGame.Instance != null && NetGame.Instance.TryNearestSpear(transform.position + Vector3.up, Cfg.SpearPickupRange, out _))
            {
                AimText = "E: pick up the spear";
                return;
            }
            string self = m_Net.StuckSpears.Value > 0 ? $"<color=#ff8888>{m_Net.StuckSpears.Value} spear(s) stuck in you - E: pull out</color>" : "";
            AimText = self;
            if (!Aim(CenterRay(), 6f, out var hit)) return;
            var no = hit.collider.GetComponentInParent<NetworkObject>();
            if (no == null) return;
            AimText = "";
            if (no.TryGetComponent(out Structure s))
            {
                AimText = $"{s.DisplayName} ({Cfg.TeamName[s.Team.Value]})  {s.Health.Value:0}/{s.MaxHp:0}";
                if (s.PType == PieceType.Doorway && s.Team.Value == m_Net.Team.Value) AimText += "   E: open/close";
                if (m_Net.HeldItem == Item.BuildingPlan && s.Team.Value == m_Net.Team.Value && s.Tier.Value == 0)
                    AimText += $"   F: upgrade to stone ({Cfg.PieceUpgradeStone(s.PType)} stone)";
                if (m_Net.HeldItem == Item.Ram && s.Team.Value != m_Net.Team.Value && hit.distance <= Cfg.RamRange)
                    AimText += s.Tier.Value == 1 ? "   hold LMB: ram down to wood" : "   hold LMB: ram to smash";
            }
            else if (no.TryGetComponent(out ResourceNode n))
                AimText = $"{n.DisplayName}  ({n.Amount.Value} {(n.Kind.Value == ResourceNode.Tree ? "wood" : "stone")} left)";
            else if (no.TryGetComponent(out PlayerNet p) && p != m_Net)
            {
                AimText = $"{Cfg.TeamName[p.Team.Value]} player";
                if (p.StuckSpears.Value > 0 && hit.distance <= Cfg.InteractRange + 1f) AimText += "   E: pull out spear";
            }
            if (AimText == "") AimText = self;
        }
    }
}
