using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Headless end-to-end test driver. Inactive unless launched with "-autotest ball", "-autotest sd" or "-autotest shots".
    /// Drives real RPCs through the network: gather -> craft -> build -> chest/bag/berries/barrier -> (ball capture | sudden death kill).
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        string m_Mode;

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-autotest") m_Mode = args[i + 1];
            if (m_Mode == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[AUTOTEST] " + s);

        IEnumerator Run()
        {
            float timeout = Time.time + 60f;
            while (PlayerNet.Local == null || PlayerController.Local == null)
            {
                if (Time.time > timeout) { Log("FAIL: local player never spawned"); Application.Quit(2); yield break; }
                yield return null;
            }
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            var nm = NetworkManager.Singleton;
            yield return new WaitForSeconds(0.5f);
            Log($"local player spawned: team={Cfg.TeamName[me.Team.Value]} host={nm.IsHost} nodes={FindObjectsByType<ResourceNode>(FindObjectsSortMode.None).Length}");
            Check(Cfg.BaseTeamAt(me.transform.position) == me.Team.Value, $"spawned inside own base ({me.transform.position})");
            Check(me.Count(Item.Rock) == 1 && me.HeldItem == Item.Rock, "start with a rock in hand");
            StartCoroutine(Watch());
            if (m_Mode == "shots") yield return ShotsRoutine(me, pc);
            else if (nm.IsHost) yield return HostRoutine(me, pc);
            else yield return ClientRoutine(me, pc);
        }

        IEnumerator Watch()
        {
            int last = -1;
            while (true)
            {
                var g = NetGame.Instance;
                if (g != null && g.State.Value != last)
                {
                    last = g.State.Value;
                    Log($"state -> {g.S} (timeLeft {g.TimeLeft:0.0}s, players {PlayerNet.All.Count})");
                    if (g.S == GameState.GameOver)
                    {
                        Log($"RESULT winner={(g.Winner.Value < 0 ? "DRAW" : Cfg.TeamName[g.Winner.Value])} reason=\"{g.EndReason.Value}\"");
                        yield return new WaitForSeconds(3f);
                        Application.Quit(0);
                    }
                }
                yield return null;
            }
        }

        /// <summary>Put the item in hand, dragging it onto the last hotbar slot first if it's in the main inventory.</summary>
        static IEnumerator Hold(PlayerNet me, Item id)
        {
            int s = me.HotbarSlotOf(id);
            if (s < 0)
            {
                int from = -1;
                for (int i = Cfg.HotbarSize; i < Cfg.PlayerSlots && from < 0; i++) if (me.SlotAt(i).Id == id) from = i;
                if (from < 0) { Log($"FAIL: no {id} in the inventory"); yield break; }
                var displaced = me.SlotAt(Cfg.HotbarSize - 1);
                me.MoveItemRpc(0, (byte)from, 0, (byte)(Cfg.HotbarSize - 1), me.SlotAt(from).Count, default);
                yield return new WaitForSeconds(0.4f);
                s = me.HotbarSlotOf(id);
                if (s < 0 || me.SlotAt(from).Id != displaced.Id) { Log($"FAIL: dragging {id} onto the hotbar (swap) didn't work"); yield break; }
            }
            me.HeldSlot.Value = (byte)s;
            yield return new WaitForSeconds(0.4f);
        }

        static int RamHits(PlayerNet me)
        {
            int s = me.HotbarSlotOf(Item.Ram);
            return s < 0 ? 0 : me.SlotAt(s).Data;
        }

        IEnumerator ClientRoutine(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];

            // ---- gather ----
            yield return Gather(me, pc, ResourceNode.Tree, 52, baseC);
            yield return Gather(me, pc, ResourceNode.Boulder, 6, baseC);
            Log($"gathered wood={me.Count(Item.Wood)} stone={me.Count(Item.Stone)}");
            Check(me.Count(Item.Wood) >= 180, "gathered enough wood (as inventory items)");

            Vector3 stand = baseC + new Vector3(-4, 0.1f, team == 1 ? -3 : 3);
            pc.LocalTeleport(stand, 0);

            // ---- craft building plan (anywhere) & build ----
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.BuildingPlan) == 1, "crafted building plan");
            yield return Hold(me, Item.BuildingPlan);
            int ci = BuildGrid.CellOf(baseC.x + 0.1f), cj = BuildGrid.CellOf(baseC.z + 0.1f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.3f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(1.3f);
            me.PlaceRpc((byte)PieceType.Wall, ci + 5, cj, 0, 1); // unsupported - must be rejected
            yield return new WaitForSeconds(1.3f);
            Check(CountStructures(PieceType.Foundation, team) == 1, "foundation built");
            Check(CountStructures(PieceType.Wall, team) == 1, "wall built on foundation, floating wall rejected");
            me.PlaceRpc((byte)PieceType.Foundation, 0, 0, 0, 0); // outside the base - must be rejected
            yield return new WaitForSeconds(1.3f);
            Check(CountStructures(PieceType.Foundation, team) == 1, "cannot build outside base");
            Log($"after building wood={me.Count(Item.Wood)}");

            if (m_Mode != "ball") yield break;

            // ---- ball capture ----
            while (Ball.Instance == null) yield return null;
            yield return new WaitForSeconds(4f);
            var ball = Ball.Instance;
            Log($"ball landed at {ball.transform.position}");
            pc.LocalTeleport(new Vector3(ball.transform.position.x + 1.5f, 0.1f, ball.transform.position.z), 0);
            yield return new WaitForSeconds(0.5f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.6f);
            Check(me.CarryingBall, "picked up ball");
            pc.LocalTeleport(baseC + new Vector3(6, 0.1f, 5), 0);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc(Vector3.down);
            yield return new WaitForSeconds(3f);
            Check(!me.CarryingBall && ball.BaseTeam.Value == team, $"threw the ball into own base (BaseTeam={ball.BaseTeam.Value})");
        }

        IEnumerator HostRoutine(PlayerNet me, PlayerController pc)
        {
            if (m_Mode != "sd") yield break;
            yield return HostRaidTest(me, pc);
            while (NetGame.Instance == null || NetGame.Instance.S != GameState.SuddenDeath) yield return null;
            yield return new WaitForSeconds(1.5f);
            PlayerNet other = null;
            foreach (var p in PlayerNet.All) if (p != me) other = p;
            Check(other != null, "opponent present in sudden death");
            Check(Mathf.Abs(me.transform.position.z - Cfg.ArenaCenter.z) < 30f, "host was teleported to the arena");
            Check(Mathf.Abs(other.transform.position.z - Cfg.ArenaCenter.z) < 30f, "client was teleported to the arena");
            Check(me.HeldItem == Item.Rock, "rock forced in sudden death");
            int bagsBefore = CountBags();
            while (NetGame.Instance != null && NetGame.Instance.S == GameState.SuddenDeath)
            {
                var tp = other.transform.position;
                var dir = (me.transform.position - tp);
                dir.y = 0;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
                pc.LocalTeleport(tp + dir.normalized * 1.3f, Quaternion.LookRotation(-dir).eulerAngles.y);
                me.MeleeRpc(true, other.NetworkObject, tp + Vector3.up * 1.0f, false);
                yield return new WaitForSeconds(0.65f);
            }
            Check(CountBags() > bagsBefore && other.Count(Item.Rock) == 0, "killed player dropped everything in a loot bag");
        }

        /// <summary>Host-only (the server may write its own inventory directly): crafting, building, chest, bag, berries, barrier, bow, spear, ram.</summary>
        IEnumerator HostRaidTest(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];
            me.ServerGive(Item.Wood, 3000);
            me.ServerGive(Item.Stone, 2000);

            pc.LocalTeleport(baseC + new Vector3(-4, 0.1f, 3), 0);
            foreach (var it in new[] { Item.BuildingPlan, Item.Hatchet, Item.Pickaxe, Item.Spear, Item.Bow, Item.Arrow, Item.Ram, Item.Chest, Item.Barrier })
            {
                me.CraftRpc(Cfg.RecipeIndex(it));
                yield return new WaitForSeconds(0.3f);
            }
            Check(me.Count(Item.BuildingPlan) == 1 && me.Count(Item.Hatchet) == 1 && me.Count(Item.Pickaxe) == 1 && me.Count(Item.Spear) == 1 && me.Count(Item.Bow) == 1
                  && me.Count(Item.Arrow) == Cfg.ArrowsPerCraft && RamHits(me) == Cfg.RamUses && me.Count(Item.Chest) == 1 && me.Count(Item.Barrier) == 1,
                  "host crafted every item into the inventory (no table)");
            Check(me.Count(Item.Wood) == 3000 - (Cfg.PlanWood + Cfg.HatchetWood + Cfg.PickaxeWood + Cfg.SpearWood + Cfg.BowWood + Cfg.ArrowWood + Cfg.RamWood + Cfg.ChestWood + Cfg.BarrierWood),
                  $"crafting consumed wood items (left {me.Count(Item.Wood)})");

            // build + upgrade + door
            yield return Hold(me, Item.BuildingPlan);
            int ci = BuildGrid.CellOf(baseC.x + 0.1f), cj = BuildGrid.CellOf(baseC.z + 0.1f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Doorway, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Floor, ci, cj, 1, 0);
            yield return new WaitForSeconds(1.2f);
            Structure wall = null, door = null, floor = null;
            foreach (var s in Structure.All)
            {
                if (s.Team.Value != team) continue;
                if (s.PType == PieceType.Wall) wall = s;
                if (s.PType == PieceType.Doorway) door = s;
                if (s.PType == PieceType.Floor) floor = s;
            }
            Check(wall != null && door != null && floor != null, "host built foundation, wall, doorway and floor");
            if (wall != null)
            {
                me.UpgradeRpc(wall.NetworkObject);
                yield return new WaitForSeconds(0.6f);
                Check(wall.Tier.Value == 1 && Mathf.Approximately(wall.Health.Value, Cfg.PieceHp(PieceType.Wall, 1)), "wall upgraded to stone");
            }
            if (door != null)
            {
                pc.LocalTeleport(door.transform.position + new Vector3(2f, -0.9f, 0), 270f);
                yield return new WaitForSeconds(0.4f);
                me.ToggleDoorRpc(door.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(door.DoorOpen.Value, "door opens for owner");
            }

            // chest: place in base, store stuff, take it back
            var chestPos = baseC + new Vector3(8f, 0f, 8f);
            pc.LocalTeleport(chestPos + new Vector3(0, 0.1f, -2.5f), 0);
            yield return Hold(me, Item.Chest);
            me.PlaceDeployableRpc((byte)Item.Chest, chestPos, 180f);
            yield return new WaitForSeconds(0.6f);
            Container chest = null;
            foreach (var c in Container.All) if (!c.IsBag) chest = c;
            Check(chest != null && chest.Slots.Count == Cfg.ChestSlots && me.Count(Item.Chest) == 0, "chest placed in base");
            if (chest != null)
            {
                int stoneSlot = -1;
                for (int i = 0; i < Cfg.PlayerSlots && stoneSlot < 0; i++) if (me.SlotAt(i).Id == Item.Stone) stoneSlot = i;
                int before = me.Count(Item.Stone);
                me.MoveItemRpc(0, (byte)stoneSlot, 1, 3, 250, chest.NetworkObject);
                yield return new WaitForSeconds(0.4f);
                Check(chest.Slots[3].Id == Item.Stone && chest.Slots[3].Count == 250 && me.Count(Item.Stone) == before - 250, "dragged 250 stone into the chest (split stack)");
                me.MoveItemRpc(1, 3, 0, 255, 0, chest.NetworkObject);
                yield return new WaitForSeconds(0.4f);
                Check(chest.Slots[3].Empty && me.Count(Item.Stone) == before, "shift-click took it back out");
            }
            var farPos = new Vector3(0, 0.1f, 0);
            Check(PlayerNet.DeployProblem(Item.Chest, team, Cfg.BaseCenter[1 - team], 0) != null && PlayerNet.DeployProblem(Item.Chest, team, farPos, 0) != null, "chests only allowed in own base");

            // barrier out in the field
            var barrierPos = new Vector3(20f, 0f, -30f);
            pc.LocalTeleport(barrierPos + new Vector3(0, 0.1f, -3f), 0);
            yield return Hold(me, Item.Barrier);
            me.PlaceDeployableRpc((byte)Item.Barrier, barrierPos, 0f);
            yield return new WaitForSeconds(0.6f);
            Check(CountStructures(PieceType.Barrier, team) == 1 && me.Count(Item.Barrier) == 0, "barrier placed out on the map");

            // berries: pick and eat
            ResourceNode bush = null;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.IsBush && (bush == null || (n.transform.position - me.transform.position).sqrMagnitude < (bush.transform.position - me.transform.position).sqrMagnitude)) bush = n;
            Check(bush != null, "berry bushes spawned");
            if (bush != null)
            {
                pc.LocalTeleport(bush.transform.position + new Vector3(1.5f, 0.1f, 0), 270f);
                yield return new WaitForSeconds(0.4f);
                me.PickBerriesRpc(bush.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Berry) == Cfg.BerriesPerPick, "picked berries from a bush");
                me.Health.Value = 50f;
                yield return Hold(me, Item.Berry);
                me.EatRpc();
                yield return new WaitForSeconds(0.5f);
                Check(Mathf.Approximately(me.Health.Value, 50f + Cfg.BerryHeal) && me.Count(Item.Berry) == Cfg.BerriesPerPick - 1, "ate a berry to heal");
                me.Health.Value = Cfg.MaxHealth;
            }

            // bow hit on the opponent
            PlayerNet other = null;
            while (other == null)
            {
                foreach (var p in PlayerNet.All) if (p != me) other = p;
                yield return null;
            }
            yield return Hold(me, Item.Bow);
            float hpBefore = other.Health.Value;
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(other.Health.Value < hpBefore - 40f && me.Count(Item.Arrow) == Cfg.ArrowsPerCraft - 1, $"arrow hit opponent (hp {hpBefore:0} -> {other.Health.Value:0})");
            other.Health.Value = 500f; // padded so the headshot can't kill the client mid-routine
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up * 1.6f, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Abs(other.Health.Value - (500f - Cfg.ArrowPlayerDamage * Cfg.HeadshotMul)) < 0.5f, $"headshot does x{Cfg.HeadshotMul} (500 -> {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;

            // thrown spear sticks in the opponent, then gets pulled out
            yield return Hold(me, Item.Spear);
            hpBefore = other.Health.Value;
            me.ThrowSpearRpc(me.EyePos, Vector3.forward * Cfg.SpearThrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.SpearLandRpc(true, other.NetworkObject, other.transform.position + Vector3.up, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Spear) == 0 && other.StuckSpears.Value == 1 && other.Health.Value < hpBefore, $"thrown spear stuck in opponent (hp {hpBefore:0} -> {other.Health.Value:0})");
            pc.LocalTeleport(other.transform.position + new Vector3(1.5f, 0, 0), 270f);
            yield return new WaitForSeconds(0.4f);
            me.PullSpearRpc(other.NetworkObject);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Spear) == 1 && other.StuckSpears.Value == 0, "pulled the spear out of the opponent");
            other.Health.Value = Cfg.MaxHealth;
            yield return Hold(me, Item.Spear);
            me.ThrowSpearRpc(me.EyePos, Vector3.forward * Cfg.SpearThrowSpeed);
            yield return new WaitForSeconds(0.1f);
            var landAt = me.transform.position + new Vector3(0, 0.05f, 4f);
            me.SpearLandRpc(false, default, landAt, Vector3.down);
            yield return new WaitForSeconds(0.5f);
            Check(NetGame.Instance.Spears.Count == 1 && me.Count(Item.Spear) == 0, $"thrown spear landed on the ground (dropped={NetGame.Instance.Spears.Count})");
            if (NetGame.Instance.Spears.Count == 1)
            {
                pc.LocalTeleport(landAt + new Vector3(0, 0.05f, -1f), 0);
                yield return new WaitForSeconds(0.4f);
                me.PickupSpearRpc(NetGame.Instance.Spears[0].Id);
                yield return new WaitForSeconds(0.5f);
                Check(NetGame.Instance.Spears.Count == 0 && me.Count(Item.Spear) == 1, "picked the spear back up");
            }

            // hand-held ram on the opponent's wall
            float waitUntil = Time.time + 60f;
            Structure enemyWall = null;
            while (enemyWall == null && Time.time < waitUntil)
            {
                foreach (var s in Structure.All) if (s.Team.Value != team && s.PType == PieceType.Wall) enemyWall = s;
                yield return new WaitForSeconds(0.5f);
            }
            Check(enemyWall != null, "opponent built a wall to raid");
            if (enemyWall == null) yield break;
            yield return new WaitForSeconds(4f); // let the client verify its wall before we smash it
            var wp = enemyWall.transform.position;
            yield return Hold(me, Item.Ram);
            pc.LocalTeleport(new Vector3(wp.x, 0.1f, wp.z + 1.5f), 180f);
            yield return new WaitForSeconds(Cfg.RamWindup);
            me.RamStrikeRpc(enemyWall.NetworkObject, wp + Vector3.up * 1.5f);
            yield return new WaitForSeconds(0.6f);
            Check(!enemyWall.IsSpawned && RamHits(me) == Cfg.RamUses - 1, "ram smashed the wooden wall in one hit");
            if (wall != null && wall.IsSpawned)
            {
                // the ram can't hit your own walls, so exercise the stone -> wood path directly on ours
                wall.ServerDowngrade();
                Check(wall.Tier.Value == 0 && Mathf.Approximately(wall.Health.Value, Cfg.PieceHp(PieceType.Wall, 0)), "stone downgrades to full-health wood");
            }
            pc.LocalTeleport(baseC + new Vector3(-4, 0.1f, 3), 0);
            yield return Hold(me, Item.Rock);
        }

        /// <summary>Visual check: build a small base via RPCs and capture screenshots. Run windowed with -host -solo -fast.</summary>
        IEnumerator ShotsRoutine(PlayerNet me, PlayerController pc)
        {
            string dir = ".";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
                yield return new WaitForSeconds(0.3f);
                Log("shot " + name);
            }

            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            me.ServerGive(Item.Wood, 5000);
            me.ServerGive(Item.Stone, 3000);
            pc.LocalTeleport(new Vector3(0, 0.1f, -81f), 0);
            foreach (var it in new[] { Item.BuildingPlan, Item.Bow, Item.Arrow, Item.Ram, Item.Spear, Item.Chest, Item.Barrier, Item.Hatchet })
            {
                me.CraftRpc(Cfg.RecipeIndex(it));
                yield return new WaitForSeconds(0.2f);
            }
            yield return Hold(me, Item.BuildingPlan);

            var plan = new System.Collections.Generic.List<(PieceType, int, int, int, int)>();
            for (int i = -1; i <= 1; i++) for (int j = -26; j <= -25; j++) plan.Add((PieceType.Foundation, i, j, 0, 0));
            for (int i = -1; i <= 1; i++) plan.Add((i == 0 ? PieceType.Doorway : PieceType.Wall, i, -27, 0, 1));
            for (int i = -1; i <= 1; i++) plan.Add((PieceType.Wall, i, -25, 0, 1));
            for (int j = -26; j <= -25; j++) { plan.Add((PieceType.Wall, -2, j, 0, 0)); plan.Add((PieceType.Wall, 1, j, 0, 0)); }
            for (int i = -1; i <= 1; i++) for (int j = -26; j <= -25; j++) plan.Add((PieceType.Floor, i, j, 1, 0));
            plan.Add((PieceType.Wall, -1, -27, 1, 1));
            plan.Add((PieceType.Wall, 1, -27, 1, 1));
            foreach (var (t, i, j, l, d) in plan)
            {
                me.PlaceRpc((byte)t, i, j, l, d);
                yield return new WaitForSeconds(1.0f);
            }
            int n = 0;
            foreach (var s in Structure.All)
                if (s.PType == PieceType.Wall && (n++ % 2 == 0)) { me.UpgradeRpc(s.NetworkObject); yield return new WaitForSeconds(1.35f); }
            Log($"structures={Structure.All.Count}");

            // 1: base with ghost
            pc.LocalTeleport(new Vector3(4, 0.1f, -88f), 0);
            pc.SetLook(-15f, 14f);
            pc.BuildPiece = PieceType.Foundation;
            yield return Shot("01_base_and_ghost");

            // 2: held items (hands)
            pc.LocalTeleport(new Vector3(14, 0.1f, -92f), 0);
            pc.SetLook(-40f, 12f);
            yield return Hold(me, Item.Ram);
            yield return Shot("02_ram");
            yield return Hold(me, Item.Spear);
            yield return Shot("02b_spear");
            pc.DebugDraw = 1f;
            yield return Shot("02c_spear_aim");
            yield return Hold(me, Item.Bow);
            yield return Shot("02d_bow_drawn");
            pc.DebugDraw = -1f;
            yield return Hold(me, Item.Rock);
            yield return Shot("02e_rock");
            foreach (var it in new[] { Item.Rock, Item.Hatchet })
            {
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.5f);
                pc.DebugSwing(0.8f);
                yield return new WaitForSeconds(ViewModel.ImpactTime * 0.4f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"02f_{it}_windup.png"));
                yield return new WaitForSeconds(ViewModel.ImpactTime * 0.8f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"02g_{it}_slam.png"));
                yield return new WaitForSeconds(0.8f);
            }

            // 3: chest + barrier placed
            pc.LocalTeleport(new Vector3(8, 0.1f, -66f), 180f);
            yield return Hold(me, Item.Chest);
            me.PlaceDeployableRpc((byte)Item.Chest, new Vector3(8, 0, -69f), 0f);
            yield return new WaitForSeconds(0.4f);
            pc.LocalTeleport(new Vector3(10, 0.1f, -50f), 180f);
            yield return Hold(me, Item.Barrier);
            me.PlaceDeployableRpc((byte)Item.Barrier, new Vector3(10, 0, -53f), 0f);
            yield return new WaitForSeconds(0.4f);
            pc.LocalTeleport(new Vector3(12, 0.1f, -46f), 200f);
            pc.SetLook(200f, 18f);
            yield return Hold(me, Item.Rock);
            yield return Shot("03_barrier");

            // 4: tree weak spot (X)
            ResourceNode tree = null, rock = null, bush = null;
            foreach (var node in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            {
                if (node.Kind.Value == ResourceNode.Tree && tree == null) tree = node;
                if (node.Kind.Value == ResourceNode.Boulder && rock == null) rock = node;
                if (node.IsBush && bush == null) bush = node;
            }
            foreach (var (node, name) in new[] { (tree, "04_tree_x"), (rock, "05_rock_star"), (bush, "06_bush") })
            {
                if (node == null) continue;
                Vector3 look = node.transform.position + Vector3.up * 1f;
                if (node.TryGetSpot(out var sp, out var nrm)) { look = sp; }
                var from = look + (node.TryGetSpot(out _, out var n2) ? new Vector3(n2.x, 0, n2.z).normalized : Vector3.back) * 2.2f;
                from.y = 0.1f;
                var d = look - (from + Vector3.up * Cfg.EyeHeight);
                pc.LocalTeleport(from, Quaternion.LookRotation(new Vector3(d.x, 0, d.z)).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(new Vector3(d.x, 0, d.z)).eulerAngles.y, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                yield return Shot(name);
            }

            // 7: ball in hands
            while (Ball.Instance == null) yield return null;
            yield return new WaitForSeconds(4f);
            pc.LocalTeleport(Ball.Instance.transform.position + new Vector3(0, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.5f);
            pc.SetLook(0f, 5f);
            yield return Shot("07_ball_in_hands");

            // 8: inventory + crafting
            pc.MenuOpen = true;
            yield return Shot("08_inventory");
            pc.MenuOpen = false;

            // 9: the other player's alien model (only when a client joined instead of -solo)
            foreach (var p in PlayerNet.All)
            {
                if (p == me) continue;
                var fwd = p.transform.forward;
                pc.LocalTeleport(p.transform.position + fwd * 3f + Vector3.up * 0.1f, Quaternion.LookRotation(-fwd).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(-fwd).eulerAngles.y, 8f);
                yield return Shot("09_alien");
                // blood: hit them
                me.ThrowBallRpc(Vector3.down);
                yield return new WaitForSeconds(0.3f);
                yield return Hold(me, Item.Hatchet);
                Fx.Blood(p.transform.position + Vector3.up * 1.5f, -fwd, true);
                Fx.DamageNumber(p.transform.position + Vector3.up * 1.5f, 28, true);
                Hud.HitMarker(false, true);
                yield return new WaitForSeconds(0.05f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "10_blood.png"));
                yield return new WaitForSeconds(0.3f);
            }
            Application.Quit(0);
        }

        IEnumerator Gather(PlayerNet me, PlayerController pc, byte kind, int hits, Vector3 near)
        {
            ResourceNode node = null;
            for (int h = 0; h < hits; h++)
            {
                if (node == null || node.Amount.Value <= 0)
                {
                    node = Nearest(kind, near);
                    if (node == null) { Log("FAIL: no resource node"); yield break; }
                    var dir = (near - node.transform.position);
                    dir.y = 0;
                    dir.Normalize();
                    pc.LocalTeleport(node.transform.position + dir * 2.3f + Vector3.up * 0.1f, Quaternion.LookRotation(-dir).eulerAngles.y);
                    yield return new WaitForSeconds(0.3f);
                }
                me.MeleeRpc(true, node.NetworkObject, node.transform.position + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(0.65f);
            }
        }

        static ResourceNode Nearest(byte kind, Vector3 p)
        {
            ResourceNode best = null;
            float bd = float.MaxValue;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            {
                if (n.Kind.Value != kind || n.Amount.Value <= 0) continue;
                float d = (n.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        static int CountStructures(PieceType t, int team)
        {
            int c = 0;
            foreach (var s in Structure.All) if (s.PType == t && s.Team.Value == team) c++;
            return c;
        }

        static int CountBags()
        {
            int c = 0;
            foreach (var b in Container.All) if (b.IsBag) c++;
            return c;
        }

        static void Check(bool ok, string what) => Log((ok ? "PASS: " : "FAIL: ") + what);
    }
}
