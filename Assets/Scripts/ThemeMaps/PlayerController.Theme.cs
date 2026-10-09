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

        /// <summary>Seconds of breath you have under water before you start drowning (the server counts the same).</summary>
        public const float BreathTime = 14f;
        /// <summary>Your breath (1 = full): it runs down while your head's under a deep-water map's sea, and comes back
        /// quickly once it's out. The HUD shows it as bubbles.</summary>
        public float Breath { get; private set; } = 1f;
        /// <summary>Your head is under the sea (a deep-water map).</summary>
        public static bool HeadUnder(Vector3 feet) => ThemeMaps.Custom != null && ThemeMaps.Custom.DeepWater && feet.y + 1.5f < ThemeMaps.WaterY;

        void TickBreath()
        {
            bool under = !m_Net.Riding && !m_Net.Dead.Value && HeadUnder(transform.position);
            Breath = Mathf.Clamp01(Breath + (under ? -1f / BreathTime : 0.5f) * Time.deltaTime);
        }

        Vector3 ThemeGround(Vector3 planar, bool grounded)
        {
            if (!ThemeMaps.IsTheme) return planar;
            var p = transform.position;
            if (m_Net != null) TickBreath();
            if (Swimming)
            {
                // swimming: you sink, very slowly; hold Space to swim up at a decent pace (and keep your head out at the
                // top). Going forwards is the slow part.
                float surface = ThemeMaps.WaterY - 1.25f; // (feet this far under: the head is out)
                float want = Binds.Held(Bind.Jump) ? (p.y < surface ? 3.2f : 0f) : -0.35f;
                m_VelY = Mathf.MoveTowards(m_VelY, want, 10f * Time.deltaTime);
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
