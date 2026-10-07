using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > POST PROCESSING, more layers with looks of their own (when one is off it
    /// looks like everything else; the hands' and the notifications' are on to start with, the tools' off):
    /// HANDS and TOOLS & WEAPONS - your first-person hands, and what they hold, each with their own outlines (and how
    ///   dark they are), cel banding, saturation and contrast instead of the world's (PostFx.cs: the hands and the held
    ///   item are drawn into a mask - 1 and 0.5 - and the Stylize pass uses these numbers inside it).
    /// NOTIFICATIONS - the big messages in the middle of the screen (TRADE STATION UNLOCKED, AIRDROP INCOMING, the
    ///   countdowns): their own cel shading, outlines, glow, saturation and contrast, laid on after the world's post
    ///   processing (UiLook.cs: they're drawn into a texture of their own and composited with these).
    /// </summary>
    public static partial class GameSettings
    {
        // ---- hands and tools ----
        const string GHands = "HANDS AND TOOLS";
        /// <summary>The hands and tools get the looks below instead of the world's.</summary>
        public static readonly DisplayPref.Bool HandsOwn = new("hands.own", GHands, DisplayDefaults.HandsOwn);
        public static readonly DisplayPref.Bool HandsOutline = new("hands.outline", GHands, DisplayDefaults.HandsOutline);
        public static readonly DisplayPref.Float HandsOutlineStrength = new("hands.outline.strength", GHands, DisplayDefaults.HandsOutlineStrength, 0f, 1f);
        public static readonly DisplayPref.Bool HandsCel = new("hands.cel", GHands, DisplayDefaults.HandsCel);
        public static readonly DisplayPref.Float HandsCelStrength = new("hands.cel.strength", GHands, DisplayDefaults.HandsCelStrength, 0f, 1f);
        public static readonly DisplayPref.Float HandsSaturation = new("hands.saturation", GHands, DisplayDefaults.HandsSaturation, 0f, 2f);
        public static readonly DisplayPref.Float HandsContrast = new("hands.contrast", GHands, DisplayDefaults.HandsContrast, 0.5f, 1.6f);
        /// <summary>How dark the hands' outlines are (1 = as designed; up past 1.6 the darkest reach black).</summary>
        public static readonly DisplayPref.Float HandsOutlineDark = new("hands.outline.dark", GHands, DisplayDefaults.HandsOutlineDark, 0.5f, 2.5f);

        // ---- tools & weapons: what the hands hold, with looks apart from the hands' (PostFx.cs: a second value in the mask) ----
        const string GTools = "TOOLS AND WEAPONS";
        public static readonly DisplayPref.Bool ToolsOwn = new("tools.own", GTools, DisplayDefaults.ToolsOwn);
        public static readonly DisplayPref.Bool ToolsOutline = new("tools.outline", GTools, DisplayDefaults.ToolsOutline);
        public static readonly DisplayPref.Float ToolsOutlineStrength = new("tools.outline.strength", GTools, DisplayDefaults.ToolsOutlineStrength, 0f, 1f);
        public static readonly DisplayPref.Float ToolsOutlineDark = new("tools.outline.dark", GTools, DisplayDefaults.ToolsOutlineDark, 0.5f, 2.5f);
        public static readonly DisplayPref.Bool ToolsCel = new("tools.cel", GTools, DisplayDefaults.ToolsCel);
        public static readonly DisplayPref.Float ToolsCelStrength = new("tools.cel.strength", GTools, DisplayDefaults.ToolsCelStrength, 0f, 1f);
        public static readonly DisplayPref.Float ToolsSaturation = new("tools.saturation", GTools, DisplayDefaults.ToolsSaturation, 0f, 2f);
        public static readonly DisplayPref.Float ToolsContrast = new("tools.contrast", GTools, DisplayDefaults.ToolsContrast, 0.5f, 1.6f);

        /// <summary>Both the hands' and the tools' own looks back to the defaults.</summary>
        public static void ResetHandsLook(bool save = true) { ResetHandsOnly(save); ResetToolsLook(save); }

        public static void ResetHandsOnly(bool save = true)
        {
            HandsOwn.Set(DisplayDefaults.HandsOwn, save);
            HandsOutline.Set(DisplayDefaults.HandsOutline, save); HandsOutlineStrength.Set(DisplayDefaults.HandsOutlineStrength, save);
            HandsCel.Set(DisplayDefaults.HandsCel, save); HandsCelStrength.Set(DisplayDefaults.HandsCelStrength, save);
            HandsSaturation.Set(DisplayDefaults.HandsSaturation, save); HandsContrast.Set(DisplayDefaults.HandsContrast, save);
            HandsOutlineDark.Set(DisplayDefaults.HandsOutlineDark, save);
        }

        public static void ResetToolsLook(bool save = true)
        {
            ToolsOwn.Set(DisplayDefaults.ToolsOwn, save);
            ToolsOutline.Set(DisplayDefaults.ToolsOutline, save); ToolsOutlineStrength.Set(DisplayDefaults.ToolsOutlineStrength, save);
            ToolsOutlineDark.Set(DisplayDefaults.ToolsOutlineDark, save);
            ToolsCel.Set(DisplayDefaults.ToolsCel, save); ToolsCelStrength.Set(DisplayDefaults.ToolsCelStrength, save);
            ToolsSaturation.Set(DisplayDefaults.ToolsSaturation, save); ToolsContrast.Set(DisplayDefaults.ToolsContrast, save);
        }

        // ---- the main menu's cutscene (the UFO in space: MenuSpace.cs) ----
        const string GMenu = "MAIN MENU CUTSCENE";
        /// <summary>The main menu's cutscene gets the post processing below instead of the world's.</summary>
        public static readonly DisplayPref.Bool MenuOwn = new("menu.own", GMenu, true);
        public static readonly DisplayPref.Bool MenuBloom = new("menu.bloom", GMenu, true);
        public static readonly DisplayPref.Float MenuBloomStrength = new("menu.bloom.strength", GMenu, 1f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuVignette = new("menu.vignette", GMenu, true);
        public static readonly DisplayPref.Float MenuVignetteStrength = new("menu.vignette.strength", GMenu, 1f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuGrading = new("menu.grading", GMenu, true);
        public static readonly DisplayPref.Float MenuGradingStrength = new("menu.grading.strength", GMenu, 1f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuOutlines = new("menu.outlines", GMenu, true);
        public static readonly DisplayPref.Float MenuOutlinesStrength = new("menu.outlines.strength", GMenu, 0.8f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuCel = new("menu.cel", GMenu, true);
        public static readonly DisplayPref.Float MenuCelStrength = new("menu.cel.strength", GMenu, 0.75f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuGrain = new("menu.grain", GMenu, false);
        public static readonly DisplayPref.Float MenuGrainStrength = new("menu.grain.strength", GMenu, 1f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuChromatic = new("menu.chromatic", GMenu, true);
        public static readonly DisplayPref.Float MenuChromaticStrength = new("menu.chromatic.strength", GMenu, 1f, 0f, 1f);

        /// <summary>The menu cutscene's own post processing is in use right now (switched on, and it's showing).</summary>
        public static bool MenuPostNow => MenuOwn.Value && (MenuSpace.Showing || MatchIntro.MenuLook); // (the match intro uses it too)

        static float Amt(DisplayPref.Bool on, DisplayPref.Float s) => on.Value ? s.Value : 0f;
        /// <summary>Bloom / vignette / grading strength in use now (0 = off): the menu cutscene's own while it's on, else the world's.</summary>
        public static float BloomNow => MenuPostNow ? Amt(MenuBloom, MenuBloomStrength) : LobbyPostNow ? Amt(LobbyBloom, LobbyBloomStrength) : PostBloom ? PostBloomStrength : 0f;
        public static float VignetteNow => MenuPostNow ? Amt(MenuVignette, MenuVignetteStrength) : LobbyPostNow ? Amt(LobbyVignette, LobbyVignetteStrength) : PostVignette ? PostVignetteStrength : 0f;
        public static float GradingNow => MenuPostNow ? Amt(MenuGrading, MenuGradingStrength) : LobbyPostNow ? Amt(LobbyGrading, LobbyGradingStrength) : PostGrading ? PostGradingStrength : 0f;
        /// <summary>An extra look's strength in use now (0 = off): the menu cutscene has outlines, cel banding, film grain and
        /// chromatic aberration of its own (and none of the others).</summary>
        public static float ExtraNow(PostExtra e)
        {
            if (!MenuPostNow && LobbyPostNow) return LobbyExtra(e);
            if (!MenuPostNow) return PostExtraAmount(e);
            switch (e)
            {
                case PostExtra.Outlines: return Amt(MenuOutlines, MenuOutlinesStrength);
                case PostExtra.CelBanding: return Amt(MenuCel, MenuCelStrength);
                case PostExtra.FilmGrain: return Amt(MenuGrain, MenuGrainStrength);
                case PostExtra.Chromatic: return Amt(MenuChromatic, MenuChromaticStrength);
            }
            return 0f;
        }

        public static void ResetMenuPost(bool save = true)
        {
            DisplayPref.ResetAll(save, MenuOwn, MenuBloom, MenuBloomStrength, MenuVignette, MenuVignetteStrength, MenuGrading, MenuGradingStrength,
                MenuOutlines, MenuOutlinesStrength, MenuCel, MenuCelStrength, MenuGrain, MenuGrainStrength, MenuChromatic, MenuChromaticStrength);
        }

        // ---- notifications ----
        const string GNotif = "NOTIFICATIONS";
        /// <summary>The big notifications get the looks below (and skip the world's post processing).</summary>
        public static readonly DisplayPref.Bool NotifOwn = new("notif.own", GNotif, DisplayDefaults.NotifOwn);
        public static readonly DisplayPref.Bool NotifCel = new("notif.cel", GNotif, DisplayDefaults.NotifCel);
        public static readonly DisplayPref.Float NotifCelStrength = new("notif.cel.strength", GNotif, DisplayDefaults.NotifCelStrength, 0f, 1f);
        public static readonly DisplayPref.Bool NotifOutline = new("notif.outline", GNotif, DisplayDefaults.NotifOutline);
        public static readonly DisplayPref.Float NotifOutlineWidth = new("notif.outline.width", GNotif, DisplayDefaults.NotifOutlineWidth, UiOutlineWidthMin, UiOutlineWidthMax);
        public static readonly DisplayPref.Colour NotifOutlineColour = new("notif.outline.colour", GNotif, DisplayDefaults.Hex(DisplayDefaults.NotifOutlineColour));
        public static readonly DisplayPref.Float NotifOutlineOpacity = new("notif.outline.opacity", GNotif, DisplayDefaults.NotifOutlineOpacity, 0f, 1f);
        public static readonly DisplayPref.Bool NotifBloom = new("notif.bloom", GNotif, DisplayDefaults.NotifBloom);
        public static readonly DisplayPref.Float NotifBloomStrength = new("notif.bloom.strength", GNotif, DisplayDefaults.NotifBloomStrength, 0f, 1f);
        public static readonly DisplayPref.Float NotifSaturation = new("notif.saturation", GNotif, DisplayDefaults.NotifSaturation, 0f, 2f);
        public static readonly DisplayPref.Float NotifContrast = new("notif.contrast", GNotif, DisplayDefaults.NotifContrast, 0.5f, 1.6f);

        public static void ResetNotifLook(bool save = true)
        {
            NotifOwn.Set(DisplayDefaults.NotifOwn, save);
            NotifCel.Set(DisplayDefaults.NotifCel, save); NotifCelStrength.Set(DisplayDefaults.NotifCelStrength, save);
            NotifOutline.Set(DisplayDefaults.NotifOutline, save); NotifOutlineWidth.Set(DisplayDefaults.NotifOutlineWidth, save);
            NotifOutlineColour.Set(DisplayDefaults.Hex(DisplayDefaults.NotifOutlineColour), save); NotifOutlineOpacity.Set(DisplayDefaults.NotifOutlineOpacity, save);
            NotifBloom.Set(DisplayDefaults.NotifBloom, save); NotifBloomStrength.Set(DisplayDefaults.NotifBloomStrength, save);
            NotifSaturation.Set(DisplayDefaults.NotifSaturation, save); NotifContrast.Set(DisplayDefaults.NotifContrast, save);
        }
    }
}
