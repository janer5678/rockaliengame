using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Base upgrades: Fortify All Walls (every piece your team has built, and builds from then on, goes up to stone, then
    /// metal, then armoured) and, in Auto Wood, the wood gen (the first level builds your wood machine, the next two make it faster). They aren't crafted:
    /// press E on your own UPGRADE STATION (UpgradeStation.cs, the orange-arrow terminal to the left of the alien machine) and the UPGRADES screen opens (Hud.Upgrades.cs) - laid out like the crafting
    /// screen, with your inventory on the left - and each one has an UPGRADE button. They're there from the start (no
    /// workbench needed), cost what CHANGE VALUES says (Fortify Stone / Metal / Armoured Wood, Wood Gen Build / 1 / 2 Wood), are for
    /// the whole team for the rest of the match, and the server checks everything (BaseUpgradeRpc). When one is bought,
    /// every client sees the team's station celebrate (UpgradeFxRpc -> UpgradeStation.Celebrate: orange up arrows, a screen
    /// flash, a squash-and-stretch bounce).
    /// Which modes have them: Fortify wherever there's a POWER ITEMS menu (Arsenal, Auto Wood), the wood gen in Auto Wood
    /// only. Builder has no bases (no stations), so its Fortify stays a POWER ITEMS craft.
    /// While Cfg.Upgrade10WallsEverywhere is on (JonahMode.cs) every mode with a station but the tutorial has Upgrade 10
    /// Walls in Fortify's place (Cfg.Upgrade10Walls: the wall picker -> UpgradeWallsRpc), as Jonah mode always does.
    ///
    /// THE HAMMER (Cfg.HammerUpgrades, on): there's no upgrade station in any base any more (only the tutorial keeps its
    /// own, for its station step). Walls are upgraded one at a time with the HAMMER, a starter item (under the building
    /// plan in the crafting list): aim it at one of your team's pieces and it lights up green with what the next tier
    /// costs (HammerStoneWood / HammerMetalWood / HammerArmouredWood a piece); LMB upgrades it (HammerUpgradeRpc). At
    /// anything else it's a weapon: half the sword's damage. The wood machine is in every base from the start and speeds
    /// up by itself over the match (Cfg.AutoWoodGen, NetGame.ServerTickAutoWood) instead of being bought.
    /// Set HammerUpgrades false to bring the upgrade stations, Upgrade 10 Walls / Fortify and the bought wood gen back.
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

        /// <summary>THE HAMMER: true = no upgrade stations (but the tutorial's), walls upgraded one by one with the hammer,
        /// and the wood machine there from the start, speeding up over the match. false = the stations as before.</summary>
        public const bool HammerUpgrades = true;
        /// <summary>The hammer is in this mode (a starter item): where the upgrade stations used to be, but the tutorial.</summary>
        public static bool HammerOn => HammerUpgrades && PowerMenu && !Builder && !Tutorial;
        /// <summary>The wood machine is there from the start and levels up by itself over the match (not bought).</summary>
        public static bool AutoWoodGen => HammerUpgrades && AutoWood && !Tutorial;
        /// <summary>What the hammer charges to take one piece up to stone, metal, armoured (in the mode's currency).</summary>
        [Tune("Hammer")] public static int HammerStoneWood = 50, HammerMetalWood = 100, HammerArmouredWood = 250;
        [Tune("Hammer")] public static int HammerWood = 100;
        /// <summary>A piece that's just been hit (by anyone) can't be hammered up a tier for this many seconds.</summary>
        [Tune("Hammer")] public static float HammerHitLock = 5f;
        /// <summary>The hammer as a weapon: half the sword's damage, a quicker swing.</summary>
        [Tune("Hammer")] public static float HammerBodyDamage = 47.5f, HammerHeadDamage = 75f, HammerSwingTime = 0.9f, HammerRange = 2.6f;
        /// <summary>What the hammer charges to take a piece from `tier` to the next one (0: it can't go higher).</summary>
        public static int HammerCost(int tier) => tier >= MaxFortify ? 0 : DnaPrice(tier >= 2 ? HammerArmouredWood : tier == 1 ? HammerMetalWood : HammerStoneWood);
        static int DnaPrice(int wood) => DnaPriced(new Recipe { Wood = wood }).Wood;

        /// <summary>This mode has base upgrades, and an upgrade station in every base (E on it opens UPGRADES).</summary>
        public static bool HasBaseUpgrades => (PowerMenu || Tutorial) && !Builder && (!HammerUpgrades || Tutorial); // (the tutorial teaches the station: Tutorial.cs, the "upgrade" step)

        /// <summary>Is this upgrade in this mode?</summary>
        public static bool BaseUpgradeOn(Item id) => id == Item.FortifyBuff ? HasBaseUpgrades : id == Item.WoodGenBuff && AutoWood && HasBaseUpgrades;

        /// <summary>The upgrades this mode has, in the order the screen lists them.</summary>
        public static void BaseUpgrades(List<Item> into)
        {
            into.Clear();
            foreach (var id in k_BaseUpgrades) if (BaseUpgradeOn(id)) into.Add(id);
        }

        /// <summary>How many times a team has bought it (0 = not yet).</summary>
        public static int BaseUpgradeLevel(Item id, int team) => id == Item.WoodGenBuff ? WoodGenLevel(team) : Upgrade10Walls ? 0 : FortifyLevel(team);
        public static int BaseUpgradeMax(Item id) => id == Item.WoodGenBuff ? MaxWoodGen : Upgrade10Walls ? 0 : MaxFortify;
        /// <summary>(Upgrade 10 Walls is never maxed: it can be bought as long as there's a piece left to upgrade.)</summary>
        public static bool BaseUpgradeMaxed(Item id, int team) => Upgrade10Walls && id == Item.FortifyBuff ? UpgradablePieces(team) == 0 : BaseUpgradeLevel(id, team) >= BaseUpgradeMax(id);

        /// <summary>What the next level costs (it goes up each time; team -1: the first level). In DNA mode the price is DNA.</summary>
        public static Recipe BaseUpgradeRecipe(Item id, int team)
        {
            int lvl = BaseUpgradeLevel(id, team);
            if (id == Item.WoodGenBuff) return JonahPriced(DnaPriced(new Recipe { Output = id, Count = 1, Wood = WoodGenWood(lvl + 1) }));
            if (Upgrade10Walls) return Upgrade10WallsRecipe;
            return DnaPriced(new Recipe { Output = Item.FortifyBuff, Count = 1, Wood = FortifyWoodFor(lvl + 1) });
        }

        /// <summary>What the next level does (or that it's maxed out).</summary>
        public static string BaseUpgradeBlurb(Item id, int team)
        {
            int lvl = BaseUpgradeLevel(id, team);
            if (id == Item.WoodGenBuff)
            {
                if (lvl >= MaxWoodGen) return $"maxed out: {WoodGenRate(lvl)} wood a second";
                if (lvl == 0) return $"builds a wood machine in your base: {WoodGenRate(1)} wood a second";
                return $"level {lvl + 1}: your base makes {WoodGenRate(lvl + 1)} wood a second (now {WoodGenRate(lvl)})";
            }
            if (Upgrade10Walls) return UpgradablePieces(team) == 0 ? "build some walls first (or they're all armoured)" : $"fly round your base and pick {WallsPerUpgrade} pieces: each goes up a tier";
            if (lvl >= MaxFortify) return "your pieces are all armoured - fully fortified";
            return $"all your team's pieces from {TierName(lvl).ToLower()} to {TierName(lvl + 1).ToLower()} ({lvl + 2} ram hits each)";
        }

        /// <summary>Where the team's upgrade stands now, in a few words: "Stone walls", "12 wood a second", "no wood machine yet".</summary>
        public static string BaseUpgradeNow(Item id, int team)
        {
            int lvl = BaseUpgradeLevel(id, team);
            return id == Item.WoodGenBuff ? (lvl == 0 ? "no wood machine yet" : $"{WoodGenRate(lvl)} wood a second") : Upgrade10Walls ? "no walls to upgrade" : $"{TierName(lvl)} walls";
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
        /// <summary>The hammer: LMB on one of your team's pieces takes it up a tier, for HammerCost of the tier it's at.</summary>
        [Rpc(SendTo.Server)]
        public void HammerUpgradeRpc(NetworkObjectReference target)
        {
            var g = NetGame.Instance;
            if (!Cfg.HammerOn || Dead.Value || HeldItem != Item.Hammer || InSuddenDeath || g == null || g.S == GameState.GameOver) return;
            if (!target.TryGet(out var no) || !no.TryGetComponent(out Structure s)) return;
            if (Vector3.Distance(s.transform.position, transform.position) > Cfg.BuildRange + 4f) return;
            ServerHammer(s);
        }

        /// <summary>Server: one piece up a tier with the hammer (the checks, the price, the upgrade). True if it went up.</summary>
        public bool ServerHammer(Structure s)
        {
            if (s == null || !s.IsSpawned || s.Team.Value != Team.Value || !s.Upgradable) return false;
            if (s.Tier.Value >= Cfg.MaxFortify) { Notify("That's already armoured"); return false; }
            if (s.UpgradeLockLeft > 0f) { Notify($"Under attack - you can upgrade it in {Mathf.CeilToInt(s.UpgradeLockLeft)} s"); return false; }
            int cost = Cfg.HammerCost(s.Tier.Value);
            var r = new Recipe { Output = Item.FortifyBuff, Count = 1, Wood = cost }; // (Wood is the currency's slot: DNA in DNA mode)
            if (!CanAfford(r)) { Notify($"Need {cost} {Cfg.CurrencyName} to upgrade it to {Cfg.TierName(s.Tier.Value + 1).ToLower()}"); return false; }
            ServerPay(r);
            s.ServerUpgrade(s.Tier.Value + 1);
            Fx.Server(FxKind.StructureHit, s.transform.position + Vector3.up * 1.2f, Vector3.up);
            HammeredRpc((byte)s.Tier.Value);
            return true;
        }

        /// <summary>Server (bots): the hammer on the piece of ours nearest our machine that can still go up a tier.</summary>
        public bool ServerHammerNearest()
        {
            var ids = Cfg.NearestUpgradable(Team.Value, 1);
            if (ids.Length == 0 || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(ids[0], out var no) || !no.TryGetComponent(out Structure s)) return false;
            return ServerHammer(s);
        }

        /// <summary>The hammerer: it worked (a clink and the upgrade chime).</summary>
        [Rpc(SendTo.Owner)]
        void HammeredRpc(byte tier)
        {
            Sfx.Play2D(Sfx.Unlock, 0.55f, 0.05f);
            HammerUpgradesDone++;
        }

        /// <summary>Test hook: pieces this client has upgraded with the hammer.</summary>
        public static int HammerUpgradesDone;

        /// <summary>UPGRADE pressed on the UPGRADES screen: the server checks you're at your own upgrade station, it's in this
        /// mode and not maxed out and you can pay; then it pays, upgrades the whole team and every client sees the station celebrate.</summary>
        [Rpc(SendTo.Server)]
        public void BaseUpgradeRpc(Item id)
        {
            var g = NetGame.Instance;
            if (Dead.Value || g == null || InSuddenDeath || g.S == GameState.GameOver || !Cfg.BaseUpgradeOn(id)) return;
            int team = Team.Value;
            if (!Tutorial.AllowsFor(this, TutFeature.Station)) { Notify("Not yet - the tutorial gets to the upgrade station soon"); return; }
            if (Cfg.Upgrade10Walls && id == Item.FortifyBuff) { Notify("Upgrade 10 Walls: press UPGRADE and pick the pieces"); return; } // (JonahMode.cs: WallPicker -> UpgradeWallsRpc)
            if (id == Item.FortifyBuff && !Tutorial.AllowsFor(this, TutFeature.Fortify)) { Notify("Not yet - the wood gen first, then the tutorial gets to your walls"); return; }
            if (!Cfg.AtOwnStation(team, transform.position)) { Notify("Upgrades are bought at your upgrade station (E on it - the orange arrow, left of your alien machine)"); return; }
            if (Cfg.BaseUpgradeMaxed(id, team)) { Notify(id == Item.WoodGenBuff ? "Your wood gen is already maxed out" : "Your walls are already armoured - fully fortified"); return; }
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
                g.Broadcast(lvl <= 1 ? $"{Cfg.TeamLabel(team)} built a wood machine: {Cfg.WoodGenRate(lvl)} wood a second!"
                    : $"{Cfg.TeamLabel(team)} upgraded their wood gen to level {lvl}: {Cfg.WoodGenRate(lvl)} wood a second!");
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
