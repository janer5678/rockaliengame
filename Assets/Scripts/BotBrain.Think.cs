using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot's thinking (a few times a second, BotBrain.cs): one job, picked by what matters most right now.
    /// The ball in its hands beats everything; then an enemy running off with the ball; then its own skin (badly hurt
    /// with someone after it: back home to heal and eat); then a fight with an enemy it can see near enough (how near
    /// depends on how aggressive it is and what it's about - and a player in tree camo is just a tree to it); then the
    /// ball when it's right there (loose and near, a teammate running past with it) and enemies in its base; and then
    /// whatever it has set its mind on for this stretch of the match (BotBrain.Plans.cs: farming till it has a load,
    /// building up at home, raiding for the ball, hunting, guarding) - with a wander now and then.
    /// </summary>
    public partial class BotBrain
    {
        PlayerNet m_Target;
        float m_HurtAt = -10f, m_LastHp = -1f;
        int m_RaidTeam = -1, m_HuntTeam = -1;
        bool m_RaidMachine;

        void Think(NetGame g, bool lobby)
        {
            m_Strafe = false;
            m_HasLook = false;
            if (lobby) { Lounge(); return; }
            float hp = m_P.Health.Value;
            if (m_LastHp >= 0f && hp < m_LastHp - 0.5f) m_HurtAt = Time.time;
            m_LastHp = hp;

            var ball = Ball.Instance;
            bool live = g.S == GameState.BallLive;
            bool sudden = g.S == GameState.SuddenDeath;
            bool withBall = live && !Cfg.NoBall && ball != null && ball.IsSpawned;
            var carrier = withBall ? ball.Carrier : null;

            // its team's mind: the stance, the siege, what they've all seen (BotBrain.Team.cs)
            var mind = TeamMind(g, ball, carrier, withBall, live);
            TalkTick();

            // 1. the ball in its hands: home with it, nothing else matters
            if (m_P.CarryingBall) { Set(Task.Carry); return; }

            // 2. an enemy has the ball: after them (from anywhere on the map - cutting them off on their way home: TickFight)
            bool hurt = Hp01 < m_RetreatAt;
            if (carrier != null && carrier.Team.Value != MyTeam && Hostile(carrier) && Flat(carrier.transform.position, transform.position) < 400f && !hurt)
            {
                m_Target = carrier;
                Set(Task.Fight);
                return;
            }

            // 2b. a door just opened in the base it is raiding: after whoever opened it, through it (even out of sight)
            if (Rushing && !hurt) { m_Target = m_Opener; Set(Task.Fight); return; }
            if (m_Plan == Plan.Raid && m_RaidTeam >= 0 && m_RaidTeam < Cfg.TeamCount && withBall && ball.SocketTeam.Value == m_RaidTeam && Flat(transform.position, Cfg.BaseCenter[m_RaidTeam]) < 40f) WatchDoors(m_RaidTeam, ball.transform.position);

            // 3. who's about (sudden death: anyone on the platform) - a hunter or a guard looks further, a farmer keeps its head down
            float range = sudden ? 500f : 16f + 16f * m_Aggro + (m_Task == Task.Defend || m_Plan == Plan.Hunt || m_Plan == Plan.Guard || m_Plan == Plan.Raid ? 10f : 0f) - (m_Plan == Plan.Farm ? 4f : 0f);
            var foe = PickTarget(range, sudden);

            // 4. badly hurt with someone stronger after it: back off home (healing in base, eating on the way)
            if (!sudden && foe != null && hurt && foe.Health.Value > hp * 0.8f) { m_Target = foe; m_RetreatSeen = Time.time; if (m_Task != Task.Retreat) Say("Hurt - falling back!", "retreat" + m_P.NetworkObjectId, 30f); Set(Task.Retreat); return; }
            // (a retreat sticks: till it has healed up, or a few seconds after it last saw anyone after it - no turning back the moment they drop out of sight)
            if (!sudden && m_Task == Task.Retreat && Hp01 < 0.7f && (foe != null || Time.time - m_RetreatSeen < 6f)) { if (foe != null) { m_Target = foe; m_RetreatSeen = Time.time; } return; }

            // 5. a fight
            if (foe != null) { m_Target = foe; Set(Task.Fight); return; }
            m_Target = null;
            if (sudden) { GoTo(Cfg.ArenaCenter, 2f, false); Set(Task.Hunt); return; }

            // 6. a wander it's on (a little look about), unless there's something for it to do with the ball
            if (m_Task == Task.Wander && Time.time < m_TaskUntil && !(withBall && ball.SocketTeam.Value < 0 && !ball.IsCarried)) return;

            // 7. the ball right there (anyone would), and enemies in its base when it's about
            if (withBall && BallNearJob(ball, carrier)) return;
            var home = Cfg.BaseCenter[MyTeam];
            if (live && !Cfg.Builder && m_Plan != Plan.Raid && m_Plan != Plan.Hunt && Flat(transform.position, home) < 45f && EnemyNear(home, 28f)) { Set(Task.Defend); return; }
            // the base's alarm (a piece being hit, an enemy seen in it): back to deal with it, from a good way off - unless
            // it's in the middle of a raid of its own, or someone else on the team is already on it
            if (live && !Cfg.Builder && (mind.Stance == Stance.Hold || mind.Stance == Stance.Prepare || mind.Stance == Stance.Develop) && Time.time - mind.AlarmAt < 8f
                && Flat(transform.position, home) < 150f && !MateOnAlarm(mind))
            { Set(Task.Defend); return; }

            // an airdrop down: the team's pick goes for it (BotBrain.Team.cs: PickDrop)
            if (mind.DropBot == m_P && mind.Drop != null && mind.Drop.IsSpawned && !mind.Drop.Empty && !Avoided(mind.Drop))
            {
                if (m_LootBox != mind.Drop) { m_LootBox = mind.Drop; m_LootId = -1; Note("off to an airdrop"); AirdropsRun++; Say("Getting the airdrop.", "drop", 30f); }
                Set(Task.Loot);
                return;
            }

            // a human teammate said "follow", or a teammate is being hit and it is the one going to help (BotBrain.Talk.cs)
            if (BuddyJob(mind)) return;

            // 8. what it has set its mind on (BotBrain.Plans.cs): a new plan when the last ran out or is done, or the team's
            // stance changed (a Rusher drops whatever it's doing half a minute before the wall goes)
            if (WantsRush(g) && m_Plan != Plan.Rush) StartPlan(Plan.Rush, 0);
            bool newStance = mind.Stance != m_PlanStance;
            m_PlanStance = mind.Stance;
            if (m_Plan == Plan.None || Time.time >= m_PlanUntil || newStance || PlanDone(g, ball, carrier, withBall, live)) PickPlan(g, ball, carrier, withBall, live);
            if (PlanJob(g, ball, carrier, withBall, live)) { m_PlanIdle = -1f; return; }
            // nothing in the plan for it just now: a few seconds' grace (a piece not held up yet, a craft that didn't go through) before it's dropped
            if (m_PlanIdle < 0f) m_PlanIdle = Time.time;
            else if (Time.time - m_PlanIdle > 4f)
            {
                // nothing in it: straight on to something else (no standing about in between)
                PlanFailed();
                PickPlan(g, ball, carrier, withBall, live);
                if (PlanJob(g, ball, carrier, withBall, live)) { m_PlanIdle = -1f; return; }
            }

            // 9. nothing in that for it right now: the chest if it's loaded, the trees, or a look about
            bool homeSide = !MapBuilder.GlassUp || Cfg.RegionOf(transform.position) == Cfg.RegionOf(home);
            if (homeSide && StoreJob()) return;
            if (FarmJob()) return;
            StartWander(8f);
        }

        Stance m_PlanStance;
        float m_RetreatSeen = -10f;
        /// <summary>(tests) airdrops it has gone for.</summary>
        public int AirdropsRun { get; private set; }

        /// <summary>Someone else on its team (a human, or a bot already defending) is nearer the alarm than it is.</summary>
        bool MateOnAlarm(Mind m)
        {
            float mine = Flat(transform.position, m.AlarmPos);
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p == m_P || !p.IsSpawned || p.Dead.Value || p.Team.Value != MyTeam) continue;
                if (Flat(p.transform.position, m.AlarmPos) >= mine - 5f) continue;
                if (!p.Bot.Value) return true;
                var b = p.GetComponent<BotBrain>();
                if (b != null && (b.m_Task == Task.Defend || b.m_Task == Task.Fight)) return true;
            }
            return false;
        }

        void Set(Task t)
        {
            if (m_Task == t) return;
            Note($"{m_Plan}: {t}");
            m_Task = t;
            m_Stuck = 0;
            m_TaskUntil = 0f;
        }

        /// <summary>The waiting stadium / ship lobby: just amble about (no fighting, no anything).</summary>
        void Lounge()
        {
            m_Task = Task.Lounge;
            m_Target = null;
            if ((m_Goal - transform.position).sqrMagnitude < 4f || Flat(m_Goal, Cfg.ArenaCenter) > 12f || Time.time > m_TaskUntil)
            {
                m_Goal = Safe(Cfg.ArenaCenter + new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f)));
                m_TaskUntil = Time.time + Random.Range(4f, 10f);
            }
            m_Stop = 0.6f;
            m_Sprint = false;
        }

        /// <summary>A short look about: somewhere nearby, walking or jogging.</summary>
        void StartWander(float seconds)
        {
            Set(Task.Wander);
            m_TaskUntil = Time.time + seconds * Random.Range(0.5f, 1f);
            var pos = transform.position;
            // drift a little towards home rather than away from it, unless it's a raider
            var home = Cfg.BaseCenter[MyTeam];
            var c = m_Bent == Bent.Raider ? pos : Vector3.Lerp(pos, home, 0.2f);
            GoTo(Around(c, 22f), 1.2f, Random.value < 0.5f * m_Playful);
        }

        // ---------------------------------------------------------------- who to fight

        /// <summary>
        /// An enemy it would go for: alive, not behind the glass, not invisible - and not in tree camo (to a bot that's
        /// just a tree). Seen in range (or anyone, in sudden death); the weak, the near and whoever has the ball first.
        /// Just hit, it looks a bit further for whoever did it.
        /// </summary>
        PlayerNet PickTarget(float range, bool anyRange)
        {
            PlayerNet best = null;
            float bs = float.MaxValue;
            var pos = transform.position;
            bool hurtNow = Time.time - m_HurtAt < 3f;
            foreach (var p in PlayerNet.All)
            {
                if (!Hostile(p)) continue;
                float d = Flat(p.transform.position, pos);
                float r = range;
                if (hurtNow) r = Mathf.Max(r, 16f);
                if (p == m_Target) r *= 1.4f; // (it doesn't give up on someone the moment they step back)
                if (!anyRange && (d > r || (d > 3f && !CanSee(p)))) continue;
                // not after someone back in their own base (its turrets, its walls, their mates) unless it's raiding it
                if (!anyRange && d > 8f && m_Plan != Plan.Raid && !p.CarryingBall && Cfg.BaseTeamAt(p.transform.position) == p.Team.Value) continue;
                float score = d - (Cfg.MaxHealth - p.Health.Value) * 0.08f * m_Aggro - (p == m_Target ? 3f : 0f) - (p.CarryingBall ? 15f : 0f);
                if (score < bs) { bs = score; best = p; }
            }
            return best;
        }

        /// <summary>Someone it may fight: an enemy, alive, on this side of the glass, not invisible and not disguised as a tree.</summary>
        bool Hostile(PlayerNet p) =>
            p != null && p != m_P && p.IsSpawned && !p.Dead.Value && p.Team.Value != MyTeam && !p.Hidden && !p.TreeCamo
            && !PlayerNet.GlassBetween(transform.position, p.transform.position);

        /// <summary>A clear line from its eyes to them.</summary>
        bool CanSee(PlayerNet p)
        {
            var to = p.transform.position + Vector3.up * 1.2f;
            if (!Physics.Linecast(m_P.EyePos, to, out var hit, Mask, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.transform.IsChildOf(p.transform);
        }

        bool EnemyNear(Vector3 at, float range)
        {
            foreach (var p in PlayerNet.All)
                if (Hostile(p) && Flat(p.transform.position, at) < range) return true;
            return false;
        }

        int NearestEnemyTeam()
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                if (t == MyTeam) continue;
                float d = Flat(Cfg.BaseCenter[t], transform.position);
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        static int TeamSize(int team)
        {
            int n = 0;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.Team.Value == team) n++;
            return n;
        }

        static float TimeLeft(NetGame g) => NetworkManager.Singleton != null && !g.NoClock ? (float)(g.PhaseEnd.Value - NetworkManager.Singleton.ServerTime.Time) * SimScale : 999f;
        /// <summary>(tests: AutoTest.BotSim) how much faster than real time the match is played (its phases cut to match).</summary>
        public static float SimScale = 1f;
    }
}
