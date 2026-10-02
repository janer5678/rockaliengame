using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Menu search boxes (CHANGE VALUES and the pause menu's Dev settings): rows are filtered live as you type (every
    /// word typed has to be in the row's name or its section; case doesn't matter), with an x to clear it. While a menu
    /// text box has the keyboard (a search, a hex code) the game's keys are muted (Binds.Muted), and Esc / a click
    /// anywhere else lets go of it first.
    /// </summary>
    public partial class Hud
    {
        int m_TypingFrame = -10;
        bool m_DropFocus;
        GUIStyle m_SearchField, m_SearchHint;
        float m_SearchStyleK;
        static string s_FocusSearch;

        /// <summary>A menu text box has the keyboard (as of the last frame drawn): the game's keys do nothing.</summary>
        public static bool Typing => s_I != null && Time.frameCount - s_I.m_TypingFrame <= 1;

        /// <summary>Esc while typing: let go of the text box (instead of leaving the page). True if it did.</summary>
        static bool DropTyping()
        {
            if (!Typing) return false;
            s_I.m_DropFocus = true;
            s_I.m_TypingFrame = -10;
            return true;
        }

        /// <summary>(tests) give a search box the keyboard, as if it had been clicked: "search values" or "search dev".</summary>
        public static void FocusSearch(string controlName) => s_FocusSearch = controlName;

        /// <summary>Start of OnGUI: a pending "let go of the keyboard".</summary>
        void SearchFrame()
        {
            if (!m_DropFocus) return;
            m_DropFocus = false;
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>Right after drawing a named text field: notes whether it has the keyboard.</summary>
        void NoteTyping(string controlName)
        {
            if (GUI.GetNameOfFocusedControl() == controlName) m_TypingFrame = Time.frameCount;
        }

        /// <summary>"Search [ typed text ] [x]" on one row. Returns the text (the caller filters with it).</summary>
        string SearchBox(string controlName, string query, string hint)
        {
            float k = m_Scale;
            if (m_SearchField == null || !Mathf.Approximately(m_SearchStyleK, k))
            {
                m_SearchStyleK = k;
                m_SearchField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(16 * k), alignment = TextAnchor.MiddleLeft, padding = new RectOffset(Mathf.RoundToInt(10 * k), 6, 2, 2) };
                m_SearchHint = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            }
            query = query ?? "";
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Search</b>", m_Label, GUILayout.Width(76 * k), GUILayout.Height(32 * k));
            GUI.SetNextControlName(controlName);
            string q = GUILayout.TextField(query, 48, m_SearchField, GUILayout.Height(32 * k), GUILayout.ExpandWidth(true));
            var fr = GUILayoutUtility.GetLastRect();
            TrackHover(fr);
            if (s_FocusSearch == controlName) { GUI.FocusControl(controlName); s_FocusSearch = null; }
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            if (focused) m_TypingFrame = Time.frameCount;
            if (q.Length == 0 && !focused && Event.current.type == EventType.Repaint)
                GUI.Label(new Rect(fr.x + 11 * k, fr.y, fr.width - 14 * k, fr.height), $"<color=#8c8c8c>{hint}</color>", m_SearchHint);
            bool had = q.Length > 0;
            GUI.enabled = had;
            if (Btn("x", GUILayout.Width(40 * k), GUILayout.Height(32 * k))) { q = ""; GUIUtility.keyboardControl = 0; }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            // a click anywhere else lets go of the keyboard (so the game's keys work again)
            var e = Event.current;
            if (focused && e.type == EventType.MouseDown && !fr.Contains(e.mousePosition)) GUIUtility.keyboardControl = 0;
            return q;
        }

        static readonly char[] s_Space = { ' ' };

        /// <summary>Every word typed is somewhere in the text (any case; spaces in the text don't matter).</summary>
        static bool Matches(string query, string text)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string t = text.ToLowerInvariant(), tight = t.Replace(" ", "");
            foreach (var w in query.ToLowerInvariant().Split(s_Space, System.StringSplitOptions.RemoveEmptyEntries))
                if (!t.Contains(w) && !tight.Contains(w)) return false;
            return true;
        }

        /// <summary>The label with the (longest) word typed picked out in gold.</summary>
        static string Highlight(string label, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return label;
            string best = null;
            foreach (var w in query.Split(s_Space, System.StringSplitOptions.RemoveEmptyEntries))
                if (best == null || w.Length > best.Length) best = w;
            int i = label.IndexOf(best, System.StringComparison.OrdinalIgnoreCase);
            if (i < 0) return label;
            return label.Substring(0, i) + "<color=#ffd24a><b>" + label.Substring(i, best.Length) + "</b></color>" + label.Substring(i + best.Length);
        }

        // ------------------------------------------------------------------ CHANGE VALUES and Dev settings searches

        string m_ValuesQuery = "", m_ValuesQueryWas = "", m_DevQuery = "";
        /// <summary>(tests) put this in the CHANGE VALUES / Dev settings search box (null = leave it).</summary>
        public static string SetValuesSearch, SetDevSearch;
        /// <summary>(tests) how many rows the last search showed (-1 = no search).</summary>
        public static int ValuesMatches = -1, DevMatches = -1;
    }
}
