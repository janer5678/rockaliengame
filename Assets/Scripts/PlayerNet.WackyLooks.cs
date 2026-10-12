using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// How the wacky items make a player look, on every screen: a BEAR or an ANT instead of the alien, locked in a big
    /// see-through TIMEOUT BALL with its countdown over it, a SHIELD bubble, gold glitter, purple goo at the feet, the jump
    /// boots on their feet, upside down (gravity flipped), in the enemy's colour (disguised), dancing and knocked flat
    /// (BodyAnimator's Dance / Knock).
    /// </summary>
    public partial class PlayerNet
    {
        GameObject m_BearLook, m_AntLook, m_PrisonLook, m_ShieldLook;
        readonly System.Collections.Generic.List<GameObject> m_Boots = new System.Collections.Generic.List<GameObject>();
        TextMesh m_PrisonText;
        readonly System.Collections.Generic.List<Transform> m_CritterLegs = new System.Collections.Generic.List<Transform>();
        Vector3 m_CritterLast;
        float m_CritterPhase, m_KnockVis, m_DanceVis, m_NextGlitter, m_NextGoo;
        int m_LookTeam = -1;
        /// <summary>For the tests: is this player's own body (the alien) being drawn?</summary>
        public bool BodyShownForTests => m_VisualRoot != null && m_VisualRoot.gameObject.activeSelf;
        bool m_WasFlipVis;

        /// <summary>Update, before the body's shown or hidden: what the wacky items make of this player.</summary>
        void TickWackyLooks(bool dead, ref bool showBody)
        {
            float dt = Time.deltaTime;
            bool hidden = !Mine && Hidden;
            // the bear and the ant take the alien's place
            bool bear = IsBear && !dead, ant = IsAnt && !dead && !bear;
            if (bear != (m_BearLook != null)) { if (m_BearLook) Destroy(m_BearLook); m_BearLook = bear ? BuildBear() : null; m_CritterLegs.Clear(); if (bear) CollectLegs(m_BearLook); }
            if (ant != (m_AntLook != null)) { if (m_AntLook) Destroy(m_AntLook); m_AntLook = ant ? BuildAnt() : null; if (!bear) m_CritterLegs.Clear(); if (ant) CollectLegs(m_AntLook); }
            if (bear || ant) showBody = false;
            var critter = bear ? m_BearLook : ant ? m_AntLook : null;
            if (critter != null)
            {
                critter.SetActive(!hidden);
                critter.transform.localRotation = Quaternion.identity;
                // legs going as fast as it really moves
                var d = transform.position - m_CritterLast; d.y = 0f;
                m_CritterLast = transform.position;
                float speed = dt > 0f ? Mathf.Min(d.magnitude / dt, 12f) : 0f;
                m_CritterPhase += dt * speed * (ant ? 14f : 3.2f);
                for (int i = 0; i < m_CritterLegs.Count; i++)
                    m_CritterLegs[i].localRotation = Quaternion.Euler(Mathf.Sin(m_CritterPhase + (i % 2 == 0 ? 0f : Mathf.PI) + i * 0.3f) * (ant ? 30f : 28f) * Mathf.Clamp01(speed / 2f), 0, 0);
                if (bear && m_CritterLegs.Count > 0) critter.transform.localPosition = Vector3.up * Mathf.Abs(Mathf.Sin(m_CritterPhase)) * 0.06f * Mathf.Clamp01(speed / 2f);
            }
            // the timeout ball round them, its seconds over it
            bool prison = Prisoned && !dead;
            if (prison != (m_PrisonLook != null)) { if (m_PrisonLook) Destroy(m_PrisonLook); m_PrisonLook = prison ? BuildPrison() : null; }
            if (m_PrisonLook != null)
            {
                float left = Mathf.Max(0f, (float)(PrisonUntil.Value - Now));
                m_PrisonText.text = Mathf.CeilToInt(left).ToString();
                var cam = Camera.main;
                if (cam != null) m_PrisonText.transform.rotation = Quaternion.LookRotation(m_PrisonText.transform.position - cam.transform.position);
                m_PrisonLook.transform.localRotation = Quaternion.Euler(0, Time.time * 40f, 0);
            }
            // the shield bubble
            bool shield = Shielded && !dead;
            if (shield != (m_ShieldLook != null)) { if (m_ShieldLook) Destroy(m_ShieldLook); m_ShieldLook = shield ? BuildShield() : null; }
            if (m_ShieldLook != null) { m_ShieldLook.SetActive(!hidden); m_ShieldLook.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 6f) * 0.03f); }
            // the jump boots
            bool boots = WornBoots.Value && !dead && !bear && !ant && m_Anim != null;
            if (boots != (m_Boots.Count > 0)) { foreach (var b in m_Boots) if (b) Destroy(b); m_Boots.Clear(); if (boots) BuildBoots(); }
            // gold glitter and slowness goo
            if (Glittering && !dead && !hidden && !Mine && Time.time >= m_NextGlitter)
            {
                m_NextGlitter = Time.time + 0.05f;
                var p = transform.position + new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(0.2f, 1.9f), Random.Range(-0.45f, 0.45f));
                FxParticle.Spawn(p, Random.insideUnitSphere * 0.6f + Vector3.up * 0.3f, Color.Lerp(new Color(1f, 0.8f, 0.15f), Color.white, Random.value * 0.5f), 0.05f, 0.8f, 1f, false);
            }
            if (Slowed && !dead && !hidden && Time.time >= m_NextGoo)
            {
                m_NextGoo = Time.time + 0.12f;
                FxParticle.Puff(transform.position + new Vector3(Random.Range(-0.4f, 0.4f), 0.1f, Random.Range(-0.4f, 0.4f)), new Color(0.55f, 0.2f, 0.85f, 0.6f), Random.Range(0.4f, 0.7f), 0.8f, Vector3.zero);
            }
            // upside down (the gravity staff): the body turned over, its feet up where the capsule's top is
            bool flip = Flipped && !dead;
            if (flip) { m_VisualRoot.localRotation = Quaternion.Euler(0, 0, 180f); m_VisualRoot.localPosition = Vector3.up * (Crouch.Value ? Cfg.CrouchHeight : Cfg.StandHeight); m_WasFlipVis = true; }
            else if (m_WasFlipVis) { m_VisualRoot.localRotation = Quaternion.identity; m_VisualRoot.localPosition = Vector3.zero; m_WasFlipVis = false; }
            // the disguise: the enemy's colour
            if (ApparentTeam != m_LookTeam) { m_LookTeam = ApparentTeam; Recolor(); }
            // dancing / knocked flat (smoothed, for BodyAnimator)
            m_DanceVis = Mathf.MoveTowards(m_DanceVis, Dancing && !dead ? 1f : 0f, dt * 5f);
            m_KnockVis = Mathf.MoveTowards(m_KnockVis, KnockedDown && !dead ? 1f : 0f, dt * (KnockedDown ? 4f : 1.6f));
        }

        void CollectLegs(GameObject look)
        {
            foreach (var t in look.GetComponentsInChildren<Transform>()) if (t.name == "leg") m_CritterLegs.Add(t);
            m_CritterLast = transform.position;
        }

        GameObject BuildBear()
        {
            var go = new GameObject("bear");
            go.transform.SetParent(transform, false);
            var t = go.transform;
            var fur = new Color(0.45f, 0.29f, 0.16f);
            var dark = new Color(0.22f, 0.13f, 0.07f);
            var team = Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, 3)];
            Art.Part(t, Art.Sphere, fur, new Vector3(0, 0.95f, -0.15f), new Vector3(1.0f, 0.85f, 1.75f));    // the body
            Art.Part(t, Art.Sphere, fur * 1.05f, new Vector3(0, 1.18f, 0.5f), new Vector3(0.85f, 0.8f, 0.8f));  // the hump
            for (int s2 = -1; s2 <= 1; s2 += 2) Art.Part(t, Art.Sphere, fur * 0.92f, new Vector3(s2 * 0.3f, 0.95f, -0.75f), new Vector3(0.45f, 0.6f, 0.5f)); // the haunches
            var head = new GameObject("head").transform;
            head.SetParent(t, false);
            head.localPosition = new Vector3(0, 1.42f, 1.0f);
            Art.Part(head, Art.Sphere, fur, Vector3.zero, new Vector3(0.62f, 0.58f, 0.62f));
            Art.Part(head, Art.Sphere, new Color(0.6f, 0.45f, 0.3f), new Vector3(0, -0.08f, 0.3f), new Vector3(0.3f, 0.24f, 0.32f)); // the snout
            Art.Part(head, Art.Sphere, Color.black, new Vector3(0, -0.02f, 0.46f), new Vector3(0.11f, 0.08f, 0.08f));
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Part(head, Art.Sphere, dark, new Vector3(s * 0.22f, 0.28f, -0.02f), new Vector3(0.17f, 0.17f, 0.09f));
                Art.Part(head, Art.Sphere, Color.black, new Vector3(s * 0.13f, 0.1f, 0.27f), Vector3.one * 0.07f);
            }
            Art.Part(t, Art.Cylinder, team, new Vector3(0, 1.25f, 0.85f), new Vector3(0.66f, 0.05f, 0.66f), new Vector3(70, 0, 0)); // a collar in their team's colour
            for (int i = 0; i < 4; i++)
            {
                var leg = new GameObject("leg").transform;
                leg.SetParent(t, false);
                leg.localPosition = new Vector3(i % 2 == 0 ? -0.36f : 0.36f, 0.85f, i < 2 ? 0.55f : -0.65f);
                Art.Part(leg, Art.Capsule, dark * 1.4f, new Vector3(0, -0.42f, 0), new Vector3(0.3f, 0.42f, 0.32f));
                Art.Box(leg, dark, new Vector3(0, -0.82f, 0.06f), new Vector3(0.3f, 0.08f, 0.38f));
            }
            Art.Part(t, Art.Sphere, new Color(0.62f, 0.45f, 0.28f), new Vector3(0, 1.08f, -1.02f), Vector3.one * 0.22f); // the tail
            Art.Part(t, Art.Sphere, fur * 1.15f, new Vector3(0, 1.32f, -0.1f), new Vector3(0.7f, 0.25f, 1.2f)); // (the light along its back)
            t.localScale = Vector3.one * 1.15f;
            return go; // (your own bear is seen too: a bear's view is from behind it)
        }

        GameObject BuildAnt()
        {
            var go = new GameObject("ant");
            go.transform.SetParent(transform, false);
            var t = go.transform;
            var shell = new Color(0.08f, 0.06f, 0.06f);
            Art.Part(t, Art.Sphere, shell, new Vector3(0, 0.07f, -0.1f), new Vector3(0.11f, 0.09f, 0.15f));
            Art.Part(t, Art.Sphere, shell, new Vector3(0, 0.07f, 0.0f), new Vector3(0.06f, 0.06f, 0.08f));
            Art.Part(t, Art.Sphere, shell, new Vector3(0, 0.08f, 0.08f), new Vector3(0.08f, 0.07f, 0.08f));
            Art.Part(t, Art.Sphere, Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, 3)], new Vector3(0, 0.115f, -0.1f), new Vector3(0.05f, 0.02f, 0.05f));
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Box(t, shell, new Vector3(s * 0.025f, 0.13f, 0.13f), new Vector3(0.006f, 0.006f, 0.08f), new Vector3(-40, s * 25f, 0));
                for (int k = 0; k < 3; k++)
                {
                    var leg = new GameObject("leg").transform;
                    leg.SetParent(t, false);
                    leg.localPosition = new Vector3(s * 0.03f, 0.07f, 0.03f - k * 0.04f);
                    Art.Box(leg, shell, new Vector3(s * 0.05f, -0.03f, 0), new Vector3(0.1f, 0.006f, 0.006f), new Vector3(0, 0, s * -35f));
                }
            }
            if (Mine) Art.SetLayerShadowsOnly(go);
            return go;
        }

        GameObject BuildPrison()
        {
            var go = new GameObject("timeout ball");
            go.transform.SetParent(transform, false);
            var ball = Art.Part(go.transform, Art.Sphere, Color.white, new Vector3(0, 1f, 0), Vector3.one * 2.6f, default, false, Art.Ghost(new Color(0.75f, 0.92f, 1f, 0.35f)));
            ball.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var cols = new[] { new Color(1f, 0.3f, 0.3f, 0.45f), new Color(1f, 0.85f, 0.2f, 0.45f), new Color(0.25f, 0.55f, 1f, 0.45f) };
            for (int i = 0; i < 3; i++)
                Art.Part(go.transform, Art.Sphere, Color.white, new Vector3(0, 1f, 0), new Vector3(2.64f, 2.64f, 0.5f), new Vector3(0, i * 60f, 0), false, Art.Ghost(cols[i])).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var txt = new GameObject("countdown");
            txt.transform.SetParent(transform, false);
            txt.transform.localPosition = new Vector3(0, 2.75f, 0);
            txt.transform.SetParent(go.transform, true);
            m_PrisonText = txt.AddComponent<TextMesh>();
            m_PrisonText.anchor = TextAnchor.MiddleCenter;
            m_PrisonText.alignment = TextAlignment.Center;
            m_PrisonText.fontSize = 64;
            m_PrisonText.characterSize = 0.06f;
            m_PrisonText.fontStyle = FontStyle.Bold;
            m_PrisonText.color = new Color(1f, 0.85f, 0.25f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { m_PrisonText.font = font; txt.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            return go;
        }

        GameObject BuildShield()
        {
            var go = Art.Part(transform, Art.Sphere, Color.white, new Vector3(0, 0.95f, 0), Vector3.one * 2.3f, default, false, Art.Ghost(new Color(0.35f, 0.9f, 1f, 0.22f)));
            go.name = "shield";
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void BuildBoots()
        {
            foreach (var foot in new[] { m_Anim.LeftFoot, m_Anim.RightFoot })
            {
                if (foot == null) continue;
                var b = new GameObject("boot");
                m_Boots.Add(b);
                b.transform.SetParent(foot, false);
                b.transform.localScale = Vector3.one / Mathf.Max(0.01f, foot.lossyScale.y);
                b.transform.rotation = m_VisualRoot.rotation;
                b.transform.position = foot.position;
                Art.Box(b.transform, new Color(0.92f, 0.2f, 0.18f), new Vector3(0, -0.02f, 0.04f), new Vector3(0.16f, 0.12f, 0.24f));
                Art.Box(b.transform, Color.white, new Vector3(0, 0.05f, 0.0f), new Vector3(0.17f, 0.03f, 0.17f));
                for (int k = 0; k < 3; k++) Art.Part(b.transform, Art.Cylinder, new Color(0.78f, 0.8f, 0.84f), new Vector3(0, -0.1f - k * 0.025f, 0.04f), new Vector3(0.1f, 0.005f, 0.1f));
                if (Mine) Art.SetLayerShadowsOnly(b);
            }
        }
    }
}
