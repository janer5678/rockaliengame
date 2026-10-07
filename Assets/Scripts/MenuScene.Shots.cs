using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The menu's map page (Hud.MapPreview): the map itself, in slow shots at eye level - no flying about, no quick
    /// turns. The first is always from a corner of the map looking across the whole of it; then the crashed UFO in the
    /// middle, the tall grass (on the maps that have it: up to the edge of one of its biggest patches, as you'd see it in a
    /// match) and the other corners. Each shot drifts slowly forward and pans gently across, and they cut through a short
    /// fade.
    /// The same shots play behind the host's map page over a running lobby (BACK in the lobby: Hud.LobbyBack.cs) while the
    /// map picked is the one being hosted - filmed over the session's own map (LobbyPreview), the camera taken from the
    /// ship lobby for as long as the page is up (a late LateUpdate, after ShipLobby's).
    /// </summary>
    public static partial class MenuScene
    {
        struct Shot { public Vector3 From, To, LookFrom, LookTo; }
        static readonly List<Shot> s_Shots = new List<Shot>();
        static int s_ShotsRoot, s_Shot, s_RootSeen, s_RootSeenFrame;
        static float s_ShotT;
        /// <summary>How long each shot lasts, and its fade in and out (s).</summary>
        public const float ShotSeconds = 8f, ShotFade = 0.6f;
        /// <summary>The shots' field of view (the same on the menu and over the lobby).</summary>
        public const float ShotFov = 55f;
        /// <summary>(tests) which shot is showing, and where the first one stands.</summary>
        public static int ShotIndex => s_Shot;
        public static Vector3 FirstShotFrom => s_Shots.Count > 0 ? s_Shots[0].From : Vector3.zero;
        /// <summary>(tests) how many of the shots are of the tall grass.</summary>
        public static int TallGrassShots { get; private set; }
        /// <summary>(tests) the index of the first tall grass shot (-1: none), and jump to a shot (a second into it).</summary>
        public static int FirstTallGrassShot { get; private set; } = -1;
        public static void TestJumpShot(int i) { if (i >= 0 && i < s_Shots.Count) { s_Shot = i; s_ShotT = 1f; } }

        /// <summary>The host's map page over the running lobby is showing the hosted map: the shots are filmed over the
        /// session's own map, and the ship lobby leaves the camera (and its dimmed lights) alone meanwhile.</summary>
        public static bool LobbyPreview => Hud.LobbyMapLive && !TestHold;
        static bool s_LobbyWas;

        static void TickShots(Camera camera, float dt)
        {
            var cam = camera.transform;
            int root = MapBuilder.Root.GetInstanceID() ^ (TreeCount * 7919);
            // (a world just built - another map previewed over the lobby - is planned a couple of frames later, once the old
            // one's colliders are gone and the new one's are in: the shots keep out of anything solid)
            if (root != s_RootSeen) { s_RootSeen = root; s_RootSeenFrame = Time.frameCount; }
            if ((root != s_ShotsRoot || s_Shots.Count == 0) && (s_ShotsRoot == 0 || Time.frameCount - s_RootSeenFrame >= 2))
            {
                if (s_ShotsRoot != 0 && root != s_ShotsRoot) Physics.SyncTransforms();
                s_ShotsRoot = root;
                PlanShots();
                s_Shot = 0;
                s_ShotT = 0f;
            }
            if (s_Shots.Count == 0) return;
            s_ShotT += dt;
            if (s_ShotT > ShotSeconds) { s_ShotT = 0f; s_Shot = (s_Shot + 1) % s_Shots.Count; }
            var sh = s_Shots[s_Shot];
            float u = s_ShotT / ShotSeconds, e = u * u * (3f - 2f * u) * 0.5f + u * 0.5f; // (gentle at the ends, steady in the middle)
            var pos = Vector3.Lerp(sh.From, sh.To, e);
            pos.y = Mathf.Max(pos.y, Ground(pos) + Eye);
            var look = Vector3.Lerp(sh.LookFrom, sh.LookTo, e);
            cam.SetPositionAndRotation(pos, Quaternion.LookRotation((look - pos).normalized, Vector3.up));
            camera.fieldOfView = ShotFov;
            // a short fade through black between shots
            float fin = Mathf.Clamp01(s_ShotT / ShotFade), fout = Mathf.Clamp01((ShotSeconds - s_ShotT) / ShotFade);
            Fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Min(fin, fout));
        }

        /// <summary>LateUpdate (after ShipLobby's camera): the shots over the session's map while the host's lobby map page
        /// shows the hosted map. Each time it opens it starts again from the first shot.</summary>
        public static void TickLobbyPreview()
        {
            Hud.TickLobbyMapPreview(); // (another map picked: built live in place of the hosted one first - Hud.LobbyBack.cs)
            bool on = LobbyPreview && MapBuilder.Root != null;
            if (on && !s_LobbyWas) s_ShotsRoot = 0; // (planned afresh for this map, from the first shot)
            s_LobbyWas = on;
            if (!on) return;
            var cam = Camera.main;
            if (cam != null) TickShots(cam, Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        static void PlanShots()
        {
            s_Shots.Clear();
            TallGrassShots = 0;
            FirstTallGrassShot = -1;
            float lim = Cfg.MapHalf - 5f;
            var centre = new Vector3(0f, 0f, 0f);
            centre.y = Ground(centre) + Eye + 1.5f;
            // the tall grass's biggest patches (only some maps have it)
            var tall = GrassField.Current != null && !GameSettings.PsxGraphics && !GameSettings.AiPsx ? GrassField.Current.TallGrassSpots(2) : new List<Vector3>();
            // the four corners (the first one first), each looking in across the whole map, panning gently
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1) };
            for (int i = 0; i < corners.Length; i++)
            {
                var c = new Vector3(corners[i].x * lim, 0f, corners[i].y * lim);
                var inward = (new Vector3(-c.x, 0f, -c.z)).normalized;
                var from = Clear(c, inward);
                var to = from + inward * 8f; // (a short, slow drift in: 8 m over the shot, never further)
                from.y = Ground(from) + Eye;
                to.y = Ground(to) + Eye;
                var side = Vector3.Cross(Vector3.up, inward);
                float span = Cfg.MapHalf * 0.25f;
                s_Shots.Add(new Shot { From = from, To = to, LookFrom = centre + side * span, LookTo = centre - side * span });
                // after the first corner: the crashed UFO in the middle, drifting slowly round it
                if (i == 0 && CrashSite.Current != null)
                {
                    var crash = CrashSite.Current.SaucerCentre;
                    var away = new Vector3(crash.x, 0f, crash.z).sqrMagnitude > 1f ? new Vector3(crash.x, 0f, crash.z).normalized : Vector3.forward;
                    var s2 = Vector3.Cross(Vector3.up, away);
                    var a = Clear(crash + (away + s2 * 0.6f).normalized * 18f, -away);
                    var b = Clear(crash + (away - s2 * 0.6f).normalized * 18f, -away);
                    a.y = Ground(a) + Eye; b.y = Ground(b) + Eye;
                    s_Shots.Add(new Shot { From = a, To = b, LookFrom = crash + Vector3.up * 2f, LookTo = crash + Vector3.up * 2f });
                }
                // then (and after the third corner) the tall grass
                if ((i == 0 || i == 2) && tall.Count > 0) { AddTallGrassShot(tall[0]); tall.RemoveAt(0); }
            }
        }

        /// <summary>A shot of a tall grass patch: from a few metres outside it, drifting slowly in to its knee-high rim,
        /// looking across its head-high middle (and out past it, toward the edge of the map).</summary>
        static void AddTallGrassShot(Vector3 mid)
        {
            var g = GrassField.Current;
            var flat = new Vector3(mid.x, 0f, mid.z);
            var out_ = flat.sqrMagnitude > 1f ? -flat.normalized : Vector3.forward; // (from the patch toward the map's middle: the camera stands there, looking out)
            // how far it is from the middle to the patch's edge that way
            float edge = 3f;
            for (float d = 1f; d < 35f; d += 0.5f)
            {
                var p = mid + out_ * d;
                if (g.WheatAt(p.x, p.z) < 0.05f) { edge = d; break; }
            }
            var from = Clear(mid + out_ * (edge + 8f), out_);
            var to = mid + out_ * (edge + 2.6f);
            if (Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z)) > 9f) to = from - out_ * 4f; // (pushed back by something solid: just a short drift)
            from.y = Ground(from) + Eye;
            to.y = Ground(to) + Eye;
            var side = Vector3.Cross(Vector3.up, out_);
            float h = Ground(mid) + 1.8f;
            var look = new Vector3(mid.x, h, mid.z);
            if (FirstTallGrassShot < 0) FirstTallGrassShot = s_Shots.Count;
            s_Shots.Add(new Shot { From = from, To = to, LookFrom = look + side * 3.5f, LookTo = look - side * 3.5f });
            TallGrassShots++;
        }

        /// <summary>A spot in the open near `p`: stepped along `dir` until there's nothing solid round eye height.</summary>
        static Vector3 Clear(Vector3 p, Vector3 dir)
        {
            p.y = 0f;
            for (int i = 0; i < 20; i++)
            {
                var eye = new Vector3(p.x, Ground(p) + Eye, p.z);
                bool tree = false;
                if (s_Trees) foreach (var t in s_TreePos) if ((t.x - p.x) * (t.x - p.x) + (t.z - p.z) * (t.z - p.z) < 9f) { tree = true; break; } // (the menu's trees: no colliders)
                if (!tree && !Physics.CheckSphere(eye, 0.9f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) break;
                p += dir * 2f;
            }
            return p;
        }
    }

    /// <summary>Runs MenuScene's lobby map preview after the ship lobby has placed the camera (so it has the last word).</summary>
    [DefaultExecutionOrder(1500)]
    public class MenuSceneLate : MonoBehaviour
    {
        static MenuSceneLate s_Me;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Make()
        {
            if (s_Me != null) return;
            var go = new GameObject("menu scene (late)");
            DontDestroyOnLoad(go);
            s_Me = go.AddComponent<MenuSceneLate>();
        }

        void LateUpdate() => MenuScene.TickLobbyPreview();
    }
}
