using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt15 -host -solo [-rules emerald | autowood] -shotdir DIR: no door is broken on its own any more (a
        /// doorway's door and a large gate's leaf are part of the piece - every mode); and EMERALD MODE (Emerald.cs): its
        /// rules, the Trade Station crafted before the ball's captured and traded at with E (Armour first, the ram last at
        /// 500, nothing of it in the bag), no Advanced Trade Station, the smaller machine with the emerald shop on its left,
        /// the emerald spawners (one emerald at a time, every EmeraldEvery s, a clock over them), the airdrops' three spots,
        /// the emerald shop (everything on it bought) and its things: High Power Armour, the lightsaber, Instant Heal
        /// Berries, the land mine, the base turret, the high defensive walls and gate, and the fort creator.
        /// -autotest prompt15menu -shotdir DIR (no -host): Emerald Mode on the main menu's mode list and its page.
        /// </summary>
        IEnumerator Prompt15Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            var face = Quaternion.LookRotation(-back);

            // ---- no door raiding: hitting a door (or a large gate's leaf) hurts the whole piece
            {
                Check(Cfg.DoorIsPiece, "doors aren't broken on their own in any mode");
                var spot = OnGround(Cfg.BaseCenter[enemy] + Cfg.BackDir(enemy) * -(Cfg.BaseHalf + 10f), 0f);
                var dw = SpawnKeylessPiece(PieceType.Doorway, enemy, spot, Quaternion.LookRotation(side));
                yield return new WaitForSeconds(0.4f);
                float hp0 = dw.Health.Value, door0 = dw.DoorHealth.Value;
                var leaf = dw.transform.position + Vector3.up * 1.2f;
                bool leafHit = dw.IsDoorHit(leaf);
                dw.ServerDamageAt(40f, leaf);
                yield return new WaitForSeconds(0.2f);
                Check(!leafHit && dw.Health.Value < hp0 && Mathf.Approximately(dw.DoorHealth.Value, door0) && dw.HasDoor,
                    $"a hit on a doorway's door damages the doorway, the door stays on ({hp0:0} -> {dw.Health.Value:0}, door {door0:0} -> {dw.DoorHealth.Value:0})");
                dw.ServerDamageAt(dw.Health.Value + 5f, leaf);
                yield return new WaitForSeconds(0.3f);
                Check(dw == null || !dw.IsSpawned, "...and the door only goes with the doorway");
                var gate = SpawnKeylessPiece(PieceType.Gate, enemy, OnGround(spot + side * 8f, 0f), Quaternion.LookRotation(side));
                yield return new WaitForSeconds(0.4f);
                float ghp = gate.Health.Value, gdoor = gate.DoorHealth.Value;
                var gl = gate.transform.TransformPoint(new Vector3(Structure.GateHingeX + 1.2f, 1.5f, 0f));
                gate.ServerDamageAt(50f, gl);
                yield return new WaitForSeconds(0.2f);
                Check(!gate.IsDoorHit(gl) && gate.Health.Value < ghp && Mathf.Approximately(gate.DoorHealth.Value, gdoor), $"a large gate's leaf: the same ({ghp:0} -> {gate.Health.Value:0}, leaf {gate.DoorHealth.Value:0})");
                gate.NetworkObject.Despawn(true);
            }

            if (!Cfg.Emerald)
            {
                // outside Emerald mode: none of it
                Check(Cfg.Removed(Item.LandMine) && Cfg.Removed(Item.Lightsaber) && Cfg.Removed(Item.AutoTurret) && Cfg.Removed(Item.Barrier) && EmeraldShop.ByTeam[team] == null && EmeraldSpawner.Tops.Count == 0,
                    "outside Emerald mode: no emerald shop, spawners or emerald things");
                int ri = Cfg.CraftIndexOf(Item.Ram);
                Check(ri >= 0 && Cfg.CraftRecipe(ri, team).Wood == Cfg.RamWood && Cfg.CraftTier(Item.Ram) == 0 && Cfg.CraftIndexOf(Item.Workbench2) >= 0, $"the ram's still {Cfg.RamWood} in the basics, the Advanced Trade Station's there");
                Check(Cfg.BigWallTall == 1f, "large walls their usual height");
                Log("prompt15 done");
                Application.Quit(0);
                yield break;
            }

            // ---- the rules
            Check(Cfg.ClassicMode && Cfg.ProgressMode && Cfg.RulesName(Cfg.Rules) == "Emerald Mode" && System.Array.IndexOf(Hud.MenuModes, GameRules.Emerald) >= 0, "Emerald Mode: Classic's rules, on the menu");
            Check(Cfg.CraftIndexOf(Item.Workbench2) < 0 && Cfg.CraftIndexOf(Item.DeathWand) < 0, "no Advanced Trade Station (and the death wand's not crafted)");
            {
                var t0 = new List<int>(); Cfg.AddTier(t0, 0);
                var t1 = new List<int>(); Cfg.AddTier(t1, 1);
                var t2 = new List<int>(); Cfg.AddTier(t2, 2);
                bool ramIn0 = false; foreach (var i in t0) ramIn0 |= Cfg.CraftRecipe(i, team).Output == Item.Ram;
                var first = t1.Count > 0 ? Cfg.CraftRecipe(t1[0], team) : default;
                var last = t1.Count > 0 ? Cfg.CraftRecipe(t1[t1.Count - 1], team) : default;
                Check(first.Output == Item.HeavyArmor && last.Output == Item.Ram && last.Wood == 500 && !ramIn0 && t2.Count == 0,
                    $"the Trade Station: {first.Output} at the top, {last.Output} ({last.Wood}) at the bottom; the ram's not a basic; nothing on an Advanced one");
            }

            // ---- the base: a smaller machine, the emerald shop on its left
            {
                var mach = GameObject.Find("AlienMachine " + Cfg.TeamName[team]);
                var shop = EmeraldShop.ByTeam[team];
                Check(mach != null && shop != null, "the alien machine and the emerald shop are there");
                if (mach != null && shop != null)
                {
                    var l = Quaternion.Inverse(face) * (shop.transform.position - mach.transform.position);
                    Check(l.x > 0.5f, $"the emerald shop is on the machine's left ({l.x:0.00} m)");
                    if (Cfg.SmallBedrock) Check(mach.transform.localScale.x < Cfg.SmallMachineScale - 0.1f, $"the machine's smaller ({mach.transform.localScale.x:0.00} wide, {Cfg.SmallMachineScale} elsewhere)");
                    // both on the bedrock, not inside each other
                    var sb = shop.GetComponentInChildren<BoxCollider>().bounds;
                    string clash = null;
                    foreach (var col in mach.GetComponentsInChildren<Collider>()) if (!col.isTrigger && col.bounds.Intersects(sb)) clash = col.name + " " + col.bounds;
                    Check(clash == null, $"the shop and the machine don't overlap ({clash ?? "clear"}; the shop {sb})");
                }
                pc.LocalTeleport(Cfg.SpawnPos(team) - back * 1.2f, Quaternion.LookRotation(back).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 14f);
                yield return new WaitForSeconds(0.6f);
                yield return Snap("prompt15_base");
            }

            // ---- the Trade Station: crafted before anyone's captured the ball, put down, and traded at with E
            Container bench = null;
            {
                Check(g.S == GameState.PreBall && Cfg.BenchUnlocked(team), $"before the ball's captured, the Trade Station can be crafted ({g.S})");
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.ServerGive(Item.Wood, 4000);
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.4f);
                me.CraftRpc(Cfg.CraftIndexOf(Item.Workbench));
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Workbench) == 1, "crafted the Trade Station in our base");
                yield return Hold(me, Item.Workbench);
                me.PlaceDeployableRpc((byte)Item.Workbench, Workbench.DefaultPos(team), Workbench.DefaultYaw(team));
                yield return new WaitForSeconds(0.6f);
                bench = Workbench.ForTeam(team, 1);
                Check(bench != null, "put it down");
                // the bag: just the basics
                pc.CloseMenu();
                pc.MenuOpen = true;
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                bool tradeInBag = false, hatchet = false;
                foreach (var r in Hud.CraftRowsShown) { if (r.Id == Item.Sword || r.Id == Item.Crossbow || r.Id == Item.HeavyArmor || r.Id == Item.Ram) tradeInBag = true; if (r.Id == Item.Hatchet) hatchet = true; }
                Check(hatchet && !tradeInBag, $"the bag only has the basics - the Trade Station's things aren't in it ({Hud.CraftRowsShown.Count} rows)");
                pc.CloseMenu();
                // E on it: its shop
                pc.OpenShop(PlayerController.ShopTrade);
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                var rows = Hud.ShopRowsShown;
                Check(Hud.ShopShown == PlayerController.ShopTrade && rows.Count > 3 && rows[0].Id == Item.HeavyArmor && rows[rows.Count - 1].Id == Item.Ram,
                    $"E on it: its shop - {(rows.Count > 0 ? rows[0].Id.ToString() : "-")} first, {(rows.Count > 0 ? rows[rows.Count - 1].Id.ToString() : "-")} last ({rows.Count} rows)");
                yield return Snap("prompt15_trade_station");
                pc.CloseMenu();
                // the ram: 500, and only at the Trade Station
                int ri = Cfg.CraftIndexOf(Item.Ram);
                pc.LocalTeleport(OnGround(Cfg.BaseCenter[team] + side * (Cfg.BaseHalf - 3f) - back * 8f), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.5f);
                int w0 = me.Count(Item.Wood);
                me.CraftRpc(ri);
                yield return new WaitForSeconds(0.4f);
                Check(me.Count(Item.Ram) == 0 && me.Count(Item.Wood) == w0 && Cfg.BaseTeamAt(me.transform.position) == team, "in our base but away from the Trade Station: no trading");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.5f);
                me.CraftRpc(ri);
                yield return new WaitForSeconds(0.4f);
                Check(me.Count(Item.Ram) == 1 && w0 - me.Count(Item.Wood) == 500, $"at it: a battering ram for {w0 - me.Count(Item.Wood)} wood");
                int ai = Cfg.CraftIndexOf(Item.HeavyArmor);
                me.CraftRpc(ai);
                yield return new WaitForSeconds(0.4f);
                Check(me.ArmorHp.Value >= Cfg.HeavyArmorHp, $"...and Armour from the top of it ({me.ArmorHp.Value})");
            }

            // ---- the wall drops; an enemy bot to try things on
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.8f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value && p.Team.Value == enemy) bot = p;
            if (bot == null) { Log("FAIL: no bot"); Application.Quit(1); yield break; }
            foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1f);
            g.TimerPaused.Value = true;
            if (bot.Dead.Value) bot.ServerRespawn(false);
            Check(g.NoClock, "no clock (fill your bar)");

            // ---- airdrops: the middle, or the middle of the left or right side
            {
                var spots = Cfg.EmeraldDropSpots;
                bool sides = spots.Length == 3 && spots[0].sqrMagnitude < 0.01f && Vector3.Distance(spots[1], -spots[2]) < 0.1f;
                if (!Cfg.FourWay) sides &= Mathf.Abs(spots[1].z) < 1f && Mathf.Abs(spots[1].x) > Cfg.MapHalf * 0.4f;
                Check(sides, $"the three airdrop spots: the middle, {spots[1]}, {spots[2]}");
                var hits = new int[3];
                bool near = true;
                for (int i = 0; i < 60; i++)
                {
                    var p = NetGame.PickEmeraldDropSpot();
                    int k = NetGame.LastEmeraldDropSpot;
                    if (k < 0 || k > 2 || Flat(p, spots[k]) > 8f) near = false; else hits[k]++;
                }
                Check(near && hits[0] > 0 && hits[1] > 0 && hits[2] > 0, $"60 airdrops: every one on one of them, picked at random ({hits[0]} / {hits[1]} / {hits[2]})");
            }

            // ---- the emerald spawners: one emerald at a time, every EmeraldEvery s
            {
                var tops = EmeraldSpawner.Tops;
                Check(tops.Count == Cfg.Copies, $"an emerald spawner on every team's side ({tops.Count})");
                bool clear = true, away = true;
                foreach (var top in tops)
                {
                    foreach (var n in ResourceNode.All) if (n != null && n.Kind.Value == ResourceNode.Tree && Flat(n.transform.position, top) < 3.2f) clear = false;
                    for (int t = 0; t < Cfg.TeamCount; t++) if (Flat(top, Cfg.BaseCenter[t]) < Cfg.BaseHalf + 4f) away = false;
                    if (top.y < MapBuilder.Height(top.x, top.z) + 0.6f) away = false;
                }
                Check(clear && away, "raised, out of the bases, no trees on them");
                Cfg.EmeraldEvery = 5f;
                g.ServerEmeraldsNow();
                yield return new WaitForSeconds(0.6f);
                int Emeralds(Vector3 top) { int n = 0; foreach (var it in g.Items) if (it.Stack.Id == Item.Emerald && Flat(it.Pos, top) < 2f) n += it.Stack.Count; return n; }
                int on0 = Emeralds(tops[0]), on1 = tops.Count > 1 ? Emeralds(tops[1]) : 1;
                Check(on0 == 1 && on1 == 1 && g.EmeraldWaiting(0), $"each grew one emerald ({on0}, {on1})");
                // the clock over it
                var dir = new Vector3(tops[0].x, 0, tops[0].z).normalized;
                pc.LocalTeleport(OnGround(tops[0] + dir * 7f), Quaternion.LookRotation(-dir).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(-dir).eulerAngles.y, 4f);
                yield return new WaitForSeconds(0.6f);
                bool ready = false; foreach (var s in Hud.EmeraldClocksShown) ready |= s.Contains("EMERALD!");
                Check(ready, $"over it: EMERALD! ({string.Join(", ", Hud.EmeraldClocksShown)})");
                yield return Snap("prompt15_spawner");
                // pick it up: the clock starts again
                pc.LocalTeleport(tops[0] + dir * 0.8f + Vector3.up * 0.1f, Quaternion.LookRotation(-dir).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                int id = -1; foreach (var it in g.Items) if (it.Stack.Id == Item.Emerald && Flat(it.Pos, tops[0]) < 2f) id = it.Id;
                int e0 = me.Count(Item.Emerald);
                me.PickupItemRpc(id);
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Emerald) == e0 + 1 && !g.EmeraldWaiting(0) && g.EmeraldSecondsLeft(0) > 3f, $"picked it up (+{me.Count(Item.Emerald) - e0}); the next one in {g.EmeraldSecondsLeft(0):0.0} s");
                pc.LocalTeleport(OnGround(tops[0] + dir * 7f), Quaternion.LookRotation(-dir).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(-dir).eulerAngles.y, 4f);
                yield return new WaitForSeconds(0.5f);
                bool clock = false; foreach (var s in Hud.EmeraldClocksShown) clock |= s.Contains("EMERALD</color> 0:0");
                Check(clock, $"the clock counts down ({string.Join(", ", Hud.EmeraldClocksShown)})");
                yield return Snap("prompt15_spawner_clock");
                yield return new WaitForSeconds(Cfg.EmeraldEvery + 1f);
                Check(Emeralds(tops[0]) == 1 && (tops.Count < 2 || Emeralds(tops[1]) == 1), $"{Cfg.EmeraldEvery:0} s on: a new one on the first, still only one on the other ({Emeralds(tops[0])}, {(tops.Count > 1 ? Emeralds(tops[1]) : 0)})");
                Cfg.EmeraldEvery = 180f;
            }

            // ---- the emerald shop
            var deals = Cfg.EmeraldDeals;
            {
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.ArmorHp.Value = 0;
                me.ServerGive(Item.Emerald, 80);
                pc.LocalTeleport(Cfg.SpawnPos(team), Quaternion.LookRotation(back).eulerAngles.y);
                yield return new WaitForSeconds(0.5f);
                Check(Cfg.AtOwnEmeraldShop(team, me.transform.position), "at our emerald shop");
                pc.OpenShop(PlayerController.ShopEmerald);
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                var want = new[] { Item.Shotgun, Item.Revolver, Item.C4, Item.DeathWand, Item.RevolverAmmo, Item.ShotgunShell, Item.Arrow, Item.HighArmor, Item.Lightsaber, Item.HealBerry, Item.EnderPearl, Item.KnockStick, Item.Barrier, Item.LandMine, Item.InvisPotion, Item.AutoTurret, Item.FortCreator };
                var missing = new List<string>();
                foreach (var w in want) { bool f = false; foreach (var r in Hud.ShopRowsShown) f |= r.Id == w; if (!f) missing.Add(w.ToString()); }
                Check(Hud.ShopShown == PlayerController.ShopEmerald && missing.Count == 0 && Hud.ShopRowsShown.Count == deals.Count, $"E on it: the emerald shop, everything on it ({Hud.ShopRowsShown.Count} rows){(missing.Count > 0 ? " - missing " + string.Join(" ", missing) : "")}");
                yield return Snap("prompt15_emerald_shop");
                Hud.TestShopScrollTo = 100000f;
                for (int i = 0; i < 4; i++) yield return null;
                yield return Snap("prompt15_emerald_shop_bottom");
                pc.CloseMenu();
                // too far off: no
                pc.LocalTeleport(OnGround(Cfg.BaseCenter[team] + side * (Cfg.BaseHalf - 3f) - back * 8f), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.4f);
                me.EmeraldBuyRpc(0);
                yield return new WaitForSeconds(0.3f);
                Check(me.Count(Item.Emerald) == 80 && me.ArmorHp.Value == 0, "away from the shop: nothing bought");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.4f);
                int spent = 0, buys0 = PlayerNet.EmeraldBuys;
                for (int i = 0; i < deals.Count; i++)
                {
                    me.EmeraldBuyRpc((byte)i);
                    spent += deals[i].Price;
                    yield return new WaitForSeconds(0.15f);
                }
                yield return new WaitForSeconds(0.3f);
                var got = new List<string>();
                foreach (var d in deals)
                {
                    bool ok = d.Id == Item.HighArmor ? me.ArmorHp.Value == Cfg.HighArmorHp : me.Count(d.Id) >= d.Count;
                    if (d.Extra != Item.None) ok &= me.Count(d.Extra) >= d.ExtraCount;
                    if (!ok) got.Add($"{d.Id} ({me.Count(d.Id)})");
                }
                Check(got.Count == 0 && PlayerNet.EmeraldBuys - buys0 == deals.Count && me.Count(Item.Emerald) == 80 - spent,
                    $"bought everything: {deals.Count} things for {spent} emeralds ({me.Count(Item.Emerald)} left){(got.Count > 0 ? " - didn't get " + string.Join(", ", got) : "")}");
                Check(me.ArmorHp.Value == Cfg.HighArmorHp, $"High Power Armour on: {me.ArmorHp.Value} armour");
                Check(me.Count(Item.RevolverAmmo) == 3 * Cfg.RevolverMag && me.Count(Item.ShotgunShell) == 12 && me.Count(Item.Arrow) == 15 && deals[6].Price == 1, "1 emerald: 3 clips of each kind of ammo");
                Check(me.Count(Item.Barrier) == 8 && me.Count(Item.LargeGate) == 1, $"the high defensive walls: {me.Count(Item.Barrier)} and a gate");
            }

            // ---- Instant Heal Berries
            {
                me.Health.Value = 25f;
                yield return Hold(me, Item.HealBerry);
                int b0 = me.Count(Item.HealBerry);
                me.UseItemRpc();
                yield return new WaitForSeconds(0.4f);
                Check(me.Health.Value >= Cfg.MaxHealth - 0.01f && me.Count(Item.HealBerry) == b0 - 1, $"a heal berry: 25 -> {me.Health.Value:0} at once");
            }

            // ---- the lightsaber
            {
                var at = OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 14f), 0.1f);
                pc.LocalTeleport(at, Quaternion.LookRotation(side).eulerAngles.y);
                bot.TeleportRpc(OnGround(at + side * 1.6f, 0.1f), Quaternion.LookRotation(-side).eulerAngles.y);
                bot.ArmorHp.Value = 0; bot.Health.Value = Cfg.MaxHealth;
                yield return Hold(me, Item.Lightsaber);
                pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 5f);
                yield return new WaitForSeconds(1f);
                yield return Snap("prompt15_lightsaber");
                float h0 = bot.Health.Value;
                me.MeleeRpc(true, bot.NetworkObject, bot.transform.position + Vector3.up * 1.0f, false);
                yield return new WaitForSeconds(0.4f);
                Check(Mathf.Abs(h0 - bot.Health.Value - Cfg.LightsaberDamage) < 1f, $"a lightsaber hit: {h0 - bot.Health.Value:0} damage");
            }

            // ---- the land mine
            {
                var at = OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 14f) - side * 6f, 0.1f);
                pc.LocalTeleport(at, Quaternion.LookRotation(side).eulerAngles.y);
                yield return Hold(me, Item.LandMine);
                yield return new WaitForSeconds(0.3f);
                var mpos = OnGround(at + side * 2.5f, 0f);
                me.PlaceDeployableRpc((byte)Item.LandMine, mpos, 0f);
                yield return new WaitForSeconds(0.5f);
                Container mine = null;
                foreach (var c in Container.All) if (c != null && c.IsSpawned && c.Kind.Value == Container.Mine) mine = c;
                Check(mine != null && me.Count(Item.LandMine) == 1, "put a land mine down");
                pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 30f);
                yield return new WaitForSeconds(0.4f);
                yield return Snap("prompt15_land_mine");
                if (bot.Dead.Value) bot.ServerRespawn(false);
                yield return new WaitForSeconds(0.3f);
                bot.ArmorHp.Value = 60; bot.Health.Value = Cfg.MaxHealth;
                int blown = Deployables.MinesBlown;
                bot.TeleportRpc(mpos + Vector3.up * 0.05f, 0f);
                yield return new WaitForSeconds(0.8f);
                Check(Deployables.MinesBlown == blown + 1 && bot.Dead.Value && (mine == null || !mine.IsSpawned), $"an enemy steps on it: they blow up (dead: {bot.Dead.Value}, armour and all) and it's gone");
                yield return new WaitForSeconds(0.5f);
                bot.ServerRespawn(false);
                yield return new WaitForSeconds(0.5f);
            }

            // ---- the base turret: all the way round, 5 a shot
            {
                var tp = OnGround(Cfg.BaseCenter[team] - back * 7f + side * 7f, 0f);
                pc.LocalTeleport(tp - side * 2f + Vector3.up * 0.1f, Quaternion.LookRotation(side).eulerAngles.y);
                yield return Hold(me, Item.AutoTurret);
                yield return new WaitForSeconds(0.3f);
                me.PlaceDeployableRpc((byte)Item.AutoTurret, tp, Quaternion.LookRotation(-back).eulerAngles.y);
                yield return new WaitForSeconds(0.6f);
                Container tur = null;
                foreach (var c in Container.All) if (c != null && c.IsSpawned && c.Kind.Value == Container.Turret && c.Team.Value == team) tur = c;
                Check(tur != null && tur.Slots.Count == 0, "the base turret's down - nothing to put in it");
                // the bot BEHIND it (it faces the middle): it still turns round and shoots
                pc.LocalTeleport(tp - side * 5f + Vector3.up * 0.1f, Quaternion.LookRotation(side).eulerAngles.y);
                bot.ArmorHp.Value = 0; bot.Health.Value = Cfg.MaxHealth;
                bot.TeleportRpc(OnGround(tp + back * 9f, 0.1f), 0f);
                int hits0 = Deployables.BaseTurretHits;
                yield return new WaitForSeconds(1.5f);
                pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 5f);
                yield return Snap("prompt15_base_turret");
                yield return new WaitForSeconds(2.5f);
                int hits = Deployables.BaseTurretHits - hits0;
                float lost = Cfg.MaxHealth - bot.Health.Value;
                Check(hits >= 3 && Mathf.Abs(lost - hits * Cfg.BaseTurretDamage) < 0.5f, $"an enemy behind it: it turned round and hit them {hits} times, {lost:0} damage ({Cfg.BaseTurretDamage:0} a shot)");
                bot.TeleportRpc(OnGround(Cfg.BaseCenter[enemy], 0.1f), 0f);
                yield return new WaitForSeconds(0.3f);
            }

            // ---- high defensive walls and the gate
            {
                var wp = OnGround(Cfg.BaseCenter[team] - back * 12f - side * 6f, 0f);
                pc.LocalTeleport(wp - back * 3.5f + Vector3.up * 0.1f, Quaternion.LookRotation(back).eulerAngles.y);
                yield return Hold(me, Item.Barrier);
                yield return new WaitForSeconds(0.3f);
                float yaw = Quaternion.LookRotation(back).eulerAngles.y;
                me.PlaceDeployableRpc((byte)Item.Barrier, wp, yaw);
                yield return new WaitForSeconds(0.6f);
                Structure wall = null, gate = null;
                foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.PType == PieceType.Barrier && Flat(s.transform.position, wp) < 1f) wall = s;
                float tall = 0f;
                if (wall != null) foreach (var col in wall.GetComponentsInChildren<Collider>()) if (!col.isTrigger) tall = Mathf.Max(tall, col.bounds.max.y - wall.transform.position.y);
                Check(wall != null && tall > 4f * Cfg.BigWallTallMul - 0.4f && me.Count(Item.Barrier) == 7, $"a high defensive wall: {tall:0.0} m tall (large walls are 4 m)");
                yield return Hold(me, Item.LargeGate);
                yield return new WaitForSeconds(0.3f);
                var gp = wp - side * 5.6f;
                gp.y = MapBuilder.Height(gp.x, gp.z);
                me.PlaceDeployableRpc((byte)Item.LargeGate, gp, yaw);
                yield return new WaitForSeconds(0.6f);
                foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.PType == PieceType.Gate && Flat(s.transform.position, gp) < 1.5f) gate = s;
                Check(gate != null, "and the gate beside it");
                if (gate != null)
                {
                    me.ToggleDoorRpc(gate.NetworkObject);
                    yield return new WaitForSeconds(1.5f);
                    Check(gate.DoorOpen.Value, "the gate opens");
                    pc.LocalTeleport(wp - back * 9f - side * 2.8f + Vector3.up * 0.1f, Quaternion.LookRotation(back).eulerAngles.y);
                    pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 12f);
                    yield return new WaitForSeconds(0.8f);
                    yield return Snap("prompt15_defensive_walls");
                    me.ToggleDoorRpc(gate.NetworkObject);
                    yield return new WaitForSeconds(0.3f);
                    Check(!gate.DoorOpen.Value, "...and closes");
                }
            }

            // ---- the fort creator
            {
                var at = OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 22f) + side * 10f, 0f);
                pc.LocalTeleport(OnGround(at + back * 11f, 0.1f), Quaternion.LookRotation(-back).eulerAngles.y);
                yield return Hold(me, Item.FortCreator);
                yield return new WaitForSeconds(0.6f);
                int forts = PlayerNet.Forts;
                me.ThrowItemRpc(Item.FortCreator, me.EyePos, (-back) * 12f + Vector3.up * 3f);
                yield return new WaitForSeconds(0.3f);
                me.ThrownLandRpc(Item.FortCreator, at, Vector3.up);
                yield return new WaitForSeconds(1.2f);
                int walls = 0, gates = 0, towers = 0;
                foreach (var s in Structure.All)
                {
                    if (s == null || !s.IsSpawned || s.Team.Value != team || Flat(s.transform.position, at) > 4.5f) continue;
                    if (s.PType == PieceType.Barrier) walls++; else if (s.PType == PieceType.Gate) gates++; else if (s.PType == PieceType.Tower) towers++;
                }
                Check(PlayerNet.Forts == forts + 1 && walls == 3 && gates == 1 && towers == 1 && me.Count(Item.FortCreator) == 0, $"the fort creator: a fort where it landed ({walls} walls, {gates} gate, {towers} tower)");
                pc.SetLook(Quaternion.LookRotation(-back).eulerAngles.y, 8f);
                yield return new WaitForSeconds(1.2f);
                yield return Snap("prompt15_fort");
            }

            // ---- the rest of what we bought is all ours to use
            Check(me.Count(Item.Shotgun) == 1 && me.Count(Item.Revolver) == 1 && me.Count(Item.C4) == 1 && me.Count(Item.DeathWand) == 1 && me.Count(Item.EnderPearl) >= 2 && me.Count(Item.KnockStick) == 1 && me.Count(Item.InvisPotion) == 1,
                "the shotgun, revolver, C4, death wand, ender pearls, knockback stick and invisibility potion");
            Log("prompt15 done");
            Application.Quit(0);
        }

        IEnumerator Prompt15MenuShots()
        {
            Hud.TestNewMenu = true;
            yield return new WaitForSeconds(2.5f);
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.Emerald);
            Hud.TestNewPage(6);
            Hud.TestModesOpen(true);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("prompt15_menu_modes");
            Hud.TestModesOpen(false);
            yield return new WaitForSeconds(0.4f);
            Check(Cfg.RulesOf(Bootstrap.MapChoice) == GameRules.Emerald, $"Emerald Mode picked ({Cfg.RulesOf(Bootstrap.MapChoice)})");
            yield return Snap("prompt15_menu_emerald");
            Log("prompt15menu done");
            Application.Quit(0);
        }
    }
}
