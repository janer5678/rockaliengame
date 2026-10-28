using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// LOW GRAPHICS (Settings > Display > Graphics > Quality: Low): the game's look, drawn as cheaply as it can be. It caps
    /// what the other display settings ask for without touching them (back on Normal, every slider is as you left it):
    /// a 75% render scale (upscaled to the window), no MSAA, shadows no further than 30 m in one cascade from a 1024 shadow
    /// map, grass out to 60 m at half density, no ambient occlusion or depth of field, and textures one mip smaller.
    /// The outlines, cel banding, haze, the alien glow, the colours, the hands' and tools' looks and everything else stay.
    /// Applied by UiLook.ApplyShadows (the pipeline asset: shadows, render scale, MSAA, the mip limit), GrassField
    /// (GrassDistanceNow / GrassDensityNow) and PostFx.Apply (AO, DoF) - all of them listen for DisplayPref.Changed.
    /// Saved on this PC (RockGame.Disp.gfx.low) and in the Share code (gfx.low).
    /// </summary>
    public static partial class GameSettings
    {
        public static readonly DisplayPref.Bool LowGraphics = new("gfx.low", "GRAPHICS", false);

        public const float LowShadowDistance = 30f, LowGrassDistance = 60f, LowGrassDensity = 0.5f, LowRenderScale = 0.75f;
        public const int LowShadowCascades = 1, LowShadowMapSize = 1024;

        /// <summary>Low graphics on (the caps below apply).</summary>
        public static bool LowGfx => LowGraphics.Value;
        /// <summary>The shadow distance to draw with: the slider's, capped on Low.</summary>
        public static float ShadowDistanceNow => LowGfx ? Mathf.Min(ShadowDistance, LowShadowDistance) : ShadowDistance;
        /// <summary>How far the grass goes: the slider's, capped on Low.</summary>
        public static float GrassDistanceNow => LowGfx ? Mathf.Min(GrassDistance, LowGrassDistance) : GrassDistance;
        /// <summary>How thick the grass is: the slider's, capped on Low.</summary>
        public static float GrassDensityNow => LowGfx ? Mathf.Min(GrassDensity, LowGrassDensity) : GrassDensity;

        /// <summary>Normal (false) or Low (true) graphics. Everything that draws re-applies itself off DisplayPref.Changed.</summary>
        public static void SetLowGraphics(bool on, bool save = true) => LowGraphics.Set(on, save);
    }
}
