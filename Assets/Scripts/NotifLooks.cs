using System.Text.RegularExpressions;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Settings > Display > NOTIFICATIONS: how the big messages in the middle of the screen look and what they say.
    /// STYLE - the banners (TRADE STATION UNLOCKED, AIRDROP INCOMING...) and the end countdown (YOU WIN IN 7): their ink
    ///   outline thickness, band width and darkness, font, size and height on the screen (Hud.Notify.cs reads these).
    /// WORDING - every notification's title can be reworded (NotifText below: Hud.Banner maps each title through it; an
    ///   empty override = the game's own words). Titles with a part that changes (a team, a number of seconds) keep
    ///   it as {0}.
    /// ANIMATION - how a banner comes in (snap open, slide down, fade, pop, none) and goes (lift & fade, fade, slide up,
    ///   shrink), how long it stays and how far up it flies on the way out.
    /// COLOURS - each kind of news' accent colour (unlocks, airdrops, the wall, destroyed / down, goals...: NotifAccentFor).
    /// The timer's header words (WALL DROPS IN, TIME LEFT...) are reworded here too (NotifText.Timer: apart from the
    /// banners'). The countdowns have a look of their own (CountdownLooks.cs).
    /// Also the interface switch for the "YOU ARE BLUE" box in the top left.
    /// </summary>
    public static partial class GameSettings
    {
        const string GNotifStyle = "NOTIFICATION STYLE";
        /// <summary>TEXT OUTLINE: the words' own ink stroke (Hud.InkText round the banner's title and the countdown's
        /// label and number) - its thickness (1 = as designed, 0 = none), on / off, colour and opacity. Not the OWN LOOK
        /// post outline, which is an extra line laid on round everything after.</summary>
        public static readonly DisplayPref.Float NotifInk = new("notif.ink", GNotifStyle, 0.45f, 0f, 3f);
        public static readonly DisplayPref.Bool NotifInkOn = new("notif.ink.on", GNotifStyle, true);
        public static readonly DisplayPref.Colour NotifInkColour = new("notif.ink.colour", GNotifStyle, Color.black);
        public static readonly DisplayPref.Float NotifInkOpacity = new("notif.ink.opacity", GNotifStyle, 1f, 0f, 1f);
        /// <summary>The text outline's thickness in use (0 while it's switched off).</summary>
        public static float NotifInkNow => NotifInkOn.Value ? NotifInk.Value : 0f;
        /// <summary>The text outline's colour, at `alpha` (what the design had there) times its opacity.</summary>
        public static Color NotifInkEdge(float alpha)
        {
            var c = NotifInkColour.Value;
            c.a = Mathf.Clamp01(alpha * NotifInkOpacity.Value);
            return c;
        }
        /// <summary>How wide the dark band behind a banner / the countdown's label is (1 = as designed).</summary>
        public static readonly DisplayPref.Float NotifWidth = new("notif.width", GNotifStyle, 0.85f, 0.4f, 1.8f);
        /// <summary>How dark that band is (1 = as designed).</summary>
        public static readonly DisplayPref.Float NotifPlate = new("notif.plate", GNotifStyle, 0.2f, 0f, 1.5f);
        public static readonly DisplayPref.Choice NotifFont = new("notif.font", GNotifStyle, FontPrefNames, 4); // (4 = Consolas)
        /// <summary>How big the words (and the countdown's number) are (1 = as designed).</summary>
        public static readonly DisplayPref.Float NotifSize = new("notif.size", GNotifStyle, 1.25f, 0.5f, 1.8f);
        /// <summary>How far up (-) or down (+) the screen they sit, in pixels at the UI scale.</summary>
        public static readonly DisplayPref.Float NotifY = new("notif.y", GNotifStyle, -25f, -200f, 400f);

        public static Font NotifFontNow => FontForPref(NotifFont.Value);

        public static void ResetNotifStyle(bool save = true)
        {
            DisplayPref.ResetAll(save, NotifInk, NotifInkOn, NotifInkColour, NotifInkOpacity, NotifWidth, NotifPlate, NotifFont, NotifSize, NotifY);
        }

        // ---- how a banner comes and goes ----
        const string GNotifMove = "NOTIFICATION ANIMATION";
        /// <summary>How a banner comes in: the band snapping open from the middle (as designed), sliding down from above,
        /// fading in, popping in from small, or just there.</summary>
        public static readonly DisplayPref.Choice NotifEnter = new("notif.enter", GNotifMove, new[] { "Snap open", "Slide down", "Fade in", "Pop", "None" }, 0);
        /// <summary>How it goes: lifting up as it fades (as designed), just fading, sliding up off the screen, or shrinking away.</summary>
        public static readonly DisplayPref.Choice NotifExit = new("notif.exit", GNotifMove, new[] { "Lift & fade", "Fade", "Slide up", "Shrink" }, 0);
        /// <summary>How long a banner stays (seconds, the way in and out included).</summary>
        public static readonly DisplayPref.Float NotifTime = new("notif.time", GNotifMove, 4f, 1.5f, 12f);
        /// <summary>How far up the screen it flies as it leaves (pixels at the UI scale; Lift & fade and Slide up).</summary>
        public static readonly DisplayPref.Float NotifFly = new("notif.fly", GNotifMove, 40f, 0f, 600f);

        public static float NotifTimeNow => NotifTime.Value;

        public static void ResetNotifMove(bool save = true)
        {
            NotifEnter.Set(0, save); NotifExit.Set(0, save); NotifTime.Set(4f, save); NotifFly.Set(40f, save);
        }

        // ---- each kind of news' accent colour (the band's lines and the title settle into it: Hud.BannerAccent) ----
        const string GNotifColours = "NOTIFICATION COLOURS";
        public static readonly DisplayPref.Colour NotifColUnlock = new("notif.colour.unlock", GNotifColours, new Color(1f, 0.88f, 0.2f));
        public static readonly DisplayPref.Colour NotifColAirdrop = new("notif.colour.airdrop", GNotifColours, new Color(1f, 0.38f, 0.78f));
        public static readonly DisplayPref.Colour NotifColWall = new("notif.colour.wall", GNotifColours, new Color(0.4f, 0.85f, 1f));
        public static readonly DisplayPref.Colour NotifColDown = new("notif.colour.down", GNotifColours, new Color(1f, 0.3f, 0.25f));
        public static readonly DisplayPref.Colour NotifColGoal = new("notif.colour.goal", GNotifColours, new Color(1f, 0.85f, 0.3f));
        public static readonly DisplayPref.Colour NotifColAirstrike = new("notif.colour.airstrike", GNotifColours, new Color(1f, 0.85f, 0.3f));
        public static readonly DisplayPref.Colour NotifColBall = new("notif.colour.ball", GNotifColours, new Color(1f, 0.85f, 0.3f));
        public static readonly DisplayPref.Colour NotifColClock = new("notif.colour.clock", GNotifColours, new Color(1f, 0.85f, 0.3f));
        public static readonly DisplayPref.Colour NotifColLobby = new("notif.colour.lobby", GNotifColours, new Color(1f, 0.85f, 0.3f));
        public static readonly DisplayPref.Colour NotifColDefault = new("notif.colour.default", GNotifColours, new Color(1f, 0.85f, 0.3f));

        /// <summary>The kinds of news with their colours, in the settings' order: (name, what it covers, colour).</summary>
        public static readonly (string name, string covers, DisplayPref.Colour pref)[] NotifColours =
        {
            ("Unlocks", "TRADE STATION UNLOCKED", NotifColUnlock),
            ("Airdrops", "AIRDROP INCOMING, AIRDROP IN 30 SECONDS", NotifColAirdrop),
            ("The wall", "THE WALL IS DROPPING, THE WALL IS DOWN", NotifColWall),
            ("Destroyed / down", "A TEAM'S MACHINE IS DOWN", NotifColDown),
            ("Goals", "GOAL! BLUE", NotifColGoal),
            ("Airstrikes", "AIRSTRIKE INBOUND", NotifColAirstrike),
            ("The ball", "BALL INCOMING", NotifColBall),
            ("The clock", "1 MINUTE LEFT, OVERTIME, SUDDEN DEATH, GATHER & BUILD", NotifColClock),
            ("The lobby", "EVERYONE'S READY / HERE, LOBBY / TEAM FULL", NotifColLobby),
            ("Anything else", "the tutorial and the rest", NotifColDefault),
        };

        /// <summary>A banner's accent colour, by the game's own words for it.</summary>
        public static Color NotifAccentFor(string title)
        {
            string s = title ?? "";
            if (s.Contains("UNLOCK")) return NotifColUnlock.Value;
            if (s.Contains("AIRDROP")) return NotifColAirdrop.Value;
            if (s.Contains("AIRSTRIKE")) return NotifColAirstrike.Value;
            if (s.Contains("WALL")) return NotifColWall.Value;
            if (s.StartsWith("GOAL")) return NotifColGoal.Value;
            if (s.Contains("BALL")) return NotifColBall.Value;
            if (s.Contains("DROP")) return NotifColWall.Value;
            if (s.Contains("DESTROY") || s.Contains("DOWN") || s.Contains("OUT")) return NotifColDown.Value;
            if (s.Contains("MINUTE") || s.Contains("OVERTIME") || s.Contains("SUDDEN") || s.Contains("GATHER")) return NotifColClock.Value;
            if (s.Contains("READY") || s.Contains("HERE") || s.Contains("FULL")) return NotifColLobby.Value;
            return NotifColDefault.Value;
        }

        public static void ResetNotifColours(bool save = true) { foreach (var c in NotifColours) c.pref.Set(c.pref.Default, save); }

        // ---- the HUD ----
        const string GHud = "HUD";
        /// <summary>The "YOU ARE BLUE" box in the top left of the HUD.</summary>
        public static readonly DisplayPref.Bool HudTeamBox = new("hud.teambox", GHud, false);
        /// <summary>The "Your Base" radar under it (it moves up into the corner while the team box is off).</summary>
        public static readonly DisplayPref.Bool HudBaseRadar = new("hud.baseradar", GHud, false);

        /// <summary>(makes the wording lines exist with GameSettings, so they're in the display settings code)</summary>
        static readonly int s_NotifLines = NotifText.All.Length + NotifText.Timer.Length;
    }

    /// <summary>
    /// The notifications' wording: each title the game can show (by a stable key), the words it uses, and the player's
    /// own words for it (a DisplayPref.Text, "notif.text.KEY" - in the display settings code too). Hud.Banner and the end
    /// countdown pass their titles through Map.
    /// </summary>
    public static class NotifText
    {
        public sealed class Line
        {
            public readonly string Key, Default, Where;
            public readonly DisplayPref.Text Own;
            internal Regex Match;
            public Line(string key, string def, string where)
            {
                Key = key; Default = def; Where = where;
                Own = new DisplayPref.Text("notif.text." + key, "NOTIFICATION WORDING");
            }
        }

        /// <summary>Every notification title, in the order the settings list them. ({0}: the part that changes.)</summary>
        public static readonly Line[] All =
        {
            new("gather", "GATHER & BUILD", "the match starts"),
            new("wall_dropping", "THE WALL IS DROPPING", "the ball is out"),
            new("wall_down", "THE WALL IS DOWN", "the ball is out (fun modes)"),
            new("one_minute", "1 MINUTE LEFT", "a minute to go"),
            new("overtime", "OVERTIME", "time ran out with no ball in"),
            new("sudden_death", "SUDDEN DEATH", "the space arena"),
            new("trade_station", "TRADE STATION UNLOCKED", "a workbench unlocks"),
            new("airdrop_soon", "AIRDROP IN {0} SECONDS", "an airdrop is coming ({0}: seconds)"),
            new("airdrop", "AIRDROP INCOMING", "an airdrop is falling"),
            new("airstrike", "AIRSTRIKE INBOUND", "someone called an airstrike"),
            new("ball_incoming", "BALL INCOMING", "the ball is dropped back in"),
            new("goal", "GOAL! {0}", "a goal ({0}: the team)"),
            new("machine_down", "{0}'S MACHINE IS DOWN", "a team's machine is destroyed ({0}: the team)"),
            new("everyone_ready", "EVERYONE'S READY", "the lobby is ready"),
            new("everyone_here", "EVERYONE'S HERE", "the lobby is full"),
            new("lobby_full", "LOBBY FULL", "no place to play"),
            new("team_full", "TEAM FULL", "no room for a bot on that team"),
            new("full", "FULL", "no room for another bot"),
            new("tutorial", "TUTORIAL", "the tutorial starts"),
            // the end countdown's label
            new("cd_you_win", "YOU WIN IN", "end countdown: your ball is in"),
            new("cd_team_wins", "{0} WINS IN", "end countdown: their ball is in ({0}: the team)"),
            new("cd_overtime", "OVERTIME IN", "end countdown: no ball in"),
            new("cd_sudden", "SUDDEN DEATH IN", "end countdown: no ball in, no overtime"),
        };

        /// <summary>The words to show for a title the game gave (the player's own when they've set some).</summary>
        public static string Map(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            foreach (var l in All)
            {
                string own = l.Own.Value;
                if (string.IsNullOrWhiteSpace(own)) continue;
                if (l.Default.IndexOf("{0}", System.StringComparison.Ordinal) < 0)
                {
                    if (title == l.Default) return own;
                    continue;
                }
                l.Match ??= new Regex("^" + Regex.Escape(l.Default).Replace("\\{0}", "(.+?)") + "$");
                var m = l.Match.Match(title);
                if (m.Success) return own.Replace("{0}", m.Groups[1].Value);
            }
            return title;
        }

        /// <summary>The timer's header words (Hud.Notify.cs: DrawTopPanel passes its label through MapTimer) - apart from
        /// the banners', so rewording the OVERTIME banner doesn't reword the timer's OVERTIME and the other way round.</summary>
        public static readonly Line[] Timer =
        {
            new("timer_waiting", "WAITING FOR PLAYERS", "timer: the lobby is filling up"),
            new("timer_starts", "MATCH STARTS IN", "timer: everyone's in"),
            new("timer_wall", "WALL DROPS IN", "timer: gathering and building"),
            new("timer_timeleft", "TIME LEFT", "timer: the ball is out"),
            new("timer_overtime", "OVERTIME", "timer: overtime"),
            new("timer_sudden", "SUDDEN DEATH", "timer: the space arena"),
            new("timer_tutorial", "TUTORIAL", "timer: in the tutorial"),
        };

        /// <summary>The timer's header in the player's words (their own for it, or the game's).</summary>
        public static string MapTimer(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            foreach (var l in Timer)
                if (label == l.Default) return string.IsNullOrWhiteSpace(l.Own.Value) ? label : l.Own.Value;
            return label;
        }

        public static Line Find(string key)
        {
            foreach (var l in All) if (l.Key == key) return l;
            foreach (var l in Timer) if (l.Key == key) return l;
            return null;
        }

        /// <summary>How many have the player's own words (the banners' and the timer's).</summary>
        public static int Changed
        {
            get
            {
                int n = 0;
                foreach (var l in All) if (!string.IsNullOrWhiteSpace(l.Own.Value)) n++;
                foreach (var l in Timer) if (!string.IsNullOrWhiteSpace(l.Own.Value)) n++;
                return n;
            }
        }

        public static void ResetAll(bool save = true) { foreach (var l in All) l.Own.Set("", save); foreach (var l in Timer) l.Own.Set("", save); }
    }
}
