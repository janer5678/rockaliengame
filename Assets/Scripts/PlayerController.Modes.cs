using UnityEngine;

namespace RockGame
{
    /// <summary>Client side of the game-mode items: the pistol (fire, reload) and the ender pearl throw.</summary>
    public partial class PlayerController
    {
        float m_NextPistolShot, m_PistolReloadStart = -1f;

        /// <summary>True while the pistol is being reloaded (for the HUD).</summary>
        public float PistolReloadProgress => m_PistolReloadStart < 0 ? -1f : Mathf.Clamp01((Time.time - m_PistolReloadStart) / Mathf.Max(0.05f, Cfg.PistolReload));

        /// <summary>Pistol: LMB fires (as fast as you click, up to its fire rate), R reloads; it reloads by itself when empty.</summary>
        void HandlePistol()
        {
            if (m_PistolReloadStart >= 0) return;
            var st = m_Net.HeldStack;
            if (Binds.Down(Bind.Rotate) && st.Data < Cfg.PistolMag) { StartPistolReload(); return; }
            if (!Binds.Down(Bind.Attack) || Time.time < m_NextPistolShot) return;
            if (st.Data <= 0) { StartPistolReload(); return; }
            m_NextPistolShot = Time.time + Cfg.PistolFireRate;
            // hitscan: whatever the crosshair is on, right now
            var ray = CenterRay();
            bool hit = AimWithAssist(ray, 250f, Cfg.ProjectileAssist, out var h);
            var no = hit ? h.collider.GetComponentInParent<Unity.Netcode.NetworkObject>() : null;
            var point = hit ? h.point : ray.GetPoint(250f);
            if (no != null && no.TryGetComponent(out PlayerNet p) && p != m_Net && !p.Dead.Value)
            {
                bool head = p.IsHeadshot(point);
                Fx.Blood(point, ray.direction, head);
                Fx.DamageNumber(point, head ? Cfg.PistolHeadDamage : Cfg.PistolBodyDamage, head);
                Hud.HitMarker(!(head && p.HelmetHp.Value > 0), head);
            }
            m_Net.FirePistolRpc(no != null, no != null ? new Unity.Netcode.NetworkObjectReference(no) : default, point, ray.direction);
            Fx.Tracer(ray.origin + ray.direction * 0.5f - Vector3.up * 0.15f, point);
            m_VM.Use();
            Sfx.Play2D(Sfx.Sniper, 0.45f, 0.08f);
            Fx.Kick(2f);
        }

        void StartPistolReload()
        {
            if (m_Net.Count(Item.PistolAmmo) <= 0) { Hud.Push("The pistol is out of shots"); return; }
            m_PistolReloadStart = Time.time;
            Sfx.Play2D(Sfx.Clink, 0.4f);
        }

        void TickPistolReload(Item held)
        {
            if (held != Item.Pistol || m_Net.Dead.Value) { m_PistolReloadStart = -1f; return; }
            if (m_PistolReloadStart < 0)
            {
                // empty and there's ammo: it reloads by itself
                if (m_Net.HeldStack.Data == 0 && m_Net.Count(Item.PistolAmmo) > 0 && !MenuOpen) StartPistolReload();
                return;
            }
            if (Time.time - m_PistolReloadStart >= Cfg.PistolReload)
            {
                m_PistolReloadStart = -1f;
                m_Net.ReloadPistolRpc();
                Sfx.Play2D(Sfx.Clink, 0.6f);
            }
        }
    }
}
