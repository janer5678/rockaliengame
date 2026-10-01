using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The Rust-style crafting screen. TAB shows the inventory with a big CRAFTING button on the right; pressing it swaps
    /// to this screen: "INVENTORY" tab at the top (back), categories down the left, an icon grid with a search box, the
    /// crafting queue under it, and the picked item's details (name, where it can be made, description, cost table,
    /// quantity and CRAFT) on the right. Esc goes back to the inventory first; TAB closes everything.
    /// </summary>
    public partial class Hud
    {
        struct CraftEntry { public int Index; public Recipe R; public bool Power; }

        static readonly string[] k_CraftCats = { "COMMON", "CONSTRUCTION", "ITEMS", "TOOLS", "WEAPONS", "AMMO", "POWER" };
        static readonly Item[] k_CraftCatIcon = { Item.BuildingPlan, Item.Barrier, Item.Saddle, Item.Hatchet, Item.Bow, Item.Arrow, Item.C4 };
        public const int CatCommon = 0, CatConstruction = 1, CatItems = 2, CatTools = 3, CatWeapons = 4, CatAmmo = 5, CatPower = 6;

        bool m_CraftView, m_SearchFocus, m_SearchUnfocus;
        int m_CraftCat, m_CraftQty = 1, m_InvFrame = -10;
        Item m_CraftSel = Item.None;
        string m_CraftSearch = "";
        readonly List<CraftEntry> m_CraftAll = new List<CraftEntry>();
        readonly List<CraftEntry> m_CraftShown = new List<CraftEntry>();

        // Rust's flat greys
        static readonly Color k_Panel = new Color(0.22f, 0.22f, 0.20f, 0.92f);
        static readonly Color k_PanelLight = new Color(0.32f, 0.32f, 0.30f, 0.9f);
        static readonly Color k_Blue = new Color(0.20f, 0.47f, 0.74f, 1f);
        static readonly Color k_TextDim = new Color(0.78f, 0.78f, 0.74f, 1f);

        /// <summary>The search box has the keyboard: game keys (other than the inventory key) are ignored meanwhile.</summary>
        public static bool CraftTyping => s_I != null && s_I.m_CraftView && s_I.m_SearchFocus && Time.frameCount - s_I.m_InvFrame <= 2;

        /// <summary>The crafting screen is up (not the inventory).</summary>
        public static bool CraftViewOpen => s_I != null && s_I.m_CraftView;

        // ------------------------------------------------------------------ costs (one place: the DNA mode can redirect this)

        /// <summary>What one craft of a recipe costs, as (item, amount) lines (only the ones above zero).</summary>
        static IEnumerable<(Item item, int amount)> CostLines(Recipe r)
        {
            if (r.Wood > 0) yield return (Cfg.CurrencyItem, r.Wood); // (DNA mode: the price is in DNA)
            if (r.Stone > 0) yield return (Item.Stone, r.Stone);
        }

        /// <summary>The materials you'd pay with, for the header over the CRAFTING button.</summary>
        static string CurrencyText(PlayerNet me) => Cfg.DnaRules ? $"<color=#7dffb0>{me.Count(Item.Dna)} DNA</color>" :
            $"<color=#d9a066>{me.Count(Item.Wood)} wood</color>" + (Cfg.WoodMode ? "" : $"  <color=#c8c8d0>{me.Count(Item.Stone)} stone</color>");

        /// <summary>How many of this recipe you could pay for right now (99 if it's free).</summary>
        static int Affordable(PlayerNet me, Recipe r)
        {
            int n = 99;
            foreach (var (item, amount) in CostLines(r)) n = Mathf.Min(n, me.Count(item) / Mathf.Max(1, amount));
            return n;
        }

        // ------------------------------------------------------------------ categories / recipes

        /// <summary>Which category an item sorts into (COMMON has everything; POWER has the power items).</summary>
        static int CategoryOf(Item id)
        {
            switch (id)
            {
                case Item.Chest: case Item.Barrier: case Item.FortifyBuff: case Item.WoodGenBuff: return CatConstruction;
                case Item.Hatchet: case Item.Pickaxe: case Item.Chainsaw: case Item.BuildingPlan: case Item.Ram: return CatTools;
                case Item.Spear: case Item.Bow: case Item.Crossbow: case Item.Sword: case Item.Shotgun: case Item.Revolver: case Item.Pistol: case Item.C4: return CatWeapons;
                case Item.Arrow: case Item.ShotgunShell: case Item.RevolverAmmo: case Item.PistolAmmo: return CatAmmo;
                default: return CatItems; // armour, saddle, boat, helmet and anything new
            }
        }

        static bool InCategory(int cat, CraftEntry e) => cat == CatCommon || (cat == CatPower ? e.Power : CategoryOf(e.R.Output) == cat);

        /// <summary>Everything craftable in this mode: the normal recipes, then the power items (CraftRpc index, recipe).</summary>
        void BuildCraftEntries(PlayerNet me)
        {
            m_CraftAll.Clear();
            for (int i = 0; i < Cfg.RecipeCount; i++) m_CraftAll.Add(new CraftEntry { Index = i, R = Cfg.GetRecipe(i) });
            for (int i = 0; i < Cfg.PowerCount; i++) m_CraftAll.Add(new CraftEntry { Index = Cfg.PowerBase + i, R = Cfg.GetPowerRecipe(i, me.Team.Value), Power = true });
        }

        /// <summary>A short description for every craftable and power item.</summary>
        static string CraftDescription(Item id, int team)
        {
            switch (id)
            {
                case Item.Hatchet: return "A stone hatchet. Chops trees much faster than your rock (hit the X on the bark for a bonus) and makes a decent weapon in a pinch.";
                case Item.Pickaxe: return "A stone pickaxe. Mines boulders and rocks for stone much faster than your rock.";
                case Item.Spear: return "LMB stabs up close. Hold RMB and press LMB to throw it - walk up to a thrown spear and press E to pick it back up.";
                case Item.BuildingPlan: return "Hold it to build: a see-through ghost shows where the piece goes (green fits, red doesn't). Hold RMB for the building wheel - foundation, wall, doorway, window, floor, stairs. F upgrades a piece to stone, X takes yours down.";
                case Item.Chest: return "A storage chest. Hold it and click to place it inside your base, then press E to open it (14 slots). Enemies who break in can loot it, and smashing it spills everything into a bag.";
                case Item.Bow: return "Hold LMB to draw and let go to fire. The longer you draw, the harder and further it hits. Uses arrows.";
                case Item.Arrow: return $"A bundle of {Mathf.Max(1, Cfg.ArrowsPerCraft)} arrows for the bow and the crossbow.";
                case Item.Crossbow: return $"Fires an arrow for {Cfg.CrossbowDamage:0} damage, faster and flatter than the bow. Hold RMB to aim; it reloads by itself from your arrows.";
                case Item.Armor: return $"Wooden armour. Goes straight on: {Cfg.ArmorHp} extra health that gets used up before your own.";
                case Item.Chainsaw: return $"Hold LMB to rip through wood and stone fast - and anyone in the way. {Cfg.ChainsawUses} uses.";
                case Item.Ram: return $"A battering ram ({Cfg.RamUses} hit{(Cfg.RamUses == 1 ? "" : "s")}). Hold LMB at an enemy building piece to charge it: wood breaks instantly, stone, metal and refined drop one step.";
                case Item.Barrier: return "A tall wooden wall. Place it in your base or out in the open (not in the enemy base) to block a path or cover a door.";
                case Item.Saddle: return "Walk up to a wild horse and press E to saddle it and ride. It goes where you look; Shift gallops, Space jumps. In your team's colour.";
                case Item.Boat: return "Put it in on open water and press E to get in and drive. Only on maps with water.";
                case Item.Sword: return $"A slow, heavy swing: {Cfg.SwordBodyDamage:0} to the body, {Cfg.SwordHeadDamage:0} to the head.";
                case Item.Shotgun: return $"A waterpipe shotgun. One shell at a time (loads the next by itself, or R), {Cfg.ShotgunPellets} pellets: about {Cfg.ShotgunPellets * Cfg.ShotgunPelletDamage:0} damage up close, much less further out. Comes empty - buy shells.";
                case Item.ShotgunShell: return "One shell for the waterpipe shotgun.";
                case Item.Revolver: return $"Hitscan, {Cfg.RevolverMag} rounds: {Cfg.RevolverBodyDamage:0} to the body, {Cfg.RevolverHeadDamage:0} to the head. R reloads from your bullets. Comes empty - buy bullets.";
                case Item.RevolverAmmo: return "One bullet for the revolver.";
                case Item.C4: return "Throw it at enemy buildings: it wrecks the pieces around where it sticks (less against sheet metal and refined).";
                case Item.Helmet: return "The alien helmet. Put it on and the next headshot does no damage - it breaks instead.";
                case Item.FortifyBuff:
                    return "Every piece your team has placed - and every piece you build from now on - goes up a step: stone (2 ram hits), then sheet metal (3), then refined (4). Gets dearer each time.\n\n<color=#ffd24a>" + Cfg.PowerBlurb(id, team) + "</color>";
                case Item.WoodGenBuff:
                    return "Your base makes more wood a second, piling up beside the machine. Three levels, each dearer than the last.\n\n<color=#ffd24a>" + Cfg.PowerBlurb(id, team) + "</color>";
                default: return Cfg.PowerIndex(id) >= 0 ? Cfg.PowerBlurb(id, team) : Cfg.ItemName(id) + ".";
            }
        }

        /// <summary>Why this can't be crafted right now (null if it can), and how many you could make at most.</summary>
        string CraftProblem(PlayerNet me, PlayerController pc, CraftEntry e, out int max)
        {
            var r = e.R;
            int team = me.Team.Value;
            max = Affordable(me, r);
            if (r.Output == Item.FortifyBuff || r.Output == Item.WoodGenBuff || r.Output == Item.Armor || r.Output == Item.HeavyArmor) max = Mathf.Min(max, 1);
            if (Cfg.Builder && Cfg.CraftSeconds(r) > 0f)
                max = Mathf.Min(max, PlayerNet.MaxCraftQueue + 1 - (me.CraftingItem.Value != 0 ? 1 : 0) - me.CraftQueue.Count);
            if (r.Output == Item.FortifyBuff && Cfg.FortifyLevel(team) >= Cfg.MaxFortify) { max = 0; return "Your walls are already refined - fully fortified"; }
            if (r.Output == Item.WoodGenBuff && Cfg.WoodGenLevel(team) >= Cfg.MaxWoodGen) { max = 0; return "Your wood gen is already maxed out"; }
            if (!(pc.CraftOpen || Cfg.CraftAnywhere(r.Output))) { max = 0; return "Go back inside your base to craft this"; }
            if (r.Output == Item.Armor && me.ArmorHp.Value >= Cfg.ArmorHp) { max = 0; return "You're already wearing full armour"; }
            foreach (var (item, amount) in CostLines(r))
                if (me.Count(item) < amount) { max = 0; return $"Not enough {Cfg.ItemName(item).ToLower()} - you need {amount - me.Count(item)} more"; }
            if (max <= 0) { max = 0; return "Your crafting queue is full"; }
            return null;
        }

        /// <summary>Crafts it n times (each is its own CraftRpc, like pressing the button n times).</summary>
        static void CraftTimes(PlayerNet me, CraftEntry e, int n)
        {
            for (int i = 0; i < n; i++) me.CraftRpc(e.Index);
        }

        // ------------------------------------------------------------------ hooks from the inventory screen / PlayerController / tests

        /// <summary>Called at the top of DrawInventory: true if the crafting screen should be drawn instead.</summary>
        bool CraftViewFrame(PlayerController pc)
        {
            // the menu was closed since last time: TAB opens the inventory again, not crafting
            if (Time.frameCount > m_InvFrame + 2) { m_CraftView = false; m_SearchFocus = false; m_CraftSearch = ""; m_CraftQty = 1; }
            m_InvFrame = Time.frameCount;
            if (pc.LootTarget != null) m_CraftView = false; // a chest keeps the normal looting view
            return m_CraftView;
        }

        /// <summary>Esc on the crafting screen: leaves the search box first, then goes back to the inventory. True if used up.</summary>
        static bool CraftBackOut()
        {
            if (s_I == null || !s_I.m_CraftView || Time.frameCount - s_I.m_InvFrame > 2) return false;
            if (s_I.m_SearchFocus) { s_I.m_SearchFocus = false; s_I.m_SearchUnfocus = true; return true; }
            s_I.m_CraftView = false;
            ClickSound();
            return true;
        }

        /// <summary>AutoTest: open the crafting screen on a category with an item picked (open = false: the inventory).</summary>
        public static void DebugCraft(bool open, int cat = CatCommon, Item sel = Item.None, string search = "", int qty = 1)
        {
            if (s_I == null) return;
            s_I.m_CraftView = open;
            s_I.m_CraftCat = cat;
            s_I.m_CraftSel = sel;
            s_I.m_CraftSearch = search;
            s_I.m_CraftQty = qty;
            s_I.m_InvFrame = Time.frameCount + 1000; // survives the menu opening next frame
        }

        /// <summary>AutoTest: what the CRAFT button would do for an item and quantity (returns the reason if it can't).</summary>
        public static string DebugCraftNow(Item id, int qty)
        {
            var me = PlayerNet.Local;
            var pc = PlayerController.Local;
            if (s_I == null || me == null || pc == null) return "no player";
            s_I.BuildCraftEntries(me);
            foreach (var e in s_I.m_CraftAll)
            {
                if (e.R.Output != id) continue;
                var why = s_I.CraftProblem(me, pc, e, out int max);
                if (why != null) return why;
                if (qty > max) return $"only {max} affordable";
                CraftTimes(me, e, qty);
                return null;
            }
            return "not craftable in this mode";
        }

        // ------------------------------------------------------------------ drawing helpers

        GUIStyle CraftStyle(float size, FontStyle fs, TextAnchor a, Color c, bool wrap = false)
        {
            var s = new GUIStyle(m_Label) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size)), fontStyle = fs, alignment = a, wordWrap = wrap, clipping = TextClipping.Clip };
            s.normal.textColor = c;
            s.padding = new RectOffset(0, 0, 0, 0);
            return s;
        }

        /// <summary>A flat Rust-style button: a filled rect that lightens under the mouse.</summary>
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

        // ------------------------------------------------------------------ the inventory screen's CRAFTING button

        /// <summary>Right of the inventory (instead of the old crafting list): one big button that opens the crafting screen.</summary>
        void DrawCraftingButton(PlayerNet me, PlayerController pc, Rect r, float k)
        {
            Shadowed(new Rect(r.x, r.y - 34 * k, r.width, 30 * k), $"<b>YOU HAVE</b>   {CurrencyText(me)}", m_Label);
            bool over = r.Contains(Event.current.mousePosition);
            if (FlatBtn(r, k_Panel, k_PanelLight)) { m_CraftView = true; m_CraftQty = 1; }
            Fill(new Rect(r.x, r.yMax - 4 * k, r.width, 4 * k), over ? new Color(0.35f, 0.62f, 0.9f) : k_Blue);

            // a faded spread of what you can make, behind the title
            BuildCraftEntries(me);
            int n = Mathf.Min(m_CraftAll.Count, 12);
            float cell = r.width / 4f;
            for (int i = 0; i < n; i++)
            {
                var cr = new Rect(r.x + (i % 4) * cell, r.y + 8 * k + (i / 4) * cell * 0.8f, cell, cell * 0.8f);
                Icon(new Rect(cr.x + cell * 0.15f, cr.y + cell * 0.08f, cell * 0.7f, cell * 0.64f), m_CraftAll[i].R.Output, over ? 0.38f : 0.26f);
            }

            float mid = r.y + r.height * 0.5f;
            GUI.Label(new Rect(r.x, mid - 34 * k, r.width, 56 * k), "CRAFTING", CraftStyle(46 * k, FontStyle.Bold, TextAnchor.MiddleCenter, over ? Color.white : new Color(0.88f, 0.88f, 0.84f)));
            int normal = Cfg.RecipeCount, power = Cfg.PowerCount;
            GUI.Label(new Rect(r.x, mid + 20 * k, r.width, 24 * k), $"{normal} items" + (power > 0 ? $"  ·  <color=#ffd24a>{power} power items</color>" : ""), CraftStyle(15 * k, FontStyle.Normal, TextAnchor.MiddleCenter, k_TextDim));
            string where = Cfg.Builder ? "<color=#9dd36a>Craft anywhere - one thing at a time</color>"
                : pc.CraftOpen ? "<color=#9dd36a>In your base - everything can be crafted</color>"
                : "<color=#ff8a7a>Out of your base: only spears and hatchets</color>";
            GUI.Label(new Rect(r.x + 8 * k, r.yMax - 46 * k, r.width - 16 * k, 36 * k), where, CraftStyle(14 * k, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, true));
        }

        // ------------------------------------------------------------------ the crafting screen

        void DrawCraftingView(PlayerNet me, PlayerController pc)
        {
            float sw = Screen.width, sh = Screen.height, k = m_Scale;
            var ev = Event.current;
            Fill(new Rect(0, 0, sw, sh), new Color(0.05f, 0.05f, 0.05f, 0.62f));

            // laid out in the 1200x675 Rust reference's units, starting below the timer at the top
            float topY = 74 * k;
            float u = Mathf.Min((sw - 24f) / 1060f, (sh - topY - 4f) / 665f);
            float ox = (sw - 1055f * u) / 2f, oy = topY;
            Rect R(float x, float y, float w, float h) => new Rect(ox + (x - 65f) * u, oy + y * u, w * u, h * u);
            float fz = u * 1.12f; // font size per reference pixel

            BuildCraftEntries(me);
            int team = me.Team.Value;
            var search = R(200, 466, 392, 30);
            if (m_SearchUnfocus || (ev.type == EventType.MouseDown && !search.Contains(ev.mousePosition)))
            {
                GUIUtility.keyboardControl = 0;
                m_SearchFocus = m_SearchUnfocus = false;
            }

            // ---- the INVENTORY tab at the top: back to the inventory ----
            var tab = R(473, 0, 240, 44);
            if (FlatBtn(tab, k_PanelLight, new Color(0.40f, 0.40f, 0.38f, 0.95f))) { m_CraftView = false; return; }
            bool tabOver = tab.Contains(ev.mousePosition);
            var arrowBox = R(481, 7, 34, 30);
            Fill(arrowBox, new Color(1, 1, 1, tabOver ? 0.85f : 0.65f));
            GUI.Label(arrowBox, "◄", CraftStyle(17 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.25f, 0.25f, 0.23f)));
            GUI.Label(R(515, 0, 198, 44), "INVENTORY", CraftStyle(19 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.86f)));

            // ---- categories (left) ----
            bool searching = m_CraftSearch.Trim() != "";
            Fill(R(65, 48, 133, 448), k_Panel);
            int ci = 0;
            for (int c = 0; c < k_CraftCats.Length; c++)
            {
                int count = 0;
                foreach (var e in m_CraftAll) if (InCategory(c, e)) count++;
                if (c != CatCommon && count == 0) { if (m_CraftCat == c) m_CraftCat = CatCommon; continue; }
                float y = 54 + ci * 31;
                ci++;
                bool on = m_CraftCat == c && !searching;
                var row = R(65, y, 133, 30);
                bool over = row.Contains(ev.mousePosition);
                if (on) Fill(R(59, y, 145, 30), k_Blue);
                else if (over) Fill(row, new Color(1, 1, 1, 0.06f));
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    if (m_CraftCat != c || searching) ClickSound();
                    m_CraftCat = c;
                    m_CraftSearch = "";
                }
                TrackHover(row);
                Icon(R(69, y + 8, 14, 14), k_CraftCatIcon[c], on ? 1f : 0.55f);
                var textC = on ? Color.white : c == CatPower ? new Color(1f, 0.82f, 0.35f) : k_TextDim;
                GUI.Label(R(87, y, 92, 30), k_CraftCats[c], new GUIStyle(CraftStyle(9 * fz, FontStyle.Bold, TextAnchor.MiddleLeft, textC)) { clipping = TextClipping.Overflow });
                if (c != CatCommon)
                    GUI.Label(R(177, y, 17, 30), count.ToString(), CraftStyle(9.5f * fz, FontStyle.Bold, TextAnchor.MiddleRight, on ? Color.white : new Color(0.45f, 0.66f, 0.88f)));
            }

            // ---- what's shown in the grid ----
            m_CraftShown.Clear();
            string q = m_CraftSearch.Trim().ToLower();
            foreach (var e in m_CraftAll)
            {
                if (searching) { if (Cfg.ItemName(e.R.Output).ToLower().Contains(q) || k_CraftCats[e.Power ? CatPower : CategoryOf(e.R.Output)].ToLower().Contains(q)) m_CraftShown.Add(e); }
                else if (InCategory(m_CraftCat, e)) m_CraftShown.Add(e);
            }
            int selIdx = m_CraftShown.FindIndex(e => e.R.Output == m_CraftSel);
            if (selIdx < 0 && m_CraftShown.Count > 0) { selIdx = 0; m_CraftSel = m_CraftShown[0].R.Output; m_CraftQty = 1; }

            // ---- icon grid (middle) ----
            var grid = R(200, 48, 392, 416);
            Fill(grid, k_Panel);
            float cell = grid.width / 5f;
            int rows = Mathf.CeilToInt(m_CraftShown.Count / 5f);
            if (rows * cell > grid.height) cell = grid.height / rows;
            string hover = null;
            for (int i = 0; i < m_CraftShown.Count; i++)
            {
                var e = m_CraftShown[i];
                var cr = new Rect(grid.x + (i % 5) * cell, grid.y + (i / 5) * cell, cell, cell);
                var inner = new Rect(cr.x + 4 * u, cr.y + 4 * u, cr.width - 8 * u, cr.height - 8 * u);
                bool picked = i == selIdx, over = cr.Contains(ev.mousePosition);
                if (picked) { Fill(inner, new Color(1, 1, 1, 0.16f)); Fill(new Rect(inner.x, inner.yMax - 3 * u, inner.width, 3 * u), k_Blue); }
                else if (over) Fill(inner, new Color(1, 1, 1, 0.07f));
                bool ok = CraftProblem(me, pc, e, out _) == null;
                Icon(new Rect(inner.x + inner.width * 0.1f, inner.y + inner.height * 0.08f, inner.width * 0.8f, inner.height * 0.8f), e.R.Output, ok ? 1f : 0.42f);
                if (e.R.Count > 1) GUI.Label(new Rect(inner.x, inner.yMax - 18 * u, inner.width - 4 * u, 16 * u), "x" + e.R.Count, CraftStyle(11 * fz, FontStyle.Bold, TextAnchor.LowerRight, k_TextDim));
                if (over) hover = Cfg.ItemName(e.R.Output);
                if (GUI.Button(cr, GUIContent.none, GUIStyle.none) && m_CraftSel != e.R.Output) { m_CraftSel = e.R.Output; selIdx = i; m_CraftQty = 1; ClickSound(); }
                TrackHover(cr);
            }
            if (m_CraftShown.Count == 0)
                GUI.Label(grid, searching ? $"Nothing called \"{m_CraftSearch.Trim()}\"" : "Nothing to craft here", CraftStyle(14 * fz, FontStyle.Normal, TextAnchor.MiddleCenter, k_TextDim));

            // ---- search ----
            Fill(search, new Color(0.17f, 0.17f, 0.16f, 0.9f));
            var fs = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(13 * fz), alignment = TextAnchor.MiddleLeft, padding = new RectOffset(Mathf.RoundToInt(10 * u), 4, 0, 0) };
            fs.normal.background = fs.hover.background = fs.focused.background = fs.active.background = null;
            fs.normal.textColor = fs.hover.textColor = fs.focused.textColor = fs.active.textColor = new Color(0.92f, 0.92f, 0.88f);
            GUI.SetNextControlName("CraftSearch");
            string ns = GUI.TextField(search, m_CraftSearch, 24, fs).Replace("\t", "");
            if (ns != m_CraftSearch) { m_CraftSearch = ns; m_CraftQty = 1; }
            m_SearchFocus = GUI.GetNameOfFocusedControl() == "CraftSearch";
            if (m_SearchFocus && ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)) { GUIUtility.keyboardControl = 0; m_SearchFocus = false; ev.Use(); }
            if (m_CraftSearch == "" && !m_SearchFocus)
                GUI.Label(new Rect(search.x + 10 * u, search.y, search.width, search.height), "Search...", CraftStyle(13 * fz, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.6f, 0.6f, 0.57f)));
            else if (m_SearchFocus) Fill(new Rect(search.x, search.yMax - 2 * u, search.width, 2 * u), k_Blue);

            // ---- crafting queue (under the grid) ----
            var queue = R(65, 499, 527, 66);
            Fill(queue, new Color(0.23f, 0.23f, 0.21f, 0.6f));
            GUI.Label(R(75, 499, 517, 66), "CRAFTING QUEUE", CraftStyle(36 * fz, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.13f)));
            DrawCraftQueue(me, R(70, 504, 517, 56), u, fz);

            // ---- details (right) ----
            var det = R(595, 48, 525, 367);
            Fill(det, k_Panel);
            var table = R(595, 420, 525, 145);
            Fill(table, k_Panel);
            if (selIdx < 0) { DrawCraftHotbar(me, u); CraftTooltip(hover, u, fz); return; }
            var sel = m_CraftShown[selIdx];
            var rec = sel.R;
            string problem = CraftProblem(me, pc, sel, out int max);
            m_CraftQty = Mathf.Clamp(m_CraftQty, 1, Mathf.Max(1, max));

            Fill(R(603, 55, 62, 62), new Color(0, 0, 0, 0.2f));
            Icon(R(606, 58, 56, 56), rec.Output);
            GUI.Label(R(670, 52, 370, 30), Cfg.ItemName(rec.Output).ToUpper(), CraftStyle(21 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.92f, 0.92f, 0.88f)));

            // where it can be made (green / red), like Rust's workbench badge
            string badge; Color badgeBg, badgeFg;
            bool maxed = (rec.Output == Item.FortifyBuff && Cfg.FortifyLevel(team) >= Cfg.MaxFortify) || (rec.Output == Item.WoodGenBuff && Cfg.WoodGenLevel(team) >= Cfg.MaxWoodGen);
            if (maxed) { badge = "MAXED OUT"; badgeBg = new Color(0.35f, 0.35f, 0.33f); badgeFg = new Color(0.85f, 0.85f, 0.8f); }
            else if (Cfg.Builder || Cfg.CraftAnywhere(rec.Output)) { badge = "CRAFT ANYWHERE"; badgeBg = new Color(0.36f, 0.50f, 0.16f); badgeFg = new Color(0.80f, 1f, 0.55f); }
            else if (pc.CraftOpen) { badge = "IN YOUR BASE"; badgeBg = new Color(0.36f, 0.50f, 0.16f); badgeFg = new Color(0.80f, 1f, 0.55f); }
            else { badge = "CRAFT IN YOUR BASE"; badgeBg = new Color(0.55f, 0.16f, 0.13f); badgeFg = new Color(1f, 0.70f, 0.64f); }
            var bst = CraftStyle(11 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, badgeFg);
            float bw = bst.CalcSize(new GUIContent(badge)).x + 16 * u;
            var bcen = R(670, 84, 370, 20);
            var br = new Rect(bcen.center.x - bw / 2f, bcen.y, bw, bcen.height);
            Fill(br, badgeBg);
            GUI.Label(br, badge, bst);
            if (sel.Power)
            {
                var pst = CraftStyle(10 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f));
                GUI.Label(R(604, 119, 160, 16), "POWER ITEM", new GUIStyle(pst) { alignment = TextAnchor.MiddleLeft });
            }

            // top right: craft time (Builder) and how many one craft makes
            if (Cfg.Builder)
            {
                float secs = Cfg.CraftSeconds(rec);
                Fill(R(1046, 55, 68, 24), new Color(0, 0, 0, 0.3f));
                GUI.Label(R(1046, 55, 64, 24), secs > 0f ? $"{secs:0.0}s" : "instant", CraftStyle(13 * fz, FontStyle.Bold, TextAnchor.MiddleRight, k_TextDim));
                GUI.Label(R(1052, 55, 40, 24), "TIME", CraftStyle(9 * fz, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.6f, 0.6f, 0.57f)));
            }
            Fill(R(1046, 83, 68, 24), new Color(0, 0, 0, 0.3f));
            GUI.Label(R(1046, 83, 64, 24), "+" + rec.Count, CraftStyle(13 * fz, FontStyle.Bold, TextAnchor.MiddleRight, k_TextDim));

            GUI.Label(R(610, 140, 500, 220), CraftDescription(rec.Output, team), CraftStyle(14 * fz, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.84f, 0.84f, 0.8f), true));
            if (problem != null)
                GUI.Label(R(610, 378, 500, 30), problem, CraftStyle(13 * fz, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.5f, 0.42f)));
            else if (rec.Output == Item.Armor && !Cfg.Builder)
                GUI.Label(R(610, 378, 500, 30), "Goes straight on when you craft it", CraftStyle(13 * fz, FontStyle.Normal, TextAnchor.MiddleLeft, k_TextDim));

            // ---- cost table: AMOUNT / ITEM TYPE / TOTAL / HAVE ----
            var hst = CraftStyle(10 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.71f));
            GUI.Label(R(598, 422, 98, 18), "AMOUNT", hst);
            GUI.Label(R(699, 422, 213, 18), "ITEM TYPE", hst);
            GUI.Label(R(915, 422, 99, 18), "TOTAL", hst);
            GUI.Label(R(1017, 422, 100, 18), "HAVE", hst);
            var lines = new List<(Item item, int amount)>(CostLines(rec));
            var cst = CraftStyle(13 * fz, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.86f));
            var cstR = CraftStyle(13 * fz, FontStyle.Normal, TextAnchor.MiddleRight, new Color(0.9f, 0.9f, 0.86f));
            for (int j = 0; j < 4; j++)
            {
                float y = 441 + j * 22;
                bool has = j < lines.Count;
                float a = has ? 0.32f : 0.12f;
                Fill(R(598, y, 98, 20), new Color(0, 0, 0, a));
                Fill(R(699, y, 213, 20), new Color(0, 0, 0, a));
                Fill(R(915, y, 99, 20), new Color(0, 0, 0, a));
                Fill(R(1017, y, 100, 20), new Color(0, 0, 0, a));
                if (!has) continue;
                var (item, amount) = lines[j];
                int total = amount * m_CraftQty, have = me.Count(item);
                GUI.Label(R(598, y, 92, 20), amount.ToString(), cstR);
                Icon(R(703, y + 1, 18, 18), item);
                GUI.Label(R(726, y, 184, 20), Cfg.ItemName(item), cst);
                GUI.Label(R(921, y, 92, 20), total.ToString(), cst);
                GUI.Label(R(1023, y, 92, 20), have < total ? $"<color=#ff6a5a><b>{have}</b></color>" : have.ToString(), cst);
            }
            if (lines.Count == 0) GUI.Label(R(705, 441, 200, 20), "Free", cst);

            // ---- quantity  - [n] + ►|   and CRAFT ----
            var btnBg = new Color(0.36f, 0.36f, 0.34f, 0.95f);
            var btnHi = new Color(0.46f, 0.46f, 0.44f, 1f);
            var sym = CraftStyle(20 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.86f));
            bool canMore = problem == null && m_CraftQty < max;
            if (FlatBtn(R(598, 532, 37, 30), btnBg, btnHi, m_CraftQty > 1)) m_CraftQty--;
            GUI.Label(R(598, 530, 37, 30), "-", sym);
            Fill(R(638, 532, 60, 30), new Color(0, 0, 0, 0.3f));
            GUI.Label(R(648, 532, 50, 30), m_CraftQty.ToString(), CraftStyle(15 * fz, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            if (FlatBtn(R(701, 532, 37, 30), btnBg, btnHi, canMore)) m_CraftQty++;
            GUI.Label(R(701, 531, 37, 30), "+", sym);
            if (FlatBtn(R(741, 532, 37, 30), btnBg, btnHi, canMore)) m_CraftQty = max;
            GUI.Label(R(741, 532, 37, 30), "►|", CraftStyle(13 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.86f)));
            // the mouse wheel over the quantity changes it too
            if (ev.type == EventType.ScrollWheel && R(598, 532, 180, 30).Contains(ev.mousePosition))
            {
                m_CraftQty = Mathf.Clamp(m_CraftQty + (ev.delta.y < 0 ? 1 : -1), 1, Mathf.Max(1, max));
                ev.Use();
            }
            if (problem == null && Cfg.Builder && Cfg.CraftSeconds(rec) > 0f)
                GUI.Label(R(786, 532, 222, 30), $"<color=#bbbbbb>{Cfg.CraftSeconds(rec) * m_CraftQty:0.#}s in all</color>", CraftStyle(12 * fz, FontStyle.Normal, TextAnchor.MiddleRight, k_TextDim));

            bool can = problem == null;
            var craftR = R(1017, 532, 100, 30);
            if (FlatBtn(craftR, can ? new Color(0.36f, 0.50f, 0.17f, 1f) : new Color(0.30f, 0.30f, 0.28f, 0.9f), new Color(0.44f, 0.62f, 0.21f, 1f), can))
            {
                CraftTimes(me, sel, m_CraftQty);
                m_CraftQty = 1;
            }
            GUI.Label(craftR, "CRAFT", CraftStyle(15 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, can ? new Color(0.88f, 1f, 0.75f) : new Color(1, 1, 1, 0.35f)));

            Shadowed(R(65, 568, 527, 24), $"Esc or the INVENTORY tab: back   ·   {Binds.Name(Bind.Inventory)}: close", CraftStyle(12 * fz, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.77f)));
            DrawCraftHotbar(me, u);
            CraftTooltip(hover, u, fz);
        }

        /// <summary>The hovered grid item's name next to the mouse.</summary>
        void CraftTooltip(string text, float u, float fz)
        {
            var ev = Event.current;
            if (text == null || ev.type != EventType.Repaint) return;
            var st = CraftStyle(12 * fz, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            var size = st.CalcSize(new GUIContent(text));
            var tr = new Rect(ev.mousePosition.x + 14 * u, ev.mousePosition.y + 16 * u, size.x + 14 * u, 20 * u);
            Fill(tr, new Color(0.08f, 0.08f, 0.08f, 0.92f));
            GUI.Label(tr, text, st);
        }

        /// <summary>Builder: what's being made (its bar running down) and what's waiting, as squares in the queue strip.</summary>
        void DrawCraftQueue(PlayerNet me, Rect area, float u, float fz)
        {
            if (me.CraftingItem.Value == 0 || me.CraftDoneAt.Value < 0) return;
            double now = me.NetworkManager.ServerTime.Time;
            float s = area.height, x = area.x;
            void Box(Item id, float left, float total)
            {
                var r = new Rect(x, area.y, s, s);
                Fill(r, new Color(0, 0, 0, 0.45f));
                Icon(new Rect(r.x + s * 0.12f, r.y + s * 0.06f, s * 0.76f, s * 0.7f), id);
                Fill(new Rect(r.x, r.yMax - 4 * u, s * Mathf.Clamp01(left / Mathf.Max(0.01f, total)), 4 * u), new Color(1f, 0.82f, 0.3f));
                GUI.Label(new Rect(r.x, r.yMax - 20 * u, s - 3 * u, 16 * u), $"{left:0.0}", CraftStyle(11 * fz, FontStyle.Bold, TextAnchor.LowerRight, Color.white));
                x += s + 6 * u;
            }
            float total0 = Mathf.Max(0.01f, (float)(me.CraftDoneAt.Value - me.CraftStartAt.Value));
            Box((Item)me.CraftingItem.Value, Mathf.Max(0f, (float)(me.CraftDoneAt.Value - now)), total0);
            for (int i = 0; i < me.CraftQueue.Count && x + s <= area.xMax; i++)
            {
                var id = (Item)me.CraftQueue[i];
                float t = Cfg.CraftSecondsOf(id);
                Box(id, t, t);
            }
        }

        /// <summary>The hotbar along the bottom, like Rust keeps it on the crafting screen.</summary>
        void DrawCraftHotbar(PlayerNet me, float u)
        {
            int n = Cfg.HotbarSize;
            float s = 56 * u, gap = 4 * u;
            float x = Screen.width / 2f - (n * s + (n - 1) * gap) / 2f, y = Screen.height - s - 8 * u;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(x + i * (s + gap), y, s, s);
                DrawSlotVisual(r, me.SlotAt(i), me.HeldSlot.Value == i, me);
                GUI.Label(new Rect(r.x + 4 * u, r.y + 1, 34 * u, 22 * u), Binds.Short(Bind.Hotbar1 + i), m_SmallNoClip);
            }
        }
    }
}
