using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Airdrop items (sniper, portal gun, jetpack, eggs, staff of the giant, rocket launcher, bomb bush, airstrike) and the airdrop signal.</summary>
    public partial class PlayerNet
    {
        float m_NextSniper, m_NextPortal, m_NextUse;
        readonly Dictionary<Item, int> m_PendingThrows = new Dictionary<Item, int>();

        static bool Throwable(Item i) => i == Item.SlenderEgg || i == Item.BuildEgg || i == Item.RocketLauncher || i == Item.BombBush || i == Item.EnderPearl;

        /// <summary>Sniper: one shot kills, whatever it hits - unless it's a headshot on someone wearing a helmet (the helmet breaks).</summary>
        [Rpc(SendTo.Server)]
        public void SniperFireRpc(bool hasTarget, NetworkObjectReference target, Vector3 point, Vector3 dir)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Sniper || Time.time < m_NextSniper) return;
            var st = HeldStack;
            if (st.Data == 0) return;
            m_NextSniper = Time.time + 1.1f;
            Reveal();
            int left = st.Data - 1;
            if (left <= 0) { ServerClearSlot(HeldSlot.Value); Notify("Out of sniper ammo - the rifle is gone"); }
            else Inv[HeldSlot.Value] = ItemStack.Of(Item.Sniper, 1, left);
            if (Vector3.Distance(point, EyePos) > 400f) point = EyePos + dir.normalized * 100f;
            Fx.Server(FxKind.SniperTracer, EyePos + dir.normalized * 0.5f - Vector3.up * 0.15f, point);
            if (!hasTarget || !target.TryGet(out var no) || !GameAllowsCombat) return;
            if (no.TryGetComponent(out PlayerNet p) && p != this && !p.Dead.Value)
            {
                bool head = p.IsHeadshot(point);
                Fx.Server(head ? FxKind.BloodHead : FxKind.Blood, point, dir);
                if (head && p.HelmetHp.Value > 0)
                {
                    p.HelmetHp.Value = 0;
                    Fx.Server(FxKind.HelmetBreak, p.EyePos, Vector3.up);
                    p.Notify("Your helmet stopped a sniper headshot!");
                    Notify("Their helmet stopped your sniper shot!");
                    return;
                }
                p.ServerKill(this);
                if (p.Dead.Value) KillConfirmRpc();
            }
            else if (no.TryGetComponent(out Vehicle v)) v.ServerDamage(9999f, this);
            else if (no.TryGetComponent(out Structure s)) s.ServerDamage(60f);
        }

        /// <summary>The portal gun's two portals have to be at least this far apart.</summary>
        public const float PortalMinGap = 3f;

        /// <summary>
        /// Portal gun: ONE shot, then it's used up. That shot makes the whole linked pair: one portal on the ground where
        /// you're standing and the other where the shot lands (any surface inside the map). Portals stay for the game (the
        /// oldest pairs go once there are too many). The portal that opens under you doesn't take you until you've stepped
        /// off it (PlayerController.TickPortals).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void PortalRpc(Vector3 point, Vector3 normal)
        {
            var g = NetGame.Instance;
            if (Dead.Value || g == null || HeldItem != Item.PortalGun || Time.time < m_NextPortal) return;
            if (Mathf.Abs(point.x) > Cfg.MapHalf + 2 || Mathf.Abs(point.z) > Cfg.MapHalf + 2 || Vector3.Distance(point, EyePos) > 120f) { Notify("Portals only work inside the battle area"); return; }
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            // the other end: on the ground under your feet
            if (!PortalFeet(out var feet, out var feetNormal)) { Notify("Stand on something to open a portal"); return; }
            if (Vector3.Distance(feet, point) < PortalMinGap) { Notify("Too close - shoot the portal further away"); return; }
            m_NextPortal = Time.time + 0.4f;
            int pair = g.ServerNewPortalPair();
            g.ServerAddPortal(feet + feetNormal * 0.03f, feetNormal, pair);
            g.ServerAddPortal(point + normal.normalized * 0.03f, normal, pair);
            ServerClearSlot(HeldSlot.Value);
            Notify("Portals linked: one where you stand, one where you shot - the portal gun is used up");
        }

        /// <summary>The ground right under this player (for the portal gun's portal at your feet).</summary>
        bool PortalFeet(out Vector3 point, out Vector3 normal)
        {
            point = transform.position;
            normal = Vector3.up;
            RaycastHit best = default;
            bool found = false;
            foreach (var h in Physics.RaycastAll(transform.position + Vector3.up * 0.5f, Vector3.down, 3f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PlayerNet>() != null || h.collider.GetComponentInParent<Vehicle>() != null) continue;
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            if (!found) return false;
            point = best.point;
            normal = best.normal.y > 0.5f ? best.normal : Vector3.up;
            return true;
        }

        /// <summary>The jetpack reports how long it thrusted; fuel runs out and then it's gone.</summary>
        [Rpc(SendTo.Server)]
        public void JetFuelRpc(float seconds)
        {
            if (Dead.Value || HeldItem != Item.Jetpack) return;
            var st = HeldStack;
            int used = Mathf.CeilToInt(Mathf.Clamp(seconds, 0f, 2f) / Mathf.Max(0.5f, Cfg.JetpackSeconds) * Cfg.JetpackFuel);
            int left = st.Data - used;
            if (left <= 0) { ServerClearSlot(HeldSlot.Value); Notify("The jetpack ran out of fuel"); }
            else Inv[HeldSlot.Value] = ItemStack.Of(Item.Jetpack, 1, left);
        }

        /// <summary>Slenderman egg, build egg, rocket and fake bomb bush: thrown / fired, then the thrower's client reports where it landed.</summary>
        [Rpc(SendTo.Server)]
        public void ThrowItemRpc(Item kind, Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || !Throwable(kind) || HeldItem != kind || Time.time < m_NextThrow) return;
            m_NextThrow = Time.time + 0.5f;
            Reveal();
            var held = HeldStack;
            if (kind == Item.RocketLauncher && held.Data > 1) Inv[HeldSlot.Value] = ItemStack.Of(Item.RocketLauncher, 1, held.Data - 1); // rockets left in it
            else ServerConsumeHeld();
            if (kind == Item.BuildEgg)
            {
                // the server lays the blocks along the egg's path itself
                if (NetGame.Instance != null) NetGame.Instance.ServerBuildEgg(origin, velocity, Team.Value);
            }
            else
            {
                m_PendingThrows.TryGetValue(kind, out int n);
                m_PendingThrows[kind] = Mathf.Min(n + 1, 3);
            }
            ThrownVisualRpc(kind, origin, velocity);
        }

        [Rpc(SendTo.Server)]
        public void ThrownLandRpc(Item kind, Vector3 point, Vector3 normal)
        {
            var g = NetGame.Instance;
            if (g == null || !m_PendingThrows.TryGetValue(kind, out int n) || n <= 0) return;
            m_PendingThrows[kind] = n - 1;
            if (Vector3.Distance(point, transform.position) > 250f) point = transform.position + transform.forward * 3f;
            switch (kind)
            {
                case Item.RocketLauncher: g.ServerRocket(point, this); break;
                case Item.SlenderEgg:
                    Vehicle.ServerSpawnSlender(point + Vector3.up * 0.2f, Team.Value);
                    g.Broadcast($"{Cfg.TeamLabel(Team.Value)} hatched a SLENDERMAN - it's coming for you...");
                    break;
                case Item.BombBush: g.ServerSpawnBombBush(point, Team.Value); break;
                case Item.EnderPearl: ServerPearlLanded(point, normal); break;
            }
        }

        /// <summary>A rocket that hits a player square on kills them outright (then it explodes as usual).</summary>
        [Rpc(SendTo.Server)]
        public void RocketDirectHitRpc(NetworkObjectReference target, Vector3 point)
        {
            var g = NetGame.Instance;
            if (g == null || !m_PendingThrows.TryGetValue(Item.RocketLauncher, out int n) || n <= 0) return;
            m_PendingThrows[Item.RocketLauncher] = n - 1;
            if (Vector3.Distance(point, transform.position) > 250f) return;
            if (target.TryGet(out var no) && no.TryGetComponent(out PlayerNet p) && p != this && !p.Dead.Value && GameAllowsCombat
                && Vector3.Distance(p.transform.position + Vector3.up, point) < 2.5f && !GlassBetween(transform.position, p.transform.position))
            {
                p.ServerKill(this);
                Notify("Direct hit!");
            }
            g.ServerRocket(point, this);
        }

        [Rpc(SendTo.NotOwner)]
        void ThrownVisualRpc(Item kind, Vector3 origin, Vector3 velocity)
        {
            ArrowProjectile.SpawnThrown(kind, origin, velocity, this, false);
            if (kind != Item.RocketLauncher) LocalThrowAnim(); // a rocket is fired, not thrown
        }

        /// <summary>Staff of the giant: the nearest enemy becomes a giant for a while (huge and easy to see, but same hitbox).</summary>
        [Rpc(SendTo.Server)]
        public void GiantStaffRpc()
        {
            if (Dead.Value || HeldItem != Item.GiantStaff || Time.time < m_NextUse) return;
            m_NextUse = Time.time + 1f;
            PlayerNet target = null;
            float best = float.MaxValue;
            foreach (var p in All)
            {
                if (p == this || p.Dead.Value || p.Team.Value == Team.Value) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < best) { best = d; target = p; }
            }
            if (target == null) { Notify("There's nobody to turn into a giant"); return; }
            ServerConsumeHeld();
            target.GiantUntil.Value = NetworkManager.ServerTime.Time + Cfg.GiantTime;
            Fx.Server(FxKind.Drink, target.transform.position, Vector3.up);
            if (NetGame.Instance != null) NetGame.Instance.Broadcast($"{Cfg.TeamLabel(Team.Value)} turned {Cfg.TeamLabel(target.Team.Value)} into a GIANT!");
        }

        /// <summary>Airstrike: picked on the map; a few seconds later everything in the zone is flattened.</summary>
        [Rpc(SendTo.Server)]
        public void AirstrikeRpc(Vector3 pos)
        {
            if (Dead.Value || HeldItem != Item.Airstrike || NetGame.Instance == null || Time.time < m_NextUse) return;
            if (Mathf.Abs(pos.x) > Cfg.MapHalf || Mathf.Abs(pos.z) > Cfg.MapHalf) return;
            m_NextUse = Time.time + 1f;
            ServerConsumeHeld();
            NetGame.Instance.ServerAirstrike(pos, this);
        }
    }
}
