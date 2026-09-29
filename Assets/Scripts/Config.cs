using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RockGame
{
    // new items go at the end so the byte values of the old ones never change
    public enum Item : byte { None, Rock, BuildingPlan, Hatchet, Pickaxe, Spear, Bow, Ram, Chest, Barrier, Wood, Stone, Arrow, Berry, C4, DeathWand, Helmet, InvisPotion, Chainsaw, Crossbow, Armor, FortTower, Car, Saddle,
        Meat, AirdropSignal, Sniper, PortalGun, Jetpack, SlenderEgg, BuildEgg, GiantStaff, RocketLauncher, BombBush, TreeCamo, Airstrike, Wallhack }

    public enum PieceType : byte { Foundation, Wall, Doorway, Floor, Stairs, Barrier, Window, Tower, EggBlock }

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
        /// <summary>Wood mode: no stone anywhere, everything costs wood, no pickaxe, no stone upgrades.</summary>
        public static bool WoodMode;
        public static int MapSeed;
        public static float MapHalf => SmallMap ? 62f : 100f;
        public const float BaseHalf = 18f; // bases are 36x36m, aligned to the 3m build grid
        public static readonly Vector3[] BaseCenter = { new Vector3(0, 0, -75), new Vector3(0, 0, 75) };
        public static string MapLabel => (SmallMap ? "Small " : "Big ") + Map + (WoodMode ? " (Wood mode)" : "") + (AirdropSides ? " (airdrops on both sides)" : "") + (RespawnLoot ? " (respawn loot)" : "");
        /// <summary>Airdrops come down on both sides (one in each half, each on its own timer) instead of anywhere.</summary>
        public static bool AirdropSides;
        /// <summary>Game option: every time you respawn you get a random airdrop item.</summary>
        public static bool RespawnLoot;
        public const int SmallBit = 16, WoodBit = 32, SidesBit = 64, RespawnLootBit = 128;
        /// <summary>Packs the map/mode choice for syncing; the seed is sent separately.</summary>
        public static int MapKey => (int)Map | (SmallMap ? SmallBit : 0) | (WoodMode ? WoodBit : 0) | (AirdropSides ? SidesBit : 0) | (RespawnLoot ? RespawnLootBit : 0);

        /// <summary>Watch towers on the Highlands map (world positions of their feet), point-mirrored between the halves.</summary>
        public static readonly List<Vector3> Towers = new List<Vector3>();

        public static void SetMap(int key, int seed)
        {
            Map = (MapKind)(key & 15);
            SmallMap = (key & SmallBit) != 0;
            WoodMode = (key & WoodBit) != 0;
            AirdropSides = (key & SidesBit) != 0;
            RespawnLoot = (key & RespawnLootBit) != 0;
            MapSeed = seed;
            float d = SmallMap ? 42f : 75f;
            BaseCenter[0] = new Vector3(0, 0, -d);
            BaseCenter[1] = new Vector3(0, 0, d);
            Towers.Clear();
        }

        // ---------- Bedrock spawn + alien machine ----------
        // Every base has an unbreakable 2x2-cell silver bedrock in its middle. You spawn on it; the alien machine
        // stands on its back edge (away from the map centre). The machine crafts (E) and has the ball socket.
        public const float BedrockHalf = 3f;

        /// <summary>Direction from a base's centre to the back of its bedrock (away from the middle of the map).</summary>
        public static Vector3 BackDir(int team) => team == 0 ? Vector3.back : Vector3.forward;
        public static Vector3 BedrockCenter(int team) => BaseCenter[team];
        // the machine stands clear of the bedrock's back edge so walls fit behind it
        public static Vector3 MachinePos(int team) => BaseCenter[team] + BackDir(team) * 2.0f + Vector3.up * BaseY;
        /// <summary>Centre of the ball when it sits in the machine's socket.</summary>
        public static Vector3 SocketPos(int team) => BaseCenter[team] + BackDir(team) * 0.35f + Vector3.up * (BaseY + 0.64f);
        /// <summary>Where you (re)spawn: on the front half of your bedrock, facing the middle of the map.</summary>
        public static Vector3 SpawnPos(int team) => BaseCenter[team] - BackDir(team) * 1.7f + Vector3.up * (BaseY + 0.05f);
        public static float SpawnYaw(int team) => Quaternion.LookRotation(-BackDir(team)).eulerAngles.y;

        public static bool IsBedrockCell(int i, int j)
        {
            for (int t = 0; t < 2; t++)
            {
                int ci = Mathf.RoundToInt(BaseCenter[t].x / Cell), cj = Mathf.RoundToInt(BaseCenter[t].z / Cell);
                if ((i == ci - 1 || i == ci) && (j == cj - 1 || j == cj)) return true;
            }
            return false;
        }

        /// <summary>Cells covered by the bedrock can't take foundations, stairs or barriers (chests are fine).</summary>
        public static bool CellBlocked(int i, int j) => IsBedrockCell(i, j);
        public static bool PointBlocked(Vector3 p) => CellBlocked(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

        public static readonly Color[] TeamColor = { new Color(0.25f, 0.5f, 1f), new Color(1f, 0.3f, 0.25f), new Color(0.3f, 0.85f, 0.3f), new Color(1f, 0.85f, 0.2f) };
        public static readonly string[] TeamName = { "BLUE", "RED", "GREEN", "YELLOW" };
        public static string TeamLabel(int t) => t >= 0 && t < TeamName.Length ? TeamName[t] : "ALIEN";
        /// <summary>How many teams (bases) there are in this match.</summary>
        public static int TeamCount = 2;

        /// <summary>Which team's side of the map a point is on (the base it's most in the direction of).</summary>
        public static int RegionOf(Vector3 p)
        {
            var d = new Vector2(p.x, p.z);
            if (d.sqrMagnitude < 0.01f) return 0;
            int best = 0;
            float bd = float.MinValue;
            for (int t = 0; t < TeamCount; t++)
            {
                var c = new Vector2(BaseCenter[t].x, BaseCenter[t].z).normalized;
                float dot = Vector2.Dot(d.normalized, c);
                if (dot > bd) { bd = dot; best = t; }
            }
            return best;
        }
        public static readonly Vector3 ArenaCenter = new Vector3(0, 0, 1000);
        public const float ArenaHalf = 22f; // radius of the stadium pit
        public static readonly Vector3 BallDropPoint = new Vector3(0, 40, 0);

        // ---------- Inventory (not tunable) ----------
        public const int HotbarSize = 7, MainSize = 21, PlayerSlots = HotbarSize + MainSize;
        public const int ChestSlots = 14;
        public const float EyeHeight = 1.6f, CrouchEyeHeight = 1.05f;
        public const float StandHeight = 1.8f, CrouchHeight = 1.2f;
        public const float InteractRange = 3f, LootRange = 4f;
        public const float MachineRange = 6f; // how close you have to be to the machine to craft / socket the ball
        public const float Cell = 3f, BaseY = 1f, LevelH = 3f;
        public const int MaxLevel = 4;
        public const float BuildRange = 7f, DeployRange = 5f;
        public const float BowMinDraw = 0.2f, SpearMinDraw = 0.25f;
        public const float SpearPickupRange = 3f;

        // ---------- Match ----------
        [Tune("Match")] public static float BallDropDelay = 300f;   // 5 min behind the glass wall to gather and build
        [Tune("Match")] public static float MatchLength = 420f;     // + 7 min with the ball
        [Tune("Match")] public static float SuddenDeathLength = 180f;
        [Tune("Match")] public static float FastBallDropDelay = 10f;
        [Tune("Match")] public static float FastMatchLength = 90f;
        [Tune("Match")] public static float RespawnTime = 5f;
        [Tune("Match")] public static float ItemDespawnTime = 300f;

        // ---------- Airdrops (after the wall drops) ----------
        [Tune("Airdrop")] public static float AirdropInterval = 60f;     // after the last one was emptied
        [Tune("Airdrop")] public static float AirdropBaseDistance = 25f; // never this close to a base
        [Tune("Airdrop")] public static float C4Fuse = 3f, C4Radius = 5f, C4PlayerDamage = 150f, C4KillRadius = 1.6f;
        [Tune("Airdrop")] public static int SniperAmmo = 3, JetpackFuel = 100, PortalShots = 2, AirdropSignalWood = 2000;
        [Tune("Airdrop")] public static float JetpackSeconds = 8f, JetpackThrust = 9f, GiantTime = 30f, GiantScale = 3f;
        [Tune("Airdrop")] public static float SlenderSpeed = 4.6f, SlenderHp = 150f, SlenderLife = 60f;
        [Tune("Airdrop")] public static float RocketSpeed = 32f, RocketRadius = 3.5f, RocketStructureDamage = 900f, RocketPlayerDamage = 70f;
        [Tune("Airdrop")] public static float BombBushDamage = 150f, AirstrikeRadius = 14f, AirstrikeDelay = 4f, EggBlockHp = 60f;
        [Tune("Airdrop")] public static float WandRange = 90f, WandRadius = 3f;
        [Tune("Airdrop")] public static float InvisTime = 30f, InvisRevealTime = 1.2f;
        [Tune("Airdrop")] public static int ChainsawUses = 67, AirdropResources = 1000;

        // ---------- Player ----------
        [Tune("Player")] public static float MaxHealth = 100f;
        [Tune("Player")] public static float WalkSpeed = 5f;
        [Tune("Player")] public static float SprintSpeed = 7.5f;
        [Tune("Player")] public static float CrouchSpeed = 2.6f;
        [Tune("Player")] public static float JumpSpeed = 7.2f;
        [Tune("Player")] public static float Gravity = 20f;
        [Tune("Player")] public static float BallCarrySpeedMul = 1f;
        [Tune("Player")] public static float BallThrowSpeed = 16f;
        [Tune("Player")] public static float BerryHeal = 30f;
        [Tune("Player")] public static int ArmorHp = 100;              // wooden armour: a second health bar, used up first (max 255)
        [Tune("Player")] public static float HeadshotMul = 1.5f;
        [Tune("Player")] public static float ModelWidth = 1.3f;       // alien model width scale
        [Tune("Player")] public static float HitboxRadius = 0.52f;
        [Tune("Player")] public static float MeleeAssist = 0.35f;     // melee counts as a hit if it passes this close (m)
        [Tune("Player")] public static float ProjectileAssist = 0.15f; // same for arrows / thrown spears

        // ---------- Melee ----------
        [Tune("Rock")] public static float RockCooldown = 0.6f, RockRange = 2.3f, RockPlayerDamage = 12f, RockWoodGather = 5f, RockStoneGather = 4f, RockStructureDamage = 6f;
        [Tune("Hatchet")] public static float HatchetCooldown = 0.7f, HatchetRange = 2.5f, HatchetPlayerDamage = 14f, HatchetWoodGather = 15f, HatchetStoneGather = 2f, HatchetStructureDamage = 12f;
        [Tune("Pickaxe")] public static float PickaxeCooldown = 0.8f, PickaxeRange = 2.5f, PickaxePlayerDamage = 14f, PickaxeWoodGather = 3f, PickaxeStoneGather = 12f, PickaxeStructureDamage = 12f;
        [Tune("Spear")] public static float SpearCooldown = 0.9f, SpearRange = 3.4f, SpearPlayerDamage = 35f, SpearWoodGather = 2f, SpearStoneGather = 1f, SpearStructureDamage = 10f;
        [Tune("Spear")] public static float SpearDrawTime = 0.6f, SpearThrowSpeed = 30f, SpearThrowDamage = 60f, SpearThrowStructureDamage = 8f;
        [Tune("Chainsaw")] public static float ChainsawCooldown = 0.15f, ChainsawRange = 2.4f, ChainsawPlayerDamage = 8f, ChainsawWoodGather = 12f, ChainsawStoneGather = 10f, ChainsawStructureDamage = 12f;
        [Tune("Melee")] public static float StoneStructureMeleeMul = 0.2f; // stone is very hard to melee - bring a ram

        // ---------- Bow ----------
        [Tune("Bow")] public static float BowDrawTime = 0.8f, ArrowSpeed = 55f, ArrowGravity = 9.81f, ArrowPlayerDamage = 50f, ArrowWoodStructureDamage = 4f;
        [Tune("Crossbow")] public static float CrossbowDamage = 55f, CrossbowSpeed = 90f, CrossbowReload = 1.6f, CrossbowZoomFov = 45f;
        [Tune("Spear")] public static float SpearGravity = 9.81f;

        // ---------- Battering ram (hand held) ----------
        // Hold LMB to wind up, then it slams whatever enemy piece you look at:
        // wooden pieces are destroyed outright, stone pieces are knocked back down to wood.
        [Tune("Ram")] public static float RamWindup = 1.5f, RamRange = 3f, RamMoveMul = 0.75f;
        [Tune("Ram")] public static int RamUses = 3;

        // ---------- Resources ----------
        [Tune("Resources")] public static int TreeAmount = 300, StoneAmount = 250, TreeFellBonus = 100;
        [Tune("Resources")] public static float WeakSpotMul = 2f, NodeRespawnTime = 60f, BushRespawnTime = 45f;

        // ---------- Building ----------
        [Tune("Building")] public static float BuildCooldown = 0f, UpgradeCooldown = 0f, DemolishRefund = 0.5f;
        [Tune("Building")] public static int FoundationWood = 15, WallWood = 15, DoorwayWood = 20, WindowWood = 15, FloorWood = 12, StairsWood = 20;
        [Tune("Building")] public static int FoundationStone = 50, WallStone = 50, DoorwayStone = 40, WindowStone = 45, FloorStone = 30, StairsStone = 30;
        [Tune("Building HP")] public static float FoundationHp = 500, WallHp = 400, DoorwayHp = 350, WindowHp = 350, FloorHp = 300, StairsHp = 300;
        [Tune("Building HP")] public static float FoundationStoneHp = 1800, WallStoneHp = 1500, DoorwayStoneHp = 1200, WindowStoneHp = 1300, FloorStoneHp = 1000, StairsStoneHp = 1000;
        [Tune("Building HP")] public static float BarrierHp = 250, ChestHp = 300, TowerHp = 800;

        // ---------- Crafting (at the alien machine) ----------
        [Tune("Crafting")] public static int PlanWood = 5;
        [Tune("Crafting")] public static int HatchetWood = 30, HatchetStone = 10, PickaxeWood = 30, PickaxeStone = 10;
        [Tune("Crafting")] public static int SpearWood = 75, SpearStone = 0, BowWood = 100, BowStone = 15;
        [Tune("Crafting")] public static int ArrowWood = 10, ArrowStone = 0, ArrowsPerCraft = 1;
        [Tune("Crafting")] public static int RamWood = 125, RamStone = 50;
        [Tune("Crafting")] public static int ChestWood = 50, BarrierWood = 20;
        [Tune("Crafting")] public static int CrossbowWood = 500, SaddleWood = 1000;
        public static int FortTowerWood = 1000; // only used for the demolish refund (the fort is an airdrop item now)

        // ---------- Vehicles ----------
        [Tune("Vehicles")] public static float CarSpeed = 17f, CarReverseSpeed = 6f, CarAccel = 9f, CarTurn = 95f, CarHitDamage = 30f, CarKnockback = 11f;
        [Tune("Vehicles")] public static float HorseWalk = 4.5f, HorseSprint = 11f, HorseJump = 8.5f, HorseHp = 120f;
        [Tune("Vehicles")] public static int HorsesPerSide = 3;

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
                case Item.C4: return "C4";
                case Item.DeathWand: return "Death Wand";
                case Item.Helmet: return "Alien Helmet";
                case Item.InvisPotion: return "Invisibility Potion";
                case Item.Chainsaw: return "Chainsaw";
                case Item.Crossbow: return "Crossbow";
                case Item.Armor: return "Armour";
                case Item.FortTower: return "Fort Tower";
                case Item.Car: return "Wooden Car";
                case Item.Saddle: return "Saddle";
                case Item.Meat: return "Horse Meat";
                case Item.AirdropSignal: return "Airdrop Signal";
                case Item.Sniper: return "Sniper Rifle";
                case Item.PortalGun: return "Portal Gun";
                case Item.Jetpack: return "Jetpack";
                case Item.SlenderEgg: return "Slenderman Egg";
                case Item.BuildEgg: return "Build Egg";
                case Item.GiantStaff: return "Staff of the Giant";
                case Item.RocketLauncher: return "Rocket Launcher";
                case Item.BombBush: return "Fake Bomb Bush";
                case Item.TreeCamo: return "Tree Camo";
                case Item.Airstrike: return "Airstrike";
                case Item.Wallhack: return "Wallhack Glasses";
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

        /// <summary>Materials: they stack onto what you already have, otherwise fill the hotbar from slot 7 backwards.</summary>
        public static bool IsMat(Item i) => i == Item.Wood || i == Item.Stone || i == Item.Arrow;

        /// <summary>Items whose Data byte is a durability / health counter (shown as a bar).</summary>
        public static int MaxData(Item i)
        {
            switch (i)
            {
                case Item.Ram: return RamUses;
                case Item.Chainsaw: return ChainsawUses;
                case Item.Armor: return ArmorHp;
                case Item.Sniper: return SniperAmmo;
                case Item.Jetpack: return JetpackFuel;
                case Item.PortalGun: return PortalShots;
                default: return 0;
            }
        }

        /// <summary>Everything an airdrop (or the respawn-loot option) can give you.</summary>
        public static readonly Item[] AirdropLoot =
        {
            Item.C4, Item.DeathWand, Item.Helmet, Item.Wood, Item.InvisPotion, Item.Chainsaw, Item.Armor, Item.FortTower,
            Item.Sniper, Item.PortalGun, Item.Jetpack, Item.SlenderEgg, Item.BuildEgg, Item.GiantStaff, Item.RocketLauncher,
            Item.BombBush, Item.TreeCamo, Item.Airstrike, Item.Wallhack,
        };

        public static MeleeStats Melee(Item i)
        {
            switch (i)
            {
                case Item.Rock:    return new MeleeStats { Cooldown = RockCooldown, Range = RockRange, PlayerDamage = RockPlayerDamage, WoodGather = RockWoodGather, StoneGather = RockStoneGather, StructureDamage = RockStructureDamage };
                case Item.Hatchet: return new MeleeStats { Cooldown = HatchetCooldown, Range = HatchetRange, PlayerDamage = HatchetPlayerDamage, WoodGather = HatchetWoodGather, StoneGather = HatchetStoneGather, StructureDamage = HatchetStructureDamage };
                case Item.Pickaxe: return new MeleeStats { Cooldown = PickaxeCooldown, Range = PickaxeRange, PlayerDamage = PickaxePlayerDamage, WoodGather = PickaxeWoodGather, StoneGather = PickaxeStoneGather, StructureDamage = PickaxeStructureDamage };
                case Item.Spear:   return new MeleeStats { Cooldown = SpearCooldown, Range = SpearRange, PlayerDamage = SpearPlayerDamage, WoodGather = SpearWoodGather, StoneGather = SpearStoneGather, StructureDamage = SpearStructureDamage };
                case Item.Chainsaw: return new MeleeStats { Cooldown = ChainsawCooldown, Range = ChainsawRange, PlayerDamage = ChainsawPlayerDamage, WoodGather = ChainsawWoodGather, StoneGather = ChainsawStoneGather, StructureDamage = ChainsawStructureDamage };
                default: return default;
            }
        }
        public static bool IsMelee(Item i) => i == Item.Rock || i == Item.Hatchet || i == Item.Pickaxe || i == Item.Spear || i == Item.Chainsaw;

        // ---------- Building ----------
        public static string PieceName(PieceType t) => t == PieceType.Tower ? "Fort Tower" : t.ToString();
        /// <summary>Pieces that sit on the 3 m building grid (placed with the building plan).</summary>
        public static bool IsGridPiece(PieceType t) => t != PieceType.Barrier && t != PieceType.Tower && t != PieceType.EggBlock;

        public static int PieceWood(PieceType t)
        {
            switch (t)
            {
                case PieceType.Foundation: return FoundationWood;
                case PieceType.Wall: return WallWood;
                case PieceType.Doorway: return DoorwayWood;
                case PieceType.Window: return WindowWood;
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
                case PieceType.Window: return WindowStone;
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
                case PieceType.Window: return stone ? WindowStoneHp : WindowHp;
                case PieceType.Tower: return TowerHp;
                case PieceType.EggBlock: return EggBlockHp;
                case PieceType.Floor: return stone ? FloorStoneHp : FloorHp;
                case PieceType.Stairs: return stone ? StairsStoneHp : StairsHp;
                case PieceType.Barrier: return BarrierHp;
                default: return 100;
            }
        }

        // ---------- Crafting ----------
        static readonly Item[] k_Recipes = { Item.BuildingPlan, Item.Hatchet, Item.Pickaxe, Item.Spear, Item.Bow, Item.Arrow, Item.Crossbow, Item.Ram, Item.Chest, Item.Barrier, Item.Saddle, Item.AirdropSignal };

        /// <summary>Recipes available in this mode (wood mode has no pickaxe).</summary>
        public static int RecipeCount => WoodMode ? k_Recipes.Length - 1 : k_Recipes.Length;

        public static Recipe GetRecipe(int i)
        {
            if (WoodMode && i >= 2) i++; // skip the pickaxe
            Recipe r;
            switch (k_Recipes[Mathf.Clamp(i, 0, k_Recipes.Length - 1)])
            {
                case Item.BuildingPlan: r = new Recipe { Output = Item.BuildingPlan, Count = 1, Wood = PlanWood }; break;
                case Item.Hatchet: r = new Recipe { Output = Item.Hatchet, Count = 1, Wood = HatchetWood, Stone = HatchetStone }; break;
                case Item.Pickaxe: r = new Recipe { Output = Item.Pickaxe, Count = 1, Wood = PickaxeWood, Stone = PickaxeStone }; break;
                case Item.Spear: r = new Recipe { Output = Item.Spear, Count = 1, Wood = SpearWood, Stone = SpearStone }; break;
                case Item.Bow: r = new Recipe { Output = Item.Bow, Count = 1, Wood = BowWood, Stone = BowStone }; break;
                case Item.Arrow: r = new Recipe { Output = Item.Arrow, Count = Mathf.Max(1, ArrowsPerCraft), Wood = ArrowWood, Stone = ArrowStone }; break;
                case Item.Ram: r = new Recipe { Output = Item.Ram, Count = 1, Wood = RamWood, Stone = RamStone }; break;
                case Item.Chest: r = new Recipe { Output = Item.Chest, Count = 1, Wood = ChestWood }; break;
                case Item.Crossbow: r = new Recipe { Output = Item.Crossbow, Count = 1, Wood = CrossbowWood }; break;
                case Item.Saddle: r = new Recipe { Output = Item.Saddle, Count = 1, Wood = SaddleWood }; break;
                case Item.AirdropSignal: r = new Recipe { Output = Item.AirdropSignal, Count = 1, Wood = AirdropSignalWood }; break;
                default: r = new Recipe { Output = Item.Barrier, Count = 1, Wood = BarrierWood }; break;
            }
            if (WoodMode) { r.Wood += r.Stone; r.Stone = 0; } // everything costs wood only
            return r;
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

        /// <summary>Crafting works anywhere inside your own base.</summary>
        public static bool CanCraftAt(int team, Vector3 p) => BaseTeamAt(p) == team;

        /// <summary>Raw base membership of a grid cell.</summary>
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
        // v2: only changed values are saved, so new default values (e.g. price changes) reach players who saved settings before
        const string PrefsKey = "RockGame.Tunables.v2";

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

        /// <summary>"Name=value;Name=value;..." of every tunable (sent from host to client), or only the changed ones.</summary>
        public static string Serialize(bool changedOnly = false)
        {
            var sb = new StringBuilder();
            foreach (var f in TuneFields)
                if (!changedOnly || !IsDefault(f)) sb.Append(f.Name).Append('=').Append(Format(f)).Append(';');
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
            PlayerPrefs.SetString(PrefsKey, Serialize(true));
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
