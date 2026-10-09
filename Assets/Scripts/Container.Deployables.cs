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
            if (!g.CanRespawn(Team.Value)) return; // (Bedwars: your machine's gone)
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
        [Tune("Crafting")] public static float BearTrapDamage = 30f, BearTrapHold = 3.5f, BearTrapRearm = 3f; // (rearm: seconds after it lets you go)
        /// <summary>The auto turret: how far it sees, and its cone either side of where it faces (degrees).</summary>
        [Tune("Crafting")] public static float TurretRange = 24f, TurretCone = 32f; // (half as wide as it was; a bit shorter)
        /// <summary>How fast a turret's aim catches up with where its target is (1/s: lower = it lags further behind
        /// someone on the move), and how much wider it sprays at a moving target (degrees per m/s).</summary>
        [Tune("Crafting")] public static float TurretTrack = 2.6f, TurretMovingSpread = 0.7f;
    }

    /// <summary>
    /// The Trade Station's placeables and the Advanced Trade Station's auto turret - kinds of Container (so they're
    /// networked, take damage and break, and come back to the hand with a held E on an empty one):
    /// - SLEEPING BAG: anywhere but an enemy base; when you're dead, your team's bags are options on the respawn screen
    ///   (each can be used once a minute: ReadyAt).
    /// - BEAR TRAP: in the team colour, anywhere - your base, the wild or theirs - on the ground or a floor (not on a
    ///   foundation). Anyone stepping on it - its own team too - is snapped (BearTrapDamage) and held (BearTrapHold);
    ///   it sets itself again BearTrapRearm seconds after it lets go (PlayerNet.Trapped).
    /// - LADDER: anywhere (to scale someone's walls, or your own); a climbable volume like the fort tower's (Ladder).
    /// - AUTO TURRET: in your own base. E on it opens its two slots - a ranged weapon (or a spear) and its ammo. It
    ///   watches a cone in front of it (shown on the placing ghost), hums as it pans, and with an enemy in view gives a
    ///   loud warning beep, then fires at the weapon's own pace while the ammo lasts.
    /// </summary>
    public static class Deployables
    {
        public const byte FxSnap = 1, FxWarn = 2, FxShot = 3, FxHum = 4, FxArrow = 5, FxBolt = 6, FxSpear = 7;
        public const float LadderHeight = 2.4f; // (half what it was: you put it as high up the wall as you aim)
        /// <summary>Put against a wall while not square on to it, a ladder turns this far your way at most (degrees).</summary>
        public const float LadderMaxTurn = 30f;

        /// <summary>The building piece a ladder standing here leans on (it faces into it), or null: a ladder needs a wall
        /// to go against, and breaks (nothing dropped) once that wall is gone.</summary>
        public static Structure LadderSupport(Vector3 pos, Quaternion rot)
        {
            var fwd = rot * Vector3.forward;
            for (float y = 0.35f; y <= LadderHeight - 0.1f; y += 0.45f)
            {
                var o = pos + Vector3.up * y - fwd * 0.2f;
                foreach (var h in Physics.RaycastAll(o, fwd, 1f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                {
                    var s = h.collider.GetComponentInParent<Structure>();
                    if (s != null && s.IsSpawned && s.Health.Value > 0f) return s;
                }
            }
            return null;
        }

        /// <summary>The top of a building piece (world y).</summary>
        public static float PieceTop(Structure s)
        {
            float top = float.MinValue;
            foreach (var col in s.GetComponentsInChildren<Collider>()) if (!col.isTrigger) top = Mathf.Max(top, col.bounds.max.y);
            return top;
        }

        /// <summary>A large wall / gate: the highest a ladder's foot may stand against it (its top stays below the spikes).</summary>
        public static float LadderMaxFoot(Structure s) => PieceTop(s) - Cfg.LadderSpikeGap - LadderHeight;

        static readonly Dictionary<Container, float> s_LadderCheck = new Dictionary<Container, float>();

        /// <summary>Server: a ladder whose wall has gone breaks with it.</summary>
        static void ServerLadder(Container c)
        {
            s_LadderCheck.TryGetValue(c, out var at);
            if (Time.time < at) return;
            s_LadderCheck[c] = Time.time + 0.4f;
            if (LadderSupport(c.transform.position, c.transform.rotation) != null) return;
            s_LadderCheck.Remove(c);
            c.ServerBreakWithSupport();
        }

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
                    // two rails and rungs, straight up, its back to +z (the wall it's put against)
                    var wood = new Color(0.5f, 0.34f, 0.18f);
                    var lean = new GameObject("lean").transform;
                    lean.SetParent(t, false);
                    lean.localRotation = Quaternion.identity; // (straight up, not leaning)
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

        /// <summary>The turret's ghost shows what it'll watch: a red outline of its cone out to its range.</summary>
        static void BuildCone(Transform t, Material ghost)
        {
            // just its outline, in red, on the ground: the two edges of what it watches and the arc at its reach
            var red = Art.Ghost(new Color(1f, 0.15f, 0.12f, 0.75f));
            var root = new GameObject("cone").transform;
            root.SetParent(t, false);
            void Line(Vector3 a, Vector3 b)
            {
                var mid = (a + b) * 0.5f;
                var d = b - a;
                var seg = Art.Box(root, Color.white, mid, new Vector3(0.12f, 0.04f, d.magnitude), new Vector3(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0), false, red);
                seg.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Vector3 At(float deg, float r) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad) * r, 0.08f, Mathf.Cos(deg * Mathf.Deg2Rad) * r);
            Line(At(-Cfg.TurretCone, 0.6f), At(-Cfg.TurretCone, Cfg.TurretRange));
            Line(At(Cfg.TurretCone, 0.6f), At(Cfg.TurretCone, Cfg.TurretRange));
            const int seg2 = 12;
            for (int i = 0; i < seg2; i++)
                Line(At(Mathf.Lerp(-Cfg.TurretCone, Cfg.TurretCone, i / (float)seg2), Cfg.TurretRange), At(Mathf.Lerp(-Cfg.TurretCone, Cfg.TurretCone, (i + 1) / (float)seg2), Cfg.TurretRange));
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
                    bc.center = new Vector3(0, LadderHeight * 0.5f, 0f);
                    bc.size = new Vector3(0.75f, LadderHeight, 0.12f);
                    var lg = new GameObject("ladder");
                    lg.transform.SetParent(visual, false);
                    lg.transform.localPosition = new Vector3(0, (LadderHeight + 1f) * 0.5f, -0.26f); // (a thin slab right in front of the rungs: on it only when you touch it)
                    var vol = lg.AddComponent<BoxCollider>();
                    vol.isTrigger = true;
                    vol.size = new Vector3(0.85f, LadderHeight + 1f, 0.4f);
                    var lad = lg.AddComponent<Ladder>();
                    lad.Deployed = true;
                    lad.BoardZ = 0.26f; // (the board is 0.26 m behind the climbing volume's centre)
                    lad.TopLocalY = LadderHeight - 0.3f;
                    lad.ExitHop = 4.6f; // (a big hop off the top: over a large wall it doesn't quite reach)
                    break;
                }
                default: bc.center = new Vector3(0, 0.6f, 0); bc.size = new Vector3(0.6f, 1.2f, 0.6f); break;
            }
        }

        // ------------------------------------------------------------------ alive

        class TurretState { public PlayerNet Target; public float WarnedAt = -10f, NextShot, NextScan, NextHum; public Vector2 Aim; public Item Shown = Item.None; public Transform Model; public Vector3 Track, LastPos; public float Speed; }
        static readonly Dictionary<Container, TurretState> s_Turrets = new Dictionary<Container, TurretState>();
        static readonly Dictionary<Container, float> s_TrapSprung = new Dictionary<Container, float>();

        /// <summary>Every peer, every frame: the trap's jaws, the turret's head turning to its aim and its weapon in it;
        /// the server also springs traps and runs the turrets.</summary>
        public static void Tick(Container c, Transform visual)
        {
            switch (c.Kind.Value)
            {
                case Container.Ladder:
                    if (c.IsServer) ServerLadder(c);
                    break;
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
                        TickNeedsWeapon(c, visual, w == Item.None);
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
                    if (c.IsServer) { ServerTurret(c, st); ServerTickFlying(); }
                    break;
                }
            }
        }

        /// <summary>An empty turret (no weapon) has a big red sign bobbing over it - "NEEDS A WEAPON" - for its own team,
        /// so it's obvious it does nothing until you give it one.</summary>
        static void TickNeedsWeapon(Container c, Transform visual, bool empty)
        {
            var sign = visual.Find("needsWeapon");
            bool show = empty && PlayerNet.Local != null && PlayerNet.Local.Team.Value == c.Team.Value;
            if (sign == null)
            {
                if (!show) return;
                sign = new GameObject("needsWeapon").transform;
                sign.SetParent(visual, false);
                var red = new Color(1f, 0.2f, 0.15f);
                var glow = new Material(Art.Mat(red));
                if (glow.HasProperty("_EmissionColor")) { glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", red * 2f); }
                // a "!" in a red ring: the bar and the dot, then the ring of short bars round it
                Art.Box(sign, red, new Vector3(0, 0.12f, 0), new Vector3(0.12f, 0.36f, 0.05f), default, false, glow);
                Art.Box(sign, red, new Vector3(0, -0.15f, 0), new Vector3(0.12f, 0.1f, 0.05f), default, false, glow);
                for (int k = 0; k < 12; k++)
                {
                    var r = Quaternion.Euler(0, 0, k * 30f);
                    Art.Box(sign, red, r * new Vector3(0, 0.36f, 0), new Vector3(0.16f, 0.05f, 0.05f), new Vector3(0, 0, k * 30f), false, glow);
                }
                var label = new GameObject("label").AddComponent<TextMesh>();
                label.transform.SetParent(sign, false);
                label.transform.localPosition = new Vector3(0, -0.55f, 0);
                label.text = "NEEDS A WEAPON\n(E: any weapon + arrows)";
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.characterSize = 0.045f;
                label.fontSize = 64;
                label.color = new Color(1f, 0.85f, 0.8f);
                foreach (var col in sign.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            }
            sign.gameObject.SetActive(show);
            if (!show) return;
            // bob over the turret, always facing the camera
            sign.position = c.transform.position + Vector3.up * (2.1f + Mathf.Sin(Time.time * 3f) * 0.12f);
            var cam = Camera.main;
            if (cam != null) sign.rotation = Quaternion.LookRotation(sign.position - cam.transform.position);
        }

        static void ServerTrap(Container c)
        {
            var now = NetworkManager.Singleton.ServerTime.Time;
            if (c.Flag.Value == 1)
            {
                if (s_TrapSprung.TryGetValue(c, out var at) && Time.time - at > Cfg.BearTrapHold + Cfg.BearTrapRearm) { c.Flag.Value = 0; s_TrapSprung.Remove(c); }
                return;
            }
            var p0 = c.transform.position;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value || p.Riding) continue; // (anyone - your own team too)
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

        /// <summary>
        /// What a turret does with the weapon it's given: seconds between shots, damage, spread (degrees) and pellets.
        /// It takes ANY weapon (melee ones too) and every one of them fires ARROWS from its second slot - the guns as
        /// hitscan at their own pace, everything else launching real arrows (ServerTickFlying) that hit as hard as the weapon.
        /// </summary>
        static bool Gun(Item w, out float every, out float dmg, out float spread, out int pellets)
        {
            pellets = 1;
            switch (w)
            {
                case Item.Bow: every = 1.4f; dmg = 30f; spread = 2.5f; return true;
                case Item.Crossbow: every = 1.7f; dmg = Cfg.CrossbowDamage; spread = 1.5f; return true;
                case Item.Pistol: every = 0.6f; dmg = Cfg.PistolBodyDamage * 0.45f; spread = 3f; return true;
                case Item.Revolver: every = 0.9f; dmg = Cfg.RevolverBodyDamage; spread = 2f; return true;
                case Item.Shotgun: every = 1.3f; dmg = Cfg.ShotgunPelletDamage; spread = 7f; pellets = Mathf.Max(1, Cfg.ShotgunPellets); return true;
                case Item.Spear: every = 2.2f; dmg = 45f; spread = 2f; return true;
                case Item.Sniper: every = 2.6f; dmg = 70f; spread = 0.5f; return true;
                case Item.RocketLauncher: every = 2.4f; dmg = 60f; spread = 1.5f; return true;
                case Item.DeathWand: every = 2.4f; dmg = 55f; spread = 1f; return true;
            }
            if (!TurretWeapon(w)) { every = 1f; dmg = 0f; spread = 0f; return false; }
            // a melee weapon (or a ram, a rock...): arrows at a bow's pace, as hard as the weapon hits (within reason)
            float hit = Cfg.IsMelee(w) ? Cfg.Melee(w).PlayerDamage : 30f;
            every = w == Item.Chainsaw ? 0.8f : 1.5f;
            dmg = Mathf.Clamp(hit * 1.3f, 20f, 40f);
            spread = 3f;
            return true;
        }

        /// <summary>Anything that's a weapon (melee or ranged): it can go in a turret's weapon slot.</summary>
        public static bool TurretWeapon(Item w) => Cfg.IsMelee(w) || Cfg.IsRam(w) || w == Item.Bow || w == Item.Crossbow || w == Item.Pistol || w == Item.Revolver
            || w == Item.Shotgun || w == Item.Sniper || w == Item.RocketLauncher || w == Item.DeathWand;
        /// <summary>The only thing a turret fires: arrows, from its second slot.</summary>
        public const Item TurretAmmo = Item.Arrow;
        /// <summary>The guns: a turret fires them hitscan (everything else launches a real arrow).</summary>
        static bool Hitscan(Item w) => w == Item.Pistol || w == Item.Revolver || w == Item.Shotgun || w == Item.Sniper || w == Item.RocketLauncher || w == Item.DeathWand;

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
            // aim: at where it thinks the target is - a point that follows them but lags behind (someone on the move
            // is hard for it to hit) - in the turret's own space, or back to straight ahead
            if (st.Target != null)
            {
                var tp = st.Target.transform.position + Vector3.up * 1.1f;
                float dtt = Mathf.Max(Time.deltaTime, 0.0001f);
                st.Speed = Mathf.Lerp(st.Speed, new Vector2(tp.x - st.LastPos.x, tp.z - st.LastPos.z).magnitude / dtt, 1f - Mathf.Exp(-6f * dtt));
                if ((st.Track - tp).sqrMagnitude > 400f || st.LastPos == Vector3.zero) st.Track = tp;
                st.Track = Vector3.Lerp(st.Track, tp, 1f - Mathf.Exp(-Cfg.TurretTrack * dtt));
                st.LastPos = tp;
            }
            else { st.LastPos = Vector3.zero; st.Speed = 0f; }
            var aimTo = st.Target != null ? c.transform.InverseTransformDirection(st.Track - eye) : Vector3.forward;
            var want = new Vector2(Mathf.Atan2(aimTo.x, aimTo.z) * Mathf.Rad2Deg, -Mathf.Atan2(aimTo.y, new Vector2(aimTo.x, aimTo.z).magnitude) * Mathf.Rad2Deg);
            st.Aim = new Vector2(Mathf.MoveTowardsAngle(st.Aim.x, want.x, 140f * Time.deltaTime), Mathf.MoveTowardsAngle(st.Aim.y, want.y, 90f * Time.deltaTime));
            if ((st.Aim - c.Aim.Value).sqrMagnitude > 1f) c.Aim.Value = st.Aim;
            if (st.Target == null || Time.time - st.WarnedAt < 0.9f || Time.time < st.NextShot) return;
            if (Mathf.Abs(Mathf.DeltaAngle(st.Aim.x, want.x)) > 6f || Mathf.Abs(Mathf.DeltaAngle(st.Aim.y, want.y)) > 8f) return;
            // fire, if it has a weapon and ammo for it
            if (c.Slots.Count < 2) return;
            var w = c.Slots[0];
            if (!Gun(w.Id, out var every, out var dmg, out var spread, out var pellets)) return;
            var a = c.Slots[1];
            if (a.Id != TurretAmmo || a.Count <= 0) return;
            c.Slots[1] = a.WithCount(a.Count - 1);
            st.NextShot = Time.time + every;
            var dir = c.transform.TransformDirection(Quaternion.Euler(st.Aim.y, st.Aim.x, 0) * Vector3.forward);
            var muzzle = eye + dir * 0.55f;
            spread += Mathf.Min(st.Speed, 10f) * Cfg.TurretMovingSpread; // (a moving target: it sprays)
            // a bow, crossbow or spear launches the real thing: it flies, drops and takes time to get there (ServerTickFlying)
            if (!Hitscan(w.Id))
            {
                float speed = w.Id == Item.Crossbow ? Cfg.CrossbowSpeed : w.Id == Item.Spear ? Cfg.SpearThrowSpeed : Cfg.ArrowSpeed;
                float grav = w.Id == Item.Crossbow ? Cfg.CrossbowGravity : w.Id == Item.Spear ? Cfg.SpearGravity : Cfg.ArrowGravity;
                float dist = Vector3.Distance(st.Track, muzzle), tt = dist / speed;
                var d = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0) * dir;
                d = (d * dist + Vector3.up * 0.5f * grav * tt * tt).normalized; // (aimed a little high: it drops)
                s_Flying.Add(new Flying { Pos = muzzle, Vel = d * speed, Dmg = dmg, Grav = grav, Until = Time.time + 4f, Team = c.Team.Value });
                c.DeployFxRpc(w.Id == Item.Crossbow ? FxBolt : w.Id == Item.Spear ? FxSpear : FxArrow, d * speed);
                return;
            }
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

        /// <summary>A turret's arrow, bolt or spear in the air (server): it flies on, drops, and hurts what it hits.</summary>
        class Flying { public Vector3 Pos, Vel; public float Dmg, Grav, Until; public int Team; }
        static readonly List<Flying> s_Flying = new List<Flying>();
        static int s_FlyFrame = -1;

        /// <summary>Server, once a frame: every turret projectile in the air moves on and lands.</summary>
        static void ServerTickFlying()
        {
            if (s_FlyFrame == Time.frameCount || s_Flying.Count == 0) return;
            s_FlyFrame = Time.frameCount;
            float dt = Time.deltaTime;
            for (int i = s_Flying.Count - 1; i >= 0; i--)
            {
                var f = s_Flying[i];
                var step = f.Vel * dt;
                f.Vel += Vector3.down * f.Grav * dt;
                if (Physics.Raycast(f.Pos, step, out var hit, step.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    var p = hit.collider.GetComponentInParent<PlayerNet>();
                    if (p != null && p.Team.Value != f.Team && !p.Dead.Value) p.ServerDamage(f.Dmg, null, (byte)Item.AutoTurret);
                    else
                    {
                        var s = hit.collider.GetComponentInParent<Structure>();
                        if (s != null && s.Team.Value != f.Team) s.ServerDamageAt(f.Dmg * 0.3f, hit.point);
                    }
                    s_Flying.RemoveAt(i);
                    continue;
                }
                f.Pos += step;
                if (Time.time > f.Until) s_Flying.RemoveAt(i);
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
                case FxArrow: case FxBolt: case FxSpear:
                {
                    // the real projectile on every screen (just the look: the server says what it hits)
                    var eye = c.transform.position + Vector3.up * 0.95f;
                    var vel = at;
                    var muzzle = eye + vel.normalized * 0.55f;
                    Sfx.Play(Sfx.Twang, eye, 1f, 0.06f, 60f);
                    if (what == FxSpear) ArrowProjectile.SpawnSpear(muzzle, vel, null, false);
                    else ArrowProjectile.Spawn(muzzle, vel, null, false, what == FxBolt ? Cfg.CrossbowDamage : -1f);
                    break;
                }
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
