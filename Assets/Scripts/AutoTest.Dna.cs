using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>-autotest modes -rules dna -host -solo -fast: DNA from trees and rocks, prices in DNA, and the gambling machine (both outcomes forced).</summary>
    public partial class AutoTest
    {
        IEnumerator Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), name + ".png"));
            Log("shot " + name);
            yield return new WaitForSeconds(0.25f);
        }

        static int GambleBet(Container c)
        {
            int n = 0;
            for (int i = 0; i < c.Slots.Count; i++) if (c.Slots[i].Id == Item.Dna) n += c.Slots[i].Count;
            return n;
        }

        static int WorldDna(out int far, Vector3 near)
        {
            int n = 0;
            far = 0;
            foreach (var it in NetGame.Instance.Items)
                if (it.Stack.Id == Item.Dna) { n += it.Stack.Count; if (Vector3.Distance(it.Pos, near) > 3f) far++; }
            return n;
        }

        IEnumerator DnaTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(Cfg.DnaRules && !Cfg.PowerMenu && !Cfg.LimitedCrafting && !Cfg.Builder, "DNA mode: the classic game's crafting, no power items");
            Check(Cfg.ItemName(Item.Dna) == "DNA" && Cfg.MaxStack(Item.Dna) == 1000 && Cfg.IsMat(Item.Dna) && ItemIcons.Get(Item.Dna) != null, "DNA is a material (stacks of 1000) with an icon");

            // rocks are rarer
            int trees = 0, rocks = 0;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            {
                if (n.Kind.Value == ResourceNode.Tree) trees++;
                else if (n.Kind.Value == ResourceNode.Boulder) rocks++;
            }
            Check(rocks > 0 && rocks < trees * 0.5f, $"rock nodes are rare ({rocks} rocks, {trees} trees; normally about 3 rocks to 4 trees)");

            // every price is in DNA (stone at 1.5 DNA each)
            bool allDna = true;
            for (int i = 0; i < Cfg.RecipeCount; i++) allDna &= Cfg.GetRecipe(i).Stone == 0 && Cfg.CostText(Cfg.GetRecipe(i)).EndsWith(" DNA");
            var pick = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Pickaxe));
            var bow = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Bow));
            Check(allDna && pick.Wood == Cfg.PickaxeWood + Mathf.RoundToInt(Cfg.PickaxeStone * Cfg.DnaStonePrice) && bow.Wood == Cfg.BowWood + Mathf.RoundToInt(Cfg.BowStone * Cfg.DnaStonePrice),
                $"every recipe costs DNA only (pickaxe {Cfg.CostText(pick)}, bow {Cfg.CostText(bow)})");
            Check(Cfg.CurrencyItem == Item.Dna && Cfg.UpgradeItem == Item.Dna && Cfg.UpgradeCost(PieceType.Wall) == Mathf.RoundToInt(Cfg.WallStone * Cfg.DnaStonePrice),
                $"building costs DNA (wall {Cfg.PieceWood(PieceType.Wall)} DNA, upgrade {Cfg.UpgradeCost(PieceType.Wall)} DNA)");

            // gathering with the rock: a tree gives DNA, a rock gives more
            yield return Hold(me, Item.Rock);
            int d0 = me.Count(Item.Dna);
            yield return Gather(me, pc, ResourceNode.Tree, 3, Cfg.BaseCenter[team]);
            int fromTree = me.Count(Item.Dna) - d0;
            Check(fromTree > 0 && me.Count(Item.Wood) == 0, $"hitting a tree gives DNA, not wood (+{fromTree} DNA in 3 hits)");
            yield return Shot("dna_tree");
            d0 = me.Count(Item.Dna);
            yield return Gather(me, pc, ResourceNode.Boulder, 3, Cfg.BaseCenter[team]);
            int fromRock = me.Count(Item.Dna) - d0;
            Check(fromRock > fromTree && me.Count(Item.Stone) == 0, $"hitting a rock gives more DNA than a tree, and no stone (+{fromRock} DNA in 3 hits)");

            // crafting and building take DNA
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            me.ServerGive(Item.Dna, 1000);
            yield return new WaitForSeconds(0.3f);
            int before = me.Count(Item.Dna);
            me.CraftRpc(Cfg.RecipeIndex(Item.Pickaxe));
            yield return new WaitForSeconds(0.4f);
            Check(me.Count(Item.Pickaxe) == 1 && before - me.Count(Item.Dna) == pick.Wood, $"crafted a pickaxe for {before - me.Count(Item.Dna)} DNA");
            pc.MenuOpen = true;
            yield return Snap("dna_crafting");
            pc.CloseMenu();
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            before = me.Count(Item.Dna);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(0.6f);
            Check(before - me.Count(Item.Dna) == Cfg.FoundationWood, $"a foundation costs {before - me.Count(Item.Dna)} DNA");
            Structure found = null;
            foreach (var s in Structure.All) if (s.PType == PieceType.Foundation && s.Team.Value == team) found = s;
            if (found != null)
            {
                before = me.Count(Item.Dna);
                me.UpgradeRpc(found.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(found.Tier.Value == 1 && before - me.Count(Item.Dna) == Cfg.UpgradeCost(PieceType.Foundation), $"upgrading it to stone costs {before - me.Count(Item.Dna)} DNA");
            }
            else Log("FAIL: the foundation wasn't built");

            // the gambling machine: one in every base, on the bedrock left of the alien machine
            Container gm = null;
            int machines = 0;
            foreach (var c in Container.All)
                if (c.IsGamble) { machines++; if (c.Team.Value == team) gm = c; }
            if (gm == null) { Log("FAIL: no gambling machine"); yield break; }
            var left = -Vector3.Cross(Vector3.up, Cfg.BackDir(team));
            Check(machines == Cfg.TeamCount && Vector3.Dot(gm.transform.position - Cfg.MachinePos(team), left) > 1.5f && Cfg.BaseTeamAt(gm.transform.position) == team
                && Mathf.Abs(gm.transform.position.y - Cfg.BaseY) < 0.05f && GambleMachine.Of(gm) != null,
                $"a gambling machine in every base ({machines}), on the bedrock left of the alien machine");

            // stand in front of it, looking at it
            yield return Hold(me, Item.Rock);
            var front = gm.transform.position + gm.transform.forward * 2.9f;
            front.y = Cfg.BaseY + 0.05f;
            pc.LocalTeleport(front, Quaternion.LookRotation(-gm.transform.forward).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            var look = Quaternion.LookRotation(gm.transform.position + Vector3.up * 1.35f - me.EyePos).eulerAngles;
            pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
            yield return new WaitForSeconds(0.3f);
            yield return Snap("dna_gamble_idle");

            // only DNA goes in
            int pickSlot = -1;
            for (int i = 0; i < Cfg.PlayerSlots && pickSlot < 0; i++) if (me.SlotAt(i).Id == Item.Pickaxe) pickSlot = i;
            if (pickSlot >= 0)
            {
                me.MoveItemRpc(0, (byte)pickSlot, 1, 0, 1, gm.NetworkObject);
                yield return new WaitForSeconds(0.3f);
                Check(gm.Slots[0].Empty && me.Count(Item.Pickaxe) == 1, "the gambling machine won't take anything but DNA");
            }

            // bet 300 (100 in each slot) and lose it
            yield return PutDna(me, gm, 100, 3);
            Check(GambleBet(gm) == 300, $"300 DNA in the three slots ({GambleBet(gm)})");
            pc.LootTarget = gm;
            pc.MenuOpen = true;
            yield return Snap("dna_gamble_ui");
            int dna0 = me.Count(Item.Dna);
            GambleMachine.ForceNext = 0;
            me.GambleRpc(gm.NetworkObject);
            pc.CloseMenu();
            yield return new WaitForSeconds(1.0f);
            yield return Shot("dna_gamble_spin");
            Check(GambleBet(gm) == 0, "the bet left the slots when the reels started");
            yield return new WaitForSeconds(2.95f);
            yield return Shot("dna_gamble_lose");
            Check(GambleOverlay.Showing == 2 && me.Count(Item.Dna) == dna0 && WorldDna(out _, gm.transform.position) == 0, "lost: YOU LOST on screen, the 300 DNA is gone and nothing comes out");
            yield return new WaitForSeconds(0.9f);
            yield return Shot("dna_gamble_lose2");
            yield return new WaitForSeconds(3f);

            // bet 200 and win 400
            yield return PutDna(me, gm, 200, 1);
            dna0 = me.Count(Item.Dna);
            GambleMachine.ForceNext = 1;
            me.GambleRpc(gm.NetworkObject);
            yield return new WaitForSeconds(1.7f);
            yield return Shot("dna_gamble_spin2");
            yield return new WaitForSeconds(2.15f);
            yield return Shot("dna_gamble_win");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("dna_gamble_win2");
            int won = WorldDna(out int far, gm.transform.TransformPoint(new Vector3(0, 0, 1f)));
            Check(GambleOverlay.Showing == 1 && won == 400 && far == 0, $"won: JACKPOT on screen and double the bet ({won} DNA) spat out of the hatch");
            var ids = new System.Collections.Generic.List<int>();
            foreach (var it in NetGame.Instance.Items) if (it.Stack.Id == Item.Dna) ids.Add(it.Id);
            foreach (var id in ids) me.PickupItemRpc(id);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Dna) == dna0 + 400, $"picked the winnings up ({me.Count(Item.Dna) - dna0} DNA)");
            yield return new WaitForSeconds(2.5f);
        }

        /// <summary>Drag `each` DNA into the first `slots` gambling slots.</summary>
        static IEnumerator PutDna(PlayerNet me, Container gm, int each, int slots)
        {
            for (int s = 0; s < slots; s++)
            {
                int from = -1;
                for (int i = 0; i < Cfg.PlayerSlots && from < 0; i++) if (me.SlotAt(i).Id == Item.Dna && me.SlotAt(i).Count >= each) from = i;
                if (from < 0) { Log("FAIL: not enough DNA to bet"); yield break; }
                me.MoveItemRpc(0, (byte)from, 1, (byte)s, (ushort)each, gm.NetworkObject);
                yield return new WaitForSeconds(0.25f);
            }
        }
    }
}
