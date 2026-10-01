using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest arena: the sudden death platform in space. Start the host on its own (-autotest arena -host -fast
    /// -shotdir DIR, windowed for the screenshots) and a client (-autotest arena -client 127.0.0.1) about a minute later.
    /// Alone in the lobby the host photographs the platform, the crowd and space, and checks that walking off the edge
    /// just puts you back on top. When the client joins, sudden death starts, and the host walks off the edge: that's a
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
            int fans = 0;
            foreach (var t in Stadium.Instance.GetComponentsInChildren<Transform>()) if (t.name == "fan") fans++;
            Check(fans >= 40 && fans <= 100, $"a crowd of {fans} aliens on the floating rocks");

            // first person, from the spawn spot
            NetGame.SpawnPoint(me.Team.Value, true, me.Slot.Value, out var sp, out var sy);
            pc.LocalTeleport(sp, sy);
            pc.SetLook(sy, 4f);
            yield return Snap("arena_01_fp_spawn");
            pc.SetLook(sy + 70f, -4f);
            yield return Snap("arena_02_fp_crowd");
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
            foreach (Transform rock in Stadium.Instance.transform)
                if (rock.name == "crowd rock" && rock.localPosition.y > -3f && rock.localPosition.y < 4f)
                {
                    var lp = rock.localPosition;
                    var toC = -new Vector3(lp.x, 0, lp.z).normalized;
                    yield return View("arena_08_crowd_close", lp + toC * 11f + Vector3.up * 3f + Vector3.Cross(Vector3.up, toC) * 4f, lp + Vector3.up * 1.2f);
                    break;
                }
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
            yield return Snap("arena_11_sd_countdown");
            while (g.FightFrozen) yield return null;
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
