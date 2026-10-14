using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt2 -host -solo -fast -rules autowood -shotdir DIR (Classic): no wood machine while the glass walls
        /// are up, one in every base once they drop; the hammer a Trade Station item (all wood); a wall needs a foundation
        /// (nothing suggested); a piece that's just been hit can't be hammered up for 5 s; the barricade (T1) - crouch-high
        /// cover, down in the enemy base but not by their buildings; the wooden armour gone and Armour (+50) for 500; no
        /// large wall / gate; ammo 100; 5 airdrops; the bow and crossbow weaker; level 3 of the wood gen faster; the death
        /// wand crafted (last on the T2 list, 5000) and out of the airdrops; the intro can't be skipped; the jetpack and the
        /// wallhack glasses put on by themselves, shown bottom left and kept through death; invisible: your hands go, what
        /// you hold stays; the kill feed names the weapon that did it (an arrow after a switch: the bow); the kill cam's
        /// replay shows arrows and spears; the airdrop banner over an open bag; the floors' faces apart (no flicker).
        /// </summary>
        IEnumerator Prompt2Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);

            // ---- the wood machine: not working while the walls are up (it stands there switched off: AutoTest.Prompt7.cs)
            WoodMachine wm = null;
            foreach (var w in FindObjectsByType<WoodMachine>(FindObjectsSortMode.None)) if (w.Team == team) wm = w;
            if (g.S == GameState.PreBall)
                Check(wm != null && !wm.Built && g.WoodGenLevelOf(team) == 0, $"no working wood machine while the glass walls are up (level {g.WoodGenLevelOf(team)})");

            // ---- the numbers and the lists
            Check(Cfg.CraftIndexOf(Item.Hammer) < 0 && Cfg.PlanUpgradeOn, "no hammer: UPGRADE is on the building plan");
            Check(Cfg.CraftIndexOf(Item.Armor) < 0 && Cfg.ItemName(Item.HeavyArmor) == "Armour" && Cfg.HeavyArmorWood == 500 && Cfg.HeavyArmorHp == 50,
                $"no wooden armour; the heavy armour is just Armour: +{Cfg.HeavyArmorHp} for {Cfg.HeavyArmorWood}");
            Check(Cfg.CraftIndexOf(Item.Barrier) < 0 && Cfg.CraftIndexOf(Item.LargeGate) < 0, "no large wall or large gate to craft");
            Check(Cfg.CraftTier(Item.Barricade) == 1 && Cfg.CraftIndexOf(Item.Barricade) >= 0, "the barricade is a Trade Station (T1) item");
            Check(Cfg.ShellWood == 100 && Cfg.RevolverAmmoWood == 500 && Cfg.RevolverAmmoPerCraft == 6, $"shotgun shells 100 each, revolver bullets 500 for 6 ({Cfg.ShellWood}, {Cfg.RevolverAmmoWood})");
            Check(Cfg.AirdropCount == 5, $"5 airdrops a match by default ({Cfg.AirdropCount})");
            Check(Cfg.ArrowPlayerDamage < 50f && Cfg.CrossbowDamage < 55f, $"the bow and crossbow hit softer ({Cfg.ArrowPlayerDamage}, {Cfg.CrossbowDamage})");
            Check(Cfg.WoodGenRate(3) > 25 && Cfg.WoodGenRate(3) > Cfg.WoodGenRate(2), $"the wood gen's third level makes more wood ({Cfg.WoodGenRate(3)} a second, was 25)");
            {
                var all = new List<int>();
                Cfg.CraftList(all, 2);
                var last = all.Count > 0 ? Cfg.CraftRecipe(all[all.Count - 1], team) : default;
                Check(last.Output == Item.DeathWand && last.Wood == 4000 && Cfg.CraftTier(Item.DeathWand) == 2, $"the death wand is the last thing on the Advanced Trade Station's list, for 4000 ({last.Output} {last.Wood})");
                bool wandDrop = Cfg.AirdropLoot.Contains(Item.DeathWand);
                int mask = Cfg.AirdropItemMask, mask2 = Cfg.AirdropItemMask2;
                Cfg.PickAllAirdrops(true);
                for (int i = 0; i < 300 && !wandDrop; i++) wandDrop |= NetGame.RollAirdropCrate()[0].Id == Item.DeathWand || NetGame.RollAirdropLoot().Id == Item.DeathWand;
                Cfg.AirdropItemMask = mask; Cfg.AirdropItemMask2 = mask2;
                Check(!wandDrop, "no death wand in the airdrops any more");
            }
            Check(!MatchIntro.CanSkip, "the match intro can't be skipped");
            Check(!PlayerController.AutoFoundationOn, "a wall on bare ground doesn't put its own foundation down any more");

            // ---- the walls drop: the wood machine lands in every base
            for (float t = 0; t < 30f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(1.5f);
            Check(g.S == GameState.BallLive && wm != null && wm.Built && g.WoodGenLevelOf(team) == 1 && g.WoodGenLevelOf(enemy) == 1, $"the walls dropped: a wood machine landed in every base (level {g.WoodGenLevelOf(team)})");

            // ---- building: a wall needs a foundation under it
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            me.ServerGive(Item.Wood, 1000);
            me.ServerGive(Item.BuildingPlan, 1);
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 0, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.4f);
            int walls0 = CountStructures(PieceType.Wall, team), found0 = CountStructures(PieceType.Foundation, team);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(0.8f);
            me.PlaceOnNewFoundationRpc((byte)PieceType.Wall, ci, cj, 1, ci, cj);
            yield return new WaitForSeconds(0.8f);
            Check(CountStructures(PieceType.Wall, team) == walls0 && CountStructures(PieceType.Foundation, team) == found0, "a wall on bare ground is refused - and no foundation goes down for it");
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(1.2f);
            Check(CountStructures(PieceType.Wall, team) == walls0 + 1, "on a foundation it goes up");
            // (a ceiling over it: the floor's top sits a hair over the wall's top, its beams a hair under it - no faces on top of each other)
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 0);
            yield return new WaitForSeconds(1.2f);
            me.PlaceRpc((byte)PieceType.Floor, ci, cj, 1, 0);
            yield return new WaitForSeconds(1.2f);
            {
                var probe = new GameObject("floor probe");
                Structure.CreateVisual(PieceType.Floor, 0, probe.transform, false, null, out _);
                float top = float.MinValue;
                var bottoms = new List<float>();
                foreach (var r in probe.GetComponentsInChildren<MeshRenderer>()) { top = Mathf.Max(top, r.bounds.max.y); bottoms.Add(r.bounds.min.y); }
                bool apart = true;
                for (int a = 0; a < bottoms.Count; a++) for (int b = a + 1; b < bottoms.Count; b++) apart &= Mathf.Abs(bottoms[a] - bottoms[b]) > 0.002f;
                Destroy(probe);
                Check(top > 0.005f && apart, $"a floor's faces don't lie on the walls' tops or on each other (top {top:0.000}, bottoms {string.Join(" ", bottoms.ConvertAll(x => x.ToString("0.000")))})");
            }
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(0.6f, 0.1f, 0.6f), 225f);
            pc.SetLook(225f, 55f);
            yield return Snap("prompt2_ceiling_from_below");

            // ---- the hammer (T1 now, all wood): a piece that's just been hit can't go up for 5 s
            if (Workbench.ForTeam(team, 1) == null) Workbench.ServerSpawn(team, Workbench.DefaultPos(team), Workbench.DefaultYaw(team));
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.5f);
            if (me.Count(Item.BuildingPlan) == 0) me.ServerGive(Item.BuildingPlan, 1);
            yield return Hold(me, Item.BuildingPlan);
            pc.SelectWheel(0); // (UPGRADE: it took the hammer's place)
            var wallAt = Cfg.BaseCenter[team] - back * 9f - side * 9f; // (clear of the walls built above)
            wallAt.y = MapBuilder.Height(wallAt.x, wallAt.z);
            float wallYaw = Quaternion.LookRotation(back).eulerAngles.y;
            var wall = SpawnKeylessPiece(PieceType.Wall, team, wallAt, Quaternion.Euler(0, wallYaw, 0));
            yield return new WaitForSeconds(0.4f);
            float lookYaw = Quaternion.LookRotation(-back).eulerAngles.y;
            pc.LocalTeleport(wallAt + back * 3.2f, lookYaw);
            pc.SetLook(lookYaw, 4f);
            yield return Snap("prompt2_wooden_hammer");
            wall.ServerDamage(5f);
            yield return new WaitForSeconds(0.4f);
            me.PlanUpgradeRpc(wall.NetworkObject);
            yield return new WaitForSeconds(0.4f);
            Check(wall.Tier.Value == 0 && wall.UpgradeLockLeft > 3f, $"just hit: the hammer can't take it up ({wall.UpgradeLockLeft:0.0} s left)");
            Check(pc.HammerTarget() == wall && pc.AimText.Contains("UNDER ATTACK"), $"...and aiming at it says so ({pc.AimText})");
            yield return Snap("prompt2_hammer_under_attack");
            yield return new WaitForSeconds(Cfg.HammerHitLock);
            me.ServerGive(Cfg.CurrencyItem, Cfg.HammerCost(0));
            me.PlanUpgradeRpc(wall.NetworkObject);
            yield return new WaitForSeconds(0.5f);
            Check(wall.Tier.Value == 1, "5 seconds later it goes up to stone");
            pc.SelectWheel(System.Array.FindIndex(PlayerController.WheelOptions, x => x.Piece == PieceType.Foundation && !x.Demolish && !x.Upgrade));

            // ---- the barricade: crouch-high cover; in the enemy base too, but not right by their buildings
            {
                me.ServerGive(Item.Barricade, 3);
                yield return Hold(me, Item.Barricade);
                Vector3 Spot(int t, float r0)
                {
                    var c = Cfg.BaseCenter[t];
                    var bk = Cfg.BackDir(t);
                    var sd = Vector3.Cross(Vector3.up, bk);
                    for (int r = 0; r < 12; r++)
                        for (int a = -4; a <= 4; a++)
                        {
                            var p = c - bk * (r0 + r * 1.2f) + sd * a * 1.6f;
                            p.y = MapBuilder.Height(p.x, p.z);
                            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var h, 12f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) p.y = h.point.y;
                            if (PlayerNet.DeployProblem(Item.Barricade, team, p, Quaternion.LookRotation(bk).eulerAngles.y) == null) return p;
                        }
                    return Vector3.zero;
                }
                var mine = Spot(team, 8f);
                Check(mine != Vector3.zero, "a spot in our base for a barricade");
                float bYaw = Quaternion.LookRotation(-back).eulerAngles.y;
                pc.LocalTeleport(mine + back * 2.5f + Vector3.up * 0.1f, bYaw);
                yield return new WaitForSeconds(0.4f);
                me.PlaceDeployableRpc((byte)Item.Barricade, mine, bYaw);
                yield return new WaitForSeconds(0.6f);
                Container ours = null;
                foreach (var c in Container.All) if (c != null && c.Kind.Value == Container.Barricade && c.Team.Value == team) ours = c;
                Check(ours != null && me.Count(Item.Barricade) == 2, "a barricade goes down in our base");
                if (ours != null)
                {
                    var b = new Bounds(ours.transform.position, Vector3.zero);
                    foreach (var col in ours.GetComponentsInChildren<Collider>()) if (!col.isTrigger) b.Encapsulate(col.bounds);
                    float h = b.max.y - ours.transform.position.y, wd = Mathf.Max(b.size.x, b.size.z);
                    Check(h > Cfg.CrouchHeight + 0.05f && h < Cfg.EyeHeight + 0.4f && wd > 1.5f, $"it's tall enough to hide someone crouching (30% bigger since b1e14f9) ({h:0.00} m tall, {wd:0.0} m wide)");
                    // crouched behind it
                    pc.LocalTeleport(ours.transform.position + ours.transform.forward * 0.9f + Vector3.up * 0.1f, Quaternion.LookRotation(-ours.transform.forward).eulerAngles.y);
                    Binds.TestHold(Bind.Crouch, true);
                    yield return new WaitForSeconds(0.6f);
                    yield return Snap("prompt2_barricade_crouched");
                    Binds.TestReleaseAll();
                    var view = ours.transform.position + ours.transform.forward * -5f + ours.transform.right * 2f;
                    view.y = MapBuilder.Height(view.x, view.z) + 0.1f;
                    pc.LocalTeleport(view, Quaternion.LookRotation(ours.transform.position - view).eulerAngles.y);
                    pc.SetLook(Quaternion.LookRotation(ours.transform.position - view).eulerAngles.y, 8f);
                    yield return Snap("prompt2_barricade");
                }
                // the enemy base: fine out in the open there, not up against their buildings or their machine
                var theirs = Spot(enemy, 10f);
                Check(theirs != Vector3.zero && Cfg.BaseTeamAt(theirs) == enemy, $"a barricade can go in the enemy base ({theirs})");
                var ebk = Cfg.BackDir(enemy);
                var ew = Cfg.BaseCenter[enemy] - ebk * 12f + Vector3.Cross(Vector3.up, ebk) * 7f;
                ew.y = MapBuilder.Height(ew.x, ew.z);
                SpawnKeylessPiece(PieceType.Wall, enemy, ew, Quaternion.LookRotation(ebk));
                yield return new WaitForSeconds(0.4f);
                string near = PlayerNet.DeployProblem(Item.Barricade, team, ew - ebk * 1.6f, 0f);
                string machine = PlayerNet.DeployProblem(Item.Barricade, team, Cfg.MachinePos(enemy) - ebk * 2.5f, 0f);
                Check(near == "Too close to the enemy's buildings", $"...not right up against their buildings ({near})");
                Check(machine != null && machine.StartsWith("Too close") || machine == "Not on the bedrock", $"...or their machine ({machine})");
                if (theirs != Vector3.zero)
                {
                    float eYaw = Quaternion.LookRotation(ebk).eulerAngles.y;
                    pc.LocalTeleport(theirs - ebk * 2.5f + Vector3.up * 0.1f, eYaw);
                    yield return new WaitForSeconds(0.5f);
                    me.PlaceDeployableRpc((byte)Item.Barricade, theirs, eYaw);
                    yield return new WaitForSeconds(0.6f);
                    bool placed = false;
                    foreach (var c in Container.All) if (c != null && c.Kind.Value == Container.Barricade && c.Team.Value == team && Cfg.BaseTeamAt(c.transform.position) == enemy) placed = true;
                    Check(placed, "and one went down in the enemy base");
                }
            }

            // ---- the jetpack and the wallhack glasses: put on when you click with them (nothing goes on by itself since
            // 95fa0b8), bottom left, kept through death
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            me.ServerGive(Item.Jetpack, 1, Cfg.JetpackFuel);
            me.ServerGive(Item.Wallhack, 1);
            yield return new WaitForSeconds(0.8f);
            Check(!me.WearingJetpack && me.Count(Item.Jetpack) == 1 && !me.WornGlasses.Value, "the jetpack and glasses wait in the bag");
            foreach (var gear in new[] { Item.Jetpack, Item.Wallhack })
            {
                yield return Hold(me, gear);
                me.UseItemRpc();
                yield return new WaitForSeconds(0.7f);
            }
            Check(me.WearingJetpack && me.FuelPercent == 100 && me.Count(Item.Jetpack) == 0, $"the jetpack goes on with a click ({me.FuelPercent}% fuel)");
            Check(me.WornGlasses.Value && me.SeesThroughWalls && me.Count(Item.Wallhack) == 0, "the wallhack glasses go on with a click");
            Check(Time.time - Hud.JetpackRowShownAt < 0.5f && Time.time - Hud.GlassesRowShownAt < 0.5f, "both show bottom left, like the armour");
            yield return Snap("prompt2_gear_hud");
            {
                float y0 = me.transform.position.y, peak = y0;
                Binds.TestHold(Bind.Jump, true);
                for (float until = Time.time + 1.4f; Time.time < until; ) { peak = Mathf.Max(peak, me.transform.position.y); yield return null; }
                Binds.TestReleaseAll();
                yield return new WaitForSeconds(0.6f);
                Check(peak - y0 > 2.5f && me.FuelPercent < 100, $"holding jump flies it up ({peak - y0:0.0} m, {me.FuelPercent}% fuel left)");
                yield return new WaitForSeconds(1.5f);
            }
            // ---- invisible: your hands go, what you hold stays
            {
                me.ServerGive(Item.InvisPotion, 1);
                yield return Hold(me, Item.InvisPotion);
                me.UseItemRpc();
                yield return new WaitForSeconds(0.5f);
                if (me.Count(Item.Spear) == 0) me.ServerGive(Item.Spear, 1);
                yield return Hold(me, Item.Spear); // (the hammer's gone)
                yield return new WaitForSeconds(0.4f);
                var vm = pc.DebugViewModel;
                var hooks = vm != null ? vm.Root.GetComponentsInChildren<HandColorHook>() : new HandColorHook[0];
                bool hidden = hooks.Length > 0;
                foreach (var hk in hooks) hidden &= hk.Ghost && hk.HandsHidden;
                int shown = 0;
                if (vm != null) foreach (var r in vm.Root.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) shown++;
                Check(me.Invisible && hidden && shown > 0, $"invisible: our own hands are gone, the spear in them still shows ({shown} parts)");
                yield return Snap("prompt2_invisible_hands");
                me.InvisUntil.Value = -1;
                yield return new WaitForSeconds(0.4f);
                hidden = false;
                foreach (var hk in hooks) hidden |= hk.HandsHidden;
                Check(!hidden, "...and they're back when it wears off");
            }

            // ---- the kill feed: the weapon that did it (an arrow shot from the bow, then a switch to the hatchet)
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.8f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            Check(bot != null, "an enemy bot to shoot");
            if (bot != null)
            {
                foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
                if (bot.Dead.Value) bot.ServerRespawn(false);
                yield return new WaitForSeconds(0.3f);
                var at = me.transform.position + me.transform.forward * 4f;
                at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
                bot.TeleportRpc(at, Quaternion.LookRotation(me.transform.position - at).eulerAngles.y);
                yield return new WaitForSeconds(0.5f);
                bot.Health.Value = 1f;
                me.ServerGive(Item.Bow, 1);
                me.ServerGive(Item.Arrow, 10);
                me.ServerGive(Item.Hatchet, 1);
                yield return Hold(me, Item.Bow);
                var dir = (bot.transform.position + Vector3.up * 1.1f - me.EyePos).normalized;
                me.FireArrowRpc(me.EyePos + dir * 0.6f, dir * Cfg.ArrowSpeed);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.Hatchet);
                int lines0 = Hud.KillLines;
                me.ArrowHitRpc(bot.NetworkObject, bot.transform.position + Vector3.up * 1.1f, dir);
                yield return new WaitForSeconds(0.6f);
                Check(bot.Dead.Value && Hud.KillLines > lines0 && Hud.LastKillLine.Contains("[Bow"), $"the kill feed shows the bow that fired it, not the hatchet now in hand ({Hud.LastKillLine})");

                // ---- the kill cam's replay: the arrows and the spear flying at us are in it
                float respawnWas = Cfg.RespawnTime;
                for (float t = 0; t < 12f && bot.Dead.Value; t += 0.2f) yield return new WaitForSeconds(0.2f);
                if (bot.Dead.Value) { bot.ServerRespawn(false); yield return new WaitForSeconds(0.3f); }
                bot.TeleportRpc(at, Quaternion.LookRotation(me.transform.position - at).eulerAngles.y);
                yield return new WaitForSeconds(1.2f);
                for (int i = 0; i < 4; i++)
                {
                    var from = bot.EyePos + Vector3.up * 0.1f;
                    var to = me.transform.position + Vector3.up * (0.6f + i * 0.25f) + me.transform.right * (i - 1.5f) * 0.4f;
                    if (i == 2) ArrowProjectile.SpawnSpear(from, (to - from).normalized * Cfg.SpearThrowSpeed, bot, false);
                    else ArrowProjectile.Spawn(from, (to - from).normalized * Cfg.ArrowSpeed, bot, false);
                    yield return new WaitForSeconds(0.25f);
                }
                yield return new WaitForSeconds(0.15f);
                me.ServerKill(bot, (byte)Item.Bow);
                float until = Time.time + Cfg.KillCamTime + 4f;
                while (!pc.KillCamReplay && Time.time < until) yield return null;
                bool shotSnap = false;
                until = Time.time + DeathReplay.Duration + 2f;
                // (the banner over the death screen: always on top)
                Hud.Banner("AIRDROP INCOMING", "On the test side - look for the purple beam");
                int bannerFrame = Hud.BannerShownFrame;
                while (pc.KillCamReplay && Time.time < until)
                {
                    if (!shotSnap && DeathReplay.ShotsShown > 0) { shotSnap = true; yield return Snap("prompt2_killcam_arrows"); }
                    yield return null;
                }
                Check(DeathReplay.MostShotsShown >= 1, $"the kill cam's replay shows the arrows / spear flying (up to {DeathReplay.MostShotsShown} at once)");
                Check(Hud.BannerShownFrame > bannerFrame, "the airdrop banner shows while we're dead too");
                // dead: the jetpack and the glasses stay on, nothing of them dropped
                bool dropped = false;
                foreach (var it in g.Items) if (it.Stack.Id == Item.Jetpack || it.Stack.Id == Item.Wallhack) dropped = true;
                Check(me.Dead.Value && me.WearingJetpack && me.WornGlasses.Value && !dropped, "dead: the jetpack and the glasses stay on - neither dropped");
                until = Time.time + Cfg.RespawnTime + 10f;
                while (me.Dead.Value && Time.time < until)
                {
                    if (me.ChoosingRespawn) me.RespawnChoiceRpc(false);
                    yield return new WaitForSeconds(0.3f);
                }
                Check(!me.Dead.Value && me.WearingJetpack && me.WornGlasses.Value, "respawned still wearing them");
                Cfg.RespawnTime = respawnWas;
            }

            // ---- the airdrop banner over an open bag
            pc.MenuOpen = true;
            yield return new WaitForSeconds(0.3f);
            Hud.Banner("AIRDROP INCOMING", "On the test side - look for the purple beam");
            yield return new WaitForSeconds(0.5f);
            Check(Time.frameCount - Hud.BannerShownFrame < 5, "the airdrop banner shows over the open bag");
            yield return Snap("prompt2_banner_over_bag");
            pc.CloseMenu();
            Log("prompt2 done");
            Application.Quit(0);
        }

        /// <summary>
        /// -autotest arcade -host (the ship lobby, alone): the arcade machine where the right-hand trade station stood; the
        /// telly's game hides the team buttons, restarts (scores to 0) when someone new joins, and whoever plays it holds the
        /// controller low; UFO DODGE on the arcade machine - the camera moves in, Space flies, the saucer passes gaps for
        /// points, crashes, the game over screen with your best and the leaderboard, again with Space; STOP PLAYING goes back.
        /// </summary>
        IEnumerator ArcadeRoutine(PlayerNet me)
        {
            yield return new WaitForSeconds(1.5f);
            Check(ShipLobby.Active, "the ship lobby is up");
            Check(Time.time - Hud.LobbyTeamsShownAt < 0.5f, "the team buttons show on the couch");
            Check(ShipLobby.CamToCabinet < 50f, "the arcade machine stands in the room");
            yield return Snap("arcade_room");

            // ---- the telly's game: no team buttons over it; a newcomer restarts it
            ShipLobby.TestClickTv = true;
            yield return new WaitForSeconds(2f);
            Check(LobbyArcade.Focused && me.ArcadePlaying.Value, "clicked the telly: playing its game");
            Check(Time.time - Hud.LobbyTeamsShownAt > 1f, "the team buttons are hidden while playing it");
            yield return Snap("arcade_telly_no_teams");
            LobbyArcade.TestAuto = 1;
            for (float t = 0; t < 10f && LobbyArcade.Score(me.Team.Value) < 3; t += 0.25f) yield return new WaitForSeconds(0.25f);
            LobbyArcade.TestAuto = 0;
            int scored = LobbyArcade.Score(me.Team.Value), resets = LobbyArcade.ServerResets;
            Check(scored > 0, $"points on the telly ({scored})");
            PlayerNet.ServerAddBot();
            yield return new WaitForSeconds(1f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            Check(bot != null, "a bot joined the lobby");
            if (bot != null)
            {
                bot.ArcadePlaying.Value = true;
                yield return new WaitForSeconds(1.2f);
                Check(LobbyArcade.ServerResets == resets + 1 && LobbyArcade.Score(0) == 0 && LobbyArcade.Score(1) == 0 && LobbyArcade.PlayerScore(me.OwnerClientId) == 0 && LobbyArcade.Playing == 2,
                    $"someone new joined the telly's game: everything restarted, the scores back to 0 ({LobbyArcade.Score(0)}, {LobbyArcade.Score(1)}, {LobbyArcade.Playing} playing)");
                yield return Snap("arcade_telly_new_player");
            }
            LobbyArcade.Focus(false);
            yield return new WaitForSeconds(1.5f);
            // whoever plays it holds the controller low, in the lap
            if (bot != null)
            {
                int seat = -1;
                for (int i = 0; i < ShipLobby.Seated.Count; i++) if (ShipLobby.Seated[i] == bot) seat = i;
                ShipLobby.TestFocus = seat;
                yield return new WaitForSeconds(1.5f);
                Check(ShipLobby.IsGaming(bot), "the bot on the telly holds a controller");
                yield return Snap("arcade_controller_low");
                ShipLobby.TestFocus = -1;
                bot.ArcadePlaying.Value = false;
                yield return new WaitForSeconds(1f);
            }

            // ---- UFO DODGE
            ShipLobby.TestClickCabinet = true;
            yield return new WaitForSeconds(2.2f);
            Check(UfoArcade.Focused && ShipLobby.CamToCabinet < 1.4f, $"clicked the arcade machine: the camera's on its screen ({ShipLobby.CamToCabinet:0.00} m)");
            Check(Time.time - Hud.LobbyTeamsShownAt > 1f, "no team buttons over it either");
            yield return Snap("arcade_ready");
            int games0 = UfoArcade.Games;
            UfoArcade.TestAutoPilot = true;
            UfoArcade.TestFlap = true;
            yield return new WaitForSeconds(0.5f);
            Check(UfoArcade.Playing, "Space: the saucer's flying");
            int hashA = UfoArcade.PictureHash;
            yield return new WaitForSeconds(3.5f);
            Check(UfoArcade.PictureHash != hashA && UfoArcade.Glow.maxColorComponent > 0.02f, "the picture moves and the screen lights the room");
            yield return Snap("arcade_play");
            for (float t = 0; t < 20f && UfoArcade.Score < 3 && !UfoArcade.Crashed; t += 0.25f) yield return new WaitForSeconds(0.25f);
            Check(UfoArcade.Score >= 1, $"through gaps for points ({UfoArcade.Score})");
            UfoArcade.TestAutoPilot = false;
            for (float t = 0; t < 8f && !UfoArcade.Crashed; t += 0.1f) yield return new WaitForSeconds(0.1f);
            int score = UfoArcade.Score;
            Check(UfoArcade.Crashed && UfoArcade.Games == games0 + 1, "no flapping: it crashes - game over");
            Check(ArcadeBoard.LocalBest >= score && ArcadeBoard.Entries.Count >= 1, $"the best is kept and on the leaderboard ({ArcadeBoard.LocalBest}, {ArcadeBoard.Entries.Count} on it: {ArcadeBoard.Heading})");
            yield return new WaitForSeconds(1.2f);
            yield return Snap("arcade_game_over");
            UfoArcade.TestFlap = true;
            yield return new WaitForSeconds(0.5f);
            Check(!UfoArcade.Crashed && UfoArcade.Score == 0, "Space again: a new game");
            UfoArcade.Focus(false);
            yield return new WaitForSeconds(2.5f);
            Check(!UfoArcade.Focused && ShipLobby.CamToCabinet > 2f && Time.time - Hud.LobbyTeamsShownAt < 0.5f, "STOP PLAYING: back on the couch, the team buttons back");
            yield return Snap("arcade_room_after");
            Log("arcade done");
            Application.Quit(0);
        }
    }
}
