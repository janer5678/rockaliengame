using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The tutorial test (-autotest modes -rules tutorial, on a host with -solo, or a host plus a client that joins it):
    /// plays the whole tutorial step by step, mostly with pretend key presses (Binds.TestHold / TestPress) that go
    /// through the same gates as real keys, and checks that every control is locked until the step that teaches it.
    /// No stone anywhere; the ball is captured before the workbench (that's what unlocks it). With two players the host
    /// captures the ball first, then the client steals it out of the host's machine (E) and captures it for its own team.
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

        /// <summary>Tutorial: the map is forced to small Plains, the clock is stopped, there are no airdrops, every control
        /// is locked until its step, steps only move on when they're done (no Enter), the wall drops once every player
        /// has walked up to it, then the ball steps. Works on the host and on a client that joins mid-tutorial
        /// (-autotest modes -client ...): both run this.</summary>
        IEnumerator TutorialTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            bool host = NetworkManager.Singleton.IsServer;
            string who = m_TutWho = host ? "host" : "client";
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(1f);
            Check(Cfg.Map == MapKind.Plains && Cfg.Size == MapSize.Small && !Cfg.WoodMode && g.MapKey.Value == Cfg.MapKey,
                $"Tutorial ({who}): the map is forced to small Plains, normal materials ({Cfg.MapLabel})");
            Check(Cfg.Tutorial && !Cfg.LimitedCrafting && Cfg.RecipeIndex(Item.Workbench) >= 0 && g.S == GameState.PreBall && g.TimerPaused.Value,
                $"Tutorial ({who}): playing the classic crafting (starter items + workbenches), the clock stopped");
            // a player who joined mid-tutorial gets dropped straight into their base
            float until = Time.time + 10f;
            while (Cfg.BaseTeamAt(me.transform.position) != team && Time.time < until) yield return null;
            Check(Cfg.BaseTeamAt(me.transform.position) == team, $"Tutorial ({who}): in my own base, not the waiting stadium ({me.transform.position})");
            float t0 = g.TimeLeft;
            yield return new WaitForSeconds(2f);
            Check(Mathf.Abs(g.TimeLeft - t0) < 0.5f, $"the clock doesn't move ({t0:0.0} -> {g.TimeLeft:0.0})");
            Check(g.LaneStartAt(0) < 0 && g.NextDropLands.Value < 0, "no airdrops in the tutorial");
            Check(Ball.Instance != null && MapBuilder.GlassUp && new Vector2(Ball.Instance.transform.position.x, Ball.Instance.transform.position.z).magnitude < 0.5f && !Ball.Instance.IsCarried,
                "the ball is already in the middle, under the glass dome");
            var ids = Tutorial.StepIds();
            Log($"tutorial steps ({ids.Count}): {string.Join(" ", ids)}");
            Check(Tutorial.StepCount >= 30 && Tutorial.StepId == "machine" && !Tutorial.HasEnterKey, $"the guide is on step 1 of {Tutorial.StepCount} ({Tutorial.StepId}), no Enter to skip");
            // everything unlocks in the order it's taught
            Check(Tutorial.UnlockStep(TutFeature.Move) < Tutorial.UnlockStep(TutFeature.Sprint) && Tutorial.UnlockStep(TutFeature.Sprint) < Tutorial.UnlockStep(TutFeature.Crouch)
                && Tutorial.UnlockStep(TutFeature.Crouch) < Tutorial.UnlockStep(TutFeature.Slide) && Tutorial.UnlockStep(TutFeature.Slide) < Tutorial.UnlockStep(TutFeature.Hit)
                && Tutorial.UnlockStep(TutFeature.Hit) < Tutorial.UnlockStep(TutFeature.Inventory) && Tutorial.UnlockStep(TutFeature.Inventory) < Tutorial.UnlockStep(TutFeature.Craft)
                && Tutorial.UnlockStep(TutFeature.Craft) < Tutorial.UnlockStep(TutFeature.Build) && Tutorial.UnlockStep(TutFeature.Build) < Tutorial.UnlockStep(TutFeature.Demolish)
                && Tutorial.UnlockStep(TutFeature.Demolish) < ids.IndexOf("glass") && ids.IndexOf("glass") < ids.IndexOf("score") && ids.IndexOf("score") < ids.IndexOf("bench")
                && ids.IndexOf("bench") < Tutorial.UnlockStep(TutFeature.Workbench),
                "the steps go look > move > run/jump > crouch > slide > hit > bag > craft > build > break your own > the wall > capture the ball > workbench");
            Check(!ids.Contains("stone") && !ids.Contains("mine") && !ids.Contains("pickaxe") && !ids.Contains("ram"), $"no stone steps in the tutorial ({string.Join(" ", ids)})");
            {
                // nothing the tutorial teaches you to craft needs stone
                bool noStone = true;
                foreach (var it in new[] { Item.Hatchet, Item.BuildingPlan, Item.Spear, Item.Workbench })
                    noStone &= Cfg.RecipeIndex(it) >= 0 && Cfg.GetRecipe(Cfg.RecipeIndex(it)).Stone == 0;
                Check(noStone, "nothing the tutorial has you craft needs stone (hatchet, building plan, spear, workbench)");
            }

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
            Check(!pc.MenuOpen, $"({who}) TAB doesn't open the bag before the bag step");
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

            // ---- more wood; TAB is still locked ----
            yield return TutPress(Bind.Inventory);
            Check(!pc.MenuOpen, $"({who}) TAB still doesn't open the bag on the wood step");
            me.DevRpc(DevCmd.GiveWood); // (the rest of the wood the tutorial needs)
            yield return TutWaitStep("bag");
            Check(Tutorial.StepId == "bag" && Tutorial.Allows(TutFeature.Inventory) && !Tutorial.Allows(TutFeature.Craft), $"({who}) 100 wood ticks off the wood step; the bag unlocks, crafting doesn't yet ({Tutorial.StepId})");

            // ---- the bag: crafting is refused (by the server too) before the crafting step ----
            int w0 = me.Count(Item.Wood);
            me.CraftRpc(Cfg.RecipeIndex(Item.Hatchet));
            yield return new WaitForSeconds(0.8f);
            Check(me.Count(Item.Hatchet) == 0 && me.Count(Item.Wood) == w0, $"({who}) crafting is refused before the crafting step");
            yield return TutPress(Bind.Inventory);
            Check(pc.MenuOpen, $"({who}) TAB opens the bag on the bag step");
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
            yield return TutPress(Bind.Hotbar7);
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
            yield return TutWaitStep("berry");
            Check(Tutorial.StepId == "berry" && me.Count(Item.Spear) == 1 && Tutorial.Allows(TutFeature.Health) && !Tutorial.AllowsItem(Item.Workbench) && !Tutorial.AllowsItem(Item.Ram),
                $"({who}) picked the spear back up - no stone steps, straight on to food (the health bar shows; no workbench or ram yet) ({Tutorial.StepId})");
            Check(!Cfg.BenchUnlocked(team), $"({who}) our workbench is still locked (the ball hasn't been captured)");
            Check(Tutorial.HideStone && !Tutorial.Allows(TutFeature.Upgrade) && !Binds.Name(Bind.Upgrade).Equals(""),
                $"({who}) no stone in the tutorial: the HUD leaves the stone count and \"F: upgrade to stone\" out, and stone upgrades wait until it's done");

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

            // the glass wall: walk up to it; it only drops once every player has
            if (host && !Bootstrap.Solo)
            {
                until = Time.time + 150f;
                while (PlayerNet.All.Count < 2 && Time.time < until) yield return null;
                Check(PlayerNet.All.Count >= 2, $"a client joined the tutorial ({PlayerNet.All.Count} players)");
            }
            // wait for the others to get this far too (their step is synced: PlayerNet.TutStep), so the wall waits for them
            int glassAt = ids.IndexOf("glass");
            until = Time.time + 400f;
            while (Time.time < until)
            {
                bool all = true;
                foreach (var p in PlayerNet.All) if (p != null && p.TutStep.Value < glassAt) all = false;
                if (all) break;
                yield return null;
            }
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
            int notThere = 0;
            foreach (var p in PlayerNet.All) if (!p.TutAtWall.Value) notThere++;
            if (notThere > 0)
            {
                Check(g.S == GameState.PreBall, $"the wall waits for the {notThere} player(s) not at it yet");
                yield return TutShot("waiting");
            }
            until = Time.time + 150f;
            while (g.S == GameState.PreBall && Time.time < until) yield return null;
            bool allThere = true;
            foreach (var p in PlayerNet.All) allThere &= p.TutAtWall.Value;
            Check(g.S == GameState.BallLive && g.TimerPaused.Value && allThere, $"the wall dropped once all {PlayerNet.All.Count} player(s) reached it (clock still stopped)");
            yield return new WaitForSeconds(2f); // (a done step ticks, then moves on a second later)
            Check(!MapBuilder.GlassUp && Tutorial.StepId == "toball", $"({who}) the glass is gone and the guide moved on to the ball ({Tutorial.StepId})");
            yield return new WaitForSeconds(3f);
            yield return TutShot("walldown");

            // the ball: run to it, grab it, take it home (that captures it: our workbench unlocks), guard it
            var ball = Ball.Instance;
            if (ball == null) yield break;
            pc.LocalTeleport(ball.transform.position + new Vector3(1.5f, 0.1f, host ? 0f : 3f), 270f);
            yield return new WaitForSeconds(2f);
            Check(Tutorial.StepId == "grab", $"({who}) getting to the ball ticks it off ({Tutorial.StepId})");
            int notices0 = NetGame.BenchUnlockNotices;
            if (!host)
            {
                // a second player: the host takes the ball home first; once it's done, we steal it out of its machine (E)
                until = Time.time + 300f;
                while (Time.time < until)
                {
                    bool hostDone = false;
                    foreach (var p in PlayerNet.All) if (p != null && p != me && p.TutStep.Value == Tutorial.FinishedStep) hostDone = true;
                    if (hostDone && ball.SocketTeam.Value >= 0 && !ball.IsCarried) break;
                    yield return null;
                }
                int their = ball.SocketTeam.Value;
                Check(their >= 0 && their != team, $"({who}) the host captured the ball first (it's in team {their}'s machine)");
                if (their >= 0)
                {
                    var sp = Cfg.SocketPos(their);
                    var toSpawn = Cfg.SpawnPos(their) - sp;
                    toSpawn.y = 0;
                    var steal = sp + toSpawn.normalized * 1.5f;
                    steal.y = Cfg.SpawnPos(their).y;
                    pc.LocalTeleport(steal, Quaternion.LookRotation(-toSpawn).eulerAngles.y);
                    yield return new WaitForSeconds(0.6f);
                    me.PickupBallRpc();
                    yield return new WaitForSeconds(1f);
                }
            }
            else
            {
                me.PickupBallRpc();
                yield return new WaitForSeconds(2f);
            }
            Check(me.CarryingBall && Tutorial.StepId == "score", $"({who}) picked up the ball ({Tutorial.StepId})");
            yield return TutShot("score");
            pc.LocalTeleport(Cfg.SpawnPos(team, me.Slot.Value), Cfg.SpawnYaw(team) + 180f);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc(me.EyePos, (Cfg.SocketPos(team) - me.EyePos).normalized, Vector3.zero);
            yield return new WaitForSeconds(2.5f);
            Check(ball.SocketTeam.Value == team && Tutorial.StepId == "guard" && Cfg.BenchUnlocked(team),
                $"({who}) the ball is in my machine: that captures it and unlocks our workbench; now guard it ({Tutorial.StepId}, unlocked {Cfg.BenchUnlocked(team)})");
            Check(NetGame.BenchUnlockNotices == notices0, $"({who}) the WORKBENCH UNLOCKED notice waits for the workbench step");
            yield return TutShot("guard");
            yield return TutWaitStep("bench", 9f);

            // ---- the workbench: unlocked by the capture - craft a T1, put it down, see its list ----
            yield return new WaitForSeconds(0.3f);
            Check(Tutorial.StepId == "bench" && Tutorial.AllowsItem(Item.Workbench) && NetGame.BenchUnlockNotices == notices0 + 1 && Hud.LastBanner.Contains("WORKBENCH UNLOCKED"),
                $"({who}) guarding ticks off; the workbench step says WORKBENCH UNLOCKED ({Tutorial.StepId}, \"{Hud.LastBanner}\")");
            me.CraftRpc(Cfg.CraftIndexOf(Item.Crossbow));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.Crossbow) == 0, $"({who}) no crossbow before there's a workbench");
            if (me.Count(Item.Wood) < Cfg.WorkbenchWood + 50) me.DevRpc(DevCmd.GiveWood);
            pc.LocalTeleport(spawn, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            yield return TutPress(Bind.Inventory);
            for (int i = 0; i < 3; i++) yield return null;
            string benchRow = RowState(Item.Workbench);
            yield return TutShot("bench");
            Check(benchRow == "ok", $"({who}) in the bag the Workbench T1 has a green CRAFT ({benchRow})");
            me.CraftRpc(Cfg.RecipeIndex(Item.Workbench));
            yield return TutWaitStep("placebench");
            Check(Tutorial.StepId == "placebench" && me.Count(Item.Workbench) == 1 && Tutorial.Allows(TutFeature.Deploy), $"({who}) crafted a Workbench T1 ({Tutorial.StepId})");
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
            Check(Tutorial.StepId == "newcrafts" && bench != null, $"({who}) put the Workbench T1 down on the grass in the base ({Tutorial.StepId})");
            Check(Tutorial.AllowsItem(Item.Crossbow) && Tutorial.AllowsItem(Item.Workbench2) && Cfg.CraftTierAt(team, me.transform.position) == 1, $"({who}) the T1 items are unlocked");
            yield return TutPress(Bind.Inventory);
            yield return new WaitForSeconds(0.4f);
            yield return TutShot("bag_t1");
            yield return new WaitForSeconds(1.5f);
            Check(Tutorial.Finished && Tutorial.Allows(TutFeature.Move) && Tutorial.AllowsItem(Item.Bow) && Tutorial.AllowsItem(Item.Chest) && Tutorial.AllowsItem(Item.Ram) && me.TutStep.Value == Tutorial.FinishedStep,
                $"({who}) opening the bag in the base shows the T1 list and finishes the tutorial; everything is unlocked (the bow, chest and ram too)");
            yield return TutPress(Bind.Inventory);
            yield return TutShot("done");
            if (host && !Bootstrap.Solo)
            {
                // let the other player steal the ball and finish too
                until = Time.time + 300f;
                while (Time.time < until)
                {
                    bool all = true;
                    foreach (var p in PlayerNet.All) if (p != null && p.TutStep.Value != Tutorial.FinishedStep) all = false;
                    if (all) break;
                    yield return null;
                }
                bool allDone = true;
                foreach (var p in PlayerNet.All) allDone &= p != null && p.TutStep.Value == Tutorial.FinishedStep;
                Check(allDone, $"every player finished the tutorial ({PlayerNet.All.Count} players)");
                yield return new WaitForSeconds(3f);
            }
        }
    }
}
