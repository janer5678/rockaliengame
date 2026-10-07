using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > WORLD COLOURS: every colour of the world's models as a named slot (a default, the colour
    /// picked, a few ready-made ones), saved in PlayerPrefs ("RockGame.World.&lt;Id&gt;" as hex) and applied live.
    ///
    /// How a slot reaches the models: most of them are built from plain colour materials (Art.Mat, cached by colour).
    /// A model's builder wraps itself in <c>using (ColorSlots.Use(ColorSlots.Workbench))</c>; every Art.Mat call inside
    /// gets a material of that slot's own (cached by slot + colour), coloured `colour x (picked / default)` per channel.
    /// When the slot changes, only those materials are re-coloured (no rebuilding, nothing per frame), so all the
    /// model's parts shift together and the slot's main colour lands exactly on the colour picked. Other looks (the
    /// shader-drawn grass and flowers, the pine needles, clouds, sky, sun, ground, the hands) read their slot directly.
    /// The tint only applies in Normal graphics; PSX and AI PSX keep their looks (the materials still count as their
    /// original colour for them).
    /// </summary>
    public static class ColorSlots
    {
        public sealed class Slot
        {
            public int Index;
            public string Id, Label, Group;
            /// <summary>What it starts at on a PC that never changed it (and what Default / Reset go back to).</summary>
            public Color Default;
            /// <summary>The colour the models are built in: the tint is picked / Base, per channel. The same as Default
            /// unless the default was moved off it later (StartAt), so moving a default never changes how a colour looks.</summary>
            public Color Base;
            public Color[] Presets;
            internal Color m_Value;
            internal readonly List<(Material m, Color c)> Mats = new List<(Material, Color)>();
            public Color Value { get { Load(); return m_Value; } }
            /// <summary>Picked something other than the default (hands: a colour of their own instead of the team's).</summary>
            public bool Changed => this == Hands ? !HandsTeam : !Same(Value, Default);
            public string Hex => "#" + ColorUtility.ToHtmlStringRGB(Value);
            /// <summary>A new default colour (the models stay built in Base: the slot just starts tinted to this).</summary>
            internal Slot StartAt(string hex)
            {
                if (ColorUtility.TryParseHtmlString(hex, out var c)) { c.a = 1f; Default = c; m_Value = c; }
                return this;
            }
        }

        public static readonly List<Slot> All = new List<Slot>();

        public const string GSky = "SKY", GGround = "GROUND AND MAP", GPlants = "PLANTS", GBases = "BASES",
            GBuild = "BUILDING PIECES", GThings = "ANIMALS AND THINGS", GYou = "YOU";
        /// <summary>The order the settings list them in.</summary>
        public static readonly string[] Groups = { GSky, GGround, GPlants, GBases, GBuild, GThings, GYou };

        static Slot Add(string id, string label, string group, Color def, params Color[] presets)
        {
            var s = new Slot { Index = All.Count, Id = id, Label = label, Group = group, Default = def, Base = def, m_Value = def };
            s.Presets = presets.Length > 0 ? presets : AutoPresets(def);
            All.Add(s);
            return s;
        }

        /// <summary>Ready-made colours for a slot that has none of its own: the default, darker, lighter, two hue shifts, greyer.</summary>
        static Color[] AutoPresets(Color d)
        {
            Color.RGBToHSV(d, out float h, out float s, out float v);
            return new[]
            {
                d,
                Color.HSVToRGB(h, s, v * 0.7f),
                Color.Lerp(d, Color.white, 0.35f),
                Color.HSVToRGB(Mathf.Repeat(h + 0.08f, 1f), Mathf.Max(s, 0.25f), v),
                Color.HSVToRGB(Mathf.Repeat(h - 0.08f, 1f), Mathf.Max(s, 0.25f), v),
                Color.HSVToRGB(Mathf.Repeat(h + 0.5f, 1f), Mathf.Max(s, 0.35f), v),
                Color.HSVToRGB(h, s * 0.35f, v),
            };
        }

        // ---- the slots (the first six are the old GameSettings.WorldColor ones, same order and saved keys) ----
        // (.StartAt: the colour a fresh install starts at, when it's not the one the models are built in - the game's
        // tuned look, a COPY SETTINGS export of 2026-10-06; the first colour of each Add is the old default / the Base)
        public static readonly Slot
            Ground = Add("Ground", "Ground", GGround, new Color(0.44f, 0.64f, 0.31f),
                new Color(0.44f, 0.64f, 0.31f), new Color(0.34f, 0.52f, 0.25f), new Color(0.55f, 0.62f, 0.3f), new Color(0.66f, 0.6f, 0.36f), new Color(0.5f, 0.38f, 0.25f), new Color(0.88f, 0.9f, 0.93f), new Color(0.3f, 0.55f, 0.45f)).StartAt("#72A326"),
            Rock = Add("Rock", "Highlands rock", GGround, new Color(0.58f, 0.56f, 0.52f),
                new Color(0.58f, 0.56f, 0.52f), new Color(0.45f, 0.43f, 0.4f), new Color(0.7f, 0.68f, 0.64f), new Color(0.62f, 0.5f, 0.38f), new Color(0.5f, 0.52f, 0.6f), new Color(0.35f, 0.33f, 0.36f), new Color(0.75f, 0.62f, 0.5f)),
            Grass = Add("Grass", "Grass", GPlants, new Color(0.36f, 0.6f, 0.16f),
                new Color(0.36f, 0.6f, 0.16f), new Color(0.25f, 0.5f, 0.15f), new Color(0.48f, 0.66f, 0.18f), new Color(0.66f, 0.62f, 0.25f), new Color(0.2f, 0.45f, 0.3f), new Color(0.75f, 0.45f, 0.2f), new Color(0.45f, 0.35f, 0.65f)).StartAt("#6CB430"),
            Leaves = Add("Leaves", "Pine needles", GPlants, new Color(0.29f, 0.55f, 0.15f),
                new Color(0.29f, 0.55f, 0.15f), new Color(0.16f, 0.36f, 0.14f), new Color(0.36f, 0.58f, 0.18f), new Color(0.2f, 0.42f, 0.32f), new Color(0.7f, 0.42f, 0.15f), new Color(0.72f, 0.25f, 0.18f), new Color(0.85f, 0.9f, 0.92f)).StartAt("#86BD15"),
            Clouds = Add("Clouds", "Clouds", GSky, new Color(0.97f, 0.97f, 1f),
                new Color(0.97f, 0.97f, 1f), new Color(1f, 0.93f, 0.85f), new Color(1f, 0.8f, 0.85f), new Color(0.8f, 0.83f, 0.9f), new Color(0.55f, 0.57f, 0.62f), new Color(1f, 0.75f, 0.5f), new Color(0.8f, 0.7f, 1f)),
            Sky = Add("Sky", "Sky", GSky, new Color(0.45f, 0.65f, 0.95f),
                new Color(0.45f, 0.65f, 0.95f), new Color(0.3f, 0.5f, 0.95f), new Color(0.6f, 0.75f, 0.95f), new Color(0.95f, 0.6f, 0.45f), new Color(0.75f, 0.5f, 0.9f), new Color(0.55f, 0.6f, 0.65f), new Color(0.4f, 0.85f, 0.8f)).StartAt("#004EFF"),

            Sun = Add("Sun", "Sun", GSky, new Color(1f, 0.86f, 0.42f),
                new Color(1f, 0.86f, 0.42f), new Color(1f, 0.95f, 0.75f), new Color(1f, 0.65f, 0.3f), new Color(1f, 0.45f, 0.3f), new Color(0.85f, 0.95f, 1f), new Color(1f, 0.6f, 0.85f), new Color(0.7f, 1f, 0.6f)),
            Mountains = Add("Mountains", "Mountains", GGround, new Color(0.48f, 0.5f, 0.51f)),
            MapWalls = Add("MapWalls", "Map dome (glass)", GGround, new Color(0.75f, 0.95f, 1f)), // (was the grey map walls: now the glass dome over the map, MapDome)
            BallZone = Add("BallZone", "Crash dirt (ball zone)", GGround, new Color(0.45f, 0.34f, 0.23f)), // (was the yellow ball drop circle: now the crash site's dirt, CrashSite)
            Wheat = Add("Wheat", "Tall wheat", GPlants, new Color(0.74f, 0.6f, 0.28f),
                new Color(0.74f, 0.6f, 0.28f), new Color(0.85f, 0.72f, 0.35f), new Color(0.6f, 0.48f, 0.22f), new Color(0.55f, 0.62f, 0.25f), new Color(0.8f, 0.5f, 0.25f), new Color(0.65f, 0.6f, 0.5f), new Color(0.75f, 0.4f, 0.55f)),
            Daisies = Add("Daisies", "Daisies", GPlants, new Color(0.97f, 0.97f, 0.94f),
                new Color(0.97f, 0.97f, 0.94f), new Color(1f, 0.85f, 0.4f), new Color(1f, 0.65f, 0.8f), new Color(0.6f, 0.75f, 1f), new Color(1f, 0.45f, 0.35f), new Color(0.8f, 0.6f, 1f), new Color(1f, 1f, 0.7f)),
            Lupins = Add("Lupins", "Lupins (flowers)", GPlants, new Color(0.6f, 0.42f, 0.86f)),
            TreeTrunks = Add("TreeTrunks", "Tree trunks", GPlants, new Color(0.36f, 0.23f, 0.12f)),
            Bushes = Add("Bushes", "Berry bushes", GPlants, new Color(0.25f, 0.5f, 0.2f)),
            Berries = Add("Berries", "Berries", GPlants, new Color(0.75f, 0.08f, 0.2f)),
            StoneNodes = Add("StoneNodes", "Stone nodes", GGround, new Color(0.56f, 0.56f, 0.6f)),
            Boulders = Add("Boulders", "Big rocks", GGround, new Color(0.54f, 0.53f, 0.5f)),
            BasePads = Add("BasePads", "Base floor", GBases, new Color(0.5f, 0.45f, 0.35f)),
            BaseGrid = Add("BaseGrid", "Base floor grid", GBases, new Color(0.625f, 0.5875f, 0.5125f)), // (the build grid's lines; BaseFloorLook)
            Bedrock = Add("Bedrock", "Bedrock", GBases, new Color(0.72f, 0.74f, 0.78f)),
            AlienMachine = Add("AlienMachine", "Alien machine", GBases, new Color(0.3f, 0.33f, 0.36f)),
            WoodMachine = Add("WoodMachine", "Wood machine", GBases, new Color(0.3f, 0.2f, 0.38f)),
            Workbench = Add("Workbench", "Trade station", GBases, new Color(0.55f, 0.37f, 0.2f)),
            BuildWood = Add("BuildWood", "Wood", GBuild, new Color(0.55f, 0.37f, 0.2f)),
            BuildStone = Add("BuildStone", "Stone", GBuild, new Color(0.56f, 0.56f, 0.6f)),
            BuildSheetMetal = Add("BuildSheetMetal", "Sheet metal", GBuild, new Color(0.5f, 0.46f, 0.42f)),
            BuildRefined = Add("BuildRefined", "Armoured", GBuild, new Color(0.27f, 0.29f, 0.34f)),
            Horses = Add("Horses", "Horses", GThings, new Color(0.45f, 0.3f, 0.18f)),
            Chests = Add("Chests", "Chests and crates", GThings, new Color(0.55f, 0.37f, 0.2f)),
            CrashSite = Add("CrashSite", "Crashed UFO", GThings, new Color(0.62f, 0.64f, 0.68f)),
            Hands = Add("Hands", "First-person hands", GYou, new Color(0.78f, 0.82f, 0.74f)).StartAt("#CED1CA"),
            // the building plan's see-through preview while the piece can go there (PlayerController.GhostOkColour; the
            // can't-go-there red stays red). Not a model colour: read straight from the slot
            BuildPlan = Add("BuildPlan", "Building plan preview", GBuild, new Color(0.3f, 1f, 0.45f)),
            // the building wheel's blue slices (Hud.DrawWheel; its centre disc is a darker shade of it). Read straight from the slot
            BuildWheel = Add("BuildWheel", "Building wheel", GBuild, new Color(0.38f, 0.62f, 0.95f)).StartAt("#006BFF");

        public static Slot Find(string id) { foreach (var s in All) if (s.Id == id) return s; return null; }

        // ---------------------------------------------------------------- picked colours

        static bool s_Loaded;
        static bool s_HandsTeam = true;

        /// <summary>First-person hands: in the team colour (the default, like the body) or the Hands colour.</summary>
        public static bool HandsTeam { get { Load(); return s_HandsTeam; } }

        static void Load()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            foreach (var s in All)
            {
                var hex = PlayerPrefs.GetString("RockGame.World." + s.Id, "");
                if (hex.Length > 0 && ColorUtility.TryParseHtmlString("#" + hex, out var c)) { c.a = 1f; s.m_Value = c; }
            }
            s_HandsTeam = PlayerPrefs.GetInt("RockGame.HandsTeam", 1) == 1;
        }

        public static bool Same(Color a, Color b) => ColorUtility.ToHtmlStringRGB(a) == ColorUtility.ToHtmlStringRGB(b);

        public static void Set(Slot s, Color c, bool save = true)
        {
            Load();
            c.a = 1f;
            bool handsSwitch = s == Hands && s_HandsTeam;
            if (s.m_Value == c && !handsSwitch) return;
            s.m_Value = c;
            if (s == Hands) s_HandsTeam = false; // (picking a colour for the hands means: not the team's)
            if (save) Save(s);
            Retint(s);
            GameSettings.FireWorldLookChanged();
        }

        /// <summary>Saves the slot's colour as it is now (the colour picker applies with save: false while you drag,
        /// then saves when you let go).</summary>
        public static void Save(Slot s)
        {
            Load();
            if (Same(s.m_Value, s.Default)) PlayerPrefs.DeleteKey("RockGame.World." + s.Id);
            else PlayerPrefs.SetString("RockGame.World." + s.Id, ColorUtility.ToHtmlStringRGB(s.m_Value));
            if (s == Hands) PlayerPrefs.SetInt("RockGame.HandsTeam", s_HandsTeam ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void SetHandsTeam(bool on, bool save = true)
        {
            Load();
            if (s_HandsTeam == on) return;
            s_HandsTeam = on;
            if (save) { PlayerPrefs.SetInt("RockGame.HandsTeam", on ? 1 : 0); PlayerPrefs.Save(); }
            GameSettings.FireWorldLookChanged();
        }

        /// <summary>Every slot back to its default (no event; the caller fires it).</summary>
        internal static void ResetAll(bool save)
        {
            Load();
            foreach (var s in All)
            {
                s.m_Value = s.Default;
                if (save) PlayerPrefs.DeleteKey("RockGame.World." + s.Id);
                Retint(s);
            }
            s_HandsTeam = true;
            if (save) PlayerPrefs.DeleteKey("RockGame.HandsTeam");
        }

        /// <summary>The colour as a multiplier of the default (per channel).</summary>
        public static Color Ratio(Slot s)
        {
            var c = s.Value;
            var d = s.Base;
            return new Color(c.r / Mathf.Max(0.02f, d.r), c.g / Mathf.Max(0.02f, d.g), c.b / Mathf.Max(0.02f, d.b), 1f);
        }

        /// <summary>A model colour shifted by the slot (only in Normal graphics).</summary>
        public static Color Tinted(Slot s, Color c)
        {
            if (GameSettings.GraphicsMode != 0 || Same(s.Value, s.Base)) return c;
            var k = Ratio(s);
            return new Color(Mathf.Clamp01(c.r * k.r), Mathf.Clamp01(c.g * k.g), Mathf.Clamp01(c.b * k.b), c.a);
        }

        /// <summary>The ratio as a linear multiplier for shaders that work in linear space.</summary>
        public static Vector4 LinearRatio(Slot s)
        {
            var c = s.Value.linear;
            var d = s.Base.linear;
            return new Vector4(c.r / Mathf.Max(0.002f, d.r), c.g / Mathf.Max(0.002f, d.g), c.b / Mathf.Max(0.002f, d.b), 1f);
        }

        /// <summary>The first-person block hands' skin for a team: grey-green with a quarter of the team colour (the
        /// original look), or the Hands colour picked.</summary>
        public static Color HandTint(Color team) => HandsTeam ? Color.Lerp(new Color(0.6f, 0.64f, 0.58f), team, 0.25f) : Hands.Value;

        // ---------------------------------------------------------------- the models' materials

        static int s_Active = -1;
        static readonly Dictionary<(int, Color), Material> s_Mats = new Dictionary<(int, Color), Material>();

        /// <summary>Restores the slot that was active before (use with `using`).</summary>
        public readonly struct Scope : System.IDisposable
        {
            readonly int m_Prev;
            internal Scope(int prev) { m_Prev = prev; }
            public void Dispose() => s_Active = m_Prev;
        }

        /// <summary>Art.Mat calls until the scope ends make materials of this slot (null: plain ones).</summary>
        public static Scope Use(Slot s)
        {
            int prev = s_Active;
            s_Active = s != null ? s.Index : -1;
            return new Scope(prev);
        }

        public static Slot Active => s_Active >= 0 ? All[s_Active] : null;

        /// <summary>The slot's own plain-colour material for colour c (tinted by the slot).</summary>
        public static Material Mat(Slot s, Color c)
        {
            if (s_Mats.TryGetValue((s.Index, c), out var m) && m) return m;
            m = Art.NewMat(c);
            m.name = s.Id + " " + ColorUtility.ToHtmlStringRGB(c);
            Art.Register(m, c); // (PSX / AI PSX see the original colour)
            s_Mats[(s.Index, c)] = m;
            s.Mats.Add((m, c));
            var t = Tinted(s, c);
            m.SetColor("_BaseColor", t);
            m.color = t;
            return m;
        }

        static void Retint(Slot s)
        {
            for (int i = s.Mats.Count - 1; i >= 0; i--)
            {
                var (m, c) = s.Mats[i];
                if (!m) { s.Mats.RemoveAt(i); continue; }
                var t = Tinted(s, c);
                m.SetColor("_BaseColor", t);
                m.color = t;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() => GameSettings.GraphicsChanged += () => { foreach (var s in All) Retint(s); };

        // ---------------------------------------------------------------- the clipboard

        /// <summary>"Id = #RRGGBB" lines, for the slots changed from their default (or all of them).</summary>
        public static string Lines(bool changedOnly)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in All)
            {
                if (changedOnly && !s.Changed) continue;
                sb.Append(s.Id).Append(" = ").Append(s.Hex).Append('\n');
            }
            if (!changedOnly || !HandsTeam) sb.Append("HandsUseTeamColour = ").Append(HandsTeam ? "true" : "false").Append('\n');
            return sb.ToString();
        }

        public static int ChangedCount { get { int n = 0; foreach (var s in All) if (s.Changed) n++; return n; } }

        /// <summary>Copies the lines to the clipboard; returns how many colours.</summary>
        public static int Copy(bool changedOnly)
        {
            GUIUtility.systemCopyBuffer = Lines(changedOnly);
            return changedOnly ? ChangedCount : All.Count;
        }
    }

    /// <summary>
    /// Re-colours the first-person block hands when their colour setting changes (sits on each hand): the fist, thumb and
    /// forearm in the skin colour (ColorSlots.HandTint), the knuckle row a shade darker, the wrist band in the team colour.
    /// </summary>
    public class HandColorHook : MonoBehaviour
    {
        public const int Skin = 0, Knuckles = 1, Band = 2;
        Color m_Team;
        readonly List<(Renderer r, int part)> m_Parts = new List<(Renderer, int)>();

        /// <summary>The colour of one part of the hands for this team.</summary>
        public static Color Shade(Color team, int part)
        {
            var skin = ColorSlots.HandTint(team);
            if (part == Band) return team;
            if (part == Knuckles) { var dark = skin * 0.85f; dark.a = 1f; return dark; }
            return skin;
        }

        public static HandColorHook Add(Transform hand, Color team)
        {
            var h = hand.gameObject.AddComponent<HandColorHook>();
            h.m_Team = team;
            return h;
        }

        /// <summary>Keeps this box in its part's colour from now on.</summary>
        public GameObject Track(GameObject box, int part)
        {
            var r = box != null ? box.GetComponent<Renderer>() : null;
            if (r != null) m_Parts.Add((r, part));
            return box;
        }

        /// <summary>(tests) Every part of this hand in its colour now.</summary>
        public bool AllShaded(out int parts)
        {
            parts = m_Parts.Count;
            foreach (var p in m_Parts) if (p.r == null || !ColorSlots.Same(p.r.sharedMaterial.color, Shade(m_Team, p.part))) return false;
            return parts > 0;
        }

        void OnEnable() { GameSettings.WorldLookChanged += Apply; Apply(); }
        void OnDisable() => GameSettings.WorldLookChanged -= Apply;

        void Apply()
        {
            if (this == null) return;
            foreach (var p in m_Parts)
            {
                if (p.r == null) continue;
                var c = Shade(m_Team, p.part);
                if (p.r.sharedMaterial == null || p.r.sharedMaterial.color != c) p.r.sharedMaterial = Art.Mat(c);
            }
        }
    }
}
