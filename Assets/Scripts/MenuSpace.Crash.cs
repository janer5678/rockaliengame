using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The match intro's first two scenes (MatchIntro.cs), played with the main menu cutscene's own UFO, fire, colours
    /// and post processing:
    /// SPACE (MatchIntro.SpaceEnd s) - the menu's shot of the UFO tearing through space: a quick push in on the chase
    ///   and a whip-past fly-by.
    /// CRASH (MatchIntro.CrashLength s) - far off from everything else (CrashOrigin) the same UFO comes screaming down
    ///   into a planet's atmosphere: a burning sheath of plasma round it, speed streaks, a trail of fire and smoke, the
    ///   sky turning from black to burning orange (ENTRY, the camera riding just ahead of it), the ground rushing up as it
    ///   punches down through the clouds (DIVE, chasing it from behind and above), then from the ground (IMPACT) it slams
    ///   in: a white-hot flash, a fireball, a dust ring tearing out across the grass and bending the trees, chunks of hull
    ///   and burning debris flying, the camera shaking hard.
    /// Everything moves as a function of the time into the scene, so it plays the same on every screen and in the
    /// screenshots. The UFO is the menu's own (reparented into the crash scene for it, and back after).
    /// </summary>
    public static partial class MenuSpace
    {
        static readonly Vector3 CrashOrigin = new Vector3(-6000f, 1500f, -7500f);
        /// <summary>How steeply it dives (degrees down), how fast (m/s), when it hits the ground and when the camera cuts
        /// (seconds into the crash scene).</summary>
        const float DiveAngle = 28f, DiveSpeed = 380f;
        public const float CrashImpact = 2.85f, CrashShotB = 1.0f, CrashShotC = 2.1f;
        static readonly Vector3 k_GroundCam = new Vector3(80f, 3.6f, 50f);

        /// <summary>The intro's camera this frame (MatchIntro.CameraPose hands it to the player's camera).</summary>
        public static Vector3 IntroPos { get; private set; }
        public static Quaternion IntroRot { get; private set; } = Quaternion.identity;
        public static float IntroFov { get; private set; } = 60f;
        public static bool IntroPoseOk { get; private set; }
        /// <summary>Test hooks: the crash scene is up; which intro shot is on (0 chase, 1 fly-by, 2 entry, 3 dive, 4 impact);
        /// the crash has happened (the fireball's out).</summary>
        public static bool CrashUp => s_CrashOn;
        public static int IntroShot { get; private set; } = -1;
        public static bool CrashExploded { get; private set; }

        static GameObject s_Crash;
        static bool s_CrashOn;
        static Transform s_Sheath, s_Streaks, s_Streaks2, s_Boom;
        static Light s_BoomLight;
        static Transform s_Core, s_Crater, s_SmokeDome;
        static readonly List<(Transform t, float size)> s_SheathCones = new List<(Transform, float)>();
        static readonly List<Transform> s_FireBalls = new List<Transform>();
        static readonly List<Transform> s_Smoke = new List<Transform>(), s_Hot = new List<Transform>();
        static readonly List<Vector3> s_SmokeJit = new List<Vector3>();
        static readonly List<Transform> s_Dust = new List<Transform>(), s_Spray = new List<Transform>();
        static readonly List<(Transform t, Vector3 vel, Vector3 spin, Vector3 size)> s_Debris = new List<(Transform, Vector3, Vector3, Vector3)>();
        static readonly List<(Transform t, float dist, Vector3 away)> s_Trees = new List<(Transform, float, Vector3)>();
        const int SmokePuffs = 44, DustPuffs = 30;

        static Vector3 DiveDir => new Vector3(0f, -Mathf.Sin(DiveAngle * Mathf.Deg2Rad), Mathf.Cos(DiveAngle * Mathf.Deg2Rad));
        static Vector3 DiveUp => new Vector3(0f, Mathf.Cos(DiveAngle * Mathf.Deg2Rad), Mathf.Sin(DiveAngle * Mathf.Deg2Rad));
        /// <summary>Where the UFO is (crash scene's own space: the impact at 0) c seconds into the crash scene.</summary>
        static Vector3 DiveAt(float c) => -DiveDir * DiveSpeed * Mathf.Max(0f, CrashImpact - c);

        // ------------------------------------------------------------------ the intro, every frame

        static void TickIntro(Camera cam)
        {
            float t = Mathf.Max(0f, MatchIntro.Elapsed);
            if (s_Crash == null) BuildCrash(); // (built on the first frame, under the fade in: no hitch at the cut)
            bool crash = t >= MatchIntro.SpaceEnd;
            SetCrashScene(crash);
            if (s_ColoursDirty) ApplyColours();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.farClipPlane = Mathf.Max(s_OldFar, crash ? 4200f : 3000f);
            Animate(6f + t); // (the menu's fire is roaring from the first frame)
            Vector3 pos; Quaternion rot; float fov;
            if (!crash)
            {
                cam.backgroundColor = GameSettings.MenuSpaceColour.Value;
                SpaceIntroShot(t, out pos, out rot, out fov);
            }
            else
            {
                CrashTick(t - MatchIntro.SpaceEnd, out pos, out rot, out fov, out var sky);
                cam.backgroundColor = sky;
            }
            IntroPos = pos; IntroRot = rot; IntroFov = fov; IntroPoseOk = true;
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
        }

        /// <summary>The intro's space scene: the menu's chase shot pushing in fast, then the fly-by whipping past.</summary>
        static void SpaceIntroShot(float t, out Vector3 pos, out Quaternion rot, out float fov)
        {
            var ship = s_Ship.Root.transform.position;
            Vector3 look;
            const float Cut = 0.8f;
            if (t < Cut)
            {
                IntroShot = 0;
                float u = t / Cut, e = 1f - (1f - u) * (1f - u);
                pos = ship + new Vector3(Mathf.Sin(t * 2f) * 4f, 24f - e * 9f, -88f + e * 32f);
                look = ship + new Vector3(0, 0, 30f);
                fov = 58f - e * 8f;
            }
            else
            {
                IntroShot = 1;
                float u = Mathf.Clamp01((t - Cut) / (MatchIntro.SpaceEnd - Cut));
                pos = ship + new Vector3(40f, 9f, Mathf.Lerp(175f, -70f, u));
                look = ship;
                fov = 50f;
            }
            pos += s_Rattle * 0.5f;
            rot = Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(s_Rattle.y, s_Rattle.x, s_Rattle.z * 1.5f);
        }

        /// <summary>Into the crash scene (the UFO moves over into it) or back out to space.</summary>
        static void SetCrashScene(bool on)
        {
            if (on == s_CrashOn) return;
            s_CrashOn = on;
            if (s_Crash != null) s_Crash.SetActive(on);
            if (s_Root != null && s_Showing) s_Root.SetActive(!on);
            if (s_Ship != null && s_Ship.Root != null)
            {
                s_Ship.Root.transform.SetParent(on && s_Crash != null ? s_Crash.transform : s_Root.transform, false);
                s_Ship.Root.SetActive(true);
            }
            if (s_Sheath != null) s_Sheath.gameObject.SetActive(on);
            if (!on) CrashExploded = false;
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - a) / (b - a)));

        /// <summary>The crash scene c seconds in: the UFO, its trail, the explosion, the trees and the camera.</summary>
        static void CrashTick(float c, out Vector3 pos, out Quaternion rot, out float fov, out Color sky)
        {
            var o = s_Crash.transform.position;
            var d = DiveDir;
            var up = DiveUp;
            var dive = Quaternion.LookRotation(d, up);
            float a = c - CrashImpact; // (time since the impact)

            // ---- the UFO: diving in, wobbling and rolling harder the nearer the ground, gone into the fireball at the hit
            var ship = s_Ship.Root.transform;
            var P = DiveAt(c);
            float near = Mathf.Clamp01(c / CrashImpact);
            ship.localPosition = P + s_Rattle * (1f + near * 1.5f);
            ship.localRotation = dive * Quaternion.Euler(Mathf.Sin(c * 7f) * (4f + 6f * near), Mathf.Sin(c * 4.3f) * 5f, Mathf.Sin(c * 5.3f) * 14f + c * 45f)
                                 * Quaternion.Euler(s_Rattle.y * 4f, s_Rattle.x * 2f, s_Rattle.z * 5f);
            ship.gameObject.SetActive(a < 0.06f);
            // the plasma sheath round it: a bow of fire, narrow at the nose and flaring wide behind, licking in and out
            for (int i = 0; i < s_SheathCones.Count; i++)
            {
                var (st, size) = s_SheathCones[i];
                float n = Mathf.PerlinNoise(c * (11f + i * 4f), i * 2.1f);
                float w = size * (0.85f + 0.3f * Mathf.PerlinNoise(c * 17f, i * 3.3f + 1f));
                float len = size * 1.6f * (0.8f + 0.4f * n);
                st.localScale = new Vector3(w, len, w * 0.55f);
                st.localPosition = new Vector3(0f, 48f - len, 0f); // (the tip stays just ahead of the nose)
            }

            // ---- speed streaks rushing past it (in the air)
            bool air = c < CrashShotC;
            s_Streaks.gameObject.SetActive(air);
            s_Streaks2.gameObject.SetActive(air);
            if (air)
            {
                float lz = -((c * DiveSpeed * 1.7f) % LinesBox) - LinesBox * 0.5f;
                s_Streaks.localPosition = P + dive * new Vector3(0, 0, lz);
                s_Streaks2.localPosition = P + dive * new Vector3(0, 0, lz + LinesBox);
                s_Streaks.localRotation = s_Streaks2.localRotation = dive;
            }

            // ---- the trail: hot puffs of fire right behind it, cooling into a long column of smoke
            for (int i = 0; i < SmokePuffs; i++)
            {
                float born = i * (CrashImpact / SmokePuffs);
                float age = c - born;
                var sm = s_Smoke[i];
                var hot = s_Hot[i];
                if (age < 0f) { sm.gameObject.SetActive(false); hot.gameObject.SetActive(false); continue; }
                var at = DiveAt(born) - d * 13f + s_SmokeJit[i] * (1.5f + age * 7f) + Vector3.up * age * 4f;
                sm.gameObject.SetActive(true);
                sm.localPosition = at;
                sm.localScale = Vector3.one * Mathf.Min(10f + age * 24f, 70f);
                bool burning = age < 0.4f;
                hot.gameObject.SetActive(burning);
                if (burning)
                {
                    hot.localPosition = at;
                    hot.localScale = Vector3.one * (20f * (1f - age / 0.4f) + 2f);
                }
            }

            // ---- the explosion
            CrashExploded = a >= 0f;
            s_Boom.gameObject.SetActive(a >= 0f);
            if (a >= 0f) Explode(a);
            // the trees round the impact bend away as the dust ring reaches them
            float ring = a >= 0f ? 150f * (1f - Mathf.Exp(-a * 3.2f)) : -1f;
            foreach (var (tt, dist, away) in s_Trees)
            {
                float hit = ring < 0f ? 0f : Smooth(dist - 6f, dist + 18f, ring);
                float wob = hit > 0f ? Mathf.Sin(a * 23f + dist) * 5f * (1f - hit * 0.6f) : 0f;
                var axis = Vector3.Cross(Vector3.up, away);
                tt.localRotation = Quaternion.AngleAxis(hit * (32f - Mathf.Min(dist, 300f) * 0.07f) + wob, axis);
            }

            // ---- the sky: black space, burning orange as it hits the air, then a warm alien afternoon
            var space = GameSettings.MenuSpaceColour.Value;
            var hotSky = new Color(0.96f, 0.42f, 0.18f);
            var day = new Color(0.98f, 0.7f, 0.5f);
            sky = c < 0.9f ? Color.Lerp(space, hotSky, Smooth(0f, 0.9f, c)) : Color.Lerp(hotSky, day, Smooth(0.9f, CrashShotC, c));

            // ---- the camera
            Vector3 look;
            float amp;
            if (c < CrashShotB)
            {
                // ENTRY: riding just ahead of it and off to the side, looking back into its fire
                IntroShot = 2;
                float u = c / CrashShotB;
                pos = P + d * (62f - u * 10f) + Vector3.right * (26f - u * 6f) - up * 10f;
                look = P + d * 6f;
                fov = 64f - u * 8f;
                amp = 1.0f;
            }
            else if (c < CrashShotC)
            {
                // DIVE: chasing it from behind and above, the ground rushing up, through the clouds
                IntroShot = 3;
                pos = P - d * 78f + up * 24f + Vector3.right * Mathf.Sin(c * 2f) * 8f;
                look = P + d * 70f;
                fov = 62f;
                amp = 1.2f;
            }
            else
            {
                // IMPACT: from the ground, it screams in and slams down a hundred metres away
                IntroShot = 4;
                pos = k_GroundCam;
                var lookShip = P + d * 18f;
                look = Vector3.Lerp(lookShip, new Vector3(0f, 22f, 0f), Smooth(-0.12f, 0.25f, a));
                fov = Mathf.Lerp(40f, 58f, Smooth(-0.3f, 0.4f, a));
                amp = a < 0f ? 0.2f + 0.6f * Smooth(-0.3f, 0f, a) : 3.5f * Mathf.Exp(-a * 3.2f) + 0.35f;
            }
            var n3 = new Vector3(Mathf.PerlinNoise(c * 31f, 1.1f) - 0.5f, Mathf.PerlinNoise(c * 29f, 2.7f) - 0.5f, Mathf.PerlinNoise(c * 37f, 3.9f) - 0.5f);
            pos += n3 * amp * 0.9f + s_Rattle * (c < CrashShotC ? 0.6f : 0f);
            rot = Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(n3.y * amp * 2.2f, n3.x * amp * 2.2f, n3.z * amp * 3.5f);
            pos += o;
        }

        /// <summary>The explosion a seconds after the impact (everything a function of a).</summary>
        static void Explode(float a)
        {
            float grow = 1f - Mathf.Exp(-a * 7f);
            // the white-hot core: blinding at once, burning down in half a second
            float core = 75f * grow * (1f - Smooth(0.2f, 0.7f, a));
            s_Core.gameObject.SetActive(core > 0.5f);
            s_Core.localScale = Vector3.one * Mathf.Max(0.01f, core);
            s_Core.localPosition = new Vector3(0, 8f + a * 10f, 0);
            // the fireball, layer on layer, boiling and rolling up
            for (int i = 0; i < s_FireBalls.Count; i++)
            {
                float size = (90f + i * 28f) * (1f - Mathf.Exp(-a * (7f - i * 1.6f))) * (1f - Smooth(0.6f + i * 0.15f, 1.4f + i * 0.2f, a) * 0.5f);
                float boil = 1f + (Mathf.PerlinNoise(a * 9f, i * 3f) - 0.5f) * 0.18f;
                s_FireBalls[i].localScale = new Vector3(size * boil, size * (0.8f + 0.1f * i) / boil, size * boil);
                s_FireBalls[i].localPosition = new Vector3(0, 10f + a * (22f + i * 6f), 0);
            }
            // the smoke rolling up over it
            float sd = 190f * (1f - Mathf.Exp(-a * 2.2f)) * Smooth(0.1f, 0.4f, a);
            s_SmokeDome.gameObject.SetActive(sd > 1f);
            s_SmokeDome.localScale = new Vector3(sd, sd * 0.6f, sd);
            s_SmokeDome.localPosition = new Vector3(0, 30f + a * 40f, 0);
            // the crater, scorched
            s_Crater.localScale = new Vector3(80f, 3f, 80f) * Mathf.Min(1f, a * 8f + 0.05f);
            // the dust ring tearing out across the ground
            float r = 150f * (1f - Mathf.Exp(-a * 3.2f));
            for (int i = 0; i < s_Dust.Count; i++)
            {
                float ang = (i + 0.5f) * Mathf.PI * 2f / s_Dust.Count;
                var p = new Vector3(Mathf.Cos(ang) * r, 5f + a * 7f, Mathf.Sin(ang) * r);
                s_Dust[i].localPosition = p;
                float s = 14f + a * 44f;
                s_Dust[i].localScale = new Vector3(s * 1.4f, s * 0.7f, s * 1.4f);
            }
            // dirt blasted up in spikes
            float spray = 75f * Smooth(0f, 0.3f, a) * (1f - Smooth(0.5f, 1.0f, a) * 0.5f);
            foreach (var sp in s_Spray)
            {
                float w = 7f + a * 6f;
                sp.localScale = new Vector3(w, Mathf.Max(0.01f, spray), w);
            }
            // hull plates and burning chunks thrown out in arcs (they land and stop)
            foreach (var (t, vel, spin, size) in s_Debris)
            {
                var p = new Vector3(0, 5f, 0) + vel * a + 0.5f * a * a * new Vector3(0, -70f, 0);
                bool landed = p.y < 0.5f;
                if (landed) p.y = 0.5f;
                t.localPosition = p;
                t.localRotation = Quaternion.Euler(spin * (landed ? Mathf.Min(a, 1.2f) : a));
                t.localScale = size;
            }
            // the light of it
            s_BoomLight.intensity = 30f * Mathf.Exp(-a * 2.5f) + 2f;
            s_BoomLight.transform.localPosition = new Vector3(0, 20f + a * 15f, 0);
        }

        // ------------------------------------------------------------------ building it

        static GameObject Ghosted(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Color c, DisplayPref.Colour pref = null, Color? shiftRef = null)
        {
            var g = Art.Part(parent, mesh, Color.white, pos, scale, default, false, Art.Ghost(c));
            var r = g.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (pref != null) Paint(r, pref, shiftRef);
            return g;
        }

        static void BuildCrash()
        {
            s_Crash = new GameObject("MenuSpace crash");
            s_Crash.transform.position = CrashOrigin;
            var root = s_Crash.transform;
            var rng = new System.Random(4242);
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;

            // ---- light: a low warm sun over it all
            var sun = new GameObject("crash sun").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.transform.localRotation = Quaternion.Euler(32f, -40f, 0f);
            sun.type = LightType.Directional; sun.intensity = 0.65f; sun.color = new Color(1f, 0.86f, 0.7f); sun.shadows = LightShadows.None;

            // ---- the ground: grass with patches of light and dark, rolling hills, and a ring of far mountains
            var ground = Art.Part(root, Art.Cylinder, new Color(0.34f, 0.5f, 0.26f), new Vector3(0, -1f, 0), new Vector3(5200f / cr, 2f / ch, 5200f / cr));
            ground.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 70; i++)
            {
                float ang = R(0f, Mathf.PI * 2f), dist = R(30f, 1100f);
                var col = Color.Lerp(new Color(0.27f, 0.42f, 0.2f), i % 4 == 0 ? new Color(0.5f, 0.42f, 0.28f) : new Color(0.44f, 0.6f, 0.3f), R(0f, 1f));
                Art.Part(root, Art.Sphere, col, new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist), new Vector3(R(40f, 170f), 0.8f, R(40f, 170f)), new Vector3(0, R(0f, 180f), 0))
                    .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for (int i = 0; i < 38; i++)
            {
                float ang = R(0f, Mathf.PI * 2f), dist = R(220f, 1500f);
                var p = new Vector3(Mathf.Cos(ang) * dist, -8f, Mathf.Sin(ang) * dist);
                if (Mathf.Abs(p.x) < 90f && p.z < 0f && p.z > -700f) continue; // (clear of the dive)
                var col = Color.Lerp(new Color(0.28f, 0.45f, 0.22f), new Color(0.4f, 0.55f, 0.28f), R(0f, 1f));
                Art.Part(root, Art.Sphere, col, p, new Vector3(R(160f, 340f), R(40f, 110f), R(160f, 340f)), new Vector3(0, R(0f, 180f), 0));
            }
            for (int i = 0; i < 30; i++)
            {
                float ang = i * Mathf.PI * 2f / 30f + R(-0.08f, 0.08f), dist = R(1750f, 2300f);
                float w = R(420f, 760f), h = R(260f, 580f);
                var p = new Vector3(Mathf.Cos(ang) * dist, -5f, Mathf.Sin(ang) * dist);
                var col = Color.Lerp(new Color(0.42f, 0.38f, 0.55f), new Color(0.55f, 0.45f, 0.55f), R(0f, 1f));
                Art.Part(root, Art.Cone, col, p, new Vector3(w, h, w), new Vector3(0, R(0f, 60f), 0)).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Art.Part(root, Art.Cone, new Color(0.93f, 0.9f, 0.96f), p + Vector3.up * h * 0.7f, new Vector3(w * 0.3f, h * 0.3f, w * 0.3f), new Vector3(0, R(0f, 60f), 0))
                    .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // ---- pines round the impact (they bend away from the blast)
            s_Trees.Clear();
            for (int i = 0; i < 90; i++)
            {
                float ang = R(0f, Mathf.PI * 2f), dist = 28f + Mathf.Pow(R(0f, 1f), 1.6f) * 420f;
                var p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if ((p - new Vector3(k_GroundCam.x, 0f, k_GroundCam.z)).magnitude < 16f) continue; // (not in the camera's face)
                // (not between the ground camera and the impact)
                var toCam = new Vector3(k_GroundCam.x, 0f, k_GroundCam.z);
                float along = Vector3.Dot(p, toCam.normalized);
                if (along > 0f && along < toCam.magnitude && Vector3.Cross(toCam.normalized, p).magnitude < 14f) continue;
                var tree = new GameObject("pine").transform;
                tree.SetParent(root, false);
                tree.localPosition = p;
                var body = new GameObject("body").transform;
                body.SetParent(tree, false);
                body.localScale = Vector3.one * R(0.8f, 1.5f);
                body.localRotation = Quaternion.Euler(0, R(0f, 360f), 0);
                Art.Part(body, Art.Cylinder, new Color(0.36f, 0.24f, 0.15f), new Vector3(0, 2f, 0), new Vector3(1.3f / cr, 4f / ch, 1.3f / cr));
                var green = Color.Lerp(new Color(0.14f, 0.3f, 0.19f), new Color(0.22f, 0.4f, 0.22f), R(0f, 1f));
                Art.Part(body, Art.Cone, green, new Vector3(0, 3f, 0), new Vector3(7.5f, 9f, 7.5f));
                Art.Part(body, Art.Cone, green * 1.1f, new Vector3(0, 7.5f, 0), new Vector3(5.5f, 7f, 5.5f));
                s_Trees.Add((body, dist, p.normalized));
            }

            // ---- clouds: a layer it punches down through, and high streaks for the entry
            var cloud = new Color(1f, 1f, 1f, 0.3f);
            var warm = new Color(1f, 0.88f, 0.8f, 0.24f);
            for (int i = 0; i < 30; i++)
                Ghosted(root, Art.Sphere, new Vector3(R(-300f, 300f), R(185f, 235f), R(-760f, -40f)), new Vector3(R(90f, 180f), R(16f, 28f), R(60f, 130f)), i % 2 == 0 ? cloud : warm);
            for (int i = 0; i < 10; i++) // (right on the way down: it goes through these)
                Ghosted(root, Art.Sphere, new Vector3(R(-55f, 55f), R(188f, 215f), R(-430f, -290f)), new Vector3(R(70f, 120f), R(14f, 22f), R(60f, 100f)), cloud);
            for (int i = 0; i < 14; i++)
                Ghosted(root, Art.Sphere, new Vector3(R(-250f, 250f), R(380f, 460f), R(-950f, -600f)), new Vector3(R(30f, 60f), R(4f, 7f), R(150f, 300f)), warm);

            // ---- the plasma sheath on the UFO (on only in the crash scene): cones of fire wrapping it, streaming back
            var sheath = new GameObject("plasma sheath").transform;
            sheath.SetParent(s_Ship.Root.transform, false);
            sheath.localPosition = Vector3.zero;
            sheath.localRotation = Quaternion.Euler(90f, 0, 0); // (the cones' tips point forward, +z: they widen out behind)
            s_SheathCones.Clear();
            foreach (var (w, col) in new[] { (54f, new Color(1f, 0.38f, 0.08f, 0.2f)), (40f, new Color(1f, 0.6f, 0.2f, 0.28f)), (28f, new Color(1f, 0.85f, 0.55f, 0.36f)) })
            {
                var cone = Ghosted(sheath, Art.Cone, Vector3.zero, new Vector3(w, w * 1.6f, w * 0.55f), col, GameSettings.MenuFireColour, MenuLooks.FireRef);
                s_SheathCones.Add((cone.transform, w));
            }
            s_Sheath = sheath;
            sheath.gameObject.SetActive(false);
            // ---- streaks rushing past it
            s_Streaks = LinesTile("crash streaks", root);
            s_Streaks2 = LinesTile("crash streaks 2", root);

            // ---- the trail
            s_Smoke.Clear(); s_Hot.Clear(); s_SmokeJit.Clear();
            for (int i = 0; i < SmokePuffs; i++)
            {
                s_Smoke.Add(Ghosted(root, Art.Sphere, Vector3.zero, Vector3.one, new Color(0.22f, 0.19f, 0.18f, 0.42f)).transform);
                s_Hot.Add(Ghosted(root, Art.Sphere, Vector3.zero, Vector3.one, new Color(1f, 0.55f, 0.15f, 0.5f), GameSettings.MenuFireColour, MenuLooks.FireRef).transform);
                s_SmokeJit.Add(new Vector3(R(-1f, 1f), R(-0.5f, 1f), R(-1f, 1f)));
            }

            // ---- the explosion (hidden until the impact)
            s_Boom = new GameObject("boom").transform;
            s_Boom.SetParent(root, false);
            s_Crater = Art.Part(s_Boom, Art.Sphere, new Color(0.12f, 0.1f, 0.09f), Vector3.zero, Vector3.one).transform;
            s_Core = Art.Part(s_Boom, Art.Sphere, Color.white, Vector3.zero, Vector3.one, default, false, Unlit(new Color(1f, 0.92f, 0.7f), 4f)).transform;
            s_Core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s_FireBalls.Clear();
            foreach (var col in new[] { new Color(1f, 0.85f, 0.45f, 0.75f), new Color(1f, 0.5f, 0.12f, 0.55f), new Color(0.75f, 0.2f, 0.06f, 0.42f) })
                s_FireBalls.Add(Ghosted(s_Boom, Art.Sphere, Vector3.zero, Vector3.one, col, GameSettings.MenuFireColour, MenuLooks.FireRef).transform);
            s_SmokeDome = Ghosted(s_Boom, Art.Sphere, Vector3.zero, Vector3.one, new Color(0.16f, 0.13f, 0.12f, 0.5f)).transform;
            s_Dust.Clear();
            for (int i = 0; i < DustPuffs; i++)
                s_Dust.Add(Ghosted(s_Boom, Art.Sphere, Vector3.zero, Vector3.one, new Color(0.58f, 0.45f, 0.33f, 0.42f)).transform);
            s_Spray.Clear();
            for (int i = 0; i < 14; i++)
            {
                float ang = i * Mathf.PI * 2f / 14f + R(-0.2f, 0.2f);
                var dir = new Vector3(Mathf.Cos(ang) * R(0.4f, 0.9f), 1f, Mathf.Sin(ang) * R(0.4f, 0.9f)).normalized;
                var sp = Art.Part(s_Boom, Art.Cone, new Color(0.38f, 0.28f, 0.18f), Vector3.zero, Vector3.one, Quaternion.FromToRotation(Vector3.up, dir).eulerAngles);
                sp.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s_Spray.Add(sp.transform);
            }
            s_Debris.Clear();
            var hullMat = Art.Mat(new Color(0.5f, 0.52f, 0.57f));
            var hotMat = Unlit(new Color(1f, 0.55f, 0.2f), 2.6f);
            for (int i = 0; i < 48; i++)
            {
                float ang = R(0f, Mathf.PI * 2f), out_ = R(25f, 110f);
                var vel = new Vector3(Mathf.Cos(ang) * out_, R(35f, 115f), Mathf.Sin(ang) * out_);
                bool hot = i % 3 == 0;
                var size = hot ? Vector3.one * R(1.2f, 2.6f) : new Vector3(R(2f, 6f), R(0.4f, 1f), R(2f, 5f));
                var chunk = Art.Part(s_Boom, Art.Cube, Color.white, Vector3.zero, size, default, false, hot ? hotMat : hullMat);
                chunk.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s_Debris.Add((chunk.transform, vel, new Vector3(R(-600f, 600f), R(-600f, 600f), R(-600f, 600f)), size));
            }
            s_BoomLight = new GameObject("boom light").AddComponent<Light>();
            s_BoomLight.transform.SetParent(s_Boom, false);
            s_BoomLight.type = LightType.Point; s_BoomLight.range = 650f; s_BoomLight.color = new Color(1f, 0.7f, 0.4f);
            PaintLight(s_BoomLight, GameSettings.MenuFireColour, MenuLooks.FireRef);
            s_Boom.gameObject.SetActive(false);

            s_ColoursDirty = true;
            s_Crash.SetActive(false);
            s_CrashOn = false;
        }
    }
}
