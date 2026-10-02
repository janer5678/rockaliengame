using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Auto Wood: the wood machine on the bedrock, to the right of the alien machine. An alien bio-machine that grows wood
    /// out of thin air and spits it out of its mouth onto a pile (the pile itself is a world item, NetGame.ServerTickAutoWood).
    /// A living pod on claw legs with glowing veins in the team colour, an eye that watches you, swaying tentacles and
    /// floating harvester rings, and it looks the part of its wood gen level, changing in front of you when your team upgrades it:
    /// level 0 - a small pod with one eye, one tentacle and one ring;
    /// level 1 - bigger, with chitin plates, two rings round a floating core, two tentacles, a spore vent and a glowing antenna;
    /// level 2 - an alien-metal shell over the pod, three eyes, a three-ring gyroscope, four tentacles, two spore vents,
    ///           a glass dome with a glowing brain and a spinning crystal hovering over it.
    /// </summary>
    public class WoodMachine : MonoBehaviour
    {
        int m_Team, m_Level = -1;
        Transform m_Visual;
        readonly List<Transform> m_Rings = new List<Transform>();
        readonly List<Transform[]> m_Tentacles = new List<Transform[]>();
        readonly List<Transform> m_Vents = new List<Transform>();
        readonly List<Transform> m_Eyes = new List<Transform>();
        readonly List<Transform> m_Throb = new List<Transform>();
        readonly List<Vector3> m_ThrobBase = new List<Vector3>();
        Transform m_Core, m_Crystal, m_Log;
        Vector3 m_CoreBase, m_CrystalBase;
        Light m_Light;
        Material m_GlowMat;
        Color m_Glow;
        float m_NextPuff, m_Pop = 1f, m_LogT = 1f, m_NextLog, m_NextBlink, m_BlinkT = 1f, m_LightBase;

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

        /// <summary>A glowing (emissive) material in the team colour, one per machine so its veins can pulse.</summary>
        Material GlowMat()
        {
            if (m_GlowMat) return m_GlowMat;
            m_GlowMat = new Material(Art.Mat(m_Glow));
            if (m_GlowMat.HasProperty("_EmissionColor"))
            {
                m_GlowMat.EnableKeyword("_EMISSION");
                m_GlowMat.SetColor("_EmissionColor", m_Glow * 1.5f);
            }
            return m_GlowMat;
        }

        void OnDestroy()
        {
            if (m_GlowMat) Destroy(m_GlowMat);
        }

        /// <summary>A cylinder from a to b (local to parent), thick across. (Art.Cylinder is radius 1, 2 tall.)</summary>
        static GameObject Limb(Transform parent, Vector3 a, Vector3 b, float thick, Color c, Material mat = null)
        {
            var d = b - a;
            return Art.Part(parent, Art.Cylinder, c, (a + b) * 0.5f, new Vector3(thick * 0.5f, d.magnitude * 0.5f, thick * 0.5f),
                Quaternion.FromToRotation(Vector3.up, d.normalized).eulerAngles, false, mat);
        }

        void Throb(Transform t)
        {
            m_Throb.Add(t);
            m_ThrobBase.Add(t.localScale);
        }

        void Build(int level)
        {
            if (m_Visual) Destroy(m_Visual.gameObject);
            m_Rings.Clear();
            m_Tentacles.Clear();
            m_Vents.Clear();
            m_Eyes.Clear();
            m_Throb.Clear();
            m_ThrobBase.Clear();
            m_Core = m_Crystal = null;
            m_Level = level;
            m_Visual = new GameObject("visual").transform;
            m_Visual.SetParent(transform, false);
            var t = m_Visual;
            var team = Cfg.TeamColor[Mathf.Clamp(m_Team, 0, 3)];
            m_Glow = Color.Lerp(team, Color.white, 0.3f);
            var glow = GlowMat();
            var flesh = Color.Lerp(new Color(0.3f, 0.2f, 0.38f), team, 0.18f);      // dusky violet alien skin, a hint of the team
            var chitin = new Color(0.14f, 0.12f, 0.18f);
            var silver = new Color(0.72f, 0.75f, 0.8f);
            var dark = new Color(0.05f, 0.04f, 0.06f);
            float s = level == 2 ? 1.15f : level == 1 ? 1.08f : 1f;
            float cy = 0.66f * s, rx = 0.52f * s, ry = 0.56f * s, rz = 0.44f * s; // the pod
            float top = cy + ry;

            // what you bump into
            var body = new GameObject("body");
            body.transform.SetParent(t, false);
            var bc = body.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, top * 0.5f, 0);
            bc.size = new Vector3(rx * 2.1f, top, rz * 2.2f);

            // claw legs, then the pod
            int legs = level == 2 ? 4 : 3;
            for (int i = 0; i < legs; i++)
            {
                float a = (legs == 3 ? 90f + i * 120f : 45f + i * 90f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var hip = new Vector3(dir.x * rx * 0.6f, cy - ry * 0.45f, dir.z * rz * 0.6f);
                var knee = new Vector3(dir.x * rx * 1.25f, cy * 0.75f, dir.z * rz * 1.2f);
                var foot = new Vector3(dir.x * rx * 1.35f, 0.02f, dir.z * rz * 1.3f);
                Limb(t, hip, knee, 0.16f * s, level == 2 ? silver * 0.8f : chitin);
                Limb(t, knee, foot, 0.12f * s, level == 2 ? silver * 0.8f : chitin);
                Art.Part(t, Art.Ico, m_Glow, knee, Vector3.one * 0.06f * s, default, false, glow);
            }
            var pod = Art.Part(t, Art.Sphere, flesh, new Vector3(0, cy, 0), new Vector3(rx, ry, rz)); // (Art.Sphere is radius 1)
            Throb(pod.transform);

            // glowing veins running up the pod
            int veins = level == 0 ? 4 : level == 1 ? 6 : 8;
            for (int v = 0; v < veins; v++)
            {
                float a = (v + 0.5f) / veins * Mathf.PI * 2f;
                Vector3 prev = default;
                for (int k = 0; k <= 4; k++)
                {
                    float th = Mathf.Lerp(0.35f, 2.75f, k / 4f);
                    float wob = Mathf.Sin(k * 1.7f + v) * 0.18f;
                    var p = new Vector3(Mathf.Sin(th) * rx * Mathf.Cos(a + wob) * 1.02f, cy - Mathf.Cos(th) * ry * 1.02f, Mathf.Sin(th) * rz * Mathf.Sin(a + wob) * 1.02f);
                    if (k > 0) Limb(t, prev, p, 0.05f, m_Glow, glow);
                    prev = p;
                }
            }

            // chitin plates (level 1) / an alien-metal shell with glowing seams (level 2), over the back of the pod
            if (level >= 1)
            {
                int plates = level == 2 ? 5 : 3;
                for (int i = 0; i < plates; i++)
                {
                    float a = Mathf.Lerp(-70f, 70f, plates == 1 ? 0.5f : i / (float)(plates - 1));
                    var rot = Quaternion.Euler(0, a, 0);
                    var p = rot * new Vector3(0, 0, -rz * 0.72f) + new Vector3(0, cy + 0.08f * s, 0);
                    Art.Part(t, Art.Ico, level == 2 ? silver : chitin, p, new Vector3(0.3f, 0.5f, 0.16f) * s, new Vector3(-12f, a, 0));
                    if (level == 2) Art.Part(t, Art.Cube, m_Glow, p + rot * new Vector3(0, 0, -0.17f * s), new Vector3(0.025f, 0.5f, 0.02f) * s, new Vector3(-12f, a, 0), false, glow);
                }
            }
            if (level == 2)
            {
                // a silver collar round the middle
                Art.Part(t, Art.Cylinder, silver, new Vector3(0, cy, 0), new Vector3(rx * 1.04f, 0.05f, rz * 1.04f));
                Art.Part(t, Art.Cylinder, m_Glow, new Vector3(0, cy, 0), new Vector3(rx * 1.06f, 0.018f, rz * 1.06f), default, false, glow);
            }

            // eyes: one, one, three - they look around and blink
            var eyeSpots = level == 2
                ? new[] { new Vector3(0, cy + 0.2f * s, rz * 0.93f), new Vector3(-0.24f * s, cy + 0.06f * s, rz * 0.86f), new Vector3(0.24f * s, cy + 0.06f * s, rz * 0.86f) }
                : new[] { new Vector3(0, cy + 0.14f * s, rz * 0.93f) };
            for (int i = 0; i < eyeSpots.Length; i++)
            {
                float es = (i == 0 ? (level == 0 ? 0.2f : 0.24f) : 0.14f) * s;
                Art.Part(t, Art.Ico, chitin, eyeSpots[i] - new Vector3(0, 0, 0.03f), new Vector3(es * 0.68f, es * 0.58f, es * 0.4f)); // the socket
                var eye = new GameObject("eye").transform;
                eye.SetParent(t, false);
                eye.localPosition = eyeSpots[i];
                Art.Part(eye, Art.Sphere, new Color(0.92f, 0.95f, 0.85f), Vector3.zero, Vector3.one * es * 0.5f);
                Art.Part(eye, Art.Sphere, m_Glow, new Vector3(0, 0, es * 0.45f), new Vector3(es * 0.31f, es * 0.31f, es * 0.1f), default, false, glow);
                Art.Part(eye, Art.Cube, dark, new Vector3(0, 0, es * 0.56f), new Vector3(es * 0.12f, es * 0.5f, es * 0.04f)); // slit pupil
                m_Eyes.Add(eye);
            }

            // the mouth the wood comes out of, onto the pile in front
            var mouthAt = new Vector3(0, 0.42f, rz * 0.95f);
            Art.Part(t, Art.Cylinder, flesh * 0.75f, mouthAt, new Vector3(0.21f, 0.09f, 0.18f), new Vector3(65f, 0, 0));
            Art.Part(t, Art.Cylinder, m_Glow, mouthAt + new Vector3(0, -0.04f, 0.04f), new Vector3(0.15f, 0.095f, 0.125f), new Vector3(65f, 0, 0), false, glow);
            // a slimy tongue down to the pile
            Art.Box(t, flesh * 0.65f, new Vector3(0, 0.24f, rz + 0.3f), new Vector3(0.32f, 0.05f, 0.5f), new Vector3(28f, 0, 0));
            for (int k = -1; k <= 1; k += 2)
                Art.Part(t, Art.Cone, chitin, mouthAt + new Vector3(k * 0.16f, -0.05f, 0.1f), new Vector3(0.06f, 0.14f, 0.06f), new Vector3(150f, 0, 0)); // fangs

            // floating harvester rings over the pod (in place of saws), round a glowing core
            int rings = level + 1;
            var ringRoot = new GameObject("rings").transform;
            ringRoot.SetParent(t, false);
            ringRoot.localPosition = new Vector3(0, top + 0.32f * s, -0.02f);
            for (int r = 0; r < rings; r++)
            {
                var ring = new GameObject("ring").transform;
                ring.SetParent(ringRoot, false);
                ring.localRotation = Quaternion.Euler(r == 0 ? 0f : r == 1 ? 70f : 20f, 0, r == 2 ? 70f : 10f);
                float rad = (level == 0 ? 0.3f : 0.38f + r * 0.03f) * s;
                int bits = 10;
                for (int i = 0; i < bits; i++)
                {
                    float a = i * Mathf.PI * 2f / bits;
                    bool lit = i % 2 == 0;
                    Art.Box(ring, lit ? m_Glow : (level == 2 ? silver : chitin), new Vector3(Mathf.Sin(a) * rad, 0, Mathf.Cos(a) * rad),
                        new Vector3(0.16f, 0.04f, 0.06f) * s, new Vector3(0, a * Mathf.Rad2Deg + 90f, 0), false, lit ? glow : null);
                }
                m_Rings.Add(ring);
            }
            m_Core = Art.Part(ringRoot, Art.Ico, m_Glow, Vector3.zero, Vector3.one * (0.09f + level * 0.025f) * s, default, false, glow).transform;
            Art.Part(m_Core, Art.Ico, Color.white, Vector3.zero, Vector3.one * 1.7f, default, false, Art.Ghost(new Color(m_Glow.r, m_Glow.g, m_Glow.b, 0.25f)));
            m_CoreBase = m_Core.localPosition;
            // a stalk from the pod up to the rings
            Limb(t, new Vector3(0, top - 0.05f, -0.02f), ringRoot.localPosition - new Vector3(0, 0.1f, 0), 0.09f * s, level == 2 ? silver : chitin);

            // tentacles: 1, 2, 4, swaying
            int tents = level == 0 ? 1 : level == 1 ? 2 : 4;
            for (int i = 0; i < tents; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                bool back = i >= 2;
                var root = new GameObject("tentacle").transform;
                root.SetParent(t, false);
                root.localPosition = new Vector3(side * rx * 0.85f, cy + (back ? 0.15f : -0.05f) * s, back ? -rz * 0.45f : rz * 0.15f);
                root.localRotation = Quaternion.Euler(back ? -25f : 10f, 0, -side * (back ? 35f : 55f));
                int segs = 6;
                var chain = new Transform[segs];
                var parent = root;
                for (int k = 0; k < segs; k++)
                {
                    var j = new GameObject("seg").transform;
                    j.SetParent(parent, false);
                    j.localPosition = k == 0 ? Vector3.zero : new Vector3(0, 0.11f * s, 0);
                    float w = Mathf.Lerp(0.11f, 0.04f, k / (float)(segs - 1)) * s;
                    Art.Part(j, Art.Sphere, k % 2 == 0 ? flesh : flesh * 0.82f, new Vector3(0, 0.055f * s, 0), new Vector3(w * 0.5f, 0.065f * s, w * 0.5f));
                    if (k == segs - 1) Art.Part(j, Art.Ico, m_Glow, new Vector3(0, 0.13f * s, 0), Vector3.one * 0.045f * s, default, false, glow);
                    chain[k] = j;
                    parent = j;
                }
                m_Tentacles.Add(chain);
            }

            // spore vents (level 1: one, level 2: two) puffing glowing spores
            for (int i = 0; i < level; i++)
            {
                var v = new GameObject("vent").transform;
                v.SetParent(t, false);
                v.localPosition = new Vector3(i == 0 ? -0.24f * s : 0.24f * s, top - 0.12f * s, -rz * 0.55f);
                v.localRotation = Quaternion.Euler(-20f, 0, i == 0 ? 15f : -15f);
                Art.Part(v, Art.Cone, level == 2 ? silver : chitin, Vector3.zero, new Vector3(0.2f, 0.3f, 0.2f) * s, new Vector3(180f, 0, 0));
                Art.Part(v, Art.Cylinder, chitin, new Vector3(0, 0.02f, 0), new Vector3(0.08f, 0.1f, 0.08f) * s);
                Art.Part(v, Art.Cylinder, m_Glow, new Vector3(0, 0.12f * s, 0), new Vector3(0.06f, 0.012f, 0.06f) * s, default, false, glow);
                m_Vents.Add(v);
            }
            // level 1: a glowing antenna
            if (level == 1)
            {
                var a0 = new Vector3(0.22f, top - 0.08f, -0.12f);
                var a1 = new Vector3(0.36f, top + 0.42f, -0.2f);
                Limb(t, a0, a1, 0.03f, chitin);
                var bulb = Art.Part(t, Art.Ico, m_Glow, a1, Vector3.one * 0.075f, default, false, glow);
                Throb(bulb.transform);
            }
            // level 2: a glass dome with a glowing brain, and a crystal spinning over it
            if (level == 2)
            {
                var domeAt = new Vector3(0, top - 0.08f, -0.22f);
                var brain = Art.Part(t, Art.Ico, Color.Lerp(m_Glow, new Color(1f, 0.55f, 0.85f), 0.5f), domeAt + new Vector3(0, 0.08f, 0), new Vector3(0.17f, 0.12f, 0.15f), default, false, glow);
                Throb(brain.transform);
                Art.Part(t, Art.Sphere, Color.white, domeAt, new Vector3(0.21f, 0.18f, 0.21f), default, false, Art.Ghost(new Color(0.7f, 0.95f, 1f, 0.28f)));
                Art.Part(t, Art.Cylinder, silver, domeAt, new Vector3(0.22f, 0.03f, 0.22f));
                m_Crystal = new GameObject("crystal").transform;
                m_Crystal.SetParent(t, false);
                m_Crystal.localPosition = new Vector3(0, top + 1.02f * s, -0.05f);
                Art.Part(m_Crystal, Art.Cone, m_Glow, Vector3.zero, new Vector3(0.22f, 0.3f, 0.22f), default, false, glow);
                Art.Part(m_Crystal, Art.Cone, m_Glow, Vector3.zero, new Vector3(0.22f, 0.22f, 0.22f), new Vector3(180f, 0, 0), false, glow);
                m_CrystalBase = m_Crystal.localPosition;
            }

            // a light in the team colour, brighter with each level
            var lg = new GameObject("light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, top + 0.35f, 0.45f);
            m_Light = lg.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.color = m_Glow;
            m_Light.range = 2.5f + level * 1.8f;
            m_LightBase = 0.7f + level * 0.55f;
            m_Light.intensity = m_LightBase;

            // level pips on the front: how far it's been upgraded
            for (int i = 0; i < Cfg.MaxWoodGen; i++)
                Art.Part(t, Art.Ico, i < level ? m_Glow : chitin, new Vector3(-0.09f + i * 0.18f, cy - 0.2f * s, rz * 1.0f), Vector3.one * 0.05f, default, false, i < level ? glow : null);

            // a log being grown and spat out of the mouth
            m_Log = Art.Part(t, Art.Cylinder, Art.Wood, mouthAt, new Vector3(0.14f, 0.2f, 0.14f), new Vector3(0, 0, 90)).transform;
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

            // pulsing glow (veins, eyes, rings) and light
            float beat = 0.5f + 0.5f * Mathf.Sin(time * 2.6f * pace);
            if (m_GlowMat && m_GlowMat.HasProperty("_EmissionColor")) m_GlowMat.SetColor("_EmissionColor", m_Glow * (0.9f + 1.6f * beat));
            if (m_Light) m_Light.intensity = m_LightBase * (0.75f + 0.5f * beat);
            for (int i = 0; i < m_Throb.Count; i++)
                if (m_Throb[i]) m_Throb[i].localScale = m_ThrobBase[i] * (1f + 0.035f * Mathf.Sin(time * 2.6f * pace + i));

            // the rings spin round the core, faster with each level
            float spin = running ? 90f + m_Level * 110f : 20f;
            for (int i = 0; i < m_Rings.Count; i++)
                if (m_Rings[i]) m_Rings[i].Rotate(0, (i % 2 == 0 ? spin : -spin * 1.3f) * dt, 0, Space.Self);
            if (m_Core) m_Core.localPosition = m_CoreBase + Vector3.up * Mathf.Sin(time * 2f) * 0.04f;
            if (m_Crystal)
            {
                m_Crystal.localPosition = m_CrystalBase + Vector3.up * Mathf.Sin(time * 1.5f) * 0.08f;
                m_Crystal.Rotate(0, 70f * dt, 0, Space.Self);
            }

            // tentacles sway (curling a little more at the tip)
            for (int i = 0; i < m_Tentacles.Count; i++)
            {
                var chain = m_Tentacles[i];
                for (int k = 0; k < chain.Length; k++)
                {
                    if (!chain[k]) continue;
                    float ph = time * 1.7f * pace + i * 1.3f - k * 0.55f;
                    float amp = 8f + k * 4f;
                    chain[k].localRotation = Quaternion.Euler(Mathf.Sin(ph) * amp, 0, Mathf.Cos(ph * 0.8f) * amp * 0.8f + (k > 0 ? 6f : 0f));
                }
            }

            // eyes: follow you when you're close, otherwise look around; blink now and then
            var cam = Camera.main;
            if (time > m_NextBlink) { m_NextBlink = time + Random.Range(2.5f, 5.5f); m_BlinkT = 0f; }
            m_BlinkT = Mathf.Min(1f, m_BlinkT + dt * 7f);
            float lid = m_BlinkT < 1f ? Mathf.Max(0.08f, Mathf.Abs(m_BlinkT * 2f - 1f)) : 1f;
            for (int i = 0; i < m_Eyes.Count; i++)
            {
                var e = m_Eyes[i];
                if (!e) continue;
                Quaternion look = Quaternion.Euler(Mathf.Sin(time * 0.7f + i) * 12f, Mathf.Sin(time * 0.45f + i * 2f) * 30f, 0);
                if (cam != null)
                {
                    var to = cam.transform.position - e.position;
                    if (to.sqrMagnitude < 15f * 15f)
                    {
                        var local = Quaternion.Inverse(m_Visual.rotation) * to.normalized;
                        if (local.z > 0.1f) look = Quaternion.LookRotation(local);
                    }
                }
                e.localRotation = Quaternion.Slerp(e.localRotation, look, dt * 6f);
                e.localScale = new Vector3(1f, lid, 1f);
            }

            // glowing spores out of the vents
            if (running && m_Vents.Count > 0 && time > m_NextPuff)
            {
                m_NextPuff = time + 0.45f / m_Vents.Count;
                var v = m_Vents[Random.Range(0, m_Vents.Count)];
                if (v) FxParticle.Puff(v.position + v.up * 0.18f, new Color(m_Glow.r, m_Glow.g, m_Glow.b, 0.45f), Random.Range(0.18f, 0.32f));
            }

            // a log grows in the mouth and slides out onto the pile about once a second
            if (running && time > m_NextLog && m_Log != null)
            {
                m_NextLog = time + 1f;
                m_LogT = 0f;
                FxParticle.Spawn(m_Log.parent.TransformPoint(new Vector3(0, 0.45f, 0.5f)), (transform.forward + Random.insideUnitSphere * 0.5f) * 0.8f + Vector3.up, m_Glow, 0.04f, 0.5f, 3f, false);
            }
            if (m_Log != null)
            {
                m_LogT = Mathf.Min(1f, m_LogT + dt * 2.2f);
                m_Log.gameObject.SetActive(m_LogT < 1f);
                float grow = Mathf.Clamp01(m_LogT * 4f);
                m_Log.localScale = new Vector3(0.14f, 0.2f, 0.14f) * Mathf.Lerp(0.2f, 1f, grow);
                m_Log.localPosition = Vector3.Lerp(new Vector3(0, 0.45f, 0.42f), new Vector3(0, 0.2f, 1.0f), Mathf.Clamp01((m_LogT - 0.2f) / 0.8f));
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
