using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Networked player state + all server-side validation of player actions.
    /// Movement is owner-authoritative (NetworkTransform in Owner mode), everything else is server-authoritative.
    /// </summary>
    public class PlayerNet : NetworkBehaviour
    {
        public static PlayerNet Local;
        public static readonly List<PlayerNet> All = new List<PlayerNet>();

        // ---- server-written state ----
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(Cfg.MaxHealth);
        public readonly NetworkVariable<bool> Dead = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> RespawnAt = new NetworkVariable<double>();
        public readonly NetworkVariable<int> Wood = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Stone = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Arrows = new NetworkVariable<int>();
        public readonly NetworkVariable<int> OwnedMask = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Tables = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Rams = new NetworkVariable<int>();

        // ---- owner-written state ----
        public readonly NetworkVariable<byte> Held = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Pitch = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public Item HeldItem => (Item)Held.Value;
        public bool CarryingBall => Ball.Instance != null && Ball.Instance.CarrierId.Value == NetworkObjectId;
        public Vector3 EyePos => transform.position + Vector3.up * Cfg.EyeHeight;

        public bool Owns(Item i)
        {
            switch (i)
            {
                case Item.Rock: return true;
                case Item.CraftingTable: return Tables.Value > 0;
                case Item.Ram: return Rams.Value > 0;
                default: return (OwnedMask.Value & (1 << (int)i)) != 0;
            }
        }

        // ---- visuals ----
        CharacterController m_CC;
        Transform m_VisualRoot, m_Head, m_Hand;
        GameObject m_HandItem;
        readonly List<Renderer> m_TeamRenderers = new List<Renderer>();
        float m_Swing;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            m_CC = GetComponent<CharacterController>();
            if (IsServer)
                Team.Value = (byte)(OwnerClientId == NetworkManager.ServerClientId ? 0 : 1);

            BuildBody();
            Held.OnValueChanged += OnHeldChanged;
            Team.OnValueChanged += OnTeamChanged;
            if (IsOwner)
            {
                Local = this;
                Art.SetLayerShadowsOnly(m_VisualRoot.gameObject);
            }
            name = $"Player {OwnerClientId}";
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Held.OnValueChanged -= OnHeldChanged;
            Team.OnValueChanged -= OnTeamChanged;
            if (Local == this) Local = null;
        }

        void OnHeldChanged(byte prev, byte cur) => RebuildHandItem();
        void OnTeamChanged(byte prev, byte cur) => Recolor();

        void BuildBody()
        {
            m_VisualRoot = new GameObject("body").transform;
            m_VisualRoot.SetParent(transform, false);
            var torso = Art.Part(m_VisualRoot, Art.Capsule, Color.white, new Vector3(0, 0.85f, 0), new Vector3(0.75f, 0.8f, 0.6f));
            m_TeamRenderers.Add(torso.GetComponent<Renderer>());
            Art.Box(m_VisualRoot, new Color(0.25f, 0.2f, 0.18f), new Vector3(0, 0.95f, 0), new Vector3(0.78f, 0.12f, 0.64f)); // belt
            m_Head = new GameObject("head").transform;
            m_Head.SetParent(m_VisualRoot, false);
            m_Head.localPosition = new Vector3(0, 1.6f, 0);
            Art.Box(m_Head, new Color(0.93f, 0.76f, 0.6f), Vector3.zero, new Vector3(0.42f, 0.42f, 0.42f));
            Art.Box(m_Head, Color.black, new Vector3(-0.09f, 0.05f, 0.21f), new Vector3(0.07f, 0.07f, 0.02f));
            Art.Box(m_Head, Color.black, new Vector3(0.09f, 0.05f, 0.21f), new Vector3(0.07f, 0.07f, 0.02f));
            var band = Art.Box(m_Head, Color.white, new Vector3(0, 0.17f, 0), new Vector3(0.45f, 0.1f, 0.45f));
            m_TeamRenderers.Add(band.GetComponent<Renderer>());
            m_Hand = new GameObject("hand").transform;
            m_Hand.SetParent(m_VisualRoot, false);
            m_Hand.localPosition = new Vector3(0.42f, 1.1f, 0.3f);
            Art.Box(m_Hand, new Color(0.93f, 0.76f, 0.6f), Vector3.zero, new Vector3(0.14f, 0.14f, 0.14f));
            Recolor();
            RebuildHandItem();
        }

        void Recolor()
        {
            var c = Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, 1)];
            foreach (var r in m_TeamRenderers) r.sharedMaterial = Art.Mat(c);
        }

        void RebuildHandItem()
        {
            if (m_HandItem) Destroy(m_HandItem);
            m_HandItem = ItemModels.Create(HeldItem, m_Hand);
            m_HandItem.transform.localRotation = Quaternion.Euler(20, 0, 0);
            if (IsOwner) Art.SetLayerShadowsOnly(m_HandItem);
        }

        void Update()
        {
            bool alive = !Dead.Value;
            if (m_VisualRoot.gameObject.activeSelf != alive) m_VisualRoot.gameObject.SetActive(alive);
            if (m_CC.enabled != alive) m_CC.enabled = alive;
            if (!IsOwner) m_Head.localRotation = Quaternion.Euler(Pitch.Value * 0.6f, 0, 0);
            if (m_Swing > 0)
            {
                m_Swing = Mathf.Max(0, m_Swing - Time.deltaTime * 3f);
                m_Hand.localRotation = Quaternion.Euler(Mathf.Sin(m_Swing * Mathf.PI) * 80f, 0, 0);
            }

            if (IsServer && Dead.Value && NetGame.Instance != null && NetworkManager.ServerTime.Time >= RespawnAt.Value)
            {
                var s = NetGame.Instance.S;
                if (s != GameState.SuddenDeath && s != GameState.GameOver) ServerRespawn();
            }
        }

        // =====================================================================
        // Server-side logic
        // =====================================================================

        float m_NextMelee, m_NextShot, m_NextBuild, m_NextUpgrade;
        readonly Queue<float> m_PendingArrows = new Queue<float>();

        bool GameAllowsCombat => NetGame.Instance == null || NetGame.Instance.S != GameState.GameOver;
        bool InSuddenDeath => NetGame.Instance != null && NetGame.Instance.S == GameState.SuddenDeath;

        public void ServerDamage(float dmg, PlayerNet attacker)
        {
            if (Dead.Value || !GameAllowsCombat) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0) ServerDie(attacker);
        }

        void ServerDie(PlayerNet killer)
        {
            Dead.Value = true;
            Health.Value = 0;
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position + Vector3.up * 1.5f, Vector3.up * 3f);
            RespawnAt.Value = NetworkManager.ServerTime.Time + Cfg.RespawnTime;
            var victimName = Cfg.TeamName[Team.Value];
            if (NetGame.Instance != null)
            {
                NetGame.Instance.Broadcast(killer != null && killer != this ? $"{Cfg.TeamName[killer.Team.Value]} killed {victimName}" : $"{victimName} died");
                NetGame.Instance.ServerOnPlayerKilled(this, killer);
            }
        }

        public void ServerRespawn()
        {
            Health.Value = Cfg.MaxHealth;
            NetGame.SpawnPoint(Team.Value, false, out var pos, out var yaw);
            TeleportRpc(pos, yaw);
            Dead.Value = false;
        }

        public void ServerEnterArena()
        {
            if (CarryingBall) Ball.Instance.ServerDrop(transform.position, Vector3.zero);
            Health.Value = Cfg.MaxHealth;
            Dead.Value = false;
            NetGame.SpawnPoint(Team.Value, true, out var pos, out var yaw);
            TeleportRpc(pos, yaw);
        }

        void Notify(string msg) => NotifyRpc(new FixedString128Bytes(msg));

        bool NearOwnTable()
        {
            foreach (var s in Structure.All)
                if (s.PType == PieceType.CraftingTable && s.Team.Value == Team.Value &&
                    Vector3.Distance(s.transform.position, transform.position) <= Cfg.CraftTableRange + 1f)
                    return true;
            return false;
        }

        // ---------------- combat ----------------

        [Rpc(SendTo.Server)]
        public void MeleeRpc(bool hasTarget, NetworkObjectReference target, Vector3 point, bool head)
        {
            if (Dead.Value || CarryingBall) return;
            var item = HeldItem;
            if (!Cfg.IsMelee(item) || !Owns(item)) return;
            if (InSuddenDeath && item != Item.Rock) return;
            var st = Cfg.Melee(item);
            if (Time.time < m_NextMelee) return;
            m_NextMelee = Time.time + st.Cooldown * 0.85f; // small tolerance for latency jitter
            SwingRpc();

            if (!hasTarget || !target.TryGet(out var no)) return;
            if (Vector3.Distance(EyePos, point) > st.Range + 2f) return;

            if (no.TryGetComponent(out PlayerNet p))
            {
                if (p == this || p.Dead.Value || !GameAllowsCombat) return;
                p.ServerDamage(st.PlayerDamage * (head ? Cfg.HeadshotMul : 1f), this);
                HitMarkerRpc(p.Dead.Value);
            }
            else if (no.TryGetComponent(out ResourceNode n))
            {
                bool tree = n.Kind.Value == ResourceNode.Tree;
                int got = n.ServerHarvest(Mathf.RoundToInt(tree ? st.WoodGather : st.StoneGather));
                if (tree) Wood.Value += got; else Stone.Value += got;
            }
            else if (no.TryGetComponent(out Structure s))
            {
                if (s.Team.Value == Team.Value || !GameAllowsCombat) return;
                float dmg = st.StructureDamage * (s.Tier.Value == 1 ? Cfg.StoneStructureMeleeMul : 1f);
                s.ServerDamage(dmg);
                HitMarkerRpc(false);
            }
            else if (no.TryGetComponent(out Ram r))
            {
                if (r.Team.Value == Team.Value) return;
                r.ServerDamage(st.RamDamage);
                HitMarkerRpc(false);
            }
        }

        [Rpc(SendTo.Server)]
        public void FireArrowRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Bow || !Owns(Item.Bow) || Arrows.Value <= 0) return;
            if (Time.time < m_NextShot) return;
            m_NextShot = Time.time + 0.3f;
            Arrows.Value--;
            float power = Mathf.Clamp01(velocity.magnitude / Cfg.ArrowSpeed);
            m_PendingArrows.Enqueue(power);
            while (m_PendingArrows.Count > 6) m_PendingArrows.Dequeue();
            ArrowVisualRpc(origin, velocity);
        }

        [Rpc(SendTo.Server)]
        public void ArrowHitRpc(NetworkObjectReference target, Vector3 point, bool head)
        {
            if (m_PendingArrows.Count == 0) return;
            float power = m_PendingArrows.Dequeue();
            if (!target.TryGet(out var no) || !GameAllowsCombat) return;
            if (no.TryGetComponent(out PlayerNet p))
            {
                if (p == this || p.Dead.Value) return;
                p.ServerDamage(Cfg.ArrowPlayerDamage * power * (head ? Cfg.HeadshotMul : 1f), this);
                HitMarkerRpc(p.Dead.Value);
            }
            else if (no.TryGetComponent(out Structure s))
            {
                if (s.Team.Value == Team.Value || s.Tier.Value == 1) return;
                s.ServerDamage(Cfg.ArrowWoodStructureDamage);
            }
            else if (no.TryGetComponent(out Ram r))
            {
                if (r.Team.Value == Team.Value) return;
                r.ServerDamage(Cfg.ArrowRamDamage * power);
                HitMarkerRpc(false);
            }
        }

        // ---------------- crafting ----------------

        [Rpc(SendTo.Server)]
        public void CraftRpc(int recipe)
        {
            if (Dead.Value || recipe < 0 || recipe >= Cfg.Recipes.Length) return;
            if (InSuddenDeath || (NetGame.Instance != null && NetGame.Instance.S == GameState.GameOver)) return;
            var r = Cfg.Recipes[recipe];
            var id = (Cfg.R)recipe;
            if (r.NeedsTable && !NearOwnTable()) { Notify("You must stand next to your Crafting Table"); return; }

            Item unique = Item.Rock;
            switch (id)
            {
                case Cfg.R.BuildingPlan: unique = Item.BuildingPlan; break;
                case Cfg.R.Hatchet: unique = Item.Hatchet; break;
                case Cfg.R.Pickaxe: unique = Item.Pickaxe; break;
                case Cfg.R.Spear: unique = Item.Spear; break;
                case Cfg.R.Bow: unique = Item.Bow; break;
            }
            if (unique != Item.Rock && Owns(unique)) { Notify($"You already have a {Cfg.ItemName(unique)}"); return; }
            if (Wood.Value < r.Wood || Stone.Value < r.Stone) { Notify($"Not enough resources for {r.Name}"); return; }

            Wood.Value -= r.Wood;
            Stone.Value -= r.Stone;
            if (unique != Item.Rock) OwnedMask.Value |= 1 << (int)unique;
            else if (id == Cfg.R.CraftingTable) Tables.Value++;
            else if (id == Cfg.R.Arrows) Arrows.Value += Cfg.ArrowsPerCraft;
            else if (id == Cfg.R.Ram) Rams.Value++;
            Notify($"Crafted {r.Name}");
        }

        // ---------------- building ----------------

        [Rpc(SendTo.Server)]
        public void PlaceRpc(byte type, int i, int j, int l, int d)
        {
            var t = (PieceType)type;
            if (t == PieceType.CraftingTable || type > (byte)PieceType.CraftingTable) return;
            if (Dead.Value || CarryingBall || !Owns(Item.BuildingPlan) || HeldItem != Item.BuildingPlan || InSuddenDeath) return;
            if (Time.time < m_NextBuild) return;

            var key = new PieceKey(BuildGrid.KindOf(t), i, j, l, t == PieceType.Stairs ? (d & 3) : (d & 1));
            if (key.L < 0 || key.L > Cfg.MaxLevel) return;
            if (!BuildGrid.InTeamBase(Team.Value, key)) { Notify("You can only build inside your own base area"); return; }
            BuildGrid.Pose(t, key, out var pos, out var rot);
            if (Vector3.Distance(pos, transform.position) > Cfg.BuildRange + 4f) { Notify("Too far away"); return; }
            if (BuildGrid.IsOccupied(key, BuildGrid.Registry.ContainsKey)) { Notify("Something is already built there"); return; }
            if (!BuildGrid.IsSupported(key, BuildGrid.Registry.ContainsKey))
            {
                Notify(t == PieceType.Floor ? "Floors need a wall below or a floor next to them" :
                       t == PieceType.Foundation ? "Foundations go on the ground" : "Needs a foundation or floor underneath");
                return;
            }
            if (!AreaClear(t, pos, rot)) { Notify("Placement blocked"); return; }
            int cost = Cfg.PieceWood(t);
            if (Wood.Value < cost) { Notify($"Need {cost} wood"); return; }

            Wood.Value -= cost;
            m_NextBuild = Time.time + Cfg.BuildCooldown * 0.85f;
            var go = Instantiate(Bootstrap.I.structurePrefab, pos, rot);
            var s = go.GetComponent<Structure>();
            s.ServerInit(t, Team.Value, key, true);
            BuildGrid.Registry[key] = s;
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        static bool AreaClear(PieceType t, Vector3 pos, Quaternion rot)
        {
            BuildGrid.LocalBounds(t, out var c, out var size);
            var half = size * 0.5f - Vector3.one * 0.18f;
            half = Vector3.Max(half, Vector3.one * 0.02f);
            var hits = Physics.OverlapBox(pos + rot * c, half, rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<GroundMarker>() != null) continue;
                var s = h.GetComponentInParent<Structure>();
                if (s != null && s.HasKey) continue; // grid pieces are handled by the grid registry
                return false;
            }
            return true;
        }

        [Rpc(SendTo.Server)]
        public void PlaceTableRpc(Vector3 pos, float yaw)
        {
            if (Dead.Value || CarryingBall || Tables.Value <= 0 || HeldItem != Item.CraftingTable || InSuddenDeath) return;
            if (Cfg.BaseTeamAt(pos) != Team.Value) { Notify("The crafting table must go inside your base"); return; }
            if (Vector3.Distance(pos, transform.position) > 7f) return;
            var rot = Quaternion.Euler(0, yaw, 0);
            var hits = Physics.OverlapBox(pos + Vector3.up * 0.6f, new Vector3(0.7f, 0.45f, 0.35f), rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<GroundMarker>() != null) continue;
                var s = h.GetComponentInParent<Structure>();
                if (s != null && (s.PType == PieceType.Foundation || s.PType == PieceType.Floor)) continue;
                Notify("Placement blocked");
                return;
            }
            Tables.Value--;
            var go = Instantiate(Bootstrap.I.structurePrefab, pos, rot);
            go.GetComponent<Structure>().ServerInit(PieceType.CraftingTable, Team.Value, default, false);
            go.GetComponent<NetworkObject>().Spawn(true);
            Notify("Crafting table placed - press TAB near it to craft");
        }

        [Rpc(SendTo.Server)]
        public void UpgradeRpc(NetworkObjectReference target)
        {
            if (Dead.Value || !Owns(Item.BuildingPlan) || HeldItem != Item.BuildingPlan || InSuddenDeath) return;
            if (Time.time < m_NextUpgrade) return;
            if (!target.TryGet(out var no) || !no.TryGetComponent(out Structure s)) return;
            if (s.Team.Value != Team.Value || !s.HasKey) return;
            if (s.Tier.Value == 1) { Notify("Already stone"); return; }
            if (Vector3.Distance(s.transform.position, transform.position) > Cfg.BuildRange + 4f) return;
            int cost = Cfg.PieceUpgradeStone(s.PType);
            if (Stone.Value < cost) { Notify($"Need {cost} stone to upgrade"); return; }
            Stone.Value -= cost;
            m_NextUpgrade = Time.time + Cfg.UpgradeCooldown * 0.85f;
            s.ServerUpgrade();
        }

        [Rpc(SendTo.Server)]
        public void PlaceRamRpc(Vector3 pos, float yaw)
        {
            if (Dead.Value || CarryingBall || Rams.Value <= 0 || HeldItem != Item.Ram || InSuddenDeath) return;
            if (Vector3.Distance(pos, transform.position) > 9f) return;
            var rot = Quaternion.Euler(0, yaw, 0);
            var hits = Physics.OverlapBox(pos + Vector3.up * (Ram.HalfExtents.y + 0.3f), Ram.HalfExtents - Vector3.one * 0.1f, rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (h.GetComponentInParent<GroundMarker>() == null) { Notify("Not enough room for the ram"); return; }
            Rams.Value--;
            var go = Instantiate(Bootstrap.I.ramPrefab, pos, rot);
            go.GetComponent<Ram>().ServerInit(Team.Value);
            go.GetComponent<NetworkObject>().Spawn(true);
            Notify("Ram placed - hold E next to it to push. It smashes enemy walls in front of it.");
        }

        // ---------------- interaction ----------------

        [Rpc(SendTo.Server)]
        public void ToggleDoorRpc(NetworkObjectReference target)
        {
            if (Dead.Value || !target.TryGet(out var no) || !no.TryGetComponent(out Structure s)) return;
            if (s.PType != PieceType.Doorway) return;
            if (s.Team.Value != Team.Value) { Notify("This door is locked"); return; }
            if (Vector3.Distance(s.transform.position, transform.position) > Cfg.InteractRange + 3f) return;
            s.DoorOpen.Value = !s.DoorOpen.Value;
        }

        [Rpc(SendTo.Server)]
        public void PickupBallRpc()
        {
            var b = Ball.Instance;
            if (Dead.Value || b == null || b.IsCarried || InSuddenDeath) return;
            if (Vector3.Distance(b.transform.position, transform.position + Vector3.up) > Cfg.InteractRange + 1.5f) return;
            if (b.ServerPickup(this) && NetGame.Instance != null)
                NetGame.Instance.Broadcast($"{Cfg.TeamName[Team.Value]} picked up the ball!");
        }

        [Rpc(SendTo.Server)]
        public void DropBallRpc(Vector3 velocity)
        {
            if (!CarryingBall) return;
            Ball.Instance.ServerDrop(transform.position + Vector3.up * 2.4f, velocity);
        }

        [Rpc(SendTo.Server)]
        public void PushRamRpc(NetworkObjectReference target, Vector3 dir)
        {
            if (Dead.Value || !target.TryGet(out var no) || !no.TryGetComponent(out Ram r)) return;
            if (Vector3.Distance(r.transform.position, transform.position) > 4.5f) return;
            r.ServerPush(dir);
        }

        // =====================================================================
        // Server -> client
        // =====================================================================

        [Rpc(SendTo.Owner)]
        public void TeleportRpc(Vector3 pos, float yaw)
        {
            var pc = GetComponent<PlayerController>();
            if (pc != null) pc.LocalTeleport(pos, yaw);
        }

        [Rpc(SendTo.Owner)]
        public void NotifyRpc(FixedString128Bytes msg) => Hud.Push(msg.ToString());

        [Rpc(SendTo.Owner)]
        public void HitMarkerRpc(bool kill) => Hud.HitMarker(kill);

        [Rpc(SendTo.NotOwner)]
        public void SwingRpc() => m_Swing = 1f;

        [Rpc(SendTo.NotOwner)]
        public void ArrowVisualRpc(Vector3 origin, Vector3 velocity) => ArrowProjectile.Spawn(origin, velocity, this, false);
    }
}
