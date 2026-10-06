using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>Once every player is in the waiting stadium, the match starts this many seconds later (0 = at once).</summary>
        [Tune("Match")] public static float StartCountdown = 10f;
        /// <summary>The clock running out with the ball in nobody's machine: OVERTIME until someone captures it (off: the
        /// old sudden death arena - the sudden death tests switch it off).</summary>
        public static bool UseOvertime = true;
    }

    /// <summary>
    /// The start of the match: when the lobby is full and the newest player is in the stadium with the others (their own
    /// machine has built the map and put them on the platform: PlayerNet.LobbyReadyRpc), a Cfg.StartCountdown second
    /// countdown runs (StartAt, synced: the HUD, the stadium's screens and a beep each second show it) before everyone
    /// is sent home. It starts again from the top if another player comes in while it runs, and is called off if somebody
    /// leaves. Solo and the tutorial start at once, as before.
    /// </summary>
    public partial class NetGame
    {
        /// <summary>Waiting stadium: the server time the match starts at (-1: no countdown running).</summary>
        public readonly NetworkVariable<double> StartAt = new NetworkVariable<double>(-1);
        /// <summary>Seconds until the match starts (0 when no countdown is running).</summary>
        public float StartsIn => IsSpawned && StartAt.Value >= 0 ? Mathf.Max(0f, (float)(StartAt.Value - NetworkManager.ServerTime.Time)) : 0f;
        public bool StartCounting => IsSpawned && S == GameState.Waiting && StartAt.Value >= 0;

        /// <summary>Server, for the tests: how many times the start countdown has begun (again).</summary>
        public static int StartCountdowns;
        int m_StartPlayers;

        /// <summary>The ship lobby (ShipLobby.cs): the match starts once at least two players are in and every one of them
        /// has pressed READY (it needn't be full). The tests (Bootstrap.Testing) keep the old rule: a full lobby starts.</summary>
        public static bool ReadyLobby => (!Bootstrap.Testing || TestLobby) && !Bootstrap.Solo; // (the tutorial with a friend too)
        /// <summary>(tests: -autotest lobby) the ship lobby and READY, as in a real game.</summary>
        public static bool TestLobby;

        /// <summary>Server, every frame in the waiting stadium: true when the match should start now.</summary>
        bool ServerReadyToStart(int players, double now)
        {
            if ((Bootstrap.Solo || (Cfg.Tutorial && !ReadyLobby)) && players >= 1) return true;
            bool ready = ReadyLobby;
            bool everyone = (ready ? players >= 2 : players >= Cfg.PlayersNeeded) && players >= Spectator.ServerPlayerClients(NetworkManager); // (spectators aren't waited for)
            if (everyone)
                foreach (var p in PlayerNet.All)
                    if (p == null || !p.ServerInStadium || (ready && !p.LobbyReady.Value)) { everyone = false; break; }
            if (!everyone)
            {
                // somebody left, or the newest player isn't in yet: no countdown
                if (StartAt.Value >= 0) { StartAt.Value = -1; Broadcast("Start called off - waiting for players"); }
                m_StartPlayers = 0;
                return false;
            }
            float wait = Bootstrap.Fast ? Mathf.Min(1f, Cfg.StartCountdown) : ready ? Mathf.Min(3f, Cfg.StartCountdown) : Cfg.StartCountdown; // (everyone READY: a short count)
            if (wait <= 0f) return true;
            if (StartAt.Value < 0 || players != m_StartPlayers)
            {
                // everyone's here (or one more just came in): the countdown starts from the top
                m_StartPlayers = players;
                StartAt.Value = now + wait;
                StartCountdowns++;
                return false;
            }
            if (now < StartAt.Value) return false;
            StartAt.Value = -1;
            m_StartPlayers = 0;
            return true;
        }

        int m_StartSecond = -1;

        /// <summary>Every peer: the countdown's banner as it begins and a beep each second (higher on the last three).</summary>
        void TickStartCountdown()
        {
            if (!StartCounting) { m_StartSecond = -1; return; }
            int s = Mathf.CeilToInt(StartsIn);
            if (s == m_StartSecond || s <= 0) return;
            if (m_StartSecond < 0 || s > m_StartSecond) Hud.Banner(ReadyLobby ? "EVERYONE'S READY" : "EVERYONE'S HERE", $"The match starts in {s} seconds - get ready!");
            m_StartSecond = s;
            Sfx.Play2D(s <= 3 ? Sfx.Ding : Sfx.Beep, s <= 3 ? 0.45f : 0.35f, 0f);
        }
    }

    public partial class PlayerNet
    {
        bool m_LobbyReady;
        float m_LobbySince = -1f;

        /// <summary>Server: this player is in the waiting stadium with the others - their machine said so (LobbyReadyRpc),
        /// or they've been connected a good while (so one stuck machine can't hold the match up for ever).</summary>
        public bool ServerInStadium
        {
            get
            {
                if (m_LobbySince < 0f) m_LobbySince = Time.time;
                return m_LobbyReady || Time.time - m_LobbySince > 20f;
            }
        }

        /// <summary>The owner's machine has built the map and put them on the stadium's platform (PlayerController.TryPlace).</summary>
        [Rpc(SendTo.Server)]
        public void LobbyReadyRpc() => m_LobbyReady = true;
    }
    public partial class NetGame
    {
        /// <summary>How long the result stays up after the match (and its cutscene) before everyone's back in the lobby (s).</summary>
        public const float BackToLobbyAfter = 7f;
        float m_OverSince = -1f;

        /// <summary>Seconds until everyone's sent back to the lobby (-1: not counting: the cutscene's still on, or no lobby).</summary>
        public float BackToLobbyIn => m_OverSince < 0f ? -1f : Mathf.Max(0f, BackToLobbyAfter - (Time.time - m_OverSince));

        /// <summary>Server: the match is over and its cutscene has played - a few seconds on the result, then the whole
        /// session goes back to the ship lobby for the next one (Bootstrap.ServerBackToLobby). Lobby games only (not solo,
        /// not the tests).</summary>
        /// <summary>Every client: the host is about to restart the session for the lobby - come straight back in when it drops.</summary>
        [Rpc(SendTo.NotServer)]
        public void BackToLobbyRpc()
        {
            Bootstrap.s_RejoinExpectedUntil = Time.unscaledTime + 10f;
            Debug.Log("[RockGame] the host is going back to the lobby: rejoining when it drops");
        }

        void ServerTickBackToLobby()
        {
            bool waiting = S == GameState.GameOver && ReadyLobby && !Cfg.Tutorial && !VictoryCutscene.Active;
            if (!waiting) { m_OverSince = -1f; return; }
            if (m_OverSince < 0f) m_OverSince = Time.time;
            if (IsServer && Time.time - m_OverSince >= BackToLobbyAfter && Bootstrap.I != null) Bootstrap.I.ServerBackToLobby();
        }
    }
}
