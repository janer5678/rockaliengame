using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest looks -host -solo: Settings > Display's two layers with looks of their own (LayerLooks.cs) and the
        /// mouse icons. HANDS AND TOOLS on (strong cel shading, no colour, thick outlines): the hands go on their layer and
        /// the Stylize pass runs; NOTIFICATIONS on (thick red outlines, glow): a banner goes through its own layer; a held
        /// item's hint shows the left mouse button as its icon. Each photographed; nothing is saved.
        /// </summary>
        IEnumerator LooksRoutine(PlayerNet me, PlayerController pc)
        {
            yield return Hold(me, Item.Rock);
            GameSettings.SetPostFx(true, false);
            yield return Snap("looks_00_default");

            // the hands and tools with looks of their own
            GameSettings.HandsOwn.Set(true, false);
            GameSettings.HandsCel.Set(true, false); GameSettings.HandsCelStrength.Set(1f, false);
            GameSettings.HandsOutline.Set(true, false); GameSettings.HandsOutlineStrength.Set(1f, false);
            GameSettings.HandsSaturation.Set(0f, false);
            yield return new WaitForSeconds(0.5f);
            int onLayer = 0, all = 0;
            var vm = ViewModel.Last;
            if (vm != null && vm.Root != null)
                foreach (var r in vm.Root.GetComponentsInChildren<Renderer>()) { all++; if (r.gameObject.layer == PostFx.HandLayer) onLayer++; }
            Check(all > 0 && onLayer == all && PostFx.HandsOwnLook && PostFx.StylizeOn, $"HANDS AND TOOLS own look: the hands on their layer ({onLayer}/{all}) and the stylize pass on");
            yield return Snap("looks_01_hands_own_grey_cel");
            GameSettings.ResetHandsLook(false);

            // the notifications with looks of their own
            GameSettings.NotifOwn.Set(true, false);
            GameSettings.NotifOutline.Set(true, false); GameSettings.NotifOutlineWidth.Set(8f, false);
            GameSettings.NotifOutlineColour.Set(new Color(0.8f, 0.1f, 0.1f), false);
            GameSettings.NotifBloom.Set(true, false); GameSettings.NotifBloomStrength.Set(1f, false);
            Hud.Banner("TRADE STATION UNLOCKED", "Craft it in your bag - a test of the notifications' own look");
            yield return new WaitForSeconds(0.5f);
            Check(UiLook.NotifOwnLayer, "NOTIFICATIONS own look: the banner went through its own layer");
            yield return Snap("looks_02_notification_own");
            GameSettings.ResetNotifLook(false);
            yield return new WaitForSeconds(0.3f);
            Hud.Banner("AIRDROP INCOMING", "the usual look again");
            yield return Snap("looks_03_notification_default");

            // the animated banner (caught early, mid-bounce, and settled) and the end countdown
            yield return new WaitForSeconds(4.2f);
            Hud.Banner("TRADE STATION UNLOCKED", "Craft it in your bag (I) - the wall dropped");
            yield return new WaitForSeconds(0.12f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_05_banner_in.png"));
            yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_06_banner_settled.png"));
            yield return new WaitForSeconds(3.6f);
            Hud.Banner("AIRDROP INCOMING", "A crate is coming down by the north rocks");
            yield return new WaitForSeconds(0.9f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_07_banner_airdrop.png"));
            yield return new WaitForSeconds(3.4f);
            Hud.TestCountdown = 7;
            yield return new WaitForSeconds(Mathf.Repeat(-Time.time, 1f) + 0.08f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_08_countdown_slam.png"));
            yield return new WaitForSeconds(0.55f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_09_countdown_settled.png"));
            Hud.TestCountdown = 2;
            yield return new WaitForSeconds(Mathf.Repeat(-Time.time, 1f) + 0.15f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_10_countdown_last.png"));
            yield return new WaitForSeconds(0.3f);
            Hud.TestCountdown = -1;

            // the left mouse button as its icon in a held item's hint
            me.ServerGive(Item.C4, 1);
            yield return Hold(me, Item.C4);
            Check(Tutorial.WithMouseIcons("LMB: throw").IndexOf("LMB", System.StringComparison.Ordinal) < 0, "LMB in a hint turns into the mouse icon");
            yield return Snap("looks_04_lmb_icon_hint");

            yield return TimerAndNotifLooks(pc);
            Log("looks test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>
        /// Prompt 11: the timer's own look (its label above the clock, a custom phase colour), a reworded notification, the
        /// YOU ARE box switched off, the damage numbers' plain look, and Settings > Display in categories (a picture of
        /// each, the test notification shown over the settings). Nothing is saved.
        /// </summary>
        IEnumerator TimerAndNotifLooks(PlayerController pc)
        {
            // ---- the timer: its label above the clock, every phase in magenta ----
            var magenta = new Color(1f, 0f, 1f);
            GameSettings.TimerOwn.Set(true, false);
            GameSettings.TimerLabelPos.Set(1, false);
            foreach (var (_, pref) in GameSettings.TimerColours) pref.Set(magenta, false);
            yield return new WaitForSeconds(0.4f);
            Check(Time.frameCount - Hud.TopPanelFrame <= 3 && Hud.TopPanelLabelAbove, $"TIMER: the label sits above the clock (drawn {Time.frameCount - Hud.TopPanelFrame} frames ago, above {Hud.TopPanelLabelAbove})");
            Check(ColorSlots.Same(Hud.TopPanelAccent, magenta), $"TIMER: a phase colour of its own (#{ColorUtility.ToHtmlStringRGB(Hud.TopPanelAccent)}, wanted #FF00FF)");
            yield return Snap("looks_11_timer_label_above_magenta");
            // the bar's grey ends and the top line off, a wider plate, another font, thick ink, lower down
            GameSettings.TimerBarBack.Set(false, false); GameSettings.TimerTopLine.Set(false, false);
            GameSettings.TimerWidth.Set(1.5f, false); GameSettings.TimerInk.Set(2.5f, false); GameSettings.TimerY.Set(60f, false);
            GameSettings.TimerFont.Set(System.Array.IndexOf(GameSettings.FontPrefNames, "Impact"), false);
            yield return new WaitForSeconds(0.3f);
            yield return Snap("looks_12_timer_trimmed");
            GameSettings.ResetTimer(false);
            yield return new WaitForSeconds(0.2f);
            Check(!Hud.TopPanelLabelAbove && !ColorSlots.Same(Hud.TopPanelAccent, magenta), "TIMER: Defaults put the label left of the clock and the phase colours back");

            // ---- a reworded notification ----
            var airdrop = NotifText.Find("airdrop");
            var goal = NotifText.Find("goal");
            airdrop.Own.Set("CRATE FROM THE SKY", false);
            goal.Own.Set("{0} SCORES!", false);
            Check(NotifText.Map("GOAL! BLUE") == "BLUE SCORES!" && NotifText.Map("TRADE STATION UNLOCKED") == "TRADE STATION UNLOCKED",
                $"WORDING: a title with a changing part keeps it ({NotifText.Map("GOAL! BLUE")}), the others keep the game's words");
            Hud.Banner("AIRDROP INCOMING", "A crate is coming down by the north rocks");
            yield return new WaitForSeconds(0.8f);
            Check(Hud.LastBannerShown == "CRATE FROM THE SKY" && Time.frameCount - Hud.BannerShownFrame <= 3,
                $"WORDING: the reworded banner shows the player's words ({Hud.LastBannerShown})");
            yield return Snap("looks_13_notification_reworded");
            // its style: bigger, Impact, no outline, a wide band, lower
            GameSettings.NotifSize.Set(1.3f, false); GameSettings.NotifInk.Set(0f, false); GameSettings.NotifWidth.Set(1.6f, false); GameSettings.NotifY.Set(80f, false);
            GameSettings.NotifFont.Set(System.Array.IndexOf(GameSettings.FontPrefNames, "Impact"), false);
            yield return new WaitForSeconds(0.1f);
            yield return Snap("looks_14_notification_styled");
            GameSettings.ResetNotifStyle(false);
            NotifText.ResetAll(false);
            yield return new WaitForSeconds(3.5f);

            // ---- the YOU ARE box off and on ----
            GameSettings.HudTeamBox.Set(false, false);
            yield return new WaitForSeconds(0.3f);
            Check(Time.frameCount - Hud.TeamBoxShownFrame > 5, "HUD: the YOU ARE box can be switched off");
            yield return Snap("looks_15_no_team_box");
            GameSettings.HudTeamBox.Set(true, false);
            yield return new WaitForSeconds(0.3f);
            Check(Time.frameCount - Hud.TeamBoxShownFrame <= 3, "HUD: ...and back on");

            // ---- the damage numbers: the old plain look (a drop shadow, no thick outline), far and up close ----
            var cam = Camera.main;
            if (cam != null)
            {
                Fx.DamageNumber(cam.transform.position + cam.transform.forward * 8f + cam.transform.right * 1.5f, 34f, false);
                Fx.DamageNumber(cam.transform.position + cam.transform.forward * 8f - cam.transform.right * 1.5f, 61f, true);
                Fx.DamageNumber(cam.transform.position + cam.transform.forward * 2f, 18f, false); // (a melee hit: nudged off the crosshair)
                yield return new WaitForSeconds(0.25f);
                yield return Snap("looks_16_damage_numbers_plain");
            }

            // ---- Settings > Display in categories: a picture of each ----
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < Hud.DisplayCatNames.Length; i++)
            {
                Hud.OpenDisplayCategory(i);
                yield return new WaitForSeconds(0.35f);
                Check(Hud.DisplayCategoryShown == i, $"Settings > Display > {Hud.DisplayCatNames[i]}: shown as its own category ({Hud.DisplayCategoryShown}; -1 = Hud.Menus.cs DrawDisplayTab doesn't call DrawDisplayTabbed)");
                yield return Snap($"looks_2{i}_settings_{Hud.DisplayCatNames[i].ToLowerInvariant().Replace(' ', '_').Replace("&", "and")}");
            }
            // the test notification and the test countdown over the settings
            Hud.OpenDisplayCategory((int)Hud.DisplayCat.Notifications);
            yield return new WaitForSeconds(0.2f);
            string tested = Hud.TestNotification();
            yield return new WaitForSeconds(0.7f);
            Check(Time.frameCount - Hud.BannerShownFrame <= 3, $"TEST NOTIFICATION shows a banner ({tested}) over the settings");
            yield return Snap("looks_30_test_notification_over_settings");
            Hud.TestCountdownNow();
            yield return new WaitForSeconds(1.3f);
            Check(Time.frameCount - Hud.CountdownShownFrame <= 3, "TEST COUNTDOWN shows the end countdown over the settings");
            yield return Snap("looks_31_test_countdown_over_settings");
            yield return new WaitForSeconds(4f);
            Hud.OpenDisplayCategory(0);
            pc.Paused = false;
            yield return new WaitForSeconds(0.3f);
        }
    }
}
