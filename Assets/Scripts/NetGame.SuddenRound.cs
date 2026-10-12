using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>The sudden death round (no more overtime): the glass walls are back up this long (s) with the bases
        /// wiped, then the ball phase lasts SuddenBallTime - whoever has the ball in their machine when it runs out wins, or
        /// it's the stadium rock fight.</summary>
        [Tune("Match")] public static float SuddenWallTime = 60f, SuddenBallTime = 300f;
        /// <summary>The sudden death round is on: only the starter (pre Trade Station) things can be made.</summary>
        public static bool SuddenRound => NetGame.Instance != null && NetGame.Instance.IsSpawned && NetGame.Instance.SuddenBall.Value;
        /// <summary>Can't be made in the sudden death round: everything from the Trade Stations, and the stations themselves
        /// (those aren't even on the list: Cfg.AddTier).</summary>
        public static bool SuddenLocked(Item i) => SuddenRound && (CraftTier(i) >= 1 || Workbench.IsBench(i));
    }

    /// <summary>
    /// SUDDEN DEATH (it replaced overtime in the ball modes): the clock runs out with the ball in nobody's machine, so
    /// every base is wiped back to how it started - its pieces, chests, trade stations, barricades, traps, ladders, loot
    /// on the floor, the wood machine (it lands again when the walls drop) - while everything out of the bases (the
    /// middle, the wild, airdrop crates, horses) stays as it is. The ball goes back under the dome, everyone alive is sent
    /// home (keeping what they carry) and the glass walls come back up for Cfg.SuddenWallTime; only the starter things can
    /// be made (Cfg.SuddenLocked; no trade stations on the list at all). Then the walls drop for Cfg.SuddenBallTime and it
    /// plays like the normal match: a capture doesn't end it - when the time runs out, the team with the ball in its machine
    /// wins; if nobody has it, it's the rock fight in the space stadium (StartSuddenDeath). No airdrops
    /// in the round. Not Builder, the tutorial or the modes that score something else (Bedwars, 3 Goal, Progress,
    /// Assassin keep their overtime).
    /// </summary>
    public partial class NetGame
    {
        /// <summary>The sudden death round is on (bases wiped, starter things only, the ball in your machine at the end wins).</summary>
        public readonly NetworkVariable<bool> SuddenBall = new NetworkVariable<bool>();

        /// <summary>This match goes to the sudden death round when the clock runs out with nobody's ball in a machine.</summary>
        static bool HasSuddenRound => !Cfg.Builder && !Cfg.Tutorial && !(Cfg.ClassicMode && !Cfg.Domination) && !Cfg.NoBall;

        /// <summary>How long this ball phase is: the sudden death round's own, else the match's.</summary>
        float BallPhaseNow => SuddenBall.Value ? Cfg.SuddenBallTime : BallPhase;

        /// <summary>Server: the clock ran out with the ball in nobody's machine - wipe the bases, walls back up.</summary>
        void ServerStartSuddenRound()
        {
            SuddenBall.Value = true;
            // the ball back under the dome in the middle (from anyone's hands or wherever it was)
            if (Ball.Instance == null || !Ball.Instance.IsSpawned) SpawnBall();
            else Ball.Instance.ServerPlaceInDome();
            ServerWipeBases();
            // everyone alive goes home (the dead come back in their base anyway while the walls are up)
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value) continue;
                if (p.Riding) p.ServerDismount();
                p.TrappedUntil.Value = -1;
                p.ServerSendHome();
            }
            SetPhase(GameState.PreBall, Cfg.SuddenWallTime);
            BannerRpc(new FixedString64Bytes("SUDDEN DEATH"), new FixedString128Bytes($"The bases are wiped - basics only. The walls drop in {Mathf.RoundToInt(Cfg.SuddenWallTime)} s: the ball in your machine when time's up wins!"));
            Broadcast("SUDDEN DEATH! Bases wiped, basics only - whoever has the ball in their machine when time runs out wins");
        }

        /// <summary>Server: every base back to how the match started (everything out of the bases stays).</summary>
        void ServerWipeBases()
        {
            for (int i = Structure.All.Count - 1; i >= 0; i--)
            {
                var s = Structure.All[i];
                if (s != null && s.IsSpawned && InABase(s.transform.position)) s.NetworkObject.Despawn(true);
            }
            for (int i = Container.All.Count - 1; i >= 0; i--)
            {
                var c = Container.All[i];
                if (c == null || !c.IsSpawned || c.IsAirdrop || c.IsGamble || c.IsConverter) continue; // (the bases' own machines stay)
                if (InABase(c.transform.position)) c.NetworkObject.Despawn(true);
            }
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (!InABase(Items[i].Pos)) continue;
                m_ItemBorn.Remove(Items[i].Id);
                Items.RemoveAt(i);
            }
            // the wood machines go (they land again when the walls drop, at level 1), and their piles with them
            WoodGenLevels.Value = 0;
            FortifyLevels.Value = 0;
            m_WoodGenSecs = 0;
            for (int t = 0; t < m_WoodPile.Length; t++) m_WoodPile[t] = -1;
        }

        /// <summary>In (or right round) a team's base.</summary>
        static bool InABase(Vector3 p)
        {
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var bc = Cfg.BaseCenter[t];
                if (Mathf.Abs(p.x - bc.x) < Cfg.BaseHalf + 3f && Mathf.Abs(p.z - bc.z) < Cfg.BaseHalf + 3f) return true;
            }
            return false;
        }

    }
}
