using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>The kill cam: how long, after you're killed, the camera shows who did it.</summary>
        [Tune("Player")] public static float KillCamTime = 3.5f;
    }

    /// <summary>
    /// The kill cam: killed by someone, the camera leaves your eyes and glides over to them - stopping a few metres off,
    /// a little above and to one side, looking them in the face - and holds there for Cfg.KillCamTime, while the HUD says
    /// who it was, what they had and how much health they've left (Hud.cs, under YOU DIED). Then it's back to the usual
    /// death screen. Not after a fall, the void or your own C4 (nobody to look at).
    /// </summary>
    public partial class PlayerController
    {
        float m_DiedAt = -1f;
        Vector3 m_KillCamFrom;
        Quaternion m_KillCamFromRot;

        /// <summary>Who the kill cam is looking at right now (null: it isn't on).</summary>
        public PlayerNet KillCamTarget { get; private set; }

        /// <summary>LateUpdate: runs the kill cam while it's on (true: it has the camera).</summary>
        bool TickKillCam()
        {
            KillCamTarget = null;
            if (!m_Net.Dead.Value) { m_DiedAt = -1f; return false; }
            if (m_DiedAt < 0f)
            {
                m_DiedAt = Time.time;
                m_KillCamFrom = m_Cam.transform.position;
                m_KillCamFromRot = m_Cam.transform.rotation;
            }
            float t = Time.time - m_DiedAt;
            if (t > Cfg.KillCamTime || m_Net.KilledBy.Value == 0) return false;
            PlayerNet k = null;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.NetworkObjectId == m_Net.KilledBy.Value) { k = p; break; }
            if (k == null) return false;
            KillCamTarget = k;
            var head = k.transform.position + Vector3.up * 1.55f;
            var away = m_KillCamFrom - head;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -k.transform.forward;
            away.Normalize();
            var side = Vector3.Cross(Vector3.up, away);
            var to = head + away * 3.4f + side * 1.1f + Vector3.up * 0.55f;
            // (not through a wall: stop short of whatever's between them and the spot)
            if (Physics.Linecast(head, to, out var hit, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) to = hit.point + (head - to).normalized * 0.3f;
            float e = Smooth01(Mathf.Clamp01(t / 0.9f));
            var pos = Vector3.Lerp(m_KillCamFrom, to, e);
            var rot = Quaternion.Slerp(m_KillCamFromRot, Quaternion.LookRotation(head - pos), Smooth01(Mathf.Clamp01(t / 0.6f)));
            m_Cam.transform.SetPositionAndRotation(pos, rot);
            m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, 52f, Time.deltaTime * 6f);
            return true;
        }
    }
}
