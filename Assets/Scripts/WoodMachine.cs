using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Auto Wood: the wood machine on the bedrock, to the right of the alien machine. Futuristic tech in the alien
    /// machine's style (silver housing, a column with spinning rings and a floating orb, glowing-tipped pylons, a console
    /// with a screen) that saws wood out of nothing and pushes it down its chute onto the pile (the pile itself is a world
    /// item, NetGame.ServerTickAutoWood). It changes in front of you when your team upgrades its wood gen:
    /// level 0 - silver like the alien machine (green and team-colour lights): one ring, two pylons, a saw blade on the side;
    /// level 1 - bigger and YELLOW: yellow light strips on every edge, two rings, a spinning yellow beacon, twin exhaust
    ///           stacks puffing yellow, hazard stripes on the chute;
    /// level 2 - bigger again and PINK: hovering over a pink glow on four tesla pylons, a three-ring gyroscope round a
    ///           glass dome with a pink energy core, a spinning crystal over it, two emitter arms aimed at the chute, four
    ///           exhausts puffing pink.
    /// </summary>
    public class WoodMachine : MonoBehaviour
    {
        int m_Team, m_Level = -1;
        Transform m_Visual;
        readonly List<Transform> m_Rings = new List<Transform>();
        readonly List<Transform> m_Vents = new List<Transform>();
        readonly List<Transform> m_Saws = new List<Transform>();
        Transform m_Core, m_Crystal, m_Beacon, m_Log;
        Vector3 m_CoreBase, m_CrystalBase;
        Light m_Light;
        Material m_GlowMat;
        Color m_Accent;
        float m_NextPuff, m_Pop = 1f, m_LogT = 1f, m_NextLog, m_LightBase;

        // the alien machine's palette (MapBuilder.BuildMachine) and the level colours
        static readonly Color k_Metal = new Color(0.3f, 0.33f, 0.36f), k_Silver = new Color(0.72f, 0.74f, 0.78f), k_SilverDark = new Color(0.5f, 0.52f, 0.56f);
        static readonly Color k_Dark = new Color(0.08f, 0.09f, 0.11f);
        static readonly Color k_Green = new Color(0.45f, 0.95f, 0.55f), k_Yellow = new Color(1f, 0.82f, 0.15f), k_Pink = new Color(1f, 0.32f, 0.78f), k_Cyan = new Color(0.35f, 0.95f, 1f);

        public static WoodMachine Create(Transform root, int team)
        {
            var go = new GameObject("WoodMachine " + Cfg.TeamName[team]);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(Cfg.WoodMachinePos(team), Quaternion.LookRotation(-Cfg.BackDir(team)));
            var w = go.AddComponent<WoodMachine>();
            w.m_Team = team;
            w.Build(0);
            return w;
        }

        int Level => NetGame.Instance != null && NetGame.Instance.IsSpawned ? NetGame.Instance.WoodGenLevelOf(m_Team) : 0;

        /// <summary>The level colour as a glowing material, one per machine so it can pulse.</summary>
        Material AccentMat()
        {
            if (m_GlowMat) Destroy(m_GlowMat);
            m_GlowMat = new Material(Art.Mat(m_Accent));
            if (m_GlowMat.HasProperty("_EmissionColor"))
            {
                m_GlowMat.EnableKeyword("_EMISSION");
                m_GlowMat.SetColor("_EmissionColor", m_Accent * 1.5f);
            }
            Art.Register(m_GlowMat, m_Accent);
            return m_GlowMat;
        }

        void OnDestroy()
        {
            if (m_GlowMat) Destroy(m_GlowMat);
        }

        void Build(int level)
        {
            using var tint = ColorSlots.Use(ColorSlots.WoodMachine); // (Settings > Display colours)
            if (m_Visual) Destroy(m_Visual.gameObject);
            m_Rings.Clear();
            m_Vents.Clear();
            m_Saws.Clear();
            m_Core = m_Crystal = m_Beacon = null;
            m_Level = level;
            m_Visual = new GameObject("visual").transform;
            m_Visual.SetParent(transform, false);
            var t = m_Visual;
            var teamGlow = Color.Lerp(Cfg.TeamColor[Mathf.Clamp(m_Team, 0, 3)], Color.white, 0.35f);
            var gTeam = Workbench.Glow(teamGlow);
            m_Accent = level == 2 ? k_Pink : level == 1 ? k_Yellow : k_Green;
            var acc = AccentMat();
            var gGreen = Workbench.Glow(k_Green);
            var gYellow = Workbench.Glow(k_Yellow, 1.8f);
            var gCyan = Workbench.Glow(k_Cyan, 1.8f);
            var screen = Art.Ghost(level == 1 ? new Color(1f, 0.9f, 0.4f, 0.7f) : level == 2 ? new Color(1f, 0.5f, 0.9f, 0.7f) : new Color(0.4f, 1f, 0.8f, 0.7f));
            float s = level == 2 ? 1.16f : level == 1 ? 1.08f : 1f;
            float hx = 0.48f * s, hz = 0.4f * s;           // the housing's half size
            float baseTop = level == 2 ? 0.42f : 0.3f;     // (level 2 hovers on its pylons)
            float houseTop = baseTop + 0.78f * s;
            float colTop = houseTop + 0.5f * s;
            float cz = -0.08f;                             // the column stands a little back

            // what you bump into
            var body = new GameObject("body");
            body.transform.SetParent(t, false);
            var bc = body.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, houseTop * 0.5f, -0.05f);
            bc.size = new Vector3(hx * 2.3f, houseTop, hz * 2.2f);

            // ---- the base ----
            if (level < 2)
            {
                Art.Box(t, k_Metal, new Vector3(0, baseTop * 0.5f, -0.05f), new Vector3(hx * 2.5f, baseTop, hz * 2.5f));
                Art.Box(t, k_SilverDark, new Vector3(0, baseTop + 0.015f, -0.05f), new Vector3(hx * 2.35f, 0.03f, hz * 2.35f));
            }
            else
            {
                // hovering: a thin plate over a pink glow, held by four tesla pylons
                Workbench.Cyl(t, k_Pink, new Vector3(0, 0.02f, -0.05f), hx * 1.2f, 0.02f, default, acc);
                Workbench.Cyl(t, Color.white, new Vector3(0, 0.2f, -0.05f), hx * 1.05f, 0.36f, default, Art.Ghost(new Color(1f, 0.4f, 0.85f, 0.16f)));
                Art.Box(t, k_Metal, new Vector3(0, baseTop - 0.05f, -0.05f), new Vector3(hx * 2.4f, 0.1f, hz * 2.4f));
                Art.Box(t, k_Pink, new Vector3(0, baseTop - 0.1f, -0.05f), new Vector3(hx * 2.2f, 0.01f, hz * 2.2f), default, false, acc);
            }
            if (level >= 1) // a glowing band round the base
                for (int k = -1; k <= 1; k += 2)
                {
                    Art.Box(t, m_Accent, new Vector3(0, baseTop - 0.05f, -0.05f + k * hz * 1.25f), new Vector3(hx * 2.4f, 0.025f, 0.012f), default, false, acc);
                    Art.Box(t, m_Accent, new Vector3(k * hx * 1.25f, baseTop - 0.05f, -0.05f), new Vector3(0.012f, 0.025f, hz * 2.4f), default, false, acc);
                }

            // ---- the housing: silver, a dark front panel with the output port, vents ----
            float hy = (baseTop + houseTop) * 0.5f, hh = houseTop - baseTop;
            Art.Box(t, k_Silver, new Vector3(0, hy, -0.05f), new Vector3(hx * 2f, hh, hz * 2f));
            Art.Box(t, k_SilverDark, new Vector3(0, houseTop - 0.02f, -0.05f), new Vector3(hx * 2.06f, 0.05f, hz * 2.06f));
            Art.Box(t, k_Dark, new Vector3(0, hy + 0.05f, hz - 0.04f), new Vector3(hx * 1.4f, hh * 0.62f, 0.02f));
            // the port the wood comes out of, framed in the level colour
            var portAt = new Vector3(0, baseTop + 0.2f, hz - 0.03f);
            Art.Box(t, k_Dark, portAt, new Vector3(0.36f, 0.22f, 0.04f));
            Art.Box(t, m_Accent, portAt + new Vector3(0, 0.12f, 0.01f), new Vector3(0.4f, 0.025f, 0.02f), default, false, acc);
            for (int k = -1; k <= 1; k += 2) Art.Box(t, m_Accent, portAt + new Vector3(k * 0.19f, 0, 0.01f), new Vector3(0.025f, 0.24f, 0.02f), default, false, acc);
            // grille on the front panel
            for (int i = 0; i < 4; i++) Art.Box(t, k_Metal, new Vector3(0, hy + 0.12f * s + i * 0.07f * s, hz - 0.025f), new Vector3(hx * 1.2f, 0.022f, 0.012f));
            // the chute down to the pile (hazard stripes from level 1)
            var chuteA = new Vector3(0, baseTop + 0.16f, hz);
            var chuteB = new Vector3(0, 0.22f, 1.02f);
            var cd = chuteB - chuteA;
            var cRot = Quaternion.LookRotation(cd.normalized).eulerAngles;
            var chuteMid = (chuteA + chuteB) * 0.5f;
            Art.Box(t, k_Metal, chuteMid, new Vector3(0.38f, 0.03f, cd.magnitude), cRot);
            for (int k = -1; k <= 1; k += 2)
                Art.Box(t, k_Silver, chuteMid + new Vector3(k * 0.2f, 0.04f, 0), new Vector3(0.025f, 0.08f, cd.magnitude), cRot);
            if (level >= 1)
                for (int i = 0; i < 4; i++)
                {
                    var p = Vector3.Lerp(chuteA, chuteB, 0.15f + i * 0.22f) + new Vector3(0, 0.022f, 0);
                    Art.Box(t, k_Yellow, p + new Vector3(-0.2f, 0.085f, 0), new Vector3(0.03f, 0.012f, 0.06f), cRot, false, gYellow);
                    Art.Box(t, k_Yellow, p + new Vector3(0.2f, 0.085f, 0), new Vector3(0.03f, 0.012f, 0.06f), cRot, false, gYellow);
                }

            // level 1+: light strips up every corner of the housing
            if (level >= 1)
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Art.Box(t, m_Accent, new Vector3(sx * (hx + 0.005f), hy, -0.05f + sz * (hz + 0.005f)), new Vector3(0.03f, hh * 0.92f, 0.03f), default, false, acc);
            // level 2: pink seams across the sides
            if (level == 2)
                for (int k = -1; k <= 1; k += 2)
                    for (int i = 0; i < 3; i++)
                        Art.Box(t, k_Pink, new Vector3(k * (hx + 0.006f), baseTop + hh * (0.25f + i * 0.25f), -0.05f), new Vector3(0.01f, 0.02f, hz * 1.7f), default, false, acc);

            // ---- the left side: a slanted console with a screen ----
            var conAt = new Vector3(-hx - 0.1f, baseTop + 0.42f * s, 0.05f);
            Art.Box(t, k_Metal, conAt + new Vector3(0.04f, -0.2f * s, 0), new Vector3(0.14f, 0.4f * s, 0.34f));
            Art.Box(t, k_Metal, conAt, new Vector3(0.24f, 0.05f, 0.4f), new Vector3(0, 0, 35));
            Art.Box(t, Color.white, conAt + new Vector3(-0.012f, 0.018f, 0), new Vector3(0.2f, 0.012f, 0.34f), new Vector3(0, 0, 35), false, screen);

            // ---- the right side: the saw blade in a round housing (spins while it works) ----
            var sawAt = new Vector3(hx + 0.03f, baseTop + 0.42f * s, 0.02f);
            Workbench.Cyl(t, k_Metal, sawAt, 0.26f * s, 0.04f, new Vector3(0, 0, 90));
            Workbench.Cyl(t, k_Dark, sawAt + new Vector3(0.022f, 0, 0), 0.22f * s, 0.01f, new Vector3(0, 0, 90));
            var saw = new GameObject("saw").transform;
            saw.SetParent(t, false);
            saw.localPosition = sawAt + new Vector3(0.035f, 0, 0);
            Workbench.Cyl(saw, k_Silver * 1.2f, Vector3.zero, 0.19f * s, 0.008f, new Vector3(0, 0, 90));
            for (int i = 0; i < 10; i++)
            {
                var r = Quaternion.Euler(i * 36f, 0, 0);
                Art.Box(saw, k_SilverDark, r * new Vector3(0, 0.19f * s, 0), new Vector3(0.012f, 0.035f, 0.035f), r.eulerAngles);
            }
            Workbench.Cyl(saw, m_Accent, new Vector3(0.006f, 0, 0), 0.05f, 0.012f, new Vector3(0, 0, 90), acc);
            m_Saws.Add(saw);
            if (level == 2)
            {
                // a second blade on the left, under the console
                var saw2 = new GameObject("saw").transform;
                saw2.SetParent(t, false);
                saw2.localPosition = new Vector3(-hx - 0.04f, baseTop + 0.16f, 0.3f);
                Workbench.Cyl(saw2, k_Silver * 1.2f, Vector3.zero, 0.12f, 0.008f, new Vector3(0, 0, 90));
                for (int i = 0; i < 8; i++)
                {
                    var r = Quaternion.Euler(i * 45f, 0, 0);
                    Art.Box(saw2, k_SilverDark, r * new Vector3(0, 0.12f, 0), new Vector3(0.012f, 0.03f, 0.03f), r.eulerAngles);
                }
                m_Saws.Add(saw2);
            }

            // ---- the column on top, with rings round it and an orb over it (like the alien machine) ----
            Workbench.Cyl(t, k_Metal, new Vector3(0, houseTop + 0.03f, cz), 0.3f * s, 0.06f);
            Workbench.Cyl(t, k_Silver, new Vector3(0, (houseTop + colTop) * 0.5f, cz), 0.17f * s, colTop - houseTop);
            Workbench.Cyl(t, k_Metal, new Vector3(0, colTop, cz), 0.25f * s, 0.05f);
            for (int i = 0; i <= level; i++) // glowing bands up the column
                Workbench.Cyl(t, m_Accent, new Vector3(0, houseTop + 0.14f * s + i * 0.12f * s, cz), 0.18f * s, 0.025f, default, acc);
            var ringAt = new Vector3(0, (houseTop + colTop) * 0.5f + 0.04f, cz);
            m_Rings.Add(Workbench.Ring(t, ringAt, new Vector3(15, 0, 8), 0.4f * s, 12, 0.16f * s, k_Silver, gTeam, 3));
            if (level >= 1) m_Rings.Add(Workbench.Ring(t, ringAt, new Vector3(-25, 0, -15), 0.46f * s, 12, 0.14f * s, k_SilverDark, acc, 2));
            if (level >= 2) m_Rings.Add(Workbench.Ring(t, ringAt, new Vector3(75, 0, 20), 0.53f * s, 16, 0.12f * s, k_Silver, gCyan, 2));
            if (level < 2)
            {
                // the floating orb: green, then yellow
                m_Core = Art.Part(t, Art.Ico, m_Accent, new Vector3(0, colTop + 0.3f, cz), Vector3.one * 0.13f * s, default, false, acc, "orb").transform;
                Art.Part(m_Core, Art.Ico, Color.white, Vector3.zero, Vector3.one * 1.5f, default, false, Art.Ghost(new Color(m_Accent.r, m_Accent.g, m_Accent.b, 0.3f)));
            }
            else
            {
                // a glass dome on the column with a pink energy core, and a crystal spinning over it
                var domeAt = new Vector3(0, colTop + 0.02f, cz);
                Workbench.Cyl(t, k_Silver, domeAt, 0.26f, 0.04f);
                m_Core = Art.Part(t, Art.Ico, k_Pink, domeAt + new Vector3(0, 0.15f, 0), Vector3.one * 0.1f, default, false, acc, "core").transform;
                Workbench.Ball(t, Color.white, domeAt + new Vector3(0, 0.06f, 0), new Vector3(0.24f, 0.26f, 0.24f), Art.Ghost(new Color(0.75f, 0.95f, 1f, 0.26f)));
                m_Crystal = new GameObject("crystal").transform;
                m_Crystal.SetParent(t, false);
                m_Crystal.localPosition = new Vector3(0, colTop + 0.75f, cz);
                Art.Part(m_Crystal, Art.Cone, k_Pink, Vector3.zero, new Vector3(0.22f, 0.32f, 0.22f), default, false, acc);
                Art.Part(m_Crystal, Art.Cone, k_Pink, Vector3.zero, new Vector3(0.22f, 0.22f, 0.22f), new Vector3(180f, 0, 0), false, acc);
                m_CrystalBase = m_Crystal.localPosition;
            }
            m_CoreBase = m_Core.localPosition;
            // level 1: a spinning yellow beacon on the front of the roof
            if (level == 1)
            {
                m_Beacon = new GameObject("beacon").transform;
                m_Beacon.SetParent(t, false);
                m_Beacon.localPosition = new Vector3(hx * 0.6f, houseTop + 0.05f, hz * 0.55f);
                Workbench.Cyl(m_Beacon, k_Metal, Vector3.zero, 0.07f, 0.04f);
                Workbench.Ball(m_Beacon, k_Yellow, new Vector3(0, 0.07f, 0), new Vector3(0.06f, 0.07f, 0.06f), gYellow);
                Art.Box(m_Beacon, k_Dark, new Vector3(0, 0.07f, 0), new Vector3(0.13f, 0.1f, 0.015f));
            }

            // ---- pylons with glowing tips (two; level 2: four tesla pylons standing on the ground, holding it up) ----
            if (level < 2)
            {
                for (int k = -1; k <= 1; k += 2)
                {
                    var p = new Vector3(k * (hx - 0.06f), houseTop, -0.05f - hz + 0.08f);
                    Art.Box(t, k_Metal, p + new Vector3(0, 0.3f * s, 0), new Vector3(0.1f, 0.6f * s, 0.1f), new Vector3(0, 0, -k * 7f));
                    Art.Part(t, Art.Ico, level == 1 ? k_Yellow : teamGlow, p + new Vector3(k * 0.04f, 0.64f * s, 0), Vector3.one * 0.06f, default, false, level == 1 ? gYellow : gTeam);
                    Art.Part(t, Art.Cone, k_Silver, p + new Vector3(k * 0.04f, 0.66f * s, 0), new Vector3(0.08f, 0.2f, 0.08f));
                }
            }
            else
            {
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var p = new Vector3(sx * (hx * 1.2f + 0.04f), 0, -0.05f + sz * (hz * 1.2f + 0.04f));
                        bool backPair = sz < 0;
                        float ph = backPair ? colTop - 0.1f : houseTop * 0.75f;
                        Workbench.Cyl(t, k_Metal, p + Vector3.up * 0.03f, 0.1f, 0.06f);
                        Workbench.Cyl(t, k_Silver, p + Vector3.up * ph * 0.5f, 0.045f, ph);
                        for (int i = 0; i < 3; i++)
                            Workbench.Cyl(t, i == 2 ? k_Pink : k_SilverDark, p + Vector3.up * (ph * 0.55f + i * 0.13f), 0.11f - i * 0.02f, 0.025f, default, i == 2 ? acc : null);
                        Art.Part(t, Art.Ico, k_Pink, p + Vector3.up * (ph + 0.06f), Vector3.one * 0.075f, default, false, acc);
                    }
                // two emitter arms off the column, aimed down at the chute
                for (int k = -1; k <= 1; k += 2)
                {
                    var a = new Vector3(k * 0.12f, colTop - 0.15f, cz);
                    var b = new Vector3(k * 0.36f, houseTop + 0.3f, hz + 0.2f);
                    Workbench.Rod(t, k_Metal, a, b, 0.03f);
                    Workbench.Ball(t, k_SilverDark, b, Vector3.one * 0.05f);
                    Art.Part(t, Art.Cone, k_Cyan, b + new Vector3(0, -0.02f, 0.02f), new Vector3(0.09f, 0.12f, 0.09f), new Vector3(140, 0, -k * 25f), false, gCyan);
                }
            }

            // ---- exhaust stacks on the back (level 1: two, level 2: four), puffing the level colour ----
            int vents = level == 0 ? 0 : level == 1 ? 2 : 4;
            for (int i = 0; i < vents; i++)
            {
                float vx = vents == 2 ? (i == 0 ? -0.26f : 0.26f) * s : (-0.36f + i * 0.24f) * s;
                var v = new GameObject("vent").transform;
                v.SetParent(t, false);
                v.localPosition = new Vector3(vx, houseTop, -0.05f - hz + 0.1f);
                float vh = (0.36f + (i % 2) * 0.1f) * s;
                Workbench.Cyl(v, k_SilverDark, new Vector3(0, vh * 0.5f, 0), 0.06f, vh);
                Workbench.Cyl(v, k_Metal, new Vector3(0, vh, 0), 0.075f, 0.04f);
                Workbench.Cyl(v, m_Accent, new Vector3(0, vh + 0.022f, 0), 0.06f, 0.01f, default, acc);
                m_Vents.Add(v);
            }

            // a light in the level colour, brighter with each level
            var lg = new GameObject("light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, houseTop + 0.35f, 0.55f);
            m_Light = lg.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.color = m_Accent;
            m_Light.range = 2.8f + level * 1.2f;
            m_LightBase = 0.8f + level * 0.3f;
            m_Light.intensity = m_LightBase;

            // level pips on the front: how far it's been upgraded
            for (int i = 0; i < Cfg.MaxWoodGen; i++)
                Art.Part(t, Art.Ico, i < level ? m_Accent : k_Metal, new Vector3(-0.1f + i * 0.2f, houseTop - 0.12f, hz - 0.02f), Vector3.one * 0.045f, default, false, i < level ? acc : null);

            // a log being sawn and pushed out of the port
            m_Log = Art.Part(t, Art.Cylinder, Art.Wood, portAt, new Vector3(0.14f, 0.2f, 0.14f), new Vector3(0, 0, 90)).transform;
            m_Log.gameObject.SetActive(false);
            if (AiPsxArt.On) AiPsxArt.Apply(t);
        }

        void Update()
        {
            int level = Level;
            if (level != m_Level)
            {
                bool upgrade = m_Level >= 0 && level > m_Level;
                Build(level);
                if (upgrade)
                {
                    m_Pop = 0f;
                    Fx.Play(FxKind.WeakSpot, transform.position + Vector3.up * 1.2f, Vector3.up);
                    Sfx.Play(Sfx.Ding, transform.position + Vector3.up, 1f);
                }
            }
            bool running = NetGame.Instance != null && NetGame.Instance.IsSpawned && (NetGame.Instance.S == GameState.PreBall || NetGame.Instance.S == GameState.BallLive);
            float time = Time.time, dt = Time.deltaTime;
            float pace = running ? 1f + m_Level * 0.6f : 0.35f;

            // pulsing glow and light
            float beat = 0.5f + 0.5f * Mathf.Sin(time * 2.6f * pace);
            if (m_GlowMat && m_GlowMat.HasProperty("_EmissionColor")) m_GlowMat.SetColor("_EmissionColor", m_Accent * (0.9f + 1.4f * beat));
            if (m_Light) m_Light.intensity = m_LightBase * (0.75f + 0.5f * beat);

            // the rings spin, faster with each level; the saws run while it works
            float spin = running ? 90f + m_Level * 110f : 20f;
            for (int i = 0; i < m_Rings.Count; i++)
                if (m_Rings[i]) m_Rings[i].Rotate(0, (i % 2 == 0 ? spin : -spin * 1.3f) * dt, 0, Space.Self);
            float saw = running ? 900f + m_Level * 400f : 60f;
            foreach (var sw in m_Saws) if (sw) sw.Rotate(saw * dt, 0, 0, Space.Self);
            if (m_Core)
            {
                m_Core.localPosition = m_CoreBase + Vector3.up * Mathf.Sin(time * 2f) * 0.04f;
                m_Core.Rotate(15f * dt, 50f * dt, 0, Space.Self);
            }
            if (m_Crystal)
            {
                m_Crystal.localPosition = m_CrystalBase + Vector3.up * Mathf.Sin(time * 1.5f) * 0.08f;
                m_Crystal.Rotate(0, 70f * dt, 0, Space.Self);
            }
            if (m_Beacon) m_Beacon.Rotate(0, (running ? 300f : 60f) * dt, 0, Space.Self);

            // the level colour puffing out of the exhausts
            if (running && m_Vents.Count > 0 && time > m_NextPuff)
            {
                m_NextPuff = time + 0.45f / m_Vents.Count;
                var v = m_Vents[Random.Range(0, m_Vents.Count)];
                if (v) FxParticle.Puff(v.position + v.up * 0.5f, new Color(m_Accent.r, m_Accent.g, m_Accent.b, 0.45f), Random.Range(0.18f, 0.32f));
            }

            // a log comes out of the port and slides down the chute onto the pile about once a second
            if (running && time > m_NextLog && m_Log != null)
            {
                m_NextLog = time + 1f;
                m_LogT = 0f;
                if (m_Saws.Count > 0 && m_Saws[0])
                    for (int i = 0; i < 3; i++)
                        FxParticle.Spawn(m_Saws[0].position, (transform.right + Random.insideUnitSphere * 0.6f) * 1.2f + Vector3.up, new Color(0.86f, 0.72f, 0.5f), 0.03f, 0.5f, 6f, false); // sawdust
            }
            if (m_Log != null)
            {
                m_LogT = Mathf.Min(1f, m_LogT + dt * 2.2f);
                m_Log.gameObject.SetActive(m_LogT < 1f);
                float grow = Mathf.Clamp01(m_LogT * 4f);
                m_Log.localScale = new Vector3(0.14f, 0.2f, 0.14f) * Mathf.Lerp(0.2f, 1f, grow);
                float baseTop = m_Level == 2 ? 0.42f : 0.3f;
                m_Log.localPosition = Vector3.Lerp(new Vector3(0, baseTop + 0.2f, 0.4f), new Vector3(0, 0.25f, 1.0f), Mathf.Clamp01((m_LogT - 0.2f) / 0.8f));
            }

            // squash and stretch when it's just been upgraded
            if (m_Pop < 1f && m_Visual)
            {
                m_Pop = Mathf.Min(1f, m_Pop + dt * 2.5f);
                float b = Mathf.Sin(m_Pop * Mathf.PI * 3f) * (1f - m_Pop) * 0.35f;
                m_Visual.localScale = new Vector3(1f - b * 0.5f, 1f + b, 1f - b * 0.5f);
            }
            else if (m_Visual) m_Visual.localScale = Vector3.one;
        }
    }
}
