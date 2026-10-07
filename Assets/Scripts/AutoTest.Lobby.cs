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
                    // a map with tall grass (Highlands): one of the shots is of a patch of it, as in a match
                    int hk = (Bootstrap.MapChoice & ~15) | (int)MapKind.Highlands;
                    Bootstrap.I.SetMapChoice(hk);
                    yield return new WaitForSeconds(1.5f);
                    Check(MenuScene.TallGrassShots > 0 && MenuScene.FirstTallGrassShot > 0, $"Highlands: the map page's shots show its tall grass ({MenuScene.TallGrassShots} shots of it)");
                    MenuScene.TestJumpShot(MenuScene.FirstTallGrassShot);
                    yield return new WaitForSeconds(1.5f);
                    var g = GrassField.Current;
                    var cp = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                    var fw = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
                    float tallAhead = 0f;
                    if (g != null) for (float d = 2f; d < 20f; d += 1f) tallAhead = Mathf.Max(tallAhead, g.WheatAt(cp.x + fw.x * d, cp.z + fw.z * d));
                    Check(tallAhead > 0.5f && g != null && g.DrawnPatches > 0, $"...the camera looks into a tall grass patch ({tallAhead:0.00} deep, {(g != null ? g.DrawnPatches : 0)} patches drawn)");
                    yield return Shot("page7_tallgrass");
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

        /// <summary>
        /// -autotest lobbymap (a host -host and a client -client 127.0.0.1): the lobby's BACK doesn't close the lobby for
        /// the host - it opens the menu's map page over it (the lobby still running, the host not READY); BACK again goes
        /// back a page (still in the lobby); another map and CONFIRM restart the lobby on it - the client comes back in by
        /// itself and both are in the ship lobby on the new map. Then BACK all the way past the first page closes the lobby.
        /// </summary>
        IEnumerator LobbyMapRoutine()
        {
            var nm = NetworkManager.Singleton;
            float until = Time.time + 60f;
            while ((PlayerNet.Local == null || NetGame.Instance == null || !NetGame.Instance.IsSpawned) && Time.time < until) yield return null;
            if (PlayerNet.Local == null) { Log("FAIL: never got into the lobby"); Application.Quit(2); yield break; }
            bool host = nm.IsHost;
            string who = host ? "host" : "client";
            Check(ShipLobby.Active, $"{who}: in the ship lobby to start with");
            var from = Cfg.Map;
            var to = from == MapKind.Beach ? MapKind.Canyon : MapKind.Beach;
            if (host)
            {
                until = Time.time + 40f;
                while (PlayerNet.All.Count < 2 && Time.time < until) yield return null;
                yield return new WaitForSeconds(2f);
                PlayerNet.Local.LobbyReadyRpc(true);
                yield return new WaitForSeconds(0.5f);
                Hud.TestLobbyBack();
                yield return new WaitForSeconds(1f);
                Check(Hud.LobbyMenuOpen && Hud.LobbyMenuPage == 7 && ShipLobby.Active && nm.IsListening && PlayerNet.All.Count == 2,
                    $"host: BACK in the lobby opens the map page over it, the lobby still open with both in it (page {Hud.LobbyMenuPage}, {PlayerNet.All.Count} players)");
                Check(!PlayerNet.Local.LobbyReady.Value, "host: in the menu the host isn't READY (the match can't start without them)");
                // the hosted map's page: the map itself, in the menu's slow shots over the session's own map (not a picture)
                yield return new WaitForSeconds(1f);
                var mc = Camera.main != null ? Camera.main.transform.position : ShipLobby.Center;
                float eye = mc.y - MapBuilder.GroundHeight(mc.x, mc.z);
                Check(MenuScene.LobbyPreview && Vector3.Distance(mc, ShipLobby.Center) > 200f && Mathf.Abs(mc.x) < Cfg.MapHalf + 5f && Mathf.Abs(mc.z) < Cfg.MapHalf + 5f && eye < 2.5f,
                    $"host: the map page shows the hosted map live - the cinematic shots over it ({mc.x:0}, {mc.z:0}, {eye:0.0} m up)");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobbymap_host_mappage.png"));
                yield return new WaitForSeconds(0.5f);
                Hud.TestMenuBack();
                yield return new WaitForSeconds(0.8f);
                Check(!MenuScene.LobbyPreview, "host: off the map page the live map preview stops");
                Check(Hud.LobbyMenuOpen && Hud.LobbyMenuPage == 6 && nm.IsListening && ShipLobby.Active, $"host: BACK again - the game mode page, still in the lobby (page {Hud.LobbyMenuPage})");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobbymap_host_modepage.png"));
                yield return new WaitForSeconds(0.5f);
                Hud.TestNewPage(7);
                Hud.TestLobbyPickMap(to);
                yield return new WaitForSeconds(0.8f);
                Check(!MenuScene.LobbyPreview, "host: another map picked - its picture (it isn't built), not the live map");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobbymap_host_newmap.png"));
                yield return new WaitForSeconds(0.5f);
                Check(NetGame.Instance != null && NetGame.Instance.S == GameState.Waiting, "host: the match never started while the host was in the menu");
                Hud.TestLobbyConfirm();
            }
            // both: the session restarts on the new map, and everyone's back in the ship lobby
            until = Time.time + 60f;
            bool left = false;
            while (Time.time < until)
            {
                if (NetGame.Instance == null || !NetGame.Instance.IsSpawned) left = true;
                if (left && ShipLobby.Active && NetGame.Instance != null && NetGame.Instance.S == GameState.Waiting && Cfg.Map == to) break;
                yield return null;
            }
            Check(left && ShipLobby.Active && Cfg.Map == to, $"{who}: back in the ship lobby on the new map ({from} -> {Cfg.Map})");
            yield return new WaitForSeconds(3f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobbymap_{who}_after.png"));
            if (!host)
            {
                yield return new WaitForSeconds(1f);
                Log("lobby map test done");
                Application.Quit(0);
                yield break;
            }
            until = Time.time + 20f;
            while (PlayerNet.All.Count < 2 && Time.time < until) yield return null;
            Check(PlayerNet.All.Count == 2 && !Hud.LobbyMenuOpen, $"host: the client's back in too ({PlayerNet.All.Count} players), the menu's gone");
            yield return new WaitForSeconds(3f); // (the client takes its picture and goes)
            // BACK all the way: the lobby only closes past the first page
            Hud.TestLobbyBack();
            yield return new WaitForSeconds(0.3f);
            int expect = Cfg.FreeForAll || Cfg.TeamCap(0) > 1 || Cfg.TeamCap(1) > 1 ? 5 : 4; // (map, mode, [players,] battle type, multiplayer)
            int presses = 0;
            for (; presses < 8 && nm.IsListening; presses++)
            {
                Hud.TestMenuBack();
                yield return new WaitForSeconds(0.4f);
            }
            Check(!nm.IsListening && presses == expect && !Hud.LobbyMenuOpen && Hud.LobbyMenuPage == 0,
                $"host: BACK through every page ({presses} presses) - the lobby closes only past the first one, back to the main menu");
            yield return new WaitForSeconds(1f);
            Log("lobby map test done");
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

                // now and then they look round at each other
                until = Time.time + 16f;
                while (!ShipLobby.AnyLooking && Time.time < until) yield return null;
                Check(ShipLobby.AnyLooking, "now and then the aliens on the couch turn and look at each other");
                if (ShipLobby.AnyLooking) { yield return new WaitForSeconds(0.5f); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_looking.png")); yield return null; }

                // THE TELLY: clicking it moves the camera onto its game and starts you playing
                ShipLobby.TestClickTv = true;
                yield return new WaitForSeconds(1.8f);
                Check(LobbyArcade.Focused && ShipLobby.CamToTelly < 1.4f, $"clicking the telly moves the camera onto its screen ({ShipLobby.CamToTelly:0.00} m away)");
                until = Time.time + 4f;
                while ((!me.ArcadePlaying.Value || LobbyArcade.Playing < 1 || !LobbyArcade.MyDot(out _)) && Time.time < until) yield return null;
                yield return new WaitForSeconds(0.4f);
                Check(me.ArcadePlaying.Value && LobbyArcade.Playing >= 1, $"you're playing the telly's game ({LobbyArcade.Playing} playing)");
                Check(ShipLobby.IsGaming(me), "your alien on the couch holds a controller and watches the telly");
                // the score bar along the top lists everyone playing (you too, the moment you join)
                Check(LobbyArcade.BoardShown == LobbyArcade.Playing && LobbyArcade.PlayerScore(nm.LocalClientId) >= 0,
                    $"the telly's score bar shows everyone playing ({LobbyArcade.BoardShown} on it, {LobbyArcade.Playing} playing)");
                // the camera sits back from the telly: the HUD (the title top left, the buttons along the bottom) stays off the picture
                var tv = ShipLobby.TellyOnScreen;
                Check(tv.height > Screen.height * 0.4f && tv.yMin > Screen.height * 0.13f && tv.yMax < Screen.height * 0.87f,
                    $"the telly's picture sits clear of the lobby's HUD (from {tv.yMin / Screen.height:0.00} to {tv.yMax / Screen.height:0.00} of the screen's height)");
                LobbyArcade.MyDot(out var dot0);
                int hash0 = LobbyArcade.PictureHash, draws0 = LobbyArcade.Redraws;
                LobbyArcade.TestStick = new Vector2(1f, 0f); // (D held)
                yield return new WaitForSeconds(1.2f);
                LobbyArcade.MyDot(out var dot1);
                Check(dot1.x > dot0.x + 10f, $"WASD moves your little alien across the telly ({dot0.x:0} -> {dot1.x:0})");
                Check(LobbyArcade.Redraws > draws0 + 10 && LobbyArcade.PictureHash != hash0, $"the telly's picture keeps redrawing ({LobbyArcade.Redraws - draws0} frames) and changes");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_arcade_host.png"));
                yield return null;
                int team = me.Team.Value & 3, score0 = LobbyArcade.Score(team), mine0 = LobbyArcade.PlayerScore(nm.LocalClientId);
                LobbyArcade.TestAuto = 1; // (steering itself into the nearest tree)
                until = Time.time + 10f;
                while (LobbyArcade.Score(team) < score0 + 1 && Time.time < until) yield return null;
                Check(LobbyArcade.Score(team) >= score0 + 1 && LobbyArcade.PlayerScore(nm.LocalClientId) >= mine0 + 1,
                    $"walking into a tree collects it: +1 for you and your team ({score0} -> {LobbyArcade.Score(team)}, yours {mine0} -> {LobbyArcade.PlayerScore(nm.LocalClientId)})");
                int score1 = LobbyArcade.Score(team);
                LobbyArcade.TestAuto = 2; // (to the ball, then home with it)
                until = Time.time + 20f;
                while (LobbyArcade.Score(team) < score1 + 5 && Time.time < until) yield return null;
                Check(LobbyArcade.Score(team) >= score1 + 5, $"running the ball to your machine scores +5 ({score1} -> {LobbyArcade.Score(team)})");
                yield return new WaitForSeconds(0.3f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_arcade_goal.png"));
                yield return null;
                LobbyArcade.TestAuto = 0;
                LobbyArcade.TestStick = new Vector2(9f, 9f);
                // the clients see it too (they wait for a few seconds of it), then STOP PLAYING
                yield return new WaitForSeconds(3f);
                LobbyArcade.Focus(false); // (what STOP PLAYING does)
                yield return new WaitForSeconds(2.5f);
                Check(!LobbyArcade.Focused && !me.ArcadePlaying.Value && !ShipLobby.IsGaming(me) && ShipLobby.CamToTelly > 2f,
                    $"STOP PLAYING: back to the couch view ({ShipLobby.CamToTelly:0.0} m from the telly) and the usual idle");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_arcade_stopped.png"));
                yield return null;

                // SPECTATE (the grey team option): a client gets up to watch - their alien stays on the end of the couch,
                // grey - then picking a team takes a seat on it again
                ulong other = 0;
                foreach (var p in PlayerNet.All) if (p != null && !p.Bot.Value && p.OwnerClientId != nm.LocalClientId) other = p.OwnerClientId;
                int before = PlayerNet.All.Count;
                bool off = g.ServerSetSpectating(other, true); // (what their SPECTATE option sends: NetGame.SpectateRpc)
                yield return new WaitForSeconds(1.5f);
                Check(off && PlayerNet.All.Count == before - 1 && g.IsSpectator(other), $"SPECTATE: client {other} gets up to watch ({PlayerNet.All.Count} players, {g.Spectators.Count} watching)");
                Check(ShipLobby.GreyAliens == 1 && ShipLobby.GhostsGrey && ShipLobby.Seated.Count + ShipLobby.GreyAliens == before,
                    $"the spectator's alien stays on the couch, grey ({ShipLobby.GreyAliens} grey, {ShipLobby.Seated.Count} playing)");
                Check(ShipLobby.SpectatorHeadOnScreen(other, out _), "the spectator has a name tag over their grey alien");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "lobby_one_spectating.png"));
                yield return null;
                int pick = 0, least = 99;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int n = 0;
                    foreach (var p in PlayerNet.All) if (p.Team.Value == t) n++;
                    if (n < least && n < Cfg.TeamCap(t)) { least = n; pick = t; }
                }
                bool on = g.ServerPlayOnTeam(other, pick); // (what clicking a team while spectating sends: NetGame.PlayOnTeamRpc)
                until = Time.time + 6f;
                while (PlayerNet.All.Count < before && Time.time < until) yield return null;
                yield return new WaitForSeconds(1.5f);
                PlayerNet back = null;
                foreach (var p in PlayerNet.All) if (p != null && p.OwnerClientId == other && !p.Bot.Value) back = p;
                Check(on && PlayerNet.All.Count == before && !g.IsSpectator(other) && back != null && back.Team.Value == pick && ShipLobby.GreyAliens == 0,
                    $"picking {Cfg.TeamName[pick]} while spectating: they take a seat on it again ({PlayerNet.All.Count} players, team {(back != null ? back.Team.Value : -1)}, {ShipLobby.GreyAliens} grey)");
            }
            else
            {
                // (a client: the host plays the telly's game for a bit - we see it too)
                float arcadeUntil = Time.time + 60f;
                while (LobbyArcade.Playing < 1 && Time.time < arcadeUntil) yield return null;
                if (LobbyArcade.Playing >= 1)
                {
                    yield return new WaitForSeconds(1.5f);
                    int draws = LobbyArcade.Redraws;
                    PlayerNet player = null;
                    foreach (var p in PlayerNet.All) if (p != null && p.ArcadePlaying.Value) player = p;
                    yield return new WaitForSeconds(1f);
                    Check(LobbyArcade.Playing >= 1 && LobbyArcade.Redraws > draws, $"our telly shows the game the host is playing ({LobbyArcade.Playing} playing)");
                    Check(player != null && ShipLobby.IsGaming(player), "the player on the telly holds a controller on our screen too");
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobby_arcade_client{nm.LocalClientId}.png"));
                    yield return null;
                }
                else Check(false, "our telly shows the game the host is playing (nobody seen playing)");
                // (watched for a moment by the host's SPECTATE check - the lobby with our alien grey on the couch; or
                // another client was picked: wait until they're back)
                float watchUntil = Time.time + 90f;
                bool sawWatch = false, sawOther = false;
                while (Time.time < watchUntil && !sawWatch)
                {
                    if (Spectator.Active && ShipLobby.Active) sawWatch = true;
                    else if (g.Spectators.Count > 0) sawOther = true;
                    else if (sawOther) break;
                    yield return null;
                }
                if (sawWatch)
                {
                    yield return new WaitForSeconds(0.6f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), $"lobby_spectating_client{nm.LocalClientId}.png"));
                    yield return null;
                    Check(ShipLobby.Active, "spectating, you still see the ship lobby");
                    Check(ShipLobby.GreyAliens >= 1 && ShipLobby.SpectatorHeadOnScreen(nm.LocalClientId, out _), "spectating, our own alien sits on the couch, grey");
                    until = Time.time + 15f;
                    while (PlayerNet.Local == null && Time.time < until) yield return null;
                    Check(PlayerNet.Local != null, "picking a team again: our alien's back on the couch");
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
            until = Time.time + 60f; // (the clients READY once the host's done with them)
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
