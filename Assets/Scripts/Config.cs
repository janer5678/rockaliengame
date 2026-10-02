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
        Meat, AirdropSignal, Sniper, PortalGun, Jetpack, SlenderEgg, BuildEgg, GiantStaff, RocketLauncher, BombBush, TreeCamo, Airstrike, Wallhack,
        EnderPearl, Pistol, PistolAmmo, HeavyArmor, TreeCracker, FortifyBuff, WoodGenBuff, Boat,
        Sword, Shotgun, ShotgunShell, Revolver, RevolverAmmo,
        Dna,
        Workbench }

    /// <summary>
    /// The game mode (picked in the main menu, next to the players). They don't mix:
    /// Classic - the original game. Arsenal - everything costs much less, plus a menu of powerful items (2-4k wood).
    /// Builder - Arsenal's items, crafting anywhere with a wait while each item is made, build anywhere with pieces that lock
    /// onto each other, and you win with the ball inside a fort your team built. Fun - every 9 s everyone gets the same random
    /// item (any item in the game). Fun Random - every 9 s each player gets their own random airdrop item.
    /// Auto Wood - Arsenal, but wood piles up by itself at your base.
    /// (Synced in 4 bits of the map key: never reorder.)
    /// </summary>
    public enum GameRules : byte { Classic, Arsenal, Builder, Fun, FunRandom, FunRandomLimited, Primitive, BuildingPrimitive, AutoWood, Tutorial, Dna }

    public enum PieceType : byte { Foundation, Wall, Doorway, Floor, Stairs, Barrier, Window, Tower, EggBlock }

    public enum GameState : byte { Waiting, PreBall, BallLive, SuddenDeath, GameOver }

    public enum MapKind : byte { Plains, Highlands,
        Beach, Canyon, Frostlake, Volcano, Ruins } // THEME MAPS (the second line)

    /// <summary>Map size: Big (the original), Small, and 1.5x / 2x versions of Big.</summary>
    public enum MapSize : byte { Big, Small, Large, Huge }

    /// <summary>
    /// 1v1 / 2v2 / 3v3 / 4v4: red vs blue. Free for all: 3 or 4 players, each with their own base (glass walls in an X).
    /// 2v2v2 / 2v2v2v2: 3 or 4 teams of two on the same X-shaped map. (Values are synced in 3 bits: never reorder.)
    /// </summary>
    public enum GameMode : byte { Duel, Teams, Ffa3, Ffa4, Teams3, Teams4, Trio, Quad }

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
    public static partial class Cfg
    {
        // ---------- Map (chosen by the host in the menu) ----------
        public static MapKind Map = MapKind.Plains;
        public static MapSize Size = MapSize.Big;
        public static bool SmallMap => Size == MapSize.Small;
        public static float SizeScale => Size == MapSize.Large ? 1.5f : Size == MapSize.Huge ? 2f : 1f;
        public static string SizeLabel(MapSize s) => s == MapSize.Small ? "Small" : s == MapSize.Large ? "Large" : s == MapSize.Huge ? "Huge" : "Medium";
        /// <summary>Wood mode: no stone anywhere, everything costs wood, no pickaxe, no stone upgrades.</summary>
        public static bool WoodMode;
        public static int MapSeed;
        public static float MapHalf => SmallMap ? 62f : 100f * SizeScale;
        public const float BaseHalf = 18f; // bases are 36x36m, aligned to the 3m build grid
        public static readonly Vector3[] BaseCenter = { new Vector3(0, 0, -75), new Vector3(0, 0, 75), new Vector3(75, 0, 0), new Vector3(-75, 0, 0) };
        public static string MapLabel => ModeLabel + ", " + SizeLabel(Size).ToLower() + " " + (ThemeMaps.IsTheme ? ThemeMaps.Label(Map) : Map.ToString()) /* THEME MAPS */ + (WoodMode ? " (Wood mode)" : "") + (AirdropCenter ? " (airdrops in the middle)" : AirdropSides ? " (airdrops on both sides)" : "") + (RespawnLoot ? " (respawn loot)" : "");
        /// <summary>Airdrops come down on both sides (one in each half, each on its own timer) instead of anywhere.</summary>
        public static bool AirdropSides;
        /// <summary>Airdrops always come down in the middle of the map.</summary>
        public static bool AirdropCenter;
        /// <summary>Game option: every time you respawn you get a random airdrop item.</summary>
        public static bool RespawnLoot;
        public const int SmallBit = 16, WoodBit = 32, SidesBit = 64, RespawnLootBit = 128, ModeShift = 8, ModeMask = 7, SizeShift = 12, CenterBit = 1 << 14, RulesShift = 15, RulesMask = 15, GraphicsShift = 19, GraphicsMask = 3;
        public static GameMode Mode = GameMode.Duel;
        public static GameRules Rules = GameRules.Classic;
        /// <summary>Arsenal and Builder: cheap items and the powerful items menu.</summary>
        public static bool PowerMenu => Rules == GameRules.Arsenal || Rules == GameRules.Builder || Rules == GameRules.AutoWood;
        /// <summary>Auto Wood: Arsenal, plus wood piles up at every base by itself.</summary>
        public static bool AutoWood => Rules == GameRules.AutoWood;
        /// <summary>The graphics everyone plays with, picked by the host (0 Normal, 1 PSX, 2 AI PSX TEST). Synced in the map key.</summary>
        public static int HostGraphics;
        /// <summary>Builder: craft anywhere (with a wait), build anywhere, win with the ball in your own fort.</summary>
        public static bool Builder => Rules == GameRules.Builder || Rules == GameRules.BuildingPrimitive;
        public static bool FunRules => Rules == GameRules.Fun || Rules == GameRules.FunRandom || Rules == GameRules.FunRandomLimited;
        /// <summary>Fun Random Limited and Primitive: only the hatchet, spear, building plan and ram can be crafted.</summary>
        public static bool LimitedCrafting => Rules == GameRules.FunRandomLimited || Rules == GameRules.Primitive || Rules == GameRules.BuildingPrimitive;
        /// <summary>Tutorial: the classic rules (starter items in the bag, the rest at a workbench) with the clock stopped, no airdrops, and a guide that unlocks the game a step at a time (Tutorial.cs).</summary>
        public static bool Tutorial => Rules == GameRules.Tutorial;
        public static string RulesName(GameRules r) => r == GameRules.Arsenal ? "Arsenal" : r == GameRules.Builder ? "Builder" : r == GameRules.Fun ? "Fun"
            : r == GameRules.FunRandom ? "Fun Random" : r == GameRules.FunRandomLimited ? "Fun Random Limited" : r == GameRules.Primitive ? "Primitive" : r == GameRules.BuildingPrimitive ? "Building Primitive" : r == GameRules.AutoWood ? "Auto Wood" : r == GameRules.Tutorial ? "Tutorial" : r == GameRules.Dna ? "DNA" : "Classic";
        public static string RulesDesc(GameRules r)
        {
            switch (r)
            {
                case GameRules.Arsenal: return "Normal prices (the crossbow is cheaper), plus a POWER ITEMS menu next to crafting: sword, shotgun, revolver, C4, headshot helmet and Fortify All Walls.";
                case GameRules.Dna: return DnaDesc;
                case GameRules.Tutorial: return "New here? Start with this. Short, simple steps teach you the whole game - you do each one to go on, and each control unlocks as it's taught. The clock is stopped, friends can join any time, and it's always the small Plains map. Just press HOST GAME.";
                case GameRules.AutoWood: return "Arsenal, but wood piles up at your base by itself (5 a second) - go and pick it up.";
                case GameRules.Builder: return "No bases. Arsenal's items, but each takes a while to make. Craft and build anywhere - pieces lock onto each other. Plant the ball anywhere (E): whoever's ball it is at the end wins.";
                case GameRules.Fun: return "No building phase, a short match, and every so often everyone gets the same random item - any item in the game.";
                case GameRules.FunRandom: return "No building phase, a short match, and every so often each player gets their own random airdrop item.";
                case GameRules.FunRandomLimited: return "Fun Random, but the only things you can craft are the hatchet, spear, building plan and battering ram.";
                case GameRules.Primitive: return "The original game, but the only things you can craft are the hatchet, spear, building plan and battering ram.";
                case GameRules.BuildingPrimitive: return "Builder (no bases, build anywhere, plant the ball), but the only things you can craft are the hatchet, spear, building plan and battering ram - no power items.";
                default: return "The original game: gather, build your base, craft in it, get the ball into your machine.";
            }
        }
        /// <summary>Players needed to start (and the most that can join).</summary>
        public static int PlayersNeeded => ModeTeams(Mode) * ModeTeamSize(Mode);
        public static bool FreeForAll => Mode == GameMode.Ffa3 || Mode == GameMode.Ffa4;
        /// <summary>How many teams (bases) a mode has.</summary>
        public static int ModeTeams(GameMode m) => m == GameMode.Ffa3 || m == GameMode.Trio ? 3 : m == GameMode.Ffa4 || m == GameMode.Quad ? 4 : 2;
        /// <summary>Players per team.</summary>
        public static int ModeTeamSize(GameMode m) => m == GameMode.Teams || m == GameMode.Trio || m == GameMode.Quad ? 2 : m == GameMode.Teams3 ? 3 : m == GameMode.Teams4 ? 4 : 1;
        public static string ModeName(GameMode m)
        {
            switch (m)
            {
                case GameMode.Teams: return "2v2";
                case GameMode.Teams3: return "3v3";
                case GameMode.Teams4: return "4v4";
                case GameMode.Ffa3: return "Free for all (3)";
                case GameMode.Ffa4: return "Free for all (4)";
                case GameMode.Trio: return "2v2v2";
                case GameMode.Quad: return "2v2v2v2";
                default: return "1v1";
            }
        }
        /// <summary>Teammates stand side by side: 0 in the middle, then right, left, further right...</summary>
        /// <summary>The magazine guns: the pistol and the revolver.</summary>
        public static bool IsGun(Item i) => i == Item.Pistol || i == Item.Revolver;
        public static int GunMag(Item i) => i == Item.Revolver ? RevolverMag : PistolMag;
        public static Item GunAmmo(Item i) => i == Item.Revolver ? Item.RevolverAmmo : Item.PistolAmmo;
        public static float GunFireRate(Item i) => i == Item.Revolver ? RevolverFireRate : PistolFireRate;
        public static float GunReload(Item i) => i == Item.Revolver ? RevolverReload : PistolReload;
        public static float GunBody(Item i) => i == Item.Revolver ? RevolverBodyDamage : PistolBodyDamage;
        public static float GunHead(Item i) => i == Item.Revolver ? RevolverHeadDamage : PistolHeadDamage;
        public static float SlotOffset(int slot, float step) => slot == 0 ? 0f : ((slot + 1) / 2) * step * (slot % 2 == 1 ? 1f : -1f);
        /// <summary>More than two bases: the map is laid out four ways round with the glass walls in an X.</summary>
        public static bool FourWay => TeamCount > 2;
        public static string ModeLabel => ModeName(Mode);
        /// <summary>Packs the map/mode choice for syncing; the seed is sent separately.</summary>
        public static int MapKey => (int)Map | ((int)Size << SizeShift) | (WoodMode ? WoodBit : 0) | (AirdropSides ? SidesBit : 0) | (AirdropCenter ? CenterBit : 0) | (RespawnLoot ? RespawnLootBit : 0) | ((int)Mode << ModeShift) | ((int)Rules << RulesShift) | ((HostGraphics & GraphicsMask) << GraphicsShift);

        /// <summary>Watch towers on the Highlands map (world positions of their feet), point-mirrored between the halves.</summary>
        public static readonly List<Vector3> Towers = new List<Vector3>();

        public static void SetMap(int key, int seed)
        {
            Map = (MapKind)(key & 15);
            Size = (MapSize)((key >> SizeShift) & 3);
            if ((key & SmallBit) != 0) Size = MapSize.Small; // older saved menu choice
            WoodMode = (key & WoodBit) != 0;
            AirdropSides = (key & SidesBit) != 0;
            AirdropCenter = (key & CenterBit) != 0;
            RespawnLoot = (key & RespawnLootBit) != 0;
            Mode = (GameMode)((key >> ModeShift) & ModeMask);
            Rules = (GameRules)Mathf.Clamp((key >> RulesShift) & RulesMask, 0, (int)GameRules.Dna);
            if (Rules == GameRules.Tutorial)
            {
                // the tutorial is always the small, flat Plains map with normal materials (whatever the menu says)
                Map = MapKind.Plains;
                Size = MapSize.Small;
                WoodMode = AirdropSides = AirdropCenter = RespawnLoot = false;
            }
            HostGraphics = Mathf.Clamp((key >> GraphicsShift) & GraphicsMask, 0, 2);
            TeamCount = ModeTeams(Mode);
            MapSeed = seed;
            // on the 3 m building grid, or the grid wouldn't line up with the base area
            float d = SmallMap ? 42f : Mathf.Round(75f * SizeScale / Cell) * Cell;
            // blue south, red north, green east, yellow west
            BaseCenter[0] = new Vector3(0, 0, -d);
            BaseCenter[1] = new Vector3(0, 0, d);
            BaseCenter[2] = new Vector3(d, 0, 0);
            BaseCenter[3] = new Vector3(-d, 0, 0);
            Towers.Clear();
        }

        // ---------- Bedrock spawn + alien machine ----------
        // Every base has an unbreakable 2x2-cell silver bedrock in its middle. You spawn on it; the alien machine
        // stands on its back edge (away from the map centre). The machine crafts (E) and has the ball socket.
        public const float BedrockHalf = 3f;

        /// <summary>Direction from a base's centre to the back of its bedrock (away from the middle of the map).</summary>
        public static Vector3 BackDir(int team) => new Vector3(BaseCenter[team].x, 0, BaseCenter[team].z).normalized;
        public static Vector3 BedrockCenter(int team) => BaseCenter[team];
        // the machine stands clear of the bedrock's back edge so walls fit behind it
        public static Vector3 MachinePos(int team) => BaseCenter[team] + BackDir(team) * 2.0f + Vector3.up * BaseY;
        /// <summary>Centre of the ball when it sits in the machine's socket.</summary>
        public static Vector3 SocketPos(int team) => BaseCenter[team] + BackDir(team) * 0.35f + Vector3.up * (BaseY + 0.64f);
        /// <summary>Where you (re)spawn: on the front half of your bedrock, facing the middle of the map.</summary>
        public static Vector3 SpawnPos(int team, int slot = 0) => BaseCenter[team] - BackDir(team) * 1.7f + Vector3.Cross(Vector3.up, BackDir(team)) * SlotOffset(slot, 1.3f) + Vector3.up * (BaseY + 0.05f);

        /// <summary>Rotate a point about the map centre by 90 degrees `quarter` times (four-way symmetric layouts).</summary>
        public static Vector3 Rotate(Vector3 p, int quarter) => Quaternion.Euler(0, 90f * quarter, 0) * p;

        /// <summary>How many symmetric copies of everything the map has (2 = point mirror, 4 = four ways round).</summary>
        public static int Copies => FourWay ? 4 : 2;
        /// <summary>The k-th symmetric copy of a point laid out in the first (blue) sector.</summary>
        public static Vector3 Copy(Vector3 p, int k) => Rotate(p, k * (4 / Copies));
        /// <summary>Points are generated in blue's sector and then copied round: the south half, or the south quarter (between the X walls).</summary>
        public static bool InFirstSector(Vector3 p, float margin) => FourWay ? p.z < -margin && Mathf.Abs(p.x) < -p.z - margin : p.z < -margin;
        public static float SpawnYaw(int team) => Quaternion.LookRotation(-BackDir(team)).eulerAngles.y;

        public static bool IsBedrockCell(int i, int j)
        {
            for (int t = 0; t < TeamCount; t++)
            {
                int ci = Mathf.RoundToInt(BaseCenter[t].x / Cell), cj = Mathf.RoundToInt(BaseCenter[t].z / Cell);
                if ((i == ci - 1 || i == ci) && (j == cj - 1 || j == cj)) return true;
            }
            return false;
        }

        /// <summary>Cells covered by the bedrock can't take foundations, stairs or barriers (chests are fine).</summary>
        public static bool CellBlocked(int i, int j) => !Builder && IsBedrockCell(i, j); // Builder has no bases (no bedrock)
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
        public const float SpearMinDraw = 0.25f;
        public const float SpearPickupRange = 3f;

        // ---------- Match ----------
        [Tune("Match")] public static float BallDropDelay = 300f;   // 5 min behind the glass wall to gather and build
        [Tune("Match")] public static float MatchLength = 900f;     // + 15 min with the ball
        [Tune("Match")] public static float SuddenDeathLength = 180f;
        [Tune("Match")] public static float FastBallDropDelay = 10f;
        [Tune("Match")] public static float FastMatchLength = 90f;
        [Tune("Match")] public static float RespawnTime = 5f;
        [Tune("Match")] public static float ItemDespawnTime = 300f;

        // ---------- Airdrops (after the wall drops) ----------
        /// <summary>Mode options: how many airdrops come after the wall drops, evenly spaced (1 = half way through, 2 = at the thirds...).</summary>
        [Tune("Mode options")] public static int AirdropCount = 3;
        /// <summary>Mode options: which airdrop items are in the pool (bit i = AirdropChoices[i]).</summary>
        [Tune("Mode options")] public static int AirdropItemMask = 1023;
        [Tune("Airdrop")] public static float AirdropBaseDistance = 25f; // never this close to a base
        [Tune("Airdrop")] public static float C4Fuse = 3f, C4Radius = 5f, C4PlayerDamage = 150f, C4KillRadius = 1.6f;
        [Tune("Airdrop")] public static int SniperAmmo = 3, JetpackFuel = 100, PortalShots = 2, RocketAmmo = 3;
        [Tune("Airdrop")] public static float JetpackSeconds = 8f, JetpackThrust = 9f, GiantTime = 30f, GiantScale = 3f;
        [Tune("Airdrop")] public static float SlenderSpeed = 4.6f, SlenderHp = 150f, SlenderLife = 60f;
        [Tune("Airdrop")] public static float RocketSpeed = 32f, RocketRadius = 3.5f, RocketStructureDamage = 900f, RocketPlayerDamage = 140f;
        [Tune("Airdrop")] public static float BombBushDamage = 150f, AirstrikeRadius = 14f, AirstrikeDelay = 4f, EggBlockHp = 60f;
        [Tune("Airdrop")] public static float WandRange = 90f, WandRadius = 3f;
        [Tune("Airdrop")] public static float InvisTime = 30f, InvisRevealTime = 1.2f;
        [Tune("Airdrop")] public static int ChainsawUses = 67, AirdropResources = 1000;
        // how common each item is in airdrops (and respawn loot, Fun Random): a weight - 20 comes twice as often as 10, 0 never
        // (only among the items picked in the mode options; if every picked item is 0 they're all equally likely)
        [Tune("Airdrop rarity")] public static int RarityC4 = 10, RarityDeathWand = 10, RarityPortalGun = 10, RarityRocketLauncher = 10, RarityTreeCamo = 10,
            RarityInvisPotion = 10, RarityJetpack = 10, RarityWallhack = 10, RarityBombBush = 10, RarityEnderPearl = 10;
        [Tune("Airdrop rarity")] public static int RarityHelmet = 10, RarityArmor = 10, RarityChainsaw = 10, RarityFortTower = 10, RaritySniper = 10,
            RaritySlenderEgg = 10, RarityBuildEgg = 10, RarityGiantStaff = 10, RarityAirstrike = 10, RarityWood = 10;

        // ---------- Player ----------
        [Tune("Player")] public static float MaxHealth = 100f;
        [Tune("Player")] public static float WalkSpeed = 5f;
        [Tune("Player")] public static float SprintSpeed = 7.5f;
        [Tune("Player")] public static float CrouchSpeed = 2.6f;
        [Tune("Player")] public static float JumpSpeed = 7.2f;
        [Tune("Player")] public static float Gravity = 20f;
        [Tune("Player")] public static float BallCarrySpeedMul = 1f;
        [Tune("Player")] public static float BallThrowSpeed = 11f;
        // sliding (sprint, then crouch), like Apex / Titanfall: a boost when it starts (only once the cooldown is over, so
        // spamming it gains nothing), friction slows you on the flat, slopes speed you up going down and slow you going up
        [Tune("Player")] public static float SlideSlipperiness = 5f;  // 0 = stops almost at once, 10 = like ice (5: about a second on the flat)
        [Tune("Player")] public static float SlideBoost = 2f;         // extra speed when the slide starts
        [Tune("Player")] public static float SlideMinSpeed = 2f;      // the slide ends when you're slower than this
        [Tune("Player")] public static float SlideSteer = 70f;        // degrees per second you can turn while sliding
        [Tune("Player")] public static float SlideBoostCooldown = 1.5f; // seconds before another slide boosts you again
        [Tune("Player")] public static float SlideMaxSpeed = 11f;     // no slide goes faster than this (m/s)
        [Tune("Player")] public static float SlideSlopeAccel = 9f;    // how hard a slope pulls you along (m/s² on a 45° slope)
        [Tune("Player")] public static float SlideUphillMul = 1.8f;   // going up a slope slows you this many times harder
        /// <summary>Standing in your own base heals you slowly (HP a second).</summary>
        [Tune("Player")] public static float BaseRegen = 2f;
        /// <summary>While your team's ball is in your machine's socket (Builder: planted for your team), everything you gather gives this much more.</summary>
        [Tune("Player")] public static float BallGatherMul = 1.15f;
        [Tune("Player")] public static float BerryHeal = 25f, BerryEatTime = 1.5f, MeatEatTime = 3f;
        [Tune("Player")] public static int ArmorHp = 50;               // wooden armour: a second health bar, used up first (max 255)
        [Tune("Player")] public static float HeadshotMul = 2f;
        [Tune("Player")] public static float ModelWidth = 1.3f;       // alien model width scale
        [Tune("Player")] public static float HitboxRadius = 0.45f;
        [Tune("Player")] public static float MeleeAssist = 0.35f;     // melee counts as a hit if it passes this close (m)
        [Tune("Player")] public static float ProjectileAssist = 0.15f; // same for arrows / thrown spears

        // ---------- Melee ----------
        [Tune("Rock")] public static float RockCooldown = 0.7f, RockRange = 2.3f, RockPlayerDamage = 12f, RockWoodGather = 5f, RockStoneGather = 4f, RockStructureDamage = 6f;
        [Tune("Hatchet")] public static float HatchetCooldown = 0.7f, HatchetRange = 2.5f, HatchetPlayerDamage = 14f, HatchetWoodGather = 15f, HatchetStoneGather = 2f, HatchetStructureDamage = 12f;
        [Tune("Pickaxe")] public static float PickaxeCooldown = 0.8f, PickaxeRange = 2.5f, PickaxePlayerDamage = 14f, PickaxeWoodGather = 3f, PickaxeStoneGather = 12f, PickaxeStructureDamage = 12f;
        [Tune("Spear")] public static float SpearCooldown = 0.9f, SpearRange = 3.4f, SpearPlayerDamage = 35f, SpearWoodGather = 2f, SpearStoneGather = 1f, SpearStructureDamage = 10f;
        [Tune("Spear")] public static float SpearDrawTime = 0.6f, SpearThrowSpeed = 30f, SpearThrowDamage = 90f, SpearThrowStructureDamage = 8f;
        [Tune("Chainsaw")] public static float ChainsawCooldown = 0.15f, ChainsawRange = 2.4f, ChainsawPlayerDamage = 8f, ChainsawWoodGather = 12f, ChainsawStoneGather = 10f, ChainsawStructureDamage = 12f;
        [Tune("Melee")] public static float StoneStructureMeleeMul = 0.2f; // stone is very hard to melee - bring a ram

        // ---------- Bow ----------
        [Tune("Bow")] public static float BowDrawTime = 0.8f, ArrowSpeed = 55f, ArrowGravity = 9.81f, ArrowPlayerDamage = 50f, ArrowWoodStructureDamage = 4f;
        /// <summary>The bow fires the moment you let go, however short the draw: an instant shot flies at this share of full speed (so it drops
        /// short) and does BowMinDamage; both grow the longer you hold it, the damage slowly at first and fastest near the full draw.</summary>
        [Tune("Bow")] public static float BowMinSpeed = 0.3f, BowMinDamage = 6f;
        /// <summary>How far drawn the bow was (0..1), from how fast the arrow left it (power = speed / ArrowSpeed).</summary>
        public static float BowDrawFromPower(float power) => Mathf.Clamp01((power - BowMinSpeed) / Mathf.Max(0.01f, 1f - BowMinSpeed));
        /// <summary>Arrow damage for a shot at this power.</summary>
        public static float BowDamage(float power) { float d = BowDrawFromPower(power); return Mathf.Lerp(BowMinDamage, ArrowPlayerDamage, d * d); }
        [Tune("Crossbow")] public static float CrossbowDamage = 55f, CrossbowSpeed = 58f, CrossbowGravity = 11f, CrossbowReload = 1.6f, CrossbowZoomFov = 45f;
        [Tune("Spear")] public static float SpearGravity = 9.81f;

        // ---------- Battering ram (hand held) ----------
        // Hold LMB to wind up, then it slams whatever enemy piece you look at:
        // wooden pieces are destroyed outright, stone pieces are knocked back down to wood.
        [Tune("Ram")] public static float RamWindup = 1.5f, RamRange = 3f, RamMoveMul = 0.75f;
        [Tune("Ram")] public static int RamUses = 1;

        // ---------- Resources ----------
        [Tune("Resources")] public static int TreeAmount = 300, StoneAmount = 250, TreeFellBonus = 100;
        [Tune("Resources")] public static float WeakSpotMul = 2f, NodeRespawnTime = 60f, BushRespawnTime = 150f;

        // ---------- Building ----------
        [Tune("Building")] public static float BuildCooldown = 0f, UpgradeCooldown = 0f, DemolishRefund = 0.5f;
        [Tune("Building")] public static int FoundationWood = 15, WallWood = 15, DoorwayWood = 20, WindowWood = 15, FloorWood = 12, StairsWood = 20;
        [Tune("Building")] public static int FoundationStone = 50, WallStone = 50, DoorwayStone = 40, WindowStone = 45, FloorStone = 30, StairsStone = 30;
        [Tune("Building HP")] public static float FoundationHp = 500, WallHp = 400, DoorwayHp = 350, WindowHp = 350, FloorHp = 300, StairsHp = 300;
        [Tune("Building HP")] public static float FoundationStoneHp = 1800, WallStoneHp = 1500, DoorwayStoneHp = 1200, WindowStoneHp = 1300, FloorStoneHp = 1000, StairsStoneHp = 1000;
        [Tune("Building HP")] public static float BarrierHp = 500, ChestHp = 300, TowerHp = 800;

        // ---------- Crafting (at the alien machine) ----------
        [Tune("Crafting")] public static int PlanWood = 5;
        [Tune("Crafting")] public static int HatchetWood = 50, HatchetStone = 0, PickaxeWood = 30, PickaxeStone = 10;
        [Tune("Crafting")] public static int SpearWood = 100, SpearStone = 0, BowWood = 100, BowStone = 15;
        [Tune("Crafting")] public static int ArrowWood = 50, ArrowStone = 0, ArrowsPerCraft = 5;
        [Tune("Crafting")] public static int RamWood = 125, RamStone = 50;
        [Tune("Crafting")] public static int ChestWood = 50, BarrierWood = 40;
        [Tune("Crafting")] public static int CrossbowWood = 500, SaddleWood = 750, ArmorWood = 250, ChainsawWood = 500;
        public static int FortTowerWood = 1000; // only used for the demolish refund (the fort is an airdrop item now)

        // ---------- Vehicles ----------
        [Tune("Vehicles")] public static float CarSpeed = 17f, CarReverseSpeed = 6f, CarAccel = 9f, CarTurn = 95f, CarHitDamage = 30f, CarKnockback = 11f;
        [Tune("Vehicles")] public static float HorseWalk = 4.5f, HorseSprint = 11f, HorseJump = 8.5f, HorseHp = 60f;
        [Tune("Vehicles")] public static int HorsesPerSide = 3;

        // ---------- Game modes ----------
        /// <summary>Arsenal / Builder: normal prices, except the crossbow is cheaper.</summary>
        [Tune("Arsenal and Builder")] public static int ModesCrossbowWood = 350;
        /// <summary>Builder: the ball always has a flag pointing at the sky (on), or only grows one while it's planted (off).</summary>
        [Tune("Arsenal and Builder")] public static bool BuilderFlagAlwaysUp = true;
        [Tune("Arsenal and Builder")] public static int FortifyWood = 5000, PistolWood = 5000;
        /// <summary>Fortify All Walls goes up a step every time your team buys it: stone, then metal, then refined.</summary>
        [Tune("Arsenal and Builder")] public static int FortifyStoneWood = 1000, FortifyMetalWood = 2000, FortifyRefinedWood = 2500;
        [Tune("Arsenal and Builder")] public static int SwordWood = 500, C4Wood = 2500, HelmetWood = 800, ShotgunWood = 2000, ShellWood = 250, RevolverWood = 2500, RevolverAmmoWood = 200;
        /// <summary>Sword: a slow, heavy swing (the swing time is adjustable) - its own head / body damage instead of the usual x2.</summary>
        [Tune("Arsenal and Builder")] public static float SwordSwingTime = 1.25f, SwordHeadDamage = 150f, SwordBodyDamage = 95f, SwordRange = 2.9f;
        /// <summary>Waterpipe shotgun: one shell at a time. Each pellet does full damage within PointBlank metres, falling off to FarMul at Range.</summary>
        [Tune("Arsenal and Builder")] public static int ShotgunPellets = 10;
        [Tune("Arsenal and Builder")] public static float ShotgunPelletDamage = 20f, ShotgunSpread = 5f, ShotgunPointBlank = 1f, ShotgunRange = 25f, ShotgunFarMul = 0.15f, ShotgunReload = 2.4f;
        /// <summary>Shotgun pellets that hit the head do this many times their damage.</summary>
        [Tune("Arsenal and Builder")] public static float ShotgunHeadMul = 1.5f;
        /// <summary>Shotgun pellet damage share at this distance: full up close, down to FarMul at the range.</summary>
        public static float ShotgunFalloff(float dist) => dist <= ShotgunPointBlank ? 1f : dist >= ShotgunRange ? 0f : Mathf.Lerp(1f, ShotgunFarMul, (dist - ShotgunPointBlank) / Mathf.Max(0.1f, ShotgunRange - ShotgunPointBlank));
        /// <summary>Revolver: 6 rounds, its own head / body damage.</summary>
        [Tune("Arsenal and Builder")] public static int RevolverMag = 3;
        [Tune("Arsenal and Builder")] public static float RevolverBodyDamage = 30f, RevolverHeadDamage = 50f, RevolverFireRate = 0.3f, RevolverReload = 2f;
        /// <summary>Auto Wood: wood added to the pile at every base each second.</summary>
        [Tune("Auto Wood")] public static int AutoWoodPerSecond = 5;
        /// <summary>Auto Wood: the wood gen upgrade's three levels - wood a second at each, and what each costs.</summary>
        /// <summary>The wood gen upgrade has two levels: wood a second at each, and what each costs.</summary>
        [Tune("Auto Wood")] public static int AutoWoodLevel1 = 12, AutoWoodLevel2 = 25;
        [Tune("Auto Wood")] public static int WoodGen1Wood = 1000, WoodGen2Wood = 3000;
        /// <summary>Pistol: hitscan, this much damage a shot (a headshot has its own number instead of the usual x2).</summary>
        [Tune("Arsenal and Builder")] public static float PistolHeadDamage = 200f, PistolBodyDamage = 95f;
        /// <summary>Metal (Fortify All Walls): this many times the stone HP; melee does this share of its damage.</summary>
        [Tune("Building HP")] public static float MetalHpMul = 2f, MetalMeleeMul = 0.1f;
        /// <summary>Refined (the third fortify): this many times the stone HP; melee does this share of its damage.</summary>
        [Tune("Building HP")] public static float RefinedHpMul = 3f, RefinedMeleeMul = 0.05f;
        /// <summary>Rockets: the share of their damage a piece of this tier takes (sheet metal and refined shrug most of it off).</summary>
        [Tune("Building HP")] public static float MetalRocketMul = 0.45f, RefinedRocketMul = 0.2f;
        public static float TierBlastMul(int tier) => tier >= 3 ? RefinedRocketMul : tier == 2 ? MetalRocketMul : 1f;
        /// <summary>The share of melee damage a piece of this tier takes.</summary>
        public static float TierMeleeMul(int tier) => tier >= 3 ? RefinedMeleeMul : tier == 2 ? MetalMeleeMul : tier == 1 ? StoneStructureMeleeMul : 1f;
        [Tune("Arsenal and Builder")] public static float PistolFireRate = 0.22f, PistolReload = 1.3f;
        [Tune("Arsenal and Builder")] public static int PistolMag = 5, HeavyArmorHp = 200, TreeCrackerUses = 40;
        /// <summary>Builder: every craft takes a while (seconds per 100 wood of its price, between the min and max).</summary>
        [Tune("Arsenal and Builder")] public static float BuilderCraftSecsPer100 = 0.6f, BuilderCraftMin = 2f, BuilderCraftMax = 20f;
        /// <summary>Test (main menu > Testing, any mode): enemies have a faint glow in their team colour so they're easier to see.</summary>
        [Tune("Test")] public static bool AlienOutlines = true;
        /// <summary>How strong (0 = invisible, 1 = bright) and how thick (metres) the glow is.</summary>
        [Tune("Test")] public static float AlienOutlineStrength = 0.3f, AlienOutlineWidth = 0.03f;
        [Tune("Fun modes")] public static float FunItemInterval = 45f;
        /// <summary>Fun modes: no building phase (the wall is down and the ball in from the start), and a shorter match.</summary>
        [Tune("Fun modes")] public static float FunMatchLength = 600f;
        [Tune("Airdrop")] public static float EnderPearlSpeed = 24f;

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
                case Item.Barrier: return "High External Wall";
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
                case Item.EnderPearl: return "Ender Pearl";
                case Item.Pistol: return "Pistol";
                case Item.PistolAmmo: return "Pistol Ammo";
                case Item.HeavyArmor: return "Heavy Armour";
                case Item.TreeCracker: return "Tree Cracker";
                case Item.FortifyBuff: return "Fortify All Walls";
                case Item.WoodGenBuff: return "Auto Wood Gen";
                case Item.Boat: return "Wooden Boat";
                case Item.Sword: return "Sword";
                case Item.Shotgun: return "Waterpipe Shotgun";
                case Item.ShotgunShell: return "Shotgun Shell";
                case Item.Revolver: return "Revolver";
                case Item.RevolverAmmo: return "Revolver Bullet";
                case Item.Dna: return "DNA";
                case Item.Workbench: return "Workbench";
                case Item.None: return "";
                default: return i.ToString();
            }
        }

        public static int MaxStack(Item i)
        {
            switch (i)
            {
                case Item.Wood: case Item.Stone: case Item.Dna: return 1000;
                case Item.Arrow: return 64;
                case Item.PistolAmmo: return 120;
                case Item.ShotgunShell: return 64;
                case Item.RevolverAmmo: return 120;
                case Item.EnderPearl: return 4;
                case Item.Berry: return 20;
                case Item.Barrier: return 5;
                case Item.None: return 0;
                default: return 1;
            }
        }

        /// <summary>Materials: they stack onto what you already have, otherwise fill the hotbar from slot 7 backwards.</summary>
        public static bool IsMat(Item i) => i == Item.Wood || i == Item.Stone || i == Item.Dna || i == Item.Arrow || i == Item.ShotgunShell || i == Item.RevolverAmmo;

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
                case Item.RocketLauncher: return RocketAmmo;
                case Item.Pistol: return PistolMag;
                case Item.Revolver: return RevolverMag;
                case Item.TreeCracker: return TreeCrackerUses;
                default: return 0;
            }
        }

        /// <summary>The airdrop items the host can pick from in the mode options (the others are unused for now).</summary>
        public static readonly Item[] AirdropChoices =
        {
            Item.C4, Item.DeathWand, Item.PortalGun, Item.RocketLauncher, Item.TreeCamo, Item.InvisPotion, Item.Jetpack, Item.Wallhack, Item.BombBush, Item.EnderPearl,
        };

        /// <summary>Fun Random: every airdrop item there is, the unused ones too.</summary>
        public static readonly Item[] AllAirdropItems =
        {
            Item.C4, Item.DeathWand, Item.PortalGun, Item.RocketLauncher, Item.TreeCamo, Item.InvisPotion, Item.Jetpack, Item.Wallhack, Item.BombBush, Item.EnderPearl,
            Item.Helmet, Item.Armor, Item.Chainsaw, Item.FortTower, Item.Sniper, Item.SlenderEgg, Item.BuildEgg, Item.GiantStaff, Item.Airstrike, Item.Wood,
        };

        /// <summary>Fun: every item in the game (tools, weapons, loot, the unused ones) - not the rock, materials or buffs.</summary>
        public static List<Item> AllFunItems
        {
            get
            {
                var l = new List<Item>();
                foreach (Item i in Enum.GetValues(typeof(Item)))
                {
                    if (i == Item.None || i == Item.Rock || i == Item.FortifyBuff || i == Item.WoodGenBuff || i == Item.AirdropSignal || i == Item.Workbench) continue;
                    if (i == Item.Boat && !ThemeMaps.HasWater) continue; // THEME MAPS
                    l.Add(i);
                }
                return l;
            }
        }

        /// <summary>A stack of an item as it's handed out (full durability, a sensible amount of materials / ammo).</summary>
        public static ItemStack GiftStack(Item id)
        {
            switch (id)
            {
                case Item.Wood: return DnaSwap(ItemStack.Of(WoodMode || UnityEngine.Random.value < 0.5f ? Item.Wood : Item.Stone, Mathf.Clamp(AirdropResources, 1, 1000)));
                case Item.Stone: return DnaSwap(ItemStack.Of(WoodMode ? Item.Wood : Item.Stone, 300));
                case Item.Arrow: return ItemStack.Of(Item.Arrow, 20);
                case Item.Berry: return ItemStack.Of(Item.Berry, 5);
                case Item.PistolAmmo: return ItemStack.Of(Item.PistolAmmo, 30);
                case Item.ShotgunShell: return ItemStack.Of(Item.ShotgunShell, 8);
                case Item.RevolverAmmo: return ItemStack.Of(Item.RevolverAmmo, 18);
                case Item.Shotgun: return ItemStack.Of(Item.Shotgun, 1, 0);   // guns come empty: buy the ammo
                case Item.Revolver: return ItemStack.Of(Item.Revolver, 1, 0);
                case Item.Helmet: return ItemStack.Of(Item.Helmet, 1, 1);
                case Item.Saddle: return ItemStack.Of(Item.Saddle, 1, 0);
                case Item.Crossbow: return ItemStack.Of(Item.Crossbow, 1, 1);
                default: return ItemStack.Of(id, 1, Mathf.Clamp(MaxData(id), 0, 255));
            }
        }

        /// <summary>An airdrop item's rarity weight (CHANGE VALUES > Airdrop rarity): higher = more common, 0 = never.</summary>
        public static int AirdropRarity(Item i)
        {
            switch (i)
            {
                case Item.C4: return RarityC4;
                case Item.DeathWand: return RarityDeathWand;
                case Item.PortalGun: return RarityPortalGun;
                case Item.RocketLauncher: return RarityRocketLauncher;
                case Item.TreeCamo: return RarityTreeCamo;
                case Item.InvisPotion: return RarityInvisPotion;
                case Item.Jetpack: return RarityJetpack;
                case Item.Wallhack: return RarityWallhack;
                case Item.BombBush: return RarityBombBush;
                case Item.EnderPearl: return RarityEnderPearl;
                case Item.Helmet: return RarityHelmet;
                case Item.Armor: return RarityArmor;
                case Item.Chainsaw: return RarityChainsaw;
                case Item.FortTower: return RarityFortTower;
                case Item.Sniper: return RaritySniper;
                case Item.SlenderEgg: return RaritySlenderEgg;
                case Item.BuildEgg: return RarityBuildEgg;
                case Item.GiantStaff: return RarityGiantStaff;
                case Item.Airstrike: return RarityAirstrike;
                case Item.Wood: return RarityWood;
                default: return 10;
            }
        }

        /// <summary>A random airdrop item out of `pool`, weighted by its rarity (all equally likely if they're all 0).</summary>
        public static Item PickAirdropItem(IList<Item> pool)
        {
            if (pool == null || pool.Count == 0) return Item.C4;
            int total = 0;
            foreach (var i in pool) total += Mathf.Max(0, AirdropRarity(i));
            if (total <= 0) return pool[UnityEngine.Random.Range(0, pool.Count)];
            int r = UnityEngine.Random.Range(0, total);
            foreach (var i in pool)
            {
                r -= Mathf.Max(0, AirdropRarity(i));
                if (r < 0) return i;
            }
            return pool[pool.Count - 1];
        }

        /// <summary>Everything an airdrop (or the respawn-loot option) can give you: the picked items (all of them if none are picked).</summary>
        public static List<Item> AirdropLoot
        {
            get
            {
                var l = new List<Item>();
                for (int i = 0; i < AirdropChoices.Length; i++) if ((AirdropItemMask & (1 << i)) != 0) l.Add(AirdropChoices[i]);
                if (l.Count == 0) l.AddRange(AirdropChoices);
                return l;
            }
        }

        public static MeleeStats Melee(Item i)
        {
            switch (i)
            {
                case Item.Rock:    return new MeleeStats { Cooldown = RockCooldown, Range = RockRange, PlayerDamage = RockPlayerDamage, WoodGather = RockWoodGather, StoneGather = RockStoneGather, StructureDamage = RockStructureDamage };
                case Item.Hatchet: return new MeleeStats { Cooldown = HatchetCooldown, Range = HatchetRange, PlayerDamage = HatchetPlayerDamage, WoodGather = HatchetWoodGather, StoneGather = HatchetStoneGather, StructureDamage = HatchetStructureDamage };
                case Item.Pickaxe: return new MeleeStats { Cooldown = PickaxeCooldown, Range = PickaxeRange, PlayerDamage = PickaxePlayerDamage, WoodGather = PickaxeWoodGather, StoneGather = PickaxeStoneGather, StructureDamage = PickaxeStructureDamage };
                case Item.Spear:   return new MeleeStats { Cooldown = SpearCooldown, Range = SpearRange, PlayerDamage = SpearPlayerDamage, WoodGather = SpearWoodGather, StoneGather = SpearStoneGather, StructureDamage = SpearStructureDamage };
                case Item.Chainsaw: return new MeleeStats { Cooldown = ChainsawCooldown, Range = ChainsawRange, PlayerDamage = ChainsawPlayerDamage, WoodGather = ChainsawWoodGather, StoneGather = ChainsawStoneGather, StructureDamage = ChainsawStructureDamage };
                // a huge axe: slow, fells any tree in one hit (the server gives the whole tree)
                case Item.TreeCracker: return new MeleeStats { Cooldown = 1.1f, Range = 2.8f, PlayerDamage = 30f, WoodGather = 9999f, StoneGather = 3f, StructureDamage = 20f };
                // a heavy sword: a slow swing, big hits (its head and body damage are its own numbers)
                case Item.Sword: return new MeleeStats { Cooldown = Mathf.Max(0.2f, SwordSwingTime), Range = SwordRange, PlayerDamage = SwordBodyDamage, WoodGather = 4f, StoneGather = 1f, StructureDamage = 15f };
                default: return default;
            }
        }
        public static bool IsMelee(Item i) => i == Item.Rock || i == Item.Hatchet || i == Item.Pickaxe || i == Item.Spear || i == Item.Chainsaw || i == Item.TreeCracker || i == Item.Sword;
        /// <summary>Melee damage to a player: the sword has its own headshot number, everything else does x2 to the head.</summary>
        public static float MeleePlayerDamage(Item i, bool head) => i == Item.Sword ? (head ? SwordHeadDamage : SwordBodyDamage) : Melee(i).PlayerDamage * (head ? HeadshotMul : 1f);

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
            if (tier >= 3) return PieceHp(t, 1) * RefinedHpMul; // refined
            if (tier >= 2) return PieceHp(t, 1) * MetalHpMul; // metal
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
        static readonly Item[] k_Recipes = { Item.Hatchet, Item.Pickaxe, Item.Spear, Item.BuildingPlan, Item.Chest, Item.Bow, Item.Arrow, Item.Crossbow, Item.Armor, Item.Chainsaw, Item.Ram, Item.Barrier, Item.Saddle, Item.Workbench };

        static readonly Item[] k_Limited = { Item.Hatchet, Item.Spear, Item.BuildingPlan, Item.Ram };
        static readonly List<Item> s_Active = new List<Item>();

        /// <summary>What can be crafted in this mode, in menu order (wood mode has no pickaxe; Primitive / Fun Random Limited only the basics).</summary>
        static List<Item> ActiveRecipes()
        {
            s_Active.Clear();
            if (LimitedCrafting) { s_Active.AddRange(k_Limited); return s_Active; }
            foreach (var it in k_Recipes) if (!(WoodMode && it == Item.Pickaxe)) s_Active.Add(it);
            if (ThemeMaps.HasWater) s_Active.Add(Item.Boat); // THEME MAPS (the boat)
            return s_Active;
        }

        public static int RecipeCount => ActiveRecipes().Count;

        public static Recipe GetRecipe(int i)
        {
            var list = ActiveRecipes();
            var id = list[Mathf.Clamp(i, 0, list.Count - 1)];
            if (id == Item.Boat) return DnaPriced(ThemeMaps.BoatRecipe); // THEME MAPS
            Recipe r;
            switch (id)
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
                case Item.Armor: r = new Recipe { Output = Item.Armor, Count = 1, Wood = ArmorWood }; break;
                case Item.Chainsaw: r = new Recipe { Output = Item.Chainsaw, Count = 1, Wood = ChainsawWood }; break;
                case Item.Workbench: r = new Recipe { Output = Item.Workbench, Count = 1, Wood = WorkbenchWood }; break;
                default: r = new Recipe { Output = Item.Barrier, Count = 1, Wood = BarrierWood }; break;
            }
            if (WoodMode) { r.Wood += r.Stone; r.Stone = 0; } // everything costs wood only
            return DnaPriced(Priced(r)); // DNA mode: the price in DNA
        }
        /// <summary>A recipe's price in this game mode (Arsenal / Builder: the crossbow is cheaper).</summary>
        public static Recipe Priced(Recipe r)
        {
            if (PowerMenu && r.Output == Item.Crossbow) r.Wood = ModesCrossbowWood;
            return r;
        }

        // ---------- Arsenal / Builder / Auto Wood: the powerful items menu ----------
        static readonly Item[] k_PowerBase = { Item.Sword, Item.Shotgun, Item.ShotgunShell, Item.Revolver, Item.RevolverAmmo, Item.C4, Item.Helmet, Item.FortifyBuff };
        static readonly Item[] k_PowerAutoWood = { Item.Sword, Item.Shotgun, Item.ShotgunShell, Item.Revolver, Item.RevolverAmmo, Item.C4, Item.Helmet, Item.FortifyBuff, Item.WoodGenBuff };
        /// <summary>Auto Wood also sells the wood gen upgrade.</summary>
        static Item[] k_Power => AutoWood ? k_PowerAutoWood : k_PowerBase;
        /// <summary>Auto Wood: how many times a team has upgraded its wood gen (0-3), synced by NetGame.</summary>
        public static int WoodGenLevel(int team) => NetGame.Instance != null && team >= 0 && team < 4 ? NetGame.Instance.WoodGenLevelOf(team) : 0;
        public const int MaxWoodGen = 2;
        /// <summary>Wood a second at each wood gen level.</summary>
        public static int WoodGenRate(int level) => level >= 2 ? AutoWoodLevel2 : level == 1 ? AutoWoodLevel1 : AutoWoodPerSecond;
        /// <summary>Auto Wood: the wood machine stands on the bedrock to the right of the alien machine (as you look at it from your spawn).</summary>
        public static Vector3 WoodMachinePos(int team) => MachinePos(team) + Quaternion.LookRotation(-BackDir(team)) * new Vector3(-2.35f, 0, 0.1f);
        /// <summary>Where the wood machine's pile of wood comes out: on the bedrock in front of its chute.</summary>
        public static Vector3 WoodTrayPos(int team) => WoodMachinePos(team) + Quaternion.LookRotation(-BackDir(team)) * new Vector3(0, 0.3f, 0.95f);
        /// <summary>Power recipes are numbered from here in CraftRpc.</summary>
        public const int PowerBase = 100;
        /// <summary>Where an item is in the power menu (-1 if it isn't there).</summary>
        public static int PowerIndex(Item id) => System.Array.IndexOf(k_Power, id);
        public static int PowerCount => PowerMenu ? k_Power.Length : 0;
        /// <summary>How many times a team has bought Fortify All Walls (0 never, 1 stone, 2 metal, 3 refined), synced by NetGame.</summary>
        public static int FortifyLevel(int team) => NetGame.Instance != null && team >= 0 && team < 4 ? NetGame.Instance.FortifyLevelOf(team) : 0;
        public const int MaxFortify = 3;
        public static string TierName(int tier) => tier >= 3 ? "Refined" : tier == 2 ? "Metal" : tier == 1 ? "Stone" : "Wooden";

        /// <summary>A power item's price. Fortify costs more each time the team buys it (team -1: the first step).</summary>
        public static Recipe GetPowerRecipe(int i, int team = -1)
        {
            var id = k_Power[Mathf.Clamp(i, 0, k_Power.Length - 1)];
            switch (id)
            {
                case Item.Sword: return new Recipe { Output = id, Count = 1, Wood = SwordWood };
                case Item.Shotgun: return new Recipe { Output = id, Count = 1, Wood = ShotgunWood };
                case Item.ShotgunShell: return new Recipe { Output = id, Count = 1, Wood = ShellWood };
                case Item.Revolver: return new Recipe { Output = id, Count = 1, Wood = RevolverWood };
                case Item.RevolverAmmo: return new Recipe { Output = id, Count = 1, Wood = RevolverAmmoWood };
                case Item.C4: return new Recipe { Output = id, Count = 1, Wood = C4Wood };
                case Item.Helmet: return new Recipe { Output = id, Count = 1, Wood = HelmetWood };
                case Item.WoodGenBuff:
                {
                    int lvl = WoodGenLevel(team);
                    return new Recipe { Output = id, Count = 1, Wood = lvl >= 1 ? WoodGen2Wood : WoodGen1Wood };
                }
                default:
                {
                    int lvl = FortifyLevel(team);
                    return new Recipe { Output = Item.FortifyBuff, Count = 1, Wood = lvl >= 2 ? FortifyRefinedWood : lvl == 1 ? FortifyMetalWood : FortifyStoneWood };
                }
            }
        }

        /// <summary>A power item's one-line description in the menu.</summary>
        public static string PowerBlurb(Item id, int team = -1)
        {
            switch (id)
            {
                case Item.Sword: return $"slow heavy swing: {SwordBodyDamage:0} body / {SwordHeadDamage:0} head";
                case Item.Shotgun: return $"one shell at a time, {ShotgunPellets * ShotgunPelletDamage:0} up close";
                case Item.ShotgunShell: return "one shell for the shotgun";
                case Item.Revolver: return $"{RevolverMag} rounds, {RevolverBodyDamage:0} body / {RevolverHeadDamage:0} head";
                case Item.RevolverAmmo: return "one bullet for the revolver";
                case Item.C4: return "thrown: wrecks every building piece nearby";
                case Item.Helmet: return "put it on: stops one headshot completely";
                case Item.WoodGenBuff:
                {
                    int lvl = WoodGenLevel(team);
                    if (lvl >= MaxWoodGen) return $"maxed out: {WoodGenRate(lvl)} wood a second";
                    return $"level {lvl + 1}: your base makes {WoodGenRate(lvl + 1)} wood a second (now {WoodGenRate(lvl)})";
                }
                default:
                {
                    int lvl = FortifyLevel(team);
                    if (lvl >= MaxFortify) return "your pieces are all refined - fully fortified";
                    return $"all your team's pieces to {TierName(lvl + 1).ToLower()} ({lvl + 2} ram hits each)";
                }
            }
        }

        /// <summary>Builder: how long a queued item will take (from its recipe).</summary>
        public static float CraftSecondsOf(Item id)
        {
            for (int i = 0; i < PowerCount; i++) if (GetPowerRecipe(i).Output == id) return CraftSeconds(GetPowerRecipe(i));
            int k = RecipeIndex(id);
            return k >= 0 ? CraftSeconds(GetRecipe(k)) : 0f;
        }

        /// <summary>Builder: how long an item takes to make (by its price).</summary>
        public static float CraftSeconds(Recipe r) => !Builder || r.Output == Item.BuildingPlan || r.Output == Item.FortifyBuff || r.Output == Item.WoodGenBuff || r.Output == Item.ShotgunShell || r.Output == Item.RevolverAmmo ? 0f : Mathf.Clamp((r.Wood + r.Stone) / 100f * BuilderCraftSecsPer100, BuilderCraftMin, BuilderCraftMax);

        public static int RecipeIndex(Item output)
        {
            for (int i = 0; i < RecipeCount; i++) if (GetRecipe(i).Output == output) return i;
            return -1;
        }

        // ---------- Areas ----------
        public static int BaseTeamAt(Vector3 p)
        {
            for (int t = 0; t < TeamCount; t++)
                if (Mathf.Abs(p.x - BaseCenter[t].x) <= BaseHalf && Mathf.Abs(p.z - BaseCenter[t].z) <= BaseHalf)
                    return t;
            return -1;
        }

        /// <summary>Spears and hatchets can be crafted anywhere; everything else only inside your own base.</summary>
        public static bool CraftAnywhere(Item i) => i == Item.Spear || i == Item.Hatchet;
        public static bool CanCraftAt(int team, Vector3 p) => Builder || BaseTeamAt(p) == team;
        public static bool CanCraftAt(int team, Vector3 p, Item i) => CraftAnywhere(i) || CanCraftAt(team, p);

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

        public static string FormatDefault(FieldInfo f)
        {
            _ = TuneFields;
            var v = s_Defaults[f.Name];
            return v is float fl ? fl.ToString("0.###", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool TrySet(FieldInfo f, string text)
        {
            if (f.FieldType == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv)) { f.SetValue(null, fv); return true; }
            if (f.FieldType == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv)) { f.SetValue(null, iv); return true; }
            if (f.FieldType == typeof(bool))
            {
                string t = text.Trim().ToLowerInvariant();
                if (t == "true" || t == "on" || t == "1" || t == "yes") { f.SetValue(null, true); return true; }
                if (t == "false" || t == "off" || t == "0" || t == "no") { f.SetValue(null, false); return true; }
            }
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

        /// <summary>A client takes the host's settings: the defaults plus whatever the host changed.</summary>
        public static void ApplyHost(string data)
        {
            ResetDefaults();
            Apply(data);
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
            MigratePrefs();
            Apply(PlayerPrefs.GetString(PrefsKey, ""));
        }

        /// <summary>Values whose defaults were changed on request: a value saved on this PC before that is dropped once, so the new default is used.</summary>
        static readonly (int version, string[] names)[] k_Reset =
        {
            (1, new[] { "SpearWood", "ArmorWood", "ArmorHp", "SaddleWood", "RamUses", "CrossbowSpeed", "DropWarning" }),
            (2, new[] { "SlideSlipperiness", "SlideBoost", "SlideMaxSpeed", "SlideSlopeAccel", "AlienOutlines", "RevolverMag", "BowMinSpeed", "BowMinDamage" }),
            (3, new[] { "RocketPlayerDamage", "HorseHp", "WoodGen1Wood", "WoodGen2Wood", "AutoWoodLevel1", "AutoWoodLevel2", "BaseRegen" }),
            (4, new[] { "SpearThrowDamage" }),
            (5, new[] { "BallGatherMul" }),
        };
        const string MigrateKey = "RockGame.Tunables.migrated";

        static void MigratePrefs()
        {
            int done = PlayerPrefs.GetInt(MigrateKey, 0), latest = done;
            string saved = PlayerPrefs.GetString(PrefsKey, "");
            var drop = new HashSet<string>();
            foreach (var (version, names) in k_Reset)
                if (version > done) { foreach (var n in names) drop.Add(n); latest = Mathf.Max(latest, version); }
            if (latest == done) return;
            var sb = new StringBuilder();
            foreach (var pair in saved.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0 || drop.Contains(pair.Substring(0, eq))) continue;
                sb.Append(pair).Append(';');
            }
            PlayerPrefs.SetString(PrefsKey, sb.ToString());
            PlayerPrefs.SetInt(MigrateKey, latest);
            PlayerPrefs.Save();
        }
    }
}
