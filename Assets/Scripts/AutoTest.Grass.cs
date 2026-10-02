using System.Collections;
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
            // a wheat patch, if there's one near
            for (int k = 0; k < 4000; k++)
            {
                var p = new Vector3(Random.Range(-Cfg.MapHalf, Cfg.MapHalf), 0, Random.Range(-Cfg.MapHalf, Cfg.MapHalf));
                if (g.WheatAt(p.x, p.z) < 0.95f || g.CoverAt(p.x, p.z) < 0.99f) continue;
                var from = p - new Vector3(6, 0, 0);
                from.y = MapBuilder.Height(from.x, from.z);
                yield return Shoot("wheat", from, 90f, 10f);
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
    }
}
