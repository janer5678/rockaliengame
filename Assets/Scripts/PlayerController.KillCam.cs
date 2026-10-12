using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>The kill cam: how long, after you're killed, the camera looks at who did it (then the replay).</summary>
        [Tune("Player")] public static float KillCamTime = 1.6f;
    }

    /// <summary>
    /// The kill cam: killed by someone, the camera leaves your eyes and glides over to them - stopping a few metres off,
    /// a little above and to one side, looking them in the face, then slowly closing in - for Cfg.KillCamTime, while the
    /// HUD says who it was, what they had and how much health they've left (Hud.Death.cs). Then the replay
    /// (DeathReplay.cs): the last seconds again, out of the killer's eyes with their weapon in hand, their crosshair and
    /// the damage they did, until you see yourself die and fall in slow motion (jump skips it). Then the usual death
    /// screen. Not after a fall, the void or your own C4 (nobody to look at). Respawning ends it whenever that comes.
    /// </summary>
    public partial class PlayerController
    {
        float m_DiedAt = -1f;
        Vector3 m_KillCamFrom;
        Quaternion m_KillCamFromRot;
        bool m_Replay, m_KcDone;
        float m_ReplayT = -1f;
        // how far from their head the camera may be (kept clear of walls: pulled in at once, eased back out)
        float m_KcClear = -1f;

        /// <summary>Who the kill cam is looking at right now (null: it isn't on).</summary>
        public PlayerNet KillCamTarget { get; private set; }
        /// <summary>The kill cam's replay is playing (out of the killer's eyes), and how far into it it is (s).</summary>
        public bool KillCamReplay => m_ReplayT >= 0f;
        public float KillCamReplayT => m_ReplayT;

        /// <summary>LateUpdate: runs the kill cam while it's on (true: it has the camera).</summary>
        bool TickKillCam()
        {
            KillCamTarget = null;
            m_ReplayT = -1f;
            if (!m_Net.Dead.Value)
            {
                if (m_DiedAt >= 0f) DeathReplay.End();
                m_DiedAt = -1f; m_Replay = false; m_KcDone = false;
                return false;
            }
            if (m_DiedAt < 0f)
            {
                m_DiedAt = Time.time;
                m_KillCamFrom = m_Cam.transform.position;
                m_KillCamFromRot = m_Cam.transform.rotation;
                m_KcClear = -1f;
            }
            float t = Time.time - m_DiedAt;
            if (m_KcDone || m_Net.KilledBy.Value == 0) return false;
            PlayerNet k = null;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.NetworkObjectId == m_Net.KilledBy.Value) { k = p; break; }
            if (k == null || k == m_Net) { DeathReplay.End(); return false; }
            if (t > Cfg.KillCamTime)
            {
                // the replay: the last seconds again, out of the killer's eyes
                if (!m_Replay) { m_Replay = true; if (!DeathReplay.Begin(m_Net, k)) m_KcDone = true; }
                if (Binds.Down(Bind.Jump)) m_KcDone = true;
                float rt = t - Cfg.KillCamTime;
                if (m_KcDone || !DeathReplay.Play(rt, m_Cam)) { DeathReplay.End(); m_KcDone = true; return false; }
                KillCamTarget = k;
                m_ReplayT = rt;
                return true;
            }
            KillCamTarget = k;
            var head = k.transform.position + Vector3.up * 1.55f;
            var away = m_KillCamFrom - head;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -k.transform.forward;
            away.Normalize();
            var side = Vector3.Cross(Vector3.up, away);
            // (after the glide over, it keeps easing in closer and round a little, so the shot never sits still)
            float drift = Smooth01(Mathf.Clamp01((t - 0.85f) / Mathf.Max(0.1f, Cfg.KillCamTime - 0.85f)));
            var to = head + away * Mathf.Lerp(3.4f, 2.5f, drift) + side * Mathf.Lerp(1.1f, 0.6f, drift) + Vector3.up * Mathf.Lerp(0.55f, 0.35f, drift);
            // (not through a wall: stop short of whatever's between them and the spot - with a ball the size of the view,
            // not a thin line, and ignoring bodies and ragdolls (your own falling one flicked the camera in and out every
            // frame); pulled in straight away, eased back out, so it never jumps back and forth)
            float clear = KillCamClear(head, to);
            m_KcClear = m_KcClear < 0f || clear < m_KcClear ? clear : Mathf.MoveTowards(m_KcClear, clear, Time.deltaTime * 3f);
            var toDir = to - head;
            if (toDir.magnitude > m_KcClear) to = head + toDir.normalized * m_KcClear;
            float e = Smooth01(Mathf.Clamp01(t / 0.85f)); // (a quick glide over)
            var pos = Vector3.Lerp(m_KillCamFrom, to, e);
            // (the glide over doesn't cut through walls either: anywhere along it, kept on their side of whatever's in the way)
            if (e < 1f)
            {
                var pd = pos - head;
                float pc = KillCamClear(head, pos);
                if (pd.magnitude > pc) pos = head + pd.normalized * pc;
            }
            var rot = Quaternion.Slerp(m_KillCamFromRot, Quaternion.LookRotation(head - pos), Smooth01(Mathf.Clamp01(t / 0.55f)));
            m_Cam.transform.SetPositionAndRotation(pos, rot);
            m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, 52f, Time.deltaTime * 6f);
            return true;
        }

        /// <summary>How far from `head` towards `to` the kill cam can go before it would be in something solid (players,
        /// their hit boxes and ragdolls don't count).</summary>
        static float KillCamClear(Vector3 head, Vector3 to)
        {
            var d = to - head;
            float len = d.magnitude;
            if (len < 0.01f) return len;
            d /= len;
            float best = len;
            const int mask = ~((1 << PlayerNet.HitboxLayer) | (1 << 2)); // (2: Ignore Raycast - the ragdolls)
            foreach (var h in Physics.SphereCastAll(head, 0.2f, d, len, mask, QueryTriggerInteraction.Ignore))
            {
                if (h.distance <= 0f || h.collider.GetComponentInParent<PlayerNet>() != null) continue;
                best = Mathf.Min(best, Mathf.Max(0.3f, h.distance - 0.05f));
            }
            return best;
        }
    }
}
