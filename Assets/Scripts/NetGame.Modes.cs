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


        /// <summary>Fortify: every wooden grid piece the team has placed turns to stone at full health. Returns how many.</summary>
        public int ServerFortify(int team)
        {
            int n = 0;
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || s.Team.Value != team || s.Tier.Value != 0 || !s.Upgradable) continue;
                s.ServerUpgrade();
                n++;
            }
            return n;
        }

        void ServerTickModes(double now)
        {
            bool playing = S == GameState.PreBall || S == GameState.BallLive;

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
                    var stack = Cfg.GiftStack(Cfg.AllAirdropItems[Random.Range(0, Cfg.AllAirdropItems.Length)]);
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
