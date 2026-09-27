using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>A tree (wood) or boulder (stone). Depletes when harvested and regrows later.</summary>
    public class ResourceNode : NetworkBehaviour
    {
        public const byte Tree = 0, Boulder = 1;

        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> Amount = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Seed = new NetworkVariable<int>();

        GameObject m_Visual;
        float m_RespawnAt, m_Shake;
        Vector3 m_VisualBase;

        public int MaxAmount => Kind.Value == Tree ? Cfg.TreeAmount : Cfg.StoneAmount;
        public string DisplayName => Kind.Value == Tree ? "Tree" : "Stone";

        public override void OnNetworkSpawn()
        {
            BuildVisual();
            Amount.OnValueChanged += OnAmountChanged;
            RefreshState();
        }

        public override void OnNetworkDespawn()
        {
            Amount.OnValueChanged -= OnAmountChanged;
        }

        void OnAmountChanged(int prev, int cur)
        {
            if (cur < prev) m_Shake = 0.25f;
            RefreshState();
        }

        void RefreshState()
        {
            bool alive = Amount.Value > 0;
            if (m_Visual.activeSelf != alive) m_Visual.SetActive(alive);
            if (alive && Kind.Value == Boulder)
            {
                float s = Mathf.Lerp(0.55f, 1f, Amount.Value / (float)MaxAmount);
                m_Visual.transform.localScale = Vector3.one * s;
            }
        }

        void BuildVisual()
        {
            var rng = new System.Random(Seed.Value);
            float r() => (float)rng.NextDouble();
            m_Visual = new GameObject("visual");
            m_Visual.transform.SetParent(transform, false);
            var tr = m_Visual.transform;
            if (Kind.Value == Tree)
            {
                float h = 4.5f + r() * 2.5f;
                var trunk = Art.Part(tr, Art.Cylinder, Art.DarkWood, new Vector3(0, h * 0.5f, 0), new Vector3(0.6f, h * 0.5f, 0.6f), default, true, null, "trunk");
                Color leaf = Color.Lerp(Art.Leaves, new Color(0.3f, 0.55f, 0.2f), r());
                int layers = 3;
                for (int k = 0; k < layers; k++)
                {
                    float y = h * 0.45f + k * 1.4f;
                    float w = 3.6f - k * 0.9f;
                    Art.Part(tr, Art.Cone, leaf, new Vector3(0, y, 0), new Vector3(w, 2.4f, w), new Vector3(0, r() * 60f, 0));
                }
                trunk.name = "trunk";
            }
            else
            {
                var mesh = Art.MakeRock(Seed.Value, 0.28f);
                Color c = Color.Lerp(Art.Stone, new Color(0.45f, 0.44f, 0.42f), r());
                var main = Art.Part(tr, mesh, c, new Vector3(0, 0.6f, 0), new Vector3(1.6f, 1.2f, 1.4f), new Vector3(r() * 30, r() * 360, r() * 20), false, null, "rock");
                var sc = main.AddComponent<SphereCollider>();
                sc.radius = 0.95f;
                Art.Part(tr, Art.MakeRock(Seed.Value + 7, 0.3f), c * 0.9f, new Vector3(0.9f, 0.3f, 0.5f), Vector3.one * 0.6f, new Vector3(0, r() * 360, 0));
                // light ore streaks so boulders read as "stone node"
                Art.Box(tr, new Color(0.75f, 0.72f, 0.6f), new Vector3(0.2f, 1.25f, 0.2f), new Vector3(0.25f, 0.1f, 0.25f), new Vector3(20, 45, 10));
            }
            m_VisualBase = tr.localPosition;
        }

        void Update()
        {
            if (m_Shake > 0 && m_Visual)
            {
                m_Shake -= Time.deltaTime;
                float k = Mathf.Max(0, m_Shake) * 0.4f;
                m_Visual.transform.localPosition = m_VisualBase + new Vector3(Mathf.Sin(Time.time * 70f) * k, 0, Mathf.Cos(Time.time * 55f) * k);
            }
            else if (m_Visual) m_Visual.transform.localPosition = m_VisualBase;

            if (IsServer && Amount.Value <= 0 && Time.time >= m_RespawnAt)
                Amount.Value = MaxAmount;
        }

        // ---------------- Server ----------------
        public void ServerInit(byte kind, int seed)
        {
            Kind.Value = kind;
            Seed.Value = seed;
            Amount.Value = kind == Tree ? Cfg.TreeAmount : Cfg.StoneAmount;
        }

        /// <summary>Returns how much was actually harvested.</summary>
        public int ServerHarvest(int want)
        {
            if (Amount.Value <= 0) return 0;
            int got = Mathf.Min(want, Amount.Value);
            Amount.Value -= got;
            if (Amount.Value <= 0) m_RespawnAt = Time.time + Cfg.NodeRespawnTime;
            return got;
        }
    }
}
