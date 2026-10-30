using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// A bot team's house (BotBrain.cs): built with the building plan through PlaceRpc, like a player. Each team builds
    /// in one of four styles this match (HouseStyle, dealt out by the map seed so the teams' bases differ and the next
    /// match's do too):
    ///   Bunker:   a two-storey room round the bedrock with a roof, inside a two-storey outer ring with a roof (12 x 12 m);
    ///   Tower:    a three-storey tower on the bedrock with a roof, inside a one-storey yard wall with windows;
    ///   Compound: a one-storey room with a roof, inside a big two-storey ring (18 x 18 m) with windows;
    ///   Keep:     the two-storey room with a roof, inside a one-storey ring with a little tower on each corner.
    /// It goes up in stages, each one only once the one before holds it up (the Bunker's, for one):
    ///   0. walls round the machine's bedrock (6 x 6 m), with two doorways (doors) in the front - a closed room;
    ///   1. the storeys of walls on top;
    ///   2. a roof over it;
    ///   3. foundations all round it (the pad);
    ///   4. an outer ring of walls on their edge, two doorways in the front, lined up with the inner ones;
    ///   5. a second storey on the outer ring (the Keep: floors and walls on the corner cells);
    ///   6. a roof over the outer ring (the Keep: over the corner towers).
    /// Anything that can't go in (the machine, a chest, someone else's piece in the way) is skipped for a while, then for
    /// good. Fortify All Walls (the upgrade station) turns it all to stone / metal / armoured, and new pieces come out at
    /// the team's level. A bot gets in and out by its front doors: Route walks it round to the front, lines it up with
    /// the doorway, opens the door and goes through; it shuts it behind itself.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>One piece of the house plan.</summary>
        public struct PlanPiece
        {
            public PieceType Type;
            public PieceKey Key;
            public byte Stage;
        }

        /// <summary>The kinds of base a bot team builds (one each a match: StyleOf).</summary>
        public enum HouseStyle : byte { Bunker, Tower, Compound, Keep }

        static readonly List<PlanPiece>[] s_Plan = new List<PlanPiece>[4];
        static readonly Vector3[] s_PlanAt = new Vector3[4];
        static readonly HouseStyle[] s_PlanStyle = new HouseStyle[4];
        static readonly HouseStyle[] s_Styles = new HouseStyle[4];
        static int s_StyleSeed = int.MinValue;
        /// <summary>(tests) force every team's house style (-1: dealt out by the map seed).</summary>
        public static int TestStyle = -1;

        /// <summary>The style `team` builds in this match: the four styles dealt out among the teams from the map seed, so no
        /// two teams build the same base and another match (another seed) deals them differently.</summary>
        public static HouseStyle StyleOf(int team)
        {
            team = Mathf.Clamp(team, 0, 3);
            if (TestStyle >= 0) return (HouseStyle)(TestStyle & 3);
            int seed = Cfg.MapSeed;
            if (s_StyleSeed != seed)
            {
                s_StyleSeed = seed;
                var r = new System.Random(seed ^ 0x3c6ef372);
                var pool = new List<int> { 0, 1, 2, 3 };
                for (int t = 0; t < 4; t++) { int i = r.Next(pool.Count); s_Styles[t] = (HouseStyle)pool[i]; pool.RemoveAt(i); }
            }
            return s_Styles[team];
        }

        /// <summary>How many cells out from the middle the outer ring stands (2: 12 x 12 m; the Compound 3: 18 x 18 m).</summary>
        static int OuterR(HouseStyle s) => s == HouseStyle.Compound ? 3 : 2;
        static int OuterR(int team) => OuterR(StyleOf(team));
        /// <summary>Each team's front doorways: [team * 2] the inner house's, [team * 2 + 1] the outer ring's.</summary>
        static readonly List<PieceKey>[] s_Doors = new List<PieceKey>[8];
        static readonly List<PieceKey> s_Edges = new List<PieceKey>(32);

        /// <summary>The team's house plan, in building order (made once per base position).</summary>
        public static List<PlanPiece> HousePlan(int team)
        {
            team = Mathf.Clamp(team, 0, 3);
            var c = Cfg.BaseCenter[team];
            var style = StyleOf(team);
            if (s_Plan[team] != null && s_PlanAt[team] == c && s_PlanStyle[team] == style) return s_Plan[team];
            var plan = s_Plan[team] = new List<PlanPiece>(120);
            s_PlanAt[team] = c;
            s_PlanStyle[team] = style;
            var inner = s_Doors[team * 2] = new List<PieceKey>(2);
            var outer = s_Doors[team * 2 + 1] = new List<PieceKey>(2);
            int ci = Mathf.RoundToInt(c.x / Cfg.Cell), cj = Mathf.RoundToInt(c.z / Cfg.Cell);
            int R = OuterR(style);
            int storeys = style == HouseStyle.Tower ? 3 : style == HouseStyle.Compound ? 1 : 2;
            bool windows = style != HouseStyle.Bunker;
            // the inner room round the bedrock: its storeys of walls (doors on the ground floor) and a roof
            for (int l = 0; l < storeys; l++)
            {
                EdgesRound(ci, cj, 1, l);
                AddEdges(team, plan, (byte)(l == 0 ? 0 : 1), l == 0, l == 0 ? inner : null, windows && l > 0);
            }
            for (int i = ci - 1; i <= ci; i++)
                for (int j = cj - 1; j <= cj; j++)
                    plan.Add(new PlanPiece { Type = PieceType.Floor, Key = new PieceKey(PieceKey.KFloor, i, j, storeys, 0), Stage = 2 });
            // the outer ring: foundations (front first), then walls on their edge with the doors in front
            int f0 = plan.Count;
            for (int i = ci - R; i <= ci + R - 1; i++)
                for (int j = cj - R; j <= cj + R - 1; j++)
                    if (i < ci - 1 || i > ci || j < cj - 1 || j > cj)
                        plan.Add(new PlanPiece { Type = PieceType.Foundation, Key = new PieceKey(PieceKey.KFoundation, i, j, 0, 0), Stage = 3 });
            plan.Sort(f0, plan.Count - f0, Comparer<PlanPiece>.Create((a, b) => Facing(team, a).CompareTo(Facing(team, b))));
            EdgesRound(ci, cj, R, 0);
            AddEdges(team, plan, 4, true, outer, windows);
            switch (style)
            {
                case HouseStyle.Bunker:
                case HouseStyle.Compound:
                    // a second storey on the ring - and the Bunker roofs it over
                    EdgesRound(ci, cj, R, 1);
                    AddEdges(team, plan, 5, false, null, windows);
                    if (style == HouseStyle.Bunker)
                        for (int i = ci - R; i <= ci + R - 1; i++)
                            for (int j = cj - R; j <= cj + R - 1; j++)
                                if (i < ci - 1 || i > ci || j < cj - 1 || j > cj)
                                    plan.Add(new PlanPiece { Type = PieceType.Floor, Key = new PieceKey(PieceKey.KFloor, i, j, 2, 0), Stage = 6 });
                    break;
                case HouseStyle.Keep:
                    // a little tower on each corner of the ring: a floor over the corner cell, walls round it, a roof
                    foreach (var (i, j) in new[] { (ci - R, cj - R), (ci + R - 1, cj - R), (ci - R, cj + R - 1), (ci + R - 1, cj + R - 1) })
                    {
                        plan.Add(new PlanPiece { Type = PieceType.Floor, Key = new PieceKey(PieceKey.KFloor, i, j, 1, 0), Stage = 5 });
                        plan.Add(new PlanPiece { Type = PieceType.Wall, Key = new PieceKey(PieceKey.KEdge, i, j, 1, 0), Stage = 5 });
                        plan.Add(new PlanPiece { Type = PieceType.Wall, Key = new PieceKey(PieceKey.KEdge, i - 1, j, 1, 0), Stage = 5 });
                        plan.Add(new PlanPiece { Type = PieceType.Window, Key = new PieceKey(PieceKey.KEdge, i, j, 1, 1), Stage = 5 });
                        plan.Add(new PlanPiece { Type = PieceType.Window, Key = new PieceKey(PieceKey.KEdge, i, j - 1, 1, 1), Stage = 5 });
                        plan.Add(new PlanPiece { Type = PieceType.Floor, Key = new PieceKey(PieceKey.KFloor, i, j, 2, 0), Stage = 6 });
                    }
                    break;
                // (the Tower: a yard wall is all - the height's in the middle)
            }
            return plan;
        }

        /// <summary>The edges round the square of cells ci-r .. ci+r-1, cj-r .. cj+r-1, at level l (into s_Edges).</summary>
        static void EdgesRound(int ci, int cj, int r, int l)
        {
            s_Edges.Clear();
            for (int k = -r; k < r; k++)
            {
                s_Edges.Add(new PieceKey(PieceKey.KEdge, ci - r - 1, cj + k, l, 0)); // -x side
                s_Edges.Add(new PieceKey(PieceKey.KEdge, ci + r - 1, cj + k, l, 0)); // +x
                s_Edges.Add(new PieceKey(PieceKey.KEdge, ci + k, cj - r - 1, l, 1)); // -z
                s_Edges.Add(new PieceKey(PieceKey.KEdge, ci + k, cj + r - 1, l, 1)); // +z
            }
        }

        /// <summary>s_Edges into the plan, the back ones first; doors: the front middle ones are doorways (noted in `doors`);
        /// windows: every other wall that isn't at the front is a window.</summary>
        static void AddEdges(int team, List<PlanPiece> plan, byte stage, bool doors, List<PieceKey> doorKeys, bool windows = false)
        {
            var c = Cfg.BaseCenter[team];
            var lat = Vector3.Cross(Vector3.up, Cfg.BackDir(team));
            int from = plan.Count;
            foreach (var k in s_Edges)
            {
                var t = PieceType.Wall;
                float facing = FacingKey(team, k);
                if (doors && facing < -0.7f)
                {
                    BuildGrid.Pose(PieceType.Wall, k, out var p, out _);
                    if (Mathf.Abs(Vector3.Dot(p - c, lat)) < 2f) { t = PieceType.Doorway; doorKeys?.Add(k); }
                }
                if (windows && t == PieceType.Wall && facing > -0.7f && ((k.I + k.J + k.D) & 1) == 0) t = PieceType.Window;
                plan.Add(new PlanPiece { Type = t, Key = k, Stage = stage });
            }
            // (the doorways last; walls and windows alike, the back ones first)
            plan.Sort(from, plan.Count - from, Comparer<PlanPiece>.Create((a, b) =>
            {
                int ra = a.Type == PieceType.Doorway ? 1 : 0, rb = b.Type == PieceType.Doorway ? 1 : 0;
                return ra != rb ? ra.CompareTo(rb) : Facing(team, b).CompareTo(Facing(team, a));
            }));
        }

        static float Facing(int team, PlanPiece p) => FacingKey(team, p.Key);

        /// <summary>How much a piece faces out the back of the base (1: right at the back, -1: the front).</summary>
        public static float FacingKey(int team, PieceKey k)
        {
            var p = BuildGrid.PieceCenter(k);
            var d = p - Cfg.BaseCenter[team];
            d.y = 0f;
            return d.sqrMagnitude < 0.01f ? 0f : Vector3.Dot(d.normalized, Cfg.BackDir(team));
        }

        /// <summary>How many pieces of the team's house plan are up.</summary>
        public static int HouseBuilt(int team, out int total)
        {
            var plan = HousePlan(team);
            total = plan.Count;
            int n = 0;
            foreach (var p in plan)
                if (BuildGrid.Registry.TryGetValue(p.Key, out var s) && s != null && s.Team.Value == team) n++;
            return n;
        }

        static readonly float[] s_EnclosedAt = { -10f, -10f, -10f, -10f };
        static readonly bool[] s_Enclosed = new bool[4];

        /// <summary>The team's machine is shut in: its room's ground-floor walls and doorways are all up (or the outer ring's
        /// are) - a piece or two that can't go in anywhere (the machine, a chest in the way) aside. Looked at once a second.</summary>
        public static bool Enclosed(int team)
        {
            if (team < 0 || team > 3 || Cfg.Builder) return true;
            if (Time.time - s_EnclosedAt[team] < 1f) return s_Enclosed[team];
            s_EnclosedAt[team] = Time.time;
            int missInner = 0, missOuter = 0;
            foreach (var p in HousePlan(team))
            {
                if (p.Stage != 0 && p.Stage != 4) continue;
                if (BuildGrid.OnBedrock(p.Key)) continue;
                if (BuildGrid.Registry.TryGetValue(p.Key, out var s) && s != null && s.IsSpawned) continue;
                if (p.Stage == 0) missInner++; else missOuter++;
            }
            return s_Enclosed[team] = missInner <= 1 || missOuter <= 1;
        }

        // ---------------------------------------------------------------- building it

        readonly Dictionary<PieceKey, float> m_PieceFail = new Dictionary<PieceKey, float>();
        readonly Dictionary<PieceKey, int> m_PieceFails = new Dictionary<PieceKey, int>();

        /// <summary>The next piece of the plan it can put up now (held up, free, not refused lately).</summary>
        bool NextPiece(out PlanPiece piece)
        {
            piece = default;
            var g = NetGame.Instance;
            if (Cfg.Builder || g == null || g.S == GameState.SuddenDeath || !Tutorial.AllowsFor(m_P, TutFeature.Build)) return false;
            int team = MyTeam;
            foreach (var p in HousePlan(team))
            {
                var k = p.Key;
                if (BuildGrid.Registry.ContainsKey(k)) continue;
                if (m_PieceFails.TryGetValue(k, out int fails) && fails >= 3) continue;
                if (m_PieceFail.TryGetValue(k, out var until) && Time.time < until) continue;
                if (!BuildGrid.CanBuildAt(team, k) || BuildGrid.OnBedrock(k) || Structure.RebuildWait(k) > 0f) continue;
                if (BuildGrid.IsOccupied(k, BuildGrid.Registry.ContainsKey) || !BuildGrid.IsSupported(k, BuildGrid.Registry.ContainsKey)) continue;
                piece = p;
                return true;
            }
            return false;
        }

        void TickBuild()
        {
            var cur = Cfg.CurrencyItem;
            if (!NextPiece(out var piece) || m_P.Count(cur) < Cfg.PieceWood(piece.Type)) { Done(); return; }
            BuildGrid.Pose(piece.Type, piece.Key, out var wp, out _);
            var mid = BuildGrid.PieceCenter(piece.Key);
            float far = Vector3.Distance(wp, transform.position);
            // walls are put up from outside them, so it never shuts itself in (the roof from anywhere near)
            var stand = mid;
            if (piece.Type != PieceType.Floor)
            {
                var outward = mid - Cfg.BaseCenter[MyTeam];
                outward.y = 0f;
                if (outward.sqrMagnitude > 0.01f) stand = mid + outward.normalized * 2.5f;
            }
            bool wrongSide = piece.Type != PieceType.Floor && piece.Type != PieceType.Foundation && far < 9f
                && Flat(transform.position, Cfg.BaseCenter[MyTeam]) < Flat(mid, Cfg.BaseCenter[MyTeam]) - 0.6f && Flat(transform.position, mid) < 4f;
            if (far > 9f || wrongSide) // (the server takes it from up to 11 m)
            {
                GoTo(stand, 1.2f, Flat(stand, transform.position) > 6f);
                return;
            }
            m_Arrived = true;
            m_HasLook = true;
            m_LookAt = mid + Vector3.up * 0.6f;
            if (Time.time < m_NextUse || Eating) return;
            m_NextUse = Time.time + Mathf.Max(Cfg.BuildCooldown + 0.05f, Random.Range(0.4f, 0.85f));
            int slot = HotbarFor(Item.BuildingPlan);
            if (slot < 0) { Done(); return; }
            Hold(slot);
            m_P.PlaceRpc((byte)piece.Type, piece.Key.I, piece.Key.J, piece.Key.L, piece.Key.D);
            if (BuildGrid.Registry.ContainsKey(piece.Key)) { Built++; m_PieceFails.Remove(piece.Key); }
            else
            {
                // blocked (the machine, a chest, a player's piece...): skip it a while - after three goes, for good
                m_PieceFails[piece.Key] = (m_PieceFails.TryGetValue(piece.Key, out int n) ? n : 0) + 1;
                m_PieceFail[piece.Key] = Time.time + 60f;
            }
        }

        // ---------------------------------------------------------------- in and out by the doors

        /// <summary>Doors it has opened and not shut yet (it shuts every one behind it).</summary>
        readonly List<Structure> m_OpenedDoors = new List<Structure>(2);
        readonly Dictionary<Structure, float> m_DoorWait = new Dictionary<Structure, float>(), m_DoorNear = new Dictionary<Structure, float>();
        float m_NextDoor;
        /// <summary>(tests) doors it has shut behind itself.</summary>
        public int DoorsShut { get; private set; }

        /// <summary>Which part of its base a point is in: 0 inside the inner room, 1 in the outer ring, 2 outside (only
        /// counting the rings that have their doorways up).</summary>
        static int RingOf(int team, Vector3 p, bool innerUp, bool outerUp)
        {
            var d = p - Cfg.BaseCenter[team];
            float m = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z));
            if (innerUp && m < 3f) return 0;
            if (outerUp && m < 3f * OuterR(team)) return 1;
            return 2;
        }

        /// <summary>The doorway of ring b (0 inner, 1 outer) nearest to `from`, if one's up.</summary>
        static bool DoorOf(int team, int b, Vector3 from, out Vector3 at, out Structure door)
        {
            at = default;
            door = null;
            HousePlan(team);
            var keys = s_Doors[team * 2 + b];
            if (keys == null) return false;
            float bd = float.MaxValue;
            foreach (var k in keys)
            {
                if (!BuildGrid.Registry.TryGetValue(k, out var s) || s == null || !s.IsSpawned || s.Team.Value != team || s.PType != PieceType.Doorway) continue;
                BuildGrid.Pose(PieceType.Doorway, k, out var p, out _);
                float d = Flat(p, from);
                if (d < bd) { bd = d; at = p; door = s; }
            }
            return door != null;
        }

        /// <summary>
        /// Where to head for right now to get to m_Goal: m_Goal itself, unless it's on the other side of its own house's
        /// walls - then the front door: round to the front, lined up with the doorway, through it (opening the door).
        /// </summary>
        Vector3 Route(Vector3 pos, out bool routed)
        {
            routed = false;
            var goal = m_Goal;
            var g = NetGame.Instance;
            int team = MyTeam;
            if (g == null || Cfg.Builder || team < 0 || team > 3) return goal;
            var c = Cfg.BaseCenter[team];
            float ring = 3f * OuterR(team);
            if (Flat(pos, c) > ring + 6f && Flat(goal, c) > ring + 6f) return goal; // (nowhere near home)
            if (Flat(goal, pos) <= m_Stop + 0.2f) return goal;
            bool innerUp = DoorOf(team, 0, pos, out _, out _), outerUp = DoorOf(team, 1, pos, out _, out _);
            if (!innerUp && !outerUp) return goal;
            int rp = RingOf(team, pos, innerUp, outerUp), rg = RingOf(team, goal, innerUp, outerUp);
            if (rp == rg) return goal;
            bool goingOut = rp < rg;
            int b = goingOut ? (rp == 0 ? 0 : 1) : (rp == 2 ? (outerUp ? 1 : 0) : 0);
            if (!DoorOf(team, b, pos, out var door, out var doorS)) return goal;
            routed = true;
            var back = Cfg.BackDir(team);
            var lat = Vector3.Cross(Vector3.up, back);
            var inPt = door + back * 1.3f;
            var outPt = door - back * 1.3f;
            var rel = pos - door;
            float latOff = Mathf.Abs(Vector3.Dot(rel, lat));
            float fwd = Vector3.Dot(rel, -back); // (+: in front of the doorway)
            if (Flat(pos, door) < 2.8f) OpenDoor(doorS);
            // lined up with it: through
            if (latOff < 0.6f && (goingOut ? fwd > -2.4f && fwd < 1.0f : fwd < 2.4f && fwd > -1.0f)) return goingOut ? outPt : inPt;
            // round to the front of the room it's going into / out of first (it's at the back or the side)
            float ahead = Vector3.Dot(pos - c, -back);
            float line = b == 0 ? 3f : ring;
            bool wrapAround = goingOut ? b == 1 && rp == 1 && ahead < 3.4f : ahead < line + 0.4f;
            if (wrapAround)
            {
                float corner = (goingOut || b == 0 ? 3f : ring) + 1.6f;
                float side = Vector3.Dot(pos - c, lat) >= 0f ? 1f : -1f;
                var cp = c - back * corner + lat * corner * side;
                return Flat(pos, cp) < 1.2f ? (goingOut ? inPt : outPt) : cp;
            }
            return goingOut ? inPt : outPt;
        }

        /// <summary>Open one of its team's doors (it's right by it).</summary>
        void OpenDoor(Structure s)
        {
            if (s == null || !s.IsSpawned || !s.HasDoor || s.DoorOpen.Value || Time.time < m_NextDoor) return;
            m_NextDoor = Time.time + 0.5f;
            m_P.ToggleDoorRpc(s.NetworkObject);
            if (s.DoorOpen.Value && !m_OpenedDoors.Contains(s)) { m_OpenedDoors.Add(s); DoorsOpened++; }
        }

        /// <summary>Where a doorway's gap really is (a grid piece's own pose; a large gate's middle).</summary>
        static Vector3 DoorPos(Structure s)
        {
            if (s.Key.Kind == PieceKey.KEdge && BuildGrid.Registry.TryGetValue(s.Key, out var r) && r == s) { BuildGrid.Pose(s.PType, s.Key, out var p, out _); return p; }
            return s.transform.position;
        }

        /// <summary>(tests) doors it has opened.</summary>
        public int DoorsOpened { get; private set; }

        /// <summary>Its own closed door right in front of it: open it. Every door it opened, once it's through and a couple of
        /// steps past it: shut it behind itself, going in or out (a teammate right in the doorway gets a moment to come
        /// through first; an enemy gets the door shut in their face).</summary>
        void TickDoors()
        {
            if (m_Blocker != null && Time.time - m_BlockedAt < 0.4f && Vector3.Distance(m_P.EyePos, m_BlockPoint) < 2.6f)
            {
                var s = m_Blocker.GetComponentInParent<Structure>();
                if (s != null && s.IsSpawned && s.Team.Value == MyTeam && s.HasDoor && !s.DoorOpen.Value) OpenDoor(s);
            }
            for (int i = m_OpenedDoors.Count - 1; i >= 0; i--)
            {
                var d = m_OpenedDoors[i];
                if (d == null || !d.IsSpawned || !d.HasDoor || !d.DoorOpen.Value) { m_OpenedDoors.RemoveAt(i); m_DoorWait.Remove(d); m_DoorNear.Remove(d); continue; }
                var dp = DoorPos(d);
                float away = Flat(dp, transform.position);
                // gone right off without it (a respawn, knocked flying): too far to reach now (the reach ToggleDoorRpc allows)
                if (Vector3.Distance(d.transform.position, transform.position) > Cfg.InteractRange + 2.8f) { Note($"too far from a door to shut it ({away:0.0} m)"); m_OpenedDoors.RemoveAt(i); m_DoorWait.Remove(d); m_DoorNear.Remove(d); continue; }
                // through it (it came right up to the doorway and is now past it), or it went off another way
                float near = m_DoorNear.TryGetValue(d, out var nd) ? Mathf.Min(nd, away) : away;
                m_DoorNear[d] = near;
                if (!(near < 1.4f && away > 2.1f) && away < 4f) continue;
                if (Time.time < m_NextDoor) continue;
                bool mate = false;
                foreach (var p in PlayerNet.All)
                    if (p != null && p != m_P && p.IsSpawned && !p.Dead.Value && p.Team.Value == MyTeam && Flat(p.transform.position, dp) < 1.6f) mate = true;
                if (mate)
                {
                    // a teammate in the doorway: give them a moment, then shut it anyway
                    if (!m_DoorWait.TryGetValue(d, out var since)) m_DoorWait[d] = since = Time.time;
                    if (Time.time - since < 2.5f) continue;
                }
                m_NextDoor = Time.time + 0.3f;
                m_P.ToggleDoorRpc(d.NetworkObject);
                if (!d.DoorOpen.Value) DoorsShut++; else Note("a door would not shut");
                m_OpenedDoors.RemoveAt(i);
                m_DoorWait.Remove(d);
                m_DoorNear.Remove(d);
            }
        }
    }
}
