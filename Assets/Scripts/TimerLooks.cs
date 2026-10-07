using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > TIMER: the top-centre timer panel (Hud.Notify.cs: DrawTopPanel).</summary>
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
    }

    public partial class Hud
    {
        /// <summary>Settings > Display: the TIMER section.</summary>
        void DrawTimerLooks()
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
        }
    }
}
