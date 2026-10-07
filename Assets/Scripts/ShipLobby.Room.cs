using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// More of the ship lobby's room (ShipLobby.cs builds the rest):
    /// - the front wall, behind the camera: the room is closed all round, so no shot (the couch, looking left and right,
    ///   the telly's close-up) shows the sky - a hatch door with its EXIT light, panels, pipes, a coat hook, posters;
    /// - the telly's light: the picture throws light onto the floor and the couch in front of it and onto the set and
    ///   the speakers round it, and a soft glow hangs round the screen (all in the picture's colour, flickering with it);
    /// - the glowing ball on the floor: toned down, its glow and light set by Settings > Display > SHIP LOBBY > Glowing
    ///   ball (also LOBBY LOOK in the lobby), live;
    /// - what's under the mouse: the telly (Hud.Lobby.cs shows PLAY GAME? by the mouse), and (tests) how much sky shows.
    /// </summary>
    public partial class ShipLobby
    {
        /// <summary>The front wall's inner face (room space z): the camera side of the room.</summary>
        const float FrontZ = -6.35f;

        // ------------------------------------------------------------------ the front wall

        /// <summary>The wall behind the camera, across the whole room (floor to ceiling, out past the side walls).</summary>
        void BuildFrontWall(Transform t, Color trim, Color metal, Color pipe, Color wallDark)
        {
            var rng = new System.Random(23); // (its own: the rest of the room's mess stays where it was)
            float R() => (float)rng.NextDouble();
            WallBox(t, new Vector3(0f, (RoomH + 0.4f) * 0.5f - 0.1f, FrontZ - 0.15f), new Vector3(15.2f, RoomH + 0.6f, 0.3f));
            float z = FrontZ + 0.02f;
            // panel seams, scuffed kick panels and grime (the same as the side walls)
            for (int i = -2; i <= 2; i++)
            {
                Art.Box(t, trim * 0.8f, new Vector3(i * 2.4f, RoomH * 0.5f, z + 0.03f), new Vector3(0.2f, RoomH, 0.08f));
                Art.Box(t, wallDark, new Vector3(i * 2.4f + 1.2f, 0.45f, z + 0.01f), new Vector3(2f, 0.6f, 0.04f), default, false, Tone(Tones.Wall, 0.9f));
                Art.Box(t, Color.white, new Vector3(i * 2.4f + 0.6f + R(), 1.2f + R() * 1.4f, z + 0.005f), new Vector3(0.4f + R() * 0.6f, 0.3f + R() * 0.5f, 0.03f), default, false, Tone(Tones.Wall, 0.75f + R() * 0.15f));
                Art.Box(t, Color.white, new Vector3(i * 2.4f + R() * 2f - 1f, 0.9f + R() * 1.2f, z + 0.008f), new Vector3(0.08f + R() * 0.2f, 0.5f + R() * 0.8f, 0.02f), default, false, Tone(Tones.Stain, 0.9f + R() * 0.2f));
            }
            // the pipes carry on along it
            foreach (float py in new[] { 2.75f, 2.95f })
                Art.Part(t, Art.Cylinder, pipe, new Vector3(0f, py, z + 0.15f), new Vector3(0.1f / CR, 15f / CH, 0.1f / CR), new Vector3(0, 0, 90f));
            // a hatch door (shut) with a round porthole, a handle, and the EXIT light over it
            {
                var d = new Vector3(1.3f, 0f, z);
                Art.Box(t, trim, d + new Vector3(0f, 1.1f, 0.04f), new Vector3(1.25f, 2.25f, 0.08f));                  // the frame
                Art.Box(t, metal * 0.8f, d + new Vector3(0f, 1.08f, 0.09f), new Vector3(1.05f, 2.1f, 0.06f));         // the door
                for (int k = 0; k < 3; k++) Art.Box(t, metal * 0.6f, d + new Vector3(0f, 0.35f + k * 0.7f, 0.125f), new Vector3(0.95f, 0.04f, 0.015f));
                Art.Part(t, Art.Cylinder, metal * 1.2f, d + new Vector3(0f, 1.55f, 0.13f), new Vector3(0.36f / CR, 0.03f / CH, 0.36f / CR), new Vector3(90, 0, 0));
                Art.Part(t, Art.Cylinder, Color.white, d + new Vector3(0f, 1.55f, 0.146f), new Vector3(0.28f / CR, 0.01f / CH, 0.28f / CR), new Vector3(90, 0, 0), false, Workbench.Glow(new Color(0.25f, 0.35f, 0.6f), 0.6f));
                Art.Box(t, metal * 1.3f, d + new Vector3(0.38f, 1.05f, 0.15f), new Vector3(0.05f, 0.22f, 0.05f));    // the handle
                Art.Box(t, Color.white, d + new Vector3(0f, 2.42f, 0.06f), new Vector3(0.42f, 0.14f, 0.06f), default, false, Workbench.Glow(new Color(1f, 0.15f, 0.1f), 1.8f));
                Light(t, d + new Vector3(0f, 2.3f, 0.4f), new Color(1f, 0.2f, 0.15f), 0.6f, 2.2f).name = "exit light";
                Art.Box(t, Color.white, d + new Vector3(-0.3f, 0.4f, 0.13f), new Vector3(0.3f, 0.5f, 0.01f), default, false, Tone(Tones.Stain, 1.1f)); // (scuffed)
            }
            // a coat hook with a jacket on it, a light switch, a couple of posters
            Art.Box(t, metal, new Vector3(-1.3f, 1.85f, z + 0.06f), new Vector3(0.5f, 0.05f, 0.08f));
            Art.Box(t, new Color(0.25f, 0.3f, 0.2f), new Vector3(-1.3f, 1.45f, z + 0.12f), new Vector3(0.48f, 0.75f, 0.12f), new Vector3(0, 0, 3f));
            Art.Box(t, new Color(0.85f, 0.83f, 0.78f), new Vector3(0.45f, 1.25f, z + 0.02f), new Vector3(0.08f, 0.12f, 0.02f));
            foreach (var (x, y, w, h, hue) in new[] { (-4.3f, 1.8f, 0.7f, 0.95f, 0.08f), (-3.2f, 2.2f, 0.5f, 0.6f, 0.6f), (4.2f, 1.9f, 0.8f, 0.6f, 0.33f) })
            {
                Art.Box(t, new Color(0.9f, 0.88f, 0.82f), new Vector3(x, y, z + 0.02f), new Vector3(w + 0.06f, h + 0.06f, 0.01f), new Vector3(0, 0, R() * 6f - 3f));
                var bg = Color.HSVToRGB(hue, 0.6f, 0.65f);
                Art.Box(t, bg, new Vector3(x, y, z + 0.03f), new Vector3(w, h, 0.01f), new Vector3(0, 0, R() * 6f - 3f));
                Art.Part(t, Art.Sphere, Color.Lerp(bg, Color.white, 0.7f), new Vector3(x, y + h * 0.08f, z + 0.04f), new Vector3(w * 0.45f, h * 0.4f, 0.01f) / SR);
            }
        }

        // ------------------------------------------------------------------ the telly's light

        Light m_TvNear, m_TvSpill;
        Material m_HaloMat;
        Texture2D m_HaloTex;

        /// <summary>The light round the telly: a light just in front of the screen (on the set, the speakers, the floor in
        /// front), a wide one down over the floor towards the couch, and a soft glow hung round the screen.</summary>
        void BuildTellyGlow(Transform tv)
        {
            m_TvNear = Light(tv, k_Screen + new Vector3(0f, 0.02f, 0.55f), new Color(0.6f, 0.8f, 1f), 2.4f, 3.4f);
            m_TvNear.name = "telly near light";
            m_TvSpill = Light(tv, new Vector3(0f, 1.25f, 0.45f), new Color(0.6f, 0.8f, 1f), 4f, 7f);
            m_TvSpill.name = "telly spill light";
            m_TvSpill.type = LightType.Spot;
            m_TvSpill.spotAngle = 120f;
            m_TvSpill.innerSpotAngle = 60f;
            m_TvSpill.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.55f, 1f)); // (down over the floor, to the couch)
            // the glow round the screen: additive, nothing over the picture itself, fading out round it
            var sh = Resources.Load<Shader>("SpaceArena/SpaceGlow");
            if (sh == null || !sh.isSupported) return;
            const int TW = 96, TH = 80;
            const float QW = 1.2f, QH = 1f, HW = 0.27f, HH = 0.21f;
            m_HaloTex = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "telly halo" };
            var px = new Color32[TW * TH];
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    float lx = ((x + 0.5f) / TW - 0.5f) * QW, ly = ((y + 0.5f) / TH - 0.5f) * QH;
                    float dx = Mathf.Max(0f, Mathf.Abs(lx) - HW), dy = Mathf.Max(0f, Mathf.Abs(ly) - HH);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= 0f ? 0f : Mathf.Exp(-d / 0.075f) * Mathf.SmoothStep(0f, 1f, d / 0.012f); // (none on the picture)
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    px[y * TW + x] = new Color32(v, v, v, v);
                }
            m_HaloTex.SetPixels32(px);
            m_HaloTex.Apply(false);
            m_HaloMat = new Material(sh) { name = "telly halo", renderQueue = 3000 };
            m_HaloMat.SetTexture("_MainTex", m_HaloTex);
            m_HaloMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m_HaloMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m_HaloMat.SetFloat("_ZWrite", 0f);
            var halo = new GameObject("telly halo");
            halo.transform.SetParent(tv, false);
            halo.transform.localPosition = k_Screen + new Vector3(0f, 0f, 0.012f);
            halo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            halo.transform.localScale = new Vector3(QW, QH, 1f);
            halo.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mr = halo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m_HaloMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Every frame: the telly's extra lights and glow take the picture's colour and flicker (and Settings >
        /// Display > SHIP LOBBY > Telly turns them up or down - the picture itself a little).</summary>
        void TickTellyGlow(Color tint, float flicker)
        {
            float tvk = GameSettings.LobbyTvNow;
            if (m_TvNear) { m_TvNear.color = tint; m_TvNear.intensity = 2.4f * flicker * tvk; }
            if (m_TvSpill) { m_TvSpill.color = tint; m_TvSpill.intensity = 4f * flicker * tvk; }
            if (m_HaloMat) m_HaloMat.SetColor("_Color", tint * (0.55f * flicker * Mathf.Min(tvk, 1.6f)));
            LobbyArcade.SetScreenBrightness(Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(tvk)) * (tvk > 1f ? 1f + (tvk - 1f) * 0.25f : 1f));
        }

        // ------------------------------------------------------------------ the glowing ball

        Material m_BallCore, m_BallBand;
        Light m_BallLight;
        float m_BallSet = -1f;

        /// <summary>The ball's glow and light follow Settings > Display > SHIP LOBBY > Glowing ball.</summary>
        void TickBall()
        {
            float k = GameSettings.LobbyBallNow;
            if (Mathf.Approximately(k, m_BallSet)) return;
            m_BallSet = k;
            if (m_BallCore != null && m_BallCore.HasProperty("_Intensity")) m_BallCore.SetFloat("_Intensity", Mathf.Max(0.35f, BallCore * k));
            if (m_BallBand != null && m_BallBand.HasProperty("_Intensity")) m_BallBand.SetFloat("_Intensity", Mathf.Max(0.3f, BallBand * k));
            if (m_BallLight) { m_BallLight.intensity = BallLight * k; m_BallLight.enabled = k > 0.01f; }
        }

        /// <summary>(tests) how bright the glowing ball's light is right now.</summary>
        public static float BallLightNow => s_I != null && s_I.m_BallLight != null && s_I.m_BallLight.enabled ? s_I.m_BallLight.intensity : 0f;
        /// <summary>(tests) how bright the telly's light on the room is right now (its three lights together).</summary>
        public static float TellyLightNow => s_I == null ? 0f : (s_I.m_TvLight ? s_I.m_TvLight.intensity : 0f) + (s_I.m_TvNear ? s_I.m_TvNear.intensity : 0f) + (s_I.m_TvSpill ? s_I.m_TvSpill.intensity : 0f);

        /// <summary>The ball's glow (the core, the band) and its light at 100% - much calmer than it was.</summary>
        const float BallCore = 1.5f, BallBand = 1.2f, BallLight = 1.1f;

        // ------------------------------------------------------------------ what's under the mouse

        /// <summary>The telly is under this point of the screen (GUI space: y down from the top).</summary>
        public static bool TellyUnder(Vector2 gui)
        {
            var cam = Camera.main;
            if (s_I == null || s_I.m_Tv == null || cam == null) return false;
            var ray = cam.ScreenPointToRay(new Vector3(gui.x, Screen.height - gui.y, 0f));
            var tv = s_I.m_Tv;
            return k_TvBounds.IntersectRay(new Ray(tv.InverseTransformPoint(ray.origin), tv.InverseTransformDirection(ray.direction)));
        }

        /// <summary>(tests) the middle of the telly's picture on screen (GUI space), if it's in view.</summary>
        public static bool TellyMiddleOnScreen(out Vector2 gui)
        {
            gui = default;
            var cam = Camera.main;
            if (s_I == null || s_I.m_Tv == null || cam == null) return false;
            var sp = cam.WorldToScreenPoint(s_I.m_Tv.TransformPoint(k_Screen));
            if (sp.z <= 0f) return false;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        static readonly List<Renderer> s_RoomRenderers = new List<Renderer>();

        /// <summary>(tests) how many of a grid of rays through the camera's view (24 x 14) leave the room without hitting
        /// anything of it - each one would show the sky.</summary>
        public static int SkyRays()
        {
            var cam = Camera.main;
            if (s_I == null || s_I.m_Room == null || cam == null) return -1;
            s_RoomRenderers.Clear();
            s_I.m_Room.GetComponentsInChildren(false, s_RoomRenderers);
            int miss = 0;
            for (int y = 0; y < 14; y++)
                for (int x = 0; x < 24; x++)
                {
                    var ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / 24f, (y + 0.5f) / 14f, 0f));
                    bool hit = false;
                    foreach (var r in s_RoomRenderers)
                        if (r != null && r.enabled && r.bounds.IntersectRay(ray, out float d) && d < cam.farClipPlane) { hit = true; break; }
                    if (!hit) miss++;
                }
            return miss;
        }

        void ClearRoomExtras()
        {
            if (m_HaloMat) Destroy(m_HaloMat);
            if (m_HaloTex) Destroy(m_HaloTex);
            m_HaloMat = null; m_HaloTex = null;
            m_TvNear = m_TvSpill = m_BallLight = null;
            m_BallCore = m_BallBand = null;
            m_BallSet = -1f;
        }
    }
}
