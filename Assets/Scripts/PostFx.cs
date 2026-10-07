using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RockGame
{
    /// <summary>Settings > Display > POST PROCESSING (Normal graphics, just on this PC): a master switch plus bloom,
    /// vignette and colour grading, each with its own strength, and the EXTRA LOOKS (outlines, ambient occlusion, distance
    /// haze, depth of field, film grain, chromatic aberration, sharpen, cel banding), each off to start with. Saved in
    /// PlayerPrefs; changes apply live.</summary>
    public static partial class GameSettings
    {
        static bool s_PostLoaded;
        static bool s_Post = DisplayDefaults.PostFx, s_PostBloom = DisplayDefaults.Bloom, s_PostVignette = DisplayDefaults.Vignette, s_PostGrading = DisplayDefaults.Grading;
        static float s_PostBloomStr = DisplayDefaults.BloomStrength, s_PostVignetteStr = DisplayDefaults.VignetteStrength, s_PostGradingStr = DisplayDefaults.GradingStrength;

        /// <summary>Fired when any post processing setting changes.</summary>
        public static event System.Action PostFxChanged;

        /// <summary>The screen resolution the outline thicknesses are set at: 2560x1440 looks right, and every other
        /// resolution scales them with its height, so they're the same share of the screen everywhere.</summary>
        public const float OutlineRefHeight = 1440f;

        /// <summary>Pixels on this screen for a thickness set in pixels at 1440p (thickness x screen height / 1440).</summary>
        public static float ScreenPx(float pxAt1440, float screenHeight = -1f) => pxAt1440 * (screenHeight > 0f ? screenHeight : Screen.height) / OutlineRefHeight;

        static void LoadPost()
        {
            if (s_PostLoaded) return;
            s_PostLoaded = true;
            s_Post = PlayerPrefs.GetInt("RockGame.PostFx", B(DisplayDefaults.PostFx)) == 1;
            s_PostBloom = PlayerPrefs.GetInt("RockGame.PostBloom", B(DisplayDefaults.Bloom)) == 1;
            s_PostVignette = PlayerPrefs.GetInt("RockGame.PostVignette", B(DisplayDefaults.Vignette)) == 1;
            s_PostGrading = PlayerPrefs.GetInt("RockGame.PostGrading", B(DisplayDefaults.Grading)) == 1;
            s_PostBloomStr = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.PostBloomStr", DisplayDefaults.BloomStrength));
            s_PostVignetteStr = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.PostVignetteStr", DisplayDefaults.VignetteStrength));
            s_PostGradingStr = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.PostGradingStr", DisplayDefaults.GradingStrength));
        }

        static int B(bool b) => b ? 1 : 0;

        /// <summary>Post processing on at all (off = exactly the plain look).</summary>
        public static bool PostFx { get { LoadPost(); return s_Post; } }
        public static bool PostBloom { get { LoadPost(); return s_PostBloom; } }
        public static bool PostVignette { get { LoadPost(); return s_PostVignette; } }
        public static bool PostGrading { get { LoadPost(); return s_PostGrading; } }
        /// <summary>0..1 (0.5 = the default look).</summary>
        public static float PostBloomStrength { get { LoadPost(); return s_PostBloomStr; } }
        public static float PostVignetteStrength { get { LoadPost(); return s_PostVignetteStr; } }
        public static float PostGradingStrength { get { LoadPost(); return s_PostGradingStr; } }

        public static void SetPostFx(bool on, bool bloom, bool vignette, bool grading, float bloomStr, float vignetteStr, float gradingStr, bool save = true)
        {
            LoadPost();
            bloomStr = Mathf.Clamp01(bloomStr); vignetteStr = Mathf.Clamp01(vignetteStr); gradingStr = Mathf.Clamp01(gradingStr);
            if (on == s_Post && bloom == s_PostBloom && vignette == s_PostVignette && grading == s_PostGrading
                && Mathf.Approximately(bloomStr, s_PostBloomStr) && Mathf.Approximately(vignetteStr, s_PostVignetteStr) && Mathf.Approximately(gradingStr, s_PostGradingStr)) return;
            s_Post = on; s_PostBloom = bloom; s_PostVignette = vignette; s_PostGrading = grading;
            s_PostBloomStr = bloomStr; s_PostVignetteStr = vignetteStr; s_PostGradingStr = gradingStr;
            if (save)
            {
                PlayerPrefs.SetInt("RockGame.PostFx", on ? 1 : 0);
                PlayerPrefs.SetInt("RockGame.PostBloom", bloom ? 1 : 0);
                PlayerPrefs.SetInt("RockGame.PostVignette", vignette ? 1 : 0);
                PlayerPrefs.SetInt("RockGame.PostGrading", grading ? 1 : 0);
                PlayerPrefs.SetFloat("RockGame.PostBloomStr", bloomStr);
                PlayerPrefs.SetFloat("RockGame.PostVignetteStr", vignetteStr);
                PlayerPrefs.SetFloat("RockGame.PostGradingStr", gradingStr);
                PlayerPrefs.Save();
            }
            PostFxChanged?.Invoke();
        }

        public static void SetPostFx(bool on, bool save = true) => SetPostFx(on, PostBloom, PostVignette, PostGrading, PostBloomStrength, PostVignetteStrength, PostGradingStrength, save);

        /// <summary>Post processing back to the defaults (all on, middle strengths, every extra look off).</summary>
        public static void ResetPostFx(bool save = true)
        {
            for (int i = 0; i < PostExtraCount; i++) SetPostExtra((PostExtra)i, DisplayDefaults.PostExtraOn(i), DisplayDefaults.PostExtraStrength(i), save);
            SetPostFx(DisplayDefaults.PostFx, DisplayDefaults.Bloom, DisplayDefaults.Vignette, DisplayDefaults.Grading,
                DisplayDefaults.BloomStrength, DisplayDefaults.VignetteStrength, DisplayDefaults.GradingStrength, save);
        }

        // ---- the extra looks: each off to start with, with its own strength ----

        /// <summary>The extra looks, in the order they're listed in Settings > Display.</summary>
        public enum PostExtra { Outlines, AmbientOcclusion, Haze, DepthOfField, FilmGrain, Chromatic, Sharpen, CelBanding }
        public const int PostExtraCount = 8;
        public static readonly string[] PostExtraNames = { "Outlines", "Ambient occlusion", "Distance haze", "Depth of field", "Film grain", "Chromatic aberration", "Sharpen", "Cel banding" };
        static readonly bool[] s_Extra = new bool[PostExtraCount];
        static readonly float[] s_ExtraStr = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f }; // (loaded: DisplayDefaults.PostExtraStrength(i))
        static bool s_ExtraLoaded;

        static void LoadExtras()
        {
            if (s_ExtraLoaded) return;
            s_ExtraLoaded = true;
            for (int i = 0; i < PostExtraCount; i++)
            {
                string key = "RockGame.PostX." + (PostExtra)i;
                s_Extra[i] = PlayerPrefs.GetInt(key, B(DisplayDefaults.PostExtraOn(i))) == 1;
                s_ExtraStr[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(key + "Str", DisplayDefaults.PostExtraStrength(i)));
            }
        }

        /// <summary>Is this extra look switched on (it's only drawn while post processing is on too).</summary>
        public static bool PostExtraOn(PostExtra e) { LoadExtras(); return s_Extra[(int)e]; }
        /// <summary>0..1 (0.5 = the middle).</summary>
        public static float PostExtraStrength(PostExtra e) { LoadExtras(); return s_ExtraStr[(int)e]; }
        /// <summary>The strength if it's on, else 0.</summary>
        public static float PostExtraAmount(PostExtra e) => PostExtraOn(e) ? PostExtraStrength(e) : 0f;

        public static void SetPostExtra(PostExtra e, bool on, float strength, bool save = true)
        {
            LoadExtras();
            int i = (int)e;
            strength = Mathf.Clamp01(strength);
            if (on == s_Extra[i] && Mathf.Approximately(strength, s_ExtraStr[i])) return;
            s_Extra[i] = on;
            s_ExtraStr[i] = strength;
            if (save)
            {
                string key = "RockGame.PostX." + e;
                PlayerPrefs.SetInt(key, on ? 1 : 0);
                PlayerPrefs.SetFloat(key + "Str", strength);
                PlayerPrefs.Save();
            }
            PostFxChanged?.Invoke();
        }

        /// <summary>(tests) every extra look off, without saving.</summary>
        public static void PostExtrasOff() { for (int i = 0; i < PostExtraCount; i++) SetPostExtra((PostExtra)i, false, PostExtraStrength((PostExtra)i), false); }
    }

    /// <summary>
    /// The Normal look's post processing: one global URP Volume with a profile made here (bloom, colour adjustments,
    /// vignette, and for the extra looks depth of field, film grain and chromatic aberration), and the main camera's post
    /// processing switched on while it's wanted. Off (or in PSX / AI PSX) the camera renders no post processing at all,
    /// so the look is exactly what it was without it.
    /// Every parameter the URP asset's own default profile sets (bloom, tone mapping, vignette) is overridden here, so
    /// an effect switched off really is off. (The bloom shader variants are kept in the build because
    /// the profiles in Assets/Settings use them - URP strips the post variants no profile asset uses; the depth of
    /// field and chromatic aberration ones are kept by Assets/Settings/PostFxVariants.asset, see ProjectSetup.)
    /// The other extra looks: outlines, distance haze, sharpen and cel banding are one full-screen pass of our own
    /// (StylizePass, shader Assets/Game/Resources/PostFx/Stylize.shader) put in before URP's post processing, only on
    /// frames where one of them is on; ambient occlusion turns up the renderer's own SSAO (PC_Renderer's, which
    /// always runs gently) and puts it back exactly when it's off.
    /// </summary>
    public class PostFx : MonoBehaviour
    {
        static PostFx s_I;
        Volume m_Volume;
        VolumeProfile m_Profile;
        Bloom m_Bloom;
        Tonemapping m_Tone;
        ColorAdjustments m_Color;
        Vignette m_Vignette;
        DepthOfField m_Dof;
        FilmGrain m_Grain;
        ChromaticAberration m_Chroma;
        Camera m_Cam;
        Material m_StylizeMat;
        Material m_MaskMat, m_ToolMaskMat;
        int m_MenuPost;
        static readonly List<Renderer> s_VmRends = new List<Renderer>();
        /// <summary>The layer the first-person hands are put on while they (or what they hold) have looks of their own
        /// (HANDS): the mask pass draws it as 1. (Nothing else uses it; the camera and lights see every layer.)</summary>
        public const int HandLayer = 29;
        /// <summary>...and the layer what they hold goes on (TOOLS & WEAPONS): drawn into the mask as 0.5.</summary>
        public const int ToolLayer = 28;
        StylizePass m_Pass;
        bool m_StylizeOn;
        // the renderer's SSAO settings (internal to URP: reached by reflection) and what the asset had
        object m_Ao;
        FieldInfo m_AoIntensity, m_AoRadius, m_AoDirect;
        float m_AoBase0, m_AoBase1, m_AoBase2;

        /// <summary>Post processing is drawn right now (switched on, Normal graphics).</summary>
        public static bool Active => GameSettings.PostFx && GameSettings.GraphicsMode == 0;

        /// <summary>(tests) whether the main camera renders post processing.</summary>
        public static bool CameraOn
        {
            get
            {
                var cam = Camera.main;
                return cam != null && cam.TryGetComponent(out UniversalAdditionalCameraData d) && d.renderPostProcessing;
            }
        }

        /// <summary>(tests) the extra full-screen pass (outlines, haze, sharpen, cel banding) is being drawn.</summary>
        public static bool StylizeOn => s_I != null && s_I.m_StylizeOn;
        /// <summary>(tests) the renderer's SSAO intensity right now, and what the asset has.</summary>
        public static float AoIntensity => s_I != null && s_I.m_Ao != null ? (float)s_I.m_AoIntensity.GetValue(s_I.m_Ao) : -1f;
        public static float AoBaseIntensity => s_I != null ? s_I.m_AoBase0 : -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (s_I != null) return;
            var go = new GameObject("PostFx");
            DontDestroyOnLoad(go);
            s_I = go.AddComponent<PostFx>();
        }

        void Awake()
        {
            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.name = "RockGame PostFx";
            m_Bloom = m_Profile.Add<Bloom>(true);
            m_Tone = m_Profile.Add<Tonemapping>(true);
            m_Color = m_Profile.Add<ColorAdjustments>(true);
            m_Vignette = m_Profile.Add<Vignette>(true);
            m_Dof = m_Profile.Add<DepthOfField>(true);
            m_Grain = m_Profile.Add<FilmGrain>(true);
            m_Chroma = m_Profile.Add<ChromaticAberration>(true);
            // cheap bloom (an integrated GPU): quarter-res start, fewer passes, the plain filter
            m_Bloom.threshold.Override(0.85f);
            m_Bloom.scatter.Override(0.7f);
            m_Bloom.highQualityFiltering.Override(false);
            m_Bloom.downscale.Override(BloomDownscaleMode.Quarter);
            m_Bloom.maxIterations.Override(5);
            m_Bloom.tint.Override(new Color(1f, 0.96f, 0.88f));
            m_Vignette.color.Override(Color.black);
            m_Vignette.smoothness.Override(0.42f);
            m_Vignette.rounded.Override(false);
            m_Vignette.center.Override(new Vector2(0.5f, 0.5f));
            // the cheap depth of field: Gaussian, far blur only (nothing near you goes soft)
            m_Dof.mode.Override(DepthOfFieldMode.Off);
            m_Dof.highQualitySampling.Override(false);
            m_Grain.type.Override(FilmGrainLookup.Thin2);
            m_Grain.response.Override(0.8f);
            m_Grain.intensity.Override(0f);
            m_Chroma.intensity.Override(0f);
            var go = new GameObject("PostFx Volume");
            go.transform.SetParent(transform, false);
            m_Volume = go.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = 100f;
            m_Volume.weight = 1f;
            m_Volume.sharedProfile = m_Profile;
            var sh = Resources.Load<Shader>("PostFx/Stylize");
            if (sh != null && sh.isSupported)
            {
                m_StylizeMat = new Material(sh) { name = "RockGame Stylize", hideFlags = HideFlags.DontSave };
                var ms = Resources.Load<Shader>("PostFx/HandMask");
                if (ms != null && ms.isSupported)
                {
                    m_MaskMat = new Material(ms) { name = "RockGame hand mask", hideFlags = HideFlags.DontSave };
                    m_MaskMat.SetFloat(k_MaskValue, 1f);
                    m_ToolMaskMat = new Material(ms) { name = "RockGame tool mask", hideFlags = HideFlags.DontSave };
                    m_ToolMaskMat.SetFloat(k_MaskValue, 0.5f);
                }
                m_StylizeMat.SetTexture(k_HandMask, Texture2D.blackTexture);
                m_Pass = new StylizePass(m_StylizeMat, m_MaskMat, m_ToolMaskMat);
            }
            else Debug.LogWarning("[RockGame] PostFx/Stylize shader missing: no outlines, haze, sharpen or cel banding");
            FindAo();
            GameSettings.PostFxChanged += Apply;
            DisplayPref.Changed += Apply; // (HANDS AND TOOLS)
            GameSettings.GraphicsChanged += Apply;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Apply();
        }

        void OnDestroy()
        {
            GameSettings.PostFxChanged -= Apply;
            DisplayPref.Changed -= Apply;
            GameSettings.GraphicsChanged -= Apply;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            SetAo(0f); // (the renderer asset's own SSAO back as it was)
            if (m_Profile != null) Destroy(m_Profile);
            if (m_StylizeMat != null) Destroy(m_StylizeMat);
            if (m_MaskMat != null) Destroy(m_MaskMat);
            if (m_ToolMaskMat != null) Destroy(m_ToolMaskMat);
        }

        /// <summary>The renderer's own SSAO feature (PC_Renderer has one, running gently all the time).</summary>
        void FindAo()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            var list = urp.rendererDataList;
            if (list.Length == 0 || list[0] == null) return;
            foreach (var f in list[0].rendererFeatures)
            {
                if (!(f is ScreenSpaceAmbientOcclusion)) continue;
                var sf = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic);
                var ao = sf?.GetValue(f);
                if (ao == null) return;
                var t = ao.GetType();
                const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                m_AoIntensity = t.GetField("Intensity", any);
                m_AoRadius = t.GetField("Radius", any);
                m_AoDirect = t.GetField("DirectLightingStrength", any);
                if (m_AoIntensity == null || m_AoRadius == null || m_AoDirect == null) return;
                m_Ao = ao;
                m_AoBase0 = (float)m_AoIntensity.GetValue(ao);
                m_AoBase1 = (float)m_AoRadius.GetValue(ao);
                m_AoBase2 = (float)m_AoDirect.GetValue(ao);
                return;
            }
        }

        /// <summary>0: the asset's own SSAO exactly; up to 1: deep, wide contact shadows in creases and under things.</summary>
        void SetAo(float s)
        {
            if (m_Ao == null) return;
            bool off = s <= 0f;
            m_AoIntensity.SetValue(m_Ao, off ? m_AoBase0 : Mathf.Max(m_AoBase0, 0.9f + s * 2.2f));
            m_AoRadius.SetValue(m_Ao, off ? m_AoBase1 : Mathf.Max(m_AoBase1, 0.35f + s * 0.45f));
            m_AoDirect.SetValue(m_Ao, off ? m_AoBase2 : Mathf.Max(m_AoBase2, 0.35f + s * 0.4f));
        }

        // the main camera can come and go (scene rebuilds): keep its post processing flag in step
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != m_Cam) { m_Cam = cam; ApplyCamera(); }
            // the main menu cutscene has its own post processing (LayerLooks.cs): switch over as it comes and goes
            int menu = GameSettings.MenuPostNow ? 1 : GameSettings.LobbyPostNow ? 2 : 0; // (and the ship lobby too)
            if (menu != m_MenuPost) { m_MenuPost = menu; Apply(); }
            if (m_StylizeOn) UpdateStylize();
            // the hands on their layer and what they hold on another while either has looks of its own (what's held
            // changes: every frame)
            var vm = ViewModel.Last;
            if (HandsOwnLook && vm != null && vm.Root != null)
            {
                vm.Root.GetComponentsInChildren(true, s_VmRends);
                foreach (var r in s_VmRends)
                {
                    int want = vm.IsHand(r) ? HandLayer : ToolLayer;
                    if (r.gameObject.layer != want) r.gameObject.layer = want;
                }
            }
        }

        /// <summary>The hands or what they hold have looks of their own right now (HANDS / TOOLS & WEAPONS, while post
        /// processing is on): the mask is drawn.</summary>
        public static bool HandsOwnLook => s_I != null && s_I.m_MaskMat != null && Active && (GameSettings.HandsOwn.Value || GameSettings.ToolsOwn.Value);

        void ApplyCamera()
        {
            if (m_Cam == null) return;
            var data = m_Cam.GetUniversalAdditionalCameraData();
            bool on = Active;
            if (data.renderPostProcessing != on) data.renderPostProcessing = on;
        }

        /// <summary>Our full-screen pass goes in for the main camera only, and only while one of its looks is on.</summary>
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!m_StylizeOn || m_Pass == null || cam == null || cam != m_Cam || cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(m_Pass);
        }

        static readonly int k_Outline = Shader.PropertyToID("_RgOutline"), k_Haze = Shader.PropertyToID("_RgHaze"),
                            k_HazeColor = Shader.PropertyToID("_RgHazeColor"), k_Look = Shader.PropertyToID("_RgLook"),
                            k_FarLine = Shader.PropertyToID("_RgFarLine"), k_Hand = Shader.PropertyToID("_RgHand"),
                            k_HandOutline = Shader.PropertyToID("_RgHandOutline"), k_HandMask = Shader.PropertyToID("_RgHandMask"),
                            k_Tool = Shader.PropertyToID("_RgTool"), k_ToolOutline = Shader.PropertyToID("_RgToolOutline"),
                            k_MaskValue = Shader.PropertyToID("_RgMaskValue");

        /// <summary>
        /// How thick the world outlines are at 1440p (pixels) for an Outlines strength. Exactly what 1440p showed before
        /// they were scaled with the screen: 1.333 x (0.8 + 0.7 strength) px, which the shader's pixel-by-pixel depth
        /// reads rounded to whole pixels (1 or 2).
        /// </summary>
        public static float OutlinePxAt1440(float strength) => Mathf.Max(1f, Mathf.Round(1440f / 1080f * (0.8f + strength * 0.7f)));

        /// <summary>The full-screen pass's numbers (the haze follows the sky colour; there's none in space).</summary>
        void UpdateStylize()
        {
            float o = GameSettings.ExtraNow(GameSettings.PostExtra.Outlines);
            float h = GameSettings.ExtraNow(GameSettings.PostExtra.Haze);
            float sh = GameSettings.ExtraNow(GameSettings.PostExtra.Sharpen);
            float cel = GameSettings.ExtraNow(GameSettings.PostExtra.CelBanding);
            if (h > 0f && m_Cam != null && (SpaceArena.NearArena(m_Cam.transform.position) || MenuSpace.Showing)) h = 0f; // (no haze in space)
            // outlines: how dark (0..1), how thick (pixels), the depth step (relative) and the fold (normals) that count.
            // The thickness is set at 1440p (the reference: OutlinePxAt1440) and scaled with the screen height, so the
            // lines are the same share of the screen at any resolution (the shader blends between whole pixels).
            float px = GameSettings.ScreenPx(OutlinePxAt1440(o));
            m_StylizeMat.SetVector(k_Outline, o > 0f ? new Vector4(0.5f + o * 0.5f, px, 0.11f - o * 0.04f, 0.55f) : Vector4.zero);
            // the far things' lines (clouds, planets, far mountains: Settings > Display > Far line thickness): x their
            // thickness as a share of the usual (1 = the usual look), y how much their silhouettes against the sky keep a
            // line past the usual distance fade (0 up to 100%, all of it by 200%)
            float far = GameSettings.FarLineThickness.Value;
            m_StylizeMat.SetVector(k_FarLine, new Vector4(far, Mathf.Clamp01(far - 1f), 0f, 0f));
            // haze: how much at most, where it starts and how far until it's all there (m)
            m_StylizeMat.SetVector(k_Haze, h > 0f ? new Vector4(0.12f + h * 0.4f, 35f, Mathf.Lerp(520f, 200f, h), 0f) : Vector4.zero);
            var sky = ColorSlots.Sky.Value;
            m_StylizeMat.SetColor(k_HazeColor, Color.Lerp(sky, Color.white, 0.4f).linear);
            // sharpen amount; cel banding: how many brightness steps (fewer = stronger)
            m_StylizeMat.SetVector(k_Look, new Vector4(sh > 0f ? 0.15f + sh * 0.6f : 0f, cel > 0f ? Mathf.Round(Mathf.Lerp(14f, 4f, cel)) : 0f, 0f, 0f));
            // the hands and tools: their own outlines (the same make-up as the world's), cel steps, saturation and contrast
            // (and what they hold: TOOLS & WEAPONS - the same make-up, its own numbers). Each only while its own look is on.
            // The outline's darkness (x) times its darkness setting: past ~1.3 the shader's saturate lets it reach black.
            bool own = HandsOwnLook;
            SetLayerLook(k_Hand, k_HandOutline, own && GameSettings.HandsOwn.Value, GameSettings.HandsOutline, GameSettings.HandsOutlineStrength, GameSettings.HandsOutlineDark,
                GameSettings.HandsCel, GameSettings.HandsCelStrength, GameSettings.HandsSaturation, GameSettings.HandsContrast);
            SetLayerLook(k_Tool, k_ToolOutline, own && GameSettings.ToolsOwn.Value, GameSettings.ToolsOutline, GameSettings.ToolsOutlineStrength, GameSettings.ToolsOutlineDark,
                GameSettings.ToolsCel, GameSettings.ToolsCelStrength, GameSettings.ToolsSaturation, GameSettings.ToolsContrast);
        }

        void SetLayerLook(int look, int outline, bool on, DisplayPref.Bool olOn, DisplayPref.Float olStrength, DisplayPref.Float olDark,
            DisplayPref.Bool celOn, DisplayPref.Float celStrength, DisplayPref.Float saturation, DisplayPref.Float contrast)
        {
            if (!on) { m_StylizeMat.SetVector(look, Vector4.zero); m_StylizeMat.SetVector(outline, Vector4.zero); return; }
            float o = olOn.Value ? olStrength.Value : 0f;
            float cel = celOn.Value ? Mathf.Round(Mathf.Lerp(14f, 4f, celStrength.Value)) : 0f;
            m_StylizeMat.SetVector(outline, o > 0f ? new Vector4((0.5f + o * 0.5f) * olDark.Value, GameSettings.ScreenPx(OutlinePxAt1440(o)), 0.11f - o * 0.04f, 0.55f) : Vector4.zero);
            m_StylizeMat.SetVector(look, new Vector4(1f, cel, saturation.Value, contrast.Value));
        }

        /// <summary>(tests) the numbers the Stylize pass has for the hands and for the tools (x 0: that one's own look is off).</summary>
        public static Vector4 HandLookNow => s_I != null && s_I.m_StylizeMat != null ? s_I.m_StylizeMat.GetVector(k_Hand) : Vector4.zero;
        public static Vector4 ToolLookNow => s_I != null && s_I.m_StylizeMat != null ? s_I.m_StylizeMat.GetVector(k_Tool) : Vector4.zero;
        public static Vector4 ToolOutlineNow => s_I != null && s_I.m_StylizeMat != null ? s_I.m_StylizeMat.GetVector(k_ToolOutline) : Vector4.zero;
        public static Vector4 HandOutlineNow => s_I != null && s_I.m_StylizeMat != null ? s_I.m_StylizeMat.GetVector(k_HandOutline) : Vector4.zero;

        void Apply()
        {
            if (this == null) return;
            bool on = Active;
            float b = GameSettings.BloomNow; // (the main menu cutscene's own while it's on: LayerLooks.cs)
            float v = GameSettings.VignetteNow;
            float g = GameSettings.GradingNow;
            // 0.5 = the default look: subtle bloom, a light vignette, a little more colour and contrast. No tone
            // mapping curve: it shifted the bright, saturated colours (the golden wheat turned lemon yellow), so the
            // grading keeps the palette's hues and only adds colour and contrast (the URP asset's own tone mapping is
            // switched off here too).
            m_Bloom.intensity.Override(b * 1.6f);
            m_Vignette.intensity.Override(v * 0.42f);
            m_Tone.mode.Override(TonemappingMode.None);
            m_Color.postExposure.Override(0f);
            m_Color.contrast.Override(g * 20f);
            m_Color.saturation.Override(g * 30f);
            m_Color.hueShift.Override(0f);
            m_Color.colorFilter.Override(Color.white);
            // the extra looks (all 0 / off unless switched on)
            float dof = on ? GameSettings.ExtraNow(GameSettings.PostExtra.DepthOfField) : 0f;
            m_Dof.mode.Override(dof > 0f ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Off);
            m_Dof.gaussianStart.Override(Mathf.Lerp(70f, 22f, dof));
            m_Dof.gaussianEnd.Override(Mathf.Lerp(260f, 90f, dof));
            m_Dof.gaussianMaxRadius.Override(Mathf.Lerp(0.5f, 1f, dof));
            m_Grain.intensity.Override(GameSettings.ExtraNow(GameSettings.PostExtra.FilmGrain) * 0.55f);
            m_Chroma.intensity.Override(GameSettings.ExtraNow(GameSettings.PostExtra.Chromatic) * 0.3f);
            SetAo(on ? GameSettings.ExtraNow(GameSettings.PostExtra.AmbientOcclusion) : 0f);
            m_StylizeOn = on && m_Pass != null && (GameSettings.ExtraNow(GameSettings.PostExtra.Outlines) > 0f || GameSettings.ExtraNow(GameSettings.PostExtra.Haze) > 0f
                || GameSettings.ExtraNow(GameSettings.PostExtra.Sharpen) > 0f || GameSettings.ExtraNow(GameSettings.PostExtra.CelBanding) > 0f || (m_MaskMat != null && (GameSettings.HandsOwn.Value || GameSettings.ToolsOwn.Value)));
            m_Volume.enabled = on;
            m_Cam = Camera.main;
            ApplyCamera();
            if (m_StylizeOn) UpdateStylize();
        }

        /// <summary>
        /// Outlines, distance haze, sharpen and cel banding in one full-screen pass (render graph), before URP's post
        /// processing: reads the camera colour, depth and normals (the normals are already drawn for the renderer's
        /// SSAO), writes a new colour target and hands it on as the camera colour (no extra copy).
        /// </summary>
        class StylizePass : ScriptableRenderPass
        {
            readonly Material m_Mat, m_MaskMat, m_ToolMaskMat;
            static readonly MaterialPropertyBlock s_Props = new MaterialPropertyBlock();
            static readonly int k_BlitTex = Shader.PropertyToID("_BlitTexture"), k_BlitScale = Shader.PropertyToID("_BlitScaleBias"),
                                k_Mask = Shader.PropertyToID("_RgHandMask");
            static readonly List<ShaderTagId> s_Tags = new List<ShaderTagId> { new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit") };

            class PassData { public Material Mat; public TextureHandle Src, Mask; public bool HasMask; }
            class MaskData { public RendererListHandle Tools, Hands; }

            public StylizePass(Material mat, Material maskMat, Material toolMaskMat)
            {
                m_Mat = mat;
                m_MaskMat = maskMat;
                m_ToolMaskMat = toolMaskMat;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                profilingSampler = new ProfilingSampler("RockGame Stylize");
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer || !res.cameraColor.IsValid()) return;
                var desc = renderGraph.GetTextureDesc(res.activeColorTexture);
                // HANDS and TOOLS & WEAPONS: the first-person hands (their layer) drawn into a mask as 1, what they hold
                // (its layer) as 0.5 - with a depth buffer of the mask's own, so where they overlap the nearer one wins
                var mask = TextureHandle.nullHandle;
                if (HandsOwnLook && m_MaskMat != null && m_ToolMaskMat != null)
                {
                    var rd = frameData.Get<UniversalRenderingData>();
                    var cd = frameData.Get<UniversalCameraData>();
                    var ld = frameData.Get<UniversalLightData>();
                    var md = new TextureDesc(desc.width, desc.height)
                    {
                        name = "_RockGameHandMask", colorFormat = GraphicsFormat.R8_UNorm, clearBuffer = true, clearColor = Color.clear, msaaSamples = MSAASamples.None,
                    };
                    mask = renderGraph.CreateTexture(md);
                    var dd = new TextureDesc(desc.width, desc.height)
                    {
                        name = "_RockGameHandMaskDepth", depthBufferBits = DepthBits.Depth32, clearBuffer = true, msaaSamples = MSAASamples.None,
                    };
                    var depth = renderGraph.CreateTexture(dd);
                    var hs = RenderingUtils.CreateDrawingSettings(s_Tags, rd, cd, ld, SortingCriteria.CommonOpaque);
                    hs.overrideMaterial = m_MaskMat;
                    hs.overrideMaterialPassIndex = 0;
                    var ts = RenderingUtils.CreateDrawingSettings(s_Tags, rd, cd, ld, SortingCriteria.CommonOpaque);
                    ts.overrideMaterial = m_ToolMaskMat;
                    ts.overrideMaterialPassIndex = 0;
                    var hands = renderGraph.CreateRendererList(new RendererListParams(rd.cullResults, hs, new FilteringSettings(RenderQueueRange.all, 1 << HandLayer)));
                    var tools = renderGraph.CreateRendererList(new RendererListParams(rd.cullResults, ts, new FilteringSettings(RenderQueueRange.all, 1 << ToolLayer)));
                    using (var mb = renderGraph.AddRasterRenderPass<MaskData>("RockGame Hand Mask", out var mdata, profilingSampler))
                    {
                        mdata.Hands = hands;
                        mdata.Tools = tools;
                        mb.UseRendererList(hands);
                        mb.UseRendererList(tools);
                        mb.SetRenderAttachment(mask, 0, AccessFlags.Write);
                        mb.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                        mb.SetRenderFunc((MaskData d, RasterGraphContext ctx) => { ctx.cmd.DrawRendererList(d.Tools); ctx.cmd.DrawRendererList(d.Hands); });
                    }
                }
                desc.name = "_RockGameStylize";
                desc.clearBuffer = false;
                desc.msaaSamples = MSAASamples.None;
                var dst = renderGraph.CreateTexture(desc);
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("RockGame Stylize", out var data, profilingSampler))
                {
                    data.Mat = m_Mat;
                    data.Src = res.activeColorTexture;
                    data.HasMask = mask.IsValid();
                    data.Mask = mask;
                    builder.UseTexture(data.Src, AccessFlags.Read);
                    if (data.HasMask) builder.UseTexture(mask, AccessFlags.Read);
                    if (res.cameraDepthTexture.IsValid()) builder.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                    if (res.cameraNormalsTexture.IsValid()) builder.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        s_Props.Clear();
                        s_Props.SetTexture(k_BlitTex, d.Src);
                        s_Props.SetVector(k_BlitScale, new Vector4(1, 1, 0, 0));
                        if (d.HasMask) s_Props.SetTexture(k_Mask, d.Mask);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.Mat, 0, MeshTopology.Triangles, 3, 1, s_Props);
                    });
                }
                res.cameraColor = dst;
            }
        }
    }
}
