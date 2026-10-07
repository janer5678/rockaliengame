using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > HUD AND TIMER > TIMER: the top-centre timer panel (Hud.Notify.cs: DrawTopPanel) - its
    /// size, plate, font, ink outline, where it sits, which parts show, where its label goes and each phase's colour.
    /// All of it only while the timer's "Own look" is on (off: the timer as designed).</summary>
    public static partial class GameSettings
    {
        /// <summary>The font choices for the timer and the notifications: the UI's own, then the UI fonts (the same names as
        /// FontChoices - written out here, as the other files' static fields may not be made yet when these are).</summary>
        public static string[] FontPrefNames => FontPrefList.Names;

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
        /// <summary>How thick the ink outline round the label and the clock is (1 = as designed, 0 = none).</summary>
        public static readonly DisplayPref.Float TimerInk = new("timer.ink", GTimer, 1f, 0f, 3f);
        /// <summary>How wide the dark plate (and its line and bar) is (1 = as designed).</summary>
        public static readonly DisplayPref.Float TimerWidth = new("timer.width", GTimer, 1f, 0.5f, 1.8f);
        /// <summary>The timer's font: "Same as the UI" or one of the UI fonts.</summary>
        public static readonly DisplayPref.Choice TimerFont = new("timer.font", GTimer, FontPrefNames, 0);
        /// <summary>How far down the screen it sits (pixels at the UI scale; 0 = the top).</summary>
        public static readonly DisplayPref.Float TimerY = new("timer.y", GTimer, 0f, 0f, 400f);
        /// <summary>The thin accent line along the top of the plate.</summary>
        public static readonly DisplayPref.Bool TimerTopLine = new("timer.topline", GTimer, true);
        /// <summary>The drain bar's dark track (the grey ends it drains into); off: just the coloured part.</summary>
        public static readonly DisplayPref.Bool TimerBarBack = new("timer.bar.back", GTimer, true);
        /// <summary>Where the label (TIME LEFT...) goes: left of the clock, or above it.</summary>
        public static readonly DisplayPref.Choice TimerLabelPos = new("timer.label", GTimer, new[] { "Left", "Above" }, 0);
        // each phase's colour (the label, the line, the bar, the tag)
        public static readonly DisplayPref.Colour TimerColWaiting = new("timer.colour.waiting", GTimer, new Color(0.85f, 0.88f, 0.95f));
        public static readonly DisplayPref.Colour TimerColStart = new("timer.colour.start", GTimer, new Color(0.45f, 1f, 0.45f));
        public static readonly DisplayPref.Colour TimerColPreBall = new("timer.colour.preball", GTimer, new Color(0.35f, 0.85f, 1f));
        public static readonly DisplayPref.Colour TimerColBallLive = new("timer.colour.timeleft", GTimer, new Color(1f, 0.85f, 0.3f));
        public static readonly DisplayPref.Colour TimerColOvertime = new("timer.colour.overtime", GTimer, new Color(1f, 0.22f, 0.15f));
        public static readonly DisplayPref.Colour TimerColSudden = new("timer.colour.suddendeath", GTimer, new Color(1f, 0.3f, 0.25f));

        /// <summary>The font for a FontPrefNames choice (null: the UI's own).</summary>
        public static Font FontForPref(int choice)
        {
            if (choice <= 0 || choice >= FontPrefNames.Length) return null;
            int i = System.Array.IndexOf(FontChoices, FontPrefNames[choice]);
            return i < 0 ? null : UiLook.FontFor(i);
        }
        /// <summary>A FontPrefNames choice is installed on this PC.</summary>
        public static bool FontPrefInstalled(int choice)
        {
            if (choice <= 0) return true;
            int i = choice < FontPrefNames.Length ? System.Array.IndexOf(FontChoices, FontPrefNames[choice]) : -1;
            return i >= 0 && FontInstalled(i);
        }

        public static float TimerSizeNow => TimerOwn.Value ? TimerSize.Value : 1f;
        public static float TimerPlateNow => TimerOwn.Value ? TimerPlate.Value : 0.6f;
        public static bool TimerBarNow => !TimerOwn.Value || TimerBar.Value;
        public static bool TimerSubNow => !TimerOwn.Value || TimerSub.Value;
        public static bool TimerTagNow => !TimerOwn.Value || TimerTag.Value;
        public static bool TimerFlashNow => !TimerOwn.Value || TimerFlash.Value;
        public static bool TimerAccentNow => !TimerOwn.Value || TimerAccent.Value;
        public static float TimerInkNow => TimerOwn.Value ? TimerInk.Value : 1f;
        public static float TimerWidthNow => TimerOwn.Value ? TimerWidth.Value : 1f;
        public static Font TimerFontNow => TimerOwn.Value ? FontForPref(TimerFont.Value) : null;
        public static float TimerYNow => TimerOwn.Value ? TimerY.Value : 0f;
        public static bool TimerTopLineNow => !TimerOwn.Value || TimerTopLine.Value;
        public static bool TimerBarBackNow => !TimerOwn.Value || TimerBarBack.Value;
        public static bool TimerLabelAboveNow => TimerOwn.Value && TimerLabelPos.Value == 1;
        /// <summary>A phase's colour in use: its own while the timer's own look is on, else as designed.</summary>
        public static Color TimerColourNow(DisplayPref.Colour c) => TimerOwn.Value ? c.Value : c.Default;

        public static void ResetTimer(bool save = true)
        {
            TimerOwn.Set(false, save); TimerSize.Set(1f, save); TimerPlate.Set(0.6f, save);
            TimerBar.Set(true, save); TimerSub.Set(true, save); TimerTag.Set(true, save); TimerFlash.Set(true, save); TimerAccent.Set(true, save);
            TimerInk.Set(1f, save); TimerWidth.Set(1f, save); TimerFont.Set(0, save); TimerY.Set(0f, save);
            TimerTopLine.Set(true, save); TimerBarBack.Set(true, save); TimerLabelPos.Set(0, save);
            foreach (var c in TimerColours) c.pref.Set(c.pref.Default, save);
        }

        /// <summary>The phase colours with their names (the settings rows).</summary>
        public static readonly (string name, DisplayPref.Colour pref)[] TimerColours =
        {
            ("Waiting for players", TimerColWaiting), ("Match starts in", TimerColStart), ("Wall drops in", TimerColPreBall),
            ("Time left", TimerColBallLive), ("Overtime", TimerColOvertime), ("Sudden death", TimerColSudden),
        };
    }

    /// <summary>(its own class, so it's always made before the GameSettings fields that use it, whichever file they're in)</summary>
    static class FontPrefList
    {
        public static readonly string[] Names = { "Same as the UI", "Classic", "Bahnschrift", "Impact", "Consolas", "Trebuchet MS", "Segoe UI", "Verdana", "Georgia", "Tahoma" };
    }

    public partial class Hud
    {
        /// <summary>(tests / the tabs) the Lobby category draws the SHIP LOBBY section without the timer (it has its own tab).</summary>
        bool m_HideTimerLooks;

        /// <summary>Settings > Display: the TIMER section.</summary>
        void DrawTimerLooks()
        {
            if (m_HideTimerLooks) return;
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
                SubHead("SIZE AND PLACE");
                Slider("Size", GameSettings.TimerSize, $"{GameSettings.TimerSize.Value * 100f:0}%", 0.05f);
                Slider("Plate width", GameSettings.TimerWidth, $"{GameSettings.TimerWidth.Value * 100f:0}%", 0.05f);
                Slider("Plate darkness", GameSettings.TimerPlate, $"{GameSettings.TimerPlate.Value * 100f:0}%", 0.05f);
                Slider("Height on screen", GameSettings.TimerY, GameSettings.TimerY.Value < 0.5f ? "top" : $"{GameSettings.TimerY.Value:0} px", 5f);
                SubHead("TEXT");
                Slider("Outline thickness", GameSettings.TimerInk, GameSettings.TimerInk.Value < 0.01f ? "none" : $"{GameSettings.TimerInk.Value * 100f:0}%", 0.05f);
                ChoiceRow("Font", GameSettings.TimerFont, true);
                ChoiceRow("TIME LEFT label", GameSettings.TimerLabelPos, false, "Left of the clock", "Above the clock");
                SubHead("PARTS");
                Toggle("Top line", GameSettings.TimerTopLine);
                Toggle("Drain bar", GameSettings.TimerBar);
                Toggle("Bar's grey ends", GameSettings.TimerBarBack);
                Toggle("What to do line", GameSettings.TimerSub);
                Toggle("Game mode tag", GameSettings.TimerTag);
                Toggle("Final minute flashing", GameSettings.TimerFlash);
                Toggle("Phase colours", GameSettings.TimerAccent);
                if (GameSettings.TimerAccent.Value)
                {
                    SubHead("PHASE COLOURS");
                    foreach (var (name, pref) in GameSettings.TimerColours) ColourPrefRow(name, pref);
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#bbbbbb>The timer at the top of the screen in a match. Phase colours: the label, the line and the bar in each part of the match (off: plain white).</color>", m_SmallWrap);
                if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetTimer();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>A small heading inside a section.</summary>
        void SubHead(string text)
        {
            GUILayout.Space(4 * m_Scale);
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * m_Scale);
            GUILayout.Label($"<color=#9ab8d8><b>{text}</b></color>", m_Small);
            GUILayout.EndHorizontal();
        }

        /// <summary>"Name  [a] [b] [c]..." - a DisplayPref.Choice's options as buttons (fonts: each written in its own font,
        /// the ones this PC hasn't got left out). `shown`: the buttons' words instead of the option names.</summary>
        void ChoiceRow(string name, DisplayPref.Choice p, bool fonts, params string[] shown)
        {
            float k = m_Scale, lw = 210 * k;
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            GUILayout.Label(name, m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
            GUILayout.BeginVertical();
            int col = 0, per = fonts ? 3 : 4;
            for (int i = 0; i < p.Names.Length; i++)
            {
                if (fonts && !GameSettings.FontPrefInstalled(i)) continue;
                if (col % per == 0) GUILayout.BeginHorizontal();
                var st = m_Choice;
                if (fonts && i > 0) st = new GUIStyle(m_Choice) { font = GameSettings.FontForPref(i) };
                string label = shown != null && i < shown.Length ? shown[i] : p.Names[i];
                bool on = GUILayout.Toggle(p.Value == i, label, st, GUILayout.Height(26 * k));
                TrackHover(GUILayoutUtility.GetLastRect());
                if (on && p.Value != i) { ClickSound(); p.Set(i); }
                if (col % per == per - 1) GUILayout.EndHorizontal();
                col++;
            }
            if (col % per != 0) GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        string m_ColourHexKey, m_ColourHexText;
        /// <summary>"Name  [swatch] [#RRGGBB] ↺" - a DisplayPref.Colour picked from a few presets or typed as hex.</summary>
        void ColourPrefRow(string name, DisplayPref.Colour p)
        {
            float k = m_Scale, lw = 210 * k;
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            GUILayout.Label(name, m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
            var cur = p.Value;
            float sw = 22 * k;
            var big = GUILayoutUtility.GetRect(40 * k, sw, GUILayout.Width(40 * k), GUILayout.Height(26 * k));
            big.y += (26 * k - sw) * 0.5f; big.height = sw;
            Fill(big, new Color(0.75f, 0.75f, 0.75f));
            Fill(new Rect(big.x + 2, big.y + 2, big.width - 4, big.height - 4), cur);
            GUILayout.Space(6 * k);
            foreach (var pc in s_PhasePresets)
            {
                var pr = GUILayoutUtility.GetRect(sw, sw, GUILayout.Width(sw), GUILayout.Height(26 * k));
                pr.y += (26 * k - sw) * 0.5f; pr.height = sw;
                Fill(pr, ColorSlots.Same(pc, cur) ? new Color(1f, 0.82f, 0.3f) : new Color(0.4f, 0.4f, 0.4f, 0.8f));
                Fill(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), pc);
                TrackHover(pr);
                if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) { ClickSound(); p.Set(pc); m_ColourHexKey = null; }
                GUILayout.Space(2 * k);
            }
            GUILayout.Space(6 * k);
            if (m_InkField == null) m_InkField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.MiddleCenter };
            string ctl = "hex." + p.Key;
            if (m_ColourHexKey == ctl && GUI.GetNameOfFocusedControl() != ctl) m_ColourHexKey = null; // (typing elsewhere: the colour's hex again)
            string hex = m_ColourHexKey == ctl ? m_ColourHexText : ColorUtility.ToHtmlStringRGB(cur);
            GUI.SetNextControlName(ctl);
            string nh = GUILayout.TextField(hex, 7, m_InkField, GUILayout.Width(80 * k), GUILayout.Height(24 * k));
            NoteTyping(ctl);
            if (nh != hex)
            {
                m_ColourHexKey = ctl; m_ColourHexText = nh;
                var t = nh.Trim().TrimStart('#');
                if (t.Length == 6 && ColorUtility.TryParseHtmlString("#" + t, out var hc)) p.Set(hc);
            }
            if (!ColorSlots.Same(cur, p.Default) && Btn("↺", GUILayout.Width(30 * k), GUILayout.Height(24 * k))) { p.Set(p.Default); m_ColourHexKey = null; }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        static readonly Color[] s_PhasePresets =
        {
            Color.white, new Color(1f, 0.85f, 0.3f), new Color(1f, 0.55f, 0.15f), new Color(1f, 0.25f, 0.2f), new Color(1f, 0.45f, 0.8f),
            new Color(0.7f, 0.45f, 1f), new Color(0.35f, 0.85f, 1f), new Color(0.45f, 1f, 0.45f),
        };
    }
}
