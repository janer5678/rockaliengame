using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>-autotest world -host -solo -map plains|highlands -shotdir DIR: the Normal look's world - tall swaying
    /// pines (the trunk never pokes out of the top), the plain boundary walls (no castle any more), the bases' waving
    /// flags, the clouds and the low-poly sun, Settings > Display (grass distance / density / far thickness and every
    /// world colour), colour changes and the clipboard text, trampled grass recovering, tall wheat hiding a player,
    /// the first-person hand colour, and frame times (default, low, 270 m grass).</summary>
    public partial class AutoTest
    {
        IEnumerator WorldShots(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            GameSettings.SetGraphics(0, false);
            GameSettings.ResetWorldLook(false); // (the test's pictures use the defaults; nothing is saved)
            yield return new WaitForSeconds(0.5f);
            float half = Cfg.MapHalf;
            var team = me.Team.Value;
            var root = MapBuilder.Root;

            // ---- checks ----
            // the kinds of pine and how tall they are (from the seed alone, so every peer builds the same)
            {
                var kinds = new int[ResourceNode.PineVariants];
                float lo = float.MaxValue, hi = 0f, thinnest = float.MaxValue;
                for (int seed = 0; seed < 3000; seed++)
                {
                    var sh = ResourceNode.PineShapeOf(seed * 7919 + 13);
                    kinds[sh.Variant]++;
                    lo = Mathf.Min(lo, sh.Top); hi = Mathf.Max(hi, sh.Top);
                    for (float y = sh.UnderY(0); y < sh.TrunkTop; y += 0.05f) thinnest = Mathf.Min(thinnest, sh.TrunkRadius(y));
                }
                Log($"pine kinds over 3000 seeds: {string.Join(", ", System.Array.ConvertAll(kinds, k => k.ToString()))}; {lo:F1} .. {hi:F1} m tall; thinnest trunk in a crown {thinnest:F2} m");
                Check(System.Array.TrueForAll(kinds, k => k > 300), $"all {kinds.Length} kinds of pine come up");
                Check(lo >= 9.4f && hi <= 16.6f && hi - lo > 5f, $"the pines are 10-16 m tall ({lo:F1} .. {hi:F1} m)");
                Check(thinnest >= 0.05f, $"the trunk never pinches off inside the needles (thinnest {thinnest:F2} m)");
            }
            int pines = 0, bad = 0, tallOnes = 0;
            var kindsHere = new HashSet<int>();
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Kind.Value != ResourceNode.Tree) continue;
                var needles = n.transform.Find("visual/needles");
                var trunk = n.transform.Find("visual/trunk");
                if (needles == null || trunk == null) continue;
                pines++;
                kindsHere.Add(ResourceNode.PineVariant(n.Seed.Value));
                var nb = needles.GetComponent<Renderer>().bounds;
                var tc = trunk.GetComponent<Collider>();
                var tb = tc != null ? tc.bounds : trunk.GetComponent<Renderer>().bounds;
                if (tb.max.y > nb.max.y - 0.8f || tb.max.y < nb.min.y + 1f) bad++;
                if (nb.max.y - n.transform.position.y > 9.5f) tallOnes++;
                var cap = trunk.GetComponent<CapsuleCollider>();
                if (pines == 1) Log($"built-in cylinder mesh extents {Art.Cylinder.bounds.extents}; trunk collider radius {(cap != null ? cap.radius * trunk.lossyScale.x : -1f):F2} m, {tb.size.y:F1} m tall; tree {nb.max.y - n.transform.position.y:F1} m");
            }
            Check(pines > 0 && bad == 0, $"{pines} pines, trunk colliders end well inside the needles ({bad} not)");
            Check(tallOnes == pines, $"every pine on the map is over 9.5 m tall ({tallOnes} of {pines})");
            Check(kindsHere.Count >= Mathf.Min(4, pines), $"{kindsHere.Count} kinds of pine on the map");
            Check(root.Find("Castle") == null, "no castle walls any more");
            // the plain boundary walls stop you, and they're what you see now
            List<Renderer> PlainWallRs()
            {
                var l = new List<Renderer>();
                foreach (var bc in root.GetComponentsInChildren<BoxCollider>())
                {
                    var c = bc.transform.position;
                    bool edge = (Mathf.Abs(Mathf.Abs(c.x) - (half + 1)) < 0.01f && Mathf.Abs(c.z) < 1f) || (Mathf.Abs(Mathf.Abs(c.z) - (half + 1)) < 0.01f && Mathf.Abs(c.x) < 1f);
                    if (edge && !bc.isTrigger) l.Add(bc.GetComponent<Renderer>());
                }
                return l;
            }
            int WallsShown() { int k = 0; foreach (var r in PlainWallRs()) if (r && r.enabled) k++; return k; }
            // (Normal graphics: the walls still collide but you see the glass dome over the map instead)
            Check(PlainWallRs().Count == 4 && WallsShown() == 0 && MapDome.Shown, $"the 4 boundary walls collide, the glass dome is drawn instead ({PlainWallRs().Count} walls, {WallsShown()} shown, dome {MapDome.Shown})");
            var clouds = root.GetComponentInChildren<CloudLayer>();
            Check(clouds != null && clouds.Count > 5, $"clouds up ({(clouds != null ? clouds.Count : 0)})");
            int flags = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>()) if (r.name == "flag" && r.enabled) flags++;
            Check(Cfg.Builder || flags >= 4, $"the bases' waving flags are up ({flags})");
            var sun = SkySun.Current;
            yield return null;
            Check(sun != null && sun.Shown, "the low-poly sun is in the sky");
            // you can't walk out through the wall
            pc.LocalTeleport(new Vector3(0, MapBuilder.Height(0, -half + 3f) + 0.2f, -half + 3f), 180f);
            yield return new WaitForSeconds(0.3f);
            bool blocked = Physics.Raycast(new Vector3(0, MapBuilder.Height(0, -half + 3f) + 1f, -half + 3f), Vector3.back, out var wallHit, 6f, ~0, QueryTriggerInteraction.Ignore);
            Check(blocked && wallHit.point.z > -half - 0.05f, $"the wall stops you at the edge of the map (hit at z {(blocked ? wallHit.point.z : 0f):F2}, edge {-half})");

            int shot = 0;
            Vector3 trampleMid = default; float trampleYaw = 0f;
            void Snap(string name) => ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"world_{shot++:00}_{name}.png"));
            IEnumerator Shoot(string name, Vector3 at, float yaw, float pitch, float wait = 0.8f)
            {
                pc.LocalTeleport(at + Vector3.up * 0.1f, yaw);
                pc.SetLook(yaw, pitch);
                yield return new WaitForSeconds(wait);
                Snap(name);
                yield return new WaitForSeconds(0.3f);
            }
            Vector3 Ground(float x, float z) => new Vector3(x, MapBuilder.Height(x, z), z);
            float YawTo(Vector3 from, Vector3 to) => Quaternion.LookRotation(new Vector3(to.x - from.x, 0, to.z - from.z)).eulerAngles.y;

            IEnumerator TrampleAndWheat()
            {
                var gr = GrassField.Current;
            // ---- trampled grass: a trail right after walking, standing again ~20 s later ----
            if (gr != null)
            {
                Vector3 start = trampleMid;
                var dirW = Quaternion.Euler(0, trampleYaw + 50f, 0) * Vector3.forward;
                bool Clear(Vector3 p) { for (float s = -3f; s <= 12f; s += 1f) { var q = p + dirW * s; if (gr.CoverAt(q.x, q.z) < 0.99f || gr.WheatAt(q.x, q.z) > 0.02f) return false; } return true; }
                for (int k = 0; k < 400 && !Clear(start); k++) start = Ground(trampleMid.x + Random.Range(-40f, 40f), trampleMid.z + Random.Range(-40f, 40f));
                // walk 8 m (small steps, like walking)
                for (float s = 0; s <= 8f; s += 0.12f)
                {
                    var p = Ground(start.x + dirW.x * s, start.z + dirW.z * s);
                    pc.LocalTeleport(p + Vector3.up * 0.05f, trampleYaw + 50f);
                    yield return null;
                }
                yield return new WaitForSeconds(0.25f);
                var trailMid = start + dirW * 4f;
                float flatNow = gr.FlatAt(trailMid.x, trailMid.z);
                var side = Vector3.Cross(Vector3.up, dirW);
                float flatSide = gr.FlatAt(trailMid.x + side.x * 3f, trailMid.z + side.z * 3f);
                Check(flatNow > 0.9f && flatSide < 0.05f, $"walking flattens a trail ({flatNow:F2} on it, {flatSide:F2} 3 m to the side)");
                {
                    // what the GPU has (the trample map, read back)
                    var tex = Shader.GetGlobalTexture("_GrassTrample");
                    var rect = Shader.GetGlobalVector("_GrassTrampleRect");
                    var tm = Shader.GetGlobalVector("_GrassTrampleTime");
                    float gpu = float.NaN;
                    if (tex != null)
                    {
                        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                        Graphics.Blit(tex, rt);
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        int px = Mathf.FloorToInt((trailMid.x - rect.x) * rect.z * tex.width), py = Mathf.FloorToInt((trailMid.z - rect.y) * rect.w * tex.height);
                        var rd = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                        rd.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
                        rd.Apply();
                        gpu = rd.GetPixel(0, 0).r;
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(rt);
                        Destroy(rd);
                    }
                    Log($"trample map on the GPU: {(tex != null ? tex.name + " " + tex.width : "none")}, rect {rect}, time {tm}, at the trail {gpu:F2} (now {Time.time:F2})");
                }
                // look back along the trail from beside its start (standing off it)
                var view = Ground(start.x - dirW.x * 1.5f + side.x * 1.7f, start.z - dirW.z * 1.5f + side.z * 1.7f);
                float back = YawTo(view, start + dirW * 4f);
                yield return Shoot("trample_trail_now", view, back, 38f, 0.4f);
                float t0 = Time.time;
                yield return new WaitForSeconds(9f);
                Snap("trample_trail_10s");
                Log($"trail after {Time.time - t0:F0} s: {gr.FlatAt(trailMid.x, trailMid.z):F2} flat");
                yield return new WaitForSeconds(11f);
                float flatLater = gr.FlatAt(trailMid.x, trailMid.z);
                Snap("trample_trail_20s_recovered");
                yield return new WaitForSeconds(0.3f);
                Check(flatLater < 0.02f, $"the trail stands back up after {Time.time - t0:F0} s ({flatLater:F2} flat)");
            }

                yield break;
            }

            IEnumerator TrampleAndWheat2()
            {
                var gr = GrassField.Current;
            // ---- tall wheat hides a player ----
            if (gr != null)
            {
                Vector3 inWheat = default, from = default;
                bool found = false;
                for (int k = 0; k < 20000 && !found; k++)
                {
                    var p = new Vector3(Random.Range(-half, half), 0, Random.Range(-half, half));
                    // (in a patch's tall middle, tall grass all round it, and open grass ~2 m past the patch's edge)
                    if (gr.WheatAt(p.x, p.z) < 0.6f || gr.CoverAt(p.x, p.z) < 0.99f) continue;
                    var d = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
                    bool ok = gr.WheatAt(p.x + d.x, p.z + d.z) > 0.55f && gr.WheatAt(p.x - d.x * 1.2f, p.z - d.z * 1.2f) > 0.55f;
                    float edge = 0f;
                    while (edge < 8f && gr.WheatAt(p.x - d.x * edge, p.z - d.z * edge) > 0.001f) edge += 0.25f;
                    var f = p - d * (edge + 2f);
                    ok &= edge < 8f && gr.WheatAt(f.x, f.z) < 0.001f && gr.CoverAt(f.x, f.z) > 0.5f;
                    if (!ok) continue;
                    inWheat = Ground(p.x, p.z); from = Ground(f.x, f.z); found = true;
                }
                Check(found, "found a field of tall wheat with open grass beside it");
                Check(gr.WheatAt(Cfg.BaseCenter[team].x, Cfg.BaseCenter[team].z) < 0.01f && gr.WheatAt(0, 0) < 0.01f, "no tall wheat on the bases or the ball zone");
                if (found)
                {
                    var prefab = Resources.Load<GameObject>("Alien/AlienRigged");
                    if (prefab != null)
                    {
                        var dummy = Instantiate(prefab);
                        dummy.name = "test dummy";
                        dummy.transform.SetPositionAndRotation(inWheat, Quaternion.LookRotation(new Vector3(from.x - inWheat.x, 0, from.z - inWheat.z)));
                        foreach (var a in dummy.GetComponentsInChildren<Animator>()) a.enabled = false;
                        foreach (var r in dummy.GetComponentsInChildren<Renderer>()) foreach (var m in r.materials) { m.SetColor("_BaseColor", Cfg.TeamColor[1]); m.color = Cfg.TeamColor[1]; }
                        float yaw = YawTo(from, inWheat);
                        yield return Shoot("wheat_hides_standing_player", from, yaw, 4f, 1f);
                        dummy.transform.localScale = new Vector3(1f, 0.62f, 1f); // (about crouching height)
                        yield return Shoot("wheat_hides_crouching_player", from, yaw, 4f, 0.5f);
                        // the same dummy out in the short grass, for comparison
                        var open = from + (from - inWheat).normalized * 4f;
                        open = Ground(open.x, open.z);
                        dummy.transform.localScale = Vector3.one;
                        dummy.transform.position = open;
                        var from2 = Ground(open.x + (from.x - inWheat.x), open.z + (from.z - inWheat.z));
                        yield return Shoot("player_in_short_grass", from2, YawTo(from2, open), 4f, 0.6f);
                        Destroy(dummy);
                        yield return Shoot("wheat_from_inside", inWheat, YawTo(inWheat, from), 2f, 0.6f);
                    }
                }
            }

                yield break;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-trees-only") >= 0)
            {
                yield return TreeCloseups(pc, dir, (x, z) => Ground(x, z));
                yield return BushAndWheatCloseups(me, pc, dir, (x, z) => Ground(x, z));
                Application.Quit(0);
                yield break;
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-trample-only") >= 0)
            {
                var mid0 = Ground(Mathf.Lerp(Cfg.BaseCenter[team].x, 0, 0.5f), Mathf.Lerp(Cfg.BaseCenter[team].z, 0, 0.5f));
                float outYaw0 = YawTo(Cfg.BaseCenter[team], Vector3.zero);
                trampleMid = mid0; trampleYaw = outYaw0;
                yield return TrampleAndWheat();
                yield return TrampleAndWheat2();
                Application.Quit(0);
                yield break;
            }

            // ---- trees ----
            var bcn = Cfg.BaseCenter[team];
            ResourceNode tree = null;
            foreach (var n in ResourceNode.All)
                if (n != null && n.Kind.Value == ResourceNode.Tree && n.Amount.Value > 0 && Cfg.BaseTeamAt(n.transform.position) < 0
                    && (tree == null || (n.transform.position - bcn).sqrMagnitude < (tree.transform.position - bcn).sqrMagnitude)) tree = n;
            if (tree != null)
            {
                var tp = tree.transform.position;
                var away = (new Vector3(bcn.x - tp.x, 0, bcn.z - tp.z)).normalized;
                var close = Ground(tp.x + away.x * 6f, tp.z + away.z * 6f);
                yield return Shoot("tree_close", close, YawTo(close, tp), -22f);
                var far = Ground(tp.x + away.x * 32f, tp.z + away.z * 32f);
                yield return Shoot("trees_far", far, YawTo(far, tp), -4f);
            }
            // the pines up close (no trunk through the needles), the bush, flag, clouds, hills, towers, inside the wheat
            yield return TreeCloseups(pc, dir, (x, z) => Ground(x, z));
            yield return BushAndWheatCloseups(me, pc, dir, (x, z) => Ground(x, z));

            // ---- the plain walls (no castle) ----
            float sideZ = bcn.z < 0 ? -half : half;
            float faceYaw = sideZ < 0 ? 180f : 0f;
            var wallSpot = Ground(half * 0.35f, sideZ - Mathf.Sign(sideZ) * 16f);
            yield return Shoot("walls_no_castle", wallSpot, faceYaw, -6f);
            var longSpot = Ground(-half + 6f, sideZ - Mathf.Sign(sideZ) * 5f);
            yield return Shoot("walls_along", longSpot, YawTo(longSpot, new Vector3(half, 0, sideZ)), -3f);

            // ---- flags ----
            if (!Cfg.Builder)
            {
                var flagPole = bcn + new Vector3(Cfg.BaseHalf + 1f, 0, (bcn.z < 0 ? 1f : -1f) * (Cfg.BaseHalf + 1f));
                var fs = Ground(flagPole.x - 4.5f, flagPole.z + (bcn.z < 0 ? -3f : 3f));
                yield return Shoot("flag", fs, YawTo(fs, flagPole), -30f);
            }

            // ---- clouds and the sun ----
            var mid = Ground(Mathf.Lerp(bcn.x, 0, 0.5f), Mathf.Lerp(bcn.z, 0, 0.5f));
            float outYaw = YawTo(bcn, Vector3.zero);
            trampleMid = mid; trampleYaw = outYaw;
            yield return Shoot("clouds", mid, outYaw + 30f, -32f);
            var sd = SkySun.Direction;
            float sunYaw = Quaternion.LookRotation(new Vector3(sd.x, 0, sd.z)).eulerAngles.y;
            float sunPitch = -Mathf.Asin(Mathf.Clamp(sd.y, -1f, 1f)) * Mathf.Rad2Deg;
            yield return Shoot("sun", mid, sunYaw, sunPitch + 12f);
            yield return Shoot("sun_and_meadow", mid, sunYaw + 25f, Mathf.Max(-34f, sunPitch + 20f));
            yield return Shoot("meadow_default", mid, outYaw + 50f, 6f);

            // ---- frame time (the default look) ----
            IEnumerator Measure(System.Action<float> done)
            {
                int vs = QualitySettings.vSyncCount, fr = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000;
                yield return new WaitForSeconds(0.4f);
                float sum = 0; int n = 0;
                float until = UnityEngine.Time.realtimeSinceStartup + 2f;
                while (UnityEngine.Time.realtimeSinceStartup < until) { yield return null; sum += UnityEngine.Time.unscaledDeltaTime; n++; }
                QualitySettings.vSyncCount = vs; Application.targetFrameRate = fr;
                done(sum / Mathf.Max(1, n) * 1000f);
            }
            float tDefault = 0, tLow = 0, tFar = 0, tFarThick = 0, tOff = 0;
            pc.LocalTeleport(mid + Vector3.up * 0.1f, outYaw + 50f); pc.SetLook(outYaw + 50f, 6f);
            yield return Measure(v => tDefault = v);
            var g = GrassField.Current;
            int trisDefault = g != null ? g.DrawnTriangles : 0, patchesDefault = g != null ? g.DrawnPatches : 0;

            // ---- Settings > Display ----
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.8f);
            Snap("settings_display");
            yield return new WaitForSeconds(0.3f);
            Hud.ScrollToColours = true;
            yield return new WaitForSeconds(0.5f);
            Snap("settings_colours");
            yield return new WaitForSeconds(0.3f);
            Check(ColorSlots.All.Count >= 25, $"{ColorSlots.All.Count} colour slots in the settings");

            // grass: closer and thinner, then the most
            GameSettings.SetGrass(30f, 0.4f, false);
            yield return new WaitForSeconds(0.3f);
            pc.Paused = false;
            yield return Shoot("grass_30m_40pct", mid, outYaw + 50f, 6f);
            yield return Measure(v => tLow = v);
            int trisLow = g != null ? g.DrawnTriangles : 0;
            Check(trisLow < trisDefault * 0.6f, $"less grass drawn at 30 m / 40% ({trisLow / 1000}k triangles, was {trisDefault / 1000}k)");
            GameSettings.SetGrass(GameSettings.GrassDistanceMax, 1f, GameSettings.GrassFalloffDefault, false);
            Check(GameSettings.GrassDistanceMax >= 270f, $"grass distance goes to {GameSettings.GrassDistanceMax:0} m");
            yield return Shoot("grass_270m_100pct", mid, outYaw + 50f, 6f);
            yield return Measure(v => tFar = v);
            int trisFar = g != null ? g.DrawnTriangles : 0, patchesFar = g != null ? g.DrawnPatches : 0;
            Check(trisFar > trisDefault, $"more grass drawn at 270 m ({trisFar / 1000}k triangles, {patchesFar} patches)");
            // from up high, to see how far it goes
            var high = mid + Vector3.up * 25f;
            pc.LocalTeleport(high, outYaw + 50f); pc.SetLook(outYaw + 50f, 14f);
            yield return new WaitForSeconds(0.6f);
            Snap("grass_270m_from_above");
            yield return new WaitForSeconds(0.3f);
            GameSettings.SetGrass(GameSettings.GrassDistanceMax, 1f, GameSettings.GrassFalloffMin, false);
            yield return Shoot("grass_270m_thickest_far", mid, outYaw + 50f, 6f);
            yield return Measure(v => tFarThick = v);
            int trisThick = g != null ? g.DrawnTriangles : 0;
            Check(trisThick > trisFar, $"thicker far grass draws more ({trisThick / 1000}k triangles)");
            GameSettings.SetGrass(GameSettings.GrassDistanceMax, 1f, GameSettings.GrassFalloffMax, false);
            yield return Shoot("grass_270m_thinnest_far", mid, outYaw + 50f, 6f);
            if (g != null)
            {
                g.ForceOff = true; g.Refresh();
                yield return Measure(v => tOff = v);
                g.ForceOff = false; g.Refresh();
            }
            Log($"frame time: default look {tDefault:F2} ms ({trisDefault / 1000}k grass triangles, {patchesDefault} patches), grass 30 m / 40% {tLow:F2} ms ({trisLow / 1000}k), "
                + $"grass 270 m / 100% {tFar:F2} ms ({trisFar / 1000}k, {patchesFar} patches), 270 m thickest far grass {tFarThick:F2} ms ({trisThick / 1000}k), no grass {tOff:F2} ms");
            GameSettings.SetGrass(GameSettings.GrassDistanceDefault, GameSettings.GrassDensityDefault, GameSettings.GrassFalloffDefault, false);

            // ---- colour changes, applied live ----
            var snowy = ColorSlots.Ground.Presets[5];
            var wallMat = MapDome.FrameMaterial; // (the "Map dome" colour: the glass dome's frame)
            Color wallBefore = wallMat.color;
            ColorSlots.Set(ColorSlots.Ground, snowy, false);
            ColorSlots.Set(ColorSlots.Grass, ColorSlots.Grass.Presets[5], false);
            ColorSlots.Set(ColorSlots.Leaves, ColorSlots.Leaves.Presets[4], false);
            ColorSlots.Set(ColorSlots.TreeTrunks, new Color(0.85f, 0.85f, 0.8f), false);
            ColorSlots.Set(ColorSlots.Sky, ColorSlots.Sky.Presets[3], false);
            ColorSlots.Set(ColorSlots.Sun, ColorSlots.Sun.Presets[3], false);
            ColorSlots.Set(ColorSlots.MapWalls, new Color(0.62f, 0.3f, 0.25f), false);
            ColorSlots.Set(ColorSlots.Mountains, new Color(0.55f, 0.45f, 0.62f), false);
            ColorSlots.Set(ColorSlots.Wheat, ColorSlots.Wheat.Presets[6], false);
            ColorSlots.Set(ColorSlots.Bedrock, new Color(0.9f, 0.75f, 0.3f), false);
            ColorSlots.Set(ColorSlots.Hands, new Color(0.3f, 0.85f, 0.9f), false);
            yield return new WaitForSeconds(0.3f);
            Check(WorldLook.GroundMaterial(false).color == snowy, "the ground takes the colour picked");
            Check(ColorSlots.Same(wallMat.color, ColorSlots.MapWalls.Value) && !ColorSlots.Same(wallMat.color, wallBefore), $"the map dome takes the colour picked (#{ColorUtility.ToHtmlStringRGB(wallMat.color)})");
            int handRs = 0, handOk = 0;
            foreach (var hook in FindObjectsByType<HandColorHook>(FindObjectsSortMode.None))
            {
                handRs++;
                if (hook.AllShaded(out _) && ColorSlots.Same(ColorSlots.HandTint(Color.red), ColorSlots.Hands.Value)) handOk++;
            }
            Check(handRs > 0 && handOk == handRs, $"the first-person hands take their colour ({handOk} / {handRs} hands)");
            string clip = ColorSlots.Lines(true);
            ColorSlots.Copy(true);
            Log("clipboard (changed colours):\n" + GUIUtility.systemCopyBuffer);
            Check(GUIUtility.systemCopyBuffer == clip || string.IsNullOrEmpty(GUIUtility.systemCopyBuffer), "copied to the clipboard");
            Check(clip.Contains("TreeTrunks = #D9D9CC") && clip.Contains("MapWalls = #") && !clip.Contains("Clouds ="), "the clipboard lists just the changed colours as Id = #hex");
            if (tree != null)
            {
                var tp = tree.transform.position;
                var away = (new Vector3(bcn.x - tp.x, 0, bcn.z - tp.z)).normalized;
                var far = Ground(tp.x + away.x * 16f, tp.z + away.z * 16f);
                yield return Shoot("colours_changed", far, YawTo(far, tp), -12f);
            }
            yield return Shoot("colours_changed_walls", wallSpot, faceYaw, -6f);
            var bedrock = Cfg.BedrockCenter(team);
            var bs = Ground(bedrock.x + 5f, bedrock.z + 5f);
            yield return Shoot("colours_changed_base_and_hands", bs, YawTo(bs, bedrock), 20f);
            yield return Shoot("colours_changed_sun", mid, sunYaw, sunPitch + 12f);
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.5f);
            Hud.ScrollToColours = true;
            yield return new WaitForSeconds(0.6f);
            Snap("settings_colours_changed");
            yield return new WaitForSeconds(0.3f);
            pc.Paused = false;
            GameSettings.ResetWorldLook(false);
            yield return new WaitForSeconds(0.2f);
            Check(WorldLook.GroundMaterial(false).color == GameSettings.WorldColorDefaults[0] && ColorSlots.ChangedCount == 0, "reset puts the default colours back");
            Check(ColorSlots.Same(wallMat.color, wallBefore), "the map dome is back to its colour");

            yield return TrampleAndWheat();
            yield return TrampleAndWheat2();

            // ---- PSX and AI PSX keep their own looks ----
            GameSettings.SetGraphics(1, false);
            yield return new WaitForSeconds(0.5f);
            yield return Shoot("psx_walls", wallSpot, faceYaw, -6f);
            Check(WallsShown() == 4 && !MapDome.Shown && !clouds.Shown && (sun == null || !sun.Shown), "PSX: the plain (concrete) walls, no glass dome, no clouds, no sun");
            GameSettings.SetGraphics(2, false);
            yield return new WaitForSeconds(0.8f);
            Check(WallsShown() == 4 && !MapDome.Shown && !clouds.Shown && (sun == null || !sun.Shown), "AI PSX: the plain walls, no glass dome, no clouds, no sun");
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);
            Check(WallsShown() == 0 && MapDome.Shown && clouds.Shown && (sun == null || sun.Shown), "Normal again: the glass dome, the clouds and the sun");
            Log("world test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
