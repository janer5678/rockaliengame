using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Your name: the first-launch screen that asks for it (saved to PlayerPrefs; you can always change it in Settings >
    /// Name), and that Settings tab. The name is fixed for a match: during one the tab only shows it (the server got it
    /// when you spawned: PlayerNet.Identity.cs), so it's changed from the menus between games.
    /// </summary>
    public partial class Hud
    {
        const string NameSetKey = "RockGame.NameChosen";

        /// <summary>Has this PC been through the first-launch name screen (or had a name before it existed)?</summary>
        public static bool NameChosen => PlayerPrefs.GetInt(NameSetKey, 0) == 1 || GameSettings.PlayerName.Length > 0;

        static void MarkNameChosen() { PlayerPrefs.SetInt(NameSetKey, 1); PlayerPrefs.Save(); }

        /// <summary>The first time the game is started: a screen asking for your name (it can't be skipped empty).</summary>
        void DrawNameScreen()
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            MouseOverUI = true;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.55f));
            float w = Mathf.Min(sw - 40, 560 * k), h = 300 * k;
            var r = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            Fill(r, new Color(0.04f, 0.03f, 0.08f, 0.94f));
            GUILayout.BeginArea(new Rect(r.x + 28 * k, r.y + 22 * k, r.width - 56 * k, r.height - 44 * k));
            GUILayout.Label($"<b><size={Mathf.RoundToInt(34 * k)}>WHAT'S YOUR NAME?</size></b>", m_Center);
            GUILayout.Space(14 * k);
            if (m_NameEdit == null) m_NameEdit = GameSettings.PlayerName.Length > 0 ? GameSettings.PlayerName : GameSettings.CleanName(SteamBoot.MyName); // (your Steam name to start with)
            GUI.SetNextControlName("first name");
            string typed = GUILayout.TextField(m_NameEdit, GameSettings.PlayerNameMax, m_Field, GUILayout.Height(44 * k));
            if (typed != m_NameEdit) m_NameEdit = typed.Replace("<", "").Replace(">", "");
            if (Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "first name") GUI.FocusControl("first name");
            GUILayout.Space(8 * k);
            GUILayout.Label("<color=#bbbbbb>You can always change your name via the options menu.</color>", m_Center);
            GUILayout.FlexibleSpace();
            string clean = GameSettings.CleanName(m_NameEdit ?? "");
            GUI.enabled = clean.Length > 0;
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Btn("CONTINUE", m_Primary, GUILayout.Height(46 * k)) || (enter && clean.Length > 0))
            {
                GameSettings.PlayerName = clean;
                m_NameEdit = clean;
                MarkNameChosen();
                ClickSound();
            }
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        /// <summary>Settings > Name: change your name (not during a game: then it only says what it is).</summary>
        void DrawNameTab()
        {
            float k = m_Scale;
            Caption("YOUR NAME");
            bool inGame = Bootstrap.I != null && Bootstrap.I.InSession;
            if (m_NameEdit == null) m_NameEdit = GameSettings.PlayerName;
            GUILayout.BeginHorizontal();
            RowLabel("Name");
            GUI.enabled = !inGame;
            string typed = GUILayout.TextField(inGame ? GameSettings.PlayerName : m_NameEdit, GameSettings.PlayerNameMax, m_Field, GUILayout.Width(320 * k), GUILayout.Height(32 * k));
            if (!inGame && typed != m_NameEdit)
            {
                m_NameEdit = typed.Replace("<", "").Replace(">", "");
                GameSettings.PlayerName = m_NameEdit;
                if (GameSettings.PlayerName.Length > 0) MarkNameChosen();
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(inGame
                ? "<color=#ffcc66>You can't change your name during a game - change it from the main menu's OPTIONS.</color>"
                : "<color=#bbbbbb>What everyone sees on the scoreboard, in the kill feed, the chat and over your head in the lobby.</color>", m_SmallWrap);
        }
    }
}
