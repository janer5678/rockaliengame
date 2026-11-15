using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot's ranged weapons (BotBrain.Combat.cs: TickFight) - the bow, the crossbow, the pistol / revolver / alien gun,
    /// the shotgun and the sniper - used the way a player does, through the players' own server calls:
    ///   the bow and crossbow fire a real arrow (ArrowProjectile, spawned on the server and reporting its own hit or miss
    ///   through ArrowHitRpc / ArrowLandRpc, as a player's client does) - aimed with a lead on a moving target and an
    ///   arc for the drop, off by more the poorer the bot and the further away they are;
    ///   the guns are hitscan (FirePistolRpc / SniperFireRpc / FireShotgunRpc + ShotgunHitRpc): a hit or a miss rolled
    ///   from its skill, the range and how fast the target's moving, only with a clear line to them; reloads take a
    ///   player's time (ReloadPistolRpc, ReloadShotgunRpc, ReloadCrossbowRpc).
    /// Which weapon: whatever does the most damage a second at that range, counting how likely it is to land (a
    /// shotgun up close, the revolver or the bow further out, the sword or spear in a scrap). Ranged, it keeps its
    /// distance from someone with only a melee weapon. The host sees and hears its shots (they're not sent back to the
    /// owner - the server - so they're played here). Also C4 on a wall in a raid (BotBrain.Raid.cs).
    /// </summary>
    public partial class BotBrain
    {
        float m_NextShot, m_ReadyAt, m_DrawFrom = -1f;
        Item m_Aimed;
        Vector3 m_TargetLast, m_TargetVel;
        PlayerNet m_VelOf;
        /// <summary>(tests) shots it has fired, and how many it rolled as hits (arrows: fired at a target in its sights).</summary>
        public int ShotsFired { get; private set; }
        public int ShotsHit { get; private set; }

        static bool IsRanged(Item i) => i == Item.Bow || i == Item.Crossbow || i == Item.Shotgun || i == Item.Sniper || Cfg.IsGun(i);

        /// <summary>Has what it needs to fire this (loaded, or ammo to load it).</summary>
        bool CanFire(Item i, int slot)
        {
            var st = m_P.Inv[slot];
            switch (i)
            {
                case Item.Bow: return CountItem(Item.Arrow) > 0;
                case Item.Crossbow: return st.Data > 0 || CountItem(Item.Arrow) > 0;
                case Item.Shotgun: return st.Data > 0 || CountItem(Item.ShotgunShell) > 0;
                case Item.Sniper: return st.Data > 0;
                default: return Cfg.IsGun(i) && (st.Data > 0 || (Cfg.GunReloads(i) && CountItem(Cfg.GunAmmo(i)) > 0));
            }
        }

        /// <summary>How likely a shot at d m is to land (its skill, the range, how fast they're moving across).</summary>
        float HitChance(Item i, float d)
        {
            float range = i == Item.Shotgun ? Cfg.ShotgunRange : i == Item.Sniper ? 160f : i == Item.Bow || i == Item.Crossbow ? 70f : 55f;
            float p = (0.42f + 0.5f * m_Skill) * Mathf.Clamp01(1.15f - d / range);
            float across = m_TargetVel.magnitude;
            p *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(across / 6f) * Mathf.Clamp01(d / 25f));
            return Mathf.Clamp01(p);
        }

        /// <summary>Damage a second with this at d m (0: no good there).</summary>
        float RangedValue(Item i, int slot, float d)
        {
            if (!CanFire(i, slot)) return 0f;
            float dmg, cycle;
            switch (i)
            {
                case Item.Bow: dmg = Cfg.BowDamage(1f); cycle = Cfg.BowDrawTime + 0.35f; break;
                case Item.Crossbow: dmg = Cfg.CrossbowDamage; cycle = Cfg.CrossbowReload + 0.3f; break;
                case Item.Sniper: dmg = 120f; cycle = 1.4f; break;
                case Item.Shotgun:
                    if (d > Cfg.ShotgunRange) return 0f;
                    dmg = Cfg.ShotgunPellets * Cfg.ShotgunPelletDamage * Cfg.ShotgunFalloff(d) * Mathf.Clamp01(1.2f - d / 14f);
                    cycle = Cfg.ShotgunReload + 0.4f;
                    break;
                default:
                    dmg = Cfg.GunBody(i);
                    cycle = Cfg.GunFireRate(i) + 0.12f + Cfg.GunReload(i) / Mathf.Max(1, Cfg.GunMag(i));
                    break;
            }
            return dmg / cycle * HitChance(i, d);
        }

        /// <summary>The best ranged weapon for d m, and how good it is (damage a second, counting the misses).</summary>
        Item BestRanged(float d, out float value)
        {
            value = 0f;
            var best = Item.None;
            var inv = m_P.Inv;
            for (int s = 0; s < inv.Count; s++)
            {
                var it = inv[s].Id;
                if (!IsRanged(it) || it == best) continue;
                float v = RangedValue(it, s, d);
                if (v > value) { value = v; best = it; }
            }
            return best;
        }

        /// <summary>
        /// In a fight at d m: shoot if a ranged weapon beats its best melee weapon there (or the target's out of reach).
        /// True: it's shooting (it has done the moving and aiming; the melee swing is skipped).
        /// </summary>
        bool RangedFight(PlayerNet t, float d)
        {
            TrackTarget(t);
            if (Eating || m_P.CarryingBall) return false;
            var gun = BestRanged(d, out float gv);
            if (gun == Item.None) return false;
            // its best melee, if it could get to them (from a few metres out it would have to close first: worth less)
            float melee = Rate(Item.Rock, Use.Fight, d);
            var inv = m_P.Inv;
            for (int i = 0; i < inv.Count; i++) if (Cfg.IsMelee(inv[i].Id)) melee = Mathf.Max(melee, Rate(inv[i].Id, Use.Fight, d));
            if (d > 3.5f) melee *= Mathf.Clamp01(3.5f / d);
            if (gv <= melee && d < 4f) return false;
            if (!CanSee(t)) return false; // (no clear line: close in instead)

            int slot = HotbarFor(gun);
            if (slot < 0) return false;
            if (m_P.HeldItem != gun) { Hold(slot); m_ReadyAt = Time.time + 0.35f; m_DrawFrom = -1f; m_Aimed = gun; }
            // keep a good distance: off someone with only a sword, in close with the shotgun, otherwise where it is
            var tp = t.transform.position;
            var away = transform.position - tp; away.y = 0f;
            float keep = gun == Item.Shotgun ? 5f : TargetRanged(t) ? 14f : 11f;
            if (gun != Item.Shotgun && d < keep - 3f && !TargetRanged(t)) GoTo(transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward) * 4f, 0.5f, true);
            else if (d > keep + 6f || (gun == Item.Shotgun && d > 7f)) GoTo(tp, keep, d > keep + 10f);
            else GoTo(transform.position, 0.5f, false);
            m_Strafe = m_Skill > 0.4f && d > 4f;
            m_HasLook = true;
            var aim = AimPoint(t, gun, d);
            m_LookAt = aim;
            if (Time.time < m_ReadyAt || Time.time < m_NextShot) return true;
            Fire(t, gun, slot, d, aim);
            return true;
        }

        /// <summary>The target can shoot back from range.</summary>
        static bool TargetRanged(PlayerNet t)
        {
            var inv = t.Inv;
            for (int i = 0; i < inv.Count; i++) if (IsRanged(inv[i].Id)) return true;
            return false;
        }

        /// <summary>How fast the target's going (for leading a shot and for the odds of a hit).</summary>
        void TrackTarget(PlayerNet t)
        {
            var p = t.transform.position;
            if (m_VelOf != t) { m_VelOf = t; m_TargetLast = p; m_TargetVel = Vector3.zero; return; }
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            var v = (p - m_TargetLast) / dt;
            v.y = 0f;
            m_TargetVel = Vector3.Lerp(m_TargetVel, Vector3.ClampMagnitude(v, 15f), Mathf.Clamp01(dt * 6f));
            m_TargetLast = p;
        }

        /// <summary>Where it aims: their chest (a good bot at their head now and then), led for an arrow's flight and raised for its drop.</summary>
        Vector3 AimPoint(PlayerNet t, Item gun, float d)
        {
            var body = t.transform.position + Vector3.up * (t.Crouch.Value ? 0.8f : 1.15f);
            if (gun != Item.Bow && gun != Item.Crossbow) return body;
            float speed = gun == Item.Crossbow ? Cfg.CrossbowSpeed : Cfg.ArrowSpeed;
            float grav = gun == Item.Crossbow ? Cfg.CrossbowGravity : Cfg.ArrowGravity;
            float flight = Vector3.Distance(m_P.EyePos, body) / Mathf.Max(1f, speed);
            return body + m_TargetVel * flight * Mathf.Lerp(0.4f, 1f, m_Skill) + Vector3.up * 0.5f * grav * flight * flight;
        }

        void Fire(PlayerNet t, Item gun, int slot, float d, Vector3 aim)
        {
            var st = m_P.Inv[slot];
            var eye = m_P.EyePos;
            switch (gun)
            {
                case Item.Bow:
                    // draw (full power), then let go
                    if (m_DrawFrom < 0f) { m_DrawFrom = Time.time; return; }
                    if (Time.time - m_DrawFrom < Cfg.BowDrawTime) return;
                    m_DrawFrom = -1f;
                    Loose(t, aim, Cfg.ArrowSpeed, -1f, d, gun);
                    m_P.FireArrowRpc(m_ShotFrom, m_ShotVel);
                    m_NextShot = Time.time + 0.35f;
                    return;
                case Item.Crossbow:
                    if (st.Data == 0)
                    {
                        m_P.ReloadCrossbowRpc();
                        m_NextShot = Time.time + 0.3f;
                        if (m_P.Inv[slot].Data > 0) m_ReadyAt = Time.time + Cfg.CrossbowReload * 0.85f; // (loading a bolt takes a moment)
                        return;
                    }
                    Loose(t, aim, Cfg.CrossbowSpeed, Cfg.CrossbowDamage, d, gun);
                    m_P.FireCrossbowRpc(m_ShotFrom, m_ShotVel);
                    m_NextShot = Time.time + 0.4f;
                    return;
                case Item.Shotgun:
                {
                    if (st.Data == 0)
                    {
                        m_P.ReloadShotgunRpc();
                        m_ReadyAt = Time.time + Cfg.ShotgunReload;
                        return;
                    }
                    var dir = (aim - eye).normalized;
                    m_P.FireShotgunRpc(dir);
                    m_NextShot = Time.time + 0.45f;
                    ShotsFired++;
                    int n = Mathf.Clamp(Cfg.ShotgunPellets, 1, 30), hits = 0, heads = 0;
                    float spread = Mathf.Max(0.3f, d * Mathf.Tan(Cfg.ShotgunSpread * Mathf.Deg2Rad));
                    float each = Mathf.Clamp01(0.85f / spread) * (0.6f + 0.4f * m_Skill); // (a pellet lands: the cone against a body's width)
                    for (int i = 0; i < n; i++) if (Random.value < each) { hits++; if (Random.value < 0.08f + 0.12f * m_Skill) heads++; }
                    bool clear = CanSee(t);
                    var end = clear && hits > 0 ? aim : eye + dir * Mathf.Min(Cfg.ShotgunRange, 30f);
                    if (clear && hits > 0) { ShotsHit++; m_P.ShotgunHitRpc(t.NetworkObject, aim, (byte)(hits - heads), (byte)heads); }
                    ShotLooks(FxKind.ShotgunTracer, Fx.Gun.Shotgun, eye, end, dir);
                    return;
                }
                default:
                {
                    // the pistol / revolver / alien gun and the sniper: hitscan
                    bool sniper = gun == Item.Sniper;
                    if (st.Data <= 0)
                    {
                        if (sniper || !Cfg.GunReloads(gun)) return;
                        m_P.ReloadPistolRpc();
                        m_ReadyAt = Time.time + Cfg.GunReload(gun);
                        return;
                    }
                    bool hit = Random.value < HitChance(gun, d) && CanSee(t);
                    bool head = hit && Random.value < 0.07f + 0.2f * m_Skill;
                    var point = head ? t.EyePos + Vector3.up * 0.05f : aim;
                    var dir = (point - eye).normalized;
                    if (!hit)
                    {
                        // a miss: just past them
                        var side = Vector3.Cross(Vector3.up, dir).normalized * (Random.value < 0.5f ? -1f : 1f);
                        dir = (point + side * Random.Range(0.7f, 1.6f) + Vector3.up * Random.Range(-0.4f, 0.8f) - eye).normalized;
                        point = Physics.Raycast(eye, dir, out var h, 120f, Mask, QueryTriggerInteraction.Ignore) ? h.point : eye + dir * 120f;
                    }
                    if (sniper) m_P.SniperFireRpc(hit, hit ? new NetworkObjectReference(t.NetworkObject) : default, point, dir);
                    else m_P.FirePistolRpc(hit, hit ? new NetworkObjectReference(t.NetworkObject) : default, point, dir);
                    ShotsFired++;
                    if (hit) ShotsHit++;
                    m_NextShot = Time.time + (sniper ? 1.5f : Cfg.GunFireRate(gun) + Random.Range(0.08f, 0.3f) * (1.3f - m_Skill));
                    bool big = gun == Item.Revolver || gun == Item.AlienGun;
                    ShotLooks(sniper ? FxKind.SniperTracer : big ? FxKind.RevolverTracer : FxKind.PistolTracer, sniper ? Fx.Gun.Sniper : big ? Fx.Gun.Revolver : Fx.Gun.Pistol, eye, point, dir);
                    return;
                }
            }
        }

        Vector3 m_ShotFrom, m_ShotVel;

        /// <summary>An arrow or bolt off at `aim`, off by its aim error; spawned here so it flies, hits and reports like a
        /// player's (the server's own copy is the one that counts).</summary>
        void Loose(PlayerNet t, Vector3 aim, float speed, float damage, float d, Item gun)
        {
            var eye = m_P.EyePos;
            var dir = (aim - eye).normalized;
            // the aim error: a cone that's narrower the better the bot (and a little wider far off)
            float err = Mathf.Lerp(5f, 1.8f, m_Skill) * (0.8f + Mathf.Clamp01(d / 50f) * 0.5f);
            dir = Quaternion.AngleAxis(Random.Range(-err, err), Vector3.up) * Quaternion.AngleAxis(Random.Range(-err, err) * 0.6f, Vector3.Cross(Vector3.up, dir)) * dir;
            m_ShotFrom = eye + dir * 0.6f;
            m_ShotVel = dir * speed;
            ArrowProjectile.Spawn(m_ShotFrom, m_ShotVel, m_P, true, damage);
            Sfx.Play(Sfx.Twang, m_ShotFrom, 0.9f, 0.05f, 110f);
            ShotsFired++;
            m_ArrowOn = t;
            m_ArrowHp = t.Health.Value + t.ArmorHp.Value;
            m_ArrowUntil = Time.time + d / Mathf.Max(1f, speed) + 0.4f;
        }

        PlayerNet m_ArrowOn;
        float m_ArrowHp, m_ArrowUntil;

        /// <summary>(tests) an arrow it loosed at someone: did they lose health before it could have landed (ShotsHit)?</summary>
        void CheckArrow()
        {
            if (m_ArrowOn == null) return;
            float hp = m_ArrowOn.Health.Value + m_ArrowOn.ArmorHp.Value;
            if (hp < m_ArrowHp - 0.5f || m_ArrowOn.Dead.Value) { ShotsHit++; m_ArrowOn = null; }
            else if (Time.time > m_ArrowUntil) m_ArrowOn = null;
        }

        /// <summary>The shot's tracer and bang for this screen (the server's: everyone else is sent theirs by the shot itself).</summary>
        static void ShotLooks(FxKind tracer, Fx.Gun gun, Vector3 eye, Vector3 end, Vector3 dir)
        {
            if (Application.isBatchMode) return;
            Fx.Play(tracer, eye + dir * 0.5f - Vector3.up * 0.15f, end);
            Fx.Gunshot(gun, eye + dir * 0.5f, dir, false);
        }

        // ---------------------------------------------------------------- C4

        /// <summary>A raid stuck on a wall: C4 on it (thrown and stuck where it lands: ThrowC4Rpc / C4LandRpc). True: planted.</summary>
        bool PlantC4(Structure s, Vector3 at)
        {
            if (s == null || !s.IsSpawned || !Has(Item.C4) || Time.time < m_NextUse) return false;
            if (Vector3.Distance(m_P.EyePos, at) > 3.2f) return false;
            int slot = HotbarFor(Item.C4);
            if (slot < 0) return false;
            Hold(slot);
            int before = CountItem(Item.C4);
            var dir = (at - m_P.EyePos).normalized;
            m_P.ThrowC4Rpc(m_P.EyePos + dir * 0.5f, dir * 6f);
            if (CountItem(Item.C4) >= before) return false;
            m_P.C4LandRpc(at - dir * 0.05f, -dir);
            m_NextUse = Time.time + 0.8f;
            C4Planted++;
            Note("C4 on a wall");
            return true;
        }

        /// <summary>(tests) C4 it has stuck on enemy walls.</summary>
        public int C4Planted { get; private set; }
    }
}
