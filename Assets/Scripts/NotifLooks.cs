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
    /// Also the interface switch for the "YOU ARE BLUE" box in the top left.
    /// </summary>
    public static partial class GameSettings
    {
        const string GNotifStyle = "NOTIFICATION STYLE";
        /// <summary>TEXT OUTLINE: the words' own ink stroke (Hud.InkText round the banner's title and the countdown's
        /// label and number) - its thickness (1 = as designed, 0 = none), on / off, colour and opacity. Not the OWN LOOK
        /// post outline, which is an extra line laid on round everything after.</summary>
        public static readonly DisplayPref.Float NotifInk = new("notif.ink", GNotifStyle, 1f, 0f, 3f);
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
        public static readonly DisplayPref.Float NotifWidth = new("notif.width", GNotifStyle, 1f, 0.4f, 1.8f);
        /// <summary>How dark that band is (1 = as designed).</summary>
        public static readonly DisplayPref.Float NotifPlate = new("notif.plate", GNotifStyle, 1f, 0f, 1.5f);
        public static readonly DisplayPref.Choice NotifFont = new("notif.font", GNotifStyle, FontPrefNames, 0);
        /// <summary>How big the words (and the countdown's number) are (1 = as designed).</summary>
        public static readonly DisplayPref.Float NotifSize = new("notif.size", GNotifStyle, 1f, 0.5f, 1.8f);
        /// <summary>How far up (-) or down (+) the screen they sit, in pixels at the UI scale.</summary>
        public static readonly DisplayPref.Float NotifY = new("notif.y", GNotifStyle, 0f, -200f, 400f);

        public static Font NotifFontNow => FontForPref(NotifFont.Value);

        public static void ResetNotifStyle(bool save = true)
        {
            NotifInk.Set(1f, save); NotifInkOn.Set(true, save); NotifInkColour.Set(Color.black, save); NotifInkOpacity.Set(1f, save);
            NotifWidth.Set(1f, save); NotifPlate.Set(1f, save); NotifFont.Set(0, save); NotifSize.Set(1f, save); NotifY.Set(0f, save);
        }

        // ---- the HUD ----
        const string GHud = "HUD";
        /// <summary>The "YOU ARE BLUE" box in the top left of the HUD.</summary>
        public static readonly DisplayPref.Bool HudTeamBox = new("hud.teambox", GHud, true);
        /// <summary>The "Your Base" radar under it (it moves up into the corner while the team box is off).</summary>
        public static readonly DisplayPref.Bool HudBaseRadar = new("hud.baseradar", GHud, true);

        /// <summary>(makes the wording lines exist with GameSettings, so they're in the display settings code)</summary>
        static readonly int s_NotifLines = NotifText.All.Length;
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

        public static Line Find(string key)
        {
            foreach (var l in All) if (l.Key == key) return l;
            return null;
        }

        /// <summary>How many have the player's own words.</summary>
        public static int Changed
        {
            get { int n = 0; foreach (var l in All) if (!string.IsNullOrWhiteSpace(l.Own.Value)) n++; return n; }
        }

        public static void ResetAll(bool save = true) { foreach (var l in All) l.Own.Set("", save); }
    }
}
