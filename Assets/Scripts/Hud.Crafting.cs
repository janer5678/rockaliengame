using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Crafting: TAB shows the inventory with the crafting list on its right, one row per item (icon, name, price, CRAFT).
    /// The starter items are always there; a Workbench T1 your team has put down adds the T1 items under their own
    /// heading, and a Workbench T2 the T2 items (Cfg.BenchTier). Away from your base they're still listed, greyed with
    /// "in your base" (they're only crafted there: Cfg.CraftTierAt / the server's check). It's one column: when it's longer
    /// than the screen it scrolls (mouse wheel - Shift+wheel too, so it scrolls while you sprint - or drag the scroll bar
    /// that shows up on its right).
    /// The Workbench T1 row is grey and says so until your team has captured the ball (Cfg.BenchUnlocked).
    /// </summary>
    public partial class Hud
    {
        /// <summary>The list as drawn: a heading (Idx -1, its Tier) or an item (craft number Idx).</summary>
        readonly List<(int Idx, int Tier)> m_CraftRows = new List<(int Idx, int Tier)>();
        int m_CraftTier;
        // the scroll: how far down the list is (pixels), whether the scroll bar's thumb is being dragged, and when the
        // bag was last drawn (it opens at the top again)
        float m_CraftScroll, m_ThumbGrab;
        bool m_ThumbDrag;
        int m_CraftDrawnFrame = -10, m_WheelFrame = -10;

        /// <summary>Test hooks: how far the list can scroll (0 = it all fits, no scroll bar) and how far it is scrolled,
        /// the rows as last drawn (item, why it can't be crafted - null = it can), and a scroll position to jump to.</summary>
        public static float CraftScrollMax { get; private set; }
        public static float CraftScrollNow { get; private set; }
        public static readonly List<(Item Id, string Problem)> CraftRowsShown = new List<(Item, string)>();
        public static float TestScrollTo = -1f;

        static readonly Color k_RowOk = new Color(0.12f, 0.2f, 0.09f, 0.85f), k_RowNo = new Color(0, 0, 0, 0.55f);
        static readonly Color k_BtnOk = new Color(0.33f, 0.56f, 0.18f, 1f), k_BtnOkHi = new Color(0.42f, 0.7f, 0.24f, 1f), k_BtnNo = new Color(0.25f, 0.25f, 0.24f, 0.9f);

        // ------------------------------------------------------------------ shared helpers

        GUIStyle CraftStyle(float size, FontStyle fs, TextAnchor a, Color c, bool wrap = false)
        {
            var s = new GUIStyle(m_Label) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size)), fontStyle = fs, alignment = a, wordWrap = wrap, clipping = TextClipping.Clip, richText = true };
            s.normal.textColor = c;
            s.padding = new RectOffset(0, 0, 0, 0);
            return s;
        }

        /// <summary>CraftStyle, shrunk until the text fits on one line in `w`.</summary>
        GUIStyle FitStyle(string text, float w, float size, FontStyle fs, TextAnchor a, Color c)
        {
            var st = CraftStyle(size, fs, a, c);
            float tw = st.CalcSize(new GUIContent(text)).x;
            if (tw > w && tw > 0f) st.fontSize = Mathf.Max(8, Mathf.FloorToInt(st.fontSize * w / tw));
            return st;
        }

        /// <summary>A flat button: a filled rect that lightens under the mouse.</summary>
        bool FlatBtn(Rect r, Color bg, Color hover, bool enabled = true)
        {
            bool over = enabled && r.Contains(Event.current.mousePosition);
            Fill(r, over ? hover : bg);
            bool was = GUI.enabled;
            GUI.enabled = enabled && was;
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            TrackHover(r);
            GUI.enabled = was;
            if (hit) ClickSound();
            return hit;
        }

        static void Icon(Rect r, Item id, float alpha = 1f)
        {
            var icon = ItemIcons.Get(id);
            if (icon == null) return;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        static string Thousands(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A price, each part red when you're short of it: "150 wood", "30 wood + 10 stone", "120 DNA".</summary>
        static string CostColored(PlayerNet me, Recipe r)
        {
            bool woodOk = me.Count(Cfg.CurrencyItem) >= r.Wood, stoneOk = me.Count(Item.Stone) >= r.Stone;
            string cur = Cfg.DnaRules ? "#7dffb0" : "#f0b878";
            string s = r.Wood > 0 || r.Stone == 0 ? $"<color={(woodOk ? cur : "#ff6a5a")}>{Thousands(r.Wood)} {Cfg.CurrencyName}</color>" : "";
            if (r.Stone > 0) s += (s != "" ? "  +  " : "") + $"<color={(stoneOk ? "#d4d4dc" : "#ff6a5a")}>{Thousands(r.Stone)} stone</color>";
            return s;
        }

        /// <summary>What you have to spend (the TAB header).</summary>
        static string CurrencyText(PlayerNet me) => Cfg.DnaRules ? $"<color=#7dffb0>{Thousands(me.Count(Item.Dna))} DNA</color>" :
            $"<color=#f0b878>{Thousands(me.Count(Item.Wood))} wood</color>" + (Cfg.WoodMode || Tutorial.HideStone ? "" : $"   <color=#d4d4dc>{Thousands(me.Count(Item.Stone))} stone</color>");

        /// <summary>A short description of everything you can make (shown when you point at it).</summary>
        static string CraftDescription(Item id, int team)
        {
            switch (id)
            {
                case Item.Hatchet: return "Stone Hatchet: chops trees much faster than your rock. Can be crafted anywhere.";
                case Item.Pickaxe: return "Stone Pickaxe: mines rocks for stone much faster than your rock.";
                case Item.Spear: return "Spear: LMB stabs; hold RMB and press LMB to throw it (E picks it back up). Can be crafted anywhere.";
                case Item.BuildingPlan: return "Building Plan: hold it to build. Hold RMB for the building wheel.";
                case Item.Chest: return $"Storage Chest: place it in your base, E opens it ({Cfg.ChestSlots} slots).";
                case Item.Bow: return "Bow: hold LMB to draw, let go to fire. Uses arrows.";
                case Item.Arrow: return $"Arrows: {Mathf.Max(1, Cfg.ArrowsPerCraft)} a craft, for the bow and the crossbow.";
                case Item.Ram: return $"Battering Ram ({Cfg.RamUses} hit{(Cfg.RamUses == 1 ? "" : "s")}): hold LMB at an enemy piece - wood breaks, stone and up drop a step.";
                case Item.Barrier: return "High External Wall: a tall log wall for your base or out in the open.";
                case Item.Workbench:
                    return "Workbench T1: put it down anywhere in your base. The sword, crossbow, armour, chainsaw, high walls, saddle and more show up in this list (crafted in your base). Only C4 stuck right on it breaks it (it drops)."
                        + (Cfg.BenchUnlocked(team) ? "" : $" LOCKED until your team captures the ball: put it in your machine once, or keep it in your base for {Cfg.BenchUnlockSeconds:0} s"
                            + (NetGame.Instance != null ? $" ({Mathf.Min(NetGame.Instance.BallInBaseSecondsOf(team), Mathf.RoundToInt(Cfg.BenchUnlockSeconds))} / {Cfg.BenchUnlockSeconds:0} s so far)." : "."));
                case Item.Workbench2: return "Workbench T2 (needs the T1): put it down in your base for the ammo, guns, the alien helmet, C4 and more. Only C4 stuck right on it breaks it (it drops).";
                case Item.Crossbow: return $"Crossbow: {Cfg.CrossbowDamage:0} damage, faster and flatter than the bow. Reloads itself from your arrows.";
                case Item.Armor: return $"Armour: {Cfg.ArmorHp} extra health used up before your own.";
                case Item.Chainsaw: return $"Chainsaw: rips through wood and stone. {Cfg.ChainsawUses} uses.";
                case Item.Saddle: return "Saddle: E on a wild horse to ride it.";
                case Item.Boat: return "Boat: put it on open water and E to drive it.";
                case Item.Sword: return $"Sword: a slow heavy swing, {Cfg.SwordBodyDamage:0} body / {Cfg.SwordHeadDamage:0} head.";
                case Item.Shotgun: return "Waterpipe Shotgun: one shell at a time, huge up close.";
                case Item.ShotgunShell: return "One shotgun shell.";
                case Item.Revolver: return $"Revolver: {Cfg.RevolverMag} rounds, hitscan.";
                case Item.RevolverAmmo: return "One revolver bullet.";
                case Item.C4: return "C4: throw it at enemy buildings.";
                case Item.Helmet: return "Alien Helmet: goes straight on - stops one headshot completely.";
                default: return Cfg.ItemName(id) + (Cfg.PowerIndex(id) >= 0 ? ": " + Cfg.PowerBlurb(id, team) : "");
            }
        }

        // ------------------------------------------------------------------ what the mouse is over

        /// <summary>A "Name: what it does" description: the name on top (white), the rest under it (grey).</summary>
        void SetHover(string full)
        {
            int c = full.IndexOf(": ");
            if (c > 0 && c < 48) { m_HoverTitle = full.Substring(0, c); m_HoverText = full.Substring(c + 2); }
            else { m_HoverTitle = full; m_HoverText = ""; }
        }

        /// <summary>An item in the bag or a chest: its name (and how many) and what it does.</summary>
        void SetHover(ItemStack s, int team)
        {
            SetHover(CraftDescription(s.Id, team));
            if (m_HoverText == "") m_HoverText = ItemNote(s.Id);
            m_HoverTitle = Cfg.ItemName(s.Id) + (s.Count > 1 ? $"  x{s.Count}" : "");
            string state = ItemBlurb(s);
            if (s.Id != Item.Berry && state.StartsWith("  (")) m_HoverText = state.Trim() + (m_HoverText != "" ? "  " + m_HoverText : "");
        }

        /// <summary>What the things that aren't in the crafting list are for.</summary>
        static string ItemNote(Item id)
        {
            switch (id)
            {
                case Item.Wood: return "Building, crafting and upgrades all use it. Chop trees for more.";
                case Item.Stone: return "For stone upgrades and stone tools. Mine rocks for more.";
                case Item.Rock: return "Your rock: you hold it whenever your hotbar slot is empty.";
                case Item.Berry: return $"RMB to eat ({Cfg.BerryEatTime:0.#}s, +{Cfg.BerryHeal:0} HP). LMB on a horse feeds it (+{Cfg.HorseBerryHeal:0} HP).";
                case Item.Meat: return $"RMB to eat ({Cfg.MeatEatTime:0.#}s): heals you fully.";
                case Item.Dna: return "What everything costs in DNA mode. Mine trees and rocks for more.";
                case Item.PistolAmmo: return "The pistol reloads from this.";
                default: return "";
            }
        }

        /// <summary>The room the description needs under the crafting list (kept the same so the list doesn't jump).</summary>
        static float HoverInfoHeight(float k) => 104 * k;

        /// <summary>The hovered thing's name (white, bold) with its description under it (grey). False when there's nothing.</summary>
        bool DrawHoverInfo(Rect r, float k)
        {
            if (Event.current.type == EventType.Repaint) HoverShown = string.IsNullOrEmpty(m_HoverTitle) ? "" : m_HoverTitle + " | " + m_HoverText;
            if (string.IsNullOrEmpty(m_HoverTitle)) return false;
            float th = 22 * k;
            Shadowed(new Rect(r.x, r.y, r.width, th), $"<b>{m_HoverTitle}</b>", CraftStyle(16 * k, FontStyle.Bold, TextAnchor.UpperLeft, Color.white));
            if (m_HoverText != "")
                Shadowed(new Rect(r.x, r.y + th, r.width, Mathf.Max(20 * k, r.height - th)), $"<color=#c8c8c8>{m_HoverText}</color>",
                    CraftStyle(13 * k, FontStyle.Normal, TextAnchor.UpperLeft, Color.white, true));
            return true;
        }

        /// <summary>Test hook: what the hover box last showed ("name | description", "" = nothing).</summary>
        public static string HoverShown { get; private set; } = "";

        // ------------------------------------------------------------------ TAB: the crafting list

        /// <summary>Why something in the list can't be crafted right now (null = it can).</summary>
        static string ListProblem(PlayerNet me, PlayerController pc, Recipe r)
        {
            int team = me.Team.Value;
            if (r.Output == Item.Workbench && !Cfg.BenchUnlocked(team)) return "locked"; // (until the team captures the ball)
            if (!(pc.CraftOpen || Cfg.CraftAnywhere(r.Output))) return "in your base";
            if (r.Output == Item.FortifyBuff && Cfg.FortifyLevel(team) >= Cfg.MaxFortify) return "maxed out";
            if (r.Output == Item.WoodGenBuff && Cfg.WoodGenLevel(team) >= Cfg.MaxWoodGen) return "maxed out";
            if (r.Output == Item.Armor && me.ArmorHp.Value >= Cfg.ArmorHp) return "wearing it";
            if (r.Output == Item.HeavyArmor && me.ArmorHp.Value >= Cfg.HeavyArmorHp) return "wearing it";
            if (r.Output == Item.Helmet && me.HelmetHp.Value > 0) return "wearing it";
            if (Cfg.Builder && Cfg.CraftSeconds(r) > 0f && me.CraftQueue.Count >= PlayerNet.MaxCraftQueue) return "queue full";
            if (!me.CanAfford(r)) return "can't afford";
            if (BagFull(me, r)) return "bag full"; // (the server would refuse it: no green CRAFT that does nothing)
            return null;
        }

        /// <summary>No room for what this makes (the server's "Inventory full!"). Armour / fortify / wood gen don't need room,
        /// nor do Builder's timed crafts (they drop at your feet if there's no room when they're done).</summary>
        static bool BagFull(PlayerNet me, Recipe r)
        {
            var o = r.Output;
            if (o == Item.Armor || o == Item.HeavyArmor || o == Item.Helmet || o == Item.FortifyBuff || o == Item.WoodGenBuff) return false;
            if (Cfg.Builder && Cfg.CraftSeconds(r) > 0f) return false;
            int data = o == Item.Saddle ? me.Team.Value + 1 : Mathf.Clamp(Cfg.MaxData(o), 0, 255);
            return InvOps.Space(me.Inv, o, data) < r.Count && !InvOps.HasEmpty(me.Inv);
        }

        /// <summary>A short note after the price (what it does when it isn't an item you get).</summary>
        static string ListNote(Recipe r, int team)
        {
            switch (r.Output)
            {
                case Item.FortifyBuff:
                {
                    int lvl = Cfg.FortifyLevel(team);
                    return lvl >= Cfg.MaxFortify ? "" : $"walls to {Cfg.TierName(lvl + 1).ToLower()}";
                }
                case Item.WoodGenBuff:
                {
                    int lvl = Cfg.WoodGenLevel(team);
                    return lvl >= Cfg.MaxWoodGen ? "" : $"level {lvl + 1}";
                }
                default: return "";
            }
        }

        static readonly Color[] k_TierCol = { new Color(0.85f, 0.85f, 0.8f), new Color(0.45f, 1f, 0.55f), new Color(1f, 0.42f, 0.85f) };
        readonly List<int> m_TierTmp = new List<int>();

        /// <summary>
        /// Works out what the list shows (into m_CraftRows). It's always one column now (a long list scrolls), so this
        /// returns 1 (the caller still asks how many columns to lay out).
        /// </summary>
        int LayoutCraftList(PlayerNet me, PlayerController pc, float top, float bottom, float k)
        {
            int team = me.Team.Value;
            // your team's benches decide what's listed wherever you are (out of your base it's greyed: ListProblem)
            m_CraftTier = Cfg.BenchTier(team);
            m_CraftRows.Clear();
            for (int tier = 0; tier <= m_CraftTier; tier++)
            {
                m_TierTmp.Clear();
                Cfg.AddTier(m_TierTmp, tier);
                int before = m_CraftRows.Count;
                foreach (int i in m_TierTmp)
                {
                    var id = Cfg.CraftRecipe(i, team).Output;
                    if (!Tutorial.AllowsItem(id)) continue; // (the tutorial adds them one at a time)
                    if (Workbench.IsBench(id) && Workbench.ForTeam(team, Workbench.TierOfItem(id)) != null) continue; // one of each per team: already down
                    m_CraftRows.Add((i, tier));
                }
                // a heading over each tier (the basics get one too once there's more than them)
                if (m_CraftRows.Count > before && m_CraftTier > 0) m_CraftRows.Insert(before, (-1, tier));
            }
            return 1;
        }

        /// <summary>
        /// Right of the inventory: the list from LayoutCraftList, one column of width colW between top and bottom (the
        /// footer under it). When it's longer than that it scrolls: the mouse wheel over it, or the scroll bar on its right
        /// (drag the thumb, or click the track to jump there).
        /// </summary>
        void DrawCraftList(PlayerNet me, PlayerController pc, float x, float top, float colW, float colGap, float bottom, float k)
        {
            var ev = Event.current;
            int team = me.Team.Value;
            Shadowed(new Rect(x, top - 36 * k, colW, 32 * k), "<b>CRAFTING</b>", CraftStyle(20 * k, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            Shadowed(new Rect(x, top - 36 * k, colW, 32 * k), CurrencyText(me), CraftStyle(17 * k, FontStyle.Bold, TextAnchor.MiddleRight, Color.white));

            // the footer: the name and description of whatever the mouse is over (a row here, or an item in the bag)
            float footer = HoverInfoHeight(k);

            // the rows: fixed heights (the size they were when everything fitted)
            float headH = 24 * k, gap = 4 * k, row = 50 * k;
            float contentH = 0f;
            foreach (var r in m_CraftRows) contentH += r.Idx < 0 ? headH : row + gap;
            var view = new Rect(x, top, colW, Mathf.Max(row, bottom - top - footer));
            float max = Mathf.Max(0f, contentH - view.height);
            bool bar = max > 0.5f;
            float barW = 14 * k, barGap = 6 * k;
            float rowW = bar ? colW - barW - barGap : colW;

            // the bag opens at the top; the scroll stays in range as the list changes
            if (Time.frameCount - m_CraftDrawnFrame > 2) { m_CraftScroll = 0f; m_ThumbDrag = false; }
            m_CraftDrawnFrame = Time.frameCount;
            if (TestScrollTo >= 0f && ev.type == EventType.Repaint) { m_CraftScroll = TestScrollTo; TestScrollTo = -1f; }
            bool overList = view.Contains(ev.mousePosition);
            if (ev.type == EventType.ScrollWheel && overList && bar)
            {
                // (one notch is about a row; with Shift held - sprinting - Windows turns the wheel sideways, so x counts too)
                float d = Mathf.Abs(ev.delta.y) > 0.001f ? ev.delta.y : ev.delta.x;
                m_CraftScroll += d * 18f * k;
                m_WheelFrame = Time.frameCount;
                ev.Use();
            }
            // (and if no wheel event got here this frame - a held key swallowed it - the wheel itself still scrolls the list)
            else if (ev.type == EventType.Repaint && overList && bar && m_WheelFrame != Time.frameCount)
            {
                var w = Input.mouseScrollDelta;
                float d = Mathf.Abs(w.y) > 0.001f ? w.y : w.x;
                if (d != 0f) m_CraftScroll -= d * 3f * 18f * k; // (Input: +1 a notch up; the event: +3 a notch down)
            }

            // the scroll bar: a track as tall as the list and a thumb as long as the share of it you can see
            var track = new Rect(x + colW - barW, top, barW, view.height);
            float thumbH = bar ? Mathf.Clamp(view.height * view.height / Mathf.Max(1f, contentH), 30 * k, view.height) : view.height;
            if (bar)
            {
                if (ev.type == EventType.MouseDown && ev.button == 0 && track.Contains(ev.mousePosition))
                {
                    float thumbY0 = top + (view.height - thumbH) * Mathf.Clamp01(m_CraftScroll / max);
                    // on the thumb: hold it where you grabbed it; on the track: jump so the thumb's middle is under the mouse
                    bool onThumb = ev.mousePosition.y >= thumbY0 && ev.mousePosition.y <= thumbY0 + thumbH;
                    m_ThumbGrab = onThumb ? ev.mousePosition.y - thumbY0 : thumbH * 0.5f;
                    m_ThumbDrag = true;
                    m_CraftScroll = Mathf.Clamp01((ev.mousePosition.y - m_ThumbGrab - top) / Mathf.Max(1f, view.height - thumbH)) * max;
                    ev.Use();
                }
                else if (m_ThumbDrag && ev.type == EventType.MouseDrag)
                {
                    m_CraftScroll = Mathf.Clamp01((ev.mousePosition.y - m_ThumbGrab - top) / Mathf.Max(1f, view.height - thumbH)) * max;
                    ev.Use();
                }
                else if (m_ThumbDrag && ev.rawType == EventType.MouseUp) m_ThumbDrag = false;
            }
            else m_ThumbDrag = false;
            m_CraftScroll = Mathf.Clamp(m_CraftScroll, 0f, max);
            CraftScrollMax = max;
            CraftScrollNow = m_CraftScroll;

            // the rows, clipped to the view and moved up by the scroll (only what's in view is drawn, and the buttons only
            // take clicks while the mouse is over the list)
            if (ev.type == EventType.Repaint) CraftRowsShown.Clear();
            GUI.BeginClip(view);
            float y = -m_CraftScroll;
            for (int i = 0; i < m_CraftRows.Count; i++)
            {
                var (idx, tier) = m_CraftRows[i];
                float h = idx < 0 ? headH : row;
                bool visible = y + h > 0f && y < view.height;
                if (idx < 0)
                {
                    if (visible)
                    {
                        // the heading: WORKBENCH T1 / T2 (or BASICS) with a line in its colour
                        var col = k_TierCol[Mathf.Clamp(tier, 0, 2)];
                        string title = tier == 0 ? "BASICS" : $"WORKBENCH T{tier}";
                        Shadowed(new Rect(2 * k, y, rowW, headH - 3 * k), $"<b>{title}</b>", CraftStyle(Mathf.Min(15 * k, headH * 0.7f), FontStyle.Bold, TextAnchor.MiddleLeft, col));
                        Fill(new Rect(0, y + headH - 4 * k, rowW, 2 * k), new Color(col.r, col.g, col.b, 0.6f));
                    }
                    y += headH;
                }
                else
                {
                    if (visible) DrawCraftRow(me, pc, new Rect(0, y, rowW, row), idx, tier, k, ev, overList);
                    else if (ev.type == EventType.Repaint) { var rec = Cfg.CraftRecipe(idx, team); CraftRowsShown.Add((rec.Output, ListProblem(me, pc, rec))); }
                    y += row + gap;
                }
            }
            GUI.EndClip();
            if (bar)
            {
                // the scroll bar, right of the rows
                float thumbY = top + (view.height - thumbH) * (m_CraftScroll / max);
                bool hot = m_ThumbDrag || track.Contains(ev.mousePosition);
                Fill(track, new Color(0, 0, 0, 0.5f));
                Fill(new Rect(track.x + 2 * k, thumbY + 2 * k, track.width - 4 * k, thumbH - 4 * k), hot ? new Color(1f, 0.93f, 0.7f, 0.95f) : new Color(0.78f, 0.78f, 0.74f, 0.85f));
                // a thin line at an edge that has more past it
                if (m_CraftScroll > 1f) Fill(new Rect(x, top, rowW, 2 * k), new Color(1, 1, 1, 0.4f));
                if (m_CraftScroll < max - 1f) Fill(new Rect(x, top + view.height - 2 * k, rowW, 2 * k), new Color(1, 1, 1, 0.4f));
            }

            // (right under the rows when they all fit, at the bottom when the list scrolls)
            DrawHoverInfo(new Rect(x, top + Mathf.Min(view.height, contentH) + 6 * k, colW, footer), k);
        }

        /// <summary>One row: icon, name, price (red when you're short) and a CRAFT button (green when you can make it).
        /// `canClick`: the mouse is over the list (rows scrolled out of view never take clicks).</summary>
        void DrawCraftRow(PlayerNet me, PlayerController pc, Rect rr, int idx, int tier, float k, Event ev, bool canClick)
        {
            int team = me.Team.Value;
            var rec = Cfg.CraftRecipe(idx, team);
            float row = rr.height;
            string problem = ListProblem(me, pc, rec);
            if (ev.type == EventType.Repaint) CraftRowsShown.Add((rec.Output, problem));
            bool locked = problem == "locked";
            bool ok = problem == null, here = problem != "in your base" && !locked;
            bool over = canClick && rr.Contains(ev.mousePosition);
            Fill(rr, ok ? k_RowOk : k_RowNo);
            if (over) { Fill(rr, new Color(1, 1, 1, 0.06f)); SetHover(CraftDescription(rec.Output, team)); }
            var edge = locked ? new Color(0.35f, 0.35f, 0.34f) : tier > 0 ? k_TierCol[tier] : ok ? new Color(0.5f, 0.9f, 0.3f) : new Color(0.4f, 0.4f, 0.38f);
            if (tier > 0 && !ok) edge *= 0.6f;
            Fill(new Rect(rr.x, rr.y, 4 * k, rr.height), edge);
            // the icon
            float pad = Mathf.Min(4 * k, row * 0.08f);
            var ib = new Rect(rr.x + 8 * k, rr.y + pad, row - 2 * pad, row - 2 * pad);
            Fill(ib, new Color(0, 0, 0, 0.3f));
            Icon(new Rect(ib.x + 2, ib.y + 2, ib.width - 4, ib.height - 4), rec.Output, here ? 1f : locked ? 0.3f : 0.45f);
            // name and price
            float bw = Mathf.Min(104 * k, rr.width * 0.24f);
            float tx = ib.xMax + 8 * k, tw = rr.xMax - bw - 10 * k - tx;
            string nm = Cfg.ItemName(rec.Output) + (rec.Count > 1 ? $" <color=#bbbbbb>x{rec.Count}</color>" : "");
            GUI.Label(new Rect(tx, rr.y + row * 0.06f, tw, row * 0.48f), nm,
                FitStyle(nm, tw, Mathf.Min(19 * k, row * 0.36f), FontStyle.Bold, TextAnchor.MiddleLeft, here ? Color.white : new Color(0.6f, 0.6f, 0.6f)));
            string sub = CostColored(me, rec);
            string note = ListNote(rec, team);
            if (note != "") sub += $"  <color=#a8a8a0>{note}</color>";
            if (Cfg.Builder && Cfg.CraftSeconds(rec) > 0f) sub += $"  <color=#bbbbbb>{Cfg.CraftSeconds(rec):0}s</color>";
            if (locked) sub = $"<color=#b4b4b4>{Cfg.BenchLockedText}</color>"; // (grey: the ball hasn't been captured yet)
            else if (!here) sub += "  <color=#8fb8ff>in your base</color>";
            else if (problem == "queue full" || problem == "maxed out" || problem == "wearing it" || problem == "bag full") sub = $"<color=#ffd24a>{problem}</color>";
            var subStyle = locked ? FitStyle(Cfg.BenchLockedText, tw, Mathf.Min(16 * k, row * 0.3f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white)
                : CraftStyle(Mathf.Min(16 * k, row * 0.3f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            GUI.Label(new Rect(tx, rr.y + row * 0.52f, tw, row * 0.42f), sub, subStyle);
            // CRAFT (LOCKED on the workbench until the ball's been captured)
            float bp = Mathf.Max(3 * k, row * 0.14f);
            var br = new Rect(rr.xMax - bw - 6 * k, rr.y + bp, bw, row - 2 * bp);
            if (FlatBtn(br, ok ? k_BtnOk : k_BtnNo, k_BtnOkHi, ok && canClick)) me.CraftRpc(idx);
            GUI.Label(br, locked ? "LOCKED" : "CRAFT", CraftStyle(Mathf.Min(17 * k, row * 0.36f), FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(1, 1, 1, 0.35f)));
        }
    }
}
