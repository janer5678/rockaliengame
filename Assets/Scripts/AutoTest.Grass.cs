using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>-autotest grass -host -solo -map plains|highlands -shotdir DIR: photographs the meadow (Normal graphics)
    /// and times frames with the grass on and off.</summary>
    public partial class AutoTest
    {
        IEnumerator GrassShots(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);
            var g = GrassField.Current;
            Check(g != null, "the grass field is built");
            Log($"grass fields: {FindObjectsByType<GrassField>(FindObjectsSortMode.None).Length}");
            if (g == null) { Application.Quit(0); yield break; }
            var team = me.Team.Value;
            var bc = Cfg.BaseCenter[team];
            Check(g.CoverAt(bc.x, bc.z) == 0f && g.CoverAt(0, 0) == 0f, "no grass on the base or the ball drop zone");

            // a spot out in the wild, between our base and the middle, with grass all round
            Vector3 Spot(Vector3 want)
            {
                for (int k = 0; k < 200; k++)
                {
                    var p = want + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)) * k * 0.4f;
                    if (g.CoverAt(p.x, p.z) > 0.99f && g.CoverAt(p.x + 3, p.z) > 0.99f && g.CoverAt(p.x - 3, p.z) > 0.99f) return new Vector3(p.x, MapBuilder.Height(p.x, p.z), p.z);
                }
                return want;
            }
            int shot = 0;
            IEnumerator Shoot(string name, Vector3 at, float yaw, float pitch)
            {
                pc.LocalTeleport(at + Vector3.up * 0.1f, yaw);
                pc.SetLook(yaw, pitch);
                yield return new WaitForSeconds(0.8f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"grass_{shot++:00}_{name}.png"));
                yield return new WaitForSeconds(0.3f);
                Log($"shot {name} at {at}: {g.DrawnPatches} patches drawn, {g.DrawnTriangles / 1000}k triangles");
            }
            var mid = Spot(Vector3.Lerp(bc, Vector3.zero, 0.5f));
            float outYaw = Quaternion.LookRotation(new Vector3(-bc.x, 0, -bc.z)).eulerAngles.y;

            yield return Shoot("meadow", mid, outYaw + 50f, 8f);
            yield return Shoot("meadow_down", mid, outYaw - 40f, 35f);
            yield return Shoot("towards_base", mid, outYaw + 180f, 6f);
            Check(g.DrawnPatches > 10, $"grass patches drawn round the player ({g.DrawnPatches})");
            if (g.ClearingSpots.Count > 0)
            {
                // the nearest daisy clearing, from a couple of metres away
                var c = g.ClearingSpots[0];
                foreach (var s in g.ClearingSpots) if (Vector2.Distance(s, new Vector2(mid.x, mid.z)) < Vector2.Distance(c, new Vector2(mid.x, mid.z))) c = s;
                var from = new Vector3(c.x - 3.5f, 0, c.y - 3.5f);
                from.y = MapBuilder.Height(from.x, from.z);
                yield return Shoot("daisies", from, 45f, 30f);
            }
            // the tall grass patches: fewer than there were, each with a knee-high rim round its tall middle
            Log($"tall grass: {g.WheatPatches} patches ({g.WheatPatchesRaw - g.WheatPatches} too small dropped), {g.WheatArea:F0} m² ({g.WheatCore:F0} m² tall middles) on a {Cfg.MapHalf * 2f:F0} m map");
            Check(g.WheatPatches > 0 && g.WheatCore > 0f && g.WheatCore < g.WheatArea, $"tall grass patches with tall middles ({g.WheatPatches})");
            // one from outside (a few metres past its edge)
            for (int k = 0; k < 4000; k++)
            {
                var p = new Vector3(Random.Range(-Cfg.MapHalf, Cfg.MapHalf), 0, Random.Range(-Cfg.MapHalf, Cfg.MapHalf));
                if (g.WheatAt(p.x, p.z) < 0.9f || g.CoverAt(p.x, p.z) < 0.99f) continue;
                float edge = 0f;
                while (edge < 40f && g.WheatAt(p.x - edge, p.z) > 0.001f) edge += 0.5f;
                var from = p - new Vector3(edge + 5f, 0, 0);
                from.y = MapBuilder.Height(from.x, from.z);
                yield return Shoot("wheat", from, 90f, 4f);
                break;
            }

            // frame time with and without the grass (same spot, same view)
            pc.LocalTeleport(mid + Vector3.up * 0.1f, outYaw + 50f);
            pc.SetLook(outYaw + 50f, 8f);
            yield return new WaitForSeconds(0.5f);
            float on = 0, off = 0;
            int onN = 0, offN = 0;
            int vs = QualitySettings.vSyncCount, fr = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000; // (uncapped, to see the real cost)
            for (int pass = 0; pass < 4; pass++)
            {
                bool grass = pass % 2 == 0;
                g.ForceOff = !grass; g.Refresh();
                yield return new WaitForSeconds(0.4f);
                float until = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    if (grass) { on += Time.unscaledDeltaTime; onN++; } else { off += Time.unscaledDeltaTime; offN++; }
                }
            }
            g.ForceOff = false; g.Refresh();
            QualitySettings.vSyncCount = vs; Application.targetFrameRate = fr;
            Log($"frame time with grass {on / Mathf.Max(1, onN) * 1000f:F2} ms, without {off / Mathf.Max(1, offN) * 1000f:F2} ms");

            // nothing grows under what's built
            var spot = mid + new Vector3(2, 0, 2);
            GrassField.ClearUnder(new Bounds(spot + Vector3.up, new Vector3(3, 2, 3)));
            Check(g.CoverAt(spot.x, spot.z) == 0f, "grass cleared under a building piece");

            // ---- the blades stand on the ground as it's drawn, and (Highlands) grass covers all the green hillsides ----
            {
                int green = 0, bare = 0, bareOld = 0, rays = 0;
                float worstRoot = 0f, worstOld = 0f;
                float half = Cfg.MapHalf;
                var rnd = new System.Random(11);
                var band = new List<Vector3>();
                for (int k = 0; k < 8000; k++)
                {
                    float x = ((float)rnd.NextDouble() * 2f - 1f) * (half - 4f), z = ((float)rnd.NextDouble() * 2f - 1f) * (half - 4f);
                    if (Cfg.BaseTeamAt(new Vector3(x, 0, z)) >= 0 || new Vector2(x, z).magnitude < 16f) continue;
                    if (rays < 2000 && Physics.Raycast(new Vector3(x, 300f, z), Vector3.down, out var hit, 600f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<GroundMarker>() != null)
                    {
                        rays++;
                        // (how far a blade's root is under the ground you see: now, and planted on Height as before)
                        worstRoot = Mathf.Max(worstRoot, hit.point.y - (g.RootAt(x, z) + 0.04f));
                        worstOld = Mathf.Max(worstOld, hit.point.y - MapBuilder.Height(x, z));
                    }
                    if (Cfg.Map != MapKind.Highlands || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > half - 2f) continue;
                    float rf = MapBuilder.RockField(x, z);
                    if (rf >= -0.06f) continue; // (rock, or right at its edge)
                    green++;
                    if (g.CoverAt(x, z) < 0.5f) bare++;
                    if (Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, -0.18f, rf)) < 0.5f)
                    {
                        bareOld++;
                        if (band.Count < 40 && Mathf.Sign(z) == Mathf.Sign(bc.z + 0.01f)) band.Add(new Vector3(x, MapBuilder.Height(x, z), z));
                    }
                }
                Log($"grass roots against the drawn ground ({rays} spots): at most {worstRoot * 100f:F1} cm under it (planted on Height as before: {worstOld * 100f:F1} cm)");
                Check(rays > 100 && worstRoot < 0.08f, $"the blades stand on the ground you see, not under it (worst {worstRoot * 100f:F1} cm)");
                if (Cfg.Map == MapKind.Highlands)
                {
                    Log($"green hillside spots: {green}, {bare} with little or no grass (before: {bareOld})");
                    Check(green > 100 && bare <= green / 100, $"grass covers the green ground on the hillsides ({bare} of {green} spots bare; before {bareOld})");
                    // photographs of where it used to be bare
                    for (int k = 0; k < Mathf.Min(2, band.Count); k++)
                    {
                        var p = band[k * band.Count / 2];
                        var from = p + new Vector3(-p.x, 0, -p.z).normalized * 9f;
                        from.y = MapBuilder.Height(from.x, from.z);
                        yield return Shoot($"hillside_{k}", from, Quaternion.LookRotation(new Vector3(p.x - from.x, 0, p.z - from.z)).eulerAngles.y, 12f);
                    }
                }
            }

            // ---- taller grass (Settings > Display > Grass height) ----
            {
                float was = GameSettings.GrassHeight;
                foreach (float h in new[] { 1f, GameSettings.GrassHeightMax })
                {
                    GameSettings.SetGrassHeight(h, false);
                    yield return Shoot($"grass_height_{h * 100f:0}", mid, outYaw + 50f, 8f);
                }
                Check(Mathf.Approximately(GameSettings.GrassHeight, GameSettings.GrassHeightMax) && Mathf.Approximately(Shader.GetGlobalFloat("_GrassHeight"), GameSettings.GrassHeightMax), $"the grass height setting reaches the shader ({Shader.GetGlobalFloat("_GrassHeight"):F2})");
                GameSettings.SetGrassHeight(was, false);
            }

            // ---- the far grass even thicker (Settings > Display > Far grass thickness, now up to 150%) ----
            {
                float d0 = GameSettings.GrassDistance, n0 = GameSettings.GrassDensity, f0 = GameSettings.GrassFalloff;
                Check(GameSettings.GrassThicknessPercent(GameSettings.GrassFalloffMin) > 140f && Mathf.Approximately(GameSettings.GrassThicknessPercent(1f), 100f), $"far grass thickness goes past the old 100% (to {GameSettings.GrassThicknessPercent(GameSettings.GrassFalloffMin):0}%)");
                var high = mid + Vector3.up * 0.1f;
                foreach (float f in new[] { 1f, GameSettings.GrassFalloffMin })
                {
                    GameSettings.SetGrass(GameSettings.GrassDistanceMax, 1f, f, false);
                    yield return Shoot($"far_thickness_{GameSettings.GrassThicknessPercent(f):0}", high, outYaw, 3f);
                    Log($"far thickness {GameSettings.GrassThicknessPercent(f):0}% at 270 m: {g.DrawnPatches} patches, {g.DrawnTriangles / 1000}k triangles");
                    Check(g.DrawnPatches > 0 && g.DrawnPatches <= 1024, $"far thickness {GameSettings.GrassThicknessPercent(f):0}%: every patch fits ({g.DrawnPatches} of 1024)");
                }
                GameSettings.SetGrass(d0, n0, f0, false);
            }

            // ---- no outlines on the faded grass round you ----
            {
                Vector3 inWheat = Vector3.zero;
                for (int k = 0; k < 20000; k++)
                {
                    var p = new Vector3(Random.Range(-Cfg.MapHalf, Cfg.MapHalf), 0, Random.Range(-Cfg.MapHalf, Cfg.MapHalf));
                    if (g.WheatAt(p.x, p.z) > 0.85f && g.CoverAt(p.x, p.z) > 0.99f) { inWheat = new Vector3(p.x, MapBuilder.Height(p.x, p.z), p.z); break; }
                }
                Check(inWheat != Vector3.zero, "found a spot in the tall grass");
                bool post = GameSettings.PostFx;
                bool ol = GameSettings.PostExtraOn(GameSettings.PostExtra.Outlines);
                float ols = GameSettings.PostExtraStrength(GameSettings.PostExtra.Outlines);
                var shots = new List<(string, Vector3, float)> { ("tall_grass", inWheat, 4f), ("short_grass", mid, 25f) };
                foreach (var (what, at, pitch) in shots)
                {
                    if (at == Vector3.zero) continue;
                    pc.LocalTeleport(at + Vector3.up * 0.1f, outYaw);
                    pc.SetLook(outYaw, pitch);
                    GameSettings.SetPostFx(true, false);
                    yield return new WaitForSeconds(0.8f);
                    Time.timeScale = 0f; // (the wind stops: only the outlines change between the pictures)
                    Texture2D plain = null, masked = null, unmasked = null;
                    GameSettings.SetPostExtra(GameSettings.PostExtra.Outlines, false, 0.7f, false);
                    yield return GrabFrame(t => plain = t);
                    GameSettings.SetPostExtra(GameSettings.PostExtra.Outlines, true, 0.7f, false);
                    GrassField.OutlineMaskOff = false;
                    yield return GrabFrame(t => masked = t);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"grass_{shot++:00}_outlines_{what}_now.png"), masked.EncodeToPNG());
                    GrassField.OutlineMaskOff = true;
                    yield return GrabFrame(t => unmasked = t);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"grass_{shot++:00}_outlines_{what}_before.png"), unmasked.EncodeToPNG());
                    GrassField.OutlineMaskOff = false;
                    Time.timeScale = 1f;
                    // how much the outlines change the middle of the screen (where the grass round you is), and the top
                    float mNow = FrameDiff(plain, masked, 0.15f, 0.85f, 0.05f, 0.55f), mBefore = FrameDiff(plain, unmasked, 0.15f, 0.85f, 0.05f, 0.55f);
                    float far = FrameDiff(plain, masked, 0.1f, 0.9f, 0.6f, 0.95f);
                    Log($"outlines in the {what.Replace('_', ' ')}: the lower middle of the screen changes {mNow:F4} now, {mBefore:F4} before (the far part {far:F4})");
                    if (what == "tall_grass") Check(mNow < mBefore * 0.5f, $"no outlines on the see-through grass round you in the tall grass ({mNow:F4} vs {mBefore:F4})");
                    Destroy(plain); Destroy(masked); Destroy(unmasked);
                }
                GameSettings.SetPostExtra(GameSettings.PostExtra.Outlines, ol, ols, false);
                GameSettings.SetPostFx(post, false);
            }

            // hidden in PSX
            GameSettings.SetGraphics(1, false);
            yield return new WaitForSeconds(0.5f);
            yield return Shoot("psx_hidden", mid, outYaw + 50f, 8f);
            Check(g.DrawnPatches == 0, "no grass drawn in PSX graphics");
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);
            Log("grass test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>The next frame as it's shown (time stopped or not), handed to `got`.</summary>
        IEnumerator GrabFrame(System.Action<Texture2D> got)
        {
            for (int i = 0; i < 4; i++) yield return null;
            yield return new WaitForEndOfFrame();
            got(ScreenCapture.CaptureScreenshotAsTexture());
        }

        /// <summary>How different two frames are over a part of the screen (0..1 of its width / height, from the bottom):
        /// the mean difference in brightness.</summary>
        static float FrameDiff(Texture2D a, Texture2D b, float x0, float x1, float y0, float y1)
        {
            if (a == null || b == null || a.width != b.width || a.height != b.height) return -1f;
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            int w = a.width, h = a.height;
            double sum = 0;
            int n = 0;
            for (int y = (int)(y0 * h); y < (int)(y1 * h); y += 2)
                for (int x = (int)(x0 * w); x < (int)(x1 * w); x += 2)
                {
                    var ca = pa[y * w + x];
                    var cb = pb[y * w + x];
                    sum += System.Math.Abs((ca.r + ca.g + ca.b) - (cb.r + cb.g + cb.b)) / (3.0 * 255.0);
                    n++;
                }
            return n > 0 ? (float)(sum / n) : 0f;
        }
    }
}
