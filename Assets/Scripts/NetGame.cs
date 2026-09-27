using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Server-driven match flow: waiting -> gather (ball drop countdown) -> ball live (5 min) -> win or sudden death.</summary>
    public class NetGame : NetworkBehaviour
    {
        public static NetGame Instance;

        public readonly NetworkVariable<byte> State = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> PhaseEnd = new NetworkVariable<double>();
        public readonly NetworkVariable<sbyte> Winner = new NetworkVariable<sbyte>(-1);
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();

        public const float SuddenDeathLength = 180f;

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
        }

        void OnStateChanged(byte prev, byte cur)
        {
            switch ((GameState)cur)
            {
                case GameState.PreBall: Hud.Banner("GATHER & BUILD", "Get wood and stone. Craft a Crafting Table first (TAB)."); break;
                case GameState.BallLive: Hud.Banner("THE BALL HAS DROPPED", "Grab it from the middle and bring it to your base!"); break;
                case GameState.SuddenDeath: Hud.Banner("SUDDEN DEATH", "Rocks only. First kill wins."); break;
            }
        }

        void Update()
        {
            if (!IsServer) return;
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
