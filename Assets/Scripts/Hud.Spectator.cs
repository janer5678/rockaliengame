using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The spectator's HUD (Spectator.cs): none of the player's HUD, just a bar along the bottom - who you're watching in
    /// their team's colour, the match clock, the clicks (left: next player, right: previous) and LEAVE - plus the chat,
    /// and above the bar what the watched player sees of their own things: their hotbar, and (read-only) their bag, the
    /// chest they're in or their upgrade station when they have it open (PlayerNet.Watch.cs).
    /// The victory cutscene and the end screen are the same ones the players get (seen as a neutral: "BLUE WINS!").
    /// </summary>
    public partial class Hud
    {
        /// <summary>Test hook: when the spectator bar was last drawn (Time.time; -1 never) and what it said.</summary>
        public static float SpectatorBarShownAt { get; private set; } = -1f;
        public static string SpectatorBarText { get; private set; } = "";

        void DrawSpectator(Bootstrap boot)
        {
            var game = NetGame.Instance;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            if (game != null && VictoryCutscene.Active) { DrawVictoryCutscene(game, -1); return; }
            if (game != null && game.S == GameState.GameOver) { DrawSpectatorGameOver(boot, game); return; }

            Chat.Draw(k, m_Small, Fill, Shadowed);
            var t = Spectator.Target;
            string who = t != null ? t.DisplayName : "nobody yet";
            string clock = "";
            if (game != null)
                clock = game.S == GameState.Waiting ? "Waiting for players"
                    : game.S == GameState.PreBall ? (Cfg.Tutorial ? "Tutorial" : "Wall drops in " + Clock(game.TimeLeft))
                    : game.S == GameState.BallLive ? "Time left " + Clock(game.TimeLeft)
                    : game.S == GameState.SuddenDeath ? "SUDDEN DEATH " + Clock(game.TimeLeft) : "";

            float w = Mathf.Min(760 * k, sw - 32), h = 58 * k;
            var bar = new Rect((sw - w) / 2, sh - h - 16 * k, w, h);
            Fill(bar, new Color(0.03f, 0.035f, 0.05f, 0.8f));
            Fill(new Rect(bar.x, bar.y, bar.width, 3 * k), GameSettings.AccentColor);
            var col = t != null ? PlayerNet.NameColor(t.Team.Value) : Color.white;
            if (t != null) Fill(new Rect(bar.x, bar.y + 3 * k, 5 * k, bar.height - 3 * k), col);
            string name = $"<color=#{ColorUtility.ToHtmlStringRGB(col)}>{who}</color>" + (t != null && t.Dead.Value ? "  <color=#ff7777>(dead)</color>" : "");
            SpectatorBarText = $"SPECTATING {who}";
            SpectatorBarShownAt = Time.time;
            Shadowed(new Rect(bar.x + 16 * k, bar.y + 6 * k, w, 28 * k), $"<b>SPECTATING</b>  <b><size={Mathf.RoundToInt(20 * k)}>{name}</size></b>", m_Label);
            Shadowed(new Rect(bar.x + 16 * k, bar.y + 32 * k, w, 22 * k), $"<color=#bbbbbb>Left click: next player   ·   Right click: previous</color>" + (clock != "" ? $"   ·   {clock}" : ""), m_Small);
            var lr = new Rect(bar.xMax - 104 * k, bar.y + 12 * k, 92 * k, 34 * k);
            if (lr.Contains(Event.current.mousePosition) || bar.Contains(Event.current.mousePosition)) MouseOverUI = true;
            if (BtnAt(lr, "LEAVE", m_Button)) boot.Leave();
            // what they see of their own things: their hotbar (what's in their hand picked out) and, when they have it open,
            // their bag, the chest they're in or their upgrade station - all just to look at. Not while they're dead.
            if (t != null && t.IsSpawned && !t.Dead.Value) DrawWatchedHud(t, bar.y - 10 * k, k);
        }

        /// <summary>Test hooks: when the watched player's hotbar was last drawn (Time.time; -1 never), and what was open.</summary>
        public static float WatchedHotbarShownAt { get; private set; } = -1f;
        public static string WatchedPanel { get; private set; } = "";

        /// <summary>The watched player's hotbar along the bottom (above the spectator bar), and their open screen above it.</summary>
        void DrawWatchedHud(PlayerNet t, float bottom, float k)
        {
            float sw = Screen.width, cx = sw / 2f;
            int n = Cfg.HotbarSize;
            float slot = Mathf.Min(70 * k, (sw - 40) / (n + 1)), gap = 6 * k;
            float hx = cx - (n * slot + (n - 1) * gap) / 2f, hy = bottom - slot;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(hx + i * (slot + gap), hy, slot, slot);
                bool sel = t.HeldSlot.Value == i && !t.CarryingBall;
                DrawSlotVisual(r, t.SlotAt(i), sel, t);
                if (sel && t.SlotAt(i).Empty) DrawRockGhost(r);
                Shadowed(new Rect(r.x + 5 * k, r.y + 2, 34 * k, 22 * k), (i + 1).ToString(), m_SlotKey);
            }
            WatchedHotbarShownAt = Time.time;
            var open = (WatchUi)t.WatchOpen.Value;
            WatchedPanel = open == WatchUi.None ? "" : open.ToString();
            if (open == WatchUi.None) return;

            // their open screen, above the hotbar: the bag (3 rows of 6) on the left, the chest or the upgrades on the right
            const int cols = Cfg.HotbarSize;
            float s2 = Mathf.Min(58 * k, (sw - 80) / 13f), g2 = 5 * k;
            float gridW = cols * s2 + (cols - 1) * g2;
            int rows = (Cfg.MainSize + cols - 1) / cols;
            var box = open == WatchUi.Container ? t.WatchContainer : null;
            bool right = open == WatchUi.Upgrades || box != null;
            float rightW = open == WatchUi.Upgrades ? 360 * k : gridW;
            float total = gridW + (right ? 30 * k + rightW : 0f);
            float px = cx - total / 2f, top = hy - 24 * k - rows * (s2 + g2) - 34 * k;
            int boxRows = box != null ? Mathf.Max(1, (box.Slots.Count + cols - 1) / cols) : 0;
            top = Mathf.Min(top, hy - 24 * k - Mathf.Max(rows, boxRows) * (s2 + g2) - 34 * k);
            var panel = new Rect(px - 14 * k, top - 10 * k, total + 28 * k, hy - 14 * k - top + 10 * k);
            Fill(panel, new Color(0.02f, 0.025f, 0.035f, 0.78f));
            Fill(new Rect(panel.x, panel.y, panel.width, 2 * k), GameSettings.AccentColor);
            float y0 = top + 26 * k;
            Shadowed(new Rect(px, top, gridW, 24 * k), $"<b>{t.DisplayName.ToUpper()}'S BAG</b>", m_Small);
            for (int i = 0; i < Cfg.MainSize; i++)
                DrawSlotVisual(new Rect(px + (i % cols) * (s2 + g2), y0 + (i / cols) * (s2 + g2), s2, s2), t.SlotAt(Cfg.HotbarSize + i), false, t);
            float rx = px + gridW + 30 * k;
            if (box != null)
            {
                Shadowed(new Rect(rx, top, rightW, 24 * k), $"<b>{box.DisplayName.ToUpper()}</b>", m_Small);
                for (int i = 0; i < box.Slots.Count; i++)
                    DrawSlotVisual(new Rect(rx + (i % cols) * (s2 + g2), y0 + (i / cols) * (s2 + g2), s2, s2), box.Slots[i], false, t);
            }
            else if (open == WatchUi.Upgrades)
            {
                // the upgrade station: each upgrade with the level their team has bought
                int team = t.Team.Value;
                Shadowed(new Rect(rx, top, rightW, 24 * k), "<b>UPGRADE STATION</b>", m_Small);
                Cfg.BaseUpgrades(m_UpgradeTmp);
                float ry = y0;
                foreach (var id in m_UpgradeTmp)
                {
                    var rr = new Rect(rx, ry, rightW, 52 * k);
                    Fill(rr, new Color(1f, 1f, 1f, 0.06f));
                    Fill(new Rect(rr.x, rr.y, 3 * k, rr.height), k_UpEdge);
                    var icon = ItemIcons.GetForTeam(id, team);
                    if (icon != null) GUI.DrawTexture(new Rect(rr.x + 8 * k, rr.y + 4 * k, 44 * k, 44 * k), icon, ScaleMode.ScaleToFit, true);
                    int lvl = Cfg.BaseUpgradeLevel(id, team), max = Cfg.BaseUpgradeMax(id);
                    Shadowed(new Rect(rr.x + 60 * k, rr.y + 4 * k, rightW - 64 * k, 22 * k), $"<b>{Cfg.ItemName(id)}</b>  <color=#d8d8d0>LV {lvl}/{max}</color>", m_Small);
                    for (int i = 0; i < max; i++)
                    {
                        var pr = new Rect(rr.x + 60 * k + i * 17 * k, rr.y + 30 * k, 13 * k, 13 * k);
                        Fill(pr, new Color(0, 0, 0, 0.5f));
                        if (i < lvl) Fill(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), k_UpEdge);
                    }
                    ry += 58 * k;
                }
            }
        }

        /// <summary>The end of the match for a spectator: who won and why, and back to the menu.</summary>
        void DrawSpectatorGameOver(Bootstrap boot, NetGame game)
        {
            GameOverShownAt = Time.time;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            int w = game.Winner.Value;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.6f));
            string title = w < 0 ? "DRAW" : $"<color=#{ColorUtility.ToHtmlStringRGB(PlayerNet.NameColor(w))}>{Cfg.TeamLabel(w).ToUpper()} WINS</color>";
            Shadowed(new Rect(0, sh * 0.3f, sw, 70 * k), $"<size={Mathf.RoundToInt(64 * k)}>{title}</size>", m_Big);
            Shadowed(new Rect(0, sh * 0.3f + 80 * k, sw, 30 * k), game.EndReason.Value.ToString(), m_Center);
            if (game == null || !game.GoingBackToLobby) DrawLeaveButton(boot, new Rect(sw / 2 - 110 * k, sh * 0.3f + 140 * k, 220 * k, 46 * k));
        }
    }
}
