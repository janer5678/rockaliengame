using UnityEngine;

namespace RockGame
{
    /// <summary>THEME MAPS: things on a map that throw you (Wonderland's launch pads) or carry you (Cherry Blossom's moving
    /// chunks of earth). Only ever called on the local player (PlayerController.Local) from a map's ClientTick.</summary>
    public partial class PlayerController
    {
        float m_LaunchedAt = -10f;

        /// <summary>Seconds since the last Launch (a pad shouldn't throw you again while you're still on it).</summary>
        public float SinceLaunch => Time.time - m_LaunchedAt;

        /// <summary>Flings you: `velocity` up (m/s) and along the ground (it fades in the air like a car's knockback, so
        /// aim with that in mind - ThemeKitB.LaunchVelocity works it out). You can still steer a little in the air.</summary>
        public void Launch(Vector3 velocity)
        {
            if (m_Net == null || m_Net.Dead.Value || m_Net.Riding) return;
            if (m_SlideOn) { m_SlideVel = Vector3.zero; EndSlide(); }
            m_Push = new Vector3(velocity.x, 0f, velocity.z);
            m_VelY = velocity.y;
            m_JumpPressedAt = -10f;
            m_JumpedSinceGround = true;
            m_LaunchedAt = Time.time;
        }

        /// <summary>Moves you along with the platform under you (by `delta` this frame, metres), through the controller so
        /// you still bump into things.</summary>
        public void Carry(Vector3 delta)
        {
            if (m_CC == null || !m_CC.enabled || m_Net == null || m_Net.Dead.Value || m_Net.Riding) return;
            if (delta.sqrMagnitude < 1e-10f) return;
            m_CC.Move(delta);
        }
    }
}
