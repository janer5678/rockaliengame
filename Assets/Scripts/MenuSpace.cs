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
        static GameObject s_Tip;
        static float s_Travel;
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
            if (s_Tip) s_Tip.SetActive(Mathf.Repeat(t, 1f) < 0.5f);
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
            int n = Mathf.FloorToInt(t / Shot) % 5;
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
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
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
            // the paint job: a glowing pink band of segments round the rim and a ring of acid-green running lights on top
            for (int i = 0; i < 56; i++)
            {
                float a = i * Mathf.PI * 2f / 56f;
                var seg = Art.Box(s_Ship.Root.transform, Color.white, Vector3.Scale(new Vector3(Mathf.Sin(a) * 13.05f, 0.62f, Mathf.Cos(a) * 13.05f), 2f * Art.Sphere.bounds.extents), new Vector3(1.1f, 0.16f, 0.12f), new Vector3(0, a * Mathf.Rad2Deg + 90f, 0), false, Unlit(new Color(1f, 0.3f, 0.7f), 1.4f));
                seg.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                var dot = Art.Part(s_Ship.Root.transform, Art.Sphere, Color.white, Vector3.Scale(new Vector3(Mathf.Sin(a) * 8.5f, 1.75f, Mathf.Cos(a) * 8.5f), 2f * Art.Sphere.bounds.extents), Vector3.one * 0.7f, default, false, Unlit(new Color(0.65f, 1f, 0.25f), 1.8f));
                dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // the stars: two tiles of a box of little glowing points, streaming past
            s_Stars = StarTile("stars");
            s_Stars2 = StarTile("stars 2");
            s_Lines = LinesTile("speed lines");
            s_Lines2 = LinesTile("speed lines 2");
            BuildDetail(s_Ship.Root.transform);
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



        /// <summary>The ship's extra detail for the menu: panel seams round the hull, a ring of glowing portholes, an alien
        /// at the controls under the dome, an antenna with a blinking tip, three engine pods out the back with long
        /// flickering exhaust flames, and two swept fins.</summary>
        static void BuildDetail(Transform shipRoot)
        {
            s_Exhaust.Clear();
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
                    Art.Box(ship, Color.white, Quaternion.Euler(0, a + 7.5f, 0) * new Vector3(0, y + 0.2f, r + 0.62f), new Vector3(1.4f, 0.06f, 0.1f), new Vector3(-slope, a + 7.5f, 0), false, Unlit(new Color(0.45f, 0.95f, 1f), 1.8f));
                }
            }
            for (int i = 0; i < 40; i++)
            {
                float a = i * 360f / 40f;
                Art.Box(ship, dark * 1.4f, Quaternion.Euler(0, a, 0) * new Vector3(0, -0.45f, 12.2f), new Vector3(1.95f, 0.5f, 0.3f), new Vector3(20f, a, 0));
            }
            // a glowing cyan ring of segments round the top, seen from anywhere above
            for (int i = 0; i < 48; i++)
            {
                float a = i * 360f / 48f, r = 10.8f, y = HullY(r), slope = Mathf.Atan2(HullY(r + 0.3f) - HullY(r - 0.3f), 0.6f) * Mathf.Rad2Deg;
                Art.Box(ship, Color.white, Quaternion.Euler(0, a, 0) * new Vector3(0, y + 0.08f, r), new Vector3(1.15f, 0.08f, 0.3f), new Vector3(-slope, a, 0), false, Unlit(new Color(0.4f, 0.95f, 1f), 1.6f));
            }
            // portholes round the rim
            for (int i = 0; i < 28; i++)
            {
                float a = i * Mathf.PI * 2f / 28f;
                Art.Part(ship, Art.Sphere, Color.white, new Vector3(Mathf.Sin(a) * 12.5f, 0.6f, Mathf.Cos(a) * 12.5f), new Vector3(0.42f, 0.26f, 0.42f), default, false, Unlit(new Color(1f, 0.9f, 0.55f), 1.8f));
            }
            // the pilot: an alien head and shoulders under the dome, lit green
            Art.Part(ship, Art.Sphere, new Color(0.45f, 0.85f, 0.5f), new Vector3(0, 3.2f, 1.2f), new Vector3(1.4f, 1.7f, 1.4f));
            Art.Part(ship, Art.Capsule, new Color(0.3f, 0.6f, 0.35f), new Vector3(0, 2.2f, 1.2f), new Vector3(1.8f, 0.8f, 1.2f));
            for (int s = -1; s <= 1; s += 2) Art.Part(ship, Art.Sphere, Color.black, new Vector3(s * 0.35f, 3.3f, 1.8f), new Vector3(0.45f, 0.6f, 0.2f), new Vector3(0, 0, s * -25f));
            // antenna with a blinking tip
            Art.Part(ship, Art.Cylinder, dark, new Vector3(0, 5.6f, 0), new Vector3(0.12f / cr * 2f, 1.4f / ch * 2f, 0.12f / cr * 2f));
            s_Tip = Art.Part(ship, Art.Sphere, Color.white, new Vector3(0, 7f, 0), Vector3.one * 0.45f, default, false, Unlit(new Color(1f, 0.25f, 0.3f), 2.5f));
            // swept fins at the back
            for (int s = -1; s <= 1; s += 2)
                Art.Box(ship, hull * 0.9f, new Vector3(s * 7f, 1.6f, -11f), new Vector3(0.3f, 2.6f, 4.5f), new Vector3(-25f, s * 12f, s * 30f));
            // engine pods out the back with their flames (cones pointing back, stretched and flickering: Animate)
            foreach (var p in new[] { new Vector3(-5f, 0.2f, -12.5f), new Vector3(5f, 0.2f, -12.5f), new Vector3(0f, -0.6f, -13f) })
            {
                Art.Part(ship, Art.Cylinder, dark, p, new Vector3(2.6f / cr, 3f / ch, 2.6f / cr), new Vector3(90f, 0, 0));
                Art.Part(ship, Art.Cylinder, hull, p + new Vector3(0, 0, 1.4f), new Vector3(2.9f / cr, 0.4f / ch, 2.9f / cr), new Vector3(90f, 0, 0));
                Art.Part(ship, Art.Cylinder, Color.white, p - new Vector3(0, 0, 1.52f), new Vector3(2.1f / cr, 0.04f / ch, 2.1f / cr), new Vector3(90f, 0, 0), false, Unlit(new Color(0.55f, 0.95f, 1f), 2.4f));
                var flame = new GameObject("flame").transform;
                flame.SetParent(ship, false);
                flame.localPosition = p - new Vector3(0, 0, 1.5f);
                flame.localRotation = Quaternion.Euler(-90f, 0, 0); // (the cone's tip points back, -z)
                var core = Art.Part(flame, Art.Cone, Color.white, new Vector3(0, 2.5f, 0), new Vector3(1.4f, 5f, 1.4f), default, false, Art.Ghost(new Color(0.75f, 0.98f, 1f, 0.85f)));
                var glow = Art.Part(flame, Art.Cone, Color.white, new Vector3(0, 4f, 0), new Vector3(2.4f, 8f, 2.4f), default, false, Art.Ghost(new Color(0.3f, 0.6f, 1f, 0.35f)));
                core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s_Exhaust.Add(flame);
                s_ExhaustBase.Add(Vector3.one);
            }
            var l = new GameObject("engine light").AddComponent<Light>();
            l.transform.SetParent(ship, false);
            l.transform.localPosition = new Vector3(0, 0, -17f);
            l.type = LightType.Point; l.range = 30f; l.intensity = 3f; l.color = new Color(0.45f, 0.8f, 1f);
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
