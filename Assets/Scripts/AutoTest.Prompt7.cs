using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt7 -host -solo [-rules jonahbruh | progress] [-map cherryblossom] -shotdir DIR: the spear thrown
        /// mid-jump lands on the crosshair, your hands in your alien's colour, the wood machine standing there (switched off)
        /// before the wall drops, the tree X on the bark, the Rock of Immortality and the inventory swapper gone, the airdrop
        /// pool (every newer item, no C4), the futuristic hammer, Progress's bars top right (-rules progress), and
        /// Jonah Test Mode Bruh's changes only in that mode (-rules jonahbruh).
        /// </summary>
        /// <summary>-autotest prompt7menu -shotdir DIR: the main menu's mode page - Jonah Test Mode Bruh in the list, and
        /// Progress's PROGRESS TIME row (+ / - change it).</summary>
        IEnumerator Prompt7MenuShots()
        {
            Hud.TestNewMenu = true;
            yield return new WaitForSeconds(2.5f);
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.JonahBruh);
            Hud.TestNewPage(6);
            Hud.TestModesOpen(true);
            yield return Snap("prompt7_menu_modes");
            Check(System.Array.IndexOf(Hud.MenuModes, GameRules.JonahBruh) >= 0, "Jonah Test Mode Bruh is on the menu");
            Hud.TestModesOpen(false);
            yield return Snap("prompt7_menu_jonahbruh");
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.Progress);
            yield return Snap("prompt7_menu_progress");
            Log("prompt7menu done");
            Application.Quit(0);
        }

        IEnumerator Prompt7Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);

            // ---- the lists
            Check(Cfg.Removed(Item.GoldenRock) && !Cfg.AirdropLoot.Contains(Item.GoldenRock) && !Cfg.AllFunItems.Contains(Item.GoldenRock), "the Rock of Immortality is out of the game");
            Check(Cfg.Removed(Item.InvSwapper) && !Cfg.AirdropLoot.Contains(Item.InvSwapper) && !Cfg.AllFunItems.Contains(Item.InvSwapper), "the inventory swapper is out of the game");
            {
                var loot = Cfg.AirdropLoot;
                var missing = new List<string>();
                foreach (var it in new[] { Item.C4, Item.Jetpack, Item.BouncyCastle, Item.DanceBomb }) if (!loot.Contains(it)) missing.Add(it.ToString());
                Check(missing.Count == 0 && (Cfg.AirdropTest || !loot.Contains(Item.AntGun)),
                    $"the airdrop pool by default: just the nine picked ones (AutoTest.Prompt8.cs) ({loot.Count} items{(missing.Count > 0 ? ", missing " + string.Join(" ", missing) : "")})");
            }
            Check(Mathf.Approximately(Cfg.ProgressSeconds, 400f), $"Progress takes {Cfg.ProgressSeconds:0} s to fill");
            {
                // the spear flies flatter: thrown at something 25 m off it goes barely over the line to it
                var v = PlayerController.SpearAimAt(Vector3.zero, new Vector3(0, 0, 25f), Cfg.SpearThrowSpeed, Vector3.forward);
                float lob = Vector3.Angle(v, Vector3.forward);
                Check(lob < 8f, $"the spear's throw is flat: {lob:0.0} degrees over the line at 25 m (it was {Vector3.Angle(PlayerController.SpearAimAt(Vector3.zero, new Vector3(0, 0, 25f), 30f, Vector3.forward), Vector3.forward):0.0})");
            }

            // ---- the hands: your own alien's colour (the team colour), not one dark blue
            {
                var tc = Cfg.TeamColor[team];
                int hooks = 0, right = 0;
                foreach (var hook in FindObjectsByType<HandColorHook>(FindObjectsSortMode.None))
                {
                    if (!hook.transform.IsChildOf(Camera.main.transform)) continue;
                    hooks++;
                    bool ok = hook.AllShaded(out _);
                    foreach (var r in hook.GetComponentsInChildren<Renderer>(true))
                        if (r.name == "skin" && !ColorSlots.Same(r.sharedMaterial.color, HandColorHook.AlienShade(tc, false))) ok = false;
                    if (ok) right++;
                }
                Check(hooks >= 2 && right == hooks, $"both hands are your alien's colour ({right} of {hooks}; {HandColorHook.LastMismatch})");
            }

            // ---- the wood machine: there before the wall drops, switched off, and it says so
            WoodMachine wm = null;
            foreach (var w in FindObjectsByType<WoodMachine>(FindObjectsSortMode.None)) if (w.Team == team) wm = w;
            if (Cfg.AutoWoodGen)
            {
                Check(g.S == GameState.PreBall && wm != null && wm.Shown && wm.Dormant && !wm.Built && wm.GetComponentInChildren<Collider>() != null,
                    $"the wood machine stands in the base before the wall drops, switched off ({g.S}, shown {wm != null && wm.Shown})");
                var wmPos = Cfg.WoodMachinePos(team);
                var from = wmPos - back * 3f;
                pc.LocalTeleport(OnGround(from, 0.1f), Quaternion.LookRotation(wmPos - from).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                var lk = Quaternion.LookRotation(wmPos + Vector3.up * (0.6f * (Cfg.SmallBedrock ? Cfg.SmallMachineScale : 1f)) - me.EyePos).eulerAngles; // (Jonah Test Mode Bruh: a half-size machine)
                pc.SetLook(lk.y, lk.x > 180f ? lk.x - 360f : lk.x);
                yield return new WaitForSeconds(0.4f);
                Check(pc.AimText.Contains("till the wall drops"), $"looking at it: it doesn't give wood till the wall drops ({pc.AimText})");
                yield return Snap("prompt7_wood_machine_off");
                int piles = 0; foreach (var it in g.Items) if (it.Stack.Id == Item.Wood && Vector3.Distance(it.Pos, Cfg.WoodTrayPos(team)) < 2f) piles++;
                Check(piles == 0, "no wood out of it yet");
            }

            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(4f);
            if (Cfg.AutoWoodGen) Check(wm != null && wm.Built && !wm.Dormant && g.WoodGenLevelOf(team) >= 1, "the wall dropped: the wood machine started up");

            // ---- the spear: thrown standing, mid-jump going up, at the top and coming down, it lands on the crosshair
            if (ClearLane(team, out var lane, out float laneYaw))
            {
                var results = new List<string>();
                float worst = 0f;
                foreach (var when in new[] { "standing", "jumping up", "at the top", "coming down", "running jump" })
                {
                    for (int i = 0; i < me.Inv.Count; i++) if (me.Inv[i].Id == Item.Spear) me.Inv[i] = default;
                    me.ServerGive(Item.Spear, 1);
                    yield return new WaitForSeconds(0.2f);
                    yield return Hold(me, Item.Spear);
                    pc.LocalTeleport(lane, laneYaw);
                    pc.SetLook(laneYaw, 9f);
                    yield return new WaitForSeconds(0.8f);
                    Binds.TestHold(Bind.Aim, true);
                    yield return new WaitForSeconds(0.3f);
                    if (when == "running jump") { Binds.TestHold(Bind.Forward, true); Binds.TestHold(Bind.Sprint, true); yield return new WaitForSeconds(0.5f); }
                    if (when != "standing")
                    {
                        Binds.TestPress(Bind.Jump);
                        yield return new WaitForSeconds(when == "jumping up" || when == "running jump" ? 0.12f : when == "at the top" ? 0.33f : 0.55f);
                    }
                    int hits0 = ArrowProjectile.SpearHits;
                    Binds.TestPress(Bind.Attack);
                    for (float t = 0; t < 4f && ArrowProjectile.SpearHits == hits0; t += 0.05f) yield return new WaitForSeconds(0.05f);
                    Binds.TestReleaseAll();
                    float miss = ArrowProjectile.SpearHits > hits0 ? Vector3.Distance(ArrowProjectile.LastSpearHit, PlayerController.LastSpearTarget) : 99f;
                    float dist = Vector3.Distance(PlayerController.LastSpearTarget, me.EyePos);
                    worst = Mathf.Max(worst, miss);
                    results.Add($"{when}: {miss:0.00} m off at {dist:0} m");
                    yield return new WaitForSeconds(0.6f);
                }
                Check(worst < 0.6f, $"the spear lands where the crosshair was, jumping or not ({string.Join(", ", results)})");
            }
            else Log("FAIL: no clear lane for the spear");

            // ---- the tree X: close up on a few trees, from the front and the side
            {
                var trees = new List<ResourceNode>();
                foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)) if (n.Kind.Value == ResourceNode.Tree && n.IsSpawned) trees.Add(n);
                var home = Cfg.SpawnPos(team);
                trees.Sort((a, b) => Vector3.Distance(a.transform.position, home).CompareTo(Vector3.Distance(b.transform.position, home)));
                int shots = 0;
                for (int k = 0; k < trees.Count && shots < 3; k++)
                {
                    var tree = trees[k];
                    if (Cfg.BaseTeamAt(tree.transform.position) >= 0) continue;
                    var dir = home - tree.transform.position; dir.y = 0; dir.Normalize();
                    // the spot facing us
                    int best = 0; float bd = -2f;
                    for (int s = 0; s < 12; s++)
                    {
                        float a = s * 30f * Mathf.Deg2Rad;
                        var sd = tree.transform.rotation * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        if (Vector3.Dot(sd, dir) > bd) { bd = Vector3.Dot(sd, dir); best = s; }
                    }
                    tree.Spot.Value = (byte)best;
                    yield return new WaitForSeconds(0.3f);
                    if (!tree.TryGetSpot(out var xp, out var xn)) { Log($"tree {k}: no X shown"); continue; }
                    foreach (var view in new[] { 0f, 50f })
                    {
                        var vdir = Quaternion.Euler(0, view, 0) * new Vector3(xn.x, 0, xn.z).normalized;
                        var eye = xp + vdir * 2.4f;
                        pc.LocalTeleport(OnGround(eye, 0.1f), Quaternion.LookRotation(-vdir).eulerAngles.y);
                        yield return new WaitForSeconds(0.3f);
                        var lk = Quaternion.LookRotation(xp - me.EyePos).eulerAngles;
                        pc.SetLook(lk.y, lk.x > 180f ? lk.x - 360f : lk.x);
                        yield return Snap($"prompt7_{Cfg.Map}_tree{shots}_x{(view == 0f ? "_front" : "_side")}");
                    }
                    Log($"tree {shots} X: {tree.XDebug()}");
                    shots++;
                }
                Check(shots > 0, $"close-ups of the X on {shots} trees");
            }

            // ---- the hammer: the same mallet in a futuristic glowing skin
            {
                me.ServerGive(Item.Hammer, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.Hammer);
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                pc.SetLook(Cfg.SpawnYaw(team), 10f);
                yield return new WaitForSeconds(0.5f);
                var vm = Camera.main.GetComponentInChildren<HammerGlow>();
                Check(vm != null, "the hammer in your hands glows (the futuristic skin)");
                yield return Snap("prompt7_hammer");
                var icon = new GameObject("hammer probe");
                ItemModels.Create(Item.Hammer, icon.transform);
                var b = new Bounds(); bool any = false;
                foreach (var r in icon.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                Destroy(icon);
                Check(any && b.size.y > 0.7f && b.size.y < 0.8f && b.size.z > 0.29f && b.size.z < 0.33f, $"...the same shape as before ({b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00})");
            }

            // ---- Progress: the bars are small, top right
            if (Cfg.ProgressMode)
            {
                g.ProgressBars.Value = new Vector4(0.37f, 0.64f, 0f, 0f);
                yield return new WaitForSeconds(0.5f);
                Check(Time.frameCount - Hud.ProgressCornerShown < 5, "Progress: the bars are in the top right");
                yield return Snap("prompt7_progress_top_right");
            }

            // ---- Jonah Test Mode Bruh (and only there)
            if (Cfg.JonahBruh)
            {
                Check(Cfg.RulesName(Cfg.Rules) == "Jonah Test Mode Bruh", $"the mode is called {Cfg.RulesName(Cfg.Rules)}");
                Check(Cfg.RecipeIndex(Item.Ladder) < 0 && Cfg.RecipeIndex(Item.BearTrap) < 0 && Cfg.RecipeIndex(Item.DeathWand) < 0 && Cfg.RecipeIndex(Item.Chainsaw) < 0,
                    "no ladder, stun trap, death wand or chainsaw to craft");
                Check(!Cfg.AllFunItems.Contains(Item.Ladder) && !Cfg.AllFunItems.Contains(Item.Chainsaw) && !Cfg.AllFunItems.Contains(Item.BearTrap), "...nor anywhere else (the death wand: in its airdrops - AutoTest.Prompt8.cs)");
                int bh = Cfg.RecipeIndex(Item.BetterHatchet);
                Check(bh >= 0 && Cfg.GetRecipe(bh).Wood == 50 && Cfg.CraftTier(Item.BetterHatchet) == 1, $"the Better Hatchet in the chainsaw's place ({(bh >= 0 ? Cfg.GetRecipe(bh).Wood : -1)} wood, T{Cfg.CraftTier(Item.BetterHatchet)})");
                int ai = Cfg.PowerIndex(Item.HeavyArmor);
                Check(ai >= 0 && Cfg.GetPowerRecipe(ai).Wood == 150, $"armour costs {(ai >= 0 ? Cfg.GetPowerRecipe(ai).Wood : -1)}");
                Check(Cfg.HammerCost(0) == 150 && Cfg.HammerCost(1) == 0 && Mathf.Approximately(Cfg.PieceHp(PieceType.Wall, 1), Cfg.WallStoneHp * Cfg.JonahBruhStoneHpMul) && Cfg.JonahBruhStoneHpMul > 1f,
                    $"the hammer: stone for {Cfg.HammerCost(0)} and no further; stone walls {Cfg.PieceHp(PieceType.Wall, 1):0} HP (was {Cfg.WallStoneHp:0})");

                // the better hatchet against the hatchet, on a tree each
                float WoodPerSecond(Item axe) => Cfg.Melee(axe).WoodGather / Mathf.Max(0.05f, Cfg.Melee(axe).Cooldown);
                Check(WoodPerSecond(Item.BetterHatchet) > WoodPerSecond(Item.Hatchet) * 1.5f, $"it chops faster ({WoodPerSecond(Item.BetterHatchet):0} vs {WoodPerSecond(Item.Hatchet):0} wood a second)");
                foreach (var axe in new[] { Item.Hatchet, Item.BetterHatchet })
                {
                    for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                    me.ServerGive(axe, 1);
                    yield return new WaitForSeconds(0.2f);
                    yield return Hold(me, axe);
                    var tree = Nearest(ResourceNode.Tree, me.transform.position);
                    var dir = me.transform.position - tree.transform.position; dir.y = 0; dir.Normalize();
                    pc.LocalTeleport(tree.transform.position + dir * 2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-dir).eulerAngles.y);
                    yield return new WaitForSeconds(1.2f);
                    me.MeleeRpc(true, tree.NetworkObject, tree.transform.position + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(0.4f);
                    int got = me.Count(Cfg.GatherItem(Item.Wood)); // (the bag was emptied first)
                    Log($"{axe}: {got} wood from one hit");
                    if (axe == Item.BetterHatchet)
                    {
                        Check(got > 15, $"the better hatchet gets more wood a hit ({got})");
                        yield return Snap("prompt7_better_hatchet");
                    }
                }

                // the hammer: up to stone for 150, then no further
                me.ServerGive(Item.Hammer, 1);
                me.ServerGive(Cfg.GatherItem(Item.Wood), 1000);
                yield return Hold(me, Item.Hammer);
                var wallAt = Cfg.BaseCenter[team] - back * 9f - side * 9f;
                wallAt.y = MapBuilder.Height(wallAt.x, wallAt.z);
                float wallYaw = Quaternion.LookRotation(back).eulerAngles.y;
                var wall = SpawnKeylessPiece(PieceType.Wall, team, wallAt, Quaternion.Euler(0, wallYaw, 0));
                yield return new WaitForSeconds(0.4f);
                float lookYaw = Quaternion.LookRotation(-back).eulerAngles.y;
                pc.LocalTeleport(wallAt + back * 3.2f, lookYaw);
                pc.SetLook(lookYaw, 4f);
                yield return new WaitForSeconds(0.5f);
                Check(pc.AimText.Contains("UPGRADE TO STONE") && pc.AimText.Contains("150"), $"aiming the hammer: upgrade to stone for 150 ({pc.AimText})");
                int w0 = me.Count(Cfg.GatherItem(Item.Wood));
                me.HammerUpgradeRpc(wall.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(wall.Tier.Value == 1 && w0 - me.Count(Cfg.GatherItem(Item.Wood)) == 150 && Mathf.Approximately(wall.MaxHp, Cfg.WallStoneHp * Cfg.JonahBruhStoneHpMul),
                    $"stone for 150 wood, with {wall.MaxHp:0} HP");
                me.HammerUpgradeRpc(wall.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(wall.Tier.Value == 1 && pc.HammerTarget() == null && pc.AimText.Contains("as far as the hammer goes"), $"...and no further ({pc.AimText})");
                yield return Snap("prompt7_stone_only");
            }
            else
            {
                Check(Cfg.RecipeIndex(Item.Chainsaw) >= 0 && Cfg.RecipeIndex(Item.Ladder) >= 0 && Cfg.RecipeIndex(Item.BetterHatchet) < 0 && Cfg.HammerCost(0) == Cfg.HammerStoneWood && Cfg.HammerCost(1) > 0
                    && Mathf.Approximately(Cfg.PieceHp(PieceType.Wall, 1), Cfg.WallStoneHp) && Cfg.GetPowerRecipe(Cfg.PowerIndex(Item.HeavyArmor)).Wood == Cfg.HeavyArmorWood,
                    "the Jonah Test Mode Bruh changes stay in that mode (chainsaw, ladder, metal walls, armour price as before here)");
            }

            Log("prompt7 done");
            Application.Quit(0);
        }
    }
}
