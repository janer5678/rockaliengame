using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// What killed someone, for the kill feed: an item id (the weapon - Item.C4, Item.Spear, Item.Rock for the rock...),
    /// or one of these for deaths no weapon did. 0 = work it out from what the killer is holding.
    /// </summary>
    public static class KillCause
    {
        public const byte Died = 200, Suicide = 201, Fall = 202, Lava = 203, Slenderman = 204;
        /// <summary>Not deaths: the ball's big moments get a line in the kill feed too (who picked it up / captured it).</summary>
        public const byte BallPickup = 205, BallCapture = 206;
        public static bool IsItem(byte c) => c > 0 && c < Died;
        public static bool IsBall(byte c) => c == BallPickup || c == BallCapture;
        /// <summary>The game modes' moments (NetGame.GameModes.cs): a goal (3 Goal), a machine destroyed (Bedwars), a skull
        /// handed in (Assassin), the ball changing hands (Domination) - their words come in the line itself.</summary>
        public const byte Goal = 207, MachineDown = 208, SkullIn = 209, Dominate = 210;
        public static bool IsEvent(byte c) => c >= Goal && c <= Dominate;
    }

    public partial class NetGame
    {
        /// <summary>Seconds before the same player's same ball line (picked up / captured) can show in the kill feed again.</summary>
        public const float BallFeedCooldown = 10f;
        readonly Dictionary<(ulong, bool), float> m_BallFeedAt = new Dictionary<(ulong, bool), float>();

        /// <summary>Server: a ball line in everyone's kill feed - `who` picked it up, or it was captured for `team` (by `who`,
        /// when it's known who brought it in; null = just the team). Sent as a kill feed line with a ball cause: the
        /// "killer" is the player, the "victim" side is the team (it's drawn as words, not a name - Hud.AddKill).
        /// The same line for the same player comes at most once every BallFeedCooldown seconds.</summary>
        public void ServerBallFeed(PlayerNet who, int team, bool capture)
        {
            if (!IsServer || !IsSpawned || team < 0) return;
            // flood protection: one player (or, for a capture nobody's named in, one team) gets the same ball line at most
            // once every BallFeedCooldown seconds - spam picking it up / dropping it, or popping it in and out of the
            // machine, doesn't fill the feed
            var key = (who != null ? who.NetworkObjectId : ulong.MaxValue - (ulong)team, capture);
            if (m_BallFeedAt.TryGetValue(key, out float last) && Time.time - last < BallFeedCooldown) return;
            m_BallFeedAt[key] = Time.time;
            KillFeedRpc(who != null ? who.Team.Value : (byte)255, who != null ? who.Slot.Value : (byte)0, (byte)team, 0,
                capture ? KillCause.BallCapture : KillCause.BallPickup, false, new Unity.Collections.FixedString32Bytes(who != null ? who.DisplayName : ""), default);
        }

        /// <summary>Someone died: a line in everyone's kill feed. killerTeam 255 = nobody killed them. `head`: a headshot.
        /// The names are the players' as they were (empty = their team colour and number).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void KillFeedRpc(byte killerTeam, byte killerSlot, byte victimTeam, byte victimSlot, byte cause, bool head = false,
            Unity.Collections.FixedString32Bytes killerName = default, Unity.Collections.FixedString32Bytes victimName = default)
        {
            Hud.AddKill(killerTeam, killerSlot, victimTeam, victimSlot, cause, head, killerName.ToString(), victimName.ToString());
        }
    }

    /// <summary>
    /// The kill feed, top right, like CS:GO's: one dark strip per death - the killer's name, the icon of what did it, a
    /// headshot icon when it was one, the victim's name, bold and in their team colours. A death nobody caused has its own
    /// icon (a fall, lava, a skull). Each strip pops in from the right with a flash, the ones under it slide down to make
    /// room, and it fades after a few seconds. Your own kills are edged in gold and brighter, your deaths in red.
    /// The ball gets strips too: "NAME (ball) picked up the ball" and "NAME (ball) captured the ball for TEAM".
    /// </summary>
    public partial class Hud
    {
        struct Kill { public byte KillerTeam, KillerSlot, VictimTeam, VictimSlot, Cause; public bool Head; public string Killer, Victim; public float Time, Y; }
        static readonly List<Kill> s_Kills = new List<Kill>();
        const float KillShow = 6.5f, KillFade = 0.8f, KillPop = 0.22f;
        static Texture2D s_Skull, s_Flame, s_HeadIcon, s_FallIcon, s_BallIcon;

        /// <summary>Test hooks: the last kill feed line (plain text), how many lines there have been, and whether the last was a headshot.</summary>
        public static string LastKillLine { get; private set; } = "";
        public static int KillLines { get; private set; }
        public static bool LastKillHead { get; private set; }
        /// <summary>(tests) how many kill feed lines were last drawn, the frame, and how tall each was.</summary>
        public static int KillFeedShownLines, KillFeedShownFrame = -10;
        public static float KillFeedRowH;

        public static void AddKill(byte killerTeam, byte killerSlot, byte victimTeam, byte victimSlot, byte cause, bool head = false, string killerName = "", string victimName = "")
        {
            string killer = killerTeam == 255 ? (cause == KillCause.Slenderman ? "SLENDERMAN" : "") : !string.IsNullOrEmpty(killerName) ? killerName : PlayerNet.DefaultName(killerTeam, killerSlot);
            string victim = !string.IsNullOrEmpty(victimName) ? victimName : PlayerNet.DefaultName(victimTeam, victimSlot);
            // the ball (NetGame.ServerBallFeed): the player, the ball, then what happened (in the team's colour)
            if (KillCause.IsEvent(cause)) { victim = victimName; if (killerTeam == 255) killer = ""; }
            else if (KillCause.IsBall(cause))
            {
                string team = Cfg.TeamName[Mathf.Clamp(victimTeam, 0, Cfg.TeamName.Length - 1)];
                victim = cause == KillCause.BallPickup ? "picked up the ball" : killer == "" ? $"{team} captured the ball" : $"captured the ball for {team}";
            }
            s_Kills.Add(new Kill { KillerTeam = killerTeam, KillerSlot = killerSlot, VictimTeam = victimTeam, VictimSlot = victimSlot, Cause = cause, Head = head, Killer = killer, Victim = victim, Time = Time.time, Y = -1f });
            while (s_Kills.Count > 10) s_Kills.RemoveAt(0); // (Settings > Display > KILL FEED: up to 10 at once)
            LastKillLine = (killer + " [" + CauseName(cause) + (head ? ", headshot" : "") + "] " + victim).Trim();
            LastKillHead = head;
            KillLines++;
            Debug.Log("[KILLFEED] " + LastKillLine);
        }

        public static void ClearKills() => s_Kills.Clear();

        static Color NameColor(byte team) => PlayerNet.NameColor(team);

        static string CauseName(byte cause)
        {
            switch (cause)
            {
                case KillCause.Suicide: return "suicide";
                case KillCause.Fall: return "fell";
                case KillCause.Lava: return "lava";
                case KillCause.Slenderman: return "slenderman";
                case KillCause.Died: return "died";
                case KillCause.BallPickup: case KillCause.BallCapture: return "ball";
                default: return KillCause.IsItem(cause) ? Cfg.ItemName((Item)cause) : "died";
            }
        }

        static Texture2D CauseIcon(byte cause)
        {
            if (KillCause.IsItem(cause)) { var t = ItemIcons.Get((Item)cause); if (t != null) return t; }
            if (cause == KillCause.Slenderman) { var t = ItemIcons.Get(Item.SlenderEgg); if (t != null) return t; }
            EnsureKillIcons();
            return KillCause.IsBall(cause) || cause == KillCause.Goal || cause == KillCause.Dominate ? s_BallIcon : cause == KillCause.MachineDown ? s_Flame : cause == KillCause.SkullIn ? s_Skull : cause == KillCause.Lava ? s_Flame : cause == KillCause.Fall ? s_FallIcon : s_Skull;
        }

        /// <summary>The drawn icons: a skull (died, suicide), a flame (lava), a falling arrow (a fall), the golden ball (ball
        /// lines) and the headshot mark.</summary>
        static void EnsureKillIcons()
        {
            if (s_Skull != null) return;
            const int S = 64;
            Texture2D Make(System.Func<float, float, Color> f)
            {
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                        px[y * S + x] = f((x + 0.5f) / S * 2f - 1f, 1f - (y + 0.5f) / S * 2f); // (u right, v down, -1..1)
                tex.SetPixels(px);
                tex.Apply();
                return tex;
            }
            float Disc(float u, float v, float cx, float cy, float rx, float ry) { float dx = (u - cx) / rx, dy = (v - cy) / ry; return Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * 18f); }
            float Box(float u, float v, float x0, float y0, float x1, float y1) => u >= x0 && u <= x1 && v >= y0 && v <= y1 ? 1f : 0f;
            s_Skull = Make((u, v) =>
            {
                float a = Mathf.Max(Disc(u, v, 0, -0.18f, 0.72f, 0.62f), Box(u, v, -0.42f, 0.2f, 0.42f, 0.7f));
                float holes = Mathf.Max(Disc(u, v, -0.3f, -0.1f, 0.2f, 0.22f), Disc(u, v, 0.3f, -0.1f, 0.2f, 0.22f));
                holes = Mathf.Max(holes, Disc(u, v, 0, 0.22f, 0.09f, 0.12f));
                for (int i = -1; i <= 1; i++) holes = Mathf.Max(holes, Box(u, v, i * 0.2f - 0.035f, 0.5f, i * 0.2f + 0.035f, 0.72f));
                float val = 0.95f - 0.15f * Mathf.Clamp01(v);
                return new Color(val, val, val * 0.95f, a * (1f - holes));
            });
            s_Flame = Make((u, v) =>
            {
                // a teardrop: round at the bottom, pointed at the top, hot yellow inside
                float w = Mathf.Lerp(0.62f, 0.02f, Mathf.Clamp01((0.55f - v) / 1.45f)) * (v > 0.55f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.55f) / 0.4f, 2))) : 1f);
                float a = Mathf.Clamp01((w - Mathf.Abs(u + 0.08f * Mathf.Sin(v * 4f))) * 20f) * (v < 0.95f ? 1f : 0f);
                float inner = Mathf.Clamp01((w * 0.5f - Mathf.Abs(u)) * 10f) * Mathf.Clamp01(v + 0.2f);
                return new Color(1f, Mathf.Lerp(0.35f, 0.9f, inner), Mathf.Lerp(0.05f, 0.4f, inner), a);
            });
            s_FallIcon = Make((u, v) =>
            {
                // an arrow pointing down onto a line of ground
                float shaft = Box(u, v, -0.13f, -0.85f, 0.13f, 0.1f);
                float headA = v >= 0.05f && v <= 0.62f && Mathf.Abs(u) <= (0.62f - v) * 0.95f ? 1f : 0f;
                float ground = Box(u, v, -0.8f, 0.72f, 0.8f, 0.9f);
                float a = Mathf.Max(shaft, Mathf.Max(headA, ground));
                return ground > 0f && shaft + headA <= 0f ? new Color(0.75f, 0.75f, 0.78f, a) : new Color(0.96f, 0.96f, 0.98f, a);
            });
            s_BallIcon = Make((u, v) =>
            {
                // the golden ball: a gold disc, lit from the top left, with a white glint
                float a = Disc(u, v, 0, 0, 0.62f, 0.62f);
                float lit = Mathf.Clamp01(0.75f - (u + v) * 0.45f);
                float glint = Disc(u, v, -0.22f, -0.24f, 0.16f, 0.13f);
                var c = Color.Lerp(new Color(0.78f, 0.52f, 0.08f), new Color(1f, 0.88f, 0.35f), lit);
                c = Color.Lerp(c, Color.white, glint * 0.85f);
                c.a = a;
                return c;
            });
            s_HeadIcon = Make((u, v) =>
            {
                // a head in a sight: a red ring with four ticks round a pale head (dark eyes), like CS:GO's headshot mark
                float d = Mathf.Sqrt(u * u + v * v);
                float ring = Mathf.Clamp01((0.11f - Mathf.Abs(d - 0.8f)) * 16f);
                float ticks = Mathf.Max(Mathf.Max(Box(u, v, -0.07f, -1f, 0.07f, -0.62f), Box(u, v, -0.07f, 0.62f, 0.07f, 1f)), Mathf.Max(Box(u, v, -1f, -0.07f, -0.62f, 0.07f), Box(u, v, 0.62f, -0.07f, 1f, 0.07f)));
                float head = Mathf.Max(Disc(u, v, 0, -0.08f, 0.44f, 0.46f), Box(u, v, -0.2f, 0.2f, 0.2f, 0.48f));
                float eyes = Mathf.Max(Disc(u, v, -0.19f, -0.06f, 0.12f, 0.14f), Disc(u, v, 0.19f, -0.06f, 0.12f, 0.14f));
                float red = Mathf.Max(ring, ticks);
                if (head > 0f) { float val = eyes > 0.5f ? 0.12f : 0.97f; return new Color(val, val, val, Mathf.Max(head, red)); }
                return new Color(1f, 0.27f, 0.2f, red);
            });
        }

        /// <summary>Top right. Returns how tall it is (the messages go under it).</summary>
        float DrawKillFeed(float k0, float top)
        {
            if (!GameSettings.KillFeedOn.Value) return 0f;
            // (Settings > Display > HUD AND TIMER > KILL FEED: KillFeedLooks.cs)
            float k = k0 * GameSettings.KillFeedSize.Value, bgA = GameSettings.KillFeedBack.Value, show = GameSettings.KillFeedTime.Value;
            bool icons = GameSettings.KillFeedIcons.Value, teamCols = GameSettings.KillFeedTeamColours.Value, edged = GameSettings.KillFeedHighlight.Value;
            int maxLines = GameSettings.KillFeedLinesNow, shown = 0;
            float offY = GameSettings.KillFeedY.Value * k0, offX = GameSettings.KillFeedX.Value * k0;
            float now = Time.time, y = top + offY, sw = Screen.width - offX;
            var me = PlayerNet.Local;
            bool repaint = Event.current.type == EventType.Repaint;
            float rowH = 36 * k, gap = 4 * k, pad = 10 * k, iconW = icons ? 58 * k : 0f, headW = 30 * k;
            var nameSt = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(17 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow, richText = false };
            // the names' font and edge (a drop shadow as designed, an outline, or none: KillFeedLooks.cs)
            var kfont = GameSettings.FontForPref(GameSettings.KillFeedFont.Value);
            if (kfont != null) nameSt.font = kfont;
            m_KfEdge = GameSettings.KillFeedEdge.Value; m_KfInk = GameSettings.KillFeedInk.Value * k0; m_KfInkColour = GameSettings.KillFeedInkColour.Value;
            for (int i = s_Kills.Count - 1; i >= 0; i--)
            {
                var kl = s_Kills[i];
                float age = now - kl.Time;
                if (age > show || age < 0f) continue;
                if (shown >= maxLines) break; // (the newest few)
                shown++;
                // the newest is on top: the older ones ease down to make room for it (and back up as strips go)
                if (kl.Y < 0f) kl.Y = y;
                else if (repaint) kl.Y = Mathf.Abs(kl.Y - y) < 0.5f ? y : Mathf.Lerp(kl.Y, y, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
                s_Kills[i] = kl;
                float a = Mathf.Clamp01((show - age) / KillFade);
                // it pops in from the right and overshoots a touch before settling; a white flash dies away over it
                float p = Mathf.Clamp01(age / KillPop), q = p - 1f;
                float back = 1f + 2.6f * q * q * q + 1.6f * q * q;
                float flash = Mathf.Clamp01(1f - age / 0.4f);
                float bump = 1f + 0.3f * Mathf.Sin(Mathf.Clamp01(age / 0.3f) * Mathf.PI);
                string killer = kl.Killer ?? "", victim = kl.Victim ?? "";
                float kw = killer != "" ? nameSt.CalcSize(new GUIContent(killer)).x : 0f, vw = nameSt.CalcSize(new GUIContent(victim)).x;
                float w = pad + (kw > 0 ? kw + 8 * k : 0) + (icons ? iconW + 8 * k : 0) + (kl.Head ? headW + 6 * k : 0) + vw + pad;
                float x = sw - 10 - w + (1f - back) * (w + 20f);
                var r = new Rect(x, kl.Y, w, rowH);
                bool mine = edged && me != null && kl.KillerTeam == me.Team.Value && kl.KillerSlot == me.Slot.Value && kl.KillerTeam != 255;
                bool myDeath = edged && me != null && kl.VictimTeam == me.Team.Value && kl.VictimSlot == me.Slot.Value && !KillCause.IsBall(kl.Cause) && !KillCause.IsEvent(kl.Cause); // (a ball or mode line's "victim" is words)
                var old = GUI.color;
                Fill(r, mine ? new Color(0.2f, 0.14f, 0.02f, Mathf.Clamp01(0.86f * bgA) * a) : myDeath ? new Color(0.24f, 0.04f, 0.04f, Mathf.Clamp01(0.84f * bgA) * a) : new Color(0.04f, 0.04f, 0.06f, Mathf.Clamp01(0.72f * bgA) * a));
                if (bgA > 0.01f) Fill(new Rect(r.x, r.y, r.width, 2 * k), new Color(1, 1, 1, 0.08f * a * Mathf.Min(1f, bgA)));
                var edge = mine ? new Color(1f, 0.82f, 0.25f, a) : myDeath ? new Color(1f, 0.25f, 0.2f, a) : new Color(1, 1, 1, 0.12f * a * Mathf.Min(1f, bgA));
                Fill(new Rect(r.x, r.y, 3 * k, r.height), edge);
                if (mine || myDeath)
                {
                    // yours: edged all the way round
                    Fill(new Rect(r.x, r.yMax - 2 * k, r.width, 2 * k), edge);
                    Fill(new Rect(r.x, r.y, r.width, 2 * k), edge);
                    Fill(new Rect(r.xMax - 2 * k, r.y, 2 * k, r.height), edge);
                }
                if (flash > 0f) Fill(r, new Color(1f, 1f, mine ? 0.75f : 1f, (mine ? 0.5f : 0.3f) * flash * a));
                float cx = r.x + pad;
                if (kw > 0)
                {
                    DrawName(new Rect(cx, r.y, kw + 4, rowH), killer, !teamCols ? GameSettings.KillFeedNameColour.Value : kl.KillerTeam != 255 ? NameColor(kl.KillerTeam) : new Color(0.8f, 0.6f, 1f), nameSt, a);
                    cx += kw + 8 * k;
                }
                var icon = icons ? CauseIcon(kl.Cause) : null;
                if (icon != null)
                {
                    // (on a faint light tile so dark weapons show; the icon swells as the strip lands)
                    Fill(new Rect(cx - 2 * k, r.y + 3 * k, iconW + 4 * k, rowH - 6 * k), new Color(1, 1, 1, 0.1f * a));
                    GUI.color = new Color(1, 1, 1, a * s_HudAlpha);
                    float iw = iconW * bump, ih = (rowH - 2 * k) * bump;
                    GUI.DrawTexture(new Rect(cx + iconW / 2 - iw / 2, r.center.y - ih / 2, iw, ih), icon, ScaleMode.ScaleToFit, true);
                }
                if (icons) cx += iconW + 8 * k;
                if (kl.Head)
                {
                    // the headshot mark: it lands a beat after the weapon, bigger
                    EnsureKillIcons();
                    float hb = 1f + 0.6f * Mathf.Sin(Mathf.Clamp01((age - 0.08f) / 0.32f) * Mathf.PI);
                    float hs = (rowH - 8 * k) * hb;
                    GUI.color = new Color(1, 1, 1, a * s_HudAlpha * Mathf.Clamp01((age - 0.05f) / 0.08f));
                    GUI.DrawTexture(new Rect(cx + headW / 2 - hs / 2, r.center.y - hs / 2, hs, hs), s_HeadIcon, ScaleMode.ScaleToFit, true);
                    cx += headW + 6 * k;
                }
                GUI.color = old;
                DrawName(new Rect(cx, r.y, vw + 4, rowH), victim, teamCols ? NameColor(kl.VictimTeam) : GameSettings.KillFeedNameColour.Value, nameSt, a);
                y += rowH + gap;
            }
            KillFeedShownLines = shown; // (tests)
            if (shown > 0) { KillFeedShownFrame = Time.frameCount; KillFeedRowH = rowH; }
            return shown > 0 ? y - top : 0f;
        }

        // the names' edge this frame (Settings > Display > KILL FEED: KillFeedLooks.cs)
        int m_KfEdge;
        float m_KfInk = 1.5f;
        Color m_KfInkColour = Color.black;
        /// <summary>(tests) the kill feed names' edge as last drawn (0 shadow, 1 outline, 2 none) and how thick (px).</summary>
        public static int KillFeedEdgeShown = -1;
        public static float KillFeedInkShown;

        void DrawName(Rect r, string text, Color c, GUIStyle st, float a)
        {
            var ec = m_KfInkColour; ec.a = 0.85f * a;
            KillFeedEdgeShown = m_KfEdge; KillFeedInkShown = m_KfInk;
            EdgeText(r, text, st, new Color(c.r, c.g, c.b, a), m_KfEdge, m_KfInk, ec);
        }
    }
}
