using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Going through linked portals (NetGame.Portals) for everything that isn't a walking player (PlayerController.TickPortals):
    /// arrows, bolts, spears, thrown C4 / rockets / eggs (ArrowProjectile), hitscan shots (Hitscan) and mounts (Vehicle).
    /// The way through is the same for all of them: what goes into one portal comes out of its partner, moving out of it,
    /// turned the same way (Portal's rule: into the front of one, out of the front of the other, mirrored left-right).
    /// </summary>
    public static class PortalPass
    {
        /// <summary>The oval a portal is drawn as (PortalFx: 1.3 m wide, 2 m tall), a touch generous.</summary>
        public const float HalfWidth = 0.7f, HalfHeight = 1.05f;

        /// <summary>A portal's own space: +Z out of its surface, X across, Y up the oval (the same as PortalFx draws it).</summary>
        public static Quaternion Frame(Vector3 normal) => Quaternion.LookRotation(normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up);

        /// <summary>How a direction is turned going into `from` and out of `to`.</summary>
        public static Quaternion Turn(PortalInfo from, PortalInfo to) => Frame(to.Normal) * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(Frame(from.Normal));

        /// <summary>Is x (near the plane of portal p) inside its oval?</summary>
        public static bool Inside(PortalInfo p, Vector3 x)
        {
            var l = Quaternion.Inverse(Frame(p.Normal)) * (x - p.Pos);
            float a = l.x / HalfWidth, b = l.y / HalfHeight;
            return a * a + b * b <= 1f && Mathf.Abs(l.z) < 1.5f;
        }

        /// <summary>Where a point on `from`'s surface comes out on `to`'s (just in front of it, kept inside its oval).</summary>
        public static Vector3 MapPoint(PortalInfo from, PortalInfo to, Vector3 p)
        {
            var l = Quaternion.Inverse(Frame(from.Normal)) * (p - from.Pos);
            var lat = new Vector2(-l.x / HalfWidth, l.y / HalfHeight);
            if (lat.magnitude > 0.8f) lat = lat.normalized * 0.8f;
            return to.Pos + Frame(to.Normal) * new Vector3(lat.x * HalfWidth, lat.y * HalfHeight, 0.06f);
        }

        /// <summary>Something at `at` (on `from`) moving with `vel`: where it comes out of `to`, and its new velocity.</summary>
        public static void Through(PortalInfo from, PortalInfo to, Vector3 at, Vector3 vel, out Vector3 pos, out Vector3 newVel)
        {
            pos = MapPoint(from, to, at);
            newVel = Turn(from, to) * vel;
            // never back into the surface it comes out of
            var n = to.Normal.normalized;
            float outward = Vector3.Dot(newVel, n);
            if (outward < 0.05f * newVel.magnitude) newVel += n * (0.05f * newVel.magnitude - outward);
        }

        /// <summary>
        /// Does the straight piece a -> b go into a linked portal? `bIsHit`: b is where it hit something (a portal sits 3 cm
        /// out from its surface, so a hit on that surface - or on a bump just in front of it - inside the oval counts too).
        /// `skipPair`: a pair to ignore (the one it just came out of). Gives the nearest one along the way.
        /// </summary>
        public static bool Cross(Vector3 a, Vector3 b, bool bIsHit, int skipPair, out PortalInfo from, out PortalInfo to, out Vector3 at)
        {
            from = to = default;
            at = b;
            var g = NetGame.Instance;
            if (g == null || g.Portals.Count < 2) return false;
            var seg = b - a;
            if (seg.sqrMagnitude < 1e-8f) return false;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < g.Portals.Count; i++)
            {
                var p = g.Portals[i];
                if (p.Pair == skipPair) continue;
                var n = p.Normal;
                if (Vector3.Dot(seg, n) >= 0f) continue; // moving away from it (or along it)
                float da = Vector3.Dot(a - p.Pos, n), db = Vector3.Dot(b - p.Pos, n);
                if (da < -0.1f) continue; // already behind it
                float f;
                if (db <= 0f) f = da <= 0f ? 0f : da / (da - db);
                else if (bIsHit && db < 0.25f) f = 1f;
                else continue;
                if (f >= best) continue;
                var x = a + seg * f;
                if (!Inside(p, x)) continue;
                if (!g.TryPartner(i, out var partner)) continue;
                best = f;
                from = p;
                to = partner;
                at = x;
                found = true;
            }
            return found;
        }

        /// <summary>A hit on this is a hit on a surface a portal could be on (not a player or a creature standing in front of one).</summary>
        public static bool Surface(Collider c) => c != null && c.GetComponentInParent<PlayerNet>() == null && c.GetComponentInParent<Vehicle>() == null;

        /// <summary>Is any linked pair open?</summary>
        public static bool AnyLinked
        {
            get
            {
                var g = NetGame.Instance;
                if (g == null || g.Portals.Count < 2) return false;
                for (int i = 0; i < g.Portals.Count; i++) if (g.TryPartner(i, out _)) return true;
                return false;
            }
        }

        /// <summary>
        /// Server: is a reported landing / hit point believable? Within `limit` of the shooter as before - or, while a
        /// linked portal pair is open (it may have gone through one), anywhere on the battlefield.
        /// </summary>
        public static bool ReachOk(Vector3 shooter, Vector3 point, float limit)
        {
            if (Vector3.Distance(point, shooter) <= limit) return true;
            return AnyLinked && Mathf.Abs(point.x) <= Cfg.MapHalf + 40f && Mathf.Abs(point.z) <= Cfg.MapHalf + 40f && point.y > -60f && point.y < 300f;
        }

        // ------------------------------------------------------------------ hitscan

        /// <summary>The same shape as PlayerController.AimWithAssist.</summary>
        public delegate bool AimFn(Ray ray, float range, float radius, out RaycastHit hit);

        /// <summary>The straight pieces of the last Hitscan, in pairs (start, end, start, end...): one tracer each.</summary>
        public static readonly List<Vector3> Path = new List<Vector3>();

        /// <summary>
        /// A hitscan shot that follows portals: `aim` is cast along the ray; if the shot goes into a linked portal before (or
        /// where) it hits, it carries on out of the other one, turned the same way, for what's left of its range. Returns what
        /// it hit in the end (like `aim`), `end` (the hit point, or where it ran out) and `endDir` (its last direction). Path
        /// holds every straight piece for the tracers.
        /// Example (PlayerController.Modes.cs, the pistol):
        ///   bool hit = PortalPass.Hitscan(ray, 250f, Cfg.ProjectileAssist, AimWithAssist, out var h, out var point, out var shotDir);
        /// </summary>
        public static bool Hitscan(Ray ray, float range, float radius, AimFn aim, out RaycastHit hit, out Vector3 end, out Vector3 endDir)
        {
            Path.Clear();
            float left = range;
            int skip = -1;
            for (int hop = 0; ; hop++)
            {
                bool got = aim(ray, left, radius, out hit);
                var b = got ? hit.point : ray.GetPoint(left);
                if (hop < 4 && Cross(ray.origin, b, got && Surface(hit.collider), skip, out var from, out var to, out var at))
                {
                    Path.Add(ray.origin);
                    Path.Add(at);
                    left -= Vector3.Distance(ray.origin, at);
                    Through(from, to, at, ray.direction, out var np, out var nd);
                    ray = new Ray(np, nd.normalized);
                    skip = to.Pair;
                    if (left > 0.5f) continue;
                    got = false;
                    hit = default;
                    b = np;
                }
                Path.Add(ray.origin);
                Path.Add(b);
                end = b;
                endDir = ray.direction;
                return got;
            }
        }

        /// <summary>Draws the last Hitscan's tracers (the first from `muzzle` instead of the eye).</summary>
        public static void Tracers(Vector3 muzzle, Fx.Gun gun)
        {
            for (int i = 0; i + 1 < Path.Count; i += 2)
                Fx.Tracer(i == 0 ? muzzle : Path[i], Path[i + 1], gun, true, false);
        }

        /// <summary>
        /// Server: the tracer everyone else sees for a hitscan shot from `eye` along `dir` that ended at `point` - bent through
        /// any portal it went into on the way (one tracer per straight piece). `start` is where the first piece starts.
        /// </summary>
        public static void ServerTracer(FxKind kind, Vector3 start, Vector3 eye, Vector3 dir, Vector3 point, ulong skipClient)
        {
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            var o = eye;
            float left = 400f;
            int skip = -1;
            int mask = ~(1 << PlayerNet.HitboxLayer);
            for (int hop = 0; hop < 4 && AnyLinked; hop++)
            {
                bool got = Physics.Raycast(o, dir, out var h, left, mask, QueryTriggerInteraction.Ignore);
                var b = got ? h.point : o + dir * left;
                if (!Cross(o, b, got && Surface(h.collider), skip, out var from, out var to, out var at)) break;
                Fx.Server(kind, hop == 0 ? start : o, at, skipClient);
                left -= Vector3.Distance(o, at);
                Through(from, to, at, dir, out o, out dir);
                dir.Normalize();
                skip = to.Pair;
                start = o;
                if (left <= 0.5f) break;
            }
            Fx.Server(kind, start, point, skipClient);
        }
    }
}
