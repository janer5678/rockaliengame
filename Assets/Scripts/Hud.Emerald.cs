using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Emerald mode's screens (Emerald.cs): its two shops beside your bag - the TRADE STATION's (E on it: its things for
    /// wood, your wood up top) and the EMERALD SHOP's (E on it: its things for emeralds, your emeralds up top) - in the
    /// crafting list's style: a row each (icon, name and how many, what it does, the price - red when you're short - and
    /// BUY), the list scrolling with the mouse wheel when it's longer than the screen. Also a clock over every emerald
    /// spawner in view ("EMERALD 2:13", or "EMERALD!" when one's waiting), and your emeralds top left.
    /// </summary>
    public partial class Hud
    {
        float m_ShopScroll;
        int m_ShopDrawnFrame = -10;
        readonly List<int> m_ShopTmp = new List<int>();

        /// <summary>Test hooks: the shop's rows as last drawn (item, why it can't be bought - null = it can), which shop,
        /// how far its list can scroll, and the clocks drawn over the spawners.</summary>
        public static readonly List<(Item Id, string Problem)> ShopRowsShown = new List<(Item, string)>();
        public static int ShopShown;
        public static float ShopScrollMax;
        public static readonly List<string> EmeraldClocksShown = new List<string>();
        public static float TestShopScrollTo = -1f;

        static readonly Color k_EmGreen = new Color(0.36f, 1f, 0.55f), k_EmRow = new Color(0.05f, 0.18f, 0.1f, 0.85f);

        /// <summary>Right of the bag: the open shop's list (PlayerController.ShopOpen).</summary>
        void DrawShopList(PlayerNet me, PlayerController pc, float x, float top, float colW, float bottom, float k)
        {
            var ev = Event.current;
            int team = me.Team.Value;
            bool em = pc.ShopOpen == PlayerController.ShopEmerald;
            if (ev.type == EventType.Repaint) { ShopRowsShown.Clear(); ShopShown = pc.ShopOpen; }
            string title = em ? "EMERALD SHOP" : "TRADE STATION";
            Shadowed(new Rect(x, top - 36 * k, colW, 32 * k), $"<b>{title}</b>", CraftStyle(20 * k, FontStyle.Bold, TextAnchor.MiddleLeft, em ? k_EmGreen : Color.white));
            string have = em ? $"<color=#5dff8a>{Thousands(me.Count(Item.Emerald))} emerald{(me.Count(Item.Emerald) == 1 ? "" : "s")}</color>" : CurrencyText(me);
            Shadowed(new Rect(x, top - 36 * k, colW, 32 * k), have, CraftStyle(18 * k, FontStyle.Bold, TextAnchor.MiddleRight, Color.white));
            if (em)
            {
                // the emerald by the count
                var icon = ItemIcons.Get(Item.Emerald);
                float tw = CraftStyle(18 * k, FontStyle.Bold, TextAnchor.MiddleRight, Color.white).CalcSize(new GUIContent(have)).x;
                if (icon != null) GUI.DrawTexture(new Rect(x + colW - tw - 34 * k, top - 34 * k, 28 * k, 28 * k), icon, ScaleMode.ScaleToFit, true);
            }

            // the rows
            float row = 54 * k, gap = 4 * k, footer = HoverInfoHeight(k);
            int n;
            List<Cfg.EmeraldDeal> deals = null;
            if (em) { deals = Cfg.EmeraldDeals; n = deals.Count; }
            else
            {
                m_ShopTmp.Clear();
                Cfg.AddTier(m_ShopTmp, 1);
                for (int i = m_ShopTmp.Count - 1; i >= 0; i--) if (Workbench.IsBench(Cfg.CraftRecipe(m_ShopTmp[i], team).Output)) m_ShopTmp.RemoveAt(i);
                n = m_ShopTmp.Count;
            }
            float contentH = n * (row + gap);
            var view = new Rect(x, top, colW, Mathf.Max(row, bottom - top - footer));
            float max = Mathf.Max(0f, contentH - view.height);
            ShopScrollMax = max;
            if (Time.frameCount - m_ShopDrawnFrame > 2) m_ShopScroll = 0f;
            m_ShopDrawnFrame = Time.frameCount;
            if (TestShopScrollTo >= 0f && ev.type == EventType.Repaint) { m_ShopScroll = TestShopScrollTo; TestShopScrollTo = -1f; }
            bool overList = view.Contains(ev.mousePosition);
            if (ev.type == EventType.ScrollWheel && overList && max > 0f)
            {
                float d = Mathf.Abs(ev.delta.y) > 0.001f ? ev.delta.y : ev.delta.x;
                m_ShopScroll += d * 18f * k;
                ev.Use();
            }
            m_ShopScroll = Mathf.Clamp(m_ShopScroll, 0f, max);
            GUI.BeginClip(view);
            float y = -m_ShopScroll;
            for (int i = 0; i < n; i++)
            {
                var rr = new Rect(0, y, colW - (max > 0f ? 10 * k : 0f), row);
                bool visible = y + row > 0f && y < view.height;
                if (em) DrawEmeraldRow(me, rr, i, deals[i], k, ev, overList, visible);
                else DrawTradeRow(me, pc, rr, m_ShopTmp[i], k, ev, overList, visible);
                y += row + gap;
            }
            GUI.EndClip();
            if (max > 0f)
            {
                // a thin scroll bar on the right
                float thumbH = Mathf.Max(30 * k, view.height * view.height / contentH);
                float thumbY = top + (view.height - thumbH) * (m_ShopScroll / max);
                Fill(new Rect(x + colW - 6 * k, top, 6 * k, view.height), new Color(0, 0, 0, 0.5f));
                Fill(new Rect(x + colW - 6 * k, thumbY, 6 * k, thumbH), new Color(0.8f, 0.8f, 0.75f, 0.85f));
            }
            var info = new Rect(x, top + Mathf.Min(view.height, contentH) + 6 * k, colW, footer);
            if (!DrawHoverInfo(info, k))
                Shadowed(info, em ? $"Emeralds grow on the emerald spawners (the raised green platforms) - one every {Cfg.EmeraldEvery / 60f:0.#} minutes on each. LMB on BUY. {KT(Bind.Inventory)} or [Esc] closes this."
                    : $"The Trade Station's things are traded here, for wood. LMB on BUY. {KT(Bind.Inventory)} or [Esc] closes this.", CraftStyle(13 * k, FontStyle.Normal, TextAnchor.UpperLeft, Color.white, true));
        }

        /// <summary>A shop row: icon, name (and how many), what it does, the price and BUY.</summary>
        void ShopRow(Rect rr, Item id, string name, string blurb, string price, string problem, bool green, float k, Event ev, bool canClick, System.Action buy)
        {
            bool ok = problem == null;
            bool over = canClick && rr.Contains(ev.mousePosition);
            float row = rr.height;
            Fill(rr, ok ? (green ? k_EmRow : k_RowOk) : k_RowNo);
            if (over) { Fill(rr, new Color(1, 1, 1, 0.06f)); SetHover($"{Cfg.ItemName(id)}: {blurb}"); }
            Fill(new Rect(rr.x, rr.y, 4 * k, row), ok ? (green ? k_EmGreen : new Color(0.5f, 0.9f, 0.3f)) : new Color(0.4f, 0.4f, 0.38f));
            float pad = Mathf.Min(4 * k, row * 0.08f);
            var ib = new Rect(rr.x + 8 * k, rr.y + pad, row - 2 * pad, row - 2 * pad);
            Fill(ib, new Color(0, 0, 0, 0.3f));
            Icon(new Rect(ib.x + 2, ib.y + 2, ib.width - 4, ib.height - 4), id, ok ? 1f : 0.55f);
            float bw = Mathf.Min(96 * k, rr.width * 0.22f);
            float tx = ib.xMax + 8 * k, tw = rr.xMax - bw - 10 * k - tx;
            GUI.Label(new Rect(tx, rr.y + row * 0.02f, tw, row * 0.36f), name, FitStyle(name, tw, Mathf.Min(18 * k, row * 0.33f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            GUI.Label(new Rect(tx, rr.y + row * 0.36f, tw, row * 0.3f), $"<color=#c8c8c0>{blurb}</color>", FitStyle(blurb, tw, Mathf.Min(13 * k, row * 0.24f), FontStyle.Normal, TextAnchor.MiddleLeft, Color.white));
            string sub = price + (problem != null && problem != "can't afford" ? $"  <color=#ffd24a>{problem}</color>" : "");
            GUI.Label(new Rect(tx, rr.y + row * 0.66f, tw, row * 0.32f), sub, FitStyle(sub, tw, Mathf.Min(15 * k, row * 0.27f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            float bp = Mathf.Max(3 * k, row * 0.16f);
            var br = new Rect(rr.xMax - bw - 6 * k, rr.y + bp, bw, row - 2 * bp);
            if (FlatBtn(br, ok ? (green ? new Color(0.12f, 0.55f, 0.28f) : k_BtnOk) : k_BtnNo, green ? new Color(0.2f, 0.72f, 0.38f) : k_BtnOkHi, ok && canClick)) buy();
            GUI.Label(br, "BUY", CraftStyle(Mathf.Min(17 * k, row * 0.34f), FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(1, 1, 1, 0.35f)));
        }

        void DrawEmeraldRow(PlayerNet me, Rect rr, int index, Cfg.EmeraldDeal d, float k, Event ev, bool canClick, bool visible)
        {
            string problem = me.Count(Item.Emerald) < d.Price ? "can't afford" : me.EmeraldProblem(d) != null ? (d.Id == Item.HighArmor ? "wearing it" : "bag full") : null;
            if (ev.type == EventType.Repaint) ShopRowsShown.Add((d.Id, problem));
            if (!visible) return;
            string name = Cfg.ItemName(d.Id) + (d.Count > 1 ? $" <color=#bbbbbb>x{d.Count}</color>" : "") + (d.Extra != Item.None ? $" <color=#bbbbbb>+ {Cfg.ItemName(d.Extra).ToLower()}</color>" : "");
            bool afford = me.Count(Item.Emerald) >= d.Price;
            string price = $"<color={(afford ? "#5dff8a" : "#ff6a5a")}>{d.Price} emerald{(d.Price == 1 ? "" : "s")}</color>";
            ShopRow(rr, d.Id, name, d.Blurb, price, problem, true, k, ev, canClick, () => { me.EmeraldBuyRpc((byte)index); Sfx.PlayUi(Sfx.UiClick, 0.8f, 0.7f); });
        }

        void DrawTradeRow(PlayerNet me, PlayerController pc, Rect rr, int idx, float k, Event ev, bool canClick, bool visible)
        {
            int team = me.Team.Value;
            var rec = Cfg.CraftRecipe(idx, team);
            string problem = ListProblem(me, pc, rec);
            if (ev.type == EventType.Repaint) ShopRowsShown.Add((rec.Output, problem));
            if (!visible) return;
            string name = Cfg.ItemName(rec.Output) + (rec.Count > 1 ? $" <color=#bbbbbb>x{rec.Count}</color>" : "");
            string full = CraftDescription(rec.Output, team);
            int c = full.IndexOf(": ");
            string blurb = c > 0 && c < 48 ? full.Substring(c + 2) : full;
            ShopRow(rr, rec.Output, name, blurb, CostColored(me, rec), problem, false, k, ev, canClick, () => me.CraftRpc(idx));
        }

        // ------------------------------------------------------------------ out in the world

        /// <summary>A clock over each emerald spawner in view, and your emeralds top left.</summary>
        void DrawEmeraldHud(PlayerNet me, float k)
        {
            if (!Cfg.Emerald) return;
            var g = NetGame.Instance;
            var cam = Camera.main;
            if (Event.current.type == EventType.Repaint) EmeraldClocksShown.Clear();
            var pcl = PlayerController.Local;
            if (g != null && cam != null && (pcl == null || !pcl.MenuOpen))
            {
                var st = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter, richText = true, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(m_Small.fontSize * 1.1f) };
                for (int i = 0; i < EmeraldSpawner.Tops.Count; i++)
                {
                    var top = EmeraldSpawner.Tops[i] + Vector3.up * 1.9f;
                    var sp = cam.WorldToScreenPoint(top);
                    float dist = Vector3.Distance(cam.transform.position, top);
                    if (sp.z <= 0f || dist > 60f) continue;
                    float left = g.EmeraldSecondsLeft(i);
                    if (left < 0f) continue;
                    string text = left <= 0f ? "<color=#5dff8a>EMERALD!</color>" : $"<color=#9dffb8>EMERALD</color> {Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}";
                    var r = new Rect(sp.x - 90 * k, Screen.height - sp.y - 14 * k, 180 * k, 28 * k);
                    Shadowed(r, text, st);
                    if (Event.current.type == EventType.Repaint) EmeraldClocksShown.Add(text);
                }
            }
            // your emeralds: top left, under the team box and the radar
            int have = me.Count(Item.Emerald);
            var box = new Rect(12 * k, Mathf.Max(12 * k, m_RadarBottom + 34 * k) + (Time.time - BallWoodShownAt < 0.2f ? BallWoodRect.height + 6 * k : 0f), 150 * k, 30 * k);
            Fill(box, new Color(0.03f, 0.14f, 0.07f, 0.7f));
            Fill(new Rect(box.x, box.y, 5 * k, box.height), k_EmGreen);
            var icon = ItemIcons.Get(Item.Emerald);
            if (icon != null) GUI.DrawTexture(new Rect(box.x + 9 * k, box.y + 2 * k, 26 * k, 26 * k), icon, ScaleMode.ScaleToFit, true);
            Shadowed(new Rect(box.x + 40 * k, box.y, box.width - 44 * k, box.height), $"<b><color=#5dff8a>{have}</color> EMERALD{(have == 1 ? "" : "S")}</b>", new GUIStyle(m_Small) { alignment = TextAnchor.MiddleLeft, richText = true });
        }

        /// <summary>The held-item line for Emerald mode's things (and its own takes on the large walls and the turret).</summary>
        string EmeraldHint(ItemStack s, PlayerNet me)
        {
            switch (s.Id)
            {
                case Item.Emerald: return $"<b>Emerald</b> x{s.Count}    spend them at your emerald shop ({KT(Bind.Interact)} on it, left of your alien machine)";
                case Item.HighArmor: return $"<b>High Power Armour</b> ({(s.Data > 0 ? s.Data : Cfg.HighArmorHp)} HP)    LMB: put it on - a second health bar that goes first";
                case Item.Lightsaber: return $"<b>Lightsaber</b>    LMB: slash ({Cfg.LightsaberDamage:0} a hit)";
                case Item.HealBerry: return $"<b>Instant Heal Berries</b> x{s.Count}    LMB / RMB: eat one - full health at once";
                case Item.LandMine: return $"<b>Land Mine</b> x{s.Count}    LMB: put it on the ground, anywhere - an enemy who steps on it blows up (they can hardly see it)";
                case Item.FortCreator: return "<b>Fort Creator</b>    LMB: throw it - a fort pops up where it lands: tall walls round a lookout tower, and a gate";
            }
            if (!Cfg.Emerald) return null;
            switch (s.Id)
            {
                case Item.Barrier: return $"<b>High Defensive Wall</b> x{s.Count}    LMB: place it - a very tall log wall, round your base or out in the open (not in the enemy base)";
                case Item.LargeGate: return $"<b>High Defensive Gate</b> x{s.Count}    LMB: place it in your wall line - your team opens it with {KT(Bind.Interact)}";
                case Item.AutoTurret: return $"<b>Base Turret</b>    LMB: place it in your base - it looks all the way round and shoots any enemy it sees ({Cfg.BaseTurretDamage:0} a shot)";
                default: return null;
            }
        }
    }
}
