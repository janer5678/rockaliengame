using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest craftui (with -host -solo -fast -shotdir DIR, any -rules): the TAB crafting list with no workbench
    /// (starter items only), crafting and placing a Workbench T1 anywhere in the base (not outside it, not in the enemy's,
    /// one per team, unbreakable, E opens nothing), the T1 items showing up in the list only while you're in your base,
    /// crafting them straight into the inventory, then the Workbench T2 (needs the T1) and its items - with screenshots
    /// of each list and of both benches.
    /// Also BenchBuy: crafts a workbench-tier item like a player would (for the other tests).
    /// </summary>
    public partial class AutoTest
    {
        /// <summary>
        /// Craft an item that may need a workbench tier: puts the team's benches down first if they're missing (on the
        /// bedrock corners), goes home if you're out of your base, then crafts it from the list (CraftRpc) and waits for it.
        /// </summary>
        IEnumerator BenchBuy(PlayerNet me, PlayerController pc, Item id, bool pickUp = true)
        {
            int team = me.Team.Value;
            int tier = Cfg.CraftTier(id);
            for (int t = 1; t <= tier; t++)
            {
                if (Workbench.ForTeam(team, t) != null) continue;
                var at = Workbench.DefaultPos(team, t);
                if (Cfg.Builder)
                {
                    at = me.transform.position + me.transform.forward * 2.5f + me.transform.right * (t == 2 ? 1.8f : -1.8f);
                    at.y = MapBuilder.Height(at.x, at.z);
                }
                Workbench.ServerSpawn(team, at, Workbench.DefaultYaw(team), t);
                yield return new WaitForSeconds(0.3f);
            }
            if (Cfg.CraftTierAt(team, me.transform.position) < tier || !Cfg.CanCraftAt(team, me.transform.position))
            {
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.3f);
            }
            int idx = Cfg.CraftIndexOf(id);
            if (idx < 0) { Check(false, $"the {Cfg.ItemName(id)} can't be crafted in this mode"); yield break; }
            me.CraftRpc(idx);
            yield return new WaitForSeconds(0.3f);
            // Builder: it takes a while (and may be queued behind something)
            float until = Time.time + 30f;
            while (Cfg.Builder && (me.CraftingItem.Value != 0 || me.CraftQueue.Count > 0) && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.1f);
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

        /// <summary>The names in the crafting list a player of this team would see standing at p.</summary>
        static List<Item> ListAt(int team, Vector3 p)
        {
            var idx = new List<int>();
            Cfg.CraftList(idx, Cfg.CraftTierAt(team, p));
            var l = new List<Item>();
            foreach (var i in idx) l.Add(Cfg.CraftRecipe(i, team).Output);
            return l;
        }

        /// <summary>Craft from the list and wait for it (Builder: its timer).</summary>
        IEnumerator CraftWait(PlayerNet me, Item id)
        {
            me.CraftRpc(Cfg.CraftIndexOf(id));
            float until = Time.time + 0.5f + Cfg.CraftSecondsOf(id) + 1f;
            yield return new WaitForSeconds(0.3f);
            while (Cfg.Builder && (me.CraftingItem.Value != 0 || me.CraftQueue.Count > 0) && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.2f);
        }

        /// <summary>A look at a bench from the front and from the side.</summary>
        IEnumerator BenchShots(PlayerNet me, PlayerController pc, Container bench, string front, string side)
        {
            var fwd = bench.transform.forward;
            var top = Workbench.Top(bench);
            float lookUp = bench.BenchTier == 2 ? 0.55f : 0.35f;
            var stand = top + fwd * 3.6f - bench.transform.right * 1.3f;
            stand.y = MapBuilder.Height(stand.x, stand.z) + 0.05f;
            if (Cfg.BaseTeamAt(stand) >= 0 && Mathf.Abs(stand.y - bench.transform.position.y) < 0.4f) stand.y = bench.transform.position.y + 0.05f;
            pc.LocalTeleport(stand, 0f);
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, top + Vector3.up * lookUp);
            yield return Snap(front);
            var sd = top - bench.transform.right * 3.6f + fwd * 1.2f;
            sd.y = Mathf.Max(MapBuilder.Height(sd.x, sd.z), bench.transform.position.y) + 0.05f;
            pc.LocalTeleport(sd, 0f);
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, top + Vector3.up * lookUp);
            yield return Snap(side);
        }

        IEnumerator CraftUiRoutine(PlayerNet me, PlayerController pc)
        {
            int team = me.Team.Value;
            var cur = Cfg.CurrencyItem;
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            string P(string what) => $"craftui_{rn}_{Screen.width}_{what}";
            var spawn = Cfg.SpawnPos(team);
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            for (int i = 0; i < 6; i++) me.ServerGive(cur, 1000);
            if (!Cfg.WoodMode && !Cfg.DnaRules) me.ServerGive(Item.Stone, 300);
            yield return new WaitForSeconds(0.5f);

            // ---- TAB with no workbench: the starter items only ----
            var listed = ListAt(team, me.transform.position);
            bool startersOnly = true;
            foreach (var it in listed) startersOnly &= Cfg.IsStarter(it);
            Check(startersOnly && listed.Count == (Cfg.LimitedCrafting ? 4 : 8), $"TAB with no workbench: just the starter items ({string.Join(", ", listed)})");
            pc.MenuOpen = true;
            yield return Snap(P("tab_nobench"));
            if (Cfg.LimitedCrafting)
            {
                Check(Cfg.RecipeIndex(Item.Workbench) < 0 && Cfg.RecipeIndex(Item.Workbench2) < 0, $"{Cfg.RulesName(Cfg.Rules)}: no workbenches");
                pc.CloseMenu();
                Log("craftui test done");
                Application.Quit(0);
                yield break;
            }
            pc.CloseMenu();
            Check(!listed.Contains(Item.Barrier) && Cfg.CraftTier(Item.Barrier) == 1, "the high external wall moved to the T1 list");

            // tier items are refused without a bench
            int c0 = me.Count(cur);
            me.CraftRpc(Cfg.CraftIndexOf(Item.Crossbow));
            me.CraftRpc(Cfg.RecipeIndex(Item.Workbench2));
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Crossbow) == 0 && me.Count(Item.Workbench2) == 0 && me.Count(cur) == c0, "no crossbow and no Workbench T2 without a Workbench T1");

            // ---- craft a Workbench T1 ----
            c0 = me.Count(cur);
            yield return CraftWait(me, Item.Workbench);
            int price1 = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Workbench)).Wood;
            Check(me.Count(Item.Workbench) == 1 && c0 - me.Count(cur) == price1, $"crafted a Workbench T1 for {c0 - me.Count(cur)} {Cfg.CurrencyName}");

            // ---- placing it: anywhere inside your own base, not outside it, not in the enemy's ----
            float yaw = Workbench.DefaultYaw(team);
            var back = Cfg.BackDir(team);
            var pos = Cfg.BaseCenter[team] - back * 7f + Vector3.Cross(Vector3.up, back) * 3f; // on the grass in the base
            pos.y = MapBuilder.Height(pos.x, pos.z);
            if (!Cfg.Builder)
            {
                Check(Cfg.BaseTeamAt(pos) == team && PlayerNet.DeployProblem(Item.Workbench, team, pos, yaw) == null,
                    $"a workbench can go on the grass in your base ({PlayerNet.DeployProblem(Item.Workbench, team, pos, yaw) ?? "ok"})");
                Check(PlayerNet.DeployProblem(Item.Workbench, team, Workbench.DefaultPos(team), yaw) == null, "and on your bedrock");
                var outside = Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 6f);
                outside.y = MapBuilder.Height(outside.x, outside.z);
                Check(Cfg.BaseTeamAt(outside) < 0 && PlayerNet.DeployProblem(Item.Workbench, team, outside, yaw) != null, "but not outside your base");
                Check(PlayerNet.DeployProblem(Item.Workbench, team, Workbench.DefaultPos(1 - team), yaw) != null, "or in the enemy's base");
                Check(PlayerNet.DeployProblem(Item.Workbench, team, Cfg.SpawnPos(team), yaw) != null, "or on the spawn spot");
            }
            else pos = me.transform.position + me.transform.forward * 3f;
            yield return Hold(me, Item.Workbench);
            var near = pos + back * 2.6f;
            near.y = MapBuilder.Height(near.x, near.z) + 0.05f;
            if (Cfg.Builder) near = me.transform.position;
            pc.LocalTeleport(near, 0f);
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, pos);
            yield return new WaitForSeconds(0.4f);
            yield return Snap(P("ghost_t1"));
            me.PlaceDeployableRpc((byte)Item.Workbench, pos, yaw);
            yield return new WaitForSeconds(0.6f);
            var bench = Workbench.ForTeam(team, 1);
            Check(bench != null && me.Count(Item.Workbench) == 0 && Cfg.BenchTier(team) == 1, "placed the Workbench T1");
            if (bench == null) { Application.Quit(1); yield break; }
            Check(PlayerNet.DeployProblem(Item.Workbench, team, Workbench.DefaultPos(team), yaw) == "Your team already has a Workbench T1", "one Workbench T1 per team");
            bench.ServerDamage(99999f);
            yield return new WaitForSeconds(0.3f);
            Check(bench != null && bench.IsSpawned && !bench.Breakable, "the workbench can't be broken");
            // E on it: nothing opens (it's not a shop any more)
            LookAt(pc, me, bench.Center);
            yield return new WaitForSeconds(0.2f);
            Binds.TestPress(Bind.Interact);
            yield return new WaitForSeconds(0.4f);
            Check(!pc.MenuOpen && pc.LootTarget == null, "E on the workbench doesn't open a menu");
            yield return BenchShots(me, pc, bench, P("workbench_t1"), P("workbench_t1_side"));

            // ---- in the base: the T1 items are in the list and craft straight into the inventory ----
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            listed = ListAt(team, me.transform.position);
            bool t1 = listed.Contains(Item.Crossbow) && listed.Contains(Item.Armor) && listed.Contains(Item.Chainsaw) && listed.Contains(Item.Barrier) && listed.Contains(Item.Workbench2);
            bool noT2 = !listed.Contains(Item.Saddle) && !listed.Contains(Item.Pickaxe);
            if (Cfg.PowerMenu) { t1 &= listed.Contains(Item.Sword) && listed.Contains(Item.FortifyBuff); noT2 &= !listed.Contains(Item.Revolver) && !listed.Contains(Item.C4); }
            if (Cfg.AutoWood) t1 &= listed.Contains(Item.WoodGenBuff);
            Check(t1 && noT2, $"with a T1 bench, in the base the list has the T1 items too, not T2 ({string.Join(", ", listed)})");
            pc.MenuOpen = true;
            yield return Snap(P("tab_t1"));
            pc.CloseMenu();
            c0 = me.Count(cur);
            var xrec = Cfg.CraftRecipe(Cfg.CraftIndexOf(Item.Crossbow), team);
            yield return CraftWait(me, Item.Crossbow);
            Check(me.Count(Item.Crossbow) == 1 && c0 - me.Count(cur) == xrec.Wood && WorldItemNear(Item.Crossbow, Workbench.Top(bench), 2f) < 0,
                $"the crossbow goes straight into the inventory ({c0 - me.Count(cur)} {Cfg.CurrencyName}), nothing on the bench");
            yield return CraftWait(me, Item.Barrier);
            Check(me.Count(Item.Barrier) >= 1, "a high external wall from the T1 list");
            // armour: goes straight on
            c0 = me.Count(cur);
            yield return CraftWait(me, Item.Armor);
            Check(me.ArmorHp.Value == Cfg.ArmorHp && c0 - me.Count(cur) == Cfg.GetRecipe(Cfg.RecipeIndex(Item.Armor)).Wood, "armour from the T1 list goes straight on");
            if (Cfg.CraftIndexOf(Item.FortifyBuff) >= 0)
            {
                yield return CraftWait(me, Item.FortifyBuff);
                Check(Cfg.FortifyLevel(team) == 1, "Fortify All Walls from the T1 list");
            }

            // ---- out of the base: just the starter items again, and the T1 items are refused ----
            if (!Cfg.Builder)
            {
                var mid = back * 4f;
                mid.y = MapBuilder.Height(mid.x, mid.z) + 0.1f;
                pc.LocalTeleport(mid, Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.5f);
                listed = ListAt(team, me.transform.position);
                Check(!listed.Contains(Item.Crossbow) && Cfg.CraftTierAt(team, me.transform.position) == 0, $"out of the base the T1 items aren't listed ({string.Join(", ", listed)})");
                int x0 = me.Count(Item.Crossbow);
                me.CraftRpc(Cfg.CraftIndexOf(Item.Crossbow));
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Crossbow) == x0, "and can't be crafted out there");
                pc.MenuOpen = true;
                yield return Snap(P("tab_outside"));
                pc.CloseMenu();
                pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.4f);
            }

            // ---- the Workbench T2 ----
            c0 = me.Count(cur);
            yield return CraftWait(me, Item.Workbench2);
            int price2 = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Workbench2)).Wood;
            Check(me.Count(Item.Workbench2) == 1 && c0 - me.Count(cur) == price2, $"crafted a Workbench T2 from the T1 list for {c0 - me.Count(cur)} {Cfg.CurrencyName}");
            var pos2 = Cfg.Builder ? me.transform.position + me.transform.forward * 3f + me.transform.right * 2f : Workbench.DefaultPos(team, 2);
            if (Cfg.Builder) pos2.y = MapBuilder.Height(pos2.x, pos2.z);
            Check(PlayerNet.DeployProblem(Item.Workbench2, team, pos2, yaw) == null, $"the T2 can go down ({PlayerNet.DeployProblem(Item.Workbench2, team, pos2, yaw) ?? "ok"})");
            yield return Hold(me, Item.Workbench2);
            LookAt(pc, me, pos2);
            yield return new WaitForSeconds(0.4f);
            yield return Snap(P("ghost_t2"));
            me.PlaceDeployableRpc((byte)Item.Workbench2, pos2, yaw);
            yield return new WaitForSeconds(0.6f);
            var bench2 = Workbench.ForTeam(team, 2);
            Check(bench2 != null && Cfg.BenchTier(team) == 2 && !bench2.Breakable, "placed the Workbench T2 (unbreakable too)");
            if (bench2 == null) { Application.Quit(1); yield break; }
            yield return BenchShots(me, pc, bench2, P("workbench_t2"), P("workbench_t2_side"));

            // ---- with both: everything craftable in this mode is in the list, and T2 items craft ----
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            listed = ListAt(team, me.transform.position);
            bool all = true;
            var missing = new List<string>();
            for (int i = 0; i < Cfg.RecipeCount; i++) if (!listed.Contains(Cfg.GetRecipe(i).Output)) { all = false; missing.Add(Cfg.ItemName(Cfg.GetRecipe(i).Output)); }
            for (int i = 0; i < Cfg.PowerCount; i++) if (!listed.Contains(Cfg.GetPowerRecipe(i).Output)) { all = false; missing.Add(Cfg.ItemName(Cfg.GetPowerRecipe(i).Output)); }
            Check(all, $"with a T2 bench everything in this mode is in the list ({listed.Count} items{(missing.Count > 0 ? ", missing " + string.Join(", ", missing) : "")})");
            pc.MenuOpen = true;
            yield return Snap(P("tab_t2"));
            pc.CloseMenu();
            yield return CraftWait(me, Item.Saddle);
            Check(me.Count(Item.Saddle) == 1, "a saddle from the T2 list, straight into the inventory");
            if (Cfg.CraftIndexOf(Item.Revolver) >= 0)
            {
                yield return CraftWait(me, Item.Revolver);
                yield return CraftWait(me, Item.RevolverAmmo);
                Check(me.Count(Item.Revolver) == 1 && me.Count(Item.RevolverAmmo) >= 1, "a revolver and a bullet from the T2 list");
            }

            Log("craftui test done");
            Application.Quit(0);
        }
    }
}
