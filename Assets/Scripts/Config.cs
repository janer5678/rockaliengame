using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RockGame
{
    public enum Item : byte { None, Rock, BuildingPlan, Hatchet, Pickaxe, Spear, Bow, Ram, Chest, Barrier, Wood, Stone, Arrow, Berry }

    public enum PieceType : byte { Foundation, Wall, Doorway, Floor, Stairs, Barrier }

    public enum GameState : byte { Waiting, PreBall, BallLive, SuddenDeath, GameOver }

    public enum MapKind : byte { Plains, Highlands }

    public struct MeleeStats
    {
        public float Cooldown, Range, PlayerDamage, WoodGather, StoneGather, StructureDamage;
    }

    public struct Recipe
    {
        public Item Output;
        public int Count, Wood, Stone;
        public string Name => Count > 1 ? $"{Cfg.ItemName(Output)} x{Count}" : Cfg.ItemName(Output);
    }

    /// <summary>Marks a Cfg field as a game stat the host can edit from the main menu (synced to the client).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class TuneAttribute : Attribute
    {
        public readonly string Section;
        public TuneAttribute(string section) { Section = section; }
    }

    /// <summary>All gameplay tuning lives here. Every [Tune] field is editable in the main menu settings.</summary>
    public static class Cfg
    {
        // ---------- Map (chosen by the host in the menu) ----------
        public static MapKind Map = MapKind.Plains;
        public static bool SmallMap;
        public static int MapSeed;
        public static float MapHalf => SmallMap ? 62f : 100f;
        public const float BaseHalf = 18f; // bases are 36x36m, aligned to the 3m build grid
        public static readonly Vector3[] BaseCenter = { new Vector3(0, 0, -75), new Vector3(0, 0, 75) };
        public static string MapLabel => (SmallMap ? "Small " : "Big ") + Map;
        /// <summary>Packs the map choice for syncing; the seed is sent separately.</summary>
        public static int MapKey => (int)Map | (SmallMap ? 16 : 0);

        /// <summary>Crashed UFO (spawn) per team: centre cell and door direction (0 +x, 1 -x, 2 +z, 3 -z).</summary>
        public static readonly int[] UfoI = new int[2], UfoJ = new int[2], UfoDir = new int[2];

        public static void SetMap(int key, int seed)
        {
            Map = (MapKind)(key & 15);
            SmallMap = (key & 16) != 0;
            MapSeed = seed;
            float d = SmallMap ? 42f : 75f;
            BaseCenter[0] = new Vector3(0, 0, -d);
            BaseCenter[1] = new Vector3(0, 0, d);
            for (int t = 0; t < 2; t++) PlaceUfo(t);
        }

        static void PlaceUfo(int team)
        {
            var rng = new System.Random(MapSeed * 31 + team * 977 + 5);
            var c = BaseCenter[team];
            int iMin = Mathf.FloorToInt((c.x - BaseHalf) / Cell), iMax = Mathf.FloorToInt((c.x + BaseHalf - 0.01f) / Cell);
            int jMin = Mathf.FloorToInt((c.z - BaseHalf) / Cell), jMax = Mathf.FloorToInt((c.z + BaseHalf - 0.01f) / Cell);
            int dir = rng.Next(4);
            // 3x3 footprint plus two clear cells in front of the door, all inside the base
            int lo = iMin + 1 + (dir == 1 ? 3 : 0), hi = iMax - 1 - (dir == 0 ? 3 : 0);
            UfoI[team] = rng.Next(lo, hi + 1);
            lo = jMin + 1 + (dir == 3 ? 3 : 0); hi = jMax - 1 - (dir == 2 ? 3 : 0);
            UfoJ[team] = rng.Next(lo, hi + 1);
            UfoDir[team] = dir;
        }

        public static Vector3 DirVec(int dir) => dir == 0 ? Vector3.right : dir == 1 ? Vector3.left : dir == 2 ? Vector3.forward : Vector3.back;
        public static Vector3 UfoCenter(int team) => new Vector3((UfoI[team] + 0.5f) * Cell, 0, (UfoJ[team] + 0.5f) * Cell);
        public static Vector3 UfoForward(int team) => DirVec(UfoDir[team]);
        /// <summary>Where you (re)spawn: inside the cryo chamber at the back of your UFO.</summary>
        public static Vector3 ChamberPos(int team) => UfoCenter(team) - UfoForward(team) * 3.0f + Vector3.up * 0.32f;

        /// <summary>Cells covered by a UFO or the path out of its door can't be built on.</summary>
        public static bool CellBlocked(int i, int j)
        {
            for (int t = 0; t < 2; t++)
            {
                int di = i - UfoI[t], dj = j - UfoJ[t];
                if (Mathf.Abs(di) <= 1 && Mathf.Abs(dj) <= 1) return true;
                var f = DirVec(UfoDir[t]);
                int fx = Mathf.RoundToInt(f.x), fz = Mathf.RoundToInt(f.z);
                if ((di == fx * 2 && dj == fz * 2) || (di == fx * 3 && dj == fz * 3)) return true;
            }
            return false;
        }
        public static bool PointBlocked(Vector3 p) => CellBlocked(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
        public static readonly Color[] TeamColor = { new Color(0.25f, 0.5f, 1f), new Color(1f, 0.3f, 0.25f) };
        public static readonly string[] TeamName = { "BLUE", "RED" };
        public static readonly Vector3 ArenaCenter = new Vector3(0, 0, 1000);
        public const float ArenaHalf = 20f;
        public static readonly Vector3 BallDropPoint = new Vector3(0, 40, 0);

        // ---------- Inventory (not tunable) ----------
        public const int HotbarSize = 7, MainSize = 21, PlayerSlots = HotbarSize + MainSize;
        public const int ChestSlots = 14;
        public const float EyeHeight = 1.6f, CrouchEyeHeight = 1.05f;
        public const float StandHeight = 1.8f, CrouchHeight = 1.2f;
        public const float InteractRange = 3f, LootRange = 4f;
        public const float Cell = 3f, BaseY = 1f, LevelH = 3f;
        public const int MaxLevel = 4;
        public const float BuildRange = 7f, DeployRange = 5f;
        public const float BowMinDraw = 0.2f, SpearMinDraw = 0.25f;
        public const float SpearPickupRange = 3f;

        // ---------- Match ----------
        [Tune("Match")] public static float BallDropDelay = 180f;   // 3 min to gather and build
        [Tune("Match")] public static float MatchLength = 420f;     // + 7 min with the ball = 10 min game
        [Tune("Match")] public static float SuddenDeathLength = 180f;
        [Tune("Match")] public static float FastBallDropDelay = 10f;
        [Tune("Match")] public static float FastMatchLength = 90f;
        [Tune("Match")] public static float RespawnTime = 5f;
        [Tune("Match")] public static float ItemDespawnTime = 300f;

        // ---------- Player ----------
        [Tune("Player")] public static float MaxHealth = 100f;
        [Tune("Player")] public static float WalkSpeed = 5f;
        [Tune("Player")] public static float SprintSpeed = 7.5f;
        [Tune("Player")] public static float CrouchSpeed = 2.6f;
        [Tune("Player")] public static float JumpSpeed = 7.2f;
        [Tune("Player")] public static float Gravity = 20f;
        [Tune("Player")] public static float BallCarrySpeedMul = 1f;
        [Tune("Player")] public static float BallThrowSpeed = 16f;
        [Tune("Player")] public static float BerryHeal = 15f;
        [Tune("Player")] public static float HeadshotMul = 2f;
        [Tune("Player")] public static float ModelWidth = 1.3f;       // alien model + hitbox width scale
        [Tune("Player")] public static float HitboxRadius = 0.62f;
        [Tune("Player")] public static float MeleeAssist = 0.35f;     // melee counts as a hit if it passes this close (m)
        [Tune("Player")] public static float ProjectileAssist = 0.15f; // same for arrows / thrown spears

        // ---------- Melee ----------
        [Tune("Rock")] public static float RockCooldown = 0.6f, RockRange = 2.3f, RockPlayerDamage = 12f, RockWoodGather = 5f, RockStoneGather = 4f, RockStructureDamage = 3f;
        [Tune("Hatchet")] public static float HatchetCooldown = 0.7f, HatchetRange = 2.5f, HatchetPlayerDamage = 14f, HatchetWoodGather = 15f, HatchetStoneGather = 2f, HatchetStructureDamage = 6f;
        [Tune("Pickaxe")] public static float PickaxeCooldown = 0.8f, PickaxeRange = 2.5f, PickaxePlayerDamage = 14f, PickaxeWoodGather = 3f, PickaxeStoneGather = 12f, PickaxeStructureDamage = 6f;
        [Tune("Spear")] public static float SpearCooldown = 0.9f, SpearRange = 3.4f, SpearPlayerDamage = 35f, SpearWoodGather = 2f, SpearStoneGather = 1f, SpearStructureDamage = 5f;
        [Tune("Spear")] public static float SpearDrawTime = 0.6f, SpearThrowSpeed = 30f, SpearThrowDamage = 60f, SpearThrowStructureDamage = 4f;
        [Tune("Melee")] public static float StoneStructureMeleeMul = 0.2f; // stone is very hard to melee - bring a ram

        // ---------- Bow ----------
        [Tune("Bow")] public static float BowDrawTime = 0.8f, ArrowSpeed = 95f, ArrowGravity = 5f, ArrowPlayerDamage = 50f, ArrowWoodStructureDamage = 2f;
        [Tune("Spear")] public static float SpearGravity = 9.81f;

        // ---------- Battering ram (hand held) ----------
        // Hold LMB to wind up, then it slams whatever enemy piece you look at:
        // wooden pieces are destroyed outright, stone pieces are knocked back down to wood.
        [Tune("Ram")] public static float RamWindup = 1.5f, RamRange = 3f, RamMoveMul = 0.75f;
        [Tune("Ram")] public static int RamUses = 3;

        // ---------- Resources ----------
        [Tune("Resources")] public static int TreeAmount = 300, StoneAmount = 250, BushBerries = 4, BerriesPerPick = 2;
        [Tune("Resources")] public static float WeakSpotMul = 2f, NodeRespawnTime = 60f, BushRespawnTime = 45f;

        // ---------- Building ----------
        [Tune("Building")] public static float BuildCooldown = 1.1f, UpgradeCooldown = 1.5f;
        [Tune("Building")] public static int FoundationWood = 30, WallWood = 30, DoorwayWood = 40, FloorWood = 25, StairsWood = 40;
        [Tune("Building")] public static int FoundationStone = 100, WallStone = 100, DoorwayStone = 80, FloorStone = 60, StairsStone = 60;
        [Tune("Building HP")] public static float FoundationHp = 500, WallHp = 400, DoorwayHp = 350, FloorHp = 300, StairsHp = 300;
        [Tune("Building HP")] public static float FoundationStoneHp = 1800, WallStoneHp = 1500, DoorwayStoneHp = 1200, FloorStoneHp = 1000, StairsStoneHp = 1000;
        [Tune("Building HP")] public static float BarrierHp = 250, ChestHp = 300;

        // ---------- Crafting (anywhere) ----------
        [Tune("Crafting")] public static int PlanWood = 10;
        [Tune("Crafting")] public static int HatchetWood = 60, HatchetStone = 20, PickaxeWood = 60, PickaxeStone = 20;
        [Tune("Crafting")] public static int SpearWood = 150, SpearStone = 25, BowWood = 200, BowStone = 30;
        [Tune("Crafting")] public static int ArrowWood = 30, ArrowStone = 10, ArrowsPerCraft = 10;
        [Tune("Crafting")] public static int RamWood = 250, RamStone = 100;
        [Tune("Crafting")] public static int ChestWood = 100, BarrierWood = 40;

        // ---------- Items ----------
        public static string ItemName(Item i)
        {
            switch (i)
            {
                case Item.BuildingPlan: return "Building Plan";
                case Item.Hatchet: return "Stone Hatchet";
                case Item.Pickaxe: return "Stone Pickaxe";
                case Item.Ram: return "Battering Ram";
                case Item.Chest: return "Storage Chest";
                case Item.Barrier: return "Wooden Barrier";
                case Item.Stone: return "Stone";
                case Item.Arrow: return "Arrow";
                case Item.Berry: return "Berries";
                case Item.None: return "";
                default: return i.ToString();
            }
        }

        public static int MaxStack(Item i)
        {
            switch (i)
            {
                case Item.Wood: case Item.Stone: return 1000;
                case Item.Arrow: return 64;
                case Item.Berry: return 20;
                case Item.Barrier: return 5;
                case Item.None: return 0;
                default: return 1;
            }
        }

        /// <summary>Tools & weapons land on the hotbar first; materials fill the main inventory first.</summary>
        public static bool PrefersHotbar(Item i) => i != Item.Wood && i != Item.Stone && i != Item.Arrow;

        public static MeleeStats Melee(Item i)
        {
            switch (i)
            {
                case Item.Rock:    return new MeleeStats { Cooldown = RockCooldown, Range = RockRange, PlayerDamage = RockPlayerDamage, WoodGather = RockWoodGather, StoneGather = RockStoneGather, StructureDamage = RockStructureDamage };
                case Item.Hatchet: return new MeleeStats { Cooldown = HatchetCooldown, Range = HatchetRange, PlayerDamage = HatchetPlayerDamage, WoodGather = HatchetWoodGather, StoneGather = HatchetStoneGather, StructureDamage = HatchetStructureDamage };
                case Item.Pickaxe: return new MeleeStats { Cooldown = PickaxeCooldown, Range = PickaxeRange, PlayerDamage = PickaxePlayerDamage, WoodGather = PickaxeWoodGather, StoneGather = PickaxeStoneGather, StructureDamage = PickaxeStructureDamage };
                case Item.Spear:   return new MeleeStats { Cooldown = SpearCooldown, Range = SpearRange, PlayerDamage = SpearPlayerDamage, WoodGather = SpearWoodGather, StoneGather = SpearStoneGather, StructureDamage = SpearStructureDamage };
                default: return default;
            }
        }
        public static bool IsMelee(Item i) => i == Item.Rock || i == Item.Hatchet || i == Item.Pickaxe || i == Item.Spear;

        // ---------- Building ----------
        public static string PieceName(PieceType t) => t.ToString();

        public static int PieceWood(PieceType t)
        {
            switch (t)
            {
                case PieceType.Foundation: return FoundationWood;
                case PieceType.Wall: return WallWood;
                case PieceType.Doorway: return DoorwayWood;
                case PieceType.Floor: return FloorWood;
                case PieceType.Stairs: return StairsWood;
                default: return 0;
            }
        }
        public static int PieceUpgradeStone(PieceType t)
        {
            switch (t)
            {
                case PieceType.Foundation: return FoundationStone;
                case PieceType.Wall: return WallStone;
                case PieceType.Doorway: return DoorwayStone;
                case PieceType.Floor: return FloorStone;
                case PieceType.Stairs: return StairsStone;
                default: return 0;
            }
        }
        public static float PieceHp(PieceType t, int tier)
        {
            bool stone = tier == 1;
            switch (t)
            {
                case PieceType.Foundation: return stone ? FoundationStoneHp : FoundationHp;
                case PieceType.Wall: return stone ? WallStoneHp : WallHp;
                case PieceType.Doorway: return stone ? DoorwayStoneHp : DoorwayHp;
                case PieceType.Floor: return stone ? FloorStoneHp : FloorHp;
                case PieceType.Stairs: return stone ? StairsStoneHp : StairsHp;
                case PieceType.Barrier: return BarrierHp;
                default: return 100;
            }
        }

        // ---------- Crafting ----------
        public const int RecipeCount = 9;
        public static Recipe GetRecipe(int i)
        {
            switch (i)
            {
                case 0: return new Recipe { Output = Item.BuildingPlan, Count = 1, Wood = PlanWood };
                case 1: return new Recipe { Output = Item.Hatchet, Count = 1, Wood = HatchetWood, Stone = HatchetStone };
                case 2: return new Recipe { Output = Item.Pickaxe, Count = 1, Wood = PickaxeWood, Stone = PickaxeStone };
                case 3: return new Recipe { Output = Item.Spear, Count = 1, Wood = SpearWood, Stone = SpearStone };
                case 4: return new Recipe { Output = Item.Bow, Count = 1, Wood = BowWood, Stone = BowStone };
                case 5: return new Recipe { Output = Item.Arrow, Count = Mathf.Max(1, ArrowsPerCraft), Wood = ArrowWood, Stone = ArrowStone };
                case 6: return new Recipe { Output = Item.Ram, Count = 1, Wood = RamWood, Stone = RamStone };
                case 7: return new Recipe { Output = Item.Chest, Count = 1, Wood = ChestWood };
                default: return new Recipe { Output = Item.Barrier, Count = 1, Wood = BarrierWood };
            }
        }
        public static int RecipeIndex(Item output)
        {
            for (int i = 0; i < RecipeCount; i++) if (GetRecipe(i).Output == output) return i;
            return -1;
        }

        // ---------- Areas ----------
        public static int BaseTeamAt(Vector3 p)
        {
            for (int t = 0; t < 2; t++)
                if (Mathf.Abs(p.x - BaseCenter[t].x) <= BaseHalf && Mathf.Abs(p.z - BaseCenter[t].z) <= BaseHalf)
                    return t;
            return -1;
        }

        /// <summary>Raw base membership of a grid cell (ignores the UFO).</summary>
        public static bool CellInBase(int team, int i, int j)
        {
            Vector3 c = new Vector3((i + 0.5f) * Cell, 0, (j + 0.5f) * Cell);
            return BaseTeamAt(c) == team;
        }

        // =====================================================================
        // Tunables: reflection-driven editing, saving and host -> client sync
        // =====================================================================

        static List<FieldInfo> s_Tune;
        static Dictionary<string, object> s_Defaults;
        const string PrefsKey = "RockGame.Tunables";

        public static List<FieldInfo> TuneFields
        {
            get
            {
                if (s_Tune != null) return s_Tune;
                s_Tune = new List<FieldInfo>();
                foreach (var f in typeof(Cfg).GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (f.GetCustomAttribute<TuneAttribute>() != null && !f.IsLiteral && !f.IsInitOnly) s_Tune.Add(f);
                s_Defaults = new Dictionary<string, object>();
                foreach (var f in s_Tune) s_Defaults[f.Name] = f.GetValue(null);
                return s_Tune;
            }
        }

        public static string SectionOf(FieldInfo f) => f.GetCustomAttribute<TuneAttribute>().Section;

        public static string Format(FieldInfo f)
        {
            var v = f.GetValue(null);
            return v is float fl ? fl.ToString("0.###", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool TrySet(FieldInfo f, string text)
        {
            if (f.FieldType == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv)) { f.SetValue(null, fv); return true; }
            if (f.FieldType == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv)) { f.SetValue(null, iv); return true; }
            return false;
        }

        public static bool IsDefault(FieldInfo f)
        {
            _ = TuneFields;
            return Equals(s_Defaults[f.Name], f.GetValue(null));
        }

        /// <summary>"Name=value;Name=value;..." of every tunable (sent from host to client).</summary>
        public static string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var f in TuneFields) sb.Append(f.Name).Append('=').Append(Format(f)).Append(';');
            return sb.ToString();
        }

        public static void Apply(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            var map = new Dictionary<string, FieldInfo>();
            foreach (var f in TuneFields) map[f.Name] = f;
            foreach (var pair in data.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (map.TryGetValue(pair.Substring(0, eq), out var f)) TrySet(f, pair.Substring(eq + 1));
            }
        }

        public static void ResetDefaults()
        {
            foreach (var f in TuneFields) f.SetValue(null, s_Defaults[f.Name]);
        }

        public static void SavePrefs()
        {
            PlayerPrefs.SetString(PrefsKey, Serialize());
            PlayerPrefs.Save();
        }

        /// <summary>Loads this machine's own settings (also used to undo the host's settings after leaving a game).</summary>
        public static void LoadPrefs()
        {
            ResetDefaults();
            Apply(PlayerPrefs.GetString(PrefsKey, ""));
        }
    }
}
