using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > HUD & TIMER > DEATH SCREEN: how the death screen looks (Hud.Death.cs reads these) - YOU DIED's
    /// font, size and height, its outline (thickness right down to none, and colour) and colour, the red accent (the line
    /// under it, the respawn bar, the cards' edges, the kill cam's line), how strong the red edges of the screen are, and
    /// the glow, the line and the respawn bar on / off. The defaults are the death screen as designed.
    /// </summary>
    public static partial class GameSettings
    {
        const string GDeath = "DEATH SCREEN";
        public static readonly DisplayPref.Choice DeathFont = new("death.font", GDeath, FontPrefNames, 0);
        /// <summary>How big YOU DIED is (1 = as designed).</summary>
        public static readonly DisplayPref.Float DeathSize = new("death.size", GDeath, 1f, 0.4f, 1.8f);
        /// <summary>How thick the outline round its words is (1 = as designed, 0 = none).</summary>
        public static readonly DisplayPref.Float DeathInk = new("death.ink", GDeath, 1f, 0f, 2f);
        public static readonly DisplayPref.Colour DeathInkColour = new("death.ink.colour", GDeath, new Color(0.25f, 0f, 0f));
        public static readonly DisplayPref.Colour DeathTextColour = new("death.colour.text", GDeath, new Color(1f, 0.93f, 0.9f));
        public static readonly DisplayPref.Colour DeathAccent = new("death.colour.accent", GDeath, new Color(1f, 0.22f, 0.2f));
        /// <summary>How strong the dark red round the screen's edges (and the tint over it) is (1 = as designed, 0 = none).</summary>
        public static readonly DisplayPref.Float DeathVignette = new("death.vignette", GDeath, 1f, 0f, 2f);
        /// <summary>How far up (-) or down (+) the screen it sits (pixels at the UI scale).</summary>
        public static readonly DisplayPref.Float DeathY = new("death.y", GDeath, 0f, -200f, 300f);
        public static readonly DisplayPref.Bool DeathGlow = new("death.glow", GDeath, true);
        public static readonly DisplayPref.Bool DeathLine = new("death.line", GDeath, true);
        public static readonly DisplayPref.Bool DeathBar = new("death.bar", GDeath, true);

        public static Font DeathFontNow => FontForPref(DeathFont.Value);

        public static void ResetDeath(bool save = true)
        {
            DeathFont.Set(0, save); DeathSize.Set(1f, save); DeathInk.Set(1f, save); DeathInkColour.Set(DeathInkColour.Default, save);
            DeathTextColour.Set(DeathTextColour.Default, save); DeathAccent.Set(DeathAccent.Default, save); DeathVignette.Set(1f, save);
            DeathY.Set(0f, save); DeathGlow.Set(true, save); DeathLine.Set(true, save); DeathBar.Set(true, save);
        }
    }

    public partial class Hud
    {
        /// <summary>(tests) show the death screen's YOU DIED (as if dead, with a respawn bar) over the HUD and the settings.</summary>
        public static bool TestDeathScreen;
        /// <summary>(tests) the frame the death screen was last drawn, and YOU DIED's outline then (px) and its font size.</summary>
        public static int DeathShownFrame = -10;
        public static float DeathInkPx, DeathFontPx;

        /// <summary>Settings > Display > HUD & TIMER: the DEATH SCREEN section.</summary>
        void DrawDeathLooks()
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
            Caption("DEATH SCREEN  ·  YOU DIED");
            GUILayout.BeginHorizontal();
            bool test = ToggleBtn(TestDeathScreen, TestDeathScreen ? "Hide the test death screen" : "Show a test death screen", GUILayout.Width(260 * k), GUILayout.Height(30 * k));
            if (test != TestDeathScreen) { TestDeathScreen = test; m_TestDeadSince = -1f; }
            GUILayout.Label("<color=#bbbbbb>  over these settings, so you can see each change</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            ChoiceRow("Font", GameSettings.DeathFont, true);
            Slider("Size", GameSettings.DeathSize, $"{GameSettings.DeathSize.Value * 100f:0}%", 0.05f);
            Slider("Height on screen", GameSettings.DeathY, Mathf.Abs(GameSettings.DeathY.Value) < 0.5f ? "as usual" : $"{(GameSettings.DeathY.Value > 0 ? "+" : "")}{GameSettings.DeathY.Value:0} px", 5f);
            Slider("Outline width", GameSettings.DeathInk, GameSettings.DeathInk.Value < 0.01f ? "none" : $"{GameSettings.DeathInk.Value * 100f:0}%", 0.05f);
            ColourPrefRow("Outline colour", GameSettings.DeathInkColour, s_InkPresets);
            ColourPrefRow("YOU DIED colour", GameSettings.DeathTextColour);
            ColourPrefRow("Accent (line, bar)", GameSettings.DeathAccent);
            Slider("Red edges", GameSettings.DeathVignette, GameSettings.DeathVignette.Value < 0.01f ? "none" : $"{GameSettings.DeathVignette.Value * 100f:0}%", 0.05f);
            Toggle("Glow behind YOU DIED", GameSettings.DeathGlow);
            Toggle("Line under it", GameSettings.DeathLine);
            Toggle("Respawn bar", GameSettings.DeathBar);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>The screen while you wait to respawn (not the kill cam - its line takes the accent colour). Outline width goes right down to none.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetDeath();
            GUILayout.EndHorizontal();
        }
    }
}
