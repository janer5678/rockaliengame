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
