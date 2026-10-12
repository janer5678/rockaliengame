using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The arcade machine against the back wall on the right (where the Trade Station stood): an upright cabinet in
    /// neon purple - a lit UFO DODGE marquee, a tilted screen with the game on it (UfoArcade.cs), a control panel with
    /// a joystick and three glowing buttons, a coin door with lit slots, neon strips down its edges. Its screen lights
    /// the room the way the telly's does: a spot light out into the room and a light just in front, both taking the
    /// picture's colour and flickering, and a soft glow round the screen. Click it: the camera moves in on the screen
    /// and you play (Esc or STOP PLAYING to go back to the couch).
    /// </summary>
    public partial class ShipLobby
    {
        /// <summary>Where it stands in the room, and what a click on it hits (its own space: front +z).</summary>
        static readonly Vector3 k_CabAt = new Vector3(4.8f, 0f, 3.62f);
        static readonly Bounds k_CabBounds = new Bounds(new Vector3(0f, 0.95f, 0.05f), new Vector3(0.9f, 1.95f, 0.9f));
        /// <summary>The screen: its middle (cabinet space), how far it leans back, its size (4:3).</summary>
        static readonly Vector3 k_CabScreen = new Vector3(0f, 1.4f, 0.27f);
        const float CabLean = 12f, CabScrW = 0.52f, CabScrH = 0.39f;

        Transform m_Cab, m_CabScreen;
        Light m_CabSpot, m_CabNear, m_CabMarqueeLight;
        Material m_CabHalo, m_CabMarquee;
        Texture2D m_CabHaloTex, m_CabMarqueeTex;

        void BuildCabinet(Transform room)
        {
            var t = new GameObject("arcade machine").transform;
            t.SetParent(room, false);
            t.localPosition = k_CabAt;
            t.localRotation = Quaternion.Euler(0f, 180f, 0f); // (facing the room)
            m_Cab = t;
            var body = new Color(0.08f, 0.05f, 0.13f);
            var side = new Color(0.2f, 0.08f, 0.32f);
            var metal = new Color(0.22f, 0.22f, 0.26f);
            var pink = new Color(1f, 0.3f, 0.85f);
            var cyan = new Color(0.3f, 0.95f, 1f);
            var gPink = Workbench.Glow(pink, 2.4f);
            var gCyan = Workbench.Glow(cyan, 2.2f);

            // the body: a plinth, the cabinet, purple side panels with neon down their front edges
            Art.Box(t, metal * 0.7f, new Vector3(0f, 0.04f, 0f), new Vector3(0.8f, 0.08f, 0.74f));
            Art.Box(t, body, new Vector3(0f, 0.52f, 0f), new Vector3(0.72f, 0.96f, 0.6f));      // (the lower body, out to the coin door)
            Art.Box(t, body, new Vector3(0f, 1.44f, -0.1f), new Vector3(0.72f, 0.88f, 0.5f));    // (the upper body, set back behind the screen)
            foreach (float s in new[] { -1f, 1f })
            {
                Art.Box(t, side, new Vector3(s * 0.375f, 0.98f, 0f), new Vector3(0.03f, 1.88f, 0.74f));
                Art.Box(t, pink, new Vector3(s * 0.392f, 0.98f, 0.36f), new Vector3(0.012f, 1.8f, 0.014f), default, false, gPink);
                Art.Box(t, cyan, new Vector3(s * 0.392f, 1.9f, 0f), new Vector3(0.012f, 0.014f, 0.7f), default, false, gCyan);
                // side art: a big saucer shape and a slash of colour on each side
                Art.Part(t, Art.Sphere, new Color(0.55f, 0.58f, 0.66f), new Vector3(s * 0.393f, 1.25f, -0.05f), new Vector3(0.01f, 0.08f, 0.42f) / SR);
                Art.Part(t, Art.Sphere, new Color(0.25f, 0.65f, 0.75f), new Vector3(s * 0.394f, 1.3f, -0.05f), new Vector3(0.01f, 0.08f, 0.14f) / SR);
                Art.Box(t, new Color(0.9f, 0.3f, 0.7f), new Vector3(s * 0.393f, 0.6f, 0f), new Vector3(0.008f, 0.06f, 0.6f), new Vector3(30f, 0f, 0f));
                Art.Box(t, new Color(0.3f, 0.75f, 0.95f), new Vector3(s * 0.393f, 0.5f, 0f), new Vector3(0.008f, 0.04f, 0.6f), new Vector3(30f, 0f, 0f));
            }
            // the top: a little overhang and the lit marquee under it
            Art.Box(t, side, new Vector3(0f, 1.93f, 0.02f), new Vector3(0.78f, 0.05f, 0.7f));
            Art.Box(t, body, new Vector3(0f, 1.79f, 0.25f), new Vector3(0.72f, 0.22f, 0.14f));
            m_CabMarquee = MarqueeMat();
            var mq = new GameObject("marquee");
            mq.transform.SetParent(t, false);
            mq.transform.localPosition = new Vector3(0f, 1.79f, 0.322f);
            mq.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            mq.transform.localScale = new Vector3(0.68f, 0.18f, 1f);
            mq.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mqr = mq.AddComponent<MeshRenderer>();
            mqr.sharedMaterial = m_CabMarquee;
            mqr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_CabMarqueeLight = Light(t, new Vector3(0f, 1.78f, 0.6f), pink, 0.9f, 1.8f);
            m_CabMarqueeLight.name = "arcade marquee light";
            // a speaker grille under it
            Art.Box(t, metal * 0.5f, new Vector3(0f, 1.65f, 0.3f), new Vector3(0.5f, 0.06f, 0.01f));
            for (int i = 0; i < 6; i++) Art.Box(t, metal * 0.25f, new Vector3(-0.2f + i * 0.08f, 1.65f, 0.306f), new Vector3(0.03f, 0.04f, 0.004f));

            // the screen: leaning back in a black bezel with a thin cyan trim, the game on it
            var sr = new GameObject("arcade screen").transform;
            sr.SetParent(t, false);
            sr.localPosition = k_CabScreen;
            sr.localRotation = Quaternion.Euler(-CabLean, 0f, 0f);
            Art.Box(sr, new Color(0.03f, 0.03f, 0.04f), new Vector3(0f, 0f, -0.03f), new Vector3(0.64f, 0.5f, 0.05f));
            foreach (float s in new[] { -1f, 1f })
            {
                Art.Box(sr, cyan, new Vector3(0f, s * (CabScrH * 0.5f + 0.025f), 0.002f), new Vector3(CabScrW + 0.06f, 0.008f, 0.006f), default, false, gCyan);
                Art.Box(sr, cyan, new Vector3(s * (CabScrW * 0.5f + 0.025f), 0f, 0.002f), new Vector3(0.008f, CabScrH + 0.06f, 0.006f), default, false, gCyan);
            }
            var scr = new GameObject("arcade picture");
            scr.transform.SetParent(sr, false);
            scr.transform.localPosition = new Vector3(0f, 0f, 0.001f);
            scr.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // (a quad faces -z: turned round to face out)
            scr.transform.localScale = new Vector3(CabScrW, CabScrH, 1f);
            scr.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var pr = scr.AddComponent<MeshRenderer>();
            pr.sharedMaterial = UfoArcade.ScreenMat != null ? UfoArcade.ScreenMat : Unlit(new Color(0.3f, 0.2f, 0.5f), 1.4f);
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_CabScreen = sr;
            // the cabinet round the screen comes forward to the control panel
            Art.Box(t, body, new Vector3(0f, 1.1f, 0.26f), new Vector3(0.72f, 0.12f, 0.14f));

            // the control panel: sloping down towards you, a joystick and three glowing buttons
            var panel = new GameObject("control panel").transform;
            panel.SetParent(t, false);
            panel.localPosition = new Vector3(0f, 1.04f, 0.42f);
            panel.localRotation = Quaternion.Euler(14f, 0f, 0f);
            Art.Box(panel, side, Vector3.zero, new Vector3(0.74f, 0.05f, 0.3f));
            Art.Box(panel, metal * 0.4f, new Vector3(0f, 0.026f, 0f), new Vector3(0.66f, 0.004f, 0.24f));
            Art.Box(panel, pink, new Vector3(0f, 0f, 0.152f), new Vector3(0.74f, 0.012f, 0.008f), default, false, gPink);
            Art.Part(panel, Art.Cylinder, metal, new Vector3(-0.17f, 0.075f, 0f), new Vector3(0.025f / CR, 0.1f / CH, 0.025f / CR));
            Art.Part(panel, Art.Sphere, new Color(0.9f, 0.15f, 0.15f), new Vector3(-0.17f, 0.13f, 0f), Vector3.one * (0.06f / SR));
            Art.Part(panel, Art.Cylinder, new Color(0.05f, 0.05f, 0.05f), new Vector3(-0.17f, 0.03f, 0f), new Vector3(0.08f / CR, 0.008f / CH, 0.08f / CR));
            var buttons = new[] { new Color(1f, 0.25f, 0.3f), new Color(1f, 0.85f, 0.2f), cyan };
            for (int i = 0; i < 3; i++)
                Art.Part(panel, Art.Cylinder, buttons[i], new Vector3(0.03f + i * 0.09f, 0.035f, i == 1 ? -0.035f : 0.01f), new Vector3(0.055f / CR, 0.02f / CH, 0.055f / CR), default, false, Workbench.Glow(buttons[i], 1.8f));
            Art.Box(t, body, new Vector3(0f, 0.92f, 0.5f), new Vector3(0.72f, 0.16f, 0.06f)); // (its apron)

            // the coin door, its two slots lit orange
            Art.Box(t, metal * 0.6f, new Vector3(0f, 0.45f, 0.293f), new Vector3(0.44f, 0.5f, 0.02f));
            foreach (float s in new[] { -1f, 1f })
            {
                Art.Box(t, Color.white, new Vector3(s * 0.09f, 0.56f, 0.305f), new Vector3(0.06f, 0.09f, 0.008f), default, false, Workbench.Glow(new Color(1f, 0.55f, 0.15f), 2.2f));
                Art.Box(t, new Color(0.02f, 0.02f, 0.02f), new Vector3(s * 0.09f, 0.56f, 0.31f), new Vector3(0.008f, 0.05f, 0.004f));
            }
            Art.Box(t, metal * 0.3f, new Vector3(0f, 0.32f, 0.305f), new Vector3(0.2f, 0.04f, 0.008f)); // (the return slot)

            // the screen's light on the room (like the telly's: UfoArcade.Glow's colour, a CRT flicker)
            var spot = new GameObject("arcade light");
            spot.transform.SetParent(sr, false);
            spot.transform.localPosition = new Vector3(0f, 0f, 0.2f);
            spot.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.25f, 1f));
            m_CabSpot = spot.AddComponent<Light>();
            m_CabSpot.type = LightType.Spot;
            m_CabSpot.spotAngle = 115f;
            m_CabSpot.innerSpotAngle = 55f;
            m_CabSpot.range = 9f;
            m_CabSpot.intensity = 5f;
            m_CabSpot.shadows = LightShadows.None;
            m_CabNear = Light(sr, new Vector3(0f, 0f, 0.45f), new Color(0.6f, 0.5f, 1f), 1.8f, 2.6f);
            m_CabNear.name = "arcade near light";
            m_CabHalo = Halo(sr, Vector3.zero, CabScrW * 0.5f, CabScrH * 0.5f, out m_CabHaloTex);
        }

        /// <summary>A soft additive glow hung round a screen (none over the picture), like the telly's.</summary>
        static Material Halo(Transform parent, Vector3 at, float hw, float hh, out Texture2D tex)
        {
            tex = null;
            var sh = Resources.Load<Shader>("SpaceArena/SpaceGlow");
            if (sh == null || !sh.isSupported) return null;
            const int TW = 96, TH = 80;
            float qw = hw * 2f + 0.65f, qh = hh * 2f + 0.6f;
            tex = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "arcade halo" };
            var px = new Color32[TW * TH];
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    float lx = ((x + 0.5f) / TW - 0.5f) * qw, ly = ((y + 0.5f) / TH - 0.5f) * qh;
                    float dx = Mathf.Max(0f, Mathf.Abs(lx) - hw), dy = Mathf.Max(0f, Mathf.Abs(ly) - hh);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= 0f ? 0f : Mathf.Exp(-d / 0.075f) * Mathf.SmoothStep(0f, 1f, d / 0.012f);
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    px[y * TW + x] = new Color32(v, v, v, v);
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            var m = new Material(sh) { name = "arcade halo", renderQueue = 3000 };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            var halo = new GameObject("arcade halo");
            halo.transform.SetParent(parent, false);
            halo.transform.localPosition = at + new Vector3(0f, 0f, 0.012f);
            halo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            halo.transform.localScale = new Vector3(qw, qh, 1f);
            halo.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mr = halo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return m;
        }

        /// <summary>The marquee's picture: UFO DODGE in the pixel font over a purple glow, a saucer either side.</summary>
        Material MarqueeMat()
        {
            const int TW = 96, TH = 24;
            m_CabMarqueeTex = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "arcade marquee" };
            var px = new Color32[TW * TH];
            void P(int x, int y, Color32 c) { if (x >= 0 && x < TW && y >= 0 && y < TH) px[(TH - 1 - y) * TW + x] = c; }
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    float v = y / (float)TH;
                    var c = Color.Lerp(new Color(0.35f, 0.05f, 0.45f), new Color(0.08f, 0.02f, 0.2f), Mathf.Abs(v - 0.5f) * 2f);
                    if (y == 1 || y == TH - 2) c = new Color(0.3f, 0.95f, 1f);
                    px[(TH - 1 - y) * TW + x] = c;
                }
            // the title, two pixels a font pixel, pink with a cyan shadow
            const string title = "UFO DODGE";
            int tx = TW / 2 - (title.Length * 8 - 2) / 2, ty = 7;
            for (int pass = 0; pass < 2; pass++)
            {
                int x = tx + (pass == 0 ? 1 : 0), y = ty + (pass == 0 ? 1 : 0);
                var col = pass == 0 ? new Color32(60, 230, 255, 255) : new Color32(255, 110, 230, 255);
                foreach (char ch in title)
                {
                    var g = LobbyArcade.Glyph(ch);
                    for (int r = 0; r < 5; r++)
                        for (int cc = 0; cc < 3; cc++)
                            if ((g[r] & (4 >> cc)) != 0)
                                for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) P(x + cc * 2 + i, y + r * 2 + j, col);
                    x += 8;
                }
            }
            // a little saucer at each end
            foreach (int cx in new[] { 6, TW - 7 })
            {
                for (int i = -4; i <= 4; i++) P(cx + i, 13, new Color32(200, 205, 220, 255));
                for (int i = -3; i <= 3; i++) P(cx + i, 14, new Color32(120, 125, 140, 255));
                for (int i = -1; i <= 1; i++) { P(cx + i, 11, new Color32(90, 220, 255, 255)); P(cx + i, 12, new Color32(60, 160, 200, 255)); }
                P(cx, 15, new Color32(120, 200, 255, 255));
            }
            m_CabMarqueeTex.SetPixels32(px);
            m_CabMarqueeTex.Apply(false);
            var screen = Resources.Load<Shader>("World/Screen");
            Material m;
            if (screen != null && screen.isSupported)
            {
                m = new Material(screen) { name = "arcade marquee" };
                m.SetTexture("_MainTex", m_CabMarqueeTex);
                m.SetFloat("_Intensity", 1.7f);
            }
            else
            {
                m = Art.NewMat(Color.white);
                m.SetTexture("_BaseMap", m_CabMarqueeTex);
                m.mainTexture = m_CabMarqueeTex;
            }
            return m;
        }

        /// <summary>Every frame: the screen's lights take the picture's colour and flicker a little (Settings > Display >
        /// SHIP LOBBY > Telly turns them up or down too); the marquee hums and pulses.</summary>
        void TickCabinet(float t)
        {
            if (m_Cab == null) return;
            float tvk = GameSettings.LobbyTvNow;
            float fl = 0.85f + 0.15f * Mathf.PerlinNoise(t * 21f, 5f);
            var glow = UfoArcade.Glow;
            float lum = Mathf.Max(0.05f, glow.maxColorComponent);
            var tint = Color.Lerp(glow / lum, new Color(0.75f, 0.7f, 1f), 0.3f);
            if (m_CabSpot) { m_CabSpot.color = tint; m_CabSpot.intensity = 5.5f * fl * tvk; }
            if (m_CabNear) { m_CabNear.color = tint; m_CabNear.intensity = 1.8f * fl * tvk; }
            if (m_CabHalo) m_CabHalo.SetColor("_Color", tint * (0.5f * fl * Mathf.Min(tvk, 1.6f)));
            if (m_CabMarqueeLight) m_CabMarqueeLight.intensity = (0.75f + 0.25f * Mathf.Sin(t * 3f)) * Mathf.Max(0.3f, tvk);
            UfoArcade.SetScreenBrightness(Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(tvk)) * (tvk > 1f ? 1f + (tvk - 1f) * 0.25f : 1f));
        }

        void ClearCabinet()
        {
            if (m_CabHalo) Destroy(m_CabHalo);
            if (m_CabHaloTex) Destroy(m_CabHaloTex);
            if (m_CabMarquee) Destroy(m_CabMarquee);
            if (m_CabMarqueeTex) Destroy(m_CabMarqueeTex);
            m_CabHalo = m_CabMarquee = null;
            m_CabHaloTex = m_CabMarqueeTex = null;
            m_Cab = m_CabScreen = null;
            m_CabSpot = m_CabNear = m_CabMarqueeLight = null;
        }

        /// <summary>A click (or the tests' TestClickCabinet) on the arcade machine: the camera goes to it and you play.</summary>
        bool CabinetHit(Ray ray)
        {
            if (m_Cab == null) return false;
            return k_CabBounds.IntersectRay(new Ray(m_Cab.InverseTransformPoint(ray.origin), m_Cab.InverseTransformDirection(ray.direction)));
        }

        /// <summary>The arcade machine is under this point of the screen (GUI space: y down from the top).</summary>
        public static bool CabinetUnder(Vector2 gui)
        {
            var cam = Camera.main;
            if (s_I == null || cam == null) return false;
            return s_I.CabinetHit(cam.ScreenPointToRay(new Vector3(gui.x, Screen.height - gui.y, 0f)));
        }

        /// <summary>(tests) click the arcade machine this frame (as if the mouse were on it).</summary>
        public static bool TestClickCabinet;

        /// <summary>(tests) how far the camera is from the arcade machine's screen (m).</summary>
        public static float CamToCabinet => s_I != null && s_I.m_CabScreen != null && Camera.main != null ? Vector3.Distance(Camera.main.transform.position, s_I.m_CabScreen.position) : 99f;

        /// <summary>The camera playing the arcade machine: square on to its screen, close enough to fill most of the view.</summary>
        bool CabinetShot(out Vector3 pos, out Vector3 look)
        {
            pos = look = default;
            if (m_CabScreen == null) return false;
            var scr = m_CabScreen.position;
            pos = scr + m_CabScreen.forward * 0.78f;
            look = scr;
            return true;
        }
    }
}
