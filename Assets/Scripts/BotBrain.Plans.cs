using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// What a bot has set its mind on (BotBrain.cs): its priority for the next stretch of the match, which it commits to
    /// the way a person does instead of flitting between jobs every think -
    ///   Farm:  out to the trees till it's carrying a good load (its own idea of one: m_PlanWood), only then home;
    ///   Build: home, spending it - its tools, a chest, the team's goals (the house, weapons, Trade Stations, upgrades:
    ///          BotBrain.Goals.cs), the spare in the chest - till there's nothing left it can pay for;
    ///   Raid:  off for the ball in an enemy machine (Bedwars: an enemy machine), with whoever's carrying it, fetching it loose;
    ///   Hunt:  looking for a fight - the lanes between the bases, lying in wait outside the enemy's, or the approaches to its own;
    ///   Guard: minding its base (patrolling out front, the wood pile, bags).
    /// Picked by weighted chance from what the match looks like (behind the wall only farming and building; the ball's
    /// whereabouts, how late it is, how rich the team is, whether it's armed) and its leaning (raiders raid and hunt,
    /// gatherers farm, defenders guard and build), each for a stretch (a minute or two) or till it's done - a load
    /// farmed, the ball home, nothing left to buy. A plan with nothing in it is put off for a while. Emergencies still
    /// cut in over any plan (Think): the ball in its hands, an enemy running off with it, a fight on it, enemies in its
    /// base, a loose ball right there.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>A bot's priority for a while (tests can read it: Priority).</summary>
        public enum Plan : byte { None, Farm, Build, Raid, Hunt, Guard, Rush }

        Plan m_Plan;
        float m_PlanUntil, m_PlanIdle = -1f;
        int m_PlanWood;     // Farm: the wood on hand it's after before it goes home
        int m_HuntStyle;    // Hunt: 0 the lanes between the bases, 1 lying in wait off the enemy's base, 2 the approaches to its own
        readonly float[] m_PlanOff = new float[7]; // (a plan that came to nothing: not again till then)
        /// <summary>(tests) when it got to the glass wall to wait for the drop (Rush; -1 not yet).</summary>
        public float AtWallAt { get; private set; } = -1f;

        /// <summary>What it has set its mind on right now.</summary>
        public Plan Priority => m_Plan;
        /// <summary>(tests) how many plans it has made, and how long the current one runs.</summary>
        public int PlansMade { get; private set; }
        public float PlanLeft => Mathf.Max(0f, m_PlanUntil - Time.time);
        /// <summary>(tests) every plan it has been on this match, in order.</summary>
        public readonly List<Plan> PlanLog = new List<Plan>(16);

        /// <summary>The plan it's on has run its course: a load farmed, the ball home, too hurt to hunt.</summary>
        bool PlanDone(NetGame g, Ball ball, PlayerNet carrier, bool withBall, bool live)
        {
            switch (m_Plan)
            {
                case Plan.Farm: return m_P.Count(Cfg.CurrencyItem) >= m_PlanWood;
                case Plan.Raid:
                    if (!withBall) return !(live && Cfg.Bedwars);
                    if (Hp01 < m_RetreatAt) return true;
                    return carrier == null && ball.SocketTeam.Value == MyTeam;
                case Plan.Hunt: return Hp01 < 0.4f;
                case Plan.Guard: return !live || (withBall && carrier == null && ball.SocketTeam.Value >= 0 && ball.SocketTeam.Value != MyTeam); // (the ball's gone to them: off after it)
                case Plan.Rush: return live && (!withBall || ball.IsCarried || ball.SocketTeam.Value >= 0); // (it's been taken: the plans take over)
                case Plan.Build: return live && !pre(g) && Enclosed(MyTeam) && BallTheirs(withBall, ball, carrier) && HasWeapon && Time.time - m_PlanStarted > 25f; // (the ball's theirs: done building)
            }
            return false;
        }

        float m_PlanStarted;

        /// <summary>A Rusher, half a minute before the wall drops: time to go and wait at the glass by the ball.</summary>
        bool WantsRush(NetGame g) =>
            m_Bent == Bent.Rusher && g.S == GameState.PreBall && !Cfg.NoBall && !Cfg.Builder && !Cfg.Tutorial && !g.SuddenBall.Value && !g.NoClock
            && TimeLeft(g) < 32f && TimeLeft(g) > 0.5f && !m_P.CarryingBall;
        static bool pre(NetGame g) => g.S == GameState.PreBall || MapBuilder.GlassUp;
        bool BallTheirs(bool withBall, Ball ball, PlayerNet carrier) =>
            withBall && ((carrier != null && carrier.Team.Value != MyTeam) || (carrier == null && ball.SocketTeam.Value >= 0 && ball.SocketTeam.Value != MyTeam));

        /// <summary>Pick what to set its mind on next (by weighted chance), and for how long.</summary>
        void PickPlan(NetGame g, Ball ball, PlayerNet carrier, bool withBall, bool live)
        {
            int team = MyTeam;
            bool pre = g.S == GameState.PreBall || MapBuilder.GlassUp;
            bool late = TimeLeft(g) < 150f;
            bool geared = HasWeapon || m_P.ArmorHp.Value > 0;
            bool alone = TeamSize(team) <= 1;
            var cur = Cfg.CurrencyItem;
            int onHand = m_P.Count(cur);
            int wealth = onHand + ChestStock();
            int load = Mathf.Max(220, Mathf.RoundToInt(m_StoreAt));
            int sock = withBall ? ball.SocketTeam.Value : -2;
            bool ballOurs = withBall && carrier == null && sock == team;
            bool ballTheirs = withBall && carrier == null && sock >= 0 && sock != team;
            bool machinesUp = false;
            if (live && Cfg.Bedwars) for (int t = 0; t < Cfg.TeamCount; t++) if (t != team && !g.MachineDown(t)) machinesUp = true;
            bool trouble = Time.time - m_HurtAt < 20f || EnemyNear(Cfg.BaseCenter[team], 40f);
            bool ballLoose = withBall && carrier == null && sock < 0;
            bool carried = withBall && carrier != null && carrier.Team.Value != team;
            // the two halves of a match: behind the wall it's all building (every base shut in before the drop); after it,
            // the ball - gearing up, raiding for it, keeping it
            bool open = !Cfg.Builder && !Enclosed(team);
            Score();
            bool bestBase = s_Base[team] >= EnemyBest(s_Base) - 1f;

            float farm = onHand >= load ? 0f : 1f + (wealth < 300 ? 1f : 0f) + (m_Bent == Bent.Gatherer ? 0.7f : 0f); // (carrying a load already: home with it first)
            float build = (onHand >= 150 ? 2f : wealth >= 400 ? 1.2f : 0.2f) + (m_Bent == Bent.Defender ? 0.6f : 0f) + (pre ? 0.5f : 0f) + (!HasAxe && onHand >= 40 ? 1f : 0f); // (carrying plenty: spend it)
            if (open && onHand >= 60) build += pre ? 3f : 2f; // (not shut in yet: walls before anything)
            float raid = 0f, hunt = 0f, guard = 0f;
            if (live && !pre)
            {
                if (ballTheirs || machinesUp || carried)
                    raid = 1.4f + (m_Bent == Bent.Raider || m_Bent == Bent.Rusher ? 1.2f : 0f) + (late ? 1.5f : 0f) + (geared ? 0.6f : -0.3f) + m_Aggro * 0.5f;
                if (ballLoose)
                    raid = 2f + (m_Bent == Bent.Rusher ? 4f : m_Bent == Bent.Raider ? 1.5f : 0f) + (m_Bent == Bent.Defender && open ? -1.5f : 0f); // (the ball's loose: everyone wants it)
                if (geared && Hp01 > 0.6f)
                    hunt = 0.3f + m_Aggro * 1.0f + (m_Bent == Bent.Raider ? 1.4f : 0f) + (ballOurs ? 0.4f : 0f);
                // minding the base: really only with the ball at home (and a base worth sitting in) or trouble there
                guard = 0.1f + (m_Bent == Bent.Defender ? 0.4f : 0f) + (ballOurs ? 0.6f + (bestBase ? 0.6f : 0f) : 0f) + (trouble ? 1f : 0f) + (Cfg.Bedwars && !alone ? 0.5f : 0f);
                if (alone && !late) guard *= 0.3f; // (on its own: nobody farms for it while it stands about)
                if (late && ballTheirs) raid += 1.5f; // (time's running out with the ball in their machine: everybody goes)
                if (ballOurs && !bestBase && onHand >= 100) build += 1.2f; // (holding the ball in the weaker base: build it up)
                if (ballTheirs && !geared) farm += 0.8f; // (gear up before going in)
            }
            // the Rusher: half a minute before the wall drops, out to the glass by the ball to be first to it
            if (WantsRush(g)) { StartPlan(Plan.Rush, load); return; }
            // plans that came to nothing lately are put off
            if (Time.time < m_PlanOff[(int)Plan.Farm]) farm = 0f;
            if (Time.time < m_PlanOff[(int)Plan.Build]) build = 0f;
            if (Time.time < m_PlanOff[(int)Plan.Raid]) raid = 0f;
            if (Time.time < m_PlanOff[(int)Plan.Hunt]) hunt = 0f;
            if (Time.time < m_PlanOff[(int)Plan.Guard]) guard = 0f;
            farm = Mathf.Max(0f, farm); build = Mathf.Max(0f, build); raid = Mathf.Max(0f, raid); hunt = Mathf.Max(0f, hunt); guard = Mathf.Max(0f, guard);

            float total = farm + build + raid + hunt + guard;
            var pick = onHand >= 150 ? Plan.Build : Plan.Farm; // (nothing on: spend what it carries, or farm)
            if (total > 0f)
            {
                float r = Random.value * total;
                if ((r -= farm) < 0f) pick = Plan.Farm;
                else if ((r -= build) < 0f) pick = Plan.Build;
                else if ((r -= raid) < 0f) pick = Plan.Raid;
                else if ((r -= hunt) < 0f) pick = Plan.Hunt;
                else pick = Plan.Guard;
            }
            StartPlan(pick, load);
        }

        void StartPlan(Plan p, int load)
        {
            m_Plan = p;
            m_PlanIdle = -1f;
            m_PlanStarted = Time.time;
            m_Prepped = false;
            PlansMade++;
            Note($"plan {p}");
            if (PlanLog.Count < 200) PlanLog.Add(p);
            switch (p)
            {
                case Plan.Farm:
                    m_PlanWood = m_P.Count(Cfg.CurrencyItem) + Mathf.RoundToInt(load * Random.Range(0.6f, 1.1f));
                    m_PlanUntil = Time.time + Random.Range(60f, 120f);
                    break;
                case Plan.Build: m_PlanUntil = Time.time + 120f; break; // (it ends sooner, when there's nothing left it can pay for: PlanJob)
                case Plan.Raid: m_PlanUntil = Time.time + Random.Range(60f, 110f); break;
                case Plan.Hunt:
                    m_PlanUntil = Time.time + Random.Range(35f, 75f) * (0.7f + m_Aggro * 0.6f);
                    m_HuntStyle = Random.value < 0.4f ? 0 : m_Aggro > 0.6f ? 1 : 2;
                    break;
                case Plan.Guard: m_PlanUntil = Time.time + Random.Range(25f, 45f); break;
                case Plan.Rush: m_PlanUntil = Time.time + 60f; AtWallAt = -1f; break;
                default: m_PlanUntil = Time.time + 10f; break;
            }
            if (m_Task != Task.Fight && m_Task != Task.Retreat && m_Task != Task.Carry) Set(Task.Idle);
        }

        /// <summary>The plan came to nothing (no trees, nothing it can buy, no ball to raid): drop it and not that one again for a bit.</summary>
        void PlanFailed()
        {
            if (m_Plan != Plan.None) { m_PlanOff[(int)m_Plan] = Time.time + 20f; Note($"plan {m_Plan} came to nothing"); }
            m_Plan = Plan.None;
            m_PlanUntil = 0f;
        }

        /// <summary>The job for the plan it's on (false: nothing to do for it right now - the plan's over).</summary>
        bool PlanJob(NetGame g, Ball ball, PlayerNet carrier, bool withBall, bool live)
        {
            bool homeSide = !MapBuilder.GlassUp || Cfg.RegionOf(transform.position) == Cfg.RegionOf(Cfg.BaseCenter[MyTeam]);
            bool hungry = Hp01 < 0.75f && m_P.Count(Item.Berry) == 0;
            switch (m_Plan)
            {
                case Plan.Farm:
                    // out at the trees till it has its load: a bush when hurt (or one close by: berries are its heals in a
                    // fight - it stocks up), loot right by it, a hatchet first if it's on the rock
                    if (hungry && FindBush()) { Set(Task.Berries); return true; }
                    if (BerryStop(14f, 8)) return true;
                    if (LootJob(18f)) return true;
                    if (!HasAxe && CraftJob(homeSide)) return true;
                    if (FarmJob()) return true;
                    return homeSide && StoreJob(); // (full up before its number: the chest, then back out)
                case Plan.Build:
                    // home, spending: what it made goes down, its tools, a chest, the load into the chest, the team's goals
                    if (hungry && FindBush()) { Set(Task.Berries); return true; }
                    if (LootJob(45f)) return true;
                    if (homeSide && DeployJob()) return true;
                    if (homeSide && ChestJob()) return true; // (a chest before anything else: on a foundation - PickDeploySpot)
                    if (CraftJob(homeSide)) return true;
                    if (homeSide && StoreJob()) return true;
                    return homeSide && GoalJob(g);
                case Plan.Rush:
                    if (live && withBall && carrier == null && ball.SocketTeam.Value < 0) { Set(Task.Fetch); return true; } // (it's down: GO)
                    if (BerryStop(10f, 5)) return true;
                    Set(Task.Rush);
                    return true;
                case Plan.Raid:
                    if (!withBall) return live && Cfg.Bedwars && MachineRaid(g);
                    // going in for the ball in their machine: a weapon, a few berries and a raiding tool first, if it's near home
                    if (carrier == null && ball.SocketTeam.Value >= 0 && ball.SocketTeam.Value != MyTeam && RaidPrep(ball.SocketTeam.Value, homeSide)) return true;
                    if (BerryStop(12f, 4)) return true;
                    if (carrier != null)
                    {
                        if (carrier.Team.Value == MyTeam && carrier != m_P) { Set(Task.Escort); return true; }
                        return false;
                    }
                    if (ball.SocketTeam.Value < 0) { Set(Task.Fetch); return true; }
                    if (ball.SocketTeam.Value == MyTeam) return false;
                    m_RaidTeam = ball.SocketTeam.Value;
                    m_RaidMachine = false;
                    Set(Task.Raid);
                    return true;
                case Plan.Hunt:
                    if (hungry && FindBush()) { Set(Task.Berries); return true; }
                    if (BerryStop(12f, 5)) return true;
                    if (LootJob(30f)) return true;
                    if (m_Task != Task.Hunt) { m_HuntTeam = NearestEnemyTeam(); Set(Task.Hunt); }
                    return true;
                case Plan.Guard:
                    if (homeSide && LootJob(25f)) return true; // (the wood machine's pile, a bag by the door)
                    if (homeSide && StoreJob()) return true;
                    Set(Task.Defend);
                    return true;
            }
            return false;
        }

        /// <summary>Bedwars: the nearest enemy machine still standing - off to smash it (false: none left).</summary>
        bool MachineRaid(NetGame g)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                if (t == MyTeam || g.MachineDown(t)) continue;
                float d = Flat(Cfg.MachinePos(t), transform.position);
                if (d < bd) { bd = d; best = t; }
            }
            if (best < 0) return false;
            m_RaidTeam = best;
            m_RaidMachine = true;
            Set(Task.Raid);
            return true;
        }

        /// <summary>The ball right there, whatever it had in mind: loose and near, go and get it; a teammate running past with
        /// it, run with them (a gatherer only when they're close).</summary>
        bool BallNearJob(Ball ball, PlayerNet carrier)
        {
            if (carrier != null)
            {
                if (carrier.Team.Value == MyTeam && carrier != m_P && Flat(carrier.transform.position, transform.position) < (m_Bent == Bent.Gatherer ? 15f : 35f)) { Set(Task.Escort); return true; }
                return false;
            }
            if (ball.SocketTeam.Value < 0 && Flat(ball.transform.position, transform.position) < (m_Plan == Plan.Farm ? 30f : 50f)) { Set(Task.Fetch); return true; }
            return false;
        }
    }
}
