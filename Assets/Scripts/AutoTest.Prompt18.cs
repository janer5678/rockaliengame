using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt18 -host -solo -rules emerald -shotdir DIR: the 2026-10-28 (3) patch -
        /// no ball buffs and the wood machine quietly making double (BallWood.cs); Emerald mode's spawners on each team's own
        /// half (EmeraldSpawner.NearBaseSpot), emeralds every 2:30, and its shop remade as team perks (Emerald.Perks.cs:
        /// the intruder alarm, auto armour, craft anywhere, the raid discount, the instant repair); the death wand and the
        /// barricade out of crafting; the med pen (MedPen.cs).
        /// </summary>
        IEnumerator Prompt18Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            g.TimerPaused.Value = true;

            // ---- the numbers
            {
                Check(Mathf.Approximately(Cfg.EmeraldEvery, 150f), $"emeralds every {Cfg.EmeraldEvery} s (2:30)");
                Check(Cfg.BallGatherNow == 1f && Cfg.AutoWoodSecretMul == 2 && !Cfg.BallDoublesWood(team),
                    $"no ball buffs: gathering x{Cfg.BallGatherNow}, the ball never doubles the machine - the machine itself makes x{Cfg.AutoWoodSecretMul} of its label, quietly");
                Check(Cfg.RecipeIndex(Item.Barricade) < 0 && Cfg.RecipeIndex(Item.DeathWand) < 0, "the barricade and the death wand are out of crafting");
                Check(Cfg.InAirdropPool(Item.DeathWand), "...the death wand still comes in airdrops");
                int mp = Cfg.RecipeIndex(Item.MedPen);
                Check(mp >= 0 && Cfg.CraftTier(Item.MedPen) == 1 && Cfg.GetRecipe(mp).Wood == Cfg.MedPenWood && Cfg.MaxStack(Item.MedPen) == 10 && Cfg.ItemName(Item.MedPen) == "Med Pen",
                    $"the Med Pen: a tier {Cfg.CraftTier(Item.MedPen)} craft for {(mp >= 0 ? Cfg.GetRecipe(mp).Wood : -1)} wood, stacks to {Cfg.MaxStack(Item.MedPen)}");
                bool rows = true;
                foreach (var it in new[] { Item.BaseAlarm, Item.AutoArmor, Item.CraftAnywherePerk, Item.RaidDiscount, Item.BaseRepair }) rows &= Cfg.Removed(it) && Cfg.IsEmeraldPerk(it) && ItemIcons.Get(it) != null;
                Check(rows && ItemIcons.Get(Item.MedPen) != null, "the shop's rows are never things in a bag, and every new icon rendered");
            }

            // ---- the med pen heals a berry's worth, used up as it goes in
            {
                me.ServerGive(Item.MedPen, 2);
                yield return Hold(me, Item.MedPen);
                me.Health.Value = 40f;
                yield return null;
                int pens0 = me.Count(Item.MedPen);
                int used0 = PlayerNet.MedPensUsed;
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(0.25f);
                Check(Mathf.Approximately(me.Health.Value, 40f) && me.Count(Item.MedPen) == pens0, "the jab starts: the arm comes up first, nothing's healed yet");
                ViewModel.DebugInjectE = 0.5f;
                yield return null; yield return null;
                yield return Snap("prompt18_med_pen_jab");
                ViewModel.DebugInjectE = 0.22f;
                yield return null; yield return null;
                yield return Snap("prompt18_med_pen_arm_up");
                ViewModel.DebugInjectE = -1f;
                yield return new WaitForSeconds(0.6f);
                Check(me.Health.Value >= 40f + Cfg.BerryHeal - 0.5f && me.Health.Value <= 40f + Cfg.BerryHeal + 6f && me.Count(Item.MedPen) == pens0 - 1 && PlayerNet.MedPensUsed == used0 + 1,
                    $"...then the heal lands: {me.Health.Value} HP (+{Cfg.BerryHeal}, and a touch of base regen), one pen gone ({me.Count(Item.MedPen)} left)");
                me.Health.Value = Cfg.MaxHealth;
                yield return null;
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(1.2f);
                Check(me.Count(Item.MedPen) == pens0 - 1, "at full health a pen isn't wasted");
            }

            if (!Cfg.Emerald)
            {
                Log("prompt18 done (not Emerald mode: the shop and spawner checks need -rules emerald)");
                Application.Quit(0);
                yield break;
            }

            // ---- crafting is Classic's: the Trade Station waits for the capture, the Advanced one's back, nothing needs E on a bench
            {
                int ramI = Cfg.RecipeIndex(Item.Ram);
                var ram = ramI >= 0 ? Cfg.GetRecipe(ramI) : default;
                Check(!Cfg.BenchUnlocked(team) && !g.BenchUnlockedFor(team), "the Trade Station can't be crafted before the ball's been captured");
                Check(Cfg.RecipeIndex(Item.Workbench2) >= 0 && !Cfg.EmeraldOut(Item.Workbench2) && Cfg.CraftTier(Item.HeavyArmor) == 2 && Cfg.CraftTier(Item.Ram) == 0,
                    $"the Advanced Trade Station is back (Armour is tier {Cfg.CraftTier(Item.HeavyArmor)}, the ram a starter)");
                Check(ramI >= 0 && ram.Wood == Cfg.RamWood && ram.Stone == Cfg.RamStone, $"the ram's at Classic's price ({ram.Wood} wood, {ram.Stone} stone)");
                var l1 = new System.Collections.Generic.List<int>(); Cfg.AddTier(l1, 1);
                var l2 = new System.Collections.Generic.List<int>(); Cfg.AddTier(l2, 2);
                Check(l1.Count > 3 && l2.Count > 3, $"both tiers list their things ({l1.Count} / {l2.Count})");
                int wb = Cfg.CraftIndexOf(Item.Workbench);
                int had = me.Count(Item.Workbench);
                me.ServerGive(Cfg.BuildItem, 400);
                me.CraftRpc(wb);
                yield return new WaitForSeconds(0.4f);
                Check(me.Count(Item.Workbench) == had, "...crafting the Trade Station now is refused (no capture yet)");
            }

            // ---- the spawners: on each team's own half, as far out to the side as before
            {
                int n = EmeraldSpawner.Tops.Count;
                Check(n == Cfg.Copies, $"{n} emerald spawners (one a team)");
                if (n >= 2 && !Cfg.FourWay)
                {
                    var t0 = EmeraldSpawner.Tops[0];
                    var t1 = EmeraldSpawner.Tops[1];
                    var axis = Cfg.BaseCenter[1] - Cfg.BaseCenter[0]; axis.y = 0; axis.Normalize();
                    var across = Vector3.Cross(Vector3.up, axis);
                    float side0 = Mathf.Abs(Vector3.Dot(t0, across)), along0 = Vector3.Dot(t0, axis), along1 = Vector3.Dot(t1, axis);
                    float baseDist = new Vector2(Cfg.BaseCenter[0].x, Cfg.BaseCenter[0].z).magnitude;
                    Check(side0 > Cfg.MapHalf * 0.5f - 13f && side0 < Cfg.MapHalf * 0.5f + 7f, $"spawner 0 is as far out to the side as before ({side0:0} m; MapHalf * 0.5 = {Cfg.MapHalf * 0.5f:0})");
                    Check(along0 < -baseDist * 0.3f && along0 > -(baseDist - Cfg.BaseHalf - 6f), $"...and half way out towards blue's base, clear of it ({-along0:0} m of {baseDist:0} towards it)");
                    Check(along1 > baseDist * 0.3f && Cfg.RegionOf(t0) == 0 && Cfg.RegionOf(t1) == 1 && Cfg.BaseTeamAt(t0) < 0 && Cfg.BaseTeamAt(t1) < 0,
                        $"each spawner's on its own team's half, outside the base (regions {Cfg.RegionOf(t0)} / {Cfg.RegionOf(t1)})");
                    pc.LocalTeleport(OnGround(EmeraldSpawner.Tops[team] - back * 9f + Vector3.up * 1f), Quaternion.LookRotation(back).eulerAngles.y);
                    yield return new WaitForSeconds(0.5f);
                    yield return Snap("prompt18_spawner_on_our_half");
                }
            }

            // ---- the shop: five rows for the whole team, none of the old things
            var deals = Cfg.EmeraldDeals;
            var shopSpot = Cfg.EmeraldShopPos(team) - back * 2.5f + Vector3.up * 0.2f;
            float shopYaw = Quaternion.LookRotation(back).eulerAngles.y;
            {
                bool five = deals.Count == 5 && deals[0].Id == Item.BaseAlarm && deals[1].Id == Item.AutoArmor && deals[2].Id == Item.CraftAnywherePerk && deals[3].Id == Item.RaidDiscount && deals[4].Id == Item.BaseRepair;
                Check(five, $"the emerald shop: {deals.Count} rows - " + string.Join(", ", deals.ConvertAll(d => Cfg.ItemName(d.Id) + " " + d.Price)));
                bool none = true;
                foreach (var d in deals) none &= d.Id != Item.KnockStick && d.Id != Item.EnderPearl && d.Id != Item.Lightsaber && d.Id != Item.AlienGun && d.Id != Item.C4 && d.Id != Item.Barrier;
                Check(none, "...and none of the old rows (stick, pearls, lightsaber, alien gun, C4, walls)");
                // at the shop, with emeralds, the screen lists them
                pc.LocalTeleport(shopSpot, shopYaw);
                me.ServerGive(Item.Emerald, 20);
                yield return new WaitForSeconds(0.4f);
                pc.OpenShop(PlayerController.ShopEmerald);
                yield return new WaitForSeconds(0.5f);
                string repairTag = Hud.ShopRowsShown.Count == 5 ? Hud.ShopRowsShown[4].Problem : "(no rows drawn)";
                Check(Hud.ShopRowsShown.Count == 5 && Hud.ShopRowsShown[0].Problem == null && repairTag != null,
                    $"the shop screen shows {Hud.ShopRowsShown.Count} rows; the perks can be bought, the repair says {repairTag} (nothing's damaged)");
                yield return Snap("prompt18_emerald_shop");
                pc.ShopOpen = 0;
                pc.MenuOpen = false;
                yield return null;
            }

            // ---- the perks
            {
                int em0 = me.Count(Item.Emerald);
                // craft anywhere
                int idx = 2;
                me.EmeraldBuyRpc((byte)idx);
                yield return new WaitForSeconds(0.4f);
                var far = OnGround(Vector3.zero + side * 30f);
                Check(Cfg.HasPerk(team, Item.CraftAnywherePerk) && me.Count(Item.Emerald) == em0 - deals[idx].Price && Cfg.CanCraftAt(team, far) && !Cfg.CanCraftAt(enemy, far),
                    $"CRAFT ANYWHERE bought for {deals[idx].Price}: our team can craft out in the open, the enemy can't");
                Check(me.EmeraldProblem(deals[idx]) != null && Cfg.PerkShort(me.EmeraldProblem(deals[idx])) == "your team has it", "...it can't be bought twice");
                // raid discount
                em0 = me.Count(Item.Emerald);
                idx = 3;
                int ramBefore = Cfg.CraftRecipe(Cfg.CraftIndexOf(Item.Ram), team).Wood;
                me.EmeraldBuyRpc((byte)idx);
                yield return new WaitForSeconds(0.4f);
                int ramAfter = Cfg.CraftRecipe(Cfg.CraftIndexOf(Item.Ram), team).Wood;
                int ramEnemy = Cfg.CraftRecipe(Cfg.CraftIndexOf(Item.Ram), enemy).Wood;
                Check(Cfg.HasPerk(team, Item.RaidDiscount) && ramAfter == (ramBefore + 1) / 2 && ramEnemy == ramBefore && me.Count(Item.Emerald) == em0 - deals[idx].Price,
                    $"RAID DISCOUNT: the ram {ramBefore} -> {ramAfter} wood for us, still {ramEnemy} for the enemy");
                Check(Cfg.IsRaidable(Item.Ram) && Cfg.IsRaidable(Item.C4) && Cfg.IsRaidable(Item.Ladder) && !Cfg.IsRaidable(Item.Sword) && !Cfg.IsRaidable(Item.Hatchet), "...the raiding things: the ram, C4, ladders - not weapons or tools");
                // auto armour
                em0 = me.Count(Item.Emerald);
                idx = 1;
                me.ArmorHp.Value = 0;
                me.EmeraldBuyRpc((byte)idx);
                yield return new WaitForSeconds(0.4f);
                Check(Cfg.HasPerk(team, Item.AutoArmor) && me.ArmorHp.Value == Cfg.HeavyArmorHp && me.Count(Item.Emerald) == em0 - deals[idx].Price,
                    $"AUTO ARMOUR: armour on the moment it's bought ({me.ArmorHp.Value})");
                me.ArmorHp.Value = 0;
                me.ServerDamage(99999f, null, (byte)Item.Rock);
                yield return new WaitForSeconds(0.5f);
                bool died = me.Dead.Value;
                me.ServerRespawn(false);
                yield return new WaitForSeconds(0.6f);
                Check(died && !me.Dead.Value && me.ArmorHp.Value == Cfg.HeavyArmorHp, $"...and back in armour on respawn ({me.ArmorHp.Value})");
                // the intruder alarm
                em0 = me.Count(Item.Emerald);
                idx = 0;
                pc.LocalTeleport(shopSpot, shopYaw); // (back at the shop after the respawn - the emeralds dropped with us: more)
                me.ServerGive(Item.Emerald, 10);
                yield return new WaitForSeconds(0.4f);
                em0 = me.Count(Item.Emerald);
                me.EmeraldBuyRpc((byte)idx);
                yield return new WaitForSeconds(0.4f);
                Check(Cfg.HasPerk(team, Item.BaseAlarm) && me.Count(Item.Emerald) == em0 - deals[idx].Price, "INTRUDER ALARM bought");
                PlayerNet.ServerAddBot(enemy);
                yield return new WaitForSeconds(0.6f);
                PlayerNet bot = null;
                foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value && p.Team.Value == enemy) bot = p;
                if (bot != null)
                {
                    int alarms0 = NetGame.AlarmsRaised;
                    float shown0 = Hud.LastAlarmAt;
                    pc.LocalTeleport(OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 6f)), Cfg.SpawnYaw(team)); // (we stand just outside, watching)
                    if (bot.Dead.Value) bot.ServerRespawn(false);
                    bot.TeleportRpc(OnGround(Cfg.BaseCenter[team] + side * 4f, 0.3f), 0f);
                    yield return new WaitForSeconds(0.6f);
                    Check(NetGame.AlarmsRaised == alarms0 + 1 && Hud.LastAlarmAt > shown0 && Hud.LastAlarmText.Contains("INTRUDER ALARM") && Hud.LastAlarmText.Contains(bot.DisplayName),
                        $"a bot walks into our base: the alarm goes off for us - \"{Hud.LastAlarmText}\"");
                    // out and straight back in: the 5 s cooldown holds it
                    bot.TeleportRpc(OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 8f) + side * 6f, 0.3f), 0f);
                    yield return new WaitForSeconds(0.4f);
                    bot.TeleportRpc(OnGround(Cfg.BaseCenter[team] + side * 4f, 0.3f), 0f);
                    yield return new WaitForSeconds(0.5f);
                    Check(NetGame.AlarmsRaised == alarms0 + 1, $"...in again within {Cfg.AlarmCooldown:0} s: no second alarm yet");
                    yield return new WaitForSeconds(Cfg.AlarmCooldown);
                    bot.TeleportRpc(OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 8f) + side * 6f, 0.3f), 0f);
                    yield return new WaitForSeconds(0.4f);
                    bot.TeleportRpc(OnGround(Cfg.BaseCenter[team] + side * 4f, 0.3f), 0f);
                    yield return new WaitForSeconds(0.5f);
                    Check(NetGame.AlarmsRaised == alarms0 + 2, "...after the cooldown it sounds again");
                    // the enemy's own base never alarms us; our own team walking in doesn't either
                    int alarmsE = NetGame.AlarmsRaised;
                    bot.TeleportRpc(OnGround(Cfg.BaseCenter[enemy] + side * 4f, 0.3f), 0f);
                    yield return new WaitForSeconds(Cfg.AlarmCooldown + 0.5f);
                    pc.LocalTeleport(OnGround(Cfg.BaseCenter[team] - side * 4f), Cfg.SpawnYaw(team));
                    yield return new WaitForSeconds(0.6f);
                    Check(NetGame.AlarmsRaised == alarmsE, "the bot going home, and us coming into our own base, set nothing off");

                    // ---- the instant repair: a damaged foundation mended, a destroyed one back - not with the bot in our base
                    me.ServerGive(Cfg.BuildItem, 400);
                    if (me.Count(Item.BuildingPlan) == 0) me.ServerGive(Item.BuildingPlan, 1);
                    yield return Hold(me, Item.BuildingPlan);
                    var c = Cfg.BaseCenter[team];
                    int ci = Mathf.RoundToInt(c.x / Cfg.Cell) - Mathf.RoundToInt(side.x) * 3, cj = Mathf.RoundToInt(c.z / Cfg.Cell) - Mathf.RoundToInt(side.z) * 3;
                    me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
                    me.PlaceRpc((byte)PieceType.Foundation, ci, cj + (Mathf.Abs(side.z) > 0.5f ? 0 : 1), 0, 0);
                    yield return new WaitForSeconds(0.6f);
                    var k1 = new PieceKey(PieceKey.KFoundation, ci, cj, 0, 0);
                    var k2 = new PieceKey(PieceKey.KFoundation, ci, cj + (Mathf.Abs(side.z) > 0.5f ? 0 : 1), 0, 0);
                    if (k2.Equals(k1)) k2 = new PieceKey(PieceKey.KFoundation, ci + 1, cj, 0, 0);
                    if (!BuildGrid.Registry.ContainsKey(k2)) { me.PlaceRpc((byte)PieceType.Foundation, k2.I, k2.J, 0, 0); yield return new WaitForSeconds(0.6f); }
                    BuildGrid.Registry.TryGetValue(k1, out var s1);
                    BuildGrid.Registry.TryGetValue(k2, out var s2);
                    Check(s1 != null && s2 != null, $"two foundations of ours up ({ci}, {cj})");
                    if (s1 != null && s2 != null)
                    {
                        float full = s1.MaxHp;
                        s1.ServerDamage(full * 0.5f);
                        s2.ServerDamage(99999f);
                        yield return new WaitForSeconds(0.5f);
                        Check(s1.Health.Value < full * 0.6f && !BuildGrid.Registry.ContainsKey(k2), $"one's at {s1.Health.Value:0} of {full:0}, the other's destroyed");
                        // the bot's in our base: no repair
                        bot.TeleportRpc(OnGround(Cfg.BaseCenter[team] + side * 4f, 0.3f), 0f);
                        yield return new WaitForSeconds(0.6f);
                        pc.LocalTeleport(shopSpot, shopYaw);
                        yield return new WaitForSeconds(0.4f);
                        string why = me.EmeraldProblem(deals[4]);
                        int em1 = me.Count(Item.Emerald);
                        me.EmeraldBuyRpc(4);
                        yield return new WaitForSeconds(0.4f);
                        Check(why != null && why.Contains("enemy") && me.Count(Item.Emerald) == em1 && s1.Health.Value < full * 0.6f, $"with the bot in our base the repair's refused: \"{why}\" (no emeralds taken)");
                        bot.TeleportRpc(OnGround(Cfg.BaseCenter[enemy] + side * 4f, 0.3f), 0f);
                        yield return new WaitForSeconds(0.6f);
                        int rep0 = NetGame.Repairs;
                        Check(me.EmeraldProblem(deals[4]) == null, "the bot gone: the repair can be bought");
                        me.EmeraldBuyRpc(4);
                        yield return new WaitForSeconds(0.6f);
                        Check(NetGame.Repairs == rep0 + 1 && me.Count(Item.Emerald) == em1 - deals[4].Price && Mathf.Approximately(s1.Health.Value, full) && BuildGrid.Registry.ContainsKey(k2),
                            $"INSTANT REPAIR for {deals[4].Price}: the damaged foundation's at {s1.Health.Value:0} again and the destroyed one's back");
                        yield return Snap("prompt18_repaired");
                        Check(me.EmeraldProblem(deals[4]) != null && Cfg.PerkShort(me.EmeraldProblem(deals[4])) == "nothing to mend", "...and now there's nothing to mend");
                    }
                }
                else Check(false, "no enemy bot joined for the alarm and repair checks");
            }

            Log("prompt18 done");
            Application.Quit(0);
        }
    }
}
