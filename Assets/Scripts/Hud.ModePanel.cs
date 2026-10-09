using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The game modes' own panel (NetGame.GameModes.cs), under the timer: one chip per team in its colour - yours framed
    /// in white - showing the mode's score at a glance: 3 Goal's goals as pips, Progress's bar, Bedwars' machine health
    /// (or DOWN) and how many are still alive, Assassin's skulls handed in out of the enemies there are, and in
    /// Domination who holds the advanced trades. The line under the timer just says the goal in a few words (ModeGoal).
    /// </summary>
    public partial class Hud
    {
        /// <summary>The mode's goal in a few words (null: Classic / Primitive / the tutorial).</summary>
        static string ModeGoal(int myTeam)
        {
            var g = NetGame.Instance;
            if (g == null || !Cfg.ClassicMode) return null;
            if (Cfg.Bedwars) return g.MachineDown(myTeam) ? "<color=#ff7777>Your machine is gone - no more respawns!</color>" : "Smash their machines - guard yours";
            if (Cfg.ThreeGoal) return $"First to {Cfg.GoalsToWin} goals wins";
            if (Cfg.ProgressMode) return "Keep the ball in your machine to fill your bar";
            if (Cfg.Assassin) return "Kill them - take their skulls to your machine (E)";
            int bt = g.BallTeam.Value;
            return bt == myTeam ? "<color=#77ff77>You have the ball: advanced trades unlocked!</color>" : "Hold the ball for the advanced trades";
        }

        /// <summary>The panel under the timer (only in the five modes, once the wall's down).</summary>
        void DrawModePanel(NetGame g, int myTeam, float k, float y)
        {
            if (g == null || !Cfg.ClassicMode || g.S != GameState.BallLive || Cfg.Bedwars) return; // (Bedwars: just the small bed list, top left - DrawBedList)
            float sw = Screen.width, cw = 170 * k, ch = 40 * k, gap = 8 * k;
            int n = Cfg.TeamCount;
            float x0 = sw / 2f - (n * cw + (n - 1) * gap) / 2f;
            var nameSt = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(15 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var valSt = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(15 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            int enemiesOf(int t) { int e = 0; foreach (var p in PlayerNet.All) if (p != null && p.Team.Value != t) e++; return e; }
            for (int t = 0; t < n; t++)
            {
                var c = Cfg.TeamColor[t];
                var r = new Rect(x0 + t * (cw + gap), y, cw, ch);
                Fill(r, new Color(0f, 0f, 0f, 0.55f));
                Fill(new Rect(r.x, r.y, 6 * k, r.height), c);
                if (t == myTeam) Frame(r, new Color(1f, 1f, 1f, 0.85f), 2f);
                GUI.Label(new Rect(r.x + 12 * k, r.y, 70 * k, r.height), $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(c, Color.white, 0.35f))}>{Cfg.TeamName[t]}</color>", nameSt);
                var inner = new Rect(r.x + 80 * k, r.y + 8 * k, r.width - 88 * k, r.height - 16 * k);
                if (Cfg.ThreeGoal) Pips(inner, g.GoalsOf(t), Cfg.GoalsToWin, c);
                else if (Cfg.ProgressMode)
                {
                    float p = g.ProgressOf(t);
                    Fill(inner, new Color(1f, 1f, 1f, 0.15f));
                    Fill(new Rect(inner.x, inner.y, inner.width * p, inner.height), c);
                    GUI.Label(inner, $"{Mathf.FloorToInt(p * 100f)}%", new GUIStyle(valSt) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(13 * k) });
                }
                else if (Cfg.Bedwars)
                {
                    if (g.MachineDown(t))
                    {
                        int alive = 0;
                        foreach (var p in PlayerNet.All) if (p != null && p.Team.Value == t && !p.Dead.Value) alive++;
                        GUI.Label(inner, alive > 0 ? $"<color=#ff7777>DOWN</color>  {alive} left" : "<color=#ff7777>OUT</color>", valSt);
                    }
                    else Pips(inner, Cfg.MachineHitsToBreak - g.HitsOn(t), Cfg.MachineHitsToBreak, c);
                }
                else if (Cfg.Assassin) GUI.Label(inner, $"{g.SkullsOf(t)} / {enemiesOf(t)} skulls", valSt);
                else if (Cfg.Domination)
                {
                    if (g.BallTeam.Value == t) { Fill(inner, new Color(c.r, c.g, c.b, 0.6f)); GUI.Label(inner, "TRADES", new GUIStyle(valSt) { alignment = TextAnchor.MiddleCenter }); }
                    else GUI.Label(inner, "-", new GUIStyle(valSt) { alignment = TextAnchor.MiddleCenter });
                }
            }
        }

        /// <summary>Bedwars: a small list in the top left (under the base radar) - each team and whether it still has its
        /// bed (its machine), nothing more.</summary>
        void DrawBedList(NetGame g, float k, float y)
        {
            if (g == null || !Cfg.Bedwars || (g.S != GameState.BallLive && !g.WallUp)) return;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var c = Cfg.TeamColor[t];
                bool bed = !g.MachineDown(t);
                var r = new Rect(10, y, 200 * k, 24 * k);
                Fill(r, new Color(0f, 0f, 0f, 0.45f));
                Fill(new Rect(r.x, r.y, 5 * k, r.height), c);
                Shadowed(new Rect(r.x + 10 * k, r.y, r.width, r.height), $"<b><color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(c, Color.white, 0.35f))}>{Cfg.TeamName[t]}</color></b>  " + (bed ? "<color=#9dff9d>BED</color>" : "<color=#ff7777>NO BED</color>"), m_Small);
                y += 27 * k;
            }
            // the two clocks: when the chambers break (no respawns), then when the bases break down
            float cl = g.ChambersLeft, bl = g.BasesLeft;
            string clock = cl > 0f ? $"Chambers break in <b>{Clock(cl)}</b>" : bl > 0f ? $"<color=#ff9a7a>Bases break in <b>{Clock(bl)}</b></color>" : g.BasesBreakAt.Value > 0 ? "<color=#ff7777><b>BASES DOWN</b></color>" : null;
            if (clock != null)
            {
                var r = new Rect(10, y, 200 * k, 24 * k);
                Fill(r, new Color(0f, 0f, 0f, 0.45f));
                Shadowed(new Rect(r.x + 10 * k, r.y, r.width, r.height), clock, m_Small);
            }
        }

        /// <summary>`have` of `of` little squares, filled in the team's colour from the left.</summary>
        void Pips(Rect r, int have, int of, Color c)
        {
            float k = m_Scale, s = Mathf.Min(r.height, (r.width - (of - 1) * 5 * k) / Mathf.Max(1, of));
            float x = r.xMax - (of * s + (of - 1) * 5 * k);
            for (int i = 0; i < of; i++)
            {
                var p = new Rect(x + i * (s + 5 * k), r.y + (r.height - s) / 2f, s, s);
                Fill(p, i < have ? c : new Color(1f, 1f, 1f, 0.12f));
                Frame(p, new Color(1f, 1f, 1f, 0.5f), 1f);
            }
        }
    }
}
