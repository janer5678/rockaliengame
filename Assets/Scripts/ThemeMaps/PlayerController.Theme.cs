using UnityEngine;

namespace RockGame
{
    /// <summary>THEME MAPS: how the ground feels - wading is slow, ice is slippery (you keep sliding the way you were going).</summary>
    public partial class PlayerController
    {
        Vector3 m_IceVel;

        /// <summary>A deep-water map (ThemeMap.DeepWater), off a boat and down in the water: you're swimming - very slowly,
        /// floating up to the surface (Space keeps your head up and lets you climb out onto a shore).</summary>
        public bool Swimming
        {
            get
            {
                var tm = ThemeMaps.Custom;
                if (tm == null || !tm.DeepWater || m_Net == null || m_Net.Riding) return false;
                var p = transform.position;
                return p.y < ThemeMaps.WaterY - 0.6f && MapBuilder.Height(p.x, p.z) < ThemeMaps.WaterY - 0.9f;
            }
        }
        /// <summary>(The old name: it's swimming now, not drowning.)</summary>
        public bool Drowning => false;

        Vector3 ThemeGround(Vector3 planar, bool grounded)
        {
            if (!ThemeMaps.IsTheme) return planar;
            var p = transform.position;
            if (Swimming)
            {
                // swimming: you bob up to float with your head out (Space swims up a bit faster), and move very slowly
                float surface = ThemeMaps.WaterY - 1.25f; // (feet this far under: the head is out)
                float want = p.y < surface ? (Binds.Held(Bind.Jump) ? 2.6f : 1.4f) : 0f;
                m_VelY = Mathf.MoveTowards(m_VelY, want, 12f * Time.deltaTime);
                return planar * 0.3f;
            }
            if (grounded && ThemeMaps.OnIce(p))
            {
                var tm = ThemeMaps.Custom;
                if (tm != null && tm.IceSteer > 0f)
                {
                    // Minecraft-like ice: pushing a direction gets you going (and turns you) at IceSteer; let go and you
                    // glide on, slowing only at IceGrip - slippery, but never fighting you when you turn
                    bool pushing = planar.sqrMagnitude > 0.01f;
                    m_IceVel = Vector3.MoveTowards(m_IceVel, planar, (pushing ? tm.IceSteer : tm.IceGrip) * Time.deltaTime);
                    return m_IceVel;
                }
                // on the ice your speed only changes slowly
                m_IceVel = Vector3.MoveTowards(m_IceVel, planar, (tm != null ? tm.IceGrip : 3.5f) * Time.deltaTime);
                return m_IceVel;
            }
            m_IceVel = grounded ? planar : Vector3.Lerp(m_IceVel, planar, 2f * Time.deltaTime);
            return planar;
        }
    }
}
