using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// READY / SET / ROCK! before the match: a lobby game (NetGame.ReadyLobby, not the tutorial) starts with everyone
    /// frozen at home while the match intro plays (MatchIntro.cs), and once every screen has finished it (or skipped it,
    /// or had none: MatchIntro reports it, IntroDoneRpc) a RockCountdown second countdown - READY, SET, then ROCK! -
    /// nobody can move, look about the HUD's menus aside, or fight until ROCK! (FightFrozen, which PlayerController and
    /// the bots already honour for sudden death). The build phase's clock doesn't run meanwhile (its end is pushed back
    /// every frame). A screen that never says it's done can't hold everyone up: the host gives up waiting after the
    /// intro's own length and a moment more.
    /// RockAt (synced) is the server time of ROCK!: until all the intros are done it's the latest it can be, then it's
    /// pulled in to RockCountdown seconds away. The HUD draws the words (Hud.ReadySetRock.cs); every peer plays the
    /// beeps and the boom, and shows the build phase's banner at ROCK! (TickReadySetRock).
    /// </summary>
    public partial class NetGame
    {
        /// <summary>READY (the first half), SET (the second half), then ROCK! - seconds in all.</summary>
        public const float RockCountdown = 3f;
        /// <summary>(tests) READY / SET / ROCK! in the tests too (they skip it otherwise, like the intro).</summary>
        public static bool TestRockToo;
        /// <summary>The server time of ROCK! (-1: none this match).</summary>
        public readonly NetworkVariable<double> RockAt = new NetworkVariable<double>(-1);

        /// <summary>Everyone's held still before ROCK! (the intro, then READY / SET).</summary>
        public bool RockHeld => IsSpawned && RockAt.Value > 0 && (S == GameState.PreBall || S == GameState.BallLive) && NetworkManager.ServerTime.Time < RockAt.Value;
        /// <summary>Seconds until ROCK! (negative after it; +inf when there's none).</summary>
        public double RockLeft => IsSpawned && RockAt.Value > 0 ? RockAt.Value - NetworkManager.ServerTime.Time : double.PositiveInfinity;
        /// <summary>The countdown itself is on (READY / SET), not the wait for the intros before it.</summary>
        public bool RockCounting => RockHeld && RockLeft <= RockCountdown + 0.01;

        /// <summary>The countdown's word right now ("" when there's none): READY, SET, then ROCK! for a moment after; t is
        /// 0..1 through it.</summary>
        public static string RockWord(NetGame g, out float t)
        {
            t = 0f;
            if (g == null || !g.IsSpawned || g.RockAt.Value <= 0 || (g.S != GameState.PreBall && g.S != GameState.BallLive)) return "";
            double left = g.RockLeft;
            float half = RockCountdown * 0.5f;
            if (left > RockCountdown) return "";
            if (left > half) { t = (float)(RockCountdown - left) / half; return "READY"; }
            if (left > 0) { t = (float)(half - left) / half; return "SET"; }
            if (left > -1.2) { t = (float)(-left) / 1.2f; return "ROCK!"; }
            return "";
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

        /// <summary>(tests) READY / SET / ROCK! now, from the top - or (waitForIntros) as a match starts: held until every
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
                // (the build phase's clock - and the fun modes' airdrop clock - start at ROCK!)
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

        /// <summary>Every peer, every frame: a ding on READY and SET, a boom on ROCK! - and the build phase's banner then.</summary>
        void TickReadySetRock()
        {
            string w = RockWord(this, out _);
            if (w == m_RockWas) return;
            m_RockWas = w;
            if (w == "READY" || w == "SET") { Sfx.Play2D(Sfx.Ding, 1f, 0f); Fx.Shake(0.15f); }
            else if (w == "ROCK!") { Sfx.Play2D(Sfx.Boom, 0.7f, 0f); Fx.Shake(0.45f); MatchIntro.ShowBuildBanner(this); }
        }
    }
}
