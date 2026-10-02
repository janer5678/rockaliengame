using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Auto Wood: the wood machine on the bedrock, to the right of the alien machine. Futuristic tech in the alien
    /// machine's style (a silver housing with a flat lid, a console with a screen, a saw blade) that saws wood out of
    /// nothing and pushes it out of a lit port: each log slides down the chute, drops off the end and lands on the pile
    /// out in front (Cfg.WoodTrayLocal; the pile itself is a world item, NetGame.ServerTickAutoWood). Nothing stands on
    /// top of it (no column, rings, orb or pylons). It changes in front of you when your team upgrades its wood gen:
    /// level 0 - silver like the alien machine: green trim round the lid, a team-colour and a green lens, a saw blade on the side;
    /// level 1 - bigger and YELLOW: yellow light strips on every edge and round the base, hazard stripes on the lid and the
    ///           chute, two exhaust vents on the back puffing yellow;
    /// level 2 - bigger again and PINK: hovering over a pink glow on four short tesla legs, pink seams down its sides, a pink
    ///           diamond and studs on the lid, a second saw, four vents puffing pink.
    /// </summary>
    public class WoodMachine : MonoBehaviour
    {
        int m_Team, m_Level = -1;
        Transform m_Visual;
        readonly List<Transform> m_Vents = new List<Transform>();
        readonly List<Transform> m_Saws = new List<Transform>();
        Transform m_Log;
        Vector3 m_ChuteA, m_ChuteB;
        Light m_Light;
        Material m_GlowMat;
        Color m_Accent;
        float m_NextPuff, m_Pop = 1f, m_LogT = 1f, m_NextLog, m_LightBase;

        // the alien machine's palette (MapBuilder.BuildMachine) and the level colours
        static readonly Color k_Metal = new Color(0.3f, 0.33f, 0.36f), k_Silver = new Color(0.72f, 0.74f, 0.78f), k_SilverDark = new Color(0.5f, 0.52f, 0.56f);
        static readonly Color k_Dark = new Color(0.08f, 0.09f, 0.11f);
        static readonly Color k_Green = new Color(0.45f, 0.95f, 0.55f), k_Yellow = new Color(1f, 0.82f, 0.15f), k_Pink = new Color(1f, 0.32f, 0.78f);

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
            m_Vents.Clear();
            m_Saws.Clear();
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
            var screen = Art.Ghost(level == 1 ? new Color(1f, 0.9f, 0.4f, 0.7f) : level == 2 ? new Color(1f, 0.5f, 0.9f, 0.7f) : new Color(0.4f, 1f, 0.8f, 0.7f));
            float s = level == 2 ? 1.16f : level == 1 ? 1.08f : 1f;
            float hx = 0.48f * s, hz = 0.4f * s;           // the housing's half size
            float baseTop = level == 2 ? 0.42f : 0.3f;     // (level 2 hovers on its legs)
            float houseTop = baseTop + 0.78f * s;

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
            // the chute out to the front (hazard stripes from level 1): its end is held up off the ground on a strut, so the
            // logs drop off it onto the pile out in front (Cfg.WoodTrayLocal)
            var chuteA = new Vector3(0, baseTop + 0.16f, hz);
            var chuteB = new Vector3(0, 0.36f, 1.0f);
            m_ChuteA = chuteA;
            m_ChuteB = chuteB;
            var cd = chuteB - chuteA;
            var cRot = Quaternion.LookRotation(cd.normalized).eulerAngles;
            var chuteMid = (chuteA + chuteB) * 0.5f;
            Art.Box(t, k_Metal, chuteMid, new Vector3(0.38f, 0.03f, cd.magnitude), cRot);
            for (int k = -1; k <= 1; k += 2)
                Art.Box(t, k_Silver, chuteMid + new Vector3(k * 0.2f, 0.04f, 0), new Vector3(0.025f, 0.08f, cd.magnitude), cRot);
            Art.Box(t, k_SilverDark, chuteB + new Vector3(0, 0.02f, -0.01f), new Vector3(0.42f, 0.03f, 0.04f)); // (a lip on the end)
            for (int k = -1; k <= 1; k += 2)
                Art.Box(t, k_Metal, new Vector3(k * 0.15f, chuteB.y * 0.5f - 0.01f, chuteB.z - 0.06f), new Vector3(0.04f, chuteB.y, 0.04f));
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

            // ---- the top: a finished lid - nothing stands on it any more (no column, rings, orb, beacon or pylons) ----
            // a metal lid with a silver panel set into it, outlined in the level colour, flat louvres at the back
            float capY = houseTop + 0.03f;
            Art.Box(t, k_Metal, new Vector3(0, capY, -0.05f), new Vector3(hx * 1.94f, 0.06f, hz * 1.94f));
            Art.Box(t, k_Silver, new Vector3(0, capY + 0.035f, -0.05f), new Vector3(hx * 1.7f, 0.012f, hz * 1.7f));
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, m_Accent, new Vector3(0, capY + 0.043f, -0.05f + k * hz * 0.86f), new Vector3(hx * 1.74f, 0.008f, 0.025f), default, false, acc);
                Art.Box(t, m_Accent, new Vector3(k * hx * 0.86f, capY + 0.043f, -0.05f), new Vector3(0.025f, 0.008f, hz * 1.74f), default, false, acc);
            }
            for (int i = 0; i < 4; i++)
                Art.Box(t, k_Dark, new Vector3(0, capY + 0.044f, -0.05f - hz * 0.6f + i * 0.07f * s), new Vector3(hx * 1.2f, 0.008f, 0.03f));
            if (level == 0)
            {
                // two status lenses flush with the lid: the team colour and the alien machine's green
                Workbench.Cyl(t, teamGlow, new Vector3(-hx * 0.45f, capY + 0.045f, hz * 0.4f), 0.05f, 0.012f, default, gTeam);
                Workbench.Cyl(t, k_Green, new Vector3(hx * 0.45f, capY + 0.045f, hz * 0.4f), 0.05f, 0.012f, default, gGreen);
            }
            else if (level == 1)
            {
                // yellow and black hazard stripes along the front of the lid
                for (int i = 0; i < 7; i++)
                    Art.Box(t, i % 2 == 0 ? k_Yellow : k_Dark, new Vector3((i - 3) * hx * 0.24f, capY + 0.046f, hz * 0.45f), new Vector3(hx * 0.2f, 0.008f, 0.1f), new Vector3(0, 30f, 0), false, i % 2 == 0 ? gYellow : null);
            }
            else
            {
                // a pink diamond set into the lid and four glowing studs on its corners
                Art.Box(t, k_Pink, new Vector3(0, capY + 0.046f, -0.05f), new Vector3(hx * 0.5f, 0.008f, hx * 0.5f), new Vector3(0, 45f, 0), false, acc);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Art.Part(t, Art.Ico, k_Pink, new Vector3(sx * hx * 0.97f, capY + 0.03f, -0.05f + sz * hz * 0.97f), Vector3.one * 0.045f, default, false, acc);
            }

            // level 2: four short tesla legs on the ground holding it up (they stop below the lid)
            if (level == 2)
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var p = new Vector3(sx * (hx * 1.2f + 0.04f), 0, -0.05f + sz * (hz * 1.2f + 0.04f));
                        float ph = houseTop * 0.75f;
                        Workbench.Cyl(t, k_Metal, p + Vector3.up * 0.03f, 0.1f, 0.06f);
                        Workbench.Cyl(t, k_Silver, p + Vector3.up * ph * 0.5f, 0.045f, ph);
                        for (int i = 0; i < 3; i++)
                            Workbench.Cyl(t, i == 2 ? k_Pink : k_SilverDark, p + Vector3.up * (ph * 0.55f + i * 0.13f), 0.11f - i * 0.02f, 0.025f, default, i == 2 ? acc : null);
                        Art.Part(t, Art.Ico, k_Pink, p + Vector3.up * (ph + 0.06f), Vector3.one * 0.075f, default, false, acc);
                    }

            // ---- exhaust vents low on the back (level 1: two, level 2: four), puffing the level colour out backwards ----
            int vents = level == 0 ? 0 : level == 1 ? 2 : 4;
            for (int i = 0; i < vents; i++)
            {
                float vx = vents == 2 ? (i == 0 ? -0.24f : 0.24f) * s : (-0.33f + i * 0.22f) * s;
                var v = new GameObject("vent").transform;
                v.SetParent(t, false);
                v.localPosition = new Vector3(vx, baseTop + hh * 0.45f, -0.05f - hz - 0.01f);
                v.localRotation = Quaternion.LookRotation(Vector3.back); // (its forward points out of the back)
                Art.Box(v, k_Metal, Vector3.zero, new Vector3(0.16f, 0.22f, 0.04f));
                for (int j = 0; j < 3; j++) Art.Box(v, k_Dark, new Vector3(0, -0.06f + j * 0.06f, 0.022f), new Vector3(0.12f, 0.025f, 0.01f));
                Art.Box(v, m_Accent, new Vector3(0, 0.12f, 0.02f), new Vector3(0.16f, 0.015f, 0.012f), default, false, acc);
                m_Vents.Add(v);
            }

            // a light in the level colour, brighter with each level
            var lg = new GameObject("light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, houseTop + 0.25f, 0.55f);
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

            // the saws run while it works
            float saw = running ? 900f + m_Level * 400f : 60f;
            foreach (var sw in m_Saws) if (sw) sw.Rotate(saw * dt, 0, 0, Space.Self);

            // the level colour puffing out of the vents on the back
            if (running && m_Vents.Count > 0 && time > m_NextPuff)
            {
                m_NextPuff = time + 0.45f / m_Vents.Count;
                var v = m_Vents[Random.Range(0, m_Vents.Count)];
                if (v) FxParticle.Puff(v.position + v.forward * 0.18f + Vector3.up * 0.05f, new Color(m_Accent.r, m_Accent.g, m_Accent.b, 0.45f), Random.Range(0.18f, 0.32f));
            }

            // a log comes out of the port about once a second, rolls down the chute, drops off the end and lands on the pile out in front
            if (running && time > m_NextLog && m_Log != null)
            {
                m_NextLog = time + 1f;
                m_LogT = 0f;
                m_Landed = false;
                if (m_Saws.Count > 0 && m_Saws[0])
                    for (int i = 0; i < 3; i++)
                        FxParticle.Spawn(m_Saws[0].position, (transform.right + Random.insideUnitSphere * 0.6f) * 1.2f + Vector3.up, new Color(0.86f, 0.72f, 0.5f), 0.03f, 0.5f, 6f, false); // sawdust
            }
            if (m_Log != null) AnimateLog(dt);

            // squash and stretch when it's just been upgraded
            if (m_Pop < 1f && m_Visual)
            {
                m_Pop = Mathf.Min(1f, m_Pop + dt * 2.5f);
                float b = Mathf.Sin(m_Pop * Mathf.PI * 3f) * (1f - m_Pop) * 0.35f;
                m_Visual.localScale = new Vector3(1f - b * 0.5f, 1f + b, 1f - b * 0.5f);
            }
            else if (m_Visual) m_Visual.localScale = Vector3.one;
        }

        bool m_Landed;

        /// <summary>Where the log is in its trip (0 just out of the port, 1 landed; the test photographs it falling).</summary>
        public float LogPhase => m_LogT;
        /// <summary>The trip's phases: out of the port, rolling down the chute, falling off its end.</summary>
        public const float LogOut = 0.2f, LogDrop = 0.55f;
        /// <summary>Where the logs land, in the machine's space: on the pile (Cfg.WoodTrayLocal), not inside the machine.</summary>
        public static Vector3 LandingLocal => new Vector3(Cfg.WoodTrayLocal.x, 0.14f, Cfg.WoodTrayLocal.z);

        void AnimateLog(float dt)
        {
            m_LogT = Mathf.Min(1f, m_LogT + dt * 1.25f);
            m_Log.gameObject.SetActive(m_LogT < 1f);
            m_Log.localScale = new Vector3(0.14f, 0.2f, 0.14f) * Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(m_LogT / LogOut));
            var lift = Vector3.up * 0.1f; // (it rolls on top of the chute)
            Vector3 p;
            if (m_LogT < LogOut) p = Vector3.Lerp(m_ChuteA + new Vector3(0, 0.1f, -0.25f), m_ChuteA + lift, m_LogT / LogOut);
            else if (m_LogT < LogDrop)
            {
                float u = (m_LogT - LogOut) / (LogDrop - LogOut);
                p = Vector3.Lerp(m_ChuteA, m_ChuteB, u * u * 0.6f + u * 0.4f) + lift; // (speeding up down the slope)
            }
            else
            {
                // off the end: it keeps going forward and falls onto the pile
                float u = (m_LogT - LogDrop) / (1f - LogDrop);
                var from = m_ChuteB + lift;
                var to = LandingLocal;
                p = new Vector3(Mathf.Lerp(from.x, to.x, u), from.y + (to.y - from.y) * (0.2f * u + 0.8f * u * u), Mathf.Lerp(from.z, to.z, u));
                if (u >= 0.97f && !m_Landed)
                {
                    m_Landed = true;
                    var at = transform.TransformPoint(to);
                    for (int i = 0; i < 3; i++)
                        FxParticle.Spawn(at, (Random.insideUnitSphere + Vector3.up * 1.5f) * 0.9f, new Color(0.62f, 0.44f, 0.25f), 0.035f, 0.45f, 9f, false); // chips off the pile
                }
            }
            m_Log.localPosition = p;
            m_Log.localRotation = Quaternion.Euler(m_LogT * 540f, 0, 0) * Quaternion.Euler(0, 0, 90); // (rolling forwards)
        }
    }
}
