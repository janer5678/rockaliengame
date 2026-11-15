using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest botsim -host -solo -mode 2v2 [-simspeed 3]: a whole match played by bots only, to watch how they play.
        /// The host's own player is taken out and four bots play 2v2 at a few times the speed (Time.timeScale, the phases
        /// cut to match: BotBrain.SimScale keeps their sense of the clock right). Every few game seconds it logs where each
        /// bot is (home / out / the enemy's base), its plan and job; at the end, per bot, how much of the match it spent
        /// at home, out in the field and in the enemy base, its plans, kills, deaths, how often it got stuck, and how often
        /// its job flipped back and forth (A > B > A > B: a loop). Checks the bots really play the match.
        /// </summary>
        IEnumerator BotSimRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            float speed = 3f;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-simspeed" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sp)) speed = Mathf.Clamp(sp, 1f, 6f);
            int myTeam = me.Team.Value;
            // the host's player out of the way (the match goes on without it: -solo), four bots in
            me.NetworkObject.Despawn(true);
            yield return null;
            for (int t = 0; t < 2; t++)
                while (CountTeam(t) < Cfg.TeamCap(t) && PlayerNet.ServerAddBot(t)) yield return null;
            yield return new WaitForSeconds(0.5f);
            var bots = new List<PlayerNet>();
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bots.Add(p);
            Log($"botsim: {bots.Count} bots on {Cfg.MapLabel} (seed {Cfg.MapSeed}), {Cfg.ModeLabel}, x{speed}, host was on {Cfg.TeamName[myTeam]}");
            Check(bots.Count >= 4, $"four bots playing ({bots.Count})");

            Time.timeScale = speed;
            BotBrain.SimScale = speed;
            float respawn = Cfg.RespawnTime;
            Cfg.RespawnTime /= speed;
            var cut = new HashSet<GameState>();
            int n = bots.Count;
            var home = new float[n]; var field = new float[n]; var raidT = new float[n]; var enemyHalf = new float[n]; var deadT = new float[n];
            var lastTask = new BotBrain.Task[n]; var prevTask = new BotBrain.Task[n]; var flips = new int[n]; var changes = new int[n];
            var tally = new Dictionary<string, float>[n];
            for (int i = 0; i < n; i++) tally[i] = new Dictionary<string, float>();
            int lastSock = -1, captures = 0, steals = 0;
            float nextLog = 0f, started = Time.time;
            float gameSeconds = 0f;
            while (g != null && g.IsSpawned && g.S != GameState.GameOver && Time.time - started < 1500f * speed)
            {
                var s = g.S;
                if ((s == GameState.PreBall || s == GameState.BallLive || s == GameState.SuddenDeath) && !cut.Contains(s))
                {
                    cut.Add(s);
                    if (g.TimeLeft < 1e5f) g.DevSetTimeLeft(Mathf.Max(5f, g.TimeLeft / speed)); // (Classic / Progress: no clock - the bars fill in game time)
                    Log($"botsim: {s} - {g.TimeLeft * speed:0} game seconds");
                }
                float dt = Time.deltaTime;
                gameSeconds += dt;
                var ball = Ball.Instance;
                if (ball != null && ball.IsSpawned)
                {
                    int sock = ball.SocketTeam.Value;
                    if (sock != lastSock)
                    {
                        if (sock >= 0) { if (lastSock >= 0) steals++; captures++; Log($"botsim {gameSeconds:0}s: the ball goes into {Cfg.TeamName[sock]}'s machine"); }
                        lastSock = sock;
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    var p = bots[i];
                    if (p == null || !p.IsSpawned) continue;
                    var b = p.GetComponent<BotBrain>();
                    if (p.Dead.Value) { deadT[i] += dt; continue; }
                    int t = p.Team.Value, e = 1 - t;
                    var pos = p.transform.position;
                    float dh = Flat(pos, Cfg.BaseCenter[t]), de = Flat(pos, Cfg.BaseCenter[e]);
                    if (dh < 26f) home[i] += dt; else if (de < 26f) raidT[i] += dt; else field[i] += dt;
                    if (de < dh) enemyHalf[i] += dt;
                    var job = b.Job;
                    string key = $"{b.Priority}/{job}";
                    tally[i][key] = (tally[i].TryGetValue(key, out var v) ? v : 0f) + dt;
                    if (job != lastTask[i])
                    {
                        changes[i]++;
                        if (job == prevTask[i]) flips[i]++;
                        prevTask[i] = lastTask[i];
                        lastTask[i] = job;
                    }
                }
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 10f;
                    var sb = new StringBuilder($"botsim {gameSeconds:0}s {g.S} ball={(ball == null ? "-" : ball.IsCarried ? "carried by " + ball.Carrier.DisplayName : ball.SocketTeam.Value >= 0 ? "in " + Cfg.TeamName[ball.SocketTeam.Value] : "loose")}:");
                    for (int i = 0; i < n; i++)
                    {
                        var p = bots[i];
                        if (p == null || !p.IsSpawned) continue;
                        var b = p.GetComponent<BotBrain>();
                        int t = p.Team.Value;
                        var pos = p.transform.position;
                        sb.Append($" | {p.DisplayName}({Cfg.TeamName[t][0]},{b.Leaning}) {(p.Dead.Value ? "DEAD" : $"{b.Priority}/{b.Job}")} home {Flat(pos, Cfg.BaseCenter[t]):0}m enemy {Flat(pos, Cfg.BaseCenter[1 - t]):0}m hp {p.Health.Value:0} wood {p.Count(Cfg.CurrencyItem)} K{p.Kills.Value}/D{p.Deaths.Value}");
                    }
                    sb.Append($" | base {BotBrain.BaseScore(0):0}/{BotBrain.BaseScore(1):0} | stance {BotBrain.TeamStance(0)}{(BotBrain.SiegeGo(0) ? "!" : "")}/{BotBrain.TeamStance(1)}{(BotBrain.SiegeGo(1) ? "!" : "")} styles {BotBrain.TeamStyleOf(0)}/{BotBrain.TeamStyleOf(1)} | bars {g.ProgressOf(0):0.00}/{g.ProgressOf(1):0.00}");
                    Log(sb.ToString());
                }
                yield return null;
            }
            Time.timeScale = 1f;
            BotBrain.SimScale = 1f;
            Cfg.RespawnTime = respawn;
            Log($"botsim over after {gameSeconds:0} game seconds: {(g != null ? g.S.ToString() : "-")}, winner {(g != null && g.Winner.Value >= 0 ? Cfg.TeamName[g.Winner.Value] : "none")} ({(g != null ? g.EndReason.Value.ToString() : "")}), {captures} captures ({steals} taken from a machine)");
            float liveTotal = 0f;
            int totalKills = 0, outBots = 0, shots = 0, ranged = 0;
            for (int i = 0; i < n; i++)
            {
                var p = bots[i];
                if (p == null) continue;
                var b = p.GetComponent<BotBrain>();
                float alive = home[i] + field[i] + raidT[i];
                liveTotal = Mathf.Max(liveTotal, alive);
                totalKills += p.Kills.Value;
                var top = new List<KeyValuePair<string, float>>(tally[i]);
                top.Sort((a, c) => c.Value.CompareTo(a.Value));
                var jobs = new StringBuilder();
                for (int k = 0; k < Mathf.Min(8, top.Count); k++) jobs.Append($"{top[k].Key} {top[k].Value / Mathf.Max(1f, alive) * 100f:0}%, ");
                var plans = new Dictionary<BotBrain.Plan, int>();
                foreach (var pl in b.PlanLog) plans[pl] = (plans.TryGetValue(pl, out var c) ? c : 0) + 1;
                var ps = new StringBuilder();
                foreach (var kv in plans) ps.Append($"{kv.Key} x{kv.Value} ");
                float pctOut = (field[i] + raidT[i]) / Mathf.Max(1f, alive);
                if (pctOut > 0.35f) outBots++;
                shots += b.ShotsFired; ranged += HasRanged(p) ? 1 : 0;
                Log($"botsim {p.DisplayName} ({Cfg.TeamName[p.Team.Value]}, {b.Leaning}): home {home[i] / Mathf.Max(1f, alive) * 100f:0}% field {field[i] / Mathf.Max(1f, alive) * 100f:0}% enemy base {raidT[i] / Mathf.Max(1f, alive) * 100f:0}% enemy half {enemyHalf[i] / Mathf.Max(1f, alive) * 100f:0}% dead {deadT[i]:0}s | K{p.Kills.Value} D{p.Deaths.Value} | stuck {b.StuckTotal} | job changes {changes[i]}, flips {flips[i]} | long stucks {b.LongStucks} | shots {b.ShotsFired} ({b.ShotsHit} hit) door unsticks {b.DoorUnsticks} broke out {b.BrokeOut} door rushes {b.DoorRushes} C4 {b.C4Planted} raid switches {b.RaidSwitches} airdrops {b.AirdropsRun} | kit: {Kit(p)} | built {b.Built} crafted {b.Crafted} farm hits {b.FarmHits} | plans: {ps}| jobs: {jobs}");
                var tr = b.Trace;
                Log($"botsim {p.DisplayName} trace (last 40): {string.Join(" > ", tr.GetRange(Mathf.Max(0, tr.Count - 40), Mathf.Min(40, tr.Count)))}");
                if (b.StuckWhy.Count > 0) Log($"botsim {p.DisplayName} long stucks: {string.Join(" || ", b.StuckWhy)}");
            }
            Check(outBots >= 2, $"the bots get out and about: {outBots} of {n} spent over a third of the match away from home");
            Check(totalKills >= 4, $"they fight: {totalKills} kills between them");
            Check(ranged >= 2 && shots > 0, $"they climb the tech: {ranged} of {n} end up with a ranged weapon, {shots} shots fired");
            Check(captures >= 1, $"the ball gets taken: {captures} captures, {steals} of them out of a machine");
            Log("botsim done");
            Application.Quit(0);
        }

        /// <summary>A player's weapons, armour and raiding kit, for the log.</summary>
        static string Kit(PlayerNet p)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < p.Inv.Count; i++)
            {
                var id = p.Inv[i].Id;
                if (id == Item.None || id == Cfg.CurrencyItem || id == Item.Berry || id == Item.BuildingPlan || id == Item.Wood || id == Item.Stone) continue;
                sb.Append(id).Append(p.Inv[i].Count > 1 ? "x" + p.Inv[i].Count : "").Append(' ');
            }
            if (p.ArmorHp.Value > 0) sb.Append($"armour {p.ArmorHp.Value}");
            return sb.ToString();
        }

        static bool HasRanged(PlayerNet p)
        {
            for (int i = 0; i < p.Inv.Count; i++)
            {
                var id = p.Inv[i].Id;
                if (id == Item.Bow || id == Item.Crossbow || id == Item.Shotgun || id == Item.Revolver || id == Item.Pistol || id == Item.Sniper) return true;
            }
            return false;
        }

        static int CountTeam(int t)
        {
            int k = 0;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.Team.Value == t) k++;
            return k;
        }
    }
}
