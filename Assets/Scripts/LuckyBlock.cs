using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// LUCKY BLOCK MODE (GameRules.LuckyBlock): Bedwars (no ball - smash the enemy machines, respawn from your cryochamber,
    /// last team standing) with emeralds instead of trees:
    /// - no trees, logs or berry bushes anywhere, and no airdrops;
    /// - everyone starts with LuckyStartWood wood, and the wood machine in every base spams wood (LuckyWoodPerSecond a
    ///   second, from when the wall drops) - the building plan and the battering ram (LuckyRamWood) are all you craft;
    /// - emerald spawners (Emerald.cs): the ones in between the bases, nearer the middle (an emerald every LuckyMiddleEvery
    ///   s) - none by any base;
    /// - the emerald shop beside your machine sells just the one thing: a LUCKY BLOCK (LuckyBlockPrice emeralds) - a random
    ///   airdrop item, from the whole pool of them (LuckyBlockLoot);
    /// - the glass wall drops after LuckyWallDrop seconds.
    /// </summary>
    public static partial class Cfg
    {
        public static bool LuckyBlock => Rules == GameRules.LuckyBlock;
        /// <summary>The modes with emerald spawners and an emerald shop: Emerald mode and Lucky Block mode.</summary>
        public static bool HasEmeralds => Emerald || LuckyBlock;

        /// <summary>A lucky block's price (emeralds).</summary>
        [Tune("Lucky Block mode")] public static int LuckyBlockPrice = 1;
        /// <summary>A battering ram's price (wood) - the one thing besides the building plan you can craft.</summary>
        [Tune("Lucky Block mode")] public static int LuckyRamWood = 250;
        /// <summary>Seconds between emeralds on the spawners (in between the bases - there's none by a base).</summary>
        [Tune("Lucky Block mode")] public static float LuckyMiddleEvery = 20f;
        /// <summary>The wood machine's wood a second (from when the wall drops), and the wood everyone starts with.</summary>
        [Tune("Lucky Block mode")] public static int LuckyWoodPerSecond = 25, LuckyStartWood = 2000;
        /// <summary>Seconds before the glass wall drops.</summary>
        [Tune("Lucky Block mode")] public static float LuckyWallDrop = 30f;

        /// <summary>Seconds before the glass wall drops (the host's setting; Lucky Block mode: its own).</summary>
        public static float WallDropDelay => LuckyBlock ? Mathf.Max(5f, LuckyWallDrop) : BallDropDelay;

        static readonly List<Item> s_LuckyPool = new List<Item>();

        /// <summary>Every item a lucky block can be: every airdrop item there is (the unused ones too - not the ones taken
        /// out of the game, nor the airdrop's wood: the wood machine has plenty).</summary>
        public static List<Item> LuckyBlockPool
        {
            get
            {
                s_LuckyPool.Clear();
                foreach (var i in AllAirdropItems) if (i != Item.Wood && InAirdropPool(i) && !s_LuckyPool.Contains(i)) s_LuckyPool.Add(i);
                return s_LuckyPool;
            }
        }

        /// <summary>What a lucky block gives: any one of LuckyBlockPool, all as likely.</summary>
        public static ItemStack LuckyBlockLoot()
        {
            var pool = LuckyBlockPool;
            var id = pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : Item.C4;
            if (id == Item.Helmet) return ItemStack.Of(Item.Helmet, 1, 1);
            return ItemStack.Of(id, 1, Mathf.Clamp(MaxData(id), 0, 255));
        }
    }

    /// <summary>The lucky block's look (the emerald shop's one row, and over the shop in Lucky Block mode): a golden cube
    /// with a darker rim and a white question mark on every side.</summary>
    public static class LuckyBlockModel
    {
        public static readonly Color Gold = new Color(1f, 0.78f, 0.16f), Rim = new Color(0.78f, 0.5f, 0.08f);

        /// <summary>A lucky block `size` across, its bottom at the origin.</summary>
        public static void Build(Transform t, float size, bool glow = false)
        {
            var gold = glow ? Workbench.Glow(Gold, 1.1f) : Art.Mat(Gold);
            Art.Box(t, Gold, new Vector3(0, size * 0.5f, 0), Vector3.one * size, default, false, gold);
            // the darker rim: a thin frame along the twelve edges
            float e = size * 0.09f, h = size * 0.5f;
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    Art.Box(t, Rim, new Vector3(a * (h - e * 0.45f), h, b * (h - e * 0.45f)), new Vector3(e, size + 0.002f, e));
                    Art.Box(t, Rim, new Vector3(0, h + a * (h - e * 0.45f), b * (h - e * 0.45f)), new Vector3(size + 0.002f, e, e));
                    Art.Box(t, Rim, new Vector3(a * (h - e * 0.45f), h + b * (h - e * 0.45f), 0), new Vector3(e, e, size + 0.002f));
                }
            // a "?" in white pixels on the four sides and the top (3 x 5 pixels)
            string[] q = { "###", "..#", ".##", "...", ".#." };
            float px = size * 0.13f;
            var white = glow ? Workbench.Glow(Color.white, 1.4f) : Art.Mat(Color.white);
            for (int face = 0; face < 5; face++)
            {
                var rot = face < 4 ? Quaternion.Euler(0, face * 90f, 0) : Quaternion.Euler(-90f, 0, 0);
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 3; c++)
                    {
                        if (q[r][c] != '#') continue;
                        var local = new Vector3((c - 1) * px, (2 - r) * px, h + 0.004f);
                        Art.Box(t, Color.white, rot * local + new Vector3(0, h, 0), new Vector3(px, px, 0.01f), rot.eulerAngles, false, white);
                    }
            }
        }
    }
}
