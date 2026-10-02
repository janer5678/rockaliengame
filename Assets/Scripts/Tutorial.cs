using System;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Something the tutorial keeps locked until the step that teaches it (Tutorial.Allows). Looking around with the mouse
    /// is never locked. Outside the tutorial everything is always allowed.
    /// </summary>
    public enum TutFeature
    {
        Move, Jump, Sprint, Crouch, Slide,
        Hit,        // LMB: hit, chop, mine
        HotbarHud,  // the hotbar strip and the "+30 Wood" pop-ups
        Inventory,  // TAB opens the bag
        Craft,      // the crafting list in the bag (items show up one by one: Tutorial.AllowsItem)
        Hotbar,     // 1-7 and the mouse wheel pick what you hold
        Build,      // placing building pieces with the plan (and R / F)
        Aim,        // RMB (the building wheel first; the spear throw and eating unlock later)
        Interact,   // E
        Demolish,   // X, and the wheel's Demolish
        Throw,      // spear: hold RMB + LMB
        Deploy,     // putting down a workbench / chest / high wall
        Workbench,  // the workbench shop (and everything it sells)
        Health,     // the health bar (and armour / buffs under it)
        Eat,        // RMB with food
    }

    /// <summary>
    /// Tutorial mode (the classic rules, always a small flat Plains map, the clock stopped, no airdrops): short, simple
    /// steps, and every one has to be DONE to move on (no skipping). The game is revealed a bit at a time: at the start
    /// you can only look around; each control, HUD part and craftable item unlocks on the step that teaches it
    /// (Allows / AllowsItem, checked by Binds, PlayerController, the HUD and - for crafting, building and the workbench -
    /// the server, using each player's own synced step PlayerNet.TutStep).
    /// A small panel on the left says what to do, a marker points at what it's about, and a one-line hint shows up if a
    /// step takes a while. Each player goes through their own steps; the match-level bits run on the server (ServerTick):
    /// the clock stays stopped, players who join later are dropped straight into their base, and the glass wall drops
    /// once EVERY player has walked up to it (or 90 s after the first one got there, so nobody waits forever).
    ///
    /// When a game mechanic changes, update the steps (and what they unlock) here to match.
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
        /// <summary>PlayerNet.TutStep when the tutorial is finished (everything unlocked).</summary>
        public const byte FinishedStep = 255;

        class Step
        {
            public string Id, Title, Body, Goal, Hint;
            public Func<string> BodyF;              // a body that changes (overrides Body)
            public Func<bool> Done;
            public Func<string> Progress;           // e.g. "12 / 50 wood"
            public Func<Vector3?> Target;           // where the on-screen marker points
            public string TargetLabel;
            public Action Enter;
            public TutFeature[] Unlocks;            // what this step lets you do from now on
            public Item[] Items;                    // starter items that show up in the crafting list from now on
        }

        static List<Step> s_Steps;
        static readonly int[] s_FeatureAt = new int[Enum.GetValues(typeof(TutFeature)).Length];
        static readonly Dictionary<Item, int> s_ItemAt = new Dictionary<Item, int>();
        static bool s_Started;
        static int s_Index;
        static float s_DoneAt = -1f, s_StepStart, s_LockedAt = -10f, s_NextHungry;
        static bool s_Finished;
        // what things were when the step started
        static float s_Moved;
        static Vector3 s_LastPos;
        static int s_Wood0, s_Weak0, s_Spear0, s_Berry0, s_LastBerries, s_Bench0, s_WallMax;
        static float s_SprintTime, s_GuardTime, s_CrouchTime;
        static bool s_Jumped, s_Slid, s_Ate;
        // server
        static float s_FirstAtWall = -1f;
        static readonly Dictionary<ulong, float> s_NextHome = new Dictionary<ulong, float>();

        static List<Step> Steps { get { if (s_Steps == null) Build(); return s_Steps; } }
        public static int StepNumber => s_Index + 1;
        public static int StepCount => Steps.Count;
        public static string StepId => s_Finished ? "done" : s_Started ? Steps[s_Index].Id : "";
        public static bool Finished => s_Finished;
        public static bool HasEnterKey => false; // there's no Enter to read on or skip any more (AutoTest)
        /// <summary>The step this player is on (FinishedStep once done), as synced to the server in PlayerNet.TutStep.</summary>
        public static byte MyStep => s_Finished ? FinishedStep : (byte)Mathf.Min(s_Started ? s_Index : 0, 254);

        public static void Reset()
        {
            s_Steps = null; // (rebuilt with the current key names)
            s_Started = false;
            s_Index = 0;
            s_DoneAt = -1f;
            s_Finished = false;
            WeakHits = SpearThrows = 0;
            s_FirstAtWall = -1f;
            s_NextHome.Clear();
        }

        // ------------------------------------------------------------------ the gates

        /// <summary>Is this unlocked for the local player yet? Always true outside the tutorial.</summary>
        public static bool Allows(TutFeature f) => !On || Unlocked(FeatureAt(f), MyStep);

        /// <summary>Server: is this unlocked for player p (by the step their client says they're on)?</summary>
        public static bool AllowsFor(PlayerNet p, TutFeature f) => !On || p == null || Unlocked(FeatureAt(f), p.TutStep.Value);

        /// <summary>Does this item show in the crafting list (and can it be crafted / bought) yet?</summary>
        public static bool AllowsItem(Item i) => !On || Unlocked(ItemAt(i), MyStep);
        public static bool AllowsItemFor(PlayerNet p, Item i) => !On || p == null || Unlocked(ItemAt(i), p.TutStep.Value);

        static bool Unlocked(int at, byte step) => step == FinishedStep || at <= step;

        static int FeatureAt(TutFeature f)
        {
            if (s_Steps == null) Build();
            return s_FeatureAt[(int)f];
        }

        static int ItemAt(Item i)
        {
            if (s_Steps == null) Build();
            if (!Cfg.IsStarter(i)) return FeatureAt(TutFeature.Workbench); // everything else is bought at the workbench
            return s_ItemAt.TryGetValue(i, out int at) ? at : int.MaxValue; // starters nobody teaches: once you're done
        }

        /// <summary>Keybinds: is this key's action unlocked yet?</summary>
        public static bool BindAllowed(Bind b)
        {
            if (!On) return true;
            var f = FeatureOf(b);
            return f == null || Allows(f.Value);
        }

        static TutFeature? FeatureOf(Bind b)
        {
            switch (b)
            {
                case Bind.Forward: case Bind.Back: case Bind.Left: case Bind.Right: return TutFeature.Move;
                case Bind.Jump: return TutFeature.Jump;
                case Bind.Sprint: return TutFeature.Sprint;
                case Bind.Crouch: return TutFeature.Crouch;
                case Bind.Slide: return TutFeature.Slide;
                case Bind.Attack: return TutFeature.Hit;
                case Bind.Aim: return TutFeature.Aim;
                case Bind.Interact: return TutFeature.Interact;
                case Bind.Inventory: return TutFeature.Inventory;
                case Bind.Rotate: case Bind.Upgrade: return TutFeature.Build;
                case Bind.Demolish: return TutFeature.Demolish;
                case Bind.PushToTalk: return null;
                default: return b >= Bind.Hotbar1 && b <= Bind.Hotbar7 ? TutFeature.Hotbar : (TutFeature?)null;
            }
        }

        static string FeatureLabel(TutFeature f)
        {
            switch (f)
            {
                case TutFeature.Move: return $"{K(Bind.Forward)}{K(Bind.Left)}{K(Bind.Back)}{K(Bind.Right)} walk";
                case TutFeature.Jump: return $"{K(Bind.Jump)} jump";
                case TutFeature.Sprint: return $"{K(Bind.Sprint)} run";
                case TutFeature.Crouch: return $"{K(Bind.Crouch)} crouch";
                case TutFeature.Slide: return $"{K(Bind.Slide)} slide";
                case TutFeature.Hit: return $"{K(Bind.Attack)} hit";
                case TutFeature.HotbarHud: return "your hotbar";
                case TutFeature.Inventory: return $"{K(Bind.Inventory)} bag";
                case TutFeature.Craft: return "crafting";
                case TutFeature.Hotbar: return $"{K(Bind.Hotbar1)}-{K(Bind.Hotbar7)} pick a slot";
                case TutFeature.Build: return "building";
                case TutFeature.Aim: return $"{K(Bind.Aim)} building wheel";
                case TutFeature.Interact: return $"{K(Bind.Interact)} use";
                case TutFeature.Demolish: return $"{K(Bind.Demolish)} demolish";
                case TutFeature.Throw: return "spear throw";
                case TutFeature.Deploy: return "putting things down";
                case TutFeature.Workbench: return "the workbench";
                case TutFeature.Health: return "health bar";
                case TutFeature.Eat: return $"{K(Bind.Aim)} eat";
            }
            return f.ToString();
        }

        // ------------------------------------------------------------------ helpers

        static string K(Bind b) => $"<b><color=#ffd24a>[{Binds.Name(b)}]</color></b>";
        static string Hi(string s) => $"<b><color=#9fe0ff>{s}</color></b>";

        static PlayerNet Me => PlayerNet.Local;
        static PlayerController PC => PlayerController.Local;
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

        static int WallPieces => Pieces(PieceType.Wall) + Pieces(PieceType.Doorway) + Pieces(PieceType.Window);

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

        /// <summary>Short of wood for the next thing? Point at a tree; otherwise at `then`.</summary>
        static Vector3? TreeIfShort(int wood, Vector3? then) => Count(Item.Wood) < wood ? Nearest(ResourceNode.Tree) : then;
        static string WoodNeed(int wood) => Count(Item.Wood) < wood ? $"wood {Count(Item.Wood)} / {wood} - chop more!" : "";
        static string Price(Item i) { int r = Cfg.RecipeIndex(i); return r < 0 ? "" : CostText(Cfg.GetRecipe(r)); }
        static string CostText(Recipe r) => r.Stone > 0 ? $"{r.Wood} wood and {r.Stone} stone" : $"{r.Wood} wood";
        static int WoodOf(Item i) { int r = Cfg.RecipeIndex(i); return r < 0 ? 0 : Cfg.GetRecipe(r).Wood; }

        static Vector3? BaseSpot => Cfg.BaseCenter[Team] - Cfg.BackDir(Team) * 7f + Vector3.up * (Cfg.BaseY + 0.5f);
        static Vector3? MachineSpot => Cfg.MachinePos(Team) + Vector3.up * 1.6f;
        static Vector3? BallSpot => Ball.Instance != null ? Ball.Instance.transform.position + Vector3.up * 0.6f : (Vector3?)null;
        static Container MyBench => Workbench.ForTeam(Team);
        static Vector3? BenchSpot => MyBench != null ? Workbench.Top(MyBench) + Vector3.up * 0.3f : Workbench.DefaultPos(Team) + Vector3.up * 0.5f;

        /// <summary>Things that only come from the workbench (and armour, which goes straight on).</summary>
        static int BenchThings() => Count(Item.Pickaxe) + Count(Item.Crossbow) + Count(Item.Chainsaw) + Count(Item.Saddle) + (Me != null && Me.ArmorHp.Value > 0 ? 1 : 0);

        static bool BenchBusyOrDone()
        {
            var c = MyBench;
            if (c == null) return false;
            var w = Workbench.Of(c);
            if (w != null && w.Working) return true;
            if (G != null)
            {
                var top = Workbench.Top(c);
                foreach (var it in G.Items) if ((it.Pos - top).sqrMagnitude < 1f) return true;
            }
            return BenchThings() > s_Bench0;
        }

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

        // ------------------------------------------------------------------ the steps

        static void Build()
        {
            s_Steps = new List<Step>
            {
                // ---- you can only look around ----
                new Step
                {
                    Id = "machine", Title = "Hi, little alien!",
                    Body = "Move the " + Hi("mouse") + " to look around. Behind you is " + Hi("your machine") + ". Put the ball in it to win!",
                    Goal = "Turn around and look at your machine",
                    Hint = "Move the mouse left or right to turn around.",
                    Done = () => LookingAt(MachineSpot.Value, 25f),
                    Target = () => MachineSpot, TargetLabel = "YOUR MACHINE",
                },
                // ---- moving ----
                new Step
                {
                    Id = "walk", Title = "Walk",
                    Body = $"Now you can walk! Use {K(Bind.Forward)} {K(Bind.Left)} {K(Bind.Back)} {K(Bind.Right)}.",
                    Goal = "Walk 8 metres", Progress = () => $"{Mathf.Min(8, s_Moved):0} / 8 m",
                    Done = () => s_Moved >= 8f,
                    Unlocks = new[] { TutFeature.Move },
                },
                new Step
                {
                    Id = "sprint", Title = "Run and jump",
                    Body = $"Hold {K(Bind.Sprint)} to run. Tap {K(Bind.Jump)} to jump.",
                    Goal = "Run 2 seconds and jump",
                    Progress = () => $"run {Mathf.Min(2f, s_SprintTime):0.0}/2 s   jump {(s_Jumped ? "✔" : "-")}",
                    Hint = $"Hold {Binds.Name(Bind.Sprint)} while you walk forward.",
                    Done = () => s_SprintTime >= 2f && s_Jumped,
                    Unlocks = new[] { TutFeature.Sprint, TutFeature.Jump },
                },
                new Step
                {
                    Id = "crouch", Title = "Crouch",
                    Body = $"Hold {K(Bind.Crouch)} to crouch. You're small and your steps are silent. Sneaky!",
                    Goal = "Crouch for 1 second", Progress = () => $"{Mathf.Min(1f, s_CrouchTime):0.0} / 1 s",
                    Hint = $"Keep {Binds.Name(Bind.Crouch)} held down.",
                    Done = () => s_CrouchTime >= 1f,
                    Unlocks = new[] { TutFeature.Crouch },
                },
                new Step
                {
                    Id = "slide", Title = "Slide",
                    Body = $"While running, press {K(Bind.Slide)} to slide. Wheee! Press it mid-jump and you land in a slide; push the other way to stop in a crouch. ({K(Bind.Crouch)} only crouches.)",
                    Goal = "Run, then slide",
                    Hint = $"Run first ({Binds.Name(Bind.Sprint)} + {Binds.Name(Bind.Forward)}), then tap {Binds.Name(Bind.Slide)}.",
                    Done = () => s_Slid,
                    Unlocks = new[] { TutFeature.Slide },
                },
                // ---- hitting and gathering ----
                new Step
                {
                    Id = "chop", Title = "Chop a tree",
                    Body = "Your rock can chop! Walk to a " + Hi("tree") + $" and hit it with {K(Bind.Attack)}. The wood goes on your " + Hi("hotbar") + " (bottom).",
                    Goal = "Get 20 wood", Progress = () => $"{Mathf.Max(0, Count(Item.Wood) - s_Wood0)} / 20",
                    Hint = "Follow the yellow marker to a tree. Get close and click.",
                    Done = () => Count(Item.Wood) - s_Wood0 >= 20,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                    Unlocks = new[] { TutFeature.Hit, TutFeature.HotbarHud },
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
                // ---- the bag and crafting ----
                new Step
                {
                    Id = "bag", Title = "Your bag",
                    Body = $"Press {K(Bind.Inventory)} to open your bag. The bottom row is your hotbar.",
                    Goal = "Open your bag",
                    Done = () => PC != null && PC.MenuOpen,
                    Unlocks = new[] { TutFeature.Inventory },
                },
                new Step
                {
                    Id = "hatchet", Title = "Make an axe",
                    Body = "On the right is " + Hi("crafting") + ". Click " + Hi("CRAFT") + " next to " + Hi("Stone Hatchet") + ". It chops 3x faster!",
                    Goal = "Craft a Stone Hatchet",
                    Hint = $"Open your bag with {Binds.Name(Bind.Inventory)}. Crafting is on the right. It costs {Price(Item.Hatchet)}.",
                    Done = () => Count(Item.Hatchet) >= 1,
                    Unlocks = new[] { TutFeature.Craft }, Items = new[] { Item.Hatchet },
                },
                new Step
                {
                    Id = "axechop", Title = "Use the axe",
                    Body = $"Close the bag ({K(Bind.Inventory)}). Press the axe's number ({K(Bind.Hotbar1)}-{K(Bind.Hotbar7)}) or use the mouse wheel, then chop!",
                    Goal = "Get 100 wood with the axe", Progress = () => $"{Mathf.Max(0, Count(Item.Wood) - s_Wood0)} / 100",
                    Hint = $"Keys {Binds.Name(Bind.Hotbar1)}-{Binds.Name(Bind.Hotbar7)} or the mouse wheel pick what you hold. An empty slot is your rock.",
                    Done = () => Count(Item.Wood) - s_Wood0 >= 100 && Me.HeldItem == Item.Hatchet,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                    Unlocks = new[] { TutFeature.Hotbar },
                },
                // ---- building ----
                new Step
                {
                    Id = "home", Title = "Go home",
                    Body = "You can only build and craft big things in " + Hi("your base") + ". Go back!",
                    Goal = "Walk into your base",
                    Done = () => Cfg.BaseTeamAt(Me.transform.position) == Team,
                    Target = () => BaseSpot, TargetLabel = "YOUR BASE",
                },
                new Step
                {
                    Id = "plan", Title = "Building plan",
                    Body = $"Open your bag ({K(Bind.Inventory)}) and craft a " + Hi("Building Plan") + ".",
                    Goal = "Craft a Building Plan",
                    Hint = $"It costs {Price(Item.BuildingPlan)}. Craft it inside your base.",
                    Done = () => Count(Item.BuildingPlan) >= 1,
                    Target = () => Cfg.BaseTeamAt(Me.transform.position) == Team ? null : BaseSpot, TargetLabel = "YOUR BASE",
                    Items = new[] { Item.BuildingPlan },
                },
                new Step
                {
                    Id = "floor", Title = "Build a floor",
                    Body = $"Hold the plan, look at the ground and click {K(Bind.Attack)}. Green means it fits!",
                    Goal = "Place a foundation",
                    Hint = "Pick the plan on your hotbar. Look at the grid in your base.",
                    Done = () => Pieces(PieceType.Foundation) >= 1,
                    Target = () => BaseSpot, TargetLabel = "BUILD HERE",
                    Unlocks = new[] { TutFeature.Build },
                },
                new Step
                {
                    Id = "wall", Title = "Build a wall",
                    Body = $"Hold {K(Bind.Aim)} for the wheel. Pick " + Hi("Wall") + ", then click the floor's edge.",
                    Goal = "Place a wall",
                    Hint = $"Keep holding {Binds.Name(Bind.Aim)}, move the mouse onto Wall, let go.",
                    Done = () => Pieces(PieceType.Wall) >= 1,
                    Unlocks = new[] { TutFeature.Aim },
                },
                new Step
                {
                    Id = "door", Title = "Build a door",
                    Body = "Pick " + Hi("Doorway") + $" on the wheel and place it. {K(Bind.Interact)} opens your door - enemies can't.",
                    Goal = "Place a doorway",
                    Hint = "Doorways go on a floor's edge, just like walls.",
                    Done = () => Pieces(PieceType.Doorway) >= 1,
                    Unlocks = new[] { TutFeature.Interact },
                },
                new Step
                {
                    Id = "breakwall", Title = "Oops? Break it!",
                    BodyF = () => (s_WallMax == 0 ? "Build a wall first. " : "")
                        + "You can break " + Hi("your own") + $" walls. Hold the plan and press {K(Bind.Demolish)} on one (half the wood back), or just hit it.",
                    Goal = "Take down one of your walls",
                    Hint = $"Hold the building plan, look at your wall and press {Binds.Name(Bind.Demolish)}.",
                    Done = () => WallPieces < s_WallMax,
                    Unlocks = new[] { TutFeature.Demolish },
                },
                // ---- weapons ----
                new Step
                {
                    Id = "spear", Title = "Make a spear",
                    Body = "Craft a " + Hi("Spear") + $" in your bag. Click {K(Bind.Attack)} to poke!",
                    Goal = "Craft a spear", Progress = () => WoodNeed(WoodOf(Item.Spear)),
                    Hint = $"It costs {Price(Item.Spear)}. Chop more if you need it.",
                    Done = () => Count(Item.Spear) >= 1,
                    Target = () => TreeIfShort(WoodOf(Item.Spear), null), TargetLabel = "TREE",
                    Items = new[] { Item.Spear },
                },
                new Step
                {
                    Id = "throw", Title = "Throw it",
                    Body = $"Hold the spear. Hold {K(Bind.Aim)}, click {K(Bind.Attack)} to throw - it hits hard! {K(Bind.Interact)} picks it up.",
                    Goal = "Throw the spear and pick it up",
                    Progress = () => SpearThrows - s_Spear0 > 0 ? (Count(Item.Spear) > 0 ? "got it ✔" : "thrown ✔ - pick it up") : "",
                    Hint = "Walk to the spear, look at it and press " + Binds.Name(Bind.Interact) + ".",
                    Done = () => SpearThrows - s_Spear0 > 0 && Count(Item.Spear) > 0,
                    Unlocks = new[] { TutFeature.Throw },
                },
                new Step
                {
                    Id = "stone", Title = "Get stone",
                    Body = "Grey rocks give " + Hi("stone") + ". Hit one!",
                    Goal = "Have 10 stone", Progress = () => $"{Mathf.Min(10, Count(Item.Stone))} / 10",
                    Hint = "Follow the marker to a grey rock and hit it. Look for the sparkly star.",
                    Done = () => Count(Item.Stone) >= 10,
                    Target = () => Nearest(ResourceNode.Boulder), TargetLabel = "STONE",
                },
                // ---- the workbench ----
                new Step
                {
                    Id = "bench", Title = "The Workbench",
                    Body = "Your bag only makes the basics. Everything else comes from a " + Hi("Workbench") + ". Craft one!",
                    Goal = "Craft a Workbench", Progress = () => WoodNeed(WoodOf(Item.Workbench)),
                    Hint = $"It costs {Price(Item.Workbench)}. Craft it in your base.",
                    Done = () => Count(Item.Workbench) >= 1 || MyBench != null,
                    Target = () => TreeIfShort(WoodOf(Item.Workbench), Cfg.BaseTeamAt(Me.transform.position) == Team ? null : BaseSpot), TargetLabel = "",
                    Items = new[] { Item.Workbench },
                },
                new Step
                {
                    Id = "placebench", Title = "Put it down",
                    Body = "Hold the workbench and click to put it on the " + Hi("metal floor") + " in the middle of your base. Nothing can break it!",
                    Goal = "Place your workbench",
                    Hint = "Pick it on your hotbar. It only fits on the silver metal floor, not on the spawn spot or the ball socket.",
                    Done = () => MyBench != null,
                    Target = () => Workbench.DefaultPos(Team) + Vector3.up * 0.5f, TargetLabel = "METAL FLOOR",
                    Unlocks = new[] { TutFeature.Deploy },
                },
                new Step
                {
                    Id = "openbench", Title = "Open it",
                    Body = $"Look at your workbench and press {K(Bind.Interact)}.",
                    Goal = "Open the workbench",
                    Done = () => PC != null && PC.MenuOpen && PC.LootTarget != null && PC.LootTarget.IsWorkbench,
                    Target = () => BenchSpot, TargetLabel = "WORKBENCH",
                    Unlocks = new[] { TutFeature.Workbench },
                },
                new Step
                {
                    Id = "buy", Title = "Buy something",
                    Body = "Click the " + Hi("Stone Pickaxe") + ". The bench saws and hammers it out of a cloud of sawdust!",
                    Goal = "Buy a pickaxe", Progress = () => Count(Item.Stone) < 10 ? $"stone {Count(Item.Stone)} / 10" : "",
                    Hint = $"It costs {Price(Item.Pickaxe)}. Not enough? Get more first.",
                    Done = () => BenchBusyOrDone(),
                    Target = () => Count(Item.Stone) < 10 ? Nearest(ResourceNode.Boulder) : BenchSpot, TargetLabel = "",
                },
                new Step
                {
                    Id = "pickup", Title = "Grab it",
                    Body = $"It's lying on the bench. Look at it and press {K(Bind.Interact)}. (Anyone can grab it - guard your bench!)",
                    Goal = "Pick it up",
                    Hint = "Wait for the sawdust to clear, then look at the item on top of the bench.",
                    Done = () => BenchThings() > s_Bench0,
                    Target = () => BenchSpot, TargetLabel = "WORKBENCH",
                },
                new Step
                {
                    Id = "mine", Title = "Mine",
                    Body = "A pickaxe mines stone fast. Hold it and hit rocks!",
                    Goal = "Have 50 stone", Progress = () => $"{Mathf.Min(50, Count(Item.Stone))} / 50",
                    Done = () => Count(Item.Stone) >= 50,
                    Target = () => Nearest(ResourceNode.Boulder), TargetLabel = "STONE",
                },
                new Step
                {
                    Id = "ram", Title = "Battering ram",
                    Body = "A " + Hi("ram") + " smashes walls - the enemy's, or your own. Craft one in your bag!",
                    Goal = "Craft a battering ram", Progress = () => WoodNeed(WoodOf(Item.Ram)),
                    Hint = $"It costs {Price(Item.Ram)}. Craft it in your base.",
                    Done = () => Count(Item.Ram) >= 1,
                    Target = () => TreeIfShort(WoodOf(Item.Ram), null), TargetLabel = "TREE",
                    Items = new[] { Item.Ram },
                },
                // ---- health ----
                new Step
                {
                    Id = "berry", Title = "Food",
                    Body = "Bottom left is your " + Hi("health") + ". Berry bushes heal you. Press " + K(Bind.Interact) + " on a bush.",
                    Goal = "Pick a berry",
                    Done = () => Count(Item.Berry) > s_Berry0,
                    Target = () => Nearest(ResourceNode.Bush), TargetLabel = "BERRIES",
                    Unlocks = new[] { TutFeature.Health },
                },
                new Step
                {
                    Id = "eat", Title = "Eat",
                    Body = $"Ouch, you're hungry! Hold the berry and press {K(Bind.Aim)}. Yum!",
                    Goal = "Eat a berry",
                    Hint = "Pick the berry on your hotbar first. No berry? Get one from a bush.",
                    Enter = () => { if (Me != null) Me.TutorialRpc(2); s_NextHungry = Time.time + 3f; },
                    Done = () => s_Ate,
                    Target = () => Count(Item.Berry) > 0 ? null : Nearest(ResourceNode.Bush), TargetLabel = "BERRIES",
                    Unlocks = new[] { TutFeature.Eat },
                },
                // ---- the glass wall and the ball ----
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
                    BodyF = () => Me.CarryingBall ? $"Run home and throw it ({K(Bind.Attack)}) into your machine's socket."
                        : "You lost it! Get it back - " + K(Bind.Interact) + " takes it out of any machine.",
                    Goal = "Put the ball in your machine",
                    Hint = $"Stand at your machine, look at the glowing socket and click {Binds.Name(Bind.Attack)}.",
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

            // where everything unlocks (a feature no step teaches: once you're done)
            for (int f = 0; f < s_FeatureAt.Length; f++) s_FeatureAt[f] = int.MaxValue;
            s_ItemAt.Clear();
            for (int i = s_Steps.Count - 1; i >= 0; i--)
            {
                var st = s_Steps[i];
                if (st.Unlocks != null) foreach (var f in st.Unlocks) s_FeatureAt[(int)f] = i;
                if (st.Items != null) foreach (var it in st.Items) s_ItemAt[it] = i;
            }
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
            s_Bench0 = BenchThings();
            s_WallMax = WallPieces;
            s_SprintTime = s_GuardTime = s_CrouchTime = 0f;
            s_Jumped = s_Slid = s_Ate = false;
            SyncStep();
            s_Steps[s_Index].Enter?.Invoke();
        }

        /// <summary>Tell the server which step we're on (so it unlocks crafting, building and the workbench for us too).</summary>
        static void SyncStep()
        {
            var me = Me;
            if (me != null && me.IsSpawned && me.IsOwner && me.TutStep.Value != MyStep) me.TutStep.Value = MyStep;
        }

        /// <summary>Every frame on the local player (PlayerController).</summary>
        public static void Tick(PlayerController pc, PlayerNet me)
        {
            if (!On || me == null || pc == null) return;
            if (me.TutStep.Value != MyStep) me.TutStep.Value = MyStep; // so the server knows what's unlocked for us
            if (G == null || G.S == GameState.Waiting) return;
            if (!s_Started) { Build(); s_Started = true; s_Index = 0; EnterStep(); }
            // pressing something that isn't unlocked yet: say so (it doesn't do anything)
            if (!Chat.Open && !pc.Paused && !pc.MenuOpen && !me.Dead.Value)
            {
                for (var b = Bind.Forward; b <= Bind.Hotbar7; b++)
                    if (b != Bind.PushToTalk && !BindAllowed(b) && Binds.RawDown(b)) s_LockedAt = Time.time;
                if (!Allows(TutFeature.Hotbar) && Input.mouseScrollDelta.y != 0) s_LockedAt = Time.time;
            }
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
            if (pc.Crouching && !pc.Sliding) s_CrouchTime += Time.deltaTime;
            int berries = me.Count(Item.Berry);
            if (berries < s_LastBerries && !me.Dead.Value) s_Ate = true;
            s_LastBerries = berries;
            if (st.Id == "breakwall") s_WallMax = Mathf.Max(s_WallMax, WallPieces);
            // eating needs you to be hurt: if you've healed up again (your base heals you), you get hungry again
            if (st.Id == "eat" && !s_Ate && me.Health.Value >= Cfg.MaxHealth - 0.5f && Time.time >= s_NextHungry) { s_NextHungry = Time.time + 3f; me.TutorialRpc(2); }
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
            if (s_Index >= s_Steps.Count - 1) { s_Finished = true; SyncStep(); return; }
            s_Index++;
            EnterStep();
        }

        /// <summary>AutoTest only: jump straight to a step (by its id) to test the later parts.</summary>
        public static bool TestGoTo(string id)
        {
            if (!s_Started) return false;
            for (int i = 0; i < s_Steps.Count; i++)
                if (s_Steps[i].Id == id) { s_Index = i; s_Finished = false; EnterStep(); return true; }
            return false;
        }

        /// <summary>AutoTest: the ids of every step, in order.</summary>
        public static List<string> StepIds()
        {
            var l = new List<string>();
            foreach (var s in Steps) l.Add(s.Id);
            return l;
        }

        /// <summary>AutoTest: the step (index) a feature unlocks on.</summary>
        public static int UnlockStep(TutFeature f) => FeatureAt(f);

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

        /// <summary>Server: the eat step - knock the player's health down a bit so there's something to heal.</summary>
        public static void ServerHungry(PlayerNet p)
        {
            if (!On || p == null || p.Dead.Value) return;
            float to = Mathf.Round(Cfg.MaxHealth * 0.5f);
            if (p.Health.Value > to) p.Health.Value = to;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>The tutorial panel (left side), a goal strip while menus are open, and the marker.</summary>
        public static void Draw(float k, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            var pc = PlayerController.Local;
            if (!On || !s_Started || pc == null) return;
            float sw = Screen.width, sh = Screen.height;
            if (s_Finished)
            {
                if (pc.MenuOpen || pc.Paused) return;
                float fw = 340 * k, fx = 14, fy = 230 * k;
                var fb = new GUIStyle(small) { wordWrap = true, richText = true };
                string txt = "Win: have the ball " + Hi("in your machine") + " when the clock ends.\n"
                    + "Everything is unlocked now - your bag also makes a " + Hi("bow, arrows, chests") + " and " + Hi("high walls") + ".\n"
                    + "Real matches have " + Hi("airdrops") + ": the purple timer at the top says when the next one lands.\n"
                    + "Tip: Settings > Display changes the grass and the world's colours.\n"
                    + Hi("Esc") + " > Leave game, then host a real match!";
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
            bool locked = Time.time - s_LockedAt < 2f;

            if (pc.MenuOpen || pc.Paused)
            {
                // just the goal, along the bottom, while the inventory is up
                if (pc.Paused) return;
                bool shop = pc.LootTarget != null && pc.LootTarget.IsWorkbench; // (clear of the shop's own hint line)
                var gr = new Rect(sw / 2 - 330 * k, sh - (shop ? 90 : 40) * k, 660 * k, 32 * k);
                fill(gr, new Color(0, 0, 0, 0.75f));
                var c = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
                shadowed(gr, $"<b>TUTORIAL</b>  {goal}", c);
                return;
            }

            float w = 340 * k, x = 14, y = 230 * k;
            string bodyText = SafeBody(st);
            var body = new GUIStyle(small) { wordWrap = true, richText = true };
            var titleSt = new GUIStyle(label) { wordWrap = true, richText = true };
            float bodyH = body.CalcHeight(new GUIContent(bodyText), w - 24 * k);
            // what this step just unlocked
            string newText = null;
            if (st.Unlocks != null && st.Unlocks.Length > 0)
            {
                var parts = new List<string>();
                foreach (var f in st.Unlocks) if (f != TutFeature.HotbarHud) parts.Add(FeatureLabel(f));
                if (parts.Count > 0) newText = "<color=#7dff7d><b>NEW:</b></color> " + string.Join("   ", parts);
            }
            float newH = newText != null ? body.CalcHeight(new GUIContent(newText), w - 24 * k) + 2 * k : 0f;
            string extra = locked ? "<color=#ff9f7a><b>Not yet!</b> You'll learn that soon.</color>" : hint ? $"<color=#aaaaaa><i>Hint: {st.Hint}</i></color>" : null;
            float extraH = extra != null ? body.CalcHeight(new GUIContent(extra), w - 24 * k) + 2 * k : 0f;
            float h = 26 * k + 28 * k + bodyH + newH + 30 * k + extraH + 6 * k;
            fill(new Rect(x, y, w, h), new Color(0.04f, 0.06f, 0.1f, 0.82f));
            fill(new Rect(x, y, 4 * k, h), new Color(1f, 0.82f, 0.29f, 0.9f));
            float pct = (float)s_Index / Mathf.Max(1, s_Steps.Count);
            fill(new Rect(x, y + h - 3 * k, w * pct, 3 * k), new Color(1f, 0.82f, 0.29f, 0.8f));
            shadowed(new Rect(x + 14 * k, y + 5 * k, w - 24 * k, 22 * k), $"<size={Mathf.RoundToInt(12 * k)}><color=#ffd24a>TUTORIAL  {s_Index + 1} / {s_Steps.Count}</color></size>", small);
            shadowed(new Rect(x + 14 * k, y + 24 * k, w - 24 * k, 30 * k), $"<b>{st.Title}</b>", titleSt);
            float ty = y + 54 * k;
            GUI.Label(new Rect(x + 14 * k, ty, w - 24 * k, bodyH), bodyText, body);
            ty += bodyH;
            if (newText != null) { GUI.Label(new Rect(x + 14 * k, ty + 2 * k, w - 24 * k, newH), newText, body); ty += newH; }
            shadowed(new Rect(x + 14 * k, ty + 4 * k, w - 24 * k, 26 * k), goal, body);
            if (extra != null) GUI.Label(new Rect(x + 14 * k, ty + 30 * k, w - 24 * k, extraH), extra, body);

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
