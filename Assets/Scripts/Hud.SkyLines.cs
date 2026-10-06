using UnityEngine;

namespace RockGame
{
    public partial class Hud
    {
        /// <summary>
        /// Settings > Display > SKY LINES AND THE WALL (just on this PC; both in COPY / PASTE SETTINGS): the thickness of
        /// the outline lines on the far things - clouds, planets, far mountains (GameSettings.FarLineThickness, 100% = the
        /// usual look; only while the Outlines extra look is on) - and the Energy wall switch (GameSettings.EnergyWall:
        /// the glass wall between the halves drawn as an energy field; it stops you and drops just the same).
        /// </summary>
        void DrawSkyLinesAndWall()
        {
            float k = m_Scale, lw = 210 * k;
            Caption("SKY LINES AND THE WALL  ·  just on this PC");
            float far = GameSettings.FarLineThickness.Value;
            float f = SliderRow("Far line thickness", far, GameSettings.FarLineThickness.Min, GameSettings.FarLineThickness.Max,
                far <= 0.001f ? "none" : $"{far * 100f:0}%", lw);
            f = Mathf.Round(f * 20f) / 20f;
            if (!Mathf.Approximately(f, far)) GameSettings.FarLineThickness.Set(f);
            GUILayout.BeginHorizontal();
            RowLabel("Energy wall", lw);
            bool en = ToggleBtn(GameSettings.EnergyWall.Value, GameSettings.EnergyWall.Value ? "On" : "Off", GUILayout.Width(90 * k), GUILayout.Height(30 * k));
            if (en != GameSettings.EnergyWall.Value) GameSettings.EnergyWall.Set(en);
            GUILayout.Label("<color=#bbbbbb>  all the glass (the wall, the ball's dome and the big dome) as a glowing energy field</color>", m_Small, GUILayout.Height(30 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (GameSettings.EnergyWall.Value)
            {
                float es = GameSettings.EnergyWallStrength.Value;
                float ns = SliderRow("Energy wall strength", es, 0f, 1f, $"{es * 100f:0}%", lw);
                ns = Mathf.Round(ns * 20f) / 20f;
                if (!Mathf.Approximately(ns, es)) GameSettings.EnergyWallStrength.Set(ns);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>Far line thickness: the ink lines round the clouds, the planets and the far mountains (with Outlines on in POST PROCESSING). 100% is the usual look; lower is thinner (0 = none), higher is thicker - and past 100% their outlines against the sky stay however far away they are.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetSkyLinesAndWall();
            GUILayout.EndHorizontal();
        }
    }
}
