using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public static partial class Cfg
    {
        public static bool Bedwars => Rules == GameRules.Bedwars;
        public static bool ThreeGoal => Rules == GameRules.ThreeGoal;
        public static bool ProgressMode => Rules == GameRules.Progress;
        public static bool Assassin => Rules == GameRules.Assassin;
        public static bool Domination => Rules == GameRules.Domination;
        /// <summary>Bedwars and Assassin have no ball (and their Trade Station unlocks for everyone when the wall drops).</summary>
        public static bool NoBall => Bedwars || Assassin;

        [Tune("Modes")] public static int GoalsToWin = 3;
        /// <summary>Progress: seconds of the ball in your machine to fill your bar.</summary>
        [Tune("Modes")] public static float ProgressSeconds = 90f;
        /// <summary>Bedwars: hits it takes to destroy a machine (one explosive right on it does it at once).</summary>
        [Tune("Modes")] public static int MachineHitsToBreak = 3;

        /// <summary>A skull's Data: who it was (their team and slot), 1..16.</summary>
        public static int SkullData(int team, int slot) => Mathf.Clamp(team, 0, 3) * 4 + Mathf.Clamp(slot, 0, 3) + 1;
        public static void SkullWho(int data, out int team, out int slot) { int d = Mathf.Max(0, data - 1); team = d / 4; slot = d % 4; }
        /// <summary>The name on a skull (the player it was, as they're called now).</summary>
        public static string SkullName(int data)
        {
            SkullWho(data, out int t, out int s);
            foreach (var p in PlayerNet.All) if (p != null && p.Team.Value == t && p.Slot.Value == s) return p.DisplayName;
            return PlayerNet.DefaultName((byte)t, (byte)s);
        }
    }

    /// <summary>
    /// The five modes that are Classic with another way to win (the kill feed tells each mode's big moments):
    /// - BEDWARS: no ball. A team's alien machine has a cryochamber where the socket was, and its players respawn from
    ///   it; enemies destroy it with 3 hits of anything (or one explosive right on it) - it shows the damage in three
    ///   steps, and goes up spectacularly. A team without its machine doesn't respawn: last team standing wins.
    /// - 3 GOAL: the ball into your machine scores; a UFO drops it back into the middle. First to 3 (most goals when the
    ///   clock runs out; tied: overtime, next goal wins).
    /// - PROGRESS: while the ball sits in your machine your bar fills (ProgressSeconds); full, you win (the clock running
    ///   out: the fullest bar wins).
    /// - ASSASSIN: no ball. Every kill drops the victim's skull (a big world item; in team games it carries their name);
    ///   hand skulls in at your machine (E) - you see them on it - and a skull of every enemy wins.
    /// - DOMINATION: whoever has the ball (carrying it or in their machine) gets the Advanced Trade Station's items; lose
    ///   it, lose them (the Trade Station unlocks for everyone on the first capture, as usual).
    /// </summary>
    public partial class NetGame
    {
        /// <summary>3 Goal: goals per team (8 bits each).</summary>
        public readonly NetworkVariable<int> Goals = new NetworkVariable<int>();
        /// <summary>Progress: each team's bar, 0..1.</summary>
        public readonly NetworkVariable<Vector4> ProgressBars = new NetworkVariable<Vector4>();
        /// <summary>Bedwars: hits on each team's machine (4 bits each; MachineHitsToBreak = destroyed).</summary>
        public readonly NetworkVariable<int> MachineHits = new NetworkVariable<int>();
        /// <summary>Assassin: the skulls handed in - (team &lt;&lt; 5) | skull data, in the order they went in.</summary>
        public readonly NetworkList<byte> SkullsIn = new NetworkList<byte>();
        /// <summary>Domination: the team that has the ball now (-1 nobody).</summary>
        public readonly NetworkVariable<sbyte> BallTeam = new NetworkVariable<sbyte>(-1);

        public int GoalsOf(int t) => t < 0 || t > 3 ? 0 : (Goals.Value >> (t * 8)) & 255;
        public float ProgressOf(int t) => t switch { 0 => ProgressBars.Value.x, 1 => ProgressBars.Value.y, 2 => ProgressBars.Value.z, 3 => ProgressBars.Value.w, _ => 0f };
        public int HitsOn(int t) => t < 0 || t > 3 ? 0 : (MachineHits.Value >> (t * 4)) & 15;
        public bool MachineDown(int t) => Cfg.Bedwars && HitsOn(t) >= Cfg.MachineHitsToBreak;
        public int SkullsOf(int t) { int n = 0; foreach (var b in SkullsIn) if (b >> 5 == t) n++; return n; }

        /// <summary>Bedwars: can this team still respawn (its machine's standing)?</summary>
        public bool CanRespawn(int team) => !MachineDown(team);

        // ------------------------------------------------------------------ server

        readonly Dictionary<ulong, float> m_MachineHitAt = new Dictionary<ulong, float>();
        double m_GoalResetAt = -1;
        int m_LastBallTeam = -1;

        /// <summary>Server, every frame (Update): the modes' own clocks and wins.</summary>
        void ServerTickGameModes(double now)
        {
            if (!Cfg.ClassicMode || S != GameState.BallLive) return;
            var ball = Ball.Instance;
            if (Cfg.ThreeGoal && m_GoalResetAt >= 0 && now >= m_GoalResetAt)
            {
                // the goal's done: a UFO brings the ball back to the middle
                m_GoalResetAt = -1;
                if (ball != null && ball.IsSpawned) { ball.ServerReset(); BannerRpc(new FixedString64Bytes("BALL INCOMING"), new FixedString128Bytes("A UFO is dropping the ball back in the middle!")); }
            }
            if (Cfg.ProgressMode && ball != null && ball.SocketTeam.Value >= 0)
            {
                int t = ball.SocketTeam.Value;
                var v = ProgressBars.Value;
                float add = Time.deltaTime / Mathf.Max(1f, Cfg.ProgressSeconds);
                if (t == 0) v.x = Mathf.Min(1f, v.x + add); else if (t == 1) v.y = Mathf.Min(1f, v.y + add); else if (t == 2) v.z = Mathf.Min(1f, v.z + add); else v.w = Mathf.Min(1f, v.w + add);
                ProgressBars.Value = v;
                if (ProgressOf(t) >= 1f) { ServerVictoryCutscene(t, $"{Cfg.TeamName[t]} filled their progress bar"); return; }
            }
            if (Cfg.Domination)
            {
                int bt = ball == null ? -1 : ball.IsCarried ? (ball.Carrier != null ? ball.Carrier.Team.Value : -1) : ball.SocketTeam.Value;
                if (bt != m_LastBallTeam)
                {
                    m_LastBallTeam = bt;
                    BallTeam.Value = (sbyte)bt;
                    if (bt >= 0) ServerEventFeed(null, bt, KillCause.Dominate, "has the advanced trades");
                }
            }
            if (Cfg.Bedwars && !Bootstrap.Solo)
            {
                // last team standing: a team's in while its machine's up or anyone on it is alive
                int alive = 0, last = -1;
                for (int t = 0; t < Cfg.TeamCount; t++)
                {
                    bool any = !MachineDown(t);
                    foreach (var p in PlayerNet.All) if (p != null && p.Team.Value == t && !p.Dead.Value) any = true;
                    bool teamHasPlayers = false;
                    foreach (var p in PlayerNet.All) if (p != null && p.Team.Value == t) teamHasPlayers = true;
                    if (any && teamHasPlayers) { alive++; last = t; }
                }
                if (alive == 1) ServerVictoryCutscene(last, $"{Cfg.TeamName[last]}: last team standing");
            }
        }

        /// <summary>Server: the clock ran out in one of these modes - the leader wins (true), or it's level (false: overtime).</summary>
        bool ServerModeTimeUp()
        {
            int best = -1;
            float bestV = 0f;
            bool tie = false;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                float v = Cfg.ThreeGoal ? GoalsOf(t) : Cfg.ProgressMode ? ProgressOf(t) : Cfg.Assassin ? SkullsOf(t) : 0f;
                if (v > bestV + 0.0001f) { bestV = v; best = t; tie = false; }
                else if (best >= 0 && Mathf.Abs(v - bestV) < 0.0001f) tie = true;
            }
            if (Cfg.Bedwars || best < 0 || tie) return false;
            string why = Cfg.ThreeGoal ? $"{Cfg.TeamName[best]} scored the most goals" : Cfg.ProgressMode ? $"{Cfg.TeamName[best]} had the most progress" : $"{Cfg.TeamName[best]} handed in the most skulls";
            ServerVictoryCutscene(best, why);
            return true;
        }

        /// <summary>Server (Ball.ServerSocket): the ball went into a machine - 3 Goal scores it.</summary>
        public void ServerOnSocket(int team, PlayerNet by)
        {
            if (!Cfg.ThreeGoal || S != GameState.BallLive || team < 0) return;
            int g = GoalsOf(team) + 1;
            Goals.Value = (Goals.Value & ~(255 << (team * 8))) | (Mathf.Min(255, g) << (team * 8));
            ServerEventFeed(by, team, KillCause.Goal, $"scored for {Cfg.TeamName[team]} ({g}/{Cfg.GoalsToWin})");
            BannerRpc(new FixedString64Bytes($"GOAL! {Cfg.TeamName[team]}"), new FixedString128Bytes($"{g} of {Cfg.GoalsToWin}"));
            if (g >= Cfg.GoalsToWin || Overtime.Value) { ServerVictoryCutscene(team, $"{Cfg.TeamName[team]} scored {g}"); return; }
            m_GoalResetAt = NetworkManager.ServerTime.Time + 2.5;
        }

        /// <summary>Server: a hit landed at `point` (a melee swing, an arrow or spear, a bullet, a blast) - Bedwars counts it
        /// if it's on an enemy machine (one hit per attacker every half second; an explosion right on it destroys it).</summary>
        public void ServerMaybeHitMachine(Vector3 point, PlayerNet by, bool explosive)
        {
            if (!Cfg.Bedwars || S != GameState.BallLive) return;
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                if (MachineDown(t) || (by != null && by.Team.Value == t)) continue;
                var m = Cfg.MachinePos(t);
                var d = point - m;
                bool on = new Vector2(d.x, d.z).magnitude < (explosive ? 3.2f : 1.9f) && d.y > -1f && d.y < 4.5f;
                if (!on) continue;
                ulong key = by != null ? by.NetworkObjectId : 0UL;
                if (!explosive && m_MachineHitAt.TryGetValue(key, out var at) && Time.time - at < 0.5f) return;
                m_MachineHitAt[key] = Time.time;
                int hits = Mathf.Min(Cfg.MachineHitsToBreak, explosive ? Cfg.MachineHitsToBreak : HitsOn(t) + 1);
                MachineHits.Value = (MachineHits.Value & ~(15 << (t * 4))) | (hits << (t * 4));
                MachineFxRpc((byte)t, (byte)hits);
                if (hits >= Cfg.MachineHitsToBreak)
                {
                    ServerEventFeed(by, by != null ? by.Team.Value : t, KillCause.MachineDown, $"destroyed {Cfg.TeamName[t]}'s machine");
                    BannerRpc(new FixedString64Bytes($"{Cfg.TeamName[t]}'S MACHINE IS DOWN"), new FixedString128Bytes("They won't respawn any more"));
                }
                return;
            }
        }

        /// <summary>Every screen: a machine was hit (its damage shows in steps; the last one blows it up).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        void MachineFxRpc(byte team, byte hits)
        {
            var at = Cfg.MachinePos(team) + Vector3.up * 1.5f;
            if (hits >= Cfg.MachineHitsToBreak)
            {
                Fx.Play(FxKind.Explosion, at, Vector3.up);
                Fx.Play(FxKind.Explosion, at + Vector3.up * 1.2f, Vector3.up);
                Fx.Play(FxKind.Break, at, Vector3.up);
                Sfx.Play(Sfx.BigBoom, at, 1f, 0f, 200f);
                Fx.Shake(0.6f);
            }
            else
            {
                Fx.Play(FxKind.StructureHit, at, Vector3.up);
                Fx.Play(FxKind.Break, at, Vector3.up);
                Sfx.Play(Sfx.Smash, at, 1f, 0.05f, 80f);
            }
        }

        /// <summary>Server: someone died - Assassin drops their skull.</summary>
        void ServerModesOnKilled(PlayerNet victim, PlayerNet killer)
        {
            if (!Cfg.Assassin || victim == null || S != GameState.BallLive) return;
            ServerDropItem(ItemStack.Of(Item.Skull, 1, Cfg.SkullData(victim.Team.Value, victim.Slot.Value)), victim.transform.position + Vector3.up * 0.5f, Vector3.up, victim.transform.position + Vector3.up * 1.2f);
        }

        /// <summary>Server: a player hands in every skull they carry at their machine. A skull of each enemy player wins.</summary>
        public void ServerDepositSkulls(PlayerNet p)
        {
            if (!Cfg.Assassin || p == null || S != GameState.BallLive) return;
            int team = p.Team.Value, given = 0;
            for (int i = 0; i < p.Inv.Count; i++)
            {
                var st = p.Inv[i];
                if (st.Id != Item.Skull) continue;
                Cfg.SkullWho(st.Data, out int vt, out _);
                if (vt == team) continue; // (your own team's skulls don't count)
                for (int k = 0; k < st.Count; k++) { SkullsIn.Add((byte)((team << 5) | (st.Data & 31))); given++; }
                p.Inv[i] = default;
            }
            if (given == 0) { p.NotifyPublic("Bring enemy skulls here (kill them - they drop one)"); return; }
            ServerEventFeed(p, team, KillCause.SkullIn, $"handed in {given} skull{(given == 1 ? "" : "s")}");
            // won: a skull of every enemy player that's in the game
            bool all = true;
            foreach (var e in PlayerNet.All)
            {
                if (e == null || e.Team.Value == team) continue;
                byte want = (byte)Cfg.SkullData(e.Team.Value, e.Slot.Value);
                bool have = false;
                foreach (var b in SkullsIn) if (b >> 5 == team && (b & 31) == want) { have = true; break; }
                if (!have) { all = false; break; }
            }
            if (all || Overtime.Value) ServerVictoryCutscene(team, $"{Cfg.TeamName[team]} collected every enemy skull");
        }

        /// <summary>Server: a mode's line in everyone's kill feed (the player who did it - or just the team - and words).</summary>
        public void ServerEventFeed(PlayerNet who, int team, byte cause, string text)
        {
            KillFeedRpc(who != null ? who.Team.Value : (byte)team, who != null ? who.Slot.Value : (byte)0, (byte)Mathf.Max(0, team), 0, cause, false,
                new FixedString32Bytes(who != null ? who.DisplayName : Cfg.TeamName[Mathf.Clamp(team, 0, 3)]), new FixedString32Bytes(text.Length > 29 ? text.Substring(0, 29) : text));
        }
    }

    public partial class PlayerNet
    {
        /// <summary>Assassin: E on your own machine with skulls - hand them in.</summary>
        [Rpc(SendTo.Server)]
        public void DepositSkullsRpc()
        {
            if (Dead.Value || NetGame.Instance == null) return;
            if (Vector3.Distance(transform.position, Cfg.MachinePos(Team.Value)) > Cfg.InteractRange + 3f) return;
            NetGame.Instance.ServerDepositSkulls(this);
        }
    }
}
