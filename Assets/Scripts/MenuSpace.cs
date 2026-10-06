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
        const float StarBox = 700f, Cruise = 90f;

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
            // cruising along +z (the stars do the moving): banking into gentle S-curves and bobbing
            float bank = Mathf.Sin(t * 0.35f) * 14f + Mathf.Sin(t * 0.9f) * 3f;
            ship.localPosition = new Vector3(Mathf.Sin(t * 0.35f - 1.2f) * 6f, Mathf.Sin(t * 0.7f) * 1.6f, 0f);
            ship.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.5f) * 3f, Mathf.Sin(t * 0.35f - 1.2f) * 8f, -bank);
            if (s_Ship.Rim) s_Ship.Rim.localRotation = Quaternion.Euler(0f, t * 70f, 0f);
            // the star fields stream past (two tiles, so there's never an edge)
            float z = -((t * Cruise) % StarBox);
            s_Stars.localPosition = new Vector3(0, 0, z);
            s_Stars2.localPosition = new Vector3(0, 0, z + StarBox);
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
            // the paint job: a pink stripe round the hull and a ring of acid-green running lights on top (unlit: they glow)
            var stripe = Art.Part(s_Ship.Root.transform, Art.Cylinder, Color.white, new Vector3(0, 0.55f, 0), new Vector3(27.4f, 0.12f, 27.4f) / (2f * Art.Cylinder.bounds.extents.x), default, false, Unlit(new Color(1f, 0.3f, 0.7f), 1.4f));
            stripe.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                var dot = Art.Part(s_Ship.Root.transform, Art.Sphere, Color.white, new Vector3(Mathf.Sin(a) * 8.5f, 1.65f, Mathf.Cos(a) * 8.5f), Vector3.one * 0.7f, default, false, Unlit(new Color(0.65f, 1f, 0.25f), 1.8f));
                dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // the stars: two tiles of a box of little glowing points, streaming past
            s_Stars = StarTile("stars");
            s_Stars2 = StarTile("stars 2");
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
