using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Horses with a bit of life in them: ridden, the body bobs and rocks with the stride (more at a gallop); wild, it hops about like the tree disguise. And now and then a
    /// wild horse is a Wild Unicorn (Cfg.UnicornChance, rolled by the server when the horses are put out, synced to every
    /// screen in Unicorn): white with a gold horn and hooves and a pastel mane, a little faster (UnicornSpeedMul) and
    /// tougher (UnicornHpMul), and at a gallop it leaves a rainbow hanging in the air behind it.
    /// </summary>
    public partial class Vehicle
    {
        public readonly NetworkVariable<bool> Unicorn = new NetworkVariable<bool>();
        public bool IsUnicorn => IsHorse && Unicorn.Value;
        /// <summary>How much faster than a horse it runs (a unicorn is a little quicker).</summary>
        public float SpeedMul => IsUnicorn ? Mathf.Max(0.1f, Cfg.UnicornSpeedMul) : 1f;

        static readonly Color UnicornCoat = new Color(0.96f, 0.96f, 0.98f), UnicornMane = new Color(0.86f, 0.72f, 0.96f), UnicornHorn = new Color(1f, 0.85f, 0.42f);

        /// <summary>The rainbow's stripes, top to bottom.</summary>
        static readonly Color[] s_Rainbow =
        {
            new Color(1f, 0.2f, 0.2f), new Color(1f, 0.6f, 0.1f), new Color(1f, 0.95f, 0.2f),
            new Color(0.25f, 0.9f, 0.3f), new Color(0.25f, 0.55f, 1f), new Color(0.6f, 0.3f, 0.95f),
        };

        /// <summary>For the tests: rainbow pieces left behind (all unicorns, this machine).</summary>
        public static int RainbowPieces { get; private set; }
        /// <summary>How far the body is bobbing up right now (m; tests).</summary>
        public float BobHeight => m_Visual ? m_Visual.localPosition.y : 0f;
        /// <summary>How fast it is really moving forwards (m/s, smoothed; tests).</summary>
        public float AnimSpeed => m_AnimSpeed;

        float m_RainbowDist;

        /// <summary>A gold horn on the forehead, pointing up and forwards, with a spiral of rings round it.</summary>
        static void AddHorn(Transform neck)
        {
            var horn = new GameObject("horn").transform;
            horn.SetParent(neck, false);
            horn.localPosition = new Vector3(0f, 0.76f, 0.56f);
            horn.localRotation = Quaternion.Euler(38f, 0f, 0f);
            Art.Part(horn, Art.Cone, UnicornHorn, Vector3.zero, new Vector3(0.1f, 0.46f, 0.1f));
            for (int i = 0; i < 3; i++)
            {
                float y = 0.07f + i * 0.1f, w = 0.1f * (1f - y / 0.46f) + 0.012f;
                Art.Part(horn, Art.Cylinder, Color.white, new Vector3(0f, y, 0f), new Vector3(w, 0.008f, w), new Vector3(0f, 0f, 8f));
            }
        }

        /// <summary>
        /// Ridden: the body bobs up and down twice a stride and rocks nose-to-tail a little (more at a gallop).
        /// Wild (nobody on it, wandering or bolting): it hops about the way the tree disguise does (PlayerNet.TreeHop) -
        /// up off the ground stretched tall, a squash as it lands, pitching nose-down into the hop - and settles back to
        /// standing when it stops. Driven by how fast it really moves, so every screen sees the same.
        /// </summary>
        void Bob(float v, float dt)
        {
            if (!m_Visual) return;
            if (!m_VisualScaleSet) { m_VisualScale = m_Visual.localScale; m_VisualScaleSet = true; }
            if (!HasDriver) { WildHop(v, dt); return; }
            m_HopPhase = 0f;
            m_HopMove = 0f;
            m_Visual.localScale = Vector3.Lerp(m_Visual.localScale, m_VisualScale, Mathf.Clamp01(dt * 14f));
            float sp = Mathf.Abs(v);
            float k = Mathf.Clamp01(sp / 3f);
            bool gallop = sp > 7f;
            float up = Mathf.Abs(Mathf.Sin(m_Anim)) * (gallop ? 0.12f : 0.06f) * k;
            float rock = Mathf.Sin(m_Anim + 0.6f) * (gallop ? 4.5f : 2f) * k;
            m_Visual.localPosition = Vector3.Lerp(m_Visual.localPosition, new Vector3(0f, up, 0f), Mathf.Clamp01(dt * 20f));
            m_Visual.localRotation = Quaternion.Slerp(m_Visual.localRotation, Quaternion.Euler(rock, 0f, 0f), Mathf.Clamp01(dt * 12f));
        }

        /// <summary>How far a wild horse travels in one hop (m) - a bit shorter than the tree disguise's, it's a smaller thing.</summary>
        public const float HopStride = 2.2f;
        /// <summary>For the tests: hops finished by this horse (this machine).</summary>
        public int Hops { get; private set; }
        float m_HopPhase, m_HopMove, m_HopSquash;
        Vector3 m_VisualScale = Vector3.one;
        bool m_VisualScaleSet;

        void WildHop(float v, float dt)
        {
            if (dt <= 0f) return;
            float speed = Mathf.Abs(v);
            m_HopMove = Mathf.MoveTowards(m_HopMove, Mathf.Clamp01((speed - 0.3f) / 1.5f), dt * 5f);
            if (m_HopMove <= 0.001f && m_HopPhase == 0f)
            {
                // settle back to standing still
                float k = Mathf.Clamp01(dt * 14f);
                m_HopSquash = Mathf.Lerp(m_HopSquash, 0f, k);
                m_Visual.localPosition = Vector3.Lerp(m_Visual.localPosition, Vector3.zero, k);
                m_Visual.localRotation = Quaternion.Slerp(m_Visual.localRotation, Quaternion.identity, k);
                m_Visual.localScale = Vector3.Lerp(m_Visual.localScale, m_VisualScale, k);
                return;
            }
            // a hop every ~2.2 m (quicker when it bolts); finish the hop it's in before settling
            float before = m_HopPhase;
            m_HopPhase += dt * Mathf.Max(speed, 2f) / HopStride * Mathf.PI;
            bool landed = Mathf.Floor(before / Mathf.PI) != Mathf.Floor(m_HopPhase / Mathf.PI);
            if (landed) Hops++;
            if (m_HopMove <= 0.001f && landed) m_HopPhase = 0f;
            if (m_HopPhase > 400f * Mathf.PI) m_HopPhase -= 396f * Mathf.PI; // (keep it small: whole wobbles only)
            float arc = Mathf.Abs(Mathf.Sin(m_HopPhase));
            float hop = m_HopPhase == 0f ? 0f : arc * (0.3f + 0.15f * Mathf.Clamp01(speed / 8f)) * Mathf.Max(m_HopMove, 0.4f);
            // landing squash: squashes as it touches down, springs up long as it takes off
            float target = arc < 0.25f ? -0.14f * (1f - arc / 0.25f) : 0.06f * arc;
            m_HopSquash = Mathf.Lerp(m_HopSquash, target * Mathf.Max(m_HopMove, 0.3f), dt * 18f);
            float sy = 1f + m_HopSquash, sxz = 1f / Mathf.Sqrt(Mathf.Max(0.5f, sy));
            // nose up as it takes off, nose down as it comes in to land, and a little wobble from side to side
            float pitch = -Mathf.Cos(m_HopPhase) * Mathf.Sign(Mathf.Sin(m_HopPhase)) * 8f * m_HopMove;
            float wob = Mathf.Sin(m_HopPhase * 0.5f) * 4f * m_HopMove;
            m_Visual.localPosition = new Vector3(0f, hop, 0f);
            m_Visual.localRotation = Quaternion.Slerp(m_Visual.localRotation, Quaternion.Euler(pitch, 0f, wob), Mathf.Clamp01(dt * 10f));
            m_Visual.localScale = new Vector3(m_VisualScale.x * sxz, m_VisualScale.y * sy, m_VisualScale.z * sxz);
        }

        /// <summary>
        /// Galloping (sprinting) unicorn: a band of rainbow pieces left hanging in the air behind it every few tenths of a
        /// metre, drifting and fading - a rainbow trail. Driven by how fast it really moves, so everyone sees it.
        /// </summary>
        void Rainbow(float v, float dt)
        {
            float sp = Mathf.Abs(v);
            if (sp < Mathf.Max(6.5f, Cfg.HorseWalk * SpeedMul * 1.3f)) { m_RainbowDist = 0f; return; }
            m_RainbowDist += sp * dt;
            const float every = 0.3f;
            int n = 0;
            while (m_RainbowDist >= every && n++ < 4)
            {
                m_RainbowDist -= every;
                // behind the tail, a little lower than the back; each stripe a little lower than the last
                var back = transform.TransformPoint(new Vector3(0f, 1.42f, -0.95f)) - transform.forward * m_RainbowDist;
                for (int i = 0; i < s_Rainbow.Length; i++)
                {
                    var at = back + Vector3.down * (i * 0.11f) + Random.insideUnitSphere * 0.02f;
                    FxParticle.Spawn(at, Vector3.up * 0.15f - transform.forward * 0.4f, s_Rainbow[i], 0.16f, 0.8f, 0f, false);
                    RainbowPieces++;
                }
            }
        }
    }
}
