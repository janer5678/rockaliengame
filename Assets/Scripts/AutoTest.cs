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
            StartCoroutine(Watch());
            Log($"local player spawned: team={Cfg.TeamName[me.Team.Value]} host={nm.IsHost} mode={Cfg.ModeLabel} nodes={FindObjectsByType<ResourceNode>(FindObjectsSortMode.None).Length}");
            if (NetGame.Instance.S == GameState.Waiting)
                Check(Vector3.Distance(me.transform.position, Cfg.ArenaCenter) < 30f && me.HeldItem == Item.Rock, "waiting for players in the stadium with a rock");
            while (NetGame.Instance.S == GameState.Waiting) yield return null;
            yield return new WaitForSeconds(0.8f);
            if (m_Mode == "teams") { yield return TeamsRoutine(me); yield break; }
            Check(Cfg.BaseTeamAt(me.transform.position) == me.Team.Value, $"spawned inside own base ({me.transform.position})");
            Check(me.Count(Item.Rock) == 0 && me.HeldItem == Item.Rock, "empty hand = holding the rock (no rock item)");
            Check(Vector3.Distance(me.transform.position, Cfg.SpawnPos(me.Team.Value, me.Slot.Value)) < 1.5f, $"sent home to the bedrock when the match started on {Cfg.MapLabel} (seed {Cfg.MapSeed})");
            Check(NetGame.Instance != null && NetGame.Instance.MapKey.Value == Cfg.MapKey && NetGame.Instance.MapSeed.Value == Cfg.MapSeed, "map + seed synced from the host");
            var bc = Cfg.BedrockCenter(me.Team.Value);
            var rockCell = new PieceKey(PieceKey.KFoundation, BuildGrid.CellOf(bc.x + 0.1f), BuildGrid.CellOf(bc.z + 0.1f), 0, 0);
            Check(BuildGrid.OnBedrock(rockCell), "can't build a foundation on the bedrock");
            Check(BuildGrid.IsSupported(new PieceKey(PieceKey.KEdge, rockCell.I, rockCell.J, 0, 1), k => false), "walls stand on the bedrock without a foundation");
            if (m_Mode == "shots") yield return ShotsRoutine(me, pc);
            else if (nm.IsHost) yield return HostRoutine(me, pc);
            else yield return ClientRoutine(me, pc);
        }

        /// <summary>2v2 / free for all: every player checks where they ended up; the host checks the teams and quits the match.</summary>
        IEnumerator TeamsRoutine(PlayerNet me)
        {
            Check(Cfg.BaseTeamAt(me.transform.position) == me.Team.Value && Vector3.Distance(me.transform.position, Cfg.SpawnPos(me.Team.Value, me.Slot.Value)) < 1.5f,
                  $"{Cfg.TeamName[me.Team.Value]} (slot {me.Slot.Value}) sent home to its own base in {Cfg.ModeLabel} (at {me.transform.position}, spawn {Cfg.SpawnPos(me.Team.Value, me.Slot.Value)})");
            Check(Machine.ByTeam[me.Team.Value] != null, "our base has its machine");
            yield return new WaitForSeconds(3f);
            if (NetworkManager.Singleton.IsHost)
            {
                var perTeam = new int[4];
                foreach (var p in PlayerNet.All) perTeam[p.Team.Value]++;
                bool ok = true;
                for (int t = 0; t < Cfg.TeamCount; t++) ok &= perTeam[t] == Cfg.PlayersNeeded / Cfg.TeamCount;
                Check(ok && PlayerNet.All.Count == Cfg.PlayersNeeded, $"{Cfg.ModeLabel}: {PlayerNet.All.Count} players split {perTeam[0]}/{perTeam[1]}/{perTeam[2]}/{perTeam[3]} over {Cfg.TeamCount} teams");
                int bases = 0;
                for (int t = 0; t < 4; t++) if (Machine.ByTeam[t] != null) bases++;
                Check(bases == Cfg.TeamCount, $"{bases} bases built");
                // the wild respawn goes into an enemy's side
                me.ServerRespawn(true);
                yield return new WaitForSeconds(0.8f);
                int region = Cfg.RegionOf(me.transform.position);
                Check(region != me.Team.Value && Cfg.BaseTeamAt(me.transform.position) < 0, $"wild respawn landed on {Cfg.TeamName[region]}'s side");
                // sudden death is last team standing
                NetGame.Instance.DevStartSuddenDeath();
                yield return new WaitForSeconds(1f);
                foreach (var p in PlayerNet.All) if (p.Team.Value != me.Team.Value) p.ServerKill(me);
                yield return new WaitForSeconds(1f);
                Check(NetGame.Instance.S == GameState.GameOver && NetGame.Instance.Winner.Value == me.Team.Value, $"last team standing won ({NetGame.Instance.EndReason.Value})");
            }
            else
            {
                while (NetGame.Instance != null && NetGame.Instance.S != GameState.GameOver) yield return null;
            }
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
            if (id == Item.Rock)
            {
                // the rock = any empty hotbar slot
                for (int i = 0; i < Cfg.HotbarSize; i++) if (me.SlotAt(i).Empty) { me.HeldSlot.Value = (byte)i; break; }
                yield return new WaitForSeconds(0.3f);
                yield break;
            }
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
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Ram) return me.SlotAt(i).Data;
            return 0;
        }

        IEnumerator ClientRoutine(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];

            // ---- gather ----
            yield return Gather(me, pc, ResourceNode.Tree, 52, baseC);
            if (Cfg.WoodMode)
            {
                bool stone = false;
                foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.Kind.Value == ResourceNode.Boulder) stone = true;
                Check(!stone && Cfg.RecipeIndex(Item.Pickaxe) < 0 && Cfg.GetRecipe(Cfg.RecipeIndex(Item.Bow)).Stone == 0, "wood mode: no stone nodes, no pickaxe, recipes cost wood only");
            }
            else yield return Gather(me, pc, ResourceNode.Boulder, 6, baseC);
            Log($"gathered wood={me.Count(Item.Wood)} stone={me.Count(Item.Stone)}");
            Check(me.Count(Item.Wood) >= 180, "gathered enough wood (as inventory items)");

            // ---- crafting only works at the machine ----
            pc.LocalTeleport(baseC + new Vector3(Cfg.BaseHalf + 6f, 0.1f, 0), 0);
            yield return new WaitForSeconds(0.3f);
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.BuildingPlan) == 0, "can't craft outside your base");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);

            // ---- craft building plan at the machine & build ----
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.6f);
            Check(me.Count(Item.BuildingPlan) == 1, "crafted building plan");
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0); // stand next to it (the UFO moves the free cells around)
            yield return new WaitForSeconds(0.3f);
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
            Check(!me.CarryingBall && ball.BaseTeam.Value == team && ball.SocketTeam.Value < 0, $"ball lying in own base does not count yet (BaseTeam={ball.BaseTeam.Value}, Socket={ball.SocketTeam.Value})");
            pc.LocalTeleport(ball.transform.position + new Vector3(1.5f, 0.1f, 0), 270f);
            yield return new WaitForSeconds(0.5f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.6f);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team) + 180f);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc((Cfg.SocketPos(team) - me.EyePos).normalized);
            yield return new WaitForSeconds(2f);
            Check(!me.CarryingBall && ball.SocketTeam.Value == team, $"threw the ball into own machine's socket and it snapped in (Socket={ball.SocketTeam.Value})");
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
            int stuff = 0;
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (!me.SlotAt(i).Empty) stuff++;
            Check(stuff == 0 && me.ArmorHp.Value == 0 && me.HelmetHp.Value == 0, "inventory, armour and helmet cleared for sudden death");
            Check(NetGame.Instance.FightFrozen, "sudden death starts with the stadium countdown");
            other.ServerGive(Item.Wood, 50); // something to spill (the raid test may already have killed them once)
            int itemsBefore = NetGame.Instance.Items.Count;
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
            Check(NetGame.Instance.Items.Count > itemsBefore && other.Count(Item.Wood) == 0, $"killed player's items spilled out of the body ({NetGame.Instance.Items.Count - itemsBefore} piles)");
        }

        /// <summary>Host-only (the server may write its own inventory directly): crafting, building, chest, bag, berries, barrier, bow, spear, ram.</summary>
        IEnumerator HostRaidTest(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];
            yield return Hold(me, Item.Rock);
            me.ServerGive(Item.Wood, 3000);
            me.ServerGive(Item.Stone, 2000);
            yield return new WaitForSeconds(0.3f);
            Check(me.SlotAt(6).Id == Item.Wood && me.SlotAt(5).Id == Item.Wood && me.SlotAt(4).Id == Item.Wood && me.SlotAt(3).Id == Item.Stone && me.SlotAt(me.HeldSlot.Value).Empty,
                  "materials fill the hotbar from slot 7 backwards and keep clear of the rock slot");

            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            foreach (var it in new[] { Item.BuildingPlan, Item.Hatchet, Item.Pickaxe, Item.Spear, Item.Bow, Item.Arrow, Item.Ram, Item.Chest, Item.Barrier })
            {
                me.CraftRpc(Cfg.RecipeIndex(it));
                yield return new WaitForSeconds(0.3f);
            }
            Check(me.Count(Item.BuildingPlan) == 1 && me.Count(Item.Hatchet) == 1 && me.Count(Item.Pickaxe) == 1 && me.Count(Item.Spear) == 1 && me.Count(Item.Bow) == 1
                  && me.Count(Item.Arrow) == Cfg.ArrowsPerCraft && RamHits(me) == Cfg.RamUses && me.Count(Item.Chest) == 1 && me.Count(Item.Barrier) == 1,
                  "host crafted every item in base");
            {
                var sb = new System.Text.StringBuilder("inventory after crafting: ");
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (!me.SlotAt(i).Empty) sb.Append($"[{i}]{me.SlotAt(i).Id}x{me.SlotAt(i).Count}/{me.SlotAt(i).Data} ");
                Log(sb.ToString());
            }
            Check(me.Count(Item.Wood) == 3000 - (Cfg.PlanWood + Cfg.HatchetWood + Cfg.PickaxeWood + Cfg.SpearWood + Cfg.BowWood + Cfg.ArrowWood + Cfg.RamWood + Cfg.ChestWood + Cfg.BarrierWood),
                  $"crafting consumed wood items (left {me.Count(Item.Wood)})");

            // build + upgrade + door
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0); // stand next to it (the UFO moves the free cells around)
            yield return new WaitForSeconds(0.3f);
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
                Check(wall.Tier.Value == 1 && Mathf.Approximately(wall.Health.Value, Cfg.PieceHp(PieceType.Wall, 1)), $"wall upgraded to stone (tier {wall.Tier.Value}, hp {wall.Health.Value}, held {me.HeldItem}, stone {me.Count(Item.Stone)})");
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
            FreeCell(team, 1, out int chI, out int chJ);
            var chestPos = BuildGrid.CellCenter(chI, chJ);
            pc.LocalTeleport(chestPos + new Vector3(0, 0.1f, -2.5f), 0);
            yield return Hold(me, Item.Chest);
            me.PlaceDeployableRpc((byte)Item.Chest, chestPos, 180f);
            yield return new WaitForSeconds(0.6f);
            Container chest = null;
            foreach (var c in Container.All) if (c.Breakable) chest = c;
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
            var onRock = Cfg.BedrockCenter(team) + new Vector3(2.3f, Cfg.BaseY, 0);
            Check(PlayerNet.DeployProblem(Item.Chest, team, onRock, 0) == null && PlayerNet.DeployProblem(Item.Chest, team, Cfg.SpawnPos(team), 0) != null, "chests can go on the bedrock by the machine (not on the spawn spot)");

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
                Check(me.Count(Item.Berry) == 1 && !bush.IsSpawned, "picked the whole berry bush (it's gone)");
                me.Health.Value = 50f;
                yield return Hold(me, Item.Berry);
                me.EatRpc();
                yield return new WaitForSeconds(0.5f);
                Check(Mathf.Approximately(me.Health.Value, 50f + Cfg.BerryHeal) && me.Count(Item.Berry) == 0, $"ate the berries to heal {Cfg.BerryHeal:0}");
                me.Health.Value = Cfg.MaxHealth;
            }

            // bow hit on the opponent
            PlayerNet other = null;
            while (other == null)
            {
                foreach (var p in PlayerNet.All) if (p != me) other = p;
                yield return null;
            }
            me.ServerGive(Item.Arrow, 10);
            int arrowsBefore = me.Count(Item.Arrow) + 1;
            yield return Hold(me, Item.Bow);
            float hpBefore = other.Health.Value;
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(other.Health.Value < hpBefore - 40f && me.Count(Item.Arrow) == arrowsBefore - 2, $"arrow hit opponent (hp {hpBefore:0} -> {other.Health.Value:0})");
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
            int spearItem = FindWorldItem(Item.Spear);
            Check(spearItem >= 0 && me.Count(Item.Spear) == 0, "thrown spear landed on the ground as a world item");
            if (spearItem >= 0)
            {
                pc.LocalTeleport(landAt + new Vector3(0, 0.05f, -1f), 0);
                yield return new WaitForSeconds(0.4f);
                me.PickupItemRpc(spearItem);
                yield return new WaitForSeconds(0.5f);
                Check(FindWorldItem(Item.Spear) < 0 && me.Count(Item.Spear) == 1, "picked the spear back up (E)");
            }

            // drag an item out of the inventory onto the ground, then pick it back up
            int woodSlot = -1;
            for (int i = 0; i < Cfg.PlayerSlots && woodSlot < 0; i++) if (me.SlotAt(i).Id == Item.Wood) woodSlot = i;
            int woodBefore = me.Count(Item.Wood);
            me.DropItemRpc(0, (byte)woodSlot, 100, default);
            yield return new WaitForSeconds(0.5f);
            int woodItem = FindWorldItem(Item.Wood);
            Check(woodItem >= 0 && me.Count(Item.Wood) == woodBefore - 100, "dropped 100 wood on the ground");
            me.PickupItemRpc(woodItem);
            yield return new WaitForSeconds(0.5f);
            Check(me.Count(Item.Wood) == woodBefore && FindWorldItem(Item.Wood) < 0, "picked the wood back up");

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
            // demolish one of your own pieces with the building plan (part of the wood comes back)
            if (floor != null && floor.IsSpawned)
            {
                yield return Hold(me, Item.BuildingPlan);
                pc.LocalTeleport(floor.transform.position + new Vector3(-4f, -3.9f, 0), 90f);
                yield return new WaitForSeconds(0.3f);
                int woodBefore2 = me.Count(Item.Wood);
                me.DemolishRpc(floor.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(!floor.IsSpawned && me.Count(Item.Wood) == woodBefore2 + Mathf.FloorToInt(Cfg.FloorWood * Cfg.DemolishRefund), "demolished own floor with the plan (wood refunded)");
            }

            // the helmet stops one headshot completely and breaks
            other.Health.Value = 500f;
            other.HelmetHp.Value = 1;
            yield return Hold(me, Item.Bow);
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up * 1.6f, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(other.HelmetHp.Value == 0 && Mathf.Abs(other.Health.Value - 500f) < 0.5f, $"helmet stopped a headshot completely and broke (hp {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;
            me.ServerGive(Item.Helmet, 1, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Helmet);
            me.UseItemRpc();
            yield return new WaitForSeconds(0.9f);
            Check(me.HelmetHp.Value == 1 && me.Count(Item.Helmet) == 0, "put on a found helmet");
            me.HelmetHp.Value = 0;

            // wooden armour: a second bar that takes the damage first
            other.ArmorHp.Value = 50;
            other.Health.Value = Cfg.MaxHealth;
            other.ServerDamage(80f, me);
            Check(other.ArmorHp.Value == 0 && Mathf.Abs(other.Health.Value - (Cfg.MaxHealth - 30f)) < 0.5f, $"armour soaked up the first 50 of 80 damage (hp {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;
            me.ServerGive(Item.Armor, 1, Cfg.ArmorHp);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Armor);
            me.UseItemRpc();
            yield return new WaitForSeconds(0.9f);
            Check(me.ArmorHp.Value == Cfg.ArmorHp && me.Count(Item.Armor) == 0, "put the armour on");
            me.ArmorHp.Value = 0;

            // crossbow: loads a bolt (uses an arrow), fires one hard hit
            me.ServerGive(Item.Crossbow, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Crossbow);
            int arrowsNow = me.Count(Item.Arrow);
            me.ReloadCrossbowRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == 1 && me.Count(Item.Arrow) == arrowsNow - 1, "crossbow loaded a bolt");
            other.Health.Value = 500f;
            me.FireCrossbowRpc(me.EyePos, Vector3.forward * Cfg.CrossbowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up, Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Abs(other.Health.Value - (500f - Cfg.CrossbowDamage)) < 0.5f, $"crossbow bolt did {Cfg.CrossbowDamage:0} (500 -> {other.Health.Value:0})");
            other.Health.Value = Cfg.MaxHealth;

            // a window, and walls behind the machine
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 2, out int wi, out int wj);
            pc.LocalTeleport(BuildGrid.CellCenter(wi, wj) + new Vector3(-4f, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Foundation, wi, wj, 0, 0);
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Window, wi - 1, wj, 0, 0);
            yield return new WaitForSeconds(0.5f);
            Check(CountStructures(PieceType.Window, team) == 1, "built a window");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            int bi = Mathf.RoundToInt(Cfg.BedrockCenter(team).x / Cfg.Cell), bj = Mathf.RoundToInt(Cfg.BedrockCenter(team).z / Cfg.Cell);
            int backJ = Cfg.BackDir(team).z < 0 ? bj - 2 : bj; // edge on the far side of the bedrock
            int wallsBefore = CountStructures(PieceType.Wall, team);
            me.PlaceRpc((byte)PieceType.Wall, bi - 1, backJ, 0, 1);
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Wall, bi, backJ, 0, 1);
            yield return new WaitForSeconds(0.5f);
            Check(CountStructures(PieceType.Wall, team) == wallsBefore + 2, "walls fit behind the machine on the bedrock's back edge");

            // fort tower
            me.ServerGive(Item.FortTower, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.FortTower);
            var fortAt = new Vector3(-20f, 0f, baseC.z > 0 ? 35f : -35f);
            pc.LocalTeleport(fortAt + new Vector3(0, 1f, -6f), 0);
            yield return new WaitForSeconds(0.9f);
            me.ThrowFortRpc(me.EyePos, Vector3.forward * 10f);
            yield return new WaitForSeconds(0.1f);
            me.FortLandRpc(fortAt + Vector3.up * 0.5f);
            yield return new WaitForSeconds(0.6f);
            Check(CountStructures(PieceType.Tower, team) == 1 && me.Count(Item.FortTower) == 0, "threw a fort tower and it went up");

            // wooden car: place it, get in, get out
            me.ServerGive(Item.Car, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Car);
            var carAt = Vector3.zero;
            for (float cx2 = 10f; cx2 < 80f; cx2 += 4f)
            {
                carAt = new Vector3(cx2, 0f, baseC.z > 0 ? 35f : -35f);
                carAt.y = MapBuilder.Height(carAt.x, carAt.z);
                if (PlayerNet.DeployProblem(Item.Car, team, carAt, 0f) == null) break;
            }
            pc.LocalTeleport(carAt + new Vector3(0, 0.3f, -3.5f), 0);
            yield return new WaitForSeconds(0.9f);
            me.PlaceDeployableRpc((byte)Item.Car, carAt, 0f);
            yield return new WaitForSeconds(0.6f);
            Vehicle car = null;
            foreach (var v in Vehicle.All) if (!v.IsHorse) car = v;
            Check(car != null && me.Count(Item.Car) == 0, "placed the wooden car");
            if (car != null)
            {
                me.MountRpc(car.NetworkObject);
                yield return new WaitForSeconds(0.6f);
                Check(me.Riding && car.HasDriver, "got in the car");
                me.DismountRpc();
                yield return new WaitForSeconds(0.6f);
                Check(!me.Riding && !car.HasDriver, "got out of the car");
            }

            // horses: need a saddle
            int horses = 0;
            Vehicle horse = null;
            foreach (var v in Vehicle.All) if (v.IsHorse) { horses++; if (horse == null || (v.transform.position - me.transform.position).sqrMagnitude < (horse.transform.position - me.transform.position).sqrMagnitude) horse = v; }
            Check(horses == Cfg.HorsesPerSide * 2, $"wild horses roam the map ({horses})");
            if (horse != null)
            {
                pc.LocalTeleport(horse.transform.position + new Vector3(1.8f, 0.5f, 0), 270f);
                yield return new WaitForSeconds(0.6f);
                me.MountRpc(horse.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(!me.Riding, "can't ride a wild horse without a saddle");
                me.ServerGive(Item.Saddle, 1);
                yield return new WaitForSeconds(0.2f);
                pc.LocalTeleport(horse.transform.position + new Vector3(1.8f, 0.5f, 0), 270f);
                yield return new WaitForSeconds(0.4f);
                me.MountRpc(horse.NetworkObject);
                yield return new WaitForSeconds(0.6f);
                Check(me.Riding && horse.Saddled.Value && me.Count(Item.Saddle) == 0, "saddled the horse and got on");
                me.DismountRpc();
                yield return new WaitForSeconds(0.5f);
            }

            // C4 on the opponent's foundation
            Structure enemyFoundation = null;
            foreach (var s in Structure.All) if (s.Team.Value != team && s.PType == PieceType.Foundation) enemyFoundation = s;
            if (enemyFoundation != null)
            {
                me.ServerGive(Item.C4, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.C4);
                var fp = enemyFoundation.transform.position;
                pc.LocalTeleport(new Vector3(fp.x, 0.1f, fp.z + 6f), 180f);
                yield return new WaitForSeconds(0.9f);
                me.ThrowC4Rpc(me.EyePos, Vector3.back * 10f);
                yield return new WaitForSeconds(0.1f);
                me.C4LandRpc(fp + Vector3.up * 1.03f, Vector3.up);
                yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                Check(!enemyFoundation.IsSpawned && me.Count(Item.C4) == 0, "C4 blew up the enemy foundation");
            }
            else Check(false, "C4: no enemy foundation to blow up");

            // respawn in the wild lands in the enemy half
            other.ServerRespawn(true);
            yield return new WaitForSeconds(0.8f);
            var ec = Cfg.BaseCenter[team];
            Check(Mathf.Sign(other.transform.position.z) == Mathf.Sign(ec.z) && Cfg.BaseTeamAt(other.transform.position) < 0, $"wild respawn puts you in the enemy half, outside their base ({other.transform.position})");

            // death wand: one shot, instant kill
            me.ServerGive(Item.DeathWand, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.DeathWand);
            var op = other.transform.position;
            pc.LocalTeleport(op + new Vector3(0, 1.5f, -8f), 0f);
            yield return new WaitForSeconds(0.9f);
            other.Health.Value = Cfg.MaxHealth;
            me.WandRpc(((op + Vector3.up * 1.4f) - me.EyePos + new Vector3(1.2f, 0, 0)).normalized); // a near miss still kills
            yield return new WaitForSeconds(0.5f);
            Check(other.Dead.Value && me.Count(Item.DeathWand) == 0, "death wand near miss killed instantly");

            // ---------------- airdrop items ----------------
            NetGame.Instance.TimerPaused.Value = true; // plenty of time for these
            yield return LootTests(me, pc, other, team, baseC);

            // airdrop: call one in, open it and take the item
            NetGame.Instance.DevSpawnAirdrop();
            float dropWait = Time.time + 20f;
            Container drop = null;
            while (drop == null && Time.time < dropWait && NetGame.Instance.S == GameState.BallLive)
            {
                foreach (var c in Container.All) if (c.IsAirdrop && Cfg.BaseTeamAt(c.transform.position) < 0) drop = c;
                yield return new WaitForSeconds(0.5f);
            }
            Check(drop != null, "an airdrop was beamed down");
            if (drop != null)
            {
                var dp = drop.transform.position;
                Check(Mathf.Max(Mathf.Abs(dp.x - Cfg.BaseCenter[0].x), Mathf.Abs(dp.z - Cfg.BaseCenter[0].z)) - Cfg.BaseHalf >= Cfg.AirdropBaseDistance - 0.5f
                      && Mathf.Max(Mathf.Abs(dp.x - Cfg.BaseCenter[1].x), Mathf.Abs(dp.z - Cfg.BaseCenter[1].z)) - Cfg.BaseHalf >= Cfg.AirdropBaseDistance - 0.5f, $"airdrop not next to a base ({dp})");
                var loot = drop.Slots[0];
                pc.LocalTeleport(dp + new Vector3(0, 0.1f, -2f), 0f);
                yield return new WaitForSeconds(0.4f);
                int had = me.Count(loot.Id);
                me.MoveItemRpc(1, 0, 0, 255, 0, drop.NetworkObject);
                yield return new WaitForSeconds(1.2f);
                Check(me.Count(loot.Id) == had + loot.Count && !drop.IsSpawned, $"took the airdrop's {Cfg.ItemName(loot.Id)} x{loot.Count}, crate disappeared");
            }

            NetGame.Instance.TimerPaused.Value = false;
            NetGame.Instance.DevSetTimeLeft(3f);
            pc.LocalTeleport(baseC + new Vector3(-4, 0.1f, 3), 0);
            yield return Hold(me, Item.Rock);
        }

        IEnumerator LootTests(PlayerNet me, PlayerController pc, PlayerNet other, int team, Vector3 baseC)
        {
            var g = NetGame.Instance;
            void Revive(PlayerNet p) { if (p.Dead.Value) p.ServerRespawn(false); p.Health.Value = Cfg.MaxHealth; p.ArmorHp.Value = 0; }
            var field = new Vector3(-30f, 0f, baseC.z > 0 ? 30f : -30f);
            field.y = MapBuilder.Height(field.x, field.z);

            // sniper: one shot kills, a helmet stops a headshot
            Revive(other);
            me.ServerGive(Item.Sniper, 1, Cfg.SniperAmmo);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Sniper);
            other.HelmetHp.Value = 1;
            me.SniperFireRpc(true, other.NetworkObject, other.transform.position + Vector3.up * 1.6f, Vector3.forward);
            yield return new WaitForSeconds(0.4f);
            Check(!other.Dead.Value && other.HelmetHp.Value == 0, "a helmet stopped a sniper headshot");
            yield return new WaitForSeconds(1.2f);
            other.ArmorHp.Value = 100;
            me.SniperFireRpc(true, other.NetworkObject, other.transform.position + Vector3.up * 1f, Vector3.forward);
            yield return new WaitForSeconds(0.4f);
            Check(other.Dead.Value && me.HeldStack.Data == Cfg.SniperAmmo - 2, "sniper body shot killed through armour");
            Revive(other);
            yield return new WaitForSeconds(0.5f);

            // portal gun: two shots = one linked pair, then it's used up
            me.ServerGive(Item.PortalGun, 1, Cfg.PortalShots);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.PortalGun);
            int portals = g.Portals.Count;
            me.PortalRpc(field + new Vector3(0, 0.05f, 0), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            me.PortalRpc(field + new Vector3(12f, 0.05f, 0), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            Check(g.Portals.Count == portals + 2 && g.Portals[portals].Pair == g.Portals[portals + 1].Pair && me.Count(Item.PortalGun) == 0, "portal gun made a linked pair of portals and was used up");

            // jetpack burns fuel and runs out
            me.ServerGive(Item.Jetpack, 1, Cfg.JetpackFuel);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Jetpack);
            me.JetFuelRpc(Cfg.JetpackSeconds * 0.25f);
            me.JetFuelRpc(Cfg.JetpackSeconds * 0.25f);
            yield return new WaitForSeconds(0.3f);
            int fuel = me.HeldStack.Data;
            me.JetFuelRpc(2f); me.JetFuelRpc(2f); me.JetFuelRpc(2f); me.JetFuelRpc(2f);
            yield return new WaitForSeconds(0.4f);
            Check(fuel > 30 && fuel < 70 && me.Count(Item.Jetpack) == 0, $"jetpack used fuel ({fuel}%) and ran out");

            // rocket wrecks an enemy barrier
            var bar = Object.Instantiate(Bootstrap.I.structurePrefab, field + new Vector3(0, 0, 6f), Quaternion.identity);
            bar.GetComponent<Structure>().ServerInit(PieceType.Barrier, 1 - team, default, false);
            bar.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
            var barS = bar.GetComponent<Structure>();
            me.ServerGive(Item.RocketLauncher, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.RocketLauncher);
            pc.LocalTeleport(field + new Vector3(0, 0.3f, -4f), 0);
            yield return new WaitForSeconds(0.6f);
            me.ThrowItemRpc(Item.RocketLauncher, me.EyePos, Vector3.forward * Cfg.RocketSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ThrownLandRpc(Item.RocketLauncher, barS.transform.position + Vector3.up * 0.7f, Vector3.back);
            yield return new WaitForSeconds(0.5f);
            Check(!barS.IsSpawned && me.Count(Item.RocketLauncher) == 0, "rocket destroyed an enemy barrier");

            // build egg lays a path of blocks
            int blocks = 0;
            me.ServerGive(Item.BuildEgg, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.BuildEgg);
            me.ThrowItemRpc(Item.BuildEgg, me.EyePos + Vector3.up, new Vector3(1f, 0.5f, 0.3f).normalized * 16f);
            yield return new WaitForSeconds(2.5f);
            foreach (var st in Structure.All) if (st.PType == PieceType.EggBlock) blocks++;
            Check(blocks >= 4, $"build egg laid a path of {blocks} blocks");

            // slenderman hatches (and can be killed)
            me.ServerGive(Item.SlenderEgg, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.SlenderEgg);
            me.ThrowItemRpc(Item.SlenderEgg, me.EyePos, Vector3.forward * 10f);
            yield return new WaitForSeconds(0.1f);
            me.ThrownLandRpc(Item.SlenderEgg, field + new Vector3(-8f, 0.2f, 0), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            Vehicle slender = null;
            foreach (var v in Vehicle.All) if (v.IsSlender) slender = v;
            Check(slender != null, "Slenderman hatched from the egg");
            if (slender != null)
            {
                slender.ServerDamage(9999f, me);
                yield return new WaitForSeconds(0.4f);
                Check(!slender.IsSpawned, "Slenderman can be killed");
            }

            // staff of the giant
            me.ServerGive(Item.GiantStaff, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.GiantStaff);
            me.GiantStaffRpc();
            yield return new WaitForSeconds(0.4f);
            Check(other.Giant && me.Count(Item.GiantStaff) == 0, "staff of the giant turned the opponent into a giant");
            other.GiantUntil.Value = -1;

            // C4 right on top of someone kills them
            Revive(other);
            other.ArmorHp.Value = 100;
            g.ServerArmC4(other.transform.position + Vector3.up * 0.5f, Vector3.up, me);
            yield return new WaitForSeconds(Cfg.C4Fuse + 0.5f);
            Check(other.Dead.Value, "C4 right on top of someone kills them (even with armour)");
            Revive(other);

            // horse: killing it drops meat and its (team coloured) saddle; meat heals you fully
            Vehicle horse = null;
            foreach (var v in Vehicle.All) if (v.IsHorse && v.Saddled.Value) horse = v;
            if (horse == null) foreach (var v in Vehicle.All) if (v.IsHorse) horse = v;
            if (horse != null)
            {
                bool hadSaddle = horse.Saddled.Value;
                int saddleTeam = horse.SaddleTeam.Value;
                var hp = horse.transform.position;
                horse.ServerDamage(9999f, me);
                yield return new WaitForSeconds(0.5f);
                int meat = FindWorldItem(Item.Meat), saddle = FindWorldItem(Item.Saddle);
                Check(!horse.IsSpawned && meat >= 0 && (!hadSaddle || saddle >= 0), "killed a horse: it dropped meat" + (hadSaddle ? " and its saddle" : ""));
                if (hadSaddle && saddle >= 0)
                    foreach (var it in g.Items) if (it.Id == saddle) Check(it.Stack.Data == saddleTeam + 1 && saddleTeam == team, "the saddle is in the team colour of whoever saddled it");
                pc.LocalTeleport(hp + new Vector3(0, 0.5f, -1.5f), 0);
                yield return new WaitForSeconds(0.5f);
                me.PickupItemRpc(meat);
                yield return new WaitForSeconds(0.4f);
                me.Health.Value = 20f;
                yield return Hold(me, Item.Meat);
                me.EatRpc();
                yield return new WaitForSeconds(0.4f);
                Check(Mathf.Approximately(me.Health.Value, Cfg.MaxHealth), "horse meat healed fully");
            }
            else Check(false, "no horse to test");

            // felling a whole tree pays a bonus
            ResourceNode tree = null;
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.Kind.Value == ResourceNode.Tree && n.Amount.Value > 0 && Cfg.BaseTeamAt(n.transform.position) < 0) { tree = n; break; }
            if (tree != null)
            {
                yield return Hold(me, Item.Rock);
                tree.Amount.Value = 3;
                var tp = tree.transform.position;
                pc.LocalTeleport(tp + new Vector3(0, 0.2f, -2f), 0);
                yield return new WaitForSeconds(0.7f);
                int wood = me.Count(Item.Wood);
                me.MeleeRpc(true, tree.NetworkObject, tp + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Wood) == wood + 3 + Cfg.TreeFellBonus, $"felling the whole tree gave the {Cfg.TreeFellBonus} bonus");
            }

            // airstrike flattens the zone (trees too)
            ResourceNode victim = null;
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (!n.IsBush && n.Amount.Value > 0 && Cfg.BaseTeamAt(n.transform.position) < 0 && Vector3.Distance(n.transform.position, other.transform.position) > 30f && Vector3.Distance(n.transform.position, me.transform.position) > 30f) { victim = n; break; }
            if (victim != null)
            {
                me.ServerGive(Item.Airstrike, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.Airstrike);
                me.AirstrikeRpc(victim.transform.position);
                yield return new WaitForSeconds(Cfg.AirstrikeDelay + 0.8f);
                Check(victim.Amount.Value == 0 && me.Count(Item.Airstrike) == 0, "airstrike flattened the trees/rocks in its zone");
            }

            // fake bomb bush: whoever picks it blows up
            me.ServerGive(Item.BombBush, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.BombBush);
            me.ThrowItemRpc(Item.BombBush, me.EyePos, Vector3.forward * 8f);
            yield return new WaitForSeconds(0.1f);
            var bushAt = me.transform.position + me.transform.forward * 3f;
            me.ThrownLandRpc(Item.BombBush, bushAt, Vector3.up);
            yield return new WaitForSeconds(0.6f);
            ResourceNode trap = null;
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.IsBush && n.TrapTeam.Value != ResourceNode.NoTrap) trap = n;
            Check(trap != null, "fake bomb bush planted");
            if (trap != null)
            {
                me.Health.Value = Cfg.MaxHealth;
                pc.LocalTeleport(trap.transform.position + new Vector3(0, 0.3f, -1.5f), 0);
                yield return new WaitForSeconds(0.5f);
                me.PickBerriesRpc(trap.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(me.Dead.Value && !trap.IsSpawned, "picking the fake bomb bush blew us up");
                Revive(me);
                yield return new WaitForSeconds(0.5f);
            }

            // airdrop signal: an airdrop beams straight onto our bedrock
            me.ServerGive(Item.AirdropSignal, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.AirdropSignal);
            me.UseSignalRpc();
            yield return new WaitForSeconds(NetGame.DropLand + 1f);
            bool home = false;
            foreach (var c in Container.All) if (c.IsAirdrop && Cfg.BaseTeamAt(c.transform.position) == team) home = true;
            Check(home && me.Count(Item.AirdropSignal) == 0, "airdrop signal beamed a crate into our base");
        }

        /// <summary>Visual check: capture screenshots of the new features. Run windowed with -host -fast (optionally with a client).</summary>
        IEnumerator ShotsRoutine(PlayerNet me, PlayerController pc)
        {
            string dir = ".";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            void Snap(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("shot " + name); }
            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.6f);
                Snap(name);
                yield return new WaitForSeconds(0.3f);
            }
            int team = me.Team.Value;

            // 1: spawning on the bedrock
            yield return new WaitForSeconds(0.2f);
            NetGame.SpawnPoint(team, false, out var cp, out var cy);
            pc.LocalTeleport(cp, cy);
            pc.SetLook(cy, 8f);
            yield return Shot("01_spawn_on_bedrock");

            // 2: the bedrock and the alien machine from the front, and the glass wall
            var back = Cfg.BackDir(team);
            var front = Cfg.BedrockCenter(team) - back * 7f + Vector3.Cross(Vector3.up, back) * 3f + Vector3.up * 0.1f;
            var look = Cfg.MachinePos(team) + Vector3.up * 1.2f - front;
            pc.LocalTeleport(front, Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y, 6f);
            yield return Shot("02_machine");
            var above = Cfg.BedrockCenter(team) - back * 9f + Vector3.up * 8f;
            pc.LocalTeleport(above, Quaternion.LookRotation(back).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 30f);
            yield return Shot("02b_bedrock_above");
            pc.LocalTeleport(new Vector3(8f, 0.1f, -6f * Mathf.Sign(-back.z)), Quaternion.LookRotation(-back).eulerAngles.y + 20f);
            pc.SetLook(Quaternion.LookRotation(-back).eulerAngles.y + 20f, -5f);
            yield return Shot("02d_glass_wall");
            pc.LocalTeleport(new Vector3(Cfg.MapHalf * 0.55f, 45f, Cfg.BaseCenter[team].z - 10f), 0f);
            pc.SetLook(-30f, 32f);
            yield return Shot("02c_map_overview");

            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            me.ServerGive(Item.Wood, 3000);
            me.ServerGive(Item.Stone, 1000);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            foreach (var it in new[] { Item.BuildingPlan, Item.Bow, Item.Arrow, Item.Hatchet, Item.Spear, Item.Chest })
            {
                me.CraftRpc(Cfg.RecipeIndex(it));
                yield return new WaitForSeconds(0.2f);
            }
            me.ServerGive(Item.Berry, 6);

            // 3: rock (two-handed) idle, swing that connects (bounces up) and one that misses (follows through)
            ResourceNode tree = null;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.Kind.Value == ResourceNode.Tree && (tree == null || (n.transform.position - me.transform.position).sqrMagnitude < (tree.transform.position - me.transform.position).sqrMagnitude)) tree = n;
            var tp = tree.transform.position;
            var away = (me.transform.position - tp); away.y = 0; away.Normalize();
            float yawToTree = Quaternion.LookRotation(-away).eulerAngles.y;
            pc.LocalTeleport(tp + away * 2f + Vector3.up * 0.1f, yawToTree);
            pc.SetLook(yawToTree, 12f);
            yield return Hold(me, Item.Rock);
            yield return Shot("03_rock_idle");
            pc.DebugSwing(Cfg.RockCooldown);
            yield return new WaitForSeconds(0.05f);
            Snap("04_rock_raised");
            yield return new WaitForSeconds(ViewModel.ImpactTime - 0.05f);
            pc.DebugImpact(true);
            yield return new WaitForSeconds(0.14f);
            Snap("05_rock_hit_bounce");
            yield return new WaitForSeconds(0.8f);
            pc.SetLook(yawToTree + 90f, 12f);
            pc.DebugSwing(Cfg.RockCooldown);
            yield return new WaitForSeconds(ViewModel.ImpactTime);
            pc.DebugImpact(false);
            yield return new WaitForSeconds(0.1f);
            Snap("06_rock_miss_follow");
            yield return new WaitForSeconds(0.8f);

            // 4: bow (Rust style, right hand) + ball
            pc.SetLook(yawToTree + 90f, 4f);
            yield return Hold(me, Item.Bow);
            yield return Shot("07_bow");
            pc.DebugDraw = 1f;
            yield return Shot("08_bow_drawn");
            pc.DebugDraw = -1f;
            while (Ball.Instance == null) yield return null;
            yield return new WaitForSeconds(3f);
            pc.LocalTeleport(Ball.Instance.transform.position + new Vector3(0, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.5f);
            pc.SetLook(0f, 3f);
            yield return Shot("09_ball_in_hands");
            me.ThrowBallRpc(Vector3.forward + Vector3.up * 0.3f);
            yield return new WaitForSeconds(0.25f);
            Snap("10_ball_thrown");
            yield return new WaitForSeconds(0.6f);

            // 5: items dropped on the ground (drag out of the inventory)
            yield return Hold(me, Item.Rock);
            pc.SetLook(0f, 35f);
            for (int i = Cfg.PlayerSlots - 1; i >= 0; i--)
            {
                var st = me.SlotAt(i);
                if (st.Empty || st.Id == Item.Arrow) continue;
                me.DropItemRpc(0, (byte)i, (ushort)Mathf.Min(st.Count, 200), default);
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(0.2f);
            Snap("11_items_tossed");
            yield return Shot("12_items_on_ground");

            // 6: third-person rig demo (walk / sprint / crouch / jump / swing)
            var demoRoot = me.transform.position + new Vector3(0, 0, 6f);
            var demos = new System.Collections.Generic.List<(Transform root, BodyAnimator anim, int mode)>();
            for (int m = 0; m < 5; m++)
            {
                var r = new GameObject("demo" + m).transform;
                r.position = demoRoot + new Vector3((m - 2) * 1.8f, 0, 0);
                r.rotation = Quaternion.Euler(0, 200f, 0);
                var anim = BodyAnimator.TryCreate(r, Cfg.ModelWidth, out _);
                if (anim == null) { Log("FAIL: rigged alien missing"); break; }
                demos.Add((r, anim, m));
            }
            pc.LocalTeleport(me.transform.position, 0f);
            pc.SetLook(0f, 8f);
            float t0 = Time.time;
            while (Time.time - t0 < 1.35f)
            {
                float dt = Time.deltaTime, t = Time.time - t0;
                foreach (var (r, anim, m) in demos)
                {
                    float speed = m == 0 ? 3f : m == 1 ? 7.5f : m == 2 ? 2f : 0f;
                    r.position += r.forward * speed * dt * 0f; // animate in place, but feed the speed below
                    var pose = new BodyAnimator.Pose { Crouch = m == 2, Holding = m == 4, TwoHanded = m == 4, Swing = m == 4 ? Mathf.Clamp01(1f - (t % 0.55f) / 0.55f) : 0f };
                    if (m == 3) r.position = demoRoot + new Vector3((m - 2) * 1.8f, 1.2f, 0);
                    anim.TickWithVelocity(pose, dt, r.forward * speed);
                }
                yield return null;
            }
            Snap("13_rig_walk_sprint_crouch_jump_swing");
            yield return new WaitForSeconds(0.3f);

            // 7: inventory, and crafting at the machine
            pc.MenuOpen = true;
            yield return Shot("14_inventory");
            pc.CloseMenu();
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            pc.MenuOpen = true;
            yield return Shot("15_machine_crafting");
            pc.CloseMenu();

            // 8: airdrop loot in hand
            foreach (var it in new[] { Item.C4, Item.DeathWand, Item.InvisPotion })
            {
                me.ServerGive(it, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return Shot("16_" + it);
            }
            me.ServerGive(Item.Chainsaw, 1, Cfg.ChainsawUses);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Chainsaw);
            yield return Shot("16_Chainsaw");

            // 9: crossbow, building wheel, pause menu
            me.ServerGive(Item.Crossbow, 1, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Crossbow);
            yield return Shot("17_crossbow");
            yield return Hold(me, Item.BuildingPlan);
            pc.WheelOpen = true;
            Hud.WheelOpened();
            yield return Shot("18_build_wheel");
            pc.WheelOpen = false;
            pc.Paused = true;
            yield return Shot("19_pause_menu");
            pc.Paused = false;

            // 10: car, horse and a fort tower
            yield return Hold(me, Item.Rock);
            var spot = me.transform.position;
            me.DevRpc(DevCmd.SpawnCar);
            me.DevRpc(DevCmd.SpawnHorse);
            yield return new WaitForSeconds(1.5f);
            pc.LocalTeleport(spot + me.transform.forward * -3f + Vector3.up * 0.2f, me.transform.eulerAngles.y);
            pc.SetLook(me.transform.eulerAngles.y, 12f);
            yield return Shot("20_car_and_horse");
            me.ServerGive(Item.FortTower, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.FortTower);
            var fwd2 = me.transform.forward;
            me.ThrowFortRpc(me.EyePos, fwd2 * 8f);
            yield return new WaitForSeconds(0.1f);
            me.FortLandRpc(spot + Quaternion.Euler(0, 50, 0) * fwd2 * 9f + Vector3.up);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("21_fort_tower");

            // 11: new airdrop items, the airstrike map, then the sudden death stadium
            foreach (var it in new[] { Item.Sniper, Item.PortalGun, Item.RocketLauncher, Item.GiantStaff })
            {
                me.ServerGive(it, 1, Mathf.Clamp(Cfg.MaxData(it), 0, 255));
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return Shot("22_" + it);
            }
            me.ServerGive(Item.Airstrike, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Airstrike);
            pc.OpenAirstrikeMap();
            yield return Shot("23_airstrike_map");
            pc.CloseAirstrikeMap();
            NetGame.Instance.DevSetTimeLeft(9.5f);
            yield return new WaitForSeconds(2f);
            Snap("24_last_seconds_countdown");
            while (NetGame.Instance.S != GameState.SuddenDeath) yield return null;
            yield return new WaitForSeconds(1.5f);
            Snap("25_stadium_countdown");
            yield return new WaitForSeconds(4.2f);
            Snap("26_stadium_fight");
            pc.SetLook(0f, -8f);
            yield return new WaitForSeconds(1.5f);
            Snap("27_stadium_crowd");
            yield return new WaitForSeconds(0.5f);
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

        /// <summary>The n-th cell in the base (centre outwards) where a foundation + walls on +x/+z fit (the UFO is random).</summary>
        static void FreeCell(int team, int n, out int ci, out int cj)
        {
            var c = Cfg.BaseCenter[team];
            int i0 = BuildGrid.CellOf(c.x + 0.1f), j0 = BuildGrid.CellOf(c.z + 0.1f);
            for (int r = 0; r < 6; r++)
            for (int di = -r; di <= r; di++)
            for (int dj = -r; dj <= r; dj++)
            {
                if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue;
                int i = i0 + di, j = j0 + dj;
                bool ok = true;
                for (int a = -1; a <= 2 && ok; a++)
                for (int b = -1; b <= 2 && ok; b++)
                    ok = Cfg.CellInBase(team, i + a, j + b) && !Cfg.CellBlocked(i + a, j + b);
                if (ok && n-- == 0) { ci = i; cj = j; return; }
            }
            ci = i0; cj = j0;
        }

        static int FindWorldItem(Item id)
        {
            foreach (var it in NetGame.Instance.Items) if (it.Stack.Id == id) return it.Id;
            return -1;
        }

        static void Check(bool ok, string what) => Log((ok ? "PASS: " : "FAIL: ") + what);
    }
}
