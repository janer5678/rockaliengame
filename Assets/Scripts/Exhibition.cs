using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The exhibition build (ProjectSetup.BuildExhibition, or any build with -exhibition): four PCs, one group of visitors
    /// after another, nothing to set up between them. HOST or JOIN once (a Unity Relay join code: ExhibitionRelay.cs), then
    /// round and round: the READY screen (over the main menu's UFO: Hud.Exhibition.cs), everyone READY plays the UFO crash
    /// (MatchIntro), the tutorial in 2v2, a Classic match, and back to the READY screen. All on Highlands, medium.
    /// The READY screen and the tutorial are one session (rules Tutorial) and the match is another (rules AutoWood); going
    /// from one to the other is the usual back to the lobby restart (Bootstrap.BackToLobby: everyone drops and comes straight
    /// back in), which is why the session's rules are what says which part we're in.
    /// </summary>
    public static class Exhibition
    {
        public static readonly bool On = IsOn();
        /// <summary>Players READY before it starts (-exhibitneed N: fewer PCs, for trying it out).</summary>
        public static readonly int Needed = Arg("-exhibitneed", 4);
        /// <summary>Everyone's finished the tutorial: this long, then the match. Someone has: the others get this long to
        /// catch up, so a visitor who's wandered off can't hold up the group (seconds).</summary>
        public const float AfterAllDone = 4f, AfterFirstDone = 120f;
        /// <summary>The tutorial's regroup step goes on without whoever's this far behind (seconds).</summary>
        public const float RegroupWait = 90f;
        /// <summary>The match starts without whoever hasn't reconnected by now (seconds).</summary>
        public const float WaitForAllBack = 30f;

        static bool IsOn()
        {
#if EXHIBITION
            return true;
#else
            return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-exhibition") >= 0;
#endif
        }

        static int Arg(string name, int fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name && int.TryParse(args[i + 1], out int v)) return Mathf.Max(1, v);
            return fallback;
        }

        public static bool TutorialPhase => On && Cfg.Tutorial;
        public static bool MatchPhase => On && !Cfg.Tutorial;
        /// <summary>The READY screen (or the moment before the match) is up: the UFO behind it, not the ship lobby's room.</summary>
        public static bool LobbyUp => On && ShipLobby.Active;

        /// <summary>Every session's map seed. Building a map holds a PC up for many seconds, and Relay drops whoever goes quiet
        /// for about 10: so everyone already has the map built before they connect (Bootstrap.CleanupLocal) and joining
        /// finds it there (MapBuilder.IsBuilt) instead of building it with the connection open.</summary>
        public const int Seed = 4242;

        public static int LobbyKey => Key(GameRules.Tutorial);
        public static int MatchKey => Key(GameRules.AutoWood); // (the menu's Classic)

        static int Key(GameRules rules)
        {
            int key = (int)MapKind.Highlands | ((int)MapSize.Big << Cfg.SizeShift) | ((int)GameMode.Teams << Cfg.ModeShift); // (MapSize.Big is Medium on screen)
            if (Cfg.WoodIsNormal) key |= Cfg.WoodBit;
            return Cfg.WithRules(key, rules);
        }

        /// <summary>Host, as it restarts: how many players it's waiting to see again before the match starts.</summary>
        public static int ExpectBack;

        // Everyone stays on their team across the restarts, but they come back in any order and joining puts you on the
        // emptiest team: each PC remembers its team as the host restarts and claims it again (PlayerNet.ExhibitionTeamRpc).
        static int s_WantTeam = -1;
        static PlayerNet s_Claimed;

        public static void RememberTeam()
        {
            if (On && PlayerNet.Local != null && PlayerNet.Local.IsSpawned) s_WantTeam = PlayerNet.Local.Team.Value;
        }

        public static void ForgetTeam() => s_WantTeam = -1;

        /// <summary>A client, told the host is restarting: the map it builds while it's out is the next part's (see Seed).</summary>
        public static void ClientNextPart()
        {
            if (On) Bootstrap.MapChoice = Cfg.Tutorial ? MatchKey : LobbyKey;
        }

        /// <summary>Every frame (Bootstrap.Update).</summary>
        public static void Tick()
        {
            if (!On) return;
            // (in the log: Relay drops whoever goes quiet for too long, and a long frame is how that starts)
            if (Time.unscaledDeltaTime > 3f) Debug.Log($"[RockGame] t={Time.realtimeSinceStartup:0.0} stalled {Time.unscaledDeltaTime:0.0} s");
            var me = PlayerNet.Local;
            if (me == null || !me.IsSpawned) { s_Claimed = null; return; }
            if (me == s_Claimed) return;
            s_Claimed = me;
            if (s_WantTeam >= 0) me.ExhibitionTeamRpc((byte)s_WantTeam);
        }
    }

    public partial class NetGame
    {
        /// <summary>The tutorial: the server time everyone's sent on to the match (-1: nobody's finished it yet).</summary>
        public readonly NetworkVariable<double> ExhibitionNextAt = new NetworkVariable<double>(-1);
        /// <summary>Seconds until the match, once somebody's finished the tutorial (-1: not yet).</summary>
        public float ExhibitionNextIn => IsSpawned && ExhibitionNextAt.Value >= 0 ? Mathf.Max(0f, (float)(ExhibitionNextAt.Value - NetworkManager.ServerTime.Time)) : -1f;
        double m_ExhibitionWaitSince = -1;

        /// <summary>Server, in the waiting state: how many players it takes to start. The READY screen wants everyone; the
        /// match is everyone coming back from the tutorial, READY already, or whoever's made it after WaitForAllBack.</summary>
        int ServerExhibitionNeed(double now)
        {
            if (Cfg.Tutorial) return Exhibition.Needed;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && !p.LobbyReady.Value) p.LobbyReady.Value = true;
            if (m_ExhibitionWaitSince < 0) m_ExhibitionWaitSince = now;
            return now - m_ExhibitionWaitSince > Exhibition.WaitForAllBack ? 1 : Mathf.Max(1, Exhibition.ExpectBack);
        }

        /// <summary>Server, every frame: the tutorial's done when everyone's finished it (or AfterFirstDone after the first
        /// one has): on to the match. The match's own end goes back through ServerTickBackToLobby.</summary>
        void ServerTickExhibition()
        {
            if (!Exhibition.TutorialPhase || S == GameState.Waiting || S == GameState.GameOver) return;
            int done = 0, total = 0;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned) continue;
                total++;
                if (p.TutStep.Value == Tutorial.FinishedStep) done++;
            }
            if (done == 0) return;
            double now = NetworkManager.ServerTime.Time;
            if (ExhibitionNextAt.Value < 0) ExhibitionNextAt.Value = now + Exhibition.AfterFirstDone;
            if (done >= total && ExhibitionNextAt.Value > now + Exhibition.AfterAllDone) ExhibitionNextAt.Value = now + Exhibition.AfterAllDone;
            if (now >= ExhibitionNextAt.Value && Bootstrap.I != null) Bootstrap.I.ServerBackToLobby();
        }

        /// <summary>Every client: the next session's join code, as the host is about to restart (each session has its own).</summary>
        [Rpc(SendTo.NotServer)]
        public void ExhibitionCodeRpc(FixedString32Bytes code)
        {
            if (Bootstrap.I != null) Bootstrap.I.Ip = code.ToString();
        }
    }

    public partial class PlayerNet
    {
        bool m_TeamClaimed;

        /// <summary>Owner, back in after a restart: my team from before. If somebody's sitting in my place who hasn't claimed
        /// theirs yet, they're moved over (they'll claim their own in a moment).</summary>
        [Rpc(SendTo.Server)]
        public void ExhibitionTeamRpc(byte team)
        {
            var g = NetGame.Instance;
            if (g == null || g.S != GameState.Waiting || team >= Cfg.TeamCount) return;
            m_TeamClaimed = true;
            if (team == Team.Value) return;
            PlayerNet unclaimed = null;
            int n = 0;
            foreach (var p in All)
            {
                if (p == this || p.Team.Value != team) continue;
                n++;
                if (!p.m_TeamClaimed) unclaimed = p;
            }
            int from = Team.Value;
            if (n >= Cfg.TeamCap(team))
            {
                if (unclaimed == null) return;
                unclaimed.Team.Value = (byte)from;
            }
            Team.Value = team;
            Slot.Value = (byte)FreeSlot(team);
            if (n >= Cfg.TeamCap(team)) unclaimed.Slot.Value = (byte)unclaimed.FreeSlot(from);
        }
    }
}
