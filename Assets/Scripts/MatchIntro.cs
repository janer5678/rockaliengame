using System;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The match intro: once everyone's READY in the ship lobby and the match begins (the state leaves Waiting in a lobby
    /// game - NetGame.ReadyLobby; not the tutorial, not the tests unless TestsToo or TestPlay), every screen plays a fast
    /// ~9 s cutscene, in letterbox bars with hard cuts and flashes:
    /// 1. SPACE (0 .. SpaceEnd): the main menu's UFO tearing through space (MenuSpace's own scene and shots).
    /// 2. CRASH (SpaceEnd .. CrashEnd): the same UFO streaking down into a planet's atmosphere in a sheath of fire, through
    ///    the clouds, and slamming into the ground - flash, fireball, dust ring, debris (MenuSpace.Crash.cs).
    /// 3. THE CRASH SITE (CrashEnd .. Length): a swooping push in and orbit over the real map's middle - the wreck and the
    ///    ball under its dome - with the ball's goal marker (the EMERGENCY FLARE card and gold diamond you see by the glass:
    ///    FlareTip) popping up on the ball, lock-on brackets round it, and GET THE BALL INTO YOUR MACHINE! in the bottom bar.
    /// Then the camera's handed back (a quick dip through black) and the GATHER & BUILD banner comes up again.
    /// Client side only (each screen starts it when it sees the match begin). While it plays nobody can move, look, fight
    /// or open menus (PlayerController: like the victory cutscene), the camera is the intro's (CameraPose) and the HUD is
    /// the intro's (DrawHud). Jump (Space) skips it. The scenes in space use the main menu cutscene's own post
    /// processing (MenuLook: GameSettings.MenuPostNow).
    /// </summary>
    public static class MatchIntro
    {
        /// <summary>The timeline (seconds): space until SpaceEnd, the crash until CrashEnd (it hits the ground at ImpactAt),
        /// the crash site until Length; then Outro seconds of fading back in on the game.</summary>
        public const float SpaceEnd = 1.6f, CrashLength = 3.7f, CrashEnd = SpaceEnd + CrashLength, Length = 9.0f, Outro = 0.35f;
        public const float ImpactAt = SpaceEnd + MenuSpace.CrashImpact;
        /// <summary>When the goal marker pops up on the ball (seconds into the crash site shot).</summary>
        public const float CardAt = 0.45f;

        public enum Scene { None, Space, Crash, Map }

        static float s_At = -1f;
        static NetGame s_Game;
        static GameState s_Last;
        static bool s_Ended, s_MapPlanned;
        static float s_Yaw0, s_Yaw1;
        static Vector3 s_Ball, s_Wreck;
        static readonly HashSet<int> s_Cues = new HashSet<int>();

        /// <summary>Play it in the tests too (they skip it otherwise).</summary>
        public static bool TestsToo;
        /// <summary>Test hooks: how many times it's started; when the HUD last drew it and the goal marker on the ball.</summary>
        public static int Plays { get; private set; }
        public static float HudShownAt { get; private set; } = -1f;
        public static float CardShownAt { get; private set; } = -1f;

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
            if (g != s_Game) { s_Game = g; s_Last = g.S; } // (joined mid-match: no intro)
            if (g.S != s_Last)
            {
                if (s_Last == GameState.Waiting && g.S != GameState.GameOver && Wanted()) Begin();
                s_Last = g.S;
            }
            if (s_At < 0f) return;
            float e = Elapsed;
            if (e >= Length && !s_Ended) { s_Ended = true; AfterBanner(g); }
            if (e >= Length + Outro) { s_At = -1f; return; }
            if (e > 0.6f && e < Length - 0.3f && !Chat.Open && Input.GetKeyDown(KeyCode.Space)) Skip();
            Cues(e);
        }

        static bool Wanted() => NetGame.ReadyLobby && !Cfg.Tutorial && (!Bootstrap.Testing || TestsToo) && (PlayerNet.Local != null || Spectator.Active);

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
            Begin();
        }

        /// <summary>Skip to the end (jump pressed; tests).</summary>
        public static void Skip()
        {
            if (Active) s_At = Time.unscaledTime - (Length - 0.25f);
        }

        /// <summary>Back in the game: the build phase's banner again (it came up under the intro).</summary>
        static void AfterBanner(NetGame g)
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
            if (Cue(7, CrashEnd, e)) Play(Sfx.Whiz, 0.4f);
            if (Cue(8, CrashEnd + CardAt, e)) { Play(Sfx.Ding, 0.6f); Play(Sfx.Pop, 0.5f); }
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
                    s_Yaw0 = s_Yaw1 + 48f * side;
                }
            }
        }

        static float Ease(float u) { u = Mathf.Clamp01(u); return 1f - (1f - u) * (1f - u) * (1f - u); }

        /// <summary>The crash site shot m seconds in: swooping down and in from high up and round to low and close by the
        /// ball's dome, the wreck beside it.</summary>
        static void MapPose(float m, out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (!s_MapPlanned) PlanMap();
            if (Ball.Instance != null) s_Ball = Ball.Instance.transform.position;
            float u = Mathf.Clamp01(m / (Length - CrashEnd));
            float e = Ease(u * 1.15f);
            float yaw = Mathf.LerpAngle(s_Yaw0, s_Yaw1, e) + u * 6f; // (and a slow drift on, to the end)
            float r = Mathf.Lerp(52f, 13.5f, e), h = Mathf.Lerp(26f, 3.4f, e);
            pos = s_Ball + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * r;
            pos.y = Mathf.Max(s_Ball.y + h, MapBuilder.GroundHeight(pos.x, pos.z) + 2.2f);
            pos += new Vector3(Mathf.Sin(m * 1.3f) * 0.08f, Mathf.Sin(m * 1.7f + 1f) * 0.06f, 0f); // (a hand-held sway)
            var mid = Vector3.Lerp(s_Ball, s_Wreck, 0.4f) + Vector3.up * 2.5f;
            var look = Vector3.Lerp(mid, s_Ball + (s_Wreck - s_Ball) * 0.2f + Vector3.up * 1.4f, e);
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
            // a little landing jolt right at the cut
            float jolt = Mathf.Exp(-m * 9f) * 1.6f;
            rot *= Quaternion.Euler(Mathf.Sin(m * 47f) * jolt, Mathf.Sin(m * 41f) * jolt, 0f);
            fov = Mathf.Lerp(62f, 44f, e);
        }

        // ------------------------------------------------------------------ the HUD

        static float S01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        /// <summary>The intro's HUD (Hud.DrawGame / DrawSpectator call it first): letterbox bars, the flashes and cuts,
        /// the goal marker on the ball and the captions. True while it's playing (draw nothing else); in the moment after
        /// (Outro) it draws the fade back in and returns false (the HUD goes on over it).</summary>
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

            // the goal marker on the ball (under the bars)
            if (m >= CardAt) DrawGoalMarker(k, m - CardAt, label, small, fill, shadowed);

            // the flashes: in from black, the cut to the crash, the white-hot impact, the cut to the crash site
            float black = 1f - Mathf.Clamp01(e / 0.25f);
            if (e > Length - 0.22f) black = Mathf.Max(black, Mathf.Clamp01((e - (Length - 0.22f)) / 0.2f));
            float white = 0f;
            if (e >= SpaceEnd && e < SpaceEnd + 0.2f) white = 0.85f * (1f - (e - SpaceEnd) / 0.2f);
            if (e >= ImpactAt && e < ImpactAt + 0.65f) { float f = 1f - (e - ImpactAt) / 0.65f; white = Mathf.Max(white, f * f); }
            if (e >= CrashEnd - 0.14f && e < CrashEnd) white = Mathf.Max(white, (e - (CrashEnd - 0.14f)) / 0.14f);
            if (e >= CrashEnd && e < CrashEnd + 0.3f) white = Mathf.Max(white, 1f - (e - CrashEnd) / 0.3f);
            if (white > 0f) fill(full, new Color(1f, 0.96f, 0.88f, Mathf.Clamp01(white)));

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

            // the crash site: what to do, punched in
            if (m >= 0f && bars > 10f)
            {
                float pop = S01((m - 0.2f) / 0.18f);
                if (pop > 0f)
                {
                    float scale = Mathf.Lerp(1.7f, 1f, pop);
                    float a = Mathf.Clamp01(pop * 2f) * (1f - S01((e - (Length - 0.3f)) / 0.25f));
                    var bs = new GUIStyle(big) { fontSize = Mathf.RoundToInt(big.fontSize * 0.95f * scale), alignment = TextAnchor.MiddleCenter, richText = true };
                    bs.normal.textColor = new Color(1f, 1f, 1f, a);
                    var me = PlayerNet.Local;
                    var tc = me != null ? Cfg.TeamColor[Mathf.Clamp(me.Team.Value, 0, 3)] : new Color(1f, 0.82f, 0.29f);
                    string col = ColorUtility.ToHtmlStringRGBA(new Color(Mathf.Lerp(tc.r, 1f, 0.2f), Mathf.Lerp(tc.g, 1f, 0.2f), Mathf.Lerp(tc.b, 1f, 0.2f), a));
                    string goal = Cfg.Builder ? $"GRAB THE BALL - <color=#{col}>KEEP IT</color> TILL TIME'S UP!" : $"GET THE BALL INTO <color=#{col}>YOUR MACHINE!</color>";
                    shadowed(new Rect(0, sh - bars + 2f * k, sw, bars * 0.62f), goal, bs);
                    var ss = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, richText = true };
                    ss.normal.textColor = new Color(0.85f, 0.88f, 0.92f, a * S01((m - 0.45f) / 0.25f));
                    GUI.Label(new Rect(0, sh - bars * 0.42f, sw, bars * 0.36f), FlareTip.Line, ss);
                }
                // the top bar: where we are
                var top = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                top.normal.textColor = new Color(1f, 0.82f, 0.29f, S01(m / 0.3f) * (1f - S01((e - (Length - 0.3f)) / 0.25f)));
                GUI.Label(new Rect(0, 0, sw, bars), "THE CRASH SITE", top);
            }
            return true;
        }

        /// <summary>The ball's goal marker as you see it by the glass (FlareTip: the EMERGENCY FLARE card and the gold
        /// diamond over the ball), popping up, with gold lock-on brackets closing in round the ball.</summary>
        static void DrawGoalMarker(float k, float t, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ballPos = Ball.Instance != null ? Ball.Instance.transform.position : s_Ball;
            var sp = cam.WorldToScreenPoint(ballPos + Vector3.up * 2.4f);
            var bp = cam.WorldToScreenPoint(ballPos);
            if (sp.z < 0f || bp.z < 0f) return;
            CardShownAt = Time.unscaledTime;
            float sw = Screen.width, sh = Screen.height;
            var pos = new Vector2(sp.x, sh - sp.y);
            var ball = new Vector2(bp.x, sh - bp.y);
            float pop = S01(t / 0.22f);
            float a = pop * (1f - S01((Elapsed - (Length - 0.3f)) / 0.25f));
            Color A(Color c) { c.a *= a; return c; }
            var gold = new Color(1f, 0.82f, 0.29f, 0.95f);

            // lock-on brackets round the ball, closing in and pulsing
            float rad = Mathf.Max(14f * k, Ball.Radius / Mathf.Max(0.01f, bp.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * sh * 0.5f * 1.8f);
            rad *= Mathf.Lerp(3.2f, 1f, S01(t / 0.3f)) * (1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.08f);
            float len = Mathf.Max(8f * k, rad * 0.45f), th = Mathf.Max(2f, 3f * k);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    float cx = ball.x + sx * rad, cy = ball.y + sy * rad;
                    fill(new Rect(sx < 0 ? cx : cx - len, cy - th / 2f, len, th), A(gold));
                    fill(new Rect(cx - th / 2f, sy < 0 ? cy : cy - len, th, len), A(gold));
                }

            // the card (FlareTip's): the title, big, and one short line under it - punched in
            float s = Mathf.Lerp(1.4f, 1f, pop);
            var l2 = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true, fontSize = Mathf.RoundToInt(label.fontSize * 1.25f * s) };
            var l3 = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true, fontSize = Mathf.RoundToInt(small.fontSize * s) };
            float w = Mathf.Min(sw - 20f, 380f * k * s);
            float h2 = l2.CalcHeight(new GUIContent(FlareTip.Title), w), h3 = l3.CalcHeight(new GUIContent(FlareTip.Line), w - 24f * k);
            float h = 10f * k + h2 + h3 + 12f * k;
            float x = Mathf.Clamp(pos.x - w / 2f, 10f, sw - w - 10f);
            float y = Mathf.Clamp(pos.y - h - 18f * k, sh * 0.12f, sh * 0.89f - h - 8f * k);
            fill(new Rect(x, y, w, h), A(new Color(0.04f, 0.06f, 0.1f, 0.82f)));
            fill(new Rect(x, y, w, 3f * k), A(new Color(1f, 0.82f, 0.29f, 0.9f)));
            string hex(Color c) => ColorUtility.ToHtmlStringRGBA(A(c));
            float ty = y + 8f * k;
            shadowed(new Rect(x, ty, w, h2), $"<b><color=#{hex(Color.white)}>{FlareTip.Title}</color></b>", l2);
            ty += h2;
            shadowed(new Rect(x + 12f * k, ty, w - 24f * k, h3), $"<color=#{hex(new Color(0.85f, 0.88f, 0.92f))}>{FlareTip.Line}</color>", l3);

            // the gold diamond over the ball
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.15f;
            float sz = 16f * k * pulse * Mathf.Lerp(2f, 1f, pop);
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, pos);
            fill(new Rect(pos.x - sz / 2, pos.y - sz / 2, sz, sz), A(gold));
            fill(new Rect(pos.x - sz / 4, pos.y - sz / 4, sz / 2, sz / 2), A(new Color(0.1f, 0.1f, 0.1f, 0.9f)));
            GUI.matrix = old;
        }
    }
}
