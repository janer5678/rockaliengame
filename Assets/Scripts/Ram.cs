using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Battering ram. Players push it (hold E next to it). Every few seconds it swings its log and smashes
    /// the enemy building piece directly in front of it.
    /// </summary>
    public class Ram : NetworkBehaviour
    {
        public static readonly List<Ram> All = new List<Ram>();

        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(Cfg.RamHp);
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> Strikes = new NetworkVariable<int>();

        public static readonly Vector3 HalfExtents = new Vector3(0.9f, 0.9f, 1.7f);
        static readonly Vector3 k_LogRest = new Vector3(0, 1.25f, 0.2f);

        Transform m_Log;
        float m_StrikeAnim;
        float m_NextStrike;
        float m_PushUntil;
        Vector3 m_PushDir;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            CreateVisual(transform, null, out m_Log);
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, HalfExtents.y + 0.1f, 0);
            bc.size = HalfExtents * 2f;
            Strikes.OnValueChanged += OnStrike;
            m_NextStrike = Time.time + Cfg.RamStrikeInterval;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Strikes.OnValueChanged -= OnStrike;
        }

        void OnStrike(int prev, int cur) => m_StrikeAnim = 1f;

        public static GameObject CreateVisual(Transform parent, Material ghost, out Transform log)
        {
            var root = new GameObject("visual");
            root.transform.SetParent(parent, false);
            var tr = root.transform;
            // chassis
            Art.Box(tr, Art.DarkWood, new Vector3(-0.75f, 0.55f, 0), new Vector3(0.18f, 0.18f, 3.2f));
            Art.Box(tr, Art.DarkWood, new Vector3(0.75f, 0.55f, 0), new Vector3(0.18f, 0.18f, 3.2f));
            Art.Box(tr, Art.DarkWood, new Vector3(0, 0.55f, 1.4f), new Vector3(1.6f, 0.16f, 0.16f));
            Art.Box(tr, Art.DarkWood, new Vector3(0, 0.55f, -1.4f), new Vector3(1.6f, 0.16f, 0.16f));
            // A-frame uprights
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Box(tr, Art.DarkWood, new Vector3(s * 0.75f, 1.2f, 0.9f), new Vector3(0.14f, 1.4f, 0.14f));
                Art.Box(tr, Art.DarkWood, new Vector3(s * 0.75f, 1.2f, -0.9f), new Vector3(0.14f, 1.4f, 0.14f));
                Art.Box(tr, Art.Wood, new Vector3(s * 0.45f, 2.05f, 0), new Vector3(0.9f, 0.1f, 3.3f), new Vector3(0, 0, s * -35f));
            }
            // wheels
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Art.Part(tr, Art.Cylinder, Art.DarkWood, new Vector3(sx * 0.9f, 0.4f, sz * 1.1f), new Vector3(0.8f, 0.08f, 0.8f), new Vector3(0, 0, 90));
            // the log
            var logGo = new GameObject("log");
            logGo.transform.SetParent(tr, false);
            logGo.transform.localPosition = k_LogRest;
            Art.Part(logGo.transform, Art.Cylinder, Art.Wood, Vector3.zero, new Vector3(0.45f, 1.7f, 0.45f), new Vector3(90, 0, 0));
            Art.Part(logGo.transform, Art.Cylinder, Art.Metal, new Vector3(0, 0, 1.75f), new Vector3(0.55f, 0.12f, 0.55f), new Vector3(90, 0, 0));
            Art.Box(logGo.transform, Art.Metal, new Vector3(0, 0, 1.95f), new Vector3(0.35f, 0.35f, 0.25f));
            log = logGo.transform;

            if (ghost != null)
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = ghost;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            return root;
        }

        void Update()
        {
            if (m_StrikeAnim > 0f)
            {
                m_StrikeAnim = Mathf.Max(0f, m_StrikeAnim - Time.deltaTime * 1.6f);
                // quick thrust forward, slow pull back
                float t = 1f - m_StrikeAnim;
                float k = t < 0.2f ? t / 0.2f : 1f - (t - 0.2f) / 0.8f;
                m_Log.localPosition = k_LogRest + Vector3.forward * (k * 0.9f);
            }

            if (!IsServer) return;

            if (Time.time < m_PushUntil) ServerMove();

            if (Time.time >= m_NextStrike)
            {
                var target = FindTarget();
                if (target != null)
                {
                    Strikes.Value++;
                    target.ServerDamage(Cfg.RamStrikeDamage);
                    m_NextStrike = Time.time + Cfg.RamStrikeInterval;
                }
                else m_NextStrike = Time.time + 0.25f;
            }
        }

        Structure FindTarget()
        {
            Vector3 c = transform.position + transform.forward * (HalfExtents.z + 0.9f) + Vector3.up * 1.3f;
            var hits = Physics.OverlapBox(c, new Vector3(0.7f, 1.0f, 0.9f), transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            Structure best = null;
            int bestPri = int.MaxValue;
            float bestDist = float.MaxValue;
            foreach (var h in hits)
            {
                var s = h.GetComponentInParent<Structure>();
                if (s == null || !s.IsSpawned || s.Team.Value == Team.Value) continue;
                // prefer walls/doors, then tables/stairs, then floors/foundations
                int pri = s.PType == PieceType.Wall || s.PType == PieceType.Doorway ? 0 : s.PType == PieceType.Foundation || s.PType == PieceType.Floor ? 2 : 1;
                float d = Vector3.Distance(transform.position, s.transform.position);
                if (pri < bestPri || (pri == bestPri && d < bestDist)) { best = s; bestPri = pri; bestDist = d; }
            }
            return best;
        }

        void ServerMove()
        {
            Vector3 fwd = transform.forward;
            float dot = Vector3.Dot(m_PushDir, fwd);
            Vector3 face = dot >= 0 ? m_PushDir : -m_PushDir;
            var targetRot = Quaternion.LookRotation(face, Vector3.up);
            var newRot = Quaternion.RotateTowards(transform.rotation, targetRot, Cfg.RamTurnSpeed * Time.deltaTime);
            float sign = dot >= 0 ? 1f : -1f;
            float speed = Mathf.Abs(dot) > 0.5f ? Cfg.RamSpeed : Cfg.RamSpeed * 0.3f;
            Vector3 newPos = transform.position + newRot * Vector3.forward * (sign * speed * Time.deltaTime);

            // ground snap
            if (Physics.Raycast(newPos + Vector3.up * 1.5f, Vector3.down, out var gh, 5f, ~0, QueryTriggerInteraction.Ignore) && !gh.collider.transform.IsChildOf(transform))
                newPos.y = gh.point.y;

            if (!Blocked(newPos, newRot)) transform.SetPositionAndRotation(newPos, newRot);
            else if (!Blocked(transform.position, newRot)) transform.rotation = newRot;
        }

        bool Blocked(Vector3 pos, Quaternion rot)
        {
            var hits = Physics.OverlapBox(pos + Vector3.up * (HalfExtents.y + 0.25f), HalfExtents - new Vector3(0.05f, 0.1f, 0.05f), rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.transform.IsChildOf(transform)) continue;
                if (h.GetComponentInParent<GroundMarker>() != null) continue;
                return true;
            }
            return false;
        }

        // ---------------- Server API ----------------
        public void ServerInit(int team) => Team.Value = (byte)team;

        public void ServerPush(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) return;
            m_PushDir = dir.normalized;
            m_PushUntil = Time.time + 0.25f;
        }

        public void ServerDamage(float dmg)
        {
            if (!IsSpawned) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0) NetworkObject.Despawn(true);
        }
    }

    /// <summary>Marks static ground colliders so placement/overlap tests can ignore them.</summary>
    public class GroundMarker : MonoBehaviour { }
}
