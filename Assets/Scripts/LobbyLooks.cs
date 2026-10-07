using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display, two more sections:
    /// TIMER - the top-centre timer panel (Hud.Notify.cs: DrawTopPanel): its size, how dark its plate is, and whether it
    ///   shows the drain bar, the line about what to do, the mode's tag and the final-minute flashing.
    /// SHIP LOBBY - the living room you wait in (ShipLobby.cs): post processing of its own (like the main menu
    ///   cutscene's) and the room itself - how dark it is, how bright the lamp, the telly and the lights passing the
    ///   window are, the lamp swinging, the cigarette smoke and the camera's field of view.
    /// </summary>
    public static partial class GameSettings
    {
        // ---- the timer ----
        const string GTimer = "TIMER";
        /// <summary>The timer uses the settings below (off: the defaults).</summary>
        public static readonly DisplayPref.Bool TimerOwn = new("timer.own", GTimer, false);
        public static readonly DisplayPref.Float TimerSize = new("timer.size", GTimer, 1f, 0.6f, 1.5f);
        public static readonly DisplayPref.Float TimerPlate = new("timer.plate", GTimer, 0.6f, 0f, 1f);
        public static readonly DisplayPref.Bool TimerBar = new("timer.bar", GTimer, true);
        public static readonly DisplayPref.Bool TimerSub = new("timer.sub", GTimer, true);
        public static readonly DisplayPref.Bool TimerTag = new("timer.tag", GTimer, true);
        public static readonly DisplayPref.Bool TimerFlash = new("timer.flash", GTimer, true);
        public static readonly DisplayPref.Bool TimerAccent = new("timer.accent", GTimer, true);

        public static float TimerSizeNow => TimerOwn.Value ? TimerSize.Value : 1f;
        public static float TimerPlateNow => TimerOwn.Value ? TimerPlate.Value : 0.6f;
        public static bool TimerBarNow => !TimerOwn.Value || TimerBar.Value;
        public static bool TimerSubNow => !TimerOwn.Value || TimerSub.Value;
        public static bool TimerTagNow => !TimerOwn.Value || TimerTag.Value;
        public static bool TimerFlashNow => !TimerOwn.Value || TimerFlash.Value;
        public static bool TimerAccentNow => !TimerOwn.Value || TimerAccent.Value;

        public static void ResetTimer(bool save = true)
        {
            TimerOwn.Set(false, save); TimerSize.Set(1f, save); TimerPlate.Set(0.6f, save);
            TimerBar.Set(true, save); TimerSub.Set(true, save); TimerTag.Set(true, save); TimerFlash.Set(true, save); TimerAccent.Set(true, save);
        }

        // ---- the ship lobby ----
        const string GLobby = "SHIP LOBBY";
        /// <summary>The ship lobby uses the post processing and room settings below (off: the world's post processing and the room as built).</summary>
        public static readonly DisplayPref.Bool LobbyOwn = new("lobby.own", GLobby, false);
        public static readonly DisplayPref.Bool LobbyBloom = new("lobby.bloom", GLobby, true);
        public static readonly DisplayPref.Float LobbyBloomStrength = new("lobby.bloom.strength", GLobby, 0.6f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyVignette = new("lobby.vignette", GLobby, true);
        public static readonly DisplayPref.Float LobbyVignetteStrength = new("lobby.vignette.strength", GLobby, 0.5f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyGrading = new("lobby.grading", GLobby, true);
        public static readonly DisplayPref.Float LobbyGradingStrength = new("lobby.grading.strength", GLobby, 0.6f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyOutlines = new("lobby.outlines", GLobby, true);
        public static readonly DisplayPref.Float LobbyOutlinesStrength = new("lobby.outlines.strength", GLobby, 0.4f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyCel = new("lobby.cel", GLobby, false);
        public static readonly DisplayPref.Float LobbyCelStrength = new("lobby.cel.strength", GLobby, 0.5f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyGrain = new("lobby.grain", GLobby, true);
        public static readonly DisplayPref.Float LobbyGrainStrength = new("lobby.grain.strength", GLobby, 0.35f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyChromatic = new("lobby.chromatic", GLobby, false);
        public static readonly DisplayPref.Float LobbyChromaticStrength = new("lobby.chromatic.strength", GLobby, 0.4f, 0f, 1f);
        // the room
        public static readonly DisplayPref.Float LobbyDark = new("lobby.dark", GLobby, 1f, 0.3f, 1.6f);
        public static readonly DisplayPref.Float LobbyLamp = new("lobby.lamp", GLobby, 1f, 0f, 2f);
        public static readonly DisplayPref.Float LobbyTv = new("lobby.tv", GLobby, 1f, 0f, 2f);
        public static readonly DisplayPref.Float LobbyWindow = new("lobby.window", GLobby, 1f, 0f, 2f);
        public static readonly DisplayPref.Bool LobbySway = new("lobby.sway", GLobby, true);
        public static readonly DisplayPref.Bool LobbySmoke = new("lobby.smoke", GLobby, true);
        public static readonly DisplayPref.Float LobbyFov = new("lobby.fov", GLobby, 60f, 45f, 85f);

        /// <summary>The ship lobby's own post processing is in use right now (switched on, and it's showing).</summary>
        public static bool LobbyPostNow => LobbyOwn.Value && ShipLobby.Active;
        /// <summary>The lobby's room settings in use (1 = as built).</summary>
        public static float LobbyDarkNow => LobbyOwn.Value ? LobbyDark.Value : 1f;
        public static float LobbyLampNow => LobbyOwn.Value ? LobbyLamp.Value : 1f;
        public static float LobbyTvNow => LobbyOwn.Value ? LobbyTv.Value : 1f;
        public static float LobbyWindowNow => LobbyOwn.Value ? LobbyWindow.Value : 1f;
        public static bool LobbySwayNow => !LobbyOwn.Value || LobbySway.Value;
        public static bool LobbySmokeNow => !LobbyOwn.Value || LobbySmoke.Value;
        public static float LobbyFovNow => LobbyOwn.Value ? LobbyFov.Value : 60f;

        static float LobbyExtra(PostExtra e)
        {
            switch (e)
            {
                case PostExtra.Outlines: return Amt(LobbyOutlines, LobbyOutlinesStrength);
                case PostExtra.CelBanding: return Amt(LobbyCel, LobbyCelStrength);
                case PostExtra.FilmGrain: return Amt(LobbyGrain, LobbyGrainStrength);
                case PostExtra.Chromatic: return Amt(LobbyChromatic, LobbyChromaticStrength);
            }
            return 0f;
        }

        public static void ResetLobby(bool save = true)
        {
            LobbyOwn.Set(false, save);
            LobbyBloom.Set(true, save); LobbyBloomStrength.Set(0.6f, save);
            LobbyVignette.Set(true, save); LobbyVignetteStrength.Set(0.5f, save);
            LobbyGrading.Set(true, save); LobbyGradingStrength.Set(0.6f, save);
            LobbyOutlines.Set(true, save); LobbyOutlinesStrength.Set(0.4f, save);
            LobbyCel.Set(false, save); LobbyCelStrength.Set(0.5f, save);
            LobbyGrain.Set(true, save); LobbyGrainStrength.Set(0.35f, save);
            LobbyChromatic.Set(false, save); LobbyChromaticStrength.Set(0.4f, save);
            LobbyDark.Set(1f, save); LobbyLamp.Set(1f, save); LobbyTv.Set(1f, save); LobbyWindow.Set(1f, save);
            LobbySway.Set(true, save); LobbySmoke.Set(true, save); LobbyFov.Set(60f, save);
        }
    }

    public partial class Hud
    {
        bool m_TimerOpen, m_LobbyLooksOpen;

        /// <summary>Settings > Display: the TIMER and SHIP LOBBY sections (after the other own-look sections).</summary>
        void DrawTimerAndLobbyLooks()
        {
            float k = m_Scale, lw = 210 * k;
            void Slider(string name, DisplayPref.Float p, string shown, float step)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                float v = SliderRow(name, p.Value, p.Min, p.Max, shown, lw - 16 * k);
                p.Set(Mathf.Round(v / step) * step);
                GUILayout.EndHorizontal();
            }
            void Toggle(string name, DisplayPref.Bool p)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                GUILayout.Label(name, m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
                p.Set(ToggleBtn(p.Value, p.Value ? "On" : "Off", GUILayout.Width(80 * k), GUILayout.Height(26 * k)));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            void Row(string name, DisplayPref.Bool on, DisplayPref.Float s)
            {
                float v = s.Value;
                bool o = EffectRow(name, on.Value, ref v);
                on.Set(o); s.Set(Mathf.Round(v * 20f) / 20f);
            }
            // ---- the timer ----
            if (FoldRow("TIMER", ref m_TimerOpen, GameSettings.TimerOwn))
            {
                Slider("Size", GameSettings.TimerSize, $"{GameSettings.TimerSize.Value * 100f:0}%", 0.05f);
                Slider("Plate darkness", GameSettings.TimerPlate, $"{GameSettings.TimerPlate.Value * 100f:0}%", 0.05f);
                Toggle("Drain bar", GameSettings.TimerBar);
                Toggle("What to do line", GameSettings.TimerSub);
                Toggle("Game mode tag", GameSettings.TimerTag);
                Toggle("Final minute flashing", GameSettings.TimerFlash);
                Toggle("Phase colours", GameSettings.TimerAccent);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#bbbbbb>The timer at the top of the screen in a match.</color>", m_SmallWrap);
                if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetTimer();
                GUILayout.EndHorizontal();
            }
            // ---- the ship lobby ----
            if (FoldRow("SHIP LOBBY", ref m_LobbyLooksOpen, GameSettings.LobbyOwn))
            {
                Row("Bloom", GameSettings.LobbyBloom, GameSettings.LobbyBloomStrength);
                Row("Vignette", GameSettings.LobbyVignette, GameSettings.LobbyVignetteStrength);
                Row("Colour grading", GameSettings.LobbyGrading, GameSettings.LobbyGradingStrength);
                Row("Outlines", GameSettings.LobbyOutlines, GameSettings.LobbyOutlinesStrength);
                Row("Cel banding", GameSettings.LobbyCel, GameSettings.LobbyCelStrength);
                Row("Film grain", GameSettings.LobbyGrain, GameSettings.LobbyGrainStrength);
                Row("Chromatic aberration", GameSettings.LobbyChromatic, GameSettings.LobbyChromaticStrength);
                Slider("Room darkness", GameSettings.LobbyDark, $"{GameSettings.LobbyDark.Value * 100f:0}%", 0.05f);
                Slider("Lamp", GameSettings.LobbyLamp, $"{GameSettings.LobbyLamp.Value * 100f:0}%", 0.05f);
                Slider("Telly", GameSettings.LobbyTv, $"{GameSettings.LobbyTv.Value * 100f:0}%", 0.05f);
                Slider("Window lights", GameSettings.LobbyWindow, $"{GameSettings.LobbyWindow.Value * 100f:0}%", 0.05f);
                Slider("Field of view", GameSettings.LobbyFov, $"{GameSettings.LobbyFov.Value:0}°", 1f);
                Toggle("Lamp swinging", GameSettings.LobbySway);
                Toggle("Cigarette smoke", GameSettings.LobbySmoke);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#bbbbbb>The living room you wait in before a match: its own post processing (the game keeps the settings above) and the room's lights.</color>", m_SmallWrap);
                if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetLobby();
                GUILayout.EndHorizontal();
            }
        }
    }
}
