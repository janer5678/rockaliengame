using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The ship lobby's screen (ShipLobby.cs is the room): everyone's name over their alien (in their team's colour, with a
    /// tick once they're READY), the buttons along the bottom left like the reference - LEAVE (closes the lobby), COPY ROOM
    /// ID (the host's IPv4 address, for a friend to type into JOIN) and GAME OPTIONS (the host: change the game mode,
    /// length and team sizes; everyone READYs again after) - in team games a JOIN row for each team, and the big green
    /// READY bottom right. The chat works as usual.
    /// </summary>
    public partial class Hud
    {
        bool m_LobbyOptions;
        float m_CopiedAt = -10f;

        /// <summary>(tests) the lobby's screen was drawn this frame.</summary>
        public static float LobbyShownAt = -10f;

        void DrawLobby(Bootstrap boot, PlayerNet me)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            LobbyShownAt = Time.time;
            var g = NetGame.Instance;
            // the title, top left: what's being played and how full it is
            var title = new GUIStyle(m_Big) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(40 * k) };
            Shadowed(new Rect(28 * k, 44 * k, sw, 60 * k), "<b>LOBBY</b>", title); // (under the FPS counter)
            int players = ShipLobby.Seated.Count;
            string sub = $"{Cfg.RulesName(Cfg.Rules).ToUpper()}  ·  {Cfg.ModeLabel}  ·  {players}/{Cfg.PlayersNeeded} players";
            Shadowed(new Rect(30 * k, 90 * k, sw, 30 * k), sub, new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(18 * k) });
            int readyN = 0;
            foreach (var p in ShipLobby.Seated) if (p.LobbyReady.Value) readyN++;
            string wait = g != null && g.StartCounting ? $"<color=#7dff7a>Starting in {Mathf.CeilToInt(g.StartsIn)}...</color>"
                : players < 2 ? "Waiting for someone to join - COPY ROOM ID and send it to a friend"
                : $"{readyN}/{players} ready - the match starts when everyone is";
            Shadowed(new Rect(30 * k, 118 * k, sw, 26 * k), wait, new GUIStyle(m_Small) { fontSize = Mathf.RoundToInt(15 * k) });

            // name tags over the aliens
            var tag = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(19 * k), fontStyle = FontStyle.Bold };
            foreach (var p in ShipLobby.Seated)
            {
                if (!ShipLobby.HeadOnScreen(p, out var at)) continue;
                var c = Cfg.TeamColor[Mathf.Clamp(p.Team.Value, 0, 3)];
                string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(c, Color.white, 0.35f));
                string tick = p.LobbyReady.Value ? "  <color=#7dff7a>✔</color>" : "";
                Shadowed(new Rect(at.x - 150 * k, at.y - 34 * k, 300 * k, 30 * k), $"<color=#{hex}>{p.DisplayName}</color>{tick}", tag);
            }

            // the buttons along the bottom left, white-framed like the reference
            float bh = 40 * k, by = sh - bh - 22 * k, bx = 24 * k;
            if (FramedBtn(ref bx, by, bh, "LEAVE")) { boot.Leave(); return; }
            if (FramedBtn(ref bx, by, bh, Time.time - m_CopiedAt < 2f ? "COPIED!" : "COPY ROOM ID"))
            {
                string ip = boot.IsHostSession ? Tutorial.LocalIp() : boot.Ip.Trim();
                GUIUtility.systemCopyBuffer = string.IsNullOrEmpty(ip) ? "127.0.0.1" : ip;
                m_CopiedAt = Time.time;
            }
            if (boot.IsHostSession && FramedBtn(ref bx, by, bh, "GAME OPTIONS")) m_LobbyOptions = !m_LobbyOptions;

            // team games: a JOIN button for each team, with how many are on it
            if (Cfg.TeamCount >= 2 && !Cfg.FreeForAll)
            {
                float tw = 190 * k, th = 40 * k, gap = 12 * k;
                float tx = (sw - (tw * Cfg.TeamCount + gap * (Cfg.TeamCount - 1))) / 2f, ty = sh - th - 84 * k;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int n = 0;
                    foreach (var p in ShipLobby.Seated) if (p.Team.Value == t) n++;
                    var r = new Rect(tx + t * (tw + gap), ty, tw, th);
                    bool mine = me.Team.Value == t, full = n >= Cfg.TeamCap(t);
                    var c = Cfg.TeamColor[t];
                    Fill(r, new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, mine ? 0.95f : 0.7f));
                    if (mine) Frame(r, Color.white, 2f);
                    if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
                    string label = $"<b>{Cfg.TeamName[t]}</b>  {n}/{Cfg.TeamCap(t)}" + (mine ? "  (you)" : full ? "  FULL" : "");
                    if (GUI.Button(r, label, new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(17 * k) }) && !mine && !full) { me.LobbyTeamRpc((byte)t); ClickSound(); }
                }
            }

            // READY, bottom right: big and green
            {
                float rw = 230 * k, rh = 64 * k;
                var r = new Rect(sw - rw - 26 * k, sh - rh - 22 * k, rw, rh);
                bool ready = me.LobbyReady.Value;
                Fill(r, ready ? new Color(0.12f, 0.75f, 0.25f, 0.98f) : new Color(0.08f, 0.45f, 0.14f, 0.92f));
                Frame(r, ready ? Color.white : new Color(0.6f, 1f, 0.6f), 3f);
                if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
                var st = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(28 * k), fontStyle = FontStyle.Bold };
                if (GUI.Button(r, ready ? "READY ✔" : "READY", st)) { me.LobbyReadyRpc(!ready); ClickSound(); }
                if (ready) Shadowed(new Rect(r.x - 40 * k, r.y - 26 * k, r.width + 40 * k, 22 * k), "<color=#bbbbbb>click again to un-ready</color>", new GUIStyle(m_Small) { alignment = TextAnchor.MiddleRight });
            }

            if (m_LobbyOptions && boot.IsHostSession) DrawLobbyOptions(boot);
            Chat.Draw(k, m_Small, Fill, Shadowed);
        }

        /// <summary>A white-framed button along the bottom (LEAVE, COPY ROOM ID...), moving x on past it.</summary>
        bool FramedBtn(ref float x, float y, float h, string text)
        {
            float k = m_Scale;
            var st = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(18 * k), font = m_Center.font };
            float w = st.CalcSize(new GUIContent(text)).x + 30 * k;
            var r = new Rect(x, y, w, h);
            x += w + 8 * k;
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover) MouseOverUI = true;
            Fill(r, hover ? new Color(1f, 1f, 1f, 0.25f) : new Color(0f, 0f, 0f, 0.75f));
            Frame(r, Color.white, 2f);
            bool hit = GUI.Button(r, text, st);
            if (hit) ClickSound();
            return hit;
        }

        void Frame(Rect r, Color c, float w)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        /// <summary>The host's GAME OPTIONS in the lobby: the game mode, the game length and (team games) the team sizes.
        /// The map and its size stay as hosted. APPLY sends it to everyone (NetGame.ServerApplyLobbyOptions).</summary>
        void DrawLobbyOptions(Bootstrap boot)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            float w = Mathf.Min(sw - 40, 620 * k), h = Mathf.Min(sh - 160 * k, 560 * k);
            var r = new Rect((sw - w) / 2, (sh - h) / 2 - 30 * k, w, h);
            MouseOverUI = true;
            Fill(r, new Color(0.05f, 0.04f, 0.09f, 0.96f));
            Frame(r, new Color(1f, 1f, 1f, 0.8f), 2f);
            GUILayout.BeginArea(new Rect(r.x + 22 * k, r.y + 16 * k, r.width - 44 * k, r.height - 32 * k));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt(26 * k)}>GAME OPTIONS</size></b>", m_Label);
            GUILayout.FlexibleSpace();
            if (Btn("Close", GUILayout.Width(110 * k), GUILayout.Height(32 * k))) m_LobbyOptions = false;
            GUILayout.EndHorizontal();
            int key = Bootstrap.MapChoice;
            key = DrawRulesPicker(key);
            DrawLengthRow(key);
            if (!Cfg.FreeForAll && Cfg.PlayersNeeded > 2) key = DrawCapsRow(key); // (team games: the team sizes)
            GUILayout.Label($"<color=#bbbbbb>The map ({ThemeMaps.Label(Cfg.Map)}, {Cfg.SizeLabel(Cfg.Size)}) stays as hosted. Changing anything here un-readies everyone.</color>", m_SmallWrap);
            GUILayout.FlexibleSpace();
            if (Btn("APPLY", m_Primary, GUILayout.Height(44 * k)))
            {
                Cfg.SavePrefs();
                Bootstrap.MapChoice = key;
                PlayerPrefs.SetInt("RockGame.Map", key);
                NetGame.Instance?.ServerApplyLobbyOptions(key);
                m_LobbyOptions = false;
            }
            GUILayout.EndArea();
        }
    }
}
