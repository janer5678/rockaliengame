using UnityEngine;

namespace RockGame
{
    /// <summary>THEME MAPS: things on a map that throw you (Wonderland's launch pads) or carry you (Cherry Blossom's moving
    /// chunks of earth). Only ever called on the local player (PlayerController.Local) from a map's ClientTick.</summary>
    public partial class PlayerController
    {
        float m_LaunchedAt = -10f;
        /// <summary>Flying from a Launch (until you land): your keys steer the throw, and landing stops you dead.</summary>
        bool m_LaunchFlying;

        /// <summary>How fast WASD steers you in the air after a launch (m/s per second), and the least speed it steers toward.</summary>
        const float LaunchSteerRate = 13f, LaunchSteerSpeed = 7f;

        /// <summary>Seconds since the last Launch (a pad shouldn't throw you again while you're still on it).</summary>
        public float SinceLaunch => Time.time - m_LaunchedAt;

        /// <summary>Flings you: `velocity` up (m/s) and along the ground (it fades in the air like a car's knockback, so
        /// aim with that in mind - ThemeKitB.LaunchVelocity works it out). In the air WASD steers the throw (LaunchTick), and
        /// you stop where you land.</summary>
        public void Launch(Vector3 velocity)
        {
            if (m_Net == null || m_Net.Dead.Value || m_Net.Riding) return;
            if (m_SlideOn) { m_SlideVel = Vector3.zero; EndSlide(); }
            m_Push = new Vector3(velocity.x, 0f, velocity.z);
            m_VelY = velocity.y;
            m_JumpPressedAt = -10f;
            m_JumpedSinceGround = true;
            m_LaunchedAt = Time.time;
            m_LaunchFlying = true;
            m_SlideQueued = -1f; // (no slide out of the landing: you stop where you land)
        }

        /// <summary>Every frame on a map with launches (from its ClientTick): while flying from a Launch, WASD turns the
        /// throw your way (the push blends toward where you're pressing, at least LaunchSteerSpeed - pressing back brakes
        /// and turns you round); the moment you touch the ground the throw is gone - you stop right there.</summary>
        public void LaunchTick()
        {
            if (!m_LaunchFlying) return;
            if (m_Net == null || m_Net.Dead.Value || m_Net.Riding || m_CC == null || !m_CC.enabled) { m_LaunchFlying = false; return; }
            if (SinceLaunch > 0.15f && (m_CC.isGrounded || OnLadder()))
            {
                // landed: no sliding on, no knockback carrying you over the edge
                m_LaunchFlying = false;
                m_Push = Vector3.zero;
                m_SlideQueued = -1f;
                if (m_SlideOn) { m_SlideVel = Vector3.zero; EndSlide(); }
                return;
            }
            if (Paused || Chat.Open) return;
            float h = Binds.Axis(Bind.Right, Bind.Left), f = Binds.Axis(Bind.Forward, Bind.Back);
            var wish = transform.right * h + transform.forward * f;
            wish.y = 0f;
            if (wish.sqrMagnitude < 0.01f) return; // (no keys: the throw just carries on)
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            float keep = Mathf.Max(LaunchSteerSpeed, m_Push.magnitude);
            var target = wish.normalized * keep * wish.magnitude;
            m_Push = Vector3.MoveTowards(m_Push, target, LaunchSteerRate * Time.deltaTime);
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
