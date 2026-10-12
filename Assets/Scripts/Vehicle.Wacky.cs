using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The wacky items that move about the world, on the vehicle prefab (its NetworkTransform syncs them):
    /// the DECOY - a copy of whoever used it, holding what they held, that runs at the nearest enemy and goes up in smoke
    /// (with a laugh) the moment anything hits it; the BLUE SHELL - it climbs, then homes in on its target over everything
    /// and knocks them flat; the LITTLE TURRET BOT - driven by whoever put it down (it's theirs: they move it), it blows up
    /// on LMB; and the DRAWN MONSTER - the user's drawing (MonsterArt) walking about as a cut-out, hunting an enemy to
    /// splat itself all over their screen.
    /// </summary>
    public partial class Vehicle
    {
        public const byte Decoy = 5, Shell = 6, Drone = 7, Monster = 8;
        public bool IsDecoy => Kind.Value == Decoy;
        public bool IsShell => Kind.Value == Shell;
        public bool IsDrone => Kind.Value == Drone;
        public bool IsMonster => Kind.Value == Monster;
        public bool IsWackyKind => Kind.Value >= Decoy && Kind.Value <= Monster;

        /// <summary>Who it belongs to (the player's NetworkObjectId), what the decoy holds, who the shell is after.</summary>
        public readonly NetworkVariable<ulong> WackyOwner = new NetworkVariable<ulong>();
        public readonly NetworkVariable<byte> WackyItem = new NetworkVariable<byte>();
        public readonly NetworkVariable<ulong> WackyTarget = new NetworkVariable<ulong>();
        /// <summary>The drawn monster's picture (MonsterArt, base64).</summary>
        public readonly NetworkVariable<FixedString4096Bytes> MonsterPic = new NetworkVariable<FixedString4096Bytes>();

        float m_WackyDieAt, m_WackyBorn;
        BodyAnimator m_DecoyAnim;
        Transform m_DecoyHand, m_WackySpin;
        GameObject m_DecoyItem;
        readonly List<Material> m_DecoyMats = new List<Material>();

        PlayerNet WackyOwnerPlayer => NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(WackyOwner.Value, out var no) ? no.GetComponent<PlayerNet>() : null;

        string WackyName
        {
            get
            {
                var o = WackyOwnerPlayer;
                if (IsDecoy) return o != null ? o.ApparentName : "Decoy";
                if (IsShell) return "Blue Shell";
                if (IsDrone) return o != null ? $"{o.DisplayName}'s Turret Bot" : "Turret Bot";
                return o != null ? $"{o.DisplayName}'s Monster" : "Drawn Monster";
            }
        }

        float WackyMaxHp => IsDecoy ? 1f : IsShell ? 9999f : IsDrone ? Cfg.DroneHp : 60f;

        // ---------------- spawning (server) ----------------

        static Vehicle SpawnWacky(byte kind, Vector3 pos, float yaw, PlayerNet owner)
        {
            var go = Instantiate(Bootstrap.I.vehiclePrefab, pos, Quaternion.Euler(0, yaw, 0));
            var v = go.GetComponent<Vehicle>();
            v.Kind.Value = kind;
            v.OwnerTeam.Value = (byte)(owner != null ? owner.ApparentTeam : 0);
            v.WackyOwner.Value = owner != null ? owner.NetworkObjectId : 0;
            v.Hp.Value = v.WackyMaxHp;
            v.m_WackyBorn = Time.time;
            return v;
        }

        public static Vehicle ServerSpawnDecoy(Vector3 pos, float yaw, PlayerNet owner, Item held)
        {
            var v = SpawnWacky(Decoy, pos, yaw, owner);
            v.WackyItem.Value = (byte)held;
            v.m_WackyDieAt = Time.time + Cfg.DecoyLife;
            v.GetComponent<NetworkObject>().Spawn(true);
            return v;
        }

        public static Vehicle ServerSpawnShell(Vector3 pos, PlayerNet owner, PlayerNet target)
        {
            var v = SpawnWacky(Shell, pos, owner != null ? owner.transform.eulerAngles.y : 0f, owner);
            v.WackyTarget.Value = target != null ? target.NetworkObjectId : 0;
            v.m_WackyDieAt = Time.time + 25f;
            v.GetComponent<NetworkObject>().Spawn(true);
            return v;
        }

        public static Vehicle ServerSpawnDrone(Vector3 pos, float yaw, PlayerNet owner)
        {
            var v = SpawnWacky(Drone, pos, yaw, owner);
            v.OwnerTeam.Value = (byte)(owner != null ? owner.Team.Value : 0);
            v.m_WackyDieAt = float.MaxValue;
            var no = v.GetComponent<NetworkObject>();
            no.Spawn(true);
            // whoever put it down drives it (their machine moves it: NetworkTransform in owner mode)
            if (owner != null && owner.OwnerClientId != NetworkManager.ServerClientId && !owner.Bot.Value) no.ChangeOwnership(owner.OwnerClientId);
            return v;
        }

        public static Vehicle ServerSpawnMonster(Vector3 pos, PlayerNet owner, byte[] pixels)
        {
            var v = SpawnWacky(Monster, pos, owner != null ? owner.transform.eulerAngles.y : 0f, owner);
            v.MonsterPic.Value = new FixedString4096Bytes(System.Convert.ToBase64String(pixels));
            v.m_WackyDieAt = Time.time + Cfg.MonsterLife;
            v.GetComponent<NetworkObject>().Spawn(true);
            Fx.Server(FxKind.Scribble, pos + Vector3.up, Vector3.up);
            return v;
        }

        // ---------------- damage (server) ----------------

        /// <summary>Server, at the top of ServerDamage: true = handled here.</summary>
        bool ServerWackyDamage(float dmg, PlayerNet attacker)
        {
            if (!IsWackyKind) return false;
            if (dmg <= 0f || IsShell) return true; // (nothing stops a blue shell)
            if (attacker != null && attacker.NetworkObjectId == WackyOwner.Value && !IsMonster) return true; // (not your own decoy / bot)
            if (IsDecoy)
            {
                // one hit and it's gone - in a puff of smoke, laughing at you
                Fx.Server(FxKind.DecoyPoof, transform.position + Vector3.up, Vector3.up);
                NetworkObject.Despawn(true);
                return true;
            }
            Hp.Value = Mathf.Max(0f, Hp.Value - dmg);
            Fx.Server(IsDrone ? FxKind.Smash : FxKind.Scribble, transform.position + Vector3.up * 0.5f, Vector3.up);
            if (Hp.Value > 0f) return true;
            if (IsDrone) { var o = WackyOwnerPlayer; if (o != null) o.DroneId.Value = 0; Fx.Server(FxKind.Explosion, transform.position, Vector3.up); }
            else Fx.Server(FxKind.BigPoof, transform.position + Vector3.up, Vector3.up);
            NetworkObject.Despawn(true);
            return true;
        }

        // ---------------- every frame ----------------

        void TickWacky(float dt)
        {
            if (IsServer && Time.time >= m_WackyDieAt && !IsDrone)
            {
                Fx.Server(IsDecoy ? FxKind.DecoyPoof : FxKind.Poof, transform.position + Vector3.up, Vector3.up);
                NetworkObject.Despawn(true);
                return;
            }
            switch (Kind.Value)
            {
                case Decoy: if (IsServer) ServerChase(dt, Cfg.DecoySpeed, 1.2f, false); AnimateDecoy(dt); break;
                case Monster: if (IsServer) ServerChase(dt, Cfg.MonsterSpeed, 2.2f, true); AnimateMonster(dt); break;
                case Shell: if (IsServer) ServerShell(dt); if (m_WackySpin) m_WackySpin.Rotate(0, 720f * dt, 0, Space.Self); break;
                case Drone:
                {
                    if (IsOwner) DriveDrone(dt);
                    // its driver doesn't see the bot itself (the camera's on its nose)
                    bool mine = PlayerNet.Local != null && PlayerNet.Local.DroneId.Value == NetworkObjectId;
                    if (m_Visual && m_Visual.gameObject.activeSelf == mine) m_Visual.gameObject.SetActive(!mine);
                    if (m_WackySpin) m_WackySpin.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 9f) * 4f);
                    break;
                }
            }
        }

        /// <summary>Server: run at the nearest enemy of whoever made it (jumping over what's in the way). The monster splats
        /// all over their screen when it gets them; the decoy just keeps after them.</summary>
        void ServerChase(float dt, float speed, float reach, bool splat)
        {
            PlayerNet target = null;
            float best = float.MaxValue;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p.Dead.Value || p.Team.Value == OwnerTeam.Value || p.NetworkObjectId == WackyOwner.Value) continue;
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
                if (splat && best < reach)
                {
                    target.MonsterSplatRpc(MonsterPic.Value);
                    Fx.Server(FxKind.Scribble, transform.position + Vector3.up, Vector3.up);
                    NetworkObject.Despawn(true);
                    return;
                }
                if (to.sqrMagnitude > 0.01f) m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, Quaternion.LookRotation(to).eulerAngles.y, 540f * dt);
                // (the decoy runs right up to them and dances round them; it doesn't stop)
                move = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward * (best < reach ? speed * 0.4f : speed);
            }
            var flags = m_CC.Move((move + Vector3.up * m_VelY) * dt);
            if ((flags & CollisionFlags.Sides) != 0 && grounded) m_VelY = 8f; // (hop over things)
            transform.rotation = Quaternion.Euler(0, m_Yaw, 0);
            if (transform.position.y < -40f) { NetworkObject.Despawn(true); return; }
            // stuck behind something too tall to hop: it blinks past it, a few metres closer (in a puff)
            if (target != null && move.sqrMagnitude > 0.1f)
            {
                if (Time.time >= m_ChaseCheckAt)
                {
                    bool stuck = Vector3.Distance(transform.position, m_ChaseFrom) < 0.6f;
                    m_ChaseFrom = transform.position;
                    m_ChaseCheckAt = Time.time + 0.8f;
                    if (stuck)
                    {
                        var to = target.transform.position - transform.position; to.y = 0f;
                        var p = transform.position + to.normalized * Mathf.Min(3f, Mathf.Max(0f, to.magnitude - 1f));
                        p = PlayerNet.WackyGround(p, 4f) + Vector3.up * 0.1f;
                        Fx.Server(FxKind.Poof, transform.position + Vector3.up, Vector3.up);
                        m_CC.enabled = false;
                        transform.position = p;
                        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
                        if (nt != null && nt.IsSpawned) nt.Teleport(p, transform.rotation, transform.localScale);
                        m_CC.enabled = true;
                        m_ChaseFrom = p;
                    }
                }
            }
            else m_ChaseCheckAt = Time.time + 0.8f;
        }

        float m_ShellRise = -1f, m_ChaseCheckAt;
        Vector3 m_ChaseFrom;

        /// <summary>Server: the blue shell climbs high, then flies straight at its target (through everything) and hits.</summary>
        void ServerShell(float dt)
        {
            if (m_CC != null && m_CC.enabled) m_CC.enabled = false;
            var target = NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(WackyTarget.Value, out var no) ? no.GetComponent<PlayerNet>() : null;
            if (m_ShellRise < 0f) m_ShellRise = Time.time;
            var pos = transform.position;
            if (Time.time - m_ShellRise < 1.1f) { transform.position = pos + Vector3.up * 12f * dt; return; }
            if (target == null || target.Dead.Value)
            {
                Fx.Server(FxKind.ShellBoom, pos, Vector3.up);
                NetworkObject.Despawn(true);
                return;
            }
            var aim = target.transform.position + Vector3.up * 1.1f;
            var d = aim - pos;
            float dist = d.magnitude;
            // high over everything until it's nearly over them, then it dives straight down onto them
            float flat = new Vector2(d.x, d.z).magnitude;
            if (flat > 6f) d.y = Mathf.Max(d.y, (aim.y + 14f) - pos.y);
            if (dist < 1.6f)
            {
                target.ServerKnockDown(pos);
                NetworkObject.Despawn(true);
                return;
            }
            var step = d.normalized * Mathf.Min(dist, Cfg.ShellSpeed * dt);
            transform.position = pos + step;
            if (step.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(new Vector3(step.x, 0, step.z).sqrMagnitude > 1e-6f ? new Vector3(step.x, 0, step.z) : transform.forward);
        }

        /// <summary>The bot's driver (their machine): W/S/A/D along where they look, Space hops.</summary>
        void DriveDrone(float dt)
        {
            var me = PlayerNet.Local;
            if (me == null || me.DroneId.Value != NetworkObjectId) return;
            var pc = PlayerController.Local;
            float f = 0f, s = 0f, look = m_Yaw;
            bool jump = false, sprint = false;
            if (pc != null) pc.GetDriveInput(out f, out s, out jump, out sprint, out look);
            m_Yaw = look;
            bool grounded = m_CC.isGrounded;
            if (grounded && m_VelY < 0) m_VelY = -2f;
            if (jump && grounded) m_VelY = 5.5f;
            m_VelY -= Cfg.Gravity * dt;
            var rot = Quaternion.Euler(0, m_Yaw, 0);
            var wish = rot * new Vector3(s, 0, f);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            m_Planar = Vector3.MoveTowards(m_Planar, wish * Cfg.DroneSpeed * (sprint ? 1.3f : 1f), 30f * dt);
            m_CC.Move((m_Planar + Vector3.up * m_VelY) * dt);
            transform.rotation = rot;
            if (transform.position.y < -40f) { m_CC.enabled = false; transform.position = new Vector3(transform.position.x, MapBuilder.Height(transform.position.x, transform.position.z) + 1f, transform.position.z); m_CC.enabled = true; }
        }

        /// <summary>Where the driver's camera sits on the bot (low, on its nose).</summary>
        public Vector3 DroneEye => transform.position + Vector3.up * 0.42f + transform.forward * 0.3f;

        // ---------------- looks ----------------

        /// <summary>OnNetworkSpawn: its size and its looks.</summary>
        void SetupWacky()
        {
            switch (Kind.Value)
            {
                case Decoy: m_CC.radius = 0.4f; m_CC.height = 1.8f; m_CC.center = new Vector3(0, 0.9f, 0); BuildDecoy(); break;
                case Monster: m_CC.radius = 0.5f; m_CC.height = 2.2f; m_CC.center = new Vector3(0, 1.1f, 0); BuildMonster(); break;
                case Shell: m_CC.radius = 0.4f; m_CC.height = 0.8f; m_CC.center = new Vector3(0, 0.4f, 0); BuildShell(); break;
                case Drone: m_CC.radius = 0.24f; m_CC.height = 0.48f; m_CC.center = new Vector3(0, 0.24f, 0); m_CC.stepOffset = 0.2f; BuildDrone(); break;
            }
            m_Yaw = transform.eulerAngles.y;
        }

        void BuildDecoy()
        {
            var root = new GameObject("decoy").transform;
            root.SetParent(m_Visual, false);
            m_DecoyAnim = BodyAnimator.TryCreate(root, Cfg.ModelWidth, out var model);
            if (model != null)
            {
                PlayerNet.SkinAlien(model, m_DecoyMats);
                int team = OwnerTeam.Value;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        var c = PlayerNet.TeamTint(m, team);
                        m.SetColor("_BaseColor", c);
                        m.color = c;
                    }
            }
            else
            {
                var c = Cfg.TeamColor[Mathf.Clamp(OwnerTeam.Value, 0, 3)];
                Art.Part(root, Art.Capsule, Color.Lerp(Color.white, c, 0.6f), new Vector3(0, 0.85f, 0), new Vector3(0.75f, 0.8f, 0.6f));
                Art.Part(root, Art.Sphere, Color.Lerp(Color.white, c, 0.4f), new Vector3(0, 1.6f, 0), Vector3.one * 0.45f);
            }
            m_DecoyHand = new GameObject("hand").transform;
            m_DecoyHand.SetParent(root, false);
            m_DecoyHand.localPosition = new Vector3(0.45f, 0.85f, 0.15f);
            var held = (Item)WackyItem.Value;
            if (held != Item.None) m_DecoyItem = ItemModels.Create(held, m_DecoyHand);
            // its hit box, like a player's: shots find it, nothing bumps into it
            var hb = new GameObject("decoy hitbox") { layer = PlayerNet.HitboxLayer };
            hb.transform.SetParent(transform, false);
            var cap = hb.AddComponent<CapsuleCollider>();
            cap.radius = Cfg.HitboxRadius; cap.height = 1.85f; cap.center = new Vector3(0, 0.925f, 0);
        }

        void AnimateDecoy(float dt)
        {
            if (m_DecoyAnim == null) return;
            var held = (Item)WackyItem.Value;
            m_DecoyAnim.Tick(new BodyAnimator.Pose { Holding = held != Item.None, Item = held, TwoHanded = held == Item.Rock || held == Item.Spear || Cfg.IsRam(held) || held == Item.Crossbow || held == Item.Shotgun }, dt);
            m_DecoyAnim.GripPose(out var gp, out var gr);
            if (m_DecoyHand) m_DecoyHand.SetPositionAndRotation(gp, gr);
        }

        void BuildShell()
        {
            if (m_CC != null) m_CC.enabled = false;
            var spin = new GameObject("spin").transform;
            spin.SetParent(m_Visual, false);
            spin.localPosition = new Vector3(0, 0.4f, 0);
            m_WackySpin = spin;
            var blue = new Color(0.15f, 0.35f, 1f);
            Art.Part(spin, Art.Sphere, blue, Vector3.zero, new Vector3(1.0f, 0.55f, 1.0f));
            Art.Part(spin, Art.Cylinder, Color.white, new Vector3(0, -0.12f, 0), new Vector3(1.08f, 0.06f, 1.08f));
            Art.Part(spin, Art.Sphere, new Color(1f, 0.95f, 0.6f), new Vector3(0, -0.2f, 0), new Vector3(0.85f, 0.2f, 0.85f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f * Mathf.Deg2Rad;
                Art.Part(spin, Art.Cone, Color.white, new Vector3(Mathf.Cos(a) * 0.32f, 0.2f, Mathf.Sin(a) * 0.32f), new Vector3(0.14f, 0.24f, 0.14f), new Vector3(Mathf.Sin(a) * 35f, 0, -Mathf.Cos(a) * 35f));
            }
            Art.Part(spin, Art.Cone, Color.white, new Vector3(0, 0.27f, 0), new Vector3(0.16f, 0.26f, 0.16f));
            // little white wings either side
            for (int s = -1; s <= 1; s += 2)
            {
                var wing = Art.Box(m_Visual, Color.white, new Vector3(s * 0.75f, 0.55f, 0), new Vector3(0.6f, 0.04f, 0.3f), new Vector3(0, 0, s * 20f));
                wing.name = "wing";
            }
            foreach (var r in m_Visual.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void BuildDrone()
        {
            var body = new GameObject("bot").transform;
            body.SetParent(m_Visual, false);
            m_WackySpin = body;
            var dark = new Color(0.16f, 0.17f, 0.2f);
            var team = Cfg.TeamColor[Mathf.Clamp(OwnerTeam.Value, 0, 3)];
            Art.Box(body, dark, new Vector3(0, 0.22f, 0), new Vector3(0.34f, 0.16f, 0.3f));
            Art.Box(body, team, new Vector3(0, 0.305f, -0.02f), new Vector3(0.24f, 0.02f, 0.2f));
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Part(body, Art.Cylinder, new Color(0.08f, 0.08f, 0.09f), new Vector3(s * 0.21f, 0.2f, 0), new Vector3(0.36f, 0.05f, 0.36f), new Vector3(0, 0, 90));
                Art.Part(body, Art.Cylinder, new Color(0.35f, 0.36f, 0.4f), new Vector3(s * 0.24f, 0.2f, 0), new Vector3(0.14f, 0.02f, 0.14f), new Vector3(0, 0, 90));
            }
            Art.Part(body, Art.Sphere, new Color(0.05f, 0.05f, 0.06f), new Vector3(0, 0.24f, 0.16f), new Vector3(0.11f, 0.11f, 0.06f));
            Art.Part(body, Art.Sphere, Color.white, new Vector3(0, 0.24f, 0.18f), new Vector3(0.05f, 0.05f, 0.03f), default, false, Art.Ghost(new Color(team.r, team.g, team.b, 0.9f)));
            Art.Box(body, dark, new Vector3(0.08f, 0.42f, -0.1f), new Vector3(0.012f, 0.22f, 0.012f), new Vector3(-12, 0, 0));
            Art.Part(body, Art.Sphere, new Color(1f, 0.2f, 0.15f), new Vector3(0.08f, 0.53f, -0.12f), Vector3.one * 0.03f);
            // shots can hit it: a box on the hit box layer
            var hb = new GameObject("bot hitbox") { layer = PlayerNet.HitboxLayer };
            hb.transform.SetParent(transform, false);
            var bc = hb.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, 0.22f, 0); bc.size = new Vector3(0.5f, 0.36f, 0.4f);
        }

        Texture2D m_MonsterTex;

        void BuildMonster()
        {
            m_MonsterTex = MonsterArt.Texture(MonsterPic.Value.ToString());
            var card = new GameObject("monster").transform;
            card.SetParent(m_Visual, false);
            card.localPosition = new Vector3(0, 1.25f, 0);
            m_WackySpin = card;
            var mat = MonsterArt.CardMaterial(m_MonsterTex);
            for (int side = 0; side < 2; side++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(card, false);
                q.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
                q.transform.localRotation = Quaternion.Euler(0, side == 0 ? 180f : 0f, 0); // (both sides show it)
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var hb = new GameObject("monster hitbox") { layer = PlayerNet.HitboxLayer };
            hb.transform.SetParent(transform, false);
            var bc = hb.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, 1.25f, 0); bc.size = new Vector3(1.8f, 2.4f, 0.4f);
        }

        void AnimateMonster(float dt)
        {
            if (!m_WackySpin) return;
            // a waddle: rocking from side to side and bobbing as it goes
            float t = Time.time * 7f;
            m_WackySpin.localPosition = new Vector3(0, 1.25f + Mathf.Abs(Mathf.Sin(t)) * 0.25f, 0);
            m_WackySpin.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t) * 12f);
        }

        public override void OnDestroy()
        {
            if (m_MonsterTex) Destroy(m_MonsterTex);
            base.OnDestroy();
        }
    }

    /// <summary>
    /// The drawing from the Draw a Monster canvas: 64 x 64, 16 colours (4 bits a pixel, 2048 bytes). Colour 0 is the
    /// paper (it comes out see-through on the monster).
    /// </summary>
    public static class MonsterArt
    {
        public const int Size = 64, Bytes = Size * Size / 2;
        public static readonly Color[] Palette =
        {
            Color.white, Color.black, new Color(0.9f, 0.15f, 0.15f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.9f, 0.15f), new Color(0.3f, 0.8f, 0.25f),
            new Color(0.1f, 0.5f, 0.15f), new Color(0.2f, 0.75f, 0.95f), new Color(0.15f, 0.3f, 0.9f), new Color(0.55f, 0.25f, 0.85f), new Color(1f, 0.5f, 0.8f),
            new Color(0.55f, 0.35f, 0.2f), new Color(0.5f, 0.5f, 0.5f), new Color(0.8f, 0.8f, 0.8f), new Color(1f, 0.85f, 0.7f), new Color(0.15f, 0.15f, 0.2f),
        };

        public static int Get(byte[] px, int x, int y)
        {
            int i = y * Size + x;
            int b = px[i >> 1];
            return (i & 1) == 0 ? b & 15 : b >> 4;
        }

        public static void Set(byte[] px, int x, int y, int c)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            int i = y * Size + x;
            int b = px[i >> 1];
            px[i >> 1] = (byte)((i & 1) == 0 ? (b & 0xF0) | (c & 15) : (b & 0x0F) | ((c & 15) << 4));
        }

        /// <summary>The drawing as a texture (the paper see-through), or a scribble if it's missing.</summary>
        public static Texture2D Texture(string base64, bool paper = false)
        {
            byte[] px = null;
            try { if (!string.IsNullOrEmpty(base64)) px = System.Convert.FromBase64String(base64); } catch (System.FormatException) { px = null; }
            if (px == null || px.Length != Bytes) px = Scribble();
            return Texture(px, paper);
        }

        public static Texture2D Texture(byte[] px, bool paper)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var cols = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int c = Get(px, x, y);
                    var col = Palette[c];
                    cols[y * Size + x] = c == 0 && !paper ? new Color32(255, 255, 255, 0) : (Color32)col;
                }
            tex.SetPixels32(cols);
            tex.Apply();
            return tex;
        }

        /// <summary>A stand-in when no drawing arrived: a scribbly little monster.</summary>
        static byte[] Scribble()
        {
            var px = new byte[Bytes];
            for (int y = 12; y < 52; y++)
                for (int x = 14; x < 50; x++)
                {
                    float dx = (x - 32) / 18f, dy = (y - 32) / 20f;
                    if (dx * dx + dy * dy < 1f) Set(px, x, y, 5);
                }
            for (int k = -1; k <= 1; k += 2) { for (int y = 34; y < 40; y++) for (int x = 32 + k * 8 - 2; x < 32 + k * 8 + 3; x++) Set(px, x, y, 1); }
            for (int x = 24; x < 41; x++) Set(px, x, 24 + (x % 3), 2);
            return px;
        }

        /// <summary>A flat, unlit card with the drawing on it, see-through where it's paper, both sides (the always-included
        /// sprite shader: URP draws it as an unlit pass).</summary>
        public static Material CardMaterial(Texture2D tex)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
            m.renderQueue = 3000;
            return m;
        }
    }
}
