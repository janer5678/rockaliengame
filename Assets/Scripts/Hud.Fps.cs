using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > FPS counter (just on this PC, saved), and the uncapped framerate switch.</summary>
    public static partial class GameSettings
    {
        static int s_ShowFps = -1, s_RoundHands = -1;

        /// <summary>Settings > Display > Round hands (on to start with): the first-person hands are round instead of the old
        /// square ones. Off brings the old hands straight back.</summary>
        public static bool RoundHands
        {
            get { if (s_RoundHands < 0) s_RoundHands = PlayerPrefs.GetInt("RockGame.RoundHands", 1); return s_RoundHands == 1; }
        }
        public static event System.Action HandStyleChanged;

        public static void SetRoundHands(bool on, bool save = true)
        {
            s_RoundHands = on ? 1 : 0;
            if (save) { PlayerPrefs.SetInt("RockGame.RoundHands", s_RoundHands); PlayerPrefs.Save(); }
            HandStyleChanged?.Invoke();
        }

        /// <summary>Show the frames-per-second counter in the top left corner.</summary>
        public static bool ShowFps
        {
            get { if (s_ShowFps < 0) s_ShowFps = PlayerPrefs.GetInt("RockGame.ShowFps", DisplayDefaults.ShowFps ? 1 : 0); return s_ShowFps == 1; }
        }

        public static void SetShowFps(bool on, bool save = true)
        {
            s_ShowFps = on ? 1 : 0;
            if (save) { PlayerPrefs.SetInt("RockGame.ShowFps", s_ShowFps); PlayerPrefs.Save(); }
        }

        // ---- Settings > Display > Uncapped framerate (fps.uncapped; off to start with, just on this PC) ----
        static int s_Uncapped = -1, s_VsyncBefore = -1;

        /// <summary>Run as fast as the PC can: vsync off and no frame rate cap. Off: capped at the screen's refresh rate.</summary>
        public static bool UncappedFps
        {
            get { if (s_Uncapped < 0) s_Uncapped = PlayerPrefs.GetInt("RockGame.UncappedFps", DisplayDefaults.UncappedFps ? 1 : 0); return s_Uncapped == 1; }
        }

        public static void SetUncappedFps(bool on, bool save = true)
        {
            s_Uncapped = on ? 1 : 0;
            if (save) { PlayerPrefs.SetInt("RockGame.UncappedFps", s_Uncapped); PlayerPrefs.Save(); }
            ApplyFrameCap();
        }

        /// <summary>The frame rate cap as the settings say: uncapped (vsync off, targetFrameRate -1), or the refresh rate
        /// we run at (at least 60) with vsync as the project had it. Called at startup, on a display change and on the toggle.</summary>
        public static void ApplyFrameCap()
        {
            if (UncappedFps)
            {
                if (s_VsyncBefore < 0) s_VsyncBefore = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                return;
            }
            if (s_VsyncBefore >= 0) { QualitySettings.vSyncCount = s_VsyncBefore; s_VsyncBefore = -1; }
            double hz = ChosenRate.denominator > 0 ? ChosenRate.value : 60.0;
            Application.targetFrameRate = Mathf.Max(60, Mathf.RoundToInt((float)hz));
        }
    }

    /// <summary>The FPS counter: frames per second and the frame time, averaged over half a second (so it's readable),
    /// in green / yellow / red. In a match it sits under the base radar (under YOU ARE ...), on the menus in the top left corner.</summary>
    public partial class Hud
    {
        float m_FpsTime, m_FpsShownFps, m_FpsShownMs;
        int m_FpsFrames;

        /// <summary>(tests) the frames per second the counter shows right now (0 until it has measured).</summary>
        public static float FpsShown => s_I != null ? s_I.m_FpsShownFps : 0f;

        void CountFps()
        {
            m_FpsFrames++;
            m_FpsTime += Time.unscaledDeltaTime;
            if (m_FpsTime < 0.5f) return;
            m_FpsShownFps = m_FpsFrames / m_FpsTime;
            m_FpsShownMs = m_FpsTime * 1000f / m_FpsFrames;
            m_FpsFrames = 0;
            m_FpsTime = 0f;
        }

        void DrawFps(bool inGame)
        {
            if (!GameSettings.ShowFps || m_FpsShownFps <= 0f) return;
            float k = m_Scale;
            // in a match: under the base radar when that's up (else straight under YOU ARE ...)
            float y = !inGame ? 10 : m_RadarBottom > 0f ? m_RadarBottom + 4 * k : 10 + 34 * k;
            var r = new Rect(10, y, 150 * k, 22 * k);
            string col = m_FpsShownFps >= 55f ? "#8dff8d" : m_FpsShownFps >= 30f ? "#ffd24a" : "#ff6a5a";
            Fill(r, new Color(0f, 0f, 0f, 0.45f));
            Shadowed(new Rect(r.x + 6 * k, r.y + 1, r.width, r.height), $"<color={col}><b>{m_FpsShownFps:0} FPS</b></color>  <color=#cccccc>{m_FpsShownMs:0.0} ms</color>", m_Small);
        }
    }
}
