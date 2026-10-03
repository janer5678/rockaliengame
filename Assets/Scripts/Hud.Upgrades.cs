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
            if (id == Item.WoodGenBuff)
                return $"Wood Gen: your wood machine makes wood faster - {Cfg.WoodGenRate(0)}, then {Cfg.WoodGenRate(1)}, then {Cfg.WoodGenRate(2)} a second. Level {lvl} of {max}.";
            return $"Fortify All Walls: every piece your team has built goes up a step at full health - stone, then metal, then refined - and pieces you build after come out that strong too. Level {lvl} of {max}.";
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
            string hint = "Upgrades are here from the start - no workbench needed. Press "
                + $"{Binds.Name(Bind.Interact)} on your upgrade station (the green plus, left of your alien machine) to open this; {Binds.Name(Bind.Inventory)} or Esc closes it.";
            var hintStyle = CraftStyle(13 * k, FontStyle.Normal, TextAnchor.UpperLeft, Color.white, true);
            Shadowed(new Rect(x, y + 4 * k, colW, Mathf.Max(40 * k, bottom - y)), hint, hintStyle);
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
            if (over) { Fill(rr, new Color(1, 1, 1, 0.06f)); m_HoverName = UpgradeDescription(id, team); }
            Fill(new Rect(rr.x, rr.y, 4 * k, rr.height), ok ? k_UpEdge : k_UpEdge * 0.6f);
            // the icon
            float pad = 8 * k, isz = Mathf.Min(row - 2 * pad, 72 * k);
            var ib = new Rect(rr.x + 10 * k, rr.y + (row - isz) * 0.5f, isz, isz);
            Fill(ib, new Color(0, 0, 0, 0.3f));
            Icon(new Rect(ib.x + 3, ib.y + 3, ib.width - 6, ib.height - 6), id);
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
            GUI.Label(new Rect(px, rr.y + row * 0.05f, 60 * k, row * 0.3f), $"LV {lvl}/{max}", CraftStyle(12 * k, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.85f, 0.85f, 0.8f)));
            px += 52 * k;
            for (int i = 0; i < max; i++)
            {
                var pr = new Rect(px + i * (pip + pipGap), rr.y + row * 0.2f - pip * 0.5f, pip, pip);
                Fill(pr, new Color(0, 0, 0, 0.5f));
                if (i < lvl) Fill(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), k_UpEdge);
            }
            GUI.Label(new Rect(tx, rr.y + row * 0.36f, tw, row * 0.3f), Cfg.BaseUpgradeBlurb(id, team),
                CraftStyle(Mathf.Min(14 * k, row * 0.17f), FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.88f, 0.88f, 0.84f), true));
            string sub = maxed ? $"<color=#ffd24a>MAXED OUT</color>  <color=#a8a8a0>{Cfg.BaseUpgradeNow(id, team)}</color>"
                : CostColored(me, rec);
            if (problem == "at your station") sub += "  <color=#8fb8ff>at your upgrade station</color>";
            GUI.Label(new Rect(tx, rr.y + row * 0.66f, tw, row * 0.28f), sub, FitStyle(sub, tw, Mathf.Min(16 * k, row * 0.19f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white));
            if (FlatBtn(br, ok ? k_BtnOk : k_BtnNo, k_BtnOkHi, ok)) me.BaseUpgradeRpc(id);
            GUI.Label(br, maxed ? "MAXED" : "UPGRADE", CraftStyle(Mathf.Min(17 * k, row * 0.22f), FontStyle.Bold, TextAnchor.MiddleCenter, ok ? Color.white : new Color(1, 1, 1, 0.35f)));
        }
    }
}
