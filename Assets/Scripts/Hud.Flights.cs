using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Items flying between slots in the bag (inventory, hotbar and an open chest): whenever a slot gains something that
    /// another slot just lost (a drag, a split, shift-click quick-move, a chest sorting itself), its icon flies from the
    /// old slot to the new one - or from the pointer, when it was dragged there - and pops as it lands. Worked out on this
    /// screen only, by comparing the slots with what they held last frame, so it never changes what the server does.
    /// </summary>
    public partial class Hud
    {
        struct Flight { public Item Id; public int Count; public Vector2 From; public byte Kind; public int Index; public float Start; }
        struct Change { public byte Kind; public int Index; public Item Id; public int Amount; public float Time; }

        const float FlightTime = 0.24f, LandTime = 0.08f, MatchWindow = 0.4f; // (the landing pop is quick: half what it was)
        readonly List<Flight> m_Flights = new List<Flight>();
        readonly List<Change> m_Losses = new List<Change>(), m_Gains = new List<Change>();
        readonly Dictionary<int, Rect> m_SlotRects = new Dictionary<int, Rect>();
        readonly Dictionary<int, float> m_Landed = new Dictionary<int, float>();
        ItemStack[] m_SnapInv = new ItemStack[0], m_SnapLoot = new ItemStack[0];
        Container m_SnapLootOf;
        int m_FlightFrame = -10;
        // the last drag let go over a slot: its icon starts from the pointer, not from the slot it came from
        byte m_DropKind;
        int m_DropIndex = -1;
        Vector2 m_DropAt;
        float m_DropTime = -10f;
        ItemStack m_DropStack;
        int m_DropAmount;
        bool m_DropWaiting; // (until the server's answer comes back, the dropped icon stays where it was let go)

        /// <summary>Test hooks: how many flights have started, and how many are in the air now.</summary>
        public static int FlightsStarted { get; private set; }
        public static int FlightsNow { get; private set; }
        /// <summary>Test hooks: how long a flight and its landing pop take.</summary>
        public static float FlightSeconds => FlightTime;
        public static float LandPopSeconds => LandTime;

        static int SlotKey(byte kind, int index) => kind * 1024 + index;
        void NoteSlotRect(byte kind, int index, Rect r) => m_SlotRects[SlotKey(kind, index)] = r;

        void NoteDrop(byte kind, int index, Vector2 at, ItemStack stack, int amount)
        {
            m_DropKind = kind;
            m_DropIndex = index;
            m_DropAt = at;
            m_DropTime = Time.unscaledTime;
            m_DropStack = stack;
            m_DropAmount = amount;
            m_DropWaiting = true;
        }

        /// <summary>A drag just let go over another slot, waiting for the server: its slot shows what's left behind.</summary>
        bool DropWaiting(byte kind, int index) => m_DropWaiting && m_DropKind == kind && m_DropIndex == index && Time.unscaledTime - m_DropTime < 0.6f;

        /// <summary>The dropped icon, held where it was let go until it starts flying.</summary>
        void DrawDropWaiting()
        {
            if (!m_DropWaiting) return;
            if (Time.unscaledTime - m_DropTime >= 0.6f) { m_DropWaiting = false; return; }
            var icon = ItemIcons.Get(m_DropStack.Id);
            float s = m_DragSlot * 0.84f;
            if (icon != null) GUI.DrawTexture(new Rect(m_DropAt.x - s / 2, m_DropAt.y - s / 2, s, s), icon, ScaleMode.ScaleToFit, true);
        }

        /// <summary>Once a frame while the bag is open: what changed since last frame, and the flights that makes.</summary>
        void TrackFlights(PlayerNet me, PlayerController pc)
        {
            if (m_FlightFrame == Time.frameCount) return;
            bool fresh = m_FlightFrame < Time.frameCount - 2; // (the bag has just opened: nothing to compare with)
            m_FlightFrame = Time.frameCount;
            float now = Time.unscaledTime;
            if (fresh) { m_Losses.Clear(); m_Gains.Clear(); m_Flights.Clear(); m_Landed.Clear(); m_SlotRects.Clear(); }
            Snapshot(0, me.Inv, ref m_SnapInv, !fresh);
            var c = pc.LootTarget;
            if (c != m_SnapLootOf || c == null) { m_SnapLootOf = c; m_SnapLoot = new ItemStack[0]; }
            if (c != null && c.IsSpawned) Snapshot(1, c.Slots, ref m_SnapLoot, m_SnapLoot.Length == c.Slots.Count);
            m_Losses.RemoveAll(x => now - x.Time > MatchWindow);
            m_Gains.RemoveAll(x => now - x.Time > MatchWindow);
            Match(now);
            FlightsNow = m_Flights.Count;
        }

        /// <summary>Compares a list with last frame's copy (noting gains and losses when `diff`), then copies it.</summary>
        void Snapshot(byte kind, NetworkList<ItemStack> list, ref ItemStack[] snap, bool diff)
        {
            float now = Time.unscaledTime;
            if (snap.Length != list.Count) { snap = new ItemStack[list.Count]; diff = false; }
            for (int i = 0; i < list.Count; i++)
            {
                var o = snap[i];
                var n = list[i];
                snap[i] = n;
                if (!diff || o.Equals(n)) continue;
                bool same = !o.Empty && !n.Empty && o.Id == n.Id;
                if (same)
                {
                    int d = n.Count - o.Count;
                    if (d < 0) m_Losses.Add(new Change { Kind = kind, Index = i, Id = o.Id, Amount = -d, Time = now });
                    else if (d > 0) m_Gains.Add(new Change { Kind = kind, Index = i, Id = n.Id, Amount = d, Time = now });
                    continue;
                }
                if (!o.Empty) m_Losses.Add(new Change { Kind = kind, Index = i, Id = o.Id, Amount = o.Count, Time = now });
                if (!n.Empty) m_Gains.Add(new Change { Kind = kind, Index = i, Id = n.Id, Amount = n.Count, Time = now });
            }
        }

        /// <summary>Pairs each gain with a loss of the same thing (the same amount first) and starts its flight.</summary>
        void Match(float now)
        {
            for (int g = 0; g < m_Gains.Count; g++)
            {
                var gain = m_Gains[g];
                int best = -1;
                for (int l = 0; l < m_Losses.Count; l++)
                {
                    var loss = m_Losses[l];
                    if (loss.Id != gain.Id || (loss.Kind == gain.Kind && loss.Index == gain.Index)) continue;
                    if (best < 0 || (loss.Amount == gain.Amount && m_Losses[best].Amount != gain.Amount)) best = l;
                }
                if (best < 0) continue;
                var from = m_Losses[best];
                if (!m_SlotRects.TryGetValue(SlotKey(from.Kind, from.Index), out var fr) || !m_SlotRects.TryGetValue(SlotKey(gain.Kind, gain.Index), out _)) continue;
                // dragged there by hand: it starts where the pointer let it go
                bool dropped = from.Kind == m_DropKind && from.Index == m_DropIndex && now - m_DropTime < 1f;
                var start = dropped ? m_DropAt : fr.center;
                if (dropped) m_DropWaiting = false;
                int amount = Mathf.Min(gain.Amount, from.Amount);
                m_Flights.Add(new Flight { Id = gain.Id, Count = amount, From = start, Kind = gain.Kind, Index = gain.Index, Start = now });
                FlightsStarted++;
                from.Amount -= amount;
                if (from.Amount <= 0) m_Losses.RemoveAt(best); else m_Losses[best] = from;
                m_Gains.RemoveAt(g--);
            }
            if (m_DropIndex >= 0 && now - m_DropTime > 1f) { m_DropIndex = -1; m_DropWaiting = false; }
        }

        /// <summary>A slot something is flying into shows what it had before (less what's still in the air).</summary>
        ItemStack FlightView(byte kind, int index, ItemStack s)
        {
            if (s.Empty) return s;
            int inAir = 0;
            foreach (var f in m_Flights) if (f.Kind == kind && f.Index == index && f.Id == s.Id) inAir += f.Count;
            return inAir > 0 ? s.WithCount(s.Count - inAir) : s;
        }

        /// <summary>The little bounce of an icon that has just landed (1 = normal size).</summary>
        float LandPop(byte kind, int index)
        {
            if (!m_Landed.TryGetValue(SlotKey(kind, index), out float t)) return 1f;
            float a = (Time.unscaledTime - t) / LandTime;
            if (a >= 1f) { m_Landed.Remove(SlotKey(kind, index)); return 1f; }
            return 1f + 0.18f * Mathf.Sin(a * Mathf.PI);
        }

        /// <summary>The icons in the air: ease out along a little arc, a touch bigger in the middle of the flight.</summary>
        void DrawFlights(PlayerNet me)
        {
            if (Event.current.type != EventType.Repaint) return;
            DrawDropWaiting();
            float now = Time.unscaledTime;
            for (int i = m_Flights.Count - 1; i >= 0; i--)
            {
                var f = m_Flights[i];
                float t = (now - f.Start) / FlightTime;
                if (t >= 1f || !m_SlotRects.TryGetValue(SlotKey(f.Kind, f.Index), out var to))
                {
                    m_Landed[SlotKey(f.Kind, f.Index)] = now;
                    m_Flights.RemoveAt(i);
                    continue;
                }
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                var p = Vector2.Lerp(f.From, to.center, e);
                float dist = Vector2.Distance(f.From, to.center);
                p.y -= Mathf.Sin(t * Mathf.PI) * Mathf.Min(48f * m_Scale, dist * 0.18f);
                float size = to.width * 0.8f * (1f + 0.12f * Mathf.Sin(t * Mathf.PI));
                var r = new Rect(p.x - size / 2, p.y - size / 2, size, size);
                var icon = ItemIcons.Get(f.Id);
                if (icon != null) GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit, true);
                else GUI.Label(r, Cfg.ItemName(f.Id), m_Small);
                if (f.Count > 1) Shadowed(new Rect(r.x, r.yMax - 18 * m_Scale, r.width, 18 * m_Scale), f.Count.ToString(), new GUIStyle(m_Small) { alignment = TextAnchor.LowerRight });
            }
            FlightsNow = m_Flights.Count;
        }
    }
}
