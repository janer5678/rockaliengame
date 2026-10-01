using System;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Tutorial mode (the Primitive rules, always a small flat Plains map, the clock stopped): short, simple steps, and
    /// every one has to be DONE to move on (no skipping). A small panel on the left says what to do, a marker points at
    /// what it's about, and a one-line hint shows up if a step takes a while.
    /// Each player goes through their own steps; the match-level bits run on the server (ServerTick): the clock stays
    /// stopped, players who join later are dropped straight into their base, and the glass wall drops once EVERY player
    /// has walked up to it (or 90 s after the first one got there, so nobody waits forever).
    /// </summary>
    public static class Tutorial
    {
        public static bool On => Cfg.Rules == GameRules.Tutorial;

        // things the game tells the tutorial about
        public static int WeakHits, SpearThrows;

        /// <summary>Seconds before a stuck step shows its hint.</summary>
        public const float HintAfter = 40f;
        /// <summary>Server: the wall drops this long after the first player reached it, even if others haven't yet.</summary>
        public const float WallWait = 90f;

        class Step
        {
            public string Id, Title, Body, Goal, Hint;
            public Func<string> BodyF;              // a body that changes (overrides Body)
            public Func<bool> Done;
            public Func<string> Progress;           // e.g. "12 / 50 wood"
            public Func<Vector3?> Target;           // where the on-screen marker points
            public string TargetLabel;
            public Action Enter;
        }

        static List<Step> s_Steps;
        static int s_Index;
        static float s_DoneAt = -1f, s_StepStart;
        static bool s_Finished;
        // what things were when the step started
        static float s_Moved;
        static Vector3 s_LastPos;
        static int s_Wood0, s_Weak0, s_Spear0, s_Berry0, s_LastBerries;
        static float s_SprintTime, s_GuardTime;
        static bool s_Jumped, s_Slid, s_Ate;
        // server
        static float s_FirstAtWall = -1f;
        static readonly Dictionary<ulong, float> s_NextHome = new Dictionary<ulong, float>();

        public static int StepNumber => s_Index + 1;
        public static int StepCount => s_Steps != null ? s_Steps.Count : 0;
        public static string StepId => s_Steps != null && !s_Finished ? s_Steps[s_Index].Id : (s_Finished ? "done" : "");
        public static bool Finished => s_Finished;
        public static bool HasEnterKey => false; // there's no Enter to read on or skip any more (AutoTest)

        public static void Reset()
        {
            s_Steps = null;
            s_Index = 0;
            s_DoneAt = -1f;
            s_Finished = false;
            WeakHits = SpearThrows = 0;
            s_FirstAtWall = -1f;
            s_NextHome.Clear();
        }

        static string K(Bind b) => $"<b><color=#ffd24a>[{Binds.Name(b)}]</color></b>";
        static string Hi(string s) => $"<b><color=#9fe0ff>{s}</color></b>";

        static PlayerNet Me => PlayerNet.Local;
        static int Count(Item i) => Me != null ? Me.Count(i) : 0;
        static int Team => Me != null ? Me.Team.Value : 0;
        static NetGame G => NetGame.Instance;
        static bool WallDown => G != null && G.S != GameState.Waiting && G.S != GameState.PreBall;

        static int Pieces(PieceType t)
        {
            int n = 0;
            foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.Team.Value == Team && s.PType == t) n++;
            return n;
        }

        static Vector3? Nearest(byte kind)
        {
            var me = Me;
            if (me == null) return null;
            ResourceNode best = null;
            float bd = float.MaxValue;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Kind.Value != kind || n.Amount.Value <= 0) continue;
                float d = (n.transform.position - me.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = n; }
            }
            return best != null ? best.transform.position + Vector3.up * 1.4f : (Vector3?)null;
        }

        static Vector3? BaseSpot => Cfg.BaseCenter[Team] - Cfg.BackDir(Team) * 7f + Vector3.up * (Cfg.BaseY + 0.5f);
        static Vector3? MachineSpot => Cfg.MachinePos(Team) + Vector3.up * 1.6f;
        static Vector3? BallSpot => Ball.Instance != null ? Ball.Instance.transform.position + Vector3.up * 0.6f : (Vector3?)null;

        /// <summary>The glass wall, just on your side of the middle of the map (where all the walls meet).</summary>
        public static Vector3 WallSpot(int team)
        {
            var p = Cfg.BackDir(team) * 2.5f;
            p.y = MapBuilder.Height(p.x, p.z) + 1.5f;
            return p;
        }

        /// <summary>Within a few metres of the glass wall: some spot close by is on another side of it.</summary>
        public static bool NearWall(Vector3 p)
        {
            int r = Cfg.RegionOf(p);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                if (Cfg.RegionOf(p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6f) != r) return true;
            }
            return false;
        }

        static bool LookingAt(Vector3 target, float maxAngle)
        {
            var me = Me;
            var d = target - me.transform.position;
            d.y = 0;
            return d.sqrMagnitude > 0.01f && Vector3.Angle(me.transform.forward, d) < maxAngle;
        }

        static int PlayersNotAtWall()
        {
            int n = 0;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && !p.TutAtWall.Value) n++;
            return n;
        }

        static void Build()
        {
            var pc = PlayerController.Local;
            s_Steps = new List<Step>
            {
                new Step
                {
                    Id = "machine", Title = "Hi, little alien!",
                    Body = "Behind you is " + Hi("your machine") + ". Put the ball in it to win!",
                    Goal = "Turn around and look at your machine",
                    Hint = "Move the mouse to turn around.",
                    Done = () => LookingAt(MachineSpot.Value, 25f),
                    Target = () => MachineSpot, TargetLabel = "YOUR MACHINE",
                },
                new Step
                {
                    Id = "walk", Title = "Walk",
                    Body = $"Walk with {K(Bind.Forward)} {K(Bind.Left)} {K(Bind.Back)} {K(Bind.Right)}.",
                    Goal = "Walk 8 metres", Progress = () => $"{Mathf.Min(8, s_Moved):0} / 8 m",
                    Done = () => s_Moved >= 8f,
                },
                new Step
                {
                    Id = "sprint", Title = "Run and jump",
                    Body = $"Hold {K(Bind.Sprint)} to run. Tap {K(Bind.Jump)} to jump.",
                    Goal = "Run 2 seconds and jump",
                    Progress = () => $"run {Mathf.Min(2f, s_SprintTime):0.0}/2 s   jump {(s_Jumped ? "✔" : "-")}",
                    Hint = $"Hold {Binds.Name(Bind.Sprint)} while you walk forward.",
                    Done = () => s_SprintTime >= 2f && s_Jumped,
                },
                new Step
                {
                    Id = "slide", Title = "Slide",
                    Body = $"While running, press {K(Bind.Crouch)} to slide. Wheee!",
                    Goal = "Run, then slide",
                    Hint = $"Run first ({Binds.Name(Bind.Sprint)} + {Binds.Name(Bind.Forward)}), then tap {Binds.Name(Bind.Crouch)}.",
                    Done = () => s_Slid,
                },
                new Step
                {
                    Id = "chop", Title = "Chop a tree",
                    Body = $"Your rock can chop! Walk to a " + Hi("tree") + $" and hit it with {K(Bind.Attack)}.",
                    Goal = "Get 20 wood", Progress = () => $"{Mathf.Max(0, Count(Item.Wood) - s_Wood0)} / 20",
                    Hint = "Follow the yellow marker to a tree. Get close and click.",
                    Done = () => Count(Item.Wood) - s_Wood0 >= 20,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Id = "weak", Title = "Hit the X",
                    Body = "See the orange " + Hi("X") + " on the tree? Hit it for double wood!",
                    Goal = "Hit the X 2 times", Progress = () => $"{Mathf.Min(2, WeakHits - s_Weak0)} / 2",
                    Hint = "Aim right at the orange X when you swing. It moves after each hit.",
                    Done = () => WeakHits - s_Weak0 >= 2,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Id = "wood", Title = "More wood",
                    Body = "Wood makes everything. Keep chopping!",
                    Goal = "Have 100 wood", Progress = () => $"{Mathf.Min(100, Count(Item.Wood))} / 100",
                    Done = () => Count(Item.Wood) >= 100,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Id = "bag", Title = "Your bag",
                    Body = $"Press {K(Bind.Inventory)} to open your bag.",
                    Goal = "Open your bag",
                    Done = () => pc != null && pc.MenuOpen,
                },
                new Step
                {
                    Id = "hatchet", Title = "Make an axe",
                    Body = "On the right, click " + Hi("Craft") + " next to " + Hi("Stone Hatchet") + ". It chops 3x faster!",
                    Goal = "Craft a Stone Hatchet",
                    Hint = $"Open your bag with {Binds.Name(Bind.Inventory)}. Crafting is on the right. It costs 50 wood.",
                    Done = () => Count(Item.Hatchet) >= 1,
                },
                new Step
                {
                    Id = "axechop", Title = "Use the axe",
                    Body = $"Close the bag ({K(Bind.Inventory)}). Pick the axe with its number key, then chop!",
                    Goal = "Get 60 wood with the axe", Progress = () => $"{Mathf.Max(0, Count(Item.Wood) - s_Wood0)} / 60",
                    Hint = $"Keys {Binds.Name(Bind.Hotbar1)}-{Binds.Name(Bind.Hotbar7)} or the mouse wheel pick what you hold.",
                    Done = () => Count(Item.Wood) - s_Wood0 >= 60 && Me.HeldItem == Item.Hatchet,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Id = "home", Title = "Go home",
                    Body = "You can only build in " + Hi("your base") + ". Go back!",
                    Goal = "Walk into your base",
                    Done = () => Cfg.BaseTeamAt(Me.transform.position) == Team,
                    Target = () => BaseSpot, TargetLabel = "YOUR BASE",
                },
                new Step
                {
                    Id = "plan", Title = "Building plan",
                    Body = $"Open your bag ({K(Bind.Inventory)}) and craft a " + Hi("Building Plan") + ".",
                    Goal = "Craft a Building Plan",
                    Hint = "It costs 5 wood. Craft it inside your base.",
                    Done = () => Count(Item.BuildingPlan) >= 1,
                },
                new Step
                {
                    Id = "floor", Title = "Build a floor",
                    Body = $"Hold the plan, look at the ground and click {K(Bind.Attack)}. Green means it fits!",
                    Goal = "Place a foundation",
                    Hint = "Pick the plan on your hotbar. Look at the grid in your base.",
                    Done = () => Pieces(PieceType.Foundation) >= 1,
                    Target = () => BaseSpot, TargetLabel = "BUILD HERE",
                },
                new Step
                {
                    Id = "wall", Title = "Build a wall",
                    Body = $"Hold {K(Bind.Aim)} for the wheel. Pick " + Hi("Wall") + ", then click the floor's edge.",
                    Goal = "Place a wall",
                    Hint = $"Keep holding {Binds.Name(Bind.Aim)}, move the mouse onto Wall, let go.",
                    Done = () => Pieces(PieceType.Wall) >= 1,
                },
                new Step
                {
                    Id = "door", Title = "Build a door",
                    Body = "Pick " + Hi("Doorway") + $" on the wheel and place it. {K(Bind.Interact)} opens the door.",
                    Goal = "Place a doorway",
                    Hint = "Doorways go on a floor's edge, just like walls.",
                    Done = () => Pieces(PieceType.Doorway) >= 1,
                },
                new Step
                {
                    Id = "stone", Title = "Get stone",
                    Body = "Grey rocks give " + Hi("stone") + ". Hit them!",
                    Goal = "Have 50 stone", Progress = () => $"{Mathf.Min(50, Count(Item.Stone))} / 50",
                    Done = () => Count(Item.Stone) >= 50,
                    Target = () => Nearest(ResourceNode.Boulder), TargetLabel = "STONE",
                },
                new Step
                {
                    Id = "spear", Title = "Make a spear",
                    Body = "In your base, craft a " + Hi("Spear") + $". Click {K(Bind.Attack)} to poke!",
                    Goal = "Craft a spear",
                    Hint = "It costs 100 wood. Chop more if you need it.",
                    Done = () => Count(Item.Spear) >= 1,
                    Target = () => BaseSpot, TargetLabel = "YOUR BASE",
                },
                new Step
                {
                    Id = "throw", Title = "Throw it",
                    Body = $"Hold the spear. Hold {K(Bind.Aim)}, click {K(Bind.Attack)} to throw. {K(Bind.Interact)} picks it up.",
                    Goal = "Throw the spear and pick it up",
                    Progress = () => SpearThrows - s_Spear0 > 0 ? (Count(Item.Spear) > 0 ? "got it ✔" : "thrown ✔ - pick it up") : "",
                    Hint = "Walk to the spear, look at it and press " + Binds.Name(Bind.Interact) + ".",
                    Done = () => SpearThrows - s_Spear0 > 0 && Count(Item.Spear) > 0,
                },
                new Step
                {
                    Id = "ram", Title = "Battering ram",
                    Body = "A " + Hi("ram") + " smashes enemy walls. Craft one!",
                    Goal = "Craft a battering ram",
                    Hint = "It costs 125 wood and 50 stone. Craft it in your base.",
                    Done = () => Count(Item.Ram) >= 1,
                },
                new Step
                {
                    Id = "berry", Title = "Food",
                    Body = "Berry bushes heal you. Press " + K(Bind.Interact) + " on a bush.",
                    Goal = "Pick a berry",
                    Done = () => Count(Item.Berry) > s_Berry0,
                    Target = () => Nearest(ResourceNode.Bush), TargetLabel = "BERRIES",
                },
                new Step
                {
                    Id = "eat", Title = "Eat",
                    Body = $"Hold the berry and press {K(Bind.Aim)}. Yum!",
                    Goal = "Eat a berry",
                    Hint = "Pick the berry on your hotbar first. No berry? Get one from a bush.",
                    Done = () => s_Ate,
                    Target = () => Count(Item.Berry) > 0 ? null : Nearest(ResourceNode.Bush), TargetLabel = "BERRIES",
                },
                new Step
                {
                    Id = "glass", Title = "The glass wall",
                    Body = "A big " + Hi("glass wall") + " splits the map. Walk up to it!",
                    Goal = "Walk up to the glass wall",
                    Done = () => NearWall(Me.transform.position) || WallDown,
                    Target = () => WallSpot(Team), TargetLabel = "GLASS WALL",
                },
                new Step
                {
                    Id = "drop", Title = "The wall drops!",
                    BodyF = () =>
                    {
                        int left = PlayersNotAtWall();
                        return WallDown || left == 0 ? "Down it goes! A " + Hi("ball") + " falls in the middle."
                            : $"It drops when everyone is here. Waiting for {left} more player{(left == 1 ? "" : "s")}...";
                    },
                    Goal = "Wait for the wall to drop",
                    Hint = "Slow friends? The wall drops by itself soon.",
                    Enter = () => { if (Me != null) Me.TutorialRpc(1); },
                    Done = () => WallDown,
                    Target = () => WallSpot(Team), TargetLabel = "GLASS WALL",
                },
                new Step
                {
                    Id = "toball", Title = "Get the ball",
                    Body = "The " + Hi("ball") + " is in the middle. Run to it!",
                    Goal = "Run to the ball",
                    Done = () => Me.CarryingBall || (Ball.Instance != null && Vector3.Distance(Ball.Instance.transform.position, Me.transform.position) < 6f),
                    Target = () => BallSpot, TargetLabel = "BALL",
                },
                new Step
                {
                    Id = "grab", Title = "Grab it",
                    BodyF = () =>
                    {
                        var c = Ball.Instance != null ? Ball.Instance.Carrier : null;
                        return c != null && c != Me ? "Someone else has it! Hit them - they drop it." : $"Look at the ball and press {K(Bind.Interact)}.";
                    },
                    Goal = "Pick up the ball",
                    Hint = "Get close, look right at the ball and press " + Binds.Name(Bind.Interact) + ".",
                    Done = () => Me.CarryingBall || (Ball.Instance != null && Ball.Instance.SocketTeam.Value == Team),
                    Target = () => BallSpot, TargetLabel = "BALL",
                },
                new Step
                {
                    Id = "score", Title = "Take it home",
                    BodyF = () => Me.CarryingBall ? $"Run home! Press {K(Bind.Interact)} at your machine to put it in."
                        : "You lost it! Get it back - " + K(Bind.Interact) + " takes it out of any machine.",
                    Goal = "Put the ball in your machine",
                    Hint = $"Stand at your machine and press {Binds.Name(Bind.Interact)}.",
                    Done = () => Ball.Instance != null && Ball.Instance.SocketTeam.Value == Team,
                    Target = () => Me.CarryingBall ? MachineSpot : BallSpot, TargetLabel = "",
                },
                new Step
                {
                    Id = "guard", Title = "Guard it!",
                    BodyF = () => Ball.Instance != null && Ball.Instance.SocketTeam.Value == Team
                        ? $"Enemies can steal it with {K(Bind.Interact)}! Stay by your machine."
                        : "It got stolen! Get it back into your machine.",
                    Goal = "Guard your machine for 5 seconds",
                    Progress = () => $"{Mathf.Min(5f, s_GuardTime):0} / 5 s",
                    Done = () => s_GuardTime >= 5f,
                    Target = () => Ball.Instance != null && Ball.Instance.SocketTeam.Value != Team ? BallSpot : MachineSpot, TargetLabel = "",
                },
            };
        }

        static void EnterStep()
        {
            var me = Me;
            s_StepStart = Time.time;
            s_DoneAt = -1f;
            if (me != null)
            {
                s_LastPos = me.transform.position;
                s_Wood0 = me.Count(Item.Wood);
                s_Berry0 = s_LastBerries = me.Count(Item.Berry);
            }
            s_Moved = 0f;
            s_Weak0 = WeakHits;
            s_Spear0 = SpearThrows;
            s_SprintTime = s_GuardTime = 0f;
            s_Jumped = s_Slid = s_Ate = false;
            s_Steps[s_Index].Enter?.Invoke();
        }

        /// <summary>Every frame on the local player (PlayerController).</summary>
        public static void Tick(PlayerController pc, PlayerNet me)
        {
            if (!On || me == null || pc == null || G == null || G.S == GameState.Waiting) return;
            if (s_Steps == null) { Build(); s_Index = 0; EnterStep(); }
            if (s_Finished) return;
            var st = s_Steps[s_Index];
            var p = me.transform.position;
            var d = p - s_LastPos;
            d.y = 0;
            if (d.magnitude < 3f) s_Moved += d.magnitude; // (not teleports)
            s_LastPos = p;
            if (pc.Sprinting) s_SprintTime += Time.deltaTime;
            if (!pc.Grounded) s_Jumped = true;
            if (pc.Sliding) s_Slid = true;
            int berries = me.Count(Item.Berry);
            if (berries < s_LastBerries && !me.Dead.Value) s_Ate = true;
            s_LastBerries = berries;
            if (Ball.Instance != null && Ball.Instance.SocketTeam.Value == me.Team.Value && Vector3.Distance(p, Cfg.MachinePos(me.Team.Value)) < 9f) s_GuardTime += Time.deltaTime;

            // you have to actually do it: a finished goal ticks, then moves on by itself (there's no skipping)
            if (s_DoneAt < 0 && Time.time - s_StepStart > 0.3f && SafeDone(st))
            {
                s_DoneAt = Time.time;
                Sfx.Play2D(Sfx.Ding, 0.6f);
            }
            if (s_DoneAt >= 0 && Time.time - s_DoneAt > 1.1f) Advance();
        }

        static bool SafeDone(Step st)
        {
            try { return st.Done(); } catch (Exception) { return false; }
        }

        static void Advance()
        {
            Sfx.Play2D(Sfx.Pop, 0.4f);
            if (s_Index >= s_Steps.Count - 1) { s_Finished = true; return; }
            s_Index++;
            EnterStep();
        }

        /// <summary>AutoTest only: jump straight to a step (by its id) to test the later parts.</summary>
        public static bool TestGoTo(string id)
        {
            if (s_Steps == null) return false;
            for (int i = 0; i < s_Steps.Count; i++)
                if (s_Steps[i].Id == id) { s_Index = i; s_Finished = false; EnterStep(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ server

        /// <summary>Server, every frame (NetGame.Update): keep the clock stopped, drop late joiners into the match, and
        /// drop the glass wall once every player has walked up to it (or WallWait seconds after the first one did).</summary>
        public static void ServerTick(NetGame g)
        {
            if (!On || g == null || !g.IsServer) return;
            if (g.S == GameState.Waiting || g.S == GameState.GameOver) return;
            if (!g.TimerPaused.Value) g.TimerPaused.Value = true;
            // players who joined after the start are still in the waiting stadium: straight into their base
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p.Dead.Value) continue;
                if (Vector3.Distance(p.transform.position, Cfg.ArenaCenter) > 60f) continue;
                if (s_NextHome.TryGetValue(p.OwnerClientId, out var at) && Time.time < at) continue;
                s_NextHome[p.OwnerClientId] = Time.time + 2f;
                p.ServerSendHome();
                p.NotifyPublic("Welcome to the tutorial! Follow the steps on the left.");
            }
            if (g.S != GameState.PreBall) return;
            int there = 0, total = 0;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned) continue;
                total++;
                if (p.TutAtWall.Value) there++;
            }
            if (there == 0) { s_FirstAtWall = -1f; return; }
            if (s_FirstAtWall < 0) s_FirstAtWall = Time.time;
            if (there >= total || Time.time - s_FirstAtWall > WallWait)
                g.PhaseEnd.Value = g.NetworkManager.ServerTime.Time - 1.0; // NetGame drops the wall and the ball this frame
        }

        /// <summary>Server: a player reached the glass wall step.</summary>
        public static void ServerAtWall(PlayerNet p)
        {
            var g = G;
            if (!On || g == null || p == null || p.TutAtWall.Value) return;
            p.TutAtWall.Value = true;
            if (g.S != GameState.PreBall) return;
            int left = 0;
            foreach (var o in PlayerNet.All) if (o != null && o.IsSpawned && !o.TutAtWall.Value) left++;
            if (left > 0) g.Broadcast($"{Cfg.TeamLabel(p.Team.Value)} is at the glass wall - it drops when everyone gets there ({left} to go)");
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>The tutorial panel (left side), a goal strip while menus are open, and the marker.</summary>
        public static void Draw(float k, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            var pc = PlayerController.Local;
            if (!On || s_Steps == null || pc == null) return;
            float sw = Screen.width, sh = Screen.height;
            if (s_Finished)
            {
                if (pc.MenuOpen || pc.Paused) return;
                float fw = 330 * k, fx = 14, fy = 230 * k;
                var fb = new GUIStyle(small) { wordWrap = true, richText = true };
                string txt = "Win: have the ball " + Hi("in your machine") + " when the clock ends.\n" + Hi("Esc") + " > Leave game, then host a real match!";
                float fh = 30 * k + 30 * k + fb.CalcHeight(new GUIContent(txt), fw - 24 * k) + 12 * k;
                fill(new Rect(fx, fy, fw, fh), new Color(0.04f, 0.06f, 0.1f, 0.82f));
                fill(new Rect(fx, fy, 4 * k, fh), new Color(0.49f, 1f, 0.49f, 0.9f));
                shadowed(new Rect(fx + 14 * k, fy + 6 * k, fw - 24 * k, 24 * k), $"<size={Mathf.RoundToInt(13 * k)}><color=#7dff7d>TUTORIAL COMPLETE</color></size>", small);
                shadowed(new Rect(fx + 14 * k, fy + 28 * k, fw - 24 * k, 30 * k), "<b>You did it!</b>", new GUIStyle(label) { richText = true });
                GUI.Label(new Rect(fx + 14 * k, fy + 60 * k, fw - 24 * k, fh - 60 * k), txt, fb);
                return;
            }
            var st = s_Steps[s_Index];
            bool done = s_DoneAt >= 0;
            string prog = st.Progress != null && !done ? SafeProgress(st) : "";
            string goal = (done ? "<color=#7dff7d>✔ " : "<color=#ffd24a>☐ ") + st.Goal + "</color>" + (prog.Length > 0 ? $"  <color=#dddddd>{prog}</color>" : "");
            bool hint = !done && st.Hint != null && Time.time - s_StepStart > HintAfter;

            if (pc.MenuOpen || pc.Paused)
            {
                // just the goal, along the bottom, while the inventory is up
                if (pc.Paused) return;
                var gr = new Rect(sw / 2 - 330 * k, sh - 40 * k, 660 * k, 32 * k);
                fill(gr, new Color(0, 0, 0, 0.75f));
                var c = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
                shadowed(gr, $"<b>TUTORIAL</b>  {goal}", c);
                return;
            }

            float w = 330 * k, x = 14, y = 230 * k;
            string bodyText = SafeBody(st);
            var body = new GUIStyle(small) { wordWrap = true, richText = true };
            var titleSt = new GUIStyle(label) { wordWrap = true, richText = true };
            float bodyH = body.CalcHeight(new GUIContent(bodyText), w - 24 * k);
            string hintText = hint ? $"<color=#aaaaaa><i>Hint: {st.Hint}</i></color>" : null;
            float hintH = hint ? body.CalcHeight(new GUIContent(hintText), w - 24 * k) + 2 * k : 0f;
            float h = 26 * k + 28 * k + bodyH + 30 * k + hintH + 6 * k;
            fill(new Rect(x, y, w, h), new Color(0.04f, 0.06f, 0.1f, 0.82f));
            fill(new Rect(x, y, 4 * k, h), new Color(1f, 0.82f, 0.29f, 0.9f));
            float pct = (float)s_Index / Mathf.Max(1, s_Steps.Count);
            fill(new Rect(x, y + h - 3 * k, w * pct, 3 * k), new Color(1f, 0.82f, 0.29f, 0.8f));
            shadowed(new Rect(x + 14 * k, y + 5 * k, w - 24 * k, 22 * k), $"<size={Mathf.RoundToInt(12 * k)}><color=#ffd24a>TUTORIAL  {s_Index + 1} / {s_Steps.Count}</color></size>", small);
            shadowed(new Rect(x + 14 * k, y + 24 * k, w - 24 * k, 30 * k), $"<b>{st.Title}</b>", titleSt);
            GUI.Label(new Rect(x + 14 * k, y + 54 * k, w - 24 * k, bodyH), bodyText, body);
            shadowed(new Rect(x + 14 * k, y + 58 * k + bodyH, w - 24 * k, 26 * k), goal, body);
            if (hint) GUI.Label(new Rect(x + 14 * k, y + 84 * k + bodyH, w - 24 * k, hintH), hintText, body);

            // the marker: a diamond over what the step is about, pinned to the screen edge when it's off screen
            Vector3? target = null;
            try { target = st.Target?.Invoke(); } catch (Exception) { }
            var cam = Camera.main;
            if (target.HasValue && cam != null)
            {
                var t = target.Value;
                var sp = cam.WorldToScreenPoint(t);
                bool behind = sp.z < 0;
                if (behind) sp = -sp;
                var pos = new Vector2(sp.x, sh - sp.y);
                float m = 40 * k;
                bool off = behind || pos.x < m || pos.x > sw - m || pos.y < m || pos.y > sh - m;
                if (off)
                {
                    var c = new Vector2(sw / 2, sh / 2);
                    var dir = (pos - c).normalized;
                    if (dir.sqrMagnitude < 0.01f) dir = Vector2.down;
                    float s = Mathf.Min((sw / 2 - m) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (sh / 2 - m) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                    pos = c + dir * s;
                    pos.y = Mathf.Min(pos.y, sh - 110 * k); // clear of the health bar and hotbar
                }
                float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.15f;
                float sz = 16 * k * pulse;
                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, pos);
                fill(new Rect(pos.x - sz / 2, pos.y - sz / 2, sz, sz), new Color(1f, 0.82f, 0.29f, 0.95f));
                fill(new Rect(pos.x - sz / 4, pos.y - sz / 4, sz / 2, sz / 2), new Color(0.1f, 0.1f, 0.1f, 0.9f));
                GUI.matrix = old;
                float dist = Me != null ? Vector3.Distance(Me.transform.position, t) : 0f;
                string lab = (st.TargetLabel ?? "") + $" {dist:0}m";
                var ls = new GUIStyle(small) { alignment = TextAnchor.UpperCenter, wordWrap = false };
                shadowed(new Rect(pos.x - 100 * k, pos.y + 12 * k, 200 * k, 22 * k), $"<b><color=#ffd24a>{lab}</color></b>", ls);
            }
        }

        static string SafeBody(Step st)
        {
            if (st.BodyF == null) return st.Body;
            try { return st.BodyF(); } catch (Exception) { return st.Body ?? ""; }
        }

        static string SafeProgress(Step st)
        {
            try { return st.Progress(); } catch (Exception) { return ""; }
        }
    }
}
