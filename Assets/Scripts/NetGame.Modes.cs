using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The game modes' server rules: Arsenal / Builder power items (fortify, auto wood gen), Builder's "ball in your own
    /// structure" win, and Fun / Fun Random's free item every few seconds.
    /// </summary>
    public partial class NetGame
    {
        /// <summary>Teams with an auto wood gen (bit per team).</summary>
        public readonly NetworkVariable<byte> WoodGenTeams = new NetworkVariable<byte>();
        /// <summary>Builder: the team whose own structure the ball is shut inside right now (-1 = nobody's).</summary>
        public readonly NetworkVariable<sbyte> BallEnclosedBy = new NetworkVariable<sbyte>(-1);
        /// <summary>Fun modes: when everyone gets their next item (server time, -1 = not handing out).</summary>
        public readonly NetworkVariable<double> NextFunItem = new NetworkVariable<double>(-1);

        double m_NextWoodGen;
        float m_NextEnclosureCheck;

        public bool HasWoodGen(int team) => team >= 0 && team < 8 && (WoodGenTeams.Value & (1 << team)) != 0;

        public void ServerWoodGen(int team)
        {
            if (team < 0 || team >= 8) return;
            WoodGenTeams.Value = (byte)(WoodGenTeams.Value | (1 << team));
        }

        /// <summary>Fortify: every wooden grid piece the team has placed turns to stone at full health. Returns how many.</summary>
        public int ServerFortify(int team)
        {
            int n = 0;
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || s.Team.Value != team || s.Tier.Value != 0 || !s.Upgradable) continue;
                s.ServerUpgrade();
                n++;
            }
            return n;
        }

        void ServerTickModes(double now)
        {
            bool playing = S == GameState.PreBall || S == GameState.BallLive;

            // auto wood gen
            if (playing && WoodGenTeams.Value != 0 && now >= m_NextWoodGen)
            {
                m_NextWoodGen = now + Mathf.Max(1f, Cfg.WoodGenInterval);
                foreach (var p in PlayerNet.All)
                    if (!p.Dead.Value && HasWoodGen(p.Team.Value)) p.ServerGive(Item.Wood, Cfg.WoodGenAmount);
            }

            // Fun / Fun Random: a free item every few seconds
            if (Cfg.FunRules && playing)
            {
                if (NextFunItem.Value < 0) NextFunItem.Value = now + Cfg.FunItemInterval;
                else if (now >= NextFunItem.Value)
                {
                    NextFunItem.Value = now + Mathf.Max(1f, Cfg.FunItemInterval);
                    ServerGiveFunItems();
                }
            }
            else if (NextFunItem.Value >= 0) NextFunItem.Value = -1;

            // Builder: is the ball shut in somebody's own structure?
            if (Cfg.Builder && Time.time >= m_NextEnclosureCheck)
            {
                m_NextEnclosureCheck = Time.time + 0.5f;
                int t = S == GameState.BallLive ? BallEnclosure() : -1;
                if (BallEnclosedBy.Value != t) BallEnclosedBy.Value = (sbyte)t;
            }
        }

        void ServerGiveFunItems()
        {
            if (Cfg.Rules == GameRules.Fun)
            {
                // everybody gets the same thing
                var all = Cfg.AllFunItems;
                var stack = Cfg.GiftStack(all[Random.Range(0, all.Count)]);
                foreach (var p in PlayerNet.All) if (!p.Dead.Value) ServerGiftTo(p, stack);
                Broadcast($"FUN: everyone got {Cfg.ItemName(stack.Id)}{(stack.Count > 1 ? " x" + stack.Count : "")}!");
            }
            else
            {
                // everybody gets something different
                foreach (var p in PlayerNet.All)
                {
                    if (p.Dead.Value) continue;
                    var stack = Cfg.GiftStack(Cfg.AllAirdropItems[Random.Range(0, Cfg.AllAirdropItems.Length)]);
                    ServerGiftTo(p, stack);
                    p.NotifyPublic($"FUN RANDOM: you got {Cfg.ItemName(stack.Id)}{(stack.Count > 1 ? " x" + stack.Count : "")}!");
                }
            }
        }

        void ServerGiftTo(PlayerNet p, ItemStack stack)
        {
            int left = p.ServerGive(stack.Id, stack.Count, stack.Data);
            // no room: it lands at their feet
            if (left > 0) ServerDropItem(ItemStack.Of(stack.Id, left, stack.Data), p.transform.position + p.transform.forward, p.transform.forward, p.EyePos);
        }

        static readonly Vector3[] k_EncloseDirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up };

        /// <summary>
        /// Builder's win check: from the ball, look out in the four flat directions and up. If walls/floors/doors of one
        /// team block at least 4 of those 5 ways (within 14 m), the ball counts as inside that team's structure.
        /// </summary>
        public int BallEnclosure()
        {
            var b = Ball.Instance;
            if (b == null || !b.IsSpawned) return -1;
            var from = b.transform.position + Vector3.up * 0.3f;
            var counts = new Dictionary<int, int>();
            foreach (var d in k_EncloseDirs)
            {
                var hits = Physics.RaycastAll(from, d, 14f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                foreach (var h in hits)
                {
                    if (h.collider.GetComponentInParent<Ball>() != null || h.collider.GetComponentInParent<PlayerNet>() != null) continue;
                    var s = h.collider.GetComponentInParent<Structure>();
                    if (s != null && s.IsSpawned)
                    {
                        counts.TryGetValue(s.Team.Value, out int c);
                        counts[s.Team.Value] = c + 1;
                    }
                    break; // the first solid thing decides this direction
                }
            }
            foreach (var kv in counts) if (kv.Value >= 4) return kv.Key;
            return -1;
        }
    }
}
