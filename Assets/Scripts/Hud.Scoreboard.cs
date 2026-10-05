using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The scoreboard: hold the scoreboard key (Tab; Controls can rebind it) and every player is listed, grouped by team
    /// under a bar in the team's colour - name, kills, deaths and ping (the server keeps and syncs them: PlayerNet.Identity.cs).
    /// The mouse is free while it's up: MESSAGE next to a player opens the chat line as a private message to just them
    /// (a whisper - Chat.cs). Your own row is lit; dead players are greyed.
    /// </summary>
    public partial class Hud
    {
        /// <summary>AutoTest: show the scoreboard without the key held.</summary>
        public static bool TestScoreboard;
        /// <summary>Test hooks: when it was last drawn (Time.time, -1 never), how many player rows and MESSAGE buttons it had.</summary>
        public static float ScoreboardShownAt { get; private set; } = -1f;
        public static int ScoreboardRows { get; private set; }
        public static int ScoreboardButtons { get; private set; }

        readonly List<PlayerNet> m_ScoreRows = new List<PlayerNet>();
        float m_ScoreOpened = -10f;
        bool m_ScoreWasOpen;

        /// <summary>AutoTest: press the MESSAGE button next to this player.</summary>
        public static void TestMessage(PlayerNet p)
        {
            if (p != null) Chat.BeginWhisper(p.OwnerClientId, p.DisplayName, p.Team.Value);
        }

        void DrawScoreboard(PlayerNet me, PlayerController pc)
        {
            bool open = pc.ScoreboardOpen || TestScoreboard;
            if (open && !m_ScoreWasOpen) m_ScoreOpened = Time.unscaledTime;
            m_ScoreWasOpen = open;
            if (!open) return;
            ScoreboardShownAt = Time.time;
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            var e = Event.current;
            // it drops in quickly from just above where it sits
            float inT = Mathf.Clamp01((Time.unscaledTime - m_ScoreOpened) / 0.12f);
            float ease = 1f - (1f - inT) * (1f - inT);

            int teams = Mathf.Clamp(Cfg.TeamCount, 1, Cfg.TeamColor.Length);
            float w = Mathf.Min(720 * k, sw - 40), rowH = 32 * k, headH = 30 * k, titleH = 46 * k, colsH = 24 * k, gap = 8 * k;
            int shownRows = 0, shownTeams = 0;
            for (int t = 0; t < teams; t++)
            {
                int n = 0;
                foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.Team.Value == t) n++;
                if (n == 0 && teams > 2) continue; // (free for all: only the teams somebody is on)
                shownTeams++;
                shownRows += Mathf.Max(1, n);
            }
            float h = titleH + colsH + shownTeams * (headH + gap) + shownRows * rowH + 34 * k;
            float x = (sw - w) / 2, y = Mathf.Max(70 * k, (sh - h) * 0.4f) - (1f - ease) * 16 * k;
            var panel = new Rect(x, y, w, h);
            if (panel.Contains(e.mousePosition)) MouseOverUI = true;
            var oldCol = GUI.color;
            GUI.color = new Color(1, 1, 1, ease);
            Fill(panel, new Color(0.03f, 0.035f, 0.05f, 0.88f * ease));
            Fill(new Rect(x, y, w, 3 * k), GameSettings.AccentColor);

            var game = NetGame.Instance;
            string mode = Cfg.RulesName(Cfg.Rules).ToUpper() + "  ·  " + Cfg.ModeLabel;
            Shadowed(new Rect(x + 16 * k, y + 8 * k, w, 30 * k), $"<b><size={Mathf.RoundToInt(22 * k)}>SCOREBOARD</size></b>   <color=#aaaaaa>{mode}</color>", m_Label);
            if (game != null && (game.S == GameState.PreBall || game.S == GameState.BallLive || game.S == GameState.SuddenDeath) && !Cfg.Tutorial)
                Shadowed(new Rect(x, y + 10 * k, w - 16 * k, 28 * k), Clock(game.TimeLeft), new GUIStyle(m_Label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold });

            // columns: name | kills | deaths | ping | message
            float nameX = x + 22 * k, btnW = 110 * k, pingW = 78 * k, numW = 64 * k;
            float btnX = x + w - btnW - 12 * k, pingX = btnX - pingW - 8 * k, dX = pingX - numW, kX = dX - numW;
            float cy = y + titleH;
            var colSt = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            colSt.normal.textColor = new Color(0.7f, 0.7f, 0.75f, ease);
            GUI.Label(new Rect(nameX, cy, 200 * k, colsH), "PLAYER", new GUIStyle(colSt) { alignment = TextAnchor.MiddleLeft });
            GUI.Label(new Rect(kX, cy, numW, colsH), "KILLS", colSt);
            GUI.Label(new Rect(dX, cy, numW, colsH), "DEATHS", colSt);
            GUI.Label(new Rect(pingX, cy, pingW, colsH), "PING", colSt);
            cy += colsH;

            var nameSt = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, richText = false, wordWrap = false, clipping = TextClipping.Clip };
            var numSt = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = false };
            var btnSt = new GUIStyle(m_Button) { fontSize = Mathf.RoundToInt(13 * k), fontStyle = FontStyle.Bold };
            int rows = 0, buttons = 0;
            for (int t = 0; t < teams; t++)
            {
                m_ScoreRows.Clear();
                int tk = 0, td = 0;
                foreach (var p in PlayerNet.All)
                {
                    if (p == null || !p.IsSpawned || p.Team.Value != t) continue;
                    m_ScoreRows.Add(p);
                    tk += p.Kills.Value;
                    td += p.Deaths.Value;
                }
                if (m_ScoreRows.Count == 0 && teams > 2) continue;
                // most kills first, then fewest deaths, then their number in the team
                m_ScoreRows.Sort((a, b) => a.Kills.Value != b.Kills.Value ? b.Kills.Value.CompareTo(a.Kills.Value)
                    : a.Deaths.Value != b.Deaths.Value ? a.Deaths.Value.CompareTo(b.Deaths.Value) : a.Slot.Value.CompareTo(b.Slot.Value));
                var tc = Cfg.TeamColor[t];
                var light = PlayerNet.NameColor(t);
                // the team's bar, in its colour
                var hr = new Rect(x + 10 * k, cy, w - 20 * k, headH);
                Fill(hr, new Color(tc.r, tc.g, tc.b, 0.55f * ease));
                Fill(new Rect(hr.x, hr.y, 5 * k, hr.height), new Color(light.r, light.g, light.b, ease));
                Shadowed(new Rect(nameX, cy, 300 * k, headH), $"<b>{Cfg.TeamLabel(t)}</b>" + (me.Team.Value == t ? "   <color=#ffffffaa>your team</color>" : ""), new GUIStyle(m_Label) { alignment = TextAnchor.MiddleLeft });
                numSt.normal.textColor = new Color(1, 1, 1, 0.9f * ease);
                GUI.Label(new Rect(kX, cy, numW, headH), tk.ToString(), numSt);
                GUI.Label(new Rect(dX, cy, numW, headH), td.ToString(), numSt);
                cy += headH;
                if (m_ScoreRows.Count == 0)
                {
                    var none = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleLeft };
                    none.normal.textColor = new Color(0.6f, 0.6f, 0.6f, ease);
                    GUI.Label(new Rect(nameX, cy, w, rowH), "nobody yet", none);
                    cy += rowH;
                }
                for (int i = 0; i < m_ScoreRows.Count; i++)
                {
                    var p = m_ScoreRows[i];
                    bool mine = p == me, dead = p.Dead.Value;
                    var rr = new Rect(x + 10 * k, cy, w - 20 * k, rowH - 2 * k);
                    Fill(rr, mine ? new Color(1f, 0.85f, 0.3f, 0.2f * ease) : new Color(1, 1, 1, (i % 2 == 0 ? 0.06f : 0.03f) * ease));
                    if (mine) Fill(new Rect(rr.x, rr.y, 3 * k, rr.height), new Color(1f, 0.85f, 0.3f, ease));
                    float da = dead ? 0.45f : 1f;
                    nameSt.normal.textColor = new Color(light.r, light.g, light.b, da * ease);
                    GUI.Label(new Rect(nameX, cy, kX - nameX - 8 * k, rr.height), p.DisplayName + (mine ? "  (you)" : "") + (dead ? "  - dead" : ""), nameSt);
                    numSt.normal.textColor = new Color(1, 1, 1, da * ease);
                    GUI.Label(new Rect(kX, cy, numW, rr.height), p.Kills.Value.ToString(), numSt);
                    GUI.Label(new Rect(dX, cy, numW, rr.height), p.Deaths.Value.ToString(), numSt);
                    // ping: green / yellow / red; the host has none
                    bool host = p.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId;
                    int ms = p.Ping.Value;
                    var pcCol = host ? new Color(0.7f, 0.7f, 0.75f) : ms < 80 ? new Color(0.5f, 1f, 0.55f) : ms < 160 ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.45f, 0.4f);
                    numSt.normal.textColor = new Color(pcCol.r, pcCol.g, pcCol.b, ease);
                    GUI.Label(new Rect(pingX, cy, pingW, rr.height), host ? "host" : ms + " ms", numSt);
                    if (!mine)
                    {
                        // a private message to just this player: the chat line opens, addressed to them
                        buttons++;
                        if (BtnAt(new Rect(btnX, cy + 2 * k, btnW, rr.height - 4 * k), "MESSAGE", btnSt))
                        {
                            Chat.BeginWhisper(p.OwnerClientId, p.DisplayName, p.Team.Value);
                            TestScoreboard = false;
                        }
                    }
                    rows++;
                    cy += rowH;
                }
                cy += gap;
            }
            var foot = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter };
            foot.normal.textColor = new Color(0.65f, 0.65f, 0.7f, ease);
            GUI.Label(new Rect(x, y + h - 30 * k, w, 24 * k), $"Hold {Binds.Name(Bind.Scoreboard)}  ·  MESSAGE: a private message only that player sees  ·  click a whisper in the chat to answer it", foot);
            GUI.color = oldCol;
            ScoreboardRows = rows;
            ScoreboardButtons = buttons;
        }
    }
}
