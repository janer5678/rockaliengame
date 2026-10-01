using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Auto Wood: the wood machine on the bedrock, to the right of the alien machine. It chops wood out of thin air and
    /// pushes it out of its chute onto a pile (the pile itself is a world item, NetGame.ServerTickAutoWood). It looks
    /// the part of its wood gen level, and changes in front of you when your team upgrades it:
    /// level 0 - a plank hopper with a small saw; level 1 - iron bands, a bigger saw, a smoking chimney and a lamp;
    /// level 2 - a riveted steel housing, twin saws, two chimneys, glowing gauges and a light on top.
    /// </summary>
    public class WoodMachine : MonoBehaviour
    {
        int m_Team, m_Level = -1;
        Transform m_Visual;
        readonly List<Transform> m_Saws = new List<Transform>();
        readonly List<Transform> m_Chimneys = new List<Transform>();
        Transform m_Log;
        float m_NextPuff, m_Pop = 1f, m_LogT = 1f, m_NextLog;

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

        void Build(int level)
        {
            if (m_Visual) Destroy(m_Visual.gameObject);
            m_Saws.Clear();
            m_Chimneys.Clear();
            m_Level = level;
            m_Visual = new GameObject("visual").transform;
            m_Visual.SetParent(transform, false);
            var t = m_Visual;
            var team = Cfg.TeamColor[Mathf.Clamp(m_Team, 0, 3)];
            var iron = new Color(0.32f, 0.33f, 0.36f);
            var steel = new Color(0.55f, 0.57f, 0.6f);
            var blade = new Color(0.8f, 0.82f, 0.86f);
            var glow = Color.Lerp(team, Color.white, 0.35f);
            float s = level == 2 ? 1.15f : 1f;

            if (level < 2)
            {
                // the plank body and the hopper on top
                Art.Box(t, Art.Wood, new Vector3(0, 0.55f, 0), new Vector3(1.1f, 1.1f, 0.9f), default, true);
                for (int k = 0; k < 4; k++) Art.Box(t, Art.DarkWood, new Vector3(0, 0.15f + k * 0.27f, 0.455f), new Vector3(1.12f, 0.05f, 0.02f));
                Art.Box(t, Art.DarkWood, new Vector3(0, 1.25f, -0.05f), new Vector3(1.0f, 0.3f, 0.75f), new Vector3(-10, 0, 0));
                if (level == 1)
                    for (int k = 0; k < 2; k++) Art.Box(t, iron, new Vector3(0, 0.3f + k * 0.55f, 0), new Vector3(1.14f, 0.07f, 0.94f)); // iron bands
            }
            else
            {
                // riveted steel housing in the team colour
                Art.Box(t, steel, new Vector3(0, 0.65f * s, 0), new Vector3(1.15f, 1.3f, 0.95f) * 1f, default, true);
                Art.Box(t, team * 0.8f, new Vector3(0, 0.65f * s, 0.48f), new Vector3(0.9f, 0.9f, 0.02f));
                for (int x = -1; x <= 1; x += 2)
                for (int y = 0; y < 3; y++)
                    Art.Part(t, Art.Sphere, iron, new Vector3(x * 0.5f, 0.25f + y * 0.45f, 0.48f), Vector3.one * 0.06f);
                Art.Box(t, iron, new Vector3(0, 1.38f, -0.05f), new Vector3(1.1f, 0.2f, 0.85f));
                // glowing gauges
                for (int x = -1; x <= 1; x += 2)
                {
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(x * 0.25f, 1.0f, 0.5f), new Vector3(0.18f, 0.01f, 0.18f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, glow, new Vector3(x * 0.25f, 1.0f, 0.505f), new Vector3(0.12f, 0.01f, 0.12f), new Vector3(90, 0, 0));
                }
            }
            // the chute that the wood comes out of, onto the pile in front
            Art.Box(t, level == 2 ? iron : Art.DarkWood, new Vector3(0, 0.35f, 0.62f), new Vector3(0.55f, 0.08f, 0.5f), new Vector3(20, 0, 0));
            Art.Box(t, level == 2 ? iron : Art.DarkWood, new Vector3(0.27f, 0.42f, 0.62f), new Vector3(0.04f, 0.15f, 0.5f), new Vector3(20, 0, 0));
            Art.Box(t, level == 2 ? iron : Art.DarkWood, new Vector3(-0.27f, 0.42f, 0.62f), new Vector3(0.04f, 0.15f, 0.5f), new Vector3(20, 0, 0));
            // saw blades on top (more and bigger with each level)
            int saws = level == 2 ? 2 : 1;
            float r = level == 0 ? 0.32f : 0.42f;
            for (int i = 0; i < saws; i++)
            {
                var pivot = new GameObject("saw").transform;
                pivot.SetParent(t, false);
                pivot.localPosition = new Vector3(saws == 1 ? 0f : (i == 0 ? -0.28f : 0.28f), (level == 2 ? 1.5f : 1.35f) + r * 0.6f, 0.05f);
                Art.Part(pivot, Art.Cylinder, blade, Vector3.zero, new Vector3(r * 2f, 0.015f, r * 2f), new Vector3(0, 0, 90));
                for (int k = 0; k < 8; k++)
                {
                    var rot = Quaternion.Euler(k * 45f, 0, 0);
                    Art.Box(pivot, blade * 0.9f, rot * new Vector3(0, r, 0), new Vector3(0.02f, 0.08f, 0.06f), new Vector3(k * 45f, 0, 0));
                }
                Art.Part(pivot, Art.Cylinder, iron, Vector3.zero, new Vector3(0.12f, 0.03f, 0.12f), new Vector3(0, 0, 90));
                m_Saws.Add(pivot);
            }
            // chimneys (level 1: one, level 2: two) with a lamp
            int chimneys = level;
            for (int i = 0; i < chimneys; i++)
            {
                var c = new GameObject("chimney").transform;
                c.SetParent(t, false);
                c.localPosition = new Vector3(i == 0 ? -0.42f : 0.42f, level == 2 ? 1.45f : 1.3f, -0.32f);
                Art.Part(c, Art.Cylinder, iron, new Vector3(0, 0.35f, 0), new Vector3(0.16f, 0.35f, 0.16f));
                Art.Part(c, Art.Cylinder, iron * 0.7f, new Vector3(0, 0.71f, 0), new Vector3(0.2f, 0.03f, 0.2f));
                m_Chimneys.Add(c);
            }
            if (level >= 1)
                Art.Part(t, Art.Ico, glow, new Vector3(0.45f, (level == 2 ? 1.5f : 1.25f), 0.35f), Vector3.one * 0.09f);
            if (level == 2)
            {
                // a light on top, and a little sign with the level
                Art.Part(t, Art.Ico, glow, new Vector3(0, 2.25f, -0.1f), Vector3.one * 0.14f);
                var lg = new GameObject("light");
                lg.transform.SetParent(t, false);
                lg.transform.localPosition = new Vector3(0, 2.2f, 0.2f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = glow;
                l.range = 5f;
                l.intensity = 1.6f;
            }
            // level pips on the front: how far it's been upgraded
            for (int i = 0; i < Cfg.MaxWoodGen; i++)
                Art.Box(t, i < level ? glow : new Color(0.15f, 0.15f, 0.15f), new Vector3(-0.12f + i * 0.24f, (level == 2 ? 1.25f : 1.0f), level == 2 ? 0.49f : 0.465f), new Vector3(0.16f, 0.07f, 0.02f));
            // a log on its way out of the chute
            m_Log = Art.Part(t, Art.Cylinder, Art.Wood, new Vector3(0, 0.5f, 0.5f), new Vector3(0.14f, 0.2f, 0.14f), new Vector3(0, 0, 90)).transform;
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
            float speed = running ? 360f + m_Level * 360f : 0f;
            foreach (var saw in m_Saws) if (saw) saw.Rotate(speed * Time.deltaTime, 0, 0, Space.Self);
            // smoke from the chimneys
            if (running && m_Chimneys.Count > 0 && Time.time > m_NextPuff)
            {
                m_NextPuff = Time.time + 0.5f / m_Chimneys.Count;
                var c = m_Chimneys[Random.Range(0, m_Chimneys.Count)];
                if (c) FxParticle.Puff(c.position + Vector3.up * 0.75f, new Color(0.55f, 0.55f, 0.55f, 0.5f), Random.Range(0.25f, 0.45f));
            }
            // a log slides down the chute about once a second (it's what lands on the pile)
            if (running && Time.time > m_NextLog && m_Log != null) { m_NextLog = Time.time + 1f; m_LogT = 0f; }
            if (m_Log != null)
            {
                m_LogT = Mathf.Min(1f, m_LogT + Time.deltaTime * 2.2f);
                m_Log.gameObject.SetActive(m_LogT < 1f);
                m_Log.localPosition = Vector3.Lerp(new Vector3(0, 0.55f, 0.35f), new Vector3(0, 0.2f, 1.0f), m_LogT);
            }
            // squash and stretch when it's just been upgraded
            if (m_Pop < 1f && m_Visual)
            {
                m_Pop = Mathf.Min(1f, m_Pop + Time.deltaTime * 2.5f);
                float b = Mathf.Sin(m_Pop * Mathf.PI * 3f) * (1f - m_Pop) * 0.35f;
                m_Visual.localScale = new Vector3(1f - b * 0.5f, 1f + b, 1f - b * 0.5f);
            }
            else if (m_Visual) m_Visual.localScale = Vector3.one;
        }
    }
}
