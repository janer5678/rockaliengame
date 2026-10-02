using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Crafting: TAB shows the inventory with the crafting list on its right, one row per item (icon, name, price, CRAFT).
    /// The starter items are always there; while you're in your base, a Workbench T1 your team has put down adds the T1
    /// items under their own heading, and a Workbench T2 the T2 items (Cfg.CraftTierAt). When the list gets long it goes
    /// into two columns with smaller rows, so it always fits on the screen.
    /// </summary>
    public partial class Hud
    {
        /// <summary>The list as drawn: a heading (Idx -1, its Tier) or an item (craft number Idx).</summary>
        readonly List<(int Idx, int Tier)> m_CraftRows = new List<(int Idx, int Tier)>();
        int m_CraftCols = 1, m_CraftTier;

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
            $"<color=#f0b878>{Thousands(me.Count(Item.Wood))} wood</color>" + (Cfg.WoodMode ? "" : $"   <color=#d4d4dc>{Thousands(me.Count(Item.Stone))} stone</color>");

        /// <summary>A short description of everything you can make (shown when you point at it).</summary>
        static string CraftDescription(Item id, int team)
        {
            switch (id)
            {
                case Item.Hatchet: return "Stone Hatchet: chops trees much faster than your rock. Can be crafted anywhere.";
                case Item.Pickaxe: return "Stone Pickaxe: mines rocks for stone much faster than your rock.";
                case Item.Spear: return "Spear: LMB stabs; hold RMB and press LMB to throw it (E picks it back up). Can be crafted anywhere.";
                case Item.BuildingPlan: return "Building Plan: hold it to build. Hold RMB for the building wheel.";
                case Item.Chest: return "Storage Chest: place it in your base, E opens it (14 slots).";
                case Item.Bow: return "Bow: hold LMB to draw, let go to fire. Uses arrows.";
                case Item.Arrow: return $"Arrows: {Mathf.Max(1, Cfg.ArrowsPerCraft)} a craft, for the bow and the crossbow.";
                case Item.Ram: return $"Battering Ram ({Cfg.RamUses} hit{(Cfg.RamUses == 1 ? "" : "s")}): hold LMB at an enemy piece - wood breaks, stone and up drop a step.";
                case Item.Barrier: return "High External Wall: a tall log wall for your base or out in the open.";
                case Item.Workbench: return "Workbench T1: put it down anywhere in your base. While you're in your base, the crossbow, armour, chainsaw, high walls and more show up in this list. Can't be broken.";
                case Item.Workbench2: return "Workbench T2 (needs the T1): put it down in your base for the guns, ammo, C4, the saddle, the helmet and more. Can't be broken.";
                case Item.Crossbow: return $"Crossbow: {Cfg.CrossbowDamage:0} damage, faster and flatter than the bow. Reloads itself from your arrows.";
                case Item.Armor: return $"Armour: goes straight on - {Cfg.ArmorHp} extra health used up before your own.";
                case Item.Chainsaw: return $"Chainsaw: rips through wood and stone. {Cfg.ChainsawUses} uses.";
                case Item.Saddle: return "Saddle: E on a wild horse to ride it.";
                case Item.Boat: return "Boat: put it on open water and E to drive it.";
                case Item.Sword: return $"Sword: a slow heavy swing, {Cfg.SwordBodyDamage:0} body / {Cfg.SwordHeadDamage:0} head.";
                case Item.Shotgun: return "Waterpipe Shotgun: one shell at a time, huge up close. Comes empty.";
                case Item.ShotgunShell: return "One shotgun shell.";
                case Item.Revolver: return $"Revolver: {Cfg.RevolverMag} rounds, hitscan. Comes empty.";
                case Item.RevolverAmmo: return "One revolver bullet.";
                case Item.C4: return "C4: throw it at enemy buildings.";
                case Item.Helmet: return "Alien Helmet: stops one headshot completely.";
                default: return Cfg.ItemName(id) + (Cfg.PowerIndex(id) >= 0 ? ": " + Cfg.PowerBlurb(id, team) : "");
            }
        }

        // ------------------------------------------------------------------ TAB: the crafting list

        /// <summary>Why something in the list can't be crafted right now (null = it can).</summary>
        static string ListProblem(PlayerNet me, PlayerController pc, Recipe r)
        {
            int team = me.Team.Value;
            if (!(pc.CraftOpen || Cfg.CraftAnywhere(r.Output))) return "in your base";
            if (r.Output == Item.FortifyBuff && Cfg.FortifyLevel(team) >= Cfg.MaxFortify) return "maxed out";
            if (r.Output == Item.WoodGenBuff && Cfg.WoodGenLevel(team) >= Cfg.MaxWoodGen) return "maxed out";
            if (r.Output == Item.Armor && me.ArmorHp.Value >= Cfg.ArmorHp) return "wearing it";
            if (r.Output == Item.HeavyArmor && me.ArmorHp.Value >= Cfg.HeavyArmorHp) return "wearing it";
            if (Cfg.Builder && Cfg.CraftSeconds(r) > 0f && me.CraftQueue.Count >= PlayerNet.MaxCraftQueue) return "queue full";
            if (!me.CanAfford(r)) return "can't afford";
            return null;
        }

        /// <summary>A short note after the price (what it does when it isn't an item you get).</summary>
        static string ListNote(Recipe r, int team)
        {
            switch (r.Output)
            {
                case Item.Armor: return "goes straight on";
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
                case Item.Shotgun:
                case Item.Revolver: return "comes empty";
                default: return "";
            }
        }

        static readonly Color[] k_TierCol = { new Color(0.85f, 0.85f, 0.8f), new Color(0.45f, 1f, 0.55f), new Color(1f, 0.42f, 0.85f) };
        readonly List<int> m_TierTmp = new List<int>();

        /// <summary>
        /// Works out what the list shows (into m_CraftRows) and how many columns it needs to fit between top and bottom.
        /// Returns the column count (1 or 2).
        /// </summary>
        int LayoutCraftList(PlayerNet me, PlayerController pc, float top, float bottom, float k)
        {
            int team = me.Team.Value;
            m_CraftTier = Cfg.CraftTierAt(team, me.transform.position);
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
            float avail = bottom - top - 44 * k;
            int items = 0, heads = 0;
            foreach (var r in m_CraftRows) if (r.Idx < 0) heads++; else items++;
            float need = items * (50 * k + 4 * k) + heads * 24 * k;
            m_CraftCols = need > avail && items > 6 ? 2 : 1;
            return m_CraftCols;
        }

        /// <summary>Right of the inventory: the list from LayoutCraftList in one or two columns of width colW.</summary>
        void DrawCraftList(PlayerNet me, PlayerController pc, float x, float top, float colW, float colGap, float bottom, float k)
        {
            var ev = Event.current;
            int team = me.Team.Value;
            int cols = m_CraftCols;
            float fullW = cols * colW + (cols - 1) * colGap;
            Shadowed(new Rect(x, top - 36 * k, fullW, 32 * k), "<b>CRAFTING</b>", CraftStyle(20 * k, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            Shadowed(new Rect(x, top - 36 * k, fullW, 32 * k), CurrencyText(me), CraftStyle(17 * k, FontStyle.Bold, TextAnchor.MiddleRight, Color.white));

            // split into columns: by height, about half each (a heading never ends a column)
            float headH = 24 * k, gap = 4 * k, footer = 44 * k;
            int n = m_CraftRows.Count, split = n;
            if (cols == 2)
            {
                float total = 0f;
                foreach (var r in m_CraftRows) total += r.Idx < 0 ? 1f : 2.2f;
                float run = 0f;
                for (int i = 0; i < n; i++)
                {
                    run += m_CraftRows[i].Idx < 0 ? 1f : 2.2f;
                    if (run >= total * 0.5f) { split = i + 1; break; }
                }
                while (split > 0 && split < n && m_CraftRows[split - 1].Idx < 0) split--;
            }
            // one row height for both columns: as big as fits the taller one
            float rowsH = bottom - top - footer;
            int itemsA = 0, headsA = 0, itemsB = 0, headsB = 0;
            for (int i = 0; i < n; i++)
            {
                bool a = i < split, h = m_CraftRows[i].Idx < 0;
                if (a) { if (h) headsA++; else itemsA++; } else { if (h) headsB++; else itemsB++; }
            }
            float rowA = itemsA > 0 ? (rowsH - headsA * headH) / itemsA - gap : 999f;
            float rowB = itemsB > 0 ? (rowsH - headsB * headH) / itemsB - gap : 999f;
            float row = Mathf.Clamp(Mathf.Min(rowA, rowB), 26 * k, 66 * k);

            float yA = top, yB = top, colBottom = top;
            for (int i = 0; i < n; i++)
            {
                bool colA = i < split;
                float cx = colA ? x : x + colW + colGap;
                float y = colA ? yA : yB;
                var (idx, tier) = m_CraftRows[i];
                if (idx < 0)
                {
                    // the heading: WORKBENCH T1 / T2 (or BASICS) with a line in its colour
                    var col = k_TierCol[Mathf.Clamp(tier, 0, 2)];
                    string title = tier == 0 ? "BASICS" : $"WORKBENCH T{tier}";
                    Shadowed(new Rect(cx + 2 * k, y, colW, headH - 3 * k), $"<b>{title}</b>", CraftStyle(Mathf.Min(15 * k, headH * 0.7f), FontStyle.Bold, TextAnchor.MiddleLeft, col));
                    Fill(new Rect(cx, y + headH - 4 * k, colW, 2 * k), new Color(col.r, col.g, col.b, 0.6f));
                    y += headH;
                }
                else
                {
                    DrawCraftRow(me, pc, new Rect(cx, y, colW, row), idx, tier, k, ev);
                    y += row + gap;
                }
                if (colA) yA = y; else yB = y;
                colBottom = Mathf.Max(colBottom, y);
            }

            // the footer: where more comes from
            string hint = Cfg.Builder ? "BUILDER: craft anywhere, one thing at a time - each takes a few seconds."
                : "Spears and hatchets can be crafted anywhere; the rest inside your base.";
            int benchTier = Cfg.BenchTier(team);
            bool benches = Cfg.RecipeIndex(Item.Workbench) >= 0 && Tutorial.AllowsItem(Item.Workbench);
            if (benches)
            {
                if (benchTier > m_CraftTier) hint += $"\n<color=#8fb8ff>Go back to your base for your Workbench T{benchTier} items.</color>";
                else if (benchTier == 0) hint += "\n<color=#8dff9a>Put a <b>Workbench T1</b> down in your base: more things show up here while you're in your base.</color>";
                else if (benchTier == 1 && Tutorial.AllowsItem(Item.Workbench2)) hint += "\n<color=#ff9ae0>A <b>Workbench T2</b> adds the guns, C4, the saddle and more.</color>";
            }
            Shadowed(new Rect(x, colBottom + 2 * k, fullW, footer + 10 * k), hint, CraftStyle(13 * k, FontStyle.Normal, TextAnchor.UpperLeft, Color.white, true));
        }

        /// <summary>One row: icon, name, price (red when you're short) and a CRAFT button (green when you can make it).</summary>
        void DrawCraftRow(PlayerNet me, PlayerController pc, Rect rr, int idx, int tier, float k, Event ev)
        {
            int team = me.Team.Value;
            var rec = Cfg.CraftRecipe(idx, team);
            float row = rr.height;
            string problem = ListProblem(me, pc, rec);
            bool ok = problem == null, here = problem != "in your base";
            bool over = rr.Contains(ev.mousePosition);
            Fill(rr, ok ? k_RowOk : k_RowNo);
            if (over) { Fill(rr, new Color(1, 1, 1, 0.06f)); m_HoverName = CraftDescription(rec.Output, team); }
            var edge = tier > 0 ? k_TierCol[tier] : ok ? new Color(0.5f, 0.9f, 0.3f) : new Color(0.4f, 0.4f, 0.38f);
            if (tier > 0 && !ok) edge *= 0.6f;
            Fill(new Rect(rr.x, rr.y, 4 * k, rr.height), edge);
            // the icon
            float pad = Mathf.Min(4 * k, row * 0.08f);
            var ib = new Rect(rr.x + 8 * k, rr.y + pad, row - 2 * pad, row - 2 * pad);
            Fill(ib, new Color(0, 0, 0, 0.3f));
            Icon(new Rect(ib.x + 2, ib.y + 2, ib.width - 4, ib.height - 4), rec.Output, here ? 1f : 0.45f);
            // name and price
            float bw = Mathf.Min(104 * k, rr.width * 0.24f);
            float tx = ib.xMax + 8 * k, tw = rr.xMax - bw - 10 * k - tx;
            string nm = Cfg.ItemName(rec.Output) + (rec.Count > 1 ? $" <color=#bbbbbb>x{rec.Count}</color>" : "");
            GUI.Label(new Rect(tx, rr.y + row * 0.06f, tw, row * 0.48f), nm,
                FitStyle(nm, tw, Mathf.Min(19 * k, row * 0.36f), FontStyle.Bold, TextAnchor.MiddleLeft, here ? Color.white : new Color(0.65f, 0.65f, 0.65f)));
            string sub = CostColored(me, rec);
            string note = ListNote(rec, team);
            if (note != "") sub += $"  <color=#a8a8a0>{note}</color>";
            if (Cfg.Builder && Cfg.CraftSeconds(rec) > 0f) sub += $"  <color=#bbbbbb>{Cfg.CraftSeconds(rec):0}s</color>";
            if (!here) sub += "  <color=#8fb8ff>in your base</color>";
            else if (problem == "queue full" || problem == "maxed out" || problem == "wearing it") sub = $"<color=#ffd24a>{problem}</color>";
            GUI.Label(new Rect(tx, rr.y + row * 0.52f, tw, row * 0.42f), sub, CraftStyle(Mathf.Min(16 * k, row * 0.3f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            // CRAFT
            float bp = Mathf.Max(3 * k, row * 0.14f);
            var br = new Rect(rr.xMax - bw - 6 * k, rr.y + bp, bw, row - 2 * bp);
            if (FlatBtn(br, ok ? k_BtnOk : k_BtnNo, k_BtnOkHi, ok)) me.CraftRpc(idx);
            GUI.Label(br, "CRAFT", CraftStyle(Mathf.Min(17 * k, row * 0.36f), FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(1, 1, 1, 0.35f)));
        }
    }
}
