using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The start and the end of a lobby game on the HUD:
    /// - READY / SET / ROCK! (NetGame.ReadySetRock.cs) after the match intro: the sudden death countdown's look - each
    ///   word punched in big and settling over a dimmed screen, ROCK! fading away as everyone's let go. Before the
    ///   countdown starts (this screen skipped its intro and the others are still watching theirs) a quiet line says so.
    /// - Straight back to the lobby after the victory cutscene (NetGame.StraightToLobby): no result screen and countdown,
    ///   just black and "BACK TO THE LOBBY..." while the host restarts the session (the cutscene already said who won).
    /// </summary>
    public partial class Hud
    {
        /// <summary>Test hooks: when READY / SET / ROCK! was last drawn (Time.unscaledTime) and its word then; when the plain
        /// back-to-the-lobby screen was last drawn.</summary>
        public static float RockShownAt { get; private set; } = -1f;
        public static string RockShownWord { get; private set; } = "";
        public static float StraightToLobbyShownAt { get; private set; } = -1f;

        /// <summary>READY / SET / ROCK! over the game (Hud.DrawGame, right after DrawCountdowns; Hud.DrawSpectator too).</summary>
        void DrawReadySetRock(NetGame game, bool dead)
        {
            if (game == null || dead || MatchIntro.Active) return;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            string word = NetGame.RockWord(game, out float t);
            if (word == "")
            {
                // (this screen's intro is over but someone's is still playing: held still until theirs is)
                if (game.RockHeld && !game.RockCounting)
                {
                    var wst = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(20 * k), fontStyle = FontStyle.Bold };
                    Shadowed(new Rect(0, sh * 0.3f, sw, 30 * k), "<color=#ffd75a>Waiting for everyone to finish the intro...</color>", wst);
                }
                return;
            }
            RockShownAt = Time.unscaledTime;
            RockShownWord = word;
            int n = word == "READY" ? 2 : word == "SET" ? 1 : 0;
            float pop = 1f + 0.35f * Mathf.Clamp01(1f - t * 4f);
            if (n > 0) Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.25f));
            var cap = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * k) };
            cap.normal.textColor = new Color(1f, 0.9f, 0.4f, n > 0 ? 1f : 1f - t);
            GUI.Label(new Rect(0, sh * 0.2f, sw, 44 * k), MatchIntro.MapName, cap);
            var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt((n > 0 ? 170f : 200f) * pop * k), clipping = TextClipping.Overflow };
            big.normal.textColor = n == 2 ? new Color(1f, 1f, 1f, 0.95f) : n == 1 ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1f, 0.3f, 0.15f, 1f - t);
            GUI.Label(new Rect(0, sh * 0.24f, sw, 300 * k), word, big);
        }

        /// <summary>The game's over and it's going straight back to the lobby (NetGame.StraightToLobby): a plain black screen
        /// instead of the result (Hud.DrawGameOver / DrawSpectatorGameOver draw this and stop).</summary>
        void DrawStraightToLobby()
        {
            StraightToLobbyShownAt = Time.unscaledTime;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            Fill(new Rect(0, 0, sw, sh), Color.black);
            var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * k) };
            float dots = Mathf.Repeat(Time.unscaledTime * 2f, 4f);
            Shadowed(new Rect(0, sh * 0.46f, sw, 44 * k), "BACK TO THE LOBBY" + new string('.', Mathf.FloorToInt(dots)), st);
        }
    }
}
