using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest dome -host -solo -fast -rules classic -map plains|highlands|beach|... -shotdir DIR: the glass dome over the
    /// map (it's what you see at the edge now, the old walls are invisible but still collide; nothing gets out over the top;
    /// it's closed at the top, above the ball drop and below the clouds; its tall wall and the trim along its bottom; the
    /// airdrop ship hovers over it, cuts a hole in it for the crate, patches it and only then leaves), the
    /// crashed UFO round the ball (the ball in plain sight from every base, nothing traps it, the ball reset still lands in
    /// the middle, a player can run up to it), and the ball's beacon coming up out of the ground through the ball.
    /// Pictures: dome_MAP_*.png.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator DomeRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            string map = (ThemeMaps.IsTheme ? ThemeMaps.Label(Cfg.Map) : Cfg.Map.ToString()).ToLower() + (Cfg.FourWay ? "_" + Cfg.ModeLabel.ToLower().Replace(" ", "") : "");
            string dir = ShotDir();
            GameSettings.SetGraphics(0, false);
            g.TimerPaused.Value = true;
            float half = Cfg.MapHalf;
            var root = MapBuilder.Root;
            yield return new WaitForSeconds(0.5f);
            Vector3 Ground(float x, float z) => new Vector3(x, MapBuilder.Height(x, z), z);
            float YawTo(Vector3 from, Vector3 to) => Quaternion.LookRotation(new Vector3(to.x - from.x, 0, to.z - from.z)).eulerAngles.y;
            float PitchTo(Vector3 eye, Vector3 to) { var d = to - eye; return Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; }
            // hold the camera still somewhere (even up in the air) for a moment, then take the picture
            IEnumerator HoldShot(string name, Vector3 at, float yaw, float pitch, float secs = 0.6f)
            {
                float until = Time.time + secs;
                while (Time.time < until)
                {
                    pc.LocalTeleport(at, yaw);
                    pc.SetLook(yaw, pitch);
                    yield return null;
                }
                pc.LocalTeleport(at, yaw);
                pc.SetLook(yaw, pitch);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"dome_{map}_{name}.png"));
                Log($"shot dome_{map}_{name}");
                yield return null;
                yield return null;
            }
            IEnumerator LookShot(string name, Vector3 eyeAt, Vector3 target, float secs = 0.6f)
            {
                var feet = eyeAt - Vector3.up * Cfg.EyeHeight;
                yield return HoldShot(name, feet, YawTo(eyeAt, target), -PitchTo(eyeAt, target), secs);
            }

            // ---------------- the dome ----------------
            Log($"map dome: footprint half {MapDome.HalfSize:0.0} m, shoulder {MapDome.Shoulder:0.0} m, peak {MapDome.Peak:0.0} m (map half {half})");
            Check(MapDome.Built && MapDome.Shown && MapDome.Collider != null && MapDome.Collider.enabled, "the glass dome over the map is built, drawn and solid");
            Check(MapDome.Peak >= Cfg.BallDropPoint.y + 3f && MapDome.Peak <= MapDome.PeakMax + 12f, $"its peak ({MapDome.Peak:0.0} m) is over the ball drop ({Cfg.BallDropPoint.y} m)");
            Check(MapDome.Shoulder >= 22f && MapDome.Peak >= MapDome.Shoulder + 14f, $"its wall goes straight up a good way before it curves over (shoulder {MapDome.Shoulder:0.0} m)");
            Check(MapDome.FrameLines > 0 && MapDome.TrimGap < 0.6f, $"a trim line runs round the bottom, on the ground all the way round (at most {MapDome.TrimGap:0.00} m off it; {MapDome.FrameLines} frame lines in all)");
            // closed at the top: straight up from round the middle you hit the glass right at the peak
            {
                int tops = 0;
                foreach (var o in new[] { Vector2.zero, new Vector2(2f, 0f), new Vector2(-4f, 3f), new Vector2(6f, -6f), new Vector2(-9f, -8f) })
                {
                    var from = new Vector3(o.x, MapDome.Peak - 6f, o.y);
                    if (Physics.Raycast(from, Vector3.up, out var th, 20f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore) && th.collider == MapDome.Collider
                        && Mathf.Abs(th.point.y - MapDome.HeightAt(o.x, o.y)) < 0.3f) tops++;
                }
                Check(tops == 5, $"it's closed at the top: straight up round the peak you hit the glass ({tops} / 5)");
            }
            float lowCloud = float.MaxValue;
            var cl = root.GetComponentInChildren<CloudLayer>();
            if (cl != null) foreach (Transform c in cl.transform) lowCloud = Mathf.Min(lowCloud, c.GetComponent<Renderer>().bounds.min.y);
            Check(cl == null || lowCloud > MapDome.Peak, $"the clouds are outside it, above the glass (lowest {lowCloud:0.0} m)");
            Check(MapDome.ExtentAlong(Vector3.right) >= half && MapDome.ExtentAlong(Vector3.forward) >= half && MapDome.ExtentAlong(new Vector3(1, 0, 1)) > half * 1.3f,
                $"its footprint covers the map (sides {MapDome.ExtentAlong(Vector3.right):0.0} m, corner {MapDome.ExtentAlong(new Vector3(1, 0, 1)):0.0} m from the middle)");
            int walls = 0, wallsDrawn = 0;
            foreach (var bc in root.GetComponentsInChildren<BoxCollider>())
                if (bc.name == "map wall") { walls++; if (bc.GetComponent<Renderer>().enabled) wallsDrawn++; }
            Check(walls == 4 && wallsDrawn == 0, $"the old boundary walls still collide but aren't drawn ({walls} walls, {wallsDrawn} drawn)");
            // the shoulder is over the ground all round the edge
            float worstClear = float.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI * 2f / 64f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var p = d * (MapDome.ExtentAlong(d) - 1f);
                worstClear = Mathf.Min(worstClear, MapDome.HeightAt(p.x, p.z) - MapBuilder.Height(p.x, p.z));
            }
            Check(worstClear > 5f, $"the glass stands well over the ground all round the edge (at least {worstClear:0.0} m)");
            // straight up from anywhere: the dome
            int ups = 0;
            var rng = new System.Random(7);
            for (int i = 0; i < 40; i++)
            {
                var p = Ground(((float)rng.NextDouble() * 2f - 1f) * (half - 3f), ((float)rng.NextDouble() * 2f - 1f) * (half - 3f));
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.up, out var hit, 300f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore) && hit.collider == MapDome.Collider) ups++;
                else if (Physics.RaycastAll(p + Vector3.up * 2f, Vector3.up, 300f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore).Length > 0) ups++; // (something else first, e.g. a tree: still covered)
            }
            Check(ups == 40, $"straight up from anywhere on the map you hit the glass ({ups} / 40)");
            // out over the old walls, sideways, at a few heights: the glass
            int outs = 0, outTries = 0;
            foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized })
                foreach (float y in new[] { 8f, MapDome.Shoulder + 4f, (MapDome.Shoulder + MapDome.Peak) * 0.5f })
                {
                    outTries++;
                    var from = new Vector3(0, y, 0) + d * 5f;
                    bool got = false;
                    foreach (var h in Physics.RaycastAll(from, d, half * 2f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                        if (h.collider == MapDome.Collider) got = true;
                    if (got) outs++;
                    else Log($"sideways ray from {from} towards {d} missed the glass");
                }
            Check(outs == outTries, $"looking out sideways at any height you hit the glass ({outs} / {outTries})");
            // a ball flung up and out over the edge stays in
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                probe.name = "dome probe";
                probe.transform.position = Ground(half - 4f, 0) + Vector3.up * 3f;
                probe.transform.localScale = Vector3.one * 0.6f;
                var rb = probe.AddComponent<Rigidbody>();
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.linearVelocity = new Vector3(18f, 40f, 4f);
                var probe2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                probe2.name = "dome probe 2";
                probe2.transform.position = new Vector3(3f, MapBuilder.Height(3f, 3f) + 2f, 3f);
                var rb2 = probe2.AddComponent<Rigidbody>();
                rb2.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb2.linearVelocity = new Vector3(0.5f, 70f, 0.3f);
                float top = 0f;
                for (float t = 0; t < 4f; t += Time.deltaTime) { top = Mathf.Max(top, probe2.transform.position.y); yield return null; }
                var pp = probe.transform.position;
                Check(Mathf.Abs(pp.x) < half + 0.5f && Mathf.Abs(pp.z) < half + 0.5f && pp.y > -5f, $"something flung up and out over the edge stays inside ({pp})");
                Check(top < MapDome.Peak + 0.5f, $"something shot straight up stops at the glass (got to {top:0.0} m, peak {MapDome.Peak:0.0} m)");
                Destroy(probe);
                Destroy(probe2);
            }
            // a player up high by the edge can't get out
            {
                var cc = me.GetComponent<CharacterController>();
                var at = new Vector3(half * 0.2f, MapDome.Shoulder + 6f, half - 3f);
                pc.LocalTeleport(at, 0f);
                yield return null;
                pc.LocalTeleport(at, 0f);
                cc.Move(new Vector3(0, 2f, 14f));
                var mp = me.transform.position;
                Check(mp.z < half + 0.3f, $"a player flying out over the old wall is stopped by the glass (at z {mp.z:0.0}, edge {half})");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.3f);
            }
            Check(MapDome.HeightAt(Cfg.ArenaCenter.x, Cfg.ArenaCenter.z) == float.MinValue, "the sudden death arena in space is well outside the dome");

            // ---- pictures of the dome ----
            float side = Cfg.BaseCenter[team].z < 0 ? -1f : 1f;
            {
                var spot = Ground(half * 0.42f, side * half * 0.62f);
                var edge = new Vector3(spot.x, 0, side * half);
                yield return HoldShot("inside_ground", spot, YawTo(spot, edge), -14f);
                yield return HoldShot("inside_looking_up", spot, YawTo(spot, edge), -55f);
                var mid = Ground(half * 0.25f, side * half * 0.3f);
                yield return HoldShot("inside_ground_across", mid, YawTo(mid, new Vector3(-half, 0, side * half)), -6f);
                var high = new Vector3(half * 0.15f, MapDome.Shoulder + 14f, side * half * 0.35f);
                yield return HoldShot("inside_high", high, YawTo(high, new Vector3(half, 0, side * half)), -2f);
                var corner = Ground(half - 7f, side * (half - 7f));
                yield return HoldShot("corner", corner, YawTo(corner, new Vector3(half, 0, side * half)), -22f);
                yield return HoldShot("corner_across", corner, YawTo(corner, Vector3.zero), -10f);
                var outside = new Vector3(half + 70f, MapDome.Peak + 30f, side * (half + 70f));
                yield return LookShot("outside", outside, new Vector3(0, MapDome.Shoulder, 0));
                // the top, closed: from the ground under it, from high up inside, from outside over it
                var under = Ground(half * 0.06f, side * half * 0.1f);
                yield return HoldShot("top_from_below", under, YawTo(under, Vector3.zero), -84f);
                var nearTop = new Vector3(half * 0.3f, MapDome.Peak - 16f, side * half * 0.25f);
                yield return LookShot("top_inside_high", nearTop + Vector3.up * Cfg.EyeHeight, new Vector3(0, MapDome.Peak, 0));
                var overTop = new Vector3(half * 0.35f, MapDome.Peak + 28f, side * half * 0.3f);
                yield return LookShot("top_outside", overTop, new Vector3(0, MapDome.Peak - 4f, 0));
                // the taller wall, and the trim along its bottom where it meets the ground
                var byWall = Ground(half * 0.3f, side * (half - 7f));
                yield return HoldShot("wall_bottom_trim", byWall, YawTo(byWall, new Vector3(byWall.x + 6f, 0, side * half)), 14f);
                var offWall = Ground(-half * 0.2f, side * (half - 34f));
                yield return HoldShot("wall_tall", offWall, YawTo(offWall, new Vector3(offWall.x, 0, side * half)), -16f);
            }

            // ---------------- the crashed UFO round the ball ----------------
            var cs = CrashSite.Current;
            var ball = Ball.Instance;
            Check(cs != null && cs.Collider != null && cs.FlameCount >= 8, $"the crashed UFO is there ({(cs != null ? cs.FlameCount : 0)} flames)");
            Check(ball != null && g.S == GameState.PreBall && new Vector2(ball.transform.position.x, ball.transform.position.z).magnitude < 0.5f, "the ball waits in the middle, in the crash site, under the glass dome");
            if (cs != null && ball != null)
            {
                var bp = ball.transform.position;
                var cp = cs.Collider.ClosestPoint(bp);
                Check((cp - bp).magnitude > Ball.Radius + 0.8f, $"the wreck's solid part keeps clear of the ball ({(cp - bp).magnitude - Ball.Radius:0.0} m)");
                Check(cs.Collider.bounds.max.y - MapBuilder.Height(0, 0) < 5f, $"the wreck is low ({cs.Collider.bounds.max.y - MapBuilder.Height(0, 0):0.0} m high)");
                // every base sees the ball: nothing of the wreck between an eye near each base side and the ball
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int seen = 0;
                    foreach (float far in new[] { 16f, 24f, 40f })
                    {
                        var eye = Cfg.BackDir(t) * far;
                        eye.y = MapBuilder.Height(eye.x, eye.z) + Cfg.EyeHeight;
                        bool clear = true;
                        foreach (var target in new[] { bp, bp + Vector3.up * 0.4f })
                            foreach (var h in Physics.RaycastAll(eye, (target - eye).normalized, (target - eye).magnitude - Ball.Radius - 0.05f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                                if (h.collider.GetComponentInParent<CrashSite>() != null) clear = false;
                        // (and nothing drawn of it either: no flame, debris or hull piece right in the line)
                        foreach (var r in cs.GetComponentsInChildren<Renderer>())
                        {
                            if (!r.enabled || r.name == "smoke" || r.name == "scorch" || r.name == "crater" || r.name == "furrow" || r.name == "glass shards") continue;
                            if (r.name.StartsWith("ground ") || r.name.StartsWith("rubble ")) continue; // (flat on the ground, or merged all round the middle: -autotest scenery checks the rubble against these lines)
                            var ray = new Ray(eye, (bp + Vector3.up * 0.2f - eye).normalized);
                            if (r.bounds.IntersectRay(ray, out float dist) && dist < (bp - eye).magnitude - 1.2f && r.bounds.size.y > 0.25f && r.bounds.max.y > bp.y + 0.1f
                                && DistanceToRay(ray, r.bounds.center) < 0.5f) clear = false;
                        }
                        if (clear) seen++;
                    }
                    Check(seen == 3, $"{Cfg.TeamName[t]}'s side sees the ball past the wreck ({seen} / 3 spots)");
                }
                // pictures from each side (the glass wall and the dome still up)
                for (int t = 0; t < Mathf.Min(2, Cfg.TeamCount); t++)
                {
                    var eye = Cfg.BackDir(t) * (MapBuilder.DomeRadius + 7f);
                    eye.y = MapBuilder.Height(eye.x, eye.z) + Cfg.EyeHeight;
                    yield return LookShot($"crash_from_{Cfg.TeamName[t].ToLower()}", eye, bp + Vector3.up * 0.3f, 1.2f);
                }
            }
            // the walls drop: the wreck stays, the ball is loose in it
            g.TimerPaused.Value = false;
            g.DevSkipPhase(GameState.PreBall);
            while (g.S != GameState.BallLive) yield return null;
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(1.5f);
            Check(!MapBuilder.GlassUp && CrashSite.Current != null && CrashSite.Current.gameObject.activeInHierarchy, "the walls dropped, the crashed UFO is still there");
            if (cs != null && ball != null)
            {
                var bp = ball.transform.position;
                Check(new Vector2(bp.x, bp.z).magnitude < 1f && bp.y < MapBuilder.Height(0, 0) + 1f, $"the ball sits loose in the crater ({bp})");
                for (int t = 0; t < Mathf.Min(2, Cfg.TeamCount); t++)
                {
                    var eye = Cfg.BackDir(t) * 9f;
                    eye.y = MapBuilder.Height(eye.x, eye.z) + Cfg.EyeHeight;
                    yield return LookShot($"crash_open_from_{Cfg.TeamName[t].ToLower()}", eye, bp + Vector3.up * 0.4f, 1.0f);
                }
                var side3 = Quaternion.Euler(0, 55f, 0) * CrashSite.Dir * 11f;
                var eye3 = Ground(side3.x, side3.z) + Vector3.up * Cfg.EyeHeight;
                yield return LookShot("crash_close", eye3, cs.SaucerCentre * 0.5f);
                var above = -CrashSite.Dir * 10f + Vector3.up * 16f + Quaternion.Euler(0, 90f, 0) * CrashSite.Dir * 6f;
                yield return LookShot("crash_above", above, cs.SaucerCentre * 0.45f);
                // the beacon: once it's sat still for 3 s, it comes up out of the ground and through the ball into the sky
                yield return new WaitForSeconds(2.5f);
                var pillar = ball.transform.Find("beacon/pillar");
                var pr = pillar != null ? pillar.GetComponent<Renderer>() : null;
                bool on = pr != null && pr.gameObject.activeInHierarchy;
                Check(on && pr.bounds.min.y < MapBuilder.Height(bp.x, bp.z) - 3f && pr.bounds.max.y > bp.y + 150f,
                    $"the beacon comes up out of the ground under the ball and through it ({(pr != null ? pr.bounds.min.y : 0f):0.0} m to {(pr != null ? pr.bounds.max.y : 0f):0.0} m, ball at {bp.y:0.0} m)");
                var eyeB = Cfg.BackDir(team) * 13f + Quaternion.Euler(0, 90f, 0) * Cfg.BackDir(team) * 4f;
                eyeB.y = MapBuilder.Height(eyeB.x, eyeB.z) + Cfg.EyeHeight;
                yield return LookShot("beacon_crash", eyeB, bp + Vector3.up * 5f);
                // out in the open, on the grass
                // (a flat spot, so it lies still: on the hilly maps the first flat one between our base and the middle)
                var open = Ground(Cfg.BaseCenter[team].x * 0.45f + 6f, Cfg.BaseCenter[team].z * 0.45f);
                for (float f = 0.45f; f < 0.95f; f += 0.02f)
                {
                    var q = Ground(Cfg.BaseCenter[team].x * f + 6f, Cfg.BaseCenter[team].z * f);
                    float bump = 0f;
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        bump = Mathf.Max(bump, Mathf.Abs(MapBuilder.Height(q.x + Mathf.Cos(a) * 2f, q.z + Mathf.Sin(a) * 2f) - q.y));
                    }
                    if (bump < 0.05f && !ThemeMaps.WaterAt(q.x, q.z) && !ThemeMaps.LavaAt(q.x, q.z)) { open = q; break; }
                }
                ball.ServerDrop(open + Vector3.up * 1.5f, Vector3.zero);
                yield return new WaitForSeconds(4.5f);
                var ob = ball.transform.position;
                bool on2 = pr != null && pr.gameObject.activeInHierarchy;
                Check(on2 && pr.bounds.min.y < MapBuilder.Height(ob.x, ob.z) - 3f && Mathf.Abs(pr.bounds.center.x - ob.x) < 0.05f && Mathf.Abs(pr.bounds.center.z - ob.z) < 0.05f,
                    $"out in the open too, the beacon runs from under the ground up through the ball ({(pr != null ? pr.bounds.min.y : 0f):0.0} m, ground {MapBuilder.Height(ob.x, ob.z):0.0} m)");
                var eyeO = ob + (Cfg.BackDir(team) * 9f) + Quaternion.Euler(0, 90f, 0) * Cfg.BackDir(team) * 5f;
                eyeO.y = MapBuilder.Height(eyeO.x, eyeO.z) + 0.6f;
                yield return LookShot("beacon_ground_low", eyeO, ob + Vector3.up * 1.6f);
                var eyeM = ob + Cfg.BackDir(team) * 11f + Quaternion.Euler(0, 90f, 0) * Cfg.BackDir(team) * 3f;
                eyeM.y = MapBuilder.Height(eyeM.x, eyeM.z) + Cfg.EyeHeight;
                yield return LookShot("beacon_ground", eyeM, ob + Vector3.up * 4f);
                var eyeO2 = ob + Cfg.BackDir(team) * 4f + Quaternion.Euler(0, 90f, 0) * Cfg.BackDir(team) * 1.5f + Vector3.up * 2.4f;
                yield return LookShot("beacon_ground_close", eyeO2, ob - Vector3.up * 0.2f);

                // the ball reset still drops it into the middle, onto the ground, clear of the wreck
                ball.ServerReset();
                float until = Time.time + 12f;
                while (Time.time < until && (ball.transform.position.y > MapBuilder.Height(0, 0) + 1.5f || ball.GetComponent<Rigidbody>().linearVelocity.magnitude > 0.3f)) yield return null;
                yield return new WaitForSeconds(0.5f);
                var rp = ball.transform.position;
                var inside = cs.Collider.ClosestPoint(rp);
                Check(new Vector2(rp.x, rp.z).magnitude < 4f && rp.y < MapBuilder.Height(rp.x, rp.z) + 1.2f && (inside - rp).magnitude > 0.2f,
                    $"a ball reset still drops it in the middle, on the ground clear of the wreck ({rp})");
                // dropped onto the saucer, it rolls off (or sits on top, where you can walk up to it) - never stuck under it
                ball.ServerDrop(cs.SaucerCentre + Vector3.up * 4f + Quaternion.Euler(0, 90f, 0) * CrashSite.Dir * 1.2f, Vector3.zero);
                yield return new WaitForSeconds(6f);
                var sp = ball.transform.position;
                bool under = Physics.Raycast(sp + Vector3.up * 0.7f, Vector3.up, out var uh, 20f) && uh.collider == cs.Collider;
                Check(!under && (cs.Collider.ClosestPoint(sp) - sp).magnitude > 0.2f && sp.y > MapBuilder.Height(sp.x, sp.z) - 0.2f,
                    $"a ball dropped on the saucer isn't trapped ({sp}, {(sp.y - MapBuilder.Height(sp.x, sp.z)):0.0} m up)");
                // and a player can run straight up to it in the crater and pick it up
                ball.ServerDrop(Ball.DomeSpot + Vector3.up * 0.3f, Vector3.zero);
                yield return new WaitForSeconds(1.5f);
                for (int t = 0; t < Mathf.Min(2, Cfg.TeamCount); t++)
                {
                    var from = Cfg.BackDir(t) * 16f;
                    from.y = MapBuilder.Height(from.x, from.z) + 0.1f;
                    float yaw = Quaternion.LookRotation(-Cfg.BackDir(t)).eulerAngles.y;
                    pc.LocalTeleport(from, yaw);
                    pc.SetLook(yaw, 10f);
                    yield return new WaitForSeconds(0.2f);
                    Binds.TestHold(Bind.Forward, true);
                    float stop = Time.time + 4f;
                    while (Time.time < stop && Vector3.Distance(me.transform.position, ball.transform.position) > 1.6f) yield return null;
                    Binds.TestReleaseAll();
                    float dist = Vector3.Distance(me.transform.position, ball.transform.position);
                    me.PickupBallRpc();
                    yield return new WaitForSeconds(0.4f);
                    Check(dist < 2.2f && me.CarryingBall, $"from {Cfg.TeamName[t]}'s side you run straight up to the ball and pick it up ({dist:0.0} m)");
                    ball.ServerDrop(Ball.DomeSpot + Vector3.up * 0.3f, Vector3.zero);
                    yield return new WaitForSeconds(1.2f);
                }
            }

            // ---------------- the airdrop ship over the dome ----------------
            for (int pass = 0; pass < 2; pass++)
            {
                bool centre = Cfg.AirdropCenter;
                Cfg.AirdropCenter = pass == 1;
                foreach (var c in new List<Container>(Container.All)) if (c != null && c.IsAirdrop && c.IsSpawned) c.NetworkObject.Despawn(true);
                g.DevSpawnAirdrop();
                Cfg.AirdropCenter = centre;
                double st = g.LaneStartAt(0);
                var gp = g.LanePosAt(0);
                double E() => g.NetworkManager.ServerTime.Time - st;
                var toMid = new Vector3(-gp.x, 0, -gp.z);
                if (toMid.sqrMagnitude < 1f) toMid = -Cfg.BackDir(team);
                var eye = gp + toMid.normalized * (pass == 0 ? 45f : 40f) + Vector3.up * Cfg.EyeHeight;
                eye.y = MapBuilder.Height(eye.x, eye.z) + Cfg.EyeHeight;
                var hover = AirdropShip.HoverAt(gp);
                string tag = pass == 0 ? "airdrop" : "airdrop_centre";
                Log($"{tag}: drop at {gp}, ship hovers at {hover.y:0.0} m, glass over it {MapDome.HeightAt(gp.x, gp.z):0.0} m");
                while (E() < 2.2) yield return null;
                var shipNow = AirdropShip.ShipTransform;
                yield return LookShot(tag + "_arriving", eye, shipNow != null ? Vector3.Lerp(shipNow.position, gp, 0.3f) : gp + Vector3.up * (hover.y - gp.y) * 0.9f, 0.05f);
                while (E() < NetGame.DropArrive - 0.4) yield return null;
                var ship = AirdropShip.ShipTransform;
                float hullR = Mathf.Max(26f * Art.Sphere.bounds.extents.x, 15.2f);
                float glassUnder = MapDome.HighestOver(gp.x, gp.z, hullR);
                Check(ship != null && ship.position.y - (Art.Sphere.bounds.extents.y * 4.5f + 5.5f) > glassUnder,
                    $"{tag}: the ship hovers clear over the glass ({(ship != null ? ship.position.y : 0f):0.0} m, its open hatch down to {(ship != null ? ship.position.y - Art.Sphere.bounds.extents.y * 4.5f - 5.5f : 0f):0.0} m, glass under it up to {glassUnder:0.0} m)");
                // the beam comes down onto the glass and cuts a hole in it (a rim round it like the frame lines)
                float glassY = MapDome.HeightAt(gp.x, gp.z);
                var hole = new Vector3(gp.x, glassY, gp.z);
                var aside = Vector3.Cross(Vector3.up, toMid.normalized);
                var hi = gp + toMid.normalized * 24f + aside * 8f;
                hi.y = Mathf.Min(MapDome.HeightAt(hi.x, hi.z) - 5f, glassY - 6f);
                var above = gp - toMid.normalized * 30f + aside * 20f + Vector3.up * (glassY + 9f - gp.y);
                while (E() < AirdropShip.CutStart - 0.09) yield return null;
                Check(Mathf.Abs(AirdropShip.BeamBottom - glassY) < 0.5f && AirdropShip.HoleOpen < 0.03f && MapDome.GlassAt(gp.x, gp.z) > 0.99f,
                    $"{tag}: the beam comes down onto the glass and stops there (to {AirdropShip.BeamBottom:0.0} m, glass at {glassY:0.0} m; no hole yet: {AirdropShip.HoleOpen:0.00} open, glass {MapDome.GlassAt(gp.x, gp.z):0.00}, {E() - AirdropShip.CutStart:0.00} s to go)");
                yield return LookShot(tag + "_beam_on_glass", eye, hole, 0.02f);
                while (E() < AirdropShip.CutStart + AirdropShip.OpenTime * 0.45) yield return null;
                float opening = AirdropShip.HoleOpen;
                yield return LookShot(tag + "_hole_opening", hi, hole, 0.02f);
                Check(opening > 0.1f && opening < 0.95f, $"{tag}: the hole opens up round the beam ({opening:0.00} open)");
                while (E() < AirdropShip.CutStart + AirdropShip.OpenTime + 0.2) yield return null;
                var rim = MapDome.HoleRim(0);
                var off = aside * (AirdropShip.HoleR + 3f);
                Check(Mathf.Abs(MapDome.HoleRadius(0) - AirdropShip.HoleR) < 0.05f && MapDome.GlassAt(gp.x, gp.z) < 0.01f && MapDome.GlassAt(gp.x + off.x, gp.z + off.z) > 0.99f
                    && rim != null && rim.enabled && rim.bounds.size.x > AirdropShip.HoleR * 2f,
                    $"{tag}: a round hole is open in the glass under the ship (radius {MapDome.HoleRadius(0):0.0} m, glass in it {MapDome.GlassAt(gp.x, gp.z):0.00}, beside it {MapDome.GlassAt(gp.x + off.x, gp.z + off.z):0.00}), with its rim");
                Check(AirdropShip.BeamBottom < gp.y + 0.5f, $"{tag}: then the beam goes on down through the hole to the ground ({AirdropShip.BeamBottom:0.0} m)");
                bool solid = Physics.Raycast(new Vector3(gp.x, glassY - 3f, gp.z), Vector3.up, out var sh, 10f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore) && sh.collider == MapDome.Collider;
                Check(solid && MapDome.Collider.enabled, $"{tag}: the dome stays solid there (nothing can get out through the hole)");
                if (pass == 0) yield return LookShot(tag + "_hole_open_from_outside", above, hole, 0.02f);
                // the crate comes down through it
                float crossR = -1f, crossT = -1f;
                bool crateShot = false, noColliders = true;
                while (E() < NetGame.DropLand)
                {
                    var fc = AirdropShip.FallingCrate;
                    if (fc != null)
                    {
                        noColliders &= fc.GetComponentsInChildren<Collider>().Length == 0;
                        float y = fc.position.y;
                        if (y < glassY + 1.5f && y > glassY - 1.5f && (crossR < 0f || MapDome.HoleRadius(0) < crossR)) { crossR = MapDome.HoleRadius(0); crossT = (float)E(); }
                        if (!crateShot && y < glassY + 2.5f)
                        {
                            crateShot = true;
                            yield return LookShot(tag + "_crate_through_hole", hi, hole, 0.02f);
                            continue;
                        }
                    }
                    yield return null;
                }
                Check(crossR > 2f && noColliders, $"{tag}: the crate comes down through the open hole ({crossT - NetGame.DropArrive:0.0} s into the beam, the hole {crossR:0.0} m wide; the falling crate has nothing solid to catch on the glass)");
                int Crates() { int n = 0; foreach (var c in Container.All) if (c != null && c.IsAirdrop) n++; return n; }
                while (E() < NetGame.DropLand + 1.0) yield return null;
                Check(Crates() >= 1, $"{tag}: the crate came down through the glass and landed");
                // the hole is patched, then the ship leaves
                while (E() < (AirdropShip.SealStart + AirdropShip.SealEnd) * 0.5) yield return null;
                float sealing = AirdropShip.HoleOpen;
                yield return LookShot(tag + "_hole_patching", hi, hole, 0.02f);
                Check(sealing > 0.05f && sealing < 0.95f && AirdropShip.ShipScale > 0.999f, $"{tag}: once the crate's down, the hole closes up ({sealing:0.00} open), the ship waits");
                while (E() < AirdropShip.SealEnd + 0.1) yield return null;
                var shipAt = AirdropShip.ShipTransform;
                Check(MapDome.HoleRadius(0) == 0f && MapDome.HoleRim(0) == null && MapDome.GlassAt(gp.x, gp.z) > 0.99f
                    && shipAt != null && Vector3.Distance(shipAt.position, AirdropShip.HoverPoint) < 1f && AirdropShip.ShipScale > 0.999f,
                    $"{tag}: the hole is patched (glass {MapDome.GlassAt(gp.x, gp.z):0.00}) before the ship moves off");
                yield return LookShot(tag + "_hole_patched", hi, hole, 0.02f);
                while (E() < AirdropShip.LeaveStart + 2.0) yield return null;
                float leaving = AirdropShip.ShipScale;
                var shipNow2 = AirdropShip.ShipTransform;
                yield return LookShot(tag + "_ship_leaving", eye, shipNow2 != null ? shipNow2.position : hole, 0.02f);
                Check(leaving < 0.9f && leaving > 0.1f, $"{tag}: then it flies off, shrinking away ({leaving:0.00})");
                while (E() < AirdropShip.Gone + 0.5) yield return null;
                Check(AirdropShip.ShipTransform == null && MapDome.HoleRim(0) == null, $"{tag}: the ship's gone, the glass is whole");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            }

            // ---------------- PSX / AI PSX: their concrete walls, no dome drawn (it still stops you) ----------------
            {
                var spot = Ground(half * 0.42f, side * half * 0.62f);
                var edge = new Vector3(spot.x, 0, side * half);
                GameSettings.SetGraphics(1, false);
                yield return new WaitForSeconds(0.6f);
                int shown = 0;
                foreach (var bc in root.GetComponentsInChildren<BoxCollider>()) if (bc.name == "map wall" && bc.GetComponent<Renderer>().enabled) shown++;
                Check(!MapDome.Shown && shown == 4 && MapDome.Collider.enabled, $"PSX: the concrete walls ({shown}), no glass drawn, still solid");
                yield return HoldShot("psx_edge", spot, YawTo(spot, edge), -10f);
                GameSettings.SetGraphics(2, false);
                yield return new WaitForSeconds(0.8f);
                Check(!MapDome.Shown && MapDome.Collider.enabled, "AI PSX: no glass drawn, still solid");
                var aiAt = Cfg.BackDir(team) * 14f;
                yield return HoldShot("aipsx_crash", Ground(aiAt.x, aiAt.z) + Vector3.up * 0.1f, YawTo(aiAt, Vector3.zero), -4f);
                GameSettings.SetGraphics(0, false);
                yield return new WaitForSeconds(0.6f);
                Check(MapDome.Shown, "Normal again: the glass dome");
            }
            Log("dome test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        static float DistanceToRay(Ray r, Vector3 p) => Vector3.Cross(r.direction, p - r.origin).magnitude;
    }
}
