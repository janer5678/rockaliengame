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
            yield return Prompt12Looks(me, pc);
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

        /// <summary>
        /// Prompt 12: keys as keycap icons in the hints, the notifications' own text outline (colour, on / off), the timer's
        /// drain bar on top, the base radar moving into the corner without the team box (and switched off), the kill feed's
        /// looks, the colour wheel (opened on a timer colour, dragged, undone; it closes with the settings), and the ball's
        /// tip under the crosshair while carrying it. Nothing is saved.
        /// </summary>
        IEnumerator Prompt12Looks(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;

            // ---- keys as icons ----
            string a = Tutorial.WithKeyIcons("[E]: open"), b = Tutorial.WithKeyIcons("hold E to pick up"), c = Tutorial.WithKeyIcons("RMB / Esc to cancel");
            string plain = "Every Enemy: TIME 1:30 - Assassin: hand in";
            Check(Tutorial.HasIcons(a) && a.IndexOf("[E]", System.StringComparison.Ordinal) < 0, "KEY ICONS: [E] turns into its keycap");
            Check(Tutorial.HasIcons(b) && Tutorial.HasIcons(c) && c.IndexOf("\u0001Esc\u0002", System.StringComparison.Ordinal) >= 0 /* (Esc as its keycap token) */ && c.IndexOf("RMB", System.StringComparison.Ordinal) < 0,
                "KEY ICONS: hold E, Esc and RMB turn into icons");
            Check(Tutorial.WithKeyIcons(plain) == plain, $"KEY ICONS: ordinary words and a clock are left alone ({Tutorial.PlainKeys(Tutorial.WithKeyIcons(plain))})");
            Check(Tutorial.KeyTag(Bind.Interact) == "[" + Binds.Name(Bind.Interact) + "]", $"KEY ICONS: hints write the interact key as {Tutorial.KeyTag(Bind.Interact)}");
            me.ServerGive(Item.Spear, 1);
            yield return Hold(me, Item.Spear);
            yield return new WaitForSeconds(0.3f);
            yield return Snap("looks_40_key_icons_spear_hint");

            // ---- the notifications' own text outline: red and thick, then off ----
            var red = new Color(1f, 0f, 0f);
            GameSettings.NotifInkColour.Set(red, false); GameSettings.NotifInk.Set(2.5f, false);
            Hud.Banner("AIRDROP INCOMING", "a red text outline");
            yield return new WaitForSeconds(0.7f);
            var edge = Hud.BannerInkEdge; edge.a = 1f;
            Check(ColorSlots.Same(edge, red) && Hud.BannerInkPx > 1f, $"TEXT OUTLINE: the banner's own stroke takes the colour and thickness (#{ColorUtility.ToHtmlStringRGB(edge)}, {Hud.BannerInkPx:0.0} px)");
            yield return Snap("looks_41_notification_red_text_outline");
            GameSettings.NotifInkOn.Set(false, false);
            yield return new WaitForSeconds(0.2f);
            Check(Hud.BannerInkPx < 0.01f, $"TEXT OUTLINE: switched off, the banner has no stroke ({Hud.BannerInkPx:0.0} px)");
            GameSettings.ResetNotifStyle(false);
            yield return new WaitForSeconds(3.5f);

            // ---- the timer's drain bar on top ----
            GameSettings.TimerOwn.Set(true, false);
            GameSettings.TimerBarPos.Set(1, false);
            yield return new WaitForSeconds(0.3f);
            if (g != null && g.S != GameState.Waiting) Check(Hud.TopPanelBarOnTop, "TIMER: the drain bar can go on top of the timer");
            else Log("TIMER bar on top: skipped (no drain bar while waiting for players)");
            yield return Snap("looks_42_timer_bar_on_top");
            GameSettings.ResetTimer(false);

            // ---- the base radar: out of our base it shows; with no team box it's up in the corner; it can be hidden ----
            var spot = Vector3.Lerp(Cfg.BaseCenter[team], Vector3.zero, 0.55f);
            spot.y = MapBuilder.Height(spot.x, spot.z) + 0.1f;
            pc.LocalTeleport(spot, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.5f);
            Check(Time.frameCount - Hud.RadarShownFrame <= 3 && Hud.RadarRect.y > 30f, $"RADAR: under the team box out of our base (y {Hud.RadarRect.y:0})");
            GameSettings.HudTeamBox.Set(false, false);
            yield return new WaitForSeconds(0.3f);
            Check(Time.frameCount - Hud.RadarShownFrame <= 3 && Hud.RadarRect.y < 12f, $"RADAR: no team box - up in the corner (y {Hud.RadarRect.y:0})");
            yield return Snap("looks_43_radar_in_the_corner");
            GameSettings.HudBaseRadar.Set(false, false);
            yield return new WaitForSeconds(0.3f);
            Check(Time.frameCount - Hud.RadarShownFrame > 5, "RADAR: can be switched off");
            yield return Snap("looks_44_no_team_box_no_radar");
            GameSettings.HudTeamBox.Set(true, false); GameSettings.HudBaseRadar.Set(true, false);

            // ---- the kill feed's looks ----
            Hud.ClearKills();
            Hud.TestKillFeed();
            yield return new WaitForSeconds(0.6f);
            Check(Time.frameCount - Hud.KillFeedShownFrame <= 3 && Hud.KillFeedShownLines == 4, $"KILL FEED: the test lines show ({Hud.KillFeedShownLines})");
            float row = Hud.KillFeedRowH;
            yield return Snap("looks_45_kill_feed_default");
            GameSettings.KillFeedLines.Set(2f, false); GameSettings.KillFeedSize.Set(1.5f, false); GameSettings.KillFeedIcons.Set(false, false);
            GameSettings.KillFeedTeamColours.Set(false, false); GameSettings.KillFeedBack.Set(0.2f, false); GameSettings.KillFeedX.Set(80f, false); GameSettings.KillFeedY.Set(60f, false);
            yield return new WaitForSeconds(0.3f);
            Check(Hud.KillFeedShownLines == 2 && Hud.KillFeedRowH > row * 1.4f, $"KILL FEED: at most 2 lines, bigger ({Hud.KillFeedShownLines} lines, {Hud.KillFeedRowH:0} px rows, was {row:0})");
            yield return Snap("looks_46_kill_feed_styled");
            GameSettings.KillFeedOn.Set(false, false);
            yield return new WaitForSeconds(0.3f);
            Check(Time.frameCount - Hud.KillFeedShownFrame > 5, "KILL FEED: can be switched off");
            GameSettings.ResetKillFeed(false);
            Hud.ClearKills();

            // ---- the colour wheel, on a timer phase colour in Settings > Display > HUD & TIMER ----
            GameSettings.TimerOwn.Set(true, false);
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.4f);
            Hud.OpenDisplayCategory((int)Hud.DisplayCat.HudTimer);
            Hud.SetSettingsScroll(100000f); // (down to the TIMER section)
            yield return new WaitForSeconds(0.3f);
            var pref = GameSettings.TimerColBallLive;
            var before = pref.Value;
            Hud.OpenColourWheel(pref, "Time left");
            yield return new WaitForSeconds(0.4f);
            Check(Hud.ColourWheelOpen && Time.frameCount - Hud.ColourWheelShownFrame <= 3, "COLOUR WHEEL: opens over the settings and stays open while its colour square is drawn");
            Hud.TestColourWheelPick(0.75f, 0.9f, 1f, false);
            yield return new WaitForSeconds(0.2f);
            Check(ColorSlots.Same(pref.Value, Color.HSVToRGB(0.75f, 0.9f, 1f)), $"COLOUR WHEEL: dragging puts the colour in live (#{ColorUtility.ToHtmlStringRGB(pref.Value)})");
            yield return Snap("looks_47_colour_wheel");
            Hud.CloseColourWheel(false);
            yield return new WaitForSeconds(0.2f);
            Check(!Hud.ColourWheelOpen && ColorSlots.Same(pref.Value, before), "COLOUR WHEEL: Undo puts the colour from before back");
            Hud.OpenColourWheel(pref, "Time left");
            yield return new WaitForSeconds(0.2f);
            pc.Paused = false;
            yield return new WaitForSeconds(0.5f);
            Check(!Hud.ColourWheelOpen, "COLOUR WHEEL: closes by itself when the settings close");
            pref.Set(before, false);
            GameSettings.ResetTimer(false);
            Hud.OpenDisplayCategory(0);

            // ---- the ball's tip under the crosshair while carrying it ----
            Check(PlayerController.BallCarryTip(false).Contains("Carrying the ball") && Tutorial.HasIcons(Tutorial.WithKeyIcons(PlayerController.BallCarryTip(false))),
                $"BALL TIP: says what the mouse does with it ({PlayerController.BallCarryTip(false)})");
            if (g != null && !Cfg.Builder)
            {
                g.TimerPaused.Value = true;
                g.DevSkipPhase(GameState.PreBall);
                float until = Time.time + 12f;
                while ((g.S != GameState.BallLive || Ball.Instance == null || g.WallUp) && Time.time < until) yield return null;
                yield return new WaitForSeconds(3f);
                var ball = Ball.Instance;
                if (ball != null && g.S == GameState.BallLive && !g.WallUp)
                {
                    var bp = ball.transform.position;
                    pc.LocalTeleport(new Vector3(bp.x + 1.5f, MapBuilder.Height(bp.x + 1.5f, bp.z) + 0.1f, bp.z), 270f);
                    yield return new WaitForSeconds(0.5f);
                    me.PickupBallRpc();
                    yield return new WaitForSeconds(0.8f);
                    Check(me.CarryingBall && pc.AimText.Contains("Carrying the ball"), $"BALL TIP: carrying the ball, the tip is under the crosshair (\"{pc.AimText}\")");
                    yield return Snap("looks_48_ball_carry_tip");
                }
                else Log("BALL TIP in play: skipped (the ball didn't come out in time)");
            }
        }
    }
}
