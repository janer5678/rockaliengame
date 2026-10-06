using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The world behind the main menu. The camera films it in one long, unbroken shot: a slow loop through the map, mostly
    /// close and low - round the crashed UFO in the middle, out through the wild to a base, across in front of its alien
    /// machine, back through the wild (a little higher, a wider look) and round the crash site again to the next base, and
    /// so on round every base. The route is a smooth curve (centripetal Catmull-Rom through those stops, then blurred) at a
    /// smoothed height over the highest ground near it - not every bump of the floor - and the camera glides along it at
    /// an eased speed (slower past what's worth a look), turning its head gently. It's planned once per map (Bake), so it
    /// stays out of the trees (round them, or over their tops), the boulders and the ball's glass dome, out of anything
    /// solid, under the map's glass, and always with a clear view of what it's looking at (it rises where something's in the
    /// way). It only fades in from black when the menu comes up (Hud.Menus draws MenuScene.Fade under the menu). And trees (always) stand
    /// in the play space where a match would grow them: only pictures, gone the moment a match starts.
    /// </summary>
    public static class MenuScene
    {
        /// <summary>Seconds of the fade in from black as the menu comes up.</summary>
        public const float FadeSeconds = 1.2f;
        /// <summary>How fast the camera glides along (m/s): slowly past the crash site and the bases, faster through the wild.</summary>
        public const float SlowSpeed = 3.2f, BaseSpeed = 3.8f, WildSpeed = 6f;
        /// <summary>The menu's map page (Hud.MapPreview): a quick look round the map - no crash site, low (about a
        /// player's height) through the wild and past every base, PreviewSpeed times as fast, from the wild at once.</summary>
        public static bool Preview;
        public const float PreviewSpeed = 4f;
        static bool s_BakedPreview;
        /// <summary>How black the screen is right now (0..1; 0 while in a match): only the fade in as the menu comes up.</summary>
        public static float Fade { get; private set; }
        /// <summary>Test hook: how many trees stand on the menu's map right now.</summary>
        public static int TreeCount => s_Trees ? s_TreePos.Count : 0;
        /// <summary>Test hooks: how long the camera's loop is (m), how far along it the camera is, and how many of its points
        /// had to rise for a clear view of what they look at.</summary>
        public static float RouteLength => s_Len;
        public static float Along => s_Along;
        public static int Lifted { get; private set; }

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
            // (always on: "show trees on the main menu" isn't a setting any more - DisplayDefaults.MenuTrees)
            bool want = menu && DisplayDefaults.MenuTrees && MapBuilder.Root != null;
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

        /// <summary>Metres between two of the route's points (it's planned as a ring of them, then read between them).</summary>
        const float Step = 1f;
        /// <summary>How far the route keeps out from a menu tree (round its leaves) and a boulder's side, and how high over
        /// a tree's top it goes when it can't go round (PSX trees are twice as tall).</summary>
        const float TreeKeep = 4.6f, BoulderKeep = 2.6f;
        static float TreeTop => PsxArt.On ? 20f : 10.5f;
        /// <summary>How far round the route looks for the highest ground under it, and how far its height is blurred along it (m).</summary>
        const float GroundReach = 7f;
        const int HeightBlur = 10;

        /// <summary>One stop on the loop: where it passes (flat), how high over the ground, what it looks at there (Ahead:
        /// down the way it's going) and how fast it glides by. Crash: a pass of the crashed UFO (where the loop can start).</summary>
        struct Stop { public Vector3 At, Look; public float Up, Speed; public bool Ahead, Crash; }

        static Vector3[] s_Pos, s_Look;
        static float[] s_Speed;
        static readonly List<float> s_CrashAt = new List<float>();
        static float s_Len, s_Along, s_FadeT;
        static int s_BakedRoot, s_BakedTrees = -1, s_Starts;
        static Vector3 s_LookNow;
        static Quaternion s_RotNow = Quaternion.identity;

        /// <summary>(tests) leave the camera alone (AutoTest.Lobby.cs takes the map pictures with it).</summary>
        public static bool TestHold;

        static void TickCamera(Transform cam)
        {
            if (TestHold) return; // (tests: the camera is theirs - the map pictures)
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (MapBuilder.Root == null) return;
            int root = MapBuilder.Root.GetInstanceID();
            if (s_Pos == null || root != s_BakedRoot || TreeCount != s_BakedTrees || Preview != s_BakedPreview)
            {
                // a new map (or its trees came or went): plan the loop again (the same map: carry on from as far round it)
                float was = s_Len > 0f ? s_Along / s_Len : 0f;
                bool same = root == s_BakedRoot && s_Started && Preview == s_BakedPreview;
                s_BakedPreview = Preview;
                Bake();
                s_BakedRoot = root;
                s_BakedTrees = TreeCount;
                if (same) s_Along = was * s_Len; else s_Started = false;
            }
            if (s_Pos == null || s_Pos.Length < 4) return;
            if (!s_Started)
            {
                // back on the menu: fade in a little before a pass of the crash site (a different one each time)
                s_Started = true;
                s_FadeT = 0f;
                s_Starts += 1 + Random.Range(0, Mathf.Max(1, s_CrashAt.Count));
                s_Along = Wrap(s_CrashAt.Count > 0 ? s_CrashAt[s_Starts % s_CrashAt.Count] - 14f : 0f);
                s_LookNow = Read(s_Look, s_Along);
                var d0 = s_LookNow - Read(s_Pos, s_Along);
                if (d0.sqrMagnitude > 0.01f) s_RotNow = Quaternion.LookRotation(d0.normalized, Vector3.up);
            }
            s_FadeT += dt;
            Fade = 1f - Mathf.SmoothStep(0f, 1f, s_FadeT / (Preview ? FadeSeconds * 0.4f : FadeSeconds));
            // on round the loop at its (already eased) speed; the look follows its target softly, the head turns softly
            s_Along = Wrap(s_Along + Read(s_Speed, s_Along) * (Preview ? PreviewSpeed : 1f) * dt);
            var pos = Read(s_Pos, s_Along);
            s_LookNow = Vector3.Lerp(s_LookNow, Read(s_Look, s_Along), 1f - Mathf.Exp((Preview ? -4f : -1.6f) * dt));
            var dir = s_LookNow - pos;
            if (dir.sqrMagnitude > 0.01f) s_RotNow = Quaternion.Slerp(s_RotNow, Quaternion.LookRotation(dir.normalized, Vector3.up), 1f - Mathf.Exp((Preview ? -7f : -4f) * dt));
            cam.SetPositionAndRotation(pos, s_RotNow);
        }

        static float Wrap(float s) => s_Len > 0f ? Mathf.Repeat(s, s_Len) : 0f;

        /// <summary>The route's value `s` metres round the loop (between its two nearest points).</summary>
        static Vector3 Read(Vector3[] a, float s)
        {
            float f = Wrap(s) / Step;
            int i = Mathf.FloorToInt(f) % a.Length;
            return Vector3.Lerp(a[i], a[(i + 1) % a.Length], f - Mathf.Floor(f));
        }

        static float Read(float[] a, float s)
        {
            float f = Wrap(s) / Step;
            int i = Mathf.FloorToInt(f) % a.Length;
            return Mathf.Lerp(a[i], a[(i + 1) % a.Length], f - Mathf.Floor(f));
        }

        static float Ground(Vector3 p) => MapBuilder.GroundHeight(p.x, p.z);

        /// <summary>
        /// The stops of the loop: for each base in turn round the middle - round the crashed UFO (close and low one time,
        /// further out and higher the next, looking at it), out through the wild toward the base (low, looking ahead), in
        /// across the front of the base past its alien machine (close, looking at it), then back through the wild on the
        /// other side, a little higher for a wider look - and on to the crash site again.
        /// </summary>
        static List<Stop> Stops()
        {
            var stops = new List<Stop>();
            float bh = Cfg.BaseHalf, lim = Cfg.MapHalf - 10f;
            var crash = CrashSite.Current != null ? CrashSite.Current.SaucerCentre : CrashSite.Dir * CrashSite.SaucerDist + Vector3.up * Ground(Vector3.zero);
            var crashLook = crash + Vector3.up * 1f;
            var c = new Vector3(crash.x, 0f, crash.z);
            var order = new List<int>();
            for (int t = 0; t < Mathf.Min(Cfg.TeamCount, Cfg.BaseCenter.Length); t++) order.Add(t);
            order.Sort((a, b) => Mathf.Atan2(Cfg.BaseCenter[a].x, Cfg.BaseCenter[a].z).CompareTo(Mathf.Atan2(Cfg.BaseCenter[b].x, Cfg.BaseCenter[b].z)));
            void Add(Vector3 at, float up, Vector3 look, float speed, bool ahead = false, bool isCrash = false)
            {
                at.y = 0f;
                at.x = Mathf.Clamp(at.x, -lim, lim);
                at.z = Mathf.Clamp(at.z, -lim, lim);
                stops.Add(new Stop { At = at, Up = up, Look = look, Speed = speed, Ahead = ahead, Crash = isCrash });
            }
            for (int k = 0; k < order.Count; k++)
            {
                int t = order[k];
                Vector3 u = Cfg.BackDir(t), side = Vector3.Cross(Vector3.up, u), b = Cfg.BaseCenter[t];
                float far = new Vector2(b.x, b.z).magnitude;
                var machine = Cfg.MachinePos(t) + Vector3.up * 1.6f;
                // round the crash site, from its far side to the side toward this base (alternately close and low, and
                // further out and higher, looking down into the crater)
                bool close = k % 2 == 0;
                float r = close ? 15f : 20f, up = close ? 3.4f : 7f;
                if (!Preview)
                {
                    Add(c + (CrashSite.Dir - u * 0.9f).normalized * r, up, crashLook, SlowSpeed, isCrash: true);
                    Add(c + (CrashSite.Dir + u * 0.7f).normalized * r, up + 0.6f, crashLook, SlowSpeed);
                }
                // out through the wild, low
                Add(u * (far * 0.42f) + side * (far * 0.18f), Preview ? 2.4f : 4.4f, default, WildSpeed, true);
                // across the front of the base, past its machine
                Add(b - u * (bh + 6f) + side * (bh * 0.45f), Preview ? 2.6f : 4.2f, machine, BaseSpeed);
                Add(b - u * (bh + 4f) - side * (bh * 0.55f), Preview ? 2.8f : 4.8f, machine, BaseSpeed);
                // back through the wild on the other side, a little higher
                Add(u * (far * 0.5f) - side * (far * 0.3f), Preview ? 3.2f : 8f, default, WildSpeed, true);
            }
            // the "ahead" stops look down the way the loop goes there, at the ground a way in front
            for (int i = 0; i < stops.Count; i++)
            {
                if (!stops[i].Ahead) continue;
                var s = stops[i];
                var fwd = stops[(i + 1) % stops.Count].At - stops[(i + stops.Count - 1) % stops.Count].At;
                fwd.y = 0f;
                var look = s.At + (fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward) * 22f;
                look.y = Ground(look) + 1.5f;
                s.Look = look;
                stops[i] = s;
            }
            return stops;
        }

        /// <summary>Centripetal Catmull-Rom (no loops or overshoot where the stops are unevenly spaced) from p1 to p2.</summary>
        static Vector3 Spline(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f, t1 = t0 + Mathf.Max(0.01f, Mathf.Sqrt((p1 - p0).magnitude));
            float t2 = t1 + Mathf.Max(0.01f, Mathf.Sqrt((p2 - p1).magnitude)), t3 = t2 + Mathf.Max(0.01f, Mathf.Sqrt((p3 - p2).magnitude));
            float u = Mathf.Lerp(t1, t2, t);
            var a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
            var a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
            var a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
            var b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
            var b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
            return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
        }

        /// <summary>Plain Catmull-Rom (for the look points: two stops can look at the very same thing).</summary>
        static Vector3 Uniform(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);

        /// <summary>
        /// Plan the loop for this map: the stops joined up by a smooth curve, sampled every metre; pushed out of the trees,
        /// the boulders and the ball's dome (and blurred again, so the pushes don't leave kinks); its height the highest
        /// ground near it plus the stop's height, never under a tree's top or a boulder's where it passes over one, then
        /// heavily smoothed (only ever upward); then every point checked for a clear view of what it looks at - nothing solid
        /// (the glass doesn't count), no tree, no boulder in the way, and not inside anything - rising until it has one, and
        /// smoothed again. The speed's blurred too, so it eases between slow and fast.
        /// </summary>
        static void Bake()
        {
            var stops = Stops();
            int m = stops.Count;
            s_CrashAt.Clear();
            if (m < 3) { s_Pos = null; return; }
            // the curve, densely, with how far round it each bit is
            const int Sub = 48;
            var pts = new List<Vector3>(m * Sub + 1);
            var looks = new List<Vector3>(m * Sub + 1);
            var ups = new List<float>(m * Sub + 1);
            var speeds = new List<float>(m * Sub + 1);
            var dist = new List<float>(m * Sub + 1);
            float len = 0f;
            for (int i = 0; i < m; i++)
            {
                Stop a = stops[(i + m - 1) % m], b = stops[i], c = stops[(i + 1) % m], d = stops[(i + 2) % m];
                if (b.Crash) s_CrashAt.Add(len);
                for (int k = 0; k < Sub; k++)
                {
                    float t = k / (float)Sub, e = t * t * (3f - 2f * t);
                    var p = Spline(a.At, b.At, c.At, d.At, t);
                    if (pts.Count > 0) len += Vector3.Distance(pts[pts.Count - 1], p);
                    pts.Add(p);
                    dist.Add(len);
                    looks.Add(Uniform(a.Look, b.Look, c.Look, d.Look, t));
                    ups.Add(Mathf.Lerp(b.Up, c.Up, e));
                    speeds.Add(Mathf.Lerp(b.Speed, c.Speed, e));
                }
            }
            len += Vector3.Distance(pts[pts.Count - 1], pts[0]);
            // ...evenly every Step metres round the loop
            int n = Mathf.Max(8, Mathf.RoundToInt(len / Step));
            float step = len / n;
            for (int i = 0; i < s_CrashAt.Count; i++) s_CrashAt[i] *= Step / step;
            s_Len = n * Step;
            var pos = new Vector3[n];
            var look = new Vector3[n];
            var up = new float[n];
            var speed = new float[n];
            int j = 0;
            for (int i = 0; i < n; i++)
            {
                float want = i * step;
                while (j + 1 < pts.Count && dist[j + 1] < want) j++;
                int j1 = (j + 1) % pts.Count;
                float d1 = j + 1 < pts.Count ? dist[j + 1] : len;
                float f = Mathf.Clamp01((want - dist[j]) / Mathf.Max(1e-4f, d1 - dist[j]));
                pos[i] = Vector3.Lerp(pts[j], pts[j1], f);
                look[i] = Vector3.Lerp(looks[j], looks[j1], f);
                up[i] = Mathf.Lerp(ups[j], ups[j1], f);
                speed[i] = Mathf.Lerp(speeds[j], speeds[j1], f);
            }
            // round the trees, the boulders and the ball's dome (and smooth again: a few times over)
            for (int it = 0; it < 4; it++)
            {
                PushClear(pos);
                Blur(pos, 5, 2);
            }
            PushClear(pos);
            Blur(pos, 2, 1);
            Blur(look, 6, 2);
            Blur(speed, 18, 3);
            Blur(up, 8, 2);

            // the height: over the highest ground near it, over any tree or boulder it still passes over - smoothed
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                float g = GroundMax(pos[i]);
                float floor = g + 1.6f;
                var p = pos[i];
                for (int q = 0; q < s_TreePos.Count; q++)
                {
                    var tp = s_TreePos[q];
                    float dx = p.x - tp.x, dz = p.z - tp.z;
                    if (dx * dx + dz * dz < (TreeKeep + 1.5f) * (TreeKeep + 1.5f)) floor = Mathf.Max(floor, tp.y + TreeTop + 1f);
                }
                for (int q = 0; q < MapScenery.Boulders.Count && q < MapScenery.BoulderRadius.Count; q++)
                {
                    var bp = MapScenery.Boulders[q];
                    float br = MapScenery.BoulderRadius[q], dx = p.x - bp.x, dz = p.z - bp.z;
                    if (dx * dx + dz * dz < (br + 1.5f) * (br + 1.5f)) floor = Mathf.Max(floor, bp.y + br * 1.3f + 1.5f);
                }
                y[i] = Mathf.Max(g + up[i], floor);
                var lk = look[i];
                lk.y = Mathf.Max(lk.y, Ground(lk) + 0.8f);
                look[i] = lk;
            }
            SmoothAbove(y, HeightBlur);
            // a clear view of what it looks at, and not inside anything solid: rise until it is
            Physics.SyncTransforms();
            int lifted = 0;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(pos[i].x, y[i], pos[i].z);
                for (int k = 0; k <= 16; k++)
                {
                    var at = p + Vector3.up * (k * 1.5f);
                    if (InSolid(at) || !Seen(at, look[i])) continue;
                    if (k > 0) { y[i] = at.y; lifted++; }
                    break;
                }
            }
            Lifted = lifted;
            SmoothAbove(y, HeightBlur);
            // under the map's glass
            float cap = MapDome.Built && MapDome.Shoulder > 8f ? MapDome.Shoulder - 3f : float.MaxValue;
            for (int i = 0; i < n; i++) pos[i].y = Mathf.Min(y[i], Mathf.Max(cap, Ground(pos[i]) + 1.2f));
            s_Pos = pos;
            s_Look = look;
            s_Speed = speed;
        }

        /// <summary>Push the route's points (flat) out of the menu's trees, the boulders and the ball's glass dome, and inside the map's edge.</summary>
        static void PushClear(Vector3[] pos)
        {
            float dome = MapBuilder.DomeRadius + 4f, lim = Cfg.MapHalf - 9f;
            for (int i = 0; i < pos.Length; i++)
            {
                var p = pos[i];
                float r = new Vector2(p.x, p.z).magnitude;
                if (r < dome)
                {
                    var o = r > 0.01f ? new Vector3(p.x / r, 0f, p.z / r) : CrashSite.Dir;
                    p.x = o.x * dome;
                    p.z = o.z * dome;
                }
                for (int q = 0; q < s_TreePos.Count; q++) Out(ref p, s_TreePos[q], TreeKeep);
                for (int q = 0; q < MapScenery.Boulders.Count && q < MapScenery.BoulderRadius.Count; q++) Out(ref p, MapScenery.Boulders[q], MapScenery.BoulderRadius[q] + BoulderKeep);
                p.x = Mathf.Clamp(p.x, -lim, lim);
                p.z = Mathf.Clamp(p.z, -lim, lim);
                pos[i] = p;
            }
        }

        static void Out(ref Vector3 p, Vector3 from, float keep)
        {
            float dx = p.x - from.x, dz = p.z - from.z, d2 = dx * dx + dz * dz;
            if (d2 >= keep * keep) return;
            float d = Mathf.Sqrt(d2);
            if (d < 0.01f) { dx = 1f; dz = 0f; d = 1f; }
            p.x = from.x + dx / d * keep;
            p.z = from.z + dz / d * keep;
        }

        /// <summary>The highest ground within GroundReach of p (so the route rides over the bumps, not up and down them).</summary>
        static float GroundMax(Vector3 p)
        {
            float g = Ground(p);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f;
                g = Mathf.Max(g, MapBuilder.GroundHeight(p.x + Mathf.Cos(a) * GroundReach, p.z + Mathf.Sin(a) * GroundReach));
                g = Mathf.Max(g, MapBuilder.GroundHeight(p.x + Mathf.Cos(a + 0.4f) * GroundReach * 0.5f, p.z + Mathf.Sin(a + 0.4f) * GroundReach * 0.5f));
            }
            return g;
        }

        /// <summary>Smooth a ring of heights without ever going under any of them: the highest within 3r first, then three
        /// box blurs of radius r (each point's average is then over values all at least its own).</summary>
        static void SmoothAbove(float[] a, int r)
        {
            int n = a.Length;
            var mx = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = float.MinValue;
                for (int k = -3 * r; k <= 3 * r; k++) v = Mathf.Max(v, a[((i + k) % n + n) % n]);
                mx[i] = v;
            }
            System.Array.Copy(mx, a, n);
            Blur(a, r, 3);
        }

        /// <summary>Box blur a ring of values `passes` times (radius r).</summary>
        static void Blur(float[] a, int r, int passes)
        {
            int n = a.Length;
            var tmp = new float[n];
            for (int p = 0; p < passes; p++)
            {
                for (int i = 0; i < n; i++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += a[((i + k) % n + n) % n];
                    tmp[i] = s / (2 * r + 1);
                }
                System.Array.Copy(tmp, a, n);
            }
        }

        static void Blur(Vector3[] a, int r, int passes)
        {
            int n = a.Length;
            var tmp = new Vector3[n];
            for (int p = 0; p < passes; p++)
            {
                for (int i = 0; i < n; i++)
                {
                    var s = Vector3.zero;
                    for (int k = -r; k <= r; k++) s += a[((i + k) % n + n) % n];
                    tmp[i] = s / (2 * r + 1);
                }
                System.Array.Copy(tmp, a, n);
            }
        }

        static int Mask => ~(1 << PlayerNet.HitboxLayer);

        /// <summary>Does this collider hide what's behind it? The glass (the ball's dome, the walls between the halves, the
        /// map's dome) is see-through: no.</summary>
        static bool Solid(Collider c)
        {
            if (c == null || c == MapDome.Collider) return false;
            for (var t = c.transform; t != null; t = t.parent) if (t.name == "GlassWall") return false;
            return true;
        }

        static bool InSolid(Vector3 p)
        {
            foreach (var c in Physics.OverlapSphere(p, 1.2f, Mask, QueryTriggerInteraction.Ignore)) if (Solid(c)) return true;
            return false;
        }

        /// <summary>Can the camera at `cam` see `look`? Nothing solid, no menu tree (they've no colliders) and no boulder in
        /// a fat line between them - stopping a few metres short (what it looks at is often solid itself).</summary>
        static bool Seen(Vector3 cam, Vector3 look)
        {
            var d = look - cam;
            float len = d.magnitude;
            if (len < 1f) return true;
            float reach = Mathf.Max(0f, len - 5f);
            var end = cam + d / len * reach;
            for (int q = 0; q < s_TreePos.Count; q++)
                if (Crosses(cam, end, s_TreePos[q], 3.4f, TreeTop)) return false;
            for (int q = 0; q < MapScenery.Boulders.Count && q < MapScenery.BoulderRadius.Count; q++)
                if (Crosses(cam, end, MapScenery.Boulders[q], MapScenery.BoulderRadius[q] + 0.4f, MapScenery.BoulderRadius[q] * 1.3f)) return false;
            if (reach < 0.5f) return true;
            foreach (var hit in Physics.SphereCastAll(cam, 0.4f, d / len, reach, Mask, QueryTriggerInteraction.Ignore))
                if (Solid(hit.collider)) return false;
            return true;
        }

        /// <summary>Does the line a..b pass through an upright cylinder (radius r, `top` high) standing at `foot`?</summary>
        static bool Crosses(Vector3 a, Vector3 b, Vector3 foot, float r, float top)
        {
            float ax = a.x - foot.x, az = a.z - foot.z, dx = b.x - a.x, dz = b.z - a.z;
            float dd = dx * dx + dz * dz;
            float t = dd > 1e-4f ? Mathf.Clamp01(-(ax * dx + az * dz) / dd) : 0f;
            float cx = ax + dx * t, cz = az + dz * t;
            if (cx * cx + cz * cz >= r * r) return false;
            float yAt = Mathf.Lerp(a.y, b.y, t);
            return yAt < foot.y + top && yAt > foot.y - 1f;
        }
    }
}
