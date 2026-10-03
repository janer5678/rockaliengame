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
            string rn = Cfg.RulesId(Cfg.Rules);
            // every mode: a floor (ceiling) can hang off the top of a ramp
            var ramp = new PieceKey(PieceKey.KStairs, 0, 0, 0, 0);
            Check(BuildGrid.IsSupported(new PieceKey(PieceKey.KFloor, 1, 0, 1, 0), k => k.Equals(ramp)) && BuildGrid.IsSupported(new PieceKey(PieceKey.KFloor, 0, -1, 1, 0), k => k.Equals(ramp)),
                "a ceiling can be built off a ramp");
            // every mode: the wild respawn is anywhere out in the wild
            var spots = new System.Collections.Generic.HashSet<int>();
            bool allWild = true;
            for (int i = 0; i < 40; i++)
            {
                NetGame.WildSpawnPoint(team, out var wp, out _);
                allWild &= Cfg.BaseTeamAt(wp) < 0 && new Vector2(wp.x, wp.z).magnitude >= 14f;
                spots.Add(Cfg.RegionOf(wp) * 1000 + Mathf.RoundToInt(wp.x / 10f) * 37 + Mathf.RoundToInt(wp.z / 10f));
            }
            Check(allWild && spots.Count >= 25, $"wild respawns are random ({spots.Count} different spots out of 40, none in a base)");
            Log($"game mode {Cfg.RulesName(Cfg.Rules)} on {Cfg.MapLabel}");
            Check(Cfg.MapKey >> Cfg.RulesShift != 0 || Cfg.Rules == GameRules.Classic, "the game mode is in the map key (synced to clients)");
            {
                var k0 = Cfg.MapKey;
                var rules0 = Cfg.Rules;
                Cfg.SetMap(k0, Cfg.MapSeed);
                Check(Cfg.Rules == rules0, "the game mode survives the map key round trip");
            }
            // prices and order asked for
            Check(Cfg.SpearWood == 100 && Cfg.ArmorWood == 250 && Cfg.ArmorHp == 50 && Cfg.SaddleWood == 750 && Cfg.RamUses == 1,
                $"spear 100, armour 250 (+50 HP), saddle 750, ram 1 hit ({Cfg.SpearWood}, {Cfg.ArmorWood}, {Cfg.ArmorHp}, {Cfg.SaddleWood}, {Cfg.RamUses})");
            if (!Cfg.LimitedCrafting) Check(Cfg.RecipeIndex(Item.Chest) >= 0 && Cfg.RecipeIndex(Item.Chest) < Cfg.RecipeIndex(Item.Bow), "the storage chest is above the bow in crafting");
            // the glass wall: nobody hurts anyone on the other side of it
            if (MapBuilder.GlassUp)
                Check(PlayerNet.GlassBetween(Cfg.BaseCenter[0], Cfg.BaseCenter[1]) && !PlayerNet.GlassBetween(Cfg.BaseCenter[0], Cfg.BaseCenter[0] + Vector3.right), "no hitting through the glass wall");
            // high external walls stay out of the enemy base, even poking in from the edge
            if (!Cfg.Builder)
            {
                var eb = Cfg.BaseCenter[1 - team];
                var edge = eb + (Vector3.zero - eb).normalized * (Cfg.BaseHalf + 1.2f);
                edge.y = MapBuilder.Height(edge.x, edge.z);
                // the wall runs straight at the base: its middle is outside, its far end pokes 0.9 m in
                float yaw = Quaternion.LookRotation((Vector3.zero - eb).normalized).eulerAngles.y + 90f;
                var why = PlayerNet.DeployProblem(Item.Barrier, team, edge, yaw);
                Check(why == "Not in the enemy base", $"a high external wall poking into the enemy base is refused ({why ?? "allowed"})");
            }
            if (Cfg.Rules == GameRules.Classic || Cfg.Rules == GameRules.Arsenal || Cfg.Rules == GameRules.AutoWood || Cfg.Rules == GameRules.Dna)
                yield return RequestTests(me, pc, g, team);
            switch (Cfg.Rules)
            {
                case GameRules.Classic: break;
                case GameRules.Arsenal: yield return ArsenalTests(me, pc, g, team); yield return BlastAndRamTests(me, pc, g, team); yield return SuicideTest(me, pc, g); break;
                case GameRules.AutoWood: yield return AutoWoodTests(me, pc, g, team); yield return ArsenalTests(me, pc, g, team); break;
                case GameRules.Tutorial: yield return TutorialTests(me, pc, g, team); break;
                case GameRules.Dna: yield return DnaTests(me, pc, g, team); break;
                case GameRules.Primitive:
                case GameRules.FunRandomLimited:
                case GameRules.BuildingPrimitive:
                {
                    var names = new System.Text.StringBuilder();
                    bool only = Cfg.RecipeCount == 4;
                    for (int i = 0; i < Cfg.RecipeCount; i++)
                    {
                        var o = Cfg.GetRecipe(i).Output;
                        names.Append(o).Append(' ');
                        only &= o == Item.Hatchet || o == Item.Spear || o == Item.BuildingPlan || o == Item.Ram;
                    }
                    Check(only && Cfg.PowerCount == 0, $"{Cfg.RulesName(Cfg.Rules)}: only {names}can be crafted");
                    pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                    yield return new WaitForSeconds(0.3f);
                    pc.MenuOpen = true;
                    yield return Snap("mode_" + rn + "_crafting");
                    pc.CloseMenu();
                    if (Cfg.Rules == GameRules.FunRandomLimited) yield return FunTests(me, pc, g, team);
                    else if (Cfg.Rules == GameRules.BuildingPrimitive)
                    {
                        Check(Cfg.Builder && !Cfg.PowerMenu && Machine.ByTeam[team] == null, "Building Primitive: Builder's rules (no bases), no power items");
                        me.ServerGive(Item.Wood, 1000);
                        yield return new WaitForSeconds(0.2f);
                        me.CraftRpc(Cfg.RecipeIndex(Item.Spear));
                        me.CraftRpc(Cfg.RecipeIndex(Item.Hatchet));
                        yield return new WaitForSeconds(0.4f);
                        Check(me.CraftingItem.Value == (byte)Item.Spear && me.CraftQueue.Count == 1, "crafting takes a while, with the queue");
                        yield return Snap("mode_buildingprimitive_queue");
                    }
                    else Check(!Cfg.FunRules && NetGame.Instance.NextFunItem.Value < 0, "Primitive is the normal game otherwise (no free items)");
                    break;
                }
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
            Check(Cfg.PowerMenu && Cfg.PowerCount == 7 && Cfg.PowerIndex(Item.Pistol) < 0 && Cfg.PowerIndex(Item.FortifyBuff) < 0 && hat.Wood == Cfg.HatchetWood && xbow.Wood == 350, $"{Cfg.RulesName(Cfg.Rules)}: power menu ({Cfg.PowerCount} items, no pistol, no fortify - it's in UPGRADES), normal prices (hatchet {hat.Wood} wood), crossbow {xbow.Wood} wood");
            Check(Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.Sword)).Wood == 500 && Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.C4)).Wood == 2500 && Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.Helmet)).Wood == 800
                && Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.Shotgun)).Wood == 2000 && Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.ShotgunShell)).Wood == 250
                && Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.Revolver)).Wood == 2500 && Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.RevolverAmmo)).Wood == 200,
                "power prices: sword 500, C4 2500, helmet 800, shotgun 2000 + 250 a shell, revolver 2500 + 200 a bullet");
            Check(Cfg.MeleePlayerDamage(Item.Sword, true) == 150f && Cfg.MeleePlayerDamage(Item.Sword, false) == 95f && Cfg.Melee(Item.Sword).Cooldown > Cfg.Melee(Item.Hatchet).Cooldown,
                "sword: 150 head, 95 body, a slower swing");
            Check(Mathf.Abs(Cfg.ShotgunPellets * Cfg.ShotgunPelletDamage * Cfg.ShotgunFalloff(0.9f) - 200f) < 0.5f && Cfg.ShotgunFalloff(10f) < 0.7f, "shotgun: 200 within a metre, less further out");
            Check(Cfg.PieceWood(PieceType.Wall) == Cfg.WallWood, $"building pieces at their normal price (wall {Cfg.PieceWood(PieceType.Wall)} wood)");
            for (int i = 0; i < 18; i++) me.ServerGive(Item.Wood, 1000); // leaves room in the 24 slots for what gets bought
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            int w0 = me.Count(Item.Wood);

            // the revolver (5 rounds, comes empty, bullets bought one at a time)
            yield return BenchBuy(me, pc, Item.Revolver);
            Check(me.Count(Item.Revolver) == 1 && w0 - me.Count(Item.Wood) == Cfg.RevolverWood, $"bought a revolver ({w0 - me.Count(Item.Wood)} wood)");
            yield return Hold(me, Item.Revolver);
            Check(me.HeldStack.Data == 0 && Cfg.RevolverMag == 5, $"the revolver comes empty and holds 5 ({me.HeldStack.Data})");
            for (int i = 0; i < Cfg.RevolverMag; i++) yield return BenchBuy(me, pc, Item.RevolverAmmo);
            me.ReloadPistolRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == Cfg.RevolverMag && me.Count(Item.RevolverAmmo) == 0, $"bought {Cfg.RevolverMag} bullets and loaded them ({me.HeldStack.Data})");
            yield return Snap("arsenal_revolver");
            var fwd = me.transform.forward;
            me.FirePistolRpc(false, default, me.EyePos + fwd * 30f, fwd);
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == Cfg.RevolverMag - 1 && FindWorldItem(Item.Arrow) < 0, $"a shot uses a round ({me.HeldStack.Data} left), hitscan with nothing left behind");
            me.ReloadPistolRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == Cfg.RevolverMag - 1, "no reloading without bullets");
            yield return BenchBuy(me, pc, Item.RevolverAmmo);
            me.ReloadPistolRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == Cfg.RevolverMag && me.Count(Item.RevolverAmmo) == 0, "bought a bullet and reloaded");

            // the waterpipe shotgun: one shell at a time
            yield return BenchBuy(me, pc, Item.Shotgun);
            yield return BenchBuy(me, pc, Item.ShotgunShell);
            yield return BenchBuy(me, pc, Item.ShotgunShell);
            yield return Hold(me, Item.Shotgun);
            Check(me.HeldStack.Data == 0 && me.Count(Item.ShotgunShell) == 2, "bought a shotgun (empty) and 2 shells");
            me.ReloadShotgunRpc();
            yield return new WaitForSeconds(0.3f);
            me.ReloadShotgunRpc();
            yield return new WaitForSeconds(0.3f);
            Check(me.HeldStack.Data == 1 && me.Count(Item.ShotgunShell) == 1, "it takes one shell per reload");
            me.FireShotgunRpc(me.transform.forward);
            yield return new WaitForSeconds(0.4f);
            Check(me.HeldStack.Data == 0, "fired the shell");
            yield return Snap("arsenal_shotgun");

            // the sword, C4 and the headshot helmet
            yield return BenchBuy(me, pc, Item.Sword);
            yield return BenchBuy(me, pc, Item.C4);
            me.HelmetHp.Value = 0;
            yield return BenchBuy(me, pc, Item.Helmet);
            Check(me.Count(Item.Sword) == 1 && me.Count(Item.C4) == 1, "bought a sword and C4");
            Check(me.HelmetHp.Value == 1 && me.Count(Item.Helmet) == 0, $"the alien helmet goes straight on when it's crafted, like armour (wearing {me.HelmetHp.Value}, {me.Count(Item.Helmet)} in the bag)");
            int hw0 = me.Count(Item.Wood);
            me.CraftRpc(Cfg.CraftIndexOf(Item.Helmet));
            yield return new WaitForSeconds(0.4f);
            Check(me.Count(Item.Wood) == hw0 && me.CraftingItem.Value != (byte)Item.Helmet, "a second helmet is refused while you're wearing one (nothing paid)");
            me.HelmetHp.Value = 0;
            yield return Hold(me, Item.Sword);
            yield return Snap("arsenal_sword");

            // two loaded crossbows: firing one makes the other wait for the reload too
            {
                me.ServerGive(Item.Arrow, 5);
                for (int i = 0; i < Cfg.HotbarSize; i++) if (me.Inv[i].Id != Item.Arrow) me.Inv[i] = default; // room on the hotbar
                for (int i = 0; i < 2; i++) me.ServerGive(Item.Crossbow, 1, 1);
                yield return new WaitForSeconds(0.3f);
                int a = -1, b = -1;
                for (int i = 0; i < Cfg.HotbarSize; i++) if (me.SlotAt(i).Id == Item.Crossbow) { if (a < 0) a = i; else if (b < 0) b = i; }
                if (a >= 0 && b >= 0)
                {
                    me.HeldSlot.Value = (byte)a;
                    yield return new WaitForSeconds(0.3f);
                    me.FireCrossbowRpc(me.EyePos, me.transform.forward * Cfg.CrossbowSpeed);
                    yield return new WaitForSeconds(0.35f);
                    me.HeldSlot.Value = (byte)b;
                    yield return new WaitForSeconds(0.2f);
                    me.FireCrossbowRpc(me.EyePos, me.transform.forward * Cfg.CrossbowSpeed);
                    yield return new WaitForSeconds(0.3f);
                    Check(me.SlotAt(a).Data == 0 && me.SlotAt(b).Data == 1, "the second crossbow can't fire straight after the first (shared reload)");
                    yield return new WaitForSeconds(Cfg.CrossbowReload);
                    me.FireCrossbowRpc(me.EyePos, me.transform.forward * Cfg.CrossbowSpeed);
                    yield return new WaitForSeconds(0.3f);
                    Check(me.SlotAt(b).Data == 0, "after the reload time it fires");
                }
                else Log("FAIL: no room for two crossbows");
            }

            Check(Cfg.Melee(Item.Rock).Cooldown == Cfg.Melee(Item.Hatchet).Cooldown, $"the rock swings at the hatchet's speed ({Cfg.Melee(Item.Rock).Cooldown}s)");
            // fortify: a foundation and a wall, then every wooden piece turns to stone
            for (int i = 0; i < 8; i++) me.ServerGive(Item.Wood, 1000); // (topped up: the 24 slots can't hold it all up front)
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
            // a chest pushed right up against that wall: too tight at the exact spot, so it slides into the nearest free one
            {
                BuildGrid.Pose(PieceType.Wall, new PieceKey(PieceKey.KEdge, ci, cj, 0, 0), out var wallPos, out _);
                var tight = new Vector3(wallPos.x - 0.35f, BuildGrid.LevelY(0), wallPos.z);
                Physics.SyncTransforms();
                bool raw = PlayerNet.DeployProblem(Item.Chest, team, tight, 90f) != null;
                var spot = tight;
                bool ok = PlayerNet.FindDeploySpot(Item.Chest, team, ref spot, 90f, out var why);
                Check(raw && ok && Vector3.Distance(spot, tight) < 0.75f, $"a chest aimed right against a wall slides {Vector3.Distance(spot, tight):0.00} m to fit ({why ?? "placed"})");
            }
            // stairs, then from half way up them the ceiling next to the top already shows green
            {
                FreeCell(team, 2, out int si, out int sj);
                pc.LocalTeleport(BuildGrid.CellCenter(si, sj) + new Vector3(-4f, 0.1f, -1.5f), 0);
                yield return new WaitForSeconds(0.4f);
                me.PlaceRpc((byte)PieceType.Foundation, si, sj, 0, 0);
                yield return new WaitForSeconds(0.8f);
                me.PlaceRpc((byte)PieceType.Stairs, si, sj, 0, 0);
                yield return new WaitForSeconds(0.8f);
                pc.BuildPiece = PieceType.Floor;
                // a little way up the stairs, looking up at where the ceiling next to them goes
                var mid = BuildGrid.CellCenter(si, sj) + Vector3.up * (BuildGrid.LevelY(0) + 0.5f);
                var ceil = BuildGrid.CellCenter(si - 1, sj) + Vector3.up * BuildGrid.LevelY(1);
                pc.LocalTeleport(mid, 270f);
                yield return new WaitForSeconds(0.4f);
                var look = Quaternion.LookRotation(ceil - me.EyePos).eulerAngles;
                pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
                yield return new WaitForSeconds(0.2f);
                Check(pc.GhostOk && pc.GhostKey.L == 1, $"from low on the stairs the ceiling shows green ({pc.GhostKey}, feet at {me.transform.position.y:0.0})");
                yield return Snap("arsenal_ceiling_from_stairs");
                pc.BuildPiece = PieceType.Foundation;
            }
            int wood = 0;
            foreach (var s in Structure.All) if (s.Team.Value == team && s.Upgradable && s.Tier.Value == 0) wood++;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            // fortify (UPGRADES at the alien machine) goes up a step every time it's bought: stone 1000, metal 2000, refined 2500
            int[] price = { 1000, 2000, 2500 };
            string[] names = { "Stone Wall", "Metal Wall", "Refined Wall" };
            for (int step = 0; step < 3; step++)
            {
                int before = me.Count(Item.Wood);
                Check(Cfg.BaseUpgradeRecipe(Item.FortifyBuff, team).Wood == price[step], $"fortify step {step + 1} costs {Cfg.BaseUpgradeRecipe(Item.FortifyBuff, team).Wood}");
                yield return UpgradeBuy(me, pc, Item.FortifyBuff);
                yield return new WaitForSeconds(0.3f);
                int up = 0, still = 0;
                foreach (var s in Structure.All) if (s.Team.Value == team && s.Upgradable) { if (s.Tier.Value == step + 1) up++; else still++; }
                var w = Structure.All.Find(s => s.Team.Value == team && s.PType == PieceType.Wall);
                Check(wood >= 2 && up >= wood && still == 0 && before - me.Count(Item.Wood) == price[step] && w != null && w.DisplayName == names[step] && Mathf.Approximately(w.Health.Value, Cfg.PieceHp(PieceType.Wall, step + 1)),
                    $"fortify {step + 1}: {up} pieces to {names[step]} for {before - me.Count(Item.Wood)} wood ({(w != null ? w.Health.Value : 0):0} HP)");
            }
            {
                int before = me.Count(Item.Wood);
                yield return UpgradeBuy(me, pc, Item.FortifyBuff);
                yield return new WaitForSeconds(0.2f);
                Check(me.Count(Item.Wood) == before, "a fourth fortify isn't sold (already refined)");
            }
            // a refined piece takes 4 ram hits: each knocks it down one step
            {
                var w = Structure.All.Find(s => s.Team.Value == team && s.PType == PieceType.Wall);
                if (w != null)
                {
                    w.ServerDowngrade();
                    Check(w.Tier.Value == 2 && w.DisplayName == "Metal Wall", "a ram hit knocks refined down to metal");
                    w.ServerUpgrade(3);
                }
            }
            // pieces built after fortifying come out fortified too
            {
                yield return Hold(me, Item.BuildingPlan);
                pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0);
                yield return new WaitForSeconds(0.4f);
                var k2 = new PieceKey(PieceKey.KEdge, ci, cj, 0, 1);
                me.PlaceRpc((byte)PieceType.Wall, k2.I, k2.J, k2.L, k2.D);
                yield return new WaitForSeconds(0.8f);
                BuildGrid.Registry.TryGetValue(k2, out var fresh);
                Check(fresh != null && fresh.Tier.Value == 3, $"a wall built after fortifying is refined straight away (tier {(fresh != null ? fresh.Tier.Value : -1)})");
                yield return Snap("arsenal_fortify_looks");
            }

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

        /// <summary>Prices, arrows, your own C4 hitting your own base, and the ram going through a row of high external walls.</summary>
        IEnumerator BlastAndRamTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            var arrows = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Arrow));
            Check(arrows.Wood == 50 && arrows.Count == 5, $"arrows: {arrows.Count} for {arrows.Wood} wood");
            Check(Cfg.RevolverHeadDamage == 50f && Cfg.RevolverBodyDamage == 30f && Cfg.RevolverMag == 5, "revolver: 5 rounds, 50 head, 30 body");
            // the bow: an instant shot is weak and short, a full draw hits hard
            Check(Mathf.Approximately(Cfg.BowDamage(Cfg.BowMinSpeed), Cfg.BowMinDamage) && Mathf.Approximately(Cfg.BowDamage(1f), Cfg.ArrowPlayerDamage)
                && Cfg.BowDamage(Mathf.Lerp(Cfg.BowMinSpeed, 1f, 0.5f)) < Cfg.ArrowPlayerDamage * 0.4f && Cfg.BowMinSpeed < 0.5f,
                $"bow: {Cfg.BowDamage(Cfg.BowMinSpeed):0} instantly, {Cfg.BowDamage(Mathf.Lerp(Cfg.BowMinSpeed, 1f, 0.5f)):0} half drawn, {Cfg.BowDamage(1f):0} full");

            // C4 against fortified walls: sheet metal goes in the layer it's on (not behind), refined only where it's stuck
            for (int tier = 2; tier <= 3; tier++)
            {
                var spot = Cfg.BaseCenter[team] + new Vector3(tier == 2 ? 14f : -14f, 0, 16f * Mathf.Sign(-Cfg.BaseCenter[team].z));
                spot.y = MapBuilder.Height(spot.x, spot.z);
                Structure Wall(Vector3 at)
                {
                    var go = Instantiate(Bootstrap.I.structurePrefab, at, Quaternion.identity);
                    var s = go.GetComponent<Structure>();
                    s.ServerInit(PieceType.Wall, 1 - team, default, false);
                    go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                    s.ServerUpgrade(tier);
                    return s;
                }
                var mid = Wall(spot);
                var left = Wall(spot + new Vector3(-3f, 0, 0));
                var right = Wall(spot + new Vector3(3f, 0, 0));
                var behind = Wall(spot + new Vector3(0, 0, 3f));
                yield return new WaitForSeconds(0.3f);
                pc.LocalTeleport(spot + new Vector3(0, 0.1f, -16f), 0f);
                pc.SetLook(0f, 0f);
                g.ServerArmC4(spot + new Vector3(0, 1.5f, -0.2f), Vector3.back, me);
                yield return Snap($"fortify_tier{tier}");
                yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                bool Gone(Structure s) => s == null || !s.IsSpawned;
                if (tier == 2) Check(Gone(mid) && Gone(left) && Gone(right) && !Gone(behind), $"C4 on sheet metal: that wall and the ones beside it go, the one behind stays (mid {Gone(mid)}, left {Gone(left)}, right {Gone(right)}, behind {Gone(behind)})");
                else Check(Gone(mid) && !Gone(left) && !Gone(right) && !Gone(behind), "C4 on refined: only the wall it's stuck to goes");
            }
            Check(Cfg.TierBlastMul(3) < Cfg.TierBlastMul(2) && Cfg.TierBlastMul(2) < 1f, "rockets do less to sheet metal and less again to refined");

            // our own C4 blows up our own high external wall and our own pieces
            var bc = Cfg.BaseCenter[team];
            var own = bc + new Vector3(-12f, 0, 12f * Mathf.Sign(-bc.z));
            own.y = MapBuilder.Height(own.x, own.z);
            var ogo = Instantiate(Bootstrap.I.structurePrefab, own, Quaternion.identity);
            ogo.GetComponent<Structure>().ServerInit(PieceType.Barrier, team, default, false);
            ogo.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
            var ownWall = ogo.GetComponent<Structure>();
            g.ServerArmC4(own + new Vector3(0, 1f, -0.3f), Vector3.back, me);
            pc.LocalTeleport(own + new Vector3(0, 0.1f, -14f), 0f);
            yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
            Check(ownWall == null || !ownWall.IsSpawned, "our own C4 blew up our own high external wall");

            // five enemy high external walls stacked one behind another, and two beside them: the ram takes the whole row
            var row0 = bc + new Vector3(10f, 0, 6f * Mathf.Sign(-bc.z));
            row0.y = MapBuilder.Height(row0.x, row0.z);
            var rowWalls = new System.Collections.Generic.List<Structure>();
            Structure Spawn(Vector3 at)
            {
                var go = Instantiate(Bootstrap.I.structurePrefab, at, Quaternion.identity);
                go.GetComponent<Structure>().ServerInit(PieceType.Barrier, 1 - team, default, false);
                go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                return go.GetComponent<Structure>();
            }
            for (int i = 0; i < 5; i++) rowWalls.Add(Spawn(row0 + new Vector3(0, 0, 0.6f * i)));
            var besideA = Spawn(row0 + new Vector3(4.2f, 0, 0));
            var besideB = Spawn(row0 + new Vector3(-4.2f, 0, 0.6f));
            me.ServerGive(Item.Ram, 1, Cfg.MaxData(Item.Ram));
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Ram);
            pc.LocalTeleport(row0 + new Vector3(0, 0.1f, -2f), 0f);
            yield return new WaitForSeconds(Cfg.RamWindup + 0.3f);
            me.RamStrikeRpc(rowWalls[0].NetworkObject, row0 + new Vector3(0, 1.5f, -0.2f));
            yield return new WaitForSeconds(0.5f);
            int gone = 0;
            foreach (var w in rowWalls) if (w == null || !w.IsSpawned) gone++;
            bool sidesOk = besideA != null && besideA.IsSpawned && besideB != null && besideB.IsSpawned;
            Check(gone == 5 && sidesOk, $"the ram smashed {gone}/5 walls stacked in a row and left the ones beside them");
        }

        IEnumerator SuicideTest(PlayerNet me, PlayerController pc, NetGame g)
        {
            if (me.Dead.Value) yield break;
            // standing in your base heals you
            me.Health.Value = 50f;
            yield return new WaitForSeconds(2.2f);
            Check(me.Health.Value > 52f, $"your base heals you slowly (50 -> {me.Health.Value:0})");
            // chat
            me.ChatRpc(new Unity.Collections.FixedString128Bytes("hello from the test"), false);
            yield return new WaitForSeconds(0.6f);
            Check(Chat.LastLine.Contains("hello from the test") && !Chat.LastLine.Contains("TEAM CHAT"), $"Enter chat goes to everyone ({Chat.LastLine})");
            me.ChatRpc(new Unity.Collections.FixedString128Bytes("team only"), true);
            yield return new WaitForSeconds(0.6f);
            Check(Chat.LastLine.Contains("(TEAM CHAT)") && Chat.LastLine.Contains("team only") && PlayerNet.LastTeamChatTo >= 1, $"T chat is team chat: \"(TEAM CHAT)\" before the name ({Chat.LastLine}, to {PlayerNet.LastTeamChatTo})");
            int graves0 = g.Graves.Count;
            var diedAt = me.transform.position;
            me.SuicideRpc();
            yield return new WaitForSeconds(0.5f);
            Check(me.Dead.Value, "suicide from the pause menu kills you");
            yield return GraveTests(me, pc, g, graves0, diedAt); // (waits out the respawn)
            me.SuicideRpc();
            yield return new WaitForSeconds(0.5f);
            Check(!me.Dead.Value, "no second suicide within 30 seconds");
        }

        /// <summary>Auto Wood: Arsenal, plus a pile of wood that grows at your base by itself.</summary>
        IEnumerator AutoWoodTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(Cfg.AutoWood && Cfg.PowerMenu, "Auto Wood: Arsenal's power menu");
            while (g.S != GameState.PreBall && g.S != GameState.BallLive) yield return null;
            yield return new WaitForSeconds(3.5f);
            int best = -1, count = 0;
            foreach (var it in g.Items)
                if (it.Stack.Id == Item.Wood && Cfg.BaseTeamAt(it.Pos) == team && it.Stack.Count > count) { best = it.Id; count = it.Stack.Count; }
            Check(best >= 0 && count >= Cfg.AutoWoodPerSecond * 2 && count % Cfg.AutoWoodPerSecond == 0, $"a wood pile grew at our base ({count} wood, one stack)");
            yield return new WaitForSeconds(1.2f);
            int now = 0;
            foreach (var it in g.Items) if (it.Id == best) now = it.Stack.Count;
            Check(now > count, $"it keeps stacking up ({count} -> {now})");
            var pile = default(DroppedItem);
            foreach (var it in g.Items) if (it.Id == best) pile = it;
            pc.LocalTeleport(pile.Pos + Vector3.up * 0.1f - Cfg.BackDir(team) * 1.2f, Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            int w0 = me.Count(Item.Wood);
            me.PickupItemRpc(best);
            yield return new WaitForSeconds(0.4f);
            Check(me.Count(Item.Wood) >= w0 + now, $"picked up the pile ({me.Count(Item.Wood) - w0} wood)");
            yield return Snap("autowood_pile");
            // the pile keeps stacking past 1000, and splits into stacks when it's picked up
            yield return new WaitForSeconds(1.2f);
            int big = -1;
            for (int i = 0; i < g.Items.Count; i++) if (g.Items[i].Stack.Id == Item.Wood && Cfg.BaseTeamAt(g.Items[i].Pos) == team) big = i;
            if (big >= 0)
            {
                var it = g.Items[big];
                it.Stack = it.Stack.WithCount(2500);
                g.Items[big] = it;
                yield return new WaitForSeconds(1.2f);
                int c2 = 0;
                foreach (var x in g.Items) if (x.Id == it.Id) c2 = x.Stack.Count;
                Check(c2 > 2500, $"the pile stacks past 1000 ({c2})");
                int stacks0 = 0;
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Wood) stacks0++;
                int wb = me.Count(Item.Wood);
                me.PickupItemRpc(it.Id);
                yield return new WaitForSeconds(0.4f);
                int stacks1 = 0;
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Wood) stacks1++;
                Check(me.Count(Item.Wood) - wb >= 2500 && stacks1 >= stacks0 + 2, $"picked up {me.Count(Item.Wood) - wb} wood into {stacks1 - stacks0} more stacks");
            }
            else Log("FAIL: no wood pile");
            // wood gen upgrades (UPGRADES at the alien machine): 1000, then 3000 - faster each time
            for (int i = 0; i < 5; i++) me.ServerGive(Item.Wood, 1000);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            int[] cost = { 1000, 3000 };
            var wm = FindAnyObjectByType<WoodMachine>();
            Check(wm != null, "the wood machine stands next to the alien machine");
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            {
                var look = Quaternion.LookRotation(Cfg.WoodMachinePos(team) + Vector3.up * 0.8f - me.EyePos).eulerAngles;
                pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
            }
            yield return Snap("autowood_machine_0");
            yield return WoodMachineCloseUp(me, pc, team, 0);
            for (int lvl = 0; lvl < 2; lvl++)
            {
                int before = me.Count(Item.Wood);
                yield return UpgradeBuy(me, pc, Item.WoodGenBuff);
                yield return new WaitForSeconds(0.2f);
                Check(g.WoodGenLevelOf(team) == lvl + 1 && before - me.Count(Item.Wood) == cost[lvl] && Cfg.WoodGenRate(lvl + 1) > Cfg.WoodGenRate(lvl),
                    $"wood gen level {lvl + 1} for {before - me.Count(Item.Wood)} wood: {Cfg.WoodGenRate(lvl + 1)} a second");
                yield return new WaitForSeconds(0.6f);
                yield return Snap("autowood_machine_" + (lvl + 1));
                yield return WoodMachineCloseUp(me, pc, team, lvl + 1);
            }
            {
                int before = me.Count(Item.Wood);
                yield return UpgradeBuy(me, pc, Item.WoodGenBuff);
                yield return new WaitForSeconds(0.2f);
                Check(me.Count(Item.Wood) == before && g.WoodGenLevelOf(team) == 2, "only two wood gen levels");
            }
            for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default; // (room for the Arsenal tests that follow)
        }

        /// <summary>A close look at the wood machine from the front-left, then back to the spawn view.</summary>
        IEnumerator WoodMachineCloseUp(PlayerNet me, PlayerController pc, int team, int level)
        {
            var rot = Quaternion.LookRotation(-Cfg.BackDir(team));
            var wmPos = Cfg.WoodMachinePos(team);
            var from = wmPos + rot * new Vector3(-1.6f, 0, 3.4f);
            pc.LocalTeleport(from, 0f);
            yield return new WaitForSeconds(0.3f);
            var look = Quaternion.LookRotation(wmPos + Vector3.up * 1.0f - me.EyePos).eulerAngles;
            pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
            yield return Snap("autowood_machine_close_" + level);
            // from the side: a log dropping off the end of the chute onto the pile out in front
            WoodMachine wmc = null;
            foreach (var w in FindObjectsByType<WoodMachine>(FindObjectsSortMode.None))
                if (wmc == null || (w.transform.position - wmPos).sqrMagnitude < (wmc.transform.position - wmPos).sqrMagnitude) wmc = w;
            if (wmc != null)
            {
                var land = wmPos + rot * WoodMachine.LandingLocal;
                var sideAt = wmPos + rot * new Vector3(1.15f, 0, 3.1f); // (front right, on the bedrock clear of the socket)
                pc.LocalTeleport(sideAt, 0f);
                yield return new WaitForSeconds(0.3f);
                var lk = Quaternion.LookRotation(Vector3.Lerp(wmPos, land, 0.55f) + Vector3.up * 0.5f - me.EyePos).eulerAngles;
                pc.SetLook(lk.y, lk.x > 180f ? lk.x - 360f : lk.x);
                float until = Time.time + 3f;
                bool caught = false;
                while (Time.time < until)
                {
                    float ph = wmc.LogPhase;
                    if (ph > WoodMachine.LogDrop + 0.12f && ph < 0.93f) { caught = true; break; }
                    yield return null;
                }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "autowood_log_landing_" + level + ".png"));
                Log($"shot autowood_log_landing_{level} (log {(caught ? "falling off the chute" : "not caught mid-air")})");
                yield return null;
                var pile = Cfg.WoodTrayPos(team);
                var machineLocal = Quaternion.Inverse(rot) * (pile - wmPos);
                Check(caught && machineLocal.z > 1.4f, $"wood machine level {level}: logs drop off the end of the chute onto the pile well out in front ({machineLocal.z:0.00} m out)");
                yield return new WaitForSeconds(0.2f);
            }
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            var back = Quaternion.LookRotation(wmPos + Vector3.up * 0.8f - me.EyePos).eulerAngles;
            pc.SetLook(back.y, back.x > 180f ? back.x - 360f : back.x);
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
            // the building plan (and fortify) are instant
            me.CraftRpc(Cfg.RecipeIndex(Item.BuildingPlan));
            yield return new WaitForSeconds(0.4f);
            Check(me.Count(Item.BuildingPlan) == 1 && me.CraftingItem.Value == 0, "the building plan is instant");
            Check(Cfg.CraftSeconds(Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.FortifyBuff))) == 0f, "fortify all walls is instant");
            // everything else takes a while, one at a time, with a queue like Rust
            var spear = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Spear));
            var hatchet = Cfg.GetRecipe(Cfg.RecipeIndex(Item.Hatchet));
            float s1 = Cfg.CraftSeconds(spear), s2 = Cfg.CraftSeconds(hatchet);
            me.CraftRpc(Cfg.RecipeIndex(Item.Spear));
            yield return new WaitForSeconds(0.2f);
            me.CraftRpc(Cfg.RecipeIndex(Item.Hatchet));
            yield return new WaitForSeconds(0.4f);
            Check(me.CraftingItem.Value == (byte)Item.Spear && me.CraftQueue.Count == 1 && me.CraftQueue[0] == (byte)Item.Hatchet && me.Count(Item.Spear) == 0,
                $"the spear is being made ({s1:0.0}s) and the hatchet waits in the queue");
            yield return Snap("builder_crafting_queue");
            yield return new WaitForSeconds(s1);
            Check(me.Count(Item.Spear) == 1 && me.CraftingItem.Value == (byte)Item.Hatchet && me.CraftQueue.Count == 0, "spear done, the hatchet started straight after");
            yield return new WaitForSeconds(s2 + 0.3f);
            Check(me.Count(Item.Hatchet) == 1 && me.CraftingItem.Value == 0, "the queue emptied (one thing at a time)");

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

            Check(Machine.ByTeam[team] == null && !Cfg.PointBlocked(Cfg.BaseCenter[team]), "no base, no machine and no bedrock in Builder");

            // a chest goes down anywhere, even out in the open
            Check(PlayerNet.DeployProblem(Item.Chest, team, cc + new Vector3(-6f, 0, -6f), 0f) == null, "a chest can go anywhere");

            // the ram one-shots an enemy high external wall
            var wallAt = cc + new Vector3(-10f, 0, -10f);
            var bgo = Instantiate(Bootstrap.I.structurePrefab, wallAt, Quaternion.identity);
            bgo.GetComponent<Structure>().ServerInit(PieceType.Barrier, 1 - team, default, false);
            bgo.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
            var barrier = bgo.GetComponent<Structure>();
            me.ServerGive(Item.Ram, 1, Cfg.MaxData(Item.Ram));
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.Ram);
            pc.LocalTeleport(wallAt + new Vector3(0, 0.1f, -2f), 0f);
            yield return new WaitForSeconds(Cfg.RamWindup + 0.2f);
            me.RamStrikeRpc(barrier.NetworkObject, wallAt + new Vector3(0, 1.5f, -0.2f));
            yield return new WaitForSeconds(0.5f);
            Check(barrier == null || !barrier.IsSpawned, "the ram smashed the high external wall in one hit");

            // pick the ball up and plant it: a block and a flag in our colour, and it's ours
            var bp = Ball.Instance.transform.position;
            pc.LocalTeleport(bp + new Vector3(1.5f, 0.1f, 0), 270f);
            yield return new WaitForSeconds(0.4f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.CarryingBall, "picked up the ball");
            var ground = me.transform.position + me.transform.forward * 1.8f;
            ground.y = MapBuilder.Height(ground.x, ground.z);
            me.PlantBallRpc(ground);
            yield return new WaitForSeconds(1f);
            var ball = Ball.Instance;
            var block = GameObject.Find("ball block");
            Check(!ball.IsCarried && ball.SocketTeam.Value == team && block != null && block.activeInHierarchy && Mathf.Abs(ball.transform.position.y - (ground.y + Ball.PlinthH + Ball.Radius)) < 0.1f,
                $"planted the ball: it sits on a block in our colour and it's ours ({ball.SocketTeam.Value})");
            var eye = ground + new Vector3(3.5f, 1.2f, -3.5f);
            pc.LocalTeleport(eye, Quaternion.LookRotation(ground - eye).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(ground - eye).eulerAngles.y, 5f);
            yield return Snap("builder_ball_planted");
            // anyone can take it: picking it up makes it nobody's again and the block sinks back
            pc.LocalTeleport(ball.transform.position + new Vector3(1.5f, -Ball.PlinthH - Ball.Radius + 0.1f, 0), 270f);
            yield return new WaitForSeconds(0.3f);
            me.PickupBallRpc();
            yield return new WaitForSeconds(1f);
            Check(me.CarryingBall && ball.SocketTeam.Value == -1 && (block == null || !block.activeInHierarchy), "picking it up takes it back (the block and flag go away)");
            me.PlantBallRpc(ground);
            yield return new WaitForSeconds(0.8f);
            g.DevSetTimeLeft(1f);
            yield return new WaitForSeconds(2.5f);
            Check(g.S == GameState.GameOver && g.Winner.Value == team, $"won with the ball planted for us ({g.EndReason.Value})");
        }

        IEnumerator FunTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(Cfg.FunRules && !Cfg.PowerMenu, $"{Cfg.RulesName(Cfg.Rules)}: no power menu, normal prices");
            Check(g.S == GameState.BallLive && Ball.Instance != null && Mathf.Abs(g.TimeLeft - Cfg.FastMatchLength) < 15f, $"fun modes start with the wall down and the ball in ({g.S})");
            Check(!me.SlotAt(Cfg.HotbarSize - 1).Empty, $"the first free item came straight away, onto the right of the hotbar like wood ({me.SlotAt(Cfg.HotbarSize - 1).Id})");
            Check(Cfg.AirdropCount == 0 || g.NextDropLands.Value < 0, "no airdrops in the fun modes");
            Cfg.FunItemInterval = 2f;
            int Items()
            {
                int n = 0;
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (!me.SlotAt(i).Empty) n++;
                return n;
            }
            int before = Items();
            yield return new WaitForSeconds(Mathf.Max(0f, (float)(g.NextFunItem.Value - me.NetworkManager.ServerTime.Time)) + 2.6f); // the next one was already scheduled
            Check(Items() > before && g.NextFunItem.Value > 0, $"free items handed out ({before} -> {Items()} slots used)");
            int lanes = 0;
            for (int i = 0; i < NetGame.LaneTotal; i++) if (g.LaneStartAt(i) >= 0) lanes++;
            Check(lanes == 0 && g.NextDropLands.Value < 0, "still no airdrops");
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
            int inside = 0;
            foreach (var n in ResourceNode.All)
            {
                var p = n.transform.position;
                foreach (var h in Physics.OverlapCapsule(p + Vector3.up * 0.9f, p + Vector3.up * 4f, 1.2f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                    if (h.GetComponentInParent<GroundMarker>() == null && h.GetComponentInParent<Unity.Netcode.NetworkObject>() == null) { inside++; break; }
            }
            Check(inside == 0, $"no trees or rocks inside the map's scenery ({inside})");

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
                    yield return BenchBuy(me, pc, Item.Boat);
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

        // ------------------------------------------------------------------ AI PSX TEST graphics

        static int CountAiPsx()
        {
            int n = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials) if (m != null && m.name.StartsWith("aipsx")) { n++; break; }
            return n;
        }

        /// <summary>-autotest aipsx -aipsx: pictures of the AI PSX TEST look, and Normal / PSX are exactly as before when switched back.</summary>
        IEnumerator AiPsxShots(PlayerNet me, PlayerController pc)
        {
            int team = me.Team.Value;
            Check(GameSettings.AiPsx && !GameSettings.PsxGraphics, "AI PSX TEST mode is on");
            int skinned = CountAiPsx();
            Check(skinned > 50, $"{skinned} things re-skinned in the PSX style");
            var views = new[]
            {
                (new Vector3(Cfg.MapHalf * 0.55f, 45f, Cfg.BaseCenter[team].z - 10f), -30f, 32f),
                (Cfg.BaseCenter[team] * 0.55f + new Vector3(8f, 0, 0), 0f, 4f),
            };
            ResourceNode tree = Nearest(ResourceNode.Tree, me.transform.position), rock = Nearest(ResourceNode.Boulder, me.transform.position);
            IEnumerator Views(string mode)
            {
                for (int v = 0; v < views.Length; v++)
                {
                    var (pos, yaw, pitch) = views[v];
                    if (v == 1)
                    {
                        pos.y = MapBuilder.Height(pos.x, pos.z) + 0.2f;
                        yaw = Quaternion.LookRotation(-pos).eulerAngles.y;
                    }
                    pc.LocalTeleport(pos, yaw);
                    pc.SetLook(yaw, pitch);
                    yield return Snap($"aipsx_{mode}_{v + 1}");
                }
                foreach (var (n, name) in new[] { (tree, "tree"), (rock, "rock") })
                {
                    if (n == null) continue;
                    var np = n.transform.position;
                    var away = me.transform.position - np;
                    away.y = 0;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                    var eye = np + away * (name == "tree" ? 6f : 3.5f);
                    eye.y = MapBuilder.Height(eye.x, eye.z) + 0.1f;
                    float y2 = Quaternion.LookRotation(-away).eulerAngles.y;
                    pc.LocalTeleport(eye, y2);
                    pc.SetLook(y2, name == "tree" ? -12f : 12f);
                    yield return Snap($"aipsx_{mode}_{name}");
                }
                Vehicle horse = null;
                foreach (var h in Vehicle.All) if (h.IsHorse) { horse = h; break; }
                if (horse != null)
                {
                    var hp = horse.transform.position;
                    var eye = hp + new Vector3(3f, 0.2f, -3f);
                    eye.y = MapBuilder.Height(eye.x, eye.z) + 0.1f;
                    float y3 = Quaternion.LookRotation(hp - eye).eulerAngles.y;
                    pc.LocalTeleport(eye, y3);
                    pc.SetLook(y3, 8f);
                    yield return Snap($"aipsx_{mode}_horse");
                }
            }
            yield return Views("ai");
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);
            Check(CountAiPsx() == 0 && !GameSettings.AiPsx, "switching to Normal puts every original look back");
            yield return Views("normal");
            GameSettings.SetGraphics(1, false);
            yield return new WaitForSeconds(0.5f);
            Check(CountAiPsx() == 0 && GameSettings.PsxGraphics, "PSX is untouched by the AI PSX mode");
            yield return Views("psx");
            GameSettings.SetGraphics(2, false);
            yield return new WaitForSeconds(0.5f);
            Check(CountAiPsx() >= skinned - 5, "switching back to AI PSX TEST re-skins everything");
            Log("aipsx shots done");
            Application.Quit(0);
        }

        /// <summary>-autotest outline (host + client): the client turns the alien outlines on and photographs the host at a few strengths.</summary>
        IEnumerator OutlineShots(PlayerNet me, PlayerController pc)
        {
            if (Unity.Netcode.NetworkManager.Singleton.IsHost) { yield return new WaitForSeconds(30f); Application.Quit(0); yield break; }
            PlayerNet other = null;
            foreach (var p in PlayerNet.All) if (p != me) other = p;
            if (other == null) { Log("FAIL: nobody to look at"); Application.Quit(0); yield break; }
            yield return new WaitForSeconds(2f);
            var op = other.transform.position;
            var eye = op + new Vector3(0, 0, 3.5f * (op.z > 0 ? -1f : 1f));
            eye.y = MapBuilder.Height(eye.x, eye.z) + 0.1f;
            float yaw = Quaternion.LookRotation(op - eye).eulerAngles.y;
            foreach (var (on, st, w) in new[] { (false, 0.3f, 0.03f), (true, 0.3f, 0.03f), (true, 0.8f, 0.06f) })
            {
                Cfg.AlienOutlines = on;
                Cfg.AlienOutlineStrength = st;
                Cfg.AlienOutlineWidth = w;
                pc.LocalTeleport(eye, yaw);
                pc.SetLook(yaw, 8f);
                yield return new WaitForSeconds(1.2f);
                int glowing = 0;
                foreach (var r in other.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null && m.name == "alien outline") { glowing++; break; }
                Check(on ? glowing > 0 : glowing == 0, $"outlines {(on ? "on" : "off")}: {glowing} glowing parts");
                yield return Snap($"outline_{(on ? "on" : "off")}_{st * 100f:0}");
            }
            Log("outline shots done");
            Application.Quit(0);
        }
    }
}
