using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// "-autotest spectate" (Tools/run_autotests.ps1 "spectate+client2:-fast"): a 1v1 host, a client that plays, and a
    /// second client that joins once the match is going - it isn't turned away but spectates: no player object, not
    /// counted, the camera on a player's eyes, and the next / previous click switches who it watches. The host checks
    /// the match carried on as a 1v1 and that the spectator leaving didn't end it.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator SpectateRoutine()
        {
            var nm = NetworkManager.Singleton;
            float timeout = Time.time + 90f;
            while (PlayerNet.Local == null && !Spectator.Active)
            {
                if (Time.time > timeout) { Log("FAIL: neither spawned as a player nor spectating"); Application.Quit(2); yield break; }
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var g = NetGame.Instance;

            if (Spectator.Active)
            {
                Log($"spectating: state={g.S} players={PlayerNet.All.Count}");
                Check(PlayerNet.Local == null && PlayerController.Local == null, "the late joiner has no player object (spectating)");
                Check(g.S != GameState.Waiting || PlayerNet.All.Count >= Cfg.PlayersNeeded, $"it joined a match it couldn't play in ({g.S}, {PlayerNet.All.Count}/{Cfg.PlayersNeeded})");
                Check(PlayerNet.All.Count == Cfg.PlayersNeeded, $"the spectator isn't counted as a player ({PlayerNet.All.Count} players)");
                Check(g.IsSpectator(nm.LocalClientId), "the server lists us as a spectator");
                float until = Time.time + 5f;
                while (Spectator.Target == null && Time.time < until) yield return null;
                yield return new WaitForSeconds(1f);
                var first = Spectator.Target;
                Check(first != null, $"watching a player ({(first != null ? first.DisplayName : "none")})");
                if (first != null)
                {
                    float d = Vector3.Distance(Camera.main.transform.position, first.EyePos);
                    Check(d < 0.8f, $"the camera is at their eyes ({d:0.00} m off)");
                    float pitchOff = Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.x, first.Pitch.Value));
                    float yawOff = Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y, first.transform.eulerAngles.y));
                    Check(pitchOff < 3f && yawOff < 3f, $"looking where they look (pitch {pitchOff:0.0}, yaw {yawOff:0.0} degrees off)");
                    Check(Time.time - Hud.SpectatorBarShownAt < 0.5f && Hud.SpectatorBarText.Contains(first.DisplayName), $"the spectator bar: \"{Hud.SpectatorBarText}\"");
                }
                int sw = Spectator.Switches;
                Spectator.Step(1); // (left click)
                yield return new WaitForSeconds(0.5f);
                var second = Spectator.Target;
                Check(second != null && second != first && Spectator.Switches == sw + 1, $"left click: on to the next player ({(second != null ? second.DisplayName : "none")})");
                if (second != null)
                    Check(Vector3.Distance(Camera.main.transform.position, second.EyePos) < 0.8f, "the camera moved to their eyes");
                Spectator.Step(-1); // (right click)
                yield return new WaitForSeconds(0.3f);
                Check(Spectator.Target == first, "right click: back to the first one");
                Spectator.Step(1);
                Spectator.Step(1);
                yield return new WaitForSeconds(0.3f);
                Check(Spectator.Target == first, "two clicks with two players: round to the first again");
                Log("spectator done - leaving");
                Bootstrap.I.Leave();
                yield return new WaitForSeconds(1f);
                Check(!Spectator.Active && Spectator.Target == null, "after leaving: not spectating any more");
                Application.Quit(0);
                yield break;
            }

            if (nm.IsServer)
            {
                // the host: the match starts with the two players, then the spectator comes in
                float t0 = Time.time + 60f;
                while (g.S == GameState.Waiting && Time.time < t0) yield return null;
                Check(g.S != GameState.Waiting, $"the 1v1 started ({g.S})");
                t0 = Time.time + 60f;
                while (g.Spectators.Count == 0 && Time.time < t0) yield return null;
                Check(g.Spectators.Count == 1, $"a late joiner was let in as a spectator ({g.Spectators.Count}: {(g.Spectators.Count > 0 ? g.Spectators[0].Name.ToString() : "")})");
                Check(PlayerNet.All.Count == Cfg.PlayersNeeded && Spectator.ServerPlayerClients(nm) == Cfg.PlayersNeeded, $"still {PlayerNet.All.Count} players ({nm.ConnectedClientsIds.Count} connected)");
                var state = g.S;
                t0 = Time.time + 60f;
                while (g.Spectators.Count > 0 && Time.time < t0) yield return null;
                yield return new WaitForSeconds(1f);
                Check(g.Spectators.Count == 0, "the spectator left");
                Check(g.S != GameState.GameOver, $"the spectator leaving didn't end the match ({g.S}, was {state})");
                Log("host done");
                Application.Quit(0);
                yield break;
            }

            // the client that plays: just stays in until the host has finished
            float end = Time.time + 150f;
            while (nm.IsConnectedClient && Time.time < end) yield return null;
            Log("player client done");
            Application.Quit(0);
        }
    }
}
