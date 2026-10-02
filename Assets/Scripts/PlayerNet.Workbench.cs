using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Buying from your team's workbench (Workbench.cs): everything that isn't crafted from the inventory.</summary>
    public partial class PlayerNet
    {
        /// <summary>A tile in the workbench menu was clicked: pay, then make it on the bench (or just apply it: armour, base upgrades).</summary>
        [Rpc(SendTo.Server)]
        public void WorkbenchBuyRpc(NetworkObjectReference bench, int recipe)
        {
            if (Dead.Value || InSuddenDeath || NetGame.Instance == null || NetGame.Instance.S == GameState.GameOver) return;
            if (!bench.TryGet(out var no) || !no.TryGetComponent(out Container c) || !c.IsWorkbench || !c.InReach(EyePos)) return;
            string why = ServerWorkbenchBuy(c, recipe);
            if (why != null) Notify(why);
        }

        /// <summary>Server: buy craft number `recipe` at workbench c. Returns why not (null = bought).</summary>
        string ServerWorkbenchBuy(Container c, int recipe)
        {
            if (c.Team.Value != Team.Value) return "That's the enemy's workbench";
            if (!Tutorial.AllowsFor(this, TutFeature.Workbench)) return "Not yet - the tutorial gets to the workbench soon";
            if (!Cfg.ValidCraftIndex(recipe)) return "You can't make that in this mode";
            var r = Cfg.CraftRecipe(recipe, Team.Value);
            if (Cfg.IsStarter(r.Output)) return $"Craft the {Cfg.ItemName(r.Output)} from your inventory";
            if (r.Output == Item.Armor && ArmorHp.Value >= Cfg.ArmorHp) return "You're already wearing full armour";
            if (r.Output == Item.HeavyArmor && ArmorHp.Value >= Cfg.HeavyArmorHp) return "You're already wearing heavy armour";
            if (r.Output == Item.FortifyBuff && Cfg.FortifyLevel(Team.Value) >= Cfg.MaxFortify) return "Your walls are already refined - fully fortified";
            if (r.Output == Item.WoodGenBuff && Cfg.WoodGenLevel(Team.Value) >= Cfg.MaxWoodGen) return "Your wood gen is already maxed out";
            if (!CanAfford(r)) return $"Not enough {Cfg.CurrencyName} for the {r.Name}";
            bool noItem = Cfg.BenchNoItem(r.Output);
            if (!noItem && Workbench.ServerBusy(c)) return "The workbench is still making something";

            ServerPay(r);
            if (noItem)
            {
                ServerFinishCraft(r); // armour on / fortify / wood gen up (PlayerNet.Modes.cs)
                WorkbenchNoiseRpc(c.NetworkObject, (byte)r.Output);
                return null;
            }
            Workbench.ServerStart(c, this, r);
            return null;
        }

        /// <summary>Everyone: the workbench is making something - sawdust and tool noise, then it's on the bench.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void WorkbenchCraftRpc(NetworkObjectReference bench, byte item, float secs)
        {
            if (!bench.TryGet(out var no) || !no.TryGetComponent(out Container c)) return;
            var w = Workbench.Of(c);
            if (w != null) w.Play((Item)item, secs);
            if (IsOwner) Hud.Push($"Making the {Cfg.ItemName((Item)item)}...");
        }

        /// <summary>Everyone: armour or a base upgrade was bought at the workbench - just a noise to go with it.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void WorkbenchNoiseRpc(NetworkObjectReference bench, byte item)
        {
            if (!bench.TryGet(out var no) || !no.TryGetComponent(out Container c)) return;
            var w = Workbench.Of(c);
            if (w != null) w.PlayNoItem((Item)item);
        }
    }
}
