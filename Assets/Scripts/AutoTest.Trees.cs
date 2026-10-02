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
                    bool ok = gr.CoverAt(p.x, p.z) > 0.99f;
                    for (int s = 0; s < 8 && ok; s++) { float a = s * Mathf.PI / 4f; ok &= gr.WheatAt(p.x + Mathf.Cos(a) * 3f, p.z + Mathf.Sin(a) * 3f) > 0.97f; }
                    if (ok) { inWheat = ground(p.x, p.z); found = true; }
                }
                Check(found, "found a spot deep in the tall wheat");
                if (found)
                {
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
