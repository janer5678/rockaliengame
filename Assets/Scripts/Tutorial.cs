using System;
using System.Collections.Generic;
using Unity.Netcode;
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
        Inventory,  // the bag (I by default; Tab is the hold-to-show scoreboard)
        Craft,      // the crafting list in the bag (items show up one by one: Tutorial.AllowsItem)
        Hotbar,     // 1-6 and the mouse wheel pick what you hold
        Build,      // placing building pieces with the plan (and R / F)
        Aim,        // RMB (the building wheel first; the spear throw and eating unlock later)
        Interact,   // E
        Demolish,   // X (it isn't on the building wheel any more)
        Throw,      // spear: hold RMB + LMB
        Deploy,     // putting down a trade station / chest / high wall
        Workbench,  // the trade station tiers in the crafting list (everything that isn't a starter item)
        Health,     // the health bar (and armour / buffs under it)
        Eat,        // RMB with food
        Upgrade,    // F: stone upgrades (no step teaches it - the tutorial has no stone: once you're done)
        Station,    // E on the upgrade station: the UPGRADES screen (new ones go on the end)
    }

    /// <summary>
    /// Tutorial mode (the classic rules plus the upgrade station and its wood machine, always a small flat Plains map, the
    /// clock stopped until the very end, no scheduled airdrops): short, simple steps, and every one has to be DONE to move
    /// on (no skipping). Nothing in it needs stone. The order: the basics (look, move, chop, the bag, crafting, building,
    /// the spear), then a bow and arrows and the training dummies (shoot one, destroy one up close, one from range), food,
    /// the glass wall and the ball - capturing it is what unlocks the Trade Station (like in a match), so the trade
    /// station steps come after it - then a storage chest, a real airdrop, raiding a small enemy hut, the upgrade
    /// station, and the finale: the clock runs out with the ball in your machine and the real victory cutscene plays.
    /// The game is revealed a bit at a time: at the start you can only look around; each control, HUD part and craftable
    /// item unlocks on the step that teaches it (Allows / AllowsItem, checked by Binds, PlayerController, the HUD and -
    /// for crafting, building, the trade station and upgrades - the server, using each player's own synced step
    /// PlayerNet.TutStep).
    /// A small panel on the left says what to do, a marker points at what it's about, and a one-line hint shows up if a
    /// step takes a while. Each player goes through their own steps on their own machine; the props the later steps need
    /// (dummies, the airdrop, the hut, the wood for an upgrade, the finale) are asked of the server (PlayerNet.TutorialRpc
    /// -> ServerAction), and the match-level bits run on the server (ServerTick): the clock stays stopped until the
    /// finale, players who join later are dropped straight into their base, and the glass wall drops once every player
    /// still on those steps has walked up to it (or 90 s after the first one got there).
    ///
    /// Solo or with a friend (the menu asks: Hud.Menus.cs). Solo (Bootstrap.Solo) plays it all alone and nobody can join.
    /// With a friend the host plays everything up to the finale on their own, then the "friend" step stops them: it
    /// waits (showing the host's IP) until a second player has joined. Whoever joins a tutorial is never taken through
    /// the early steps: their guide starts at that same "friend" step with everything unlocked and a starter kit, and
    /// waits there for the host. From there the steps use both players: a duel, then the finale as a real short match
    /// for the ball. (The friend-only steps are skipped in solo: Step.Skip.)
    ///
    /// When a game mechanic changes, update the steps (and what they unlock) here to match.
    /// </summary>
    public static class Tutorial
    {
        public static bool On => Cfg.Rules == GameRules.Tutorial;

        // things the game tells the tutorial about
        public static int WeakHits, SpearThrows;
        /// <summary>Training dummies (told by the server: PlayerNet.TutEventRpc -> OnEvent): hits on one, hits with the bow
        /// in your hands, and ones you finished off up close / from range.</summary>
        public static int DummyHits, DummyBowHits, DummyKillsNear, DummyKillsFar;

        /// <summary>Seconds before a stuck step shows its hint.</summary>
        public const float HintAfter = 40f;
        /// <summary>Server: the wall drops this long after the first player reached it, even if others haven't yet.</summary>
        public const float WallWait = 90f;
        /// <summary>PlayerNet.TutStep when the tutorial is finished (everything unlocked).</summary>
        public const byte FinishedStep = 255;
        /// <summary>What a guide asks the server for (PlayerNet.TutorialRpc -> ServerAction).</summary>
        public const byte AskWall = 1, AskHungry = 2, AskDummies = 3, AskAirdrop = 4, AskHut = 5, AskUpgradeWood = 6, AskFinale = 7, AskKit = 8;
        /// <summary>The finale: how long the clock runs before the match ends - alone (the ball is already in your machine),
        /// and with a friend (a real short match for it); and how much longer if nobody has it in a machine at 0.</summary>
        public const float SoloFinaleSeconds = 10f, FriendMatchSeconds = 120f, OvertimeSeconds = 20f;
        /// <summary>The duel step carries on alone this long after your friend has left.</summary>
        public const float FriendGoneWait = 30f;

        class Step
        {
            public string Id, Title, Body, Goal, Hint;
            public Func<string> BodyF;              // a body that changes (overrides Body)
            public Func<bool> Done;
            public Func<string> Progress;           // e.g. "12 / 50 wood"
            public Func<Vector3?> Target;           // where the on-screen marker points
            public string TargetLabel;
            public Action Enter;
            public Func<bool> Skip;                 // not part of this run (the friend steps, playing solo): passed straight over
            public TutFeature[] Unlocks;            // what this step lets you do from now on
            public Item[] Items;                    // starter items that show up in the crafting list from now on
        }

        static List<Step> s_Steps;
        static readonly int[] s_FeatureAt = new int[Enum.GetValues(typeof(TutFeature)).Length];
        static readonly Dictionary<Item, int> s_ItemAt = new Dictionary<Item, int>();
        static bool s_Started;
        static int s_Index;
        static float s_DoneAt = -1f, s_StepStart, s_LockedAt = -10f, s_NextHungry, s_NextAsk;
        static bool s_Finished, s_OverScreen;
        // what things were when the step started
        static float s_Moved;
        static Vector3 s_LastPos;
        static int s_Wood0, s_Weak0, s_Spear0, s_Berry0, s_LastBerries, s_WallMax, s_Bow0, s_Near0, s_Far0, s_Fights0;
        static float s_SprintTime, s_GuardTime, s_CrouchTime;
        static bool s_Jumped, s_Slid, s_Ate;
        // props seen once (so "it's gone" means it was dealt with, not that it hasn't arrived yet)
        static bool s_DropSeen, s_HutSeen, s_HutChestSeen;
        static int s_HutMax;
        // server
        static float s_FirstAtWall = -1f;
        static readonly Dictionary<ulong, float> s_NextHome = new Dictionary<ulong, float>();
        static readonly bool[] s_HutBuilt = new bool[4];
        static readonly HashSet<ulong> s_Kit = new HashSet<ulong>();
        /// <summary>Server: the finale - 0 not yet, 1 asked for (the wall drops first if it's still up), 2 the clock is running.</summary>
        static int s_Finale, s_FinaleTeam;

        static List<Step> Steps { get { if (s_Steps == null) Build(); return s_Steps; } }
        public static int StepNumber => s_Index + 1;
        public static int StepCount => Steps.Count;
        public static string StepId => s_Finished ? "done" : s_Started ? Steps[s_Index].Id : "";
        public static bool Finished => s_Finished;
        /// <summary>The tutorial has nothing to do with stone: the HUD leaves it out (the stone count, "F: upgrade to stone") until you're done.</summary>
        public static bool HideStone => On && !s_Finished;
        public static bool HasEnterKey => false; // there's no Enter to read on or skip any more (AutoTest)
        /// <summary>The step this player is on (FinishedStep once done), as synced to the server in PlayerNet.TutStep.</summary>
        public static byte MyStep => s_Finished ? FinishedStep : (byte)Mathf.Min(s_Started ? s_Index : 0, 254);
        /// <summary>Server, for the tests: is the finale's clock running?</summary>
        public static bool FinaleRunning => s_Finale == 2;

        /// <summary>
        /// This tutorial is being played with a friend: the host didn't pick Solo - or this machine joined somebody's
        /// tutorial (a tutorial that can be joined is never a solo one). The host and the client agree, so the same steps
        /// are skipped (or not) on both.
        /// </summary>
        public static bool Friend
        {
            get
            {
                var g = G;
                return On && g != null && g.IsSpawned && (!g.IsServer || !Bootstrap.Solo);
            }
        }

        public static void Reset()
        {
            s_Steps = null; // (rebuilt with the current key names)
            s_Started = false;
            s_Index = 0;
            s_DoneAt = -1f;
            s_Finished = false;
            WeakHits = SpearThrows = 0;
            DummyHits = DummyBowHits = DummyKillsNear = DummyKillsFar = 0;
            s_DropSeen = s_HutSeen = s_HutChestSeen = false;
            s_HutMax = 0;
            s_NextAsk = 0f;
            s_FirstAtWall = -1f;
            s_NextHome.Clear();
            for (int i = 0; i < s_HutBuilt.Length; i++) s_HutBuilt[i] = false;
            s_Kit.Clear();
            s_Finale = 0;
            s_FinaleTeam = 0;
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
            if (!Cfg.IsStarter(i)) return FeatureAt(TutFeature.Workbench); // the trade station tiers: once the bag shows them
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
                case Bind.Rotate: return TutFeature.Build;
                case Bind.Upgrade: return TutFeature.Upgrade;
                case Bind.Demolish: return TutFeature.Demolish;
                case Bind.PushToTalk: return null;
                default: return b >= Bind.Hotbar1 && b <= Bind.Hotbar6 ? TutFeature.Hotbar : (TutFeature?)null; // (the scoreboard key is never locked)
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
                case TutFeature.Hotbar: return $"{K(Bind.Hotbar1)}-{K(Bind.Hotbar6)} pick a slot";
                case TutFeature.Build: return "building";
                case TutFeature.Aim: return $"{K(Bind.Aim)} building wheel";
                case TutFeature.Interact: return $"{K(Bind.Interact)} use";
                case TutFeature.Demolish: return $"{K(Bind.Demolish)} demolish";
                case TutFeature.Throw: return "spear throw";
                case TutFeature.Deploy: return "putting things down";
                case TutFeature.Workbench: return "trade station crafts";
                case TutFeature.Health: return "health bar";
                case TutFeature.Eat: return $"{K(Bind.Aim)} eat";
                case TutFeature.Upgrade: return $"{K(Bind.Upgrade)} upgrade";
                case TutFeature.Station: return "the upgrade station";
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
        static bool GameOver => G != null && G.S == GameState.GameOver;

        static int Pieces(PieceType t)
        {
            int n = 0;
            foreach (var s in Structure.All) if (s != null && s.IsSpawned && s.Team.Value == Team && s.PType == t) n++;
            return n;
        }

        static int WallPieces => Pieces(PieceType.Wall) + Pieces(PieceType.Doorway) + Pieces(PieceType.Window);

        /// <summary>The nearest tree / bush (...) with something left on it in your own team's part of the map - the
        /// marker never sends you over to the enemy's side.</summary>
        internal static Vector3? Nearest(byte kind)
        {
            var me = Me;
            if (me == null) return null;
            ResourceNode best = null;
            float bd = float.MaxValue;
            int team = Team;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Kind.Value != kind || n.Amount.Value <= 0) continue;
                if (Cfg.RegionOf(n.transform.position) != team) continue; // (only in your own team's space)
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
        /// <summary>The wood still needed for the bow and a first bundle of arrows.</summary>
        static int BowNeed => (Count(Item.Bow) > 0 ? 0 : WoodOf(Item.Bow)) + (Count(Item.Arrow) > 0 ? 0 : WoodOf(Item.Arrow));

        static Vector3? BaseSpot => Cfg.BaseCenter[Team] - Cfg.BackDir(Team) * 7f + Vector3.up * (Cfg.BaseY + 0.5f);
        static Vector3? MachineSpot => Cfg.MachinePos(Team) + Vector3.up * 1.6f;
        static Vector3? StationSpot => Cfg.UpgradeStationPos(Team) + Vector3.up * 1.9f;
        static Vector3? BallSpot => Ball.Instance != null ? Ball.Instance.transform.position + Vector3.up * 0.6f : (Vector3?)null;
        static Container MyBench => Workbench.ForTeam(Team, 1);
        static Container MyBench2 => Workbench.ForTeam(Team, 2);
        static bool InBase => Me != null && Cfg.BaseTeamAt(Me.transform.position) == Team;
        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }

        /// <summary>The glass wall on your side of the map, just past the glass dome in the middle (where the walls meet the dome).</summary>
        public static Vector3 WallSpot(int team)
        {
            // along the wall from the middle (the wall runs across your direction home), a little out past the dome
            var back = Cfg.BackDir(team);
            var along = Cfg.FourWay ? (Quaternion.Euler(0, 45f, 0) * back).normalized : Vector3.Cross(Vector3.up, back);
            var p = along * (MapBuilder.DomeRadius + 3.5f);
            p += back * (Cfg.FourWay ? 1.5f : 2.5f);
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

        /// <summary>Players the glass wall still waits for: on the wall steps (or before them) and not at the wall yet.
        /// Whoever is past those steps - a friend who joined at the "with a friend" point - isn't waited for.</summary>
        static int PlayersNotAtWall()
        {
            int n = 0, drop = StepAt("drop");
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && !p.TutAtWall.Value && p.TutStep.Value <= drop) n++;
            return n;
        }

        /// <summary>The index of a step (by its id; -1 if there's none).</summary>
        static int StepAt(string id)
        {
            var steps = Steps;
            for (int i = 0; i < steps.Count; i++) if (steps[i].Id == id) return i;
            return -1;
        }

        static void Ask(byte what)
        {
            var me = Me;
            if (me != null && me.IsSpawned) me.TutorialRpc(what);
        }

        // ---- the training yard: dummies in front of your base ----

        /// <summary>Where a team's training dummies stand: just outside the front of its base (the side towards the middle).</summary>
        public static Vector3 TrainSpot(int team)
        {
            var p = Cfg.BaseCenter[team] - Cfg.BackDir(team) * (Cfg.BaseHalf + 6f);
            p.y = MapBuilder.Height(p.x, p.z);
            return p;
        }

        static bool OnMySide(Vector3 p) => Cfg.RegionOf(p) == Cfg.RegionOf(Cfg.BaseCenter[Team]) && Cfg.BaseTeamAt(p) < 0;

        /// <summary>The nearest standing training dummy on your side of the map (null: none).</summary>
        public static Vehicle NearestDummy()
        {
            var me = Me;
            if (me == null) return null;
            Vehicle best = null;
            float bd = float.MaxValue;
            foreach (var v in Vehicle.All)
            {
                if (v == null || !v.IsSpawned || !v.IsDummy || v.Hp.Value <= 0f || !OnMySide(v.transform.position)) continue;
                float d = (v.transform.position - me.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        static int DummyCount()
        {
            int n = 0;
            foreach (var v in Vehicle.All) if (v != null && v.IsSpawned && v.IsDummy && v.Hp.Value > 0f && OnMySide(v.transform.position)) n++;
            return n;
        }

        static Vector3? DummySpot
        {
            get
            {
                var d = NearestDummy();
                return (d != null ? d.transform.position : TrainSpot(Team)) + Vector3.up * 2.3f;
            }
        }

        static string DummyHp()
        {
            var d = NearestDummy();
            return d == null ? "" : $"dummy {d.Hp.Value:0} / {d.MaxHp:0} HP";
        }

        // ---- your chest, the airdrop, the hut ----

        /// <summary>A storage chest of your team's (null: none yet).</summary>
        public static Container MyChest
        {
            get
            {
                foreach (var c in Container.All) if (c != null && c.IsSpawned && c.Breakable && c.Team.Value == Team) return c;
                return null;
            }
        }

        static bool ChestHasStuff
        {
            get
            {
                foreach (var c in Container.All) if (c != null && c.IsSpawned && c.Breakable && c.Team.Value == Team && !c.Empty) return true;
                return false;
            }
        }

        /// <summary>The tutorial's airdrop for your team is on its way (its ship flying in, or beaming the crate down).</summary>
        static bool DropComing
        {
            get
            {
                var g = G;
                if (g == null || !g.IsSpawned) return false;
                double s = g.LaneStartAt(Team);
                return s >= 0 && g.NetworkManager.ServerTime.Time < s + NetGame.DropLand + 1.5;
            }
        }

        static float DropLandsIn
        {
            get
            {
                var g = G;
                if (g == null || !g.IsSpawned || g.LaneStartAt(Team) < 0) return 0f;
                return Mathf.Max(0f, (float)(g.LaneStartAt(Team) + NetGame.DropLand - g.NetworkManager.ServerTime.Time));
            }
        }

        /// <summary>Your team's airdrop crate, once it has landed (null: not yet, or it's been emptied and is gone).</summary>
        public static Container MyDrop
        {
            get
            {
                var g = G;
                if (g == null || !g.IsSpawned || g.LaneStartAt(Team) < 0) return null;
                var at = g.LanePosAt(Team);
                foreach (var c in Container.All) if (c != null && c.IsSpawned && c.IsAirdrop && Flat(c.transform.position, at) < 3f) return c;
                return null;
            }
        }

        static Vector3? DropSpot
        {
            get
            {
                var c = MyDrop;
                if (c != null) return c.Center + Vector3.up * 1.2f;
                return DropComing ? G.LanePosAt(Team) + Vector3.up * 1.8f : (Vector3?)null;
            }
        }

        /// <summary>The hut the raid steps are about: enemy pieces standing on your own side of the map, outside any base
        /// (the server builds it there: ServerHut). Its doorway while the door's still on, its walls, and its chest.</summary>
        public static Structure HutDoor
        {
            get
            {
                foreach (var s in Structure.All)
                    if (s != null && s.IsSpawned && s.Team.Value != Team && s.PType == PieceType.Doorway && s.HasDoor && OnMySide(s.transform.position)) return s;
                return null;
            }
        }

        static int HutPieces
        {
            get
            {
                int n = 0;
                foreach (var s in Structure.All)
                    if (s != null && s.IsSpawned && s.Team.Value != Team && (s.PType == PieceType.Wall || s.PType == PieceType.Doorway) && OnMySide(s.transform.position)) n++;
                return n;
            }
        }

        public static Container HutChest
        {
            get
            {
                foreach (var c in Container.All)
                    if (c != null && c.IsSpawned && c.Breakable && c.Team.Value != Team && OnMySide(c.transform.position)) return c;
                return null;
            }
        }

        static Vector3? HutSpot
        {
            get
            {
                var d = HutDoor;
                if (d != null) return d.transform.position + Vector3.up * 3.6f;
                var c = HutChest;
                if (c != null) return c.Center + Vector3.up * 0.8f;
                foreach (var s in Structure.All)
                    if (s != null && s.IsSpawned && s.Team.Value != Team && OnMySide(s.transform.position)) return s.transform.position + Vector3.up * 3.6f;
                return null;
            }
        }

        // ---- with a friend ----

        /// <summary>Another player in this tutorial (null: you're alone).</summary>
        static PlayerNet Other
        {
            get
            {
                foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned && p != Me) return p;
                return null;
            }
        }

        /// <summary>Somebody else is here, and everyone else has got to this step (or past it).</summary>
        static bool OthersAt(int step)
        {
            bool any = false;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned || p == Me) continue;
                if (p.TutStep.Value < step) return false;
                any = true;
            }
            return any;
        }

        /// <summary>Kills and deaths so far, everyone's together (it goes up when somebody is knocked out).</summary>
        static int Fights()
        {
            int n = 0;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned) n += p.Kills.Value + p.Deaths.Value;
            return n;
        }

        static string s_Ip;

        /// <summary>This PC's address on the local network, for the "tell your friend your IP" step ("" if it can't be found).</summary>
        static string LocalIp()
        {
            if (s_Ip != null) return s_Ip;
            s_Ip = "";
            try
            {
                // an adapter that's up and has a gateway first (the one that's really on the network), then any other
                for (int pass = 0; pass < 2 && s_Ip.Length == 0; pass++)
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                        var props = ni.GetIPProperties();
                        if (pass == 0 && props.GatewayAddresses.Count == 0) continue;
                        foreach (var ua in props.UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || System.Net.IPAddress.IsLoopback(ua.Address)) continue;
                            string a = ua.Address.ToString();
                            if (a.StartsWith("169.254.")) continue;
                            s_Ip = a;
                            break;
                        }
                        if (s_Ip.Length > 0) break;
                    }
            }
            catch (Exception) { s_Ip = ""; }
            return s_Ip;
        }

        static string FriendBody()
        {
            var g = G;
            var other = Other;
            if (g != null && g.IsServer)
            {
                if (other != null) return "Your friend is here! Getting them ready...";
                string ip = LocalIp();
                string port = Bootstrap.I != null ? Bootstrap.I.Port : "7777";
                return "That's everything you can do alone - the rest you play " + Hi("together") + ". Your friend starts the game, types your IP into "
                    + Hi("Host IP") + " on the main menu and presses " + Hi("JOIN GAME") + ".\n"
                    + (ip.Length > 0 ? "Your IP on this network: " + Hi(ip) : "Your IP: look it up in your network settings") + $"  (port {port})\n"
                    + "Over the internet they need your public IP, and that port (UDP) forwarded to this PC.";
            }
            // the one who joined: no early steps - a starter kit, the keys at a glance, and wait for the host
            string where = other == null ? "" : other.TutStep.Value == FinishedStep ? "" : $" They're on step {Shown(other.TutStep.Value) + 1} of {Shown(Steps.Count)}.";
            return "You've joined your friend's tutorial: you get a " + Hi("starter kit") + " and everything is unlocked. When your friend gets here, you play the last part together." + where + "\n"
                + $"{K(Bind.Forward)}{K(Bind.Left)}{K(Bind.Back)}{K(Bind.Right)} move  {K(Bind.Sprint)} run  {K(Bind.Jump)} jump  {K(Bind.Crouch)} crouch  {K(Bind.Slide)} slide\n"
                + $"{K(Bind.Attack)} hit / shoot  {K(Bind.Aim)} aim, eat, building wheel  {K(Bind.Interact)} use  {K(Bind.Inventory)} bag and crafting  {K(Bind.Hotbar1)}-{K(Bind.Hotbar6)} hotbar";
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
                    Body = $"Hold {K(Bind.Sprint)} to run. Tap {K(Bind.Jump)} to jump. (Press it just before you land and you jump again the moment you touch down.)",
                    Goal = "Run 2 seconds and jump",
                    Progress = () => $"run {Mathf.Min(2f, s_SprintTime):0.0}/2 s   jump {(s_Jumped ? "✔" : "-")}",
                    Hint = $"Hold {Binds.Name(Bind.Sprint)} while you walk forward.",
                    Done = () => s_SprintTime >= 2f && s_Jumped,
                    Unlocks = new[] { TutFeature.Sprint, TutFeature.Jump },
                },
                new Step
                {
                    Id = "crouch", Title = "Crouch",
                    Body = $"Hold {K(Bind.Crouch)} to crouch. Enemies hear your footsteps from far off - but crouch-walking is silent. Sneaky!",
                    Goal = "Crouch for 1 second", Progress = () => $"{Mathf.Min(1f, s_CrouchTime):0.0} / 1 s",
                    Hint = $"Keep {Binds.Name(Bind.Crouch)} held down.",
                    Done = () => s_CrouchTime >= 1f,
                    Unlocks = new[] { TutFeature.Crouch },
                },
                new Step
                {
                    Id = "slide", Title = "Slide",
                    Body = $"While running, press {K(Bind.Slide)} to slide. Wheee! Press it mid-jump and you land in a slide; push the other way or press {K(Bind.Crouch)} to drop straight into a crouch. ({K(Bind.Crouch)} only crouches.)",
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
                    Body = "See the glowing orange " + Hi("X") + " on the tree? Hit it for double wood!",
                    Goal = "Hit the X 2 times", Progress = () => $"{Mathf.Min(2, WeakHits - s_Weak0)} / 2",
                    Hint = "Aim right at the orange X when you swing. It moves after each hit.",
                    Done = () => WeakHits - s_Weak0 >= 2,
                    Target = () => Nearest(ResourceNode.Tree), TargetLabel = "TREE",
                },
                new Step
                {
                    Id = "wood", Title = "More wood",
                    Body = "Wood makes everything. Keep chopping! " + Hi("Fallen logs") + " give wood too (and have an X), and a felled tree grows back.",
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
                    Body = "On the right is " + Hi("crafting") + ". Click " + Hi("CRAFT") + " next to " + Hi(Cfg.ItemName(Item.Hatchet)) + ". It chops 3x faster!",
                    Goal = "Craft a " + Cfg.ItemName(Item.Hatchet),
                    Hint = $"Open your bag with {Binds.Name(Bind.Inventory)}. Crafting is on the right. It costs {Price(Item.Hatchet)}.",
                    Done = () => Count(Item.Hatchet) >= 1,
                    Unlocks = new[] { TutFeature.Craft }, Items = new[] { Item.Hatchet },
                },
                new Step
                {
                    Id = "axechop", Title = "Use the axe",
                    Body = $"Close the bag ({K(Bind.Inventory)}). Press the axe's number ({K(Bind.Hotbar1)}-{K(Bind.Hotbar6)}) or use the mouse wheel, then chop!",
                    Goal = "Get 100 wood with the axe", Progress = () => $"{Mathf.Max(0, Count(Item.Wood) - s_Wood0)} / 100",
                    Hint = $"Keys {Binds.Name(Bind.Hotbar1)}-{Binds.Name(Bind.Hotbar6)} or the mouse wheel pick what you hold. An empty slot is your rock.",
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
                    Body = $"Hold {K(Bind.Aim)} for the wheel. Pick " + Hi("Wall") + ", then click the floor's edge. (Aim a wall at bare ground in your base and a floor goes down under it first - you pay for both.)",
                    Goal = "Place a wall",
                    Hint = $"Keep holding {Binds.Name(Bind.Aim)}, move the mouse onto Wall, let go.",
                    Done = () => Pieces(PieceType.Wall) >= 1,
                    Unlocks = new[] { TutFeature.Aim },
                },
                new Step
                {
                    Id = "door", Title = "Build a door",
                    Body = "Pick " + Hi("Doorway") + $" on the wheel and place it. {K(Bind.Interact)} opens your door - it has a " + Hi("padlock") + ", so enemies can't.",
                    Goal = "Place a doorway",
                    Hint = "Doorways go on a floor's edge, just like walls - aim near your last pieces and it carries them on.",
                    Done = () => Pieces(PieceType.Doorway) >= 1,
                    Unlocks = new[] { TutFeature.Interact },
                },
                new Step
                {
                    Id = "breakwall", Title = "Oops? Break it!",
                    BodyF = () => (s_WallMax == 0 ? "Build a wall first. " : "")
                        + "You can take down " + Hi("your own") + $" walls. Hold the plan and press {K(Bind.Demolish)} on one (half the wood back) - you can build there again straight away. "
                        + $"Wood is weak: hitting breaks it too, but where a piece is " + Hi("broken") + $" nothing can be built again for {Cfg.WallRebuildCooldown:0} seconds.",
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
                    Body = $"Hold the spear. Hold {K(Bind.Aim)} to wind it up, click {K(Bind.Attack)} to throw - it hits hard! (Click early and it throws once it's wound up.) {K(Bind.Interact)} picks it up.",
                    Goal = "Throw the spear and pick it up",
                    Progress = () => SpearThrows - s_Spear0 > 0 ? (Count(Item.Spear) > 0 ? "got it ✔" : "thrown ✔ - pick it up") : "",
                    Hint = "Walk to the spear, look at it and press " + Binds.Name(Bind.Interact) + ".",
                    Done = () => SpearThrows - s_Spear0 > 0 && Count(Item.Spear) > 0,
                    Unlocks = new[] { TutFeature.Throw },
                },
                // ---- the bow, and the training dummies ----
                new Step
                {
                    Id = "bow", Title = "Make a bow",
                    Body = "Now something that reaches further. Craft a " + Hi("Bow") + " and some " + Hi("Arrows") + " in your bag, in your base.",
                    Goal = "Craft a bow and arrows",
                    Progress = () => $"bow {(Count(Item.Bow) > 0 ? "✔" : "-")}   arrows {Count(Item.Arrow)}" + (Count(Item.Wood) < BowNeed ? $"   wood {Count(Item.Wood)} / {BowNeed} - chop more!" : ""),
                    Hint = $"The bow costs {Price(Item.Bow)}; {Cfg.ArrowsPerCraft} arrows cost {Price(Item.Arrow)}. Craft them inside your base.",
                    Done = () => Count(Item.Bow) >= 1 && Count(Item.Arrow) >= 1,
                    Target = () => TreeIfShort(BowNeed, InBase ? null : BaseSpot), TargetLabel = "",
                    Items = new[] { Item.Bow, Item.Arrow },
                },
                new Step
                {
                    Id = "shoot", Title = "Shoot it",
                    Body = Hi("Training dummies") + $" are standing in front of your base. Hold the bow, hold {K(Bind.Attack)} to draw it back - the longer, the faster and harder it flies - and let go. "
                        + $"Arrows drop on the way: aim a little high. {K(Bind.Interact)} picks up the ones that miss.",
                    Goal = "Hit a dummy with an arrow",
                    Progress = () => Count(Item.Arrow) == 0 ? "no arrows - craft more in your base" : $"arrows {Count(Item.Arrow)}",
                    Hint = "Pick the bow on your hotbar, stand a few steps back from a dummy and aim at the target on its chest.",
                    Enter = () => Ask(AskDummies),
                    Done = () => DummyBowHits - s_Bow0 > 0,
                    Target = () => Count(Item.Arrow) == 0 ? TreeIfShort(WoodOf(Item.Arrow), InBase ? DummySpot : BaseSpot) : DummySpot, TargetLabel = "",
                },
                new Step
                {
                    Id = "melee", Title = "Fight up close",
                    Body = "Now up close. Hold your " + Hi("spear") + $" (or the axe) and hit a dummy with {K(Bind.Attack)} until it falls apart. The spear pokes hardest and reaches furthest. Against a player, a hit on the " + Hi("head") + " does double.",
                    Goal = "Destroy a dummy up close",
                    Progress = DummyHp,
                    Hint = "Walk right up to a dummy and keep clicking. A new one pops up when one is gone.",
                    Enter = () => Ask(AskDummies),
                    Done = () => DummyKillsNear - s_Near0 > 0,
                    Target = () => DummySpot, TargetLabel = "DUMMY",
                },
                new Step
                {
                    Id = "ranged", Title = "Fight from range",
                    Body = "And one from further back: finish a dummy off with " + Hi("arrows") + " (a full draw does the most) or a " + Hi("thrown spear") + $" - from at least {Vehicle.DummyFar + 0.5f:0} metres away.",
                    Goal = "Destroy a dummy from range",
                    Progress = () =>
                    {
                        var d = NearestDummy();
                        string arrows = Count(Item.Arrow) == 0 ? (Count(Item.Spear) > 0 ? "no arrows - throw your spear" : "no arrows - craft more in your base") : $"arrows {Count(Item.Arrow)}";
                        if (d == null || Me == null) return arrows;
                        float dist = Vector3.Distance(Me.transform.position, d.transform.position);
                        return arrows + (dist <= Vehicle.DummyFar + 0.5f ? "   too close - step back" : $"   {DummyHp()}");
                    },
                    Hint = "The shot that finishes it has to come from range. Out of arrows? " + Cfg.ArrowsPerCraft + " more cost " + Price(Item.Arrow) + ".",
                    Enter = () => Ask(AskDummies),
                    Done = () => DummyKillsFar - s_Far0 > 0,
                    Target = () => Count(Item.Arrow) == 0 && Count(Item.Spear) == 0 ? TreeIfShort(WoodOf(Item.Arrow), InBase ? DummySpot : BaseSpot) : DummySpot, TargetLabel = "",
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
                    Enter = () => { Ask(AskHungry); s_NextHungry = Time.time + 3f; },
                    Done = () => s_Ate,
                    Target = () => Count(Item.Berry) > 0 ? null : Nearest(ResourceNode.Bush), TargetLabel = "BERRIES",
                    Unlocks = new[] { TutFeature.Eat },
                },
                // ---- the glass wall and the ball ----
                new Step
                {
                    Id = "glass", Title = "The glass wall",
                    Body = "A big " + Hi("glass wall") + " splits the map. The " + Hi("ball") + " waits under the glass dome in the middle - it's your " + Hi("emergency flare") + ". Walk up to the wall!",
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
                        return WallDown || left == 0 ? "Down it slides into the ground - and the dome too! The " + Hi("ball") + " in the middle is free."
                            : $"It drops when everyone is here. Waiting for {left} more player{(left == 1 ? "" : "s")}...";
                    },
                    Goal = "Wait for the wall to drop",
                    Hint = "Slow friends? The wall drops by itself soon.",
                    Enter = () => Ask(AskWall),
                    Done = () => WallDown,
                    Target = () => WallSpot(Team), TargetLabel = "GLASS WALL",
                },
                new Step
                {
                    Id = "toball", Title = "Get the ball",
                    Body = "The " + Hi("ball") + " is in the middle - its beam of light shows where from anywhere. Run to it!",
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
                        ? $"You captured the ball - that unlocks the " + Hi("Trade Station") + $" (for everyone)! Enemies can steal it with {K(Bind.Interact)}, so stay by your machine."
                        : "It got stolen! Get it back into your machine.",
                    Goal = "Guard your machine for 5 seconds",
                    Progress = () => $"{Mathf.Min(5f, s_GuardTime):0} / 5 s",
                    Done = () => s_GuardTime >= 5f,
                    Target = () => Ball.Instance != null && Ball.Instance.SocketTeam.Value != Team ? BallSpot : MachineSpot, TargetLabel = "",
                },
                // ---- the trade station: locked until the ball has been captured (NetGame.Bench.cs) ----
                new Step
                {
                    Id = "bench", Title = "The Trade Station",
                    BodyF = () => (Cfg.BenchUnlocked(Team) ? "You captured the ball, so the " + Hi("Trade Station") + " is unlocked! "
                            : "The " + Hi("Trade Station") + " unlocks once the ball is captured. ")
                        + $"(In a match: any team puts the ball in its machine once, or keeps it in its base for {Cfg.BenchUnlockSeconds:0} seconds - and that unlocks it for " + Hi("everyone") + ".) "
                        + "A Trade Station in your base adds more things to your bag. Craft a " + Hi(Cfg.ItemName(Item.Workbench)) + "!",
                    Goal = "Craft a " + Cfg.ItemName(Item.Workbench),
                    Progress = () => !Cfg.BenchUnlocked(Team) ? "locked - get the ball into your machine" : WoodNeed(WoodOf(Item.Workbench)),
                    Hint = $"Open your bag in your base. It costs {Price(Item.Workbench)}.",
                    Done = () => Count(Item.Workbench) >= 1 || MyBench != null,
                    Target = () => !Cfg.BenchUnlocked(Team) ? (Me.CarryingBall ? MachineSpot : BallSpot) : TreeIfShort(WoodOf(Item.Workbench), InBase ? null : BaseSpot), TargetLabel = "",
                    Items = new[] { Item.Workbench },
                },
                new Step
                {
                    Id = "placebench", Title = "Put it down",
                    Body = "Hold the trade station and click to put it down " + Hi("anywhere in your base") + ". Only C4 stuck right on it can break it (and then it just drops).",
                    Goal = "Place your trade station",
                    Hint = "Pick it on your hotbar and aim at flat ground inside your base (not the spawn spot or the ball socket).",
                    Done = () => MyBench != null,
                    Target = () => InBase ? null : BaseSpot, TargetLabel = "YOUR BASE",
                    Unlocks = new[] { TutFeature.Deploy },
                },
                new Step
                {
                    Id = "newcrafts", Title = "New things!",
                    Body = $"Open your bag ({K(Bind.Inventory)}). Your trade station adds a " + Hi("TRADE STATION") + " list: crossbow, armour, chainsaw, high walls, a saddle... "
                        + "and the " + Hi(Cfg.ItemName(Item.Workbench2)) + $" ({Price(Item.Workbench2)}) for guns, ammo, C4 and more. You can see them anywhere, but only craft them in your base. Scroll the list with the mouse wheel.",
                    Goal = "Open your bag in your base",
                    Hint = $"Stand inside your base and press {Binds.Name(Bind.Inventory)}. The new list is under the basics.",
                    Done = () => PC != null && PC.MenuOpen && PC.LootTarget == null && !PC.UpgradesOpen && Cfg.CraftTierAt(Team, Me.transform.position) >= 1,
                    Target = () => InBase ? null : BaseSpot, TargetLabel = "YOUR BASE",
                    Unlocks = new[] { TutFeature.Workbench },
                },
                // ---- a storage chest ----
                new Step
                {
                    Id = "chest", Title = "A storage chest",
                    Body = "When you die, " + Hi("everything you carry drops") + " where you fell. A " + Hi(Cfg.ItemName(Item.Chest)) + " in your base keeps things safe - until somebody breaks in. Craft one!",
                    Goal = "Craft a " + Cfg.ItemName(Item.Chest),
                    Progress = () => WoodNeed(WoodOf(Item.Chest)),
                    Hint = $"Open your bag in your base. It costs {Price(Item.Chest)}.",
                    Done = () => Count(Item.Chest) >= 1 || MyChest != null,
                    Target = () => TreeIfShort(WoodOf(Item.Chest), InBase ? null : BaseSpot), TargetLabel = "",
                    Items = new[] { Item.Chest },
                },
                new Step
                {
                    Id = "placechest", Title = "Put the chest down",
                    Body = $"Hold the chest and click {K(Bind.Attack)} to put it down in your base. Behind your own walls is safest.",
                    Goal = "Place your chest",
                    Hint = "Pick the chest on your hotbar and aim at the ground (or a floor) inside your base.",
                    Done = () => MyChest != null,
                    Target = () => InBase ? null : BaseSpot, TargetLabel = "YOUR BASE",
                },
                new Step
                {
                    Id = "usechest", Title = "Fill it",
                    Body = $"Tap {K(Bind.Interact)} on the chest to open it. Your bag is beside it: " + Hi("Shift + click") + " something (or drag it across) to put it in the chest. "
                        + $"(Hold {K(Bind.Interact)} on an empty chest or trade station of yours to pick it back up.)",
                    Goal = "Put something in your chest",
                    Hint = "Look at the chest from close by and tap " + Binds.Name(Bind.Interact) + ". Then hold Shift and click some wood.",
                    Done = () => ChestHasStuff,
                    Target = () => { var c = MyChest; return c != null ? c.Center + Vector3.up * 0.8f : (Vector3?)null; }, TargetLabel = "YOUR CHEST",
                },
                // ---- an airdrop ----
                new Step
                {
                    Id = "airdrop", Title = "Airdrop!",
                    Body = "Look up - a UFO is bringing an " + Hi("airdrop") + ". It flies in, then beams a crate down slowly: the " + Hi("purple beam") + " shows where. "
                        + $"In a match a warning counts down {NetGame.DropWarning:0} seconds before each one, and everyone races for it. Go and meet it!",
                    Goal = "Get to the airdrop crate",
                    Progress = () => MyDrop != null ? "it's down!" : DropComing ? $"lands in {Mathf.CeilToInt(DropLandsIn)} s" : "",
                    Hint = "Follow the yellow marker to the purple beam and wait for the crate to touch down.",
                    Enter = () => Ask(AskAirdrop),
                    Done = () => { var c = MyDrop; return c != null && Vector3.Distance(c.transform.position, Me.transform.position) < 6f; },
                    Target = () => DropSpot, TargetLabel = "AIRDROP",
                },
                new Step
                {
                    Id = "loot", Title = "Take the loot",
                    Body = $"Press {K(Bind.Interact)} on the crate and take what's inside (click it, or Shift + click). An airdrop holds " + Hi("one powerful item") + " - rockets, C4, a jetpack... Hold it to see what it does.",
                    Goal = "Empty the airdrop crate",
                    Hint = "Look at the crate from close by and press " + Binds.Name(Bind.Interact) + ". Make room in your bag if it's full.",
                    Done = () => s_DropSeen && MyDrop == null && !DropComing,
                    Target = () => DropSpot, TargetLabel = "AIRDROP",
                },
                // ---- raiding ----
                new Step
                {
                    Id = "raid", Title = "Raid!",
                    Body = "That hut is the " + Hi("enemy's") + ". Its door has a " + Hi("padlock") + ": only their team can open it. But a door has " + Hi("its own, weaker health") + $" - hit the door ({K(Bind.Attack)}) with your axe until it breaks off. "
                        + "You've also been given a " + Hi("battering ram") + ": hold " + K(Bind.Attack) + " right at a wooden piece and one hit smashes it. Wood is weak; stone, metal and armoured walls take far more.",
                    Goal = "Break into the enemy hut",
                    Progress = () => { var d = HutDoor; return d != null ? $"door {d.DoorHealth.Value:0} / {d.DoorMaxHp:0}" : ""; },
                    Hint = "Stand right at the door and keep hitting the door itself, not the frame round it.",
                    Enter = () => Ask(AskHut),
                    Done = () => s_HutSeen && (HutDoor == null || HutPieces < s_HutMax),
                    Target = () => HutSpot, TargetLabel = "ENEMY HUT",
                    Items = new[] { Item.Ram },
                },
                new Step
                {
                    Id = "raidloot", Title = "Take their stuff",
                    Body = $"You're in! Open their chest ({K(Bind.Interact)}) and take everything - or smash the chest and pick it all up off the floor. That's what raiding is for.",
                    Goal = "Empty the enemy's chest",
                    Hint = "Shift + click each thing in the chest to move it to your bag.",
                    Done = () => s_HutChestSeen && (HutChest == null || HutChest.Empty),
                    Target = () => HutSpot, TargetLabel = "THEIR CHEST",
                },
                // ---- the upgrade station ----
                new Step
                {
                    Id = "upgrade", Title = "The upgrade station",
                    Body = "Left of your machine stands the " + Hi("upgrade station") + $" (the orange arrow). Press {K(Bind.Interact)} on it: " + Hi("UPGRADES") + ", for your whole team. "
                        + "Buy the " + Hi(Cfg.ItemName(Item.WoodGenBuff)) + ": it builds a " + Hi("wood machine") + " that makes wood by itself (two more upgrades speed it up). "
                        + "Fortify All Walls turns every piece you have to stone, then metal, then armoured. You've been given the wood for the first one.",
                    Goal = "Buy the wood gen upgrade",
                    Progress = () => WoodNeed(Cfg.WoodGenWood(1)),
                    Hint = "Stand at the terminal with the orange arrow, press " + Binds.Name(Bind.Interact) + " and click UPGRADE next to " + Cfg.ItemName(Item.WoodGenBuff) + ".",
                    Enter = () => Ask(AskUpgradeWood),
                    Done = () => Cfg.WoodGenLevel(Team) >= 1,
                    Target = () => StationSpot, TargetLabel = "UPGRADES",
                    Unlocks = new[] { TutFeature.Station },
                },
                // ---- with a friend: the host stops here until a second player is in; whoever joins starts here ----
                new Step
                {
                    Id = "friend", Title = "With a friend",
                    BodyF = FriendBody,
                    Goal = "Wait for your friend",
                    Progress = () => { var o = Other; return o == null ? "nobody has joined yet" : $"{o.DisplayName} is here"; },
                    Hint = "Nobody coming? Leave the game (Esc) and play the tutorial Solo to see how it ends.",
                    Skip = () => !Friend,
                    Enter = () => { if (G != null && !G.IsServer) Ask(AskKit); },
                    Done = () => OthersAt(StepAt("friend")),
                    Target = () => { var o = Other; return o != null ? o.transform.position + Vector3.up * 2.4f : (Vector3?)null; }, TargetLabel = "FRIEND",
                },
                new Step
                {
                    Id = "duel", Title = "Fight!",
                    BodyF = () => Other == null ? $"Your friend has left. They can join again - or the tutorial carries on without them in a moment."
                        : "Your friend is on the " + Hi("other team") + ": find them (the yellow marker) and fight! Spear, bow, axe - whatever you have. "
                            + "Whoever is knocked out " + Hi("drops everything") + " they carry and comes back a few seconds later.",
                    Goal = "Fight your friend: first knock-out",
                    Hint = "Poke with the spear up close, or draw the bow from further off. Berries heal.",
                    Skip = () => !Friend,
                    Done = () => Fights() > s_Fights0 || (Other == null && Time.time - s_StepStart > FriendGoneWait),
                    Target = () => { var o = Other; return o != null && !o.Dead.Value ? o.transform.position + Vector3.up * 2.4f : (Vector3?)null; }, TargetLabel = "FRIEND",
                },
                // ---- the finale: the clock runs out with the ball in a machine - the real end of a match ----
                new Step
                {
                    Id = "win", Title = "Win the match",
                    BodyF = () => Other != null
                        ? "The last round - for real. The " + Hi("clock") + " at the top is running, and the ball is dropping into the middle. Whoever has it " + Hi("in their machine") + " when the clock hits zero wins. "
                            + $"Take it out of your friend's machine with {K(Bind.Interact)}, and hit whoever carries it - they drop it."
                        : "This is how a match is won: the ball is " + Hi("in your machine") + " when the " + Hi("clock") + " at the top runs out. It's running now - watch! "
                            + "(If nobody has it in a machine at zero, it's sudden death up in space: rocks only, first kill wins.)",
                    Goal = "Have the ball in your machine when the clock hits 0",
                    Progress = () => G != null && !GameOver ? $"{Mathf.CeilToInt(G.TimeLeft)} s" : "",
                    Enter = () => Ask(AskFinale),
                    Done = () => GameOver,
                    Target = () => GameOver ? null : Ball.Instance != null && Ball.Instance.SocketTeam.Value == Team ? MachineSpot : Me.CarryingBall ? MachineSpot : BallSpot, TargetLabel = "",
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
            s_Bow0 = DummyBowHits;
            s_Near0 = DummyKillsNear;
            s_Far0 = DummyKillsFar;
            s_Fights0 = Fights();
            s_WallMax = WallPieces;
            s_SprintTime = s_GuardTime = s_CrouchTime = 0f;
            s_Jumped = s_Slid = s_Ate = false;
            s_NextAsk = Time.time + 3f;
            SyncStep();
            s_Steps[s_Index].Enter?.Invoke();
        }

        /// <summary>Tell the server which step we're on (so it unlocks crafting, building and the trade station for us too).</summary>
        static void SyncStep()
        {
            var me = Me;
            if (me != null && me.IsSpawned && me.IsOwner && me.TutStep.Value != MyStep) me.TutStep.Value = MyStep;
        }

        /// <summary>Steps that aren't part of this run (the friend steps, playing solo) are passed straight over.</summary>
        static void SkipAhead()
        {
            while (s_Index < s_Steps.Count - 1 && SafeSkip(s_Steps[s_Index])) s_Index++;
        }

        static bool SafeSkip(Step st)
        {
            if (st.Skip == null) return false;
            try { return st.Skip(); } catch (Exception) { return false; }
        }

        /// <summary>How many of the steps before index `upTo` are part of this run (the panel's "12 / 44").</summary>
        static int Shown(int upTo)
        {
            var steps = Steps;
            int n = 0;
            for (int i = 0; i < upTo && i < steps.Count; i++) if (!SafeSkip(steps[i])) n++;
            return n;
        }

        /// <summary>Every frame on the local player (PlayerController).</summary>
        public static void Tick(PlayerController pc, PlayerNet me)
        {
            if (!On || me == null || pc == null) return;
            if (me.TutStep.Value != MyStep) me.TutStep.Value = MyStep; // so the server knows what's unlocked for us
            if (G == null || G.S == GameState.Waiting) return;
            if (!s_Started)
            {
                Build();
                s_Started = true;
                // the host starts at the top. Whoever joins somebody's tutorial isn't taken through the early steps: they
                // start at the "with a friend" step (everything before it unlocked, a starter kit) and wait there for the host
                s_Index = G.IsServer ? 0 : Mathf.Max(0, StepAt("friend"));
                SkipAhead();
                EnterStep();
            }
            // pressing something that isn't unlocked yet: say so (it doesn't do anything)
            if (!Chat.Open && !pc.Paused && !pc.MenuOpen && !me.Dead.Value)
            {
                for (var b = Bind.Forward; b <= Bind.Hotbar6; b++)
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
            if (st.Id == "eat" && !s_Ate && me.Health.Value >= Cfg.MaxHealth - 0.5f && Time.time >= s_NextHungry) { s_NextHungry = Time.time + 3f; me.TutorialRpc(AskHungry); }
            if (Ball.Instance != null && Ball.Instance.SocketTeam.Value == me.Team.Value && Vector3.Distance(p, Cfg.MachinePos(me.Team.Value)) < 9f) s_GuardTime += Time.deltaTime;

            // the later steps' props: note them once they're there, and ask the server again every few seconds for one
            // that's still missing (a dummy to replace the one just destroyed, an airdrop or a hut that never came)
            bool ask = Time.time >= s_NextAsk;
            if (ask) s_NextAsk = Time.time + 3f;
            switch (st.Id)
            {
                case "shoot": case "melee": case "ranged":
                    if (ask && DummyCount() < 2) me.TutorialRpc(AskDummies);
                    break;
                case "airdrop": case "loot":
                    if (MyDrop != null) s_DropSeen = true;
                    else if (ask && st.Id == "airdrop" && !s_DropSeen && !DropComing) me.TutorialRpc(AskAirdrop);
                    break;
                case "raid": case "raidloot":
                {
                    // (seen once its door is there: on a client its pieces can turn up a frame apart)
                    if (HutDoor != null) s_HutSeen = true;
                    s_HutMax = Mathf.Max(s_HutMax, HutPieces);
                    if (HutChest != null) s_HutChestSeen = true;
                    if (ask && st.Id == "raid" && !s_HutSeen) me.TutorialRpc(AskHut);
                    break;
                }
            }

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
            SkipAhead();
            EnterStep();
        }

        /// <summary>The server's news for this player's guide (PlayerNet.TutEventRpc): 1 = you hit a training dummy,
        /// 2 = that hit finished it; arg 1 = from range (Vehicle.DummyFar), 0 = up close.</summary>
        public static void OnEvent(byte what, byte arg)
        {
            if (!On || (what != 1 && what != 2)) return;
            DummyHits++;
            var me = Me;
            if (me != null && me.HeldItem == Item.Bow) DummyBowHits++;
            if (what == 2) { if (arg == 1) DummyKillsFar++; else DummyKillsNear++; }
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

        /// <summary>Server, every frame (NetGame.Update): keep the clock stopped (until the finale), drop late joiners into
        /// the match, drop the glass wall once every player still on those steps has walked up to it (or WallWait seconds
        /// after the first one did), and run the finale.</summary>
        public static void ServerTick(NetGame g)
        {
            if (!On || g == null || !g.IsServer) return;
            if (g.S == GameState.Waiting || g.S == GameState.GameOver) return;
            if (s_Finale != 2 && !g.TimerPaused.Value) g.TimerPaused.Value = true;
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
            if (s_Finale != 0) { ServerTickFinale(g); return; }
            if (g.S != GameState.PreBall) return;
            int there = 0, total = 0, drop = StepAt("drop");
            foreach (var p in PlayerNet.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (p.TutStep.Value > drop) continue; // (past the wall steps: a friend who joined at the "with a friend" point)
                total++;
                if (p.TutAtWall.Value) there++;
            }
            if (there == 0) { s_FirstAtWall = -1f; return; }
            if (s_FirstAtWall < 0) s_FirstAtWall = Time.time;
            if (there >= total || Time.time - s_FirstAtWall > WallWait)
                g.PhaseEnd.Value = g.NetworkManager.ServerTime.Time - 1.0; // NetGame drops the wall and the ball this frame
        }

        /// <summary>
        /// Server: the finale. Asked for (1): the wall drops first if it's still up; then the clock is let go with a short
        /// time on it - alone, the ball is put in your machine and the match ends 10 s later; with a friend the ball drops
        /// into the middle and it's a real two minute match for it. Running (2): NetGame ends the match the usual way
        /// when the clock runs out (the ball in a machine: that team wins, with the victory cutscene). The tutorial never
        /// goes to sudden death: with nobody's ball in a machine at zero, a lone player's is put back in theirs, and
        /// friends get overtime.
        /// </summary>
        static void ServerTickFinale(NetGame g)
        {
            double now = g.NetworkManager.ServerTime.Time;
            var b = Ball.Instance;
            int players = 0;
            foreach (var p in PlayerNet.All) if (p != null && p.IsSpawned) players++;
            bool friends = players > 1;
            if (s_Finale == 1)
            {
                if (g.S == GameState.PreBall) { g.PhaseEnd.Value = now - 1.0; return; } // (the wall and the dome go first)
                if (g.S != GameState.BallLive || b == null || !b.IsSpawned) return;
                if (friends) b.ServerDropFromSky(Cfg.BallDropPoint); // a fair start: it falls into the middle
                else if (b.SocketTeam.Value != s_FinaleTeam) b.ServerSocket(s_FinaleTeam);
                g.TimerPaused.Value = false;
                g.PhaseEnd.Value = now + (friends ? FriendMatchSeconds : SoloFinaleSeconds);
                s_Finale = 2;
                g.Broadcast(friends ? $"The last round: {FriendMatchSeconds:0} seconds - whoever has the ball in their machine at 0 wins!"
                    : "The clock is running: the ball is in your machine - hold on until it hits 0!");
                return;
            }
            if (g.S != GameState.BallLive || g.PhaseEnd.Value - now > 0.6) return;
            // the clock is about to run out with the ball in nobody's machine
            if (b != null && b.IsSpawned && b.SocketTeam.Value >= 0) return;
            if (friends)
            {
                g.PhaseEnd.Value = now + OvertimeSeconds;
                g.Broadcast($"Nobody has the ball in their machine - {OvertimeSeconds:0} more seconds!");
            }
            else if (b != null && b.IsSpawned) b.ServerSocket(s_FinaleTeam);
        }

        /// <summary>Server: what a player's guide asked for (PlayerNet.TutorialRpc; the Ask... numbers).</summary>
        public static void ServerAction(PlayerNet p, byte action)
        {
            var g = G;
            if (!On || g == null || !g.IsServer || p == null || !p.IsSpawned) return;
            switch (action)
            {
                case AskWall: ServerAtWall(p); break;
                case AskHungry: ServerHungry(p); break;
                case AskDummies: ServerDummies(p); break;
                case AskAirdrop: ServerAirdrop(p); break;
                case AskHut: ServerHut(p); break;
                case AskUpgradeWood: ServerUpgradeWood(p); break;
                case AskFinale: if (s_Finale == 0) { s_Finale = 1; s_FinaleTeam = p.Team.Value; } break;
                case AskKit: ServerKit(p); break;
            }
        }

        /// <summary>Server: a player reached the glass wall step.</summary>
        public static void ServerAtWall(PlayerNet p)
        {
            var g = G;
            if (!On || g == null || p == null || p.TutAtWall.Value) return;
            p.TutAtWall.Value = true;
            if (g.S != GameState.PreBall) return;
            int left = 0, drop = StepAt("drop");
            foreach (var o in PlayerNet.All) if (o != null && o.IsSpawned && !o.TutAtWall.Value && o.TutStep.Value <= drop) left++;
            if (left > 0) g.Broadcast($"{Cfg.TeamLabel(p.Team.Value)} is at the glass wall - it drops when everyone gets there ({left} to go)");
        }

        /// <summary>Server: the eat step - knock the player's health down a bit so there's something to heal.</summary>
        public static void ServerHungry(PlayerNet p)
        {
            if (!On || p == null || p.Dead.Value) return;
            float to = Mathf.Round(Cfg.MaxHealth * 0.5f);
            if (p.Health.Value > to) p.Health.Value = to;
        }

        /// <summary>Server: nothing but the ground (no tree, building, player...) in this box.</summary>
        static bool ServerClear(Vector3 c, Vector3 half)
        {
            foreach (var h in Physics.OverlapBox(c, half, Quaternion.identity, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                if (h.GetComponentInParent<GroundMarker>() == null) return false;
            return true;
        }

        /// <summary>Server: a spot out in the open on this team's own side of the map - outside the bases, clear of the
        /// glass dome and the map's edge.</summary>
        static bool ServerSpotOk(Vector3 c, int team)
        {
            float half = Cfg.MapHalf - 6f;
            return Cfg.RegionOf(c) == Cfg.RegionOf(Cfg.BaseCenter[team]) && Cfg.BaseTeamAt(c) < 0
                && new Vector2(c.x, c.z).magnitude > MapBuilder.DomeRadius + 4f && Mathf.Abs(c.x) < half && Mathf.Abs(c.z) < half;
        }

        /// <summary>Server: a clear spot in front of this team's base - `forward` metres out from its front edge, `side`
        /// metres along it (the first of the choices with nothing in the way; the very first if they're all taken).</summary>
        static Vector3 ServerFrontSpot(int team, float[] forward, float[] side, Vector3 boxUp, Vector3 half)
        {
            var back = Cfg.BackDir(team);
            var along = Vector3.Cross(Vector3.up, back);
            Vector3 first = default;
            bool any = false;
            foreach (float f in forward)
                foreach (float s in side)
                {
                    var c = Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + f) + along * s;
                    c.y = MapBuilder.Height(c.x, c.z);
                    if (!any) { first = c; any = true; }
                    if (ServerSpotOk(c, team) && ServerClear(c + boxUp, half)) return c;
                }
            return first;
        }

        static readonly float[] k_DummySide = { -2.5f, 2.5f, -6f, 6f, 0f, -9.5f, 9.5f };

        /// <summary>Server: keep two training dummies standing in front of this player's base (the shoot / melee / ranged
        /// steps ask again whenever one's been destroyed).</summary>
        static void ServerDummies(PlayerNet p)
        {
            if (Bootstrap.I == null) return;
            int team = p.Team.Value;
            var yard = TrainSpot(team);
            int alive = 0;
            foreach (var v in Vehicle.All) if (v != null && v.IsSpawned && v.IsDummy && v.Hp.Value > 0f && Flat(v.transform.position, yard) < 16f) alive++;
            var back = Cfg.BackDir(team);
            var along = Vector3.Cross(Vector3.up, back);
            float yaw = Quaternion.LookRotation(back).eulerAngles.y; // (facing the base: the target on its chest is towards you)
            foreach (float s in k_DummySide)
            {
                if (alive >= 2) break;
                var at = yard + along * s;
                at.y = MapBuilder.Height(at.x, at.z) + 0.05f;
                if (!ServerClear(at + Vector3.up * 1.1f, new Vector3(0.55f, 0.8f, 0.55f))) continue;
                Vehicle.ServerSpawn(Vehicle.Dummy, at, yaw);
                Fx.Server(FxKind.Spawn, at, Vector3.up);
                alive++;
            }
        }

        /// <summary>Server: a real airdrop for this player's team, coming down out in front of their base (NetGame.ServerTutorialDrop).</summary>
        static void ServerAirdrop(PlayerNet p)
        {
            int team = p.Team.Value;
            var at = ServerFrontSpot(team, new[] { 10f, 14f }, new[] { -9f, 9f, -14f, 14f, 0f }, Vector3.up * 1.4f, new Vector3(1f, 0.9f, 1f));
            G.ServerTutorialDrop(team, at);
        }

        static Structure ServerPiece(PieceType t, int team, Vector3 pos, Quaternion rot)
        {
            var go = UnityEngine.Object.Instantiate(Bootstrap.I.structurePrefab, pos, rot);
            var s = go.GetComponent<Structure>();
            s.ServerInit(t, team, default, false); // (not a grid piece of anyone's base: it just stands there)
            go.GetComponent<NetworkObject>().Spawn(true);
            return s;
        }

        /// <summary>
        /// Server: the hut the raid steps break into - a foundation, three wooden walls, a doorway (its door shut and
        /// padlocked: it belongs to the other team) and a roof, with a chest of loot inside - out in front of this
        /// player's base, the door towards it. Once per team. The player is handed a battering ram as well.
        /// </summary>
        static void ServerHut(PlayerNet p)
        {
            int team = p.Team.Value;
            if (team < 0 || team >= s_HutBuilt.Length || s_HutBuilt[team] || Bootstrap.I == null) return;
            s_HutBuilt[team] = true;
            int enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var at = ServerFrontSpot(team, new[] { 8f, 12f }, new[] { 11f, -11f, 15f, -15f, 7f, -7f }, Vector3.up * 2.2f, new Vector3(2.4f, 1.9f, 2.4f));
            var rot = Quaternion.LookRotation(Cfg.BackDir(team)); // (its +z, the door's side, faces the base)
            var floor = at + Vector3.down * 0.25f;                // (sunk in a little, so it sits on uneven ground)
            var top = floor + Vector3.up * 1f;
            var side = rot * Quaternion.Euler(0, 90f, 0);
            ServerPiece(PieceType.Foundation, enemy, floor, rot);
            ServerPiece(PieceType.Doorway, enemy, top + rot * new Vector3(0, 0, 1.5f), rot);
            ServerPiece(PieceType.Wall, enemy, top + rot * new Vector3(0, 0, -1.5f), rot);
            ServerPiece(PieceType.Wall, enemy, top + rot * new Vector3(1.5f, 0, 0), side);
            ServerPiece(PieceType.Wall, enemy, top + rot * new Vector3(-1.5f, 0, 0), side);
            ServerPiece(PieceType.Floor, enemy, top + Vector3.up * 3f, rot);
            var cgo = UnityEngine.Object.Instantiate(Bootstrap.I.containerPrefab, top + rot * new Vector3(0, 0, -0.8f), rot);
            cgo.GetComponent<Container>().ServerInit(Container.Chest, enemy, Cfg.ChestSlots, new List<ItemStack>
            {
                ItemStack.Of(Cfg.GatherItem(Item.Wood), 300), ItemStack.Of(Item.Arrow, 10), ItemStack.Of(Item.Berry, 5),
            });
            cgo.GetComponent<NetworkObject>().Spawn(true);
            Fx.Server(FxKind.Spawn, top, Vector3.up);
            if (p.Count(Item.Ram) == 0 && p.ServerGive(Item.Ram, 1, Mathf.Clamp(Cfg.MaxData(Item.Ram), 0, 255)) == 0)
                p.NotifyPublic("You've been given a battering ram");
        }

        /// <summary>Server: the upgrade step - top the player's wood up to what the wood machine costs (until their team has one).</summary>
        static void ServerUpgradeWood(PlayerNet p)
        {
            int team = p.Team.Value;
            if (Cfg.WoodGenLevel(team) > 0) return;
            var wood = Cfg.GatherItem(Item.Wood);
            int need = Cfg.BaseUpgradeRecipe(Item.WoodGenBuff, team).Wood - p.Count(wood);
            if (need <= 0) return;
            p.ServerGive(wood, need);
            p.NotifyPublic($"You've been given {need} wood for the upgrade");
        }

        /// <summary>Server: whoever joins a friend's tutorial skips the gathering steps, so they're handed what those would have made.</summary>
        static void ServerKit(PlayerNet p)
        {
            if (!s_Kit.Add(p.OwnerClientId)) return;
            foreach (var it in new[] { Item.Hatchet, Item.Spear, Item.Bow, Item.BuildingPlan })
                if (p.Count(it) == 0) p.ServerGive(it, 1, Mathf.Clamp(Cfg.MaxData(it), 0, 255));
            p.ServerGive(Item.Arrow, 20);
            p.ServerGive(Item.Berry, 5);
            p.ServerGive(Cfg.GatherItem(Item.Wood), 400);
            p.NotifyPublic("Starter kit: hatchet, spear, bow, arrows, building plan, berries and wood");
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Hud.DrawGameOver: the "tutorial complete" card goes on top of the victory screen (drawn under it, the
        /// winners' all-black screen would hide it).</summary>
        public static void DrawOverGameOver(float k, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            s_OverScreen = true;
            try { Draw(k, label, small, fill, shadowed); }
            finally { s_OverScreen = false; }
        }

        /// <summary>The tutorial panel (left side), a goal strip while menus are open, and the marker.</summary>
        public static void Draw(float k, GUIStyle label, GUIStyle small, Action<Rect, Color> fill, Action<Rect, string, GUIStyle> shadowed)
        {
            var pc = PlayerController.Local;
            if (!On || !s_Started || pc == null) return;
            float sw = Screen.width, sh = Screen.height;
            if (GameOver != s_OverScreen) return; // (once the match is over: only the card over the victory screen)
            if (s_Finished)
            {
                if (pc.MenuOpen || pc.Paused) return;
                float fw = 340 * k, fx = 14, fy = 230 * k;
                var fb = new GUIStyle(small) { wordWrap = true, richText = true };
                string txt = "A match is won like that: the ball " + Hi("in your machine") + " when the clock ends.\n"
                    + "While it's in your machine, your team gathers " + Hi($"{Mathf.RoundToInt((Cfg.BallGatherMul - 1f) * 100f)}% more") + ".\n"
                    + "Everything is unlocked in a real match - your trade stations add " + Hi("high walls, guns, armour, a saddle") + " and more.\n"
                    + "Real matches have several " + Hi("airdrops") + $": a warning counts down {NetGame.DropWarning:0} seconds to each UFO.\n"
                    + "The " + Hi("upgrade station") + " (orange arrow) has two more wood gen speeds, and Fortify All Walls: stone, metal, then armoured.\n"
                    + "Horses: with a " + Hi("saddle") + $", {K(Bind.Interact)} rides one; {K(Bind.Crouch)} or {K(Bind.Slide)} gets you off ({K(Bind.Interact)} still does what it does on foot). Hold " + Hi("berries") + $" and {K(Bind.Attack)} on a hurt horse to feed it.\n"
                    + $"Hold {K(Bind.Scoreboard)} for the scoreboard (and to whisper to a player).\n"
                    + "Tip: Settings > Display changes the shadows, post processing, the UI's font and size, the grass and the world's colours.\n"
                    + Hi("Leave game") + ", then host a real match!";
                float fh = 30 * k + 30 * k + fb.CalcHeight(new GUIContent(txt), fw - 24 * k) + 12 * k;
                fill(new Rect(fx, fy, fw, fh), new Color(0.04f, 0.06f, 0.1f, 0.82f));
                fill(new Rect(fx, fy, 4 * k, fh), new Color(0.49f, 1f, 0.49f, 0.9f));
                shadowed(new Rect(fx + 14 * k, fy + 6 * k, fw - 24 * k, 24 * k), $"<size={Mathf.RoundToInt(13 * k)}><color=#7dff7d>TUTORIAL COMPLETE</color></size>", small);
                shadowed(new Rect(fx + 14 * k, fy + 28 * k, fw - 24 * k, 30 * k), "<b>You did it!</b>", new GUIStyle(label) { richText = true });
                GUI.Label(new Rect(fx + 14 * k, fy + 60 * k, fw - 24 * k, fh - 60 * k), txt, fb);
                return;
            }
            if (s_OverScreen) return; // (still on the last step as the match ends: it ticks off in a moment)
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
                var gr = new Rect(sw / 2 - 330 * k, sh - 40 * k, 660 * k, 32 * k);
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
            float goalH = Mathf.Max(26 * k, body.CalcHeight(new GUIContent(goal), w - 24 * k));
            float h = 26 * k + 28 * k + bodyH + newH + 4 * k + goalH + extraH + 6 * k;
            fill(new Rect(x, y, w, h), new Color(0.04f, 0.06f, 0.1f, 0.82f));
            fill(new Rect(x, y, 4 * k, h), new Color(1f, 0.82f, 0.29f, 0.9f));
            // (the steps that aren't part of this run - the friend ones, playing solo - aren't counted)
            int shownAt = Shown(s_Index), shownAll = Mathf.Max(1, Shown(s_Steps.Count));
            float pct = (float)shownAt / shownAll;
            fill(new Rect(x, y + h - 3 * k, w * pct, 3 * k), new Color(1f, 0.82f, 0.29f, 0.8f));
            shadowed(new Rect(x + 14 * k, y + 5 * k, w - 24 * k, 22 * k), $"<size={Mathf.RoundToInt(12 * k)}><color=#ffd24a>TUTORIAL  {shownAt + 1} / {shownAll}</color></size>", small);
            shadowed(new Rect(x + 14 * k, y + 24 * k, w - 24 * k, 30 * k), $"<b>{st.Title}</b>", titleSt);
            float ty = y + 54 * k;
            GUI.Label(new Rect(x + 14 * k, ty, w - 24 * k, bodyH), bodyText, body);
            ty += bodyH;
            if (newText != null) { GUI.Label(new Rect(x + 14 * k, ty + 2 * k, w - 24 * k, newH), newText, body); ty += newH; }
            shadowed(new Rect(x + 14 * k, ty + 4 * k, w - 24 * k, goalH), goal, body);
            if (extra != null) GUI.Label(new Rect(x + 14 * k, ty + 4 * k + goalH, w - 24 * k, extraH), extra, body);

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
            try { return st.Progress() ?? ""; } catch (Exception) { return ""; }
        }
    }
}
