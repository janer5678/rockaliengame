using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>
        /// The Workbench T1 is locked for EVERYONE until the ball has been captured by any team: a team puts it in its
        /// machine's socket once, or has it in its base this many seconds in all (lying there, in the socket, or carried
        /// by one of that team - each team's seconds add up over the match, they don't have to be in one go). The first
        /// capture unlocks every team's workbench at once. Builder has no bases or machines: always unlocked.
        /// </summary>
        [Tune("Crafting")] public static float BenchUnlockSeconds = 10f; // (it used to be 30)

        /// <summary>Can this team craft its Workbench T1 yet? (synced: NetGame.BenchUnlocks - every team's bit comes on together)</summary>
        public static bool BenchUnlocked(int team) => Builder || NetGame.Instance == null || NetGame.Instance.BenchUnlockedFor(team);

        /// <summary>What the crafting list says on the locked Workbench T1.</summary>
        public const string BenchLockedText = "Can only craft once the ball has been captured";
    }

    /// <summary>
    /// The workbench unlock: nobody can craft the Workbench T1 until the ball has been captured - some team (any team)
    /// has put it in its machine socket once, or had it in its base for Cfg.BenchUnlockSeconds in all. That first capture
    /// unlocks it for every team, not just the one that did it. The server keeps score and syncs a bit per team
    /// (BenchUnlocks: all of them come on together) and each team's seconds so far (BallInBaseSecs); every client says
    /// "WORK BENCHES UNLOCKED" (banner + sound) when its team's bit comes on. In the tutorial the notice waits for the
    /// workbench step.
    /// </summary>
    public partial class NetGame
    {
        /// <summary>Bit t: team t's Workbench T1 is unlocked (every team's at once; it stays unlocked for the rest of the match).</summary>
        public readonly NetworkVariable<byte> BenchUnlocks = new NetworkVariable<byte>();
        /// <summary>8 bits a team: whole seconds the ball has spent in that team's base so far (towards the unlock).</summary>
        public readonly NetworkVariable<int> BallInBaseSecs = new NetworkVariable<int>();

        public bool BenchUnlockedFor(int team) => team >= 0 && team < 8 && ((BenchUnlocks.Value >> team) & 1) != 0;
        public int BallInBaseSecondsOf(int team) => team >= 0 && team < 4 ? (BallInBaseSecs.Value >> (team * 8)) & 255 : 0;
        /// <summary>The most seconds any team has had the ball in its base so far (the team nearest to unlocking it for everyone).</summary>
        public int BallInBaseSecondsBest { get { int best = 0; for (int t = 0; t < 4; t++) best = Mathf.Max(best, BallInBaseSecondsOf(t)); return best; } }
        /// <summary>Which team's capture unlocked the workbenches (-1: not yet, or the dev setting).</summary>
        public readonly NetworkVariable<sbyte> BenchUnlockedBy = new NetworkVariable<sbyte>(-1);

        readonly float[] m_BallInBase = new float[4];

        /// <summary>Server, every frame: unlock everyone's workbench when the ball is in a team's socket, or has been in a team's base long enough.</summary>
        void ServerTickBenchUnlock()
        {
            if (Cfg.Builder || (S != GameState.PreBall && S != GameState.BallLive)) return;
            var b = Ball.Instance;
            if (b == null || !b.IsSpawned) return;
            // (the tutorial: a team nobody plays for - the solo raid's enemy, holding the ball - captures nothing)
            int st = b.SocketTeam.Value;
            if (st >= 0 && st < 4 && !BenchUnlockedFor(st) && Tutorial.TeamCanCapture(st)) ServerUnlockBench(st, "put the ball in their machine");
            var p = b.transform.position;
            int t = p.y < 30f ? Cfg.BaseTeamAt(p) : -1;
            if (t < 0 || t >= 4 || BenchUnlockedFor(t) || !Tutorial.TeamCanCapture(t)) return;
            // carried through by an enemy doesn't count
            var carrier = b.IsCarried ? b.Carrier : null;
            if (b.IsCarried && (carrier == null || carrier.Team.Value != t)) return;
            m_BallInBase[t] += Time.deltaTime;
            int secs = Mathf.Clamp(Mathf.FloorToInt(m_BallInBase[t]), 0, 255);
            if (secs != BallInBaseSecondsOf(t)) BallInBaseSecs.Value = (BallInBaseSecs.Value & ~(255 << (t * 8))) | (secs << (t * 8));
            if (m_BallInBase[t] >= Cfg.BenchUnlockSeconds) ServerUnlockBench(t, $"kept the ball in their base for {Cfg.BenchUnlockSeconds:0} seconds");
        }

        /// <summary>Server: this team captured the ball - EVERY team can craft its Workbench T1 from now on (also the dev setting).</summary>
        public void ServerUnlockBench(int team, string why)
        {
            if (!IsServer || team < 0 || team >= 4 || BenchUnlockedFor(team)) return;
            BenchUnlockedBy.Value = (sbyte)team;
            BenchUnlocks.Value = 0xFF; // (every team's bit: a synced NetworkVariable, so every client - late joiners too - has it)
            Debug.Log($"[RockGame] Trade Station unlocked for everyone: {Cfg.TeamLabel(team)} {why}"); // (every client says so: TickBenchUnlockNotice)
        }

        // ---- the client: say so when our own team's bench unlocks ----

        /// <summary>Test hook: how many times this client has shown "TRADE STATION UNLOCKED".</summary>
        public static int BenchUnlockNotices;
        int m_SeenUnlockTeam = -1;
        bool m_SeenUnlock, m_UnlockPending;

        void TickBenchUnlockNotice()
        {
            var me = PlayerNet.Local;
            if (me == null || !me.IsSpawned) return;
            int team = me.Team.Value;
            bool on = BenchUnlockedFor(team);
            if (m_SeenUnlockTeam != team) { m_SeenUnlockTeam = team; m_SeenUnlock = on; return; } // first look (a late joiner isn't told)
            if (on && !m_SeenUnlock) m_UnlockPending = true;
            m_SeenUnlock = on;
            // the tutorial tells you when it gets to the workbench step
            if (!m_UnlockPending || (Tutorial.On && !Tutorial.AllowsItem(Item.Workbench))) return;
            m_UnlockPending = false;
            BenchUnlockNotices++;
            int by = BenchUnlockedBy.Value;
            string who = Cfg.NoBall ? "the wall dropped" : by < 0 ? "the ball has been captured" : by == team ? "your team captured the ball" : $"{Cfg.TeamLabel(by)} captured the ball - everyone gets it";
            Hud.Banner("TRADE STATION UNLOCKED", $"Craft it in your bag ({Binds.Name(Bind.Inventory)}) - {who}");
            Sfx.Play2D(Sfx.Unlock, 0.8f, 0f);
        }
    }
}
