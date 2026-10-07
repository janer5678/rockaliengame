using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > NOTIFICATIONS (TRY IT, STYLE, OWN LOOK, WORDING) and HUD & TIMER's HUD parts, and the test
    /// notification: a sample banner or end countdown drawn on top of everything (the settings and the menus too), so the
    /// look can be tried from anywhere.
    /// </summary>
    public partial class Hud
    {
        /// <summary>The banner showing is a test one (drawn on top of everything by DrawTestNotification, not by the HUD).</summary>
        static bool s_BannerTest;
        static float s_TestBannerAt = -10f, s_TestCountAt = -10f;
        static int s_TestCycle, s_TestCountLast = -1;
        /// <summary>(tests) the frame a banner / the end countdown / the YOU ARE box was last drawn.</summary>
        public static int BannerShownFrame = -10, CountdownShownFrame = -10, TeamBoxShownFrame = -10;

        static readonly (string title, string sub)[] s_TestBanners =
        {
            ("AIRDROP INCOMING", "A crate is coming down by the north rocks"),
            ("TRADE STATION UNLOCKED", "Craft it in your bag"),
            ("THE WALL IS DOWN", "Grab the ball from the middle and put it in YOUR machine's socket!"),
        };

        /// <summary>TEST NOTIFICATION (also for the tests): the next sample banner (in the player's own words if they've
        /// reworded it), on top of everything. Returns the game's own title for it.</summary>
        public static string TestNotification()
        {
            var (title, sub) = s_TestBanners[s_TestCycle % s_TestBanners.Length];
            s_TestCycle++;
            Banner(title, sub);
            s_BannerTest = true;
            s_TestBannerAt = Time.unscaledTime;
            s_TestCountAt = -10f;
            s_TestFightAt = -10f;
            return title;
        }

        /// <summary>TEST COUNTDOWN (also for the tests): the end countdown from 5, on top of everything.</summary>
        public static void TestCountdownNow()
        {
            s_TestCountAt = Time.unscaledTime;
            s_TestCountLast = -1;
            s_BannerTest = false;
            s_TestFightAt = -10f;
        }

        /// <summary>A test notification, if one's going (end of OnGUI: over the HUD, the menus and the settings).</summary>
        void DrawTestNotification()
        {
            // the test death screen (DeathLooks.cs): only while Settings > Display is up - it goes when they're closed
            if (TestDeathScreen)
            {
                if (DisplayCategoryShown < 0) TestDeathScreen = false;
                else if (Event.current.type == EventType.Repaint) DrawTestDeath(m_Scale);
            }
            float bage = Time.unscaledTime - s_TestBannerAt, cage = Time.unscaledTime - s_TestCountAt, fage = Time.unscaledTime - s_TestFightAt;
            bool banner = s_BannerTest && bage >= 0f && bage < GameSettings.NotifTimeNow, count = cage >= 0f && cage < 5f, fight = fage >= 0f && fage < 4.2f;
            if (!banner && !count && !fight) return;
            float k = m_Scale;
            bool layer = UiLook.BeginNotif(out var prev);
            if (fight) DrawTestFightWords(k); // (CountdownLooks.cs)
            else if (count)
            {
                int n = 5 - Mathf.FloorToInt(cage);
                if (n != s_TestCountLast) { s_TestCountLast = n; Sfx.PlayUi(n <= 3 ? Sfx.Ding : Sfx.Beep, n <= 3 ? 0.7f : 0.5f); }
                DrawEndCountdown(n, 1f - (cage - Mathf.Floor(cage)), "YOU WIN IN", false, k);
                CountdownShownFrame = Time.frameCount;
            }
            else
            {
                DrawBannerFx(s_BannerTitle, s_BannerSub, bage, k);
                BannerShownFrame = Time.frameCount;
            }
            if (layer) UiLook.EndNotif(prev);
        }

        /// <summary>Settings > Display > NOTIFICATIONS.</summary>
        void DrawNotifSettings()
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
            // ---- try it ----
            Caption("TRY IT");
            GUILayout.BeginHorizontal();
            string next = s_TestBanners[s_TestCycle % s_TestBanners.Length].title;
            if (Btn($"Test notification  <color=#aaaaaa>({NotifText.Map(next)})</color>", m_Primary, GUILayout.Height(34 * k))) TestNotification();
            if (Btn("Test countdown", GUILayout.Width(170 * k), GUILayout.Height(34 * k))) TestCountdownNow();
            GUILayout.EndHorizontal();
            Hint("Shows over everything - the game, the menus and these settings - so you can see each change.");

            // ---- style ----
            Caption("STYLE  ·  the banners (the countdowns have their own: COUNTDOWN below)");
            Slider("Size", GameSettings.NotifSize, $"{GameSettings.NotifSize.Value * 100f:0}%", 0.05f);
            Slider("Band width", GameSettings.NotifWidth, $"{GameSettings.NotifWidth.Value * 100f:0}%", 0.05f);
            Slider("Band darkness", GameSettings.NotifPlate, GameSettings.NotifPlate.Value < 0.01f ? "none" : $"{GameSettings.NotifPlate.Value * 100f:0}%", 0.05f);
            Slider("Height on screen", GameSettings.NotifY, Mathf.Abs(GameSettings.NotifY.Value) < 0.5f ? "as usual" : $"{(GameSettings.NotifY.Value > 0 ? "+" : "")}{GameSettings.NotifY.Value:0} px", 5f);
            ChoiceRow("Font", GameSettings.NotifFont, true);
            // the words' own ink stroke (InkText) - not OWN LOOK's outline, which is an extra line laid round everything after
            SubHead("TEXT OUTLINE  ·  the words' own stroke");
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            GUILayout.Label("Text outline", m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
            GameSettings.NotifInkOn.Set(ToggleBtn(GameSettings.NotifInkOn.Value, GameSettings.NotifInkOn.Value ? "On" : "Off", GUILayout.Width(80 * k), GUILayout.Height(26 * k)));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (GameSettings.NotifInkOn.Value)
            {
                Slider("Text outline thickness", GameSettings.NotifInk, GameSettings.NotifInk.Value < 0.01f ? "none" : $"{GameSettings.NotifInk.Value * 100f:0}%", 0.05f);
                ColourPrefRow("Text outline colour", GameSettings.NotifInkColour, s_InkPresets);
                Slider("Text outline opacity", GameSettings.NotifInkOpacity, $"{GameSettings.NotifInkOpacity.Value * 100f:0}%", 0.05f);
            }
            Hint("The ink stroke drawn round the banner's and the countdown's own letters. (OWN LOOK's Outlines below is a separate, extra line laid round everything afterwards.) Double-click a colour square for the colour wheel.");
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>The big messages in the middle of the screen. Height: up (left) or down (right) from where they usually sit.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetNotifStyle();
            GUILayout.EndHorizontal();

            // ---- how they come and go ----
            Caption("ANIMATION  ·  how a banner comes in and goes");
            ChoiceRow("Comes in", GameSettings.NotifEnter, false, "Snap open", "Slide down", "Fade in", "Pop", "Just appears");
            ChoiceRow("Goes", GameSettings.NotifExit, false, "Lift & fade", "Fade", "Slide up", "Shrink");
            Slider("Stays for", GameSettings.NotifTime, $"{GameSettings.NotifTime.Value:0.#} s", 0.25f);
            Slider("Flies up as it goes", GameSettings.NotifFly, GameSettings.NotifFly.Value < 0.5f ? "not at all" : $"{GameSettings.NotifFly.Value:0} px", 5f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>Flies up: how far it moves up the screen on its way out (Lift & fade and Slide up).</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetNotifMove();
            GUILayout.EndHorizontal();

            // ---- each kind's colour ----
            Caption("COLOURS  ·  each kind of news");
            foreach (var (name, covers, pref) in GameSettings.NotifColours)
            {
                ColourPrefRow(name, pref);
                GUILayout.BeginHorizontal();
                GUILayout.Space(32 * k);
                GUILayout.Label($"<color=#999999>{covers}</color>", m_Small);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>The lines along the band and the colour the title settles into. Double-click a colour square for the colour wheel.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetNotifColours();
            GUILayout.EndHorizontal();

            // ---- the countdowns: a look of their own ----
            DrawCountdownLooks(); // (CountdownLooks.cs)

            // ---- their own post processing ----
            Caption("OWN LOOK  ·  post processing just for them");
            if (!GameSettings.PostFx) Hint("Only while post processing is on (Post FX).");
            DrawNotifOwnLooks(); // (Hud.Picker.cs)

            // ---- wording ----
            Caption("WORDING  ·  what each one says");
            Hint("Type your own words for any of them (empty = the game's own). {0} is the part that changes - a team, or a number of seconds.");
            if (m_NotifField == null || !Mathf.Approximately(m_NotifFieldK, k))
            {
                m_NotifFieldK = k;
                m_NotifField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(14 * k), alignment = TextAnchor.MiddleLeft, padding = new RectOffset(Mathf.RoundToInt(6 * k), 4, 2, 2) };
            }
            int li = 0;
            foreach (var line in System.Linq.Enumerable.Concat(NotifText.All, NotifText.Timer))
            {
                if (li++ == NotifText.All.Length) SubHead("THE TIMER'S HEADERS  ·  top of the screen");
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                GUILayout.Label($"<b>{line.Default}</b>\n<color=#999999>{line.Where}</color>", m_SmallWrap, GUILayout.Width(lw), GUILayout.MinHeight(34 * k));
                string ctl = "notiftext." + line.Key;
                GUI.SetNextControlName(ctl);
                string cur = line.Own.Value;
                string nv = GUILayout.TextField(cur, 48, m_NotifField, GUILayout.ExpandWidth(true), GUILayout.Height(26 * k));
                NoteTyping(ctl);
                if (nv != cur) line.Own.Set(nv.Replace("\n", " "));
                bool set = !string.IsNullOrWhiteSpace(line.Own.Value);
                GUI.enabled = set;
                if (Btn("↺", GUILayout.Width(30 * k), GUILayout.Height(26 * k))) { line.Own.Set(""); GUIUtility.keyboardControl = 0; }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            int changed = NotifText.Changed;
            GUILayout.Label(changed > 0 ? $"<color=#ffd27a>{changed} reworded</color>" : "<color=#bbbbbb>All in the game's own words.</color>", m_SmallWrap);
            if (Btn("Reset wording", GUILayout.Width(150 * k), GUILayout.Height(28 * k))) { NotifText.ResetAll(); GUIUtility.keyboardControl = 0; }
            GUILayout.EndHorizontal();
        }

        GUIStyle m_NotifField;
        float m_NotifFieldK;

        /// <summary>HUD & TIMER: which parts of the HUD show.</summary>
        void DrawHudPartsSection()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("HUD");
            GUILayout.BeginHorizontal();
            RowLabel("Team box", lw);
            bool box = ToggleBtn(GameSettings.HudTeamBox.Value, GameSettings.HudTeamBox.Value ? "On" : "Off", GUILayout.Width(90 * k), GUILayout.Height(30 * k));
            GameSettings.HudTeamBox.Set(box);
            GUILayout.Label("<color=#bbbbbb>  \"YOU ARE BLUE\" in the top left</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            RowLabel("Base radar", lw);
            bool radar = ToggleBtn(GameSettings.HudBaseRadar.Value, GameSettings.HudBaseRadar.Value ? "On" : "Off", GUILayout.Width(90 * k), GUILayout.Height(30 * k));
            GameSettings.HudBaseRadar.Set(radar);
            GUILayout.Label(GameSettings.HudTeamBox.Value ? "<color=#bbbbbb>  \"Your Base\" arrow, under the team box</color>" : "<color=#bbbbbb>  \"Your Base\" arrow, up in the corner (no team box)</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            RowLabel("FPS counter", lw);
            bool fps = ToggleBtn(GameSettings.ShowFps, GameSettings.ShowFps ? "On" : "Off", GUILayout.Width(90 * k), GUILayout.Height(30 * k));
            if (fps != GameSettings.ShowFps) GameSettings.SetShowFps(fps);
            GUILayout.Label("<color=#bbbbbb>  frames per second, top left</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
