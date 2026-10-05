using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The spectator's HUD (Spectator.cs): none of the player's HUD, just a bar along the bottom - who you're watching in
    /// their team's colour, the match clock, the clicks (left: next player, right: previous) and LEAVE - plus the chat.
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
            DrawLeaveButton(boot, new Rect(sw / 2 - 110 * k, sh * 0.3f + 140 * k, 220 * k, 46 * k));
        }
    }
}
