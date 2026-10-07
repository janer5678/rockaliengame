using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot and floor loot (BotBrain.cs): anything lying on the ground near it (what a dead player dropped, a spear in
    /// the grass, the pile out of its wood machine) it walks over and picks up with E (PickupItemRpc); a death bag or an
    /// airdrop crate it goes and empties (MoveItemRpc shift-clicks, slot by slot) - an airdrop from a good way off. Never
    /// in an enemy base, and only on its own side while the glass wall's up. Armour, heavy armour or a helmet it finds
    /// goes straight on (UseItemRpc); an invisibility potion it drinks on a raid. Whatever else it can't use goes in the
    /// team chest with the wood (BotBrain.Economy.cs: TickStore).
    /// </summary>
    public partial class BotBrain
    {
        int m_LootId = -1;
        Container m_LootBox;
        float m_NextLootLook, m_NextGear;
        readonly HashSet<int> m_LootSkip = new HashSet<int>();

        /// <summary>(tests) things it has picked up off the floor or out of a bag / airdrop.</summary>
        public int Looted { get; private set; }

        /// <summary>Somewhere it may go for loot.</summary>
        bool LootOk(Vector3 p)
        {
            if (MapBuilder.GlassUp && Cfg.RegionOf(p) != Cfg.RegionOf(transform.position)) return false;
            int bt = Cfg.BaseTeamAt(p);
            if (bt >= 0 && bt != MyTeam && !Cfg.Builder) return false;
            float lim = Cfg.MapHalf - 6f;
            return Mathf.Abs(p.x) < lim && Mathf.Abs(p.z) < lim;
        }

        /// <summary>Loot worth going for: the best of what's lying about (near ones first, airdrops from far off, a big
        /// pile of wood more than a stick). False: nothing.</summary>
        bool LootJob()
        {
            var g = NetGame.Instance;
            if (g == null || m_P.CarryingBall) return false;
            if (m_Task == Task.Loot && (m_LootBox != null || m_LootId >= 0)) return true; // (still on its way)
            if (Time.time < m_NextLootLook) return false;
            m_NextLootLook = Time.time + 1f;
            var pos = transform.position;
            var cur = Cfg.CurrencyItem;
            float bs = float.MaxValue;
            int bestId = -1;
            Container bestBox = null;
            for (int i = 0; i < g.Items.Count; i++)
            {
                var it = g.Items[i];
                if (it.Stack.Empty || it.Stack.Id == Item.Rock || m_LootSkip.Contains(it.Id)) continue;
                float d = Flat(it.Pos, pos);
                bool home = Cfg.BaseTeamAt(it.Pos) == MyTeam;
                if (d > (home ? 45f : 28f) || !LootOk(it.Pos)) continue;
                // (a few sticks out of the wood machine: let the pile grow first, unless it's right there)
                if (it.Stack.Id == cur && it.Stack.Count < 40 && d > 6f) continue;
                if (InvOps.Space(m_P.Inv, it.Stack.Id, it.Stack.Data) <= 0) continue;
                float ground = MapBuilder.Height(it.Pos.x, it.Pos.z);
                if (it.Center.y - ground > 3.2f) continue; // (stuck up high in something)
                float s = d - (it.Stack.Id == cur ? Mathf.Min(it.Stack.Count, 600) / 60f : 2f);
                if (s < bs) { bs = s; bestId = it.Id; bestBox = null; }
            }
            bool room = InvOps.HasEmpty(m_P.Inv);
            foreach (var c in Container.All)
            {
                if (c == null || !c.IsSpawned || !c.TakeOnly || c.Empty || Avoided(c) || !room) continue;
                var cp = c.transform.position;
                float d = Flat(cp, pos);
                if (d > (c.IsAirdrop ? 95f : 30f) || !LootOk(cp)) continue;
                float s = d - (c.IsAirdrop ? 30f : 4f);
                if (s < bs) { bs = s; bestBox = c; bestId = -1; }
            }
            if (bestId < 0 && bestBox == null) return false;
            m_LootId = bestId;
            m_LootBox = bestBox;
            Set(Task.Loot);
            return true;
        }

        void TickLoot()
        {
            var g = NetGame.Instance;
            var pos = transform.position;
            if (m_LootBox != null)
            {
                var c = m_LootBox;
                if (!c.IsSpawned || c.Empty) { m_LootBox = null; Done(); return; }
                var cp = c.transform.position;
                GoTo(cp, 1.0f, Flat(cp, pos) > 8f);
                if (Flat(cp, pos) > 2.3f || !c.InReach(m_P.EyePos)) return;
                m_Arrived = true;
                m_HasLook = true;
                m_LookAt = c.Center;
                if (Time.time < m_NextUse) return;
                m_NextUse = Time.time + Random.Range(0.25f, 0.45f);
                int si = -1;
                for (int i = 0; i < c.Slots.Count && si < 0; i++) if (!c.Slots[i].Empty) si = i;
                if (si < 0) { m_LootBox = null; Done(); return; }
                int before = TotalItems();
                m_P.MoveItemRpc(1, (byte)si, 0, 255, (ushort)c.Slots[si].Count, c.NetworkObject);
                if (TotalItems() > before) Looted++;
                else { Avoid(c, 40f); m_LootBox = null; Done(); } // (no room for it)
                return;
            }
            if (g == null || m_LootId < 0) { Done(); return; }
            bool found = false;
            DroppedItem it = default;
            for (int i = 0; i < g.Items.Count; i++) if (g.Items[i].Id == m_LootId) { it = g.Items[i]; found = true; break; }
            if (!found) { m_LootId = -1; Done(); return; }
            GoTo(it.Pos, 0.35f, Flat(it.Pos, pos) > 8f);
            if (Flat(it.Pos, pos) > 1.7f || Vector3.Distance(it.Center, m_P.EyePos) > Cfg.InteractRange + 1.5f) return;
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = it.Center;
            if (Time.time < m_NextUse) return;
            m_NextUse = Time.time + 0.3f;
            int had = TotalItems();
            m_P.PickupItemRpc(m_LootId);
            if (TotalItems() > had) Looted++;
            else m_LootSkip.Add(m_LootId); // (couldn't take it: leave it be)
            m_LootId = -1;
            Done();
        }

        /// <summary>Gave up getting to it: leave that one.</summary>
        void LootGaveUp()
        {
            if (m_LootBox != null) Avoid(m_LootBox, 45f);
            if (m_LootId >= 0) m_LootSkip.Add(m_LootId);
            m_LootBox = null;
            m_LootId = -1;
        }

        // ---------------------------------------------------------------- putting things on

        /// <summary>Armour it's carrying goes on (heavy armour over light, a helmet if it has none on); an invisibility
        /// potion it drinks closing in on an enemy base. Not with an enemy right on it.</summary>
        void TickGear()
        {
            if (Time.time < m_NextGear || Eating || m_P.CarryingBall || Time.time < m_SlideUntil) return;
            m_NextGear = Time.time + 1f;
            if (m_Target != null && Flat(m_Target.transform.position, transform.position) < 6f) return;
            var use = Item.None;
            if (Has(Item.HeavyArmor) && m_P.ArmorHp.Value < Cfg.HeavyArmorHp) use = Item.HeavyArmor;
            else if (Has(Item.Armor) && m_P.ArmorHp.Value == 0) use = Item.Armor;
            else if (Has(Item.Helmet) && m_P.HelmetHp.Value == 0) use = Item.Helmet;
            else if (Has(Item.InvisPotion) && m_Task == Task.Raid && !m_P.Invisible && m_RaidTeam >= 0 && m_RaidTeam < Cfg.TeamCount
                     && Flat(Cfg.BaseCenter[m_RaidTeam], transform.position) < 45f) use = Item.InvisPotion;
            if (use == Item.None) return;
            int slot = HotbarFor(use);
            if (slot < 0) return;
            Hold(slot);
            m_P.UseItemRpc();
        }

        /// <summary>What it keeps on it rather than putting in the chest: its tools and weapons, the plan, food, its armour
        /// and potions, things it's about to put down, and a bow and arrows for the turret.</summary>
        bool KeepsItem(Item id)
        {
            if (id == Cfg.CurrencyItem || Cfg.IsMelee(id)) return true;
            switch (id)
            {
                case Item.BuildingPlan: case Item.Berry: case Item.Meat: case Item.Ram:
                case Item.Armor: case Item.HeavyArmor: case Item.Helmet: case Item.InvisPotion:
                case Item.Chest: case Item.Workbench: case Item.Workbench2: case Item.SleepingBag: case Item.AutoTurret:
                    return true;
                case Item.Bow: case Item.Crossbow: case Item.Arrow:
                    return TeamHasBox(Container.Turret) || Has(Item.AutoTurret);
            }
            return false;
        }

        /// <summary>The first bag slot (or hotbar slot) with something it doesn't need on it (-1: none).</summary>
        int JunkSlot()
        {
            var inv = m_P.Inv;
            for (int i = 0; i < inv.Count; i++)
                if (!inv[i].Empty && !KeepsItem(inv[i].Id)) return i;
            return -1;
        }
    }
}
