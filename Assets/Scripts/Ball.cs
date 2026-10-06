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
        /// <summary>Someone has picked the ball up this match (the glow on the ground at the beam's foot goes for good:
        /// it looked odd hanging in the air under a carried or flying ball).</summary>
        public readonly NetworkVariable<bool> EverPickedUp = new NetworkVariable<bool>(false);
        public const float PlinthH = 1.2f, Radius = 0.62f;
        /// <summary>The beacon pillar, from the ball's centre: from 4 m under the ground the ball sits on, up into the sky.</summary>
        public const float BeaconBottom = -(Radius + 4f), BeaconTop = 300.65f;

        Rigidbody m_Rb;
        Collider m_Col;
        Transform m_Visual;
        GameObject m_Mesh;
        GameObject m_Beacon, m_Foot;
        Renderer m_BeaconRenderer;
        Material m_BeaconMat, m_HaloMat, m_FootMat;
        /// <summary>The beacon's bright core and its soft halo (radius, metres).</summary>
        public const float BeaconCore = 0.55f, BeaconHalo = 1.9f;
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

            // Beacon: a tall glowing pillar of light (gold loose, team colour when socketed) - additive, so it shines out
            // in daylight and from right across the map: a bright core, a wide soft halo round it that fades out high up,
            // and a glow on the ground at its foot. It doesn't start at the ball: it comes up out of the ground a few
            // metres under it, runs right through the ball and on up into the sky
            m_Beacon = new GameObject("beacon");
            m_Beacon.transform.SetParent(transform, false);
            float halfH = (BeaconTop - BeaconBottom) * 0.5f;
            float hy = halfH / Mathf.Max(0.01f, Art.Cylinder.bounds.extents.y), cr = 0.5f / Mathf.Max(0.01f, Art.Cylinder.bounds.extents.x);
            m_BeaconMat = BeamFx.AsBeam(BeamFx.Column(Color.white, 1f, 1.1f, 0.25f, 0.6f, 0f, 0.3f, 0.12f)); // (fades down as you come up to it: Settings > Display > BEAMS)
            var pillar = BeamFx.Cylinder(m_Beacon.transform, m_BeaconMat, "pillar");
            pillar.transform.localPosition = new Vector3(0, BeaconBottom + halfH, 0);
            pillar.transform.localScale = new Vector3(BeaconCore * 2f * cr, hy, BeaconCore * 2f * cr);
            m_BeaconRenderer = pillar.GetComponent<MeshRenderer>();
            m_HaloMat = BeamFx.AsBeam(BeamFx.Column(Color.white, 1f, 2.2f, 0f, 0f, 0f, 0.55f));
            var halo = BeamFx.Cylinder(m_Beacon.transform, m_HaloMat, "pillar halo");
            halo.transform.localPosition = pillar.transform.localPosition;
            halo.transform.localScale = new Vector3(BeaconHalo * 2f * cr, hy, BeaconHalo * 2f * cr);
            m_FootMat = BeamFx.Glow(Color.white, 1f, 1.8f);
            var foot = BeamFx.Disc(m_Beacon.transform, m_FootMat, "pillar foot glow");
            foot.transform.localPosition = new Vector3(0, -Radius + 0.06f, 0);
            foot.transform.localScale = Vector3.one * BeaconHalo * 3f;
            m_Foot = foot;
            SocketTeam.OnValueChanged += OnSocketChanged;
            OnSocketChanged(-1, SocketTeam.Value);
            m_Ready = true;
        }

        public override void OnNetworkDespawn()
        {
            if (m_Plinth) Destroy(m_Plinth.gameObject);
            EndSocket();
            foreach (var m in new[] { m_BeaconMat, m_HaloMat, m_FootMat }) if (m) Destroy(m);
            SocketTeam.OnValueChanged -= OnSocketChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>The ball went into / out of a socket: it's pulled in (AnimateSocket) with its whoosh, clunk and
        /// chord, and the light's colour and strength.</summary>
        void OnSocketChanged(sbyte prev, sbyte cur)
        {
            if (cur >= 0 && prev != cur)
            {
                if (Cfg.Builder || !m_Ready)
                {
                    // (Builder: planted, no machine; or it was in there already when we joined: no pull-in to show)
                    Sfx.Play(Sfx.Zap, transform.position, 1f);
                    Sfx.Play(Sfx.Ding, transform.position, 0.8f);
                }
                else BeginSocketPull(cur);
            }
            if (cur < 0) EndSocket();
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
            BeamFx.Set(m_BeaconMat, Color.Lerp(c, Color.white, 0.25f), team >= 0 ? 2.1f : 1.8f);
            BeamFx.Set(m_HaloMat, c, team >= 0 ? 0.5f : 0.42f);
            BeamFx.Set(m_FootMat, c, 1.3f);
        }

        /// <summary>Test hook: the beacon's core brightness (0 when it isn't up).</summary>
        public float BeaconIntensity => m_Beacon != null && m_Beacon.activeInHierarchy ? BeamFx.Intensity(m_BeaconMat) : 0f;

        void LateUpdate()
        {
            TintBeacon();
            // the beam of light is always on again - carried, rolling, lying still or in a socket (it used to wait until
            // the ball had sat still for 3 seconds) - so everyone can always find the ball. Only the victory cutscene
            // turns it off: it ran straight up through the middle of the UFO over the winners' base
            // The one carrying it doesn't see it (it filled their screen); everyone else still does
            var carrier = Carrier;
            bool beacon = !VictoryCutscene.Active && !(carrier != null && carrier.Mine);
            if (m_Beacon.activeSelf != beacon) m_Beacon.SetActive(beacon);
            // the glow on the ground at its foot: only until the first pickup (after that the ball's off in the air as
            // often as not, and a disc of light floating under it looked odd)
            bool foot = !EverPickedUp.Value;
            if (m_Foot != null && m_Foot.activeSelf != foot) m_Foot.SetActive(foot);
            // Clients: render the carried ball in the carrier's arms (avoids interpolation lag).
            // The carrier themselves sees it in their first-person hands instead.
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
            bool show = carrier == null || !carrier.Mine;
            if (m_Mesh.activeSelf != show) m_Mesh.SetActive(show);
            if (Cfg.Builder) AnimatePlant();
            else if (carrier == null && SocketTeam.Value >= 0) AnimateSocket(SocketTeam.Value);
            m_LastVisual = m_Visual.position;
        }

        // ---------------- in a machine's socket: pulled in, then turning ----------------

        /// <summary>Seconds for the ball to be pulled into the socket (it shoots a little past and settles back), when in
        /// that it seats (the flash, as the sound's clunk lands), and how fast it turns while it sits there (degrees a second).</summary>
        public const float SocketPull = 0.45f, SocketSeat = 0.32f, SocketSpin = 80f;
        /// <summary>How far past the socket the pull-in overshoots (the "back" ease's constant: about a sixth of the way).</summary>
        const float SocketOvershoot = 2.2f;
        bool m_Ready, m_Seated;
        float m_SocketAt = -100f;
        Vector3 m_SocketFrom, m_LastVisual;
        GameObject m_Flash;
        Material m_FlashMat;

        /// <summary>Test hooks: how far along the pull-in is (0 just caught .. 1 seated; 1 when it isn't in a socket), and
        /// how far the ball's picture has turned (degrees) while it sits in the socket.</summary>
        public float SocketPullK => Mathf.Clamp01((Time.time - m_SocketAt) / SocketPull);
        public float SocketYaw => m_Visual != null ? m_Visual.localEulerAngles.y : 0f;

        /// <summary>On every peer, the moment the ball's caught by a socket: it's pulled in from where it was seen last.</summary>
        void BeginSocketPull(int team)
        {
            var sp = Cfg.SocketPos(Mathf.Clamp(team, 0, 3));
            m_SocketFrom = m_LastVisual;
            // (put there from far off - a dev setting: it drops in from just above instead of flying across the map)
            if ((m_SocketFrom - sp).sqrMagnitude > 5f * 5f || m_SocketFrom == Vector3.zero) m_SocketFrom = sp + Vector3.up * 1.6f;
            m_SocketAt = Time.time;
            m_Seated = false;
            Sfx.Play(Sfx.Socket, sp, 1f, 0f, 110f);
        }

        /// <summary>
        /// The ball in a machine's socket (only its picture moves; on every peer): an eased pull-in that shoots a little
        /// past and settles back, spinning down from a fast whirl; as it seats, a flash - sparks, puffs in the team's
        /// colour, a ring of light racing out, the ball's light flaring, the ball swelling for a moment - and from then on
        /// it turns steadily (by the server's clock, so everyone sees it at the same angle) and bobs a little.
        /// </summary>
        void AnimateSocket(int team)
        {
            team = Mathf.Clamp(team, 0, 3);
            var sp = Cfg.SocketPos(team);
            var c = Cfg.TeamColor[team];
            float t = Time.time - m_SocketAt;
            float k = Mathf.Clamp01(t / SocketPull), q = k - 1f;
            float ease = 1f + (SocketOvershoot + 1f) * q * q * q + SocketOvershoot * q * q;
            float since = t - SocketSeat;
            if (since >= 0f && !m_Seated)
            {
                m_Seated = true;
                if (t < 2f) SocketFlash(sp, c);
            }
            float punch = since > 0f && since < 0.4f ? Mathf.Sin(since / 0.4f * Mathf.PI) * 0.22f : 0f;
            float bob = k >= 1f ? Mathf.Sin((t - SocketPull) * 1.9f) * 0.05f : 0f;
            double clock = NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
            float yaw = (float)(clock * SocketSpin % 360.0) - 720f * q * q;
            m_Visual.position = Vector3.LerpUnclamped(m_SocketFrom, sp, ease) + Vector3.up * bob;
            m_Visual.rotation = Quaternion.Euler(0f, yaw, 0f);
            m_Visual.localScale = Vector3.one * (1f + punch);
            // its light flares as it seats and settles to the socket's steady glow
            float flare = since > 0f ? Mathf.Exp(-since * 3.5f) : 0f;
            m_Light.intensity = 6f + 20f * flare;
            m_Light.range = 14f + 8f * flare;
            if (m_Flash != null)
            {
                float u = since / 0.55f;
                if (u >= 1f) { Destroy(m_Flash); Destroy(m_FlashMat); m_Flash = null; }
                else
                {
                    m_Flash.transform.localScale = Vector3.one * Mathf.Lerp(1.5f, 9f, 1f - (1f - u) * (1f - u));
                    BeamFx.Set(m_FlashMat, Color.Lerp(c, Color.white, 0.4f), 3f * (1f - u) * (1f - u));
                }
            }
        }

        /// <summary>The flash as the ball seats in the socket.</summary>
        void SocketFlash(Vector3 sp, Color c)
        {
            Fx.Sparks(sp, Vector3.up, 28);
            for (int i = 0; i < 10; i++)
            {
                var off = Quaternion.Euler(0f, i * 36f, 0f) * Vector3.forward * 0.9f;
                FxParticle.Puff(sp + off + Vector3.up * Random.Range(-0.2f, 0.5f), new Color(c.r, c.g, c.b, 0.6f), Random.Range(0.7f, 1.2f));
            }
            if (m_Flash == null)
            {
                m_FlashMat = BeamFx.Glow(c, 0f, 1.2f);
                m_Flash = BeamFx.Disc(null, m_FlashMat, "socket flash");
            }
            m_Flash.transform.position = sp + Vector3.down * (Radius - 0.1f);
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - sp).sqrMagnitude < 14f * 14f) Fx.Shake(0.18f);
        }

        /// <summary>Out of the socket (picked up, knocked out, reset): the picture is the plain ball again.</summary>
        void EndSocket()
        {
            if (m_Visual != null) m_Visual.localScale = Vector3.one;
            if (m_Flash != null) { Destroy(m_Flash); Destroy(m_FlashMat); m_Flash = null; }
            m_SocketAt = -100f;
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
            if (NetGame.Instance != null) NetGame.Instance.ServerBallFeed(by, by.Team.Value, true); // (Builder's capture: a kill feed line - and no top-right message as well)
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
            bool flagOn = flagAmt > 0.001f && !(holder != null && holder.Mine);
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
            // the kill feed (and only it - no top-right message saying the same): who captured it (whoever last picked it up, if they're on that team), or just the team
            if (NetGame.Instance != null) NetGame.Instance.ServerBallFeed(m_LastHolder != null && m_LastHolder.IsSpawned && m_LastHolder.Team.Value == team ? m_LastHolder : null, team, true);
            if (NetGame.Instance != null) NetGame.Instance.ServerOnSocket(team, m_LastHolder != null && m_LastHolder.IsSpawned && m_LastHolder.Team.Value == team ? m_LastHolder : null); // (3 Goal: a goal)
        }

        /// <summary>Server: who picked the ball up last (the kill feed names them when it goes into their machine).</summary>
        PlayerNet m_LastHolder;

        public void ServerReset()
        {
            SocketTeam.Value = -1;
            CarrierId.Value = NoCarrier;
            m_Rb.isKinematic = false;
            MoveTo(Cfg.BallDropPoint);
            m_Rb.linearVelocity = Vector3.zero;
            m_Rb.angularVelocity = Vector3.zero;
            m_SlowFall = true;
        }

        /// <summary>Where the ball waits at the start of a match: on the ground in the middle, under the glass dome.</summary>
        public static Vector3 DomeSpot => new Vector3(0, MapBuilder.Height(0, 0) + Radius + 0.02f, 0);

        /// <summary>Server: the match starts with the ball sitting still in the middle of the map, under the glass dome (nobody can reach it until the wall drops).</summary>
        public void ServerPlaceInDome()
        {
            SocketTeam.Value = -1;
            CarrierId.Value = NoCarrier;
            EverPickedUp.Value = false; // (a new match: the foot glow's back)
            m_Rb.isKinematic = true;
            MoveTo(DomeSpot);
            transform.rotation = Quaternion.identity;
            m_SlowFall = false;
        }

        /// <summary>Server: the wall (and the dome) dropped - the ball is loose (a normal physics ball again).</summary>
        public void ServerRelease()
        {
            if (IsCarried || SocketTeam.Value >= 0) return; // (picked up or socketed by a dev setting meanwhile)
            m_Rb.isKinematic = false;
            m_Rb.linearVelocity = Vector3.zero;
            m_Rb.WakeUp();
        }

        // ---------------- falling from the sky slowly ----------------

        /// <summary>The ball is dropping in from high up (a reset, the dev setting's "ball to the middle"): it falls at half the speed.</summary>
        bool m_SlowFall;
        /// <summary>Gravity on a slowly falling ball: a quarter of normal, so it's going half as fast at any height.</summary>
        const float SlowFallGravity = 0.25f;

        /// <summary>Server: drop the ball from up in the sky at half speed (it slows its fall until it hits something).</summary>
        public void ServerDropFromSky(Vector3 pos)
        {
            ServerDrop(pos, Vector3.zero);
            m_SlowFall = true;
        }

        void FixedUpdate()
        {
            if (!IsServer || !m_SlowFall || m_Rb == null) return;
            if (m_Rb.isKinematic || IsCarried) { m_SlowFall = false; return; }
            m_Rb.AddForce(-Physics.gravity * (1f - SlowFallGravity), ForceMode.Acceleration);
        }

        void OnCollisionEnter(Collision c)
        {
            if (m_SlowFall && IsServer) m_SlowFall = false; // landed: normal physics again
        }

        public bool ServerPickup(PlayerNet p)
        {
            if (IsCarried) return false;
            SocketTeam.Value = -1;
            CarrierId.Value = p.NetworkObjectId;
            EverPickedUp.Value = true;
            m_Rb.isKinematic = true;
            m_LastHolder = p;
            if (NetGame.Instance != null) NetGame.Instance.ServerBallFeed(p, p.Team.Value, false); // (a line in the kill feed)
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
            m_SlowFall = false;
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
