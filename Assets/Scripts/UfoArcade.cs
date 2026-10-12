using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// UFO DODGE: the arcade machine in the ship lobby (where the Trade Station stood on the right - ShipLobby.Arcade.cs),
    /// a little pixel game in the telly's style (LobbyArcade.cs: point filtered, scanlines, the 3x5 font). Click the
    /// machine and the camera moves in on its screen: you fly the saucer from the main menu, Flappy Bird style - Space
    /// (or a click, W, Up) gives it a burst of thrust, gravity pulls it down - through gaps in walls of space junk
    /// (asteroid columns, planets, enemy saucer convoys), while enemy ships wobble at you and meteors streak across.
    /// Every gap is +1. It speeds up as you go. Juice: squash and tilt on the saucer, a thruster trail, sparks, a shake,
    /// a white flash and a burst of debris when you crash, popping "+1"s, a CLOSE! for a near miss, a parallax starfield
    /// and a nebula going by. Game over shows your score, your best, and the LEADERBOARD of your Steam friends' scores
    /// (a Steam leaderboard, ArcadeBoard; without Steam just your own best). Nobody on this PC playing: it plays itself
    /// (the attract mode) and shows the high scores now and then. Only this PC's game (nothing is networked).
    /// The screen lights the room like the telly does (ShipLobby.Arcade.cs reads Glow).
    /// Lives on the Bootstrap object next to ShipLobby and LobbyArcade.
    /// </summary>
    public class UfoArcade : MonoBehaviour
    {
        /// <summary>The picture (4:3, like the cabinet's screen).</summary>
        public const int W = 160, H = 120;
        static UfoArcade s_I;

        public static void Ensure(GameObject host)
        {
            if (s_I == null) s_I = host.GetComponent<UfoArcade>() ?? host.AddComponent<UfoArcade>();
        }

        void Awake() => s_I = this;

        // ================================================================== focus (the camera on the machine)

        /// <summary>The camera's on the arcade machine and you're playing it.</summary>
        public static bool Focused { get; private set; }

        public static void Focus(bool on)
        {
            if (Focused == on) return;
            Focused = on;
            if (on && LobbyArcade.Focused) LobbyArcade.Focus(false); // (one game at a time)
            if (s_I != null) s_I.Begin(on ? St.Ready : St.Attract);
            if (on) { ArcadeBoard.Refresh(); Sfx.Play2D(Sfx.UiClick, 0.5f); }
        }

        // ================================================================== the game

        enum St { Attract, Ready, Play, Dead }
        St m_St = St.Attract;
        float m_StT;

        // the saucer
        const float UfoX = 38f, UfoR = 4.1f, Grav = 310f, FlapV = -108f, MaxFall = 175f, Floor = H - 9f;
        float m_Y = H * 0.45f, m_Vy, m_Squash, m_Tilt, m_Thrust;

        int m_Score;
        bool m_NewBest;
        float m_Speed, m_Dist, m_NextGateAt, m_Time, m_Shake, m_Flash, m_Acc, m_BgX;
        Color32 m_FlashC = new Color32(255, 255, 255, 255);
        System.Random m_Rng = new System.Random(7);
        float R01() => (float)m_Rng.NextDouble();

        /// <summary>A round thing in a wall of junk: kind 0 asteroid, 1 planet, 2 enemy saucer (x off the wall's x).</summary>
        class Body { public float Dx, Y, R; public byte Kind, Look; public bool Ring; }
        class Gate { public float X, GapY, Gap, Clear = 99f; public bool Passed; public readonly List<Body> Bodies = new List<Body>(); public float Half; }
        /// <summary>A loose hazard: kind 0 an enemy saucer wobbling along, 1 a meteor streaking down across.</summary>
        class Mover { public Vector2 Pos, Vel; public float R, BaseY, Phase; public byte Kind; }
        class Spark { public Vector2 Pos, Vel; public float Life, Max; public Color32 C; public bool Add; }
        class Pop { public Vector2 Pos; public string Text; public float Life; public Color32 C; public int Scale; }
        readonly List<Gate> m_Gates = new List<Gate>();
        readonly List<Mover> m_Movers = new List<Mover>();
        readonly List<Spark> m_Sparks = new List<Spark>();
        readonly List<Pop> m_Pops = new List<Pop>();

        /// <summary>(tests) the score of the game going on (or the last one), how many games and flaps on this PC.</summary>
        public static int Score => s_I != null ? s_I.m_Score : 0;
        public static int Games { get; private set; }
        public static int Flaps { get; private set; }
        public static bool Crashed => s_I != null && s_I.m_St == St.Dead;
        public static bool Playing => s_I != null && s_I.m_St == St.Play && Focused;
        /// <summary>(tests) flap once (as if Space were pressed); fly by itself (the attract mode's pilot) while playing.</summary>
        public static bool TestFlap, TestAutoPilot;
        /// <summary>(tests) how many times the picture's been drawn, and a hash of the last one.</summary>
        public static int Redraws { get; private set; }
        public static int PictureHash { get; private set; }

        void Begin(St st)
        {
            m_St = st;
            m_StT = 0f;
            m_Gates.Clear();
            m_Movers.Clear();
            m_Pops.Clear();
            m_Score = 0;
            m_NewBest = false;
            m_Y = H * 0.45f;
            m_Vy = 0f;
            m_Speed = 46f;
            m_Dist = 0f;
            m_NextGateAt = 70f;
            m_Shake = 0f;
        }

        float GateSpeed => Mathf.Min(84f, 46f + m_Score * 1.25f);
        float GapSize => Mathf.Max(31f, 45f - m_Score * 0.55f);
        const float Spacing = 80f;

        void Flap()
        {
            m_Vy = FlapV;
            m_Squash = 1f;
            m_Thrust = 1f;
            if (m_St == St.Play && m_St != St.Attract && Focused) { Flaps++; Sfx.Play2D(Sfx.Jet, 0.18f, 0.12f); }
            // a puff of blue out of the bottom
            for (int i = 0; i < 6; i++)
                m_Sparks.Add(new Spark { Pos = new Vector2(UfoX + R01() * 6f - 3f, m_Y + 3f), Vel = new Vector2(-20f - R01() * 25f, 30f + R01() * 40f), Life = 0f, Max = 0.25f + R01() * 0.2f, C = new Color32(110, 200, 255, 255), Add = true });
        }

        void SpawnGate()
        {
            var g = new Gate { X = W + 30f, Gap = GapSize };
            float margin = 16f + g.Gap * 0.5f;
            g.GapY = Mathf.Lerp(margin, Floor - margin + 4f, R01());
            float top = g.GapY - g.Gap * 0.5f, bot = g.GapY + g.Gap * 0.5f;
            int kind = m_Score < 2 ? 0 : m_Rng.Next(0, m_Score < 6 ? 2 : 3);
            if (kind == 1)
            {
                // planets: one hanging over the gap, one under it - their edges are the gap
                float r1 = 14f + R01() * 14f, r2 = 14f + R01() * 14f;
                g.Bodies.Add(new Body { Dx = R01() * 6f - 3f, Y = top - r1, R = r1, Kind = 1, Look = (byte)m_Rng.Next(0, 6), Ring = R01() < 0.5f });
                g.Bodies.Add(new Body { Dx = R01() * 6f - 3f, Y = bot + r2, R = r2, Kind = 1, Look = (byte)m_Rng.Next(0, 6), Ring = R01() < 0.35f });
                g.Half = Mathf.Max(r1, r2) * 0.75f;
            }
            else
            {
                // a column of asteroids (or of enemy saucers in a convoy) above the gap and under it
                byte k = (byte)(kind == 2 ? 2 : 0);
                float y = top;
                while (y > -8f)
                {
                    float r = k == 2 ? 5f : 4.5f + R01() * 3f;
                    g.Bodies.Add(new Body { Dx = R01() * 4f - 2f, Y = y - r, R = r, Kind = k, Look = (byte)m_Rng.Next(0, 255) });
                    y -= r * 2f - 1f;
                }
                y = bot;
                while (y < H + 8f)
                {
                    float r = k == 2 ? 5f : 4.5f + R01() * 3f;
                    g.Bodies.Add(new Body { Dx = R01() * 4f - 2f, Y = y + r, R = r, Kind = k, Look = (byte)m_Rng.Next(0, 255) });
                    y += r * 2f - 1f;
                }
                g.Half = 7f;
            }
            m_Gates.Add(g);
            // something loose coming at you between this wall and the next one (later on)
            if (m_Score >= 4 && R01() < Mathf.Min(0.6f, 0.25f + m_Score * 0.02f))
            {
                if (m_Score >= 9 && R01() < 0.45f)
                    m_Movers.Add(new Mover { Kind = 1, Pos = new Vector2(W + 30f + Spacing * 0.5f + R01() * 40f, -6f), Vel = new Vector2(-38f - R01() * 20f, 26f + R01() * 14f), R = 3f });
                else
                {
                    float y0 = Mathf.Lerp(20f, Floor - 20f, R01());
                    m_Movers.Add(new Mover { Kind = 0, Pos = new Vector2(W + 30f + Spacing * 0.5f, y0), BaseY = y0, Vel = new Vector2(-28f - R01() * 18f, 0f), R = 4.2f, Phase = R01() * 6f });
                }
            }
        }

        void Crash()
        {
            m_St = St.Dead;
            m_StT = 0f;
            m_Shake = 0.5f;
            m_Flash = 1f;
            m_FlashC = new Color32(255, 255, 255, 255);
            for (int i = 0; i < 34; i++)
            {
                float a = R01() * Mathf.PI * 2f, sp = 20f + R01() * 90f;
                var c = i % 3 == 0 ? new Color32(255, 200, 80, 255) : i % 3 == 1 ? new Color32(190, 196, 210, 255) : new Color32(90, 220, 240, 255);
                m_Sparks.Add(new Spark { Pos = new Vector2(UfoX, m_Y), Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, Max = 0.5f + R01() * 0.7f, C = c, Add = i % 3 == 0 });
            }
            if (Focused)
            {
                Sfx.Play2D(Sfx.Boom, 0.45f, 0.05f);
                Games++;
                int before = ArcadeBoard.LocalBest;
                m_NewBest = m_Score > before && m_Score > 0;
                ArcadeBoard.Submit(m_Score);
                if (m_NewBest) Sfx.Play2D(Sfx.Unlock, 0.5f, 0f);
            }
        }

        void Step(float dt, bool flap)
        {
            m_Time += dt;
            m_StT += dt;
            m_Shake = Mathf.Max(0f, m_Shake - dt);
            m_Flash = Mathf.Max(0f, m_Flash - dt * 3.5f);
            m_Squash = Mathf.Max(0f, m_Squash - dt * 5f);
            m_Thrust = Mathf.Max(0f, m_Thrust - dt * 4f);
            float scroll = m_St == St.Play || m_St == St.Attract ? m_Speed : m_St == St.Ready ? 30f : 8f;
            m_BgX += scroll * dt;

            // sparks and pops
            for (int i = m_Sparks.Count - 1; i >= 0; i--)
            {
                var s = m_Sparks[i];
                s.Life += dt;
                if (s.Life >= s.Max) { m_Sparks.RemoveAt(i); continue; }
                s.Pos += s.Vel * dt;
                s.Vel *= 1f - dt * 2.5f;
            }
            for (int i = m_Pops.Count - 1; i >= 0; i--)
            {
                var p = m_Pops[i];
                p.Life += dt;
                p.Pos.y -= dt * 14f;
                if (p.Life > 0.9f) m_Pops.RemoveAt(i);
            }

            switch (m_St)
            {
                case St.Ready:
                    // bobbing in place, waiting for the first flap
                    m_Y = H * 0.45f + Mathf.Sin(m_StT * 3f) * 3f;
                    m_Vy = Mathf.Cos(m_StT * 3f) * 9f;
                    Trail(dt);
                    if (flap) { m_St = St.Play; m_StT = 0f; Flap(); }
                    return;
                case St.Dead:
                    // the wreck falls; a flap after a moment starts again
                    m_Vy = Mathf.Min(MaxFall, m_Vy + Grav * dt);
                    m_Y = Mathf.Min(Floor + 6f, m_Y + m_Vy * dt);
                    if (Focused && flap && m_StT > 0.9f) Begin(St.Ready);
                    else if (!Focused && m_StT > 2.5f) Begin(St.Attract);
                    return;
            }

            // playing (or the attract mode, playing itself)
            if ((m_St == St.Attract || TestAutoPilot) && AiWantsFlap()) flap = true;
            else if (m_St == St.Attract) flap = false;
            if (flap) Flap();
            m_Vy = Mathf.Min(MaxFall, m_Vy + Grav * dt);
            m_Y += m_Vy * dt;
            if (m_Y < 5f) { m_Y = 5f; m_Vy = Mathf.Max(0f, m_Vy); }
            m_Tilt = Mathf.Lerp(m_Tilt, Mathf.Clamp(m_Vy * 0.0035f, -0.4f, 0.55f), 1f - Mathf.Exp(-12f * dt));
            Trail(dt);

            m_Speed = GateSpeed;
            m_Dist += m_Speed * dt;
            if (m_Dist >= m_NextGateAt) { m_NextGateAt += Spacing; SpawnGate(); }

            bool hit = m_Y > Floor;
            for (int gi = m_Gates.Count - 1; gi >= 0; gi--)
            {
                var g = m_Gates[gi];
                g.X -= m_Speed * dt;
                if (g.X < -70f) { m_Gates.RemoveAt(gi); continue; }
                foreach (var b in g.Bodies)
                {
                    float d = new Vector2(g.X + b.Dx - UfoX, b.Y - m_Y).magnitude - b.R - UfoR;
                    if (d < 0f) hit = true;
                    if (Mathf.Abs(g.X - UfoX) < g.Half + 6f) g.Clear = Mathf.Min(g.Clear, d);
                }
                if (!g.Passed && g.X + g.Half < UfoX - UfoR)
                {
                    g.Passed = true;
                    m_Score++;
                    m_Pops.Add(new Pop { Pos = new Vector2(UfoX + 4f, m_Y - 10f), Text = "+1", C = new Color32(255, 230, 90, 255), Scale = 1 });
                    if (Focused && m_St == St.Play) Sfx.Play2D(Sfx.Ding, 0.32f, 0.04f);
                    if (g.Clear < 3f)
                    {
                        // a near miss: CLOSE! and a little shower of gold
                        m_Pops.Add(new Pop { Pos = new Vector2(UfoX - 8f, m_Y + 6f), Text = "CLOSE!", C = new Color32(120, 255, 200, 255), Scale = 1 });
                        for (int i = 0; i < 8; i++) m_Sparks.Add(new Spark { Pos = new Vector2(UfoX, m_Y), Vel = new Vector2(R01() * 60f - 30f, R01() * 60f - 30f), Max = 0.4f, C = new Color32(255, 230, 120, 255), Add = true });
                    }
                    if (m_Score % 10 == 0)
                    {
                        m_Pops.Add(new Pop { Pos = new Vector2(W * 0.5f - 18f, 40f), Text = "SPEED UP!", C = new Color32(255, 120, 200, 255), Scale = 1 });
                        m_Flash = 0.35f;
                        m_FlashC = new Color32(255, 120, 220, 255);
                    }
                }
            }
            for (int i = m_Movers.Count - 1; i >= 0; i--)
            {
                var m = m_Movers[i];
                m.Pos.x += (m.Vel.x - (m.Kind == 0 ? m_Speed : m_Speed * 0.6f)) * dt;
                if (m.Kind == 0) m.Pos.y = m.BaseY + Mathf.Sin(m_Time * 3.2f + m.Phase) * 9f;
                else
                {
                    m.Pos.y += m.Vel.y * dt;
                    if (R01() < 0.7f) m_Sparks.Add(new Spark { Pos = m.Pos, Vel = new Vector2(R01() * 16f + 10f, -R01() * 14f), Max = 0.3f, C = new Color32(255, (byte)(120 + R01() * 100), 40, 255), Add = true });
                }
                if (m.Pos.x < -20f || m.Pos.y > H + 20f) { m_Movers.RemoveAt(i); continue; }
                if ((m.Pos - new Vector2(UfoX, m_Y)).magnitude < m.R + UfoR - 0.5f) hit = true;
            }
            if (hit) Crash();
        }

        /// <summary>The attract mode's pilot: aims for the middle of the next gap, a little low (it flaps up into it).</summary>
        bool AiWantsFlap()
        {
            float target = H * 0.45f;
            foreach (var g in m_Gates)
                if (g.X + g.Half > UfoX - 6f) { target = g.GapY + 3f; break; }
            foreach (var m in m_Movers)
                if (m.Pos.x > UfoX && m.Pos.x - UfoX < 30f && Mathf.Abs(m.Pos.y - m_Y) < 10f) target += m.Pos.y > m_Y ? -8f : 8f;
            return m_Y > target && m_Vy > -15f;
        }

        void Trail(float dt)
        {
            // the saucer's blue exhaust trail, out behind it
            if (R01() < 0.8f)
                m_Sparks.Add(new Spark { Pos = new Vector2(UfoX - 7f, m_Y + 1f + R01() * 2f - 1f), Vel = new Vector2(-m_Speed * 0.6f - 10f - R01() * 10f, R01() * 6f - 3f), Max = 0.35f + R01() * 0.2f, C = new Color32(80, 170, 255, 255), Add = true });
        }

        // ================================================================== every frame

        float m_NextDraw, m_BoardShowAt;

        void Update()
        {
            float now = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (!ShipLobby.Active)
            {
                if (Focused) Focus(false);
                return;
            }
            bool flap = TestFlap;
            TestFlap = false;
            if (Focused && !Chat.Open && GUIUtility.keyboardControl == 0 && Application.isFocused)
                flap |= Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || (Input.GetMouseButtonDown(0) && !Hud.MouseOverUI);
            if (!Focused && m_St != St.Attract && m_St != St.Dead) Begin(St.Attract);
            // fixed steps (the same game at any frame rate); a flap goes in the first one
            m_Acc += dt;
            const float Fixed = 1f / 120f;
            int steps = 0;
            while (m_Acc >= Fixed && steps < 24)
            {
                m_Acc -= Fixed;
                Step(Fixed, flap);
                flap = false;
                steps++;
            }
            if (Focused) ArcadeBoard.Tick();
            if (now >= m_NextDraw)
            {
                m_NextDraw = now + (Focused ? 1f / 45f : 1f / 20f);
                Draw(now);
            }
        }

        // ================================================================== the picture

        Texture2D m_Tex;
        Material m_Mat;
        readonly Color32[] m_Px = new Color32[W * H];
        Color32[] m_Bg;
        float[] m_Vignette;
        const int BgW = 320;
        Vector3[] m_Stars;

        /// <summary>The screen's material (the picture, lit by itself).</summary>
        public static Material ScreenMat
        {
            get
            {
                if (s_I == null) return null;
                s_I.MakeTex();
                return s_I.m_Mat;
            }
        }

        /// <summary>The picture's average colour: the machine's light on the room follows it.</summary>
        public static Color Glow { get; private set; } = new Color(0.35f, 0.3f, 0.6f);

        const float ScreenBright = 1.6f;
        float m_BrightSet = -1f;

        /// <summary>The picture's brightness x k (ShipLobby: Settings > Display > SHIP LOBBY > Telly).</summary>
        public static void SetScreenBrightness(float k)
        {
            if (s_I == null || s_I.m_Mat == null || Mathf.Approximately(s_I.m_BrightSet, k)) return;
            s_I.m_BrightSet = k;
            if (s_I.m_Mat.HasProperty("_Intensity")) s_I.m_Mat.SetFloat("_Intensity", ScreenBright * k);
            else if (s_I.m_Mat.HasProperty("_EmissionColor")) s_I.m_Mat.SetColor("_EmissionColor", Color.white * ScreenBright * k);
        }

        void MakeTex()
        {
            if (m_Tex != null) return;
            m_Tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "ufoArcade" };
            BuildBackdrop();
            var screen = Resources.Load<Shader>("World/Screen");
            if (screen != null && screen.isSupported)
            {
                m_Mat = new Material(screen) { name = "ufo arcade" };
                m_Mat.SetTexture("_MainTex", m_Tex);
                m_Mat.SetFloat("_Intensity", ScreenBright);
            }
            else
            {
                var m = Art.NewMat(Color.white);
                m.name = "ufo arcade";
                m.SetTexture("_BaseMap", m_Tex);
                m.mainTexture = m_Tex;
                if (m.HasProperty("_EmissionMap") && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetTexture("_EmissionMap", m_Tex);
                    m.SetColor("_EmissionColor", Color.white * 1.2f);
                }
                m_Mat = m;
            }
            Draw(Time.time);
        }

        /// <summary>The nebula behind it all (twice the screen's width, wrapped round as it scrolls), the stars, the CRT's dark corners.</summary>
        void BuildBackdrop()
        {
            m_Bg = new Color32[BgW * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < BgW; x++)
                {
                    float u = x / (float)BgW, v = y / (float)H;
                    // (wraps round: the noise sampled on a circle across)
                    float a = u * Mathf.PI * 2f;
                    float n1 = Mathf.PerlinNoise(3f + Mathf.Cos(a) * 1.6f, 5f + Mathf.Sin(a) * 1.6f + v * 2.2f);
                    float n2 = Mathf.PerlinNoise(11f + Mathf.Cos(a) * 3.1f, 2f + Mathf.Sin(a) * 3.1f + v * 4f);
                    float neb = Mathf.Clamp01((n1 * 0.7f + n2 * 0.5f) - 0.55f) * 2.2f;
                    var deep = Color.Lerp(new Color(0.02f, 0.02f, 0.07f), new Color(0.06f, 0.02f, 0.1f), v);
                    var c = deep + Color.Lerp(new Color(0.35f, 0.08f, 0.45f), new Color(0.08f, 0.25f, 0.5f), n2) * neb * 0.55f;
                    m_Bg[y * BgW + x] = new Color32((byte)Mathf.Clamp(c.r * 255f, 0, 255), (byte)Mathf.Clamp(c.g * 255f, 0, 255), (byte)Mathf.Clamp(c.b * 255f, 0, 255), 255);
                }
            var rng = new System.Random(3);
            m_Stars = new Vector3[80];
            for (int i = 0; i < m_Stars.Length; i++) m_Stars[i] = new Vector3((float)rng.NextDouble() * W, (float)rng.NextDouble() * (H - 10), i % 3);
            m_Vignette = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = (x - W * 0.5f) / (W * 0.5f), dy = (y - H * 0.5f) / (H * 0.5f);
                    m_Vignette[y * W + x] = Mathf.Clamp01(1.08f - (dx * dx * dx * dx + dy * dy * dy * dy) * 0.45f);
                }
        }

        void OnDestroy()
        {
            if (m_Tex) Destroy(m_Tex);
            if (m_Mat) Destroy(m_Mat);
        }

        int m_Ox, m_Oy;

        void Px(int x, int y, Color32 c) { if (x >= 0 && x < W && y >= 0 && y < H) m_Px[(H - 1 - y) * W + x] = c; }
        /// <summary>A world point (shaken).</summary>
        void PxW(int x, int y, Color32 c) => Px(x + m_Ox, y + m_Oy, c);
        void Add(int x, int y, Color32 c, float a)
        {
            x += m_Ox; y += m_Oy;
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            int i = (H - 1 - y) * W + x;
            var o = m_Px[i];
            m_Px[i] = new Color32((byte)Mathf.Min(255, o.r + c.r * a), (byte)Mathf.Min(255, o.g + c.g * a), (byte)Mathf.Min(255, o.b + c.b * a), 255);
        }
        void Blend(int x, int y, Color32 c, float a, bool shaken = true)
        {
            if (shaken) { x += m_Ox; y += m_Oy; }
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            int i = (H - 1 - y) * W + x;
            m_Px[i] = Color32.Lerp(m_Px[i], c, a);
        }
        void Rect(int x, int y, int w, int h, Color32 c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Px(x + i, y + j, c); }
        void Shade(int x, int y, int w, int h, float a) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Blend(x + i, y + j, new Color32(0, 0, 0, 255), a, false); }

        static Color32 C(float r, float g, float b) => new Color32((byte)Mathf.Clamp(r * 255f, 0, 255), (byte)Mathf.Clamp(g * 255f, 0, 255), (byte)Mathf.Clamp(b * 255f, 0, 255), 255);
        static Color32 Mul(Color32 c, float k) => new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), 255);

        /// <summary>A shaded ball (lit from the top left, a dark rim): asteroids, planets, saucers' domes.</summary>
        void Ball(float cx, float cy, float r, Color32 baseC, int look, bool craters, bool bands)
        {
            int x0 = Mathf.FloorToInt(cx - r - 1), x1 = Mathf.CeilToInt(cx + r + 1), y0 = Mathf.FloorToInt(cy - r - 1), y1 = Mathf.CeilToInt(cy + r + 1);
            for (int y = Mathf.Max(y0, -m_Oy - 2); y <= Mathf.Min(y1, H - m_Oy + 2); y++)
                for (int x = Mathf.Max(x0, -m_Ox - 2); x <= Mathf.Min(x1, W - m_Ox + 2); x++)
                {
                    float dx = (x + 0.5f - cx) / r, dy = (y + 0.5f - cy) / r;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > 1f) continue;
                    float z = Mathf.Sqrt(1f - d2);
                    float l = Mathf.Clamp01(-dx * 0.55f - dy * 0.6f + z * 0.6f) * 0.85f + 0.2f;
                    float k = l;
                    if (bands) k *= 0.85f + 0.15f * Mathf.Sin((dy * 6f + look) * 1.3f + Mathf.Sin(dx * 3f + look) * 0.6f);
                    if (craters)
                    {
                        // a few dark pits, fixed by its look
                        for (int c = 0; c < 3; c++)
                        {
                            float px = Mathf.Sin(look * 1.7f + c * 2.1f) * 0.5f, py = Mathf.Cos(look * 2.3f + c * 1.3f) * 0.5f;
                            float cd = (dx - px) * (dx - px) + (dy - py) * (dy - py);
                            if (cd < 0.06f) k *= 0.65f;
                        }
                    }
                    if (d2 > 0.8f) k *= 0.7f; // (the rim)
                    PxW(x, y, Mul(baseC, k));
                }
        }

        /// <summary>A planet's ring (an ellipse band); `front` = the half in front of the planet.</summary>
        void Ring(float cx, float cy, float r, bool front, Color32 c)
        {
            float rx = r * 1.7f, ry = r * 0.32f;
            for (int i = 0; i < 160; i++)
            {
                float a = i / 160f * Mathf.PI * 2f;
                float s = Mathf.Sin(a);
                if ((s > 0f) != front) continue;
                for (float w = 0.86f; w <= 1.01f; w += 0.07f)
                {
                    int x = Mathf.RoundToInt(cx + Mathf.Cos(a) * rx * w), y = Mathf.RoundToInt(cy + s * ry * w);
                    PxW(x, y, Mul(c, 0.8f + 0.2f * w));
                }
            }
        }

        static readonly Color32[] k_PlanetC = { C(0.85f, 0.45f, 0.3f), C(0.35f, 0.6f, 0.9f), C(0.55f, 0.85f, 0.45f), C(0.8f, 0.35f, 0.75f), C(0.95f, 0.8f, 0.4f), C(0.45f, 0.85f, 0.85f) };
        static readonly Color32 k_Rock = C(0.5f, 0.44f, 0.4f), k_White = C(0.95f, 0.95f, 0.95f), k_Black = C(0.02f, 0.02f, 0.03f), k_Gold = C(1f, 0.85f, 0.25f);

        /// <summary>The saucer from the main menu (silver, a teal glass dome, a ring of lights going round, blue thrust under
        /// it), tilted and squashed: drawn by mapping each pixel back into its little shape.</summary>
        void Saucer(float cx, float cy, float tilt, float squash, bool enemy, float t)
        {
            float sx = 1f + squash * 0.25f, sy = 1f - squash * 0.22f, sc = enemy ? 0.82f : 1f;
            float cs = Mathf.Cos(-tilt), sn = Mathf.Sin(-tilt);
            for (int y = -9; y <= 9; y++)
                for (int x = -11; x <= 11; x++)
                {
                    // back into the saucer's own space
                    float wx = x + 0.5f, wy = y + 0.5f;
                    float u = (wx * cs - wy * sn) / (sx * sc), v = (wx * sn + wy * cs) / (sy * sc);
                    Color32 c;
                    if (!SaucerAt(u, v, enemy, t, out c)) continue;
                    PxW(Mathf.FloorToInt(cx) + x, Mathf.FloorToInt(cy) + y, c);
                }
        }

        static bool SaucerAt(float u, float v, bool enemy, float t, out Color32 c)
        {
            c = default;
            // the dome: a half ball on top
            float dx = u / 3.4f, dy = (v + 1.2f) / 3.2f;
            if (v < -0.8f && dx * dx + dy * dy <= 1f)
            {
                bool hi = u < -0.4f && u > -2f && v < -2.4f && v > -3.6f;
                c = hi ? new Color32(220, 255, 255, 255) : enemy ? (v < -2.5f ? C(0.5f, 1f, 0.45f) : C(0.25f, 0.7f, 0.25f)) : (v < -2.5f ? C(0.45f, 0.9f, 1f) : C(0.2f, 0.55f, 0.7f));
                return true;
            }
            // the disc: wide and flat, lit on top, dark underneath
            float ex = u / 8.4f, ey = (v - 0.4f) / 2.3f;
            float e = ex * ex + ey * ey;
            if (e > 1f) return false;
            if (e > 0.82f && v > 0.4f) { c = enemy ? C(0.25f, 0.06f, 0.06f) : C(0.15f, 0.16f, 0.2f); return true; } // (the rim's shadow)
            if (v < 0f) c = enemy ? C(0.85f, 0.3f, 0.28f) : C(0.82f, 0.85f, 0.9f);
            else if (v < 1.2f) c = enemy ? C(0.6f, 0.15f, 0.15f) : C(0.58f, 0.6f, 0.67f);
            else c = enemy ? C(0.32f, 0.08f, 0.08f) : C(0.3f, 0.32f, 0.38f);
            // the ring of lights along the middle, chasing round
            if (v >= 0f && v < 1.2f)
            {
                int slot = Mathf.FloorToInt((u + 8f) / 2.6f);
                if (Mathf.Abs(u - (slot * 2.6f - 8f + 1.3f)) < 0.7f)
                {
                    int lit = Mathf.FloorToInt(t * 8f) % 7;
                    c = enemy ? (slot % 2 == lit % 2 ? C(1f, 0.95f, 0.3f) : C(0.5f, 0.2f, 0.1f))
                        : slot == lit || slot == (lit + 3) % 7 ? C(1f, 0.95f, 0.55f) : slot % 2 == 0 ? C(0.4f, 0.95f, 1f) : C(1f, 0.4f, 0.85f);
                }
            }
            return true;
        }

        void Draw(float now)
        {
            MakeTex();
            Redraws++;
            float sh = m_Shake > 0f ? m_Shake * 6f : 0f;
            m_Ox = sh > 0f ? Mathf.RoundToInt((Mathf.PerlinNoise(now * 40f, 1f) - 0.5f) * sh) : 0;
            m_Oy = sh > 0f ? Mathf.RoundToInt((Mathf.PerlinNoise(now * 40f, 7f) - 0.5f) * sh) : 0;

            // the nebula (slow) and the stars (three layers, faster the nearer)
            int bgOff = Mathf.FloorToInt(m_BgX * 0.08f);
            for (int y = 0; y < H; y++)
            {
                int row = (H - 1 - y) * W;
                for (int x = 0; x < W; x++) m_Px[row + x] = m_Bg[y * BgW + (((x + bgOff) % BgW) + BgW) % BgW];
            }
            for (int i = 0; i < m_Stars.Length; i++)
            {
                var s = m_Stars[i];
                float sp = s.z == 0 ? 0.12f : s.z == 1 ? 0.3f : 0.65f;
                float x = Mathf.Repeat(s.x - m_BgX * sp, W);
                float tw = 0.55f + 0.45f * Mathf.Sin(now * (2f + i % 5) + i);
                var c = s.z == 2 ? k_White : s.z == 1 ? C(0.7f, 0.75f, 0.95f) : C(0.45f, 0.45f, 0.6f);
                Add(Mathf.FloorToInt(x), Mathf.FloorToInt(s.y), c, tw);
                if (s.z == 2 && (m_St == St.Play || m_St == St.Attract)) Add(Mathf.FloorToInt(x) + 1, Mathf.FloorToInt(s.y), c, tw * 0.4f); // (streaking)
            }
            // the planet surface along the bottom: a glowing horizon (fly into it and you burn up)
            for (int x = 0; x < W; x++)
            {
                float dx = (x - W * 0.5f) / (W * 0.5f);
                int top = Mathf.RoundToInt(Floor + 3f + dx * dx * 4f);
                for (int y = top - 3; y < H; y++)
                {
                    if (y < top) { Add(x, y, C(1f, 0.45f, 0.2f), 0.25f * (y - top + 4) / 4f); continue; }
                    float n = Mathf.PerlinNoise((x + m_BgX * 0.5f) * 0.12f, y * 0.3f);
                    var c = y == top ? C(1f, 0.75f, 0.4f) : Color32.Lerp(C(0.55f, 0.18f, 0.12f), C(0.3f, 0.08f, 0.1f), n);
                    PxW(x, y, c);
                }
            }

            // the walls of junk
            foreach (var g in m_Gates)
                foreach (var b in g.Bodies)
                {
                    float cx = g.X + b.Dx;
                    if (cx + b.R * 2f < -4f || cx - b.R * 2f > W + 4f) continue;
                    if (b.Kind == 1)
                    {
                        var pc = k_PlanetC[b.Look % k_PlanetC.Length];
                        if (b.Ring) Ring(cx, b.Y, b.R, false, Mul(pc, 1.25f));
                        Ball(cx, b.Y, b.R, pc, b.Look * 3 + 1, false, true);
                        if (b.Ring) Ring(cx, b.Y, b.R, true, Mul(pc, 1.25f));
                    }
                    else if (b.Kind == 2) Saucer(cx, b.Y, Mathf.Sin(now * 2f + b.Look) * 0.15f, 0f, true, now + b.Look);
                    else Ball(cx, b.Y, b.R, Mul(k_Rock, 0.85f + (b.Look % 7) * 0.04f), b.Look, true, false);
                }
            foreach (var m in m_Movers)
            {
                if (m.Kind == 0)
                {
                    Saucer(m.Pos.x, m.Pos.y, Mathf.Cos(m_Time * 3.2f + m.Phase) * 0.25f, 0f, true, now + m.Phase);
                    // its warning: a blinking ! at the right edge before it's on screen
                    if (m.Pos.x > W - 2f && Mathf.Repeat(now * 6f, 1f) < 0.6f) Text("!", W - 6, Mathf.Clamp(Mathf.RoundToInt(m.Pos.y) - 2, 2, H - 8), C(1f, 0.3f, 0.3f), 1);
                }
                else
                {
                    Ball(m.Pos.x, m.Pos.y, m.R, C(1f, 0.75f, 0.35f), 3, false, false);
                    Add(Mathf.RoundToInt(m.Pos.x) - 1, Mathf.RoundToInt(m.Pos.y) - 1, k_White, 0.8f);
                }
            }

            // sparks (the glowing ones add light)
            foreach (var s in m_Sparks)
            {
                float a = 1f - s.Life / s.Max;
                int x = Mathf.FloorToInt(s.Pos.x), y = Mathf.FloorToInt(s.Pos.y);
                if (s.Add) Add(x, y, s.C, a);
                else if (a > 0.3f) PxW(x, y, s.C);
            }

            // the saucer (a wreck tumbling once it's crashed)
            if (m_St != St.Dead || Mathf.Repeat(now * 10f, 1f) < 0.5f || m_StT < 0.2f)
            {
                float tilt = m_St == St.Dead ? m_StT * 6f : m_Tilt;
                // its thrust: a cone of blue light down from under it just after a flap
                if (m_Thrust > 0.05f && m_St != St.Dead)
                    for (int k = 0; k < 7; k++)
                        for (int w = -2; w <= 2; w++)
                            if (Mathf.Abs(w) <= k / 2 + 1) Add(Mathf.FloorToInt(UfoX) + w, Mathf.FloorToInt(m_Y) + 4 + k, C(0.4f, 0.75f, 1f), m_Thrust * (1f - k / 7f) * (0.7f + 0.3f * Mathf.Sin(now * 50f + k)));
                Saucer(UfoX, m_Y, tilt, m_Squash, false, now);
            }

            // the pops: +1, CLOSE!, SPEED UP!
            foreach (var p in m_Pops)
            {
                float a = 1f - Mathf.Clamp01((p.Life - 0.55f) / 0.35f);
                if (a <= 0.05f) continue;
                var c = Mul(p.C, 0.4f + 0.6f * a);
                Text(p.Text, Mathf.RoundToInt(p.Pos.x) + 1, Mathf.RoundToInt(p.Pos.y) + 1, k_Black, p.Scale);
                Text(p.Text, Mathf.RoundToInt(p.Pos.x), Mathf.RoundToInt(p.Pos.y), c, p.Scale);
            }

            // the score, big, at the top (and the best in the corner)
            if (m_St == St.Play || m_St == St.Dead || (m_St == St.Attract && m_Score > 0))
            {
                string sc = m_Score.ToString();
                int x = W / 2 - TextW(sc, 3) / 2;
                Text(sc, x + 1, 5, k_Black, 3);
                Text(sc, x, 4, m_St == St.Dead ? C(1f, 0.6f, 0.5f) : k_White, 3);
            }
            if (Focused) Text("BEST " + ArcadeBoard.LocalBest, W - TextW("BEST " + ArcadeBoard.LocalBest, 1) - 3, 3, C(0.75f, 0.75f, 0.9f), 1);

            switch (m_St)
            {
                case St.Ready:
                    Centered("GET READY", 30, k_Gold, 2, true);
                    if (Mathf.Repeat(now, 1f) < 0.7f) Centered("SPACE OR CLICK TO FLY UP", 72, k_White, 1, true);
                    Centered("DODGE THE PLANETS SHIPS AND METEORS", 84, C(0.7f, 0.75f, 0.95f), 1, true);
                    break;
                case St.Dead:
                    if (m_StT > 0.35f) DrawGameOver(now);
                    break;
                case St.Attract:
                    // the title, and now and then the high scores
                    if (Mathf.Repeat(now, 14f) < 8f || ArcadeBoard.Entries.Count == 0)
                    {
                        Title(now);
                        if (Mathf.Repeat(now, 1.2f) < 0.85f) Centered("CLICK TO PLAY", 78, k_Gold, 1, true);
                    }
                    else DrawBoard(18, now, false);
                    break;
            }

            // a flash (a crash is white, SPEED UP pink)
            if (m_Flash > 0f)
                for (int i = 0; i < m_Px.Length; i++) m_Px[i] = Color32.Lerp(m_Px[i], m_FlashC, m_Flash * 0.8f);

            // scanlines, the dark corners, and the glow on the room
            long sr = 0, sg = 0, sb = 0;
            int hash = 17;
            for (int y = 0; y < H; y++)
            {
                bool dark = (y & 1) == 1;
                for (int x = 0; x < W; x++)
                {
                    int i = (H - 1 - y) * W + x;
                    var c = m_Px[i];
                    float k = m_Vignette[y * W + x] * (dark ? 0.82f : 1f);
                    c = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);
                    m_Px[i] = c;
                    if ((x & 7) == 0 && (y & 7) == 0) { sr += c.r; sg += c.g; sb += c.b; }
                    hash = hash * 31 + c.r + c.g * 7 + c.b * 13;
                }
            }
            int samples = (W / 8) * (H / 8);
            Glow = new Color(sr / (255f * samples), sg / (255f * samples), sb / (255f * samples));
            PictureHash = hash;
            m_Tex.SetPixels32(m_Px);
            m_Tex.Apply(false);
        }

        void Title(float now)
        {
            // UFO DODGE, big, bouncing letters in the arcade colours
            const string t = "UFO DODGE";
            int scale = 3, x = W / 2 - TextW(t, scale) / 2;
            for (int i = 0; i < t.Length; i++)
            {
                int bob = Mathf.RoundToInt(Mathf.Sin(now * 4f + i * 0.7f) * 2f);
                var c = Color32.Lerp(C(1f, 0.35f, 0.85f), C(0.35f, 0.95f, 1f), 0.5f + 0.5f * Mathf.Sin(now * 2f + i * 0.5f));
                Text(t[i].ToString(), x + 1, 26 + bob + 1, k_Black, scale);
                Text(t[i].ToString(), x, 26 + bob, c, scale);
                x += 4 * scale;
            }
        }

        void DrawGameOver(float now)
        {
            Shade(14, 26, W - 28, H - 40, 0.62f);
            Centered("GAME OVER", 30, C(1f, 0.35f, 0.3f), 2, true);
            Centered("SCORE " + m_Score + "   BEST " + ArcadeBoard.LocalBest, 44, k_White, 1, true);
            if (m_NewBest && Mathf.Repeat(now * 3f, 1f) < 0.65f) Centered("NEW BEST!", 52, k_Gold, 1, true);
            DrawBoard(60, now, true);
            if (m_StT > 0.9f && Mathf.Repeat(now, 1f) < 0.7f) Centered("SPACE TO PLAY AGAIN", H - 21, k_Gold, 1, true);
        }

        /// <summary>The leaderboard: your Steam friends' best scores (you highlighted).</summary>
        void DrawBoard(int y, float now, bool compact)
        {
            var list = ArcadeBoard.Entries;
            if (!compact) { Shade(16, y - 4, W - 32, 82, 0.6f); Centered("HIGH SCORES", y, k_Gold, 2, true); y += 14; }
            Centered(ArcadeBoard.Heading, y, C(0.6f, 0.85f, 1f), 1, true);
            y += 8;
            int rows = compact ? 4 : 7;
            if (list.Count == 0) { Centered(ArcadeBoard.Loading ? "LOADING..." : "NO SCORES YET", y + 4, C(0.7f, 0.7f, 0.8f), 1, true); return; }
            // (you're always on it: the rows round you if you're further down)
            int me = list.FindIndex(x => x.Me);
            int first = me >= rows ? me - rows + 1 : 0;
            for (int i = first; i < list.Count && i < first + rows; i++)
            {
                var e = list[i];
                var c = e.Me ? (Mathf.Repeat(now * 2f, 1f) < 0.75f ? k_Gold : C(1f, 0.95f, 0.6f)) : k_White;
                string rank = (i + 1).ToString();
                Text(rank, 28 - TextW(rank, 1), y, C(0.6f, 0.6f, 0.75f), 1);
                Text(e.Name, 34, y, c, 1);
                string sc = e.Score.ToString();
                Text(sc, W - 28 - TextW(sc, 1), y, c, 1);
                y += 7;
            }
        }

        // ------------------------------------------------------------------ text (LobbyArcade's 3x5 font, scaled)

        static int TextW(string s, int scale) => s.Length * 4 * scale - scale;

        void Centered(string s, int y, Color32 c, int scale, bool shadow)
        {
            int x = W / 2 - TextW(s, scale) / 2;
            if (shadow) Text(s, x + 1, y + 1, k_Black, scale);
            Text(s, x, y, c, scale);
        }

        void Text(string s, int x, int y, Color32 c, int scale)
        {
            foreach (char ch in s)
            {
                var g = ch == '.' ? k_Dot : LobbyArcade.Glyph(ch);
                for (int r = 0; r < 5; r++)
                    for (int col = 0; col < 3; col++)
                        if ((g[r] & (4 >> col)) != 0)
                            for (int j = 0; j < scale; j++) for (int i = 0; i < scale; i++) Px(x + col * scale + i, y + r * scale + j, c);
                x += 4 * scale;
            }
        }
        static readonly int[] k_Dot = { 0, 0, 0, 0, 2 };
    }

    /// <summary>
    /// UFO DODGE's leaderboard: a Steam leaderboard ("UFO Dodge", best score kept), read back for your Steam friends
    /// (and you). Without Steam it's just your own best on this PC (PlayerPrefs).
    /// </summary>
    public static class ArcadeBoard
    {
        public struct Entry { public string Name; public int Score; public bool Me; }
        public static readonly List<Entry> Entries = new List<Entry>();
        public static bool Loading { get; private set; }
        /// <summary>What the list is: your friends on Steam, or just you.</summary>
        public static string Heading => SteamBoot.Ready ? "STEAM FRIENDS" : "THIS PC - STEAM IS OFF";

        const string BoardName = "UFO Dodge", BestKey = "RockGame.UfoDodge.Best";
        public static int LocalBest => PlayerPrefs.GetInt(BestKey, 0);

        static SteamLeaderboard_t s_Board;
        static bool s_Have, s_Finding;
        static int s_ToUpload = -1;
        static float s_NextRefresh;
        static CallResult<LeaderboardFindResult_t> s_Find;
        static CallResult<LeaderboardScoreUploaded_t> s_Up;
        static CallResult<LeaderboardScoresDownloaded_t> s_Down;

        /// <summary>A game's over: keep the best here, and send it to Steam (it keeps your best there).</summary>
        public static void Submit(int score)
        {
            if (score > LocalBest) { PlayerPrefs.SetInt(BestKey, score); PlayerPrefs.Save(); }
            if (!SteamBoot.Ready) { LocalOnly(); return; }
            if (score > 0) s_ToUpload = Mathf.Max(s_ToUpload, score);
            if (s_Have) Upload();
            else Find();
            MergeMe();
        }

        /// <summary>Read the friends' scores again (on opening the machine, after a game, now and then).</summary>
        public static void Refresh()
        {
            s_NextRefresh = Time.time + 45f;
            if (!SteamBoot.Ready) { LocalOnly(); return; }
            if (!s_Have) { Find(); return; }
            Download();
        }

        /// <summary>While the machine's open: now and then, fresh scores.</summary>
        public static void Tick()
        {
            if (Time.time >= s_NextRefresh) Refresh();
        }

        static void LocalOnly()
        {
            Entries.Clear();
            if (LocalBest > 0) Entries.Add(new Entry { Name = Clean(GameSettings.PlayerName.Length > 0 ? GameSettings.PlayerName : "YOU"), Score = LocalBest, Me = true });
            Loading = false;
        }

        static void Find()
        {
            if (s_Finding || !SteamBoot.Ready) return;
            try
            {
                if (s_Find == null)
                {
                    s_Find = CallResult<LeaderboardFindResult_t>.Create(OnFind);
                    s_Up = CallResult<LeaderboardScoreUploaded_t>.Create(OnUploaded);
                    s_Down = CallResult<LeaderboardScoresDownloaded_t>.Create(OnDownloaded);
                }
                s_Finding = true;
                Loading = true;
                s_Find.Set(SteamUserStats.FindOrCreateLeaderboard(BoardName, ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending, ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric));
            }
            catch (System.Exception e) { s_Finding = false; Loading = false; Debug.LogWarning("[RockGame] arcade leaderboard: " + e.Message); LocalOnly(); }
        }

        static void OnFind(LeaderboardFindResult_t r, bool fail)
        {
            s_Finding = false;
            if (fail || r.m_bLeaderboardFound == 0) { LocalOnly(); return; }
            s_Board = r.m_hSteamLeaderboard;
            s_Have = true;
            if (s_ToUpload > 0) Upload();
            else Download();
        }

        static void Upload()
        {
            if (!s_Have || s_ToUpload <= 0) return;
            int score = s_ToUpload;
            s_ToUpload = -1;
            s_Up.Set(SteamUserStats.UploadLeaderboardScore(s_Board, ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest, score, null, 0));
        }

        static void OnUploaded(LeaderboardScoreUploaded_t r, bool fail) => Download();

        static void Download()
        {
            if (!s_Have) return;
            Loading = true;
            s_Down.Set(SteamUserStats.DownloadLeaderboardEntries(s_Board, ELeaderboardDataRequest.k_ELeaderboardDataRequestFriends, 0, 0));
        }

        static void OnDownloaded(LeaderboardScoresDownloaded_t r, bool fail)
        {
            Loading = false;
            if (fail) return;
            Entries.Clear();
            var me = SteamUser.GetSteamID();
            for (int i = 0; i < r.m_cEntryCount; i++)
            {
                if (!SteamUserStats.GetDownloadedLeaderboardEntry(r.m_hSteamLeaderboardEntries, i, out var e, null, 0)) continue;
                bool mine = e.m_steamIDUser == me;
                string name = mine ? SteamFriends.GetPersonaName() : SteamFriends.GetFriendPersonaName(e.m_steamIDUser);
                Entries.Add(new Entry { Name = Clean(name), Score = e.m_nScore, Me = mine });
            }
            MergeMe();
        }

        /// <summary>Your own best is always on the list (Steam may not have it yet), in its place.</summary>
        static void MergeMe()
        {
            int best = LocalBest;
            int at = Entries.FindIndex(x => x.Me);
            if (at >= 0) { if (Entries[at].Score < best) Entries[at] = new Entry { Name = Entries[at].Name, Score = best, Me = true }; }
            else if (best > 0) Entries.Add(new Entry { Name = Clean(SteamBoot.Ready ? SteamBoot.MyName : (GameSettings.PlayerName.Length > 0 ? GameSettings.PlayerName : "YOU")), Score = best, Me = true });
            Entries.Sort((a, b) => b.Score.CompareTo(a.Score));
        }

        /// <summary>A name in the screen's pixel font: capitals, digits and a few marks, ten letters at most.</summary>
        static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (s ?? "").ToUpperInvariant())
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '!' || ch == '+') sb.Append(ch);
                else if (ch == ' ' && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                if (sb.Length >= 10) break;
            }
            var n = sb.ToString().Trim();
            return n.Length == 0 ? "PLAYER" : n;
        }
    }
}
