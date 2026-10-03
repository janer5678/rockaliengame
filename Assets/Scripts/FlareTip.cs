using System;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// While the glass wall is up (the build phase, every mode), walking up to the glass wall in the middle of the map
    /// (or the dome over the ball) shows a tip over the ball, in the tutorial's marker style (the gold diamond and a dark
    /// card): EMERGENCY FLARE / Have it in your base when the timer ends to win. (The ball is the emergency flare.) It
    /// fades in and out. Drawn by the HUD (Hud.cs calls Draw).
    /// </summary>
    public static class FlareTip
    {
        public const string Title = "EMERGENCY FLARE", Line = "Have it in your base when the timer ends to win";
        /// <summary>How close to the wall (m) and how far from the middle along it (m) it shows.</summary>
        public const float WallReach = 7f, CentreReach = 45f;
        static float s_Alpha;

        /// <summary>Is the tip showing (tests)? (Fading in or fully in.)</summary>
        public static bool Showing => s_Alpha > 0.01f;
        /// <summary>Should it be showing for a player standing at p (the wall up)?</summary>
        public static bool Wanted(Vector3 p)
        {
            var g = NetGame.Instance;
            if (g == null || g.S != GameState.PreBall || !MapBuilder.GlassUp || Ball.Instance == null) return false;
            float fromMiddle = new Vector2(p.x, p.z).magnitude;
            if (fromMiddle < MapBuilder.DomeRadius + 8f) return true;
            return fromMiddle < CentreReach && WallDistance(p) < WallReach;
        }

        /// <summary>How far (flat) p is from the glass wall line(s) through the middle.</summary>
        public static float WallDistance(Vector3 p)
        {
            var flat = new Vector3(p.x, 0, p.z);
            if (!Cfg.FourWay) return Mathf.Abs(flat.z); // (one wall, along x)
            float best = float.MaxValue;
            for (int k = 0; k < 2; k++)
            {
                var along = Quaternion.Euler(0, 45f + 90f * k, 0) * Vector3.right;
                best = Mathf.Min(best, Vector3.Cross(along, flat).magnitude);
            }
            return best;
        }

        public static void Draw(float k, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            var me = PlayerNet.Local;
            var cam = Camera.main;
            bool want = me != null && !me.Dead.Value && cam != null && Wanted(me.transform.position);
            s_Alpha = Mathf.MoveTowards(s_Alpha, want ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            if (s_Alpha <= 0.01f || cam == null || Ball.Instance == null) return;
            var pc = PlayerController.Local;
            if (pc != null && (pc.Paused || pc.MenuOpen)) return;

            // over the ball, under the dome
            var at = Ball.Instance.transform.position + Vector3.up * 2.4f;
            var sp = cam.WorldToScreenPoint(at);
            if (sp.z < 0f) return;
            float sw = Screen.width, sh = Screen.height;
            var pos = new Vector2(sp.x, sh - sp.y);
            float a = s_Alpha;
            Color A(Color c) { c.a *= a; return c; }

            // the card: the title, big, and one short line under it
            var l2 = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true, fontSize = Mathf.RoundToInt(label.fontSize * 1.25f) };
            var l3 = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            float w = Mathf.Min(sw - 20f, 380f * k);
            float h2 = l2.CalcHeight(new GUIContent(Title), w), h3 = l3.CalcHeight(new GUIContent(Line), w - 24f * k);
            float h = 10f * k + h2 + h3 + 12f * k;
            // (kept on the screen, above the marker)
            float x = Mathf.Clamp(pos.x - w / 2f, 10f, sw - w - 10f);
            float y = Mathf.Clamp(pos.y - h - 18f * k, 10f, sh - h - 120f * k);
            fill(new Rect(x, y, w, h), A(new Color(0.04f, 0.06f, 0.1f, 0.82f)));
            fill(new Rect(x, y, w, 3f * k), A(new Color(1f, 0.82f, 0.29f, 0.9f)));
            string hex(Color c) => ColorUtility.ToHtmlStringRGBA(A(c));
            float ty = y + 8f * k;
            shadowed(new Rect(x, ty, w, h2), $"<b><color=#{hex(Color.white)}>{Title}</color></b>", l2);
            ty += h2;
            shadowed(new Rect(x + 12f * k, ty, w - 24f * k, h3), $"<color=#{hex(new Color(0.85f, 0.88f, 0.92f))}>{Line}</color>", l3);

            // the tutorial's marker: a gold diamond over the ball
            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.15f;
            float sz = 16f * k * pulse;
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, pos);
            fill(new Rect(pos.x - sz / 2, pos.y - sz / 2, sz, sz), A(new Color(1f, 0.82f, 0.29f, 0.95f)));
            fill(new Rect(pos.x - sz / 4, pos.y - sz / 4, sz / 2, sz / 2), A(new Color(0.1f, 0.1f, 0.1f, 0.9f)));
            GUI.matrix = old;
        }
    }
}
