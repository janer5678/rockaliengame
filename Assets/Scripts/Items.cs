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

        /// <summary>Adds items (topping up stacks first, then empty slots). Returns how many did NOT fit.</summary>
        public static int Add(NetworkList<ItemStack> list, Item id, int count, int data = 0, bool playerInv = false)
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
            // empty slots: tools prefer the hotbar, materials prefer the main inventory
            bool hotbarFirst = !playerInv || Cfg.PrefersHotbar(id);
            for (int pass = 0; pass < 2 && count > 0; pass++)
            {
                for (int i = 0; i < list.Count && count > 0; i++)
                {
                    if (playerInv)
                    {
                        bool hot = i < Cfg.HotbarSize;
                        if ((pass == 0) != (hot == hotbarFirst)) continue;
                    }
                    else if (pass == 1) break;
                    if (!list[i].Empty) continue;
                    int put = Mathf.Min(count, max);
                    list[i] = ItemStack.Of(id, put, data);
                    count -= put;
                }
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
