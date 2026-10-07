using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot's thinking (a few times a second, BotBrain.cs): one job, picked by what matters most right now.
    /// The ball in its hands beats everything; then an enemy running off with the ball; then its own skin (badly hurt
    /// with someone after it: back home to heal and eat); then a fight with an enemy it can see near enough (how near
    /// depends on how aggressive it is - and a player in tree camo is just a tree to it); then the ball (fetch it when
    /// it's loose, escort a teammate carrying it, raid the base it's in, defend it at home - by its leaning and how much
    /// time is left); and then the economy - berries when hurt, floor loot and airdrops, putting down what it made, its
    /// starter tools, the team chest, stashing its load, the team's goals (a bigger house, better weapons, more crafting
    /// unlocked, more upgrades: BotBrain.Goals.cs), and otherwise farming trees - with a wander now and then.
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

            // 1. the ball in its hands: home with it, nothing else matters
            if (m_P.CarryingBall) { Set(Task.Carry); return; }

            // 2. an enemy has the ball: after them (from most of the map away)
            bool hurt = Hp01 < m_RetreatAt;
            if (carrier != null && carrier.Team.Value != MyTeam && Hostile(carrier) && Flat(carrier.transform.position, transform.position) < 90f && !hurt)
            {
                m_Target = carrier;
                Set(Task.Fight);
                return;
            }

            // 3. who's about (sudden death: anyone on the platform)
            float range = sudden ? 500f : 10f + 14f * m_Aggro + (m_Task == Task.Defend ? 8f : 0f);
            var foe = PickTarget(range, sudden);

            // 4. badly hurt with someone stronger after it: back off home (healing in base, eating on the way)
            if (!sudden && foe != null && hurt && foe.Health.Value > hp * 0.8f) { m_Target = foe; Set(Task.Retreat); return; }
            if (!sudden && m_Task == Task.Retreat && Hp01 < 0.7f && foe != null) return;

            // 5. a fight
            if (foe != null) { m_Target = foe; Set(Task.Fight); return; }
            m_Target = null;
            if (sudden) { GoTo(Cfg.ArenaCenter, 2f, false); Set(Task.Hunt); return; }

            // 6. a wander it's on (a little look about), unless there's something for it to do with the ball
            if (m_Task == Task.Wander && Time.time < m_TaskUntil && !(withBall && ball.SocketTeam.Value < 0 && !ball.IsCarried)) return;

            // 7. the ball (Bedwars: the machines)
            if (withBall && BallJob(g, ball, carrier)) return;
            if (live && Cfg.Bedwars && MachineJob(g)) return;

            // 8. the economy, and a wander when there's nothing to do
            if (EconomyJob(g)) return;
            StartWander(8f);
        }

        void Set(Task t)
        {
            if (m_Task == t) return;
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

        // ---------------------------------------------------------------- the ball

        /// <summary>The ball's in play: what this bot does about it (false: nothing - get on with the economy).</summary>
        bool BallJob(NetGame g, Ball ball, PlayerNet carrier)
        {
            int team = MyTeam;
            bool late = TimeLeft(g) < 150f;
            bool geared = HasWeapon || m_P.ArmorHp.Value > 0;
            bool alone = TeamSize(team) <= 1;
            if (carrier != null)
            {
                // a teammate has it: raiders and defenders run with them (the others keep the economy going)
                if (carrier.Team.Value == team && carrier != m_P && (m_Bent != Bent.Gatherer || late)) { Set(Task.Escort); return true; }
                return false;
            }
            int sock = ball.SocketTeam.Value;
            if (sock < 0)
            {
                // loose: go and get it (a defender only if it's not far from home - someone has to mind the base)
                float d = Flat(ball.transform.position, transform.position);
                if (m_Bent != Bent.Defender || alone || d < 45f || late) { Set(Task.Fetch); return true; }
                return false;
            }
            if (sock == team)
            {
                // ours: defenders hold the base (everyone does if enemies are in it); raiders go hunting
                if (m_Bent == Bent.Defender || EnemyNear(Cfg.BaseCenter[team], 28f)) { Set(Task.Defend); return true; }
                if (m_Bent == Bent.Raider && geared && Hp01 > 0.6f) { m_HuntTeam = NearestEnemyTeam(); Set(Task.Hunt); return true; }
                return false;
            }
            // in an enemy machine: raid for it (when it's ready for it - or time's running out)
            bool go = m_Bent == Bent.Raider || alone ? Hp01 > 0.5f && (geared || late || m_Bent == Bent.Raider)
                    : m_Bent == Bent.Gatherer ? (geared || late) && Hp01 > 0.6f
                    : late && Hp01 > 0.6f;
            if (!go) return false;
            m_RaidTeam = sock;
            m_RaidMachine = false;
            Set(Task.Raid);
            return true;
        }

        /// <summary>Bedwars: defenders guard the machine, the rest go and smash an enemy one when they're ready.</summary>
        bool MachineJob(NetGame g)
        {
            bool alone = TeamSize(MyTeam) <= 1;
            if (m_Bent == Bent.Defender && !alone) { Set(Task.Defend); return true; }
            bool geared = HasWeapon || m_P.ArmorHp.Value > 0;
            if (!geared && m_Bent != Bent.Raider && TimeLeft(g) > 150f) return false;
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

        // ---------------------------------------------------------------- the economy

        bool EconomyJob(NetGame g)
        {
            bool homeSide = !MapBuilder.GlassUp || Cfg.RegionOf(transform.position) == Cfg.RegionOf(Cfg.BaseCenter[MyTeam]);
            // hurt and out of berries: a bush (the eating itself happens on the way: TickEat)
            if (Hp01 < 0.75f && m_P.Count(Item.Berry) == 0 && FindBush()) { Set(Task.Berries); return true; }
            if (LootJob()) return true;                     // floor loot, death bags, airdrops (BotBrain.Loot.cs)
            if (homeSide && DeployJob()) return true;       // what it made: a Trade Station, a chest, a bag, a turret
            if (CraftJob(homeSide)) return true;            // the basics first: a hatchet, a spear
            if (homeSide && ChestJob()) return true;        // somewhere to keep it all
            if (homeSide && StoreJob()) return true;        // a good load on it (or junk): into the chest
            if (homeSide && GoalJob(g)) return true;        // whatever the team's most behind on (BotBrain.Goals.cs)
            // now and then just a look round, like a person
            if (Random.value < 0.02f * m_Playful) { StartWander(6f); return true; }
            // and otherwise it farms - always, well past what it needs (the spare goes in the chest)
            if (FarmJob()) return true;
            return homeSide && StoreJob(); // (full up)
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

        static float TimeLeft(NetGame g) => NetworkManager.Singleton != null ? (float)(g.PhaseEnd.Value - NetworkManager.Singleton.ServerTime.Time) : 999f;
    }
}
