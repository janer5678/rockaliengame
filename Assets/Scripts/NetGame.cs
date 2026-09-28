using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>An item lying in the world (dropped, thrown, or spilled from a body/chest). E picks it up.</summary>
    public struct DroppedItem : INetworkSerializeByMemcpy, System.IEquatable<DroppedItem>
    {
        public int Id;
        public ItemStack Stack;
        public Vector3 Pos, Dir;   // resting position (spears: the tip) and facing
        public Vector3 From;       // where it was thrown from (the client animates the toss)
        public bool Equals(DroppedItem o) => Id == o.Id && Stack.Equals(o.Stack) && Pos == o.Pos && Dir == o.Dir;
        /// <summary>Point used for "am I looking at it" tests.</summary>
        public Vector3 Center => Stack.Id == Item.Spear ? Pos - Dir * 0.6f : Stack.Id == Item.Arrow ? Pos - Dir * 0.3f : Pos + Vector3.up * 0.12f;
        /// <summary>Spears and arrows are drawn tip-first along Dir (stuck in the ground / a wall).</summary>
        public bool Pointed => Stack.Id == Item.Spear || Stack.Id == Item.Arrow;
    }

    /// <summary>
    /// Server-driven match flow: waiting -> gather behind the glass wall (ball drop countdown) -> ball live (airdrops) ->
    /// win (ball in your machine's socket) or sudden death.
    /// </summary>
    public class NetGame : NetworkBehaviour
    {
        public static NetGame Instance;

        public readonly NetworkVariable<byte> State = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> PhaseEnd = new NetworkVariable<double>();
        public readonly NetworkVariable<sbyte> Winner = new NetworkVariable<sbyte>(-1);
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();

        public readonly NetworkList<DroppedItem> Items = new NetworkList<DroppedItem>();
        public readonly NetworkVariable<int> MapKey = new NetworkVariable<int>();
        public readonly NetworkVariable<int> MapSeed = new NetworkVariable<int>();
        /// <summary>The host's game settings (Cfg tunables), applied on the client.</summary>
        public readonly NetworkVariable<FixedString4096Bytes> Tunables = new NetworkVariable<FixedString4096Bytes>();
        /// <summary>Airdrop ship: server time it started beaming in (-1 = none) and where the crate lands.</summary>
        public readonly NetworkVariable<double> DropStart = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<Vector3> DropPos = new NetworkVariable<Vector3>();

        int m_NextItemId = 1;
        float m_NextItemCheck;
        readonly Dictionary<int, double> m_ItemBorn = new Dictionary<int, double>();
        readonly Dictionary<int, GameObject> m_ItemVisuals = new Dictionary<int, GameObject>();
        readonly Dictionary<int, float> m_ItemSeenAt = new Dictionary<int, float>();
        readonly HashSet<int> m_Seen = new HashSet<int>();

        public GameState S => (GameState)State.Value;
        public bool WallUp => S == GameState.Waiting || S == GameState.PreBall;
        public float TimeLeft => IsSpawned ? Mathf.Max(0f, (float)(PhaseEnd.Value - NetworkManager.ServerTime.Time)) : 0f;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            State.OnValueChanged += OnStateChanged;
            Tunables.OnValueChanged += OnTunablesChanged;
            if (!IsServer)
            {
                Cfg.Apply(Tunables.Value.ToString());
                // build the host's map (same map + seed = same terrain and UFO spots)
                Cfg.SetMap(MapKey.Value, MapSeed.Value);
                if (!MapBuilder.IsBuilt(MapKey.Value, MapSeed.Value)) MapBuilder.Build();
            }
            if (IsServer)
            {
                MapKey.Value = Cfg.MapKey;
                MapSeed.Value = Cfg.MapSeed;
                var data = Cfg.Serialize();
                if (System.Text.Encoding.UTF8.GetByteCount(data) < 4000) Tunables.Value = new FixedString4096Bytes(data);
                else Debug.LogError("[RockGame] Settings too large to sync: " + data.Length);
                BuildGrid.Registry.Clear();
                SpawnNodes();
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
            }
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            Tunables.OnValueChanged -= OnTunablesChanged;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
            if (Instance == this) Instance = null;
            AirdropShip.Clear();
            foreach (var v in m_ItemVisuals.Values) if (v) Destroy(v);
            m_ItemVisuals.Clear();
        }

        void OnTunablesChanged(FixedString4096Bytes prev, FixedString4096Bytes cur)
        {
            if (!IsServer) Cfg.Apply(cur.ToString());
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void FxRpc(byte kind, Vector3 pos, Vector3 dir, ulong skipClient)
        {
            if (NetworkManager.LocalClientId == skipClient) return;
            Fx.Play((FxKind)kind, pos, dir);
        }

        void OnStateChanged(byte prev, byte cur)
        {
            switch ((GameState)cur)
            {
                case GameState.PreBall: Hud.Banner("GATHER & BUILD", $"A glass wall splits the map for {Clock(Bootstrap.Fast ? Cfg.FastBallDropDelay : Cfg.BallDropDelay)}. Craft at the alien machine on your bedrock (E)."); break;
                case GameState.BallLive: Hud.Banner("THE WALL IS DOWN", "Grab the ball from the middle and put it in YOUR machine's socket!"); break;
                case GameState.SuddenDeath: Hud.Banner("SUDDEN DEATH", "Rocks only. First kill wins."); break;
            }
        }

        static string Clock(float t)
        {
            int s = Mathf.CeilToInt(t);
            return s % 60 == 0 ? $"{s / 60} minutes" : $"{s / 60}:{s % 60:00}";
        }

        void Update()
        {
            SyncItemVisuals();
            MapBuilder.SetGlassWall(WallUp);
            AirdropShip.Tick(this);
            if (!IsServer) return;
            if (Time.time >= m_NextItemCheck) { m_NextItemCheck = Time.time + 0.5f; ServerSettleItems(); }
            double now = NetworkManager.ServerTime.Time;
            ServerTickC4(now);
            int players = PlayerNet.All.Count;
            bool fast = Bootstrap.Fast;
            switch (S)
            {
                case GameState.Waiting:
                    if (players >= 2 || (Bootstrap.Solo && players >= 1))
                    {
                        float delay = fast ? Cfg.FastBallDropDelay : Cfg.BallDropDelay;
                        SetPhase(GameState.PreBall, delay);
                        Broadcast($"Match started! The glass wall drops (and the ball with it) in {Clock(delay)}");
                    }
                    break;
                case GameState.PreBall:
                    if (now >= PhaseEnd.Value)
                    {
                        SpawnBall();
                        SetPhase(GameState.BallLive, fast ? Cfg.FastMatchLength : Cfg.MatchLength);
                        Broadcast("The glass wall is down and the BALL has dropped in the middle!");
                        m_NextDropAt = now + Cfg.AirdropInterval;
                    }
                    break;
                case GameState.BallLive:
                    ServerTickAirdrop(now);
                    if (now >= PhaseEnd.Value)
                    {
                        // only the machine socket counts - a ball lying around in your base doesn't win
                        int t = Ball.Instance != null ? Ball.Instance.SocketTeam.Value : -1;
                        if (t >= 0) EndGame(t, $"{Cfg.TeamName[t]} had the ball in their machine when time ran out!");
                        else StartSuddenDeath();
                    }
                    break;
                case GameState.SuddenDeath:
                    if (players == 1 && !Bootstrap.Solo)
                        EndGame(PlayerNet.All[0].Team.Value, "The opponent left during sudden death");
                    else if (now >= PhaseEnd.Value)
                        EndGame(-1, "Nobody won the sudden death duel in time - DRAW");
                    break;
            }
        }

        void SetPhase(GameState s, float seconds)
        {
            PhaseEnd.Value = NetworkManager.ServerTime.Time + seconds;
            State.Value = (byte)s;
        }

        void StartSuddenDeath()
        {
            if (Ball.Instance != null && Ball.Instance.IsSpawned) Ball.Instance.NetworkObject.Despawn(true);
            SetPhase(GameState.SuddenDeath, Cfg.SuddenDeathLength);
            foreach (var p in PlayerNet.All) p.ServerEnterArena();
            Broadcast("Nobody had the ball in their base - SUDDEN DEATH!");
        }

        public void EndGame(int team, string reason)
        {
            if (S == GameState.GameOver) return;
            Winner.Value = (sbyte)team;
            EndReason.Value = new FixedString128Bytes(reason.Length > 120 ? reason.Substring(0, 120) : reason);
            State.Value = (byte)GameState.GameOver;
        }

        public void ServerOnPlayerKilled(PlayerNet victim, PlayerNet killer)
        {
            if (S == GameState.SuddenDeath)
            {
                int w = 1 - victim.Team.Value;
                EndGame(w, $"{Cfg.TeamName[w]} won the sudden death duel!");
            }
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId) return;
            if (S == GameState.PreBall || S == GameState.BallLive || S == GameState.SuddenDeath)
                EndGame(0, $"{Cfg.TeamName[1]} left the game");
        }

        public void ServerCollapseCheck()
        {
            var unsupported = BuildGrid.FindUnsupported();
            foreach (var s in unsupported)
                if (s != null && s.IsSpawned) s.NetworkObject.Despawn(true);
        }

        // ------------------------------------------------------------------ items in the world

        /// <summary>Server: put an item in the world. `stick` keeps it where it is (a spear in a wall); otherwise it lands on the ground below.</summary>
        public void ServerDropItem(ItemStack stack, Vector3 at, Vector3 dir, Vector3 from, bool stick = false)
        {
            if (stack.Empty || stack.Id == Item.Rock) return;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            var pos = stick ? at : Ground(at);
            if (!stick && (stack.Id == Item.Spear || stack.Id == Item.Arrow)) dir = Flat(dir);
            int id = m_NextItemId++;
            m_ItemBorn[id] = NetworkManager.ServerTime.Time;
            Items.Add(new DroppedItem { Id = id, Stack = stack, Pos = pos, Dir = dir.normalized, From = from });
        }

        /// <summary>Server: spill a pile of items around a point (death, broken chest).</summary>
        public void ServerScatter(List<ItemStack> items, Vector3 center)
        {
            for (int i = 0; i < items.Count; i++)
            {
                float a = i * 2.39996f + Random.Range(-0.3f, 0.3f); // golden angle spiral
                float r = 0.5f + 0.22f * Mathf.Sqrt(i) + Random.Range(0f, 0.3f);
                var off = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                ServerDropItem(items[i], center + off, off, center);
            }
        }

        static Vector3 Flat(Vector3 d)
        {
            d.y = 0;
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            return (d.normalized + Vector3.down * 0.12f).normalized;
        }

        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerNet>() == null)
                return hit.point + Vector3.up * 0.04f;
            return new Vector3(p.x, MapBuilder.Height(p.x, p.z) + 0.04f, p.z);
        }

        /// <summary>Server: take (up to `max`) of a world item if `from` is close enough. Returns the stack taken.</summary>
        public ItemStack ServerTakeItem(int id, Vector3 from, float range, int max)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                if (it.Id != id) continue;
                if (Vector3.Distance(it.Center, from) > range || max <= 0) return default;
                int take = Mathf.Min(max, it.Stack.Count);
                var taken = it.Stack.WithCount(take);
                if (take >= it.Stack.Count) { Items.RemoveAt(i); m_ItemBorn.Remove(id); }
                else { it.Stack = it.Stack.WithCount(it.Stack.Count - take); Items[i] = it; }
                return taken;
            }
            return default;
        }

        /// <summary>Things stuck in something that got destroyed fall down; old items despawn.</summary>
        void ServerSettleItems()
        {
            double now = NetworkManager.ServerTime.Time;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var it = Items[i];
                if (m_ItemBorn.TryGetValue(it.Id, out var born) && now - born > Cfg.ItemDespawnTime)
                {
                    Items.RemoveAt(i);
                    m_ItemBorn.Remove(it.Id);
                    continue;
                }
                if (Physics.CheckSphere(it.Pos, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                it.From = it.Pos;
                it.Pos = Ground(it.Pos);
                if (it.Pointed) it.Dir = Flat(it.Dir);
                Items[i] = it;
            }
        }

        void SyncItemVisuals()
        {
            m_Seen.Clear();
            foreach (var it in Items)
            {
                m_Seen.Add(it.Id);
                if (!m_ItemVisuals.TryGetValue(it.Id, out var go) || !go)
                {
                    go = new GameObject("WorldItem");
                    if (it.Stack.Id == Item.Spear) ItemModels.CreateSpearTipForward(go.transform);
                    else if (it.Stack.Id == Item.Arrow) ItemModels.CreateArrowTipForward(go.transform);
                    else
                    {
                        var m = ItemModels.Create(it.Stack.Id, go.transform);
                        // tools lie flat; small stuff is scaled up a little so it reads on the ground
                        bool longItem = it.Stack.Id == Item.Hatchet || it.Stack.Id == Item.Pickaxe || it.Stack.Id == Item.Bow || it.Stack.Id == Item.BuildingPlan || it.Stack.Id == Item.DeathWand;
                        m.transform.localRotation = longItem ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                        m.transform.localScale = Vector3.one * (it.Stack.Id == Item.Ram || it.Stack.Id == Item.Chainsaw ? 1f : 1.6f);
                        if (it.Stack.Id == Item.Helmet) m.transform.localPosition = new Vector3(0, 0.25f, 0);
                        if (longItem) m.transform.localPosition = new Vector3(0, 0.05f, -0.2f);
                    }
                    m_ItemVisuals[it.Id] = go;
                    m_ItemSeenAt[it.Id] = Time.time;
                }
                var rot = it.Pointed ? Quaternion.LookRotation(it.Dir) : Quaternion.LookRotation(new Vector3(it.Dir.x, 0, it.Dir.z).sqrMagnitude > 0.001f ? new Vector3(it.Dir.x, 0, it.Dir.z) : Vector3.forward);
                // toss animation: arc from where it was thrown to where it rests
                float t = Mathf.Clamp01((Time.time - m_ItemSeenAt[it.Id]) / 0.45f);
                var pos = it.Pos;
                if (t < 1f && (it.From - it.Pos).sqrMagnitude > 0.01f)
                {
                    pos = Vector3.Lerp(it.From, it.Pos, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.6f;
                    rot *= Quaternion.Euler((1f - t) * 360f, 0, 0);
                }
                go.transform.SetPositionAndRotation(pos, rot);
            }
            if (m_ItemVisuals.Count == m_Seen.Count) return;
            var gone = new List<int>();
            foreach (var kv in m_ItemVisuals) if (!m_Seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { if (m_ItemVisuals[id]) Destroy(m_ItemVisuals[id]); m_ItemVisuals.Remove(id); m_ItemSeenAt.Remove(id); }
        }

        public void Broadcast(string msg) => BroadcastRpc(new FixedString128Bytes(msg.Length > 120 ? msg.Substring(0, 120) : msg));

        [Rpc(SendTo.ClientsAndHost)]
        void BroadcastRpc(FixedString128Bytes msg) => Hud.Push(msg.ToString());

        // ------------------------------------------------------------------ spawning

        void SpawnBall()
        {
            var go = Instantiate(Bootstrap.I.ballPrefab, Cfg.BallDropPoint, Quaternion.identity);
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        void SpawnNodes()
        {
            var rng = new System.Random(1337 + Cfg.MapSeed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var placed = new List<Vector3>();
            // wood mode has no stone at all: every rock becomes a tree
            byte rockKind = Cfg.WoodMode ? ResourceNode.Tree : ResourceNode.Boulder;
            bool NearTower(Vector3 p)
            {
                foreach (var t in Cfg.Towers) if (new Vector2(p.x - t.x, p.z - t.z).magnitude < 12f) return true;
                return false;
            }
            // the rocks on the Highlands hills are real, minable stone nodes
            foreach (var p in MapBuilder.WildRocks)
            {
                if (NearTower(p)) continue;
                bool close = false;
                foreach (var q in placed) if ((q - p).sqrMagnitude < 5f * 5f) { close = true; break; }
                if (close) continue;
                placed.Add(p);
                int seed = rng.Next();
                SpawnNode(rockKind, p, seed);
                SpawnNode(rockKind, new Vector3(-p.x, 0, -p.z), seed);
            }
            float area = Mathf.Clamp01(Cfg.MapHalf / 100f);
            area *= area;
            int trees = Mathf.Max(8, Mathf.RoundToInt(24 * area)), stones = Mathf.Max(6, Mathf.RoundToInt(18 * area)), bushes = Mathf.Max(5, Mathf.RoundToInt(10 * area));
            float half = Cfg.MapHalf;
            for (int n = 0; n < trees + stones + bushes; n++)
            {
                byte kind = n < trees ? ResourceNode.Tree : n < trees + stones ? rockKind : ResourceNode.Bush;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var p = new Vector3(R(-half + 8, half - 8), 0, R(-half + 8, -5f));
                    if (Mathf.Abs(p.x - Cfg.BaseCenter[0].x) < Cfg.BaseHalf + 3 && Mathf.Abs(p.z - Cfg.BaseCenter[0].z) < Cfg.BaseHalf + 3) continue;
                    if (new Vector2(p.x, p.z).magnitude < 12f || NearTower(p)) continue;
                    bool close = false;
                    foreach (var q in placed) if ((q - p).sqrMagnitude < 7.5f * 7.5f) { close = true; break; }
                    if (close) continue;
                    placed.Add(p);
                    int seed = rng.Next();
                    SpawnNode(kind, p, seed);
                    SpawnNode(kind, new Vector3(-p.x, 0, -p.z), seed); // point-mirrored so both teams get the same layout
                    break;
                }
            }
        }

        void SpawnNode(byte kind, Vector3 pos, int seed)
        {
            pos.y = MapBuilder.Height(pos.x, pos.z) - 0.1f;
            var go = Instantiate(Bootstrap.I.nodePrefab, pos, Quaternion.Euler(0, seed % 360, 0));
            go.GetComponent<ResourceNode>().ServerInit(kind, seed);
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        /// <summary>Where a team spawns: on its bedrock, facing the middle of the map (or the arena in sudden death).</summary>
        public static void SpawnPoint(int team, bool arena, out Vector3 pos, out float yaw)
        {
            team = Mathf.Clamp(team, 0, 1);
            if (arena)
            {
                yaw = team == 0 ? 0f : 180f;
                pos = Cfg.ArenaCenter + new Vector3(0, 0.1f, team == 0 ? -14f : 14f);
                return;
            }
            pos = Cfg.SpawnPos(team);
            yaw = Cfg.SpawnYaw(team);
        }

        /// <summary>"Respawn in the wild": a random free spot in the enemy's half of the map (never inside their base).</summary>
        public static void WildSpawnPoint(int team, out Vector3 pos, out float yaw)
        {
            float half = Cfg.MapHalf;
            int enemy = 1 - Mathf.Clamp(team, 0, 1);
            float side = Cfg.BaseCenter[enemy].z > 0 ? 1f : -1f;
            var ec = Cfg.BaseCenter[enemy];
            for (int tries = 0; tries < 60; tries++)
            {
                var p = new Vector3(Random.Range(-half + 8f, half - 8f), 0, side * Random.Range(8f, half - 8f));
                if (Mathf.Abs(p.x - ec.x) < Cfg.BaseHalf + 6f && Mathf.Abs(p.z - ec.z) < Cfg.BaseHalf + 6f) continue;
                p.y = MapBuilder.Height(p.x, p.z) + 0.1f;
                if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.45f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) continue;
                pos = p;
                yaw = Random.Range(0f, 360f);
                return;
            }
            SpawnPoint(team, false, out pos, out yaw);
        }

        // ------------------------------------------------------------------ airdrops

        const float DropArrive = 4f, DropBeam = 4f;
        /// <summary>Seconds after DropStart when the crate touches down (the ship arrives, then beams it down).</summary>
        public const float DropLand = DropArrive + DropBeam;
        double m_NextDropAt = double.MaxValue;
        Container m_Drop;
        bool m_DropIncoming;

        void ServerTickAirdrop(double now)
        {
            if (m_DropIncoming)
            {
                if (now < DropStart.Value + DropLand) return;
                m_DropIncoming = false;
                var go = Instantiate(Bootstrap.I.containerPrefab, DropPos.Value, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                m_Drop = go.GetComponent<Container>();
                m_Drop.ServerInit(Container.Airdrop, 2, 1, new List<ItemStack> { RollAirdropLoot() });
                go.GetComponent<NetworkObject>().Spawn(true);
                Fx.Server(FxKind.Spawn, DropPos.Value, Vector3.up);
                return;
            }
            if (m_Drop != null)
            {
                // taken: the next one comes a minute after the last one was emptied
                if (!m_Drop.IsSpawned || m_Drop.Empty)
                {
                    if (m_Drop.IsSpawned)
                    {
                        Fx.Server(FxKind.Break, m_Drop.transform.position + Vector3.up * 0.6f, Vector3.up);
                        m_Drop.NetworkObject.Despawn(true);
                    }
                    m_Drop = null;
                    m_NextDropAt = now + Cfg.AirdropInterval;
                }
                return;
            }
            if (now < m_NextDropAt) return;
            DropPos.Value = PickDropSpot();
            DropStart.Value = now;
            m_DropIncoming = true;
            Broadcast("An alien AIRDROP is beaming down! Look for the purple beam");
        }

        /// <summary>One random OP item.</summary>
        static ItemStack RollAirdropLoot()
        {
            switch (Random.Range(0, 6))
            {
                case 0: return ItemStack.Of(Item.C4, 1);
                case 1: return ItemStack.Of(Item.DeathWand, 1);
                case 2: return ItemStack.Of(Item.Helmet, 1, Mathf.Clamp(Cfg.HelmetHp, 1, 255));
                case 3: return ItemStack.Of(Cfg.WoodMode || Random.value < 0.5f ? Item.Wood : Item.Stone, Mathf.Clamp(Cfg.AirdropResources, 1, 1000));
                case 4: return ItemStack.Of(Item.InvisPotion, 1);
                default: return ItemStack.Of(Item.Chainsaw, 1, Mathf.Clamp(Cfg.ChainsawUses, 1, 255));
            }
        }

        /// <summary>Random open spot on the map, never close to either base.</summary>
        static Vector3 PickDropSpot()
        {
            float half = Cfg.MapHalf;
            Vector3 best = Vector3.zero;
            float bestD = -1f;
            for (int tries = 0; tries < 80; tries++)
            {
                var p = new Vector3(Random.Range(-half + 12f, half - 12f), 0, Random.Range(-half + 12f, half - 12f));
                float d = float.MaxValue;
                for (int t = 0; t < 2; t++)
                {
                    var c = Cfg.BaseCenter[t];
                    d = Mathf.Min(d, Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.z - c.z)) - Cfg.BaseHalf);
                }
                p.y = MapBuilder.Height(p.x, p.z);
                if (Physics.CheckBox(p + Vector3.up * 1.2f, new Vector3(1f, 1f, 1f), Quaternion.identity, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) continue;
                if (d >= Cfg.AirdropBaseDistance) return p;
                if (d > bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>The crate on the ground right now (null if none).</summary>
        public Container ActiveDrop
        {
            get
            {
                foreach (var c in Container.All) if (c.IsAirdrop) return c;
                return null;
            }
        }

        // ------------------------------------------------------------------ C4

        struct PendingC4 { public Vector3 Pos; public int Team; public PlayerNet Thrower; public double At; }
        readonly List<PendingC4> m_C4 = new List<PendingC4>();

        public void ServerArmC4(Vector3 pos, Vector3 normal, PlayerNet thrower)
        {
            m_C4.Add(new PendingC4 { Pos = pos, Team = thrower.Team.Value, Thrower = thrower, At = NetworkManager.ServerTime.Time + Cfg.C4Fuse });
            Fx.Server(FxKind.C4Placed, pos, normal);
        }

        void ServerTickC4(double now)
        {
            for (int i = m_C4.Count - 1; i >= 0; i--)
            {
                if (now < m_C4[i].At) continue;
                var c = m_C4[i];
                m_C4.RemoveAt(i);
                ServerExplode(c.Pos, c.Team, c.Thrower);
            }
        }

        /// <summary>C4 goes off: every enemy building piece, barrier and chest in range is destroyed outright; players nearby get hurt.</summary>
        void ServerExplode(Vector3 pos, int team, PlayerNet thrower)
        {
            Fx.Server(FxKind.Explosion, pos, Vector3.up);
            var structures = new HashSet<Structure>();
            var chests = new HashSet<Container>();
            foreach (var h in Physics.OverlapSphere(pos, Cfg.C4Radius, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                var s = h.GetComponentInParent<Structure>();
                if (s != null && s.IsSpawned && s.Team.Value != team) structures.Add(s);
                var c = h.GetComponentInParent<Container>();
                if (c != null && c.IsSpawned && c.Breakable && c.Team.Value != team) chests.Add(c);
            }
            foreach (var c in chests) if (c.IsSpawned) c.ServerBreak();
            foreach (var s in structures) if (s.IsSpawned) s.NetworkObject.Despawn(true);
            if (structures.Count > 0) ServerCollapseCheck();
            foreach (var p in PlayerNet.All)
            {
                if (p.Dead.Value) continue;
                float d = Vector3.Distance(p.transform.position + Vector3.up, pos);
                if (d > Cfg.C4Radius) continue;
                p.ServerDamage(Cfg.C4PlayerDamage * Mathf.Lerp(1f, 0.3f, d / Cfg.C4Radius), thrower);
            }
            if (thrower != null && structures.Count > 0) thrower.NotifyPublic($"Your C4 destroyed {structures.Count} enemy piece{(structures.Count == 1 ? "" : "s")}!");
        }
    }
}
