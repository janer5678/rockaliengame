using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// JONAH TEST MODE BRUH (GameRules.JonahBruh): Classic, with these changes - and only in this mode:
    /// - Armour (the POWER ITEMS one) costs JonahBruhArmorWood (250);
    /// - no stun trap, no death wand, no ladder: not craftable, not in airdrops, Fun or the notebook (Cfg.Removed);
    /// - no chainsaw: the BETTER HATCHET is in its place on the Trade Station (a steel hatchet that chops faster, 50 wood);
    /// - the hammer only takes walls up to stone (no metal or armoured walls), a stone upgrade costs JonahBruhStoneWood (150)
    ///   a piece, and stone pieces have JonahBruhStoneHpMul times their HP.
    /// </summary>
    public static partial class Cfg
    {
        public static bool JonahBruh => Rules == GameRules.JonahBruh;
        [Tune("Jonah Test Mode Bruh")] public static int JonahBruhArmorWood = 250, JonahBruhStoneWood = 150, BetterHatchetWood = 50;
        [Tune("Jonah Test Mode Bruh")] public static float JonahBruhStoneHpMul = 2f;
        /// <summary>The better hatchet: faster swings and more wood a hit than the hatchet.</summary>
        [Tune("Jonah Test Mode Bruh")] public static float BetterHatchetCooldown = 0.55f, BetterHatchetWoodGather = 24f, BetterHatchetStoneGather = 3f, BetterHatchetPlayerDamage = 16f, BetterHatchetStructureDamage = 14f;

        /// <summary>Taken out of Jonah Test Mode Bruh (only there).</summary>
        public static bool JonahBruhOut(Item i) => JonahBruh && (i == Item.BearTrap || i == Item.DeathWand || i == Item.Ladder || i == Item.Chainsaw);

        /// <summary>The highest tier the hammer takes a piece to: stone in Jonah Test Mode Bruh, armoured everywhere else.</summary>
        public static int HammerMaxTier => JonahBruh ? 1 : MaxFortify;

        /// <summary>Stone pieces are tougher in Jonah Test Mode Bruh.</summary>
        static float StoneHpMul => JonahBruh ? Mathf.Max(0.1f, JonahBruhStoneHpMul) : 1f;
    }
}
