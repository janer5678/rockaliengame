using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest gamemodes -host -solo -rules bedwars|threegoal|progress|assassin|domination: the mode's own way to
        /// win, on its own (solo, so the enemy side is played from the server): 3 Goal scores three goals (the ball comes
        /// back to the middle between them) and wins; Progress fills the bar and wins; Bedwars counts hits on the enemy
        /// machine, an explosive finishes it, and a team without its machine can't respawn; Assassin hands in a skull
        /// and wins; Domination gives the team with the ball the advanced trades and takes them away again.
        /// </summary>
        IEnumerator GameModesRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = 1 - team;
            Log($"game mode: {Cfg.RulesName(Cfg.Rules)}");
            Check(Cfg.ClassicMode && Cfg.AutoWood && Cfg.PowerMenu, $"{Cfg.RulesName(Cfg.Rules)} is Classic underneath (upgrades, wood machine, power items)");
            me.DevRpc(DevCmd.DropWallNow);
            float until = Time.time + 5f;
            while (g.S != GameState.BallLive && Time.time < until) yield return null;
            Check(g.S == GameState.BallLive, "the wall's down");
            yield return new WaitForSeconds(0.5f);
            Check(Cfg.NoBall == (Ball.Instance == null), $"the ball: {(Cfg.NoBall ? "none in this mode" : "in play")}");
            if (Cfg.NoBall) Check(Cfg.BenchUnlocked(team), "no ball to capture: the Trade Station unlocked when the wall dropped");
            yield return Shot("gamemode_" + Cfg.RulesId(Cfg.Rules) + "_hud");

            if (Cfg.ThreeGoal)
            {
                var b = Ball.Instance;
                for (int i = 1; i <= Cfg.GoalsToWin; i++)
                {
                    b.ServerSocket(team);
                    yield return new WaitForSeconds(0.4f);
                    Check(g.GoalsOf(team) == i, $"goal {i}: the score is {g.GoalsOf(team)}");
                    if (i < Cfg.GoalsToWin)
                    {
                        yield return new WaitForSeconds(3f);
                        Check(b.SocketTeam.Value < 0, "after a goal the ball comes back out to the middle (the UFO drop)");
                    }
                }
                yield return new WaitForSeconds(0.5f);
                Check(VictoryCutscene.Active || g.S == GameState.GameOver, $"{Cfg.GoalsToWin} goals win");
            }
            else if (Cfg.ProgressMode)
            {
                Cfg.ProgressSeconds = 3f;
                Ball.Instance.ServerSocket(team);
                yield return new WaitForSeconds(1.2f);
                float mid = g.ProgressOf(team);
                Check(mid > 0.2f && mid < 1f, $"the ball in our machine fills our bar ({mid * 100f:0}%)");
                yield return new WaitForSeconds(3f);
                Check(VictoryCutscene.Active || g.S == GameState.GameOver, "a full bar wins");
            }
            else if (Cfg.Bedwars)
            {
                // respawning: you wake up inside the machine's cryochamber, held for a moment, then its doors open
                {
                    me.DevRpc(DevCmd.KillMe);
                    yield return new WaitForSeconds(0.4f);
                    float podUntil = Time.time + Cfg.RespawnTime + 4f;
                    while (me.Dead.Value && Time.time < podUntil) yield return null;
                    if (me.Dead.Value) me.ServerRespawn(false);
                    yield return new WaitForSeconds(0.25f);
                    var pod = Cfg.SocketPos(team) - Vector3.up * 0.34f;
                    float off = new Vector2(me.transform.position.x - pod.x, me.transform.position.z - pod.z).magnitude;
                    Check(!me.Dead.Value && off < 1.2f, $"Bedwars: respawned inside the cryochamber ({off:0.00} m from its middle)");
                    yield return Shot("gamemode_bedwars_pod_inside");
                    var p0 = me.transform.position;
                    Binds.TestHold(Bind.Forward, true);
                    yield return new WaitForSeconds(0.4f);
                    Check(Vector3.Distance(me.transform.position, p0) < 0.2f, "held in the pod until its doors open");
                    yield return new WaitForSeconds(1.2f);
                    Binds.TestReleaseAll();
                    Check(Vector3.Distance(me.transform.position, p0) > 0.6f, $"...then you walk out ({Vector3.Distance(me.transform.position, p0):0.0} m)");
                    yield return Shot("gamemode_bedwars_pod_out");
                }
                var m = Cfg.MachinePos(enemy) + Vector3.up * 1.5f;
                g.ServerMaybeHitMachine(m, me, false);
                yield return new WaitForSeconds(0.6f);
                g.ServerMaybeHitMachine(m, me, false);
                yield return new WaitForSeconds(0.3f);
                Check(g.HitsOn(enemy) == 2 && !g.MachineDown(enemy), $"two hits on the enemy machine ({g.HitsOn(enemy)})");
                var mine = Cfg.MachinePos(team) + Vector3.up * 1.5f;
                g.ServerMaybeHitMachine(mine, me, false);
                Check(g.HitsOn(team) == 0, "our own machine doesn't take our hits");
                yield return Shot("gamemode_bedwars_damaged");
                g.ServerMaybeHitMachine(m, me, true);
                yield return new WaitForSeconds(1f);
                Check(g.MachineDown(enemy) && !g.CanRespawn(enemy), "an explosive right on it: the enemy machine is destroyed - they can't respawn");
                yield return Shot("gamemode_bedwars_destroyed");
                // and ours: down, we're out
                g.ServerMaybeHitMachine(mine, null, true);
                yield return new WaitForSeconds(0.5f);
                me.SuicideRpc();
                yield return new WaitForSeconds(Cfg.RespawnTime + 1.5f);
                me.RespawnChoiceRpc(false);
                yield return new WaitForSeconds(0.5f);
                Check(me.Dead.Value, "with our machine gone we don't respawn");
                yield return Shot("gamemode_bedwars_eliminated");
            }
            else if (Cfg.Assassin)
            {
                me.ServerGive(Item.Skull, 1, Cfg.SkullData(enemy, 0));
                yield return new WaitForSeconds(0.3f);
                Check(me.Count(Item.Skull) == 1, "we've a skull");
                pc.LocalTeleport(OnGround(Cfg.MachinePos(team) - Cfg.BackDir(team) * 2.2f), Quaternion.LookRotation(Cfg.BackDir(team)).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                me.DepositSkullsRpc();
                yield return new WaitForSeconds(0.6f);
                Check(me.Count(Item.Skull) == 0 && g.SkullsOf(team) == 1, $"handed it in at our machine ({g.SkullsOf(team)} in)");
                yield return Shot("gamemode_assassin_skull_in");
                Check(VictoryCutscene.Active || g.S == GameState.GameOver, "a skull of every enemy wins");
            }
            else if (Cfg.Domination)
            {
                var b = Ball.Instance;
                Check(Cfg.BenchTier(team) < 2, "no ball, no advanced trades");
                b.ServerSocket(team);
                yield return new WaitForSeconds(0.4f);
                Check(g.BallTeam.Value == team && Cfg.BenchTier(team) == 2, "the ball in our machine: the advanced trades are ours");
                yield return Shot("gamemode_domination");
                b.ServerReset();
                yield return new WaitForSeconds(0.4f);
                Check(g.BallTeam.Value != team && Cfg.BenchTier(team) < 2, "lose the ball, lose them");
            }
            Log("game mode test done");
            yield return new WaitForSeconds(1f);
            Application.Quit(0);

            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.4f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), name + ".png"));
                Log("shot " + name);
                yield return null; yield return null;
            }
        }
    }
}
