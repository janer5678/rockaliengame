using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The exhibition build's screens (Exhibition.cs), in the main menu's own look over its UFO in space: the front screen
    /// (HOST, or a join code and JOIN), and once you're in, the READY screen - one big READY 1/4, and under it the two teams
    /// (click the other one to swap). The join code stays in the corner, for a PC that needs to get back in.
    /// </summary>
    public partial class Hud
    {
        /// <summary>(tests) when the front screen and the READY screen were last drawn.</summary>
        public static float ExhibitionFrontShownAt = -10f, ExhibitionLobbyShownAt = -10f;

        void ExhibitionBackdrop()
        {
            if (MenuSpace.Fade > 0.001f) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, MenuSpace.Fade));
            GUI.DrawTexture(new Rect(0, 0, 760 * m_Scale, Screen.height), LeftFade());
            DrawPunkTitle(MainTitle, new Vector2(60 * m_Scale, 82 * m_Scale), 86);
        }

        void DrawExhibitionBlack(string text)
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            Shadowed(new Rect(0, Screen.height / 2 - 40, Screen.width, 80), text, new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(30 * m_Scale) });
        }

        void DrawExhibitionFront(Bootstrap boot)
        {
            if (boot.BackingToLobby) { DrawExhibitionBlack("Loading..."); return; }
            ExhibitionFrontShownAt = Time.time;
            float k = m_Scale, x = 70 * k, y = 230 * k, w = 470 * k;
            ExhibitionBackdrop();
            GUI.enabled = !boot.RelayBusy;
            if (BigBtn(ref y, x, w, "HOST", k_Red)) boot.ExhibitionHost();
            y += 16 * k;
            GUI.Label(new Rect(x, y, w, 26 * k), "<b>JOIN CODE</b>", m_Label);
            y += 28 * k;
            string typed = GUI.TextField(new Rect(x, y, w, 52 * k), boot.Ip, 32, new GUIStyle(m_Field) { fontSize = Mathf.RoundToInt(28 * k) });
            boot.Ip = typed.IndexOf('.') >= 0 ? typed : typed.ToUpperInvariant();
            y += 60 * k;
            if (BigBtn(ref y, x, w, "JOIN", k_Blue))
            {
                if (string.IsNullOrWhiteSpace(boot.Ip)) boot.SetStatus("Type the join code from the host's screen");
                else boot.Join();
            }
            if (boot.RelayFailed && BigBtn(ref y, x, w, "HOST ON THIS NETWORK", k_Acid)) boot.ExhibitionHostLan();
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(boot.Status)) Note(ref y, x, w, "<color=#ffcc66>" + boot.Status + "</color>");
            if (SmallBtn(new Rect(Screen.width - 136 * k, Screen.height - 56 * k, 120 * k, 40 * k), "QUIT")) Application.Quit();
        }

        void DrawExhibitionLobby(Bootstrap boot, PlayerNet me)
        {
            LobbyShownAt = Time.time;
            if (Exhibition.MatchPhase) { DrawExhibitionBlack("The match is starting..."); return; } // (coming back in from the tutorial: nothing to press)
            ExhibitionLobbyShownAt = Time.time;
            ShipLobby.Customising = false;
            float k = m_Scale, x = 70 * k, y = 230 * k, w = 470 * k;
            ExhibitionBackdrop();
            var g = NetGame.Instance;
            int readyN = 0;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.LobbyReady.Value) readyN++;
            bool mine = me != null && me.LobbyReady.Value;
            if (BigBtn(ref y, x, w, $"READY{(mine ? " ✔" : "")}  {readyN}/{Exhibition.Needed}", mine ? new Color(0.5f, 1f, 0.55f) : k_Acid) && me != null) me.LobbyReadyRpc(!mine);
            // the two teams under it
            y += 6 * k;
            float gap = 10 * k, tw = (w - gap) / 2f, th = 120 * k;
            for (int t = 0; t < 2; t++)
            {
                var r = new Rect(x + t * (tw + gap), y, tw, th);
                var c = Cfg.TeamColor[t];
                bool on = me != null && me.Team.Value == t;
                int n = 0;
                string names = "";
                foreach (var p in PlayerNet.All)
                {
                    if (p == null || !p.IsSpawned || p.Team.Value != t) continue;
                    n++;
                    names += $"{p.DisplayName}{(p.LobbyReady.Value ? " <color=#7dff7a>✔</color>" : "")}\n";
                }
                bool hover = r.Contains(Event.current.mousePosition);
                if (hover) MouseOverUI = true;
                Fill(r, new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, on || hover ? 0.95f : 0.7f));
                if (on) Frame(r, Color.white, 3f);
                GUI.Label(new Rect(r.x, r.y + 8 * k, r.width, 30 * k), $"<b>{Cfg.TeamName[t]} TEAM</b>{(on ? "  (you)" : "")}", new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(20 * k) });
                GUI.Label(new Rect(r.x, r.y + 42 * k, r.width, r.height - 46 * k), names, new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(16 * k), alignment = TextAnchor.UpperCenter });
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && me != null && !on && n < Cfg.TeamCap(t)) { ClickSound(); me.LobbyTeamRpc((byte)t); }
            }
            y += th + 10 * k;
            string say = g != null && g.StartCounting ? $"<color=#7dff7a>Everyone's ready! Starting in {Mathf.CeilToInt(g.StartsIn)}...</color>"
                : me == null ? "You're watching this one."
                : mine ? "Waiting for everyone else..." : "Press READY when you're set.";
            Note(ref y, x, w, say);
            DrawExhibitionCode(boot, 22f);
        }

        /// <summary>Over in the corner: what another PC needs to get in (or back in).</summary>
        void DrawExhibitionCode(Bootstrap boot, float size)
        {
            float k = m_Scale;
            string code = boot.RoomId;
            if (string.IsNullOrEmpty(code)) return;
            var st = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(size * k), alignment = TextAnchor.LowerRight };
            Shadowed(new Rect(0, Screen.height - 60 * k, Screen.width - 24 * k, 44 * k), $"JOIN CODE  <b>{code}</b>", st);
        }
    }
}
