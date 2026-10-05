using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > TREE X (Normal graphics, just on this PC, saved in PlayerPrefs like the other display
    /// settings): how bright the glowing weak spot X on trees and fallen logs is, how big the soft glow round it is,
    /// its colour and its size. ResourceNode applies them (TreeXChanged) to the shared glow material and halo mesh.
    /// </summary>
    public static partial class GameSettings
    {
        /// <summary>Glow amount: the bars' HDR brightness (over 1 blooms with post processing on); the halo follows it.</summary>
        public const float TreeXGlowMin = 0.3f, TreeXGlowMax = 4f, TreeXGlowDefault = 2.85f;
        /// <summary>The glow amount that counts as 100% (the slider's label, and ResourceNode's halo scale): the old default,
        /// kept as the yardstick so the default moving up didn't change how any value looks.</summary>
        public const float TreeXGlowBase = 1.6f;
        /// <summary>Halo size: times the default halo (which is already bigger than it was; 0 = no halo).</summary>
        public const float TreeXHaloMin = 0f, TreeXHaloMax = 2.5f, TreeXHaloDefault = 1f;
        /// <summary>The X's size: times the default (the X moves out off the bark to stay clear of it).</summary>
        public const float TreeXSizeMin = 0.6f, TreeXSizeMax = 1.8f, TreeXSizeDefault = 1f;

        public static readonly Color[] TreeXColours =
        {
            new Color(1f, 0.42f, 0.08f), new Color(1f, 0.16f, 0.1f), new Color(1f, 0.85f, 0.15f), new Color(1f, 1f, 1f),
            new Color(0.2f, 0.9f, 1f), new Color(0.35f, 1f, 0.3f), new Color(1f, 0.3f, 0.85f),
        };
        public static readonly string[] TreeXColourNames = { "Orange", "Red", "Yellow", "White", "Cyan", "Green", "Pink" };

        static bool s_XLoaded;
        static float s_XGlow = TreeXGlowDefault, s_XHalo = TreeXHaloDefault, s_XSize = TreeXSizeDefault;
        static Color s_XColour = new Color(1f, 0.42f, 0.08f);

        /// <summary>Fired when any of the tree X settings change.</summary>
        public static event System.Action TreeXChanged;

        static void LoadTreeX()
        {
            if (s_XLoaded) return;
            s_XLoaded = true;
            s_XGlow = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.TreeXGlow", TreeXGlowDefault), TreeXGlowMin, TreeXGlowMax);
            s_XHalo = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.TreeXHalo", TreeXHaloDefault), TreeXHaloMin, TreeXHaloMax);
            s_XSize = Mathf.Clamp(PlayerPrefs.GetFloat("RockGame.TreeXSize", TreeXSizeDefault), TreeXSizeMin, TreeXSizeMax);
            s_XColour = ColorUtility.TryParseHtmlString("#" + PlayerPrefs.GetString("RockGame.TreeXColour", ColorUtility.ToHtmlStringRGB(TreeXColours[0])), out var c) ? c : TreeXColours[0];
            s_XColour.a = 1f;
        }

        public static float TreeXGlow { get { LoadTreeX(); return s_XGlow; } }
        public static float TreeXHalo { get { LoadTreeX(); return s_XHalo; } }
        public static float TreeXSize { get { LoadTreeX(); return s_XSize; } }
        public static Color TreeXColour { get { LoadTreeX(); return s_XColour; } }
        public static bool TreeXIsDefault => Mathf.Approximately(TreeXGlow, TreeXGlowDefault) && Mathf.Approximately(TreeXHalo, TreeXHaloDefault)
            && Mathf.Approximately(TreeXSize, TreeXSizeDefault) && ColorUtility.ToHtmlStringRGB(TreeXColour) == ColorUtility.ToHtmlStringRGB(TreeXColours[0]);

        public static void SetTreeX(float glow, float halo, float size, Color colour, bool save = true)
        {
            LoadTreeX();
            glow = Mathf.Clamp(glow, TreeXGlowMin, TreeXGlowMax);
            halo = Mathf.Clamp(halo, TreeXHaloMin, TreeXHaloMax);
            size = Mathf.Clamp(size, TreeXSizeMin, TreeXSizeMax);
            colour.a = 1f;
            if (Mathf.Approximately(glow, s_XGlow) && Mathf.Approximately(halo, s_XHalo) && Mathf.Approximately(size, s_XSize) && colour == s_XColour) return;
            s_XGlow = glow; s_XHalo = halo; s_XSize = size; s_XColour = colour;
            if (save)
            {
                PlayerPrefs.SetFloat("RockGame.TreeXGlow", glow);
                PlayerPrefs.SetFloat("RockGame.TreeXHalo", halo);
                PlayerPrefs.SetFloat("RockGame.TreeXSize", size);
                PlayerPrefs.SetString("RockGame.TreeXColour", ColorUtility.ToHtmlStringRGB(colour));
                PlayerPrefs.Save();
            }
            TreeXChanged?.Invoke();
        }

        public static void ResetTreeX(bool save = true)
        {
            SetTreeX(TreeXGlowDefault, TreeXHaloDefault, TreeXSizeDefault, TreeXColours[0], false);
            if (save)
            {
                foreach (var key in new[] { "RockGame.TreeXGlow", "RockGame.TreeXHalo", "RockGame.TreeXSize", "RockGame.TreeXColour" }) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }
    }

    /// <summary>Settings > Display: the compact TREE X section (glow, halo size, X size, colour).</summary>
    public partial class Hud
    {
        void DrawTreeXSettings()
        {
            float k = m_Scale;
            Caption("TREE X  ·  Normal graphics, just on this PC");
            float glow = SliderRow("X glow", GameSettings.TreeXGlow, GameSettings.TreeXGlowMin, GameSettings.TreeXGlowMax, $"{GameSettings.TreeXGlow / GameSettings.TreeXGlowBase * 100f:0}%", 150 * k);
            float halo = SliderRow("X glow size", GameSettings.TreeXHalo, GameSettings.TreeXHaloMin, GameSettings.TreeXHaloMax, GameSettings.TreeXHalo < 0.025f ? "off" : $"{GameSettings.TreeXHalo * 100f:0}%", 150 * k);
            float size = SliderRow("X size", GameSettings.TreeXSize, GameSettings.TreeXSizeMin, GameSettings.TreeXSizeMax, $"{GameSettings.TreeXSize * 100f:0}%", 150 * k);
            var colour = GameSettings.TreeXColour;
            GUILayout.BeginHorizontal();
            RowLabel("X colour", 150 * k);
            string cur = ColorUtility.ToHtmlStringRGB(colour);
            for (int i = 0; i < GameSettings.TreeXColours.Length; i++)
            {
                string hex = ColorUtility.ToHtmlStringRGB(GameSettings.TreeXColours[i]);
                if (Choice(hex == cur, $"<color=#{hex}>■</color>", GUILayout.Width(34 * k), GUILayout.Height(28 * k))) colour = GameSettings.TreeXColours[i];
            }
            GUILayout.FlexibleSpace();
            bool reset = Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k));
            GUILayout.EndHorizontal();
            if (reset) { GameSettings.ResetTreeX(); return; }
            GameSettings.SetTreeX(Mathf.Round(glow * 20f) / 20f, Mathf.Round(halo * 20f) / 20f, Mathf.Round(size * 20f) / 20f, colour);
        }
    }
}
