using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// The sudden death arena (also the waiting lobby), Super Smash Bros "Final Destination" style, in the game's own
    /// low-poly look (flat colours, no textures): a flat octagonal platform with pink edges, a blue mechanical underside
    /// with orange panel lines narrowing to a point, floating in space - a black sky full of low-poly 3D stars, a galaxy
    /// band, spiral galaxies and faceted planets (some ringed, some with moons), slowly turning. All round it a standard
    /// stadium - one continuous bowl of stands (a padded front wall, rising rows of steps and benches, a back wall with
    /// floodlights and big screens), with a gap all round between it and the platform - packed with a crowd of aliens like
    /// you in every colour, each doing its own thing (jumping, fist pumping, waving, clapping, swaying, dancing, spinning,
    /// sitting and getting up to cheer...), with a Mexican wave going round now and then and the whole crowd going wild on
    /// ROCK! and every death. Struts far below join the platform to the stadium. No walls: walk off the edge and you fall
    /// into space (in sudden death that's a death like any other; in the lobby you're just put back on the platform).
    /// Four screens on the back wall show the countdown and the clock. Built identically on every peer (non-networked)
    /// by MapBuilder.
    /// Kept cheap: the whole sky is three meshes; the crowd is one small alien mesh drawn once per section of the stadium
    /// in view (procedural instancing) and animated in its vertex shader (SpaceArena/Crowd.shader); the stadium is one mesh.
    /// </summary>
    public static class SpaceArena
    {
        /// <summary>Half sizes of the octagon (x across, z along the duel) and how much its corners are cut off.</summary>
        public const float HalfX = 17.85f, HalfZ = 22.95f, Chamfer = 6.8f;
        /// <summary>Fall this far below the top of the platform in sudden death and you're lost in space.</summary>
        public const float KillDepth = 10f;
        /// <summary>The space shell around the arena (it hides the far-away map).</summary>
        public const float ShellRadius = 360f;
        /// <summary>How many low-poly 3D stars hang in the sky (one merged mesh), and the galaxy band's specks.</summary>
        public const int StarCount = 1500, GalaxySpecks = 5200;
        /// <summary>How many planets (plus their moons and rings) are dotted round the sky.</summary>
        public const int PlanetCount = 18;
        /// <summary>The colour of space: black.</summary>
        public static readonly Color SkyColor = Color.black;
        /// <summary>The crowd's animations (see Crowd.shader): how many there are.</summary>
        public const int CrowdAnimations = 13;
        /// <summary>Which way the galaxy band's bright middle is, in the sky's own space (it turns: Stadium.Sky).</summary>
        public static Vector3 GalaxyCore { get; private set; } = Vector3.up;

        static Shader s_Unlit, s_Sky;

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

            // ---------- pink edges and white corner marks ----------
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

            // ---------- space all around: black, full of stars, a galaxy and planets ----------
            var skyMat = UnlitMat(SkyColor, CullMode.Front);
            var shell = Art.Part(t, Art.Sphere, Color.white, Vector3.zero, Vector3.one * ShellRadius * 2f, default, false, skyMat != null ? skyMat : Art.Mat(SkyColor), "space");
            var sr = shell.GetComponent<MeshRenderer>();
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            if (skyMat == null) shell.GetComponent<MeshFilter>().sharedMesh = Inverted(Art.Sphere);
            stadium.SetShell(sr);
            BuildSky(t, stadium);

            // ---------- the stadium all round, packed with the crowd, and the struts holding the platform in it ----------
            BuildCrowd(t, stadium);
            BuildStruts(t, hullMat);

            // ---------- big screens on posts on the stadium's back wall, with the countdown and the clock (readable from both sides) ----------
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + i * 90f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var rp = RingPoint(a, Gap + BowlDepth + 0.2f);
                var screen = new GameObject("jumbotron").transform;
                screen.SetParent(t, false);
                screen.localPosition = new Vector3(rp.x, WallTop + 7f, rp.y);
                screen.localRotation = Quaternion.LookRotation(dir); // faces outward, so its back (-z) faces the platform
                Art.Box(screen, new Color(0.05f, 0.04f, 0.1f), Vector3.zero, new Vector3(10f, 5.5f, 0.3f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                Art.Box(screen, new Color(0.11f, 0.1f, 0.17f), new Vector3(0f, -5.6f, 0.2f), new Vector3(0.6f, 5.6f, 0.6f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                var frame = new MeshBatch();
                for (int side = 0; side < 2; side++)
                {
                    float z = side == 0 ? -0.2f : 0.2f;
                    var up = side == 0 ? Vector3.back : Vector3.forward;
                    Vector3 s0 = new Vector3(-5.1f, -2.85f, z), s1 = new Vector3(5.1f, -2.85f, z), s2 = new Vector3(5.1f, 2.85f, z), s3 = new Vector3(-5.1f, 2.85f, z);
                    frame.Line(s0, s1, 0.16f, 0.1f, up); frame.Line(s1, s2, 0.16f, 0.1f, up);
                    frame.Line(s2, s3, 0.16f, 0.1f, up); frame.Line(s3, s0, 0.16f, 0.1f, up);
                    var txt = new GameObject("text");
                    txt.transform.SetParent(screen, false);
                    txt.transform.localPosition = new Vector3(0, 0, side == 0 ? -0.3f : 0.3f);
                    txt.transform.localRotation = side == 0 ? Quaternion.identity : Quaternion.Euler(0, 180f, 0); // readable from the platform / from outside
                    var tm = txt.AddComponent<TextMesh>();
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    tm.characterSize = 0.22f;
                    tm.fontSize = 64;
                    tm.color = new Color(1f, 0.75f, 1f);
                    tm.text = "ROCK BRAWL";
                    stadium.AddScreen(tm);
                }
                frame.Build(screen, "frame", pinkMat, false);
            }
        }

        // =====================================================================
        // the sky: stars, the galaxy, planets
        // =====================================================================

        /// <summary>The sky's unlit vertex-colour material; glow: added on top (no depth writes, both sides).</summary>
        static Material SkyMat(bool glow)
        {
            if (s_Sky == null) s_Sky = Resources.Load<Shader>("SpaceArena/SpaceSky");
            if (s_Sky == null || !s_Sky.isSupported) return glow ? null : Art.Mat(Color.white);
            var m = new Material(s_Sky) { name = glow ? "space glow" : "space sky" };
            if (glow)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }

        static float Sq(float x) => x * x;
        static float AngleDiff(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg)) * Mathf.Deg2Rad;

        /// <summary>A unit icosphere, subdivided `sub` times (each triangle on its own: faceted).</summary>
        static List<(Vector3 a, Vector3 b, Vector3 c)> Ico(int sub)
        {
            float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new[]
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var tris = new List<(Vector3, Vector3, Vector3)>();
            for (int i = 0; i < f.Length; i += 3) tris.Add((v[f[i]].normalized, v[f[i + 1]].normalized, v[f[i + 2]].normalized));
            for (int s = 0; s < sub; s++)
            {
                var next = new List<(Vector3, Vector3, Vector3)>();
                foreach (var (a, b, c) in tris)
                {
                    var ab = (a + b).normalized; var bc = (b + c).normalized; var ca = (c + a).normalized;
                    next.Add((a, ab, ca)); next.Add((ab, b, bc)); next.Add((ca, bc, c)); next.Add((ab, bc, ca));
                }
                tris = next;
            }
            return tris;
        }

        /// <summary>A speck of the galaxy: a small flat four-pointed diamond facing `n`.</summary>
        static void Speck(MeshBatch mb, Vector3 c, Vector3 n, float s, float spin, Color col, Vector4 twinkle)
        {
            var u = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(n, u);
            var a = u * Mathf.Cos(spin) + v * Mathf.Sin(spin);
            var b = -u * Mathf.Sin(spin) + v * Mathf.Cos(spin);
            mb.Tint = col;
            mb.Extra = twinkle;
            Vector3 p0 = c + a * s, p1 = c + b * (s * 0.6f), p2 = c - a * s, p3 = c - b * (s * 0.6f);
            mb.Tri(p0, p1, p2, n);
            mb.Tri(p0, p2, p3, n);
        }

        /// <summary>A soft glow (added on top): a disc of rings in the plane (u, v), colours from the middle out (the last one black).</summary>
        static void GlowDisc(MeshBatch glow, Vector3 c, Vector3 u, Vector3 v, float[] radii, Color[] cols, int segs)
        {
            for (int s = 0; s < segs; s++)
            {
                float a0 = s * Mathf.PI * 2f / segs, a1 = (s + 1) * Mathf.PI * 2f / segs;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                glow.TriC(c, c + d0 * radii[0], c + d1 * radii[0], cols[0], cols[1], cols[1]);
                for (int k = 1; k < radii.Length; k++)
                {
                    Vector3 i0 = c + d0 * radii[k - 1], i1 = c + d1 * radii[k - 1], o0 = c + d0 * radii[k], o1 = c + d1 * radii[k];
                    glow.TriC(i0, o0, o1, cols[k], cols[k + 1], cols[k + 1]);
                    glow.TriC(i0, o1, i1, cols[k], cols[k + 1], cols[k]);
                }
            }
        }

        /// <summary>
        /// The sky, all inside the space shell and slowly turning (Stadium): ~1500 chunky low-poly 3D stars in white and
        /// a few pale tints (some twinkle); a galaxy band right round the sky made of thousands of tiny specks, thickest
        /// and warmest at its bright middle, with a faint glow along it; two spiral galaxies; a low-poly sun; and faceted
        /// planets in flat colours (gas giants with bands, rocky, icy, ocean, lava, alien worlds), some with rings and
        /// moons, lit by that sun. Three meshes: stars, galaxy (specks, planets, sun) and the glow.
        /// </summary>
        static void BuildSky(Transform t, Stadium stadium)
        {
            var sky = new GameObject("sky").transform;
            sky.SetParent(t, false);
            stadium.SetSky(sky);
            var rng = new System.Random(9157);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Gauss()
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
            }
            Color L(float r, float g, float b) => new Color(r, g, b).linear;
            Vector3 RandomDir(float minY, float maxY)
            {
                Vector3 d;
                do d = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)); while (d.sqrMagnitude > 1f || d.sqrMagnitude < 0.05f || d.normalized.y < minY || d.normalized.y > maxY);
                return d.normalized;
            }

            // ---- the stars: a flat star outline pulled out to a point front and back, facing the platform ----
            var stars = new MeshBatch { Colored = true };
            var pts = new Vector3[10];
            var starTints = new[] { L(1f, 1f, 1f), L(0.78f, 0.88f, 1f), L(1f, 0.93f, 0.7f), L(1f, 0.72f, 0.88f), L(0.8f, 0.75f, 1f) };
            for (int s = 0; s < StarCount; s++)
            {
                var dir = RandomDir(-0.9f, 1f);
                float dist = R(170f, ShellRadius - 25f);
                var c = dir * dist;
                var n = -dir;
                var u = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var v = Vector3.Cross(n, u);
                float spin = R(0f, Mathf.PI * 2f);
                float big = R(0f, 1f);
                float r0 = (big > 0.96f ? R(5f, 8f) : big > 0.8f ? R(3f, 5f) : big > 0.45f ? R(1.8f, 3f) : R(1.1f, 1.8f)) * dist / 250f;
                float r1 = r0 * 0.42f, depth = r0 * 0.38f;
                float tint = R(0f, 1f);
                stars.Tint = tint < 0.68f ? starTints[0] : tint < 0.8f ? starTints[1] : tint < 0.9f ? starTints[2] : tint < 0.95f ? starTints[3] : starTints[4];
                stars.Tint *= R(0.75f, 1f);
                stars.Extra = R(0f, 1f) < 0.3f ? new Vector4(R(0f, 6.3f), R(0.25f, 0.6f), R(0f, 2.5f), 0) : Vector4.zero;
                for (int i = 0; i < 10; i++)
                {
                    float a = spin + i * Mathf.PI / 5f;
                    pts[i] = c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * (i % 2 == 0 ? r0 : r1);
                }
                var front = c + n * depth;
                var back = c - n * depth;
                for (int i = 0; i < 10; i++)
                {
                    var p0 = pts[i];
                    var p1 = pts[(i + 1) % 10];
                    var mid = (p0 + p1) * 0.5f - c;
                    stars.Tri(front, p0, p1, mid + n * depth, 4f);
                    stars.Tri(back, p0, p1, mid - n * depth, 4f);
                }
            }
            stars.Build(sky, "stars", SkyMat(false), false).GetComponent<MeshRenderer>().receiveShadows = false;

            var gal = new MeshBatch { Colored = true };
            var glow = new MeshBatch { Colored = true };

            // ---- the galaxy band: a great circle tilted 60 degrees, thick and warm round its bright middle ----
            var nb = new Vector3(0.75f, 0.5f, 0.43f).normalized;
            var e1 = Vector3.Cross(nb, Vector3.up).normalized;
            var e2 = Vector3.Cross(nb, e1);
            if (e2.y < 0) e2 = -e2;
            const float core = Mathf.PI * 0.5f + 0.45f; // (high in the sky)
            Vector3 Band(float th, float lat) => ((e1 * Mathf.Cos(th) + e2 * Mathf.Sin(th)) * Mathf.Cos(lat) + nb * Mathf.Sin(lat)).normalized;
            GalaxyCore = Band(core, 0f);
            Color[] bandCols = { L(0.75f, 0.85f, 1f), L(0.8f, 0.66f, 1f), L(1f, 0.62f, 0.86f), L(1f, 0.9f, 0.72f), L(1f, 1f, 1f) };
            for (int i = 0; i < GalaxySpecks; i++)
            {
                bool inCore = R(0f, 1f) < 0.4f;
                float th = inCore ? core + Gauss() * 0.42f : R(0f, Mathf.PI * 2f);
                float near = Mathf.Exp(-Sq(AngleDiff(th, core) / 0.55f));
                float lat = Gauss() * (0.045f + 0.1f * near);
                var dir = Band(th, lat);
                float pick = R(0f, 1f);
                var col = near > 0.5f && pick < 0.55f ? bandCols[3] : pick < 0.4f ? bandCols[0] : pick < 0.62f ? bandCols[1] : pick < 0.75f ? bandCols[2] : bandCols[4];
                col *= R(0.35f, 1f);
                float size = R(0.6f, 1.5f) * (R(0f, 1f) < 0.05f ? 2.2f : 1f);
                Speck(gal, dir * R(318f, 342f), -dir, size, R(0f, 6.3f), col, R(0f, 1f) < 0.15f ? new Vector4(R(0f, 6.3f), 0.5f, R(0f, 2f), 0) : Vector4.zero);
            }
            // its glow: a ribbon right round, brightest along the middle line and at the core
            const int bandSegs = 120;
            float[] lats = { -0.26f, -0.1f, 0f, 0.1f, 0.26f };
            float[] across = { 0f, 0.5f, 1f, 0.5f, 0f };
            Color BandGlow(float th, int j)
            {
                float near = Mathf.Exp(-Sq(AngleDiff(th, core) / 0.6f));
                return Color.Lerp(new Color(0.045f, 0.04f, 0.11f), new Color(0.2f, 0.13f, 0.15f), near) * across[j];
            }
            for (int i = 0; i < bandSegs; i++)
            {
                float t0 = i * Mathf.PI * 2f / bandSegs, t1 = (i + 1) * Mathf.PI * 2f / bandSegs;
                float w0 = 1f + 0.9f * Mathf.Exp(-Sq(AngleDiff(t0, core) / 0.6f)), w1 = 1f + 0.9f * Mathf.Exp(-Sq(AngleDiff(t1, core) / 0.6f));
                for (int j = 0; j < lats.Length - 1; j++)
                {
                    Vector3 a = Band(t0, lats[j] * w0) * 348f, b = Band(t1, lats[j] * w1) * 348f;
                    Vector3 c2 = Band(t1, lats[j + 1] * w1) * 348f, d = Band(t0, lats[j + 1] * w0) * 348f;
                    glow.TriC(a, b, c2, BandGlow(t0, j), BandGlow(t1, j), BandGlow(t1, j + 1));
                    glow.TriC(a, c2, d, BandGlow(t0, j), BandGlow(t1, j + 1), BandGlow(t0, j + 1));
                }
            }
            {
                var cd = Band(core, 0f);
                var bu = Vector3.Cross(cd, nb).normalized;
                GlowDisc(glow, cd * 346f, bu * 1.7f, nb, new[] { 14f, 34f }, new[] { new Color(0.28f, 0.2f, 0.17f), new Color(0.1f, 0.07f, 0.08f), Color.black }, 20);
            }

            // ---- spiral galaxies: specks along log-spiral arms round a bright bulge, and a soft glow ----
            void Spiral(Vector3 dir, float dist, float radius, float tilt, int count, int arms, Color coreCol, Color armCol, Color knotCol, Color glowCol)
            {
                var centre = dir * dist;
                var side = Vector3.Cross(dir, Vector3.up).normalized;
                var axis = Quaternion.AngleAxis(tilt, side) * -dir;
                var p1 = Vector3.Cross(axis, dir).normalized;
                var p2 = Vector3.Cross(axis, p1).normalized;
                for (int i = 0; i < count; i++)
                {
                    Vector3 local;
                    Color col;
                    if (R(0f, 1f) < 0.22f)
                    {
                        float r = Mathf.Abs(Gauss()) * radius * 0.12f, a = R(0f, Mathf.PI * 2f);
                        local = (p1 * Mathf.Cos(a) + p2 * Mathf.Sin(a)) * r + axis * (Gauss() * radius * 0.04f);
                        col = coreCol;
                    }
                    else
                    {
                        float th = Mathf.Pow(R(0f, 1f), 0.8f) * 10.5f;
                        float r = radius * Mathf.Exp(0.25f * (th - 10.5f)) * (1f + Gauss() * 0.08f);
                        float a = th + rng.Next(arms) * Mathf.PI * 2f / arms + Gauss() * 0.16f;
                        local = (p1 * Mathf.Cos(a) + p2 * Mathf.Sin(a)) * r + axis * (Gauss() * radius * 0.012f);
                        col = R(0f, 1f) < 0.1f ? knotCol : Color.Lerp(coreCol, armCol, Mathf.Clamp01(r / (radius * 0.35f)));
                    }
                    col *= R(0.45f, 1f);
                    Speck(gal, centre + local, -dir, R(0.5f, 1.3f) * radius / 60f, R(0f, 6.3f), col, Vector4.zero);
                }
                GlowDisc(glow, centre + dir * 2f, p1, p2, new[] { radius * 0.12f, radius * 0.45f, radius * 1.05f },
                         new[] { glowCol, glowCol * 0.5f, glowCol * 0.16f, Color.black }, 24);
            }
            Spiral(Quaternion.Euler(-30f, 200f, 0) * Vector3.forward, 305f, 62f, 58f, 2200, 2,
                   L(1f, 0.92f, 0.75f), L(0.7f, 0.82f, 1f), L(1f, 0.5f, 0.8f), new Color(0.26f, 0.22f, 0.3f));
            Spiral(Quaternion.Euler(-48f, 75f, 0) * Vector3.forward, 315f, 30f, 35f, 800, 3,
                   L(1f, 0.85f, 0.9f), L(0.85f, 0.6f, 1f), L(0.6f, 0.9f, 1f), new Color(0.2f, 0.12f, 0.24f));

            // ---- a low-poly sun far off: two yellows, a halo and rays ----
            var sunDir = (Quaternion.Euler(-24f, 235f, 0) * Vector3.forward).normalized;
            var sunPos = sunDir * 300f;
            {
                int f = 0;
                foreach (var (a, b, c) in Ico(1))
                {
                    gal.Tint = (f++ * 7 % 3) == 0 ? L(1f, 0.85f, 0.45f) : L(1f, 0.96f, 0.72f);
                    gal.Extra = Vector4.zero;
                    gal.Tri(sunPos + a * 10f, sunPos + b * 10f, sunPos + c * 10f, a + b + c);
                }
                var su = Vector3.Cross(sunDir, Vector3.up).normalized;
                var sv = Vector3.Cross(sunDir, su);
                GlowDisc(glow, sunPos + sunDir * 2f, su, sv, new[] { 12f, 20f, 44f }, new[] { new Color(0.6f, 0.5f, 0.25f), new Color(0.5f, 0.38f, 0.16f), new Color(0.16f, 0.1f, 0.04f), Color.black }, 20);
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI * 2f / 12f + 0.13f;
                    var d = su * Mathf.Cos(a) + sv * Mathf.Sin(a);
                    var w = Vector3.Cross(sunDir, d) * 2.4f;
                    float len = k % 2 == 0 ? 46f : 30f;
                    glow.TriC(sunPos + sunDir * 2f + d * 11f + w, sunPos + sunDir * 2f + d * 11f - w, sunPos + sunDir * 2f + d * len,
                              new Color(0.32f, 0.24f, 0.08f), new Color(0.32f, 0.24f, 0.08f), Color.black);
                }
            }

            // ---- planets ----
            var types = new[]
            {
                // gas giants (banded), then rocky, icy, ocean, lava and alien worlds
                new[] { L(0.85f, 0.62f, 0.4f), L(0.97f, 0.86f, 0.66f), L(0.7f, 0.42f, 0.28f), L(0.99f, 0.94f, 0.82f) },
                new[] { L(0.35f, 0.55f, 0.9f), L(0.58f, 0.77f, 1f), L(0.24f, 0.36f, 0.75f), L(0.78f, 0.9f, 1f) },
                new[] { L(1f, 0.55f, 0.75f), L(1f, 0.78f, 0.88f), L(0.82f, 0.38f, 0.6f), L(0.95f, 0.65f, 0.5f) },
                new[] { L(0.78f, 0.36f, 0.22f), L(0.6f, 0.26f, 0.17f), L(0.9f, 0.58f, 0.38f) },
                new[] { L(0.86f, 0.94f, 1f), L(0.62f, 0.8f, 0.95f), L(1f, 1f, 1f) },
                new[] { L(0.14f, 0.34f, 0.85f), L(0.25f, 0.66f, 0.3f), L(0.78f, 0.68f, 0.42f), L(1f, 1f, 1f) },
                new[] { L(0.2f, 0.12f, 0.12f), L(0.36f, 0.18f, 0.15f), L(1f, 0.45f, 0.08f) },
                new[] { L(0.6f, 0.3f, 0.86f), L(0.86f, 0.46f, 0.92f), L(0.38f, 0.2f, 0.6f) },
                new[] { L(0.46f, 0.8f, 0.3f), L(0.3f, 0.58f, 0.24f), L(0.72f, 0.92f, 0.42f) },
                new[] { L(0.55f, 0.53f, 0.58f), L(0.4f, 0.38f, 0.44f), L(0.72f, 0.7f, 0.74f) },
            };
            var placed = new List<(Vector3 dir, float ang)>
            {
                (sunDir, 0.2f), (Band(core, 0f), 0.22f),
                ((Quaternion.Euler(-30f, 200f, 0) * Vector3.forward).normalized, 0.24f),
                ((Quaternion.Euler(-48f, 75f, 0) * Vector3.forward).normalized, 0.12f),
            };
            var ico0 = Ico(0); var ico1 = Ico(1); var ico2 = Ico(2);
            int planets = 0;
            for (int p = 0; p < PlanetCount; p++)
            {
                int type = p % types.Length;
                bool gas = type <= 2;
                float radius = p < 2 ? R(22f, 30f) : p < 8 ? R(9f, 16f) : R(3.5f, 8f);
                bool ringed = gas ? R(0f, 1f) < 0.65f : R(0f, 1f) < 0.18f;
                Vector3 dir = Vector3.up;
                float dist = 0f, ang = 0f;
                bool ok = false;
                for (int tries = 0; tries < 200 && !ok; tries++)
                {
                    dir = RandomDir(-0.55f, 0.88f);
                    dist = p < 2 ? R(265f, 300f) : p < 8 ? R(175f, 290f) : R(135f, 280f);
                    ang = Mathf.Asin(Mathf.Min(1f, radius * (ringed ? 2.3f : 1.3f) / dist));
                    ok = true;
                    foreach (var (d2, a2) in placed)
                        if (Vector3.Angle(dir, d2) * Mathf.Deg2Rad < (ang + a2) * 1.25f + 0.05f) { ok = false; break; }
                }
                if (!ok) continue;
                placed.Add((dir, ang));
                planets++;
                var centre = dir * dist;
                var pal = types[type];
                var tilt = Quaternion.Euler(R(-28f, 28f), R(0f, 360f), R(-28f, 28f));
                var toSun = (sunPos - centre).normalized;
                float s1 = R(0f, 9f), s2 = R(0f, 9f), s3 = R(0f, 9f), freq = R(2.2f, 3.4f);
                float Noise(Vector3 q) => Mathf.Sin(q.x * freq + s1) * Mathf.Sin(q.y * freq * 1.1f + s2) * Mathf.Sin(q.z * freq * 0.9f + s3)
                                          + 0.5f * Mathf.Sin((q.x + q.z) * freq * 2.1f + s2 * 1.7f);
                foreach (var (a, b, c) in radius > 12f ? ico2 : ico1)
                {
                    var fc = (a + b + c).normalized; // (in the planet's own frame: y is its axis)
                    float nz = Noise(fc), lat = fc.y;
                    Color col;
                    if (gas) col = pal[Mathf.Abs(Mathf.FloorToInt(lat * 3.6f + nz * 0.45f + 10f)) % pal.Length];
                    else if (type == 4) col = Mathf.Abs(lat) > 0.75f ? pal[2] : nz > 0.35f ? pal[1] : pal[0];
                    else if (type == 5) col = Mathf.Abs(lat) > 0.82f ? pal[3] : nz > 0.25f ? (nz > 0.7f ? pal[2] : pal[1]) : pal[0];
                    else if (type == 6) col = nz > 0.55f ? pal[2] : nz > -0.1f ? pal[1] : pal[0];
                    else col = nz > 0.4f ? pal[2] : nz > -0.25f ? pal[0] : pal[1];
                    var wn = tilt * fc;
                    float lit = 0.17f + 0.83f * Mathf.Max(0f, Vector3.Dot(wn, toSun));
                    if (type == 6 && col == pal[2]) lit = Mathf.Max(lit, 0.85f); // lava glows in the dark
                    gal.Tint = col * lit;
                    gal.Extra = Vector4.zero;
                    gal.Tri(centre + tilt * a * radius, centre + tilt * b * radius, centre + tilt * c * radius, wn);
                }
                var axisW = tilt * Vector3.up;
                if (ringed)
                {
                    var ru = (tilt * Vector3.right).normalized;
                    var rv = (tilt * Vector3.forward).normalized;
                    float ringLit = 0.45f + 0.55f * Mathf.Abs(Vector3.Dot(axisW, toSun));
                    var bands = new[] { (1.35f, 1.62f, pal[1]), (1.68f, 1.98f, pal[0]), (2.03f, 2.25f, pal[pal.Length - 1]) };
                    const int rs = 40;
                    foreach (var (r0, r1, rc) in bands)
                    {
                        gal.Tint = rc * ringLit * 0.85f;
                        for (int s = 0; s < rs; s++)
                        {
                            float a0 = s * Mathf.PI * 2f / rs, a1 = (s + 1) * Mathf.PI * 2f / rs;
                            Vector3 d0 = ru * Mathf.Cos(a0) + rv * Mathf.Sin(a0), d1 = ru * Mathf.Cos(a1) + rv * Mathf.Sin(a1);
                            Vector3 i0 = centre + d0 * radius * r0, i1 = centre + d1 * radius * r0, o0 = centre + d0 * radius * r1, o1 = centre + d1 * radius * r1;
                            gal.Quad(i0, o0, o1, i1, axisW); // both sides
                            gal.Quad(i0, o0, o1, i1, -axisW);
                        }
                    }
                }
                // moons
                int moons = R(0f, 1f) < 0.45f ? 0 : R(0f, 1f) < 0.65f ? 1 : 2;
                for (int m = 0; m < moons; m++)
                {
                    float mr = radius * R(0.13f, 0.26f);
                    var md = (axisW * R(-0.35f, 0.35f) + Vector3.Cross(axisW, RandomDir(-1f, 1f))).normalized;
                    var mc = centre + md * radius * (ringed ? R(2.6f, 3.3f) : R(1.7f, 2.8f));
                    var mcol = R(0f, 1f) < 0.5f ? L(0.7f, 0.68f, 0.66f) : L(0.75f, 0.62f, 0.48f);
                    foreach (var (a, b, c) in mr > 3f ? ico1 : ico0)
                    {
                        var fc = (a + b + c).normalized;
                        float lit = 0.17f + 0.83f * Mathf.Max(0f, Vector3.Dot(fc, (sunPos - mc).normalized));
                        gal.Tint = mcol * lit * R(0.88f, 1f);
                        gal.Tri(mc + a * mr, mc + b * mr, mc + c * mr, fc);
                    }
                }
            }
            stadium.Planets = planets;
            gal.Build(sky, "galaxy", SkyMat(false), false).GetComponent<MeshRenderer>().receiveShadows = false;
            var glowMat = SkyMat(true);
            if (glowMat != null) glow.Build(sky, "galaxy glow", glowMat, false).GetComponent<MeshRenderer>().receiveShadows = false;
        }

        // =====================================================================
        // the crowd
        // =====================================================================

        /// <summary>
        /// The stadium round the platform: how far its front wall is from the platform's edge (the gap you fall through
        /// into space), its rows (how many, how deep, how much each one rises), the first row's floor (a little under the
        /// platform's top, so the front rows look across at the fight), how round its corners are, seat spacing, how big
        /// the fans are (a bit bigger than life, so they read from the platform) and how many sections it's drawn in.
        /// </summary>
        public const float Gap = 12f, RowDepth = 1.75f, RowRise = 0.85f, FrontY = -2.4f, SeatSpacing = 1.2f, FanScale = 1.35f;
        public const int Rows = 6, CornerSteps = 3, Sections = 16;
        /// <summary>From the front wall to the back wall, and the back wall's top.</summary>
        public const float BowlDepth = Rows * RowDepth, WallTop = FrontY + (Rows - 1) * RowRise + 2.2f;
        /// <summary>The bottom of the stadium's hull (a ring keel) and the struts that hold the platform in its middle.</summary>
        public const float KeelY = -12.5f, StrutY = -12.9f;

        /// <summary>Outward normal of the platform outline's edge i (from corner i to i + 1).</summary>
        static Vector2 EdgeNormal(int i)
        {
            int n = Outline.Length;
            i = (i % n + n) % n;
            var a = Outline[i];
            var b = Outline[(i + 1) % n];
            var e = b - a;
            var nrm = new Vector2(e.y, -e.x).normalized;
            return Vector2.Dot(nrm, (a + b) * 0.5f) < 0 ? -nrm : nrm;
        }

        /// <summary>The outward direction at each point of a Ring (the same for every distance).</summary>
        static Vector2[] RingNormals()
        {
            int n = Outline.Length;
            var r = new Vector2[n * (CornerSteps + 1)];
            for (int i = 0; i < n; i++)
            {
                Vector2 n0 = EdgeNormal(i - 1), n1 = EdgeNormal(i);
                float a0 = Mathf.Atan2(n0.y, n0.x), da = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, Mathf.Atan2(n1.y, n1.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                for (int k = 0; k <= CornerSteps; k++)
                {
                    float a = a0 + da * k / CornerSteps;
                    r[i * (CornerSteps + 1) + k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                }
            }
            return r;
        }

        /// <summary>The platform's outline pushed out by d all round, with rounded corners (x, z; going round). Every
        /// ring has the same points in the same order, so rings at different distances join up into one stadium.</summary>
        public static Vector2[] Ring(float d)
        {
            var nrm = RingNormals();
            var r = new Vector2[nrm.Length];
            for (int i = 0; i < r.Length; i++) r[i] = Outline[i / (CornerSteps + 1)] + nrm[i] * d;
            return r;
        }

        /// <summary>Where a line out from the middle at this angle (degrees from +z) crosses the ring d out from the edge.</summary>
        public static Vector2 RingPoint(float angle, float d)
        {
            var ring = Ring(d);
            var dir = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad));
            float best = 0f;
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % ring.Length], e = b - a;
                float den = dir.x * e.y - dir.y * e.x;
                if (Mathf.Abs(den) < 1e-6f) continue;
                float t = (a.x * e.y - a.y * e.x) / den, u = (a.x * dir.y - a.y * dir.x) / den;
                if (t > 0f && u >= 0f && u <= 1f) best = Mathf.Max(best, t);
            }
            return dir * best;
        }

        /// <summary>How often each animation comes up (Crowd.shader: 0 jump, 1 fist pump, 2 wave, 3 clap, 4 sway, 5 dance,
        /// 6 spin, 7 sit and get up, 8 sit and clap, 9 sit back, 10 point, 11 head-bang, 12 clap over the head).</summary>
        static readonly float[] k_AnimWeights = { 11, 12, 10, 13, 5, 10, 3, 9, 6, 5, 6, 5, 7 };

        /// <summary>A fan's animation. Each stand has a mood: 0 a mix, 1 a section swaying together, 2 mostly sitting, 3 jumping, 4 clapping.</summary>
        static int PickAnim(System.Random rng, int mood, int row, int rows, out bool together)
        {
            together = false;
            double u = rng.NextDouble();
            switch (mood)
            {
                case 1: if (u < 0.6) { together = true; return 4; } break;
                case 2: if (u < 0.55) return 7 + rng.Next(3); break;
                case 3: if (u < 0.45) return rng.Next(2) == 0 ? 0 : 12; break;
                case 4: if (u < 0.45) return 3; break;
            }
            var w = new float[k_AnimWeights.Length];
            float total = 0f;
            for (int i = 0; i < w.Length; i++)
            {
                w[i] = k_AnimWeights[i];
                if (i >= 7 && i <= 9 && row == rows - 1) w[i] *= 1.8f; // the back row sits more
                if (row == 0 && (i == 0 || i == 6 || i == 10)) w[i] *= 1.5f; // the front row's the wildest
                total += w[i];
            }
            float x = (float)rng.NextDouble() * total;
            for (int i = 0; i < w.Length; i++) { x -= w[i]; if (x <= 0f) return i; }
            return 0;
        }

        /// <summary>A fan's skin: hues all round the wheel (golden ratio), plus classic grey-green aliens and pastels.</summary>
        static Color FanColour(System.Random rng, int i)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            double u = rng.NextDouble();
            if (u < 0.12) return Color.HSVToRGB(R(0.22f, 0.36f), R(0.18f, 0.4f), R(0.75f, 0.92f));
            if (u < 0.22) return Color.HSVToRGB(R(0f, 1f), R(0.2f, 0.38f), R(0.92f, 1f));
            return Color.HSVToRGB(Mathf.Repeat(i * 0.618034f + R(-0.04f, 0.04f), 1f), R(0.5f, 1f), R(0.6f, 1f));
        }

        /// <summary>
        /// The crowd alien (CrowdAlien.txt, baked from the player model by Tools/crowd_alien.py): flat-coloured triangles
        /// with what moves each corner (uv0: chain, w1, w2; colour alpha: on the head), and the joints.
        /// </summary>
        static Mesh LoadCrowdAlien(Dictionary<string, Vector3> joints)
        {
            var ta = Resources.Load<TextAsset>("SpaceArena/CrowdAlien");
            if (ta == null) return null;
            var inv = CultureInfo.InvariantCulture;
            float F(string s) => float.Parse(s, NumberStyles.Float, inv);
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var col = new List<Color>();
            var uv = new List<Vector4>();
            foreach (var raw in ta.text.Split('\n'))
            {
                var s = raw.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (s.Length == 0) continue;
                if (s[0] == "P" && s.Length >= 5) { joints[s[1]] = new Vector3(F(s[2]), F(s[3]), F(s[4])); continue; }
                if (s.Length < 13) continue;
                pos.Add(new Vector3(F(s[0]), F(s[1]), F(s[2])));
                nrm.Add(new Vector3(F(s[3]), F(s[4]), F(s[5])));
                var c = new Color(F(s[6]), F(s[7]), F(s[8])).linear;
                c.a = F(s[12]);
                col.Add(c);
                uv.Add(new Vector4(F(s[9]), F(s[10]), F(s[11]), 0f));
            }
            if (pos.Count < 3) return null;
            var tris = new int[pos.Count / 3 * 3];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            var mesh = new Mesh { name = "crowd alien" };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetColors(col);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(0, 1f, 0), new Vector3(4f, 4f, 4f));
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// The stadium into the stands mesh (coloured per vertex; local space, the platform's top in the middle at y = 0):
        /// one continuous bowl all the way round the platform, like a normal stadium - a padded front wall (red and blue
        /// pads, like the old stadium's), rows of steps rising away from the platform with a bench along the back of each
        /// (the sitters sit on it) and glowing pink step edges, a tall back wall with floodlight masts on it, and under it
        /// all one ring of blue hull with orange panel lines narrowing to a pale crystal keel. Nothing of it is within
        /// Gap of the platform, so you still fall off the edge into space.
        /// </summary>
        static void BuildBowlMesh(MeshBatch mb)
        {
            Color L(float r, float g, float b) => new Color(r, g, b).linear;
            Color deck = L(0.34f, 0.33f, 0.42f), deck2 = L(0.29f, 0.28f, 0.37f), riser = L(0.22f, 0.21f, 0.3f), rim = L(0.11f, 0.1f, 0.17f), hull = L(0.2f, 0.36f, 0.8f);
            Color pink = L(1f, 0.32f, 0.9f), orange = L(1f, 0.56f, 0.15f), seat = L(0.42f, 0.26f, 0.7f), seatFront = L(0.33f, 0.2f, 0.56f), tip = L(0.6f, 0.75f, 1f);
            Color padRed = L(0.75f, 0.2f, 0.2f), padBlue = L(0.2f, 0.25f, 0.55f), lamp = L(1f, 0.97f, 0.82f), outside = L(0.16f, 0.28f, 0.66f);
            mb.Extra = Vector4.zero;
            var nrm = RingNormals();
            int m = nrm.Length;
            Vector3 P(Vector2[] ring, int i, float y) => new Vector3(ring[i].x, y, ring[i].y);
            Vector3 Out(int i, int j) => new Vector3(nrm[i].x + nrm[j].x, 0f, nrm[i].y + nrm[j].y);
            // a flat band between two rings (facing up), a wall on one ring (facing in, towards the platform, or out), and a
            // sloping band from one ring at one height to another ring at another (facing `side` in or out, and down)
            void Flat(float d0, float d1, float y, Color c)
            {
                Vector2[] a = Ring(d0), b = Ring(d1);
                mb.Tint = c;
                for (int i = 0; i < m; i++) { int j = (i + 1) % m; mb.Quad(P(a, i, y), P(a, j, y), P(b, j, y), P(b, i, y), Vector3.up); }
            }
            void Wall(float d, float y0, float y1, Color c, bool facingIn)
            {
                var a = Ring(d);
                mb.Tint = c;
                for (int i = 0; i < m; i++) { int j = (i + 1) % m; mb.Quad(P(a, i, y0), P(a, j, y0), P(a, j, y1), P(a, i, y1), facingIn ? -Out(i, j) : Out(i, j)); }
            }
            void Slope(float d0, float y0, float d1, float y1, Color c, float side, bool line)
            {
                Vector2[] a = Ring(d0), b = Ring(d1);
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m;
                    var face = Out(i, j).normalized * side + Vector3.down * 0.6f;
                    mb.Tint = c;
                    mb.Quad(P(a, i, y0), P(a, j, y0), P(b, j, y1), P(b, i, y1), face);
                    if (!line) continue;
                    // an orange panel line round the hull, just under its top edge
                    Vector3 la = Vector3.Lerp(P(a, i, y0), P(b, i, y1), 0.22f), lb = Vector3.Lerp(P(a, j, y0), P(b, j, y1), 0.22f);
                    var n = Vector3.Cross(lb - la, P(b, i, y1) - P(a, i, y0)).normalized;
                    if (Vector3.Dot(n, face) < 0) n = -n;
                    mb.Tint = orange;
                    mb.Line(la + n * 0.03f, lb + n * 0.03f, 0.12f, 0.05f, n);
                }
            }

            float back = Gap + BowlDepth;
            // the padded front wall, its pink top and the pads along its face (red and blue, like the old stadium)
            Wall(Gap - 0.3f, FrontY - 1f, FrontY + 0.6f, rim, true);
            Wall(Gap, FrontY, FrontY + 0.6f, rim, false);
            Flat(Gap - 0.3f, Gap, FrontY + 0.6f, pink);
            {
                var ring = Ring(Gap - 0.36f);
                float len = 0f;
                for (int i = 0; i < m; i++) len += Vector2.Distance(ring[i], ring[(i + 1) % m]);
                int pads = Mathf.RoundToInt(len / 2.6f);
                for (int p = 0; p < pads; p++)
                {
                    var (pos, n) = AlongRing(ring, nrm, (p + 0.5f) / pads * len);
                    var rot = Quaternion.LookRotation(new Vector3(n.x, 0f, n.y));
                    mb.Tint = p % 2 == 0 ? padBlue : padRed;
                    mb.Box(new Vector3(pos.x, FrontY - 0.15f, pos.y), rot, new Vector3(2.4f, 1.3f, 0.12f));
                }
            }
            // the rows: a step, its riser, a bench along its back and a pink edge along its front
            for (int r = 0; r < Rows; r++)
            {
                float yt = FrontY + r * RowRise, d0 = Gap + r * RowDepth, d1 = d0 + RowDepth;
                Flat(d0, d1, yt, r % 2 == 0 ? deck : deck2);
                if (r > 0) Wall(d0, yt - RowRise, yt, riser, true);
                Flat(d1 - 0.6f, d1 - 0.1f, yt + 0.55f, seat);
                Wall(d1 - 0.6f, yt, yt + 0.55f, seatFront, true);
                Flat(d0 + 0.02f, d0 + 0.14f, yt + 0.012f, pink);
            }
            // the back wall (inside and out) with a pink top
            float lastTread = FrontY + (Rows - 1) * RowRise;
            Wall(back, lastTread, WallTop, rim, true);
            Wall(back + 0.35f, FrontY - 1f, WallTop, outside, false);
            Wall(back + 0.37f, WallTop - 0.75f, WallTop - 0.5f, pink, false);
            Wall(back + 0.37f, FrontY - 0.6f, FrontY - 0.45f, orange, false);
            Flat(back, back + 0.35f, WallTop, pink);
            // the hull under it all: one ring, in and out, narrowing to a pale crystal keel
            float mid = Gap + BowlDepth * 0.5f;
            Slope(Gap - 0.3f, FrontY - 1f, Gap + 2.2f, FrontY - 5f, hull, -1f, true);
            Slope(Gap + 2.2f, FrontY - 5f, mid - 0.4f, KeelY + 1.6f, hull, -1f, true);
            Slope(back + 0.35f, FrontY - 1f, back - 2.2f, FrontY - 5f, hull, 1f, true);
            Slope(back - 2.2f, FrontY - 5f, mid + 0.4f, KeelY + 1.6f, hull, 1f, true);
            Slope(mid - 0.4f, KeelY + 1.6f, mid, KeelY, tip, -1f, false);
            Slope(mid + 0.4f, KeelY + 1.6f, mid, KeelY, tip, 1f, false);
            // floodlight masts on the back wall, off the ends and the sides, their lamps tipped down at the platform
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var p = RingPoint(a, back + 0.6f);
                var outDir = new Vector3(p.x, 0f, p.y).normalized;
                var basePos = new Vector3(p.x, WallTop, p.y);
                mb.Tint = rim;
                mb.Box(basePos + Vector3.up * 6.5f, Quaternion.LookRotation(outDir), new Vector3(0.55f, 13f, 0.55f));
                var head = basePos + Vector3.up * 13.4f - outDir * 0.4f;
                var rot = Quaternion.LookRotation(-outDir) * Quaternion.Euler(28f, 0f, 0f);
                mb.Box(head + rot * new Vector3(0, 0, -0.25f), rot, new Vector3(5f, 2.4f, 0.4f));
                mb.Tint = lamp;
                for (int lx = 0; lx < 3; lx++)
                    for (int ly = 0; ly < 2; ly++)
                        mb.Box(head + rot * new Vector3(-1.6f + lx * 1.6f, -0.55f + ly * 1.1f, 0.02f), rot, new Vector3(1.35f, 0.9f, 0.12f));
            }
        }

        /// <summary>The point s metres round a ring (and the outward direction there).</summary>
        static (Vector2 pos, Vector2 n) AlongRing(Vector2[] ring, Vector2[] nrm, float s)
        {
            int m = ring.Length;
            for (int guard = 0; guard < 2; guard++)
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m;
                    float seg = Vector2.Distance(ring[i], ring[j]);
                    if (s <= seg || (guard == 1 && i == m - 1))
                    {
                        float t = seg > 1e-5f ? Mathf.Clamp01(s / seg) : 0f;
                        return (Vector2.Lerp(ring[i], ring[j], t), Vector2.Lerp(nrm[i], nrm[j], t).normalized);
                    }
                    s -= seg;
                }
            return (ring[0], nrm[0]);
        }

        /// <summary>
        /// The struts under the platform: eight beams from the stadium's keel in to a hub under the platform's crystal
        /// point, so the platform and the stadium are one structure. They're well below KillDepth (you're lost in space
        /// before you'd get near them) and have no colliders.
        /// </summary>
        static void BuildStruts(Transform t, Material mat)
        {
            var mb = new MeshBatch();
            float mid = Gap + BowlDepth * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                var p = RingPoint(22.5f + i * 45f, mid);
                var a = new Vector3(p.x, StrutY, p.y);
                var b = new Vector3(p.x, 0f, p.y).normalized * 2.2f + Vector3.up * StrutY;
                mb.Box((a + b) * 0.5f, Quaternion.LookRotation(b - a), new Vector3(0.9f, 0.9f, (a - b).magnitude));
            }
            var hub = Scale(Outline, 2.6f / HalfX);
            mb.Frustum(hub, StrutY + 0.7f, 1f, StrutY - 0.7f, 1f, true, true, 4f);
            mb.Build(t, "stadium struts", mat, false);
        }

        /// <summary>
        /// The crowd: one stadium all round the platform (not touching it), packed with aliens like the players - every
        /// one its own colour, size, animation and timing. Fans in the front of each row stand, the sitters sit on the
        /// benches. Drawn by Stadium in sections (one draw per section in view); the stadium itself is one mesh.
        /// </summary>
        static void BuildCrowd(Transform t, Stadium stadium)
        {
            var shader = Resources.Load<Shader>("SpaceArena/Crowd");
            if (shader == null || !shader.isSupported) { Debug.LogWarning("[RockGame] crowd shader missing: no crowd in the arena"); return; }
            var joints = new Dictionary<string, Vector3>();
            var mesh = LoadCrowdAlien(joints);
            var rng = new System.Random(4242);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var standMesh = new MeshBatch { Colored = true };
            BuildBowlMesh(standMesh);
            var centre = Cfg.ArenaCenter;
            int[] moods = { 0, 1, 2, 3, 4, 0, 2, 1, 0, 3, 4, 0, 1, 2, 0, 3 };
            var nrm = RingNormals();
            // every seat, binned by section (by its angle round the middle) so each section's fans are one run in the buffer
            var bins = new List<Vector4>[Sections];
            for (int s = 0; s < Sections; s++) bins[s] = new List<Vector4>();
            var anims = new HashSet<int>();
            var hues = new HashSet<int>();
            var sectionTogether = new float[Sections];
            for (int s = 0; s < Sections; s++) sectionTogether[s] = R(0f, 1f);
            int fanNo = 0;
            for (int r = 0; r < Rows; r++)
            {
                // the standing fans' line (the front of the row) and the sitters' (on the bench at the back)
                float yt = FrontY + r * RowRise;
                var standRing = Ring(Gap + r * RowDepth + 0.8f);
                var sitRing = Ring(Gap + (r + 1) * RowDepth - 0.36f);
                float len = 0f;
                for (int i = 0; i < standRing.Length; i++) len += Vector2.Distance(standRing[i], standRing[(i + 1) % standRing.Length]);
                int seats = Mathf.FloorToInt(len / SeatSpacing);
                float sitLen = 0f;
                for (int i = 0; i < sitRing.Length; i++) sitLen += Vector2.Distance(sitRing[i], sitRing[(i + 1) % sitRing.Length]);
                float offset = R(0f, 1f);
                for (int k = 0; k < seats; k++)
                {
                    if (R(0f, 1f) < 0.05f) continue; // an empty seat here and there
                    float u = (k + offset + R(-0.08f, 0.08f)) / seats;
                    var (probe, _) = AlongRing(standRing, nrm, u * len);
                    float ang = Mathf.Atan2(probe.x, probe.y) * Mathf.Rad2Deg;
                    int sec = Mathf.FloorToInt(Mathf.Repeat(ang + 180f / Sections, 360f) / (360f / Sections)) % Sections;
                    int anim = PickAnim(rng, moods[sec % moods.Length], r, Rows, out bool sync);
                    bool sitting = anim >= 7 && anim <= 9;
                    Vector2 p, n;
                    if (sitting) (p, n) = AlongRing(sitRing, nrm, u * sitLen);
                    else (p, n) = AlongRing(standRing, nrm, u * len);
                    var world = centre + new Vector3(p.x, yt, p.y);
                    // facing in: across at the platform (a little towards its middle), each one turned a bit
                    var face = Vector2.Lerp(-n, -p.normalized, 0.3f);
                    float yaw = Mathf.Atan2(face.x, face.y) + R(-14f, 14f) * Mathf.Deg2Rad;
                    var skin = FanColour(rng, fanNo);
                    Color.RGBToHSV(skin, out float h, out float sat, out _);
                    if (sat > 0.45f) hues.Add(Mathf.FloorToInt(h * 12f) % 12);
                    var lin = skin.linear;
                    var bin = bins[sec];
                    bin.Add(new Vector4(world.x, world.y, world.z, yaw));
                    bin.Add(new Vector4(lin.r, lin.g, lin.b, FanScale * R(0.93f, 1.07f)));
                    bin.Add(new Vector4(anim, sync ? sectionTogether[sec] + R(0f, 0.004f) : R(0f, 1f), sync ? 1f : R(0.85f, 1.2f), 0f));
                    anims.Add(anim);
                    fanNo++;
                }
            }
            var data = new List<Vector4>();
            var draws = new List<Stadium.StandDraw>();
            for (int s = 0; s < Sections; s++)
            {
                var bin = bins[s];
                if (bin.Count == 0) continue;
                int start = data.Count / 3;
                // what has to be in view for the section's fans to be drawn (arms up, jumping)
                var b = new Bounds((Vector3)bin[0], Vector3.zero);
                var sum = Vector3.zero;
                for (int i = 0; i < bin.Count; i += 3)
                {
                    var p = (Vector3)bin[i];
                    b.Encapsulate(p + Vector3.down * 0.5f);
                    b.Encapsulate(p + Vector3.up * 4f);
                    sum += p;
                }
                b.Expand(2f);
                data.AddRange(bin);
                draws.Add(new Stadium.StandDraw { Start = start, Count = bin.Count / 3, Bounds = b, Centre = sum / (bin.Count / 3) });
            }
            var standMat = new Material(shader) { name = "crowd stands" };
            standMat.SetFloat("_Stand", 1f);
            var sgo = standMesh.Build(t, "crowd stands", standMat, false);
            sgo.GetComponent<MeshRenderer>().receiveShadows = false;
            if (mesh == null) { Debug.LogWarning("[RockGame] CrowdAlien.txt missing: empty stands"); return; }
            var fanMat = new Material(shader) { name = "crowd" };
            fanMat.SetFloat("_Stand", 0f);
            Vector4 J(string n) => joints.TryGetValue(n, out var v) ? (Vector4)v : Vector4.zero;
            fanMat.SetVectorArray("_CrowdJ", new[] { J("hips"), J("neck"), J("shoulderL"), J("shoulderR"), J("elbowL"), J("elbowR"), J("hipL"), J("hipR") });
            float rest = Vector3.Angle((Vector3)(J("elbowR") - J("shoulderR")), Vector3.down);
            float drop = J("hipL").y - J("kneeL").y;
            fanMat.SetVectorArray("_CrowdK", new[] { J("kneeL"), J("kneeR"), new Vector4(rest, drop, 0, 0), Vector4.zero });
            Vector4 Axis(string s) => Vector3.Cross(((Vector3)(J("wrist" + s) - J("elbow" + s))).normalized, Vector3.forward).normalized;
            fanMat.SetVectorArray("_CrowdAxis", new[] { Axis("L"), Axis("R") });
            stadium.SetCrowd(mesh, fanMat, data.ToArray(), draws, anims.Count, hues.Count);
        }

        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static Vector2[] Scale(Vector2[] poly, float s)
        {
            var r = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++) r[i] = poly[i] * s;
            return r;
        }

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
        readonly List<Color> m_C = new List<Color>();
        readonly List<Vector4> m_X = new List<Vector4>();
        readonly List<int> m_T = new List<int>();

        /// <summary>
        /// Coloured: every triangle added gets Tint as its vertex colour and Extra as its uv0 (instead of projected
        /// UVs), so many colours can share one draw (shaders that read vertex colours, e.g. the arena's sky and stands).
        /// </summary>
        public bool Colored;
        public Color Tint = Color.white;
        public Vector4 Extra;

        /// <summary>A triangle facing `outward`; UVs are projected onto its plane, `tile` metres per repeat.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, float tile = 4f)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0) { (b, c) = (c, b); n = -n; }
            n.Normalize();
            int i = m_V.Count;
            if (Colored)
            {
                m_V.Add(a); m_V.Add(b); m_V.Add(c);
                for (int k = 0; k < 3; k++) { m_N.Add(n); m_C.Add(Tint); m_X.Add(Extra); }
                m_T.Add(i); m_T.Add(i + 1); m_T.Add(i + 2);
                return;
            }
            Vector3 e1, e2;
            if (Mathf.Abs(n.y) > 0.7f) { e1 = Vector3.right; e2 = Vector3.forward; }
            else { e1 = Vector3.Cross(Vector3.up, n).normalized; e2 = Vector3.Cross(n, e1); }
            foreach (var p in new[] { a, b, c })
            {
                m_V.Add(p);
                m_N.Add(n);
                m_U.Add(new Vector2(Vector3.Dot(p, e1), Vector3.Dot(p, e2)) / tile);
            }
            m_T.Add(i); m_T.Add(i + 1); m_T.Add(i + 2);
        }

        /// <summary>Coloured meshes: a triangle with its own colour at each corner (whichever way it faces).</summary>
        public void TriC(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = m_V.Count;
            m_V.Add(a); m_V.Add(b); m_V.Add(c);
            m_C.Add(ca); m_C.Add(cb); m_C.Add(cc);
            for (int k = 0; k < 3; k++) { m_N.Add(n); m_X.Add(Extra); }
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
            if (Colored)
            {
                mesh.SetColors(m_C);
                mesh.SetUVs(0, m_X);
            }
            else mesh.SetUVs(0, m_U);
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
    /// The sudden death arena at runtime: the screens show the countdown and the clock, the sky turns slowly, and the
    /// crowd is drawn (one draw per section of the stadium in view) and egged on: livelier in sudden death, wild for a few seconds on
    /// ROCK! and every death, celebrating at the end, and every so often a Mexican wave goes round. Everything is only
    /// drawn while you're there, and while you are, it's space: no fog, a purple ambient light.
    /// </summary>
    public class Stadium : MonoBehaviour
    {
        public static Stadium Instance;

        readonly List<TextMesh> m_Screens = new List<TextMesh>();
        Renderer[] m_Renderers;
        Light[] m_Lights;
        bool m_Shown = true;
        Renderer m_Shell;
        Transform m_Sky;

        /// <summary>One section of the stadium: its fans' run in the crowd buffer, and what has to be in view to draw them.</summary>
        public struct StandDraw
        {
            public int Start, Count;
            public Bounds Bounds;
            public Vector3 Centre;
            public MaterialPropertyBlock Props;
        }

        Mesh m_FanMesh;
        Material m_FanMat;
        GraphicsBuffer m_FanBuf;
        readonly List<StandDraw> m_Stands = new List<StandDraw>();
        readonly Plane[] m_Planes = new Plane[6];
        float m_Hype, m_Clock, m_NextWave = 8f, m_WaveAt = -100f, m_WaveFrom;
        string m_LastWord = "";
        int m_LastDead;
        AudioSource m_CrowdSound;

        /// <summary>How many fans there are, how many different animations and hues (of 12) they have.</summary>
        public int CrowdCount { get; private set; }
        public int CrowdAnimations { get; private set; }
        public int CrowdHues { get; private set; }
        /// <summary>How many fans were drawn this frame (the sections in view).</summary>
        public int DrawnFans { get; private set; }
        /// <summary>How excited the crowd is (0..1).</summary>
        public float Excitement { get; private set; }
        /// <summary>The planets in the sky.</summary>
        public int Planets;
        /// <summary>(tests) don't draw the crowd, to time it.</summary>
        public bool CrowdHidden;
        public IReadOnlyList<StandDraw> CrowdStands => m_Stands;

        static readonly int k_Data = Shader.PropertyToID("_CrowdData"), k_Mood = Shader.PropertyToID("_CrowdMood"),
                            k_Centre = Shader.PropertyToID("_CrowdCentre"), k_Start = Shader.PropertyToID("_Start");

        // what the space look changed, put back when you leave
        bool m_EnvOn, m_SavedFog;
        Color m_SavedSky, m_SavedEquator, m_SavedGround;
        AmbientMode m_SavedMode;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            SpaceLook(false);
            if (m_FanBuf != null) { m_FanBuf.Release(); m_FanBuf = null; }
            if (Instance == this) Instance = null;
        }

        public void SetShell(Renderer r) => m_Shell = r;
        public void SetSky(Transform t) => m_Sky = t;
        /// <summary>The sky (stars, galaxy, planets): it turns slowly.</summary>
        public Transform Sky => m_Sky;
        public void AddScreen(TextMesh tm) => m_Screens.Add(tm);

        /// <summary>The crowd: the alien mesh, its material, 3 float4s per fan (see Crowd.shader) and the stands.</summary>
        public void SetCrowd(Mesh mesh, Material mat, Vector4[] data, List<StandDraw> stands, int animations, int hues)
        {
            m_FanMesh = mesh;
            m_FanMat = mat;
            CrowdCount = data.Length / 3;
            CrowdAnimations = animations;
            CrowdHues = hues;
            if (m_FanBuf != null) m_FanBuf.Release();
            m_FanBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(3, data.Length), 16);
            if (data.Length > 0) m_FanBuf.SetData(data);
            m_Stands.Clear();
            foreach (var s in stands)
            {
                var d = s;
                d.Props = new MaterialPropertyBlock();
                d.Props.SetFloat(k_Start, s.Start);
                m_Stands.Add(d);
            }
        }

        /// <summary>The crowd goes wild for a moment (the fight starts, someone dies).</summary>
        public static void Roar() { if (Instance != null) Instance.m_Hype = 1f; }

        /// <summary>Start a Mexican wave now (they come round on their own every 25 s).</summary>
        public void StartWave() { m_WaveAt = Time.time; m_WaveFrom = Random.Range(0f, Mathf.PI * 2f); m_NextWave = Time.time + 25f; }

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

        /// <summary>Space has no fog, and a cool purple ambient (it lights the underside and the crowd).</summary>
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
            var game = NetGame.Instance;
            var cam = Camera.main;
            float camDist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : float.MaxValue;
            bool shown = camDist < 300f;
            ShowStadium(shown);
            SpaceLook(shown);
            if (!shown)
            {
                if (m_CrowdSound) m_CrowdSound.volume = 0f;
                return;
            }
            if (m_Sky) m_Sky.localRotation = Quaternion.Euler(0f, Time.time * 0.25f, 0f); // the galaxy turns, very slowly
            string text = "ROCK BRAWL", word = "";
            if (game != null && game.IsSpawned)
            {
                if (game.S == GameState.SuddenDeath)
                {
                    word = Hud.FightWord(game, out _);
                    if (word != "") text = word;
                    else { int s = Mathf.CeilToInt(game.TimeLeft); text = $"SUDDEN DEATH\n{s / 60}:{s % 60:00}"; }
                }
                else if (game.S == GameState.Waiting) text = "WAITING FOR\nPLAYERS";
                else if (game.S == GameState.GameOver) text = game.Winner.Value >= 0 ? $"{Cfg.TeamLabel(game.Winner.Value)}\nWINS!" : "DRAW";
            }
            // each screen has its text on both faces: only the one facing you is drawn (the font shows through things)
            var cp = Camera.main != null ? Camera.main.transform.position : transform.position;
            foreach (var s in m_Screens)
            {
                if (s.text != text) s.text = text;
                var r = s.GetComponent<Renderer>();
                if (r) r.enabled = Vector3.Dot(cp - s.transform.position, -s.transform.forward) > 0f;
            }
            Cheer(game, word);
        }

        /// <summary>How excited the crowd is, the Mexican wave, the crowd's noise.</summary>
        void Cheer(NetGame game, string word)
        {
            // ROCK! and every death in the arena set them off
            if (word == "ROCK!" && m_LastWord != "ROCK!") Roar();
            m_LastWord = word;
            int dead = 0;
            foreach (var p in PlayerNet.All) if (p != null && p.Dead.Value && SpaceArena.NearArena(p.transform.position)) dead++;
            if (dead > m_LastDead) Roar();
            m_LastDead = dead;
            bool live = game != null && game.IsSpawned && game.S == GameState.SuddenDeath;
            bool over = game != null && game.IsSpawned && game.S == GameState.GameOver;
            m_Hype = Mathf.MoveTowards(m_Hype, 0f, Time.deltaTime * 0.22f);
            Excitement = Mathf.Clamp01((over ? 0.75f : live ? 0.3f : 0.12f) + m_Hype * 0.75f);
            m_Clock += Time.deltaTime * (1f + Excitement * 0.5f); // (the crowd's own clock: they speed up when it's wild)
            // a Mexican wave every so often: one and a half times round in ten seconds
            if (Time.time > m_NextWave) StartWave();
            float u = (Time.time - m_WaveAt) / 10f, wave = 0f, waveAng = 0f;
            if (u >= 0f && u <= 1f)
            {
                wave = Mathf.SmoothStep(0f, 1f, u / 0.1f) * (1f - Mathf.SmoothStep(0f, 1f, (u - 0.85f) / 0.15f));
                waveAng = m_WaveFrom + u * Mathf.PI * 3f;
            }
            Shader.SetGlobalVector(k_Mood, new Vector4(Excitement, waveAng, wave, m_Clock));
            Shader.SetGlobalVector(k_Centre, new Vector4(transform.position.x, transform.position.y, transform.position.z, 30f));
            if (m_FanBuf != null) Shader.SetGlobalBuffer(k_Data, m_FanBuf);
            if (m_FanMesh != null && Sfx.Crowd != null)
            {
                if (m_CrowdSound == null)
                {
                    m_CrowdSound = gameObject.AddComponent<AudioSource>();
                    m_CrowdSound.clip = Sfx.Crowd;
                    m_CrowdSound.loop = true;
                    m_CrowdSound.spatialBlend = 0f;
                    m_CrowdSound.Play();
                }
                m_CrowdSound.volume = (0.08f + Excitement * 0.22f + wave * 0.06f) * GameSettings.SfxVolume;
            }
        }

        /// <summary>The fans: one draw per section of the stadium in view (in LateUpdate: after the camera has moved this frame).</summary>
        void LateUpdate()
        {
            DrawnFans = 0;
            var cam = Camera.main;
            if (!m_Shown || CrowdHidden || cam == null || m_FanMesh == null || m_FanMat == null || m_FanBuf == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, m_Planes);
            foreach (var s in m_Stands)
            {
                if (s.Count <= 0 || !GeometryUtility.TestPlanesAABB(m_Planes, s.Bounds)) continue;
                var rp = new RenderParams(m_FanMat)
                {
                    camera = cam,
                    layer = gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = s.Bounds,
                    matProps = s.Props,
                };
                Graphics.RenderMeshPrimitives(rp, m_FanMesh, 0, s.Count);
                DrawnFans += s.Count;
            }
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
