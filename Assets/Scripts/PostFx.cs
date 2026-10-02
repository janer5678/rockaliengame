using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RockGame
{
    /// <summary>Settings > Display > POST PROCESSING (Normal graphics, just on this PC): a master switch plus bloom,
    /// vignette and colour grading, each with its own strength. Saved in PlayerPrefs; changes apply live.</summary>
    public static partial class GameSettings
    {
        static bool s_PostLoaded;
        static bool s_Post = true, s_PostBloom = true, s_PostVignette = true, s_PostGrading = true;
        static float s_PostBloomStr = 0.5f, s_PostVignetteStr = 0.5f, s_PostGradingStr = 0.5f;

        /// <summary>Fired when any post processing setting changes.</summary>
        public static event System.Action PostFxChanged;

        static void LoadPost()
        {
            if (s_PostLoaded) return;
            s_PostLoaded = true;
            s_Post = PlayerPrefs.GetInt("RockGame.PostFx", 1) == 1;
            s_PostBloom = PlayerPrefs.GetInt("RockGame.PostBloom", 1) == 1;
            s_PostVignette = PlayerPrefs.GetInt("RockGame.PostVignette", 1) == 1;
            s_PostGrading = PlayerPrefs.GetInt("RockGame.PostGrading", 1) == 1;
            s_PostBloomStr = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.PostBloomStr", 0.5f));
            s_PostVignetteStr = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.PostVignetteStr", 0.5f));
            s_PostGradingStr = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.PostGradingStr", 0.5f));
        }

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

        /// <summary>Post processing back to the defaults (all on, middle strengths).</summary>
        public static void ResetPostFx(bool save = true) => SetPostFx(true, true, true, true, 0.5f, 0.5f, 0.5f, save);
    }

    /// <summary>
    /// The Normal look's post processing: one global URP Volume with a profile made here (bloom, colour adjustments,
    /// vignette), and the main camera's post processing switched on while it's wanted. Off (or in
    /// PSX / AI PSX) the camera renders no post processing at all, so the look is exactly what it was without it.
    /// Every parameter the URP asset's own default profile sets (bloom, tone mapping, vignette) is overridden here, so
    /// an effect switched off really is off. (The bloom shader variants are kept in the build because
    /// the profiles in Assets/Settings use them - URP strips the post variants no profile asset uses.)
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
        Camera m_Cam;

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
            var go = new GameObject("PostFx Volume");
            go.transform.SetParent(transform, false);
            m_Volume = go.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = 100f;
            m_Volume.weight = 1f;
            m_Volume.sharedProfile = m_Profile;
            GameSettings.PostFxChanged += Apply;
            GameSettings.GraphicsChanged += Apply;
            Apply();
        }

        void OnDestroy()
        {
            GameSettings.PostFxChanged -= Apply;
            GameSettings.GraphicsChanged -= Apply;
            if (m_Profile != null) Destroy(m_Profile);
        }

        // the main camera can come and go (scene rebuilds): keep its post processing flag in step
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != m_Cam) { m_Cam = cam; ApplyCamera(); }
        }

        void ApplyCamera()
        {
            if (m_Cam == null) return;
            var data = m_Cam.GetUniversalAdditionalCameraData();
            bool on = Active;
            if (data.renderPostProcessing != on) data.renderPostProcessing = on;
        }

        void Apply()
        {
            if (this == null) return;
            bool on = Active;
            float b = GameSettings.PostBloom ? GameSettings.PostBloomStrength : 0f;
            float v = GameSettings.PostVignette ? GameSettings.PostVignetteStrength : 0f;
            float g = GameSettings.PostGrading ? GameSettings.PostGradingStrength : 0f;
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
            m_Volume.enabled = on;
            m_Cam = Camera.main;
            ApplyCamera();
        }
    }
}
