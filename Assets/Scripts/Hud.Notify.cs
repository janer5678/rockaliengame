using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The big moments on screen, drawn to feel like events:
    /// - THE END COUNTDOWN (the match's last ten seconds, "YOU WIN IN" / "OVERTIME IN"): each second the number slams in
    ///   big and white-hot, springs and wobbles as it settles into the colour, a soft glow bursts out from
    ///   behind it, and a row of pips under it ticks down; the label sits on a dark band with an accent line that fills
    ///   through each second. The last three hit harder and redder.
    /// - THE BANNERS (TRADE STATION UNLOCKED, AIRDROP INCOMING, the wall dropping...): a dark band snaps open across the
    ///   middle from the centre, accent lines race out along its edges, the title drops in big and settles with a bounce,
    ///   a glint sweeps across it, and at the end it all lifts and fades away. Each kind of news has its own accent colour.
    ///   (Settings > Display > NOTIFICATIONS: the way in / out, how long, how far up, each kind's colour - NotifLooks.cs;
    ///   the countdowns' own look - CountdownLooks.cs; the timer's bar, tag and words - TimerLooks.cs.)
    /// </summary>
    public partial class Hud
    {
        static Texture2D s_RingTex, s_GlowTex, s_BandTex, s_GlintTex;

        static void EnsureNotifyTextures()
        {
            if (s_RingTex != null) return;
            const int S = 128;
            s_RingTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            s_GlowTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var ring = new Color32[S * S];
            var glow = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float r = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.07f);
                    ring[y * S + x] = new Color32(255, 255, 255, (byte)(r * r * 255));
                    float g = Mathf.Clamp01(1f - d);
                    glow[y * S + x] = new Color32(255, 255, 255, (byte)(g * g * 255));
                }
            s_RingTex.SetPixels32(ring); s_RingTex.Apply();
            s_GlowTex.SetPixels32(glow); s_GlowTex.Apply();
            // a band that fades out at both ends, and a soft vertical glint
            const int W = 256;
            s_BandTex = new Texture2D(W, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            s_GlintTex = new Texture2D(32, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var band = new Color32[W];
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, a = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 5f);
                band[x] = new Color32(255, 255, 255, (byte)(a * a * (3f - 2f * a) * 255));
            }
            var glint = new Color32[32];
            for (int x = 0; x < 32; x++) { float u = (x + 0.5f) / 32f, a = Mathf.Sin(u * Mathf.PI); glint[x] = new Color32(255, 255, 255, (byte)(a * a * 255)); }
            s_BandTex.SetPixels32(band); s_BandTex.Apply();
            s_GlintTex.SetPixels32(glint); s_GlintTex.Apply();
        }

        /// <summary>Text with a thick ink edge round it (drawn round it twelve times, then the text on top).</summary>
        void InkText(Rect r, string text, GUIStyle st, Color fill, Color edge, float px)
        {
            var old = st.normal.textColor;
            string plain = s_ColorTag.Replace(text, "");
            st.normal.textColor = edge;
            for (int i = 0; i < 12 && px > 0.05f; i++) // (no edge at all at 0: Settings > Display, outline thickness)
            {
                float a = i * Mathf.PI * 2f / 12f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * px, r.y + Mathf.Sin(a) * px, r.width, r.height), plain, st);
            }
            st.normal.textColor = fill;
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }

        static void Tinted(Rect r, Texture t, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        /// <summary>The end countdown's second `n` (frac: how much of it is left, 1 at its start).</summary>
        void DrawEndCountdown(int n, float frac, string label, bool overtime, float k)
        {
            EnsureNotifyTextures();
            float sw = Screen.width, sh = Screen.height;
            float t = Mathf.Clamp01(1f - frac);                                   // time into this second (0..1)
            bool hard = n <= 3;
            var accent = overtime ? GameSettings.CountColourOver.Value : hard ? GameSettings.CountColourHard.Value : GameSettings.CountColour.Value;
            // (Settings > Display > NOTIFICATIONS > COUNTDOWN: its own size, height, band, font, outline, colours -
            // CountdownLooks.cs; the banners' STYLE doesn't touch it)
            float zs = GameSettings.CountSize.Value, ink = GameSettings.CountInkNow, dy = GameSettings.CountY.Value * k;
            var font = GameSettings.CountFontNow;
            CountInkPx = 2.5f * k * ink; // (tests)
            label = NotifText.Map(label);
            float cx = sw / 2f, cy = sh * 0.24f + 110 * k * zs + dy;
            // the glow and the shockwave behind the number
            float gs = (hard ? 520f : 420f) * k * zs * (0.9f + 0.25f * Mathf.Exp(-5f * t));
            if (GameSettings.CountGlow.Value) Tinted(new Rect(cx - gs / 2f, cy - gs / 2f, gs, gs), s_GlowTex, new Color(accent.r, accent.g, accent.b, 0.4f * (1f - t) + 0.08f));
            // (no rings round it)
            // the label on its band, the accent line under it filling through the second
            float bw = 560f * k * GameSettings.CountWidth.Value, bh = 54 * k * zs, by = sh * 0.2f - 6 * k + dy;
            Tinted(new Rect(cx - bw / 2f, by, bw, bh), s_BandTex, new Color(0f, 0f, 0f, Mathf.Clamp01(0.6f * GameSettings.CountPlate.Value)));
            Fill(new Rect(cx - bw * 0.35f, by + bh - 4 * k, bw * 0.7f * t, 3 * k), accent);
            var lst = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * k * zs), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            if (font != null) lst.font = font;
            InkText(new Rect(0, by, sw, bh), label, lst, GameSettings.CountLabelColour.Value, GameSettings.CountInkEdge(0.9f), 2.5f * k * ink);
            // the number: slammed in white-hot, springing and wobbling as it settles into the colour
            float spring = 1f + (hard ? 0.6f : 0.42f) * Mathf.Exp(-7f * t) * Mathf.Cos(t * 22f);
            float size = (hard ? 200f : 165f) * k * zs * spring;
            var nst = new GUIStyle(m_Big) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size)), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            if (font != null) nst.font = font;
            var fill = Color.Lerp(Color.white, accent, Mathf.Clamp01(t * 4f));
            fill.a = 0.75f + 0.25f * frac;
            float wob = (hard ? 9f : 6f) * Mathf.Exp(-6f * t) * Mathf.Sin(t * 26f);
            var mat = GUI.matrix;
            GUIUtility.RotateAroundPivot(wob, new Vector2(cx, cy));
            InkText(new Rect(cx - 300 * k * zs, cy - size * 0.6f, 600 * k * zs, size * 1.2f), n.ToString(), nst, fill, GameSettings.CountInkEdge(0.85f), (hard ? 6f : 5f) * k * ink);
            GUI.matrix = mat;
            // the pips: one per second left
            if (!GameSettings.CountPips.Value) return;
            float pip = 12 * k, gap = 7 * k, px0 = cx - (10 * pip + 9 * gap) / 2f, py = cy + 120 * k * zs;
            for (int i = 0; i < 10; i++)
            {
                bool on = i < n;
                var pr = new Rect(px0 + i * (pip + gap), py, pip, pip);
                Fill(new Rect(pr.x - 1, pr.y - 1, pr.width + 2, pr.height + 2), new Color(0f, 0f, 0f, 0.6f));
                Fill(pr, on ? (i == n - 1 ? Color.Lerp(Color.white, accent, t) : accent) : new Color(1f, 1f, 1f, 0.12f));
            }
        }

        /// <summary>The sudden death arena's countdown word (Ready?, Set, ROCK!) `t` (0..1) through it: each pops in big and
        /// settles; ROCK! fades away as the duel starts. (Settings > Display > NOTIFICATIONS > COUNTDOWN: its size, height,
        /// font, outline and colours - CountdownLooks.cs)
        /// `title`: the line over it (null: none; the match start's READY / SET / ROCK! can use this too).</summary>
        void DrawFightWord(string word, float t, float k, string title = "SUDDEN DEATH")
        {
            float sw = Screen.width, sh = Screen.height;
            string w = (word ?? "").ToUpperInvariant();
            int n = w.StartsWith("READY") ? 2 : w.StartsWith("SET") ? 1 : 0;
            float zs = GameSettings.CountSize.Value, dy = GameSettings.CountY.Value * k, ink = GameSettings.CountInkNow;
            var font = GameSettings.CountFontNow;
            float pop = 1f + 0.35f * Mathf.Clamp01(1f - t * 4f);
            if (n > 0 && GameSettings.CountDim.Value) Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.25f));
            var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(34 * k * zs), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            if (font != null) st.font = font;
            var tc = GameSettings.CountColTitle.Value;
            tc.a = n > 0 ? 1f : 1f - t;
            if (title != null) InkText(new Rect(0, sh * 0.2f + dy, sw, 44 * k * zs), NotifText.MapTimer(title), st, tc, GameSettings.CountInkEdge(0.85f * tc.a), 2f * k * ink);
            var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt((n > 0 ? 170f : 200f) * pop * k * zs), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            if (font != null) big.font = font;
            var c = n == 2 ? GameSettings.CountColReady.Value : n == 1 ? GameSettings.CountColSet.Value : GameSettings.CountColRock.Value;
            c.a = n == 2 ? 0.95f : n == 1 ? 0.95f : 1f - t;
            InkText(new Rect(0, sh * 0.24f + dy, sw, 300 * k * zs), word, big, c, GameSettings.CountInkEdge(0.85f * c.a), 4f * k * ink);
            FightWordShownFrame = Time.frameCount; FightWordShown = word; FightWordColour = c; CountInkPx = 4f * k * ink; // (tests)
        }

        // the top panel's memory: which phase it's showing, how long that phase was, the last whole second shown
        /// <summary>(tests) the top panel as last drawn: the frame, its accent colour, whether its label sat above the clock.</summary>
        public static int TopPanelFrame = -10;
        public static Color TopPanelAccent;
        public static bool TopPanelLabelAbove;
        /// <summary>(tests) the top panel's drain bar was last drawn over the plate (TIMER > Drain bar: On top).</summary>
        public static bool TopPanelBarOnTop;
        /// <summary>(tests) where the mode's tag and the drain bar's coloured part were last drawn (an empty rect: no tag).</summary>
        public static Rect TopPanelTagRect, TopPanelBarFill;

        static Texture2D s_CapTex;
        /// <summary>A piece of the drain bar: a plain rectangle, or with round ends (half discs either side of a rectangle -
        /// no overlap, so a see-through track stays even).</summary>
        static void BarPiece(Rect r, Color c, bool round)
        {
            if (!round || r.height < 2f) { Fill(r, c); return; }
            if (s_CapTex == null)
            {
                const int S = 64;
                s_CapTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                        float a = Mathf.Clamp01(S / 2f - Mathf.Sqrt(dx * dx + dy * dy));
                        px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                s_CapTex.SetPixels32(px); s_CapTex.Apply();
            }
            float h = r.height, rad = Mathf.Min(h / 2f, r.width / 2f);
            var old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, rad, h), s_CapTex, new Rect(0f, 0f, 0.5f, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - rad, r.y, rad, h), s_CapTex, new Rect(0.5f, 0f, 0.5f, 1f));
            GUI.color = old;
            if (r.width > rad * 2f) Fill(new Rect(r.x + rad, r.y, r.width - rad * 2f, h), c);
        }

        GameState m_TopState = (GameState)255;
        float m_TopTotal = 1f, m_TopSecondAt = -10f;
        int m_TopSecond = -1;

        /// <summary>
        /// The top-centre header in a match: a dark plate with the phase's accent along it - its label ("WALL DROPS IN",
        /// "TIME LEFT", "SUDDEN DEATH"...), the clock in inked digits, the mode on a tag the same size as the label - a bar
        /// under it draining in from both ends to the middle through the phase, and the line about what to do under that.
        /// Calm until the phase's final minute: only then does the clock pop each second and the clock and bar pulse red.
        /// OVERTIME is loud: the plate throbs red, the word shakes and glows, the edges flash. Settings > Display > TIMER
        /// sizes and trims it (LobbyLooks.cs). Returns how tall it is (the mode chips go under it).
        /// </summary>
        float DrawTopPanel(NetGame game, string label, string clock, string tag, string sub, Color accent, float left, float k)
        {
            EnsureNotifyTextures();
            float baseK = k;
            k *= 0.82f * GameSettings.TimerSizeNow; // (smaller than it was)
            float sw = Screen.width, cx = sw / 2f, y = 8f + GameSettings.TimerYNow * baseK; // (Settings > Display > TIMER: height on screen)
            float ink = GameSettings.TimerInkNow;
            var font = GameSettings.TimerFontNow;
            if (!GameSettings.TimerAccentNow) accent = new Color(0.92f, 0.92f, 0.92f);
            bool overtime = game != null && game.S == GameState.BallLive && game.Overtime.Value;
            if (overtime) accent = GameSettings.TimerAccentNow ? GameSettings.TimerColourNow(GameSettings.TimerColOvertime) : new Color(1f, 0.22f, 0.15f);
            // the phase's length (for the bar): what the clock read when the phase began
            var st = game != null ? game.S : GameState.Waiting;
            if (st != m_TopState) { m_TopState = st; m_TopTotal = Mathf.Max(1f, left); }
            if (left > m_TopTotal) m_TopTotal = left;
            int sec = left >= 0f ? Mathf.CeilToInt(left) : -1;
            if (sec != m_TopSecond) { m_TopSecond = sec; m_TopSecondAt = Time.time; }
            bool timed = game != null && (game.S == GameState.PreBall || game.S == GameState.BallLive || game.S == GameState.SuddenDeath);
            bool final = timed && left >= 0f && left <= 60f && GameSettings.TimerFlashNow; // (only the final minute flashes)
            float pulse = final ? 0.5f + 0.5f * Mathf.Sin(Time.time * 7f) : 0f;
            float beat = overtime ? Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * 3.4f)), 6f) : 0f; // (a heartbeat)
            bool hasClock = !string.IsNullOrEmpty(clock);
            // the label above the clock (Settings > Display > TIMER) makes the plate a little taller
            bool above = hasClock && GameSettings.TimerLabelAboveNow;
            TopPanelFrame = Time.frameCount; TopPanelAccent = accent; TopPanelLabelAbove = above; // (tests)
            float labelFont = 17 * k;
            float aboveH = above ? 22 * k : 0f;
            label = NotifText.MapTimer(label); // (its words: Settings > Display > NOTIFICATIONS > WORDING, the timer's own lines)
            // the mode's tag: right of the clock (as designed), under the bar, or above the label (TIMER > Game mode tag goes)
            bool hasTag = !string.IsNullOrEmpty(tag) && GameSettings.TimerTagNow;
            int tagPos = hasTag ? GameSettings.TimerTagPosNow : 0;
            var tst = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(labelFont), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            if (font != null) tst.font = font;
            float tw = hasTag ? tst.CalcSize(new GUIContent(tag)).x + 18 * k : 0f;
            void Tag(Rect tr)
            {
                Fill(tr, new Color(accent.r * 0.3f, accent.g * 0.3f, accent.b * 0.3f, 0.85f));
                Frame(tr, new Color(accent.r, accent.g, accent.b, 0.9f), 1.5f);
                GUI.Label(tr, $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(accent, Color.white, 0.4f))}>{tag}</color>", tst);
                TopPanelTagRect = tr; // (tests)
            }
            TopPanelTagRect = default;
            if (hasTag && tagPos == 2) { Tag(new Rect(cx - tw / 2f, y, tw, 26 * k)); y += 30 * k; }
            // the drain bar goes under the plate, or over it (TIMER > Drain bar: on top) - then the plate starts under it
            bool showBar = GameSettings.TimerBarNow && game != null && game.S != GameState.Waiting && (left >= 0f || overtime);
            bool barTop = GameSettings.TimerBarTopNow;
            float barH = 4 * k * GameSettings.TimerBarHeightNow;
            float barTopY = y;
            if (showBar && barTop) y += barH + 2 * k;
            TopPanelBarOnTop = showBar && barTop;
            // the plate (its width: TIMER > Plate width)
            float pw = 600 * k * GameSettings.TimerWidthNow, plateH = 56 * k + aboveH;
            float plate = GameSettings.TimerPlateNow;
            Tinted(new Rect(cx - pw * 0.62f, y, pw * 1.24f, plateH), s_BandTex, new Color(0f, 0f, 0f, plate));
            if (overtime)
            {
                Tinted(new Rect(cx - pw * 0.7f, y - 10 * k, pw * 1.4f, plateH + 20 * k), s_BandTex, new Color(0.7f, 0.02f, 0.02f, 0.35f + 0.4f * beat));
                Tinted(new Rect(cx - 260 * k, y - 60 * k, 520 * k, plateH + 120 * k), s_GlowTex, new Color(1f, 0.15f, 0.1f, 0.25f + 0.35f * beat));
                Fill(new Rect(cx - pw * 0.5f, y + plateH - 2 * k, pw, 2 * k), new Color(1f, 0.3f, 0.2f, 0.5f + 0.5f * beat));
            }
            if (GameSettings.TimerTopLineNow) Fill(new Rect(cx - pw * 0.42f, y, pw * 0.84f, 2 * k), new Color(accent.r, accent.g, accent.b, overtime ? 0.6f + 0.4f * beat : 0.85f));
            float clockW = hasClock ? 130 * k : 0f;
            float clockY = y + aboveH, clockH = plateH - aboveH;
            // the label: left of the clock, or above it (alone and bigger without one; OVERTIME shakes)
            var lst = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(labelFont), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            if (font != null) lst.font = font;
            var labelR = hasClock ? new Rect(cx - clockW / 2f - 250 * k, clockY + 4 * k, 240 * k, clockH - 8 * k) : new Rect(0, y + 4 * k, sw, plateH - 8 * k);
            if (above)
            {
                lst.alignment = TextAnchor.MiddleCenter;
                lst.fontSize = Mathf.RoundToInt(labelFont * 0.95f);
                lst.clipping = TextClipping.Overflow;
                labelR = new Rect(0, y + 5 * k, sw, aboveH);
            }
            if (!hasClock) { lst.alignment = TextAnchor.MiddleCenter; lst.fontSize = Mathf.RoundToInt((overtime ? 34f * (1f + 0.08f * beat) : 24f) * k); lst.clipping = TextClipping.Overflow; }
            if (overtime)
            {
                labelR.x += (Mathf.PerlinNoise(Time.time * 30f, 1f) - 0.5f) * 5f * k * (0.4f + beat);
                labelR.y += (Mathf.PerlinNoise(2f, Time.time * 30f) - 0.5f) * 4f * k * (0.4f + beat);
                InkText(labelR, label, lst, Color.Lerp(accent, Color.white, beat * 0.7f), new Color(0.15f, 0f, 0f, 0.95f), 2.5f * k * ink);
            }
            else InkText(labelR, $"<color=#{ColorUtility.ToHtmlStringRGB(accent)}>{label}</color>", lst, Color.white, new Color(0f, 0f, 0f, 0.8f), 1.5f * k * ink);
            // the clock: inked digits - steady, except in the final minute (a pop each second, pulsing red)
            if (hasClock)
            {
                float since = Time.time - m_TopSecondAt;
                float pop = final ? 1f + 0.16f * Mathf.Exp(-9f * since) : 1f;
                var cst = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(40 * k * pop), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
                if (font != null) cst.font = font;
                var fill = final ? Color.Lerp(Color.white, new Color(1f, 0.35f, 0.25f), 0.4f + 0.6f * pulse) : Color.Lerp(accent, Color.white, 0.55f);
                InkText(new Rect(cx - clockW / 2f, clockY, clockW, clockH), clock, cst, fill, new Color(0f, 0f, 0f, 0.9f), 2.5f * k * ink);
            }
            // the mode's tag, right of the clock: the same size as the label
            if (hasTag && tagPos == 0)
            {
                float tx = hasClock ? cx + clockW / 2f + 12 * k : cx + lst.CalcSize(new GUIContent(label)).x / 2f + 20 * k;
                Tag(new Rect(tx, clockY + clockH / 2f - 13 * k, tw, 26 * k));
            }
            y += plateH;
            // the bar: draining in from both ends, meeting in the middle at the end of the phase (its dark track - the grey
            // ends - can be switched off: TIMER > Bar's grey ends). TIMER > DRAIN BAR: its thickness, width, colour, which
            // way it drains, rounded ends, the glowing heads and how dark the track is.
            if (showBar)
            {
                float bw = pw * 0.84f * GameSettings.TimerBarWidthNow, frac = overtime ? 1f : Mathf.Clamp01(left / m_TopTotal);
                var br = new Rect(cx - bw / 2f, barTop ? barTopY : y, bw, barH);
                bool round = GameSettings.TimerBarRoundNow;
                if (GameSettings.TimerBarBackNow) BarPiece(br, new Color(0f, 0f, 0f, GameSettings.TimerBarBackOpacityNow), round);
                var bAccent = GameSettings.TimerBarOwnColourNow ? GameSettings.TimerBarColour.Value : accent;
                var bc = overtime ? Color.Lerp(bAccent * 0.8f, new Color(1f, 0.6f, 0.5f), beat) : final ? Color.Lerp(bAccent, new Color(1f, 0.3f, 0.2f), pulse) : bAccent;
                bc.a = 1f;
                int drain = GameSettings.TimerBarDrainNow;
                float fw = bw * frac;
                // (both ends: centred; from the right: held at the left end; from the left: held at the right end)
                float fx = drain == 1 ? br.x : drain == 2 ? br.xMax - fw : cx - fw / 2f;
                if (fw > 0.5f) BarPiece(new Rect(fx, br.y, fw, br.height), bc, round);
                TopPanelBarFill = new Rect(fx, br.y, fw, br.height); // (tests)
                if (!overtime && frac > 0.002f && GameSettings.TimerBarGlowNow)
                {
                    float gw = 24 * k, gh = br.height + 10 * k;
                    if (drain != 2) Tinted(new Rect(fx + fw - gw / 2f, br.y - 5 * k, gw, gh), s_GlowTex, new Color(1f, 1f, 1f, 0.6f));
                    if (drain != 1) Tinted(new Rect(fx - gw / 2f, br.y - 5 * k, gw, gh), s_GlowTex, new Color(1f, 1f, 1f, 0.6f));
                }
                if (!barTop) y += barH + 2 * k; // (on top: its room was made over the plate)
            }
            // the mode's tag under the bar
            if (hasTag && tagPos == 1) { y += 2 * k; Tag(new Rect(cx - tw / 2f, y, tw, 26 * k)); y += 30 * k; }
            // the line about what to do
            int lines = string.IsNullOrEmpty(sub) || !GameSettings.TimerSubNow ? 0 : sub.Split('\n').Length;
            if (lines > 0)
            {
                float sh2 = lines * 20 * k + 6 * k;
                Tinted(new Rect(cx - pw * 0.55f, y, pw * 1.1f, sh2), s_BandTex, new Color(0f, 0f, 0f, plate * 0.75f));
                var sst = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(15 * k) };
                if (font != null) sst.font = font;
                if (overtime) sub = $"<color=#ffb0a0>{sub}</color>";
                Shadowed(new Rect(0, y + 3 * k, sw, lines * 20 * k), sub, sst);
                y += sh2;
            }
            return y - 8f;
        }

        /// <summary>A banner's accent colour, by what the news is (Settings > Display > NOTIFICATIONS > COLOURS: each kind's
        /// own - NotifLooks.cs).</summary>
        static Color BannerAccent(string title) => GameSettings.NotifAccentFor(title);

        /// <summary>(tests) the last banner's text outline: its thickness in pixels and its colour (at full opacity).</summary>
        public static float BannerInkPx;
        public static Color BannerInkEdge;
        /// <summary>(tests) the last banner as drawn: its accent colour, its opacity, how far it had moved up (px) and its scale.</summary>
        public static Color BannerAccentShown;
        public static float BannerAlphaShown, BannerLiftShown, BannerScaleShown;

        static float EaseOut3(float x) { x = Mathf.Clamp01(x); return 1f - Mathf.Pow(1f - x, 3f); }

        /// <summary>A banner `age` seconds old. How long it stays, how it comes in and goes, and how far up it flies on the
        /// way out: Settings > Display > NOTIFICATIONS > ANIMATION (NotifLooks.cs). As designed: the band snaps open from
        /// the middle, the title drops in and bounces, and after 4 s it all lifts 40 px and fades.</summary>
        void DrawBannerFx(string title, string sub, float age, float k)
        {
            EnsureNotifyTextures();
            float sw = Screen.width, sh = Screen.height;
            var accent = BannerAccent(title);  // (by the game's own words...)
            title = NotifText.Map(title);      // (...shown in the player's own, if they've reworded it: NotifLooks.cs)
            float dur = GameSettings.NotifTimeNow, outLen = Mathf.Min(0.65f, dur * 0.3f);
            int enter = GameSettings.NotifEnter.Value, exit = GameSettings.NotifExit.Value;
            float fly = GameSettings.NotifFly.Value * k;
            float inT = Mathf.Clamp01(age / 0.32f), outT = Mathf.Clamp01((age - (dur - outLen)) / outLen);
            // ---- the way in ----
            float open = 1f, alpha = 1f, scale = 1f, lift = 0f, lineAge = age, titleAge = age;
            bool bounce = true;
            switch (enter)
            {
                case 0: open = EaseOut3(inT); break;                                            // the band snapping open
                case 1: lift = -(1f - EaseOut3(age / 0.4f)) * (sh * 0.27f + 80 * k); break;     // sliding down from above
                case 2: alpha = Mathf.Clamp01(age / 0.35f); bounce = false; break;              // fading in
                case 3:                                                                          // popping in from small
                {
                    float p = Mathf.Clamp01(age / 0.3f), q = p - 1f;
                    scale = Mathf.Max(0.05f, 1f + 2.4f * q * q * q + 1.4f * q * q);
                    alpha = Mathf.Clamp01(age / 0.1f);
                    bounce = false;
                    break;
                }
                default: lineAge = titleAge = 10f; bounce = false; break;                       // just there
            }
            // ---- ...and out ----
            switch (exit)
            {
                case 0: lift += outT * outT * fly; alpha *= 1f - outT; break;                    // lifting away as it fades
                case 1: alpha *= 1f - outT; break;                                               // fading
                case 2: lift += outT * outT * fly; alpha *= 1f - Mathf.Clamp01((outT - 0.7f) / 0.3f); break; // sliding up
                default: scale *= 1f - outT * outT; alpha *= 1f - outT * outT * outT; break;     // shrinking away
            }
            // (Settings > Display > NOTIFICATIONS: size, height, band width / darkness, font, outline; NotifLooks.cs)
            float zs = GameSettings.NotifSize.Value * scale, ink = GameSettings.NotifInkNow * Mathf.Min(1f, scale); // (TEXT OUTLINE)
            var font = GameSettings.NotifFontNow;
            float mid = sh * 0.27f + GameSettings.NotifY.Value * k + 33 * k * GameSettings.NotifSize.Value; // (the band's middle stays put while it scales)
            float bh = 66 * k * zs, by = mid - bh / 2f - lift, cx = sw / 2f;
            float bw = Mathf.Min(sw, 820 * k * GameSettings.NotifWidth.Value * scale) * open; // (smaller and lower than it was; just the title)
            BannerAccentShown = accent; BannerAlphaShown = alpha; BannerLiftShown = lift; BannerScaleShown = scale; // (tests)
            if (alpha <= 0.001f || bh < 1f) return;
            Tinted(new Rect(cx - bw / 2f, by, bw, bh), s_BandTex, new Color(0f, 0f, 0f, Mathf.Clamp01(0.62f * GameSettings.NotifPlate.Value) * alpha));
            // accent lines racing out along the band's edges
            float lines = EaseOut3((lineAge - 0.08f) / 0.45f);
            float lw = bw * 0.8f * lines;
            var ac = new Color(accent.r, accent.g, accent.b, alpha);
            Fill(new Rect(cx - lw / 2f, by, lw, 3 * k), ac);
            Fill(new Rect(cx - lw / 2f, by + bh - 3 * k, lw, 3 * k), ac);
            // the title: drops in big and settles with a bounce
            float tt = Mathf.Clamp01((titleAge - 0.05f) / 0.5f);
            float bnc = bounce ? 1f + 0.45f * Mathf.Exp(-6f * tt) * Mathf.Cos(tt * 16f) * (tt > 0f ? 1f : 0f) : 1f;
            float ta = (enter == 0 || enter == 1 ? Mathf.Clamp01((titleAge - 0.05f) / 0.12f) : 1f) * alpha;
            var tst = new GUIStyle(m_Big) { fontSize = Mathf.Max(8, Mathf.RoundToInt(38 * k * zs * bnc)), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            if (font != null) tst.font = font;
            var fill = Color.Lerp(Color.white, accent, Mathf.Clamp01((titleAge - 0.1f) * 3f));
            fill.a = ta;
            BannerInkPx = 3f * k * ink; BannerInkEdge = GameSettings.NotifInkEdge(0.9f); // (tests)
            InkText(new Rect(0, by + 4 * k * scale, sw, bh - 8 * k * scale), title, tst, fill, GameSettings.NotifInkEdge(0.9f * ta), 3f * k * ink);
            // a glint sweeping across the title
            float g = Mathf.Clamp01((titleAge - 0.35f) / 0.55f);
            if (g > 0f && g < 1f)
            {
                float gx = cx - bw * 0.4f + bw * 0.8f * g;
                Tinted(new Rect(gx - 24 * k, by + 6 * k, 48 * k, bh - 12 * k), s_GlintTex, new Color(1f, 1f, 1f, 0.35f * Mathf.Sin(g * Mathf.PI) * alpha));
            }
            // (no line under it any more: just the title)
        }
    }
}
