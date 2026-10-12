using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Worn gear, put on by itself like armour the moment it's in your inventory:
    /// - the JETPACK goes on your back (hold Space to fly while there's fuel - PlayerController.TickJetpack); its fuel % shows
    ///   bottom left, under the armour. Run dry, it's gone, and the next jetpack you have goes straight on.
    /// - the WALLHACK GLASSES go on your face: enemies glow through walls for as long as you wear them (no durability).
    /// Once worn, neither drops when you die: you keep them on.
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>The worn jetpack's fuel (0..Cfg.JetpackFuel), -1 = no jetpack on.</summary>
        public readonly NetworkVariable<short> WornJetFuel = new NetworkVariable<short>(-1);
        /// <summary>Wearing the wallhack glasses.</summary>
        public readonly NetworkVariable<bool> WornGlasses = new NetworkVariable<bool>();

        public bool WearingJetpack => WornJetFuel.Value > 0;
        /// <summary>Enemies show through walls for this player: glasses on, or a pair held.</summary>
        public bool SeesThroughWalls => WornGlasses.Value || HeldItem == Item.Wallhack;

        float m_NextGearCheck;
        GameObject m_JetPack;

        /// <summary>Server, a few times a second: a jetpack or a pair of glasses in the inventory goes straight on.</summary>
        void ServerAutoEquip()
        {
            if (Time.time < m_NextGearCheck || Dead.Value) return;
            m_NextGearCheck = Time.time + 0.2f;
            for (int i = 0; i < Inv.Count; i++)
            {
                var st = Inv[i];
                if (st.Id == Item.Jetpack && WornJetFuel.Value <= 0)
                {
                    int fuel = st.Data > 0 ? st.Data : Cfg.JetpackFuel;
                    Inv[i] = default;
                    WornJetFuel.Value = (short)Mathf.Clamp(fuel, 1, 999);
                    Notify($"Jetpack on: hold jump to fly ({FuelPercent}% fuel)");
                }
                else if (st.Id == Item.Wallhack && !WornGlasses.Value)
                {
                    Inv[i] = default;
                    WornGlasses.Value = true;
                    Notify("Wallhack glasses on: you see your enemies through walls");
                }
            }
        }

        /// <summary>The worn jetpack's fuel as a percentage (0 with none on).</summary>
        public int FuelPercent => WornJetFuel.Value <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(WornJetFuel.Value * 100f / Mathf.Max(1, Cfg.JetpackFuel)), 0, 100);

        /// <summary>Server: the worn jetpack burned this much fuel; empty, it's gone.</summary>
        void ServerBurnFuel(float seconds)
        {
            if (WornJetFuel.Value <= 0) return;
            int used = Mathf.CeilToInt(Mathf.Clamp(seconds, 0f, 2f) / Mathf.Max(0.5f, Cfg.JetpackSeconds) * Cfg.JetpackFuel);
            int left = WornJetFuel.Value - used;
            if (left <= 0) { WornJetFuel.Value = -1; Notify("The jetpack ran out of fuel"); m_NextGearCheck = 0f; }
            else WornJetFuel.Value = (short)left;
        }

        /// <summary>Every screen: the jetpack on the wearer's back (the flame under it is RebuildFlame's).</summary>
        void TickGearLooks(bool dead)
        {
            bool pack = WornJetFuel.Value > 0 && !dead && m_VisualRoot != null;
            if (pack == (m_JetPack != null)) return;
            if (m_JetPack) { Destroy(m_JetPack); m_JetPack = null; }
            if (!pack) return;
            m_JetPack = new GameObject("jetpack");
            m_JetPack.transform.SetParent(m_VisualRoot, false);
            m_JetPack.transform.localPosition = new Vector3(0, 1.05f, -0.33f);
            var metal = new Color(0.3f, 0.3f, 0.33f);
            Art.Box(m_JetPack.transform, metal, new Vector3(0, 0, 0), new Vector3(0.42f, 0.48f, 0.16f));
            for (int s = -1; s <= 1; s += 2)
            {
                Art.Part(m_JetPack.transform, Art.Cylinder, metal * 1.2f, new Vector3(s * 0.12f, -0.02f, -0.08f), new Vector3(0.11f, 0.24f, 0.11f));
                Art.Part(m_JetPack.transform, Art.Cylinder, new Color(0.15f, 0.15f, 0.16f), new Vector3(s * 0.12f, -0.27f, -0.08f), new Vector3(0.08f, 0.04f, 0.08f));
            }
            Art.Box(m_JetPack.transform, Cfg.TeamColor[Mathf.Clamp(Team.Value, 0, 3)], new Vector3(0, 0.16f, -0.085f), new Vector3(0.3f, 0.05f, 0.01f));
            if (Mine) Art.SetLayerShadowsOnly(m_JetPack);
        }
    }
}
