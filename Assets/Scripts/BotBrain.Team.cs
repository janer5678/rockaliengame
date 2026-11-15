using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot team's shared mind (BotBrain.cs): the bots on a team play as a team, the way a good squad does, instead of
    /// each one doing its own thing -
    ///   Stance:  what the team's about right now, from where the ball is and how close the enemy is to winning with it
    ///            (Classic / Progress: how full their bar is; a clocked mode: how little time is left) -
    ///            Develop (behind the wall: farm and build), Contest (the ball's loose: everyone goes), Hold (the ball's in
    ///            our machine: one anchor minds the base, the rest rove out and cut off raiders on their way in), Escort
    ///            (one of us carries it), Intercept (one of them carries it: cut them off before they get home), Prepare
    ///            (it's in their machine: gear up - weapon, armour, a ram or ladder - and harass) and Siege (go in for it).
    ///   Siege:   the raiders meet at a rally point off the enemy base first and go in together (no more one at a time
    ///            into two defenders); after a wipe they regroup again. A human teammate already in there starts it at once.
    ///   Intel:   enemies any of them has seen lately (hunters go where they were last seen) and an alarm when the base is
    ///            hit or an enemy is seen in it (whoever's near enough comes back to deal with it).
    ///   Roles:   the anchor (minds the base when it needs minding - not when a human teammate's at home) and the
    ///            collector (the one who empties the wood machine's pile, so they don't all stand at it).
    ///   Style:   each team plays one way this match, dealt from the map seed - Balanced, Aggro (sieges early, no anchor
    ///            while a human's home), Turtle (gears up fully, sits on the ball) or Sneaky (rallies round the side or the
    ///            back of the enemy base) - so matches play out differently.
    /// Personalities are dealt per team too (BotBrain.InitPersonality), so two bots on a team never have the same one.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>What a bot team is about right now (tests can read it: TeamStance).</summary>
        public enum Stance : byte { Develop, Contest, Hold, Escort, Intercept, Prepare, Siege }
        /// <summary>How a bot team plays this match (TeamStyleOf).</summary>
        public enum TeamStyle : byte { Balanced, Aggro, Turtle, Sneaky }

        class Mind
        {
            public float At = -10f;
            public Stance Stance;
            public float StanceSince;
            public readonly Dictionary<PlayerNet, (Vector3 pos, float at)> Seen = new Dictionary<PlayerNet, (Vector3, float)>();
            public float AlarmAt = -100f;
            public Vector3 AlarmPos;
            public Vector3 Rally;
            public float RallyAt = -100f, GoAt = -100f;
            public bool Go;
            public int RaidTeam = -1;
            public PlayerNet Anchor, Collector;
            public int Sieges, Wipes;
            public Vector3 Breach;
            public float BreachAt = -100f;
            public int BreachTeam = -1;
            public Container Drop;
            public PlayerNet DropBot;
            // a human teammate's orders (BotBrain.Talk.cs), and a teammate being hit
            public PlayerNet Follow, HelpOf, HelpBot;
            public float FollowUntil, OrderUntil, OrderAt = -100f, HelpAt = -100f;
            public Plan Order;
            public Vector3 HelpPos;
        }

        static readonly Mind[] s_Minds = new Mind[4];
        static NetGame s_MindsFor;
        /// <summary>(tests) force every team's style (-1: dealt by the map seed).</summary>
        public static int TestTeamStyle = -1;

        static Mind MindOf(int team)
        {
            var g = NetGame.Instance;
            if (s_MindsFor != g)
            {
                // a new match: everyone starts over
                s_MindsFor = g;
                for (int t = 0; t < 4; t++) s_Minds[t] = new Mind();
                Structure.ServerHit -= OnPieceHit;
                Structure.ServerHit += OnPieceHit;
            }
            return s_Minds[Mathf.Clamp(team, 0, 3)];
        }

        /// <summary>How `team` plays this match.</summary>
        public static TeamStyle TeamStyleOf(int team)
        {
            if (TestTeamStyle >= 0) return (TeamStyle)(TestTeamStyle & 3);
            var r = new System.Random(Cfg.MapSeed * 31 + team * 7919 ^ 0x2545f491);
            return (TeamStyle)r.Next(4);
        }

        /// <summary>(tests, spectating) a team's stance, the siege's state, sieges started and raids wiped.</summary>
        public static Stance TeamStance(int team) => MindOf(team).Stance;
        public static bool SiegeGo(int team) => MindOf(team).Go;
        public static int Sieges(int team) => MindOf(team).Sieges;

        /// <summary>A piece was hit (server): one in its own team's base during the match sounds that team's alarm.</summary>
        static void OnPieceHit(Structure s)
        {
            var g = NetGame.Instance;
            if (s == null || g == null || g.S != GameState.BallLive) return;
            int t = s.Team.Value;
            if (t < 0 || t > 3 || Cfg.BaseTeamAt(s.transform.position) != t) return;
            var m = MindOf(t);
            m.AlarmAt = Time.time;
            m.AlarmPos = s.transform.position;
        }

        /// <summary>How close an enemy team is to winning with the ball in its machine: 0 not at all .. 1 any second now.</summary>
        static float Danger(NetGame g, int team)
        {
            var b = Ball.Instance;
            if (g == null || b == null || !b.IsSpawned || b.IsCarried) return 0f;
            int s = b.SocketTeam.Value;
            if (s < 0 || s == team) return 0f;
            if (Cfg.ProgressMode && !g.SuddenBall.Value) return Mathf.Clamp01(0.2f + g.ProgressOf(s));
            return 1f - Mathf.Clamp01(TimeLeft(g) / 600f);
        }

        /// <summary>Its team's mind, brought up to date (twice a second, whichever of them thinks first).</summary>
        Mind TeamMind(NetGame g, Ball ball, PlayerNet carrier, bool withBall, bool live)
        {
            int team = MyTeam;
            var m = MindOf(team);
            if (Time.time - m.At < 0.5f) return m;
            m.At = Time.time;
            var style = TeamStyleOf(team);
            bool pre = g.S == GameState.PreBall || MapBuilder.GlassUp;
            int sock = withBall ? ball.SocketTeam.Value : -2;
            Stance want;
            if (!live || pre || !withBall || Cfg.Builder) want = Stance.Develop;
            else if (carrier != null) want = carrier.Team.Value == team ? Stance.Escort : Stance.Intercept;
            else if (sock < 0) want = Stance.Contest;
            else if (sock == team) want = Stance.Hold;
            else
            {
                // their machine: gear up first, then go in - sooner the closer they are to winning, and the bolder the team
                float danger = Danger(g, team);
                float prep = style == TeamStyle.Aggro ? 18f : style == TeamStyle.Turtle ? 75f : style == TeamStyle.Sneaky ? 40f : 45f;
                float risk = style == TeamStyle.Aggro ? 0.3f : style == TeamStyle.Turtle ? 0.6f : 0.45f;
                bool ready = TeamGeared(team);
                if (m.Stance == Stance.Siege) want = Stance.Siege;
                else if (m.Stance != Stance.Prepare) want = ready || danger > risk ? Stance.Siege : Stance.Prepare;
                else want = ready || danger > risk || Time.time - m.StanceSince > prep ? Stance.Siege : Stance.Prepare;
                if (want == Stance.Siege && m.RaidTeam != sock) { m.RaidTeam = sock; if (m.Stance == Stance.Siege) StartRally(m, team, sock, style); }
            }
            if (want != m.Stance)
            {
                m.Stance = want;
                m.StanceSince = Time.time;
                if (want == Stance.Siege) { m.RaidTeam = sock; StartRally(m, team, sock, style); m.Sieges++; }
                StanceCallout(m, team, carrier);
            }
            HelpTick(m, team);
            if (m.Stance == Stance.Siege) SiegeTick(g, m, team);
            PickRoles(m, team, style);
            PickDrop(m, team, live && !pre);
            // what any of them can see (and the base's alarm when an enemy's in it)
            float now = Time.time;
            var home = Cfg.BaseCenter[team];
            foreach (var p in PlayerNet.All)
            {
                if (!Hostile(p)) continue;
                var pp = p.transform.position;
                foreach (var q in PlayerNet.All)
                {
                    if (q == null || !q.IsSpawned || q.Dead.Value || q.Team.Value != team) continue;
                    float d = Flat(q.transform.position, pp);
                    if (d < 20f || (d < 75f && SeesFrom(q, p)) || p.CarryingBall) { m.Seen[p] = (pp, now); break; }
                }
                if (m.Seen.TryGetValue(p, out var s) && now - s.at < 1f && Flat(pp, home) < 3f * OuterR(team) + 10f && live && !pre)
                {
                    if (now - m.AlarmAt > 10f) TeamSay(team, $"{p.DisplayName} is in our base!", "alarm", 25f);
                    m.AlarmAt = now;
                    m.AlarmPos = pp;
                }
            }
            return m;
        }

        /// <summary>The raiders' meeting point: off the enemy base on the way from home - round the side or the back for a
        /// Sneaky team - out of sight of the door.</summary>
        static void StartRally(Mind m, int team, int enemy, TeamStyle style)
        {
            if (enemy < 0 || enemy >= Cfg.TeamCount) return;
            var eb = Cfg.BaseCenter[enemy];
            var toUs = Cfg.BaseCenter[team] - eb; toUs.y = 0f;
            toUs = toUs.sqrMagnitude > 0.01f ? toUs.normalized : -Cfg.BackDir(enemy);
            float ang = style == TeamStyle.Sneaky ? Random.Range(60f, 110f) : Random.Range(15f, 35f);
            if (Random.value < 0.5f) ang = -ang;
            var p = eb + Quaternion.Euler(0f, ang, 0f) * toUs * (Random.Range(40f, 50f) + 3f * OuterR(enemy));
            float lim = Cfg.MapHalf - 10f;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            p.y = MapBuilder.Height(p.x, p.z);
            m.Rally = p;
            m.RallyAt = Time.time;
            m.Go = false;
        }

        /// <summary>The siege: GO once every raider's at the rally (or it has waited long enough, or there's no time to
        /// wait, or a human teammate's already in there); after a wipe (nobody left near their base) regroup again.</summary>
        void SiegeTick(NetGame g, Mind m, int team)
        {
            int enemy = m.RaidTeam;
            if (enemy < 0 || enemy >= Cfg.TeamCount) return;
            var eb = Cfg.BaseCenter[enemy];
            float near = 3f * OuterR(enemy) + 22f;
            int raiders = 0, there = 0, close = 0;
            bool humanIn = false;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value || p.Team.Value != team) continue;
                var pos = p.transform.position;
                if (Flat(pos, eb) < near) close++;
                if (!p.Bot.Value) { if (Flat(pos, eb) < near + 10f) humanIn = true; continue; }
                var b = p.GetComponent<BotBrain>();
                if (b == null || b.m_Plan != Plan.Raid) continue;
                raiders++;
                if (Flat(pos, m.Rally) < 14f || Flat(pos, eb) < near) there++;
            }
            if (!m.Go)
            {
                float wait = TeamStyleOf(team) == TeamStyle.Aggro ? 14f : 28f;
                bool atRally = there > 0 && Time.time - m.RallyAt > 3f;
                if ((raiders > 0 && there >= raiders) || humanIn || Danger(g, team) > 0.85f || (atRally && Time.time - m.RallyAt > wait))
                {
                    m.Go = true;
                    m.GoAt = Time.time;
                    TeamSay(team, humanIn ? "You're in? Right behind you - GO!" : "Everyone here - GO GO GO!", "go", 15f);
                }
            }
            else if (Time.time - m.GoAt > 15f && close == 0)
            {
                // nobody left in there (dead, or beaten back): regroup and try again - geared up first, unless there's no time
                m.Wipes++;
                StartRally(m, team, enemy, TeamStyleOf(team));
                if (Danger(g, team) < 0.75f) { m.Stance = Stance.Prepare; m.StanceSince = Time.time; TeamSay(team, "They held us off. Gearing up - next time we bring more.", "wipe", 30f); }
                else TeamSay(team, "No time - regroup and go again!", "wipe", 30f);
            }
        }

        /// <summary>Who minds the base and who empties the wood machine.</summary>
        static void PickRoles(Mind m, int team, TeamStyle style)
        {
            var home = Cfg.BaseCenter[team];
            PlayerNet anchor = null, nearest = null;
            float nd = float.MaxValue;
            bool humanHome = false;
            int bots = 0;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Team.Value != team) continue;
                if (!p.Bot.Value) { if (!p.Dead.Value && Flat(p.transform.position, home) < 40f) humanHome = true; continue; }
                bots++;
                var b = p.GetComponent<BotBrain>();
                if (b != null && b.m_Bent == Bent.Defender && anchor == null) anchor = p;
                float d = p.Dead.Value ? 999f : Flat(p.transform.position, home);
                if (d < nd) { nd = d; nearest = p; }
            }
            // the old anchor keeps the job (no swapping every second)
            if (m.Anchor != null && m.Anchor.IsSpawned && m.Anchor.Team.Value == team) anchor = m.Anchor;
            if (anchor == null) anchor = nearest;
            // a human at home minds it: the bots go out (a Turtle team keeps its anchor anyway if it has two)
            if (humanHome && !(style == TeamStyle.Turtle && bots >= 2)) anchor = null;
            if (style == TeamStyle.Aggro && bots < 2) anchor = null;
            m.Anchor = anchor;
            m.Collector = anchor != null ? anchor : nearest;
        }

        /// <summary>An airdrop down with something in it (not in an enemy base): the bot nearest it that can be spared goes
        /// for it - not the anchor while the ball's at home, not a carrier or someone already in a siege.</summary>
        static void PickDrop(Mind m, int team, bool live)
        {
            m.Drop = null;
            m.DropBot = null;
            if (!live) return;
            Container best = null;
            PlayerNet who = null;
            float bd = 170f;
            foreach (var c in Container.All)
            {
                if (c == null || !c.IsSpawned || !c.IsAirdrop || c.Empty) continue;
                var cp = c.transform.position;
                int bt = Cfg.BaseTeamAt(cp);
                if (bt >= 0 && bt != team) continue;
                float lim = Cfg.MapHalf - 6f;
                if (Mathf.Abs(cp.x) > lim || Mathf.Abs(cp.z) > lim) continue;
                foreach (var p in PlayerNet.All)
                {
                    if (p == null || !p.IsSpawned || !p.Bot.Value || p.Dead.Value || p.Team.Value != team || p.CarryingBall) continue;
                    if (p == m.Anchor && m.Stance == Stance.Hold) continue;
                    var b = p.GetComponent<BotBrain>();
                    if (b == null || (m.Stance == Stance.Siege && m.Go && b.m_Plan == Plan.Raid) || b.Avoided(c)) continue;
                    float d = Flat(p.transform.position, cp);
                    if (d < bd) { bd = d; best = c; who = p; }
                }
            }
            m.Drop = best;
            m.DropBot = who;
        }

        /// <summary>Every bot on the team alive has a real weapon and armour on (or can't get any).</summary>
        static bool TeamGeared(int team)
        {
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || !p.Bot.Value || p.Dead.Value || p.Team.Value != team) continue;
                var b = p.GetComponent<BotBrain>();
                if (b == null) continue;
                if (!b.HasWeapon && b.Craftable(Item.Spear, out _, out _)) return false;
                if (p.ArmorHp.Value <= 0 && !b.Has(Item.Armor) && b.Craftable(Item.Armor, out _, out _)) return false;
            }
            return true;
        }

        static bool SeesFrom(PlayerNet q, PlayerNet p)
        {
            var to = p.transform.position + Vector3.up * 1.2f;
            if (!Physics.Linecast(q.EyePos, to, out var hit, Mask, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.transform.IsChildOf(p.transform);
        }

        /// <summary>The freshest sighting of an enemy out of their own base (no older than maxAge s): where to hunt.</summary>
        bool FreshFoe(float maxAge, out Vector3 at)
        {
            at = default;
            var m = MindOf(MyTeam);
            float best = float.MaxValue;
            foreach (var kv in m.Seen)
            {
                var p = kv.Key;
                if (p == null || !p.IsSpawned || p.Dead.Value || Time.time - kv.Value.at > maxAge) continue;
                if (Cfg.BaseTeamAt(kv.Value.pos) == p.Team.Value) continue; // (in their own base: not hunting there)
                if (Time.time - kv.Value.at > 2f && Flat(kv.Value.pos, transform.position) < 5f) continue; // (been there: they've gone)
                float s = Flat(kv.Value.pos, transform.position) + (Time.time - kv.Value.at) * 3f;
                if (s < best) { best = s; at = kv.Value.pos; }
            }
            return best < float.MaxValue;
        }

        bool IsAnchor => MindOf(MyTeam).Anchor == m_P;
        bool IsCollector { get { var c = MindOf(MyTeam).Collector; return c == null || c == m_P; } }

        /// <summary>The plan the team's stance asks of it (false: no say - its own choice, PickPlan's weights).</summary>
        bool StancePlan(Mind m, NetGame g, int load)
        {
            if (Cfg.Builder || Cfg.Bedwars) return false;
            var style = TeamStyleOf(MyTeam);
            bool geared = HasWeapon || m_P.ArmorHp.Value > 0;
            // (on the right plan already: it just carries on with it - no starting a raid over half way through)
            var want = m.Stance == Stance.Siege || m.Stance == Stance.Contest || m.Stance == Stance.Escort ? Plan.Raid
                : m.Stance == Stance.Intercept ? Plan.Hunt : m.Stance == Stance.Hold && IsAnchor ? Plan.Guard : Plan.None;
            if (want != Plan.None && m_Plan == want && Time.time < m_PlanUntil + 30f) { m_PlanUntil = Mathf.Max(m_PlanUntil, Time.time + 20f); if (want == Plan.Hunt) m_HuntStyle = 3; return true; }
            switch (m.Stance)
            {
                case Stance.Siege:
                case Stance.Contest:
                case Stance.Escort:
                    StartPlan(Plan.Raid, load);
                    return true;
                case Stance.Intercept:
                    StartPlan(Plan.Hunt, load);
                    m_HuntStyle = 3;
                    return true;
                case Stance.Hold:
                    if (IsAnchor) { StartPlan(Plan.Guard, load); return true; }
                    // the rest rove: out to cut raiders off on their way in (or wait off the enemy base), once armed
                    if (!geared || (m_P.Count(Cfg.CurrencyItem) >= 150 && Random.value < 0.35f)) return false;
                    StartPlan(Plan.Hunt, load);
                    m_HuntStyle = style == TeamStyle.Aggro || style == TeamStyle.Sneaky || Random.value < 0.35f ? 1 : 2;
                    return true;
            }
            return false;
        }

        /// <summary>The siege hasn't said GO yet: off to the rally point and wait there, facing their base.</summary>
        void TickRegroup()
        {
            var m = MindOf(MyTeam);
            if (m.Stance != Stance.Siege || m.Go) { Done(); return; }
            var rally = m.Rally;
            if (Flat(rally, transform.position) > 4f) { GoTo(rally, 2f, true); return; }
            // there: milling about a little, eyes on their base
            if (Time.time >= m_NextPatrol)
            {
                m_NextPatrol = Time.time + Random.Range(1.5f, 3f);
                GoTo(Around(rally, 3f), 0.8f, false);
            }
            if (m.RaidTeam >= 0 && m.RaidTeam < Cfg.TeamCount)
            {
                m_HasLook = true;
                m_LookAt = Cfg.BaseCenter[m.RaidTeam] + Vector3.up * 2f;
            }
        }
    }
}
