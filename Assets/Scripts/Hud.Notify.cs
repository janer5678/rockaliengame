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
            for (int i = 0; i < 12; i++)
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
            var accent = overtime ? new Color(1f, 0.25f, 0.18f) : hard ? new Color(1f, 0.55f, 0.15f) : new Color(1f, 0.85f, 0.25f);
            float cx = sw / 2f, cy = sh * 0.24f + 110 * k;
            // the glow and the shockwave behind the number
            float gs = (hard ? 520f : 420f) * k * (0.9f + 0.25f * Mathf.Exp(-5f * t));
            Tinted(new Rect(cx - gs / 2f, cy - gs / 2f, gs, gs), s_GlowTex, new Color(accent.r, accent.g, accent.b, 0.4f * (1f - t) + 0.08f));
            // (no rings round it)
            // the label on its band, the accent line under it filling through the second
            float bw = 560f * k, bh = 54 * k, by = sh * 0.2f - 6 * k;
            Tinted(new Rect(cx - bw / 2f, by, bw, bh), s_BandTex, new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(cx - bw * 0.35f, by + bh - 4 * k, bw * 0.7f * t, 3 * k), accent);
            var lst = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * k), alignment = TextAnchor.MiddleCenter };
            InkText(new Rect(0, by, sw, bh), label, lst, new Color(1f, 0.95f, 0.9f), new Color(0f, 0f, 0f, 0.9f), 2.5f * k);
            // the number: slammed in white-hot, springing and wobbling as it settles into the colour
            float spring = 1f + (hard ? 0.6f : 0.42f) * Mathf.Exp(-7f * t) * Mathf.Cos(t * 22f);
            float size = (hard ? 200f : 165f) * k * spring;
            var nst = new GUIStyle(m_Big) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size)), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            var fill = Color.Lerp(Color.white, accent, Mathf.Clamp01(t * 4f));
            fill.a = 0.75f + 0.25f * frac;
            float wob = (hard ? 9f : 6f) * Mathf.Exp(-6f * t) * Mathf.Sin(t * 26f);
            var mat = GUI.matrix;
            GUIUtility.RotateAroundPivot(wob, new Vector2(cx, cy));
            InkText(new Rect(cx - 300 * k, cy - size * 0.6f, 600 * k, size * 1.2f), n.ToString(), nst, fill, new Color(0f, 0f, 0f, 0.85f), (hard ? 6f : 5f) * k);
            GUI.matrix = mat;
            // the pips: one per second left
            float pip = 12 * k, gap = 7 * k, px0 = cx - (10 * pip + 9 * gap) / 2f, py = cy + 120 * k;
            for (int i = 0; i < 10; i++)
            {
                bool on = i < n;
                var pr = new Rect(px0 + i * (pip + gap), py, pip, pip);
                Fill(new Rect(pr.x - 1, pr.y - 1, pr.width + 2, pr.height + 2), new Color(0f, 0f, 0f, 0.6f));
                Fill(pr, on ? (i == n - 1 ? Color.Lerp(Color.white, accent, t) : accent) : new Color(1f, 1f, 1f, 0.12f));
            }
        }

        // the top panel's memory: which phase it's showing, how long that phase was, the last whole second shown
        GameState m_TopState = (GameState)255;
        float m_TopTotal = 1f, m_TopSecondAt = -10f;
        int m_TopSecond = -1;

        /// <summary>
        /// The top-centre header in a match: a dark plate with the phase's accent along it - its label on a chip ("WALL
        /// DROPS IN", "TIME LEFT", "OVERTIME", "SUDDEN DEATH"...), the clock big in inked digits that tick in with a little
        /// pop every second, the mode on a tag at the end - a bar under it draining through the phase with a bright head,
        /// and the line about what to do under that. In the phase's last 30 seconds the clock and its bar pulse. Returns
        /// how tall it is (the mode chips go under it).
        /// </summary>
        float DrawTopPanel(NetGame game, string label, string clock, string tag, string sub, Color accent, float left, float k)
        {
            EnsureNotifyTextures();
            float sw = Screen.width, cx = sw / 2f, y = 8f;
            // the phase's length (for the bar): what the clock read when the phase began
            var st = game != null ? game.S : GameState.Waiting;
            if (st != m_TopState) { m_TopState = st; m_TopTotal = Mathf.Max(1f, left); }
            if (left > m_TopTotal) m_TopTotal = left;
            int sec = left >= 0f ? Mathf.CeilToInt(left) : -1;
            if (sec != m_TopSecond) { m_TopSecond = sec; m_TopSecondAt = Time.time; }
            bool urgent = left >= 0f && left <= 30f;
            float pulse = urgent ? 0.5f + 0.5f * Mathf.Sin(Time.time * 7f) : 0f;
            // the plate
            float pw = 640 * k, plateH = 64 * k;
            Tinted(new Rect(cx - pw * 0.62f, y, pw * 1.24f, plateH), s_BandTex, new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(cx - pw * 0.42f, y, pw * 0.84f, 2 * k), new Color(accent.r, accent.g, accent.b, 0.85f));
            // the label chip, left of the clock
            var lst = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(19 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            bool hasClock = !string.IsNullOrEmpty(clock);
            float clockW = hasClock ? 150 * k : 0f;
            var labelR = hasClock ? new Rect(cx - clockW / 2f - 260 * k, y + 6 * k, 250 * k, plateH - 12 * k) : new Rect(0, y + 6 * k, sw, plateH - 12 * k);
            if (!hasClock) lst.alignment = TextAnchor.MiddleCenter;
            if (!hasClock) lst.fontSize = Mathf.RoundToInt(26 * k);
            InkText(labelR, $"<color=#{ColorUtility.ToHtmlStringRGB(accent)}>{label}</color>", lst, Color.white, new Color(0f, 0f, 0f, 0.8f), 1.5f * k);
            // the clock: big inked digits, popping in each second (redder in the last 30)
            if (hasClock)
            {
                float since = Time.time - m_TopSecondAt;
                float pop = 1f + (urgent ? 0.18f : 0.08f) * Mathf.Exp(-9f * since);
                var cst = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(46 * k * pop), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
                var fill = urgent ? Color.Lerp(Color.white, new Color(1f, 0.35f, 0.25f), 0.4f + 0.6f * pulse) : Color.Lerp(accent, Color.white, Mathf.Exp(-6f * since) * 0.8f + 0.35f);
                InkText(new Rect(cx - clockW / 2f, y, clockW, plateH), clock, cst, fill, new Color(0f, 0f, 0f, 0.9f), 3f * k);
            }
            // the mode's tag, right of the clock
            if (!string.IsNullOrEmpty(tag) && hasClock)
            {
                var tst = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(14 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                float tw = tst.CalcSize(new GUIContent(tag)).x + 18 * k;
                var tr = new Rect(cx + clockW / 2f + 14 * k, y + plateH / 2f - 12 * k, tw, 24 * k);
                Fill(tr, new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, 0.85f));
                Frame(tr, new Color(accent.r, accent.g, accent.b, 0.9f), 1.5f);
                GUI.Label(tr, $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(accent, Color.white, 0.4f))}>{tag}</color>", tst);
            }
            y += plateH;
            // the bar: draining through the phase, a bright head at its end
            if (left >= 0f && game != null && game.S != GameState.Waiting)
            {
                float bw = pw * 0.84f, frac = Mathf.Clamp01(left / m_TopTotal);
                var br = new Rect(cx - bw / 2f, y, bw, 5 * k);
                Fill(br, new Color(0f, 0f, 0f, 0.55f));
                var bc = urgent ? Color.Lerp(accent, new Color(1f, 0.3f, 0.2f), pulse) : accent;
                Fill(new Rect(br.x, br.y, br.width * frac, br.height), bc);
                Tinted(new Rect(br.x + br.width * frac - 14 * k, br.y - 6 * k, 28 * k, br.height + 12 * k), s_GlowTex, new Color(1f, 1f, 1f, 0.7f));
                y += 7 * k;
            }
            // the line about what to do
            int lines = string.IsNullOrEmpty(sub) ? 0 : sub.Split('\n').Length;
            if (lines > 0)
            {
                float sh2 = lines * 22 * k + 6 * k;
                Tinted(new Rect(cx - pw * 0.55f, y, pw * 1.1f, sh2), s_BandTex, new Color(0f, 0f, 0f, 0.45f));
                Shadowed(new Rect(0, y + 3 * k, sw, lines * 22 * k), sub, new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(16 * k) });
                y += sh2;
            }
            return y - 8f;
        }

        /// <summary>A banner's accent colour, by what the news is.</summary>
        static Color BannerAccent(string title)
        {
            string s = title ?? "";
            if (s.Contains("UNLOCK")) return new Color(1f, 0.88f, 0.2f);   // (yellow)
            if (s.Contains("AIRDROP")) return new Color(0.75f, 0.4f, 1f);  // (purple)
            if (s.Contains("WALL") || s.Contains("DROP")) return new Color(0.4f, 0.85f, 1f);
            if (s.Contains("DESTROY") || s.Contains("DOWN") || s.Contains("OUT")) return new Color(1f, 0.3f, 0.25f);
            return new Color(1f, 0.85f, 0.3f);
        }

        /// <summary>A banner `age` seconds old (it lasts 4 s).</summary>
        void DrawBannerFx(string title, string sub, float age, float k)
        {
            EnsureNotifyTextures();
            float sw = Screen.width, sh = Screen.height;
            var accent = BannerAccent(title);
            float inT = Mathf.Clamp01(age / 0.32f), outT = Mathf.Clamp01((age - 3.35f) / 0.65f);
            float open = 1f - Mathf.Pow(1f - inT, 3f);                 // the band snapping open
            float lift = outT * outT * 40f * k;                        // ...and lifting away at the end
            float alpha = 1f - outT;
            float cx = sw / 2f, by = sh * 0.2f - 10 * k - lift, bh = 112 * k, bw = Mathf.Min(sw, 1100 * k) * open;
            Tinted(new Rect(cx - bw / 2f, by, bw, bh), s_BandTex, new Color(0f, 0f, 0f, 0.62f * alpha));
            // accent lines racing out along the band's edges
            float lines = 1f - Mathf.Pow(1f - Mathf.Clamp01((age - 0.08f) / 0.45f), 3f);
            float lw = bw * 0.8f * lines;
            var ac = new Color(accent.r, accent.g, accent.b, alpha);
            Fill(new Rect(cx - lw / 2f, by, lw, 3 * k), ac);
            Fill(new Rect(cx - lw / 2f, by + bh - 3 * k, lw, 3 * k), ac);
            // the title: drops in big and settles with a bounce
            float tt = Mathf.Clamp01((age - 0.05f) / 0.5f);
            float bounce = 1f + 0.45f * Mathf.Exp(-6f * tt) * Mathf.Cos(tt * 16f) * (tt > 0f ? 1f : 0f);
            float ta = Mathf.Clamp01((age - 0.05f) / 0.12f) * alpha;
            var tst = new GUIStyle(m_Big) { fontSize = Mathf.Max(8, Mathf.RoundToInt(52 * k * bounce)), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            var fill = Color.Lerp(Color.white, accent, Mathf.Clamp01((age - 0.1f) * 3f));
            fill.a = ta;
            InkText(new Rect(0, by + 4 * k, sw, 66 * k), title, tst, fill, new Color(0f, 0f, 0f, 0.9f * ta), 3.5f * k);
            // a glint sweeping across the title
            float g = Mathf.Clamp01((age - 0.35f) / 0.55f);
            if (g > 0f && g < 1f)
            {
                float gx = cx - bw * 0.4f + bw * 0.8f * g;
                Tinted(new Rect(gx - 30 * k, by + 8 * k, 60 * k, 58 * k), s_GlintTex, new Color(1f, 1f, 1f, 0.35f * Mathf.Sin(g * Mathf.PI) * alpha));
            }
            // the line under it
            float sa = Mathf.Clamp01((age - 0.25f) / 0.3f) * alpha;
            var sst = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(19 * k), alignment = TextAnchor.MiddleCenter };
            InkText(new Rect(0, by + 68 * k, sw, 34 * k), sub, sst, new Color(1f, 1f, 1f, sa), new Color(0f, 0f, 0f, 0.8f * sa), 1.5f * k);
        }
    }
}
