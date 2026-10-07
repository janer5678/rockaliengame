using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display, sorted into categories (a row of tabs at the top; only one category shows at a time):
    ///   GRAPHICS       - the screen (window, resolution, refresh rate), frame rate, shadows, shading
    ///   POST FX        - post processing, on the UI too (and the UI's own looks), the extra looks, hands and tools
    ///   WORLD          - grass, world colours, alien glow, tree X, base floor, beams, sky lines and the wall
    ///   HUD & TIMER    - the interface (font, scale, opacity, accent), the HUD's parts, the KILL FEED, the trajectory line, the TIMER
    ///   NOTIFICATIONS  - test button, style (outline, band, font, size, height), own look, wording (Hud.Settings.Notif.cs)
    ///   LOBBY          - the ship lobby's own post processing and room
    ///   MAIN MENU      - the main menu cutscene's own post processing and colours
    ///   SHARE          - COPY SETTINGS / PASTE SETTINGS (the display settings code, every category's settings)
    /// Every option the long page had is still here, each in one category. Hud.Menus.cs: DrawDisplayTab's body is just
    /// DrawDisplayTabbed() (the screen, frame rate, shadows, shading, interface and glow rows it used to draw are here).
    /// </summary>
    public partial class Hud
    {
        public enum DisplayCat { Graphics, PostFx, World, HudTimer, Notifications, Lobby, MainMenu, Share }
        public static readonly string[] DisplayCatNames = { "Graphics", "Post FX", "World", "HUD & Timer", "Notifications", "Lobby", "Main menu", "Share code" };
        DisplayCat m_DisplayCat;
        bool m_DisplayTabbed, m_FoldsOpened;
        int m_TabbedFrame = -10;

        /// <summary>(tests) show this Display category (by index, DisplayCat).</summary>
        public static void OpenDisplayCategory(int c)
        {
            if (s_I == null) return;
            s_I.m_DisplayCat = (DisplayCat)Mathf.Clamp(c, 0, DisplayCatNames.Length - 1);
            s_I.m_SettingsScroll = Vector2.zero;
        }
        /// <summary>(tests) the Display category showing now (-1: the categorised Display tab isn't being drawn).</summary>
        public static int DisplayCategoryShown => s_I != null && Time.frameCount - s_I.m_TabbedFrame <= 3 ? (int)s_I.m_DisplayCat : -1;

        /// <summary>Settings > Display: the category tabs, then the chosen category.</summary>
        void DrawDisplayTabbed()
        {
            float k = m_Scale;
            if (m_ResList == null) OnTabOpened();
            m_TabbedFrame = Time.frameCount;
            if (!m_FoldsOpened)
            {
                // (in a category of their own these start open: they're most of what the category is)
                m_FoldsOpened = true;
                m_TimerOpen = m_LobbyLooksOpen = m_MenuLooksOpen = m_NotifLooksOpen = true;
            }
            // the tabs: two rows of four
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * 4; i < row * 4 + 4 && i < DisplayCatNames.Length; i++)
                {
                    string name = DisplayCatNames[i];
                    if ((DisplayCat)i == DisplayCat.Notifications && NotifText.Changed > 0) name += $" <color=#ffd27a>·{NotifText.Changed}</color>";
                    if (Choice(m_DisplayCat == (DisplayCat)i, name, GUILayout.Height(30 * k), GUILayout.MinWidth(60 * k)))
                    {
                        m_DisplayCat = (DisplayCat)i;
                        m_SettingsScroll = Vector2.zero;
                        GUIUtility.keyboardControl = 0;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4 * k);
            m_DisplayTabbed = true;
            try
            {
                switch (m_DisplayCat)
                {
                    case DisplayCat.Graphics:
                        DrawScreenSection();
                        DrawFrameRateSection();
                        DrawShadowsAndShading();
                        break;
                    case DisplayCat.PostFx:
                        DrawPostFxSettings(); // (Hud.Picker.cs: the world's, on the UI too, the extra looks, hands and tools)
                        break;
                    case DisplayCat.World:
                        DrawWorldLook();       // (grass and world colours)
                        DrawAlienGlowSection();
                        DrawTreeXSettings();     // (TreeX.cs)
                        DrawBaseFloorSettings(); // (Hud.BaseFloor.cs)
                        DrawBeamSettings();      // (Hud.Beams.cs)
                        DrawSkyLinesAndWall();   // (Hud.SkyLines.cs)
                        break;
                    case DisplayCat.HudTimer:
                        DrawInterfaceSection();
                        DrawHudPartsSection();     // (Hud.Settings.Notif.cs)
                        DrawKillFeedLooks();       // (KillFeedLooks.cs)
                        DrawAimPreviewSettings();  // (PlayerController.Preview.cs: the trajectory line)
                        Caption("TIMER  ·  the top of the screen in a match");
                        DrawTimerLooks();          // (TimerLooks.cs)
                        break;
                    case DisplayCat.Notifications:
                        DrawNotifSettings();       // (Hud.Settings.Notif.cs)
                        break;
                    case DisplayCat.Lobby:
                        Caption("SHIP LOBBY  ·  the living room you wait in");
                        m_HideTimerLooks = true;   // (the timer has its own place, under HUD & Timer)
                        try { DrawTimerAndLobbyLooks(); } finally { m_HideTimerLooks = false; } // (LobbyLooks.cs)
                        break;
                    case DisplayCat.MainMenu:
                        Caption("MAIN MENU CUTSCENE  ·  the UFO in space behind the main menu");
                        DrawMenuCutsceneLooks();   // (Hud.Picker.cs: its post processing, then its colours - Hud.MenuColours.cs)
                        break;
                    case DisplayCat.Share:
                        DrawDisplayCode();         // (Hud.Menus.cs: COPY SETTINGS / PASTE SETTINGS)
                        GUILayout.Label("<color=#bbbbbb>The code holds every category's settings (not the screen's): send it to a friend, or keep it to get your look back.</color>", m_SmallWrap);
                        break;
                }
            }
            finally { m_DisplayTabbed = false; }
        }

        // ------------------------------------------------------------------ GRAPHICS

        void DrawScreenSection()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("SCREEN");
            GUILayout.BeginHorizontal();
            RowLabel("Window", lw);
            foreach (var mode in new[] { GameSettings.WindowMode.Fullscreen, GameSettings.WindowMode.Borderless, GameSettings.WindowMode.Windowed })
                if (Choice(m_ModeSel == mode, mode == GameSettings.WindowMode.Borderless ? "Borderless" : mode.ToString(), GUILayout.Height(30 * k))) m_ModeSel = mode;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            RowLabel("Resolution", lw);
            if (Btn("<", GUILayout.Width(40 * k), GUILayout.Height(30 * k))) { m_ResSel = Mathf.Min(m_ResList.Count - 1, m_ResSel + 1); m_RateSel = 0; }
            var res = m_ResList[Mathf.Clamp(m_ResSel, 0, m_ResList.Count - 1)];
            bool native = res.x == Screen.currentResolution.width && res.y == Screen.currentResolution.height;
            GUILayout.Label($"<b>{res.x} x {res.y}</b>{(native ? "  <color=#aaaaaa>(your screen)</color>" : "")}", m_Center, GUILayout.ExpandWidth(true), GUILayout.Height(30 * k));
            if (Btn(">", GUILayout.Width(40 * k), GUILayout.Height(30 * k))) { m_ResSel = Mathf.Max(0, m_ResSel - 1); m_RateSel = 0; }
            GUILayout.EndHorizontal();

            var rates = GameSettings.RefreshRates(res);
            m_RateSel = Mathf.Clamp(m_RateSel, 0, rates.Count - 1);
            GUILayout.BeginHorizontal();
            RowLabel("Refresh rate", lw);
            if (Btn("<", GUILayout.Width(40 * k), GUILayout.Height(30 * k))) m_RateSel = Mathf.Min(rates.Count - 1, m_RateSel + 1);
            GUILayout.Label($"<b>{rates[m_RateSel].value:0.##} Hz</b>{(m_RateSel == 0 ? "  <color=#aaaaaa>(highest)</color>" : "")}", m_Center, GUILayout.ExpandWidth(true), GUILayout.Height(30 * k));
            if (Btn(">", GUILayout.Width(40 * k), GUILayout.Height(30 * k))) m_RateSel = Mathf.Max(0, m_RateSel - 1);
            GUILayout.EndHorizontal();

            GUILayout.Space(8 * k);
            GUILayout.BeginHorizontal();
            if (Btn("Apply", m_Primary, GUILayout.Height(38 * k))) GameSettings.SetDisplay(res, m_ModeSel, rates[m_RateSel]);
            if (Btn("Use my screen's best", GUILayout.Height(38 * k)))
            {
                m_ResSel = Mathf.Max(0, m_ResList.IndexOf(new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height)));
                m_RateSel = 0;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"<color=#bbbbbb>Now: {Screen.width} x {Screen.height}, {GameSettings.CurrentMode}, {GameSettings.ChosenRate.value:0.##} Hz. The refresh rate starts at the highest your screen can do; in a window it follows your desktop.</color>", m_SmallWrap);
        }

        void DrawFrameRateSection()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("FRAME RATE");
            // (the PSX test looks are hidden for now - GameSettings.ShowGraphicsPicker)
            if (GameSettings.ShowGraphicsPicker)
            {
                GUILayout.BeginHorizontal();
                RowLabel("Style", lw);
                bool inMatch = NetGame.Instance != null && NetGame.Instance.IsSpawned;
                GUI.enabled = !inMatch;
                if (Choice(GameSettings.GraphicsMode == 0, "Normal", GUILayout.Height(30 * k))) GameSettings.SetGraphics(0);
                if (Choice(GameSettings.GraphicsMode == 1, "PSX", GUILayout.Height(30 * k))) GameSettings.SetGraphics(1);
                if (Choice(GameSettings.GraphicsMode == 2, "AI PSX TEST", GUILayout.Height(30 * k))) GameSettings.SetGraphics(2);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (inMatch) GUILayout.Label("<color=#bbbbbb>Picked by the host for everyone in this match.</color>", m_SmallWrap);
            }
            GUILayout.BeginHorizontal();
            RowLabel("Uncapped", lw);
            bool unc = ToggleBtn(GameSettings.UncappedFps, GameSettings.UncappedFps ? "On" : "Off", GUILayout.Width(90 * k), GUILayout.Height(30 * k));
            if (unc != GameSettings.UncappedFps) GameSettings.SetUncappedFps(unc);
            GUILayout.Label("<color=#bbbbbb>  vsync off, no frame cap - as fast as your PC can go</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#bbbbbb>(the FPS counter is under HUD & Timer)</color>", m_SmallWrap);
        }

        void DrawShadowsAndShading()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("SHADOWS  ·  just on this PC");
            float ss = SliderRow("Shadow darkness", GameSettings.ShadowStrength, 0f, 1f, $"{GameSettings.ShadowStrength * 100f:0}%", lw);
            float sd = SliderRow("Shadow distance", GameSettings.ShadowDistance, GameSettings.ShadowDistanceMin, GameSettings.ShadowDistanceMax, $"{GameSettings.ShadowDistance:0} m", lw);
            GameSettings.SetShadows(Mathf.Round(ss * 20f) / 20f, Mathf.Round(sd / 5f) * 5f);
            GUILayout.Label("<color=#bbbbbb>Darkness: how dark the sun's shadows are (0% = none). Distance: how far from you shadows are drawn - further looks better, nearer is faster and the near shadows are sharper.</color>", m_SmallWrap);

            Caption("SHADING  ·  just on this PC");
            GUILayout.BeginHorizontal();
            RowLabel("Shade smooth", lw);
            bool sh = ToggleBtn(GameSettings.SmoothHands.Value, "Hands & items", GUILayout.Width(150 * k), GUILayout.Height(30 * k));
            GUILayout.Space(6 * k);
            bool sa = ToggleBtn(GameSettings.SmoothAliens.Value, "Aliens & crowd", GUILayout.Width(150 * k), GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GameSettings.SmoothHands.Set(sh);
            GameSettings.SmoothAliens.Set(sa);
            GUILayout.Label("<color=#bbbbbb>Lit smoothly instead of in flat facets: your first-person hands and what they hold, and the alien players and the stadium crowd (off = the normal look).</color>", m_SmallWrap);
        }

        // ------------------------------------------------------------------ HUD & TIMER, WORLD

        /// <summary>INTERFACE: the font, UI scale, HUD opacity and accent colour.</summary>
        void DrawInterfaceSection()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("INTERFACE  ·  just on this PC");
            int font = GameSettings.UiFont;
            GUILayout.BeginHorizontal();
            RowLabel("Font", lw);
            GUILayout.BeginVertical();
            int col = 0;
            for (int i = 0; i < GameSettings.FontChoices.Length; i++)
            {
                if (!GameSettings.FontInstalled(i)) continue;
                if (col % 3 == 0) GUILayout.BeginHorizontal();
                var fst = new GUIStyle(m_Choice) { font = UiLook.FontFor(i) };
                bool pick = GUILayout.Toggle(font == i, GameSettings.FontChoices[i], fst, GUILayout.Height(30 * k));
                TrackHover(GUILayoutUtility.GetLastRect());
                if (pick && font != i) { ClickSound(); font = i; }
                if (col % 3 == 2) GUILayout.EndHorizontal();
                col++;
            }
            if (col % 3 != 0) GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(lw + 4 * k);
            GUILayout.Label($"<color=#bbbbbb>{GameSettings.FontChoices[font]}: {GameSettings.FontBlurbs[font]}.</color>", m_SmallWrap);
            GUILayout.EndHorizontal();
            // (the UI scale goes in when the slider is let go, so the panel doesn't change size under the mouse)
            float shownScale = m_PendingScale > 0f ? m_PendingScale : GameSettings.UiScale;
            float scale = SliderRow("UI scale", shownScale, GameSettings.UiScaleMin, GameSettings.UiScaleMax, $"{shownScale * 100f:0}%", lw);
            if (!Mathf.Approximately(scale, shownScale)) m_PendingScale = Mathf.Round(scale * 20f) / 20f;
            if (m_PendingScale > 0f && GUIUtility.hotControl == 0) { scale = m_PendingScale; m_PendingScale = -1f; }
            else scale = GameSettings.UiScale;
            float hud = SliderRow("HUD opacity", GameSettings.HudOpacity, GameSettings.HudOpacityMin, 1f, $"{GameSettings.HudOpacity * 100f:0}%", lw);
            int accent = GameSettings.UiAccent;
            GUILayout.BeginHorizontal();
            RowLabel("Accent colour", lw);
            for (int i = 0; i < GameSettings.AccentChoices.Length; i++)
            {
                var cell = GUILayoutUtility.GetRect(30 * k, 30 * k, GUILayout.Width(30 * k), GUILayout.Height(30 * k));
                Fill(cell, accent == i ? Color.white : new Color(0, 0, 0, 0.6f));
                Fill(new Rect(cell.x + 3, cell.y + 3, cell.width - 6, cell.height - 6), GameSettings.AccentChoices[i]);
                if (BtnAt(cell, "", GUIStyle.none)) accent = i;
                GUILayout.Space(4 * k);
            }
            GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(GameSettings.AccentChoices[accent])}>  {GameSettings.AccentNames[accent]}</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.EndHorizontal();
            GameSettings.SetInterface(font, scale, Mathf.Round(hud * 20f) / 20f, accent);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>HUD opacity: the in-game HUD (bars, hotbar, timer) - the menus and the inventory stay solid.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.SetInterface(DisplayDefaults.UiFont, DisplayDefaults.UiScale, DisplayDefaults.HudOpacity, DisplayDefaults.UiAccent);
            GUILayout.EndHorizontal();
        }

        void DrawAlienGlowSection()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("ALIEN GLOW  ·  just on this PC");
            float st = SliderRow("Glow strength", Cfg.AlienOutlineStrength, 0.02f, 1f, $"{Cfg.AlienOutlineStrength * 100f:0}%", lw);
            float wd = SliderRow("Glow thickness", Cfg.AlienOutlineWidth, 0.005f, 0.12f, $"{Cfg.AlienOutlineWidth * 100f:0.0} cm", lw);
            GameSettings.SetAlienGlow(st, wd);
            GUILayout.Label("<color=#bbbbbb>Enemies always have a faint glow in their team colour so they're easier to spot (every mode).</color>", m_SmallWrap);
        }
    }
}
