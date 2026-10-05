using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public static partial class GameSettings
    {
        /// <summary>Main menu: trees stand in the play space of the map behind the menu (just to look at - a match grows its own).</summary>
        public static readonly DisplayPref.Bool MenuTrees = new("menu.trees", "MAIN MENU", DisplayDefaults.MenuTrees);
    }

    /// <summary>
    /// The world behind the main menu. The camera cycles through a set of slow cinematic shots inside the map - a high
    /// orbit, a crane up over a base, a low dolly out of the other base toward the middle, a rise over the ball's dome, a
    /// glide through the wild, a long descent from a corner, an orbit round an alien machine - each a few seconds, cut
    /// through a quick fade to black (Hud.Menus draws MenuScene.Fade under the menu). It always stays above the ground,
    /// inside the map's glass and out of the tree trunks. And (Display setting "show trees on the main menu") trees stand
    /// in the play space where a match would grow them: only pictures, gone the moment a match starts.
    /// </summary>
    public static class MenuScene
    {
        public const int ShotCount = 7;
        /// <summary>Seconds a shot lasts, and how long the fade to black (and back) between two shots takes.</summary>
        public const float ShotSeconds = 10f, FadeSeconds = 0.55f;
        /// <summary>Test hooks: the shot showing now, how many have been shown, and (AutoTest) a shot to stay on (-1 = cycle).</summary>
        public static int Shot { get; private set; }
        public static int ShotsShown { get; private set; }
        public static int ForceShot = -1;
        /// <summary>How black the screen is right now for the cut between two shots (0..1; 0 while in a match).</summary>
        public static float Fade { get; private set; }
        /// <summary>Test hook: how many trees stand on the menu's map right now.</summary>
        public static int TreeCount => s_Trees ? s_TreePos.Count : 0;

        static float s_ShotTime, s_Floor = float.MinValue;
        static bool s_Started;
        static GameObject s_Trees;
        static readonly List<Vector3> s_TreePos = new List<Vector3>();

        /// <summary>Every frame (Bootstrap): the menu's trees and its camera while no match is on; nothing of either in one.</summary>
        public static void Tick(bool inSession)
        {
            bool menu = !inSession && PlayerController.Local == null;
            TickTrees(menu);
            if (!menu) { Fade = 0f; s_Started = false; return; }
            var cam = Camera.main;
            if (cam != null) TickCamera(cam.transform);
        }

        // ------------------------------------------------------------------ trees

        static void TickTrees(bool menu)
        {
            bool want = menu && GameSettings.MenuTrees.Value && MapBuilder.Root != null;
            if (!want)
            {
                if (s_Trees) Object.Destroy(s_Trees);
                s_Trees = null;
                return;
            }
            if (s_Trees) return; // (they hang off the world: a rebuilt map takes them with it, and they're planted again)
            s_Trees = new GameObject("menu trees");
            s_Trees.transform.SetParent(MapBuilder.Root, false);
            s_TreePos.Clear();
            // where a match would put them (NetGame.SpawnNodes): scattered over one team's part of the map, clear of
            // the base and the middle, and mirrored for every other team
            var rng = new System.Random(4242 + Cfg.MapKey);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = Cfg.MapHalf, area = half / 100f;
            area *= area * 2f / Cfg.Copies;
            int trees = Mathf.RoundToInt(Mathf.Max(6, Mathf.RoundToInt(24 * area)) * ThemeMaps.NodeMul(ResourceNode.Tree));
            for (int n = 0; n < trees; n++)
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var p = new Vector3(R(-half + 8, half - 8), 0, R(-half + 8, -5f));
                    if (!Cfg.InFirstSector(p, 4f)) continue;
                    if (Mathf.Abs(p.x - Cfg.BaseCenter[0].x) < Cfg.BaseHalf + 3 && Mathf.Abs(p.z - Cfg.BaseCenter[0].z) < Cfg.BaseHalf + 3) continue;
                    if (new Vector2(p.x, p.z).magnitude < MapBuilder.DomeRadius + 3f) continue;
                    if (!ThemeMaps.SpotOk(p)) continue;
                    bool close = false;
                    foreach (var q in s_TreePos) if ((q - p).sqrMagnitude < 3.5f * 3.5f) { close = true; break; }
                    if (close) continue;
                    int seed = rng.Next();
                    float yaw = R(0, 360);
                    for (int m = 0; m < Cfg.Copies; m++)
                    {
                        var q = Cfg.Copy(p, m);
                        q.y = MapBuilder.GroundHeight(q.x, q.z);
                        var go = new GameObject("menu tree");
                        go.transform.SetParent(s_Trees.transform, false);
                        go.transform.SetPositionAndRotation(q, Quaternion.Euler(0, yaw + m * 360f / Cfg.Copies, 0));
                        ResourceNode.BuildTreeVisual(go.transform, seed, false);
                        s_TreePos.Add(q);
                    }
                    break;
                }
        }

        // ------------------------------------------------------------------ camera

        static void TickCamera(Transform cam)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (!s_Started)
            {
                // back on the menu: start on a different shot each time, fading in
                s_Started = true;
                s_ShotTime = 0f;
                Shot = ForceShot >= 0 ? ForceShot % ShotCount : (Shot + 1 + Random.Range(0, ShotCount - 1)) % ShotCount;
                ShotsShown++;
                s_Floor = float.MinValue;
            }
            s_ShotTime += dt;
            if (ForceShot >= 0 && Shot != ForceShot % ShotCount) { Shot = ForceShot % ShotCount; s_ShotTime = 0f; s_Floor = float.MinValue; }
            if (s_ShotTime >= ShotSeconds)
            {
                // (cut while the screen is black)
                s_ShotTime = 0f;
                if (ForceShot < 0) Shot = (Shot + 1) % ShotCount;
                ShotsShown++;
                s_Floor = float.MinValue;
            }
            Fade = Mathf.Clamp01(Mathf.Max(1f - s_ShotTime / FadeSeconds, (s_ShotTime - (ShotSeconds - FadeSeconds)) / FadeSeconds));
            Pose(Shot, s_ShotTime / ShotSeconds, out var pos, out var look);
            pos = Contain(pos, dt);
            cam.position = pos;
            var dir = look - pos;
            if (dir.sqrMagnitude > 0.01f) cam.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>Where shot `i` is and what it looks at, `t` (0..1) of the way through it.</summary>
        public static void Pose(int i, float t, out Vector3 pos, out Vector3 look)
        {
            float H = Cfg.MapHalf, bh = Cfg.BaseHalf;
            float g0 = MapBuilder.GroundHeight(0, 0);
            Vector3 b0 = Cfg.BaseCenter[0], b1 = Cfg.BaseCenter[1];
            Vector3 in0 = -Cfg.BackDir(0), in1 = -Cfg.BackDir(1); // (from each base toward the middle)
            Vector3 side0 = Vector3.Cross(Vector3.up, in0), side1 = Vector3.Cross(Vector3.up, in1);
            float s = Mathf.SmoothStep(0f, 1f, t);
            switch (i)
            {
                default:
                {
                    // a high, slow orbit of the whole map
                    float a = (20f + 42f * t) * Mathf.Deg2Rad;
                    pos = new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)) * (0.7f * H) + Vector3.up * (g0 + 0.34f * H);
                    look = new Vector3(0, g0 + 2f, 0);
                    break;
                }
                case 1:
                {
                    // a crane up from behind the first base: the base in front, the map opening out beyond it
                    pos = b0 - in0 * (bh + 7f) + side0 * Mathf.Lerp(-9f, 9f, t) + Vector3.up * (Ground(b0) + Mathf.Lerp(3.5f, 13f, s));
                    look = b0 + in0 * (bh * 0.6f) + Vector3.up * (Ground(b0) + 2.5f);
                    break;
                }
                case 2:
                {
                    // a low dolly out of the second base's gate toward the middle
                    float d = bh + 5f + t * 0.32f * H;
                    pos = b1 + in1 * d + side1 * 2.5f;
                    pos.y = Ground(pos) + Mathf.Lerp(2.4f, 3.6f, t);
                    look = new Vector3(0, g0 + 5f, 0);
                    break;
                }
                case 3:
                {
                    // rising over the glass dome in the middle, turning round it
                    float a = (135f + 38f * t) * Mathf.Deg2Rad, r = MapBuilder.DomeRadius + Mathf.Lerp(15f, 27f, s);
                    pos = new Vector3(Mathf.Sin(a) * r, g0 + Mathf.Lerp(2.6f, 0.2f * H, s), -Mathf.Cos(a) * r);
                    look = new Vector3(0, g0 + Mathf.Lerp(4f, 2f, s), 0);
                    break;
                }
                case 4:
                {
                    // a glide through the wild between the first base and the middle
                    Vector3 from = new Vector3(-0.56f * H, 0, -0.4f * H), to = new Vector3(0.12f * H, 0, -0.27f * H);
                    pos = Vector3.Lerp(from, to, t);
                    pos.y = Ground(pos) + 4.2f;
                    var fwd = (to - from).normalized;
                    look = pos + fwd * 30f + new Vector3(0, -2.2f, 0) + Vector3.forward * 9f;
                    break;
                }
                case 5:
                {
                    // a long descent from a corner down to head height
                    Vector3 from = new Vector3(0.68f * H, g0 + 0.3f * H, 0.68f * H), to = new Vector3(0.3f * H, g0 + 6.5f, 0.38f * H);
                    pos = Vector3.Lerp(from, to, s);
                    look = Vector3.Lerp(new Vector3(0, g0 + 2f, 0), b1 + Vector3.up * (Ground(b1) + 3f), s * 0.6f);
                    break;
                }
                case 6:
                {
                    // round the second base's alien machine, close
                    var m = Cfg.MachinePos(1);
                    float baseAng = Mathf.Atan2(in1.x, in1.z) * Mathf.Rad2Deg;
                    float a = (baseAng - 34f + 68f * t) * Mathf.Deg2Rad, r = bh + 9f;
                    pos = new Vector3(m.x + Mathf.Sin(a) * r, 0, m.z + Mathf.Cos(a) * r);
                    pos.y = Ground(m) + Mathf.Lerp(7.5f, 5f, s);
                    look = m + Vector3.up * 2.2f;
                    break;
                }
            }
        }

        static float Ground(Vector3 p) => MapBuilder.GroundHeight(p.x, p.z);

        /// <summary>Keeps a shot's camera in the map: inside the edge and under the glass, out of the tree trunks, and
        /// above the ground (the highest of it close by - eased, so it doesn't bob over every bump).</summary>
        static Vector3 Contain(Vector3 p, float dt)
        {
            float lim = Cfg.MapHalf - 5f;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            // out of the trunks (and most of the leaves) of the menu's trees
            if (s_Trees)
                for (int i = 0; i < s_TreePos.Count; i++)
                {
                    var q = s_TreePos[i];
                    float dx = p.x - q.x, dz = p.z - q.z, d2 = dx * dx + dz * dz;
                    const float keep = 3.2f;
                    if (d2 >= keep * keep || p.y > q.y + 9f) continue;
                    float d = Mathf.Sqrt(d2);
                    if (d < 0.01f) { dx = 1f; dz = 0f; d = 1f; }
                    p.x = q.x + dx / d * keep;
                    p.z = q.z + dz / d * keep;
                }
            float g = Ground(p);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f;
                g = Mathf.Max(g, MapBuilder.GroundHeight(p.x + Mathf.Cos(a) * 2.5f, p.z + Mathf.Sin(a) * 2.5f));
            }
            float floor = g + 1.8f;
            s_Floor = s_Floor == float.MinValue ? floor : Mathf.Lerp(s_Floor, floor, 1f - Mathf.Exp(-3f * dt));
            p.y = Mathf.Max(p.y, Mathf.Max(s_Floor, g + 1.2f));
            if (MapDome.Built && MapDome.Shoulder > 8f) p.y = Mathf.Min(p.y, Mathf.Max(g + 1.2f, MapDome.Shoulder - 3f));
            return p;
        }
    }
}
