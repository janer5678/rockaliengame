using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>-autotest world -host -solo -map plains|highlands -shotdir DIR: the Normal look's world - tall swaying
    /// pines (the trunk never pokes out of the top), the castle walls and their towers, the bases' waving flags, the
    /// clouds, Settings > Display (grass distance / density and the world colours), a colour change, and frame times.</summary>
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
            float worst = float.MaxValue;
            for (float h = 4.5f; h <= 7.0001f; h += 0.05f) worst = Mathf.Min(worst, ResourceNode.PineClearance(h));
            Check(worst > 0.05f, $"pine trunks end inside the needles for every tree height, even swaying (closest {worst:F2} m)");
            int pines = 0, bad = 0;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Kind.Value != ResourceNode.Tree) continue;
                var needles = n.transform.Find("visual/needles");
                var trunk = n.transform.Find("visual/trunk");
                if (needles == null || trunk == null) continue;
                pines++;
                var nb = needles.GetComponent<Renderer>().bounds;
                var tb = trunk.GetComponent<Renderer>().bounds;
                if (tb.max.y > nb.max.y - 0.8f || tb.max.y < nb.min.y + 1f) bad++;
            }
            Check(pines > 0 && bad == 0, $"{pines} pines, trunk tops all well inside the needles ({bad} not)");
            var castle = root != null ? root.Find("Castle") : null;
            int castleParts = castle != null ? castle.GetComponentsInChildren<MeshRenderer>().Length : 0;
            int castleTris = 0;
            if (castle != null) foreach (var mf in castle.GetComponentsInChildren<MeshFilter>()) castleTris += (int)mf.sharedMesh.GetIndexCount(0) / 3;
            Check(castleParts > 8, $"castle walls built ({castleParts} meshes, {castleTris / 1000}k triangles)");
            // the plain boundary walls are still what stops you, hidden in Normal
            int walls = 0, wallsShown = 0;
            foreach (var bc in root.GetComponentsInChildren<BoxCollider>())
            {
                var c = bc.transform.position;
                bool edge = (Mathf.Abs(Mathf.Abs(c.x) - (half + 1)) < 0.01f && Mathf.Abs(c.z) < 1f) || (Mathf.Abs(Mathf.Abs(c.z) - (half + 1)) < 0.01f && Mathf.Abs(c.x) < 1f);
                if (!edge || bc.isTrigger) continue;
                walls++;
                if (bc.GetComponent<Renderer>().enabled) wallsShown++;
            }
            Check(walls == 4 && wallsShown == 0, $"the 4 boundary walls still collide, drawn as the castle ({walls} walls, {wallsShown} plain ones shown)");
            var clouds = root.GetComponentInChildren<CloudLayer>();
            Check(clouds != null && clouds.Count > 5, $"clouds up ({(clouds != null ? clouds.Count : 0)})");
            // you can't walk out through the castle
            pc.LocalTeleport(new Vector3(0, MapBuilder.Height(0, -half + 3f) + 0.2f, -half + 3f), 180f);
            yield return new WaitForSeconds(0.3f);
            bool blocked = Physics.Raycast(new Vector3(0, MapBuilder.Height(0, -half + 3f) + 1f, -half + 3f), Vector3.back, out var wallHit, 6f, ~0, QueryTriggerInteraction.Ignore);
            Check(blocked && wallHit.point.z > -half - 0.05f, $"the wall stops you at the edge of the map (hit at z {(blocked ? wallHit.point.z : 0f):F2}, edge {-half})");

            int shot = 0;
            IEnumerator Shoot(string name, Vector3 at, float yaw, float pitch, float wait = 0.8f)
            {
                pc.LocalTeleport(at + Vector3.up * 0.1f, yaw);
                pc.SetLook(yaw, pitch);
                yield return new WaitForSeconds(wait);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"world_{shot++:00}_{name}.png"));
                yield return new WaitForSeconds(0.3f);
            }
            Vector3 Ground(float x, float z) => new Vector3(x, MapBuilder.Height(x, z), z);
            float YawTo(Vector3 from, Vector3 to) => Quaternion.LookRotation(new Vector3(to.x - from.x, 0, to.z - from.z)).eulerAngles.y;

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
                var under = Ground(tp.x + away.x * 2.6f, tp.z + away.z * 2.6f);
                yield return Shoot("tree_under_looking_up", under, YawTo(under, tp), -70f);
                var far = Ground(tp.x + away.x * 32f, tp.z + away.z * 32f);
                yield return Shoot("trees_far", far, YawTo(far, tp), -4f);
                // the wind: the same tree a moment later
                yield return Shoot("tree_close_later", close, YawTo(close, tp), -22f, 1.4f);
            }

            // ---- castle walls ----
            // our own end of the map: the wall behind our base, a tower with a banner, a corner tower
            float sideZ = bcn.z < 0 ? -half : half;
            float faceYaw = sideZ < 0 ? 180f : 0f;
            var wallSpot = Ground(half * 0.35f, sideZ - Mathf.Sign(sideZ) * 16f);
            yield return Shoot("castle_wall", wallSpot, faceYaw, -6f);
            int towers = Mathf.Max(2, Mathf.RoundToInt(2f * half / 42f));
            float tx = -half + 2f * half * 0.5f / towers;
            var towerSpot = Ground(tx, sideZ - Mathf.Sign(sideZ) * 14f);
            yield return Shoot("castle_tower_banner", towerSpot, faceYaw, -14f);
            var cornerSpot = Ground(half - 18f, sideZ - Mathf.Sign(sideZ) * 18f);
            yield return Shoot("castle_corner_tower", cornerSpot, YawTo(cornerSpot, new Vector3(half, 0, sideZ)), -14f);
            var longSpot = Ground(-half + 6f, sideZ - Mathf.Sign(sideZ) * 5f);
            yield return Shoot("castle_along_the_wall", longSpot, YawTo(longSpot, new Vector3(half, 0, sideZ)), -3f);

            // ---- flags ----
            if (!Cfg.Builder)
            {
                var flagPole = bcn + new Vector3(Cfg.BaseHalf + 1f, 0, (bcn.z < 0 ? 1f : -1f) * (Cfg.BaseHalf + 1f));
                var fs = Ground(flagPole.x - 5f, flagPole.z - Mathf.Sign(flagPole.z - bcn.z) * 5f);
                fs = Ground(flagPole.x - 4.5f, flagPole.z + (bcn.z < 0 ? -3f : 3f));
                yield return Shoot("flag", fs, YawTo(fs, flagPole), -30f);
                yield return Shoot("flag_later", fs, YawTo(fs, flagPole), -30f, 0.45f);
            }

            // ---- clouds ----
            var mid = Ground(Mathf.Lerp(bcn.x, 0, 0.5f), Mathf.Lerp(bcn.z, 0, 0.5f));
            float outYaw = YawTo(bcn, Vector3.zero);
            yield return Shoot("clouds", mid, outYaw + 30f, -32f);
            yield return Shoot("meadow_default", mid, outYaw + 50f, 6f);

            // ---- frame time (the default look) ----
            IEnumerator Measure(string what, System.Action<float> done)
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
            float tDefault = 0, tLow = 0, tFar = 0;
            pc.LocalTeleport(mid + Vector3.up * 0.1f, outYaw + 50f); pc.SetLook(outYaw + 50f, 6f);
            yield return Measure("default", v => tDefault = v);
            var g = GrassField.Current;
            int trisDefault = g != null ? g.DrawnTriangles : 0;

            // ---- Settings > Display ----
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"world_{shot++:00}_settings_display.png"));
            yield return new WaitForSeconds(0.3f);

            // grass: closer and thinner, then the most
            GameSettings.SetGrass(30f, 0.4f, false);
            yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"world_{shot++:00}_settings_grass_low.png"));
            yield return new WaitForSeconds(0.3f);
            pc.Paused = false;
            yield return Shoot("grass_30m_40pct", mid, outYaw + 50f, 6f);
            yield return Measure("low", v => tLow = v);
            int trisLow = g != null ? g.DrawnTriangles : 0;
            Check(trisLow < trisDefault * 0.6f, $"less grass drawn at 30 m / 40% ({trisLow / 1000}k triangles, was {trisDefault / 1000}k)");
            GameSettings.SetGrass(GameSettings.GrassDistanceMax, 1f, false);
            yield return Shoot("grass_90m_100pct", mid, outYaw + 50f, 6f);
            yield return Measure("far", v => tFar = v);
            int trisFar = g != null ? g.DrawnTriangles : 0;
            Check(trisFar > trisDefault, $"more grass drawn at 90 m ({trisFar / 1000}k triangles)");
            Log($"frame time: default look {tDefault:F2} ms ({trisDefault / 1000}k grass triangles), grass 30 m / 40% {tLow:F2} ms, grass 90 m / 100% {tFar:F2} ms");
            GameSettings.SetGrass(GameSettings.GrassDistanceDefault, GameSettings.GrassDensityDefault, false);

            // ---- a colour change, applied live ----
            var snowy = GameSettings.WorldColorPresets[(int)GameSettings.WorldColor.Ground][5];
            GameSettings.SetWorldColor(GameSettings.WorldColor.Ground, snowy, false);
            GameSettings.SetWorldColor(GameSettings.WorldColor.Rock, GameSettings.WorldColorPresets[(int)GameSettings.WorldColor.Rock][3], false);
            GameSettings.SetWorldColor(GameSettings.WorldColor.Grass, GameSettings.WorldColorPresets[(int)GameSettings.WorldColor.Grass][5], false);
            GameSettings.SetWorldColor(GameSettings.WorldColor.Leaves, GameSettings.WorldColorPresets[(int)GameSettings.WorldColor.Leaves][4], false);
            GameSettings.SetWorldColor(GameSettings.WorldColor.Clouds, GameSettings.WorldColorPresets[(int)GameSettings.WorldColor.Clouds][2], false);
            GameSettings.SetWorldColor(GameSettings.WorldColor.Sky, GameSettings.WorldColorPresets[(int)GameSettings.WorldColor.Sky][3], false);
            yield return new WaitForSeconds(0.3f);
            Check(WorldLook.GroundMaterial(false).color == snowy, "the ground takes the colour picked");
            if (tree != null)
            {
                var tp = tree.transform.position;
                var away = (new Vector3(bcn.x - tp.x, 0, bcn.z - tp.z)).normalized;
                var far = Ground(tp.x + away.x * 16f, tp.z + away.z * 16f);
                yield return Shoot("colours_changed", far, YawTo(far, tp), -12f);
            }
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"world_{shot++:00}_settings_colours_changed.png"));
            yield return new WaitForSeconds(0.3f);
            pc.Paused = false;
            GameSettings.ResetWorldLook(false);
            yield return new WaitForSeconds(0.2f);
            Check(WorldLook.GroundMaterial(false).color == GameSettings.WorldColorDefaults[0], "reset puts the default colours back");

            // ---- PSX and AI PSX keep their own looks ----
            var castleRs = castle != null ? castle.GetComponentsInChildren<Renderer>() : new Renderer[0];
            bool AnyCastle() { foreach (var r in castleRs) if (r && r.enabled) return true; return false; }
            int PlainWalls()
            {
                int k = 0;
                foreach (var bc in root.GetComponentsInChildren<BoxCollider>())
                {
                    var c = bc.transform.position;
                    if (((Mathf.Abs(Mathf.Abs(c.x) - (half + 1)) < 0.01f && Mathf.Abs(c.z) < 1f) || (Mathf.Abs(Mathf.Abs(c.z) - (half + 1)) < 0.01f && Mathf.Abs(c.x) < 1f)) && bc.GetComponent<Renderer>().enabled) k++;
                }
                return k;
            }
            GameSettings.SetGraphics(1, false);
            yield return new WaitForSeconds(0.5f);
            yield return Shoot("psx_walls", wallSpot, faceYaw, -6f);
            Check(!AnyCastle() && PlainWalls() == 4 && !clouds.Shown, "PSX: the plain (concrete) walls, no castle, no clouds");
            GameSettings.SetGraphics(2, false);
            yield return new WaitForSeconds(0.8f);
            Check(!AnyCastle() && PlainWalls() == 4 && !clouds.Shown, "AI PSX: the plain walls, no castle, no clouds");
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);
            Check(AnyCastle() && PlainWalls() == 0 && clouds.Shown, "Normal again: the castle and the clouds are back");
            Log("world test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
