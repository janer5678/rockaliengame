using UnityEngine;

namespace RockGame
{
    /// <summary>Settings > Display > HUD AND TIMER > KILL FEED: how the kill feed in the top right looks (Hud.KillFeed.cs
    /// reads these) - shown at all, its size, how dark its strips are, the weapon icons, team-coloured or white names, the
    /// gold / red edge on your own kills and deaths, how long a line stays, how many lines at once, and how far in from the
    /// top right corner it sits. The defaults are the kill feed as designed.</summary>
    public static partial class GameSettings
    {
        const string GKillFeed = "KILL FEED";
        public static readonly DisplayPref.Bool KillFeedOn = new("killfeed.on", GKillFeed, true);
        /// <summary>How big its strips, names and icons are (1 = as designed).</summary>
        public static readonly DisplayPref.Float KillFeedSize = new("killfeed.size", GKillFeed, 0.95f, 0.6f, 1.6f);
        /// <summary>How dark the strips behind the lines are (1 = as designed, 0 = none).</summary>
        public static readonly DisplayPref.Float KillFeedBack = new("killfeed.back", GKillFeed, 0f, 0f, 1.3f);
        /// <summary>The weapon (what did it) icon between the names.</summary>
        public static readonly DisplayPref.Bool KillFeedIcons = new("killfeed.icons", GKillFeed, true);
        /// <summary>The names in their team's colours (off: white).</summary>
        public static readonly DisplayPref.Bool KillFeedTeamColours = new("killfeed.teamcolours", GKillFeed, true);
        /// <summary>Your own kills edged in gold, your deaths in red.</summary>
        public static readonly DisplayPref.Bool KillFeedHighlight = new("killfeed.highlight", GKillFeed, true);
        /// <summary>How many seconds a line stays.</summary>
        public static readonly DisplayPref.Float KillFeedTime = new("killfeed.time", GKillFeed, 10f, 2f, 20f);
        /// <summary>How many lines show at once (the newest).</summary>
        public static readonly DisplayPref.Float KillFeedLines = new("killfeed.lines", GKillFeed, 5f, 1f, 10f);
        /// <summary>How far in from the right edge and down from the top it sits (pixels at the UI scale, past where it was).</summary>
        public static readonly DisplayPref.Float KillFeedX = new("killfeed.x", GKillFeed, 0f, 0f, 600f);
        public static readonly DisplayPref.Float KillFeedY = new("killfeed.y", GKillFeed, 0f, 0f, 500f);
        /// <summary>The names' colour while team colours are off.</summary>
        public static readonly DisplayPref.Colour KillFeedNameColour = new("killfeed.namecolour", GKillFeed, Color.white);
        /// <summary>The names' font, and their edge: a drop shadow (as designed), an outline all round, or none (HudTextLooks.cs:
        /// EdgeText) - how thick (pixels at the UI scale) and its colour.</summary>
        public static readonly DisplayPref.Choice KillFeedFont = new("killfeed.font", GKillFeed, FontPrefNames, 0);
        public static readonly DisplayPref.Choice KillFeedEdge = new("killfeed.edge", GKillFeed, EdgeNames, 0);
        public static readonly DisplayPref.Float KillFeedInk = new("killfeed.ink", GKillFeed, 1.5f, 0.25f, 4f);
        public static readonly DisplayPref.Colour KillFeedInkColour = new("killfeed.ink.colour", GKillFeed, Color.black);

        public static int KillFeedLinesNow => Mathf.Clamp(Mathf.RoundToInt(KillFeedLines.Value), 1, 10);

        public static void ResetKillFeed(bool save = true)
        {
            DisplayPref.ResetAll(save, KillFeedOn, KillFeedSize, KillFeedBack, KillFeedIcons, KillFeedTeamColours, KillFeedHighlight, KillFeedTime,
                KillFeedLines, KillFeedX, KillFeedY, KillFeedNameColour, KillFeedFont, KillFeedEdge, KillFeedInk, KillFeedInkColour);
        }
    }

    public partial class Hud
    {
        static readonly Color[] s_NamePresets = { Color.white, new Color(0.85f, 0.85f, 0.85f), new Color(1f, 0.85f, 0.3f), new Color(0.45f, 1f, 0.45f), new Color(0.35f, 0.85f, 1f), new Color(1f, 0.45f, 0.8f) };

        /// <summary>(tests) a few sample lines in the kill feed (a kill, a headshot, a fall, the ball).</summary>
        public static void TestKillFeed()
        {
            var me = PlayerNet.Local;
            byte t = me != null ? me.Team.Value : (byte)0, s = me != null ? me.Slot.Value : (byte)0, o = (byte)((t + 1) % Mathf.Max(2, Cfg.TeamCount));
            AddKill(t, s, o, 1, (byte)Item.Spear, false, "", "");
            AddKill(o, 2, t, 3, (byte)Item.Sniper, true, "", "");
            AddKill(255, 0, o, 4, KillCause.Fall, false, "", "");
            AddKill(o, 1, o, 0, KillCause.BallPickup, false, "", "");
        }

        /// <summary>Settings > Display > HUD AND TIMER: KILL FEED.</summary>
        void DrawKillFeedLooks()
        {
            float k = m_Scale, lw = 210 * k;
            void Slider(string name, DisplayPref.Float p, string shown, float step)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                float v = SliderRow(name, p.Value, p.Min, p.Max, shown, lw - 16 * k);
                p.Set(Mathf.Round(v / step) * step);
                GUILayout.EndHorizontal();
            }
            void Toggle(string name, DisplayPref.Bool p, string note = "")
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                GUILayout.Label(name, m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
                p.Set(ToggleBtn(p.Value, p.Value ? "On" : "Off", GUILayout.Width(80 * k), GUILayout.Height(26 * k)));
                if (note != "") GUILayout.Label($"<color=#bbbbbb>  {note}</color>", m_Small, GUILayout.Height(26 * k));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            Caption("KILL FEED  ·  the top right");
            Toggle("Kill feed", GameSettings.KillFeedOn, "who killed who, the ball's moments");
            if (GameSettings.KillFeedOn.Value)
            {
                Slider("Size", GameSettings.KillFeedSize, $"{GameSettings.KillFeedSize.Value * 100f:0}%", 0.05f);
                Slider("Background", GameSettings.KillFeedBack, GameSettings.KillFeedBack.Value < 0.01f ? "none" : $"{GameSettings.KillFeedBack.Value * 100f:0}%", 0.05f);
                Toggle("Weapon icons", GameSettings.KillFeedIcons);
                Toggle("Team colour names", GameSettings.KillFeedTeamColours, GameSettings.KillFeedTeamColours.Value ? "" : "off: every name in the colour below");
                if (!GameSettings.KillFeedTeamColours.Value) ColourPrefRow("Name colour", GameSettings.KillFeedNameColour, s_NamePresets);
                ChoiceRow("Font", GameSettings.KillFeedFont, true);
                ChoiceRow("Text edge", GameSettings.KillFeedEdge, false, "Drop shadow", "Outline", "None");
                if (GameSettings.KillFeedEdge.Value != 2)
                {
                    Slider(GameSettings.KillFeedEdge.Value == 0 ? "Shadow distance" : "Outline thickness", GameSettings.KillFeedInk, $"{GameSettings.KillFeedInk.Value:0.##} px", 0.25f);
                    ColourPrefRow("Edge colour", GameSettings.KillFeedInkColour, s_InkPresets);
                }
                Toggle("Your kills / deaths edged", GameSettings.KillFeedHighlight, "gold / red");
                Slider("Lines stay for", GameSettings.KillFeedTime, $"{GameSettings.KillFeedTime.Value:0.#} s", 0.5f);
                Slider("Most lines at once", GameSettings.KillFeedLines, $"{GameSettings.KillFeedLinesNow}", 1f);
                Slider("In from the right", GameSettings.KillFeedX, GameSettings.KillFeedX.Value < 0.5f ? "as usual" : $"{GameSettings.KillFeedX.Value:0} px", 5f);
                Slider("Down from the top", GameSettings.KillFeedY, GameSettings.KillFeedY.Value < 0.5f ? "as usual" : $"{GameSettings.KillFeedY.Value:0} px", 5f);
            }
            GUILayout.BeginHorizontal();
            if (Btn("Test kill feed", GUILayout.Width(160 * k), GUILayout.Height(28 * k))) TestKillFeed();
            GUILayout.Label("<color=#bbbbbb>  a few sample lines (they show in a match, top right)</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetKillFeed();
            GUILayout.EndHorizontal();
        }
    }
}
