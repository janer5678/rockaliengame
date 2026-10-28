using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The wood machine quietly makes DOUBLE (2026-10-28): whatever its label says (Cfg.WoodGenRate: 10 / 24 / 80 a second
    /// by level), it piles up AutoWoodSecretMul times that (NetGame.ServerTickAutoWood) - and nothing on screen says so.
    /// The ball in your machine no longer buffs anything (it used to double the machine, with a "BALL IN YOUR BASE" box top
    /// left, and buff gathering): BallDoublesWood is always false now and the box is never drawn; its test hooks stay.
    /// </summary>
    public static partial class Cfg
    {
        /// <summary>How many times its label the wood machine really makes (not a tunable: it isn't told).</summary>
        public const int AutoWoodSecretMul = 2;
        /// <summary>(Old name, kept for the tests: the machine's real multiplier.)</summary>
        public static int BallWoodMul => AutoWoodSecretMul;

        /// <summary>Does the ball double `team`'s wood machine right now? Never, since 2026-10-28 (the machine always makes double instead).</summary>
        public static bool BallDoublesWood(int team) => false;

        /// <summary>`team`'s wood machine's wood a second right now: double its label (Lucky Block's machine is its own, and honest).</summary>
        public static int WoodPerSecondNow(int team) => Mathf.Max(0, WoodGenRate(WoodGenLevel(team))) * (LuckyBlock ? 1 : AutoWoodSecretMul);
    }

    public partial class Hud
    {
        /// <summary>(tests) when the old "ball in your base: wood machine x2" box was last drawn - never, now.</summary>
        public static float BallWoodShownAt = -10f;
        public static string BallWoodShown = "";
        public static Rect BallWoodRect;
    }
}
