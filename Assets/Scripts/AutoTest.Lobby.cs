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
                if (p == 7)
                {
                    // the map page: slow shots of the map at eye level, the first from a corner across the whole map
                    Check(MenuScene.Preview && !MenuSpace.Showing, "the map page shows the map itself (slow shots), not the UFO");
                    yield return new WaitForSeconds(0.2f);
                    var at0 = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                    var rot0 = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
                    yield return new WaitForSeconds(2f);
                    yield return Shot("page7_map_2s");
                    var at1 = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                    var rot1 = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
                    float h = at1.y - MapBuilder.GroundHeight(at1.x, at1.z);
                    bool corner = Mathf.Abs(at0.x) > Cfg.MapHalf * 0.6f && Mathf.Abs(at0.z) > Cfg.MapHalf * 0.6f;
                    Check(MenuScene.ShotIndex == 0 && corner && h < 2.5f && Vector3.Distance(at0, at1) < 8f && Quaternion.Angle(rot0, rot1) < 25f,
                        $"the first shot is from a corner ({at0.x:0}, {at0.z:0}) at eye level ({h:0.0} m), drifting slowly ({Vector3.Distance(at0, at1):0.0} m, {Quaternion.Angle(rot0, rot1):0} deg in 2 s)");
                    yield return new WaitForSeconds(3f);
                    yield return Shot("page7_map_5s");
                }
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
            float sink = ShipLobby.WorstFootSink;
            Check(sink < 0.04f, $"no seated alien's feet go through the floor ({sink:0.000} m under at worst)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobby_{(nm.IsHost ? "host" : "client" + nm.LocalClientId)}.png"));
            yield return new WaitForSeconds(0.5f);
            if (nm.IsHost)
            {
                // close on each of them, twice (their props and what they're doing with them)
                for (int i = 0; i < ShipLobby.Seated.Count; i++)
                    for (int s = 0; s < 3; s++)
                    {
                        ShipLobby.TestFocus = i;
                        ShipLobby.TestPhase = s == 0 ? 0.1f : s == 1 ? 1.2f : 2.9f; // (at rest, the drag / swig, breathing the smoke out)
                        yield return new WaitForSeconds(0.6f);
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobby_close_{i}_{s}.png"));
                        yield return null;
                    }
                ShipLobby.TestFocus = -1;
                ShipLobby.TestPhase = -1f;
                yield return new WaitForSeconds(0.3f);
                // looking round the room: the mouse at the left edge turns the camera that way
                ShipLobby.TestMouse = new Vector2(0.01f, 0.5f);
                yield return new WaitForSeconds(1.2f);
                Check(ShipLobby.LookYaw < -20f, $"the mouse at the screen's edge looks round the room ({ShipLobby.LookYaw:0} degrees)");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_look_left.png"));
                yield return null;
                ShipLobby.TestMouse = new Vector2(0.99f, 0.99f);
                yield return new WaitForSeconds(2.4f);
                Check(ShipLobby.LookPitch == 0f, $"the mouse at the top edge doesn't tilt the view up or down ({ShipLobby.LookPitch:0.0} degrees)");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_look_right.png"));
                yield return null;
                ShipLobby.TestMouse = new Vector2(0.5f, 0.5f);
                yield return new WaitForSeconds(0.3f);
                ShipLobby.TestMouse = new Vector2(-1f, -1f);
                Check(ShipLobby.IdlesAllDifferent, "no two seated aliens have the same idle");
                // (hats and CUSTOMISE ALIEN are off for now: nobody wears one)
                me.SetHatRpc(3);
                yield return new WaitForSeconds(0.5f);
                Check(!ShipLobby.AnyHat, "hats are off: none shows even when one is picked");
                me.SetHatRpc(0);
                // SPECTATE: a client gets up off the couch to watch, then PLAY puts them back
                ulong other = 0;
                foreach (var p in PlayerNet.All) if (p != null && !p.Bot.Value && p.OwnerClientId != nm.LocalClientId) other = p.OwnerClientId;
                int before = PlayerNet.All.Count;
                bool off = g.ServerSetSpectating(other, true);
                yield return new WaitForSeconds(1.5f);
                Check(off && PlayerNet.All.Count == before - 1 && g.IsSpectator(other), $"SPECTATE: client {other} leaves the couch to watch ({PlayerNet.All.Count} players, {g.Spectators.Count} watching)");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_one_spectating.png"));
                yield return null;
                bool on = g.ServerSetSpectating(other, false);
                until = Time.time + 6f;
                while (PlayerNet.All.Count < before && Time.time < until) yield return null;
                yield return new WaitForSeconds(1.5f);
                Check(on && PlayerNet.All.Count == before && !g.IsSpectator(other), $"PLAY: they take a seat again ({PlayerNet.All.Count} players)");
            }
            else
            {
                // (a client: watched for a moment by the host's SPECTATE check - the lobby with no seat of our own)
                float watchUntil = Time.time + 20f;
                bool sawWatch = false;
                while (Time.time < watchUntil && !sawWatch) { if (Spectator.Active && ShipLobby.Active) sawWatch = true; yield return null; }
                if (sawWatch)
                {
                    yield return new WaitForSeconds(0.4f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobby_spectating_client{nm.LocalClientId}.png"));
                    yield return null;
                    Check(ShipLobby.Active, "spectating, you still see the ship lobby");
                    until = Time.time + 15f;
                    while (PlayerNet.Local == null && Time.time < until) yield return null;
                    Check(PlayerNet.Local != null, "PLAY again: our alien's back on the couch");
                    me = PlayerNet.Local;
                }
            }
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
