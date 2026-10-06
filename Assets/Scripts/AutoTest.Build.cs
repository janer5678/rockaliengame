using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest buildbias -host -solo: building in a line. The ghost leans towards slots that carry on your last two
    /// pieces (BuildGrid.PreferRecent / PlayerController.PreferRecentSlot) when the aim is near one, but aiming clearly
    /// somewhere else still goes there. First the rule on its own (made-up aim points), then for real: two foundations
    /// in a row and the ghost carrying the row on from an aim just over the line into the next row, then two walls in a
    /// row and the ghost carrying the wall line on from an aim at the middle of the next foundation.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator BuildBiasRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            g.TimerPaused.Value = true;
            BuildBiasRules();
            yield return BuildBiasLive(me, pc, team);
            Log("buildbias test done");
            g.EndGame(team, "buildbias test done");
        }

        static PieceKey Fnd(int i, int j) => new PieceKey(PieceKey.KFoundation, i, j, 0, 0);
        static PieceKey Edge(int i, int j, int d, int l = 0) => new PieceKey(PieceKey.KEdge, i, j, l, d);

        /// <summary>The rule on its own: a point on the ground (foundation top) at (x, z) in cells.</summary>
        static void BuildBiasRules()
        {
            float C = Cfg.Cell, top = BuildGrid.LevelY(0);
            System.Func<float, float, System.Func<PieceKey, (bool, Vector3)>> at = (x, z) => k => (true, new Vector3(x * C, top, z * C));
            System.Func<PieceKey, bool> any = k => true;
            var fRow = new List<PieceKey> { Fnd(0, 0), Fnd(1, 0) };
            // aiming 0.4 m over the line into the next row: carries the row on
            bool b = BuildGrid.PreferRecent(PieceType.Foundation, fRow, Fnd(2, 1), true, at(2.5f, 1f + 0.4f / C), any, out var k1);
            Check(b && k1.Equals(Fnd(2, 0)), $"rule: a foundation aimed just over the line carries the row on ({(b ? k1.ToString() : "kept " + Fnd(2, 1))})");
            // aiming at the middle of the next row's cell: that cell
            b = BuildGrid.PreferRecent(PieceType.Foundation, fRow, Fnd(2, 1), true, at(2.5f, 1.5f), any, out k1);
            Check(!b, $"rule: aiming at the middle of another cell still goes there ({(b ? k1.ToString() : "kept")})");
            // nothing near: nothing changes
            b = BuildGrid.PreferRecent(PieceType.Foundation, fRow, Fnd(6, 6), true, at(6.5f, 6.5f), any, out k1);
            Check(!b, "rule: aiming far from your last pieces is left alone");
            // only the last two count
            b = BuildGrid.PreferRecent(PieceType.Foundation, new List<PieceKey> { Fnd(1, 0) }, Fnd(-1, 1), true, at(-0.5f, 1f + 0.3f / C), any, out k1);
            Check(!b, $"rule: a piece that isn't one of your last two isn't carried on ({(b ? k1.ToString() : "kept")})");
            // a slot that can't be built isn't taken
            b = BuildGrid.PreferRecent(PieceType.Foundation, fRow, Fnd(2, 1), true, at(2.5f, 1f + 0.4f / C), kk => !kk.Equals(Fnd(2, 0)), out k1);
            Check(!b || !k1.Equals(Fnd(2, 0)), "rule: a slot you can't build in isn't picked");
            // walls along z = 1 (the +z sides of cells (0,0), (1,0)): from the middle of cell (2,0) (a little towards -z) the
            // line carries straight on, not round the corner and not the nearer far side
            var wRow = new List<PieceKey> { Edge(0, 0, 1), Edge(1, 0, 1) };
            b = BuildGrid.PreferRecent(PieceType.Wall, wRow, Edge(2, -1, 1), true, at(2.5f, 0.5f - 0.1f / C), any, out var k2, default, _ => true);
            Check(b && k2.Equals(Edge(2, 0, 1)), $"rule: a wall aimed at the middle of the next cell carries the wall line straight on ({(b ? k2.ToString() : "kept " + Edge(2, -1, 1))})");
            // aimed right by the far side: the far side
            b = BuildGrid.PreferRecent(PieceType.Wall, wRow, Edge(2, -1, 1), true, at(2.5f, 0.1f), any, out k2, default, _ => true);
            Check(!b, $"rule: a wall aimed clearly at another edge goes there ({(b ? k2.ToString() : "kept")})");
            // one wall just built (along z = 1, the +z side of cell (0,0)) and you facing it (looking +z): aimed at the floor
            // in front of the line, a bit past its end, the next wall carries the line on in the same rotation - not the
            // turned wall round the corner that's a little nearer the aim
            var w1 = new List<PieceKey> { Edge(0, 0, 1) };
            b = BuildGrid.PreferRecent(PieceType.Wall, w1, Edge(0, 0, 0), true, at(1.35f, 0.45f), any, out k2, Vector3.forward, _ => true); // (held up: foundations under it)
            Check(b && k2.Equals(Edge(1, 0, 1)), $"rule: after a wall, facing the line, the next one carries it on in the same rotation ({(b ? k2.ToString() : "kept the turned " + Edge(0, 0, 0))})");
            // ...but not when nothing would hold it up (no foundation, no wall under it): then it isn't pushed first
            b = BuildGrid.PreferRecent(PieceType.Wall, w1, Edge(0, 0, 0), true, at(1.35f, 0.45f), any, out k2, Vector3.forward, _ => false);
            Check(!(b && k2.Equals(Edge(1, 0, 1))), $"rule: a wall in line with nothing under it isn't pushed first ({(b ? k2.ToString() : "kept")})");
            // ...but aimed right at the turned wall's spot, the turned one
            b = BuildGrid.PreferRecent(PieceType.Wall, w1, Edge(0, 0, 0), true, at(1f, 0.3f), any, out k2, Vector3.forward, _ => true);
            Check(!b, $"rule: a wall aimed right at the corner's turned spot still goes there ({(b ? k2.ToString() : "kept")})");
            // a floor next to the last floor
            var flRow = new List<PieceKey> { new PieceKey(PieceKey.KFloor, 0, 0, 1, 0), new PieceKey(PieceKey.KFloor, 1, 0, 1, 0) };
            float y1 = BuildGrid.LevelY(1);
            b = BuildGrid.PreferRecent(PieceType.Floor, flRow, new PieceKey(PieceKey.KFloor, 2, 1, 1, 0), true, kk => (true, new Vector3(2.5f * C, y1, C + 0.3f)), any, out var k3);
            Check(b && k3.Equals(new PieceKey(PieceKey.KFloor, 2, 0, 1, 0)), $"rule: floors carry the row on too ({(b ? k3.ToString() : "kept")})");
        }

        IEnumerator BuildBiasLive(PlayerNet me, PlayerController pc, int team)
        {
            for (int n = 0; n < 4; n++) me.ServerGive(Cfg.CurrencyItem, 250);
            me.ServerGive(Item.BuildingPlan, 1);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            float C = Cfg.Cell;
            // two foundations in a row (along +x), put down by this player
            pc.LocalTeleport(OnGround(BuildGrid.CellCenter(ci + 1, cj + 2), 0.1f), 180f);
            yield return new WaitForSeconds(0.4f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(0.8f);
            me.PlaceRpc((byte)PieceType.Foundation, ci + 1, cj, 0, 0);
            yield return new WaitForSeconds(0.8f);
            Check(BuildGrid.Registry.ContainsKey(Fnd(ci, cj)) && BuildGrid.Registry.ContainsKey(Fnd(ci + 1, cj)), "built two foundations in a row");
            pc.NoteBuilt(Fnd(ci, cj));
            pc.NoteBuilt(Fnd(ci + 1, cj));
            pc.BuildPiece = PieceType.Foundation;
            pc.LocalTeleport(OnGround(BuildGrid.CellCenter(ci + 2, cj + 2) + new Vector3(0f, 0f, 0.5f), 0.1f), 180f);
            yield return new WaitForSeconds(0.4f);

            // just over the line into the next row: the row carries on
            var near = new Vector3((ci + 2.5f) * C, 0f, (cj + 1) * C + 0.4f);
            yield return AimAtGround(me, pc, near);
            Check(pc.GhostOk && pc.GhostKey.Equals(Fnd(ci + 2, cj)) && pc.GhostFromRecent,
                $"live: a foundation aimed 0.4 m over the line carries the row on ({pc.GhostKey}, want {Fnd(ci + 2, cj)}, ok {pc.GhostOk})");
            yield return Snap("buildbias_foundation_row");
            // the middle of the cell next to it: that cell
            var mid = new Vector3((ci + 2.5f) * C, 0f, (cj + 1.5f) * C);
            yield return AimAtGround(me, pc, mid);
            Check(pc.GhostOk && pc.GhostKey.Equals(Fnd(ci + 2, cj + 1)) && !pc.GhostFromRecent,
                $"live: aiming at the middle of another cell still puts it there ({pc.GhostKey}, want {Fnd(ci + 2, cj + 1)})");
            // with no recent pieces the old rule: the cell under the aim
            var keep = new List<PieceKey>(pc.RecentPieces);
            pc.NoteBuilt(Fnd(ci + 40, cj + 40)); pc.NoteBuilt(Fnd(ci + 41, cj + 40)); // (pieces that aren't there don't count)
            yield return AimAtGround(me, pc, near);
            Check(pc.GhostKey.Equals(Fnd(ci + 2, cj + 1)) && !pc.GhostFromRecent, $"live: without recent pieces it's the cell under the aim ({pc.GhostKey})");
            foreach (var k in keep) pc.NoteBuilt(k);

            // walls: a third foundation (not one of "yours"), then two walls in a row along the +z sides of the first two
            me.PlaceRpc((byte)PieceType.Foundation, ci + 2, cj, 0, 0);
            yield return new WaitForSeconds(0.8f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(0.8f);
            me.PlaceRpc((byte)PieceType.Wall, ci + 1, cj, 0, 1);
            yield return new WaitForSeconds(0.8f);
            Check(BuildGrid.Registry.ContainsKey(Edge(ci, cj, 1)) && BuildGrid.Registry.ContainsKey(Edge(ci + 1, cj, 1)), "built two walls in a row");
            pc.NoteBuilt(Edge(ci, cj, 1));
            pc.NoteBuilt(Edge(ci + 1, cj, 1));
            pc.BuildPiece = PieceType.Wall;
            // from the open -z side, looking at the middle of the third foundation
            pc.LocalTeleport(OnGround(BuildGrid.CellCenter(ci + 2, cj - 1) + new Vector3(0f, 0f, -0.3f), 0.1f), 0f);
            yield return new WaitForSeconds(0.4f);
            var fmid = new Vector3((ci + 2.5f) * C, BuildGrid.LevelY(0), (cj + 0.5f) * C - 0.1f);
            yield return AimAt(me, pc, fmid);
            Check(pc.GhostOk && pc.GhostKey.Equals(Edge(ci + 2, cj, 1)) && pc.GhostFromRecent,
                $"live: a wall aimed at the middle of the next foundation carries the wall line on ({pc.GhostKey}, want {Edge(ci + 2, cj, 1)}, ok {pc.GhostOk})");
            yield return Snap("buildbias_wall_line");
            // aimed right by the near side: the near side
            var fnear = new Vector3((ci + 2.5f) * C, BuildGrid.LevelY(0), cj * C + 0.3f);
            yield return AimAt(me, pc, fnear);
            Check(pc.GhostOk && pc.GhostKey.Equals(Edge(ci + 2, cj - 1, 1)) && !pc.GhostFromRecent,
                $"live: a wall aimed clearly at another edge goes there ({pc.GhostKey}, want {Edge(ci + 2, cj - 1, 1)})");
            // clicking for real remembers the piece: the newest of the last two is now what was just put down
            yield return AimAt(me, pc, fmid);
            Binds.TestHold(Bind.Attack, true);
            yield return null;
            yield return null;
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(0.8f);
            Check(BuildGrid.Registry.ContainsKey(Edge(ci + 2, cj, 1)) && pc.RecentPieces.Count == 2 && pc.RecentPieces[1].Equals(Edge(ci + 2, cj, 1)),
                $"live: clicking builds it there and it becomes the newest of your last two ({(pc.RecentPieces.Count > 0 ? pc.RecentPieces[pc.RecentPieces.Count - 1].ToString() : "none")})");

            // Demolish is back on the wheel, straight down at the bottom (no X key): picked, only the piece in the crosshair
            // lights up red, and LMB takes it down. (Wall sits on the right of the wheel.)
            int wallSlice = System.Array.FindIndex(PlayerController.WheelOptions, o => o.Piece == PieceType.Wall && !o.Demolish);
            Check(wallSlice >= 0 && Mathf.Abs(Mathf.DeltaAngle(PlayerController.WheelAngle(wallSlice), 90f)) < 30f,
                $"the Wall slice is on the right of the wheel ({(wallSlice >= 0 ? PlayerController.WheelAngle(wallSlice) : -1f):0} deg)");
            int demo = System.Array.FindIndex(PlayerController.WheelOptions, o => o.Demolish);
            Check(demo >= 0 && Mathf.Abs(Mathf.DeltaAngle(PlayerController.WheelAngle(demo), 180f)) < 0.5f && Binds.Get(Bind.Demolish) == KeyCode.None,
                $"the wheel has Demolish straight down at the bottom ({(demo >= 0 ? PlayerController.WheelAngle(demo) : -1f):0} deg), and there's no demolish key");
            var target = Edge(ci + 2, cj, 1);
            BuildGrid.Registry.TryGetValue(target, out var wall);
            pc.SelectWheel(demo);
            yield return AimAt(me, pc, BuildGrid.PieceCenter(target));
            yield return new WaitForSeconds(0.35f);
            int lit = pc.DemolishLitCount;
            bool aimedOk = wall != null && pc.DemolishAimed == wall.NetworkObject;
            yield return Snap("buildbias_demolish_highlight");
            Check(pc.DemolishMode && lit == 1 && aimedOk, $"Demolish on the wheel: only the wall in the crosshair lights up red ({lit} lit, the aimed one: {aimedOk})");
            Binds.TestHold(Bind.Attack, true);
            yield return null;
            yield return null;
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(0.8f);
            Check(!BuildGrid.Registry.ContainsKey(target), "LMB with Demolish picked takes the wall down");
            pc.SelectWheel(0);
            yield return null;
            Check(pc.DemolishLitCount == 0, $"picking a piece again takes the red off ({pc.DemolishLitCount})");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator AimAtGround(PlayerNet me, PlayerController pc, Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            else p.y = MapBuilder.Height(p.x, p.z);
            yield return AimAt(me, pc, p);
        }

        IEnumerator AimAt(PlayerNet me, PlayerController pc, Vector3 p)
        {
            for (int i = 0; i < 3; i++)
            {
                var look = Quaternion.LookRotation(p - pc.CenterRay().origin).eulerAngles;
                pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
                yield return null;
            }
            yield return new WaitForSeconds(0.15f);
        }
    }
}
