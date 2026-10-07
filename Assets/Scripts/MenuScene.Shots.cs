using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The menu's map page (Hud.MapPreview): the map itself, in slow shots at eye level - no flying about, no quick
    /// turns. The first is always from a corner of the map looking across the whole of it; then the other corners and the
    /// crashed UFO in the middle. Each shot drifts slowly forward and pans gently across, and they cut through a short fade.
    /// </summary>
    public static partial class MenuScene
    {
        struct Shot { public Vector3 From, To, LookFrom, LookTo; }
        static readonly List<Shot> s_Shots = new List<Shot>();
        static int s_ShotsRoot, s_Shot;
        static float s_ShotT;
        /// <summary>How long each shot lasts, and its fade in and out (s).</summary>
        public const float ShotSeconds = 8f, ShotFade = 0.6f;
        /// <summary>(tests) which shot is showing, and where the first one stands.</summary>
        public static int ShotIndex => s_Shot;
        public static Vector3 FirstShotFrom => s_Shots.Count > 0 ? s_Shots[0].From : Vector3.zero;

        static void TickShots(Transform cam, float dt)
        {
            int root = MapBuilder.Root.GetInstanceID() ^ (TreeCount * 7919);
            if (root != s_ShotsRoot || s_Shots.Count == 0)
            {
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
            // a short fade through black between shots
            float fin = Mathf.Clamp01(s_ShotT / ShotFade), fout = Mathf.Clamp01((ShotSeconds - s_ShotT) / ShotFade);
            Fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Min(fin, fout));
        }

        static void PlanShots()
        {
            s_Shots.Clear();
            float lim = Cfg.MapHalf - 5f;
            var centre = new Vector3(0f, 0f, 0f);
            centre.y = Ground(centre) + Eye + 1.5f;
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
            }
        }

        /// <summary>A spot in the open near `p`: stepped along `dir` until there's nothing solid round eye height.</summary>
        static Vector3 Clear(Vector3 p, Vector3 dir)
        {
            p.y = 0f;
            for (int i = 0; i < 20; i++)
            {
                var eye = new Vector3(p.x, Ground(p) + Eye, p.z);
                bool tree = false;
                foreach (var t in s_TreePos) if ((t.x - p.x) * (t.x - p.x) + (t.z - p.z) * (t.z - p.z) < 9f) { tree = true; break; }
                if (!tree && !Physics.CheckSphere(eye, 0.9f, ~0, QueryTriggerInteraction.Ignore)) break;
                p += dir * 2f;
            }
            return p;
        }
    }
}
