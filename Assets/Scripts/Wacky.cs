using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The wacky airdrop items: their names, descriptions, numbers and the airdrop picks for them. What each one does is in
    /// PlayerNet.Wacky.cs (the server), PlayerController.Wacky.cs (your controls, camera and moving), NetGame.Wacky.cs (the
    /// things they leave in the world and the minigames), Vehicle.Wacky.cs (the decoy, the blue shell, the turret bot and
    /// the drawn monster), PlayerNet.WackyLooks.cs (how a player looks under them), Gadgets.cs, WackyModels.cs and
    /// Hud.Wacky.cs (the minigames, the drawing canvas, the map, the notebook).
    /// </summary>
    public static partial class Cfg
    {
        /// <summary>Every wacky airdrop item (they're also on the end of AirdropChoices and AllAirdropItems).</summary>
        public static readonly Item[] WackyItems =
        {
            Item.BouncyCastle, Item.Decoy, Item.OmenMap, Item.GoldenRock, Item.BlueShell, Item.Connect4Wand, Item.JumpBoots, Item.StickOfTruth, Item.SuicideBomb, Item.GravityStaff,
            Item.AttentionSnail, Item.InvSwapper, Item.TimeoutBall, Item.KnockStick1, Item.KnockStick, Item.ShieldPotion, Item.SlowGun, Item.TreeBreaker, Item.DrawMonster, Item.BearJuice,
            Item.ChaosSigil, Item.DroneBot, Item.GameshowWand, Item.DanceBomb, Item.DecoyBase, Item.BaseRebuild, Item.FakeFlare, Item.DisguiseKit, Item.BallGrabber, Item.Scribble,
            Item.AntGun, Item.ClutterBomb,
        };

        public static bool IsWacky(Item i) => i >= Item.BouncyCastle && i <= Item.ClutterBomb;

        // ---------- the numbers (CHANGE VALUES > Wacky items) ----------
        [Tune("Wacky items")] public static float ShieldTime = 10f, SlowTime = 6f, SlowMul = 0.35f, SlowRadius = 4.5f, AntTime = 7f, PrisonTime = 10f;
        /// <summary>An ant: how fast it walks and how high it jumps next to an alien (both way down).</summary>
        [Tune("Wacky items")] public static float AntSpeedMul = 0.3f, AntJumpMul = 0.35f;
        [Tune("Wacky items")] public static float DanceTime = 4f, DanceRadius = 9f, KnockDownTime = 2.5f, DecoyLife = 20f, DecoySpeed = 6.5f;
        [Tune("Wacky items")] public static float CastleLife = 180f, CastleBounce = 17f, SnailTime = 15f, SnailShortTime = 5f;
        [Tune("Wacky items")] public static float TruthRadius = 7f, TruthLaunch = 55f, SuicideRadius = 6f, SuicideFuse = 1.2f;
        /// <summary>The lifting stick: how high a building / chest / tree / horse goes (10x what it was - fast, and never
        /// through the map's glass dome: NetGame.LiftTop), how long it hangs up there before it drops back into its place,
        /// and how hard a player is thrown up (10x as high as it was, up to just under the glass).</summary>
        [Tune("Wacky items")] public static float LiftHeight = 90f, LiftTime = 10f, LiftLaunch = 101f;
        [Tune("Wacky items")] public static float BearHp = 300f, BearClawDamage = 45f, BearClawStructure = 220f, BearSpeedMul = 0.375f, BearTime = 120f, BearSplash = 3.5f;
        [Tune("Wacky items")] public static float DroneSpeed = 7f, DroneBlastRadius = 4.5f, DroneHp = 40f, BallGrabRange = 120f, BallGrabSpeed = 18f;
        [Tune("Wacky items")] public static float ClutterTime = 12f, ClutterRadius = 10f, MinigameClock = 60f, GameshowAnswerTime = 20f, ChaosGlowTime = 120f;
        [Tune("Wacky items")] public static float KnockStickPush = 18f, KnockStick1Push = 62f, RebuildWindow = 20f, GlitterTime = 6f;
        [Tune("Wacky items")] public static float MonsterLife = 30f, MonsterSpeed = 7f, MonsterSplatTime = 4f, DrawTime = 20f, ShellSpeed = 26f;
        /// <summary>The triple jump boots: jumps in the air (before you land again). The scribble notebook's lightning
        /// ("kill", "smite", "thor"...): its damage to the nearest enemy.</summary>
        [Tune("Wacky items")] public static int BootsAirJumps = 2;
        [Tune("Wacky items")] public static float ScribbleSmite = 60f;
        [Tune("Wacky items")] public static int StickOfTruthShots = 2, SlowGunShots = 3, AntGunShots = 2, TreeBreakerUses = 10, TreeBreakerWood = 300;

        // how common each one is in airdrops (CHANGE VALUES > Airdrop rarity, like the others)
        [Tune("Airdrop rarity")] public static int RarityBouncyCastle = 10, RarityDecoy = 10, RarityOmenMap = 10, RarityGoldenRock = 10, RarityBlueShell = 10,
            RarityConnect4Wand = 10, RarityJumpBoots = 10, RarityStickOfTruth = 10, RaritySuicideBomb = 10, RarityGravityStaff = 10, RarityAttentionSnail = 10;
        [Tune("Airdrop rarity")] public static int RarityInvSwapper = 10, RarityTimeoutBall = 10, RarityKnockStick1 = 10, RarityKnockStick = 10, RarityShieldPotion = 10,
            RaritySlowGun = 10, RarityTreeBreaker = 10, RarityDrawMonster = 10, RarityBearJuice = 10, RarityChaosSigil = 10, RarityDroneBot = 10;
        [Tune("Airdrop rarity")] public static int RarityGameshowWand = 10, RarityDanceBomb = 10, RarityDecoyBase = 10, RarityBaseRebuild = 10, RarityFakeFlare = 10,
            RarityDisguiseKit = 10, RarityBallGrabber = 10, RarityScribble = 10, RarityAntGun = 10, RarityClutterBomb = 10;

        /// <summary>Mode options: which of the airdrop choices past the 32nd are picked (bit i - 32 = AirdropChoices[i]):
        /// just the Scribblenauts notebook (40) to start with (AirdropItemMask).</summary>
        [Tune("Mode options")] public static int AirdropItemMask2 = 1 << 8;

        /// <summary>Is airdrop choice i picked in the mode options (either mask)?</summary>
        public static bool AirdropPicked(int i) => i < 32 ? (AirdropItemMask & (1 << i)) != 0 : (AirdropItemMask2 & (1 << (i - 32))) != 0;

        public static void SetAirdropPicked(int i, bool on)
        {
            if (i < 32) AirdropItemMask = on ? AirdropItemMask | (1 << i) : AirdropItemMask & ~(1 << i);
            else AirdropItemMask2 = on ? AirdropItemMask2 | (1 << (i - 32)) : AirdropItemMask2 & ~(1 << (i - 32));
        }

        /// <summary>Pick every airdrop choice, or none.</summary>
        public static void PickAllAirdrops(bool on) { AirdropItemMask = on ? -1 : 0; AirdropItemMask2 = on ? -1 : 0; }

        /// <summary>Pick just this one airdrop choice.</summary>
        public static void PickOnlyAirdrop(Item only)
        {
            PickAllAirdrops(false);
            int i = System.Array.IndexOf(AirdropChoices, only);
            if (i >= 0) SetAirdropPicked(i, true);
        }

        public static bool AnyAirdropPicked()
        {
            for (int i = 0; i < AirdropChoices.Length; i++) if (AirdropPicked(i)) return true;
            return false;
        }

        static readonly Dictionary<Item, System.Reflection.FieldInfo> s_RarityFields = new Dictionary<Item, System.Reflection.FieldInfo>();

        /// <summary>The rarity weight of an item with no case of its own in AirdropRarity: its Rarity... field, if it has one.</summary>
        static int WackyRarity(Item i)
        {
            if (!s_RarityFields.TryGetValue(i, out var f))
            {
                f = typeof(Cfg).GetField("Rarity" + i, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                s_RarityFields[i] = f;
            }
            return f != null && f.FieldType == typeof(int) ? (int)f.GetValue(null) : 10;
        }

        /// <summary>The disguise kit only works where you've got teammates to blend in with.</summary>
        public static bool TeamGame => TeamCount > 1 && (Mode == GameMode.Teams || Mode == GameMode.Teams3 || Mode == GameMode.Teams4 || Mode == GameMode.Trio || Mode == GameMode.Quad);

        public static string WackyName(Item i)
        {
            switch (i)
            {
                case Item.BouncyCastle: return "Bouncy Castle Palace Spawner";
                case Item.Decoy: return "Decoy";
                case Item.OmenMap: return "Omen's Map";
                case Item.GoldenRock: return "Rock of Immortality";
                case Item.BlueShell: return "Blue Shell";
                case Item.Connect4Wand: return "Connect 4 Wand";
                case Item.JumpBoots: return "Triple Jump Boots";
                case Item.StickOfTruth: return "Lifting Stick";
                case Item.SuicideBomb: return "Suicide Bomb";
                case Item.GravityStaff: return "Gravity Staff";
                case Item.AttentionSnail: return "Attention Snail";
                case Item.InvSwapper: return "Inventory Swapper";
                case Item.TimeoutBall: return "Timeout Ball";
                case Item.KnockStick1: return "Knockback Stick (1 use)";
                case Item.KnockStick: return "Knockback Stick";
                case Item.ShieldPotion: return "10 Second Shield";
                case Item.SlowGun: return "Slowness Gun";
                case Item.TreeBreaker: return "Tree Breaker";
                case Item.DrawMonster: return "Draw a Monster";
                case Item.BearJuice: return "Bear Juice";
                case Item.ChaosSigil: return "Sigil of CHAOS";
                case Item.DroneBot: return "Little Turret Bot";
                case Item.GameshowWand: return "Gameshow Wand";
                case Item.DanceBomb: return "Dance Bomb";
                case Item.DecoyBase: return "Decoy Base";
                case Item.BaseRebuild: return "Instant Base Rebuild";
                case Item.FakeFlare: return "Fake Airdrop Flare";
                case Item.DisguiseKit: return "Disguise Kit";
                case Item.BallGrabber: return "Ball Grabber 3000";
                case Item.Scribble: return "Scribblenauts Notebook";
                case Item.AntGun: return "Ant Gun";
                case Item.ClutterBomb: return "Clutter Bomb";
                default: return null;
            }
        }

        /// <summary>What it does, one line (the bag's description: "Name: what it does").</summary>
        public static string WackyBlurb(Item i)
        {
            switch (i)
            {
                case Item.BouncyCastle: return "LMB: throw it like a grenade - a pink bouncy castle pops up where it lands (anywhere - even their base, just not right against their buildings). Jump on it!";
                case Item.Decoy: return "LMB: a fake you, holding what you held, runs at the nearest enemy. Hit it and it's gone in a puff of smoke and a laugh.";
                case Item.OmenMap: return "LMB: a map of the whole game - click a spot and you're teleported there.";
                case Item.GoldenRock: return "Hold it in your hand (it hits like your rock) and you can't die - a killing hit leaves you on 1 health. In your bag it does nothing.";
                case Item.BlueShell: return "LMB: it flies up and homes in on the leader (whoever has the ball, or the most kills) and knocks them flat - they drop half their things. No damage.";
                case Item.Connect4Wand: return $"LMB: zap someone and everything stops for a game of Connect 4 - {MinigameClock:0} s on each clock. Lose (or run out of time) and you die. Everyone watches.";
                case Item.JumpBoots: return "LMB: put them on - then jump twice more in the air. They break when you die.";
                case Item.StickOfTruth: return $"LMB ({StickOfTruthShots} shots): whatever you shoot is LIFTED - a player goes sky high; a wall, a chest, a tree or a horse floats up for {LiftTime:0} s, then drops back.";
                case Item.SuicideBomb: return "LMB: you blow up - and so does everyone round you (not through walls).";
                case Item.GravityStaff: return "LMB: flip your own gravity - you fall up and walk upside down. Again to flip back. Never runs out.";
                case Item.AttentionSnail: return $"LMB: put a snail down. For {SnailTime:0} s every camera in the game is on it ({SnailShortTime:0} s in the last 30 s of a match).";
                case Item.InvSwapper: return "LMB: two players at random swap everything in their bags.";
                case Item.TimeoutBall: return $"LMB: throw it - it's bouncy. Whoever it hits (you too!) is locked in a ball for {PrisonTime:0} s: nobody can hurt them, and they come out in smoke.";
                case Item.KnockStick1: return "LMB: one hit and it breaks - but whoever it hits goes flying a VERY long way. No damage.";
                case Item.KnockStick: return "LMB: knocks whoever it hits back a good way. No damage. Never breaks.";
                case Item.ShieldPotion: return $"LMB: drink - for {ShieldTime:0} s nothing can hurt you at all. You can't attack either.";
                case Item.SlowGun: return $"LMB ({SlowGunShots} shots): a blob of goo - everyone where it lands is much slower for {SlowTime:0} s.";
                case Item.TreeBreaker: return $"LMB: any tree falls in one hit - {TreeBreakerWood} wood ({TreeBreakerUses} trees). Hits players and walls hard too.";
                case Item.DrawMonster: return $"LMB: draw a monster ({DrawTime:0} s). It comes alive, finds an enemy and fills their screen with copies of itself.";
                case Item.BearJuice: return $"LMB: throw it - whoever it splashes (an enemy, or you: throw it at your feet) is a BEAR for {BearTime / 60f:0} minutes or until they die - lots of health, huge claws that tear through bases, very slow, no items.";
                case Item.ChaosSigil: return $"LMB: every item in the game - in bags, chests and on the floor - rains down spread evenly over the whole map, and glows through walls for {ChaosGlowTime:0} s.";
                case Item.DroneBot: return "LMB: put it down and drive it (WASD, Space hops). LMB blows it up: anyone close is left on 5 HP (40 or less: dead). E: back to your body.";
                case Item.GameshowWand: return $"LMB: zap someone and everything stops for a trivia question. They get it right: you die. Wrong: they die. ({GameshowAnswerTime:0} s to answer.)";
                case Item.DanceBomb: return $"LMB: throw it - everyone within {DanceRadius:0} m has to dance for {DanceTime:0} s (careful - you too).";
                case Item.DecoyBase: return "LMB: throw it - a copy of your whole base appears there. Anyone who hits it finds out it's fake.";
                case Item.BaseRebuild: return $"LMB: everything of your base destroyed in the last {RebuildWindow:0} s comes back.";
                case Item.FakeFlare: return "LMB: calls in a FAKE airdrop - it's full of every item in the game, and none of them come out.";
                case Item.DisguiseKit: return "LMB (team games): you look like the enemy - their colour and name - until you hit someone or get hit.";
                case Item.BallGrabber: return $"Look at the ball and LMB: it snaps straight into your hands (not through walls, up to {BallGrabRange:0} m). Then it breaks.";
                case Item.Scribble: return "LMB: type anything - it appears and does something.";
                case Item.AntGun: return $"LMB ({AntGunShots} shots): whoever you hit is an ANT for {AntTime:0} s - they can't hurt anyone, and nobody can hurt them.";
                case Item.ClutterBomb: return $"LMB: throw it - glitter, confetti and junk fill the air there for {ClutterTime:0} s.";
                default: return null;
            }
        }

        /// <summary>Shots / uses a fresh one has (the stack's Data).</summary>
        static int WackyMaxData(Item i)
        {
            switch (i)
            {
                case Item.StickOfTruth: return StickOfTruthShots;
                case Item.SlowGun: return SlowGunShots;
                case Item.AntGun: return AntGunShots;
                case Item.TreeBreaker: return TreeBreakerUses;
                default: return 0;
            }
        }

        public static bool IsWackyMelee(Item i) => i == Item.KnockStick1 || i == Item.KnockStick || i == Item.TreeBreaker || i == Item.GoldenRock;

        static MeleeStats WackyMelee(Item i)
        {
            switch (i)
            {
                case Item.GoldenRock: return new MeleeStats { Cooldown = RockCooldown, Range = RockRange, PlayerDamage = RockPlayerDamage, WoodGather = RockWoodGather, StoneGather = RockStoneGather, StructureDamage = RockStructureDamage };
                case Item.KnockStick1: return new MeleeStats { Cooldown = 0.6f, Range = 3.2f, PlayerDamage = 0f, WoodGather = 2f, StoneGather = 1f, StructureDamage = 5f };
                case Item.KnockStick: return new MeleeStats { Cooldown = 0.55f, Range = 3.2f, PlayerDamage = 0f, WoodGather = 2f, StoneGather = 1f, StructureDamage = 5f };
                case Item.TreeBreaker: return new MeleeStats { Cooldown = 0.9f, Range = 2.9f, PlayerDamage = 45f, WoodGather = 9999f, StoneGather = 2f, StructureDamage = 45f }; // (three times as strong as it was)
                default: return default;
            }
        }

        /// <summary>Thrown like the bomb bush (ThrowItemRpc), something happening where it lands.</summary>
        public static bool WackyThrown(Item i) => i == Item.DanceBomb || i == Item.ClutterBomb || i == Item.DecoyBase || i == Item.BouncyCastle || i == Item.BearJuice;

        /// <summary>Worn gear that sits in your bag until you hold it and click (nothing from an airdrop goes on by itself).</summary>
        public static bool WornGear(Item i) => i == Item.Jetpack || i == Item.Wallhack || i == Item.JumpBoots;
    }
}
