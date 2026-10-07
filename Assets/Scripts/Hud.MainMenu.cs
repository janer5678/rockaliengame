using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The main menu players see (the old one, with every mode and setting on one page, is the DEV MAIN MENU now: Tab
    /// swaps to it and back). Over the UFO cruising through the stars (MenuSpace.cs), a punky title and a column of big
    /// buttons: TUTORIAL (with a friend / solo, straight in), MULTIPLAYER (JOIN: the host's IP and port; HOST: battle type
    /// 1V1 / TEAM BATTLE / FREE FOR ALL, then for teams how many v how many - uneven is fine - or for FFA how many players, then the game
    /// mode with its length and options, then the map with pictures of each and its size, then the ship lobby), SOLO (a
    /// match on your own: mode, then map), OPTIONS (the settings) and QUIT. Esc or BACK goes back a screen.
    /// </summary>
    public partial class Hud
    {
        enum NewPage { Root, Tutorial, Multiplayer, Join, Battle, Players, Mode, Map }
        NewPage m_New;
        bool m_DevMenu, m_SoloFlow, m_ModesOpen;
        int m_Battle;              // 0: 1v1, 1: teams, 2: free for all
        int m_CapA = 2, m_CapB = 2, m_FfaN = 4, m_MapPick;
        static readonly MapKind[] k_Maps = { MapKind.Plains, MapKind.Highlands, MapKind.Beach, MapKind.Canyon, MapKind.Frostlake, MapKind.Volcano, MapKind.Ruins };

        /// <summary>The modes the main menu offers (the others are hidden for now; the dev main menu still has them all).</summary>
        public static readonly GameRules[] MenuModes = { GameRules.AutoWood, GameRules.Classic, GameRules.Bedwars, GameRules.ThreeGoal, GameRules.Progress, GameRules.Assassin, GameRules.Domination };

        /// <summary>The dev main menu (the old all-in-one page) is up instead of the new one: Tab swaps them.</summary>
        public static bool DevMenuShown => s_I != null && (s_I.m_DevMenu || (Bootstrap.Testing && !TestNewMenu));
        /// <summary>(tests) show the new main menu / the name screen / one of its screens (1.. NewPage) though it's a test.</summary>
        public static bool TestNewMenu, TestNameScreen;
        /// <summary>The new menu's map page is up: the background is the map itself (MenuScene's flight round it), not the UFO.</summary>
        public static bool MapPreview => s_I != null && !DevMenuShown && s_I.m_New == NewPage.Map;
        int m_PreviewKey = -1;
        public static void TestNewPage(int p) { if (s_I != null) { s_I.m_New = (NewPage)p; s_I.m_Battle = p == (int)NewPage.Players ? 1 : s_I.m_Battle; } }

        // (the menu's accents: the teams' red and blue, the tutorial green; QUIT stays grey)
        static readonly Color k_Red = new Color(1f, 0.3f, 0.25f), k_Acid = new Color(0.65f, 1f, 0.2f), k_Blue = new Color(0.25f, 0.5f, 1f);

        void NewMenuKeys(Bootstrap boot)
        {
            if (boot == null || boot.InSession || Typing || m_Rebinding) return;
            if (Input.GetKeyDown(KeyCode.Tab) && GUIUtility.keyboardControl == 0 && m_Page == MenuPage.Main) { m_DevMenu = !m_DevMenu; ClickSound(); }
            if (!m_DevMenu && m_Page == MenuPage.Main && m_New != NewPage.Root && Input.GetKeyDown(KeyCode.Escape)) { NewBack(); ClickSound(); }
        }

        void NewBack()
        {
            m_ModesOpen = false;
            switch (m_New)
            {
                case NewPage.Join: m_New = NewPage.Multiplayer; break;
                case NewPage.Battle: m_New = m_SoloFlow ? NewPage.Root : NewPage.Multiplayer; break;
                case NewPage.Players: m_New = NewPage.Battle; break;
                case NewPage.Mode: m_New = m_SoloFlow || m_Battle == 0 ? NewPage.Battle : NewPage.Players; break;
                case NewPage.Map: m_New = NewPage.Mode; break;
                default: m_New = NewPage.Root; break;
            }
            // the host's lobby menu: back past the first page closes the lobby (Hud.LobbyBack.cs)
            if (m_LobbyMenu && m_New == NewPage.Root) LobbyMenuLeave(Bootstrap.I);
        }

        void DrawNewMenu(Bootstrap boot)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            bool lobby = boot.InSession; // (the host's lobby menu: Hud.LobbyBack.cs - the lobby stays open behind it)
            if (lobby) DrawLobbyMenuBackdrop();
            if (MenuSpace.Fade > 0.001f) Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, MenuSpace.Fade));
            // a dark wash down the left so the buttons read over the stars
            GUI.DrawTexture(new Rect(0, 0, 760 * k, sh), LeftFade());
            if (!lobby && m_New == NewPage.Map && MenuScene.Fade > 0.001f) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, MenuScene.Fade)); // (the map fading in behind the map page)
            DrawPunkTitle(m_New == NewPage.Root ? MainTitle : PageTitle(), new Vector2(60 * k, 50 * k), m_New == NewPage.Root ? 86 : 64);
            float x = 70 * k, y = (m_New == NewPage.Root ? 230 : 190) * k, w = 470 * k;
            switch (m_New)
            {
                case NewPage.Root:
                    if (BigBtn(ref y, x, w, "TUTORIAL", k_Acid)) m_New = NewPage.Tutorial;
                    if (BigBtn(ref y, x, w, "MULTIPLAYER", k_Red)) m_New = NewPage.Multiplayer;
                    if (BigBtn(ref y, x, w, "SOLO", k_Blue)) { m_SoloFlow = true; m_Battle = 0; m_New = NewPage.Battle; }
                    if (BigBtn(ref y, x, w, "OPTIONS", new Color(1f, 0.8f, 0.2f))) { m_Page = MenuPage.Settings; m_Tab = SettingsTab.Sound; OnTabOpened(); }
                    if (BigBtn(ref y, x, w, "QUIT", new Color(0.7f, 0.7f, 0.75f))) Application.Quit();
                    break;
                case NewPage.Tutorial:
                    if (BigBtn(ref y, x, w, "SOLO", k_Blue)) StartTutorial(boot, true);
                    if (BigBtn(ref y, x, w, "PLAY WITH A FRIEND", k_Red)) StartTutorial(boot, false);
                    Note(ref y, x, w, "Learn the whole game a step at a time. With a friend, they join your IP when the tutorial asks.");
                    break;
                case NewPage.Multiplayer:
                    if (BigBtn(ref y, x, w, "HOST", k_Red)) { m_SoloFlow = false; m_New = NewPage.Battle; }
                    if (BigBtn(ref y, x, w, "JOIN", k_Blue)) { if (lobby) LobbyMenuLeave(boot); m_New = NewPage.Join; }
                    break;
                case NewPage.Join: DrawJoinPage(boot, x, ref y, w); break;
                case NewPage.Battle:
                    if (BigBtn(ref y, x, w, "1V1", k_Blue)) { m_Battle = 0; m_New = NewPage.Mode; }
                    if (BigBtn(ref y, x, w, "TEAM BATTLE", k_Red)) { m_Battle = 1; if (m_SoloFlow) { m_CapA = m_CapB = 2; m_New = NewPage.Mode; } else m_New = NewPage.Players; }
                    if (BigBtn(ref y, x, w, "FREE FOR ALL", k_Acid)) { m_Battle = 2; if (m_SoloFlow) { m_FfaN = 4; m_New = NewPage.Mode; } else m_New = NewPage.Players; }
                    break;
                case NewPage.Players: DrawPlayersPage(x, ref y, w); break;
                case NewPage.Mode: DrawModePage(x, ref y, w); break;
                case NewPage.Map: DrawMapPage(boot); break;
            }
            if (m_New != NewPage.Root && m_New != NewPage.Map)
            {
                y += 14 * k;
                if (SmallBtn(new Rect(x, y, 160 * k, 40 * k), "◀ BACK")) NewBack();
            }
            if (!string.IsNullOrEmpty(boot.Status) && (m_New == NewPage.Join || m_New == NewPage.Root))
                Shadowed(new Rect(x, sh - 70 * k, sw - x, 30 * k), "<color=#ffcc66>" + boot.Status + "</color>", m_Label);
            if (lobby) DrawLobbyMenuNote();
            else Shadowed(new Rect(0, sh - 30 * k, sw - 16 * k, 24 * k), "<color=#ffffff55>TAB: dev main menu</color>", new GUIStyle(m_Small) { alignment = TextAnchor.MiddleRight });
        }


        static Texture2D s_LeftFade;
        /// <summary>A smooth dark fade from the left edge (behind the menu's buttons), clear by its right end.</summary>
        static Texture2D LeftFade()
        {
            if (s_LeftFade != null) return s_LeftFade;
            s_LeftFade = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < 256; x++)
            {
                float u = x / 255f;
                s_LeftFade.SetPixel(x, 0, new Color(0f, 0f, 0f, 0.62f * (1f - u * u * (3f - 2f * u))));
            }
            s_LeftFade.Apply();
            return s_LeftFade;
        }
        string PageTitle()
        {
            switch (m_New)
            {
                case NewPage.Tutorial: return "TUTORIAL";
                case NewPage.Multiplayer: return "MULTIPLAYER";
                case NewPage.Join: return "JOIN GAME";
                case NewPage.Battle: return m_SoloFlow ? "SOLO TYPE" : "MULTIPLAYER TYPE";
                case NewPage.Players: return "PLAYERS";
                case NewPage.Mode: return m_SoloFlow ? "SOLO GAME MODE" : m_Battle == 0 ? "1V1 GAME MODE" : m_Battle == 1 ? "TEAM BATTLE GAME MODE" : "FREE FOR ALL GAME MODE";
                case NewPage.Map: return "CHOOSE MAP";
                default: return MainTitle;
            }
        }

        // ------------------------------------------------------------------ the pages

        void StartTutorial(Bootstrap boot, bool solo)
        {
            int key = Bootstrap.MapChoice & ~(Cfg.RulesMask << Cfg.RulesShift);
            key = Cfg.WithCaps(key, null);
            Bootstrap.MapChoice = key | ((int)GameRules.Tutorial << Cfg.RulesShift);
            PlayerPrefs.SetInt("RockGame.Map", Bootstrap.MapChoice);
            boot.Host(solo);
        }

        void DrawJoinPage(Bootstrap boot, float x, ref float y, float w)
        {
            float k = m_Scale;
            GUI.Label(new Rect(x, y, w, 26 * k), "<b>HOST IP</b>", m_Label);
            y += 28 * k;
            boot.Ip = GUI.TextField(new Rect(x, y, w - 130 * k, 46 * k), boot.Ip, new GUIStyle(m_Field) { fontSize = Mathf.RoundToInt(24 * k) });
            if (SmallBtn(new Rect(x + w - 122 * k, y, 122 * k, 46 * k), $"port {(m_ShowPort ? "▲" : "▼")}")) m_ShowPort = !m_ShowPort;
            y += 54 * k;
            if (m_ShowPort)
            {
                GUI.Label(new Rect(x, y, 70 * k, 34 * k), "Port", m_Label);
                boot.Port = GUI.TextField(new Rect(x + 70 * k, y, 120 * k, 34 * k), boot.Port, m_Field);
                GUI.Label(new Rect(x + 200 * k, y, w - 200 * k, 34 * k), "<color=#bbbbbb>UDP · 7777 unless the host changed it</color>", m_Small);
                y += 42 * k;
            }
            y += 6 * k;
            if (BigBtn(ref y, x, w, "JOIN", k_Acid)) boot.Join();
            Note(ref y, x, w, "Ask the host for their room ID (the lobby's COPY ROOM ID) and paste it in.");
        }

        void DrawPlayersPage(float x, ref float y, float w)
        {
            float k = m_Scale;
            var big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(64 * k), fontStyle = FontStyle.Bold };
            if (m_Battle == 1)
            {
                // how many v how many (uneven is fine)
                float col = 150 * k;
                Counter(new Rect(x, y, col, 170 * k), ref m_CapA, 1, 4, Cfg.TeamColor[0], "BLUE");
                GUI.Label(new Rect(x + col, y + 40 * k, 90 * k, 80 * k), "<b>V</b>", big);
                Counter(new Rect(x + col + 90 * k, y, col, 170 * k), ref m_CapB, 1, 4, Cfg.TeamColor[1], "RED");
                y += 186 * k;
                Note(ref y, x, w, $"{m_CapA} v {m_CapB}" + (m_CapA != m_CapB ? " - uneven teams: the smaller side has the same base." : ""));
            }
            else
            {
                Counter(new Rect(x, y, 150 * k, 170 * k), ref m_FfaN, 3, 4, k_Blue, "PLAYERS");
                y += 186 * k;
                Note(ref y, x, w, $"Free for all: {m_FfaN} players, a base each.");
            }
            if (BigBtn(ref y, x, w, "CONTINUE", k_Acid)) m_New = NewPage.Mode;
        }

        /// <summary>A big number with + and - (and a name) for the PLAYERS screen.</summary>
        void Counter(Rect r, ref int v, int min, int max, Color c, string label)
        {
            float k = m_Scale;
            Fill(r, new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 0.75f));
            Frame(r, c, 3f);
            var lab = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(18 * k), fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(r.x, r.y + 8 * k, r.width, 26 * k), label, lab);
            var num = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(68 * k), fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(r.x, r.y + 34 * k, r.width, 80 * k), v.ToString(), num);
            float bw = r.width / 2f - 12 * k;
            if (SmallBtn(new Rect(r.x + 8 * k, r.yMax - 46 * k, bw, 38 * k), "−") && v > min) v--;
            if (SmallBtn(new Rect(r.x + r.width / 2f + 4 * k, r.yMax - 46 * k, bw, 38 * k), "+") && v < max) v++;
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
        }

        void DrawModePage(float x, ref float y, float w)
        {
            // in the menu's own look (not the settings panel's): a big drop-down of the modes, the length on chunky
            // + / - buttons, the options under it, and the big CONTINUE
            float k = m_Scale;
            int key = Bootstrap.MapChoice;
            var rules = (GameRules)Mathf.Clamp((key >> Cfg.RulesShift) & Cfg.RulesMask, 0, (int)Cfg.LastRules);
            if (System.Array.IndexOf(MenuModes, rules) < 0) { rules = MenuModes[0]; key = (key & ~(Cfg.RulesMask << Cfg.RulesShift)) | ((int)rules << Cfg.RulesShift); }
            // the drop-down
            var dr = new Rect(x, y, w, 58 * k);
            bool hover = dr.Contains(Event.current.mousePosition);
            if (hover) MouseOverUI = true;
            Fill(dr, hover ? new Color(k_Acid.r, k_Acid.g, k_Acid.b, 0.25f) : new Color(0.03f, 0.02f, 0.06f, 0.85f));
            Fill(new Rect(dr.x, dr.y, 10 * k, dr.height), k_Acid);
            var big = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(28 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(dr.x + 28 * k, dr.y, dr.width - 80 * k, dr.height), Cfg.RulesName(rules).ToUpper(), big);
            GUI.Label(new Rect(dr.xMax - 54 * k, dr.y, 44 * k, dr.height), m_ModesOpen ? "▲" : "▼", new GUIStyle(big) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(dr, GUIContent.none, GUIStyle.none)) { m_ModesOpen = !m_ModesOpen; ClickSound(); }
            y += 64 * k;
            if (m_ModesOpen)
            {
                foreach (var gr in MenuModes)
                {
                    var rr = new Rect(x + 18 * k, y, w - 18 * k, 40 * k);
                    bool on = gr == rules;
                    if (on) { Fill(rr, new Color(k_Acid.r, k_Acid.g, k_Acid.b, 0.3f)); Frame(rr, k_Acid, 2f); }
                    if (SmallBtn(rr, Cfg.RulesName(gr).ToUpper())) { key = (key & ~(Cfg.RulesMask << Cfg.RulesShift)) | ((int)gr << Cfg.RulesShift); m_ModesOpen = false; rules = gr; }
                    y += 44 * k;
                }
                y += 4 * k;
            }
            Bootstrap.MapChoice = key;
            var desc = new GUIStyle(m_SmallWrap) { fontSize = Mathf.RoundToInt(15 * k) };
            float dh = desc.CalcHeight(new GUIContent(Cfg.RulesDesc(rules)), w);
            Shadowed(new Rect(x, y, w, dh), $"<color=#ffd24a>{Cfg.RulesDesc(rules)}</color>", desc);
            y += dh + 14 * k;
            // the game length: the build phase behind the glass wall, then the match with the ball
            var cap = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(16 * k), fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(x, y, w, 24 * k), "GAME LENGTH", cap);
            y += 28 * k;
            float bw = 42 * k, bh = 42 * k, lw = 130 * k;
            var val = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(20 * k), fontStyle = FontStyle.Bold };
            float cx = x;
            if (SmallBtn(new Rect(cx, y, bw, bh), "−")) { Cfg.BallDropDelay = Mathf.Max(30f, Cfg.BallDropDelay - 30f); Cfg.SavePrefs(); }
            GUI.Label(new Rect(cx + bw, y, lw, bh), $"{Clock(Cfg.BallDropDelay)} <size={Mathf.RoundToInt(13 * k)}>build</size>", val);
            if (SmallBtn(new Rect(cx + bw + lw, y, bw, bh), "+")) { Cfg.BallDropDelay = Mathf.Min(1800f, Cfg.BallDropDelay + 30f); Cfg.SavePrefs(); }
            cx += bw * 2 + lw + 18 * k;
            if (SmallBtn(new Rect(cx, y, bw, bh), "−")) { Cfg.MatchLength = Mathf.Max(60f, Cfg.MatchLength - 60f); Cfg.SavePrefs(); }
            GUI.Label(new Rect(cx + bw, y, lw, bh), $"{Clock(Cfg.MatchLength)} <size={Mathf.RoundToInt(13 * k)}>match</size>", val);
            if (SmallBtn(new Rect(cx + bw + lw, y, bw, bh), "+")) { Cfg.MatchLength = Mathf.Min(3600f, Cfg.MatchLength + 60f); Cfg.SavePrefs(); }
            y += bh + 14 * k;
            // the options pages, and what's set
            float half = (w - 10 * k) * 0.5f;
            if (SmallBtn(new Rect(x, y, half, 40 * k), "MODE OPTIONS")) m_Page = MenuPage.ModeOptions;
            if (SmallBtn(new Rect(x + half + 10 * k, y, half, 40 * k), "CHANGE VALUES")) m_Page = MenuPage.Values;
            y += 46 * k;
            Shadowed(new Rect(x, y, w, 22 * k), $"<color=#bbbbbb>{ModeOptionsSummary(key)}</color>", m_Small);
            y += 30 * k;
            if (BigBtn(ref y, x, w, "CONTINUE", k_Acid)) { m_New = NewPage.Map; m_MapPick = Mathf.Max(0, System.Array.IndexOf(k_Maps, Cfg.Map)); }
        }

        /// <summary>The game mode as a drop-down of the menu's modes, with what it is under it. Returns the new map key.</summary>
        int DrawRulesPicker(int key)
        {
            float k = m_Scale;
            var rules = (GameRules)Mathf.Clamp((key >> Cfg.RulesShift) & Cfg.RulesMask, 0, (int)Cfg.LastRules);
            if (System.Array.IndexOf(MenuModes, rules) < 0) { rules = MenuModes[0]; key = (key & ~(Cfg.RulesMask << Cfg.RulesShift)) | ((int)rules << Cfg.RulesShift); }
            GUILayout.BeginHorizontal();
            RowLabel("Game mode");
            if (Btn($"<b>{Cfg.RulesName(rules).ToUpper()}</b>      {(m_ModesOpen ? "▲" : "▼")}", GUILayout.Height(36 * k))) m_ModesOpen = !m_ModesOpen;
            GUILayout.EndHorizontal();
            if (m_ModesOpen)
                foreach (var gr in MenuModes)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(103 * k);
                    if (Choice(rules == gr, Cfg.RulesName(gr), GUILayout.Height(32 * k))) { key = (key & ~(Cfg.RulesMask << Cfg.RulesShift)) | ((int)gr << Cfg.RulesShift); m_ModesOpen = false; }
                    GUILayout.EndHorizontal();
                }
            rules = (GameRules)((key >> Cfg.RulesShift) & Cfg.RulesMask);
            GUILayout.BeginHorizontal();
            GUILayout.Space(103 * k);
            GUILayout.Label($"<color=#ffd24a>{Cfg.RulesDesc(rules)}</color>", m_SmallWrap);
            GUILayout.EndHorizontal();
            return key;
        }

        /// <summary>The game length: how long behind the glass wall, then how long with the ball.</summary>
        void DrawLengthRow(int key)
        {
            float k = m_Scale;
            GUILayout.BeginHorizontal();
            RowLabel("Game length");
            float wallT = Cfg.BallDropDelay, matchT = Cfg.MatchLength;
            if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.BallDropDelay = Mathf.Max(30f, wallT - 30f); Cfg.SavePrefs(); }
            GUILayout.Label($"<b>{Clock(Cfg.BallDropDelay)}</b> <size={Mathf.RoundToInt(12 * k)}>build</size>", m_Center, GUILayout.Width(96 * k), GUILayout.Height(30 * k));
            if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.BallDropDelay = Mathf.Min(1800f, wallT + 30f); Cfg.SavePrefs(); }
            GUILayout.Space(8 * k);
            if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.MatchLength = Mathf.Max(60f, matchT - 60f); Cfg.SavePrefs(); }
            GUILayout.Label($"<b>{Clock(Cfg.MatchLength)}</b> <size={Mathf.RoundToInt(12 * k)}>ball</size>", m_Center, GUILayout.Width(96 * k), GUILayout.Height(30 * k));
            if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) { Cfg.MatchLength = Mathf.Min(3600f, matchT + 60f); Cfg.SavePrefs(); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(103 * k);
            GUILayout.Label($"<color=#bbbbbb>{Clock(Cfg.BallDropDelay)} to gather and build behind the glass wall, then {Clock(Cfg.MatchLength)} with the ball.</color>", m_SmallWrap);
            GUILayout.EndHorizontal();
        }

        /// <summary>(the lobby's GAME OPTIONS) the two team sizes, each 1..4. Returns the new map key.</summary>
        int DrawCapsRow(int key)
        {
            float k = m_Scale;
            int a = Cfg.TeamCap(0), b = Cfg.TeamCap(1);
            if ((key & Cfg.CapsBit) != 0) { a = ((key >> Cfg.CapsShift) & 3) + 1; b = ((key >> (Cfg.CapsShift + 2)) & 3) + 1; }
            GUILayout.BeginHorizontal();
            RowLabel("Teams");
            if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k)) && a > 1) a--;
            GUILayout.Label($"<b>{a}</b>", m_Center, GUILayout.Width(40 * k), GUILayout.Height(30 * k));
            if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k)) && a < 4) a++;
            GUILayout.Label("v", m_Center, GUILayout.Width(30 * k), GUILayout.Height(30 * k));
            if (Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k)) && b > 1) b--;
            GUILayout.Label($"<b>{b}</b>", m_Center, GUILayout.Width(40 * k), GUILayout.Height(30 * k));
            if (Btn("+", GUILayout.Width(34 * k), GUILayout.Height(30 * k)) && b < 4) b++;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return WithTeams(key, a, b);
        }

        /// <summary>A two-team map key with these sizes: the mode that fits the bigger side, and the sizes in it.</summary>
        static int WithTeams(int key, int a, int b)
        {
            int big = Mathf.Max(a, b);
            var mode = big >= 4 ? GameMode.Teams4 : big == 3 ? GameMode.Teams3 : big == 2 ? GameMode.Teams : GameMode.Duel;
            key = (key & ~(Cfg.ModeMask << Cfg.ModeShift)) | ((int)mode << Cfg.ModeShift);
            return a == b && Cfg.ModeTeamSize(mode) == a ? Cfg.WithCaps(key, null) : Cfg.WithCaps(key, new[] { a, b, 1, 1 });
        }

        void DrawMapPage(Bootstrap boot)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            var kind = k_Maps[Mathf.Clamp(m_MapPick, 0, k_Maps.Length - 1)];
            // the background is the map itself: MenuScene's quick, low flight round it (under its dome), rebuilt whenever
            // the map or its size changes
            int key = Bootstrap.MapChoice;
            int want = (key & ~15) | (int)kind;
            // (the host's lobby menu: no rebuilding the map under the running session - its picture is behind instead)
            if (boot.InSession) key = want;
            else if (want != m_PreviewKey && Event.current.type == EventType.Layout) { m_PreviewKey = want; boot.SetMapChoice(want); key = want; }
            // along the bottom, all centred: the name between its arrows, a dot per map, the blurb, the sizes; BACK and
            // the go button out in the corners
            float bandH = 200 * k, by = sh - bandH;
            Fill(new Rect(0, by, sw, bandH), new Color(0f, 0f, 0f, 0.5f));
            float mid = sw / 2f, y = by + 12 * k;
            float nameW = 440 * k, arrow = 56 * k;
            var nm = new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(38 * k) };
            if (SmallBtn(new Rect(mid - nameW / 2f - arrow, y, arrow, arrow), "◀")) m_MapPick = (m_MapPick + k_Maps.Length - 1) % k_Maps.Length;
            if (SmallBtn(new Rect(mid + nameW / 2f, y, arrow, arrow), "▶")) m_MapPick = (m_MapPick + 1) % k_Maps.Length;
            Shadowed(new Rect(mid - nameW / 2f, y, nameW, arrow), $"<b>{ThemeMaps.Label(kind).ToUpper()}</b>", nm);
            y += arrow + 8 * k;
            // which of the maps this is
            float dot = 8 * k, dgap = 8 * k, dx = mid - (k_Maps.Length * dot + (k_Maps.Length - 1) * dgap) / 2f;
            for (int i = 0; i < k_Maps.Length; i++) Fill(new Rect(dx + i * (dot + dgap), y, dot, dot), i == m_MapPick ? k_Acid : new Color(1f, 1f, 1f, 0.3f));
            y += dot + 8 * k;
            string blurb = kind == MapKind.Plains ? "Rolling grass, wheat and forests: the classic map." : kind == MapKind.Highlands ? "Wild hills and rocky ridges between the bases." : ThemeMaps.Blurb(kind);
            Shadowed(new Rect(mid - 450 * k, y, 900 * k, 24 * k), blurb, new GUIStyle(m_SmallWrap) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
            y += 34 * k;
            // the size: one row of four, centred
            var size = (key & Cfg.SmallBit) != 0 ? MapSize.Small : (MapSize)((key >> Cfg.SizeShift) & 3);
            float bw = 112 * k, bgap = 6 * k, bx = mid - (4 * bw + 3 * bgap) / 2f;
            foreach (var sz in new[] { MapSize.Small, MapSize.Big, MapSize.Large, MapSize.Huge })
            {
                var r = new Rect(bx, y, bw, 40 * k);
                bool sel = sz == size;
                if (sel) r = new Rect(r.x - 4 * k, r.y - 5 * k, r.width + 8 * k, r.height + 10 * k); // (the picked one stands out: bigger, solid green, dark text)
                if (SmallBtn(r, Cfg.SizeLabel(sz))) key = (key & ~Cfg.SmallBit & ~(3 << Cfg.SizeShift)) | ((int)sz << Cfg.SizeShift);
                if (sel)
                {
                    Fill(new Rect(r.x - 3 * k, r.y - 3 * k, r.width + 6 * k, r.height + 6 * k), new Color(k_Acid.r, k_Acid.g, k_Acid.b, 0.25f));
                    Fill(r, k_Acid);
                    Frame(r, Color.white, 2f);
                    var ss = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(19 * k), fontStyle = FontStyle.Bold };
                    ss.normal.textColor = new Color(0.05f, 0.08f, 0.02f);
                    GUI.Label(r, Cfg.SizeLabel(sz), ss);
                }
                bx += bw + bgap;
            }
            Bootstrap.MapChoice = key;
            // back, and go: in the corners
            if (SmallBtn(new Rect(40 * k, sh - 72 * k, 150 * k, 48 * k), "◀ BACK")) NewBack();
            float gy = sh - 84 * k;
            if (boot.InSession)
            {
                // the host's lobby menu: back to the lobby with this map (everyone rebuilds it - Hud.LobbyBack.cs)
                if (BigBtn(ref gy, sw - 300 * k, 260 * k, "CONFIRM", k_Acid)) LobbyMenuApply(boot, kind);
            }
            else if (BigBtn(ref gy, sw - 300 * k, 260 * k, m_SoloFlow ? "PLAY" : "HOST", k_Acid)) LaunchFromMenu(boot, kind);
        }

        /// <summary>The menu's choices into the map key, then host (to the ship lobby) or play solo.</summary>
        void LaunchFromMenu(Bootstrap boot, MapKind kind)
        {
            int key = MenuKey(kind);
            Bootstrap.MapChoice = key;
            PlayerPrefs.SetInt("RockGame.Map", key);
            m_New = NewPage.Root;
            m_PreviewKey = -1;
            boot.Host(m_SoloFlow);
        }

        /// <summary>The map key the menu's picks make: this map, the mode page's rules, the battle type and its sizes.</summary>
        int MenuKey(MapKind kind)
        {
            int key = Bootstrap.MapChoice;
            key = (key & ~15) | (int)kind;
            var rules = (GameRules)((key >> Cfg.RulesShift) & Cfg.RulesMask);
            if (System.Array.IndexOf(MenuModes, rules) < 0) key = (key & ~(Cfg.RulesMask << Cfg.RulesShift)) | ((int)MenuModes[0] << Cfg.RulesShift);
            if (m_Battle == 0) key = WithTeams(key, 1, 1);
            else if (m_Battle == 1) key = WithTeams(key, m_CapA, m_CapB);
            else key = Cfg.WithCaps((key & ~(Cfg.ModeMask << Cfg.ModeShift)) | ((int)(m_FfaN >= 4 ? GameMode.Ffa4 : GameMode.Ffa3) << Cfg.ModeShift), null);
            return key;
        }

        // ------------------------------------------------------------------ the look

        /// <summary>The title: big, bold and a bit wonky - white with a hard black shadow (black and white only), the
        /// whole thing tilted and gently rocking.</summary>
        void DrawPunkTitle(string text, Vector2 at, int size)
        {
            float k = m_Scale;
            var st = new GUIStyle(m_Big) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(size * k), fontStyle = FontStyle.Bold, richText = true };
            var m = GUI.matrix;
            float wob = Mathf.Sin(Time.unscaledTime * 1.3f) * 1.2f;
            GUIUtility.RotateAroundPivot(-4f + wob, at + new Vector2(200 * k, 40 * k));
            var r = new Rect(at.x, at.y, Screen.width, size * 1.6f * k);
            // (black and white: a hard black shadow under white letters)
            GUI.Label(new Rect(r.x + 5 * k, r.y + 6 * k, r.width, r.height), $"<color=#000000ee>{text}</color>", st);
            GUI.Label(r, $"<color=#ffffff>{text}</color>", st);
            GUI.matrix = m;
        }

        /// <summary>A big menu button: dark, a coloured bar down its left; on hover it slides right and fills with the
        /// colour. Moves y on past it.</summary>
        bool BigBtn(ref float y, float x, float w, string text, Color c)
        {
            float k = m_Scale, h = 62 * k;
            var r = new Rect(x, y, w, h);
            y += h + 12 * k;
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover) MouseOverUI = true;
            var d = hover ? new Rect(r.x + 14 * k, r.y, r.width, r.height) : r;
            Fill(d, hover ? new Color(c.r, c.g, c.b, 0.9f) : new Color(0.03f, 0.02f, 0.06f, 0.8f));
            Fill(new Rect(d.x, d.y, 10 * k, d.height), c);
            var st = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(32 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            string col = hover ? "#000000" : "#ffffff";
            GUI.Label(new Rect(d.x + 28 * k, d.y, d.width - 28 * k, d.height), $"<color={col}>{text}</color>", st);
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (hit) ClickSound();
            return hit;
        }

        bool SmallBtn(Rect r, string text)
        {
            float k = m_Scale;
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover) MouseOverUI = true;
            Fill(r, hover ? new Color(1f, 1f, 1f, 0.22f) : new Color(0.03f, 0.02f, 0.06f, 0.8f));
            Frame(r, hover ? Color.white : new Color(1f, 1f, 1f, 0.55f), 2f);
            var st = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(20 * k), fontStyle = FontStyle.Bold };
            bool hit = GUI.Button(r, text, st);
            if (hit) ClickSound();
            return hit;
        }

        void Note(ref float y, float x, float w, string text)
        {
            float k = m_Scale;
            Shadowed(new Rect(x, y, w, 44 * k), $"<color=#cccccc>{text}</color>", m_SmallWrap);
            y += 48 * k;
        }
    }
}
