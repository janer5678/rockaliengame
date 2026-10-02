using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest arena: the sudden death platform in space. Start the host on its own (-autotest arena -host -fast
    /// -shotdir DIR, windowed for the screenshots) and a client (-autotest arena -client 127.0.0.1) about a minute later.
    /// Alone in the lobby the host checks the look (no crowd, flat colours, low-poly stars, 15% smaller), photographs the
    /// platform and space, and checks that walking off the edge
    /// just puts you back on top. When the client joins, sudden death starts (Ready? / Set / ROCK!, frozen until ROCK!), and the host walks off the edge: that's a
    /// death like a kill, so the client wins the duel.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator ArenaRoutine(PlayerNet me, PlayerController pc)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsHost) yield break; // the client just stands there (Watch quits when the game is over)
            var g = NetGame.Instance;
            var c = Cfg.ArenaCenter;
            var cam = Camera.main;
            var hud = FindAnyObjectByType<Hud>();
            Check(g.S == GameState.Waiting, "alone in the lobby: still waiting for players");
            Check(SpaceArena.OverPlatform(me.transform.position) && Mathf.Abs(me.transform.position.y - c.y) < 0.6f, $"standing on the platform in the lobby ({me.transform.position - c})");
            Check(Stadium.Instance != null && Stadium.Instance.GetComponentInChildren<TextMesh>().GetComponent<Renderer>().enabled, "the arena is drawn while you're in it");
            Check(!RenderSettings.fog, "no fog in space");
            int fans = 0, rocks = 0, textured = 0, starTris = 0;
            foreach (var t in Stadium.Instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "fan") fans++;
                if (t.name == "crowd rock") rocks++;
                if (t.name == "stars" && t.TryGetComponent<MeshFilter>(out var mf)) starTris = mf.sharedMesh.triangles.Length / 3;
            }
            foreach (var r in Stadium.Instance.GetComponentsInChildren<MeshRenderer>(true))
                if (r.GetComponent<TextMesh>() == null) // (the screens' text is a font)
                foreach (var m in r.sharedMaterials)
                    if (m != null && ((m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null && m.GetTexture("_MainTex").name != "UnityWhite"))) textured++;
            Check(fans == 0 && rocks == 0, $"no crowd any more ({fans} fans, {rocks} rocks)");
            Check(starTris >= SpaceArena.StarCount * 10, $"low-poly white stars in the sky ({SpaceArena.StarCount} stars, {starTris} triangles in one mesh)");
            Check(textured == 0, $"flat colours, no textures ({textured} textured materials)");
            Check(Mathf.Approximately(SpaceArena.HalfX, 21f * 0.85f) && Mathf.Approximately(SpaceArena.HalfZ, 27f * 0.85f), $"the platform is 15% smaller ({SpaceArena.HalfX} x {SpaceArena.HalfZ})");
            for (int team = 0; team < 4; team++)
                for (int slot = 0; slot < 4; slot++)
                {
                    NetGame.SpawnPoint(team, true, slot, out var tp, out _);
                    if (!SpaceArena.OverPlatform(tp)) Check(false, $"arena spawn {team}/{slot} is off the platform ({tp - c})");
                }

            // first person, from the spawn spot
            NetGame.SpawnPoint(me.Team.Value, true, me.Slot.Value, out var sp, out var sy);
            pc.LocalTeleport(sp, sy);
            pc.SetLook(sy, 4f);
            yield return Snap("arena_01_fp_spawn");
            pc.SetLook(sy + 70f, -4f);
            yield return Snap("arena_02_fp_stars");
            pc.SetLook(sy + 150f, -32f);
            yield return Snap("arena_03_fp_space_up");

            // outside views: the camera on its own, no hands or HUD
            pc.enabled = false;
            if (hud) hud.enabled = false;
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (Transform ch in cam.transform) if (ch.gameObject.activeSelf) { hidden.Add(ch.gameObject); ch.gameObject.SetActive(false); }
            IEnumerator View(string name, Vector3 from, Vector3 at)
            {
                cam.transform.position = c + from;
                cam.transform.LookAt(c + at);
                yield return Snap(name);
            }
            yield return View("arena_05_side", new Vector3(0f, 6f, -44f), new Vector3(0, -4f, 0));
            yield return View("arena_06_three_quarter", new Vector3(48f, 26f, -52f), new Vector3(0, -3f, 0));
            yield return View("arena_07_below", new Vector3(26f, -30f, -44f), new Vector3(0, -6f, 0));
            yield return View("arena_04_edge", new Vector3(9f, 1.5f, -33f), new Vector3(0f, -5f, -18f));
            yield return View("arena_08_stars", new Vector3(0f, 3f, 0f), new Vector3(60f, 45f, 120f));
            yield return View("arena_09_top", new Vector3(0f, 70f, -10f), new Vector3(0, 0, 0));
            foreach (var h in hidden) h.SetActive(true);
            if (hud) hud.enabled = true;
            pc.enabled = true;

            // the lobby: walking off the edge just puts you back on the platform
            pc.LocalTeleport(c + new Vector3(SpaceArena.HalfX + 2.5f, 0.3f, 0), 90f);
            pc.SetLook(270f, 20f);
            yield return new WaitForSeconds(0.6f);
            Check(me.transform.position.y < c.y - 1f, $"stepped off the edge and fell (y {me.transform.position.y - c.y:0.0})");
            yield return Snap("arena_10_falling_lobby");
            float until = Time.time + 6f;
            while (Time.time < until && !(SpaceArena.OverPlatform(me.transform.position) && me.transform.position.y > c.y - 1f)) yield return null;
            Check(SpaceArena.OverPlatform(me.transform.position) && me.transform.position.y > c.y - 1f && !me.Dead.Value && g.S == GameState.Waiting,
                  $"lobby: falling into space puts you back on the platform, alive ({me.transform.position - c})");

            // sudden death as soon as the opponent is in
            Log("waiting for the client to join");
            while (g.S == GameState.Waiting) yield return null;
            PlayerNet other = null;
            foreach (var p in PlayerNet.All) if (p != me) other = p;
            Check(other != null, "opponent joined");
            yield return new WaitForSeconds(1f);
            g.DevStartSuddenDeath();
            while (g.S != GameState.SuddenDeath) yield return null;
            yield return new WaitForSeconds(1.5f);
            Check(SpaceArena.OverPlatform(me.transform.position) && SpaceArena.OverPlatform(other.transform.position), "both players teleported onto the platform for sudden death");
            NetGame.SpawnPoint(me.Team.Value, true, me.Slot.Value, out sp, out sy);
            pc.SetLook(sy, 2f);
            // Ready? / Set / ROCK! - and nobody moves until ROCK!
            while (Hud.FightWord(g, out _) != "Ready?" && g.FightFrozen) yield return null;
            var frozenAt = me.transform.position;
            Binds.TestHold(Bind.Forward, true);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "arena_11_sd_ready.png"));
            Log("shot arena_11_sd_ready");
            while (Hud.FightWord(g, out _) == "Ready?") yield return null;
            Check(Hud.FightWord(g, out _) == "Set" && g.FightFrozen, $"Ready? then Set ({Hud.FightWord(g, out _)})");
            yield return new WaitForSeconds(0.3f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "arena_11_sd_set.png"));
            Log("shot arena_11_sd_set");
            float moved = 0f;
            while (g.FightFrozen)
            {
                moved = Mathf.Max(moved, Vector3.Distance(new Vector3(frozenAt.x, 0, frozenAt.z), new Vector3(me.transform.position.x, 0, me.transform.position.z)));
                yield return null;
            }
            Check(moved < 0.2f, $"holding W during Ready? / Set doesn't move you ({moved:0.00} m)");
            Check(Hud.FightWord(g, out _) == "ROCK!", $"then ROCK! ({Hud.FightWord(g, out _)})");
            yield return new WaitForSeconds(0.15f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "arena_11_sd_rock.png"));
            Log("shot arena_11_sd_rock");
            yield return new WaitForSeconds(0.6f);
            Binds.TestReleaseAll();
            float moved2 = Vector3.Distance(new Vector3(frozenAt.x, 0, frozenAt.z), new Vector3(me.transform.position.x, 0, me.transform.position.z));
            Check(moved2 > 1f, $"after ROCK! you can move ({moved2:0.0} m)");
            pc.LocalTeleport(sp, sy);
            yield return new WaitForSeconds(2.5f);
            yield return Snap("arena_12_sd_duel");
            Check(g.S == GameState.SuddenDeath && !me.Dead.Value && !other.Dead.Value, "nobody died before anyone fell");

            // walk off the end of the platform: lost in space, and the opponent wins
            pc.LocalTeleport(c + new Vector3(0, 0.3f, -SpaceArena.HalfZ - 2.5f), 180f);
            pc.SetLook(0f, 25f);
            until = Time.time + 5f;
            while (Time.time < until && !me.Dead.Value) yield return null;
            Check(me.Dead.Value, $"sudden death: falling into space kills you (died at y {me.transform.position.y - c.y:0.0})");
            yield return Snap("arena_13_sd_fell");
            until = Time.time + 3f;
            while (Time.time < until && g.S != GameState.GameOver) yield return null;
            Check(g.S == GameState.GameOver && g.Winner.Value == other.Team.Value, $"the one who didn't fall wins the duel (winner {g.Winner.Value}, reason \"{g.EndReason.Value}\")");
            Log("arena done");
        }
    }
}
