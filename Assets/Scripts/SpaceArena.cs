using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// The sudden death arena (also the waiting lobby), Super Smash Bros "Final Destination" style: a flat octagonal
    /// platform with glowing pink edges and a lightning bolt inlay, a blue mechanical underside with orange panel lines
    /// and a glowing core, floating in a starry nebula. No walls: walk off the edge and you fall into space (in sudden
    /// death that's a death like any other; in the lobby you're just put back on the platform). The crowd stands on
    /// floating rocks all around, detached from the platform: aliens like you, every one a different colour.
    /// Built identically on every peer (non-networked) by MapBuilder.
    /// </summary>
    public static class SpaceArena
    {
        /// <summary>Half sizes of the octagon (x across, z along the duel) and how much its corners are cut off.</summary>
        public const float HalfX = 21f, HalfZ = 27f, Chamfer = 8f;
        /// <summary>Fall this far below the top of the platform in sudden death and you're lost in space.</summary>
        public const float KillDepth = 10f;
        /// <summary>The space shell around the arena (it hides the far-away map).</summary>
        public const float ShellRadius = 360f;

        static Texture2D s_Deck, s_Hull, s_Inlay;
        static Shader s_Glow, s_Sky;

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

        static Material GlowMat(Color c, Texture tex = null, bool additive = false)
        {
            if (s_Glow == null) s_Glow = Resources.Load<Shader>("SpaceArena/SpaceGlow");
            if (s_Glow == null || !s_Glow.isSupported) return Art.Mat(c);
            var m = new Material(s_Glow) { name = "space glow" };
            m.SetColor("_Color", c);
            if (tex != null) m.SetTexture("_MainTex", tex);
            if (additive)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = 3000;
            }
            return m;
        }

        static Material LitMat(Color c, Texture tex, float smooth)
        {
            var m = new Material(Art.Mat(Color.white)) { name = "space lit" };
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
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
            MakeTextures();

            var pink = new Color(1f, 0.28f, 0.95f);
            var orange = new Color(1f, 0.55f, 0.12f);
            var deckMat = LitMat(new Color(0.62f, 0.62f, 0.7f), s_Deck, 0.3f);
            var rimMat = LitMat(new Color(0.07f, 0.06f, 0.12f), null, 0.5f);
            var hullMat = LitMat(Color.white, s_Hull, 0.45f);
            var pinkMat = GlowMat(pink);
            var orangeMat = GlowMat(orange);
            var whiteMat = GlowMat(new Color(0.95f, 0.95f, 1f));

            // ---------- the top: a flat octagon (the only thing you can stand on) ----------
            var deck = new MeshBatch();
            deck.Frustum(Outline, 0f, 1f, -1f, 1f, true, true, 6f);
            var deckGo = deck.Build(t, "ArenaFloor", deckMat, true);
            // the rim faces use their own dark material: rebuild the slab as two submeshes would be fussier, so the
            // rim is a second, slightly bigger band around the top's sides
            var rim = new MeshBatch();
            rim.Frustum(Scale(Outline, 1.004f), -0.02f, 1f, -1.02f, 1f, false, false, 4f);
            rim.Build(t, "rim", rimMat, true);
            var mc = deckGo.AddComponent<MeshCollider>();
            mc.sharedMesh = deckGo.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
            deckGo.AddComponent<GroundMarker>();

            // ---------- the underside: tiers of blue machinery, narrowing to a glowing core ----------
            var hull = new MeshBatch();
            var lines = new MeshBatch();
            var tiers = new (float y0, float s0, float y1, float s1, int dividers)[]
            {
                (-1.0f, 0.975f, -2.3f, 0.87f, 1),
                (-2.3f, 0.84f, -5.2f, 0.62f, 2),
                (-5.2f, 0.6f, -8.2f, 0.38f, 1),
                (-8.2f, 0.36f, -10.8f, 0.15f, 0),
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
                    var centre = V(p2, -5.6f);
                    var size = new Vector3(Mathf.Min(3.2f, Vector3.Distance(V(a, 0), V(b, 0)) * 0.22f), 6.5f, 1.1f);
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
                    // a little yellow light at the bottom of every other fin
                    if (k == 0) hull.Box(centre + f + rot * new Vector3(0, -size.y * 0.5f + 0.5f, 0.02f), rot, new Vector3(0.6f, 0.25f, 0.05f));
                }
            }
            hull.Build(t, "underside", hullMat, true);
            lines.Build(t, "panel lines", orangeMat, false);

            // the dark core with its halo, and a thin beam of light falling away below it
            var core = Art.Part(t, Art.Sphere, Color.white, new Vector3(0, -11.6f, 0), Vector3.one * 5.6f, default, false, LitMat(new Color(0.02f, 0.02f, 0.05f), null, 0.92f), "core");
            core.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            // its glow: nested additive shells, brighter towards the middle
            for (int k = 0; k < 5; k++)
            {
                var shellMat = GlowMat(new Color(0.55f, 0.65f, 1f) * (0.2f - k * 0.035f), null, true);
                shellMat.SetFloat("_Cull", (float)CullMode.Back);
                var g = Art.Part(t, Art.Sphere, Color.white, new Vector3(0, -11.6f, 0), Vector3.one * (6.3f + k * 1.6f), default, false, shellMat, "core glow");
                g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            var beam = Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, -54f, 0), new Vector3(0.12f, 40f, 0.12f), default, false, GlowMat(new Color(0.9f, 0.95f, 1f)), "beam");
            beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var beamGlow = Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, -54f, 0), new Vector3(0.7f, 40f, 0.7f), default, false, GlowMat(new Color(0.15f, 0.2f, 0.35f), null, true), "beam glow");
            beamGlow.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---------- glowing pink edges, white corner marks and the lightning bolt inlay ----------
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
            inlay.Build(t, "bolt inlay", GlowMat(new Color(0.9f, 0.9f, 1f), s_Inlay), false);

            // ---------- lights: a cool glow up from the core onto the machinery, a pink wash over the top ----------
            var lc = new GameObject("core light");
            lc.transform.SetParent(t, false);
            lc.transform.localPosition = new Vector3(0, -14f, 0);
            var l = lc.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 32f;
            l.intensity = 5f;
            l.color = new Color(0.55f, 0.7f, 1f);
            var lp = new GameObject("pink light");
            lp.transform.SetParent(t, false);
            lp.transform.localPosition = new Vector3(0, 16f, 0);
            var l2 = lp.AddComponent<Light>();
            l2.type = LightType.Point;
            l2.range = 45f;
            l2.intensity = 1.2f;
            l2.color = new Color(0.9f, 0.5f, 1f);

            // ---------- space all around ----------
            if (s_Sky == null) s_Sky = Resources.Load<Shader>("SpaceArena/SpaceSky");
            var skyMat = s_Sky != null && s_Sky.isSupported ? new Material(s_Sky) { name = "space sky" } : Art.Mat(new Color(0.03f, 0.02f, 0.08f));
            var shell = Art.Part(t, Art.Sphere, Color.white, Vector3.zero, Vector3.one * ShellRadius * 2f, default, false, skyMat, "space");
            var sr = shell.GetComponent<MeshRenderer>();
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            if (s_Sky == null) shell.GetComponent<MeshFilter>().sharedMesh = Inverted(Art.Sphere);
            stadium.SetShell(sr);

            // ---------- the crowd, on rocks floating in space ----------
            BuildCrowd(t, stadium);

            // ---------- floating holo screens with the countdown and the clock ----------
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + i * 90f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var screen = new GameObject("jumbotron").transform;
                screen.SetParent(t, false);
                screen.localPosition = dir * 50f + Vector3.up * 20f;
                screen.localRotation = Quaternion.LookRotation(dir); // faces outward, so its back (-z) faces the platform
                Art.Box(screen, new Color(0.04f, 0.02f, 0.09f), Vector3.zero, new Vector3(10f, 5.5f, 0.3f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
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

        static void BuildCrowd(Transform t, Stadium stadium)
        {
            var rng = new System.Random(4242);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var prefab = Resources.Load<GameObject>("Alien/AlienRigged");
            var rockMat = LitMat(new Color(0.42f, 0.38f, 0.5f), null, 0.1f);
            var topMat = LitMat(new Color(0.75f, 0.7f, 0.85f), s_Deck, 0.2f);
            var ringMat = GlowMat(new Color(1f, 0.28f, 0.95f));
            const int islands = 10;
            int fanNo = 0;
            for (int i = 0; i < islands; i++)
            {
                float ang = (i + R(-0.25f, 0.25f)) * Mathf.PI * 2f / islands;
                float dist = R(40f, 54f);
                float h = (i % 3) switch { 0 => R(-6f, -2.5f), 1 => R(-1f, 3f), _ => R(6f, 11f) };
                var island = new GameObject("crowd rock").transform;
                island.SetParent(t, false);
                island.localPosition = new Vector3(Mathf.Cos(ang) * dist, h, Mathf.Sin(ang) * dist);
                var toCentre = -new Vector3(island.localPosition.x, 0, island.localPosition.z).normalized;
                island.localRotation = Quaternion.LookRotation(toCentre);
                float r = R(3.9f, 4.7f);
                var ce = Art.Cylinder.bounds.extents; // (sized by the mesh's real bounds)
                var top = Art.Part(island, Art.Cylinder, Color.white, new Vector3(0, -0.4f, 0), new Vector3(r / ce.x, 0.4f / ce.y, r / ce.z), default, false, topMat, "top");
                var rock = Art.Part(island, Art.MakeRock(i + 7, 0.15f), Color.white, new Vector3(0, -r * 1.3f, 0), new Vector3(r * 1.25f, r * 1.2f, r * 1.25f), new Vector3(R(-8, 8), R(0, 360), 180f), false, rockMat, "rock");
                var ring = new MeshBatch();
                const int segs = 16;
                for (int s = 0; s < segs; s++)
                {
                    float a0 = s * Mathf.PI * 2f / segs, a1 = (s + 1) * Mathf.PI * 2f / segs;
                    var p0 = new Vector3(Mathf.Cos(a0) * r, -0.12f, Mathf.Sin(a0) * r);
                    var p1 = new Vector3(Mathf.Cos(a1) * r, -0.12f, Mathf.Sin(a1) * r);
                    var n = ((p0 + p1) * 0.5f).normalized;
                    ring.Line(p0 + n * 0.03f, p1 + n * 0.03f, 0.14f, 0.06f, n);
                    ring.Line(p0 * 0.93f + Vector3.up * 0.13f, p1 * 0.93f + Vector3.up * 0.13f, 0.16f, 0.03f, Vector3.up);
                }
                ring.Build(island, "ring", ringMat, false);
                foreach (var mr in island.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;
                _ = top; _ = rock;
                stadium.AddIsland(island, R(0f, 10f));
                if (prefab == null) continue;
                // the fans: two loose rows facing the platform (local +z)
                var spots = new List<Vector2>();
                for (int row = 0; row < 2; row++)
                {
                    float z = r * (0.42f - row * 0.45f);
                    for (float x = -r; x <= r; x += 1.6f)
                    {
                        var sp = new Vector2(x + (row % 2) * 0.8f + R(-0.15f, 0.15f), z + R(-0.15f, 0.15f));
                        if (sp.magnitude < r - 0.75f) spots.Add(sp);
                    }
                }
                foreach (var sp in spots)
                {
                    float x = sp.x, z = sp.y;
                    var fan = new GameObject("fan").transform;
                    fan.SetParent(island, false);
                    fan.localPosition = new Vector3(x, 0f, z);
                    fan.localRotation = Quaternion.Euler(0, R(-20f, 20f), 0);
                    fan.localScale = Vector3.one * 1.3f; // a bit bigger than life, so you can see them from the platform
                    var model = Object.Instantiate(prefab, fan, false);
                    model.name = "alien";
                    model.transform.localScale = new Vector3(Cfg.ModelWidth, 1f, Cfg.ModelWidth);
                    foreach (var an in model.GetComponentsInChildren<Animator>(true)) Object.Destroy(an);
                    // your skin, in every colour: hues spread out round the wheel (golden ratio)
                    var mats = new List<Material>();
                    PlayerNet.SkinAlien(model, mats);
                    float hue = Mathf.Repeat(fanNo * 0.618034f + 0.11f, 1f);
                    var col = Color.HSVToRGB(hue, R(0.6f, 1f), R(0.75f, 1f));
                    foreach (var m in mats)
                    {
                        var tint = m.name.Contains("Head") ? Color.Lerp(Color.white, col, 0.55f) * 1.35f : col * 1.6f;
                        tint.a = 1f;
                        m.SetColor("_BaseColor", tint);
                        m.color = tint;
                    }
                    foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        smr.shadowCastingMode = ShadowCastingMode.Off;
                        smr.localBounds = new Bounds(new Vector3(0, 0.9f, 0), new Vector3(2.5f, 2.5f, 2.5f));
                    }
                    stadium.AddFan(fan, model.transform, R(0f, 10f), R(0.8f, 1.3f));
                    fanNo++;
                }
            }
        }

        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static Vector2[] Scale(Vector2[] poly, float s)
        {
            var r = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++) r[i] = poly[i] * s;
            return r;
        }

        /// <summary>The lightning bolt across the middle of the platform (x, z), going round.</summary>
        static Vector2[] BoltOutline() => new[]
        {
            new Vector2(1.2f, -20f), new Vector2(3.6f, 1.6f), new Vector2(0.9f, 0.6f),
            new Vector2(-1.2f, 20f), new Vector2(-3.6f, -1.6f), new Vector2(-0.9f, -0.6f),
        };

        static Mesh Inverted(Mesh src)
        {
            var m = Object.Instantiate(src);
            var tris = m.triangles;
            for (int i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            m.triangles = tris;
            return m;
        }

        // =====================================================================
        // generated textures
        // =====================================================================

        static void MakeTextures()
        {
            if (s_Deck != null) return;
            var rng = new System.Random(77);
            float Rn() => (float)rng.NextDouble();
            // the top: dark grey with a fine woven grid and some grit
            const int S = 256;
            s_Deck = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "fd deck", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float v = 0.2f + (Rn() - 0.5f) * 0.05f;
                bool grid = x % 8 == 0 || y % 8 == 0;
                if (grid) v -= 0.05f;
                if ((x / 8 + y / 8) % 2 == 0) v += 0.015f;
                if (Rn() < 0.004f) v += 0.12f;
                px[y * S + x] = new Color(v, v, v * 1.08f, 1f);
            }
            s_Deck.SetPixels(px);
            s_Deck.Apply(true);

            // the underside: blue plates with dark seams and rivets
            s_Hull = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "fd hull", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = new Color(0.16f, 0.33f, 0.78f);
                float k = 1f + (Rn() - 0.5f) * 0.08f;
                int px64 = x % 64, py64 = y % 128;
                if (px64 < 2 || py64 < 2) k = 0.35f;                      // seams
                else if (px64 < 5 || py64 < 5) k *= 1.18f;               // a lit bevel
                else if (px64 > 60 || py64 > 124) k *= 0.7f;             // a shadowed bevel
                if ((px64 == 9 || px64 == 55) && (py64 % 32 == 12)) k = 1.5f; // rivets
                if (x % 128 > 70 && x % 128 < 74 && py64 > 20 && py64 < 100) k *= 0.6f;
                c *= k;
                c.a = 1f;
                px[y * S + x] = c;
            }
            s_Hull.SetPixels(px);
            s_Hull.Apply(true);

            // the bolt inlay: a glittery purple mesh
            const int I = 128;
            s_Inlay = new Texture2D(I, I, TextureFormat.RGBA32, true) { name = "fd inlay", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var ip = new Color[I * I];
            var sparkle = new[] { new Color(0.4f, 0.9f, 1f), new Color(1f, 0.85f, 0.3f), new Color(1f, 0.4f, 0.95f), Color.white, new Color(0.5f, 0.6f, 1f) };
            for (int y = 0; y < I; y++)
            for (int x = 0; x < I; x++)
            {
                var c = Color.Lerp(new Color(0.2f, 0.08f, 0.38f), new Color(0.12f, 0.16f, 0.45f), Rn());
                if ((x + y) % 6 == 0 || (x - y + I) % 6 == 0) c = Color.Lerp(c, new Color(0.55f, 0.45f, 0.9f), 0.6f);
                if (Rn() < 0.07f) c = sparkle[rng.Next(sparkle.Length)] * (0.6f + Rn() * 0.5f);
                c.a = 1f;
                ip[y * I + x] = c;
            }
            s_Inlay.SetPixels(ip);
            s_Inlay.Apply(true);
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
    /// The sudden death arena at runtime: the crowd cheers (and goes wild on the countdown and kills), their rocks drift,
    /// the screens show the clock. Everything is only drawn while you're there, and while you are, it's space: no fog,
    /// a purple ambient light.
    /// </summary>
    public class Stadium : MonoBehaviour
    {
        public static Stadium Instance;

        struct Fan
        {
            public Transform Body;
            public Vector3 Base;
            public Transform LArm, RArm;
            public Quaternion LLow, LUp, RLow, RUp;
            public float Phase, Speed;
        }

        readonly List<Fan> m_Fans = new List<Fan>();
        readonly List<(Transform t, Vector3 basePos, float phase)> m_Islands = new List<(Transform, Vector3, float)>();
        readonly List<TextMesh> m_Screens = new List<TextMesh>();
        AudioSource m_Crowd;
        float m_Hype;
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
        public void AddIsland(Transform t, float phase) => m_Islands.Add((t, t.localPosition, phase));

        /// <summary>A fan (the rigged alien under `body`): works out its arms-out and arms-up poses for cheering.</summary>
        public void AddFan(Transform body, Transform model, float phase, float speed)
        {
            var f = new Fan { Body = body, Base = body.localPosition, Phase = phase, Speed = speed };
            var map = new Dictionary<string, Transform>();
            foreach (var tr in model.GetComponentsInChildren<Transform>(true)) map[tr.name] = tr;
            map.TryGetValue("LeftUpperArm", out var la); map.TryGetValue("LeftLowerArm", out var lf);
            map.TryGetValue("RightUpperArm", out var ra); map.TryGetValue("RightLowerArm", out var rf);
            if (la != null && lf != null && ra != null && rf != null)
            {
                Pose(body, la, lf, out f.LLow, out f.LUp);
                Pose(body, ra, rf, out f.RLow, out f.RUp);
                f.LArm = la;
                f.RArm = ra;
            }
            m_Fans.Add(f);
        }

        static void Pose(Transform body, Transform arm, Transform fore, out Quaternion low, out Quaternion up)
        {
            var cur = (fore.position - arm.position).normalized;
            float side = Mathf.Sign(Vector3.Dot(arm.position - body.position, body.right));
            if (side == 0) side = 1;
            var wantUp = body.rotation * new Vector3(side * 0.38f, 1f, 0.12f).normalized;
            var wantLow = body.rotation * new Vector3(side * 0.9f, 0.45f, 0.35f).normalized;
            var parent = arm.parent != null ? arm.parent.rotation : Quaternion.identity;
            up = Quaternion.Inverse(parent) * (Quaternion.FromToRotation(cur, wantUp) * arm.rotation);
            low = Quaternion.Inverse(parent) * (Quaternion.FromToRotation(cur, wantLow) * arm.rotation);
        }

        /// <summary>Someone scored a kill / the fight started: the crowd goes wild for a moment.</summary>
        public static void Roar() { if (Instance != null) Instance.m_Hype = 1f; }

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
            var g = NetGame.Instance;
            bool live = g != null && g.IsSpawned && g.S == GameState.SuddenDeath;
            var cam = Camera.main;
            float camDist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : float.MaxValue;
            bool shown = camDist < 300f;
            ShowStadium(shown);
            SpaceLook(shown);
            bool near = camDist < 160f;
            if (!near) { if (m_Crowd) m_Crowd.volume = 0f; return; }
            m_Hype = Mathf.MoveTowards(m_Hype, 0f, Time.deltaTime * 0.3f);
            float excite = 0.35f + (live ? 0.3f : 0f) + m_Hype * 0.7f;
            float t = Time.time;
            foreach (var (it, bp, ph) in m_Islands) it.localPosition = bp + Vector3.up * Mathf.Sin(t * 0.45f + ph) * 0.7f;
            for (int i = 0; i < m_Fans.Count; i++)
            {
                var f = m_Fans[i];
                float w = t * (4f + excite * 3f) * f.Speed + f.Phase;
                float jump = Mathf.Max(0f, Mathf.Sin(w)) * 0.3f * excite;
                f.Body.localPosition = f.Base + Vector3.up * jump;
                if (f.LArm == null) continue;
                // arms pump up and down, a bit out of step with each other
                float a = Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(w * 1.3f + 0.5f) * (0.6f + excite));
                float b = Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(w * 1.3f + 1.4f) * (0.6f + excite));
                f.LArm.localRotation = Quaternion.Slerp(f.LLow, f.LUp, a);
                f.RArm.localRotation = Quaternion.Slerp(f.RLow, f.RUp, b);
            }
            if (m_Crowd == null)
            {
                m_Crowd = gameObject.AddComponent<AudioSource>();
                m_Crowd.clip = Sfx.Crowd;
                m_Crowd.loop = true;
                m_Crowd.spatialBlend = 0f;
                m_Crowd.Play();
            }
            m_Crowd.volume = (0.12f + excite * 0.25f) * GameSettings.SfxVolume;
            string text = "ROCK BRAWL";
            if (g != null && g.IsSpawned)
            {
                if (g.S == GameState.SuddenDeath)
                {
                    double left = g.FightAt.Value - g.NetworkManager.ServerTime.Time;
                    if (left > 0) text = Mathf.CeilToInt((float)left).ToString();
                    else if (left > -1.5) text = "FIGHT!";
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
