using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest killcam -host -solo: hit a couple of times and then killed by someone, the kill cam glides to their face,
        /// then the replay plays the last seconds back out of the killer's eyes (DeathReplay.cs): their hands and weapon on
        /// screen, a damage number for each hit as they saw it, a copy of us acting it out that goes down as a ragdoll at the
        /// end, the slow motion starting just before the killing blow - quick: about 1.6 s of glide and 3.7 of replay. When it's over the camera, the copy, the hands and the
        /// killer's body are back to normal and the death screen shows.
        /// </summary>
        IEnumerator KillCamRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            g.TimerPaused.Value = true;
            // (before the wall drops the server brings you back after Cfg.RespawnTime - longer here, so the whole kill cam plays)
            float respawnWas = Cfg.RespawnTime;
            Cfg.RespawnTime = Cfg.KillCamTime + DeathReplay.Duration + 3f;
            int enemy = (me.Team.Value + 1) % Mathf.Max(2, Cfg.TeamCount);
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.6f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            if (bot == null) { Log("FAIL: no bot to be killed by"); Application.Quit(1); yield break; }
            foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false; // (it stands still for the picture)
            if (bot.Dead.Value) bot.ServerRespawn(false);
            yield return new WaitForSeconds(0.3f);
            var near = me.transform.position + me.transform.forward * 3f;
            near.y = MapBuilder.Height(near.x, near.z) + 0.1f;
            float face = Quaternion.LookRotation(me.transform.position - near).eulerAngles.y;
            bot.TeleportRpc(near, face);
            pc.SetLook(Quaternion.LookRotation(near - me.transform.position).eulerAngles.y, 0f);
            // a few seconds of us walking about in front of them (the replay's footage), swung at and hit twice
            float walkFrom = Time.time, walkUntil = Time.time + 4f;
            int hits = 0;
            while (Time.time < walkUntil)
            {
                me.transform.position += me.transform.right * Mathf.Sin(Time.time * 2f) * 1.2f * Time.deltaTime;
                float w = Time.time - walkFrom;
                if ((hits == 0 && w > 1.6f) || (hits == 1 && w > 2.8f))
                {
                    hits++;
                    bot.BotSwing();
                    bot.BotHit(me, 15f, me.transform.position + Vector3.up * 1.1f);
                }
                yield return null;
            }
            float hpBefore = me.Health.Value;
            Log($"health before the kill: {hpBefore:0} (hit {hits} times)");
            bot.BotSwing();
            me.ServerKill(bot);
            float diedAt = Time.time;
            yield return new WaitForSeconds(0.6f);
            Check(pc.KillCamTarget == bot && !pc.KillCamReplay, "killed: the kill cam looks at who did it");
            yield return Snap("killcam_glide");
            float until = Time.time + Cfg.KillCamTime + 3f;
            while (!pc.KillCamReplay && Time.time < until) yield return null;
            float replayFrom = Time.time;
            Check(pc.KillCamReplay, $"then the replay starts ({replayFrom - diedAt:0.0} s after the kill)");
            Check(replayFrom - diedAt > 1.2f && replayFrom - diedAt < 2.3f, "...after a quick glide (about 1.6 s)");
            yield return new WaitForSeconds(0.8f);
            var cam = Camera.main;
            float off = cam != null ? Vector3.Distance(cam.transform.position, bot.EyePos) : 99f;
            Check(off < 1.2f, $"...out of the killer's eyes ({off:0.00} m from them)");
            Check(GameObject.Find("replay alien") != null, "...with a copy of us acting out what we did");
            Check(DeathReplay.KillerHandsShown, "...and the killer's hands and weapon on screen");
            yield return Snap("killcam_replay");
            // through to the end: the damage numbers, the slow motion and the fall
            bool number = false, hands = true, slowBeforeFall = false, shotHit = false, shotFall = false;
            float slowFrom = -1f;
            float fellAt = -1f, rateAtFall = 1f;
            until = Time.time + DeathReplay.Duration + 3f;
            while (pc.KillCamReplay && Time.time < until)
            {
                hands &= DeathReplay.KillerHandsShown;
                if (Time.time - Hud.ReplayNumberShownAt < 0.1f && !number)
                {
                    number = true;
                    if (!shotHit) { shotHit = true; yield return Snap("killcam_hit"); }
                }
                if (!DeathReplay.GhostFell && DeathReplay.Rate < 1f) { slowBeforeFall = true; if (slowFrom < 0f) slowFrom = Time.time; }
                if (DeathReplay.GhostFell && fellAt < 0f) { fellAt = Time.time; rateAtFall = DeathReplay.Rate; }
                if (fellAt > 0f && Time.time - fellAt > 0.9f && !shotFall) { shotFall = true; yield return Snap("killcam_ragdoll"); }
                yield return null;
            }
            float total = Time.time - diedAt;
            Check(number && DeathReplay.HitsPlayed >= 1, $"a damage number showed in the replay ({DeathReplay.HitsPlayed} hits played back)");
            Check(hands, "the killer's hands stayed on screen all through the replay");
            Check(fellAt > 0f, $"our copy went down as a ragdoll at the end ({(fellAt > 0f ? fellAt - replayFrom : -1f):0.0} s into the replay)");
            Check(DeathReplay.SlowFromReal > 0f && DeathReplay.FellAtReal > DeathReplay.SlowFromReal && rateAtFall < 1f, $"slow motion just before the death and through the fall (rate {rateAtFall:0.00})");
            Check(total > 4f && total < 7f, $"the whole kill cam ran {total:0.0} s (quicker: it was about 9.5)");
            float lead = DeathReplay.FellAtReal - DeathReplay.SlowFromReal;
            Check(DeathReplay.SlowFromReal > 0f && lead > 0.1f && lead < DeathReplay.SlowBefore / DeathReplay.SlowRate + 0.35f, $"the slow motion starts just before the killing blow ({lead:0.00} s before the fall, real time)");
            Check(!pc.KillCamReplay && GameObject.Find("replay alien") == null && GameObject.Find("replay ragdoll") == null && !DeathReplay.KillerHandsShown,
                "the replay ends and its copy and the killer's hands go");
            yield return new WaitForSeconds(0.15f);
            if (me.Dead.Value) yield return Snap("death_screen");
            Cfg.RespawnTime = respawnWas;
            until = Time.time + Cfg.RespawnTime + 8f;
            while (me.Dead.Value && Time.time < until) yield return null;
            Check(!me.Dead.Value, "and we respawn as usual");
            // the death screen on its own (no killer: no kill cam)
            yield return new WaitForSeconds(2f);
            me.ServerKill(null);
            yield return new WaitForSeconds(0.9f);
            yield return Snap("death_screen_plain");
            until = Time.time + Cfg.RespawnTime + 4f;
            while (me.Dead.Value && Time.time < until) yield return null;
            Log("killcam test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
