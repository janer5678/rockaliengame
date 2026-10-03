using UnityEngine;

namespace RockGame
{
    public partial class Hud
    {
        /// <summary>
        /// Settings > Display > BEAMS (just on this PC): how strong the beams of light are from far away (the ball's beacon,
        /// the airdrop ships' beams and the landed crates' beacons, the victory UFO's beam), and over how many metres they
        /// fade down to a faint glow as you come up to them. Stored in GameSettings (BeamStrength, BeamFalloff) like the
        /// other display settings.
        /// </summary>
        void DrawBeamSettings()
        {
            float k = m_Scale, lw = 210 * k;
            Caption("BEAMS  ·  just on this PC");
            float st = SliderRow("Beam strength", GameSettings.BeamStrength, GameSettings.BeamStrengthMin, GameSettings.BeamStrengthMax, $"{GameSettings.BeamStrength * 100f:0}%", lw);
            float fo = SliderRow("Beam falloff", GameSettings.BeamFalloff, GameSettings.BeamFalloffMin, GameSettings.BeamFalloffMax,
                GameSettings.BeamFalloff < 0.5f ? "off" : $"{GameSettings.BeamFalloff:0} m", lw);
            st = Mathf.Round(st * 20f) / 20f;
            fo = Mathf.Round(fo / 5f) * 5f;
            if (!Mathf.Approximately(st, GameSettings.BeamStrength) || !Mathf.Approximately(fo, GameSettings.BeamFalloff)) GameSettings.SetBeams(st, fo);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>Strength: how bright beams are from far away. Falloff: they fade down to a faint glow as you come up to them, over this many metres (off = always full strength).</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetBeams();
            GUILayout.EndHorizontal();
        }
    }
}
