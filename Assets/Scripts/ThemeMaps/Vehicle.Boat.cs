using UnityEngine;

namespace RockGame
{
    /// <summary>THEME MAPS: the Beach boat. Drives like the car but only on open water (it stops at the shore) and floats.</summary>
    public partial class Vehicle
    {
        public const byte Boat = 3;
        public bool IsBoat => Kind.Value == Boat;

        void DriveBoat(float dt)
        {
            var pc = PlayerController.Local;
            float f = 0, s = 0;
            bool jump = false, sprint = false;
            float look = m_Yaw;
            if (pc != null) pc.GetDriveInput(out f, out s, out jump, out sprint, out look);
            if (f > 0.1f) m_Speed = Mathf.MoveTowards(m_Speed, ThemeMaps.BoatSpeed, 6f * dt);
            else if (f < -0.1f) m_Speed = Mathf.MoveTowards(m_Speed, -ThemeMaps.BoatSpeed * 0.35f, 8f * dt);
            else m_Speed = Mathf.MoveTowards(m_Speed, 0, 2.5f * dt);
            m_Yaw += s * 75f * Mathf.Clamp01(Mathf.Abs(m_Speed) / 3f + 0.35f) * dt;
            FloatStep(dt);
        }

        void IdleBoat(float dt)
        {
            m_Speed = Mathf.MoveTowards(m_Speed, 0, 3f * dt);
            FloatStep(dt);
        }

        /// <summary>Moves along the water (never onto land) and bobs on the surface.</summary>
        void FloatStep(float dt)
        {
            var fwd = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward;
            var move = fwd * m_Speed;
            var ahead = transform.position + fwd * (Mathf.Sign(m_Speed) * 1.6f) + move * dt;
            if (Mathf.Abs(m_Speed) > 0.01f && !ThemeMaps.WaterAt(ahead.x, ahead.z))
            {
                // ran aground: stop
                m_Speed = 0f;
                move = Vector3.zero;
            }
            float targetY = ThemeMaps.WaterY - 0.1f;
            m_CC.Move(move * dt + Vector3.up * (targetY - transform.position.y));
            float t = Time.time;
            transform.rotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 2f - m_Speed * 0.25f, m_Yaw, Mathf.Sin(t * 1.7f) * 2.5f);
        }

        /// <summary>A little wooden boat with an outboard motor (its propeller spins like the car's fan).</summary>
        static Transform BuildBoat(Transform t)
        {
            var hull = new Color(0.55f, 0.38f, 0.22f);
            var trim = new Color(0.9f, 0.9f, 0.85f);
            Art.Box(t, hull, new Vector3(0, 0.35f, 0), new Vector3(1.6f, 0.2f, 3.2f));
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, hull, new Vector3(k * 0.78f, 0.62f, 0), new Vector3(0.1f, 0.55f, 3.2f));
                Art.Box(t, trim, new Vector3(k * 0.78f, 0.92f, 0), new Vector3(0.14f, 0.06f, 3.22f));
                Art.Box(t, hull, new Vector3(k * 0.42f, 0.62f, 1.9f), new Vector3(0.1f, 0.55f, 1.0f), new Vector3(0, k * -38f, 0)); // bow
            }
            Art.Box(t, hull, new Vector3(0, 0.62f, -1.58f), new Vector3(1.6f, 0.55f, 0.1f));
            Art.Box(t, new Color(0.4f, 0.27f, 0.15f), new Vector3(0, 0.62f, -0.3f), new Vector3(1.5f, 0.08f, 0.5f)); // seat
            // motor + propeller
            Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, 0.95f, -1.75f), new Vector3(0.4f, 0.45f, 0.35f));
            Art.Box(t, new Color(0.3f, 0.3f, 0.32f), new Vector3(0, 0.3f, -1.8f), new Vector3(0.1f, 0.8f, 0.1f));
            var prop = new GameObject("prop").transform;
            prop.SetParent(t, false);
            prop.localPosition = new Vector3(0, -0.05f, -1.85f);
            for (int k = 0; k < 3; k++) Art.Box(prop, new Color(0.75f, 0.75f, 0.7f), Vector3.zero, new Vector3(0.08f, 0.4f, 0.03f), new Vector3(0, 0, k * 60f));
            return prop;
        }
    }
}
