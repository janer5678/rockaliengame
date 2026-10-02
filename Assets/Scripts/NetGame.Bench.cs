using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>
        /// The Workbench T1 is locked until your team has captured the ball: put it in your machine's socket once, or had
        /// it in your base this many seconds in all (lying there, in the socket, or carried by one of your team - the
        /// seconds add up over the match, they don't have to be in one go). Builder has no bases or machines: always unlocked.
        /// </summary>
        [Tune("Crafting")] public static float BenchUnlockSeconds = 30f;

        /// <summary>Can this team craft its Workbench T1 yet? (synced: NetGame.BenchUnlocks)</summary>
        public static bool BenchUnlocked(int team) => Builder || NetGame.Instance == null || NetGame.Instance.BenchUnlockedFor(team);

        /// <summary>What the crafting list says on the locked Workbench T1.</summary>
        public const string BenchLockedText = "Can only craft once the ball has been captured";
    }

    /// <summary>
    /// The workbench unlock: per team, the Workbench T1 can only be crafted once the team has put the ball in its machine
    /// socket once, or had the ball in its base for Cfg.BenchUnlockSeconds in all. The server keeps score and syncs a bit
    /// per team (BenchUnlocks) and the seconds so far (BallInBaseSecs); each client says "WORKBENCH UNLOCKED" (banner +
    /// sound) when its own team's bit comes on. In the tutorial the notice waits for the workbench step.
    /// </summary>
    public partial class NetGame
    {
        /// <summary>Bit t: team t's Workbench T1 is unlocked (it stays unlocked for the rest of the match).</summary>
        public readonly NetworkVariable<byte> BenchUnlocks = new NetworkVariable<byte>();
        /// <summary>8 bits a team: whole seconds the ball has spent in that team's base so far (towards the unlock).</summary>
        public readonly NetworkVariable<int> BallInBaseSecs = new NetworkVariable<int>();

        public bool BenchUnlockedFor(int team) => team >= 0 && team < 8 && ((BenchUnlocks.Value >> team) & 1) != 0;
        public int BallInBaseSecondsOf(int team) => team >= 0 && team < 4 ? (BallInBaseSecs.Value >> (team * 8)) & 255 : 0;

        readonly float[] m_BallInBase = new float[4];

        /// <summary>Server, every frame: unlock a team's workbench when the ball is in its socket, or has been in its base long enough.</summary>
        void ServerTickBenchUnlock()
        {
            if (Cfg.Builder || (S != GameState.PreBall && S != GameState.BallLive)) return;
            var b = Ball.Instance;
            if (b == null || !b.IsSpawned) return;
            int st = b.SocketTeam.Value;
            if (st >= 0 && st < 4 && !BenchUnlockedFor(st)) ServerUnlockBench(st, "put the ball in their machine");
            var p = b.transform.position;
            int t = p.y < 30f ? Cfg.BaseTeamAt(p) : -1;
            if (t < 0 || t >= 4 || BenchUnlockedFor(t)) return;
            // carried through by an enemy doesn't count
            var carrier = b.IsCarried ? b.Carrier : null;
            if (b.IsCarried && (carrier == null || carrier.Team.Value != t)) return;
            m_BallInBase[t] += Time.deltaTime;
            int secs = Mathf.Clamp(Mathf.FloorToInt(m_BallInBase[t]), 0, 255);
            if (secs != BallInBaseSecondsOf(t)) BallInBaseSecs.Value = (BallInBaseSecs.Value & ~(255 << (t * 8))) | (secs << (t * 8));
            if (m_BallInBase[t] >= Cfg.BenchUnlockSeconds) ServerUnlockBench(t, $"kept the ball in their base for {Cfg.BenchUnlockSeconds:0} seconds");
        }

        /// <summary>Server: this team can craft its Workbench T1 from now on (also the dev setting).</summary>
        public void ServerUnlockBench(int team, string why)
        {
            if (!IsServer || team < 0 || team >= 4 || BenchUnlockedFor(team)) return;
            BenchUnlocks.Value = (byte)(BenchUnlocks.Value | (1 << team));
            Debug.Log($"[RockGame] {Cfg.TeamLabel(team)}'s Workbench T1 unlocked ({why})"); // (the team's own clients say so: TickBenchUnlockNotice)
        }

        // ---- the client: say so when our own team's bench unlocks ----

        /// <summary>Test hook: how many times this client has shown "WORKBENCH UNLOCKED".</summary>
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
            Hud.Banner("WORKBENCH UNLOCKED", $"Craft it in your bag ({Binds.Name(Bind.Inventory)}) - your team captured the ball");
            Hud.Push("Workbench unlocked: craft a Workbench T1 in your bag");
            Sfx.Play2D(Sfx.Unlock, 0.8f, 0f);
        }
    }
}
