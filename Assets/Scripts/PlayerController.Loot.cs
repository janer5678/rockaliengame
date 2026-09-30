using System;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Controls for the airdrop items: sniper, portal gun (and walking through portals), jetpack, eggs / rocket / bomb bush, airstrike map.</summary>
    public partial class PlayerController
    {
        public bool Scoped { get; private set; }
        public bool AirstrikeMapOpen { get; private set; }
        int m_PortalLockPair = -1;
        float m_JetUsed, m_JetSendAt;

        void HandleOnce(Item held, Action act)
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextSwing) return;
            m_NextSwing = Time.time + 0.8f;
            act();
            m_VM.Use();
            Sfx.Play2D(held == Item.GiantStaff ? Sfx.Zap : Sfx.Click, 0.7f);
        }

        /// <summary>Sniper: RMB scope, LMB fires (3 shots). Anything it hits dies, except a helmet stops a headshot.</summary>
        void HandleSniper()
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextSwing) return;
            if (m_Net.HeldStack.Data == 0) return;
            m_NextSwing = Time.time + 1.2f;
            var ray = CenterRay();
            bool hit = AimWithAssist(ray, 400f, Cfg.ProjectileAssist, out var h);
            var no = hit ? h.collider.GetComponentInParent<NetworkObject>() : null;
            var point = hit ? h.point : ray.GetPoint(400f);
            if (no != null && no.TryGetComponent(out PlayerNet p) && p != m_Net && !p.Dead.Value)
            {
                bool head = p.IsHeadshot(point);
                Fx.Blood(point, ray.direction, head);
                Hud.HitMarker(!(head && p.HelmetHp.Value > 0), head);
            }
            m_Net.SniperFireRpc(no != null, no != null ? new NetworkObjectReference(no) : default, point, ray.direction);
            Fx.Tracer(ray.origin + ray.direction * 0.5f - Vector3.up * 0.15f, point);
            m_VM.Use();
            Fx.Kick(6f);
            Fx.Shake(0.25f);
        }

        /// <summary>Portal gun: LMB shoots a portal onto whatever surface you aim at (two shots, one linked pair).</summary>
        void HandlePortalGun()
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextSwing) return;
            m_NextSwing = Time.time + 0.5f;
            var ray = CenterRay();
            RaycastHit best = default;
            bool found = false;
            foreach (var h in Physics.RaycastAll(ray, 120f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<PlayerNet>() != null) continue;
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            if (!found) { Hud.Push("Nothing to put a portal on there"); return; }
            m_Net.PortalRpc(best.point, best.normal);
            m_VM.Use();
            Sfx.Play2D(Sfx.Portal, 0.7f);
            Fx.Kick(1.5f);
        }

        /// <summary>Walk into a portal and come out of its partner. You can't go back until you've stepped away from both.</summary>
        void TickPortals(bool dead)
        {
            var g = NetGame.Instance;
            if (g == null || dead || m_Net.Riding || g.Portals.Count < 2) return;
            var c = transform.position + Vector3.up * 0.9f;
            if (m_PortalLockPair >= 0)
            {
                bool near = false;
                foreach (var p in g.Portals) if (p.Pair == m_PortalLockPair && Vector3.Distance(p.Pos, c) < 2f) near = true;
                if (near) return;
                m_PortalLockPair = -1;
            }
            for (int i = 0; i < g.Portals.Count; i++)
            {
                var p = g.Portals[i];
                var d = c - p.Pos;
                float along = Vector3.Dot(d, p.Normal);
                var lateral = d - p.Normal * along;
                if (along < -0.4f || along > 1.2f || lateral.magnitude > 1.0f) continue;
                if (!g.TryPartner(i, out var to)) continue;
                // out of the other one, facing out of it
                Vector3 feet;
                float yaw = m_Yaw;
                if (to.Normal.y > 0.7f) feet = to.Pos + Vector3.up * 0.15f;             // a floor portal: pop up out of it
                else if (to.Normal.y < -0.7f) feet = to.Pos + Vector3.down * 2.1f;     // a ceiling portal: drop out of it
                else
                {
                    var flat = new Vector3(to.Normal.x, 0, to.Normal.z).normalized;
                    feet = to.Pos + flat * 0.8f + Vector3.down * 0.9f;
                    yaw = Quaternion.LookRotation(flat).eulerAngles.y;
                    m_Push = flat * 3f;
                }
                float pitch = m_Pitch;
                LocalTeleport(feet, yaw);
                m_Pitch = pitch;
                m_PortalLockPair = to.Pair;
                Sfx.Play2D(Sfx.Portal, 0.8f);
                Fx.Punch(8f);
                break;
            }
        }

        /// <summary>Jetpack (held): hold Space to fly up while there's fuel.</summary>
        void TickJetpack(Item held, bool canMove)
        {
            bool want = held == Item.Jetpack && canMove && Binds.Held(Bind.Jump) && m_Net.HeldStack.Data > 0 && !MenuOpen;
            if (want)
            {
                m_VelY = Mathf.Min(m_VelY + (Cfg.Gravity + Cfg.JetpackThrust) * Time.deltaTime, 7f);
                m_JetUsed += Time.deltaTime;
                if (Time.frameCount % 6 == 0) Sfx.Play2D(Sfx.Jet, 0.25f, 0.1f);
            }
            if (m_Net.Jetting.Value != want) m_Net.Jetting.Value = want;
            if (m_JetUsed > 0f && Time.time >= m_JetSendAt)
            {
                m_JetSendAt = Time.time + 0.3f;
                m_Net.JetFuelRpc(m_JetUsed);
                m_JetUsed = 0f;
            }
        }

        /// <summary>Slenderman egg, build egg, fake bomb bush: thrown. Rocket launcher: fires its one rocket.</summary>
        void HandleLootThrow(Item kind)
        {
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextSwing) return;
            m_NextSwing = Time.time + 0.6f;
            var ray = CenterRay();
            Vector3 origin = SafeOrigin(ray, 0.7f);
            Vector3 vel = kind == Item.RocketLauncher ? ray.direction * Cfg.RocketSpeed
                : kind == Item.EnderPearl ? ray.direction * Cfg.EnderPearlSpeed + Vector3.up * 1.5f
                : ray.direction * 16f + Vector3.up * 2.5f;
            ArrowProjectile.SpawnThrown(kind, origin, vel, m_Net, kind != Item.BuildEgg);
            m_Net.ThrowItemRpc(kind, origin, vel);
            if (kind == Item.RocketLauncher) { m_VM.Use(); Fx.Kick(5f); Fx.Shake(0.3f); }
            else { m_VM.Throw(); Sfx.Play2D(Sfx.Throw, 0.6f); }
        }

        public void OpenAirstrikeMap()
        {
            AirstrikeMapOpen = true;
            MenuOpen = false;
        }

        public void CloseAirstrikeMap() => AirstrikeMapOpen = false;

        /// <summary>Clicked a spot on the map: call the airstrike in there.</summary>
        public void ConfirmAirstrike(Vector3 world)
        {
            AirstrikeMapOpen = false;
            if (m_Net.HeldItem != Item.Airstrike) return;
            m_Net.AirstrikeRpc(world);
            Sfx.Play2D(Sfx.Beep, 0.8f);
        }
    }
}
