using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The game modes' server rules: Arsenal / Builder power items (fortify), Builder's planted ball, and Fun / Fun Random's free item every few seconds.
    /// </summary>
    public partial class NetGame
    {
        /// <summary>Fun modes: when everyone gets their next item (server time, -1 = not handing out).</summary>
        public readonly NetworkVariable<double> NextFunItem = new NetworkVariable<double>(-1);


        /// <summary>How many times each team has bought Fortify All Walls (2 bits a team: 0 never, 1 stone, 2 metal, 3 armoured).</summary>
        public readonly NetworkVariable<int> FortifyLevels = new NetworkVariable<int>();
        public int FortifyLevelOf(int team) => (FortifyLevels.Value >> (team * 2)) & 3;

        /// <summary>Auto Wood: each team's wood gen upgrade level (2 bits a team: 0 no wood machine yet, 1-3).</summary>
        public readonly NetworkVariable<int> WoodGenLevels = new NetworkVariable<int>();
        public int WoodGenLevelOf(int team) => (WoodGenLevels.Value >> (team * 2)) & 3;

        public int ServerWoodGenUp(int team)
        {
            int lvl = Mathf.Min(Cfg.MaxWoodGen, WoodGenLevelOf(team) + 1);
            WoodGenLevels.Value = (WoodGenLevels.Value & ~(3 << (team * 2))) | (lvl << (team * 2));
            return lvl;
        }

        /// <summary>
        /// Fortify: each buy takes the team a step further - every grid piece they've placed goes up to stone, then metal,
        /// then refined (at full health). Returns how many pieces changed.
        /// </summary>
        public int ServerFortify(int team)
        {
            int tier = Mathf.Min(Cfg.MaxFortify, FortifyLevelOf(team) + 1);
            FortifyLevels.Value = (FortifyLevels.Value & ~(3 << (team * 2))) | (tier << (team * 2));
            int n = 0;
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || s.Team.Value != team || s.Tier.Value >= tier || !s.Upgradable) continue;
                s.ServerUpgrade(tier);
                n++;
            }
            return n;
        }

        // ---------------- Auto Wood: a pile of wood grows at every base ----------------

        readonly int[] m_WoodPile = { -1, -1, -1, -1 };
        double m_NextWoodTick = -1;

        /// <summary>Where a team's wood pile is: on the bedrock, beside the machine.</summary>
        static Vector3 WoodPilePos(int team) => Cfg.WoodTrayPos(team); // out of the wood machine's chute

        void ServerTickAutoWood(double now, bool playing)
        {
            if (!Cfg.AutoWood || !playing) { m_NextWoodTick = -1; return; }
            if (m_NextWoodTick < 0) m_NextWoodTick = now + 1.0;
            if (now < m_NextWoodTick) return;
            m_NextWoodTick += 1.0;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                int add = Mathf.Max(0, Cfg.WoodGenRate(WoodGenLevelOf(t)));
                if (add == 0) continue;
                // top up this base's pile - it keeps stacking past 1000 (it splits into stacks when it's picked up) - or start
                // a new one if it was picked up
                int idx = -1;
                for (int i = 0; i < Items.Count; i++) if (Items[i].Id == m_WoodPile[t]) { idx = i; break; }
                if (idx >= 0 && Items[idx].Stack.Count + add <= ushort.MaxValue)
                {
                    var it = Items[idx];
                    it.Stack = it.Stack.WithCount(it.Stack.Count + add);
                    it.From = it.Pos; // no toss animation: it just grows
                    Items[idx] = it;
                    m_ItemBorn[it.Id] = now; // a growing pile never despawns
                }
                else
                {
                    var at = WoodPilePos(t);
                    if (idx >= 0) at += Vector3.Cross(Vector3.up, Cfg.BackDir(t)) * 0.7f; // that one's full: the next one goes beside it
                    m_WoodPile[t] = ServerDropItem(ItemStack.Of(Item.Wood, add), at, -Cfg.BackDir(t), at);
                }
            }
        }

        void ServerTickModes(double now)
        {
            bool playing = S == GameState.PreBall || S == GameState.BallLive;
            ServerTickAutoWood(now, playing);

            // Fun / Fun Random: a free item every few seconds
            if (Cfg.FunRules && playing)
            {
                if (NextFunItem.Value < 0) NextFunItem.Value = now; // the first one straight away
                if (now >= NextFunItem.Value)
                {
                    NextFunItem.Value = now + Mathf.Max(1f, Cfg.FunItemInterval);
                    ServerGiveFunItems();
                }
            }
            else if (NextFunItem.Value >= 0) NextFunItem.Value = -1;
        }

        void ServerGiveFunItems()
        {
            if (Cfg.Rules == GameRules.Fun)
            {
                // everybody gets the same thing
                var all = Cfg.AllFunItems;
                var stack = Cfg.GiftStack(all[Random.Range(0, all.Count)]);
                foreach (var p in PlayerNet.All) if (!p.Dead.Value) ServerGiftTo(p, stack);
                Broadcast($"FUN: everyone got {Cfg.ItemName(stack.Id)}{(stack.Count > 1 ? " x" + stack.Count : "")}!");
            }
            else
            {
                // everybody gets something different
                foreach (var p in PlayerNet.All)
                {
                    if (p.Dead.Value) continue;
                    var stack = Cfg.GiftStack(Cfg.PickAirdropItem(Cfg.AllAirdropItems)); // (by Airdrop Rarity)
                    ServerGiftTo(p, stack);
                    p.NotifyPublic($"FUN RANDOM: you got {Cfg.ItemName(stack.Id)}{(stack.Count > 1 ? " x" + stack.Count : "")}!");
                }
            }
        }

        void ServerGiftTo(PlayerNet p, ItemStack stack)
        {
            int left = p.ServerGiveFromRight(stack.Id, stack.Count, stack.Data);
            // no room: it lands at their feet
            if (left > 0) ServerDropItem(ItemStack.Of(stack.Id, left, stack.Data), p.transform.position + p.transform.forward, p.transform.forward, p.EyePos);
        }
    }
}
