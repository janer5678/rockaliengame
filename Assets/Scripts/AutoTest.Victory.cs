using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest victory -host -solo -fast -rules classic -shotdir DIR (windowed, for the pictures):
    /// - airdrops: the ship never pops out of existence - a ship that's flying off keeps shrinking away even when its lane
    ///   starts the next drop at once (two in the sky), it stays low (in from at most ArriveUp over its hover point, off
    ///   under the clouds), it shines down out of its belly (a spotlight and a glow), and its beam is bright
    ///   (victory_airdrop_beam, victory_airdrop_far, victory_airdrop_leaving);
    /// - the ball's beacon is bright (victory_ball_beacon_far);
    /// - the win: we take the ball, put it in our socket, walk out into the wild and the timer runs out - the game is
    ///   over, the victory cutscene plays (we're sent home, a UFO comes, its beam takes us up, it flies off; a winner who
    ///   isn't there any more is skipped), nothing can be done meanwhile (no cursor, no pause, the camera is the
    ///   cutscene's), the victory screen doesn't show until it's over and then does (victory_cut_*, victory_screen).
    /// Running `-autotest ball` (host + client) also checks it: the client wins, both see the cutscene, then the screen.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator VictoryRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            g.TimerPaused.Value = true;
            yield return VictoryAirdropChecks(me, pc, g, team);
            yield return VictoryBeaconCheck(me, pc, g);

            // ---------------- the win, and the cutscene ----------------
            g.TimerPaused.Value = false;
            g.DevSkipPhase(GameState.PreBall);
            while (g.S != GameState.BallLive) yield return null;
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(4f);
            var ball = Ball.Instance;
            var bp = ball.transform.position;
            pc.LocalTeleport(new Vector3(bp.x + 1.5f, MapBuilder.Height(bp.x + 1.5f, bp.z) + 0.1f, bp.z), 270f);
            yield return new WaitForSeconds(0.5f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.6f);
            Check(me.CarryingBall, "victory: picked up the ball");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team) + 180f);
            yield return new WaitForSeconds(0.5f);
            me.ThrowBallRpc(me.EyePos, (Cfg.SocketPos(team) - me.EyePos).normalized, Vector3.zero);
            yield return new WaitForSeconds(2f);
            Check(ball.SocketTeam.Value == team, $"victory: the ball is in our machine's socket ({ball.SocketTeam.Value})");
            // out in the wild when the time runs out: the cutscene has to bring us home
            NetGame.WildSpawnPoint(team, out var wild, out var wy);
            pc.LocalTeleport(wild, wy);
            yield return new WaitForSeconds(0.6f);
            // who it beams up: everyone on the team who's alive and here (here: a winner who's dead or gone is skipped)
            var riders = VictoryCutscene.PickRiders(team, PlayerNet.All);
            var none = VictoryCutscene.PickRiders(team, new List<PlayerNet> { null });
            Check(riders.Count == 1 && riders[0] == me && none.Count == 0, $"victory: the riders are the winners who are alive and here ({riders.Count})");
            g.TimerPaused.Value = false;
            g.DevSetTimeLeft(1.5f);
            while (g.S != GameState.GameOver) yield return null;
            float t0 = Time.time;
            float screenBefore = Hud.GameOverShownAt;
            Check(g.Winner.Value == team && g.CutsceneAt.Value >= 0 && VictoryCutscene.Active && g.CutsceneRiders.Count == 1 && g.CutsceneRiders[0] == me.NetworkObjectId,
                $"victory: we win and the cutscene starts for the winners ({g.CutsceneRiders.Count} riders, winner {g.Winner.Value})");
            // a winner who left mid-cutscene: their id is still on the list but nobody's there - it just skips them
            g.CutsceneRiders.Add(987654321UL);
            bool early = false, lockedAll = true, camAll = true, homeOk = false, liftSeen = false, goneSeen = false;
            float lastE = 0f, maxLift = 0f, maxShip = 0f;
            int taken = 0;
            float hoverY = 0f;
            var shots = new Queue<(float at, string name)>(new[]
            {
                (1.9f, "victory_cut_arriving"), (VictoryCutscene.BeamOn + 0.8f, "victory_cut_beam"), (VictoryCutscene.LiftStart + 1.7f, "victory_cut_lift"),
                (VictoryCutscene.BeamOff - 0.3f, "victory_cut_taken"), (VictoryCutscene.LeaveStart + 1.6f, "victory_cut_leaving"),
            });
            var body = me.transform.Find("body");
            while (VictoryCutscene.Active)
            {
                float e = VictoryCutscene.Elapsed;
                lastE = e;
                taken = Mathf.Max(taken, VictoryCutscene.Taken);
                if (Hud.GameOverShownAt != screenBefore) early = true;
                // locked: no free cursor, no pause, the camera's the cutscene's (away from our eyes)
                lockedAll &= Cursor.lockState == CursorLockMode.Locked && !pc.Paused && !pc.MenuOpen;
                var cam = Camera.main.transform;
                if (e > 0.3f && VictoryCutscene.CameraPose(out var cp, out _, out _)) camAll &= Vector3.Distance(cam.position, cp) < 0.5f && Vector3.Distance(cam.position, me.EyePos) > 6f;
                if (e > 1.2f && e < VictoryCutscene.LiftStart) homeOk |= Vector3.Distance(me.transform.position, Cfg.SpawnPos(team, me.Slot.Value)) < 1.6f;
                if (body != null && body.gameObject.activeSelf) maxLift = Mathf.Max(maxLift, body.localPosition.y);
                liftSeen |= VictoryCutscene.LiftOf(0) > 0.3f && VictoryCutscene.LiftOf(0) < 0.9f && body != null && body.localPosition.y > 2f;
                goneSeen |= VictoryCutscene.LiftOf(0) >= 1f && body != null && !body.gameObject.activeSelf;
                if (VictoryCutscene.Ship != null) maxShip = Mathf.Max(maxShip, VictoryCutscene.Ship.position.y);
                if (VictoryCutscene.Ship != null) hoverY = VictoryCutscene.HoverPoint.y;
                if (shots.Count > 0 && e >= shots.Peek().at)
                {
                    var s = shots.Dequeue();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), s.name + ".png"));
                    Log($"shot {s.name} (e {e:0.0}, lift {VictoryCutscene.LiftOf(0):0.00}, ship {(VictoryCutscene.Ship != null ? VictoryCutscene.Ship.position : Vector3.zero)}, scale {VictoryCutscene.ShipScale:0.00}, beam {VictoryCutscene.BeamIntensity:0.0})");
                    if (s.name == "victory_cut_beam") Check(VictoryCutscene.BeamIntensity > 0.6f, $"victory: the UFO's beam is on ({VictoryCutscene.BeamIntensity:0.0})");
                    if (s.name == "victory_cut_leaving") Check(VictoryCutscene.ShipScale > 0.02f && VictoryCutscene.ShipScale < 0.95f, $"victory: the UFO flies off shrinking ({VictoryCutscene.ShipScale:0.00})");
                }
                yield return null;
            }
            Check(lastE > VictoryCutscene.Length - 0.5f, $"victory: the cutscene ran its whole length ({lastE:0.0} of {VictoryCutscene.Length:0.0} s, {Time.time - t0:0.0} s)");
            Check(homeOk, "victory: the winner out in the wild was sent home under the UFO");
            Check(liftSeen && goneSeen && taken == 1, $"victory: the winner floats up the beam (up to {maxLift:0.0} m) and is taken into the UFO; the one who left is skipped ({taken} taken)");
            Check(lockedAll && camAll, $"victory: everything locked meanwhile (cursor, pause, menus: {lockedAll}) and the camera is the cutscene's ({camAll})");
            Check(maxShip <= hoverY + AirdropShip.ArriveUp + 1f, $"victory: the UFO stays low in the sky (highest {maxShip:0} m, hovering at {hoverY:0} m)");
            Check(!early, "victory: the victory screen waited for the cutscene");
            yield return new WaitForSeconds(1.2f);
            Check(Hud.GameOverShownAt > t0 + VictoryCutscene.Length - 0.5f && !VictoryCutscene.Active && VictoryCutscene.Ship == null,
                $"victory: then the victory screen comes up ({Hud.GameOverShownAt - t0:0.0} s after the end) and the UFO is gone");
            Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, "victory: the cursor is free on the victory screen");
            yield return Snap("victory_screen");
            Log("victory test done");
            Application.Quit(0);
        }

        /// <summary>The airdrop ship: never pops out, stays low, shines down, bright beam.</summary>
        IEnumerator VictoryAirdropChecks(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            foreach (var c in new List<Container>(Container.All)) if (c != null && c.IsAirdrop && c.IsSpawned) c.NetworkObject.Despawn(true);
            g.DevSpawnAirdrop();
            double st = g.LaneStartAt(0);
            var gp = g.LanePosAt(0);
            double E() => g.NetworkManager.ServerTime.Time - st;
            var hover = AirdropShip.HoverAt(gp);
            var toMid = new Vector3(-gp.x, 0, -gp.z);
            if (toMid.sqrMagnitude < 1f) toMid = Vector3.forward;
            var eye = gp + toMid.normalized * 40f;
            eye.y = MapBuilder.Height(eye.x, eye.z) + 0.1f;
            float yaw = Quaternion.LookRotation(-toMid).eulerAngles.y;
            pc.LocalTeleport(eye, yaw);
            pc.SetLook(yaw, -30f);
            float highest = 0f;
            while (E() < 0.15) yield return null;
            var first = AirdropShip.ShipTransform;
            float startY = first != null ? first.position.y : 0f;
            Check(first != null && startY <= hover.y + AirdropShip.ArriveUp + 1f, $"airdrop: the ship comes in low ({startY - hover.y:0} m over its hover point, at {startY:0} m)");
            while (E() < NetGame.DropArrive + 2.5) yield return null;
            var spot = AirdropShip.BellySpot;
            Check(spot != null && spot.type == LightType.Spot && spot.enabled && spot.intensity > 100f && spot.transform.forward.y < -0.95f,
                $"airdrop: a spotlight shines down out of the ship's belly ({(spot != null ? spot.intensity : 0f):0})");
            Check(AirdropShip.BeamIntensity > 1f && BeamFx.Real, $"airdrop: the beam is bright ({AirdropShip.BeamIntensity:0.0}, beam shader {BeamFx.Real})");
            yield return Snap("victory_airdrop_beam");
            // from far away too
            var far = gp + toMid.normalized * 140f;
            far.y = MapBuilder.Height(far.x, far.z) + 0.1f;
            pc.LocalTeleport(far, yaw);
            pc.SetLook(yaw, -12f);
            yield return Snap("victory_airdrop_far");
            while (E() < AirdropShip.LeaveStart + 1.0) yield return null;
            // the next drop starts in the same lane while this ship's flying off: it must keep going, not vanish
            var old = AirdropShip.ShipTransform;
            float oldScale = old != null ? old.localScale.x : -1f;
            g.DevSpawnAirdrop();
            yield return null;
            yield return null;
            var ships = AirdropShip.Ships;
            Check(old != null && ships.Contains(old) && AirdropShip.Flying == 2 && g.LaneStartAt(0) > st,
                $"airdrop: the next drop starting in the same lane doesn't tear down the ship that's leaving ({AirdropShip.Flying} ships in the sky)");
            yield return Snap("victory_airdrop_leaving");
            yield return new WaitForSeconds(1.5f);
            float later = old != null ? old.localScale.x : -1f;
            Check(old != null && later < oldScale && later > 0.001f, $"airdrop: and it keeps shrinking away smoothly ({oldScale:0.00} -> {later:0.00})");
            highest = old != null ? old.position.y : 0f;
            while (E() < AirdropShip.Gone - 0.05) { if (old != null) highest = Mathf.Max(highest, old.position.y); yield return null; }
            Check(highest <= Mathf.Max(hover.y + 1f, AirdropShip.LeaveCeiling + 1f), $"airdrop: it flies off under the clouds (highest {highest:0} m as it leaves, hover {hover.y:0} m)");
            yield return new WaitForSeconds(0.3f);
            Check(old == null && AirdropShip.Flying == 1, $"airdrop: once it's gone it's cleaned up; the new one flies on ({AirdropShip.Flying})");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>The ball's beacon (under the glass dome in the middle, sitting still): bright, seen from far away.</summary>
        IEnumerator VictoryBeaconCheck(PlayerNet me, PlayerController pc, NetGame g)
        {
            float wait = Time.time + 8f;
            while ((Ball.Instance == null || Ball.Instance.BeaconIntensity <= 0f) && Time.time < wait) yield return null;
            var ball = Ball.Instance;
            Check(ball != null && ball.BeaconIntensity > 1f, $"the ball's beacon shines bright ({(ball != null ? ball.BeaconIntensity : 0f):0.0})");
            if (ball == null) yield break;
            var bp = ball.transform.position;
            var dir = (Cfg.SpawnPos(me.Team.Value) - bp);
            dir.y = 0f;
            var eye = bp + dir.normalized * 70f;
            eye.y = MapBuilder.Height(eye.x, eye.z) + 0.1f;
            float yaw = Quaternion.LookRotation(-dir).eulerAngles.y;
            pc.LocalTeleport(eye, yaw);
            pc.SetLook(yaw, -14f);
            yield return Snap("victory_ball_beacon_far");
        }
    }
}
