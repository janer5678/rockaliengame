using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Locally simulated ballistic arrow. The shooter's copy reports hits to the server (client-side hit detection);
    /// everyone else's copy is purely visual.
    /// </summary>
    public class ArrowProjectile : MonoBehaviour
    {
        Vector3 m_Vel;
        PlayerNet m_Shooter;
        Transform m_ShooterRoot;
        bool m_Report, m_Stuck;
        float m_Life = 6f;

        public static void Spawn(Vector3 pos, Vector3 vel, PlayerNet shooter, bool report)
        {
            var go = new GameObject("Arrow");
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(vel));
            Art.Box(go.transform, Art.Wood, new Vector3(0, 0, -0.35f), new Vector3(0.03f, 0.03f, 0.75f));
            Art.Box(go.transform, Art.Stone, new Vector3(0, 0, 0.04f), new Vector3(0.07f, 0.07f, 0.12f), new Vector3(0, 0, 45));
            Art.Box(go.transform, Color.white, new Vector3(0, 0, -0.66f), new Vector3(0.13f, 0.01f, 0.12f));
            Art.Box(go.transform, Color.white, new Vector3(0, 0, -0.66f), new Vector3(0.01f, 0.13f, 0.12f));
            var a = go.AddComponent<ArrowProjectile>();
            a.m_Vel = vel;
            a.m_Shooter = shooter;
            a.m_ShooterRoot = shooter != null ? shooter.transform : null;
            a.m_Report = report;
        }

        void Update()
        {
            m_Life -= Time.deltaTime;
            if (m_Life <= 0) { Destroy(gameObject); return; }
            if (m_Stuck) return;

            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 step = m_Vel * dt;
            m_Vel += Vector3.down * Cfg.ArrowGravity * dt;
            float dist = step.magnitude;
            if (dist > 0.0001f)
            {
                var hits = Physics.RaycastAll(pos, step / dist, dist, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                foreach (var h in hits)
                {
                    if (m_ShooterRoot != null && h.collider.transform.IsChildOf(m_ShooterRoot)) continue;
                    if (h.collider.transform.IsChildOf(transform)) continue;
                    OnHit(h);
                    return;
                }
            }
            transform.position = pos + step;
            if (m_Vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(m_Vel);
        }

        void OnHit(RaycastHit h)
        {
            m_Stuck = true;
            m_Life = 5f;
            transform.position = h.point - transform.forward * 0.1f;
            var no = h.collider.GetComponentInParent<NetworkObject>();
            if (no != null) transform.SetParent(no.transform, true);

            if (m_Report && m_Shooter != null && m_Shooter.IsSpawned && no != null)
            {
                bool head = false;
                var p = no.GetComponent<PlayerNet>();
                if (p != null) head = h.point.y > p.transform.position.y + 1.4f;
                m_Shooter.ArrowHitRpc(no, h.point, head);
            }
        }
    }
}
