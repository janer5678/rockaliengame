using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The 3, 2, 1 countdown before the match (it was READY / SET / ROCK!): a lobby game (NetGame.ReadyLobby, not the
    /// tutorial) starts with everyone frozen at home while the match intro plays (MatchIntro.cs), and once every screen has
    /// finished it (or skipped it, or had none: MatchIntro reports it, IntroDoneRpc) a RockCountdown second countdown -
    /// 3, 2, 1 - nobody can move, look about the HUD's menus aside, or fight until it ends (FightFrozen, which PlayerController and
    /// the bots already honour for sudden death). The build phase's clock doesn't run meanwhile (its end is pushed back
    /// every frame). A screen that never says it's done can't hold everyone up: the host gives up waiting after the
    /// intro's own length and a moment more.
    /// RockAt (synced) is the server time the countdown ends (everyone's let go): until all the intros are done it's the
    /// latest it can be, then it's pulled in to RockCountdown seconds away. The HUD draws the numbers (Hud.ReadySetRock.cs);
    /// every peer plays a tick on each and shows the build phase's banner as it ends (TickReadySetRock).
    /// </summary>
    public partial class NetGame
    {
        /// <summary>3, 2, 1 - a second each - seconds in all.</summary>
        public const float RockCountdown = 3f;
        /// <summary>(tests) the 3, 2, 1 countdown in the tests too (they skip it otherwise, like the intro).</summary>
        public static bool TestRockToo;
        /// <summary>The server time the countdown ends (everyone let go) (-1: none this match).</summary>
        public readonly NetworkVariable<double> RockAt = new NetworkVariable<double>(-1);

        /// <summary>Everyone's held still until the countdown ends (the intro, then 3, 2, 1).</summary>
        public bool RockHeld => IsSpawned && RockAt.Value > 0 && (S == GameState.PreBall || S == GameState.BallLive) && NetworkManager.ServerTime.Time < RockAt.Value;
        /// <summary>Seconds until the countdown ends (negative after; +inf when there's none).</summary>
        public double RockLeft => IsSpawned && RockAt.Value > 0 ? RockAt.Value - NetworkManager.ServerTime.Time : double.PositiveInfinity;
        /// <summary>The countdown itself is on (3, 2, 1), not the wait for the intros before it.</summary>
        public bool RockCounting => RockHeld && RockLeft <= RockCountdown + 0.01;

        /// <summary>The countdown's number right now ("" when there's none, or it's over): "3", "2", then "1"; t is 0..1
        /// through that second.</summary>
        public static string RockWord(NetGame g, out float t)
        {
            t = 0f;
            if (g == null || !g.IsSpawned || g.RockAt.Value <= 0 || (g.S != GameState.PreBall && g.S != GameState.BallLive)) return "";
            double left = g.RockLeft;
            if (left > RockCountdown || left <= 0) return "";
            int n = Mathf.Clamp((int)System.Math.Ceiling(left), 1, 3);
            t = Mathf.Clamp01((float)(n - left));
            return n.ToString();
        }

        readonly HashSet<ulong> m_IntroDone = new HashSet<ulong>();
        bool m_RockWaiting;
        /// <summary>(tests) the server pulled the countdown in because every screen said its intro was done.</summary>
        public static int RockAllReported;

        /// <summary>Server, as the match starts (Waiting -> PreBall): everyone frozen until the intros are done and the
        /// countdown's run.</summary>
        void ServerBeginReadySetRock(double now)
        {
            m_IntroDone.Clear();
            m_RockWaiting = false;
            RockAt.Value = -1;
            if (!ReadyLobby || Cfg.Tutorial || (Bootstrap.Testing && !TestRockToo)) return;
            m_RockWaiting = true;
            RockAt.Value = now + MatchIntro.Length + MatchIntro.Outro + 1.5 + RockCountdown; // (the longest it waits for an intro)
        }

        /// <summary>(tests) The 3, 2, 1 countdown now, from the top - or (waitForIntros) as a match starts: held until every
        /// screen says its intro's done.</summary>
        public void ServerTestReadySetRock(bool waitForIntros)
        {
            if (!IsServer) return;
            double now = NetworkManager.ServerTime.Time;
            m_IntroDone.Clear();
            m_RockWaiting = waitForIntros;
            RockAt.Value = now + (waitForIntros ? MatchIntro.Length + MatchIntro.Outro + 1.5 : 0) + RockCountdown;
        }

        /// <summary>Every screen, when its match intro is over (or it had none).</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void IntroDoneRpc(RpcParams p = default) => m_IntroDone.Add(p.Receive.SenderClientId);

        /// <summary>Server, every frame: the clock waits while everyone's held; once every screen's intro is done the
        /// countdown starts.</summary>
        void ServerTickReadySetRock(double now)
        {
            if (!RockHeld) { m_RockWaiting = false; return; }
            if (!TimerPaused.Value)
            {
                // (the build phase's clock - and the fun modes' airdrop clock - start as the countdown ends)
                PhaseEnd.Value += Time.deltaTime;
                if (m_BallStart >= 0) m_BallStart += Time.deltaTime;
            }
            if (!m_RockWaiting) return;
            bool all = true;
            foreach (var id in NetworkManager.ConnectedClientsIds) if (!m_IntroDone.Contains(id)) { all = false; break; }
            if (!all) return;
            m_RockWaiting = false;
            RockAllReported++;
            if (RockAt.Value > now + RockCountdown) RockAt.Value = now + RockCountdown;
        }

        string m_RockWas = "";

        /// <summary>Every peer, every frame: a ding on each number - and the build phase's banner as it ends.</summary>
        void TickReadySetRock()
        {
            string w = RockWord(this, out _);
            if (w == m_RockWas) return;
            bool ended = m_RockWas == "1" && w == "";
            m_RockWas = w;
            if (w != "") Sfx.Play2D(Sfx.Ding, 1f, 0f);
            else if (ended) MatchIntro.ShowBuildBanner(this);
        }
    }
}
