using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The alien machine in the game modes (NetGame.GameModes.cs):
    /// - Bedwars: the ball's socket gives way to a CRYOCHAMBER (a frosted glass pod with a sleeper's glow and cold mist)
    ///   that the team respawns from; the machine shows its damage in three steps - cracked and sparking, then smoking
    ///   with its rings knocked askew, then (destroyed) blown apart into a burning wreck.
    /// - Assassin: the skulls the team has handed in sit round the machine's base, one by one as they come in (in team
    ///   games their names show over them: Hud).
    /// </summary>
    public partial class Machine
    {
        GameObject m_Cryo, m_Cracks, m_Wreck, m_Sleeper;
        Transform m_DoorL, m_DoorR;
        float m_ReleaseAt = -99f;
        Transform m_SkullRoot;
        int m_Stage = -1, m_SkullCount = -1;
        float m_NextPuff;
        readonly List<Renderer> m_Body = new List<Renderer>();

        /// <summary>Assassin: where this machine's skulls are and whose they are (for the name tags).</summary>
        public readonly List<(Vector3 pos, int data)> SkullSpots = new List<(Vector3, int)>();

        void TickModes()
        {
            var g = NetGame.Instance;
            if (g == null || !g.IsSpawned) return;
            if (Cfg.Bedwars) TickBedwars(g);
            if (Cfg.Assassin) TickSkulls(g);
        }

        void TickBedwars(NetGame g)
        {
            var socket = transform.Find("socket");
            if (socket != null && socket.gameObject.activeSelf) socket.gameObject.SetActive(false);
            if (m_Cryo == null) BuildCryo();
            int stage = g.HitsOn(Team);
            if (stage != m_Stage)
            {
                m_Stage = stage;
                ShowStage(stage);
            }
            TickRelease();
            // the cryochamber's cold mist, and the damage: sparks, smoke, then fire
            if (Time.time >= m_NextPuff)
            {
                m_NextPuff = Time.time + (stage >= Cfg.MachineHitsToBreak ? 0.12f : 0.35f);
                var at = transform.position;
                if (stage < Cfg.MachineHitsToBreak)
                    FxParticle.Puff(Cfg.SocketPos(Team) + new Vector3(Random.Range(-0.4f, 0.4f), -0.4f, Random.Range(-0.4f, 0.4f)), new Color(0.85f, 0.95f, 1f, 0.35f), Random.Range(0.3f, 0.6f));
                if (stage == 1 && Random.value < 0.4f) FxParticle.Puff(at + Vector3.up * Random.Range(1f, 2.6f) + Random.insideUnitSphere * 0.5f, new Color(1f, 0.85f, 0.3f, 0.9f), 0.12f);
                if (stage == 2) FxParticle.Puff(at + Vector3.up * Random.Range(1.5f, 3f) + Random.insideUnitSphere * 0.4f, new Color(0.15f, 0.15f, 0.15f, 0.6f), Random.Range(0.5f, 1f));
                if (stage >= Cfg.MachineHitsToBreak)
                {
                    FxParticle.Puff(at + Vector3.up * Random.Range(0.3f, 1.2f) + Random.insideUnitSphere * 0.9f, new Color(1f, 0.45f, 0.1f, 0.9f), Random.Range(0.3f, 0.7f));
                    FxParticle.Puff(at + Vector3.up * Random.Range(1.2f, 2.5f) + Random.insideUnitSphere * 0.6f, new Color(0.1f, 0.1f, 0.1f, 0.5f), Random.Range(0.8f, 1.5f));
                }
            }
        }

        void BuildCryo()
        {
            m_Cryo = new GameObject("cryochamber");
            m_Cryo.transform.SetParent(transform, false);
            m_Cryo.transform.position = Cfg.SocketPos(Team) - Vector3.up * 0.64f;
            m_Cryo.transform.rotation = Quaternion.LookRotation(-Cfg.BackDir(Team)); // (its doors face out of the base)
            var t = m_Cryo.transform;
            var metal = new Color(0.32f, 0.35f, 0.4f);
            var tc = Cfg.TeamColor[Mathf.Clamp(Team, 0, 3)];
            float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
            Art.Part(t, Art.Cylinder, metal, new Vector3(0, 0.12f, 0), new Vector3(1.5f / cr, 0.24f / ch, 1.5f / cr));
            Art.Part(t, Art.Cylinder, tc, new Vector3(0, 0.26f, 0), new Vector3(1.55f / cr, 0.04f / ch, 1.55f / cr));
            // the pod: a frosted glass shell round the back, two curved frosted doors at the front that slide apart to let
            // a respawning player out (PlayRelease), a sleeper's soft glow inside, a cap with lights on top
            var frost = Art.Ghost(new Color(0.75f, 0.92f, 1f, 0.32f));
            void Arc(Transform into, float from, float to, int n)
            {
                for (int k = 0; k < n; k++)
                {
                    float a = Mathf.Lerp(from, to, (k + 0.5f) / n) * Mathf.Deg2Rad;
                    Art.Box(into, Color.white, new Vector3(Mathf.Sin(a) * 0.52f, 1.32f, Mathf.Cos(a) * 0.52f), new Vector3(2f * 0.52f * Mathf.Sin((to - from) * Mathf.Deg2Rad / n / 2f) + 0.02f, 2.1f, 0.04f), new Vector3(0, a * Mathf.Rad2Deg, 0), false, frost);
                }
            }
            Arc(t, 95f, 265f, 10); // (the back)
            m_DoorL = new GameObject("door L").transform; m_DoorL.SetParent(t, false);
            m_DoorR = new GameObject("door R").transform; m_DoorR.SetParent(t, false);
            Arc(m_DoorL, -90f, 0f, 6);
            Arc(m_DoorR, 0f, 90f, 6);
            Art.Box(m_DoorL, metal, new Vector3(-0.02f, 1.32f, 0.53f), new Vector3(0.04f, 2.1f, 0.05f)); // (the doors' meeting edges)
            Art.Box(m_DoorR, metal, new Vector3(0.02f, 1.32f, 0.53f), new Vector3(0.04f, 2.1f, 0.05f));
            m_Sleeper = Art.Part(t, Art.Capsule, Color.white, new Vector3(0, 1.3f, 0), new Vector3(0.45f, 0.75f, 0.45f), default, false, Workbench.Glow(new Color(0.55f, 0.9f, 1f), 1.4f));
            Art.Part(t, Art.Cylinder, metal, new Vector3(0, 2.45f, 0), new Vector3(1.1f / cr, 0.14f / ch, 1.1f / cr));
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                Art.Box(t, metal * 0.85f, new Vector3(Mathf.Sin(a) * 0.62f, 1.35f, Mathf.Cos(a) * 0.62f), new Vector3(0.08f, 2.2f, 0.08f));
                Art.Part(t, Art.Sphere, Color.white, new Vector3(Mathf.Sin(a) * 0.5f, 2.54f, Mathf.Cos(a) * 0.5f), Vector3.one * 0.08f, default, false, Workbench.Glow(tc, 2f));
            }
            var l = new GameObject("cryo light").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.localPosition = new Vector3(0, 1.4f, 0);
            l.type = LightType.Point; l.color = new Color(0.6f, 0.9f, 1f); l.range = 5f; l.intensity = 1.6f;
            foreach (var r in m_Cryo.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Every screen: someone respawns in this team's pod (Bedwars) - inside it, behind the frost, for a moment;
        /// then a hiss of cold mist and its doors slide apart, and they step out; a few seconds later the doors close again.</summary>
        public void PlayRelease() => m_ReleaseAt = Time.time;

        /// <summary>How long the pod holds you before its doors open (s).</summary>
        public const float PodHold = 0.9f;

        void TickRelease()
        {
            if (m_DoorL == null || m_DoorR == null) return;
            float t = Time.time - m_ReleaseAt;
            float open;
            if (t < PodHold) open = 0f;
            else if (t < PodHold + 0.6f) open = Mathf.SmoothStep(0f, 1f, (t - PodHold) / 0.6f);
            else if (t < 3.6f) open = 1f;
            else if (t < 4.3f) open = 1f - Mathf.SmoothStep(0f, 1f, (t - 3.6f) / 0.7f);
            else open = 0f;
            // each door slides out sideways and round the back of the pod
            m_DoorL.localPosition = new Vector3(-0.08f * open, 0f, -0.05f * open);
            m_DoorL.localRotation = Quaternion.Euler(0f, -95f * open, 0f);
            m_DoorR.localPosition = new Vector3(0.08f * open, 0f, -0.05f * open);
            m_DoorR.localRotation = Quaternion.Euler(0f, 95f * open, 0f);
            // nobody's asleep in it while it lets someone out
            if (m_Sleeper) m_Sleeper.SetActive(t > 4.3f || t < 0f);
            // the hiss of cold mist as the doors part
            if (t >= PodHold && t < PodHold + 0.45f && m_Cryo != null)
            {
                var at = m_Cryo.transform.position + m_Cryo.transform.forward * 0.6f;
                for (int i = 0; i < 3; i++)
                    FxParticle.Puff(at + Vector3.up * Random.Range(0.3f, 2f) + m_Cryo.transform.right * Random.Range(-0.5f, 0.5f), new Color(0.88f, 0.96f, 1f, 0.55f), Random.Range(0.4f, 0.9f));
            }
            if (t >= PodHold && t - Time.deltaTime < PodHold && m_Cryo != null) Sfx.Play(Sfx.Hiss, m_Cryo.transform.position + Vector3.up, 0.9f, 0.05f, 30f);
        }

        /// <summary>The damage steps: 0 whole, 1 cracked, 2 battered (rings askew), 3+ destroyed (a burning wreck).</summary>
        void ShowStage(int stage)
        {
            if (m_Body.Count == 0)
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (m_Cryo == null || !r.transform.IsChildOf(m_Cryo.transform)) m_Body.Add(r);
            if (m_Cracks) Destroy(m_Cracks);
            m_Cracks = null;
            bool dead = stage >= Cfg.MachineHitsToBreak;
            foreach (var r in m_Body) if (r) r.enabled = !dead;
            if (m_Cryo) m_Cryo.SetActive(!dead);
            if (m_Light) m_Light.enabled = !dead;
            if (dead)
            {
                if (m_Wreck == null)
                {
                    m_Wreck = new GameObject("wreck");
                    m_Wreck.transform.SetParent(transform, false);
                    var rng = new System.Random(Team * 31 + 7);
                    var scorched = new Color(0.12f, 0.12f, 0.13f);
                    for (int i = 0; i < 18; i++)
                    {
                        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                        Art.Box(m_Wreck.transform, i % 3 == 0 ? new Color(0.3f, 0.32f, 0.35f) : scorched, new Vector3(R(-1.6f, 1.6f), R(0.1f, 0.6f), R(-1.2f, 1.2f)),
                            new Vector3(R(0.2f, 0.9f), R(0.1f, 0.5f), R(0.2f, 0.8f)), new Vector3(R(-40f, 40f), R(0f, 360f), R(-40f, 40f)));
                    }
                    Art.Box(m_Wreck.transform, scorched, new Vector3(0, 0.25f, 0), new Vector3(2.7f, 0.5f, 1.2f));
                    var fire = new GameObject("fire").AddComponent<Light>();
                    fire.transform.SetParent(m_Wreck.transform, false);
                    fire.transform.localPosition = new Vector3(0, 1f, 0);
                    fire.type = LightType.Point; fire.color = new Color(1f, 0.5f, 0.15f); fire.range = 8f; fire.intensity = 2.5f;
                }
                return;
            }
            if (stage <= 0) return;
            // cracks: dark jagged lines over the column, more of them the worse it is; then the rings knocked askew
            m_Cracks = new GameObject("cracks");
            m_Cracks.transform.SetParent(transform, false);
            var rng2 = new System.Random(Team * 17 + stage);
            int n = stage == 1 ? 7 : 16;
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng2.NextDouble() * 360f, y = 0.6f + (float)rng2.NextDouble() * 2.2f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Art.Box(m_Cracks.transform, new Color(0.05f, 0.05f, 0.05f), dir * 0.62f + Vector3.up * y, new Vector3(0.04f, 0.25f + (float)rng2.NextDouble() * 0.4f, 0.03f),
                    new Vector3(0, a, (float)rng2.NextDouble() * 70f - 35f));
            }
            if (stage >= 2 && m_Rings) m_Rings.localRotation = Quaternion.Euler(14f, 0f, -9f);
        }

        void TickSkulls(NetGame g)
        {
            int count = g.SkullsOf(Team);
            if (count == m_SkullCount) return;
            m_SkullCount = count;
            if (m_SkullRoot) Destroy(m_SkullRoot.gameObject);
            m_SkullRoot = new GameObject("skulls").transform;
            m_SkullRoot.SetParent(transform, false);
            SkullSpots.Clear();
            int i = 0;
            foreach (var b in g.SkullsIn)
            {
                if (b >> 5 != Team) continue;
                // round the front of the base in a curve, two rows when it's full
                int row = i / 8, col = i % 8;
                float a = (-70f + col * 20f) * Mathf.Deg2Rad;
                var local = new Vector3(Mathf.Sin(a) * (1.75f + row * 0.15f), 0.5f + row * 0.55f, Mathf.Cos(a) * (1.75f + row * 0.15f));
                var s = ItemModels.Create(Item.Skull, m_SkullRoot);
                s.transform.localPosition = local;
                s.transform.localRotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
                s.transform.localScale = Vector3.one * 2.2f;
                SkullSpots.Add((transform.TransformPoint(local + Vector3.up * 0.6f), b & 31));
                i++;
            }
        }
    }
}
