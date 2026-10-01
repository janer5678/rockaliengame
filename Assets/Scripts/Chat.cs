using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Text chat: Enter opens a line to type in (Enter sends, Esc cancels); everyone in the match sees it, in the sender's
    /// team colour. Commands: /kill (suicide - not again within 30 s), /help.
    /// </summary>
    public static class Chat
    {
        public static bool Open { get; private set; }
        static string s_Text = "";
        static int s_OpenedFrame;

        struct Line { public string Text; public float Time; }
        static readonly List<Line> s_Lines = new List<Line>();

        public static void Begin()
        {
            Open = true;
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

        /// <summary>A line for the log (from the server, or a local note).</summary>
        public static void Add(string text)
        {
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
            me.ChatRpc(new FixedString128Bytes(t));
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
            if (e.type == EventType.KeyDown && Time.frameCount != s_OpenedFrame)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.Use(); Submit(me); return; }
                if (e.keyCode == KeyCode.Escape) { e.Use(); Close(); return; }
            }
            var st = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(15 * k), richText = false };
            st.normal.background = null;
            st.focused.background = null;
            st.normal.textColor = st.focused.textColor = Color.white;
            GUI.SetNextControlName("chat line");
            s_Text = GUI.TextField(new Rect(r.x + 6, r.y + 2, r.width - 12, r.height - 4), s_Text, 70, st);
            GUI.FocusControl("chat line");
            if (s_Text.Length == 0) shadowed(new Rect(r.x + 10, r.y + 3, r.width, r.height), "<color=#888888>Say something...   (/kill, /help)   Enter: send   Esc: cancel</color>", wrap);
        }
    }

    public partial class PlayerNet
    {
        float m_NextChat;

        /// <summary>A chat line: everyone sees it, in the sender's team colour.</summary>
        [Rpc(SendTo.Server)]
        public void ChatRpc(FixedString128Bytes text)
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
            NetGame.Instance.ChatLineRpc(new FixedString128Bytes(line));
        }
    }

    public partial class NetGame
    {
        [Rpc(SendTo.ClientsAndHost)]
        public void ChatLineRpc(FixedString128Bytes line)
        {
            Chat.Add(line.ToString());
            Sfx.Play2D(Sfx.Pop, 0.25f, 0.1f);
        }
    }
}
