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
        public Vector3 Center => Stack.Id == Item.Spear ? Pos - Dir * 0.6f : Stack.Id == Item.Arrow ? Pos - Dir * 0.3f : Stack.Id == Item.Skull ? Pos + Vector3.up * 0.6f : Pos + Vector3.up * 0.12f;
        /// <summary>Spears and arrows are drawn tip-first along Dir (stuck in the ground / a wall).</summary>
        public bool Pointed => Stack.Id == Item.Spear || Stack.Id == Item.Arrow;
    }

    /// <summary>
    /// Server-driven match flow: waiting -> gather behind the glass wall (ball drop countdown) -> ball live (airdrops) ->
    /// win (ball in your machine's socket) or sudden death.
    /// </summary>
    public partial class NetGame : NetworkBehaviour
    {
        public static NetGame Instance;

        public readonly NetworkVariable<byte> State = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> PhaseEnd = new NetworkVariable<double>();
        public readonly NetworkVariable<sbyte> Winner = new NetworkVariable<sbyte>(-1);
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();

        public readonly NetworkList<DroppedItem> Items = new NetworkList<DroppedItem>();
        /// <summary>The clock ran out with the ball in nobody's machine: OVERTIME - the first capture wins.</summary>
        public readonly NetworkVariable<bool> Overtime = new NetworkVariable<bool>();
        public readonly NetworkVariable<int> MapKey = new NetworkVariable<int>();
        public readonly NetworkVariable<int> MapSeed = new NetworkVariable<int>();
        /// <summary>The host's game settings (Cfg tunables), applied on the client.</summary>
        public readonly NetworkVariable<FixedString4096Bytes> Tunables = new NetworkVariable<FixedString4096Bytes>();
        /// <summary>Airdrop ships: when each lane started beaming one in (-1 = none) and where its crate lands.</summary>
        public readonly NetworkList<DropLaneState> Lanes = new NetworkList<DropLaneState>();
        /// <summary>Every portal shot this game (they stay until the end).</summary>
        public readonly NetworkList<PortalInfo> Portals = new NetworkList<PortalInfo>();
        /// <summary>Sudden death: nobody can move or fight until this server time (the big countdown).</summary>
        public readonly NetworkVariable<double> FightAt = new NetworkVariable<double>(-1);
        /// <summary>Dev setting: the match timer (and the airdrop timers) are frozen.</summary>
        public readonly NetworkVariable<bool> TimerPaused = new NetworkVariable<bool>();
        /// <summary>When the next scheduled airdrop's ship comes in (server time: the announcement counts down to the UFO
        /// showing up, not to the crate landing), set when it's announced (DropWarning s ahead); -1 when none is coming.</summary>
        public readonly NetworkVariable<double> NextDropLands = new NetworkVariable<double>(-1);
        /// <summary>The victory cutscene (VictoryCutscene.cs): when it started (server time; -1 none), the spot under the
        /// UFO (the winners' bedrock) and the winners it beams up (their NetworkObjectIds, by slot).</summary>
        public readonly NetworkVariable<double> CutsceneAt = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<Vector3> CutsceneSpot = new NetworkVariable<Vector3>();
        public readonly NetworkList<ulong> CutsceneRiders = new NetworkList<ulong>();
        /// <summary>Seconds of warning before an airdrop lands (the one and only airdrop announcement).</summary>
        public const float DropWarning = 15f;
        int m_DropsWarned;

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
            Tutorial.Reset();
            State.OnValueChanged += OnStateChanged;
            Tunables.OnValueChanged += OnTunablesChanged;
            MapKey.OnValueChanged += OnMapKeyChanged; // (the host changing the options in the lobby)
            if (!IsServer)
            {
                Cfg.ApplyHost(Tunables.Value.ToString());
                // build the host's map (same map + seed = same terrain and UFO spots)
                Cfg.SetMap(MapKey.Value, MapSeed.Value);
                GameSettings.ApplyHostGraphics(Cfg.HostGraphics); // the host picks the graphics for everyone
                if (!MapBuilder.IsBuilt(MapKey.Value, MapSeed.Value)) MapBuilder.Build();
            }
            if (IsServer)
            {
                MapKey.Value = Cfg.MapKey;
                MapSeed.Value = Cfg.MapSeed;
                var data = Cfg.Serialize(true); // only what the host changed (the client starts from the defaults)
                if (System.Text.Encoding.UTF8.GetByteCount(data) < 4000) Tunables.Value = new FixedString4096Bytes(data);
                else Debug.LogError("[RockGame] Settings too large to sync: " + data.Length);
                BuildGrid.Registry.Clear();
                for (int i = 0; i < LaneTotal; i++) { Lanes.Add(new DropLaneState { Start = -1 }); m_Lanes[i] = new DropLane(); }
                SpawnNodes();
                GambleMachine.ServerSpawnAll(); // DNA mode: a gambling machine in every base
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
            }
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            Tunables.OnValueChanged -= OnTunablesChanged;
            MapKey.OnValueChanged -= OnMapKeyChanged;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
            if (Instance == this) Instance = null;
            AirdropShip.Clear();
            VictoryCutscene.Clear();
            PortalFx.Clear();
            GraveFx.Clear();
            Ragdoll.Clear();
            foreach (var v in m_ItemVisuals.Values) if (v) Destroy(v);
            m_ItemVisuals.Clear();
        }

        void OnTunablesChanged(FixedString4096Bytes prev, FixedString4096Bytes cur)
        {
            if (!IsServer) Cfg.ApplyHost(cur.ToString());
        }

        /// <summary>Clients: the host changed the match options in the lobby (the game mode, team sizes...; never the map,
        /// size or seed: those are fixed once hosted, so nothing is rebuilt).</summary>
        void OnMapKeyChanged(int prev, int cur)
        {
            if (!IsServer && prev != cur) Cfg.SetMap(cur, MapSeed.Value);
        }

        /// <summary>Server: the host's GAME OPTIONS in the lobby - a new map key (same map, size and seed) and the tunables
        /// (game length, mode options) go to everyone, everybody's READY is cleared, and anyone on a team that's now too
        /// small moves to one with room.</summary>
        public void ServerApplyLobbyOptions(int key)
        {
            if (!IsServer || S != GameState.Waiting) return;
            int keep = Cfg.MapKey & (15 | (3 << Cfg.SizeShift) | Cfg.SmallBit);
            key = (key & ~(15 | (3 << Cfg.SizeShift) | Cfg.SmallBit)) | keep;
            Bootstrap.MapChoice = key;
            Cfg.SetMap(key, Cfg.MapSeed);
            MapKey.Value = Cfg.MapKey;
            var data = Cfg.Serialize(true);
            if (System.Text.Encoding.UTF8.GetByteCount(data) < 4000) Tunables.Value = new FixedString4096Bytes(data);
            foreach (var p in PlayerNet.All)
            {
                if (p == null) continue;
                int on = 0;
                foreach (var o in PlayerNet.All) if (o != null && o != p && o.Team.Value == p.Team.Value) on++;
                if (p.Team.Value < Cfg.TeamCount && on < Cfg.TeamCap(p.Team.Value)) continue;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int n = 0;
                    foreach (var o in PlayerNet.All) if (o != null && o != p && o.Team.Value == t) n++;
                    if (n < Cfg.TeamCap(t)) { p.ServerSetLobbyTeam(t); break; }
                }
            }
            PlayerNet.ServerUnreadyAll();
            Broadcast("The host changed the match options - READY again");
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
                case GameState.PreBall: if (Cfg.FunRules) break; if (Cfg.Tutorial) { Hud.Banner("TUTORIAL", "Do each step on the left. The clock is stopped."); break; } Hud.Banner("GATHER & BUILD", $"The ball waits under the glass dome - the walls drop in {Clock(Bootstrap.Fast ? Cfg.FastBallDropDelay : Cfg.BallDropDelay)}. " + (Cfg.Builder ? $"BUILDER: build and craft anywhere ({Binds.Name(Bind.Inventory)})." : $"Craft anywhere inside your base ({Binds.Name(Bind.Inventory)}).")); break;
                case GameState.BallLive:
                    // the build phase is over: the glass wall and the dome slide down into the ground (fun modes: no build phase, gone at once)
                    if ((GameState)prev == GameState.PreBall && !Cfg.FunRules) MapBuilder.DropGlassWall();
                    Hud.Banner(Cfg.FunRules ? "THE WALL IS DOWN" : "THE WALL IS DROPPING", Cfg.Builder ? "Grab the ball and plant it anywhere (E) - whoever's ball it is when time runs out wins!" : "Grab the ball from the middle and put it in YOUR machine's socket!"); break;
                case GameState.SuddenDeath: Hud.Banner("SUDDEN DEATH", "Welcome to space. Rocks only. First kill wins - and don't fall off!"); break;
            }
        }


        float m_LastTimeLeft = -1f;
        /// <summary>Every peer: "1 MINUTE LEFT" across the middle of the screen as the match clock passes a minute.</summary>
        void TickMinuteNotice()
        {
            if (!IsSpawned || S != GameState.BallLive || Overtime.Value || Cfg.Tutorial) { m_LastTimeLeft = -1f; return; }
            float tl = TimeLeft;
            if (m_LastTimeLeft > 60f && tl <= 60f && tl > 50f)
            {
                Hud.Banner("1 MINUTE LEFT", "Get the ball into your machine before the clock runs out!");
                Sfx.Play2D(Sfx.Ding, 0.6f, 0f);
            }
            m_LastTimeLeft = tl;
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
            VictoryCutscene.Tick(this); // the winners beamed up into a UFO (when the ball in their socket won it)
            PortalFx.Sync(this);
            GraveFx.Sync(this);
            TickStartCountdown(); // the waiting stadium's 10 s countdown once everyone's in (NetGame.Lobby.cs)
            TickBenchUnlockNotice(); // "WORK BENCHES UNLOCKED" when our team captures the ball (NetGame.Bench.cs)
            TickMinuteNotice();
            if (!IsServer) return;
            ResourceNode.ServerFlocksTick(); // (flocks of birds sitting in a few trees, there before anyone hits them)
            if (Time.time >= m_NextItemCheck) { m_NextItemCheck = Time.time + 0.5f; ServerSettleItems(); }
            double now = NetworkManager.ServerTime.Time;
            if (TimerPaused.Value)
            {
                // frozen: push every deadline back by the time that passed
                double dt = Time.deltaTime;
                if (S == GameState.PreBall || S == GameState.BallLive || S == GameState.SuddenDeath) PhaseEnd.Value += dt;
                if (m_BallStart >= 0) m_BallStart += dt;
            }
            ServerTickC4(now);
            ServerTickAirstrikes(now);
            ServerTickBushes(now);
            ServerTickModes(now);
            ServerTickGameModes(now); // (Bedwars, 3 Goal, Progress, Assassin, Domination: NetGame.GameModes.cs)
            ServerTickBenchUnlock(); // the Workbench T1 unlocks once a team has captured the ball
            Tutorial.ServerTick(this); // tutorial: clock stopped, late joiners in, the wall drops once everyone reaches it
            SpaceArena.ServerTick(this); // sudden death and the waiting stadium: falling off the platform into space
            ServerTickGraves(now); // old gravestones go (NetGame.Graves.cs)
            ThemeMaps.ServerTick(); // THEME MAPS
            int players = PlayerNet.All.Count;
            bool fast = Bootstrap.Fast;
            switch (S)
            {
                case GameState.Waiting:
                    // (a full lobby, everyone in the stadium, then the start countdown: NetGame.Lobby.cs)
                    if (ServerReadyToStart(players, now))
                    {
                        float delay = Cfg.FunRules ? 0f : fast ? Cfg.FastBallDropDelay : Cfg.BallDropDelay; // fun modes: wall down and ball in from the start
                        SetPhase(GameState.PreBall, delay);
                        if (Cfg.Tutorial) TimerPaused.Value = true; // the tutorial goes at your pace
                        // the ball is there from the start: in the middle of the map, under the glass dome
                        SpawnBall();
                        // out of the waiting stadium and into your base
                        foreach (var p in PlayerNet.All) p.ServerSendHome();
                        Broadcast(Cfg.Tutorial ? "Tutorial started! Do each step on the left - the wall drops when everyone reaches it" : Cfg.FunRules ? "Match started! The wall is down and the ball is in - free items on the way!" : Cfg.NoBall ? $"Match started! Gather and build - the wall drops in {Clock(delay)}" : $"Match started! The ball is under the glass dome in the middle - the wall and the dome drop in {Clock(delay)}");
                    }
                    break;
                case GameState.PreBall:
                    ServerTickAirdrop(now);
                    if (now >= PhaseEnd.Value)
                    {
                        if (Ball.Instance == null) SpawnBall();
                        if (Ball.Instance != null) Ball.Instance.ServerRelease(); // the dome's gone: the ball is up for grabs
                        if (Cfg.NoBall) ServerUnlockBench(0, "the wall dropped"); // (Bedwars / Assassin: no ball to capture - the Trade Station unlocks now)
                        SetPhase(GameState.BallLive, BallPhase);
                        m_BallStart = now;
                        m_DropsDone = 0;
                        m_DropsWarned = 0;
                        m_SuddenDeathAt = -1;
                        if (!Cfg.FunRules) Broadcast(Cfg.Bedwars ? "The glass wall is dropping - go smash the enemy machines!" : Cfg.Assassin ? "The glass wall is dropping - hunt them down and take their skulls!" : "The glass wall and the dome are dropping - grab the BALL in the middle!");
                    }
                    break;
                case GameState.BallLive:
                    ServerTickScheduledDrops(now);
                    ServerTickDropWarning(now);
                    ServerTickAirdrop(now);
                    if (now >= PhaseEnd.Value)
                    {
                        if (m_SuddenDeathAt < 0)
                        {
                            if (Cfg.Builder)
                            {
                                // Builder: whoever's ball it is (planted, not carried or loose) wins
                                int pt = Ball.Instance != null && !Ball.Instance.IsCarried ? Ball.Instance.SocketTeam.Value : -1;
                                if (pt >= 0) { EndGame(pt, $"Ball planted for {Cfg.TeamLabel(pt)}"); break; }
                            }
                            else if (Cfg.ClassicMode && !Cfg.Domination)
                            {
                                // Bedwars, 3 Goal, Progress, Assassin: the leader wins (NetGame.GameModes.cs); level: overtime
                                if (ServerModeTimeUp()) break;
                            }
                            else
                            {
                                // only the machine socket counts - a ball lying around in your base doesn't win
                                int t = Ball.Instance != null ? Ball.Instance.SocketTeam.Value : -1;
                                if (t >= 0) { ServerVictoryCutscene(t, $"{Cfg.TeamName[t]} captured the ball"); break; }
                            }
                            // nobody has it: OVERTIME - it carries on until a team gets the ball into its machine, and that
                            // team wins (Cfg.UseOvertime; off: the old way, the countdown sits on 0, then the sudden death arena)
                            if (Cfg.UseOvertime && !Cfg.Builder)
                            {
                                if (!Overtime.Value)
                                {
                                    Overtime.Value = true;
                                    BannerRpc(new FixedString64Bytes("OVERTIME"), new FixedString128Bytes("First team to get the ball into its machine wins!"));
                                    Broadcast("OVERTIME - first team to get the ball into its machine wins!");
                                }
                                break;
                            }
                            // nobody has it: the countdown sits on 0 for a moment, then everyone goes to the arena
                            m_SuddenDeathAt = now + ZeroHold;
                        }
                        if (now >= m_SuddenDeathAt) StartSuddenDeath();
                    }
                    break;
                case GameState.SuddenDeath:
                    if (!Bootstrap.Solo && !Cfg.Tutorial && AliveTeams(out int last) <= 1)
                        EndGame(last, last >= 0 ? $"{Cfg.TeamName[last]}: last one standing" : "Nobody survived");
                    else if (now >= PhaseEnd.Value)
                        EndGame(-1, "Sudden death ran out of time");
                    break;
            }
        }

        void SetPhase(GameState s, float seconds)
        {
            PhaseEnd.Value = NetworkManager.ServerTime.Time + seconds;
            State.Value = (byte)s;
        }

        /// <summary>Sudden death: Ready? (the first half), Set (the second half), then ROCK! - nobody can move until ROCK!</summary>
        public const float FightCountdown = 3.6f;
        /// <summary>The end-of-match countdown stays on 0 this long before everyone is sent to the sudden death arena.</summary>
        public const float ZeroHold = 1.2f;
        double m_SuddenDeathAt = -1;
        public bool FightFrozen => S == GameState.SuddenDeath && IsSpawned && NetworkManager.ServerTime.Time < FightAt.Value;

        void StartSuddenDeath()
        {
            m_SuddenDeathAt = -1;
            if (Ball.Instance != null && Ball.Instance.IsSpawned) Ball.Instance.NetworkObject.Despawn(true);
            // everybody into the stadium, then Ready? / Set / ROCK! before the duel
            FightAt.Value = NetworkManager.ServerTime.Time + FightCountdown;
            SetPhase(GameState.SuddenDeath, Cfg.SuddenDeathLength + FightCountdown);
            foreach (var p in PlayerNet.All) p.ServerEnterArena();
            Broadcast("Nobody had the ball in their base - SUDDEN DEATH!");
        }

        /// <summary>
        /// Server: the team won with its ball in its machine's socket when the timer ran out - the game's over, and every
        /// peer plays the victory cutscene (VictoryCutscene) before the victory screen: the winners who are alive and still
        /// here are sent home onto their bedrock (off any horse) and beamed up by a UFO over it.
        /// </summary>
        public void ServerVictoryCutscene(int team, string reason)
        {
            if (S == GameState.GameOver) return;
            CutsceneRiders.Clear();
            var spot = Vector3.zero;
            var riders = VictoryCutscene.PickRiders(team, PlayerNet.All);
            foreach (var p in riders)
            {
                if (p.Riding) p.ServerDismount();
                SpawnPoint(team, false, p.Slot.Value, out var pos, out var yaw);
                p.TeleportRpc(pos, yaw);
                CutsceneRiders.Add(p.NetworkObjectId);
                spot += pos;
            }
            if (riders.Count > 0) spot /= riders.Count;
            else SpawnPoint(team, false, 0, out spot, out _);
            spot.y = Cfg.SpawnPos(team).y - 0.05f;
            CutsceneSpot.Value = spot;
            CutsceneAt.Value = NetworkManager.ServerTime.Time;
            EndGame(team, reason);
        }

        public void EndGame(int team, string reason)
        {
            if (S == GameState.GameOver) return;
            Winner.Value = (sbyte)team;
            EndReason.Value = new FixedString128Bytes(reason.Length > 120 ? reason.Substring(0, 120) : reason);
            State.Value = (byte)GameState.GameOver;
        }

        /// <summary>How many teams still have someone alive (and one of them).</summary>
        public int AliveTeams(out int anyTeam)
        {
            anyTeam = -1;
            var teams = new HashSet<int>();
            foreach (var p in PlayerNet.All) if (!p.Dead.Value) teams.Add(p.Team.Value);
            foreach (var t in teams) anyTeam = t;
            return teams.Count;
        }

        public void ServerOnPlayerKilled(PlayerNet victim, PlayerNet killer)
        {
            ServerModesOnKilled(victim, killer); // (Assassin: their skull drops)
            if (S != GameState.SuddenDeath) return;
            // sudden death: no respawns; the last team with someone standing wins
            if (AliveTeams(out int w) <= 1 && !Bootstrap.Solo && !Cfg.Tutorial)
                EndGame(w, w < 0 ? "Nobody survived" : PlayerNet.All.Count <= 2 ? $"{Cfg.TeamName[w]} won the duel" : $"{Cfg.TeamName[w]}: last one standing");
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId) return;
            if (Spectator.ServerIs(clientId)) return; // a spectator going changes nothing (Spectator.cs)
            if (Cfg.Tutorial) return; // nobody wins a tutorial by the others leaving
            if (S != GameState.PreBall && S != GameState.BallLive && S != GameState.SuddenDeath) return;
            // whoever is left: if only one team still has players, they win
            var teams = new HashSet<int>();
            foreach (var p in PlayerNet.All) if (p.OwnerClientId != clientId) teams.Add(p.Team.Value);
            if (teams.Count <= 1)
            {
                int w = -1;
                foreach (var t in teams) w = t;
                EndGame(w < 0 ? 0 : w, "Everyone else left");
            }
        }

        /// <summary>destroyed: the pieces that come down count as destroyed (no rebuilding in their spots for a while -
        /// Structure.ServerNoteDestroyed); false when the owner took the supporting piece down themselves (demolish).</summary>
        public void ServerCollapseCheck(bool destroyed = true)
        {
            var unsupported = BuildGrid.FindUnsupported();
            foreach (var s in unsupported)
                if (s != null && s.IsSpawned)
                {
                    if (destroyed) s.ServerNoteDestroyed();
                    s.NetworkObject.Despawn(true);
                }
        }

        /// <summary>A piece of a base was destroyed at this grid slot: every peer notes when, so the building ghost there
        /// shows red with the seconds left until it can be rebuilt (Structure.RebuildWait; the server refuses it anyway).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void PieceBrokenRpc(byte kind, int i, int j, int l, int d)
        {
            Structure.NoteBroken(new PieceKey(kind, i, j, l, d));
        }

        // ------------------------------------------------------------------ items in the world

        /// <summary>Server: put an item in the world. `stick` keeps it where it is (a spear in a wall); otherwise it lands on the ground below.</summary>
        public int ServerDropItem(ItemStack stack, Vector3 at, Vector3 dir, Vector3 from, bool stick = false)
        {
            if (stack.Empty || stack.Id == Item.Rock) return -1;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            var pos = stick ? at : Ground(at);
            if (!stick && (stack.Id == Item.Spear || stack.Id == Item.Arrow)) dir = Flat(dir);
            int id = m_NextItemId++;
            m_ItemBorn[id] = NetworkManager.ServerTime.Time;
            Items.Add(new DroppedItem { Id = id, Stack = stack, Pos = pos, Dir = dir.normalized, From = from });
            return id;
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
                bool fresh = false;
                if (!m_ItemVisuals.TryGetValue(it.Id, out var go) || !go)
                {
                    fresh = true;
                    go = new GameObject("WorldItem");
                    if (it.Stack.Id == Item.Spear) ItemModels.CreateSpearTipForward(go.transform);
                    else if (it.Stack.Id == Item.Arrow) ItemModels.CreateArrowTipForward(go.transform);
                    else
                    {
                        var m = ItemModels.Create(it.Stack.Id, go.transform);
                        // tools lie flat; small stuff is scaled up a little so it reads on the ground
                        bool longItem = it.Stack.Id == Item.Hatchet || it.Stack.Id == Item.Pickaxe || it.Stack.Id == Item.Bow || it.Stack.Id == Item.BuildingPlan || it.Stack.Id == Item.DeathWand;
                        m.transform.localRotation = longItem ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                        m.transform.localScale = Vector3.one * (it.Stack.Id == Item.Skull ? 4.5f : it.Stack.Id == Item.Ram || it.Stack.Id == Item.Chainsaw ? 1f : 1.6f); // (Assassin's skulls: big, so they're seen)
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
                ItemBounce(it, go.transform, fresh); // (a stack that's been added to bounces)
                ItemGlint(it, go.transform, t < 1f); // (and it glints now and then when you're near: NetGame.ItemGlint.cs)
            }
            if (m_ItemVisuals.Count == m_Seen.Count) return;
            var gone = new List<int>();
            foreach (var kv in m_ItemVisuals) if (!m_Seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { if (m_ItemVisuals[id]) Destroy(m_ItemVisuals[id]); m_ItemVisuals.Remove(id); m_ItemSeenAt.Remove(id); ForgetItemBounce(id); ForgetItemGlint(id); }
        }

        public void Broadcast(string msg) => BroadcastRpc(new FixedString128Bytes(msg.Length > 120 ? msg.Substring(0, 120) : msg));

        [Rpc(SendTo.ClientsAndHost)]
        void BroadcastRpc(FixedString128Bytes msg) => Hud.Push(msg.ToString());

        // ------------------------------------------------------------------ spawning

        void SpawnBall()
        {
            if (Ball.Instance != null && Ball.Instance.IsSpawned) return;
            if (Cfg.NoBall) return; // (Bedwars and Assassin have no ball)
            var go = Instantiate(Bootstrap.I.ballPrefab, Ball.DomeSpot, Quaternion.identity);
            go.GetComponent<NetworkObject>().Spawn(true);
            go.GetComponent<Ball>().ServerPlaceInDome(); // sitting still under the glass dome until the wall drops
        }

        void SpawnNodes()
        {
            Physics.SyncTransforms(); // the map was only just built: its colliders have to be where they look
            var rng = new System.Random(1337 + Cfg.MapSeed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var placed = new List<Vector3>();
            float half0 = Cfg.MapHalf;
            // wood mode has no stone at all: every rock becomes a tree
            byte rockKind = Cfg.WoodMode ? ResourceNode.Tree : ResourceNode.Boulder;
            // the rocks on the Highlands hills are real, minable stone nodes
            foreach (var p in MapBuilder.WildRocks)
            {
                if (InsideScenery(p)) continue;
                if (Cfg.DnaRules && rng.NextDouble() > Cfg.DnaRockShare) continue; // DNA mode: rocks are rarer
                bool close = false;
                foreach (var q in placed) if ((q - p).sqrMagnitude < 6.5f * 6.5f) { close = true; break; } // (the stone nodes are bigger now: room between them)
                if (close) continue;
                placed.Add(p);
                int seed = rng.Next();
                for (int m = 0; m < Cfg.Copies; m++) SpawnNode(rockKind, Cfg.Copy(p, m), seed);
            }
            // wild horses, the same number in each half
            if (Bootstrap.I.vehiclePrefab != null)
                for (int h = 0; h < Mathf.Max(Cfg.HorsesPerSide, Mathf.RoundToInt(Cfg.HorsesPerSide * Cfg.MapHalf / 100f)); h++)
                {
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        var p = new Vector3(R(-half0 + 10, half0 - 10), 0, R(-half0 + 10, -8f));
                        if (!Cfg.InFirstSector(p, 6f) || new Vector2(p.x, p.z).magnitude < MapBuilder.DomeRadius + 4f) continue; // (not under the glass dome)
                        if (Mathf.Abs(p.x - Cfg.BaseCenter[0].x) < Cfg.BaseHalf + 6 && Mathf.Abs(p.z - Cfg.BaseCenter[0].z) < Cfg.BaseHalf + 6) continue;
                        if (!ThemeMaps.SpotOk(p)) continue; // THEME MAPS
                        if (InsideScenery(p)) continue;
                        float yaw = R(0, 360);
                        // now and then it's a Wild Unicorn - in every half at once, so it's fair (UnityEngine.Random, so the
                        // map's own random sequence isn't disturbed)
                        bool unicorn = Random.value < Cfg.UnicornChance;
                        for (int m = 0; m < Cfg.Copies; m++)
                        {
                            var q = Cfg.Copy(p, m);
                            q.y = MapBuilder.Height(q.x, q.z) + 0.2f;
                            Vehicle.ServerSpawn(Vehicle.Horse, q, yaw + m * 360f / Cfg.Copies, unicorn);
                        }
                        break;
                    }
                }
            float area = Cfg.MapHalf / 100f; // bigger maps get more of everything
            area *= area;
            area *= 2f / Cfg.Copies; // a quarter of the map per team in free for all
            int trees = Mathf.Max(6, Mathf.RoundToInt(24 * area)), stones = Mathf.Max(4, Mathf.RoundToInt(18 * area)), bushes = Mathf.Max(2, Mathf.RoundToInt(5 * area));
            // THEME MAPS: more or fewer of each to suit the map
            trees = Mathf.RoundToInt(trees * ThemeMaps.NodeMul(ResourceNode.Tree)); stones = Mathf.RoundToInt(stones * ThemeMaps.NodeMul(ResourceNode.Boulder)); bushes = Mathf.RoundToInt(bushes * ThemeMaps.NodeMul(ResourceNode.Bush));
            if (Cfg.DnaRules) stones = Mathf.Max(2, Mathf.RoundToInt(stones * Cfg.DnaRockShare)); // DNA mode: rocks are rarer
            // fallen logs (wood, with an X on them too): Plains and Highlands
            int logs = ThemeMaps.IsTheme ? 0 : Mathf.Max(2, Mathf.RoundToInt(3.5f * area)); // (half as many as there were)
            float half = Cfg.MapHalf;
            for (int n = 0; n < trees + stones + bushes + logs; n++)
            {
                byte kind = n < trees ? ResourceNode.Tree : n < trees + stones ? rockKind : n < trees + stones + bushes ? ResourceNode.Bush : ResourceNode.Log;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var p = new Vector3(R(-half + 8, half - 8), 0, R(-half + 8, -5f));
                    if (!Cfg.InFirstSector(p, 4f)) continue;
                    if (Mathf.Abs(p.x - Cfg.BaseCenter[0].x) < Cfg.BaseHalf + 3 && Mathf.Abs(p.z - Cfg.BaseCenter[0].z) < Cfg.BaseHalf + 3) continue;
                    if (new Vector2(p.x, p.z).magnitude < 12f) continue;
                    if (!ThemeMaps.SpotOk(p)) continue; // THEME MAPS
                    if (InsideScenery(p)) continue;
                    bool close = false;
                    foreach (var q in placed) if ((q - p).sqrMagnitude < 7.5f * 7.5f) { close = true; break; }
                    if (close) continue;
                    int seed = rng.Next();
                    if (kind == ResourceNode.Log)
                    {
                        bool fits = true;
                        for (int m = 0; m < Cfg.Copies && fits; m++) fits = LogFits(Cfg.Copy(p, m), seed); // (every team's copy lies the same way round)
                        if (!fits) continue;
                    }
                    placed.Add(p);
                    for (int m = 0; m < Cfg.Copies; m++) SpawnNode(kind, Cfg.Copy(p, m), seed); // copied round so every team gets the same layout
                    break;
                }
            }
        }

        /// <summary>A fallen log (turned by its seed, like every node) lies flat enough here: not across a steep slope, and
        /// the ground along it not too far off the straight line between its ends.</summary>
        static bool LogFits(Vector3 p, int seed)
        {
            var q = Quaternion.Euler(0, seed % 360, 0);
            Vector3 along = q * Vector3.right * 2.4f, across = q * Vector3.forward * 1f;
            float H(Vector3 v) => MapBuilder.Height(v.x, v.z);
            float a = H(p - along), b = H(p + along), c = H(p);
            if (Mathf.Abs(H(p + across) - H(p - across)) > 0.5f) return false; // (it would look like it's rolling)
            if (Mathf.Abs(c - (a + b) * 0.5f) > 0.3f) return false;            // (a hump or a dip under it)
            return Mathf.Abs(b - a) < 2.2f;
        }

        /// <summary>Would something standing at p (a tree, rock, bush or horse) be inside the map's own scenery - ruins, cacti, pillars...? Checked for every team's copy of the spot.</summary>
        public static bool InsideScenery(Vector3 p)
        {
            for (int m = 0; m < Cfg.Copies; m++)
            {
                var q = Cfg.Copy(p, m);
                q.y = MapBuilder.Height(q.x, q.z);
                foreach (var h in Physics.OverlapCapsule(q + Vector3.up * 0.9f, q + Vector3.up * 4f, 1.6f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                    if (h.GetComponentInParent<GroundMarker>() == null && h.GetComponentInParent<NetworkObject>() == null) return true;
            }
            return false;
        }

        void SpawnNode(byte kind, Vector3 pos, int seed)
        {
            pos.y = MapBuilder.Height(pos.x, pos.z) - 0.1f;
            var go = Instantiate(Bootstrap.I.nodePrefab, pos, Quaternion.Euler(0, seed % 360, 0));
            go.GetComponent<ResourceNode>().ServerInit(kind, seed);
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        /// <summary>Where a team spawns: on its bedrock, facing the middle of the map (or the arena in sudden death).</summary>
        public static void SpawnPoint(int team, bool arena, out Vector3 pos, out float yaw) => SpawnPoint(team, arena, 0, out pos, out yaw);

        /// <summary>slot: which of the team's players (teammates stand side by side).</summary>
        public static void SpawnPoint(int team, bool arena, int slot, out Vector3 pos, out float yaw)
        {
            team = Mathf.Clamp(team, 0, 3);
            if (arena)
            {
                // on your team's spot around the stadium pit, facing the middle
                var dir = Quaternion.Euler(0, team * 360f / Mathf.Max(2, Cfg.TeamCount), 0) * Vector3.back;
                pos = Cfg.ArenaCenter + dir * 12f + Vector3.Cross(Vector3.up, dir) * Cfg.SlotOffset(slot, 1.5f) + Vector3.up * 0.1f;
                yaw = Quaternion.LookRotation(-dir).eulerAngles.y;
                return;
            }
            pos = Cfg.SpawnPos(team, slot);
            yaw = Cfg.SpawnYaw(team);
        }

        /// <summary>"Respawn in the wild": a truly random free spot anywhere out in the wild (any side of the map, never in a base or the middle).</summary>
        public static void WildSpawnPoint(int team, out Vector3 pos, out float yaw)
        {
            float half = Cfg.MapHalf;
            for (int tries = 0; tries < 400; tries++)
            {
                var p = new Vector3(Random.Range(-half + 8f, half - 8f), 0, Random.Range(-half + 8f, half - 8f));
                if (new Vector2(p.x, p.z).magnitude < 14f) continue;
                bool inBase = false;
                for (int t = 0; t < Cfg.TeamCount && !inBase; t++)
                {
                    var bc = Cfg.BaseCenter[t];
                    inBase = Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 6f && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 6f;
                }
                if (inBase) continue;
                if (!ThemeMaps.SpotOk(p)) continue; // THEME MAPS (not in water, lava or up on a mesa)
                p.y = MapBuilder.Height(p.x, p.z) + 0.1f;
                if (Blocked(p + Vector3.up * 1.1f, new Vector3(0.45f, 0.6f, 0.45f))) continue;
                pos = p;
                yaw = Random.Range(0f, 360f);
                return;
            }
            SpawnPoint(team, false, out pos, out yaw);
        }

        // ------------------------------------------------------------------ airdrops

        /// <summary>The ship arrives and opens its hatch (DropArrive), then beams the crate down - slowly (DropBeam).</summary>
        public const float DropArrive = AirdropShip.Arrive + 1f, DropBeam = 8f;
        /// <summary>Seconds after a lane's start when the crate touches down (the ship arrives, then beams it down).</summary>
        public const float DropLand = DropArrive + DropBeam;
        /// <summary>Lanes 0-3: the world airdrops (one for the whole map, or one per team's side).</summary>
        public const int LaneTotal = 4;

        /// <summary>A lane's timer, the ship on its way and the crate on the ground (server side).</summary>
        class DropLane
        {
            public int Region = -1; // -1 = anywhere, else that team's side of the map
            public bool Incoming;
            public Container Crate;
        }
        readonly DropLane[] m_Lanes = new DropLane[LaneTotal];
        int WorldLaneCount => Cfg.AirdropSides && !Cfg.AirdropCenter ? Cfg.TeamCount : 1;

        public double LaneStartAt(int i) => i < Lanes.Count ? Lanes[i].Start : -1;
        public Vector3 LanePosAt(int i) => i < Lanes.Count ? Lanes[i].Pos : Vector3.zero;

        void SetLane(int i, double start, Vector3 pos) => Lanes[i] = new DropLaneState { Start = start, Pos = pos };

        double m_BallStart = -1;
        int m_DropsDone;
        readonly List<Container> m_OldCrates = new List<Container>();

        /// <summary>Length of the ball phase (from the wall dropping to the end of the match).</summary>
        static float BallPhase => Bootstrap.Fast ? Cfg.FastMatchLength : Cfg.FunRules ? Cfg.FunMatchLength : Cfg.MatchLength;

        /// <summary>
        /// Mode options: N airdrops per match, evenly spaced over the time after the wall drops (1 = half way through,
        /// 2 = at a third and two thirds...). Anywhere: one per drop; one per side: every side gets one; middle: one in the centre.
        /// </summary>
        void ServerTickScheduledDrops(double now)
        {
            if (Cfg.FunRules || Cfg.Tutorial) return; // the fun modes hand out items instead; the tutorial has none
            for (int i = m_OldCrates.Count - 1; i >= 0; i--)
            {
                var c = m_OldCrates[i];
                if (c != null && c.IsSpawned && !c.Empty) continue;
                if (c != null && c.IsSpawned) c.NetworkObject.Despawn(true);
                m_OldCrates.RemoveAt(i);
            }
            int n = Mathf.Clamp(Cfg.AirdropCount, 0, 20);
            if (m_BallStart < 0 || m_DropsDone >= n) return;
            double launch = m_BallStart + (m_DropsDone + 1) * (double)BallPhase / (n + 1);
            if (launch + DropLand > PhaseEnd.Value - 1.0) return; // it couldn't land before the clock runs out: no warning, no ship
            // 15 seconds before the ship comes in (not before the crate lands): the one and only airdrop announcement
            // (where it's coming down)
            if (m_DropsWarned <= m_DropsDone && now >= launch - DropWarning)
            {
                m_DropsWarned = m_DropsDone + 1;
                ServerAirdropWarning(launch);
            }
            if (now < launch) return;
            bool split = Cfg.AirdropSides && !Cfg.AirdropCenter;
            int lanes = split ? Cfg.TeamCount : 1;
            for (int i = 0; i < lanes; i++) if (m_Lanes[i].Incoming) return; // one still on its way: right after it lands
            m_DropsDone++;
            for (int i = 0; i < lanes; i++)
            {
                var lane = m_Lanes[i];
                // a crate nobody emptied stays where it is
                if (lane.Crate != null && lane.Crate.IsSpawned) m_OldCrates.Add(lane.Crate);
                lane.Crate = null;
                lane.Region = split ? i : -1;
                ServerLaunchDrop(i, now, Cfg.AirdropCenter ? PickCenterSpot() : PickDropSpot(lane.Region), false);
            }
        }

        /// <summary>Where scheduled airdrops come down, for the warning.</summary>
        static string DropWhere => Cfg.AirdropCenter ? "It's dropping in the centre of the map!"
            : Cfg.AirdropSides ? "One is dropping on every side of the map - watch for the purple beams"
            : "Somewhere on the map - watch for the purple beam";

        /// <summary>Server: the airdrop warning, DropWarning seconds before its ship comes in - the only notification an airdrop gets.</summary>
        public void ServerAirdropWarning(double shipAt)
        {
            NextDropLands.Value = shipAt;
            int secs = Mathf.Max(1, Mathf.RoundToInt((float)(shipAt - NetworkManager.ServerTime.Time)));
            BannerRpc(new FixedString64Bytes($"AIRDROP IN {secs} SECONDS"), new FixedString128Bytes(DropWhere));
            Broadcast($"Airdrop in {secs} seconds! " + DropWhere);
        }

        /// <summary>Server: forget the countdown once that airdrop's ship is here.</summary>
        void ServerTickDropWarning(double now)
        {
            if (NextDropLands.Value > 0 && now > NextDropLands.Value + 1) NextDropLands.Value = -1;
        }

        /// <summary>"Airdrops in the middle": a free spot right by the centre (next to the ball drop zone, not on the ball).</summary>
        static Vector3 PickCenterSpot()
        {
            for (int tries = 0; tries < 40; tries++)
            {
                var off = Random.insideUnitCircle * (tries < 10 ? 4f : 8f);
                var p = new Vector3(off.x, 0, off.y);
                p.y = MapBuilder.Height(p.x, p.z);
                if (Ball.Instance != null && Vector3.Distance(Ball.Instance.transform.position, p + Vector3.up * 0.5f) < 2.5f) continue;
                if (Blocked(p + Vector3.up * 1.4f, new Vector3(1f, 0.9f, 1f))) continue;
                return p;
            }
            var away = -CrashSite.Dir * 3.5f; // (the side away from the crashed UFO)
            return new Vector3(away.x, MapBuilder.Height(away.x, away.z), away.z);
        }

        void ServerTickAirdrop(double now)
        {
            for (int i = 0; i < LaneTotal; i++) ServerTickLane(i, now);
        }

        void ServerTickLane(int i, double now)
        {
            var lane = m_Lanes[i];
            if (lane.Incoming)
            {
                if (now < LaneStartAt(i) + DropLand) return;
                lane.Incoming = false;
                var pos = LanePosAt(i);
                var go = Instantiate(Bootstrap.I.containerPrefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                lane.Crate = go.GetComponent<Container>();
                // three slots: an explosive, 500-1000 wood and another airdrop item (the tutorial's: just C4 for its raid)
                var crate = Cfg.Tutorial ? new List<ItemStack> { Tutorial.DropLoot } : RollAirdropCrate();
                lane.Crate.ServerInit(Container.Airdrop, 7, Mathf.Max(1, crate.Count), crate);
                go.GetComponent<NetworkObject>().Spawn(true);
                Fx.Server(FxKind.Spawn, pos, Vector3.up);
                return;
            }
            if (lane.Crate != null)
            {
                // taken: the next one comes a minute after the last one was emptied
                if (!lane.Crate.IsSpawned || lane.Crate.Empty)
                {
                    if (lane.Crate.IsSpawned)
                    {
                        Fx.Server(FxKind.Break, lane.Crate.transform.position + Vector3.up * 0.6f, Vector3.up);
                        lane.Crate.NetworkObject.Despawn(true);
                    }
                    lane.Crate = null;
                }
            }
        }

        /// <summary>announce: the dev setting's airdrop says it's coming (a scheduled one was already announced by its warning).</summary>
        void ServerLaunchDrop(int i, double now, Vector3 pos, bool announce)
        {
            var lane = m_Lanes[i];
            SetLane(i, now, pos);
            lane.Incoming = true;
            if (!announce) return;
            string where = lane.Region < 0 ? (Cfg.AirdropCenter ? "In the middle of the map!" : "Look for the purple beam") : $"On the {Cfg.TeamLabel(lane.Region)} side - look for the purple beam";
            BannerRpc(new FixedString64Bytes("AIRDROP INCOMING"), new FixedString128Bytes(where));
            Broadcast("An alien AIRDROP is beaming down! " + where);
        }


        static readonly Item[] k_Explosives = { Item.C4, Item.RocketLauncher, Item.BombBush };

        /// <summary>An airdrop crate's contents, three slots: always an explosive of some kind (C4, a rocket launcher or a
        /// bomb bush - whichever are picked in the mode options, else C4), always 500-1000 wood, and always one more
        /// airdrop item (not an explosive), by its Airdrop Rarity.</summary>
        public static List<ItemStack> RollAirdropCrate()
        {
            var pool = Cfg.AirdropLoot;
            var boom = new List<Item>();
            foreach (var i in k_Explosives) if (pool.Contains(i)) boom.Add(i);
            if (boom.Count == 0) boom.Add(Item.C4);
            var b = boom[Random.Range(0, boom.Count)];
            var other = new List<Item>();
            foreach (var i in pool) if (i != Item.Wood && i != Item.Stone && System.Array.IndexOf(k_Explosives, i) < 0) other.Add(i);
            if (other.Count == 0) foreach (var i in Cfg.AirdropChoices) if (System.Array.IndexOf(k_Explosives, i) < 0) other.Add(i);
            var o = Cfg.PickAirdropItem(other);
            return new List<ItemStack>
            {
                ItemStack.Of(b, 1, Mathf.Clamp(Cfg.MaxData(b), 0, 255)),
                Cfg.DnaSwap(ItemStack.Of(Item.Wood, Random.Range(500, 1001))),
                o == Item.Helmet ? ItemStack.Of(Item.Helmet, 1, 1) : ItemStack.Of(o, 1, Mathf.Clamp(Cfg.MaxData(o), 0, 255)),
            };
        }
        /// <summary>One random OP item from the picked ones, rarer or commoner by its Airdrop Rarity weight.</summary>
        public static ItemStack RollAirdropLoot()
        {
            var id = Cfg.PickAirdropItem(Cfg.AirdropLoot);
            if (id == Item.Wood) return Cfg.DnaSwap(ItemStack.Of(Cfg.WoodMode || Random.value < 0.5f ? Item.Wood : Item.Stone, Mathf.Clamp(Cfg.AirdropResources, 1, 1000)));
            if (id == Item.Helmet) return ItemStack.Of(Item.Helmet, 1, 1);
            return ItemStack.Of(id, 1, Mathf.Clamp(Cfg.MaxData(id), 0, 255));
        }

        /// <summary>Random open spot, never close to any base (region: -1 anywhere, else only that team's side).</summary>
        static Vector3 PickDropSpot(int region)
        {
            float half = Cfg.MapHalf;
            Vector3 best = Vector3.zero;
            float bestD = -1f;
            for (int tries = 0; tries < 120; tries++)
            {
                var p = new Vector3(Random.Range(-half + 12f, half - 12f), 0, Random.Range(-half + 12f, half - 12f));
                if (region >= 0 && Cfg.RegionOf(p) != region) continue;
                if (MapBuilder.GlassUp && new Vector2(p.x, p.z).magnitude < MapBuilder.DomeRadius + 3f) continue; // (not on the glass dome)
                float d = float.MaxValue;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    var c = Cfg.BaseCenter[t];
                    d = Mathf.Min(d, Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.z - c.z)) - Cfg.BaseHalf);
                }
                p.y = MapBuilder.Height(p.x, p.z);
                if (!ThemeMaps.SpotOk(p)) continue; // THEME MAPS
                if (Blocked(p + Vector3.up * 1.4f, new Vector3(1f, 0.9f, 1f))) continue;
                if (d >= Cfg.AirdropBaseDistance) return p;
                if (d > bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>Something other than the ground (a tree, rock, building...) in this box.</summary>
        static bool Blocked(Vector3 c, Vector3 half)
        {
            foreach (var h in Physics.OverlapBox(c, half, Quaternion.identity, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                if (h.GetComponentInParent<GroundMarker>() == null) return true;
            return false;
        }

        /// <summary>A crate on the ground right now (null if none).</summary>
        public Container ActiveDrop
        {
            get
            {
                foreach (var c in Container.All) if (c.IsAirdrop) return c;
                return null;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BannerRpc(FixedString64Bytes title, FixedString128Bytes sub)
        {
            Hud.Banner(title.ToString(), sub.ToString());
            Sfx.Play2D(Sfx.Hum, 0.4f, 0f);
        }

        // ------------------------------------------------------------------ dev settings (pause menu)

        public void DevSkipPhase(GameState from)
        {
            if (S == from) PhaseEnd.Value = NetworkManager.ServerTime.Time;
        }

        public void DevAddTime(float seconds)
        {
            if (S == GameState.PreBall || S == GameState.BallLive || S == GameState.SuddenDeath)
                PhaseEnd.Value = System.Math.Max(NetworkManager.ServerTime.Time + 1, PhaseEnd.Value + seconds);
        }

        public void DevSetTimeLeft(float seconds)
        {
            if (S == GameState.PreBall || S == GameState.BallLive || S == GameState.SuddenDeath)
                PhaseEnd.Value = NetworkManager.ServerTime.Time + seconds;
        }

        public void DevSpawnAirdrop()
        {
            double now = NetworkManager.ServerTime.Time;
            for (int i = 0; i < WorldLaneCount; i++)
            {
                var lane = m_Lanes[i];
                if (lane.Incoming) continue;
                if (lane.Crate != null && lane.Crate.IsSpawned) lane.Crate.NetworkObject.Despawn(true);
                lane.Crate = null;
                lane.Region = WorldLaneCount > 1 ? i : -1;
                ServerLaunchDrop(i, now, Cfg.AirdropCenter && !WallUp ? PickCenterSpot() : PickDropSpot(lane.Region), true); // (not under the glass dome)
            }
        }

        /// <summary>Tutorial (the airdrop step - Tutorial.ServerAction): a real airdrop for this team, on its own lane, coming
        /// down at pos - the ship flies in and beams the crate down like any other, announced like the dev setting's.
        /// False if that team's one is still on its way or its crate hasn't been emptied yet.</summary>
        public bool ServerTutorialDrop(int team, Vector3 pos)
        {
            if (!IsServer || team < 0 || team >= LaneTotal || (S != GameState.PreBall && S != GameState.BallLive)) return false;
            var lane = m_Lanes[team];
            if (lane.Incoming || (lane.Crate != null && lane.Crate.IsSpawned && !lane.Crate.Empty)) return false;
            if (lane.Crate != null && lane.Crate.IsSpawned) lane.Crate.NetworkObject.Despawn(true);
            lane.Crate = null;
            lane.Region = team;
            ServerLaunchDrop(team, NetworkManager.ServerTime.Time, pos, true);
            return true;
        }

        public void DevStartSuddenDeath()
        {
            if (S == GameState.PreBall || S == GameState.BallLive) StartSuddenDeath();
        }

        public void DevRegrowNodes()
        {
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
                if (!n.IsBush) n.ServerRegrow();
        }

        // ------------------------------------------------------------------ berry bushes

        readonly List<(double at, int region)> m_BushQueue = new List<(double, int)>();

        /// <summary>A bush was picked: a new one grows back later somewhere else on the same side.</summary>
        public void ServerScheduleBush(Vector3 where) => m_BushQueue.Add((NetworkManager.ServerTime.Time + Cfg.BushRespawnTime, Cfg.RegionOf(where)));

        void ServerTickBushes(double now)
        {
            for (int i = m_BushQueue.Count - 1; i >= 0; i--)
            {
                if (now < m_BushQueue[i].at) continue;
                int region = m_BushQueue[i].region;
                m_BushQueue.RemoveAt(i);
                float half = Cfg.MapHalf;
                for (int tries = 0; tries < 60; tries++)
                {
                    var p = new Vector3(Random.Range(-half + 8f, half - 8f), 0, Random.Range(-half + 8f, half - 8f));
                    if (Cfg.RegionOf(p) != region || Cfg.BaseTeamAt(p) >= 0 || new Vector2(p.x, p.z).magnitude < MapBuilder.DomeRadius + 2f) continue; // (not under the glass dome)
                    p.y = MapBuilder.Height(p.x, p.z);
                    if (Blocked(p + Vector3.up * 1.3f, new Vector3(1f, 0.9f, 1f))) continue;
                    SpawnNode(ResourceNode.Bush, p, Random.Range(0, 1 << 30));
                    break;
                }
            }
        }

        /// <summary>Fake bomb bush: looks just like a berry bush; whoever picks it gets blown up.</summary>
        public void ServerSpawnBombBush(Vector3 pos, int team)
        {
            pos.y = MapBuilder.Height(pos.x, pos.z);
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) pos = hit.point;
            var go = Instantiate(Bootstrap.I.nodePrefab, pos - Vector3.up * 0.1f, Quaternion.Euler(0, Random.Range(0, 360), 0));
            var n = go.GetComponent<ResourceNode>();
            n.ServerInit(ResourceNode.Bush, Random.Range(0, 1 << 30));
            n.TrapTeam.Value = (byte)team;
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        // ------------------------------------------------------------------ explosions (C4, rockets, bomb bush, airstrike)

        struct PendingC4 { public Vector3 Pos, Normal; public int Team; public PlayerNet Thrower; public double At; public Structure On; public Container OnBox; public NetworkObject Stuck; public Vector3 Local; }
        readonly List<PendingC4> m_C4 = new List<PendingC4>();

        /// <summary>
        /// The chest or workbench a C4 is stuck right onto (null if it's on anything else - a wall next to one, the ground):
        /// a short ray back into the surface it landed on (`normal` points out of it) has to hit the box itself first.
        /// </summary>
        public static Container C4StuckBox(Vector3 pos, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            normal.Normalize();
            var from = pos + normal * 0.25f;
            RaycastHit best = default;
            bool found = false;
            foreach (var h in Physics.RaycastAll(from, -normal, 0.6f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PlayerNet>() != null) continue;
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            if (!found) return null;
            var c = best.collider.GetComponentInParent<Container>();
            return c != null && c.IsSpawned && (c.Breakable || c.IsWorkbench) ? c : null;
        }

        public void ServerArmC4(Vector3 pos, Vector3 normal, PlayerNet thrower)
        {
            // stuck right onto a chest or workbench: that's what it blows up (nothing through walls - see ServerBlast)
            var box = C4StuckBox(pos, normal);
            // the piece it's stuck to (or the nearest one right next to it): refined and sheet metal only give way around that one
            Structure on = null;
            float best = 1.2f;
            foreach (var h in Physics.OverlapSphere(pos, 1.2f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                var s = h.GetComponentInParent<Structure>();
                if (s == null || !s.IsSpawned) continue;
                float d = Vector3.Distance(h.ClosestPoint(pos), pos);
                if (d < best) { best = d; on = s; }
            }
            if (box != null) on = null; // (on a chest / bench: no fortified piece is the one it's stuck to)
            m_C4.Add(new PendingC4 { Pos = pos, Normal = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up, Team = thrower.Team.Value, Thrower = thrower, At = NetworkManager.ServerTime.Time + Cfg.C4Fuse, On = on, OnBox = box });
            Fx.Server(FxKind.C4Placed, pos, normal);
        }

        /// <summary>
        /// C4 stuck onto a player or a horse (a car, Slenderman): it rides along with them (every peer parents its beeping
        /// charge to them - C4StuckRpc) and goes off wherever they are when the fuse runs out. If they die or are gone
        /// before that, it goes off where they last were.
        /// </summary>
        public void ServerArmC4On(NetworkObject target, Vector3 local, Vector3 localNormal, PlayerNet thrower)
        {
            if (localNormal.sqrMagnitude < 0.01f) localNormal = Vector3.up;
            m_C4.Add(new PendingC4 { Pos = target.transform.TransformPoint(local), Normal = Vector3.up, Team = thrower.Team.Value, Thrower = thrower, At = NetworkManager.ServerTime.Time + Cfg.C4Fuse, Stuck = target, Local = local });
            C4StuckRpc(target, local, localNormal.normalized);
        }

        /// <summary>Everyone sees (and hears) the charge on whoever it's stuck to, moving with them.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void C4StuckRpc(NetworkObjectReference target, Vector3 local, Vector3 localNormal)
        {
            if (target.TryGet(out var no)) C4Bomb.SpawnOn(no.transform, local, localNormal);
        }

        /// <summary>For the tests: charges ticking right now, and how many of them are stuck to someone.</summary>
        public int C4Pending => m_C4.Count;
        public int C4StuckPending { get { int n = 0; foreach (var c in m_C4) if (c.Stuck != null) n++; return n; } }

        /// <summary>
        /// What a blast does to a piece, by how far it's been fortified. C4: wood and stone go; sheet metal goes only in the
        /// layer the C4 is on (the piece it's stuck to and the ones next to it on the same side - not the ones behind);
        /// refined only the piece it's stuck to. Rockets do less to metal and refined. An airstrike flattens wood and stone
        /// and knocks metal and refined down a step. Returns true if the piece was destroyed.
        /// </summary>
        bool ServerBlastPiece(Structure s, BlastKind kind, float structureDamage, Vector3 pos, float radius, Structure on, Vector3 normal)
        {
            int tier = s.Upgradable ? s.Tier.Value : 0;
            if (kind == BlastKind.C4 && tier >= 2)
            {
                bool stuck = s == on;
                bool layer = false;
                if (on != null && on.IsSpawned && !stuck)
                {
                    var off = s.transform.position - on.transform.position;
                    // same side of the wall (not behind it), and right next to it
                    bool flatS = s.PType == PieceType.Floor || s.PType == PieceType.Foundation, flatOn = on.PType == PieceType.Floor || on.PType == PieceType.Foundation;
                    layer = flatS == flatOn && Mathf.Abs(Vector3.Dot(off, normal)) < 0.8f && off.magnitude < 3.4f;
                }
                if (tier >= 3 ? !stuck : !(stuck || layer)) return false;
                s.ServerDamage(s.Health.Value + 1f, false);
                return true;
            }
            if (kind == BlastKind.Airstrike && tier >= 2) { s.ServerDowngrade(); return false; }
            float d = structureDamage < 0 ? s.Health.Value + 1f : structureDamage * Mathf.Lerp(1f, 0.5f, Vector3.Distance(s.transform.position, pos) / radius);
            if (kind == BlastKind.Rocket) d *= Cfg.TierBlastMul(tier);
            bool gone = d >= s.Health.Value;
            s.ServerDamage(d, false);
            return gone;
        }

        public enum BlastKind { Other, C4, Rocket, Airstrike }

        void ServerTickC4(double now)
        {
            for (int i = m_C4.Count - 1; i >= 0; i--)
            {
                // stuck to someone: it's wherever they are (where they last were, once they're dead or gone)
                var on = m_C4[i].Stuck;
                if (on != null && on.IsSpawned && !(on.TryGetComponent(out PlayerNet sp) && sp.Dead.Value))
                {
                    var moved = m_C4[i];
                    moved.Pos = on.transform.TransformPoint(moved.Local);
                    m_C4[i] = moved;
                }
                if (now < m_C4[i].At) continue;
                var c = m_C4[i];
                m_C4.RemoveAt(i);
                // (everyone's buildings - your own C4 blows up your own base too; trees in the blast are felled, like a rocket's)
                int n = ServerBlast(c.Pos, Cfg.C4Radius, -1, Cfg.C4PlayerDamage, -1f, Cfg.C4KillRadius, c.Thrower, false, true, BlastKind.C4, c.On, c.Normal, c.OnBox);
                if (c.Thrower != null && n > 0) c.Thrower.NotifyPublic($"Your C4 destroyed {n} piece{(n == 1 ? "" : "s")}!");
            }
        }

        public void ServerRocket(Vector3 pos, PlayerNet shooter)
        {
            // rockets also blow up any trees in the blast (they fall and regrow like felled ones)
            int n = ServerBlast(pos, Cfg.RocketRadius + 1f, -1, Cfg.RocketPlayerDamage, Cfg.RocketStructureDamage, 0.8f, shooter, false, true, BlastKind.Rocket); // your own base too
            if (shooter != null && n > 0) shooter.NotifyPublic($"Your rocket destroyed {n} piece{(n == 1 ? "" : "s")}!");
        }

        /// <summary>
        /// An explosion. `team`: that team's own buildings are spared (-1 = nothing is spared, -2 = buildings are not hurt at all).
        /// structureDamage &lt; 0 destroys pieces outright. Players take damage falling off with distance; inside killRadius they die.
        /// C4 only breaks the chest or workbench it's stuck right onto (`onBox`): a chest spills what's in it, a workbench drops
        /// as its item (and its team can put that or another one down). Nothing else ever breaks a workbench; the other blasts
        /// still break chests nearby. Returns how many building pieces were destroyed.
        /// </summary>
        public int ServerBlast(Vector3 pos, float radius, int team, float playerDamage, float structureDamage, float killRadius, PlayerNet attacker, bool nodes, bool trees = false,
            BlastKind kind = BlastKind.Other, Structure on = null, Vector3 normal = default, Container onBox = null)
        {
            Fx.Server(FxKind.Explosion, pos, Vector3.up);
            ServerMaybeHitMachine(pos, attacker, true); // (Bedwars: an explosive right on an enemy machine destroys it)
            var structures = new HashSet<Structure>();
            var chests = new HashSet<Container>();
            var creatures = new HashSet<Vehicle>();
            var hitNodes = new HashSet<ResourceNode>();
            foreach (var h in Physics.OverlapSphere(pos, radius, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Collide))
            {
                var s = h.GetComponentInParent<Structure>();
                if (s != null && s.IsSpawned && team != -2 && s.Team.Value != team) structures.Add(s);
                var c = h.GetComponentInParent<Container>();
                if (c != null && c.IsSpawned && c.Breakable && team != -2 && c.Team.Value != team && kind != BlastKind.C4) chests.Add(c); // (C4: only the one it's on)
                var v = h.GetComponentInParent<Vehicle>();
                if (v != null && v.IsSpawned) creatures.Add(v);
                var n = h.GetComponentInParent<ResourceNode>();
                if (n != null && n.IsSpawned && (nodes || (trees && n.IsWood && n.Amount.Value > 0))) hitNodes.Add(n);
            }
            if (kind == BlastKind.C4 && onBox != null && onBox.IsSpawned && team != -2 && onBox.Team.Value != team)
            {
                if (onBox.IsWorkbench) onBox.ServerBreakBench();
                else if (onBox.Breakable) chests.Add(onBox);
            }
            foreach (var c in chests) if (c.IsSpawned) c.ServerBreak();
            int destroyed = 0;
            // (the piece the C4 is stuck to goes last: the others are judged by where they are next to it)
            foreach (var s in structures)
            {
                if (!s.IsSpawned || s == on) continue;
                if (ServerBlastPiece(s, kind, structureDamage, pos, radius, on, normal)) destroyed++;
            }
            if (on != null && structures.Contains(on) && on.IsSpawned && ServerBlastPiece(on, kind, structureDamage, pos, radius, on, normal)) destroyed++;
            if (structures.Count > 0) ServerCollapseCheck();
            foreach (var v in creatures) if (v.IsSpawned) v.ServerDamage(playerDamage * 2f, attacker);
            // raiding breaks portals (C4, rockets, airstrikes - not the fake bomb bush)
            if (kind != BlastKind.Other)
            {
                int portals = ServerBreakPortals(pos, radius);
                if (portals > 0 && attacker != null) attacker.NotifyPublic($"Your {(kind == BlastKind.C4 ? "C4" : kind == BlastKind.Rocket ? "rocket" : "airstrike")} broke {(portals == 1 ? "a portal" : portals + " portals")}!");
            }
            foreach (var n in hitNodes)
            {
                if (!n.IsSpawned) continue;
                if (trees && !nodes) Fx.Server(FxKind.Timber, n.transform.position + Vector3.up * 2f, Vector3.up);
                n.ServerDeplete();
            }
            foreach (var p in PlayerNet.All.ToArray())
            {
                if (p.Dead.Value) continue;
                float d = Vector3.Distance(p.transform.position + Vector3.up, pos);
                if (d > radius) continue;
                byte cause = kind == BlastKind.C4 ? (byte)Item.C4 : kind == BlastKind.Rocket ? (byte)Item.RocketLauncher : kind == BlastKind.Airstrike ? (byte)Item.Airstrike : (byte)Item.BombBush;
                if (d <= killRadius) p.ServerKill(attacker, cause);
                else p.ServerDamage(playerDamage * Mathf.Lerp(1f, 0.3f, d / radius), attacker, cause);
            }
            return destroyed;
        }

        // ------------------------------------------------------------------ airstrike

        readonly List<(Vector3 pos, double at, PlayerNet by)> m_Strikes = new List<(Vector3, double, PlayerNet)>();

        public void ServerAirstrike(Vector3 pos, PlayerNet by)
        {
            pos.y = MapBuilder.Height(pos.x, pos.z);
            m_Strikes.Add((pos, NetworkManager.ServerTime.Time + Cfg.AirstrikeDelay, by));
            Fx.Server(FxKind.AirstrikeWarn, pos, new Vector3(Cfg.AirstrikeRadius, Cfg.AirstrikeDelay, 0));
            BannerRpc(new FixedString64Bytes("AIRSTRIKE INBOUND"), new FixedString128Bytes($"{(by != null ? by.DisplayName : "Someone")} called an airstrike - get out of the red zone!"));
        }

        void ServerTickAirstrikes(double now)
        {
            for (int i = m_Strikes.Count - 1; i >= 0; i--)
            {
                if (now < m_Strikes[i].at) continue;
                var (pos, _, by) = m_Strikes[i];
                m_Strikes.RemoveAt(i);
                // a carpet of bombs, then everything in the zone is flattened
                for (int k = 0; k < 7; k++)
                {
                    var off = Random.insideUnitCircle * Cfg.AirstrikeRadius * 0.8f;
                    Fx.Server(FxKind.Explosion, pos + new Vector3(off.x, 0.5f, off.y), Vector3.up);
                }
                ServerBlast(pos, Cfg.AirstrikeRadius, -1, 9999f, -1f, Cfg.AirstrikeRadius, by, true, false, BlastKind.Airstrike);
            }
        }

        // ------------------------------------------------------------------ build egg

        /// <summary>Build egg: blocks appear under its flight path as it flies, making a staircase you can walk along.</summary>
        public void ServerBuildEgg(Vector3 origin, Vector3 vel, int team) => StartCoroutine(BuildEggRoutine(origin, vel, team));

        System.Collections.IEnumerator BuildEggRoutine(Vector3 pos, Vector3 vel, int team)
        {
            const float dt = 0.02f, spacing = 0.75f, stepH = 0.4f;
            float travelled = spacing, t = 0f;
            int blocks = 0;
            float lastY = float.NaN;
            var flat = new Vector3(vel.x, 0, vel.z).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            var rot = Quaternion.LookRotation(flat);
            while (blocks < 40 && t < 6f)
            {
                var step = vel * dt;
                vel += Vector3.down * 9.81f * dt;
                if (Physics.Raycast(pos, step.normalized, step.magnitude + 0.2f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) break;
                pos += step;
                t += dt;
                travelled += new Vector2(step.x, step.z).magnitude;
                if (travelled >= spacing)
                {
                    travelled = 0f;
                    // a slab just under the egg's path, snapped to walkable step heights
                    float y = Mathf.Round((pos.y - 1.1f) / stepH) * stepH;
                    if (!float.IsNaN(lastY)) y = Mathf.Clamp(y, lastY - stepH * 3, lastY + stepH);
                    lastY = y;
                    var bp = new Vector3(pos.x, y, pos.z);
                    if (bp.y < MapBuilder.Height(bp.x, bp.z) - 0.3f) break;
                    if (!Physics.CheckBox(bp + Vector3.up * 0.2f, new Vector3(0.55f, 0.18f, 0.35f), rot, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                    {
                        var go = Instantiate(Bootstrap.I.structurePrefab, bp, rot);
                        go.GetComponent<Structure>().ServerInit(PieceType.EggBlock, team, default, false);
                        go.GetComponent<NetworkObject>().Spawn(true);
                        blocks++;
                    }
                }
                yield return new WaitForSeconds(dt);
            }
            Fx.Server(FxKind.Spawn, pos, Vector3.up);
        }

        // ------------------------------------------------------------------ portals

        /// <summary>Portal gun: each gun makes one linked pair (its two shots). Portals stay for the whole game (unless they're
        /// raided: ServerBreakPortals), every one a different colour.</summary>
        public void ServerAddPortal(Vector3 pos, Vector3 normal, int pair)
        {
            // past MaxPortals, the oldest pair (not the one being made) goes
            while (Portals.Count >= MaxPortals)
            {
                int oldest = Portals[0].Pair == pair && Portals.Count > 1 ? Portals[1].Pair : Portals[0].Pair;
                for (int i = Portals.Count - 1; i >= 0; i--) if (Portals[i].Pair == oldest) Portals.RemoveAt(i);
            }
            int index = 0;
            foreach (var p in Portals) if (p.Pair == pair) index++;
            Portals.Add(new PortalInfo { Pos = pos, Normal = normal.normalized, Pair = (short)pair, Index = (byte)index });
            Fx.Server(FxKind.PortalOpen, pos, normal);
        }

        /// <summary>Most portals in the world at once (the oldest pair goes first).</summary>
        public const int MaxPortals = 24;

        public int ServerNewPortalPair() => m_NextPortalPair++;
        /// <summary>How many portal pairs have been started (the server's count; 0 on a client - the portal gun's preview guesses from the portals it can see).</summary>
        public int PortalPairsMade => m_NextPortalPair;
        int m_NextPortalPair;

        /// <summary>
        /// Raiding breaks portals: a raid blast (C4, a rocket, an airstrike - ServerBlast) closes every portal within its radius,
        /// and the other end of its pair with it (a portal on its own goes nowhere). Returns how many pairs it closed.
        /// </summary>
        public int ServerBreakPortals(Vector3 pos, float radius)
        {
            var pairs = new HashSet<int>();
            foreach (var p in Portals) if (Vector3.Distance(p.Pos, pos) <= radius) pairs.Add(p.Pair);
            if (pairs.Count == 0) return 0;
            for (int i = Portals.Count - 1; i >= 0; i--)
            {
                var p = Portals[i];
                if (!pairs.Contains(p.Pair)) continue;
                Portals.RemoveAt(i);
                Fx.Server(FxKind.PortalOpen, p.Pos, p.Normal); // (the same burst of sparks it opened with)
            }
            return pairs.Count;
        }

        /// <summary>Is there a portal within r of p?</summary>
        public bool PortalNear(Vector3 p, float r)
        {
            foreach (var x in Portals) if (Vector3.Distance(x.Pos, p) <= r) return true;
            return false;
        }

        /// <summary>The other portal of this one's pair, if it exists.</summary>
        public bool TryPartner(int i, out PortalInfo partner)
        {
            partner = default;
            var me = Portals[i];
            for (int k = 0; k < Portals.Count; k++)
                if (k != i && Portals[k].Pair == me.Pair) { partner = Portals[k]; return true; }
            return false;
        }
    }

    public struct DropLaneState : INetworkSerializeByMemcpy, System.IEquatable<DropLaneState>
    {
        public double Start;
        public Vector3 Pos;
        public bool Equals(DropLaneState o) => Start == o.Start && Pos == o.Pos;
    }

    public struct PortalInfo : INetworkSerializeByMemcpy, System.IEquatable<PortalInfo>
    {
        public Vector3 Pos, Normal;
        public short Pair;
        public byte Index;
        public bool Equals(PortalInfo o) => Pos == o.Pos && Normal == o.Normal && Pair == o.Pair && Index == o.Index;
        /// <summary>Every portal gets its own colour: the first pair is Portal's blue and orange.</summary>
        public Color Color => ColorOf(Pair, Index);
        /// <summary>The colour of portal `index` (0 or 1) of pair `pair` (the portal gun's preview shows it before the shot).</summary>
        public static Color ColorOf(int pair, int index) => Palette[(Mathf.Max(0, pair) * 2 + index) % Palette.Length];
        static readonly Color[] Palette =
        {
            new Color(0.15f, 0.55f, 1f), new Color(1f, 0.55f, 0.1f), new Color(0.6f, 0.2f, 1f), new Color(0.4f, 1f, 0.2f),
            new Color(1f, 0.2f, 0.7f), new Color(0.1f, 1f, 0.9f), new Color(1f, 0.15f, 0.15f), new Color(1f, 0.95f, 0.2f),
            new Color(1f, 1f, 1f), new Color(0.55f, 0.35f, 0.15f),
        };
    }
}
