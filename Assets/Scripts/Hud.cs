using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Immediate-mode (OnGUI) main menu + settings, HUD, inventory/crafting/loot screen, pause and end screens.</summary>
    public class Hud : MonoBehaviour
    {
        struct Msg { public string Text; public float Time; }
        static readonly List<Msg> s_Msgs = new List<Msg>();
        /// <summary>Bottom-right feed of what just went into your inventory ("+30 Wood").</summary>
        struct Gain { public Item Id; public int Amount; public float Time; }
        static readonly List<Gain> s_Gains = new List<Gain>();
        readonly Dictionary<Item, int> m_LastCounts = new Dictionary<Item, int>();
        PlayerNet m_CountsFor;
        static float s_HitTime = -10f, s_BannerTime = -10f, s_WakeTime = -10f;
        static bool s_HitKill, s_HitHead;
        static string s_BannerTitle, s_BannerSub;
        public static bool MouseOverUI;
        static readonly System.Text.RegularExpressions.Regex s_ColorTag = new System.Text.RegularExpressions.Regex("</?color[^>]*>");

        bool m_ShowHelp = true, m_ShowSettings, m_ShowDev;
        /// <summary>Build wheel: which slice the mouse is over (-1 = none).</summary>
        public static int WheelHover = -1;
        static Vector2 s_WheelCenter;
        public static void WheelOpened() { s_WheelCenter = new Vector2(Screen.width / 2f, Screen.height / 2f); WheelHover = -1; }
        float m_LastHealth = 100f, m_DamageFlash;
        GUIStyle m_Label, m_Center, m_Big, m_Small, m_Button, m_Box, m_Field;
        float m_Scale = 1f;
        Vector2 m_SettingsScroll;
        readonly Dictionary<string, string> m_EditBuffers = new Dictionary<string, string>();

        // drag & drop
        struct SlotRef { public byte Kind; public int Index; }
        bool m_Dragging, m_DragHalf;
        SlotRef m_DragFrom;
        ItemStack m_DragStack;
        string m_HoverName = "";

        public static void Push(string text)
        {
            Debug.Log("[HUD] " + text);
            s_Msgs.Add(new Msg { Text = text, Time = Time.time });
            if (s_Msgs.Count > 6) s_Msgs.RemoveAt(0);
        }
        public static void HitMarker(bool kill, bool head)
        {
            if (kill || Time.time - s_HitTime > 0.05f || !s_HitKill) { s_HitKill = kill; s_HitHead = head; }
            s_HitTime = Time.time;
        }
        public static void Wake() => s_WakeTime = Time.time;
        public static void Banner(string title, string sub) { s_BannerTitle = title; s_BannerSub = sub; s_BannerTime = Time.time; }
        public static void Clear() { s_Msgs.Clear(); s_Gains.Clear(); s_BannerTime = -10f; Fx.Numbers.Clear(); }

        /// <summary>Materials spent (building, crafting): a red "-15 Wood" line in the same feed.</summary>
        public static void Loss(Item id, int amount)
        {
            if (amount <= 0) return;
            int idx = s_Gains.FindIndex(g => g.Id == id && g.Amount < 0 && Time.time - g.Time < 2.5f);
            if (idx >= 0) { var g = s_Gains[idx]; g.Amount -= amount; g.Time = Time.time; s_Gains[idx] = g; }
            else s_Gains.Add(new Gain { Id = id, Amount = -amount, Time = Time.time });
            if (s_Gains.Count > 7) s_Gains.RemoveAt(0);
        }

        /// <summary>Compare the inventory with last frame: anything that went up gets a "+N item" line.</summary>
        void TrackGains(PlayerNet me)
        {
            if (me != m_CountsFor) { m_CountsFor = me; m_LastCounts.Clear(); }
            if (me == null || !me.IsSpawned || me.Inv.Count == 0) return;
            foreach (Item id in System.Enum.GetValues(typeof(Item)))
            {
                if (id == Item.None) continue;
                int n = me.Count(id);
                bool known = m_LastCounts.TryGetValue(id, out var before);
                m_LastCounts[id] = n;
                if (!known || n <= before) continue;
                int add = n - before;
                // stack onto a recent line for the same item
                int idx = s_Gains.FindIndex(g => g.Id == id && g.Amount > 0 && Time.time - g.Time < 2.5f);
                if (idx >= 0) { var g = s_Gains[idx]; g.Amount += add; g.Time = Time.time; s_Gains[idx] = g; }
                else s_Gains.Add(new Gain { Id = id, Amount = add, Time = Time.time });
                if (s_Gains.Count > 6) s_Gains.RemoveAt(0);
            }
        }

        void Update()
        {
            if (Time.frameCount > 3) ItemIcons.EnsureRendered();
            if (Input.GetKeyDown(KeyCode.F1)) m_ShowHelp = !m_ShowHelp;
            var me = PlayerNet.Local;
            TrackGains(me);
            if (me != null)
            {
                if (me.Health.Value < m_LastHealth - 0.5f) m_DamageFlash = 0.5f;
                m_LastHealth = me.Health.Value;
            }
            m_DamageFlash = Mathf.Max(0, m_DamageFlash - Time.deltaTime);
        }

        void Styles()
        {
            m_Scale = Mathf.Max(0.75f, Screen.height / 900f);
            int fs = Mathf.RoundToInt(16 * m_Scale);
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = fs, richText = true };
            m_Label.normal.textColor = Color.white;
            m_Center = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
            m_Big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(40 * m_Scale), fontStyle = FontStyle.Bold };
            m_Small = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(13 * m_Scale) };
            m_Button = new GUIStyle(GUI.skin.button) { fontSize = fs };
            m_Box = new GUIStyle(GUI.skin.box);
            m_Field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(14 * m_Scale) };
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Shadowed(Rect r, string text, GUIStyle style)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0, 0, 0, 0.8f);
            string plain = s_ColorTag.Replace(text, "");
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), plain, style);
            style.normal.textColor = old;
            GUI.Label(r, text, style);
        }

        static string Clock(float t)
        {
            int s = Mathf.CeilToInt(t);
            return $"{s / 60}:{s % 60:00}";
        }

        void OnGUI()
        {
            Styles();
            MouseOverUI = false;
            var boot = Bootstrap.I;
            if (boot == null) return;
            if (!boot.InSession) { DrawMainMenu(boot); return; }
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            if (me == null || pc == null)
            {
                Shadowed(new Rect(0, Screen.height / 2 - 40, Screen.width, 80), boot.Status != "" ? boot.Status : "Connecting...", m_Big);
                DrawLeaveButton(boot, new Rect(Screen.width / 2 - 100 * m_Scale, Screen.height / 2 + 60, 200 * m_Scale, 40 * m_Scale));
                return;
            }
            DrawGame(boot, me, pc);
        }

        // ------------------------------------------------------------------ main menu

        void DrawMainMenu(Bootstrap boot)
        {
            if (m_ShowSettings) { DrawSettings(); return; }
            float w = 460 * m_Scale, h = 690 * m_Scale;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(r, new Color(0, 0, 0, 0.65f));
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 15, r.width - 40, r.height - 30));
            GUILayout.Label("<b>ROCK BASE BRAWL</b>", m_Big);
            GUILayout.Label("1v1 · gather · build · raid · steal the ball", m_Center);
            GUILayout.Space(15 * m_Scale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host IP", m_Label, GUILayout.Width(90 * m_Scale));
            boot.Ip = GUILayout.TextField(boot.Ip, new GUIStyle(GUI.skin.textField) { fontSize = m_Label.fontSize });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Port", m_Label, GUILayout.Width(90 * m_Scale));
            boot.Port = GUILayout.TextField(boot.Port, new GUIStyle(GUI.skin.textField) { fontSize = m_Label.fontSize });
            GUILayout.EndHorizontal();
            GUILayout.Space(10 * m_Scale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map", m_Label, GUILayout.Width(90 * m_Scale));
            int key = Bootstrap.MapChoice;
            int flags = key & ~15;
            bool small = (key & Cfg.SmallBit) != 0, wood = (key & Cfg.WoodBit) != 0;
            var kind = (MapKind)(key & 15);
            if (GUILayout.Toggle(kind == MapKind.Plains, " Plains", m_Button, GUILayout.Height(30 * m_Scale)) && kind != MapKind.Plains) boot.SetMapChoice((int)MapKind.Plains | flags);
            if (GUILayout.Toggle(kind == MapKind.Highlands, " Highlands (wild)", m_Button, GUILayout.Height(30 * m_Scale)) && kind != MapKind.Highlands) boot.SetMapChoice((int)MapKind.Highlands | flags);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Size", m_Label, GUILayout.Width(90 * m_Scale));
            if (GUILayout.Toggle(!small, " Big", m_Button, GUILayout.Height(30 * m_Scale)) && small) boot.SetMapChoice(key & ~Cfg.SmallBit);
            if (GUILayout.Toggle(small, " Small", m_Button, GUILayout.Height(30 * m_Scale)) && !small) boot.SetMapChoice(key | Cfg.SmallBit);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode", m_Label, GUILayout.Width(90 * m_Scale));
            if (GUILayout.Toggle(!wood, " Normal", m_Button, GUILayout.Height(30 * m_Scale)) && wood) boot.SetMapChoice(key & ~Cfg.WoodBit);
            if (GUILayout.Toggle(wood, " Wood mode", m_Button, GUILayout.Height(30 * m_Scale)) && !wood) boot.SetMapChoice(key | Cfg.WoodBit);
            GUILayout.EndHorizontal();
            if (wood) GUILayout.Label("<color=#d9a066>Wood only: no stone, no pickaxe, everything costs wood.</color>", m_Small);
            bool sides = (key & Cfg.SidesBit) != 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Airdrops", m_Label, GUILayout.Width(90 * m_Scale));
            if (GUILayout.Toggle(!sides, " Anywhere", m_Button, GUILayout.Height(30 * m_Scale)) && sides) boot.SetMapChoice(key & ~Cfg.SidesBit);
            if (GUILayout.Toggle(sides, " One per side", m_Button, GUILayout.Height(30 * m_Scale)) && !sides) boot.SetMapChoice(key | Cfg.SidesBit);
            GUILayout.EndHorizontal();
            GUILayout.Space(6 * m_Scale);
            Bootstrap.Solo = GUILayout.Toggle(Bootstrap.Solo, " Solo test (start without an opponent)", m_Label);
            Bootstrap.Fast = GUILayout.Toggle(Bootstrap.Fast, $" Fast timers ({Cfg.FastBallDropDelay:0}s ball drop, {Cfg.FastMatchLength:0}s match)", m_Label);
            GUILayout.Space(12 * m_Scale);
            if (GUILayout.Button("HOST GAME", m_Button, GUILayout.Height(44 * m_Scale))) boot.Host();
            GUILayout.Space(6 * m_Scale);
            if (GUILayout.Button("JOIN GAME", m_Button, GUILayout.Height(44 * m_Scale))) boot.Join();
            GUILayout.Space(6 * m_Scale);
            if (GUILayout.Button("GAME SETTINGS", m_Button, GUILayout.Height(36 * m_Scale))) m_ShowSettings = true;
            GUILayout.Space(8 * m_Scale);
            if (!string.IsNullOrEmpty(boot.Status)) GUILayout.Label("<color=#ffcc66>" + boot.Status + "</color>", m_Center);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Host picks the options and settings. Your friend joins with your IP (port 7777 UDP).", m_Small);
            GUILayout.EndArea();
        }

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

        /// <summary>Every [Tune] stat in Cfg, editable. Saved on this PC; the host's values are used in a match.</summary>
        void DrawSettings()
        {
            float k = m_Scale;
            float w = Mathf.Min(Screen.width - 40, 900 * k), h = Screen.height - 40;
            var r = new Rect((Screen.width - w) / 2, 20, w, h);
            Fill(r, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b><size=" + Mathf.RoundToInt(28 * k) + ">GAME SETTINGS</size></b>", m_Label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset all to defaults", m_Button, GUILayout.Width(220 * k), GUILayout.Height(34 * k)))
            {
                Cfg.ResetDefaults();
                Cfg.SavePrefs();
                m_EditBuffers.Clear();
            }
            if (GUILayout.Button("Done", m_Button, GUILayout.Width(120 * k), GUILayout.Height(34 * k))) m_ShowSettings = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("When you host, these values are used for the whole match (they're sent to your opponent). Times are in seconds, speeds in m/s. Changed values are highlighted.", m_Small);
            GUILayout.Space(6 * k);

            m_SettingsScroll = GUILayout.BeginScrollView(m_SettingsScroll);
            string section = null;
            int col = 0;
            const int cols = 2;
            foreach (var f in Cfg.TuneFields)
            {
                var sec = Cfg.SectionOf(f);
                if (sec != section)
                {
                    if (section != null && col != 0) GUILayout.EndHorizontal();
                    col = 0;
                    section = sec;
                    GUILayout.Space(8 * k);
                    GUILayout.Label($"<b><color=#ffd27a>{sec.ToUpper()}</color></b>", m_Label);
                }
                if (col == 0) GUILayout.BeginHorizontal();
                bool changed = !Cfg.IsDefault(f);
                GUILayout.Label((changed ? "<color=#8fe38f>" : "") + Pretty(f.Name) + (changed ? "</color>" : ""), m_Small, GUILayout.Width(w * 0.3f));
                if (!m_EditBuffers.TryGetValue(f.Name, out var text)) text = Cfg.Format(f);
                var edited = GUILayout.TextField(text, m_Field, GUILayout.Width(w * 0.13f));
                if (edited != text)
                {
                    m_EditBuffers[f.Name] = edited;
                    if (Cfg.TrySet(f, edited)) Cfg.SavePrefs();
                }
                GUILayout.Space(20 * k);
                col++;
                if (col == cols) { GUILayout.EndHorizontal(); col = 0; }
            }
            if (col != 0) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawLeaveButton(Bootstrap boot, Rect r)
        {
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            if (GUI.Button(r, "Leave game", m_Button)) boot.Leave();
        }

        // ------------------------------------------------------------------ in-game HUD

        void DrawGame(Bootstrap boot, PlayerNet me, PlayerController pc)
        {
            var game = NetGame.Instance;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            int team = me.Team.Value;

            if (m_DamageFlash > 0) Fill(new Rect(0, 0, sw, sh), new Color(0.8f, 0, 0, m_DamageFlash * 0.5f));
            float wake = Time.time - s_WakeTime;
            if (wake < 1.6f)
            {
                float a = Mathf.Clamp01(1f - wake / 1.6f);
                Fill(new Rect(0, 0, sw, sh), new Color(0.75f, 0.95f, 1f, a * a * 0.95f));
                if (wake < 1.1f) Shadowed(new Rect(0, sh * 0.42f, sw, 40 * k), "<color=#12506a>RESPAWNED</color>", m_Center);
            }

            // ---- top centre: phase & timer ----
            string phase = "", sub = "";
            if (game != null)
            {
                switch (game.S)
                {
                    case GameState.Waiting:
                        phase = "Waiting for an opponent to join...";
                        sub = boot.IsHost ? "Tell your friend to join your IP. (Enable 'Solo test' in the menu to play alone)" : "";
                        break;
                    case GameState.PreBall:
                        phase = "Wall drops in " + Clock(game.TimeLeft);
                        sub = Cfg.WoodMode ? "Gather wood and build your base (craft at your machine)" : "Gather wood & stone, build your base (craft at your machine)";
                        break;
                    case GameState.BallLive:
                        phase = "Time left  " + Clock(game.TimeLeft);
                        sub = BallStatus(team);
                        break;
                    case GameState.SuddenDeath:
                        phase = "<color=#ff5555>SUDDEN DEATH</color>  " + Clock(game.TimeLeft);
                        sub = "Rocks only - first kill wins";
                        break;
                }
            }
            if (phase != "")
            {
                Fill(new Rect(sw / 2 - 260 * k, 8, 520 * k, 62 * k), new Color(0, 0, 0, 0.45f));
                Shadowed(new Rect(0, 10, sw, 30 * k), $"<b><size={Mathf.RoundToInt(24 * k)}>{phase}</size></b>", m_Center);
                Shadowed(new Rect(0, 40 * k, sw, 26 * k), sub, m_Center);
            }

            // ---- top left: identity + compass ----
            var tc = Cfg.TeamColor[team];
            Fill(new Rect(10, 10, 230 * k, 30 * k), new Color(tc.r, tc.g, tc.b, 0.6f));
            Shadowed(new Rect(18, 12, 230 * k, 28 * k), $"<b>YOU ARE {Cfg.TeamName[team]}</b>", m_Label);
            if (game == null || game.S != GameState.SuddenDeath)
            {
                Shadowed(new Rect(12, 44 * k, 400 * k, 24 * k), $"Your machine: {Direction(me.transform, Cfg.MachinePos(team))}", m_Small);
                var ball = Ball.Instance;
                if (ball != null) Shadowed(new Rect(12, 64 * k, 400 * k, 24 * k), $"Ball: {Direction(me.transform, ball.transform.position)}", m_Small);
                if (game != null)
                {
                    float ly = 84 * k;
                    foreach (var c in Container.All)
                    {
                        if (!c.IsAirdrop) continue;
                        Shadowed(new Rect(12, ly, 400 * k, 24 * k), $"<color=#c98bff>Airdrop: {Direction(me.transform, c.transform.position)}</color>", m_Small);
                        ly += 20 * k;
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        double st0 = game.LaneStart(i).Value;
                        if (st0 < 0 || me.NetworkManager.ServerTime.Time - st0 >= NetGame.DropLand) continue;
                        Shadowed(new Rect(12, ly, 400 * k, 24 * k), $"<color=#c98bff>Airdrop incoming: {Direction(me.transform, game.LanePos(i).Value)}</color>", m_Small);
                        ly += 20 * k;
                    }
                }
            }

            // ---- messages (top right) ----
            float my = 10;
            for (int i = s_Msgs.Count - 1; i >= 0; i--)
            {
                float age = Time.time - s_Msgs[i].Time;
                if (age > 6f) continue;
                var st = new GUIStyle(m_Label) { alignment = TextAnchor.UpperRight };
                st.normal.textColor = new Color(1, 1, 1, Mathf.Clamp01(6f - age));
                GUI.Label(new Rect(sw - 620 * k - 10, my, 620 * k, 26 * k), s_Msgs[i].Text, st);
                my += 24 * k;
            }

            // ---- crosshair / hit marker / charge ----
            float cx = sw / 2, cy = sh / 2;
            if (!pc.MenuOpen)
            {
                Fill(new Rect(cx - 1, cy - 8, 2, 16), new Color(1, 1, 1, 0.8f));
                Fill(new Rect(cx - 8, cy - 1, 16, 2), new Color(1, 1, 1, 0.8f));
            }
            float hitAge = Time.time - s_HitTime;
            if (hitAge < 0.3f)
            {
                float grow = 1f + (0.3f - hitAge) * (s_HitKill ? 3f : 1.5f);
                var hc = s_HitKill ? new Color(1, 0.15f, 0.15f) : s_HitHead ? new Color(1f, 0.75f, 0.1f) : Color.white;
                int size = Mathf.RoundToInt((s_HitKill ? 40 : s_HitHead ? 32 : 24) * k * grow);
                var st = new GUIStyle(m_Center) { fontSize = size, fontStyle = FontStyle.Bold };
                st.normal.textColor = new Color(hc.r, hc.g, hc.b, Mathf.Clamp01((0.3f - hitAge) * 5f));
                GUI.Label(new Rect(cx - 50, cy - 50, 100, 100), "X", st);
            }
            float charge = Mathf.Max(pc.DrawAmount, pc.RamCharge);
            if (charge > 0)
            {
                Fill(new Rect(cx - 50 * k, cy + 30 * k, 100 * k, 8 * k), new Color(0, 0, 0, 0.5f));
                Fill(new Rect(cx - 50 * k, cy + 30 * k, 100 * k * charge, 8 * k), charge >= 1f ? new Color(1f, 0.85f, 0.2f) : Color.white);
            }
            DrawDamageNumbers(k);

            // ---- aim info / held item hints ----
            float by = sh - 150 * k;
            if (!string.IsNullOrEmpty(pc.AimText) && !pc.MenuOpen) Shadowed(new Rect(0, cy + 45 * k, sw, 26 * k), pc.AimText, m_Center);
            if (!me.CarryingBall && !pc.MenuOpen)
            {
                string hint = HeldHint(me, pc);
                if (hint != "") Shadowed(new Rect(0, by - 52 * k, sw, 26 * k), hint, m_Center);
                if (!string.IsNullOrEmpty(pc.BuildHint)) Shadowed(new Rect(0, by - 28 * k, sw, 26 * k), "<color=#ff8888>" + pc.BuildHint + "</color>", m_Center);
            }

            // ---- hotbar ----
            if (!pc.MenuOpen)
            {
                float slot = 70 * k, gap = 6 * k;
                int n = Cfg.HotbarSize;
                float hx = cx - (n * slot + (n - 1) * gap) / 2, hy = sh - slot - 14 * k;
                for (int i = 0; i < n; i++)
                {
                    var r = new Rect(hx + i * (slot + gap), hy, slot, slot);
                    bool sel = me.HeldSlot.Value == i && !me.CarryingBall;
                    DrawSlotVisual(r, me.SlotAt(i), sel, me);
                    if (sel && me.SlotAt(i).Empty) DrawRockGhost(r);
                    GUI.Label(new Rect(r.x + 4, r.y + 2, 20, 20), (i + 1).ToString(), m_Small);
                }
            }

            // ---- health (bottom left) ----
            float hw = 280 * k;
            var hr = new Rect(14, sh - 44 * k, hw, 28 * k);
            Fill(hr, new Color(0, 0, 0, 0.55f));
            float hp = Mathf.Clamp01(me.Health.Value / Mathf.Max(1f, Cfg.MaxHealth));
            Fill(new Rect(hr.x + 3, hr.y + 3, (hr.width - 6) * hp, hr.height - 6), Color.Lerp(new Color(0.85f, 0.15f, 0.1f), new Color(0.3f, 0.85f, 0.3f), hp));
            Shadowed(new Rect(hr.x + 8, hr.y + 2, hr.width, hr.height), $"<b>HP {me.Health.Value:0}</b>", m_Label);
            float statY = hr.y - 26 * k;
            if (me.ArmorHp.Value > 0)
            {
                // second health bar, used up first
                var ab = new Rect(hr.x, statY, hw, 22 * k);
                Fill(ab, new Color(0, 0, 0, 0.55f));
                Fill(new Rect(ab.x + 2, ab.y + 2, (ab.width - 4) * Mathf.Clamp01(me.ArmorHp.Value / (float)Mathf.Max(1, Cfg.ArmorHp)), ab.height - 4), new Color(0.75f, 0.55f, 0.3f));
                Shadowed(new Rect(ab.x + 6, ab.y, ab.width, ab.height), $"<b>ARMOUR {me.ArmorHp.Value}</b>", m_Small);
                statY -= 26 * k;
            }
            if (me.HelmetHp.Value > 0)
            {
                Shadowed(new Rect(hr.x, statY, 400 * k, 22 * k), "<color=#a8c4e0><b>HELMET</b> (stops one headshot)</color>", m_Small);
                statY -= 22 * k;
            }
            if (VoiceChat.Transmitting)
            {
                Shadowed(new Rect(hr.x, statY, 400 * k, 22 * k), "<color=#7dff9a><b>● MIC ON</b></color>", m_Small);
                statY -= 22 * k;
            }
            if (me.Invisible)
            {
                float left = (float)(me.InvisUntil.Value - me.NetworkManager.ServerTime.Time);
                Shadowed(new Rect(hr.x, statY, 400 * k, 22 * k), me.Hidden ? $"<color=#c98bff><b>INVISIBLE</b> {Mathf.CeilToInt(left)}s</color>" : $"<color=#ff9f5a><b>VISIBLE (attacking)</b></color> <color=#c98bff>{Mathf.CeilToInt(left)}s</color>", m_Label);
            }

            DrawGains(k);

            // ---- help ----
            if (m_ShowHelp && !pc.MenuOpen)
            {
                string help =
                    "<b>CONTROLS</b>  (F1 hide)\n" +
                    "WASD move · Shift sprint · Space jump · Ctrl/C crouch\n" +
                    "LMB attack / gather / place · hold LMB: draw bow / ram\n" +
                    "Spear: hold RMB + LMB to throw · Berries: RMB to eat\n" +
                    "1-7 / scroll: hotbar (empty slot = rock) · TAB: inventory + crafting in base\n" +
                    "E: ball, door, chest, bush, items, horse/car (E again to get off)\n" +
                    "Plan: hold RMB for the building wheel · X demolishes your own pieces\n" +
                    "Ball: LMB throws it · E at your machine to put it in\n" +
                    $"{(GameSettings.VoiceMode == GameSettings.VoicePushToTalk ? "V: push to talk · " : "")}Esc: pause, sound, mic & dev settings";
                var hrct = new Rect(12, 130 * k, 500 * k, 185 * k);
                Fill(hrct, new Color(0, 0, 0, 0.35f));
                GUI.Label(new Rect(hrct.x + 8, hrct.y + 4, hrct.width - 10, hrct.height), help, m_Small);
            }

            // ---- banner ----
            float bage = Time.time - s_BannerTime;
            if (bage < 4f && game != null && game.S != GameState.GameOver)
            {
                float a = Mathf.Clamp01(4f - bage);
                var st = new GUIStyle(m_Big);
                st.normal.textColor = new Color(1, 0.9f, 0.4f, a);
                GUI.Label(new Rect(0, sh * 0.22f, sw, 60 * k), s_BannerTitle, st);
                var st2 = new GUIStyle(m_Center);
                st2.normal.textColor = new Color(1, 1, 1, a);
                GUI.Label(new Rect(0, sh * 0.22f + 55 * k, sw, 30 * k), s_BannerSub, st2);
            }

            // ---- dead ----
            if (me.Dead.Value && (game == null || game.S != GameState.GameOver))
            {
                Fill(new Rect(0, 0, sw, sh), new Color(0.3f, 0, 0, 0.35f));
                float t = Mathf.Max(0, (float)(me.RespawnAt.Value - me.NetworkManager.ServerTime.Time));
                Shadowed(new Rect(0, sh * 0.4f, sw, 60 * k), "YOU DIED", m_Big);
                bool home = game == null || game.WallUp;
                if (me.ChoosingRespawn)
                {
                    Shadowed(new Rect(0, sh * 0.4f + 60 * k, sw, 30 * k), "Your stuff spilled out where you died. Where do you want to respawn?", m_Center);
                    float bw = 260 * k, bh = 50 * k, by2 = sh * 0.4f + 100 * k;
                    var r1 = new Rect(cx - bw - 10 * k, by2, bw, bh);
                    var r2 = new Rect(cx + 10 * k, by2, bw, bh);
                    if (r1.Contains(Event.current.mousePosition) || r2.Contains(Event.current.mousePosition)) MouseOverUI = true;
                    if (GUI.Button(r1, "[1]  RESPAWN IN BASE", m_Button)) pc.ChooseRespawn(false);
                    if (GUI.Button(r2, "[2]  RESPAWN IN THE WILD", m_Button)) pc.ChooseRespawn(true);
                    Shadowed(new Rect(0, by2 + bh + 6 * k, sw, 26 * k), "<color=#bbbbbb>The wild drops you somewhere random in the enemy's half of the map</color>", m_Center);
                }
                else Shadowed(new Rect(0, sh * 0.4f + 60 * k, sw, 30 * k), home
                    ? $"Your stuff spilled out where you died. Respawning on your bedrock in {Mathf.CeilToInt(t)}"
                    : $"Your stuff spilled out where you died. Respawn in {Mathf.CeilToInt(t)}", m_Center);
            }

            if (pc.MenuOpen) DrawInventory(me, pc);
            if (pc.WheelOpen) DrawWheel(pc);

            if (pc.Paused && (game == null || game.S != GameState.GameOver))
            {
                if (m_ShowDev) DrawDevMenu(me, game);
                else DrawPauseMenu(boot, pc);
            }
            else m_ShowDev = false;

            if (game != null && game.S == GameState.GameOver) DrawGameOver(boot, game, team);
        }

        string HeldHint(PlayerNet me, PlayerController pc)
        {
            var s = me.HeldStack;
            switch (s.Id)
            {
                case Item.BuildingPlan:
                    if (pc.DemolishMode) return "<b>Demolish</b>    LMB: take down your own piece (half the wood back)    hold RMB: building wheel";
                    return $"<b>{Cfg.PieceName(pc.BuildPiece)}</b>  ({Cfg.PieceWood(pc.BuildPiece)} wood)    hold RMB: building wheel   R: rotate stairs   " + (Cfg.WoodMode ? "" : "F: upgrade to stone   ") + "X: demolish yours";
                case Item.Ram: return $"<b>Battering Ram</b> ({s.Data} hits left)    hold LMB at an enemy piece: wood breaks instantly, stone drops to wood";
                case Item.Spear: return "<b>Spear</b>    LMB: stab    hold RMB + LMB: throw    E: pick thrown spears back up";
                case Item.Bow: return $"<b>Bow</b>  ({me.Count(Item.Arrow)} arrows)    hold LMB to draw, release to fire";
                case Item.Chest: return "<b>Storage Chest</b>    LMB: place it inside your base";
                case Item.Barrier: return $"<b>Wooden Barrier</b> x{s.Count}    LMB: place it anywhere (not in the enemy base)";
                case Item.Berry: return $"<b>Berries</b> x{s.Count}    RMB: eat (+{Cfg.BerryHeal:0} HP)";
                case Item.C4: return "<b>C4</b>    LMB: throw it at enemy buildings - it blows up everything nearby";
                case Item.DeathWand: return "<b>Death Wand</b> (1 shot)    LMB: fire - anyone it passes close to dies";
                case Item.Helmet: return "<b>Alien Helmet</b>    LMB: put it on - the next headshot does no damage and breaks it";
                case Item.Armor: return $"<b>Wooden Armour</b> ({(s.Data > 0 ? s.Data : Cfg.ArmorHp)} HP)    LMB: put it on - a second health bar that goes first";
                case Item.Crossbow: return $"<b>Crossbow</b>  ({(s.Data > 0 ? "loaded" : "empty")}, {me.Count(Item.Arrow)} arrows)    LMB: fire   hold RMB: aim   reloads by itself";
                case Item.FortTower: return "<b>Fort Tower</b>    LMB: throw it - a lookout tower with a ladder pops up where it lands";
                case Item.Car: return "<b>Wooden Car</b>    LMB: put it down, then E to drive";
                case Item.Saddle: return "<b>Saddle</b>    walk up to a wild horse and press E to saddle and ride it";
                case Item.InvisPotion: return $"<b>Invisibility Potion</b>    LMB: drink ({Cfg.InvisTime:0}s, attacking shows you)";
                case Item.Chainsaw: return $"<b>Chainsaw</b> ({s.Data} uses left)    hold LMB: cuts wood and stone fast";
                default: return "";
            }
        }

        void DrawDamageNumbers(float k)
        {
            var cam = Camera.main;
            if (cam == null) return;
            for (int i = Fx.Numbers.Count - 1; i >= 0; i--)
            {
                var n = Fx.Numbers[i];
                float age = Time.time - n.Time;
                if (age > 0.9f) { Fx.Numbers.RemoveAt(i); continue; }
                var sp = cam.WorldToScreenPoint(n.Pos + Vector3.up * (0.3f + age * 0.8f));
                if (sp.z < 0) continue;
                float a = Mathf.Clamp01((0.9f - age) * 3f);
                float pop = 1f + Mathf.Max(0, 0.15f - age) * 4f;
                var st = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt((n.Head ? 26 : 20) * k * pop), fontStyle = FontStyle.Bold };
                var c = n.Head ? new Color(1f, 0.8f, 0.15f, a) : new Color(1f, 1f, 1f, a);
                string txt = Mathf.RoundToInt(n.Value) + (n.Head ? "!" : "");
                var r = new Rect(sp.x - 60, Screen.height - sp.y - 20, 120, 40);
                st.normal.textColor = new Color(0, 0, 0, a * 0.8f);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), txt, st);
                st.normal.textColor = c;
                GUI.Label(r, txt, st);
            }
        }

        // ------------------------------------------------------------------ slots

        /// <summary>An empty slot in your hand = your rock, shown faded.</summary>
        void DrawRockGhost(Rect r)
        {
            var icon = ItemIcons.Get(Item.Rock);
            if (icon == null) return;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.5f);
            GUI.DrawTexture(new Rect(r.x + r.width * 0.15f, r.y + r.height * 0.12f, r.width * 0.7f, r.height * 0.7f), icon, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        void DrawSlotVisual(Rect r, ItemStack s, bool selected, PlayerNet me)
        {
            Fill(r, selected ? new Color(1f, 0.85f, 0.3f, 0.55f) : new Color(0, 0, 0, 0.5f));
            Fill(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(1, 1, 1, 0.08f));
            if (s.Empty) return;
            var icon = ItemIcons.Get(s.Id);
            var inner = new Rect(r.x + r.width * 0.1f, r.y + r.height * 0.08f, r.width * 0.8f, r.height * 0.8f);
            if (icon != null) GUI.DrawTexture(inner, icon, ScaleMode.ScaleToFit, true);
            else GUI.Label(r, Cfg.ItemName(s.Id), new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true });
            string count = s.Count > 1 ? s.Count.ToString() : s.Id == Item.Bow && me != null ? me.Count(Item.Arrow) + "a" : "";
            if (count != "")
                Shadowed(new Rect(r.x, r.yMax - 22 * m_Scale, r.width - 5, 20 * m_Scale), count, new GUIStyle(m_Small) { alignment = TextAnchor.LowerRight });
            if (Cfg.MaxData(s.Id) > 0)
            {
                float d = Mathf.Clamp01(s.Data / (float)Mathf.Max(1, Cfg.MaxData(s.Id)));
                Fill(new Rect(r.x + 4, r.yMax - 6, (r.width - 8), 3), new Color(0, 0, 0, 0.6f));
                Fill(new Rect(r.x + 4, r.yMax - 6, (r.width - 8) * d, 3), new Color(0.4f, 0.9f, 0.4f));
            }
        }

        /// <summary>A draggable inventory slot (kind 0 = my inventory, 1 = open container).</summary>
        void DrawSlot(Rect r, ItemStack s, byte kind, int index, bool selected, PlayerNet me, PlayerController pc)
        {
            var e = Event.current;
            bool hover = r.Contains(e.mousePosition);
            bool isSource = m_Dragging && m_DragFrom.Kind == kind && m_DragFrom.Index == index;
            DrawSlotVisual(r, isSource ? default : s, selected, me);
            if (hover) { Fill(r, new Color(1, 1, 1, 0.08f)); if (!s.Empty) m_HoverName = Cfg.ItemName(s.Id) + ItemBlurb(s); }

            if (e.type == EventType.MouseDown && hover && !s.Empty)
            {
                if (e.shift && e.button == 0) QuickMove(kind, index, me, pc);
                else
                {
                    m_Dragging = true;
                    m_DragHalf = e.button == 1;
                    m_DragFrom = new SlotRef { Kind = kind, Index = index };
                    m_DragStack = s;
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && hover && m_Dragging)
            {
                int amount = m_DragHalf ? Mathf.Max(1, m_DragStack.Count / 2) : m_DragStack.Count;
                if (!(m_DragFrom.Kind == kind && m_DragFrom.Index == index))
                {
                    me.MoveItemRpc(m_DragFrom.Kind, (byte)m_DragFrom.Index, kind, (byte)index, (ushort)amount, LootRef(pc));
                    Sfx.Play2D(Sfx.Pop, 0.3f, 0.2f);
                }
                m_Dragging = false;
                e.Use();
            }
        }

        static NetworkObjectReference LootRef(PlayerController pc) =>
            pc.LootTarget != null && pc.LootTarget.IsSpawned ? new NetworkObjectReference(pc.LootTarget.NetworkObject) : default;

        static string ItemBlurb(ItemStack s)
        {
            switch (s.Id)
            {
                case Item.Ram: return $"  ({s.Data} hits left)";
                case Item.Chainsaw: return $"  ({s.Data} uses left)";
                case Item.Armor: return $"  ({(s.Data > 0 ? s.Data : Cfg.ArmorHp)} armour HP)";
                case Item.Crossbow: return s.Data > 0 ? "  (loaded)" : "  (empty)";
                case Item.Berry: return $"  (RMB to eat, +{Cfg.BerryHeal:0} HP)";
                default: return s.Count > 1 ? $"  x{s.Count}" : "";
            }
        }

        /// <summary>Shift-click: to/from the open container, or between hotbar and main inventory.</summary>
        void QuickMove(byte kind, int index, PlayerNet me, PlayerController pc)
        {
            if (pc.LootTarget != null)
            {
                if (kind == 0 && pc.LootTarget.TakeOnly) return;
                me.MoveItemRpc(kind, (byte)index, (byte)(1 - kind), 255, 0, LootRef(pc));
                Sfx.Play2D(Sfx.Pop, 0.3f, 0.2f);
                return;
            }
            bool fromHotbar = index < Cfg.HotbarSize;
            int start = fromHotbar ? Cfg.HotbarSize : 0, end = fromHotbar ? Cfg.PlayerSlots : Cfg.HotbarSize;
            var s = me.SlotAt(index);
            int target = -1;
            for (int i = start; i < end && target < 0; i++) if (me.SlotAt(i).Id == s.Id && me.SlotAt(i).Count < Cfg.MaxStack(s.Id)) target = i;
            for (int i = start; i < end && target < 0; i++) if (me.SlotAt(i).Empty) target = i;
            if (target >= 0) me.MoveItemRpc(0, (byte)index, 0, (byte)target, s.Count, default);
        }

        // ------------------------------------------------------------------ inventory / crafting / loot

        void DrawInventory(PlayerNet me, PlayerController pc)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            MouseOverUI = true;
            m_HoverName = "";
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.4f));
            float slot = Mathf.Min(64 * k, (sw - 120) / 24f), gap = 6 * k;
            float gridW = 7 * slot + 6 * gap;
            bool craft = pc.CraftOpen;
            float craftW = 330 * k;
            bool loot = pc.LootTarget != null;
            float lootW = loot ? gridW : 0;
            float total = gridW + 30 * k + (loot ? lootW : craftW);
            float x0 = Mathf.Max(10, (sw - total) / 2);
            float top = sh * 0.14f;
            float invX = x0;
            float lootX = invX + gridW + 30 * k;

            // ---- loot container (right of the inventory) ----
            if (loot)
            {
                var c = pc.LootTarget;
                Shadowed(new Rect(lootX, top - 34 * k, lootW, 30 * k), $"<b>{c.DisplayName.ToUpper()}</b>" + (c.TakeOnly ? "  <color=#bbbbbb>(take only)</color>" : ""), m_Label);
                int n = c.Slots.Count;
                for (int i = 0; i < n; i++)
                {
                    var r = new Rect(lootX + (i % 7) * (slot + gap), top + (i / 7) * (slot + gap), slot, slot);
                    DrawSlot(r, c.Slots[i], 1, i, false, me, pc);
                }
            }

            // ---- my inventory (21 slots) + hotbar ----
            Shadowed(new Rect(invX, top - 34 * k, gridW, 30 * k), "<b>INVENTORY</b>", m_Label);
            for (int i = 0; i < Cfg.MainSize; i++)
            {
                int idx = Cfg.HotbarSize + i;
                var r = new Rect(invX + (i % 7) * (slot + gap), top + (i / 7) * (slot + gap), slot, slot);
                DrawSlot(r, me.SlotAt(idx), 0, idx, false, me, pc);
            }
            float hotY = top + 3 * (slot + gap) + 26 * k;
            Shadowed(new Rect(invX, hotY - 26 * k, gridW, 24 * k), "<b>HOTBAR</b>  <color=#bbbbbb>(keys 1-7)</color>", m_Small);
            for (int i = 0; i < Cfg.HotbarSize; i++)
            {
                var r = new Rect(invX + i * (slot + gap), hotY, slot, slot);
                DrawSlot(r, me.SlotAt(i), 0, i, me.HeldSlot.Value == i, me, pc);
                GUI.Label(new Rect(r.x + 4, r.y + 2, 20, 20), (i + 1).ToString(), m_Small);
            }
            float infoY = hotY + slot + 10 * k;
            Shadowed(new Rect(invX, infoY, gridW + 200, 24 * k), m_HoverName != "" ? m_HoverName : "Drag to move · right-drag splits a stack · shift-click quick-moves · drag outside to drop", m_Small);

            // ---- crafting (right, when not looting) ----
            float cxp = invX + gridW + 30 * k;
            string mats = $"<color=#d9a066>{me.Count(Item.Wood)} wood</color>" + (Cfg.WoodMode ? "" : $"  <color=#c8c8d0>{me.Count(Item.Stone)} stone</color>");
            if (!loot) Shadowed(new Rect(cxp, top - 34 * k, craftW, 30 * k), $"<b>CRAFTING</b>   {mats}", m_Label);
            float row = Mathf.Min(50 * k, (sh * 0.84f - top) / Mathf.Max(1, Cfg.RecipeCount) - 4 * k);
            if (!craft && !loot)
            {
                var nr = new Rect(cxp, top, craftW, 90 * k);
                Fill(nr, new Color(0, 0, 0, 0.45f));
                GUI.Label(new Rect(nr.x + 10, nr.y + 8, nr.width - 20, nr.height - 16),
                    "You can craft anywhere <b><color=#7dff9a>inside your own base</color></b>. Head home to craft.", new GUIStyle(m_Label) { wordWrap = true });
                Shadowed(new Rect(cxp, nr.yMax + 6, craftW, 24 * k), "TAB / Esc to close", m_Small);
            }
            for (int i = 0; craft && !loot && i < Cfg.RecipeCount; i++)
            {
                var rec = Cfg.GetRecipe(i);
                var rr = new Rect(cxp, top + i * (row + 4 * k), craftW, row);
                bool afford = me.Count(Item.Wood) >= rec.Wood && me.Count(Item.Stone) >= rec.Stone;
                Fill(rr, new Color(0, 0, 0, 0.45f));
                var icon = ItemIcons.Get(rec.Output);
                if (icon != null) GUI.DrawTexture(new Rect(rr.x + 4, rr.y + 3, row - 6, row - 6), icon, ScaleMode.ScaleToFit, true);
                string cost = rec.Wood + " wood" + (rec.Stone > 0 ? ", " + rec.Stone + " stone" : "");
                GUI.Label(new Rect(rr.x + row + 4, rr.y + 2, craftW - row - 100 * k, row), $"<b>{rec.Name}</b>\n<size={Mathf.RoundToInt(12 * k)}><color={(afford ? "#bbbbbb" : "#ff7777")}>{cost}</color></size>", m_Label);
                GUI.enabled = afford;
                if (GUI.Button(new Rect(rr.xMax - 90 * k, rr.y + 8 * k, 84 * k, row - 16 * k), "Craft", m_Button)) me.CraftRpc(i);
                GUI.enabled = true;
            }
            if (craft && !loot) Shadowed(new Rect(cxp, top + Cfg.RecipeCount * (row + 4 * k) + 6, craftW, 24 * k), "TAB / Esc to close", m_Small);

            // ---- drag visual ----
            var e = Event.current;
            if (m_Dragging)
            {
                if (e.type == EventType.MouseUp)
                {
                    // released outside the item grids: throw it on the ground (the rock stays with you)
                    var invRect = new Rect(invX - 10, top - 10, gridW + 20, hotY + slot - top + 20);
                    var lootRect = new Rect(lootX - 10, top - 10, lootW + 20, 5 * (slot + gap) + 20);
                    bool outside = !invRect.Contains(e.mousePosition) && !(loot && lootRect.Contains(e.mousePosition));
                    if (outside)
                    {
                        int amount = m_DragHalf ? Mathf.Max(1, m_DragStack.Count / 2) : m_DragStack.Count;
                        me.DropItemRpc(m_DragFrom.Kind, (byte)m_DragFrom.Index, (ushort)amount, LootRef(pc));
                        Sfx.Play2D(Sfx.Throw, 0.4f);
                    }
                    m_Dragging = false;
                    e.Use();
                }
                else
                {
                    var icon = ItemIcons.Get(m_DragStack.Id);
                    var dr = new Rect(e.mousePosition.x - slot * 0.4f, e.mousePosition.y - slot * 0.4f, slot * 0.8f, slot * 0.8f);
                    if (icon != null) GUI.DrawTexture(dr, icon, ScaleMode.ScaleToFit, true);
                    else GUI.Label(dr, Cfg.ItemName(m_DragStack.Id), m_Small);
                    int amt = m_DragHalf ? Mathf.Max(1, m_DragStack.Count / 2) : m_DragStack.Count;
                    if (amt > 1) Shadowed(new Rect(dr.x, dr.yMax - 16, dr.width, 18), amt.ToString(), new GUIStyle(m_Small) { alignment = TextAnchor.LowerRight });
                }
            }
        }

        // ------------------------------------------------------------------ build wheel

        /// <summary>Rust-style radial menu while RMB is held with the building plan: move the mouse onto a slice and let go.</summary>
        void DrawWheel(PlayerController pc)
        {
            float k = m_Scale;
            var opts = PlayerController.WheelOptions;
            var c = s_WheelCenter;
            var mouse = Event.current.mousePosition;
            var d = mouse - c;
            WheelHover = -1;
            if (d.magnitude > 45f * k)
            {
                float ang = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg; // 0 = up, clockwise
                if (ang < 0) ang += 360f;
                WheelHover = Mathf.RoundToInt(ang / (360f / opts.Length)) % opts.Length;
            }
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.25f));
            float rad = 150f * k, bw = 120f * k, bh = 62f * k;
            for (int i = 0; i < opts.Length; i++)
            {
                float a = i * (360f / opts.Length) * Mathf.Deg2Rad;
                var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * rad;
                var r = new Rect(p.x - bw / 2, p.y - bh / 2, bw, bh);
                bool hover = i == WheelHover;
                bool current = opts[i].Demolish ? pc.DemolishMode : !pc.DemolishMode && pc.BuildPiece == opts[i].Piece;
                Fill(r, hover ? new Color(1f, 0.8f, 0.3f, 0.85f) : current ? new Color(0.3f, 0.6f, 1f, 0.7f) : new Color(0, 0, 0, 0.7f));
                string cost = opts[i].Demolish ? "your pieces" : $"{Cfg.PieceWood(opts[i].Piece)} wood";
                var st = new GUIStyle(m_Center) { fontStyle = FontStyle.Bold };
                st.normal.textColor = opts[i].Demolish ? new Color(1f, 0.55f, 0.45f) : Color.white;
                GUI.Label(new Rect(r.x, r.y + 6 * k, r.width, 26 * k), opts[i].Label, st);
                GUI.Label(new Rect(r.x, r.y + 30 * k, r.width, 22 * k), $"<size={Mathf.RoundToInt(12 * k)}>{cost}</size>", m_Center);
            }
            Fill(new Rect(c.x - 40 * k, c.y - 40 * k, 80 * k, 80 * k), new Color(0, 0, 0, 0.5f));
            GUI.Label(new Rect(c.x - 60 * k, c.y - 12 * k, 120 * k, 24 * k), WheelHover >= 0 ? opts[WheelHover].Label : "release RMB", m_Center);
            Fill(new Rect(mouse.x - 3, mouse.y - 3, 6, 6), Color.white);
        }

        // ------------------------------------------------------------------ pause: sound, mic, leave, dev

        static float Slider(string label, float v, float min, float max, float scale, GUIStyle style, string shown)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, style, GUILayout.Width(170 * scale));
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(220 * scale));
            GUILayout.Label(shown, style, GUILayout.Width(70 * scale));
            GUILayout.EndHorizontal();
            return nv;
        }

        void DrawPauseMenu(Bootstrap boot, PlayerController pc)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.55f));
            float w = 560 * k, h = Mathf.Min(sh - 40, 640 * k);
            var r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(r, new Color(0.06f, 0.06f, 0.08f, 0.94f));
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, r.width - 40, r.height - 28));
            GUILayout.Label("<b>PAUSED</b>", m_Big);
            GUILayout.Label("<color=#bbbbbb>The match keeps running!</color>", m_Center);
            GUILayout.Space(8 * k);
            if (GUILayout.Button("Resume", m_Button, GUILayout.Height(36 * k))) pc.Paused = false;
            GUILayout.Space(10 * k);

            bool changed = false;
            GUILayout.Label("<b><color=#ffd27a>SOUND</color></b>", m_Label);
            float v = Slider("Master volume", GameSettings.MasterVolume, 0f, 1f, k, m_Small, $"{GameSettings.MasterVolume * 100:0}%");
            if (!Mathf.Approximately(v, GameSettings.MasterVolume)) { GameSettings.MasterVolume = v; changed = true; }
            v = Slider("Voice chat volume", GameSettings.VoiceVolume, 0f, 2f, k, m_Small, $"{GameSettings.VoiceVolume * 100:0}%");
            if (!Mathf.Approximately(v, GameSettings.VoiceVolume)) { GameSettings.VoiceVolume = v; changed = true; }

            GUILayout.Space(8 * k);
            GUILayout.Label("<b><color=#ffd27a>PROXIMITY VOICE CHAT</color></b>  <color=#bbbbbb>(people hear you when they're near you)</color>", m_Small);
            GUILayout.BeginHorizontal();
            string[] modes = { "Off", "Open mic", $"Push to talk ({GameSettings.PushToTalkKey})" };
            for (int i = 0; i < 3; i++)
                if (GUILayout.Toggle(GameSettings.VoiceMode == i, modes[i], m_Button, GUILayout.Height(30 * k)) && GameSettings.VoiceMode != i) { GameSettings.VoiceMode = i; changed = true; }
            GUILayout.EndHorizontal();
            var devs = Microphone.devices;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Microphone", m_Small, GUILayout.Width(170 * k));
            if (devs.Length == 0) GUILayout.Label("<color=#ff8888>no microphone found</color>", m_Small);
            else
            {
                int cur = System.Array.IndexOf(devs, GameSettings.MicDevice);
                if (cur < 0) cur = 0;
                if (GUILayout.Button("<", m_Button, GUILayout.Width(34 * k))) { GameSettings.MicDevice = devs[(cur + devs.Length - 1) % devs.Length]; changed = true; }
                string name = devs[cur];
                GUILayout.Label(name.Length > 34 ? name.Substring(0, 34) + "..." : name, m_Small, GUILayout.Width(260 * k));
                if (GUILayout.Button(">", m_Button, GUILayout.Width(34 * k))) { GameSettings.MicDevice = devs[(cur + 1) % devs.Length]; changed = true; }
            }
            GUILayout.EndHorizontal();
            v = Slider("Mic volume", GameSettings.MicGain, 0.2f, 4f, k, m_Small, $"{GameSettings.MicGain * 100:0}%");
            if (!Mathf.Approximately(v, GameSettings.MicGain)) { GameSettings.MicGain = v; changed = true; }
            if (GameSettings.VoiceMode == GameSettings.VoiceOpen)
            {
                v = Slider("Open mic sensitivity", 0.1f - GameSettings.MicThreshold, 0f, 0.1f, k, m_Small, $"{(0.1f - GameSettings.MicThreshold) * 1000:0}");
                if (!Mathf.Approximately(0.1f - v, GameSettings.MicThreshold)) { GameSettings.MicThreshold = 0.1f - v; changed = true; }
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mic level", m_Small, GUILayout.Width(170 * k));
            var lr = GUILayoutUtility.GetRect(220 * k, 14 * k, GUILayout.Width(220 * k));
            Fill(lr, new Color(0, 0, 0, 0.6f));
            Fill(new Rect(lr.x, lr.y, lr.width * Mathf.Clamp01(VoiceChat.Level), lr.height), VoiceChat.Transmitting ? new Color(0.4f, 1f, 0.5f) : new Color(0.6f, 0.6f, 0.6f));
            if (GameSettings.VoiceMode == GameSettings.VoiceOpen) Fill(new Rect(lr.x + lr.width * Mathf.Clamp01(GameSettings.MicThreshold * 3f), lr.y - 2, 2, lr.height + 4), new Color(1f, 0.8f, 0.3f));
            GUILayout.Label(VoiceChat.Transmitting ? "<color=#7dff9a>sending</color>" : "", m_Small);
            GUILayout.EndHorizontal();
            if (changed) GameSettings.Save();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dev settings", m_Button, GUILayout.Height(40 * k))) m_ShowDev = true;
            if (GUILayout.Button("Leave game", m_Button, GUILayout.Height(40 * k))) boot.Leave();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawDevMenu(PlayerNet me, NetGame game)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.55f));
            float w = Mathf.Min(sw - 40, 760 * k), h = Mathf.Min(sh - 40, 620 * k);
            var r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            MouseOverUI = true;
            Fill(r, new Color(0.08f, 0.05f, 0.05f, 0.95f));
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, r.width - 40, r.height - 28));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt(26 * k)}>DEV SETTINGS</size></b>", m_Label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back", m_Button, GUILayout.Width(120 * k), GUILayout.Height(34 * k))) m_ShowDev = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#bbbbbb>For testing. Everyone in the match is told when you use one.</color>", m_Small);
            string timer = game == null ? "" : $"{game.S}  ·  {Clock(game.TimeLeft)} left" + (game.TimerPaused.Value ? "  <color=#ffcc66>(PAUSED)</color>" : "");
            GUILayout.Label(timer, m_Label);

            void Section(string title) { GUILayout.Space(6 * k); GUILayout.Label($"<b><color=#ffd27a>{title}</color></b>", m_Label); }
            void Row(params (string label, DevCmd cmd)[] buttons)
            {
                GUILayout.BeginHorizontal();
                foreach (var (label, cmd) in buttons)
                    if (GUILayout.Button(label, m_Button, GUILayout.Height(32 * k))) me.DevRpc(cmd);
                GUILayout.EndHorizontal();
            }
            Section("MATCH");
            Row(("Drop the wall now", DevCmd.DropWallNow), (game != null && game.TimerPaused.Value ? "Resume timer" : "Pause timer", DevCmd.TogglePauseTimer), ("Timer to 10s", DevCmd.TimerTo10s));
            Row(("+1 minute", DevCmd.AddMinute), ("-1 minute", DevCmd.SubMinute), ("Start sudden death", DevCmd.StartSuddenDeath), ("Win now", DevCmd.WinNow));
            Section("WORLD");
            Row(("Spawn airdrop now", DevCmd.SpawnAirdrop), ("Ball to me", DevCmd.BallToMe), ("Ball to middle", DevCmd.BallToMiddle), ("Regrow nodes", DevCmd.RegrowNodes));
            Row(("Spawn horse", DevCmd.SpawnHorse), ("Spawn car", DevCmd.SpawnCar));
            Section("ME");
            Row(("+1000 wood", DevCmd.GiveWood), ("+1000 stone", DevCmd.GiveStone), ("+50 arrows", DevCmd.GiveArrows));
            Row(("All airdrop items", DevCmd.GiveOpItems), ("One of every craftable", DevCmd.GiveCraftables), ("Clear inventory", DevCmd.ClearInventory));
            Row(("Heal", DevCmd.HealFull), ("God mode on/off", DevCmd.ToggleGod), ("Kill me", DevCmd.KillMe));
            Section("TELEPORT");
            Row(("My base", DevCmd.TpMyBase), ("Enemy base", DevCmd.TpEnemyBase), ("The ball", DevCmd.TpBall), ("The airdrop", DevCmd.TpAirdrop));
            GUILayout.EndArea();
        }

        /// <summary>Bottom right, above the corner: "+30 Wood" with the item icon, fading out.</summary>
        void DrawGains(float k)
        {
            float sw = Screen.width, sh = Screen.height;
            float rowH = 34 * k, w = 230 * k;
            float y = sh - 20 * k - rowH;
            for (int i = s_Gains.Count - 1; i >= 0; i--)
            {
                var g = s_Gains[i];
                float age = Time.time - g.Time;
                if (age > 4f) { s_Gains.RemoveAt(i); continue; }
                float a = Mathf.Clamp01((4f - age) * 1.5f);
                float slide = Mathf.Max(0f, 0.15f - age) / 0.15f * 40f * k;
                var r = new Rect(sw - w - 16 * k + slide, y, w, rowH - 4 * k);
                bool lost = g.Amount < 0;
                Fill(r, lost ? new Color(0.3f, 0.08f, 0.08f, 0.55f * a) : new Color(0.1f, 0.25f, 0.1f, 0.55f * a));
                Fill(new Rect(r.x, r.y, 4 * k, r.height), lost ? new Color(1f, 0.35f, 0.3f, a) : new Color(0.45f, 0.95f, 0.45f, a));
                var icon = ItemIcons.Get(g.Id);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                if (icon != null) GUI.DrawTexture(new Rect(r.xMax - r.height - 4 * k, r.y, r.height, r.height), icon, ScaleMode.ScaleToFit, true);
                GUI.color = old;
                var st = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleLeft };
                st.normal.textColor = new Color(1, 1, 1, a);
                GUI.Label(new Rect(r.x + 12 * k, r.y, r.width - r.height - 20 * k, r.height), lost ? $"<b><color=#ff7a70>{g.Amount}</color></b>  {Cfg.ItemName(g.Id)}" : $"<b><color=#9dff9d>+{g.Amount}</color></b>  {Cfg.ItemName(g.Id)}", st);
                y -= rowH;
            }
        }

        // ------------------------------------------------------------------ misc

        string BallStatus(int myTeam)
        {
            var b = Ball.Instance;
            if (b == null) return "";
            if (b.IsCarried)
            {
                var c = b.Carrier;
                if (c == null) return "Ball is being carried";
                return c.IsOwner ? "<color=#ffdd55>You have the ball - put it in your machine (E) or throw it (LMB)!</color>" : $"<color=#ff7777>{Cfg.TeamName[c.Team.Value]} has the ball!</color>";
            }
            int sock = b.SocketTeam.Value;
            if (sock >= 0)
                return sock == myTeam ? "<color=#77ff77>The ball is in YOUR machine - defend it!</color>" : $"<color=#ff7777>The ball is in the {Cfg.TeamName[sock]} machine - raid them and take it!</color>";
            int t = b.BaseTeam.Value;
            if (t < 0) return "The ball is loose - put it in your machine!";
            return t == myTeam ? "<color=#ffdd55>The ball is in your base but NOT in the machine - it only counts in the socket!</color>" : $"<color=#ff7777>The ball is in the {Cfg.TeamName[t]} base (not in their machine yet)</color>";
        }

        static string Direction(Transform me, Vector3 target)
        {
            Vector3 d = target - me.position;
            d.y = 0;
            float dist = d.magnitude;
            if (dist < 3f) return "here";
            float ang = Vector3.SignedAngle(me.forward, d, Vector3.up);
            string arrow = Mathf.Abs(ang) < 22.5f ? "ahead" : Mathf.Abs(ang) > 157.5f ? "behind" : ang > 0 ? (ang < 67.5f ? "ahead-right" : ang < 112.5f ? "right" : "behind-right") : (ang > -67.5f ? "ahead-left" : ang > -112.5f ? "left" : "behind-left");
            return $"{dist:0}m {arrow}";
        }

        void DrawGameOver(Bootstrap boot, NetGame game, int myTeam)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.6f));
            int w = game.Winner.Value;
            string title = w < 0 ? "DRAW" : w == myTeam ? "<color=#77ff77>VICTORY</color>" : "<color=#ff5555>DEFEAT</color>";
            Shadowed(new Rect(0, sh * 0.3f, sw, 70 * k), $"<size={Mathf.RoundToInt(64 * k)}>{title}</size>", m_Big);
            Shadowed(new Rect(0, sh * 0.3f + 80 * k, sw, 30 * k), game.EndReason.Value.ToString(), m_Center);
            DrawLeaveButton(boot, new Rect(sw / 2 - 110 * k, sh * 0.3f + 140 * k, 220 * k, 46 * k));
        }
    }
}
