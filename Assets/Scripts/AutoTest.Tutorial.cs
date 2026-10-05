using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The tutorial test (-autotest modes -rules tutorial, on a host with -solo, or a host plus a client that joins it):
    /// plays the whole tutorial step by step, mostly with pretend key presses (Binds.TestHold / TestPress) that go
    /// through the same gates as real keys, and checks that every control is locked until the step that teaches it.
    /// No stone anywhere; the bow and the training dummies come before the ball, the ball is captured before the trade
    /// station (that's what unlocks it), then the chest, a real airdrop, the hut to raid and the upgrade station. Solo
    /// (-solo) it ends with the finale: the clock runs out with the ball in our machine and the victory cutscene plays.
    /// Without -solo it's "with a friend": the host stops at the friend step until the client has joined; the client
    /// (TutorialJoinerTests) starts at that step with a starter kit; then the duel and a short match for the ball.
    /// </summary>
    public partial class AutoTest
    {
        string m_TutWho;

        /// <summary>Wait (up to `secs`) for the guide to be on step `id` (a done step ticks, then moves on a second later).</summary>
        IEnumerator TutWaitStep(string id, float secs = 6f)
        {
            float until = Time.time + secs;
            while (Tutorial.StepId != id && Time.time < until) yield return null;
            if (Tutorial.StepId == id) yield return new WaitForSeconds(0.3f); // (the server hears about the new step a moment later)
        }

        IEnumerator TutShot(string what) => Snap($"tutorial_{m_TutWho}_{what}");

        static bool InMyHalf(Vector3 p, PlayerNet me) => Cfg.RegionOf(p) == Cfg.RegionOf(me.transform.position) || Cfg.RegionOf(p) == Cfg.RegionOf(Cfg.BaseCenter[me.Team.Value]);

        static ResourceNode TutNearest(byte kind, PlayerNet me)
        {
            ResourceNode best = null;
            float bd = float.MaxValue;
            var from = Cfg.BaseCenter[me.Team.Value];
            foreach (var n in ResourceNode.All)
            {
                if (n == null || !n.IsSpawned || n.Kind.Value != kind || n.Amount.Value <= 0) continue;
                if (Cfg.RegionOf(n.transform.position) != Cfg.RegionOf(from)) continue; // our side of the glass
                float d = (n.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        /// <summary>Walk up to a tree or rock and really hit it (held LMB), aiming at its weak spot once it shows,
        /// until `done` or the time runs out.</summary>
        IEnumerator TutHitNodes(PlayerNet me, PlayerController pc, byte kind, System.Func<bool> done, float secs)
        {
            float until = Time.time + secs;
            ResourceNode node = null;
            Binds.TestHold(Bind.Attack, true);
            while (!done() && Time.time < until)
            {
                if (node == null || !node.IsSpawned || node.Amount.Value <= 0)
                {
                    Binds.TestHold(Bind.Attack, false);
                    node = TutNearest(kind, me);
                    if (node == null) { Log("FAIL: no resource node to hit"); break; }
                    var dir = Cfg.BaseCenter[me.Team.Value] - node.transform.position;
                    dir.y = 0;
                    dir.Normalize();
                    var at = node.transform.position + dir * (kind == ResourceNode.Tree ? 1.9f : 2.3f);
                    at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
                    pc.LocalTeleport(at, Quaternion.LookRotation(-dir).eulerAngles.y);
                    yield return new WaitForSeconds(0.3f);
                    Binds.TestHold(Bind.Attack, true);
                }
                var aim = node.transform.position + Vector3.up * (kind == ResourceNode.Tree ? 1.2f : 0.5f);
                if (node.TryGetSpot(out var spot, out _) && Vector3.Distance(spot, me.EyePos) < 3.2f) aim = spot;
                LookAt(pc, me, aim);
                yield return null;
            }
            Binds.TestHold(Bind.Attack, false);
            yield return null;
        }

        IEnumerator TutPress(Bind b, float wait = 0.35f)
        {
            Binds.TestPress(b);
            yield return new WaitForSeconds(wait);
        }

        /// <summary>Pick a hotbar item with its number key (like a player would).</summary>
        IEnumerator TutSelect(PlayerNet me, Item id)
        {
            int s = me.HotbarSlotOf(id);
            if (s < 0) { yield return Hold(me, id); yield break; } // (not on the hotbar: drag it there)
            yield return TutPress(Bind.Hotbar1 + s);
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }

        /// <summary>Training dummies standing right now.</summary>
        static int TutDummies()
        {
            int n = 0;
            foreach (var v in Vehicle.All) if (v != null && v.IsSpawned && v.IsDummy && v.Hp.Value > 0f) n++;
            return n;
        }

        /// <summary>Stand `dist` metres from the nearest training dummy (on the base's side of it), pick the bow, draw it
        /// right back with a held LMB and let go at the target on its chest.</summary>
        IEnumerator TutShootDummy(PlayerNet me, PlayerController pc, int team, float dist)
        {
            var d = Tutorial.NearestDummy();
            if (d == null) { Log("FAIL: no training dummy to shoot at"); yield break; }
            var at = d.transform.position + Cfg.BackDir(team) * dist;
            at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
            pc.LocalTeleport(at, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            yield return TutSelect(me, Item.Bow);
            if (d == null || !d.IsSpawned) yield break;
            LookAt(pc, me, d.transform.position + Vector3.up * 1.15f);
            yield return new WaitForSeconds(0.15f);
            Binds.TestHold(Bind.Attack, true);
            yield return new WaitForSeconds(Cfg.BowDrawTime + 0.3f);
            if (d != null && d.IsSpawned) LookAt(pc, me, d.transform.position + Vector3.up * 1.15f);
            yield return null;
            Binds.TestHold(Bind.Attack, false);
            yield return new WaitForSeconds(1.2f);
        }

        /// <summary>Tutorial: the map is forced to small Plains, the clock is stopped, there are no scheduled airdrops,
        /// every control is locked until its step, steps only move on when they're done (no Enter), the bow and the
        /// dummies, the wall drops once the player has walked up to it, the ball steps, the trade station, the chest,
        /// the airdrop, the raid, the upgrade station and the finale. On a client that joins (-autotest modes -client
        /// ...) it's TutorialJoinerTests after the first checks.</summary>
        IEnumerator TutorialTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            bool host = NetworkManager.Singleton.IsServer;
            string who = m_TutWho = host ? "host" : "client";
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(1f);
            Check(Cfg.Map == MapKind.Plains && Cfg.Size == MapSize.Small && Cfg.WoodMode == Cfg.WoodIsNormal && g.MapKey.Value == Cfg.MapKey,
                $"Tutorial ({who}): the map is forced to small Plains, the normal (wood) materials ({Cfg.MapLabel})");
            Check(Cfg.Tutorial && !Cfg.LimitedCrafting && Cfg.RecipeIndex(Item.Workbench) >= 0 && (g.S == GameState.PreBall || !host) && g.TimerPaused.Value,
                $"Tutorial ({who}): playing the classic crafting (starter items + trade stations), the clock stopped");
            Check(Cfg.HasBaseUpgrades && Cfg.BaseUpgradeOn(Item.WoodGenBuff) && Cfg.AutoWood && !Cfg.PowerMenu && Cfg.PowerCount == 0 && UpgradeStation.ByTeam[team] != null && Cfg.WoodGenLevel(team) == 0,
                $"Tutorial ({who}): it has the upgrade station and the wood gen (no wood machine yet), but no power items");
            Check(Tutorial.Friend == !(host && Bootstrap.Solo), $"Tutorial ({who}): {(Tutorial.Friend ? "with a friend" : "solo")} (solo: {Bootstrap.Solo})");
            // a player who joined mid-tutorial gets dropped straight into their base
            float until = Time.time + 10f;
            while (Cfg.BaseTeamAt(me.transform.position) != team && Time.time < until) yield return null;
            Check(Cfg.BaseTeamAt(me.transform.position) == team, $"Tutorial ({who}): in my own base, not the waiting stadium ({me.transform.position})");
            float t0 = g.TimeLeft;
            yield return new WaitForSeconds(2f);
            Check(Mathf.Abs(g.TimeLeft - t0) < 0.5f, $"the clock doesn't move ({t0:0.0} -> {g.TimeLeft:0.0})");
            if (host)
            {
                // (a client joins whenever it's started: the host may be past these by then)
                Check(g.LaneStartAt(0) < 0 && g.LaneStartAt(team) < 0 && g.NextDropLands.Value < 0, "no airdrops in the tutorial before its airdrop step");
                Check(Ball.Instance != null && MapBuilder.GlassUp && new Vector2(Ball.Instance.transform.position.x, Ball.Instance.transform.position.z).magnitude < 0.5f && !Ball.Instance.IsCarried,
                    "the ball is already in the middle, under the glass dome");
            }
            var ids = Tutorial.StepIds();
            Log($"tutorial steps ({ids.Count}): {string.Join(" ", ids)}");
            Check(Tutorial.StepCount >= 30 && Tutorial.StepId == (host ? "machine" : "friend") && !Tutorial.HasEnterKey,
                $"the guide is on {(host ? "step 1" : "the with-a-friend step")} of {Tutorial.StepCount} ({Tutorial.StepId}), no Enter to skip");
            // everything unlocks in the order it's taught
            Check(Tutorial.UnlockStep(TutFeature.Move) < Tutorial.UnlockStep(TutFeature.Sprint) && Tutorial.UnlockStep(TutFeature.Sprint) < Tutorial.UnlockStep(TutFeature.Crouch)
                && Tutorial.UnlockStep(TutFeature.Crouch) < Tutorial.UnlockStep(TutFeature.Slide) && Tutorial.UnlockStep(TutFeature.Slide) < Tutorial.UnlockStep(TutFeature.Hit)
                && Tutorial.UnlockStep(TutFeature.Hit) < Tutorial.UnlockStep(TutFeature.Inventory) && Tutorial.UnlockStep(TutFeature.Inventory) < Tutorial.UnlockStep(TutFeature.Craft)
                && Tutorial.UnlockStep(TutFeature.Craft) < Tutorial.UnlockStep(TutFeature.Build) && Tutorial.UnlockStep(TutFeature.Build) < Tutorial.UnlockStep(TutFeature.Demolish)
                && Tutorial.UnlockStep(TutFeature.Demolish) < ids.IndexOf("glass") && ids.IndexOf("glass") < ids.IndexOf("score") && ids.IndexOf("score") < ids.IndexOf("bench")
                && ids.IndexOf("bench") < Tutorial.UnlockStep(TutFeature.Workbench),
                "the steps go look > move > run/jump > crouch > slide > hit > bag > craft > build > break your own > the wall > capture the ball > trade station");
            // the new order: the bow and the dummies before the wall and the trade station; then the chest, the airdrop,
            // the raid, the upgrade station, the friend steps and the finale
            {
                int I(string id) => ids.IndexOf(id);
                Check(I("throw") < I("bow") && I("bow") < I("shoot") && I("shoot") < I("melee") && I("melee") < I("ranged") && I("ranged") < I("berry") && I("ranged") < I("glass") && I("ranged") < I("bench"),
                    "crafting and shooting a bow, and the training dummies (melee, ranged), come before the wall and the trade station");
                Check(I("newcrafts") < I("chest") && I("chest") < I("placechest") && I("placechest") < I("usechest") && I("usechest") < I("airdrop") && I("airdrop") < I("loot") && I("loot") < I("raid")
                    && I("raid") < I("raidloot") && I("raidloot") < I("upgrade") && I("upgrade") < I("friend") && I("friend") < I("duel") && I("duel") < I("win") && I("win") == ids.Count - 1,
                    "then: a chest (craft, place, fill) > an airdrop (meet it, loot it) > a raid (break in, loot) > the upgrade station > with a friend > the duel > the finale");
                Check(Tutorial.UnlockStep(TutFeature.Station) == I("upgrade") && Tutorial.UnlockStep(TutFeature.Deploy) < I("chest"), "the upgrade station unlocks on its own step; putting things down before the chest");
            }
            Check(!ids.Contains("stone") && !ids.Contains("mine") && !ids.Contains("pickaxe") && !ids.Contains("ram"), $"no stone steps in the tutorial ({string.Join(" ", ids)})");
            {
                // nothing the tutorial teaches you to craft needs stone
                bool noStone = true;
                foreach (var it in new[] { Item.Hatchet, Item.BuildingPlan, Item.Spear, Item.Bow, Item.Arrow, Item.Workbench, Item.Chest })
                    noStone &= Cfg.RecipeIndex(it) >= 0 && Cfg.GetRecipe(Cfg.RecipeIndex(it)).Stone == 0;
                Check(noStone, "nothing the tutorial has you craft needs stone (hatchet, building plan, spear, bow, arrows, trade station, chest)");
            }
            // whoever joined a friend's tutorial: no early steps - straight to the part played together
            if (!host) { yield return TutorialJoinerTests(me, pc, g, team); yield break; }

            // ---- step 1: you can only look around ----
            var spawn = Cfg.SpawnPos(team, me.Slot.Value);
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.5f);
            Check(!Tutorial.Allows(TutFeature.Move) && !Tutorial.Allows(TutFeature.Jump) && !Tutorial.Allows(TutFeature.Hit) && !Tutorial.Allows(TutFeature.Inventory)
                && !Tutorial.Allows(TutFeature.Craft) && !Tutorial.Allows(TutFeature.HotbarHud) && !Tutorial.Allows(TutFeature.Health) && !Tutorial.Allows(TutFeature.Interact),
                $"({who}) step 1: only looking around is unlocked");
            var p0 = me.transform.position;
            float yMax = p0.y;
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Right, true);
            Binds.TestPress(Bind.Jump);
            for (float t = 0; t < 1.2f; t += Time.deltaTime) { yMax = Mathf.Max(yMax, me.transform.position.y); yield return null; }
            Binds.TestReleaseAll();
            Check(Flat(me.transform.position, p0) < 0.15f && yMax - p0.y < 0.25f, $"({who}) W/D and Space do nothing before the walk step (moved {Flat(me.transform.position, p0):0.00} m, up {yMax - p0.y:0.00} m)");
            yield return TutPress(Bind.Inventory);
            Check(!pc.MenuOpen, $"({who}) the bag key ({Binds.Name(Bind.Inventory)}) doesn't open the bag before the bag step");
            Binds.TestPress(Bind.Attack);
            yield return TutShot("step1_locked"); // (just the panel: no hotbar, no health bar - and "Not yet!")
            yield return new WaitForSeconds(1.5f);
            Check(Tutorial.StepId == "machine", $"step 1 waits while you look away ({Tutorial.StepId})");
            var toMachine = Cfg.MachinePos(team) - spawn;
            toMachine.y = 0;
            pc.LocalTeleport(spawn, Quaternion.LookRotation(toMachine).eulerAngles.y);
            yield return TutWaitStep("walk");
            Check(Tutorial.StepId == "walk" && Tutorial.Allows(TutFeature.Move) && !Tutorial.Allows(TutFeature.Sprint), $"looking at the machine ticks step 1 off and unlocks walking ({Tutorial.StepId})");

            // ---- walk (really, with W): Shift doesn't run yet ----
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            p0 = me.transform.position;
            bool ranEarly = false;
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            until = Time.time + 8f;
            while (Tutorial.StepId == "walk" && Time.time < until) { ranEarly |= pc.Sprinting; yield return null; }
            Binds.TestReleaseAll();
            Check(Tutorial.StepId == "sprint" && Flat(me.transform.position, p0) > 7f && !ranEarly,
                $"({who}) holding W really walks and ticks the walk step off (walked {Flat(me.transform.position, p0):0.0} m; Shift didn't run yet: {!ranEarly})");
            yield return TutShot("walk_done");

            // ---- run and jump: crouch is still locked ----
            Binds.TestHold(Bind.Crouch, true);
            yield return new WaitForSeconds(0.4f);
            Check(!pc.Crouching, $"({who}) Ctrl doesn't crouch before the crouch step");
            Binds.TestReleaseAll();
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.2f);
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.8f);
            Binds.TestPress(Bind.Jump);
            until = Time.time + 6f;
            while (Tutorial.StepId == "sprint" && Time.time < until) yield return null;
            Binds.TestReleaseAll();
            Check(Tutorial.StepId == "crouch", $"({who}) running and jumping ticks the run step off ({Tutorial.StepId})");

            // ---- crouch: the slide key (C) is still locked ----
            Binds.TestHold(Bind.Slide, true);
            yield return new WaitForSeconds(0.4f);
            Check(!pc.Crouching && !pc.Sliding, $"({who}) C does nothing before the slide step");
            Binds.TestReleaseAll();
            Binds.TestHold(Bind.Crouch, true);
            yield return new WaitForSeconds(0.4f);
            Check(pc.Crouching, $"({who}) Ctrl crouches on the crouch step");
            until = Time.time + 5f;
            while (Tutorial.StepId == "crouch" && Time.time < until) yield return null;
            Binds.TestReleaseAll();
            Check(Tutorial.StepId == "slide", $"({who}) crouching ticks the crouch step off ({Tutorial.StepId})");

            // ---- slide: run, then C ----
            Check(!Tutorial.Allows(TutFeature.Hit), $"({who}) hitting is still locked on the slide step");
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.2f);
            for (int tries = 0; tries < 3 && Tutorial.StepId == "slide"; tries++)
            {
                Binds.TestHold(Bind.Forward, true);
                Binds.TestHold(Bind.Sprint, true);
                yield return new WaitForSeconds(0.7f);
                Binds.TestHold(Bind.Slide, true);
                yield return new WaitForSeconds(0.6f);
                Binds.TestReleaseAll();
                yield return TutWaitStep("chop", 2.5f);
                pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            }
            Check(Tutorial.StepId == "chop" && Tutorial.Allows(TutFeature.Hit) && Tutorial.Allows(TutFeature.HotbarHud) && !Tutorial.Allows(TutFeature.Inventory),
                $"({who}) a slide ticks the slide step off; hitting and the hotbar unlock ({Tutorial.StepId})");

            // ---- chop and hit the X (really: held LMB at a tree) ----
            int wood0 = me.Count(Item.Wood);
            yield return TutHitNodes(me, pc, ResourceNode.Tree, () => Tutorial.StepId != "chop" && Tutorial.StepId != "weak", 60f);
            Check(Tutorial.StepId == "wood" && me.Count(Item.Wood) > wood0, $"({who}) chopping a tree with the rock and hitting its X tick off the chop steps ({me.Count(Item.Wood) - wood0} wood, {Tutorial.WeakHits} X hits, step {Tutorial.StepId})");
            yield return TutShot("hotbar"); // the hotbar showed up with the wood

            // ---- more wood; the bag key is still locked ----
            yield return TutPress(Bind.Inventory);
            Check(!pc.MenuOpen, $"({who}) the bag key still doesn't open the bag on the wood step");
            me.DevRpc(DevCmd.GiveWood); // (the rest of the wood the tutorial needs)
            yield return TutWaitStep("bag");
            Check(Tutorial.StepId == "bag" && Tutorial.Allows(TutFeature.Inventory) && !Tutorial.Allows(TutFeature.Craft), $"({who}) 100 wood ticks off the wood step; the bag unlocks, crafting doesn't yet ({Tutorial.StepId})");

            // ---- the bag: crafting is refused (by the server too) before the crafting step ----
            int w0 = me.Count(Item.Wood);
            me.CraftRpc(Cfg.RecipeIndex(Item.Hatchet));
            yield return new WaitForSeconds(0.8f);
            Check(me.Count(Item.Hatchet) == 0 && me.Count(Item.Wood) == w0, $"({who}) crafting is refused before the crafting step");
            yield return TutPress(Bind.Inventory);
            Check(pc.MenuOpen, $"({who}) the bag key opens the bag on the bag step");
            yield return TutShot("bag"); // no crafting list yet
            yield return TutWaitStep("hatchet");
            Check(Tutorial.StepId == "hatchet" && Tutorial.Allows(TutFeature.Craft) && Tutorial.AllowsItem(Item.Hatchet) && !Tutorial.AllowsItem(Item.BuildingPlan)
                && !Tutorial.AllowsItem(Item.Bow) && !Tutorial.AllowsItem(Item.Workbench) && !Tutorial.AllowsItem(Item.Pickaxe),
                $"({who}) opening the bag ticks it off; the crafting list has just the hatchet ({Tutorial.StepId})");
            yield return TutShot("craft_hatchet");
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.BuildingPlan) == 0, $"({who}) the building plan can't be crafted yet");
            pc.CloseMenu();
            int slot0 = me.HeldSlot.Value;
            yield return TutPress(Bind.Hotbar5);
            yield return TutPress(Bind.Hotbar6);
            Check(me.HeldSlot.Value == slot0, $"({who}) the number keys don't pick hotbar slots yet (slot {slot0} -> {me.HeldSlot.Value})");
            me.CraftRpc(Cfg.RecipeIndex(Item.Hatchet));
            yield return TutWaitStep("axechop");
            Check(me.Count(Item.Hatchet) == 1 && Tutorial.StepId == "axechop" && Tutorial.Allows(TutFeature.Hotbar), $"({who}) crafted the hatchet; the hotbar keys unlock ({Tutorial.StepId})");

            // ---- the axe: its number key, then chop ----
            yield return TutSelect(me, Item.Hatchet);
            Check(me.HeldItem == Item.Hatchet, $"({who}) the hatchet's number key picks it ({me.HeldItem})");
            yield return TutHitNodes(me, pc, ResourceNode.Tree, () => Tutorial.StepId != "axechop", 12f);
            if (Tutorial.StepId == "axechop") { me.DevRpc(DevCmd.GiveWood); yield return TutWaitStep("home"); }
            Check(Tutorial.StepId == "home", $"({who}) chopping with the axe ticks it off ({Tutorial.StepId})");

            // ---- home, the plan, and a floor ----
            Check(!Tutorial.Allows(TutFeature.Build), $"({who}) building is still locked");
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return TutWaitStep("plan");
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return TutWaitStep("floor");
            Check(Tutorial.StepId == "floor" && me.Count(Item.BuildingPlan) == 1, $"({who}) home, then the building plan ({Tutorial.StepId})");
            yield return TutSelect(me, Item.BuildingPlan);
            yield return TutPress(Bind.Aim);
            Check(!pc.WheelOpen, $"({who}) RMB doesn't open the building wheel before the wall step");
            FreeCell(team, me.Slot.Value * 3, out int ci, out int cj);
            var cell = BuildGrid.CellCenter(ci, cj);
            cell.y = BuildGrid.LevelY(0);
            pc.LocalTeleport(cell + new Vector3(-4f, 0.1f, -1.5f), 0f);
            yield return new WaitForSeconds(0.3f);
            LookAt(pc, me, cell);
            until = Time.time + 2f;
            while (!pc.GhostOk && Time.time < until) yield return null;
            bool ghost = pc.GhostOk;
            if (ghost) { ci = pc.GhostKey.I; cj = pc.GhostKey.J; cell = BuildGrid.CellCenter(ci, cj); cell.y = BuildGrid.LevelY(0); } // (where the click puts it)
            yield return TutPress(Bind.Attack, 0.8f);
            if (CountStructures(PieceType.Foundation, team) == 0)
            {
                Log($"(clicking didn't place it - ghost ok {ghost} - placing by RPC)");
                me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            }
            yield return TutWaitStep("wall");
            Check(Tutorial.StepId == "wall", $"({who}) placing a foundation ticks off the floor step (by clicking: {ghost}) ({Tutorial.StepId})");

            // ---- the wheel, a wall and a doorway ----
            Check(!Tutorial.Allows(TutFeature.Interact), $"({who}) E is still locked on the wall step");
            Binds.TestHold(Bind.Aim, true);
            yield return new WaitForSeconds(0.3f);
            Check(pc.WheelOpen, $"({who}) holding RMB opens the building wheel on the wall step");
            pc.SelectWheel(2); // (the mouse onto Wall)
            Binds.TestHold(Bind.Aim, false);
            yield return new WaitForSeconds(0.3f);
            Check(!pc.WheelOpen && pc.BuildPiece == PieceType.Wall, $"({who}) letting go picks the Wall ({pc.BuildPiece})");
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 0);
            yield return TutWaitStep("door");
            Check(Tutorial.StepId == "door" && Tutorial.Allows(TutFeature.Interact), $"({who}) a wall ticks off the wall step; E unlocks ({Tutorial.StepId})");
            yield return TutShot("door");
            int doors0 = CountStructures(PieceType.Doorway, team);
            foreach (var k2 in new[] { new PieceKey(PieceKey.KEdge, ci, cj, 0, 1), new PieceKey(PieceKey.KEdge, ci + 1, cj, 0, 0), new PieceKey(PieceKey.KEdge, ci, cj + 1, 0, 1) })
            {
                if (CountStructures(PieceType.Doorway, team) > doors0) break;
                me.PlaceRpc((byte)PieceType.Doorway, k2.I, k2.J, k2.L, k2.D);
                yield return new WaitForSeconds(0.6f);
            }
            yield return TutWaitStep("breakwall");
            Check(Tutorial.StepId == "breakwall" && Tutorial.Allows(TutFeature.Demolish), $"({who}) a doorway ticks off the door step ({Tutorial.StepId})");

            // ---- break your own wall (X, with the plan, aiming at it) ----
            Structure wall = null;
            foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.Team.Value == team && s.PType == PieceType.Wall) wall = s;
            if (wall != null)
            {
                var wc = wall.transform.position + Vector3.up * 1.5f;
                var off = wc - cell;
                off.y = 0;
                var stand = wc + (off.sqrMagnitude > 0.01f ? off.normalized : Vector3.right) * 2.2f;
                stand.y = BuildGrid.LevelY(0) + 0.1f;
                pc.LocalTeleport(stand, 0f);
                yield return new WaitForSeconds(0.3f);
                LookAt(pc, me, wc);
                yield return new WaitForSeconds(0.1f);
                yield return TutPress(Bind.Demolish, 0.8f);
                if (wall != null && wall.IsSpawned) { Log("(X didn't reach the wall - demolishing by RPC)"); me.DemolishRpc(wall.NetworkObject); }
            }
            yield return TutWaitStep("spear");
            Check(Tutorial.StepId == "spear" && !Tutorial.Allows(TutFeature.Throw), $"({who}) taking down your own wall ticks it off ({Tutorial.StepId})");

            // ---- a spear: craft, throw (RMB + LMB), pick it up (E) ----
            me.CraftRpc(Cfg.RecipeIndex(Item.Spear));
            yield return TutWaitStep("throw");
            Check(Tutorial.StepId == "throw" && Tutorial.Allows(TutFeature.Throw), $"({who}) crafted a spear ({Tutorial.StepId})");
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            yield return TutSelect(me, Item.Spear);
            pc.SetLook(Cfg.SpawnYaw(team), -8f);
            int throws0 = Tutorial.SpearThrows;
            Binds.TestHold(Bind.Aim, true);
            yield return new WaitForSeconds(0.7f);
            yield return TutPress(Bind.Attack, 0.2f);
            Binds.TestHold(Bind.Aim, false);
            yield return new WaitForSeconds(3f);
            Check(Tutorial.SpearThrows > throws0 && me.Count(Item.Spear) == 0, $"({who}) holding RMB and clicking throws the spear");
            int sid = -1;
            Vector3 sc = Vector3.zero;
            float sd = float.MaxValue;
            foreach (var it in g.Items)
                if (it.Stack.Id == Item.Spear && Vector3.Distance(it.Pos, me.transform.position) < sd) { sd = Vector3.Distance(it.Pos, me.transform.position); sid = it.Id; sc = it.Center; }
            if (sid >= 0)
            {
                var d = me.transform.position - sc;
                d.y = 0;
                var at = sc + (d.sqrMagnitude > 0.01f ? d.normalized : Vector3.back) * 1.6f;
                at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
                pc.LocalTeleport(at, 0f);
                yield return new WaitForSeconds(0.3f);
                LookAt(pc, me, sc);
                yield return new WaitForSeconds(0.1f);
                yield return TutPress(Bind.Interact, 0.6f);
                if (me.Count(Item.Spear) == 0) { Log("(E didn't pick the spear up - by RPC)"); me.PickupItemRpc(sid); }
            }
            yield return TutWaitStep("bow");
            Check(Tutorial.StepId == "bow" && me.Count(Item.Spear) == 1 && Tutorial.AllowsItem(Item.Bow) && Tutorial.AllowsItem(Item.Arrow) && !Tutorial.Allows(TutFeature.Health)
                && !Tutorial.AllowsItem(Item.Chest) && !Tutorial.AllowsItem(Item.Workbench) && !Tutorial.AllowsItem(Item.Ram),
                $"({who}) picked the spear back up - no stone steps, on to the bow (bow and arrows in the crafting list; no chest, trade station or ram yet) ({Tutorial.StepId})");
            Check(!Cfg.BenchUnlocked(team), $"({who}) our trade station is still locked (the ball hasn't been captured)");
            Check(Tutorial.HideStone && !Tutorial.Allows(TutFeature.Upgrade) && !Binds.Name(Bind.Upgrade).Equals(""),
                $"({who}) no stone in the tutorial: the HUD leaves the stone count and \"F: upgrade to stone\" out, and stone upgrades wait until it's done");

            // ---- the bow: craft it and arrows (before the trade station), then the training dummies ----
            Check(TutDummies() == 0, $"({who}) no training dummies before the shoot step ({TutDummies()})");
            if (me.Count(Item.Wood) < 400) me.DevRpc(DevCmd.GiveWood);
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            me.CraftRpc(Cfg.RecipeIndex(Item.Bow));
            yield return new WaitForSeconds(0.4f);
            Check(me.Count(Item.Bow) == 1 && Tutorial.StepId == "bow", $"({who}) crafted a bow; the step wants arrows too ({Tutorial.StepId})");
            me.CraftRpc(Cfg.RecipeIndex(Item.Arrow));
            yield return new WaitForSeconds(0.4f);
            me.CraftRpc(Cfg.RecipeIndex(Item.Arrow));
            yield return TutWaitStep("shoot");
            Check(Tutorial.StepId == "shoot" && me.Count(Item.Arrow) >= Cfg.ArrowsPerCraft, $"({who}) crafted arrows ({me.Count(Item.Arrow)}): on to shooting ({Tutorial.StepId})");
            until = Time.time + 6f;
            while (TutDummies() < 2 && Time.time < until) yield return null;
            {
                var dummy = Tutorial.NearestDummy();
                Check(TutDummies() == 2 && dummy != null && !dummy.Rideable && Mathf.Approximately(dummy.Hp.Value, Vehicle.DummyHp) && Cfg.BaseTeamAt(dummy.transform.position) < 0
                    && Flat(dummy.transform.position, Tutorial.TrainSpot(team)) < 12f && Cfg.RegionOf(dummy.transform.position) == Cfg.RegionOf(Cfg.BaseCenter[team]),
                    $"({who}) the shoot step puts two training dummies out in front of our base ({TutDummies()}, {(dummy != null ? dummy.transform.position.ToString() : "none")})");
            }
            yield return TutShootDummy(me, pc, team, 5f);
            yield return TutShot("dummies");
            yield return TutWaitStep("melee", 3f);
            if (Tutorial.StepId == "shoot")
            {
                var dd = Tutorial.NearestDummy();
                Log($"(the arrow didn't hit - reporting one by RPC; bow hits {Tutorial.DummyBowHits})");
                if (dd != null)
                {
                    yield return TutSelect(me, Item.Bow);
                    var aim = dd.transform.position + Vector3.up * 1.1f;
                    var dir = (aim - me.EyePos).normalized;
                    me.FireArrowRpc(me.EyePos, dir * Cfg.ArrowSpeed);
                    yield return new WaitForSeconds(0.3f);
                    me.ArrowHitRpc(dd.NetworkObject, aim, dir);
                }
                yield return TutWaitStep("melee", 4f);
            }
            Check(Tutorial.StepId == "melee" && Tutorial.DummyBowHits > 0, $"({who}) an arrow into a dummy ticks the shoot step off ({Tutorial.DummyBowHits} bow hits, {Tutorial.StepId})");

            // ---- combat: destroy a dummy up close (the spear, held LMB), then one from range (arrows) ----
            {
                yield return TutSelect(me, Item.Spear);
                int near0 = Tutorial.DummyKillsNear;
                Vehicle target = null;
                until = Time.time + 16f;
                Binds.TestHold(Bind.Attack, true);
                while (Tutorial.StepId == "melee" && Tutorial.DummyKillsNear == near0 && Time.time < until)
                {
                    if (target == null || !target.IsSpawned || target.Hp.Value <= 0f)
                    {
                        target = Tutorial.NearestDummy();
                        if (target != null)
                        {
                            var at = target.transform.position + Cfg.BackDir(team) * 2.2f;
                            at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
                            pc.LocalTeleport(at, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
                            yield return new WaitForSeconds(0.3f);
                        }
                        else yield return null;
                        continue;
                    }
                    LookAt(pc, me, target.transform.position + Vector3.up * 1.1f);
                    yield return null;
                }
                Binds.TestHold(Bind.Attack, false);
                yield return TutWaitStep("ranged", 4f);
                Check(Tutorial.StepId == "ranged" && Tutorial.DummyKillsNear > near0, $"({who}) poking a dummy to bits with the spear ticks the melee step off ({Tutorial.DummyKillsNear - near0} destroyed up close, {Tutorial.StepId})");
                until = Time.time + 8f;
                while (TutDummies() < 2 && Time.time < until) yield return null;
                Check(TutDummies() == 2, $"({who}) a new dummy pops up for the one destroyed ({TutDummies()})");
                int far0 = Tutorial.DummyKillsFar;
                for (int shot = 0; shot < 5 && Tutorial.StepId == "ranged" && Tutorial.DummyKillsFar == far0; shot++)
                {
                    if (me.Count(Item.Arrow) == 0) { me.DevRpc(DevCmd.GiveArrows); yield return new WaitForSeconds(0.3f); }
                    yield return TutShootDummy(me, pc, team, Vehicle.DummyFar + 1.3f);
                }
                if (Tutorial.DummyKillsFar == far0)
                {
                    var dd = Tutorial.NearestDummy();
                    Log("(the arrows didn't finish a dummy - damaging it on the server, from range)");
                    if (dd != null) dd.ServerDamage(9999f, me);
                }
                yield return TutWaitStep("berry", 4f);
                Check(Tutorial.StepId == "berry" && Tutorial.DummyKillsFar > far0 && Tutorial.Allows(TutFeature.Health) && !Tutorial.AllowsItem(Item.Workbench) && !Tutorial.AllowsItem(Item.Ram),
                    $"({who}) arrows from range finish a dummy: on to food (the health bar shows; no trade station or ram yet) ({Tutorial.DummyKillsFar - far0} destroyed from range, {Tutorial.StepId})");
            }

            // ---- food: pick a bush (E), eat (RMB) ----
            var bush = TutNearest(ResourceNode.Bush, me);
            if (bush != null)
            {
                var d = Cfg.BaseCenter[team] - bush.transform.position;
                d.y = 0;
                var at = bush.transform.position + d.normalized * 1.8f;
                at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
                pc.LocalTeleport(at, 0f);
                yield return new WaitForSeconds(0.3f);
                LookAt(pc, me, bush.transform.position + Vector3.up * 0.5f);
                yield return new WaitForSeconds(0.1f);
                yield return TutPress(Bind.Interact, 0.6f);
                if (me.Count(Item.Berry) == 0) { Log("(E didn't pick the bush - by RPC)"); me.PickBerriesRpc(bush.NetworkObject); }
            }
            else Log("FAIL: no berry bush on my side");
            yield return TutWaitStep("eat");
            Check(Tutorial.StepId == "eat" && me.Count(Item.Berry) > 0, $"({who}) picked berries ({me.Count(Item.Berry)}) ({Tutorial.StepId})");
            yield return new WaitForSeconds(0.6f);
            float hp0 = me.Health.Value;
            Check(hp0 < Cfg.MaxHealth, $"({who}) the eat step makes you hungry ({hp0:0} HP)");
            yield return TutShot("eat");
            yield return TutSelect(me, Item.Berry);
            yield return TutPress(Bind.Aim, Cfg.BerryEatTime + 1.2f);
            yield return TutWaitStep("glass");
            Check(Tutorial.StepId == "glass" && me.Health.Value > hp0, $"({who}) RMB eats the berry ({hp0:0} -> {me.Health.Value:0} HP) ({Tutorial.StepId})");

            // the glass wall: walk up to it; it drops once every player who's on these steps has (a friend who joined is
            // already past them - at the "with a friend" step - and isn't waited for)
            int dropAt = ids.IndexOf("drop");
            pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team));
            yield return TutShot("glass");
            Check(g.S == GameState.PreBall && Tutorial.StepId == "glass", "the glass step waits until you walk up to the wall");
            // the ball waiting under the glass dome, from out on our side (not near enough to the wall to tick the step off)
            {
                var view = Cfg.BackDir(team) * (MapBuilder.DomeRadius + 8f);
                view.y = MapBuilder.Height(view.x, view.z) + 0.1f;
                pc.LocalTeleport(view, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y, 4f);
                yield return TutShot("glass_dome");
                Check(Tutorial.StepId == "glass" && g.S == GameState.PreBall && Ball.Instance != null && !Ball.Instance.IsCarried, "the ball sits under the dome until the wall drops");
            }
            var near = Tutorial.WallSpot(team); // (by the wall, just outside the glass dome)
            near.y = MapBuilder.Height(near.x, near.z) + 0.1f;
            pc.LocalTeleport(near, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(2.5f);
            Check(Tutorial.StepId == "drop" && me.TutAtWall.Value, $"walking up to the wall ticks it off and tells the server ({Tutorial.StepId}, at wall {me.TutAtWall.Value})");
            until = Time.time + 20f;
            while (g.S == GameState.PreBall && Time.time < until) yield return null;
            bool allThere = true;
            foreach (var p in PlayerNet.All) allThere &= p.TutAtWall.Value || p.TutStep.Value > dropAt;
            Check(g.S == GameState.BallLive && g.TimerPaused.Value && allThere, $"the wall dropped once everyone on the wall steps reached it ({PlayerNet.All.Count} player(s) here; clock still stopped)");
            yield return new WaitForSeconds(2f); // (a done step ticks, then moves on a second later)
            Check(!MapBuilder.GlassUp && Tutorial.StepId == "toball", $"({who}) the glass is gone and the guide moved on to the ball ({Tutorial.StepId})");
            yield return new WaitForSeconds(3f);
            yield return TutShot("walldown");

            // the ball: run to it, grab it, take it home (that captures it: our trade station unlocks), guard it
            var ball = Ball.Instance;
            if (ball == null) yield break;
            pc.LocalTeleport(ball.transform.position + new Vector3(1.5f, 0.1f, 0f), 270f);
            yield return new WaitForSeconds(2f);
            Check(Tutorial.StepId == "grab", $"({who}) getting to the ball ticks it off ({Tutorial.StepId})");
            int notices0 = NetGame.BenchUnlockNotices;
            me.PickupBallRpc();
            yield return new WaitForSeconds(2f); // (the grab step ticks, then moves on a second later)
            Check(me.CarryingBall && Tutorial.StepId == "score", $"({who}) picked up the ball ({Tutorial.StepId})");
            yield return TutShot("score");
            pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team) + 180f);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc(me.EyePos, (Cfg.SocketPos(team) - me.EyePos).normalized, Vector3.zero);
            yield return new WaitForSeconds(2.5f);
            Check(ball.SocketTeam.Value == team && Tutorial.StepId == "guard" && Cfg.BenchUnlocked(team),
                $"({who}) the ball is in my machine: that captures it and unlocks our trade station; now guard it ({Tutorial.StepId}, unlocked {Cfg.BenchUnlocked(team)})");
            Check(NetGame.BenchUnlockNotices == notices0, $"({who}) the TRADE STATION UNLOCKED notice waits for the trade station step");
            yield return TutShot("guard");
            yield return TutWaitStep("bench", 9f);

            // ---- the trade station: unlocked by the capture - craft one, put it down, see its list ----
            yield return new WaitForSeconds(0.3f);
            Check(Tutorial.StepId == "bench" && Tutorial.AllowsItem(Item.Workbench) && NetGame.BenchUnlockNotices == notices0 + 1 && Hud.LastBanner.Contains("TRADE STATION UNLOCKED"),
                $"({who}) guarding ticks off; the trade station step says TRADE STATION UNLOCKED ({Tutorial.StepId}, \"{Hud.LastBanner}\")");
            me.CraftRpc(Cfg.CraftIndexOf(Item.Crossbow));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.Crossbow) == 0, $"({who}) no crossbow before there's a trade station");
            if (me.Count(Item.Wood) < Cfg.WorkbenchWood + 50) me.DevRpc(DevCmd.GiveWood);
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            yield return TutPress(Bind.Inventory);
            for (int i = 0; i < 3; i++) yield return null;
            string benchRow = RowState(Item.Workbench);
            yield return TutShot("bench");
            Check(benchRow == "ok", $"({who}) in the bag the Trade Station has a green CRAFT ({benchRow})");
            me.CraftRpc(Cfg.RecipeIndex(Item.Workbench));
            yield return TutWaitStep("placebench");
            Check(Tutorial.StepId == "placebench" && me.Count(Item.Workbench) == 1 && Tutorial.Allows(TutFeature.Deploy), $"({who}) crafted a Trade Station ({Tutorial.StepId})");
            yield return TutPress(Bind.Inventory); // (close the bag)
            yield return TutSelect(me, Item.Workbench);
            yield return TutShot("placebench");
            var grassSpot = Cfg.BaseCenter[team] - Cfg.BackDir(team) * 7f + Vector3.Cross(Vector3.up, Cfg.BackDir(team)) * (host ? 3f : -3f);
            grassSpot.y = MapBuilder.Height(grassSpot.x, grassSpot.z);
            var nearGrass = grassSpot + Cfg.BackDir(team) * 2.5f;
            nearGrass.y = MapBuilder.Height(nearGrass.x, nearGrass.z) + 0.1f;
            pc.LocalTeleport(nearGrass, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            me.PlaceDeployableRpc((byte)Item.Workbench, grassSpot, Workbench.DefaultYaw(team));
            yield return TutWaitStep("newcrafts");
            var bench = Workbench.ForTeam(team, 1);
            Check(Tutorial.StepId == "newcrafts" && bench != null, $"({who}) put the Trade Station down on the grass in the base ({Tutorial.StepId})");
            Check(Tutorial.AllowsItem(Item.Crossbow) && Tutorial.AllowsItem(Item.Workbench2) && Cfg.CraftTierAt(team, me.transform.position) == 1, $"({who}) the trade station's items are unlocked");
            yield return TutPress(Bind.Inventory);
            yield return new WaitForSeconds(0.4f);
            yield return TutShot("bag_t1");
            yield return TutWaitStep("chest", 4f);
            Check(Tutorial.StepId == "chest" && !Tutorial.Finished && Tutorial.AllowsItem(Item.Chest) && !Tutorial.AllowsItem(Item.Ram) && !Tutorial.Allows(TutFeature.Station),
                $"({who}) opening the bag in the base shows the trade station's list; on to the chest (in the crafting list now; no ram, no upgrade station yet) ({Tutorial.StepId})");
            yield return TutPress(Bind.Inventory); // (close the bag)

            // the upgrade station is still locked: standing right at it with the wood, the server refuses
            var stationAt = Cfg.UpgradeStationPos(team) - Cfg.BackDir(team) * 1.7f;
            stationAt.y = Cfg.SpawnPos(team).y;
            var stationAim = Cfg.UpgradeStationPos(team) + Vector3.up * 1.1f;
            me.DevRpc(DevCmd.GiveWood);
            pc.LocalTeleport(stationAt, Quaternion.LookRotation(Cfg.BackDir(team)).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, stationAim);
            yield return new WaitForSeconds(0.1f);
            yield return TutPress(Bind.Interact, 0.5f);
            me.BaseUpgradeRpc(Item.WoodGenBuff);
            yield return new WaitForSeconds(0.6f);
            Check(!pc.UpgradesOpen && Cfg.WoodGenLevel(team) == 0 && Cfg.AtOwnStation(team, me.transform.position) && me.Count(Item.Wood) >= Cfg.WoodGenWood(1),
                $"({who}) the upgrade station doesn't open and the server refuses an upgrade before its step (screen {pc.UpgradesOpen}, level {Cfg.WoodGenLevel(team)})");
            pc.CloseMenu();

            // ---- the chest: craft it, put it down, put something in it ----
            pc.LocalTeleport(nearGrass, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            me.CraftRpc(Cfg.RecipeIndex(Item.Chest));
            yield return TutWaitStep("placechest");
            Check(Tutorial.StepId == "placechest" && me.Count(Item.Chest) == 1, $"({who}) crafted a storage chest ({Tutorial.StepId})");
            yield return TutSelect(me, Item.Chest);
            var chestSpot = grassSpot + Vector3.Cross(Vector3.up, Cfg.BackDir(team)) * 3.2f; // (clear of the trade station beside it)
            chestSpot.y = MapBuilder.Height(chestSpot.x, chestSpot.z);
            me.PlaceDeployableRpc((byte)Item.Chest, chestSpot, Workbench.DefaultYaw(team));
            yield return TutWaitStep("usechest");
            var myChest = Tutorial.MyChest;
            Check(Tutorial.StepId == "usechest" && myChest != null && myChest.Empty, $"({who}) put the chest down in the base ({Tutorial.StepId})");
            if (myChest != null)
            {
                var from = myChest.Center + Cfg.BackDir(team) * 1.8f;
                from.y = MapBuilder.Height(from.x, from.z) + 0.1f;
                pc.LocalTeleport(from, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                LookAt(pc, me, myChest.Center);
                yield return new WaitForSeconds(0.1f);
                yield return TutPress(Bind.Interact, 0.6f); // (a tap: it opens - holding E would pick the empty chest back up)
                bool opened = pc.LootTarget == myChest && pc.MenuOpen;
                if (!opened) { Log("(E didn't open the chest - opening it directly)"); pc.LootTarget = myChest; pc.MenuOpen = true; yield return null; }
                yield return TutShot("chest_open");
                int woodSlot = -1;
                for (int i = 0; i < Cfg.PlayerSlots && woodSlot < 0; i++) if (me.SlotAt(i).Id == Item.Wood) woodSlot = i;
                if (woodSlot >= 0) me.MoveItemRpc(0, (byte)woodSlot, 1, 255, me.SlotAt(woodSlot).Count, myChest.NetworkObject); // (a shift-click)
                yield return TutWaitStep("airdrop", 5f);
                Check(Tutorial.StepId == "airdrop" && !myChest.Empty, $"({who}) a tap of E opens the chest (by the key: {opened}); shift-clicking wood into it ticks the step off ({Tutorial.StepId})");
                pc.CloseMenu();
            }

            // ---- the airdrop: a real one, on our lane, out in front of the base - meet it, empty it ----
            until = Time.time + 6f;
            while (g.LaneStartAt(team) < 0 && Time.time < until) yield return null;
            {
                var dropAt2 = g.LanePosAt(team);
                Check(g.LaneStartAt(team) >= 0 && Cfg.BaseTeamAt(dropAt2) < 0 && Cfg.RegionOf(dropAt2) == Cfg.RegionOf(Cfg.BaseCenter[team]) && Flat(dropAt2, Cfg.BaseCenter[team]) < Cfg.BaseHalf + 30f && Tutorial.MyDrop == null,
                    $"({who}) the airdrop step sends a real airdrop to our side, out in front of the base ({dropAt2}); its ship is on the way");
                var watch = dropAt2 + Cfg.BackDir(team) * 7f;
                watch.y = MapBuilder.Height(watch.x, watch.z) + 0.1f;
                pc.LocalTeleport(watch, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y, -35f);
                yield return new WaitForSeconds(NetGame.DropArrive * 0.6f);
                yield return TutShot("airdrop_ship");
                Check(Tutorial.StepId == "airdrop", $"({who}) the airdrop step waits for the crate ({Tutorial.StepId})");
                until = Time.time + NetGame.DropLand + 8f;
                while (Tutorial.MyDrop == null && Time.time < until) yield return null;
                var crate = Tutorial.MyDrop;
                Check(crate != null && crate.IsAirdrop && !crate.Empty, $"({who}) the crate landed with something in it ({(crate != null ? Cfg.ItemName(crate.Slots[0].Id) : "no crate")})");
                if (crate != null)
                {
                    var by = crate.transform.position + Cfg.BackDir(team) * 2.2f;
                    by.y = MapBuilder.Height(by.x, by.z) + 0.1f;
                    pc.LocalTeleport(by, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
                    yield return new WaitForSeconds(0.3f);
                    LookAt(pc, me, crate.Center);
                    yield return TutWaitStep("loot", 4f);
                    Check(Tutorial.StepId == "loot", $"({who}) getting to the crate ticks the airdrop step off ({Tutorial.StepId})");
                    yield return TutPress(Bind.Interact, 0.6f);
                    bool opened = pc.LootTarget == crate && pc.MenuOpen;
                    if (!opened) { Log("(E didn't open the crate - opening it directly)"); pc.LootTarget = crate; pc.MenuOpen = true; yield return null; }
                    yield return TutShot("airdrop_crate");
                    var loot = crate.Slots[0];
                    me.MoveItemRpc(1, 0, 0, 255, loot.Count, crate.NetworkObject);
                    yield return TutWaitStep("raid", 5f);
                    pc.CloseMenu();
                    Check(Tutorial.StepId == "raid" && me.Count(loot.Id) >= 1 && Tutorial.MyDrop == null, $"({who}) took the {Cfg.ItemName(loot.Id)} out of the crate (opened by the key: {opened}): the crate goes, on to the raid ({Tutorial.StepId})");
                }
            }

            // ---- the raid: an enemy hut on our side - its padlocked door has its own, weaker health; then its chest ----
            until = Time.time + 8f;
            while ((Tutorial.HutDoor == null || Tutorial.HutChest == null) && Time.time < until) yield return null;
            {
                var door = Tutorial.HutDoor;
                var theirs = Tutorial.HutChest;
                Check(door != null && theirs != null && door.Team.Value != team && theirs.Team.Value != team && door.HasDoor && Mathf.Approximately(door.DoorHealth.Value, Cfg.DoorLeafHp(0))
                    && door.DoorHealth.Value < door.Health.Value && Cfg.BaseTeamAt(door.transform.position) < 0 && !theirs.Empty,
                    $"({who}) the raid step builds an enemy hut on our side: a padlocked door (its own {(door != null ? door.DoorHealth.Value : 0f):0} HP, the frame {(door != null ? door.Health.Value : 0f):0}) and a chest of loot inside");
                Check(me.Count(Item.Ram) == 1 && Tutorial.AllowsItem(Item.Ram), $"({who}) a battering ram was handed over (and can be crafted now) ({me.Count(Item.Ram)})");
                if (door != null && theirs != null)
                {
                    var front = door.transform.position + Cfg.BackDir(team) * 2f;
                    front.y = MapBuilder.Height(front.x, front.z) + 0.1f;
                    var doorAim = door.transform.position + Vector3.up * 1.2f;
                    pc.LocalTeleport(front, Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y);
                    yield return new WaitForSeconds(0.4f);
                    LookAt(pc, me, doorAim);
                    yield return TutShot("raid_hut");
                    yield return TutPress(Bind.Interact, 0.6f);
                    Check(!door.DoorOpen.Value && Tutorial.StepId == "raid", $"({who}) E doesn't open the enemy's padlocked door");
                    yield return TutSelect(me, Item.Hatchet);
                    Binds.TestHold(Bind.Attack, true);
                    until = Time.time + 16f;
                    while (door != null && door.IsSpawned && door.HasDoor && Time.time < until) { LookAt(pc, me, doorAim); yield return null; }
                    Binds.TestHold(Bind.Attack, false);
                    bool byHitting = door != null && door.IsSpawned && !door.HasDoor;
                    if (door != null && door.IsSpawned && door.HasDoor) { Log("(hitting it didn't break the door - breaking it on the server)"); door.ServerDamageDoor(9999f); }
                    yield return TutWaitStep("raidloot", 4f);
                    Check(Tutorial.StepId == "raidloot" && door != null && door.IsSpawned && !door.HasDoor && door.Health.Value > 0f,
                        $"({who}) the hatchet breaks the door off (by hitting it: {byHitting}) - the frame still stands - and that ticks the raid step off ({Tutorial.StepId})");
                    yield return TutShot("raid_open");
                    for (int i = 0; i < theirs.Slots.Count; i++)
                        if (!theirs.Slots[i].Empty) me.MoveItemRpc(1, (byte)i, 0, 255, theirs.Slots[i].Count, theirs.NetworkObject);
                    yield return TutWaitStep("upgrade", 5f);
                    Check(Tutorial.StepId == "upgrade" && theirs.Empty && me.Count(Item.Arrow) >= 10, $"({who}) emptied the enemy's chest: on to the upgrade station ({Tutorial.StepId}, {me.Count(Item.Arrow)} arrows)");
                }
            }

            // ---- the upgrade station: given the wood, E opens UPGRADES, the wood gen builds the wood machine ----
            yield return new WaitForSeconds(0.6f);
            Check(Tutorial.StepId == "upgrade" && Tutorial.Allows(TutFeature.Station) && me.Count(Item.Wood) >= Cfg.WoodGenWood(1) && Cfg.WoodGenLevel(team) == 0,
                $"({who}) the upgrade step unlocks the station and tops the wood up to the wood machine's price ({me.Count(Item.Wood)} / {Cfg.WoodGenWood(1)})");
            pc.LocalTeleport(stationAt, Quaternion.LookRotation(Cfg.BackDir(team)).eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            LookAt(pc, me, stationAim);
            yield return new WaitForSeconds(0.1f);
            yield return TutPress(Bind.Interact, 0.5f);
            {
                bool opened = pc.UpgradesOpen;
                if (!opened) { Log("(E didn't open the upgrade station - opening it directly)"); pc.OpenUpgrades(); yield return null; }
                yield return TutShot("upgrades");
                me.BaseUpgradeRpc(Item.WoodGenBuff);
                string next = Bootstrap.Solo ? "win" : "friend";
                yield return TutWaitStep(next, 5f);
                pc.CloseMenu();
                Check(Cfg.WoodGenLevel(team) == 1 && Tutorial.StepId == next, $"({who}) bought the wood gen (E opened UPGRADES: {opened}): our wood machine is built, and the guide moves on to {next} ({Tutorial.StepId})");
            }

            if (Bootstrap.Solo)
            {
                // ---- solo: the friend steps are skipped; the finale - the clock runs out with the ball in our machine ----
                yield return new WaitForSeconds(1f);
                Check(Tutorial.FinaleRunning && !g.TimerPaused.Value && g.S == GameState.BallLive && g.TimeLeft <= Tutorial.SoloFinaleSeconds + 0.5f && ball.SocketTeam.Value == team,
                    $"({who}) the finale: the clock is running ({g.TimeLeft:0.0} s left) with the ball in our machine");
                pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team));
                yield return TutShot("finale_clock");
                until = Time.time + Tutorial.SoloFinaleSeconds + 8f;
                while (g.S != GameState.GameOver && Time.time < until) yield return null;
                Check(g.S == GameState.GameOver && g.Winner.Value == team && g.CutsceneAt.Value >= 0 && VictoryCutscene.Active,
                    $"({who}) the clock ran out with the ball in our machine: we win, and the real victory cutscene plays (winner {g.Winner.Value}, \"{g.EndReason.Value}\")");
                yield return new WaitForSeconds(2.5f);
                Check(Tutorial.Finished && me.TutStep.Value == Tutorial.FinishedStep && Tutorial.Allows(TutFeature.Upgrade) && Tutorial.AllowsItem(Item.Ram),
                    $"({who}) winning finishes the tutorial; everything is unlocked");
                yield return TutShot("finale_cutscene");
                until = Time.time + VictoryCutscene.Length + 4f;
                while (VictoryCutscene.Active && Time.time < until) yield return null;
                yield return new WaitForSeconds(1f);
                Check(!VictoryCutscene.Active && Hud.GameOverShownAt > 0f && Time.time - Hud.GameOverShownAt < 2f, $"({who}) then the victory screen comes up (with the tutorial's last card and Leave game)");
                yield return TutShot("done");
                yield break;
            }

            // ---- with a friend: the host stops here until the client is in; then the duel and a short match for the ball ----
            Check(Tutorial.StepId == "friend" && Tutorial.Friend && !Tutorial.FinaleRunning, $"({who}) with a friend: the guide stops at the friend step until somebody has joined ({Tutorial.StepId})");
            yield return TutShot("friend_waiting");
            until = Time.time + 300f;
            while (PlayerNet.All.Count < 2 && Time.time < until) yield return null;
            Check(PlayerNet.All.Count >= 2, $"a client joined the tutorial ({PlayerNet.All.Count} players)");
            yield return TutWaitStep("duel", 30f);
            PlayerNet friend = null;
            foreach (var p in PlayerNet.All) if (p != null && p != me) friend = p;
            Check(Tutorial.StepId == "duel" && friend != null && friend.TutStep.Value >= ids.IndexOf("friend") && friend.Team.Value != team,
                $"({who}) once the friend is at the same step, both move on to the duel ({Tutorial.StepId})");
            if (friend == null) yield break;
            {
                yield return TutSelect(me, Item.Spear);
                int deaths0 = friend.Deaths.Value;
                float nextHop = 0f;
                Binds.TestHold(Bind.Attack, true);
                until = Time.time + 30f;
                while (Tutorial.StepId == "duel" && friend != null && friend.Deaths.Value == deaths0 && Time.time < until)
                {
                    if (Time.time >= nextHop)
                    {
                        nextHop = Time.time + 2f;
                        var toMe = Cfg.BaseCenter[team] - friend.transform.position;
                        toMe.y = 0;
                        var at = friend.transform.position + toMe.normalized * 2.2f;
                        pc.LocalTeleport(at + Vector3.up * 0.1f, Quaternion.LookRotation(-toMe).eulerAngles.y);
                    }
                    LookAt(pc, me, friend.transform.position + Vector3.up * 1.1f);
                    yield return null;
                }
                Binds.TestHold(Bind.Attack, false);
                if (friend != null && friend.Deaths.Value == deaths0) { Log("(the spear didn't knock the friend out - by the server)"); friend.ServerKill(me); }
                yield return TutWaitStep("win", 6f);
                Check(Tutorial.StepId == "win" && friend != null && friend.Deaths.Value > deaths0, $"({who}) knocking the friend out ticks the duel off for both ({Tutorial.StepId})");
            }
            // the match: the ball drops into the middle, the clock runs; we take it home and win when it runs out
            yield return new WaitForSeconds(1.5f);
            Check(Tutorial.FinaleRunning && !g.TimerPaused.Value && g.S == GameState.BallLive && g.TimeLeft > Tutorial.SoloFinaleSeconds + 5f && ball.SocketTeam.Value < 0,
                $"({who}) with a friend the finale is a real match for the ball ({g.TimeLeft:0} s on the clock, the ball back in play)");
            until = Time.time + 20f;
            while (ball.transform.position.y > 6f && Time.time < until) yield return null;
            yield return new WaitForSeconds(1f);
            pc.LocalTeleport(ball.transform.position + new Vector3(1.5f, 0.1f, 0f), 270f);
            yield return new WaitForSeconds(0.6f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(1f);
            pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team) + 180f);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc(me.EyePos, (Cfg.SocketPos(team) - me.EyePos).normalized, Vector3.zero);
            yield return new WaitForSeconds(2.5f);
            Check(ball.SocketTeam.Value == team, $"({who}) the ball is in our machine ({ball.SocketTeam.Value})");
            g.DevSetTimeLeft(8f); // (not the whole two minutes)
            until = Time.time + 14f;
            while (g.S != GameState.GameOver && Time.time < until) yield return null;
            Check(g.S == GameState.GameOver && g.Winner.Value == team && g.CutsceneAt.Value >= 0 && VictoryCutscene.Active, $"({who}) the clock ran out with the ball in our machine: we win the match, with the victory cutscene (winner {g.Winner.Value})");
            yield return new WaitForSeconds(2.5f);
            Check(Tutorial.Finished, $"({who}) and that finishes the tutorial");
            until = Time.time + VictoryCutscene.Length + 4f;
            while (VictoryCutscene.Active && Time.time < until) yield return null;
            yield return new WaitForSeconds(1f);
            yield return TutShot("done");
        }

        /// <summary>
        /// Whoever joins a friend's tutorial (-autotest modes -client ... -rules tutorial): no early steps - the guide
        /// starts at the "with a friend" step with everything unlocked and a starter kit, waits for the host to get there,
        /// then the duel (the host's test comes for us) and the short match for the ball (the host's test wins it).
        /// </summary>
        IEnumerator TutorialJoinerTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            string who = m_TutWho;
            var ids = Tutorial.StepIds();
            Check(Tutorial.Friend && Tutorial.StepId == "friend" && me.TutStep.Value == ids.IndexOf("friend"), $"({who}) joining a tutorial starts the guide at the with-a-friend step ({Tutorial.StepId}, synced step {me.TutStep.Value})");
            Check(Tutorial.Allows(TutFeature.Move) && Tutorial.Allows(TutFeature.Slide) && Tutorial.Allows(TutFeature.Inventory) && Tutorial.Allows(TutFeature.Craft) && Tutorial.Allows(TutFeature.Build)
                && Tutorial.Allows(TutFeature.Deploy) && Tutorial.Allows(TutFeature.Eat) && Tutorial.Allows(TutFeature.Station) && Tutorial.AllowsItem(Item.Bow) && Tutorial.AllowsItem(Item.Crossbow),
                $"({who}) everything the earlier steps teach is unlocked for the one who joined");
            yield return new WaitForSeconds(1.5f);
            Check(me.Count(Item.Hatchet) == 1 && me.Count(Item.Spear) == 1 && me.Count(Item.Bow) == 1 && me.Count(Item.Arrow) >= 20 && me.Count(Item.BuildingPlan) == 1 && me.Count(Item.Wood) >= 400,
                $"({who}) and they're handed a starter kit (hatchet {me.Count(Item.Hatchet)}, spear {me.Count(Item.Spear)}, bow {me.Count(Item.Bow)}, arrows {me.Count(Item.Arrow)}, plan {me.Count(Item.BuildingPlan)}, wood {me.Count(Item.Wood)})");
            yield return TutShot("friend_joined");
            // really walk a bit: nothing is locked
            var p0 = me.transform.position;
            Binds.TestHold(Bind.Forward, true);
            yield return new WaitForSeconds(1f);
            Binds.TestReleaseAll();
            Check(Flat(me.transform.position, p0) > 1.5f, $"({who}) W walks straight away ({Flat(me.transform.position, p0):0.0} m)");
            // the host plays its own steps first: wait for it at the gate
            float until = Time.time + 1200f;
            while (Tutorial.StepId == "friend" && Time.time < until) yield return null;
            Check(Tutorial.StepId == "duel", $"({who}) when the host gets to the friend step, both move on to the duel ({Tutorial.StepId})");
            until = Time.time + 120f;
            while (Tutorial.StepId == "duel" && Time.time < until) yield return null;
            Check(Tutorial.StepId == "win", $"({who}) a knock-out (ours: {me.Deaths.Value} deaths) ticks the duel off here too ({Tutorial.StepId})");
            until = Time.time + Tutorial.FriendMatchSeconds + 60f;
            while (g != null && g.S != GameState.GameOver && Time.time < until)
            {
                if (me.ChoosingRespawn) pc.ChooseRespawn(false);
                yield return null;
            }
            Check(g != null && g.S == GameState.GameOver && g.CutsceneAt.Value >= 0 && g.Winner.Value >= 0,
                $"({who}) the match for the ball ended with the victory cutscene (winner {(g != null ? g.Winner.Value : -1)}, we're {team})");
            yield return new WaitForSeconds(2.5f);
            Check(Tutorial.Finished, $"({who}) and that finishes the tutorial here as well");
            yield return new WaitForSeconds(VictoryCutscene.Length);
            yield return TutShot("done");
        }
    }
}
