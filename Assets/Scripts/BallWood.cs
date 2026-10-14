using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The ball in your machine doubles your wood machine: while it sits in your team's machine (the wall down), your
    /// wood machine piles up BallWoodMul times its usual wood a second (NetGame.ServerTickAutoWood), and the top left of
    /// your screen says so (Hud.DrawBallWoodCorner).
    /// </summary>
    public static partial class Cfg
    {
        [Tune("Auto Wood")] public static int BallWoodMul = 2;

        /// <summary>Is `team`'s wood machine doubled right now (the ball in its machine, the wall down, not sudden death)?</summary>
        public static bool BallDoublesWood(int team)
        {
            var g = NetGame.Instance;
            var b = Ball.Instance;
            return g != null && g.S == GameState.BallLive && b != null && b.IsSpawned && !b.IsCarried && b.SocketTeam.Value == team && team >= 0
                && BallWoodMul > 1 && WoodGenLevel(team) > 0;
        }

        /// <summary>`team`'s wood machine's wood a second right now (doubled with the ball).</summary>
        public static int WoodPerSecondNow(int team) => Mathf.Max(0, WoodGenRate(WoodGenLevel(team))) * (BallDoublesWood(team) ? BallWoodMul : 1);
    }

    public partial class Hud
    {
        /// <summary>(tests) when the "ball in your base: wood machine x2" box was last drawn, and what it said.</summary>
        public static float BallWoodShownAt = -10f;
        public static string BallWoodShown = "";
        public static Rect BallWoodRect;

        /// <summary>Top left: your team's ball is in your machine - your wood machine makes double.</summary>
        void DrawBallWoodCorner(PlayerNet me, float k)
        {
            int team = me.Team.Value;
            if (!Cfg.BallDoublesWood(team)) return;
            string top = $"<b>BALL IN YOUR BASE</b>";
            string sub = $"<b>WOOD MACHINE x{Cfg.BallWoodMul}</b>  <color=#d8d8d8>{Cfg.WoodPerSecondNow(team)} wood a second</color>";
            var st = new GUIStyle(m_Small) { fontSize = Mathf.RoundToInt(m_Small.fontSize * 1.15f), alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false, clipping = TextClipping.Overflow };
            float w = Mathf.Max(st.CalcSize(new GUIContent(top)).x, st.CalcSize(new GUIContent(sub)).x) + 34 * k;
            var r = new Rect(12 * k, Mathf.Max(12 * k, m_RadarBottom + 34 * k), w, 54 * k); // (under the team box, the base radar and the FPS line)
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
            var gold = new Color(1f, 0.82f, 0.25f);
            Fill(r, new Color(0.18f, 0.13f, 0.02f, 0.78f));
            Fill(new Rect(r.x, r.y, 6 * k, r.height), new Color(gold.r, gold.g, gold.b, pulse));
            Shadowed(new Rect(r.x + 16 * k, r.y + 2 * k, r.width, 26 * k), $"<color=#ffd24a>{top}</color>", st);
            Shadowed(new Rect(r.x + 16 * k, r.y + 26 * k, r.width, 26 * k), sub, st);
            BallWoodShownAt = Time.time;
            BallWoodShown = top + " " + sub;
            BallWoodRect = r;
        }
    }
}
