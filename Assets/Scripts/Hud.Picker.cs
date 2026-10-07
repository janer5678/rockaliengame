using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > World colours: the colour picker for a world colour (and the first-person hands) - a saturation /
    /// brightness square and a hue strip (textures made here) dragged with the mouse, the colour before and now side by
    /// side, Revert and Close. The colour shows on everything while you drag and is saved when you let go. Also the POST
    /// PROCESSING rows (their extra looks, and the UI's own looks: cel shading, glow, outlines, saturation, contrast) in
    /// Settings > Display.
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
            if (defaults)
            {
                on = DisplayDefaults.PostFx; bloom = DisplayDefaults.Bloom; vig = DisplayDefaults.Vignette; grade = DisplayDefaults.Grading;
                bs = DisplayDefaults.BloomStrength; vs = DisplayDefaults.VignetteStrength; gs = DisplayDefaults.GradingStrength;
            }
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
            if (defaults) { ui = DisplayDefaults.PostOnUi; GameSettings.ResetUiPost(); }
            GameSettings.SetPostOnUi(ui);
            GUILayout.Label("<color=#bbbbbb>  menus, HUD, icons and inventory</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (on) DrawUiPostSettings();
            // the extra looks: each with its own default (outlines, haze and cel banding on; Defaults puts them back)
            if (on)
            {
                GUILayout.Space(4 * k);
                GUILayout.Label("<color=#9ab8d8><b>EXTRA LOOKS</b></color>  <color=#bbbbbb>each costs a little more on a laptop</color>", m_SmallWrap);
                for (int i = 0; i < GameSettings.PostExtraCount; i++)
                {
                    var e = (GameSettings.PostExtra)i;
                    bool x = GameSettings.PostExtraOn(e);
                    float xs = GameSettings.PostExtraStrength(e);
                    x = EffectRow(GameSettings.PostExtraNames[i], x, ref xs);
                    if (defaults) { x = DisplayDefaults.PostExtraOn(i); xs = DisplayDefaults.PostExtraStrength(i); }
                    GameSettings.SetPostExtra(e, x, Mathf.Round(xs * 20f) / 20f);
                }
                GUILayout.Label("<color=#bbbbbb>Outlines: dark ink lines round things and along sharp folds, fading with distance. Ambient occlusion: deeper soft shadows in corners, creases and under things. Distance haze: far things fade into a pale sky colour. Depth of field: far away goes softly out of focus. Film grain: fine animated noise. Chromatic aberration: a hint of colour fringing towards the edges. Sharpen: crisper edges. Cel banding: the light falls in a few flat steps, like a cartoon.</color>", m_SmallWrap);
            }
            else if (defaults) for (int i = 0; i < GameSettings.PostExtraCount; i++) GameSettings.SetPostExtra((GameSettings.PostExtra)i, DisplayDefaults.PostExtraOn(i), DisplayDefaults.PostExtraStrength(i));
            if (defaults) { GameSettings.ResetHandsLook(); GameSettings.ResetNotifLook(); GameSettings.ResetMenuPost(); GameSettings.ResetTimer(); GameSettings.ResetLobby(); }
            if (m_DisplayTabbed)
            {
                // (in categories: just the hands here - the notifications, the menu cutscene, the timer and the lobby
                // each have a category of their own: Hud.Settings.cs)
                if (on) { GUILayout.Space(6 * k); DrawHandsLooks(); }
                return;
            }
            if (on) DrawLayerLooks();
            DrawTimerAndLobbyLooks(); // (LobbyLooks.cs: the lobby's post processing only shows while post processing is on)
        }

        bool m_HandsLooksOpen, m_NotifLooksOpen, m_MenuLooksOpen;
        /// <summary>(tests) fold the hands' and the notifications' own looks open in Settings > Display.</summary>
        public static void OpenLayerLooks(bool open) { if (s_I != null) { s_I.m_HandsLooksOpen = open; s_I.m_NotifLooksOpen = open; } }

        /// <summary>HANDS AND TOOLS and NOTIFICATIONS: looks of their own, apart from the world's and the rest of the UI's
        /// (LayerLooks.cs). Each folded away, and off (= the same as everything else) to start with.</summary>
        void DrawLayerLooks()
        {
            GUILayout.Space(6 * m_Scale);
            DrawHandsLooks();
            DrawMenuCutsceneLooks();
            DrawNotifOwnLooks();
        }

        /// <summary>HANDS AND TOOLS: their own outlines, cel shading and colour.</summary>
        void DrawHandsLooks()
        {
            float k = m_Scale, lw = 210 * k;
            // ---- hands and tools ----
            if (FoldRow("HANDS AND TOOLS", ref m_HandsLooksOpen, GameSettings.HandsOwn))
            {
                float os = GameSettings.HandsOutlineStrength.Value;
                bool ol = EffectRow("Outlines", GameSettings.HandsOutline.Value, ref os);
                GameSettings.HandsOutline.Set(ol); GameSettings.HandsOutlineStrength.Set(Mathf.Round(os * 20f) / 20f);
                float cs = GameSettings.HandsCelStrength.Value;
                bool cel = EffectRow("Cel shading", GameSettings.HandsCel.Value, ref cs);
                GameSettings.HandsCel.Set(cel); GameSettings.HandsCelStrength.Set(Mathf.Round(cs * 20f) / 20f);
                ColourRows(GameSettings.HandsSaturation, GameSettings.HandsContrast, lw);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#bbbbbb>Your first-person hands and whatever they hold, with their own ink outlines, cel shading and colour instead of the world's.</color>", m_SmallWrap);
                if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetHandsLook();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>MAIN MENU CUTSCENE: its own post processing.</summary>
        void DrawMenuCutsceneLooks()
        {
            float k = m_Scale;
            // ---- the main menu's cutscene ----
            if (FoldRow("MAIN MENU CUTSCENE", ref m_MenuLooksOpen, GameSettings.MenuOwn))
            {
                void Row(string name, DisplayPref.Bool on, DisplayPref.Float s)
                {
                    float v = s.Value;
                    bool o = EffectRow(name, on.Value, ref v);
                    on.Set(o); s.Set(Mathf.Round(v * 20f) / 20f);
                }
                Row("Bloom", GameSettings.MenuBloom, GameSettings.MenuBloomStrength);
                Row("Vignette", GameSettings.MenuVignette, GameSettings.MenuVignetteStrength);
                Row("Colour grading", GameSettings.MenuGrading, GameSettings.MenuGradingStrength);
                Row("Outlines", GameSettings.MenuOutlines, GameSettings.MenuOutlinesStrength);
                Row("Cel banding", GameSettings.MenuCel, GameSettings.MenuCelStrength);
                Row("Film grain", GameSettings.MenuGrain, GameSettings.MenuGrainStrength);
                Row("Chromatic aberration", GameSettings.MenuChromatic, GameSettings.MenuChromaticStrength);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#bbbbbb>The post processing on the main menu's UFO cutscene only - the game keeps the settings above.</color>", m_SmallWrap);
                if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetMenuPost();
                GUILayout.EndHorizontal();
            }
            // its colours (Hud.MenuColours.cs): whenever the section is open - they don't need the own look switched on
            if (m_MenuLooksOpen) DrawMenuCutsceneColours();
        }

        /// <summary>NOTIFICATIONS: their own post processing layer (cel shading, glow, outlines, colour).</summary>
        void DrawNotifOwnLooks()
        {
            float k = m_Scale, lw = 210 * k;
            // ---- notifications ----
            if (FoldRow("NOTIFICATIONS", ref m_NotifLooksOpen, GameSettings.NotifOwn))
            {
                float cs = GameSettings.NotifCelStrength.Value;
                bool cel = EffectRow("Cel shading", GameSettings.NotifCel.Value, ref cs);
                GameSettings.NotifCel.Set(cel); GameSettings.NotifCelStrength.Set(Mathf.Round(cs * 20f) / 20f);
                float bs = GameSettings.NotifBloomStrength.Value;
                bool glow = EffectRow("Glow", GameSettings.NotifBloom.Value, ref bs);
                GameSettings.NotifBloom.Set(glow); GameSettings.NotifBloomStrength.Set(Mathf.Round(bs * 20f) / 20f);
                float wd01 = Mathf.InverseLerp(GameSettings.UiOutlineWidthMin, GameSettings.UiOutlineWidthMax, GameSettings.NotifOutlineWidth.Value);
                bool ink = EffectRow("Extra outline (post)", GameSettings.NotifOutline.Value, ref wd01); // (not the TEXT OUTLINE: that is the words' own stroke)
                float wd = Mathf.Round(Mathf.Lerp(GameSettings.UiOutlineWidthMin, GameSettings.UiOutlineWidthMax, wd01) * 2f) / 2f;
                GameSettings.NotifOutline.Set(ink); GameSettings.NotifOutlineWidth.Set(wd);
                if (ink)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(32 * k);
                    GUILayout.Label($"<color=#bbbbbb>{wd * 4f / 3f:0.#} px · colour</color>", m_Small, GUILayout.Width(110 * k), GUILayout.Height(26 * k));
                    float sw = 22 * k;
                    // the colour now (double-click: the colour wheel), then the presets
                    var nowR = GUILayoutUtility.GetRect(40 * k, sw, GUILayout.Width(40 * k), GUILayout.Height(26 * k));
                    nowR.y += (26 * k - sw) * 0.5f; nowR.height = sw;
                    ColourSwatch(nowR, GameSettings.NotifOutlineColour, "Notifications' post outline");
                    GUILayout.Space(6 * k);
                    foreach (var pc in s_InkPresets)
                    {
                        var pr = GUILayoutUtility.GetRect(sw, sw, GUILayout.Width(sw), GUILayout.Height(26 * k));
                        pr.y += (26 * k - sw) * 0.5f; pr.height = sw;
                        PresetSwatch(pr, pc, GameSettings.NotifOutlineColour, "Notifications' post outline");
                        GUILayout.Space(3 * k);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(16 * k);
                    float op = SliderRow("Outline opacity", GameSettings.NotifOutlineOpacity.Value, 0f, 1f, $"{GameSettings.NotifOutlineOpacity.Value * 100f:0}%", lw - 16 * k);
                    GameSettings.NotifOutlineOpacity.Set(Mathf.Round(op * 20f) / 20f);
                    GUILayout.EndHorizontal();
                }
                ColourRows(GameSettings.NotifSaturation, GameSettings.NotifContrast, lw);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#bbbbbb>The big messages in the middle of the screen - TRADE STATION UNLOCKED, AIRDROP INCOMING, the countdowns - with their own look, laid on after the world's post processing.</color>", m_SmallWrap);
                if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetNotifLook();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>A section's fold button and its "Own look" switch on one row; true while it's open and switched on.</summary>
        bool FoldRow(string title, ref bool open, DisplayPref.Bool own)
        {
            float k = m_Scale;
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            if (Btn($"{title}  {(open ? "▲" : "▼")}", GUILayout.Width(260 * k), GUILayout.Height(28 * k))) open = !open;
            GUILayout.Space(10 * k);
            bool on = ToggleBtn(own.Value, own.Value ? "Own look: on" : "Own look: off", GUILayout.Width(150 * k), GUILayout.Height(28 * k));
            own.Set(on);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!open) return false;
            if (!on) { Hint("Off: they look like everything else. Switch Own look on to set them apart."); return false; }
            return true;
        }

        /// <summary>A saturation and a contrast slider (100% = as drawn).</summary>
        void ColourRows(DisplayPref.Float saturation, DisplayPref.Float contrast, float lw)
        {
            float k = m_Scale;
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            float sat = SliderRow("Saturation", saturation.Value, 0f, 2f, $"{saturation.Value * 100f:0}%", lw - 16 * k);
            saturation.Set(Mathf.Round(sat * 20f) / 20f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            float con = SliderRow("Contrast", contrast.Value, 0.5f, 1.6f, $"{contrast.Value * 100f:0}%", lw - 16 * k);
            contrast.Set(Mathf.Round(con * 20f) / 20f);
            GUILayout.EndHorizontal();
        }

        static readonly Color[] s_InkPresets =
        {
            Color.black, Color.white, new Color(0.1f, 0.13f, 0.22f), new Color(0.23f, 0.14f, 0.08f), new Color(0.2f, 0.45f, 0.25f), new Color(0.55f, 0.1f, 0.1f),
        };
        string m_InkHex;
        GUIStyle m_InkField;
        bool m_UiLooksOpen;
        /// <summary>(tests) fold the UI's own looks open in Settings > Display.</summary>
        public static void OpenUiLooks(bool open)
        {
            if (s_I == null) return;
            s_I.m_UiLooksOpen = open;
            if (open) s_I.m_DisplayCat = DisplayCat.PostFx; // (they're in the Post FX category)
        }
        /// <summary>(tests) scroll the settings window to this height.</summary>
        public static void SetSettingsScroll(float y) { if (s_I != null) s_I.m_SettingsScroll.y = y; }

        /// <summary>POST PROCESSING ON THE UI: the UI's own looks, apart from the world's (shown while "On the UI too" is on).</summary>
        void DrawUiPostSettings()
        {
            float k = m_Scale, lw = 210 * k;
            GUILayout.Space(4 * k);
            // folded away to start with (it only does anything while "On the UI too" is on)
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            if (Btn($"THE UI'S OWN LOOKS  {(m_UiLooksOpen ? "▲" : "▼")}", GUILayout.Width(260 * k), GUILayout.Height(28 * k))) m_UiLooksOpen = !m_UiLooksOpen;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("cel shading, outlines, glow, colour - just the menus and HUD (while On the UI too is on)");
            if (!m_UiLooksOpen) return;
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            bool world = ToggleBtn(GameSettings.UiWorldPost.Value, "World effects too", GUILayout.Width(214 * k), GUILayout.Height(28 * k));
            GameSettings.UiWorldPost.Set(world);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint(world ? "World effects too: the world's bloom, vignette, grading and extra looks go over the UI as well."
                : "World effects off: the UI goes on after the world's post processing - only its own looks below.");

            float cs = GameSettings.UiCelStrength.Value;
            bool cel = EffectRow("Cel shading", GameSettings.UiCel.Value, ref cs);
            GameSettings.UiCel.Set(cel); GameSettings.UiCelStrength.Set(Mathf.Round(cs * 20f) / 20f);

            float bs = GameSettings.UiBloomStrength.Value;
            bool bloom = EffectRow("Glow", GameSettings.UiBloom.Value, ref bs);
            GameSettings.UiBloom.Set(bloom); GameSettings.UiBloomStrength.Set(Mathf.Round(bs * 20f) / 20f);

            // outlines: on / off with their thickness, then the colour and how solid it is
            float wd01 = Mathf.InverseLerp(GameSettings.UiOutlineWidthMin, GameSettings.UiOutlineWidthMax, GameSettings.UiOutlineWidth.Value);
            bool ink = EffectRow("Outlines", GameSettings.UiOutline.Value, ref wd01);
            float wd = Mathf.Round(Mathf.Lerp(GameSettings.UiOutlineWidthMin, GameSettings.UiOutlineWidthMax, wd01) * 2f) / 2f;
            GameSettings.UiOutline.Set(ink); GameSettings.UiOutlineWidth.Set(wd);
            if (ink)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(32 * k);
                // (the thickness in pixels at 1440p; other resolutions scale it with the screen height)
                GUILayout.Label($"<color=#bbbbbb>{wd * 4f / 3f:0.#} px · colour</color>", m_Small, GUILayout.Width(110 * k), GUILayout.Height(26 * k));
                var cur = GameSettings.UiOutlineColour.Value;
                float sw = 22 * k;
                foreach (var pc in s_InkPresets)
                {
                    var pr = GUILayoutUtility.GetRect(sw, sw, GUILayout.Width(sw), GUILayout.Height(26 * k));
                    pr.y += (26 * k - sw) * 0.5f; pr.height = sw;
                    if (PresetSwatch(pr, pc, GameSettings.UiOutlineColour, "The UI's outline")) m_InkHex = null; // (double-click: the colour wheel)
                    GUILayout.Space(3 * k);
                }
                GUILayout.Space(6 * k);
                if (m_InkField == null) m_InkField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.MiddleCenter };
                if (GUIUtility.keyboardControl == 0) m_InkHex = null;
                string hex = m_InkHex ?? ColorUtility.ToHtmlStringRGB(cur);
                GUI.SetNextControlName("inkhex");
                string nh = GUILayout.TextField(hex, 7, m_InkField, GUILayout.Width(86 * k), GUILayout.Height(24 * k));
                NoteTyping("inkhex");
                if (nh != hex)
                {
                    m_InkHex = nh;
                    var t = nh.Trim().TrimStart('#');
                    if (t.Length == 6 && ColorUtility.TryParseHtmlString("#" + t, out var hc)) GameSettings.UiOutlineColour.Set(hc);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                float op = SliderRow("Outline opacity", GameSettings.UiOutlineOpacity.Value, 0f, 1f, $"{GameSettings.UiOutlineOpacity.Value * 100f:0}%", lw - 16 * k);
                GameSettings.UiOutlineOpacity.Set(Mathf.Round(op * 20f) / 20f);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            float sat = SliderRow("UI saturation", GameSettings.UiSaturation.Value, 0f, 2f, $"{GameSettings.UiSaturation.Value * 100f:0}%", lw - 16 * k);
            GameSettings.UiSaturation.Set(Mathf.Round(sat * 20f) / 20f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            float con = SliderRow("UI contrast", GameSettings.UiContrast.Value, 0.5f, 1.6f, $"{GameSettings.UiContrast.Value * 100f:0}%", lw - 16 * k);
            GameSettings.UiContrast.Set(Mathf.Round(con * 20f) / 20f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>Cel shading: the UI's colours in a few flat steps. Glow: a soft glow round the bright text and icons. Outlines: ink lines round text, icons and panels - make them thick for a cartoon look (the thickness is in pixels at 1440p; other resolutions scale it with the screen height, so it looks the same everywhere).</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetUiPost();
            GUILayout.EndHorizontal();
        }

        /// <summary>A grey note on its own line, indented under a row.</summary>
        void Hint(string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20 * m_Scale);
            GUILayout.Label($"<color=#bbbbbb>{text}</color>", m_SmallWrap);
            GUILayout.EndHorizontal();
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
