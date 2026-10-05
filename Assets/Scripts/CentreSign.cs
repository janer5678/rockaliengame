using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A signpost in the middle of the map, on the other side of the ball from the crashed UFO (inside the ball's glass
    /// dome): a tall wooden pole with an arrow board near its top for every team, each in the team's colour and pointing
    /// at that team's base (1v1: a blue and a red one; 3 or 4 bases: 3 or 4 arrows, one above the other). It stands off
    /// every line from a base to the ball, so it never blocks the way in or the view of the ball. The pole is solid (a thin
    /// capsule), and so are the arrow boards (their own shape; well above head height, so they're never in the way on
    /// the ground). Built the same on every peer (MapBuilder.Build); none in
    /// Builder (no bases).
    /// </summary>
    public static class CentreSign
    {
        /// <summary>How far from the middle it stands (m: inside the ball's dome, MapBuilder.DomeRadius 11), how tall the
        /// pole is, and the arrows' length / board height. The arrows are big (3 m long, they were 2.1): the sign stands
        /// a little nearer the middle (it was 7.5 m out) and taller (5.4 m), so the highest arrow's point is still under
        /// the half-sphere of glass and the lowest (four teams) still over your head.</summary>
        public const float Dist = 5.8f, PoleH = 6.05f, ArrowLen = 3f, ArrowH = 0.7f;
        /// <summary>How far apart (up the pole) the arrows are, and how far out from the pole's middle each board is nailed
        /// (the pole is 0.1 m round: the board and its paint sit clear in front of it, not in it).</summary>
        public const float ArrowGap = 0.88f, ArrowOut = 0.19f;
        /// <summary>How long an arrow's head is, and how deep the notch in its tail.</summary>
        const float HeadLen = 0.8f, TailNotch = 0.24f;

        public static GameObject Current { get; private set; }
        /// <summary>(tests) Each arrow: its team and which way it points (flat, unit), and its height on the pole.</summary>
        public static readonly List<(int team, Vector3 dir, float y)> Arrows = new List<(int, Vector3, float)>();

        /// <summary>
        /// Which way the sign is from the middle (flat, unit): straight away from the UFO - unless that's (nearly) towards a
        /// base (3 bases: the UFO lies on the side with no base, so straight across is a base's way in), then turned 45
        /// degrees between two bases.
        /// </summary>
        public static Vector3 Dir
        {
            get
            {
                var crash = CrashSite.Dir;
                var away = -crash;
                Vector3 best = away;
                float bestGap = -1f;
                foreach (float turn in new[] { 0f, 45f, -45f, 90f, -90f })
                {
                    var d = Quaternion.Euler(0f, turn, 0f) * away;
                    if (Vector3.Angle(d, crash) < 100f) continue;
                    float gap = 180f;
                    for (int t = 0; t < Cfg.TeamCount; t++)
                    {
                        var b = new Vector3(Cfg.BaseCenter[t].x, 0f, Cfg.BaseCenter[t].z);
                        if (b.sqrMagnitude > 1e-4f) gap = Mathf.Min(gap, Vector3.Angle(d, b));
                    }
                    if (gap >= 40f) return d.normalized;
                    if (gap > bestGap) { bestGap = gap; best = d; }
                }
                return best.normalized;
            }
        }

        static Mesh s_Arrow;

        public static GameObject Build(Transform root)
        {
            Arrows.Clear();
            Current = null;
            if (Cfg.Builder || Cfg.TeamCount < 2) return null; // (no bases to point at)
            var dir = Dir;
            var at = dir * Dist;
            at.y = MapBuilder.Height(at.x, at.z);
            var go = new GameObject("CentreSign");
            go.transform.SetParent(root, false);
            go.transform.position = at;
            var t = go.transform;

            // the pole: a squared-off wooden post, solid (a thin capsule round it)
            var pole = Art.Part(t, Art.Cylinder, Art.DarkWood, new Vector3(0, PoleH * 0.5f - 0.3f, 0), new Vector3(0.2f, PoleH * 0.5f + 0.3f, 0.2f), default, true, null, "pole");
            var cap = pole.GetComponent<CapsuleCollider>();
            if (cap != null) { cap.radius = 0.6f; } // (0.12 m round: a hair outside the post)
            // a little pointed cap on top, and a collar of stones round its foot (low: nothing to trip on)
            Art.Part(t, Art.Cone, Art.DarkWood * 0.8f, new Vector3(0, PoleH - 0.02f, 0), new Vector3(0.3f, 0.25f, 0.3f), default, false, null, "cap");
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f + 0.3f;
                Art.Part(t, Art.MakeRock(31 + i, 0.25f), Color.Lerp(Art.Stone, new Color(0.42f, 0.4f, 0.38f), (i % 3) / 2f),
                    new Vector3(Mathf.Cos(a) * 0.3f, 0.04f, Mathf.Sin(a) * 0.3f), new Vector3(0.24f, 0.14f, 0.22f), new Vector3(0, i * 57f, 0), false, null, "stone");
            }

            // the arrows, one above the other near the top: each team's colour, pointing at its base
            if (s_Arrow == null) s_Arrow = ArrowMesh();
            float y = PoleH - 0.5f;
            for (int team = 0; team < Cfg.TeamCount; team++, y -= ArrowGap) // (next to each other they alternate sides of the post, so their heads pass)
            {
                var to = Cfg.BaseCenter[team] - at;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) continue;
                to.Normalize();
                float yaw = -Mathf.Atan2(to.z, to.x) * Mathf.Rad2Deg; // (the arrow's +x = the way to the base)
                var arm = new GameObject("arrow " + Cfg.TeamLabel(team)).transform;
                arm.SetParent(t, false);
                arm.localPosition = new Vector3(0, y, 0);
                arm.localRotation = Quaternion.Euler(0, yaw, 0);
                // a dark wooden board with the team-colour arrow painted on both faces, nailed to the side of the post
                // (every other one on the other side)
                float side = team % 2 == 0 ? ArrowOut : -ArrowOut;
                var board = Art.Part(arm, s_Arrow, Art.DarkWood, new Vector3(0f, 0f, side), new Vector3(1f, 1f, 0.07f), default, false, null, "board");
                // solid (arrows and spears stick in it, a ball bounces off it, you can stand on it if you get up there):
                // its own shape, convex (so nothing can catch inside the notch in its tail)
                var bc = board.AddComponent<MeshCollider>();
                bc.sharedMesh = s_Arrow;
                bc.convex = true;
                Art.Part(arm, s_Arrow, Cfg.TeamColor[team], new Vector3(0.05f, 0f, side), new Vector3(0.9f, 0.78f, 0.1f), default, false, null, "paint");
                Art.Part(arm, Art.Cylinder, Art.Metal, new Vector3(0f, 0f, side * 0.62f), new Vector3(0.06f, 0.11f, 0.06f), new Vector3(90f, 0, 0), false, null, "nail");
                Arrows.Add((team, to, y));
            }
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (AiPsxArt.On) AiPsxArt.Apply(t);
            Current = go;
            return go;
        }

        /// <summary>An arrow board along +x (unit thickness along z, centred): a shaft from just behind the post (x -0.4)
        /// to the head, which comes to a point at ArrowLen - 0.4, ArrowH tall (the head a little taller). Flat-shaded.</summary>
        static Mesh ArrowMesh()
        {
            float x0 = -0.4f, x2 = ArrowLen - 0.4f, x1 = x2 - HeadLen, h = ArrowH * 0.5f, hh = ArrowH * 0.85f;
            // the outline, round the front face
            var pts = new[] { new Vector2(x0, -h), new Vector2(x1, -h), new Vector2(x1, -hh), new Vector2(x2, 0f), new Vector2(x1, hh), new Vector2(x1, h), new Vector2(x0, h), new Vector2(x0 + TailNotch, 0f) };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 want)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) < 0f) (b, c) = (c, b);
                int k = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(want); n.Add(want); n.Add(want);
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
            }
            foreach (float z in new[] { 0.5f, -0.5f })
            {
                var want = new Vector3(0, 0, z > 0 ? 1f : -1f);
                Vector3 P(int i) => new Vector3(pts[i].x, pts[i].y, z);
                // the shaft (with a notch in its tail), then the head
                Tri(P(0), P(1), P(7), want); Tri(P(1), P(5), P(7), want); Tri(P(5), P(6), P(7), want);
                Tri(P(2), P(3), P(4), want);
            }
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                var side = new Vector3(b.y - a.y, -(b.x - a.x), 0f).normalized;
                // (outward: away from the shaft's middle line)
                var mid = new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, 0f);
                if (Vector3.Dot(side, mid - new Vector3(Mathf.Clamp(mid.x, x0 + TailNotch + 0.03f, x1), 0f, 0f)) < 0f) side = -side;
                Vector3 a0 = new Vector3(a.x, a.y, 0.5f), a1 = new Vector3(a.x, a.y, -0.5f), b0 = new Vector3(b.x, b.y, 0.5f), b1 = new Vector3(b.x, b.y, -0.5f);
                Tri(a0, b0, b1, side);
                Tri(a0, b1, a1, side);
            }
            var m = new Mesh { name = "sign arrow" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
