using UnityEngine;

namespace RockGame
{
    // Hotbar order == enum order (keys 1..7)
    public enum Item : byte { Rock, BuildingPlan, Hatchet, Pickaxe, Spear, Bow, Ram }

    public enum PieceType : byte { Foundation, Wall, Doorway, Floor, Stairs }

    public enum GameState : byte { Waiting, PreBall, BallLive, SuddenDeath, GameOver }

    public struct MeleeStats
    {
        public float Cooldown, Range, PlayerDamage, WoodGather, StoneGather, StructureDamage;
    }

    public struct Recipe
    {
        public string Name;
        public int Wood, Stone;
    }

    /// <summary>All gameplay tuning lives here.</summary>
    public static class Cfg
    {
        // ---------- Map ----------
        public const float MapHalf = 100f;
        public const float BaseHalf = 18f; // bases are 36x36m, aligned to the 3m build grid
        public static readonly Vector3[] BaseCenter = { new Vector3(0, 0, -75), new Vector3(0, 0, 75) };
        public static readonly Color[] TeamColor = { new Color(0.25f, 0.5f, 1f), new Color(1f, 0.3f, 0.25f) };
        public static readonly string[] TeamName = { "BLUE", "RED" };
        public static readonly Vector3 ArenaCenter = new Vector3(0, 0, 1000);
        public const float ArenaHalf = 20f;
        public static readonly Vector3 BallDropPoint = new Vector3(0, 40, 0);

        // ---------- Match ----------
        public const float BallDropDelay = 180f;  // 3 min to gather and build
        public const float MatchLength = 420f;    // + 7 min with the ball = 10 min game
        public const float FastBallDropDelay = 10f;
        public const float FastMatchLength = 90f;
        public const float RespawnTime = 5f;
        public const float MaxHealth = 100f;

        // ---------- Movement ----------
        public const float WalkSpeed = 5f, SprintSpeed = 7.5f, JumpSpeed = 7.2f, Gravity = 20f, EyeHeight = 1.6f;
        public const float BallCarrySpeedMul = 0.75f;
        public const float InteractRange = 3f;

        // ---------- Weapons & tools ----------
        public static MeleeStats Melee(Item i)
        {
            switch (i)
            {
                case Item.Rock:    return new MeleeStats { Cooldown = 0.6f, Range = 2.3f, PlayerDamage = 12, WoodGather = 5,  StoneGather = 4,  StructureDamage = 3 };
                case Item.Hatchet: return new MeleeStats { Cooldown = 0.7f, Range = 2.5f, PlayerDamage = 14, WoodGather = 15, StoneGather = 2,  StructureDamage = 6 };
                case Item.Pickaxe: return new MeleeStats { Cooldown = 0.8f, Range = 2.5f, PlayerDamage = 14, WoodGather = 3,  StoneGather = 12, StructureDamage = 6 };
                case Item.Spear:   return new MeleeStats { Cooldown = 0.9f, Range = 3.4f, PlayerDamage = 35, WoodGather = 2,  StoneGather = 1,  StructureDamage = 5 };
                default: return default;
            }
        }
        public const int ItemCount = 7;
        public static bool IsMelee(Item i) => i == Item.Rock || i == Item.Hatchet || i == Item.Pickaxe || i == Item.Spear;

        public const float HeadshotMul = 1.5f;
        public const float StoneStructureMeleeMul = 0.2f; // stone is very hard to melee - bring a ram

        public const float BowDrawTime = 0.8f, BowMinDraw = 0.2f;
        public const float ArrowSpeed = 55f, ArrowGravity = 9.81f;
        public const float ArrowPlayerDamage = 50f; // at full draw
        public const float ArrowWoodStructureDamage = 2f;

        // Spear throw (Rust style): hold RMB to wind up, LMB to throw. The spear stays in the world / in the victim.
        public const float SpearDrawTime = 0.6f, SpearMinDraw = 0.25f;
        public const float SpearThrowSpeed = 30f, SpearGravity = 9.81f;
        public const float SpearThrowDamage = 60f;        // at full wind-up
        public const float SpearThrowStructureDamage = 4f; // wood only
        public const float SpearPickupRange = 3f;

        // ---------- Building ----------
        public const float Cell = 3f, BaseY = 1f, LevelH = 3f;
        public const int MaxLevel = 4;
        public const float BuildCooldown = 1.1f;   // janky & slow, Rust style
        public const float UpgradeCooldown = 1.5f;
        public const float BuildRange = 7f;

        public static string PieceName(PieceType t)
        {
            switch (t)
            {
                default: return t.ToString();
            }
        }
        public static int PieceWood(PieceType t)
        {
            switch (t)
            {
                case PieceType.Foundation: return 50;
                case PieceType.Wall: return 50;
                case PieceType.Doorway: return 60;
                case PieceType.Floor: return 40;
                case PieceType.Stairs: return 60;
                default: return 0;
            }
        }
        public static int PieceUpgradeStone(PieceType t)
        {
            switch (t)
            {
                case PieceType.Foundation: return 150;
                case PieceType.Wall: return 150;
                case PieceType.Doorway: return 120;
                case PieceType.Floor: return 100;
                case PieceType.Stairs: return 100;
                default: return 0;
            }
        }
        public static float PieceHp(PieceType t, int tier)
        {
            bool stone = tier == 1;
            switch (t)
            {
                case PieceType.Foundation: return stone ? 1800 : 500;
                case PieceType.Wall: return stone ? 1500 : 400;
                case PieceType.Doorway: return stone ? 1200 : 350;
                case PieceType.Floor: return stone ? 1000 : 300;
                case PieceType.Stairs: return stone ? 1000 : 300;
                default: return 100;
            }
        }

        // ---------- Battering ram (hand held) ----------
        // Hold LMB to wind up, then it slams whatever enemy piece you look at:
        // wooden pieces are destroyed outright, stone pieces are knocked back down to wood.
        public const float RamWindup = 1.5f;
        public const float RamRange = 3f;
        public const int RamUses = 3;        // strikes per crafted ram
        public const float RamMoveMul = 0.75f;

        // ---------- Resources ----------
        public const int TreeAmount = 300, StoneAmount = 250;
        public const float NodeRespawnTime = 60f;

        // ---------- Crafting (anywhere, no table needed) ----------
        public const int ArrowsPerCraft = 10;
        public enum R { BuildingPlan, Hatchet, Pickaxe, Spear, Bow, Arrows, Ram }
        public static readonly Recipe[] Recipes =
        {
            new Recipe { Name = "Building Plan",        Wood = 30,  Stone = 0 },
            new Recipe { Name = "Stone Hatchet",        Wood = 60,  Stone = 20 },
            new Recipe { Name = "Stone Pickaxe",        Wood = 60,  Stone = 20 },
            new Recipe { Name = "Spear",                Wood = 150, Stone = 25 },
            new Recipe { Name = "Bow",                  Wood = 200, Stone = 30 },
            new Recipe { Name = "Arrows x10",           Wood = 30,  Stone = 10 },
            new Recipe { Name = "Battering Ram (3 hits)", Wood = 450, Stone = 150 },
        };

        public static string ItemName(Item i)
        {
            switch (i)
            {
                case Item.BuildingPlan: return "Building Plan";
                case Item.Hatchet: return "Stone Hatchet";
                case Item.Pickaxe: return "Stone Pickaxe";
                case Item.Ram: return "Battering Ram";
                default: return i.ToString();
            }
        }

        public static int BaseTeamAt(Vector3 p)
        {
            for (int t = 0; t < 2; t++)
                if (Mathf.Abs(p.x - BaseCenter[t].x) <= BaseHalf && Mathf.Abs(p.z - BaseCenter[t].z) <= BaseHalf)
                    return t;
            return -1;
        }

        public static bool CellInBase(int team, int i, int j)
        {
            Vector3 c = new Vector3((i + 0.5f) * Cell, 0, (j + 0.5f) * Cell);
            return BaseTeamAt(c) == team;
        }
    }
}
