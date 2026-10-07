using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The tutorial panel's key and mouse icons, drawn in code: a key is a keycap (a light rounded key with a darker lip
    /// and its name on it - E, Shift, Space...), a mouse button is a little mouse with that button lit up in gold (left
    /// for LMB, right for RMB, the wheel for the mouse wheel / middle button). The step texts carry them as tokens
    /// (K(Bind), Cap, MouseL...: \u0001 id \u0002) and RichDraw lays the text out itself - word by word, keeping the rich
    /// text tags open across the line breaks - with the icons sitting in the line like words. Anywhere the text is drawn
    /// plainly (the goal strip while the bag is open) PlainKeys turns them back into [E] / LMB.
    /// </summary>
    public static partial class Tutorial
    {
        const char IconOpen = '\u0001', IconClose = '\u0002';
        const string MouseLeftId = "#ML", MouseRightId = "#MR", MouseMiddleId = "#MM", MouseWheelId = "#MW", MouseId = "#M";

        static string Icon(string id) => IconOpen + id + IconClose;

        /// <summary>The key (or mouse button) an action is on, as an icon.</summary>
        static string K(Bind b) => KeyIcon(Binds.Get(b));

        static string KeyIcon(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse0: return Icon(MouseLeftId);
                case KeyCode.Mouse1: return Icon(MouseRightId);
                case KeyCode.Mouse2: return Icon(MouseMiddleId);
            }
            return Icon(Binds.KeyName(k));
        }

        /// <summary>A keycap with this on it (a key that isn't a rebindable action: Shift for Shift + click).</summary>
        static string Cap(string label) => Icon(label);
        static string MouseL => Icon(MouseLeftId);
        static string MouseWheel => Icon(MouseWheelId);
        static string MouseIcon => Icon(MouseId);

        /// <summary>The left mouse button icon, as a token in a text (drawn by IconText / IconLine).</summary>
        public static string LmbIcon => Icon(MouseLeftId);

        /// <summary>Every "LMB" / "RMB" in a hint turned into the left / right mouse button icon.</summary>
        public static string WithMouseIcons(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf("LMB", StringComparison.Ordinal) >= 0) s = s.Replace("[LMB]", Icon(MouseLeftId)).Replace("LMB", Icon(MouseLeftId));
            if (s.IndexOf("RMB", StringComparison.Ordinal) >= 0) s = s.Replace("[RMB]", Icon(MouseRightId)).Replace("RMB", Icon(MouseRightId));
            return s;
        }

        // ------------------------------------------------------------------ keys in any HUD text

        /// <summary>The names keys go by in hints (Binds.KeyName's, and the usual short ones) - drawn as keycaps.</summary>
        static readonly HashSet<string> s_KeyWords = new HashSet<string>
        {
            "Esc", "Escape", "Tab", "Shift", "Space", "Ctrl", "Alt", "Enter", "Backspace", "Caps Lock", "Delete", "Insert", "Home", "End",
            "Page Up", "Page Down", "Up", "Down", "Left", "Right", "Right Shift", "Right Ctrl", "Right Alt", "Mouse 4", "Mouse 5", "Mouse 6", "Mouse 7",
        };
        /// <summary>The ones that are drawn as keys wherever they stand as a word (capitalised: "Esc to cancel", "hold Space").</summary>
        const string KeyWordAlternation = "Esc|Tab|Shift|Space|Ctrl|Alt";
        static readonly System.Text.RegularExpressions.Regex s_KeyRx = new System.Text.RegularExpressions.Regex(
            // [E] [Tab] [Mouse 4]...  |  WASD  |  W/S  |  Esc, Shift... as a word  |  a single capital before a colon (E: open)
            //  |  a single capital after hold / press / then (hold E, then E to drive)
            @"(?<![\w\u0001#])(?:\[(?<b>[^\[\]<>\u0001\u0002]{1,14})\]|(?<w>WASD)(?!\w)|(?<p>[A-Z])/(?<q>[A-Z])(?![\w\u0002])|(?<n>" + KeyWordAlternation + @")(?![\w\u0002])|(?<n>Enter)(?=:| to )|(?<l>[A-Z])(?=:(?:\s|$|<))|(?<=(?:[Hh]old|[Pp]ress|then) )(?<h>[A-Z])(?=[\s,.;:)!]|$))",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        static readonly Dictionary<string, string> s_KeyIconMemo = new Dictionary<string, string>();

        /// <summary>A name in [brackets] that is a key (not [LMB] / [RMB]: WithMouseIcons does those).</summary>
        static bool IsKeyName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            if (n.Length == 1) return char.IsLetterOrDigit(n[0]) || (n != "-" && "`=[];',./\\".IndexOf(n[0]) >= 0);
            if (s_KeyWords.Contains(n)) return true;
            if (n.Length <= 3 && n[0] == 'F' && int.TryParse(n.Substring(1), out int f) && f >= 1 && f <= 15) return true; // (F1..F15)
            if (n.StartsWith("Num ", StringComparison.Ordinal) || n.StartsWith("Keypad", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Every key a HUD hint mentions turned into its keycap icon (and LMB / RMB into the mouse icons - WithMouseIcons):
        /// "[E]", "[Tab]", "[Mouse 4]" (the hint builders write keys like that: KeyTag), "E: open" (a single capital
        /// before a colon), "hold E" / "press E" / "then E", "WASD", "W/S", and Esc / Tab / Shift / Space / Ctrl / Alt /
        /// Enter as words. Ordinary words are left alone. Drawn by IconLine / IconText (Hud.Shadowed does it for every HUD
        /// line). Remembered, so it's cheap every frame.
        /// </summary>
        public static string WithKeyIcons(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s_KeyIconMemo.TryGetValue(s, out var done)) return done;
            string r = s_KeyRx.Replace(s, m =>
            {
                if (m.Groups["b"].Success)
                {
                    string n = m.Groups["b"].Value;
                    if (n == "LMB" || n == "RMB") return m.Value; // (the mouse: below)
                    if (n == "Middle mouse") return Icon(MouseMiddleId);
                    if (n == "mouse wheel") return Icon(MouseWheelId);
                    return IsKeyName(n) ? Cap(n) : m.Value;
                }
                if (m.Groups["w"].Success) return Cap("W") + Cap("A") + Cap("S") + Cap("D");
                if (m.Groups["p"].Success) return Cap(m.Groups["p"].Value) + "/" + Cap(m.Groups["q"].Value);
                if (m.Groups["n"].Success) return Cap(m.Groups["n"].Value);
                if (m.Groups["l"].Success) return Cap(m.Groups["l"].Value);
                if (m.Groups["h"].Success) return Cap(m.Groups["h"].Value);
                return m.Value;
            });
            r = WithMouseIcons(r);
            if (s_KeyIconMemo.Count > 600) s_KeyIconMemo.Clear();
            s_KeyIconMemo[s] = r;
            return r;
        }

        /// <summary>The text has an icon in it (a key or a mouse button) - draw it with IconLine / IconText.</summary>
        public static bool HasIcons(string s) => !string.IsNullOrEmpty(s) && s.IndexOf(IconOpen) >= 0;

        /// <summary>An action's key for a hint: "[E]" (WithKeyIcons draws it as the keycap; LMB / RMB as the mouse).</summary>
        public static string KeyTag(Bind b)
        {
            string n = Binds.Name(b);
            return n == "-" ? "(unbound)" : "[" + n + "]";
        }

        /// <summary>A wrapped text with its icons (left aligned, from the top of `r`).</summary>
        public static void IconText(Rect r, string text, GUIStyle st, Action<Rect, string, GUIStyle> shadowed = null) => RichDraw(r, text, st, shadowed);

        /// <summary>One line of text with its icons, placed in `r` by the style's alignment (centred, left or right).</summary>
        public static void IconLine(Rect r, string text, GUIStyle st, Action<Rect, string, GUIStyle> shadowed = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            EnsureIcons();
            var laid = Lay(text, st, 100000f);
            float w = 0f;
            foreach (var it in laid.Items) w = Mathf.Max(w, it.R.xMax);
            var a = st.alignment;
            bool centre = a == TextAnchor.UpperCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.LowerCenter;
            bool right = a == TextAnchor.UpperRight || a == TextAnchor.MiddleRight || a == TextAnchor.LowerRight;
            bool middle = a == TextAnchor.MiddleLeft || a == TextAnchor.MiddleCenter || a == TextAnchor.MiddleRight;
            bool lower = a == TextAnchor.LowerLeft || a == TextAnchor.LowerCenter || a == TextAnchor.LowerRight;
            float x = centre ? r.x + (r.width - w) * 0.5f : right ? r.xMax - w : r.x;
            float y = middle ? r.y + (r.height - laid.Height) * 0.5f : lower ? r.yMax - laid.Height : r.y;
            DrawLaid(new Vector2(Mathf.Round(x), Mathf.Round(y)), laid, st, shadowed);
        }

        /// <summary>Just the left mouse button icon, fitted into `r`.</summary>
        public static void DrawLmb(Rect r)
        {
            EnsureIcons();
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, s_MouseLTex, ScaleMode.ScaleToFit, true);
        }

        /// <summary>The text with its icons written out ([E], LMB, mouse wheel), for where it's drawn as plain text.</summary>
        public static string PlainKeys(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf(IconOpen) < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != IconOpen) { sb.Append(s[i]); continue; }
                int end = s.IndexOf(IconClose, i + 1);
                if (end < 0) break;
                sb.Append(PlainIcon(s.Substring(i + 1, end - i - 1)));
                i = end;
            }
            return sb.ToString();
        }

        static string PlainIcon(string id)
        {
            switch (id)
            {
                case MouseLeftId: return "<b><color=#ffd24a>[LMB]</color></b>";
                case MouseRightId: return "<b><color=#ffd24a>[RMB]</color></b>";
                case MouseMiddleId: return "<b><color=#ffd24a>[Middle mouse]</color></b>";
                case MouseWheelId: return "<b><color=#ffd24a>[mouse wheel]</color></b>";
                case MouseId: return "<b><color=#ffd24a>[mouse]</color></b>";
            }
            return $"<b><color=#ffd24a>[{id}]</color></b>";
        }

        // ------------------------------------------------------------------ the textures

        static Texture2D s_CapTex, s_MouseTex, s_MouseLTex, s_MouseRTex, s_MouseWTex;
        /// <summary>The keycap texture's size, and its ends (left / right, in texture pixels) that keep their shape when a
        /// wide key (Shift, Space) stretches its middle.</summary>
        const int CapTexSize = 48, CapEnd = 14;
        /// <summary>The middle of the keycap's face, from its top (a fraction of its height): the lip along the bottom is
        /// left out, so the name sits on the face.</summary>
        const float CapFaceMid = 22f / 48f;
        /// <summary>Key and mouse icons, as a multiple of a line of text's height.</summary>
        const float IconScale = 1.45f;

        /// <summary>A keycap in `r`: scaled to the rect's height as a whole (the old 9-sliced GUIStyle kept its borders at
        /// texture size, so a key shorter than them came out squashed with its top cut off), only the middle stretched
        /// for a key wider than it is tall.</summary>
        static void DrawCap(Rect r)
        {
            float s = r.height / CapTexSize, end = Mathf.Min(CapEnd * s, r.width * 0.5f);
            float u = (float)CapEnd / CapTexSize;
            if (r.width <= r.height + 0.5f)
            {
                GUI.DrawTexture(r, s_CapTex, ScaleMode.StretchToFill, true);
                return;
            }
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, end, r.height), s_CapTex, new Rect(0f, 0f, u, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(r.x + end, r.y, r.width - 2f * end, r.height), s_CapTex, new Rect(u, 0f, 1f - 2f * u, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - end, r.y, end, r.height), s_CapTex, new Rect(1f - u, 0f, u, 1f));
        }

        static void EnsureIcons()
        {
            if (s_CapTex != null) return;
            // a keycap: a pale rounded face with a dark rim, and a darker lip along the bottom (it stands up off the panel)
            const int C = 48;
            s_CapTex = new Texture2D(C, C, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[C * C];
            var rim = new Color(0.16f, 0.18f, 0.22f);
            var face = new Color(0.94f, 0.94f, 0.96f);
            var lip = new Color(0.6f, 0.62f, 0.68f);
            for (int y = 0; y < C; y++)
                for (int x = 0; x < C; x++)
                {
                    // (texture rows go up: y = 0 is the bottom)
                    float d = RoundRect(x + 0.5f, y + 0.5f, 0, 0, C, C, 11f);        // >0 inside the key
                    float top = RoundRect(x + 0.5f, y + 0.5f, 3, 7, C - 3, C - 3, 8f); // the face, above the lip
                    Color c = d > 3f ? (top > 0f ? Color.Lerp(lip, face, Mathf.Clamp01(top)) : lip) : rim;
                    if (top > 0f && y > C - 12) c = Color.Lerp(c, Color.white, 0.25f); // (a little shine along the top)
                    c.a = Mathf.Clamp01(d);
                    px[y * C + x] = c;
                }
            s_CapTex.SetPixels(px);
            s_CapTex.Apply();

            s_MouseTex = MouseTex(0);
            s_MouseLTex = MouseTex(1);
            s_MouseRTex = MouseTex(2);
            s_MouseWTex = MouseTex(3);
        }

        /// <summary>How far inside a rounded rectangle (pixels; negative outside), for an anti-aliased edge.</summary>
        static float RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            float cx = Mathf.Clamp(x, x0 + r, x1 - r), cy = Mathf.Clamp(y, y0 + r, y1 - r);
            float dx = x - cx, dy = y - cy;
            return r - Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>A mouse seen from above: a dark rounded body with a white rim, the two buttons and the wheel - `lit`
        /// 1 = the left button gold, 2 = the right, 3 = the wheel, 0 = none.</summary>
        static Texture2D MouseTex(int lit)
        {
            const int W = 40, H = 56;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[W * H];
            var gold = new Color(1f, 0.82f, 0.29f);
            var rim = new Color(0.93f, 0.94f, 0.97f);
            var body = new Color(0.17f, 0.19f, 0.24f);
            var wheel = new Color(0.62f, 0.64f, 0.7f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // u -1..1 left to right, v -1..1 top to bottom
                    float u = (x + 0.5f) / W * 2f - 1f, v = 1f - (y + 0.5f) / H * 2f;
                    float f = Mathf.Pow(Mathf.Abs(u) / 0.9f, 2.6f) + Mathf.Pow(Mathf.Abs(v) / 0.96f, 2.6f);
                    float a = Mathf.Clamp01((1f - f) * 14f);
                    if (a <= 0f) { px[y * W + x] = new Color(0, 0, 0, 0); continue; }
                    bool edge = f > 0.72f;
                    bool buttons = v < -0.1f;
                    bool split = Mathf.Abs(u) < 0.06f && buttons;
                    bool divider = Mathf.Abs(v + 0.1f) < 0.05f;
                    bool onWheel = Mathf.Abs(u) < 0.15f && v > -0.72f && v < -0.3f;
                    Color c = body;
                    if (buttons && !split && ((lit == 1 && u < 0f) || (lit == 2 && u > 0f))) c = gold;
                    if (edge || split || divider) c = rim;
                    if (onWheel) c = lit == 3 ? gold : wheel;
                    if (onWheel && Mathf.Abs(u) > 0.1f) c = rim;
                    c.a = a;
                    px[y * W + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ laying the text out

        /// <summary>One bit of a laid-out text: a word (rich text, its tags closed), an icon, or a gap.</summary>
        struct Piece
        {
            public byte Kind; // 0 word, 1 icon, 2 space, 3 line break
            public string Text;
        }

        struct Placed
        {
            public Rect R;
            public Piece P;
        }

        class Laid
        {
            public readonly List<Placed> Items = new List<Placed>();
            public float Height, Row, Line, IconH;
        }

        static readonly Dictionary<string, Laid> s_Laid = new Dictionary<string, Laid>();

        static bool IsTag(string t) => t == "b" || t == "i" || t == "/b" || t == "/i" || t.StartsWith("color") || t.StartsWith("/color") || t.StartsWith("size") || t.StartsWith("/size");

        /// <summary>The text cut into words, icons, spaces and line breaks; a rich text tag left open at the end of a word
        /// is closed there and opened again on the next one, so every word draws on its own.</summary>
        static List<Piece> Cut(string s)
        {
            var list = new List<Piece>();
            var open = new List<string>();
            var word = new StringBuilder();
            string prefix = "";
            bool any = false;
            void Flush()
            {
                if (!any) { word.Clear(); return; }
                var sb = new StringBuilder(prefix);
                sb.Append(word);
                for (int i = open.Count - 1; i >= 0; i--)
                {
                    string n = open[i];
                    int eq = n.IndexOf('=');
                    sb.Append("</").Append(eq >= 0 ? n.Substring(0, eq) : n).Append('>');
                }
                list.Add(new Piece { Kind = 0, Text = sb.ToString() });
                word.Clear();
                any = false;
            }
            void Start()
            {
                if (word.Length > 0) return;
                var sb = new StringBuilder();
                foreach (var t in open) sb.Append('<').Append(t).Append('>');
                prefix = sb.ToString();
            }
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '<')
                {
                    int end = s.IndexOf('>', i + 1);
                    string tag = end > i ? s.Substring(i + 1, end - i - 1) : "";
                    if (end > i && end - i < 40 && IsTag(tag))
                    {
                        Start();
                        word.Append(s, i, end - i + 1);
                        if (tag[0] == '/') { if (open.Count > 0) open.RemoveAt(open.Count - 1); }
                        else open.Add(tag);
                        i = end;
                        continue;
                    }
                }
                if (ch == IconOpen)
                {
                    int end = s.IndexOf(IconClose, i + 1);
                    if (end > i)
                    {
                        Flush();
                        list.Add(new Piece { Kind = 1, Text = s.Substring(i + 1, end - i - 1) });
                        i = end;
                        continue;
                    }
                }
                if (ch == ' ' || ch == '\n')
                {
                    Flush();
                    list.Add(new Piece { Kind = (byte)(ch == ' ' ? 2 : 3) });
                    continue;
                }
                Start();
                word.Append(ch);
                any = true;
            }
            Flush();
            return list;
        }

        static GUIStyle WordStyle(GUIStyle st)
        {
            return new GUIStyle(st)
            {
                wordWrap = false, richText = true, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), stretchWidth = false, stretchHeight = false,
            };
        }

        static int CapFont(GUIStyle ws) => Mathf.Max(8, Mathf.RoundToInt((ws.fontSize > 0 ? ws.fontSize : 13) * 0.85f));

        static Laid Lay(string text, GUIStyle st, float width)
        {
            string key = text + "\u0003" + Mathf.RoundToInt(width) + "\u0003" + st.fontSize + "\u0003" + (st.font != null ? st.font.name : "");
            if (s_Laid.TryGetValue(key, out var done)) return done;
            if (s_Laid.Count > 96) s_Laid.Clear();
            var ws = WordStyle(st);
            var laid = new Laid();
            float line = ws.CalcSize(new GUIContent("Ag")).y;
            float iconH = Mathf.Round(line * IconScale);
            float row = Mathf.Max(line, iconH) + Mathf.Max(2f, line * 0.18f);
            float space = Mathf.Max(2f, ws.CalcSize(new GUIContent("a a")).x - ws.CalcSize(new GUIContent("aa")).x);
            float gap = Mathf.Max(1f, iconH * 0.1f);
            var capSt = new GUIStyle(ws) { fontSize = CapFont(ws), fontStyle = FontStyle.Bold, richText = false };
            laid.Row = row;
            laid.Line = line;
            laid.IconH = iconH;
            float x = 0f, y = 0f, pend = 0f;
            bool lastIcon = false;
            foreach (var p in Cut(text))
            {
                if (p.Kind == 3) { x = 0f; y += row; pend = 0f; lastIcon = false; continue; }
                if (p.Kind == 2) { if (x > 0f) pend = pend >= space ? pend + space : space; lastIcon = false; continue; } // (a run of spaces keeps its width)
                float w, h;
                if (p.Kind == 1)
                {
                    h = iconH;
                    // (a keycap is square - a single letter, a digit - unless its name needs more room: Shift, Space)
                    w = p.Text.StartsWith("#M") ? Mathf.Round(iconH * 0.72f) : Mathf.Round(Mathf.Max(iconH, capSt.CalcSize(new GUIContent(p.Text)).x + iconH * 0.45f));
                    if (pend == 0f && x > 0f) pend = gap; // (icons side by side, or against a word, get a hair of room)
                }
                else
                {
                    h = line;
                    w = ws.CalcSize(new GUIContent(p.Text)).x;
                    if (pend == 0f && x > 0f && lastIcon) pend = gap;
                }
                if (x > 0f && x + pend + w > width) { x = 0f; y += row; pend = 0f; }
                laid.Items.Add(new Placed { R = new Rect(x + pend, y + (row - h) * 0.5f, w, h), P = p });
                x += pend + w;
                pend = 0f;
                lastIcon = p.Kind == 1;
            }
            laid.Height = y + row;
            s_Laid[key] = laid;
            return laid;
        }

        /// <summary>How tall RichDraw draws this text at this width.</summary>
        static float RichHeight(string text, GUIStyle st, float width) => string.IsNullOrEmpty(text) ? 0f : Lay(text, st, width).Height;

        /// <summary>Draws the text word by word, wrapped at r.width, its key and mouse tokens as icons. `shadowed` draws a word
        /// with a drop shadow (null: plainly).</summary>
        static void RichDraw(Rect r, string text, GUIStyle st, Action<Rect, string, GUIStyle> shadowed = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            EnsureIcons();
            var laid = Lay(text, st, r.width);
            DrawLaid(r.position, laid, st, shadowed);
        }

        static void DrawLaid(Vector2 at, Laid laid, GUIStyle st, Action<Rect, string, GUIStyle> shadowed)
        {
            var ws = WordStyle(st);
            var capSt = new GUIStyle(ws) { fontSize = CapFont(ws), fontStyle = FontStyle.Bold, richText = false, alignment = TextAnchor.MiddleCenter };
            capSt.normal.textColor = new Color(0.1f, 0.11f, 0.14f);
            bool repaint = Event.current.type == EventType.Repaint;
            foreach (var it in laid.Items)
            {
                var rr = new Rect(at.x + it.R.x, at.y + it.R.y, it.R.width, it.R.height);
                if (it.P.Kind == 0)
                {
                    if (shadowed != null) shadowed(rr, it.P.Text, ws);
                    else GUI.Label(rr, it.P.Text, ws);
                    continue;
                }
                if (!repaint) continue;
                string id = it.P.Text;
                if (id.StartsWith("#M"))
                {
                    var tex = id == MouseLeftId ? s_MouseLTex : id == MouseRightId ? s_MouseRTex : id == MouseWheelId || id == MouseMiddleId ? s_MouseWTex : s_MouseTex;
                    GUI.DrawTexture(rr, tex, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    DrawCap(rr);
                    // (the name sits in the middle of the face, above the lip - in a rect tall enough for the whole
                    // glyph, so nothing is cut off at the top)
                    float th = Mathf.Max(rr.height, capSt.CalcSize(new GUIContent(id)).y + 4f);
                    GUI.Label(new Rect(rr.x, rr.y + rr.height * CapFaceMid - th * 0.5f, rr.width, th), id, capSt);
                }
            }
        }
    }
}
