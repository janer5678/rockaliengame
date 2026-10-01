using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// DNA mode: the classic game, but trees and rocks give DNA instead of wood and stone (rocks a bit more, and there are
    /// fewer of them), and everything is bought with DNA. Each base has a gambling machine (GambleMachine.cs) next to the
    /// alien machine: bet DNA, 50/50 to double it.
    ///
    /// The currency is abstracted here so any price shown or charged reads right in every mode: a Recipe's Wood field is
    /// the price in Cfg.CurrencyItem (wood, or DNA in DNA mode) and its Stone field is extra stone (always 0 in DNA mode).
    /// </summary>
    public static partial class Cfg
    {
        /// <summary>DNA mode is on.</summary>
        public static bool DnaRules => Rules == GameRules.Dna;

        /// <summary>DNA mode: a rock gives this many times the DNA per hit that it would give in stone (trees give their wood as DNA).</summary>
        [Tune("DNA mode")] public static float DnaRockMul = 1.5f;
        /// <summary>DNA mode: every stone in a price costs this much DNA instead (wood is 1:1).</summary>
        [Tune("DNA mode")] public static float DnaStonePrice = 1.5f;
        /// <summary>DNA mode: the share of the usual rock nodes that spawn (rocks are rarer).</summary>
        [Tune("DNA mode")] public static float DnaRockShare = 0.45f;
        /// <summary>DNA mode: the gambling machine's chance to win (and double the bet).</summary>
        [Tune("DNA mode")] public static float GambleWinChance = 0.5f;

        public const string DnaDesc = "The classic game, but trees and rocks give DNA instead of wood and stone (rocks give more, and they're rarer). Everything costs DNA. A gambling machine next to your alien machine doubles your DNA - or takes it.";

        /// <summary>What prices are paid in: DNA in DNA mode, wood otherwise.</summary>
        public static Item CurrencyItem => DnaRules ? Item.Dna : Item.Wood;
        /// <summary>What upgrading a building piece costs: DNA in DNA mode, stone otherwise.</summary>
        public static Item UpgradeItem => DnaRules ? Item.Dna : Item.Stone;
        public static string CurrencyName => DnaRules ? "DNA" : "wood";
        public static string UpgradeName => DnaRules ? "DNA" : "stone";
        /// <summary>What upgrading a piece to stone costs, in UpgradeItem.</summary>
        public static int UpgradeCost(PieceType t) => DnaRules ? Mathf.RoundToInt(PieceUpgradeStone(t) * DnaStonePrice) : PieceUpgradeStone(t);

        /// <summary>A price in this mode's currency: in DNA mode wood + stone (stone at DnaStonePrice) all in DNA.</summary>
        public static Recipe DnaPriced(Recipe r)
        {
            if (!DnaRules) return r;
            r.Wood += Mathf.RoundToInt(r.Stone * DnaStonePrice);
            r.Stone = 0;
            return r;
        }

        /// <summary>"120 DNA" or "50 wood, 10 stone": what a recipe costs, for menus.</summary>
        public static string CostText(Recipe r) => DnaRules ? $"{r.Wood} DNA" : r.Wood + " wood" + (r.Stone > 0 ? ", " + r.Stone + " stone" : "");

        /// <summary>What a tree / rock hit gives: wood and stone, or DNA in DNA mode.</summary>
        public static Item GatherItem(Item yield) => DnaRules && (yield == Item.Wood || yield == Item.Stone) ? Item.Dna : yield;
        /// <summary>How much a hit gives: in DNA mode rocks give DnaRockMul times as much.</summary>
        public static int GatherCount(Item yield, int got) => DnaRules && yield == Item.Stone ? Mathf.RoundToInt(got * DnaRockMul) : got;
        /// <summary>Wood / stone handed out (airdrops, gifts) become DNA in DNA mode.</summary>
        public static ItemStack DnaSwap(ItemStack s) => DnaRules && (s.Id == Item.Wood || s.Id == Item.Stone) ? ItemStack.Of(Item.Dna, Mathf.Min(MaxStack(Item.Dna), GatherCount(s.Id, s.Count))) : s;
    }

    /// <summary>DNA's look: a glowing double helix.</summary>
    public static class DnaArt
    {
        static readonly Color k_StrandA = new Color(0.25f, 0.95f, 1f), k_StrandB = new Color(1f, 0.35f, 0.85f);
        static readonly Color[] k_Rungs = { new Color(1f, 0.9f, 0.25f), new Color(0.4f, 1f, 0.45f), new Color(1f, 0.55f, 0.2f), new Color(0.55f, 0.6f, 1f) };

        /// <summary>A double helix about `height` tall standing on the origin (the item model, the machine's topper...).</summary>
        public static Transform Helix(Transform parent, float height, int turnsTimes10 = 12)
        {
            var root = new GameObject("helix").transform;
            root.SetParent(parent, false);
            int n = turnsTimes10;
            float r = height * 0.2f, bead = height * 0.11f, step = height / (n - 1);
            for (int i = 0; i < n; i++)
            {
                float a = i * 34f;
                float rad = a * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad));
                float y = i * step + bead * 0.5f;
                Art.Part(root, Art.Sphere, k_StrandA, d * r + Vector3.up * y, Vector3.one * bead);
                Art.Part(root, Art.Sphere, k_StrandB, -d * r + Vector3.up * y, Vector3.one * bead);
                // the base pair between them, two colours
                var c = k_Rungs[i % k_Rungs.Length];
                Art.Box(root, c, d * r * 0.5f + Vector3.up * y, new Vector3(r, bead * 0.28f, bead * 0.28f), new Vector3(0, -a, 0));
                Art.Box(root, c * 0.75f + Color.white * 0.25f, -d * r * 0.5f + Vector3.up * y, new Vector3(r, bead * 0.28f, bead * 0.28f), new Vector3(0, -a, 0));
            }
            return root;
        }
    }
}
