using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Paying prices in this mode's currency (wood, or DNA in DNA mode) and DNA mode's gambling machine.</summary>
    public partial class PlayerNet
    {
        /// <summary>Enough of this mode's currency (and stone, outside DNA mode) for a recipe.</summary>
        public bool CanAfford(Recipe r) => Count(Cfg.CurrencyItem) >= r.Wood && Count(Item.Stone) >= r.Stone;

        /// <summary>Server: take a recipe's price (shown in red in the bottom right).</summary>
        void ServerPay(Recipe r)
        {
            var cur = Cfg.CurrencyItem;
            InvOps.Remove(Inv, cur, r.Wood);
            InvOps.Remove(Inv, Item.Stone, r.Stone);
            if (r.Wood > 0) SpentRpc((byte)cur, r.Wood);
            if (r.Stone > 0) SpentRpc((byte)Item.Stone, r.Stone);
        }

        /// <summary>The GAMBLE button: everything in the machine's slots is the bet. The server rolls it and every screen plays it out.</summary>
        [Rpc(SendTo.Server)]
        public void GambleRpc(NetworkObjectReference machine)
        {
            if (Dead.Value || InSuddenDeath || !machine.TryGet(out var no) || !no.TryGetComponent(out Container c) || !c.IsGamble || !c.InReach(EyePos)) return;
            string why = GambleMachine.ServerGamble(c, this);
            if (why != null) Notify(why);
        }

        /// <summary>Everyone: the reels spin on the machine and land on the result (the server already decided it).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void GambleSpinRpc(NetworkObjectReference machine, bool win, int bet, int seed)
        {
            if (!machine.TryGet(out var no) || !no.TryGetComponent(out Container c)) return;
            var gm = GambleMachine.Of(c);
            if (gm != null) gm.Play(win, bet, seed, IsOwner);
        }
    }
}
