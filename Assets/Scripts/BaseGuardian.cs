using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>The emerald shop's walls pack: 8 high tech walls, a high tech gate and a base guardian (emeralds).</summary>
        [Tune("Emerald shop")] public static int EmeraldPriceWalls = 5;
        /// <summary>The BASE GUARDIAN (Emerald mode, with the walls pack): a laser bolt's damage, seconds between bolts, how
        /// far it sees, and its health.</summary>
        [Tune("Emerald mode")] public static float GuardianDamage = 12f, GuardianEvery = 0.7f, GuardianRange = 28f, GuardianHp = 800f;
        /// <summary>Emerald mode's walls and gate are high tech: steel with glowing seams.</summary>
        public static bool HighTechWalls => Emerald;
    }

    /// <summary>
    /// The BASE GUARDIAN (Emerald mode's walls pack, Container.Guardian): a high-tech robot that hovers over a pad in your
    /// base. It looks round slowly while all's quiet; an enemy who comes into your base (or right up to it) and that it can
    /// see gets a warning beep, then it turns its head and both arm cannons on them and fires laser bolts (GuardianDamage
    /// every GuardianEvery s) until they leave, hide or die. It can be shot and smashed (GuardianHp) like the other
    /// placeables, and only goes in your own base.
    /// </summary>
    public static class Guardian
    {
        public const byte FxLaser = 8;
        /// <summary>The robot's glow (its eye, its core, its bolts).</summary>
        public static readonly Color Laser = new Color(1f, 0.25f, 0.2f);
        /// <summary>How high over its pad it hovers (its body's middle) and where its eye is.</summary>
        public const float BodyY = 1.0f, EyeY = 1.62f;

        /// <summary>The robot: a pad on the ground, and over it ("hover", bobbing) a pearly body with a glowing core, and an
        /// "aim" turning to its target that carries the head (a dome with a glowing visor) and the two arm cannons.</summary>
        public static void Build(int team, Transform t, Material ghost)
        {
            var tc = Cfg.TeamColor[Mathf.Clamp(team, 0, 3)];
            var shell = new Color(0.86f, 0.88f, 0.92f);
            var dark = new Color(0.18f, 0.19f, 0.23f);
            var glowTeam = ghost != null ? ghost : Workbench.Glow(tc, 1.8f);
            var glowRed = ghost != null ? ghost : Workbench.Glow(Laser, 2.2f);
            float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
            // the pad
            Art.Part(t, Art.Cylinder, dark, new Vector3(0, 0.05f, 0), new Vector3(0.9f / cr, 0.1f / ch, 0.9f / cr));
            Art.Part(t, Art.Cylinder, tc, new Vector3(0, 0.105f, 0), new Vector3(0.7f / cr, 0.012f / ch, 0.7f / cr), default, false, glowTeam, "pad glow");
            var hover = new GameObject("hover").transform;
            hover.SetParent(t, false);
            hover.localPosition = new Vector3(0, BodyY, 0);
            // the body: an egg of white plating, a dark waist, the team's colour across the chest, a glowing core
            Art.Part(hover, Art.Sphere, shell, new Vector3(0, 0, 0), new Vector3(0.6f, 0.72f, 0.5f));
            Art.Part(hover, Art.Cylinder, dark, new Vector3(0, -0.12f, 0), new Vector3(0.56f / cr, 0.08f / ch, 0.46f / cr));
            Art.Box(hover, tc, new Vector3(0, 0.12f, 0.2f), new Vector3(0.36f, 0.08f, 0.1f));
            Art.Part(hover, Art.Sphere, tc, new Vector3(0, -0.02f, 0.23f), Vector3.one * 0.13f, default, false, glowTeam, "core");
            // the thruster under it
            Art.Part(hover, Art.Cone, dark, new Vector3(0, -0.4f, 0), new Vector3(0.26f, -0.18f, 0.26f));
            Art.Part(hover, Art.Sphere, tc, new Vector3(0, -0.5f, 0), new Vector3(0.16f, 0.06f, 0.16f), default, false, glowTeam, "jet");
            // the turning part: shoulders, the two arm cannons, the head
            var aim = new GameObject("aim").transform;
            aim.SetParent(hover, false);
            aim.localPosition = new Vector3(0, 0.22f, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Part(aim, Art.Sphere, shell * 0.95f, new Vector3(s * 0.36f, 0, 0), Vector3.one * 0.2f);
                Art.Part(aim, Art.Cylinder, dark, new Vector3(s * 0.4f, -0.04f, 0.18f), new Vector3(0.1f / cr, 0.36f / ch, 0.1f / cr), new Vector3(90, 0, 0));
                Art.Part(aim, Art.Cylinder, shell, new Vector3(s * 0.4f, -0.04f, 0.04f), new Vector3(0.14f / cr, 0.12f / ch, 0.14f / cr), new Vector3(90, 0, 0));
                Art.Part(aim, Art.Sphere, Laser, new Vector3(s * 0.4f, -0.04f, 0.37f), Vector3.one * 0.07f, default, false, glowRed, s < 0 ? "muzzleL" : "muzzleR");
            }
            var head = new GameObject("head").transform;
            head.SetParent(aim, false);
            head.localPosition = new Vector3(0, 0.4f, 0);
            Art.Part(head, Art.Sphere, shell, Vector3.zero, new Vector3(0.38f, 0.32f, 0.36f));
            Art.Box(head, dark, new Vector3(0, 0.0f, 0.15f), new Vector3(0.3f, 0.11f, 0.08f));
            Art.Box(head, Laser, new Vector3(0, 0.0f, 0.19f), new Vector3(0.24f, 0.05f, 0.02f), default, false, glowRed).name = "visor";
            Art.Box(head, dark, new Vector3(0.1f, 0.22f, -0.04f), new Vector3(0.02f, 0.18f, 0.02f));
            Art.Part(head, Art.Sphere, tc, new Vector3(0.1f, 0.32f, -0.04f), Vector3.one * 0.05f, default, false, glowTeam);
            if (ghost != null) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = ghost;
        }

        class State { public PlayerNet Target; public float NextScan, NextShot, WarnedAt = -10f, Idle; public Vector2 Aim; public bool Left; }
        static readonly Dictionary<Container, State> s_State = new Dictionary<Container, State>();

        public static void Forget(Container c) => s_State.Remove(c);

        /// <summary>Every peer, every frame: it bobs over its pad and turns its head and cannons to its aim; the server also
        /// picks its target and fires.</summary>
        public static void Tick(Container c, Transform visual)
        {
            if (!s_State.TryGetValue(c, out var st)) s_State[c] = st = new State();
            var hover = visual.Find("hover");
            if (hover != null)
            {
                float ph = c.GetInstanceID() * 0.37f;
                hover.localPosition = new Vector3(0, BodyY + Mathf.Sin(Time.time * 2.1f + ph) * 0.05f, 0);
                var aim = hover.Find("aim");
                if (aim != null)
                {
                    var want = c.IsServer ? st.Aim : c.Aim.Value;
                    var cur = aim.localEulerAngles;
                    float ny = Mathf.MoveTowardsAngle(cur.y, want.x, 220f * Time.deltaTime);
                    float np = Mathf.MoveTowardsAngle(cur.x, Mathf.Clamp(want.y, -35f, 35f), 160f * Time.deltaTime);
                    aim.localRotation = Quaternion.Euler(np, ny, 0);
                }
            }
            if (c.IsServer) ServerTick(c, st);
        }

        static Vector3 Eye(Container c) => c.transform.position + Vector3.up * EyeY;

        static void ServerTick(Container c, State st)
        {
            var g = NetGame.Instance;
            var eye = Eye(c);
            if (Time.time >= st.NextScan)
            {
                st.NextScan = Time.time + 0.25f;
                PlayerNet best = null;
                float bestD = float.MaxValue;
                if (g != null && g.S != GameState.Waiting && g.S != GameState.GameOver)
                    foreach (var p in PlayerNet.All)
                    {
                        if (p == null || !p.IsSpawned || p.Dead.Value || p.Team.Value == c.Team.Value || p.Hidden) continue;
                        var at = p.transform.position;
                        float d = Vector3.Distance(at + Vector3.up * 1.1f, eye);
                        if (d > Cfg.GuardianRange) continue;
                        // they've come into its base (or right up to it)
                        if (Cfg.BaseTeamAt(at) != c.Team.Value && d > 9f) continue;
                        if (!Deployables.Clear(eye, p)) continue;
                        if (d < bestD) { bestD = d; best = p; }
                    }
                if (best != st.Target)
                {
                    st.Target = best;
                    if (best != null) { st.WarnedAt = Time.time; c.DeployFxRpc(Deployables.FxWarn, eye); }
                }
            }
            float dt = Time.deltaTime;
            Vector2 want;
            if (st.Target != null)
            {
                var to = c.transform.InverseTransformDirection(st.Target.transform.position + Vector3.up * 1.1f - (eye - Vector3.up * 0.4f));
                want = new Vector2(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            }
            else
            {
                // all quiet: it looks slowly round
                st.Idle += dt * 25f;
                want = new Vector2(Mathf.Sin(st.Idle * Mathf.Deg2Rad) * 70f, 5f);
            }
            st.Aim = new Vector2(Mathf.MoveTowardsAngle(st.Aim.x, want.x, 220f * dt), Mathf.MoveTowardsAngle(st.Aim.y, want.y, 160f * dt));
            if ((st.Aim - c.Aim.Value).sqrMagnitude > 1f) c.Aim.Value = st.Aim;
            if (st.Target == null || Time.time - st.WarnedAt < 0.7f || Time.time < st.NextShot) return;
            if (Mathf.Abs(Mathf.DeltaAngle(st.Aim.x, want.x)) > 5f || Mathf.Abs(Mathf.DeltaAngle(st.Aim.y, want.y)) > 7f) return;
            // a bolt from one cannon, then the other
            st.NextShot = Time.time + Mathf.Max(0.15f, Cfg.GuardianEvery);
            st.Left = !st.Left;
            var rot = c.transform.rotation * Quaternion.Euler(st.Aim.y, st.Aim.x, 0);
            var muzzle = c.transform.position + Vector3.up * (BodyY + 0.18f) + rot * new Vector3(st.Left ? -0.4f : 0.4f, 0f, 0.4f);
            var tp = st.Target.transform.position + Vector3.up * 1.1f;
            var dir = Quaternion.Euler(Random.Range(-1.2f, 1.2f), Random.Range(-1.2f, 1.2f), 0) * (tp - muzzle).normalized;
            var end = muzzle + dir * Cfg.GuardianRange;
            foreach (var h in SortedHits(muzzle, dir, Cfg.GuardianRange))
            {
                if (h.collider.transform.IsChildOf(c.transform)) continue; // (itself)
                end = h.point;
                var p = h.collider.GetComponentInParent<PlayerNet>();
                if (p != null && p.Team.Value != c.Team.Value && !p.Dead.Value) { p.ServerDamageFrom(Cfg.GuardianDamage, (byte)Item.BaseGuardian, c.Team.Value); Hits++; }
                break;
            }
            c.DeployFxRpc(FxLaser, end);
        }

        static readonly List<RaycastHit> s_Hits = new List<RaycastHit>();
        static List<RaycastHit> SortedHits(Vector3 from, Vector3 dir, float range)
        {
            s_Hits.Clear();
            s_Hits.AddRange(Physics.RaycastAll(from, dir, range, ~0, QueryTriggerInteraction.Ignore));
            s_Hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            return s_Hits;
        }

        /// <summary>Test hook: guardian bolts that hit someone (server).</summary>
        public static int Hits;

        /// <summary>Every screen: a bolt - a red beam from the cannon that fades fast, a zap, sparks where it lands.</summary>
        public static void ClientFx(Container c, Vector3 at)
        {
            var v = c != null ? c.transform.Find("visual") : null;
            Vector3 from = c.transform.position + Vector3.up * (BodyY + 0.2f);
            if (v != null)
            {
                // from whichever muzzle is nearer the line to the target
                Transform best = null; float bd = float.MaxValue;
                foreach (var n in new[] { "muzzleL", "muzzleR" })
                {
                    var m = FindDeep(v, n);
                    if (m == null) continue;
                    float d = Vector3.Distance(m.position, at);
                    if (d < bd) { bd = d; best = m; }
                }
                if (best != null) from = best.position;
            }
            var d0 = at - from;
            float len = d0.magnitude;
            if (len < 0.1f) return;
            var mat = new Material(Art.Ghost(new Color(Laser.r, Laser.g, Laser.b, 0.9f)));
            var go = Art.Part(null, Art.Cube, Color.white, from + d0 * 0.5f, new Vector3(0.06f, 0.06f, len), Quaternion.LookRotation(d0).eulerAngles, false, mat);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FadeOut>().Init(mat, 0.22f);
            Fx.Sparks(at, -d0, 8);
            Sfx.Play(Sfx.Zap, from, 0.8f, 0.12f, 55f);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform k in t) { var f = FindDeep(k, name); if (f != null) return f; }
            return null;
        }
    }
}
