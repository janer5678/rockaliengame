using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest craftui (with -host -solo -fast -shotdir DIR, any -rules): the TAB crafting list (starter items only),
    /// crafting and placing a workbench (only on your bedrock, one per team, unbreakable), its shop, an item being made
    /// in a cloud of sawdust and then lying on the bench, armour going straight on, and what each place refuses.
    /// Also BenchBuy: buys something at the team's workbench like a player would (for the other tests).
    /// </summary>
    public partial class AutoTest
    {
        /// <summary>
        /// Buy an item at your team's workbench (one is put on the bedrock first if there's none): sends WorkbenchBuyRpc
        /// from where you are (or the spawn if the bench is out of reach), waits for it to be made and picks it up off the bench.
        /// </summary>
        IEnumerator BenchBuy(PlayerNet me, PlayerController pc, Item id, bool pickUp = true)
        {
            int team = me.Team.Value;
            var bench = Workbench.ForTeam(team);
            if (bench == null)
            {
                var at = Cfg.Builder ? me.transform.position + me.transform.forward * 2.5f : Workbench.DefaultPos(team);
                if (Cfg.Builder) at.y = MapBuilder.Height(at.x, at.z);
                Workbench.ServerSpawn(team, at, Workbench.DefaultYaw(team));
                yield return new WaitForSeconds(0.3f);
                bench = Workbench.ForTeam(team);
            }
            if (bench == null) { Check(false, $"no workbench to buy the {Cfg.ItemName(id)} at"); yield break; }
            if (!bench.InReach(me.EyePos))
            {
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.3f);
            }
            int idx = Cfg.CraftIndexOf(id);
            me.WorkbenchBuyRpc(new NetworkObjectReference(bench.NetworkObject), idx);
            yield return new WaitForSeconds(0.2f);
            if (Cfg.BenchNoItem(id)) yield break;
            float until = Time.time + 30f;
            while (Workbench.ServerBusy(bench) && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.1f);
            if (!pickUp) yield break;
            var top = Workbench.Top(bench);
            var ids = new List<int>();
            foreach (var it in NetGame.Instance.Items) if (it.Stack.Id == id && (it.Pos - top).sqrMagnitude < 1f) ids.Add(it.Id);
            foreach (var i in ids) me.PickupItemRpc(i);
            yield return new WaitForSeconds(0.25f);
        }

        int WorldItemNear(Item id, Vector3 p, float r)
        {
            foreach (var it in NetGame.Instance.Items) if (it.Stack.Id == id && (it.Pos - p).sqrMagnitude < r * r) return it.Id;
            return -1;
        }

        void LookAt(PlayerController pc, PlayerNet me, Vector3 p)
        {
            var e = Quaternion.LookRotation(p - me.EyePos).eulerAngles;
            pc.SetLook(e.y, e.x > 180f ? e.x - 360f : e.x);
        }

        IEnumerator CraftUiRoutine(PlayerNet me, PlayerController pc)
        {
            int team = me.Team.Value;
            var g = NetGame.Instance;
            var cur = Cfg.CurrencyItem;
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            string P(string what) => $"craftui_{rn}_{Screen.width}_{what}";
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            for (int i = 0; i < 4; i++) me.ServerGive(cur, 1000);
            if (!Cfg.WoodMode && !Cfg.DnaRules) me.ServerGive(Item.Stone, 120);
            yield return new WaitForSeconds(0.5f);

            // ---- TAB: the crafting list next to the inventory, starter items only ----
            var listed = new List<Item>();
            for (int i = 0; i < Cfg.RecipeCount; i++) if (Cfg.IsStarter(Cfg.GetRecipe(i).Output)) listed.Add(Cfg.GetRecipe(i).Output);
            Check(listed.Count == (Cfg.LimitedCrafting ? 4 : 9), $"TAB crafts the starter items ({string.Join(", ", listed)})");
            pc.MenuOpen = true;
            yield return Snap(P("tab_crafting"));
            if (Cfg.LimitedCrafting)
            {
                var benchList = new List<int>();
                Cfg.BenchItems(benchList);
                Check(Cfg.RecipeIndex(Item.Workbench) < 0 && benchList.Count == 0, $"{Cfg.RulesName(Cfg.Rules)}: no workbench (nothing else to make)");
                pc.CloseMenu();
                Log("craftui test done");
                Application.Quit(0);
                yield break;
            }
            pc.CloseMenu();

            // only starter items craft from the inventory
            int c0 = me.Count(cur);
            int xi = Cfg.CraftIndexOf(Item.Crossbow);
            if (xi >= 0)
            {
                me.CraftRpc(xi);
                yield return new WaitForSeconds(0.4f);
                Check(me.Count(Item.Crossbow) == 0 && me.Count(cur) == c0, "the crossbow can't be crafted from the inventory");
            }
            me.CraftRpc(Cfg.RecipeIndex(Item.Workbench));
            {
                float until = Time.time + 0.5f + Cfg.CraftSecondsOf(Item.Workbench) + 1f; // (Builder: crafting takes a while)
                while (me.Count(Item.Workbench) == 0 && Time.time < until) yield return null;
                yield return new WaitForSeconds(0.3f);
            }
            Check(me.Count(Item.Workbench) == 1 && c0 - me.Count(cur) == Cfg.WorkbenchWood, $"crafted a workbench for {c0 - me.Count(cur)} {Cfg.CurrencyName}");

            // ---- placing it: only on your own metal floor ----
            var pos = Workbench.DefaultPos(team);
            float yaw = Workbench.DefaultYaw(team);
            if (!Cfg.Builder)
            {
                var grass = Cfg.BaseCenter[team] - Cfg.BackDir(team) * 7f;
                grass.y = MapBuilder.Height(grass.x, grass.z);
                Check(PlayerNet.DeployProblem(Item.Workbench, team, grass, yaw) != null, "a workbench can't go on the grass in your base");
                var enemy = Workbench.DefaultPos(1 - team);
                Check(PlayerNet.DeployProblem(Item.Workbench, team, enemy, yaw) != null, "or on the enemy's bedrock");
                Check(PlayerNet.DeployProblem(Item.Workbench, team, pos, yaw) == null, $"but it can go on your bedrock ({PlayerNet.DeployProblem(Item.Workbench, team, pos, yaw) ?? "ok"})");
            }
            else pos = me.transform.position + me.transform.forward * 3f;
            yield return Hold(me, Item.Workbench);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            LookAt(pc, me, pos);
            yield return new WaitForSeconds(0.4f);
            yield return Snap(P("ghost"));
            me.PlaceDeployableRpc((byte)Item.Workbench, pos, yaw);
            yield return new WaitForSeconds(0.6f);
            var bench = Workbench.ForTeam(team);
            Check(bench != null && me.Count(Item.Workbench) == 0, "placed the workbench");
            if (bench == null) { Application.Quit(1); yield break; }
            var bref = new NetworkObjectReference(bench.NetworkObject);
            Check(PlayerNet.DeployProblem(Item.Workbench, team, pos + Vector3.right * 0.01f, yaw) == "Your team already has a workbench", "one workbench per team");
            // it can't be broken
            bench.ServerDamage(99999f);
            yield return new WaitForSeconds(0.3f);
            Check(bench != null && bench.IsSpawned && !bench.Breakable, "the workbench can't be broken");

            // a look at it: from the front, then from the side
            var fwd = bench.transform.forward;
            var top = Workbench.Top(bench);
            var stand = top + fwd * 2.2f - bench.transform.right * 0.9f;
            stand.y = bench.transform.position.y + 0.05f;
            pc.LocalTeleport(stand, 0f);
            yield return new WaitForSeconds(0.3f);
            LookAt(pc, me, top + Vector3.up * 0.25f);
            yield return Snap(P("workbench_model"));
            var side = top - bench.transform.right * 2.4f + fwd * 0.6f;
            side.y = stand.y;
            pc.LocalTeleport(side, 0f);
            yield return new WaitForSeconds(0.3f);
            LookAt(pc, me, top + Vector3.up * 0.2f);
            yield return Snap(P("workbench_side"));
            pc.LocalTeleport(stand, 0f);
            yield return new WaitForSeconds(0.3f);
            LookAt(pc, me, top + Vector3.up * 0.1f);

            // ---- its shop: everything that isn't a starter item ----
            var shop = new List<int>();
            Cfg.BenchItems(shop);
            bool noStarters = true, allThere = true;
            foreach (var i in shop) noStarters &= !Cfg.IsStarter(Cfg.CraftRecipe(i, team).Output);
            for (int i = 0; i < Cfg.RecipeCount; i++) if (!Cfg.IsStarter(Cfg.GetRecipe(i).Output)) allThere &= shop.Contains(i);
            for (int i = 0; i < Cfg.PowerCount; i++) allThere &= shop.Contains(Cfg.PowerBase + i);
            var names = new List<string>();
            foreach (var i in shop) names.Add(Cfg.ItemName(Cfg.CraftRecipe(i, team).Output));
            Check(shop.Count > 0 && noStarters && allThere, $"the workbench sells everything else ({string.Join(", ", names)})");
            pc.LootTarget = bench;
            pc.MenuOpen = true;
            yield return Snap(P("workbench_menu"));
            // starter items aren't sold here
            {
                int s0 = me.Count(Item.Spear), w0 = me.Count(cur);
                me.WorkbenchBuyRpc(bref, Cfg.RecipeIndex(Item.Spear));
                yield return new WaitForSeconds(0.4f);
                Check(me.Count(Item.Spear) == s0 && me.Count(cur) == w0, "the workbench doesn't make starter items");
            }

            // ---- buying an item: the menu closes, sawdust, then it's lying on the bench ----
            Item buy = Cfg.CraftIndexOf(Item.Crossbow) >= 0 ? Item.Crossbow : Cfg.CraftRecipe(shop[0], team).Output;
            var rec = Cfg.CraftRecipe(Cfg.CraftIndexOf(buy), team);
            {
                int w0 = me.Count(cur);
                me.WorkbenchBuyRpc(bref, Cfg.CraftIndexOf(buy));
                pc.CloseMenu(); // (what clicking the tile does)
                me.WorkbenchBuyRpc(bref, Cfg.CraftIndexOf(buy)); // a second one while it's busy: refused
                yield return new WaitForSeconds(0.3f);
                var wb = Workbench.Of(bench);
                Check(Workbench.ServerBusy(bench) && wb != null && wb.Working && w0 - me.Count(cur) == rec.Wood, $"buying the {rec.Name} takes {w0 - me.Count(cur)} {Cfg.CurrencyName} (once - a second one while it's busy is refused) and it's being made");
                yield return new WaitForSeconds(Mathf.Max(0f, Cfg.WorkbenchCraftSeconds * 0.45f - 0.3f));
                Check(WorldItemNear(buy, top, 1f) < 0, "nothing on the bench while the sawdust flies");
                yield return Snap(P("sawdust"));
                float until = Time.time + 10f;
                while (Workbench.ServerBusy(bench) && Time.time < until) yield return null;
                yield return new WaitForSeconds(0.2f);
                int id = WorldItemNear(buy, top, 0.6f);
                Check(id >= 0, $"the {rec.Name} is lying in the middle of the workbench");
                yield return Snap(P("dust_clearing"));
                yield return new WaitForSeconds(1.4f);
                yield return Snap(P("item_on_bench"));
                Check(pc.Target.Kind == PlayerController.TargetKind.WorldItem, $"looking at it targets the item, not the bench ({pc.Target.Kind})");
                me.PickupItemRpc(id);
                yield return new WaitForSeconds(0.4f);
                Check(me.Count(buy) >= 1 && WorldItemNear(buy, top, 1f) < 0, $"picked the {rec.Name} up off the bench");
            }

            // ---- armour: goes straight on (no sawdust, nothing on the bench) ----
            if (Cfg.CraftIndexOf(Item.Armor) >= 0)
            {
                int w0 = me.Count(cur);
                me.WorkbenchBuyRpc(bref, Cfg.CraftIndexOf(Item.Armor));
                yield return new WaitForSeconds(0.4f);
                Check(me.ArmorHp.Value == Cfg.ArmorHp && w0 - me.Count(cur) == Cfg.GetRecipe(Cfg.RecipeIndex(Item.Armor)).Wood && !Workbench.ServerBusy(bench) && WorldItemNear(Item.Armor, top, 1.5f) < 0,
                    "armour from the workbench goes straight on - no sawdust, nothing on the bench");
            }
            // ---- a base upgrade: just happens ----
            if (Cfg.CraftIndexOf(Item.FortifyBuff) >= 0)
            {
                me.WorkbenchBuyRpc(bref, Cfg.CraftIndexOf(Item.FortifyBuff));
                yield return new WaitForSeconds(0.4f);
                Check(Cfg.FortifyLevel(team) == 1 && !Workbench.ServerBusy(bench), "Fortify All Walls from the workbench: straight away, no sawdust");
            }

            // ---- out of reach / closing ----
            pc.LootTarget = bench;
            pc.MenuOpen = true;
            yield return new WaitForSeconds(0.2f);
            pc.LocalTeleport(new Vector3(0, MapBuilder.Height(0, 0) + 1f, 0), 0f);
            yield return new WaitForSeconds(0.5f);
            Check(pc.LootTarget == null, "walking away from the workbench closes it");
            pc.CloseMenu();
            Log("craftui test done");
            Application.Quit(0);
        }
    }
}
