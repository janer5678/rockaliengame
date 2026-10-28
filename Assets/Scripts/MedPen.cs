using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The MED PEN (2026-10-28): a tier 1 craft (Cfg.MedPenWood, every mode) that heals a berry's worth (Cfg.BerryHeal) at
    /// once. LMB / RMB with it in your hand: your left forearm comes up across the view, palm up, the pen swings over
    /// and is jabbed into it and held a moment, then both drop back (like Rust's syringe) - the heal lands at the jab
    /// (PlayerNet.ServerMedPen, in Emerald.Perks.cs with the other 2026-10-28 server bits). Stacks to 10.
    /// </summary>
    public partial class PlayerController
    {
        float m_InjectSendAt = -1f;

        /// <summary>The med pen in hand: click to jab it in (the heal goes through at the jab, ~0.45 s in).</summary>
        void HandleMedPen()
        {
            if (m_InjectSendAt >= 0f)
            {
                if (Time.time >= m_InjectSendAt) { m_InjectSendAt = -1f; m_Net.UseItemRpc(); }
                return;
            }
            if (!(Binds.Down(Bind.Attack) || Binds.Down(Bind.Aim)) || Time.time < m_NextEat) return;
            if (m_Net.Health.Value >= Cfg.MaxHealth) { Hud.Push("You're already at full health"); return; }
            m_NextEat = Time.time + 1.1f;
            m_VM.Inject();
            Sfx.Play2D(Sfx.Hiss, 0.45f);
            m_EatFlashUntil = Time.time + 0.9f; // (everyone else sees the arm come up: BodyAnimator.Act.Eat)
            m_InjectSendAt = Time.time + 0.45f;
        }
    }

    public partial class ViewModel
    {
        /// <summary>Test hook: freeze the med pen at this many seconds into its jab (-1: off).</summary>
        public static float DebugInjectE = -1f;
        public const float InjectTime = 0.9f;

        public void Inject() => m_InjectStart = Now;

        /// <summary>The med pen: held low in the right hand like the other small things; on use the left forearm comes up
        /// across the lower middle of the view, palm up, the right hand swings the pen over and jabs it into the forearm
        /// (a quick push in), holds it there, then both go back down.</summary>
        void PoseMedPen(Vector3 shared, Quaternion sharedRot)
        {
            float start = DebugInjectE >= 0f ? Now - DebugInjectE : m_InjectStart;
            float k = Mathf.Clamp01((Now - start) / InjectTime);
            // the pen in a stabbing grip: its body up through the fist, the needle out of the bottom (the hand's -Y)
            var rIdle = new Vector3(0.21f, -0.24f, 0.45f);
            var rIdleRot = Quaternion.Euler(-12f, -14f, -4f);
            if (k >= 1f)
            {
                Set(m_R, shared + rIdle, sharedRot * rIdleRot);
                AttachItemToRight(new Vector3(0, 0.17f, 0.05f), new Vector3(180f, 0, 0), 1f); // (the body up through the fist, the needle just out of the bottom)
                HideLeft();
                return;
            }
            // the left forearm comes up from the bottom left to lie across the lower view, palm up (the hand's +Z - the
            // knuckles - points right and a little away; rolled 180 so the palm's up; the forearm runs off to the lower left),
            // and goes back down at the end
            float up = Smooth(k / 0.22f) * (1f - Smooth((k - 0.8f) / 0.2f));
            var lHidden = new Vector3(-0.3f, -0.9f, 0.2f);
            var lUp = new Vector3(0.12f, -0.19f, 0.44f);
            Set(m_L, Vector3.Lerp(lHidden, lUp, up), Quaternion.Slerp(Quaternion.identity, sharedRot * Quaternion.Euler(-10f, 70f, 180f), up));
            // the right hand carries the pen over the forearm, needle down, jabs it in (a quick drop), holds, then lifts off
            float over = Smooth((k - 0.08f) / 0.25f) * (1f - Smooth((k - 0.78f) / 0.22f));
            float inK = Mathf.Clamp01((k - 0.36f) / 0.08f) * (1f - Smooth((k - 0.7f) / 0.08f));
            // (the fist points left and up, so the forearm runs off down to the right - the elbow out by the hip - and the
            // needle, out of the bottom of the fist, points down at the forearm)
            var rOver = new Vector3(0.05f, -0.02f, 0.4f);
            var pos = Vector3.Lerp(rIdle, rOver, over) + new Vector3(0, -0.05f, 0) * inK;
            var rot = Quaternion.Slerp(rIdleRot, Quaternion.Euler(-35f, -30f, 0f), over);
            Set(m_R, shared + pos, sharedRot * rot);
            AttachItemToRight(new Vector3(0, 0.17f, 0.05f), new Vector3(180f, 0, 0), 1f);
        }
    }
}
