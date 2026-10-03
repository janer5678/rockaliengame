using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RockGame
{
    /// <summary>
    /// Settings > Display, the per-PC extras (saved in PlayerPrefs, applied live):
    /// SHADOWS - how dark the sun's shadows are and how far out they're drawn;
    /// INTERFACE - the font every bit of menu / HUD text uses, the UI scale, the HUD's opacity and the accent colour;
    /// ALIEN GLOW - the enemy glow's strength and thickness (the glow itself is always on);
    /// POST PROCESSING > "On the UI too" - the menus, HUD, icons and inventory get the post processing as well.
    /// </summary>
    public static partial class GameSettings
    {
        /// <summary>The PSX / AI PSX test graphics are hidden: everyone plays Normal. (true brings the picker back on
        /// the main menu and in Settings > Display; -psx / -aipsx on the command line still work for the tests.)</summary>
        public const bool ShowGraphicsPicker = false;

        static bool s_UiLoaded;
        static float s_ShadowStrength = 1f, s_ShadowDistance = ShadowDistanceDefault;
        static int s_Font;
        static float s_UiScale = 1f, s_HudOpacity = 1f;
        static int s_Accent;
        static bool s_PostOnUi;

        public const float ShadowDistanceMin = 20f, ShadowDistanceMax = 300f, ShadowDistanceDefault = 50f;
        public const float UiScaleMin = 0.75f, UiScaleMax = 1.4f, HudOpacityMin = 0.25f;

        /// <summary>The fonts to pick from (Windows fonts, so nothing to ship; the ones this PC hasn't got are left out).
        /// The first is Unity's own (the original look).</summary>
        public static readonly string[] FontChoices = { "Classic", "Bahnschrift", "Impact", "Consolas", "Trebuchet MS", "Segoe UI", "Verdana", "Georgia", "Tahoma" };
        public static readonly string[] FontBlurbs =
        {
            "the original look", "industrial, like a survival game's HUD", "big and blocky", "alien terminal",
            "rounded and friendly", "clean and modern", "wide and easy to read", "old-fashioned serif", "small and tidy",
        };

        /// <summary>Accent colours (picked options' text, the menus' section titles).</summary>
        public static readonly Color[] AccentChoices =
        {
            new Color(1f, 0.82f, 0.35f),   // gold (the original)
            new Color(0.45f, 1f, 0.55f),   // alien green
            new Color(0.4f, 0.85f, 1f),    // ice blue
            new Color(1f, 0.55f, 0.25f),   // rust orange
            new Color(1f, 0.45f, 0.75f),   // pink
            new Color(0.85f, 0.85f, 0.9f), // silver
        };
        public static readonly string[] AccentNames = { "Gold", "Alien green", "Ice blue", "Rust", "Pink", "Silver" };

        /// <summary>Fired when a shadow or interface setting changes.</summary>
        public static event System.Action UiLookChanged;

        static void LoadUi()
        {
            if (s_UiLoaded) return;
            s_UiLoaded = true;
            s_ShadowStrength = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.ShadowStrength", 1f));
            s_ShadowDistance = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.ShadowDistance", ShadowDistanceDefault), ShadowDistanceMin, ShadowDistanceMax);
            s_Font = Mathf.Clamp(PlayerPrefs.GetInt("RockGame.UiFont", 0), 0, FontChoices.Length - 1);
            s_UiScale = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.UiScale", 1f), UiScaleMin, UiScaleMax);
            s_HudOpacity = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.HudOpacity", 1f), HudOpacityMin, 1f);
            s_Accent = Mathf.Clamp(PlayerPrefs.GetInt("RockGame.UiAccent", 0), 0, AccentChoices.Length - 1);
            s_PostOnUi = PlayerPrefs.GetInt("RockGame.PostOnUi", 0) == 1;
            // the glow (it used to be a host value in CHANGE VALUES; now it's how this PC draws it)
            Cfg.AlienOutlineStrength = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.GlowStrength", Cfg.AlienOutlineStrength), 0.02f, 1f);
            Cfg.AlienOutlineWidth = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.GlowWidth", Cfg.AlienOutlineWidth), 0.005f, 0.12f);
        }

        static void SaveUi()
        {
            PlayerPrefs.SetFloat("RockGame.ShadowStrength", s_ShadowStrength);
            PlayerPrefs.SetFloat("RockGame.ShadowDistance", s_ShadowDistance);
            PlayerPrefs.SetInt("RockGame.UiFont", s_Font);
            PlayerPrefs.SetFloat("RockGame.UiScale", s_UiScale);
            PlayerPrefs.SetFloat("RockGame.HudOpacity", s_HudOpacity);
            PlayerPrefs.SetInt("RockGame.UiAccent", s_Accent);
            PlayerPrefs.SetInt("RockGame.PostOnUi", s_PostOnUi ? 1 : 0);
            PlayerPrefs.SetFloat("RockGame.GlowStrength", Cfg.AlienOutlineStrength);
            PlayerPrefs.SetFloat("RockGame.GlowWidth", Cfg.AlienOutlineWidth);
            PlayerPrefs.Save();
        }

        /// <summary>How dark the sun's shadows are (0 = none, 1 = full).</summary>
        public static float ShadowStrength { get { LoadUi(); return s_ShadowStrength; } }
        /// <summary>How far from the camera shadows are drawn (m).</summary>
        public static float ShadowDistance { get { LoadUi(); return s_ShadowDistance; } }
        public static int UiFont { get { LoadUi(); return s_Font; } }
        /// <summary>A multiplier on the size of every menu and HUD element.</summary>
        public static float UiScale { get { LoadUi(); return s_UiScale; } }
        /// <summary>How see-through the in-game HUD is (menus, the inventory and the pause menu stay solid).</summary>
        public static float HudOpacity { get { LoadUi(); return s_HudOpacity; } }
        public static int UiAccent { get { LoadUi(); return s_Accent; } }
        public static Color AccentColor => AccentChoices[UiAccent];
        /// <summary>The post processing goes over the UI too (off to start with).</summary>
        public static bool PostOnUi { get { LoadUi(); return s_PostOnUi; } }

        public static void SetShadows(float strength, float distance, bool save = true)
        {
            LoadUi();
            strength = Mathf.Clamp01(strength);
            distance = Mathf.Clamp(distance, ShadowDistanceMin, ShadowDistanceMax);
            if (Mathf.Approximately(strength, s_ShadowStrength) && Mathf.Approximately(distance, s_ShadowDistance)) return;
            s_ShadowStrength = strength;
            s_ShadowDistance = distance;
            if (save) SaveUi();
            UiLookChanged?.Invoke();
        }

        public static void SetInterface(int font, float scale, float hudOpacity, int accent, bool save = true)
        {
            LoadUi();
            font = Mathf.Clamp(font, 0, FontChoices.Length - 1);
            scale = Mathf.Clamp(scale, UiScaleMin, UiScaleMax);
            hudOpacity = Mathf.Clamp(hudOpacity, HudOpacityMin, 1f);
            accent = Mathf.Clamp(accent, 0, AccentChoices.Length - 1);
            if (font == s_Font && Mathf.Approximately(scale, s_UiScale) && Mathf.Approximately(hudOpacity, s_HudOpacity) && accent == s_Accent) return;
            s_Font = font; s_UiScale = scale; s_HudOpacity = hudOpacity; s_Accent = accent;
            if (save) SaveUi();
            UiLookChanged?.Invoke();
        }

        public static void SetPostOnUi(bool on, bool save = true)
        {
            LoadUi();
            if (on == s_PostOnUi) return;
            s_PostOnUi = on;
            if (save) SaveUi();
            UiLookChanged?.Invoke();
        }

        public static void SetAlienGlow(float strength, float width, bool save = true)
        {
            LoadUi();
            strength = Mathf.Clamp(strength, 0.02f, 1f);
            width = Mathf.Clamp(width, 0.005f, 0.12f);
            if (Mathf.Approximately(strength, Cfg.AlienOutlineStrength) && Mathf.Approximately(width, Cfg.AlienOutlineWidth)) return;
            Cfg.AlienOutlineStrength = strength;
            Cfg.AlienOutlineWidth = width;
            if (save) SaveUi();
        }

        /// <summary>Shadows, interface and glow back to the defaults.</summary>
        public static void ResetUiLook(bool save = true)
        {
            LoadUi();
            s_ShadowStrength = 1f; s_ShadowDistance = ShadowDistanceDefault;
            s_Font = 0; s_UiScale = 1f; s_HudOpacity = 1f; s_Accent = 0; s_PostOnUi = false;
            Cfg.AlienOutlineStrength = 0.3f; Cfg.AlienOutlineWidth = 0.03f;
            if (save) SaveUi();
            UiLookChanged?.Invoke();
        }

        /// <summary>Is this font on this PC (the first one always is).</summary>
        public static bool FontInstalled(int i)
        {
            if (i <= 0) return true;
            if (s_Installed == null) s_Installed = new HashSet<string>(Font.GetOSInstalledFontNames(), System.StringComparer.OrdinalIgnoreCase);
            return i < FontChoices.Length && s_Installed.Contains(FontChoices[i]);
        }
        static HashSet<string> s_Installed;
    }

    /// <summary>
    /// Applies Settings > Display's shadows (the sun's shadow strength, the URP asset's shadow distance) and the UI
    /// font, and draws the UI under the post processing when "On the UI too" is picked: the Hud's OnGUI then draws
    /// into a screen-sized texture (BeginUi / EndUi) instead of the screen, and a pass of ours lays that texture over
    /// the camera's picture just before URP's post processing, so bloom, vignette, grading and the extra looks go over
    /// the menus, HUD, icons and inventory as well (a frame behind; the clicks are unchanged).
    /// </summary>
    public class UiLook : MonoBehaviour
    {
        static UiLook s_I;
        Light m_Sun;
        float m_BaseShadowDistance = -1f;
        UniversalRenderPipelineAsset m_Urp;
        RenderTexture m_UiRt;
        Material m_Mat;
        UiPass m_Pass;
        bool m_HaveUi;
        int m_UiFrame = -10;
        static Font[] s_Fonts;
        static Font s_DefaultFont;

        /// <summary>(tests) the UI went through the post processing this frame.</summary>
        public static bool UiUnderPost => s_I != null && s_I.m_HaveUi && Time.frameCount - s_I.m_UiFrame <= 2;
        /// <summary>(tests) the sun's shadow strength and the shadow distance in use.</summary>
        public static float SunShadowStrength => s_I != null && s_I.m_Sun != null ? s_I.m_Sun.shadowStrength : -1f;
        public static float UrpShadowDistance => s_I != null && s_I.m_Urp != null ? s_I.m_Urp.shadowDistance : -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (s_I != null) return;
            var go = new GameObject("UiLook");
            DontDestroyOnLoad(go);
            s_I = go.AddComponent<UiLook>();
        }

        void Awake()
        {
            m_Urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (m_Urp != null) m_BaseShadowDistance = m_Urp.shadowDistance;
            var sh = Resources.Load<Shader>("PostFx/UiComposite");
            if (sh != null && sh.isSupported)
            {
                m_Mat = new Material(sh) { name = "RockGame UI composite", hideFlags = HideFlags.DontSave };
                m_Pass = new UiPass(m_Mat);
            }
            else Debug.LogWarning("[RockGame] PostFx/UiComposite shader missing: post processing can't go over the UI");
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            GameSettings.UiLookChanged += ApplyShadows;
            ApplyShadows();
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            GameSettings.UiLookChanged -= ApplyShadows;
            if (m_Urp != null && m_BaseShadowDistance > 0f) m_Urp.shadowDistance = m_BaseShadowDistance; // (the asset as it was - matters in the editor)
            if (m_UiRt != null) { m_UiRt.Release(); Destroy(m_UiRt); }
            if (m_Mat != null) Destroy(m_Mat);
        }

        void LateUpdate()
        {
            // the sun can be rebuilt (scene loads): keep its shadows in step
            if (m_Sun == null || !m_Sun.isActiveAndEnabled)
            {
                m_Sun = RenderSettings.sun;
                if (m_Sun == null)
                    foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional && l.enabled) { m_Sun = l; break; }
                ApplyShadows();
            }
            else if (!Mathf.Approximately(m_Sun.shadowStrength, GameSettings.ShadowStrength)) m_Sun.shadowStrength = GameSettings.ShadowStrength;
        }

        void ApplyShadows()
        {
            if (this == null) return;
            if (m_Sun != null) m_Sun.shadowStrength = GameSettings.ShadowStrength;
            if (m_Urp != null) m_Urp.shadowDistance = GameSettings.ShadowDistance;
        }

        // ------------------------------------------------------------------ the font

        /// <summary>Puts the picked font on the GUI skin (every GUI style that doesn't name its own font uses it).</summary>
        public static void ApplyFont()
        {
            var skin = GUI.skin;
            if (skin == null) return;
            if (s_DefaultFont == null) s_DefaultFont = skin.font;
            var f = FontFor(GameSettings.UiFont);
            if (skin.font != f) skin.font = f;
            CurrentFontName = f != null ? f.name : "";
        }

        /// <summary>One of the font choices (Unity's own for the first, or one this PC hasn't got).</summary>
        public static Font FontFor(int i)
        {
            if (s_DefaultFont == null && GUI.skin != null) s_DefaultFont = GUI.skin.font;
            if (i <= 0 || i >= GameSettings.FontChoices.Length || !GameSettings.FontInstalled(i)) return s_DefaultFont;
            if (s_Fonts == null) s_Fonts = new Font[GameSettings.FontChoices.Length];
            if (s_Fonts[i] == null) s_Fonts[i] = Font.CreateDynamicFontFromOSFont(GameSettings.FontChoices[i], 16);
            return s_Fonts[i] != null ? s_Fonts[i] : s_DefaultFont;
        }

        /// <summary>(tests) the font the GUI is using now.</summary>
        public static string CurrentFontName { get; private set; } = "";

        // ------------------------------------------------------------------ the UI under the post processing

        static bool Wanted => s_I != null && s_I.m_Pass != null && GameSettings.PostOnUi && PostFx.Active && Camera.main != null;

        /// <summary>Called first thing in OnGUI: on the repaint, while the UI goes under the post processing, the GUI is
        /// drawn into our texture. Returns the render target to put back (EndUi), or null if nothing was changed.</summary>
        public static bool BeginUi(out RenderTexture prev)
        {
            prev = null;
            if (Event.current.type != EventType.Repaint || !Wanted) return false;
            var me = s_I;
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            if (me.m_UiRt == null || me.m_UiRt.width != w || me.m_UiRt.height != h)
            {
                if (me.m_UiRt != null) { me.m_UiRt.Release(); Destroy(me.m_UiRt); }
                me.m_UiRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "RockGame UI", hideFlags = HideFlags.DontSave };
                me.m_UiRt.Create();
            }
            prev = RenderTexture.active;
            RenderTexture.active = me.m_UiRt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            return true;
        }

        public static void EndUi(RenderTexture prev)
        {
            RenderTexture.active = prev;
            if (s_I == null) return;
            s_I.m_HaveUi = true;
            s_I.m_UiFrame = Time.frameCount;
        }

        /// <summary>Our pass goes in for the main camera only, while the UI goes under the post processing.</summary>
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (m_Pass == null || cam == null || cam != Camera.main || cam.cameraType != CameraType.Game) return;
            // (stops as soon as the option goes off: the GUI is drawn straight to the screen again that frame)
            if (!Wanted || m_UiRt == null || !m_HaveUi || Time.frameCount - m_UiFrame > 2) { m_HaveUi = false; return; }
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            m_Mat.SetTexture(k_UiTex, m_UiRt);
            m_Mat.SetFloat(k_UiFlip, FlipUi ? 1f : 0f);
            data.scriptableRenderer.EnqueuePass(m_Pass);
        }

        static readonly int k_UiTex = Shader.PropertyToID("_RgUiTex"), k_UiFlip = Shader.PropertyToID("_RgUiFlip");
        /// <summary>Turn the UI texture upside down as it goes on (how the GUI lands in a texture depends on the graphics API).</summary>
        public static bool FlipUi;

        /// <summary>The UI texture laid over the camera colour (premultiplied alpha), before URP's post processing.</summary>
        class UiPass : ScriptableRenderPass
        {
            readonly Material m_Mat;
            class PassData { public Material Mat; }

            public UiPass(Material mat)
            {
                m_Mat = mat;
                // after our own stylize pass (outlines, haze...) so those don't draw ink round the text
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing + 1;
                profilingSampler = new ProfilingSampler("RockGame UI");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer || !res.activeColorTexture.IsValid()) return;
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("RockGame UI", out var data, profilingSampler))
                {
                    data.Mat = m_Mat;
                    builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc((PassData d, RasterGraphContext c) => c.cmd.DrawProcedural(Matrix4x4.identity, d.Mat, 0, MeshTopology.Triangles, 3, 1));
                }
            }
        }
    }
}
