using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public static partial class GameSettings
    {
        /// <summary>Settings > Display > SKY LINES AND THE WALL: how thick the outline pass's ink lines are on the far things -
        /// the clouds, the planets and the far mountains (and anything else past ~40 m, or against the sky). 1 = the usual
        /// look; under 1 thinner (0: none), over 1 thicker, and past 1 their silhouettes against the sky keep a line however
        /// far off they are (fully at 2) - the planets, the far ranges and the clouds get one too. Only while Outlines is on.
        /// In the display settings code as post.outlines.far.</summary>
        public static readonly DisplayPref.Float FarLineThickness = new("post.outlines.far", "POST PROCESSING", DisplayDefaults.FarLineThickness, 0f, 3f);

        /// <summary>Settings > Display: the glass wall between the halves (and the ball's dome) drawn as a glowing energy
        /// field instead of glass (EnergyWall.cs). Only the look: it stops you and drops exactly as before. world.energywall.</summary>
        public static readonly DisplayPref.Bool EnergyWall = new("world.energywall", "WORLD", DisplayDefaults.EnergyWall);

        public static void ResetSkyLinesAndWall(bool save = true)
        {
            FarLineThickness.Set(DisplayDefaults.FarLineThickness, save);
            EnergyWall.Set(DisplayDefaults.EnergyWall, save);
        }
    }

    /// <summary>
    /// The glass wall between the halves and the ball's dome in the middle (MapBuilder.BuildGlassWall) as an ENERGY WALL
    /// when Settings > Display > Energy wall is on: their glass takes the animated energy-field material
    /// (Assets/Game/Resources/World/EnergyWall.shader - a cyan honeycomb shimmer with rising scan bands, team-neutral) and
    /// the glass's frame lines and ribs are hidden. Only the renderers change: the colliders, the drop (GlassWallDrop) and
    /// everything that goes by the wall are untouched. Switches live, both ways (the glass materials are kept to go back to).
    /// </summary>
    public class EnergyWall : MonoBehaviour
    {
        static Material s_Mat;
        static bool s_Tried;
        readonly List<(Renderer r, Material glass)> m_Glass = new List<(Renderer, Material)>();
        readonly List<Renderer> m_Lines = new List<Renderer>();
        bool m_Shown;

        /// <summary>Test hooks: is the energy look on this wall now, and the material it uses.</summary>
        public bool Energy => m_Shown;
        public static Material Material => Mat();
        public static EnergyWall Current { get; private set; }

        static Material Mat()
        {
            if (s_Mat != null || s_Tried) return s_Mat;
            s_Tried = true;
            var sh = Resources.Load<Shader>("World/EnergyWall");
            if (sh == null || !sh.isSupported) { Debug.LogWarning("[RockGame] World/EnergyWall shader missing: the energy wall stays glass"); return null; }
            s_Mat = new Material(sh) { name = "energy wall", hideFlags = HideFlags.DontSave };
            s_Mat.renderQueue = 3000;
            return s_Mat;
        }

        /// <summary>Puts the energy look on a glass wall (its glass: names starting "glass" without "lines" / "ribs").</summary>
        public static void Attach(GameObject wall)
        {
            if (wall == null) return;
            var e = wall.GetComponent<EnergyWall>();
            if (e == null) e = wall.AddComponent<EnergyWall>();
            e.Collect();
            e.Apply();
        }

        void Collect()
        {
            m_Glass.Clear();
            m_Lines.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n.Contains("lines") || n.Contains("ribs")) m_Lines.Add(r);
                else m_Glass.Add((r, r.sharedMaterial));
            }
            m_Shown = false;
        }

        void OnEnable()
        {
            Current = this;
            DisplayPref.Changed += Apply;
            Apply();
        }

        void OnDisable()
        {
            DisplayPref.Changed -= Apply;
            if (Current == this) Current = null;
        }

        void Apply()
        {
            if (this == null) return;
            bool want = GameSettings.EnergyWall.Value && Mat() != null;
            if (want == m_Shown) return;
            m_Shown = want;
            foreach (var (r, glass) in m_Glass)
                if (r) r.sharedMaterial = want ? s_Mat : glass;
            foreach (var r in m_Lines)
                if (r) r.forceRenderingOff = want; // (hidden, not disabled: whatever switches them on and off still can)
        }
    }
}
