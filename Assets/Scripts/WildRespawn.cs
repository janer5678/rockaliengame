using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// "Respawn in the wild": in every mode (not the tutorial) you pick the spot on a map of the whole map
    /// (Hud.Death.cs: DrawWildPicker) - but never close to an enemy base (WildBaseGap metres from its edge) or an airdrop
    /// (WildDropGap metres from a crate on the ground, a fake one too, or one still beaming down). The picker shades
    /// those zones red; the server keeps to them too (NetGame.PickedSpawnPoint), and so does the random wild spot.
    /// </summary>
    public static partial class Cfg
    {
        [Tune("Match")] public static float WildBaseGap = 25f, WildDropGap = 30f;

        /// <summary>The picker's on in this mode (everywhere but the tutorial).</summary>
        public static bool WildPicker => !Tutorial;

        /// <summary>How far p is outside base t's square (0 inside it).</summary>
        public static float BaseGap(int t, Vector3 p)
        {
            var c = BaseCenter[t];
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - c.x) - BaseHalf), dz = Mathf.Max(0f, Mathf.Abs(p.z - c.z) - BaseHalf);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static readonly List<Vector3> s_DropSpots = new List<Vector3>();

        /// <summary>Can someone on `team` come back here in the wild? (why not, if not: "an enemy base" / "an airdrop")</summary>
        public static bool WildSpotOk(int team, Vector3 p, out string why)
        {
            why = null;
            for (int t = 0; t < TeamCount; t++)
                if (t != team && BaseGap(t, p) < WildBaseGap) { why = "an enemy base"; return false; }
            NetGame.AirdropSpots(s_DropSpots);
            foreach (var d in s_DropSpots)
            {
                float dx = p.x - d.x, dz = p.z - d.z;
                if (dx * dx + dz * dz < WildDropGap * WildDropGap) { why = "an airdrop"; return false; }
            }
            return true;
        }
        public static bool WildSpotOk(int team, Vector3 p) => WildSpotOk(team, p, out _);
    }

    public partial class NetGame
    {
        /// <summary>Every airdrop there is right now (for the wild respawn's no-go zones): the crates on the ground (fake ones
        /// too) and the ones still beaming down (their lane's spot).</summary>
        public static void AirdropSpots(List<Vector3> into)
        {
            into.Clear();
            foreach (var c in Container.All)
                if (c != null && c.IsSpawned && c.IsAirdrop) into.Add(c.transform.position);
            var g = Instance;
            if (g == null || !g.IsSpawned) return;
            double now = g.NetworkManager.ServerTime.Time;
            for (int i = 0; i < LaneTotal; i++)
            {
                double start = g.LaneStartAt(i);
                if (start >= 0 && now < start + DropLand / DropSpeed + 1.0) into.Add(g.LanePosAt(i));
            }
        }
    }
}
