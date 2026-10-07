using System;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// THE COLOUR WHEEL: double-click any colour square in the settings and a little window opens over everything - a
    /// colour wheel (round it is the hue, out from the middle is how strong the colour is), a brightness strip beside it,
    /// the colour before and now side by side, its hex code to type, and Undo / OK. The colour changes live as you drag
    /// (saved when you let go). Enter or a click outside keeps it, Esc puts the old colour back.
    ///
    /// HOW A PANEL USES IT (any partial of Hud):
    ///   - a square that already does something on a click (a preset): call <c>ColourWheelOnDoubleClick(rect, pref)</c>
    ///     right BEFORE the GUI.Button for it - a double-click opens the wheel and eats the click;
    ///   - a square that just shows the colour: <c>ColourSwatch(rect, pref)</c> draws it and opens the wheel on a
    ///     double-click; <c>PresetSwatch(rect, colour, pref)</c> draws a preset (gold edge while it's the one in use) -
    ///     click sets it, double-click opens the wheel;
    ///   - a colour that isn't a DisplayPref.Colour: the overloads with a key, a getter and a setter (set(colour, save):
    ///     save is false while dragging, true when let go / OK / Undo);
    ///   - from anywhere: <c>Hud.OpenColourWheel(...)</c> (static).
    /// The wheel stays open while the panel that opened it keeps drawing its square (it closes by itself when the settings
    /// close); it's drawn last in OnGUI, and while it's open the clicks and keys only go to it.
    /// </summary>
    public partial class Hud
    {
        // the open wheel
        bool m_CwOpen, m_CwPinned, m_CwDropFocus;
        string m_CwKey, m_CwTitle;
        Func<Color> m_CwGet;
        Action<Color, bool> m_CwSet;
        Color m_CwBefore;
        Vector3 m_CwHsv;
        Color m_CwHsvOf;
        Vector2 m_CwAt;
        int m_CwOwnerFrame, m_CwDrag; // drag: 0 none, 1 the wheel, 2 the brightness strip
        string m_CwHex;
        // the event held back from everything under the wheel (OnGUI: ColourWheelGate, then DrawColourWheel)
        bool m_CwHeld;
        EventType m_CwHeldType;
        // double-click detection (Event.clickCount isn't always filled in in a build)
        string m_CwClickKey;
        float m_CwClickAt = -10f;
        static Texture2D s_CwDisc, s_CwStrip;
        static Vector2 s_CwStripFor = new Vector2(-1f, -1f);
        GUIStyle m_CwHexStyle, m_CwTitleStyle;
        float m_CwStyleK;

        /// <summary>(tests) the frame the wheel was last drawn, and whether it's open now.</summary>
        public static int ColourWheelShownFrame = -10;
        public static bool ColourWheelOpen => s_I != null && s_I.m_CwOpen;
        /// <summary>(tests) the key of the colour the wheel is open on (a DisplayPref's key).</summary>
        public static string ColourWheelKey => s_I != null && s_I.m_CwOpen ? s_I.m_CwKey : null;

        // ------------------------------------------------------------------ opening it

        /// <summary>Opens the wheel on any colour. `key`: what it's for (the square that opened it keeps it open by calling
        /// one of the swatch helpers with the same key each frame; pinned = stays open without that).</summary>
        public static void OpenColourWheel(string key, string title, Func<Color> get, Action<Color, bool> set, bool pinned = false)
        {
            if (s_I == null || get == null || set == null) return;
            s_I.CwOpen(key, title, get, set, pinned);
        }

        /// <summary>Opens the wheel on a DisplayPref colour.</summary>
        public static void OpenColourWheel(DisplayPref.Colour pref, string title = null, bool pinned = false)
        {
            if (pref == null) return;
            OpenColourWheel(pref.Key, title ?? pref.Key, () => pref.Value, PrefSetter(pref), pinned);
        }

        static Action<Color, bool> PrefSetter(DisplayPref.Colour pref) => (c, save) => { pref.Set(c, false); if (save) pref.Save(); };

        void CwOpen(string key, string title, Func<Color> get, Action<Color, bool> set, bool pinned)
        {
            if (m_CwOpen) CwClose(true);
            m_CwOpen = true;
            m_CwPinned = pinned;
            m_CwKey = key;
            m_CwTitle = string.IsNullOrEmpty(title) ? "Colour" : title;
            m_CwGet = get;
            m_CwSet = set;
            m_CwBefore = get();
            m_CwBefore.a = 1f;
            Color.RGBToHSV(m_CwBefore, out float h, out float s, out float v);
            m_CwHsv = new Vector3(h, s, v);
            m_CwHsvOf = m_CwBefore;
            m_CwAt = Event.current != null ? Event.current.mousePosition : new Vector2(Screen.width * 0.5f, Screen.height * 0.4f);
            m_CwOwnerFrame = Time.frameCount;
            m_CwDrag = 0;
            m_CwHex = null;
            m_CwDropFocus = true; // (whatever text box had the keyboard lets go: next OnGUI)
            ClickSound();
        }

        /// <summary>Closes it: keeps the colour (saved), or puts the one from before back.</summary>
        void CwClose(bool keep)
        {
            if (!m_CwOpen) return;
            m_CwOpen = false;
            m_CwDrag = 0;
            try { m_CwSet(keep ? Cur() : m_CwBefore, true); } catch (Exception e) { Debug.LogWarning("[HUD] colour wheel: " + e.Message); }
            m_CwDropFocus = true; // (its hex box lets go of the keyboard: next OnGUI, ColourWheelGate)
        }

        /// <summary>(tests) closes the wheel, keeping the colour (or not).</summary>
        public static void CloseColourWheel(bool keep = true) { if (s_I != null) s_I.CwClose(keep); }

        /// <summary>(tests) exactly what a drag on the wheel does: hue / strength / brightness (0..1), with the mouse still
        /// held (not saved yet) or let go (saved).</summary>
        public static void TestColourWheelPick(float h, float s, float v, bool release)
        {
            if (s_I == null || !s_I.m_CwOpen) return;
            s_I.CwApply(new Vector3(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), Mathf.Clamp01(v)), release);
        }

        Color Cur() { var c = m_CwGet(); c.a = 1f; return c; }

        void CwApply(Vector3 hsv, bool save)
        {
            m_CwHsv = hsv;
            var c = Color.HSVToRGB(hsv.x, hsv.y, hsv.z);
            c.a = 1f;
            m_CwSet(c, save);
            m_CwHsvOf = Cur();
            m_CwHex = null;
        }

        // ------------------------------------------------------------------ the squares that open it

        /// <summary>Call right BEFORE the control that takes a click on a colour square: a double-click on `r` opens the
        /// wheel on `pref` (and eats the click). True when it opened. Also keeps the wheel open while it's on this colour.</summary>
        public bool ColourWheelOnDoubleClick(Rect r, DisplayPref.Colour pref, string title = null)
        {
            if (pref == null) return false;
            return ColourWheelOnDoubleClick(r, pref.Key, title ?? pref.Key, () => pref.Value, PrefSetter(pref));
        }

        /// <summary>The same for any colour (a key for it, its name, a getter and a setter(colour, save)).</summary>
        public bool ColourWheelOnDoubleClick(Rect r, string key, string title, Func<Color> get, Action<Color, bool> set)
        {
            if (m_CwOpen && m_CwKey == key) m_CwOwnerFrame = Time.frameCount;
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition)) return false;
            string ck = key + "@" + Mathf.RoundToInt(r.x) + "," + Mathf.RoundToInt(r.y);
            bool dbl = e.clickCount >= 2 || (m_CwClickKey == ck && Time.unscaledTime - m_CwClickAt < 0.4f);
            m_CwClickKey = ck;
            m_CwClickAt = Time.unscaledTime;
            if (!dbl) return false;
            m_CwClickKey = null;
            CwOpen(key, title, get, set, false);
            e.Use();
            return true;
        }

        /// <summary>A colour square showing `pref`'s colour (a light edge; gold while the wheel is open on it): double-click
        /// it for the wheel. Draw it at `r` anywhere in a settings panel.</summary>
        public void ColourSwatch(Rect r, DisplayPref.Colour pref, string title = null)
        {
            ColourWheelOnDoubleClick(r, pref, title);
            bool open = m_CwOpen && m_CwKey == pref.Key;
            Fill(r, open ? new Color(1f, 0.82f, 0.3f) : new Color(0.75f, 0.75f, 0.75f));
            Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), pref.Value);
            TrackHover(r);
        }

        /// <summary>A preset colour square for `pref`: gold edged while it's the colour in use; a click sets it, a
        /// double-click opens the wheel. True when it was clicked (picked).</summary>
        public bool PresetSwatch(Rect r, Color c, DisplayPref.Colour pref, string title = null)
        {
            Fill(r, ColorSlots.Same(c, pref.Value) ? new Color(1f, 0.82f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.8f));
            Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), c);
            TrackHover(r);
            bool wheel = ColourWheelOnDoubleClick(r, pref, title);
            bool click = GUI.Button(r, GUIContent.none, GUIStyle.none); // (always made, so the control ids stay the same)
            if (wheel || !click) return false;
            ClickSound();
            pref.Set(c);
            return true;
        }

        // ------------------------------------------------------------------ while it's open: input only goes to it

        /// <summary>Start of OnGUI: while the wheel is open, a click / key isn't seen by anything under it (it's held back
        /// as Ignore and given back to the wheel at the end - DrawColourWheel). Also counts as typing, so Esc closes the
        /// wheel instead of the page.</summary>
        void ColourWheelGate()
        {
            m_CwHeld = false;
            if (m_CwDropFocus) { m_CwDropFocus = false; GUIUtility.keyboardControl = 0; }
            if (!m_CwOpen) return;
            m_TypingFrame = Time.frameCount;
            var e = Event.current;
            if (e.isMouse || e.isKey || e.type == EventType.ScrollWheel)
            {
                m_CwHeld = true;
                m_CwHeldType = e.type;
                e.type = EventType.Ignore;
            }
        }

        static void EnsureWheelTextures()
        {
            if (s_CwDisc != null) return;
            const int S = 160;
            s_CwDisc = new Texture2D(S, S, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // (texture rows go up: y = 0 is the bottom; the hue goes round anticlockwise from red on the right)
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float h = Mathf.Repeat(Mathf.Atan2(dy, dx) / (Mathf.PI * 2f), 1f);
                    Color c = Color.HSVToRGB(h, Mathf.Clamp01(d), 1f);
                    c.a = Mathf.Clamp01((1f - d) * S * 0.5f);
                    px[y * S + x] = c;
                }
            s_CwDisc.SetPixels32(px);
            s_CwDisc.Apply(false);
            s_CwStrip = new Texture2D(1, 64, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        }

        void StripTexture(float h, float s)
        {
            var want = new Vector2(h, s);
            if ((want - s_CwStripFor).sqrMagnitude < 1e-7f) return;
            s_CwStripFor = want;
            var px = new Color32[64];
            for (int y = 0; y < 64; y++) px[y] = Color.HSVToRGB(h, s, y / 63f); // (bright at the top)
            s_CwStrip.SetPixels32(px);
            s_CwStrip.Apply(false);
        }

        /// <summary>End of OnGUI: the wheel's window, over everything (gets the held-back event).</summary>
        void DrawColourWheel()
        {
            if (!m_CwOpen) { m_CwHeld = false; return; }
            var e = Event.current;
            if (m_CwHeld) { e.type = m_CwHeldType; m_CwHeld = false; }
            // the square that opened it isn't being drawn any more (the settings closed, another tab): it's done
            if (!m_CwPinned && Time.frameCount - m_CwOwnerFrame > 10) { CwClose(true); return; }
            float k = m_Scale;
            s_HudAlpha = 1f;
            GUI.color = Color.white;
            EnsureWheelTextures();
            if (m_CwHexStyle == null || !Mathf.Approximately(m_CwStyleK, k))
            {
                m_CwStyleK = k;
                m_CwHexStyle = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.MiddleCenter };
                m_CwTitleStyle = new GUIStyle(m_Label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
            }
            // keep the hue / strength while it's dark or grey (only re-read it when the colour changed from elsewhere)
            var now = Cur();
            if (!ColorSlots.Same(now, m_CwHsvOf))
            {
                Color.RGBToHSV(now, out float h0, out float s0, out float v0);
                m_CwHsv = new Vector3(h0, s0, v0);
                m_CwHsvOf = now;
            }
            float pad = Mathf.Round(12 * k), disc = Mathf.Round(200 * k), strip = Mathf.Round(24 * k), titleH = Mathf.Round(28 * k);
            float rowH = Mathf.Round(32 * k), lineH = Mathf.Round(20 * k);
            float pw = pad * 3 + disc + strip, ph = pad + titleH + disc + pad + rowH + 6 * k + lineH + 6 * k + rowH + pad;
            // next to where it was opened (the right of it, or the left if there's no room), on the screen
            float px = m_CwAt.x + 24 * k, py = m_CwAt.y - ph * 0.3f;
            if (px + pw > Screen.width - 8) px = m_CwAt.x - 24 * k - pw;
            px = Mathf.Clamp(px, 8, Mathf.Max(8, Screen.width - pw - 8));
            py = Mathf.Clamp(py, 8, Mathf.Max(8, Screen.height - ph - 8));
            var panel = new Rect(Mathf.Round(px), Mathf.Round(py), pw, ph);
            if (panel.Contains(e.mousePosition)) MouseOverUI = true;
            ColourWheelShownFrame = Time.frameCount;

            var discR = new Rect(panel.x + pad, panel.y + pad + titleH, disc, disc);
            var stripR = new Rect(discR.xMax + pad, discR.y, strip, disc);
            var hsv = m_CwHsv;

            // ---- the mouse: press on the wheel or the strip and drag ----
            Vector2 c0 = discR.center;
            float rad = disc * 0.5f;
            bool onDisc = (e.mousePosition - c0).magnitude <= rad + 2f;
            if (e.type == EventType.MouseDown && e.button == 0 && (onDisc || stripR.Contains(e.mousePosition)))
            {
                m_CwDrag = onDisc ? 1 : 2;
                CwApply(CwPickAt(hsv, m_CwDrag, c0, rad, stripR, e.mousePosition), false);
                ClickSound();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && m_CwDrag != 0)
            {
                var nh = CwPickAt(hsv, m_CwDrag, c0, rad, stripR, e.mousePosition);
                if (nh != hsv)
                {
                    CwApply(nh, false);
                    if (Time.unscaledTime - m_LastSlideSound > 0.045f)
                    {
                        m_LastSlideSound = Time.unscaledTime;
                        Sfx.PlayUi(Sfx.UiSlide, 0.5f, 0.8f + 0.7f * (m_CwDrag == 1 ? nh.y : nh.z));
                    }
                }
                e.Use();
            }
            else if ((e.type == EventType.MouseUp && m_CwDrag != 0) || (e.type == EventType.Repaint && m_CwDrag != 0 && !Input.GetMouseButton(0)))
            {
                m_CwDrag = 0;
                m_CwSet(Cur(), true); // (let go: saved)
                if (e.type == EventType.MouseUp) e.Use();
            }
            hsv = m_CwHsv;
            now = Cur();

            // ---- drawn ----
            Fill(new Rect(panel.x - 2, panel.y - 2, panel.width + 4, panel.height + 4), new Color(0f, 0f, 0f, 0.55f));
            Fill(panel, new Color(0.09f, 0.1f, 0.13f, 0.97f));
            Fill(new Rect(panel.x, panel.y, panel.width, 2 * k), GameSettings.AccentColor);
            GUI.Label(new Rect(panel.x + pad, panel.y + pad * 0.5f, panel.width - pad * 2 - 30 * k, titleH), m_CwTitle, m_CwTitleStyle);
            if (BtnAt(new Rect(panel.xMax - pad - 26 * k, panel.y + pad * 0.5f + 1, 26 * k, 24 * k), "x", m_Button)) { CwClose(true); return; }
            if (e.type == EventType.Repaint)
            {
                // the wheel at this brightness (the texture is at full brightness; darkening it by the brightness gives
                // exactly the colours there), then the strip at this hue and strength
                var oldC = GUI.color;
                GUI.color = new Color(hsv.z, hsv.z, hsv.z, 1f);
                GUI.DrawTexture(discR, s_CwDisc, ScaleMode.StretchToFill, true);
                GUI.color = oldC;
                StripTexture(hsv.x, hsv.y);
                Fill(new Rect(stripR.x - 1, stripR.y - 1, stripR.width + 2, stripR.height + 2), new Color(0.75f, 0.75f, 0.75f));
                GUI.DrawTexture(stripR, s_CwStrip, ScaleMode.StretchToFill, false);
                // where the colour is: a ring on the wheel, a bar across the strip
                float ang = hsv.x * Mathf.PI * 2f;
                var p = c0 + new Vector2(Mathf.Cos(ang), -Mathf.Sin(ang)) * hsv.y * rad;
                float r = Mathf.Round(7 * k), t = Mathf.Max(1f, Mathf.Round(1.5f * k));
                Frame(new Rect(p.x - r - t, p.y - r - t, 2 * (r + t), 2 * (r + t)), t, Color.black);
                Frame(new Rect(p.x - r, p.y - r, 2 * r, 2 * r), t, Color.white);
                Fill(new Rect(p.x - r + t, p.y - r + t, 2 * (r - t), 2 * (r - t)), now);
                float vy = stripR.y + (1f - hsv.z) * stripR.height;
                Frame(new Rect(stripR.x - 3 * t, vy - 3 * t, stripR.width + 6 * t, 6 * t), t, Color.black);
                Frame(new Rect(stripR.x - 2 * t, vy - 2 * t, stripR.width + 4 * t, 4 * t), t, Color.white);
            }

            // ---- before / now, the hex code ----
            float y = discR.yMax + pad;
            float sw = Mathf.Round(54 * k);
            var beforeR = new Rect(panel.x + pad, y, sw, rowH);
            var nowR = new Rect(beforeR.xMax, y, sw, rowH);
            Fill(new Rect(beforeR.x - 2, beforeR.y - 2, sw * 2 + 4, rowH + 4), new Color(0.8f, 0.8f, 0.8f));
            Fill(beforeR, m_CwBefore);
            Fill(nowR, now);
            // (a click on "before" puts it back)
            if (GUI.Button(beforeR, GUIContent.none, GUIStyle.none)) { ClickSound(); m_CwSet(m_CwBefore, true); m_CwHex = null; }
            TrackHover(beforeR);
            const string ctl = "cwheel.hex";
            var hexR = new Rect(nowR.xMax + pad, y + 2 * k, panel.xMax - pad - nowR.xMax - pad, rowH - 4 * k);
            bool focused = GUI.GetNameOfFocusedControl() == ctl;
            if (!focused) m_CwHex = null;
            string hex = m_CwHex ?? "#" + ColorUtility.ToHtmlStringRGB(now);
            GUI.SetNextControlName(ctl);
            string nh2 = GUI.TextField(hexR, hex, 7, m_CwHexStyle);
            if (focused) m_TypingFrame = Time.frameCount;
            if (nh2 != hex)
            {
                m_CwHex = nh2;
                var tt = nh2.Trim().TrimStart('#');
                if (tt.Length == 6 && ColorUtility.TryParseHtmlString("#" + tt, out var hc)) { hc.a = 1f; m_CwSet(hc, true); }
            }
            y += rowH + 6 * k;
            GUI.Label(new Rect(panel.x + pad, y, panel.width - pad * 2, lineH),
                $"<color=#bbbbbb>H {hsv.x * 360f:0}°   S {hsv.y * 100f:0}%   B {hsv.z * 100f:0}%    <size={Mathf.RoundToInt(12 * k)}>before | now</size></color>", m_Small);
            y += lineH + 6 * k;

            // ---- Undo / OK ----
            float bw = (panel.width - pad * 3) * 0.5f;
            if (BtnAt(new Rect(panel.x + pad, y, bw, rowH), "Undo", m_Button)) { CwClose(false); return; }
            if (BtnAt(new Rect(panel.x + pad * 2 + bw, y, bw, rowH), "<b>OK</b>", m_Button)) { CwClose(true); return; }

            // ---- keys, and a click outside ----
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape) { CwClose(false); e.Use(); return; }
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { CwClose(true); e.Use(); return; }
            }
            if (e.type == EventType.MouseDown && !panel.Contains(e.mousePosition)) { CwClose(true); e.Use(); return; }
            // (nothing under it gets anything that happened over it)
            if ((e.isMouse || e.type == EventType.ScrollWheel) && panel.Contains(e.mousePosition)) e.Use();
        }

        static Vector3 CwPickAt(Vector3 hsv, int what, Vector2 c0, float rad, Rect strip, Vector2 m)
        {
            if (what == 1)
            {
                var d = m - c0;
                hsv.x = Mathf.Repeat(Mathf.Atan2(-d.y, d.x) / (Mathf.PI * 2f), 1f);
                hsv.y = Mathf.Clamp01(d.magnitude / rad);
                if (hsv.z < 0.04f) hsv.z = 1f; // (picking a hue while it's black: bring it up so it shows)
            }
            else if (what == 2) hsv.z = Mathf.Clamp01(1f - (m.y - strip.y) / strip.height);
            return hsv;
        }
    }
}
