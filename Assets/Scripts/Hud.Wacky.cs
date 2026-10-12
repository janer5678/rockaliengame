using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The wacky airdrop items on the HUD: big notes in the middle of the screen (the old top-right messages are off),
    /// what they do while you hold them, rows under the health bar (the jump boots, a shield, slowed...), and their screens:
    /// the CONNECT 4 game and the GAMESHOW question everyone watches, Omen's map, the Draw a Monster canvas, the scribble
    /// notebook - and the views they put you in (a bear, the turret bot, the attention snail, a timeout ball, a drawn
    /// monster all over your screen).
    /// </summary>
    public partial class Hud
    {
        // ---------------- notes ----------------

        struct WNote { public string Text; public float Time; }
        static readonly List<WNote> s_Notes = new List<WNote>();
        /// <summary>Test hook: the last big note.</summary>
        public static string LastWackyNote { get; private set; } = "";

        /// <summary>A big line in the middle of the screen for a couple of seconds.</summary>
        public static void WackyNote(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("[HUD] " + text);
            LastWackyNote = text;
            s_Notes.RemoveAll(n => n.Text == text);
            s_Notes.Add(new WNote { Text = text, Time = Time.time });
            if (s_Notes.Count > 3) s_Notes.RemoveAt(0);
        }

        // ---------------- held hints ----------------

        string WackyHint(ItemStack s, PlayerNet me)
        {
            if (!Cfg.IsWacky(s.Id)) return null;
            string name = Cfg.ItemName(s.Id);
            int max = Cfg.MaxData(s.Id);
            string shots = max > 0 ? $" ({(s.Data <= 0 || s.Data > max ? max : s.Data)} left)" : "";
            return $"<b>{name}</b>{shots}    {Cfg.WackyBlurb(s.Id)}";
        }

        // ---------------- under the health bar ----------------

        float DrawWackyStatus(PlayerNet me, Rect hr, float statY, float hw, float k)
        {
            double now = me.NetworkManager != null ? me.NetworkManager.ServerTime.Time : 0;
            void Row(string text, Color fill, float frac = 1f)
            {
                var b = new Rect(hr.x, statY, hw, 22 * k);
                Fill(b, new Color(0, 0, 0, 0.55f));
                Fill(new Rect(b.x + 2, b.y + 2, (b.width - 4) * Mathf.Clamp01(frac), b.height - 4), fill);
                Shadowed(new Rect(b.x + 6, b.y, b.width, b.height), $"<b>{text}</b>", m_Small);
                statY -= 26 * k;
            }
            float Left(double until) => Mathf.Max(0f, (float)(until - now));
            if (me.WornBoots.Value) Row("DOUBLE JUMP BOOTS", new Color(0.35f, 0.8f, 0.45f, 0.85f));
            if (me.IsBear) Row($"BEAR {me.BearHpLeft.Value:0} HP    X: turn back", new Color(0.55f, 0.35f, 0.18f, 0.95f), me.BearHpLeft.Value / Mathf.Max(1f, Cfg.BearHp));
            if (me.Shielded) Row($"SHIELD {Mathf.CeilToInt(Left(me.ShieldUntil.Value))}s", new Color(0.3f, 0.85f, 1f, 0.85f), Left(me.ShieldUntil.Value) / Mathf.Max(1f, Cfg.ShieldTime));
            if (me.Slowed) Row($"SLOWED {Mathf.CeilToInt(Left(me.SlowUntil.Value))}s", new Color(0.6f, 0.3f, 0.85f, 0.85f), Left(me.SlowUntil.Value) / Mathf.Max(1f, Cfg.SlowTime));
            if (me.IsAnt) Row($"ANT {Mathf.CeilToInt(Left(me.AntUntil.Value))}s", new Color(0.35f, 0.3f, 0.25f, 0.95f), Left(me.AntUntil.Value) / Mathf.Max(1f, Cfg.AntTime));
            if (me.Disguised) Row($"DISGUISED AS {Cfg.TeamLabel(me.DisguiseTeam.Value)}", Color.Lerp(Cfg.TeamColor[Mathf.Clamp(me.DisguiseTeam.Value, 0, 3)], Color.black, 0.2f));
            if (me.Flipped) Row("GRAVITY FLIPPED", new Color(0.55f, 0.4f, 1f, 0.85f));
            if (me.Glittering) Row("GOLDEN!", new Color(1f, 0.82f, 0.2f, 0.9f), Left(me.GlitterUntil.Value) / Mathf.Max(1f, Cfg.GlitterTime));
            return statY;
        }

        // ---------------- everything else ----------------

        void DrawWacky(PlayerNet me, PlayerController pc, NetGame game)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            double now = me.NetworkManager != null ? me.NetworkManager.ServerTime.Time : 0;
            DrawMonsterSplats(sw, sh);
            // the views
            if (PlayerController.SnailShot(out _, out _))
            {
                float bar = sh * 0.11f;
                Fill(new Rect(0, 0, sw, bar), new Color(0, 0, 0, 0.92f));
                Fill(new Rect(0, sh - bar, sw, bar), new Color(0, 0, 0, 0.92f));
                double left = 0;
                if (game != null) foreach (var g in game.Gadgets) if (g.Kind == Gadget.Snail && g.Until > now) left = System.Math.Max(left, g.Until - now);
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * k) };
                Shadowed(new Rect(0, bar * 0.2f, sw, bar * 0.7f), $"<color=#ffd24a>ATTENTION SNAIL</color>  <size={Mathf.RoundToInt(20 * k)}>{Mathf.CeilToInt((float)left)}</size>", st);
                Shadowed(new Rect(0, sh - bar * 0.8f, sw, bar * 0.6f), "Everyone is looking at the snail. You can still move.", m_Center);
            }
            if (me.Droning)
            {
                // the bot's camera: a green tint, scan lines, its buttons
                Fill(new Rect(0, 0, sw, sh), new Color(0.1f, 0.6f, 0.2f, 0.12f));
                for (float y = Mathf.Repeat(Time.time * 40f, 6f); y < sh; y += 6f) Fill(new Rect(0, y, sw, 1.5f), new Color(0, 0, 0, 0.12f));
                Shadowed(new Rect(20 * k, 16 * k, 600 * k, 30 * k), "<b><color=#7dff9a>● TURRET BOT CAM</color></b>", m_Label);
                Shadowed(new Rect(0, sh - 90 * k, sw, 30 * k), "<b>WASD</b> drive   <b>Space</b> hop   <b>LMB</b> BLOW UP (5 HP left near it, 40 or less dies)   <b>E</b> back to your body", m_Center);
            }
            if (me.IsBear)
            {
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(26 * k) };
                Shadowed(new Rect(0, sh - 205 * k, sw, 40 * k), "<color=#ffcf8a>PRESS X TO TURN BACK</color>", st);
                Shadowed(new Rect(0, sh - 172 * k, sw, 26 * k), "LMB: claws (tear through bases)   You can carry the ball - but no looting or items", m_Center);
            }
            if (me.Prisoned)
            {
                // inside the ball: a soft bubble round the edge of the screen and the time left, big
                float left = Mathf.Max(0f, (float)(me.PrisonUntil.Value - now));
                Fill(new Rect(0, 0, sw, sh), new Color(0.6f, 0.85f, 1f, 0.16f));
                DrawRing(new Vector2(sw / 2, sh / 2), Mathf.Min(sw, sh) * 0.62f, 30 * k, new Color(0.7f, 0.9f, 1f, 0.35f));
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(64 * k) };
                Shadowed(new Rect(0, sh * 0.2f, sw, 80 * k), $"TIMEOUT  {Mathf.CeilToInt(left)}", st);
                Shadowed(new Rect(0, sh * 0.2f + 80 * k, sw, 26 * k), "Nobody can hurt you in here - and you can't do anything", m_Center);
            }
            if (me.Dancing)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 12f) * 0.08f;
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(56 * k * pulse) };
                Color c = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.6f, 1f), 0.6f, 1f);
                Shadowed(new Rect(0, sh * 0.18f, sw, 90 * k), $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>DANCE!</color>", st);
            }
            if (me.KnockedDown)
            {
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(44 * k) };
                Shadowed(new Rect(0, sh * 0.2f, sw, 70 * k), "<color=#7fb0ff>KNOCKED FLAT!</color>", st);
                for (int i = 0; i < 5; i++)
                {
                    float a = Time.time * 3f + i * 1.256f;
                    var c = new Vector2(sw / 2 + Mathf.Cos(a) * 120 * k, sh * 0.42f + Mathf.Sin(a) * 30 * k);
                    Shadowed(new Rect(c.x - 20, c.y - 20, 40, 40), "<color=#ffe066>★</color>", new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * k) });
                }
            }
            if (me.IsAnt)
            {
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(34 * k) };
                Shadowed(new Rect(0, sh * 0.16f, sw, 60 * k), $"<color=#d9c09a>YOU'RE AN ANT  {Mathf.CeilToInt(Mathf.Max(0f, (float)(me.AntUntil.Value - now)))}</color>", st);
            }
            if (me.Glittering)
            {
                // covered in gold glitter: sparkles all over the edges of your view
                var rr = new System.Random(Mathf.FloorToInt(Time.time * 8f));
                for (int i = 0; i < 40; i++)
                {
                    float x = (float)rr.NextDouble() * sw, y = (float)rr.NextDouble() * sh;
                    float edge = Mathf.Max(Mathf.Abs(x / sw - 0.5f), Mathf.Abs(y / sh - 0.5f));
                    if (edge < 0.3f) continue;
                    float sz = (4f + (float)rr.NextDouble() * 8f) * k;
                    DrawDot(new Rect(x, y, sz, sz), new Color(1f, 0.85f, 0.3f, 0.85f));
                }
            }
            if (me.Shielded) DrawRing(new Vector2(sw / 2, sh / 2), Mathf.Min(sw, sh) * 0.7f, 18 * k, new Color(0.4f, 0.9f, 1f, 0.18f + 0.06f * Mathf.Sin(Time.time * 5f)));
            // the screens
            if (pc.OmenMapOpen) DrawOmenMap(me, pc);
            if (pc.DrawOpen) DrawCanvas(pc);
            if (pc.ScribbleOpen) DrawNotebook(pc);
            if (game != null && game.MinigameOn) DrawMinigame(me, game);
            // the notes, over it all
            for (int i = s_Notes.Count - 1, row = 0; i >= 0; i--, row++)
            {
                float age = Time.time - s_Notes[i].Time;
                if (age > 3f) { s_Notes.RemoveAt(i); continue; }
                float a = Mathf.Clamp01((3f - age) / 0.5f) * Mathf.Clamp01(age / 0.12f + 0.2f);
                var st = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(26 * k) };
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                Shadowed(new Rect(0, sh * 0.26f - row * 38 * k, sw, 40 * k), s_Notes[i].Text, st);
                GUI.color = old;
            }
        }

        static Texture2D s_WRingTex, s_WDotTex;

        static Texture2D Disc(bool ring)
        {
            var t = ring ? s_WRingTex : s_WDotTex;
            if (t != null) return t;
            const int N = 128;
            t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.1f) : Mathf.Clamp01((1f - d) * N / 4f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            if (ring) s_WRingTex = t; else s_WDotTex = t;
            return t;
        }

        static void DrawRing(Vector2 c, float r, float width, Color col)
        {
            var old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2, r * 2), Disc(true));
            GUI.color = old;
        }

        static void DrawDot(Rect r, Color col)
        {
            var old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(r, Disc(false));
            GUI.color = old;
        }

        // ---------------- a drawn monster all over your screen ----------------

        struct Splat { public Texture2D Tex; public float Time; public int Seed; }
        static readonly List<Splat> s_Splats = new List<Splat>();
        /// <summary>For the tests: splats shown.</summary>
        public static int Splats { get; private set; }

        public static void MonsterSplat(string pic)
        {
            s_Splats.Add(new Splat { Tex = MonsterArt.Texture(pic), Time = Time.time, Seed = Random.Range(1, 99999) });
            Splats++;
            Sfx.Play2D(Sfx.Laugh, 0.8f);
        }

        /// <summary>The monster bursts onto the screen and keeps copying itself - more and more of them, bouncing all over -
        /// for Cfg.MonsterSplatTime, then they all pop.</summary>
        void DrawMonsterSplats(float sw, float sh)
        {
            for (int i = s_Splats.Count - 1; i >= 0; i--)
            {
                var sp = s_Splats[i];
                float age = Time.time - sp.Time;
                if (age > Cfg.MonsterSplatTime) { if (sp.Tex) Destroy(sp.Tex); s_Splats.RemoveAt(i); continue; }
                int copies = Mathf.Clamp(1 + Mathf.FloorToInt(age * 9f), 1, 40);
                var r = new System.Random(sp.Seed);
                for (int c = 0; c < copies; c++)
                {
                    float size = Mathf.Lerp(0.55f, 0.18f, c / 40f) * Mathf.Min(sw, sh) * (c == 0 ? Mathf.Min(1f, age * 4f) * 1.2f : 1f);
                    float px = (float)r.NextDouble(), py = (float)r.NextDouble(), vx = (float)r.NextDouble() - 0.5f, vy = (float)r.NextDouble() - 0.5f;
                    float born = c / 9f;
                    float t = Mathf.Max(0f, age - born);
                    // bouncing round the screen
                    float x = Mathf.PingPong(px * sw + vx * 900f * t, sw - size);
                    float y = Mathf.PingPong(py * sh + vy * 900f * t, sh - size);
                    var rect = new Rect(x, y, size, size);
                    var old = GUI.matrix;
                    GUIUtility.RotateAroundPivot(Mathf.Sin(Time.time * 6f + c) * 18f, rect.center);
                    GUI.DrawTexture(rect, sp.Tex, ScaleMode.ScaleToFit, true);
                    GUI.matrix = old;
                }
            }
        }

        // ---------------- Omen's map ----------------

        void DrawOmenMap(PlayerNet me, PlayerController pc)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            MouseOverUI = true;
            Fill(new Rect(0, 0, sw, sh), new Color(0.05f, 0.02f, 0.08f, 0.82f));
            float size = Mathf.Min(sw, sh) * 0.8f;
            var map = new Rect((sw - size) / 2, (sh - size) / 2 + 14 * k, size, size);
            float half = Cfg.MapHalf;
            Vector2 ToMap(Vector3 w) => new Vector2(map.center.x + w.x / (2 * half) * size, map.center.y - w.z / (2 * half) * size);
            Fill(map, new Color(0.28f, 0.45f, 0.26f));
            // an old map: the edges darker, a grid of faint lines
            for (int g = 1; g < 8; g++)
            {
                Fill(new Rect(map.x + g * size / 8f, map.y, 1, size), new Color(0, 0, 0, 0.12f));
                Fill(new Rect(map.x, map.y + g * size / 8f, size, 1), new Color(0, 0, 0, 0.12f));
            }
            if (MapBuilder.GlassUp) Fill(Cfg.FourWay ? new Rect(0, 0, 0, 0) : new Rect(map.x, map.center.y - 1, size, 3), new Color(0.6f, 0.85f, 1f, 0.7f));
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var bc = Cfg.BaseCenter[t];
                var a = ToMap(bc + new Vector3(-Cfg.BaseHalf, 0, Cfg.BaseHalf));
                float bs = Cfg.BaseHalf * 2 / (2 * half) * size;
                var col = Cfg.TeamColor[t];
                Fill(new Rect(a.x, a.y, bs, bs), new Color(col.r, col.g, col.b, 0.5f));
                GUI.Label(new Rect(a.x, a.y + bs / 2 - 12, bs, 24), $"<b>{Cfg.TeamName[t]}</b>", m_Center);
            }
            foreach (var c in Container.All) if (c != null && c.IsAirdrop) { var q = ToMap(c.transform.position); Fill(new Rect(q.x - 5, q.y - 5, 10, 10), new Color(1f, 0.38f, 0.78f)); }
            if (Ball.Instance != null) { var q = ToMap(Ball.Instance.transform.position); DrawDot(new Rect(q.x - 7, q.y - 7, 14, 14), new Color(1f, 0.85f, 0.15f)); }
            // the whole game: everyone, enemies too
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p.Dead.Value) continue;
                var q = ToMap(p.transform.position);
                DrawDot(new Rect(q.x - 6, q.y - 6, 12, 12), p == me ? Color.white : Cfg.TeamColor[Mathf.Clamp(p.Team.Value, 0, 3)]);
                if (p != me) GUI.Label(new Rect(q.x - 60, q.y + 6, 120, 20), p.DisplayName, new GUIStyle(m_Small) { alignment = TextAnchor.UpperCenter });
            }
            Shadowed(new Rect(0, map.y - 36 * k, sw, 30 * k), "<b><color=#c9a2ff>OMEN'S MAP</color></b> - click anywhere to teleport there  ·  RMB / Esc to put it away", m_Center);
            var e = Event.current;
            var m = e.mousePosition;
            if (map.Contains(m))
            {
                DrawRing(m, 14 * k, 3, new Color(0.8f, 0.6f, 1f, 0.9f));
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    var world = new Vector3((m.x - map.center.x) / size * 2 * half, 0, -(m.y - map.center.y) / size * 2 * half);
                    pc.ConfirmOmen(world);
                    e.Use();
                }
            }
            if (e.type == EventType.MouseDown && e.button == 1) { pc.CloseWackyUi(); e.Use(); }
        }

        // ---------------- the Draw a Monster canvas ----------------

        static byte[] s_Pixels = new byte[MonsterArt.Bytes];
        static Texture2D s_CanvasTex;
        static bool s_CanvasDirty = true;
        static int s_Ink = 1, s_Brush = 2;
        static Vector2Int s_LastPx = new Vector2Int(-1, -1);

        public static void BeginDrawing()
        {
            s_Pixels = new byte[MonsterArt.Bytes];
            s_CanvasDirty = true;
            s_LastPx = new Vector2Int(-1, -1);
        }

        public static byte[] DrawingPixels() => (byte[])s_Pixels.Clone();

        void Paint(int x, int y)
        {
            int r = s_Brush;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r * r + r) MonsterArt.Set(s_Pixels, x + dx, y + dy, s_Ink);
            s_CanvasDirty = true;
        }

        void DrawCanvas(PlayerController pc)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            MouseOverUI = true;
            Fill(new Rect(0, 0, sw, sh), new Color(0.08f, 0.08f, 0.1f, 0.9f));
            float size = Mathf.Min(sh * 0.72f, sw * 0.55f);
            var canvas = new Rect((sw - size) / 2 - 60 * k, (sh - size) / 2 + 10 * k, size, size);
            Shadowed(new Rect(0, canvas.y - 64 * k, sw, 34 * k), $"<b>DRAW A MONSTER</b>   <color=#ffd24a>{Mathf.CeilToInt(pc.DrawTimeLeft)} s</color>", new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(28 * k) });
            Shadowed(new Rect(0, canvas.y - 32 * k, sw, 26 * k), "It comes alive, finds an enemy and covers their screen. White is see-through.", m_Center);
            if (s_CanvasTex == null || s_CanvasDirty)
            {
                if (s_CanvasTex) Destroy(s_CanvasTex);
                s_CanvasTex = MonsterArt.Texture(s_Pixels, true);
                s_CanvasDirty = false;
            }
            Fill(new Rect(canvas.x - 4, canvas.y - 4, canvas.width + 8, canvas.height + 8), new Color(0.3f, 0.3f, 0.32f));
            GUI.DrawTexture(canvas, s_CanvasTex);
            var e = Event.current;
            var m = e.mousePosition;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && canvas.Contains(m))
            {
                int px = Mathf.Clamp(Mathf.FloorToInt((m.x - canvas.x) / size * MonsterArt.Size), 0, MonsterArt.Size - 1);
                int py = Mathf.Clamp(MonsterArt.Size - 1 - Mathf.FloorToInt((m.y - canvas.y) / size * MonsterArt.Size), 0, MonsterArt.Size - 1);
                // a line from the last point, so a fast stroke doesn't leave gaps
                if (e.type == EventType.MouseDrag && s_LastPx.x >= 0)
                {
                    int steps = Mathf.Max(Mathf.Abs(px - s_LastPx.x), Mathf.Abs(py - s_LastPx.y));
                    for (int i = 1; i <= steps; i++) Paint(Mathf.RoundToInt(Mathf.Lerp(s_LastPx.x, px, i / (float)steps)), Mathf.RoundToInt(Mathf.Lerp(s_LastPx.y, py, i / (float)steps)));
                }
                else Paint(px, py);
                s_LastPx = new Vector2Int(px, py);
                e.Use();
            }
            if (e.type == EventType.MouseUp) s_LastPx = new Vector2Int(-1, -1);
            // the paints, brushes and buttons, down the right
            float bx = canvas.xMax + 24 * k, by = canvas.y, cs = 40 * k;
            for (int i = 0; i < MonsterArt.Palette.Length; i++)
            {
                var r = new Rect(bx + (i % 4) * (cs + 6 * k), by + (i / 4) * (cs + 6 * k), cs, cs);
                if (s_Ink == i) Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Color.white);
                Fill(r, MonsterArt.Palette[i]);
                if (e.type == EventType.MouseDown && r.Contains(m)) { s_Ink = i; e.Use(); }
            }
            by += 4 * (cs + 6 * k) + 14 * k;
            Shadowed(new Rect(bx, by, 300 * k, 24 * k), "BRUSH", m_Label);
            by += 26 * k;
            int[] brushes = { 0, 1, 2, 4, 7 };
            for (int i = 0; i < brushes.Length; i++)
            {
                var r = new Rect(bx + i * (cs + 6 * k), by, cs, cs);
                Fill(r, s_Brush == brushes[i] ? new Color(0.4f, 0.4f, 0.45f) : new Color(0.2f, 0.2f, 0.22f));
                float d = Mathf.Lerp(6f, cs - 6f, brushes[i] / 7f);
                DrawDot(new Rect(r.center.x - d / 2, r.center.y - d / 2, d, d), Color.white);
                if (e.type == EventType.MouseDown && r.Contains(m)) { s_Brush = brushes[i]; e.Use(); }
            }
            by += cs + 18 * k;
            if (BtnAt(new Rect(bx, by, 4 * cs + 18 * k, 40 * k), "Clear", m_Button)) { BeginDrawing(); }
            by += 50 * k;
            if (BtnAt(new Rect(bx, by, 4 * cs + 18 * k, 56 * k), "DONE - BRING IT TO LIFE!", m_Button)) pc.SubmitDrawing(DrawingPixels());
            by += 66 * k;
            if (BtnAt(new Rect(bx, by, 4 * cs + 18 * k, 34 * k), "Cancel (Esc)", m_Button)) pc.CloseWackyUi();
        }

        // ---------------- the scribble notebook ----------------

        static string s_Scribble = "";
        static bool s_ScribbleFocus;

        public static void BeginScribble() { s_Scribble = ""; s_ScribbleFocus = true; }

        void DrawNotebook(PlayerController pc)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            MouseOverUI = true;
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.6f));
            float w = 620 * k, h = 330 * k;
            var page = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            Fill(page, new Color(0.98f, 0.96f, 0.86f));
            for (float y = page.y + 70 * k; y < page.yMax - 10; y += 30 * k) Fill(new Rect(page.x, y, page.width, 1.5f), new Color(0.55f, 0.7f, 0.95f, 0.6f));
            Fill(new Rect(page.x + 56 * k, page.y, 2, page.height), new Color(0.95f, 0.45f, 0.45f, 0.7f));
            var ink = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(26 * k), alignment = TextAnchor.MiddleLeft };
            ink.normal.textColor = new Color(0.15f, 0.2f, 0.45f);
            GUI.Label(new Rect(page.x + 70 * k, page.y + 10 * k, w - 80 * k, 44 * k), "<b>SCRIBBLENAUTS NOTEBOOK</b>", ink);
            var small = new GUIStyle(m_Small) { wordWrap = true };
            small.normal.textColor = new Color(0.25f, 0.25f, 0.3f);
            GUI.Label(new Rect(page.x + 70 * k, page.y + 54 * k, w - 90 * k, 40 * k), "Write anything and it appears - and does something. Try a horse, a bomb, pizza, a tower, a unicorn, speed, a storm... or any item's name.", small);
            var field = new GUIStyle(m_Field) { fontSize = Mathf.RoundToInt(28 * k), alignment = TextAnchor.MiddleLeft };
            GUI.SetNextControlName("scribble");
            var fr = new Rect(page.x + 70 * k, page.y + 120 * k, w - 100 * k, 52 * k);
            var e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            s_Scribble = GUI.TextField(fr, s_Scribble, 28, field);
            if (s_ScribbleFocus) { GUI.FocusControl("scribble"); s_ScribbleFocus = false; }
            m_TypingFrame = Time.frameCount; // (the game's keys are off while you write)
            if (enter && s_Scribble.Trim().Length > 0) { pc.SubmitScribble(s_Scribble); e.Use(); return; }
            if (BtnAt(new Rect(page.x + 70 * k, page.y + 196 * k, 260 * k, 54 * k), "WRITE IT!", m_Button) && s_Scribble.Trim().Length > 0) pc.SubmitScribble(s_Scribble);
            if (BtnAt(new Rect(page.xMax - 210 * k, page.y + 196 * k, 180 * k, 54 * k), "Cancel (Esc)", m_Button)) pc.CloseWackyUi();
        }

        // ---------------- the minigames ----------------

        static PlayerNet PlayerById(ulong id)
        {
            foreach (var p in PlayerNet.All) if (p != null && p.NetworkObjectId == id) return p;
            return null;
        }

        static readonly Color k_RedPiece = new Color(0.92f, 0.2f, 0.18f), k_YellowPiece = new Color(1f, 0.85f, 0.15f);

        void DrawMinigame(PlayerNet me, NetGame game)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            MouseOverUI = true;
            var m = game.Mini.Value;
            double now = game.NetworkManager.ServerTime.Time;
            var a = PlayerById(m.A);
            var b = PlayerById(m.B);
            string an = a != null ? a.DisplayName : "?", bn = b != null ? b.DisplayName : "?";
            Fill(new Rect(0, 0, sw, sh), new Color(0.02f, 0.02f, 0.06f, 0.78f));
            var title = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(40 * k) };
            if (m.Kind == MiniGame.Connect4) DrawConnect4(me, m, now, an, bn, title, sw, sh, k);
            else DrawGameshow(me, m, now, an, bn, title, sw, sh, k);
        }

        void DrawConnect4(PlayerNet me, MiniGame m, double now, string an, string bn, GUIStyle title, float sw, float sh, float k)
        {
            Shadowed(new Rect(0, sh * 0.06f, sw, 50 * k), "<color=#ffd24a>CONNECT 4</color>", title);
            Shadowed(new Rect(0, sh * 0.06f + 48 * k, sw, 26 * k), "Four in a row wins - the LOSER DIES. Run out of time and you die. Everyone's watching.", m_Center);
            float cell = Mathf.Min(64 * k, sh * 0.09f);
            float bw = cell * MiniGame.Cols, bh = cell * MiniGame.Rows;
            var board = new Rect((sw - bw) / 2, sh * 0.2f + 50 * k, bw, bh);
            // the two players and their clocks
            float clockA = m.ClockA - (m.Phase == 0 && m.Turn == 0 ? (float)(now - m.TurnStart) : 0f);
            float clockB = m.ClockB - (m.Phase == 0 && m.Turn == 1 ? (float)(now - m.TurnStart) : 0f);
            void Side(Rect r, string name, float clock, Color piece, bool turn)
            {
                Fill(r, new Color(0, 0, 0, turn ? 0.7f : 0.4f));
                if (turn && m.Phase == 0) Fill(new Rect(r.x, r.yMax - 4, r.width, 4), piece);
                DrawDot(new Rect(r.x + 10 * k, r.y + 10 * k, 30 * k, 30 * k), piece);
                Shadowed(new Rect(r.x + 48 * k, r.y + 4 * k, r.width - 50 * k, 26 * k), $"<b>{name}</b>", m_Label);
                int c = Mathf.Max(0, Mathf.CeilToInt(clock));
                Shadowed(new Rect(r.x + 48 * k, r.y + 30 * k, r.width - 50 * k, 30 * k), $"<size={Mathf.RoundToInt(22 * k)}>{(c <= 10 ? "<color=#ff6a5a>" : "")}{c / 60}:{c % 60:00}{(c <= 10 ? "</color>" : "")}</size>", m_Label);
            }
            Side(new Rect(board.x - 250 * k, board.y, 230 * k, 66 * k), an, clockA, k_RedPiece, m.Turn == 0);
            Side(new Rect(board.xMax + 20 * k, board.y, 230 * k, 66 * k), bn, clockB, k_YellowPiece, m.Turn == 1);
            // the board
            Fill(new Rect(board.x - 8, board.y - 8, board.width + 16, board.height + 16), new Color(0.1f, 0.25f, 0.75f));
            ulong mover = m.Turn == 0 ? m.A : m.B;
            bool myMove = m.Phase == 0 && me != null && me.NetworkObjectId == mover;
            var e = Event.current;
            int hoverCol = -1;
            if (myMove && board.Contains(e.mousePosition)) hoverCol = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.x - board.x) / cell), 0, MiniGame.Cols - 1);
            for (int c = 0; c < MiniGame.Cols; c++)
            {
                if (c == hoverCol) Fill(new Rect(board.x + c * cell, board.y, cell, board.height), new Color(1, 1, 1, 0.12f));
                for (int r = 0; r < MiniGame.Rows; r++)
                {
                    var hole = new Rect(board.x + c * cell + cell * 0.1f, board.yMax - (r + 1) * cell + cell * 0.1f, cell * 0.8f, cell * 0.8f);
                    Color col = MiniGame.Has(m.BoardA, c, r) ? k_RedPiece : MiniGame.Has(m.BoardB, c, r) ? k_YellowPiece : new Color(0.04f, 0.05f, 0.1f);
                    DrawDot(hole, col);
                }
            }
            if (hoverCol >= 0 && m.Height(hoverCol) < MiniGame.Rows)
            {
                DrawDot(new Rect(board.x + hoverCol * cell + cell * 0.1f, board.y - cell * 0.95f, cell * 0.8f, cell * 0.8f), (m.Turn == 0 ? k_RedPiece : k_YellowPiece) * new Color(1, 1, 1, 0.6f));
                if (e.type == EventType.MouseDown && e.button == 0) { me.MiniMoveRpc(hoverCol); Sfx.Play2D(Sfx.Click, 0.6f); e.Use(); }
            }
            string line;
            if (m.Phase == 1)
            {
                var loser = PlayerById(m.Loser);
                string ln = loser != null ? loser.DisplayName : "";
                line = m.Why == 2 ? "<color=#ffffff>A DRAW - nobody dies!</color>" : m.Why == 3 ? "Called off" : m.Why == 1 ? $"<color=#ff6a5a>{ln} RAN OUT OF TIME - {ln} DIES!</color>" : $"<color=#ff6a5a>FOUR IN A ROW! {ln} DIES!</color>";
            }
            else line = myMove ? "<color=#7dff7d><b>YOUR MOVE</b> - click a column</color>" : $"Waiting for {(m.Turn == 0 ? an : bn)}...";
            Shadowed(new Rect(0, board.yMax + 22 * k, sw, 40 * k), line, new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(26 * k) });
        }

        void DrawGameshow(PlayerNet me, MiniGame m, double now, string an, string bn, GUIStyle title, float sw, float sh, float k)
        {
            // studio lights sweeping across
            for (int i = 0; i < 6; i++)
            {
                float x = Mathf.PingPong(Time.time * 220f + i * 260f, sw);
                Fill(new Rect(x - 40, 0, 80, sh), Color.HSVToRGB((i * 0.17f + Time.time * 0.1f) % 1f, 0.6f, 1f) * new Color(1, 1, 1, 0.05f));
            }
            Shadowed(new Rect(0, sh * 0.07f, sw, 52 * k), "<color=#ff7ad9>★ GAMESHOW! ★</color>", title);
            Shadowed(new Rect(0, sh * 0.07f + 52 * k, sw, 26 * k), $"<b>{bn}</b> answers. Right: <b>{an}</b> dies. Wrong (or too slow): <b>{bn}</b> dies.", m_Center);
            float left = m.Phase == 0 ? Mathf.Max(0f, m.ClockB - (float)(now - m.TurnStart)) : 0f;
            float w = Mathf.Min(900 * k, sw - 40), x0 = (sw - w) / 2, y = sh * 0.24f;
            Fill(new Rect(x0, y, w, 110 * k), new Color(0.15f, 0.05f, 0.3f, 0.95f));
            Fill(new Rect(x0, y, w, 4), new Color(1f, 0.5f, 0.9f));
            var q = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(28 * k), wordWrap = true };
            Shadowed(new Rect(x0 + 16, y + 8, w - 32, 94 * k), Trivia.Question(m.Question), q);
            y += 130 * k;
            bool mine = m.Phase == 0 && me != null && me.NetworkObjectId == m.B;
            var e = Event.current;
            int right = Trivia.Correct(m.Question);
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x0 + (i % 2) * (w / 2 + 8), y + (i / 2) * (84 * k), w / 2 - 8, 74 * k);
                bool hover = mine && r.Contains(e.mousePosition);
                Color bg = new Color(0.1f, 0.1f, 0.35f, 0.95f);
                if (m.Phase == 1) bg = i == right ? new Color(0.15f, 0.65f, 0.25f) : i == m.Answer ? new Color(0.75f, 0.15f, 0.15f) : new Color(0.1f, 0.1f, 0.2f, 0.9f);
                else if (hover) bg = new Color(0.3f, 0.25f, 0.6f);
                Fill(r, bg);
                Shadowed(new Rect(r.x + 14, r.y, r.width - 20, r.height), $"<b><color=#ffd24a>{(char)('A' + i)}</color></b>   {Trivia.Answer(m.Question, i)}", new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(22 * k), alignment = TextAnchor.MiddleLeft, wordWrap = true });
                if (hover && e.type == EventType.MouseDown && e.button == 0) { me.MiniMoveRpc(i); Sfx.Play2D(Sfx.Click, 0.6f); e.Use(); }
            }
            y += 180 * k;
            string line;
            if (m.Phase == 1)
            {
                var loser = PlayerById(m.Loser);
                string ln = loser != null ? loser.DisplayName : "";
                line = m.Why == 3 ? "Called off" : m.Why == 1 ? $"<color=#ff6a5a>TIME'S UP! {ln} DIES!</color>" : m.Answer == right ? $"<color=#7dff7d>CORRECT!</color> <color=#ff6a5a>{ln} DIES!</color>" : $"<color=#ff6a5a>WRONG! {ln} DIES!</color>";
            }
            else line = mine ? $"<color=#7dff7d><b>YOUR ANSWER</b></color> - click one   <color=#ffd24a>{Mathf.CeilToInt(left)}</color>" : $"{bn} is thinking...   <color=#ffd24a>{Mathf.CeilToInt(left)}</color>";
            Shadowed(new Rect(0, y, sw, 40 * k), line, new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(26 * k) });
        }
    }
}
