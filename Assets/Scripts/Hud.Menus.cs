using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Main menu, mode options, CHANGE VALUES (every game stat), settings (sound, controls, display, voice), pause and dev menus.</summary>
    public partial class Hud
    {
        enum MenuPage { Main, ModeOptions, Values, Settings }
        enum PausePage { Root, Settings, Dev }
        enum SettingsTab { Sound, Controls, Display, Voice }

        static Hud s_I;
        MenuPage m_Page;
        PausePage m_PausePage;
        SettingsTab m_Tab;
        bool m_ShowPort, m_ShowModes, m_ShowMoreRules, m_ShowMoreMaps;
        Vector2 m_MenuScroll, m_ValuesScroll, m_SettingsScroll;
        readonly Dictionary<string, string> m_EditBuffers = new Dictionary<string, string>();
        string m_FileMsg = "";
        float m_FileMsgTime = -10f;

        /// <summary>Screenshot tests: open these pages from code.</summary>
        public static bool OpenModeOptions, OpenValues;
        public static int OpenSettingsTab = -1, OpenPause = -1;

        void Awake() => s_I = this;

        // ------------------------------------------------------------------ keys (rebinding, Esc)

        bool m_Rebinding, m_RebindAlt;
        Bind m_RebindWhat;
        int m_RebindFrame = -1;

        /// <summary>Waiting for a key (or one was just taken this frame): the game ignores the key presses.</summary>
        public static bool Rebinding => s_I != null && (s_I.m_Rebinding || s_I.m_RebindFrame == Time.frameCount);

        void MenuUpdate()
        {
            if (m_Rebinding && Time.frameCount > m_RebindFrame)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { m_Rebinding = false; m_RebindFrame = Time.frameCount; }
                else
                {
                    var key = Binds.Pressed();
                    if (key == KeyCode.Backspace || key == KeyCode.Delete) { Binds.Set(m_RebindWhat, m_RebindAlt, KeyCode.None); m_Rebinding = false; ClickSound(); }
                    else if (key != KeyCode.None) { Binds.Set(m_RebindWhat, m_RebindAlt, key); m_Rebinding = false; ClickSound(); }
                    if (!m_Rebinding) m_RebindFrame = Time.frameCount;
                }
            }
            // a colour picked by dragging is saved once the mouse is let go (even if the menu closed mid-drag)
            if (m_PickerDirty && !PickerMouseHeld) { m_PickerDrag = 0; SavePick(); }
            // Esc on the main menu goes back a page
            var boot = Bootstrap.I;
            if (boot != null && !boot.InSession && Input.GetKeyDown(KeyCode.Escape) && !m_Rebinding && m_RebindFrame != Time.frameCount && m_Page != MenuPage.Main)
            {
                if (DropTyping()) return; // (Esc in a search box lets go of it first)
                if (m_Page == MenuPage.Settings && m_ColourScreen) { m_ColourScreen = false; ClickSound(); return; } // (the colour screen: back to Display)
                m_Page = MenuPage.Main;
                ClickSound();
            }
        }

        /// <summary>Esc in a match: cancels a rebind or goes back to the pause menu. True if it was used up here.</summary>
        public static bool BackOut()
        {
            if (s_I == null) return false;
            if (DropTyping()) return true; // typing in a search box: Esc lets go of it first
            if (s_I.m_Rebinding || s_I.m_RebindFrame == Time.frameCount) { s_I.m_Rebinding = false; return true; }
            if (s_I.m_PausePage == PausePage.Settings && s_I.m_ColourScreen) { s_I.m_ColourScreen = false; ClickSound(); return true; } // (back to Display)
            if (s_I.m_PausePage != PausePage.Root) { s_I.m_PausePage = PausePage.Root; ClickSound(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ main menu

        void DrawMainMenu(Bootstrap boot)
        {
            if (OpenModeOptions) { OpenModeOptions = false; m_Page = MenuPage.ModeOptions; }
            if (OpenValues) { OpenValues = false; m_Page = MenuPage.Values; m_Open.Add("v:Match"); m_Open.Add("v:Player"); }
            if (OpenSettingsTab >= 0) { m_Tab = (SettingsTab)OpenSettingsTab; OpenSettingsTab = -1; m_Page = MenuPage.Settings; OnTabOpened(); }
            switch (m_Page)
            {
                case MenuPage.ModeOptions: DrawModeOptions(boot); return;
                case MenuPage.Values: DrawValues(); return;
                case MenuPage.Settings: if (DrawSettingsPanel()) m_Page = MenuPage.Main; return;
            }
            float k = m_Scale;
            float w = 540 * k, h = Mathf.Min(Screen.height - 20, 860 * k);
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(r, new Color(0, 0, 0, 0.72f));
            GUILayout.BeginArea(new Rect(r.x + 20 * k, r.y + 14 * k, r.width - 40 * k, r.height - 24 * k));
            m_MenuScroll = GUILayout.BeginScrollView(m_MenuScroll, GUIStyle.none, GUIStyle.none);
            GUILayout.Label("<b>ROCK BASE BRAWL</b>", m_Big);
            GUILayout.Label("1v1 · gather · build · raid · steal the ball", m_Center);

            int key = Bootstrap.MapChoice;
            var mode = (GameMode)((key >> Cfg.ModeShift) & Cfg.ModeMask);
            bool wood = (key & Cfg.WoodBit) != 0;
            var size = (key & Cfg.SmallBit) != 0 ? MapSize.Small : (MapSize)((key >> Cfg.SizeShift) & 3);
            var kind = (MapKind)(key & 15);

            Caption("MATCH SETUP  (the host picks these)");
            // players: one button that folds open the eight modes
            GUILayout.BeginHorizontal();
            RowLabel("Players");
            if (Btn($"<b>{ModeTitle(mode)}</b>      {(m_ShowModes ? "▲" : "▼")}", GUILayout.Height(32 * k))) m_ShowModes = !m_ShowModes;
            GUILayout.EndHorizontal();
            if (m_ShowModes)
            {
                var rows = new[]
                {
                    new[] { GameMode.Duel, GameMode.Teams, GameMode.Teams3, GameMode.Teams4 },
                    new[] { GameMode.Ffa3, GameMode.Ffa4, GameMode.Trio, GameMode.Quad },
                };
                for (int row = 0; row < rows.Length; row++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(row == 0 ? "<color=#aaaaaa>red vs blue</color>" : "<color=#aaaaaa>3-4 bases</color>", m_Small, GUILayout.Width(100 * k), GUILayout.Height(30 * k));
                    foreach (var gm in rows[row])
                        if (Choice(mode == gm, ModeShort(gm), GUILayout.Height(30 * k)))
                        {
                            boot.SetMapChoice((key & ~(Cfg.ModeMask << Cfg.ModeShift)) | ((int)gm << Cfg.ModeShift));
                            m_ShowModes = false;
                        }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.BeginHorizontal();
            GUILayout.Space(103 * k);
            GUILayout.Label($"<color=#bbbbbb>{ModeDesc(mode)}</color>", m_SmallWrap);
            GUILayout.EndHorizontal();

            // game mode: separate ways to play (they don't mix)
            key = Bootstrap.MapChoice;
            var rules = (GameRules)Mathf.Clamp((key >> Cfg.RulesShift) & Cfg.RulesMask, 0, (int)GameRules.Dna);
            int noRules = key & ~(Cfg.RulesMask << Cfg.RulesShift);
            // Tutorial, Classic (the Auto Wood rules) and Primitive (the original game); the rest fold out under "More modes"
            bool moreRule = System.Array.IndexOf(Cfg.MoreRules, rules) >= 0;
            GUILayout.BeginHorizontal();
            RowLabel("Game mode");
            foreach (var gr in Cfg.MainRules)
                if (Choice(rules == gr, Cfg.RulesName(gr), GUILayout.Height(30 * k))) { boot.SetMapChoice(noRules | ((int)gr << Cfg.RulesShift)); m_ShowMoreRules = false; }
            string moreLabel = (moreRule ? $"<color=#{ColorUtility.ToHtmlStringRGB(GameSettings.AccentColor)}>{MoreRuleLabel(rules)}</color>" : "More modes") + (m_ShowMoreRules ? "  ▲" : "  ▼");
            if (Btn(moreLabel, GUILayout.Height(30 * k))) m_ShowMoreRules = !m_ShowMoreRules;
            GUILayout.EndHorizontal();
            if (m_ShowMoreRules)
                for (int row = 0; row < Cfg.MoreRules.Length; row += 3)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(103 * k);
                    for (int i = row; i < row + 3 && i < Cfg.MoreRules.Length; i++)
                    {
                        var gr = Cfg.MoreRules[i];
                        if (Choice(rules == gr, MoreRuleLabel(gr), GUILayout.Height(30 * k))) { boot.SetMapChoice(noRules | ((int)gr << Cfg.RulesShift)); m_ShowMoreRules = false; }
                    }
                    GUILayout.EndHorizontal();
                }
            GUILayout.BeginHorizontal();
            GUILayout.Space(103 * k);
            GUILayout.Label($"<color=#ffd24a>{Cfg.RulesDesc(rules)}</color>", m_SmallWrap);
            GUILayout.EndHorizontal();

            // game length: how long behind the glass wall, then how long with the ball (fun modes: no wall, just the match)
            bool funPick = rules == GameRules.Fun || rules == GameRules.FunRandom || rules == GameRules.FunRandomLimited;
            GUILayout.BeginHorizontal();
            RowLabel("Game length");
            if (funPick)
            {
                float funT = Cfg.FunMatchLength;
                if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.FunMatchLength = Mathf.Max(60f, funT - 60f); Cfg.SavePrefs(); }
                GUILayout.Label($"<b>{Clock(Cfg.FunMatchLength)}</b>", m_Center, GUILayout.Width(70 * k), GUILayout.Height(30 * k));
                if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.FunMatchLength = Mathf.Min(3600f, funT + 60f); Cfg.SavePrefs(); }
                GUILayout.Label($"<size={Mathf.RoundToInt(12 * k)}>item every</size>", m_Center, GUILayout.Width(66 * k), GUILayout.Height(30 * k));
                float every = Cfg.FunItemInterval;
                if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.FunItemInterval = Mathf.Max(5f, every - 5f); Cfg.SavePrefs(); }
                GUILayout.Label($"<b>{Cfg.FunItemInterval:0}s</b>", m_Center, GUILayout.Width(48 * k), GUILayout.Height(30 * k));
                if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.FunItemInterval = Mathf.Min(600f, every + 5f); Cfg.SavePrefs(); }
            }
            else
            {
                float wallT = Cfg.BallDropDelay, matchT = Cfg.MatchLength;
                if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.BallDropDelay = Mathf.Max(30f, wallT - 30f); Cfg.SavePrefs(); }
                GUILayout.Label($"<b>{Clock(Cfg.BallDropDelay)}</b> <size={Mathf.RoundToInt(12 * k)}>build</size>", m_Center, GUILayout.Width(96 * k), GUILayout.Height(30 * k));
                if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.BallDropDelay = Mathf.Min(1800f, wallT + 30f); Cfg.SavePrefs(); }
                GUILayout.Space(8 * k);
                if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.MatchLength = Mathf.Max(60f, matchT - 60f); Cfg.SavePrefs(); }
                GUILayout.Label($"<b>{Clock(Cfg.MatchLength)}</b> <size={Mathf.RoundToInt(12 * k)}>ball</size>", m_Center, GUILayout.Width(96 * k), GUILayout.Height(30 * k));
                if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.MatchLength = Mathf.Min(3600f, matchT + 60f); Cfg.SavePrefs(); }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(103 * k);
            GUILayout.Label(funPick
                ? $"<color=#bbbbbb>No building phase: the wall is down and the ball is in from the start. {Clock(Cfg.FunMatchLength)} match (plus sudden death), first free item straight away, then one every {Cfg.FunItemInterval:0}s (into the back of your inventory).</color>"
                : $"<color=#bbbbbb>{Clock(Cfg.BallDropDelay)} to gather and build behind the glass wall, then {Clock(Cfg.MatchLength)} with the ball - {Clock(Cfg.BallDropDelay + Cfg.MatchLength)} in all (plus sudden death).</color>", m_SmallWrap);
            GUILayout.EndHorizontal();

            key = Bootstrap.MapChoice;
            int flags = key & ~15;
            // the tutorial always plays on the small Plains map: show that, and lock these rows
            bool tutMap = rules == GameRules.Tutorial;
            if (tutMap)
            {
                kind = MapKind.Plains;
                size = MapSize.Small;
                wood = Cfg.WoodIsNormal;
                GUILayout.BeginHorizontal();
                GUILayout.Space(103 * k);
                GUILayout.Label("<color=#ffd24a>Tutorial: always the small, flat Plains map.</color>", m_SmallWrap);
                GUILayout.EndHorizontal();
                GUI.enabled = false;
            }
            // Plains and Highlands; the theme maps fold out under "More maps"
            bool themePicked = kind >= MapKind.Beach;
            GUILayout.BeginHorizontal();
            RowLabel("Map");
            if (Choice(kind == MapKind.Plains, "Plains", GUILayout.Height(30 * k))) { boot.SetMapChoice((int)MapKind.Plains | flags); m_ShowMoreMaps = false; }
            if (Choice(kind == MapKind.Highlands, "Highlands (wild)", GUILayout.Height(30 * k))) { boot.SetMapChoice((int)MapKind.Highlands | flags); m_ShowMoreMaps = false; }
            string mapsLabel = (themePicked ? $"<color=#{ColorUtility.ToHtmlStringRGB(GameSettings.AccentColor)}>{ThemeMaps.Label(kind)}</color>" : "More maps") + (m_ShowMoreMaps ? "  ▲" : "  ▼");
            if (Btn(mapsLabel, GUILayout.Height(30 * k))) m_ShowMoreMaps = !m_ShowMoreMaps;
            GUILayout.EndHorizontal();
            // THEME MAPS: the five extra maps
            if (m_ShowMoreMaps)
                for (int tr = 0; tr < ThemeMaps.Kinds.Length; tr += 3)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(103 * k);
                    for (int ti = tr; ti < tr + 3 && ti < ThemeMaps.Kinds.Length; ti++)
                    {
                        var tk = ThemeMaps.Kinds[ti];
                        if (Choice(kind == tk, ThemeMaps.Label(tk), GUILayout.Height(30 * k))) { boot.SetMapChoice((int)tk | flags); m_ShowMoreMaps = false; }
                    }
                    GUILayout.EndHorizontal();
                }
            if (themePicked)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(103 * k);
                GUILayout.Label($"<color=#bbbbbb>{ThemeMaps.Blurb(kind)}</color>", m_SmallWrap);
                GUILayout.EndHorizontal();
            }
            // END THEME MAPS

            GUILayout.BeginHorizontal();
            RowLabel("Size");
            foreach (var sz in new[] { MapSize.Small, MapSize.Big, MapSize.Large, MapSize.Huge })
                if (Choice(size == sz, Cfg.SizeLabel(sz), GUILayout.Height(30 * k)))
                    boot.SetMapChoice((key & ~Cfg.SmallBit & ~(3 << Cfg.SizeShift)) | ((int)sz << Cfg.SizeShift));
            GUILayout.EndHorizontal();

            // materials: wood is the normal game now (no row; the old stone materials are kept in the code - Cfg.WoodIsNormal)
            if (!Cfg.WoodIsNormal)
            {
                GUILayout.BeginHorizontal();
                RowLabel("Materials");
                if (Choice(!wood, "Normal", GUILayout.Height(30 * k))) boot.SetMapChoice(key & ~Cfg.WoodBit);
                if (Choice(wood, "Wood mode", GUILayout.Height(30 * k))) boot.SetMapChoice(key | Cfg.WoodBit);
                GUILayout.EndHorizontal();
                if (wood) GUILayout.Label("<color=#d9a066>Wood only: no stone, no pickaxe, everything costs wood.</color>", m_SmallWrap);
            }
            if (tutMap) GUI.enabled = true;

            // graphics: just Normal for now (the PSX test looks are hidden - GameSettings.ShowGraphicsPicker)
            if (GameSettings.ShowGraphicsPicker)
            {
                GUILayout.BeginHorizontal();
                RowLabel("Graphics");
                if (Choice(GameSettings.GraphicsMode == 0, "Normal", GUILayout.Height(30 * k))) GameSettings.SetGraphics(0);
                if (Choice(GameSettings.GraphicsMode == 1, "PSX", GUILayout.Height(30 * k))) GameSettings.SetGraphics(1);
                if (Choice(GameSettings.GraphicsMode == 2, "AI PSX TEST", GUILayout.Height(30 * k))) GameSettings.SetGraphics(2);
                GUILayout.EndHorizontal();
                if (GameSettings.PsxGraphics) GUILayout.Label("<color=#bbbbbb>PSX: low-res PSX models (trees so far). Just the looks. When you host, everyone in the match plays with your graphics.</color>", m_SmallWrap);
                if (GameSettings.AiPsx) GUILayout.Label("<color=#bbbbbb>AI PSX TEST: the whole wild map redone in the PSX trees' style - pixel textures, wobbly vertices, warping textures, 15-bit colour, half resolution, fog. A test; Normal and PSX are untouched.</color>", m_SmallWrap);
            }

            GUILayout.Space(4 * k);
            GUILayout.BeginHorizontal();
            if (Btn("MODE OPTIONS", GUILayout.Height(34 * k))) m_Page = MenuPage.ModeOptions;
            if (Btn("CHANGE VALUES", GUILayout.Height(34 * k))) m_Page = MenuPage.Values;
            GUILayout.EndHorizontal();
            GUILayout.Label($"<color=#bbbbbb>{ModeOptionsSummary(key)}</color>", m_SmallWrap);

            Caption("PLAY");
            if (Btn("HOST GAME", m_Primary, GUILayout.Height(44 * k))) boot.Host(false);
            GUILayout.Space(4 * k);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host IP", m_Label, GUILayout.Width(70 * k), GUILayout.Height(30 * k));
            boot.Ip = GUILayout.TextField(boot.Ip, m_Field, GUILayout.Height(30 * k));
            if (Btn($"port {boot.Port}  {(m_ShowPort ? "▲" : "▼")}", GUILayout.Width(120 * k), GUILayout.Height(30 * k))) m_ShowPort = !m_ShowPort;
            GUILayout.EndHorizontal();
            if (m_ShowPort)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port", m_Label, GUILayout.Width(70 * k), GUILayout.Height(30 * k));
                boot.Port = GUILayout.TextField(boot.Port, m_Field, GUILayout.Width(110 * k), GUILayout.Height(30 * k));
                GUILayout.Label("<color=#bbbbbb>UDP · 7777 unless the host changed it</color>", m_Small, GUILayout.Height(30 * k));
                GUILayout.EndHorizontal();
            }
            if (Btn("JOIN GAME", m_Primary, GUILayout.Height(44 * k))) boot.Join();
            // solo test: hosts, and the match starts without an opponent
            GUILayout.Space(4 * k);
            GUILayout.BeginHorizontal();
            if (Btn("SOLO TEST", GUILayout.Width(180 * k), GUILayout.Height(34 * k))) boot.Host(true);
            GUILayout.Label("<color=#bbbbbb>  host a match on your own: it starts straight away, no opponent needed</color>", m_SmallWrap, GUILayout.MinHeight(34 * k));
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(boot.Status)) GUILayout.Label("<color=#ffcc66>" + boot.Status + "</color>", m_LabelWrap);

            GUILayout.Space(8 * k);
            GUILayout.BeginHorizontal();
            if (Btn("SETTINGS", GUILayout.Height(34 * k))) { m_Page = MenuPage.Settings; m_Tab = SettingsTab.Sound; OnTabOpened(); }
            if (Btn("QUIT", GUILayout.Width(110 * k), GUILayout.Height(34 * k))) Application.Quit();
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#aaaaaa>Host picks the options and values. Your friend joins with your IP.</color>", m_SmallWrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>A "More modes" button's name (the fun modes say MODE, as they always did).</summary>
        static string MoreRuleLabel(GameRules r) => r == GameRules.Fun ? "Fun mode" : r == GameRules.FunRandom ? "Random Fun" : r == GameRules.FunRandomLimited ? "Random Fun Limited" : Cfg.RulesName(r);

        /// <summary>(tests) fold the main menu's "More modes" / "More maps" open or shut.</summary>
        public static void ShowMore(bool modes, bool maps) { if (s_I != null) { s_I.m_ShowMoreRules = modes; s_I.m_ShowMoreMaps = maps; } }

        static string ModeShort(GameMode m) => m == GameMode.Ffa3 ? "FFA 3" : m == GameMode.Ffa4 ? "FFA 4" : Cfg.ModeName(m);
        static string ModeTitle(GameMode m) => m == GameMode.Ffa3 ? "FFA 3  (free for all)" : m == GameMode.Ffa4 ? "FFA 4  (free for all)" : Cfg.ModeName(m);

        static string ModeDesc(GameMode m)
        {
            int teams = Cfg.ModeTeams(m), per = Cfg.ModeTeamSize(m);
            return teams == 2
                ? (per == 1 ? "Red vs blue, one on one." : $"Red vs blue, {per} players each ({teams * per} players).")
                : per == 1 ? $"Everyone for themselves: {teams} bases, glass walls in an X."
                : $"{teams} teams of {per} ({teams * per} players): {teams} bases, glass walls in an X.";
        }

        static string ModeOptionsSummary(int key)
        {
            int n = Mathf.Clamp(Cfg.AirdropCount, 0, 20);
            int items = Cfg.AirdropLoot.Count;
            return (n == 0 ? "No airdrops" : $"{n} airdrop{(n == 1 ? "" : "s")} a match ({((key & Cfg.CenterBit) != 0 ? "in the middle" : (key & Cfg.SidesBit) != 0 ? "one per side" : "anywhere")}, {items} item{(items == 1 ? "" : "s")})")
                + ((key & Cfg.RespawnLootBit) != 0 ? " · respawn with an airdrop item" : "");
        }

        // ------------------------------------------------------------------ mode options

        /// <summary>Mode options: how many airdrops, where they land, which items they can have, and the respawn option.</summary>
        void DrawModeOptions(Bootstrap boot)
        {
            float k = m_Scale;
            float w = Mathf.Min(Screen.width - 20, 780 * k), h = 900 * k;
            var r = new Rect((Screen.width - w) / 2, Mathf.Max(10, (Screen.height - h) / 2), w, Mathf.Min(h, Screen.height - 20));
            Fill(r, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            GUILayout.BeginArea(new Rect(r.x + 20 * k, r.y + 14 * k, r.width - 40 * k, r.height - 28 * k));
            GUILayout.Label("<b>MODE OPTIONS</b>", m_Big);
            m_ModeScroll = GUILayout.BeginScrollView(m_ModeScroll);
            int key = Bootstrap.MapChoice;
            float bh = 32 * k;

            Caption("AIRDROPS PER MATCH");
            int n = Mathf.Clamp(Cfg.AirdropCount, 0, 20);
            GUILayout.BeginHorizontal();
            if (Btn("-", GUILayout.Width(50 * k), GUILayout.Height(bh)) && n > 0) { Cfg.AirdropCount = n - 1; Cfg.SavePrefs(); }
            GUILayout.Label($"<b><size={Mathf.RoundToInt(24 * k)}>{n}</size></b>", m_Center, GUILayout.Width(70 * k), GUILayout.Height(bh));
            if (Btn("+", GUILayout.Width(50 * k), GUILayout.Height(bh)) && n < 20) { Cfg.AirdropCount = n + 1; Cfg.SavePrefs(); }
            GUILayout.EndHorizontal();
            n = Mathf.Clamp(Cfg.AirdropCount, 0, 20);
            float total = Cfg.MatchLength;
            var times = new System.Text.StringBuilder();
            for (int i = 1; i <= n && i <= 8; i++) times.Append(i > 1 ? ", " : "").Append(Clock(total * i / (n + 1)));
            if (n > 8) times.Append(", ...");
            GUILayout.Label(n == 0 ? "<color=#bbbbbb>No airdrops this match.</color>"
                : $"<color=#bbbbbb>Evenly spaced over the {Clock(total)} after the glass wall drops (1 = half way through, 2 = at the thirds...). " +
                  $"They land {times} after the wall drops.</color>", m_SmallWrap);

            GUILayout.Space(8 * k);
            bool center = (key & Cfg.CenterBit) != 0, sides = !center && (key & Cfg.SidesBit) != 0, anywhere = !center && !sides;
            int noWhere = key & ~Cfg.SidesBit & ~Cfg.CenterBit;
            GUILayout.BeginHorizontal();
            RowLabel("Where", 90 * k);
            if (Choice(anywhere, "Anywhere", GUILayout.Height(bh))) boot.SetMapChoice(noWhere);
            if (Choice(sides, "One per side", GUILayout.Height(bh))) boot.SetMapChoice(noWhere | Cfg.SidesBit);
            if (Choice(center, "Middle of the map", GUILayout.Height(bh))) boot.SetMapChoice(noWhere | Cfg.CenterBit);
            GUILayout.EndHorizontal();
            GUILayout.Label($"<color=#bbbbbb>{(center ? "Every airdrop comes down in the middle of the map." : sides ? "Every side of the map gets its own airdrop each time." : "One airdrop each time, at a random spot.")}</color>", m_Small);

            GUILayout.Space(10 * k);
            GUILayout.BeginHorizontal();
            GUILayout.Label("AIRDROP ITEMS  <color=#bbbbbb>(click to pick · - / + for how often it drops)</color>", m_Caption);
            GUILayout.FlexibleSpace();
            if (Btn("All", GUILayout.Width(70 * k), GUILayout.Height(26 * k))) { Cfg.AirdropItemMask = (1 << Cfg.AirdropChoices.Length) - 1; Cfg.SavePrefs(); }
            if (Btn("None", GUILayout.Width(70 * k), GUILayout.Height(26 * k))) { Cfg.AirdropItemMask = 0; Cfg.SavePrefs(); }
            if (Btn("Even chances", GUILayout.Width(130 * k), GUILayout.Height(26 * k))) { foreach (var it in Cfg.AllAirdropItems) Cfg.SetAirdropRarity(it, 10); Cfg.SavePrefs(); }
            GUILayout.EndHorizontal();
            float gridW = r.width - 40 * k - 20 * k; // (room for the scroll bar)
            var loot = Cfg.AirdropLoot;
            for (int i = 0; i < Cfg.AirdropChoices.Length; i++)
            {
                var it = Cfg.AirdropChoices[i];
                bool on = (Cfg.AirdropItemMask & (1 << i)) != 0;
                if (DropCell(i, it, true, on, Cfg.AirdropChance(it, loot), gridW)) { Cfg.AirdropItemMask ^= 1 << i; Cfg.SavePrefs(); }
            }
            if (Cfg.AirdropChoices.Length % DropCols != 0) { GUILayout.EndHorizontal(); GUILayout.Space(5 * k); }
            if ((Cfg.AirdropItemMask & ((1 << Cfg.AirdropChoices.Length) - 1)) == 0)
                GUILayout.Label("<color=#ffcc66>Nothing picked: airdrops will have any of these.</color>", m_Small);
            GUILayout.Label("<color=#bbbbbb>The % is each picked item's chance to be in an airdrop (respawn loot too). - / + change how common it is: 20 comes twice as often as 10, 0 never (if every picked item is 0 they're all even).</color>", m_SmallWrap);

            // the items only the Random Fun modes hand out (every airdrop item there is): just how often
            GUILayout.Space(6 * k);
            GUILayout.Label("MORE ITEMS  <color=#bbbbbb>(only in the Random Fun modes, which hand out every airdrop item there is)</color>", m_Caption);
            int cell = 0;
            foreach (var it in Cfg.AllAirdropItems)
            {
                if (System.Array.IndexOf(Cfg.AirdropChoices, it) >= 0) continue;
                DropCell(cell++, it, false, true, Cfg.AirdropChance(it, Cfg.AllAirdropItems), gridW);
            }
            if (cell % DropCols != 0) { GUILayout.EndHorizontal(); GUILayout.Space(5 * k); }

            GUILayout.Space(10 * k);
            bool respawnLoot = (key & Cfg.RespawnLootBit) != 0;
            GUILayout.BeginHorizontal();
            RowLabel("Respawn", 90 * k);
            if (Choice(!respawnLoot, "Normal", GUILayout.Height(bh))) boot.SetMapChoice(key & ~Cfg.RespawnLootBit);
            if (Choice(respawnLoot, "With an airdrop item", GUILayout.Height(bh))) boot.SetMapChoice(key | Cfg.RespawnLootBit);
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            GUILayout.Space(6 * k);
            if (Btn("Done", GUILayout.Height(40 * k))) m_Page = MenuPage.Main;
            GUILayout.EndArea();
        }

        const int DropCols = 3;
        Vector2 m_ModeScroll;

        /// <summary>One airdrop item in the mode options grid (three to a row): its icon and name (click: pick it, when
        /// it can be picked), its chance, and - / + for its rarity weight. True when the item was clicked.</summary>
        bool DropCell(int index, Item it, bool pickable, bool on, float chance, float gridW)
        {
            float k = m_Scale;
            if (index % DropCols == 0) GUILayout.BeginHorizontal();
            float cw = gridW / DropCols - 6 * k, ch = 50 * k;
            var cell = GUILayoutUtility.GetRect(cw, ch, GUILayout.Width(cw));
            float bw = 24 * k, pctW = 46 * k;
            // the right side: [-] 12% [+] (the weight under the %)
            var minus = new Rect(cell.xMax - bw * 2 - pctW - 6 * k, cell.y + (ch - bw) / 2, bw, bw);
            var pct = new Rect(minus.xMax, cell.y, pctW, ch);
            var plus = new Rect(pct.xMax, minus.y, bw, bw);
            var pick = new Rect(cell.x, cell.y, minus.x - cell.x - 2 * k, ch);
            bool hover = pickable && pick.Contains(Event.current.mousePosition);
            Fill(cell, on ? new Color(0.2f, 0.45f, 0.25f, hover ? 0.95f : 0.8f) : new Color(0.2f, 0.2f, 0.2f, hover ? 0.8f : 0.6f));
            var icon = ItemIcons.Get(it);
            if (icon != null) GUI.DrawTexture(new Rect(cell.x + 4, cell.y + (ch - 40 * k) / 2, 40 * k, 40 * k), icon, ScaleMode.ScaleToFit, true);
            string label = it == Item.BombBush ? "Bomb Bush" : it == Item.RocketLauncher ? "Rocket" : Cfg.ItemName(it);
            var small = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleLeft, wordWrap = true };
            GUI.Label(new Rect(cell.x + 48 * k, cell.y, pick.width - 48 * k, ch), (on ? "<b>" : "<color=#888888>") + label + (on ? "</b>" : "</color>"), small);
            int weight = Cfg.AirdropRarity(it);
            string pctText = on ? $"<b>{chance * 100f:0.#}%</b>" : "<color=#888888>-</color>";
            GUI.Label(new Rect(pct.x, pct.y + 2 * k, pct.width, ch * 0.55f), pctText, new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(pct.x, pct.y + ch * 0.5f, pct.width, ch * 0.45f), $"<color=#aaaaaa><size={Mathf.RoundToInt(10 * k)}>x{weight}</size></color>", new GUIStyle(m_Small) { alignment = TextAnchor.UpperCenter });
            int step = weight > 20 ? 10 : 5;
            if (BtnAt(minus, "-", m_Button) && weight > 0) { Cfg.SetAirdropRarity(it, weight - (weight > 20 ? 10 : 5)); Cfg.SavePrefs(); }
            if (BtnAt(plus, "+", m_Button)) { Cfg.SetAirdropRarity(it, weight + step); Cfg.SavePrefs(); }
            bool clicked = pickable && BtnAt(pick, "", GUIStyle.none);
            GUILayout.Space(6 * k);
            if (index % DropCols == DropCols - 1) { GUILayout.EndHorizontal(); GUILayout.Space(5 * k); }
            return clicked;
        }

        // ------------------------------------------------------------------ CHANGE VALUES

        static string Pretty(string field)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < field.Length; i++)
            {
                if (i > 0 && char.IsUpper(field[i]) && !char.IsUpper(field[i - 1])) sb.Append(' ');
                sb.Append(field[i]);
            }
            return sb.ToString().Replace("Hp", "HP");
        }

        /// <summary>The value sections set on the MODE OPTIONS screen (the airdrops, and each airdrop item's chance), not in CHANGE VALUES.</summary>
        public static bool IsModeOptionsSection(string sec) => sec == "Mode options" || sec == "Airdrop rarity";

        /// <summary>Every [Tune] stat in Cfg, folded into sections. Saved on this PC; the host's values are used in a match.</summary>
        void DrawValues()
        {
            float k = m_Scale;
            float w = Mathf.Min(Screen.width - 40, 960 * k), h = Screen.height - 40;
            var r = new Rect((Screen.width - w) / 2, 20, w, h);
            Fill(r, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            GUILayout.BeginArea(new Rect(r.x + 18 * k, r.y + 12 * k, r.width - 36 * k, r.height - 24 * k));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt(28 * k)}>CHANGE VALUES</size></b>", m_Label, GUILayout.Height(40 * k));
            GUILayout.FlexibleSpace();
            if (Btn("Done", GUILayout.Width(120 * k), GUILayout.Height(34 * k))) m_Page = MenuPage.Main;
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#bbbbbb>When you host, these values are used for the whole match (everyone gets them). Times are in seconds, speeds in m/s. Changed values are green. Click a heading to open it.</color>", m_SmallWrap);

            GUILayout.BeginHorizontal();
            if (Btn("Export to a file", GUILayout.Height(30 * k)))
            {
                var path = SettingsFile.Export();
                m_FileMsg = path != null ? $"Saved {System.IO.Path.GetFileName(path)} in the game folder - send it over and say what it's for (new defaults, or a new game mode)." : "Couldn't write the file.";
                m_FileMsgTime = Time.unscaledTime;
            }
            if (Btn("Import from the file", GUILayout.Height(30 * k)))
            {
                int n = SettingsFile.Import(out string err);
                m_FileMsg = err ?? $"Loaded {n} value{(n == 1 ? "" : "s")} from {SettingsFile.FileName}.";
                m_FileMsgTime = Time.unscaledTime;
                m_EditBuffers.Clear();
            }
            if (Btn("Open the folder", GUILayout.Height(30 * k))) Application.OpenURL("file:///" + SettingsFile.Folder.Replace('\\', '/'));
            if (Btn("Reset all to defaults", GUILayout.Height(30 * k)))
            {
                Cfg.ResetDefaults();
                Cfg.SavePrefs();
                m_EditBuffers.Clear();
            }
            GUILayout.EndHorizontal();
            if (Time.unscaledTime - m_FileMsgTime < 12f) GUILayout.Label($"<color=#9dff9d>{m_FileMsg}</color>", m_SmallWrap);
            GUILayout.Space(6 * k);
            // search: filters the rows as you type (name or section); the sections with a match open up
            if (SetValuesSearch != null) { m_ValuesQuery = SetValuesSearch; SetValuesSearch = null; }
            m_ValuesQuery = SearchBox("search values", m_ValuesQuery, "type to find a value - its name or section (slide, arrow, airdrop...)");
            if (m_ValuesQuery != m_ValuesQueryWas) { m_ValuesQueryWas = m_ValuesQuery; m_ValuesScroll = Vector2.zero; }
            bool searching = !string.IsNullOrWhiteSpace(m_ValuesQuery);
            GUILayout.Space(4 * k);

            m_ValuesScroll = GUILayout.BeginScrollView(m_ValuesScroll);
            // group the fields by section, keeping Cfg's order
            var sections = new List<string>();
            var bySection = new Dictionary<string, List<System.Reflection.FieldInfo>>();
            foreach (var f in Cfg.TuneFields)
            {
                var sec = Cfg.SectionOf(f);
                if (IsModeOptionsSection(sec)) continue; // (those are on the MODE OPTIONS screen)
                if (!bySection.TryGetValue(sec, out var l)) { bySection[sec] = l = new List<System.Reflection.FieldInfo>(); sections.Add(sec); }
                l.Add(f);
            }
            float colW = (w - 70 * k) / 2f;
            int matches = 0;
            foreach (var sec in sections)
            {
                var fields = bySection[sec];
                if (searching)
                {
                    // only the rows that match, under their section's heading (sections with none are left out)
                    int all = fields.Count;
                    fields = fields.FindAll(f => Matches(m_ValuesQuery, Pretty(f.Name) + " " + f.Name + " " + sec));
                    if (fields.Count == 0) continue;
                    matches += fields.Count;
                    GUILayout.Label($"▼   {Highlight(sec.ToUpper(), m_ValuesQuery)}   <color=#aaaaaa>{fields.Count} of {all}</color>", m_Header, GUILayout.Height(30 * k));
                }
                else
                {
                    int changed = 0;
                    foreach (var f in fields) if (!Cfg.IsDefault(f)) changed++;
                    string extra = $"   <color=#aaaaaa>{fields.Count} value{(fields.Count == 1 ? "" : "s")}</color>" + (changed > 0 ? $"   <color=#8fe38f>{changed} changed</color>" : "");
                    if (!Section("v:" + sec, sec.ToUpper(), extra)) continue;
                }
                for (int i = 0; i < fields.Count; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < 2 && i + c < fields.Count; c++)
                    {
                        var f = fields[i + c];
                        bool ch = !Cfg.IsDefault(f);
                        GUILayout.Space(12 * k);
                        GUILayout.Label((ch ? "<color=#8fe38f>" : "") + (searching ? Highlight(Pretty(f.Name), m_ValuesQuery) : Pretty(f.Name)) + (ch ? "</color>" : ""), m_Small, GUILayout.Width(colW * 0.62f), GUILayout.Height(24 * k));
                        if (f.FieldType == typeof(bool))
                        {
                            // on / off settings are a toggle button
                            bool on = (bool)f.GetValue(null);
                            if (ToggleBtn(on, on ? "On" : "Off", GUILayout.Width(colW * 0.3f), GUILayout.Height(24 * k)) != on) { f.SetValue(null, !on); Cfg.SavePrefs(); }
                            continue;
                        }
                        if (!m_EditBuffers.TryGetValue(f.Name, out var text)) text = Cfg.Format(f);
                        var edited = GUILayout.TextField(text, m_Field, GUILayout.Width(colW * 0.3f), GUILayout.Height(24 * k));
                        if (edited != text)
                        {
                            m_EditBuffers[f.Name] = edited;
                            if (Cfg.TrySet(f, edited)) Cfg.SavePrefs();
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(6 * k);
            }
            if (searching && matches == 0)
                GUILayout.Label($"<color=#ffcc66>No values match \"{m_ValuesQuery.Trim()}\".</color>  <color=#bbbbbb>Try part of a name (slide, arrow, wood) or a section (Airdrop, Player).</color>", m_LabelWrap);
            ValuesMatches = searching ? matches : -1;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ settings: sound, controls, display, voice

        List<Vector2Int> m_ResList;
        int m_ResSel, m_RateSel;
        GameSettings.WindowMode m_ModeSel;

        void OnTabOpened()
        {
            if (m_Tab != SettingsTab.Display) return;
            m_ResList = GameSettings.Resolutions();
            m_ResSel = Mathf.Max(0, m_ResList.IndexOf(GameSettings.CurrentSize));
            m_ModeSel = GameSettings.CurrentMode;
            var rates = GameSettings.RefreshRates(m_ResList[m_ResSel]);
            m_RateSel = 0;
            for (int i = 0; i < rates.Count; i++) if (System.Math.Abs(rates[i].value - GameSettings.ChosenRate.value) < 0.5) m_RateSel = i;
        }

        /// <summary>(tests) the settings window's rect (GUI space), as last drawn.</summary>
        public static Rect SettingsPanel { get; private set; }
        /// <summary>The Display tab is a side panel with the game in view beside it (no dark cover behind it).</summary>
        bool SideSettings => m_Tab == SettingsTab.Display || m_ColourScreen;

        /// <summary>The settings window (main menu and pause menu). Returns true when Back is pressed.</summary>
        bool DrawSettingsPanel()
        {
            // the colour screen only stays open while the settings stay open (shut them, and they come back on the tabs)
            if (Time.frameCount - m_SettingsFrame > 1) m_ColourScreen = false;
            m_SettingsFrame = Time.frameCount;
            if ((ScrollToColours || OpenPickerFor >= 0) && m_Tab == SettingsTab.Display) { m_ColourScreen = true; ScrollToColours = false; }
            if (m_ColourScreen) { DrawColourScreen(); return false; }
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            Rect r;
            bool side = m_Tab == SettingsTab.Display;
            if (side)
            {
                // Display: a panel down one side, like the colour screen (no dark cover), so the game shows beside it
                // and every change to the look (post processing, shadows, grass, the UI) shows on it as you pick
                float pw = Mathf.Min(sw * 0.5f, 640 * k), m = 10 * k;
                r = new Rect(m_ColourLeft ? m : sw - pw - m, m, pw, sh - 2 * m);
            }
            else
            {
                float w = Mathf.Min(sw - 40, 900 * k), h = Mathf.Min(sh - 40, 760 * k);
                r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            }
            SettingsPanel = r;
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(r, new Color(0.05f, 0.05f, 0.06f, side ? 0.9f : 0.95f));
            bool back = false;
            float pad = side ? 14 * k : 20 * k;
            GUILayout.BeginArea(new Rect(r.x + pad, r.y + 14 * k, r.width - 2 * pad, r.height - 28 * k));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt((side ? 24 : 28) * k)}>SETTINGS</size></b>", m_Label, GUILayout.Height(40 * k));
            GUILayout.FlexibleSpace();
            if (side && Btn(m_ColourLeft ? "▶" : "◀", GUILayout.Width(44 * k), GUILayout.Height(34 * k))) m_ColourLeft = !m_ColourLeft;
            if (Btn("Back", GUILayout.Width((side ? 96 : 120) * k), GUILayout.Height(34 * k))) back = true;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            string[] tabs = { "Sound", "Controls", "Display", "Voice chat" };
            for (int i = 0; i < tabs.Length; i++)
                if (Choice((int)m_Tab == i, tabs[i], GUILayout.Height(34 * k))) { m_Tab = (SettingsTab)i; m_Rebinding = false; OnTabOpened(); }
            GUILayout.EndHorizontal();
            GUILayout.Space(10 * k);
            m_SettingsScroll = GUILayout.BeginScrollView(m_SettingsScroll);
            switch (m_Tab)
            {
                case SettingsTab.Sound: DrawSoundTab(); break;
                case SettingsTab.Controls: DrawControlsTab(); break;
                case SettingsTab.Display: DrawDisplayTab(); break;
                case SettingsTab.Voice: DrawVoiceTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (back) m_Rebinding = false;
            return back;
        }

        void DrawSoundTab()
        {
            float k = m_Scale;
            bool changed = false;
            Caption("VOLUME");
            float v = SliderRow("Master", GameSettings.MasterVolume, 0f, 1f, $"{GameSettings.MasterVolume * 100:0}%");
            if (!Mathf.Approximately(v, GameSettings.MasterVolume)) { GameSettings.MasterVolume = v; changed = true; }
            v = SliderRow("Sound effects", GameSettings.SfxVolume, 0f, 1.5f, $"{GameSettings.SfxVolume * 100:0}%");
            if (!Mathf.Approximately(v, GameSettings.SfxVolume)) { GameSettings.SfxVolume = v; changed = true; }
            v = SliderRow("Voice chat", GameSettings.VoiceVolume, 0f, 2f, $"{GameSettings.VoiceVolume * 100:0}%");
            if (!Mathf.Approximately(v, GameSettings.VoiceVolume)) { GameSettings.VoiceVolume = v; changed = true; }
            GUILayout.Space(6 * k);
            GUILayout.Label("<color=#bbbbbb>Master turns everything up or down. Sound effects is every game and menu sound (hits, gathering, building, arrows, footsteps). Voice chat is how loud other players are.</color>", m_SmallWrap);
            GUILayout.Space(8 * k);
            if (Btn("Test sound", GUILayout.Width(160 * k), GUILayout.Height(30 * k))) Sfx.Play2D(Sfx.Ding, 0.7f, 0f);
            if (changed) GameSettings.Save();
        }

        void DrawControlsTab()
        {
            float k = m_Scale;
            Caption("MOUSE");
            float s = GameSettings.MouseSensitivity;
            float ns = SliderRow("Mouse sensitivity", s, 0.1f, 8f, s.ToString("0.00"));
            ns = Mathf.Round(ns * 20f) / 20f;
            if (!Mathf.Approximately(ns, s)) { GameSettings.MouseSensitivity = Mathf.Max(0.05f, ns); GameSettings.Save(); }

            GUILayout.Space(4 * k);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>Click a key, then press the new key or mouse button.  Esc cancels · Backspace clears.</color>", m_SmallWrap);
            if (Btn("Reset controls", GUILayout.Width(170 * k), GUILayout.Height(30 * k))) { Binds.ResetDefaults(); m_Rebinding = false; }
            GUILayout.EndHorizontal();

            string group = null;
            float labelW = 190 * k, cellW = 130 * k;
            foreach (var info in Binds.All)
            {
                if (info.Group != group)
                {
                    group = info.Group;
                    Caption(group);
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label(info.Label, m_Label, GUILayout.Width(labelW), GUILayout.Height(30 * k));
                for (int alt = 0; alt < 2; alt++)
                {
                    bool capturing = m_Rebinding && m_RebindWhat == info.Bind && m_RebindAlt == (alt == 1);
                    var cur = Binds.Get(info.Bind, alt == 1);
                    string text = capturing ? $"<color=#ffd27a>{(Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f ? "press a key" : "")}</color>" : cur == KeyCode.None ? "<color=#777777>-</color>" : Binds.KeyName(cur);
                    bool on = GUILayout.Toggle(capturing, text, m_KeyCell, GUILayout.Width(cellW), GUILayout.Height(30 * k));
                    TrackHover(GUILayoutUtility.GetLastRect());
                    // (the click that just set a key mustn't start capturing again)
                    if (on != capturing && m_RebindFrame != Time.frameCount)
                    {
                        ClickSound();
                        if (on) { m_Rebinding = true; m_RebindWhat = info.Bind; m_RebindAlt = alt == 1; m_RebindFrame = Time.frameCount; }
                        else m_Rebinding = false;
                    }
                }
                GUILayout.Space(8 * k);
                // the hint wraps onto as many lines as it needs (never cut off)
                GUILayout.Label($"<color=#aaaaaa>{info.Hint}</color>", m_SmallWrap, GUILayout.MinHeight(30 * k));
                GUILayout.EndHorizontal();
            }
            Caption("ALWAYS");
            GUILayout.Label("<color=#bbbbbb>Esc: pause (or close a menu) · mouse wheel: hotbar · 1 / 2 on the death screen: respawn in base / in the wild · building plan: hold Aim for the building wheel</color>", m_SmallWrap);
        }

        void DrawDisplayTab()
        {
            float k = m_Scale;
            if (m_ResList == null) OnTabOpened();
            float lw = 150 * k;
            Caption("GRAPHICS");
            // (the PSX test looks are hidden for now - GameSettings.ShowGraphicsPicker)
            if (GameSettings.ShowGraphicsPicker)
            {
                GUILayout.BeginHorizontal();
                RowLabel("Style", lw);
                // in a match the host's graphics are used by everyone
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
            RowLabel("FPS counter", lw);
            bool fps = ToggleBtn(GameSettings.ShowFps, GameSettings.ShowFps ? "On" : "Off", GUILayout.Width(90 * k), GUILayout.Height(30 * k));
            if (fps != GameSettings.ShowFps) GameSettings.SetShowFps(fps);
            GUILayout.Label("<color=#bbbbbb>  frames per second, top left</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ---- shadows ----
            Caption("SHADOWS  ·  just on this PC");
            float ss = SliderRow("Shadow darkness", GameSettings.ShadowStrength, 0f, 1f, $"{GameSettings.ShadowStrength * 100f:0}%", lw);
            float sd = SliderRow("Shadow distance", GameSettings.ShadowDistance, GameSettings.ShadowDistanceMin, GameSettings.ShadowDistanceMax, $"{GameSettings.ShadowDistance:0} m", lw);
            GameSettings.SetShadows(Mathf.Round(ss * 20f) / 20f, Mathf.Round(sd / 5f) * 5f);
            GUILayout.Label("<color=#bbbbbb>Darkness: how dark the sun's shadows are (0% = none). Distance: how far from you shadows are drawn - further looks better, nearer is faster and the near shadows are sharper.</color>", m_SmallWrap);

            DrawPostFxSettings();
            DrawInterfaceSettings();
            DrawWorldLook();
            DrawTreeXSettings(); // (TreeX.cs)
            DrawBaseFloorSettings(); // (Hud.BaseFloor.cs)
            DrawBeamSettings(); // (Hud.Beams.cs)

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

        /// <summary>Settings > Display > INTERFACE (the font, UI scale, HUD opacity, accent colour) and ALIEN GLOW.</summary>
        void DrawInterfaceSettings()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("INTERFACE  ·  just on this PC");
            int font = GameSettings.UiFont;
            // the font: every bit of menu and HUD text (each button is written in its own font)
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
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.SetInterface(0, 1f, 1f, 0);
            GUILayout.EndHorizontal();

            Caption("ALIEN GLOW  ·  just on this PC");
            float st = SliderRow("Glow strength", Cfg.AlienOutlineStrength, 0.02f, 1f, $"{Cfg.AlienOutlineStrength * 100f:0}%", lw);
            float wd = SliderRow("Glow thickness", Cfg.AlienOutlineWidth, 0.005f, 0.12f, $"{Cfg.AlienOutlineWidth * 100f:0.0} cm", lw);
            GameSettings.SetAlienGlow(st, wd);
            GUILayout.Label("<color=#bbbbbb>Enemies always have a faint glow in their team colour so they're easier to spot (every mode).</color>", m_SmallWrap);
        }

        float m_PendingScale = -1f;

        // ------------------------------------------------------------------ display: grass and world colours (Normal graphics)

        readonly Dictionary<int, Vector3> m_Hsv = new Dictionary<int, Vector3>();
        readonly Dictionary<int, Color> m_HsvOf = new Dictionary<int, Color>();
        readonly Dictionary<int, string> m_HexEdit = new Dictionary<int, string>();
        string m_CopyNote;
        float m_CopyNoteUntil;
        GUIStyle m_HexField, m_RowLabel;
        /// <summary>(tests) open the world colours screen (from Settings > Display).</summary>
        public static bool ScrollToColours;

        void DrawWorldLook()
        {
            float k = m_Scale;
            Caption("GRASS  ·  Normal graphics, just on this PC");
            float gd = SliderRow("Grass render distance", GameSettings.GrassDistance, GameSettings.GrassDistanceMin, GameSettings.GrassDistanceMax, $"{GameSettings.GrassDistance:0} m", 210 * k);
            float gn = SliderRow("Grass density", GameSettings.GrassDensity, GameSettings.GrassDensityMin, 1f, $"{GameSettings.GrassDensity * 100f:0}%", 210 * k);
            // falloff: shown the other way round (right = more grass far away)
            float gf = SliderRow("Far grass thickness", GameSettings.GrassFalloffMax + GameSettings.GrassFalloffMin - GameSettings.GrassFalloff, GameSettings.GrassFalloffMin, GameSettings.GrassFalloffMax,
                $"{GameSettings.GrassThicknessPercent(GameSettings.GrassFalloff):0}%", 210 * k);
            gf = Mathf.Round((GameSettings.GrassFalloffMax + GameSettings.GrassFalloffMin - gf) * 20f) / 20f;
            if (!Mathf.Approximately(Mathf.Round(gd), GameSettings.GrassDistance) || !Mathf.Approximately(gn, GameSettings.GrassDensity) || !Mathf.Approximately(gf, GameSettings.GrassFalloff))
                GameSettings.SetGrass(Mathf.Round(gd), Mathf.Round(gn * 20f) / 20f, gf);
            float gh = SliderRow("Grass height", GameSettings.GrassHeight, GameSettings.GrassHeightMin, GameSettings.GrassHeightMax, $"{GameSettings.GrassHeight * 100f:0}%", 210 * k);
            gh = Mathf.Round(gh * 20f) / 20f;
            if (!Mathf.Approximately(gh, GameSettings.GrassHeight)) GameSettings.SetGrassHeight(gh);
            GUILayout.Label("<color=#bbbbbb>Less distance, density or far thickness = faster (it matters most on laptops). Far thickness is how slowly the grass thins out with distance. The far grass fades into the ground either way.</color>", m_SmallWrap);

            Caption("WORLD COLOURS  ·  Normal graphics, just on this PC");
            GUILayout.BeginHorizontal();
            int nChanged = ColorSlots.ChangedCount;
            if (Btn("World colours  ▶", m_Primary, GUILayout.Width(240 * k), GUILayout.Height(36 * k))) { m_ColourScreen = true; m_ColourScroll = Vector2.zero; }
            GUILayout.Label($"<color=#bbbbbb>  Opens beside the game, so you can see the world change as you pick.</color>{(nChanged > 0 ? $"  <color=#ffd27a>{nChanged} changed</color>" : "")}", m_SmallWrap, GUILayout.MinHeight(36 * k));
            GUILayout.EndHorizontal();
            GUILayout.Space(6 * k);
            GUILayout.BeginHorizontal();
            if (Btn("Reset grass and colours", GUILayout.Width(260 * k), GUILayout.Height(32 * k))) GameSettings.ResetWorldLook();
            GUILayout.Label("<color=#bbbbbb>  Changes show straight away.</color>", m_SmallWrap, GUILayout.Height(32 * k));
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ display: the world colours screen

        bool m_ColourScreen, m_ColourLeft;
        int m_SettingsFrame = -10;
        Vector2 m_ColourScroll;

        /// <summary>(tests) the world colours screen is open.</summary>
        public static bool ColourScreenOpen => s_I != null && s_I.m_ColourScreen;
        /// <summary>(tests) the colour screen's panel, in screen pixels (GUI space: y down).</summary>
        public static Rect ColourPanel { get; private set; }
        /// <summary>(tests) put the colour screen on the left (or back on the right), as its ◀ / ▶ button does.</summary>
        public static void SetColourSide(bool left) { if (s_I != null) s_I.m_ColourLeft = left; }

        /// <summary>
        /// Settings > Display > World colours: a narrow panel down one side of the screen (no dimming behind it, the pause
        /// menu's dark cover is off too) so the world stays in view and every colour change shows on it as you pick.
        /// It can swap sides; Back (or Esc) goes back to the Display tab.
        /// </summary>
        void DrawColourScreen()
        {
            float k = m_Scale, scrW = Screen.width, scrH = Screen.height;
            float w = Mathf.Min(scrW * 0.46f, 560 * k), m = 10 * k;
            var panel = new Rect(m_ColourLeft ? m : scrW - w - m, m, w, scrH - 2 * m);
            ColourPanel = panel;
            if (panel.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(panel, new Color(0.05f, 0.05f, 0.06f, 0.9f));
            float pad = 14 * k;
            var area = new Rect(panel.x + pad, panel.y + 10 * k, panel.width - 2 * pad, panel.height - 20 * k);
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt(22 * k)}>WORLD COLOURS</size></b>", m_Label, GUILayout.Height(34 * k));
            GUILayout.FlexibleSpace();
            if (Btn(m_ColourLeft ? "▶" : "◀", GUILayout.Width(44 * k), GUILayout.Height(32 * k))) m_ColourLeft = !m_ColourLeft;
            if (Btn("Back", GUILayout.Width(96 * k), GUILayout.Height(32 * k))) m_ColourScreen = false;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            int changed = ColorSlots.ChangedCount;
            if (Btn($"COPY CHANGED ({changed})", m_Primary, GUILayout.Height(30 * k)))
            {
                int n = ColorSlots.Copy(true);
                m_CopyNote = n == 0 ? "Nothing changed from the defaults (copied an empty list)." : $"Copied {n} changed colour{(n == 1 ? "" : "s")} to the clipboard.";
                m_CopyNoteUntil = Time.unscaledTime + 4f;
            }
            if (Btn("COPY ALL", GUILayout.Width(100 * k), GUILayout.Height(30 * k)))
            {
                int n = ColorSlots.Copy(false);
                m_CopyNote = $"Copied all {n} colours to the clipboard.";
                m_CopyNoteUntil = Time.unscaledTime + 4f;
            }
            if (Btn("Reset all", GUILayout.Width(100 * k), GUILayout.Height(30 * k))) GameSettings.ResetWorldColours();
            GUILayout.EndHorizontal();
            GUILayout.Label(Time.unscaledTime < m_CopyNoteUntil ? $"<color=#9fe0a0>{m_CopyNote}</color>"
                : "<color=#bbbbbb>Click a swatch, type a hex code, or Pick for the colour picker - it shows on the world straight away. The copy buttons put lines like  TreeTrunks = #4F8F2A  on the clipboard.</color>", m_SmallWrap);
            GUILayout.Space(4 * k);

            if (m_HexField == null)
            {
                m_HexField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.MiddleCenter };
                m_RowLabel = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleLeft };
            }
            if (GUIUtility.keyboardControl == 0) m_HexEdit.Clear();
            if (OpenPickerFor >= 0 && OpenPickerFor < ColorSlots.All.Count)
            {
                var ps = ColorSlots.All[OpenPickerFor];
                OpenPickerFor = -1;
                m_ScrollToPicker = true;
                m_PickerRowY = -1f;
                if (m_PickerSlot != ps.Index) TogglePicker(ps, ps == ColorSlots.Hands && ColorSlots.HandsTeam ? ColorSlots.HandTint(Cfg.TeamColor[0]) : ps.Value, ps == ColorSlots.Hands && ColorSlots.HandsTeam);
            }
            if (m_ScrollToPicker && m_PickerRowY >= 0f) { m_ColourScroll.y = m_PickerRowY - 40f * k; m_ScrollToPicker = false; }
            m_ColourScroll = GUILayout.BeginScrollView(m_ColourScroll);
            // each colour on two lines (the panel is narrow): its name, Pick and Default; then the colour now, the
            // ready-made ones and its hex code
            float sw = 22 * k, rowH = 26 * k, avail = area.width - 22 * k;
            foreach (var group in ColorSlots.Groups)
            {
                GUILayout.Space(4 * k);
                GUILayout.Label($"<color=#9ab8d8><b>{group}</b></color>", m_SmallWrap);
                foreach (var s in ColorSlots.All)
                {
                    if (s.Group != group) continue;
                    int i = s.Index;
                    var cur = s.Value;
                    bool teamHands = s == ColorSlots.Hands && ColorSlots.HandsTeam;
                    var shown = teamHands ? ColorSlots.HandTint(Cfg.TeamColor[0]) : cur;
                    bool picking = m_PickerSlot == i;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((s.Changed ? "<color=#ffd27a>" : "") + s.Label + (s.Changed ? "</color>" : ""), m_RowLabel, GUILayout.Height(rowH));
                    GUILayout.FlexibleSpace();
                    if (Btn(picking ? "Pick ▲" : "Pick ▼", GUILayout.Width(72 * k), GUILayout.Height(rowH))) TogglePicker(s, shown, teamHands);
                    if (s == ColorSlots.Hands)
                    {
                        bool team = ToggleBtn(ColorSlots.HandsTeam, "Team colour", GUILayout.Width(120 * k), GUILayout.Height(rowH));
                        if (team != ColorSlots.HandsTeam) ColorSlots.SetHandsTeam(team);
                    }
                    else if (s.Changed) { if (Btn("Default", GUILayout.Width(84 * k), GUILayout.Height(rowH))) ColorSlots.Set(s, s.Default); }
                    else GUILayout.Space(84 * k + 4);
                    GUILayout.EndHorizontal();
                    if (picking && Event.current.type == EventType.Repaint) m_PickerRowY = GUILayoutUtility.GetLastRect().y;
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(12 * k);
                    // the colour now (click it: the colour picker), then ready-made ones to click
                    var r = GUILayoutUtility.GetRect(sw * 1.6f, sw, GUILayout.Width(sw * 1.6f), GUILayout.Height(rowH));
                    r.y += (rowH - sw) * 0.5f; r.height = sw;
                    Fill(r, picking ? new Color(1f, 0.82f, 0.3f) : new Color(0.8f, 0.8f, 0.8f));
                    Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), shown);
                    TrackHover(r);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { ClickSound(); TogglePicker(s, shown, teamHands); }
                    GUILayout.Space(8 * k);
                    foreach (var pc in s.Presets)
                    {
                        var pr = GUILayoutUtility.GetRect(sw, sw, GUILayout.Width(sw), GUILayout.Height(rowH));
                        pr.y += (rowH - sw) * 0.5f; pr.height = sw;
                        bool on = !teamHands && ColorSlots.Same(pc, cur);
                        Fill(pr, on ? new Color(1f, 0.82f, 0.3f) : new Color(0, 0, 0, 0.6f));
                        Fill(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), pc);
                        TrackHover(pr);
                        if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) { ClickSound(); ColorSlots.Set(s, pc); }
                        GUILayout.Space(3 * k);
                    }
                    GUILayout.FlexibleSpace();
                    // the hex code: type or paste one
                    string hex = m_HexEdit.TryGetValue(i, out var ed) ? ed : ColorUtility.ToHtmlStringRGB(cur);
                    GUI.SetNextControlName("hex" + i);
                    string nh = GUILayout.TextField(hex, 7, m_HexField, GUILayout.Width(86 * k), GUILayout.Height(rowH - 2 * k));
                    NoteTyping("hex" + i); // (typing a hex code mutes the game's keys)
                    if (nh != hex)
                    {
                        m_HexEdit[i] = nh;
                        var t = nh.Trim().TrimStart('#');
                        if (t.Length == 6 && ColorUtility.TryParseHtmlString("#" + t, out var hc)) ColorSlots.Set(s, hc);
                    }
                    GUILayout.EndHorizontal();
                    if (picking) DrawColourPicker(s, shown, teamHands, avail);
                    GUILayout.Space(3 * k);
                }
            }
            GUILayout.Space(8 * k);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawVoiceTab()
        {
            float k = m_Scale;
            bool changed = false;
            Caption("PROXIMITY VOICE CHAT  ·  people hear you when they're near you");
            GUILayout.BeginHorizontal();
            string[] modes = { "Off", "Open mic", $"Push to talk ({Binds.Name(Bind.PushToTalk)})" };
            for (int i = 0; i < 3; i++)
                if (Choice(GameSettings.VoiceMode == i, modes[i], GUILayout.Height(30 * k))) { GameSettings.VoiceMode = i; changed = true; }
            GUILayout.EndHorizontal();
            var devs = Microphone.devices;
            GUILayout.BeginHorizontal();
            RowLabel("Microphone", 190 * k);
            if (devs.Length == 0) GUILayout.Label("<color=#ff8888>no microphone found</color>", m_Label, GUILayout.Height(30 * k));
            else
            {
                int cur = System.Array.IndexOf(devs, GameSettings.MicDevice);
                if (cur < 0) cur = 0;
                if (Btn("<", GUILayout.Width(40 * k), GUILayout.Height(30 * k))) { GameSettings.MicDevice = devs[(cur + devs.Length - 1) % devs.Length]; changed = true; }
                string name = devs[cur];
                GUILayout.Label(name.Length > 40 ? name.Substring(0, 40) + "..." : name, m_Center, GUILayout.Width(360 * k), GUILayout.Height(30 * k));
                if (Btn(">", GUILayout.Width(40 * k), GUILayout.Height(30 * k))) { GameSettings.MicDevice = devs[(cur + 1) % devs.Length]; changed = true; }
            }
            GUILayout.EndHorizontal();
            float v = SliderRow("Mic volume", GameSettings.MicGain, 0.2f, 4f, $"{GameSettings.MicGain * 100:0}%");
            if (!Mathf.Approximately(v, GameSettings.MicGain)) { GameSettings.MicGain = v; changed = true; }
            if (GameSettings.VoiceMode == GameSettings.VoiceOpen)
            {
                v = SliderRow("Open mic sensitivity", 0.1f - GameSettings.MicThreshold, 0f, 0.1f, $"{(0.1f - GameSettings.MicThreshold) * 1000:0}");
                if (!Mathf.Approximately(0.1f - v, GameSettings.MicThreshold)) { GameSettings.MicThreshold = 0.1f - v; changed = true; }
            }
            GUILayout.BeginHorizontal();
            RowLabel("Mic level", 190 * k);
            var lr = GUILayoutUtility.GetRect(300 * k, 14 * k, GUILayout.ExpandWidth(true));
            lr.y += 8 * k;
            Fill(lr, new Color(0, 0, 0, 0.6f));
            Fill(new Rect(lr.x, lr.y, lr.width * Mathf.Clamp01(VoiceChat.Level), lr.height), VoiceChat.Transmitting ? new Color(0.4f, 1f, 0.5f) : new Color(0.6f, 0.6f, 0.6f));
            if (GameSettings.VoiceMode == GameSettings.VoiceOpen) Fill(new Rect(lr.x + lr.width * Mathf.Clamp01(GameSettings.MicThreshold * 3f), lr.y - 2, 2, lr.height + 4), new Color(1f, 0.8f, 0.3f));
            GUILayout.Label(VoiceChat.Transmitting ? "<color=#7dff9a>sending</color>" : "", m_Small, GUILayout.Width(80 * k));
            GUILayout.EndHorizontal();
            if (changed) GameSettings.Save();
        }

        // ------------------------------------------------------------------ pause

        void DrawPause(Bootstrap boot, PlayerNet me, PlayerController pc, NetGame game)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            if (OpenPause >= 0)
            {
                m_PausePage = OpenPause == 3 ? PausePage.Dev : OpenPause >= 1 ? PausePage.Settings : PausePage.Root; // (3: Dev settings - tests)
                m_Tab = OpenPause == 2 ? SettingsTab.Display : SettingsTab.Controls; // (2: Display - for the tests)
                OpenPause = -1;
                m_ColourScreen = false;
                OnTabOpened();
            }
            // (no dark cover while Display or the world colours screen is up: the world has to show)
            if (!(m_PausePage == PausePage.Settings && SideSettings)) Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.55f));
            if (m_PausePage == PausePage.Settings) { MouseOverUI = true; if (DrawSettingsPanel()) m_PausePage = PausePage.Root; return; }
            if (m_PausePage == PausePage.Dev) { DrawDevMenu(me, game); return; }
            float w = 420 * k, h = Mathf.Min(sh - 40, 460 * k);
            var r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(r, new Color(0.06f, 0.06f, 0.08f, 0.94f));
            GUILayout.BeginArea(new Rect(r.x + 24 * k, r.y + 16 * k, r.width - 48 * k, r.height - 32 * k));
            GUILayout.Label("<b>PAUSED</b>", m_Big);
            GUILayout.Label("<color=#bbbbbb>The match keeps running!</color>", m_Center);
            GUILayout.Space(12 * k);
            if (Btn("Resume", m_Primary, GUILayout.Height(42 * k))) pc.Paused = false;
            GUILayout.Space(4 * k);
            if (Btn("Settings", GUILayout.Height(38 * k))) { m_PausePage = PausePage.Settings; m_Tab = SettingsTab.Sound; OnTabOpened(); }
            if (Btn("Controls", GUILayout.Height(38 * k))) { m_PausePage = PausePage.Settings; m_Tab = SettingsTab.Controls; OnTabOpened(); }
            if (Btn("Dev settings", GUILayout.Height(38 * k))) m_PausePage = PausePage.Dev;
            // suicide: click, then click again within 3 s to be sure
            bool armed = Time.unscaledTime < m_SuicideArmedUntil;
            GUI.enabled = me != null && !me.Dead.Value && (game == null || game.S != GameState.GameOver);
            if (Btn(armed ? "<color=#ff6666>Click again to kill yourself</color>" : "Suicide", GUILayout.Height(38 * k)))
            {
                if (armed) { me.SuicideRpc(); m_SuicideArmedUntil = 0f; pc.Paused = false; }
                else m_SuicideArmedUntil = Time.unscaledTime + 3f;
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (Btn("Leave game", GUILayout.Height(38 * k))) boot.Leave();
            GUILayout.EndArea();
        }

        float m_SuicideArmedUntil;

        void DrawDevMenu(PlayerNet me, NetGame game)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            float w = Mathf.Min(sw - 40, 760 * k), h = Mathf.Min(sh - 40, 620 * k);
            var r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            MouseOverUI = true;
            Fill(r, new Color(0.08f, 0.05f, 0.05f, 0.95f));
            GUILayout.BeginArea(new Rect(r.x + 20 * k, r.y + 14 * k, r.width - 40 * k, r.height - 28 * k));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt(26 * k)}>DEV SETTINGS</size></b>", m_Label);
            GUILayout.FlexibleSpace();
            if (Btn("Back", GUILayout.Width(120 * k), GUILayout.Height(34 * k))) m_PausePage = PausePage.Root;
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#bbbbbb>For testing. Everyone in the match is told when you use one.</color>", m_Small);
            string timer = game == null ? "" : $"{game.S}  ·  {Clock(game.TimeLeft)} left" + (game.TimerPaused.Value ? "  <color=#ffcc66>(PAUSED)</color>" : "");
            GUILayout.Label(timer, m_Label);

            if (SetDevSearch != null) { m_DevQuery = SetDevSearch; SetDevSearch = null; }
            m_DevQuery = SearchBox("search dev", m_DevQuery, "type to find a setting (ball, wood, teleport...)");
            bool searching = !string.IsNullOrWhiteSpace(m_DevQuery);

            void Row(params (string label, DevCmd cmd)[] buttons)
            {
                GUILayout.BeginHorizontal();
                foreach (var (label, cmd) in buttons)
                    if (Btn(searching ? Highlight(label, m_DevQuery) : label, GUILayout.Height(32 * k))) me.DevRpc(cmd);
                GUILayout.EndHorizontal();
            }
            var sections = new (string title, (string label, DevCmd cmd)[][] rows)[]
            {
                ("MATCH", new[]
                {
                    new[] { ("Drop the wall now", DevCmd.DropWallNow), (game != null && game.TimerPaused.Value ? "Resume timer" : "Pause timer", DevCmd.TogglePauseTimer), ("Timer to 10s", DevCmd.TimerTo10s) },
                    new[] { ("+1 minute", DevCmd.AddMinute), ("-1 minute", DevCmd.SubMinute), ("Start sudden death", DevCmd.StartSuddenDeath), ("Win now", DevCmd.WinNow) },
                }),
                ("WORLD", new[]
                {
                    new[] { ("Spawn airdrop now", DevCmd.SpawnAirdrop), ("Ball to me", DevCmd.BallToMe), ("Ball to middle", DevCmd.BallToMiddle), ("Regrow nodes", DevCmd.RegrowNodes) },
                    new[] { ("Spawn horse", DevCmd.SpawnHorse) },
                }),
                ("ME", new[]
                {
                    new[] { ("+1000 wood", DevCmd.GiveWood), ("+1000 stone", DevCmd.GiveStone), ("+50 arrows", DevCmd.GiveArrows), ("Unlock workbench", DevCmd.UnlockBench) },
                    new[] { ("All airdrop items", DevCmd.GiveOpItems), ("One of every craftable", DevCmd.GiveCraftables), ("Clear inventory", DevCmd.ClearInventory) },
                    new[] { ("Heal", DevCmd.HealFull), ("God mode on/off", DevCmd.ToggleGod), ("Kill me", DevCmd.KillMe) },
                }),
                ("TELEPORT", new[]
                {
                    new[] { ("My base", DevCmd.TpMyBase), ("Enemy base", DevCmd.TpEnemyBase), ("The ball", DevCmd.TpBall), ("The airdrop", DevCmd.TpAirdrop) },
                }),
            };
            int matches = 0;
            foreach (var (title, rows) in sections)
            {
                if (!searching)
                {
                    Caption(title);
                    foreach (var row in rows) Row(row);
                    continue;
                }
                // searching: just the buttons that match (their name or the section's), four to a row
                var hits = new List<(string label, DevCmd cmd)>();
                foreach (var row in rows)
                    foreach (var b in row)
                        if (Matches(m_DevQuery, b.label + " " + title)) hits.Add(b);
                if (hits.Count == 0) continue;
                matches += hits.Count;
                Caption(title);
                for (int i = 0; i < hits.Count; i += 4) Row(hits.GetRange(i, Mathf.Min(4, hits.Count - i)).ToArray());
            }
            if (searching && matches == 0) GUILayout.Label($"<color=#ffcc66>Nothing matches \"{m_DevQuery.Trim()}\".</color>", m_Label);
            DevMatches = searching ? matches : -1;
            GUILayout.EndArea();
        }
    }
}
