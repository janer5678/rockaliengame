using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Workbench checks (run by -autotest modes in classic / arsenal / autowood / dna, from RequestTests, on a solo host):
    /// - the Workbench T1 is locked (grey in the bag: "Can only craft once the ball has been captured", refused by the server)
    ///   until the team captures the ball - classic checks 30 s with the ball in the base, the others the machine socket -
    ///   and then "WORKBENCH UNLOCKED" (banner + sound); the Workbench T2 costs 2000;
    /// - once unlocked it can be crafted in every situation a player gets into: on the bedrock, on the grass, on a
    ///   foundation, right after dying and respawning, with a full hotbar, after the team's bench was blown up (and the bag
    ///   says "bag full" instead of a green CRAFT that does nothing when there's really no room);
    /// - C4 stuck right on a workbench blows it up and it drops (pick it up, put it down again); on a chest it spills it;
    ///   C4 on a wall next to either doesn't touch them, and a rocket doesn't hurt a bench;
    /// - the one-shot portal gun (PortalGunTests);
    /// and ClientBenchCraft does the locked / unlock / craft / place check from a client (in the sd and ball tests).
    /// </summary>
    public partial class AutoTest
    {
        /// <summary>Open the bag and let the HUD draw the crafting list (Hud.CraftRowsShown) a couple of times.</summary>
        IEnumerator BagRows(PlayerController pc)
        {
            pc.MenuOpen = true;
            Hud.CraftRowsShown.Clear();
            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();
        }

        /// <summary>What the bag's crafting list shows for an item: "missing", "ok" (green CRAFT) or why not ("locked", "bag full", ...).</summary>
        static string RowState(Item id)
        {
            foreach (var r in Hud.CraftRowsShown) if (r.Id == id) return r.Problem ?? "ok";
            return "missing";
        }

        static Vector3 OnGround(Vector3 p, float up = 0.1f) { p.y = MapBuilder.Height(p.x, p.z) + up; return p; }

        static void Drop(PlayerNet me, Item id)
        {
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == id) me.Inv[i] = default;
        }

        /// <summary>Craft a Workbench T1 standing where you are (teleported to `at` first if given): the bag must show a green
        /// CRAFT and the server must hand one over for the price.</summary>
        IEnumerator BenchCraftAt(PlayerNet me, PlayerController pc, string what, Vector3? at, float yaw)
        {
            if (at.HasValue) { pc.LocalTeleport(at.Value, yaw); yield return new WaitForSeconds(0.4f); }
            var cur = Cfg.CurrencyItem;
            int price = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Workbench)).Wood;
            if (me.Count(cur) < price + 10) { me.ServerGive(cur, 1000); yield return new WaitForSeconds(0.2f); }
            yield return BagRows(pc);
            string row = RowState(Item.Workbench);
            int n0 = me.Count(Item.Workbench), w0 = me.Count(cur);
            me.CraftRpc(Cfg.RecipeIndex(Item.Workbench));
            yield return new WaitForSeconds(0.5f);
            pc.CloseMenu();
            Check(row == "ok" && me.Count(Item.Workbench) == n0 + 1 && w0 - me.Count(cur) == price,
                $"Workbench T1 crafted {what} (the bag says {row}; {me.Count(Item.Workbench) - n0} made for {w0 - me.Count(cur)} {Cfg.CurrencyName})");
            Drop(me, Item.Workbench); // (so the next check starts clean)
            yield return null;
        }

        IEnumerator BenchTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            if (Cfg.RecipeIndex(Item.Workbench) < 0) yield break;
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            var cur = Cfg.CurrencyItem;
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            float yaw = Cfg.SpawnYaw(team);
            var spawn = Cfg.SpawnPos(team);
            int benchIdx = Cfg.RecipeIndex(Item.Workbench);
            var itemsBefore = new HashSet<int>();
            foreach (var it in g.Items) itemsBefore.Add(it.Id);
            var r2 = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Workbench2));
            Check(Cfg.Workbench2Wood == 2000 && r2.Wood == 2000 && r2.Stone == 0, $"the Workbench T2 costs 2000 ({r2.Wood} {Cfg.CurrencyName})");
            for (int i = 0; i < 2; i++) me.ServerGive(cur, 1000);
            pc.LocalTeleport(spawn, yaw);
            yield return new WaitForSeconds(0.4f);

            // ---- locked until the team has captured the ball ----
            Check(!Cfg.BenchUnlocked(team) && !Cfg.BenchUnlocked(1 - team), "the Workbench T1 starts locked for every team");
            yield return BagRows(pc);
            Check(RowState(Item.Workbench) == "locked", $"in the bag the Workbench T1 is grey - \"{Cfg.BenchLockedText}\" ({RowState(Item.Workbench)})");
            yield return Snap($"bench_locked_{rn}");
            pc.CloseMenu();
            int w0 = me.Count(cur), b0 = me.Count(Item.Workbench);
            me.CraftRpc(benchIdx);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Workbench) == b0 && me.Count(cur) == w0, "the server won't make a locked Workbench T1 (nothing paid)");

            // ---- unlocking: classic checks 30 s with the ball in the base, the others the machine socket ----
            var ball = Ball.Instance;
            int notices = NetGame.BenchUnlockNotices;
            if (ball == null) { Check(false, "no ball for the workbench unlock"); yield break; }
            if (Cfg.Rules == GameRules.Classic)
            {
                ball.ServerDrop(Cfg.BaseCenter[team] - back * 5f + side * 3f + Vector3.up * 1.5f, Vector3.zero); // (lying in our base, away from the socket)
                float t0 = Time.time;
                yield return new WaitForSeconds(12f);
                int secs = g.BallInBaseSecondsOf(team);
                Check(!Cfg.BenchUnlocked(team) && secs >= 9 && secs <= 13 && Cfg.BaseTeamAt(ball.transform.position) == team,
                    $"the ball lying in our base counts towards the unlock ({secs} s after 12 s), still locked");
                yield return BagRows(pc);
                yield return Snap($"bench_locked_counting_{rn}");
                pc.CloseMenu();
                while (!Cfg.BenchUnlocked(team) && Time.time - t0 < 45f) yield return null;
                float took = Time.time - t0;
                Check(Cfg.BenchUnlocked(team) && took >= Cfg.BenchUnlockSeconds - 1.5f && took <= Cfg.BenchUnlockSeconds + 3f && !Cfg.BenchUnlocked(1 - team),
                    $"{Cfg.BenchUnlockSeconds:0} s with the ball in our base unlocks our Workbench T1 (after {took:0.0} s), not the other team's");
            }
            else
            {
                ball.ServerSocket(team);
                yield return new WaitForSeconds(0.5f);
                Check(Cfg.BenchUnlocked(team) && !Cfg.BenchUnlocked(1 - team), "putting the ball in our machine once unlocks our Workbench T1, not the other team's");
            }
            yield return new WaitForSeconds(0.3f);
            Check(NetGame.BenchUnlockNotices == notices + 1 && Hud.LastBanner.Contains("WORKBENCH UNLOCKED") && Hud.LastBanner.Contains("bag"),
                $"it tells us (with a sound): \"{Hud.LastBanner}\"");
            pc.LocalTeleport(spawn, yaw);
            yield return Snap($"bench_unlocked_{rn}");
            ball.ServerPlaceInDome();
            yield return new WaitForSeconds(0.3f);
            Check(Cfg.BenchUnlocked(team) && NetGame.BenchUnlockNotices == notices + 1, "it stays unlocked once the ball's gone again (and says it once)");

            // ---- now it can be crafted in every spot / state a player gets into ----
            yield return BenchCraftAt(me, pc, "on the bedrock (the spawn spot)", spawn, yaw);
            yield return BenchCraftAt(me, pc, "on the grass in the base", OnGround(Cfg.BaseCenter[team] - back * 7f + side * 3f), yaw);
            yield return BenchCraftAt(me, pc, "at the very edge of the base", OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf - 0.4f)), yaw);
            // standing on a foundation
            {
                me.ServerGive(Item.BuildingPlan, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.BuildingPlan);
                FreeCell(team, 6, out int ci, out int cj);
                var cell = BuildGrid.CellCenter(ci, cj);
                pc.LocalTeleport(OnGround(cell + new Vector3(-4f, 0, -1.5f)), 0f);
                yield return new WaitForSeconds(0.3f);
                int f0 = CountStructures(PieceType.Foundation, team);
                me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
                yield return new WaitForSeconds(0.6f);
                var on = cell;
                on.y = BuildGrid.LevelY(0) + 0.1f;
                pc.LocalTeleport(on, yaw);
                yield return new WaitForSeconds(0.5f);
                bool onFoundation = Physics.Raycast(me.transform.position + Vector3.up * 0.3f, Vector3.down, out var fh, 1.5f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)
                    && fh.collider.GetComponentInParent<Structure>() != null;
                Check(CountStructures(PieceType.Foundation, team) == f0 + 1 && onFoundation, $"standing on a foundation we built ({(onFoundation ? fh.collider.name : "nothing under us")})");
                yield return BenchCraftAt(me, pc, "standing on a foundation", null, yaw);
                Drop(me, Item.BuildingPlan);
                // (take it away again: the tests after this build in the base too)
                if (BuildGrid.Registry.TryGetValue(new PieceKey(PieceKey.KFoundation, ci, cj, 0, 0), out var built) && built != null && built.IsSpawned)
                    built.NetworkObject.Despawn(true);
            }
            // right after dying and respawning (everything spilled out: fresh wood after)
            {
                int graves0 = g.Graves.Count;
                me.DevRpc(DevCmd.KillMe);
                yield return new WaitForSeconds(0.4f);
                Check(me.Dead.Value, "(died for the respawn check)");
                float until = Time.time + Cfg.RespawnTime + 4f;
                while (me.Dead.Value && Time.time < until) yield return null;
                if (me.Dead.Value) me.ServerRespawn(false);
                yield return new WaitForSeconds(0.15f);
                Check(!me.Dead.Value && Cfg.BaseTeamAt(me.transform.position) == team, "back on our bedrock after dying");
                yield return BenchCraftAt(me, pc, "right after respawning", null, yaw);
                bool crosses = AllGravesCrosses(out string gwhy);
                Check(g.Graves.Count == graves0 + 1 && crosses, $"dying left a grave, and every grave is the stone cross ({gwhy})");
            }
            // with a full hotbar (it goes into the bag)
            {
                for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                me.ServerGive(cur, 1000);
                yield return new WaitForSeconds(0.2f);
                for (int i = 0; i < Cfg.HotbarSize; i++) if (me.SlotAt(i).Empty) me.Inv[i] = ItemStack.Of(Item.Spear, 1);
                yield return new WaitForSeconds(0.2f);
                yield return BenchCraftAt(me, pc, "with a full hotbar", spawn, yaw);
                // a full bag: the list says so instead of a green CRAFT that does nothing
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Empty) me.Inv[i] = ItemStack.Of(Item.Spear, 1);
                yield return new WaitForSeconds(0.2f);
                yield return BagRows(pc);
                string full = RowState(Item.Workbench);
                yield return Snap($"bench_bag_full_{rn}");
                pc.CloseMenu();
                int n0 = me.Count(Item.Workbench), c0 = me.Count(cur);
                me.CraftRpc(benchIdx);
                yield return new WaitForSeconds(0.5f);
                Check(full == "bag full" && me.Count(Item.Workbench) == n0 && me.Count(cur) == c0, $"with no room at all the bag says \"{full}\" and nothing is taken");
                Drop(me, Item.Spear);
                yield return new WaitForSeconds(0.2f);
            }

            // ---- one per team: once it's down it leaves the list; C4 stuck right on it blows it up and it drops ----
            var benchAt = OnGround(Cfg.BaseCenter[team] - back * 9f + side * 8f, 0f);
            float benchYaw = Quaternion.LookRotation(back).eulerAngles.y;
            Container bench = null;
            {
                me.ServerGive(cur, 1000);
                yield return BenchCraftAtKeep(me, pc, spawn, yaw);
                yield return Hold(me, Item.Workbench);
                pc.LocalTeleport(OnGround(benchAt + back * 2.5f), Quaternion.LookRotation(-back).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                me.PlaceDeployableRpc((byte)Item.Workbench, benchAt, benchYaw);
                yield return new WaitForSeconds(0.6f);
                bench = Workbench.ForTeam(team, 1);
                Check(bench != null && me.Count(Item.Workbench) == 0, "put our Workbench T1 down on the grass");
                if (bench == null) yield break;
                pc.LocalTeleport(spawn, yaw);
                yield return new WaitForSeconds(0.4f);
                yield return BagRows(pc);
                string row = RowState(Item.Workbench);
                pc.CloseMenu();
                int n0 = me.Count(Item.Workbench);
                me.CraftRpc(benchIdx);
                yield return new WaitForSeconds(0.5f);
                Check(row == "missing" && me.Count(Item.Workbench) == n0, $"with ours down, the Workbench T1 leaves the list and can't be made again ({row})");
            }
            // a rocket next to it doesn't hurt it (only C4 stuck right on it does)
            {
                var top = Workbench.Top(bench);
                g.ServerRocket(top + bench.transform.forward * 0.6f, me);
                yield return new WaitForSeconds(0.5f);
                Check(bench != null && bench.IsSpawned, "a rocket going off right next to the workbench doesn't hurt it");
            }
            // C4 on a wall right next to it: the wall goes, the bench doesn't
            {
                var wall = SpawnKeylessPiece(PieceType.Wall, team, benchAt + bench.transform.forward * 1.6f, bench.transform.rotation);
                yield return new WaitForSeconds(0.3f);
                var top = Workbench.Top(bench);
                bool aimed = Physics.Raycast(top, (wall.transform.position + Vector3.up * 1.2f - top).normalized, out var wh, 4f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)
                    && wh.collider.GetComponentInParent<Structure>() == wall;
                var c4 = aimed ? wh.point + wh.normal * 0.03f : wall.transform.position + Vector3.up * 1.2f;
                Check(aimed && NetGame.C4StuckBox(c4, wh.normal) == null, "(C4 stuck on the wall's face towards the bench, not on the bench)");
                g.ServerArmC4(c4, aimed ? wh.normal : -bench.transform.forward, me);
                yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                Check((wall == null || !wall.IsSpawned) && bench != null && bench.IsSpawned, $"C4 on a wall {Vector3.Distance(c4, top):0.0} m from the workbench: the wall goes, the bench stays");
            }
            // C4 stuck right on the bench: it blows up and drops as its item - pick it up and put it down again
            {
                var at = bench.transform.position;
                g.ServerArmC4(Workbench.Top(bench) + Vector3.up * 0.03f, Vector3.up, me);
                yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                int dropped = WorldItemNear(Item.Workbench, at, 3f);
                Check((bench == null || !bench.IsSpawned) && Workbench.ForTeam(team, 1) == null && dropped >= 0, $"C4 stuck on the workbench blows it up and it drops as an item ({(dropped >= 0 ? "dropped" : "no item")})");
                yield return BagRows(pc);
                string row = RowState(Item.Workbench);
                pc.CloseMenu();
                Check(row == "ok", $"with ours blown up, the Workbench T1 is back in the list ({row})");
                yield return BenchCraftAt(me, pc, "after ours was blown up", null, yaw);
                if (dropped >= 0)
                {
                    pc.LocalTeleport(OnGround(at + back * 1.2f), Quaternion.LookRotation(-back).eulerAngles.y);
                    yield return new WaitForSeconds(0.4f);
                    me.PickupItemRpc(dropped);
                    yield return new WaitForSeconds(0.4f);
                    Check(me.Count(Item.Workbench) == 1, "picked the dropped workbench back up");
                    yield return Hold(me, Item.Workbench);
                    pc.LocalTeleport(OnGround(benchAt + back * 2.5f), Quaternion.LookRotation(-back).eulerAngles.y);
                    yield return new WaitForSeconds(0.4f);
                    me.PlaceDeployableRpc((byte)Item.Workbench, benchAt, benchYaw);
                    yield return new WaitForSeconds(0.6f);
                    bench = Workbench.ForTeam(team, 1);
                    Check(bench != null && me.Count(Item.Workbench) == 0, "... and put it down again");
                }
            }

            // ---- chests: C4 stuck right on one spills it; C4 on a wall next to one doesn't touch it ----
            {
                var chestAt = OnGround(Cfg.BaseCenter[team] - back * 9f - side * 8f, 0f);
                var chest = SpawnChest(team, chestAt, new List<ItemStack> { ItemStack.Of(Item.Meat, 1) });
                var chest2 = SpawnChest(team, chestAt + side * 4.5f, new List<ItemStack> { ItemStack.Of(Item.Berry, 3) });
                var wall = SpawnKeylessPiece(PieceType.Wall, team, chestAt + side * 4.5f + back * 1.5f, Quaternion.LookRotation(back));
                yield return new WaitForSeconds(0.4f);
                var top2 = chest2.transform.position + Vector3.up * 0.5f;
                bool aimed = Physics.Raycast(top2, (wall.transform.position + Vector3.up * 1.2f - top2).normalized, out var wh, 4f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)
                    && wh.collider.GetComponentInParent<Structure>() == wall;
                var c4 = aimed ? wh.point + wh.normal * 0.03f : wall.transform.position + Vector3.up * 1.2f;
                g.ServerArmC4(c4, aimed ? wh.normal : -back, me);
                yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                Check(aimed && (wall == null || !wall.IsSpawned) && chest2 != null && chest2.IsSpawned && chest2.Slots.Count > 0 && chest2.Slots[0].Id == Item.Berry,
                    $"C4 on a wall {Vector3.Distance(c4, top2):0.0} m from a chest: the wall goes, the chest (and what's in it) stays");
                g.ServerArmC4(chest.transform.position + Vector3.up * 0.69f, Vector3.up, me);
                yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                Check((chest == null || !chest.IsSpawned) && WorldItemNear(Item.Meat, chestAt, 3f) >= 0, "C4 stuck right on a chest blows it open: what was in it spills out");
                if (chest2 != null && chest2.IsSpawned) chest2.NetworkObject.Despawn(true);
            }
            // tidy up for the tests after this: nothing we spilled or dropped stays lying around
            var left = new List<DroppedItem>();
            foreach (var it in g.Items) if (!itemsBefore.Contains(it.Id)) left.Add(it);
            foreach (var it in left) g.ServerTakeItem(it.Id, it.Center, 1f, 65535);
            pc.LocalTeleport(spawn, yaw);
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Craft a Workbench T1 and keep it (for putting down).</summary>
        IEnumerator BenchCraftAtKeep(PlayerNet me, PlayerController pc, Vector3 at, float yaw)
        {
            pc.LocalTeleport(at, yaw);
            yield return new WaitForSeconds(0.3f);
            me.CraftRpc(Cfg.RecipeIndex(Item.Workbench));
            yield return new WaitForSeconds(0.5f);
        }

        static Structure SpawnKeylessPiece(PieceType t, int team, Vector3 at, Quaternion rot)
        {
            var go = Instantiate(Bootstrap.I.structurePrefab, at, rot);
            var s = go.GetComponent<Structure>();
            s.ServerInit(t, team, default, false);
            go.GetComponent<NetworkObject>().Spawn(true);
            return s;
        }

        static Container SpawnChest(int team, Vector3 at, List<ItemStack> contents)
        {
            var go = Instantiate(Bootstrap.I.containerPrefab, at, Quaternion.identity);
            var c = go.GetComponent<Container>();
            c.ServerInit(Container.Chest, team, Cfg.ChestSlots, contents);
            go.GetComponent<NetworkObject>().Spawn(true);
            return c;
        }

        /// <summary>
        /// The portal gun: ONE shot makes the whole linked pair - a portal where you stand and one where it lands - and uses
        /// the gun up; too close is refused (nothing used); the portal under you doesn't take you until you've stepped off it;
        /// walking back into it does; and there are never more than NetGame.MaxPortals.
        /// </summary>
        IEnumerator PortalGunTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            float yaw = Quaternion.LookRotation(-back).eulerAngles.y;
            int p0 = g.Portals.Count;
            me.ServerGive(Item.PortalGun, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.PortalGun);
            var stand = OnGround(Cfg.SpawnPos(team) - back * 9f);
            pc.LocalTeleport(stand, yaw);
            yield return new WaitForSeconds(0.5f);
            stand = me.transform.position;
            // too close: refused, nothing used up
            me.PortalRpc(OnGround(stand + side * 1.2f, 0f), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            Check(g.Portals.Count == p0 && me.Count(Item.PortalGun) == 1, "a portal shot right next to you is refused (the gun isn't used up)");
            var shot = OnGround(stand - back * 12f, 0f);
            me.PortalRpc(shot, Vector3.up);
            yield return new WaitForSeconds(0.6f);
            bool pair = g.Portals.Count == p0 + 2 && g.Portals[p0].Pair == g.Portals[p0 + 1].Pair;
            Check(pair && me.Count(Item.PortalGun) == 0 && Flat(g.Portals[p0].Pos, stand) < 1f && Flat(g.Portals[p0 + 1].Pos, shot) < 0.5f,
                $"ONE portal gun shot opens a linked pair - one where you stand, one where it lands - and the gun is used up ({g.Portals.Count - p0} portals, {me.Count(Item.PortalGun)} gun)");
            yield return new WaitForSeconds(0.8f);
            Check(Flat(me.transform.position, stand) < 1f, $"the portal that opened under you doesn't take you until you step off it ({Flat(me.transform.position, stand):0.0} m)");
            pc.SetLook(yaw, 22f);
            yield return Snap($"portal_pair_{rn}");
            if (pair)
            {
                pc.LocalTeleport(stand + side * 4f, yaw);
                yield return new WaitForSeconds(0.5f);
                pc.LocalTeleport(stand, yaw);
                yield return new WaitForSeconds(0.6f);
                Check(Flat(me.transform.position, shot) < 2.5f, $"step off it and walk back in: out of the other one ({Flat(me.transform.position, shot):0.0} m from it)");
            }
            for (int i = 0; i < 40; i++) g.ServerAddPortal(stand + Vector3.down * 50f, Vector3.up, g.ServerNewPortalPair());
            Check(g.Portals.Count <= NetGame.MaxPortals, $"no more than {NetGame.MaxPortals} portals at once ({g.Portals.Count})");
            g.Portals.Clear();
            Drop(me, Item.PortalGun);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>
        /// A client (not the host) crafting the workbench: locked first (grey, refused), then unlocked (the dev setting here),
        /// then crafted from the list and put down on the foundation it built. (The client's step in the sd / ball tests.)
        /// </summary>
        IEnumerator ClientBenchCraft(PlayerNet me, PlayerController pc, int team, Vector3 foundation)
        {
            if (Cfg.RecipeIndex(Item.Workbench) < 0 || Cfg.Builder) yield break;
            int benchIdx = Cfg.RecipeIndex(Item.Workbench);
            var cur = Cfg.CurrencyItem;
            me.DevRpc(DevCmd.GiveWood);
            pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.6f);
            int notices = NetGame.BenchUnlockNotices;
            if (!Cfg.BenchUnlocked(team))
            {
                yield return BagRows(pc);
                string locked = RowState(Item.Workbench);
                pc.CloseMenu();
                int w0 = me.Count(cur);
                me.CraftRpc(benchIdx);
                yield return new WaitForSeconds(0.6f);
                Check(locked == "locked" && me.Count(Item.Workbench) == 0 && me.Count(cur) == w0, $"(client) the Workbench T1 is locked until the ball's been captured ({locked})");
                me.DevRpc(DevCmd.UnlockBench);
                yield return new WaitForSeconds(0.8f);
                Check(Cfg.BenchUnlocked(team) && NetGame.BenchUnlockNotices == notices + 1 && Hud.LastBanner.Contains("WORKBENCH UNLOCKED"), $"(client) unlocked, and the client is told ({Hud.LastBanner})");
            }
            // standing on the foundation we built
            var on = foundation;
            on.y = BuildGrid.LevelY(0) + 0.1f;
            pc.LocalTeleport(on, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.6f);
            yield return BagRows(pc);
            string row = RowState(Item.Workbench);
            pc.CloseMenu();
            int n0 = me.Count(Item.Workbench);
            me.CraftRpc(benchIdx);
            yield return new WaitForSeconds(0.8f);
            Check(row == "ok" && me.Count(Item.Workbench) == n0 + 1, $"(client) crafted a Workbench T1 standing on our foundation (the bag says {row})");
            if (me.Count(Item.Workbench) == 0) yield break;
            yield return Hold(me, Item.Workbench);
            var back = Cfg.BackDir(team);
            var at = Cfg.BaseCenter[team] - back * 9f - Vector3.Cross(Vector3.up, back) * 8f;
            at.y = MapBuilder.Height(at.x, at.z);
            pc.LocalTeleport(OnGround(at + back * 2.5f), Quaternion.LookRotation(-back).eulerAngles.y);
            yield return new WaitForSeconds(0.6f);
            me.PlaceDeployableRpc((byte)Item.Workbench, at, Quaternion.LookRotation(back).eulerAngles.y);
            yield return new WaitForSeconds(0.8f);
            Check(Workbench.ForTeam(team, 1) != null && me.Count(Item.Workbench) == 0, "(client) put it down in our base");
            pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }
    }
}
