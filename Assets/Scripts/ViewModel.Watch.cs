using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Someone else's hands, from what everyone knows about them: what they hold and what they're doing (PlayerNet.Action:
    /// drawing the bow, aiming, eating, sawing...) - used by the spectator's view (Spectator.cs) and the kill cam's replay
    /// of the killer's eyes (DeathReplay.cs). Swings and throws are their own triggers (Swing / Throw).
    /// </summary>
    public partial class ViewModel
    {
        /// <summary>The hands' state for a watched player: `act` has been going for `actFor` seconds (draws build up over it).</summary>
        public static State Watched(Item item, BodyAnimator.Act act, float actFor, bool crouch, bool ball, bool hasArrow, bool visible)
        {
            float build = Mathf.Clamp01(actFor / 0.6f);
            return new State
            {
                Item = item,
                Ball = ball,
                Visible = visible,
                Visible2 = true,
                Grounded = true,
                Crouch = crouch,
                HasArrow = hasArrow || item == Item.Bow,
                Draw = act == BodyAnimator.Act.BowDraw || act == BodyAnimator.Act.SpearAim ? build : 0f,
                SpearAim = act == BodyAnimator.Act.SpearAim,
                Aim = act == BodyAnimator.Act.Aim,
                RamCharge = act == BodyAnimator.Act.Ram ? build : 0f,
                Firing = act == BodyAnimator.Act.Saw,
                Loaded = true,
                Reload = -1f,
                Rounds = 6,
            };
        }

        /// <summary>A watched player's swing: the impact lands `ago` seconds after it started (the swing trigger everyone gets
        /// comes as it lands, so the animation is started that much earlier).</summary>
        public void WatchedSwing(Item item, float ago = 0f)
        {
            var st = Cfg.Melee(item);
            float impact = item == Item.Sword ? Mathf.Clamp(Cfg.SwordSwingTime * 0.36f, ImpactTime, 0.75f) : ImpactTime;
            Swing(Mathf.Max(0.35f, st.Cooldown), impact);
            m_SwingStart -= Mathf.Max(0f, ago);
        }
    }
}
