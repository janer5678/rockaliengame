using UnityEngine;

namespace RockGame
{
    /// <summary>THEME MAPS: how the ground feels - wading is slow, ice is slippery (you keep sliding the way you were going).</summary>
    public partial class PlayerController
    {
        Vector3 m_IceVel;

        Vector3 ThemeGround(Vector3 planar, bool grounded)
        {
            if (!ThemeMaps.IsTheme) return planar;
            var p = transform.position;
            if (grounded && ThemeMaps.OnIce(p))
            {
                // on the ice your speed only changes slowly
                m_IceVel = Vector3.MoveTowards(m_IceVel, planar, 3.5f * Time.deltaTime);
                return m_IceVel;
            }
            m_IceVel = grounded ? planar : Vector3.Lerp(m_IceVel, planar, 2f * Time.deltaTime);
            return planar;
        }
    }
}
