using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The start and the end of a lobby game on the HUD:
    /// - The 3, 2, 1 countdown (NetGame.ReadySetRock.cs) after the match intro: a big number in the middle, each popping
    ///   in and settling. Before the countdown starts (this screen skipped its intro and the others are still watching theirs) a quiet line says so.
    /// - Straight back to the lobby after the victory cutscene (NetGame.StraightToLobby): no result screen and countdown,
    ///   just black and "BACK TO THE LOBBY..." while the host restarts the session (the cutscene already said who won).
    /// </summary>
    public partial class Hud
    {
        /// <summary>Test hooks: when the 3, 2, 1 countdown was last drawn (Time.unscaledTime) and its number then; when the plain
        /// back-to-the-lobby screen was last drawn.</summary>
        public static float RockShownAt { get; private set; } = -1f;
        public static string RockShownWord { get; private set; } = "";
        public static float StraightToLobbyShownAt { get; private set; } = -1f;

        /// <summary>The 3, 2, 1 countdown over the game (Hud.DrawGame, right after DrawCountdowns; Hud.DrawSpectator too).</summary>
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
            float pop = 1f + 0.35f * Mathf.Clamp01(1f - t * 4f);
            var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(170f * pop * k), clipping = TextClipping.Overflow };
            big.normal.textColor = new Color(1f, 1f, 1f, 0.95f * (1f - 0.5f * CountEase(t * 1.4f - 0.4f)));
            Shadowed(new Rect(0, sh * 0.24f, sw, 300 * k), word, big);
        }

        /// <summary>The game's over and it's going straight back to the lobby (NetGame.StraightToLobby): a plain black screen
        /// instead of the result (Hud.DrawGameOver / DrawSpectatorGameOver draw this and stop).</summary>
        static float CountEase(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

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
