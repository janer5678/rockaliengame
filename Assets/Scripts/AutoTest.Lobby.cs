using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest newmenu (no -host): screenshots of the new main menu's screens over the UFO in space, and the first-
        /// launch name screen. -autotest lobby -host (+ clients, run_autotests "lobby+client2"): the ship lobby - every peer
        /// sees it (no stadium on screen), the team picker swaps a team, the match doesn't start until everyone is READY,
        /// then it does.
        /// </summary>
        IEnumerator NewMenuShots()
        {
            Hud.TestNewMenu = true;
            yield return new WaitForSeconds(2.5f);
            int n = 0;
            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.7f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"newmenu_{n++:00}_{name}.png"));
                Log("shot " + name);
                yield return null; yield return null;
            }
            Check(!Hud.DevMenuShown && MenuSpace.Showing, "the new main menu is up over the UFO in space");
            yield return Shot("root");
            yield return new WaitForSeconds(4f);
            yield return Shot("root_later");
            foreach (var p in new[] { 1, 2, 3, 4, 5, 6, 7 })
            {
                Hud.TestNewPage(p);
                yield return Shot("page" + p);
            }
            Hud.TestNameScreen = true;
            yield return Shot("name_screen");
            Hud.TestNameScreen = false;
            Hud.TestNewMenu = false;
            yield return new WaitForSeconds(0.3f);
            Check(Hud.DevMenuShown && !MenuSpace.Showing, "Tab's dev main menu (the old one) is still there");
            yield return Shot("dev_menu");
            Log("new menu shots done");
            Application.Quit(0);
        }


        /// <summary>-autotest mapshots -shotdir Assets/Game/Resources/MapShots (absolute): a picture of each map from the air
        /// for the main menu's CHOOSE MAP (MapShots/&lt;kind&gt;.png), with the menu's trees on it and no HUD.</summary>
        IEnumerator MapShots()
        {
            yield return new WaitForSeconds(2f);
            Hud.TestHideAll = true;
            MenuScene.TestHold = true;
            var boot = Bootstrap.I;
            foreach (var kind in new[] { MapKind.Plains, MapKind.Highlands, MapKind.Beach, MapKind.Canyon, MapKind.Frostlake, MapKind.Volcano, MapKind.Ruins })
            {
                int key = (Bootstrap.MapChoice & ~15 & ~Cfg.SmallBit & ~(3 << Cfg.SizeShift)) | (int)kind | ((int)MapSize.Big << Cfg.SizeShift);
                boot.SetMapChoice(key);
                yield return new WaitForSeconds(3f);
                if (MapDome.Root != null) MapDome.Root.SetActive(false); // (its glass would tint the whole picture)
                var cam = Camera.main;
                float h = Cfg.MapHalf;
                var look = new Vector3(0.05f * h, MapBuilder.Height(0, 0), 0.1f * h);
                var pos = new Vector3(-0.62f * h, 0f, -1.02f * h);
                pos.y = MapBuilder.Height(pos.x, pos.z) + 0.42f * h;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                cam.fieldOfView = 52f;
                yield return new WaitForSeconds(1.2f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), kind + ".png"));
                Log("map shot " + kind);
                yield return null; yield return null; yield return null;
            }
            Hud.TestHideAll = false;
            MenuScene.TestHold = false;
            Log("map shots done");
            Application.Quit(0);
        }
        IEnumerator LobbyRoutine(PlayerNet me)
        {
            var nm = NetworkManager.Singleton;
            var g = NetGame.Instance;
            yield return new WaitForSeconds(1f);
            Check(ShipLobby.Active && Time.time - Hud.LobbyShownAt < 0.5f && g.S == GameState.Waiting, "the ship lobby is up before the match (not the stadium)");
            float until = Time.time + 40f;
            while (PlayerNet.All.Count < 3 && Time.time < until) yield return null;
            yield return new WaitForSeconds(2.5f);
            Check(ShipLobby.Seated.Count == PlayerNet.All.Count, $"every player sits in the ship ({ShipLobby.Seated.Count} seated, {PlayerNet.All.Count} players)");
            int onScreen = 0;
            foreach (var p in ShipLobby.Seated) if (ShipLobby.HeadOnScreen(p, out var at) && at.x > 0 && at.x < Screen.width && at.y > 0 && at.y < Screen.height) onScreen++;
            Check(onScreen == ShipLobby.Seated.Count, $"everyone's name tag is on screen ({onScreen})");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobby_{(nm.IsHost ? "host" : "client" + nm.LocalClientId)}.png"));
            yield return new WaitForSeconds(0.5f);
            if (nm.IsHost)
            {
                // a team swap (2v2: the host moves to the other team if there's room)
                int was = me.Team.Value, other = 1 - was;
                int there = 0;
                foreach (var p in PlayerNet.All) if (p.Team.Value == other) there++;
                if (there < Cfg.TeamCap(other))
                {
                    me.LobbyTeamRpc((byte)other);
                    yield return new WaitForSeconds(0.5f);
                    Check(me.Team.Value == other, $"the lobby's team picker moves you to {Cfg.TeamName[other]}");
                }
            }
            // not everyone READY: no start
            yield return new WaitForSeconds(nm.IsHost ? 4f : 1f);
            Check(g.S == GameState.Waiting && !g.StartCounting, "the match waits until everyone is READY");
            me.LobbyReadyRpc(true);
            until = Time.time + 25f;
            while (g.S == GameState.Waiting && Time.time < until) yield return null;
            Check(g.S != GameState.Waiting, "everyone READY: the match starts");
            yield return new WaitForSeconds(1f);
            Check(!ShipLobby.Active, "the lobby's gone once the match is on");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobby_started_{(nm.IsHost ? "host" : "client" + nm.LocalClientId)}.png"));
            yield return new WaitForSeconds(nm.IsHost ? 4f : 1f);
            Log("lobby test done");
            Application.Quit(0);
        }
    }
}
