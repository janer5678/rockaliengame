using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > BASE FLOOR: what the bases' build grid turns into after the build phase, its floor and
    /// grid colours (the world colour picker) and how much team colour is mixed in. Self-contained: one call from the
    /// Display tab (DrawDisplayTab). The values live in GameSettings (BaseFloor.cs).</summary>
    public partial class Hud
    {
        void DrawBaseFloorSettings()
        {
            float k = m_Scale, lw = 150 * k;
            Caption("BASE FLOOR  ·  just on this PC");
            GUILayout.BeginHorizontal();
            RowLabel("After building", lw);
            var after = GameSettings.BaseFloorAfter;
            for (int i = 0; i < GameSettings.BaseFloorStyleNames.Length; i++)
                if (Choice((int)after == i, GameSettings.BaseFloorStyleNames[i], GUILayout.Height(30 * k))) after = (GameSettings.BaseFloorStyle)i;
            GUILayout.EndHorizontal();
            BaseFloorColourRow("Floor colour", ColorSlots.BasePads, lw);
            BaseFloorColourRow("Grid colour", ColorSlots.BaseGrid, lw);
            float mix = SliderRow("Team colour", GameSettings.BaseFloorTeamMix, 0f, 1f, $"{GameSettings.BaseFloorTeamMix * 100f:0}%", lw);
            GameSettings.SetBaseFloor(after, Mathf.Round(mix * 20f) / 20f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>While you build it's always the grid. Team colour: how much of each team's colour goes into its floor and grid.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetBaseFloor();
            GUILayout.EndHorizontal();
        }

        /// <summary>A colour row: the swatch (click it, or Pick, for the colour picker under the row), and Default.</summary>
        void BaseFloorColourRow(string label, ColorSlots.Slot s, float lw)
        {
            float k = m_Scale, rowH = 30 * k;
            bool picking = m_PickerSlot == s.Index;
            GUILayout.BeginHorizontal();
            RowLabel(label, lw);
            var r = GUILayoutUtility.GetRect(56 * k, rowH, GUILayout.Width(56 * k), GUILayout.Height(rowH));
            r = new Rect(r.x, r.y + 4 * k, r.width, r.height - 8 * k);
            Fill(r, picking ? new Color(1f, 0.82f, 0.3f) : new Color(0.8f, 0.8f, 0.8f));
            Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), s.Value);
            TrackHover(r);
            bool wheel = ColourWheelOnDoubleClick(r, "colour." + s.Id, s.Label, () => s.Value, (c, save) => ColorSlots.Set(s, c, save)); // (double-click: the colour wheel)
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !wheel) { ClickSound(); TogglePicker(s, s.Value, false); }
            GUILayout.Label($"<color=#bbbbbb>  #{ColorUtility.ToHtmlStringRGB(s.Value)}</color>", m_Small, GUILayout.Width(90 * k), GUILayout.Height(rowH));
            if (Btn(picking ? "Pick ▲" : "Pick ▼", GUILayout.Width(80 * k), GUILayout.Height(rowH))) TogglePicker(s, s.Value, false);
            if (s.Changed && Btn("Default", GUILayout.Width(84 * k), GUILayout.Height(rowH))) ColorSlots.Set(s, s.Default);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (picking) DrawColourPicker(s, s.Value, false, 520 * k);
        }
    }
}
