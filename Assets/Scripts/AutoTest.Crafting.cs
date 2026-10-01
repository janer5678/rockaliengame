using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest craftui (with -host -solo -fast -shotdir DIR, any -rules): screenshots of the inventory with its CRAFTING
    /// button and the Rust-style crafting screen (categories, an item picked, search, power items, out of base), and
    /// checks that crafting from it (quantity included) works.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator CraftUiRoutine(PlayerNet me, PlayerController pc)
        {
            int team = me.Team.Value;
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            string P(string what) => $"craftui_{rn}_{Screen.width}_{what}";
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            for (int i = 0; i < 3; i++) me.ServerGive(Cfg.CurrencyItem, 1000);
            if (!Cfg.WoodMode) me.ServerGive(Item.Stone, 120);
            me.ServerGive(Item.Spear, 1);
            yield return new WaitForSeconds(0.5f);

            // the inventory: the CRAFTING button where the list used to be
            Hud.DebugCraft(false);
            pc.MenuOpen = true;
            yield return Snap(P("inventory"));
            Check(!Hud.CraftViewOpen, "TAB opens the inventory (not the crafting screen)");

            // the crafting screen, a few categories with something picked
            Hud.DebugCraft(true, Hud.CatCommon, Item.Bow);
            yield return Snap(P("common_bow"));
            Hud.DebugCraft(true, Hud.CatTools, Item.Ram, "", 1);
            yield return Snap(P("tools_ram"));
            Hud.DebugCraft(true, Hud.CatAmmo, Item.Arrow, "", 3);
            yield return Snap(P("ammo_arrows_x3"));
            if (Cfg.PowerMenu)
            {
                Hud.DebugCraft(true, Hud.CatPower, Item.FortifyBuff);
                yield return Snap(P("power_fortify"));
                Hud.DebugCraft(true, Hud.CatWeapons, Item.Revolver);
                yield return Snap(P("weapons_revolver"));
            }
            Hud.DebugCraft(true, Hud.CatCommon, Item.None, "bow");
            yield return Snap(P("search_bow"));

            // crafting from it: 3 x arrows = 3 crafts
            if (!Cfg.LimitedCrafting)
            {
                int a0 = me.Count(Item.Arrow), w0 = me.Count(Cfg.CurrencyItem);
                var arrows = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Arrow));
                string why = Hud.DebugCraftNow(Item.Arrow, 3);
                if (Cfg.Builder)
                {
                    // Builder: they queue up one after another
                    Hud.DebugCraft(true, Hud.CatAmmo, Item.Arrow);
                    yield return Snap(P("queue"));
                }
                float until = Time.time + (Cfg.Builder ? 3 * Cfg.CraftSeconds(arrows) + 3f : 1f);
                while (Time.time < until && me.Count(Item.Arrow) < a0 + 3 * arrows.Count) yield return null;
                Check(why == null && me.Count(Item.Arrow) == a0 + 3 * arrows.Count && me.Count(Cfg.CurrencyItem) == w0 - 3 * arrows.Wood,
                    $"CRAFT x3 makes three lots of arrows ({why ?? "ok"}: {me.Count(Item.Arrow) - a0} arrows, {w0 - me.Count(Cfg.CurrencyItem)} {Cfg.CurrencyName})");
                // too many: refused before anything is sent
                Check(Hud.DebugCraftNow(Item.Arrow, 999) != null, "can't craft more than you can afford");
            }

            // out of your base: base-only items say so (red badge) and are greyed out
            if (!Cfg.Builder)
            {
                pc.LocalTeleport(new Vector3(0, MapBuilder.Height(0, 0) + 1f, 0), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.5f);
                Hud.DebugCraft(true, Hud.CatWeapons, Item.Crossbow);
                yield return Snap(P("outside_crossbow"));
                Check(!pc.CraftOpen && Hud.DebugCraftNow(Item.Crossbow, 1) != null && Hud.DebugCraftNow(Item.Spear, 1) == null, "outside the base only spears and hatchets can be crafted");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            }

            // closing and opening again goes back to the inventory
            pc.CloseMenu();
            yield return new WaitForSeconds(0.3f);
            pc.MenuOpen = true;
            yield return new WaitForSeconds(0.3f);
            Check(!Hud.CraftViewOpen, "opening the menu again shows the inventory");
            pc.CloseMenu();
            Log("craftui test done");
            Application.Quit(0);
        }
    }
}
