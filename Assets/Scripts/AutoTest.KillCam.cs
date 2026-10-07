using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest killcam -host -solo: killed by someone, the kill cam glides to their face, then the replay plays the
        /// last seconds back out of the killer's eyes (DeathReplay.cs) with a copy of us acting it out - and when it's over
        /// the camera, the copy and the killer's body are back to normal and the new death screen shows.
        /// </summary>
        IEnumerator KillCamRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            g.TimerPaused.Value = true;
            int enemy = (me.Team.Value + 1) % Mathf.Max(2, Cfg.TeamCount);
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.6f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            if (bot == null) { Log("FAIL: no bot to be killed by"); Application.Quit(1); yield break; }
            foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false; // (it stands still for the picture)
            if (bot.Dead.Value) bot.ServerRespawn(false);
            yield return new WaitForSeconds(0.3f);
            var near = me.transform.position + me.transform.forward * 5f;
            near.y = MapBuilder.Height(near.x, near.z) + 0.1f;
            float face = Quaternion.LookRotation(me.transform.position - near).eulerAngles.y;
            bot.TeleportRpc(near, face);
            pc.SetLook(Quaternion.LookRotation(near - me.transform.position).eulerAngles.y, 0f);
            // a few seconds of us walking about in front of them (the replay's footage)
            float walkUntil = Time.time + 3.5f;
            while (Time.time < walkUntil) { me.transform.position += me.transform.right * Mathf.Sin(Time.time * 2f) * 1.2f * Time.deltaTime; yield return null; }
            me.ServerKill(bot);
            yield return new WaitForSeconds(0.6f);
            Check(pc.KillCamTarget == bot && !pc.KillCamReplay, "killed: the kill cam looks at who did it");
            yield return Snap("killcam_glide");
            float until = Time.time + 3f;
            while (!pc.KillCamReplay && Time.time < until) yield return null;
            Check(pc.KillCamReplay, "then the replay starts");
            yield return new WaitForSeconds(0.8f);
            var cam = Camera.main;
            float off = cam != null ? Vector3.Distance(cam.transform.position, bot.EyePos) : 99f;
            Check(off < 1.2f, $"...out of the killer's eyes ({off:0.00} m from them)");
            Check(GameObject.Find("replay alien") != null, "...with a copy of us acting out what we did");
            yield return Snap("killcam_replay");
            until = Time.time + DeathReplay.Duration + 2f;
            while (pc.KillCamReplay && Time.time < until) yield return null;
            Check(!pc.KillCamReplay && GameObject.Find("replay alien") == null, "the replay ends and its copy goes");
            yield return new WaitForSeconds(0.15f);
            if (me.Dead.Value) yield return Snap("death_screen");
            until = Time.time + Cfg.RespawnTime + 4f;
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
