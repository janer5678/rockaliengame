using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest looks (prompt 13): each kind of notification's own colour; the banners' way in and out, how long they
        /// stay and how far they fly; the timer smaller, its words reworded (apart from the banners'), the mode tag below
        /// the bar / above the label, the drain bar's own looks; the kill feed's and the base radar's font and outline; thin
        /// damage number outlines; the countdowns' own look apart from the banners'; the hands and the tools with looks
        /// apart (and outlines down to black); the scoreboard leaving the mouse to the game; the bag's help with mouse and
        /// key icons; the death screen's own look (its outline down to none). Each photographed; nothing is saved.
        /// </summary>
        IEnumerator Prompt13Looks(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            float k = Hud.UiK;

            // ---- each kind of news in its own colour ----
            var green = new Color(0f, 1f, 0f);
            GameSettings.NotifColAirdrop.Set(green, false);
            Check(ColorSlots.Same(GameSettings.NotifAccentFor("AIRDROP INCOMING"), green) && !ColorSlots.Same(GameSettings.NotifAccentFor("TRADE STATION UNLOCKED"), green),
                "NOTIFICATION COLOURS: an airdrop takes its own colour, an unlock keeps its own");
            Hud.Banner("AIRDROP INCOMING", "a green airdrop");
            yield return new WaitForSeconds(0.8f);
            Check(Time.frameCount - Hud.BannerShownFrame <= 3 && ColorSlots.Same(Hud.BannerAccentShown, green), $"NOTIFICATION COLOURS: the banner is drawn in it (#{ColorUtility.ToHtmlStringRGB(Hud.BannerAccentShown)})");
            yield return Snap("looks_50_notification_green_airdrop");
            GameSettings.ResetNotifColours(false);
            yield return new WaitForSeconds(3.4f);

            // ---- the way in and out: slides down, stays 2.5 s, slides up 300 px ----
            GameSettings.NotifEnter.Set(1, false); GameSettings.NotifExit.Set(2, false);
            GameSettings.NotifTime.Set(2.5f, false); GameSettings.NotifFly.Set(300f, false);
            Hud.Banner("TRADE STATION UNLOCKED", "slides down");
            yield return new WaitForSeconds(0.1f);
            Check(Hud.BannerLiftShown < -1f, $"ANIMATION: Slide down - it starts above where it sits ({Hud.BannerLiftShown:0} px)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_51_banner_slide_down.png"));
            yield return new WaitForSeconds(2.15f);
            Check(Hud.BannerLiftShown > 30f, $"ANIMATION: Slide up - on its way out it flies up ({Hud.BannerLiftShown:0} px of 300)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_52_banner_slide_up.png"));
            yield return new WaitForSeconds(0.6f);
            Check(Time.frameCount - Hud.BannerShownFrame > 5, "ANIMATION: it's gone after the 2.5 s it was set to stay");
            // pops in, shrinks away
            GameSettings.NotifEnter.Set(3, false); GameSettings.NotifExit.Set(3, false);
            Hud.Banner("AIRDROP INCOMING", "pops in");
            yield return new WaitForSeconds(0.2f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_53_banner_pop.png"));
            yield return new WaitForSeconds(2.1f);
            Check(Hud.BannerScaleShown < 0.95f, $"ANIMATION: Shrink - it shrinks away ({Hud.BannerScaleShown:0.00})");
            yield return new WaitForSeconds(0.6f);
            GameSettings.ResetNotifMove(false);

            // ---- the timer: smaller than before, reworded, its tag under the bar, the bar's own looks ----
            GameSettings.TimerOwn.Set(true, false);
            GameSettings.TimerSize.Set(0.3f, false);
            Check(Mathf.Abs(GameSettings.TimerSize.Value - 0.3f) < 0.001f, $"TIMER: it can be 30% ({GameSettings.TimerSize.Value * 100f:0}%)");
            yield return new WaitForSeconds(0.2f);
            yield return Snap("looks_54_timer_30pc");
            GameSettings.TimerSize.Set(1f, false);
            var timeLeft = NotifText.Find("timer_timeleft");
            var wall = NotifText.Find("timer_wall");
            var overtimeBanner = NotifText.Find("overtime");
            timeLeft.Own.Set("HURRY UP", false); wall.Own.Set("BUILD TIME", false); overtimeBanner.Own.Set("EXTRA TIME", false);
            Check(NotifText.MapTimer("TIME LEFT") == "HURRY UP" && NotifText.MapTimer("WALL DROPS IN") == "BUILD TIME" && NotifText.MapTimer("OVERTIME") == "OVERTIME"
                && NotifText.Map("OVERTIME") == "EXTRA TIME", "TIMER WORDING: the timer's headers take their own words, apart from the banners'");
            Hud.TestTimerTag = "ROCK RUSH";
            // (the tag is off by default now, and the plate half as wide: the tag on, the plate at its full width)
            GameSettings.TimerTag.Set(true, false); GameSettings.TimerWidth.Set(1f, false);
            GameSettings.TimerTagPos.Set(1, false);
            GameSettings.TimerBarHeight.Set(3f, false); GameSettings.TimerBarRound.Set(true, false); GameSettings.TimerBarDrain.Set(1, false);
            GameSettings.TimerBarOwnColour.Set(true, false); GameSettings.TimerBarColour.Set(new Color(0.2f, 1f, 0.9f), false);
            GameSettings.TimerBarWidth.Set(0.6f, false); GameSettings.TimerBarGlow.Set(false, false); GameSettings.TimerBarBackOpacity.Set(0.9f, false);
            yield return new WaitForSeconds(0.3f);
            var tagBelow = Hud.TopPanelTagRect;
            if (g != null && g.S != GameState.Waiting)
            {
                var fill = Hud.TopPanelBarFill;
                Check(tagBelow.height > 0f && tagBelow.y > fill.y, $"TIMER: the mode tag sits below the bar (tag y {tagBelow.y:0}, bar y {fill.y:0})");
                float barLeft = Screen.width / 2f - 600f * 0.82f * k * 0.84f * 0.6f / 2f;
                Check(Mathf.Abs(fill.x - barLeft) < 6f * k && fill.height > 8f * k, $"TIMER: the bar drains from the right (its left end held: x {fill.x:0} / {barLeft:0}) and is thicker ({fill.height:0.0} px)");
            }
            else Log("TIMER bar looks: skipped (no drain bar while waiting for players)");
            yield return Snap("looks_55_timer_reworded_tag_below_bar_styled");
            GameSettings.TimerTagPos.Set(2, false);
            yield return new WaitForSeconds(0.2f);
            Check(Hud.TopPanelTagRect.height > 0f && Hud.TopPanelTagRect.y < tagBelow.y - 20f, $"TIMER: the mode tag can go above the label (y {Hud.TopPanelTagRect.y:0}, was {tagBelow.y:0})");
            yield return Snap("looks_56_timer_tag_above");
            Hud.TestTimerTag = null;
            GameSettings.ResetTimer(false);
            NotifText.ResetAll(false);

            // ---- the kill feed's font and outline ----
            Hud.ClearKills();
            GameSettings.KillFeedEdge.Set(1, false); GameSettings.KillFeedInk.Set(3f, false); GameSettings.KillFeedInkColour.Set(new Color(0.6f, 0f, 0f), false);
            GameSettings.KillFeedFont.Set(System.Array.IndexOf(GameSettings.FontPrefNames, "Impact"), false);
            Hud.TestKillFeed();
            yield return new WaitForSeconds(0.6f);
            Check(Time.frameCount - Hud.KillFeedShownFrame <= 3 && Hud.KillFeedEdgeShown == 1 && Hud.KillFeedInkShown > 2f, $"KILL FEED: names outlined ({Hud.KillFeedEdgeShown}, {Hud.KillFeedInkShown:0.0} px)");
            yield return Snap("looks_57_kill_feed_outline_font");
            GameSettings.ResetKillFeed(false);
            Hud.ClearKills();

            // ---- the base radar's font and outline (out of our base) ----
            var spot = Vector3.Lerp(Cfg.BaseCenter[team], Vector3.zero, 0.55f);
            spot.y = MapBuilder.Height(spot.x, spot.z) + 0.1f;
            pc.LocalTeleport(spot, Cfg.SpawnYaw(team));
            GameSettings.RadarEdge.Set(1, false); GameSettings.RadarInk.Set(2f, false);
            GameSettings.RadarFont.Set(System.Array.IndexOf(GameSettings.FontPrefNames, "Impact"), false);
            yield return new WaitForSeconds(0.5f);
            if (Time.frameCount - Hud.RadarShownFrame <= 3) Check(Hud.RadarEdgeShown == 1 && Hud.RadarInkShown > 1.5f, $"RADAR: outlined ({Hud.RadarEdgeShown}, {Hud.RadarInkShown:0.0} px)");
            else Log("RADAR outline: skipped (the radar isn't showing here)");
            yield return Snap("looks_58_radar_outline_font");
            GameSettings.ResetRadar(false);

            // ---- thinner damage number outlines ----
            GameSettings.DamageEdge.Set(1, false); GameSettings.DamageInk.Set(0.5f, false);
            Hud.TestDamageNumbers();
            yield return new WaitForSeconds(0.25f);
            Check(Hud.DamageEdgeShown == 1 && Hud.DamageInkShown < 1f * k + 0.01f, $"DAMAGE NUMBERS: a thin outline ({Hud.DamageEdgeShown}, {Hud.DamageInkShown:0.00} px)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_59_damage_numbers_thin_outline.png"));
            yield return new WaitForSeconds(0.3f);
            GameSettings.ResetDamageNumbers(false);
            Check(GameSettings.DamageInk.Value < 2f, $"DAMAGE NUMBERS: thinner than the old 2 px shadow by default ({GameSettings.DamageInk.Value:0.##} px)");

            // ---- the countdowns: their own look, apart from the banners' ----
            GameSettings.NotifInkOn.Set(false, false); GameSettings.NotifSize.Set(1.8f, false); // (the banners' style: not the countdown's)
            var magenta = new Color(1f, 0f, 1f);
            GameSettings.CountColour.Set(magenta, false); GameSettings.CountSize.Set(0.7f, false); GameSettings.CountInk.Set(0.4f, false);
            GameSettings.CountY.Set(-60f, false);
            Hud.TestCountdown = 7;
            yield return new WaitForSeconds(0.6f);
            Check(Time.frameCount - Hud.CountdownShownFrame <= 3 && Hud.CountInkPx > 0.1f && Hud.CountInkPx < 2.5f * k,
                $"COUNTDOWN: its own outline, not the banners' (off there; {Hud.CountInkPx:0.0} px here)");
            yield return Snap("looks_60_countdown_own_look");
            Hud.TestCountdown = -1;
            GameSettings.ResetNotifStyle(false);
            var blue = new Color(0.3f, 0.6f, 1f);
            GameSettings.CountColReady.Set(blue, false);
            Hud.TestFightWords();
            yield return new WaitForSeconds(0.4f);
            var fc = Hud.FightWordColour; fc.a = 1f;
            Check(Time.frameCount - Hud.FightWordShownFrame <= 3 && Hud.FightWordShown == "Ready?" && ColorSlots.Same(fc, blue),
                $"COUNTDOWN: Ready? in its own colour ({Hud.FightWordShown}, #{ColorUtility.ToHtmlStringRGB(fc)})");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_61_ready_own_colour.png"));
            yield return new WaitForSeconds(2.75f);
            Check(Time.frameCount - Hud.FightWordShownFrame <= 3 && Hud.FightWordShown == "ROCK!", $"COUNTDOWN: then ROCK! ({Hud.FightWordShown})");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "looks_62_rock.png"));
            yield return new WaitForSeconds(1.2f);
            GameSettings.ResetCountdown(false);

            // ---- the hands and the tools apart, outlines down to black ----
            me.ServerGive(Item.Hatchet, 1);
            yield return Hold(me, Item.Hatchet);
            GameSettings.HandsOwn.Set(true, false); GameSettings.HandsSaturation.Set(0f, false); GameSettings.HandsOutline.Set(true, false); GameSettings.HandsOutlineStrength.Set(0.5f, false);
            GameSettings.ToolsOwn.Set(true, false); GameSettings.ToolsSaturation.Set(2f, false); GameSettings.ToolsOutline.Set(true, false); GameSettings.ToolsOutlineStrength.Set(1f, false);
            GameSettings.ToolsOutlineDark.Set(2.5f, false);
            yield return new WaitForSeconds(0.5f);
            int hands = 0, tools = 0;
            var vm = ViewModel.Last;
            if (vm != null && vm.Root != null)
                foreach (var r in vm.Root.GetComponentsInChildren<Renderer>())
                {
                    if (r.gameObject.layer == PostFx.HandLayer && vm.IsHand(r)) hands++;
                    if (r.gameObject.layer == PostFx.ToolLayer && !vm.IsHand(r)) tools++;
                }
            Check(hands > 0 && tools > 0, $"HANDS / TOOLS: the hands and what they hold on layers of their own ({hands} hand parts, {tools} tool parts)");
            Check(PostFx.HandLookNow.x > 0f && PostFx.ToolLookNow.x > 0f && Mathf.Abs(PostFx.HandLookNow.z - PostFx.ToolLookNow.z) > 1f,
                $"HANDS / TOOLS: separate looks (saturation {PostFx.HandLookNow.z:0.0} / {PostFx.ToolLookNow.z:0.0})");
            Check(PostFx.ToolOutlineNow.x * 0.78f >= 1f, $"HANDS / TOOLS: the tools' outlines can reach black (darkness {PostFx.ToolOutlineNow.x * 0.78f:0.00})");
            yield return Snap("looks_63_hands_grey_tools_vivid_black_outlines");
            GameSettings.ToolsOwn.Set(false, false);
            yield return new WaitForSeconds(0.2f);
            Check(PostFx.ToolLookNow.x <= 0f && PostFx.HandLookNow.x > 0f, "HANDS / TOOLS: the tools' own look off, the hands keep theirs");
            GameSettings.ResetHandsLook(false);

            // ---- the scoreboard leaves the mouse to the game until a right-click ----
            Hud.TestScoreboard = true;
            yield return new WaitForSeconds(0.3f);
            Check(!Hud.ScoreboardMouse, "SCOREBOARD: it doesn't ask for the mouse (look and move as usual)");
            yield return Snap("looks_64_scoreboard_mouse_stays");
            Hud.TestScoreboardMouse(true);
            Check(Hud.ScoreboardMouse, "SCOREBOARD: a right-click asks for the mouse (its MESSAGE buttons)");
            Hud.TestScoreboard = false;
            yield return new WaitForSeconds(0.2f);
            Check(!Hud.ScoreboardMouse, "SCOREBOARD: put away, the mouse goes back to the game");

            // ---- the bag's help: mouse and key icons ----
            string help = Tutorial.WithKeyIcons(Hud.InvHelpShown);
            Check(Tutorial.HasIcons(help) && help.IndexOf("LMB", System.StringComparison.Ordinal) < 0 && help.IndexOf("RMB", System.StringComparison.Ordinal) < 0
                && help.IndexOf("\u0001Shift\u0002", System.StringComparison.Ordinal) >= 0, $"BAG: its help lines lead with mouse and key icons ({Tutorial.PlainKeys(help).Replace('\n', '|')})");
            pc.MenuOpen = true;
            yield return new WaitForSeconds(0.4f);
            yield return Snap("looks_65_bag_help_icons");
            pc.MenuOpen = false;

            // ---- the death screen: its own font, size, thin (no) outline and colours, over the settings ----
            pc.Paused = true;
            Hud.OpenPause = 2;
            yield return new WaitForSeconds(0.4f);
            Hud.OpenDisplayCategory((int)Hud.DisplayCat.HudTimer);
            GameSettings.DeathInk.Set(0f, false); GameSettings.DeathSize.Set(1.3f, false); GameSettings.DeathAccent.Set(new Color(0.2f, 0.6f, 1f), false);
            GameSettings.DeathFont.Set(System.Array.IndexOf(GameSettings.FontPrefNames, "Georgia"), false); GameSettings.DeathVignette.Set(0.3f, false);
            Hud.TestDeathScreen = true;
            yield return new WaitForSeconds(0.8f);
            Check(Time.frameCount - Hud.DeathShownFrame <= 3 && Hud.DeathInkPx < 0.01f, $"DEATH SCREEN: shown with no outline ({Hud.DeathInkPx:0.0} px)");
            yield return Snap("looks_66_death_screen_own_look");
            GameSettings.DeathInk.Set(0.25f, false);
            yield return new WaitForSeconds(0.2f);
            Check(Hud.DeathInkPx > 0f && Hud.DeathInkPx < 4f * k * 0.5f, $"DEATH SCREEN: a thinner outline than designed ({Hud.DeathInkPx:0.0} px, was {4f * k:0.0})");
            Hud.TestDeathScreen = false;
            GameSettings.ResetDeath(false);
            // the new sections in the settings: Notifications (animation, colours, countdown, the timer's wording) and the
            // hands / tools under Post FX
            Hud.OpenDisplayCategory((int)Hud.DisplayCat.Notifications);
            Hud.SetSettingsScroll(900f);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("looks_67_settings_notif_animation_colours");
            Hud.SetSettingsScroll(2200f);
            yield return new WaitForSeconds(0.3f);
            yield return Snap("looks_68_settings_countdown");
            Hud.OpenDisplayCategory((int)Hud.DisplayCat.PostFx);
            Hud.OpenLayerLooks(true);
            GameSettings.HandsOwn.Set(true, false); GameSettings.ToolsOwn.Set(true, false);
            Hud.SetSettingsScroll(100000f);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("looks_69_settings_hands_tools");
            GameSettings.ResetHandsLook(false);
            Hud.OpenLayerLooks(false);
            Hud.OpenDisplayCategory(0);
            pc.Paused = false;
            yield return new WaitForSeconds(0.3f);
        }
    }
}
