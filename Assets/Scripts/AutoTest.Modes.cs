using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Tests for the game modes (-autotest modes, with -rules arsenal / builder / fun / funrandom) and the theme maps
    /// (-autotest maps -map beach / canyon / ...). Run with -host -solo -fast -shotdir DIR.
    /// </summary>
    public partial class AutoTest
    {
        string ShotDir()
        {
            string dir = ".";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        IEnumerator Snap(string name)
        {
            yield return new WaitForSeconds(0.7f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), name + ".png"));
            Log("shot " + name);
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator ModesRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            Log($"game mode {Cfg.RulesName(Cfg.Rules)} on {Cfg.MapLabel}");
            Check(Cfg.MapKey >> Cfg.RulesShift != 0 || Cfg.Rules == GameRules.Classic, "the game mode is in the map key (synced to clients)");
            switch (Cfg.Rules)
            {
                case GameRules.Arsenal: yield return ArsenalTests(me, pc, g, team); break;
                case GameRules.Builder: yield return BuilderTests(me, pc, g, team); break;
                default: yield return FunTests(me, pc, g, team); break;
            }
            yield return Snap("mode_" + rn + "_hud");
            Log("modes test done");
            Application.Quit(0);
        }

        IEnumerator ArsenalTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            var hat = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Hatchet));
            var xbow = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Crossbow));
            Check(Cfg.PowerMenu && Cfg.PowerCount == 4 && hat.Wood == Cfg.HatchetWood && xbow.Wood == 350, $"Arsenal: power menu (4 items), normal prices (hatchet {hat.Wood} wood), crossbow {xbow.Wood} wood");
            Check(Cfg.PieceWood(PieceType.Wall) == Cfg.WallWood, $"building pieces at their normal price (wall {Cfg.PieceWood(PieceType.Wall)} wood)");
            for (int i = 0; i < 20; i++) me.ServerGive(Item.Wood, 1000);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            int w0 = me.Count(Item.Wood);

            // pistol + ammo
            me.CraftRpc(Cfg.PowerBase + 0);
            yield return new WaitForSeconds(0.3f);
            me.CraftRpc(Cfg.PowerBase + 1);
            yield return new WaitForSeconds(0.3f);
            Check(me.Count(Item.Pistol) == 1 && me.Count(Item.PistolAmmo) == Cfg.PistolAmmoPerCraft && w0 - me.Count(Item.Wood) == Cfg.PistolWood + Cfg.PistolAmmoWood,
                $"bought a pistol and {me.Count(Item.PistolAmmo)} rounds ({w0 - me.Count(Item.Wood)} wood)");
            yield return Hold(me, Item.Pistol);
            Check(me.HeldStack.Data == Cfg.PistolMag, $"the pistol comes loaded ({me.HeldStack.Data})");
            yield return Snap("arsenal_pistol");
            var fwd = me.transform.forward;
            me.FirePistolRpc(me.EyePos, fwd * Cfg.PistolSpeed);
            yield return new WaitForSeconds(0.3f);
            me.ArrowLandRpc(me.transform.position + fwd * 10f, fwd);
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == Cfg.PistolMag - 1 && FindWorldItem(Item.Arrow) < 0, $"a shot uses a round ({me.HeldStack.Data} left) and leaves no arrow behind");
            me.ReloadPistolRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == Cfg.PistolMag && me.Count(Item.PistolAmmo) == Cfg.PistolAmmoPerCraft - 1, "reload fills the magazine from the ammo");

            // C4 is buyable
            me.CraftRpc(Cfg.PowerBase + 2);
            yield return new WaitForSeconds(0.4f);
            Check(me.Count(Item.C4) == 1, "bought C4");

            // fortify: a foundation and a wall, then every wooden piece turns to stone
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(0.8f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 0);
            yield return new WaitForSeconds(0.8f);
            int wood = 0;
            foreach (var s in Structure.All) if (s.Team.Value == team && s.Upgradable && s.Tier.Value == 0) wood++;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            me.CraftRpc(Cfg.PowerBase + 3);
            yield return new WaitForSeconds(0.5f);
            int stone = 0, still = 0;
            foreach (var s in Structure.All) if (s.Team.Value == team && s.Upgradable) { if (s.Tier.Value == 1) stone++; else still++; }
            Check(wood >= 2 && stone >= wood && still == 0, $"fortify turned {stone} wooden pieces to stone");

            // tree cracker (a Fun mode item now) fells a tree in one hit
            me.ServerGive(Item.TreeCracker, 1, Cfg.TreeCrackerUses);
            yield return new WaitForSeconds(0.4f);
            yield return Hold(me, Item.TreeCracker);
            var tree = Nearest(ResourceNode.Tree, me.transform.position);
            if (tree != null)
            {
                var dir = me.transform.position - tree.transform.position;
                dir.y = 0;
                dir.Normalize();
                pc.LocalTeleport(tree.transform.position + dir * 2.3f + Vector3.up * 0.1f, Quaternion.LookRotation(-dir).eulerAngles.y);
                yield return new WaitForSeconds(1.3f);
                me.MeleeRpc(true, tree.NetworkObject, tree.transform.position + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(0.6f);
                Check(tree.Amount.Value <= 0, "the tree cracker felled the tree in one hit");
            }
            else Log("FAIL: no tree for the tree cracker");

            // ender pearl: you land where it lands
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            me.ServerGive(Item.EnderPearl, 2);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.EnderPearl);
            var target = Cfg.BaseCenter[team] + Cfg.BackDir(team) * -12f + Vector3.right * 6f;
            target.y = MapBuilder.Height(target.x, target.z);
            me.ThrowItemRpc(Item.EnderPearl, me.EyePos, (target - me.EyePos).normalized * Cfg.EnderPearlSpeed);
            yield return new WaitForSeconds(0.3f);
            me.ThrownLandRpc(Item.EnderPearl, target, Vector3.up);
            yield return new WaitForSeconds(0.8f);
            Check(Vector3.Distance(me.transform.position, target) < 1.8f && me.Count(Item.EnderPearl) == 1, $"ender pearl teleported us ({Vector3.Distance(me.transform.position, target):0.0} m from where it landed)");

            // the power menu next to crafting
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            pc.MenuOpen = true;
            yield return Snap("arsenal_power_menu");
            pc.CloseMenu();
        }

        IEnumerator BuilderTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(Cfg.Builder && Cfg.PowerMenu, "Builder: power menu on");
            for (int i = 0; i < 5; i++) me.ServerGive(Item.Wood, 1000);
            // crafting anywhere (well outside the base), and it takes a while
            var outside = Cfg.BaseCenter[team] * 0.45f;
            outside.y = MapBuilder.Height(outside.x, outside.z) + 0.1f;
            pc.LocalTeleport(outside, 0f);
            yield return new WaitForSeconds(0.4f);
            Check(Cfg.BaseTeamAt(me.transform.position) < 0 && pc.CraftOpen, "crafting works outside the base");
            var plan = Cfg.GetRecipe(Cfg.RecipeIndex(Item.BuildingPlan));
            float secs = Cfg.CraftSeconds(plan);
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.4f);
            Check(me.CraftingItem.Value == (byte)Item.BuildingPlan && me.Count(Item.BuildingPlan) == 0 && secs > 0f, $"the building plan is being made ({secs:0.0}s)");
            me.CraftRpc(Cfg.RecipeIndex(Item.Spear));
            yield return new WaitForSeconds(0.2f);
            yield return Snap("builder_crafting_wait");
            yield return new WaitForSeconds(secs);
            Check(me.Count(Item.BuildingPlan) == 1 && me.Count(Item.Spear) == 0 && me.CraftingItem.Value == 0, "done after the wait (one thing at a time)");

            // wait for the ball
            while (g.S != GameState.BallLive) yield return null;
            yield return new WaitForSeconds(0.5f);
            Check(Ball.Instance != null, "the ball dropped");

            // a box of walls out in the open, one cell, pieces locking on to each other
            yield return Hold(me, Item.BuildingPlan);
            var spot = Cfg.BaseCenter[team] * 0.4f;
            int ci = BuildGrid.CellOf(spot.x), cj = BuildGrid.CellOf(spot.z);
            var cc = BuildGrid.CellCenter(ci, cj);
            pc.LocalTeleport(cc + new Vector3(-4.5f, 0.1f, -1.5f), 90f);
            yield return new WaitForSeconds(0.4f);
            var walls = new[] { new PieceKey(PieceKey.KEdge, ci, cj, 0, 0), new PieceKey(PieceKey.KEdge, ci - 1, cj, 0, 0), new PieceKey(PieceKey.KEdge, ci, cj, 0, 1), new PieceKey(PieceKey.KEdge, ci, cj - 1, 0, 1),
                new PieceKey(PieceKey.KEdge, ci, cj, 1, 0), new PieceKey(PieceKey.KEdge, ci + 1, cj, 1, 1) };
            int placed = 0;
            foreach (var k in walls)
            {
                int n0 = Structure.All.Count;
                me.PlaceRpc((byte)PieceType.Wall, k.I, k.J, k.L, k.D);
                yield return new WaitForSeconds(0.8f);
                if (Structure.All.Count > n0) placed++;
                else Log($"wall {k} was not placed");
            }
            Check(placed == walls.Length, $"built {placed}/{walls.Length} walls out in the open, one hanging off another at the top");

            // the ball inside the box counts as ours
            Ball.Instance.ServerDrop(cc + Vector3.up * 1.5f, Vector3.zero);
            yield return new WaitForSeconds(2f);
            Check(g.BallEnclosedBy.Value == team, $"the ball is inside our build (enclosed by {g.BallEnclosedBy.Value})");
            pc.LocalTeleport(cc + new Vector3(-9f, 7f, -9f), 45f);
            pc.SetLook(45f, 30f);
            yield return Snap("builder_ball_in_fort");
            g.DevSetTimeLeft(1f);
            yield return new WaitForSeconds(2.5f);
            Check(g.S == GameState.GameOver && g.Winner.Value == team, $"won with the ball in our own build ({g.EndReason.Value})");
        }

        IEnumerator FunTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(Cfg.FunRules && !Cfg.PowerMenu, $"{Cfg.RulesName(Cfg.Rules)}: no power menu, normal prices");
            Cfg.FunItemInterval = 2f;
            int Items()
            {
                int n = 0;
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (!me.SlotAt(i).Empty) n++;
                return n;
            }
            int before = Items();
            yield return new WaitForSeconds(Mathf.Max(0f, (float)(g.NextFunItem.Value - me.NetworkManager.ServerTime.Time)) + 2.6f); // the first one was already due in 9 s
            Check(Items() > before && g.NextFunItem.Value > 0, $"free items handed out ({before} -> {Items()} slots used)");
            pc.MenuOpen = true;
            yield return Snap("mode_" + Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "") + "_inventory");
            pc.CloseMenu();
        }

        // ------------------------------------------------------------------ THEME MAPS

        IEnumerator MapsRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            string name = ThemeMaps.Label(Cfg.Map).ToLower();
            Log($"theme map {Cfg.MapLabel}");
            Check(ThemeMaps.IsTheme, "a theme map is loaded");
            bool flat = Mathf.Abs(MapBuilder.Height(Cfg.BaseCenter[team].x, Cfg.BaseCenter[team].z)) < 0.01f && Mathf.Abs(MapBuilder.Height(0, 0)) < 0.01f;
            Check(flat, "bases and the ball drop zone are flat");
            bool sym = true;
            var rng = new System.Random(5);
            for (int i = 0; i < 200; i++)
            {
                float x = (float)rng.NextDouble() * 2f * Cfg.MapHalf - Cfg.MapHalf, z = (float)rng.NextDouble() * 2f * Cfg.MapHalf - Cfg.MapHalf;
                sym &= Mathf.Abs(MapBuilder.Height(x, z) - MapBuilder.Height(-x, -z)) < 0.01f;
            }
            Check(sym, "the terrain is the same for both teams");
            int bad = 0, nodes = 0;
            foreach (var n in ResourceNode.All)
            {
                nodes++;
                var p = n.transform.position;
                if (ThemeMaps.WaterAt(p.x, p.z) || ThemeMaps.LavaAt(p.x, p.z)) bad++;
            }
            Check(nodes > 10 && bad == 0, $"{nodes} resource nodes, none in water or lava");

            // views: from high up, and at ground level looking at the middle
            pc.LocalTeleport(new Vector3(Cfg.MapHalf * 0.55f, 45f, Cfg.BaseCenter[team].z - 10f), 0f);
            pc.SetLook(-30f, 32f);
            yield return Snap("map_" + name + "_1_overview");
            var ground = Cfg.BaseCenter[team] * 0.55f + new Vector3(8f, 0, 0);
            ground.y = MapBuilder.Height(ground.x, ground.z) + 0.2f;
            float yaw = Quaternion.LookRotation(-ground).eulerAngles.y;
            pc.LocalTeleport(ground, yaw);
            pc.SetLook(yaw, 4f);
            yield return Snap("map_" + name + "_2_ground");

            // a spot of each zone in our half (first sector)
            Vector3 Find(System.Func<float, float, bool> ok)
            {
                for (float r = 10f; r < Cfg.MapHalf; r += 1.5f)
                for (int a = 0; a < 72; a++)
                {
                    var d = Quaternion.Euler(0, a * 5f, 0) * Vector3.forward * r;
                    var p = new Vector3(d.x, 0, d.z);
                    if (!Cfg.InFirstSector(p, 2f) || !ok(p.x, p.z)) continue;
                    p.y = MapBuilder.Height(p.x, p.z);
                    return p;
                }
                return new Vector3(0, -999, 0);
            }

            switch (Cfg.Map)
            {
                case MapKind.Beach:
                {
                    var w = Find((x, z) => ThemeMaps.WaterAt(x, z) && ThemeMaps.WaterAt(x + 3, z) && ThemeMaps.WaterAt(x - 3, z) && ThemeMaps.WaterAt(x, z + 3) && ThemeMaps.WaterAt(x, z - 3));
                    Check(w.y > -50f, $"open water at {w}");
                    Check(Mathf.Approximately(ThemeMaps.SpeedMul(w), 0.6f), "wading is slow");
                    int bi = Cfg.RecipeIndex(Item.Boat);
                    Check(bi >= 0 && Cfg.GetRecipe(bi).Wood == ThemeMaps.BoatWood, "the boat is craftable for 1500 wood");
                    me.ServerGive(Item.Wood, 1000);
                    me.ServerGive(Item.Wood, 1000);
                    pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                    yield return new WaitForSeconds(0.3f);
                    me.CraftRpc(bi);
                    yield return new WaitForSeconds(0.4f);
                    Check(me.Count(Item.Boat) == 1, "crafted a boat");
                    yield return Hold(me, Item.Boat);
                    pc.LocalTeleport(w + new Vector3(0, 0.1f, -2.5f), 0f);
                    yield return new WaitForSeconds(0.5f);
                    int v0 = Vehicle.All.Count;
                    me.PlaceDeployableRpc((byte)Item.Boat, w, 0f);
                    yield return new WaitForSeconds(0.6f);
                    Vehicle boat = null;
                    foreach (var v in Vehicle.All) if (v.IsBoat) boat = v;
                    Check(boat != null && Mathf.Abs(boat.transform.position.y - (ThemeMaps.WaterY - 0.1f)) < 0.3f, $"the boat floats on the water ({(boat != null ? boat.transform.position.y : 0):0.00})");
                    Check(PlayerNet.DeployProblem(Item.Boat, team, Cfg.SpawnPos(team), 0f) != null, "boats can't go on land");
                    if (boat != null)
                    {
                        var bp = boat.transform.position;
                        pc.LocalTeleport(bp + new Vector3(-4f, 1.2f, -5f), 40f);
                        pc.SetLook(40f, 12f);
                        yield return Snap("map_beach_3_boat");
                    }
                    break;
                }
                case MapKind.Frostlake:
                {
                    var ice = Find((x, z) => ThemeMaps.OnIce(new Vector3(x, 0.1f, z)));
                    Check(ice.y > -50f, $"the frozen lake is ice at {ice}");
                    pc.LocalTeleport(ice + Vector3.up * 0.1f, 0f);
                    pc.SetLook(Quaternion.LookRotation(-ice).eulerAngles.y, 2f);
                    yield return Snap("map_frostlake_3_ice");
                    break;
                }
                case MapKind.Volcano:
                {
                    var lava = Find((x, z) => ThemeMaps.LavaAt(x, z) && ThemeMaps.LavaAt(x + 1, z) && ThemeMaps.LavaAt(x - 1, z));
                    Check(lava.y > -50f, $"lava at {lava}");
                    float hp = me.Health.Value;
                    pc.LocalTeleport(lava + Vector3.up * 0.1f, 0f);
                    yield return new WaitForSeconds(1.6f);
                    Check(me.Health.Value < hp || me.Dead.Value, $"lava burns ({hp:0} -> {me.Health.Value:0} HP)");
                    pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                    yield return new WaitForSeconds(0.5f);
                    var near = lava + (lava.normalized * -8f);
                    near.y = MapBuilder.Height(near.x, near.z) + 3f;
                    pc.LocalTeleport(near, Quaternion.LookRotation(lava - near).eulerAngles.y);
                    pc.SetLook(Quaternion.LookRotation(lava - near).eulerAngles.y, 15f);
                    yield return Snap("map_volcano_3_lava");
                    break;
                }
            }
            Log("maps test done");
            Application.Quit(0);
        }
    }
}
