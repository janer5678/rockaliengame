using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot's economy (BotBrain.cs): farming trees (with the X weak spot when it's good at it), picking and eating
    /// berries, crafting its gear in order (hatchet, spear, trade station, armour, sword - fetching wood from its chest
    /// when it's short), putting down its chest and trade station, building walls round its machine during the build
    /// phase, and taking its spare wood to the chest. All through the players' own server calls (MeleeRpc, PickBerriesRpc,
    /// EatRpc, CraftRpc, PlaceDeployableRpc, PlaceRpc, MoveItemRpc), so it pays the same and follows the same rules.
    /// </summary>
    public partial class BotBrain
    {
        ResourceNode m_Node, m_Bush;
        Collider m_NodeCol;
        Container m_Chest;
        int m_Withdraw;
        Item m_Want, m_Deploy;
        int m_WantIdx = -1;
        Vector3 m_DeployAt;
        float m_DeployYaw, m_DeployPickedAt = -100f;
        bool m_DeployOk;
        float m_NextSwing, m_NextUse, m_EatAt = -1f;
        /// <summary>Things it gave up on for a while (a tree it couldn't get to, a bush, a craft that didn't go through).</summary>
        readonly Dictionary<Object, float> m_Avoid = new Dictionary<Object, float>();
        readonly Dictionary<Item, float> m_CraftFail = new Dictionary<Item, float>();
        readonly Dictionary<Item, float> m_PutFail = new Dictionary<Item, float>(); // (things it couldn't find a spot for)
        readonly Dictionary<PieceKey, float> m_WallFail = new Dictionary<PieceKey, float>();

        /// <summary>(tests) trees hit, pieces built, things crafted.</summary>
        public int FarmHits { get; private set; }
        public int Built { get; private set; }
        public int Crafted { get; private set; }

        enum Use : byte { Fight, Wood, Smash }

        // ---------------------------------------------------------------- the job's actions, every frame

        void Tick(NetGame g, float dt)
        {
            TickEat();
            switch (m_Task)
            {
                case Task.Wander:
                    if (Flat(m_Goal, transform.position) < 1.6f) StartWander(4f);
                    break;
                case Task.Farm: TickFarm(); break;
                case Task.Berries: TickBerries(); break;
                case Task.Store: TickStore(); break;
                case Task.Craft: TickCraft(); break;
                case Task.Deploy: TickDeploy(); break;
                case Task.Build: TickBuild(); break;
                case Task.Fetch: TickBall(false); break;
                case Task.Raid: if (m_RaidMachine) TickMachineRaid(g); else TickBall(true); break;
                case Task.Carry: TickCarry(); break;
                case Task.Escort: TickEscort(); break;
                case Task.Defend: TickDefend(); break;
                case Task.Hunt: TickHunt(); break;
                case Task.Fight: TickFight(); break;
                case Task.Retreat: TickRetreat(); break;
            }
        }

        // ---------------------------------------------------------------- eating

        /// <summary>Hurt, with berries and nobody right on it: eat one (it takes a moment, like a player's - Action = Eat).</summary>
        void TickEat()
        {
            if (m_EatAt >= 0f)
            {
                if (m_P.CarryingBall || m_P.Count(Item.Berry) == 0) { m_EatAt = -1f; SetAction(BodyAnimator.Act.None); return; }
                if (Time.time < m_EatAt) return;
                m_EatAt = -1f;
                int s = HotbarFor(Item.Berry);
                if (s >= 0) { Hold(s); m_P.EatRpc(); }
                SetAction(BodyAnimator.Act.None);
                return;
            }
            if (m_P.Health.Value > Cfg.MaxHealth - Cfg.BerryHeal * 0.6f || m_P.Count(Item.Berry) == 0 || m_P.CarryingBall) return;
            if (m_Target != null && Flat(m_Target.transform.position, transform.position) < 7f) return;
            if (Time.time < m_SlideUntil) return;
            int slot = HotbarFor(Item.Berry);
            if (slot < 0) return;
            Hold(slot);
            SetAction(BodyAnimator.Act.Eat);
            m_EatAt = Time.time + Cfg.BerryEatTime;
        }

        bool Eating => m_EatAt >= 0f;

        // ---------------------------------------------------------------- farming

        bool FarmJob()
        {
            if (InvOps.Space(m_P.Inv, Cfg.GatherItem(Item.Wood)) < 20) return false; // full up (the chest's next)
            if (m_Node == null || !m_Node.IsSpawned || m_Node.Amount.Value <= 0)
            {
                m_Node = PickNode();
                m_NodeCol = null;
            }
            if (m_Node == null) return false;
            Set(Task.Farm);
            return true;
        }

        /// <summary>A tree or fallen log to farm: near it and not far from home, on its side of the glass while that's up,
        /// never in an enemy base.</summary>
        ResourceNode PickNode()
        {
            ResourceNode best = null;
            float bs = float.MaxValue;
            var pos = transform.position;
            int region = Cfg.RegionOf(pos);
            bool glass = MapBuilder.GlassUp;
            var home = Cfg.BaseCenter[MyTeam];
            float lim = Cfg.MapHalf - 10f;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || !n.IsSpawned || !n.IsWood || n.Amount.Value <= 0 || Avoided(n)) continue;
                var np = n.transform.position;
                if (Mathf.Abs(np.x) > lim || Mathf.Abs(np.z) > lim) continue;
                if (glass && Cfg.RegionOf(np) != region) continue;
                int bt = Cfg.BaseTeamAt(np);
                if (bt >= 0 && bt != MyTeam) continue;
                float s = Flat(np, pos) + Flat(np, home) * 0.25f + (OtherBotOn(n) ? 10f : 0f);
                if (s < bs) { bs = s; best = n; }
            }
            return best;
        }

        /// <summary>A teammate bot is already chopping this one (spread out a bit).</summary>
        bool OtherBotOn(ResourceNode n)
        {
            foreach (var p in PlayerNet.All)
                if (p != null && p != m_P && p.Bot.Value && p.Team.Value == MyTeam && p.TryGetComponent(out BotBrain b) && b.m_Node == n) return true;
            return false;
        }

        void TickFarm()
        {
            var n = m_Node;
            if (n == null || !n.IsSpawned || n.Amount.Value <= 0) { m_Node = null; m_NextThink = 0f; return; }
            var eye = m_P.EyePos;
            var near = NodeBounds(n).ClosestPoint(eye);
            float far = Flat(n.transform.position, transform.position);
            GoTo(n.transform.position, 0.3f, far > 12f);
            if (Flat(near, transform.position) > ReachTree) return;
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = near;
            if (Eating || Time.time < m_NextSwing) return;
            HoldBest(Use.Wood);
            var item = m_P.HeldItem;
            if (!Cfg.IsMelee(item)) return;
            var st = Cfg.Melee(item);
            m_NextSwing = Time.time + st.Cooldown + Random.Range(0.05f, 0.35f);
            // the X: a good bot goes for it most of the time (twice the wood), a poor one now and then
            var point = near;
            bool weak = false;
            if (n.TryGetSpot(out var spot, out _) && Vector3.Distance(eye, spot) <= st.Range + 1.5f && Random.value < 0.2f + 0.65f * m_Skill)
            {
                point = spot;
                weak = true;
            }
            m_LookAt = point;
            m_P.BotMelee(n.NetworkObject, point, weak);
            FarmHits++;
        }

        /// <summary>The trunk's bounds (its smallest solid collider - not the crown), or a rough box.</summary>
        Bounds NodeBounds(ResourceNode n)
        {
            if (m_NodeCol == null || !m_NodeCol.transform.IsChildOf(n.transform))
            {
                m_NodeCol = null;
                n.GetComponentsInChildren(s_Cols);
                float small = float.MaxValue;
                foreach (var c in s_Cols)
                {
                    if (c == null || c.isTrigger || !c.enabled) continue;
                    var sz = c.bounds.size;
                    float a = sz.x * sz.z;
                    if (a < small) { small = a; m_NodeCol = c; }
                }
                s_Cols.Clear();
            }
            return m_NodeCol != null ? m_NodeCol.bounds : new Bounds(n.transform.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
        }

        // ---------------------------------------------------------------- berries

        bool FindBush()
        {
            if (m_Bush != null && m_Bush.IsSpawned) return true;
            m_Bush = null;
            float bd = 40f;
            var pos = transform.position;
            int region = Cfg.RegionOf(pos);
            foreach (var n in ResourceNode.All)
            {
                if (n == null || !n.IsSpawned || !n.IsBush || n.Amount.Value <= 0 || Avoided(n)) continue;
                var np = n.transform.position;
                if (MapBuilder.GlassUp && Cfg.RegionOf(np) != region) continue;
                int bt = Cfg.BaseTeamAt(np);
                if (bt >= 0 && bt != MyTeam) continue;
                float d = Flat(np, pos);
                if (d < bd) { bd = d; m_Bush = n; }
            }
            return m_Bush != null;
        }

        void TickBerries()
        {
            var b = m_Bush;
            if (b == null || !b.IsSpawned || b.Amount.Value <= 0) { m_Bush = null; m_NextThink = 0f; return; }
            GoTo(b.transform.position, 1.2f, Flat(b.transform.position, transform.position) > 8f);
            if (Flat(b.transform.position, transform.position) > 2f || Time.time < m_NextUse) return;
            m_Arrived = true;
            m_NextUse = Time.time + 0.5f;
            m_P.PickBerriesRpc(b.NetworkObject);
            m_Bush = null;
            m_NextThink = Time.time + 0.2f;
        }

        // ---------------------------------------------------------------- gear

        static readonly Item[] k_RaiderGear = { Item.Spear, Item.Hatchet, Item.Workbench, Item.Armor, Item.Sword };
        static readonly Item[] k_Gear = { Item.Hatchet, Item.Spear, Item.Workbench, Item.Armor, Item.Sword };

        /// <summary>A gathering tool that beats the rock for wood.</summary>
        bool HasAxe => Has(Item.Hatchet) || Has(Item.Chainsaw) || Has(Item.TreeCracker);
        /// <summary>A real weapon.</summary>
        bool HasWeapon => Has(Item.Spear) || Has(Item.Sword);

        bool Has(Item id)
        {
            var inv = m_P.Inv;
            for (int i = 0; i < inv.Count; i++) if (inv[i].Id == id) return true;
            return false;
        }

        /// <summary>Already got this (or something as good, or no need for it).</summary>
        bool Got(Item id)
        {
            switch (id)
            {
                case Item.Hatchet: return HasAxe;
                case Item.Spear: return HasWeapon;
                case Item.Armor: return m_P.ArmorHp.Value > 0;
                case Item.Workbench: return Has(Item.Workbench) || Workbench.ForTeam(MyTeam, 1) != null;
                default: return Has(id);
            }
        }

        /// <summary>The next thing on its gear list it could make in this mode (whether or not it can afford it yet).
        /// basic: just the starter tools.</summary>
        bool NextWant(bool basic, out Item item, out int idx, out Recipe r)
        {
            item = Item.None; idx = -1; r = default;
            var list = m_Bent == Bent.Raider ? k_RaiderGear : k_Gear;
            int team = MyTeam;
            foreach (var id in list)
            {
                if (basic && id != Item.Hatchet && id != Item.Spear) continue;
                if (Got(id)) continue;
                if (m_CraftFail.TryGetValue(id, out var until) && Time.time < until) continue;
                int i = Cfg.CraftIndexOf(id);
                if (!Cfg.ValidCraftIndex(i)) continue;
                var rec = Cfg.CraftRecipe(i, team);
                if (rec.Output != id) continue;
                if (Cfg.CraftTier(id) > Cfg.BenchTier(team)) continue;
                if (id == Item.Workbench && !Cfg.BenchUnlocked(team)) continue;
                if (!Tutorial.AllowsItemFor(m_P, id)) continue;
                item = id; idx = i; r = rec;
                return true;
            }
            return false;
        }

        bool CraftJob(bool basic, bool homeSide)
        {
            if (!NextWant(basic, out var item, out int idx, out var r)) return false;
            bool anywhere = Cfg.CanCraftAt(MyTeam, transform.position, item);
            if (!anywhere && !homeSide) return false;
            var cur = Cfg.CurrencyItem;
            // keep enough for the walls if it's the builder and they're not up yet
            int reserve = !basic && IsBuilder() && NextWall(out _) ? Cfg.PieceWood(PieceType.Wall) * 3 : 0;
            if (m_P.CanAfford(r) && m_P.Count(cur) - r.Wood >= reserve)
            {
                m_Want = item;
                m_WantIdx = idx;
                Set(Task.Craft);
                return true;
            }
            // short: is the rest in the chest at home?
            if (!homeSide || r.Stone > 0) return false;
            var c = TeamChest();
            if (c == null) return false;
            int need = r.Wood + reserve - m_P.Count(cur);
            if (need <= 0 || InvOps.Count(c.Slots, cur) < need) return false;
            m_Chest = c;
            m_Withdraw = need + 5;
            Set(Task.Store);
            return true;
        }

        void TickCraft()
        {
            if (!Cfg.ValidCraftIndex(m_WantIdx)) { Done(); return; }
            if (!Cfg.CanCraftAt(MyTeam, transform.position, m_Want))
            {
                var home = Cfg.BaseCenter[MyTeam] - Cfg.BackDir(MyTeam) * 4f;
                GoTo(home, 2f, Flat(home, transform.position) > 10f);
                return;
            }
            if (Time.time < m_NextUse) return;
            m_NextUse = Time.time + 0.6f;
            var item = m_Want;
            int before = CountItem(item);
            int armour = m_P.ArmorHp.Value;
            m_P.CraftRpc(m_WantIdx);
            if (CountItem(item) > before || m_P.ArmorHp.Value != armour) Crafted++;
            else m_CraftFail[item] = Time.time + 30f; // (didn't go through - not again for a while; Builder: it's queued)
            Done();
        }

        int CountItem(Item id)
        {
            int n = 0;
            var inv = m_P.Inv;
            for (int i = 0; i < inv.Count; i++) if (inv[i].Id == id) n += inv[i].Count;
            return n;
        }

        // ---------------------------------------------------------------- chests and trade stations

        bool DeployJob()
        {
            if (Has(Item.Workbench) && Workbench.ForTeam(MyTeam, 1) == null && !Avoided(Item.Workbench)) { StartDeploy(Item.Workbench); return true; }
            if (Has(Item.Chest) && TeamChest() == null && !Avoided(Item.Chest)) { StartDeploy(Item.Chest); return true; }
            return false;
        }

        void StartDeploy(Item kind)
        {
            if (m_Task != Task.Deploy || m_Deploy != kind) m_DeployPickedAt = -100f;
            m_Deploy = kind;
            Set(Task.Deploy);
        }

        void TickDeploy()
        {
            var kind = m_Deploy;
            int team = MyTeam;
            if (!Has(kind)) { Done(); return; }
            if (Time.time - m_DeployPickedAt > 6f)
            {
                m_DeployPickedAt = Time.time;
                m_DeployOk = PickDeploySpot(kind, team, out m_DeployAt, out m_DeployYaw);
                if (!m_DeployOk) { Avoid(kind, 60f); Done(); return; }
            }
            GoTo(m_DeployAt, 1.5f, Flat(m_DeployAt, transform.position) > 10f);
            if (Flat(m_DeployAt, transform.position) > 3.5f) return;
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = m_DeployAt;
            if (Time.time < m_NextUse) return;
            m_NextUse = Time.time + 0.7f;
            int slot = HotbarFor(kind);
            if (slot < 0) return;
            Hold(slot);
            int before = CountItem(kind);
            m_P.PlaceDeployableRpc((byte)kind, m_DeployAt, m_DeployYaw);
            if (CountItem(kind) < before) { Done(); return; }
            m_DeployPickedAt = -100f; // (somewhere else next time)
            if (Random.value < 0.35f) { Avoid(kind, 45f); Done(); }
        }

        /// <summary>Where to put a chest (by the machine, on the bedrock or just off it) or the trade station (its usual corner).</summary>
        bool PickDeploySpot(Item kind, int team, out Vector3 at, out float yaw)
        {
            var back = Cfg.BackDir(team);
            var left = -Vector3.Cross(Vector3.up, back);
            var c = Cfg.BaseCenter[team];
            yaw = Workbench.DefaultYaw(team);
            at = default;
            for (int k = 0; k < 6; k++)
            {
                Vector3 p;
                if (kind == Item.Workbench) p = k == 0 ? Workbench.DefaultPos(team, 1) : c - back * Random.Range(3.5f, 8f) + left * Random.Range(-7f, 7f);
                else
                {
                    float side = (k % 2 == 0 ? m_SideSign : -m_SideSign);
                    p = k < 2 ? c - back * 0.4f + left * 2.2f * side
                      : k < 4 ? c + left * 4.6f * side
                      : c - back * Random.Range(4.5f, 9f) + left * Random.Range(-6f, 6f);
                }
                p.y = Cfg.BaseY + 3f;
                if (Physics.Raycast(p, Vector3.down, out var hit, 8f, Mask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                else p.y = MapBuilder.Height(p.x, p.z);
                if (!PlayerNet.FindDeploySpot(kind, team, ref p, yaw, out _)) continue;
                at = p;
                return true;
            }
            return false;
        }

        /// <summary>Its team's chest in its base (the nearest with room).</summary>
        Container TeamChest()
        {
            Container best = null;
            float bd = float.MaxValue;
            var cur = Cfg.CurrencyItem;
            foreach (var c in Container.All)
            {
                if (c == null || !c.IsSpawned || c.Kind.Value != Container.Chest || c.Team.Value != MyTeam) continue;
                if (Cfg.BaseTeamAt(c.transform.position) != MyTeam && !Cfg.Builder) continue;
                float d = Flat(c.transform.position, transform.position) + (InvOps.Space(c.Slots, cur) < 50 ? 100f : 0f);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ---------------------------------------------------------------- storing

        bool StoreJob()
        {
            var cur = Cfg.CurrencyItem;
            bool full = InvOps.Space(m_P.Inv, Cfg.GatherItem(Item.Wood)) < 30;
            int keep = KeepOnHand();
            if (m_P.Count(cur) < Mathf.Max(m_StoreAt, keep + 150) && !full) return false;
            var c = TeamChest();
            if (c == null)
            {
                // no chest yet: make one (DeployJob puts it down)
                if (Has(Item.Chest) || Avoided(Item.Chest)) return false;
                int idx = Cfg.CraftIndexOf(Item.Chest);
                if (!Cfg.ValidCraftIndex(idx) || !m_P.CanAfford(Cfg.CraftRecipe(idx, MyTeam)) || !Tutorial.AllowsItemFor(m_P, Item.Chest)) return false;
                if (m_CraftFail.TryGetValue(Item.Chest, out var until) && Time.time < until) return false;
                m_Want = Item.Chest;
                m_WantIdx = idx;
                Set(Task.Craft);
                return true;
            }
            if (InvOps.Space(c.Slots, cur) < 50) return false;
            m_Chest = c;
            m_Withdraw = 0;
            Set(Task.Store);
            return true;
        }

        void TickStore()
        {
            var c = m_Chest;
            if (c == null || !c.IsSpawned) { m_Chest = null; Done(); return; }
            var cp = c.transform.position;
            GoTo(cp, 1.3f, Flat(cp, transform.position) > 10f);
            if (Flat(cp, transform.position) > 2.4f || !c.InReach(m_P.EyePos)) return;
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = c.Center;
            if (Time.time < m_NextUse) return;
            m_NextUse = Time.time + 0.35f;
            var cur = Cfg.CurrencyItem;
            var inv = m_P.Inv;
            if (m_Withdraw > 0)
            {
                // take what it's short of for its next bit of gear
                int si = -1;
                for (int i = 0; i < c.Slots.Count; i++) if (c.Slots[i].Id == cur && c.Slots[i].Count > 0) { si = i; break; }
                int di = SlotFor(inv, cur, Cfg.HotbarSize);
                if (si < 0 || di < 0) { m_Withdraw = 0; Done(); return; }
                int room = inv[di].Empty ? Cfg.MaxStack(cur) : Cfg.MaxStack(cur) - inv[di].Count;
                int amt = Mathf.Min(m_Withdraw, c.Slots[si].Count, room);
                int had = m_P.Count(cur);
                m_P.MoveItemRpc(1, (byte)si, 0, (byte)di, (ushort)Mathf.Max(1, amt), c.NetworkObject);
                m_Withdraw -= Mathf.Max(1, m_P.Count(cur) - had);
                if (m_P.Count(cur) == had || m_Withdraw <= 0) { m_Withdraw = 0; Done(); }
                return;
            }
            // put away all but what it keeps on it
            int extra = m_P.Count(cur) - KeepOnHand();
            int from = -1;
            for (int i = inv.Count - 1; i >= 0; i--) if (inv[i].Id == cur && inv[i].Count > 0) { from = i; break; }
            int to = SlotFor(c.Slots, cur, 0);
            if (extra <= 0 || from < 0 || to < 0) { Done(); return; }
            int space = c.Slots[to].Empty ? Cfg.MaxStack(cur) : Cfg.MaxStack(cur) - c.Slots[to].Count;
            int n = Mathf.Min(extra, inv[from].Count, space);
            int before = m_P.Count(cur);
            m_P.MoveItemRpc(0, (byte)from, 1, (byte)to, (ushort)Mathf.Max(1, n), c.NetworkObject);
            if (m_P.Count(cur) == before) Done(); // (nothing moved: leave it)
            else Stored += before - m_P.Count(cur);
        }

        /// <summary>How much wood it keeps on it: its usual float, or enough for the next bit of gear it's after (so it
        /// doesn't put wood away only to fetch it straight back out).</summary>
        int KeepOnHand()
        {
            int keep = Mathf.RoundToInt(m_Keep);
            if (NextWant(false, out _, out _, out var r) && r.Stone == 0) keep = Mathf.Max(keep, r.Wood);
            return keep;
        }

        /// <summary>(tests) how much it has put in chests.</summary>
        public int Stored { get; private set; }

        /// <summary>A slot in `list` this item can go in: a stack of it with room, or else an empty one (from `start` on).</summary>
        static int SlotFor(NetworkList<ItemStack> list, Item id, int start)
        {
            int max = Cfg.MaxStack(id);
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id && list[i].Count < max) return i;
            for (int i = start; i < list.Count; i++) if (list[i].Empty) return i;
            for (int i = 0; i < Mathf.Min(start, list.Count); i++) if (list[i].Empty) return i;
            return -1;
        }

        // ---------------------------------------------------------------- building

        /// <summary>Its team's builder: the first bot on the team.</summary>
        bool IsBuilder()
        {
            foreach (var p in PlayerNet.All)
                if (p != null && p.IsSpawned && p.Bot.Value && p.Team.Value == MyTeam && p.NetworkObjectId < m_P.NetworkObjectId) return false;
            return true;
        }

        static readonly List<PieceKey> s_Walls = new List<PieceKey>(8);
        static int s_WallsTeam = -1;
        static Vector3 s_WallsAt;

        /// <summary>
        /// The walls it puts up: round the back and sides of its bedrock (the machine's 6 x 6 m pad), standing on the
        /// bedrock's edges - the front stays open, so nobody's shut in. Back ones first.
        /// </summary>
        static List<PieceKey> WallPlan(int team)
        {
            if (s_WallsTeam == team && s_WallsAt == Cfg.BaseCenter[team] && s_Walls.Count > 0) return s_Walls; // (bases move with the map size)
            s_Walls.Clear();
            s_WallsTeam = team;
            s_WallsAt = Cfg.BaseCenter[team];
            int ci = Mathf.RoundToInt(Cfg.BaseCenter[team].x / Cfg.Cell), cj = Mathf.RoundToInt(Cfg.BaseCenter[team].z / Cfg.Cell);
            var back = Cfg.BackDir(team);
            for (int k = 0; k < 2; k++)
            {
                AddWall(team, back, new PieceKey(PieceKey.KEdge, ci - 2, cj - 1 + k, 0, 0)); // the bedrock's -x edge
                AddWall(team, back, new PieceKey(PieceKey.KEdge, ci, cj - 1 + k, 0, 0));     // +x
                AddWall(team, back, new PieceKey(PieceKey.KEdge, ci - 1 + k, cj - 2, 0, 1)); // -z
                AddWall(team, back, new PieceKey(PieceKey.KEdge, ci - 1 + k, cj, 0, 1));     // +z
            }
            s_Walls.Sort((a, b) => Facing(team, b).CompareTo(Facing(team, a)));
            return s_Walls;
        }

        /// <summary>How much a wall faces out the back of the base (1: right at the back, -1: the front).</summary>
        static float Facing(int team, PieceKey k)
        {
            BuildGrid.Pose(PieceType.Wall, k, out var p, out _);
            var d = p - Cfg.BaseCenter[team];
            d.y = 0f;
            return Vector3.Dot(d.normalized, Cfg.BackDir(team));
        }

        static void AddWall(int team, Vector3 back, PieceKey k)
        {
            if (Facing(team, k) < -0.7f) return; // (the front: left open)
            s_Walls.Add(k);
        }

        /// <summary>The next wall of the plan still to build (not there, not refused lately, not waiting after being broken).</summary>
        bool NextWall(out PieceKey key)
        {
            key = default;
            if (Cfg.Builder) return false;
            var plan = WallPlan(MyTeam);
            for (int i = 0; i < plan.Count; i++)
            {
                var k = plan[i];
                if (BuildGrid.Registry.ContainsKey(k)) continue;
                if (m_WallFail.TryGetValue(k, out var until) && Time.time < until) continue;
                if (Structure.RebuildWait(k) > 0f || !BuildGrid.CanBuildAt(MyTeam, k)) continue;
                key = k;
                return true;
            }
            return false;
        }

        bool BuildJob(NetGame g)
        {
            if (g.S == GameState.SuddenDeath || Cfg.Builder || !IsBuilder() || !Tutorial.AllowsFor(m_P, TutFeature.Build)) return false;
            if (!NextWall(out _)) return false;
            if (!HasAxe && g.S == GameState.PreBall && TimeLeft(g) > 120f) return false; // (a hatchet first: the wood comes three times as fast)
            bool plan = Has(Item.BuildingPlan);
            int need = Cfg.PieceWood(PieceType.Wall) + (plan ? 0 : Cfg.PlanWood);
            if (m_P.Count(Cfg.CurrencyItem) < need + 5) return false;
            if (!plan)
            {
                int idx = Cfg.CraftIndexOf(Item.BuildingPlan);
                if (!Cfg.ValidCraftIndex(idx) || (m_CraftFail.TryGetValue(Item.BuildingPlan, out var until) && Time.time < until)) return false;
                m_Want = Item.BuildingPlan;
                m_WantIdx = idx;
                Set(Task.Craft);
                return true;
            }
            Set(Task.Build);
            return true;
        }

        void TickBuild()
        {
            int team = MyTeam;
            var stand = Cfg.BaseCenter[team] - Cfg.BackDir(team) * 4.5f + Vector3.Cross(Vector3.up, Cfg.BackDir(team)) * m_SideSign;
            GoTo(stand, 1.2f, Flat(stand, transform.position) > 10f);
            if (Flat(stand, transform.position) > 3f) return;
            m_Arrived = true;
            if (Time.time < m_NextUse) return;
            m_NextUse = Time.time + Random.Range(0.45f, 0.9f);
            if (!NextWall(out var key) || m_P.Count(Cfg.CurrencyItem) < Cfg.PieceWood(PieceType.Wall)) { Done(); return; }
            int slot = HotbarFor(Item.BuildingPlan);
            if (slot < 0) { Done(); return; }
            Hold(slot);
            BuildGrid.Pose(PieceType.Wall, key, out var wp, out _);
            m_HasLook = true;
            m_LookAt = wp + Vector3.up * 1.4f;
            m_P.PlaceRpc((byte)PieceType.Wall, key.I, key.J, key.L, key.D);
            if (BuildGrid.Registry.ContainsKey(key)) Built++;
            else m_WallFail[key] = Time.time + 45f; // (blocked - the machine, a chest, a player's piece: skip it a while)
        }

        // ---------------------------------------------------------------- the hand

        /// <summary>Hold this hotbar slot.</summary>
        void Hold(int slot)
        {
            if (slot >= 0 && slot < Cfg.HotbarSize && m_P.HeldSlot.Value != slot) m_P.HeldSlot.Value = (byte)slot;
        }

        /// <summary>The hotbar slot with this item in it - dragged up from the bag first if need be (-1: none).</summary>
        int HotbarFor(Item id)
        {
            int s = m_P.HotbarSlotOf(id);
            if (s >= 0) return s;
            int from = -1;
            for (int i = Cfg.HotbarSize; i < m_P.Inv.Count; i++) if (m_P.Inv[i].Id == id) { from = i; break; }
            if (from < 0) return -1;
            int to = -1;
            for (int i = 0; i < Cfg.HotbarSize && to < 0; i++) if (m_P.SlotAt(i).Empty) to = i;
            for (int i = Cfg.HotbarSize - 1; i >= 0 && to < 0; i--) if (Cfg.IsMat(m_P.SlotAt(i).Id)) to = i; // (swap a material down)
            if (to < 0) to = Cfg.HotbarSize - 1;
            m_P.MoveItemRpc(0, (byte)from, 0, (byte)to, (ushort)m_P.Inv[from].Count, default);
            return m_P.HotbarSlotOf(id);
        }

        /// <summary>The rock: an empty hotbar slot (one's made by moving a material down to the bag if need be).</summary>
        int RockSlot(bool make)
        {
            for (int i = 0; i < Cfg.HotbarSize; i++) if (m_P.SlotAt(i).Empty) return i;
            if (!make) return -1;
            int bag = -1;
            for (int i = Cfg.HotbarSize; i < m_P.Inv.Count && bag < 0; i++) if (m_P.Inv[i].Empty) bag = i;
            if (bag < 0) return -1;
            for (int i = Cfg.HotbarSize - 1; i >= 0; i--)
                if (Cfg.IsMat(m_P.SlotAt(i).Id) || m_P.SlotAt(i).Id == Item.Berry)
                {
                    m_P.MoveItemRpc(0, (byte)i, 0, (byte)bag, (ushort)m_P.SlotAt(i).Count, default);
                    return m_P.SlotAt(i).Empty ? i : -1;
                }
            return -1;
        }

        /// <summary>How good a melee item is for this (damage / wood / damage to walls, per second).</summary>
        static float Rate(Item it, Use u)
        {
            var st = Cfg.Melee(it);
            if (st.Cooldown <= 0f) return -1f;
            float v = u == Use.Fight ? Cfg.MeleePlayerDamage(it, false) : u == Use.Wood ? st.WoodGather : st.StructureDamage;
            return v / st.Cooldown;
        }

        /// <summary>Hold the best thing it has on its hotbar for this (the rock if nothing beats it).</summary>
        void HoldBest(Use u)
        {
            if (Eating) return;
            int best = -1;
            int rock = RockSlot(false);
            float bv = rock >= 0 ? Rate(Item.Rock, u) : -1f;
            for (int i = 0; i < Cfg.HotbarSize; i++)
            {
                var it = m_P.SlotAt(i).Id;
                if (!Cfg.IsMelee(it)) continue;
                float v = Rate(it, u);
                if (v > bv) { bv = v; best = i; }
            }
            if (best >= 0) { Hold(best); return; }
            if (rock < 0) rock = RockSlot(true);
            if (rock >= 0) Hold(rock);
        }

        // ---------------------------------------------------------------- odds and ends

        void Done()
        {
            m_Task = Task.Idle;
            m_NextThink = Mathf.Min(m_NextThink, Time.time + 0.15f);
        }

        bool Avoided(Object o) => o != null && m_Avoid.TryGetValue(o, out var until) && Time.time < until;
        bool Avoided(Item id) => m_PutFail.TryGetValue(id, out var until) && Time.time < until;
        void Avoid(Object o, float seconds) { if (o != null) m_Avoid[o] = Time.time + seconds; }
        void Avoid(Item id, float seconds) => m_PutFail[id] = Time.time + seconds;

        /// <summary>Stuck on the way for a good few seconds: give up on that (a tree, a bush, a spot) for a while.</summary>
        void GaveUp()
        {
            switch (m_Task)
            {
                case Task.Farm: Avoid(m_Node, 40f); m_Node = null; break;
                case Task.Berries: Avoid(m_Bush, 40f); m_Bush = null; break;
                case Task.Deploy: m_DeployPickedAt = -100f; return;
                case Task.Fight: case Task.Raid: case Task.Fetch: case Task.Carry: case Task.Retreat: return; // (it keeps at those - breaking through, sidestepping)
            }
            StartWander(3f);
        }
    }
}
