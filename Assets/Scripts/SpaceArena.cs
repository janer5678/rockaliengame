using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// The sudden death arena (also the waiting lobby), Super Smash Bros "Final Destination" style, in the game's own
    /// low-poly look (flat colours, no textures): a flat octagonal platform with pink edges and a lightning bolt inlay,
    /// a blue mechanical underside with orange panel lines narrowing to a point, floating in space - one flat dark colour
    /// all round, with low-poly white 3D stars. No walls: walk off the edge and you fall into space (in sudden death
    /// that's a death like any other; in the lobby you're just put back on the platform). Four floating screens show the
    /// countdown and the clock. Built identically on every peer (non-networked) by MapBuilder.
    /// </summary>
    public static class SpaceArena
    {
        /// <summary>Half sizes of the octagon (x across, z along the duel) and how much its corners are cut off.</summary>
        public const float HalfX = 17.85f, HalfZ = 22.95f, Chamfer = 6.8f;
        /// <summary>Fall this far below the top of the platform in sudden death and you're lost in space.</summary>
        public const float KillDepth = 10f;
        /// <summary>The space shell around the arena (it hides the far-away map).</summary>
        public const float ShellRadius = 360f;
        /// <summary>How many stars hang in the sky (one merged mesh).</summary>
        public const int StarCount = 220;
        /// <summary>The colour of space.</summary>
        public static readonly Color SkyColor = new Color(0.075f, 0.05f, 0.17f);

        static Shader s_Unlit;

        /// <summary>The octagon outline (x, z), going round.</summary>
        public static readonly Vector2[] Outline =
        {
            new Vector2(HalfX - Chamfer, HalfZ), new Vector2(HalfX, HalfZ - Chamfer), new Vector2(HalfX, -HalfZ + Chamfer), new Vector2(HalfX - Chamfer, -HalfZ),
            new Vector2(-HalfX + Chamfer, -HalfZ), new Vector2(-HalfX, -HalfZ + Chamfer), new Vector2(-HalfX, HalfZ - Chamfer), new Vector2(-HalfX + Chamfer, HalfZ),
        };

        /// <summary>Is this point over the platform (in its footprint)?</summary>
        public static bool OverPlatform(Vector3 world)
        {
            var p = world - Cfg.ArenaCenter;
            float x = Mathf.Abs(p.x), z = Mathf.Abs(p.z);
            return x <= HalfX && z <= HalfZ && (x - (HalfX - Chamfer)) / Chamfer + (z - (HalfZ - Chamfer)) / Chamfer <= 1f;
        }

        /// <summary>Somewhere around the arena (as opposed to the map, 1 km away).</summary>
        public static bool NearArena(Vector3 world) => Mathf.Abs(world.z - Cfg.ArenaCenter.z) < 400f && Mathf.Abs(world.x - Cfg.ArenaCenter.x) < 400f;

        /// <summary>Server, every frame: in sudden death, anyone who fell off the platform is lost in space.</summary>
        public static void ServerTick(NetGame g)
        {
            if (g == null || g.S != GameState.SuddenDeath) return;
            float killY = Cfg.ArenaCenter.y - KillDepth;
            for (int i = PlayerNet.All.Count - 1; i >= 0; i--)
            {
                var p = PlayerNet.All[i];
                if (p == null || p.Dead.Value) continue;
                var pos = p.transform.position;
                if (pos.y < killY && NearArena(pos)) p.ServerFellIntoSpace();
            }
        }

        // =====================================================================
        // building it
        // =====================================================================

        /// <summary>A plain unlit colour (space and the stars: they aren't lit by anything). cull: which faces to skip.</summary>
        static Material UnlitMat(Color c, CullMode cull)
        {
            if (s_Unlit == null) s_Unlit = Resources.Load<Shader>("SpaceArena/SpaceGlow");
            if (s_Unlit == null || !s_Unlit.isSupported) return null;
            var m = new Material(s_Unlit) { name = "space unlit" };
            m.SetColor("_Color", c);
            m.SetFloat("_Cull", (float)cull);
            return m;
        }

        public static void Build(Transform root)
        {
            var c = Cfg.ArenaCenter;
            var go = new GameObject("Stadium");
            go.transform.SetParent(root, false);
            go.transform.position = c;
            var t = go.transform;
            var stadium = go.AddComponent<Stadium>();

            // flat colours, like the rest of the game
            var deckMat = Art.Mat(new Color(0.34f, 0.33f, 0.42f));
            var rimMat = Art.Mat(new Color(0.11f, 0.1f, 0.17f));
            var hullMat = Art.Mat(new Color(0.2f, 0.36f, 0.8f));
            var pinkMat = Art.Mat(new Color(1f, 0.32f, 0.9f));
            var orangeMat = Art.Mat(new Color(1f, 0.56f, 0.15f));
            var whiteMat = Art.Mat(new Color(0.92f, 0.92f, 0.98f));
            var inlayMat = Art.Mat(new Color(0.42f, 0.26f, 0.7f));

            // ---------- the top: a flat octagon (the only thing you can stand on) ----------
            var deck = new MeshBatch();
            deck.Frustum(Outline, 0f, 1f, -1f, 1f, true, true, 6f);
            var deckGo = deck.Build(t, "ArenaFloor", deckMat, true);
            // the rim faces: a second, slightly bigger dark band around the top's sides
            var rim = new MeshBatch();
            rim.Frustum(Scale(Outline, 1.004f), -0.02f, 1f, -1.02f, 1f, false, false, 4f);
            rim.Build(t, "rim", rimMat, true);
            var mc = deckGo.AddComponent<MeshCollider>();
            mc.sharedMesh = deckGo.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
            deckGo.AddComponent<GroundMarker>();

            // ---------- the underside: tiers of blue machinery, narrowing to a point ----------
            var hull = new MeshBatch();
            var lines = new MeshBatch();
            var tiers = new (float y0, float s0, float y1, float s1, int dividers)[]
            {
                (-1.0f, 0.975f, -2.1f, 0.87f, 1),
                (-2.1f, 0.84f, -4.5f, 0.62f, 2),
                (-4.5f, 0.6f, -7f, 0.38f, 1),
                (-7f, 0.36f, -9.2f, 0.15f, 0),
            };
            foreach (var tr in tiers)
            {
                hull.Frustum(Outline, tr.y0, tr.s0, tr.y1, tr.s1, true, true, 4f);
                for (int i = 0; i < Outline.Length; i++)
                {
                    var a = Outline[i];
                    var b = Outline[(i + 1) % Outline.Length];
                    Vector3 A0 = V(a * tr.s0, tr.y0), B0 = V(b * tr.s0, tr.y0), A1 = V(a * tr.s1, tr.y1), B1 = V(b * tr.s1, tr.y1);
                    var n = Vector3.Cross(B0 - A0, A1 - A0).normalized;
                    if (Vector3.Dot(n, V((a + b) * 0.5f, 0f)) < 0) n = -n;
                    var o = n * 0.03f;
                    float inset = 0.25f;
                    // panel outlines inset a little from the face edges, with dividers between the panels
                    Vector3 L(float u, float v) => Vector3.Lerp(Vector3.Lerp(A0, B0, u), Vector3.Lerp(A1, B1, u), v) + o;
                    float du = inset / Mathf.Max(1f, Vector3.Distance(A0, B0)), dv = Mathf.Min(0.2f, inset / Mathf.Max(0.5f, Vector3.Distance(A0, A1)));
                    int panels = tr.dividers + 1;
                    for (int k = 0; k < panels; k++)
                    {
                        float u0 = (float)k / panels + du, u1 = (float)(k + 1) / panels - du;
                        lines.Line(L(u0, dv), L(u1, dv), 0.09f, 0.05f, n);
                        lines.Line(L(u0, 1 - dv), L(u1, 1 - dv), 0.09f, 0.05f, n);
                        lines.Line(L(u0, dv), L(u0, 1 - dv), 0.09f, 0.05f, n);
                        lines.Line(L(u1, dv), L(u1, 1 - dv), 0.09f, 0.05f, n);
                    }
                }
            }
            // hanging fins round the underside (the "claws" under Final Destination)
            for (int i = 0; i < Outline.Length; i++)
            {
                var a = Outline[i];
                var b = Outline[(i + 1) % Outline.Length];
                for (int k = 0; k < 2; k++)
                {
                    var p2 = Vector2.Lerp(a, b, k == 0 ? 0.28f : 0.72f) * 0.74f;
                    var outDir = V(p2, 0f).normalized;
                    var rot = Quaternion.LookRotation(outDir) * Quaternion.Euler(-18f, 0, 0); // bottom kicks outwards
                    var centre = V(p2, -4.8f);
                    var size = new Vector3(Mathf.Min(2.7f, Vector3.Distance(V(a, 0), V(b, 0)) * 0.22f), 5.5f, 0.95f);
                    hull.Box(centre, rot, size);
                    // orange outline on the outward face
                    var f = rot * Vector3.forward * (size.z * 0.5f + 0.03f);
                    var r = rot * Vector3.right * (size.x * 0.5f - 0.18f);
                    var u = rot * Vector3.up * (size.y * 0.5f - 0.18f);
                    var fn = rot * Vector3.forward;
                    var c0 = centre + f - r - u; var c1 = centre + f + r - u; var c2 = centre + f + r + u; var c3 = centre + f - r + u;
                    lines.Line(c0, c1, 0.09f, 0.05f, fn); lines.Line(c1, c2, 0.09f, 0.05f, fn);
                    lines.Line(c2, c3, 0.09f, 0.05f, fn); lines.Line(c3, c0, 0.09f, 0.05f, fn);
                    lines.Line(centre + f - u * 0.2f - r, centre + f - u * 0.2f + r, 0.09f, 0.05f, fn);
                }
            }
            hull.Build(t, "underside", hullMat, true);
            lines.Build(t, "panel lines", orangeMat, false);
            // the point at the bottom: a pale blue crystal
            var tip = new MeshBatch();
            tip.Frustum(Outline, -9.2f, 0.15f, -12.5f, 0.015f, false, false, 4f);
            tip.Build(t, "core", Art.Mat(new Color(0.6f, 0.75f, 1f)), false);

            // ---------- pink edges, white corner marks and the lightning bolt inlay ----------
            var edges = new MeshBatch();
            var outline1 = Scale(Outline, 1f - 0.18f / HalfX);
            var outline2 = Scale(Outline, 1f - 1.1f / HalfX);
            for (int i = 0; i < Outline.Length; i++)
            {
                int j = (i + 1) % Outline.Length;
                edges.Line(V(outline1[i], 0.012f), V(outline1[j], 0.012f), 0.22f, 0.03f, Vector3.up);
                edges.Line(V(outline2[i], 0.012f), V(outline2[j], 0.012f), 0.12f, 0.03f, Vector3.up);
                // two lines round the rim
                var n = V(Outline[i] + Outline[j], 0f).normalized;
                var e0 = V(Outline[i] * 1.004f, 0f) + n * 0.03f;
                var e1 = V(Outline[j] * 1.004f, 0f) + n * 0.03f;
                edges.Line(e0 + Vector3.down * 0.18f, e1 + Vector3.down * 0.18f, 0.12f, 0.04f, n);
                edges.Line(e0 + Vector3.down * 0.85f, e1 + Vector3.down * 0.85f, 0.12f, 0.04f, n);
            }
            var bolt = BoltOutline();
            for (int i = 0; i < bolt.Length; i++)
                edges.Line(V(bolt[i], 0.016f), V(bolt[(i + 1) % bolt.Length], 0.016f), 0.14f, 0.03f, Vector3.up);
            edges.Build(t, "pink edges", pinkMat, false);
            var marks = new MeshBatch();
            var inner = Scale(Outline, 1f - 2.2f / HalfX);
            for (int i = 0; i < Outline.Length; i++)
            {
                var p = V(inner[i], 0.012f);
                var along = V(inner[(i + 1) % Outline.Length] - inner[i], 0f).normalized;
                marks.Box(p + along * 0.9f, Quaternion.LookRotation(along), new Vector3(0.3f, 0.03f, 1.4f));
            }
            marks.Build(t, "corner marks", whiteMat, false);
            var inlay = new MeshBatch();
            inlay.Polygon(bolt, 0.008f, 3f);
            inlay.Build(t, "bolt inlay", inlayMat, false);

            // ---------- lights: a cool glow up onto the machinery, a soft pink wash over the top ----------
            var lc = new GameObject("core light");
            lc.transform.SetParent(t, false);
            lc.transform.localPosition = new Vector3(0, -13f, 0);
            var l = lc.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 28f;
            l.intensity = 4f;
            l.color = new Color(0.55f, 0.7f, 1f);
            var lp = new GameObject("pink light");
            lp.transform.SetParent(t, false);
            lp.transform.localPosition = new Vector3(0, 14f, 0);
            var l2 = lp.AddComponent<Light>();
            l2.type = LightType.Point;
            l2.range = 38f;
            l2.intensity = 0.8f;
            l2.color = new Color(0.9f, 0.6f, 1f);

            // ---------- space all around: one flat colour, with low-poly white stars ----------
            var skyMat = UnlitMat(SkyColor, CullMode.Front);
            var shell = Art.Part(t, Art.Sphere, Color.white, Vector3.zero, Vector3.one * ShellRadius * 2f, default, false, skyMat != null ? skyMat : Art.Mat(SkyColor), "space");
            var sr = shell.GetComponent<MeshRenderer>();
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            if (skyMat == null) shell.GetComponent<MeshFilter>().sharedMesh = Inverted(Art.Sphere);
            stadium.SetShell(sr);
            var stars = BuildStars();
            var starMat = UnlitMat(Color.white, CullMode.Back);
            var starGo = stars.Build(t, "stars", starMat != null ? starMat : Art.Mat(Color.white), false);
            starGo.GetComponent<MeshRenderer>().receiveShadows = false;

            // ---------- floating screens with the countdown and the clock ----------
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + i * 90f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var screen = new GameObject("jumbotron").transform;
                screen.SetParent(t, false);
                screen.localPosition = dir * 44f + Vector3.up * 16f;
                screen.localRotation = Quaternion.LookRotation(dir); // faces outward, so its back (-z) faces the platform
                Art.Box(screen, new Color(0.05f, 0.04f, 0.1f), Vector3.zero, new Vector3(10f, 5.5f, 0.3f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                var frame = new MeshBatch();
                Vector3 s0 = new Vector3(-5.1f, -2.85f, -0.2f), s1 = new Vector3(5.1f, -2.85f, -0.2f), s2 = new Vector3(5.1f, 2.85f, -0.2f), s3 = new Vector3(-5.1f, 2.85f, -0.2f);
                frame.Line(s0, s1, 0.16f, 0.1f, Vector3.back); frame.Line(s1, s2, 0.16f, 0.1f, Vector3.back);
                frame.Line(s2, s3, 0.16f, 0.1f, Vector3.back); frame.Line(s3, s0, 0.16f, 0.1f, Vector3.back);
                frame.Build(screen, "frame", pinkMat, false);
                var txt = new GameObject("text");
                txt.transform.SetParent(screen, false);
                txt.transform.localPosition = new Vector3(0, 0, -0.3f);
                txt.transform.localRotation = Quaternion.identity; // readable from the platform
                var tm = txt.AddComponent<TextMesh>();
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.22f;
                tm.fontSize = 64;
                tm.color = new Color(1f, 0.75f, 1f);
                tm.text = "ROCK BRAWL";
                stadium.AddScreen(tm);
            }
        }

        /// <summary>
        /// The stars: chunky low-poly five-pointed stars (a flat star outline pulled out to a point front and back),
        /// scattered all round inside the space shell, each turned to face the platform. One merged mesh, one draw.
        /// </summary>
        static MeshBatch BuildStars()
        {
            var rng = new System.Random(9157);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mb = new MeshBatch();
            var pts = new Vector3[10];
            for (int s = 0; s < StarCount; s++)
            {
                // a direction on the sphere, not straight down under the platform
                Vector3 dir;
                do dir = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)); while (dir.sqrMagnitude > 1f || dir.sqrMagnitude < 0.05f || dir.normalized.y < -0.85f);
                dir.Normalize();
                float dist = R(170f, ShellRadius - 30f);
                var c = dir * dist;
                var n = -dir; // faces the platform
                var u = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var v = Vector3.Cross(n, u);
                float spin = R(0f, Mathf.PI * 2f);
                // most are small, a few are big
                float big = R(0f, 1f);
                float r0 = (big > 0.9f ? R(5f, 8f) : big > 0.6f ? R(3f, 5f) : R(1.6f, 3f)) * dist / 250f;
                float r1 = r0 * 0.42f, depth = r0 * 0.38f;
                for (int i = 0; i < 10; i++)
                {
                    float a = spin + i * Mathf.PI / 5f;
                    float rr = i % 2 == 0 ? r0 : r1;
                    pts[i] = c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * rr;
                }
                var front = c + n * depth;
                var back = c - n * depth;
                for (int i = 0; i < 10; i++)
                {
                    var p0 = pts[i];
                    var p1 = pts[(i + 1) % 10];
                    var mid = (p0 + p1) * 0.5f - c;
                    mb.Tri(front, p0, p1, mid + n * depth, 4f);
                    mb.Tri(back, p0, p1, mid - n * depth, 4f);
                }
            }
            return mb;
        }

        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static Vector2[] Scale(Vector2[] poly, float s)
        {
            var r = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++) r[i] = poly[i] * s;
            return r;
        }

        /// <summary>The lightning bolt across the middle of the platform (x, z), going round.</summary>
        static Vector2[] BoltOutline() => Scale(new[]
        {
            new Vector2(1.2f, -20f), new Vector2(3.6f, 1.6f), new Vector2(0.9f, 0.6f),
            new Vector2(-1.2f, 20f), new Vector2(-3.6f, -1.6f), new Vector2(-0.9f, -0.6f),
        }, HalfZ / 27f);

        static Mesh Inverted(Mesh src)
        {
            var m = Object.Instantiate(src);
            var tris = m.triangles;
            for (int i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            m.triangles = tris;
            return m;
        }
    }

    /// <summary>Builds one flat-shaded mesh out of many pieces (boxes, line strips, prisms) so a whole part is one draw.</summary>
    public class MeshBatch
    {
        readonly List<Vector3> m_V = new List<Vector3>();
        readonly List<Vector3> m_N = new List<Vector3>();
        readonly List<Vector2> m_U = new List<Vector2>();
        readonly List<int> m_T = new List<int>();

        /// <summary>A triangle facing `outward`; UVs are projected onto its plane, `tile` metres per repeat.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, float tile = 4f)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0) { (b, c) = (c, b); n = -n; }
            n.Normalize();
            Vector3 e1, e2;
            if (Mathf.Abs(n.y) > 0.7f) { e1 = Vector3.right; e2 = Vector3.forward; }
            else { e1 = Vector3.Cross(Vector3.up, n).normalized; e2 = Vector3.Cross(n, e1); }
            int i = m_V.Count;
            foreach (var p in new[] { a, b, c })
            {
                m_V.Add(p);
                m_N.Add(n);
                m_U.Add(new Vector2(Vector3.Dot(p, e1), Vector3.Dot(p, e2)) / tile);
            }
            m_T.Add(i); m_T.Add(i + 1); m_T.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float tile = 4f)
        {
            Tri(a, b, c, outward, tile);
            Tri(a, c, d, outward, tile);
        }

        public void Box(Vector3 centre, Quaternion rot, Vector3 size)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 X = rot * Vector3.right, Y = rot * Vector3.up, Z = rot * Vector3.forward;
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), X);
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), -X);
            Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Y);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -Y);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Z);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -Z);
        }

        /// <summary>A flat strip from a to b: `width` across, `height` along `up` (the side it shows to).</summary>
        public void Line(Vector3 a, Vector3 b, float width, float height, Vector3 up)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var side = Vector3.Cross(up, d).normalized;
            var realUp = Vector3.Cross(d, side).normalized;
            Box((a + b) * 0.5f, Quaternion.LookRotation(d, realUp), new Vector3(width, height, len + width));
        }

        /// <summary>A convex polygon (x, z) ring at y0 scaled s0 joined to the same ring at y1 scaled s1, with optional caps.</summary>
        public void Frustum(Vector2[] poly, float y0, float s0, float y1, float s1, bool capTop, bool capBottom, float tile)
        {
            int n = poly.Length;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                var A0 = new Vector3(a.x * s0, y0, a.y * s0); var B0 = new Vector3(b.x * s0, y0, b.y * s0);
                var A1 = new Vector3(a.x * s1, y1, a.y * s1); var B1 = new Vector3(b.x * s1, y1, b.y * s1);
                var mid = (a + b) * 0.5f;
                Quad(A0, B0, B1, A1, new Vector3(mid.x, 0, mid.y), tile);
            }
            var top = y0 > y1 ? (y0, s0) : (y1, s1);
            var bot = y0 > y1 ? (y1, s1) : (y0, s0);
            if (capTop)
                for (int i = 1; i < n - 1; i++)
                    Tri(new Vector3(poly[0].x * top.Item2, top.Item1, poly[0].y * top.Item2), new Vector3(poly[i].x * top.Item2, top.Item1, poly[i].y * top.Item2),
                        new Vector3(poly[i + 1].x * top.Item2, top.Item1, poly[i + 1].y * top.Item2), Vector3.up, tile);
            if (capBottom)
                for (int i = 1; i < n - 1; i++)
                    Tri(new Vector3(poly[0].x * bot.Item2, bot.Item1, poly[0].y * bot.Item2), new Vector3(poly[i].x * bot.Item2, bot.Item1, poly[i].y * bot.Item2),
                        new Vector3(poly[i + 1].x * bot.Item2, bot.Item1, poly[i + 1].y * bot.Item2), Vector3.down, tile);
        }

        /// <summary>A flat, upward-facing polygon (x, z) at height y (simple, possibly concave: ear clipping).</summary>
        public void Polygon(Vector2[] poly, float y, float tile)
        {
            var idx = new List<int>();
            for (int i = 0; i < poly.Length; i++) idx.Add(i);
            float area = 0;
            for (int i = 0; i < poly.Length; i++) { var p = poly[i]; var q = poly[(i + 1) % poly.Length]; area += p.x * q.y - q.x * p.y; }
            float sign = Mathf.Sign(area);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 200)
            {
                for (int k = 0; k < idx.Count; k++)
                {
                    var a = poly[idx[(k + idx.Count - 1) % idx.Count]];
                    var b = poly[idx[k]];
                    var c = poly[idx[(k + 1) % idx.Count]];
                    float cr = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cr * sign <= 0) continue; // reflex corner
                    bool inside = false;
                    for (int m = 0; m < idx.Count && !inside; m++)
                    {
                        var p = poly[idx[m]];
                        if (p == a || p == b || p == c) continue;
                        inside = InTri(p, a, b, c);
                    }
                    if (inside) continue;
                    Tri(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), new Vector3(c.x, y, c.y), Vector3.up, tile);
                    idx.RemoveAt(k);
                    break;
                }
            }
            if (idx.Count == 3)
                Tri(new Vector3(poly[idx[0]].x, y, poly[idx[0]].y), new Vector3(poly[idx[1]].x, y, poly[idx[1]].y), new Vector3(poly[idx[2]].x, y, poly[idx[2]].y), Vector3.up, tile);
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        public GameObject Build(Transform parent, string name, Material mat, bool shadows)
        {
            var mesh = new Mesh { name = name, indexFormat = m_V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(m_V);
            mesh.SetNormals(m_N);
            mesh.SetUVs(0, m_U);
            mesh.SetTriangles(m_T, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }


    /// <summary>
    /// The sudden death arena at runtime: the screens show the countdown and the clock. Everything is only drawn while
    /// you're there, and while you are, it's space: no fog, a purple ambient light.
    /// </summary>
    public class Stadium : MonoBehaviour
    {
        public static Stadium Instance;

        readonly List<TextMesh> m_Screens = new List<TextMesh>();
        Renderer[] m_Renderers;
        Light[] m_Lights;
        bool m_Shown = true;
        Renderer m_Shell;

        // what the space look changed, put back when you leave
        bool m_EnvOn, m_SavedFog;
        Color m_SavedSky, m_SavedEquator, m_SavedGround;
        AmbientMode m_SavedMode;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            SpaceLook(false);
            if (Instance == this) Instance = null;
        }

        public void SetShell(Renderer r) => m_Shell = r;
        public void AddScreen(TextMesh tm) => m_Screens.Add(tm);

        /// <summary>The arena is far off the edge of the map: only draw it when you're near it (and then it's all you see).</summary>
        void ShowStadium(bool show)
        {
            if (m_Renderers == null) m_Renderers = GetComponentsInChildren<Renderer>(true);
            if (m_Lights == null) m_Lights = GetComponentsInChildren<Light>(true);
            if (show == m_Shown) return;
            m_Shown = show;
            foreach (var r in m_Renderers) if (r) r.enabled = show;
            foreach (var l in m_Lights) if (l) l.enabled = show;
        }

        /// <summary>Space has no fog, and a cool purple ambient (it lights the underside).</summary>
        void SpaceLook(bool on)
        {
            if (on == m_EnvOn) return;
            m_EnvOn = on;
            if (on)
            {
                m_SavedFog = RenderSettings.fog;
                m_SavedMode = RenderSettings.ambientMode;
                m_SavedSky = RenderSettings.ambientSkyColor;
                m_SavedEquator = RenderSettings.ambientEquatorColor;
                m_SavedGround = RenderSettings.ambientGroundColor;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.5f, 0.45f, 0.75f);
                RenderSettings.ambientEquatorColor = new Color(0.4f, 0.34f, 0.6f);
                RenderSettings.ambientGroundColor = new Color(0.3f, 0.34f, 0.65f);
            }
            else
            {
                RenderSettings.fog = m_SavedFog;
                RenderSettings.ambientMode = m_SavedMode;
                RenderSettings.ambientSkyColor = m_SavedSky;
                RenderSettings.ambientEquatorColor = m_SavedEquator;
                RenderSettings.ambientGroundColor = m_SavedGround;
            }
        }

        void Update()
        {
            var g = NetGame.Instance;
            var cam = Camera.main;
            float camDist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : float.MaxValue;
            bool shown = camDist < 300f;
            ShowStadium(shown);
            SpaceLook(shown);
            if (!shown) return;
            string text = "ROCK BRAWL";
            if (g != null && g.IsSpawned)
            {
                if (g.S == GameState.SuddenDeath)
                {
                    string word = Hud.FightWord(g, out _);
                    if (word != "") text = word;
                    else { int s = Mathf.CeilToInt(g.TimeLeft); text = $"SUDDEN DEATH\n{s / 60}:{s % 60:00}"; }
                }
                else if (g.S == GameState.Waiting) text = "WAITING FOR\nPLAYERS";
                else if (g.S == GameState.GameOver) text = g.Winner.Value >= 0 ? $"{Cfg.TeamLabel(g.Winner.Value)}\nWINS!" : "DRAW";
            }
            foreach (var s in m_Screens) if (s.text != text) s.text = text;
        }
    }

    public partial class PlayerNet
    {
        /// <summary>Server: walked (or got knocked) off the sudden death platform. A death like any other (god mode: back on top).</summary>
        public void ServerFellIntoSpace()
        {
            if (Dead.Value) return;
            if (m_God)
            {
                NetGame.SpawnPoint(Team.Value, true, Slot.Value, out var pos, out var yaw);
                TeleportRpc(pos, yaw);
                return;
            }
            if (NetGame.Instance != null) NetGame.Instance.Broadcast($"{Cfg.TeamLabel(Team.Value)} fell into space!");
            Notify("You fell into space!");
            ArmorHp.Value = 0;
            ServerDie(null);
        }
    }
}
