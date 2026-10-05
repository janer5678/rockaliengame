using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>Once every player is in the waiting stadium, the match starts this many seconds later (0 = at once).</summary>
        [Tune("Match")] public static float StartCountdown = 10f;
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

        /// <summary>Server, every frame in the waiting stadium: true when the match should start now.</summary>
        bool ServerReadyToStart(int players, double now)
        {
            if ((Bootstrap.Solo || Cfg.Tutorial) && players >= 1) return true;
            bool everyone = players >= Cfg.PlayersNeeded && players >= Spectator.ServerPlayerClients(NetworkManager); // (spectators aren't waited for)
            if (everyone)
                foreach (var p in PlayerNet.All)
                    if (p == null || !p.ServerInStadium) { everyone = false; break; }
            if (!everyone)
            {
                // somebody left, or the newest player isn't in yet: no countdown
                if (StartAt.Value >= 0) { StartAt.Value = -1; Broadcast("Start called off - waiting for players"); }
                m_StartPlayers = 0;
                return false;
            }
            float wait = Bootstrap.Fast ? Mathf.Min(1f, Cfg.StartCountdown) : Cfg.StartCountdown;
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
            if (m_StartSecond < 0 || s > m_StartSecond) Hud.Banner("EVERYONE'S HERE", $"The match starts in {s} seconds - get ready!");
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
}
