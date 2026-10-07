using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The ship lobby's screen (ShipLobby.cs is the room): everyone's name over their alien (in their team's colour, with a
    /// tick once they're READY), the buttons along the bottom left like the reference - LEAVE (closes the lobby), COPY ROOM
    /// ID (the host's IPv4 address, for a friend to type into JOIN) and GAME OPTIONS (the host: change the game mode,
    /// length and team sizes; everyone READYs again after), LOBBY LOOK (everyone: the room's colours, lights and post
    /// processing, for you) and, on the telly's game (LobbyArcade.cs), STOP PLAYING - a JOIN option for each team with a
    /// grey SPECTATE twice as long under them (spectating, your alien stays on the couch, grey; a team takes a seat on it
    /// again), and the big green READY bottom right. The chat works as usual.
    /// </summary>
    public partial class Hud
    {
        bool m_LobbyOptions;
        float m_CopiedAt = -10f;

        /// <summary>(tests) the lobby's screen was drawn this frame.</summary>
        public static float LobbyShownAt = -10f;
        static readonly System.Collections.Generic.List<Container> s_Bags = new System.Collections.Generic.List<Container>();

        void DrawLobby(Bootstrap boot, PlayerNet me)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            LobbyShownAt = Time.time;
            var g = NetGame.Instance;
            ShipLobby.Customising = false; // (alien customisation is off for now)
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
            // who's watching
            if (g != null && g.Spectators.Count > 0)
            {
                var names = new System.Text.StringBuilder();
                foreach (var s in g.Spectators) { if (names.Length > 0) names.Append(", "); names.Append(s.Name.ToString()); }
                Shadowed(new Rect(30 * k, 142 * k, sw, 26 * k), $"<color=#bbbbbb>Spectating: {names}</color>", new GUIStyle(m_Small) { fontSize = Mathf.RoundToInt(15 * k) });
            }

            // name tags over the aliens (a little over their heads; not while the camera's on the telly)
            var tag = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(19 * k), fontStyle = FontStyle.Bold };
            if (!LobbyArcade.Focused)
            {
                foreach (var p in ShipLobby.Seated)
                {
                    if (!ShipLobby.HeadOnScreen(p, out var at)) continue;
                    var c = Cfg.TeamColor[Mathf.Clamp(p.Team.Value, 0, 3)];
                    string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(c, Color.white, 0.35f));
                    string tick = p.LobbyReady.Value ? $"  <size={Mathf.RoundToInt(38 * k)}><color=#7dff7a>✔</color></size>" : ""; // (big: ready at a glance)
                    string pad = LobbyArcade.InGame(p) ? $"  <size={Mathf.RoundToInt(14 * k)}><color=#ffd84a>ON THE TELLY</color></size>" : "";
                    Shadowed(new Rect(at.x - 150 * k, at.y - 26 * k, 300 * k, 30 * k), $"<color=#{hex}>{p.DisplayName}</color>{tick}{pad}", tag);
                }
                // the spectators, grey on the end of the couch
                foreach (var s in ShipLobby.SeatedSpectators)
                {
                    if (!ShipLobby.SpectatorHeadOnScreen(s.Id, out var at)) continue;
                    Shadowed(new Rect(at.x - 150 * k, at.y - 26 * k, 300 * k, 30 * k), $"<color=#b4b4b4>{s.Name}</color>  <size={Mathf.RoundToInt(14 * k)}><color=#8c8c8c>SPECTATING</color></size>", tag);
                }
            }
            else
            {
                // playing the telly's game: how to play, under the title
                Shadowed(new Rect(30 * k, 168 * k, sw, 26 * k), me != null
                    ? "<color=#ffd84a><b>TELLY GAME</b></color>  WASD to move · bump trees to chop them (+1) · run the ball to your machine (+5) · run into the carrier to steal it"
                    : "<color=#ffd84a><b>TELLY GAME</b></color>  watching - pick a team to play too", new GUIStyle(m_Small) { fontSize = Mathf.RoundToInt(15 * k) });
            }

            // the buttons along the bottom left, white-framed like the reference (CUSTOMISE ALIEN is off for now)
            float bh = 40 * k, by = sh - bh - 22 * k, bx = 24 * k;
            if (FramedBtn(ref bx, by, bh, "BACK")) { LobbyBackPressed(boot); return; } // (Hud.LobbyBack.cs)
            if (FramedBtn(ref bx, by, bh, Time.time - m_CopiedAt < 2f ? "COPIED!" : "COPY ROOM ID"))
            {
                string ip = boot.IsHostSession ? Tutorial.LocalIp() : boot.Ip.Trim();
                GUIUtility.systemCopyBuffer = string.IsNullOrEmpty(ip) ? "127.0.0.1" : ip;
                m_CopiedAt = Time.time;
            }
            if (boot.IsHostSession && !Cfg.Tutorial && FramedBtn(ref bx, by, bh, "GAME OPTIONS")) { m_LobbyOptions = !m_LobbyOptions; m_LobbyLook = false; } // (the AI bots are in there now)
            if (FramedBtn(ref bx, by, bh, "LOBBY LOOK")) { m_LobbyLook = !m_LobbyLook; m_LobbyOptions = false; } // (everyone: the room's look, for you)
            if (LobbyArcade.Focused && FramedBtn(ref bx, by, bh, "STOP PLAYING")) LobbyArcade.Focus(false); // (back to the couch)
            float leftEnd = bx;

            // the teams: a JOIN button for each (how many are on it), and under them, twice as long and grey, SPECTATE
            if (!Cfg.Tutorial || me != null) DrawLobbyTeams(g, me, leftEnd, by);

            if (me == null)
            {
                // spectating: no READY - just a note where it goes
                var nr = new Rect(sw - 420 * k, sh - 86 * k, 394 * k, 64 * k);
                Fill(nr, new Color(0f, 0f, 0f, 0.6f));
                Frame(nr, new Color(1f, 1f, 1f, 0.6f), 2f);
                Shadowed(nr, "<b>SPECTATING</b>\n<size=" + Mathf.RoundToInt(14 * k) + "><color=#bbbbbb>You'll watch the match - pick a team to play</color></size>", new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(22 * k) });
                if (m_LobbyOptions && boot.IsHostSession) DrawLobbyOptions(boot);
                if (m_LobbyLook) DrawLobbyLook();
                Chat.Draw(k, m_Small, Fill, Shadowed);
                return;
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
            if (m_LobbyLook) DrawLobbyLook();
            Chat.Draw(k, m_Small, Fill, Shadowed);
        }

        /// <summary>The team options along the bottom middle: a JOIN button for each team in its colour (in free for all, one
        /// PLAY button), and under them a grey SPECTATE twice as long. Picking SPECTATE gets you up to watch (your alien
        /// stays on the end of the couch, grey); picking a team while you're watching takes a seat on it.</summary>
        void DrawLobbyTeams(NetGame g, PlayerNet me, float leftEnd, float by)
        {
            float k = m_Scale, sw = Screen.width;
            float tw = 190 * k, th = 40 * k, gap = 10 * k;
            bool teams = Cfg.TeamCount >= 2 && !Cfg.FreeForAll;
            int cols = teams ? Cfg.TeamCount : 1;
            float rowW = tw * cols + gap * (cols - 1), specW = teams ? tw * 2f + gap : tw * 2f;
            float tx = Mathf.Max(leftEnd + 12 * k, (sw - Mathf.Max(rowW, specW)) / 2f);
            float ty = by - th - gap; // (the teams over SPECTATE, which is along the bottom with the other buttons)
            var st = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(17 * k) };
            for (int t = 0; t < cols; t++)
            {
                int n = 0;
                foreach (var p in ShipLobby.Seated) if (!teams || p.Team.Value == t) n++;
                int cap = teams ? Cfg.TeamCap(t) : Cfg.PlayersNeeded;
                var r = new Rect(tx + (Mathf.Max(rowW, specW) - rowW) / 2f + t * (tw + gap), ty, tw, th);
                bool mine = me != null && (!teams || me.Team.Value == t), full = n >= cap;
                var c = teams ? Cfg.TeamColor[t] : new Color(0.3f, 0.75f, 0.4f);
                Fill(r, new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, mine ? 0.95f : 0.7f));
                if (mine) Frame(r, Color.white, 2f);
                if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
                string label = $"<b>{(teams ? Cfg.TeamName[t] : "PLAY")}</b>  {n}/{cap}" + (mine ? "  (you)" : full ? "  FULL" : "");
                if (GUI.Button(r, label, st) && !mine && !full)
                {
                    ClickSound();
                    if (me != null) me.LobbyTeamRpc((byte)t);
                    else if (g != null) g.PlayOnTeamRpc((byte)(teams ? t : 255)); // (watching: take a seat on it)
                }
            }
            if (g == null || Cfg.Tutorial) return;
            {
                // SPECTATE: grey, twice as long, under the teams
                int watching = g.IsSpawned ? g.Spectators.Count : 0;
                var r = new Rect(tx + (Mathf.Max(rowW, specW) - specW) / 2f, by, specW, th);
                bool mine = me == null;
                var c = new Color(0.55f, 0.55f, 0.58f);
                Fill(r, new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, mine ? 0.95f : 0.7f));
                if (mine) Frame(r, Color.white, 2f);
                if (r.Contains(Event.current.mousePosition)) MouseOverUI = true;
                string label = $"<b>SPECTATE</b>  {watching}/{Spectator.MaxSpectators}" + (mine ? "  (you)" : "");
                if (GUI.Button(r, label, st) && !mine) { ClickSound(); LobbyArcade.Focus(false); g.SpectateRpc(true); }
            }
        }

        bool m_LobbyLook;
        Vector2 m_LobbyLookScroll;
        static int s_LobbyPrefEdits;
        static bool s_LobbyPrefHooked;

        /// <summary>LOBBY LOOK (everyone): the SHIP LOBBY display settings (LobbyLooks.cs) right there in the lobby, so you
        /// see the room change as you go - its colours, lights and post processing. Changing anything switches the room's
        /// own look on. Only on this PC.</summary>
        void DrawLobbyLook()
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            if (!s_LobbyPrefHooked) { s_LobbyPrefHooked = true; DisplayPref.Changed += () => s_LobbyPrefEdits++; }
            // (on the right, so the room shows beside it)
            float w = Mathf.Min(sw - 40, 640 * k), h = Mathf.Min(sh - 200 * k, 620 * k);
            var r = new Rect(sw - w - 24 * k, 60 * k, w, h);
            MouseOverUI = true;
            Fill(r, new Color(0.05f, 0.04f, 0.09f, 0.94f));
            Frame(r, new Color(1f, 1f, 1f, 0.8f), 2f);
            GUILayout.BeginArea(new Rect(r.x + 18 * k, r.y + 14 * k, r.width - 36 * k, r.height - 28 * k));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size={Mathf.RoundToInt(26 * k)}>LOBBY LOOK</size></b>", m_Label);
            GUILayout.FlexibleSpace();
            bool own = ToggleBtn(GameSettings.LobbyOwn.Value, GameSettings.LobbyOwn.Value ? "Own look: on" : "Own look: off", GUILayout.Width(150 * k), GUILayout.Height(32 * k));
            GameSettings.LobbyOwn.Set(own);
            if (Btn("Defaults", GUILayout.Width(100 * k), GUILayout.Height(32 * k))) GameSettings.ResetLobby();
            if (Btn("Close", GUILayout.Width(90 * k), GUILayout.Height(32 * k))) m_LobbyLook = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#bbbbbb>How this room looks on your screen (also in Settings > Display > SHIP LOBBY). Changing anything switches its own look on.</color>", m_SmallWrap);
            m_LobbyLookScroll = GUILayout.BeginScrollView(m_LobbyLookScroll);
            int before = s_LobbyPrefEdits;
            DrawLobbyLookRows();
            if (s_LobbyPrefEdits != before && !GameSettings.LobbyOwn.Value) GameSettings.LobbyOwn.Set(true);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        string m_NameEditLobby;

        /// <summary>CUSTOMISE ALIEN: the camera's on your alien's head (ShipLobby). Arrows either side of the hat's name
        /// to try each one on (Cosmetics.cs: saved, and everyone sees it - in the lobby and in the game), your name to
        /// type, and DONE (or Esc).</summary>
        void DrawCustomise(PlayerNet me)
        {
            float k = m_Scale, sw = Screen.width, sh = Screen.height;
            var title = new GUIStyle(m_Big) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(40 * k) };
            Shadowed(new Rect(28 * k, 44 * k, sw, 60 * k), "<b>CUSTOMISE ALIEN</b>", title);
            Shadowed(new Rect(30 * k, 92 * k, sw, 26 * k), "Pick a hat and your name - everyone sees them, in here and in the game.", new GUIStyle(m_Small) { fontSize = Mathf.RoundToInt(15 * k) });
            // the hat: its name between two big arrows, low in the middle (the head's above it)
            int hat = me.Hat.Value, n = Cosmetics.HatCount;
            float rowY = sh - 230 * k, nameW = 360 * k, aw = 64 * k, mid = sw / 2f;
            Fill(new Rect(mid - nameW / 2f - aw - 14 * k, rowY - 10 * k, nameW + 2 * aw + 28 * k, aw + 20 * k), new Color(0f, 0f, 0f, 0.55f));
            int pick = -1;
            if (SmallBtn(new Rect(mid - nameW / 2f - aw - 4 * k, rowY, aw, aw), "◀")) pick = (hat + n - 1) % n;
            if (SmallBtn(new Rect(mid + nameW / 2f + 4 * k, rowY, aw, aw), "▶")) pick = (hat + 1) % n;
            Shadowed(new Rect(mid - nameW / 2f, rowY, nameW, aw * 0.62f), $"<b>{Cosmetics.HatNames[Mathf.Clamp(hat, 0, n - 1)].ToUpper()}</b>", new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(28 * k) });
            Shadowed(new Rect(mid - nameW / 2f, rowY + aw * 0.58f, nameW, aw * 0.4f), $"<color=#bbbbbb>{hat + 1} / {n}</color>", new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(15 * k) });
            if (pick >= 0) { Cosmetics.MyHat = pick; me.SetHatRpc((byte)pick); }
            // your name
            float fy = rowY + aw + 30 * k, fw = 360 * k;
            GUI.Label(new Rect(mid - fw / 2f, fy, fw, 24 * k), "<b>YOUR NAME</b>", new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(16 * k) });
            if (m_NameEditLobby == null) m_NameEditLobby = GameSettings.PlayerName;
            var field = new Rect(mid - fw / 2f, fy + 26 * k, fw, 44 * k);
            if (field.Contains(Event.current.mousePosition)) MouseOverUI = true;
            GUI.SetNextControlName("lobbyname");
            string typed = GUI.TextField(field, m_NameEditLobby, GameSettings.PlayerNameMax, new GUIStyle(m_Field) { fontSize = Mathf.RoundToInt(22 * k), alignment = TextAnchor.MiddleCenter });
            NoteTyping("lobbyname");
            if (typed != m_NameEditLobby)
            {
                m_NameEditLobby = typed;
                GameSettings.PlayerName = typed;
                if (GameSettings.PlayerName.Length > 0) MarkNameChosen();
                me.SetNameRpc(new Unity.Collections.FixedString32Bytes(GameSettings.PlayerName));
            }
            // done
            var dr = new Rect(sw - 230 * k - 26 * k, sh - 64 * k - 22 * k, 230 * k, 64 * k);
            Fill(dr, new Color(0.08f, 0.45f, 0.14f, 0.92f));
            Frame(dr, new Color(0.6f, 1f, 0.6f), 3f);
            if (dr.Contains(Event.current.mousePosition)) MouseOverUI = true;
            bool esc = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
            if (GUI.Button(dr, "DONE", new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(28 * k), fontStyle = FontStyle.Bold }) || esc)
            {
                ClickSound();
                ShipLobby.Customising = false;
                GUI.FocusControl(null);
                if (esc) Event.current.Use();
            }
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

        /// <summary>GAME OPTIONS: the AI bots (PlayerNet.Bot.cs) - add one to the team you pick, or take one off it; free for
        /// all just has the one + and -. Happens at once (no APPLY needed).</summary>
        void DrawBotsRow()
        {
            float k = m_Scale;
            GUILayout.Space(6 * k);
            GUILayout.BeginHorizontal();
            RowLabel("AI bots");
            if (Cfg.FreeForAll || Cfg.TeamCount < 2)
            {
                if (Btn("+ BOT", GUILayout.Width(110 * k), GUILayout.Height(30 * k)) && !PlayerNet.ServerAddBot()) Banner("FULL", "There's no room for another bot");
                if (PlayerNet.BotCount > 0 && Btn("- BOT", GUILayout.Width(110 * k), GUILayout.Height(30 * k))) PlayerNet.ServerRemoveBot();
                GUILayout.Label($"<color=#bbbbbb>{PlayerNet.BotCount} in</color>", m_Small, GUILayout.Height(30 * k));
            }
            else
            {
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    int bots = 0;
                    foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.Bot.Value && p.Team.Value == t) bots++;
                    var c = Cfg.TeamColor[t];
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = Color.Lerp(c, Color.white, 0.2f);
                    if (Btn($"+ {Cfg.TeamName[t]}", GUILayout.Width(120 * k), GUILayout.Height(30 * k)) && !PlayerNet.ServerAddBot(t)) Banner("TEAM FULL", $"There's no room on {Cfg.TeamName[t]}");
                    GUI.backgroundColor = old;
                    if (bots > 0 && Btn("-", GUILayout.Width(34 * k), GUILayout.Height(30 * k))) PlayerNet.ServerRemoveBot(t);
                    GUILayout.Label($"<color=#bbbbbb>{bots}</color>", m_Small, GUILayout.Width(22 * k), GUILayout.Height(30 * k));
                    GUILayout.Space(6 * k);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
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
            DrawBotsRow();
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
