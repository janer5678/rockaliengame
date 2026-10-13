using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>-autotest prompt8menu -shotdir DIR: the main menu's mode page with Jonah Test Mode Bruh picked - its HOW
        /// YOU WIN row (NORMAL / PROGRESSION), and PROGRESS TIME under it once PROGRESSION is picked.</summary>
        IEnumerator Prompt8MenuShots()
        {
            Hud.TestNewMenu = true;
            yield return new WaitForSeconds(2.5f);
            int was = Cfg.JonahBruhProgress;
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.JonahBruh);
            Hud.TestNewPage(6);
            Hud.TestModesOpen(false);
            Cfg.JonahBruhProgress = 0;
            yield return Snap("prompt8_menu_jonah_normal");
            Check(Time.time - Hud.JonahBruhMenuShownAt < 1f, "Jonah Test Mode Bruh's mode page has a HOW YOU WIN row");
            Cfg.JonahBruhProgress = 1;
            yield return Snap("prompt8_menu_jonah_progression");
            Cfg.JonahBruhProgress = was;
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.Progress);
            yield return new WaitForSeconds(0.3f);
            float shown = Hud.JonahBruhMenuShownAt;
            yield return new WaitForSeconds(0.3f);
            Check(Hud.JonahBruhMenuShownAt == shown, "...and only for Jonah Test Mode Bruh");
            Log("prompt8menu done");
            Application.Quit(0);
        }

        /// <summary>
        /// -autotest prompt8 -host -solo [-rules jonahbruh | airdrop] -shotdir DIR: the ball buff x1.5 everywhere, the default
        /// airdrop pool (just the nine: every airdrop item in Airdrop Test, the death wand too in Jonah Test Mode Bruh), the
        /// attention snail (15 s, your window half the screen with a tiny hotbar in it), the lifting stick (way higher, fast,
        /// under the glass), the airdrop's beam right after the notice with the crate landing when it always did; and in Jonah
        /// Test Mode Bruh: NORMAL / PROGRESSION, the 1000 Advanced Trade Station with Armour (150) at its top, the one-cell
        /// spawn bedrock with the smaller machine and the wood machine on it, magical stone, and picking where to respawn.
        /// </summary>
        IEnumerator Prompt8Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);

            // ---- the ball buff: x1.5, whatever the mode or team size
            Check(Mathf.Approximately(Cfg.BallGatherNow, 1.5f), $"the ball in your machine: x{Cfg.BallGatherNow} gathering ({Cfg.RulesName(Cfg.Rules)}, {Cfg.TeamCount} teams)");

            // ---- the airdrop pool
            {
                var loot = Cfg.AirdropLoot;
                var want = new List<Item> { Item.Jetpack, Item.C4, Item.GravityStaff, Item.BouncyCastle, Item.Wallhack, Item.BombBush, Item.DanceBomb, Item.SlowGun, Item.BearJuice };
                if (Cfg.AirdropTest)
                {
                    var missing = new List<string>();
                    foreach (var it in Cfg.AirdropChoices) if (Cfg.InAirdropPool(it) && !loot.Contains(it)) missing.Add(it.ToString());
                    Check(missing.Count == 0 && loot.Count > 20, $"Airdrop Test: every airdrop item is in its pool ({loot.Count}{(missing.Count > 0 ? ", missing " + string.Join(" ", missing) : "")})");
                }
                else
                {
                    if (Cfg.JonahBruh) want.Add(Item.DeathWand);
                    var extra = new List<string>(); var missing = new List<string>();
                    foreach (var it in loot) if (!want.Contains(it)) extra.Add(it.ToString());
                    foreach (var it in want) if (!loot.Contains(it)) missing.Add(it.ToString());
                    Check(extra.Count == 0 && missing.Count == 0, $"the airdrop pool by default: {string.Join(", ", loot)}{(extra.Count > 0 ? " - extra " + string.Join(" ", extra) : "")}{(missing.Count > 0 ? " - missing " + string.Join(" ", missing) : "")}");
                }
                Check(Cfg.ItemName(Item.BombBush).Contains("Bush"), $"(the berry bush is the {Cfg.ItemName(Item.BombBush)})");
            }

            // ---- Jonah Test Mode Bruh: the lists and prices
            {
                var t2 = new List<int>();
                Cfg.AddTier(t2, 2);
                int armour = Cfg.CraftIndexOf(Item.HeavyArmor);
                int w2 = Cfg.RecipeIndex(Item.Workbench2);
                int w2Wood = w2 >= 0 ? Cfg.GetRecipe(w2).Wood : -1;
                if (Cfg.JonahBruh)
                {
                    Check(t2.Count > 0 && t2[0] == armour && Cfg.CraftRecipe(armour, team).Wood == 150, $"Armour is at the top of the Advanced Trade Station for {Cfg.CraftRecipe(armour, team).Wood} (first there: {(t2.Count > 0 ? Cfg.CraftRecipe(t2[0], team).Name : "nothing")})");
                    Check(w2Wood == 1000, $"the Advanced Trade Station costs {w2Wood}");
                    Check(Cfg.RecipeIndex(Item.DeathWand) < 0 && Cfg.AirdropLoot.Contains(Item.DeathWand) && Cfg.PickAirdropItem(new List<Item> { Item.DeathWand }) == Item.DeathWand,
                        "the death wand: not crafted, but back in the airdrops");
                    Check(Cfg.RecipeIndex(Item.BearTrap) < 0 && Cfg.RecipeIndex(Item.Ladder) < 0 && Cfg.RecipeIndex(Item.Chainsaw) < 0 && Cfg.RecipeIndex(Item.BetterHatchet) >= 0, "still no stun trap, ladder or chainsaw; the better hatchet");
                    int was = Cfg.JonahBruhProgress;
                    Cfg.JonahBruhProgress = 0;
                    bool normal = !Cfg.ProgressMode && !Cfg.ClassicMode;
                    Cfg.JonahBruhProgress = 1;
                    bool prog = Cfg.ProgressMode && Cfg.ClassicMode;
                    Cfg.JonahBruhProgress = was;
                    Check(normal && prog, $"NORMAL plays like Classic, PROGRESSION like Progress ({normal}, {prog})");
                }
                else
                {
                    Check(w2Wood == Cfg.Workbench2Wood && (t2.Count == 0 || t2[0] != armour) && !Cfg.AirdropLoot.Contains(Item.DeathWand) && !Cfg.SmallBedrock && !Cfg.JonahBruhProgression,
                        $"(not Jonah Test Mode Bruh: the station {w2Wood}, Armour not first, no death wand in airdrops, the big bedrock)");
                }
            }

            // ---- the spawn bedrock: one cell in Jonah Test Mode Bruh, the machine and the wood machine on it
            {
                var bc = Cfg.BedrockCenter(team);
                int ci = Mathf.RoundToInt(Cfg.BaseCenter[team].x / Cfg.Cell), cj = Mathf.RoundToInt(Cfg.BaseCenter[team].z / Cfg.Cell), cells = 0;
                for (int i = ci - 4; i <= ci + 4; i++) for (int j = cj - 4; j <= cj + 4; j++) if (Cfg.IsBedrockCell(i, j)) cells++;
                var machine = GameObject.Find("AlienMachine " + Cfg.TeamName[team]);
                var wood = GameObject.Find("WoodMachine " + Cfg.TeamName[team]);
                float hb = Cfg.BedrockHalfNow + 0.01f;
                bool On(Vector3 p) => Mathf.Abs(p.x - bc.x) <= hb && Mathf.Abs(p.z - bc.z) <= hb;
                bool spawnsOn = true;
                for (int s = 0; s < 4; s++) spawnsOn &= On(Cfg.SpawnPos(team, s));
                if (Cfg.JonahBruh)
                {
                    Check(cells == 1 && Mathf.Approximately(Cfg.BedrockHalfNow, 1.5f), $"the spawn bedrock is one cell ({cells} cell, {Cfg.BedrockHalfNow * 2f} m across)");
                    Check(On(Cfg.MachinePos(team)) && On(Cfg.SocketPos(team)) && On(Cfg.WoodMachinePos(team)) && On(Cfg.WoodTrayPos(team)) && spawnsOn, "the machine, its socket, the wood machine, its wood and the spawn spots are all on it");
                    Check(machine != null && Mathf.Abs(machine.transform.localScale.x - Cfg.SmallMachineScale) < 0.01f && wood != null && Mathf.Abs(wood.transform.localScale.x - Cfg.SmallMachineScale) < 0.01f,
                        $"the alien machine and the wood machine are smaller (x{(machine != null ? machine.transform.localScale.x : -1f):0.##}, x{(wood != null ? wood.transform.localScale.x : -1f):0.##})");
                    // nothing solid where you spawn
                    var blocked = new List<string>();
                    for (int s = 0; s < 2; s++)
                    {
                        var sp = Cfg.SpawnPos(team, s);
                        foreach (var c in Physics.OverlapCapsule(sp + Vector3.up * 0.45f, sp + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                            if (c.GetComponentInParent<PlayerNet>() == null && c.GetComponentInParent<Ball>() == null) blocked.Add($"{s}:{c.name}");
                    }
                    Check(blocked.Count == 0, $"the spawn spots are clear{(blocked.Count > 0 ? ": " + string.Join(" ", blocked) : "")}");
                    // the ball in the socket, to see it fits
                    var ball = Ball.Instance;
                    if (ball != null && ball.IsSpawned)
                    {
                        ball.ServerSocket(team);
                        yield return new WaitForSeconds(0.5f);
                    }
                    var eye = bc - back * 4.2f + side * 1.2f;
                    eye.y = Cfg.BaseY + 1.6f;
                    pc.LocalTeleport(eye - Vector3.up * 1.55f, Quaternion.LookRotation(bc - eye).eulerAngles.y);
                    yield return new WaitForSeconds(0.5f);
                    var lk = Quaternion.LookRotation(bc + Vector3.up * (Cfg.BaseY + 0.6f) - me.EyePos).eulerAngles;
                    pc.SetLook(lk.y, lk.x > 180f ? lk.x - 360f : lk.x);
                    yield return new WaitForSeconds(0.5f);
                    yield return Snap("prompt8_small_bedrock");
                    pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team) + 180f);
                    yield return new WaitForSeconds(0.4f);
                    pc.SetLook(Cfg.SpawnYaw(team) + 180f, 22f);
                    yield return new WaitForSeconds(0.4f);
                    yield return Snap("prompt8_small_bedrock_from_spawn");
                    if (ball != null && ball.IsSpawned) { ball.ServerPlaceInDome(); yield return new WaitForSeconds(0.3f); }
                }
                else Check(cells == 4 && machine != null && Mathf.Approximately(machine.transform.localScale.x, 1f) && spawnsOn, $"(the bedrock's the usual 2 x 2: {cells} cells, the machine full size)");
            }

            // ---- magical stone (Jonah Test Mode Bruh), plain stone everywhere else
            {
                var at = OnGround(Cfg.BaseCenter[team] - back * 10f + side * 6f, 0f);
                var wall = SpawnKeylessPiece(PieceType.Wall, team, at, Quaternion.LookRotation(back));
                yield return new WaitForSeconds(0.3f);
                wall.ServerUpgrade(1);
                yield return new WaitForSeconds(0.6f);
                int glowing = 0, runes = 0;
                foreach (var r in wall.GetComponentsInChildren<MeshRenderer>()) if (r.sharedMaterial == MagicStone.GlowMat) glowing++;
                foreach (var tr in wall.GetComponentsInChildren<Transform>()) if (tr.name == "rune") runes++;
                if (Cfg.JonahBruh)
                {
                    Check(wall.Tier.Value == 1 && glowing >= 10 && runes == 6, $"stone walls are magical: glowing seams and runes ({glowing} glowing parts, {runes} runes)");
                    var eye = at - back * 4f;
                    pc.LocalTeleport(OnGround(eye, 0.1f), Quaternion.LookRotation(back).eulerAngles.y);
                    yield return new WaitForSeconds(0.4f);
                    pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 4f);
                    yield return new WaitForSeconds(0.4f);
                    yield return Snap("prompt8_magic_stone");
                }
                else Check(glowing == 0 && runes == 0, "(plain stone outside Jonah Test Mode Bruh)");
                wall.NetworkObject.Despawn(true);
            }

            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(2f);
            var spot = OnGround(Vector3.Lerp(Cfg.BaseCenter[team], Vector3.zero, 0.55f) + side * 14f, 0.1f);

            // ---- the attention snail: 15 s, your own view half the screen, your hotbar tiny in it
            {
                pc.LocalTeleport(spot, Quaternion.LookRotation(-back).eulerAngles.y);
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.ServerGive(Item.Hatchet, 1);
                me.ServerGive(Item.Spear, 1);
                me.ServerGive(Item.AttentionSnail, 1);
                yield return Hold(me, Item.AttentionSnail);
                yield return new WaitForSeconds(0.5f);
                var put = OnGround(spot - back * 3f, 0f);
                double t0 = g.NetworkManager.ServerTime.Time;
                me.UseWackyRpc(Item.AttentionSnail, me.EyePos, (put - me.EyePos).normalized, put, false, default, 0);
                yield return new WaitForSeconds(0.8f);
                double life = -1;
                foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Snail) life = gd.Until - t0;
                var pr = PlayerController.PipGuiRect;
                Check(life > 14.5 && life < 15.5, $"the attention snail lasts 15 s ({life:0.0})");
                Check(PlayerController.PipOn && Mathf.Abs(pr.width - Screen.width * 0.5f) < 2f && Mathf.Abs(pr.height - Screen.height * 0.5f) < 2f,
                    $"your own view is half the screen ({pr.width:0} x {pr.height:0} of {Screen.width} x {Screen.height})");
                Check(Time.time - Hud.PipHotbarShownAt < 0.5f, "your hotbar's in it, tiny");
                yield return Snap("prompt8_snail_window");
                for (float t = 0; t < 16f && PlayerController.PipOn; t += 0.25f) yield return new WaitForSeconds(0.25f);
                Check(!PlayerController.PipOn, "...and it's gone after 15 s");
            }

            // ---- the lifting stick: way higher (under the glass), fast
            {
                pc.LocalTeleport(spot, Quaternion.LookRotation(side).eulerAngles.y);
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                var wallAt = OnGround(spot + side * 7f, 0f);
                var wall = SpawnKeylessPiece(PieceType.Wall, team, wallAt, Quaternion.LookRotation(side));
                yield return new WaitForSeconds(0.4f);
                me.ServerGive(Item.StickOfTruth, 1, Cfg.StickOfTruthShots);
                yield return Hold(me, Item.StickOfTruth);
                yield return new WaitForSeconds(0.45f);
                var aim = wall.transform.position + Vector3.up * 1.2f;
                me.UseWackyRpc(Item.StickOfTruth, me.EyePos, (aim - me.EyePos).normalized, aim, true, new NetworkObjectReference(wall.NetworkObject), 0);
                yield return new WaitForSeconds(1.1f);
                float up = NetGame.LiftedBy(wall.NetworkObject), top = NetGame.LiftTop(wallAt);
                Check(up > 27f && up > top * 0.9f, $"the lifting stick takes a wall {up:0} m up in a second (it was 9 m; as high as it goes under the glass here: {top:0} m)");
                pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 60f);
                yield return new WaitForSeconds(0.3f);
                yield return Snap("prompt8_lifted_wall");
                yield return new WaitForSeconds(Cfg.LiftTime + 2f);
                Check(NetGame.LiftedBy(wall.NetworkObject) == 0f && Vector3.Distance(wall.transform.position, wallAt) < 0.05f, "...and it comes back down into its place");
                wall.NetworkObject.Despawn(true);
                float y0 = me.transform.position.y;
                me.UseWackyRpc(Item.StickOfTruth, me.EyePos, Vector3.down, me.transform.position, true, new NetworkObjectReference(me.NetworkObject), 0);
                float topY = y0, glass = MapDome.Built ? MapDome.HighestOver(spot.x, spot.z, 0f) : float.MaxValue;
                for (float t = 0; t < 4f; t += Time.deltaTime) { topY = Mathf.Max(topY, me.transform.position.y); yield return null; }
                Check(topY - y0 > 30f && topY < glass, $"a player goes {topY - y0:0} m up (it was about 25), up to just under the glass ({glass - y0:0} m up there)");
                for (float t = 0; t < 8f && me.transform.position.y > y0 + 1f; t += 0.25f) yield return new WaitForSeconds(0.25f);
            }

            // ---- the airdrop: its beam's out straight after AIRDROP INCOMING; the crate lands when it always did
            if (!Cfg.JonahBruh)
            {
                foreach (var c in new List<Container>(Container.All)) if (c != null && c.IsAirdrop && c.IsSpawned) c.NetworkObject.Despawn(true);
                g.DevSpawnAirdrop();
                double st = g.LaneStartAt(0);
                var gp = g.LanePosAt(0);
                var toMid = new Vector3(-gp.x, 0, -gp.z);
                if (toMid.sqrMagnitude < 1f) toMid = Vector3.forward;
                var eye = OnGround(gp + toMid.normalized * 34f, 0.1f);
                float yaw = Quaternion.LookRotation(-toMid).eulerAngles.y;
                pc.LocalTeleport(eye, yaw);
                pc.SetLook(yaw, -40f);
                double E() => g.NetworkManager.ServerTime.Time - st;
                while (E() < NetGame.DropArrive + 0.9) yield return null;
                float ground = MapBuilder.Height(gp.x, gp.z);
                Check(E() < 3.0 && AirdropShip.BeamBottom < ground + 1.5f, $"the beam's down to the ground {E():0.0} s after the notice (bottom {AirdropShip.BeamBottom - ground:0.0} m up)");
                yield return Snap("prompt8_airdrop_beam_at_once");
                int Crates() { int n = 0; foreach (var c in Container.All) if (c != null && c.IsAirdrop) n++; return n; }
                while (E() < NetGame.DropLand - 1.0) yield return null;
                int before = Crates();
                while (E() < NetGame.DropLand + 1.0) yield return null;
                int after = Crates();
                Check(before == 0 && after >= 1 && Mathf.Approximately(NetGame.DropLand, 32f), $"the crate lands {NetGame.DropLand:0} s after the notice, as it always did ({before} -> {after} crates)");
            }

            // ---- Jonah Test Mode Bruh: respawn in the wild where you pick on the map
            if (Cfg.JonahBruh)
            {
                for (int round = 0; round < 2; round++)
                {
                    me.ServerKill(null);
                    for (float t = 0; t < Cfg.RespawnTime + 4f && !me.ChoosingRespawn; t += 0.25f) yield return new WaitForSeconds(0.25f);
                    Hud.TestWildPick(true);
                    yield return new WaitForSeconds(0.6f);
                    if (round == 0)
                    {
                        Check(me.ChoosingRespawn && Time.time - Hud.WildPickerShownAt < 0.5f, "dead: RESPAWN IN THE WILD opens a map to pick the spot");
                        yield return Snap("prompt8_respawn_picker");
                    }
                    var pick = round == 0 ? spot + side * 6f : Cfg.BaseCenter[enemy];
                    me.RespawnAtRpc(pick);
                    yield return new WaitForSeconds(0.8f);
                    var p = me.transform.position;
                    if (round == 0) Check(!me.Dead.Value && Flat(p, pick) < 4f, $"...and you come back right there ({Flat(p, pick):0.0} m off)");
                    else Check(!me.Dead.Value && Cfg.BaseTeamAt(p) != enemy, $"picking the enemy's base: you come back just outside it ({Cfg.TeamLabel(Cfg.BaseTeamAt(p))})");
                }
                Hud.TestWildPick(false);
            }

            Log("prompt8 done");
            Application.Quit(0);
        }
    }
}
