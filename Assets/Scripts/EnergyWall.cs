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

        /// <summary>Settings > Display: all the glass - the wall between the halves, the ball's dome and the big dome over the
        /// map - drawn as a glowing energy field instead of glass (EnergyWall.cs). Only the look: it stops you and drops
        /// exactly as before. world.energywall.</summary>
        public static readonly DisplayPref.Bool EnergyWall = new("world.energywall", "WORLD", DisplayDefaults.EnergyWall);

        /// <summary>Settings > Display: how strong the energy wall is (0..1): how bright and opaque its honeycomb, scan bands
        /// and flares are, and how much it moves. Low keeps it a faint shimmer you see the other teams and the sky through.
        /// world.energywall.strength.</summary>
        public static readonly DisplayPref.Float EnergyWallStrength = new("world.energywall.strength", "WORLD", DisplayDefaults.EnergyWallStrength, 0f, 1f);

        public static void ResetSkyLinesAndWall(bool save = true)
        {
            FarLineThickness.Set(DisplayDefaults.FarLineThickness, save);
            EnergyWall.Set(DisplayDefaults.EnergyWall, save);
            EnergyWallStrength.Set(DisplayDefaults.EnergyWallStrength, save);
        }
    }

    /// <summary>
    /// The glass (the wall between the halves and the ball's dome - MapBuilder.BuildGlassWall - and the big dome over the
    /// map - MapDome) as an ENERGY WALL when Settings > Display > Energy wall is on: its glass takes an animated
    /// energy-field material (Assets/Game/Resources/World/EnergyWall.shader - a faint cyan honeycomb shimmer, team-neutral,
    /// its strength from Settings > Display) and the glass's frame lines and ribs are hidden. Each wall has its own copy of
    /// the material: the big dome's carries its hole mask (blown-out holes stay open) and its fade (the victory cutscene).
    /// Only the renderers change: the colliders, the drop (GlassWallDrop) and everything that goes by the wall are untouched.
    /// Switches live, both ways (the glass materials are kept to go back to).
    /// </summary>
    public class EnergyWall : MonoBehaviour
    {
        static Shader s_Shader;
        static bool s_Tried;
        static Material s_Probe;
        readonly List<(Renderer r, Material glass)> m_Glass = new List<(Renderer, Material)>();
        readonly List<Renderer> m_Lines = new List<Renderer>();
        Material m_Mat;
        Texture m_Mask;
        bool m_Dome, m_Shown;

        /// <summary>Test hooks: is the energy look on this wall now, and is a material the energy one.</summary>
        public bool Energy => m_Shown;
        public static bool IsEnergy(Material m) => m != null && m.shader != null && m.shader.name == "RockGame/EnergyWall";
        /// <summary>(tests) A material with the shader, or null if it's missing (then the walls stay glass).</summary>
        public static Material Material { get { if (s_Probe == null && Shader() != null) s_Probe = new Material(s_Shader) { hideFlags = HideFlags.DontSave }; return s_Probe; } }
        public static EnergyWall Current { get; private set; }
        public static EnergyWall Dome { get; private set; }

        static Shader Shader()
        {
            if (s_Shader != null || s_Tried) return s_Shader;
            s_Tried = true;
            var sh = Resources.Load<Shader>("World/EnergyWall");
            if (sh == null || !sh.isSupported) { Debug.LogWarning("[RockGame] World/EnergyWall shader missing: the glass stays glass"); return null; }
            s_Shader = sh;
            return s_Shader;
        }

        /// <summary>Puts the energy look on a glass wall (its glass: every renderer but the "lines" / "ribs" / "frame" ones,
        /// which are hidden while it's on). mask: the big dome's hole mask (its UVs are the map's x, z); dome: follows the
        /// dome's fade.</summary>
        public static void Attach(GameObject wall, Texture mask = null, bool dome = false)
        {
            if (wall == null) return;
            var e = wall.GetComponent<EnergyWall>();
            if (e == null) e = wall.AddComponent<EnergyWall>();
            e.m_Mask = mask;
            e.m_Dome = dome;
            if (dome) Dome = e;
            e.Collect();
            e.Apply();
        }

        void Collect()
        {
            if (m_Shown) foreach (var (r, glass) in m_Glass) if (r) r.sharedMaterial = glass;
            m_Glass.Clear();
            m_Lines.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n.Contains("hole")) continue; // (the big dome's hole rims: theirs)
                if (n.Contains("lines") || n.Contains("ribs") || n.Contains("frame")) m_Lines.Add(r);
                else m_Glass.Add((r, r.sharedMaterial));
            }
            m_Shown = false;
        }

        void OnEnable()
        {
            if (!m_Dome) Current = this;
            DisplayPref.Changed += Apply;
            Apply();
        }

        void OnDisable()
        {
            DisplayPref.Changed -= Apply;
            if (Current == this) Current = null;
            if (Dome == this) Dome = null;
        }

        void OnDestroy() { if (m_Mat != null) Destroy(m_Mat); }

        /// <summary>Something struck this collider at `point`: if it's glass drawn as the energy wall, a crackling zap and a
        /// low buzz, a burst of cyan sparks and a ripple of light (true: it was the energy wall).</summary>
        public static bool Hit(Collider c, Vector3 point, Vector3 normal)
        {
            if (c == null || !GameSettings.EnergyWall.Value) return false;
            var e = c.GetComponentInParent<EnergyWall>();
            if (e == null && MapDome.Collider == c) e = Dome;
            if (e == null || !e.Energy) return false;
            Sfx.Play(Sfx.Zap, point, 0.75f, 0.15f, 45f);
            Sfx.PlayPitched(Sfx.Hum, point, 0.6f, 0.55f, 40f);
            Fx.Chips(point, normal, new Color(0.45f, 0.95f, 1f), 9, 3.5f);
            FxParticle.Puff(point + normal * 0.05f, new Color(0.5f, 0.95f, 1f, 0.6f), 0.6f);
            return true;
        }

        Material Mat()
        {
            if (m_Mat != null) return m_Mat;
            if (Shader() == null) return null;
            m_Mat = new Material(s_Shader) { name = m_Dome ? "energy dome" : "energy wall", hideFlags = HideFlags.DontSave };
            m_Mat.renderQueue = m_Dome ? 2990 : 3000; // (the big dome first: everything see-through is inside it)
            if (m_Mask != null) { m_Mat.SetTexture("_HoleMask", m_Mask); m_Mat.SetFloat("_UseMask", 1f); }
            return m_Mat;
        }

        void Apply()
        {
            if (this == null) return;
            var mat = GameSettings.EnergyWall.Value ? Mat() : null;
            if (mat != null)
            {
                mat.SetFloat("_Strength", GameSettings.EnergyWallStrength.Value);
                if (m_Dome) mat.SetFloat("_Fade", MapDome.Fade);
            }
            bool want = mat != null;
            if (want == m_Shown) return;
            m_Shown = want;
            foreach (var (r, glass) in m_Glass)
                if (r) r.sharedMaterial = want ? mat : glass;
            foreach (var r in m_Lines)
                if (r) r.forceRenderingOff = want; // (hidden, not disabled: whatever switches them on and off still can)
        }

        void LateUpdate()
        {
            if (m_Dome && m_Shown && m_Mat != null) m_Mat.SetFloat("_Fade", MapDome.Fade); // (the victory cutscene fades the dome)
        }
    }
}
