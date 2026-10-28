using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt17 -host -solo [-rules autowood] -shotdir DIR: the 2026-10-28 (2) patch -
        /// a roof over the stairs (BuildGrid.RoofOverStairs: a floor two levels over stairs is held up by them, and a floor
        /// aimed at the stairs' top goes one level up instead); the bots' plans (BotBrain.Plans.cs: a priority they commit
        /// to for a stretch - farming till they have a load, building, raiding, hunting, guarding) and four house styles
        /// (BotBrain.House.cs); everyone slower (the old speeds kept in Cfg.Old*Speed); the wood machine doubled; and
        /// "respawn in the wild" only on your own half of the map (Cfg.OnOwnSide).
        /// </summary>
        IEnumerator Prompt17Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            g.TimerPaused.Value = true;

            // ---- the numbers
            {
                Check(Mathf.Approximately(Cfg.WalkSpeed, 4.25f) && Mathf.Approximately(Cfg.SprintSpeed, 6.4f) && Mathf.Approximately(Cfg.CrouchSpeed, 2.2f),
                    $"everyone's slower: walk {Cfg.WalkSpeed}, sprint {Cfg.SprintSpeed}, crouch {Cfg.CrouchSpeed} (15% off)");
                Check(Mathf.Approximately(Cfg.OldWalkSpeed, 5f) && Mathf.Approximately(Cfg.OldSprintSpeed, 7.5f) && Mathf.Approximately(Cfg.OldCrouchSpeed, 2.6f),
                    $"...the old speeds are kept to go back to ({Cfg.OldWalkSpeed} / {Cfg.OldSprintSpeed} / {Cfg.OldCrouchSpeed})");
                Check(Cfg.AutoWoodPerSecond == 10 && Cfg.AutoWoodLevel1 == 24 && Cfg.AutoWoodLevel2 == 80
                    && (Cfg.LuckyBlock || (Cfg.WoodGenRate(1) == 10 && Cfg.WoodGenRate(2) == 24 && Cfg.WoodGenRate(3) == 80)),
                    $"the wood machine makes double: {Cfg.WoodGenRate(1)} / {Cfg.WoodGenRate(2)} / {Cfg.WoodGenRate(3)} wood a second by level");
            }

            // ---- walking really is that speed
            {
                pc.LocalTeleport(OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 12f)), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.6f);
                var p0 = me.transform.position;
                Binds.TestHold(Bind.Forward, true);
                yield return new WaitForSeconds(2f);
                Binds.TestHold(Bind.Forward, false);
                float went = Flat(me.transform.position, p0) / 2f;
                Check(went > Cfg.WalkSpeed * 0.75f && went < Cfg.WalkSpeed * 1.15f, $"walking: {went:0.0} m/s (walk speed {Cfg.WalkSpeed})");
                yield return new WaitForSeconds(0.3f);
            }

            // ---- a roof over the stairs
            {
                var stairs = new PieceKey(PieceKey.KStairs, 4, 4, 0, 0);
                bool only(PieceKey k) => k.Equals(stairs);
                var top = new PieceKey(PieceKey.KFloor, 4, 4, 1, 0);
                var lid = new PieceKey(PieceKey.KFloor, 4, 4, 2, 0);
                Check(BuildGrid.IsOccupied(top, only) && !BuildGrid.IsOccupied(lid, only), "the slot at the top of the stairs is theirs; the one over it is free");
                Check(BuildGrid.IsSupported(lid, only) && BuildGrid.RoofOverStairs(lid, only) && BuildGrid.StairsUnder(top, only),
                    "a floor two levels over stairs is held up by them: a roof over the stairs");
                Check(!BuildGrid.IsSupported(new PieceKey(PieceKey.KFloor, 5, 4, 2, 0), only) && !BuildGrid.IsSupported(new PieceKey(PieceKey.KFloor, 4, 4, 3, 0), only),
                    "...only right over them, and only two levels up");
                // for real: a foundation, stairs on it, then the roof over them - in our base, with the plan
                var c = Cfg.BaseCenter[team];
                int ci = Mathf.RoundToInt(c.x / Cfg.Cell) + Mathf.RoundToInt(side.x) * 3, cj = Mathf.RoundToInt(c.z / Cfg.Cell) + Mathf.RoundToInt(side.z) * 3;
                var cell = BuildGrid.CellCenter(ci, cj);
                pc.LocalTeleport(OnGround(cell - back * 4f), Cfg.SpawnYaw(team));
                me.ServerGive(Cfg.BuildItem, 400);
                if (me.Count(Item.BuildingPlan) == 0) me.ServerGive(Item.BuildingPlan, 1);
                yield return Hold(me, Item.BuildingPlan);
                var fk = new PieceKey(PieceKey.KFoundation, ci, cj, 0, 0);
                var sk = new PieceKey(PieceKey.KStairs, ci, cj, 0, 0);
                var tk = new PieceKey(PieceKey.KFloor, ci, cj, 1, 0);
                var lk = new PieceKey(PieceKey.KFloor, ci, cj, 2, 0);
                me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
                yield return new WaitForSeconds(0.6f);
                me.PlaceRpc((byte)PieceType.Stairs, ci, cj, 0, 0);
                yield return new WaitForSeconds(0.6f);
                Check(BuildGrid.Registry.ContainsKey(fk) && BuildGrid.Registry.ContainsKey(sk), $"built a foundation and stairs on it in our base ({ci}, {cj})");
                me.PlaceRpc((byte)PieceType.Floor, ci, cj, 1, 0);
                yield return new WaitForSeconds(0.6f);
                Check(!BuildGrid.Registry.ContainsKey(tk), "a floor right over the stairs (their top) is refused, as before");
                me.PlaceRpc((byte)PieceType.Floor, ci, cj, 2, 0);
                yield return new WaitForSeconds(0.6f);
                Check(BuildGrid.Registry.ContainsKey(lk), "a floor two levels up goes in: the roof over the stairs");
                yield return Snap("prompt17_roof_over_stairs");
            }

            // ---- the bots: a plan they stick to, and four kinds of house
            {
                var sizes = new int[4];
                for (int s = 0; s < 4; s++) { BotBrain.TestStyle = s; sizes[s] = BotBrain.HousePlan(enemy).Count; }
                BotBrain.TestStyle = -1;
                BotBrain.HousePlan(enemy);
                bool distinct = sizes[0] != sizes[1] && sizes[1] != sizes[2] && sizes[2] != sizes[3] && sizes[0] != sizes[2];
                Check(distinct && sizes[0] > 40 && sizes[2] > sizes[0], $"four house styles, each a different plan: Bunker {sizes[0]}, Tower {sizes[1]}, Compound {sizes[2]}, Keep {sizes[3]} pieces");
                Check(BotBrain.StyleOf(0) != BotBrain.StyleOf(1) && (Cfg.TeamCount < 3 || BotBrain.StyleOf(2) != BotBrain.StyleOf(0)),
                    $"the teams build different kinds this match: {BotBrain.StyleOf(0)} / {BotBrain.StyleOf(1)} / {BotBrain.StyleOf(2)} / {BotBrain.StyleOf(3)} (seed {Cfg.MapSeed})");
                int before = PlayerNet.All.Count;
                bool added = PlayerNet.ServerAddBot(enemy);
                yield return new WaitForSeconds(0.6f);
                PlayerNet bot = null;
                foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
                var brain = bot != null ? bot.GetComponent<BotBrain>() : null;
                Check(added && brain != null, $"a bot joined the enemy team ({PlayerNet.All.Count - before} new)");
                if (brain != null)
                {
                    if (bot.Dead.Value) bot.ServerRespawn(false);
                    yield return new WaitForSeconds(4f);
                    Check(brain.Priority != BotBrain.Plan.None && brain.PlansMade >= 1, $"it has set its mind on something: {brain.Priority} ({brain.Job}, a {brain.Leaning}; {brain.PlanLeft:0} s to go)");
                    bool behindWall = g.S == GameState.PreBall || MapBuilder.GlassUp;
                    int plans0 = brain.PlansMade;
                    var seen = new HashSet<BotBrain.Plan>();
                    bool onlyHome = true;
                    for (float t = 0f; t < 20f; t += 1f)
                    {
                        seen.Add(brain.Priority);
                        onlyHome &= brain.Priority == BotBrain.Plan.Farm || brain.Priority == BotBrain.Plan.Build || brain.Priority == BotBrain.Plan.None;
                        yield return new WaitForSeconds(1f);
                    }
                    Check(brain.PlansMade - plans0 <= 6, $"it commits: {brain.PlansMade - plans0} new plans in 20 s ({string.Join(", ", seen)})");
                    if (behindWall) Check(onlyHome, $"behind the wall it only farms and builds ({string.Join(", ", seen)})");
                    Check(brain.FarmHits > 0 || bot.Count(Cfg.CurrencyItem) > 0 || brain.Job == BotBrain.Task.Craft || brain.Job == BotBrain.Task.Build, $"...and gets on with it ({brain.FarmHits} tree hits, {bot.Count(Cfg.CurrencyItem)} {Cfg.CurrencyItem}, {brain.Job})");
                    yield return Snap("prompt17_bot_plan");
                    PlayerNet.ServerRemoveBot(enemy);
                    yield return new WaitForSeconds(0.3f);
                }
            }

            // ---- respawn in the wild: your half only
            {
                var ours = Cfg.BaseCenter[team] * 0.5f + side * 25f;
                var theirs = Cfg.BaseCenter[enemy] * 0.3f + side * 25f; // (well clear of their base: on their half is the only thing wrong with it)
                Check(Cfg.OnOwnSide(team, ours) && !Cfg.OnOwnSide(team, theirs) && Cfg.OnOwnSide(enemy, theirs), "your half: the side of the map your base is on");
                bool bad = !Cfg.WildSpotOk(team, theirs, out var why);
                Check(Cfg.WildSpotOk(team, ours) && bad && why == "the enemy's half", $"open ground on the enemy's half: no ({why})");
                bool allOurs = true;
                for (int i = 0; i < 30; i++) { NetGame.WildSpawnPoint(team, out var wp, out _); allOurs &= Cfg.OnOwnSide(team, wp); }
                Check(allOurs, "30 random wild respawns: every one on our half");
                NetGame.PickedSpawnPoint(team, theirs, out var picked, out _);
                Check(Cfg.OnOwnSide(team, picked) && Cfg.WildSpotOk(team, picked), $"a pick on the enemy's half comes back on ours ({picked.x:0}, {picked.z:0})");
                g.TimerPaused.Value = false;
                if (g.S == GameState.PreBall) g.DevSkipPhase(GameState.PreBall);
                for (float t = 0; t < 8f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(1f);
                me.ServerKill(null);
                for (float t = 0; t < Cfg.RespawnTime + 4f && !me.ChoosingRespawn; t += 0.25f) yield return new WaitForSeconds(0.25f);
                Hud.TestWildPick(true);
                yield return new WaitForSeconds(1f);
                Check(me.ChoosingRespawn && Hud.WildNoGoCells >= 72 * 72 * 0.45f, $"the picker shades the enemy's half red ({Hud.WildNoGoCells} of {72 * 72} cells)");
                yield return Snap("prompt17_wild_half");
                me.RespawnAtRpc(theirs);
                yield return new WaitForSeconds(0.8f);
                Check(!me.Dead.Value && Cfg.OnOwnSide(team, me.transform.position), $"picking the enemy's half: you come back on your own ({me.transform.position.x:0}, {me.transform.position.z:0})");
            }

            Log("prompt17 done");
            Application.Quit(0);
        }
    }
}
