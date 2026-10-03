using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Part of -autotest world (or just this with -trees-only): the Normal pines up close, hunting for trunk that shows
    /// through the needles. Every tree is checked on the CPU first: the needles and the bark are moved exactly like the
    /// Painted shader moves them (WorldLook.PaintedWind) at many moments of the wind, and every bit of bark above the
    /// lowest tier's underside has to be inside a tier of needles. Then a few trees (the shortest, the tallest, one of each
    /// kind) are photographed from eye height (1.6 m) at 3 and 7 m from six sides, looking up from under them, while
    /// they shake from a hit and from far away - each as a normal picture and a zoomed one (the camera's own lens
    /// narrowed, from the same spot) on where the trunk meets the top tiers. The wind is frozen at the moment of the
    /// biggest gust for that tree (Time.timeScale 0) so every picture is the worst case.
    /// </summary>
    public partial class AutoTest
    {
        /// <summary>The needles (submesh 0) and the bark that's drawn (the pine's own submesh 1, or the old trunk cylinder),
        /// in world space after the wind at time t: how much of the bark above the lowest underside is outside every tier.</summary>
        static float TrunkExposure(ResourceNode n, float t, out string info)
        {
            info = "";
            var needles = n.transform.Find("visual/needles");
            if (needles == null) { info = "no needles"; return 0f; }
            var mf = needles.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) { info = "no mesh"; return 0f; }
            var verts = mesh.vertices;
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            var l2w = needles.localToWorldMatrix;
            var root = needles.position;
            var wv = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var ws = l2w.MultiplyPoint3x4(verts[i]);
                wv[i] = WorldLook.PaintedWind(root, ws, i < uvs.Count ? uvs[i] : Vector2.zero, t);
            }
            // the tiers: closed shells of needle triangles (joined where they share corners)
            var tris = mesh.GetTriangles(0);
            int triN = tris.Length / 3;
            var parent = new int[triN];
            for (int i = 0; i < triN; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
            var byCorner = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < triN; i++)
                for (int c = 0; c < 3; c++)
                {
                    var v = verts[tris[i * 3 + c]];
                    var key = new Vector3Int(Mathf.RoundToInt(v.x * 1000f), Mathf.RoundToInt(v.y * 1000f), Mathf.RoundToInt(v.z * 1000f));
                    if (byCorner.TryGetValue(key, out int o)) parent[Find(i)] = Find(o); else byCorner[key] = i;
                }
            var shells = new Dictionary<int, List<int>>();
            for (int i = 0; i < triN; i++) { int s = Find(i); if (!shells.TryGetValue(s, out var l)) shells[s] = l = new List<int>(); l.Add(i); }
            // the lowest underside centre (a corner of a shell right on the axis)
            float yCb = float.MaxValue;
            var axis = needles.position;
            for (int i = 0; i < verts.Length; i++)
            {
                var ws = l2w.MultiplyPoint3x4(verts[i]);
                if (new Vector2(ws.x - axis.x, ws.z - axis.z).sqrMagnitude < 0.0001f) yCb = Mathf.Min(yCb, wv[i].y);
            }
            // the bark that's drawn
            var bark = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            if (mesh.subMeshCount > 1)
            {
                var bt = mesh.GetTriangles(1);
                for (int i = 0; i < bt.Length; i += 3) bark.Add((wv[bt[i]], wv[bt[i + 1]], wv[bt[i + 2]]));
            }
            var trunkT = n.transform.Find("visual/trunk");
            var tr = trunkT != null ? trunkT.GetComponent<MeshRenderer>() : null;
            if (tr != null && tr.enabled)
            {
                var tm = trunkT.GetComponent<MeshFilter>().sharedMesh;
                var tv = tm.vertices;
                var tt = tm.triangles;
                var tw = trunkT.localToWorldMatrix;
                for (int i = 0; i < tt.Length; i += 3) bark.Add((tw.MultiplyPoint3x4(tv[tt[i]]), tw.MultiplyPoint3x4(tv[tt[i + 1]]), tw.MultiplyPoint3x4(tv[tt[i + 2]])));
            }
            // (a point is inside a closed shell if a ray from it crosses the shell an odd number of times; two rays, so a
            // ray that happens to run exactly through an edge can't call a hidden point outside)
            var dirA = new Vector3(0.123f, 0.951f, 0.284f).normalized;
            var dirB = new Vector3(-0.311f, 0.902f, -0.157f).normalized;
            bool Inside(Vector3 p, List<int> shell) => Odd(p, shell, dirA) || Odd(p, shell, dirB);
            bool Odd(Vector3 p, List<int> shell, Vector3 dir)
            {
                int hits = 0;
                foreach (int ti in shell)
                {
                    Vector3 a = wv[tris[ti * 3]], b = wv[tris[ti * 3 + 1]], c = wv[tris[ti * 3 + 2]];
                    var e1 = b - a; var e2 = c - a;
                    var pv = Vector3.Cross(dir, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float inv = 1f / det;
                    var tv2 = p - a;
                    float u = Vector3.Dot(tv2, pv) * inv;
                    if (u < 0f || u > 1f) continue;
                    var qv = Vector3.Cross(tv2, e1);
                    float v = Vector3.Dot(dir, qv) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    if (Vector3.Dot(e2, qv) * inv > 1e-5f) hits++;
                }
                return (hits & 1) == 1;
            }
            var boxes = new Dictionary<int, Bounds>();
            foreach (var kv in shells)
            {
                var bb = new Bounds(wv[tris[kv.Value[0] * 3]], Vector3.zero);
                foreach (int ti in kv.Value) for (int c = 0; c < 3; c++) bb.Encapsulate(wv[tris[ti * 3 + c]]);
                boxes[kv.Key] = bb;
            }
            int total = 0, outside = 0;
            float worstY = float.MinValue, worstR = 0f;
            foreach (var (a, b, c) in bark)
            {
                float len = Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude));
                int k = Mathf.Clamp(Mathf.CeilToInt(len / 0.08f), 1, 40);
                for (int i = 0; i <= k; i++)
                for (int j = 0; i + j <= k; j++)
                {
                    var p = a + (b - a) * (i / (float)k) + (c - a) * (j / (float)k);
                    if (p.y < yCb + 0.03f) continue;
                    total++;
                    bool hidden = false;
                    foreach (var kv in shells) if (boxes[kv.Key].Contains(p) && Inside(p, kv.Value)) { hidden = true; break; }
                    if (!hidden && p.y - n.transform.position.y > worstY) { worstY = p.y - n.transform.position.y; worstR = new Vector2(p.x - axis.x, p.z - axis.z).magnitude; }
                    if (!hidden) outside++;
                }
            }
            info = $"{shells.Count} tiers, {bark.Count} bark tris, {total} bark points in the crown, {outside} outside the needles" + (outside > 0 ? $" (highest at {worstY:F2} m, {worstR:F2} m from the axis)" : "");
            return total > 0 ? outside / (float)total : 0f;
        }

        /// <summary>When the gust over this tree is at its strongest next (shader time, = Time.timeSinceLevelLoad).</summary>
        static float NextGustPeak(Vector3 root, float now)
        {
            // gust = sin(t * 0.9 + x * 0.06 + z * 0.045): the peak is where that's pi/2 (+ 2 pi k)
            float ph = root.x * 0.06f + root.z * 0.045f;
            float k = Mathf.Ceil(((now + 0.5f) * 0.9f + ph - Mathf.PI * 0.5f) / (Mathf.PI * 2f));
            return (Mathf.PI * 0.5f + k * Mathf.PI * 2f - ph) / 0.9f;
        }

        IEnumerator TreeCloseups(PlayerController pc, string dir, System.Func<float, float, Vector3> ground)
        {
            // ---- every tree on the CPU: no bark outside the needles at any moment of the wind ----
            var trees = new List<ResourceNode>();
            foreach (var n in ResourceNode.All)
                if (n != null && n.Kind.Value == ResourceNode.Tree && n.Amount.Value > 0 && n.transform.Find("visual/needles") != null) trees.Add(n);
            int exposedTrees = 0;
            float worst = 0f;
            string worstInfo = "";
            float now = Time.timeSinceLevelLoad;
            foreach (var n in trees)
            {
                float treeWorst = 0f;
                string treeInfo = "";
                for (int s = 0; s < 16; s++)
                {
                    float t = now + s * 0.47f; // (over a whole gust, and the flutter at many phases)
                    float e = TrunkExposure(n, t, out var info);
                    if (e > treeWorst || s == 0) { treeWorst = Mathf.Max(treeWorst, e); treeInfo = info; }
                }
                if (treeWorst > 0f) exposedTrees++;
                if (treeWorst >= worst) { worst = treeWorst; worstInfo = $"{n.name} at {n.transform.position}: {treeInfo}"; }
            }
            Log($"trunk exposure over 16 moments of the wind: {exposedTrees} of {trees.Count} trees show bark outside the needles, worst {worst * 100f:F1}% ({worstInfo})");
            Check(trees.Count > 0 && exposedTrees == 0, $"no tree trunk pokes out of its needles at any moment of the wind ({exposedTrees} of {trees.Count} trees do, worst {worst * 100f:F2}% of the crown's bark)");

            // ---- photographs ----
            float Height(ResourceNode n)
            {
                var r = n.transform.Find("visual/needles").GetComponent<Renderer>();
                return r.bounds.max.y - n.transform.position.y;
            }
            var bc = Cfg.BaseCenter[PlayerNet.Local.Team.Value];
            var pick = new List<ResourceNode>();
            var wild = trees.FindAll(n => Cfg.BaseTeamAt(n.transform.position) < 0 && n.transform.position.magnitude > 16f);
            wild.Sort((a, b) => Height(a).CompareTo(Height(b)));
            if (wild.Count > 0)
            {
                pick.Add(wild[0]);
                pick.Add(wild[wild.Count - 1]);
                if (wild.Count > 3) pick.Add(wild[wild.Count / 2]);
            }
            // one of each kind of pine
            var kinds = new HashSet<int>();
            foreach (var n in pick) kinds.Add(ResourceNode.PineVariant(n.Seed.Value));
            foreach (var n in wild)
                if (kinds.Add(ResourceNode.PineVariant(n.Seed.Value))) pick.Add(n);

            var cam = Camera.main;
            int shot = 0;
            void Snap(string name) => ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"trees_{shot++:000}_{name}.png"));
            IEnumerator Frame() { yield return null; yield return null; yield return new WaitForEndOfFrame(); }
            IEnumerator Look(Vector3 feet, Vector3 at, string name, float zoomSpan)
            {
                pc.LocalTeleport(feet, 0f);
                var eye = feet + Vector3.up * Cfg.EyeHeight;
                var d = at - eye;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                pc.SetLook(yaw, pitch);
                cam.fieldOfView = 70f;
                yield return Frame();
                Snap(name);
                yield return Frame();
                if (zoomSpan > 0f)
                {
                    cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(zoomSpan * 0.5f / d.magnitude) * Mathf.Rad2Deg, 5f, 50f);
                    yield return Frame();
                    Snap(name + "_zoom");
                    yield return Frame();
                    cam.fieldOfView = 70f;
                }
            }

            foreach (var n in pick)
            {
                var tp = n.transform.position;
                float h = Height(n);
                int kind = ResourceNode.PineVariant(n.Seed.Value);
                var lay = ResourceNode.PineShapeOf(n.Seed.Value);
                // where the trunk crosses into the top tiers (the most likely place for it to show)
                float junction = lay.JunctionY;
                Log($"tree {pick.IndexOf(n)}: kind {kind} ({ResourceNode.PineVariantName(kind)}), {h:F1} m tall at {tp}, junction {junction:F2} m, exposure check: {TrunkExposure(n, Time.timeSinceLevelLoad, out var inf) * 100f:F1}% ({inf})");
                // wait for this tree's strongest gust and stop time there
                float peak = NextGustPeak(n.transform.Find("visual/needles").position, Time.timeSinceLevelLoad);
                while (Time.timeSinceLevelLoad < peak) yield return null;
                Time.timeScale = 0f;
                string tag = $"t{pick.IndexOf(n)}_k{kind}_{h:F0}m";
                var at = tp + Vector3.up * junction;
                for (int a = 0; a < 6; a++)
                {
                    float ang = a * 60f * Mathf.Deg2Rad;
                    var off = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    foreach (float dist in new[] { 3f, 7f })
                    {
                        var f = ground(tp.x + off.x * dist, tp.z + off.z * dist);
                        yield return Look(f, at, $"{tag}_side{a}_{dist:F0}m", 2.2f);
                    }
                }
                // looking up from under it, and from far away
                for (int a = 0; a < 3; a++)
                {
                    float ang = (a * 120f + 30f) * Mathf.Deg2Rad;
                    var f = ground(tp.x + Mathf.Cos(ang) * 1.2f, tp.z + Mathf.Sin(ang) * 1.2f);
                    yield return Look(f, tp + Vector3.up * (h * 0.75f), $"{tag}_under{a}", 0f);
                }
                var away = new Vector3(bc.x - tp.x, 0, bc.z - tp.z).normalized;
                var far = ground(tp.x + away.x * 40f, tp.z + away.z * 40f);
                yield return Look(far, tp + Vector3.up * (h * 0.55f), $"{tag}_far40m", h * 1.2f);
                Time.timeScale = 1f;
            }
            // shaking from a hit (the trunk and the needles have to move together)
            if (pick.Count > 0)
            {
                var n = pick[0];
                var tp = n.transform.position;
                float junction = ResourceNode.PineShapeOf(n.Seed.Value).JunctionY;
                var f = ground(tp.x - 3f, tp.z);
                pc.LocalTeleport(f, 90f);
                yield return null;
                n.ServerHarvest(1, false, f);
                yield return null;
                yield return null;
                Time.timeScale = 0f;
                yield return Look(f, tp + Vector3.up * junction, "shake_side", 2.2f);
                Time.timeScale = 1f;
            }
            Time.timeScale = 1f;
            cam.fieldOfView = 70f;
            yield return SmoothShadingCheck(trees);
            yield return TreeXCheck(pc, dir, ground, trees, pick);
            yield return FallingLeavesCheck(pc, dir, ground, pick);
        }

        /// <summary>The pines (and the clouds) are smooth-shaded: most corners' normals are bent off their own face's.</summary>
        IEnumerator SmoothShadingCheck(List<ResourceNode> trees)
        {
            float Smooth(Mesh m, int sub)
            {
                var v = m.vertices; var nr = m.normals; var t = m.GetTriangles(sub);
                int bent = 0;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                    for (int k = 0; k < 3; k++) if (Vector3.Angle(nr[t[i + k]], fn) > 3f) bent++;
                }
                return t.Length > 0 ? bent / (float)t.Length : 0f;
            }
            float needles = 1f, bark = 1f;
            foreach (var n in trees)
            {
                var m = n.transform.Find("visual/needles").GetComponent<MeshFilter>().sharedMesh;
                needles = Mathf.Min(needles, Smooth(m, 0));
                bark = Mathf.Min(bark, Smooth(m, 1));
            }
            float cloud = 1f;
            var cl = MapBuilder.Root != null ? MapBuilder.Root.GetComponentInChildren<CloudLayer>() : null;
            if (cl != null) foreach (var mf in cl.GetComponentsInChildren<MeshFilter>()) cloud = Mathf.Min(cloud, Smooth(mf.sharedMesh, 0));
            Log($"smooth shading: least share of corners bent off their face's normal - needles {needles * 100f:F0}%, bark {bark * 100f:F0}%, clouds {cloud * 100f:F0}%");
            Check(trees.Count > 0 && needles > 0.5f && bark > 0.5f, $"the pines are smooth-shaded (needles {needles * 100f:F0}%, bark {bark * 100f:F0}% of corners smoothed)");
            Check(cl != null && cloud > 0.5f, $"the clouds are smooth-shaded ({cloud * 100f:F0}% of corners smoothed)");
            yield break;
        }

        /// <summary>
        /// The weak spot X sits on the bark you see, never in it: every spot of every pine is shown in turn, and every corner
        /// of the X's two bars (at its biggest pulse) has to be outside the trunk's mesh. The old placement (on the round
        /// collider, which the ten-sided bark stands out past at its corners) is measured the same way, to compare. Then
        /// the worst old spot on a few trees is photographed from the front and from the sides, zoomed in.
        /// </summary>
        IEnumerator TreeXCheck(PlayerController pc, string dir, System.Func<float, float, Vector3> ground, List<ResourceNode> trees, List<ResourceNode> pick)
        {
            var cam = Camera.main;
            // the bark in world space (the trunk doesn't sway below the needles)
            List<Vector3> Bark(ResourceNode n)
            {
                var needles = n.transform.Find("visual/needles");
                var mesh = needles.GetComponent<MeshFilter>().sharedMesh;
                var v = mesh.vertices; var t = mesh.GetTriangles(1);
                var l = new List<Vector3>();
                var m = needles.localToWorldMatrix;
                for (int i = 0; i < t.Length; i += 3) { l.Add(m.MultiplyPoint3x4(v[t[i]])); l.Add(m.MultiplyPoint3x4(v[t[i + 1]])); l.Add(m.MultiplyPoint3x4(v[t[i + 2]])); }
                return l;
            }
            int Crossings(List<Vector3> tris, Vector3 p, Vector3 d)
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
            // inside the trunk: rays out from the point (away from the axis, and tipped up and down) cross the bark an odd number of times (2 of 3)
            bool Inside(List<Vector3> tris, Vector3 axis, Vector3 p)
            {
                var o = new Vector3(p.x - axis.x, 0, p.z - axis.z).normalized;
                int odd = 0;
                foreach (var d in new[] { o, (o + Vector3.up * 0.3f).normalized, (o - Vector3.up * 0.3f + Vector3.Cross(Vector3.up, o) * 0.2f).normalized })
                    if ((Crossings(tris, p, d) & 1) == 1) odd++;
                return odd >= 2;
            }
            // the corners (and back middles) of the X's two bars, for an X at this position / rotation at its biggest pulse
            List<Vector3> XPoints(Transform marker, Vector3 pos, Quaternion rot)
            {
                var l = new List<Vector3>();
                var m = Matrix4x4.TRS(pos, rot, Vector3.one * 1.08f);
                foreach (Transform bar in marker)
                {
                    if (bar.GetComponent<MeshFilter>() == null || bar.name == "x decal" || bar.name == "x halo") continue;
                    var bm = m * Matrix4x4.TRS(bar.localPosition, bar.localRotation, bar.localScale);
                    for (int c = 0; c < 8; c++) l.Add(bm.MultiplyPoint3x4(new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f)));
                    l.Add(bm.MultiplyPoint3x4(new Vector3(0, 0, 0.5f)));
                }
                return l;
            }
            int shown = 0, badNew = 0, badOld = 0;
            float worstOld = 0f, nearest = float.MaxValue, farthest = 0f;
            var worstSpot = new Dictionary<ResourceNode, (int spot, float depth)>();
            yield return new WaitForSeconds(0.6f); // (the tree hit above has stopped shaking)
            var fails = new List<string>();
            for (int s = 0; s < 12; s++)
            {
                foreach (var n in trees) n.Spot.Value = (byte)s;
                yield return null;
                yield return null;
                foreach (var n in trees)
                {
                    Transform marker = null;
                    foreach (var t in n.GetComponentsInChildren<Transform>(true)) if (t.name == "x") marker = t;
                    if (marker == null || !marker.gameObject.activeInHierarchy || !n.TryGetSpot(out var spot, out var normal)) continue;
                    shown++;
                    var axis = n.transform.position;
                    var tris = Bark(n);
                    int inNew = 0;
                    foreach (var q in XPoints(marker, marker.position, marker.rotation)) if (Inside(tris, axis, q)) inNew++;
                    float r = new Vector2(marker.position.x - axis.x, marker.position.z - axis.z).magnitude;
                    if (inNew > 0)
                    {
                        badNew++;
                        if (fails.Count < 8) fails.Add($"tree {trees.IndexOf(n)} spot {s}: {inNew} corners in, the X {r:F3} m out, its visual at {n.transform.Find("visual").localPosition}");
                    }
                    nearest = Mathf.Min(nearest, r); farthest = Mathf.Max(farthest, r);
                    // the old way: a ray onto the round collider, the X 1.5 cm off it
                    var col = n.transform.Find("visual/trunk").GetComponent<Collider>();
                    float y = 0.8f + (s % 3) * 0.35f;
                    var wd = n.transform.rotation * new Vector3(Mathf.Cos(s * 30f * Mathf.Deg2Rad), 0, Mathf.Sin(s * 30f * Mathf.Deg2Rad));
                    if (col.Raycast(new Ray(axis + Vector3.up * y + wd * 4f, -wd), out var hit, 8f))
                    {
                        int inOld = 0;
                        var oldPts = XPoints(marker, hit.point + hit.normal * 0.015f, Quaternion.LookRotation(-hit.normal));
                        foreach (var q in oldPts) if (Inside(tris, axis, q)) inOld++;
                        if (inOld > 0) badOld++;
                        worstOld = Mathf.Max(worstOld, inOld / (float)oldPts.Count);
                        // (how much room there is between the old X's middle and the bark behind it, in mm)
                        int room = 0;
                        while (room < 40 && !Inside(tris, axis, hit.point + hit.normal * (0.014f - room * 0.001f))) room++;
                        float bury = inOld * 100f - room;
                        if (!worstSpot.TryGetValue(n, out var w) || bury > w.depth) worstSpot[n] = (s, bury);
                    }
                }
            }
            Log($"weak spot X on the pines: {shown} shown ({trees.Count} trees x 12 spots), {badNew} with a bit of the X inside the bark; the old placement (on the collider) had {badOld} (worst {worstOld * 100f:F0}% of its corners in); the X is {nearest:F3} .. {farthest:F3} m from the axis");
            foreach (var f in fails) Log("  X in the bark: " + f);
            Check(shown >= trees.Count * 12 - 2 && badNew == 0, $"the X never goes into a pine's bark ({badNew} of {shown} spots do; the old placement: {badOld})");
            // photographs: the worst old spot on three trees, from the front and from the sides, normal and zoomed
            int shot = 0;
            IEnumerator Look(Vector3 feet, Vector3 at, string name)
            {
                pc.LocalTeleport(feet, 0f);
                var eye = feet + Vector3.up * Cfg.EyeHeight;
                var d = at - eye;
                pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                cam.fieldOfView = 70f;
                yield return null; yield return null; yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"treex_{shot++:00}_{name}.png"));
                yield return null;
                cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(0.45f / d.magnitude) * Mathf.Rad2Deg, 5f, 50f);
                yield return null; yield return null; yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"treex_{shot++:00}_{name}_zoom.png"));
                yield return null;
                cam.fieldOfView = 70f;
            }
            int done = 0;
            foreach (var n in pick)
            {
                if (done >= 3 || !worstSpot.TryGetValue(n, out var w)) continue;
                done++;
                foreach (var t in trees) t.Spot.Value = ResourceNode.NoSpot;
                n.Spot.Value = (byte)w.spot;
                yield return new WaitForSeconds(0.4f); // (the pop settles)
                Time.timeScale = 0f;
                n.TryGetSpot(out var spot, out var normal);
                foreach (float side in new[] { 0f, -40f, 40f })
                {
                    var off = Quaternion.Euler(0, side, 0) * normal;
                    var feet = ground(spot.x + off.x * 1.7f, spot.z + off.z * 1.7f);
                    yield return Look(feet, spot, $"t{pick.IndexOf(n)}_spot{w.spot}_{(side == 0f ? "front" : side < 0f ? "left" : "right")}");
                }
                Time.timeScale = 1f;
            }
            foreach (var t in trees) t.Spot.Value = ResourceNode.NoSpot;
            Time.timeScale = 1f;
            cam.fieldOfView = 70f;
        }

        /// <summary>A hit on a tree shakes a few leaves loose: they start in its crown, in its leaf colour, and drift down.
        /// Photographed as they fall (from beside the tree, and looking up into it).</summary>
        IEnumerator FallingLeavesCheck(PlayerController pc, string dir, System.Func<float, float, Vector3> ground, List<ResourceNode> pick)
        {
            if (pick.Count == 0) yield break;
            var cam = Camera.main;
            cam.fieldOfView = 70f;
            int shot = 0;
            foreach (var n in new[] { pick[0], pick[pick.Count - 1] })
            {
                var tp = n.transform.position;
                var lay = ResourceNode.PineShapeOf(n.Seed.Value);
                var away = -tp; away.y = 0; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                var feet = ground(tp.x + away.x * 4.5f, tp.z + away.z * 4.5f);
                pc.LocalTeleport(feet, 0f);
                var eye = feet + Vector3.up * Cfg.EyeHeight;
                var d = tp + Vector3.up * (lay.CanopyBottom * 0.75f) - eye;
                pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                yield return new WaitForSeconds(0.3f);
                foreach (var old in FindObjectsByType<FxLeaf>(FindObjectsSortMode.None)) Destroy(old.gameObject);
                yield return null;
                int before = FxLeaf.Alive;
                // a hit on the trunk, as the player's swing plays it
                Fx.Play(FxKind.WoodChips, tp + Vector3.up * 1.1f + away * ResourceNode.TrunkR, away);
                yield return null;
                var leaves = new List<FxLeaf>(FindObjectsByType<FxLeaf>(FindObjectsSortMode.None));
                int made = FxLeaf.Alive - before;
                var start = new Dictionary<FxLeaf, Vector3>();
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var l in leaves) { start[l] = l.transform.position; lo = Mathf.Min(lo, l.transform.position.y - tp.y); hi = Mathf.Max(hi, l.transform.position.y - tp.y); }
                var want = n.Leaf;
                var lc = FxLeaf.LastColour;
                Log($"falling leaves from tree {pick.IndexOf(n)}: {made} shaken loose, {lo:F1} .. {hi:F1} m up (its needles start at {lay.CanopyBottom:F1} m), colour #{ColorUtility.ToHtmlStringRGB(lc)} (the tree's #{ColorUtility.ToHtmlStringRGB(want)})");
                Check(made >= 3 && made <= 10, $"a hit shakes a few leaves loose ({made})");
                Check(made > 0 && lo > lay.CanopyBottom - 0.8f && hi < lay.CanopyBottom + 2.5f, $"they start in the crown ({lo:F1} .. {hi:F1} m, the needles from {lay.CanopyBottom:F1} m)");
                bool sameColour = lc.g > lc.b && lc.g > lc.r && Vector3.Distance(new Vector3(lc.r, lc.g, lc.b), new Vector3(want.r, want.g, want.b)) < 0.25f;
                Check(sameColour, $"in the tree's leaf colour (#{ColorUtility.ToHtmlStringRGB(lc)} vs #{ColorUtility.ToHtmlStringRGB(want)})");
                string tag = $"t{pick.IndexOf(n)}";
                float waited = 0f;
                foreach (float at in new[] { 0.5f, 1.2f, 2.2f })
                {
                    yield return new WaitForSeconds(at - waited);
                    waited = at;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"leaves_{shot++:00}_{tag}_{at:F1}s.png"));
                }
                // they've come down (and swung about on the way)
                float drop = 0f, sway = 0f; int alive = 0;
                foreach (var l in leaves)
                {
                    if (l == null) continue;
                    alive++;
                    var dp = l.transform.position - start[l];
                    drop += -dp.y; sway += new Vector2(dp.x, dp.z).magnitude;
                }
                if (alive > 0) { drop /= alive; sway /= alive; }
                Log($"after 2.2 s: {alive} leaves still about, fallen {drop:F2} m on average, {sway:F2} m sideways");
                Check(alive == 0 || drop > 1.2f, $"the leaves drift down ({drop:F2} m in 2.2 s)");
                yield return new WaitForSeconds(4f);
            }
            // looking up into a tree while it's hit
            {
                var n = pick[0];
                var tp = n.transform.position;
                var lay = ResourceNode.PineShapeOf(n.Seed.Value);
                var side = new Vector3(1, 0, 0.3f).normalized;
                var feet = ground(tp.x + side.x * 2.2f, tp.z + side.z * 2.2f);
                pc.LocalTeleport(feet, 0f);
                var eye = feet + Vector3.up * Cfg.EyeHeight;
                var d = tp + Vector3.up * (lay.CanopyBottom + 1f) - eye;
                pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                yield return new WaitForSeconds(0.3f);
                Fx.Play(FxKind.WoodChips, tp + Vector3.up * 1.1f + side * ResourceNode.TrunkR, side);
                yield return new WaitForSeconds(0.7f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"leaves_{shot++:00}_looking_up.png"));
                yield return new WaitForSeconds(0.3f);
            }
        }

        /// <summary>A berry bush up close (and at a gust and a lull, to see it move), the bases' flag, the clouds, the
        /// Highlands hills with the grass off (to see the ground's shading), where the watch towers stood, and the view
        /// from inside the tall wheat (standing, looking down, crouching).</summary>
        IEnumerator BushAndWheatCloseups(PlayerNet me, PlayerController pc, string dir, System.Func<float, float, Vector3> ground)
        {
            var cam = Camera.main;
            int shot = 0;
            void Snap(string name) => ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"close_{shot++:00}_{name}.png"));
            IEnumerator Frame() { yield return null; yield return null; yield return new WaitForEndOfFrame(); }
            IEnumerator Look(Vector3 feet, Vector3 at, string name, float zoomSpan, float eyeH = -1f)
            {
                pc.LocalTeleport(feet, 0f);
                var eye = feet + Vector3.up * (eyeH > 0f ? eyeH : Cfg.EyeHeight);
                var d = at - eye;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                pc.SetLook(yaw, pitch);
                cam.fieldOfView = 70f;
                yield return Frame();
                if (Time.timeScale > 0f) yield return new WaitForSeconds(0.5f);
                Snap(name);
                yield return Frame();
                if (zoomSpan > 0f)
                {
                    bool frozen = Time.timeScale == 0f;
                    Time.timeScale = 0f;
                    cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(zoomSpan * 0.5f / d.magnitude) * Mathf.Rad2Deg, 5f, 50f);
                    yield return Frame();
                    Snap(name + "_zoom");
                    yield return Frame();
                    cam.fieldOfView = 70f;
                    if (!frozen) Time.timeScale = 1f;
                }
            }
            var team = me.Team.Value;
            var bc = Cfg.BaseCenter[team];
            var mid = ground(Mathf.Lerp(bc.x, 0, 0.5f), Mathf.Lerp(bc.z, 0, 0.5f));

            // ---- a berry bush ----
            ResourceNode bush = null;
            foreach (var n in ResourceNode.All)
                if (n != null && n.IsBush && n.Amount.Value > 0 && n.TrapTeam.Value == ResourceNode.NoTrap
                    && (bush == null || (n.transform.position - mid).sqrMagnitude < (bush.transform.position - mid).sqrMagnitude)) bush = n;
            Check(bush != null, "found a berry bush");
            if (bush != null)
            {
                var bp = bush.transform.position;
                var c = bp + Vector3.up * 0.5f;
                for (int a = 0; a < 3; a++)
                {
                    float ang = (a * 120f + 20f) * Mathf.Deg2Rad;
                    yield return Look(ground(bp.x + Mathf.Cos(ang) * 2.4f, bp.z + Mathf.Sin(ang) * 2.4f), c, $"bush_side{a}", 1.9f);
                }
                // the same view at the strongest gust and in the lull after it: it sways
                var f0 = ground(bp.x + 2.4f, bp.z);
                float peak = NextGustPeak(bp, Time.timeSinceLevelLoad);
                pc.LocalTeleport(f0, 0f);
                while (Time.timeSinceLevelLoad < peak) yield return null;
                Time.timeScale = 0f;
                yield return Look(f0, c, "bush_gust", 1.9f);
                Time.timeScale = 1f;
                float lull = peak + Mathf.PI / 0.9f;
                while (Time.timeSinceLevelLoad < lull) yield return null;
                Time.timeScale = 0f;
                yield return Look(f0, c, "bush_lull", 1.9f);
                Time.timeScale = 1f;
                int rs = 0, painted = 0;
                foreach (var r in bush.GetComponentsInChildren<Renderer>()) { if (!r.enabled) continue; rs++; if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "RockGame/Painted") painted++; }
                Log($"berry bush: {rs} renderers drawn, {painted} swaying (Painted)");
            }

            // ---- the bases' flag, up close ----
            if (!Cfg.Builder && MapBuilder.Root != null)
            {
                Renderer flag = null;
                foreach (var r in MapBuilder.Root.GetComponentsInChildren<Renderer>())
                    if (r.name == "flag" && r.enabled && (flag == null || (r.bounds.center - bc).sqrMagnitude < (flag.bounds.center - bc).sqrMagnitude)) flag = r;
                if (flag != null)
                {
                    var fc = flag.bounds.center;
                    var from = ground(fc.x + 1.5f, fc.z + (bc.z < 0 ? -4.5f : 4.5f));
                    yield return Look(from, fc, "flag", 2.6f);
                }
            }

            // ---- clouds ----
            var outDir = new Vector3(-bc.x, 0, -bc.z).normalized;
            yield return Look(mid, mid + Vector3.up * 1.6f + Quaternion.Euler(0, 30f, 0) * outDir * 100f + Vector3.up * 60f, "clouds_up", 0f);
            yield return Look(mid, mid + Vector3.up * 1.6f + Quaternion.Euler(0, -40f, 0) * outDir * 300f + Vector3.up * 40f, "clouds_horizon", 0f);
            // one cloud close up (zoomed in: the shading over its puffs)
            var layer = MapBuilder.Root != null ? MapBuilder.Root.GetComponentInChildren<CloudLayer>() : null;
            if (layer != null)
            {
                Renderer near = null;
                foreach (var r in layer.GetComponentsInChildren<Renderer>())
                {
                    var c = r.bounds.center;
                    if (new Vector2(c.x - mid.x, c.z - mid.z).magnitude < 60f) continue; // (not right overhead)
                    if (near == null || (c - mid).sqrMagnitude < (near.bounds.center - mid).sqrMagnitude) near = r;
                }
                if (near != null)
                {
                    Time.timeScale = 0f;
                    yield return Look(mid, near.bounds.center, "cloud_close", near.bounds.size.magnitude * 0.8f);
                    Time.timeScale = 1f;
                }
            }

            // ---- where the watch towers stood (Highlands) and the shading of the hills (the grass off, then on) ----
            int towers = 0;
            if (MapBuilder.Root != null) foreach (Transform t in MapBuilder.Root) if (t.name == "WatchTower") towers++;
            Log($"watch towers on the map: {towers}");
            Check(towers == 0, $"no watch towers on the map ({towers})");
            var high = new Vector3(bc.x * 1.15f, MapBuilder.Height(bc.x, bc.z) + 70f, bc.z * 1.15f);
            pc.LocalTeleport(high, 0f);
            {
                var d = new Vector3(0, 0, 0) - high;
                pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 42f);
                yield return new WaitForSeconds(0.8f);
                Snap("overview_from_above");
                yield return Frame();
            }
            if (Cfg.Map == MapKind.Highlands)
            {
                // the hilliest spot on our side
                Vector3 hill = mid; float best = -1f;
                for (int k = 0; k < 400; k++)
                {
                    var p = new Vector3(Random.Range(-Cfg.MapHalf + 15f, Cfg.MapHalf - 15f), 0, (bc.z < 0f ? -1f : 1f) * Random.Range(12f, Cfg.MapHalf - 15f));
                    if (Cfg.BaseTeamAt(p) >= 0) continue;
                    float v = 0f;
                    for (int s = 0; s < 8; s++) { float a = s * Mathf.PI / 4f; v += Mathf.Abs(MapBuilder.Height(p.x + Mathf.Cos(a) * 8f, p.z + Mathf.Sin(a) * 8f) - MapBuilder.Height(p.x, p.z)); }
                    if (v > best) { best = v; hill = p; }
                }
                hill = ground(hill.x, hill.z);
                var view = ground(hill.x + outDir.x * 18f + 6f, hill.z + outDir.z * 18f);
                var g = GrassField.Current;
                for (int pass = 0; pass < 2; pass++)
                {
                    if (g != null) { g.ForceOff = pass == 0; g.Refresh(); }
                    string tag = pass == 0 ? "no_grass" : "grass";
                    yield return Look(view, hill + Vector3.up * 1f, $"terrain_hills_{tag}", 0f);
                    var up = view + Vector3.up * 22f;
                    pc.LocalTeleport(up, 0f);
                    var d = hill - up;
                    pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                    yield return new WaitForSeconds(0.8f);
                    Snap($"terrain_hills_from_above_{tag}");
                    yield return Frame();
                }
                if (g != null) { g.ForceOff = false; g.Refresh(); }
            }

            // ---- inside the tall wheat: the blades right at the camera fade out ----
            var gr = GrassField.Current;
            if (gr != null)
            {
                Vector3 inWheat = default; bool found = false;
                for (int k = 0; k < 20000 && !found; k++)
                {
                    var p = new Vector3(Random.Range(-Cfg.MapHalf, Cfg.MapHalf), 0, Random.Range(-Cfg.MapHalf, Cfg.MapHalf));
                    bool ok = gr.CoverAt(p.x, p.z) > 0.99f && gr.WheatAt(p.x, p.z) > 0.75f;
                    for (int s = 0; s < 8 && ok; s++) { float a = s * Mathf.PI / 4f; ok &= gr.WheatAt(p.x + Mathf.Cos(a) * 2.5f, p.z + Mathf.Sin(a) * 2.5f) > 0.56f; }
                    if (ok) { inWheat = ground(p.x, p.z); found = true; }
                }
                Check(found, "found a spot deep in the tall wheat");
                if (found)
                {
                    // the patch from outside: where its tall middle starts, from eye height and from above
                    var pc0 = new Vector2(inWheat.x, inWheat.z);
                    for (int k = 0; k < 60; k++)
                    {
                        // (walk up to the deepest spot of this patch)
                        Vector2 best = pc0; float bw = gr.WheatAt(pc0.x, pc0.y);
                        for (int s = 0; s < 8; s++) { var q = pc0 + new Vector2(Mathf.Cos(s * Mathf.PI / 4f), Mathf.Sin(s * Mathf.PI / 4f)); float w = gr.WheatAt(q.x, q.y); if (w > bw) { bw = w; best = q; } }
                        if (best == pc0) break;
                        pc0 = best;
                    }
                    var centre = ground(pc0.x, pc0.y);
                    // straight out from the middle until it's open grass, then 5 m more
                    var outward = new Vector2(1, 0.35f).normalized;
                    float edge = 0f;
                    while (edge < 40f && gr.WheatAt(pc0.x + outward.x * edge, pc0.y + outward.y * edge) > 0.001f) edge += 0.25f;
                    float rimAt = 0f;
                    while (rimAt < edge && gr.WheatAt(pc0.x + outward.x * rimAt, pc0.y + outward.y * rimAt) >= 0.5f) rimAt += 0.25f;
                    Log($"tall grass patch at {centre}: its edge {edge:F1} m from the middle, the tall middle out to {rimAt:F1} m (the knee-high rim {edge - rimAt:F1} m wide)");
                    Check(rimAt > 0.5f && edge - rimAt > 0.8f, $"a patch has a knee-high rim round a tall middle (middle {rimAt:F1} m, rim {edge - rimAt:F1} m)");
                    var outside = ground(pc0.x + outward.x * (edge + 5f), pc0.y + outward.y * (edge + 5f));
                    yield return Look(outside, centre + Vector3.up * 1.2f, "wheat_patch_from_outside", 0f);
                    var side = new Vector2(-outward.y, outward.x);
                    var outside2 = ground(pc0.x + side.x * (edge + 3f), pc0.y + side.y * (edge + 3f));
                    yield return Look(outside2, centre + Vector3.up * 1.2f, "wheat_patch_from_the_side", 0f);
                    var above = centre + new Vector3(outward.x * (edge + 6f), 14f, outward.y * (edge + 6f));
                    pc.LocalTeleport(above, 0f);
                    {
                        var d = centre - (above + Vector3.up * Cfg.EyeHeight);
                        pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
                        yield return new WaitForSeconds(0.8f);
                        Snap("wheat_patch_from_above");
                        yield return Frame();
                    }
                    // standing on the edge of the tall middle, looking in
                    var rimFeet = ground(pc0.x + outward.x * (rimAt + 1.2f), pc0.y + outward.y * (rimAt + 1.2f));
                    yield return Look(rimFeet, centre + Vector3.up * Cfg.EyeHeight, "wheat_patch_rim_looking_in", 0f);
                    float yaw = Random.Range(0f, 360f);
                    var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                    var fade = Shader.GetGlobalVector("_GrassNearFade");
                    Check(fade.y > fade.x && fade.x >= 0f, $"the blades at the camera fade out ({fade.x:F1} .. {fade.y:F1} m)");
                    // each view twice: as it was (every blade drawn), then with the blades at the camera faded out
                    for (int pass = 0; pass < 2; pass++)
                    {
                        GrassField.NearFadeOff = pass == 0;
                        string tag = pass == 0 ? "_before_no_fade" : "";
                        yield return Look(inWheat, inWheat + Vector3.up * Cfg.EyeHeight + fwd * 10f, "wheat_inside_standing" + tag, 0f);
                        yield return Look(inWheat, inWheat + Vector3.up * (Cfg.EyeHeight - 0.45f) + fwd * 3f, "wheat_inside_look_down" + tag, 0f);
                        // crouching (eyes at 1.05 m: the time is stopped so the player can't stand up again)
                        Time.timeScale = 0f;
                        yield return Look(inWheat + Vector3.down * (Cfg.EyeHeight - Cfg.CrouchEyeHeight), inWheat + Vector3.up * Cfg.CrouchEyeHeight + fwd * 10f, "wheat_inside_crouching" + tag, 0f);
                        Time.timeScale = 1f;
                        yield return Frame();
                    }
                    GrassField.NearFadeOff = false;
                    // (someone standing in it is still hidden from everyone else: the world test's wheat_hides_* pictures)
                }
            }
            Time.timeScale = 1f;
            cam.fieldOfView = 70f;
        }
    }
}
