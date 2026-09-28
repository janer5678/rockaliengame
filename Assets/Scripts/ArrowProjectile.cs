using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Locally simulated ballistic arrow or thrown spear. The shooter's copy reports hits to the server (client-side hit detection);
    /// everyone else's copy is purely visual.
    /// </summary>
    public class ArrowProjectile : MonoBehaviour
    {
        Vector3 m_Vel;
        PlayerNet m_Shooter;
        Transform m_ShooterRoot;
        bool m_Report, m_Stuck, m_Spear;
        float m_Life = 6f;
        float m_Gravity = Cfg.ArrowGravity;
        float m_Power = 1f;

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
            a.m_Power = Mathf.Clamp01(vel.magnitude / Mathf.Max(1f, Cfg.ArrowSpeed));
        }

        /// <summary>Thrown spear. The shooter's copy reports where it landed / who it hit; the networked dropped
        /// spear (or the spear stuck in the victim) then replaces this local copy.</summary>
        public static void SpawnSpear(Vector3 pos, Vector3 vel, PlayerNet shooter, bool report)
        {
            var go = new GameObject("ThrownSpear");
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(vel));
            ItemModels.CreateSpearTipForward(go.transform);
            var a = go.AddComponent<ArrowProjectile>();
            a.m_Vel = vel;
            a.m_Shooter = shooter;
            a.m_ShooterRoot = shooter != null ? shooter.transform : null;
            a.m_Report = report;
            a.m_Spear = true;
            a.m_Gravity = Cfg.SpearGravity;
            a.m_Life = 8f;
            a.m_Power = Mathf.Clamp01(vel.magnitude / Mathf.Max(1f, Cfg.SpearThrowSpeed));
        }

        void Update()
        {
            m_Life -= Time.deltaTime;
            if (m_Life <= 0)
            {
                // spear flew for ages without hitting anything: let the server drop it where it is
                if (m_Spear && !m_Stuck && m_Report && m_Shooter != null && m_Shooter.IsSpawned)
                    m_Shooter.SpearLandRpc(false, default, transform.position, m_Vel.normalized);
                Destroy(gameObject);
                return;
            }
            if (m_Stuck) return;

            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 step = m_Vel * dt;
            m_Vel += Vector3.down * m_Gravity * dt;
            float dist = step.magnitude;
            if (dist > 0.0001f)
            {
                var hits = Physics.RaycastAll(pos, step / dist, dist, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                RaycastHit? first = null;
                foreach (var h in hits)
                {
                    if (m_ShooterRoot != null && h.collider.transform.IsChildOf(m_ShooterRoot)) continue;
                    if (h.collider.transform.IsChildOf(transform)) continue;
                    first = h;
                    break;
                }
                // hit assist: a near miss on a player still counts (unless something solid is in front)
                if (Cfg.ProjectileAssist > 0f && (first == null || first.Value.collider.GetComponentInParent<PlayerNet>() == null))
                {
                    float limit = first != null ? first.Value.distance : dist;
                    foreach (var h in Physics.SphereCastAll(pos, Cfg.ProjectileAssist, step / dist, dist, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.distance <= 0f || h.distance > limit) continue;
                        var p = h.collider.GetComponentInParent<PlayerNet>();
                        if (p == null || p.Dead.Value || (m_ShooterRoot != null && p.transform == m_ShooterRoot)) continue;
                        first = h;
                        limit = h.distance;
                    }
                }
                if (first != null)
                {
                    OnHit(first.Value);
                    return;
                }
            }
            transform.position = pos + step;
            if (m_Vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(m_Vel);
        }

        void OnHit(RaycastHit h)
        {
            var no = h.collider.GetComponentInParent<NetworkObject>();
            var dir = m_Vel.normalized;
            bool reporter = m_Report && m_Shooter != null && m_Shooter.IsSpawned;
            if (reporter) PredictHit(no, h, dir);

            if (m_Spear)
            {
                if (reporter)
                {
                    if (no != null) m_Shooter.SpearLandRpc(true, no, h.point, dir);
                    else m_Shooter.SpearLandRpc(false, default, h.point, dir);
                }
                Destroy(gameObject);
                return;
            }
            m_Stuck = true;
            m_Life = 5f;
            transform.position = h.point - transform.forward * 0.1f;
            if (no != null) transform.SetParent(no.transform, true);
            if (no == null || no.GetComponent<PlayerNet>() == null) Sfx.Play(Sfx.Thud, h.point, 0.4f);
            if (reporter && no != null) m_Shooter.ArrowHitRpc(no, h.point, dir);
        }

        /// <summary>Shooter-side instant feedback (the server confirms kills).</summary>
        void PredictHit(NetworkObject no, RaycastHit h, Vector3 dir)
        {
            if (no == null || !no.TryGetComponent(out PlayerNet p) || p == m_Shooter || p.Dead.Value) return;
            bool head = p.IsHeadshot(h.point);
            float dmg = (m_Spear ? Cfg.SpearThrowDamage : Cfg.ArrowPlayerDamage) * m_Power * (head ? Cfg.HeadshotMul : 1f);
            Fx.Blood(h.point, dir, head);
            Fx.DamageNumber(h.point, dmg, head);
            Hud.HitMarker(false, head);
        }
    }
}
