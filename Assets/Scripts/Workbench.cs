using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Crafting rules for the two places things are made: the TAB inventory makes only the starter items (Cfg.IsStarter);
    /// everything else is bought at a workbench (Cfg.BenchItems), in this mode's currency.
    /// </summary>
    public static partial class Cfg
    {
        [Tune("Crafting")] public static int WorkbenchWood = 150;
        /// <summary>How long the workbench takes to make something (the sawdust and tool noise), in seconds.</summary>
        [Tune("Crafting")] public static float WorkbenchCraftSeconds = 2.2f;

        /// <summary>What you can craft from the TAB inventory (in this order). Everything else comes from a workbench.</summary>
        static readonly Item[] k_Starter = { Item.Hatchet, Item.Spear, Item.BuildingPlan, Item.Bow, Item.Arrow, Item.Chest, Item.Ram, Item.Barrier, Item.Workbench };
        public static bool IsStarter(Item i) => System.Array.IndexOf(k_Starter, i) >= 0;
        public static IReadOnlyList<Item> StarterOrder => k_Starter;

        /// <summary>
        /// The workbench's items in the order it shows them: weapons, ammo, armour, tools, riding / boats, then base
        /// upgrades (no headings - they're just kept together). Anything craftable that isn't here or a starter goes on the end.
        /// </summary>
        static readonly Item[] k_BenchOrder =
        {
            Item.Crossbow, Item.Sword, Item.Shotgun, Item.Revolver, Item.Pistol, Item.C4,
            Item.ShotgunShell, Item.RevolverAmmo, Item.PistolAmmo,
            Item.Armor, Item.HeavyArmor, Item.Helmet,
            Item.Pickaxe, Item.Chainsaw,
            Item.Saddle, Item.Boat,
            Item.FortifyBuff, Item.WoodGenBuff,
        };

        /// <summary>Bought at the workbench but never lands on it: armour goes straight on, base upgrades just happen.</summary>
        public static bool BenchNoItem(Item i) => i == Item.Armor || i == Item.HeavyArmor || i == Item.FortifyBuff || i == Item.WoodGenBuff;

        /// <summary>An item's craft number (CraftRpc / WorkbenchBuyRpc numbering: recipes, then power items from PowerBase), -1 if it can't be made in this mode.</summary>
        public static int CraftIndexOf(Item id)
        {
            int p = PowerIndex(id);
            if (p >= 0 && p < PowerCount) return PowerBase + p;
            return RecipeIndex(id);
        }

        public static bool ValidCraftIndex(int i) => i >= 0 && (i < PowerBase ? i < RecipeCount : i - PowerBase < PowerCount);

        /// <summary>The recipe behind a craft number (power items priced for the team: fortify and wood gen go up).</summary>
        public static Recipe CraftRecipe(int i, int team) => i >= PowerBase ? GetPowerRecipe(i - PowerBase, team) : GetRecipe(i);

        /// <summary>What the workbench sells in this mode, as craft numbers, in display order.</summary>
        public static void BenchItems(List<int> into)
        {
            into.Clear();
            foreach (var id in k_BenchOrder)
            {
                int i = CraftIndexOf(id);
                if (i >= 0 && !into.Contains(i)) into.Add(i);
            }
            for (int i = 0; i < RecipeCount; i++)
                if (!IsStarter(GetRecipe(i).Output) && !into.Contains(i)) into.Add(i);
            for (int i = 0; i < PowerCount; i++)
                if (!into.Contains(PowerBase + i)) into.Add(PowerBase + i);
        }
    }

    /// <summary>
    /// The workbench: a wooden alien machine (planks, a carved dome with antennae, a glowing eye) crafted from the
    /// inventory and placed on your own team's bedrock. It's a Container (kind Workbench) with no slots that can't be
    /// broken. E opens its shop (Hud.Crafting.cs); buying something makes it right there for everyone to see - sawdust
    /// pours out, a saw and a hammer go, and when the dust clears the item is lying in the middle of the bench (a normal
    /// dropped item, so anyone can pick it up with E). Armour and base upgrades just happen, with a fitting noise.
    /// </summary>
    public class Workbench : MonoBehaviour
    {
        // ------------------------------------------------------------------ shape (shared by the model, the ghost, the rules)

        /// <summary>Half the footprint (x across, z front to back) and the height of the bench top.</summary>
        public const float HalfX = 0.65f, HalfZ = 0.38f, TopY = 0.9f;

        static readonly Dictionary<Container, Workbench> s_Of = new Dictionary<Container, Workbench>();
        public static Workbench Of(Container c) => c != null && s_Of.TryGetValue(c, out var w) ? w : null;

        /// <summary>A team's workbench (null if it hasn't placed one).</summary>
        public static Container ForTeam(int team)
        {
            foreach (var c in Container.All)
                if (c != null && c.IsSpawned && c.IsWorkbench && c.Team.Value == team) return c;
            return null;
        }

        /// <summary>The middle of the bench top, where finished things are put.</summary>
        public static Vector3 Top(Container c) => c.transform.TransformPoint(new Vector3(0, TopY, 0.04f));

        /// <summary>Why a workbench can't go here (null = it can). Called from PlayerNet.DeployProblem.</summary>
        public static string PlaceProblem(int team, Vector3 pos, float yaw)
        {
            if (ForTeam(team) != null) return "Your team already has a workbench";
            if (Cfg.Builder) return null; // Builder: no bases, no bedrock - anywhere
            var rot = Quaternion.Euler(0, yaw, 0);
            var bc = Cfg.BedrockCenter(team);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var p = pos + rot * new Vector3(sx * HalfX, 0, sz * HalfZ);
                    if (Mathf.Abs(p.x - bc.x) > Cfg.BedrockHalf || Mathf.Abs(p.z - bc.z) > Cfg.BedrockHalf)
                        return "The workbench only goes on the metal floor in the middle of your base";
                }
            if (Mathf.Abs(pos.y - Cfg.BaseY) > 0.35f) return "Put it down on the metal floor";
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

        /// <summary>A sensible spot for a team's workbench: the front corner of the bedrock, facing in (AutoTest).</summary>
        public static Vector3 DefaultPos(int team)
        {
            var back = Cfg.BackDir(team);
            var left = -Vector3.Cross(Vector3.up, back);
            return Cfg.BedrockCenter(team) - back * 2.4f + left * 1.85f + Vector3.up * Cfg.BaseY;
        }
        public static float DefaultYaw(int team) => Quaternion.LookRotation(Cfg.BackDir(team)).eulerAngles.y;

        // ------------------------------------------------------------------ server

        static readonly Dictionary<Container, double> s_BusyUntil = new Dictionary<Container, double>();

        /// <summary>Server: something is being made on it right now.</summary>
        public static bool ServerBusy(Container c) => c != null && s_BusyUntil.TryGetValue(c, out var u) && NetworkManager.Singleton != null && NetworkManager.Singleton.ServerTime.Time < u;

        /// <summary>How long making this takes (Builder: at least its usual craft time).</summary>
        public static float CraftSeconds(Recipe r) => Mathf.Max(0.3f, Mathf.Max(Cfg.WorkbenchCraftSeconds, Cfg.CraftSeconds(r)));

        /// <summary>Server: put a workbench down (already checked and paid for).</summary>
        public static Container ServerSpawn(int team, Vector3 pos, float yaw)
        {
            var go = Instantiate(Bootstrap.I.containerPrefab, pos, Quaternion.Euler(0, yaw, 0));
            var c = go.GetComponent<Container>();
            c.ServerInit(Container.Workbench, team, 0, null);
            go.GetComponent<NetworkObject>().Spawn(true);
            return c;
        }

        /// <summary>Server: start making an item (paid already). Everyone sees the sawdust; it lands on the bench at the end.</summary>
        public static void ServerStart(Container c, PlayerNet p, Recipe r)
        {
            float secs = CraftSeconds(r);
            s_BusyUntil[c] = NetworkManager.Singleton.ServerTime.Time + secs + 0.15f;
            p.WorkbenchCraftRpc(c.NetworkObject, (byte)r.Output, secs);
            c.StartCoroutine(ServerFinish(c, p, r, secs));
        }

        static IEnumerator ServerFinish(Container c, PlayerNet p, Recipe r, float secs)
        {
            yield return new WaitForSeconds(secs);
            var g = NetGame.Instance;
            if (c == null || !c.IsSpawned || g == null) yield break;
            int team = c.Team.Value;
            // saddles are in the team colour; guns come empty
            int data = r.Output == Item.Saddle ? team + 1 : r.Output == Item.Revolver || r.Output == Item.Shotgun ? 0 : Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255);
            var at = Top(c) + Vector3.up * 0.06f;
            g.ServerDropItem(ItemStack.Of(r.Output, r.Count, data), at, c.transform.right, at); // from = where it rests: no toss, it's just there
            if (p != null && p.IsSpawned) p.NotifyPublic($"Your {r.Name} is ready - pick it up off the workbench");
        }

        // ------------------------------------------------------------------ the look

        static readonly Color k_Glow = new Color(0.45f, 1f, 0.55f);
        static readonly Color k_Pale = new Color(0.7f, 0.52f, 0.31f);
        static readonly Color[] k_Dust = { new Color(0.86f, 0.72f, 0.5f), new Color(0.78f, 0.62f, 0.4f), new Color(0.93f, 0.83f, 0.62f), new Color(0.66f, 0.5f, 0.31f) };

        /// <summary>
        /// The wooden alien workbench, 1.3 x 0.76 m with its top at TopY, front (+z) towards whoever works at it.
        /// `full` adds the light (not for ghosts and icons). Parts the animation needs are named: saw, arm, orb, light.
        /// </summary>
        public static void BuildModel(Transform parent, int team, bool full)
        {
            var t = new GameObject("workbench").transform;
            t.SetParent(parent, false);
            Color wood = Art.Wood, dark = Art.DarkWood, pale = k_Pale, glow = k_Glow;
            var teamCol = Cfg.TeamColor[Mathf.Clamp(team, 0, 3)];

            // four splayed alien legs, jointed at the knee, standing on glowing pads
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Art.Box(t, dark, new Vector3(sx * 0.53f, 0.62f, sz * 0.26f), new Vector3(0.09f, 0.5f, 0.09f), new Vector3(-sz * 6f, 0, sx * 6f));
                    Art.Box(t, dark, new Vector3(sx * 0.58f, 0.22f, sz * 0.3f), new Vector3(0.08f, 0.42f, 0.08f), new Vector3(sz * 10f, 0, -sx * 10f));
                    Art.Part(t, Art.Sphere, wood, new Vector3(sx * 0.555f, 0.41f, sz * 0.28f), Vector3.one * 0.11f); // knee
                    Art.Part(t, Art.Sphere, glow, new Vector3(sx * 0.6f, 0.03f, sz * 0.32f), new Vector3(0.14f, 0.06f, 0.14f));
                }
            // lower shelf with a couple of offcuts on it
            Art.Box(t, wood, new Vector3(0, 0.3f, 0), new Vector3(1.1f, 0.05f, 0.56f));
            Art.Box(t, pale, new Vector3(-0.25f, 0.35f, 0.05f), new Vector3(0.4f, 0.05f, 0.12f), new Vector3(0, 12, 0));
            Art.Box(t, wood, new Vector3(0.3f, 0.36f, -0.08f), new Vector3(0.12f, 0.07f, 0.3f), new Vector3(0, -8, 0));

            // the top: planks running across, darker trim round the edge
            for (int i = 0; i < 4; i++)
                Art.Box(t, i % 2 == 0 ? wood : pale, new Vector3(0, TopY - 0.035f, -0.28f + i * 0.187f), new Vector3(1.3f, 0.07f, 0.18f));
            Art.Box(t, dark, new Vector3(0, TopY - 0.05f, 0.38f), new Vector3(1.34f, 0.1f, 0.04f));
            Art.Box(t, dark, new Vector3(0, TopY - 0.05f, -0.38f), new Vector3(1.34f, 0.1f, 0.04f));
            for (int k = -1; k <= 1; k += 2) Art.Box(t, dark, new Vector3(k * 0.665f, TopY - 0.05f, 0), new Vector3(0.04f, 0.1f, 0.8f));
            // the apron under the front edge: carved runes that glow, and a strip in the team colour
            Art.Box(t, dark, new Vector3(0, TopY - 0.18f, 0.36f), new Vector3(1.2f, 0.17f, 0.03f));
            for (int i = -3; i <= 3; i++)
                Art.Box(t, glow, new Vector3(i * 0.15f, TopY - 0.17f, 0.377f), new Vector3(0.045f, i % 2 == 0 ? 0.09f : 0.045f, 0.008f), new Vector3(0, 0, i * 15f));
            Art.Box(t, teamCol, new Vector3(0, TopY - 0.27f, 0.36f), new Vector3(0.6f, 0.03f, 0.03f));

            // the back: two bowed posts and a crossbar (the alien machine's arch, in wood), a back board with a round
            // porthole and a glowing eye in it
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Box(t, dark, new Vector3(k * 0.6f, TopY + 0.42f, -0.31f), new Vector3(0.1f, 0.88f, 0.1f), new Vector3(0, 0, k * 5f));
                Art.Part(t, Art.Cone, wood, new Vector3(k * 0.64f, TopY + 0.9f, -0.31f), new Vector3(0.12f, 0.16f, 0.12f), new Vector3(0, 0, -k * 15f));
            }
            Art.Box(t, wood, new Vector3(0, TopY + 0.84f, -0.31f), new Vector3(1.22f, 0.1f, 0.14f));
            Art.Box(t, pale, new Vector3(0, TopY + 0.38f, -0.34f), new Vector3(1.04f, 0.68f, 0.04f));
            for (int i = 0; i < 4; i++) Art.Box(t, dark, new Vector3(-0.39f + i * 0.26f, TopY + 0.38f, -0.315f), new Vector3(0.015f, 0.66f, 0.01f)); // plank lines
            Art.Part(t, Art.Cylinder, dark, new Vector3(0, TopY + 0.4f, -0.31f), new Vector3(0.44f, 0.03f, 0.44f), new Vector3(90, 0, 0));
            Art.Part(t, Art.Cylinder, new Color(0.12f, 0.08f, 0.05f), new Vector3(0, TopY + 0.4f, -0.29f), new Vector3(0.34f, 0.02f, 0.34f), new Vector3(90, 0, 0));
            Art.Part(t, Art.Ico, glow, new Vector3(0, TopY + 0.4f, -0.25f), Vector3.one * 0.2f, default, false, null, "orb");
            // tool pegs on the back board: a little saw and a mallet hanging up
            Art.Box(t, Art.Metal, new Vector3(-0.36f, TopY + 0.3f, -0.3f), new Vector3(0.16f, 0.08f, 0.01f), new Vector3(0, 0, 20));
            Art.Box(t, dark, new Vector3(-0.27f, TopY + 0.34f, -0.3f), new Vector3(0.05f, 0.05f, 0.02f), new Vector3(0, 0, 20));
            Art.Box(t, wood, new Vector3(0.34f, TopY + 0.3f, -0.3f), new Vector3(0.03f, 0.22f, 0.02f));
            Art.Box(t, dark, new Vector3(0.34f, TopY + 0.43f, -0.3f), new Vector3(0.12f, 0.06f, 0.05f));

            // a carved wooden dome on top with two antennae, tipped with glowing bulbs
            Art.Part(t, Art.Sphere, wood, new Vector3(0, TopY + 0.9f, -0.31f), new Vector3(0.52f, 0.3f, 0.3f));
            Art.Part(t, Art.Sphere, dark, new Vector3(0, TopY + 0.89f, -0.31f), new Vector3(0.56f, 0.06f, 0.34f));
            for (int k = -1; k <= 1; k += 2)
            {
                Art.Part(t, Art.Cylinder, dark, new Vector3(k * 0.14f, TopY + 1.12f, -0.31f), new Vector3(0.025f, 0.16f, 0.025f), new Vector3(0, 0, -k * 22f));
                Art.Part(t, Art.Ico, glow, new Vector3(k * 0.2f, TopY + 1.27f, -0.31f), Vector3.one * 0.075f);
            }
            for (int k = -1; k <= 1; k += 2) Art.Part(t, Art.Sphere, glow, new Vector3(k * 0.12f, TopY + 0.94f, -0.17f), new Vector3(0.06f, 0.035f, 0.02f)); // its eyes

            // a round saw blade standing up through the left of the top (spins while it works)
            var saw = new GameObject("saw").transform;
            saw.SetParent(t, false);
            saw.localPosition = new Vector3(-0.48f, TopY + 0.02f, 0.02f);
            Art.Part(saw, Art.Cylinder, Art.Metal * 1.3f, Vector3.zero, new Vector3(0.3f, 0.008f, 0.3f), new Vector3(0, 0, 90));
            for (int i = 0; i < 8; i++)
            {
                var r = Quaternion.Euler(i * 45f, 0, 0);
                Art.Box(saw, Art.Metal * 0.8f, r * new Vector3(0, 0.15f, 0), new Vector3(0.012f, 0.03f, 0.03f), r.eulerAngles);
            }
            Art.Part(saw, Art.Cylinder, glow, Vector3.zero, new Vector3(0.06f, 0.012f, 0.06f), new Vector3(0, 0, 90));
            Art.Box(t, dark, new Vector3(-0.48f, TopY + 0.005f, 0.02f), new Vector3(0.06f, 0.02f, 0.34f)); // its slot

            // a jointed wooden arm off the right post with a mallet on the end (it hammers while it works)
            var arm = new GameObject("arm").transform;
            arm.SetParent(t, false);
            arm.localPosition = new Vector3(0.52f, TopY + 0.62f, -0.22f);
            Art.Part(arm, Art.Sphere, wood, Vector3.zero, Vector3.one * 0.1f);
            Art.Box(arm, dark, new Vector3(-0.2f, -0.06f, 0.1f), new Vector3(0.44f, 0.05f, 0.05f), new Vector3(0, -27f, -16f));
            Art.Part(arm, Art.Sphere, glow, new Vector3(-0.39f, -0.12f, 0.2f), Vector3.one * 0.06f);
            Art.Box(arm, dark, new Vector3(-0.39f, -0.24f, 0.2f), new Vector3(0.04f, 0.22f, 0.04f));
            Art.Box(arm, pale, new Vector3(-0.39f, -0.37f, 0.2f), new Vector3(0.16f, 0.09f, 0.09f));

            if (full)
            {
                var lg = new GameObject("light");
                lg.transform.SetParent(t, false);
                lg.transform.localPosition = new Vector3(0, TopY + 0.45f, 0.3f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = glow;
                l.range = 3.2f;
                l.intensity = 1.1f;
                l.shadows = LightShadows.None;
            }
        }

        /// <summary>Called by the Container when it spawns (kind Workbench): colliders and the animation.</summary>
        public static void Setup(Container c, Transform visual, BoxCollider bc)
        {
            bc.center = new Vector3(0, TopY * 0.5f, 0);
            bc.size = new Vector3(HalfX * 2f, TopY, HalfZ * 2f);
            var back = c.gameObject.AddComponent<BoxCollider>(); // the back board and the dome
            back.center = new Vector3(0, TopY + 0.5f, -0.31f);
            back.size = new Vector3(1.3f, 1.0f, 0.16f);
            var w = c.gameObject.AddComponent<Workbench>();
            w.m_Box = c;
            s_Of[c] = w;
            w.m_Saw = Find(visual, "saw");
            w.m_Arm = Find(visual, "arm");
            w.m_Orb = Find(visual, "orb");
            var lt = Find(visual, "light");
            if (lt != null) w.m_Light = lt.GetComponent<Light>();
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        Container m_Box;
        Transform m_Saw, m_Arm, m_Orb;
        Light m_Light;
        float m_T0 = -100f, m_Dur, m_SawSpin, m_SawAngle, m_NextSound, m_Flash = -100f;
        bool m_Playing, m_Dinged;
        readonly List<Transform> m_Cloud = new List<Transform>();
        readonly List<Material> m_CloudMats = new List<Material>();
        readonly List<Vector3> m_CloudOff = new List<Vector3>();

        /// <summary>Clients: it's making something (or false: nothing's on its way).</summary>
        public bool Working => m_Playing && Time.time - m_T0 < m_Dur;

        void OnDestroy()
        {
            if (m_Box != null) { s_Of.Remove(m_Box); s_BusyUntil.Remove(m_Box); }
            foreach (var m in m_CloudMats) if (m) Destroy(m);
            foreach (var c in m_Cloud) if (c) Destroy(c.gameObject);
        }

        /// <summary>Everyone: sawdust, a saw and a hammer for `secs`; the item appears on the bench as the dust clears.</summary>
        public void Play(Item item, float secs)
        {
            m_T0 = Time.time;
            m_Dur = Mathf.Max(0.3f, secs);
            m_Playing = true;
            m_Dinged = false;
            m_NextSound = 0f;
            EnsureCloud();
        }

        /// <summary>Everyone: armour / a base upgrade bought here - just the noise (and the eye flashes).</summary>
        public void PlayNoItem(Item item)
        {
            var at = Top(m_Box) + Vector3.up * 0.3f;
            switch (item)
            {
                case Item.Armor:
                case Item.HeavyArmor: Sfx.Play(Sfx.ArmorClank, at, 0.9f, 0.05f, 45f); break;
                case Item.FortifyBuff: Sfx.Play(Sfx.StoneGrind, at, 1f, 0.04f, 70f); break;
                case Item.WoodGenBuff: Sfx.Play(Sfx.Engine, at, 0.9f, 0.04f, 60f); break;
                default: Sfx.Play(Sfx.Ding, at, 0.7f); break;
            }
            m_Flash = Time.time;
        }

        void EnsureCloud()
        {
            if (m_Cloud.Count > 0) return;
            for (int i = 0; i < 12; i++)
            {
                var c = k_Dust[i % k_Dust.Length];
                var mat = new Material(Art.Ghost(new Color(c.r, c.g, c.b, 0.9f)));
                var go = Art.Part(null, Art.Ico, c, Vector3.zero, Vector3.zero, Random.rotation.eulerAngles, false, mat, "sawdustCloud");
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                m_Cloud.Add(go.transform);
                m_CloudMats.Add(mat);
                float a = i * 2.39996f;
                float r = i == 0 ? 0f : 0.16f + 0.05f * (i % 4);
                m_CloudOff.Add(new Vector3(Mathf.Cos(a) * r * 1.5f, 0.1f + 0.06f * (i % 3), Mathf.Sin(a) * r * 0.8f));
            }
        }

        void Update()
        {
            if (m_Box == null) return;
            float now = Time.time, dt = Time.deltaTime;
            float t = now - m_T0;
            bool working = m_Playing && t < m_Dur;

            // the eye breathes; it burns bright while it works (or when something was just bought)
            float flash = Mathf.Clamp01(1f - (now - m_Flash) / 0.8f);
            float glowK = working ? 1.6f + 0.5f * Mathf.Sin(now * 18f) : 1f + 0.15f * Mathf.Sin(now * 2f) + flash;
            if (m_Orb) { m_Orb.localScale = Vector3.one * 0.2f * (0.9f + 0.1f * glowK); m_Orb.Rotate(0, 40f * dt * glowK, 0, Space.Self); }
            if (m_Light) m_Light.intensity = 1.1f * glowK;

            // the saw spins up while it works and runs down after
            m_SawSpin = Mathf.MoveTowards(m_SawSpin, working ? 1400f : 0f, dt * (working ? 2500f : 700f));
            m_SawAngle += m_SawSpin * dt;
            if (m_Saw) m_Saw.localRotation = Quaternion.Euler(m_SawAngle, 0, 0);
            // the mallet comes down on the middle of the bench, three times a second
            if (m_Arm)
            {
                float hit = working ? Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) : 0f;
                m_Arm.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-18f, 22f, hit));
            }

            if (!m_Playing) return;
            var top = Top(m_Box);
            if (working)
            {
                // the noise: the saw-and-hammer clip, again for longer jobs (Builder)
                if (t >= m_NextSound && m_Dur - t > 0.6f)
                {
                    Sfx.Play(Sfx.Workshop, top + Vector3.up * 0.2f, 0.95f, 0.04f, 45f);
                    m_NextSound += Sfx.Workshop != null ? Sfx.Workshop.length : 2.2f;
                }
                // sawdust: chips flying out of the saw and off the top, and puffs
                int n = Mathf.Max(1, Mathf.RoundToInt(dt * 90f));
                for (int i = 0; i < n; i++)
                {
                    bool fromSaw = Random.value < 0.4f;
                    var from = fromSaw && m_Saw ? m_Saw.position + Vector3.up * 0.12f : top + new Vector3(Random.Range(-0.3f, 0.3f), 0.15f, Random.Range(-0.15f, 0.15f));
                    var dir = (Random.insideUnitSphere + Vector3.up * 1.1f + (fromSaw ? m_Box.transform.forward * 0.6f : Vector3.zero)).normalized;
                    FxParticle.Spawn(from, dir * Random.Range(1.2f, 3.2f), k_Dust[Random.Range(0, k_Dust.Length)], Random.Range(0.025f, 0.055f), Random.Range(0.6f, 1.2f), 6f, false);
                }
                if (Random.value < dt * 14f)
                    FxParticle.Puff(top + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.1f, 0.5f), Random.Range(-0.2f, 0.25f)), new Color(0.88f, 0.76f, 0.55f, 0.55f), Random.Range(0.4f, 0.8f));
            }
            else if (!m_Dinged)
            {
                m_Dinged = true;
                Sfx.Play(Sfx.Ding, top, 0.6f);
            }

            // the dust cloud over the middle of the bench: it builds up while it works (hiding what's being made), then
            // swells and thins away to show the finished item
            const float Fade = 1.1f;
            float grow = Mathf.Clamp01(t / 0.5f);
            float fade = Mathf.Clamp01((t - m_Dur) / Fade);
            for (int i = 0; i < m_Cloud.Count; i++)
            {
                var c = m_Cloud[i];
                if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
                var off = m_Box.transform.rotation * m_CloudOff[i];
                float wob = Mathf.Sin(now * (2.2f + i * 0.37f) + i) * 0.04f;
                c.position = top + off * (1f + fade * 0.6f) + Vector3.up * (wob + fade * 0.35f);
                c.Rotate(15f * dt, 30f * dt, 0, Space.Self);
                float size = (i == 0 ? 0.55f : 0.36f + 0.04f * (i % 3)) * grow * (1f + fade * 0.7f) * (1f + 0.08f * Mathf.Sin(now * 5f + i));
                c.localScale = Vector3.one * size;
                var col = k_Dust[i % k_Dust.Length];
                col.a = 0.92f * (1f - fade);
                m_CloudMats[i].SetColor("_BaseColor", col);
            }
            if (fade >= 1f)
            {
                m_Playing = false;
                foreach (var c in m_Cloud) c.gameObject.SetActive(false);
            }
        }
    }
}
