using System;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Grid key for building pieces. Cells are 3x3m. Levels are 3m tall, level 0 surface = foundation top (y=1).
    /// Edges are canonical: dir 0 = +X side of cell (i,j), dir 1 = +Z side of cell (i,j).
    /// </summary>
    public struct PieceKey : IEquatable<PieceKey>
    {
        public const byte KFoundation = 0, KEdge = 1, KFloor = 2, KStairs = 3;
        public byte Kind; public int I, J, L, D;
        public PieceKey(byte kind, int i, int j, int l, int d) { Kind = kind; I = i; J = j; L = l; D = d; }
        public bool Equals(PieceKey o) => Kind == o.Kind && I == o.I && J == o.J && L == o.L && D == o.D;
        public override bool Equals(object obj) => obj is PieceKey k && Equals(k);
        public override int GetHashCode() => (((Kind * 397 ^ I) * 397 ^ J) * 397 ^ L) * 397 ^ D;
        public override string ToString() => $"K{Kind}({I},{J},L{L},D{D})";
    }

    public static class BuildGrid
    {
        /// <summary>Server-only registry of all grid pieces.</summary>
        public static readonly Dictionary<PieceKey, Structure> Registry = new Dictionary<PieceKey, Structure>();

        public static int CellOf(float v) => Mathf.FloorToInt(v / Cfg.Cell);
        public static float LevelY(int l) => Cfg.BaseY + l * Cfg.LevelH;
        public static int LevelOf(float y) => Mathf.Clamp(Mathf.FloorToInt((y - Cfg.BaseY + 0.3f) / Cfg.LevelH), 0, Cfg.MaxLevel);

        public static byte KindOf(PieceType t)
        {
            switch (t)
            {
                case PieceType.Foundation: return PieceKey.KFoundation;
                case PieceType.Wall:
                case PieceType.Doorway:
                case PieceType.Window: return PieceKey.KEdge;
                case PieceType.Floor: return PieceKey.KFloor;
                default: return PieceKey.KStairs;
            }
        }

        public static Vector3 CellCenter(int i, int j) => new Vector3((i + 0.5f) * Cfg.Cell, 0, (j + 0.5f) * Cfg.Cell);

        /// <summary>World pose of a piece root.</summary>
        public static void Pose(PieceType t, PieceKey k, out Vector3 pos, out Quaternion rot)
        {
            switch (t)
            {
                case PieceType.Foundation:
                    pos = CellCenter(k.I, k.J); rot = Quaternion.identity; break;
                case PieceType.Wall:
                case PieceType.Doorway:
                case PieceType.Window:
                    if (k.D == 0) { pos = new Vector3((k.I + 1) * Cfg.Cell, LevelY(k.L), (k.J + 0.5f) * Cfg.Cell); rot = Quaternion.Euler(0, 90, 0); }
                    else { pos = new Vector3((k.I + 0.5f) * Cfg.Cell, LevelY(k.L), (k.J + 1) * Cfg.Cell); rot = Quaternion.identity; }
                    break;
                case PieceType.Floor:
                    pos = CellCenter(k.I, k.J) + Vector3.up * LevelY(k.L); rot = Quaternion.identity; break;
                default: // stairs, D = rotation 0..3
                    pos = CellCenter(k.I, k.J) + Vector3.up * LevelY(k.L); rot = Quaternion.Euler(0, k.D * 90, 0); break;
            }
        }

        /// <summary>Local-space bounds (center, size) of a piece for overlap tests.</summary>
        public static void LocalBounds(PieceType t, out Vector3 center, out Vector3 size)
        {
            switch (t)
            {
                case PieceType.Foundation: center = new Vector3(0, 0.5f, 0); size = new Vector3(3, 1, 3); break;
                case PieceType.Wall: center = new Vector3(0, 1.5f, 0); size = new Vector3(3, 3, 0.3f); break;
                case PieceType.Doorway: center = new Vector3(0, 1.5f, 0); size = new Vector3(3, 3, 0.3f); break;
                case PieceType.Window: center = new Vector3(0, 1.5f, 0); size = new Vector3(3, 3, 0.3f); break;
                case PieceType.Floor: center = new Vector3(0, -0.125f, 0); size = new Vector3(3, 0.25f, 3); break;
                case PieceType.Stairs: center = new Vector3(0, 1.5f, 0); size = new Vector3(3, 3, 3); break;
                default: center = new Vector3(0, 0.5f, 0); size = new Vector3(1.6f, 1f, 0.9f); break;
            }
        }

        /// <summary>
        /// Client-side: work out which grid slot the player is aiming at.
        /// </summary>
        public static bool ComputePlacement(PieceType t, Ray ray, RaycastHit hit, bool hasHit, float feetY, int rot, out PieceKey key)
        {
            key = default;
            if (t == PieceType.Floor)
            {
                int pl = Mathf.Clamp(Mathf.FloorToInt((feetY - Cfg.BaseY + 0.5f) / Cfg.LevelH), 0, Cfg.MaxLevel);
                int level = ray.direction.y > 0 ? pl + 1 : pl;
                if (level < 1 || level > Cfg.MaxLevel) return false;
                float planeY = LevelY(level);
                if (Mathf.Abs(ray.direction.y) < 0.01f) return false;
                float d = (planeY - ray.origin.y) / ray.direction.y;
                if (d < 0 || d > Cfg.BuildRange) return false;
                if (hasHit && hit.distance < d - 0.05f)
                {
                    // Something is in the way before the plane - use it if it is roughly at that level
                    if (Mathf.Abs(hit.point.y - planeY) > 0.6f) return false;
                }
                Vector3 p = ray.origin + ray.direction * d;
                key = new PieceKey(PieceKey.KFloor, CellOf(p.x), CellOf(p.z), level, 0);
                return true;
            }

            if (!hasHit || hit.distance > Cfg.BuildRange) return false;
            Vector3 q = hit.point + hit.normal * 0.05f;
            int i = CellOf(q.x), j = CellOf(q.z);
            switch (t)
            {
                case PieceType.Foundation:
                    if (q.y > 1.6f) return false;
                    key = new PieceKey(PieceKey.KFoundation, i, j, 0, 0);
                    return true;
                case PieceType.Wall:
                case PieceType.Doorway:
                case PieceType.Window:
                {
                    int l = LevelOf(q.y);
                    float fx = q.x - i * Cfg.Cell, fz = q.z - j * Cfg.Cell;
                    float dxm = fx, dxp = Cfg.Cell - fx, dzm = fz, dzp = Cfg.Cell - fz;
                    float m = Mathf.Min(Mathf.Min(dxm, dxp), Mathf.Min(dzm, dzp));
                    if (m == dxp) key = new PieceKey(PieceKey.KEdge, i, j, l, 0);
                    else if (m == dxm) key = new PieceKey(PieceKey.KEdge, i - 1, j, l, 0);
                    else if (m == dzp) key = new PieceKey(PieceKey.KEdge, i, j, l, 1);
                    else key = new PieceKey(PieceKey.KEdge, i, j - 1, l, 1);
                    return true;
                }
                case PieceType.Stairs:
                    key = new PieceKey(PieceKey.KStairs, i, j, LevelOf(q.y), rot & 3);
                    return true;
            }
            return false;
        }

        /// <summary>The edge of cell (i, j) nearest to p (walls, doorways, windows).</summary>
        public static PieceKey NearestEdge(Vector3 p, int level)
        {
            int i = CellOf(p.x), j = CellOf(p.z);
            float fx = p.x - i * Cfg.Cell, fz = p.z - j * Cfg.Cell;
            float dxm = fx, dxp = Cfg.Cell - fx, dzm = fz, dzp = Cfg.Cell - fz;
            float m = Mathf.Min(Mathf.Min(dxm, dxp), Mathf.Min(dzm, dzp));
            if (m == dxp) return new PieceKey(PieceKey.KEdge, i, j, level, 0);
            if (m == dxm) return new PieceKey(PieceKey.KEdge, i - 1, j, level, 0);
            if (m == dzp) return new PieceKey(PieceKey.KEdge, i, j, level, 1);
            return new PieceKey(PieceKey.KEdge, i, j - 1, level, 1);
        }

        /// <summary>How far p is from the line of an edge key (in the ground plane).</summary>
        public static float DistToEdge(Vector3 p, PieceKey k)
        {
            if (k.D == 0) return Mathf.Abs(p.x - (k.I + 1) * Cfg.Cell);
            return Mathf.Abs(p.z - (k.J + 1) * Cfg.Cell);
        }

        public static bool InTeamBase(int team, PieceKey k)
        {
            if (k.Kind == PieceKey.KEdge)
            {
                int i2 = k.D == 0 ? k.I + 1 : k.I, j2 = k.D == 0 ? k.J : k.J + 1;
                return Cfg.CellInBase(team, k.I, k.J) || Cfg.CellInBase(team, i2, j2);
            }
            return Cfg.CellInBase(team, k.I, k.J);
        }

        /// <summary>Where a team may build: its own base - or, in Builder, anywhere.</summary>
        public static bool CanBuildAt(int team, PieceKey k)
        {
            return Cfg.Builder || InTeamBase(team, k); // Builder has no bases: anywhere
        }

        /// <summary>Foundations and ground-level stairs can't go on the bedrock (it already is a foundation, and you spawn there).</summary>
        public static bool OnBedrock(PieceKey k)
        {
            if (k.Kind == PieceKey.KFoundation) return Cfg.CellBlocked(k.I, k.J);
            if (k.Kind == PieceKey.KStairs) return k.L == 0 && Cfg.CellBlocked(k.I, k.J);
            return false;
        }

        /// <summary>A foundation at ground level, or the bedrock (which counts as one and can never be destroyed).</summary>
        static bool GroundSupport(int i, int j, Func<PieceKey, bool> exists) =>
            Cfg.IsBedrockCell(i, j) || exists(new PieceKey(PieceKey.KFoundation, i, j, 0, 0));

        /// <summary>Rust-like support rules. exists() answers whether a (supported) piece is at a key.</summary>
        public static bool IsSupported(PieceKey k, Func<PieceKey, bool> exists)
        {
            if (Cfg.Builder && LocksOn(k, exists)) return true;
            switch (k.Kind)
            {
                case PieceKey.KFoundation:
                    return k.L == 0;
                case PieceKey.KEdge:
                {
                    int i2 = k.D == 0 ? k.I + 1 : k.I, j2 = k.D == 0 ? k.J : k.J + 1;
                    if (k.L == 0)
                        return GroundSupport(k.I, k.J, exists) || GroundSupport(i2, j2, exists);
                    return exists(new PieceKey(PieceKey.KEdge, k.I, k.J, k.L - 1, k.D))
                        || exists(new PieceKey(PieceKey.KFloor, k.I, k.J, k.L, 0))
                        || exists(new PieceKey(PieceKey.KFloor, i2, j2, k.L, 0));
                }
                case PieceKey.KFloor:
                {
                    if (k.L < 1) return false;
                    int l = k.L - 1;
                    if (exists(new PieceKey(PieceKey.KEdge, k.I, k.J, l, 0)) || exists(new PieceKey(PieceKey.KEdge, k.I, k.J, l, 1))
                        || exists(new PieceKey(PieceKey.KEdge, k.I - 1, k.J, l, 0)) || exists(new PieceKey(PieceKey.KEdge, k.I, k.J - 1, l, 1)))
                        return true;
                    // off a ramp: the top of stairs in this cell or the one next to it
                    for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                        if ((di == 0 || dj == 0) && HasStairsAt(k.I + di, k.J + dj, l, exists)) return true;
                    // cantilever from a neighbouring floor
                    return exists(new PieceKey(PieceKey.KFloor, k.I + 1, k.J, k.L, 0)) || exists(new PieceKey(PieceKey.KFloor, k.I - 1, k.J, k.L, 0))
                        || exists(new PieceKey(PieceKey.KFloor, k.I, k.J + 1, k.L, 0)) || exists(new PieceKey(PieceKey.KFloor, k.I, k.J - 1, k.L, 0));
                }
                case PieceKey.KStairs:
                    if (k.L == 0) return GroundSupport(k.I, k.J, exists);
                    return exists(new PieceKey(PieceKey.KFloor, k.I, k.J, k.L, 0));
            }
            return false;
        }

        /// <summary>
        /// Builder (Fortnite style): pieces lock on to each other. Anything at ground level stands on the ground; walls
        /// hang off the walls they touch end to end; floors hold on to any wall around them (below or above) or stairs
        /// under them; stairs hold on to walls or floors around them or stairs leading into them.
        /// </summary>
        static bool LocksOn(PieceKey k, Func<PieceKey, bool> exists)
        {
            switch (k.Kind)
            {
                case PieceKey.KEdge:
                {
                    if (k.L == 0) return true;
                    int I = k.I, J = k.J, L = k.L;
                    if (k.D == 0)
                        return exists(new PieceKey(PieceKey.KEdge, I, J + 1, L, 0)) || exists(new PieceKey(PieceKey.KEdge, I, J - 1, L, 0))
                            || exists(new PieceKey(PieceKey.KEdge, I, J, L, 1)) || exists(new PieceKey(PieceKey.KEdge, I + 1, J, L, 1))
                            || exists(new PieceKey(PieceKey.KEdge, I, J - 1, L, 1)) || exists(new PieceKey(PieceKey.KEdge, I + 1, J - 1, L, 1))
                            || AnyStairs(I, J, L - 1, exists) || AnyStairs(I + 1, J, L - 1, exists);
                    return exists(new PieceKey(PieceKey.KEdge, I + 1, J, L, 1)) || exists(new PieceKey(PieceKey.KEdge, I - 1, J, L, 1))
                        || exists(new PieceKey(PieceKey.KEdge, I, J, L, 0)) || exists(new PieceKey(PieceKey.KEdge, I, J + 1, L, 0))
                        || exists(new PieceKey(PieceKey.KEdge, I - 1, J, L, 0)) || exists(new PieceKey(PieceKey.KEdge, I - 1, J + 1, L, 0))
                        || AnyStairs(I, J, L - 1, exists) || AnyStairs(I, J + 1, L - 1, exists);
                }
                case PieceKey.KFloor:
                    if (k.L < 1) return false;
                    return AnyWallAround(k.I, k.J, k.L, exists) || AnyWallAround(k.I, k.J, k.L - 1, exists) || AnyStairs(k.I, k.J, k.L - 1, exists);
                case PieceKey.KStairs:
                    if (k.L == 0) return true;
                    return exists(new PieceKey(PieceKey.KFloor, k.I, k.J, k.L, 0)) || AnyWallAround(k.I, k.J, k.L, exists) || AnyWallAround(k.I, k.J, k.L - 1, exists)
                        || AnyStairs(k.I + 1, k.J, k.L - 1, exists) || AnyStairs(k.I - 1, k.J, k.L - 1, exists)
                        || AnyStairs(k.I, k.J + 1, k.L - 1, exists) || AnyStairs(k.I, k.J - 1, k.L - 1, exists);
            }
            return false;
        }

        static bool AnyWallAround(int i, int j, int l, Func<PieceKey, bool> exists) =>
            l >= 0 && (exists(new PieceKey(PieceKey.KEdge, i, j, l, 0)) || exists(new PieceKey(PieceKey.KEdge, i, j, l, 1))
                || exists(new PieceKey(PieceKey.KEdge, i - 1, j, l, 0)) || exists(new PieceKey(PieceKey.KEdge, i, j - 1, l, 1)));

        static bool AnyStairs(int i, int j, int l, Func<PieceKey, bool> exists) => l >= 0 && HasStairsAt(i, j, l, exists);

        static bool HasStairsAt(int i, int j, int l, Func<PieceKey, bool> has)
        {
            for (int d = 0; d < 4; d++)
                if (has(new PieceKey(PieceKey.KStairs, i, j, l, d))) return true;
            return false;
        }

        /// <summary>Slot occupancy (includes stairs/floor clashes). has() = does a piece exist at key.</summary>
        public static bool IsOccupied(PieceKey k, Func<PieceKey, bool> has)
        {
            if (k.Kind == PieceKey.KStairs)
            {
                if (HasStairsAt(k.I, k.J, k.L, has)) return true;
                return has(new PieceKey(PieceKey.KFloor, k.I, k.J, k.L + 1, 0));
            }
            if (has(k)) return true;
            if (k.Kind == PieceKey.KFloor && HasStairsAt(k.I, k.J, k.L - 1, has)) return true;
            return false;
        }

        /// <summary>Recover a grid key from a spawned piece's transform (lets clients predict placement validity).</summary>
        public static bool TryKeyFromTransform(PieceType t, Transform tr, out PieceKey k)
        {
            k = default;
            Vector3 p = tr.position;
            int l = Mathf.RoundToInt((p.y - Cfg.BaseY) / Cfg.LevelH);
            switch (t)
            {
                case PieceType.Foundation:
                    k = new PieceKey(PieceKey.KFoundation, CellOf(p.x), CellOf(p.z), 0, 0);
                    return true;
                case PieceType.Wall:
                case PieceType.Doorway:
                case PieceType.Window:
                {
                    bool alongZ = Mathf.Abs(Mathf.DeltaAngle(tr.eulerAngles.y, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(tr.eulerAngles.y, 270f)) < 45f;
                    if (alongZ) k = new PieceKey(PieceKey.KEdge, Mathf.RoundToInt(p.x / Cfg.Cell) - 1, CellOf(p.z), l, 0);
                    else k = new PieceKey(PieceKey.KEdge, CellOf(p.x), Mathf.RoundToInt(p.z / Cfg.Cell) - 1, l, 1);
                    return true;
                }
                case PieceType.Floor:
                    k = new PieceKey(PieceKey.KFloor, CellOf(p.x), CellOf(p.z), l, 0);
                    return true;
                case PieceType.Stairs:
                    k = new PieceKey(PieceKey.KStairs, CellOf(p.x), CellOf(p.z), l, Mathf.RoundToInt(tr.eulerAngles.y / 90f) & 3);
                    return true;
            }
            return false;
        }

        // ---------------- building in a line: prefer extending the last pieces you placed ----------------

        /// <summary>How much farther (m) from the aim a slot extending one of your last two pieces may be than the slot you'd otherwise get, and still win.</summary>
        public const float RecentBias = 0.75f;
        /// <summary>A slot extending your last pieces is only taken when the aim is at most this far (m) from it.</summary>
        public const float RecentReach = 1.6f;
        /// <summary>Score bonus (m) for carrying straight on in the line your last two pieces make (over turning a corner).</summary>
        public const float StraightBonus = 0.25f;
        /// <summary>Score bonus (m) for extending the very last piece (over the one before).</summary>
        public const float NewestBonus = 0.1f;
        /// <summary>Score bonus (m) for a wall that carries your last wall on in the same rotation (along its line), over one
        /// that's connected to it but turned (round the corner): the line wins unless you aim clearly at the turned one.</summary>
        public const float SameRotationBonus = 0.6f;
        /// <summary>...and how much farther (m) from the aim than the slot you'd otherwise get such a wall may be, when that
        /// other slot is a turned one (instead of RecentBias).</summary>
        public const float SameRotationBias = 1.2f;
        /// <summary>...and how far (m) from the aim such a wall may be at most (instead of RecentReach): building a wall
        /// line you usually aim at the floor in front of the next slot, not right at it.</summary>
        public const float SameRotationReach = 2.3f;
        /// <summary>Extra bias, reach and score bonus (m) for that wall when it faces you (parallel to you: you're looking
        /// across the line, the way you stand to build one) - the turned one would run away from you.</summary>
        public const float FacingBias = 0.5f;

        /// <summary>A wall slot faces someone looking along `facing` (flat): its line runs across their view.</summary>
        public static bool WallFaces(PieceKey k, Vector3 facing)
        {
            if (k.Kind != PieceKey.KEdge) return false;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) return false;
            facing.Normalize();
            return Mathf.Abs(k.D == 0 ? facing.x : facing.z) > 0.7f; // (D 0: on an x line, so its face looks along x)
        }

        static PieceType ShapeOf(byte kind) => kind == PieceKey.KFoundation ? PieceType.Foundation : kind == PieceKey.KEdge ? PieceType.Wall : kind == PieceKey.KFloor ? PieceType.Floor : PieceType.Stairs;

        /// <summary>How far `p` is from the box a piece at `k` fills (0 inside it).</summary>
        public static float DistToPiece(Vector3 p, PieceKey k)
        {
            var t = ShapeOf(k.Kind);
            Pose(t, k, out var pos, out var rot);
            LocalBounds(t, out var c, out var size);
            var local = Quaternion.Inverse(rot) * (p - pos) - c;
            var h = size * 0.5f;
            var d = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - h.x), Mathf.Max(0f, Mathf.Abs(local.y) - h.y), Mathf.Max(0f, Mathf.Abs(local.z) - h.z));
            return d.magnitude;
        }

        /// <summary>World centre of the box a piece at `k` fills.</summary>
        public static Vector3 PieceCenter(PieceKey k)
        {
            var t = ShapeOf(k.Kind);
            Pose(t, k, out var pos, out var rot);
            LocalBounds(t, out var c, out _);
            return pos + rot * c;
        }

        /// <summary>The two corners (grid-line crossings, in cells) an edge runs between.</summary>
        static void EdgeEnds(PieceKey e, out Vector2Int a, out Vector2Int b)
        {
            if (e.D == 0) { a = new Vector2Int(e.I + 1, e.J); b = new Vector2Int(e.I + 1, e.J + 1); }
            else { a = new Vector2Int(e.I, e.J + 1); b = new Vector2Int(e.I + 1, e.J + 1); }
        }

        /// <summary>
        /// The slots for a piece of kind `want` that attach to the piece at `r` (building carried on from it): a foundation
        /// next to a foundation or either side of a wall; a wall carrying a wall on along its line, round a corner or up a
        /// level, or on any side of a foundation / floor; a floor next to a floor or on top of a wall. `straight` is set for
        /// the ones carrying straight on from `r` - along a wall's own line, or for foundations / floors in the direction
        /// `dir` (cells; zero = any neighbour counts, (99, 99) = none does).
        /// </summary>
        public static void AttachedSlots(PieceKey r, byte want, Vector2Int dir, List<PieceKey> slots, List<bool> straight)
        {
            void Add(PieceKey k, bool st) { if (k.L < 0 || k.L > Cfg.MaxLevel || k.Equals(r)) return; slots.Add(k); straight.Add(st); }
            bool Along(int di, int dj) => dir == Vector2Int.zero || (dir.x == di && dir.y == dj);
            if (r.Kind == PieceKey.KStairs) return;
            if (want == PieceKey.KFoundation)
            {
                if (r.Kind == PieceKey.KFoundation)
                {
                    Add(new PieceKey(want, r.I + 1, r.J, 0, 0), Along(1, 0)); Add(new PieceKey(want, r.I - 1, r.J, 0, 0), Along(-1, 0));
                    Add(new PieceKey(want, r.I, r.J + 1, 0, 0), Along(0, 1)); Add(new PieceKey(want, r.I, r.J - 1, 0, 0), Along(0, -1));
                }
                else if (r.Kind == PieceKey.KEdge && r.L == 0)
                {
                    Add(new PieceKey(want, r.I, r.J, 0, 0), false);
                    Add(r.D == 0 ? new PieceKey(want, r.I + 1, r.J, 0, 0) : new PieceKey(want, r.I, r.J + 1, 0, 0), false);
                }
            }
            else if (want == PieceKey.KEdge)
            {
                if (r.Kind == PieceKey.KEdge)
                {
                    // along its line (straight on), round the corners at either end, and the same wall a level up
                    EdgeEnds(r, out var a, out var b);
                    foreach (var c in new[] { a, b })
                    {
                        Add(new PieceKey(want, c.x - 1, c.y - 1, r.L, 0), r.D == 0); // (on the x = c.x line, below the corner)
                        Add(new PieceKey(want, c.x - 1, c.y, r.L, 0), r.D == 0);     // (on the x = c.x line, above it)
                        Add(new PieceKey(want, c.x - 1, c.y - 1, r.L, 1), r.D == 1); // (on the z = c.y line, left of it)
                        Add(new PieceKey(want, c.x, c.y - 1, r.L, 1), r.D == 1);     // (on the z = c.y line, right of it)
                    }
                    Add(new PieceKey(want, r.I, r.J, r.L + 1, r.D), false);
                }
                else if (r.Kind == PieceKey.KFoundation || r.Kind == PieceKey.KFloor)
                {
                    Add(new PieceKey(want, r.I, r.J, r.L, 0), false); Add(new PieceKey(want, r.I - 1, r.J, r.L, 0), false);
                    Add(new PieceKey(want, r.I, r.J, r.L, 1), false); Add(new PieceKey(want, r.I, r.J - 1, r.L, 1), false);
                }
            }
            else if (want == PieceKey.KFloor)
            {
                if (r.Kind == PieceKey.KFloor)
                {
                    Add(new PieceKey(want, r.I + 1, r.J, r.L, 0), Along(1, 0)); Add(new PieceKey(want, r.I - 1, r.J, r.L, 0), Along(-1, 0));
                    Add(new PieceKey(want, r.I, r.J + 1, r.L, 0), Along(0, 1)); Add(new PieceKey(want, r.I, r.J - 1, r.L, 0), Along(0, -1));
                }
                else if (r.Kind == PieceKey.KEdge)
                {
                    Add(new PieceKey(want, r.I, r.J, r.L + 1, 0), false);
                    Add(r.D == 0 ? new PieceKey(want, r.I + 1, r.J, r.L + 1, 0) : new PieceKey(want, r.I, r.J + 1, r.L + 1, 0), false);
                }
            }
        }

        /// <summary>
        /// Building in a line: most people carry on from the piece they just put down, so when the aim is near a slot that
        /// extends one of your last two pieces (`recent`, newest last) that slot wins over the one strictly nearest the aim -
        /// a bias, not an override: it has to be within RecentReach of the aim and no more than RecentBias farther from it
        /// than the slot you'd otherwise get (`current`, or none when `currentOk` is false), so aiming clearly somewhere else
        /// still goes there. Carrying straight on (the line the two make, or a wall's own line) beats turning a corner.
        /// `aimAt(key)` gives the aim point to measure a slot against (ok false: not aimed near it), `valid(key)` whether
        /// the piece can go there. Returns true (and `key`) when a recent-extension slot should be used instead.
        /// Walls: one that carries your last wall's line on in the same rotation is preferred hard over a turned one (a
        /// bigger reach and bias, SameRotation*), the more so when it faces you (`facing`: where you look; FacingBias).
        /// </summary>
        public static bool PreferRecent(PieceType t, IList<PieceKey> recent, PieceKey current, bool currentOk,
            Func<PieceKey, (bool ok, Vector3 p)> aimAt, Func<PieceKey, bool> valid, out PieceKey key, Vector3 facing = default)
        {
            key = default;
            byte want = KindOf(t);
            if (recent == null || recent.Count == 0 || want == PieceKey.KStairs) return false;
            float curDist = float.MaxValue;
            if (currentOk)
            {
                var a = aimAt(current);
                curDist = a.ok ? DistToPiece(a.p, current) : float.MaxValue;
            }
            var slots = new List<PieceKey>();
            var straight = new List<bool>();
            float best = float.MaxValue;
            for (int n = recent.Count - 1, age = 0; n >= 0; n--, age++)
            {
                var r = recent[n];
                // the line the last two make (both the same kind, side by side)
                var dir = Vector2Int.zero;
                if (age == 0 && n >= 1)
                {
                    var o = recent[n - 1];
                    if (o.Kind == r.Kind && o.L == r.L && Mathf.Abs(r.I - o.I) + Mathf.Abs(r.J - o.J) == 1) dir = new Vector2Int(r.I - o.I, r.J - o.J);
                    else if (o.Kind == r.Kind) dir = new Vector2Int(99, 99); // (not side by side: no line to carry on)
                }
                else if (age > 0) dir = new Vector2Int(99, 99);
                slots.Clear(); straight.Clear();
                AttachedSlots(r, want, dir, slots, straight);
                for (int s = 0; s < slots.Count; s++)
                {
                    var k = slots[s];
                    var a = aimAt(k);
                    if (!a.ok) continue;
                    float d = DistToPiece(a.p, k);
                    // a wall in the same rotation as your last wall, on its line: strongly preferred over a turned one
                    bool sameRot = want == PieceKey.KEdge && r.Kind == PieceKey.KEdge && straight[s] && k.D == r.D && k.L == r.L;
                    // (against a turned wall - or nothing buildable - the line gets the big bias and reach; against a
                    // wall in the same rotation on another line, just the usual one, so aiming at that line still works)
                    bool vsTurned = sameRot && age == 0 && (!currentOk || current.Kind != PieceKey.KEdge || current.D != r.D);
                    bool faces = sameRot && age == 0 && WallFaces(k, facing);
                    float bias = vsTurned ? SameRotationBias + (faces ? FacingBias : 0f) : RecentBias;
                    float reach = vsTurned ? SameRotationReach + (faces ? FacingBias : 0f) : RecentReach;
                    if (d > reach || d > curDist + bias) continue;
                    float score = d - (sameRot ? SameRotationBonus + (faces ? FacingBias : 0f) : straight[s] && r.Kind == want ? StraightBonus : 0f) - (age == 0 ? NewestBonus : 0f);
                    if (score >= best || !valid(k)) continue;
                    best = score;
                    key = k;
                }
            }
            if (best == float.MaxValue) return false;
            return !(currentOk && key.Equals(current));
        }

        /// <summary>Server: find every piece no longer connected to a foundation and return it.</summary>
        public static List<Structure> FindUnsupported()
        {
            var supported = new HashSet<PieceKey>();
            bool changed = true;
            Func<PieceKey, bool> exists = supported.Contains;
            while (changed)
            {
                changed = false;
                foreach (var kv in Registry)
                {
                    if (supported.Contains(kv.Key)) continue;
                    if (IsSupported(kv.Key, exists)) { supported.Add(kv.Key); changed = true; }
                }
            }
            var result = new List<Structure>();
            foreach (var kv in Registry)
                if (!supported.Contains(kv.Key) && kv.Value != null) result.Add(kv.Value);
            return result;
        }
    }
}
