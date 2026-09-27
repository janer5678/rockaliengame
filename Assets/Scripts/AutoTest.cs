using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Headless end-to-end test driver. Inactive unless launched with "-autotest ball" or "-autotest sd".
    /// Drives real RPCs through the network: gather -> craft -> place table -> build -> (ball capture | sudden death kill).
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

        IEnumerator ClientRoutine(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];

            // ---- gather ----
            yield return Gather(me, pc, ResourceNode.Tree, 52, baseC);
            yield return Gather(me, pc, ResourceNode.Boulder, 6, baseC);
            Log($"gathered wood={me.Wood.Value} stone={me.Stone.Value}");
            Check(me.Wood.Value >= 180, "gathered enough wood");

            // ---- craft + place crafting table ----
            me.CraftRpc((int)Cfg.R.CraftingTable);
            yield return new WaitForSeconds(0.6f);
            Check(me.Tables.Value == 1, "crafted crafting table");
            Vector3 stand = baseC + new Vector3(-4, 0.1f, team == 1 ? -3 : 3);
            pc.LocalTeleport(stand, 0);
            me.Held.Value = (byte)Item.CraftingTable;
            yield return new WaitForSeconds(0.4f);
            me.PlaceTableRpc(baseC + new Vector3(-6, 0, team == 1 ? 1 : -1), 0);
            yield return new WaitForSeconds(0.6f);
            Check(CountStructures(PieceType.CraftingTable, team) == 1, "crafting table placed");

            // ---- craft building plan & build ----
            me.CraftRpc((int)Cfg.R.BuildingPlan);
            yield return new WaitForSeconds(0.6f);
            Check(me.Owns(Item.BuildingPlan), "crafted building plan");
            me.Held.Value = (byte)Item.BuildingPlan;
            yield return new WaitForSeconds(0.4f);
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
            Log($"after building wood={me.Wood.Value}");

            if (m_Mode != "ball") yield break;

            // ---- ball capture ----
            while (Ball.Instance == null) yield return null;
            yield return new WaitForSeconds(4f);
            var ball = Ball.Instance;
            Log($"ball landed at {ball.transform.position}");
            pc.LocalTeleport(new Vector3(ball.transform.position.x + 1.5f, 0.1f, ball.transform.position.z), 0);
            me.Held.Value = (byte)Item.Rock;
            yield return new WaitForSeconds(0.5f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.6f);
            Check(me.CarryingBall, "picked up ball");
            pc.LocalTeleport(baseC + new Vector3(6, 0.1f, 5), 0);
            yield return new WaitForSeconds(0.5f);
            me.DropBallRpc(Vector3.zero);
            yield return new WaitForSeconds(3f);
            Check(!me.CarryingBall && ball.BaseTeam.Value == team, $"ball resting in own base (BaseTeam={ball.BaseTeam.Value})");
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
        }

        /// <summary>Host-only (server may write its own resources directly): craft everything, stone upgrade, door, bow, ram raid.</summary>
        IEnumerator HostRaidTest(PlayerNet me, PlayerController pc)
        {
            while (NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) yield return null;
            int team = me.Team.Value;
            Vector3 baseC = Cfg.BaseCenter[team];
            me.Wood.Value = 5000;
            me.Stone.Value = 2000;

            pc.LocalTeleport(baseC + new Vector3(-4, 0.1f, 3), 0);
            me.CraftRpc((int)Cfg.R.CraftingTable);
            yield return new WaitForSeconds(0.4f);
            me.Held.Value = (byte)Item.CraftingTable;
            yield return new WaitForSeconds(0.4f);
            me.PlaceTableRpc(baseC + new Vector3(-6, 0, -1), 0);
            yield return new WaitForSeconds(0.5f);
            foreach (var r in new[] { Cfg.R.BuildingPlan, Cfg.R.Hatchet, Cfg.R.Pickaxe, Cfg.R.Spear, Cfg.R.Bow, Cfg.R.Arrows, Cfg.R.Ram })
            {
                me.CraftRpc((int)r);
                yield return new WaitForSeconds(0.3f);
            }
            Check(me.Owns(Item.BuildingPlan) && me.Owns(Item.Hatchet) && me.Owns(Item.Pickaxe) && me.Owns(Item.Spear) && me.Owns(Item.Bow)
                  && me.Arrows.Value == Cfg.ArrowsPerCraft && me.Rams.Value == 1, "host crafted all tools, arrows and a ram");

            // build + upgrade + door
            me.Held.Value = (byte)Item.BuildingPlan;
            yield return new WaitForSeconds(0.4f);
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

            // bow hit on the opponent
            PlayerNet other = null;
            while (other == null)
            {
                foreach (var p in PlayerNet.All) if (p != me) other = p;
                yield return null;
            }
            me.Held.Value = (byte)Item.Bow;
            yield return new WaitForSeconds(0.4f);
            float hpBefore = other.Health.Value;
            me.FireArrowRpc(me.EyePos, Vector3.forward * Cfg.ArrowSpeed);
            yield return new WaitForSeconds(0.1f);
            me.ArrowHitRpc(other.NetworkObject, other.transform.position + Vector3.up, false);
            yield return new WaitForSeconds(0.5f);
            Check(other.Health.Value < hpBefore - 40f && me.Arrows.Value == Cfg.ArrowsPerCraft - 1, $"arrow hit opponent (hp {hpBefore:0} -> {other.Health.Value:0})");

            // ram raid on the opponent's wall
            float waitUntil = Time.time + 60f;
            Structure enemyWall = null;
            while (enemyWall == null && Time.time < waitUntil)
            {
                foreach (var s in Structure.All) if (s.Team.Value != team && s.PType == PieceType.Wall) enemyWall = s;
                yield return new WaitForSeconds(0.5f);
            }
            Check(enemyWall != null, "opponent built a wall to raid");
            if (enemyWall == null) yield break;
            var wp = enemyWall.transform.position;
            me.Held.Value = (byte)Item.Ram;
            pc.LocalTeleport(new Vector3(wp.x, 0.1f, wp.z + 9f), 180f);
            yield return new WaitForSeconds(0.5f);
            me.PlaceRamRpc(new Vector3(wp.x, 0f, wp.z + 5f), 180f);
            yield return new WaitForSeconds(0.6f);
            Check(Ram.All.Count == 1 && me.Rams.Value == 0, "ram placed");
            if (Ram.All.Count == 0) yield break;
            var ram = Ram.All[0];
            float ramStart = ram.transform.position.z;
            float end = Time.time + 25f;
            while (enemyWall != null && enemyWall.IsSpawned && Time.time < end)
            {
                pc.LocalTeleport(ram.transform.position + new Vector3(0, 0.1f, 2.6f), 180f);
                me.PushRamRpc(ram.NetworkObject, Vector3.back);
                yield return new WaitForSeconds(0.1f);
            }
            Log($"ram moved {ramStart - ram.transform.position.z:0.0}m, strikes={ram.Strikes.Value}");
            Check(enemyWall == null || !enemyWall.IsSpawned, "battering ram destroyed the enemy wall");
            pc.LocalTeleport(baseC + new Vector3(-4, 0.1f, 3), 0);
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
            me.Wood.Value = 9000;
            me.Stone.Value = 3000;
            pc.LocalTeleport(new Vector3(0, 0.1f, -81f), 0);
            me.CraftRpc((int)Cfg.R.CraftingTable);
            yield return new WaitForSeconds(0.3f);
            me.Held.Value = (byte)Item.CraftingTable;
            yield return new WaitForSeconds(0.3f);
            me.PlaceTableRpc(new Vector3(-3, 0, -83), 20);
            yield return new WaitForSeconds(0.3f);
            foreach (var r in new[] { Cfg.R.BuildingPlan, Cfg.R.Bow, Cfg.R.Arrows, Cfg.R.Ram, Cfg.R.Spear })
            {
                me.CraftRpc((int)r);
                yield return new WaitForSeconds(0.2f);
            }
            me.Held.Value = (byte)Item.BuildingPlan;
            yield return new WaitForSeconds(0.3f);

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

            // 2: ram + crafting table
            me.Held.Value = (byte)Item.Ram;
            yield return new WaitForSeconds(0.3f);
            pc.LocalTeleport(new Vector3(10, 0.1f, -92f), 0);
            me.PlaceRamRpc(new Vector3(10, 0, -86f), -30f);
            yield return new WaitForSeconds(0.5f);
            me.Held.Value = (byte)Item.Spear;
            pc.LocalTeleport(new Vector3(14, 0.1f, -92f), 0);
            pc.SetLook(-40f, 12f);
            yield return Shot("02_ram_and_spear");

            // 3: ball
            while (Ball.Instance == null) yield return null;
            yield return new WaitForSeconds(4f);
            me.Held.Value = (byte)Item.Bow;
            pc.LocalTeleport(new Vector3(0, 0.1f, -7f), 0);
            pc.SetLook(0f, 15f);
            yield return Shot("03_ball_and_bow");

            // 4: beacon
            pc.LocalTeleport(new Vector3(0, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.5f);
            pc.LocalTeleport(new Vector3(8, 0.1f, -70f), 0);
            yield return new WaitForSeconds(0.4f);
            me.DropBallRpc(Vector3.zero);
            yield return new WaitForSeconds(3f);
            pc.LocalTeleport(new Vector3(20, 0.1f, -30f), 200f);
            pc.SetLook(200f, -12f);
            yield return Shot("04_beacon");

            // 5: crafting menu
            pc.MenuOpen = true;
            yield return Shot("05_crafting_menu");
            pc.MenuOpen = false;

            // 6: resource nodes
            pc.LocalTeleport(new Vector3(-30, 0.1f, -40f), 0);
            pc.SetLook(60f, 5f);
            me.Held.Value = (byte)Item.Rock;
            yield return Shot("06_nodes");
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

        static void Check(bool ok, string what) => Log((ok ? "PASS: " : "FAIL: ") + what);
    }
}
