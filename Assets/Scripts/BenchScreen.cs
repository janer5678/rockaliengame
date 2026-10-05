using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The pictures on the trade stations' monitors (Workbench.Monitor): a tiny texture (32 x 24, point filtered, also
    /// the emission map so it reads at night) redrawn a few times a second while the screen is on camera.
    /// - Face: an alien on a call - it bobs, blinks and talks, and every so often the call drops in a burst of static
    ///   and a different coloured alien (other eyes, antennae or not) picks up.
    /// - Stocks: the intergalactic markets - a scrolling line graph over a grid (green while it's up, red while it's
    ///   down), volume bars and a ticker running along the bottom.
    /// Purely local decoration: nothing is synced, every screen runs on its own.
    /// </summary>
    public class BenchScreen : MonoBehaviour
    {
        public enum Kind { Face, Stocks }

        const int W = 32, H = 24;
        /// <summary>Seconds between redraws.</summary>
        const float Tick = 0.12f;

        static readonly Color32[] k_Aliens =
        {
            new Color32(115, 242, 140, 255), new Color32(255, 96, 200, 255), new Color32(90, 235, 255, 255), new Color32(255, 176, 60, 255),
            new Color32(178, 120, 255, 255), new Color32(255, 232, 90, 255), new Color32(255, 96, 84, 255),
        };

        Kind m_Kind;
        Texture2D m_Tex;
        Material m_Mat;
        MeshRenderer m_Renderer;
        readonly Color32[] m_Px = new Color32[W * H];
        float m_Next, m_NextCall, m_Static;
        int m_Alien, m_Frame;
        readonly float[] m_Graph = new float[W];
        readonly float[] m_Volume = new float[W];
        float m_Price = 0.5f, m_Trend;

        /// <summary>Test hook: how many callers this screen has shown so far.</summary>
        public int Calls { get; private set; }

        /// <summary>Puts a live picture on a monitor's screen (a thin box facing +z).</summary>
        public static BenchScreen Attach(GameObject screen, Kind kind)
        {
            var s = screen.AddComponent<BenchScreen>();
            s.m_Kind = kind;
            s.m_Renderer = screen.GetComponent<MeshRenderer>();
            s.m_Tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "benchScreen" };
            var m = Art.NewMat(Color.white);
            m.SetTexture("_BaseMap", s.m_Tex);
            m.mainTexture = s.m_Tex;
            m.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.35f)); // (mostly its own light: it shouldn't wash out in the sun)
            if (m.HasProperty("_EmissionMap") && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", s.m_Tex);
                m.SetColor("_EmissionColor", Color.white * 1.25f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            s.m_Mat = m;
            if (s.m_Renderer) s.m_Renderer.sharedMaterial = m;
            s.m_Alien = Random.Range(0, k_Aliens.Length);
            s.m_NextCall = Time.time + Random.Range(5f, 11f);
            s.m_Next = Time.time + Random.Range(0f, Tick); // (screens don't all redraw on the same frame)
            for (int i = 0; i < W; i++) s.StepMarket();
            s.Draw();
            return s;
        }

        void OnDestroy()
        {
            if (m_Tex) Destroy(m_Tex);
            if (m_Mat) Destroy(m_Mat);
        }

        void Update()
        {
            float now = Time.time;
            if (now < m_Next || m_Tex == null) return;
            m_Next = now + Tick;
            if (m_Kind == Kind.Face && now >= m_NextCall)
            {
                // the call drops: static, then someone else picks up
                m_NextCall = now + Random.Range(6f, 13f);
                m_Static = 0.55f;
                m_Alien = (m_Alien + Random.Range(1, k_Aliens.Length)) % k_Aliens.Length;
                Calls++;
            }
            if (m_Renderer != null && !m_Renderer.isVisible) return; // nobody's looking: nothing to draw
            m_Frame++;
            if (m_Kind == Kind.Stocks && (m_Frame & 1) == 0) StepMarket();
            Draw();
        }

        void Draw()
        {
            if (m_Kind == Kind.Face) DrawFace(); else DrawStocks();
            m_Tex.SetPixels32(m_Px);
            m_Tex.Apply(false);
        }

        void Set(int x, int y, Color32 c)
        {
            if (x >= 0 && x < W && y >= 0 && y < H) m_Px[y * W + x] = c;
        }

        static Color32 Mul(Color32 c, float k) => new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), 255);

        // ------------------------------------------------------------------ the alien on a call

        void DrawFace()
        {
            float now = Time.time;
            if (m_Static > 0f)
            {
                // between callers: grey static with a bright band rolling down it
                m_Static -= Tick;
                int band = (m_Frame * 5) % H;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        byte v = (byte)Random.Range(20, Mathf.Abs(y - band) < 2 ? 255 : 150);
                        m_Px[y * W + x] = new Color32(v, v, v, 255);
                    }
                return;
            }
            var skin = k_Aliens[m_Alien];
            var dark = Mul(skin, 0.55f);
            var bg = Mul(skin, 0.1f);
            for (int y = 0; y < H; y++)
            {
                var row = (y & 1) == 0 ? bg : Mul(skin, 0.14f); // scanlines
                for (int x = 0; x < W; x++) m_Px[y * W + x] = row;
            }
            int kind = m_Alien % 3;                              // (three kinds of alien: plain, antennae, three-eyed)
            int cx = W / 2 + Mathf.RoundToInt(Mathf.Sin(now * 1.3f + m_Alien) * 1.2f); // it sways as it talks
            int nod = Mathf.Sin(now * 2.1f) > 0.6f ? 1 : 0;
            const int headLo = 3, headHi = 20;
            // shoulders, then the head: wide at the top, narrowing to a pointed chin
            for (int y = 0; y < headLo + 1; y++)
                for (int x = cx - 9 + y; x <= cx + 9 - y; x++) Set(x, y, dark);
            for (int y = headLo; y <= headHi; y++)
            {
                float u = (y - headLo) / (float)(headHi - headLo);
                float half = Mathf.Lerp(2.2f, 8.2f, Mathf.Sqrt(u)) * (u > 0.85f ? Mathf.Lerp(1f, 0.72f, (u - 0.85f) / 0.15f) : 1f);
                for (int x = Mathf.RoundToInt(cx - half); x <= Mathf.RoundToInt(cx + half); x++)
                    Set(x, y + nod, x < cx - half + 1.5f ? dark : skin); // (shaded down one side)
            }
            if (kind == 1)
                for (int k = -1; k <= 1; k += 2)
                {
                    // antennae with a bobble on each
                    for (int i = 1; i <= 2; i++) Set(cx + k * (3 + i), headHi + i + nod, dark);
                    Set(cx + k * 6, headHi + 3 + nod, Color.white);
                }
            // the eyes: big, black and slanted, with a glint; they shut for a moment every few seconds
            bool blink = Mathf.Repeat(now + m_Alien * 0.7f, 3.4f) < 0.14f;
            var black = new Color32(8, 8, 14, 255);
            int ey = 13 + nod;
            for (int k = -1; k <= 1; k += 2)
            {
                int ex = cx + k * 4;
                if (blink) { for (int x = -2; x <= 2; x++) Set(ex + x, ey, black); continue; }
                for (int y = -1; y <= 1; y++)
                    for (int x = -2; x <= 2; x++)
                    {
                        // (slanted: the outer end sits a pixel higher)
                        int sy = y + (x * k >= 2 ? 1 : 0);
                        if (Mathf.Abs(x) == 2 && y != 0) continue;
                        Set(ex + x, ey + sy, black);
                    }
                Set(ex - 1, ey + 1, Color.white);
            }
            if (kind == 2 && !blink)
            {
                // a third eye in the forehead
                for (int x = -1; x <= 1; x++) Set(cx + x, ey + 4, black);
                Set(cx, ey + 5, black);
                Set(cx, ey + 4, Color.white);
            }
            // the mouth opens and shuts as it talks
            float talk = Mathf.PerlinNoise(now * 5.5f, m_Alien * 3.1f);
            int open = talk > 0.62f ? 2 : talk > 0.42f ? 1 : 0;
            for (int x = -2; x <= 2; x++) Set(cx + x, 7 + nod, black);
            for (int o = 1; o <= open; o++)
                for (int x = -2 + o; x <= 2 - o; x++) Set(cx + x, 7 + nod - o, black);
            // the call's furniture: a blinking red dot top left, a sound meter down the right
            if (Mathf.Repeat(now, 1.2f) < 0.7f)
            {
                var red = new Color32(255, 60, 50, 255);
                Set(1, H - 2, red); Set(2, H - 2, red); Set(1, H - 3, red); Set(2, H - 3, red);
            }
            for (int b = 0; b < 3; b++)
            {
                int hgt = 1 + Mathf.RoundToInt(Mathf.PerlinNoise(now * 6f + b * 7.3f, 1.7f) * 5f * (open > 0 ? 1f : 0.35f));
                for (int y = 0; y < hgt; y++) Set(W - 2 - b * 2, 1 + y, Color.white);
            }
        }

        // ------------------------------------------------------------------ the markets

        /// <summary>The price takes a step (a random walk with a trend that turns now and then); the graph scrolls on by one.</summary>
        void StepMarket()
        {
            if (Random.value < 0.08f) m_Trend = Random.Range(-0.035f, 0.035f);
            m_Price += m_Trend + Random.Range(-0.07f, 0.07f);
            if (m_Price < 0.08f) { m_Price = 0.08f; m_Trend = Mathf.Abs(m_Trend); }
            if (m_Price > 0.95f) { m_Price = 0.95f; m_Trend = -Mathf.Abs(m_Trend); }
            for (int i = 0; i < W - 1; i++) { m_Graph[i] = m_Graph[i + 1]; m_Volume[i] = m_Volume[i + 1]; }
            m_Graph[W - 1] = m_Price;
            m_Volume[W - 1] = Random.value;
        }

        void DrawStocks()
        {
            const int lo = 7, hi = H - 3; // (the graph's rows; under it the volume bars and the ticker)
            var bg = new Color32(5, 9, 18, 255);
            var grid = new Color32(18, 34, 58, 255);
            int scroll = m_Frame / 2;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    m_Px[y * W + x] = y >= lo - 1 && (((x + scroll) & 7) == 0 || ((y - lo) % 5) == 0) ? grid : bg;
            bool up = m_Graph[W - 1] >= m_Graph[W - 9];
            var line = up ? new Color32(70, 255, 120, 255) : new Color32(255, 70, 70, 255);
            var fill = Mul(line, 0.22f);
            int prev = -1;
            for (int x = 0; x < W; x++)
            {
                int y = lo + Mathf.RoundToInt(m_Graph[x] * (hi - lo));
                for (int f = lo; f < y; f++) if (((x + f) & 1) == 0) Set(x, f, fill); // (a dithered fill under the line)
                // join it to the last point, so steep moves don't leave gaps
                int from = prev < 0 ? y : Mathf.Min(prev, y), to = prev < 0 ? y : Mathf.Max(prev, y);
                for (int j = from; j <= to; j++) Set(x, j, line);
                prev = y;
            }
            Set(W - 1, prev, Color.white); // (the live price)
            // volume bars under the graph
            for (int x = 0; x < W; x += 2)
            {
                int hgt = 1 + Mathf.RoundToInt(m_Volume[x] * 3f);
                var c = x > 0 && m_Graph[x] >= m_Graph[x - 1] ? new Color32(40, 150, 80, 255) : new Color32(160, 50, 50, 255);
                for (int y = 0; y < hgt; y++) Set(x, 2 + y, c);
            }
            // the ticker along the bottom: little blocks of green and red sliding left
            for (int x = 0; x < W; x++)
            {
                int k = (x + m_Frame) % 12;
                if (k < 7) Set(x, 0, ((x + m_Frame) / 12 % 3) == 0 ? new Color32(255, 70, 70, 255) : new Color32(70, 255, 120, 255));
            }
            // top left: which way it's going (an arrow), top right: a headline of dashes
            var a = up ? new Color32(70, 255, 120, 255) : new Color32(255, 70, 70, 255);
            int ay = H - 3;
            Set(2, ay + (up ? 1 : -1), a);
            for (int x = 1; x <= 3; x++) Set(x, ay, a);
            for (int x = 6; x < 14; x++) if (x != 9) Set(x, H - 2, new Color32(150, 170, 200, 255));
        }
    }
}
