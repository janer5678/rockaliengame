using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class Container
    {
        /// <summary>Every screen: a placeable's moment (Deployables.Fx*): a trap snapping shut, a turret's warning, a shot.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void DeployFxRpc(byte what, Vector3 at)
        {
            Deployables.ClientFx(this, what, at);
        }
    }

    public partial class PlayerNet
    {
        /// <summary>Caught in a bear trap: can't move until then (server time).</summary>
        public readonly NetworkVariable<double> TrappedUntil = new NetworkVariable<double>(-1);
        public bool Trapped => TrappedUntil.Value > Now;

        /// <summary>Choosing where to respawn: at one of your team's sleeping bags (once a minute each).</summary>
        [Rpc(SendTo.Server)]
        public void RespawnAtBagRpc(NetworkObjectReference bagRef)
        {
            var g = NetGame.Instance;
            if (!Dead.Value || g == null || NetworkManager.ServerTime.Time < RespawnAt.Value || g.S != GameState.BallLive) return;
            if (!bagRef.TryGet(out var no) || !no.TryGetComponent(out Container bag) || bag.Kind.Value != Container.SleepBag || bag.Team.Value != Team.Value) return;
            if (bag.ReadyAt.Value > NetworkManager.ServerTime.Time) { Notify("That sleeping bag isn't ready yet"); return; }
            bag.ReadyAt.Value = NetworkManager.ServerTime.Time + Cfg.SleepingBagCooldown;
            ServerRespawn(false);
            var at = bag.transform.position + bag.transform.right * 0.9f + Vector3.up * 0.1f;
            TeleportRpc(at, bag.transform.eulerAngles.y);
        }
    }

    public static partial class Cfg
    {
        /// <summary>A sleeping bag: how long before anyone can respawn at it again.</summary>
        [Tune("Crafting")] public static float SleepingBagCooldown = 60f;
        /// <summary>A bear trap: the damage when it snaps, how long it holds you, and how long until it's set again.</summary>
        [Tune("Crafting")] public static float BearTrapDamage = 30f, BearTrapHold = 3.5f, BearTrapRearm = 20f;
        /// <summary>The auto turret: how far it sees, and its cone either side of where it faces (degrees).</summary>
        [Tune("Crafting")] public static float TurretRange = 32f, TurretCone = 65f;
    }

    /// <summary>
    /// The Trade Station's placeables and the Advanced Trade Station's auto turret - kinds of Container (so they're
    /// networked, take damage and break, and come back to the hand with a held E on an empty one):
    /// - SLEEPING BAG: anywhere but an enemy base; when you're dead, your team's bags are options on the respawn screen
    ///   (each can be used once a minute: ReadyAt).
    /// - BEAR TRAP: in the team colour, anywhere - your base, the wild or theirs - on the ground or a floor (not on a
    ///   foundation). An enemy stepping on it is snapped (BearTrapDamage) and held (BearTrapHold: PlayerNet.Trapped);
    ///   it sets itself again after BearTrapRearm.
    /// - LADDER: anywhere (to scale someone's walls, or your own); a climbable volume like the fort tower's (Ladder).
    /// - AUTO TURRET: in your own base. E on it opens its two slots - a ranged weapon (or a spear) and its ammo. It
    ///   watches a cone in front of it (shown on the placing ghost), hums as it pans, and with an enemy in view gives a
    ///   loud warning beep, then fires at the weapon's own pace while the ammo lasts.
    /// </summary>
    public static class Deployables
    {
        public const byte FxSnap = 1, FxWarn = 2, FxShot = 3, FxHum = 4;
        public const float LadderHeight = 6.2f;

        public static float CenterUp(byte kind) => kind == Container.SleepBag ? 0.2f : kind == Container.Trap ? 0.15f : kind == Container.Ladder ? 1.4f : 0.9f;
        public static float MaxHp(byte kind) => kind == Container.SleepBag ? 100f : kind == Container.Trap ? 150f : kind == Container.Ladder ? 250f : 500f;

        // ------------------------------------------------------------------ the models

        public static void Build(byte kind, int team, Transform t, Material ghost)
        {
            var tc = Cfg.TeamColor[Mathf.Clamp(team, 0, 3)];
            switch (kind)
            {
                case Container.SleepBag:
                {
                    // a rolled-out bag in the team colour with a pillow and a zip
                    var cloth = Color.Lerp(tc, new Color(0.35f, 0.3f, 0.25f), 0.45f);
                    Art.Box(t, cloth, new Vector3(0, 0.07f, 0), new Vector3(0.8f, 0.14f, 1.9f));
                    Art.Box(t, cloth * 0.8f, new Vector3(0, 0.15f, 0.25f), new Vector3(0.76f, 0.05f, 1.3f));
                    Art.Box(t, new Color(0.9f, 0.88f, 0.82f), new Vector3(0, 0.16f, -0.72f), new Vector3(0.55f, 0.14f, 0.32f));
                    Art.Box(t, new Color(0.8f, 0.8f, 0.82f), new Vector3(0.36f, 0.15f, 0.25f), new Vector3(0.02f, 0.02f, 1.2f));
                    break;
                }
                case Container.Trap:
                {
                    // a round steel plate, two toothed jaws (opened flat: "jawL" / "jawR", they snap up), a team-colour ring
                    var steel = new Color(0.42f, 0.42f, 0.45f);
                    float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
                    Art.Part(t, Art.Cylinder, steel * 0.8f, new Vector3(0, 0.02f, 0), new Vector3(0.62f / cr, 0.04f / ch, 0.62f / cr));
                    Art.Part(t, Art.Cylinder, tc, new Vector3(0, 0.045f, 0), new Vector3(0.3f / cr, 0.02f / ch, 0.3f / cr));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var jaw = new GameObject(s < 0 ? "jawL" : "jawR").transform;
                        jaw.SetParent(t, false);
                        jaw.localPosition = new Vector3(s * 0.04f, 0.05f, 0);
                        Art.Box(jaw, steel, new Vector3(s * 0.15f, 0, 0), new Vector3(0.3f, 0.02f, 0.62f));
                        for (int k = -3; k <= 3; k++) Art.Box(jaw, steel * 1.2f, new Vector3(s * 0.29f, 0.03f, k * 0.085f), new Vector3(0.03f, 0.06f, 0.03f));
                        Art.Box(jaw, tc, new Vector3(s * 0.15f, 0.012f, 0), new Vector3(0.22f, 0.006f, 0.08f));
                    }
                    break;
                }
                case Container.Ladder:
                {
                    // two rails and rungs, leaning in a little towards +z (the wall it's put against)
                    var wood = new Color(0.5f, 0.34f, 0.18f);
                    var lean = new GameObject("lean").transform;
                    lean.SetParent(t, false);
                    lean.localRotation = Quaternion.Euler(10f, 0, 0);
                    for (int s = -1; s <= 1; s += 2) Art.Box(lean, wood * 0.85f, new Vector3(s * 0.3f, LadderHeight * 0.5f, 0), new Vector3(0.07f, LadderHeight, 0.08f));
                    for (float y = 0.3f; y < LadderHeight; y += 0.36f) Art.Box(lean, wood, new Vector3(0, y, 0), new Vector3(0.6f, 0.045f, 0.05f));
                    Art.Box(lean, Cfg.TeamColor[Mathf.Clamp(team, 0, 3)], new Vector3(0, LadderHeight - 0.15f, 0.045f), new Vector3(0.3f, 0.06f, 0.01f));
                    break;
                }
                default: // the auto turret
                {
                    var metal = new Color(0.32f, 0.34f, 0.38f);
                    float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
                    // a squat base with three legs, a turning ring, then the head on a yaw pivot and a pitch pivot
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * 120f;
                        Art.Box(t, metal * 0.8f, Quaternion.Euler(0, a, 0) * new Vector3(0, 0.15f, 0.25f), new Vector3(0.08f, 0.3f, 0.5f), new Vector3(-35f, a, 0));
                    }
                    Art.Part(t, Art.Cylinder, metal, new Vector3(0, 0.35f, 0), new Vector3(0.45f / cr, 0.2f / ch, 0.45f / cr));
                    Art.Part(t, Art.Cylinder, tc, new Vector3(0, 0.46f, 0), new Vector3(0.5f / cr, 0.03f / ch, 0.5f / cr));
                    var yaw = new GameObject("turretYaw").transform;
                    yaw.SetParent(t, false);
                    yaw.localPosition = new Vector3(0, 0.5f, 0);
                    Art.Box(yaw, metal, new Vector3(0, 0.12f, 0), new Vector3(0.36f, 0.24f, 0.36f));
                    var pitch = new GameObject("turretPitch").transform;
                    pitch.SetParent(yaw, false);
                    pitch.localPosition = new Vector3(0, 0.36f, 0);
                    Art.Box(pitch, metal * 1.15f, new Vector3(0, 0.02f, 0.05f), new Vector3(0.42f, 0.26f, 0.5f));
                    Art.Box(pitch, metal * 0.7f, new Vector3(0, 0.02f, 0.32f), new Vector3(0.16f, 0.12f, 0.12f));
                    // its eye: a red lens on the front, and a team stripe
                    Art.Part(pitch, Art.Sphere, Color.white, new Vector3(0.12f, 0.08f, 0.31f), Vector3.one * 0.08f, default, false, Workbench.Glow(new Color(1f, 0.15f, 0.1f), 2f), "eye");
                    Art.Box(pitch, tc, new Vector3(0, 0.155f, 0.05f), new Vector3(0.43f, 0.02f, 0.3f));
                    var mount = new GameObject("weaponMount").transform;
                    mount.SetParent(pitch, false);
                    mount.localPosition = new Vector3(-0.05f, -0.02f, 0.3f);
                    break;
                }
            }
            if (ghost != null) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = ghost;
            if (ghost != null && kind == Container.Turret) BuildCone(t, ghost);
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) if (kind != Container.Turret) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The turret's ghost shows what it'll watch: a see-through fan out to its range across its cone.</summary>
        static void BuildCone(Transform t, Material ghost)
        {
            const int seg = 24;
            var v = new List<Vector3> { new Vector3(0, 0.86f, 0) };
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(-Cfg.TurretCone, Cfg.TurretCone, i / (float)seg) * Mathf.Deg2Rad;
                v.Add(new Vector3(Mathf.Sin(a) * Cfg.TurretRange, 0.15f, Mathf.Cos(a) * Cfg.TurretRange));
                if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); tris.Add(0); tris.Add(i + 1); tris.Add(i); }
            }
            var mesh = new Mesh { name = "turret cone" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var go = new GameObject("cone");
            go.transform.SetParent(t, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var c = ghost.color;
            mr.sharedMaterial = Art.Ghost(new Color(1f, 0.25f, 0.2f, 0.12f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>A placed one's collider (and the ladder's climbable volume).</summary>
        public static void Setup(Container c, Transform visual, BoxCollider bc)
        {
            switch (c.Kind.Value)
            {
                case Container.SleepBag: bc.center = new Vector3(0, 0.1f, 0); bc.size = new Vector3(0.8f, 0.2f, 1.9f); break;
                case Container.Trap: bc.center = new Vector3(0, 0.05f, 0); bc.size = new Vector3(0.62f, 0.1f, 0.62f); bc.isTrigger = false; break;
                case Container.Ladder:
                {
                    // a thin board along the rails to hit / look at; the climbing volume in front of it (Ladder, like the
                    // fort tower's: walk into it and hold W)
                    bc.center = new Vector3(0, LadderHeight * 0.5f, LadderHeight * 0.5f * Mathf.Sin(10f * Mathf.Deg2Rad));
                    bc.size = new Vector3(0.75f, LadderHeight, 0.12f);
                    var lg = new GameObject("ladder");
                    lg.transform.SetParent(visual, false);
                    lg.transform.localPosition = new Vector3(0, (LadderHeight + 1f) * 0.5f, 0.15f);
                    var vol = lg.AddComponent<BoxCollider>();
                    vol.isTrigger = true;
                    vol.size = new Vector3(1f, LadderHeight + 1f, 1.3f);
                    lg.AddComponent<Ladder>().TopLocalY = LadderHeight - 0.4f;
                    break;
                }
                default: bc.center = new Vector3(0, 0.6f, 0); bc.size = new Vector3(0.6f, 1.2f, 0.6f); break;
            }
        }

        // ------------------------------------------------------------------ alive

        class TurretState { public PlayerNet Target; public float WarnedAt = -10f, NextShot, NextScan, NextHum; public Vector2 Aim; public Item Shown = Item.None; public Transform Model; }
        static readonly Dictionary<Container, TurretState> s_Turrets = new Dictionary<Container, TurretState>();
        static readonly Dictionary<Container, float> s_TrapSprung = new Dictionary<Container, float>();

        /// <summary>Every peer, every frame: the trap's jaws, the turret's head turning to its aim and its weapon in it;
        /// the server also springs traps and runs the turrets.</summary>
        public static void Tick(Container c, Transform visual)
        {
            switch (c.Kind.Value)
            {
                case Container.Trap:
                {
                    float shut = c.Flag.Value == 1 ? 1f : 0f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var jaw = visual.Find(s < 0 ? "jawL" : "jawR");
                        if (jaw) jaw.localRotation = Quaternion.Slerp(jaw.localRotation, Quaternion.Euler(0, 0, s * -82f * shut), Time.deltaTime * 25f);
                    }
                    if (c.IsServer) ServerTrap(c);
                    break;
                }
                case Container.Turret:
                {
                    if (!s_Turrets.TryGetValue(c, out var st)) s_Turrets[c] = st = new TurretState();
                    var yaw = visual.Find("turretYaw");
                    var pitch = yaw != null ? yaw.Find("turretPitch") : null;
                    if (yaw != null && pitch != null)
                    {
                        var want = c.IsServer ? st.Aim : c.Aim.Value;
                        var cur = new Vector2(yaw.localEulerAngles.y, pitch.localEulerAngles.x);
                        float ny = Mathf.MoveTowardsAngle(cur.x, want.x, 140f * Time.deltaTime);
                        float np = Mathf.MoveTowardsAngle(cur.y, want.y, 90f * Time.deltaTime);
                        // a hum while it pans (heard by everyone near it)
                        if (Mathf.Abs(Mathf.DeltaAngle(cur.x, ny)) > 0.4f && Time.time > st.NextHum) { st.NextHum = Time.time + 0.45f; Sfx.Play(Sfx.Hum, c.transform.position + Vector3.up, 0.5f, 0.1f, 28f); }
                        yaw.localRotation = Quaternion.Euler(0, ny, 0);
                        pitch.localRotation = Quaternion.Euler(np, 0, 0);
                        // the weapon it's been given, mounted in its head
                        var w = c.Slots.Count > 0 ? c.Slots[0].Id : Item.None;
                        if (w != st.Shown)
                        {
                            st.Shown = w;
                            if (st.Model) Object.Destroy(st.Model.gameObject);
                            st.Model = null;
                            var mount = pitch.Find("weaponMount");
                            if (mount != null && w != Item.None)
                            {
                                var m = ItemModels.Create(w, mount);
                                if (m != null) { st.Model = m.transform; st.Model.localRotation = Quaternion.identity; st.Model.localScale = Vector3.one * 1.3f; }
                            }
                        }
                    }
                    if (c.IsServer) ServerTurret(c, st);
                    break;
                }
            }
        }

        static void ServerTrap(Container c)
        {
            var now = NetworkManager.Singleton.ServerTime.Time;
            if (c.Flag.Value == 1)
            {
                if (s_TrapSprung.TryGetValue(c, out var at) && Time.time - at > Cfg.BearTrapRearm) { c.Flag.Value = 0; s_TrapSprung.Remove(c); }
                return;
            }
            var p0 = c.transform.position;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value || p.Team.Value == c.Team.Value || p.Riding) continue;
                var d = p.transform.position - p0;
                if (Mathf.Abs(d.y) > 0.7f || new Vector2(d.x, d.z).sqrMagnitude > 0.42f * 0.42f) continue;
                c.Flag.Value = 1;
                s_TrapSprung[c] = Time.time;
                p.TrappedUntil.Value = now + Cfg.BearTrapHold;
                p.ServerDamage(Cfg.BearTrapDamage, null, (byte)Item.BearTrap);
                c.DeployFxRpc(FxSnap, p0);
                break;
            }
        }

        /// <summary>What a turret weapon fires and how: its ammo (None: the weapon's own count), seconds between shots,
        /// damage, spread (degrees) and pellets.</summary>
        static bool Gun(Item w, out Item ammo, out float every, out float dmg, out float spread, out int pellets)
        {
            pellets = 1;
            switch (w)
            {
                case Item.Bow: ammo = Item.Arrow; every = 1.4f; dmg = 30f; spread = 2.5f; return true;
                case Item.Crossbow: ammo = Item.Arrow; every = 1.7f; dmg = Cfg.CrossbowDamage; spread = 1.5f; return true;
                case Item.Pistol: ammo = Item.PistolAmmo; every = 0.6f; dmg = Cfg.PistolBodyDamage; spread = 3f; return true;
                case Item.Revolver: ammo = Item.RevolverAmmo; every = 0.9f; dmg = Cfg.RevolverBodyDamage; spread = 2f; return true;
                case Item.Shotgun: ammo = Item.ShotgunShell; every = 1.3f; dmg = Cfg.ShotgunPelletDamage; spread = 7f; pellets = Mathf.Max(1, Cfg.ShotgunPellets); return true;
                case Item.Spear: ammo = Item.Spear; every = 2.2f; dmg = 45f; spread = 2f; return true;
                case Item.Sniper: ammo = Item.None; every = 2.6f; dmg = 70f; spread = 0.5f; return true;
                default: ammo = Item.None; every = 1f; dmg = 0f; spread = 0f; return false;
            }
        }

        public static bool TurretWeapon(Item w) => Gun(w, out _, out _, out _, out _, out _);

        static void ServerTurret(Container c, TurretState st)
        {
            var g = NetGame.Instance;
            var eye = c.transform.position + Vector3.up * 0.95f;
            var fwd = c.transform.forward;
            if (Time.time >= st.NextScan)
            {
                st.NextScan = Time.time + 0.25f;
                PlayerNet best = null;
                float bestD = float.MaxValue;
                if (g != null && g.S != GameState.Waiting && g.S != GameState.GameOver)
                    foreach (var p in PlayerNet.All)
                    {
                        if (p == null || !p.IsSpawned || p.Dead.Value || p.Team.Value == c.Team.Value || p.Hidden) continue;
                        var to = p.transform.position + Vector3.up * 1.1f - eye;
                        float d = to.magnitude;
                        if (d > Cfg.TurretRange || d < 0.5f) continue;
                        var flat = new Vector3(to.x, 0, to.z);
                        if (Vector3.Angle(new Vector3(fwd.x, 0, fwd.z), flat) > Cfg.TurretCone) continue;
                        if (!Clear(eye, p)) continue;
                        if (d < bestD) { bestD = d; best = p; }
                    }
                if (best != st.Target)
                {
                    st.Target = best;
                    if (best != null) { st.WarnedAt = Time.time; c.DeployFxRpc(FxWarn, eye); } // (a loud warning before it opens up)
                }
            }
            // aim: at the target (in the turret's own space), or back to straight ahead
            var aimTo = st.Target != null ? c.transform.InverseTransformDirection(st.Target.transform.position + Vector3.up * 1.1f - eye) : Vector3.forward;
            var want = new Vector2(Mathf.Atan2(aimTo.x, aimTo.z) * Mathf.Rad2Deg, -Mathf.Atan2(aimTo.y, new Vector2(aimTo.x, aimTo.z).magnitude) * Mathf.Rad2Deg);
            st.Aim = new Vector2(Mathf.MoveTowardsAngle(st.Aim.x, want.x, 140f * Time.deltaTime), Mathf.MoveTowardsAngle(st.Aim.y, want.y, 90f * Time.deltaTime));
            if ((st.Aim - c.Aim.Value).sqrMagnitude > 1f) c.Aim.Value = st.Aim;
            if (st.Target == null || Time.time - st.WarnedAt < 0.9f || Time.time < st.NextShot) return;
            if (Mathf.Abs(Mathf.DeltaAngle(st.Aim.x, want.x)) > 6f || Mathf.Abs(Mathf.DeltaAngle(st.Aim.y, want.y)) > 8f) return;
            // fire, if it has a weapon and ammo for it
            if (c.Slots.Count < 2) return;
            var w = c.Slots[0];
            if (!Gun(w.Id, out var ammo, out var every, out var dmg, out var spread, out var pellets)) return;
            if (ammo == Item.None)
            {
                if (w.Data == 0) return;
                c.Slots[0] = ItemStack.Of(w.Id, w.Count, w.Data - 1);
            }
            else
            {
                var a = c.Slots[1];
                if (a.Id != ammo || a.Count <= 0) return;
                c.Slots[1] = a.WithCount(a.Count - 1);
            }
            st.NextShot = Time.time + every;
            var dir = c.transform.TransformDirection(Quaternion.Euler(st.Aim.y, st.Aim.x, 0) * Vector3.forward);
            var muzzle = eye + dir * 0.55f;
            for (int i = 0; i < pellets; i++)
            {
                var d = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0) * dir;
                var end = muzzle + d * Cfg.TurretRange;
                if (Physics.Raycast(muzzle, d, out var hit, Cfg.TurretRange, ~0, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    var p = hit.collider.GetComponentInParent<PlayerNet>();
                    if (p != null && p.Team.Value != c.Team.Value && !p.Dead.Value) p.ServerDamage(dmg, null, (byte)Item.AutoTurret);
                    else
                    {
                        var s = hit.collider.GetComponentInParent<Structure>();
                        if (s != null && s.Team.Value != c.Team.Value) s.ServerDamageAt(dmg * 0.3f, hit.point);
                    }
                }
                if (i == 0) c.DeployFxRpc(FxShot, end);
            }
        }

        /// <summary>A clear line from the turret's eye to someone (no walls, no glass).</summary>
        static bool Clear(Vector3 eye, PlayerNet p)
        {
            var to = p.transform.position + Vector3.up * 1.1f;
            if (PlayerNet.GlassBetween(eye, to)) return false;
            foreach (var h in Physics.RaycastAll(eye, (to - eye).normalized, Vector3.Distance(eye, to), ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PlayerNet>() != null) continue;
                if (h.collider.GetComponentInParent<Container>() != null && Vector3.Distance(h.point, eye) < 1.2f) continue; // (itself)
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ every screen

        public static void ClientFx(Container c, byte what, Vector3 at)
        {
            switch (what)
            {
                case FxSnap:
                    Sfx.Play(Sfx.Smash, at, 1f, 0.05f, 40f);
                    Sfx.Play(Sfx.Clink, at, 1f, 0.1f, 40f);
                    Fx.Play(FxKind.Blood, at + Vector3.up * 0.3f, Vector3.up);
                    break;
                case FxWarn:
                    // loud enough for enemies to hear it coming
                    Sfx.Play(Sfx.Beep, at, 1f, 0f, 70f);
                    Sfx.Play(Sfx.Beep, at, 1f, 0f, 70f);
                    break;
                case FxShot:
                {
                    var eye = c.transform.position + Vector3.up * 0.95f;
                    var w = c.Slots.Count > 0 ? c.Slots[0].Id : Item.None;
                    var gun = w == Item.Revolver ? Fx.Gun.Revolver : w == Item.Shotgun ? Fx.Gun.Shotgun : w == Item.Sniper ? Fx.Gun.Sniper : Fx.Gun.Pistol;
                    if (w == Item.Bow || w == Item.Crossbow || w == Item.Spear) { Sfx.Play(Sfx.Twang, eye, 1f, 0.06f, 60f); Fx.Tracer(eye, at, gun, false, false); }
                    else Fx.Tracer(eye, at, gun, false, true);
                    Fx.MuzzleFlash(eye + (at - eye).normalized * 0.5f, (at - eye).normalized, new Color(1f, 0.75f, 0.3f), 1.2f, 6, false);
                    break;
                }
            }
        }

        /// <summary>Your team's sleeping bags (for the respawn screen), nearest the middle first.</summary>
        public static void BagsOf(int team, List<Container> into)
        {
            into.Clear();
            foreach (var c in Container.All) if (c != null && c.IsSpawned && c.Kind.Value == Container.SleepBag && c.Team.Value == team) into.Add(c);
            into.Sort((a, b) => a.transform.position.sqrMagnitude.CompareTo(b.transform.position.sqrMagnitude));
        }

        /// <summary>Server: forget a turret / trap that's gone.</summary>
        public static void Forget(Container c) { s_Turrets.Remove(c); s_TrapSprung.Remove(c); }
    }
}
