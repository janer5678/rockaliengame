using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The objective ball. Server simulates physics; carrying is server-driven.
    /// To win it has to sit in the socket of your base's alien machine: throw it (LMB) near the socket and it snaps in.
    /// </summary>
    public class Ball : NetworkBehaviour
    {
        public const ulong NoCarrier = ulong.MaxValue;
        public static Ball Instance;

        public readonly NetworkVariable<ulong> CarrierId = new NetworkVariable<ulong>(NoCarrier);
        public readonly NetworkVariable<sbyte> BaseTeam = new NetworkVariable<sbyte>(-1);
        /// <summary>Team whose machine socket the ball sits in (-1 = none). This is what wins the game.</summary>
        public readonly NetworkVariable<sbyte> SocketTeam = new NetworkVariable<sbyte>(-1);

        Rigidbody m_Rb;
        Collider m_Col;
        Transform m_Visual;
        GameObject m_Mesh;
        GameObject m_Beacon;
        Renderer m_BeaconRenderer;
        Light m_Light;
        Collider m_IgnoredCol, m_IgnoredMount;
        float m_IgnoreUntil;

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
            m_Mesh = ItemModels.CreateBall(m_Visual, 1.24f);

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
            SocketTeam.OnValueChanged += OnSocketChanged;
            OnSocketChanged(-1, SocketTeam.Value);
        }

        public override void OnNetworkDespawn()
        {
            SocketTeam.OnValueChanged -= OnSocketChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>The beacon only shoots up while the ball sits in a machine socket.</summary>
        void OnSocketChanged(sbyte prev, sbyte cur)
        {
            if (cur >= 0 && prev != cur)
            {
                Sfx.Play(Sfx.Zap, transform.position, 1f);
                Sfx.Play(Sfx.Ding, transform.position, 0.8f);
            }
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
            // Clients: render the carried ball in the carrier's arms (avoids interpolation lag).
            // The carrier themselves sees it in their first-person hands instead.
            var carrier = Carrier;
            if (carrier != null)
            {
                m_Visual.position = CarryPoint(carrier);
                m_Visual.rotation = carrier.transform.rotation;
            }
            else
            {
                m_Visual.localPosition = Vector3.zero;
                m_Visual.localRotation = Quaternion.identity;
            }
            bool show = carrier == null || !carrier.IsOwner;
            if (m_Mesh.activeSelf != show) m_Mesh.SetActive(show);
        }

        void ServerTick()
        {
            if ((m_IgnoredCol != null || m_IgnoredMount != null) && Time.time > m_IgnoreUntil)
            {
                if (m_IgnoredCol != null) Physics.IgnoreCollision(m_Col, m_IgnoredCol, false);
                if (m_IgnoredMount != null) Physics.IgnoreCollision(m_Col, m_IgnoredMount, false);
                m_IgnoredCol = m_IgnoredMount = null;
            }
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
                    transform.position = CarryPoint(c);
                }
            }

            var p = transform.position;
            if (!IsCarried && SocketTeam.Value < 0)
            {
                // rolled or thrown into a machine's socket: it locks in
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    var sp = Cfg.SocketPos(t);
                    var d = p - sp;
                    if (new Vector2(d.x, d.z).magnitude < 1.4f && d.y < 1.6f && d.y > -0.7f && m_Rb.linearVelocity.magnitude < 22f)
                    {
                        ServerSocket(t);
                        p = transform.position;
                        break;
                    }
                }
            }
            if (p.y < -20f || Mathf.Abs(p.x) > Cfg.MapHalf + 5 || Mathf.Abs(p.z) > Cfg.MapHalf + 5)
            {
                ServerReset();
                p = transform.position;
            }

            sbyte team = -1;
            if (!IsCarried && p.y < 30f) team = (sbyte)Cfg.BaseTeamAt(p);
            if (BaseTeam.Value != team) BaseTeam.Value = team;
        }

        public static Vector3 CarryPoint(PlayerNet p) =>
            p.transform.position + p.transform.forward * 0.6f + Vector3.up * (p.Crouch.Value ? 0.8f : 1.15f);

        /// <summary>Lock the ball into a team's machine socket.</summary>
        public void ServerSocket(int team)
        {
            CarrierId.Value = NoCarrier;
            m_Rb.isKinematic = true;
            transform.SetPositionAndRotation(Cfg.SocketPos(team), Quaternion.identity);
            SocketTeam.Value = (sbyte)team;
            if (NetGame.Instance != null) NetGame.Instance.Broadcast($"The ball is in the {Cfg.TeamName[team]} machine!");
        }

        public void ServerReset()
        {
            SocketTeam.Value = -1;
            CarrierId.Value = NoCarrier;
            m_Rb.isKinematic = false;
            transform.position = Cfg.BallDropPoint;
            m_Rb.linearVelocity = Vector3.zero;
            m_Rb.angularVelocity = Vector3.zero;
        }

        public bool ServerPickup(PlayerNet p)
        {
            if (IsCarried) return false;
            SocketTeam.Value = -1;
            CarrierId.Value = p.NetworkObjectId;
            m_Rb.isKinematic = true;
            return true;
        }

        /// <summary>Thrown by a player: it passes through the thrower for a moment so it can't bounce off them.</summary>
        /// <summary>`mount`: the horse the thrower is riding (the ball passes through it too).</summary>
        public void ServerThrow(PlayerNet thrower, Vector3 pos, Vector3 vel, Collider mount = null)
        {
            if (m_IgnoredCol != null) Physics.IgnoreCollision(m_Col, m_IgnoredCol, false);
            if (m_IgnoredMount != null) Physics.IgnoreCollision(m_Col, m_IgnoredMount, false);
            m_IgnoredCol = thrower.GetComponent<CharacterController>();
            if (m_IgnoredCol != null) Physics.IgnoreCollision(m_Col, m_IgnoredCol, true);
            m_IgnoredMount = mount;
            if (mount != null) Physics.IgnoreCollision(m_Col, mount, true);
            m_IgnoreUntil = Time.time + 0.8f;
            ServerDrop(pos, vel);
            m_Col.enabled = true;
        }

        public void ServerDrop(Vector3 pos, Vector3 vel)
        {
            SocketTeam.Value = -1;
            CarrierId.Value = NoCarrier;
            transform.position = pos;
            m_Rb.isKinematic = false;
            m_Rb.linearVelocity = Vector3.ClampMagnitude(vel, 40f);
        }
    }
}
