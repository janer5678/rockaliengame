using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt13 -host -solo:
        /// 1. READY / SET / ROCK! (NetGame.ReadySetRock.cs): everyone frozen (FightFrozen - a bot doesn't move either) through
        ///    READY and SET, the build clock not running; at ROCK! they're let go. And the start's wait for the intros: an
        ///    intro played and skipped here tells the host, which pulls the countdown in at once.
        /// 2. Killed by someone, the respawn comes the moment the kill cam and its replay end (RespawnAt = the kill cam's
        ///    length); a death with no killer keeps Cfg.RespawnTime.
        /// 3. A base can have Cfg.MaxDoors doors: the next is refused (with a message); one fewer and it can be built again.
        /// </summary>
        IEnumerator Prompt13Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            Check(g.S == GameState.PreBall && !g.RockHeld && g.RockAt.Value < 0, "the tests start with no READY / SET / ROCK! (solo)");
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(1.5f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            if (bot != null && bot.Dead.Value) bot.ServerRespawn(false);
            yield return new WaitForSeconds(1f);

            // ---- 1. READY / SET / ROCK!
            float clock0 = g.TimeLeft;
            var bot0 = bot != null ? bot.transform.position : Vector3.zero;
            g.ServerTestReadySetRock(false);
            yield return null;
            yield return null;
            Check(g.RockHeld && g.FightFrozen && NetGame.RockWord(g, out _) == "READY", $"READY: everyone's frozen ({NetGame.RockWord(g, out _)})");
            yield return new WaitForSeconds(0.4f);
            yield return Snap("rsr_ready");
            float until = Time.time + 3f;
            while (NetGame.RockWord(g, out _) != "SET" && Time.time < until) yield return null;
            Check(NetGame.RockWord(g, out _) == "SET" && g.FightFrozen, "then SET, still frozen");
            Check(Hud.RockShownWord == "READY" || Hud.RockShownWord == "SET", $"...the HUD draws the words (needs the Hud.DrawGame hook: {Hud.RockShownWord})");
            yield return Snap("rsr_set");
            until = Time.time + 3f;
            while (g.RockHeld && Time.time < until) yield return null;
            float botMoved = bot != null ? Vector3.Distance(bot0, bot.transform.position) : 0f;
            Check(bot == null || botMoved < 0.6f, $"...a bot doesn't move before ROCK! either ({botMoved:0.00} m)");
            Check(!g.RockHeld && !g.FightFrozen && NetGame.RockWord(g, out _) == "ROCK!", "ROCK!: everyone's let go");
            Check(Mathf.Abs(g.TimeLeft - clock0) < 0.6f, $"...and the build clock didn't run meanwhile ({clock0:0.0} -> {g.TimeLeft:0.0} s)");
            yield return new WaitForSeconds(0.1f);
            yield return Snap("rsr_rock");
            // the start's wait: held until this screen's intro is done (played here and skipped), then the countdown
            int reported = NetGame.RockAllReported;
            g.ServerTestReadySetRock(true);
            yield return null;
            Check(g.RockHeld && !g.RockCounting, $"waiting for the intros: held still, no countdown yet ({g.RockLeft:0.0} s to the latest ROCK!)");
            MatchIntro.TestPlay();
            yield return new WaitForSeconds(1f);
            MatchIntro.Skip();
            until = Time.time + 3f;
            while (NetGame.RockAllReported == reported && Time.time < until) yield return null;
            yield return null;
            Check(NetGame.RockAllReported > reported && g.RockCounting && g.RockLeft <= NetGame.RockCountdown + 0.1, $"...the intro skipped here: the host starts READY / SET / ROCK! at once ({g.RockLeft:0.0} s)");
            until = Time.time + 5f;
            while (g.RockHeld && Time.time < until) yield return null;
            Check(!g.FightFrozen, "...and lets everyone go at ROCK!");
            yield return new WaitForSeconds(1.5f);

            // ---- 2. the respawn comes the moment the kill cam ends
            if (bot != null)
            {
                foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
                if (bot.Dead.Value) bot.ServerRespawn(false);
                var near = me.transform.position + me.transform.forward * 3f;
                near.y = MapBuilder.Height(near.x, near.z) + 0.1f;
                bot.TeleportRpc(near, Quaternion.LookRotation(me.transform.position - near).eulerAngles.y);
                yield return new WaitForSeconds(1.5f);
                me.ServerKill(bot);
                double killedAt = g.NetworkManager.ServerTime.Time;
                float killCam = Cfg.KillCamTime + DeathReplay.Duration;
                double wait = me.RespawnAt.Value - killedAt;
                Check(Mathf.Abs((float)wait - killCam) < 0.3f, $"killed by someone: the respawn's due when the kill cam ends ({wait:0.00} s; the kill cam's {killCam:0.00} s)");
                float diedAt = Time.time, lastCam = -1f;
                until = Time.time + killCam + 4f;
                while (me.Dead.Value && Time.time < until)
                {
                    if (pc.KillCamTarget != null) lastCam = Time.time;
                    yield return null;
                }
                float back = Time.time - diedAt;
                Check(!me.Dead.Value && back < killCam + 0.6f, $"...back (behind the wall: home) {back:0.0} s after the kill");
                Check(lastCam < 0f || Time.time - lastCam < 0.5f, $"...no wait after the kill cam ({(lastCam < 0f ? -1f : Time.time - lastCam):0.00} s between them)");
                yield return new WaitForSeconds(1f);
                me.ServerKill(null);
                double plain = me.RespawnAt.Value - g.NetworkManager.ServerTime.Time;
                Check(Mathf.Abs((float)plain - Cfg.RespawnTime) < 0.3f, $"a death with no killer: the usual {Cfg.RespawnTime:0} s ({plain:0.0} s)");
                until = Time.time + Cfg.RespawnTime + 3f;
                while (me.Dead.Value && Time.time < until) yield return null;
                yield return new WaitForSeconds(1f);
            }

            // ---- 3. at most Cfg.MaxDoors doors a base
            int doors = Structure.DoorsOf(team);
            for (int i = doors; i < Cfg.MaxDoors; i++)
            {
                var go = Instantiate(Bootstrap.I.structurePrefab, Cfg.BaseCenter[team] + new Vector3(i * 0.5f, -30f, 0f), Quaternion.identity); // (out of sight: only counted)
                go.GetComponent<Structure>().ServerInit(PieceType.Doorway, team, default, false);
                go.GetComponent<NetworkObject>().Spawn(true);
            }
            yield return new WaitForSeconds(0.5f);
            Check(Structure.DoorsOf(team) == Cfg.MaxDoors, $"the base has {Structure.DoorsOf(team)} doors (the most: {Cfg.MaxDoors})");
            me.ServerGive(Cfg.CurrencyItem, 2000);
            me.ServerGive(Item.BuildingPlan, 1);
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            int refused = PlayerNet.DoorLimitRefusals;
            me.PlaceRpc((byte)PieceType.Doorway, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            Check(PlayerNet.DoorLimitRefusals == refused + 1 && Structure.DoorsOf(team) == Cfg.MaxDoors, $"an 11th door is refused ({Structure.DoorsOf(team)} doors)");
            yield return Snap("doors_limit");
            // one fewer: it can go in again
            foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.PType == PieceType.Doorway && s.Team.Value == team && s.transform.position.y < -10f) { s.NetworkObject.Despawn(true); break; }
            yield return new WaitForSeconds(0.5f);
            me.PlaceRpc((byte)PieceType.Doorway, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            Check(Structure.DoorsOf(team) == Cfg.MaxDoors && PlayerNet.DoorLimitRefusals == refused + 1, $"...with one taken away, the door goes in ({Structure.DoorsOf(team)} doors)");

            Log("prompt13 test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
