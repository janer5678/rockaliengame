using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Items flying between slots in the bag (inventory, hotbar and an open chest): whenever a slot gains something that
    /// another slot just lost (a split, shift-click quick-move, a chest sorting itself), its icon flies from the old slot
    /// to the new one and pops as it lands. Worked out on this screen only, by comparing the slots with what they held
    /// a moment ago, so it never changes what the server does. A drag let go over a slot (a move by hand) doesn't fly:
    /// the item is just there, straight away - both slots show what the move makes of them until the server answers.
    /// </summary>
    public partial class Hud
    {
        struct Flight { public Item Id; public int Count; public Vector2 From; public byte Kind; public int Index; public float Start; }
        struct Change { public byte Kind; public int Index; public Item Id; public int Amount; public float Time; }
        /// <summary>A move by hand (drag and drop) the server hasn't answered yet: what both slots held, and what they show meanwhile.</summary>
        struct Manual { public byte SrcKind, DstKind; public int SrcIndex, DstIndex; public ItemStack SrcWas, DstWas, SrcShow, DstShow; public float Time; public bool SrcDone, DstDone; }
        struct Expect { public Item Id; public float Time; }

        const float FlightTime = 0.3f, LandTime = 0.08f, MatchWindow = 0.4f; // (the flight is a touch slower than it was; the landing pop is quick)
        const float ManualWindow = 2f; // (long enough for a slow connection's answer: it used to run out first and the item jumped back)
        readonly List<Flight> m_Flights = new List<Flight>();
        readonly List<Change> m_Losses = new List<Change>(), m_Gains = new List<Change>();
        readonly List<Manual> m_Manual = new List<Manual>();
        /// <summary>Quick-moves asked for and not seen fly yet: what they gain stays out of sight until its flight starts.</summary>
        readonly List<Expect> m_Expect = new List<Expect>();
        readonly Dictionary<int, Rect> m_SlotRects = new Dictionary<int, Rect>();
        readonly Dictionary<int, float> m_Landed = new Dictionary<int, float>();
        ItemStack[] m_SnapInv = new ItemStack[0], m_SnapLoot = new ItemStack[0];
        Container m_SnapLootOf;
        int m_FlightFrame = -10;
        // a crate that vanished while it was open (an airdrop / loot bag goes the moment it's emptied): its panel stays,
        // empty, until the bag is closed - the bag doesn't jump over to the crafting layout in the middle of a move
        Container m_LootSeen;
        bool m_GhostLoot;
        string m_GhostName = "";
        int m_GhostSlots;

        /// <summary>Test hooks: how many flights have started, and how many are in the air now.</summary>
        public static int FlightsStarted { get; private set; }
        public static int FlightsNow { get; private set; }
        /// <summary>Test hooks: how long a flight and its landing pop take.</summary>
        public static float FlightSeconds => FlightTime;
        public static float LandPopSeconds => LandTime;
        /// <summary>Test hooks: moves by hand noted (they don't fly), and whether the emptied crate's panel is being kept.</summary>
        public static int ManualMoves { get; private set; }
        public static bool GhostLootShown { get; private set; }

        static int SlotKey(byte kind, int index) => kind * 1024 + index;
        void NoteSlotRect(byte kind, int index, Rect r) => m_SlotRects[SlotKey(kind, index)] = r;

        /// <summary>
        /// A drag was let go over another slot: no flight for it. Works out what the server will make of the two slots
        /// (as InvOps.Move does: move, merge or swap) so they show that straight away. False = the server will refuse it.
        /// </summary>
        bool NoteManualMove(byte srcKind, int srcIndex, byte dstKind, int dstIndex, ItemStack a, ItemStack b, int amount, Container c)
        {
            if (a.Empty || (dstKind == 1 && (c == null || c.TakeOnly))) return false;
            if (c != null && c.IsGamble && (srcKind == 1 || dstKind == 1) && (srcKind == 0 ? a.Id != Item.Dna : !b.Empty && b.Id != Item.Dna)) return false; // (GambleMachine.ServerMoveOk)
            amount = Mathf.Clamp(amount, 1, a.Count);
            int max = Cfg.MaxStack(a.Id);
            ItemStack src, dst;
            if (b.Empty) { dst = a.WithCount(amount); src = a.WithCount(a.Count - amount); }
            else if (b.Id == a.Id && b.Data == a.Data && b.Count < max)
            {
                int put = Mathf.Min(amount, max - b.Count);
                dst = b.WithCount(b.Count + put);
                src = a.WithCount(a.Count - put);
            }
            // out of an airdrop / loot bag onto a taken slot: nothing can go back into the crate, so the server puts the
            // crate's item in that slot and moves what was there elsewhere in the bag (PlayerNet.MoveItemRpc) - shown
            // straight away (it used to sit in the crate until the server answered, looking like it snapped back)
            else if (srcKind == 1 && dstKind == 0 && c != null && c.TakeOnly) { dst = a; src = default; }
            else if (amount != a.Count) return false;
            else { dst = a; src = b; }
            m_Manual.Add(new Manual { SrcKind = srcKind, SrcIndex = srcIndex, DstKind = dstKind, DstIndex = dstIndex, SrcWas = a, DstWas = b, SrcShow = src, DstShow = dst, Time = Time.unscaledTime });
            ManualMoves++;
            return true;
        }

        /// <summary>A slot in a move by hand the server hasn't answered yet: it shows what the move will leave in it.</summary>
        bool ManualView(byte kind, int index, ItemStack s, out ItemStack shown)
        {
            float now = Time.unscaledTime;
            for (int i = m_Manual.Count - 1; i >= 0; i--)
            {
                var m = m_Manual[i];
                if (now - m.Time > ManualWindow) continue;
                if (!m.SrcDone && m.SrcKind == kind && m.SrcIndex == index && s.Equals(m.SrcWas)) { shown = m.SrcShow; return true; }
                if (!m.DstDone && m.DstKind == kind && m.DstIndex == index && s.Equals(m.DstWas)) { shown = m.DstShow; return true; }
            }
            shown = s;
            return false;
        }

        /// <summary>A slot changed: was that the server's answer to a move by hand? (Then it doesn't fly.)</summary>
        bool ManualChanged(byte kind, int index)
        {
            for (int i = 0; i < m_Manual.Count; i++)
            {
                var m = m_Manual[i];
                if (!m.SrcDone && m.SrcKind == kind && m.SrcIndex == index) { m.SrcDone = true; m_Manual[i] = m; return true; }
                if (!m.DstDone && m.DstKind == kind && m.DstIndex == index) { m.DstDone = true; m_Manual[i] = m; return true; }
            }
            return false;
        }

        /// <summary>AutoTest: a drag from one slot let go over another, as the mouse does it (noted as a move by hand, then sent).</summary>
        public static void TestDrop(byte srcKind, int srcIndex, byte dstKind, int dstIndex, int amount)
        {
            var h = Object.FindFirstObjectByType<Hud>();
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            if (h == null || me == null || pc == null) return;
            h.NoteManualMove(srcKind, srcIndex, dstKind, dstIndex, SlotNow(srcKind, srcIndex, me, pc), SlotNow(dstKind, dstIndex, me, pc), amount, pc.LootTarget);
            me.MoveItemRpc(srcKind, (byte)srcIndex, dstKind, (byte)dstIndex, (ushort)amount, LootRef(pc));
        }

        /// <summary>A quick-move (shift-click) was asked for: what it gains stays out of sight until its flight starts.</summary>
        void ExpectFlight(Item id) => m_Expect.Add(new Expect { Id = id, Time = Time.unscaledTime });

        bool Expecting(Item id)
        {
            foreach (var x in m_Expect) if (x.Id == id) return true;
            return false;
        }

        /// <summary>The open crate is gone (emptied): keep its panel, empty, while the bag stays open.</summary>
        void TrackGhostLoot(PlayerController pc)
        {
            var c = pc.LootTarget;
            if (c != null)
            {
                m_LootSeen = c;
                m_GhostLoot = false;
                m_GhostName = c.DisplayName;
                m_GhostSlots = c.Slots.Count;
            }
            else if (!m_GhostLoot && !ReferenceEquals(m_LootSeen, null))
            {
                // (despawned - not just walked away from: then the bag goes back to crafting as it always has)
                m_GhostLoot = (!m_LootSeen || !m_LootSeen.IsSpawned) && m_GhostSlots > 0;
                m_LootSeen = null;
            }
            GhostLootShown = m_GhostLoot;
        }

        void ClearGhostLoot() { m_GhostLoot = false; m_LootSeen = null; GhostLootShown = false; }

        /// <summary>
        /// While the bag is open, before the slots are drawn (every GUI event, not once a frame: a click handled earlier
        /// in this frame may already have changed the slots on the host, and the repaint after it must not show the item
        /// in its new slot before its flight has started): what changed, and the flights that makes.
        /// </summary>
        void TrackFlights(PlayerNet me, PlayerController pc)
        {
            bool fresh = m_FlightFrame < Time.frameCount - 2; // (the bag has just opened: nothing to compare with)
            m_FlightFrame = Time.frameCount;
            float now = Time.unscaledTime;
            if (fresh) { m_Losses.Clear(); m_Gains.Clear(); m_Flights.Clear(); m_Landed.Clear(); m_SlotRects.Clear(); m_Manual.Clear(); m_Expect.Clear(); ClearGhostLoot(); }
            TrackGhostLoot(pc);
            Snapshot(0, me.Inv, ref m_SnapInv, !fresh);
            var c = pc.LootTarget;
            if (c == null && m_GhostLoot && !fresh)
            {
                // the crate went with things still in it as far as this screen knew: they left it (so what the bag
                // gained can fly from where they were)
                for (int i = 0; i < m_SnapLoot.Length; i++)
                    if (!m_SnapLoot[i].Empty && !ManualChanged(1, i)) m_Losses.Add(new Change { Kind = 1, Index = i, Id = m_SnapLoot[i].Id, Amount = m_SnapLoot[i].Count, Time = now });
            }
            if (c != m_SnapLootOf || c == null) { m_SnapLootOf = c; m_SnapLoot = new ItemStack[0]; }
            if (c != null && c.IsSpawned) Snapshot(1, c.Slots, ref m_SnapLoot, m_SnapLoot.Length == c.Slots.Count);
            m_Losses.RemoveAll(x => now - x.Time > MatchWindow);
            m_Gains.RemoveAll(x => now - x.Time > MatchWindow);
            m_Manual.RemoveAll(x => now - x.Time > ManualWindow || (x.SrcDone && x.DstDone));
            m_Expect.RemoveAll(x => now - x.Time > MatchWindow);
            Match(now);
            // landed: the slot shows it from this event on (decided here, before the slots are drawn, so there's never
            // a repaint with the icon neither in the air nor in its slot)
            for (int i = m_Flights.Count - 1; i >= 0; i--)
            {
                var f = m_Flights[i];
                if (now - f.Start < FlightTime && m_SlotRects.ContainsKey(SlotKey(f.Kind, f.Index))) continue;
                m_Landed[SlotKey(f.Kind, f.Index)] = now;
                m_Flights.RemoveAt(i);
            }
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
                if (ManualChanged(kind, i)) continue; // (the server's answer to a move by hand: no flight)
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
                int amount = Mathf.Min(gain.Amount, from.Amount);
                m_Flights.Add(new Flight { Id = gain.Id, Count = amount, From = fr.center, Kind = gain.Kind, Index = gain.Index, Start = now });
                FlightsStarted++;
                from.Amount -= amount;
                if (from.Amount <= 0) m_Losses.RemoveAt(best); else m_Losses[best] = from;
                m_Gains.RemoveAt(g--);
                int ex = m_Expect.FindIndex(x => x.Id == gain.Id);
                if (ex >= 0) m_Expect.RemoveAt(ex);
            }
        }

        /// <summary>
        /// A slot something is flying into shows what it had before (less what's still in the air) - and so does one
        /// that has gained what a quick-move is about to fly in, when its half of the server's answer got here before
        /// the other half (the slot it left): it never shows there before the flight.
        /// </summary>
        ItemStack FlightView(byte kind, int index, ItemStack s)
        {
            if (s.Empty) return s;
            int inAir = 0;
            foreach (var f in m_Flights) if (f.Kind == kind && f.Index == index && f.Id == s.Id) inAir += f.Count;
            if (m_Expect.Count > 0)
                foreach (var g in m_Gains) if (g.Kind == kind && g.Index == index && g.Id == s.Id && Expecting(g.Id)) inAir += g.Amount;
            return inAir > 0 ? s.WithCount(Mathf.Max(0, s.Count - inAir)) : s;
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
            float now = Time.unscaledTime;
            for (int i = m_Flights.Count - 1; i >= 0; i--)
            {
                var f = m_Flights[i];
                // (TrackFlights lands them, before the slots are drawn; one that's due is drawn on its slot until then)
                float t = Mathf.Clamp01((now - f.Start) / FlightTime);
                if (!m_SlotRects.TryGetValue(SlotKey(f.Kind, f.Index), out var to)) continue;
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
