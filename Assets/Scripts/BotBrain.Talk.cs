using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Bots that talk and listen (BotBrain.cs), so they're teammates to play with and not just bodies:
    ///   Callouts in team chat - what it has set its mind on (saving up for a crossbow, 230 / 500), the siege's rally
    ///   point and its GO, the ball taken or lost, an enemy in the base, an airdrop it's going for, new gear - each line
    ///   now and then, never a flood (a gap per bot and per kind of line for the team). Now and then a light word to
    ///   everyone after a kill.
    ///   Orders from a human teammate in chat: "follow" (it runs with you for a minute and a half), "defend" / "home"
    ///   (back to guard the base), "raid" / "attack" / "push" / "go" (a siege that goes in now, or a push into their
    ///   half), "farm" / "wood", and "stop" to let them think for themselves again. It answers.
    ///   Help: a teammate being hit (you too) with an enemy on them - the nearest bot that can be spared comes over.
    /// </summary>
    public partial class BotBrain
    {
        float m_NextSay;
        int m_KillsSeen;
        static readonly Dictionary<string, float> s_SaidAt = new Dictionary<string, float>();
        /// <summary>(tests) lines it has said, and orders it has taken.</summary>
        public int Said { get; private set; }
        public static int OrdersTaken { get; private set; }

        /// <summary>A line in chat (team chat unless `all`), if it hasn't spoken lately and nobody on its team has said this
        /// kind of thing lately (`key`, `gap` seconds).</summary>
        bool Say(string text, string key, float gap = 20f, bool all = false)
        {
            if (m_P == null || !m_P.IsSpawned || m_P.Dead.Value || Time.time < m_NextSay) return false;
            string k = (all ? "all" : MyTeam.ToString()) + ":" + key;
            if (s_SaidAt.TryGetValue(k, out var at) && Time.time - at < gap) return false;
            s_SaidAt[k] = Time.time;
            m_NextSay = Time.time + 4f;
            m_P.ChatRpc(new FixedString128Bytes(text.Length > 70 ? text.Substring(0, 70) : text), !all);
            Said++;
            return true;
        }

        /// <summary>One of the team's bots says it (the first alive one that's free to speak).</summary>
        static void TeamSay(int team, string text, string key, float gap = 20f)
        {
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || !p.Bot.Value || p.Dead.Value || p.Team.Value != team) continue;
                var b = p.GetComponent<BotBrain>();
                if (b != null && Time.time >= b.m_NextSay) { b.Say(text, key, gap); return; }
            }
        }

        static string ItemWord(Item i) => i == Item.BuildingPlan ? "the base" : Cfg.ItemName(i).ToLower();

        /// <summary>The team's stance just changed: someone says what that means.</summary>
        static void StanceCallout(Mind m, int team, PlayerNet carrier)
        {
            switch (m.Stance)
            {
                case Stance.Contest: TeamSay(team, "The ball's loose - go get it!", "contest", 30f); break;
                case Stance.Hold: TeamSay(team, "Ball's in! Hold the base - I'll watch the way in.", "hold", 30f); break;
                case Stance.Intercept: if (carrier != null) TeamSay(team, $"{carrier.DisplayName} has the ball - cut them off!", "intercept", 20f); break;
                case Stance.Prepare: TeamSay(team, "They've got the ball. Gear up, we're taking it back.", "prepare", 40f); break;
                case Stance.Siege: TeamSay(team, $"Meet at {RallyWord(m, team)} - we go in together.", "siege", 25f); break;
                case Stance.Escort: if (carrier != null && carrier.Bot.Value) TeamSay(team, "Got the ball - cover me!", "escort", 25f); break;
            }
        }

        /// <summary>Where the rally point is, in words: round which side of their base.</summary>
        static string RallyWord(Mind m, int team)
        {
            if (m.RaidTeam < 0 || m.RaidTeam >= Cfg.TeamCount) return "their base";
            var eb = Cfg.BaseCenter[m.RaidTeam];
            var toUs = Cfg.BaseCenter[team] - eb; toUs.y = 0f;
            var r = m.Rally - eb; r.y = 0f;
            float ang = Vector3.SignedAngle(toUs, r, Vector3.up);
            if (Mathf.Abs(ang) < 40f) return "the front of their base";
            if (Mathf.Abs(ang) > 120f) return "the back of their base";
            return ang > 0f ? "the left side of their base" : "the right side of their base";
        }

        /// <summary>Little things worth a word, every think: a kill (now and then, to everyone), a teammate it's off to help.</summary>
        void TalkTick()
        {
            int k = m_P.Kills.Value;
            if (k > m_KillsSeen)
            {
                m_KillsSeen = k;
                if (Random.value < 0.3f * m_Playful)
                {
                    string[] lines = { "gg", "too slow!", "nice try", "got one!", "back to base with you", "rock solid", "next!" };
                    Say(lines[Random.Range(0, lines.Length)], "kill" + m_P.NetworkObjectId, 40f, true);
                }
            }
        }

        // ---------------------------------------------------------------- orders

        /// <summary>A human said something in chat (server): their team's bots take it as an order if it is one.</summary>
        public static void HeardChat(PlayerNet from, string text, bool team)
        {
            if (from == null || from.Bot.Value) return;
            string t = " " + text.ToLowerInvariant() + " ";
            int tm = from.Team.Value;
            if (tm < 0 || tm > 3) return;
            var m = MindOf(tm);
            string reply = null;
            if (t.Contains("follow") || t.Contains("with me") || t.Contains("come here") || t.Contains("on me"))
            {
                m.Follow = from; m.FollowUntil = Time.time + 90f; m.Order = Plan.None;
                reply = "Right behind you.";
            }
            else if (t.Contains("defend") || t.Contains("guard") || t.Contains(" home") || t.Contains("protect"))
            {
                m.Order = Plan.Guard; m.OrderUntil = Time.time + 75f; m.Follow = null;
                reply = "Heading home to hold it.";
            }
            else if (t.Contains("raid") || t.Contains("attack") || t.Contains("push") || t.Contains(" go ") || t.Contains("charge"))
            {
                m.Follow = null;
                var b = Ball.Instance;
                bool theirs = b != null && b.IsSpawned && !b.IsCarried && b.SocketTeam.Value >= 0 && b.SocketTeam.Value != tm;
                if (theirs) { m.Order = Plan.Raid; m.OrderUntil = Time.time + 90f; m.Go = true; m.GoAt = Time.time; reply = "Going in - let's take it!"; }
                else { m.Order = Plan.Hunt; m.OrderUntil = Time.time + 75f; reply = "Pushing up - let's find them."; }
            }
            else if (t.Contains("farm") || t.Contains(" wood") || t.Contains("gather"))
            {
                m.Order = Plan.Farm; m.OrderUntil = Time.time + 60f; m.Follow = null;
                reply = "Getting wood.";
            }
            else if (t.Contains("stop") || t.Contains("free") || t.Contains("your call") || t.Contains("do your thing"))
            {
                m.Order = Plan.None; m.Follow = null;
                reply = "OK, doing my own thing.";
            }
            if (reply == null) return;
            OrdersTaken++;
            m.OrderAt = Time.time;
            // the answer, and every bot on the team thinks again straight away
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || !p.Bot.Value || p.Team.Value != tm) continue;
                var b = p.GetComponent<BotBrain>();
                if (b == null) continue;
                b.m_NextThink = 0f;
                b.m_PlanUntil = 0f;
                if (reply != null && b.Say(reply, "order", 1f)) reply = null;
            }
        }

        /// <summary>An order from a human teammate still standing: the plan it asks for (false: none, or it doesn't fit).</summary>
        bool OrderPlan(Mind m, int load)
        {
            if (m.Order == Plan.None || Time.time > m.OrderUntil) { m.Order = Plan.None; return false; }
            var ball = Ball.Instance;
            if (m.Order == Plan.Raid && (ball == null || !ball.IsSpawned || ball.SocketTeam.Value < 0 || ball.SocketTeam.Value == MyTeam) && (ball == null || !ball.IsCarried)) { m.Order = Plan.None; return false; } // (the ball's not theirs any more)
            if (m_Plan == m.Order && Time.time < m_PlanUntil + 30f) { m_PlanUntil = Mathf.Max(m_PlanUntil, Time.time + 15f); return true; }
            StartPlan(m.Order, load);
            if (m.Order == Plan.Hunt) m_HuntStyle = 0;
            return true;
        }

        /// <summary>Following a human teammate who said so, or going to a teammate who's being hit: true, it's on its way
        /// (Task.Escort with them).</summary>
        bool BuddyJob(Mind m)
        {
            PlayerNet who = null;
            if (m.Follow != null && Time.time < m.FollowUntil && m.Follow.IsSpawned && !m.Follow.Dead.Value && m.Follow.Team.Value == MyTeam) who = m.Follow;
            else if (m.Follow != null && Time.time >= m.FollowUntil) m.Follow = null;
            if (who == null && m.HelpBot == m_P && Time.time - m.HelpAt < 6f && m.HelpOf != null && m.HelpOf.IsSpawned && !m.HelpOf.Dead.Value) who = m.HelpOf;
            if (who == null) { m_EscortOf = null; return false; }
            if (m_EscortOf != who)
            {
                m_EscortOf = who;
                if (who == m.HelpOf) { Say($"Coming, {who.DisplayName}!", "help", 15f); Helped++; }
            }
            Set(Task.Escort);
            return true;
        }

        /// <summary>(tests) teammates it went to help.</summary>
        public int Helped { get; private set; }
        PlayerNet m_EscortOf;

        static readonly Dictionary<PlayerNet, float> s_LastHp = new Dictionary<PlayerNet, float>();

        /// <summary>A teammate losing health with an enemy close to them: the nearest bot that can be spared goes to help.</summary>
        void HelpTick(Mind m, int team)
        {
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Team.Value != team) continue;
                float hp = p.Health.Value + p.ArmorHp.Value;
                bool hit = s_LastHp.TryGetValue(p, out var was) && hp < was - 0.5f && !p.Dead.Value;
                s_LastHp[p] = hp;
                if (!hit || Time.time - m.HelpAt < 3f) continue;
                if (!EnemyNear(p.transform.position, 30f)) continue;
                // the nearest bot that's free (not the one being hit, not in a siege that's going, not carrying the ball)
                PlayerNet best = null;
                float bd = 75f;
                foreach (var q in PlayerNet.All)
                {
                    if (q == null || q == p || !q.IsSpawned || !q.Bot.Value || q.Dead.Value || q.Team.Value != team || q.CarryingBall) continue;
                    var b = q.GetComponent<BotBrain>();
                    if (b == null || b.m_Task == Task.Fight || (m.Stance == Stance.Siege && m.Go && b.m_Plan == Plan.Raid) || (b == null)) continue;
                    if (q == m.Anchor && m.Stance == Stance.Hold && Flat(p.transform.position, Cfg.BaseCenter[team]) > 40f) continue;
                    float d = Flat(q.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = q; }
                }
                if (best == null) continue;
                m.HelpAt = Time.time;
                m.HelpOf = p;
                m.HelpPos = p.transform.position;
                m.HelpBot = best;
            }
        }
    }
}
