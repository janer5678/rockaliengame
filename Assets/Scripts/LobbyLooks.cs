using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display, two more sections:
    /// TIMER - the top-centre timer panel (Hud.Notify.cs: DrawTopPanel): its size, how dark its plate is, and whether it
    ///   shows the drain bar, the line about what to do, the mode's tag and the final-minute flashing.
    /// SHIP LOBBY - the living room you wait in (ShipLobby.cs): post processing of its own (like the main menu
    ///   cutscene's) and the room itself - the floor, wall, couch and stain colours, how dark it is, how bright the lamp, the
    ///   telly, the lights passing the window and the glowing ball are, the lamp swinging, the cigarette smoke and the camera's field of
    ///   view. The same rows are in the lobby itself: Tab opens LOBBY LOOK (Hud.Lobby.cs).
    /// </summary>
    public static partial class GameSettings
    {
        // ---- the ship lobby ----
        const string GLobby = "SHIP LOBBY";
        /// <summary>The ship lobby uses the post processing and room settings below (off: the world's post processing and the room as built).</summary>
        public static readonly DisplayPref.Bool LobbyOwn = new("lobby.own", GLobby, true);
        public static readonly DisplayPref.Bool LobbyBloom = new("lobby.bloom", GLobby, true);
        public static readonly DisplayPref.Float LobbyBloomStrength = new("lobby.bloom.strength", GLobby, 1f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyVignette = new("lobby.vignette", GLobby, true);
        public static readonly DisplayPref.Float LobbyVignetteStrength = new("lobby.vignette.strength", GLobby, 0.5f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyGrading = new("lobby.grading", GLobby, true);
        public static readonly DisplayPref.Float LobbyGradingStrength = new("lobby.grading.strength", GLobby, 1f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyOutlines = new("lobby.outlines", GLobby, true);
        public static readonly DisplayPref.Float LobbyOutlinesStrength = new("lobby.outlines.strength", GLobby, 0.5f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyCel = new("lobby.cel", GLobby, true);
        public static readonly DisplayPref.Float LobbyCelStrength = new("lobby.cel.strength", GLobby, 0.5f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyGrain = new("lobby.grain", GLobby, false);
        public static readonly DisplayPref.Float LobbyGrainStrength = new("lobby.grain.strength", GLobby, 0f, 0f, 1f);
        public static readonly DisplayPref.Bool LobbyChromatic = new("lobby.chromatic", GLobby, true);
        public static readonly DisplayPref.Float LobbyChromaticStrength = new("lobby.chromatic.strength", GLobby, 0.45f, 0f, 1f);
        // the room
        public static readonly DisplayPref.Float LobbyDark = new("lobby.dark", GLobby, 0.3f, 0.3f, 1.6f);
        public static readonly DisplayPref.Float LobbyLamp = new("lobby.lamp", GLobby, 0.4f, 0f, 2f);
        public static readonly DisplayPref.Float LobbyTv = new("lobby.tv", GLobby, 2f, 0f, 2f);
        public static readonly DisplayPref.Float LobbyWindow = new("lobby.window", GLobby, 1f, 0f, 2f);
        /// <summary>The glowing ball lying on the floor: its glow and the light it throws (1 = as built, already toned down).</summary>
        public static readonly DisplayPref.Float LobbyBall = new("lobby.ball", GLobby, 1f, 0f, 2f);
        public static readonly DisplayPref.Bool LobbySway = new("lobby.sway", GLobby, true);
        public static readonly DisplayPref.Bool LobbySmoke = new("lobby.smoke", GLobby, true);
        public static readonly DisplayPref.Float LobbyFov = new("lobby.fov", GLobby, 61f, 45f, 85f);
        // the room's colours (the carpet, the wall panels, the couch, the stains)
        public static readonly DisplayPref.Colour LobbyFloor = new("lobby.floor", GLobby, DisplayDefaults.Hex("#4C5226"));
        public static readonly DisplayPref.Colour LobbyWall = new("lobby.wall", GLobby, new Color(0.3f, 0.28f, 0.24f));
        public static readonly DisplayPref.Colour LobbyCouch = new("lobby.couch", GLobby, DisplayDefaults.Hex("#99521F"));
        /// <summary>The stains all over the room (the carpet, the walls, the couch, the ceiling, the coffee table).</summary>
        public static readonly DisplayPref.Colour LobbyStain = new("lobby.stain", GLobby, new Color(0.2f, 0.165f, 0.105f));

        /// <summary>The ship lobby's own post processing is in use right now (switched on, and it's showing).</summary>
        public static bool LobbyPostNow => LobbyOwn.Value && ShipLobby.Active && !MenuScene.LobbyPreview;
        /// <summary>The lobby's room settings in use (1 = as built).</summary>
        public static float LobbyDarkNow => LobbyOwn.Value ? LobbyDark.Value : 1f;
        public static float LobbyLampNow => LobbyOwn.Value ? LobbyLamp.Value : 1f;
        public static float LobbyTvNow => LobbyOwn.Value ? LobbyTv.Value : 1f;
        public static float LobbyWindowNow => LobbyOwn.Value ? LobbyWindow.Value : 1f;
        public static float LobbyBallNow => LobbyOwn.Value ? LobbyBall.Value : 1f;
        public static bool LobbySwayNow => !LobbyOwn.Value || LobbySway.Value;
        public static bool LobbySmokeNow => !LobbyOwn.Value || LobbySmoke.Value;
        public static float LobbyFovNow => LobbyOwn.Value ? LobbyFov.Value : 60f;
        public static Color LobbyFloorNow => LobbyOwn.Value ? LobbyFloor.Value : FloorAsBuilt;
        public static Color LobbyWallNow => LobbyOwn.Value ? LobbyWall.Value : LobbyWall.Default;
        public static Color LobbyCouchNow => LobbyOwn.Value ? LobbyCouch.Value : CouchAsBuilt;
        public static Color LobbyStainNow => LobbyOwn.Value ? LobbyStain.Value : LobbyStain.Default;
        /// <summary>The floor and couch as built (their settings start at the 2026-10-08 defaults instead).</summary>
        static readonly Color FloorAsBuilt = new Color(0.22f, 0.2f, 0.15f), CouchAsBuilt = new Color(0.52f, 0.4f, 0.2f);

        static float LobbyExtra(PostExtra e)
        {
            switch (e)
            {
                case PostExtra.Outlines: return Amt(LobbyOutlines, LobbyOutlinesStrength);
                case PostExtra.CelBanding: return Amt(LobbyCel, LobbyCelStrength);
                case PostExtra.FilmGrain: return Amt(LobbyGrain, LobbyGrainStrength);
                case PostExtra.Chromatic: return Amt(LobbyChromatic, LobbyChromaticStrength);
            }
            return 0f;
        }

        public static void ResetLobby(bool save = true)
        {
            DisplayPref.ResetAll(save, LobbyOwn, LobbyBloom, LobbyBloomStrength, LobbyVignette, LobbyVignetteStrength, LobbyGrading, LobbyGradingStrength,
                LobbyOutlines, LobbyOutlinesStrength, LobbyCel, LobbyCelStrength, LobbyGrain, LobbyGrainStrength, LobbyChromatic, LobbyChromaticStrength,
                LobbyDark, LobbyLamp, LobbyTv, LobbyWindow, LobbyBall, LobbySway, LobbySmoke, LobbyFov,
                LobbyFloor, LobbyWall, LobbyCouch, LobbyStain);
        }
    }

    public partial class Hud
    {
        bool m_TimerOpen, m_LobbyLooksOpen; // (m_TimerOpen: TimerLooks.cs)

        /// <summary>Settings > Display: the TIMER and SHIP LOBBY sections (after the other own-look sections).</summary>
        void DrawTimerAndLobbyLooks()
        {
            float k = m_Scale;
            DrawTimerLooks(); // (TimerLooks.cs)
            // ---- the ship lobby ----
            if (FoldRow("SHIP LOBBY", ref m_LobbyLooksOpen, GameSettings.LobbyOwn))
            {
                DrawLobbyLookRows();
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#bbbbbb>The living room you wait in before a match: its own post processing (the game keeps the settings above), the room's lights and colours. Also in the lobby: press Tab.</color>", m_SmallWrap);
                if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(28 * k))) GameSettings.ResetLobby();
                GUILayout.EndHorizontal();
            }
        }

        static readonly Color[] s_LobbySwatches =
        {
            new Color(0.15f, 0.15f, 0.17f), new Color(0.12f, 0.16f, 0.3f), new Color(0.35f, 0.1f, 0.12f), new Color(0.3f, 0.32f, 0.15f),
            new Color(0.1f, 0.3f, 0.3f), new Color(0.28f, 0.15f, 0.35f), new Color(0.6f, 0.55f, 0.45f), new Color(0.6f, 0.32f, 0.12f),
        };
        readonly System.Collections.Generic.Dictionary<string, string> m_LobbyHex = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>The SHIP LOBBY settings, one row each (Settings > Display, and LOBBY LOOK in the lobby: Hud.Lobby.cs).</summary>
        void DrawLobbyLookRows()
        {
            float k = m_Scale, lw = 210 * k;
            void Slider(string name, DisplayPref.Float p, string shown, float step)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                float v = SliderRow(name, p.Value, p.Min, p.Max, shown, lw - 16 * k);
                p.Set(Mathf.Round(v / step) * step);
                GUILayout.EndHorizontal();
            }
            void Toggle(string name, DisplayPref.Bool p)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                GUILayout.Label(name, m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
                p.Set(ToggleBtn(p.Value, p.Value ? "On" : "Off", GUILayout.Width(80 * k), GUILayout.Height(26 * k)));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            void Row(string name, DisplayPref.Bool on, DisplayPref.Float s)
            {
                float v = s.Value;
                bool o = EffectRow(name, on.Value, ref v);
                on.Set(o); s.Set(Mathf.Round(v * 20f) / 20f);
            }
            void Colour(string name, DisplayPref.Colour p)
            {
                // the swatches (the room as built first) and a hex box
                GUILayout.BeginHorizontal();
                GUILayout.Space(16 * k);
                GUILayout.Label(name, m_Small, GUILayout.Width(lw - 16 * k), GUILayout.Height(26 * k));
                var cur = p.Value;
                float sw = 22 * k;
                for (int i = -1; i < s_LobbySwatches.Length; i++)
                {
                    var pc = i < 0 ? p.Default : s_LobbySwatches[i];
                    var pr = GUILayoutUtility.GetRect(sw, sw, GUILayout.Width(sw), GUILayout.Height(26 * k));
                    pr.y += (26 * k - sw) * 0.5f; pr.height = sw;
                    Fill(pr, ColorSlots.Same(pc, cur) ? new Color(1f, 0.82f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.8f));
                    Fill(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), pc);
                    TrackHover(pr);
                    bool wheel = ColourWheelOnDoubleClick(pr, p, name); // (double-click: the colour wheel - Hud.ColourWheel.cs)
                    if (GUI.Button(pr, GUIContent.none, GUIStyle.none) && !wheel) { ClickSound(); p.Set(pc); m_LobbyHex.Remove(p.Key); }
                    GUILayout.Space(3 * k);
                }
                GUILayout.Space(6 * k);
                string ctl = "lobbyhex." + p.Key;
                if (GUI.GetNameOfFocusedControl() != ctl) m_LobbyHex.Remove(p.Key);
                string hex = m_LobbyHex.TryGetValue(p.Key, out var typed) ? typed : ColorUtility.ToHtmlStringRGB(cur);
                GUI.SetNextControlName(ctl);
                string nh = GUILayout.TextField(hex, 7, new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.MiddleCenter }, GUILayout.Width(86 * k), GUILayout.Height(24 * k));
                NoteTyping(ctl);
                if (nh != hex)
                {
                    m_LobbyHex[p.Key] = nh;
                    var t = nh.Trim().TrimStart('#');
                    if (t.Length == 6 && ColorUtility.TryParseHtmlString("#" + t, out var hc)) p.Set(hc);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            Colour("Floor", GameSettings.LobbyFloor);
            Colour("Walls", GameSettings.LobbyWall);
            Colour("Couch", GameSettings.LobbyCouch);
            Colour("Stains", GameSettings.LobbyStain);
            Slider("Room darkness", GameSettings.LobbyDark, $"{GameSettings.LobbyDark.Value * 100f:0}%", 0.05f);
            Slider("Lamp", GameSettings.LobbyLamp, $"{GameSettings.LobbyLamp.Value * 100f:0}%", 0.05f);
            Slider("Telly", GameSettings.LobbyTv, $"{GameSettings.LobbyTv.Value * 100f:0}%", 0.05f);
            Slider("Window lights", GameSettings.LobbyWindow, $"{GameSettings.LobbyWindow.Value * 100f:0}%", 0.05f);
            Slider("Glowing ball", GameSettings.LobbyBall, $"{GameSettings.LobbyBall.Value * 100f:0}%", 0.05f);
            Slider("Field of view", GameSettings.LobbyFov, $"{GameSettings.LobbyFov.Value:0}°", 1f);
            Toggle("Lamp swinging", GameSettings.LobbySway);
            Toggle("Cigarette smoke", GameSettings.LobbySmoke);
            Row("Bloom", GameSettings.LobbyBloom, GameSettings.LobbyBloomStrength);
            Row("Vignette", GameSettings.LobbyVignette, GameSettings.LobbyVignetteStrength);
            Row("Colour grading", GameSettings.LobbyGrading, GameSettings.LobbyGradingStrength);
            Row("Outlines", GameSettings.LobbyOutlines, GameSettings.LobbyOutlinesStrength);
            Row("Cel banding", GameSettings.LobbyCel, GameSettings.LobbyCelStrength);
            Row("Film grain", GameSettings.LobbyGrain, GameSettings.LobbyGrainStrength);
            Row("Chromatic aberration", GameSettings.LobbyChromatic, GameSettings.LobbyChromaticStrength);
        }
    }
}
