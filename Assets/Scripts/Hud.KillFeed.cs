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
        public static bool IsItem(byte c) => c > 0 && c < Died;
    }

    public partial class NetGame
    {
        /// <summary>Someone died: a line in everyone's kill feed. killerTeam 255 = nobody killed them.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void KillFeedRpc(byte killerTeam, byte killerSlot, byte victimTeam, byte victimSlot, byte cause)
        {
            Hud.AddKill(killerTeam, killerSlot, victimTeam, victimSlot, cause);
        }
    }

    /// <summary>
    /// The kill feed, top right: one dark strip per death - the killer's name, the icon of what did it, the victim's name,
    /// bold and in their team colours. Your own kills are edged in gold, your deaths in red. Each slides in, stays a few
    /// seconds and fades.
    /// </summary>
    public partial class Hud
    {
        struct Kill { public byte KillerTeam, KillerSlot, VictimTeam, VictimSlot, Cause; public float Time; }
        static readonly List<Kill> s_Kills = new List<Kill>();
        const float KillShow = 7f;
        static Texture2D s_Skull, s_Flame;

        /// <summary>Test hooks: the last kill feed line (plain text) and how many lines there have been.</summary>
        public static string LastKillLine { get; private set; } = "";
        public static int KillLines { get; private set; }

        public static void AddKill(byte killerTeam, byte killerSlot, byte victimTeam, byte victimSlot, byte cause)
        {
            s_Kills.Add(new Kill { KillerTeam = killerTeam, KillerSlot = killerSlot, VictimTeam = victimTeam, VictimSlot = victimSlot, Cause = cause, Time = Time.time });
            while (s_Kills.Count > 5) s_Kills.RemoveAt(0);
            LastKillLine = (KillerName(killerTeam, killerSlot, cause) + " [" + CauseName(cause) + "] " + PlayerName(victimTeam, victimSlot)).Trim();
            KillLines++;
            Debug.Log("[KILLFEED] " + LastKillLine);
        }

        public static void ClearKills() => s_Kills.Clear();

        static string PlayerName(byte team, byte slot) => Cfg.TeamLabel(team) + (Cfg.ModeTeamSize(Cfg.Mode) > 1 ? " " + (slot + 1) : "");
        static string KillerName(byte team, byte slot, byte cause) => team != 255 ? PlayerName(team, slot) : cause == KillCause.Slenderman ? "SLENDERMAN" : "";
        static Color NameColor(byte team) => team < Cfg.TeamColor.Length ? Color.Lerp(Cfg.TeamColor[team], Color.white, 0.3f) : new Color(0.8f, 0.6f, 1f);

        static string CauseName(byte cause)
        {
            switch (cause)
            {
                case KillCause.Suicide: return "suicide";
                case KillCause.Fall: return "fell";
                case KillCause.Lava: return "lava";
                case KillCause.Slenderman: return "slenderman";
                case KillCause.Died: return "died";
                default: return KillCause.IsItem(cause) ? Cfg.ItemName((Item)cause) : "died";
            }
        }

        static Texture2D CauseIcon(byte cause)
        {
            if (KillCause.IsItem(cause)) { var t = ItemIcons.Get((Item)cause); if (t != null) return t; }
            if (cause == KillCause.Slenderman) { var t = ItemIcons.Get(Item.SlenderEgg); if (t != null) return t; }
            EnsureKillIcons();
            return cause == KillCause.Lava ? s_Flame : s_Skull;
        }

        /// <summary>The two drawn icons: a skull (died, suicide, fell) and a flame (lava).</summary>
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
        }

        /// <summary>Top right. Returns how tall it is (the messages go under it).</summary>
        float DrawKillFeed(float k, float top)
        {
            float now = Time.time, y = top, sw = Screen.width;
            var me = PlayerNet.Local;
            float rowH = 36 * k, gap = 4 * k, pad = 10 * k, iconW = 58 * k;
            var nameSt = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(17 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
            for (int i = s_Kills.Count - 1; i >= 0; i--)
            {
                var kl = s_Kills[i];
                float age = now - kl.Time;
                if (age > KillShow || age < 0f) continue;
                float a = Mathf.Clamp01(KillShow - age);
                float slide = Mathf.Clamp01(age / 0.18f);
                string killer = KillerName(kl.KillerTeam, kl.KillerSlot, kl.Cause), victim = PlayerName(kl.VictimTeam, kl.VictimSlot);
                float kw = killer != "" ? nameSt.CalcSize(new GUIContent(killer)).x : 0f, vw = nameSt.CalcSize(new GUIContent(victim)).x;
                float w = pad + (kw > 0 ? kw + 8 * k : 0) + iconW + 8 * k + vw + pad;
                float x = sw - 10 - w + (1f - slide * (2f - slide)) * (w * 0.6f);
                var r = new Rect(x, y, w, rowH);
                bool mine = me != null && kl.KillerTeam == me.Team.Value && kl.KillerSlot == me.Slot.Value && kl.KillerTeam != 255;
                bool myDeath = me != null && kl.VictimTeam == me.Team.Value && kl.VictimSlot == me.Slot.Value;
                var old = GUI.color;
                Fill(r, new Color(0.04f, 0.04f, 0.06f, 0.72f * a));
                Fill(new Rect(r.x, r.y, r.width, 2 * k), new Color(1, 1, 1, 0.08f * a));
                var edge = mine ? new Color(1f, 0.82f, 0.25f, a) : myDeath ? new Color(1f, 0.25f, 0.2f, a) : new Color(1, 1, 1, 0.12f * a);
                Fill(new Rect(r.x, r.y, 3 * k, r.height), edge);
                if (mine || myDeath) Fill(new Rect(r.x, r.yMax - 2 * k, r.width, 2 * k), edge);
                float cx = r.x + pad;
                if (kw > 0)
                {
                    DrawName(new Rect(cx, r.y, kw + 4, rowH), killer, kl.KillerTeam != 255 ? NameColor(kl.KillerTeam) : new Color(0.8f, 0.6f, 1f), nameSt, a);
                    cx += kw + 8 * k;
                }
                var icon = CauseIcon(kl.Cause);
                GUI.color = new Color(1, 1, 1, a);
                if (icon != null) { Fill(new Rect(cx - 2 * k, r.y + 3 * k, iconW + 4 * k, rowH - 6 * k), new Color(1, 1, 1, 0.1f * a)); GUI.color = new Color(1, 1, 1, a); GUI.DrawTexture(new Rect(cx, r.y + 1 * k, iconW, rowH - 2 * k), icon, ScaleMode.ScaleToFit, true); } // (on a faint light tile so dark weapons show)
                GUI.color = old;
                cx += iconW + 8 * k;
                DrawName(new Rect(cx, r.y, vw + 4, rowH), victim, NameColor(kl.VictimTeam), nameSt, a);
                y += rowH + gap;
            }
            return y - top;
        }

        void DrawName(Rect r, string text, Color c, GUIStyle st, float a)
        {
            var old = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, 0.85f * a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, st);
            st.normal.textColor = new Color(c.r, c.g, c.b, a);
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }
    }
}
