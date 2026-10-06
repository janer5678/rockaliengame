using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Client side of the game-mode guns: the pistol and revolver (fire, reload) and the waterpipe shotgun.</summary>
    public partial class PlayerController
    {
        float m_NextPistolShot, m_PistolReloadStart = -1f, m_PistolReloadTime = 1f;
        /// <summary>How many rounds the reload under way puts in (the revolver: just the ones that were fired).</summary>
        public int PistolReloadRounds { get; private set; }
        Item m_PistolReloadItem;

        /// <summary>How far the pistol / revolver reload is (0..1), -1 when not reloading (for the HUD).</summary>
        public float PistolReloadProgress => m_PistolReloadStart < 0 ? -1f : Mathf.Clamp01((Time.time - m_PistolReloadStart) / Mathf.Max(0.05f, m_PistolReloadTime));

        /// <summary>Pistol / revolver: LMB fires (as fast as you click, up to its fire rate), R reloads; it reloads by itself when empty.</summary>
        void HandlePistol(Item gun)
        {
            if (m_PistolReloadStart >= 0) return;
            var st = m_Net.HeldStack;
            if (Binds.Down(Bind.Rotate) && st.Data < Cfg.GunMag(gun)) { StartPistolReload(gun); return; }
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextPistolShot) return;
            if (st.Data <= 0) { StartPistolReload(gun); return; }
            m_NextPistolShot = Time.time + Cfg.GunFireRate(gun);
            // hitscan: whatever the crosshair is on, right now
            var ray = CenterRay();
            bool hit = AimWithAssist(ray, 250f, Cfg.ProjectileAssist, out var h);
            var no = hit ? h.collider.GetComponentInParent<Unity.Netcode.NetworkObject>() : null;
            var point = hit ? h.point : ray.GetPoint(250f);
            if (no != null && no.TryGetComponent(out PlayerNet p) && p != m_Net && !p.Dead.Value)
            {
                bool head = p.IsHeadshot(point);
                Fx.Blood(point, ray.direction, head);
                Fx.DamageNumber(point, head ? Cfg.GunHead(gun) : Cfg.GunBody(gun), head);
                Hud.HitMarker(!(head && p.HelmetHp.Value > 0), head);
            }
            else if (no != null && no.TryGetComponent(out Vehicle hv)) Hud.AnimalHit(hv, point, Cfg.GunBody(gun) * hv.HeadMul(point)); // (as PlayerNet.FirePistolRpc)
            m_Net.FirePistolRpc(no != null, no != null ? new Unity.Netcode.NetworkObjectReference(no) : default, point, ray.direction);
            Fx.Tracer(ray.origin + ray.direction * 0.5f - Vector3.up * 0.15f, point, Fx.Gun.Sniper, true, false);
            // the shot: its own crack in your ears and a flash at the muzzle; the revolver bucks (the view model snaps up
            // at the wrist) with a little punch out of the view
            Fx.Gunshot(gun == Item.Revolver ? Fx.Gun.Revolver : Fx.Gun.Pistol, m_VM.Muzzle(), ray.direction, true);
            m_VM.Use();
            Fx.Kick(gun == Item.Revolver ? 3.6f : 2f);
            if (gun == Item.Revolver) { Fx.Punch(1.8f); Fx.Shake(0.12f); }
        }

        void StartPistolReload(Item gun)
        {
            if (m_Net.Count(Cfg.GunAmmo(gun)) <= 0) { Hud.Push($"The {Cfg.ItemName(gun).ToLower()} is out of ammo"); return; }
            m_PistolReloadStart = Time.time;
            m_PistolReloadItem = gun;
            // as long as the rounds going in take: the revolver one by one (5 of 6 left: a one-round reload), plus a moment
            // to open and close it; the pistol's magazine all at once
            int mag = Cfg.GunMag(gun);
            PistolReloadRounds = Mathf.Clamp(Mathf.Min(mag - m_Net.HeldStack.Data, m_Net.Count(Cfg.GunAmmo(gun))), 1, mag);
            m_PistolReloadTime = gun == Item.Revolver ? Cfg.GunReload(gun) * (0.25f + 0.75f * PistolReloadRounds / (float)mag) : Cfg.GunReload(gun);
            Sfx.Play2D(Sfx.Clink, 0.4f);
        }

        void TickPistolReload(Item held)
        {
            if (!Cfg.IsGun(held) || m_Net.Dead.Value || (m_PistolReloadStart >= 0 && held != m_PistolReloadItem)) { m_PistolReloadStart = -1f; return; }
            if (m_PistolReloadStart < 0)
            {
                // empty and there's ammo: it reloads by itself
                if (m_Net.HeldStack.Data == 0 && m_Net.Count(Cfg.GunAmmo(held)) > 0 && !MenuOpen && Time.time > m_PistolReloadSent + 1f) StartPistolReload(held);
                return;
            }
            if (Time.time - m_PistolReloadStart >= m_PistolReloadTime)
            {
                m_PistolReloadStart = -1f;
                m_PistolReloadSent = Time.time; // don't start another while the server's answer is on its way
                m_Net.ReloadPistolRpc();
                Sfx.Play2D(Sfx.Clink, 0.6f);
            }
        }
        float m_PistolReloadSent = -10f;

        // ---------------- waterpipe shotgun ----------------

        float m_ShotgunReloadStart = -1f, m_ShotgunSentLoad = -10f, m_ShotgunSentFire = -10f, m_NextShotgun;
        int m_ShotgunSlot = -1;

        public float ShotgunReloadProgress => m_ShotgunReloadStart < 0 ? -1f : Mathf.Clamp01((Time.time - m_ShotgunReloadStart) / Mathf.Max(0.1f, Cfg.ShotgunReload));

        /// <summary>Loaded, counting a load / shot we sent that the server hasn't confirmed yet.</summary>
        bool ShotgunLoaded(ItemStack st)
        {
            bool same = m_ShotgunSlot == m_Net.HeldSlot.Value;
            if (same && Time.time - m_ShotgunSentFire < 1f && st.Data > 0) return false;
            if (same && Time.time - m_ShotgunSentLoad < 1f && st.Data == 0) return true;
            return st.Data > 0;
        }

        /// <summary>
        /// One shell at a time, like Rust's waterpipe: LMB fires a spread of pellets (each one hits on its own, so up close
        /// they all land), then it loads the next shell by itself if you have one (or R).
        /// </summary>
        void HandleShotgun()
        {
            if (m_ShotgunReloadStart >= 0) return;
            var st = m_Net.HeldStack;
            bool loaded = ShotgunLoaded(st);
            if (Binds.Down(Bind.Rotate) && !loaded) { StartShotgunReload(); return; }
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextShotgun) return;
            if (!loaded) { StartShotgunReload(); return; }
            m_NextShotgun = Time.time + 0.4f;
            m_ShotgunSentFire = Time.time;
            m_ShotgunSentLoad = -10f;
            m_ShotgunSlot = m_Net.HeldSlot.Value;
            var ray = CenterRay();
            m_Net.FireShotgunRpc(ray.direction);

            // every pellet: its own ray inside the spread cone
            var hits = new Dictionary<PlayerNet, (int body, int head, Vector3 point)>();
            var animals = new Dictionary<Vehicle, (int n, Vector3 point)>();
            int n = Mathf.Clamp(Cfg.ShotgunPellets, 1, 30);
            var rot = Quaternion.LookRotation(ray.direction);
            float spread = Mathf.Tan(Cfg.ShotgunSpread * Mathf.Deg2Rad);
            for (int i = 0; i < n; i++)
            {
                // spread evenly over the disc (golden angle), with a little jitter
                float r = Mathf.Sqrt((i + 0.5f) / n) * spread * Random.Range(0.85f, 1.1f);
                float a = i * 2.39996f + Random.Range(-0.2f, 0.2f);
                var dir = (rot * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 1f)).normalized;
                var pr = new Ray(ray.origin, dir);
                bool hit = AimWithAssist(pr, Cfg.ShotgunRange, 0.04f, out var h);
                var end = hit ? h.point : pr.GetPoint(Cfg.ShotgunRange);
                if (i % 2 == 0) Fx.Tracer(ray.origin + dir * 0.5f - Vector3.up * 0.15f, end, Fx.Gun.Shotgun, true, false); // (one blast for them all, below)
                if (!hit) continue;
                var p = h.collider.GetComponentInParent<PlayerNet>();
                if (p != null && p != m_Net && !p.Dead.Value)
                {
                    bool head = p.IsHeadshot(h.point);
                    hits.TryGetValue(p, out var c);
                    hits[p] = (c.body + (head ? 0 : 1), c.head + (head ? 1 : 0), h.point);
                }
                else if (h.collider.GetComponentInParent<Vehicle>() is Vehicle v && !v.IsCar)
                {
                    // horses (and Slenderman) take pellets too
                    animals.TryGetValue(v, out var c);
                    animals[v] = (c.n + 1, h.point);
                }
                else Fx.Chips(h.point, h.normal, new Color(0.35f, 0.3f, 0.22f), 2, 1.5f);
            }
            foreach (var kv in animals)
            {
                // (the same sum as the server's: PlayerNet.ShotgunHitRpc)
                Hud.AnimalHit(kv.Key, kv.Value.point, Cfg.ShotgunPelletDamage * kv.Value.n * Cfg.ShotgunFalloff(Vector3.Distance(ray.origin, kv.Key.transform.position)), false);
                m_Net.ShotgunHitRpc(new Unity.Netcode.NetworkObjectReference(kv.Key.NetworkObject), kv.Value.point, (byte)kv.Value.n, 0);
            }
            foreach (var kv in hits)
            {
                var p = kv.Key;
                var (body, head, point) = kv.Value;
                float per = Cfg.ShotgunPelletDamage * Cfg.ShotgunFalloff(Vector3.Distance(ray.origin, point));
                bool helmet = head > 0 && p.HelmetHp.Value > 0;
                float dmg = per * body + (helmet ? 0f : per * Cfg.ShotgunHeadMul * head);
                Fx.Blood(point, ray.direction, head > 0);
                Fx.DamageNumber(point, dmg, head > 0);
                Hud.HitMarker(false, head > 0);
                m_Net.ShotgunHitRpc(new Unity.Netcode.NetworkObjectReference(p.NetworkObject), point, (byte)body, (byte)head);
            }
            // the blast: one great flat boom, a big flash and sparks at the pipe's mouth, and it shoves back hard
            Fx.Gunshot(Fx.Gun.Shotgun, m_VM.Muzzle(), ray.direction, true);
            m_VM.Use();
            Fx.Kick(7f);
            Fx.Shake(0.3f);
            Fx.Punch(3.5f);
        }

        void StartShotgunReload()
        {
            if (m_Net.Count(Item.ShotgunShell) <= 0) { Hud.Push($"No shotgun shells - buy them in POWER ITEMS ({Binds.Name(Bind.Inventory)})"); return; }
            m_ShotgunReloadStart = Time.time;
            Sfx.Play2D(Sfx.Clink, 0.4f);
        }

        void TickShotgunReload(Item held)
        {
            if (held != Item.Shotgun || m_Net.Dead.Value) { m_ShotgunReloadStart = -1f; return; }
            if (m_ShotgunReloadStart < 0)
            {
                // fired and there's a shell: the next one goes in by itself
                if (!ShotgunLoaded(m_Net.HeldStack) && m_Net.Count(Item.ShotgunShell) > 0 && !MenuOpen && Time.time > m_NextShotgun) StartShotgunReload();
                return;
            }
            if (Time.time - m_ShotgunReloadStart >= Cfg.ShotgunReload)
            {
                m_ShotgunReloadStart = -1f;
                m_ShotgunSentLoad = Time.time;
                m_ShotgunSentFire = -10f;
                m_ShotgunSlot = m_Net.HeldSlot.Value;
                m_Net.ReloadShotgunRpc();
                Sfx.Play2D(Sfx.Clink, 0.6f);
            }
        }
    }
}
