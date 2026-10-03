using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest scenery -host -solo -rules classic -map plains|highlands -shotdir DIR: the scenery round the map - the
    /// crash's dirt and rubble in the middle (no yellow circle; nothing but gravel between a base and the ball; no rubble
    /// collider), the layers of mountain ranges (further out, taller, clear of the dome and the space arena), the big
    /// boulders (solid, clear of the bases, spawns, the middle, the ways between them and the walls; no trees in them)
    /// and, on Highlands, the rock on the hill faces (in big clean faces, no specks; no grass on it). Photographs each
    /// in Normal, PSX and AI PSX (scenery_MAP_*.png) and times frames with the new scenery shown and hidden.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator SceneryRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            string map = Cfg.Map.ToString().ToLower() + (Cfg.FourWay ? "_" + Cfg.ModeLabel.ToLower().Replace(" ", "") : "");
            string dir = ShotDir();
            GameSettings.SetGraphics(0, false);
            GameSettings.ResetWorldLook(false);
            g.TimerPaused.Value = true;
            float half = Cfg.MapHalf;
            var root = MapBuilder.Root;
            yield return new WaitForSeconds(0.5f);
            Vector3 Ground(float x, float z) => new Vector3(x, MapBuilder.Height(x, z), z);
            float YawTo(Vector3 from, Vector3 to) => Quaternion.LookRotation(new Vector3(to.x - from.x, 0, to.z - from.z)).eulerAngles.y;
            float PitchTo(Vector3 eye, Vector3 to) { var d = to - eye; return Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; }
            int shot = 0;
            IEnumerator LookShot(string name, Vector3 eyeAt, Vector3 target, float secs = 0.7f)
            {
                var feet = eyeAt - Vector3.up * Cfg.EyeHeight;
                float yaw = YawTo(eyeAt, target), pitch = -PitchTo(eyeAt, target);
                float until = Time.time + secs;
                while (Time.time < until) { pc.LocalTeleport(feet, yaw); pc.SetLook(yaw, pitch); yield return null; }
                pc.LocalTeleport(feet, yaw);
                pc.SetLook(yaw, pitch);
                string file = $"scenery_{map}_{shot++:00}_{name}.png";
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, file));
                Log("shot " + file);
                yield return null;
                yield return null;
            }
            Vector3 Eye(float x, float z, float up = 0f) => Ground(x, z) + Vector3.up * (Cfg.EyeHeight + up);
            var ball = Ball.Instance;
            var bp = ball != null ? ball.transform.position : Ground(0, 0) + Vector3.up * 0.5f;
            var cs = CrashSite.Current;

            // ---------------- the middle: the crash's dirt and rubble, no yellow circle ----------------
            int yellow = 0;
            bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                if (Art.IsArtMat(r.sharedMaterial, out var c) && (Near(c, new Color(0.85f, 0.75f, 0.3f)) || Near(c, new Color(0.95f, 0.88f, 0.45f)))) yellow++;
            Check(yellow == 0, $"no yellow ball zone circle any more ({yellow} yellow parts)");
            var dirt = new List<Renderer>();
            var rubble = new List<Renderer>();
            int rubbleColliders = 0;
            if (cs != null)
            {
                foreach (var r in cs.GetComponentsInChildren<Renderer>())
                {
                    if (r.name.StartsWith("ground dirt")) dirt.Add(r);
                    if (r.name.StartsWith("rubble ")) rubble.Add(r);
                }
                foreach (var col in cs.GetComponentsInChildren<Collider>()) if (col != cs.Collider) rubbleColliders++;
            }
            float dirtReach = 0f;
            foreach (var r in dirt) dirtReach = Mathf.Max(dirtReach, r.bounds.extents.x, r.bounds.extents.z);
            Check(dirt.Count > 0 && dirtReach > 10f, $"churned dirt over the ball zone ({dirt.Count} patches, the big one {dirtReach:0.0} m round)");
            int rubbleTris = 0;
            foreach (var r in rubble) rubbleTris += r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
            Check(rubble.Count >= 6 && rubbleTris > 1500, $"rubble round the crash ({rubble.Count} merged meshes, {rubbleTris} triangles)");
            Check(rubbleColliders == 0, $"nothing at the crash site is solid but the saucer ({rubbleColliders} other colliders)");
            // nothing of the rubble between any base and the ball (temporary colliders on its meshes)
            {
                var temp = new List<MeshCollider>();
                foreach (var r in rubble) { var mc = r.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh; temp.Add(mc); }
                Physics.SyncTransforms();
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int blocked = 0, rays = 0;
                    foreach (float far in new[] { 10f, 16f, 24f, 40f })
                        foreach (float side in new[] { -0.6f, 0f, 0.6f })
                        {
                            var back = Cfg.BackDir(t);
                            var eye = back * far + Vector3.Cross(Vector3.up, back) * side;
                            eye.y = MapBuilder.Height(eye.x, eye.z) + Cfg.EyeHeight;
                            foreach (var target in new[] { bp, bp + Vector3.up * 0.3f })
                            {
                                rays++;
                                foreach (var h in Physics.RaycastAll(eye, (target - eye).normalized, (target - eye).magnitude - Ball.Radius - 0.05f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                                    if (temp.Contains(h.collider as MeshCollider)) { blocked++; break; }
                            }
                        }
                    Check(blocked == 0, $"{Cfg.TeamName[t]}: no rubble between its side and the ball ({blocked} / {rays} lines blocked)");
                }
                // and the way along the ground from each base to the ball: only gravel (nothing over 25 cm up)
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    var back = Cfg.BackDir(t);
                    float worst = 0f;
                    for (float d = 2.5f; d < 22f; d += 0.25f)
                        foreach (float side in new[] { -0.5f, 0f, 0.5f })
                        {
                            var p = back * d + Vector3.Cross(Vector3.up, back) * side;
                            float gy = MapBuilder.Height(p.x, p.z);
                            if (Physics.Raycast(new Vector3(p.x, gy + 3f, p.z), Vector3.down, out var h, 3.2f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore) && temp.Contains(h.collider as MeshCollider))
                                worst = Mathf.Max(worst, h.point.y - gy);
                        }
                    Check(worst < 0.25f, $"{Cfg.TeamName[t]}: the way in from its side to the ball is clear (rubble at most {worst:0.00} m high on it)");
                }
                foreach (var mc in temp) Destroy(mc);
            }
            // pictures (the glass wall and dome up)
            for (int t = 0; t < Mathf.Min(2, Cfg.TeamCount); t++)
            {
                var back = Cfg.BackDir(t);
                yield return LookShot($"centre_from_{Cfg.TeamName[t].ToLower()}", Eye(back.x * 24f, back.z * 24f), bp);
                yield return LookShot($"centre_from_{Cfg.TeamName[t].ToLower()}_high", Eye(back.x * 26f, back.z * 26f, 9f), bp);
            }
            {
                var side = Quaternion.Euler(0, 90f, 0) * Cfg.BackDir(team);
                yield return LookShot("centre_edge", Eye(side.x * 19f + 4f, side.z * 19f), Ground(side.x * 8f, side.z * 8f));
                yield return LookShot("centre_from_above", new Vector3(3f, MapBuilder.Height(0, 0) + 32f, -14f * Mathf.Sign(Cfg.BaseCenter[team].z + 0.01f)), Ground(0, 0));
                var rim = CrashSite.Dir * -7f + side * 3f;
                yield return LookShot("centre_rim_close", Eye(rim.x, rim.z), Ground(rim.x * 0.3f + side.x * 2f, rim.z * 0.3f + side.z * 2f));
            }
            // ---------------- the emergency flare tip by the glass wall (build phase, every mode) ----------------
            {
                var spot = Tutorial.WallSpot(team);
                var back = Cfg.BackDir(team);
                var feet = Ground(spot.x, spot.z);
                float yaw = YawTo(feet, Vector3.zero);
                float until = Time.time + 1.2f;
                while (Time.time < until) { pc.LocalTeleport(feet, yaw); pc.SetLook(yaw, -4f); yield return null; }
                Check(FlareTip.Wanted(me.transform.position) && FlareTip.Showing, $"walking up to the glass wall in the middle shows the EMERGENCY FLARE tip ({FlareTip.WallDistance(me.transform.position):0.0} m from the wall)");
                string file = $"scenery_{map}_{shot++:00}_flare_tip.png";
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, file));
                Log("shot " + file);
                yield return null; yield return null;
                var far = Ground(back.x * 40f, back.z * 40f);
                until = Time.time + 1.2f;
                while (Time.time < until) { pc.LocalTeleport(far, yaw); yield return null; }
                Check(!FlareTip.Wanted(me.transform.position) && !FlareTip.Showing, "away from the wall the tip goes");
            }
            // the base floors (with their grid) are there while you build
            int floorsUp = 0;
            foreach (var f in MapBuilder.BaseFloors) if (f != null && f.activeInHierarchy) floorsUp++;
            Check(Cfg.Builder || floorsUp == Cfg.TeamCount, $"the tinted base floors with their grid are there while you build ({floorsUp})");

            // ---------------- the walls drop: they slide down into the ground ----------------
            {
                var back = Cfg.BackDir(team);
                var side = Quaternion.Euler(0, 90f, 0) * back;
                var eye = Eye(back.x * 34f + side.x * 30f, back.z * 34f + side.z * 30f, 2f);
                float yaw = YawTo(eye, Vector3.zero), pitch = -PitchTo(eye, new Vector3(0, MapBuilder.Height(0, 0) + 14f, 0));
                pc.LocalTeleport(eye - Vector3.up * Cfg.EyeHeight, yaw);
                pc.SetLook(yaw, pitch);
                yield return new WaitForSeconds(0.3f);
            }
            var glassGo = root.Find("GlassWall");
            float glassY0 = glassGo != null ? glassGo.position.y : 0f;
            g.TimerPaused.Value = false;
            g.DevSkipPhase(GameState.PreBall);
            while (g.S != GameState.BallLive) yield return null;
            g.TimerPaused.Value = true;
            {
                yield return null;
                int solid = 0;
                if (glassGo != null) foreach (var c in glassGo.GetComponentsInChildren<Collider>()) if (c.enabled) solid++;
                Check(MapBuilder.GlassDropping && !MapBuilder.GlassUp && glassGo != null && glassGo.gameObject.activeSelf && solid == 0,
                    $"the wall is dropping (still there, sliding, not solid: {solid} colliders on; counts as down)");
                Check(FlareTip.Wanted(Vector3.zero) == false, "no flare tip once the wall's dropping");
                int floors = 0;
                foreach (var f in MapBuilder.BaseFloors) if (f != null && f.activeInHierarchy) floors++;
                Check(floors == 0, $"the tinted base floors and their grid went with the build phase ({floors} left)");
                float t0 = Time.time, lastY = glassY0;
                bool down = true;
                int pic = 0;
                foreach (float at in new[] { 0.3f, 1.4f, 2.4f, 3.3f })
                {
                    while (Time.time - t0 < at) yield return null;
                    float y = glassGo.position.y;
                    if (at > 1f && y > lastY - 0.5f) down = false;
                    lastY = y;
                    string file = $"scenery_{map}_{shot++:00}_wall_dropping_{pic++}.png";
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, file));
                    Log($"shot {file} (wall {y - glassY0:0.0} m)");
                    yield return null;
                }
                Check(down, $"the wall slides down into the ground ({lastY - glassY0:0.0} m down by {Time.time - t0:0.0} s)");
                float stop = Time.time + 6f;
                while (Time.time < stop && MapBuilder.GlassDropping) yield return null;
                Check(!MapBuilder.GlassDropping && glassGo != null && !glassGo.gameObject.activeSelf, $"the wall has gone into the ground and is switched off ({Time.time - t0:0.0} s)");
                // our base now: flat ground inside the team-colour border
                var bc = Cfg.BaseCenter[team];
                var outDir = new Vector3(bc.x, 0, bc.z).normalized;
                var corner = bc + Quaternion.Euler(0, 35f, 0) * -outDir * (Cfg.BaseHalf + 8f);
                yield return LookShot("base_after_wall", Eye(corner.x, corner.z, 6f), Ground(bc.x, bc.z));
            }
            yield return new WaitForSeconds(0.5f);
            {
                var back = Cfg.BackDir(team);
                yield return LookShot("centre_open", Eye(back.x * 15f + 3f, back.z * 15f), Ground(0, 0) + Vector3.up * 0.3f);
            }

            // ---------------- the mountain ranges ----------------
            int ranges = MapScenery.Ranges.Count;
            Check(ranges == MapScenery.RangeCount && ranges >= 2, $"{ranges} layers of mountain ranges");
            float lastTop = 0f, lastDist = 0f, nearestArena = float.MaxValue, nearestMap = float.MaxValue;
            bool taller = true, further = true;
            for (int i = 0; i < ranges; i++)
            {
                var r = MapScenery.Ranges[i];
                if (r == null) { taller = false; continue; }
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var vs = mesh.vertices;
                float top = float.MinValue, dist = 0f;
                foreach (var v in vs)
                {
                    var w = r.transform.TransformPoint(v);
                    top = Mathf.Max(top, w.y);
                    dist += new Vector2(w.x, w.z).magnitude / vs.Length;
                    nearestArena = Mathf.Min(nearestArena, (w - Cfg.ArenaCenter).magnitude);
                    nearestMap = Mathf.Min(nearestMap, new Vector2(w.x, w.z).magnitude);
                }
                Log($"range {i}: {mesh.triangles.Length / 3} triangles, peaks up to {top:0} m, {dist:0} m out on average");
                if (top <= lastTop) taller = false;
                if (dist <= lastDist) further = false;
                lastTop = top; lastDist = dist;
            }
            Check(taller && further, "each range is further out and taller than the one in front of it");
            Check(nearestMap > MapDome.HalfSize * 1.3f, $"the ranges are well outside the dome (nearest {nearestMap:0} m from the middle, dome {MapDome.HalfSize:0} m)");
            Check(nearestArena > SpaceArena.ShellRadius + 20f, $"the ranges stay out of the space arena's shell (nearest {nearestArena:0} m from the arena, shell {SpaceArena.ShellRadius} m)");
            {
                var bc = Cfg.BaseCenter[team];
                var outDir = new Vector3(bc.x, 0, bc.z).normalized;
                var mid = Ground(bc.x * 0.5f, bc.z * 0.5f);
                foreach (float turn in new[] { 0f, 60f, 120f, 180f })
                {
                    var look = Quaternion.Euler(0, turn, 0) * outDir;
                    var eye = mid + Vector3.up * Cfg.EyeHeight;
                    yield return LookShot($"mountains_{turn:0}", eye, eye + look * 300f + Vector3.up * 30f);
                }
                var high = new Vector3(bc.x * 0.3f, MapBuilder.Height(0, 0) + 40f, bc.z * 0.3f);
                yield return LookShot("mountains_from_high", high, high + Quaternion.Euler(0, 40f, 0) * outDir * 300f + Vector3.up * 10f);
                var baseEye = Ground(bc.x, bc.z) + Vector3.up * (Cfg.BaseY + Cfg.EyeHeight);
                yield return LookShot("mountains_from_base_back", baseEye, baseEye + outDir * 300f + Vector3.up * 35f);
            }

            // ---------------- forests on the ranges, rocks and trees out past the edge ----------------
            {
                int treeTris = 0;
                foreach (var r in MapScenery.RangeTrees) if (r != null) treeTris += r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                Check(MapScenery.RangeTrees.Count >= 2 && treeTris > 4000, $"forests on the nearer ranges ({MapScenery.RangeTrees.Count} meshes, {treeTris} triangles)");
                Check(MapScenery.OutskirtRocks > 60 && MapScenery.OutskirtTrees > 30, $"rocks and trees out past the edge of the map ({MapScenery.OutskirtRocks} rocks, {MapScenery.OutskirtTrees} trees)");
                float inside = float.MaxValue;
                int cols = 0;
                foreach (var r in MapScenery.Outskirts)
                {
                    if (r == null) continue;
                    cols += r.GetComponentsInChildren<Collider>().Length;
                    foreach (var v in r.GetComponent<MeshFilter>().sharedMesh.vertices)
                    {
                        var w = r.transform.TransformPoint(v);
                        inside = Mathf.Min(inside, Mathf.Max(Mathf.Abs(w.x), Mathf.Abs(w.z)));
                    }
                }
                foreach (var r in MapScenery.RangeTrees) if (r != null) cols += r.GetComponentsInChildren<Collider>().Length;
                Check(inside > half + 2.5f && cols == 0, $"they're all outside the map's edge and dome ({inside - half:0.0} m past the edge at the nearest), no colliders ({cols})");
                var bc = Cfg.BaseCenter[team];
                var outDir = new Vector3(bc.x, 0, bc.z).normalized;
                var side = Quaternion.Euler(0, 90f, 0) * outDir;
                var near = Ground(bc.x + outDir.x * 14f + side.x * 30f, bc.z + outDir.z * 14f + side.z * 30f);
                var edgeLook = near + outDir * 60f;
                yield return LookShot("outskirts", near + Vector3.up * Cfg.EyeHeight, new Vector3(edgeLook.x, MapBuilder.Height(edgeLook.x, edgeLook.z) + 6f, edgeLook.z));
                yield return LookShot("outskirts_side", near + Vector3.up * Cfg.EyeHeight, near + (outDir + side * 1.2f).normalized * 120f + Vector3.up * 25f);
                yield return LookShot("range_forests", near + Vector3.up * (Cfg.EyeHeight + 25f), near + Quaternion.Euler(0, -30f, 0) * outDir * 260f + Vector3.up * 35f);
            }
            // ---------------- Highlands: the rocky rise at the edge of the map is faceted (flat-shaded) ----------------
            if (Cfg.Map == MapKind.Highlands)
            {
                var gGo = root.Find("Ground");
                var gm = gGo != null ? gGo.GetComponent<MeshFilter>().sharedMesh : null;
                if (gm != null)
                {
                    var vs = gm.vertices; var ns = gm.normals;
                    int edgeTris = 0, smooth = 0, inTris = 0, inFlat = 0;
                    for (int sub = 0; sub < gm.subMeshCount; sub++)
                    {
                        var tris = gm.GetTriangles(sub);
                        for (int i = 0; i < tris.Length; i += 3)
                        {
                            var c = (vs[tris[i]] + vs[tris[i + 1]] + vs[tris[i + 2]]) / 3f;
                            bool sameN = Vector3.Dot(ns[tris[i]], ns[tris[i + 1]]) > 0.9999f && Vector3.Dot(ns[tris[i]], ns[tris[i + 2]]) > 0.9999f;
                            if (MapBuilder.EdgeRise(c.x, c.z)) { edgeTris++; if (!sameN) smooth++; }
                            else if (Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.z)) < half - 30f && MapBuilder.RockField(c.x, c.z) > 0.1f) { inTris++; if (sameN) inFlat++; }
                        }
                    }
                    Check(edgeTris > 1000 && smooth == 0, $"the rocky rise at the edge is faceted ({edgeTris} triangles, {smooth} smooth-shaded)");
                    Check(inTris == 0 || inFlat < inTris / 2, $"the hills inside stay smooth ({inFlat} of {inTris} rock triangles flat)");
                }
                var bc = Cfg.BaseCenter[team];
                var outDir = new Vector3(bc.x, 0, bc.z).normalized;
                var side = Quaternion.Euler(0, 90f, 0) * outDir;
                // where the glass wall meets the edge of the map
                var wallEnd = side * (half - 30f) + outDir * 12f;
                yield return LookShot("edge_rise_wall_end", Eye(wallEnd.x, wallEnd.z, 2f), Ground(side.x * (half + 6f), side.z * (half + 6f)) + Vector3.up * 8f);
                var by = Ground(bc.x * 0.6f + side.x * 30f, bc.z * 0.6f + side.z * 30f);
                yield return LookShot("edge_rise", by + Vector3.up * Cfg.EyeHeight, Ground(side.x * (half + 4f) + outDir.x * 30f, side.z * (half + 4f) + outDir.z * 30f) + Vector3.up * 10f);
            }
            // ---------------- Highlands: planets in the sky ----------------
            {
                var pl = SkyPlanets.Current;
                if (Cfg.Map == MapKind.Highlands)
                {
                    Check(pl != null && pl.Count == 3 && pl.Shown, $"big planets in the Highlands sky ({(pl != null ? pl.Count : 0)})");
                    if (pl != null)
                    {
                        int cols = 0;
                        foreach (var r in pl.Renderers) cols += r.GetComponentsInChildren<Collider>().Length;
                        // (the airdrop ships come in from 300 m up and 72 m out over the hover spot - over the dome, under 75 m up -
                        // and leave 60 m up / 48 m out: nothing of theirs is ever further than this from anyone on the map)
                        float shipReach = new Vector2(MapDome.HalfSize * 2.83f + 72f, 300f + 75f).magnitude; // (from one corner to a ship over the other)
                        Check(cols == 0 && pl.NearestReach > shipReach, $"the planets are sky: no colliders ({cols}), never nearer than {pl.NearestReach:0} m (the ships stay within {shipReach:0} m)");
                        float lowest = 90f;
                        for (int i = 0; i < pl.Count; i++) lowest = Mathf.Min(lowest, Mathf.Asin(pl.DirOf(i).y) * Mathf.Rad2Deg);
                        Check(lowest > 15f, $"they're high in the sky ({lowest:0} degrees up at the lowest)");
                        var eye = Eye(Cfg.BaseCenter[team].x * 0.5f, Cfg.BaseCenter[team].z * 0.5f);
                        for (int i = 0; i < pl.Count; i++) yield return LookShot($"planet_{i}", eye, eye + pl.DirOf(i) * 100f);
                        var d0 = pl.DirOf(0);
                        yield return LookShot("planets_wide", eye, eye + new Vector3(d0.x, 0.12f, d0.z).normalized * 100f);
                    }
                }
                else Check(pl == null, "no planets on Plains");
            }
            // ---------------- clouds never pop: they shrink away before they wrap round ----------------
            {
                CloudLayer cl = null;
                foreach (var c in root.GetComponentsInChildren<CloudLayer>()) cl = c;
                if (cl != null && cl.Clouds.Count > 0)
                {
                    var c0 = cl.Clouds[0];
                    var p0 = c0.localPosition;
                    c0.localPosition = new Vector3(cl.Extent - 0.08f, p0.y, 0f);
                    float before = float.MaxValue, after = float.MaxValue, mid = 0f;
                    float until = Time.time + 1f;
                    while (Time.time < until && c0.localPosition.x > 0f) { before = c0.localScale.x; yield return null; }
                    after = c0.localScale.x;
                    c0.localPosition = new Vector3(0f, p0.y, 0f);
                    yield return null;
                    mid = c0.localScale.x;
                    c0.localPosition = p0;
                    Check(before < mid * 0.01f && after < mid * 0.01f && mid > 1f, $"a cloud is shrunk to nothing as it wraps round (size {before:0.000} before, {after:0.000} after, {mid:0.0} over the map)");
                    // and no cloud over the map is shrunk (the edge is far out over the mountains)
                    Check(cl.Fade(new Vector3(MapDome.HalfSize * 1.5f, 0, MapDome.HalfSize * 1.5f)) > 0.99f, "clouds over and round the map are full size");
                }
                else Check(false, "clouds");
            }

            // ---------------- the boulders ----------------
            int boulders = MapScenery.Boulders.Count;
            Check(boulders >= 4 && boulders % Cfg.Copies == 0, $"{boulders} big boulders about the map (the same in every team's part)");
            int badSpot = 0, notSolid = 0, nodesIn = 0;
            for (int i = 0; i < boulders; i++)
            {
                var p = MapScenery.Boulders[i];
                float r = MapScenery.BoulderRadius[i];
                var flat = new Vector2(p.x, p.z);
                bool ok = flat.magnitude > 26f + r && Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) < half - r - 5f;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    var c = Cfg.BaseCenter[t];
                    if (Mathf.Abs(p.x - c.x) < Cfg.BaseHalf + r + 6f && Mathf.Abs(p.z - c.z) < Cfg.BaseHalf + r + 6f) ok = false;
                    var bc = new Vector2(c.x, c.z);
                    float along = Mathf.Clamp01(Vector2.Dot(flat - bc, -bc) / bc.sqrMagnitude);
                    if ((bc + (-bc) * along - flat).magnitude < r + 7f) ok = false;
                }
                // (the glass walls between the halves: z = 0, or the two diagonals)
                float wall = Cfg.FourWay ? Mathf.Min(Mathf.Abs(p.x - p.z), Mathf.Abs(p.x + p.z)) / Mathf.Sqrt(2f) : Mathf.Abs(p.z);
                if (wall < r + 4f) ok = false;
                if (!ok) { badSpot++; Log($"boulder at {p} (r {r:0.0}) is in the way"); }
                bool solid = false;
                foreach (var h in Physics.OverlapSphere(p + Vector3.up * 1f, 0.6f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                    if (h.GetComponentInParent<GroundMarker>() == null) solid = true;
                if (!solid) notSolid++;
                foreach (var n in ResourceNode.All)
                    if (n != null && (new Vector2(n.transform.position.x, n.transform.position.z) - flat).magnitude < r + 0.5f) nodesIn++;
            }
            Check(badSpot == 0, $"no boulder in a base, by a spawn, in the middle, on the way between them or by a wall ({badSpot} in the way)");
            Check(notSolid == 0, $"the boulders are solid ({notSolid} not)");
            Check(nodesIn == 0, $"no tree, bush or stone grows inside a boulder ({nodesIn})");
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var sp = Cfg.SpawnPos(t);
                bool clear = true;
                foreach (var h in Physics.OverlapCapsule(sp + Vector3.up * 0.5f, sp + Vector3.up * 1.6f, 0.5f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                    if (h.GetComponentInParent<MapSceneryMarker>() != null) clear = false;
                Check(clear, $"{Cfg.TeamName[t]}'s spawn is clear");
            }
            // pictures: the nearest few to our base, close up, and from above
            {
                var bc = Cfg.BaseCenter[team];
                var order = new List<int>();
                for (int i = 0; i < boulders; i++) order.Add(i);
                order.Sort((a, b) => (MapScenery.Boulders[a] - bc).sqrMagnitude.CompareTo((MapScenery.Boulders[b] - bc).sqrMagnitude));
                for (int k = 0; k < Mathf.Min(3, order.Count); k++)
                {
                    var p = MapScenery.Boulders[order[k]];
                    float r = MapScenery.BoulderRadius[order[k]];
                    var toBase = new Vector3(bc.x - p.x, 0, bc.z - p.z).normalized;
                    var at = p + Quaternion.Euler(0, 25f, 0) * toBase * (r * 2f + 6f);
                    yield return LookShot($"boulder_{k}", Eye(at.x, at.z), p + Vector3.up * r * 0.4f);
                }
                var over = new Vector3(bc.x * 0.55f + 25f, MapBuilder.Height(0, 0) + 75f, bc.z * 0.55f);
                yield return LookShot("boulders_overview", over, Ground(bc.x * 0.45f, bc.z * 0.45f));
            }

            // ---------------- Highlands: the rock on the hill faces ----------------
            if (Cfg.Map == MapKind.Highlands)
            {
                var groundGo = root.Find("Ground");
                var mesh = groundGo != null ? groundGo.GetComponent<MeshFilter>().sharedMesh : null;
                if (mesh != null && mesh.subMeshCount == 2)
                {
                    var vs = mesh.vertices;
                    foreach (int sub in new[] { 1, 0 })
                    {
                        // patches of one kind joined edge to edge (corners in the same place count as joined)
                        var tris = mesh.GetTriangles(sub);
                        var key = new Dictionary<Vector3Int, int>();
                        int Key(Vector3 v) { var k = Vector3Int.RoundToInt(v * 100f); if (!key.TryGetValue(k, out int i)) key[k] = i = key.Count; return i; }
                        var parent = new List<int>();
                        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                        var ids = new int[tris.Length];
                        for (int i = 0; i < tris.Length; i++) { ids[i] = Key(vs[tris[i]]); while (parent.Count <= ids[i]) parent.Add(parent.Count); }
                        for (int i = 0; i < tris.Length; i += 3) { int a = Find(ids[i]); parent[Find(ids[i + 1])] = a; parent[Find(ids[i + 2])] = a; }
                        var area = new Dictionary<int, float>();
                        for (int i = 0; i < tris.Length; i += 3)
                        {
                            int rt = Find(ids[i]);
                            area.TryGetValue(rt, out float s);
                            area[rt] = s + Vector3.Cross(vs[tris[i + 1]] - vs[tris[i]], vs[tris[i + 2]] - vs[tris[i]]).magnitude * 0.5f;
                        }
                        int specks = 0;
                        float total = 0f;
                        foreach (var a in area.Values) { total += a; if (a < 30f) specks++; }
                        Log($"terrain {(sub == 1 ? "rock" : "grass")}: {area.Count} patches, {total:0} m2, {specks} under 30 m2");
                        Check(specks == 0 || sub == 0 && specks <= 2, $"no specks of {(sub == 1 ? "rock on the grass" : "grass in the rock")} ({specks} patches under 30 m2)");
                    }
                    // no grass growing on the rock
                    var gf = GrassField.Current;
                    if (gf != null)
                    {
                        int onRock = 0, rockSpots = 0;
                        for (float x = -half; x < half; x += 1.7f)
                            for (float z = -half; z < half; z += 1.7f)
                                if (MapBuilder.RockField(x, z) > 0.06f) { rockSpots++; if (gf.CoverAt(x, z) > 0.05f) onRock++; }
                        Check(onRock == 0, $"no grass on the rock faces ({onRock} of {rockSpots} rock spots have some)");
                    }
                }
                else Check(false, "the Highlands ground has its grass and rock");
                // how steep the hills inside the map are (smoothed, as the rock is picked), and how much was rock the old
                // way (each 2 m triangle on its own steeper than 0.75)
                {
                    var buckets = new[] { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f };
                    var counts = new int[buckets.Length];
                    int all = 0, oldRock = 0, rockNow = 0;
                    for (float x = -half + 16f; x < half - 16f; x += 2f)
                        for (float z = -half + 16f; z < half - 16f; z += 2f)
                        {
                            if (Cfg.BaseTeamAt(new Vector3(x, 0, z)) >= 0 || new Vector2(x, z).magnitude < 27f) continue;
                            all++;
                            float s = MapBuilder.RockField(x, z) + MapBuilder.RockSlope;
                            for (int k = 0; k < buckets.Length; k++) if (s > buckets[k]) counts[k]++;
                            if (MapBuilder.RockField(x, z) >= 0f) rockNow++;
                            float h = MapBuilder.Height(x, z), hx = MapBuilder.Height(x + 2f, z), hz = MapBuilder.Height(x, z + 2f);
                            if (new Vector3(-(hx - h) / 2f, 1f, -(hz - h) / 2f).normalized.y < 0.8f) oldRock++;
                        }
                    var sb = new System.Text.StringBuilder();
                    for (int k = 0; k < buckets.Length; k++) sb.Append($" >{buckets[k]:0.0}: {100f * counts[k] / Mathf.Max(1, all):0.0}%");
                    Log($"hills inside the map, smoothed steepness{sb}; rock now {100f * rockNow / Mathf.Max(1, all):0.0}%, the old way {100f * oldRock / Mathf.Max(1, all):0.0}%");
                }
                // pictures of the hilliest spots on our side, grass off and on
                var bc = Cfg.BaseCenter[team];
                var spots = new List<Vector3>();
                var rnd = new System.Random(5);
                for (int k = 0; k < 2000 && spots.Count < 3; k++)
                {
                    var p = new Vector3(((float)rnd.NextDouble() * 2f - 1f) * (half - 20f), 0, Mathf.Sign(bc.z + 0.01f) * (float)rnd.NextDouble() * (half - 20f));
                    if (Cfg.BaseTeamAt(p) >= 0 || MapBuilder.RockField(p.x, p.z) < 0.05f) continue;
                    bool near = false;
                    foreach (var s in spots) if ((s - p).magnitude < 45f) near = true;
                    if (!near) spots.Add(p);
                }
                var gf2 = GrassField.Current;
                for (int pass = 0; pass < 2; pass++)
                {
                    if (gf2 != null) { gf2.ForceOff = pass == 0; gf2.Refresh(); }
                    string tag = pass == 0 ? "nograss" : "grass";
                    for (int k = 0; k < spots.Count; k++)
                    {
                        var p = Ground(spots[k].x, spots[k].z);
                        var from = p + new Vector3(p.x, 0, p.z).normalized * -20f + Vector3.Cross(Vector3.up, new Vector3(p.x, 0, p.z).normalized) * 8f;
                        yield return LookShot($"hills_{k}_{tag}", Eye(from.x, from.z, 1f), p + Vector3.up * 2f);
                    }
                    if (spots.Count > 0)
                    {
                        var p = Ground(spots[0].x, spots[0].z);
                        yield return LookShot($"hills_from_above_{tag}", p + new Vector3(-18f, 30f, -18f), p);
                    }
                }
                if (gf2 != null) { gf2.ForceOff = false; gf2.Refresh(); }
            }

            // ---------------- frame time: the new scenery shown / hidden ----------------
            {
                var scenery = new List<Renderer>();
                foreach (var r in MapScenery.Ranges) if (r != null) foreach (var rr in r.transform.parent.GetComponentsInChildren<Renderer>()) if (!scenery.Contains(rr)) scenery.Add(rr);
                var bh = root.Find("Boulders");
                if (bh != null) scenery.AddRange(bh.GetComponentsInChildren<Renderer>());
                scenery.AddRange(rubble);
                foreach (var r in dirt) scenery.Add(r);
                var bc = Cfg.BaseCenter[team];
                var eye = Ground(bc.x * 0.8f, bc.z * 0.8f) + Vector3.up * Cfg.EyeHeight;
                var feet = eye - Vector3.up * Cfg.EyeHeight;
                float yaw = YawTo(eye, Vector3.zero);
                pc.LocalTeleport(feet, yaw);
                pc.SetLook(yaw, 2f);
                yield return new WaitForSeconds(0.5f);
                int vsync = QualitySettings.vSyncCount, fr = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000;
                float on = 0, off = 0; int onN = 0, offN = 0;
                for (int pass = 0; pass < 6; pass++)
                {
                    bool show = pass % 2 == 0;
                    foreach (var r in scenery) if (r) r.enabled = show;
                    yield return new WaitForSeconds(0.4f);
                    float until = Time.realtimeSinceStartup + 1.5f;
                    while (Time.realtimeSinceStartup < until)
                    {
                        pc.LocalTeleport(feet, yaw);
                        yield return null;
                        if (show) { on += Time.unscaledDeltaTime; onN++; } else { off += Time.unscaledDeltaTime; offN++; }
                    }
                }
                foreach (var r in scenery) if (r) r.enabled = true;
                QualitySettings.vSyncCount = vsync; Application.targetFrameRate = fr;
                Log($"frame time with the new scenery (ranges, boulders, crash dirt and rubble: {scenery.Count} renderers) {on / Mathf.Max(1, onN) * 1000f:F2} ms, hidden {off / Mathf.Max(1, offN) * 1000f:F2} ms");
            }

            // ---------------- PSX and AI PSX ----------------
            foreach (int mode in new[] { 1, 2 })
            {
                GameSettings.SetGraphics(mode, false);
                yield return new WaitForSeconds(1f);
                string tag = mode == 1 ? "psx" : "aipsx";
                var back = Cfg.BackDir(team);
                yield return LookShot($"{tag}_centre", Eye(back.x * 20f, back.z * 20f, 3f), bp);
                var bc = Cfg.BaseCenter[team];
                var mid = Ground(bc.x * 0.5f, bc.z * 0.5f) + Vector3.up * Cfg.EyeHeight;
                yield return LookShot($"{tag}_mountains", mid, mid + new Vector3(bc.x, 0, bc.z).normalized * 300f + Vector3.up * 30f);
                if (boulders > 0)
                {
                    var p = MapScenery.Boulders[0];
                    float r = MapScenery.BoulderRadius[0];
                    var at = p + new Vector3(-p.x, 0, -p.z).normalized * (r * 2f + 6f);
                    yield return LookShot($"{tag}_boulder", Eye(at.x, at.z), p + Vector3.up * r * 0.4f);
                }
            }
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.6f);
            Log("scenery test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
