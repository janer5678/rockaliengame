using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Crafting, in two places:
    /// - TAB: the inventory with the crafting list on its right (the old layout, with bigger, clearer rows). It only makes
    ///   the starter items (Cfg.StarterOrder), one CRAFT button each.
    /// - The workbench (E on it): a full-screen shop with no inventory - just what you've got to spend and a big tile for
    ///   everything else, kept together by kind (weapons, ammo, armour, tools, riding, base upgrades) without headings.
    ///   Clicking one closes it and the bench makes it (Workbench.cs).
    /// </summary>
    public partial class Hud
    {
        readonly List<int> m_BenchList = new List<int>();

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

        /// <summary>What you have to spend (the TAB header and the workbench).</summary>
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
                case Item.Workbench: return "Workbench: put it on the metal floor in your base and press E on it to make everything else. Can't be broken.";
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

        // ------------------------------------------------------------------ TAB: the crafting list (starter items)

        /// <summary>Why a starter item can't be crafted from the inventory right now (null = it can).</summary>
        static string ListProblem(PlayerNet me, PlayerController pc, Recipe r)
        {
            if (!(pc.CraftOpen || Cfg.CraftAnywhere(r.Output))) return "in your base";
            if (Cfg.Builder && Cfg.CraftSeconds(r) > 0f && me.CraftQueue.Count >= PlayerNet.MaxCraftQueue) return "queue full";
            if (!me.CanAfford(r)) return "can't afford";
            return null;
        }

        /// <summary>Right of the inventory: one big row per starter item - icon, name, price and a CRAFT button.</summary>
        void DrawCraftList(PlayerNet me, PlayerController pc, float x, float top, float w, float bottom, float k)
        {
            var ev = Event.current;
            Shadowed(new Rect(x, top - 36 * k, w, 32 * k), "<b>CRAFTING</b>", CraftStyle(20 * k, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            Shadowed(new Rect(x, top - 36 * k, w, 32 * k), CurrencyText(me), CraftStyle(17 * k, FontStyle.Bold, TextAnchor.MiddleRight, Color.white));
            var ids = new List<(int idx, Recipe r)>();
            foreach (var id in Cfg.StarterOrder)
            {
                int i = Cfg.RecipeIndex(id);
                if (i >= 0 && Tutorial.AllowsItem(id)) ids.Add((i, Cfg.GetRecipe(i))); // (the tutorial adds them one at a time)
            }
            float footer = 44 * k, gap = 5 * k;
            float row = Mathf.Clamp((bottom - top - footer) / Mathf.Max(1, ids.Count) - gap, 34 * k, 76 * k);
            for (int n = 0; n < ids.Count; n++)
            {
                var (idx, rec) = ids[n];
                var rr = new Rect(x, top + n * (row + gap), w, row);
                string problem = ListProblem(me, pc, rec);
                bool ok = problem == null, here = problem != "in your base";
                bool over = rr.Contains(ev.mousePosition);
                Fill(rr, ok ? k_RowOk : k_RowNo);
                if (over) { Fill(rr, new Color(1, 1, 1, 0.06f)); m_HoverName = CraftDescription(rec.Output, me.Team.Value); }
                Fill(new Rect(rr.x, rr.y, 4 * k, rr.height), ok ? new Color(0.5f, 0.9f, 0.3f) : new Color(0.4f, 0.4f, 0.38f));
                // the icon, big
                var ib = new Rect(rr.x + 8 * k, rr.y + 4 * k, row - 8 * k, row - 8 * k);
                Fill(ib, new Color(0, 0, 0, 0.3f));
                Icon(new Rect(ib.x + 2, ib.y + 2, ib.width - 4, ib.height - 4), rec.Output, here ? 1f : 0.45f);
                if (rec.Count > 1) GUI.Label(new Rect(ib.x, ib.yMax - 18 * k, ib.width - 3 * k, 16 * k), "x" + rec.Count, CraftStyle(13 * k, FontStyle.Bold, TextAnchor.LowerRight, Color.white));
                // name and price
                float tx = ib.xMax + 10 * k, bw = 112 * k, tw = rr.xMax - bw - 10 * k - tx;
                float nameSize = Mathf.Min(20 * k, row * 0.34f), costSize = Mathf.Min(17 * k, row * 0.29f);
                GUI.Label(new Rect(tx, rr.y + row * 0.1f, tw, row * 0.45f), Cfg.ItemName(rec.Output) + (rec.Count > 1 ? $" <color=#bbbbbb>x{rec.Count}</color>" : ""),
                    CraftStyle(nameSize, FontStyle.Bold, TextAnchor.MiddleLeft, here ? Color.white : new Color(0.65f, 0.65f, 0.65f)));
                string sub = CostColored(me, rec);
                if (Cfg.Builder && Cfg.CraftSeconds(rec) > 0f) sub += $"   <color=#bbbbbb>{Cfg.CraftSeconds(rec):0}s</color>";
                if (!here) sub += "   <color=#8fb8ff>in your base</color>";
                else if (problem == "queue full") sub += "   <color=#ff8a7a>queue full</color>";
                GUI.Label(new Rect(tx, rr.y + row * 0.5f, tw, row * 0.42f), sub, CraftStyle(costSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
                // CRAFT
                var br = new Rect(rr.xMax - bw - 8 * k, rr.y + Mathf.Max(5 * k, row * 0.16f), bw, row - 2 * Mathf.Max(5 * k, row * 0.16f));
                if (FlatBtn(br, ok ? k_BtnOk : k_BtnNo, k_BtnOkHi, ok)) me.CraftRpc(idx);
                GUI.Label(br, "CRAFT", CraftStyle(Mathf.Min(18 * k, row * 0.32f), FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(1, 1, 1, 0.35f)));
            }
            float fy = top + ids.Count * (row + gap) + 2 * k;
            string hint = Cfg.Builder ? "BUILDER: craft anywhere, one thing at a time - each takes a few seconds."
                : "Spears and hatchets can be crafted anywhere; the rest inside your base.";
            if (Cfg.RecipeIndex(Item.Workbench) >= 0 && Tutorial.AllowsItem(Item.Workbench))
                hint += $"\n<color=#8dff9a>Everything else is made at a <b>Workbench</b>: put it on your base's metal floor and press {Binds.Name(Bind.Interact)} on it.</color>";
            Shadowed(new Rect(x, fy, w, footer + 10 * k), hint, CraftStyle(13 * k, FontStyle.Normal, TextAnchor.UpperLeft, Color.white, true));
        }

        // ------------------------------------------------------------------ the workbench shop

        /// <summary>Why this can't be bought at the workbench right now (null = it can).</summary>
        static string BenchProblem(PlayerNet me, Recipe r, Container bench)
        {
            int team = me.Team.Value;
            if (r.Output == Item.FortifyBuff && Cfg.FortifyLevel(team) >= Cfg.MaxFortify) return "MAXED";
            if (r.Output == Item.WoodGenBuff && Cfg.WoodGenLevel(team) >= Cfg.MaxWoodGen) return "MAXED";
            if (r.Output == Item.Armor && me.ArmorHp.Value >= Cfg.ArmorHp) return "WEARING IT";
            if (r.Output == Item.HeavyArmor && me.ArmorHp.Value >= Cfg.HeavyArmorHp) return "WEARING IT";
            if (!me.CanAfford(r)) return "NEED MORE";
            var w = Workbench.Of(bench);
            if (!Cfg.BenchNoItem(r.Output) && w != null && w.Working) return "BUSY";
            return null;
        }

        /// <summary>A small line under the price: what it does when it's not an item you pick up.</summary>
        static string BenchNote(Recipe r, int team)
        {
            switch (r.Output)
            {
                case Item.Armor: return $"goes straight on: +{Cfg.ArmorHp} health";
                case Item.HeavyArmor: return $"goes straight on: +{Cfg.HeavyArmorHp} health";
                case Item.FortifyBuff:
                {
                    int lvl = Cfg.FortifyLevel(team);
                    return lvl >= Cfg.MaxFortify ? "all your walls are refined" : $"all your walls to {Cfg.TierName(lvl + 1).ToLower()}";
                }
                case Item.WoodGenBuff:
                {
                    int lvl = Cfg.WoodGenLevel(team);
                    return lvl >= Cfg.MaxWoodGen ? $"{Cfg.WoodGenRate(lvl)} wood a second" : $"level {lvl + 1}: {Cfg.WoodGenRate(lvl + 1)} wood a second";
                }
                case Item.Shotgun:
                case Item.Revolver: return "comes empty";
                default: return r.Count > 1 ? $"x{r.Count}" : "";
            }
        }

        /// <summary>E on your workbench: the whole screen is the shop. Click a tile to buy it (the menu closes and the bench makes it).</summary>
        void DrawWorkbenchMenu(PlayerNet me, PlayerController pc, Container bench)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            var ev = Event.current;
            int team = me.Team.Value;
            Fill(new Rect(0, 0, sw, sh), new Color(0.03f, 0.045f, 0.03f, 0.9f));
            Fill(new Rect(0, 0, sw, sh * 0.2f), new Color(0.28f, 0.18f, 0.09f, 0.35f));
            Fill(new Rect(0, sh * 0.2f - 3 * k, sw, 3 * k), new Color(0.45f, 1f, 0.55f, 0.55f));

            // the title, and what you have to spend - big
            Shadowed(new Rect(0, sh * 0.025f, sw, 50 * k), "WORKBENCH", CraftStyle(42 * k, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.72f, 1f, 0.74f)));
            var cur = Cfg.CurrencyItem;
            string have = Thousands(me.Count(cur)) + " " + (Cfg.DnaRules ? "DNA" : "WOOD");
            var hs = CraftStyle(34 * k, FontStyle.Bold, TextAnchor.MiddleLeft, Cfg.DnaRules ? new Color(0.5f, 1f, 0.7f) : new Color(0.95f, 0.74f, 0.47f));
            float hw = hs.CalcSize(new GUIContent(have)).x, isz = 48 * k;
            float hx = (sw - (isz + 10 * k + hw)) / 2f, hy = sh * 0.025f + 54 * k;
            Icon(new Rect(hx, hy, isz, isz), cur);
            Shadowed(new Rect(hx + isz + 10 * k, hy, hw + 10, isz), have, hs);

            Cfg.BenchItems(m_BenchList);
            bool needStone = false;
            foreach (var i in m_BenchList) needStone |= Cfg.CraftRecipe(i, team).Stone > 0;
            if (needStone)
                Shadowed(new Rect(0, hy + isz, sw, 24 * k), $"<color=#d4d4dc>and {Thousands(me.Count(Item.Stone))} stone</color>", CraftStyle(17 * k, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white));

            var wb = Workbench.Of(bench);
            bool busy = wb != null && wb.Working;

            // the tiles: as big as fit, in rows, kept in order (weapons, ammo, armour, tools, riding, base upgrades)
            int n = m_BenchList.Count;
            var area = new Rect(sw * 0.04f, sh * 0.2f + 16 * k, sw * 0.92f, sh * 0.8f - 16 * k - 52 * k);
            if (n == 0)
            {
                GUI.Label(area, "Nothing to make here in this mode", CraftStyle(22 * k, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white));
            }
            int cols = 1;
            float tile = 0f;
            for (int c = 1; c <= Mathf.Max(1, n); c++)
            {
                int rows = Mathf.CeilToInt(n / (float)c);
                float t = Mathf.Min(area.width / c, area.height / Mathf.Max(1, rows) * 1.05f);
                if (t > tile) { tile = t; cols = c; }
            }
            float gap = 10 * k;
            float tw = Mathf.Min(tile, 260 * k) - gap, th = tw / 1.05f;
            int rowsN = Mathf.CeilToInt(n / (float)cols);
            float gx = area.x + (area.width - cols * (tw + gap) + gap) / 2f;
            float gy = area.y + Mathf.Max(0f, (area.height - rowsN * (th + gap) + gap) / 2f);
            string tip = null;
            for (int j = 0; j < n; j++)
            {
                int idx = m_BenchList[j];
                var rec = Cfg.CraftRecipe(idx, team);
                string problem = BenchProblem(me, rec, bench);
                bool ok = problem == null;
                var r = new Rect(gx + (j % cols) * (tw + gap), gy + (j / cols) * (th + gap), tw, th);
                bool over = r.Contains(ev.mousePosition);
                Fill(r, ok ? new Color(0.16f, 0.13f, 0.08f, 0.95f) : new Color(0.1f, 0.1f, 0.1f, 0.9f));
                if (over) { Fill(r, new Color(1, 1, 1, ok ? 0.1f : 0.04f)); tip = CraftDescription(rec.Output, team); }
                // a glowing edge on what you can buy
                var edge = ok ? new Color(0.45f, 1f, 0.55f, over ? 1f : 0.55f) : new Color(1, 1, 1, 0.12f);
                Fill(new Rect(r.x, r.y, r.width, 2 * k), edge);
                Fill(new Rect(r.x, r.yMax - 2 * k, r.width, 2 * k), edge);
                Fill(new Rect(r.x, r.y, 2 * k, r.height), edge);
                Fill(new Rect(r.xMax - 2 * k, r.y, 2 * k, r.height), edge);

                float pad = tw * 0.06f;
                Icon(new Rect(r.x + tw * 0.18f, r.y + pad, tw * 0.64f, th * 0.52f), rec.Output, ok ? 1f : 0.4f);
                string nm = Cfg.ItemName(rec.Output), cost = CostColored(me, rec);
                GUI.Label(new Rect(r.x + pad, r.y + th * 0.56f, tw - 2 * pad, th * 0.15f), nm,
                    FitStyle(nm, tw - 2 * pad, Mathf.Min(21 * k, tw * 0.1f), FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(0.7f, 0.7f, 0.7f)));
                GUI.Label(new Rect(r.x + pad, r.y + th * 0.71f, tw - 2 * pad, th * 0.14f), cost,
                    FitStyle(cost, tw - 2 * pad, Mathf.Min(19 * k, tw * 0.09f), FontStyle.Bold, TextAnchor.MiddleCenter, Color.white));
                string note = problem == "MAXED" ? "<color=#ffd24a>MAXED OUT</color>" : problem == "WEARING IT" ? "<color=#bbbbbb>already wearing it</color>"
                    : problem == "BUSY" ? "<color=#bbbbbb>wait for the bench</color>" : $"<color=#a8a8a0>{BenchNote(rec, team)}</color>";
                GUI.Label(new Rect(r.x + pad, r.y + th * 0.85f, tw - 2 * pad, th * 0.12f), note, CraftStyle(Mathf.Min(14 * k, tw * 0.07f), FontStyle.Normal, TextAnchor.MiddleCenter, Color.white));

                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    if (ok)
                    {
                        ClickSound();
                        me.WorkbenchBuyRpc(new NetworkObjectReference(bench.NetworkObject), idx);
                        pc.CloseMenu(); // out of the menu: watch it being made
                        return;
                    }
                    Sfx.PlayUi(Sfx.UiClick, 0.35f, 0.55f);
                    Push(problem == "NEED MORE" ? $"Not enough {Cfg.CurrencyName} for the {rec.Name}" : problem == "BUSY" ? "The workbench is still making something" : problem == "MAXED" ? "That's maxed out already" : "You're already wearing it");
                }
                TrackHover(r);
            }
            if (busy) Shadowed(new Rect(0, sh - 84 * k, sw, 28 * k), "<color=#ffd24a>The workbench is busy making something...</color>", CraftStyle(18 * k, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white));
            Shadowed(new Rect(0, sh - 50 * k, sw, 26 * k), tip ?? $"Click something to have it made on the bench      {Binds.Name(Bind.Inventory)} / Esc: close",
                CraftStyle(15 * k, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.8f)));
        }
    }
}
