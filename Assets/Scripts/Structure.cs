using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>A building piece (foundation, wall, doorway, floor, stairs) or a crafting table.</summary>
    public class Structure : NetworkBehaviour
    {
        public static readonly List<Structure> All = new List<Structure>();

        public readonly NetworkVariable<byte> Type = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Tier = new NetworkVariable<byte>();   // 0 wood, 1 stone
        public readonly NetworkVariable<byte> Team = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>();
        public readonly NetworkVariable<bool> DoorOpen = new NetworkVariable<bool>();

        [NonSerialized] public PieceKey Key;
        [NonSerialized] public bool HasKey;

        Transform m_Visual, m_Hinge;
        float m_Rise = 1f, m_DoorAngle;

        public PieceType PType => (PieceType)Type.Value;
        public float MaxHp => Cfg.PieceHp(PType, Tier.Value);
        public string DisplayName => (PType == PieceType.CraftingTable ? "" : (Tier.Value == 1 ? "Stone " : "Wooden ")) + Cfg.PieceName(PType);

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Rebuild();
            m_Rise = 0f;
            Tier.OnValueChanged += OnTierChanged;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Tier.OnValueChanged -= OnTierChanged;
            if (IsServer && HasKey && BuildGrid.Registry.TryGetValue(Key, out var s) && s == this)
                BuildGrid.Registry.Remove(Key);
        }

        void OnTierChanged(byte prev, byte cur)
        {
            Rebuild();
            m_Rise = 0.4f; // little "rebuild" pop
        }

        void Rebuild()
        {
            if (m_Visual) Destroy(m_Visual.gameObject);
            m_Visual = CreateVisual(PType, Tier.Value, transform, true, null, out m_Hinge).transform;
            m_DoorAngle = DoorOpen.Value ? 100f : 0f;
        }

        void Update()
        {
            if (m_Visual && m_Rise < 1f)
            {
                m_Rise = Mathf.Min(1f, m_Rise + Time.deltaTime / 0.9f);
                float e = 1f - (1f - m_Rise) * (1f - m_Rise);
                m_Visual.localScale = new Vector3(1f, Mathf.Lerp(0.05f, 1f, e), 1f);
            }
            if (m_Hinge)
            {
                float target = DoorOpen.Value ? 100f : 0f;
                m_DoorAngle = Mathf.MoveTowards(m_DoorAngle, target, 300f * Time.deltaTime);
                m_Hinge.localRotation = Quaternion.Euler(0, m_DoorAngle, 0);
            }
        }

        // ---------------- Server ----------------

        public void ServerInit(PieceType t, int team, PieceKey key, bool hasKey)
        {
            Type.Value = (byte)t;
            Team.Value = (byte)team;
            Tier.Value = 0;
            Health.Value = Cfg.PieceHp(t, 0);
            Key = key;
            HasKey = hasKey;
        }

        public void ServerDamage(float dmg)
        {
            if (!IsServer || !IsSpawned || dmg <= 0) return;
            Health.Value = Mathf.Max(0, Health.Value - dmg);
            if (Health.Value <= 0)
            {
                NetworkObject.Despawn(true);
                if (NetGame.Instance) NetGame.Instance.ServerCollapseCheck();
            }
        }

        public void ServerUpgrade()
        {
            Tier.Value = 1;
            Health.Value = MaxHp;
        }

        // ---------------- Visuals (also used for placement ghosts) ----------------

        public static GameObject CreateVisual(PieceType t, int tier, Transform parent, bool colliders, Material ghost, out Transform hinge)
        {
            hinge = null;
            var root = new GameObject("visual");
            root.transform.SetParent(parent, false);
            var tr = root.transform;
            bool stone = tier == 1;
            Color c = stone ? Art.Stone : Art.Wood;
            Color trim = stone ? new Color(0.42f, 0.42f, 0.46f) : Art.DarkWood;
            bool col = colliders;

            switch (t)
            {
                case PieceType.Foundation:
                    Art.Box(tr, c, new Vector3(0, 0.5f, 0), new Vector3(3, 1, 3), default, col);
                    Art.Box(tr, trim, new Vector3(0, 0.85f, 0), new Vector3(3.02f, 0.12f, 3.02f));
                    Art.Box(tr, trim, new Vector3(0, 0.3f, 0), new Vector3(3.02f, 0.12f, 3.02f));
                    break;
                case PieceType.Wall:
                    Art.Box(tr, c, new Vector3(0, 1.5f, 0), new Vector3(3, 3, 0.3f), default, col);
                    for (int k = 1; k < 4; k++)
                        Art.Box(tr, trim, new Vector3(0, k * 0.75f, 0), new Vector3(2.98f, 0.07f, 0.34f));
                    if (!stone)
                    {
                        Art.Box(tr, trim, new Vector3(-1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                        Art.Box(tr, trim, new Vector3(1.2f, 1.5f, 0), new Vector3(0.12f, 2.95f, 0.36f));
                    }
                    break;
                case PieceType.Doorway:
                {
                    Art.Box(tr, c, new Vector3(-1.05f, 1.5f, 0), new Vector3(0.9f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(1.05f, 1.5f, 0), new Vector3(0.9f, 3, 0.3f), default, col);
                    Art.Box(tr, c, new Vector3(0, 2.7f, 0), new Vector3(1.2f, 0.6f, 0.3f), default, col);
                    Art.Box(tr, trim, new Vector3(0, 2.42f, 0), new Vector3(1.3f, 0.08f, 0.34f));
                    var h = new GameObject("hinge").transform;
                    h.SetParent(tr, false);
                    h.localPosition = new Vector3(-0.6f, 0, 0);
                    Art.Box(h, stone ? Art.Metal : Art.DarkWood, new Vector3(0.6f, 1.2f, 0), new Vector3(1.18f, 2.38f, 0.12f), default, col);
                    Art.Box(h, Art.Metal, new Vector3(1.0f, 1.1f, 0.1f), new Vector3(0.08f, 0.2f, 0.08f));
                    Art.Box(h, Art.Metal, new Vector3(1.0f, 1.1f, -0.1f), new Vector3(0.08f, 0.2f, 0.08f));
                    hinge = h;
                    break;
                }
                case PieceType.Floor:
                    Art.Box(tr, c, new Vector3(0, -0.125f, 0), new Vector3(3, 0.25f, 3), default, col);
                    Art.Box(tr, trim, new Vector3(0, -0.2f, 0), new Vector3(3.02f, 0.1f, 0.2f));
                    Art.Box(tr, trim, new Vector3(0, -0.2f, 0), new Vector3(0.2f, 0.1f, 3.02f));
                    break;
                case PieceType.Stairs:
                {
                    float len = Mathf.Sqrt(18f);
                    Art.Box(tr, c, new Vector3(0, 1.5f, 0), new Vector3(2.6f, 0.25f, len), new Vector3(-45, 0, 0), col);
                    Art.Box(tr, trim, new Vector3(-1.35f, 1.5f, 0), new Vector3(0.15f, 0.5f, len), new Vector3(-45, 0, 0));
                    Art.Box(tr, trim, new Vector3(1.35f, 1.5f, 0), new Vector3(0.15f, 0.5f, len), new Vector3(-45, 0, 0));
                    for (int k = 0; k < 6; k++)
                    {
                        float f = (k + 0.5f) / 6f;
                        Art.Box(tr, trim, new Vector3(0, f * 3f + 0.12f, -1.5f + f * 3f), new Vector3(2.5f, 0.06f, 0.1f));
                    }
                    break;
                }
                case PieceType.CraftingTable:
                    Art.Box(tr, Art.Wood, new Vector3(0, 0.85f, 0), new Vector3(1.6f, 0.15f, 0.9f));
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Art.Box(tr, Art.DarkWood, new Vector3(sx * 0.7f, 0.4f, sz * 0.35f), new Vector3(0.12f, 0.8f, 0.12f));
                    Art.Box(tr, Art.Metal, new Vector3(0.4f, 0.98f, 0), new Vector3(0.5f, 0.1f, 0.3f));
                    Art.Box(tr, Art.Stone, new Vector3(-0.45f, 1.02f, 0.1f), new Vector3(0.3f, 0.2f, 0.3f), new Vector3(0, 30, 0));
                    Art.Box(tr, Art.DarkWood, new Vector3(0, 0.25f, 0), new Vector3(1.4f, 0.06f, 0.7f));
                    if (col)
                    {
                        var bc = root.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 0.55f, 0);
                        bc.size = new Vector3(1.6f, 1.1f, 0.9f);
                    }
                    break;
            }

            if (ghost != null)
            {
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = ghost;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            return root;
        }
    }
}
