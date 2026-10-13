using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// JONAH TEST MODE BRUH (GameRules.JonahBruh): Classic, with these changes - and only in this mode:
    /// - how you win is picked on the menu's mode page (JonahBruhProgress): NORMAL (Classic: the ball in your machine at
    ///   the end) or PROGRESSION (Progress: the ball in your machine fills your bar);
    /// - Armour (the POWER ITEMS one) is at the top of the Advanced Trade Station and costs JonahBruhArmorWood (150);
    ///   the Advanced Trade Station itself costs JonahBruhBench2Wood (1000);
    /// - no stun trap, no ladder: not craftable, not in airdrops, Fun or the notebook (Cfg.Removed); the death wand isn't
    ///   crafted but comes in airdrops (Cfg.InAirdropPool, Cfg.AirdropLoot);
    /// - no chainsaw: the BETTER HATCHET is in its place on the Trade Station (a steel hatchet that chops faster, 50 wood);
    /// - the hammer only takes walls up to stone (no metal or armoured walls), a stone upgrade costs JonahBruhStoneWood (150)
    ///   a piece, stone pieces have JonahBruhStoneHpMul times their HP, and they're MAGICAL STONE (MagicStone: violet
    ///   stone with glowing runes and seams);
    /// - the spawn bedrock is one cell (3 x 3 m: SmallBedrock), with a smaller alien machine and the wood machine on it;
    /// - "respawn in the wild" lets you pick the spot on a map of the whole map (Hud.Death.cs: DrawWildPicker).
    /// </summary>
    public static partial class Cfg
    {
        public static bool JonahBruh => Rules == GameRules.JonahBruh;
        [Tune("Jonah Test Mode Bruh")] public static int JonahBruhArmorWood = 150, JonahBruhStoneWood = 250, BetterHatchetWood = 50, JonahBruhBench2Wood = 1000, JonahBruhCrossbowWood = 500;
        [Tune("Jonah Test Mode Bruh")] public static float JonahBruhStoneHpMul = 2f;
        /// <summary>How Jonah Test Mode Bruh is won (picked on the menu's mode page): 0 NORMAL (like Classic), 1 PROGRESSION
        /// (like Progress: keep the ball in your machine to fill your bar - Cfg.ProgressSeconds).</summary>
        [Tune("Jonah Test Mode Bruh")] public static int JonahBruhProgress = 0;
        /// <summary>Jonah Test Mode Bruh played as PROGRESSION.</summary>
        public static bool JonahBruhProgression => JonahBruh && JonahBruhProgress != 0;
        /// <summary>The better hatchet: faster swings and more wood a hit than the hatchet.</summary>
        [Tune("Jonah Test Mode Bruh")] public static float BetterHatchetCooldown = 0.55f, BetterHatchetWoodGather = 24f, BetterHatchetStoneGather = 3f, BetterHatchetPlayerDamage = 16f, BetterHatchetStructureDamage = 14f;

        /// <summary>Jonah Test Mode Bruh: no door raiding - a doorway is one piece (hitting its door hurts the doorway), with
        /// a wall's health (Cfg.PieceHp).</summary>
        public static bool DoorIsPiece => JonahBruh;

        /// <summary>Taken out of Jonah Test Mode Bruh's crafting (only there). The death wand still comes in its airdrops.</summary>
        public static bool JonahBruhOut(Item i) => JonahBruh && (i == Item.BearTrap || i == Item.DeathWand || i == Item.Ladder || i == Item.Chainsaw);

        /// <summary>The highest tier the hammer takes a piece to: stone in Jonah Test Mode Bruh, armoured everywhere else.</summary>
        public static int HammerMaxTier => JonahBruh ? 1 : MaxFortify;

        /// <summary>Stone pieces are tougher in Jonah Test Mode Bruh.</summary>
        static float StoneHpMul => JonahBruh ? Mathf.Max(0.1f, JonahBruhStoneHpMul) : 1f;

        // ---------- the one-cell spawn bedrock ----------

        /// <summary>Jonah Test Mode Bruh: the spawn bedrock is a single build cell (3 x 3 m) instead of 2 x 2 - the alien
        /// machine (SmallMachineScale), its ball socket (SmallSocketScale) and the wood machine (SmallMachineScale) all on it.</summary>
        public static bool SmallBedrock => JonahBruh;
        public const float SmallMachineScale = 0.5f, SmallSocketScale = 0.7f;
        /// <summary>The small machine is taller than it is wide (x this on its height): stood on its flat top (MachineTopY over
        /// the bedrock) you can jump over a wall on the bedrock's edge - out of a one-cell base. A step beside it
        /// (ClimbStepH high) gets you up there.</summary>
        public const float SmallMachineTall = 0.82f, MachineTopY = 2.55f * SmallMachineTall, ClimbStepH = 1.0f;
        /// <summary>Where the step up onto the small machine stands (between it and the wood machine), and its size.</summary>
        public static readonly Vector3 ClimbStepLocal = new Vector3(-0.33f, 0f, -1.1f), ClimbStepSize = new Vector3(0.42f, ClimbStepH, 0.55f);
        /// <summary>Half the bedrock's size now.</summary>
        public static float BedrockHalfNow => SmallBedrock ? Cell * 0.5f : BedrockHalf;

        /// <summary>The small bedrock's cell: the one just behind the base's middle (towards the back of the base), to one
        /// side - the base's middle is a corner of the build grid, so no single cell is centred on it.</summary>
        static Vector3 SmallBedrockCenter(int team)
        {
            var bc = BaseCenter[team];
            var corner = new Vector3(Mathf.Round(bc.x / Cell) * Cell, bc.y, Mathf.Round(bc.z / Cell) * Cell);
            var back = GridDir(BackDir(team));
            return corner + (back + GridDir(Vector3.Cross(Vector3.up, back))) * (Cell * 0.5f);
        }
        static Vector3 GridDir(Vector3 d) => Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(d.z));

        /// <summary>Where things stand on the small bedrock, in the alien machine's frame (+z out towards the middle of the
        /// map, +x the LEFT as you look at the machine from the spawn): the machine at the back on the left, its socket in
        /// front of it, the wood machine at the back on the right (its logs land in front of it), the spawn along the front.</summary>
        static readonly Vector3 k_SmallMachine = new Vector3(0.6f, 0f, -1.05f), k_SmallSocket = new Vector3(0.6f, 0f, 0.02f), k_SmallWood = new Vector3(-0.9f, 0f, -1.0f);
        static readonly float[] k_SmallSpawnX = { -0.5f, -1.12f, 0.12f, 0.75f };
        const float SmallSpawnZ = 1.02f;

        /// <summary>A point on the small bedrock, in the alien machine's frame (k_SmallMachine...).</summary>
        public static Vector3 OnSmallBedrock(int team, Vector3 local) => SmallBedrockCenter(team) + Quaternion.LookRotation(-BackDir(team)) * local;
        static Vector3 SmallMachinePos(int team) => OnSmallBedrock(team, k_SmallMachine + Vector3.up * BaseY);
        static Vector3 SmallSocketPos(int team) => OnSmallBedrock(team, k_SmallSocket + Vector3.up * (BaseY + 0.64f));
        static Vector3 SmallWoodMachinePos(int team) => OnSmallBedrock(team, k_SmallWood + Vector3.up * BaseY);
        static Vector3 SmallSpawnPos(int team, int slot) => OnSmallBedrock(team, new Vector3(k_SmallSpawnX[Mathf.Abs(slot) % k_SmallSpawnX.Length], BaseY + 0.05f, SmallSpawnZ));
        /// <summary>Is cell (i, j) the small bedrock of any team?</summary>
        static bool IsSmallBedrockCell(int i, int j)
        {
            for (int t = 0; t < TeamCount; t++)
            {
                var b = SmallBedrockCenter(t);
                if (i == Mathf.FloorToInt(b.x / Cell) && j == Mathf.FloorToInt(b.z / Cell)) return true;
            }
            return false;
        }
    }

    /// <summary>Jonah Test Mode Bruh's MAGICAL STONE: stone pieces in a deep violet stone, their seams glowing, with runes
    /// glowing on both faces of the walls and the foundations' sides - all pulsing slowly together (Structure.CreateVisual).</summary>
    public static class MagicStone
    {
        public static readonly Color Stone = new Color(0.52f, 0.42f, 0.8f), Seam = new Color(0.62f, 0.5f, 0.9f), Glow = new Color(0.62f, 0.95f, 1f);
        static Material s_Glow;
        static int s_Frame = -1;

        /// <summary>The shared glowing material (its glow pulses: Pulse).</summary>
        public static Material GlowMat
        {
            get
            {
                if (s_Glow != null) return s_Glow;
                s_Glow = new Material(Art.Mat(Glow));
                if (s_Glow.HasProperty("_EmissionColor")) { s_Glow.EnableKeyword("_EMISSION"); s_Glow.SetColor("_EmissionColor", Glow * 2.6f); }
                Art.Register(s_Glow, Glow);
                return s_Glow;
            }
        }

        /// <summary>Make a freshly built stone piece magical: its seams (drawn in Seam) glow, and runes go on its faces.</summary>
        public static void Enchant(Transform tr, PieceType t, Color seam)
        {
            var seamMat = Art.Mat(seam);
            foreach (var r in tr.GetComponentsInChildren<MeshRenderer>()) if (r.sharedMaterial == seamMat) r.sharedMaterial = GlowMat;
            var g = GlowMat;
            switch (t)
            {
                case PieceType.Wall:
                    for (int f = -1; f <= 1; f += 2)
                        for (int i = 0; i < 3; i++)
                            Rune(tr, g, new Vector3((i - 1) * 0.95f, 1.12f + (i % 2) * 0.75f, f * 0.16f), i + (f > 0 ? 3 : 0));
                    break;
                case PieceType.Doorway:
                    for (int f = -1; f <= 1; f += 2)
                        for (int k = -1; k <= 1; k += 2) Rune(tr, g, new Vector3(k * 1.05f, 1.5f, f * 0.16f), k + f + 2);
                    break;
                case PieceType.Window:
                    for (int f = -1; f <= 1; f += 2)
                        for (int k = -1; k <= 1; k += 2) Rune(tr, g, new Vector3(k * 1.225f, 1.65f, f * 0.16f), k + f + 2);
                    break;
                case PieceType.Foundation:
                    for (int s = 0; s < 4; s++)
                    {
                        var rot = Quaternion.Euler(0, s * 90f, 0);
                        var rune = Rune(tr, g, rot * new Vector3(0, 0.58f, 1.515f), s);
                        rune.localRotation = rot * rune.localRotation;
                    }
                    break;
            }
            if (tr.GetComponent<Pulser>() == null) tr.gameObject.AddComponent<Pulser>();
        }

        /// <summary>One glowing rune on a face (local z = the face): a diamond with a stroke through it, made a few ways.</summary>
        static Transform Rune(Transform tr, Material g, Vector3 at, int kind)
        {
            var r = new GameObject("rune").transform;
            r.SetParent(tr, false);
            r.localPosition = at;
            Art.Box(r, Glow, Vector3.zero, new Vector3(0.2f, 0.2f, 0.02f), new Vector3(0, 0, 45f), false, g);
            Art.Box(r, Stone, Vector3.zero, new Vector3(0.13f, 0.13f, 0.03f), new Vector3(0, 0, 45f));
            switch (((kind % 3) + 3) % 3)
            {
                case 0: Art.Box(r, Glow, Vector3.zero, new Vector3(0.035f, 0.42f, 0.035f), default, false, g); break;
                case 1: Art.Box(r, Glow, Vector3.zero, new Vector3(0.42f, 0.035f, 0.035f), default, false, g); break;
                default:
                    Art.Box(r, Glow, new Vector3(0, 0.18f, 0), new Vector3(0.24f, 0.035f, 0.035f), default, false, g);
                    Art.Box(r, Glow, new Vector3(0, -0.18f, 0), new Vector3(0.24f, 0.035f, 0.035f), default, false, g);
                    break;
            }
            return r;
        }

        static readonly Color k_Crack = new Color(0.03f, 0.02f, 0.04f), k_CrackEdge = new Color(0.93f, 0.88f, 1f);

        /// <summary>The battering ram's cracks (Structure.RamCracks): dark jagged lines across the piece's faces - a few at
        /// stage 1, more and longer, branching, at stage 2. The same on every screen (seeded by the piece).</summary>
        public static void Crack(Transform tr, PieceType t, int stage, int seed)
        {
            var rng = new System.Random(seed * 7349 + 11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int lines = stage >= 2 ? 14 : 11; // (all over it)
            // where a crack can start on each face: (centre, half size, the face's way out)
            var faces = new System.Collections.Generic.List<(Vector3 c, Vector2 half, Quaternion rot)>();
            switch (t)
            {
                case PieceType.Wall:
                    faces.Add((new Vector3(0, 1.5f, 0.175f), new Vector2(1.3f, 1.3f), Quaternion.identity));
                    faces.Add((new Vector3(0, 1.5f, -0.175f), new Vector2(1.3f, 1.3f), Quaternion.Euler(0, 180f, 0)));
                    break;
                case PieceType.Doorway:
                case PieceType.Window:
                {
                    float x = t == PieceType.Doorway ? 1.05f : 1.225f, w = t == PieceType.Doorway ? 0.35f : 0.2f;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        faces.Add((new Vector3(k * x, 1.5f, 0.175f), new Vector2(w, 1.3f), Quaternion.identity));
                        faces.Add((new Vector3(k * x, 1.5f, -0.175f), new Vector2(w, 1.3f), Quaternion.Euler(0, 180f, 0)));
                    }
                    break;
                }
                case PieceType.Foundation:
                    for (int s = 0; s < 4; s++) { var q = Quaternion.Euler(0, s * 90f, 0); faces.Add((q * new Vector3(0, 0.55f, 1.515f), new Vector2(1.3f, 0.25f), q)); }
                    break;
                case PieceType.Floor:
                    faces.Add((new Vector3(0, 0.02f, 0), new Vector2(1.3f, 1.3f), Quaternion.Euler(-90f, 0, 0)));
                    break;
                default:
                    faces.Add((new Vector3(0, 1.5f, 0.2f), new Vector2(1.0f, 1.0f), Quaternion.identity));
                    break;
            }
            var root = new GameObject("cracks").transform;
            root.SetParent(tr, false);
            foreach (var f in faces)
            {
                int n = Mathf.Max(2, Mathf.RoundToInt(lines * Mathf.Clamp01(f.half.x * f.half.y / 1.6f)));
                for (int i = 0; i < n; i++)
                {
                    // a zig-zag: from a point spread evenly over the face (a jittered grid, so it's cracked ALL over), long
                    // segments heading roughly one way, branching
                    int cols = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(n * f.half.x / Mathf.Max(0.1f, f.half.y))));
                    int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)cols));
                    float cx = (-1f + (i % cols + 0.5f) * 2f / cols) * f.half.x, cy = (-1f + (i / cols + 0.5f) * 2f / rows) * f.half.y;
                    var p = new Vector2(cx + R(-0.3f, 0.3f) * f.half.x / cols, cy + R(-0.3f, 0.3f) * f.half.y / rows);
                    float dir = R(0f, 360f);
                    int segs = 6;
                    for (int k = 0; k < segs; k++)
                    {
                        float len = R(0.18f, 0.36f), a = (dir + R(-45f, 45f)) * Mathf.Deg2Rad;
                        var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len;
                        q.x = Mathf.Clamp(q.x, -f.half.x, f.half.x); q.y = Mathf.Clamp(q.y, -f.half.y, f.half.y);
                        Segment(root, f.c, f.rot, p, q, 0.05f);
                        if (k == 2 || k == 4)
                        {
                            float b = (dir + (R(0f, 1f) < 0.5f ? 70f : -70f)) * Mathf.Deg2Rad;
                            var e = q + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * R(0.15f, 0.32f);
                            e.x = Mathf.Clamp(e.x, -f.half.x, f.half.x); e.y = Mathf.Clamp(e.y, -f.half.y, f.half.y);
                            Segment(root, f.c, f.rot, q, e, 0.035f);
                        }
                        p = q;
                    }
                }
            }
        }

        /// <summary>One straight bit of a crack on a face (face centre c, turned rot: its x and y across the face, z out of it).</summary>
        static void Segment(Transform root, Vector3 c, Quaternion rot, Vector2 a, Vector2 b, float w)
        {
            var d = b - a;
            if (d.sqrMagnitude < 0.0004f) return;
            var mid = (a + b) * 0.5f;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            // a near-black split with a pale chipped edge round it, so it stands out on any stone
            var edge = Art.Box(root, k_CrackEdge, c + rot * new Vector3(mid.x, mid.y, -0.004f), new Vector3(d.magnitude + w * 2.2f, w * 2.2f, 0.016f));
            edge.transform.localRotation = rot * Quaternion.Euler(0, 0, ang);
            edge.name = "crack edge";
            var go = Art.Box(root, k_Crack, c + rot * new Vector3(mid.x, mid.y, 0.002f), new Vector3(d.magnitude + w, w, 0.022f));
            go.transform.localRotation = rot * Quaternion.Euler(0, 0, ang);
            go.name = "crack";
        }

        /// <summary>The glow breathes in and out (one material for every magical piece: set once a frame).</summary>
        class Pulser : MonoBehaviour
        {
            void Update()
            {
                if (s_Frame == Time.frameCount || s_Glow == null || !s_Glow.HasProperty("_EmissionColor")) return;
                s_Frame = Time.frameCount;
                s_Glow.SetColor("_EmissionColor", Glow * (2.4f + Mathf.Sin(Time.time * 1.8f) * 0.9f));
            }
        }
    }
}
