using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A stack of items on the ground that gets added to (the Auto Wood pile out of the wood machine growing) gives a
    /// little bounce: it squashes, hops up stretched tall and wobbles back to rest, with a soft knock. Every screen sees
    /// it - each one watches the synced stack count in Items go up.
    /// </summary>
    public partial class NetGame
    {
        readonly Dictionary<int, int> m_ItemCount = new Dictionary<int, int>();
        readonly Dictionary<int, float> m_ItemBounceAt = new Dictionary<int, float>();

        /// <summary>For the tests: how many times a stack on the ground has bounced on this machine.</summary>
        public static int StackBounces { get; private set; }

        const float BounceTime = 0.55f;

        /// <summary>The bounce for one world item this frame (after its position is set). `fresh`: its visual was only just made.</summary>
        void ItemBounce(DroppedItem it, Transform t, bool fresh)
        {
            int c = it.Stack.Count;
            if (!fresh && m_ItemCount.TryGetValue(it.Id, out var prev) && c > prev)
            {
                m_ItemBounceAt[it.Id] = Time.time;
                StackBounces++;
                Sfx.Play(Sfx.Thud, it.Pos, 0.2f, 0.15f, 14f);
            }
            m_ItemCount[it.Id] = c;
            if (!m_ItemBounceAt.TryGetValue(it.Id, out var at)) return;
            float e = Time.time - at;
            if (e >= BounceTime)
            {
                m_ItemBounceAt.Remove(it.Id);
                t.localScale = Vector3.one;
                return;
            }
            // a damped spring: squash first, then stretch up tall, and settle
            float spring = -Mathf.Sin(e * 26f) * Mathf.Exp(-e * 7f);
            float sy = 1f + 0.32f * spring, sxz = 1f / Mathf.Sqrt(Mathf.Max(0.5f, sy));
            t.localScale = new Vector3(sxz, sy, sxz);
            // and a little hop off the ground on the stretch
            float hop = e > 0.06f && e < 0.3f ? Mathf.Sin((e - 0.06f) / 0.24f * Mathf.PI) * 0.12f : 0f;
            t.position += Vector3.up * hop;
        }

        void ForgetItemBounce(int id)
        {
            m_ItemCount.Remove(id);
            m_ItemBounceAt.Remove(id);
        }
    }
}
