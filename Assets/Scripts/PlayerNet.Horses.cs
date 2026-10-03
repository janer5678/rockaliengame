using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>Feeding horses: LMB on a horse while holding berries gives it HP back (one berry a click; you don't eat it).</summary>
    public partial class PlayerNet
    {
        float m_NextFeed;

        /// <summary>The feeder's client found a horse under the crosshair; the server checks it, takes one berry and heals it.</summary>
        [Rpc(SendTo.Server)]
        public void FeedHorseRpc(NetworkObjectReference horse)
        {
            if (Dead.Value || HeldItem != Item.Berry || Time.time < m_NextFeed) return;
            if (!horse.TryGet(out var no) || !no.TryGetComponent(out Vehicle v) || !v.IsHorse || !v.IsSpawned) return;
            if (Vector3.Distance(EyePos, v.transform.position + Vector3.up * 1f) > Cfg.InteractRange + 2.5f) return;
            m_NextFeed = Time.time + 0.35f;
            if (v.Hp.Value >= v.MaxHp - 0.5f) { Notify("That horse is already at full health"); return; }
            ServerConsumeHeld();
            float got = v.ServerHeal(Cfg.HorseBerryHeal);
            Notify($"Fed the horse a berry: +{got:0} HP ({v.Hp.Value:0}/{v.MaxHp:0})");
        }
    }
}
