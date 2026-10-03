using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > FPS counter (off to start with, just on this PC, saved).</summary>
    public static partial class GameSettings
    {
        static int s_ShowFps = -1;

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
    }

    /// <summary>The FPS counter: frames per second and the frame time, averaged over half a second (so it's readable),
    /// in green / yellow / red. In a match it sits just under YOU ARE ..., on the menus in the top left corner.</summary>
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
            var r = new Rect(10, inGame ? 10 + 34 * k : 10, 150 * k, 22 * k);
            string col = m_FpsShownFps >= 55f ? "#8dff8d" : m_FpsShownFps >= 30f ? "#ffd24a" : "#ff6a5a";
            Fill(r, new Color(0f, 0f, 0f, 0.45f));
            Shadowed(new Rect(r.x + 6 * k, r.y + 1, r.width, r.height), $"<color={col}><b>{m_FpsShownFps:0} FPS</b></color>  <color=#cccccc>{m_FpsShownMs:0.0} ms</color>", m_Small);
        }
    }
}
