using System;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>One inventory slot. Data is per-item state (battering ram: hits left).</summary>
    public struct ItemStack : INetworkSerializeByMemcpy, IEquatable<ItemStack>
    {
        public Item Id;
        public byte Data;
        public ushort Count;

        public bool Empty => Id == Item.None || Count == 0;
        public static ItemStack Of(Item id, int count, int data = 0) =>
            count <= 0 || id == Item.None ? default : new ItemStack { Id = id, Count = (ushort)count, Data = (byte)data };
        public ItemStack WithCount(int c) => Of(Id, c, Data);

        public bool Equals(ItemStack o) => Id == o.Id && Count == o.Count && Data == o.Data;
        public override bool Equals(object obj) => obj is ItemStack s && Equals(s);
        public override int GetHashCode() => ((int)Id << 24) ^ (Data << 16) ^ Count;
    }

    /// <summary>Server-side operations on slot lists (player inventories and containers).</summary>
    public static class InvOps
    {
        public static int Count(NetworkList<ItemStack> list, Item id)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) n += list[i].Count;
            return n;
        }

        public static bool HasEmpty(NetworkList<ItemStack> list)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Empty) return true;
            return false;
        }

        /// <summary>Removes n of an item, taking from the main inventory before the hotbar. Returns false (and removes nothing) if there isn't enough.</summary>
        public static bool Remove(NetworkList<ItemStack> list, Item id, int n)
        {
            if (n <= 0) return true;
            if (Count(list, id) < n) return false;
            for (int i = list.Count - 1; i >= 0 && n > 0; i--)
            {
                var s = list[i];
                if (s.Id != id) continue;
                int take = Mathf.Min(n, s.Count);
                list[i] = s.WithCount(s.Count - take);
                n -= take;
            }
            return true;
        }

        /// <summary>How many of this item would fit.</summary>
        public static int Space(NetworkList<ItemStack> list, Item id, int data = 0)
        {
            int max = Cfg.MaxStack(id), space = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Empty) space += max;
                else if (s.Id == id && s.Data == data) space += Mathf.Max(0, max - s.Count);
            }
            return space;
        }

        /// <summary>
        /// Adds items (topping up existing stacks anywhere first, then empty slots). Returns how many did NOT fit.
        /// Player inventory: materials go to the hotbar from its last slot backwards (skipping `avoidSlot`, the empty slot you're
        /// holding your rock in), tools to the hotbar from slot 1; then the main inventory.
        /// </summary>
        public static int Add(NetworkList<ItemStack> list, Item id, int count, int data = 0, bool playerInv = false, int avoidSlot = -1, bool fromRight = false)
        {
            if (id == Item.None || count <= 0) return 0;
            int max = Cfg.MaxStack(id);
            for (int i = 0; i < list.Count && count > 0; i++)
            {
                var s = list[i];
                if (s.Id != id || s.Data != data || s.Count >= max) continue;
                int put = Mathf.Min(count, max - s.Count);
                list[i] = s.WithCount(s.Count + put);
                count -= put;
            }
            if (count <= 0) return 0;
            bool mat = Cfg.IsMat(id) || fromRight;
            for (int k = 0; k < list.Count && count > 0; k++)
            {
                int i = k;
                if (playerInv && k < Cfg.HotbarSize)
                {
                    if (mat) i = Cfg.HotbarSize - 1 - k;
                    if (i == avoidSlot) continue;
                }
                if (!list[i].Empty) continue;
                int put = Mathf.Min(count, max);
                list[i] = ItemStack.Of(id, put, data);
                count -= put;
            }
            // last resort: the slot we tried to keep free
            if (count > 0 && avoidSlot >= 0 && avoidSlot < list.Count && list[avoidSlot].Empty)
            {
                int put = Mathf.Min(count, max);
                list[avoidSlot] = ItemStack.Of(id, put, data);
                count -= put;
            }
            return count;
        }

        /// <summary>
        /// Rust-style drag: move `amount` from src[si] to dst[di]. Empty target = move, same item = merge,
        /// different item = swap (whole stacks only). `takeOnlySrc`: src is a death bag, so nothing may be swapped into it.
        /// </summary>
        public static bool Move(NetworkList<ItemStack> src, int si, NetworkList<ItemStack> dst, int di, int amount, bool takeOnlySrc)
        {
            if (si < 0 || si >= src.Count || di < 0 || di >= dst.Count) return false;
            if (src == dst && si == di) return false;
            var a = src[si];
            if (a.Empty) return false;
            amount = Mathf.Clamp(amount, 1, a.Count);
            var b = dst[di];
            int max = Cfg.MaxStack(a.Id);
            if (b.Empty)
            {
                dst[di] = a.WithCount(amount);
                src[si] = a.WithCount(a.Count - amount);
                return true;
            }
            if (b.Id == a.Id && b.Data == a.Data && b.Count < max)
            {
                int put = Mathf.Min(amount, max - b.Count);
                dst[di] = b.WithCount(b.Count + put);
                src[si] = a.WithCount(a.Count - put);
                return true;
            }
            if (amount != a.Count || takeOnlySrc) return false;
            dst[di] = a;
            src[si] = b;
            return true;
        }

        /// <summary>Chest sorting: resources, then ammo, weapons, tools, things you place, armour, food, then everything else.</summary>
        static int SortGroup(Item i)
        {
            switch (i)
            {
                case Item.Wood: case Item.Stone: return 0;
                case Item.Arrow: case Item.ShotgunShell: case Item.RevolverAmmo: case Item.PistolAmmo: return 1;
                case Item.Sword: case Item.Spear: case Item.Bow: case Item.Crossbow: case Item.Shotgun: case Item.Revolver: case Item.Pistol:
                case Item.Sniper: case Item.RocketLauncher: case Item.DeathWand: case Item.C4: case Item.PortalGun: return 2;
                case Item.Hatchet: case Item.Pickaxe: case Item.Chainsaw: case Item.TreeCracker: case Item.Ram: case Item.HeavyRam: case Item.BuildingPlan: return 3;
                case Item.Chest: case Item.Barrier: case Item.Saddle: case Item.Boat: case Item.FortTower: case Item.Car: return 4;
                case Item.Helmet: case Item.Armor: case Item.HeavyArmor: return 5;
                case Item.Berry: case Item.Meat: return 6;
                default: return 7;
            }
        }

        /// <summary>Tidies a chest: tops up stacks of the same thing and puts everything in a sensible order from the first slot.</summary>
        public static void Sort(NetworkList<ItemStack> list)
        {
            var items = new System.Collections.Generic.List<ItemStack>();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Empty) continue;
                int max = Cfg.MaxStack(s.Id);
                // merge into a stack of the same thing that has room
                for (int k = 0; k < items.Count && s.Count > 0; k++)
                {
                    var t = items[k];
                    if (t.Id != s.Id || t.Data != s.Data || t.Count >= max) continue;
                    int put = Mathf.Min(s.Count, max - t.Count);
                    items[k] = t.WithCount(t.Count + put);
                    s = s.WithCount(s.Count - put);
                }
                if (!s.Empty) items.Add(s);
            }
            items.Sort((a, b) =>
            {
                int g = SortGroup(a.Id).CompareTo(SortGroup(b.Id));
                if (g != 0) return g;
                int id = ((int)a.Id).CompareTo((int)b.Id);
                return id != 0 ? id : b.Count.CompareTo(a.Count);
            });
            for (int i = 0; i < list.Count; i++)
            {
                var want = i < items.Count ? items[i] : default;
                if (!list[i].Equals(want)) list[i] = want;
            }
        }

        /// <summary>Shift-click: move a whole stack into the other container wherever it fits.</summary>
        public static bool QuickMove(NetworkList<ItemStack> src, int si, NetworkList<ItemStack> dst, bool dstIsPlayer)
        {
            if (si < 0 || si >= src.Count) return false;
            var a = src[si];
            if (a.Empty) return false;
            int left = Add(dst, a.Id, a.Count, a.Data, dstIsPlayer);
            if (left == a.Count) return false;
            src[si] = a.WithCount(left);
            return true;
        }
    }
}
