using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest victory -host -solo -fast -rules classic -shotdir DIR (windowed, for the pictures):
    /// - explosions: a close one (fireball, smoke column, hard shake) and a far one (late muffled boom, no shake);
    /// - airdrops: it hovers high (under the clouds), its underside is lit, its beam fades down as you come up to it and
    ///   the crate coming down it is solid with the beam see-through round it (victory_airdrop_crate, _beam_near); the ship never pops out of existence - a ship that's flying off keeps shrinking away even when its lane
    ///   starts the next drop at once (two in the sky), it stays low (in from at most ArriveUp over its hover point, off
    ///   under the clouds), it shines down out of its belly (a spotlight and a glow), and its beam is bright
    ///   (victory_airdrop_beam, victory_airdrop_far, victory_airdrop_leaving);
    /// - the ball's beacon is bright (victory_ball_beacon_far);
    /// - the win (with our bedrock walled in, two walls high: the camera has to film from outside, over them, with nothing
    ///   in the way; the dome goes; the winners' victory screen is black): we take the ball, put it in our socket, walk out into the wild and the timer runs out - the game is
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
            yield return VictoryExplosionChecks(me, pc);
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
            // a walled-in base round the bedrock (two walls high all round, 9 m out): the camera mustn't end up inside it or
            // behind a wall (it used to be blocked about one time in three)
            var home = Cfg.SpawnPos(team, me.Slot.Value);
            int walls = 0;
            for (int side = 0; side < 4; side++)
            {
                var n = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward;
                var along = Vector3.Cross(Vector3.up, n);
                for (int k = 0; k < 6; k++)
                    for (int lvl = 0; lvl < 2; lvl++)
                    {
                        var wp = home + n * 9f + along * (-7.5f + k * 3f);
                        wp.y = home.y - 0.1f + lvl * 3f;
                        SpawnKeylessPiece(PieceType.Wall, team, wp, Quaternion.LookRotation(n));
                        walls++;
                    }
            }
            yield return new WaitForSeconds(0.5f);
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
            float lastE = 0f, maxLift = 0f, maxShip = 0f, camFromBase = float.MaxValue;
            bool domeGone = true, ballBeamOff = true, tagSeen = false;
            float maxClose = 0f;
            float dashY = 0f;
            int camFrames = 0, camBlocked = 0, treeLooks = 0, treeBlocked = 0;
            bool pathChecked = false;
            int taken = 0;
            float hoverY = 0f;
            var shots = new Queue<(float at, string name)>(new[]
            {
                (0.9f, "victory_cut_dome"), (2.6f, "victory_cut_arriving"), (VictoryCutscene.BeamOn + 0.8f, "victory_cut_beam"), (VictoryCutscene.LiftStart + 1.7f, "victory_cut_lift"),
                (VictoryCutscene.BeamOff - 0.3f, "victory_cut_taken"), (VictoryCutscene.LeaveStart + 0.6f, "victory_cut_windup"), (VictoryCutscene.DashStart - 0.1f, "victory_cut_lifted"), (VictoryCutscene.DashStart + 0.5f, "victory_cut_leaving"), (VictoryCutscene.Gone - 0.06f, "victory_cut_twinkle"),
            });
            var body = me.transform.Find("body");
            while (VictoryCutscene.Active)
            {
                float e = VictoryCutscene.Elapsed;
                lastE = e;
                taken = Mathf.Max(taken, VictoryCutscene.Taken);
                // the camera's whole move (planned once the ship's built), sampled at 30 fps: no lurch - its top speed and
                // acceleration stay modest (the push in on the winners used to hit ~28 m/s) - and the push in/out ramp has no jumps
                if (!pathChecked && VictoryCutscene.Ship != null)
                {
                    pathChecked = true;
                    const float dt = 1f / 30f;
                    float maxV = 0f, maxA = 0f, maxDClose = 0f, atV = 0f;
                    var p0 = VictoryCutscene.CamPathAt(0f);
                    var v0 = Vector3.zero;
                    for (float t = dt; t <= VictoryCutscene.Length; t += dt)
                    {
                        var p1 = VictoryCutscene.CamPathAt(t);
                        var v1 = (p1 - p0) / dt;
                        if (v1.magnitude > maxV) { maxV = v1.magnitude; atV = t; }
                        if (t > dt * 1.5f) maxA = Mathf.Max(maxA, (v1 - v0).magnitude / dt);
                        maxDClose = Mathf.Max(maxDClose, Mathf.Abs(VictoryCutscene.CloseUp(t) - VictoryCutscene.CloseUp(t - dt)));
                        p0 = p1;
                        v0 = v1;
                    }
                    Check(maxV < 16f && maxA < 30f && maxDClose < 0.04f,
                        $"victory: the camera's move is smooth (top speed {maxV:0.0} m/s at {atV:0.0} s, top acceleration {maxA:0.0} m/s², biggest push-in step per frame {maxDClose:0.000}; pushes in {VictoryCutscene.CamPush * 100f:0}%, {VictoryCutscene.TreesHidden} trees hidden)");
                }
                if (Hud.GameOverShownAt != screenBefore) early = true;
                // locked: no free cursor, no pause, the camera's the cutscene's (away from our eyes)
                lockedAll &= Cursor.lockState == CursorLockMode.Locked && !pc.Paused && !pc.MenuOpen;
                var cam = Camera.main.transform;
                if (e > 0.3f && VictoryCutscene.CameraPose(out var cp, out _, out _)) camAll &= Vector3.Distance(cam.position, cp) < 1.5f + 35f * Time.unscaledDeltaTime /* (still last frame's pose: it drifts ~1 m/s - faster as it pushes in on the winners - and a screenshot frame is long) */ && Vector3.Distance(cam.position, me.EyePos) > 6f;
                // in close on the winners: our name tag (over our head) is on screen between the letterbox bars
                float closeNow = VictoryCutscene.CloseUp(e);
                maxClose = Mathf.Max(maxClose, closeNow);
                if (closeNow > 0.9f && body != null && body.gameObject.activeSelf)
                {
                    var tp = Camera.main.WorldToScreenPoint(me.transform.position + Vector3.up * (body.localPosition.y + 2.5f));
                    tagSeen |= tp.z > 0.5f && tp.x > 0f && tp.x < Screen.width && tp.y > Screen.height * 0.12f && tp.y < Screen.height * 0.88f;
                }
                if (e > 1.2f && e < VictoryCutscene.LiftStart) homeOk |= Vector3.Distance(me.transform.position, Cfg.SpawnPos(team, me.Slot.Value)) < 1.6f;
                if (body != null && body.gameObject.activeSelf) maxLift = Mathf.Max(maxLift, body.localPosition.y);
                liftSeen |= VictoryCutscene.LiftOf(0) > 0.3f && VictoryCutscene.LiftOf(0) < 0.9f && body != null && body.localPosition.y > 2f;
                goneSeen |= VictoryCutscene.LiftOf(0) >= 1f && body != null && !body.gameObject.activeSelf;
                if (VictoryCutscene.Ship != null) maxShip = Mathf.Max(maxShip, VictoryCutscene.Ship.position.y);
                if (VictoryCutscene.Ship != null) hoverY = VictoryCutscene.HoverPoint.y;
                // the dome's gone from the very start (so it's open sky for the UFO and the camera), and so is the ball's beam
                if (e > 0.1f && MapDome.Built) domeGone &= MapDome.Fade <= 0.001f && (MapDome.Root == null || !MapDome.Root.activeSelf);
                if (e > 0.1f && Ball.Instance != null) ballBeamOff &= Ball.Instance.BeaconIntensity <= 0f;
                if (VictoryCutscene.Ship != null && e >= VictoryCutscene.DashStart - 0.15f && e < VictoryCutscene.DashStart) dashY = VictoryCutscene.Ship.position.y;
                // nothing between the camera and the beam the winners go up (no wall of theirs, no hill): the middle of
                // it, and the hatch
                if (e > VictoryCutscene.BeamOn + 0.3f && e < VictoryCutscene.BeamOff && VictoryCutscene.CameraPose(out var vp, out _, out _))
                {
                    var hov = VictoryCutscene.HoverPoint;
                    var spotNow = g.CutsceneSpot.Value;
                    foreach (var target in new[] { Vector3.Lerp(spotNow, hov, 0.5f), hov + Vector3.up * AirdropShip.HatchY })
                    {
                        camFrames++;
                        var dv = target - vp;
                        foreach (var hit in Physics.SphereCastAll(vp, 0.2f, dv.normalized, dv.magnitude - 1f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                            if (hit.collider.GetComponentInParent<PlayerNet>() == null) { camBlocked++; break; }
                    }
                    // no tree's needles (they've no colliders) in front of the winners or the beam
                    foreach (var target in new[] { spotNow + Vector3.up * 1.2f, Vector3.Lerp(spotNow, hov, 0.5f), hov + Vector3.up * AirdropShip.HatchY })
                    {
                        treeLooks++;
                        if (VictoryCutscene.TreeInWay(vp, target)) treeBlocked++;
                    }
                    camFromBase = Mathf.Min(camFromBase, new Vector2(vp.x - spotNow.x, vp.z - spotNow.z).magnitude);
                }
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
            Check(maxShip <= Mathf.Max(hoverY + AirdropShip.ArriveUp, VictoryCutscene.CruiseY + 21f) + 1f, $"victory: the UFO goes no higher than it has to (highest {maxShip:0} m, hovering at {hoverY:0} m, over the peaks at {VictoryCutscene.CruiseY:0} m)");
            Check(!early, "victory: the victory screen waited for the cutscene");
            Check(domeGone, "victory: there's no glass dome over the map for the whole cutscene");
            Check(ballBeamOff, "victory: the ball's beam is off for the cutscene (it doesn't run up through the UFO)");
            Check(dashY > MapScenery.RangeTop(0) + 8f && dashY > hoverY + 10f, $"victory: the UFO lifts up over the mountains before it flies off ({dashY:0} m up; it hovered at {hoverY:0} m, the nearest peaks reach {MapScenery.RangeTop(0):0} m)");
            Check(camFrames > 20 && camBlocked == 0 && camFromBase > 20f && VictoryCutscene.CamClear > 0.5f,
                $"victory: the camera films from outside the walled-in base ({camFromBase:0} m out, {walls} walls round it) with nothing in the way of the beam ({camBlocked} of {camFrames} looks blocked; it sees {VictoryCutscene.CamClear * 100f:0}% of the action, {VictoryCutscene.CamTried} spots tried)");
            Check(pathChecked && treeLooks > 30 && treeBlocked == 0,
                $"victory: no tree stands between the camera and the winners or the beam ({treeBlocked} of {treeLooks} looks blocked; {VictoryCutscene.TreesHidden} trees hidden as a fallback)");
            Check(maxClose > 0.95f && VictoryCutscene.CamPush > 0.05f && tagSeen && VictoryCutscene.CloseUp(VictoryCutscene.BeamOff - 0.9f) < 0.05f,
                $"victory: the camera pushes in close on the winners ({VictoryCutscene.CamPush * 100f:0}% of the way in) with their name tags on screen ({tagSeen}), and is back out before they're all in the ship");
            var beamCol = VictoryCutscene.BeamColour(g);
            Check(beamCol == Cfg.TeamColor[team], $"victory: the UFO's beam is the winning team's colour ({beamCol} vs {Cfg.TeamColor[team]})");
            yield return new WaitForSeconds(1.2f);
            Check(Hud.GameOverShownAt > t0 + VictoryCutscene.Length - 0.5f && !VictoryCutscene.Active && VictoryCutscene.Ship == null,
                $"victory: then the victory screen comes up ({Hud.GameOverShownAt - t0:0.0} s after the end) and the UFO is gone");
            Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, "victory: the cursor is free on the victory screen");
            Check(Hud.WinnerScreenBlack, "victory: the winners' victory screen is black (they escaped)");
            yield return Snap("victory_screen");
            Log("victory test done");
            Application.Quit(0);
        }

        /// <summary>
        /// Explosions (local, just the look and sound): one 24 m in front of us - a big fireball, the smoke column, a hard
        /// shake, no late far-off boom (victory_explosion_fireball, victory_explosion_smoke) - and one 250 m off: no shake to
        /// speak of, its boom heard late (at the speed of sound) and muffled, but heard.
        /// </summary>
        IEnumerator VictoryExplosionChecks(PlayerNet me, PlayerController pc)
        {
            var spawn = me.transform.position;
            var toMid = new Vector3(-spawn.x, 0f, -spawn.z);
            if (toMid.sqrMagnitude < 1f) toMid = Vector3.forward;
            toMid.Normalize();
            float yaw = Quaternion.LookRotation(toMid).eulerAngles.y;
            pc.SetLook(yaw, -6f);
            var at = spawn + toMid * 24f;
            at.y = MapBuilder.Height(at.x, at.z) + 0.3f;
            int before = Fx.Explosions;
            Fx.Trauma = 0f;
            Fx.Explosion(at);
            Check(Fx.Explosions == before + 1 && Fx.LastBlastShake > 0.6f && Fx.LastBlastSoundDelay < 0f,
                $"explosions: one close by shakes the camera hard ({Fx.LastBlastShake:0.00} at {Fx.LastBlastDistance:0} m) with no late far-off boom");
            yield return new WaitForSeconds(0.12f);
            yield return Snap("victory_explosion_fireball");
            yield return new WaitForSeconds(1.6f);
            int smoke = FxSmoke.Alive;
            yield return Snap("victory_explosion_smoke");
            Check(smoke >= 12, $"explosions: a column of smoke rises ({smoke} puffs)");
            var far = spawn + toMid * 250f;
            Fx.Explosion(far);
            float want = Vector3.Distance(Camera.main.transform.position, far) / 343f;
            Check(Mathf.Abs(Fx.LastBlastSoundDelay - want) < 0.05f && Fx.LastBlastShake < 0.05f && Fx.BoomCutoff(250f) < 2500f && Fx.BoomVolume(600f) > 0.3f,
                $"explosions: one 250 m off is heard {Fx.LastBlastSoundDelay:0.00} s later (sound speed), muffled ({Fx.BoomCutoff(250f):0} Hz) but loud enough right across the map ({Fx.BoomVolume(600f):0.00} at 600 m), no shake");
            yield return new WaitForSeconds(4f);
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
            // it hovers higher than it used to (45 m), but under the clouds
            Check(hover.y >= Mathf.Min(gp.y + AirdropShip.Hover, AirdropShip.HoverCeiling) - 0.5f && hover.y + 6f < 100f,
                $"airdrop: the ship hovers high, under the clouds ({hover.y - gp.y:0} m over the drop, at {hover.y:0} m)");
            // its underside is lit from below (it isn't a sky-coloured disc)
            var under = AirdropShip.UnderLight;
            Check(under != null && under.enabled && under.intensity > 10f && under.transform.forward.y > 0.95f,
                $"airdrop: a light under the ship shines up at its underside ({(under != null ? under.intensity : 0f):0})");
            // the crate coming down the beam is solid and the beam's see-through round it
            var crate = AirdropShip.FallingCrate;
            bool solid = crate != null;
            if (crate != null) foreach (var r in crate.GetComponentsInChildren<MeshRenderer>()) if (r.sharedMaterial != null && r.sharedMaterial.color.a < 0.99f) solid = false;
            Check(solid && AirdropShip.CrateClear > 1f, $"airdrop: the crate coming down the beam is solid and the beam clears round it (clear {AirdropShip.CrateClear:0.0} m)");
            // beams: bright from far away, faint right next to them (Settings > Display > BEAMS, at the defaults)
            float keepS = GameSettings.BeamStrength, keepF = GameSettings.BeamFalloff;
            GameSettings.SetBeams(GameSettings.BeamStrengthDefault, GameSettings.BeamFalloffDefault, false);
            float seenNear = BeamFx.Seen(gp, gp + Vector3.right * 1.5f), seenFar = BeamFx.Seen(gp, gp + Vector3.right * 140f);
            var bm = AirdropShip.BeamMaterial;
            Check(seenNear < 0.3f && seenFar > 0.95f && bm != null && bm.GetFloat("_DistFade") > 0.5f,
                $"beams fade down as you come up to them ({seenNear:0.00} of full right by it, {seenFar:0.00} from 140 m)");
            if (crate != null)
            {
                var to = crate.position + Vector3.up * 0.6f - me.EyePos;
                pc.SetLook(yaw, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
                yield return Snap("victory_airdrop_crate");
                pc.SetLook(yaw, -30f);
            }
            yield return Snap("victory_airdrop_beam");
            // right up by the beam: faint
            var nearEye = gp + toMid.normalized * 9f;
            nearEye.y = MapBuilder.Height(nearEye.x, nearEye.z) + 0.1f;
            pc.LocalTeleport(nearEye, yaw);
            pc.SetLook(yaw, -10f);
            yield return Snap("victory_airdrop_beam_near");
            GameSettings.SetBeams(keepS, keepF, false);
            pc.LocalTeleport(eye, yaw);
            pc.SetLook(yaw, -30f);
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
