using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest nodes -host -solo -rules classic -map plains|highlands -shotdir DIR: the resource nodes up close
    /// (`nodes_*.png`) - the bigger stone nodes, the glowing X (post processing off and on), the hit marks on the bark (every
    /// one checked to sit on the bark you see, not in it), a felled tree's stump, the fallen logs (their X never on the
    /// underside, always on the side facing the hitter, never in the bark; harvesting one gives wood), the smooth, bigger
    /// berry bushes, and the tutorial's markers (only your own team's trees and bushes).
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator NodesRoutine(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.6f);
            var cam = Camera.main;
            int team = me.Team.Value;
            var bc = Cfg.BaseCenter[team];
            Vector3 Ground(float x, float z) => new Vector3(x, MapBuilder.Height(x, z), z);
            int shot = 0;
            IEnumerator Look(Vector3 feet, Vector3 at, string name, float fov = 70f)
            {
                pc.LocalTeleport(feet, 0f);
                var eye = feet + Vector3.up * Cfg.EyeHeight;
                var d = at - eye;
                pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                yield return new WaitForSeconds(0.35f);
                cam.fieldOfView = fov;
                yield return null; yield return null;
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"nodes_{shot++:00}_{name}.png"));
                yield return null;
                cam.fieldOfView = 70f;
            }
            // from `dist` m away from `p`, on `side` (a direction), the camera at eye height looking at `at`
            Vector3 From(Vector3 p, Vector3 side, float dist) { side.y = 0f; side.Normalize(); return Ground(p.x + side.x * dist, p.z + side.z * dist); }
            List<ResourceNode> Of(byte kind)
            {
                var l = new List<ResourceNode>();
                foreach (var n in ResourceNode.All) if (n != null && n.IsSpawned && n.Kind.Value == kind && n.Amount.Value > 0) l.Add(n);
                l.Sort((a, b) => (a.transform.position - bc).sqrMagnitude.CompareTo((b.transform.position - bc).sqrMagnitude));
                return l;
            }
            var toMid = new Vector3(-bc.x, 0, -bc.z).normalized;

            // ---------------- 1. the stone nodes are bigger ----------------
            var rocks = Of(ResourceNode.Boulder);
            Check(rocks.Count > 0, $"stone nodes on the map ({rocks.Count})");
            if (rocks.Count > 0)
            {
                var r = rocks[0];
                var b = new Bounds(r.transform.position, Vector3.zero);
                foreach (var rr in r.GetComponentsInChildren<Renderer>()) if (rr.enabled && rr.name == "rock") b.Encapsulate(rr.bounds);
                Log($"stone node: {b.size.x:F1} x {b.size.y:F1} x {b.size.z:F1} m (x{ResourceNode.BoulderSize} what it was)");
                Check(Mathf.Max(b.size.x, b.size.z) > 3.8f, $"the stone nodes are bigger ({Mathf.Max(b.size.x, b.size.z):F1} m across)");
                var p = r.transform.position;
                yield return Look(From(p, toMid, 8f), p + Vector3.up * 1f, "stone_node");
                // a hit shows the star on it, still on the rock
                r.Spot.Value = 2;
                yield return new WaitForSeconds(0.3f);
                Check(r.TryGetSpot(out var sp, out _) && Vector3.Distance(sp, p) < 4f, "the stone node's star is on the bigger rock");
                r.Spot.Value = ResourceNode.NoSpot;
            }

            // ---------------- 2. the X glows ----------------
            var trees = Of(ResourceNode.Tree);
            var pines = new List<ResourceNode>();
            foreach (var t in trees) if (t.transform.Find("visual/needles") != null && Cfg.BaseTeamAt(t.transform.position) < 0) pines.Add(t);
            Check(pines.Count >= 3, $"pines to test ({pines.Count})");
            if (pines.Count == 0) { Application.Quit(0); yield break; }
            {
                var t = pines[0];
                var side = toMid;
                // the spot facing `side`
                var ls = Quaternion.Inverse(t.transform.rotation) * side;
                t.Spot.Value = (byte)(Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(ls.z, ls.x) * Mathf.Rad2Deg, 360f) / 30f) % 12);
                yield return new WaitForSeconds(0.4f);
                Transform marker = null;
                foreach (var tt in t.GetComponentsInChildren<Transform>(true)) if (tt.name == "x") marker = tt;
                int glowBars = 0, halos = 0;
                if (marker != null)
                    foreach (var mr in marker.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null && mr.sharedMaterial.shader.name == "RockGame/Glow") glowBars++;
                        if (mr.name == "x halo") halos++;
                    }
                Check(glowBars == 2 && halos == 1, $"the X glows: {glowBars} glowing bars (RockGame/Glow), {halos} halo");
                var g = ResourceNode.GlowMat;
                Check(g != null && g.GetFloat("_Intensity") > 1f, "the X is HDR bright (past the bloom threshold)");
                t.TryGetSpot(out var xs, out var xn);
                bool post = GameSettings.PostFx;
                foreach (bool on in new[] { false, true })
                {
                    GameSettings.SetPostFx(on, false);
                    yield return new WaitForSeconds(0.3f);
                    yield return Look(Ground(xs.x + xn.x * 3f, xs.z + xn.z * 3f), xs, on ? "x_glow_postfx_on" : "x_glow_postfx_off");
                    yield return Look(Ground(xs.x + xn.x * 3f, xs.z + xn.z * 3f), xs, (on ? "x_glow_postfx_on" : "x_glow_postfx_off") + "_zoom", 18f);
                }
                // in the shade of the needles it's just as bright: from further off, too
                yield return Look(Ground(xs.x + xn.x * 12f, xs.z + xn.z * 12f), xs, "x_glow_from_12m");
                GameSettings.SetPostFx(post, false);
                t.Spot.Value = ResourceNode.NoSpot;
            }

            // ---------------- 6. hit marks sit on the bark ----------------
            {
                int made = 0, inBark = 0, floating = 0, oldIn = 0, tested = 0;
                float worstGap = 0f;
                foreach (var t in pines.GetRange(0, Mathf.Min(3, pines.Count)))
                {
                    var tris = WorldTris(t.transform.Find("visual/needles"), 1);
                    var axis = t.transform.position;
                    var col = t.transform.Find("visual/trunk").GetComponent<Collider>();
                    for (int a = 0; a < 12; a++)
                        foreach (float y in new[] { 0.6f, 1.2f, 1.9f })
                        {
                            var d = t.transform.rotation * new Vector3(Mathf.Cos(a * 30f * Mathf.Deg2Rad + 0.13f), 0, Mathf.Sin(a * 30f * Mathf.Deg2Rad + 0.13f));
                            if (!col.Raycast(new Ray(axis + Vector3.up * y + d * 4f, -d), out var hit, 8f)) continue;
                            tested++;
                            // the old way: a mark lying flat on the collider where the hit landed
                            var oldPts = BoxCorners(Matrix4x4.TRS(hit.point + hit.normal * 0.004f, Quaternion.LookRotation(-hit.normal), Vector3.one), new Vector3(0.17f, 0.05f, 0.006f));
                            foreach (var q in oldPts) if (InsideTrunk(tris, axis, q)) { oldIn++; break; }
                            if (!t.AddHitMark(hit.point)) continue;
                            made++;
                            var m = t.LastHitMark;
                            bool any = false;
                            foreach (Transform box in m)
                                foreach (var q in BoxCorners(box.localToWorldMatrix, Vector3.one))
                                    if (InsideTrunk(tris, axis, q)) any = true;
                            if (any) inBark++;
                            // and not floating off it: the bark is right behind its middle
                            float gap = RayToTris(tris, m.position, m.forward);
                            if (gap < 0f || gap > 0.05f) floating++;
                            worstGap = Mathf.Max(worstGap, gap);
                        }
                }
                Log($"hit marks: {made} made of {tested} hits, {inBark} with a corner in the bark, {floating} floating off it (worst gap {worstGap * 100f:F1} cm); the old way (flat on the collider) {oldIn} of {tested} were in the bark");
                Check(made >= tested - 2 && made > 20, $"every hit on a pine leaves a mark ({made} of {tested})");
                Check(inBark == 0, $"no hit mark goes into the bark ({inBark} of {made} do)");
                Check(floating == 0, $"the hit marks sit on the bark, not off it ({floating} of {made} more than 5 cm off)");
                Check(pines[0].HitMarkCount <= 10, $"a tree keeps its last ten marks ({pines[0].HitMarkCount})");
                // a real hit's chips leave one too (Fx: every peer that sees the hit)
                var t0 = pines[1 % pines.Count];
                int before = t0.HitMarkCount;
                var d0 = (Ground(bc.x, bc.z) - t0.transform.position); d0.y = 0; d0.Normalize();
                var col0 = t0.transform.Find("visual/trunk").GetComponent<Collider>();
                if (col0.Raycast(new Ray(t0.transform.position + Vector3.up * 1.3f + d0 * 4f, -d0), out var h0, 8f))
                {
                    Fx.Play(FxKind.WoodChips, h0.point, h0.normal);
                    Check(t0.HitMarkCount == Mathf.Min(10, before + 1), $"a hit's chips leave a mark on the tree ({before} -> {t0.HitMarkCount})");
                }
                // photographs: a tree's marks from the front, the side and zoomed
                var tm = pines[2 % pines.Count];
                var mk = tm.LastHitMark;
                if (mk != null)
                {
                    var mn = -mk.forward;
                    yield return Look(From(mk.position, mn, 2.2f), mk.position, "hit_marks");
                    yield return Look(From(mk.position, mn, 2.2f), mk.position, "hit_marks_zoom", 22f);
                    yield return Look(From(mk.position, Quaternion.Euler(0, 55f, 0) * mn, 1.6f), mk.position, "hit_marks_side_zoom", 22f);
                }
            }

            // ---------------- 4. a felled tree leaves a stump ----------------
            {
                var t = pines[Mathf.Min(3, pines.Count - 1)];
                var tp = t.transform.position;
                Check(t.Stump != null && !t.Stump.activeSelf, "a standing tree's stump is hidden");
                t.ServerDeplete();
                yield return new WaitForSeconds(0.5f);
                var vis = t.transform.Find("visual");
                Check(t.Stump != null && t.Stump.activeInHierarchy && (vis == null || !vis.gameObject.activeSelf), "a felled tree leaves its stump (and the tree's gone)");
                Check(t.HitMarkCount == 0, "its hit marks went with it");
                Check(t.Stump != null && t.Stump.GetComponentInChildren<Collider>() != null, "the stump is solid");
                if (t.Stump != null)
                {
                    var sb = new Bounds(tp, Vector3.zero);
                    foreach (var r in t.Stump.GetComponentsInChildren<Renderer>()) sb.Encapsulate(r.bounds);
                    Log($"stump: {sb.size.x:F2} x {sb.size.y:F2} x {sb.size.z:F2} m");
                    Check(sb.size.y > 0.5f && sb.size.y < 1.4f && sb.size.x > 1f, $"the stump is a short, trunk-thick log ({sb.size.x:F2} m across, {sb.max.y - tp.y:F2} m up)");
                }
                yield return Look(From(tp, toMid, 3.2f), tp + Vector3.up * 0.3f, "stump");
                yield return Look(From(tp, Quaternion.Euler(0, 120f, 0) * toMid, 2.2f), tp + Vector3.up * 0.2f, "stump_close");
                t.ServerRegrow();
                yield return new WaitForSeconds(0.4f);
                Check(t.Stump != null && !t.Stump.activeSelf && vis != null && vis.gameObject.activeSelf, "the stump's gone again once the tree grows back");
                // PSX graphics: a stump too
                GameSettings.SetGraphics(1, false);
                yield return new WaitForSeconds(0.6f);
                t.ServerDeplete();
                yield return new WaitForSeconds(0.4f);
                Check(t.Stump != null && t.Stump.activeInHierarchy, "PSX graphics: a felled tree leaves a stump too");
                yield return Look(From(tp, toMid, 3.5f), tp + Vector3.up * 0.3f, "stump_psx");
                t.ServerRegrow();
                GameSettings.SetGraphics(0, false);
                yield return new WaitForSeconds(0.6f);
            }

            // ---------------- 5. fallen logs ----------------
            {
                var logs = Of(ResourceNode.Log);
                Log($"fallen logs: {logs.Count}");
                Check(logs.Count >= 2 * Cfg.Copies, $"fallen logs round the map ({logs.Count})");
                int spots = 0, under = 0, inLog = 0, notFacing = 0, picks = 0;
                float lowest = 1f;
                foreach (var l in logs)
                {
                    var lm = l.transform.Find("visual/log/log mesh");
                    if (lm == null) { Check(false, "a log has its mesh"); continue; }
                    var tris = WorldTris(lm, 0);
                    tris.AddRange(WorldTris(lm, 1));
                    for (int s = 0; s < 12; s++)
                    {
                        l.Spot.Value = (byte)s;
                        yield return null;
                        yield return null;
                        if (!l.TryGetSpot(out var sp, out var sn)) continue;
                        spots++;
                        lowest = Mathf.Min(lowest, sn.y);
                        if (sn.y < 0.3f) under++;
                        Transform marker = null;
                        foreach (var tt in l.GetComponentsInChildren<Transform>(true)) if (tt.name == "x") marker = tt;
                        if (marker != null)
                        {
                            // (at its biggest pulse, once it's popped in - the pop when it moves is a quick flash)
                            var mm = Matrix4x4.TRS(marker.position, marker.rotation, Vector3.one * 1.08f);
                            foreach (Transform bar in marker)
                            {
                                if (bar.name != "box") continue;
                                foreach (var q in BoxCorners(mm * Matrix4x4.TRS(bar.localPosition, bar.localRotation, bar.localScale), Vector3.one)) if ((Crossings(tris, q, new Vector3(0.123f, 0.951f, 0.284f).normalized) & 1) == 1) { inLog++; break; }
                            }
                        }
                    }
                    // a hit from anywhere round it moves the X to the side facing the hitter (or the top)
                    var lp = l.transform.position;
                    for (int k = 0; k < 8; k++)
                    {
                        var hitter = Ground(lp.x + Mathf.Cos(k * Mathf.PI / 4f) * 3.2f, lp.z + Mathf.Sin(k * Mathf.PI / 4f) * 3.2f);
                        l.Spot.Value = l.TestPickSpot(hitter);
                        yield return null;
                        if (!l.TryGetSpot(out var sp, out var sn)) continue;
                        picks++;
                        if (Vector3.Dot(sn, (hitter + Vector3.up * 1.5f - sp).normalized) < 0.05f) notFacing++;
                    }
                    l.Spot.Value = ResourceNode.NoSpot;
                }
                Log($"log X: {spots} spots shown, the lowest faces {Mathf.Acos(Mathf.Clamp(lowest, -1f, 1f)) * Mathf.Rad2Deg:F0} degrees from straight up; {under} face the ground, {inLog} bars in the log; {notFacing} of {picks} picked spots not facing the hitter");
                Check(spots >= logs.Count * 12 - 2, $"every log spot is shown ({spots})");
                Check(under == 0, $"a log's X is never on its underside ({under} spots face the ground)");
                Check(inLog == 0, $"a log's X never goes into the log ({inLog})");
                Check(picks > 0 && notFacing == 0, $"a hit moves a log's X to the side facing the hitter ({notFacing} of {picks} don't)");
                if (logs.Count > 0)
                {
                    var l = logs[0];
                    var lp = l.transform.position;
                    var across = l.transform.forward;
                    var standAt = Ground(lp.x + across.x * 3f, lp.z + across.z * 3f);
                    l.Spot.Value = l.TestPickSpot(standAt);
                    yield return new WaitForSeconds(0.4f);
                    yield return Look(standAt, lp + Vector3.up * 0.4f, "log_side");
                    yield return Look(Ground(lp.x - across.x * 4f + l.transform.right.x * 4f, lp.z - across.z * 4f + l.transform.right.z * 4f), lp + Vector3.up * 0.4f, "log_other_side");
                    yield return Look(Ground(lp.x + l.transform.right.x * 4.5f, lp.z + l.transform.right.z * 4.5f), lp + Vector3.up * 0.4f, "log_end");
                    // hit marks on a log
                    var lm = l.transform.Find("visual/log/log mesh");
                    var ltris = WorldTris(lm, 0);
                    int lmade = 0, lin = 0;
                    for (int k = -2; k <= 2; k++)
                    {
                        var pt = lp + l.transform.right * k * 0.6f + Vector3.up * 0.9f + across * 0.5f;
                        var lc = lm.GetComponent<Collider>();
                        var cp = lc.ClosestPoint(pt);
                        if (!l.AddHitMark(cp)) continue;
                        lmade++;
                        foreach (Transform box in l.LastHitMark)
                            foreach (var q in BoxCorners(box.localToWorldMatrix, Vector3.one)) if ((Crossings(ltris, q, (across + Vector3.up).normalized) & 1) == 1) { lin++; break; }
                    }
                    Check(lmade >= 4 && lin == 0, $"hits on a log leave marks on its bark ({lmade} made, {lin} in the log)");
                    yield return Look(standAt, lp + Vector3.up * 0.5f, "log_hit_marks", 35f);
                    // harvesting it gives wood
                    int amt = l.Amount.Value;
                    int got = l.ServerHarvest(20, false, standAt);
                    Check(got == 20 && l.Amount.Value == amt - 20 && l.Yield == Item.Wood && l.IsWood, $"a fallen log gives wood ({got}, {l.Amount.Value} of {l.MaxAmount} left)");
                    l.ServerRegrow();
                    // and from up close
                    var over = Ground(lp.x + across.x * 6f + l.transform.right.x * 3f, lp.z + across.z * 6f + l.transform.right.z * 3f);
                    yield return Look(over, lp, "log_wide");
                }
            }

            // ---------------- 3. smooth, bigger berry bushes ----------------
            {
                var bushes = Of(ResourceNode.Bush);
                Check(bushes.Count > 0, $"berry bushes ({bushes.Count})");
                if (bushes.Count > 0)
                {
                    var bsh = bushes[0];
                    var mf = bsh.transform.Find("visual/bush")?.GetComponent<MeshFilter>();
                    if (mf != null)
                    {
                        var m = mf.sharedMesh;
                        var v = m.vertices; var nr = m.normals; var tr = m.GetTriangles(0);
                        int smooth = 0, all = 0;
                        for (int i = 0; i < tr.Length; i += 3)
                        {
                            var fn = Vector3.Cross(v[tr[i + 1]] - v[tr[i]], v[tr[i + 2]] - v[tr[i]]).normalized;
                            for (int c = 0; c < 3; c++) { all++; if (Vector3.Angle(fn, nr[tr[i + c]]) > 2f) smooth++; }
                        }
                        var bb = m.bounds;
                        Log($"bush: {tr.Length / 3} leaf triangles, {smooth * 100f / Mathf.Max(1, all):F0}% of corners smoothed, {bb.size.x:F2} x {bb.size.y:F2} x {bb.size.z:F2} m");
                        Check(all > 0 && smooth > all * 0.6f, $"the berry bushes are smooth-shaded ({smooth * 100f / Mathf.Max(1, all):F0}% of corners)");
                        Check(bb.size.y > 1.05f && bb.size.x > 1.3f, $"the berry bushes are bigger ({bb.size.x:F2} m across, {bb.size.y:F2} m high)");
                    }
                    else Check(false, "the Normal berry bush mesh is there");
                    var bp = bsh.transform.position;
                    yield return Look(From(bp, toMid, 3f), bp + Vector3.up * 0.5f, "bush");
                    yield return Look(From(bp, toMid, 2f), bp + Vector3.up * 0.5f, "bush_zoom", 35f);
                }
            }

            // ---------------- 7. the tutorial's markers stay on your own side ----------------
            {
                var enemy = Cfg.BaseCenter[(team + 1) % Cfg.TeamCount];
                foreach (var at in new[] { Ground(bc.x * 0.5f, bc.z * 0.5f), Ground(enemy.x * 0.6f, enemy.z * 0.6f) })
                {
                    pc.LocalTeleport(at, 0f);
                    yield return new WaitForSeconds(0.2f);
                    var tree = Tutorial.Nearest(ResourceNode.Tree);
                    var bush = Tutorial.Nearest(ResourceNode.Bush);
                    bool own = (!tree.HasValue || Cfg.RegionOf(tree.Value) == team) && (!bush.HasValue || Cfg.RegionOf(bush.Value) == team);
                    Check(tree.HasValue && own, $"the tutorial marks only your own side's trees and bushes (standing {(Cfg.RegionOf(at) == team ? "on our side" : "on the enemy's side")}: tree {(tree.HasValue ? Cfg.RegionOf(tree.Value).ToString() : "none")}, bush {(bush.HasValue ? Cfg.RegionOf(bush.Value).ToString() : "none")}, we're {team})");
                }
            }

            cam.fieldOfView = 70f;
            Log("nodes test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>Submesh `sub` of a mesh's triangles, in world space (three corners each).</summary>
        static List<Vector3> WorldTris(Transform t, int sub)
        {
            var l = new List<Vector3>();
            var mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount <= sub) return l;
            var v = mf.sharedMesh.vertices;
            var tr = mf.sharedMesh.GetTriangles(sub);
            var m = t.localToWorldMatrix;
            foreach (int i in tr) l.Add(m.MultiplyPoint3x4(v[i]));
            return l;
        }

        /// <summary>The eight corners of a box (a unit cube under `m`, sized `size`).</summary>
        static List<Vector3> BoxCorners(Matrix4x4 m, Vector3 size)
        {
            var l = new List<Vector3>(8);
            for (int c = 0; c < 8; c++) l.Add(m.MultiplyPoint3x4(Vector3.Scale(new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f), size)));
            return l;
        }

        static int Crossings(List<Vector3> tris, Vector3 p, Vector3 d)
        {
            int hits = 0;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = tris[i], e1 = tris[i + 1] - a, e2 = tris[i + 2] - a;
                var pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                var tv = p - a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < 0f || u > 1f) continue;
                var qv = Vector3.Cross(tv, e1);
                float w = Vector3.Dot(d, qv) * inv;
                if (w < 0f || u + w > 1f) continue;
                if (Vector3.Dot(e2, qv) * inv > 1e-5f) hits++;
            }
            return hits;
        }

        /// <summary>How far along d from p the first triangle is (-1: none).</summary>
        static float RayToTris(List<Vector3> tris, Vector3 p, Vector3 d)
        {
            float best = float.MaxValue;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = tris[i], e1 = tris[i + 1] - a, e2 = tris[i + 2] - a;
                var pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                var tv = p - a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < 0f || u > 1f) continue;
                var qv = Vector3.Cross(tv, e1);
                float w = Vector3.Dot(d, qv) * inv;
                if (w < 0f || u + w > 1f) continue;
                float t = Vector3.Dot(e2, qv) * inv;
                if (t > 0f && t < best) best = t;
            }
            return best == float.MaxValue ? -1f : best;
        }

        /// <summary>Inside a pine's trunk: rays out from the point (away from the axis, tipped up and down) cross the bark an
        /// odd number of times (2 of 3; the trunk is open at the bottom).</summary>
        static bool InsideTrunk(List<Vector3> tris, Vector3 axis, Vector3 p)
        {
            var o = new Vector3(p.x - axis.x, 0, p.z - axis.z).normalized;
            int odd = 0;
            foreach (var d in new[] { o, (o + Vector3.up * 0.3f).normalized, (o - Vector3.up * 0.3f + Vector3.Cross(Vector3.up, o) * 0.2f).normalized })
                if ((Crossings(tris, p, d) & 1) == 1) odd++;
            return odd >= 2;
        }
    }
}
