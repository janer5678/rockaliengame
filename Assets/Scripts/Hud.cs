using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Immediate-mode (OnGUI) main menu + settings, HUD, inventory/crafting/loot screen, pause and end screens.</summary>
    public partial class Hud : MonoBehaviour
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
        /// <summary>Test hook: the last banner (title and line under it).</summary>
        public static string LastBanner => s_BannerTitle + " | " + s_BannerSub;
        public static bool MouseOverUI;
        static readonly System.Text.RegularExpressions.Regex s_ColorTag = new System.Text.RegularExpressions.Regex("</?color[^>]*>");

        /// <summary>Build wheel: which slice the mouse is over (-1 = none).</summary>
        public static int WheelHover = -1;
        static Vector2 s_WheelCenter;
        public static void WheelOpened() { s_WheelCenter = new Vector2(Screen.width / 2f, Screen.height / 2f); WheelHover = PlayerController.Local != null ? PlayerController.Local.WheelIndex : 0; }
        float m_LastHealth = 100f, m_DamageFlash;
        GUIStyle m_Label, m_Center, m_Big, m_Small, m_Button, m_Box, m_Field;
        float m_Scale = 1f;

        // drag & drop
        struct SlotRef { public byte Kind; public int Index; }
        bool m_Dragging, m_DragHalf;
        SlotRef m_DragFrom;
        ItemStack m_DragStack;
        float m_DragSlot = 64f;
        /// <summary>How many the drag carries: the whole stack, or half of it (right-drag).</summary>
        int DragAmount => m_DragHalf ? Mathf.Max(1, m_DragStack.Count / 2) : m_DragStack.Count;
        // what the mouse is over in the bag (item or crafting row): its name (white, top line) and description (grey)
        string m_HoverTitle = "", m_HoverText = "";

        public static void Push(string text)
        {
            Debug.Log("[HUD] " + text);
            s_Msgs.Add(new Msg { Text = text, Time = Time.time });
            if (s_Msgs.Count > 6) s_Msgs.RemoveAt(0);
        }
        public static void HitMarker(bool kill, bool head)
        {
            if (!kill) Sfx.Play2D(Sfx.Hit, head ? 0.7f : 0.5f, 0.05f);
            if (kill || Time.time - s_HitTime > 0.05f || !s_HitKill) { s_HitKill = kill; s_HitHead = head; }
            s_HitTime = Time.time;
        }
        public static void Wake() => s_WakeTime = Time.time;
        public static void Banner(string title, string sub) { s_BannerTitle = title; s_BannerSub = sub; s_BannerTime = Time.time; }
        public static void Clear() { s_Msgs.Clear(); s_Gains.Clear(); s_Kills.Clear(); s_BannerTime = -10f; Fx.Numbers.Clear(); }

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
            MenuUpdate();
            CountFps();
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
            // (Settings > Display > INTERFACE: the UI scale and the font)
            m_Scale = Mathf.Max(0.75f, Screen.height / 900f) * GameSettings.UiScale;
            UiLook.ApplyFont();
            int fs = Mathf.RoundToInt(16 * m_Scale);
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = fs, richText = true };
            m_Label.normal.textColor = Color.white;
            m_Center = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
            m_Big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(40 * m_Scale), fontStyle = FontStyle.Bold };
            m_Small = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(13 * m_Scale) };
            m_Button = new GUIStyle(GUI.skin.button) { fontSize = fs };
            m_Box = new GUIStyle(GUI.skin.box);
            m_Field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(14 * m_Scale) };
            UiStyles();
        }

        /// <summary>The in-game HUD's opacity while it's being drawn (Settings > Display > INTERFACE); 1 for the menus.</summary>
        static float s_HudAlpha = 1f;

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            c.a *= s_HudAlpha;
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
            // Settings > Display > POST PROCESSING > On the UI too: the repaint goes into a texture the camera's post processing goes over
            bool toTexture = UiLook.BeginUi(out var prevTarget);
            Styles();
            SearchFrame();
            BeginHoverFrame();
            MouseOverUI = false;
            s_HudAlpha = 1f;
            DrawAll();
            s_HudAlpha = 1f;
            GUI.color = Color.white;
            DrawFps(Bootstrap.I != null && Bootstrap.I.InSession && PlayerNet.Local != null);
            EndHoverFrame();
            if (toTexture) UiLook.EndUi(prevTarget);
        }

        void DrawAll()
        {
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

        void DrawLeaveButton(Bootstrap boot, Rect r)
        {
            if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
            if (BtnAt(r, "Leave game", m_Button)) boot.Leave();
        }

        // ------------------------------------------------------------------ in-game HUD

        void DrawGame(Bootstrap boot, PlayerNet me, PlayerController pc)
        {
            var game = NetGame.Instance;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            int team = me.Team.Value;
            // the victory cutscene: just letterbox bars and a caption over it; the victory screen waits until it's over
            if (game != null && VictoryCutscene.Active) { DrawVictoryCutscene(game, team); return; }
            // Settings > Display > INTERFACE > HUD opacity (only while nothing is open over the game)
            s_HudAlpha = pc.MenuOpen || pc.Paused ? 1f : GameSettings.HudOpacity;
            GUI.color = new Color(1f, 1f, 1f, s_HudAlpha);

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
                        phase = $"Waiting for players  {PlayerNet.All.Count}/{Cfg.PlayersNeeded}  ({Cfg.ModeLabel})";
                        sub = "Rock brawl in the stadium while you wait!" + (boot.IsHost && PlayerNet.All.Count < 2 ? "  Friends join your IP." : "");
                        break;
                    case GameState.PreBall when Cfg.Tutorial:
                        phase = "Tutorial";
                        sub = "The clock is stopped - go at your own pace";
                        break;
                    case GameState.PreBall:
                        phase = "Wall drops in " + Clock(game.TimeLeft);
                        sub = Cfg.Builder ? "Gather, craft and build anywhere - then plant the ball for your team"
                            : Cfg.DnaRules ? "Mine trees and rocks for DNA, build your base (craft in your base)" : Cfg.WoodMode ? "Gather wood and build your base (craft in your base)" : "Gather wood & stone, build your base (craft in your base)";
                        break;
                    case GameState.BallLive:
                        phase = "Time left  " + Clock(game.TimeLeft);
                        sub = BallStatus(team);
                        break;
                    case GameState.SuddenDeath:
                        phase = "<color=#ff5555>SUDDEN DEATH</color>  " + Clock(game.TimeLeft);
                        sub = "Rocks only - first kill wins - don't fall into space";
                        break;
                }
            }
            if (phase != "" && Cfg.Rules != GameRules.Classic)
                phase += $"   <size={Mathf.RoundToInt(15 * k)}><color=#ffd24a>{Cfg.RulesName(Cfg.Rules).ToUpper()}</color></size>";
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
            // ---- the gun's rounds, big, top left (pistol, revolver, shotgun) ----
            if ((Cfg.IsGun(me.HeldItem) || me.HeldItem == Item.Shotgun) && !me.Dead.Value)
            {
                var ps = me.HeldStack;
                bool sg = ps.Id == Item.Shotgun;
                int mag = sg ? 1 : Cfg.GunMag(ps.Id);
                int spare = me.Count(sg ? Item.ShotgunShell : Cfg.GunAmmo(ps.Id));
                Shadowed(new Rect(14, 150 * k, 420 * k, 70 * k), $"<b><size={Mathf.RoundToInt(52 * k)}>{ps.Data}</size></b><size={Mathf.RoundToInt(22 * k)}> / {mag}  {Cfg.ItemName(ps.Id).ToUpper()}   <color=#bbbbbb>{spare} spare</color></size>", m_Label);
            }

            // ---- Builder crafting timers, then messages (top right) ----
            float my = 10 + DrawCraftStack(me, k, 10);
            float kf = DrawKillFeed(k, my); // the kill feed (Hud.KillFeed.cs), then the messages under it
            if (kf > 0) my += kf + 4 * k;
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
            if (pc.Scoped) DrawScope();
            float charge = Mathf.Max(pc.DrawAmount, pc.RamCharge, pc.EatProgress, pc.PackUpProgress); // (the last: holding E to pick up a chest / workbench)
            if (charge > 0)
            {
                Fill(new Rect(cx - 50 * k, cy + 30 * k, 100 * k, 8 * k), new Color(0, 0, 0, 0.5f));
                Fill(new Rect(cx - 50 * k, cy + 30 * k, 100 * k * charge, 8 * k), charge >= 1f ? new Color(1f, 0.85f, 0.2f) : Color.white);
            }
            DrawDamageNumbers(k);

            // ---- aim info / held item hints ----
            float by = sh - 150 * k;
            if (!string.IsNullOrEmpty(pc.AimText) && !pc.MenuOpen && Tutorial.Allows(TutFeature.Interact)) Shadowed(new Rect(0, cy + 45 * k, sw, 26 * k), pc.AimText, m_Center);
            if (!me.CarryingBall && !pc.MenuOpen)
            {
                string hint = HeldHint(me, pc);
                if (hint != "") Shadowed(new Rect(0, by - 52 * k, sw, 26 * k), hint, m_Center);
                if (!string.IsNullOrEmpty(pc.BuildHint)) Shadowed(new Rect(0, by - 28 * k, sw, 26 * k), "<color=#ff8888>" + pc.BuildHint + "</color>", m_Center);
            }

            // ---- hotbar (the tutorial shows it once you get your first wood) ----
            if (!pc.MenuOpen && Tutorial.Allows(TutFeature.HotbarHud))
            {
                float slot = 80 * k, gap = 6 * k; // (six slots: a little bigger than the seven used to be)
                int n = Cfg.HotbarSize;
                float hx = cx - (n * slot + (n - 1) * gap) / 2, hy = sh - slot - 14 * k;
                for (int i = 0; i < n; i++)
                {
                    var r = new Rect(hx + i * (slot + gap), hy, slot, slot);
                    bool sel = me.HeldSlot.Value == i && !me.CarryingBall;
                    DrawSlotVisual(r, me.SlotAt(i), sel, me);
                    if (sel && me.SlotAt(i).Empty) DrawRockGhost(r);
                    Shadowed(new Rect(r.x + 5 * k, r.y + 2, 34 * k, 22 * k), Binds.Short(Bind.Hotbar1 + i), m_SlotKey); // the key number, bold (the stack count stays normal)
                }
            }

            // ---- health (bottom left; the tutorial shows it at the food step) ----
            if (Tutorial.Allows(TutFeature.Health))
            {
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
                    Fill(new Rect(ab.x + 2, ab.y + 2, (ab.width - 4) * Mathf.Clamp01(me.ArmorHp.Value / (float)Mathf.Max(1, me.ArmorHp.Value > Cfg.ArmorHp ? Cfg.HeavyArmorHp : Cfg.ArmorHp)), ab.height - 4), new Color(0.75f, 0.55f, 0.3f));
                    Shadowed(new Rect(ab.x + 6, ab.y, ab.width, ab.height), $"<b>ARMOUR {me.ArmorHp.Value}</b>", m_Small);
                    statY -= 26 * k;
                }
                // the ball buff, just above the armour: your team's ball is in your base, so gathering pays more
                if (me.BallBuff && (game == null || game.S != GameState.SuddenDeath))
                {
                    var br = new Rect(hr.x, statY, hw, 22 * k);
                    Fill(br, new Color(0.3f, 0.22f, 0f, 0.65f));
                    Fill(new Rect(br.x, br.y, 4 * k, br.height), new Color(1f, 0.82f, 0.25f, 0.95f));
                    Shadowed(new Rect(br.x + 10 * k, br.y, br.width, br.height), $"<color=#ffd24a><b>BALL BUFF</b></color>  <color=#eeeeee>+{(Cfg.BallGatherMul - 1f) * 100f:0}% gathering</color>", m_Small);
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
                if (me.Giant)
                {
                    Shadowed(new Rect(hr.x, statY, 400 * k, 22 * k), $"<color=#ffcc55><b>GIANT</b> {Mathf.CeilToInt((float)(me.GiantUntil.Value - me.NetworkManager.ServerTime.Time))}s</color>", m_Small);
                    statY -= 22 * k;
                }
                if (me.Invisible)
                {
                    float left = (float)(me.InvisUntil.Value - me.NetworkManager.ServerTime.Time);
                    Shadowed(new Rect(hr.x, statY, 400 * k, 22 * k), me.Hidden ? $"<color=#c98bff><b>INVISIBLE</b> {Mathf.CeilToInt(left)}s</color>" : $"<color=#ff9f5a><b>VISIBLE (attacking)</b></color> <color=#c98bff>{Mathf.CeilToInt(left)}s</color>", m_Label);
                }
            }

            if (Tutorial.Allows(TutFeature.HotbarHud)) DrawGains(k);

            DrawCountdowns(game, team);

            // ---- banner ----
            float bage = Time.time - s_BannerTime;
            if (bage < 4f && game != null && game.S != GameState.GameOver && !game.FightFrozen && !(game.S == GameState.BallLive && game.TimeLeft <= 10f)) // (not over the final countdown)
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
                    if (BtnAt(r1, "[1]  RESPAWN IN BASE", m_Button)) pc.ChooseRespawn(false);
                    if (BtnAt(r2, "[2]  RESPAWN IN THE WILD", m_Button)) pc.ChooseRespawn(true);
                    Shadowed(new Rect(0, by2 + bh + 6 * k, sw, 26 * k), "<color=#bbbbbb>The wild drops you somewhere random in the enemy's half of the map</color>", m_Center);
                }
                else Shadowed(new Rect(0, sh * 0.4f + 60 * k, sw, 30 * k), home
                    ? $"Your stuff spilled out where you died. Respawning on your bedrock in {Mathf.CeilToInt(t)}"
                    : $"Your stuff spilled out where you died. Respawn in {Mathf.CeilToInt(t)}", m_Center);
            }

            // (the HUD's opacity stops here: the inventory, wheel, tutorial panel and menus are drawn solid)
            s_HudAlpha = 1f;
            GUI.color = Color.white;
            if (pc.MenuOpen) DrawInventory(me, pc);
            FlareTip.Draw(k, m_Label, m_Small, Fill, Shadowed); // (by the glass wall while it's up: the ball is the emergency flare)
            Tutorial.Draw(k, m_Label, m_Small, Fill, Shadowed);
            if (!pc.Paused) Chat.Draw(k, m_Small, Fill, Shadowed);
            DrawDragAndFlights(me, pc); // (on top of the bag, the tutorial panel and the chat)
            if (pc.WheelOpen) DrawWheel(pc);
            if (pc.AirstrikeMapOpen) DrawAirstrikeMap(me, pc);

            if (pc.Paused && (game == null || game.S != GameState.GameOver)) DrawPause(boot, me, pc, game);
            else m_PausePage = PausePage.Root;

            if (game != null && game.S == GameState.GameOver) DrawGameOver(boot, game, team);
        }

        string HeldHint(PlayerNet me, PlayerController pc)
        {
            var s = me.HeldStack;
            switch (s.Id)
            {
                case Item.BuildingPlan:
                    if (pc.DemolishMode) return "<b>Demolish</b>    LMB: take down your own piece (half the wood back)    hold RMB: building wheel";
                    return $"<b>{Cfg.PieceName(pc.BuildPiece)}</b>  ({Cfg.PieceWood(pc.BuildPiece)} {Cfg.CurrencyName})    hold RMB: building wheel   R: rotate stairs   " + (Cfg.WoodMode || Tutorial.HideStone ? "" : "F: upgrade to stone   ") + "X: demolish yours";
                case Item.Ram: return $"<b>Battering Ram</b> ({s.Data} hit{(s.Data == 1 ? "" : "s")} left)    hold LMB at an enemy piece: wood breaks instantly, stone / metal drop one step";
                case Item.Spear: return "<b>Spear</b>    LMB: stab    hold RMB + LMB: throw    E: pick thrown spears back up";
                case Item.Bow: return $"<b>Bow</b>  ({me.Count(Item.Arrow)} arrows)    hold LMB to draw, release to fire";
                case Item.Chest: return "<b>Storage Chest</b>    LMB: place it inside your base";
                case Item.Workbench:
                case Item.Workbench2: return $"<b>{Cfg.ItemName(s.Id)}</b>    LMB: put it down anywhere in your base - its items then show in your crafting list ({Binds.Name(Bind.Inventory)})";
                case Item.Barrier: return $"<b>High External Wall</b> x{s.Count}    LMB: place it - in your base or out in the open (not in the enemy base)";
                case Item.Berry: return $"<b>Berries</b> x{s.Count}    RMB: eat ({Cfg.BerryEatTime:0.#}s, +{Cfg.BerryHeal:0} HP)   LMB on a horse: feed it (+{Cfg.HorseBerryHeal:0} HP)";
                case Item.C4: return "<b>C4</b>    LMB: throw it at enemy buildings - it blows up everything nearby";
                case Item.DeathWand: return "<b>Death Wand</b> (1 shot)    LMB: fire - kills anyone it passes close to, or fells a tree (all its wood to you), or destroys one building piece";
                case Item.Helmet: return "<b>Alien Helmet</b>    LMB: put it on - the next headshot does no damage and breaks it";
                case Item.Armor: return $"<b>Wooden Armour</b> ({(s.Data > 0 ? s.Data : Cfg.ArmorHp)} HP)    LMB: put it on - a second health bar that goes first";
                case Item.Crossbow: return $"<b>Crossbow</b>  ({(s.Data > 0 ? "loaded" : "empty")}, {me.Count(Item.Arrow)} arrows)    LMB: fire   hold RMB: aim   reloads by itself (one reload for all your crossbows)";
                case Item.FortTower: return "<b>Fort Tower</b>    LMB: throw it - a lookout tower with a ladder pops up where it lands";
                case Item.Car: return "<b>Wooden Car</b>    LMB: put it down, then E to drive";
                case Item.Saddle: return $"<b>Saddle</b> ({Cfg.TeamLabel(s.Data > 0 ? s.Data - 1 : me.Team.Value)})    walk up to a wild horse and press E to saddle and ride it";
                case Item.Meat: return $"<b>Horse Meat</b>    RMB: eat ({Cfg.MeatEatTime:0.#}s, heals you fully)";
                case Item.Sniper: return $"<b>Sniper Rifle</b> ({s.Data} shots)    hold RMB: scope   LMB: fire - one hit kills (a helmet stops a headshot)";
                case Item.PortalGun:
                {
                    int left = s.Data <= 0 || s.Data > Cfg.PortalShots ? Cfg.PortalShots : s.Data;
                    return $"<b>Portal Gun</b> ({left} portal{(left == 1 ? "" : "s")} left)    LMB: shoot a portal onto any surface - " + (left >= Cfg.PortalShots ? "two shots make a linked pair" : "this shot links it to your first one");
                }
                case Item.Jetpack: return $"<b>Jetpack</b> (fuel {s.Data}%)    hold Space to fly";
                case Item.SlenderEgg: return "<b>Slenderman Egg</b>    LMB: throw it - Slenderman hatches and hunts your enemy";
                case Item.BuildEgg: return "<b>Build Egg</b>    LMB: throw it - blocks appear along its path to walk on";
                case Item.GiantStaff: return "<b>Staff of the Giant</b>    LMB: turn your nearest enemy into a giant";
                case Item.RocketLauncher: return $"<b>Rocket Launcher</b> ({Mathf.Max(1, (int)s.Data)} rocket{(s.Data > 1 ? "s" : "")})    LMB: fire - wrecks enemy buildings";
                case Item.BombBush: return "<b>Fake Bomb Bush</b>    LMB: throw it - whoever picks it blows up";
                case Item.TreeCamo: return "<b>Tree Camo</b>    while you hold it you're a tree    hold LMB / RMB: stand still and just look around";
                case Item.Airstrike: return "<b>Airstrike</b>    LMB: pick a spot on the map - everything there gets flattened";
                case Item.Wallhack: return "<b>Wallhack Glasses</b>    hold them to see your enemies through walls";
                case Item.InvisPotion: return $"<b>Invisibility Potion</b>    LMB: drink ({Cfg.InvisTime:0}s, attacking shows you)";
                case Item.Chainsaw: return $"<b>Chainsaw</b> ({s.Data} uses left)    hold LMB: cuts wood and stone fast";
                case Item.EnderPearl: return "<b>Ender Pearl</b>    LMB: throw it - you teleport to wherever it lands";
                case Item.Pistol: return $"<b>Pistol</b>  ({s.Data} shot{(s.Data == 1 ? "" : "s")} left)    LMB: shoot" + (me.Count(Item.PistolAmmo) > 0 ? $"   R: reload ({me.Count(Item.PistolAmmo)} spare ammo)" : "");
                case Item.PistolAmmo: return "<b>Pistol Ammo</b>    the pistol reloads from this";
                case Item.Revolver: return $"<b>Revolver</b>  ({s.Data}/{Cfg.RevolverMag})    LMB: shoot   R: reload ({me.Count(Item.RevolverAmmo)} bullets)";
                case Item.RevolverAmmo: return $"<b>Revolver Bullets</b> x{s.Count}    the revolver reloads from these";
                case Item.Shotgun: return $"<b>Waterpipe Shotgun</b>  ({(s.Data > 0 ? "loaded" : "empty")}, {me.Count(Item.ShotgunShell)} shells)    LMB: fire   loads the next shell by itself (or R)";
                case Item.ShotgunShell: return $"<b>Shotgun Shells</b> x{s.Count}    one at a time into the waterpipe shotgun";
                case Item.Sword: return $"<b>Sword</b>    LMB: a slow, heavy swing - {Cfg.SwordBodyDamage:0} to the body, {Cfg.SwordHeadDamage:0} to the head";
                case Item.HeavyArmor: return $"<b>Heavy Armour</b> ({Cfg.HeavyArmorHp} HP)    LMB: put it on (replaces wooden armour)";
                case Item.TreeCracker: return $"<b>Tree Cracker</b> ({s.Data} uses left)    LMB: fells a whole tree in one hit";
                case Item.Boat: return $"<b>Boat</b>    LMB on the water: put it in, then E to get in (only on maps with water)";
                case Item.FortifyBuff: return $"<b>{Cfg.ItemName(s.Id)}</b>";
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

        void DrawSlotVisual(Rect r, ItemStack s, bool selected, PlayerNet me, float pop = 1f)
        {
            Fill(r, selected ? new Color(1f, 0.85f, 0.3f, 0.55f) : new Color(0, 0, 0, 0.5f));
            Fill(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(1, 1, 1, 0.08f));
            if (s.Empty) return;
            var icon = ItemIcons.Get(s.Id);
            float iw = r.width * 0.8f * pop, ih = r.height * 0.8f * pop;
            var inner = new Rect(r.center.x - iw / 2, r.y + r.height * 0.48f - ih / 2, iw, ih);
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
            NoteSlotRect(kind, index, r);
            bool hover = r.Contains(e.mousePosition);
            bool isSource = m_Dragging && m_DragFrom.Kind == kind && m_DragFrom.Index == index;
            // the slot a drag came from shows what's left behind (nothing, or the other half of a split); a slot something
            // is flying into shows what it had before it lands (Hud.Flights.cs)
            var shown = isSource ? (m_DragHalf ? s.WithCount(s.Count - DragAmount) : default)
                : DropWaiting(kind, index) ? (s.Equals(m_DropStack) ? s.WithCount(s.Count - m_DropAmount) : s)
                : FlightView(kind, index, s);
            DrawSlotVisual(r, shown, selected, me, LandPop(kind, index));
            if (hover) { Fill(r, new Color(1, 1, 1, 0.08f)); if (!s.Empty) SetHover(s, me.Team.Value); }

            // Shift+drag: every slot the pointer passes over is shift-clicked, once each (Hud.Sweep below)
            if (m_Sweeping && e.type == EventType.MouseDrag && e.shift && !s.Empty && SweepCrosses(r, e.mousePosition)) SweepSlot(kind, index, me, pc);
            if (e.type == EventType.MouseDown && hover && !s.Empty)
            {
                if (e.shift && e.button == 0) SweepSlot(kind, index, me, pc); // (a shift-click: the sweep's first slot)
                else
                {
                    m_Dragging = true;
                    m_DragHalf = e.button == 1;
                    m_DragFrom = new SlotRef { Kind = kind, Index = index };
                    m_DragStack = s;
                    m_DragSlot = r.width;
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && hover && m_Dragging)
            {
                int amount = DragAmount;
                if (!(m_DragFrom.Kind == kind && m_DragFrom.Index == index))
                {
                    NoteDrop(m_DragFrom.Kind, m_DragFrom.Index, e.mousePosition, m_DragStack, amount);
                    me.MoveItemRpc(m_DragFrom.Kind, (byte)m_DragFrom.Index, kind, (byte)index, (ushort)amount, LootRef(pc));
                    Sfx.Play2D(Sfx.Pop, 0.3f, 0.2f);
                }
                // a right-click (no drag) on an inventory item sends it down to the hotbar
                else if (m_DragHalf && kind == 0 && index >= Cfg.HotbarSize) ToHotbar(index, me);
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
                case Item.Chainsaw:
                case Item.TreeCracker: return $"  ({s.Data} uses left)";
                case Item.Pistol: return $"  ({s.Data}/{Cfg.PistolMag})";
                case Item.Revolver: return $"  ({s.Data}/{Cfg.RevolverMag})";
                case Item.Shotgun: return s.Data > 0 ? "  (loaded)" : "  (empty)";
                case Item.Armor: return $"  ({(s.Data > 0 ? s.Data : Cfg.ArmorHp)} armour HP)";
                case Item.Crossbow: return s.Data > 0 ? "  (loaded)" : "  (empty)";
                case Item.Berry: return $"  (RMB to eat, +{Cfg.BerryHeal:0} HP)";
                default: return s.Count > 1 ? $"  x{s.Count}" : "";
            }
        }

        /// <summary>Right-click in the inventory: onto the hotbar (a stack of the same thing first, then an empty slot).</summary>
        void ToHotbar(int index, PlayerNet me)
        {
            var s = me.SlotAt(index);
            if (s.Empty) return;
            int target = -1;
            for (int i = 0; i < Cfg.HotbarSize && target < 0; i++) if (me.SlotAt(i).Id == s.Id && me.SlotAt(i).Count < Cfg.MaxStack(s.Id)) target = i;
            for (int i = 0; i < Cfg.HotbarSize && target < 0; i++) if (me.SlotAt(i).Empty) target = i;
            if (target < 0) { Push("Your hotbar is full"); Sfx.Play2D(Sfx.Click, 0.4f); return; }
            me.MoveItemRpc(0, (byte)index, 0, (byte)target, s.Count, default);
            Sfx.Play2D(Sfx.Pop, 0.3f, 0.2f);
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
            // (a slot this sweep already touched may not have heard back from the server yet: leave it alone)
            for (int i = start; i < end && target < 0; i++) if (!m_Swept.Contains(SlotKey(0, i)) && me.SlotAt(i).Id == s.Id && me.SlotAt(i).Count < Cfg.MaxStack(s.Id)) target = i;
            for (int i = start; i < end && target < 0; i++) if (!m_Swept.Contains(SlotKey(0, i)) && me.SlotAt(i).Empty) target = i;
            if (target >= 0)
            {
                me.MoveItemRpc(0, (byte)index, 0, (byte)target, s.Count, default);
                m_Swept.Add(SlotKey(0, target)); // (and a sweep doesn't carry it straight back when it passes over it)
            }
        }

        // ------------------------------------------------------------------ Shift+drag: shift-click every slot you pass over

        readonly HashSet<int> m_Swept = new HashSet<int>();
        bool m_Sweeping;
        Vector2 m_SweepLast;
        /// <summary>Test hook: how many slots shift-click / shift-drag has quick-moved.</summary>
        public static int SweepMoves { get; private set; }

        /// <summary>Shift+LMB went down in the bag: a new sweep (each slot can be moved once until the button comes up).</summary>
        void BeginSweep(Vector2 at)
        {
            m_Sweeping = true;
            m_Swept.Clear();
            m_SweepLast = at;
        }

        /// <summary>The pointer moved from where it was last drag event to `now`: did it pass over r (fast moves skip no slot)?</summary>
        bool SweepCrosses(Rect r, Vector2 now)
        {
            float len = Vector2.Distance(m_SweepLast, now);
            int steps = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(4f, r.width * 0.25f)), 1, 64);
            for (int i = 0; i <= steps; i++) if (r.Contains(Vector2.Lerp(m_SweepLast, now, i / (float)steps))) return true;
            return false;
        }

        /// <summary>Quick-move one slot, unless this sweep has already moved it (or moved something into it).</summary>
        void SweepSlot(byte kind, int index, PlayerNet me, PlayerController pc)
        {
            if (!m_Swept.Add(SlotKey(kind, index))) return;
            SweepMoves++;
            QuickMove(kind, index, me, pc);
        }

        /// <summary>After the slots have seen this event: remember where the pointer was, and stop on button up.</summary>
        void SweepEvent(Event e)
        {
            if (e.type == EventType.MouseDrag) m_SweepLast = e.mousePosition;
            if (e.rawType == EventType.MouseUp && e.button == 0) m_Sweeping = false;
        }

        /// <summary>AutoTest: a shift-drag over these slots (kind 0 bag / 1 open chest, index), in this order.</summary>
        public static void TestSweep(params Vector2Int[] path)
        {
            var h = Object.FindFirstObjectByType<Hud>();
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            if (h == null || me == null || pc == null) return;
            h.BeginSweep(Vector2.zero);
            foreach (var p in path)
            {
                var lt = pc.LootTarget;
                var s = p.x == 0 ? me.SlotAt(p.y) : lt != null && p.y < lt.Slots.Count ? lt.Slots[p.y] : default;
                if (!s.Empty) h.SweepSlot((byte)p.x, p.y, me, pc);
            }
            h.m_Sweeping = false;
        }

        // ------------------------------------------------------------------ inventory / crafting / loot

        /// <summary>The three lines under the bag that say how to move things.</summary>
        public const string InvHelp = "Drag to move\nRight+Drag to Split\nShift+Click/Drag to quick-move";

        void DrawInventory(PlayerNet me, PlayerController pc)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            MouseOverUI = true;
            m_HoverTitle = m_HoverText = "";
            TrackFlights(me, pc);
            var ev0 = Event.current;
            if (ev0.type == EventType.MouseDown && ev0.button == 0 && ev0.shift) BeginSweep(ev0.mousePosition); // (Shift+drag starts here)
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.4f));
            // six across (inventory, hotbar and chests): bigger slots than the old seven, about the same width
            const int cols = Cfg.HotbarSize;
            float slot = Mathf.Min(76 * k, (sw - 120) / 21f), gap = 6 * k;
            float gridW = cols * slot + (cols - 1) * gap;
            bool loot = pc.LootTarget != null;
            float top = sh * 0.14f;
            // the crafting list: one column (Hud.Crafting.cs)
            bool upgrades = !loot && pc.UpgradesOpen; // E on your alien machine: UPGRADES instead of crafting (Hud.Upgrades.cs)
            bool craft = !loot && !upgrades && Tutorial.Allows(TutFeature.Craft); // (the tutorial: once crafting is taught)
            int craftCols = craft ? LayoutCraftList(me, pc, top, sh - 8 * k, k) : 1;
            float colGap = 10 * k;
            float craftW = 440 * k;
            if (craftCols == 2) craftW = Mathf.Min(400 * k, (sw - 20 - gridW - 30 * k - colGap) / 2f);
            float craftTotal = craftCols * craftW + (craftCols - 1) * colGap;
            float lootW = loot ? gridW : 0;
            float total = gridW + 30 * k + (loot ? lootW : craftTotal);
            float x0 = Mathf.Max(10, (sw - total) / 2);
            float invX = x0;
            float lootX = invX + gridW + 30 * k;
            float lootBottom = top;

            // ---- loot container (right of the inventory) ----
            if (loot)
            {
                var c = pc.LootTarget;
                Shadowed(new Rect(lootX, top - 34 * k, lootW, 30 * k), $"<b>{c.DisplayName.ToUpper()}</b>" + (c.TakeOnly ? "  <color=#bbbbbb>(take only)</color>" : ""), m_Label);
                int n = c.Slots.Count;
                for (int i = 0; i < n; i++)
                {
                    var r = new Rect(lootX + (i % cols) * (slot + gap), top + (i / cols) * (slot + gap), slot, slot);
                    DrawSlot(r, c.Slots[i], 1, i, false, me, pc);
                }
                lootBottom = top + Mathf.Max(1, (n + cols - 1) / cols) * (slot + gap);
                if (c.IsGamble) { DrawGamblePanel(c, me, pc, lootX, top + slot + gap, lootW, slot, k); lootBottom = top + slot + gap + 140 * k; } // DNA mode: the GAMBLE button
            }

            // ---- my inventory (3 rows of 6) + hotbar ----
            Shadowed(new Rect(invX, top - 34 * k, gridW, 30 * k), "<b>INVENTORY</b>", m_Label);
            for (int i = 0; i < Cfg.MainSize; i++)
            {
                int idx = Cfg.HotbarSize + i;
                var r = new Rect(invX + (i % cols) * (slot + gap), top + (i / cols) * (slot + gap), slot, slot);
                DrawSlot(r, me.SlotAt(idx), 0, idx, false, me, pc);
            }
            int rows = (Cfg.MainSize + cols - 1) / cols;
            float hotY = top + rows * (slot + gap) + 26 * k;
            Shadowed(new Rect(invX, hotY - 26 * k, gridW, 24 * k), $"<b>HOTBAR</b>  <color=#bbbbbb>(keys 1-{Cfg.HotbarSize})</color>", m_Small);
            for (int i = 0; i < Cfg.HotbarSize; i++)
            {
                var r = new Rect(invX + i * (slot + gap), hotY, slot, slot);
                DrawSlot(r, me.SlotAt(i), 0, i, me.HeldSlot.Value == i, me, pc);
                Shadowed(new Rect(r.x + 5 * k, r.y + 2, 34 * k, 22 * k), Binds.Short(Bind.Hotbar1 + i), m_SlotKey); // the key number, bold (the stack count stays normal)
            }
            SweepEvent(Event.current);
            float infoY = hotY + slot + 10 * k;
            if (TestHover != Item.None) SetHover(ItemStack.Of(TestHover, 1), me.Team.Value); // (AutoTest: as if the mouse were on it)
            // ---- crafting (right, when not looting): Hud.Crafting.cs - what the mouse is over shows under it ----
            float cxp = invX + gridW + 30 * k;
            bool hoverShown = false;
            if (craft) { DrawCraftList(me, pc, cxp, top, craftW, colGap, sh - 8 * k, k); hoverShown = true; }
            if (upgrades) { DrawUpgradeList(me, pc, cxp, top, craftW, sh - 8 * k, k); hoverShown = true; }
            // how to move things: always these three lines under the bag
            Shadowed(new Rect(invX, infoY, gridW, 60 * k), $"<color=#ffffff>{InvHelp}</color>", m_SmallWrap);
            // no crafting list beside the bag (a chest is open, or the tutorial hasn't got there): the description goes
            // under the chest, or under the help lines
            if (!hoverShown)
            {
                var hr = loot ? new Rect(lootX, lootBottom + 8 * k, Mathf.Max(lootW, 300 * k), 120 * k) : new Rect(invX, infoY + 62 * k, gridW, 120 * k);
                DrawHoverInfo(hr, k);
            }

            // ---- drag: let go outside the item grids to throw it on the ground ----
            var e = Event.current;
            if (m_Dragging && e.type == EventType.MouseUp)
            {
                // released outside the item grids: throw it on the ground (the rock stays with you)
                var invRect = new Rect(invX - 10, top - 10, gridW + 20, hotY + slot - top + 20);
                var lootRect = new Rect(lootX - 10, top - 10, lootW + 20, Mathf.Max(lootBottom, top + 5 * (slot + gap)) - top + 20);
                bool outside = !invRect.Contains(e.mousePosition) && !(loot && lootRect.Contains(e.mousePosition));
                if (outside)
                {
                    me.DropItemRpc(m_DragFrom.Kind, (byte)m_DragFrom.Index, (ushort)DragAmount, LootRef(pc));
                    Sfx.Play2D(Sfx.Throw, 0.4f);
                }
                m_Dragging = false;
                e.Use();
            }
        }

        /// <summary>
        /// On top of everything: what's being dragged follows the pointer (a right-drag shows the half it's carrying from
        /// the moment it's picked up), and items fly to their new slots (Hud.Flights.cs).
        /// </summary>
        void DrawDragAndFlights(PlayerNet me, PlayerController pc)
        {
            var e = Event.current;
            if (!pc.MenuOpen) { m_Dragging = false; m_Sweeping = false; m_Flights.Clear(); return; }
            // (the mouse buttons were let go somewhere the bag never heard about: drop the drag / the shift-drag)
            if (m_Dragging && e.type == EventType.Repaint && !TestDragHeld && !Input.GetMouseButton(0) && !Input.GetMouseButton(1)) m_Dragging = false;
            if (m_Sweeping && e.type == EventType.Repaint && !Input.GetMouseButton(0)) m_Sweeping = false;
            DrawFlights(me);
            if (!m_Dragging || e.type != EventType.Repaint) return;
            // the real pointer (IMGUI's own position isn't always updated while the right button is held)
            var mp = TestDragHeld ? TestDragAt : new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            float s = m_DragSlot;
            var dr = new Rect(mp.x - s * 0.42f, mp.y - s * 0.42f, s * 0.84f, s * 0.84f);
            // (just the icon on the pointer: no slot background behind it)
            var icon = ItemIcons.Get(m_DragStack.Id);
            if (icon != null) GUI.DrawTexture(dr, icon, ScaleMode.ScaleToFit, true);
            else GUI.Label(dr, Cfg.ItemName(m_DragStack.Id), m_Small);
            int amt = DragAmount;
            if (amt > 1 || m_DragHalf)
                Shadowed(new Rect(dr.x, dr.yMax - 20 * m_Scale, dr.width - 3, 20 * m_Scale), amt.ToString(), new GUIStyle(m_Small) { alignment = TextAnchor.LowerRight });
        }

        /// <summary>AutoTest: pretend a drag is held at a screen point (to photograph the split stack on the pointer).</summary>
        public static bool TestDragHeld;
        /// <summary>AutoTest: the bag describes this item as if the mouse were over it.</summary>
        public static Item TestHover;
        public static Vector2 TestDragAt;
        public static void TestStartDrag(byte kind, int index, bool half)
        {
            var h = Object.FindFirstObjectByType<Hud>();
            var me = PlayerNet.Local;
            if (h == null || me == null) return;
            var lt = PlayerController.Local != null ? PlayerController.Local.LootTarget : null;
            var s = kind == 0 ? me.SlotAt(index) : lt != null && index < lt.Slots.Count ? lt.Slots[index] : default;
            h.m_Dragging = !s.Empty;
            h.m_DragHalf = half;
            h.m_DragFrom = new SlotRef { Kind = kind, Index = index };
            h.m_DragStack = s;
            TestDragHeld = h.m_Dragging;
        }
        public static void TestEndDrag()
        {
            var h = Object.FindFirstObjectByType<Hud>();
            if (h != null) h.m_Dragging = false;
            TestDragHeld = false;
        }
        /// <summary>AutoTest: where a slot was last drawn (GUI space), or an empty rect.</summary>
        public static Rect TestSlotRect(byte kind, int index)
        {
            var h = Object.FindFirstObjectByType<Hud>();
            return h != null && h.m_SlotRects.TryGetValue(SlotKey(kind, index), out var r) ? r : default;
        }

        // ------------------------------------------------------------------ build wheel

        static Texture2D[] s_Wedges;
        static Texture2D s_Disc, s_Scope;

        /// <summary>Donut slices for the wheel (one texture per slice), a filled circle, and the sniper scope mask.</summary>
        static void EnsureWheelTextures(int n)
        {
            if (s_Wedges != null && s_Wedges.Length == n) return;
            const int S = 256;
            s_Wedges = new Texture2D[n];
            for (int i = 0; i < n; i++)
            {
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[S * S];
                float centre = i * 360f / n, half = 180f / n - 1.2f;
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = -((y + 0.5f) / S * 2f - 1f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; // 0 = up, clockwise (texture y is flipped by GUI)
                    float da = Mathf.Abs(Mathf.DeltaAngle(ang, centre));
                    float a = Mathf.Clamp01((r - 0.34f) * 60f) * Mathf.Clamp01((0.99f - r) * 60f) * Mathf.Clamp01((half - da) * 1.5f);
                    px[(S - 1 - y) * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(px);
                tex.Apply();
                s_Wedges[i] = tex;
            }
            if (s_Disc == null)
            {
                s_Disc = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float a = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * 60f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                s_Disc.SetPixels32(px);
                s_Disc.Apply();
            }
        }

        /// <summary>
        /// Rust-style building wheel while RMB is held with the building plan: dark slices with the piece icons, the one the
        /// mouse points at lights up, and the name and cost show in the middle. There's always a slice selected.
        /// </summary>
        void DrawWheel(PlayerController pc)
        {
            float k = m_Scale;
            var opts = PlayerController.WheelOptions;
            EnsureWheelTextures(opts.Length);
            var c = s_WheelCenter;
            var mouse = Event.current.mousePosition;
            var d = mouse - c;
            // always snapped to a slice: the one in the mouse's direction, or the current one while the mouse is still in the middle
            if (d.magnitude > 18f * k)
            {
                float ang = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;
                WheelHover = Mathf.RoundToInt(ang / (360f / opts.Length)) % opts.Length;
            }
            else if (WheelHover < 0) WheelHover = pc.WheelIndex;
            float R = 190f * k;
            var area = new Rect(c.x - R, c.y - R, 2 * R, 2 * R);
            var old = GUI.color;
            for (int i = 0; i < opts.Length; i++)
            {
                bool hover = i == WheelHover;
                bool disabled = opts[i].Upgrade && Cfg.WoodMode;
                GUI.color = hover ? new Color(0.85f, 0.55f, 0.2f, 0.92f) : opts[i].Demolish ? new Color(0.35f, 0.1f, 0.08f, 0.75f) : new Color(0.38f, 0.62f, 0.95f, 0.82f);
                GUI.DrawTexture(area, s_Wedges[i]);
                float a = i * (360f / opts.Length) * Mathf.Deg2Rad;
                var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * R * 0.67f;
                float isz = 78f * k;
                GUI.color = disabled ? new Color(1, 1, 1, 0.25f) : Color.white;
                var icon = ItemIcons.Wheel(i);
                if (icon != null) GUI.DrawTexture(new Rect(p.x - isz / 2, p.y - isz / 2, isz, isz), icon, ScaleMode.ScaleToFit, true);
                else GUI.Label(new Rect(p.x - 50, p.y - 12, 100, 24), opts[i].Label, m_Center);
            }
            // centre: name and cost
            GUI.color = new Color(0.22f, 0.45f, 0.8f, 0.9f);
            GUI.DrawTexture(new Rect(c.x - R * 0.32f, c.y - R * 0.32f, R * 0.64f, R * 0.64f), s_Disc);
            GUI.color = old;
            var o = opts[Mathf.Clamp(WheelHover, 0, opts.Length - 1)];
            string cost = o.Demolish ? "your own pieces\nhalf the wood back" : o.Upgrade ? (Cfg.WoodMode ? "not in wood mode" : Tutorial.HideStone ? "after the tutorial" : "to stone\nLMB on your piece") : $"{Cfg.PieceWood(o.Piece)} {Cfg.CurrencyName}";
            var title = new GUIStyle(m_Center) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(20 * k) };
            title.normal.textColor = o.Demolish ? new Color(1f, 0.5f, 0.4f) : Color.white;
            GUI.Label(new Rect(c.x - 100 * k, c.y - 28 * k, 200 * k, 26 * k), o.Label.ToUpper(), title);
            GUI.Label(new Rect(c.x - 100 * k, c.y - 2 * k, 200 * k, 44 * k), $"<size={Mathf.RoundToInt(13 * k)}><color=#cccccc>{cost}</color></size>", m_Center);
            Fill(new Rect(mouse.x - 3, mouse.y - 3, 6, 6), Color.white);
        }

        // ------------------------------------------------------------------ airstrike map

        /// <summary>Top-down map: click where the airstrike should land.</summary>
        void DrawAirstrikeMap(PlayerNet me, PlayerController pc)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            MouseOverUI = true;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.75f));
            float size = Mathf.Min(sw, sh) * 0.8f;
            var map = new Rect((sw - size) / 2, (sh - size) / 2 + 14 * k, size, size);
            float half = Cfg.MapHalf;
            Vector2 ToMap(Vector3 w) => new Vector2(map.center.x + w.x / (2 * half) * size, map.center.y - w.z / (2 * half) * size);
            Fill(map, new Color(0.3f, 0.5f, 0.27f));
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var bc = Cfg.BaseCenter[t];
                var a = ToMap(bc + new Vector3(-Cfg.BaseHalf, 0, Cfg.BaseHalf));
                float bs = Cfg.BaseHalf * 2 / (2 * half) * size;
                var col = Cfg.TeamColor[t];
                Fill(new Rect(a.x, a.y, bs, bs), new Color(col.r, col.g, col.b, 0.55f));
                GUI.Label(new Rect(a.x, a.y + bs / 2 - 12, bs, 24), $"<b>{Cfg.TeamName[t]}</b>", m_Center);
            }
            foreach (var c in Container.All) if (c.IsAirdrop) { var q = ToMap(c.transform.position); Fill(new Rect(q.x - 5, q.y - 5, 10, 10), new Color(0.75f, 0.35f, 1f)); }
            if (Ball.Instance != null) { var q = ToMap(Ball.Instance.transform.position); Fill(new Rect(q.x - 5, q.y - 5, 10, 10), new Color(1f, 0.85f, 0.15f)); }
            foreach (var p in PlayerNet.All)
            {
                if (p.Dead.Value || (p != me && p.Team.Value != me.Team.Value)) continue; // you only know where your own side is
                var q = ToMap(p.transform.position);
                Fill(new Rect(q.x - 4, q.y - 4, 8, 8), p == me ? Color.white : Cfg.TeamColor[p.Team.Value]);
            }
            Shadowed(new Rect(0, map.y - 34 * k, sw, 30 * k), "<b>AIRSTRIKE</b> - click where it should hit  ·  RMB / Esc to cancel", m_Center);
            var e = Event.current;
            var m = e.mousePosition;
            if (map.Contains(m))
            {
                float rr = Cfg.AirstrikeRadius / (2 * half) * size;
                var old = GUI.color;
                GUI.color = new Color(1f, 0.15f, 0.1f, 0.45f);
                EnsureWheelTextures(PlayerController.WheelOptions.Length);
                GUI.DrawTexture(new Rect(m.x - rr, m.y - rr, rr * 2, rr * 2), s_Disc);
                GUI.color = old;
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    var world = new Vector3((m.x - map.center.x) / size * 2 * half, 0, -(m.y - map.center.y) / size * 2 * half);
                    pc.ConfirmAirstrike(world);
                    e.Use();
                }
            }
            if (e.type == EventType.MouseDown && e.button == 1) { pc.CloseAirstrikeMap(); e.Use(); }
        }

        // ------------------------------------------------------------------ countdowns and the sniper scope

        int m_LastCount = -1;

        /// <summary>The sudden death arena's countdown word right now ("" when there's none): Ready?, Set, then ROCK!.</summary>
        public static string FightWord(NetGame game, out float t)
        {
            t = 0f;
            if (game == null || !game.IsSpawned || game.S != GameState.SuddenDeath || game.FightAt.Value <= 0) return "";
            double left = game.FightAt.Value - game.NetworkManager.ServerTime.Time;
            float half = NetGame.FightCountdown * 0.5f;
            if (left > half) { t = (float)(NetGame.FightCountdown - left) / half; return "Ready?"; }
            if (left > 0) { t = (float)(half - left) / half; return "Set"; }
            if (left > -1.2) { t = (float)(-left) / 1.2f; return "ROCK!"; }
            return "";
        }

        /// <summary>The last 10 seconds before sudden death (it sits on 0 for a moment before the arena) or the win, and the arena's Ready? / Set / ROCK!.</summary>
        void DrawCountdowns(NetGame game, int myTeam)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            if (game == null) return;
            int sock = Ball.Instance != null ? Ball.Instance.SocketTeam.Value : -1;
            if (game.S == GameState.BallLive && game.TimeLeft <= 10f && (game.TimeLeft > 0f || sock < 0))
            {
                int n = Mathf.CeilToInt(game.TimeLeft);
                float frac = n == 0 ? 1f : game.TimeLeft - Mathf.Floor(game.TimeLeft);
                if (n != m_LastCount) { m_LastCount = n; Sfx.Play2D(n <= 3 ? Sfx.Ding : Sfx.Beep, n <= 3 ? 0.9f : 0.6f, 0f); Fx.Shake(0.08f + (10 - n) * 0.02f); }
                // (no coloured border round the screen - just the words and the number)
                string label = sock < 0 ? "SUDDEN DEATH IN" : sock == myTeam ? "YOU WIN IN" : $"{Cfg.TeamName[sock]} WINS IN";
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * k) };
                st.normal.textColor = new Color(1f, 0.9f, 0.85f, 0.95f);
                GUI.Label(new Rect(0, sh * 0.2f, sw, 40 * k), label, st);
                var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt((150f + (1f - frac) * 60f) * k) };
                big.normal.textColor = sock < 0 ? new Color(1f, 0.2f, 0.15f, 0.55f + frac * 0.4f) : new Color(1f, 0.9f, 0.3f, 0.55f + frac * 0.4f);
                GUI.Label(new Rect(0, sh * 0.24f, sw, 220 * k), n.ToString(), big);
                return;
            }
            string word = FightWord(game, out float t);
            if (word != "")
            {
                int n = word == "Ready?" ? 2 : word == "Set" ? 1 : 0;
                if (n != m_LastCount)
                {
                    m_LastCount = n;
                    if (n > 0) { Sfx.Play2D(Sfx.Ding, 1f, 0f); Fx.Shake(0.2f); }
                    else { Sfx.Play2D(Sfx.Boom, 0.7f, 0f); Fx.Shake(0.5f); }
                }
                // each word pops in big and settles; ROCK! fades away as the duel starts
                float pop = 1f + 0.35f * Mathf.Clamp01(1f - t * 4f);
                if (n > 0) Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.25f));
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(34 * k) };
                st.normal.textColor = new Color(1f, 0.9f, 0.4f, n > 0 ? 1f : 1f - t);
                GUI.Label(new Rect(0, sh * 0.2f, sw, 44 * k), "SUDDEN DEATH", st);
                var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt((n > 0 ? 170f : 200f) * pop * k) };
                big.normal.textColor = n == 2 ? new Color(1f, 1f, 1f, 0.95f) : n == 1 ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1f, 0.3f, 0.15f, 1f - t);
                GUI.Label(new Rect(0, sh * 0.24f, sw, 300 * k), word, big);
                return;
            }
            m_LastCount = -1;
        }

        void DrawScope()
        {
            float sw = Screen.width, sh = Screen.height;
            EnsureWheelTextures(PlayerController.WheelOptions.Length);
            float d = sh * 0.92f;
            var r = new Rect((sw - d) / 2, (sh - d) / 2, d, d);
            // black everywhere except the round lens, with a thin crosshair
            Fill(new Rect(0, 0, r.x + 1, sh), Color.black);
            Fill(new Rect(r.xMax - 1, 0, sw - r.xMax + 1, sh), Color.black);
            if (s_Scope == null)
            {
                const int S = 256;
                s_Scope = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float a = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.97f) * 60f);
                    px[y * S + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
                s_Scope.SetPixels32(px);
                s_Scope.Apply();
            }
            GUI.DrawTexture(r, s_Scope);
            Fill(new Rect(sw / 2 - 0.5f, r.y, 1, d), new Color(0, 0, 0, 0.8f));
            Fill(new Rect(r.x, sh / 2 - 0.5f, d, 1), new Color(0, 0, 0, 0.8f));
            Fill(new Rect(sw / 2 - 2, sh / 2 - 2, 4, 4), new Color(1f, 0.2f, 0.1f));
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
            if (Cfg.Builder)
            {
                int own = b.IsCarried ? -1 : b.SocketTeam.Value;
                if (own >= 0)
                    return own == myTeam ? "<color=#77ff77>The ball is planted for YOUR team - don't let anyone pick it up!</color>" : $"<color=#ff7777>The ball is planted for {Cfg.TeamLabel(own)} - pick it up and plant it for your team!</color>";
                if (b.IsCarried && b.Carrier != null && b.Carrier.IsOwner) return "<color=#ffdd55>You have the ball - LMB to plant it anywhere (then it's your team's)</color>";
                if (b.IsCarried && b.Carrier != null) return $"<color=#ff7777>{Cfg.TeamLabel(b.Carrier.Team.Value)} has the ball!</color>";
                return "The ball is loose - pick it up and plant it for your team!";
            }
            if (b.IsCarried)
            {
                var c = b.Carrier;
                if (c == null) return "Ball is being carried";
                return c.IsOwner ? "<color=#ffdd55>You have the ball - throw it (LMB) into your machine's socket!</color>" : $"<color=#ff7777>{Cfg.TeamName[c.Team.Value]} has the ball!</color>";
            }
            int sock = b.SocketTeam.Value;
            if (sock >= 0)
                return sock == myTeam ? "<color=#77ff77>The ball is in YOUR machine - defend it!</color>" : $"<color=#ff7777>The ball is in the {Cfg.TeamName[sock]} machine - raid them and take it!</color>";
            int t = b.BaseTeam.Value;
            if (t < 0) return "The ball is loose - put it in your machine!";
            return t == myTeam ? "<color=#ffdd55>The ball is in your base but NOT in the machine - it only counts in the socket!</color>" : $"<color=#ff7777>The ball is in the {Cfg.TeamName[t]} base (not in their machine yet)</color>";
        }

        /// <summary>
        /// Builder: small crafting timers in the top right, stacked. The top one is being made (its bar runs down); the
        /// ones queued under it wait with a full bar, and move up and start running down when their turn comes.
        /// Returns how much height it used.
        /// </summary>
        float DrawCraftStack(PlayerNet me, float k, float top)
        {
            if (me.CraftingItem.Value == 0 || me.CraftDoneAt.Value < 0) return 0f;
            float sw = Screen.width, w = 190 * k, h = 22 * k, x = sw - w - 10;
            double now = me.NetworkManager.ServerTime.Time;
            float y = top;
            void Row(Item id, float left, float total)
            {
                Fill(new Rect(x, y, w, h), new Color(0, 0, 0, 0.5f));
                Fill(new Rect(x, y + h - 4 * k, w * Mathf.Clamp01(left / Mathf.Max(0.01f, total)), 4 * k), new Color(1f, 0.82f, 0.3f));
                var icon = ItemIcons.Get(id);
                if (icon != null) GUI.DrawTexture(new Rect(x + 2, y + 1, h - 4 * k, h - 4 * k), icon, ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(x + h, y - 1, w - h - 44 * k, h), Cfg.ItemName(id), m_Small);
                GUI.Label(new Rect(x + w - 44 * k, y - 1, 40 * k, h), $"{left:0.0}s", new GUIStyle(m_Small) { alignment = TextAnchor.UpperRight });
                y += h + 3 * k;
            }
            float total0 = Mathf.Max(0.01f, (float)(me.CraftDoneAt.Value - me.CraftStartAt.Value));
            Row((Item)me.CraftingItem.Value, Mathf.Max(0f, (float)(me.CraftDoneAt.Value - now)), total0);
            for (int i = 0; i < me.CraftQueue.Count; i++)
            {
                var id = (Item)me.CraftQueue[i];
                float t = Cfg.CraftSecondsOf(id);
                Row(id, t, t);
            }
            return y - top + 4 * k;
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

        /// <summary>Test hooks: when the victory / game over screen was last drawn (Time.time; -1 never), and when the cutscene's bars were.</summary>
        public static float GameOverShownAt { get; private set; } = -1f;
        public static float CutsceneShownAt { get; private set; } = -1f;
        /// <summary>Test hook: the last victory screen drawn was the winners' all-black one.</summary>
        public static bool WinnerScreenBlack { get; private set; }

        /// <summary>The victory cutscene's HUD: black letterbox bars sliding in, the winners' name and why they won in the
        /// bottom bar, and a fade to black at the very end (then the victory screen).</summary>
        void DrawVictoryCutscene(NetGame game, int myTeam)
        {
            CutsceneShownAt = Time.time;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            float e = VictoryCutscene.Elapsed;
            float bars = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.6f)) * sh * 0.12f;
            Fill(new Rect(0, 0, sw, bars), Color.black);
            Fill(new Rect(0, sh - bars, sw, bars), Color.black);
            int w = game.Winner.Value;
            float a = Mathf.Clamp01((e - 0.8f) / 0.6f) * Mathf.Clamp01((VictoryCutscene.Gone - e) / 0.5f);
            if (w >= 0 && a > 0f)
            {
                var tc = Cfg.TeamColor[Mathf.Clamp(w, 0, 3)];
                string col = ColorUtility.ToHtmlStringRGB(Color.Lerp(tc, Color.white, 0.25f));
                string who = w == myTeam ? "YOUR TEAM WINS!" : $"{Cfg.TeamLabel(w).ToUpper()} WINS!";
                var big = new GUIStyle(m_Big);
                big.normal.textColor = new Color(1, 1, 1, a);
                GUI.Label(new Rect(0, sh - bars + 4 * k, sw, 46 * k), $"<color=#{col}>{who}</color>", big);
                var sub = new GUIStyle(m_Center);
                sub.normal.textColor = new Color(0.85f, 0.85f, 0.85f, a);
                GUI.Label(new Rect(0, sh - bars + 50 * k, sw, 26 * k), game.EndReason.Value.ToString(), sub);
            }
            // fade to black as the ship leaves, so the victory screen comes up out of the dark
            float fade = Mathf.Clamp01((e - (VictoryCutscene.Length - 0.7f)) / 0.6f);
            if (fade > 0f) Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, fade));
        }

        void DrawGameOver(Bootstrap boot, NetGame game, int myTeam)
        {
            GameOverShownAt = Time.time;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            int w = game.Winner.Value;
            // the winners have escaped (the UFO took them): all black for them; everyone else still sees the world behind it
            bool won = w >= 0 && w == myTeam;
            WinnerScreenBlack = won;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, won ? 1f : 0.6f));
            string title = w < 0 ? "DRAW" : won ? "<color=#77ff77>VICTORY</color>" : "<color=#ff5555>DEFEAT</color>";
            Shadowed(new Rect(0, sh * 0.3f, sw, 70 * k), $"<size={Mathf.RoundToInt(64 * k)}>{title}</size>", m_Big);
            Shadowed(new Rect(0, sh * 0.3f + 80 * k, sw, 30 * k), game.EndReason.Value.ToString(), m_Center);
            if (won && game.CutsceneAt.Value >= 0) GUI.Label(new Rect(0, sh * 0.3f - 34 * k, sw, 28 * k), "<color=#9a9aaa>You escaped.</color>", m_Center);
            DrawLeaveButton(boot, new Rect(sw / 2 - 110 * k, sh * 0.3f + 140 * k, 220 * k, 46 * k));
        }
    }
}
