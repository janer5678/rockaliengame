using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Text chat: Enter opens a line to everyone (global chat), T a line to your own team only (team chat: it shows
    /// "(TEAM CHAT)" before the name, and only your team gets it). Enter sends, Esc cancels. Names are in the sender's
    /// team colour. Commands: /kill (suicide - not again within 30 s), /help.
    /// </summary>
    public static class Chat
    {
        public static bool Open { get; private set; }
        /// <summary>The line being typed goes to your team only (T) rather than everyone (Enter).</summary>
        public static bool Team { get; private set; }
        static string s_Text = "";
        static int s_OpenedFrame;

        struct Line { public string Text; public float Time; }
        static readonly List<Line> s_Lines = new List<Line>();

        public static void Begin(bool team = false)
        {
            Open = true;
            Team = team;
            s_Text = "";
            s_OpenedFrame = Time.frameCount;
        }

        public static void Close()
        {
            Open = false;
            s_Text = "";
        }

        public static void Reset()
        {
            Close();
            s_Lines.Clear();
        }

        /// <summary>Test hook: the last line added.</summary>
        public static string LastLine { get; private set; } = "";

        /// <summary>A line for the log (from the server, or a local note).</summary>
        public static void Add(string text)
        {
            LastLine = text;
            s_Lines.Add(new Line { Text = text, Time = Time.time });
            while (s_Lines.Count > 40) s_Lines.RemoveAt(0);
        }

        static void Submit(PlayerNet me)
        {
            string t = s_Text.Trim();
            Close();
            if (t.Length == 0 || me == null) return;
            if (t.StartsWith("/"))
            {
                string cmd = t.Split(' ')[0].ToLowerInvariant();
                if (cmd == "/kill") me.SuicideRpc();
                else if (cmd == "/help") Add("<color=#bbbbbb>/kill - kill yourself (once every 30 s)</color>");
                else Add($"<color=#ff7777>Unknown command {cmd} - try /help</color>");
                return;
            }
            if (t.Length > 70) t = t.Substring(0, 70);
            me.ChatRpc(new FixedString128Bytes(t), Team);
        }

        /// <summary>Drawn by the HUD: the recent lines (fading out), and the typing line while open.</summary>
        public static void Draw(float k, GUIStyle small, System.Action<Rect, Color> fill, System.Action<Rect, string, GUIStyle> shadowed)
        {
            var me = PlayerNet.Local;
            float sw = Screen.width, sh = Screen.height;
            float x = 14, w = 520 * k, lineH = 22 * k;
            float bottom = sh * 0.72f;
            var wrap = new GUIStyle(small) { wordWrap = false, richText = true };
            int shown = 0;
            for (int i = s_Lines.Count - 1; i >= 0 && shown < (Open ? 12 : 7); i--)
            {
                float age = Time.time - s_Lines[i].Time;
                if (!Open && age > 12f) break;
                float a = Open ? 1f : Mathf.Clamp01(12f - age);
                float y = bottom - (shown + 1) * lineH;
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                if (Open) fill(new Rect(x - 4, y, w, lineH), new Color(0, 0, 0, 0.35f));
                shadowed(new Rect(x, y, w, lineH), s_Lines[i].Text, wrap);
                GUI.color = old;
                shown++;
            }
            if (!Open) return;
            var r = new Rect(x - 4, bottom + 4 * k, w, 28 * k);
            fill(r, new Color(0, 0, 0, 0.7f));
            var e = Event.current;
            // (the key that opened it - T - mustn't end up typed in the line)
            if (e.type == EventType.KeyDown && Time.frameCount == s_OpenedFrame) { e.Use(); return; }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.Use(); Submit(me); return; }
                if (e.keyCode == KeyCode.Escape) { e.Use(); Close(); return; }
            }
            var st = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * k), richText = false };
            st.normal.background = null;
            st.focused.background = null;
            st.normal.textColor = st.focused.textColor = Color.white;
            GUI.SetNextControlName("chat line");
            // who it goes to, in front of the line: TEAM (green) or ALL
            string to = Team ? "<color=#7dff9a><b>TEAM</b></color>" : "<color=#dddddd><b>ALL</b></color>";
            float tw = 54 * k;
            shadowed(new Rect(r.x + 8, r.y + 3, tw, r.height), to, wrap);
            var field = new Rect(r.x + 6 + tw, r.y + 2, r.width - 12 - tw, r.height - 4);
            s_Text = GUI.TextField(field, s_Text, 70, st);
            GUI.FocusControl("chat line");
            if (s_Text.Length == 0) shadowed(new Rect(field.x + 4, r.y + 3, field.width, r.height), Team
                ? "<color=#888888>Say something to your team...   Enter: send   Esc: cancel</color>"
                : "<color=#888888>Say something to everyone...   (/kill, /help)   Enter: send   Esc: cancel</color>", wrap);
        }
    }

    public partial class PlayerNet
    {
        float m_NextChat;

        /// <summary>A chat line, in the sender's team colour: everyone gets it, or (team chat) just the sender's team.</summary>
        [Rpc(SendTo.Server)]
        public void ChatRpc(FixedString128Bytes text, bool team)
        {
            if (Time.time < m_NextChat || NetGame.Instance == null) return;
            m_NextChat = Time.time + 0.5f;
            string t = text.ToString().Replace("<", "(").Replace(">", ")"); // no rich-text tricks
            if (t.Length > 70) t = t.Substring(0, 70);
            if (t.Trim().Length == 0) return;
            var c = ColorUtility.ToHtmlStringRGB(Color.Lerp(Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, 3)], Color.white, 0.25f));
            string who = Cfg.TeamLabel(Team.Value) + (Cfg.ModeTeamSize(Cfg.Mode) > 1 ? " " + (Slot.Value + 1) : "");
            string line = $"<color=#{c}><b>{who}</b></color>: {t}";
            if (line.Length > 120) line = line.Substring(0, 120);
            var g = NetGame.Instance;
            if (!team) { g.ChatLineRpc(new FixedString128Bytes(line), false, g.RpcTarget.ClientsAndHost); return; }
            // team chat: only the sender's team gets it (marked "(TEAM CHAT)" on their screens)
            var ids = new List<ulong>();
            foreach (var p in All) if (p.Team.Value == Team.Value && !ids.Contains(p.OwnerClientId)) ids.Add(p.OwnerClientId);
            if (ids.Count > 0) g.ChatLineRpc(new FixedString128Bytes(line), true, g.RpcTarget.Group(ids, RpcTargetUse.Temp));
            LastTeamChatTo = ids.Count;
        }

        /// <summary>Test hook (server): how many players the last team chat line went to.</summary>
        public static int LastTeamChatTo;
    }

    public partial class NetGame
    {
        /// <summary>A chat line for these screens (everyone, or one team for team chat).</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        public void ChatLineRpc(FixedString128Bytes line, bool team, RpcParams rpcParams)
        {
            Chat.Add(team ? "<color=#7dff9a><b>(TEAM CHAT)</b></color> " + line.ToString() : line.ToString());
            Sfx.Play2D(Sfx.Pop, 0.25f, 0.1f);
        }
    }
}
