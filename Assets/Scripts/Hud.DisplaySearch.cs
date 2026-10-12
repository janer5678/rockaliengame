using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using U = UnityEngine.GUILayout;

namespace RockGame
{
    /// <summary>
    /// Settings > Display's search box. While something is typed, every category is drawn one after the other (each under
    /// its name) and only the rows that match are shown, under their section headers; clearing it brings the tabs back.
    ///
    /// It works for every row without the rows knowing: inside Hud, "GUILayout" is the small pass-through class below
    /// (the same calls as Unity's). While the search results are being drawn it watches the column's top-level rows - a
    /// Begin/End group, or a lone label / button / slider - and reads the words on them. A row that didn't match (as of
    /// the frame before, so a frame's layout and repaint passes agree) is drawn into an area far off the screen instead,
    /// so it takes no room and can't be seen or clicked. Section headers: Caption (level 1), FoldRow (2), SubHead (3) -
    /// a header that matches shows everything under it, and a matching row shows the headers it's under. Grey hint
    /// lines ("&lt;color=#bbbbbb&gt;...") don't count as words to match. Outside the search it just calls Unity's.
    /// </summary>
    public partial class Hud
    {
        string m_DisplayQuery = "";
        /// <summary>(tests) put this in Settings > Display's search box (null = leave it).</summary>
        public static string SetDisplaySearch;
        /// <summary>(tests) how many rows (not headers) the display search showed last frame (-1 = no search).</summary>
        public static int DisplaySearchMatches = -1;

        sealed class SearchRow { public int Level; public readonly StringBuilder Text = new StringBuilder(); }
        static bool s_SearchOn, s_SearchHidden;
        static int s_SearchDepth, s_SearchHeaderNext = -1, s_SearchFrame = -1;
        static float s_SearchGap, s_SearchWidth;
        static List<SearchRow> s_SearchCur = new List<SearchRow>(), s_SearchPrev = new List<SearchRow>();
        static bool[] s_SearchVis = new bool[0];
        static readonly Regex s_Tags = new Regex("<[^>]*>");

        /// <summary>The search box over the Display tabs. True while something is typed (the results are drawn instead).</summary>
        bool DisplaySearchBox()
        {
            if (SetDisplaySearch != null) { m_DisplayQuery = SetDisplaySearch; SetDisplaySearch = null; }
            string q = SearchBox("search display", m_DisplayQuery, "find a setting in any tab");
            if (q != m_DisplayQuery) { m_DisplayQuery = q; m_SettingsScroll = Vector2.zero; }
            bool on = !string.IsNullOrWhiteSpace(m_DisplayQuery);
            if (!on) DisplaySearchMatches = -1;
            GUILayout.Space(4 * m_Scale);
            return on;
        }

        /// <summary>Every category's rows that match the search, each category under its name.</summary>
        void DrawDisplaySearch()
        {
            float k = m_Scale;
            if (Time.frameCount != s_SearchFrame)
            {
                // a new frame: what the last frame drew decides what shows in this one (the same for all its passes)
                s_SearchFrame = Time.frameCount;
                (s_SearchPrev, s_SearchCur) = (s_SearchCur, s_SearchPrev);
                SearchVisibility(m_DisplayQuery);
            }
            s_SearchCur.Clear();
            s_SearchGap = 10 * k;
            s_SearchWidth = 720 * k;
            s_SearchDepth = 0; s_SearchHeaderNext = -1; s_SearchHidden = false;
            s_SearchOn = true;
            try
            {
                for (int i = 0; i < DisplayCatNames.Length; i++)
                {
                    s_SearchHeaderNext = 0; // (the category's name: not searched, shown while anything in it is)
                    GUILayout.Label($"<color=#ffd24a><b>{DisplayCatNames[i].ToUpperInvariant()}</b></color>", m_Caption);
                    DrawDisplayCategory((DisplayCat)i);
                }
            }
            finally
            {
                if (s_SearchDepth != 0) Debug.LogWarning($"[Hud] display search: {s_SearchDepth} layout group(s) left open");
                if (s_SearchHidden) U.EndArea();
                s_SearchOn = false; s_SearchHidden = false; s_SearchDepth = 0; s_SearchHeaderNext = -1;
            }
            int shown = 0;
            for (int i = 0; i < s_SearchVis.Length && i < s_SearchPrev.Count; i++) if (s_SearchVis[i] && s_SearchPrev[i].Level < 0) shown++;
            DisplaySearchMatches = shown;
            if (shown == 0)
                GUILayout.Label($"<color=#bbbbbb>No setting matches \"{m_DisplayQuery.Trim()}\". Every word typed has to be in a setting's name or its section's.</color>", m_SmallWrap);
        }

        /// <summary>Which of last frame's rows show: the ones whose words, with their headers', have every word typed.</summary>
        static void SearchVisibility(string query)
        {
            var rows = s_SearchPrev;
            if (s_SearchVis.Length < rows.Count) s_SearchVis = new bool[rows.Count * 2];
            System.Array.Clear(s_SearchVis, 0, s_SearchVis.Length);
            var open = new List<int>();
            var ctx = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Level >= 0) while (open.Count > 0 && rows[open[open.Count - 1]].Level >= r.Level) open.RemoveAt(open.Count - 1);
                ctx.Clear();
                foreach (int h in open) ctx.Append(rows[h].Text).Append(' ');
                ctx.Append(r.Text);
                bool m = r.Level != 0 && ctx.Length > 0 && Matches(query, ctx.ToString());
                if (m) { s_SearchVis[i] = true; foreach (int h in open) s_SearchVis[h] = true; }
                if (r.Level >= 0) open.Add(i);
            }
        }

        /// <summary>The next top-level row is a section header (1 = Caption, 2 = a fold, 3 = SubHead).</summary>
        static void SearchHeader(int level)
        {
            if (s_SearchOn && s_SearchDepth == 0) s_SearchHeaderNext = level;
        }

        static void SearchRowBegin()
        {
            int i = s_SearchCur.Count;
            var r = new SearchRow { Level = s_SearchHeaderNext };
            s_SearchCur.Add(r);
            s_SearchHeaderNext = -1;
            bool show = i < s_SearchVis.Length && i < s_SearchPrev.Count && s_SearchVis[i];
            s_SearchHidden = !show;
            if (show && r.Level == 0 && i > 0) U.Space(s_SearchGap * 2f);
            else if (show && r.Level == 1) U.Space(s_SearchGap);
            if (s_SearchHidden) U.BeginArea(new Rect(-100000f, 0f, s_SearchWidth, 4000f));
        }

        static void SearchRowEnd()
        {
            if (s_SearchHidden) U.EndArea();
            s_SearchHidden = false;
        }

        /// <summary>Words drawn on the row being drawn (not grey hints, not the category names).</summary>
        static void SearchNote(string text)
        {
            if (!s_SearchOn || s_SearchCur.Count == 0 || string.IsNullOrEmpty(text) || text.StartsWith("<color=#bbbbbb>")) return;
            var r = s_SearchCur[s_SearchCur.Count - 1];
            if (r.Level == 0) return;
            r.Text.Append(s_Tags.Replace(text, "")).Append(' ');
        }

        static bool SearchTop => s_SearchOn && s_SearchDepth == 0;
        static void SearchGroupBegin() { if (!s_SearchOn) return; if (s_SearchDepth == 0) SearchRowBegin(); s_SearchDepth++; }
        static void SearchGroupEnd() { if (!s_SearchOn || s_SearchDepth == 0) return; if (--s_SearchDepth == 0) SearchRowEnd(); }
        /// <summary>A lone widget: a row of its own at the top level.</summary>
        static void SearchItemBegin(string text) { if (SearchTop) { SearchRowBegin(); SearchNote(text); s_SearchDepth = -1; } else SearchNote(text); }
        static void SearchItemEnd() { if (s_SearchOn && s_SearchDepth == -1) { s_SearchDepth = 0; SearchRowEnd(); } }

        /// <summary>
        /// Unity's GUILayout, passed straight through (inside Hud this is what "GUILayout" means) - except while the display
        /// search draws its results, when it notes each top-level row and its words and moves the hidden ones off the
        /// screen. Missing a call? Add it here the same way.
        /// </summary>
        static class GUILayout
        {
            // ---- groups ----
            public static void BeginHorizontal(params GUILayoutOption[] o) { SearchGroupBegin(); U.BeginHorizontal(o); }
            public static void BeginHorizontal(GUIStyle s, params GUILayoutOption[] o) { SearchGroupBegin(); U.BeginHorizontal(s, o); }
            public static void BeginHorizontal(string t, GUIStyle s, params GUILayoutOption[] o) { SearchGroupBegin(); SearchNote(t); U.BeginHorizontal(t, s, o); }
            public static void BeginHorizontal(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { SearchGroupBegin(); U.BeginHorizontal(c, s, o); }
            public static void EndHorizontal() { U.EndHorizontal(); SearchGroupEnd(); }
            public static void BeginVertical(params GUILayoutOption[] o) { SearchGroupBegin(); U.BeginVertical(o); }
            public static void BeginVertical(GUIStyle s, params GUILayoutOption[] o) { SearchGroupBegin(); U.BeginVertical(s, o); }
            public static void BeginVertical(string t, GUIStyle s, params GUILayoutOption[] o) { SearchGroupBegin(); SearchNote(t); U.BeginVertical(t, s, o); }
            public static void BeginVertical(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { SearchGroupBegin(); U.BeginVertical(c, s, o); }
            public static void EndVertical() { U.EndVertical(); SearchGroupEnd(); }
            public static Vector2 BeginScrollView(Vector2 p, params GUILayoutOption[] o) { SearchGroupBegin(); return U.BeginScrollView(p, o); }
            public static Vector2 BeginScrollView(Vector2 p, GUIStyle s, params GUILayoutOption[] o) { SearchGroupBegin(); return U.BeginScrollView(p, s, o); }
            public static Vector2 BeginScrollView(Vector2 p, GUIStyle h, GUIStyle v, params GUILayoutOption[] o) { SearchGroupBegin(); return U.BeginScrollView(p, h, v, o); }
            public static Vector2 BeginScrollView(Vector2 p, bool ah, bool av, params GUILayoutOption[] o) { SearchGroupBegin(); return U.BeginScrollView(p, ah, av, o); }
            public static Vector2 BeginScrollView(Vector2 p, bool ah, bool av, GUIStyle h, GUIStyle v, params GUILayoutOption[] o) { SearchGroupBegin(); return U.BeginScrollView(p, ah, av, h, v, o); }
            public static Vector2 BeginScrollView(Vector2 p, bool ah, bool av, GUIStyle h, GUIStyle v, GUIStyle bg, params GUILayoutOption[] o) { SearchGroupBegin(); return U.BeginScrollView(p, ah, av, h, v, bg, o); }
            public static void EndScrollView() { U.EndScrollView(); SearchGroupEnd(); }
            public static void BeginArea(Rect r) { SearchGroupBegin(); U.BeginArea(r); }
            public static void BeginArea(Rect r, string t) { SearchGroupBegin(); U.BeginArea(r, t); }
            public static void BeginArea(Rect r, GUIStyle s) { SearchGroupBegin(); U.BeginArea(r, s); }
            public static void BeginArea(Rect r, GUIContent c) { SearchGroupBegin(); U.BeginArea(r, c); }
            public static void BeginArea(Rect r, Texture i) { SearchGroupBegin(); U.BeginArea(r, i); }
            public static void BeginArea(Rect r, string t, GUIStyle s) { SearchGroupBegin(); U.BeginArea(r, t, s); }
            public static void BeginArea(Rect r, GUIContent c, GUIStyle s) { SearchGroupBegin(); U.BeginArea(r, c, s); }
            public static void EndArea() { U.EndArea(); SearchGroupEnd(); }

            // ---- spacing (dropped between the search's rows: the hidden ones would leave gaps) ----
            public static void Space(float pixels) { if (!SearchTop) U.Space(pixels); }
            public static void FlexibleSpace() { if (!SearchTop) U.FlexibleSpace(); }

            // ---- widgets ----
            public static void Label(string t, params GUILayoutOption[] o) { SearchItemBegin(t); U.Label(t, o); SearchItemEnd(); }
            public static void Label(string t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(t); U.Label(t, s, o); SearchItemEnd(); }
            public static void Label(GUIContent c, params GUILayoutOption[] o) { SearchItemBegin(c?.text); U.Label(c, o); SearchItemEnd(); }
            public static void Label(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(c?.text); U.Label(c, s, o); SearchItemEnd(); }
            public static void Label(Texture i, params GUILayoutOption[] o) { SearchItemBegin(null); U.Label(i, o); SearchItemEnd(); }
            public static void Label(Texture i, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); U.Label(i, s, o); SearchItemEnd(); }
            public static void Box(string t, params GUILayoutOption[] o) { SearchItemBegin(t); U.Box(t, o); SearchItemEnd(); }
            public static void Box(string t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(t); U.Box(t, s, o); SearchItemEnd(); }
            public static void Box(GUIContent c, params GUILayoutOption[] o) { SearchItemBegin(c?.text); U.Box(c, o); SearchItemEnd(); }
            public static void Box(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(c?.text); U.Box(c, s, o); SearchItemEnd(); }
            public static void Box(Texture i, params GUILayoutOption[] o) { SearchItemBegin(null); U.Box(i, o); SearchItemEnd(); }
            public static void Box(Texture i, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); U.Box(i, s, o); SearchItemEnd(); }
            public static bool Button(string t, params GUILayoutOption[] o) { SearchItemBegin(t); bool r = U.Button(t, o); SearchItemEnd(); return r; }
            public static bool Button(string t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(t); bool r = U.Button(t, s, o); SearchItemEnd(); return r; }
            public static bool Button(GUIContent c, params GUILayoutOption[] o) { SearchItemBegin(c?.text); bool r = U.Button(c, o); SearchItemEnd(); return r; }
            public static bool Button(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(c?.text); bool r = U.Button(c, s, o); SearchItemEnd(); return r; }
            public static bool Button(Texture i, params GUILayoutOption[] o) { SearchItemBegin(null); bool r = U.Button(i, o); SearchItemEnd(); return r; }
            public static bool Button(Texture i, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); bool r = U.Button(i, s, o); SearchItemEnd(); return r; }
            public static bool RepeatButton(string t, params GUILayoutOption[] o) { SearchItemBegin(t); bool r = U.RepeatButton(t, o); SearchItemEnd(); return r; }
            public static bool RepeatButton(string t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(t); bool r = U.RepeatButton(t, s, o); SearchItemEnd(); return r; }
            public static bool Toggle(bool v, string t, params GUILayoutOption[] o) { SearchItemBegin(t); bool r = U.Toggle(v, t, o); SearchItemEnd(); return r; }
            public static bool Toggle(bool v, string t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(t); bool r = U.Toggle(v, t, s, o); SearchItemEnd(); return r; }
            public static bool Toggle(bool v, GUIContent c, params GUILayoutOption[] o) { SearchItemBegin(c?.text); bool r = U.Toggle(v, c, o); SearchItemEnd(); return r; }
            public static bool Toggle(bool v, GUIContent c, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(c?.text); bool r = U.Toggle(v, c, s, o); SearchItemEnd(); return r; }
            public static bool Toggle(bool v, Texture i, params GUILayoutOption[] o) { SearchItemBegin(null); bool r = U.Toggle(v, i, o); SearchItemEnd(); return r; }
            public static bool Toggle(bool v, Texture i, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); bool r = U.Toggle(v, i, s, o); SearchItemEnd(); return r; }
            public static int Toolbar(int sel, string[] t, params GUILayoutOption[] o) { SearchItemBegin(t == null ? null : string.Join(" ", t)); int r = U.Toolbar(sel, t, o); SearchItemEnd(); return r; }
            public static int Toolbar(int sel, string[] t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(t == null ? null : string.Join(" ", t)); int r = U.Toolbar(sel, t, s, o); SearchItemEnd(); return r; }
            public static int SelectionGrid(int sel, string[] t, int x, params GUILayoutOption[] o) { SearchItemBegin(t == null ? null : string.Join(" ", t)); int r = U.SelectionGrid(sel, t, x, o); SearchItemEnd(); return r; }
            public static int SelectionGrid(int sel, string[] t, int x, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(t == null ? null : string.Join(" ", t)); int r = U.SelectionGrid(sel, t, x, s, o); SearchItemEnd(); return r; }
            public static string TextField(string t, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextField(t, o); SearchItemEnd(); return r; }
            public static string TextField(string t, int max, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextField(t, max, o); SearchItemEnd(); return r; }
            public static string TextField(string t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextField(t, s, o); SearchItemEnd(); return r; }
            public static string TextField(string t, int max, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextField(t, max, s, o); SearchItemEnd(); return r; }
            public static string TextArea(string t, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextArea(t, o); SearchItemEnd(); return r; }
            public static string TextArea(string t, int max, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextArea(t, max, o); SearchItemEnd(); return r; }
            public static string TextArea(string t, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextArea(t, s, o); SearchItemEnd(); return r; }
            public static string TextArea(string t, int max, GUIStyle s, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.TextArea(t, max, s, o); SearchItemEnd(); return r; }
            public static string PasswordField(string t, char mask, params GUILayoutOption[] o) { SearchItemBegin(null); string r = U.PasswordField(t, mask, o); SearchItemEnd(); return r; }
            public static float HorizontalSlider(float v, float a, float b, params GUILayoutOption[] o) { SearchItemBegin(null); float r = U.HorizontalSlider(v, a, b, o); SearchItemEnd(); return r; }
            public static float HorizontalSlider(float v, float a, float b, GUIStyle sl, GUIStyle th, params GUILayoutOption[] o) { SearchItemBegin(null); float r = U.HorizontalSlider(v, a, b, sl, th, o); SearchItemEnd(); return r; }
            public static float VerticalSlider(float v, float a, float b, params GUILayoutOption[] o) { SearchItemBegin(null); float r = U.VerticalSlider(v, a, b, o); SearchItemEnd(); return r; }
            public static float VerticalSlider(float v, float a, float b, GUIStyle sl, GUIStyle th, params GUILayoutOption[] o) { SearchItemBegin(null); float r = U.VerticalSlider(v, a, b, sl, th, o); SearchItemEnd(); return r; }
            public static float HorizontalScrollbar(float v, float size, float a, float b, params GUILayoutOption[] o) { SearchItemBegin(null); float r = U.HorizontalScrollbar(v, size, a, b, o); SearchItemEnd(); return r; }
            public static float VerticalScrollbar(float v, float size, float a, float b, params GUILayoutOption[] o) { SearchItemBegin(null); float r = U.VerticalScrollbar(v, size, a, b, o); SearchItemEnd(); return r; }

            // ---- layout options ----
            public static GUILayoutOption Width(float w) => U.Width(w);
            public static GUILayoutOption MinWidth(float w) => U.MinWidth(w);
            public static GUILayoutOption MaxWidth(float w) => U.MaxWidth(w);
            public static GUILayoutOption Height(float h) => U.Height(h);
            public static GUILayoutOption MinHeight(float h) => U.MinHeight(h);
            public static GUILayoutOption MaxHeight(float h) => U.MaxHeight(h);
            public static GUILayoutOption ExpandWidth(bool e) => U.ExpandWidth(e);
            public static GUILayoutOption ExpandHeight(bool e) => U.ExpandHeight(e);
        }
    }
}
