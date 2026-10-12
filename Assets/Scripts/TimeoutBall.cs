using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The thrown TIMEOUT BALL: a big bouncy ball that bounces off everything for a few seconds. The thrower's copy is the
    /// one that counts: the first player it touches - the thrower too, once it's left their hand and bounced back - is
    /// reported (PlayerNet.TimeoutHitRpc) and locked inside a ball (PlayerNet.WackyLooks draws it). Everyone else's copy
    /// just bounces about the same way.
    /// </summary>
    public class TimeoutBall : MonoBehaviour
    {
        PlayerNet m_Thrower;
        bool m_Report;
        Vector3 m_Vel;
        float m_Life = 5f, m_Born;
        int m_Bounces;
        const float Radius = 0.32f;

        public static void Spawn(Vector3 pos, Vector3 vel, PlayerNet thrower, bool report)
        {
            var go = new GameObject("TimeoutBall");
            go.transform.position = pos;
            BuildLook(go.transform);
            var b = go.AddComponent<TimeoutBall>();
            b.m_Thrower = thrower;
            b.m_Report = report;
            b.m_Vel = vel;
            b.m_Born = Time.time;
        }

        /// <summary>A beach-ball: white with bright stripes going round it.</summary>
        public static void BuildLook(Transform t)
        {
            Art.Part(t, Art.Sphere, Color.white, Vector3.zero, Vector3.one * Radius * 2f);
            var cols = new[] { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.85f, 0.2f), new Color(0.25f, 0.55f, 1f) };
            for (int i = 0; i < 3; i++)
                Art.Part(t, Art.Sphere, cols[i], Vector3.zero, new Vector3(Radius * 2.02f, Radius * 2.02f, Radius * 0.7f), new Vector3(0, i * 60f, 0));
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Life -= dt;
            if (m_Life <= 0f || transform.position.y < -50f)
            {
                if (m_Report && m_Thrower != null && m_Thrower.IsSpawned) m_Thrower.TimeoutMissRpc();
                FxParticle.Puff(transform.position, new Color(1f, 1f, 1f, 0.6f), 0.6f);
                Destroy(gameObject);
                return;
            }
            // someone in reach: they're timed out (not the thrower until it's been out of their hand a moment)
            bool selfOk = Time.time - m_Born > 0.35f && m_Bounces > 0;
            foreach (var c in Physics.OverlapSphere(transform.position, Radius + 0.1f, 1 << PlayerNet.HitboxLayer, QueryTriggerInteraction.Collide))
            {
                var p = c.GetComponentInParent<PlayerNet>();
                if (p == null || p.Dead.Value || p.Prisoned) continue;
                if (p == m_Thrower && !selfOk) continue;
                if (m_Report && m_Thrower != null && m_Thrower.IsSpawned) m_Thrower.TimeoutHitRpc(p.NetworkObject);
                Sfx.Play(Sfx.Boing, transform.position, 1f, 0.05f, 60f);
                Destroy(gameObject);
                return;
            }
            var pos = transform.position;
            m_Vel += Vector3.down * 9.81f * dt;
            var step = m_Vel * dt;
            float dist = step.magnitude;
            if (dist > 1e-5f)
            {
                RaycastHit? first = null;
                foreach (var h in Physics.SphereCastAll(pos, Radius, step / dist, dist, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.GetComponentInParent<PlayerNet>() != null) continue; // (players are found by their hit boxes, above)
                    if (h.distance <= 0f) continue;
                    if (first == null || h.distance < first.Value.distance) first = h;
                }
                if (first != null)
                {
                    var h = first.Value;
                    transform.position = pos + step / dist * Mathf.Max(0f, h.distance - 0.01f);
                    // a bounce: off the surface, losing a bit each time
                    m_Vel = Vector3.Reflect(m_Vel, h.normal) * 0.78f;
                    if (m_Vel.magnitude < 2f && h.normal.y > 0.6f) m_Vel = Vector3.up * 3.5f + new Vector3(m_Vel.x, 0, m_Vel.z); // (it keeps bouncing)
                    m_Bounces++;
                    if (m_Bounces <= 8) Sfx.Play(Sfx.Boing, h.point, 0.5f, 0.15f, 40f);
                    if (m_Report) DecoyBasePart.Report(h.collider);
                    return;
                }
            }
            transform.position = pos + step;
            transform.Rotate(new Vector3(m_Vel.z, 0, -m_Vel.x) * 60f * dt, Space.World);
        }
    }
}
