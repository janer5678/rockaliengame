using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>A gravestone where somebody died. They pile up for the whole match.</summary>
    public struct GraveInfo : INetworkSerializeByMemcpy, System.IEquatable<GraveInfo>
    {
        public Vector3 Pos;   // on the ground
        public float Yaw;     // which way it faces
        public byte Team;     // the colour band
        public byte Seed;     // the slight random lean
        public bool Equals(GraveInfo o) => Pos == o.Pos && Yaw == o.Yaw && Team == o.Team && Seed == o.Seed;
    }

    public partial class NetGame
    {
        /// <summary>
        /// Every grave this match: one goes down wherever a player dies (any mode, the space arena too) and none is ever
        /// taken away - they're gone when the match ends (a new match is a new NetGame). Synced to late joiners.
        /// </summary>
        public readonly NetworkList<GraveInfo> Graves = new NetworkList<GraveInfo>();

        /// <summary>A safety cap only (a match never gets near it): past this, new deaths don't add graves.</summary>
        public const int MaxGraves = 1000;

        /// <summary>Server: put a gravestone on the ground under where this player died (nothing if they fell into the void).</summary>
        public void ServerAddGrave(Vector3 at, float yaw, int team)
        {
            if (!IsServer || Graves.Count >= MaxGraves) return;
            if (!GroundUnder(at, out var ground)) return;
            Graves.Add(new GraveInfo { Pos = ground, Yaw = yaw, Team = (byte)team, Seed = (byte)Random.Range(0, 256) });
        }

        static readonly RaycastHit[] s_GraveHits = new RaycastHit[16];

        static bool GroundUnder(Vector3 at, out Vector3 ground)
        {
            ground = at;
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 1f, Vector3.down, s_GraveHits, 40f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = s_GraveHits[i];
                if (h.collider.GetComponentInParent<PlayerNet>() != null || h.collider.GetComponentInParent<Vehicle>() != null) continue;
                if (h.distance < best) { best = h.distance; ground = h.point; }
            }
            return best < float.MaxValue;
        }
    }

    /// <summary>Draws NetGame.Graves: every grave is the same low-poly stone cross (the same shape and stone colour for
    /// everyone; Scale times the size it was first made - about 1.7 m tall), with a band in the dead player's team colour,
    /// a dirt mound and a slight random lean. No colliders. It's all there is where someone died: no body is left.</summary>
    public static class GraveFx
    {
        /// <summary>How big the graves are (1 = the little 0.95 m cross they used to be).</summary>
        public const float Scale = 1.8f;

        static readonly List<GameObject> s_Shown = new List<GameObject>();
        static Transform s_Root;

        public static int Shown => s_Shown.Count;
        public static GameObject Get(int i) => i >= 0 && i < s_Shown.Count ? s_Shown[i] : null;

        public static void Sync(NetGame g)
        {
            int n = g.Graves.Count;
            while (s_Shown.Count > n) { if (s_Shown[s_Shown.Count - 1]) Object.Destroy(s_Shown[s_Shown.Count - 1]); s_Shown.RemoveAt(s_Shown.Count - 1); }
            for (int i = s_Shown.Count; i < n; i++) s_Shown.Add(Build(g.Graves[i]));
        }

        public static void Clear()
        {
            foreach (var go in s_Shown) if (go) Object.Destroy(go);
            s_Shown.Clear();
            if (s_Root) Object.Destroy(s_Root.gameObject);
            s_Root = null;
        }

        static readonly Color Stone = new Color(0.62f, 0.63f, 0.66f);
        static readonly Color Dirt = new Color(0.36f, 0.26f, 0.17f);

        static GameObject Build(GraveInfo gi)
        {
            if (s_Root == null) s_Root = new GameObject("Graves").transform;
            var rng = new System.Random(gi.Seed * 7919 + 13);
            float tiltX = (float)(rng.NextDouble() * 2 - 1) * 8f, tiltZ = (float)(rng.NextDouble() * 2 - 1) * 6f;
            var team = Cfg.TeamColor[Mathf.Clamp(gi.Team, 0, Cfg.TeamColor.Length - 1)];

            var go = new GameObject("Grave");
            go.transform.SetParent(s_Root, false);
            go.transform.SetPositionAndRotation(gi.Pos, Quaternion.Euler(0f, gi.Yaw, 0f));
            go.transform.localScale = Vector3.one * Scale;
            // a low mound of dirt in front of the stone
            Art.Box(go.transform, Dirt, new Vector3(0f, 0.04f, 0.45f), new Vector3(0.6f, 0.14f, 1.0f), new Vector3(0f, 0f, 0f));
            var stone = new GameObject("stone").transform;
            stone.SetParent(go.transform, false);
            stone.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
            // the stone cross (the same for everybody): an upright, the cross bar, and the team colour where they meet
            Art.Box(stone, Stone, new Vector3(0f, 0.45f, 0f), new Vector3(0.14f, 0.95f, 0.14f)).name = "cross upright";
            Art.Box(stone, Stone, new Vector3(0f, 0.68f, 0f), new Vector3(0.56f, 0.14f, 0.14f)).name = "cross bar";
            Art.Box(stone, team, new Vector3(0f, 0.68f, 0f), new Vector3(0.18f, 0.16f, 0.16f)).name = "team band";
            return go;
        }
    }
}
