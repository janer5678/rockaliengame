using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Base upgrades: Fortify All Walls (every piece your team has built, and builds from then on, goes up to stone, then
    /// metal) and, in Auto Wood, the wood gen (your wood machine makes wood faster). They aren't crafted:
    /// press E on your own UPGRADE STATION (UpgradeStation.cs, the green-plus terminal to the left of the alien machine) and the UPGRADES screen opens (Hud.Upgrades.cs) - laid out like the crafting
    /// screen, with your inventory on the left - and each one has an UPGRADE button. They're there from the start (no
    /// workbench needed), cost what CHANGE VALUES says (Fortify Stone / Metal Wood, Wood Gen 1 / 2 Wood), are for
    /// the whole team for the rest of the match, and the server checks everything (BaseUpgradeRpc). When one is bought,
    /// every client sees the team's station celebrate (UpgradeFxRpc -> UpgradeStation.Celebrate: green plus signs, a screen
    /// flash, a squash-and-stretch bounce).
    /// Which modes have them: Fortify wherever there's a POWER ITEMS menu (Arsenal, Auto Wood), the wood gen in Auto Wood
    /// only. Builder has no bases (no stations), so its Fortify stays a POWER ITEMS craft.
    /// </summary>
    public static partial class Cfg
    {
        static readonly Item[] k_BaseUpgrades = { Item.FortifyBuff, Item.WoodGenBuff };

        /// <summary>How far from your upgrade station you can be and still buy upgrades (the screen closes further away).</summary>
        public const float UpgradeReach = 6.5f;

        /// <summary>The upgrade station, in the alien machine's frame (+x is the LEFT as you look at the machine from your
        /// spawn, +z out towards the spawn): beside the machine on the bedrock, mirroring Auto Wood's wood machine on the right.</summary>
        public static readonly Vector3 UpgradeStationLocal = new Vector3(2.2f, 0f, 0.1f);
        /// <summary>It's turned this far towards the spawn (degrees), so its screen faces you as you come out.</summary>
        public const float UpgradeStationTurn = -10f;
        public static Vector3 UpgradeStationPos(int team) => MachinePos(team) + Quaternion.LookRotation(-BackDir(team)) * UpgradeStationLocal;
        public static Quaternion UpgradeStationRot(int team) => Quaternion.LookRotation(-BackDir(team)) * Quaternion.Euler(0, UpgradeStationTurn, 0);

        /// <summary>This mode has base upgrades, and an upgrade station in every base (E on it opens UPGRADES).</summary>
        public static bool HasBaseUpgrades => PowerMenu && !Builder;

        /// <summary>Is this upgrade in this mode?</summary>
        public static bool BaseUpgradeOn(Item id) => id == Item.FortifyBuff ? HasBaseUpgrades : id == Item.WoodGenBuff && AutoWood && HasBaseUpgrades;

        /// <summary>The upgrades this mode has, in the order the screen lists them.</summary>
        public static void BaseUpgrades(List<Item> into)
        {
            into.Clear();
            foreach (var id in k_BaseUpgrades) if (BaseUpgradeOn(id)) into.Add(id);
        }

        /// <summary>How many times a team has bought it (0 = not yet).</summary>
        public static int BaseUpgradeLevel(Item id, int team) => id == Item.WoodGenBuff ? WoodGenLevel(team) : FortifyLevel(team);
        public static int BaseUpgradeMax(Item id) => id == Item.WoodGenBuff ? MaxWoodGen : MaxFortify;
        public static bool BaseUpgradeMaxed(Item id, int team) => BaseUpgradeLevel(id, team) >= BaseUpgradeMax(id);

        /// <summary>What the next level costs (it goes up each time; team -1: the first level). In DNA mode the price is DNA.</summary>
        public static Recipe BaseUpgradeRecipe(Item id, int team)
        {
            int lvl = BaseUpgradeLevel(id, team);
            if (id == Item.WoodGenBuff) return DnaPriced(new Recipe { Output = id, Count = 1, Wood = lvl >= 1 ? WoodGen2Wood : WoodGen1Wood });
            return DnaPriced(new Recipe { Output = Item.FortifyBuff, Count = 1, Wood = lvl >= 1 ? FortifyMetalWood : FortifyStoneWood });
        }

        /// <summary>What the next level does (or that it's maxed out).</summary>
        public static string BaseUpgradeBlurb(Item id, int team)
        {
            int lvl = BaseUpgradeLevel(id, team);
            if (id == Item.WoodGenBuff)
            {
                if (lvl >= MaxWoodGen) return $"maxed out: {WoodGenRate(lvl)} wood a second";
                return $"level {lvl + 1}: your base makes {WoodGenRate(lvl + 1)} wood a second (now {WoodGenRate(lvl)})";
            }
            if (lvl >= MaxFortify) return "your pieces are all metal - fully fortified";
            return $"all your team's pieces from {TierName(lvl).ToLower()} to {TierName(lvl + 1).ToLower()} ({lvl + 2} ram hits each)";
        }

        /// <summary>Where the team's upgrade stands now, in a few words: "Stone walls", "12 wood a second".</summary>
        public static string BaseUpgradeNow(Item id, int team)
        {
            int lvl = BaseUpgradeLevel(id, team);
            return id == Item.WoodGenBuff ? $"{WoodGenRate(lvl)} wood a second" : $"{TierName(lvl)} walls";
        }

        /// <summary>Is a player of this team standing at p close enough to its upgrade station to buy upgrades?</summary>
        public static bool AtOwnStation(int team, Vector3 p)
        {
            if (team < 0 || team >= TeamCount) return false;
            var m = UpgradeStationPos(team);
            p.y = m.y = 0f;
            return Vector3.Distance(p, m) <= UpgradeReach;
        }
    }

    public partial class PlayerNet
    {
        /// <summary>UPGRADE pressed on the UPGRADES screen: the server checks you're at your own upgrade station, it's in this
        /// mode and not maxed out and you can pay; then it pays, upgrades the whole team and every client sees the station celebrate.</summary>
        [Rpc(SendTo.Server)]
        public void BaseUpgradeRpc(Item id)
        {
            var g = NetGame.Instance;
            if (Dead.Value || g == null || InSuddenDeath || g.S == GameState.GameOver || !Cfg.BaseUpgradeOn(id)) return;
            int team = Team.Value;
            if (!Cfg.AtOwnStation(team, transform.position)) { Notify("Upgrades are bought at your upgrade station (E on it - the green plus, left of your alien machine)"); return; }
            if (Cfg.BaseUpgradeMaxed(id, team)) { Notify(id == Item.WoodGenBuff ? "Your wood gen is already maxed out" : "Your walls are already metal - fully fortified"); return; }
            var r = Cfg.BaseUpgradeRecipe(id, team);
            if (!CanAfford(r)) { Notify($"Not enough {Cfg.CurrencyName} for {Cfg.ItemName(id)}"); return; }
            ServerPay(r);
            ServerApplyBaseUpgrade(id);
            UpgradedRpc((byte)id, (byte)Cfg.BaseUpgradeLevel(id, team));
            UpgradeFxRpc((byte)team);
        }

        /// <summary>Everyone: this team's upgrade station plays its "upgraded!" show (plus signs, flash, bounce).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        void UpgradeFxRpc(byte team) => UpgradeStation.Celebrate(team);

        /// <summary>Server: the upgrade happens (already paid for) and everyone's told.</summary>
        void ServerApplyBaseUpgrade(Item id)
        {
            var g = NetGame.Instance;
            if (g == null) return;
            int team = Team.Value;
            if (id == Item.WoodGenBuff)
            {
                int lvl = g.ServerWoodGenUp(team);
                g.Broadcast($"{Cfg.TeamLabel(team)} upgraded their wood gen to level {lvl}: {Cfg.WoodGenRate(lvl)} wood a second!");
            }
            else
            {
                int n = g.ServerFortify(team);
                g.Broadcast($"{Cfg.TeamLabel(team)} fortified all their walls - {n} piece{(n == 1 ? "" : "s")} turned to {Cfg.TierName(g.FortifyLevelOf(team)).ToUpper()}!");
            }
        }

        /// <summary>The buyer: it worked (a chime; the UPGRADES screen shows the new level).</summary>
        [Rpc(SendTo.Owner)]
        void UpgradedRpc(byte item, byte level)
        {
            var id = (Item)item;
            Hud.Push($"Upgraded {Cfg.ItemName(id)} to level {level}");
            Sfx.Play2D(Sfx.Unlock, 0.7f, 0f);
            UpgradesBought++;
        }

        /// <summary>Test hook: upgrades this client has been told it bought.</summary>
        public static int UpgradesBought;
    }
}
