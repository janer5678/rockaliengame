using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > POST PROCESSING, two more layers with looks of their own (both off to start with: they then
    /// look like everything else):
    /// HANDS AND TOOLS - your first-person hands and what they hold: their own outlines, cel banding, saturation and
    ///   contrast instead of the world's (PostFx.cs: the hands are drawn into a mask, and the Stylize pass uses these
    ///   numbers inside it).
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

        public static void ResetHandsLook(bool save = true)
        {
            HandsOwn.Set(DisplayDefaults.HandsOwn, save);
            HandsOutline.Set(DisplayDefaults.HandsOutline, save); HandsOutlineStrength.Set(DisplayDefaults.HandsOutlineStrength, save);
            HandsCel.Set(DisplayDefaults.HandsCel, save); HandsCelStrength.Set(DisplayDefaults.HandsCelStrength, save);
            HandsSaturation.Set(DisplayDefaults.HandsSaturation, save); HandsContrast.Set(DisplayDefaults.HandsContrast, save);
        }

        // ---- the main menu's cutscene (the UFO in space: MenuSpace.cs) ----
        const string GMenu = "MAIN MENU CUTSCENE";
        /// <summary>The main menu's cutscene gets the post processing below instead of the world's.</summary>
        public static readonly DisplayPref.Bool MenuOwn = new("menu.own", GMenu, false);
        public static readonly DisplayPref.Bool MenuBloom = new("menu.bloom", GMenu, true);
        public static readonly DisplayPref.Float MenuBloomStrength = new("menu.bloom.strength", GMenu, 0.6f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuVignette = new("menu.vignette", GMenu, true);
        public static readonly DisplayPref.Float MenuVignetteStrength = new("menu.vignette.strength", GMenu, 0.4f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuGrading = new("menu.grading", GMenu, true);
        public static readonly DisplayPref.Float MenuGradingStrength = new("menu.grading.strength", GMenu, 0.65f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuOutlines = new("menu.outlines", GMenu, true);
        public static readonly DisplayPref.Float MenuOutlinesStrength = new("menu.outlines.strength", GMenu, 0.45f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuCel = new("menu.cel", GMenu, false);
        public static readonly DisplayPref.Float MenuCelStrength = new("menu.cel.strength", GMenu, 0.5f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuGrain = new("menu.grain", GMenu, false);
        public static readonly DisplayPref.Float MenuGrainStrength = new("menu.grain.strength", GMenu, 0.3f, 0f, 1f);
        public static readonly DisplayPref.Bool MenuChromatic = new("menu.chromatic", GMenu, false);
        public static readonly DisplayPref.Float MenuChromaticStrength = new("menu.chromatic.strength", GMenu, 0.5f, 0f, 1f);

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
            MenuOwn.Set(false, save);
            MenuBloom.Set(true, save); MenuBloomStrength.Set(0.6f, save);
            MenuVignette.Set(true, save); MenuVignetteStrength.Set(0.4f, save);
            MenuGrading.Set(true, save); MenuGradingStrength.Set(0.65f, save);
            MenuOutlines.Set(true, save); MenuOutlinesStrength.Set(0.45f, save);
            MenuCel.Set(false, save); MenuCelStrength.Set(0.5f, save);
            MenuGrain.Set(false, save); MenuGrainStrength.Set(0.3f, save);
            MenuChromatic.Set(false, save); MenuChromaticStrength.Set(0.5f, save);
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
