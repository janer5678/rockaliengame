using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// More of the ship lobby's room (ShipLobby.cs builds the walls, the couch and the lights):
    /// - the room's own colours: the carpet, the wall panels and the couch take Settings > Display > SHIP LOBBY's colours
    ///   (LobbyLooks.cs; also LOBBY LOOK in the lobby), changed live;
    /// - the telly's kit: two big speakers either side of it, a games console on the floor in front of it with its
    ///   controllers (the telly itself plays the warm-up game: LobbyArcade.cs);
    /// - a bike on its stand (proper wheels: rings and spokes, the frame clear of them);
    /// - two trade stations against the back wall (their screens going), gear from the game all round (a rocket
    ///   launcher, the death wand, a revolver, a spear, a pile of wood, a sword, a shotgun...), dumbbells, a guitar on its
    ///   stand, "RED WAZ HERE" sprayed up on the back wall, more stains, a shelf of bottles, a clock, a ceiling vent.
    /// </summary>
    public partial class ShipLobby
    {
        // ------------------------------------------------------------------ the room's colours

        enum Tones { Floor, Wall, Couch }
        class ToneMat { public Tones T; public float Mul; public Material M; }
        readonly List<ToneMat> m_Tones = new List<ToneMat>();
        bool m_TonesSet;
        Color m_FloorWas, m_WallWas, m_CouchWas;

        static Color ToneColour(Tones t) => t == Tones.Floor ? GameSettings.LobbyFloorNow : t == Tones.Wall ? GameSettings.LobbyWallNow : GameSettings.LobbyCouchNow;

        /// <summary>A material in one of the room's colours (shaded by mul), re-tinted when the colour changes.</summary>
        Material Tone(Tones t, float mul)
        {
            mul = Mathf.Round(mul * 20f) / 20f;
            foreach (var x in m_Tones) if (x.T == t && Mathf.Approximately(x.Mul, mul) && x.M != null) return x.M;
            var c = ToneColour(t) * mul;
            c.a = 1f;
            var m = Art.NewMat(c);
            m.name = "lobby " + t;
            m_Tones.Add(new ToneMat { T = t, Mul = mul, M = m });
            return m;
        }

        /// <summary>Every frame: the room's colours follow the settings.</summary>
        void ApplyTones()
        {
            Color f = GameSettings.LobbyFloorNow, w = GameSettings.LobbyWallNow, c = GameSettings.LobbyCouchNow;
            if (m_TonesSet && f == m_FloorWas && w == m_WallWas && c == m_CouchWas) return;
            m_TonesSet = true;
            m_FloorWas = f; m_WallWas = w; m_CouchWas = c;
            foreach (var x in m_Tones)
            {
                if (x.M == null) continue;
                var col = ToneColour(x.T) * x.Mul;
                col.a = 1f;
                x.M.SetColor("_BaseColor", col);
                x.M.color = col;
            }
        }

        /// <summary>A wall panel box in the walls' colour.</summary>
        GameObject WallBox(Transform t, Vector3 pos, Vector3 scale) => Art.Box(t, Color.white, pos, scale, default, false, Tone(Tones.Wall, 1f));

        // ------------------------------------------------------------------ the telly

        /// <summary>The telly (its root: facing the couch), the middle of its screen in its space, and what a click on it hits.</summary>
        Transform m_Tv;
        static readonly Vector3 k_Screen = new Vector3(-0.06f, 0.84f, 0.27f);
        static readonly Bounds k_TvBounds = new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(1f, 1.15f, 0.9f));

        /// <summary>Big speakers either side of the telly, the console on the floor in front of it, its controllers.</summary>
        void BuildTellyKit(Transform tv)
        {
            var wood = new Color(0.17f, 0.12f, 0.09f);
            var black = new Color(0.05f, 0.05f, 0.055f);
            var grey = new Color(0.22f, 0.22f, 0.24f);
            foreach (float s in new[] { -1f, 1f })
            {
                var sp = new GameObject("speaker").transform;
                sp.SetParent(tv, false);
                sp.localPosition = new Vector3(s * 0.98f, 0f, -0.02f);
                sp.localRotation = Quaternion.Euler(0f, -s * 10f, 0f); // (turned in a little, at the couch)
                Art.Box(sp, wood, new Vector3(0, 0.6f, 0), new Vector3(0.48f, 1.2f, 0.44f));
                Art.Box(sp, black, new Vector3(0, 0.6f, 0.222f), new Vector3(0.43f, 1.13f, 0.01f)); // (the baffle)
                for (int f = -1; f <= 1; f += 2) Art.Box(sp, black, new Vector3(f * 0.18f, 0.015f, f * 0.15f), new Vector3(0.06f, 0.03f, 0.06f)); // (feet)
                // the woofer, the mid, the tweeter: cones in rings, a dust cap on each
                void Driver(float y, float d)
                {
                    Art.Part(sp, Art.Cylinder, grey, new Vector3(0, y, 0.226f), new Vector3(d / CR, 0.012f / CH, d / CR), new Vector3(90, 0, 0));
                    Art.Part(sp, Art.Cylinder, black, new Vector3(0, y, 0.232f), new Vector3(d * 0.84f / CR, 0.008f / CH, d * 0.84f / CR), new Vector3(90, 0, 0));
                    Art.Part(sp, Art.Sphere, grey * 1.3f, new Vector3(0, y, 0.236f), new Vector3(d * 0.3f, d * 0.3f, d * 0.12f) / SR);
                }
                Driver(0.36f, 0.36f);
                Driver(0.74f, 0.2f);
                Driver(0.98f, 0.1f);
                Art.Box(sp, Color.white, new Vector3(0.16f, 1.12f, 0.229f), new Vector3(0.02f, 0.02f, 0.005f), default, false, Workbench.Glow(new Color(0.3f, 0.6f, 1f), 2.5f)); // (its power light)
                if (s < 0)
                {
                    // a helmet from the game left on top of one
                    var helm = ItemModels.Create(Item.Helmet, sp);
                    if (helm != null) { helm.transform.localPosition = new Vector3(0.02f, 1.2f, 0f); helm.transform.localRotation = Quaternion.Euler(0f, 30f, 0f); }
                }
                else
                {
                    // a beer can and an ashtray on the other
                    var can = BeerCan(sp).transform;
                    can.localPosition = new Vector3(-0.1f, 1.29f, 0.05f);
                    Art.Part(sp, Art.Cylinder, new Color(0.25f, 0.25f, 0.28f), new Vector3(0.1f, 1.215f, -0.05f), new Vector3(0.13f / CR, 0.03f / CH, 0.13f / CR));
                }
            }
            // the console on the floor in front of the telly: a slab with a glowing stripe, a disc tray and a power light
            var con = new GameObject("games console").transform;
            con.SetParent(tv, false);
            con.localPosition = new Vector3(0.12f, 0f, 0.66f);
            con.localRotation = Quaternion.Euler(0f, 8f, 0f);
            Art.Box(con, black, new Vector3(0, 0.04f, 0), new Vector3(0.36f, 0.08f, 0.27f));
            Art.Box(con, grey, new Vector3(0, 0.082f, 0), new Vector3(0.34f, 0.006f, 0.25f));
            Art.Box(con, Color.white, new Vector3(0, 0.086f, 0.02f), new Vector3(0.3f, 0.003f, 0.012f), default, false, Workbench.Glow(new Color(0.3f, 1f, 0.5f), 2.2f));
            Art.Box(con, grey * 0.6f, new Vector3(-0.05f, 0.045f, 0.137f), new Vector3(0.16f, 0.012f, 0.004f)); // (the disc slot)
            Art.Box(con, Color.white, new Vector3(0.14f, 0.045f, 0.137f), new Vector3(0.015f, 0.015f, 0.004f), default, false, Workbench.Glow(new Color(0.3f, 1f, 0.5f), 3f));
            // its cables back to the telly, and the two controllers lying on the floor (on cables)
            Art.Box(tv, black, new Vector3(0.1f, 0.006f, 0.42f), new Vector3(0.012f, 0.008f, 0.26f), new Vector3(0, 6f, 0));
            Art.Box(tv, black, new Vector3(0.16f, 0.006f, 0.42f), new Vector3(0.012f, 0.008f, 0.26f), new Vector3(0, -4f, 0));
            var p1 = Controller(tv, new Color(0.12f, 0.12f, 0.14f), true).transform;
            p1.localPosition = new Vector3(0.62f, 0.022f, 1.02f);
            p1.localRotation = Quaternion.Euler(0f, 35f, 0f);
            var p2 = Controller(tv, new Color(0.6f, 0.6f, 0.62f), false).transform;
            p2.localPosition = new Vector3(-0.42f, 0.022f, 0.95f);
            p2.localRotation = Quaternion.Euler(0f, -60f, 4f);
            Art.Box(tv, black, new Vector3(0.38f, 0.005f, 0.86f), new Vector3(0.006f, 0.006f, 0.42f), new Vector3(0, 52f, 0));
            Art.Box(tv, black, new Vector3(-0.15f, 0.005f, 0.82f), new Vector3(0.006f, 0.006f, 0.45f), new Vector3(0, -58f, 0));
        }

        /// <summary>A game controller: a body, two grips, a d-pad, four buttons (lit: a glowing light on it).</summary>
        static GameObject Controller(Transform parent, Color body, bool lit)
        {
            var pad = new GameObject("controller");
            pad.transform.SetParent(parent, false);
            var t = pad.transform;
            Art.Box(t, body, Vector3.zero, new Vector3(0.15f, 0.032f, 0.075f));
            foreach (float s in new[] { -1f, 1f })
                Art.Box(t, body, new Vector3(s * 0.065f, -0.004f, -0.035f), new Vector3(0.05f, 0.03f, 0.07f), new Vector3(0, s * 18f, 0));
            var dark = new Color(0.06f, 0.06f, 0.07f);
            Art.Box(t, dark, new Vector3(-0.045f, 0.018f, 0.005f), new Vector3(0.03f, 0.006f, 0.01f));
            Art.Box(t, dark, new Vector3(-0.045f, 0.018f, 0.005f), new Vector3(0.01f, 0.006f, 0.03f));
            Color[] buttons = { new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.75f, 0.3f), new Color(0.25f, 0.45f, 0.95f), new Color(0.95f, 0.8f, 0.2f) };
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Art.Box(t, buttons[i], new Vector3(0.045f + Mathf.Cos(a) * 0.012f, 0.018f, 0.005f + Mathf.Sin(a) * 0.012f), new Vector3(0.009f, 0.006f, 0.009f));
            }
            if (lit) Art.Box(t, Color.white, new Vector3(0f, 0.017f, 0.03f), new Vector3(0.02f, 0.004f, 0.006f), default, false, Workbench.Glow(new Color(0.3f, 0.6f, 1f), 2.5f));
            return pad;
        }

        // ------------------------------------------------------------------ the bike

        /// <summary>A box from a to b (a tube of the bike's frame).</summary>
        static void Tube(Transform p, Color c, Vector3 a, Vector3 b, float th)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Art.Box(p, c, (a + b) * 0.5f, new Vector3(th, th, d.magnitude), Quaternion.LookRotation(d, Vector3.right).eulerAngles);
        }

        /// <summary>The bike on its stand, back towards the window at the couch's end. The wheels are rings (a tyre made of
        /// short segments, spokes and a hub) and the frame is laid out clear of them: the main triangle stays outside the
        /// wheels and the fork and stays run either side of the tyres to the hubs, so nothing pokes through.</summary>
        void BuildBike(Transform t)
        {
            var bike = new GameObject("bike").transform;
            bike.SetParent(t, false);
            bike.localPosition = new Vector3(-3.45f, 0f, 0.85f);
            bike.localRotation = Quaternion.Euler(0, 64f, -7f);
            var tyre = new Color(0.08f, 0.08f, 0.08f);
            var chrome = new Color(0.7f, 0.7f, 0.72f);
            var fr = new Color(0.75f, 0.15f, 0.15f);
            const float R = 0.33f, Hy = 0.35f;
            Vector3 rear = new Vector3(0, Hy, -0.52f), front = new Vector3(0, Hy, 0.52f);
            foreach (var c in new[] { rear, front })
            {
                const int N = 22;
                float seg = 2f * Mathf.PI * R / N * 1.08f;
                for (int k = 0; k < N; k++)
                {
                    float a = k * Mathf.PI * 2f / N;
                    var rad = new Vector3(0, Mathf.Cos(a), Mathf.Sin(a));
                    var tan = new Vector3(0, -Mathf.Sin(a), Mathf.Cos(a));
                    Art.Box(bike, tyre, c + rad * R, new Vector3(0.04f, 0.035f, seg), Quaternion.LookRotation(tan, rad).eulerAngles);
                    Art.Box(bike, chrome * 0.8f, c + rad * (R - 0.025f), new Vector3(0.025f, 0.012f, seg * 0.92f), Quaternion.LookRotation(tan, rad).eulerAngles); // (the rim)
                }
                for (int k = 0; k < 6; k++)
                    Art.Box(bike, chrome, c, new Vector3(0.004f, 0.004f, 2f * (R - 0.03f)), new Vector3(k * 30f, 0, 0)); // (spokes)
                Art.Part(bike, Art.Cylinder, chrome, c, new Vector3(0.05f / CR, 0.09f / CH, 0.05f / CR), new Vector3(0, 0, 90f)); // (the hub)
            }
            // the frame: the main triangle (clear of the wheels), the fork and the stays either side of the tyres
            Vector3 bb = new Vector3(0, 0.3f, -0.05f), st = new Vector3(0, 0.74f, -0.22f), ht = new Vector3(0, 0.8f, 0.22f), hb = new Vector3(0, 0.62f, 0.26f);
            const float Th = 0.035f;
            Tube(bike, fr, bb, st, Th);
            Tube(bike, fr, st, ht, Th);
            Tube(bike, fr, bb, hb, Th);
            Tube(bike, fr, ht, hb, Th * 1.3f);
            foreach (float x in new[] { -0.04f, 0.04f })
            {
                var o = new Vector3(x, 0, 0);
                Tube(bike, fr, hb + o, front + o, Th * 0.8f);          // (the fork)
                Tube(bike, fr, bb + o, rear + o, Th * 0.7f);           // (chain stays)
                Tube(bike, fr, st + o, rear + o, Th * 0.7f);           // (seat stays)
            }
            Art.Box(bike, fr, hb, new Vector3(0.1f, 0.03f, 0.04f));   // (the fork crown)
            Art.Box(bike, fr, st, new Vector3(0.1f, 0.03f, 0.04f));
            Art.Box(bike, fr, bb, new Vector3(0.1f, 0.05f, 0.05f));
            // the saddle, the bars, the cranks and pedals, the chainring
            Tube(bike, chrome, st, st + new Vector3(0, 0.13f, -0.03f), 0.022f);
            Art.Box(bike, tyre, st + new Vector3(0, 0.15f, -0.05f), new Vector3(0.1f, 0.035f, 0.24f), new Vector3(-4f, 0, 0));
            Tube(bike, chrome, ht, ht + new Vector3(0, 0.12f, -0.02f), 0.022f);
            Art.Box(bike, chrome, ht + new Vector3(0, 0.13f, -0.02f), new Vector3(0.48f, 0.024f, 0.024f));
            foreach (float s in new[] { -1f, 1f }) Art.Box(bike, tyre, ht + new Vector3(s * 0.22f, 0.13f, -0.02f), new Vector3(0.08f, 0.032f, 0.032f));
            Art.Part(bike, Art.Cylinder, chrome * 0.9f, bb + new Vector3(0.055f, 0, 0), new Vector3(0.17f / CR, 0.008f / CH, 0.17f / CR), new Vector3(0, 0, 90f));
            foreach (float s in new[] { -1f, 1f })
            {
                var crankEnd = bb + new Vector3(s * 0.075f, s * 0.12f, s * 0.06f);
                Tube(bike, chrome, bb + new Vector3(s * 0.075f, 0, 0), crankEnd, 0.018f);
                Art.Box(bike, tyre, crankEnd + new Vector3(s * 0.04f, 0, 0), new Vector3(0.08f, 0.02f, 0.06f));
            }
            // the kickstand, down to the floor on the side it leans to
            Tube(bike, chrome * 0.8f, bb + new Vector3(0.06f, -0.02f, -0.1f), new Vector3(0.19f, 0.01f, -0.2f), 0.018f);
        }

        // ------------------------------------------------------------------ the rest of the room

        void Lay(Transform t, Item it, Vector3 at, float yaw, float roll)
        {
            var go = ItemModels.Create(it, t);
            if (go == null) return;
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(0, 0, roll);
        }

        void BuildDressing(Transform t, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            // ---- two trade stations against the back wall either side of the window, their screens going ----
            void Station(Vector3 at, int team, int tier)
            {
                var root = new GameObject("trade station").transform;
                root.SetParent(t, false);
                root.localPosition = at;
                root.localRotation = Quaternion.Euler(0f, 180f, 0f); // (facing the room)
                Workbench.BuildModel(root, team, false, tier);
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name == Workbench.FaceScreen) BenchScreen.Attach(tr.gameObject, BenchScreen.Kind.Face);
                    else if (tr.name == Workbench.StockScreen) BenchScreen.Attach(tr.gameObject, BenchScreen.Kind.Stocks);
                }
            }
            Station(new Vector3(-4.8f, 0f, 3.62f), 0, 2);
            Station(new Vector3(4.8f, 0f, 3.62f), 1, 1);

            // ---- gear from the game, all round where you can see it ----
            Lay(t, Item.RocketLauncher, new Vector3(1.75f, 0.09f, -1.15f), 75f, 90f);
            Lay(t, Item.DeathWand, new Vector3(-1.25f, 0.03f, -1.55f), -30f, 90f);
            Lay(t, Item.Revolver, new Vector3(0.65f, 0.03f, -0.5f), 60f, 90f);
            Lay(t, Item.Pistol, new Vector3(-2.1f, 0.03f, -0.25f), 140f, 90f);
            Lay(t, Item.Spear, new Vector3(-0.3f, 0.04f, -2.25f), 80f, 90f);
            Lay(t, Item.Sword, new Vector3(-2.85f, 0.03f, -1.65f), 20f, 90f);
            Lay(t, Item.Shotgun, new Vector3(5.6f, 1.03f, -0.6f), 10f, 90f);   // (on top of the fridge)
            Lay(t, Item.Crossbow, new Vector3(3.05f, 0.05f, 0.55f), -20f, 90f);
            Lay(t, Item.Chainsaw, new Vector3(-3.95f, 0.02f, -0.45f), 40f, 90f);
            Lay(t, Item.Sniper, new Vector3(4.5f, 0.04f, -0.95f), 10f, 90f);
            Lay(t, Item.Meat, new Vector3(4.42f, 0.36f, 2.6f), 30f, 0f);       // (on the pizza boxes)
            Lay(t, Item.Berry, new Vector3(0.95f, 0.03f, -0.15f), 0f, 0f);
            Lay(t, Item.Hatchet, new Vector3(-4.3f, 0.05f, 3.0f), 120f, 90f);
            // a pile of wood
            for (int i = 0; i < 7; i++)
            {
                int layer = i < 4 ? 0 : i < 6 ? 1 : 2;
                float x = layer == 0 ? -0.24f + (i % 4) * 0.16f : layer == 1 ? -0.16f + (i - 4) * 0.16f + 0.08f : 0f;
                Lay(t, Item.Wood, new Vector3(2.55f + x, 0.06f + layer * 0.11f, -1.75f), 92f + R() * 6f, 90f);
            }
            // dumbbells
            void Dumbbell(Vector3 at, float yaw)
            {
                var d = new GameObject("dumbbell").transform;
                d.SetParent(t, false);
                d.localPosition = at;
                d.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Art.Part(d, Art.Cylinder, new Color(0.6f, 0.6f, 0.62f), new Vector3(0, 0.1f, 0), new Vector3(0.03f / CR, 0.36f / CH, 0.03f / CR), new Vector3(0, 0, 90f));
                foreach (float s in new[] { -1f, 1f })
                    foreach (float o in new[] { 0.11f, 0.155f })
                        Art.Part(d, Art.Cylinder, new Color(0.08f, 0.08f, 0.09f), new Vector3(s * o, 0.1f, 0), new Vector3(0.2f / CR, 0.04f / CH, 0.2f / CR), new Vector3(0, 0, 90f));
            }
            Dumbbell(new Vector3(2.05f, 0f, -0.6f), 30f);
            Dumbbell(new Vector3(2.35f, 0f, -0.3f), 75f);
            BuildGuitar(t, new Vector3(3.95f, 0f, 2.95f));
            Graffiti(t, "RED WAZ HERE", new Vector3(0.3f, 3.38f, WinZ - 0.156f), 0.07f);

            // ---- more stains: on the back wall under the window, a damp patch on the ceiling, a puddle by the fridge ----
            for (int i = 0; i < 6; i++)
                Art.Box(t, Color.white, new Vector3(R() * 7f - 3.5f, 0.25f + R() * 0.7f, WinZ - 0.153f), new Vector3(0.2f + R() * 0.5f, 0.15f + R() * 0.4f, 0.004f), new Vector3(0, 0, R() * 40f - 20f), false, Tone(Tones.Wall, 0.6f + R() * 0.15f));
            Art.Part(t, Art.Cylinder, new Color(0.24f, 0.2f, 0.13f), new Vector3(2.2f, RoomH - 0.003f, 0.6f), new Vector3(1.3f / CR, 0.003f / CH, 0.9f / CR));
            Art.Part(t, Art.Cylinder, new Color(0.17f, 0.14f, 0.09f), new Vector3(2.3f, RoomH - 0.006f, 0.65f), new Vector3(0.7f / CR, 0.003f / CH, 0.5f / CR));
            var puddle = Art.NewMat(new Color(0.06f, 0.07f, 0.06f));
            if (puddle.HasProperty("_Smoothness")) puddle.SetFloat("_Smoothness", 0.85f);
            Art.Part(t, Art.Cylinder, Color.white, new Vector3(4.95f, 0.011f, -0.55f), new Vector3(0.7f / CR, 0.002f, 0.45f / CR), new Vector3(0, 20f, 0), false, puddle);

            // ---- the left wall: a shelf of bottles and a plant, a clock ----
            {
                var wall = new GameObject("left wall bits").transform;
                wall.SetParent(t, false);
                wall.localPosition = new Vector3(-6.6f, 0f, 0f);
                wall.localRotation = Quaternion.Euler(0, 0, -8f); // (the same lean as the wall)
                var wood = new Color(0.3f, 0.2f, 0.12f);
                Art.Box(wall, wood, new Vector3(0.32f, 1.45f, -1.9f), new Vector3(0.3f, 0.03f, 1.1f));
                foreach (float z in new[] { -2.35f, -1.45f }) Art.Box(wall, wood * 0.7f, new Vector3(0.2f, 1.38f, z), new Vector3(0.04f, 0.12f, 0.04f));
                for (int i = 0; i < 5; i++)
                {
                    var bc = Color.HSVToRGB(0.25f + R() * 0.15f, 0.6f, 0.35f + R() * 0.2f);
                    float h = 0.18f + R() * 0.12f, z = -2.35f + i * 0.17f;
                    Art.Part(wall, Art.Cylinder, bc, new Vector3(0.33f, 1.465f + h * 0.5f, z), new Vector3(0.065f / CR, h / CH, 0.065f / CR));
                    Art.Part(wall, Art.Cylinder, bc, new Vector3(0.33f, 1.465f + h + 0.04f, z), new Vector3(0.025f / CR, 0.08f / CH, 0.025f / CR));
                }
                Art.Part(wall, Art.Cylinder, new Color(0.55f, 0.3f, 0.2f), new Vector3(0.33f, 1.53f, -1.5f), new Vector3(0.13f / CR, 0.13f / CH, 0.13f / CR));
                for (int i = 0; i < 5; i++)
                    Art.Part(wall, Art.Ico, new Color(0.2f, 0.42f + R() * 0.15f, 0.18f), new Vector3(0.33f + R() * 0.1f - 0.05f, 1.65f + R() * 0.12f, -1.5f + R() * 0.12f - 0.06f), Vector3.one * (0.07f + R() * 0.05f), new Vector3(R() * 360f, R() * 360f, 0));
                // the clock (stopped)
                Art.Part(wall, Art.Cylinder, new Color(0.15f, 0.15f, 0.16f), new Vector3(0.2f, 2.45f, 0.3f), new Vector3(0.36f / CR, 0.04f / CH, 0.36f / CR), new Vector3(0, 0, 90f));
                Art.Part(wall, Art.Cylinder, new Color(0.85f, 0.83f, 0.78f), new Vector3(0.225f, 2.45f, 0.3f), new Vector3(0.31f / CR, 0.01f / CH, 0.31f / CR), new Vector3(0, 0, 90f));
                Art.Box(wall, Color.black, new Vector3(0.235f, 2.49f, 0.31f), new Vector3(0.005f, 0.1f, 0.012f), new Vector3(20f, 0, 0));
                Art.Box(wall, Color.black, new Vector3(0.235f, 2.43f, 0.35f), new Vector3(0.005f, 0.07f, 0.012f), new Vector3(-60f, 0, 0));
            }
            // ---- a ceiling vent, and cables taped along the floor to the telly ----
            Art.Box(t, new Color(0.12f, 0.12f, 0.13f), new Vector3(-1.4f, RoomH - 0.01f, -0.2f), new Vector3(0.9f, 0.02f, 0.55f));
            for (int i = 0; i < 6; i++) Art.Box(t, new Color(0.35f, 0.36f, 0.38f), new Vector3(-1.4f, RoomH - 0.025f, -0.42f + i * 0.088f), new Vector3(0.86f, 0.012f, 0.02f), new Vector3(30f, 0, 0));
            Art.Box(t, new Color(0.05f, 0.05f, 0.05f), new Vector3(-4.6f, 0.006f, -1.05f), new Vector3(3.6f, 0.008f, 0.014f), new Vector3(0, 4f, 0));
            for (int i = 0; i < 4; i++) Art.Box(t, new Color(0.55f, 0.5f, 0.35f), new Vector3(-5.9f + i * 0.9f, 0.008f, -1.08f + i * 0.06f), new Vector3(0.06f, 0.004f, 0.1f)); // (tape)
        }

        /// <summary>An acoustic guitar standing on its little A-frame stand, facing the room.</summary>
        static void BuildGuitar(Transform t, Vector3 at)
        {
            var g = new GameObject("guitar").transform;
            g.SetParent(t, false);
            g.localPosition = at;
            g.localRotation = Quaternion.Euler(-8f, 165f, 0f); // (leaning back on the stand, turned a bit towards the couch)
            var body = new Color(0.75f, 0.38f, 0.12f);
            var edge = new Color(0.25f, 0.12f, 0.05f);
            var neck = new Color(0.32f, 0.2f, 0.1f);
            // (the guitar's front faces its +z)
            Art.Part(g, Art.Sphere, edge, new Vector3(0, 0.3f, 0), new Vector3(0.42f, 0.38f, 0.1f) / SR);
            Art.Part(g, Art.Sphere, edge, new Vector3(0, 0.58f, 0), new Vector3(0.32f, 0.3f, 0.1f) / SR);
            Art.Part(g, Art.Sphere, body, new Vector3(0, 0.3f, 0.006f), new Vector3(0.39f, 0.35f, 0.1f) / SR);
            Art.Part(g, Art.Sphere, body, new Vector3(0, 0.58f, 0.006f), new Vector3(0.29f, 0.27f, 0.1f) / SR);
            Art.Part(g, Art.Cylinder, new Color(0.05f, 0.04f, 0.03f), new Vector3(0, 0.5f, 0.056f), new Vector3(0.1f / CR, 0.004f / CH, 0.1f / CR), new Vector3(90, 0, 0)); // (the sound hole)
            Art.Box(g, edge, new Vector3(0, 0.26f, 0.055f), new Vector3(0.12f, 0.02f, 0.012f)); // (the bridge)
            Art.Box(g, neck, new Vector3(0, 0.98f, 0.02f), new Vector3(0.055f, 0.56f, 0.03f));
            Art.Box(g, edge, new Vector3(0, 1.31f, 0.015f), new Vector3(0.075f, 0.13f, 0.025f), new Vector3(-6f, 0, 0)); // (the headstock)
            for (int i = 0; i < 6; i++)
            {
                float x = -0.02f + i * 0.008f;
                Art.Box(g, new Color(0.85f, 0.85f, 0.8f), new Vector3(x, 0.78f, 0.04f), new Vector3(0.0015f, 1.05f, 0.0015f));
                Art.Part(g, Art.Cylinder, new Color(0.8f, 0.8f, 0.8f), new Vector3(i < 3 ? -0.045f : 0.045f, 1.27f + (i % 3) * 0.035f, 0.015f), new Vector3(0.012f / CR, 0.02f / CH, 0.012f / CR), new Vector3(0, 0, 90f)); // (tuning pegs)
            }
            // the stand: two legs behind, a cradle under the body, a fork for the neck
            var stand = new Color(0.08f, 0.08f, 0.08f);
            foreach (float s in new[] { -1f, 1f })
            {
                Tube(g, stand, new Vector3(s * 0.12f, 0.02f, 0.08f), new Vector3(s * 0.12f, 0.12f, 0.02f), 0.02f);
                Tube(g, stand, new Vector3(s * 0.12f, 0.02f, -0.25f), new Vector3(0, 0.9f, -0.06f), 0.02f);
            }
            Art.Box(g, stand, new Vector3(0, 0.12f, 0.03f), new Vector3(0.3f, 0.02f, 0.05f));
        }

        /// <summary>Words sprayed on a wall in red (the 3x5 pixel font, LobbyArcade.Glyph): each "pixel" a slightly
        /// wonky square of paint, with runs dripping off the bottoms of the letters. `top` is the middle of the top edge
        /// (room space), on the back wall's face.</summary>
        void Graffiti(Transform t, string text, Vector3 top, float cell)
        {
            var rng = new System.Random(5);
            float R() => (float)rng.NextDouble();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Quad(float x0, float y0, float x1, float y1)
            {
                // facing -z (the room): bottom left, top left, top right, bottom right
                int b = verts.Count;
                verts.Add(new Vector3(x0, y0, 0)); verts.Add(new Vector3(x0, y1, 0)); verts.Add(new Vector3(x1, y1, 0)); verts.Add(new Vector3(x1, y0, 0));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            float width = (text.Length * 4 - 1) * cell;
            float x = -width * 0.5f;
            foreach (char ch in text)
            {
                var gl = LobbyArcade.Glyph(ch);
                float lift = (R() - 0.5f) * cell * 0.4f; // (each letter a bit up or down: done by hand)
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 3; c++)
                    {
                        if ((gl[r] & (4 >> c)) == 0) continue;
                        float cx = x + c * cell, cy = -r * cell + lift;
                        float j = cell * 0.08f;
                        Quad(cx - j * R(), cy - cell - j * R(), cx + cell + j * R(), cy + j * R());
                        // runs dripping off the bottom of the strokes
                        bool bottom = r == 4 || (gl[r + 1] & (4 >> c)) == 0;
                        if (bottom && R() < 0.4f)
                        {
                            float dx = cx + cell * (0.25f + R() * 0.5f), len = cell * (0.6f + R() * 2.2f);
                            Quad(dx - cell * 0.12f, cy - cell - len, dx + cell * 0.12f, cy - cell + 0.001f);
                        }
                    }
                x += 4f * cell;
            }
            var mesh = new Mesh { name = "graffiti" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("graffiti");
            go.transform.SetParent(t, false);
            go.transform.localPosition = top;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Unlit(new Color(0.85f, 0.07f, 0.05f), 0.75f); // (spray paint, just catching what light there is)
            m_Graffiti = mesh;
        }

        Mesh m_Graffiti;
    }
}
