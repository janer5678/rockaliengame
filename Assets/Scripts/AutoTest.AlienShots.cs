using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest aliens -host -solo -mode 2v2 -shotdir DIR: a blue and a red alien (bots, held still) side by side on open
        /// ground, photographed from all round (eight angles, then above, low down and close up on each), with the HUD off.
        /// </summary>
        IEnumerator AlienShots(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            if (g != null) g.TimerPaused.Value = true;
            // a bot on each team (I'm blue: the second blue one's a bot too)
            PlayerNet Bot(int team)
            {
                foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value && p.Team.Value == team) return p;
                return null;
            }
            // fill the teams so the match starts (in the waiting stadium everyone holds a rock)
            for (int i = 0; i < 4 && PlayerNet.All.Count < Cfg.PlayersNeeded; i++) PlayerNet.ServerAddBot();
            for (float t = 0; t < 40f && g != null && g.S == GameState.Waiting; t += 0.5f) yield return new WaitForSeconds(0.5f);
            for (float t = 0; t < 20f && g != null && g.FightFrozen; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (g != null) g.TimerPaused.Value = true;
            yield return new WaitForSeconds(1.5f);
            var blue = Bot(0);
            var red = Bot(1);
            if (blue == null || red == null) { Log($"FAIL: couldn't get a blue and a red bot ({blue != null}, {red != null})"); Application.Quit(1); yield break; }
            var home = Cfg.BaseCenter[me.Team.Value];
            var spot = OnGround(home - Cfg.BackDir(me.Team.Value) * 16f, 0.05f); // (the flat grass in front of our base)
            foreach (var b in new[] { blue, red })
            {
                foreach (var mb in b.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
                if (b.Dead.Value) b.ServerRespawn(false);
                for (int i = 0; i < b.Inv.Count; i++) b.Inv[i] = default;
                b.Hat.Value = 0;
            }
            yield return new WaitForSeconds(0.5f);
            var a = OnGround(spot + Vector3.right * 0.8f, 0.05f);
            var c = OnGround(spot - Vector3.right * 0.8f, 0.05f);
            blue.TeleportRpc(a, 180f);
            red.TeleportRpc(c, 180f);
            // me out of the way, the HUD off, and a camera of our own
            pc.LocalTeleport(OnGround(home, 0.1f), 0f);
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value && p != blue && p != red) { foreach (var mb in p.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false; p.TeleportRpc(OnGround(home, 0.1f), 0f); }
            var hud = FindObjectOfType<Hud>();
            if (hud != null) hud.enabled = false;
            var cam = new GameObject("alien camera").AddComponent<Camera>();
            cam.depth = 100;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            yield return new WaitForSeconds(1.5f);
            var mid = (blue.transform.position + red.transform.position) * 0.5f + Vector3.up * 0.9f;
            for (int i = 0; i < 8; i++)
            {
                float yaw = i * 45f; // (0: from the front - they face -z)
                var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                cam.transform.position = mid - dir * 4.2f + Vector3.up * 0.5f;
                cam.transform.LookAt(mid);
                yield return Snap($"aliens_{i * 45:000}deg");
            }
            cam.transform.position = mid + new Vector3(-0.3f, 4.2f, -1.6f); // (above, from the front)
            cam.transform.LookAt(mid);
            yield return Snap("aliens_above");
            cam.transform.position = mid + new Vector3(0f, -0.7f, -3.2f); // (low down, from the front)
            cam.transform.LookAt(mid + Vector3.up * 0.3f);
            yield return Snap("aliens_low");
            foreach (var (b, n) in new[] { (blue, "blue"), (red, "red") })
            {
                var head = b.transform.position + Vector3.up * 1.7f;
                foreach (float yaw in new[] { 0f, 45f, 315f })
                {
                    var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                    cam.transform.position = head - dir * 2.3f + Vector3.up * 0.1f;
                    cam.transform.LookAt(head - Vector3.up * 0.35f);
                    yield return Snap($"alien_{n}_close_{yaw:000}");
                }
            }
            Log("aliens done");
            Application.Quit(0);
        }
    }
}
