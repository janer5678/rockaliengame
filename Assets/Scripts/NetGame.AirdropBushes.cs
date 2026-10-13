using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        /// <summary>Airdrop Test (GameRules.AirdropTest): Classic, plus airdrop berry bushes all over the map.</summary>
        public static bool AirdropTest => Rules == GameRules.AirdropTest;
        /// <summary>Airdrop Test: airdrop berry bushes in each team's half of the map (copied round, so every side has the
        /// same), and how long a picked one takes to grow back somewhere else.</summary>
        [Tune("Airdrop Test mode")] public static int AirdropBushesPerSide = 10;
        [Tune("Airdrop Test mode")] public static float AirdropBushRespawn = 25f;
    }

    /// <summary>
    /// AIRDROP TEST MODE: Classic, plus AIRDROP BERRY BUSHES all over the map - a berry bush with purple berries and a
    /// spinning purple gem and beam over it (ResourceNode.AirdropBush). Pick one (E, like any berry bush) and you get an
    /// airdrop item: one of the picked airdrop items (mode options), by its rarity, that YOU haven't had from a bush yet -
    /// everyone can get the portal gun, but nobody gets it twice. A picked bush grows back somewhere else on that side.
    /// </summary>
    public partial class NetGame
    {
        /// <summary>The airdrop berry bush's berries.</summary>
        public static readonly Color AirdropBerry = new Color(0.62f, 0.2f, 1f);
        public const float AirdropGemHeight = 2.3f;

        /// <summary>Server: the airdrop items each player (team &lt;&lt; 4 | slot) has had this match - from the bushes, out of
        /// an airdrop crate, as respawn loot or from the notebook: nobody gets the same airdrop item twice in a game (in
        /// Airdrop Test the list starts over once you've had them all).</summary>
        readonly Dictionary<int, HashSet<Item>> m_BushItemsHad = new Dictionary<int, HashSet<Item>>();
        readonly List<(double at, int region)> m_AirdropBushQueue = new List<(double, int)>();

        /// <summary>Server: `p` just got an airdrop item (out of a crate, respawn loot, the notebook): not again this game.</summary>
        public void NoteAirdropItem(PlayerNet p, Item it)
        {
            if (p == null || System.Array.IndexOf(Cfg.AllAirdropItems, it) < 0 || it == Item.Wood) return;
            BushItemsHad(p).Add(it);
        }

        /// <summary>Server: every airdrop item anyone has had this game (a crate never brings one of those).</summary>
        public HashSet<Item> AirdropItemsAnyoneHad()
        {
            var s = new HashSet<Item>();
            foreach (var h in m_BushItemsHad.Values) s.UnionWith(h);
            return s;
        }

        /// <summary>The airdrop items a player has had this game (every way: NoteAirdropItem).</summary>
        public HashSet<Item> BushItemsHad(PlayerNet p)
        {
            int key = (p.Team.Value << 4) | p.Slot.Value;
            if (!m_BushItemsHad.TryGetValue(key, out var had)) m_BushItemsHad[key] = had = new HashSet<Item>();
            return had;
        }

        /// <summary>Server, as the map's nodes go down: the airdrop berry bushes, spread over every side the same.</summary>
        void SpawnAirdropBushes(System.Random rng, List<Vector3> placed)
        {
            float half = Cfg.MapHalf;
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int n = 0; n < Mathf.Max(0, Cfg.AirdropBushesPerSide); n++)
            {
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    // (spread out: anywhere in the first team's half, away from its base, the dome and the other nodes)
                    var p = new Vector3(R(-half + 8, half - 8), 0, R(-half + 8, -6f));
                    if (!Cfg.InFirstSector(p, 4f)) continue;
                    if (Mathf.Abs(p.x - Cfg.BaseCenter[0].x) < Cfg.BaseHalf + 4 && Mathf.Abs(p.z - Cfg.BaseCenter[0].z) < Cfg.BaseHalf + 4) continue;
                    if (new Vector2(p.x, p.z).magnitude < MapBuilder.DomeRadius + 4f) continue;
                    if (!ThemeMaps.SpotOk(p) || InsideScenery(p)) continue;
                    bool close = false;
                    foreach (var q in placed) if ((q - p).sqrMagnitude < 9f * 9f) { close = true; break; }
                    if (close) continue;
                    placed.Add(p);
                    int seed = rng.Next();
                    for (int m = 0; m < Cfg.Copies; m++) SpawnAirdropBush(Cfg.Copy(p, m), seed);
                    break;
                }
            }
        }

        void SpawnAirdropBush(Vector3 pos, int seed)
        {
            if (ThemeMaps.Custom != null && !ThemeMaps.SpotOk(pos)) return; // THEME MAPS
            pos.y = MapBuilder.Height(pos.x, pos.z) - 0.1f;
            var go = Instantiate(Bootstrap.I.nodePrefab, pos, Quaternion.Euler(0, seed % 360, 0));
            var n = go.GetComponent<ResourceNode>();
            n.ServerInit(ResourceNode.Bush, seed);
            n.TrapTeam.Value = ResourceNode.AirdropBush; // (set before it spawns: everyone builds it purple from the start)
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        /// <summary>Server: an airdrop berry bush was picked - a new one grows somewhere else on that side later.</summary>
        public void ServerScheduleAirdropBush(Vector3 where) => m_AirdropBushQueue.Add((NetworkManager.ServerTime.Time + Cfg.AirdropBushRespawn, Cfg.RegionOf(where)));

        void ServerTickAirdropBushes(double now)
        {
            for (int i = m_AirdropBushQueue.Count - 1; i >= 0; i--)
            {
                if (now < m_AirdropBushQueue[i].at) continue;
                int region = m_AirdropBushQueue[i].region;
                m_AirdropBushQueue.RemoveAt(i);
                float half = Cfg.MapHalf;
                for (int tries = 0; tries < 60; tries++)
                {
                    var p = new Vector3(Random.Range(-half + 8f, half - 8f), 0, Random.Range(-half + 8f, half - 8f));
                    if (Cfg.RegionOf(p) != region || Cfg.BaseTeamAt(p) >= 0 || new Vector2(p.x, p.z).magnitude < MapBuilder.DomeRadius + 4f) continue;
                    p.y = MapBuilder.Height(p.x, p.z);
                    if (!ThemeMaps.SpotOk(p) || Blocked(p + Vector3.up * 1.3f, new Vector3(1f, 0.9f, 1f))) continue;
                    SpawnAirdropBush(p, Random.Range(0, 1 << 30));
                    break;
                }
            }
        }

        /// <summary>Server: `p` picked an airdrop berry bush - one airdrop item they haven't had yet (what doesn't fit lands
        /// at their feet). The bush is gone (and grows back somewhere else).</summary>
        public void ServerPickAirdropBush(PlayerNet p, ResourceNode n)
        {
            var had = BushItemsHad(p);
            var pool = BushPool(had);
            if (pool.Count == 0) { had.Clear(); pool = BushPool(had); } // (safety: everything had - start over)
            if (pool.Count == 0) return;
            var it = Cfg.PickAirdropItem(pool);
            if (n.ServerHarvest(1, false, p.transform.position) <= 0) return;
            had.Add(it);
            // that was the last one they hadn't had: the list starts over (anything can come again) - and they're told
            bool all = BushPool(had).Count == 0;
            if (all) had.Clear();
            var st = Cfg.GiftStack(it);
            int left = p.ServerGive(st.Id, st.Count, st.Data);
            if (left > 0) ServerDropItem(ItemStack.Of(st.Id, left, st.Data), p.transform.position + p.transform.forward, p.transform.forward, p.EyePos);
            Fx.Server(FxKind.OmenFlash, n.transform.position + Vector3.up, Vector3.up);
            p.WackyNoteRpc(new Unity.Collections.FixedString128Bytes($"AIRDROP BERRY: {Cfg.ItemName(it)}!"));
            if (all) { p.AllAirdropsRpc(); AllAirdropResets++; }
        }

        /// <summary>For the tests: how many times a player's had every airdrop item and the list started over.</summary>
        public int AllAirdropResets { get; private set; }

        /// <summary>What an airdrop berry bush can give a player who's had `had`: the picked airdrop items they haven't had
        /// (by the mode options), then any airdrop item they haven't had. Empty = they've had them all.</summary>
        static List<Item> BushPool(HashSet<Item> had)
        {
            var pool = new List<Item>();
            foreach (var i in Cfg.AirdropLoot) if (Special(i) && !had.Contains(i)) pool.Add(i);
            if (pool.Count == 0) foreach (var i in Cfg.AirdropChoices) if (Special(i) && Cfg.InAirdropPool(i) && !had.Contains(i)) pool.Add(i);
            return pool;
        }

        /// <summary>Every screen: the airdrop berry bush's marker - a purple gem spinning over it and a faint beam, so they're
        /// seen across the map. Returns the gem (ResourceNode spins it).</summary>
        public static Transform BuildAirdropBushMarker(Transform bush, float size)
        {
            var gem = new GameObject("airdrop gem").transform;
            gem.SetParent(bush, false);
            gem.localPosition = new Vector3(0, AirdropGemHeight, 0);
            var purple = AirdropBerry;
            Art.Part(gem, Art.Ico, Color.Lerp(purple, Color.white, 0.25f), Vector3.zero, new Vector3(0.38f, 0.52f, 0.38f));
            Art.Part(gem, Art.Ico, Color.white, Vector3.zero, Vector3.one * 0.16f);
            var beam = Art.Part(bush, Art.Cylinder, Color.white, new Vector3(0, 4f, 0), new Vector3(0.22f, 4f, 0.22f), default, false, Art.Ghost(new Color(purple.r, purple.g, purple.b, 0.22f)));
            beam.name = "airdrop beam";
            foreach (var mr in gem.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return gem;
        }
    }
}
