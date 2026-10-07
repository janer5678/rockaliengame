using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > HUD & TIMER: the text looks of the small HUD bits -
    /// BASE RADAR ("Your Base" + the arrow + how far, top left: Hud.cs DrawBaseRadar) - its font and its text's edge: a
    ///   drop shadow (as designed), an outline all round, or none, how thick, and its colour;
    /// DAMAGE NUMBERS (the numbers popping off what you hit, and the kill cam replay's) - their edge (a drop shadow as
    ///   designed, an outline, or none) and how thick it is (thinner than it was by default).
    /// (The kill feed's font and edge are with its other settings: KillFeedLooks.cs.) EdgeText draws a text with any of them.
    /// </summary>
    public static partial class GameSettings
    {
        /// <summary>A text's edge: a drop shadow, an outline all round, or none.</summary>
        public static string[] EdgeNames => EdgeNameList.Names; // (its own class: made before any file's fields that use it)

        const string GRadar = "BASE RADAR";
        public static readonly DisplayPref.Choice RadarFont = new("radar.font", GRadar, FontPrefNames, 0);
        public static readonly DisplayPref.Choice RadarEdge = new("radar.edge", GRadar, EdgeNames, 0);
        /// <summary>How far the shadow sits / how thick the outline is (pixels at the UI scale).</summary>
        public static readonly DisplayPref.Float RadarInk = new("radar.ink", GRadar, 1.5f, 0.25f, 4f);
        public static readonly DisplayPref.Colour RadarInkColour = new("radar.ink.colour", GRadar, Color.black);

        public static void ResetRadar(bool save = true)
        {
            RadarFont.Set(0, save); RadarEdge.Set(0, save); RadarInk.Set(1.5f, save); RadarInkColour.Set(Color.black, save);
        }

        const string GDamage = "DAMAGE NUMBERS";
        public static readonly DisplayPref.Choice DamageEdge = new("damage.edge", GDamage, EdgeNames, 0);
        /// <summary>How far the shadow sits / how thick the outline is (pixels at the UI scale; it was 2 px, the replay's 2.5).</summary>
        public static readonly DisplayPref.Float DamageInk = new("damage.ink", GDamage, 1f, 0.25f, 3f);

        public static void ResetDamageNumbers(bool save = true) { DamageEdge.Set(0, save); DamageInk.Set(1f, save); }
    }

    static class EdgeNameList
    {
        public static readonly string[] Names = { "Shadow", "Outline", "None" };
    }

    public partial class Hud
    {
        /// <summary>(tests) the base radar's text edge as last drawn (mode: 0 shadow, 1 outline, 2 none; px), and the
        /// damage numbers'.</summary>
        public static int RadarEdgeShown = -1, DamageEdgeShown = -1;
        public static float RadarInkShown, DamageInkShown;

        /// <summary>A text with an edge: `edge` 0 a drop shadow `px` down and right, 1 an outline `px` thick all round (eight
        /// copies), 2 none. The edge colour's alpha is taken as it is.</summary>
        static void EdgeText(Rect r, string text, GUIStyle st, Color fill, int edge, float px, Color edgeCol)
        {
            var old = st.normal.textColor;
            if (edge != 2 && px > 0.01f && edgeCol.a > 0.001f)
            {
                string plain = st.richText ? s_ColorTag.Replace(text, "") : text;
                st.normal.textColor = edgeCol;
                if (edge == 0) GUI.Label(new Rect(r.x + px, r.y + px, r.width, r.height), plain, st);
                else
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        GUI.Label(new Rect(r.x + Mathf.Cos(a) * px, r.y + Mathf.Sin(a) * px, r.width, r.height), plain, st);
                    }
            }
            st.normal.textColor = fill;
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }

        /// <summary>Settings > Display > HUD & TIMER: BASE RADAR and DAMAGE NUMBERS.</summary>
        void DrawHudTextLooks()
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
            Caption("BASE RADAR  ·  \"Your Base\", top left");
            ChoiceRow("Font", GameSettings.RadarFont, true);
            ChoiceRow("Text edge", GameSettings.RadarEdge, false, "Drop shadow", "Outline", "None");
            if (GameSettings.RadarEdge.Value != 2)
            {
                Slider(GameSettings.RadarEdge.Value == 0 ? "Shadow distance" : "Outline thickness", GameSettings.RadarInk, $"{GameSettings.RadarInk.Value:0.##} px", 0.25f);
                ColourPrefRow("Edge colour", GameSettings.RadarInkColour, s_InkPresets);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#bbbbbb>Shows outside your base, under the team box (switch it on or off under HUD).</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetRadar();
            GUILayout.EndHorizontal();

            Caption("DAMAGE NUMBERS  ·  the numbers off what you hit");
            ChoiceRow("Text edge", GameSettings.DamageEdge, false, "Drop shadow", "Outline", "None");
            if (GameSettings.DamageEdge.Value != 2)
                Slider(GameSettings.DamageEdge.Value == 0 ? "Shadow distance" : "Outline thickness", GameSettings.DamageInk, $"{GameSettings.DamageInk.Value:0.##} px", 0.25f);
            GUILayout.BeginHorizontal();
            if (Btn("Test numbers", GUILayout.Width(160 * k), GUILayout.Height(28 * k))) TestDamageNumbers();
            GUILayout.Label("<color=#bbbbbb>  a few in front of you (in a match). The kill cam replay's numbers use these too.</color>", m_SmallWrap);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetDamageNumbers();
            GUILayout.EndHorizontal();
        }

        /// <summary>(tests / the settings) a hit, a headshot and a kill in front of the camera.</summary>
        public static void TestDamageNumbers()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var p = cam.transform.position + cam.transform.forward * 7f;
            Fx.DamageNumber(p + cam.transform.right * 1.4f, 34f, false);
            Fx.DamageNumber(p - cam.transform.right * 1.4f, 61f, true);
            Fx.DamageNumber(p + cam.transform.up * 0.8f, 100f, false, true);
        }
    }
}
