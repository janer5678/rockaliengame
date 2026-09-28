using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Immediate-mode (OnGUI) main menu, HUD, crafting menu, pause and end screens.</summary>
    public class Hud : MonoBehaviour
    {
        struct Msg { public string Text; public float Time; }
        static readonly List<Msg> s_Msgs = new List<Msg>();
        static float s_HitTime = -10f, s_BannerTime = -10f;
        static bool s_HitKill;
        static string s_BannerTitle, s_BannerSub;
        public static bool MouseOverUI;
        static readonly System.Text.RegularExpressions.Regex s_ColorTag = new System.Text.RegularExpressions.Regex("</?color[^>]*>");

        bool m_ShowHelp = true;
        float m_LastHealth = Cfg.MaxHealth, m_DamageFlash;
        GUIStyle m_Label, m_Center, m_Big, m_Small, m_Button, m_Box;
        float m_Scale = 1f;

        public static void Push(string text)
        {
            Debug.Log("[HUD] " + text);
            s_Msgs.Add(new Msg { Text = text, Time = Time.time });
            if (s_Msgs.Count > 6) s_Msgs.RemoveAt(0);
        }
        public static void HitMarker(bool kill) { s_HitTime = Time.time; s_HitKill = kill; }
        public static void Banner(string title, string sub) { s_BannerTitle = title; s_BannerSub = sub; s_BannerTime = Time.time; }
        public static void Clear() { s_Msgs.Clear(); s_BannerTime = -10f; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) m_ShowHelp = !m_ShowHelp;
            var me = PlayerNet.Local;
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
            float w = 460 * m_Scale, h = 470 * m_Scale;
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
            Bootstrap.Solo = GUILayout.Toggle(Bootstrap.Solo, " Solo test (start without an opponent)", m_Label);
            Bootstrap.Fast = GUILayout.Toggle(Bootstrap.Fast, " Fast timers (10s ball drop, 90s match)", m_Label);
            GUILayout.Space(12 * m_Scale);
            if (GUILayout.Button("HOST GAME", m_Button, GUILayout.Height(44 * m_Scale))) boot.Host();
            GUILayout.Space(6 * m_Scale);
            if (GUILayout.Button("JOIN GAME", m_Button, GUILayout.Height(44 * m_Scale))) boot.Join();
            GUILayout.Space(8 * m_Scale);
            if (!string.IsNullOrEmpty(boot.Status)) GUILayout.Label("<color=#ffcc66>" + boot.Status + "</color>", m_Center);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Host picks the options. Your friend joins with your IP (port 7777 UDP).", m_Small);
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
                        phase = "Ball drops in " + Clock(game.TimeLeft);
                        sub = "Gather wood & stone, build your base";
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
                Shadowed(new Rect(12, 44 * k, 400 * k, 24 * k), $"Your base: {Direction(me.transform, Cfg.BaseCenter[team])}", m_Small);
                var ball = Ball.Instance;
                if (ball != null) Shadowed(new Rect(12, 64 * k, 400 * k, 24 * k), $"Ball: {Direction(me.transform, ball.transform.position)}", m_Small);
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

            // ---- crosshair / hit marker / bow draw ----
            float cx = sw / 2, cy = sh / 2;
            Fill(new Rect(cx - 1, cy - 8, 2, 16), new Color(1, 1, 1, 0.8f));
            Fill(new Rect(cx - 8, cy - 1, 16, 2), new Color(1, 1, 1, 0.8f));
            if (Time.time - s_HitTime < 0.25f)
            {
                var hc = s_HitKill ? new Color(1, 0.2f, 0.2f) : Color.white;
                GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), s_HitKill ? "<b><size=30>X</size></b>" : "<b><size=24>x</size></b>", new GUIStyle(m_Center) { normal = { textColor = hc } });
            }
            float charge = Mathf.Max(pc.DrawAmount, pc.RamCharge);
            if (charge > 0)
            {
                Fill(new Rect(cx - 50 * k, cy + 30 * k, 100 * k, 8 * k), new Color(0, 0, 0, 0.5f));
                Fill(new Rect(cx - 50 * k, cy + 30 * k, 100 * k * charge, 8 * k), charge >= 1f ? new Color(1f, 0.85f, 0.2f) : Color.white);
            }

            // ---- aim info / build info ----
            float by = sh - 150 * k;
            if (!string.IsNullOrEmpty(pc.AimText)) Shadowed(new Rect(0, cy + 45 * k, sw, 26 * k), pc.AimText, m_Center);
            if (me.HeldItem == Item.BuildingPlan && !me.CarryingBall)
            {
                var p = pc.BuildPiece;
                Shadowed(new Rect(0, by - 52 * k, sw, 26 * k), $"<b>{Cfg.PieceName(p)}</b>  ({Cfg.PieceWood(p)} wood)    RMB: next piece   R: rotate stairs   F: upgrade to stone", m_Center);
                if (!string.IsNullOrEmpty(pc.BuildHint)) Shadowed(new Rect(0, by - 28 * k, sw, 26 * k), "<color=#ff8888>" + pc.BuildHint + "</color>", m_Center);
            }
            else if (me.HeldItem == Item.Ram && !me.CarryingBall)
                Shadowed(new Rect(0, by - 52 * k, sw, 26 * k), $"<b>Battering Ram</b> ({me.RamCharges.Value} hits left)    hold LMB at an enemy piece: wood breaks instantly, stone drops to wood", m_Center);
            else if (me.HeldItem == Item.Spear && !me.CarryingBall)
                Shadowed(new Rect(0, by - 52 * k, sw, 26 * k), $"<b>Spear</b> x{me.Spears.Value}    LMB: stab    hold RMB + LMB: throw    E: pick thrown spears back up", m_Center);

            // ---- hotbar ----
            float slot = 70 * k, gap = 6 * k;
            int n = Cfg.ItemCount;
            float hx = cx - (n * slot + (n - 1) * gap) / 2, hy = sh - slot - 14 * k;
            for (int i = 0; i < n; i++)
            {
                var it = (Item)i;
                bool owned = me.Owns(it);
                bool sel = me.HeldItem == it;
                var r = new Rect(hx + i * (slot + gap), hy, slot, slot);
                Fill(r, sel ? new Color(1f, 0.85f, 0.3f, 0.6f) : new Color(0, 0, 0, owned ? 0.5f : 0.2f));
                string label = ShortName(it);
                string count = it == Item.Bow ? me.Arrows.Value + " arr" : it == Item.Spear ? "x" + me.Spears.Value : it == Item.Ram ? me.RamCharges.Value + " hit" : "";
                var st = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                st.normal.textColor = owned ? Color.white : new Color(1, 1, 1, 0.3f);
                GUI.Label(r, label, st);
                GUI.Label(new Rect(r.x + 4, r.y + 2, 20, 20), (i + 1).ToString(), m_Small);
                if (count != "" && owned) GUI.Label(new Rect(r.x, r.yMax - 20 * k, r.width - 4, 20 * k), count, new GUIStyle(m_Small) { alignment = TextAnchor.LowerRight });
            }

            // ---- health (bottom left) ----
            float hw = 280 * k;
            var hr = new Rect(14, sh - 44 * k, hw, 28 * k);
            Fill(hr, new Color(0, 0, 0, 0.55f));
            float hp = me.Health.Value / Cfg.MaxHealth;
            Fill(new Rect(hr.x + 3, hr.y + 3, (hr.width - 6) * hp, hr.height - 6), Color.Lerp(new Color(0.85f, 0.15f, 0.1f), new Color(0.3f, 0.85f, 0.3f), hp));
            Shadowed(new Rect(hr.x + 8, hr.y + 2, hr.width, hr.height), $"<b>HP {me.Health.Value:0}</b>", m_Label);

            // ---- resources (bottom right) ----
            var rr = new Rect(sw - 230 * k - 14, sh - 100 * k, 230 * k, 86 * k);
            Fill(rr, new Color(0, 0, 0, 0.5f));
            Shadowed(new Rect(rr.x + 12, rr.y + 6, rr.width, 26 * k), $"<color=#d9a066><b>Wood</b></color>   {me.Wood.Value}", m_Label);
            Shadowed(new Rect(rr.x + 12, rr.y + 32 * k, rr.width, 26 * k), $"<color=#c8c8d0><b>Stone</b></color>   {me.Stone.Value}", m_Label);
            Shadowed(new Rect(rr.x + 12, rr.y + 58 * k, rr.width, 26 * k), $"<color=#ffffff><b>Arrows</b></color>   {me.Arrows.Value}", m_Label);

            // ---- help ----
            if (m_ShowHelp && !pc.MenuOpen)
            {
                string help =
                    "<b>CONTROLS</b>  (F1 hide)\n" +
                    "WASD move · Shift sprint · Space jump\n" +
                    "LMB attack / gather / place · hold LMB = draw bow / ram\n" +
                    "Spear: hold RMB + LMB to throw\n" +
                    "1-7 / scroll: switch item · TAB: craft anywhere\n" +
                    "E: use / pick up / pull out spears\n" +
                    "G: throw ball · Esc: pause";
                var hrct = new Rect(12, 92 * k, 380 * k, 160 * k);
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
                Shadowed(new Rect(0, sh * 0.4f + 60 * k, sw, 30 * k), $"Respawning at your base in {Mathf.CeilToInt(t)}", m_Center);
            }

            if (pc.MenuOpen) DrawCrafting(me, pc);

            if (pc.Paused && (game == null || game.S != GameState.GameOver))
            {
                Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.5f));
                Shadowed(new Rect(0, sh * 0.35f, sw, 60 * k), "PAUSED", m_Big);
                Shadowed(new Rect(0, sh * 0.35f + 60 * k, sw, 30 * k), "Click anywhere to resume (the match keeps running!)", m_Center);
                DrawLeaveButton(boot, new Rect(cx - 100 * k, sh * 0.35f + 110 * k, 200 * k, 44 * k));
            }

            if (game != null && game.S == GameState.GameOver) DrawGameOver(boot, game, team);
        }

        string BallStatus(int myTeam)
        {
            var b = Ball.Instance;
            if (b == null) return "";
            if (b.IsCarried)
            {
                var c = b.Carrier;
                if (c == null) return "Ball is being carried";
                return c.IsOwner ? "<color=#ffdd55>You have the ball - drop it (E) inside your base!</color>" : $"<color=#ff7777>{Cfg.TeamName[c.Team.Value]} has the ball!</color>";
            }
            int t = b.BaseTeam.Value;
            if (t < 0) return "The ball is loose - bring it to your base!";
            return t == myTeam ? "<color=#77ff77>The ball is in YOUR base - defend it!</color>" : $"<color=#ff7777>The ball is in the {Cfg.TeamName[t]} base - raid them!</color>";
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

        static string ShortName(Item i)
        {
            switch (i)
            {
                case Item.BuildingPlan: return "Build Plan";
                case Item.Hatchet: return "Hatchet";
                case Item.Pickaxe: return "Pickaxe";
                default: return i.ToString();
            }
        }

        void DrawCrafting(PlayerNet me, PlayerController pc)
        {
            float k = m_Scale;
            float w = 520 * k, h = Mathf.Min(600 * k, Screen.height - 20);
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            Fill(r, new Color(0.08f, 0.07f, 0.06f, 0.92f));
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.Label("<b><size=" + Mathf.RoundToInt(26 * k) + ">CRAFTING</size></b>", m_Label);
            GUILayout.Label("<color=#bbbbbb>Craft anywhere - no table needed</color>", m_Small);
            GUILayout.Space(8 * k);
            for (int i = 0; i < Cfg.Recipes.Length; i++)
            {
                var rec = Cfg.Recipes[i];
                bool afford = me.Wood.Value >= rec.Wood && me.Stone.Value >= rec.Stone;
                bool can = afford;
                GUILayout.BeginHorizontal();
                string cost = rec.Wood + " wood" + (rec.Stone > 0 ? ", " + rec.Stone + " stone" : "");
                GUILayout.Label($"<b>{rec.Name}</b>\n<size={Mathf.RoundToInt(12 * k)}><color={(afford ? "#bbbbbb" : "#ff7777")}>{cost}</color></size>", m_Label, GUILayout.Width(330 * k));
                GUI.enabled = can;
                if (GUILayout.Button("Craft", m_Button, GUILayout.Width(120 * k), GUILayout.Height(38 * k))) me.CraftRpc(i);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("TAB / Esc to close", m_Small);
            GUILayout.EndArea();
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
