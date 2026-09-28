using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>A thrown spear lying on the ground or stuck in a wall, waiting to be picked up.</summary>
    public struct DroppedSpear : INetworkSerializeByMemcpy, System.IEquatable<DroppedSpear>
    {
        public int Id;
        public Vector3 Pos, Dir; // Pos = tip, Dir = direction the spear points
        public bool Equals(DroppedSpear o) => Id == o.Id && Pos == o.Pos && Dir == o.Dir;
    }

    /// <summary>Server-driven match flow: waiting -> gather (ball drop countdown) -> ball live (5 min) -> win or sudden death.</summary>
    public class NetGame : NetworkBehaviour
    {
        public static NetGame Instance;

        public readonly NetworkVariable<byte> State = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> PhaseEnd = new NetworkVariable<double>();
        public readonly NetworkVariable<sbyte> Winner = new NetworkVariable<sbyte>(-1);
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();

        public readonly NetworkList<DroppedSpear> Spears = new NetworkList<DroppedSpear>();

        public const float SuddenDeathLength = 180f;

        int m_NextSpearId = 1;
        float m_NextSpearCheck;
        readonly Dictionary<int, GameObject> m_SpearVisuals = new Dictionary<int, GameObject>();
        readonly HashSet<int> m_SeenSpears = new HashSet<int>();

        public GameState S => (GameState)State.Value;
        public float TimeLeft => IsSpawned ? Mathf.Max(0f, (float)(PhaseEnd.Value - NetworkManager.ServerTime.Time)) : 0f;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            State.OnValueChanged += OnStateChanged;
            if (IsServer)
            {
                BuildGrid.Registry.Clear();
                SpawnNodes();
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
            }
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
            if (Instance == this) Instance = null;
            foreach (var v in m_SpearVisuals.Values) if (v) Destroy(v);
            m_SpearVisuals.Clear();
        }

        void OnStateChanged(byte prev, byte cur)
        {
            switch ((GameState)cur)
            {
                case GameState.PreBall: Hud.Banner("GATHER & BUILD", "Get wood and stone. Craft anywhere with TAB."); break;
                case GameState.BallLive: Hud.Banner("THE BALL HAS DROPPED", "Grab it from the middle and bring it to your base!"); break;
                case GameState.SuddenDeath: Hud.Banner("SUDDEN DEATH", "Rocks only. First kill wins."); break;
            }
        }

        void Update()
        {
            SyncSpearVisuals();
            if (!IsServer) return;
            if (Time.time >= m_NextSpearCheck) { m_NextSpearCheck = Time.time + 0.5f; ServerSettleSpears(); }
            double now = NetworkManager.ServerTime.Time;
            int players = PlayerNet.All.Count;
            bool fast = Bootstrap.Fast;
            switch (S)
            {
                case GameState.Waiting:
                    if (players >= 2 || (Bootstrap.Solo && players >= 1))
                    {
                        float delay = fast ? Cfg.FastBallDropDelay : Cfg.BallDropDelay;
                        SetPhase(GameState.PreBall, delay);
                        Broadcast($"Match started! The ball drops in {delay:0} seconds");
                    }
                    break;
                case GameState.PreBall:
                    if (now >= PhaseEnd.Value)
                    {
                        SpawnBall();
                        SetPhase(GameState.BallLive, fast ? Cfg.FastMatchLength : Cfg.MatchLength);
                        Broadcast("The BALL has dropped in the middle of the map!");
                    }
                    break;
                case GameState.BallLive:
                    if (now >= PhaseEnd.Value)
                    {
                        int t = Ball.Instance != null ? Ball.Instance.BaseTeam.Value : -1;
                        if (t >= 0) EndGame(t, $"{Cfg.TeamName[t]} held the ball in their base when time ran out!");
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
            SetPhase(GameState.SuddenDeath, SuddenDeathLength);
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

        // ------------------------------------------------------------------ dropped spears

        public void ServerDropSpear(Vector3 tip, Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.down;
            Spears.Add(new DroppedSpear { Id = m_NextSpearId++, Pos = tip, Dir = dir.normalized });
        }

        /// <summary>Removes the spear if it exists and is within range of <paramref name="from"/>.</summary>
        public bool ServerTakeSpear(int id, Vector3 from, float range)
        {
            for (int i = 0; i < Spears.Count; i++)
            {
                if (Spears[i].Id != id) continue;
                if (Vector3.Distance(Spears[i].Pos, from) > range) return false;
                Spears.RemoveAt(i);
                return true;
            }
            return false;
        }

        public bool TryNearestSpear(Vector3 from, float range, out DroppedSpear best)
        {
            best = default;
            float bd = range;
            bool found = false;
            foreach (var sp in Spears)
            {
                float d = Vector3.Distance(sp.Pos - sp.Dir * 0.6f, from); // measure to the middle of the shaft
                if (d < bd) { bd = d; best = sp; found = true; }
            }
            return found;
        }

        /// <summary>Spears stuck in something that got destroyed fall to the ground.</summary>
        void ServerSettleSpears()
        {
            for (int i = 0; i < Spears.Count; i++)
            {
                var sp = Spears[i];
                if (Physics.CheckSphere(sp.Pos, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 p = sp.Pos;
                if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
                else p.y = 0;
                var flat = new Vector3(sp.Dir.x, 0, sp.Dir.z);
                if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
                sp.Pos = p + Vector3.up * 0.05f;
                sp.Dir = (flat.normalized + Vector3.down * 0.15f).normalized;
                Spears[i] = sp;
            }
        }

        void SyncSpearVisuals()
        {
            m_SeenSpears.Clear();
            foreach (var sp in Spears)
            {
                m_SeenSpears.Add(sp.Id);
                if (!m_SpearVisuals.TryGetValue(sp.Id, out var go) || !go)
                {
                    go = new GameObject("DroppedSpear");
                    ItemModels.CreateSpearTipForward(go.transform);
                    m_SpearVisuals[sp.Id] = go;
                }
                go.transform.SetPositionAndRotation(sp.Pos, Quaternion.LookRotation(sp.Dir));
            }
            if (m_SpearVisuals.Count == m_SeenSpears.Count) return;
            var gone = new List<int>();
            foreach (var kv in m_SpearVisuals) if (!m_SeenSpears.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { if (m_SpearVisuals[id]) Destroy(m_SpearVisuals[id]); m_SpearVisuals.Remove(id); }
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
            var rng = new System.Random(1337);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var placed = new List<Vector3>();
            int trees = 24, stones = 18;
            for (int n = 0; n < trees + stones; n++)
            {
                byte kind = n < trees ? ResourceNode.Tree : ResourceNode.Boulder;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var p = new Vector3(R(-Cfg.MapHalf + 8, Cfg.MapHalf - 8), 0, R(-Cfg.MapHalf + 8, -5f));
                    if (Mathf.Abs(p.x - Cfg.BaseCenter[0].x) < Cfg.BaseHalf + 3 && Mathf.Abs(p.z - Cfg.BaseCenter[0].z) < Cfg.BaseHalf + 3) continue;
                    if (new Vector2(p.x, p.z).magnitude < 12f) continue;
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
            var go = Instantiate(Bootstrap.I.nodePrefab, pos, Quaternion.Euler(0, seed % 360, 0));
            go.GetComponent<ResourceNode>().ServerInit(kind, seed);
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        public static void SpawnPoint(int team, bool arena, out Vector3 pos, out float yaw)
        {
            team = Mathf.Clamp(team, 0, 1);
            yaw = team == 0 ? 0f : 180f;
            if (arena)
            {
                pos = Cfg.ArenaCenter + new Vector3(0, 0.1f, team == 0 ? -14f : 14f);
                return;
            }
            pos = Cfg.BaseCenter[team] + new Vector3(team == 0 ? -3.5f : 3.5f, 0, team == 0 ? -15.5f : 15.5f);
            if (Physics.Raycast(pos + Vector3.up * 40f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y + 0.1f;
            else pos.y = 0.1f;
        }
    }
}
