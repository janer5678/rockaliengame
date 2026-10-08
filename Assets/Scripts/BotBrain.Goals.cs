using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// What a bot's team is playing for (BotBrain.cs), like a person sizing up the other side: it compares its team with
    /// the best enemy team on four things -
    ///   Base:      how built up its base is (house pieces, large walls and gates, turrets, sleeping bags, chests);
    ///   Weapons:   the best weapon (and armour) it carries against the best an enemy carries;
    ///   Crafting:  how much of the crafting list its team has unlocked (Trade Station T1 / T2);
    ///   Upgrades:  how many base upgrades its team has bought at its upgrade station (Fortify All Walls, the wood gen).
    /// Whatever it's furthest behind on (nudged by its leaning - builders like the base, raiders their weapons) it works
    /// on: the next piece of the house, the next weapon, the next Trade Station, the next upgrade. That costs wood (or
    /// DNA), so it farms for it - and keeps farming past that, stashing the spare in the team chest, which it draws on for
    /// the big buys (a 2000-wood Trade Station T2, 1000-3000-wood upgrades). Small things (a wall, a doorway) come out of
    /// what it carries; it saves up for the big ones before spending on the lesser goals.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>What a bot is working towards.</summary>
        public enum Goal : byte { None, Base, Weapons, Crafting, Upgrades }

        enum StepKind : byte { Craft, Piece, Upgrade, Arm }

        struct Step
        {
            public Goal Goal;
            public StepKind Kind;
            public int Cost, Idx;
            public Item Item;
            public float Score;
        }

        Goal m_Focus;
        /// <summary>What it's working towards right now (the goal it's furthest behind on).</summary>
        public Goal Focus => m_Focus;
        readonly List<Step> m_Steps = new List<Step>(4);
        int m_HoldFor; // (wood it's keeping on it for the step it's about to do, until m_HoldUntil)
        float m_HoldUntil;
        Item m_UpgradeId;

        /// <summary>Anything costing this much or less is just bought out of its pocket (walls, doorways, a spear).</summary>
        const int CheapCost = 60;

        // ---------------------------------------------------------------- the scores (cached for everyone a second at a time)

        static float s_ScoredAt = -10f;
        static readonly float[] s_Base = new float[4], s_Weapons = new float[4], s_Craft = new float[4], s_Upgrades = new float[4];
        static readonly bool[] s_Playing = new bool[4];
        static readonly List<Item> s_UpgradeList = new List<Item>(2);

        static void Score()
        {
            if (Time.time - s_ScoredAt < 1f) return;
            s_ScoredAt = Time.time;
            for (int t = 0; t < 4; t++) { s_Base[t] = s_Weapons[t] = s_Craft[t] = s_Upgrades[t] = 0f; s_Playing[t] = false; }
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || s.Team.Value >= 4) continue;
                float w;
                switch (s.PType)
                {
                    case PieceType.Foundation: w = 0.5f; break;
                    case PieceType.Barrier: case PieceType.Gate: case PieceType.Tower: w = 1.5f; break;
                    case PieceType.EggBlock: w = 0.25f; break;
                    default: w = 1f; break;
                }
                s_Base[s.Team.Value] += w;
            }
            foreach (var c in Container.All)
            {
                if (c == null || !c.IsSpawned || c.Team.Value >= 4) continue;
                byte k = c.Kind.Value;
                s_Base[c.Team.Value] += k == Container.Turret ? 4f : k == Container.SleepBag ? 1f : k == Container.Trap || k == Container.Ladder ? 0.5f : k == Container.Chest ? 0.5f : 0f;
            }
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Team.Value >= 4) continue;
                int t = p.Team.Value;
                s_Playing[t] = true;
                s_Weapons[t] = Mathf.Max(s_Weapons[t], WeaponScore(p));
            }
            Cfg.BaseUpgrades(s_UpgradeList);
            for (int t = 0; t < Mathf.Min(4, Cfg.TeamCount); t++)
            {
                s_Craft[t] = Cfg.BenchTier(t);
                foreach (var id in s_UpgradeList) s_Upgrades[t] += Cfg.BaseUpgradeLevel(id, t);
            }
        }

        /// <summary>(tests, the HUD) a team's base score: its pieces, turrets, bags and chests.</summary>
        public static float BaseScore(int team) { Score(); return team >= 0 && team < 4 ? s_Base[team] : 0f; }

        /// <summary>How good a weapon is (0 the rock .. 3 the big guns) - for comparing who's better armed.</summary>
        static float ItemPower(Item i)
        {
            switch (i)
            {
                case Item.Hatchet: case Item.Pickaxe: return 0.5f;
                case Item.Spear: case Item.Bow: case Item.Chainsaw: case Item.TreeCracker: return 1f;
                case Item.Crossbow: return 1.5f;
                case Item.Sword: case Item.Pistol: return 2f;
                case Item.Shotgun: case Item.Revolver: case Item.Sniper: case Item.C4: return 2.5f;
                case Item.RocketLauncher: case Item.DeathWand: case Item.GiantStaff: return 3f;
                default: return 0f;
            }
        }

        /// <summary>A player's best weapon, plus their armour and helmet.</summary>
        static float WeaponScore(PlayerNet p)
        {
            if (p.Dead.Value) return 0f;
            float best = 0f;
            var inv = p.Inv;
            for (int i = 0; i < inv.Count; i++) best = Mathf.Max(best, ItemPower(inv[i].Id));
            return best + p.ArmorHp.Value / 50f + (p.HelmetHp.Value > 0 ? 0.25f : 0f);
        }

        /// <summary>The best of the other teams (that have anyone on them) at something.</summary>
        float EnemyBest(float[] score)
        {
            float best = 0f;
            for (int t = 0; t < Mathf.Min(4, Cfg.TeamCount); t++)
                if (t != MyTeam && s_Playing[t]) best = Mathf.Max(best, score[t]);
            return best;
        }

        // ---------------------------------------------------------------- the next step on each

        /// <summary>A craft it could do now (this mode has it, its bench is good enough, wood only), and what it costs.</summary>
        bool Craftable(Item id, out int idx, out int cost)
        {
            idx = Cfg.CraftIndexOf(id);
            cost = 0;
            if (!Cfg.ValidCraftIndex(idx)) return false;
            if (m_CraftFail.TryGetValue(id, out var until) && Time.time < until) return false;
            var r = Cfg.CraftRecipe(idx, MyTeam);
            if (r.Output != id || r.Stone > 0 || r.Dust > 0) return false; // (Jonah mode's alien dust: bots don't convert)
            if (Cfg.CraftTier(id) > Cfg.BenchTier(MyTeam)) return false;
            if (id == Item.Workbench && !Cfg.BenchUnlocked(MyTeam)) return false;
            if (!Tutorial.AllowsItemFor(m_P, id)) return false;
            cost = r.Wood;
            return true;
        }

        static readonly Item[] k_Arms = { Item.Sword, Item.Armor, Item.Chainsaw, Item.HeavyArmor };

        /// <summary>The next weapon / armour it wants: a sword, armour, a chainsaw (it chops fast too), heavy armour -
        /// and a raider going up against walls wants a ram.</summary>
        bool WeaponStep(out Step s)
        {
            s = new Step { Goal = Goal.Weapons, Kind = StepKind.Craft };
            if (m_Bent == Bent.Raider && !Has(Item.Ram) && EnemyBest(s_Base) >= 6f && Craftable(Item.Ram, out s.Idx, out s.Cost)) { s.Item = Item.Ram; return true; }
            foreach (var id in k_Arms)
            {
                if (Got(id)) continue;
                if (id == Item.HeavyArmor && m_P.ArmorHp.Value >= Cfg.HeavyArmorHp) continue;
                if (!Craftable(id, out s.Idx, out s.Cost)) continue;
                s.Item = id;
                return true;
            }
            return false;
        }

        /// <summary>The next Trade Station: the T1 (once the ball's been captured), then the T2.</summary>
        bool CraftingStep(out Step s)
        {
            s = new Step { Goal = Goal.Crafting, Kind = StepKind.Craft };
            int team = MyTeam;
            if (Has(Item.Workbench) || Has(Item.Workbench2)) return false; // (it's putting one down: DeployJob)
            if (Workbench.ForTeam(team, 1) == null) { s.Item = Item.Workbench; return Craftable(Item.Workbench, out s.Idx, out s.Cost); }
            if (Workbench.ForTeam(team, 2) == null) { s.Item = Item.Workbench2; return Craftable(Item.Workbench2, out s.Idx, out s.Cost); }
            return false;
        }

        /// <summary>The cheapest base upgrade not maxed out yet (the wood gen first on a tie - it pays for the rest).</summary>
        bool UpgradeStep(out Step s)
        {
            s = new Step { Goal = Goal.Upgrades, Kind = StepKind.Upgrade };
            if (!Tutorial.AllowsFor(m_P, TutFeature.Station)) return false;
            int team = MyTeam, best = int.MaxValue;
            Cfg.BaseUpgrades(s_UpgradeList);
            foreach (var id in s_UpgradeList)
            {
                if (Cfg.BaseUpgradeMaxed(id, team)) continue;
                if (id == Item.FortifyBuff && !Tutorial.AllowsFor(m_P, TutFeature.Fortify)) continue;
                if (m_CraftFail.TryGetValue(id, out var until) && Time.time < until) continue;
                var r = Cfg.BaseUpgradeRecipe(id, team);
                if (r.Stone > 0 || r.Wood >= best) continue;
                best = r.Wood;
                s.Item = id;
                s.Cost = r.Wood;
            }
            return best < int.MaxValue;
        }

        /// <summary>The base: the next piece of the house; between its stages, a sleeping bag (T1) and an auto turret (T2) -
        /// and a turret with nothing in it gets a bow and arrows.</summary>
        bool BaseStep(out Step s)
        {
            s = new Step { Goal = Goal.Base };
            int team = MyTeam;
            bool piece = NextPiece(out var pp);
            // the deployables come once the room is shut (stage 0 done) or there's nothing left to build
            bool roomDone = !piece || pp.Stage > 0;
            if (roomDone && !Has(Item.SleepingBag) && !TeamHasBox(Container.SleepBag) && Craftable(Item.SleepingBag, out s.Idx, out s.Cost))
            { s.Kind = StepKind.Craft; s.Item = Item.SleepingBag; return true; }
            if (roomDone && !Has(Item.AutoTurret) && !TeamHasBox(Container.Turret) && Craftable(Item.AutoTurret, out s.Idx, out s.Cost))
            { s.Kind = StepKind.Craft; s.Item = Item.AutoTurret; return true; }
            if (TurretNeeds(out var turret, out var item, out int idx, out int cost))
            {
                s.Kind = item == Item.None ? StepKind.Arm : StepKind.Craft;
                s.Item = item;
                s.Idx = idx;
                s.Cost = cost;
                m_Turret = turret;
                return true;
            }
            if (!piece) return false;
            s.Kind = StepKind.Piece;
            s.Cost = Cfg.PieceWood(pp.Type) + (Has(Item.BuildingPlan) ? 0 : Cfg.PlanWood);
            return true;
        }

        bool TeamHasBox(byte kind)
        {
            foreach (var c in Container.All)
                if (c != null && c.IsSpawned && c.Kind.Value == kind && c.Team.Value == MyTeam) return true;
            return false;
        }

        // ---------------------------------------------------------------- picking one and doing it

        /// <summary>
        /// The goals, most behind first: the first one it can pay for (big buys out of its pocket and the team chest,
        /// small ones out of its pocket) it starts on. Big buys it can't pay for yet are saved up for: the lesser goals
        /// only get what's left over. False: nothing to do now (it farms).
        /// </summary>
        bool GoalJob(NetGame g)
        {
            Score();
            m_Steps.Clear();
            int team = MyTeam;
            if (team < 0 || team >= 4) return false;
            bool build = g.S == GameState.PreBall;
            float bias(Goal gl)
            {
                switch (m_Bent)
                {
                    case Bent.Raider: return gl == Goal.Weapons ? 0.6f : gl == Goal.Crafting ? 0.3f : gl == Goal.Base ? (build ? 0.3f : 0f) : 0.1f;
                    case Bent.Defender: return gl == Goal.Base ? (build ? 0.9f : 0.6f) : gl == Goal.Weapons ? 0.3f : 0.2f;
                    default: return gl == Goal.Upgrades ? 0.5f : gl == Goal.Crafting ? 0.4f : gl == Goal.Base ? (build ? 0.6f : 0.3f) : 0f;
                }
            }
            float mineW = WeaponScore(m_P);
            if (BaseStep(out var s)) { s.Score = (EnemyBest(s_Base) - s_Base[team]) / 6f + bias(Goal.Base); m_Steps.Add(s); }
            if (WeaponStep(out s)) { s.Score = EnemyBest(s_Weapons) - mineW + bias(Goal.Weapons); m_Steps.Add(s); }
            if (CraftingStep(out s)) { s.Score = EnemyBest(s_Craft) - s_Craft[team] + bias(Goal.Crafting); m_Steps.Add(s); }
            if (UpgradeStep(out s)) { s.Score = EnemyBest(s_Upgrades) - s_Upgrades[team] + bias(Goal.Upgrades); m_Steps.Add(s); }
            m_Steps.Sort((a, b) => b.Score.CompareTo(a.Score));
            m_Focus = m_Steps.Count > 0 ? m_Steps[0].Goal : Goal.None;
            var cur = Cfg.CurrencyItem;
            int onHand = m_P.Count(cur);
            int wealth = onHand + ChestStock();
            int reserved = 0;
            foreach (var st in m_Steps)
            {
                bool cheap = st.Cost <= CheapCost;
                bool ok = cheap ? onHand >= st.Cost || wealth - reserved >= st.Cost + Mathf.RoundToInt(m_Keep) : wealth - reserved >= st.Cost;
                if (ok && StartStep(st, onHand)) return true;
                if (!cheap) reserved += st.Cost;
            }
            return false;
        }

        /// <summary>Go and do it - fetching the wood from the team chest first if it's short.</summary>
        bool StartStep(Step s, int onHand)
        {
            bool cheap = s.Cost <= CheapCost;
            m_HoldUntil = Time.time + 45f; // (not put straight back in the chest: KeepOnHand)
            if (onHand < s.Cost)
            {
                var c = RichestChest();
                if (c == null) return false;
                m_Chest = c;
                m_Withdraw = (cheap ? Mathf.Max(s.Cost, Mathf.RoundToInt(m_Keep)) : s.Cost) - onHand + 5;
                m_HoldFor = s.Cost;
                Set(Task.Store);
                return true;
            }
            m_HoldFor = s.Cost;
            switch (s.Kind)
            {
                case StepKind.Craft:
                    m_Want = s.Item;
                    m_WantIdx = s.Idx;
                    Set(Task.Craft);
                    return true;
                case StepKind.Piece:
                    if (!Has(Item.BuildingPlan))
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
                case StepKind.Upgrade:
                    m_UpgradeId = s.Item;
                    Set(Task.Upgrade);
                    return true;
                case StepKind.Arm:
                    Set(Task.Arm);
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- the upgrade station

        /// <summary>Over to its upgrade station (standing by it, outside the inner room) and buy the upgrade - BaseUpgradeRpc,
        /// the UPGRADE button's own call.</summary>
        void TickUpgrade()
        {
            int team = MyTeam;
            var id = m_UpgradeId;
            if (!Cfg.BaseUpgradeOn(id) || Cfg.BaseUpgradeMaxed(id, team)) { Done(); return; }
            var c = Cfg.BaseCenter[team];
            var back = Cfg.BackDir(team);
            var lat = Vector3.Cross(Vector3.up, back);
            var station = Cfg.UpgradeStationPos(team);
            float side = Vector3.Dot(station - c, lat) >= 0f ? 1f : -1f;
            var spot = c + back * Vector3.Dot(station - c, back) + lat * 4.4f * side; // (beside the room, level with the station)
            if (!Cfg.AtOwnStation(team, transform.position) || Flat(spot, transform.position) > 2.5f)
            {
                GoTo(spot, 0.8f, Flat(spot, transform.position) > 10f);
                if (!Cfg.AtOwnStation(team, transform.position)) return;
            }
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = station + Vector3.up * 1.2f;
            if (Time.time < m_NextUse) return;
            m_NextUse = Time.time + 0.8f;
            int before = Cfg.BaseUpgradeLevel(id, team);
            if (!m_P.CanAfford(Cfg.BaseUpgradeRecipe(id, team))) { Done(); return; }
            m_P.BaseUpgradeRpc(id);
            m_HoldFor = 0;
            if (Cfg.BaseUpgradeLevel(id, team) > before) Upgraded++;
            else m_CraftFail[id] = Time.time + 30f;
            Done();
        }

        /// <summary>(tests) base upgrades it has bought.</summary>
        public int Upgraded { get; private set; }

        // ---------------------------------------------------------------- the auto turret

        Container m_Turret;

        /// <summary>Its team's turret wants a weapon (a bow - it has one: Item None, go and put it in; else craft one) or
        /// arrows (the same).</summary>
        bool TurretNeeds(out Container turret, out Item craft, out int idx, out int cost)
        {
            turret = null; craft = Item.None; idx = -1; cost = 0;
            foreach (var c in Container.All)
                if (c != null && c.IsSpawned && c.Kind.Value == Container.Turret && c.Team.Value == MyTeam && c.Slots.Count >= 2) { turret = c; break; }
            if (turret == null || Avoided(turret)) return false;
            var w = turret.Slots[0];
            var ammo = turret.Slots[1];
            if (w.Empty)
            {
                if (Has(Item.Bow) || Has(Item.Crossbow)) return true;
                craft = Item.Bow;
                return Craftable(Item.Bow, out idx, out cost);
            }
            if (ammo.Empty || (ammo.Id == Item.Arrow && ammo.Count < 15)) // (a turret fires arrows from whatever weapon it has)
            {
                if (CountItem(Item.Arrow) >= 10) return true;
                craft = Item.Arrow;
                return Craftable(Item.Arrow, out idx, out cost);
            }
            return false;
        }

        /// <summary>At the turret: its bow in the weapon slot, its arrows in the ammo slot (shift-click from the bag, MoveItemRpc).</summary>
        void TickArm()
        {
            var t = m_Turret;
            if (t == null || !t.IsSpawned || t.Slots.Count < 2) { Done(); return; }
            var tp = t.transform.position;
            GoTo(tp, 1.3f, Flat(tp, transform.position) > 10f);
            if (Flat(tp, transform.position) > 2.4f || !t.InReach(m_P.EyePos)) return;
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = t.Center;
            if (Time.time < m_NextUse) return;
            m_NextUse = Time.time + 0.4f;
            var inv = m_P.Inv;
            int from = -1;
            bool needGun = t.Slots[0].Empty;
            for (int i = 0; i < inv.Count && from < 0; i++)
            {
                var id = inv[i].Id;
                if (needGun ? id == Item.Crossbow || id == Item.Bow : id == Item.Arrow) from = i;
            }
            if (from < 0) { Done(); return; }
            int before = TotalItems();
            m_P.MoveItemRpc(0, (byte)from, 1, 255, (ushort)inv[from].Count, t.NetworkObject);
            if (TotalItems() == before) { Avoid(t, 60f); Done(); }
        }

        int TotalItems()
        {
            int n = 0;
            var inv = m_P.Inv;
            for (int i = 0; i < inv.Count; i++) if (!inv[i].Empty) n += inv[i].Count;
            return n;
        }

        // ---------------------------------------------------------------- the team chest

        /// <summary>All the wood (DNA) in its team's chests at home.</summary>
        int ChestStock()
        {
            int n = 0;
            var cur = Cfg.CurrencyItem;
            foreach (var c in Container.All)
                if (IsHomeChest(c)) n += InvOps.Count(c.Slots, cur);
            return n;
        }

        Container RichestChest()
        {
            Container best = null;
            int bn = 0;
            var cur = Cfg.CurrencyItem;
            foreach (var c in Container.All)
            {
                if (!IsHomeChest(c)) continue;
                int n = InvOps.Count(c.Slots, cur);
                if (n > bn) { bn = n; best = c; }
            }
            return best;
        }

        bool IsHomeChest(Container c) =>
            c != null && c.IsSpawned && c.Kind.Value == Container.Chest && c.Team.Value == MyTeam && (Cfg.Builder || Cfg.BaseTeamAt(c.transform.position) == MyTeam);
    }
}
