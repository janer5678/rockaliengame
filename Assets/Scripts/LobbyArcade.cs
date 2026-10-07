using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The telly's warm-up game in the ship lobby (ShipLobby.cs): click the telly and the camera moves in on it, and you're
    /// playing - a tiny pixelated top-down version of the real game on the old CRT (128 x 96, point filtered). Everyone
    /// playing is a little alien in their team's colour, moved with WASD (the lobby has no other use for them). Walk into
    /// a tree and you've got it (+1, it grows back), grab the ball in the middle and run it to your team's machine on your
    /// side (+5) - run into whoever's carrying it to knock it off them. Space (or a click) shoots an arrow the way you're
    /// facing (the little bow pixel shows it): trees stop arrows (they stick in them), an enemy it reaches is shot - down
    /// for a moment where they fell, dropping the ball, then back on their side - and the shooter gets +2. The bar along the top lists everyone playing:
    /// their colour and their own score (a newcomer shows up on it straight away). STOP PLAYING (Hud.Lobby.cs) goes back
    /// to the couch.
    /// Networked: whoever plays sends their stick to the server (PlayerNet.Arcade.cs), the server runs the game and sends
    /// everyone a small snapshot ~15 times a second (NetGame.Arcade.cs), so every telly shows the same game; who's playing
    /// is synced too (PlayerNet.ArcadePlaying), so their alien on the couch holds a controller and watches the screen.
    /// Lives on the Bootstrap object next to ShipLobby.
    /// </summary>
    public class LobbyArcade : MonoBehaviour
    {
        /// <summary>The picture's size (4:3 like the telly), and the score bar along its top.</summary>
        public const int W = 128, H = 96, Bar = 9;
        /// <summary>The playing field (pixels, y down from under the score bar).</summary>
        const float FW = W, FH = H - Bar;
        const float Speed = 40f, CarrySlow = 0.82f, Reach = 5f, ChopFlash = 0.35f, TreeBack = 7f, Stun = 0.8f;
        const int TreeScore = 1, BallScore = 5, KillScore = 2;
        /// <summary>The arrows: how fast they fly (pixels a second), how often you can shoot, how long a shot player's down.</summary>
        const float ArrowSpeed = 110f, ShootEvery = 0.45f, Respawn = 2.5f;
        const float SendEvery = 1f / 15f;

        static readonly Vector2[] k_Trees =
        {
            new Vector2(22, 12), new Vector2(22, 75), new Vector2(106, 12), new Vector2(106, 75), new Vector2(42, 30),
            new Vector2(86, 57), new Vector2(42, 57), new Vector2(86, 30), new Vector2(64, 19), new Vector2(64, 68),
        };
        /// <summary>Each team's machine: 0 on the left, 1 on the right, 2 at the top, 3 at the bottom.</summary>
        static readonly Vector2[] k_Machines = { new Vector2(5, FH * 0.5f), new Vector2(FW - 6, FH * 0.5f), new Vector2(FW * 0.5f, 4), new Vector2(FW * 0.5f, FH - 5) };
        static Vector2 BallHome => new Vector2(FW * 0.5f, FH * 0.5f);

        static LobbyArcade s_I;

        public static void Ensure(GameObject host)
        {
            if (s_I == null) s_I = host.GetComponent<LobbyArcade>() ?? host.AddComponent<LobbyArcade>();
        }

        void Awake() => s_I = this;

        // ================================================================== the snapshot everyone draws

        struct Dot { public byte Id, Team, Flags, Score, Face; public Vector2 Pos; }
        struct Shot { public Vector2 Pos; public byte Ang, Flags; }
        class Snap
        {
            public Vector2 Ball;
            public bool Held;
            public ushort Trees;
            public readonly int[] Score = new int[4];
            public byte Goals, GoalTeam;
            public readonly List<Dot> Dots = new List<Dot>();
            public readonly List<Shot> Arrows = new List<Shot>();
            public byte Kills;
            public float At;
            public void CopyFrom(Snap o)
            {
                Ball = o.Ball; Held = o.Held; Trees = o.Trees; Goals = o.Goals; GoalTeam = o.GoalTeam; At = o.At; Kills = o.Kills;
                for (int i = 0; i < 4; i++) Score[i] = o.Score[i];
                Dots.Clear(); Dots.AddRange(o.Dots);
                Arrows.Clear(); Arrows.AddRange(o.Arrows);
            }
        }
        static readonly Snap s_Prev = new Snap(), s_Cur = new Snap();
        static bool s_Have;
        const byte FCarry = 1, FChop = 2, FStun = 4, FDead = 8;

        /// <summary>Every client (and the host, from its own game): a new snapshot.</summary>
        public static void ClientReceive(byte[] b)
        {
            if (b == null || b.Length < 12) return;
            s_Prev.CopyFrom(s_Have ? s_Cur : s_Prev);
            var s = s_Cur;
            int i = 0;
            s.Ball = new Vector2(b[i++] * 0.5f, b[i++] * 0.5f);
            s.Held = b[i++] != 0;
            s.Trees = (ushort)(b[i++] | (b[i++] << 8));
            for (int t = 0; t < 4; t++) s.Score[t] = b[i++];
            s.Goals = b[i++];
            s.GoalTeam = b[i++];
            int n = b[i++];
            s.Dots.Clear();
            for (int k = 0; k < n && i + 7 <= b.Length; k++)
                s.Dots.Add(new Dot { Id = b[i++], Team = b[i++], Flags = b[i++], Score = b[i++], Pos = new Vector2(b[i++] * 0.5f, b[i++] * 0.5f), Face = b[i++] });
            // the shots: how many have been hit so far, the arrows flying (or stuck in a tree)
            s.Arrows.Clear();
            if (i + 2 <= b.Length)
            {
                s.Kills = b[i++];
                int na = b[i++];
                for (int k = 0; k < na && i + 4 <= b.Length; k++)
                    s.Arrows.Add(new Shot { Pos = new Vector2(b[i++] * 0.5f, b[i++] * 0.5f), Ang = b[i++], Flags = b[i++] });
            }
            s.At = Time.time;
            if (!s_Have) { s_Prev.CopyFrom(s_Cur); s_Have = true; }
        }

        // ================================================================== the server's game

        class Runner
        {
            public PlayerNet P;                 // (null: a test dummy)
            public byte Id;
            public int Team;
            public Vector2 Pos, Home, Facing = Vector2.right;
            public float ChopUntil, StunUntil, DeadUntil, ShootCool;
            public int Score;
            public bool Dead;
        }
        class Arrow { public Vector2 Pos, Dir; public Runner From; public int Team; public float Born, StuckUntil; public bool Stuck; }
        static readonly List<Runner> s_Run = new List<Runner>();
        static readonly List<Arrow> s_Arrows = new List<Arrow>();
        static Runner s_Holder;
        static Vector2 s_Ball = BallHome;
        static readonly float[] s_TreeBackAt = new float[k_Trees.Length];
        static readonly int[] s_Score = new int[4];
        static byte s_Goals, s_GoalTeam, s_Kills;
        static float s_StealCool, s_NextSend;
        const int MaxArrows = 24;
        static readonly byte[] s_Buf = new byte[13 + 7 * 32 + 2 + 4 * MaxArrows];

        /// <summary>Server: a player starts / stops playing (PlayerNet.ArcadeJoinRpc).</summary>
        public static void ServerJoin(PlayerNet p, bool on)
        {
            int at = IndexOf(p);
            if (!on)
            {
                if (at >= 0) Drop(s_Run[at]);
                return;
            }
            if (at >= 0 || s_Run.Count >= 32) return;
            AddRunner(p, (byte)(p.OwnerClientId & 255), p.Team.Value & 3);
        }

        static Runner AddRunner(PlayerNet p, byte id, int team)
        {
            int same = 0;
            foreach (var r in s_Run) if (r.Team == team) same++;
            var m = k_Machines[team];
            var toward = (BallHome - m).normalized;
            var side = new Vector2(-toward.y, toward.x);
            var home = m + toward * 12f + side * ((same % 2 == 0 ? 1 : -1) * (6f + 6f * (same / 2)));
            var run = new Runner { P = p, Id = id, Team = team, Pos = home, Home = home, Facing = toward };
            s_Run.Add(run);
            s_NextSend = 0f; // (everyone's telly shows the newcomer - on the field and on the scores - straight away)
            return run;
        }

        static int IndexOf(PlayerNet p)
        {
            for (int i = 0; i < s_Run.Count; i++) if (s_Run[i].P == p) return i;
            return -1;
        }

        static void Drop(Runner r)
        {
            if (s_Holder == r) { s_Holder = null; s_Ball = r.Pos; }
            s_Run.Remove(r);
            foreach (var a in s_Arrows) if (a.From == r) a.From = null;
            s_NextSend = 0f; // (off everyone's scores straight away)
            if (s_Run.Count == 0) ResetGame();
        }

        static void ResetGame()
        {
            s_Holder = null;
            s_Ball = BallHome;
            s_Arrows.Clear();
            for (int i = 0; i < s_TreeBackAt.Length; i++) s_TreeBackAt[i] = 0f;
            for (int t = 0; t < 4; t++) s_Score[t] = 0;
        }

        static bool LobbyUp
        {
            get
            {
                var g = NetGame.Instance;
                return g != null && g.IsSpawned && g.S == GameState.Waiting && NetGame.ReadyLobby;
            }
        }

        /// <summary>Server: a player fires an arrow the way they're facing (PlayerNet.ArcadeShootRpc).</summary>
        public static void ServerShoot(PlayerNet p)
        {
            int at = IndexOf(p);
            if (at >= 0) Shoot(s_Run[at], Time.time);
        }

        static void Shoot(Runner r, float now)
        {
            if (r.Dead || now < r.ShootCool || now < r.StunUntil || s_Arrows.Count >= MaxArrows) return;
            r.ShootCool = now + ShootEvery;
            s_Arrows.Add(new Arrow { Pos = r.Pos + r.Facing * 3f, Dir = r.Facing, From = r, Team = r.Team, Born = now });
            s_NextSend = 0f; // (everyone sees it go straight away)
        }

        /// <summary>The arrows fly; a standing tree stops one (it sticks in it a moment), an enemy it reaches is shot -
        /// down for a moment (dropping the ball), and the shooter gets the points.</summary>
        static void MoveArrows(float now, float dt)
        {
            for (int i = s_Arrows.Count - 1; i >= 0; i--)
            {
                var a = s_Arrows[i];
                if (a.Stuck)
                {
                    if (now >= a.StuckUntil) s_Arrows.RemoveAt(i);
                    continue;
                }
                float left = ArrowSpeed * dt;
                bool gone = false;
                while (left > 0f && !gone)
                {
                    float step = Mathf.Min(1.5f, left);
                    left -= step;
                    a.Pos += a.Dir * step;
                    if (a.Pos.x < 0f || a.Pos.x > FW || a.Pos.y < 0f || a.Pos.y > FH || now - a.Born > 2f) { s_Arrows.RemoveAt(i); gone = true; break; }
                    for (int t = 0; t < k_Trees.Length; t++)
                        if (s_TreeBackAt[t] <= now && (a.Pos - TreeMiddle(t)).sqrMagnitude < 3.2f * 3.2f)
                        {
                            // blocked: it sticks in the tree for a moment
                            a.Stuck = true;
                            a.StuckUntil = now + 0.9f;
                            ServerBlocked++;
                            gone = true;
                            break;
                        }
                    if (gone) break;
                    foreach (var r in s_Run)
                    {
                        if (r == a.From || r.Dead || r.Team == a.Team || (r.Pos - a.Pos).sqrMagnitude > 3.2f * 3.2f) continue;
                        Kill(r, a.From, now);
                        s_Arrows.RemoveAt(i);
                        gone = true;
                        break;
                    }
                }
            }
        }

        /// <summary>The middle of a tree's leaves (what an arrow hits), from its trunk point.</summary>
        static Vector2 TreeMiddle(int t) => k_Trees[t] + new Vector2(0f, -1.5f);

        static void Kill(Runner victim, Runner shooter, float now)
        {
            victim.Dead = true;
            victim.DeadUntil = now + Respawn;
            if (s_Holder == victim) { s_Holder = null; s_Ball = victim.Pos; } // (the ball drops where they fell)
            if (shooter != null && s_Run.Contains(shooter)) Add(shooter, KillScore);
            s_Kills++;
            s_NextSend = 0f;
        }

        void ServerTick(float now, float dt)
        {
            var g = NetGame.Instance;
            if (!LobbyUp)
            {
                // the match is on (or no lobby): nobody's playing any more
                if (s_Run.Count > 0) { s_Run.Clear(); ResetGame(); }
                foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.ArcadePlaying.Value) { p.ArcadePlaying.Value = false; p.ServerArcadeStick = Vector2.zero; }
                return;
            }
            ShipLobby.ServerKeepBotsReady(); // (the AI bots are always READY in the lobby: ShipLobby.Bots.cs)
            // whoever's playing (the synced flag is the truth: someone who left, or stopped, is taken out)
            for (int i = s_Run.Count - 1; i >= 0; i--)
            {
                var r = s_Run[i];
                if (r.P == null) continue; // (a test dummy)
                if (!r.P.IsSpawned || !r.P.ArcadePlaying.Value) { Drop(r); continue; }
                r.Team = r.P.Team.Value & 3;
            }
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p.ArcadePlaying.Value && IndexOf(p) < 0) ServerJoin(p, true);

            // move, collect trees, grab, tackle, score
            foreach (var r in s_Run)
            {
                if (r.Dead)
                {
                    // shot: back on their side of the field after a moment
                    if (now < r.DeadUntil) continue;
                    r.Dead = false;
                    r.Pos = r.Home;
                    r.Facing = (BallHome - r.Home).normalized;
                    r.StunUntil = 0f;
                    s_NextSend = 0f;
                }
                bool stunned = now < r.StunUntil;
                var stick = stunned || r.P == null ? Vector2.zero : r.P.ServerArcadeStick;
                if (stick.sqrMagnitude > 0.04f) r.Facing = new Vector2(stick.x, -stick.y).normalized; // (where the next arrow goes)
                float sp = Speed * (s_Holder == r ? CarrySlow : 1f);
                var move = new Vector2(stick.x, -stick.y) * sp * dt;
                r.Pos += move;
                r.Pos.x = Mathf.Clamp(r.Pos.x, 2f, FW - 3f);
                r.Pos.y = Mathf.Clamp(r.Pos.y, 2f, FH - 3f);
                for (int i = 0; i < k_Trees.Length; i++)
                {
                    if (s_TreeBackAt[i] > now) continue;
                    var d = r.Pos - k_Trees[i];
                    float m = d.magnitude;
                    if (m < 4.75f && !stunned)
                    {
                        // walking into a tree collects it there and then (+1; it grows back a while later)
                        s_TreeBackAt[i] = now + TreeBack;
                        Add(r, TreeScore);
                        r.ChopUntil = now + ChopFlash;
                        continue;
                    }
                    if (m < 4.5f)
                    {
                        // (dazed: trees are still in the way - pushed back out round them)
                        var nrm = m > 0.01f ? d / m : Vector2.right;
                        var tan = new Vector2(-nrm.y, nrm.x);
                        float into = -Vector2.Dot(move, nrm), along = Vector2.Dot(move, tan);
                        r.Pos = k_Trees[i] + nrm * 4.5f + tan * (along >= 0f ? 1f : -1f) * Mathf.Max(0f, into) * 0.7f;
                    }
                }
            }
            MoveArrows(now, dt);
            if (s_Holder == null)
            {
                foreach (var r in s_Run)
                    if (!r.Dead && now >= r.StunUntil && (r.Pos - s_Ball).magnitude < Reach) { s_Holder = r; break; }
            }
            else
            {
                // knocked off them: an enemy running into the carrier takes it (and they're dazed a moment)
                if (now >= s_StealCool)
                    foreach (var r in s_Run)
                        if (r != s_Holder && !r.Dead && r.Team != s_Holder.Team && now >= r.StunUntil && (r.Pos - s_Holder.Pos).magnitude < Reach)
                        {
                            s_Holder.StunUntil = now + Stun;
                            s_Holder = r;
                            s_StealCool = now + 1f;
                            break;
                        }
                s_Ball = s_Holder.Pos + new Vector2(0f, -4f);
                int team = s_Holder.Team;
                if ((s_Holder.Pos - k_Machines[team]).magnitude < 9f)
                {
                    Add(s_Holder, BallScore);
                    s_Goals++;
                    s_GoalTeam = (byte)team;
                    s_Holder = null;
                    s_Ball = BallHome;
                }
            }

            // send it (slower while nobody's playing: it's only the attract screen)
            if (now < s_NextSend) return;
            s_NextSend = now + (s_Run.Count > 0 ? SendEvery : 0.5f);
            int n = Write();
            var bytes = new byte[n];
            System.Array.Copy(s_Buf, bytes, n);
            ClientReceive(bytes); // (the host draws the same snapshots as everyone)
            if (g.NetworkManager != null && g.NetworkManager.ConnectedClientsIds.Count > 1) g.ArcadeStateRpc(bytes);
        }

        /// <summary>Points to a player (their own score on the bar) and their team.</summary>
        static void Add(Runner r, int pts)
        {
            r.Score = Mathf.Min(255, r.Score + pts);
            s_Score[r.Team] = Mathf.Min(255, s_Score[r.Team] + pts);
        }

        static int Write()
        {
            int i = 0;
            var b = s_Buf;
            b[i++] = Q(s_Ball.x); b[i++] = Q(s_Ball.y);
            b[i++] = (byte)(s_Holder != null ? 1 : 0);
            ushort trees = 0;
            float now = Time.time;
            for (int t = 0; t < k_Trees.Length; t++) if (s_TreeBackAt[t] <= now) trees |= (ushort)(1 << t);
            b[i++] = (byte)(trees & 255); b[i++] = (byte)(trees >> 8);
            for (int t = 0; t < 4; t++) b[i++] = (byte)Mathf.Clamp(s_Score[t], 0, 255);
            b[i++] = s_Goals; b[i++] = s_GoalTeam;
            b[i++] = (byte)s_Run.Count;
            foreach (var r in s_Run)
            {
                b[i++] = r.Id;
                b[i++] = (byte)(r.Team & 3);
                b[i++] = (byte)((s_Holder == r ? FCarry : 0) | (now < r.ChopUntil ? FChop : 0) | (now < r.StunUntil ? FStun : 0) | (r.Dead ? FDead : 0));
                b[i++] = (byte)Mathf.Clamp(r.Score, 0, 255);
                b[i++] = Q(r.Pos.x); b[i++] = Q(r.Pos.y);
                b[i++] = Ang(r.Facing);
            }
            // the shots: how many have been hit, and every arrow in the air (or stuck in a tree)
            b[i++] = s_Kills;
            int na = Mathf.Min(s_Arrows.Count, MaxArrows);
            b[i++] = (byte)na;
            for (int k = 0; k < na; k++)
            {
                var a = s_Arrows[k];
                b[i++] = Q(a.Pos.x); b[i++] = Q(a.Pos.y);
                b[i++] = Ang(a.Dir);
                b[i++] = (byte)((a.Stuck ? 1 : 0) | ((a.Team & 3) << 1));
            }
            return i;
        }

        static byte Q(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 2f), 0, 255);
        /// <summary>A direction as a byte (0..255 round the circle), and back.</summary>
        static byte Ang(Vector2 d) => (byte)(Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(d.y, d.x) / (Mathf.PI * 2f), 1f) * 256f) & 255);
        static Vector2 Dir(byte a) { float r = a / 256f * Mathf.PI * 2f; return new Vector2(Mathf.Cos(r), Mathf.Sin(r)); }

        // ------------------------------------------------------------------ (tests) the arrows

        /// <summary>(tests, server) how many arrows trees have stopped.</summary>
        public static int ServerBlocked { get; private set; }
        static Runner s_Dummy;
        /// <summary>(tests, server) a still target on the field (a player of `team` who isn't anyone) - or take it off.</summary>
        public static void TestDummy(bool on, int team, Vector2 pos)
        {
            if (s_Dummy != null) { Drop(s_Dummy); s_Dummy = null; }
            if (!on) return;
            s_Dummy = AddRunner(null, 250, team & 3);
            s_Dummy.Pos = pos;
        }
        /// <summary>(tests, server) the test dummy has been shot (and isn't back yet).</summary>
        public static bool DummyDown => s_Dummy != null && s_Dummy.Dead;
        /// <summary>(tests, server) put a player's little alien here, facing this way.</summary>
        public static bool TestPlace(ulong clientId, Vector2 pos, Vector2 facing)
        {
            foreach (var r in s_Run)
                if (r.P != null && r.P.OwnerClientId == clientId) { r.Pos = pos; r.Facing = facing.normalized; r.ShootCool = 0f; return true; }
            return false;
        }
        /// <summary>(tests, server) the tree nearest this point is standing (not a stump growing back).</summary>
        public static bool TreeStanding(Vector2 near)
        {
            int best = 0;
            for (int t = 1; t < k_Trees.Length; t++) if ((k_Trees[t] - near).sqrMagnitude < (k_Trees[best] - near).sqrMagnitude) best = t;
            return s_TreeBackAt[best] <= Time.time;
        }
        /// <summary>(tests) how many players have been shot so far (the latest snapshot), and arrows in it.</summary>
        public static int Kills => s_Have ? s_Cur.Kills : 0;
        public static int ArrowsShown => s_Have ? s_Cur.Arrows.Count : 0;
        /// <summary>(tests) shoot once (as if Space were pressed).</summary>
        public static bool TestShoot;


        // ================================================================== this PC: looking at it, playing it

        /// <summary>The camera's on the telly (ShipLobby frames it) - playing, or watching (spectators).</summary>
        public static bool Focused { get; private set; }

        /// <summary>Clicked the telly: the camera goes to it and (a player) you start playing.</summary>
        public static void Focus(bool on)
        {
            if (Focused == on) return;
            Focused = on;
            var me = PlayerNet.Local;
            if (me != null && me.IsSpawned) me.ArcadeJoinRpc(on);
            s_SentStick = new Vector2(99f, 99f);
            s_NextJoin = Time.time + 1.5f; // (the answer takes a moment to come back)
        }

        /// <summary>(tests) hold the stick this way (x &gt; 1: the real keys), or steer by itself: 1 chop the nearest tree, 2 run the ball home.</summary>
        public static Vector2 TestStick = new Vector2(9f, 9f);
        public static int TestAuto;
        /// <summary>(tests) how many times the picture has been drawn, and a hash of the last one.</summary>
        public static int Redraws { get; private set; }
        public static int PictureHash { get; private set; }
        /// <summary>(tests) how many are playing in the latest snapshot, and a team's score in it.</summary>
        public static int Playing => s_Have ? s_Cur.Dots.Count : 0;
        public static int Score(int team) => s_Have ? s_Cur.Score[team & 3] : 0;
        /// <summary>(tests) how many players the score bar along the top of the picture showed last time it was drawn.</summary>
        public static int BoardShown { get; private set; }
        static readonly List<Dot> s_Board = new List<Dot>();
        /// <summary>(tests) a player's own score in the latest snapshot (-1: not playing).</summary>
        public static int PlayerScore(ulong clientId)
        {
            if (!s_Have) return -1;
            foreach (var d in s_Cur.Dots) if (d.Id == (byte)(clientId & 255)) return d.Score;
            return -1;
        }
        /// <summary>(tests) where this PC's own little alien is (the latest snapshot).</summary>
        public static bool MyDot(out Vector2 pos)
        {
            pos = default;
            var nm = NetworkManager.Singleton;
            if (!s_Have || nm == null) return false;
            byte id = (byte)(nm.LocalClientId & 255);
            foreach (var d in s_Cur.Dots) if (d.Id == id) { pos = d.Pos; return true; }
            return false;
        }
        /// <summary>Is this player in the latest snapshot (on every PC: their seated alien plays along).</summary>
        public static bool InGame(PlayerNet p) => p != null && p.IsSpawned && p.ArcadePlaying.Value;

        static Vector2 s_SentStick = new Vector2(99f, 99f);
        static float s_NextStick, s_NextJoin;

        void LocalInput(float now)
        {
            if (!ShipLobby.Active) { if (Focused) Focus(false); return; }
            var me = PlayerNet.Local;
            if (me == null || !me.IsSpawned) return;
            if (!Focused)
            {
                // (still listed as playing after the view went back to the couch: stop)
                if (me.ArcadePlaying.Value && now > s_NextJoin) { me.ArcadeJoinRpc(false); s_NextJoin = now + 1.5f; }
                return;
            }
            if (!me.ArcadePlaying.Value && now > s_NextJoin) { me.ArcadeJoinRpc(true); s_NextJoin = now + 1.5f; } // (eg. back from spectating)
            var stick = ReadStick();
            var q = new Vector2(Mathf.Round(stick.x * 100f), Mathf.Round(stick.y * 100f));
            if (q != s_SentStick || now > s_NextStick)
            {
                s_SentStick = q;
                s_NextStick = now + 0.5f;
                if (me.ArcadePlaying.Value) me.ArcadeStickRpc((sbyte)q.x, (sbyte)q.y);
            }
            // shoot: Space or a click on the picture (not on the lobby's buttons) - an arrow the way you're facing
            bool shoot = TestShoot;
            TestShoot = false;
            if (!shoot && !Chat.Open && GUIUtility.keyboardControl == 0 && Application.isFocused)
                shoot = Input.GetKeyDown(KeyCode.Space) || (Input.GetMouseButtonDown(0) && !Hud.MouseOverUI);
            if (shoot && me.ArcadePlaying.Value) me.ArcadeShootRpc();
        }

        static Vector2 ReadStick()
        {
            if (TestAuto > 0 && MyDot(out var at))
            {
                // (tests: steering by itself)
                Vector2 goal = at;
                if (TestAuto == 1)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < k_Trees.Length; i++)
                        if ((s_Cur.Trees & (1 << i)) != 0 && (k_Trees[i] - at).sqrMagnitude < best) { best = (k_Trees[i] - at).sqrMagnitude; goal = k_Trees[i]; }
                }
                else
                {
                    bool mine = false;
                    byte id = (byte)(NetworkManager.Singleton.LocalClientId & 255);
                    int team = 0;
                    foreach (var d in s_Cur.Dots) if (d.Id == id) { mine = (d.Flags & FCarry) != 0; team = d.Team; }
                    goal = mine ? k_Machines[team & 3] : s_Cur.Ball;
                }
                var dir = goal - at;
                return dir.sqrMagnitude < 0.01f ? Vector2.zero : new Vector2(dir.x, -dir.y).normalized;
            }
            if (TestStick.x <= 1f) return TestStick;
            if (Chat.Open || GUIUtility.keyboardControl != 0 || !Application.isFocused) return Vector2.zero;
            float x = Binds.Axis(Bind.Right, Bind.Left) + (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float y = Binds.Axis(Bind.Forward, Bind.Back) + (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            var v = new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        void Update()
        {
            float now = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer && NetGame.Instance != null && NetGame.Instance.IsSpawned) ServerTick(now, dt);
            else if (s_Run.Count > 0) { s_Run.Clear(); ResetGame(); }
            if (nm == null || !nm.IsListening) { s_Have = false; Focused = false; }
            LocalInput(now);
            if (ShipLobby.Active && now >= m_NextDraw)
            {
                m_NextDraw = now + 1f / 24f;
                Draw(now);
            }
        }

        // ================================================================== the picture

        Texture2D m_Tex;
        Material m_Mat;
        readonly Color32[] m_Px = new Color32[W * H];
        float m_NextDraw;
        byte m_GoalsSeen;
        bool m_GoalsKnown;
        float m_GoalFlash = -10f;
        int m_GoalTeam;

        /// <summary>The telly's screen material: the game's picture (lit by itself).</summary>
        public static Material ScreenMat
        {
            get
            {
                if (s_I == null) return null;
                s_I.MakeTex();
                return s_I.m_Mat;
            }
        }

        /// <summary>How bright the picture is (brighter than before: the screen reads as lit in the dark room).</summary>
        const float ScreenBright = 1.5f;
        float m_BrightSet = -1f;

        /// <summary>The picture's brightness x k (ShipLobby: Settings > Display > SHIP LOBBY > Telly, gently).</summary>
        public static void SetScreenBrightness(float k)
        {
            if (s_I == null || s_I.m_Mat == null || Mathf.Approximately(s_I.m_BrightSet, k)) return;
            s_I.m_BrightSet = k;
            if (s_I.m_Mat.HasProperty("_Intensity")) s_I.m_Mat.SetFloat("_Intensity", ScreenBright * k);
            else if (s_I.m_Mat.HasProperty("_EmissionColor")) s_I.m_Mat.SetColor("_EmissionColor", Color.white * ScreenBright * k);
        }

        /// <summary>The picture's average colour (the telly's light in the room follows it).</summary>
        public static Color Glow { get; private set; } = new Color(0.3f, 0.6f, 0.35f);

        void MakeTex()
        {
            if (m_Tex != null) return;
            m_Tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "lobbyArcade" };
            // the picture unlit (Resources/World/Screen.shader): as bright in the dark as under the lamp, no shine
            var screen = Resources.Load<Shader>("World/Screen");
            if (screen != null && screen.isSupported)
            {
                var sm = new Material(screen) { name = "lobby arcade" };
                sm.SetTexture("_MainTex", m_Tex);
                sm.SetFloat("_Intensity", ScreenBright);
                m_Mat = sm;
                Draw(Time.time);
                return;
            }
            var m = Art.NewMat(Color.white);
            m.name = "lobby arcade";
            m.SetTexture("_BaseMap", m_Tex);
            m.mainTexture = m_Tex;
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f); // (no shine on it)
            if (m.HasProperty("_SpecularHighlights")) { m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
            if (m.HasProperty("_EnvironmentReflections")) { m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
            if (m.HasProperty("_EmissionMap") && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", m_Tex);
                m.SetColor("_EmissionColor", Color.white * 1.1f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m_Mat = m;
            Draw(Time.time);
        }

        void OnDestroy()
        {
            if (m_Tex) Destroy(m_Tex);
            if (m_Mat) Destroy(m_Mat);
        }

        static readonly Color32 k_Bar = new Color32(10, 10, 18, 255), k_GrassA = new Color32(38, 96, 44, 255), k_GrassB = new Color32(44, 108, 50, 255),
            k_Edge = new Color32(24, 64, 30, 255), k_Trunk = new Color32(110, 70, 36, 255), k_Leaf = new Color32(26, 74, 28, 255),
            k_LeafHi = new Color32(60, 130, 52, 255), k_Ball = new Color32(255, 222, 50, 255), k_Black = new Color32(8, 8, 10, 255),
            k_White = new Color32(240, 240, 240, 255), k_Stump = new Color32(140, 98, 56, 255), k_ArrowWood = new Color32(200, 160, 100, 255);

        static Color32 TeamC(int t, float k = 1f)
        {
            var c = Cfg.TeamColor[Mathf.Clamp(t, 0, 3)] * k;
            return new Color32((byte)Mathf.Clamp(c.r * 255f, 0, 255), (byte)Mathf.Clamp(c.g * 255f, 0, 255), (byte)Mathf.Clamp(c.b * 255f, 0, 255), 255);
        }

        void Px(int x, int y, Color32 c) { if (x >= 0 && x < W && y >= 0 && y < H) m_Px[(H - 1 - y) * W + x] = c; }
        void Rect(int x, int y, int w, int h, Color32 c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Px(x + i, y + j, c); }
        /// <summary>A field point (y down from under the bar) to a pixel.</summary>
        static Vector2Int F(Vector2 p) => new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y) + Bar);

        void Draw(float now)
        {
            MakeTex();
            Redraws++;
            // where everything is between the last two snapshots (they come ~15 a second)
            float a = s_Have ? Mathf.Clamp01((now - s_Cur.At) / SendEvery) : 1f;
            var cur = s_Cur;
            if (s_Have)
            {
                if (!m_GoalsKnown) { m_GoalsKnown = true; m_GoalsSeen = cur.Goals; } // (came in mid-game: no flash for old goals)
                else if (cur.Goals != m_GoalsSeen) { m_GoalsSeen = cur.Goals; m_GoalFlash = now; m_GoalTeam = cur.GoalTeam; }
            }
            bool playing = s_Have && cur.Dots.Count > 0;

            // the grass (checked), darker at the edges
            for (int y = Bar; y < H; y++)
                for (int x = 0; x < W; x++)
                    m_Px[(H - 1 - y) * W + x] = (x == 0 || x == W - 1 || y == Bar || y == H - 1) ? k_Edge : (((x >> 3) + ((y - Bar) >> 3)) & 1) == 0 ? k_GrassA : k_GrassB;
            Rect(0, 0, W, Bar, k_Bar);
            // the machines: a box in the team's colour with a light going round on top
            int teams = Mathf.Clamp(Cfg.TeamCount, 2, 4);
            for (int t = 0; t < teams; t++)
            {
                var m = F(k_Machines[t]);
                bool side = t < 2;
                int mw = side ? 6 : 12, mh = side ? 12 : 6;
                Rect(m.x - mw / 2, m.y - mh / 2, mw, mh, TeamC(t, 0.45f));
                Rect(m.x - mw / 2 + 1, m.y - mh / 2 + 1, mw - 2, mh - 2, TeamC(t));
                bool blink = Mathf.Repeat(now * 2f + t * 0.5f, 1f) < 0.5f;
                Rect(m.x - 1, m.y - 1, 2, 2, blink ? k_White : TeamC(t, 0.6f));
            }
            // the trees (stumps while they grow back)
            for (int i = 0; i < k_Trees.Length; i++)
            {
                var p = F(k_Trees[i]);
                bool up = !s_Have || (cur.Trees & (1 << i)) != 0;
                if (!up) { Rect(p.x - 1, p.y, 3, 2, k_Stump); continue; }
                Rect(p.x - 1, p.y + 1, 2, 3, k_Trunk);
                Rect(p.x - 2, p.y - 4, 5, 5, k_Leaf);
                Rect(p.x - 1, p.y - 5, 3, 1, k_Leaf);
                Rect(p.x - 1, p.y - 3, 2, 2, k_LeafHi);
            }
            // the players: little aliens in their team's colours (you: blinking white outline and a marker over you)
            byte myId = (byte)((NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0) & 255);
            bool meDown = false;
            if (s_Have)
                foreach (var d in cur.Dots)
                {
                    var pos = d.Pos;
                    foreach (var o in s_Prev.Dots) if (o.Id == d.Id) { if ((o.Pos - d.Pos).sqrMagnitude < 400f) pos = Vector2.Lerp(o.Pos, d.Pos, a); break; } // (back on their side after being shot: no sliding across)
                    var p = F(pos);
                    bool me = d.Id == myId && PlayerNet.Local != null;
                    if ((d.Flags & FDead) != 0)
                    {
                        // shot: a cross where they fell (blinking), until they're back on their side
                        var xc = Mathf.Repeat(now * 4f, 1f) < 0.6f ? TeamC(d.Team, 0.7f) : k_Black;
                        for (int k = -2; k <= 2; k++) { Px(p.x + k, p.y + k, xc); Px(p.x + k, p.y - k, xc); }
                        if (me) { meDown = true; Px(p.x, p.y - 6, k_White); Rect(p.x - 1, p.y - 7, 3, 1, k_White); }
                        continue;
                    }
                    if (me && Mathf.Repeat(now * 3f, 1f) < 0.6f) Rect(p.x - 3, p.y - 3, 6, 6, k_White);
                    // the bow: a pixel or two the way they're facing (where the next arrow goes)
                    {
                        var fd = Dir(d.Face);
                        Px(Mathf.RoundToInt(p.x - 0.5f + fd.x * 3.4f), Mathf.RoundToInt(p.y - 0.5f + fd.y * 3.4f), k_Trunk);
                        Px(Mathf.RoundToInt(p.x - 0.5f + fd.x * 4.4f), Mathf.RoundToInt(p.y - 0.5f + fd.y * 4.4f), k_Stump);
                    }
                    bool stun = (d.Flags & FStun) != 0;
                    Rect(p.x - 2, p.y - 2, 4, 4, stun && Mathf.Repeat(now * 8f, 1f) < 0.5f ? TeamC(d.Team, 0.4f) : TeamC(d.Team));
                    Px(p.x - 1, p.y - 1, k_Black); Px(p.x + 1, p.y - 1, k_Black); // (the eyes)
                    if (me) { Px(p.x, p.y - 6, k_White); Rect(p.x - 1, p.y - 7, 3, 1, k_White); }
                    if ((d.Flags & FChop) != 0 && Mathf.Repeat(now * 10f, 1f) < 0.5f) { Px(p.x + 3, p.y - 3, k_Stump); Px(p.x - 3, p.y - 4, k_Stump); }
                }
            // the ball (glowing; over the carrier's head)
            {
                var bp = s_Have ? Vector2.Lerp(s_Prev.Ball, cur.Ball, a) : BallHome;
                if (s_Have && (cur.Ball - s_Prev.Ball).sqrMagnitude > 400f) bp = cur.Ball; // (back to the middle: no sliding across)
                var p = F(bp);
                if (Mathf.Repeat(now * 4f, 1f) < 0.5f) Rect(p.x - 2, p.y - 2, 5, 5, new Color32(150, 120, 20, 255));
                Rect(p.x - 1, p.y - 1, 3, 3, k_Ball);
                Px(p.x - 1, p.y - 1, k_White);
            }
            // the arrows: a white tip, a wooden shaft, the shooter's colour on the flights (stuck ones quiver in the tree)
            if (s_Have)
                foreach (var ar in cur.Arrows)
                {
                    var dir = Dir(ar.Ang);
                    bool stuck = (ar.Flags & 1) != 0;
                    var at = ar.Pos;
                    if (!stuck) at += dir * ArrowSpeed * Mathf.Min(now - cur.At, SendEvery); // (on between snapshots)
                    else if (Mathf.Repeat(now * 12f, 1f) < 0.5f) at -= dir * 0.6f;
                    var fl = TeamC((ar.Flags >> 1) & 3);
                    for (int k = 0; k < 6; k++)
                    {
                        var q = at - dir * k;
                        Px(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y) + Bar, k == 0 ? k_White : k >= 4 ? fl : k_ArrowWood);
                    }
                }
            if (meDown)
            {
                Rect(W / 2 - 22, Bar + 3, 44, 9, k_Black);
                Text("SHOT!", W / 2 - 9, Bar + 5, new Color32(255, 90, 80, 255));
            }
            // the scores along the top: everyone playing (by team), their colour and their own points - you underlined
            {
                s_Board.Clear();
                if (playing) s_Board.AddRange(cur.Dots);
                s_Board.Sort((p, q) => p.Team != q.Team ? p.Team.CompareTo(q.Team) : p.Id.CompareTo(q.Id));
                int width = 0;
                foreach (var d in s_Board) width += 7 + d.Score.ToString().Length * 4 - 1;
                int gap = width + 5 * Mathf.Max(0, s_Board.Count - 1) <= W - 6 ? 5 : 2; // (tighter with a lot of them)
                int x = 3, shown = 0;
                foreach (var d in s_Board)
                {
                    string sc = d.Score.ToString();
                    if (x + 7 + sc.Length * 4 - 1 > W - 2) break; // (no room for any more)
                    Rect(x, 2, 5, 5, TeamC(d.Team));
                    Px(x + 1, 3, k_Black); Px(x + 3, 3, k_Black); // (their little alien's eyes)
                    int end = Text(sc, x + 7, 2, k_White) - 1;
                    if (d.Id == myId && PlayerNet.Local != null) Rect(x, 8, end - x, 1, k_White);
                    x = end + gap;
                    shown++;
                }
                BoardShown = shown;
                if (!playing) Text("WARM UP", 3, 2, new Color32(170, 170, 190, 255));
            }
            // nobody playing: the attract screen
            if (!playing && Mathf.Repeat(now, 1.2f) < 0.85f)
            {
                Rect(W / 2 - 34, H / 2 + 14, 68, 11, k_Black);
                Text("CLICK TO PLAY", W / 2 - 25, H / 2 + 17, k_Ball);
            }
            // a goal: the screen flashes the team's colour
            float gf = now - m_GoalFlash;
            if (gf < 1.2f)
            {
                var c = TeamC(m_GoalTeam);
                if (Mathf.Repeat(gf * 6f, 1f) < 0.5f)
                    for (int y = Bar; y < H; y++) for (int x = 0; x < W; x++) if (((x + y) & 3) == 0) m_Px[(H - 1 - y) * W + x] = c;
                Rect(W / 2 - 18, H / 2 - 5, 36, 11, k_Black);
                Text("GOAL +5", W / 2 - 14, H / 2 - 2, c);
            }
            // scanlines, and the telly's glow from the picture
            long sr = 0, sg = 0, sb = 0;
            int hash = 17;
            for (int y = 0; y < H; y++)
            {
                bool dark = (y & 1) == 1;
                for (int x = 0; x < W; x++)
                {
                    int i = (H - 1 - y) * W + x;
                    var c = m_Px[i];
                    if (dark) { c.r = (byte)(c.r * 0.8f); c.g = (byte)(c.g * 0.8f); c.b = (byte)(c.b * 0.8f); m_Px[i] = c; }
                    if ((x & 7) == 0 && (y & 7) == 0) { sr += c.r; sg += c.g; sb += c.b; }
                    hash = hash * 31 + c.r + c.g * 7 + c.b * 13; // (tests: every pixel - a small dot moving between the glow's samples still changes it)
                }
            }
            int samples = (W / 8) * (H / 8);
            Glow = new Color(sr / (255f * samples), sg / (255f * samples), sb / (255f * samples));
            PictureHash = hash;
            m_Tex.SetPixels32(m_Px);
            m_Tex.Apply(false);
        }

        /// <summary>Draws text in the 3x5 pixel font at (x, y) (top left); returns the x after it.</summary>
        int Text(string s, int x, int y, Color32 c)
        {
            foreach (char ch in s)
            {
                var g = Glyph(ch);
                for (int r = 0; r < 5; r++)
                    for (int col = 0; col < 3; col++)
                        if ((g[r] & (4 >> col)) != 0) Px(x + col, y + r, c);
                x += 4;
            }
            return x;
        }

        // ================================================================== the 3x5 pixel font

        static readonly Dictionary<char, int[]> s_Font = new Dictionary<char, int[]>
        {
            ['0'] = new[] { 7, 5, 5, 5, 7 }, ['1'] = new[] { 2, 6, 2, 2, 7 }, ['2'] = new[] { 7, 1, 7, 4, 7 }, ['3'] = new[] { 7, 1, 7, 1, 7 },
            ['4'] = new[] { 5, 5, 7, 1, 1 }, ['5'] = new[] { 7, 4, 7, 1, 7 }, ['6'] = new[] { 7, 4, 7, 5, 7 }, ['7'] = new[] { 7, 1, 1, 2, 2 },
            ['8'] = new[] { 7, 5, 7, 5, 7 }, ['9'] = new[] { 7, 5, 7, 1, 7 },
            ['A'] = new[] { 2, 5, 7, 5, 5 }, ['B'] = new[] { 6, 5, 6, 5, 6 }, ['C'] = new[] { 3, 4, 4, 4, 3 }, ['D'] = new[] { 6, 5, 5, 5, 6 },
            ['E'] = new[] { 7, 4, 6, 4, 7 }, ['F'] = new[] { 7, 4, 6, 4, 4 }, ['G'] = new[] { 3, 4, 5, 5, 3 }, ['H'] = new[] { 5, 5, 7, 5, 5 },
            ['I'] = new[] { 7, 2, 2, 2, 7 }, ['J'] = new[] { 1, 1, 1, 5, 2 }, ['K'] = new[] { 5, 5, 6, 5, 5 }, ['L'] = new[] { 4, 4, 4, 4, 7 },
            ['M'] = new[] { 5, 7, 7, 5, 5 }, ['N'] = new[] { 6, 5, 5, 5, 5 }, ['O'] = new[] { 2, 5, 5, 5, 2 }, ['P'] = new[] { 6, 5, 6, 4, 4 },
            ['Q'] = new[] { 2, 5, 5, 6, 3 }, ['R'] = new[] { 6, 5, 6, 5, 5 }, ['S'] = new[] { 3, 4, 2, 1, 6 }, ['T'] = new[] { 7, 2, 2, 2, 2 },
            ['U'] = new[] { 5, 5, 5, 5, 7 }, ['V'] = new[] { 5, 5, 5, 5, 2 }, ['W'] = new[] { 5, 5, 7, 7, 5 }, ['X'] = new[] { 5, 5, 2, 5, 5 },
            ['Y'] = new[] { 5, 5, 2, 2, 2 }, ['Z'] = new[] { 7, 1, 2, 4, 7 },
            ['!'] = new[] { 2, 2, 2, 0, 2 }, ['+'] = new[] { 0, 2, 7, 2, 0 }, ['-'] = new[] { 0, 0, 7, 0, 0 }, [':'] = new[] { 0, 2, 0, 2, 0 },
        };
        static readonly int[] k_Blank = { 0, 0, 0, 0, 0 };

        /// <summary>A character of the 3x5 pixel font: five rows, three bits each (4 = the left column).</summary>
        public static int[] Glyph(char c) => s_Font.TryGetValue(char.ToUpperInvariant(c), out var g) ? g : k_Blank;
    }
}
