using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// Something you can ride: a Bad-Piggies-style wooden car (placed from the inventory, arcade driving, runs people over)
    /// or a horse (wanders the map; needs a saddle, then rides like a Minecraft horse: goes where you look, space jumps).
    /// Whoever drives owns it (NetworkTransform in Owner mode); nobody driving = the server owns it.
    /// Also used for Slenderman (hatched from an egg): a server-driven creature that hunts the nearest enemy.
    /// Horses and Slenderman can be killed; a horse drops meat (and its saddle).
    /// </summary>
    public class Vehicle : NetworkBehaviour
    {
        public const byte Car = 0, Horse = 1, Slender = 2;
        const ulong NoDriver = ulong.MaxValue;
        public static readonly List<Vehicle> All = new List<Vehicle>();

        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        public readonly NetworkVariable<ulong> DriverId = new NetworkVariable<ulong>(NoDriver);
        public readonly NetworkVariable<bool> Saddled = new NetworkVariable<bool>();
        public readonly NetworkVariable<byte> SaddleTeam = new NetworkVariable<byte>();   // saddle colour
        public readonly NetworkVariable<float> Hp = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<byte> OwnerTeam = new NetworkVariable<byte>();    // Slenderman: who hatched it

        public bool IsHorse => Kind.Value == Horse;
        public bool IsSlender => Kind.Value == Slender;
        public bool Rideable => Kind.Value != Slender;
        public bool HasDriver => DriverId.Value != NoDriver;
        public string DisplayName => IsSlender ? "Slenderman" : IsHorse ? (Saddled.Value ? "Saddled Horse" : "Wild Horse") : "Wooden Car";
        /// <summary>Where the rider's feet go (local space).</summary>
        public Vector3 SeatLocal => IsHorse ? new Vector3(0, 0.95f, -0.1f) : new Vector3(0, 0.3f, -0.25f);
        public Vector3 SeatWorld => transform.TransformPoint(SeatLocal);

        CharacterController m_CC;
        Transform m_Visual, m_Saddle, m_Fan, m_Head, m_Tail;
        readonly List<Transform> m_Wheels = new List<Transform>();
        readonly List<Transform> m_Legs = new List<Transform>();
        float m_Speed, m_VelY, m_Yaw, m_Anim, m_AnimSpeed, m_NextWander, m_WanderSpeed;
        Vector3 m_Planar, m_LastPos;
        readonly Dictionary<ulong, float> m_HitCooldown = new Dictionary<ulong, float>();

        public static Vehicle ServerSpawn(byte kind, Vector3 pos, float yaw)
        {
            var go = Instantiate(Bootstrap.I.vehiclePrefab, pos, Quaternion.Euler(0, yaw, 0));
            var v = go.GetComponent<Vehicle>();
            v.Kind.Value = kind;
            v.Hp.Value = kind == Slender ? Cfg.SlenderHp : Cfg.HorseHp;
            go.GetComponent<NetworkObject>().Spawn(true);
            return v;
        }

        public static Vehicle ServerSpawnSlender(Vector3 pos, int team)
        {
            var go = Instantiate(Bootstrap.I.vehiclePrefab, pos, Quaternion.identity);
            var v = go.GetComponent<Vehicle>();
            v.Kind.Value = Slender;
            v.Hp.Value = Cfg.SlenderHp;
            v.OwnerTeam.Value = (byte)team;
            v.m_DieAt = Time.time + Cfg.SlenderLife;
            go.GetComponent<NetworkObject>().Spawn(true);
            Fx.Server(FxKind.Spawn, pos, Vector3.up);
            return v;
        }

        float m_DieAt = float.MaxValue, m_NextBlink;

        /// <summary>Hurt a horse / Slenderman. A dead horse drops meat (and its saddle); the rider falls off.</summary>
        public void ServerDamage(float dmg, PlayerNet attacker)
        {
            if (!IsServer || !IsSpawned || Kind.Value == Car || dmg <= 0) return;
            Hp.Value = Mathf.Max(0f, Hp.Value - dmg);
            if (Hp.Value > 0) return;
            var d = Driver;
            if (d != null) d.ServerDismount();
            var g = NetGame.Instance;
            var at = transform.position + Vector3.up * 0.8f;
            if (IsHorse && g != null)
            {
                var drops = new List<ItemStack> { ItemStack.Of(Item.Meat, 1) };
                if (Saddled.Value) drops.Add(ItemStack.Of(Item.Saddle, 1, SaddleTeam.Value + 1));
                g.ServerScatter(drops, at);
            }
            Fx.Server(IsSlender ? FxKind.Drink : FxKind.Blood, at, Vector3.up);
            if (IsSlender && g != null) g.Broadcast("Slenderman was destroyed!");
            NetworkObject.Despawn(true);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            m_CC = gameObject.AddComponent<CharacterController>();
            if (IsHorse) { m_CC.radius = 0.55f; m_CC.height = 1.7f; m_CC.center = new Vector3(0, 0.95f, 0); }
            else { m_CC.radius = 0.85f; m_CC.height = 1.3f; m_CC.center = new Vector3(0, 0.75f, 0); }
            m_CC.stepOffset = 0.45f;
            m_CC.slopeLimit = 50f;
            m_CC.skinWidth = 0.06f;
            if (IsSlender) { m_CC.radius = 0.4f; m_CC.height = 2.6f; m_CC.center = new Vector3(0, 1.3f, 0); }
            m_Visual = CreateVisual(Kind.Value, transform, null, out m_Saddle, out m_Fan, out m_Head, out m_Tail, m_Wheels, m_Legs).transform;
            SaddleTeam.OnValueChanged += (a, b) => ColourSaddle();
            ColourSaddle();
            m_Yaw = transform.eulerAngles.y;
            m_LastPos = transform.position;
            m_NextWander = Time.time + Random.Range(1f, 4f);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        /// <summary>We just started driving it (or the server took it back): carry on from where it is now.</summary>
        public override void OnGainedOwnership()
        {
            m_Yaw = transform.eulerAngles.y;
            m_Speed = 0f;
            m_VelY = 0f;
            m_Planar = Vector3.zero;
        }

        public PlayerNet Driver
        {
            get
            {
                if (!HasDriver || NetworkManager == null) return null;
                return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(DriverId.Value, out var no) ? no.GetComponent<PlayerNet>() : null;
            }
        }

        public void ServerSetDriver(PlayerNet p)
        {
            DriverId.Value = p != null ? p.NetworkObjectId : NoDriver;
            if (p != null && p.OwnerClientId != NetworkManager.ServerClientId) NetworkObject.ChangeOwnership(p.OwnerClientId);
            else if (p == null && OwnerClientId != NetworkManager.ServerClientId) NetworkObject.RemoveOwnership();
            m_Speed = 0f;
            m_Planar = Vector3.zero;
        }

        /// <summary>Saddles are in the colour of the team that put them on.</summary>
        void ColourSaddle()
        {
            if (m_Saddle == null) return;
            var blanket = m_Saddle.Find("blanket");
            if (blanket) blanket.GetComponent<Renderer>().sharedMaterial = Art.Mat(Cfg.TeamColor[Mathf.Clamp(SaddleTeam.Value, 0, Cfg.TeamColor.Length - 1)]);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (IsSlender)
            {
                if (IsServer) Hunt(dt);
                AnimateSlender();
                return;
            }
            if (m_Saddle && m_Saddle.gameObject.activeSelf != Saddled.Value) m_Saddle.gameObject.SetActive(Saddled.Value);

            if (IsOwner)
            {
                var me = PlayerNet.Local;
                bool drivenByMe = HasDriver && me != null && me.NetworkObjectId == DriverId.Value;
                if (drivenByMe) Drive(dt);
                else if (IsServer && !HasDriver) Idle(dt);
            }

            // animation from how fast it actually moves (works on every peer)
            var d = transform.position - m_LastPos;
            m_LastPos = transform.position;
            float fwd = Vector3.Dot(new Vector3(d.x, 0, d.z), transform.forward) / dt;
            if (Mathf.Abs(fwd) > 40f) fwd = 0f; // teleport
            m_AnimSpeed = Mathf.Lerp(m_AnimSpeed, fwd, dt * 8f);
            Animate(dt);
        }

        // ---------------- driving (the driver's machine) ----------------

        void Drive(float dt)
        {
            var pc = PlayerController.Local;
            float f = 0, s = 0;
            bool jump = false, sprint = false;
            float look = m_Yaw;
            if (pc != null) pc.GetDriveInput(out f, out s, out jump, out sprint, out look);
            bool grounded = m_CC.isGrounded;
            if (grounded && m_VelY < 0) m_VelY = -2f;
            m_VelY -= Cfg.Gravity * dt;

            Vector3 move;
            if (IsHorse)
            {
                // Minecraft horse: turns to where you look and runs that way; sprint to gallop, space to jump
                var camF = Quaternion.Euler(0, look, 0) * Vector3.forward;
                var camR = Quaternion.Euler(0, look, 0) * Vector3.right;
                var wish = camF * f * (f < 0 ? 0.45f : 1f) + camR * s * 0.6f;
                float speed = sprint ? Cfg.HorseSprint : Cfg.HorseWalk;
                var target = wish.sqrMagnitude > 0.01f ? wish.normalized * speed * Mathf.Clamp01(wish.magnitude) : Vector3.zero;
                m_Planar = Vector3.MoveTowards(m_Planar, target, (target.sqrMagnitude > m_Planar.sqrMagnitude ? 14f : 10f) * dt);
                if (f > 0.1f || m_Planar.sqrMagnitude > 1f) m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, look, 300f * dt);
                if (jump && grounded) m_VelY = Cfg.HorseJump;
                move = m_Planar;
            }
            else
            {
                // arcade car: W/S throttle and brake/reverse, A/D steer (more grip at speed)
                if (f > 0.1f) m_Speed = m_Speed < 0 ? Mathf.MoveTowards(m_Speed, 0, Cfg.CarAccel * 2.5f * dt) : Mathf.MoveTowards(m_Speed, Cfg.CarSpeed, Cfg.CarAccel * dt);
                else if (f < -0.1f) m_Speed = m_Speed > 0 ? Mathf.MoveTowards(m_Speed, 0, Cfg.CarAccel * 2.5f * dt) : Mathf.MoveTowards(m_Speed, -Cfg.CarReverseSpeed, Cfg.CarAccel * 0.7f * dt);
                else m_Speed = Mathf.MoveTowards(m_Speed, 0, 5f * dt);
                if (!grounded) m_Speed *= 1f - 0.2f * dt;
                float steer = s * Cfg.CarTurn * Mathf.Clamp01(Mathf.Abs(m_Speed) / 4f) * Mathf.Sign(m_Speed == 0 ? 1 : m_Speed);
                m_Yaw += steer * dt;
                move = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward * m_Speed;
                RunOver();
            }

            var before = transform.position;
            var flags = m_CC.Move((move + Vector3.up * m_VelY) * dt);
            if ((flags & CollisionFlags.Above) != 0 && m_VelY > 0) m_VelY = 0;
            if (!IsHorse && (flags & CollisionFlags.Sides) != 0)
            {
                // crashed into something: lose most of the speed
                float moved = Vector3.Distance(new Vector3(before.x, 0, before.z), new Vector3(transform.position.x, 0, transform.position.z)) / dt;
                if (moved < Mathf.Abs(m_Speed) * 0.5f) m_Speed *= 0.3f;
            }
            ApplyRotation(dt);
            if (transform.position.y < -30f)
            {
                m_CC.enabled = false;
                transform.position = new Vector3(0, 2f, Mathf.Sign(transform.position.z) * 10f);
                m_CC.enabled = true;
            }
        }

        /// <summary>The car leans with the ground under it; horses stay upright.</summary>
        void ApplyRotation(float dt)
        {
            var up = Vector3.up;
            if (!IsHorse && Physics.Raycast(transform.position + Vector3.up * 1f, Vector3.down, out var hit, 2.5f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                up = hit.normal;
            var fwd = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward;
            var target = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, up), up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, dt * 10f);
        }

        /// <summary>Anyone in front of a moving car gets hit (reported to the server, which damages and knocks them back).</summary>
        void RunOver()
        {
            if (Mathf.Abs(m_Speed) < 4f) return;
            var me = PlayerNet.Local;
            var c = transform.position + transform.up * 0.75f + transform.forward * (Mathf.Sign(m_Speed) * 1.3f);
            foreach (var col in Physics.OverlapBox(c, new Vector3(0.9f, 0.7f, 0.6f), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var p = col.GetComponentInParent<PlayerNet>();
                if (p == null || p == me || p.Dead.Value) continue;
                if (m_HitCooldown.TryGetValue(p.NetworkObjectId, out var until) && Time.time < until) continue;
                m_HitCooldown[p.NetworkObjectId] = Time.time + 0.8f;
                me.CarHitRpc(p.NetworkObject, m_Speed);
                Sfx.Play(Sfx.Thud, p.transform.position + Vector3.up, 1f);
                Fx.Shake(0.3f);
                m_Speed *= 0.7f;
            }
        }

        // ---------------- nobody riding (server) ----------------

        void Idle(float dt)
        {
            bool grounded = m_CC.isGrounded;
            if (grounded && m_VelY < 0) m_VelY = -2f;
            m_VelY -= Cfg.Gravity * dt;
            Vector3 move = Vector3.zero;
            if (IsHorse)
            {
                // wander about, grazing now and then, staying in its own half and out of the bases
                if (Time.time >= m_NextWander)
                {
                    m_NextWander = Time.time + Random.Range(2f, 5f);
                    m_WanderSpeed = Random.value < 0.55f ? Random.Range(1.2f, 2.2f) : 0f;
                    m_Yaw += Random.Range(-100f, 100f);
                }
                var fwd = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward;
                var ahead = transform.position + fwd * 3f;
                float half = Cfg.MapHalf - 8f;
                if (m_WanderSpeed > 0 && (Cfg.BaseTeamAt(ahead) >= 0 || Mathf.Abs(ahead.x) > half || Mathf.Abs(ahead.z) > half || Mathf.Sign(ahead.z) != Mathf.Sign(transform.position.z) || Mathf.Abs(ahead.z) < 4f))
                    m_Yaw += 180f;
                move = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward * m_WanderSpeed;
            }
            else m_Speed = 0f;
            m_CC.Move((move + Vector3.up * m_VelY) * dt);
            ApplyRotation(dt);
            if (transform.position.y < -30f)
            {
                m_CC.enabled = false;
                transform.position = new Vector3(transform.position.x, MapBuilder.Height(transform.position.x, transform.position.z) + 1f, transform.position.z);
                m_CC.enabled = true;
            }
        }

        // ---------------- Slenderman (server) ----------------

        /// <summary>Stalks the nearest enemy of whoever hatched it, blinking closer now and then; touching them kills them.</summary>
        void Hunt(float dt)
        {
            if (Time.time >= m_DieAt)
            {
                Fx.Server(FxKind.Drink, transform.position + Vector3.up, Vector3.up);
                NetworkObject.Despawn(true);
                return;
            }
            PlayerNet target = null;
            float best = float.MaxValue;
            foreach (var p in PlayerNet.All)
            {
                if (p.Dead.Value || p.Team.Value == OwnerTeam.Value) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < best) { best = d; target = p; }
            }
            bool grounded = m_CC.isGrounded;
            if (grounded && m_VelY < 0) m_VelY = -2f;
            m_VelY -= Cfg.Gravity * dt;
            var move = Vector3.zero;
            if (target != null)
            {
                var to = target.transform.position - transform.position;
                to.y = 0;
                if (best < 1.4f)
                {
                    target.ServerKill(null);
                    NetGame.Instance?.Broadcast($"Slenderman got {Cfg.TeamLabel(target.Team.Value)}!");
                    NetworkObject.Despawn(true);
                    return;
                }
                m_Yaw = Quaternion.LookRotation(to).eulerAngles.y;
                move = to.normalized * Cfg.SlenderSpeed;
                // every few seconds it blinks a chunk of the way towards you
                if (Time.time >= m_NextBlink && best > 8f)
                {
                    m_NextBlink = Time.time + Random.Range(4f, 7f);
                    m_CC.enabled = false;
                    var p = transform.position + to.normalized * Mathf.Min(6f, best - 4f);
                    p.y = MapBuilder.Height(p.x, p.z) + 0.2f;
                    if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 20f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) p.y = hit.point.y + 0.05f;
                    transform.position = p;
                    m_CC.enabled = true;
                    Fx.Server(FxKind.Spawn, p, Vector3.up);
                }
            }
            var flags = m_CC.Move((move + Vector3.up * m_VelY) * dt);
            if ((flags & CollisionFlags.Sides) != 0 && grounded) m_VelY = 6f; // climb over things
            transform.rotation = Quaternion.Euler(0, m_Yaw, 0);
        }

        void AnimateSlender()
        {
            m_Anim += Time.deltaTime;
            if (m_Head) m_Head.localRotation = Quaternion.Euler(Mathf.Sin(m_Anim * 7f) * 6f, Mathf.Sin(m_Anim * 3.3f) * 12f, Mathf.Sin(m_Anim * 11f) * 4f);
            for (int i = 0; i < m_Legs.Count; i++) m_Legs[i].localRotation = Quaternion.Euler(Mathf.Sin(m_Anim * 2f + i * 1.3f) * 25f, 0, (i % 2 == 0 ? 1 : -1) * (20f + Mathf.Sin(m_Anim * 3f + i) * 10f));
        }

        // ---------------- visuals ----------------

        void Animate(float dt)
        {
            float v = m_AnimSpeed;
            if (!IsHorse)
            {
                foreach (var w in m_Wheels) w.Rotate(v * dt / 0.4f * Mathf.Rad2Deg, 0, 0, Space.Self);
                if (m_Fan) m_Fan.Rotate(0, 0, (200f + Mathf.Abs(v) * 120f) * dt, Space.Self);
                return;
            }
            m_Anim += dt * Mathf.Abs(v) * 1.6f;
            float amp = Mathf.Clamp01(Mathf.Abs(v) / 3f) * (Mathf.Abs(v) > 7f ? 45f : 30f);
            for (int i = 0; i < m_Legs.Count; i++)
            {
                // diagonal pairs move together (trot)
                float ph = m_Anim + (i == 0 || i == 3 ? 0f : Mathf.PI);
                m_Legs[i].localRotation = Quaternion.Euler(Mathf.Sin(ph) * amp, 0, 0);
            }
            if (m_Head) m_Head.localRotation = Quaternion.Euler(Mathf.Abs(v) < 0.3f ? 35f + Mathf.Sin(Time.time) * 5f : Mathf.Sin(m_Anim * 2f) * 6f, 0, 0);
            if (m_Tail) m_Tail.localRotation = Quaternion.Euler(20f, Mathf.Sin(Time.time * 3f) * 15f, 0);
        }

        /// <summary>Car: wooden crate on wheels with a fan engine and a green pig at the front. Horse: blocky brown horse.</summary>
        public static GameObject CreateVisual(byte kind, Transform parent, Material ghost, out Transform saddle, out Transform fan, out Transform head, out Transform tail, List<Transform> wheels, List<Transform> legs)
        {
            saddle = fan = head = tail = null;
            var root = new GameObject("visual");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            if (kind == Car)
            {
                var plank = Art.Wood;
                // chassis: a frame of planks
                Art.Box(t, Art.DarkWood, new Vector3(0, 0.45f, 0), new Vector3(1.5f, 0.12f, 2.5f));
                for (int k = -1; k <= 1; k += 2)
                {
                    Art.Box(t, plank, new Vector3(k * 0.7f, 0.72f, 0), new Vector3(0.1f, 0.45f, 2.5f));
                    Art.Box(t, Art.DarkWood, new Vector3(k * 0.7f, 0.72f, 0.6f), new Vector3(0.12f, 0.5f, 0.1f));
                    Art.Box(t, Art.DarkWood, new Vector3(k * 0.7f, 0.72f, -0.6f), new Vector3(0.12f, 0.5f, 0.1f));
                }
                Art.Box(t, plank, new Vector3(0, 0.72f, 1.2f), new Vector3(1.4f, 0.45f, 0.1f));
                Art.Box(t, plank, new Vector3(0, 0.72f, -1.2f), new Vector3(1.4f, 0.45f, 0.1f));
                // seat
                Art.Box(t, Art.DarkWood, new Vector3(0, 0.62f, -0.3f), new Vector3(0.7f, 0.12f, 0.6f));
                Art.Box(t, Art.DarkWood, new Vector3(0, 0.95f, -0.62f), new Vector3(0.7f, 0.6f, 0.1f));
                // TNT-ish box engine and a fan on the back
                Art.Box(t, new Color(0.75f, 0.2f, 0.15f), new Vector3(0, 0.75f, -0.95f), new Vector3(0.6f, 0.45f, 0.35f));
                var fanRoot = new GameObject("fan").transform;
                fanRoot.SetParent(t, false);
                fanRoot.localPosition = new Vector3(0, 1.15f, -1.3f);
                for (int k = 0; k < 3; k++) Art.Box(fanRoot, new Color(0.85f, 0.85f, 0.8f), Vector3.zero, new Vector3(0.12f, 0.9f, 0.03f), new Vector3(0, 0, k * 60f));
                Art.Part(fanRoot, Art.Cylinder, Art.Metal, Vector3.zero, new Vector3(0.14f, 0.06f, 0.14f), new Vector3(90, 0, 0));
                fan = fanRoot;
                // the green pig on the front
                var pig = new Color(0.5f, 0.8f, 0.3f);
                Art.Part(t, Art.Sphere, pig, new Vector3(0, 1.1f, 1.05f), new Vector3(0.55f, 0.5f, 0.5f));
                Art.Part(t, Art.Cylinder, pig * 0.85f, new Vector3(0, 1.08f, 1.32f), new Vector3(0.2f, 0.05f, 0.2f), new Vector3(90, 0, 0));
                Art.Part(t, Art.Sphere, Color.white, new Vector3(0.12f, 1.22f, 1.26f), Vector3.one * 0.1f);
                Art.Part(t, Art.Sphere, Color.white, new Vector3(-0.12f, 1.22f, 1.26f), Vector3.one * 0.1f);
                Art.Part(t, Art.Sphere, Color.black, new Vector3(0.12f, 1.22f, 1.3f), Vector3.one * 0.05f);
                Art.Part(t, Art.Sphere, Color.black, new Vector3(-0.12f, 1.22f, 1.3f), Vector3.one * 0.05f);
                // wheels
                for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var w = new GameObject("wheel").transform;
                    w.SetParent(t, false);
                    w.localPosition = new Vector3(x * 0.85f, 0.4f, z * 0.85f);
                    Art.Part(w, Art.Cylinder, Art.DarkWood, Vector3.zero, new Vector3(0.8f, 0.08f, 0.8f), new Vector3(0, 0, 90));
                    Art.Box(w, plank, Vector3.zero, new Vector3(0.18f, 0.7f, 0.1f));
                    Art.Box(w, plank, Vector3.zero, new Vector3(0.18f, 0.1f, 0.7f));
                    wheels?.Add(w);
                }
            }
            else if (kind == Horse)
            {
                var coat = new Color(0.45f, 0.3f, 0.18f);
                var mane = new Color(0.18f, 0.12f, 0.08f);
                Art.Box(t, coat, new Vector3(0, 1.15f, 0), new Vector3(0.6f, 0.6f, 1.5f));
                // neck + head (head pivots to graze)
                var neck = new GameObject("neck").transform;
                neck.SetParent(t, false);
                neck.localPosition = new Vector3(0, 1.35f, 0.65f);
                Art.Box(neck, coat, new Vector3(0, 0.3f, 0.12f), new Vector3(0.32f, 0.7f, 0.35f), new Vector3(25, 0, 0));
                Art.Box(neck, mane, new Vector3(0, 0.35f, -0.02f), new Vector3(0.1f, 0.7f, 0.12f), new Vector3(25, 0, 0));
                Art.Box(neck, coat, new Vector3(0, 0.62f, 0.42f), new Vector3(0.3f, 0.3f, 0.6f));
                Art.Box(neck, mane * 1.5f, new Vector3(0, 0.56f, 0.7f), new Vector3(0.26f, 0.2f, 0.12f));
                Art.Box(neck, Color.black, new Vector3(0.16f, 0.7f, 0.5f), new Vector3(0.02f, 0.06f, 0.06f));
                Art.Box(neck, Color.black, new Vector3(-0.16f, 0.7f, 0.5f), new Vector3(0.02f, 0.06f, 0.06f));
                Art.Box(neck, coat, new Vector3(0.09f, 0.82f, 0.25f), new Vector3(0.06f, 0.14f, 0.06f));
                Art.Box(neck, coat, new Vector3(-0.09f, 0.82f, 0.25f), new Vector3(0.06f, 0.14f, 0.06f));
                head = neck;
                var tl = new GameObject("tail").transform;
                tl.SetParent(t, false);
                tl.localPosition = new Vector3(0, 1.3f, -0.76f);
                Art.Box(tl, mane, new Vector3(0, -0.3f, -0.05f), new Vector3(0.12f, 0.65f, 0.12f));
                tail = tl;
                for (int i = 0; i < 4; i++)
                {
                    float x = i % 2 == 0 ? -0.2f : 0.2f, z = i < 2 ? 0.55f : -0.55f;
                    var leg = new GameObject("leg").transform;
                    leg.SetParent(t, false);
                    leg.localPosition = new Vector3(x, 0.9f, z);
                    Art.Box(leg, coat, new Vector3(0, -0.4f, 0), new Vector3(0.16f, 0.8f, 0.16f));
                    Art.Box(leg, mane, new Vector3(0, -0.85f, 0), new Vector3(0.18f, 0.12f, 0.18f));
                    legs?.Add(leg);
                }
                var sd = new GameObject("saddle").transform;
                sd.SetParent(t, false);
                Art.Box(sd, new Color(0.35f, 0.18f, 0.08f), new Vector3(0, 1.47f, -0.05f), new Vector3(0.64f, 0.08f, 0.55f));
                Art.Box(sd, new Color(0.35f, 0.18f, 0.08f), new Vector3(0, 1.55f, 0.2f), new Vector3(0.3f, 0.12f, 0.08f));
                var blanket = Art.Box(sd, new Color(0.8f, 0.2f, 0.15f), new Vector3(0, 1.2f, -0.05f), new Vector3(0.66f, 0.5f, 0.45f));
                blanket.name = "blanket";
                saddle = sd;
            }
            else if (kind == Slender)
            {
                // tall, thin, black suit, blank white face, tendrils on its back
                var suit = new Color(0.05f, 0.05f, 0.06f);
                var skin = new Color(0.93f, 0.93f, 0.95f);
                Art.Box(t, suit, new Vector3(0.1f, 0.65f, 0), new Vector3(0.14f, 1.3f, 0.16f));
                Art.Box(t, suit, new Vector3(-0.1f, 0.65f, 0), new Vector3(0.14f, 1.3f, 0.16f));
                Art.Box(t, suit, new Vector3(0, 1.75f, 0), new Vector3(0.46f, 0.95f, 0.26f));
                Art.Box(t, Color.white, new Vector3(0, 2.05f, 0.14f), new Vector3(0.1f, 0.3f, 0.02f));
                Art.Box(t, new Color(0.6f, 0.05f, 0.05f), new Vector3(0, 1.95f, 0.15f), new Vector3(0.05f, 0.28f, 0.02f));
                var hd = new GameObject("head").transform;
                hd.SetParent(t, false);
                hd.localPosition = new Vector3(0, 2.3f, 0);
                Art.Part(hd, Art.Sphere, skin, new Vector3(0, 0.16f, 0), new Vector3(0.3f, 0.4f, 0.32f));
                head = hd;
                for (int k = -1; k <= 1; k += 2)
                {
                    var arm = new GameObject("arm").transform;
                    arm.SetParent(t, false);
                    arm.localPosition = new Vector3(k * 0.3f, 2.15f, 0);
                    Art.Box(arm, suit, new Vector3(0, -0.6f, 0), new Vector3(0.1f, 1.2f, 0.1f));
                    Art.Box(arm, skin, new Vector3(0, -1.28f, 0), new Vector3(0.08f, 0.2f, 0.06f));
                    legs?.Add(arm);
                }
                for (int k = 0; k < 4; k++)
                {
                    var ten = new GameObject("tendril").transform;
                    ten.SetParent(t, false);
                    ten.localPosition = new Vector3((k - 1.5f) * 0.12f, 2.0f, -0.15f);
                    ten.localRotation = Quaternion.Euler(-40f - k * 8f, (k - 1.5f) * 25f, 0);
                    Art.Box(ten, suit, new Vector3(0, 0.5f, 0), new Vector3(0.05f, 1f, 0.05f));
                    legs?.Add(ten);
                }
            }
            if (ghost != null)
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = ghost;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            return root;
        }
    }
}
