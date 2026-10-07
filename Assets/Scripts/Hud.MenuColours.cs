using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The main menu cutscene's colours (the UFO in space: MenuSpace.cs), set in Settings > Display > MAIN MENU
    /// CUTSCENE: space behind it all, the stars and speed lines, the nebula, the planet and its ring, the UFO's hull and
    /// dome, its thruster flames, the meteor fire round its front and sides, and the two lights on it. Each is a
    /// DisplayPref.Colour (saved, and in the display settings code on its own) and shows the moment it's picked.
    /// The layered ones - the nebula, the hull, the thrusters, the fire - are turned to the picked colour as a whole, so
    /// a flame keeps its white-hot core and fading outer layers in the new colour (MenuSpace: the reference colours below).
    /// </summary>
    public static class MenuLooks
    {
        public const string Group = "MAIN MENU CUTSCENE";
        // (what the layered parts were built round: picking exactly this leaves them as built)
        public static readonly Color HullRef = new Color(0.55f, 0.58f, 0.64f);
        public static readonly Color NebulaRef = new Color(0.9f, 0.2f, 0.8f);
        public static readonly Color ThrusterRef = new Color(0.35f, 0.75f, 1f);
        public static readonly Color FireRef = new Color(1f, 0.55f, 0.12f);
    }

    public static partial class GameSettings
    {
        public static readonly DisplayPref.Colour MenuSpaceColour = new("menu.colour.space", MenuLooks.Group, new Color(0.01f, 0.005f, 0.03f));
        public static readonly DisplayPref.Colour MenuStarsColour = new("menu.colour.stars", MenuLooks.Group, Color.white);
        public static readonly DisplayPref.Colour MenuLinesColour = new("menu.colour.speedlines", MenuLooks.Group, new Color(0.75f, 0.9f, 1f));
        public static readonly DisplayPref.Colour MenuNebulaColour = new("menu.colour.nebula", MenuLooks.Group, MenuLooks.NebulaRef);
        public static readonly DisplayPref.Colour MenuPlanetColour = new("menu.colour.planet", MenuLooks.Group, new Color(0.55f, 0.35f, 0.75f));
        public static readonly DisplayPref.Colour MenuRingColour = new("menu.colour.ring", MenuLooks.Group, new Color(1f, 0.8f, 0.55f));
        public static readonly DisplayPref.Colour MenuHullColour = new("menu.colour.hull", MenuLooks.Group, MenuLooks.HullRef);
        public static readonly DisplayPref.Colour MenuDomeColour = new("menu.colour.dome", MenuLooks.Group, DisplayDefaults.Hex("#3892B0"));
        public static readonly DisplayPref.Colour MenuThrusterColour = new("menu.colour.thruster", MenuLooks.Group, MenuLooks.ThrusterRef);
        public static readonly DisplayPref.Colour MenuFireColour = new("menu.colour.fire", MenuLooks.Group, MenuLooks.FireRef);
        public static readonly DisplayPref.Colour MenuKeyLightColour = new("menu.colour.keylight", MenuLooks.Group, new Color(1f, 0.88f, 0.75f));
        public static readonly DisplayPref.Colour MenuRimLightColour = new("menu.colour.rimlight", MenuLooks.Group, new Color(0.45f, 0.7f, 1f));

        /// <summary>Every main menu cutscene colour, with its name in the settings (and whether its swatches are dark ones).</summary>
        public static readonly (string name, DisplayPref.Colour pref, bool dark)[] MenuColours =
        {
            ("Space", MenuSpaceColour, true), ("Stars", MenuStarsColour, false), ("Speed lines", MenuLinesColour, false),
            ("Nebula", MenuNebulaColour, false), ("Planet", MenuPlanetColour, false), ("Planet ring", MenuRingColour, false),
            ("UFO hull", MenuHullColour, false), ("UFO dome", MenuDomeColour, false), ("Thruster flames", MenuThrusterColour, false),
            ("Meteor fire", MenuFireColour, false), ("Key light", MenuKeyLightColour, false), ("Rim light", MenuRimLightColour, false),
        };

        /// <summary>The cutscene's colours back as built.</summary>
        public static void ResetMenuColours(bool save = true)
        {
            foreach (var c in MenuColours) c.pref.Set(c.pref.Default, save);
        }
    }

    public partial class Hud
    {
        static readonly Color[] s_MenuPalette =
        {
            Color.white, Color.black, new Color(0.95f, 0.15f, 0.12f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.9f, 0.2f),
            new Color(0.2f, 0.85f, 0.3f), new Color(0.2f, 0.85f, 1f), new Color(0.2f, 0.35f, 1f), new Color(0.6f, 0.25f, 0.95f), new Color(1f, 0.35f, 0.75f),
        };
        readonly Dictionary<string, string> m_MenuHex = new Dictionary<string, string>();
        GUIStyle m_MenuHexField;

        /// <summary>
        /// Settings > Display > MAIN MENU CUTSCENE: a row per colour of the cutscene - its name, a swatch for how it was
        /// built (outlined in gold while it's in use), a little palette, and its hex code to type - then Defaults. Plain
        /// GUILayout rows, to go anywhere in a settings column. Changes show on the cutscene at once (MenuSpace).
        /// </summary>
        public void DrawMenuCutsceneColours()
        {
            float k = m_Scale, sw = 18 * k, rowH = 26 * k;
            GUILayout.Space(4 * k);
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            GUILayout.Label("<b>COLOURS</b>", m_Label, GUILayout.Height(rowH));
            GUILayout.FlexibleSpace();
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(rowH))) { GameSettings.ResetMenuColours(); m_MenuHex.Clear(); }
            GUILayout.EndHorizontal();
            if (m_MenuHexField == null || m_MenuHexField.fontSize != Mathf.RoundToInt(14 * k))
                m_MenuHexField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(14 * k), alignment = TextAnchor.MiddleCenter };
            if (GUIUtility.keyboardControl == 0) m_MenuHex.Clear();
            foreach (var (name, pref, dark) in GameSettings.MenuColours)
            {
                var cur = pref.Value;
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                GUILayout.Label(name, m_Small, GUILayout.Width(118 * k), GUILayout.Height(rowH));
                // as built first (a gap after it), then the palette
                void Swatch(Color c)
                {
                    var pr = GUILayoutUtility.GetRect(sw, sw, GUILayout.Width(sw), GUILayout.Height(rowH));
                    pr.y += (rowH - sw) * 0.5f; pr.height = sw;
                    Fill(pr, ColorSlots.Same(c, cur) ? new Color(1f, 0.82f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.8f));
                    Fill(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), c);
                    TrackHover(pr);
                    // (double-click: the colour wheel - Hud.ColourWheel.cs)
                    bool wheel = ColourWheelOnDoubleClick(pr, pref, name);
                    bool click = GUI.Button(pr, GUIContent.none, GUIStyle.none); // (always made, so the control ids stay the same)
                    if (wheel) m_MenuHex.Remove(pref.Key);
                    else if (click) { ClickSound(); pref.Set(c); m_MenuHex.Remove(pref.Key); }
                    GUILayout.Space(2 * k);
                }
                Swatch(pref.Default);
                GUILayout.Space(6 * k);
                foreach (var pc in s_MenuPalette) Swatch(dark ? new Color(pc.r * 0.12f, pc.g * 0.12f, pc.b * 0.12f) : pc); // (space: the palette, very dark)
                GUILayout.Space(6 * k);
                string ctl = "menuhex." + pref.Key;
                if (!m_MenuHex.TryGetValue(pref.Key, out var hex)) hex = ColorUtility.ToHtmlStringRGB(cur);
                GUI.SetNextControlName(ctl);
                string nh = GUILayout.TextField(hex, 7, m_MenuHexField, GUILayout.Width(80 * k), GUILayout.Height(rowH - 2 * k));
                NoteTyping(ctl);
                if (nh != hex)
                {
                    m_MenuHex[pref.Key] = nh;
                    var t = nh.Trim().TrimStart('#');
                    if (t.Length == 6 && ColorUtility.TryParseHtmlString("#" + t, out var hc)) pref.Set(hc);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Space(16 * k);
            GUILayout.Label("<color=#bbbbbb>The main menu's UFO cutscene only. The first swatch is each one as it was made; type a hex code (like FF8800) for any other colour, or double-click a square for the colour wheel. The nebula, hull, thrusters and fire keep their layers in the new colour.</color>", m_SmallWrap);
            GUILayout.EndHorizontal();
        }
    }
}
