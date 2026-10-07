using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > NOTIFICATIONS > COUNTDOWN: the countdowns' own look, apart from the banners' (NotifLooks.cs) -
    /// the end countdown (YOU WIN IN 7: Hud.Notify.cs DrawEndCountdown) and the sudden death arena's Ready? / Set / ROCK!
    /// (DrawFightWord). Their size, font, text outline (on / off, thickness, colour), height on the screen, the label's
    /// band, the colours (the label, the seconds, the last three, the overtime one, each fight word) and the glow, the
    /// pips and the darkening behind Ready? / Set. The defaults are the countdowns as designed.
    /// </summary>
    public static partial class GameSettings
    {
        const string GCount = "COUNTDOWN";
        /// <summary>How big the words and the numbers are (1 = as designed).</summary>
        public static readonly DisplayPref.Float CountSize = new("count.size", GCount, 1f, 0.4f, 2f);
        public static readonly DisplayPref.Choice CountFont = new("count.font", GCount, FontPrefNames, 0);
        public static readonly DisplayPref.Bool CountInkOn = new("count.ink.on", GCount, true);
        /// <summary>The text outline's thickness (1 = as designed, 0 = none).</summary>
        public static readonly DisplayPref.Float CountInk = new("count.ink", GCount, 1f, 0f, 3f);
        public static readonly DisplayPref.Colour CountInkColour = new("count.ink.colour", GCount, Color.black);
        /// <summary>How far up (-) or down (+) the screen they sit (pixels at the UI scale).</summary>
        public static readonly DisplayPref.Float CountY = new("count.y", GCount, 0f, -250f, 400f);
        /// <summary>The dark band behind the label: how wide and how dark (1 = as designed).</summary>
        public static readonly DisplayPref.Float CountWidth = new("count.width", GCount, 1f, 0.4f, 1.8f);
        public static readonly DisplayPref.Float CountPlate = new("count.plate", GCount, 1f, 0f, 1.5f);
        public static readonly DisplayPref.Colour CountLabelColour = new("count.colour.label", GCount, new Color(1f, 0.95f, 0.9f));
        public static readonly DisplayPref.Colour CountColour = new("count.colour", GCount, new Color(1f, 0.85f, 0.25f));
        public static readonly DisplayPref.Colour CountColourHard = new("count.colour.last", GCount, new Color(1f, 0.55f, 0.15f));
        public static readonly DisplayPref.Colour CountColourOver = new("count.colour.overtime", GCount, new Color(1f, 0.25f, 0.18f));
        public static readonly DisplayPref.Bool CountGlow = new("count.glow", GCount, true);
        public static readonly DisplayPref.Bool CountPips = new("count.pips", GCount, true);
        // the sudden death arena's countdown
        public static readonly DisplayPref.Colour CountColTitle = new("count.colour.fighttitle", GCount, new Color(1f, 0.9f, 0.4f));
        public static readonly DisplayPref.Colour CountColReady = new("count.colour.ready", GCount, Color.white);
        public static readonly DisplayPref.Colour CountColSet = new("count.colour.set", GCount, new Color(1f, 0.85f, 0.3f));
        public static readonly DisplayPref.Colour CountColRock = new("count.colour.rock", GCount, new Color(1f, 0.3f, 0.15f));
        /// <summary>The screen darkens a little behind Ready? and Set.</summary>
        public static readonly DisplayPref.Bool CountDim = new("count.dim", GCount, true);

        public static Font CountFontNow => FontForPref(CountFont.Value);
        public static float CountInkNow => CountInkOn.Value ? CountInk.Value : 0f;
        /// <summary>The outline's colour at `alpha` (what the design had there).</summary>
        public static Color CountInkEdge(float alpha) { var c = CountInkColour.Value; c.a = Mathf.Clamp01(alpha); return c; }

        /// <summary>The colours with their names (the settings rows).</summary>
        public static readonly (string name, DisplayPref.Colour pref)[] CountColours =
        {
            ("Label (YOU WIN IN)", CountLabelColour), ("The seconds", CountColour), ("The last three", CountColourHard), ("Into overtime", CountColourOver),
            ("SUDDEN DEATH title", CountColTitle), ("Ready?", CountColReady), ("Set", CountColSet), ("ROCK!", CountColRock),
        };

        public static void ResetCountdown(bool save = true)
        {
            CountSize.Set(1f, save); CountFont.Set(0, save); CountInkOn.Set(true, save); CountInk.Set(1f, save); CountInkColour.Set(Color.black, save);
            CountY.Set(0f, save); CountWidth.Set(1f, save); CountPlate.Set(1f, save);
            CountGlow.Set(true, save); CountPips.Set(true, save); CountDim.Set(true, save);
            foreach (var c in CountColours) c.pref.Set(c.pref.Default, save);
        }
    }

    public partial class Hud
    {
        static float s_TestFightAt = -10f;

        /// <summary>TEST READY / SET / ROCK! (also for the tests): the sudden death arena's countdown, on top of everything.</summary>
        public static void TestFightWords()
        {
            s_TestFightAt = Time.unscaledTime;
            s_TestCountAt = -10f;
            s_BannerTest = false;
        }

        /// <summary>(tests) the frame Ready? / Set / ROCK! was last drawn, its word, and the fill colour and outline it had.</summary>
        public static int FightWordShownFrame = -10;
        public static string FightWordShown = "";
        public static Color FightWordColour;
        public static float CountInkPx;

        /// <summary>The test Ready? / Set / ROCK!, if one's going (from DrawTestNotification: on top of everything).</summary>
        bool DrawTestFightWords(float k)
        {
            float age = Time.unscaledTime - s_TestFightAt;
            if (age < 0f || age >= 4.2f) return false;
            // Ready? for 1.5 s, Set for 1.5 s, then ROCK! fading over 1.2 s
            if (age < 1.5f) DrawFightWord("Ready?", age / 1.5f, k);
            else if (age < 3f) DrawFightWord("Set", (age - 1.5f) / 1.5f, k);
            else DrawFightWord("ROCK!", (age - 3f) / 1.2f, k);
            return true;
        }

        /// <summary>Settings > Display > NOTIFICATIONS: the COUNTDOWN section.</summary>
        void DrawCountdownLooks()
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
            void Toggle(string name, DisplayPref.Bool p, string note = "")
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                GUILayout.Label(name, m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
                p.Set(ToggleBtn(p.Value, p.Value ? "On" : "Off", GUILayout.Width(80 * k), GUILayout.Height(26 * k)));
                if (note != "") GUILayout.Label($"<color=#bbbbbb>  {note}</color>", m_Small, GUILayout.Height(26 * k));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            Caption("COUNTDOWN  ·  YOU WIN IN 7, and Ready? / Set / ROCK!");
            GUILayout.BeginHorizontal();
            if (Btn("Test countdown", GUILayout.Width(170 * k), GUILayout.Height(30 * k))) TestCountdownNow();
            if (Btn("Test Ready / Set / ROCK!", GUILayout.Width(230 * k), GUILayout.Height(30 * k))) TestFightWords();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Slider("Size", GameSettings.CountSize, $"{GameSettings.CountSize.Value * 100f:0}%", 0.05f);
            Slider("Height on screen", GameSettings.CountY, Mathf.Abs(GameSettings.CountY.Value) < 0.5f ? "as usual" : $"{(GameSettings.CountY.Value > 0 ? "+" : "")}{GameSettings.CountY.Value:0} px", 5f);
            ChoiceRow("Font", GameSettings.CountFont, true);
            Toggle("Text outline", GameSettings.CountInkOn);
            if (GameSettings.CountInkOn.Value)
            {
                Slider("Text outline thickness", GameSettings.CountInk, GameSettings.CountInk.Value < 0.01f ? "none" : $"{GameSettings.CountInk.Value * 100f:0}%", 0.05f);
                ColourPrefRow("Text outline colour", GameSettings.CountInkColour, s_InkPresets);
            }
            Slider("Label band width", GameSettings.CountWidth, $"{GameSettings.CountWidth.Value * 100f:0}%", 0.05f);
            Slider("Label band darkness", GameSettings.CountPlate, GameSettings.CountPlate.Value < 0.01f ? "none" : $"{GameSettings.CountPlate.Value * 100f:0}%", 0.05f);
            Toggle("Glow behind the number", GameSettings.CountGlow);
            Toggle("Pips (one per second)", GameSettings.CountPips);
            Toggle("Darken behind Ready? / Set", GameSettings.CountDim);
            SubHead("COLOURS");
            foreach (var (name, pref) in GameSettings.CountColours) ColourPrefRow(name, pref);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>The countdowns have their own look - the banners' STYLE above doesn't change them. Double-click a colour square for the colour wheel.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetCountdown();
            GUILayout.EndHorizontal();
        }
    }
}
