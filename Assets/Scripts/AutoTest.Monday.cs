using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest monday -host -solo -fast -rules autowood -shotdir DIR (Classic): no upgrade station in any base; the
        /// wood machine there from the start at level 1 and levelling up by itself (Cfg.AutoWoodGen); the hammer under the
        /// building plan in the starter list, crafted, lighting your own wall green with its price and taking it up to
        /// stone (50), metal (100), armoured (250), nothing more after that or without the wood; half the sword's damage;
        /// the chainsaw (Trade Station T1, 500 wood, 134 uses worn one every two hits) and saddle back on T1; no High
        /// Strength ram; airdrop crates two stacks of wood and one airdrop item; the ball buff x3, shown to everyone; the
        /// airdrop beam steady (no flicker); shots of the beams.
        /// </summary>
        IEnumerator MondayRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            // the wall drops 10 s in (-fast): wait for it, then hold the clock
            for (float t = 0; t < 30f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            Check(g.S == GameState.BallLive, "the wall dropped");

            // ---- no upgrade station; the wood machine is there from the start and speeds up by itself
            Check(Cfg.HammerOn && !Cfg.HasBaseUpgrades, "the hammer is on and there are no base upgrades here");
            bool anyStation = false;
            foreach (var st in UpgradeStation.ByTeam) if (st != null) anyStation = true;
            Check(!anyStation, "no upgrade station in any base");
            WoodMachine wm = null;
            foreach (var w in FindObjectsByType<WoodMachine>(FindObjectsSortMode.None)) if (w.Team == team) wm = w;
            Check(wm != null && wm.Built, "our base has its wood machine from the start");
            Check(g.WoodGenLevelOf(team) == 1, $"the wood machine starts at level 1 ({g.WoodGenLevelOf(team)})");
            float l2 = Cfg.WoodGenLevel2At, l3 = Cfg.WoodGenLevel3At;
            Cfg.WoodGenLevel2At = 0f;
            yield return new WaitForSeconds(1.5f);
            Check(g.WoodGenLevelOf(team) == 2, $"a third of the way through it goes up to level 2 by itself ({g.WoodGenLevelOf(team)})");
            Cfg.WoodGenLevel3At = 0f;
            yield return new WaitForSeconds(1.5f);
            Check(g.WoodGenLevelOf(team) == 3 && g.WoodGenLevelOf((team + 1) % Cfg.TeamCount) == 3, "two thirds of the way through, level 3 (every team)");
            Cfg.WoodGenLevel2At = l2;
            Cfg.WoodGenLevel3At = l3;
            {
                var tray = Cfg.WoodTrayPos(team);
                int pile = 0;
                foreach (var it in g.Items) if (it.Stack.Id == Item.Wood && Vector3.Distance(it.Pos, tray) < 3f) pile += it.Stack.Count;
                Check(pile > 0, $"wood piles up in front of it ({pile})");
                var yawM = Quaternion.LookRotation(Cfg.WoodMachinePos(team) - (Cfg.WoodMachinePos(team) - Cfg.BackDir(team) * 5f)).eulerAngles.y;
                pc.LocalTeleport(Cfg.WoodMachinePos(team) - Cfg.BackDir(team) * 5f, yawM);
                pc.SetLook(yawM, 12f);
                yield return Snap("monday_wood_machine");
            }

            // ---- items: the hammer under the plan, the chainsaw and saddle back on T1, no High Strength ram
            var list0 = new List<int>();
            Cfg.CraftList(list0, 0);
            int planAt = list0.IndexOf(Cfg.CraftIndexOf(Item.BuildingPlan)), hammerAt = list0.IndexOf(Cfg.CraftIndexOf(Item.Hammer));
            Check(Cfg.CraftTier(Item.Hammer) == 0 && planAt >= 0 && hammerAt == planAt + 1, $"the hammer is a starter item, right under the building plan ({planAt}, {hammerAt})");
            Check(Cfg.CraftTier(Item.Chainsaw) == 1 && Cfg.CraftTier(Item.Saddle) == 1, "the chainsaw and the saddle are Trade Station (T1) items again");
            int ci = Cfg.RecipeIndex(Item.Chainsaw);
            Check(ci >= 0 && Cfg.GetRecipe(ci).Wood == 500, $"the chainsaw costs 500 wood again ({(ci >= 0 ? Cfg.GetRecipe(ci).Wood : -1)})");
            Check(Cfg.MaxData(Item.Chainsaw) == Cfg.ChainsawUses && Cfg.ChainsawWearEvery == 2, "the chainsaw wears out again, at half the old rate");
            Check(Cfg.CraftIndexOf(Item.HeavyRam) < 0 && Cfg.Removed(Item.HeavyRam), "no High Strength Battering Ram");
            Check(Mathf.Approximately(Cfg.MeleePlayerDamage(Item.Hammer, false), Cfg.SwordBodyDamage * 0.5f) && Mathf.Approximately(Cfg.MeleePlayerDamage(Item.Hammer, true), Cfg.SwordHeadDamage * 0.5f),
                $"the hammer hits for half the sword ({Cfg.MeleePlayerDamage(Item.Hammer, false)} / {Cfg.MeleePlayerDamage(Item.Hammer, true)})");

            // ---- craft the hammer at home
            pc.LocalTeleport(Cfg.SpawnPos(team), Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            int wood0 = me.Count(Item.Wood);
            me.ServerGive(Item.Wood, Cfg.HammerWood);
            me.CraftRpc(Cfg.CraftIndexOf(Item.Hammer));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.Hammer) == 1, "the hammer can be crafted");
            // (spend whatever wood is left, so the prices can be counted)
            InvOps.Remove(me.Inv, Item.Wood, me.Count(Item.Wood));
            yield return Hold(me, Item.Hammer);

            // ---- the hammer on our own wall: green, its price, then up a tier at a time
            var back = Cfg.BackDir(team);
            var wallAt = Cfg.BaseCenter[team] - back * 9f + Vector3.Cross(Vector3.up, back) * 6f;
            wallAt.y = MapBuilder.Height(wallAt.x, wallAt.z);
            float wallYaw = Quaternion.LookRotation(back).eulerAngles.y;
            var wall = SpawnKeylessPiece(PieceType.Wall, team, wallAt, Quaternion.Euler(0, wallYaw, 0));
            yield return new WaitForSeconds(0.4f);
            var stand = wallAt + back * 3.2f;
            float lookYaw = Quaternion.LookRotation(-back).eulerAngles.y;
            pc.LocalTeleport(stand, lookYaw);
            pc.SetLook(lookYaw, 4f);
            yield return new WaitForSeconds(0.6f);
            Check(pc.HammerTarget() == wall, "aiming the hammer at our own wall picks it");
            Check(pc.DemolishLitCount == 1 && pc.DemolishAimed == wall.NetworkObject, "...and it lights up (green)");
            Check(pc.AimText.Contains("UPGRADE TO STONE") && pc.AimText.Contains($"{Cfg.HammerStoneWood} wood"), $"...with the price shown ({pc.AimText})");
            yield return Snap("monday_hammer_aim");
            // no wood: nothing happens
            me.HammerUpgradeRpc(wall.NetworkObject);
            yield return new WaitForSeconds(0.4f);
            Check(wall.Tier.Value == 0, "without the wood it stays wood");
            me.ServerGive(Item.Wood, Cfg.HammerStoneWood + Cfg.HammerMetalWood + Cfg.HammerArmouredWood);
            int[] prices = { Cfg.HammerStoneWood, Cfg.HammerMetalWood, Cfg.HammerArmouredWood };
            for (int tier = 1; tier <= 3; tier++)
            {
                int before = me.Count(Item.Wood);
                me.HammerUpgradeRpc(wall.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(wall.Tier.Value == tier && before - me.Count(Item.Wood) == prices[tier - 1], $"a click: {Cfg.TierName(tier).ToLower()} for {prices[tier - 1]} wood (tier {wall.Tier.Value}, paid {before - me.Count(Item.Wood)})");
                if (tier == 1) yield return Snap("monday_hammer_stone");
            }
            Check(prices[0] == 50 && prices[1] == 100 && prices[2] == 250, "the prices are 50 / 100 / 250 a piece");
            me.ServerGive(Item.Wood, 500);
            int w4 = me.Count(Item.Wood);
            me.HammerUpgradeRpc(wall.NetworkObject);
            yield return new WaitForSeconds(0.4f);
            Check(wall.Tier.Value == 3 && me.Count(Item.Wood) == w4, "armoured is as far as it goes (nothing charged)");
            Check(pc.HammerTarget() == null, "an armoured wall doesn't light up any more");
            yield return Snap("monday_hammer_armoured");
            // an enemy wall can't be hammered up
            var enemyWall = SpawnKeylessPiece(PieceType.Wall, (team + 1) % Cfg.TeamCount, wallAt + Vector3.Cross(Vector3.up, back) * 4f, Quaternion.Euler(0, wallYaw, 0));
            yield return new WaitForSeconds(0.4f);
            me.HammerUpgradeRpc(enemyWall.NetworkObject);
            yield return new WaitForSeconds(0.4f);
            Check(enemyWall.Tier.Value == 0, "the hammer doesn't upgrade an enemy's wall");

            // ---- the chainsaw wears one use every two hits
            ResourceNode tree = null;
            float best = float.MaxValue;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            {
                if (n == null || !n.IsSpawned || !n.IsWood || n.IsBush || n.Kind.Value != ResourceNode.Tree || n.Amount.Value < 100) continue;
                float d = Vector3.Distance(n.transform.position, me.transform.position);
                if (d < best) { best = d; tree = n; }
            }
            Check(tree != null, "a tree to saw");
            if (tree != null)
            {
                me.ServerGive(Item.Chainsaw, 1, Cfg.MaxData(Item.Chainsaw));
                yield return Hold(me, Item.Chainsaw);
                var to = tree.transform.position - me.transform.position; to.y = 0;
                var at = tree.transform.position - to.normalized * 1.4f;
                at.y = MapBuilder.Height(at.x, at.z);
                pc.LocalTeleport(at, Quaternion.LookRotation(to).eulerAngles.y);
                yield return new WaitForSeconds(0.5f);
                int d0 = me.HeldStack.Data;
                for (int i = 0; i < 4; i++)
                {
                    me.MeleeRpc(true, tree.NetworkObject, tree.transform.position + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(0.3f);
                }
                Check(me.HeldStack.Id == Item.Chainsaw && d0 - me.HeldStack.Data == 2, $"four cuts wear the chainsaw by two uses ({d0} -> {me.HeldStack.Data})");
            }

            // ---- airdrop crates: two stacks of wood and one airdrop item (any of them: explosive or not)
            {
                int mask = Cfg.AirdropItemMask;
                Cfg.AirdropItemMask = (1 << Cfg.AirdropChoices.Length) - 1; // (every airdrop item picked: this PC's saved choice may have none of the explosives)
                bool allOk = true, boom = false, other = false;
                for (int i = 0; i < 60; i++)
                {
                    var c = NetGame.RollAirdropCrate();
                    bool ok = c.Count == 3 && c[0].Id == Cfg.CurrencyItem && c[1].Id == Cfg.CurrencyItem && c[0].Count >= 500 && c[1].Count >= 500 && c[2].Id != Item.Wood && !c[2].Empty;
                    allOk &= ok;
                    if (c.Count == 3) { if (c[2].Id == Item.C4 || c[2].Id == Item.RocketLauncher || c[2].Id == Item.BombBush) boom = true; else other = true; }
                }
                Check(allOk, "every airdrop crate: two stacks of wood and one airdrop item");
                Check(boom && other, $"the item can be an explosive or something else ({boom}, {other})");
                Cfg.AirdropItemMask = mask;
            }

            // ---- the ball buff: x3 gathering, and everyone sees who has it
            Check(Mathf.Approximately(Cfg.BallGatherMul, 3f), $"the ball buff is x3 gathering ({Cfg.BallGatherMul})");
            var ball = Ball.Instance;
            Check(ball != null, "the ball is out");
            if (ball != null)
            {
                int enemy = (team + 1) % Cfg.TeamCount;
                ball.ServerSocket(enemy);
                pc.LocalTeleport(Cfg.SpawnPos(team), lookYaw);
                pc.SetLook(lookYaw, 0f);
                yield return new WaitForSeconds(1f);
                Check(Time.time - Hud.BuffShownAt < 0.5f && Hud.BuffShown.Contains($"{Cfg.TeamLabel(enemy)} HAS THE BALL BUFF") && Hud.BuffShown.Contains("3X GATHERING"),
                    $"the enemy's ball buff shows on our screen, in capitals ({Hud.BuffShown})");
                yield return Snap("monday_buff_enemy");
                ball.ServerSocket(team);
                yield return new WaitForSeconds(1f);
                Check(Hud.BuffShown.Contains("BALL BUFF") && !Hud.BuffShown.Contains("HAS THE") && Hud.BuffShown.Contains("3X GATHERING"), $"ours shows as BALL BUFF 3X GATHERING ({Hud.BuffShown})");
                yield return Snap("monday_buff_ours");
                Check(me.BallBuff, "with the ball in our machine we have the buff");
                yield return Snap("monday_ball_beacon");
            }

            // ---- the airdrop beam: steady (it used to flicker twice a second)
            g.DevSpawnAirdrop();
            for (float t = 0; t < 30f && AirdropShip.BeamIntensity <= 0f; t += 0.2f) yield return new WaitForSeconds(0.2f);
            yield return new WaitForSeconds(0.6f);
            float lo = float.MaxValue, hi = 0f;
            for (int i = 0; i < 8; i++)
            {
                float v = AirdropShip.BeamIntensity;
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
                yield return new WaitForSeconds(0.07f);
            }
            Check(hi > 0f && hi - lo < 0.01f, $"the airdrop beam is steady - no flicker ({lo:0.000} .. {hi:0.000})");
            var lp = g.LanePosAt(0);
            var far = new Vector3(lp.x, 0, lp.z) + (Cfg.BaseCenter[team] - new Vector3(lp.x, 0, lp.z)).normalized * 70f;
            far.y = MapBuilder.Height(far.x, far.z);
            pc.LocalTeleport(far, Quaternion.LookRotation(new Vector3(lp.x, far.y, lp.z) - far).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(new Vector3(lp.x, far.y, lp.z) - far).eulerAngles.y, -14f);
            yield return Snap("monday_airdrop_beam_far");
            Log("monday done");
            Application.Quit(0);
        }
    }
}
