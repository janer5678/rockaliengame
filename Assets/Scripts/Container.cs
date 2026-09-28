using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A lootable slot container: a storage chest placed in a base (anyone who gets to it can use it,
    /// so it can be raided) or the bag a player drops on death (take-only).
    /// </summary>
    public class Container : NetworkBehaviour
    {
        public const byte Chest = 0, Bag = 1;
        public static readonly List<Container> All = new List<Container>();

        public readonly NetworkList<ItemStack> Slots = new NetworkList<ItemStack>();
        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>();

        public bool IsBag => Kind.Value == Bag;
        public bool TakeOnly => IsBag;
        public string DisplayName => IsBag ? $"{Cfg.TeamName[Mathf.Clamp(Team.Value, 0, 1)]}'s loot bag" : "Storage Chest";
        public Vector3 Center => transform.position + Vector3.up * (IsBag ? 0.3f : 0.4f);

        readonly List<ItemStack> m_Pending = new List<ItemStack>();
        int m_PendingSize;
        double m_DespawnAt;
        float m_Pop = 1f;
        Transform m_Visual;

        // ---------------- Server setup (before Spawn) ----------------
        public void ServerInit(byte kind, int team, int size, List<ItemStack> contents)
        {
            Kind.Value = kind;
            Team.Value = (byte)team;
            Health.Value = kind == Chest ? Cfg.ChestHp : 1f;
            m_PendingSize = size;
            m_Pending.Clear();
            if (contents != null) m_Pending.AddRange(contents);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                for (int i = 0; i < m_PendingSize; i++) Slots.Add(i < m_Pending.Count ? m_Pending[i] : default);
                m_DespawnAt = NetworkManager.ServerTime.Time + Cfg.BagLifetime;
            }
            m_Visual = CreateVisual(Kind.Value, Team.Value, transform, null).transform;
            var bc = gameObject.AddComponent<BoxCollider>();
            if (IsBag) { bc.center = new Vector3(0, 0.3f, 0); bc.size = new Vector3(0.7f, 0.6f, 0.7f); }
            else { bc.center = new Vector3(0, 0.33f, 0); bc.size = new Vector3(1.1f, 0.66f, 0.62f); }
            m_Pop = 0f;
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        public static GameObject CreateVisual(byte kind, int team, Transform parent, Material ghost)
        {
            var root = new GameObject("visual");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            if (kind == Bag)
            {
                var sack = new Color(0.45f, 0.36f, 0.24f);
                Art.Part(t, Art.Sphere, sack, new Vector3(0, 0.28f, 0), new Vector3(0.7f, 0.55f, 0.62f));
                Art.Part(t, Art.Sphere, sack * 0.9f, new Vector3(0, 0.55f, 0), new Vector3(0.28f, 0.22f, 0.28f));
                Art.Part(t, Art.Cylinder, Cfg.TeamColor[Mathf.Clamp(team, 0, 1)], new Vector3(0, 0.5f, 0), new Vector3(0.24f, 0.03f, 0.24f));
            }
            else
            {
                Art.Box(t, Art.Wood, new Vector3(0, 0.25f, 0), new Vector3(1.05f, 0.5f, 0.58f));
                Art.Box(t, Art.DarkWood, new Vector3(0, 0.56f, 0), new Vector3(1.1f, 0.14f, 0.62f)); // lid
                Art.Box(t, Art.Metal, new Vector3(-0.35f, 0.33f, 0), new Vector3(0.06f, 0.68f, 0.64f));
                Art.Box(t, Art.Metal, new Vector3(0.35f, 0.33f, 0), new Vector3(0.06f, 0.68f, 0.64f));
                Art.Box(t, new Color(0.85f, 0.7f, 0.25f), new Vector3(0, 0.45f, 0.31f), new Vector3(0.12f, 0.14f, 0.04f)); // latch
            }
            if (ghost != null)
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = ghost;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            return root;
        }

        void Update()
        {
            if (m_Pop < 1f && m_Visual)
            {
                m_Pop = Mathf.Min(1f, m_Pop + Time.deltaTime * 4f);
                float s = 1f + Mathf.Sin(m_Pop * Mathf.PI) * 0.25f;
                m_Visual.localScale = new Vector3(s, Mathf.Lerp(0.3f, 1f, m_Pop) * s, s);
            }
            if (!IsServer || !IsBag) return;
            bool empty = true;
            for (int i = 0; i < Slots.Count; i++) if (!Slots[i].Empty) { empty = false; break; }
            if (empty || NetworkManager.ServerTime.Time >= m_DespawnAt) NetworkObject.Despawn(true);
        }

        public bool InReach(Vector3 eye) => Vector3.Distance(eye, Center) <= Cfg.LootRange + 1.5f;

        // ---------------- Server ----------------

        public void ServerDamage(float dmg)
        {
            if (!IsServer || !IsSpawned || IsBag || dmg <= 0) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0) ServerBreak();
        }

        /// <summary>A destroyed chest spills its contents into a bag.</summary>
        public void ServerBreak()
        {
            var items = new List<ItemStack>();
            for (int i = 0; i < Slots.Count; i++) if (!Slots[i].Empty) items.Add(Slots[i]);
            if (items.Count > 0) SpawnBag(transform.position, Team.Value, items);
            Fx.Server(FxKind.Break, transform.position + Vector3.up * 0.4f, Vector3.up);
            NetworkObject.Despawn(true);
        }

        public static void SpawnBag(Vector3 pos, int team, List<ItemStack> items)
        {
            if (Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, out var hit, 50f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            var go = Instantiate(Bootstrap.I.containerPrefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            go.GetComponent<Container>().ServerInit(Bag, team, Mathf.Max(items.Count, 7), items);
            go.GetComponent<NetworkObject>().Spawn(true);
        }
    }
}
