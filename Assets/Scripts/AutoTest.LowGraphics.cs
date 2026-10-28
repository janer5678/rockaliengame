using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Low graphics (LowGraphics.cs), run from the `modes` tests (AutoTest.Requests.cs): switching it on caps the shadow
    /// distance, grass and render scale the pipeline is given without changing the sliders; switching it off puts it all back.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator LowGraphicsTests()
        {
            bool was = GameSettings.LowGfx;
            float sd = GameSettings.ShadowDistance, gd = GameSettings.GrassDistance, gden = GameSettings.GrassDensity;
            float scale0 = UiLook.UrpRenderScale;
            GameSettings.SetLowGraphics(true, false);
            yield return null;
            Check(GameSettings.LowGfx && GameSettings.ShadowDistance == sd && GameSettings.GrassDistance == gd && GameSettings.GrassDensity == gden,
                $"Low graphics leaves the sliders alone (shadows {GameSettings.ShadowDistance} m, grass {GameSettings.GrassDistance} m at {GameSettings.GrassDensity:0.00})");
            Check(GameSettings.ShadowDistanceNow <= GameSettings.LowShadowDistance && GameSettings.GrassDistanceNow <= GameSettings.LowGrassDistance && GameSettings.GrassDensityNow <= GameSettings.LowGrassDensity,
                $"Low graphics caps what's drawn: shadows {GameSettings.ShadowDistanceNow} m, grass {GameSettings.GrassDistanceNow} m at {GameSettings.GrassDensityNow:0.00}");
            if (UiLook.UrpShadowDistance >= 0f)
                Check(UiLook.UrpShadowDistance <= GameSettings.LowShadowDistance + 0.01f && UiLook.UrpRenderScale <= GameSettings.LowRenderScale + 0.001f && UiLook.UrpMsaa <= 1,
                    $"the render pipeline has it: shadows {UiLook.UrpShadowDistance:0} m, render scale {UiLook.UrpRenderScale:0.00}, MSAA x{UiLook.UrpMsaa}");
            GameSettings.SetLowGraphics(false, false);
            yield return null;
            Check(!GameSettings.LowGfx && GameSettings.ShadowDistanceNow == sd && GameSettings.GrassDistanceNow == gd && GameSettings.GrassDensityNow == gden
                && (UiLook.UrpShadowDistance < 0f || (Mathf.Approximately(UiLook.UrpShadowDistance, sd) && Mathf.Approximately(UiLook.UrpRenderScale, scale0))),
                $"back on Normal everything's as it was (shadows {UiLook.UrpShadowDistance:0} m, render scale {UiLook.UrpRenderScale:0.00})");
            GameSettings.SetLowGraphics(was, false);
        }
    }
}
