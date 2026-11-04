using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest exhibition -exhibition (a host and three clients, over IP: run_autotests "exhibition+client3:-exhibition"):
        /// the exhibition build's whole round. The READY screen over the UFO (no ship lobby room), Highlands medium 2v2; it
        /// doesn't start until the fourth READY; the UFO crash plays; everyone starts the tutorial at its first step; each player
        /// raids a hut of their own; the tutorial doesn't end until everyone's finished it; into a Classic match with no READY
        /// and no second crash, on the same teams; and when it's over, back to the READY screen with nobody ready.
        /// </summary>
        IEnumerator ExhibitionRoutine()
        {
            NetGame.TestLobby = true;
            MatchIntro.TestsToo = true;
            yield return ExhibitionRelayConnect();
            float until = Time.time + 90f;
            while ((PlayerNet.Local == null || NetGame.Instance == null || !NetGame.Instance.IsSpawned || PlayerNet.All.Count < Exhibition.Needed) && Time.time < until) yield return null;
            if (PlayerNet.All.Count < Exhibition.Needed) { Log($"FAIL: only {PlayerNet.All.Count} players got in"); Application.Quit(2); yield break; }
            bool host = NetworkManager.Singleton.IsHost;
            Check(Cfg.MapSeed == Exhibition.Seed && MapBuilder.IsBuilt(Cfg.MapKey, Cfg.MapSeed), "the map was built before connecting (no rebuild with the connection open)");
            string who = host ? "host" : "client" + NetworkManager.Singleton.LocalClientId;
            var me = PlayerNet.Local;
            yield return new WaitForSeconds(2f);
            Check(ShipLobby.Active && Hud.ExhibitionLobbyShownAt > Time.time - 1f, "the READY screen is up");
            Check(Cfg.Tutorial && Cfg.Map == MapKind.Highlands && Cfg.Size == MapSize.Big && Cfg.Mode == GameMode.Teams, $"tutorial, Highlands medium, 2v2 ({Cfg.Map}, {Cfg.Size}, {Cfg.Mode})");
            Check(MenuSpace.Showing && GameObject.Find("ShipLobby") == null, "the UFO in space behind it, no ship lobby room");
            int[] onTeam = new int[2];
            foreach (var p in PlayerNet.All) onTeam[p.Team.Value]++;
            Check(onTeam[0] == 2 && onTeam[1] == 2, $"two on each team ({onTeam[0]} v {onTeam[1]})");
            yield return Snap($"exhibition_ready_{who}");

            // the clients READY, the host last: nothing starts until it does
            if (host)
            {
                until = Time.time + 30f;
                while (Time.time < until)
                {
                    int ready = 0;
                    foreach (var p in PlayerNet.All) if (p.LobbyReady.Value) ready++;
                    if (ready >= Exhibition.Needed - 1) break;
                    yield return null;
                }
                yield return new WaitForSeconds(5f);
                Check(NetGame.Instance.S == GameState.Waiting && !NetGame.Instance.StartCounting, "three of four READY: it waits");
                yield return Snap("exhibition_three_ready");
            }
            else yield return new WaitForSeconds(NetworkManager.Singleton.LocalClientId);
            me.LobbyReadyRpc(true);
            until = Time.time + 90f;
            while (NetGame.Instance.S == GameState.Waiting && Time.time < until) yield return null;
            Check(NetGame.Instance.S != GameState.Waiting, "everyone READY: it starts by itself");

            // the crash
            yield return new WaitForSeconds(2.5f);
            Check(MatchIntro.Active && MatchIntro.Plays == 1, "the UFO crash is playing");
            Check(MenuSpace.CrashUp, "...on its crash scene");
            yield return Snap($"exhibition_crash_{who}");
            until = Time.time + 25f;
            while (MatchIntro.Active && Time.time < until) yield return null;

            // the tutorial, from the first step for everyone (a client isn't dropped in at the regroup step)
            yield return new WaitForSeconds(1.5f);
            Check(Tutorial.On && Tutorial.Friend && Tutorial.StepId == "machine", $"the tutorial's first step ({Tutorial.StepId})");
            yield return Snap($"exhibition_tutorial_{who}");

            // a hut each: teammates don't share one
            Tutorial.TestGoTo("raid");
            until = Time.time + 12f;
            while (Time.time < until)
            {
                int huts = 0;
                foreach (var p in PlayerNet.All) if (p.TutHut.Value != Vector3.zero) huts++;
                if (huts >= PlayerNet.All.Count) break;
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            {
                var seen = new System.Collections.Generic.List<Vector3>();
                bool apart = true;
                foreach (var p in PlayerNet.All)
                {
                    foreach (var o in seen) if (Vector3.Distance(o, p.TutHut.Value) < 4f) apart = false;
                    if (p.TutHut.Value != Vector3.zero) seen.Add(p.TutHut.Value);
                }
                Check(seen.Count == 4 && apart, $"four huts, apart ({seen.Count})");
                Check(Tutorial.HutDoor != null, "my own hut's door is there to blow");
                yield return Snap($"exhibition_raid_{who}");
            }

            // finishing it: the host last, and it waits for the host
            int myTeam = me.Team.Value, plays = MatchIntro.Plays;
            if (host)
            {
                until = Time.time + 30f;
                while (Time.time < until)
                {
                    int done = 0;
                    foreach (var p in PlayerNet.All) if (p.TutStep.Value == Tutorial.FinishedStep) done++;
                    if (done >= PlayerNet.All.Count - 1) break;
                    yield return null;
                }
                yield return new WaitForSeconds(6f);
                Check(Cfg.Tutorial && NetGame.Instance.ExhibitionNextIn > 60f, $"three finished: it waits for the last one ({NetGame.Instance.ExhibitionNextIn:0} s to go)");
                yield return Snap("exhibition_waiting_host");
            }
            else yield return new WaitForSeconds(NetworkManager.Singleton.LocalClientId + 3);
            Tutorial.TestFinish();
            if (!host) { yield return new WaitForSeconds(1.5f); yield return Snap($"exhibition_finished_{who}"); }

            // into the match by itself: no READY, no crash, the same teams
            // (the new session's game, not the old one still closing: a client takes on the next part's settings as it's told to go)
            var tutorialGame = NetGame.Instance;
            until = Time.time + 100f;
            while (Time.time < until && !(!Cfg.Tutorial && NetGame.Instance != null && NetGame.Instance != tutorialGame && NetGame.Instance.IsSpawned && NetGame.Instance.S != GameState.Waiting && PlayerNet.Local != null)) yield return null;
            Check(Cfg.Rules == GameRules.AutoWood && Cfg.Map == MapKind.Highlands && Cfg.Size == MapSize.Big && Cfg.Mode == GameMode.Teams, $"into a Classic match, Highlands medium, 2v2 ({Cfg.Rules}, {Cfg.Map}, {Cfg.Size})");
            Check(NetGame.Instance != null && NetGame.Instance.S != GameState.Waiting, "...it started with nobody pressing READY");
            Check(PlayerNet.Local != null && PlayerNet.Local.Team.Value == myTeam, "...on the same team");
            Check(PlayerNet.All.Count == Exhibition.Needed, $"...everyone's back ({PlayerNet.All.Count})");
            Check(MatchIntro.Plays == plays, "...no second crash");
            Check(!MatchIntro.Active, "...straight into it");
            yield return new WaitForSeconds(3f);
            yield return Snap($"exhibition_match_{who}");

            // the match ends: back to the READY screen, not the front screen
            if (host) NetGame.Instance.ServerVictoryCutscene((myTeam + 1) % 2, "test over");
            until = Time.time + 120f;
            while (Time.time < until && !(Cfg.Tutorial && ShipLobby.Active && PlayerNet.Local != null && PlayerNet.Local.IsSpawned && NetGame.Instance.S == GameState.Waiting)) yield return null;
            Check(Cfg.Tutorial && ShipLobby.Active, "the match over: back to the READY screen");
            yield return new WaitForSeconds(4f);
            Check(Hud.ExhibitionLobbyShownAt > Time.time - 1f, "...it's the READY screen that's drawn");
            Check(PlayerNet.Local != null && !PlayerNet.Local.LobbyReady.Value, "...nobody's READY");
            Check(PlayerNet.Local != null && PlayerNet.Local.Team.Value == myTeam, "...the same teams");
            Check(PlayerNet.All.Count == Exhibition.Needed, $"...and everyone's back ({PlayerNet.All.Count})");
            yield return Snap($"exhibition_ready_again_{who}");
            yield return new WaitForSeconds(host ? 60f : 30f); // (everyone checks theirs at a different moment: nobody leaves until they all have, the host last)
            Log("exhibition test done");
            Application.Quit(0);
        }

        /// <summary>-relayfile FILE -relayrole host|join (Tools/run_relay_test.ps1): through real Unity Relay, as on the front
        /// screen - the host's HOST writes its join code to the file, the others type it in and JOIN.</summary>
        IEnumerator ExhibitionRelayConnect()
        {
            string file = null, role = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-relayfile") file = args[i + 1];
                if (args[i] == "-relayrole") role = args[i + 1];
            }
            if (file == null) yield break;
            yield return new WaitForSeconds(2f);
            var boot = Bootstrap.I;
            float until = Time.time + 60f;
            if (role == "host")
            {
                boot.ExhibitionHost();
                while (!boot.IsHostSession && !boot.RelayFailed && Time.time < until) yield return null;
                Check(boot.IsHostSession && ExhibitionRelay.Active, $"HOST through Relay (\"{boot.Status}\")");
                if (!boot.IsHostSession) { Application.Quit(3); yield break; }
                Log($"join code {ExhibitionRelay.Code}");
                System.IO.File.WriteAllText(file, ExhibitionRelay.Code);
                yield break;
            }
            while (!System.IO.File.Exists(file) && Time.time < until) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(0.5f);
            boot.Ip = System.IO.File.ReadAllText(file).Trim();
            boot.Join();
            until = Time.time + 60f;
            while (!boot.InSession && Time.time < until) yield return null;
            Check(boot.InSession && ExhibitionRelay.Active, $"JOIN through Relay with the code {boot.Ip} (\"{boot.Status}\")");
        }

        /// <summary>-autotest exhibitionfront -exhibition (no -host): the front screen, and HOST without Relay set up: it says
        /// why and offers plain IP.</summary>
        IEnumerator ExhibitionFrontRoutine()
        {
            yield return new WaitForSeconds(2.5f);
            var boot = Bootstrap.I;
            Check(!boot.InSession && Hud.ExhibitionFrontShownAt > Time.time - 1f, "the front screen is up");
            Check(MenuSpace.Showing, "...over the UFO");
            yield return Snap("exhibition_front");
            boot.ExhibitionHost();
            float until = Time.time + 40f;
            while (Time.time < until && (boot.RelayBusy || !boot.RelayFailed) && !boot.InSession) yield return null;
            Log($"HOST through Relay: in session {boot.InSession}, failed {boot.RelayFailed}, status \"{boot.Status}\"");
            yield return new WaitForSeconds(1f);
            yield return Snap("exhibition_front_after_host");
            Application.Quit(0);
        }
    }
}
