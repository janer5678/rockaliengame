using System;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Tutorial mode (the Primitive rules, solo, no clock): a guided walk through the base game, one step at a time.
    /// Every step says what's going on and why, with a goal that ticks itself off when you do it (or Enter to read on /
    /// skip), and a marker on screen pointing at what the step is about (a tree, your base, the machine, the ball...).
    /// Runs on the local player; the few things it needs the server for (keeping the clock stopped, dropping the wall)
    /// go through PlayerNet.TutorialRpc.
    /// </summary>
    public static class Tutorial
    {
        public static bool On => Cfg.Rules == GameRules.Tutorial;

        // things the game tells the tutorial about
        public static int WeakHits, SpearThrows;

        class Step
        {
            public string Title, Body, Goal;
            public Func<bool> Done;                 // null: a reading step (Enter to go on)
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
        static float s_Yaw0, s_Moved;
        static Vector3 s_LastPos;
        static int s_Wood0, s_Weak0, s_Spear0, s_Stone0;
        static float s_SprintTime;
        static bool s_Jumped, s_Slid, s_SawWheel;

        public static int StepNumber => s_Index + 1;
        public static int StepCount => s_Steps != null ? s_Steps.Count : 0;

        public static void Reset()
        {
            s_Steps = null;
            s_Index = 0;
            s_DoneAt = -1f;
            s_Finished = false;
            WeakHits = SpearThrows = 0;
        }

        static string K(Bind b) => $"<b><color=#ffd24a>[{Binds.Name(b)}]</color></b>";
        static string Hi(string s) => $"<b><color=#9fe0ff>{s}</color></b>";

        static int Count(Item i) => PlayerNet.Local != null ? PlayerNet.Local.Count(i) : 0;
        static int Team => PlayerNet.Local != null ? PlayerNet.Local.Team.Value : 0;

        static int Pieces(PieceType t)
        {
            int n = 0;
            foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.Team.Value == Team && s.PType == t) n++;
            return n;
        }

        static Vector3? Nearest(byte kind)
        {
            var me = PlayerNet.Local;
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

        static void Build()
        {
            var pc = PlayerController.Local;
            s_Steps = new List<Step>
            {
                new Step
                {
                    Title = "Welcome to Rock Base Brawl!",
                    Body = "You're a little alien who just crash-landed. Look behind you: that silver slab is your " + Hi("bedrock") + ", and the arch on it is your " + Hi("alien machine") + ".\n\n" +
                           "Here's the whole game in one breath: " + Hi("gather") + " wood and stone, " + Hi("build") + " a base around your machine, " + Hi("craft") + " tools and weapons, " +
                           "and when the giant " + Hi("glass wall") + " in the middle drops, grab the " + Hi("ball") + " and get it into " + Hi("your machine") + ". Whoever has the ball in their machine when the clock hits zero wins.\n\n" +
                           "In a real match an enemy is doing the same on the other side. In this tutorial the clock is " + Hi("stopped") + " - take your time.",
                    Target = () => MachineSpot, TargetLabel = "YOUR MACHINE",
                },
                new Step
                {
                    Title = "Look around",
                    Body = "Move the " + Hi("mouse") + " to look around. Have a proper look - turn all the way round once.\n\nThe tinted square of ground with the grid lines is " + Hi("your base") + ": you can only build in there.",
                    Goal = "Turn around (180°)",
                    Done = () => Mathf.Abs(Mathf.DeltaAngle(s_Yaw0, PlayerNet.Local.transform.eulerAngles.y)) > 150f,
                },
                new Step
                {
                    Title = "Walk",
                    Body = $"Walk with {K(Bind.Forward)} {K(Bind.Left)} {K(Bind.Back)} {K(Bind.Right)}. You walk where you look.",
                    Goal = "Walk 8 metres", Progress = () => $"{Mathf.Min(8, s_Moved):0} / 8 m",
                    Done = () => s_Moved >= 8f,
                },
                new Step
                {
                    Title = "Sprint and jump",
                    Body = $"Hold {K(Bind.Sprint)} while walking forward to " + Hi("sprint") + $", and tap {K(Bind.Jump)} to " + Hi("jump") + ". Sprinting is how you get around the map fast - and how you start a slide.",
                    Goal = "Sprint for 2 seconds and jump",
                    Progress = () => $"sprint {Mathf.Min(2f, s_SprintTime):0.0}/2 s   jump {(s_Jumped ? "✔" : "-")}",
                    Done = () => s_SprintTime >= 2f && s_Jumped,
                },
                new Step
                {
                    Title = "Crouch and slide",
                    Body = $"{K(Bind.Crouch)} crouches: you're slower but quieter and smaller.\n\nWhile you're " + Hi("sprinting") + $", press {K(Bind.Crouch)} to " + Hi("slide") +
                           " - a quick burst forward that keeps your speed. Slides go further " + Hi("downhill") + $" and die out going uphill. You can jump out of a slide with {K(Bind.Jump)} and keep the speed.",
                    Goal = "Sprint, then slide",
                    Done = () => s_Slid,
                },
                new Step
                {
                    Title = "Your trusty rock",
                    Body = "See the rock in your hands? You " + Hi("always") + " have it - whenever the hotbar slot you're on is empty, you're holding the rock.\n\n" +
                           $"The " + Hi("hotbar") + $" is the row of 7 slots at the bottom. Pick a slot with {K(Bind.Hotbar1)}-{K(Bind.Hotbar7)} or the mouse wheel.\n\n" +
                           "Your rock hits things, gathers wood and stone, and even fights - badly. Let's use it.",
                },
                new Step
                {
                    Title = "Chop a tree",
                    Body = $"Walk up to a " + Hi("tree") + $" (follow the marker) and hit it with {K(Bind.Attack)}. Every hit puts " + Hi("wood") + " straight into your inventory - watch the numbers pop up in the bottom right.\n\nA whole tree is 300 wood, and felling it gives a bonus +100 (TIMBER!).",
                    Goal = "Gather 20 wood", Progress = () => $"{Mathf.Max(0, Count(Item.Wood) - s_Wood0)} / 20 wood",
                    Done = () => Count(Item.Wood) - s_Wood0 >= 20,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Title = "Hit the weak spot",
                    Body = "After your first hit, an orange " + Hi("X") + " appears on the trunk. That's the tree's " + Hi("weak spot") +
                           ": hitting it gives " + Hi("double wood") + ", plays a chime, and the X jumps to a new spot facing you. Chain them and the chimes climb up a scale.\n\nAim right at the X when you swing.",
                    Goal = "Hit the X twice", Progress = () => $"{Mathf.Min(2, WeakHits - s_Weak0)} / 2",
                    Done = () => WeakHits - s_Weak0 >= 2,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Title = "Stock up",
                    Body = "Keep chopping. Wood pays for nearly everything: tools, weapons and every building piece.\n\nA rock is slow, though - with " + Hi("100 wood") + " we'll make something better.",
                    Goal = "Have 100 wood", Progress = () => $"{Mathf.Min(100, Count(Item.Wood))} / 100 wood",
                    Done = () => Count(Item.Wood) >= 100,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Title = "Your inventory",
                    Body = $"Press {K(Bind.Inventory)} to open your " + Hi("inventory") + ". You'll see:\n" +
                           "• your " + Hi("21 inventory slots") + " and the " + Hi("hotbar") + " under them,\n" +
                           "• " + Hi("CRAFTING") + " on the right: what you can make, what it costs, and a Craft button.\n\n" +
                           "Drag items to move them, shift-click to quick-move, drag one outside the window to drop it on the ground.",
                    Goal = $"Open the inventory ({Binds.Name(Bind.Inventory)})",
                    Done = () => pc != null && pc.MenuOpen,
                },
                new Step
                {
                    Title = "Craft a hatchet",
                    Body = "In CRAFTING, click " + Hi("Craft") + " next to the " + Hi("Stone Hatchet") + " (50 wood). It chops wood three times as fast as your rock.\n\n" +
                           "Spears and hatchets can be crafted " + Hi("anywhere") + ". Everything else only " + Hi("inside your own base") + ".",
                    Goal = "Craft a Stone Hatchet",
                    Done = () => Count(Item.Hatchet) >= 1,
                },
                new Step
                {
                    Title = "Chop with the hatchet",
                    Body = $"Close the inventory ({K(Bind.Inventory)}), then pick the hatchet on your hotbar (its number key, or scroll). Now go chop - feel the difference.",
                    Goal = "Gather 60 wood with the hatchet", Progress = () => $"{Mathf.Max(0, Count(Item.Wood) - s_Wood0)} / 60 wood",
                    Done = () => Count(Item.Wood) - s_Wood0 >= 60 && PlayerNet.Local.HeldItem == Item.Hatchet,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Title = "Get back to base",
                    Body = "Building and most crafting happen " + Hi("in your base") + ". Head back - follow the marker.",
                    Goal = "Go back into your base",
                    Done = () => Cfg.BaseTeamAt(PlayerNet.Local.transform.position) == Team,
                    Target = () => BaseSpot, TargetLabel = "YOUR BASE",
                },
                new Step
                {
                    Title = "The building plan",
                    Body = $"Open the inventory ({K(Bind.Inventory)}) and craft a " + Hi("Building Plan") + " (5 wood). It's how you build: hold it and you get a see-through " + Hi("ghost") + " of the piece where you're looking - green means it fits, red means it doesn't.",
                    Goal = "Craft a Building Plan",
                    Done = () => Count(Item.BuildingPlan) >= 1,
                },
                new Step
                {
                    Title = "Lay a foundation",
                    Body = $"Hold the building plan, look at the ground in your base, and click {K(Bind.Attack)} to place a " + Hi("foundation") + " (15 wood).\n\n" +
                           "Pieces snap to a 3 m grid. Foundations are the floor of your base: walls, doorways and stairs all need something under them, and if you destroy what holds a piece up, it collapses.",
                    Goal = "Place a foundation",
                    Done = () => Pieces(PieceType.Foundation) >= 1,
                    Target = () => BaseSpot, TargetLabel = "BUILD HERE",
                },
                new Step
                {
                    Title = "The building wheel",
                    Body = $"Now " + Hi("hold") + $" {K(Bind.Aim)} with the plan: the " + Hi("building wheel") + " opens. Move the mouse onto " + Hi("Wall") + " and let go.\n\n" +
                           "Clockwise from the top: Foundation, Ceiling, Wall, Window, Demolish, Stairs, Doorway, Upgrade.\n\nThen look at the edge of your foundation and click to place a " + Hi("wall") + ".",
                    Goal = "Place a wall",
                    Done = () => Pieces(PieceType.Wall) >= 1,
                },
                new Step
                {
                    Title = "A way in",
                    Body = "A base needs a door you can lock. Pick " + Hi("Doorway") + $" on the wheel and place one on another edge. Walk up to its door and press {K(Bind.Interact)} to open and close it.\n\n" +
                           $"{K(Bind.Rotate)} turns stairs, {K(Bind.Demolish)} takes down one of your own pieces (half the wood back) and {K(Bind.Upgrade)} upgrades a piece to " + Hi("stone") + " - 3-4 times the health, and melee barely scratches it.",
                    Goal = "Place a doorway",
                    Done = () => Pieces(PieceType.Doorway) >= 1,
                },
                new Step
                {
                    Title = "Mine some stone",
                    Body = "The grey boulders are " + Hi("stone") + ". Hit one (it has a sparkly weak spot too). Stone upgrades your walls and goes into tougher tools.\n\n" +
                           "Get 50 - you'll need it for the battering ram.",
                    Goal = "Have 50 stone", Progress = () => $"{Mathf.Min(50, Count(Item.Stone))} / 50 stone",
                    Done = () => Count(Item.Stone) >= 50,
                    Target = () => Nearest(ResourceNode.Boulder), TargetLabel = "STONE",
                },
                new Step
                {
                    Title = "Arm yourself: the spear",
                    Body = $"Back in your base, craft a " + Hi("Spear") + $" (100 wood). With it held, {K(Bind.Attack)} stabs (35 damage, long reach).\n\n" +
                           $"To " + Hi("throw") + $" it, hold {K(Bind.Aim)} to wind up (longer = harder), then click {K(Bind.Attack)}. A thrown spear does up to 60 and sticks in whoever it hits.",
                    Goal = "Craft a spear",
                    Done = () => Count(Item.Spear) >= 1,
                    Target = () => BaseSpot, TargetLabel = "YOUR BASE",
                },
                new Step
                {
                    Title = "Throw it",
                    Body = $"Hold {K(Bind.Aim)}, then click {K(Bind.Attack)} to throw the spear at something. Then walk to it, look at it and press {K(Bind.Interact)} to pick it back up - thrown spears stay where they land.",
                    Goal = "Throw the spear and pick it back up",
                    Progress = () => SpearThrows - s_Spear0 > 0 ? (Count(Item.Spear) > 0 ? "picked up ✔" : "thrown ✔ - now pick it up") : "not thrown yet",
                    Done = () => SpearThrows - s_Spear0 > 0 && Count(Item.Spear) > 0,
                },
                new Step
                {
                    Title = "The battering ram",
                    Body = "Raiding! Craft a " + Hi("Battering Ram") + " (125 wood, 50 stone). Hold it and " + Hi("hold") + $" {K(Bind.Attack)} next to an " + Hi("enemy") +
                           " piece: after a wind-up it slams - a wooden piece breaks instantly, a stone one is knocked down to wood. One ram, one hit, so bring a few.\n\nYou can't ram your own base, so just craft it for now.",
                    Goal = "Craft a battering ram",
                    Done = () => Count(Item.Ram) >= 1,
                },
                new Step
                {
                    Title = "Fighting",
                    Body = "Everything you hold hurts people: rock 12, hatchet 14, spear 35. " + Hi("Headshots do double") + ". You have " + Hi("100 health") + ".\n\n" +
                           $"Hurt? Pick a " + Hi("berry bush") + $" ({K(Bind.Interact)}) and eat berries with {K(Bind.Aim)} (+25 health).\n\n" +
                           "Die and " + Hi("everything you carry spills on the ground") + " for anyone to grab, and you respawn after a few seconds - at your base, or anywhere out in the wild once the wall is down.",
                },
                new Step
                {
                    Title = "The glass wall drops",
                    Body = "In a real match the glass wall comes down after 5 minutes. Let's drop it now - watch the middle of the map: the " + Hi("ball") + " falls from the sky.\n\nFollow the marker to the ball.",
                    Enter = () => { if (PlayerNet.Local != null) PlayerNet.Local.TutorialRpc(1); },
                    Goal = "Get to the ball",
                    Done = () => Ball.Instance != null && Vector3.Distance(Ball.Instance.transform.position, PlayerNet.Local.transform.position) < 6f,
                    Target = () => BallSpot, TargetLabel = "BALL",
                },
                new Step
                {
                    Title = "Grab the ball",
                    Body = $"Look at the ball and press {K(Bind.Interact)} to pick it up. While you carry it, it fills the bottom of your screen and you can't fight - you're the target now!",
                    Goal = "Pick up the ball",
                    Done = () => PlayerNet.Local.CarryingBall,
                    Target = () => BallSpot, TargetLabel = "BALL",
                },
                new Step
                {
                    Title = "Score it",
                    Body = $"Carry it home to " + Hi("your machine") + $" and press {K(Bind.Interact)} at it to put the ball in the " + Hi("socket") + $" (or throw it with {K(Bind.Attack)} - near the socket it snaps in). A beam of light shoots into the sky while it's in.\n\n" +
                           $"Careful: enemies can take it back out with {K(Bind.Interact)}. That's why you build walls around your machine!",
                    Goal = "Put the ball in your machine",
                    Done = () => Ball.Instance != null && Ball.Instance.SocketTeam.Value == Team,
                    Target = () => PlayerNet.Local.CarryingBall ? MachineSpot : BallSpot, TargetLabel = "",
                },
                new Step
                {
                    Title = "That's the game!",
                    Body = "When the clock runs out, whoever has the ball " + Hi("in their machine") + " wins. If nobody does, it's " + Hi("sudden death") + ": everyone into the stadium, rocks only, first kill wins.\n\n" +
                           "Good to know:\n• Rocks out in the wild, horses (craft a saddle in other modes), airdrops from alien ships with crazy loot.\n" +
                           "• " + Hi("Esc") + " pauses: settings, every key (rebind them), and Leave game.\n• Other modes (Classic, Arsenal, Builder...) add more crafting and power items.\n\n" +
                           "You're ready. Keep playing here as long as you like, or leave and host a real match!",
                },
            };
        }

        static void EnterStep()
        {
            var me = PlayerNet.Local;
            s_StepStart = Time.time;
            s_DoneAt = -1f;
            if (me != null)
            {
                s_Yaw0 = me.transform.eulerAngles.y;
                s_LastPos = me.transform.position;
                s_Wood0 = me.Count(Item.Wood);
                s_Stone0 = me.Count(Item.Stone);
            }
            s_Moved = 0f;
            s_Weak0 = WeakHits;
            s_Spear0 = SpearThrows;
            s_SprintTime = 0f;
            s_Jumped = s_Slid = s_SawWheel = false;
            s_Steps[s_Index].Enter?.Invoke();
        }

        /// <summary>Every frame on the local player (PlayerController).</summary>
        public static void Tick(PlayerController pc, PlayerNet me)
        {
            if (!On || me == null || pc == null || NetGame.Instance == null || NetGame.Instance.S == GameState.Waiting) return;
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
            if (pc.WheelOpen) s_SawWheel = true;

            bool menu = pc.MenuOpen || pc.Paused;
            if (s_DoneAt < 0 && st.Done != null && Time.time - s_StepStart > 0.3f && SafeDone(st))
            {
                s_DoneAt = Time.time;
                Sfx.Play2D(Sfx.Ding, 0.6f);
            }
            // a finished goal moves on by itself after a moment; Enter reads on (or skips a goal)
            bool next = (s_DoneAt >= 0 && Time.time - s_DoneAt > 1.1f) || (!menu && Input.GetKeyDown(KeyCode.Return) && Time.time - s_StepStart > 0.25f);
            if (next) Advance();
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

        /// <summary>The tutorial panel (left side), a goal strip while menus are open, and the marker.</summary>
        public static void Draw(float k, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            var pc = PlayerController.Local;
            if (!On || s_Steps == null || pc == null) return;
            float sw = Screen.width, sh = Screen.height;
            if (s_Finished)
            {
                shadowed(new Rect(16, sh * 0.32f, 460 * k, 30 * k), "<color=#7dff7d><b>TUTORIAL COMPLETE</b></color>  <color=#bbbbbb>(keep playing, or Esc > Leave game)</color>", small);
                return;
            }
            var st = s_Steps[s_Index];
            bool done = s_DoneAt >= 0;
            string goal = st.Done == null ? "<color=#bbbbbb>Press <b>Enter</b> to continue</color>"
                : (done ? "<color=#7dff7d>✔ " : "<color=#ffd24a>☐ ") + st.Goal + "</color>" + (st.Progress != null && !done ? $"   <color=#dddddd>{SafeProgress(st)}</color>" : "");

            if (pc.MenuOpen || pc.Paused)
            {
                // just the goal, along the bottom, while the inventory / pause menu is up
                if (pc.Paused) return;
                var gr = new Rect(sw / 2 - 360 * k, sh - 40 * k, 720 * k, 32 * k);
                fill(gr, new Color(0, 0, 0, 0.75f));
                var c = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
                shadowed(gr, $"<b>TUTORIAL</b>  {StripTags(st.Title)}:  {goal}", c);
                return;
            }

            float w = 440 * k, x = 14, y = 230 * k;
            var body = new GUIStyle(small) { wordWrap = true, richText = true };
            var titleSt = new GUIStyle(label) { wordWrap = true, richText = true };
            float bodyH = body.CalcHeight(new GUIContent(st.Body), w - 24 * k);
            float h = 30 * k + 30 * k + bodyH + 46 * k;
            fill(new Rect(x, y, w, h), new Color(0.04f, 0.06f, 0.1f, 0.82f));
            fill(new Rect(x, y, 4 * k, h), new Color(1f, 0.82f, 0.29f, 0.9f));
            float pct = (float)s_Index / Mathf.Max(1, s_Steps.Count - 1);
            fill(new Rect(x, y + h - 3 * k, w * pct, 3 * k), new Color(1f, 0.82f, 0.29f, 0.8f));
            shadowed(new Rect(x + 14 * k, y + 6 * k, w - 24 * k, 24 * k), $"<size={Mathf.RoundToInt(13 * k)}><color=#ffd24a>TUTORIAL  {s_Index + 1} / {s_Steps.Count}</color></size>", small);
            shadowed(new Rect(x + 14 * k, y + 28 * k, w - 24 * k, 30 * k), $"<b>{st.Title}</b>", titleSt);
            GUI.Label(new Rect(x + 14 * k, y + 60 * k, w - 24 * k, bodyH), st.Body, body);
            shadowed(new Rect(x + 14 * k, y + 64 * k + bodyH, w - 24 * k, 26 * k), goal + (st.Done != null && !done ? "  <color=#888888>(Enter: skip)</color>" : ""), body);

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
                float dist = PlayerNet.Local != null ? Vector3.Distance(PlayerNet.Local.transform.position, t) : 0f;
                string lab = (st.TargetLabel ?? "") + $" {dist:0}m";
                var ls = new GUIStyle(small) { alignment = TextAnchor.UpperCenter, wordWrap = false };
                shadowed(new Rect(pos.x - 100 * k, pos.y + 12 * k, 200 * k, 22 * k), $"<b><color=#ffd24a>{lab}</color></b>", ls);
            }
        }

        static string SafeProgress(Step st)
        {
            try { return st.Progress(); } catch (Exception) { return ""; }
        }

        static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
    }
}
