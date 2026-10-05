using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// THE DISPLAY DEFAULTS TABLE: what every Settings > Display value (except the screen settings) starts at on a PC
    /// that has never changed it, and what the Defaults / Reset buttons go back to. Change a default here and nowhere
    /// else. Each line names the key it has in the display settings code (COPY SETTINGS / PASTE SETTINGS), so a code
    /// someone sends over maps straight onto these lines. (The world colours' defaults are the slots' own, in
    /// WorldColors.cs: the Add(...) lines there.)
    /// </summary>
    public static class DisplayDefaults
    {
        // ---- post processing ----
        // (the defaults below are the look the game was tuned to - a COPY SETTINGS export, 2026-10-06; the old default
        // of each value that changed then is in its comment as "was ...")

        public const bool PostFx = true;                    // post
        public const bool Bloom = false;                    // post.bloom               (was on)
        public const float BloomStrength = 0.55f;           // post.bloom.strength      (0..1; was 0.5)
        public const bool Vignette = true;                  // post.vignette
        public const float VignetteStrength = 0f;           // post.vignette.strength   (0..1; was 0.5)
        public const bool Grading = true;                   // post.grading
        public const float GradingStrength = 0.65f;         // post.grading.strength    (0..1; was 0.5)
        // post.extra.<name> / post.extra.<name>.strength (0..1), one each, in GameSettings.PostExtra's order:
        //   outlines on 0.45 (was off 0.5) · ambientocclusion off 1 (was 0.5) · haze on 0.8 (was off 0.5) · depthoffield off 0.5
        //   filmgrain off 0 (was 0.5) · chromatic off 0.5 · sharpen off 0 (was 0.5) · celbanding on 0.5 (was off)
        static readonly bool[] s_PostExtraOn = { true, false, true, false, false, false, false, true };
        static readonly float[] s_PostExtraStrength = { 0.45f, 1f, 0.8f, 0.5f, 0f, 0.5f, 0f, 0.5f };
        public static bool PostExtraOn(int i) => i >= 0 && i < s_PostExtraOn.Length && s_PostExtraOn[i];
        public static float PostExtraStrength(int i) => i >= 0 && i < s_PostExtraStrength.Length ? s_PostExtraStrength[i] : 0.5f;

        // ---- post processing on the UI ----
        public const bool PostOnUi = false;                 // ui.post
        public const bool UiWorldPost = true;               // ui.post.world            (the world's effects go over the UI too)
        public const bool UiCel = false;                    // ui.cel
        public const float UiCelStrength = 0.1f;            // ui.cel.strength          (0..1: fewer, flatter colour steps; was 0.5)
        public const bool UiOutline = true;                 // ui.outline               (was off)
        public const float UiOutlineWidth = 3.5f;           // ui.outline.width         (0.5..8; x 1.333 = px at 1440p, scaled with the screen height; was 2)
        public const string UiOutlineColour = "#000000";    // ui.outline.colour
        public const float UiOutlineOpacity = 0.9f;         // ui.outline.opacity       (0..1)
        public const bool UiBloom = false;                  // ui.bloom
        public const float UiBloomStrength = 0.5f;          // ui.bloom.strength        (0..1)
        public const float UiSaturation = 0.9f;             // ui.saturation            (0..2, 1 = as drawn; was 1)
        public const float UiContrast = 1f;                 // ui.contrast              (0.5..1.6, 1 = as drawn)

        // ---- shadows ----
        public const float ShadowStrength = 0.75f;          // shadows.darkness         (0..1; was 1)
        public const float ShadowDistance = 80f;            // shadows.distance         (20..300 m; was 50)

        // ---- interface ----
        public const int UiFont = 1;                        // ui.font                  (1 = Bahnschrift, GameSettings.FontChoices; by name in the code; was 0 = Classic)
        public const float UiScale = 1f;                    // ui.scale                 (0.75..1.4)
        public const float HudOpacity = 1f;                 // ui.hud.opacity           (0.25..1)
        public const int UiAccent = 0;                      // ui.accent                (0 = Gold; by name in the code)
        public const bool ShowFps = true;                   // fps.counter              (was off)

        // ---- main menu ----
        public const bool MenuTrees = true;                 // menu.trees               (trees in the play space behind the main menu - always now; not a setting any more)

        // ---- alien glow ----
        public const float GlowStrength = 0.02f;            // glow.strength            (0.02..1; was 0.3)
        public const float GlowWidth = 0.005f;              // glow.width               (0.005..0.12 m; was 0.03)

        // ---- shading ----
        public const bool SmoothHands = false;              // shade.smooth.hands       (first-person hands + held items)
        public const bool SmoothAliens = true;              // shade.smooth.aliens      (alien players + the stadium crowd; was off)

        // ---- grass ----
        public const float GrassDistance = 260f;            // grass.distance           (25..270 m; was 60)
        public const float GrassDensity = 1f;               // grass.density            (0.2..1)
        public const float GrassFalloff = 0.65f;            // grass.falloff            (0.25..2.5: lower = thicker far away; was 1.35)
        public const float GrassHeight = 1.55f;             // grass.height             (0.6..2.5; was 1)

        // (elsewhere: beams.falloff 125 (was 70) - BeamFx.cs; treex.glow 2.85 (was 1.6) - TreeX.cs; basefloor.after
        //  Colour grid (was Flat grass) and basefloor.teammix 0.5 (was 0.35) - BaseFloor.cs; the world colours, and the
        //  building plan preview's colour (colour.BuildPlan) - WorldColors.cs: the .StartAt(...) ones were changed)

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.black;
    }

    /// <summary>
    /// A display setting that keeps itself: saved in PlayerPrefs under its own key, loaded on first use, and included in
    /// the display settings code automatically. Declare one as a static field of GameSettings and that's all:
    /// <c>public static readonly DisplayPref.Float BeamStrength = new("beams.strength", "BEAMS", DisplayDefaults.BeamStrength, 0f, 1f);</c>
    /// then read <c>GameSettings.BeamStrength.Value</c> and set it with <c>.Set(v)</c>.
    /// </summary>
    public abstract class DisplayPref
    {
        /// <summary>The key in the display settings code (also its PlayerPrefs key, after "RockGame.Disp.").</summary>
        public readonly string Key;
        public readonly string Group;
        /// <summary>Fired after any DisplayPref changes.</summary>
        public static event Action Changed;
        internal static readonly List<DisplayPref> All = new List<DisplayPref>();

        protected DisplayPref(string key, string group)
        {
            Key = key; Group = group;
            lock (All) All.Add(this);
        }

        protected string PrefKey => "RockGame.Disp." + Key;
        protected static void Fire() => Changed?.Invoke();
        internal abstract DisplayCode.Entry Entry();

        public sealed class Bool : DisplayPref
        {
            public readonly bool Default;
            bool m_V, m_Loaded;
            public Bool(string key, string group, bool def) : base(key, group) { Default = def; m_V = def; }
            public bool Value { get { if (!m_Loaded) { m_Loaded = true; m_V = PlayerPrefs.GetInt(PrefKey, Default ? 1 : 0) == 1; } return m_V; } }
            public void Set(bool v, bool save = true)
            {
                if (v == Value) return;
                m_V = v;
                if (save) { PlayerPrefs.SetInt(PrefKey, v ? 1 : 0); PlayerPrefs.Save(); }
                Fire();
            }
            internal override DisplayCode.Entry Entry() => DisplayCode.MakeBool(Key, Group, Default, () => Value, Set);
        }

        public sealed class Float : DisplayPref
        {
            public readonly float Default, Min, Max;
            float m_V; bool m_Loaded;
            public Float(string key, string group, float def, float min, float max) : base(key, group) { Default = def; Min = min; Max = max; m_V = def; }
            public float Value { get { if (!m_Loaded) { m_Loaded = true; m_V = Mathf.Clamp(PlayerPrefs.GetFloat(PrefKey, Default), Min, Max); } return m_V; } }
            public void Set(float v, bool save = true)
            {
                v = Mathf.Clamp(v, Min, Max);
                if (Mathf.Approximately(v, Value)) return;
                m_V = v;
                if (save) { PlayerPrefs.SetFloat(PrefKey, v); PlayerPrefs.Save(); }
                Fire();
            }
            internal override DisplayCode.Entry Entry() => DisplayCode.MakeFloat(Key, Group, Default, () => Value, Set);
        }

        public sealed class Colour : DisplayPref
        {
            public readonly Color Default;
            Color m_V; bool m_Loaded;
            public Colour(string key, string group, Color def) : base(key, group) { def.a = 1f; Default = def; m_V = def; }
            public Color Value
            {
                get
                {
                    if (!m_Loaded)
                    {
                        m_Loaded = true;
                        var hex = PlayerPrefs.GetString(PrefKey, "");
                        if (hex.Length > 0 && ColorUtility.TryParseHtmlString("#" + hex, out var c)) { c.a = 1f; m_V = c; }
                    }
                    return m_V;
                }
            }
            public void Set(Color v, bool save = true)
            {
                v.a = 1f;
                if (ColorSlots.Same(v, Value)) return;
                m_V = v;
                if (save) { PlayerPrefs.SetString(PrefKey, ColorUtility.ToHtmlStringRGB(v)); PlayerPrefs.Save(); }
                Fire();
            }
            internal override DisplayCode.Entry Entry() => DisplayCode.MakeColour(Key, Group, Default, () => Value, Set);
        }
    }

    /// <summary>
    /// Settings > Display > COPY SETTINGS / PASTE SETTINGS: every display value except the screen settings (window
    /// mode, resolution, refresh rate) as a short readable text code:
    /// <code>
    /// ALIEN ROCK GAME display settings v1
    /// # ---- POST PROCESSING ----
    /// post = on
    /// post.bloom.strength = 0.5
    /// colour.Sky = #8FC1E8      # changed (default #9DCBEA)
    /// </code>
    /// One "key = value" per line; "#" starts a comment; on / off for switches, plain numbers (a trailing % is divided by
    /// 100), #RRGGBB for colours, names for the font and accent colour. Pasting: unknown keys are ignored, keys that
    /// aren't there keep their current values, a bad value is skipped (and reported).
    /// HOW TO ADD A SETTING: one line in Table() below (Bool / Float / Choice / Colour with its key, group, default,
    /// getter and setter), or declare it as a DisplayPref field of GameSettings and it's included without even that.
    /// Every world colour slot (ColorSlots.All) is included automatically.
    /// </summary>
    public static class DisplayCode
    {
        public const int Version = 1;
        public const string Header = "ALIEN ROCK GAME display settings v";

        public sealed class Entry
        {
            public string Key, Group, Default;
            public Func<string> Get;
            /// <summary>Parses and applies a value (save: write it to PlayerPrefs). False: the value didn't parse.</summary>
            public Func<string, bool, bool> Set;
            public bool IsDefault => Same(Get(), Default);
            static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================== THE TABLE (one line per setting)

        const string GPost = "POST PROCESSING", GUiPost = "POST PROCESSING ON THE UI", GShadows = "SHADOWS",
            GInterface = "INTERFACE", GGlow = "ALIEN GLOW", GGrass = "GRASS", GColours = "WORLD COLOURS";

        static void Table()
        {
            // ---- post processing (world) ----
            Bool("post", GPost, DisplayDefaults.PostFx, () => GameSettings.PostFx, (v, s) => GameSettings.SetPostFx(v, s));
            Bool("post.bloom", GPost, DisplayDefaults.Bloom, () => GameSettings.PostBloom, (v, s) => SetPost(bloom: v, save: s));
            Float("post.bloom.strength", GPost, DisplayDefaults.BloomStrength, () => GameSettings.PostBloomStrength, (v, s) => SetPost(bloomStr: v, save: s));
            Bool("post.vignette", GPost, DisplayDefaults.Vignette, () => GameSettings.PostVignette, (v, s) => SetPost(vignette: v, save: s));
            Float("post.vignette.strength", GPost, DisplayDefaults.VignetteStrength, () => GameSettings.PostVignetteStrength, (v, s) => SetPost(vignetteStr: v, save: s));
            Bool("post.grading", GPost, DisplayDefaults.Grading, () => GameSettings.PostGrading, (v, s) => SetPost(grading: v, save: s));
            Float("post.grading.strength", GPost, DisplayDefaults.GradingStrength, () => GameSettings.PostGradingStrength, (v, s) => SetPost(gradingStr: v, save: s));
            for (int i = 0; i < GameSettings.PostExtraCount; i++)
            {
                var e = (GameSettings.PostExtra)i;
                string k = "post.extra." + e.ToString().ToLowerInvariant();
                Bool(k, GPost, DisplayDefaults.PostExtraOn(i), () => GameSettings.PostExtraOn(e), (v, s) => GameSettings.SetPostExtra(e, v, GameSettings.PostExtraStrength(e), s));
                Float(k + ".strength", GPost, DisplayDefaults.PostExtraStrength(i), () => GameSettings.PostExtraStrength(e), (v, s) => GameSettings.SetPostExtra(e, GameSettings.PostExtraOn(e), v, s));
            }
            // ---- post processing on the UI ----
            Bool("ui.post", GUiPost, DisplayDefaults.PostOnUi, () => GameSettings.PostOnUi, (v, s) => GameSettings.SetPostOnUi(v, s));
            Pref(GameSettings.UiWorldPost, GameSettings.UiCel, GameSettings.UiCelStrength, GameSettings.UiOutline, GameSettings.UiOutlineWidth,
                GameSettings.UiOutlineColour, GameSettings.UiOutlineOpacity, GameSettings.UiBloom, GameSettings.UiBloomStrength,
                GameSettings.UiSaturation, GameSettings.UiContrast);
            // ---- shadows ----
            Float("shadows.darkness", GShadows, DisplayDefaults.ShadowStrength, () => GameSettings.ShadowStrength, (v, s) => GameSettings.SetShadows(v, GameSettings.ShadowDistance, s));
            Float("shadows.distance", GShadows, DisplayDefaults.ShadowDistance, () => GameSettings.ShadowDistance, (v, s) => GameSettings.SetShadows(GameSettings.ShadowStrength, v, s));
            // ---- shading (SmoothShade.cs) ----
            Pref(GameSettings.SmoothHands, GameSettings.SmoothAliens);
            // ---- interface ----
            Choice("ui.font", GInterface, GameSettings.FontChoices, DisplayDefaults.UiFont, () => GameSettings.UiFont,
                (v, s) => GameSettings.SetInterface(v, GameSettings.UiScale, GameSettings.HudOpacity, GameSettings.UiAccent, s));
            Float("ui.scale", GInterface, DisplayDefaults.UiScale, () => GameSettings.UiScale, (v, s) => GameSettings.SetInterface(GameSettings.UiFont, v, GameSettings.HudOpacity, GameSettings.UiAccent, s));
            Float("ui.hud.opacity", GInterface, DisplayDefaults.HudOpacity, () => GameSettings.HudOpacity, (v, s) => GameSettings.SetInterface(GameSettings.UiFont, GameSettings.UiScale, v, GameSettings.UiAccent, s));
            Choice("ui.accent", GInterface, GameSettings.AccentNames, DisplayDefaults.UiAccent, () => GameSettings.UiAccent,
                (v, s) => GameSettings.SetInterface(GameSettings.UiFont, GameSettings.UiScale, GameSettings.HudOpacity, v, s));
            Bool("fps.counter", GInterface, DisplayDefaults.ShowFps, () => GameSettings.ShowFps, (v, s) => GameSettings.SetShowFps(v, s));
            // ---- alien glow ----
            Float("glow.strength", GGlow, DisplayDefaults.GlowStrength, () => Cfg.AlienOutlineStrength, (v, s) => GameSettings.SetAlienGlow(v, Cfg.AlienOutlineWidth, s));
            Float("glow.width", GGlow, DisplayDefaults.GlowWidth, () => Cfg.AlienOutlineWidth, (v, s) => GameSettings.SetAlienGlow(Cfg.AlienOutlineStrength, v, s));
            // ---- grass ----
            Float("grass.distance", GGrass, DisplayDefaults.GrassDistance, () => GameSettings.GrassDistance, (v, s) => GameSettings.SetGrass(v, GameSettings.GrassDensity, GameSettings.GrassFalloff, s));
            Float("grass.density", GGrass, DisplayDefaults.GrassDensity, () => GameSettings.GrassDensity, (v, s) => GameSettings.SetGrass(GameSettings.GrassDistance, v, GameSettings.GrassFalloff, s));
            Float("grass.falloff", GGrass, DisplayDefaults.GrassFalloff, () => GameSettings.GrassFalloff, (v, s) => GameSettings.SetGrass(GameSettings.GrassDistance, GameSettings.GrassDensity, v, s));
            Float("grass.height", GGrass, DisplayDefaults.GrassHeight, () => GameSettings.GrassHeight, (v, s) => GameSettings.SetGrassHeight(v, s));
            // ---- beams ----
            Float("beams.strength", "BEAMS", GameSettings.BeamStrengthDefault, () => GameSettings.BeamStrength, (v, s) => GameSettings.SetBeams(v, GameSettings.BeamFalloff, s));
            Float("beams.falloff", "BEAMS", GameSettings.BeamFalloffDefault, () => GameSettings.BeamFalloff, (v, s) => GameSettings.SetBeams(GameSettings.BeamStrength, v, s));
            // ---- tree X ----
            Float("treex.glow", "TREE X", GameSettings.TreeXGlowDefault, () => GameSettings.TreeXGlow, (v, s) => GameSettings.SetTreeX(v, GameSettings.TreeXHalo, GameSettings.TreeXSize, GameSettings.TreeXColour, s));
            Float("treex.halo", "TREE X", GameSettings.TreeXHaloDefault, () => GameSettings.TreeXHalo, (v, s) => GameSettings.SetTreeX(GameSettings.TreeXGlow, v, GameSettings.TreeXSize, GameSettings.TreeXColour, s));
            Float("treex.size", "TREE X", GameSettings.TreeXSizeDefault, () => GameSettings.TreeXSize, (v, s) => GameSettings.SetTreeX(GameSettings.TreeXGlow, GameSettings.TreeXHalo, v, GameSettings.TreeXColour, s));
            Colour("treex.colour", "TREE X", GameSettings.TreeXColours[0], () => GameSettings.TreeXColour, (c, s) => GameSettings.SetTreeX(GameSettings.TreeXGlow, GameSettings.TreeXHalo, GameSettings.TreeXSize, c, s));
            // ---- base floor (its colours are world colour slots, below) ----
            Choice("basefloor.after", "BASE FLOOR", GameSettings.BaseFloorStyleNames, (int)GameSettings.BaseFloorAfterDefault, () => (int)GameSettings.BaseFloorAfter,
                (v, s) => GameSettings.SetBaseFloor((GameSettings.BaseFloorStyle)v, GameSettings.BaseFloorTeamMix, s));
            Float("basefloor.teammix", "BASE FLOOR", GameSettings.BaseFloorTeamMixDefault, () => GameSettings.BaseFloorTeamMix, (v, s) => GameSettings.SetBaseFloor(GameSettings.BaseFloorAfter, v, s));
            // (new settings go here: one line each, like the ones above, e.g.
            //  Float("beams.strength", "BEAMS", DisplayDefaults.BeamStrength, () => GameSettings.BeamStrength, (v, s) => GameSettings.SetBeams(v, GameSettings.BeamFalloff, s));)

            // ---- every world colour (automatically: each slot in ColorSlots.All) ----
            Bool("colour.hands.team", GColours, true, () => ColorSlots.HandsTeam, (v, s) => ColorSlots.SetHandsTeam(v, s));
            foreach (var slot in ColorSlots.All)
            {
                var sl = slot;
                Colour("colour." + sl.Id, GColours, sl.Default, () => sl.Value, (c, s) =>
                {
                    // (setting the hands' colour switches their team colour off: keep that as colour.hands.team says,
                    // whichever order the two lines come in)
                    if (ColorSlots.Same(sl.Value, c)) return;
                    bool team = sl == ColorSlots.Hands && ColorSlots.HandsTeam;
                    ColorSlots.Set(sl, c, s);
                    if (team) ColorSlots.SetHandsTeam(true, s);
                });
            }
        }

        static void SetPost(bool? bloom = null, bool? vignette = null, bool? grading = null, float? bloomStr = null, float? vignetteStr = null, float? gradingStr = null, bool save = true)
            => GameSettings.SetPostFx(GameSettings.PostFx, bloom ?? GameSettings.PostBloom, vignette ?? GameSettings.PostVignette, grading ?? GameSettings.PostGrading,
                bloomStr ?? GameSettings.PostBloomStrength, vignetteStr ?? GameSettings.PostVignetteStrength, gradingStr ?? GameSettings.PostGradingStrength, save);

        // ================================================================== registering

        static List<Entry> s_Entries;
        static Dictionary<string, Entry> s_ByKey;

        /// <summary>Every setting in the code, in order (built on first use).</summary>
        public static IReadOnlyList<Entry> Entries { get { Build(); return s_Entries; } }

        public static Entry Find(string key)
        {
            Build();
            if (string.IsNullOrEmpty(key)) return null;
            if (s_ByKey.TryGetValue(key, out var e)) return e;
            // (the old world colours copy: "TreeTrunks = #4F8F2A", "HandsUseTeamColour = false")
            if (s_ByKey.TryGetValue("colour." + key, out e)) return e;
            if (string.Equals(key, "HandsUseTeamColour", StringComparison.OrdinalIgnoreCase)) return s_ByKey["colour.hands.team"];
            return null;
        }

        static void Build()
        {
            if (s_Entries != null) return;
            s_Entries = new List<Entry>();
            s_ByKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            // (makes every DisplayPref field of GameSettings exist, so they're all registered)
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(GameSettings).TypeHandle);
            Table();
            DisplayPref[] prefs;
            lock (DisplayPref.All) prefs = DisplayPref.All.ToArray();
            foreach (var p in prefs) if (!s_ByKey.ContainsKey(p.Key)) Add(p.Entry());
        }

        /// <summary>Adds a setting (any time; a key already there is replaced). Mostly for Table() and DisplayPref.</summary>
        public static void Add(Entry e)
        {
            if (s_Entries == null) Build();
            if (s_ByKey.TryGetValue(e.Key, out var old)) s_Entries[s_Entries.IndexOf(old)] = e;
            else s_Entries.Add(e);
            s_ByKey[e.Key] = e;
        }

        static void Pref(params DisplayPref[] prefs) { foreach (var p in prefs) Add(p.Entry()); }

        public static void Bool(string key, string group, bool def, Func<bool> get, Action<bool, bool> set) => Add(MakeBool(key, group, def, get, set));
        public static void Float(string key, string group, float def, Func<float> get, Action<float, bool> set) => Add(MakeFloat(key, group, def, get, set));
        public static void Colour(string key, string group, Color def, Func<Color> get, Action<Color, bool> set) => Add(MakeColour(key, group, def, get, set));
        public static void Choice(string key, string group, string[] names, int def, Func<int> get, Action<int, bool> set) => Add(MakeChoice(key, group, names, def, get, set));

        internal static Entry MakeBool(string key, string group, bool def, Func<bool> get, Action<bool, bool> set) => new Entry
        {
            Key = key, Group = group, Default = OnOff(def), Get = () => OnOff(get()),
            Set = (t, s) => { if (!ParseBool(t, out var v)) return false; set(v, s); return true; },
        };

        internal static Entry MakeFloat(string key, string group, float def, Func<float> get, Action<float, bool> set) => new Entry
        {
            Key = key, Group = group, Default = Num(def), Get = () => Num(get()),
            Set = (t, s) => { if (!ParseFloat(t, out var v)) return false; set(v, s); return true; },
        };

        internal static Entry MakeColour(string key, string group, Color def, Func<Color> get, Action<Color, bool> set) => new Entry
        {
            Key = key, Group = group, Default = HexOf(def), Get = () => HexOf(get()),
            Set = (t, s) => { if (!ParseColour(t, out var v)) return false; set(v, s); return true; },
        };

        internal static Entry MakeChoice(string key, string group, string[] names, int def, Func<int> get, Action<int, bool> set) => new Entry
        {
            Key = key, Group = group, Default = names[Mathf.Clamp(def, 0, names.Length - 1)], Get = () => names[Mathf.Clamp(get(), 0, names.Length - 1)],
            Set = (t, s) =>
            {
                t = t.Trim();
                int v = Array.FindIndex(names, n => string.Equals(n, t, StringComparison.OrdinalIgnoreCase));
                if (v < 0 && (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 0 || v >= names.Length)) return false;
                set(v, s);
                return true;
            },
        };

        // ================================================================== values

        static string OnOff(bool b) => b ? "on" : "off";
        static string Num(float f) => (Mathf.Round(f * 10000f) / 10000f).ToString("0.####", CultureInfo.InvariantCulture);
        static string HexOf(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static bool ParseBool(string t, out bool v)
        {
            switch (t.Trim().ToLowerInvariant())
            {
                case "on": case "true": case "yes": case "1": v = true; return true;
                case "off": case "false": case "no": case "0": v = false; return true;
            }
            v = false;
            return false;
        }

        static bool ParseFloat(string t, out float v)
        {
            t = t.Trim().Replace(',', '.');
            bool pct = t.EndsWith("%");
            if (pct) t = t.Substring(0, t.Length - 1).Trim();
            // (a unit after the number is fine: "50 m", "2 px")
            int end = 0;
            while (end < t.Length && (char.IsDigit(t[end]) || t[end] == '.' || t[end] == '-' || t[end] == '+' || t[end] == 'e' || t[end] == 'E')) end++;
            if (end < t.Length && !char.IsWhiteSpace(t[end])) { v = 0; return false; }
            if (!float.TryParse(t.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || float.IsNaN(v) || float.IsInfinity(v)) return false;
            if (pct) v /= 100f;
            return true;
        }

        static bool ParseColour(string t, out Color c)
        {
            t = t.Trim().TrimStart('#');
            if (t.Length == 6 && ColorUtility.TryParseHtmlString("#" + t, out c)) { c.a = 1f; return true; }
            c = Color.black;
            return false;
        }

        // ================================================================== copy / paste

        /// <summary>The code for every display setting as it is now.</summary>
        public static string Export()
        {
            Build();
            var sb = new StringBuilder();
            sb.Append(Header).Append(Version).Append('\n');
            sb.Append("# Settings > Display (everything except the screen settings), ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# One \"key = value\" per line. Paste it back with PASTE SETTINGS; unknown keys are ignored, missing ones are left as they are.\n");
            string group = null;
            int changed = 0;
            foreach (var e in s_Entries)
            {
                if (e.Group != group) { group = e.Group; sb.Append("# ---- ").Append(group).Append(" ----\n"); }
                string v = e.Get();
                sb.Append(e.Key).Append(" = ").Append(v);
                if (!e.IsDefault) { sb.Append("    # changed (default ").Append(e.Default).Append(')'); changed++; }
                sb.Append('\n');
            }
            sb.Append("# ").Append(changed).Append(changed == 1 ? " value" : " values").Append(" changed from the defaults.\n");
            return sb.ToString();
        }

        /// <summary>What a paste did.</summary>
        public struct Result
        {
            public bool Ok;
            public int Applied, Unknown, Bad;
            public string Error, FirstBad;
            public override string ToString() => !Ok ? Error
                : $"Pasted: {Applied} setting{(Applied == 1 ? "" : "s")} applied" + (Unknown > 0 ? $", {Unknown} unknown key{(Unknown == 1 ? "" : "s")} ignored" : "")
                  + (Bad > 0 ? $", {Bad} bad value{(Bad == 1 ? "" : "s")} skipped ({FirstBad})" : "") + ".";
        }

        /// <summary>Reads a code and applies it (save: written to PlayerPrefs too). Nothing is applied unless the text
        /// has the header or at least one known key.</summary>
        public static Result Apply(string text, bool save = true)
        {
            Build();
            var r = new Result();
            if (string.IsNullOrWhiteSpace(text)) { r.Error = "The clipboard is empty - copy a display settings code first."; return r; }
            var lines = text.Replace("\r", "").Split('\n');
            bool header = false;
            int version = 0;
            var todo = new List<(Entry e, string v, string key)>();
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(Header, StringComparison.OrdinalIgnoreCase))
                {
                    header = true;
                    int.TryParse(line.Substring(Header.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
                    continue;
                }
                if (line.StartsWith("#") || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                // a "#" starts a comment - except the one a colour starts with
                int hash = val.IndexOf('#', val.StartsWith("#") ? 1 : 0);
                if (hash >= 0) val = val.Substring(0, hash).Trim();
                if (key.Length == 0 || key.Contains("#")) continue;
                var e = Find(key);
                if (e == null) { r.Unknown++; continue; }
                todo.Add((e, val, key));
            }
            if (!header && todo.Count == 0) { r.Error = "That isn't a display settings code (no \"" + Header + "1\" line and no known settings)."; return r; }
            if (todo.Count == 0) { r.Error = "The code has no settings this version of the game knows."; return r; }
            foreach (var (e, v, key) in todo)
            {
                bool ok;
                try { ok = e.Set(v, save); }
                catch (Exception ex) { ok = false; Debug.LogWarning($"[RockGame] display code: {key} = {v}: {ex.Message}"); }
                if (ok) r.Applied++;
                else { r.Bad++; if (r.FirstBad == null) r.FirstBad = key + " = " + v; }
            }
            r.Ok = true;
            if (version > Version) Debug.Log($"[RockGame] display code v{version} is newer than this game's v{Version}: applied what it knows");
            if (save) PlayerPrefs.Save();
            return r;
        }

        /// <summary>COPY SETTINGS: the code on the clipboard. Returns how many settings it holds.</summary>
        public static int CopyToClipboard()
        {
            GUIUtility.systemCopyBuffer = Export();
            return Entries.Count;
        }

        /// <summary>PASTE SETTINGS: reads the clipboard and applies it.</summary>
        public static Result PasteFromClipboard() => Apply(GUIUtility.systemCopyBuffer);
    }
}
