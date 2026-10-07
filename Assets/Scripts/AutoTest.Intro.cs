using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest intro -host -solo: the match intro (MatchIntro.cs) played on demand (MatchIntro.TestPlay): the menu's
        /// UFO in space, its crash onto the planet (entry, dive, impact), then the slow push in over the map's middle with the
        /// map's name up top and the game mode in two lines below (no markers over the ball) - a screenshot of each - and
        /// then the camera's handed back to the player's eyes and the controls come back. Then once more, skipped with jump.
        /// </summary>
        IEnumerator IntroRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            if (g != null && g.IsServer) g.TimerPaused.Value = true; // (the glass stays up: the ball sits under its dome)
            yield return new WaitForSeconds(0.5f);
            var cam = Camera.main;
            var before = cam.transform.position;
            Check(!MatchIntro.Active, "no intro in the tests by themselves (solo / tests skip it)");
            int plays = MatchIntro.Plays;
            MatchIntro.TestPlay();
            yield return null;
            yield return null;
            Check(MatchIntro.Active && MatchIntro.Plays == plays + 1, "the intro starts");

            // 1. space: the main menu's UFO
            while (MatchIntro.Elapsed < 0.55f) yield return null;
            Check(MatchIntro.Now == MatchIntro.Scene.Space && MenuSpace.Showing && !MenuSpace.CrashUp, "scene 1: the main menu's UFO in space");
            Check(Vector3.Distance(cam.transform.position, Vector3.zero) > 3000f, $"...the camera's out in space, far from the map ({cam.transform.position})");
            Check(cam.clearFlags == CameraClearFlags.SolidColor, "...cleared to space");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_1_space.png"));
            while (MatchIntro.Elapsed < 1.3f) yield return null;
            Check(MenuSpace.IntroShot == 1, "...cutting to the fly-by");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_1b_flyby.png"));

            // 2. the crash
            while (MatchIntro.Elapsed < MatchIntro.SpaceEnd + 0.55f) yield return null;
            Check(MatchIntro.Now == MatchIntro.Scene.Crash && MenuSpace.CrashUp && MenuSpace.IntroShot == 2, "scene 2: the UFO streaking into the atmosphere");
            Check(Vector3.Distance(cam.transform.position, Vector3.zero) > 3000f, "...far from the map");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_2_entry.png"));
            while (MatchIntro.Elapsed < MatchIntro.SpaceEnd + 1.75f) yield return null;
            Check(MenuSpace.IntroShot == 3, "...the dive down through the clouds");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_2_dive.png"));
            while (MatchIntro.Elapsed < MatchIntro.ImpactAt - 0.25f) yield return null;
            Check(MenuSpace.IntroShot == 4 && !MenuSpace.CrashExploded, "...from the ground as it comes screaming in");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_2_incoming.png"));
            while (MatchIntro.Elapsed < MatchIntro.ImpactAt + 0.2f) yield return null;
            Check(MenuSpace.CrashExploded, "...it slams into the ground (the explosion's out)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_2_impact.png"));
            while (MatchIntro.Elapsed < MatchIntro.ImpactAt + 0.6f) yield return null;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_2_fireball.png"));

            // 3. the crash site and the ball
            while (MatchIntro.Elapsed < MatchIntro.CrashEnd + 0.4f) yield return null;
            Check(MatchIntro.Now == MatchIntro.Scene.Map && !MenuSpace.Showing, "scene 3: the crash site on the map (the space scenes put away)");
            float d = Ball.Instance != null ? Vector3.Distance(cam.transform.position, Ball.Instance.transform.position) : 999f;
            Check(d < 80f, $"...the camera's over the middle of the map ({d:0} m from the ball)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_3_site.png"));
            // slow and cinematic: a gentle push in, no swoop
            var p0 = cam.transform.position;
            yield return new WaitForSeconds(0.5f);
            float moved = Vector3.Distance(p0, cam.transform.position);
            Check(MatchIntro.MapShot >= 4.5f && moved < 4f, $"...a slow push in ({moved:0.0} m in half a second; the shot lasts {MatchIntro.MapShot:0.0} s)");
            while (MatchIntro.Elapsed < MatchIntro.Length - 0.8f) yield return null;
            d = Ball.Instance != null ? Vector3.Distance(cam.transform.position, Ball.Instance.transform.position) : 999f;
            Check(d < 25f, $"...pushed in close on the ball ({d:0.0} m)");
            var bsp = Ball.Instance != null ? cam.WorldToViewportPoint(Ball.Instance.transform.position) : Vector3.back;
            Check(bsp.z > 0f && bsp.x > 0.1f && bsp.x < 0.9f && bsp.y > 0.1f && bsp.y < 0.9f, $"...the ball's in the shot ({bsp})");
            Check(Time.unscaledTime - MatchIntro.HudShownAt < 0.5f, "...and the intro's bars (HUD)");
            MatchIntro.GoalLines(out var want1, out var want2);
            Check(Time.unscaledTime - MatchIntro.CaptionShownAt < 0.5f && MatchIntro.ShownLine1 == want1 && MatchIntro.ShownLine2 == want2,
                $"...the game mode explained in two lines below: \"{MatchIntro.ShownLine1}\" / \"{MatchIntro.ShownLine2}\"");
            if (!Cfg.Builder && !Cfg.NoBall && !Cfg.ThreeGoal && !Cfg.ProgressMode)
                Check(want1 == "GET THE BALL INTO YOUR MACHINE!" && want2 == "HAVE IT IN YOUR BASE WHEN TIMER ENDS TO WIN.", "...for the ball modes: GET THE BALL INTO YOUR MACHINE! / HAVE IT IN YOUR BASE WHEN TIMER ENDS TO WIN.");
            Check(MatchIntro.ShownTop == MatchIntro.MapName && MatchIntro.ShownTop != "THE CRASH SITE", $"...and the map's name up top (\"{MatchIntro.ShownTop}\"), not THE CRASH SITE");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_3_goal.png"));

            // the end: the camera's back on the player
            while (MatchIntro.Active) yield return null;
            yield return new WaitForSeconds(0.6f);
            float eye = Vector3.Distance(cam.transform.position, me.transform.position);
            Check(!MatchIntro.Active && eye < 3f, $"it ends and the camera's back in the player's eyes ({eye:0.0} m from them)");
            Check(!MenuSpace.Showing && !MenuSpace.CrashUp, $"...the space scenes are put away (camera clears {cam.clearFlags})");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "intro_4_back.png"));
            Log($"camera before {before}, after {cam.transform.position}");

            // once more, skipped
            MatchIntro.TestPlay();
            yield return new WaitForSeconds(1.2f);
            Check(MatchIntro.Active, "played again");
            MatchIntro.Skip();
            yield return new WaitForSeconds(0.5f);
            Check(!MatchIntro.Active, "...and skipped (jump): over at once");
            yield return new WaitForSeconds(0.5f);
            eye = Vector3.Distance(cam.transform.position, me.transform.position);
            Check(eye < 3f && !MenuSpace.Showing, $"...the camera's back on the player ({eye:0.0} m)");
            Log("intro test done");
            yield return new WaitForSeconds(0.3f);
            Application.Quit(0);
        }
    }
}
