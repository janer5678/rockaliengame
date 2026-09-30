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
        bool m_ShowPort, m_ShowModes;
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
            // Esc on the main menu goes back a page
            var boot = Bootstrap.I;
            if (boot != null && !boot.InSession && Input.GetKeyDown(KeyCode.Escape) && !m_Rebinding && m_RebindFrame != Time.frameCount && m_Page != MenuPage.Main)
            {
                m_Page = MenuPage.Main;
                ClickSound();
            }
        }

        /// <summary>Esc in a match: cancels a rebind or goes back to the pause menu. True if it was used up here.</summary>
        public static bool BackOut()
        {
            if (s_I == null) return false;
            if (s_I.m_Rebinding || s_I.m_RebindFrame == Time.frameCount) { s_I.m_Rebinding = false; return true; }
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

            key = Bootstrap.MapChoice;
            int flags = key & ~15;
            GUILayout.BeginHorizontal();
            RowLabel("Map");
            if (Choice(kind == MapKind.Plains, "Plains", GUILayout.Height(30 * k))) boot.SetMapChoice((int)MapKind.Plains | flags);
            if (Choice(kind == MapKind.Highlands, "Highlands (wild)", GUILayout.Height(30 * k))) boot.SetMapChoice((int)MapKind.Highlands | flags);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            RowLabel("Size");
            foreach (var sz in new[] { MapSize.Small, MapSize.Big, MapSize.Large, MapSize.Huge })
                if (Choice(size == sz, Cfg.SizeLabel(sz), GUILayout.Height(30 * k)))
                    boot.SetMapChoice((key & ~Cfg.SmallBit & ~(3 << Cfg.SizeShift)) | ((int)sz << Cfg.SizeShift));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            RowLabel("Mode");
            if (Choice(!wood, "Normal", GUILayout.Height(30 * k))) boot.SetMapChoice(key & ~Cfg.WoodBit);
            if (Choice(wood, "Wood mode", GUILayout.Height(30 * k))) boot.SetMapChoice(key | Cfg.WoodBit);
            GUILayout.EndHorizontal();
            if (wood) GUILayout.Label("<color=#d9a066>Wood only: no stone, no pickaxe, everything costs wood.</color>", m_SmallWrap);

            GUILayout.BeginHorizontal();
            RowLabel("Testing");
            Bootstrap.Solo = ToggleBtn(Bootstrap.Solo, "Solo test", GUILayout.Height(30 * k));
            Bootstrap.Fast = ToggleBtn(Bootstrap.Fast, "Fast timers", GUILayout.Height(30 * k));
            GUILayout.EndHorizontal();
            if (Bootstrap.Solo || Bootstrap.Fast)
                GUILayout.Label("<color=#bbbbbb>" + (Bootstrap.Solo ? "Solo test: the match starts without an opponent. " : "") + (Bootstrap.Fast ? $"Fast timers: {Cfg.FastBallDropDelay:0}s ball drop, {Cfg.FastMatchLength:0}s match." : "") + "</color>", m_SmallWrap);

            GUILayout.Space(4 * k);
            GUILayout.BeginHorizontal();
            if (Btn("MODE OPTIONS", GUILayout.Height(34 * k))) m_Page = MenuPage.ModeOptions;
            if (Btn("CHANGE VALUES", GUILayout.Height(34 * k))) m_Page = MenuPage.Values;
            GUILayout.EndHorizontal();
            GUILayout.Label($"<color=#bbbbbb>{ModeOptionsSummary(key)}</color>", m_SmallWrap);

            Caption("PLAY");
            if (Btn("HOST GAME", m_Primary, GUILayout.Height(44 * k))) boot.Host();
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
            float w = 640 * k, h = 720 * k;
            var r = new Rect((Screen.width - w) / 2, Mathf.Max(10, (Screen.height - h) / 2), w, Mathf.Min(h, Screen.height - 20));
            Fill(r, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            GUILayout.BeginArea(new Rect(r.x + 20 * k, r.y + 14 * k, r.width - 40 * k, r.height - 28 * k));
            GUILayout.Label("<b>MODE OPTIONS</b>", m_Big);
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
            GUILayout.Label("AIRDROP ITEMS  <color=#bbbbbb>(click to pick)</color>", m_Caption);
            GUILayout.FlexibleSpace();
            if (Btn("All", GUILayout.Width(70 * k), GUILayout.Height(26 * k))) { Cfg.AirdropItemMask = (1 << Cfg.AirdropChoices.Length) - 1; Cfg.SavePrefs(); }
            if (Btn("None", GUILayout.Width(70 * k), GUILayout.Height(26 * k))) { Cfg.AirdropItemMask = 0; Cfg.SavePrefs(); }
            GUILayout.EndHorizontal();
            const int cols = 3;
            float cw = (r.width - 40 * k) / cols - 6 * k;
            for (int i = 0; i < Cfg.AirdropChoices.Length; i++)
            {
                if (i % cols == 0) GUILayout.BeginHorizontal();
                var it = Cfg.AirdropChoices[i];
                bool on = (Cfg.AirdropItemMask & (1 << i)) != 0;
                var cell = GUILayoutUtility.GetRect(cw, 46 * k, GUILayout.Width(cw));
                // drawn first, then ONE invisible button over the whole cell (nothing else on it takes the click)
                bool hover = cell.Contains(Event.current.mousePosition);
                Fill(cell, on ? new Color(0.2f, 0.45f, 0.25f, hover ? 0.95f : 0.8f) : new Color(0.2f, 0.2f, 0.2f, hover ? 0.8f : 0.6f));
                var icon = ItemIcons.Get(it);
                if (icon != null) GUI.DrawTexture(new Rect(cell.x + 4, cell.y + 3, 40 * k, 40 * k), icon, ScaleMode.ScaleToFit, true);
                string label = it == Item.BombBush ? "Bomb Bush" : it == Item.RocketLauncher ? "Rocket" : Cfg.ItemName(it);
                GUI.Label(new Rect(cell.x + 48 * k, cell.y, cell.width - 48 * k, cell.height), (on ? "<b>" : "<color=#888888>") + label + (on ? "</b>" : "</color>"), new GUIStyle(m_Small) { alignment = TextAnchor.MiddleLeft, wordWrap = true });
                if (BtnAt(cell, "", GUIStyle.none)) { Cfg.AirdropItemMask ^= 1 << i; Cfg.SavePrefs(); }
                GUILayout.Space(6 * k);
                if (i % cols == cols - 1 || i == Cfg.AirdropChoices.Length - 1) { GUILayout.EndHorizontal(); GUILayout.Space(5 * k); }
            }
            if ((Cfg.AirdropItemMask & ((1 << Cfg.AirdropChoices.Length) - 1)) == 0)
                GUILayout.Label("<color=#ffcc66>Nothing picked: airdrops will have any of these.</color>", m_Small);

            GUILayout.Space(10 * k);
            bool loot = (key & Cfg.RespawnLootBit) != 0;
            GUILayout.BeginHorizontal();
            RowLabel("Respawn", 90 * k);
            if (Choice(!loot, "Normal", GUILayout.Height(bh))) boot.SetMapChoice(key & ~Cfg.RespawnLootBit);
            if (Choice(loot, "With an airdrop item", GUILayout.Height(bh))) boot.SetMapChoice(key | Cfg.RespawnLootBit);
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (Btn("Done", GUILayout.Height(40 * k))) m_Page = MenuPage.Main;
            GUILayout.EndArea();
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

            m_ValuesScroll = GUILayout.BeginScrollView(m_ValuesScroll);
            // group the fields by section, keeping Cfg's order
            var sections = new List<string>();
            var bySection = new Dictionary<string, List<System.Reflection.FieldInfo>>();
            foreach (var f in Cfg.TuneFields)
            {
                var sec = Cfg.SectionOf(f);
                if (sec == "Mode options") continue;
                if (!bySection.TryGetValue(sec, out var l)) { bySection[sec] = l = new List<System.Reflection.FieldInfo>(); sections.Add(sec); }
                l.Add(f);
            }
            float colW = (w - 70 * k) / 2f;
            foreach (var sec in sections)
            {
                var fields = bySection[sec];
                int changed = 0;
                foreach (var f in fields) if (!Cfg.IsDefault(f)) changed++;
                string extra = $"   <color=#aaaaaa>{fields.Count} value{(fields.Count == 1 ? "" : "s")}</color>" + (changed > 0 ? $"   <color=#8fe38f>{changed} changed</color>" : "");
                if (!Section("v:" + sec, sec.ToUpper(), extra)) continue;
                for (int i = 0; i < fields.Count; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < 2 && i + c < fields.Count; c++)
                    {
                        var f = fields[i + c];
                        bool ch = !Cfg.IsDefault(f);
                        GUILayout.Space(12 * k);
                        GUILayout.Label((ch ? "<color=#8fe38f>" : "") + Pretty(f.Name) + (ch ? "</color>" : ""), m_Small, GUILayout.Width(colW * 0.62f), GUILayout.Height(24 * k));
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

        /// <summary>The settings window (main menu and pause menu). Returns true when Back is pressed.</summary>
        bool DrawSettingsPanel()
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            float w = Mathf.Min(sw - 40, 900 * k), h = Mathf.Min(sh - 40, 760 * k);
            var r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(r, new Color(0.05f, 0.05f, 0.06f, 0.95f));
            bool back = false;
            GUILayout.BeginArea(new Rect(r.x + 20 * k, r.y + 14 * k, r.width - 40 * k, r.height - 28 * k));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt(28 * k)}>SETTINGS</size></b>", m_Label, GUILayout.Height(40 * k));
            GUILayout.FlexibleSpace();
            if (Btn("Back", GUILayout.Width(120 * k), GUILayout.Height(34 * k))) back = true;
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
            Caption("SCREEN");
            GUILayout.BeginHorizontal();
            RowLabel("Window", 170 * k);
            foreach (var mode in new[] { GameSettings.WindowMode.Fullscreen, GameSettings.WindowMode.Borderless, GameSettings.WindowMode.Windowed })
                if (Choice(m_ModeSel == mode, mode == GameSettings.WindowMode.Borderless ? "Borderless window" : mode.ToString(), GUILayout.Height(30 * k))) m_ModeSel = mode;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            RowLabel("Resolution", 170 * k);
            if (Btn("<", GUILayout.Width(44 * k), GUILayout.Height(30 * k))) { m_ResSel = Mathf.Min(m_ResList.Count - 1, m_ResSel + 1); m_RateSel = 0; }
            var res = m_ResList[Mathf.Clamp(m_ResSel, 0, m_ResList.Count - 1)];
            bool native = res.x == Screen.currentResolution.width && res.y == Screen.currentResolution.height;
            GUILayout.Label($"<b>{res.x} x {res.y}</b>{(native ? "  <color=#aaaaaa>(your screen)</color>" : "")}", m_Center, GUILayout.Width(300 * k), GUILayout.Height(30 * k));
            if (Btn(">", GUILayout.Width(44 * k), GUILayout.Height(30 * k))) { m_ResSel = Mathf.Max(0, m_ResSel - 1); m_RateSel = 0; }
            GUILayout.EndHorizontal();

            var rates = GameSettings.RefreshRates(res);
            m_RateSel = Mathf.Clamp(m_RateSel, 0, rates.Count - 1);
            GUILayout.BeginHorizontal();
            RowLabel("Refresh rate", 170 * k);
            if (Btn("<", GUILayout.Width(44 * k), GUILayout.Height(30 * k))) m_RateSel = Mathf.Min(rates.Count - 1, m_RateSel + 1);
            GUILayout.Label($"<b>{rates[m_RateSel].value:0.##} Hz</b>{(m_RateSel == 0 ? "  <color=#aaaaaa>(highest)</color>" : "")}", m_Center, GUILayout.Width(300 * k), GUILayout.Height(30 * k));
            if (Btn(">", GUILayout.Width(44 * k), GUILayout.Height(30 * k))) m_RateSel = Mathf.Max(0, m_RateSel - 1);
            GUILayout.EndHorizontal();

            GUILayout.Space(8 * k);
            GUILayout.BeginHorizontal();
            if (Btn("Apply", m_Primary, GUILayout.Width(200 * k), GUILayout.Height(38 * k))) GameSettings.SetDisplay(res, m_ModeSel, rates[m_RateSel]);
            if (Btn("Use my screen's best", GUILayout.Width(240 * k), GUILayout.Height(38 * k)))
            {
                m_ResSel = Mathf.Max(0, m_ResList.IndexOf(new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height)));
                m_RateSel = 0;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"<color=#bbbbbb>Now: {Screen.width} x {Screen.height}, {GameSettings.CurrentMode}, {GameSettings.ChosenRate.value:0.##} Hz. The refresh rate starts at the highest your screen can do; in a window it follows your desktop.</color>", m_SmallWrap);
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
                m_PausePage = OpenPause == 1 ? PausePage.Settings : PausePage.Root;
                m_Tab = SettingsTab.Controls;
                OpenPause = -1;
            }
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.55f));
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
            GUILayout.FlexibleSpace();
            if (Btn("Leave game", GUILayout.Height(38 * k))) boot.Leave();
            GUILayout.EndArea();
        }

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

            void Row(params (string label, DevCmd cmd)[] buttons)
            {
                GUILayout.BeginHorizontal();
                foreach (var (label, cmd) in buttons)
                    if (Btn(label, GUILayout.Height(32 * k))) me.DevRpc(cmd);
                GUILayout.EndHorizontal();
            }
            Caption("MATCH");
            Row(("Drop the wall now", DevCmd.DropWallNow), (game != null && game.TimerPaused.Value ? "Resume timer" : "Pause timer", DevCmd.TogglePauseTimer), ("Timer to 10s", DevCmd.TimerTo10s));
            Row(("+1 minute", DevCmd.AddMinute), ("-1 minute", DevCmd.SubMinute), ("Start sudden death", DevCmd.StartSuddenDeath), ("Win now", DevCmd.WinNow));
            Caption("WORLD");
            Row(("Spawn airdrop now", DevCmd.SpawnAirdrop), ("Ball to me", DevCmd.BallToMe), ("Ball to middle", DevCmd.BallToMiddle), ("Regrow nodes", DevCmd.RegrowNodes));
            Row(("Spawn horse", DevCmd.SpawnHorse));
            Caption("ME");
            Row(("+1000 wood", DevCmd.GiveWood), ("+1000 stone", DevCmd.GiveStone), ("+50 arrows", DevCmd.GiveArrows));
            Row(("All airdrop items", DevCmd.GiveOpItems), ("One of every craftable", DevCmd.GiveCraftables), ("Clear inventory", DevCmd.ClearInventory));
            Row(("Heal", DevCmd.HealFull), ("God mode on/off", DevCmd.ToggleGod), ("Kill me", DevCmd.KillMe));
            Caption("TELEPORT");
            Row(("My base", DevCmd.TpMyBase), ("Enemy base", DevCmd.TpEnemyBase), ("The ball", DevCmd.TpBall), ("The airdrop", DevCmd.TpAirdrop));
            GUILayout.EndArea();
        }
    }
}
