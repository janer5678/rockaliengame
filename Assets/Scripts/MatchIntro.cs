using System;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The match intro: once everyone's READY in the ship lobby and the match begins (the state leaves Waiting in a lobby
    /// game - NetGame.ReadyLobby; not the tutorial, not the tests unless TestsToo or TestPlay), every screen plays a
    /// ~10 s cutscene, in letterbox bars with hard cuts and flashes:
    /// 1. SPACE (0 .. SpaceEnd): the main menu's UFO tearing through space (MenuSpace's own scene and shots).
    /// 2. CRASH (SpaceEnd .. CrashEnd): the same UFO streaking down into a planet's atmosphere in a sheath of fire, through
    ///    the clouds, and slamming into the ground - flash, fireball, dust ring, debris (MenuSpace.Crash.cs).
    /// 3. THE MAP (CrashEnd .. Length): a slow, cinematic push in and drift round over the real map's middle - the wreck
    ///    and the ball under its dome - with the map's name in orange in the top bar and the game mode explained in two
    ///    lines of the same size in the bottom bar (GoalLines: GET THE BALL INTO YOUR MACHINE! / HAVE IT IN YOUR BASE WHEN
    ///    TIMER ENDS TO WIN. for the ball modes; each other mode its own goal). No markers over the ball.
    /// Then the camera's handed back (a quick dip through black), this screen tells the host it's done
    /// (NetGame.IntroDoneRpc), and once every screen has, everyone gets READY / SET / ROCK! (NetGame.ReadySetRock.cs).
    /// Client side only (each screen starts it when it sees the match begin). While it plays nobody can move, look, fight
    /// or open menus (PlayerController: like the victory cutscene), the camera is the intro's (CameraPose) and the HUD is
    /// the intro's (DrawHud). Jump (Space) skips it. The scenes in space use the main menu cutscene's own post
    /// processing (MenuLook: GameSettings.MenuPostNow).
    /// </summary>
    public static class MatchIntro
    {
        /// <summary>The timeline (seconds): space until SpaceEnd, the crash until CrashEnd (it hits the ground at ImpactAt),
        /// the map shot (slow: MapShot seconds) until Length; then Outro seconds of fading back in on the game.</summary>
        public const float SpaceEnd = 1.6f, CrashLength = 3.7f, CrashEnd = SpaceEnd + CrashLength, MapShot = 5.2f, Length = CrashEnd + MapShot, Outro = 0.35f;
        public const float ImpactAt = SpaceEnd + MenuSpace.CrashImpact;
        /// <summary>When the captions come up (seconds into the map shot).</summary>
        public const float CaptionAt = 0.5f;

        public enum Scene { None, Space, Crash, Map }

        static float s_At = -1f;
        static NetGame s_Game;
        static GameState s_Last;
        static bool s_Ended, s_MapPlanned, s_Reported;
        static float s_Yaw0, s_Yaw1;
        static Vector3 s_Ball, s_Wreck;
        static readonly HashSet<int> s_Cues = new HashSet<int>();

        /// <summary>Play it in the tests too (they skip it otherwise).</summary>
        public static bool TestsToo;
        /// <summary>Test hooks: how many times it's started; when the HUD last drew it and its captions, and what they said
        /// (the top bar's map name, the bottom bar's two lines).</summary>
        public static int Plays { get; private set; }
        public static float HudShownAt { get; private set; } = -1f;
        public static float CaptionShownAt { get; private set; } = -1f;
        public static string ShownTop { get; private set; } = "";
        public static string ShownLine1 { get; private set; } = "";
        public static string ShownLine2 { get; private set; } = "";

        /// <summary>Seconds into it (-1: not playing).</summary>
        public static float Elapsed => s_At < 0f ? -1f : Time.unscaledTime - s_At;
        /// <summary>It's playing: the camera, the HUD and the controls are its.</summary>
        public static bool Active => s_At >= 0f && Elapsed < Length;
        /// <summary>Which scene's on.</summary>
        public static Scene Now => !Active ? Scene.None : Elapsed < SpaceEnd ? Scene.Space : Elapsed < CrashEnd ? Scene.Crash : Scene.Map;
        /// <summary>The space / crash scenes want MenuSpace up.</summary>
        public static bool SpaceUp => Active && Elapsed < CrashEnd;
        /// <summary>The main menu cutscene's own post processing is used for it (GameSettings.MenuPostNow).</summary>
        public static bool MenuLook => Active;

        /// <summary>Every frame (MenuSpace.Tick, from Bootstrap): start it when the match begins, run its cues, end it.</summary>
        public static void Tick()
        {
            var g = NetGame.Instance;
            if (g == null || !g.IsSpawned) { s_Game = null; s_At = -1f; return; }
            if (g != s_Game) { s_Game = g; s_Last = g.S; s_Reported = false; } // (joined mid-match: no intro)
            if (g.S != s_Last)
            {
                if (s_Last == GameState.Waiting && g.S != GameState.GameOver)
                {
                    s_Reported = false;
                    if (Wanted()) Begin();
                    else Report(g); // (no intro on this screen: ready for READY / SET / ROCK! at once)
                }
                s_Last = g.S;
            }
            if (s_At < 0f) return;
            float e = Elapsed;
            if (e >= Length && !s_Ended) { s_Ended = true; Report(g); AfterBanner(g); }
            if (e >= Length + Outro) { s_At = -1f; return; }
            if (e > 0.6f && e < Length - 0.3f && !Chat.Open && Input.GetKeyDown(KeyCode.Space)) Skip();
            Cues(e);
        }

        static bool Wanted() => NetGame.ReadyLobby && !Cfg.Tutorial && (!Bootstrap.Testing || TestsToo) && (PlayerNet.Local != null || Spectator.Active);

        /// <summary>This screen's intro is over (or it had none): the host starts READY / SET / ROCK! once every screen has
        /// said so (NetGame.ReadySetRock.cs).</summary>
        static void Report(NetGame g)
        {
            if (s_Reported || g == null || !g.IsSpawned) return;
            s_Reported = true;
            g.IntroDoneRpc();
        }

        static void Begin()
        {
            s_At = Time.unscaledTime;
            s_Ended = false;
            s_MapPlanned = false;
            s_Cues.Clear();
            Plays++;
        }

        /// <summary>(tests) play it now, whatever the state.</summary>
        public static void TestPlay()
        {
            var g = NetGame.Instance;
            if (g != null && g.IsSpawned) { s_Game = g; s_Last = g.S; }
            s_Reported = false; // (it tells the host when it's done, like a real one)
            Begin();
        }

        /// <summary>Skip to the end (jump pressed; tests).</summary>
        public static void Skip()
        {
            if (Active) s_At = Time.unscaledTime - (Length - 0.25f);
        }

        /// <summary>Back in the game: the build phase's banner again (it came up under the intro). With READY / SET / ROCK!
        /// to come it waits for ROCK! instead (NetGame.TickReadySetRock shows it then).</summary>
        static void AfterBanner(NetGame g)
        {
            if (g == null || !g.IsSpawned || g.RockAt.Value > 0) return;
            ShowBuildBanner(g);
        }

        /// <summary>The build phase's banner: what to do, and how long until the walls drop.</summary>
        public static void ShowBuildBanner(NetGame g)
        {
            if (g == null || !g.IsSpawned || Cfg.FunRules || Cfg.Tutorial || g.S != GameState.PreBall) return;
            int s = Mathf.CeilToInt(g.TimeLeft);
            Hud.Banner("GATHER & BUILD", $"The walls drop in {s / 60}:{s % 60:00} - gather and build, then get the ball into your machine!");
        }

        static bool Cue(int id, float at, float e) => e >= at && e < at + 0.4f && s_Cues.Add(id);

        static void Play(AudioClip c, float vol) { if (c != null) Sfx.Play2D(c, vol, 0.04f); }

        static void Cues(float e)
        {
            if (Cue(0, 0f, e)) { Play(Sfx.Whiz, 0.55f); Play(Sfx.Engine, 0.35f); }
            if (Cue(1, 0.95f, e)) Play(Sfx.Whiz, 0.6f);
            if (Cue(2, SpaceEnd, e)) { Play(Sfx.Rocket, 0.75f); Play(Sfx.Hiss, 0.5f); }
            if (Cue(3, SpaceEnd + MenuSpace.CrashShotB, e)) Play(Sfx.Whiz, 0.7f);
            if (Cue(4, SpaceEnd + MenuSpace.CrashShotC, e)) Play(Sfx.Jet, 0.6f);
            if (Cue(5, ImpactAt, e)) { Play(Sfx.BigBoom, 1f); Play(Sfx.Boom, 0.8f); Play(Sfx.Smash, 0.5f); }
            if (Cue(6, ImpactAt + 0.35f, e)) Play(Sfx.FarBoom, 0.6f);
            if (Cue(7, CrashEnd, e)) Play(Sfx.Whiz, 0.3f);
            if (Cue(8, CrashEnd + CaptionAt, e)) Play(Sfx.Ding, 0.45f);
        }

        // ------------------------------------------------------------------ the camera

        /// <summary>The intro's camera this frame (PlayerController.LateUpdate / Spectator.LateUpdate use it instead of
        /// their own); false when it isn't playing.</summary>
        public static bool CameraPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = default;
            rot = Quaternion.identity;
            fov = 60f;
            if (!Active) return false;
            float e = Elapsed;
            if (e < CrashEnd && MenuSpace.IntroPoseOk)
            {
                pos = MenuSpace.IntroPos; rot = MenuSpace.IntroRot; fov = MenuSpace.IntroFov;
                return true;
            }
            MapPose(e - CrashEnd, out pos, out rot, out fov);
            return true;
        }

        /// <summary>Test hooks: where the crash site shot looks from and at.</summary>
        public static Vector3 BallSpot => s_Ball;

        static void PlanMap()
        {
            s_MapPlanned = true;
            s_Ball = Ball.Instance != null ? Ball.Instance.transform.position : new Vector3(0f, MapBuilder.GroundHeight(0f, 0f) + 1f, 0f);
            s_Wreck = CrashSite.Current != null ? CrashSite.Current.SaucerCentre : s_Ball + CrashSite.Dir * CrashSite.SaucerDist;
            // from the far side of the ball from the wreck, swung round so the wreck sits beside the ball (not behind it),
            // on whichever side the ground's lower (a clear look)
            var away = -CrashSite.Dir;
            float best = float.MaxValue;
            for (int side = -1; side <= 1; side += 2)
            {
                var end = Quaternion.Euler(0f, 32f * side, 0f) * away;
                float hi = 0f;
                for (int i = 0; i <= 6; i++)
                {
                    var p = s_Ball + Quaternion.Euler(0f, (32f + i * 8f) * side, 0f) * away * Mathf.Lerp(13f, 50f, i / 6f);
                    hi = Mathf.Max(hi, MapBuilder.GroundHeight(p.x, p.z));
                }
                if (hi < best)
                {
                    best = hi;
                    s_Yaw1 = Quaternion.LookRotation(end).eulerAngles.y;
                    s_Yaw0 = s_Yaw1 + 26f * side; // (a slow drift round, not a swoop)
                }
            }
        }


        /// <summary>The map shot m seconds in: a slow, cinematic push in - from a wide look over the middle of the map,
        /// easing down and in and drifting gently round to low and close by the ball's dome, the wreck beside it - eased
        /// at both ends, no jolts or whips.</summary>
        static void MapPose(float m, out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (!s_MapPlanned) PlanMap();
            if (Ball.Instance != null) s_Ball = Ball.Instance.transform.position;
            float u = Mathf.Clamp01(m / MapShot);
            float e = Mathf.SmoothStep(0f, 1f, u) * 0.75f + u * 0.25f; // (gentle at both ends, never quite still)
            float yaw = Mathf.LerpAngle(s_Yaw0, s_Yaw1, e);
            float r = Mathf.Lerp(36f, 15f, e), h = Mathf.Lerp(13f, 3.6f, e);
            pos = s_Ball + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * r;
            pos.y = Mathf.Max(s_Ball.y + h, MapBuilder.GroundHeight(pos.x, pos.z) + 2.2f);
            pos += new Vector3(Mathf.Sin(m * 0.7f) * 0.06f, Mathf.Sin(m * 0.9f + 1f) * 0.05f, 0f); // (a slow hand-held sway)
            var mid = Vector3.Lerp(s_Ball, s_Wreck, 0.4f) + Vector3.up * 2.5f;
            var look = Vector3.Lerp(mid, s_Ball + (s_Wreck - s_Ball) * 0.2f + Vector3.up * 1.4f, e);
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
            fov = Mathf.Lerp(54f, 44f, e);
        }

        // ------------------------------------------------------------------ the HUD

        static float S01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        /// <summary>The game mode in two short lines of the same size, as the intro's bottom bar says it: the ball modes
        /// GET THE BALL INTO YOUR MACHINE! / HAVE IT IN YOUR BASE WHEN TIMER ENDS TO WIN.; every other mode its own goal.</summary>
        public static void GoalLines(out string line1, out string line2)
        {
            if (Cfg.Builder) { line1 = "GRAB THE BALL AND PLANT IT!"; line2 = "HAVE IT PLANTED WHEN TIMER ENDS TO WIN."; return; }
            if (Cfg.Bedwars) { line1 = "SMASH THE ENEMY MACHINES!"; line2 = "LAST TEAM STANDING WINS."; return; }
            if (Cfg.Assassin) { line1 = "KILL THE ENEMIES AND TAKE THEIR SKULLS!"; line2 = "HAND IN A SKULL OF EVERY ENEMY TO WIN."; return; }
            line1 = "GET THE BALL INTO YOUR MACHINE!";
            if (Cfg.ThreeGoal) { line2 = $"FIRST TO {Cfg.GoalsToWin} GOALS WINS."; return; }
            if (Cfg.ProgressMode) { line2 = "KEEP IT THERE TO FILL YOUR BAR AND WIN."; return; }
            line2 = "HAVE IT IN YOUR BASE WHEN TIMER ENDS TO WIN.";
        }

        /// <summary>The map's name, as the intro's top bar shows it.</summary>
        public static string MapName => ThemeMaps.Label(Cfg.Map).ToUpperInvariant(); // THEME MAPS

        /// <summary>The intro's HUD (Hud.DrawGame / DrawSpectator call it first): letterbox bars, the flashes and cuts and
        /// the captions. True while it's playing (draw nothing else); in the moment after (Outro) it draws the fade back in
        /// and returns false (the HUD goes on over it).</summary>
        public static bool DrawHud(float k, GUIStyle big, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            if (s_At < 0f) return false;
            float e = Elapsed, sw = Screen.width, sh = Screen.height;
            var full = new Rect(0, 0, sw, sh);
            if (!Active)
            {
                float back = 1f - Mathf.Clamp01((e - Length) / Outro);
                if (back > 0f) fill(full, new Color(0, 0, 0, back));
                return false;
            }
            HudShownAt = Time.unscaledTime;
            float m = e - CrashEnd;

            // the flashes: in from black, the cut to the crash, the white-hot impact, the cut to the map
            float black = 1f - Mathf.Clamp01(e / 0.25f);
            if (e > Length - 0.3f) black = Mathf.Max(black, Mathf.Clamp01((e - (Length - 0.3f)) / 0.28f));
            float white = 0f;
            if (e >= SpaceEnd && e < SpaceEnd + 0.2f) white = 0.85f * (1f - (e - SpaceEnd) / 0.2f);
            if (e >= ImpactAt && e < ImpactAt + 0.65f) { float f = 1f - (e - ImpactAt) / 0.65f; white = Mathf.Max(white, f * f); }
            if (e >= CrashEnd - 0.14f && e < CrashEnd) white = Mathf.Max(white, (e - (CrashEnd - 0.14f)) / 0.14f);
            if (e >= CrashEnd && e < CrashEnd + 0.45f) white = Mathf.Max(white, 1f - (e - CrashEnd) / 0.45f); // (a softer cut into the slow map shot)
            if (white > 0f) fill(full, new Color(1f, 0.96f, 0.88f, Mathf.Clamp01(white)));
            if (black > 0f) fill(full, new Color(0f, 0f, 0f, Mathf.Clamp01(black)));

            // the bars: in fast, out just before the end
            float bars = S01(e / 0.3f) * (1f - S01((e - (Length - 0.3f)) / 0.3f)) * sh * 0.11f;
            fill(new Rect(0, 0, sw, bars), Color.black);
            fill(new Rect(0, sh - bars, sw, bars), Color.black);

            // the skip hint, small in the bottom bar's corner
            if (e < Length - 0.5f && bars > 10f)
            {
                var hint = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
                hint.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(new Rect(0, sh - bars, sw - 18f * k, bars), "SPACE  skip", hint);
            }

            // the map: its name in orange up top, and the game mode explained in two lines of the same size below
            if (m >= 0f && bars > 10f)
            {
                float fadeOut = 1f - S01((e - (Length - 0.4f)) / 0.3f);
                float pop = S01((m - CaptionAt) / 0.45f);
                GoalLines(out var line1, out var line2);
                if (pop > 0f)
                {
                    float a = pop * fadeOut;
                    // (both lines one size: as big as the bar fits two of)
                    int size = Mathf.RoundToInt(Mathf.Min(big.fontSize * 0.72f, bars * 0.34f));
                    var bs = new GUIStyle(big) { fontSize = Mathf.Max(8, size), alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = false, clipping = TextClipping.Overflow };
                    bs.normal.textColor = new Color(1f, 1f, 1f, a);
                    var me = PlayerNet.Local;
                    var tc = me != null ? Cfg.TeamColor[Mathf.Clamp(me.Team.Value, 0, 3)] : new Color(1f, 0.82f, 0.29f);
                    string col = ColorUtility.ToHtmlStringRGBA(new Color(Mathf.Lerp(tc.r, 1f, 0.2f), Mathf.Lerp(tc.g, 1f, 0.2f), Mathf.Lerp(tc.b, 1f, 0.2f), a));
                    // (the first line's last words in our team's colour: YOUR MACHINE!)
                    string l1 = line1;
                    int cut = line1.LastIndexOf("YOUR ", StringComparison.Ordinal);
                    if (cut >= 0) l1 = line1.Substring(0, cut) + $"<color=#{col}>{line1.Substring(cut)}</color>";
                    float lh = bars * 0.42f, slide = (1f - pop) * 6f * k;
                    shadowed(new Rect(0, sh - bars + bars * 0.08f + slide, sw, lh), l1, bs);
                    shadowed(new Rect(0, sh - bars + bars * 0.08f + lh + slide, sw, lh), line2, bs);
                    CaptionShownAt = Time.unscaledTime;
                    ShownLine1 = line1;
                    ShownLine2 = line2;
                }
                // the top bar: the map's name
                var top = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                top.normal.textColor = new Color(1f, 0.82f, 0.29f, S01(m / 0.5f) * fadeOut);
                ShownTop = MapName;
                GUI.Label(new Rect(0, 0, sw, bars), ShownTop, top);
            }
            return true;
        }
    }
}
