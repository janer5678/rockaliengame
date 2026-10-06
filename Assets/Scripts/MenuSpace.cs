using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Behind the main menu (Hud.MainMenu.cs; the dev main menu keeps the old flight over the map, MenuScene): our flying
    /// saucer (the airdrop ship, AirdropShip.BuildShip - smooth shaded, outlined by the post processing's ink lines) cruising
    /// through space - stars streaming past, a ringed planet and a glowing nebula off in the distance - while the camera
    /// cuts between cinematic shots of it: a low hero shot sweeping under its belly, a side-on track with the planet behind,
    /// a chase from behind and above, a fast fly-by whip and a slow push in on its dome, each with its own little move.
    /// The ship banks and bobs as it goes and its rim of lights turns. Far out past everything (Origin), with the camera
    /// cleared to black space while it's showing.
    /// </summary>
    public static class MenuSpace
    {
        static readonly Vector3 Origin = new Vector3(-6000f, 1500f, 0f);
        static GameObject s_Root;
        static AirdropShip.Parts s_Ship;
        static Transform s_Stars, s_Stars2;
        static bool s_Showing;
        static CameraClearFlags s_OldClear;
        static Color s_OldBg;
        static float s_OldFar, s_Start, s_Fade = 1f;
        const float StarBox = 700f, Cruise = 260f, LinesBox = 240f;
        static Transform s_Lines, s_Lines2;
        static float s_Travel;
        static Vector3 s_Rattle;
        static readonly List<Transform> s_Exhaust = new List<Transform>();
        static readonly List<Vector3> s_ExhaustBase = new List<Vector3>();

        /// <summary>The menu's fade in from black (1 at the start, gone in a second).</summary>
        public static float Fade => s_Showing ? s_Fade : 0f;
        /// <summary>The space shot is up (no distance haze: PostFx).</summary>
        public static bool Showing => s_Showing;

        /// <summary>Every frame (Bootstrap): show the space shot (on: the main menu's up) or put it away.</summary>
        public static void Tick(bool on)
        {
            var cam = Camera.main;
            if (!on || cam == null)
            {
                if (s_Showing) Hide(cam);
                return;
            }
            if (s_Root == null) Build();
            if (!s_Showing)
            {
                s_Showing = true;
                s_Root.SetActive(true);
                s_OldClear = cam.clearFlags;
                s_OldBg = cam.backgroundColor;
                s_OldFar = cam.farClipPlane;
                s_Start = Time.unscaledTime;
                s_Fade = 1f;
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.005f, 0.03f);
            cam.farClipPlane = Mathf.Max(s_OldFar, 3000f);
            s_Fade = Mathf.MoveTowards(s_Fade, 0f, Time.unscaledDeltaTime);
            float t = Time.unscaledTime - s_Start;
            Animate(t);
            Shoot(cam, t);
        }

        static void Hide(Camera cam)
        {
            s_Showing = false;
            if (s_Root) s_Root.SetActive(false);
            if (cam == null) return;
            cam.clearFlags = s_OldClear;
            cam.backgroundColor = s_OldBg;
            cam.farClipPlane = s_OldFar;
        }

        // ------------------------------------------------------------------ the ship and the stars

        static void Animate(float t)
        {
            var ship = s_Ship.Root.transform;
            // tearing along +z (the stars do the moving): S-curves with a hard bank into each, a nose that dips into the
            // turns, a boost every few seconds (it surges ahead and the engines flare) and now and then a barrel roll
            float bank = Mathf.Sin(t * 0.45f) * 22f + Mathf.Sin(t * 1.3f) * 4f;
            float boost = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 0.8f)), 6f);           // 0..1 pulses
            float rollT = Mathf.Repeat(t, 14f) - 10f;                                     // a roll every 14 s
            float roll = rollT > 0f && rollT < 1.3f ? Mathf.SmoothStep(0f, 360f, rollT / 1.3f) : 0f;
            ship.localPosition = new Vector3(Mathf.Sin(t * 0.45f - 1.2f) * 8f, Mathf.Sin(t * 0.9f) * 2f + Mathf.Sin(t * 7f) * 0.06f, boost * 6f);
            ship.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.6f) * 4f - boost * 6f, Mathf.Sin(t * 0.45f - 1.2f) * 10f, -bank - roll);
            if (s_Ship.Rim) s_Ship.Rim.localRotation = Quaternion.Euler(0f, t * (140f + boost * 400f), 0f);
            // the engines: flickering, stretching on a boost
            for (int i = 0; i < s_Exhaust.Count; i++)
            {
                var e = s_Exhaust[i];
                if (e == null) continue;
                float f = 1f + boost * 1.6f + Mathf.PerlinNoise(t * 9f, i * 3.7f) * 0.35f;
                e.localScale = new Vector3(s_ExhaustBase[i].x * (0.9f + boost * 0.3f), s_ExhaustBase[i].y * f, s_ExhaustBase[i].z * (0.9f + boost * 0.3f));
            }
            // each flame layer licks in and out on its own (the outer ones more): a fire, not a cone
            for (int i = 0; i < s_Flames.Count; i++)
            {
                var (ft, fs) = s_Flames[i];
                if (ft == null) continue;
                int layer = i % 4;
                float n = Mathf.PerlinNoise(t * (16f + layer * 5f), i * 1.7f);
                float len = 0.75f + n * (0.4f + layer * 0.25f) + boost * (0.6f + layer * 0.3f);
                float w = 0.85f + Mathf.PerlinNoise(t * 21f, i * 3.1f + 9f) * 0.3f;
                ft.localScale = new Vector3(fs.x * w, fs.y * len, fs.z * w);
                ft.localRotation = Quaternion.Euler(Mathf.Sin(t * 13f + i) * layer * 2.5f, 0f, Mathf.Cos(t * 11f + i * 2f) * layer * 2.5f);
            }
            // the meteor fire builds up over the first few seconds, then roars and flickers (hotter on a boost)
            float heat = Mathf.Clamp01((t - 0.5f) / 4f) * (0.85f + 0.15f * Mathf.Sin(t * 1.7f)) * (1f + boost * 0.35f);
            for (int i = 0; i < s_Fire.Count; i++)
            {
                var (ft, fb) = s_Fire[i];
                if (ft == null) continue;
                float n = Mathf.PerlinNoise(t * 14f, i * 2.3f);
                float f = Mathf.Max(0.001f, heat * (0.85f + 0.3f * n));
                ft.localScale = new Vector3(fb.x * f, fb.y * f * (0.8f + 0.5f * n), fb.z * f);
            }
            // it's going so fast it shakes: a hard rattle and the odd jolt
            float jolt = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 2.3f)), 30f);
            var rattle = new Vector3(Mathf.PerlinNoise(t * 23f, 1.3f) - 0.5f, Mathf.PerlinNoise(t * 27f, 7.1f) - 0.5f, Mathf.PerlinNoise(t * 19f, 3.7f) - 0.5f) * (0.7f + jolt * 2f);
            ship.localPosition += rattle;
            ship.localRotation *= Quaternion.Euler(rattle.y * 2.5f, rattle.x * 1.5f, rattle.z * 3f);
            s_Rattle = rattle;
            // the star fields stream past fast (faster on a boost): two tiles, so there's never an edge
            s_Travel += Time.unscaledDeltaTime * Cruise * (1f + boost * 1.2f);
            float z = -(s_Travel % StarBox);
            s_Stars.localPosition = new Vector3(0, 0, z);
            s_Stars2.localPosition = new Vector3(0, 0, z + StarBox);
            // speed lines whipping past close by
            float lz = -((s_Travel * 1.6f) % LinesBox);
            s_Lines.localPosition = new Vector3(0, 0, lz);
            s_Lines2.localPosition = new Vector3(0, 0, lz + LinesBox);
        }

        /// <summary>The cinematic shots, one after another (8 s each, a cut between), round and round.</summary>
        static void Shoot(Camera cam, float t)
        {
            const float Shot = 8f;
            // (it opens on the chase, then the side-on track, then round them all in order)
            int idx = Mathf.FloorToInt(t / Shot);
            int n = idx == 0 ? 2 : idx == 1 ? 1 : (idx - 2) % 5;
            float u = (t % Shot) / Shot;                      // 0..1 through this shot
            float e = u * u * (3f - 2f * u);                  // eased
            var ship = s_Ship.Root.transform.position;
            Vector3 pos, look = ship;
            float fov = 50f;
            switch (n)
            {
                case 0: // the hero shot: low and in front, sweeping round under it, looking up as it comes over
                {
                    float a = Mathf.Lerp(-55f, 35f, e) * Mathf.Deg2Rad;
                    pos = ship + new Vector3(Mathf.Sin(a) * 62f, -16f + e * 6f, Mathf.Cos(a) * 62f);
                    look = ship + new Vector3(0, 2f, 0);
                    fov = 44f;
                    break;
                }
                case 1: // side-on track, the planet behind, drifting closer
                    pos = ship + new Vector3(-78f + e * 16f, 6f, -14f + e * 16f);
                    look = ship + new Vector3(0, 0, 8f);
                    fov = 40f;
                    break;
                case 2: // the chase: behind and above, the rim lights turning, the stars rushing ahead
                    pos = ship + new Vector3(Mathf.Sin(t * 0.6f) * 5f, 24f - e * 6f, -80f + e * 12f);
                    look = ship + new Vector3(0, 0, 30f);
                    fov = 54f;
                    break;
                case 3: // the fly-by: it comes at the camera and whips past
                    pos = ship + new Vector3(34f, 8f, Mathf.Lerp(170f, -70f, u));
                    look = ship;
                    fov = 50f;
                    break;
                default: // the slow push in on the glass dome from above
                    pos = ship + new Vector3(-42f + e * 12f, 40f - e * 10f, 44f - e * 12f);
                    look = ship + new Vector3(0, 2f, 0);
                    fov = 40f;
                    break;
            }
            // the camera shakes with it (a little less than the ship: it's riding alongside)
            pos += s_Rattle * 0.35f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(s_Rattle.y * 0.8f, s_Rattle.x * 0.8f, s_Rattle.z * 1.2f));
            cam.fieldOfView = fov;
        }

        static void Build()
        {
            s_Root = new GameObject("MenuSpace");
            s_Root.transform.position = Origin;
            s_Ship = AirdropShip.BuildShip("menu ufo");
            s_Ship.Root.transform.SetParent(s_Root.transform, false);
            SmoothShadeHook.Add(s_Ship.Root, true);
            if (s_Ship.Spot) s_Ship.Spot.enabled = false;
            // (no coloured lights on top: plain metal and glass - the airdrop ship's turning rim of lights is put away)
            if (s_Ship.Rim) s_Ship.Rim.gameObject.SetActive(false);
            // the stars: two tiles of a box of little glowing points, streaming past
            s_Stars = StarTile("stars");
            s_Stars2 = StarTile("stars 2");
            s_Lines = LinesTile("speed lines");
            s_Lines2 = LinesTile("speed lines 2");
            BuildDetail(s_Ship.Root.transform);
            BuildDomeAndFire(s_Ship.Root.transform);
            // a ringed planet off to the side, and a nebula glow far behind it
            var planet = Art.Part(s_Root.transform, Art.Sphere, new Color(0.55f, 0.35f, 0.75f), new Vector3(-420f, -60f, 900f), Vector3.one * 360f);
            planet.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ring = Art.Part(s_Root.transform, Art.Cylinder, Color.white, new Vector3(-420f, -60f, 900f), new Vector3(620f, 0.5f, 620f) / (2f * Art.Cylinder.bounds.extents.x), new Vector3(18f, 0f, -12f), false, Art.Ghost(new Color(1f, 0.8f, 0.55f, 0.35f)));
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var (p, s, c) in new[] { (new Vector3(500f, 200f, 1400f), 700f, new Color(0.9f, 0.2f, 0.8f, 0.07f)), (new Vector3(700f, 50f, 1300f), 500f, new Color(0.2f, 0.8f, 1f, 0.06f)), (new Vector3(300f, -150f, 1500f), 600f, new Color(0.5f, 0.3f, 1f, 0.06f)) })
            {
                var neb = Art.Part(s_Root.transform, Art.Sphere, Color.white, p, Vector3.one * s, default, false, Art.Ghost(c));
                neb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // light: a warm key from the front-left (the sun's still on too) and a cool rim from behind
            var key = new GameObject("key light").AddComponent<Light>();
            key.transform.SetParent(s_Root.transform, false);
            key.transform.localPosition = new Vector3(-60f, 45f, 60f);
            key.type = LightType.Point; key.range = 260f; key.intensity = 7f; key.color = new Color(1f, 0.88f, 0.75f);
            var rim = new GameObject("rim light").AddComponent<Light>();
            rim.transform.SetParent(s_Root.transform, false);
            rim.transform.localPosition = new Vector3(50f, -10f, -70f);
            rim.type = LightType.Point; rim.range = 260f; rim.intensity = 6f; rim.color = new Color(0.45f, 0.7f, 1f);
        }



        /// <summary>The ship's extra detail for the menu: panel lines and hatches round the hull, glowing portholes, an
        /// opaque paneled glass dome with ribs, three engine pods out the back with long
        /// flickering exhaust flames, and two swept fins.</summary>
        static void BuildDetail(Transform shipRoot)
        {
            s_Exhaust.Clear();
            s_Flames.Clear();
            s_ExhaustBase.Clear();
            // (laid out for a hull 13 m across and 2.25 m high - the airdrop ship's sphere at radius 0.5: scaled to the
            // real mesh, so nothing ends up buried inside the hull)
            var ship = new GameObject("detail").transform;
            ship.SetParent(shipRoot, false);
            ship.localScale = new Vector3(2f * Art.Sphere.bounds.extents.x, 2f * Art.Sphere.bounds.extents.y, 2f * Art.Sphere.bounds.extents.x);
            float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
            var dark = new Color(0.18f, 0.19f, 0.22f);
            var hull = new Color(0.55f, 0.58f, 0.64f);
            // panel lines running out from the dome to the rim, raised hatches between them, and a darker band low on
            // the hull (two-tone paint), all following the hull's curve
            float HullY(float r) => 2.25f * Mathf.Sqrt(Mathf.Max(0f, 1f - (r / 13f) * (r / 13f)));
            for (int i = 0; i < 24; i++)
            {
                float a = i * 360f / 24f;
                var dir = Quaternion.Euler(0, a, 0);
                for (float r = 5.2f; r < 12.6f; r += 1.2f)
                {
                    float y = HullY(r), slope = Mathf.Atan2(HullY(r + 0.3f) - HullY(r - 0.3f), 0.6f) * Mathf.Rad2Deg;
                    Art.Box(ship, new Color(0.09f, 0.09f, 0.11f), dir * new Vector3(0, y + 0.07f, r), new Vector3(0.17f, 0.12f, 1.25f), new Vector3(-slope, a, 0));
                }
                if (i % 2 == 0)
                {
                    float r = 8.6f, y = HullY(r), slope = Mathf.Atan2(HullY(r + 0.3f) - HullY(r - 0.3f), 0.6f) * Mathf.Rad2Deg;
                    Art.Box(ship, hull * 1.25f, Quaternion.Euler(0, a + 7.5f, 0) * new Vector3(0, y + 0.1f, r), new Vector3(1.7f, 0.2f, 1.5f), new Vector3(-slope, a + 7.5f, 0));
                }
            }
            for (int i = 0; i < 40; i++)
            {
                float a = i * 360f / 40f;
                Art.Box(ship, dark * 1.4f, Quaternion.Euler(0, a, 0) * new Vector3(0, -0.45f, 12.2f), new Vector3(1.95f, 0.5f, 0.3f), new Vector3(20f, a, 0));
            }
            // portholes round the rim: dark tinted glass (no coloured lights on top)
            for (int i = 0; i < 28; i++)
            {
                float a = i * Mathf.PI * 2f / 28f;
                Art.Part(ship, Art.Sphere, Color.white, new Vector3(Mathf.Sin(a) * 12.5f, 0.6f, Mathf.Cos(a) * 12.5f), new Vector3(0.42f, 0.26f, 0.42f), default, false, PortholeGlass());
            }
            // swept fins at the back
            for (int s = -1; s <= 1; s += 2)
                Art.Box(ship, hull * 0.9f, new Vector3(s * 7f, 1.6f, -11f), new Vector3(0.3f, 2.6f, 4.5f), new Vector3(-25f, s * 12f, s * 30f));
            // engine pods well out behind the hull on pylons (clear of the saucer), each with a layered flame: a white-hot
            // core, an orange body and a long red-orange outer plume (they flicker and stretch on their own: Animate)
            foreach (var p in new[] { new Vector3(-5f, 0.3f, -16f), new Vector3(5f, 0.3f, -16f), new Vector3(0f, -0.5f, -16.6f) })
            {
                // the pylon from the hull's back edge out to the pod
                Art.Box(ship, dark * 1.3f, new Vector3(p.x * 0.85f, p.y + 0.1f, -12.6f), new Vector3(0.9f, 0.5f, 3.6f));
                Art.Part(ship, Art.Cylinder, dark, p, new Vector3(2.6f / cr, 3.4f / ch, 2.6f / cr), new Vector3(90f, 0, 0));
                Art.Part(ship, Art.Cylinder, hull, p + new Vector3(0, 0, 1.6f), new Vector3(2.9f / cr, 0.4f / ch, 2.9f / cr), new Vector3(90f, 0, 0));
                Art.Part(ship, Art.Cylinder, hull * 0.7f, p - new Vector3(0, 0, 1.55f), new Vector3(2.4f / cr, 0.35f / ch, 2.4f / cr), new Vector3(90f, 0, 0)); // (the nozzle lip)
                Art.Part(ship, Art.Cylinder, Color.white, p - new Vector3(0, 0, 1.75f), new Vector3(1.9f / cr, 0.04f / ch, 1.9f / cr), new Vector3(90f, 0, 0), false, Unlit(new Color(0.7f, 0.9f, 1f), 3f));
                var flame = new GameObject("flame").transform;
                flame.SetParent(ship, false);
                flame.localPosition = p - new Vector3(0, 0, 1.75f);
                flame.localRotation = Quaternion.Euler(-90f, 0, 0); // (the cone's tip points back, -z)
                void Layer(float w, float len, Material m)
                {
                    var c = Art.Part(flame, Art.Cone, Color.white, Vector3.zero, new Vector3(w, len, w), default, false, m);
                    c.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    s_Flames.Add((c.transform, new Vector3(w, len, w)));
                }
                Layer(1.1f, 3.2f, Unlit(new Color(0.88f, 0.96f, 1f), 3.2f));
                Layer(1.8f, 6.5f, Art.Ghost(new Color(0.35f, 0.75f, 1f, 0.75f)));
                Layer(2.5f, 11f, Art.Ghost(new Color(0.2f, 0.45f, 1f, 0.38f)));
                Layer(3.1f, 15f, Art.Ghost(new Color(0.25f, 0.2f, 0.95f, 0.16f)));
                s_Exhaust.Add(flame);
                s_ExhaustBase.Add(Vector3.one);
            }
            var l = new GameObject("engine light").AddComponent<Light>();
            l.transform.SetParent(ship, false);
            l.transform.localPosition = new Vector3(0, 0, -21f);
            l.type = LightType.Point; l.range = 40f; l.intensity = 5f; l.color = new Color(0.4f, 0.7f, 1f);
        }

        /// <summary>Each engine flame layer and its size (they flicker on their own: Animate).</summary>
        static readonly List<(Transform t, Vector3 size)> s_Flames = new List<(Transform, Vector3)>();


        static readonly List<(Transform t, Vector3 baseScale)> s_Fire = new List<(Transform, Vector3)>();

        /// <summary>The dome as opaque, glossy paneled glass (not see-through): ribs up it, rings round it, a metal collar
        /// at its foot. And the meteor fire at the front: glowing shells of heat piling up on the leading edge and flame
        /// tongues streaming back round the rim (they build up and flicker: Animate).</summary>
        static void BuildDomeAndFire(Transform ship)
        {
            var metal = new Color(0.22f, 0.24f, 0.28f);
            // the ship's own dome (BuildShip: a sphere scaled 9 x 5 x 9 at y 2), now opaque, dark and shiny
            var glass = new Material(Art.Mat(new Color(0.1f, 0.28f, 0.38f))) { name = "menu dome glass" };
            if (glass.HasProperty("_Smoothness")) glass.SetFloat("_Smoothness", 0.93f);
            if (glass.HasProperty("_Metallic")) glass.SetFloat("_Metallic", 0.3f);
            foreach (Transform c in ship)
                if ((c.localScale - new Vector3(9f, 5f, 9f)).sqrMagnitude < 0.01f && c.TryGetComponent(out Renderer dr)) dr.sharedMaterial = glass;
            for (int i = 0; i < 10; i++)
                Art.Part(ship, Art.Sphere, metal, new Vector3(0, 2f, 0), new Vector3(0.16f, 5.05f, 9.04f), new Vector3(0, i * 18f, 0));
            foreach (float h in new[] { 2.25f, 3.3f, 4.2f })
            {
                float r = 9f * Mathf.Sqrt(Mathf.Max(0f, 1f - (h / 5f) * (h / 5f)));
                Art.Part(ship, Art.Sphere, metal, new Vector3(0, 2f + h, 0), new Vector3(r + 0.05f, 0.1f, r + 0.05f));
            }
            Art.Part(ship, Art.Cylinder, metal * 1.2f, new Vector3(0, 4.25f, 0), new Vector3(9.4f, 0.22f, 9.4f));
            Art.Part(ship, Art.Sphere, metal, new Vector3(0, 7.02f, 0), new Vector3(1.2f, 0.35f, 1.2f));

            // the meteor fire on the front (+z) edge (the rim reaches 30 m out)
            s_Fire.Clear();
            void Shell(Vector3 at, Vector3 s, Color c)
            {
                var p = Art.Part(ship, Art.Sphere, Color.white, at, s, default, false, Art.Ghost(c));
                p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s_Fire.Add((p.transform, s));
            }
            // (radius-1 spheres: these are half-sizes) layers of heat piled up on the leading edge, white-hot at the front,
            // a wide haze of heat round it, and a bow of fire pushed out ahead of it
            Shell(new Vector3(0, 0, 27.5f), new Vector3(28f, 7f, 11f), new Color(1f, 0.25f, 0.05f, 0.12f));
            Shell(new Vector3(0, 0, 28.5f), new Vector3(22f, 5.5f, 8f), new Color(1f, 0.2f, 0.04f, 0.2f));
            Shell(new Vector3(0, 0, 29f), new Vector3(18f, 4.2f, 6.5f), new Color(1f, 0.35f, 0.08f, 0.32f));
            Shell(new Vector3(0, 0, 30f), new Vector3(14f, 3.2f, 4.8f), new Color(1f, 0.55f, 0.12f, 0.45f));
            Shell(new Vector3(0, 0, 31f), new Vector3(10f, 2.2f, 3f), new Color(1f, 0.85f, 0.45f, 0.65f));
            Shell(new Vector3(0, 0, 31.8f), new Vector3(6f, 1.3f, 1.6f), new Color(1f, 0.97f, 0.85f, 0.85f));
            Shell(new Vector3(0, 0, 33.5f), new Vector3(9f, 1.8f, 3.2f), new Color(1f, 0.5f, 0.1f, 0.3f));
            Shell(new Vector3(0, 0, 35.5f), new Vector3(6f, 1.2f, 2.4f), new Color(1f, 0.7f, 0.25f, 0.25f));
            // the fire streaming back from the front along the outside of the rim (clear of the hull - nothing pokes
            // through the ship): sheets of flame on each side, longest at the front, tapering off round the sides
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 12; i++)
                {
                    float a = side * Mathf.Lerp(8f, 95f, i / 11f) * Mathf.Deg2Rad;
                    var radial = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                    if (tangent.z > 0f) tangent = -tangent;
                    tangent = (tangent - radial * 0.08f).normalized; // (peeling off outwards a little)
                    float f = i / 11f;
                    var at = radial * (31.5f + f * 0.8f) + Vector3.up * Random.Range(-0.6f, 0.6f);
                    var lick = new GameObject("fire trail").transform;
                    lick.SetParent(ship, false);
                    lick.localPosition = at;
                    lick.localRotation = Quaternion.FromToRotation(Vector3.up, tangent);
                    float w = Mathf.Lerp(4.2f, 1.6f, f), len = Mathf.Lerp(26f, 9f, f) * Random.Range(0.85f, 1.15f);
                    var cone = Art.Part(lick, Art.Cone, Color.white, Vector3.zero, new Vector3(w, len, w * 0.6f), default, false,
                        Art.Ghost(Color.Lerp(new Color(1f, 0.75f, 0.3f, 0.5f), new Color(1f, 0.35f, 0.08f, 0.35f), f)));
                    cone.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    s_Fire.Add((lick, Vector3.one));
                }
            var fl = new GameObject("meteor light").AddComponent<Light>();
            fl.transform.SetParent(ship, false);
            fl.transform.localPosition = new Vector3(0, 2f, 34f);
            fl.type = LightType.Point; fl.range = 95f; fl.intensity = 9f; fl.color = new Color(1f, 0.55f, 0.2f);
        }
        /// <summary>Speed lines: long thin streaks close round the ship's path, whipping past (two tiles).</summary>
        static Transform LinesTile(string name)
        {
            var rng = new System.Random(name.Length * 131 + 9);
            var v = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < 160; i++)
            {
                var c = new Vector3(((float)rng.NextDouble() - 0.5f) * 160f, ((float)rng.NextDouble() - 0.5f) * 90f, (float)rng.NextDouble() * LinesBox);
                if (Mathf.Abs(c.x) < 24f && Mathf.Abs(c.y) < 14f) c.x += c.x < 0 ? -24f : 24f; // (never through the ship)
                float s = 0.05f, len = 6f + (float)rng.NextDouble() * 14f;
                int b = v.Count;
                v.Add(c + new Vector3(-s, 0, 0)); v.Add(c + new Vector3(s, 0, 0)); v.Add(c + new Vector3(0, -s, 0)); v.Add(c + new Vector3(0, s, 0));
                v.Add(c + new Vector3(0, 0, -len)); v.Add(c + new Vector3(0, 0, len));
                int[] f = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
                foreach (int x in f) tris.Add(b + x);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(s_Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Unlit(new Color(0.75f, 0.9f, 1f), 1.2f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
        /// <summary>Dark tinted porthole glass: glossy, not lit up.</summary>
        static Material PortholeGlass()
        {
            var m = new Material(Art.Mat(new Color(0.12f, 0.16f, 0.2f))) { name = "menu porthole" };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.95f);
            return m;
        }

        /// <summary>An unlit glowing material (RockGame/Glow, like the tree X): the same bright colour whatever the light.</summary>
        static Material Unlit(Color c, float intensity)
        {
            var sh = Resources.Load<Shader>("World/Glow");
            if (sh == null || !sh.isSupported) return Workbench.Glow(c, intensity);
            var m = new Material(sh) { name = "menu glow" };
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            return m;
        }
        static Transform StarTile(string name)
        {
            var rng = new System.Random(name.Length * 977 + 3);
            var v = new List<Vector3>();
            var tris = new List<int>();
            var cols = new List<Color>();
            for (int i = 0; i < 1400; i++)
            {
                var c = new Vector3(((float)rng.NextDouble() - 0.5f) * 1400f, ((float)rng.NextDouble() - 0.5f) * 700f, (float)rng.NextDouble() * StarBox);
                if (Mathf.Abs(c.x) < 60f && Mathf.Abs(c.y) < 40f) c.x += c.x < 0 ? -60f : 60f; // (none right through the ship)
                float s = 0.12f + (float)rng.NextDouble() * (rng.NextDouble() < 0.08 ? 0.7f : 0.25f);
                float len = s * (3f + (float)rng.NextDouble() * 6f); // (streaked along the way we fly)
                var tint = rng.NextDouble() < 0.15 ? new Color(0.7f, 0.85f, 1f) : rng.NextDouble() < 0.1 ? new Color(1f, 0.8f, 0.9f) : Color.white;
                int b = v.Count;
                // a thin diamond stretched along z, seen from any side
                v.Add(c + new Vector3(-s, 0, 0)); v.Add(c + new Vector3(s, 0, 0)); v.Add(c + new Vector3(0, -s, 0)); v.Add(c + new Vector3(0, s, 0));
                v.Add(c + new Vector3(0, 0, -len)); v.Add(c + new Vector3(0, 0, len));
                for (int q = 0; q < 6; q++) cols.Add(tint);
                int[] f = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
                foreach (int x in f) tris.Add(b + x);
            }
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(s_Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Unlit(Color.white, 1.6f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localPosition = new Vector3(0, 0, 0);
            return go.transform;
        }
    }
}
