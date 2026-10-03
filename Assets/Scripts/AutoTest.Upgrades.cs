using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest upgrades (-host -solo -fast -rules arsenal|autowood|classic -shotdir DIR; or a host without -solo plus a
    /// client with -client 127.0.0.1):
    /// - horses take damage from every weapon (HorseWeaponTests: rock, hatchet, sword, spear stab and throw, bow, crossbow,
    ///   revolver, shotgun, sniper and death wand fired like a player - LMB / RMB pressed at a fresh horse - then rocket, C4
    ///   and airstrike blasts);
    /// - LMB with berries on a hurt horse feeds it (HorseFeedTests: one berry a click, you don't eat it, not past full);
    /// - the two-shot portal gun and raiding portals (PortalGunTests in AutoTest.Bench.cs, PortalRaidTests);
    /// - the UPGRADE STATION and its UPGRADES screen (UpgradesScreenTests: the station stands left of the alien machine
    ///   clear of everything (machine, socket, spawns, benches, wood machine) inside the bedrock, a chest can't go in it,
    ///   its prompt names it, E on it opens UPGRADES (E on the alien machine doesn't any more), photographed poor / ready /
    ///   level 1 / maxed, every level bought for its price with the station's celebration (plus signs, flash, bounce)
    ///   played and photographed, refused when maxed or away from the station; the ball in a socket is still the ball);
    /// - the saddle needs the Workbench T1 (not the T2);
    /// and from a client (ClientUpgradesRoutine): hurt a horse, feed it, open UPGRADES at our upgrade station and buy
    /// Fortify there (the client sees its station celebrate, and so does the host).
    /// UpgradeBuy buys an upgrade like a player would (for the Arsenal / Auto Wood tests).
    /// </summary>
    public partial class AutoTest
    {
        /// <summary>Buy a base upgrade the way the UPGRADES screen's button does (going to our upgrade station first if we're away).</summary>
        IEnumerator UpgradeBuy(PlayerNet me, PlayerController pc, Item id)
        {
            int team = me.Team.Value;
            if (!Cfg.AtOwnStation(team, me.transform.position))
            {
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.4f);
            }
            me.BaseUpgradeRpc(id);
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator UpgradesRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            if (!NetworkManager.Singleton.IsServer) { yield return ClientUpgradesRoutine(me, pc, g, team); yield break; }
            g.TimerPaused.Value = true; // (the wall stays up until the end)
            yield return HorseWeaponTests(me, pc, g, team);
            yield return HorseFeedTests(me, pc, g, team);
            yield return PortalGunTests(me, pc, g, team); // (and PortalRaidTests)
            yield return SaddleTierTest(me, pc, team);
            yield return UpgradesScreenTests(me, pc, g, team);
            // a client: wait for it to do its part (it feeds a horse and buys Fortify at its own upgrade station)
            if (!Bootstrap.Solo)
            {
                PlayerNet other = null;
                float until = Time.time + 30f;
                while (other == null && Time.time < until) { foreach (var p in PlayerNet.All) if (p != me) other = p; yield return null; }
                if (other != null)
                {
                    int ot = other.Team.Value;
                    until = Time.time + 180f;
                    while (Cfg.FortifyLevel(ot) < 1 && Time.time < until) yield return null;
                    Check(Cfg.FortifyLevel(ot) >= 1, $"(host) the client bought Fortify All Walls at its own upgrade station (level {Cfg.FortifyLevel(ot)})");
                    yield return new WaitForSeconds(0.5f);
                    Check(UpgradeStation.CelebrationsOf[ot] >= 1, $"(host) saw the client's upgrade station celebrate ({UpgradeStation.CelebrationsOf[ot]})");
                    yield return new WaitForSeconds(5f);
                }
                else Check(false, "(host) no client joined");
            }
            Log("upgrades test done");
            g.EndGame(team, "upgrades test done");
        }

        // ------------------------------------------------------------------ horses: every weapon hurts them

        /// <summary>A fresh wild horse standing `dist` in front of where we stand (side on).</summary>
        static Vehicle FreshHorse(Vector3 stand, Vector3 fwd, float dist = 3.2f)
        {
            var at = OnGround(stand + fwd * dist, 0.05f);
            return Vehicle.ServerSpawn(Vehicle.Horse, at, Quaternion.LookRotation(Vector3.Cross(Vector3.up, fwd)).eulerAngles.y);
        }

        static bool Hurt(Vehicle v, float hp0) => v == null || !v.IsSpawned || v.Hp.Value < hp0 - 0.5f;
        static string HpText(Vehicle v) => v == null || !v.IsSpawned ? "dead" : $"{v.Hp.Value:0}/{v.MaxHp:0} HP";

        /// <summary>Keep the crosshair on the horse's body for a while (it may wander off a little).</summary>
        IEnumerator AimAt(PlayerController pc, PlayerNet me, Vehicle v, float secs)
        {
            float until = Time.time + secs;
            do
            {
                if (v != null && v.IsSpawned) LookAt(pc, me, v.transform.position + Vector3.up * 1.05f);
                yield return null;
            } while (Time.time < until);
        }

        IEnumerator HorseWeaponTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            var fwd = -Cfg.BackDir(team);
            float yaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            // out in the open in our half (trees nearby are cleared so nothing gets in the way)
            var stand = OnGround(Cfg.BaseCenter[team] * 0.55f);
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
                if (n != null && n.IsSpawned && Vector3.Distance(n.transform.position, stand + fwd * 2f) < 8f) n.NetworkObject.Despawn(true);
            for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
            yield return new WaitForSeconds(0.3f);

            // each weapon gets a fresh horse in front of us; `use` presses the keys like a player would
            IEnumerator Try(string what, Item item, int data, Func<Vehicle, IEnumerator> use, float dist = 3.2f)
            {
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.Health.Value = Cfg.MaxHealth;
                yield return new WaitForSeconds(0.1f);
                if (item != Item.Rock) me.ServerGive(item, 1, data);
                if (item == Item.Bow || item == Item.Crossbow) me.ServerGive(Item.Arrow, 10);
                if (item == Item.Shotgun) me.ServerGive(Item.ShotgunShell, 3);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, item);
                pc.LocalTeleport(stand, yaw);
                yield return new WaitForSeconds(0.3f);
                // (melee: close enough to reach its side - a hatchet reaches 2.5 m)
                var v = FreshHorse(me.transform.position, fwd, dist);
                yield return new WaitForSeconds(0.4f);
                float hp0 = v.Hp.Value;
                yield return AimAt(pc, me, v, 0.15f);
                yield return use(v);
                Check(Hurt(v, hp0), $"a horse takes damage from the {what} ({HpText(v)})");
                if (v != null && v.IsSpawned) v.NetworkObject.Despawn(true);
                Binds.TestReleaseAll();
                yield return new WaitForSeconds(0.3f);
            }
            IEnumerator Tap(Vehicle v) { Binds.TestPress(Bind.Attack); yield return AimAt(pc, me, v, 1.4f); }
            IEnumerator Swing(Vehicle v) { Binds.TestHold(Bind.Attack, true); yield return AimAt(pc, me, v, 0.2f); Binds.TestHold(Bind.Attack, false); yield return AimAt(pc, me, v, 1.4f); }
            IEnumerator Draw(Vehicle v) { Binds.TestHold(Bind.Attack, true); yield return AimAt(pc, me, v, Cfg.BowDrawTime + 0.25f); Binds.TestHold(Bind.Attack, false); yield return AimAt(pc, me, v, 1f); }
            IEnumerator Throw(Vehicle v) { Binds.TestHold(Bind.Aim, true); yield return AimAt(pc, me, v, Cfg.SpearDrawTime + 0.3f); Binds.TestPress(Bind.Attack); yield return AimAt(pc, me, v, 1f); Binds.TestHold(Bind.Aim, false); }

            yield return Try("rock", Item.Rock, 0, Swing, 2.2f);
            yield return Try("hatchet", Item.Hatchet, 0, Swing, 2.2f);
            yield return Try("sword", Item.Sword, 0, Swing, 2.2f);
            yield return Try("spear (stab)", Item.Spear, 0, Tap, 2.2f);
            yield return Try("thrown spear", Item.Spear, 0, Throw);
            yield return Try("bow", Item.Bow, 0, Draw);
            yield return Try("crossbow", Item.Crossbow, 1, Tap);
            yield return Try("revolver", Item.Revolver, Cfg.RevolverMag, Tap);
            yield return Try("shotgun", Item.Shotgun, 1, Tap);
            yield return Try("sniper", Item.Sniper, Cfg.SniperAmmo, Tap);
            yield return Try("death wand", Item.DeathWand, 0, Tap);

            // the explosives (server side, from a safe distance): a rocket, a C4 blast, an airstrike
            IEnumerator Blast(string what, Action<Vehicle> boom, float wait)
            {
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                pc.LocalTeleport(stand, yaw);
                yield return new WaitForSeconds(0.3f);
                var v = FreshHorse(stand, fwd, 22f);
                yield return new WaitForSeconds(0.4f);
                float hp0 = v.Hp.Value;
                boom(v);
                yield return new WaitForSeconds(wait);
                Check(Hurt(v, hp0), $"a horse takes damage from {what} ({HpText(v)})");
                if (v != null && v.IsSpawned) v.NetworkObject.Despawn(true);
            }
            yield return Blast("a rocket", v => g.ServerRocket(v.transform.position + Vector3.up * 0.5f + v.transform.forward * 1.5f, me), 0.4f);
            yield return Blast("C4 going off next to it", v => g.ServerBlast(v.transform.position + v.transform.forward * 2f, Cfg.C4Radius, -1, Cfg.C4PlayerDamage, -1f, Cfg.C4KillRadius, me, false, false, NetGame.BlastKind.C4), 0.4f);
            yield return Blast("an airstrike", v => g.ServerAirstrike(v.transform.position, me), Cfg.AirstrikeDelay + 1f);
            // a saddled horse is hurt too
            {
                var v = FreshHorse(stand, fwd, 25f);
                yield return new WaitForSeconds(0.4f);
                v.Saddled.Value = true;
                float hp0 = v.Hp.Value;
                me.ServerGive(Item.Revolver, 1, Cfg.RevolverMag);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.Revolver);
                me.FirePistolRpc(true, v.NetworkObject, v.transform.position + Vector3.up * 1f, fwd);
                yield return new WaitForSeconds(0.4f);
                Check(Hurt(v, hp0), $"a saddled horse takes damage from a bullet too (server-checked FirePistolRpc, {HpText(v)})");
                if (v != null && v.IsSpawned) v.NetworkObject.Despawn(true);
            }
            for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
            me.Health.Value = Cfg.MaxHealth;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        // ------------------------------------------------------------------ horses: feeding them berries

        IEnumerator HorseFeedTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            var fwd = -Cfg.BackDir(team);
            float yaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            var stand = OnGround(Cfg.BaseCenter[team] * 0.55f + Vector3.Cross(Vector3.up, fwd) * 6f);
            for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
            me.ServerGive(Item.Berry, 5);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Berry);
            pc.LocalTeleport(stand, yaw);
            yield return new WaitForSeconds(0.3f);
            var v = FreshHorse(me.transform.position, fwd, 2.8f);
            yield return new WaitForSeconds(0.4f);
            v.Hp.Value = 25f; // (hurt, without it bolting off)
            me.Health.Value = 50f; // (so eating would show)
            yield return AimAt(pc, me, v, 0.4f);
            string aim = pc.AimText;
            Check(aim.Contains("25/") && aim.Contains("feed"), $"looking at a hurt horse with berries shows its HP and LMB: feed (\"{aim}\")");
            yield return Snap($"horse_feed_aim_{rn}");
            // LMB: one berry, +HorseBerryHeal HP for the horse, and you don't eat it
            Binds.TestPress(Bind.Attack);
            yield return AimAt(pc, me, v, 0.8f);
            Check(v != null && Mathf.Approximately(v.Hp.Value, 25f + Cfg.HorseBerryHeal) && me.Count(Item.Berry) == 4 && Mathf.Approximately(me.Health.Value, 50f) && pc.EatProgress == 0f,
                $"LMB with berries on the horse feeds it a berry: {HpText(v)} (+{Cfg.HorseBerryHeal:0}), {me.Count(Item.Berry)} berries left, our health still {me.Health.Value:0} (not eaten)");
            yield return Snap($"horse_fed_{rn}");
            // again: up to full, not past it
            Binds.TestPress(Bind.Attack);
            yield return AimAt(pc, me, v, 0.8f);
            Check(v != null && Mathf.Approximately(v.Hp.Value, v.MaxHp) && me.Count(Item.Berry) == 3, $"a second berry heals it to full and no further ({HpText(v)}, {me.Count(Item.Berry)} berries)");
            // at full health: no berry used (the client says so; the server refuses too)
            Binds.TestPress(Bind.Attack);
            yield return AimAt(pc, me, v, 0.6f);
            me.FeedHorseRpc(v.NetworkObject);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Berry) == 3, $"a horse at full health isn't fed (no berry used: {me.Count(Item.Berry)})");
            // the server checks: from far away, or holding something else, nothing happens
            v.Hp.Value = 20f;
            pc.LocalTeleport(stand - fwd * 20f, yaw);
            yield return new WaitForSeconds(0.4f);
            me.FeedHorseRpc(v.NetworkObject);
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Approximately(v.Hp.Value, 20f) && me.Count(Item.Berry) == 3, "feeding from 20 m away is refused by the server");
            // RMB still eats them
            pc.LocalTeleport(stand, yaw);
            yield return new WaitForSeconds(0.3f);
            Binds.TestPress(Bind.Aim);
            yield return new WaitForSeconds(Cfg.BerryEatTime + 0.8f);
            Check(me.Count(Item.Berry) == 2 && me.Health.Value > 50f, $"RMB still eats a berry yourself ({me.Health.Value:0} HP)");
            if (v != null && v.IsSpawned) v.NetworkObject.Despawn(true);
            for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
            me.Health.Value = Cfg.MaxHealth;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        // ------------------------------------------------------------------ portals: raiding breaks them

        /// <summary>C4, rockets and the battering ram break a portal (and its partner closes with it); a fake bomb bush
        /// doesn't, and a portal out of the blast's reach stays.</summary>
        IEnumerator PortalRaidTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            float yaw = Quaternion.LookRotation(-back).eulerAngles.y;
            var stand = OnGround(Cfg.SpawnPos(team) - back * 8f);
            var a = OnGround(stand - back * 14f + side * 6f, 0.03f);
            var b = OnGround(stand - back * 14f - side * 8f, 0.03f);
            int MakePair(Vector3 p, Vector3 q) { int pr = g.ServerNewPortalPair(); g.ServerAddPortal(p, Vector3.up, pr); g.ServerAddPortal(q, Vector3.up, pr); return pr; }
            bool Open(int pr) { foreach (var p in g.Portals) if (p.Pair == pr) return true; return false; }
            g.Portals.Clear();
            pc.LocalTeleport(stand, yaw);
            yield return new WaitForSeconds(0.4f);

            // a fake bomb bush going off isn't a raid: the portal stays
            int p1 = MakePair(a, b);
            yield return new WaitForSeconds(0.3f);
            g.ServerBlast(a + Vector3.up * 0.6f, 3f, -2, 10f, 0f, 0f, null, false);
            yield return new WaitForSeconds(0.3f);
            Check(Open(p1) && g.Portals.Count == 2, "a fake bomb bush going off next to a portal doesn't break it");
            // C4 right next to one end: it breaks, and its partner closes with it
            LookAt(pc, me, a);
            yield return Snap($"portal_raid_before_{rn}");
            g.ServerArmC4(a + side * 1.2f + Vector3.up * 0.03f, Vector3.up, me);
            yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
            Check(!Open(p1) && g.Portals.Count == 0, $"C4 next to a portal breaks it - and the other end closes too ({g.Portals.Count} portals left)");
            yield return Snap($"portal_raid_after_{rn}");
            // a rocket at one end breaks that pair; a pair out of the blast's reach stays
            int p2 = MakePair(a, b);
            var c = OnGround(stand - back * 28f + side * 10f, 0.03f);
            int p3 = MakePair(c, OnGround(c + side * 6f, 0.03f));
            yield return new WaitForSeconds(0.3f);
            g.ServerRocket(b + Vector3.up * 0.4f, me);
            yield return new WaitForSeconds(0.4f);
            Check(!Open(p2) && Open(p3), "a rocket breaks the portal pair it hits; one out of its reach stays");
            // the battering ram: hold LMB at a portal on the ground
            g.Portals.Clear();
            int p4 = MakePair(a, b);
            me.ServerGive(Item.Ram, 1, Cfg.RamUses);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Ram);
            pc.LocalTeleport(OnGround(a + back * 1.6f), yaw);
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, a);
            Binds.TestHold(Bind.Attack, true);
            yield return new WaitForSeconds(Cfg.RamWindup + 0.6f);
            Binds.TestHold(Bind.Attack, false);
            yield return new WaitForSeconds(0.4f);
            Check(!Open(p4), $"a battering ram smashes a portal too ({g.Portals.Count} portals left, {me.Count(Item.Ram)} ram)");
            g.Portals.Clear();
            Drop(me, Item.Ram);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        // ------------------------------------------------------------------ the saddle is a Workbench T1 item

        IEnumerator SaddleTierTest(PlayerNet me, PlayerController pc, int team)
        {
            int idx = Cfg.CraftIndexOf(Item.Saddle);
            if (idx < 0) yield break;
            var cur = Cfg.CurrencyItem;
            me.ServerGive(cur, 1000);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            int s0 = me.Count(Item.Saddle), w0 = me.Count(cur);
            if (Cfg.BenchTier(team) < 1)
            {
                me.CraftRpc(idx);
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Saddle) == s0 && me.Count(cur) == w0, $"no saddle without a Workbench T1 (benches: T{Cfg.BenchTier(team)})");
            }
            int benchBefore = Cfg.BenchTier(team);
            yield return BenchBuy(me, pc, Item.Saddle);
            Check(Cfg.CraftTier(Item.Saddle) == 1 && Cfg.BenchTier(team) == Mathf.Max(1, benchBefore) && me.Count(Item.Saddle) == s0 + 1,
                $"the saddle is crafted with the Workbench T1 (no T2 needed; benches: T{Cfg.BenchTier(team)})");
            Drop(me, Item.Saddle);
        }

        // ------------------------------------------------------------------ the UPGRADE STATION and UPGRADES

        IEnumerator UpgradesScreenTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            var cur = Cfg.CurrencyItem;
            var spawn = Cfg.SpawnPos(team);
            string Row(Item id) { foreach (var r in Hud.UpgradeRowsShown) if (r.Id == id) return r.Problem ?? "ok"; return "missing"; }
            IEnumerator Frames() { for (int i = 0; i < 3; i++) yield return null; yield return new WaitForEndOfFrame(); }
            IEnumerator FaceMachine()
            {
                pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.4f);
                LookAt(pc, me, Cfg.MachinePos(team) + Vector3.up * 1.1f);
                yield return new WaitForSeconds(0.3f);
            }
            IEnumerator FaceStation()
            {
                pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.4f);
                LookAt(pc, me, Cfg.UpgradeStationPos(team) + Vector3.up * 0.95f);
                yield return new WaitForSeconds(0.3f);
            }
            var ups = new List<Item>();
            Cfg.BaseUpgrades(ups);
            for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
            yield return FaceMachine();
            Check(pc.Target.Kind == PlayerController.TargetKind.Machine && pc.Target.MachineTeam == team, $"looking at our alien machine ({pc.Target.Kind})");

            if (!Cfg.HasBaseUpgrades)
            {
                // modes with no base upgrades: no station, and E on the machine still just says what it's for
                Check(UpgradeStation.ByTeam[team] == null && FindAnyObjectByType<UpgradeStation>() == null, $"{Cfg.RulesName(Cfg.Rules)} has no upgrade stations");
                Binds.TestPress(Bind.Interact);
                yield return new WaitForSeconds(0.4f);
                Check(!pc.UpgradesOpen && !pc.MenuOpen && ups.Count == 0, $"{Cfg.RulesName(Cfg.Rules)} has no base upgrades: E on the machine opens nothing");
                yield break;
            }
            // the alien machine doesn't do upgrades any more
            Check(!pc.AimText.ToLower().Contains("upgrade"), $"our alien machine's prompt doesn't offer upgrades (\"{pc.AimText}\")");
            Binds.TestPress(Bind.Interact);
            yield return new WaitForSeconds(0.4f);
            Check(!pc.UpgradesOpen && !pc.MenuOpen, "E on the alien machine doesn't open UPGRADES any more");
            pc.CloseMenu();

            // the station: one per base, left of the alien machine, on the bedrock and clear of everything
            yield return StationLayoutTests(me, pc, team);

            yield return FaceStation();
            Check(pc.Target.Kind == PlayerController.TargetKind.UpgradeStation && pc.Target.MachineTeam == team, $"looking at our upgrade station ({pc.Target.Kind})");
            Check(pc.AimText.Contains("UPGRADE STATION") && pc.AimText.Contains("to open"), $"its prompt names it (\"{pc.AimText}\")");
            yield return Snap($"upgrade_station_{rn}");
            Check(ups.Contains(Item.FortifyBuff) && ups.Contains(Item.WoodGenBuff) == Cfg.AutoWood, $"UPGRADES has {string.Join(", ", ups)} (the wood gen only in Auto Wood)");
            Check(Cfg.CraftIndexOf(Item.FortifyBuff) < 0 && Cfg.CraftIndexOf(Item.WoodGenBuff) < 0 && Cfg.PowerIndex(Item.FortifyBuff) < 0, "neither is a crafted (POWER ITEMS) item any more");
            // no workbench needed: here from the start
            Check(Cfg.BenchTier(team) >= 0, $"(benches down: T{Cfg.BenchTier(team)})");

            // E opens it: poor first
            Hud.CraftRowsShown.Clear();
            Binds.TestPress(Bind.Interact);
            yield return new WaitForSeconds(0.3f);
            yield return Frames();
            Check(pc.UpgradesOpen && pc.MenuOpen && pc.LootTarget == null, "E on our upgrade station opens UPGRADES");
            Check(Row(Item.FortifyBuff) == "can't afford" && Hud.CraftRowsShown.Count == 0, $"with no {Cfg.CurrencyName} its rows say so ({Row(Item.FortifyBuff)}), and the crafting list isn't drawn ({Hud.CraftRowsShown.Count} rows)");
            yield return Snap($"upgrades_{rn}_poor");
            for (int i = 0; i < 12; i++) me.ServerGive(cur, 1000);
            yield return new WaitForSeconds(0.3f);
            yield return Frames();
            bool allOk = true;
            foreach (var id in ups) allOk &= Row(id) == "ok";
            Check(allOk, $"with enough {Cfg.CurrencyName} every upgrade is ready to buy ({string.Join(", ", Hud.UpgradeRowsShown)})");
            yield return Snap($"upgrades_{rn}");

            // away from the station: the server won't sell it, and the screen closes
            pc.LocalTeleport(spawn - Cfg.BackDir(team) * 12f, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            int w0 = me.Count(cur);
            me.BaseUpgradeRpc(Item.FortifyBuff);
            yield return new WaitForSeconds(0.4f);
            Check(!pc.UpgradesOpen && Cfg.FortifyLevel(team) == 0 && me.Count(cur) == w0, "away from the station: the screen closes and the server won't sell upgrades");
            yield return FaceStation();
            Binds.TestPress(Bind.Interact);
            yield return new WaitForSeconds(0.3f);

            // buy every level: the price goes up each time, then it's maxed out and refused
            foreach (var id in ups)
            {
                int max = Cfg.BaseUpgradeMax(id);
                for (int lvl = 0; lvl < max; lvl++)
                {
                    int price = Cfg.BaseUpgradeRecipe(id, team).Wood;
                    int before = me.Count(cur), bought = PlayerNet.UpgradesBought, cel = UpgradeStation.Celebrations;
                    me.BaseUpgradeRpc(id);
                    if (lvl == 0 && id == Item.FortifyBuff)
                    {
                        // the station's show: close the screen and photograph it mid-celebration
                        float until = Time.time + 3f;
                        while (UpgradeStation.Celebrations == cel && Time.time < until) yield return null;
                        var st = UpgradeStation.ByTeam[team];
                        Check(UpgradeStation.Celebrations == cel + 1 && st != null && st.Animating && FindObjectsByType<PlusParticle>(FindObjectsSortMode.None).Length > 0,
                            "buying an upgrade sets the station off: green plus signs, the screen flash and the bounce");
                        pc.CloseMenu();
                        yield return new WaitForSeconds(0.12f);
                        yield return Snap($"upgrade_station_{rn}_celebrate");
                        yield return new WaitForSeconds(0.25f);
                        yield return Snap($"upgrade_station_{rn}_celebrate2");
                        yield return new WaitForSeconds(2f);
                        Check(st != null && !st.Animating && st.transform.GetChild(0).localScale == Vector3.one, "and it settles back down");
                        Binds.TestPress(Bind.Interact);
                        yield return new WaitForSeconds(0.3f);
                    }
                    yield return new WaitForSeconds(0.5f);
                    yield return Frames();
                    Check(Cfg.BaseUpgradeLevel(id, team) == lvl + 1 && before - me.Count(cur) == price && PlayerNet.UpgradesBought == bought + 1 && UpgradeStation.Celebrations == cel + 1,
                        $"UPGRADE {Cfg.ItemName(id)} to level {lvl + 1} for {before - me.Count(cur)} {Cfg.CurrencyName} (price {price}), the station celebrated");
                    if (lvl == 0 && id == Item.FortifyBuff) yield return Snap($"upgrades_{rn}_level1");
                }
                {
                    int before = me.Count(cur);
                    me.BaseUpgradeRpc(id);
                    yield return new WaitForSeconds(0.4f);
                    yield return Frames();
                    Check(me.Count(cur) == before && Cfg.BaseUpgradeLevel(id, team) == max && Row(id) == "maxed out", $"{Cfg.ItemName(id)} maxed out at level {max}: refused, the row says so ({Row(id)})");
                    Check(pc.UpgradesOpen, "(UPGRADES still open)");
                }
            }
            Check(Cfg.FortifyStoneWood == 1000 && Cfg.FortifyMetalWood == 2000 && Cfg.FortifyRefinedWood == 2500 && (!Cfg.AutoWood || (Cfg.WoodGen1Wood == 1000 && Cfg.WoodGen2Wood == 3000)),
                "the prices are the CHANGE VALUES entries (fortify 1000 / 2000 / 2500, wood gen 1000 / 3000)");
            yield return Snap($"upgrades_{rn}_maxed");
            // TAB closes it (and opens plain crafting next time)
            Binds.TestPress(Bind.Inventory);
            yield return new WaitForSeconds(0.3f);
            Check(!pc.MenuOpen && !pc.UpgradesOpen, "TAB closes UPGRADES");
            Binds.TestPress(Bind.Inventory);
            yield return new WaitForSeconds(0.3f);
            Check(pc.MenuOpen && !pc.UpgradesOpen, "TAB opens the normal crafting screen, not UPGRADES");
            pc.CloseMenu();

            // the ball in our socket: E on it is still the ball, not UPGRADES
            var ball = Ball.Instance;
            if (ball != null)
            {
                ball.ServerSocket(team);
                yield return new WaitForSeconds(0.5f);
                pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.3f);
                LookAt(pc, me, ball.transform.position);
                yield return new WaitForSeconds(0.3f);
                Check(pc.Target.Kind == PlayerController.TargetKind.Ball, $"looking at the ball in our socket targets the ball ({pc.Target.Kind})");
                // and the enemy's machine with the ball in it: still the ball (E takes it out of any machine)
                int enemy = 1 - team;
                ball.ServerSocket(enemy);
                yield return new WaitForSeconds(0.5f);
                pc.LocalTeleport(Cfg.SpawnPos(enemy) + Vector3.Cross(Vector3.up, Cfg.BackDir(enemy)) * 1.8f, Cfg.SpawnYaw(enemy)); // (beside the spawn: a client may be standing on it)
                yield return new WaitForSeconds(0.4f);
                LookAt(pc, me, ball.transform.position);
                yield return new WaitForSeconds(0.3f);
                Check(pc.Target.Kind == PlayerController.TargetKind.Ball, $"looking at the ball in the enemy's socket targets the ball ({pc.Target.Kind})");
                // with the wall down, E takes it out
                me.DevRpc(DevCmd.DropWallNow);
                float until = Time.time + 10f;
                while (g.WallUp && Time.time < until) yield return null;
                yield return new WaitForSeconds(0.5f);
                if (ball.SocketTeam.Value != enemy) { ball.ServerSocket(enemy); yield return new WaitForSeconds(0.5f); }
                pc.LocalTeleport(Cfg.SpawnPos(enemy) + Vector3.Cross(Vector3.up, Cfg.BackDir(enemy)) * 1.8f, Cfg.SpawnYaw(enemy)); // (beside the spawn: a client may be standing on it)
                yield return new WaitForSeconds(0.3f);
                LookAt(pc, me, ball.transform.position);
                yield return new WaitForSeconds(0.3f);
                Binds.TestPress(Bind.Interact);
                yield return new WaitForSeconds(0.5f);
                Check(me.CarryingBall && !pc.UpgradesOpen, "E takes the ball out of the enemy's machine");
                // and carrying it, E on our own station doesn't open UPGRADES
                yield return FaceStation();
                Binds.TestPress(Bind.Interact);
                yield return new WaitForSeconds(0.4f);
                Check(!pc.UpgradesOpen, "carrying the ball, E on our upgrade station doesn't open UPGRADES");
            }
            pc.CloseMenu();
        }

        /// <summary>Every base's upgrade station: left of the alien machine, inside the bedrock (so it never sits where you
        /// could build, and leaves room for walls on the bedrock's edges), clear of the machine, socket, spawns, benches and
        /// the wood machine, and solid (a chest can't be put down in it).</summary>
        IEnumerator StationLayoutTests(PlayerNet me, PlayerController pc, int team)
        {
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var st = UpgradeStation.ByTeam[t];
                if (st == null) { Check(false, $"team {t} has an upgrade station"); continue; }
                var back = Cfg.BackDir(t);
                var left = -Vector3.Cross(Vector3.up, back);
                var c = Cfg.BedrockCenter(t);
                // the station's extent along the base's left and back axes (renderers and its collider)
                float lMin = 1e9f, lMax = -1e9f, bMin = 1e9f, bMax = -1e9f;
                var all = new List<Bounds>();
                foreach (var r in st.GetComponentsInChildren<Renderer>()) all.Add(r.bounds);
                foreach (var col in st.GetComponentsInChildren<Collider>()) all.Add(col.bounds);
                foreach (var bd in all)
                    for (int i = 0; i < 8; i++)
                    {
                        var p = new Vector3((i & 1) == 0 ? bd.min.x : bd.max.x, 0, (i & 2) == 0 ? bd.min.z : bd.max.z) - new Vector3(c.x, 0, c.z);
                        float l = Vector3.Dot(p, left), b = Vector3.Dot(p, back);
                        lMin = Mathf.Min(lMin, l); lMax = Mathf.Max(lMax, l); bMin = Mathf.Min(bMin, b); bMax = Mathf.Max(bMax, b);
                    }
                float edge = Cfg.BedrockHalf - 0.1f;
                Check((st.transform.position - Cfg.UpgradeStationPos(t)).sqrMagnitude < 0.01f && lMin > 1.45f,
                    $"team {t}'s upgrade station stands left of the alien machine (left {lMin:0.00} to {lMax:0.00} m, the machine reaches 1.4)");
                Check(lMax <= edge && bMax <= edge && bMin >= -edge, $"team {t}'s station is inside the bedrock with room for walls on its edges (left to {lMax:0.00}, back {bMin:0.00} to {bMax:0.00}, edge {Cfg.BedrockHalf})");
                // nothing solid in its body (the alien machine, the socket, a bench, the wood machine, a chest...)
                var body = st.GetComponentInChildren<BoxCollider>();
                var hits = new List<string>();
                if (body != null)
                {
                    foreach (var h in Physics.OverlapBox(body.transform.TransformPoint(body.center) + Vector3.up * 0.05f, Vector3.Scale(body.size * 0.5f, new Vector3(1f, 0.9f, 1f)), body.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.transform.IsChildOf(st.transform) || h.GetComponentInParent<PlayerNet>() != null) continue;
                        var root = h.transform; while (root.parent != null && !root.name.StartsWith("Bedrock")) root = root.parent;
                        if (root.name.StartsWith("Bedrock") || h is TerrainCollider || h.name.StartsWith("Ground") || h.name.StartsWith("Terrain")) continue;
                        hits.Add(h.name + " (" + h.transform.root.name + ")");
                    }
                }
                Check(body != null && hits.Count == 0, $"team {t}'s station overlaps nothing ({(hits.Count == 0 ? "clear" : string.Join(", ", hits))})");
                // and keeps clear of the spawns, the socket, the benches' spots and the wood machine
                bool Near(Vector3 p, float r)
                {
                    var d = p - new Vector3(c.x, 0, c.z); float l = Vector3.Dot(d, left), b = Vector3.Dot(d, back);
                    float dl = Mathf.Max(0f, Mathf.Max(lMin - l, l - lMax)), db = Mathf.Max(0f, Mathf.Max(bMin - b, b - bMax));
                    return Mathf.Sqrt(dl * dl + db * db) < r;
                }
                bool spawnsClear = true;
                for (int slot = 0; slot < 4; slot++) spawnsClear &= !Near(Cfg.SpawnPos(t, slot), 0.7f);
                Check(spawnsClear && !Near(Cfg.SocketPos(t), 1.2f), $"team {t}'s station keeps clear of the spawn spots and the ball socket");
                bool benchesClear = true;
                for (int tier = 1; tier <= 2; tier++) benchesClear &= !Near(Workbench.DefaultPos(t, tier), Mathf.Max(Workbench.HalfX, Workbench.HalfZ) + 0.2f);
                Check(benchesClear && (!Cfg.AutoWood || !Near(Cfg.WoodMachinePos(t), 1.2f)) && (!Cfg.AutoWood || !Near(Cfg.WoodTrayPos(t), 1f)),
                    $"team {t}'s station keeps clear of the workbench spots{(Cfg.AutoWood ? ", the wood machine and its pile" : "")}");
                Check(PlayerNet.DeployProblem(Item.Chest, t, Cfg.UpgradeStationPos(t), Cfg.SpawnYaw(t)) != null, $"team {t}: a chest can't be put down inside the station");
            }
            // a photo of our base's back row: the upgrade station, the alien machine (and the wood machine)
            var b0 = Cfg.BackDir(team);
            pc.LocalTeleport(Cfg.BedrockCenter(team) - b0 * 2.6f + Vector3.up * (Cfg.BaseY + 0.05f), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, Cfg.MachinePos(team) + Vector3.up * 0.9f);
            yield return new WaitForSeconds(0.3f);
            yield return Snap($"upgrade_station_{Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "")}_row");
            // close up from the front
            var sp = Cfg.UpgradeStationPos(team);
            pc.LocalTeleport(sp - b0 * 2.4f + Vector3.up * 0.05f, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, sp + Vector3.up * 0.9f);
            yield return new WaitForSeconds(0.3f);
            yield return Snap($"upgrade_station_{Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "")}_close");
        }

        // ------------------------------------------------------------------ the client's part

        IEnumerator ClientUpgradesRoutine(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            yield return new WaitForSeconds(2f);
            var fwd = -Cfg.BackDir(team);
            float yaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            for (int i = 0; i < 4; i++) me.DevRpc(DevCmd.GiveWood);
            yield return new WaitForSeconds(0.5f);

            // a wild horse in our half: hurt it with the rock, then feed it a berry from a bush
            Vehicle v = null;
            foreach (var h in Vehicle.All) if (h != null && h.IsHorse && !h.HasDriver && Cfg.RegionOf(h.transform.position) == Cfg.RegionOf(Cfg.BaseCenter[team])) { v = h; break; }
            ResourceNode bush = null;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
                if (n != null && n.IsBush && n.Amount.Value > 0 && Cfg.BaseTeamAt(n.transform.position) < 0 && (bush == null || (n.transform.position - me.transform.position).sqrMagnitude < (bush.transform.position - me.transform.position).sqrMagnitude)) bush = n;
            Check(v != null && bush != null, $"(client) found a wild horse and a berry bush ({(v != null ? "horse" : "no horse")}, {(bush != null ? "bush" : "no bush")})");
            if (bush != null)
            {
                pc.LocalTeleport(OnGround(bush.transform.position + fwd * 1.5f), yaw);
                yield return new WaitForSeconds(0.5f);
                me.PickBerriesRpc(bush.NetworkObject);
                yield return new WaitForSeconds(0.6f);
                Check(me.Count(Item.Berry) >= 1, $"(client) picked berries ({me.Count(Item.Berry)})");
            }
            if (v != null && me.Count(Item.Berry) >= 1)
            {
                // hurt it with the rock (it bolts), wait for it to calm down, then walk up and feed it
                yield return Hold(me, Item.Rock);
                float hp0 = v.Hp.Value;
                // (it wanders about: walk up beside it again before each swing, a few tries)
                for (int tries = 0; tries < 4 && !Hurt(v, hp0); tries++)
                {
                    pc.LocalTeleport(OnGround(v.transform.position - v.transform.right * 1.9f), 0f);
                    yield return AimAt(pc, me, v, 0.25f);
                    var cam = Camera.main;
                    bool sees = cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out var rh, 4f) && rh.collider.GetComponentInParent<Vehicle>() == v;
                    Log($"(client) swing {tries + 1}: {Vector3.Distance(me.EyePos, v.transform.position + Vector3.up):0.0} m from the horse, crosshair on it: {sees}, holding {me.HeldItem}");
                    Binds.TestHold(Bind.Attack, true);
                    yield return AimAt(pc, me, v, 0.2f);
                    Binds.TestHold(Bind.Attack, false);
                    yield return AimAt(pc, me, v, 1.2f);
                }
                Check(Hurt(v, hp0), $"(client) hit a horse with the rock: {HpText(v)}");
                yield return new WaitForSeconds(8f); // (it runs off for 7 s)
                if (v != null && v.IsSpawned)
                {
                    yield return Hold(me, Item.Berry);
                    float hurt = v.Hp.Value;
                    int b0 = me.Count(Item.Berry);
                    for (int tries = 0; tries < 3 && v != null && v.IsSpawned && v.Hp.Value <= hurt + 0.5f; tries++)
                    {
                        pc.LocalTeleport(OnGround(v.transform.position - v.transform.right * 2.2f), 0f);
                        yield return AimAt(pc, me, v, 0.3f);
                        Binds.TestPress(Bind.Attack);
                        yield return AimAt(pc, me, v, 1f);
                    }
                    Check(v.Hp.Value > hurt + 0.5f && me.Count(Item.Berry) == b0 - 1, $"(client) fed the horse a berry: {hurt:0} -> {HpText(v)}, {me.Count(Item.Berry)} berries left");
                }
            }

            // UPGRADES at our upgrade station: E opens it, buy Fortify
            if (Cfg.HasBaseUpgrades)
            {
                pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.5f);
                LookAt(pc, me, Cfg.UpgradeStationPos(team) + Vector3.up * 0.95f);
                yield return new WaitForSeconds(0.4f);
                Check(pc.Target.Kind == PlayerController.TargetKind.UpgradeStation && pc.AimText.Contains("UPGRADE STATION"), $"(client) looking at our upgrade station (\"{pc.AimText}\")");
                Binds.TestPress(Bind.Interact);
                yield return new WaitForSeconds(0.5f);
                Check(pc.UpgradesOpen && pc.MenuOpen, "(client) E on our upgrade station opens UPGRADES");
                int cel = UpgradeStation.Celebrations;
                yield return Snap("upgrades_client");
                int w0 = me.Count(Cfg.CurrencyItem), price = Cfg.BaseUpgradeRecipe(Item.FortifyBuff, team).Wood;
                me.BaseUpgradeRpc(Item.FortifyBuff);
                float until = Time.time + 5f;
                while (Cfg.FortifyLevel(team) < 1 && Time.time < until) yield return null;
                yield return new WaitForSeconds(0.5f);
                Check(Cfg.FortifyLevel(team) == 1 && w0 - me.Count(Cfg.CurrencyItem) == price && PlayerNet.UpgradesBought >= 1,
                    $"(client) bought Fortify All Walls level 1 for {w0 - me.Count(Cfg.CurrencyItem)} {Cfg.CurrencyName} (price {price})");
                Check(UpgradeStation.Celebrations > cel, "(client) our upgrade station celebrated (networked to this client)");
                pc.CloseMenu();
            }
            Log("(client) upgrades test done");
        }
    }
}
