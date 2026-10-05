using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The UPGRADE STATION: a terminal on the bedrock to the LEFT of the alien machine (as you look at it from your spawn),
    /// in every base in the modes that have base upgrades (Arsenal, Auto Wood - Cfg.HasBaseUpgrades). E on your own one
    /// opens the UPGRADES screen (Hud.Upgrades.cs; Fortify All Walls, the Wood Gen). It's built in the wood machine's style
    /// (WoodMachine.cs): a silver housing with orange-yellow trim on a metal plinth, a slanted keypad, a flat lid with a
    /// team-colour and an orange lens - and on its front a big screen with a glowing ORANGE-YELLOW UP ARROW (not a plus:
    /// that read as healing), with a small holographic arrow turning over the lid, so you can tell from across the base
    /// that it's where upgrades are bought.
    /// When anyone on the team buys an upgrade, every client plays it (PlayerNet.UpgradeFxRpc -> Celebrate): orange up
    /// arrows burst out of it and float up, the screen flashes, and the whole machine bounces (squash and stretch).
    /// Local scenery like the wood machine (built by MapBuilder.BuildBedrock); its body has a solid collider so chests and
    /// benches can't be put down inside it. It stands on the bedrock, where nothing can be built anyway.
    /// </summary>
    public class UpgradeStation : MonoBehaviour
    {
        public static readonly UpgradeStation[] ByTeam = new UpgradeStation[4];

        /// <summary>Test hook: how many upgrade celebrations this peer has played (any team).</summary>
        public static int Celebrations;
        /// <summary>Test hook: celebrations this peer has played, per team.</summary>
        public static readonly int[] CelebrationsOf = new int[4];

        public int Team { get; private set; }
        Transform m_Visual, m_Holo;
        Material m_ScreenMat, m_PlusMat, m_HoloMat, m_FlashMat;
        Light m_Light;
        GameObject m_FlashPanel;
        float m_Pop = 1f, m_Flash, m_LightBase = 0.6f;

        static readonly Color k_Metal = new Color(0.3f, 0.33f, 0.36f), k_Silver = new Color(0.72f, 0.74f, 0.78f), k_SilverDark = new Color(0.5f, 0.52f, 0.56f);
        static readonly Color k_Dark = new Color(0.08f, 0.09f, 0.11f);
        /// <summary>The station's colour: an orangey yellow (it was green, which read as healing). The name is from then.</summary>
        public static readonly Color Green = new Color(1f, 0.7f, 0.16f);
        static readonly Color k_ScreenBase = new Color(0.24f, 0.13f, 0.03f, 0.92f);

        /// <summary>The body's half size (what you bump into), for the layout checks.</summary>
        public const float HalfX = 0.55f, HalfZ = 0.5f;

        public static UpgradeStation Create(Transform root, int team)
        {
            var go = new GameObject("UpgradeStation " + Cfg.TeamName[team]);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(Cfg.UpgradeStationPos(team), Cfg.UpgradeStationRot(team));
            var s = go.AddComponent<UpgradeStation>();
            s.Team = team;
            ByTeam[team] = s;
            s.Build();
            return s;
        }

        void OnDestroy()
        {
            if (ByTeam[Team] == this) ByTeam[Team] = null;
            if (m_ScreenMat) Destroy(m_ScreenMat);
            if (m_PlusMat) Destroy(m_PlusMat);
            if (m_HoloMat) Destroy(m_HoloMat);
            if (m_FlashMat) Destroy(m_FlashMat);
        }

        static Material Emissive(Color c, float k)
        {
            var m = new Material(Art.Mat(c));
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * k);
            }
            Art.Register(m, c);
            return m;
        }

        /// <summary>An up arrow (the "upgrade" sign, so it isn't mistaken for a healing plus): a shaft under a stepped
        /// arrowhead, `size` tall and 0.9 x `size` wide, centred on the parent, no shadows.</summary>
        public static void BuildArrow(Transform parent, Color c, Material mat, float size, float depth)
        {
            void Bar(float y, float w, float h)
                => Art.Box(parent, c, new Vector3(0, y * size, 0), new Vector3(w * size, h * size, depth), default, false, mat)
                    .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Bar(-0.24f, 0.3f, 0.52f); // the shaft
            for (int i = 0; i < 4; i++) Bar(0.075f + i * 0.125f, 0.9f - i * 0.225f, 0.13f); // the head, narrowing to the tip
        }

        void Build()
        {
            using var tint = ColorSlots.Use(ColorSlots.WoodMachine); // (the wood machine's colour slot: Settings > Display colours)
            m_Visual = new GameObject("visual").transform;
            m_Visual.SetParent(transform, false);
            var t = m_Visual;
            var teamGlow = Color.Lerp(Cfg.TeamColor[Mathf.Clamp(Team, 0, 3)], Color.white, 0.35f);
            var gTeam = Workbench.Glow(teamGlow);
            var gGreen = Workbench.Glow(Green);
            m_PlusMat = Emissive(Green, 2f);
            m_ScreenMat = new Material(Art.Ghost(k_ScreenBase));
            m_ScreenMat.SetColor("_BaseColor", k_ScreenBase);
            m_HoloMat = new Material(Art.Ghost(new Color(1f, 0.74f, 0.22f, 0.6f)));
            m_FlashMat = new Material(Art.Ghost(new Color(1f, 0.93f, 0.75f, 0f)));

            const float hx = 0.44f, hz = 0.36f, baseTop = 0.26f, houseTop = 1.36f;
            float hy = (baseTop + houseTop) * 0.5f, hh = houseTop - baseTop;

            // what you bump into (and what chests / benches can't overlap)
            var body = new GameObject("body");
            body.transform.SetParent(t, false);
            var bc = body.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, houseTop * 0.5f, -0.02f);
            bc.size = new Vector3(HalfX * 2f, houseTop, HalfZ * 2f);

            // ---- the plinth ----
            Art.Box(t, k_Metal, new Vector3(0, baseTop * 0.5f, -0.02f), new Vector3(hx * 2.4f, baseTop, hz * 2.6f));
            Art.Box(t, k_SilverDark, new Vector3(0, baseTop + 0.015f, -0.02f), new Vector3(hx * 2.25f, 0.03f, hz * 2.45f));
            for (int k = -1; k <= 1; k += 2) // green light strips round the plinth
            {
                Art.Box(t, Green, new Vector3(0, baseTop * 0.55f, -0.02f + k * hz * 1.31f), new Vector3(hx * 2.3f, 0.025f, 0.012f), default, false, gGreen);
                Art.Box(t, Green, new Vector3(k * hx * 1.21f, baseTop * 0.55f, -0.02f), new Vector3(0.012f, 0.025f, hz * 2.5f), default, false, gGreen);
            }

            // ---- the housing ----
            Art.Box(t, k_Silver, new Vector3(0, hy, -0.05f), new Vector3(hx * 2f, hh, hz * 2f));
            Art.Box(t, k_SilverDark, new Vector3(0, houseTop - 0.02f, -0.05f), new Vector3(hx * 2.06f, 0.05f, hz * 2.06f));
            // green strips up the front corners
            for (int sx = -1; sx <= 1; sx += 2)
                Art.Box(t, Green, new Vector3(sx * (hx + 0.005f), hy, -0.05f + hz + 0.005f), new Vector3(0.025f, hh * 0.9f, 0.025f), default, false, gGreen);
            // side grilles
            for (int sx = -1; sx <= 1; sx += 2)
                for (int i = 0; i < 4; i++)
                    Art.Box(t, k_Metal, new Vector3(sx * (hx + 0.006f), baseTop + 0.2f + i * 0.08f, -0.05f), new Vector3(0.012f, 0.025f, hz * 1.3f));

            // ---- the screen: a dark bezel, a green-black glass face, and a big glowing green plus ----
            float sy = baseTop + hh * 0.66f, sz = hz - 0.05f + 0.012f;
            Art.Box(t, k_Dark, new Vector3(0, sy, sz), new Vector3(hx * 1.75f, 0.6f, 0.025f));
            Art.Box(t, k_SilverDark, new Vector3(0, sy + 0.31f, sz + 0.004f), new Vector3(hx * 1.8f, 0.025f, 0.03f));
            Art.Box(t, k_SilverDark, new Vector3(0, sy - 0.31f, sz + 0.004f), new Vector3(hx * 1.8f, 0.025f, 0.03f));
            var glass = Art.Box(t, Color.white, new Vector3(0, sy, sz + 0.015f), new Vector3(hx * 1.6f, 0.52f, 0.006f), default, false, m_ScreenMat);
            glass.name = "screen";
            var plus = new GameObject("plus").transform;
            plus.SetParent(t, false);
            plus.localPosition = new Vector3(0, sy, sz + 0.022f);
            BuildArrow(plus, Green, m_PlusMat, 0.42f, 0.01f);
            // little corner ticks on the screen, like a HUD
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy2 = -1; sy2 <= 1; sy2 += 2)
                {
                    var c = new Vector3(sx * hx * 0.72f, sy + sy2 * 0.2f, sz + 0.02f);
                    Art.Box(t, Green, c + new Vector3(-sx * 0.03f, 0, 0), new Vector3(0.06f, 0.012f, 0.006f), default, false, gGreen);
                    Art.Box(t, Green, c + new Vector3(0, -sy2 * 0.03f, 0), new Vector3(0.012f, 0.06f, 0.006f), default, false, gGreen);
                }
            // a white flash panel over the glass (only seen when an upgrade is bought)
            m_FlashPanel = Art.Box(t, Color.white, new Vector3(0, sy, sz + 0.03f), new Vector3(hx * 1.6f, 0.52f, 0.004f), default, false, m_FlashMat);
            m_FlashPanel.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_FlashPanel.SetActive(false);

            // ---- a slanted keypad under the screen ----
            var kp = new Vector3(0, baseTop + hh * 0.26f, hz - 0.05f + 0.12f);
            Art.Box(t, k_Metal, kp + new Vector3(0, -0.08f, -0.05f), new Vector3(hx * 1.5f, 0.16f, 0.16f));
            Art.Box(t, k_Metal, kp, new Vector3(hx * 1.6f, 0.04f, 0.26f), new Vector3(30, 0, 0));
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 2; j++)
                {
                    bool lit = (i + j) % 3 == 0;
                    var kpos = kp + Quaternion.Euler(30, 0, 0) * new Vector3((i - 1.5f) * 0.14f, 0.025f, (j - 0.5f) * 0.1f);
                    Art.Box(t, lit ? Green : k_SilverDark, kpos, new Vector3(0.09f, 0.02f, 0.06f), new Vector3(30, 0, 0), false, lit ? gGreen : null);
                }

            // ---- the lid: green trim, two lenses, and a small emitter with a holographic plus turning over it ----
            float capY = houseTop + 0.03f;
            Art.Box(t, k_Metal, new Vector3(0, capY, -0.05f), new Vector3(hx * 1.94f, 0.06f, hz * 1.94f));
            Art.Box(t, k_Silver, new Vector3(0, capY + 0.035f, -0.05f), new Vector3(hx * 1.7f, 0.012f, hz * 1.7f));
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, Green, new Vector3(0, capY + 0.043f, -0.05f + k * hz * 0.86f), new Vector3(hx * 1.74f, 0.008f, 0.025f), default, false, gGreen);
                Art.Box(t, Green, new Vector3(k * hx * 0.86f, capY + 0.043f, -0.05f), new Vector3(0.025f, 0.008f, hz * 1.74f), default, false, gGreen);
            }
            Workbench.Cyl(t, teamGlow, new Vector3(-hx * 0.55f, capY + 0.045f, hz * 0.45f), 0.045f, 0.012f, default, gTeam);
            Workbench.Cyl(t, Green, new Vector3(hx * 0.55f, capY + 0.045f, hz * 0.45f), 0.045f, 0.012f, default, gGreen);
            Workbench.Cyl(t, k_Metal, new Vector3(0, capY + 0.07f, -0.08f), 0.13f, 0.06f);
            Workbench.Cyl(t, Green, new Vector3(0, capY + 0.105f, -0.08f), 0.09f, 0.012f, default, gGreen);
            m_Holo = new GameObject("holo").transform;
            m_Holo.SetParent(t, false);
            m_Holo.localPosition = new Vector3(0, capY + 0.38f, -0.08f);
            BuildArrow(m_Holo, Color.white, m_HoloMat, 0.34f, 0.04f);
            // a faint beam from the emitter up to it
            var beam = Workbench.Cyl(t, Color.white, new Vector3(0, capY + 0.24f, -0.08f), 0.07f, 0.26f, default, Art.Ghost(new Color(1f, 0.74f, 0.22f, 0.12f)));
            beam.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // a green light in front of the screen
            var lg = new GameObject("light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, sy, 0.7f);
            m_Light = lg.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.color = Green;
            m_Light.range = 2.6f;
            m_Light.intensity = m_LightBase;
            if (AiPsxArt.On) AiPsxArt.Apply(t);
        }

        /// <summary>Everyone: an upgrade was bought at this team's station (PlayerNet.UpgradeFxRpc).</summary>
        public static void Celebrate(int team)
        {
            Celebrations++;
            if (team >= 0 && team < CelebrationsOf.Length) CelebrationsOf[team]++;
            var s = team >= 0 && team < ByTeam.Length ? ByTeam[team] : null;
            if (s == null) return;
            s.m_Pop = 0f;
            s.m_Flash = 1f;
            var top = s.transform.position + Vector3.up * 1.2f;
            Sfx.Play(Sfx.Unlock, top, 0.9f, 0f);
            Sfx.Play(Sfx.Ding, top, 0.6f);
            // a burst of green plus signs out of the screen and the lid, floating up
            for (int i = 0; i < 18; i++)
            {
                var from = s.transform.TransformPoint(new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.7f, 1.5f), Random.Range(0f, 0.4f)));
                var vel = s.transform.TransformDirection(new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(1.6f, 3.2f), Random.Range(0.3f, 1.6f)));
                UpgradeArrowParticle.Spawn(from, vel, Random.Range(0.12f, 0.24f), Random.Range(1.1f, 1.8f));
            }
            for (int i = 0; i < 6; i++)
                FxParticle.Puff(top + Random.insideUnitSphere * 0.4f, new Color(1f, 0.76f, 0.3f, 0.45f), Random.Range(0.3f, 0.55f));
        }

        void Update()
        {
            float time = Time.time, dt = Time.deltaTime;
            // the hologram turns and bobs
            if (m_Holo)
            {
                m_Holo.localRotation = Quaternion.Euler(0, time * 70f, 0);
                m_Holo.localPosition = new Vector3(m_Holo.localPosition.x, 1.77f + Mathf.Sin(time * 2f) * 0.04f, m_Holo.localPosition.z);
            }
            // the plus pulses softly; after a purchase the screen flashes white-green and fades back
            m_Flash = Mathf.Max(0f, m_Flash - dt * 1.6f);
            float beat = 0.5f + 0.5f * Mathf.Sin(time * 2.4f);
            float f = m_Flash * m_Flash;
            if (m_PlusMat && m_PlusMat.HasProperty("_EmissionColor")) m_PlusMat.SetColor("_EmissionColor", Green * (1.4f + 0.8f * beat + 6f * f));
            if (m_ScreenMat) m_ScreenMat.SetColor("_BaseColor", Color.Lerp(k_ScreenBase, new Color(1f, 0.86f, 0.5f, 0.95f), f));
            if (m_FlashPanel)
            {
                bool on = m_Flash > 0.01f;
                if (m_FlashPanel.activeSelf != on) m_FlashPanel.SetActive(on);
                if (on && m_FlashMat) m_FlashMat.SetColor("_BaseColor", new Color(1f, 0.94f, 0.8f, 0.85f * f));
            }
            if (m_Light) m_Light.intensity = m_LightBase * (0.85f + 0.3f * beat) + 4f * f;
            // squash and stretch: a quick squash down, then springy bounces that settle
            if (m_Pop < 1f && m_Visual)
            {
                m_Pop = Mathf.Min(1f, m_Pop + dt * 1.6f);
                float b = Mathf.Sin(m_Pop * Mathf.PI * 4f - 0.6f) * (1f - m_Pop) * 0.4f;
                m_Visual.localScale = new Vector3(1f - b * 0.55f, 1f + b, 1f - b * 0.55f);
            }
            else if (m_Visual && m_Visual.localScale != Vector3.one) m_Visual.localScale = Vector3.one;
        }

        /// <summary>Test hook: is it bouncing / flashing right now?</summary>
        public bool Animating => m_Pop < 1f || m_Flash > 0f;
    }

    /// <summary>A glowing orange up arrow that floats up, spins and shrinks away (UpgradeStation.Celebrate).</summary>
    public class UpgradeArrowParticle : MonoBehaviour
    {
        static Material s_Mat;
        static int s_Alive;
        Vector3 m_Vel;
        float m_Life, m_Max, m_Size, m_Spin;

        public static void Spawn(Vector3 pos, Vector3 vel, float size, float life)
        {
            if (s_Alive > 120) return;
            if (!s_Mat) s_Mat = Workbench.Glow(UpgradeStation.Green, 3f);
            var go = new GameObject("plusFx");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.01f;
            UpgradeStation.BuildArrow(go.transform, UpgradeStation.Green, s_Mat, 1f, 0.3f);
            var p = go.AddComponent<UpgradeArrowParticle>();
            p.m_Vel = vel; p.m_Life = p.m_Max = life; p.m_Size = size; p.m_Spin = Random.Range(-240f, 240f);
            go.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            s_Alive++;
        }

        void OnDestroy() => s_Alive--;

        void Update()
        {
            float dt = Time.deltaTime;
            m_Life -= dt;
            if (m_Life <= 0f) { Destroy(gameObject); return; }
            float age = 1f - m_Life / m_Max;
            m_Vel *= Mathf.Exp(-2.2f * dt);        // (drag)
            m_Vel += Vector3.up * 0.9f * dt;       // (they float up)
            transform.position += m_Vel * dt;
            transform.Rotate(0, m_Spin * dt, 0, Space.World);
            // pop in big, then shrink away
            float s = age < 0.15f ? Mathf.Lerp(0.2f, 1.25f, age / 0.15f) : Mathf.Lerp(1.25f, 0f, (age - 0.15f) / 0.85f);
            transform.localScale = Vector3.one * m_Size * Mathf.Max(0f, s);
        }
    }
}
