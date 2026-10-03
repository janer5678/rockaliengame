using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > World colours: the colour picker for a world colour (and the first-person hands) - a saturation /
    /// brightness square and a hue strip (textures made here) dragged with the mouse, the colour before and now side by
    /// side, Revert and Close. The colour shows on everything while you drag and is saved when you let go. Also the POST
    /// PROCESSING rows (and their extra looks) in Settings > Display.
    /// </summary>
    public partial class Hud
    {
        int m_PickerSlot = -1, m_PickerDrag; // drag: 0 none, 1 the square, 2 the hue strip
        Color m_PickerOld, m_PickerOldValue;
        bool m_PickerOldTeam, m_PickerDirty, m_ScrollToPicker;
        float m_PickerRowY = -1f;
        Texture2D m_SvTex, m_HueTex;
        float m_SvHue = -1f;
        const int SvSize = 64;

        /// <summary>(tests) open the picker on this colour slot (by index; -1 = leave it).</summary>
        public static int OpenPickerFor = -1;
        /// <summary>(tests) the slot whose picker is open (-1: none).</summary>
        public static int PickerSlot => s_I != null ? s_I.m_PickerSlot : -1;

        /// <summary>Click on a row's colour (or Pick): opens its picker (closes it if it was open).</summary>
        void TogglePicker(ColorSlots.Slot s, Color shown, bool teamHands)
        {
            bool was = m_PickerSlot == s.Index;
            ClosePicker();
            if (was) return;
            m_PickerSlot = s.Index;
            m_PickerOld = shown;
            m_PickerOldValue = s.Value;
            m_PickerOldTeam = teamHands;
        }

        void ClosePicker()
        {
            if (m_PickerDirty) SavePick();
            m_PickerSlot = -1;
            m_PickerDrag = 0;
        }

        /// <summary>A colour from the picker: on everything straight away, saved now or when the mouse lets go.</summary>
        void ApplyPick(ColorSlots.Slot s, Vector3 hsv, bool save)
        {
            m_Hsv[s.Index] = hsv;
            ColorSlots.Set(s, Color.HSVToRGB(hsv.x, hsv.y, hsv.z), false);
            m_HsvOf[s.Index] = s.Value;
            m_PickerDirty = true;
            if (save) SavePick();
        }

        void SavePick()
        {
            m_PickerDirty = false;
            if (m_PickerSlot >= 0 && m_PickerSlot < ColorSlots.All.Count) ColorSlots.Save(ColorSlots.All[m_PickerSlot]);
        }

        bool m_TestHold;
        /// <summary>The mouse button is down (a drag in progress; tests hold it with TestPick).</summary>
        bool PickerMouseHeld => Input.GetMouseButton(0) || m_TestHold;

        /// <summary>(tests) exactly what a drag in the open picker does: hue / saturation / brightness (0..1), with the
        /// mouse still held (not saved yet) or let go (`release`: saved).</summary>
        public static void TestPick(float h, float s, float v, bool release)
        {
            if (s_I == null || s_I.m_PickerSlot < 0) return;
            s_I.m_TestHold = !release;
            s_I.ApplyPick(ColorSlots.All[s_I.m_PickerSlot], new Vector3(h, s, v), release);
        }

        /// <summary>Saturation (x) / brightness (y) for this hue, and the hue strip (red at the top).</summary>
        void PickerTextures(float hue)
        {
            if (m_HueTex == null)
            {
                m_HueTex = new Texture2D(1, 128, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                var hp = new Color32[128];
                for (int y = 0; y < 128; y++) hp[y] = Color.HSVToRGB(1f - (y + 0.5f) / 128f, 1f, 1f);
                m_HueTex.SetPixels32(hp);
                m_HueTex.Apply(false);
            }
            if (m_SvTex == null)
                m_SvTex = new Texture2D(SvSize, SvSize, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            if (Mathf.Abs(hue - m_SvHue) < 0.0005f) return;
            m_SvHue = hue;
            var px = new Color32[SvSize * SvSize];
            for (int y = 0; y < SvSize; y++)
                for (int x = 0; x < SvSize; x++)
                    px[y * SvSize + x] = Color.HSVToRGB(hue, x / (SvSize - 1f), y / (SvSize - 1f));
            m_SvTex.SetPixels32(px);
            m_SvTex.Apply(false);
        }

        static Vector3 PickAt(Vector3 hsv, int what, Rect sv, Rect hue, Vector2 m)
        {
            if (what == 1)
            {
                hsv.y = Mathf.Clamp01((m.x - sv.x) / sv.width);
                hsv.z = Mathf.Clamp01(1f - (m.y - sv.y) / sv.height);
            }
            else if (what == 2) hsv.x = Mathf.Clamp((m.y - hue.y) / hue.height, 0f, 0.9999f);
            return hsv;
        }

        /// <summary>The picker panel, under the colour's row (avail: the width there is for it).</summary>
        void DrawColourPicker(ColorSlots.Slot s, Color shown, bool teamHands, float avail)
        {
            float k = m_Scale;
            int i = s.Index;
            // hue / saturation / brightness are kept while you drag (so a grey or black doesn't lose its hue)
            if (!m_Hsv.ContainsKey(i) || !m_HsvOf.TryGetValue(i, out var of) || of != shown)
            {
                Color.RGBToHSV(shown, out float h0, out float s0, out float v0);
                m_Hsv[i] = new Vector3(h0, s0, v0);
                m_HsvOf[i] = shown;
            }
            var hsv = m_Hsv[i];
            float sq = Mathf.Round(176 * k), strip = Mathf.Round(26 * k), pad = Mathf.Round(10 * k), indent = Mathf.Round(12 * k);
            float infoW = Mathf.Round(Mathf.Clamp(avail - indent - pad * 4 - sq - strip, 170 * k, 300 * k));
            float aw = pad * 4 + sq + strip + infoW, ah = sq + pad * 2;
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            var area = GUILayoutUtility.GetRect(aw, ah, GUILayout.Width(aw), GUILayout.Height(ah));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4 * k);
            var svR = new Rect(area.x + pad, area.y + pad, sq, sq);
            var hueR = new Rect(svR.xMax + pad, svR.y, strip, sq);
            var info = new Rect(hueR.xMax + pad * 2, svR.y, area.xMax - hueR.xMax - pad * 3, sq);

            // the mouse: press in the square or the strip and drag
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && (svR.Contains(e.mousePosition) || hueR.Contains(e.mousePosition)))
            {
                m_PickerDrag = svR.Contains(e.mousePosition) ? 1 : 2;
                hsv = PickAt(hsv, m_PickerDrag, svR, hueR, e.mousePosition);
                ApplyPick(s, hsv, false);
                ClickSound();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && m_PickerDrag != 0)
            {
                var nh = PickAt(hsv, m_PickerDrag, svR, hueR, e.mousePosition);
                if (nh != hsv)
                {
                    hsv = nh;
                    ApplyPick(s, hsv, false);
                    if (Time.unscaledTime - m_LastSlideSound > 0.045f)
                    {
                        m_LastSlideSound = Time.unscaledTime;
                        Sfx.PlayUi(Sfx.UiSlide, 0.5f, 0.8f + 0.7f * (m_PickerDrag == 1 ? hsv.z : hsv.x));
                    }
                }
                e.Use();
            }
            else if ((e.type == EventType.MouseUp && m_PickerDrag != 0) || (e.type == EventType.Repaint && m_PickerDrag != 0 && !PickerMouseHeld))
            {
                // let go (also if it was let go outside the window): saved
                m_PickerDrag = 0;
                SavePick();
                if (e.type == EventType.MouseUp) e.Use();
            }
            if (area.Contains(e.mousePosition)) MouseOverUI = true;

            var now = teamHands ? shown : s.Value;
            if (e.type == EventType.Repaint)
            {
                Fill(area, new Color(0f, 0f, 0f, 0.42f));
                PickerTextures(hsv.x);
                Fill(new Rect(svR.x - 1, svR.y - 1, svR.width + 2, svR.height + 2), new Color(0.75f, 0.75f, 0.75f));
                GUI.DrawTexture(svR, m_SvTex, ScaleMode.StretchToFill, false);
                Fill(new Rect(hueR.x - 1, hueR.y - 1, hueR.width + 2, hueR.height + 2), new Color(0.75f, 0.75f, 0.75f));
                GUI.DrawTexture(hueR, m_HueTex, ScaleMode.StretchToFill, false);
                // where the colour is: a ring in the square, a bar across the strip
                var p = new Vector2(svR.x + hsv.y * sq, svR.y + (1f - hsv.z) * sq);
                float r = Mathf.Round(6 * k), t = Mathf.Max(1f, Mathf.Round(1.5f * k));
                Frame(new Rect(p.x - r - t, p.y - r - t, 2 * (r + t), 2 * (r + t)), t, Color.black);
                Frame(new Rect(p.x - r, p.y - r, 2 * r, 2 * r), t, Color.white);
                float hy = hueR.y + hsv.x * sq;
                Frame(new Rect(hueR.x - 3 * t, hy - 3 * t, hueR.width + 6 * t, 6 * t), t, Color.black);
                Frame(new Rect(hueR.x - 2 * t, hy - 2 * t, hueR.width + 4 * t, 4 * t), t, Color.white);
            }
            // before / now, the numbers, Revert and Close
            float lineH = Mathf.Round(20 * k), sw = Mathf.Min(info.width, 220 * k) * 0.5f;
            GUI.Label(new Rect(info.x, info.y, sw, lineH), "<color=#bbbbbb>before</color>", m_Small);
            GUI.Label(new Rect(info.x + sw, info.y, sw, lineH), "<color=#bbbbbb>now</color>", m_Small);
            var oldR = new Rect(info.x, info.y + lineH + 2 * k, sw, Mathf.Round(38 * k));
            var newR = new Rect(oldR.xMax, oldR.y, sw, oldR.height);
            Fill(new Rect(oldR.x - 2, oldR.y - 2, oldR.width * 2 + 4, oldR.height + 4), new Color(0.8f, 0.8f, 0.8f));
            Fill(oldR, m_PickerOld);
            Fill(newR, now);
            GUI.Label(new Rect(info.x, oldR.yMax + 6 * k, info.width, lineH * 1.2f), $"<b>#{ColorUtility.ToHtmlStringRGB(now)}</b>    <color=#cccccc>H {hsv.x * 360f:0}°  S {hsv.y * 100f:0}%  B {hsv.z * 100f:0}%</color>", m_Small);
            float bw = (Mathf.Min(info.width, 220 * k) - 6 * k) * 0.5f, bh = Mathf.Round(30 * k);
            // the hint fills what's left between the numbers and the buttons (never over them)
            float hy0 = oldR.yMax + 6 * k + lineH * 1.3f;
            GUI.BeginGroup(new Rect(info.x, hy0, info.width, Mathf.Max(0f, info.yMax - bh - 4 * k - hy0)));
            GUI.Label(new Rect(0, 0, info.width, info.yMax - bh - 4 * k - hy0),
                "<color=#aaaaaa>Drag in the square or the hue strip. Saved when you let go.</color>", m_SmallWrap);
            GUI.EndGroup();
            if (BtnAt(new Rect(info.x, info.yMax - bh, bw, bh), "Revert", m_Button))
            {
                // back to the colour it had when the picker opened (the hands: their team colour again, if they had it)
                ColorSlots.Set(s, m_PickerOldValue, true);
                if (m_PickerOldTeam) ColorSlots.SetHandsTeam(true);
                m_PickerDirty = false;
            }
            if (BtnAt(new Rect(info.x + bw + 6 * k, info.yMax - bh, bw, bh), "Close", m_Button)) ClosePicker();
        }

        static void Frame(Rect r, float t, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // ------------------------------------------------------------------ display: post processing

        void DrawPostFxSettings()
        {
            float k = m_Scale;
            Caption("POST PROCESSING  ·  Normal graphics, just on this PC");
            bool on = GameSettings.PostFx, bloom = GameSettings.PostBloom, vig = GameSettings.PostVignette, grade = GameSettings.PostGrading;
            float bs = GameSettings.PostBloomStrength, vs = GameSettings.PostVignetteStrength, gs = GameSettings.PostGradingStrength;
            GUILayout.BeginHorizontal();
            RowLabel("Post processing", 210 * k);
            on = ToggleBtn(on, on ? "On" : "Off", GUILayout.Width(110 * k), GUILayout.Height(30 * k));
            if (GameSettings.GraphicsMode != 0) GUILayout.Label("<color=#bbbbbb>   (not in PSX / AI PSX)</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            bool defaults = Btn("Defaults", GUILayout.Width(110 * k), GUILayout.Height(30 * k));
            if (defaults) { on = bloom = vig = grade = true; bs = vs = gs = 0.5f; }
            GUILayout.EndHorizontal();
            if (on)
            {
                bloom = EffectRow("Bloom", bloom, ref bs);
                vig = EffectRow("Vignette", vig, ref vs);
                grade = EffectRow("Colour grading", grade, ref gs);
            }
            GameSettings.SetPostFx(on, bloom, vig, grade, Mathf.Round(bs * 20f) / 20f, Mathf.Round(vs * 20f) / 20f, Mathf.Round(gs * 20f) / 20f);
            GUILayout.Label("<color=#bbbbbb>Bloom: a soft glow round the brightest things (the sun, sky, sparks). Vignette: slightly darker corners. Colour grading: a little more colour and contrast (same hues). Off is exactly the plain look (and a little faster).</color>", m_SmallWrap);
            // the UI too: the menus, HUD, icons and inventory go under the post processing as well (off to start with)
            GUILayout.BeginHorizontal();
            RowLabel("On the UI too", 210 * k);
            GUI.enabled = on;
            bool ui = ToggleBtn(GameSettings.PostOnUi, GameSettings.PostOnUi ? "On" : "Off", GUILayout.Width(110 * k), GUILayout.Height(30 * k));
            GUI.enabled = true;
            if (defaults) ui = false;
            GameSettings.SetPostOnUi(ui);
            GUILayout.Label("<color=#bbbbbb>  menus, HUD, icons and inventory</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            // the extra looks: each off to start with (Defaults switches them all off again)
            if (on)
            {
                GUILayout.Space(4 * k);
                GUILayout.Label("<color=#9ab8d8><b>EXTRA LOOKS</b></color>  <color=#bbbbbb>off to start with · each costs a little more on a laptop</color>", m_SmallWrap);
                for (int i = 0; i < GameSettings.PostExtraCount; i++)
                {
                    var e = (GameSettings.PostExtra)i;
                    bool x = GameSettings.PostExtraOn(e);
                    float xs = GameSettings.PostExtraStrength(e);
                    x = EffectRow(GameSettings.PostExtraNames[i], x, ref xs);
                    if (defaults) { x = false; xs = 0.5f; }
                    GameSettings.SetPostExtra(e, x, Mathf.Round(xs * 20f) / 20f);
                }
                GUILayout.Label("<color=#bbbbbb>Outlines: dark ink lines round things and along sharp folds, fading with distance. Ambient occlusion: deeper soft shadows in corners, creases and under things. Distance haze: far things fade into a pale sky colour. Depth of field: far away goes softly out of focus. Film grain: fine animated noise. Chromatic aberration: a hint of colour fringing towards the edges. Sharpen: crisper edges. Cel banding: the light falls in a few flat steps, like a cartoon.</color>", m_SmallWrap);
            }
            else if (defaults) for (int i = 0; i < GameSettings.PostExtraCount; i++) GameSettings.SetPostExtra((GameSettings.PostExtra)i, false, 0.5f);
        }

        /// <summary>"[Effect on/off]  [----o----]  50%".</summary>
        bool EffectRow(string name, bool on, ref float strength)
        {
            float k = m_Scale;
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            on = ToggleBtn(on, name, GUILayout.Width(214 * k), GUILayout.Height(28 * k));
            GUILayout.Space(10 * k);
            GUI.enabled = on;
            GUILayout.BeginVertical();
            GUILayout.Space(8 * k);
            float nv = GUILayout.HorizontalSlider(strength, 0f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(22 * k));
            TrackHover(GUILayoutUtility.GetLastRect());
            GUILayout.EndVertical();
            GUI.enabled = true;
            GUILayout.Label(on ? $"{nv * 100f:0}%" : "<color=#888888>off</color>", new GUIStyle(m_Label) { alignment = TextAnchor.MiddleRight }, GUILayout.Width(72 * k), GUILayout.Height(28 * k));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(nv, strength))
            {
                if (Time.unscaledTime - m_LastSlideSound > 0.035f) { m_LastSlideSound = Time.unscaledTime; Sfx.PlayUi(Sfx.UiSlide, 0.7f, 0.8f + 0.7f * nv); }
                strength = nv;
            }
            return on;
        }
    }
}
