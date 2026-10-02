using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Crafting tiers. Everything is crafted from the TAB list (Hud.Crafting.cs), which shows:
    /// - the starter items (tier 0) always;
    /// - tier 1 items while you're in your base and your team has a Workbench T1 placed;
    /// - tier 2 items while you're in your base and your team has a Workbench T2 placed (it needs the T1 first).
    /// Builder has no bases: the tiers follow the team's benches wherever they stand.
    /// </summary>
    public static partial class Cfg
    {
        [Tune("Crafting")] public static int WorkbenchWood = 150;
        [Tune("Crafting")] public static int Workbench2Wood = 300;

        /// <summary>Tier 0: always in the TAB list (in this order).</summary>
        static readonly Item[] k_Starter = { Item.Hatchet, Item.Spear, Item.BuildingPlan, Item.Bow, Item.Arrow, Item.Chest, Item.Ram, Item.Workbench };
        /// <summary>Tier 1 (Workbench T1 in your base): the user's list, then the base upgrades and the T2 bench.</summary>
        static readonly Item[] k_Tier1 = { Item.Crossbow, Item.Sword, Item.Armor, Item.Barrier, Item.Chainsaw, Item.FortifyBuff, Item.WoodGenBuff, Item.Workbench2 };
        /// <summary>Tier 2 (Workbench T2 in your base): guns, ammo, C4, saddle, helmet, then whatever's left (pickaxe, boat...).
        /// Anything craftable that isn't tier 0 or 1 is tier 2.</summary>
        static readonly Item[] k_Tier2 = { Item.Shotgun, Item.ShotgunShell, Item.Revolver, Item.RevolverAmmo, Item.C4, Item.Saddle, Item.Helmet, Item.Pickaxe, Item.Boat, Item.Pistol, Item.PistolAmmo, Item.HeavyArmor };

        public static bool IsStarter(Item i) => System.Array.IndexOf(k_Starter, i) >= 0;
        public static IReadOnlyList<Item> StarterOrder => k_Starter;

        /// <summary>Which workbench tier an item needs (0 = none: a starter item).</summary>
        public static int CraftTier(Item i) => IsStarter(i) ? 0 : System.Array.IndexOf(k_Tier1, i) >= 0 ? 1 : 2;

        /// <summary>The best workbench a team has placed (0 none, 1, 2).</summary>
        public static int BenchTier(int team) => Workbench.TierOf(team);

        /// <summary>The highest tier a player of `team` standing at p can craft: their bench's tier inside their base
        /// (Builder: anywhere), otherwise 0 (just the starter items).</summary>
        public static int CraftTierAt(int team, Vector3 p) => CanCraftAt(team, p) ? BenchTier(team) : 0;

        /// <summary>An item's craft number (CraftRpc numbering: recipes, then power items from PowerBase), -1 if it can't be made in this mode.</summary>
        public static int CraftIndexOf(Item id)
        {
            int p = PowerIndex(id);
            if (p >= 0 && p < PowerCount) return PowerBase + p;
            return RecipeIndex(id);
        }

        public static bool ValidCraftIndex(int i) => i >= 0 && (i < PowerBase ? i < RecipeCount : i - PowerBase < PowerCount);

        /// <summary>The recipe behind a craft number (power items priced for the team: fortify and wood gen go up).</summary>
        public static Recipe CraftRecipe(int i, int team) => i >= PowerBase ? GetPowerRecipe(i - PowerBase, team) : GetRecipe(i);

        static readonly List<int> s_Seen = new List<int>();

        /// <summary>Everything craftable in this mode up to `maxTier`, as craft numbers in display order (tier by tier).</summary>
        public static void CraftList(List<int> into, int maxTier)
        {
            into.Clear();
            for (int tier = 0; tier <= Mathf.Min(2, maxTier); tier++) AddTier(into, tier);
        }

        /// <summary>One tier's craft numbers, in display order.</summary>
        public static void AddTier(List<int> into, int tier)
        {
            var order = tier == 0 ? k_Starter : tier == 1 ? k_Tier1 : k_Tier2;
            foreach (var id in order)
            {
                int i = CraftIndexOf(id);
                if (i >= 0 && !into.Contains(i)) into.Add(i);
            }
            if (tier != 2) return;
            // anything else craftable in this mode that isn't listed anywhere goes on the end of tier 2
            for (int i = 0; i < RecipeCount; i++)
                if (CraftTier(GetRecipe(i).Output) == 2 && !into.Contains(i)) into.Add(i);
            for (int i = 0; i < PowerCount; i++)
                if (CraftTier(GetPowerRecipe(i).Output) == 2 && !into.Contains(PowerBase + i)) into.Add(PowerBase + i);
        }
    }

    /// <summary>
    /// The workbenches, T1 and T2: futuristic machines in the alien machine's style (silver metal, glowing tips, spinning
    /// rings, a floating orb), crafted from the TAB list and put down anywhere inside your own base (Builder: anywhere).
    /// They can't be broken. Nothing is made AT a bench: while it stands in your base, its tier's items show up in the TAB
    /// crafting list whenever you're in your base. T2 needs a T1 placed first; one of each per team.
    /// It's a Container (kind Workbench / Workbench2) with no slots. E on it just tells you that.
    /// </summary>
    public class Workbench : MonoBehaviour
    {
        /// <summary>Half the footprint (x across, z front to back) and the height of the bench top (both tiers).</summary>
        public const float HalfX = 0.65f, HalfZ = 0.38f, TopY = 0.9f;

        public static bool IsBench(Item i) => i == Item.Workbench || i == Item.Workbench2;
        public static int TierOfItem(Item i) => i == Item.Workbench2 ? 2 : 1;

        /// <summary>A team's workbench of this tier (null if it hasn't placed one).</summary>
        public static Container ForTeam(int team, int tier = 1)
        {
            foreach (var c in Container.All)
                if (c != null && c.IsSpawned && c.IsWorkbench && c.BenchTier == tier && c.Team.Value == team) return c;
            return null;
        }

        /// <summary>The best bench a team has placed: 2, 1 or 0.</summary>
        public static int TierOf(int team)
        {
            int best = 0;
            foreach (var c in Container.All)
                if (c != null && c.IsSpawned && c.IsWorkbench && c.Team.Value == team) best = Mathf.Max(best, c.BenchTier);
            return best;
        }

        /// <summary>The middle of the bench top.</summary>
        public static Vector3 Top(Container c) => c.transform.TransformPoint(new Vector3(0, TopY, 0.04f));

        /// <summary>Why a workbench can't go here (null = it can). Called from PlayerNet.DeployProblem.</summary>
        public static string PlaceProblem(Item kind, int team, Vector3 pos, float yaw)
        {
            int tier = TierOfItem(kind);
            if (ForTeam(team, tier) != null) return $"Your team already has a Workbench T{tier}";
            if (tier == 2 && ForTeam(team, 1) == null) return "Put a Workbench T1 down first";
            if (Cfg.Builder) return null; // Builder: no bases - anywhere
            var rot = Quaternion.Euler(0, yaw, 0);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    if (Cfg.BaseTeamAt(pos + rot * new Vector3(sx * HalfX, 0, sz * HalfZ)) != team)
                        return "Workbenches go inside your own base";
            if (FootprintDistance(pos, rot, Cfg.SpawnPos(team)) < 0.45f) return "Keep the spawn spot clear";
            if (FootprintDistance(pos, rot, Cfg.SocketPos(team)) < 1.0f) return "Keep the ball socket clear";
            return null;
        }

        static float FootprintDistance(Vector3 pos, Quaternion rot, Vector3 p)
        {
            var l = Quaternion.Inverse(rot) * (p - pos);
            float dx = Mathf.Max(0f, Mathf.Abs(l.x) - HalfX), dz = Mathf.Max(0f, Mathf.Abs(l.z) - HalfZ);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>A sensible spot for a team's bench: a front corner of the bedrock (T1 left, T2 right), facing in (AutoTest).</summary>
        public static Vector3 DefaultPos(int team, int tier = 1)
        {
            var back = Cfg.BackDir(team);
            var left = -Vector3.Cross(Vector3.up, back);
            return Cfg.BedrockCenter(team) - back * 2.4f + left * (tier == 2 ? -1.85f : 1.85f) + Vector3.up * Cfg.BaseY;
        }
        public static float DefaultYaw(int team) => Quaternion.LookRotation(Cfg.BackDir(team)).eulerAngles.y;

        /// <summary>Server: put a workbench down (already checked and paid for).</summary>
        public static Container ServerSpawn(int team, Vector3 pos, float yaw, int tier = 1)
        {
            var go = Instantiate(Bootstrap.I.containerPrefab, pos, Quaternion.Euler(0, yaw, 0));
            var c = go.GetComponent<Container>();
            c.ServerInit(tier == 2 ? Container.Workbench2 : Container.Workbench, team, 0, null);
            go.GetComponent<NetworkObject>().Spawn(true);
            return c;
        }

        // ------------------------------------------------------------------ the look

        // the alien machine's palette (MapBuilder.BuildMachine)
        static readonly Color k_Metal = new Color(0.3f, 0.33f, 0.36f), k_Silver = new Color(0.72f, 0.74f, 0.78f), k_SilverDark = new Color(0.5f, 0.52f, 0.56f);
        static readonly Color k_Alien = new Color(0.45f, 0.95f, 0.55f), k_Pink = new Color(1f, 0.32f, 0.78f), k_Cyan = new Color(0.35f, 0.95f, 1f);
        static readonly Color k_Dark = new Color(0.08f, 0.09f, 0.11f);

        static readonly Dictionary<Color, Material> s_Glow = new Dictionary<Color, Material>();

        /// <summary>A glowing (emissive) material.</summary>
        public static Material Glow(Color c, float k = 1.6f)
        {
            var key = new Color(c.r, c.g, c.b, k);
            if (s_Glow.TryGetValue(key, out var m) && m) return m;
            m = new Material(Art.Mat(c));
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * k);
            }
            Art.Register(m, c);
            s_Glow[key] = m;
            return m;
        }

        // the built-in meshes' sizes, so parts can be given in metres whatever the mesh is
        static float CylR => Art.Cylinder.bounds.extents.x;
        static float CylH => Art.Cylinder.bounds.extents.y;
        static float SphR => Art.Sphere.bounds.extents.x;

        /// <summary>A cylinder of radius r and height h, centred at p (local to parent).</summary>
        public static GameObject Cyl(Transform t, Color c, Vector3 p, float r, float h, Vector3 euler = default, Material mat = null, string name = "part")
            => Art.Part(t, Art.Cylinder, c, p, new Vector3(r / CylR, h * 0.5f / CylH, r / CylR), euler, false, mat, name);

        /// <summary>A sphere with these radii.</summary>
        public static GameObject Ball(Transform t, Color c, Vector3 p, Vector3 radii, Material mat = null, string name = "part")
            => Art.Part(t, Art.Sphere, c, p, radii / SphR, default, false, mat, name);

        /// <summary>A cylinder from a to b, radius r.</summary>
        public static GameObject Rod(Transform t, Color c, Vector3 a, Vector3 b, float r, Material mat = null)
        {
            var d = b - a;
            return Art.Part(t, Art.Cylinder, c, (a + b) * 0.5f, new Vector3(r / CylR, d.magnitude * 0.5f / CylH, r / CylR),
                Quaternion.FromToRotation(Vector3.up, d.normalized).eulerAngles, false, mat);
        }

        /// <summary>A ring of `n` little blocks of radius `rad` (the alien machine's spinning rings): every `litEvery`th one glows.</summary>
        public static Transform Ring(Transform parent, Vector3 at, Vector3 tilt, float rad, int n, float block, Color plain, Material lit, int litEvery, string name = "ring")
        {
            var ring = new GameObject(name).transform;
            ring.SetParent(parent, false);
            ring.localPosition = at;
            ring.localRotation = Quaternion.Euler(tilt);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                bool on = litEvery > 0 && i % litEvery == 0;
                Art.Part(ring, Art.Cube, plain, new Vector3(Mathf.Sin(a) * rad, 0, Mathf.Cos(a) * rad), new Vector3(block, block * 0.25f, block * 0.4f),
                    new Vector3(0, a * Mathf.Rad2Deg + 90f, 0), false, on ? lit : null, "bit");
            }
            return ring;
        }

        /// <summary>
        /// A workbench, 1.3 x 0.76 m with its top at TopY, front (+z) towards whoever stands at it. `full` adds the light
        /// (not for ghosts and icons). Named parts animate: ring*, orb, holo, orbit, light.
        /// T1: a silver fabricator - a metal plinth, two pedestals, a console with a screen, a column with a spinning ring,
        ///     two glowing-tipped pylons and a floating green orb (a little alien machine).
        /// T2: much more - hovering on four legs with pink pads, glowing pink seams, twin consoles, a hologram spinning over a
        ///     projector pad, a tall tower with a three-ring gyroscope, a big pink orb in a crown of spikes, tesla pylons,
        ///     two emitter arms and crystals orbiting the tower.
        /// </summary>
        public static void BuildModel(Transform parent, int team, bool full, int tier = 1)
        {
            var t = new GameObject(tier == 2 ? "workbench2" : "workbench").transform;
            t.SetParent(parent, false);
            var tc = Cfg.TeamColor[Mathf.Clamp(team, 0, 3)];
            var teamGlow = Color.Lerp(tc, Color.white, 0.35f);
            var gTeam = Glow(teamGlow);
            var gAlien = Glow(k_Alien);
            var screen = Art.Ghost(new Color(0.4f, 1f, 0.8f, 0.7f));
            if (tier == 2) BuildT2(t, teamGlow, gTeam, gAlien, screen);
            else BuildT1(t, teamGlow, gTeam, gAlien, screen);
            if (full)
            {
                var lg = new GameObject("light");
                lg.transform.SetParent(t, false);
                lg.transform.localPosition = new Vector3(0, TopY + 0.7f, 0.35f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = tier == 2 ? k_Pink : k_Alien;
                l.range = tier == 2 ? 4.5f : 3f;
                l.intensity = tier == 2 ? 1.2f : 1.1f;
                l.shadows = LightShadows.None;
            }
        }

        static void BuildT1(Transform t, Color teamGlow, Material gTeam, Material gAlien, Material screen)
        {
            // heavy plinth with a silver trim, two pedestals and a dark core with a glowing seam between them
            Art.Box(t, k_Metal, new Vector3(0, 0.07f, 0), new Vector3(1.3f, 0.14f, 0.76f));
            Art.Box(t, k_SilverDark, new Vector3(0, 0.15f, 0), new Vector3(1.22f, 0.03f, 0.7f));
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, k_Silver, new Vector3(k * 0.44f, 0.5f, 0), new Vector3(0.36f, 0.7f, 0.66f));
                Art.Box(t, k_SilverDark, new Vector3(k * 0.44f, 0.5f, 0.335f), new Vector3(0.28f, 0.6f, 0.01f)); // a panel
                for (int i = 0; i < 3; i++) Art.Box(t, k_Metal, new Vector3(k * 0.44f, 0.32f + i * 0.08f, 0.342f), new Vector3(0.22f, 0.025f, 0.01f)); // vents
                Art.Box(t, k_Alien, new Vector3(k * 0.44f, 0.68f, 0.342f), new Vector3(0.05f, 0.05f, 0.012f), default, false, gAlien); // status light
            }
            Art.Box(t, k_Dark, new Vector3(0, 0.5f, -0.02f), new Vector3(0.54f, 0.68f, 0.56f));
            Art.Box(t, teamGlow, new Vector3(0, 0.5f, 0.262f), new Vector3(0.05f, 0.56f, 0.01f), default, false, gTeam);
            // the top: a silver slab with dark edges and a glowing strip in the team colour along the front
            Art.Box(t, k_Silver, new Vector3(0, TopY - 0.05f, 0), new Vector3(1.3f, 0.1f, 0.76f));
            Art.Box(t, k_SilverDark, new Vector3(0, TopY - 0.11f, 0), new Vector3(1.34f, 0.04f, 0.8f));
            Art.Box(t, teamGlow, new Vector3(0, TopY - 0.05f, 0.381f), new Vector3(1.1f, 0.025f, 0.01f), default, false, gTeam);
            // a slanted console with a glowing screen on the front right, like the machine's
            Art.Box(t, k_Metal, new Vector3(0.36f, TopY + 0.07f, 0.17f), new Vector3(0.42f, 0.06f, 0.3f), new Vector3(-30, 0, 0));
            Art.Box(t, Color.white, new Vector3(0.36f, TopY + 0.105f, 0.18f), new Vector3(0.36f, 0.012f, 0.24f), new Vector3(-30, 0, 0), false, screen);
            // a projector pad on the left of the top
            Cyl(t, k_SilverDark, new Vector3(-0.32f, TopY + 0.015f, 0.08f), 0.17f, 0.03f);
            Cyl(t, k_Alien, new Vector3(-0.32f, TopY + 0.035f, 0.08f), 0.12f, 0.012f, default, gAlien);

            // the back: a silver column with a spinning ring round it and a floating orb over it (the machine, small)
            float cz = -0.24f;
            Cyl(t, k_Metal, new Vector3(0, TopY + 0.04f, cz), 0.22f, 0.08f);
            Cyl(t, k_Silver, new Vector3(0, TopY + 0.42f, cz), 0.14f, 0.76f);
            Cyl(t, k_Metal, new Vector3(0, TopY + 0.82f, cz), 0.2f, 0.05f);
            Ring(t, new Vector3(0, TopY + 0.45f, cz), new Vector3(15, 0, 8), 0.27f, 12, 0.12f, k_Silver, gTeam, 3, "ring");
            var orb = Art.Part(t, Art.Ico, k_Alien, new Vector3(0, TopY + 1.05f, cz), Vector3.one * 0.09f, default, false, gAlien, "orb").transform;
            Art.Part(orb, Art.Ico, Color.white, Vector3.zero, Vector3.one * 1.6f, default, false, Art.Ghost(new Color(0.5f, 1f, 0.6f, 0.3f)));
            // an emitter arm off the column, pointing down at the pad
            Art.Box(t, k_Metal, new Vector3(-0.16f, TopY + 0.62f, cz + 0.1f), new Vector3(0.36f, 0.05f, 0.06f), new Vector3(0, 35, -10));
            Art.Part(t, Art.Cone, k_Alien, new Vector3(-0.3f, TopY + 0.6f, 0.03f), new Vector3(0.09f, 0.1f, 0.09f), new Vector3(180, 0, 0), false, gAlien);
            // two pylons on the back corners with glowing tips and silver spikes
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, k_Metal, new Vector3(k * 0.56f, TopY + 0.3f, -0.27f), new Vector3(0.1f, 0.6f, 0.1f), new Vector3(0, 0, -k * 6f));
                Art.Part(t, Art.Ico, teamGlow, new Vector3(k * 0.59f, TopY + 0.64f, -0.27f), Vector3.one * 0.055f, default, false, gTeam);
                Art.Part(t, Art.Cone, k_Silver, new Vector3(k * 0.59f, TopY + 0.66f, -0.27f), new Vector3(0.07f, 0.16f, 0.07f));
            }
            // the tier: one lit pip on the front
            Art.Part(t, Art.Ico, k_Alien, new Vector3(0, 0.07f, 0.385f), Vector3.one * 0.035f, default, false, gAlien);
        }

        static void BuildT2(Transform t, Color teamGlow, Material gTeam, Material gAlien, Material screen)
        {
            var gPink = Glow(k_Pink, 2f);
            var gCyan = Glow(k_Cyan, 1.8f);
            // four splayed metal legs on glowing pink pads, the body hovering over a glowing disc
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var foot = new Vector3(sx * 0.6f, 0.03f, sz * 0.33f);
                    var hip = new Vector3(sx * 0.45f, 0.36f, sz * 0.22f);
                    Rod(t, k_Silver, foot + Vector3.up * 0.03f, hip, 0.035f);
                    Cyl(t, k_Metal, foot, 0.08f, 0.04f);
                    Cyl(t, k_Pink, foot + Vector3.up * 0.025f, 0.06f, 0.012f, default, gPink);
                }
            Cyl(t, k_Pink, new Vector3(0, 0.1f, 0), 0.32f, 0.01f, default, gPink);
            Cyl(t, Color.white, new Vector3(0, 0.22f, 0), 0.28f, 0.22f, default, Art.Ghost(new Color(1f, 0.4f, 0.85f, 0.18f))); // its lift beam
            // the chassis: silver with a dark core, pink seams and a cyan-lit grille on the front
            Art.Box(t, k_Metal, new Vector3(0, 0.36f, 0), new Vector3(1.2f, 0.08f, 0.68f));
            Art.Box(t, k_Silver, new Vector3(0, 0.6f, 0), new Vector3(1.24f, 0.42f, 0.7f));
            for (int i = -2; i <= 2; i++)
                Art.Box(t, k_Pink, new Vector3(i * 0.24f, 0.6f, 0.352f), new Vector3(0.025f, 0.36f, 0.01f), default, false, gPink);
            Art.Box(t, k_Dark, new Vector3(0, 0.6f, 0.353f), new Vector3(0.36f, 0.26f, 0.01f));
            Art.Box(t, k_Cyan, new Vector3(0, 0.6f, 0.351f), new Vector3(0.32f, 0.22f, 0.01f), default, false, gCyan);
            for (int i = 0; i < 5; i++) Art.Box(t, k_Metal, new Vector3(0, 0.51f + i * 0.045f, 0.36f), new Vector3(0.36f, 0.018f, 0.012f));
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, k_SilverDark, new Vector3(k * 0.625f, 0.6f, 0), new Vector3(0.02f, 0.36f, 0.6f));
                Art.Box(t, k_Pink, new Vector3(k * 0.637f, 0.6f, 0), new Vector3(0.008f, 0.04f, 0.5f), default, false, gPink);
            }
            // the top: silver, glowing pink all round its edge
            Art.Box(t, k_Silver, new Vector3(0, TopY - 0.05f, 0), new Vector3(1.3f, 0.1f, 0.76f));
            Art.Box(t, k_SilverDark, new Vector3(0, TopY - 0.11f, 0), new Vector3(1.34f, 0.04f, 0.8f));
            Art.Box(t, k_Pink, new Vector3(0, TopY - 0.05f, 0.381f), new Vector3(1.3f, 0.025f, 0.01f), default, false, gPink);
            Art.Box(t, k_Pink, new Vector3(0, TopY - 0.05f, -0.381f), new Vector3(1.3f, 0.025f, 0.01f), default, false, gPink);
            for (int k = -1; k <= 1; k += 2) Art.Box(t, k_Pink, new Vector3(k * 0.651f, TopY - 0.05f, 0), new Vector3(0.01f, 0.025f, 0.76f), default, false, gPink);
            // twin consoles with screens
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, k_Metal, new Vector3(k * 0.42f, TopY + 0.07f, 0.2f), new Vector3(0.34f, 0.06f, 0.26f), new Vector3(-32, k * -12f, 0));
                Art.Box(t, Color.white, new Vector3(k * 0.42f, TopY + 0.105f, 0.21f), new Vector3(0.29f, 0.012f, 0.2f), new Vector3(-32, k * -12f, 0), false, screen);
            }
            // a projector in the middle of the top, with a hologram spinning over it
            Cyl(t, k_SilverDark, new Vector3(0, TopY + 0.02f, 0.06f), 0.16f, 0.04f);
            Cyl(t, k_Cyan, new Vector3(0, TopY + 0.045f, 0.06f), 0.11f, 0.012f, default, gCyan);
            var beam = Art.Part(t, Art.Cone, Color.white, new Vector3(0, TopY + 0.05f, 0.06f), new Vector3(0.36f, 0.4f, 0.36f), new Vector3(180, 0, 0), false, Art.Ghost(new Color(0.4f, 0.95f, 1f, 0.16f)));
            beam.transform.localPosition = new Vector3(0, TopY + 0.45f, 0.06f); // (upside down: wide at the top)
            var holo = new GameObject("holo").transform;
            holo.SetParent(t, false);
            holo.localPosition = new Vector3(0, TopY + 0.3f, 0.06f);
            Art.Part(holo, Art.Ico, Color.white, Vector3.zero, Vector3.one * 0.1f, new Vector3(20, 0, 30), false, Art.Ghost(new Color(0.4f, 1f, 1f, 0.55f)));
            Art.Part(holo, Art.Cube, Color.white, Vector3.zero, Vector3.one * 0.13f, new Vector3(45, 0, 45), false, Art.Ghost(new Color(1f, 0.45f, 0.9f, 0.3f)));

            // the tower at the back: a tall silver column in a three-ring gyroscope, a big pink orb in a crown of spikes
            float cz = -0.25f;
            Cyl(t, k_Metal, new Vector3(0, TopY + 0.05f, cz), 0.24f, 0.1f);
            Cyl(t, k_Silver, new Vector3(0, TopY + 0.65f, cz), 0.13f, 1.2f);
            for (int i = 0; i < 4; i++) Cyl(t, i % 2 == 0 ? k_SilverDark : k_Pink, new Vector3(0, TopY + 0.3f + i * 0.22f, cz), 0.145f, 0.03f, default, i % 2 == 0 ? null : gPink);
            Cyl(t, k_Metal, new Vector3(0, TopY + 1.26f, cz), 0.22f, 0.06f);
            var gyroAt = new Vector3(0, TopY + 0.7f, cz);
            Ring(t, gyroAt, new Vector3(0, 0, 0), 0.34f, 14, 0.12f, k_Silver, gTeam, 2, "ring");
            Ring(t, gyroAt, new Vector3(70, 0, 0), 0.4f, 14, 0.11f, k_SilverDark, gPink, 2, "ring");
            Ring(t, gyroAt, new Vector3(20, 0, 70), 0.46f, 16, 0.1f, k_Silver, gCyan, 2, "ring");
            for (int i = 0; i < 4; i++)
            {
                var r = Quaternion.Euler(0, 45f + i * 90f, 0);
                Art.Part(t, Art.Cone, k_Silver, new Vector3(0, TopY + 1.28f, cz) + r * new Vector3(0, 0, 0.15f), new Vector3(0.07f, 0.28f, 0.07f), (r * Quaternion.Euler(30, 0, 0)).eulerAngles);
            }
            var orb = Art.Part(t, Art.Ico, k_Pink, new Vector3(0, TopY + 1.55f, cz), Vector3.one * 0.13f, default, false, gPink, "orb").transform;
            Art.Part(orb, Art.Ico, Color.white, Vector3.zero, Vector3.one * 1.7f, default, false, Art.Ghost(new Color(1f, 0.45f, 0.85f, 0.28f)));
            // crystals orbiting the tower
            var orbit = new GameObject("orbit").transform;
            orbit.SetParent(t, false);
            orbit.localPosition = new Vector3(0, TopY + 1.05f, cz);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                var p = new Vector3(Mathf.Sin(a) * 0.55f, (i - 1) * 0.08f, Mathf.Cos(a) * 0.55f);
                var col = i == 1 ? k_Cyan : k_Pink;
                var g = i == 1 ? gCyan : gPink;
                Art.Part(orbit, Art.Cone, col, p, new Vector3(0.07f, 0.09f, 0.07f), default, false, g);
                Art.Part(orbit, Art.Cone, col, p, new Vector3(0.07f, 0.09f, 0.07f), new Vector3(180, 0, 0), false, g);
            }
            // tesla pylons: tall on the back corners (stacked discs, pink tips), short on the front corners (cyan tips)
            for (int k = -1; k <= 1; k += 2)
            {
                var b = new Vector3(k * 0.56f, TopY, -0.28f);
                Cyl(t, k_Metal, b + Vector3.up * 0.45f, 0.04f, 0.9f);
                for (int i = 0; i < 3; i++) Cyl(t, i == 2 ? k_Pink : k_Silver, b + Vector3.up * (0.4f + i * 0.17f), 0.1f - i * 0.02f, 0.025f, default, i == 2 ? gPink : null);
                Art.Part(t, Art.Ico, k_Pink, b + Vector3.up * 0.95f, Vector3.one * 0.07f, default, false, gPink);
                Art.Part(t, Art.Cone, k_Silver, b + Vector3.up * 0.98f, new Vector3(0.06f, 0.2f, 0.06f));
                var f = new Vector3(k * 0.6f, TopY, 0.33f);
                Cyl(t, k_Metal, f + Vector3.up * 0.1f, 0.03f, 0.2f);
                Art.Part(t, Art.Ico, k_Cyan, f + Vector3.up * 0.23f, Vector3.one * 0.045f, default, false, gCyan);
                // emitter arms off the tower, pointing down at the projector
                Rod(t, k_Metal, new Vector3(k * 0.1f, TopY + 0.95f, cz), new Vector3(k * 0.3f, TopY + 0.75f, 0.0f), 0.025f);
                Art.Part(t, Art.Cone, k_Cyan, new Vector3(k * 0.3f, TopY + 0.73f, 0.0f), new Vector3(0.08f, 0.1f, 0.08f), new Vector3(180, 0, -k * 25f), false, gCyan);
            }
            // the tier: two lit pips on the front
            for (int k = -1; k <= 1; k += 2) Art.Part(t, Art.Ico, k_Pink, new Vector3(k * 0.06f, 0.36f, 0.35f), Vector3.one * 0.035f, default, false, gPink);
        }

        /// <summary>Called by the Container when it spawns (kind Workbench / Workbench2): colliders and the idle animation.</summary>
        public static void Setup(Container c, Transform visual, BoxCollider bc)
        {
            bool t2 = c.BenchTier == 2;
            bc.center = new Vector3(0, TopY * 0.5f, 0);
            bc.size = new Vector3(HalfX * 2f, TopY, HalfZ * 2f);
            var back = c.gameObject.AddComponent<BoxCollider>(); // the column at the back
            back.center = new Vector3(0, TopY + (t2 ? 0.7f : 0.5f), -0.26f);
            back.size = new Vector3(t2 ? 1.2f : 1.2f, t2 ? 1.4f : 1.0f, 0.2f);
            var w = c.gameObject.AddComponent<Workbench>();
            foreach (var tr in visual.GetComponentsInChildren<Transform>(true))
            {
                if (tr.name == "ring") w.m_Rings.Add(tr);
                else if (tr.name == "orb") w.m_Orb = tr;
                else if (tr.name == "holo") w.m_Holo = tr;
                else if (tr.name == "orbit") w.m_Orbit = tr;
                else if (tr.name == "light") w.m_Light = tr.GetComponent<Light>();
            }
            if (w.m_Orb) w.m_OrbBase = w.m_Orb.localPosition;
            if (w.m_Holo) w.m_HoloBase = w.m_Holo.localPosition;
            if (w.m_Light) w.m_LightBase = w.m_Light.intensity;
            if (AiPsxArt.On) AiPsxArt.Apply(visual);
        }

        readonly List<Transform> m_Rings = new List<Transform>();
        Transform m_Orb, m_Holo, m_Orbit;
        Vector3 m_OrbBase, m_HoloBase;
        Light m_Light;
        float m_LightBase;

        void Update()
        {
            float now = Time.time, dt = Time.deltaTime;
            for (int i = 0; i < m_Rings.Count; i++)
                if (m_Rings[i]) m_Rings[i].Rotate(0, (i % 2 == 0 ? 70f : -95f) * (1f + i * 0.3f) * dt, 0, Space.Self);
            if (m_Orb)
            {
                m_Orb.localPosition = m_OrbBase + Vector3.up * Mathf.Sin(now * 1.8f) * 0.04f;
                m_Orb.Rotate(10f * dt, 45f * dt, 0, Space.Self);
            }
            if (m_Holo)
            {
                m_Holo.localPosition = m_HoloBase + Vector3.up * Mathf.Sin(now * 2.3f) * 0.025f;
                m_Holo.Rotate(0, 80f * dt, 0, Space.Self);
                m_Holo.localScale = Vector3.one * (0.92f + 0.08f * Mathf.Sin(now * 13f)); // a little hologram flicker
            }
            if (m_Orbit) m_Orbit.Rotate(0, 55f * dt, 0, Space.Self);
            if (m_Light) m_Light.intensity = m_LightBase * (0.85f + 0.15f * Mathf.Sin(now * 2.2f));
        }
    }
}
