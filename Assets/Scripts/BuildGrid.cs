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

        /// <summary>Where a team may build: its own base - or, in Builder, anywhere except another team's base.</summary>
        public static bool CanBuildAt(int team, PieceKey k)
        {
            if (!Cfg.Builder) return InTeamBase(team, k);
            for (int t = 0; t < Cfg.TeamCount; t++)
                if (t != team && InTeamBase(t, k)) return false;
            return true;
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
