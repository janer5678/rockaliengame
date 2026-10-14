using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt11 -host -solo [-rules autowood | jonahbruh] -shotdir DIR: Armour first on the Advanced Trade
        /// Station (TRADE STATION 2) for 250 (Jonah Test Mode Bruh keeps its 150), no stun trap anywhere, the death wand 5000
        /// (last on the list; Jonah Test Mode Bruh: airdrops only), and the airdrop items picked to start with: the portal
        /// gun, rocket, tree camo, Extreme Speed Juice, lifting stick, one-use knockback stick, tree breaker and Scribblenauts
        /// notebook, 12.5% each.
        /// </summary>
        IEnumerator Prompt11Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            if (g != null) g.TimerPaused.Value = true;
            int team = me.Team.Value;

            // ---- Armour: first on TRADE STATION 2, 250 (Jonah: 150)
            {
                var t2 = new List<int>();
                Cfg.AddTier(t2, 2);
                int armour = Cfg.CraftIndexOf(Item.HeavyArmor);
                int price = armour >= 0 ? Cfg.CraftRecipe(armour, team).Wood : -1;
                Check(armour >= 0 && t2.Count > 0 && t2[0] == armour, $"Armour is first on the Advanced Trade Station (first: {(t2.Count > 0 ? Cfg.CraftRecipe(t2[0], team).Name : "nothing")})");
                Check(price == (Cfg.JonahBruh ? 150 : 250) && Cfg.CraftTier(Item.HeavyArmor) == 2, $"...for {price} wood");
            }

            // ---- no stun trap
            {
                Check(Cfg.RecipeIndex(Item.BearTrap) < 0 && Cfg.CraftIndexOf(Item.BearTrap) < 0 && Cfg.Removed(Item.BearTrap), "no stun trap to craft");
                Check(!Cfg.AllFunItems.Contains(Item.BearTrap) && !Cfg.AirdropLoot.Contains(Item.BearTrap), "...nor in Fun or the airdrops");
                var all = new List<int>();
                Cfg.CraftList(all, 2);
                bool listed = false;
                foreach (var i in all) listed |= Cfg.CraftRecipe(i, team).Output == Item.BearTrap;
                Check(!listed, "...and not on any Trade Station's list");
            }

            // ---- the death wand: 5000, the last thing on the list (Jonah: only from airdrops)
            {
                var all = new List<int>();
                Cfg.CraftList(all, 2);
                var last = all.Count > 0 ? Cfg.CraftRecipe(all[all.Count - 1], team) : default;
                if (Cfg.JonahBruh) Check(Cfg.RecipeIndex(Item.DeathWand) < 0 && Cfg.AirdropLoot.Contains(Item.DeathWand), "Jonah Test Mode Bruh: the death wand still only comes in airdrops");
                else Check(Cfg.DeathWandWood == 5000 && last.Output == Item.DeathWand && last.Wood == 5000, $"the death wand costs 5000, last on the list ({last.Output} {last.Wood})");
            }

            // ---- the airdrop items picked to start with
            if (!Cfg.AirdropTest)
            {
                var want = new List<Item> { Item.PortalGun, Item.RocketLauncher, Item.TreeCamo, Item.SpeedJuice, Item.StickOfTruth, Item.KnockStick1, Item.TreeBreaker, Item.Scribble };
                var loot = Cfg.AirdropLoot;
                var wrong = new List<string>(); var missing = new List<string>();
                foreach (var it in loot) if (!want.Contains(it) && !(Cfg.JonahBruh && it == Item.DeathWand)) wrong.Add(it.ToString());
                foreach (var it in want) if (!loot.Contains(it)) missing.Add(it.ToString());
                Check(wrong.Count == 0 && missing.Count == 0,
                    $"picked by default: those 8 ({loot.Count}){(wrong.Count > 0 ? " - extra: " + string.Join(" ", wrong) : "")}{(missing.Count > 0 ? " - missing: " + string.Join(" ", missing) : "")}");
                if (!Cfg.JonahBruh)
                {
                    bool even = true;
                    foreach (var it in want) even &= Mathf.Abs(Cfg.AirdropChance(it, loot) - 0.125f) < 0.001f;
                    Check(even, $"12.5% each ({Cfg.AirdropChance(Item.PortalGun, loot) * 100f:0.#}% the portal gun)");
                }
                bool onlyThose = true;
                for (int i = 0; i < 200; i++) { var c = NetGame.RollAirdropCrate(); if (c.Count > 0 && !want.Contains(c[0].Id) && c[0].Id != Item.DeathWand) onlyThose = false; }
                Check(onlyThose, "200 airdrop crates rolled: only those come out");
            }

            // ---- the bag with both Trade Stations down: TRADE STATION 2 starts with Armour
            {
                float yaw = Cfg.SpawnYaw(team);
                pc.LocalTeleport(Cfg.SpawnPos(team), yaw);
                yield return new WaitForSeconds(0.4f);
                foreach (var tier in new[] { 1, 2 })
                {
                    var id = tier == 1 ? Item.Workbench : Item.Workbench2;
                    if (Workbench.ForTeam(team, tier) != null) continue;
                    me.ServerGive(id, 1);
                    yield return Hold(me, id);
                    me.PlaceDeployableRpc((byte)id, Workbench.DefaultPos(team, tier), yaw);
                    yield return new WaitForSeconds(0.6f);
                }
                Check(Cfg.BenchTier(team) == 2, $"both Trade Stations down (tier {Cfg.BenchTier(team)})");
                me.ServerGive(Cfg.BuildItem, 6000);
                yield return BagRows(pc);
                Hud.TestScrollTo = 100000f;
                for (int i = 0; i < 3; i++) yield return null;
                float bottom = Hud.CraftScrollMax;
                // scroll till Armour is in view
                for (float s = 0; s <= bottom + 1f; s += 60f)
                {
                    Hud.TestScrollTo = s;
                    for (int i = 0; i < 3; i++) yield return null;
                    yield return new WaitForEndOfFrame();
                    if (RowState(Item.HeavyArmor) != "missing") break;
                }
                Check(RowState(Item.HeavyArmor) == "ok", $"Armour in the bag: {RowState(Item.HeavyArmor)}");
                yield return Snap("prompt11_trade_station_2");
                Hud.TestScrollTo = 100000f;
                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();
                if (!Cfg.JonahBruh) Check(RowState(Item.DeathWand) == "ok", $"the death wand at the bottom: {RowState(Item.DeathWand)}");
                Check(RowState(Item.BearTrap) == "missing", "no stun trap in the bag");
                yield return Snap("prompt11_list_bottom");
                pc.CloseMenu();
                // buy the Armour: 250 (Jonah 150)
                int w0 = me.Count(Cfg.BuildItem);
                me.CraftRpc(Cfg.CraftIndexOf(Item.HeavyArmor));
                yield return new WaitForSeconds(0.6f);
                Check(me.ArmorHp.Value >= Cfg.HeavyArmorHp && w0 - me.Count(Cfg.BuildItem) == (Cfg.JonahBruh ? 150 : 250), $"bought the Armour for {w0 - me.Count(Cfg.BuildItem)} (+{me.ArmorHp.Value} armour)");
            }
            Log("prompt11 done");
            Application.Quit(0);
        }
    }
}
