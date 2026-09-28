using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A tree (wood), boulder (stone) or berry bush. Trees and boulders have a Rust-style weak spot
    /// (an X on trees, a sparkle on rocks) that gives bonus resources and jumps somewhere else when hit.
    /// Depleted nodes regrow later.
    /// </summary>
    public class ResourceNode : NetworkBehaviour
    {
        public const byte Tree = 0, Boulder = 1, Bush = 2;
        const int SpotCount = 12;

        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> Amount = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Seed = new NetworkVariable<int>();
        public readonly NetworkVariable<byte> Spot = new NetworkVariable<byte>();

        GameObject m_Visual;
        Transform m_Marker;
        Collider m_SpotCollider;
        GameObject[] m_Berries;
        float m_RespawnAt, m_Shake, m_MarkerPop = 1f;
        Vector3 m_VisualBase;

        public bool IsBush => Kind.Value == Bush;
        public int MaxAmount => Kind.Value == Tree ? Cfg.TreeAmount : Kind.Value == Boulder ? Cfg.StoneAmount : Cfg.BushBerries;
        public string DisplayName => Kind.Value == Tree ? "Tree" : Kind.Value == Boulder ? "Stone" : "Berry Bush";
        public Item Yield => Kind.Value == Tree ? Item.Wood : Kind.Value == Boulder ? Item.Stone : Item.Berry;

        public override void OnNetworkSpawn()
        {
            BuildVisual();
            Amount.OnValueChanged += OnAmountChanged;
            Spot.OnValueChanged += OnSpotChanged;
            RefreshState();
            PlaceMarker();
        }

        public override void OnNetworkDespawn()
        {
            Amount.OnValueChanged -= OnAmountChanged;
            Spot.OnValueChanged -= OnSpotChanged;
        }

        void OnAmountChanged(int prev, int cur)
        {
            if (cur < prev && !IsBush) m_Shake = 0.3f;
            RefreshState();
        }

        void OnSpotChanged(byte prev, byte cur)
        {
            m_MarkerPop = 0f;
            PlaceMarker();
        }

        void RefreshState()
        {
            bool alive = Amount.Value > 0;
            if (IsBush)
            {
                // the bush stays; its berries disappear as they're picked
                for (int i = 0; i < m_Berries.Length; i++)
                    m_Berries[i].SetActive(i < Mathf.CeilToInt(m_Berries.Length * Amount.Value / (float)Mathf.Max(1, MaxAmount)));
                return;
            }
            if (m_Visual.activeSelf != alive) m_Visual.SetActive(alive);
            if (alive && Kind.Value == Boulder)
            {
                float s = Mathf.Lerp(0.55f, 1f, Amount.Value / (float)MaxAmount);
                m_Visual.transform.localScale = Vector3.one * s;
                PlaceMarker();
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
                m_SpotCollider = trunk.GetComponent<Collider>();
                Color leaf = Color.Lerp(Art.Leaves, new Color(0.3f, 0.55f, 0.2f), r());
                for (int k = 0; k < 3; k++)
                {
                    float y = h * 0.45f + k * 1.4f;
                    float w = 3.6f - k * 0.9f;
                    Art.Part(tr, Art.Cone, leaf, new Vector3(0, y, 0), new Vector3(w, 2.4f, w), new Vector3(0, r() * 60f, 0));
                }
                // the X
                m_Marker = new GameObject("x").transform;
                m_Marker.SetParent(tr, false);
                var xc = new Color(1f, 0.45f, 0.1f);
                Art.Box(m_Marker, xc, Vector3.zero, new Vector3(0.34f, 0.06f, 0.02f), new Vector3(0, 0, 45));
                Art.Box(m_Marker, xc, Vector3.zero, new Vector3(0.34f, 0.06f, 0.02f), new Vector3(0, 0, -45));
            }
            else if (Kind.Value == Boulder)
            {
                var mesh = Art.MakeRock(Seed.Value, 0.28f);
                Color c = Color.Lerp(Art.Stone, new Color(0.45f, 0.44f, 0.42f), r());
                var main = Art.Part(tr, mesh, c, new Vector3(0, 0.6f, 0), new Vector3(1.6f, 1.2f, 1.4f), new Vector3(r() * 30, r() * 360, r() * 20), false, null, "rock");
                var mc = main.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
                m_SpotCollider = mc;
                Art.Part(tr, Art.MakeRock(Seed.Value + 7, 0.3f), c * 0.9f, new Vector3(0.9f, 0.3f, 0.5f), Vector3.one * 0.6f, new Vector3(0, r() * 360, 0));
                // the sparkle star
                m_Marker = new GameObject("star").transform;
                m_Marker.SetParent(tr, false);
                var sc = new Color(1f, 0.95f, 0.55f);
                for (int k = 0; k < 4; k++)
                    Art.Box(m_Marker, sc, Vector3.zero, new Vector3(k % 2 == 0 ? 0.34f : 0.2f, 0.04f, 0.02f), new Vector3(0, 0, k * 45f));
                Art.Part(m_Marker, Art.Ico, Color.white, Vector3.zero, Vector3.one * 0.05f);
            }
            else
            {
                Color leaf = Color.Lerp(ItemModels.Leaf, new Color(0.18f, 0.4f, 0.16f), r());
                Art.Part(tr, Art.MakeRock(Seed.Value, 0.2f), leaf, new Vector3(0, 0.45f, 0), new Vector3(0.75f, 0.5f, 0.7f), new Vector3(0, r() * 360, 0));
                Art.Part(tr, Art.MakeRock(Seed.Value + 1, 0.2f), leaf * 0.9f, new Vector3(0.4f, 0.35f, 0.2f), new Vector3(0.45f, 0.38f, 0.45f));
                Art.Part(tr, Art.MakeRock(Seed.Value + 2, 0.2f), leaf * 1.1f, new Vector3(-0.35f, 0.3f, -0.2f), new Vector3(0.45f, 0.35f, 0.4f));
                m_Berries = new GameObject[8];
                for (int i = 0; i < m_Berries.Length; i++)
                {
                    float a = i * 0.8f + r();
                    float y = 0.35f + r() * 0.45f;
                    var p = new Vector3(Mathf.Cos(a) * 0.72f, y, Mathf.Sin(a) * 0.68f);
                    m_Berries[i] = Art.Part(tr, Art.Sphere, ItemModels.Berry, p, Vector3.one * 0.13f);
                }
                // interaction only (you walk through bushes)
                var sc = gameObject.AddComponent<SphereCollider>();
                sc.isTrigger = true;
                sc.center = new Vector3(0, 0.5f, 0);
                sc.radius = 0.8f;
            }
            m_VisualBase = tr.localPosition;
        }

        /// <summary>Local-space direction and height of weak spot `i` (a ring around the trunk / rock).</summary>
        static void SpotDir(byte kind, int i, out Vector3 dir, out float y)
        {
            float a = i * (360f / SpotCount) * Mathf.Deg2Rad;
            dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            y = kind == Tree ? 0.8f + (i % 3) * 0.35f : 0.45f + (i % 3) * 0.2f;
        }

        /// <summary>World position/normal of the current weak spot (on the actual surface).</summary>
        public bool TryGetSpot(out Vector3 pos, out Vector3 normal)
        {
            pos = normal = default;
            if (IsBush || m_SpotCollider == null || Amount.Value <= 0) return false;
            SpotDir(Kind.Value, Spot.Value, out var dir, out var y);
            var worldDir = transform.rotation * dir;
            var center = transform.position + Vector3.up * (y * (Kind.Value == Boulder ? m_Visual.transform.localScale.y : 1f));
            var ray = new Ray(center + worldDir * 4f, -worldDir);
            if (!m_SpotCollider.Raycast(ray, out var hit, 8f)) return false;
            pos = hit.point;
            normal = hit.normal;
            return true;
        }

        public bool IsWeakSpotHit(Vector3 point, float tolerance)
        {
            return TryGetSpot(out var p, out _) && Vector3.Distance(p, point) <= tolerance;
        }

        void PlaceMarker()
        {
            if (m_Marker == null) return;
            if (!TryGetSpot(out var p, out var n)) { m_Marker.gameObject.SetActive(false); return; }
            m_Marker.gameObject.SetActive(true);
            m_Marker.position = p + n * 0.015f;
            m_Marker.rotation = Quaternion.LookRotation(-n);
        }

        void Update()
        {
            if (m_Shake > 0 && m_Visual)
            {
                m_Shake -= Time.deltaTime;
                float k = Mathf.Max(0, m_Shake) * 0.4f;
                m_Visual.transform.localPosition = m_VisualBase + new Vector3(Mathf.Sin(Time.time * 70f) * k, 0, Mathf.Cos(Time.time * 55f) * k);
                PlaceMarker();
            }
            else if (m_Visual && m_Visual.transform.localPosition != m_VisualBase)
            {
                m_Visual.transform.localPosition = m_VisualBase;
                PlaceMarker();
            }

            if (m_Marker != null && m_Marker.gameObject.activeSelf)
            {
                // pop in when it moves, then pulse (X) / twinkle (star)
                m_MarkerPop = Mathf.Min(1f, m_MarkerPop + Time.deltaTime * 5f);
                float pop = 1f + (1f - m_MarkerPop) * 1.2f;
                float pulse = Kind.Value == Tree ? 1f + Mathf.Sin(Time.time * 6f) * 0.08f : 0.85f + Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 0.35f;
                m_Marker.localScale = Vector3.one * pop * pulse;
                if (Kind.Value == Boulder) m_Marker.rotation *= Quaternion.Euler(0, 0, Time.deltaTime * 90f);
            }

            if (IsServer && Amount.Value <= 0 && Time.time >= m_RespawnAt)
                Amount.Value = MaxAmount;
        }

        // ---------------- Server ----------------
        public void ServerInit(byte kind, int seed)
        {
            Kind.Value = kind;
            Seed.Value = seed;
            Spot.Value = (byte)(seed % SpotCount);
            Amount.Value = kind == Tree ? Cfg.TreeAmount : kind == Boulder ? Cfg.StoneAmount : Cfg.BushBerries;
        }

        /// <summary>Returns how much was actually harvested. A weak-spot hit multiplies the yield and moves the spot.</summary>
        public int ServerHarvest(int want, bool weak, Vector3 hitterPos)
        {
            if (Amount.Value <= 0) return 0;
            if (weak) want = Mathf.RoundToInt(want * Cfg.WeakSpotMul);
            int got = Mathf.Min(want, Amount.Value);
            Amount.Value -= got;
            if (Amount.Value <= 0) m_RespawnAt = Time.time + (IsBush ? Cfg.BushRespawnTime : Cfg.NodeRespawnTime);
            if (weak) Spot.Value = PickSpotFacing(hitterPos);
            return got;
        }

        /// <summary>New weak spot on the side facing the player (like Rust), but never the same one.</summary>
        byte PickSpotFacing(Vector3 hitterPos)
        {
            var toHitter = Quaternion.Inverse(transform.rotation) * (hitterPos - transform.position);
            toHitter.y = 0;
            float baseAng = Mathf.Atan2(toHitter.z, toHitter.x) * Mathf.Rad2Deg;
            for (int tries = 0; tries < 10; tries++)
            {
                float a = baseAng + Random.Range(-70f, 70f);
                int i = Mathf.RoundToInt(Mathf.Repeat(a, 360f) / (360f / SpotCount)) % SpotCount;
                if (i != Spot.Value) return (byte)i;
            }
            return (byte)((Spot.Value + 1) % SpotCount);
        }
    }
}
