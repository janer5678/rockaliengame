using UnityEngine;

namespace RockGame
{
    /// <summary>THEME MAPS: how the ground feels - wading is slow, ice is slippery (you keep sliding the way you were going).</summary>
    public partial class PlayerController
    {
        Vector3 m_IceVel;

        /// <summary>A deep-water map (ThemeMap.DeepWater), off a boat and under the surface: no swimming - you sink slowly
        /// (no jumping out) and drown at the map's KillY.</summary>
        public bool Drowning
        {
            get
            {
                var tm = ThemeMaps.Custom;
                if (tm == null || !tm.DeepWater || m_Net == null || m_Net.Riding) return false;
                var p = transform.position;
                return p.y < ThemeMaps.WaterY - 0.2f && MapBuilder.Height(p.x, p.z) < ThemeMaps.WaterY - 0.3f;
            }
        }

        Vector3 ThemeGround(Vector3 planar, bool grounded)
        {
            if (!ThemeMaps.IsTheme) return planar;
            var p = transform.position;
            if (Drowning)
            {
                // sinking: slowly down, barely able to move, no way back up
                m_VelY = Mathf.Clamp(m_VelY, -1.6f, -0.6f);
                return planar * 0.25f;
            }
            if (grounded && ThemeMaps.OnIce(p))
            {
                // on the ice your speed only changes slowly
                m_IceVel = Vector3.MoveTowards(m_IceVel, planar, (ThemeMaps.Custom != null ? ThemeMaps.Custom.IceGrip : 3.5f) * Time.deltaTime);
                return m_IceVel;
            }
            m_IceVel = grounded ? planar : Vector3.Lerp(m_IceVel, planar, 2f * Time.deltaTime);
            return planar;
        }
    }
}
