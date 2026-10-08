using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// UPGRADES: E on your own upgrade station (UpgradeStation.cs, left of the alien machine) opens it (PlayerController.UpgradesOpen). It's the crafting screen's layout -
    /// your inventory on the left, the list on the right under an "UPGRADES" header with what you have to spend - with one
    /// row per base upgrade (Upgrades.cs): icon, name, the level bought so far (pips), what the next level does, its price
    /// (red when you're short) and an UPGRADE button (green when you can buy it; MAXED once it's all the way up).
    /// </summary>
    public partial class Hud
    {
        readonly List<Item> m_UpgradeTmp = new List<Item>();

        /// <summary>Test hook: the rows as last drawn (upgrade, why it can't be bought - null = it can).</summary>
        public static readonly List<(Item Id, string Problem)> UpgradeRowsShown = new List<(Item, string)>();

        static readonly Color k_UpEdge = new Color(1f, 0.82f, 0.29f);

        /// <summary>Why an upgrade can't be bought right now (null = it can).</summary>
        static string UpgradeProblem(PlayerNet me, Item id)
        {
            int team = me.Team.Value;
            if (Cfg.BaseUpgradeMaxed(id, team)) return "maxed out";
            if (!Cfg.AtOwnStation(team, me.transform.position)) return "at your station";
            if (!me.CanAfford(Cfg.BaseUpgradeRecipe(id, team))) return "can't afford";
            return null;
        }

        /// <summary>The longer description (shown when you point at a row).</summary>
        static string UpgradeDescription(Item id, int team)
        {
            int lvl = Cfg.BaseUpgradeLevel(id, team), max = Cfg.BaseUpgradeMax(id);
            if (Cfg.Jonah && id == Item.FortifyBuff)
                return $"Upgrade 10 Walls ({Cfg.Upgrade10WallsDust} alien dust): you fly round a black copy of your base and click {Cfg.WallsPerUpgrade} of your pieces (click again to un-pick). The moment you pick the last one you're back and they've gone up a tier. Buy it as often as you like.";
            if (id == Item.WoodGenBuff)
                return $"Wood Gen: the first level builds a wood machine in your base ({Cfg.WoodGenRate(1)} wood a second), the next two make it faster - {Cfg.WoodGenRate(2)}, then {Cfg.WoodGenRate(3)} a second. Level {lvl} of {max}.";
            return $"Fortify All Walls: every piece your team has built goes up a step at full health - stone, then metal, then armoured - and pieces you build after come out that strong too. Level {lvl} of {max}.";
        }

        /// <summary>Right of the inventory: the UPGRADES header and one row per upgrade, then a footer.</summary>
        void DrawUpgradeList(PlayerNet me, PlayerController pc, float x, float top, float colW, float bottom, float k)
        {
            var ev = Event.current;
            int team = me.Team.Value;
            Shadowed(new Rect(x, top - 36 * k, colW, 32 * k), "<b>UPGRADES</b>", CraftStyle(20 * k, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            Shadowed(new Rect(x, top - 36 * k, colW, 32 * k), CurrencyText(me), CraftStyle(17 * k, FontStyle.Bold, TextAnchor.MiddleRight, Color.white));
            Cfg.BaseUpgrades(m_UpgradeTmp);
            if (ev.type == EventType.Repaint) UpgradeRowsShown.Clear();

            // a heading in the crafting list's style, then the rows
            float headH = 24 * k, row = 92 * k, gap = 6 * k;
            Shadowed(new Rect(x + 2 * k, top, colW, headH - 3 * k), "<b>YOUR BASE</b>  <color=#bbbbbb>for the whole team, all match</color>",
                CraftStyle(Mathf.Min(15 * k, headH * 0.7f), FontStyle.Bold, TextAnchor.MiddleLeft, k_UpEdge));
            Fill(new Rect(x, top + headH - 4 * k, colW, 2 * k), new Color(k_UpEdge.r, k_UpEdge.g, k_UpEdge.b, 0.6f));
            float y = top + headH;
            foreach (var id in m_UpgradeTmp)
            {
                DrawUpgradeRow(me, new Rect(x, y, colW, row), id, k, ev);
                y += row + gap;
            }
            string hint = "Upgrades are here from the start - no trade station needed. Press "
                + $"{KT(Bind.Interact)} on your upgrade station (the up arrow in your team's colour, left of your alien machine) to open this; {KT(Bind.Inventory)} or [Esc] closes it. LMB on UPGRADE buys the next level.";
            var hintStyle = CraftStyle(13 * k, FontStyle.Normal, TextAnchor.UpperLeft, Color.white, true);
            // what the mouse is over (an upgrade or an item in the bag), otherwise how this screen works
            if (!DrawHoverInfo(new Rect(x, y + 4 * k, colW, Mathf.Max(40 * k, bottom - y)), k))
                Shadowed(new Rect(x, y + 4 * k, colW, Mathf.Max(40 * k, bottom - y)), hint, hintStyle);
        }

        /// <summary>Jonah mode: a panel to the right of the crafting list (and of UPGRADES at the upgrade station) - the alien
        /// dust, "1000 Alien Dust", its price in wood and a BUY button (the same craft as CraftRpc's AlienDust recipe).</summary>
        void DrawDustPanel(PlayerNet me, PlayerController pc, float x, float top, float k)
        {
            int idx = Cfg.CraftIndexOf(Item.AlienDust);
            if (idx < 0) return;
            var r = Cfg.CraftRecipe(idx, me.Team.Value);
            float w = 170 * k, h = 230 * k;
            x = Mathf.Min(x, Screen.width - w - 10);
            var red = new Color(0.85f, 0.12f, 0.08f);
            var rr = new Rect(x, top, w, h);
            Fill(rr, new Color(0.14f, 0.04f, 0.04f, 0.85f));
            Fill(new Rect(rr.x, rr.y, rr.width, 4 * k), red);
            Shadowed(new Rect(x, top + 8 * k, w, 26 * k), "<b>ALIEN DUST</b>", CraftStyle(17 * k, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white));
            var icon = ItemIcons.Get(Item.AlienDust);
            float isz = 90 * k;
            if (icon != null) GUI.DrawTexture(new Rect(x + (w - isz) / 2f, top + 36 * k, isz, isz), icon, ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(x, top + 130 * k, w, 24 * k), $"<b>{r.Count} dust</b>", CraftStyle(16 * k, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, true));
            GUI.Label(new Rect(x, top + 152 * k, w, 22 * k), CostColored(me, r), CraftStyle(14 * k, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, true));
            bool ok = me.CanAfford(r);
            var br = new Rect(x + 14 * k, top + h - 50 * k, w - 28 * k, 38 * k);
            if (FlatBtn(br, ok ? k_BtnOk : k_BtnNo, k_BtnOkHi, ok)) { me.CraftRpc(idx); Sfx.PlayUi(Sfx.UiClick, 0.8f, 0.7f); }
            GUI.Label(br, "BUY", CraftStyle(18 * k, FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(1, 1, 1, 0.35f)));
        }

        /// <summary>One row: icon, name + level pips, what the next level does, its price, and UPGRADE.</summary>
        void DrawUpgradeRow(PlayerNet me, Rect rr, Item id, float k, Event ev)
        {
            int team = me.Team.Value;
            var rec = Cfg.BaseUpgradeRecipe(id, team);
            string problem = UpgradeProblem(me, id);
            if (ev.type == EventType.Repaint) UpgradeRowsShown.Add((id, problem));
            bool ok = problem == null, maxed = problem == "maxed out";
            bool over = rr.Contains(ev.mousePosition);
            float row = rr.height;
            Fill(rr, ok ? k_RowOk : k_RowNo);
            if (over) { Fill(rr, new Color(1, 1, 1, 0.06f)); SetHover(UpgradeDescription(id, team)); }
            Fill(new Rect(rr.x, rr.y, 4 * k, rr.height), ok ? k_UpEdge : k_UpEdge * 0.6f);
            // the icon
            float pad = 8 * k, isz = Mathf.Min(row - 2 * pad, 72 * k);
            var ib = new Rect(rr.x + 10 * k, rr.y + (row - isz) * 0.5f, isz, isz);
            Fill(ib, new Color(0, 0, 0, 0.3f));
            var tIcon = ItemIcons.GetForTeam(id, team); // (its arrow in a light shade of your team's colour)
            if (tIcon != null) GUI.DrawTexture(new Rect(ib.x + 3, ib.y + 3, ib.width - 6, ib.height - 6), tIcon, ScaleMode.ScaleToFit, true);
            // UPGRADE (MAXED when it's all the way up)
            float bw = Mathf.Min(120 * k, rr.width * 0.26f);
            var br = new Rect(rr.xMax - bw - 8 * k, rr.y + row * 0.24f, bw, row * 0.52f);
            // name, level pips, what the next level does, price
            float tx = ib.xMax + 10 * k, tw = br.x - 10 * k - tx;
            int lvl = Cfg.BaseUpgradeLevel(id, team), max = Cfg.BaseUpgradeMax(id);
            string nm = Cfg.ItemName(id);
            var nameStyle = FitStyle(nm, tw * 0.62f, Mathf.Min(19 * k, row * 0.22f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            GUI.Label(new Rect(tx, rr.y + row * 0.05f, tw, row * 0.3f), nm, nameStyle);
            float pip = 13 * k, pipGap = 4 * k;
            float px = tx + Mathf.Min(nameStyle.CalcSize(new GUIContent(nm)).x, tw * 0.62f) + 10 * k;
            if (max > 0) GUI.Label(new Rect(px, rr.y + row * 0.05f, 60 * k, row * 0.3f), $"LV {lvl}/{max}", CraftStyle(12 * k, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.85f, 0.85f, 0.8f))); // (Jonah's Upgrade 10 Walls has no levels)
            px += 52 * k;
            for (int i = 0; i < max; i++)
            {
                var pr = new Rect(px + i * (pip + pipGap), rr.y + row * 0.2f - pip * 0.5f, pip, pip);
                Fill(pr, new Color(0, 0, 0, 0.5f));
                if (i < lvl) Fill(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), k_UpEdge);
            }
            GUI.Label(new Rect(tx, rr.y + row * 0.36f, tw, row * 0.3f), Cfg.BaseUpgradeBlurb(id, team),
                CraftStyle(Mathf.Min(14 * k, row * 0.17f), FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.88f, 0.88f, 0.84f), true));
            string sub = maxed && Cfg.Jonah && id == Item.FortifyBuff ? "<color=#a8a8a0>no walls to upgrade yet</color>" : maxed ? $"<color=#ffd24a>MAXED OUT</color>  <color=#a8a8a0>{Cfg.BaseUpgradeNow(id, team)}</color>"
                : CostColored(me, rec);
            if (problem == "at your station") sub += "  <color=#8fb8ff>at your upgrade station</color>";
            GUI.Label(new Rect(tx, rr.y + row * 0.66f, tw, row * 0.28f), sub, FitStyle(sub, tw, Mathf.Min(16 * k, row * 0.19f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            if (FlatBtn(br, ok ? k_BtnOk : k_BtnNo, k_BtnOkHi, ok))
            {
                if (Cfg.Jonah && id == Item.FortifyBuff) WallPicker.Begin(); // (Jonah mode: pick the 10 pieces first - JonahMode.cs)
                else me.BaseUpgradeRpc(id);
            }
            GUI.Label(br, maxed ? (Cfg.Jonah && id == Item.FortifyBuff ? "NO WALLS" : "MAXED") : Cfg.Jonah && id == Item.FortifyBuff ? "PICK 10" : "UPGRADE", CraftStyle(Mathf.Min(17 * k, row * 0.22f), FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(1, 1, 1, 0.35f)));
        }
    }
}
