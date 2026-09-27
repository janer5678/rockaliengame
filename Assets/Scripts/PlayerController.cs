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
        public float DrawAmount { get; private set; }

        const float k_Sensitivity = 2f;
        static readonly PieceType[] k_Pieces = { PieceType.Foundation, PieceType.Wall, PieceType.Doorway, PieceType.Floor, PieceType.Stairs };
        static readonly Color k_GhostOk = new Color(0.3f, 1f, 0.45f, 0.4f), k_GhostBad = new Color(1f, 0.3f, 0.3f, 0.4f);

        PlayerNet m_Net;
        CharacterController m_CC;
        NetworkTransform m_NT;
        Camera m_Cam;

        float m_Yaw, m_Pitch, m_VelY, m_Bob;
        float m_NextSwing, m_SwingDur = 0.6f, m_ViewSwing, m_NextBuild, m_NextUpgrade, m_NextPush, m_DrawStart = -1f;
        int m_RotOffset;

        Transform m_ViewRoot;
        GameObject m_ViewModel;
        int m_ViewItem = -1;

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
                bool sprint = input && Input.GetKey(KeyCode.LeftShift) && fwdInput > 0 && m_DrawStart < 0;
                float speed = sprint ? Cfg.SprintSpeed : Cfg.WalkSpeed;
                if (carrying) speed *= Cfg.BallCarrySpeedMul;
                if (m_DrawStart >= 0) speed *= 0.6f;

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
                if (Cfg.IsMelee(held)) HandleMelee(held);
                else if (held == Item.Bow) HandleBow();
                else if (held == Item.BuildingPlan) HandleBuildInput();
                else if (held == Item.CraftingTable || held == Item.Ram)
                {
                    if (Input.GetMouseButtonDown(0) && m_GhostOk)
                    {
                        if (held == Item.CraftingTable) m_Net.PlaceTableRpc(m_GhostPos, m_GhostYaw);
                        else m_Net.PlaceRamRpc(m_GhostPos, m_GhostYaw);
                    }
                }
            }
            if (held != Item.Bow || !input) m_DrawStart = -1f;
            DrawAmount = m_DrawStart >= 0 ? Mathf.Clamp01((Time.time - m_DrawStart) / Cfg.BowDrawTime) : 0f;

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
                for (int k = 0; k < 8; k++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + k) && m_Net.Owns((Item)k)) want = (Item)k;
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0)
                {
                    int dir = scroll < 0 ? 1 : -1;
                    int idx = (int)want;
                    for (int n = 0; n < 8; n++)
                    {
                        idx = (idx + dir + 8) % 8;
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
                else Hud.Push("No arrows - craft some at your crafting table");
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
                    if (s != null && s.PType != PieceType.CraftingTable)
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
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (carrying) { m_Net.DropBallRpc(CenterRay().direction * 3f + Vector3.up * 1f); return; }
                if (ball != null && !ball.IsCarried && Vector3.Distance(ball.transform.position, transform.position + Vector3.up) < Cfg.InteractRange + 0.5f)
                {
                    m_Net.PickupBallRpc();
                    return;
                }
                if (Aim(CenterRay(), Cfg.InteractRange, out var hit))
                {
                    var s = hit.collider.GetComponentInParent<Structure>();
                    if (s != null && s.PType == PieceType.Doorway) m_Net.ToggleDoorRpc(s.NetworkObject);
                }
            }
            if (Input.GetKey(KeyCode.E) && !carrying && Time.time >= m_NextPush)
            {
                var ram = NearestRam(3.8f);
                if (ram != null)
                {
                    m_NextPush = Time.time + 0.1f;
                    var f = m_Cam.transform.forward;
                    f.y = 0;
                    m_Net.PushRamRpc(ram.NetworkObject, f.normalized);
                }
            }
        }

        Ram NearestRam(float range)
        {
            Ram best = null;
            float bd = range;
            foreach (var r in Ram.All)
            {
                float d = Vector3.Distance(r.transform.position, transform.position);
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        // ------------------------------------------------------------------ ghosts

        void UpdateGhost(Item held)
        {
            int want = -1;
            if (held == Item.BuildingPlan) want = (int)BuildPiece;
            else if (held == Item.CraftingTable) want = (int)PieceType.CraftingTable;
            else if (held == Item.Ram) want = 100;

            if (want != m_GhostId)
            {
                if (m_Ghost) Destroy(m_Ghost);
                m_Ghost = null;
                m_GhostRenderers.Clear();
                m_GhostId = want;
                if (want >= 0)
                {
                    m_Ghost = new GameObject("Ghost");
                    if (want == 100) Ram.CreateVisual(m_Ghost.transform, Art.Ghost(k_GhostOk), out _);
                    else Structure.CreateVisual((PieceType)want, 0, m_Ghost.transform, false, Art.Ghost(k_GhostOk), out _);
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

            if (want == 100 || want == (int)PieceType.CraftingTable)
            {
                float range = want == 100 ? 8f : 5f;
                visible = hasHit && hit.distance <= range && hit.normal.y > 0.7f;
                if (!visible) reason = "Aim at flat ground nearby";
                else
                {
                    m_GhostPos = hit.point;
                    m_GhostYaw = want == 100 ? m_Yaw : m_Yaw + 180f;
                    if (want == (int)PieceType.CraftingTable && Cfg.BaseTeamAt(hit.point) != team) reason = "The crafting table must go inside your base";
                    else if (want == 100 && !ClearForRam(m_GhostPos, Quaternion.Euler(0, m_GhostYaw, 0))) reason = "Not enough room for the ram";
                }
                if (visible) m_Ghost.transform.SetPositionAndRotation(m_GhostPos, Quaternion.Euler(0, m_GhostYaw, 0));
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
                    else if (BuildGrid.IsOccupied(m_GhostKey, m_ClientKeys.Contains)) reason = "Something is already built there";
                    else if (!BuildGrid.IsSupported(m_GhostKey, m_ClientKeys.Contains))
                        reason = t == PieceType.Floor ? "Floors need a wall below or a floor next to them" : "Needs a foundation or floor underneath";
                    else if (m_Net.Wood.Value < Cfg.PieceWood(t)) reason = $"Need {Cfg.PieceWood(t)} wood";
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

        bool ClearForRam(Vector3 pos, Quaternion rot)
        {
            var hits = Physics.OverlapBox(pos + Vector3.up * (Ram.HalfExtents.y + 0.3f), Ram.HalfExtents - Vector3.one * 0.1f, rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<GroundMarker>() != null) continue;
                return false;
            }
            return true;
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
                    pos = new Vector3(0.3f, -0.35f, 0.3f + s * 0.6f);
                    rot = Quaternion.Euler(80f, -5f, 0);
                    break;
                case Item.Bow:
                    pos = new Vector3(0.22f - DrawAmount * 0.12f, -0.18f, 0.55f);
                    rot = Quaternion.Euler(0, -5f, -15f + DrawAmount * 10f);
                    break;
                case Item.BuildingPlan:
                case Item.CraftingTable:
                case Item.Ram:
                    pos = new Vector3(0.3f, -0.32f, 0.5f);
                    rot = Quaternion.Euler(-10f, -15f, 0);
                    break;
                default:
                    pos = new Vector3(0.32f, -0.4f, 0.55f);
                    rot = Quaternion.Euler(35f + s * 75f, -12f, s * -10f);
                    break;
            }
            float scale = held == Item.BuildingPlan ? 0.55f : held == Item.Bow ? 0.7f : 1f;
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
            var ram = NearestRam(3.8f);
            if (ram != null) { AimText = $"Battering Ram  {ram.Health.Value:0}/{Cfg.RamHp:0}  -  hold E to push"; return; }
            if (!Aim(CenterRay(), 6f, out var hit)) return;
            var no = hit.collider.GetComponentInParent<NetworkObject>();
            if (no == null) return;
            if (no.TryGetComponent(out Structure s))
            {
                AimText = $"{s.DisplayName} ({Cfg.TeamName[s.Team.Value]})  {s.Health.Value:0}/{s.MaxHp:0}";
                if (s.PType == PieceType.Doorway && s.Team.Value == m_Net.Team.Value) AimText += "   E: open/close";
                if (m_Net.HeldItem == Item.BuildingPlan && s.Team.Value == m_Net.Team.Value && s.Tier.Value == 0 && s.PType != PieceType.CraftingTable)
                    AimText += $"   F: upgrade to stone ({Cfg.PieceUpgradeStone(s.PType)} stone)";
                if (s.PType == PieceType.CraftingTable && s.Team.Value == m_Net.Team.Value) AimText += "   TAB: craft";
            }
            else if (no.TryGetComponent(out ResourceNode n))
                AimText = $"{n.DisplayName}  ({n.Amount.Value} {(n.Kind.Value == ResourceNode.Tree ? "wood" : "stone")} left)";
            else if (no.TryGetComponent(out PlayerNet p) && p != m_Net)
                AimText = $"{Cfg.TeamName[p.Team.Value]} player";
        }
    }
}
