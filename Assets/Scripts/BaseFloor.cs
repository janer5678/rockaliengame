using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > BASE FLOOR (just on this PC, saved, live): how each team's building grid on its base looks.
    /// While you build it's always the tinted floor with its grid; after the build phase (once the glass wall drops) it
    /// stays the colour grid (the default), turns into flat grass (plain ground) or grass with blades on it. Its floor and
    /// grid colours are the world colour slots "Base floor" / "Base floor grid" (the colour picker, the copy buttons and
    /// Reset all cover them), and Team colour is how much of each team's colour is mixed into both.
    /// </summary>
    public static partial class GameSettings
    {
        public enum BaseFloorStyle { Grid = 0, Grass = 1, GrassBlades = 2 }
        public static readonly string[] BaseFloorStyleNames = { "Colour grid", "Flat grass", "Grass + blades" };
        public const BaseFloorStyle BaseFloorAfterDefault = BaseFloorStyle.Grid;
        public const float BaseFloorTeamMixDefault = 0.5f;

        static bool s_BaseFloorLoaded;
        static BaseFloorStyle s_BaseFloorAfter = BaseFloorAfterDefault;
        static float s_BaseFloorTeamMix = BaseFloorTeamMixDefault;

        static void LoadBaseFloor()
        {
            if (s_BaseFloorLoaded) return;
            s_BaseFloorLoaded = true;
            s_BaseFloorAfter = (BaseFloorStyle)Mathf.Clamp(PlayerPrefs.GetInt("RockGame.BaseFloorAfter", (int)BaseFloorAfterDefault), 0, 2);
            s_BaseFloorTeamMix = Mathf.Clamp01(PlayerPrefs.GetFloat("RockGame.BaseFloorTeamMix", BaseFloorTeamMixDefault));
        }

        /// <summary>What the bases' floors turn into once the build phase is over.</summary>
        public static BaseFloorStyle BaseFloorAfter { get { LoadBaseFloor(); return s_BaseFloorAfter; } }
        /// <summary>How much of each team's colour goes into its floor and grid (0 = exactly the colours picked).</summary>
        public static float BaseFloorTeamMix { get { LoadBaseFloor(); return s_BaseFloorTeamMix; } }
        /// <summary>The floor's colour and the grid lines' colour (before the team colour goes in).</summary>
        public static Color BaseFloorColor => ColorSlots.BasePads.Value;
        public static Color BaseGridColor => ColorSlots.BaseGrid.Value;

        public static void SetBaseFloor(BaseFloorStyle after, float teamMix, bool save = true)
        {
            LoadBaseFloor();
            teamMix = Mathf.Clamp01(teamMix);
            if (after == s_BaseFloorAfter && Mathf.Approximately(teamMix, s_BaseFloorTeamMix)) return;
            s_BaseFloorAfter = after;
            s_BaseFloorTeamMix = teamMix;
            if (save)
            {
                PlayerPrefs.SetInt("RockGame.BaseFloorAfter", (int)after);
                PlayerPrefs.SetFloat("RockGame.BaseFloorTeamMix", teamMix);
                PlayerPrefs.Save();
            }
            FireWorldLookChanged();
        }

        public static void SetBaseFloorColors(Color floor, Color grid, bool save = true)
        {
            ColorSlots.Set(ColorSlots.BasePads, floor, save);
            ColorSlots.Set(ColorSlots.BaseGrid, grid, save);
        }

        /// <summary>The base floor settings back to the defaults (style, team colour and both colours).</summary>
        public static void ResetBaseFloor(bool save = true)
        {
            SetBaseFloor(BaseFloorAfterDefault, BaseFloorTeamMixDefault, save);
            SetBaseFloorColors(ColorSlots.BasePads.Default, ColorSlots.BaseGrid.Default, save);
        }

        /// <summary>"Name = value" lines for the base floor settings (for a copy / paste of the display settings).</summary>
        public static string BaseFloorLines() =>
            $"BaseFloorAfter = {BaseFloorAfter}\nBaseFloorTeamMix = {BaseFloorTeamMix.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\n" +
            $"BaseFloorColor = #{ColorUtility.ToHtmlStringRGB(BaseFloorColor)}\nBaseGridColor = #{ColorUtility.ToHtmlStringRGB(BaseGridColor)}\n";
    }

    /// <summary>
    /// Sits on a base's tinted floor (MapBuilder.Build): gives the floor and its grid lines materials of their own,
    /// coloured from Settings > Display > Base floor (the colours picked, with the team colour mixed in) and kept up to
    /// date live. MapBuilder.SetGlassWall shows or hides the floor by the style.
    /// </summary>
    public class BaseFloorLook : MonoBehaviour
    {
        int m_Team;
        Material m_Pad, m_Line;

        /// <summary>The floor and the grid as they're drawn now (tests).</summary>
        public Color PadColor => m_Pad ? m_Pad.color : Color.clear;
        public Color LineColor => m_Line ? m_Line.color : Color.clear;
        public int Team => m_Team;

        public static BaseFloorLook Dress(GameObject floor, int team, Renderer pad, List<Renderer> lines)
        {
            var l = floor.AddComponent<BaseFloorLook>();
            l.m_Team = team;
            // (the AI PSX look still sees the original colours)
            l.m_Pad = Art.NewMat(ColorSlots.BasePads.Default);
            l.m_Pad.name = "base floor " + team;
            Art.Register(l.m_Pad, ColorSlots.BasePads.Default);
            l.m_Line = Art.NewMat(ColorSlots.BaseGrid.Default);
            l.m_Line.name = "base grid " + team;
            Art.Register(l.m_Line, ColorSlots.BaseGrid.Default);
            if (pad) pad.sharedMaterial = l.m_Pad;
            foreach (var r in lines) if (r) r.sharedMaterial = l.m_Line;
            l.Apply();
            return l;
        }

        /// <summary>The colours a team's floor and grid are drawn in.</summary>
        public static void Colours(int team, out Color pad, out Color line)
        {
            var tc = Cfg.TeamColor[Mathf.Clamp(team, 0, Cfg.TeamColor.Length - 1)];
            float mix = GameSettings.BaseFloorTeamMix;
            pad = Color.Lerp(GameSettings.BaseFloorColor, tc, mix);
            line = Color.Lerp(GameSettings.BaseGridColor, tc, mix);
            pad.a = line.a = 1f;
        }

        void OnEnable() { GameSettings.WorldLookChanged += Apply; Apply(); }
        void OnDisable() => GameSettings.WorldLookChanged -= Apply;
        void OnDestroy() { if (m_Pad) Destroy(m_Pad); if (m_Line) Destroy(m_Line); }

        void Apply()
        {
            if (this == null || !m_Pad) return;
            Colours(m_Team, out var pad, out var line);
            m_Pad.SetColor("_BaseColor", pad); m_Pad.color = pad;
            m_Line.SetColor("_BaseColor", line); m_Line.color = line;
        }
    }
}
