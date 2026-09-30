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
        /// <summary>Builder: the ground under the ball's block while it's planted (SocketTeam = whose ball it is).</summary>
        public readonly NetworkVariable<Vector3> PlantPos = new NetworkVariable<Vector3>();
        public const float PlinthH = 1.2f, Radius = 0.62f;

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

            // Beacon: a tall translucent pillar of light that always shoots up from the ball (gold loose, team colour when held / socketed)
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
            if (m_Plinth) Destroy(m_Plinth.gameObject);
            SocketTeam.OnValueChanged -= OnSocketChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>The ball went into / out of a socket: the ding, and the light's colour and strength.</summary>
        void OnSocketChanged(sbyte prev, sbyte cur)
        {
            if (cur >= 0 && prev != cur)
            {
                Sfx.Play(Sfx.Zap, transform.position, 1f);
                Sfx.Play(Sfx.Ding, transform.position, 0.8f);
            }
            // the beacon is always on (so everyone can always find the ball); it takes the colour of the team that has it
            m_Beacon.SetActive(true);
            if (cur >= 0)
            {
                var c = Cfg.TeamColor[cur];
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
            // (on every peer: the server for the thrower, the thrower's own screen for itself)
            if ((m_IgnoredCol != null || m_IgnoredMount != null) && Time.time > m_IgnoreUntil)
            {
                if (m_IgnoredCol != null) Physics.IgnoreCollision(m_Col, m_IgnoredCol, false);
                if (m_IgnoredMount != null) Physics.IgnoreCollision(m_Col, m_IgnoredMount, false);
                m_IgnoredCol = m_IgnoredMount = null;
            }

            if (IsServer) ServerTick();
        }

        int m_BeaconTeam = -2;

        /// <summary>Beacon colour: the socket's team, else the carrier's team, else gold (loose).</summary>
        void TintBeacon()
        {
            int team = SocketTeam.Value >= 0 ? SocketTeam.Value : Carrier != null ? Carrier.Team.Value : -1;
            if (team == m_BeaconTeam || m_BeaconRenderer == null) return;
            m_BeaconTeam = team;
            var c = team >= 0 ? Cfg.TeamColor[Mathf.Clamp(team, 0, 3)] : new Color(1f, 0.85f, 0.3f);
            m_BeaconRenderer.sharedMaterial = Art.Ghost(new Color(c.r, c.g, c.b, team >= 0 ? 0.4f : 0.3f));
        }

        void LateUpdate()
        {
            TintBeacon();
            // (whoever is carrying it doesn't get a pillar of light through their own view)
            var holder = Carrier;
            bool beacon = holder == null || !holder.IsOwner;
            if (m_Beacon.activeSelf != beacon) m_Beacon.SetActive(beacon);
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
            if (Cfg.Builder) AnimatePlant();
        }

        // ---------------- Builder: planting the ball ----------------

        Transform m_Plinth, m_Flag;
        BoxCollider m_PlinthCol;
        MeshRenderer m_PlinthMesh, m_FlagCloth;
        float m_PlantAmt;
        int m_PlantTeam = -2, m_FlagTeam = -3;

        /// <summary>Builder: whoever plants the ball owns it until someone picks it up.</summary>
        public void ServerPlant(PlayerNet by, Vector3 ground)
        {
            CarrierId.Value = NoCarrier;
            m_Rb.isKinematic = true;
            PlantPos.Value = ground;
            MoveTo(ground + Vector3.up * (PlinthH + Radius));
            transform.rotation = Quaternion.identity;
            SocketTeam.Value = (sbyte)by.Team.Value;
            if (NetGame.Instance != null) NetGame.Instance.Broadcast($"{Cfg.TeamLabel(by.Team.Value)} planted the ball - it's theirs until someone picks it up!");
        }

        /// <summary>The block (in the owner's colour) that grows up under a planted ball, and the flag that grows out of its top.</summary>
        void BuildPlantVisuals()
        {
            m_Plinth = new GameObject("ball block").transform;
            var box = Art.Box(m_Plinth, Color.white, new Vector3(0, PlinthH * 0.5f, 0), new Vector3(PlinthH, PlinthH, PlinthH));
            m_PlinthMesh = box.GetComponent<MeshRenderer>();
            m_PlinthCol = box.AddComponent<BoxCollider>();
            m_Flag = new GameObject("flag").transform;
            m_Flag.SetParent(m_Visual, false);
            m_Flag.localPosition = new Vector3(0, Radius - 0.05f, 0);
            Art.Part(m_Flag, Art.Cylinder, new Color(0.85f, 0.85f, 0.8f), new Vector3(0, 0.9f, 0), new Vector3(0.07f, 0.9f, 0.07f));
            var cloth = Art.Box(m_Flag, Color.white, new Vector3(0.45f, 1.5f, 0), new Vector3(0.85f, 0.55f, 0.04f));
            m_FlagCloth = cloth.GetComponent<MeshRenderer>();
            m_Plinth.gameObject.SetActive(false);
            m_Flag.gameObject.SetActive(false);
        }

        void AnimatePlant()
        {
            if (m_Plinth == null) BuildPlantVisuals();
            bool planted = SocketTeam.Value >= 0 && !IsCarried;
            if (planted && m_PlantTeam != SocketTeam.Value)
            {
                m_PlantTeam = SocketTeam.Value;
                var c = Cfg.TeamColor[Mathf.Clamp(m_PlantTeam, 0, 3)];
                m_PlinthMesh.sharedMaterial = Art.Mat(c);
                m_FlagCloth.sharedMaterial = Art.Mat(Color.Lerp(c, Color.white, 0.15f));
                m_FlagTeam = m_PlantTeam;
                m_Plinth.position = PlantPos.Value;
                m_Plinth.rotation = Quaternion.identity;
                m_PlantAmt = 0f;
            }
            if (!planted) m_PlantTeam = -2;
            // grows up in half a second when planted, sinks back when someone picks the ball up
            m_PlantAmt = Mathf.MoveTowards(m_PlantAmt, planted ? 1f : 0f, Time.deltaTime * 2.2f);
            float e = m_PlantAmt * m_PlantAmt * (3f - 2f * m_PlantAmt);
            bool visible = m_PlantAmt > 0.001f;
            if (m_Plinth.gameObject.activeSelf != visible) m_Plinth.gameObject.SetActive(visible);
            m_Plinth.localScale = new Vector3(1f, Mathf.Max(0.001f, e), 1f);
            // the flag: always there pointing at the sky (in the colour of whoever has the ball, white when it's loose),
            // or (Builder Flag Always Up off) only growing out while the ball is planted
            var holder = Carrier;
            bool always = Cfg.BuilderFlagAlwaysUp;
            float flagAmt = always ? 1f : e;
            bool flagOn = flagAmt > 0.001f && !(holder != null && holder.IsOwner);
            if (m_Flag.gameObject.activeSelf != flagOn) m_Flag.gameObject.SetActive(flagOn);
            m_Flag.localScale = Vector3.one * Mathf.Max(0.001f, flagAmt);
            m_Flag.rotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 1.7f) * 12f, 0);
            m_Flag.position = m_Visual.position + Vector3.up * (Radius - 0.05f);
            if (always)
            {
                int ft = planted ? SocketTeam.Value : holder != null ? holder.Team.Value : -1;
                if (ft != m_FlagTeam)
                {
                    m_FlagTeam = ft;
                    m_FlagCloth.sharedMaterial = Art.Mat(ft >= 0 ? Color.Lerp(Cfg.TeamColor[Mathf.Clamp(ft, 0, 3)], Color.white, 0.15f) : Color.white);
                }
            }
            m_PlinthCol.enabled = planted && m_PlantAmt >= 1f;
            // the ball rides up on top of its block
            if (planted) m_Visual.localPosition = Vector3.down * PlinthH * (1f - e);
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
                    transform.position = CarryPoint(c);
                }
            }

            var p = transform.position;
            if (!IsCarried && SocketTeam.Value < 0 && !Cfg.Builder) // Builder has no machine goal
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
            MoveTo(Cfg.BallDropPoint);
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

        /// <summary>
        /// Thrown by a player: it passes through the thrower (and `mount`, the horse they ride) for a moment so it can't
        /// bounce off them.
        /// </summary>
        public void ServerThrow(PlayerNet thrower, Vector3 pos, Vector3 vel, Collider mount = null)
        {
            IgnoreLocal(thrower.GetComponent<CharacterController>(), mount, 1f);
            ServerDrop(pos, vel);
            m_Col.enabled = true;
        }

        /// <summary>The ball passes through the thrower (and their horse) for a moment.</summary>
        public void IgnoreLocal(Collider thrower, Collider mount, float seconds)
        {
            if (m_Col == null) return;
            if (m_IgnoredCol != null) Physics.IgnoreCollision(m_Col, m_IgnoredCol, false);
            if (m_IgnoredMount != null) Physics.IgnoreCollision(m_Col, m_IgnoredMount, false);
            m_IgnoredCol = thrower;
            m_IgnoredMount = mount;
            if (thrower != null) Physics.IgnoreCollision(m_Col, thrower, true);
            if (mount != null) Physics.IgnoreCollision(m_Col, mount, true);
            m_IgnoreUntil = Time.time + seconds;
        }

        public void ServerDrop(Vector3 pos, Vector3 vel)
        {
            SocketTeam.Value = -1;
            CarrierId.Value = NoCarrier;
            m_Rb.isKinematic = false;
            MoveTo(pos);
            m_Rb.linearVelocity = Vector3.ClampMagnitude(vel, 40f);
        }

        /// <summary>
        /// Teleport the ball. The physics body is moved too: with interpolation on, setting only the transform of a loose
        /// ball got overwritten by the physics step and it snapped back to where it was.
        /// </summary>
        void MoveTo(Vector3 pos)
        {
            transform.position = pos;
            m_Rb.position = pos;
            Physics.SyncTransforms();
        }
    }
}
