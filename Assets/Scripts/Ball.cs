using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>The objective ball. Server simulates physics; carrying is server-driven.</summary>
    public class Ball : NetworkBehaviour
    {
        public const ulong NoCarrier = ulong.MaxValue;
        public static Ball Instance;

        public readonly NetworkVariable<ulong> CarrierId = new NetworkVariable<ulong>(NoCarrier);
        public readonly NetworkVariable<sbyte> BaseTeam = new NetworkVariable<sbyte>(-1);

        Rigidbody m_Rb;
        Collider m_Col;
        Transform m_Visual;
        GameObject m_Beacon;
        Renderer m_BeaconRenderer;
        Light m_Light;

        public bool IsCarried => CarrierId.Value != NoCarrier;

        public PlayerNet Carrier
        {
            get
            {
                if (!IsCarried || NetworkManager == null) return null;
                return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(CarrierId.Value, out var no) ? no.GetComponent<PlayerNet>() : null;
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            m_Rb = GetComponent<Rigidbody>();
            m_Col = GetComponent<Collider>();
            m_Rb.isKinematic = !IsServer;
            m_Rb.interpolation = IsServer ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;

            m_Visual = new GameObject("visual").transform;
            m_Visual.SetParent(transform, false);
            Art.Part(m_Visual, Art.Ico, new Color(1f, 0.85f, 0.15f), Vector3.zero, Vector3.one * 0.62f, default, false, null, "ball");
            Art.Box(m_Visual, new Color(0.9f, 0.5f, 0.1f), Vector3.zero, new Vector3(1.28f, 0.12f, 0.12f));
            Art.Box(m_Visual, new Color(0.9f, 0.5f, 0.1f), Vector3.zero, new Vector3(0.12f, 0.12f, 1.28f));

            var lightGo = new GameObject("glow");
            lightGo.transform.SetParent(m_Visual, false);
            m_Light = lightGo.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.color = new Color(1f, 0.85f, 0.3f);
            m_Light.range = 8f;
            m_Light.intensity = 3f;

            // Beacon: a tall translucent pillar of light that shoots to the sky when the ball sits in a base
            m_Beacon = new GameObject("beacon");
            m_Beacon.transform.SetParent(transform, false);
            var pillar = Art.Part(m_Beacon.transform, Art.Cylinder, Color.white, new Vector3(0, 150, 0), new Vector3(1.4f, 150, 1.4f), default, false, Art.Ghost(new Color(1, 1, 1, 0.35f)), "pillar");
            pillar.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_BeaconRenderer = pillar.GetComponent<MeshRenderer>();
            BaseTeam.OnValueChanged += OnBaseTeamChanged;
            OnBaseTeamChanged(-1, BaseTeam.Value);
        }

        public override void OnNetworkDespawn()
        {
            BaseTeam.OnValueChanged -= OnBaseTeamChanged;
            if (Instance == this) Instance = null;
        }

        void OnBaseTeamChanged(sbyte prev, sbyte cur)
        {
            m_Beacon.SetActive(cur >= 0);
            if (cur >= 0)
            {
                var c = Cfg.TeamColor[cur];
                m_BeaconRenderer.sharedMaterial = Art.Ghost(new Color(c.r, c.g, c.b, 0.4f));
                m_Light.color = c;
                m_Light.range = 14f;
                m_Light.intensity = 6f;
            }
            else
            {
                m_Light.color = new Color(1f, 0.85f, 0.3f);
                m_Light.range = 8f;
                m_Light.intensity = 3f;
            }
        }

        void Update()
        {
            // keep the beacon pointing straight up even while the ball rolls
            if (m_Beacon) m_Beacon.transform.rotation = Quaternion.identity;
            m_Col.enabled = !IsCarried;

            if (IsServer) ServerTick();
        }

        void LateUpdate()
        {
            // Clients: render the carried ball exactly over the carrier's head (avoids interpolation lag)
            var carrier = Carrier;
            if (carrier != null)
            {
                m_Visual.position = carrier.transform.position + Vector3.up * 2.4f;
                m_Visual.rotation = Quaternion.Euler(0, Time.time * 90f, 0);
            }
            else
            {
                m_Visual.localPosition = Vector3.zero;
                m_Visual.localRotation = Quaternion.identity;
            }
        }

        void ServerTick()
        {
            if (IsCarried)
            {
                var c = Carrier;
                if (c == null || c.Dead.Value)
                {
                    ServerDrop(c != null ? c.transform.position + Vector3.up * 1.2f : transform.position, Vector3.up * 2f);
                }
                else
                {
                    m_Rb.isKinematic = true;
                    transform.position = c.transform.position + Vector3.up * 2.4f;
                }
            }

            var p = transform.position;
            if (p.y < -20f || Mathf.Abs(p.x) > Cfg.MapHalf + 5 || Mathf.Abs(p.z) > Cfg.MapHalf + 5)
            {
                ServerReset();
                p = transform.position;
            }

            sbyte team = -1;
            if (!IsCarried && p.y < 30f) team = (sbyte)Cfg.BaseTeamAt(p);
            if (BaseTeam.Value != team) BaseTeam.Value = team;
        }

        public void ServerReset()
        {
            CarrierId.Value = NoCarrier;
            m_Rb.isKinematic = false;
            transform.position = Cfg.BallDropPoint;
            m_Rb.linearVelocity = Vector3.zero;
            m_Rb.angularVelocity = Vector3.zero;
        }

        public bool ServerPickup(PlayerNet p)
        {
            if (IsCarried) return false;
            CarrierId.Value = p.NetworkObjectId;
            m_Rb.isKinematic = true;
            return true;
        }

        public void ServerDrop(Vector3 pos, Vector3 vel)
        {
            CarrierId.Value = NoCarrier;
            transform.position = pos;
            m_Rb.isKinematic = false;
            m_Rb.linearVelocity = Vector3.ClampMagnitude(vel, 12f);
        }
    }
}
