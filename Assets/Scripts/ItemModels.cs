using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Low-poly item models, used for the first-person hands, the third-person hand and inventory icons.
    /// Convention: tools are built along +Y with the grip at the origin.
    /// </summary>
    public static class ItemModels
    {
        public static readonly Color Berry = new Color(0.75f, 0.08f, 0.2f), Leaf = new Color(0.25f, 0.5f, 0.2f), Twine = new Color(0.8f, 0.7f, 0.5f);

        /// <summary>The darker, harder wood the hatchet's blade and the spear / arrow points are carved from (they used to be stone).</summary>
        public static readonly Color HardWood = new Color(0.24f, 0.15f, 0.08f);
        /// <summary>The hatchet's blade: the hard wood a little lighter and greyer (in the hard wood's own red-brown it
        /// looked like a bronze axe head; a pale weathered grey-brown now, well clear of bronze).</summary>
        public static readonly Color HatchetHead = new Color(0.52f, 0.48f, 0.43f);

        /// <summary>The up arrow on the base upgrades' models (Fortify, Wood Gen): the orangey upgrade colour, or - while
        /// ItemIcons renders a team's copy of their icons for the UPGRADES screen - a light shade of that team's colour.</summary>
        internal static Color? ArrowTint;
        static Color UpgradeArrow => ArrowTint ?? UpgradeStation.Green;

        static Mesh s_Leaf;
        /// <summary>
        /// A leaf, 1 long along +Z from its stalk end: pointed at both ends and widest a third of the way up, folded into a
        /// shallow V along the midrib and curling up towards the tip. Two-sided (the back has its own faces).
        /// </summary>
        public static Mesh LeafMesh
        {
            get
            {
                if (s_Leaf != null) return s_Leaf;
                const int N = 10;
                var v = new System.Collections.Generic.List<Vector3>();
                for (int i = 0; i <= N; i++)
                {
                    float z = i / (float)N;
                    float w = 0.36f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(z, 0.75f))), 0.9f); // (sin(pi) is a hair below 0 in floats: no NaN tip)
                    float curl = 0.18f * z * z;
                    v.Add(new Vector3(-w, curl + w * 0.35f, z)); // left edge (raised: the fold)
                    v.Add(new Vector3(0, curl, z));              // midrib
                    v.Add(new Vector3(w, curl + w * 0.35f, z));  // right edge
                }
                var tris = new System.Collections.Generic.List<int>();
                for (int i = 0; i < N; i++)
                {
                    int a = i * 3, b = (i + 1) * 3;
                    tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1, a + 1, b + 1, a + 2, a + 2, b + 1, b + 2 });
                }
                // the back: the same vertices again, faces turned round
                int n = v.Count, tn = tris.Count;
                v.AddRange(v.ToArray());
                for (int i = 0; i < tn; i += 3) tris.AddRange(new[] { tris[i] + n, tris[i + 2] + n, tris[i + 1] + n });
                s_Leaf = new Mesh { name = "Leaf" };
                s_Leaf.SetVertices(v);
                s_Leaf.SetTriangles(tris, 0);
                s_Leaf.RecalculateNormals();
                s_Leaf.RecalculateBounds();
                return s_Leaf;
            }
        }

        public static GameObject Create(Item item, Transform parent)
        {
            var root = new GameObject(item.ToString());
            root.transform.SetParent(parent, false);
            var t = root.transform;
            switch (item)
            {
                case Item.Rock:
                    Art.Part(t, Art.MakeRock(3, 0.25f), Art.Stone, new Vector3(0, 0.02f, 0.04f), new Vector3(0.15f, 0.12f, 0.14f));
                    break;
                case Item.BuildingPlan:
                    Art.Box(t, new Color(0.35f, 0.55f, 0.95f), new Vector3(0, 0.1f, 0.05f), new Vector3(0.32f, 0.42f, 0.02f), new Vector3(-30, 0, 0));
                    Art.Box(t, Color.white, new Vector3(0, 0.12f, 0.04f), new Vector3(0.22f, 0.02f, 0.025f), new Vector3(-30, 0, 0));
                    Art.Box(t, Color.white, new Vector3(0, 0.05f, 0.08f), new Vector3(0.22f, 0.02f, 0.025f), new Vector3(-30, 0, 0));
                    Art.Part(t, Art.Cylinder, new Color(0.8f, 0.8f, 0.7f), new Vector3(0, 0.33f, -0.07f), new Vector3(0.05f, 0.17f, 0.05f), new Vector3(0, 0, 90));
                    break;
                case Item.Hatchet:
                {
                    // all wood: a light handle and one big flat blade carved from a darker, harder wood sticking out
                    // forward (where the stone used to be), its edge whittled a shade paler, a dark wood collar and grip
                    var hard = HardWood;
                    Art.Box(t, new Color(0.72f, 0.55f, 0.32f), new Vector3(0, 0.25f, 0), new Vector3(0.05f, 0.62f, 0.05f));
                    Art.Box(t, hard * 0.85f, new Vector3(0, 0.02f, 0), new Vector3(0.06f, 0.14f, 0.06f));
                    Art.Box(t, HatchetHead, new Vector3(0, 0.5f, 0.12f), new Vector3(0.035f, 0.2f, 0.2f));
                    Art.Box(t, HatchetHead * 1.3f, new Vector3(0, 0.5f, 0.23f), new Vector3(0.03f, 0.26f, 0.04f));
                    Art.Box(t, hard * 0.85f, new Vector3(0, 0.5f, 0.0f), new Vector3(0.07f, 0.1f, 0.07f));
                    break;
                }
                case Item.Pickaxe:
                {
                    // dark handle, a long curved double-pointed iron head (a T), red cloth wrap
                    var iron = new Color(0.5f, 0.5f, 0.55f);
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.25f, 0), new Vector3(0.05f, 0.64f, 0.05f));
                    Art.Box(t, iron, new Vector3(0, 0.56f, 0.12f), new Vector3(0.05f, 0.06f, 0.26f), new Vector3(18, 0, 0));
                    Art.Box(t, iron, new Vector3(0, 0.56f, -0.12f), new Vector3(0.05f, 0.06f, 0.26f), new Vector3(-18, 0, 0));
                    Art.Part(t, Art.Cone, iron * 1.2f, new Vector3(0, 0.52f, 0.25f), new Vector3(0.05f, 0.1f, 0.05f), new Vector3(110, 0, 0));
                    Art.Part(t, Art.Cone, iron * 1.2f, new Vector3(0, 0.52f, -0.25f), new Vector3(0.05f, 0.1f, 0.05f), new Vector3(-110, 0, 0));
                    Art.Box(t, new Color(0.75f, 0.15f, 0.12f), new Vector3(0, 0.56f, 0), new Vector3(0.075f, 0.1f, 0.075f));
                    break;
                }
                case Item.Spear:
                {
                    // all wood: a long pale shaft with carved rings, a big leaf blade of darker, harder wood (where the
                    // stone point used to be) with two barbs under it and a dark wood collar
                    var hard = HardWood;
                    Art.Box(t, new Color(0.62f, 0.45f, 0.26f), new Vector3(0, 0.3f, 0), new Vector3(0.04f, 1.5f, 0.04f));
                    for (int k = 0; k < 3; k++) Art.Box(t, Art.DarkWood, new Vector3(0, -0.2f + k * 0.35f, 0), new Vector3(0.05f, 0.04f, 0.05f));
                    Art.Part(t, Art.Cone, hard, new Vector3(0, 0.93f, 0), new Vector3(0.13f, 0.35f, 0.05f));
                    Art.Box(t, hard * 1.3f, new Vector3(0.04f, 0.86f, 0), new Vector3(0.02f, 0.14f, 0.05f), new Vector3(0, 0, -25));
                    Art.Box(t, hard * 1.3f, new Vector3(-0.04f, 0.86f, 0), new Vector3(0.02f, 0.14f, 0.05f), new Vector3(0, 0, 25));
                    Art.Box(t, hard * 0.8f, new Vector3(0, 0.92f, 0), new Vector3(0.06f, 0.06f, 0.06f));
                    break;
                }
                case Item.Bow:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.2f, 0.06f), new Vector3(0.04f, 0.4f, 0.04f), new Vector3(-20, 0, 0));
                    Art.Box(t, Art.Wood, new Vector3(0, -0.2f, 0.06f), new Vector3(0.04f, 0.4f, 0.04f), new Vector3(20, 0, 0));
                    Art.Box(t, Twine, new Vector3(0, 0, 0.06f), new Vector3(0.05f, 0.1f, 0.05f));
                    Art.Box(t, new Color(0.9f, 0.9f, 0.85f), new Vector3(0, 0, -0.02f), new Vector3(0.01f, 0.75f, 0.01f));
                    break;
                case Item.Ram:
                    // a heavy log carried in both hands, iron-capped at the front (+Z)
                    Art.Part(t, Art.Cylinder, Art.Wood, new Vector3(0, 0, 0.1f), new Vector3(0.24f, 0.6f, 0.24f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, 0, 0.68f), new Vector3(0.28f, 0.06f, 0.28f), new Vector3(90, 0, 0));
                    Art.Box(t, Art.Metal, new Vector3(0, 0, 0.76f), new Vector3(0.2f, 0.2f, 0.1f));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, 0, 0.3f), new Vector3(0.26f, 0.03f, 0.26f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, 0, -0.2f), new Vector3(0.26f, 0.03f, 0.26f), new Vector3(90, 0, 0));
                    break;
                case Item.Chest:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.08f, 0), new Vector3(0.36f, 0.17f, 0.2f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.19f, 0), new Vector3(0.38f, 0.05f, 0.22f));
                    Art.Box(t, Art.Metal, new Vector3(-0.12f, 0.11f, 0), new Vector3(0.02f, 0.23f, 0.23f));
                    Art.Box(t, Art.Metal, new Vector3(0.12f, 0.11f, 0), new Vector3(0.02f, 0.23f, 0.23f));
                    Art.Box(t, new Color(0.85f, 0.7f, 0.25f), new Vector3(0, 0.15f, 0.11f), new Vector3(0.04f, 0.05f, 0.02f));
                    break;
                case Item.Workbench:
                case Item.Workbench2:
                {
                    // the workbench in miniature
                    var mini = new GameObject("mini").transform;
                    mini.SetParent(t, false);
                    mini.localScale = Vector3.one * (item == Item.Workbench2 ? 0.2f : 0.26f);
                    Workbench.BuildModel(mini, 0, false, item == Item.Workbench2 ? 2 : 1);
                    break;
                }
                case Item.Ladder:
                {
                    // a short, chunky ladder: two dark rails, five pale rungs (reads at a glance in the icon)
                    for (int s = -1; s <= 1; s += 2) Art.Box(t, Art.DarkWood, new Vector3(s * 0.11f, 0.3f, 0), new Vector3(0.05f, 0.62f, 0.05f));
                    for (int r = 0; r < 5; r++) Art.Box(t, new Color(0.78f, 0.58f, 0.32f), new Vector3(0, 0.06f + r * 0.12f, 0), new Vector3(0.2f, 0.035f, 0.04f));
                    break;
                }
                case Item.SleepingBag:
                {
                    // a rolled-up sleeping bag in the team colour, tied with two dark straps, the pale lining showing at the end
                    var tc = Cfg.TeamColor[PlayerNet.Local != null ? Mathf.Clamp(PlayerNet.Local.Team.Value, 0, 3) : 0];
                    var cloth = Color.Lerp(tc, new Color(0.35f, 0.3f, 0.25f), 0.3f);
                    Art.Part(t, Art.Cylinder, cloth, new Vector3(0, 0.12f, 0), new Vector3(0.12f, 0.17f, 0.12f), new Vector3(0, 0, 90)); // (the cylinder mesh is radius 1)
                    Art.Part(t, Art.Cylinder, new Color(0.92f, 0.9f, 0.84f), new Vector3(0.172f, 0.12f, 0), new Vector3(0.095f, 0.004f, 0.095f), new Vector3(0, 0, 90));
                    Art.Part(t, Art.Cylinder, cloth * 0.75f, new Vector3(0.176f, 0.12f, 0), new Vector3(0.035f, 0.005f, 0.035f), new Vector3(0, 0, 90));
                    for (int s = -1; s <= 1; s += 2) Art.Part(t, Art.Cylinder, new Color(0.15f, 0.12f, 0.1f), new Vector3(s * 0.08f, 0.12f, 0), new Vector3(0.126f, 0.012f, 0.126f), new Vector3(0, 0, 90));
                    break;
                }
                case Item.BearTrap:
                {
                    // a bear trap laid open: a round steel plate, two jaws flat out to the sides with big pale teeth, a
                    // trigger plate in the middle, a chain
                    var steel = new Color(0.45f, 0.46f, 0.5f);
                    float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
                    Art.Part(t, Art.Cylinder, steel * 0.75f, new Vector3(0, 0.01f, 0), new Vector3(0.2f / cr, 0.02f / ch, 0.2f / cr));
                    Art.Part(t, Art.Cylinder, new Color(0.85f, 0.25f, 0.2f), new Vector3(0, 0.025f, 0), new Vector3(0.09f / cr, 0.012f / ch, 0.09f / cr));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Art.Box(t, steel, new Vector3(s * 0.15f, 0.02f, 0), new Vector3(0.025f, 0.03f, 0.3f));
                        for (int k = -2; k <= 2; k++) Art.Box(t, new Color(0.92f, 0.9f, 0.85f), new Vector3(s * 0.13f, 0.045f, k * 0.055f), new Vector3(0.02f, 0.045f, 0.022f), new Vector3(0, 0, s * 25f));
                    }
                    for (int k = 0; k < 3; k++) Art.Part(t, Art.Sphere, steel * 0.6f, new Vector3(0, 0.015f, -0.13f - k * 0.04f), new Vector3(0.03f, 0.02f, 0.04f));
                    break;
                }
                case Item.AutoTurret:
                {
                    // the auto turret: a small copy of the real thing (Container.Deployables.cs)
                    var mini = new GameObject("mini").transform;
                    mini.SetParent(t, false);
                    Deployables.Build(Container.Turret, PlayerNet.Local != null ? PlayerNet.Local.Team.Value : 0, mini, null);
                    mini.localScale = Vector3.one * 0.32f;
                    break;
                }
                case Item.LargeGate:
                    for (int k = -2; k <= 2; k++)
                    {
                        float h = 0.28f;
                        Art.Box(t, Mathf.Abs(k) == 2 ? Art.DarkWood : Art.Wood, new Vector3(k * 0.07f, h * 0.5f, 0), new Vector3(0.06f, h, 0.06f));
                        if (Mathf.Abs(k) < 2) Art.Part(t, Art.Cone, Art.Wood, new Vector3(k * 0.07f, h, 0), new Vector3(0.06f, 0.06f, 0.06f));
                    }
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.3f, 0), new Vector3(0.36f, 0.04f, 0.05f));
                    Art.Box(t, Art.Metal, new Vector3(0.1f, 0.14f, 0.04f), new Vector3(0.02f, 0.05f, 0.02f));
                    break;
                case Item.Skull:
                {
                    // an alien's skull: big domed cranium, two huge dark eye sockets, a little jaw
                    var bone = new Color(0.9f, 0.88f, 0.8f);
                    Art.Part(t, Art.Sphere, bone, new Vector3(0, 0.16f, 0), new Vector3(0.24f, 0.26f, 0.26f));
                    Art.Part(t, Art.Sphere, bone * 0.95f, new Vector3(0, 0.05f, 0.05f), new Vector3(0.13f, 0.1f, 0.12f));
                    for (int s = -1; s <= 1; s += 2) Art.Part(t, Art.Sphere, new Color(0.05f, 0.05f, 0.06f), new Vector3(s * 0.05f, 0.13f, 0.11f), new Vector3(0.07f, 0.09f, 0.04f), new Vector3(0, 0, s * -25f));
                    break;
                }
                case Item.Barrier:
                    for (int k = -2; k <= 2; k++)
                    {
                        float h = 0.26f + (k & 1) * 0.04f;
                        Art.Box(t, k % 2 == 0 ? Art.Wood : Art.DarkWood, new Vector3(k * 0.07f, h * 0.5f, 0), new Vector3(0.06f, h, 0.06f));
                        Art.Part(t, Art.Cone, Art.Wood, new Vector3(k * 0.07f, h, 0), new Vector3(0.06f, 0.06f, 0.06f));
                    }
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.12f, 0.035f), new Vector3(0.36f, 0.04f, 0.02f));
                    break;
                case Item.Wood:
                    for (int k = 0; k < 3; k++)
                    {
                        var p = new Vector3((k - 1) * 0.07f + (k == 1 ? 0 : 0), k == 1 ? 0.12f : 0.05f, k == 1 ? 0 : 0.01f);
                        Art.Part(t, Art.Cylinder, k == 1 ? Art.Wood : Art.DarkWood, p, new Vector3(0.08f, 0.14f, 0.08f), new Vector3(90, 0, 0));
                        Art.Part(t, Art.Cylinder, new Color(0.8f, 0.65f, 0.42f), p + new Vector3(0, 0, 0.141f), new Vector3(0.07f, 0.002f, 0.07f), new Vector3(90, 0, 0));
                    }
                    break;
                case Item.Stone:
                    Art.Part(t, Art.MakeRock(11, 0.3f), Art.Stone, new Vector3(0, 0.06f, 0), new Vector3(0.12f, 0.09f, 0.11f));
                    Art.Part(t, Art.MakeRock(12, 0.3f), Art.Stone * 0.85f, new Vector3(0.09f, 0.04f, 0.05f), new Vector3(0.07f, 0.06f, 0.07f));
                    Art.Part(t, Art.MakeRock(13, 0.3f), new Color(0.5f, 0.5f, 0.52f), new Vector3(-0.07f, 0.03f, 0.06f), new Vector3(0.06f, 0.05f, 0.06f));
                    break;
                case Item.Dna: DnaArt.Helix(t, 0.34f); break;
                case Item.Arrow:
                    // all wood: a thin pale shaft, a small point of darker, harder wood, thin wooden vanes for fletching
                    Art.Box(t, new Color(0.85f, 0.75f, 0.55f), new Vector3(0, 0.2f, 0), new Vector3(0.018f, 0.7f, 0.018f));
                    Art.Part(t, Art.Cone, HardWood, new Vector3(0, 0.55f, 0), new Vector3(0.045f, 0.1f, 0.045f));
                    Art.Box(t, Art.Wood, new Vector3(0, -0.1f, 0), new Vector3(0.09f, 0.12f, 0.005f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, -0.1f, 0), new Vector3(0.005f, 0.12f, 0.09f));
                    break;
                case Item.Berry:
                    // two real leaves (pointed, folded along the middle, curling up) on a little stem over the berries
                    Art.Box(t, Leaf * 0.7f, new Vector3(0, 0.1f, 0), new Vector3(0.008f, 0.04f, 0.008f), new Vector3(0, 0, 10));
                    Art.Part(t, LeafMesh, Leaf, new Vector3(0.004f, 0.115f, 0), Vector3.one * 0.15f, new Vector3(-35, 35, 0));
                    Art.Part(t, LeafMesh, Leaf * 0.85f, new Vector3(-0.004f, 0.11f, 0), Vector3.one * 0.11f, new Vector3(-20, 215, 0));
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * 1.05f;
                        var p = new Vector3(Mathf.Cos(a) * 0.035f, 0.05f + (k % 2) * 0.03f, Mathf.Sin(a) * 0.035f);
                        Art.Part(t, Art.Sphere, k % 3 == 0 ? Berry * 0.8f : Berry, p, Vector3.one * 0.045f);
                    }
                    break;
                case Item.C4:
                {
                    // taped brick of explosive with a detonator and a blinking LED
                    var putty = new Color(0.72f, 0.66f, 0.45f);
                    Art.Box(t, putty, new Vector3(-0.055f, 0.04f, 0), new Vector3(0.1f, 0.08f, 0.22f));
                    Art.Box(t, putty * 0.92f, new Vector3(0.055f, 0.04f, 0), new Vector3(0.1f, 0.08f, 0.22f));
                    Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, 0.04f, 0.06f), new Vector3(0.22f, 0.085f, 0.03f));
                    Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, 0.04f, -0.06f), new Vector3(0.22f, 0.085f, 0.03f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, 0.095f, 0), new Vector3(0.12f, 0.03f, 0.08f));
                    Art.Box(t, new Color(0.9f, 0.15f, 0.1f), new Vector3(0.03f, 0.1f, 0.05f), new Vector3(0.01f, 0.01f, 0.12f), new Vector3(0, 30, 0));
                    Art.Box(t, new Color(0.1f, 0.4f, 0.9f), new Vector3(-0.03f, 0.1f, 0.05f), new Vector3(0.01f, 0.01f, 0.12f), new Vector3(0, -30, 0));
                    Art.Box(t, new Color(1f, 0.1f, 0.05f), new Vector3(0.03f, 0.115f, -0.02f), new Vector3(0.025f, 0.02f, 0.025f)).name = "led";
                    break;
                }
                case Item.DeathWand:
                {
                    var bone = new Color(0.85f, 0.82f, 0.72f);
                    Art.Box(t, new Color(0.12f, 0.1f, 0.12f), new Vector3(0, 0.2f, 0), new Vector3(0.035f, 0.55f, 0.035f));
                    Art.Box(t, bone, new Vector3(0, 0.0f, 0), new Vector3(0.045f, 0.12f, 0.045f));
                    for (int k = 0; k < 3; k++)
                        Art.Box(t, bone, new Vector3(0, 0.46f, 0), new Vector3(0.02f, 0.14f, 0.02f), new Vector3(k * 120f, 0, 35f)).transform.localPosition = new Vector3(Mathf.Cos(k * 2.1f) * 0.04f, 0.5f, Mathf.Sin(k * 2.1f) * 0.04f);
                    Art.Part(t, Art.Ico, new Color(0.3f, 1f, 0.35f), new Vector3(0, 0.55f, 0), Vector3.one * 0.05f);
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.55f, 0), Vector3.one * 0.075f, default, false, Art.Ghost(new Color(0.4f, 1f, 0.4f, 0.35f)));
                    break;
                }
                case Item.Helmet:
                {
                    // alien dome helmet with a dark visor; origin = centre of the head
                    var shell = new Color(0.55f, 0.6f, 0.66f);
                    Art.Part(t, Art.Sphere, shell, new Vector3(0, 0.05f, 0), new Vector3(0.36f, 0.3f, 0.4f));
                    Art.Part(t, Art.Sphere, new Color(0.1f, 0.12f, 0.16f), new Vector3(0, 0.0f, 0.1f), new Vector3(0.3f, 0.14f, 0.24f));
                    Art.Box(t, new Color(0.35f, 1f, 0.5f), new Vector3(0, 0.19f, 0), new Vector3(0.04f, 0.04f, 0.3f));
                    Art.Box(t, shell * 0.8f, new Vector3(0.17f, 0.02f, 0), new Vector3(0.04f, 0.1f, 0.1f));
                    Art.Box(t, shell * 0.8f, new Vector3(-0.17f, 0.02f, 0), new Vector3(0.04f, 0.1f, 0.1f));
                    // the detail (all inside the dome's footprint, so PlayerNet.FitHelmet sizes it as before): a rim round the
                    // bottom, a brow over the visor, two slanted glowing eye slits, a breather under it, armour plates up
                    // the crest, a light on each ear pod with an antenna behind it, and a power pack on the back
                    var dark = new Color(0.2f, 0.22f, 0.27f);
                    var glow = new Color(0.35f, 1f, 0.5f);
                    Art.Part(t, Art.Cylinder, shell * 0.7f, new Vector3(0, -0.055f, 0), new Vector3(0.345f, 0.012f, 0.385f));
                    Art.Box(t, shell * 0.85f, new Vector3(0, 0.075f, 0.165f), new Vector3(0.26f, 0.03f, 0.06f), new Vector3(-18, 0, 0));
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Art.Box(t, glow, new Vector3(k * 0.062f, 0.012f, 0.211f), new Vector3(0.075f, 0.022f, 0.012f), new Vector3(0, k * 22f, k * 16f));
                        Art.Part(t, Art.Cylinder, dark, new Vector3(k * 0.182f, 0.02f, 0), new Vector3(0.07f, 0.008f, 0.07f), new Vector3(0, 0, 90));
                        Art.Part(t, Art.Ico, glow, new Vector3(k * 0.18f, 0.02f, 0), Vector3.one * 0.012f);
                        Art.Box(t, dark, new Vector3(k * 0.16f, 0.15f, -0.05f), new Vector3(0.012f, 0.2f, 0.012f), new Vector3(-14, 0, -k * 5f));
                        Art.Part(t, Art.Ico, glow, new Vector3(k * 0.169f, 0.25f, -0.075f), Vector3.one * 0.018f);
                        // cheek plates either side of the breather
                        Art.Box(t, shell * 0.75f, new Vector3(k * 0.1f, -0.045f, 0.13f), new Vector3(0.07f, 0.05f, 0.1f), new Vector3(0, k * 28f, 0));
                    }
                    Art.Box(t, dark, new Vector3(0, -0.06f, 0.165f), new Vector3(0.1f, 0.05f, 0.07f));
                    for (int k = -1; k <= 1; k++) Art.Box(t, shell * 0.55f, new Vector3(k * 0.028f, -0.06f, 0.202f), new Vector3(0.012f, 0.036f, 0.006f));
                    for (int k = 0; k < 4; k++)
                    {
                        float z = -0.12f + k * 0.075f;
                        Art.Box(t, shell * 0.7f, new Vector3(0, 0.185f - Mathf.Abs(z) * 0.22f, z), new Vector3(0.085f, 0.022f, 0.05f), new Vector3(z * 110f, 0, 0));
                    }
                    Art.Box(t, dark, new Vector3(0, 0.03f, -0.175f), new Vector3(0.14f, 0.11f, 0.045f), new Vector3(12, 0, 0));
                    Art.Box(t, glow, new Vector3(0, 0.045f, -0.199f), new Vector3(0.09f, 0.014f, 0.01f), new Vector3(12, 0, 0));
                    Art.Box(t, glow * 0.7f, new Vector3(0, 0.015f, -0.193f), new Vector3(0.05f, 0.014f, 0.01f), new Vector3(12, 0, 0));
                    break;
                }
                case Item.SpeedJuice:
                {
                    // an energy drink: a tall lime can with a black band, a pink lightning bolt and a silver top
                    float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
                    Art.Part(t, Art.Cylinder, new Color(0.62f, 1f, 0.15f), new Vector3(0, 0.1f, 0), new Vector3(0.085f / cr, 0.2f / ch, 0.085f / cr));
                    Art.Part(t, Art.Cylinder, new Color(0.08f, 0.08f, 0.1f), new Vector3(0, 0.1f, 0), new Vector3(0.088f / cr, 0.06f / ch, 0.088f / cr));
                    Art.Part(t, Art.Cylinder, new Color(0.8f, 0.82f, 0.86f), new Vector3(0, 0.205f, 0), new Vector3(0.075f / cr, 0.012f / ch, 0.075f / cr));
                    Art.Box(t, new Color(1f, 0.25f, 0.7f), new Vector3(0, 0.12f, -0.045f), new Vector3(0.018f, 0.05f, 0.006f), new Vector3(0, 0, 25f));
                    Art.Box(t, new Color(1f, 0.25f, 0.7f), new Vector3(0.006f, 0.085f, -0.045f), new Vector3(0.018f, 0.045f, 0.006f), new Vector3(0, 0, 25f));
                    break;
                }
                case Item.InvisPotion:
                {
                    var glass = Art.Ghost(new Color(0.8f, 0.9f, 1f, 0.45f));
                    Art.Part(t, Art.Sphere, new Color(0.6f, 0.25f, 0.95f), new Vector3(0, 0.07f, 0), Vector3.one * 0.12f);
                    Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 0.08f, 0), Vector3.one * 0.15f, default, false, glass);
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, 0.18f, 0), new Vector3(0.045f, 0.05f, 0.045f), default, false, glass);
                    Art.Part(t, Art.Cylinder, new Color(0.5f, 0.35f, 0.2f), new Vector3(0, 0.235f, 0), new Vector3(0.04f, 0.015f, 0.04f));
                    break;
                }
                case Item.Crossbow:
                {
                    // Rust-style crossbow, held like a rifle: stock along +Z, prod across the front, bolt on top
                    Art.Box(t, Art.Wood, new Vector3(0, 0, 0.02f), new Vector3(0.055f, 0.07f, 0.62f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, -0.05f, -0.22f), new Vector3(0.06f, 0.1f, 0.2f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, -0.08f, -0.02f), new Vector3(0.04f, 0.09f, 0.04f), new Vector3(-15, 0, 0));
                    Art.Box(t, Art.Metal, new Vector3(0, 0.03f, 0.3f), new Vector3(0.07f, 0.05f, 0.06f));
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Art.Box(t, Art.DarkWood, new Vector3(k * 0.17f, 0.03f, 0.3f), new Vector3(0.32f, 0.03f, 0.04f), new Vector3(0, -k * 18f, 0));
                        Art.Box(t, new Color(0.9f, 0.9f, 0.85f), new Vector3(k * 0.16f, 0.035f, 0.17f), new Vector3(0.006f, 0.006f, 0.32f), new Vector3(0, k * 62f, 0));
                    }
                    var bolt = new GameObject("bolt").transform;
                    bolt.SetParent(t, false);
                    Art.Box(bolt, Art.Wood, new Vector3(0, 0.055f, 0.2f), new Vector3(0.015f, 0.015f, 0.4f));
                    Art.Part(bolt, Art.Cone, HardWood, new Vector3(0, 0.055f, 0.42f), new Vector3(0.035f, 0.07f, 0.035f), new Vector3(90, 0, 0));
                    break;
                }
                case Item.Armor:
                    // wooden chest plate with straps
                    Art.Box(t, Art.Wood, new Vector3(0, 0.15f, 0), new Vector3(0.34f, 0.36f, 0.06f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.24f, 0.035f), new Vector3(0.36f, 0.04f, 0.02f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.06f, 0.035f), new Vector3(0.36f, 0.04f, 0.02f));
                    Art.Box(t, Art.Wood, new Vector3(0.14f, 0.34f, -0.04f), new Vector3(0.1f, 0.04f, 0.14f));
                    Art.Box(t, Art.Wood, new Vector3(-0.14f, 0.34f, -0.04f), new Vector3(0.1f, 0.04f, 0.14f));
                    break;
                case Item.FortTower:
                    // a little bundle: a model of the tower you'll get
                    for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Art.Box(t, Art.DarkWood, new Vector3(x * 0.06f, 0.14f, z * 0.06f), new Vector3(0.025f, 0.28f, 0.025f));
                    Art.Box(t, Art.Wood, new Vector3(0, 0.22f, 0), new Vector3(0.16f, 0.02f, 0.16f));
                    Art.Part(t, Art.Cone, new Color(0.45f, 0.28f, 0.14f), new Vector3(0, 0.31f, 0), new Vector3(0.2f, 0.08f, 0.2f), new Vector3(0, 45, 0));
                    Art.Box(t, Twine, new Vector3(0, 0.1f, 0), new Vector3(0.15f, 0.03f, 0.15f));
                    break;
                case Item.Car:
                    // a toy-sized car: crate on four wheels with the green pig
                    Art.Box(t, Art.Wood, new Vector3(0, 0.08f, 0), new Vector3(0.2f, 0.07f, 0.32f));
                    Art.Part(t, Art.Sphere, new Color(0.5f, 0.8f, 0.3f), new Vector3(0, 0.15f, 0.12f), Vector3.one * 0.08f);
                    for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Art.Part(t, Art.Cylinder, Art.DarkWood, new Vector3(x * 0.11f, 0.05f, z * 0.1f), new Vector3(0.09f, 0.015f, 0.09f), new Vector3(0, 0, 90));
                    break;
                case Item.Saddle:
                    Art.Box(t, new Color(0.35f, 0.18f, 0.08f), new Vector3(0, 0.08f, 0), new Vector3(0.22f, 0.05f, 0.26f));
                    Art.Box(t, new Color(0.35f, 0.18f, 0.08f), new Vector3(0, 0.12f, 0.11f), new Vector3(0.1f, 0.06f, 0.04f));
                    Art.Box(t, new Color(0.8f, 0.2f, 0.15f), new Vector3(0, 0.05f, 0), new Vector3(0.26f, 0.02f, 0.2f));
                    Art.Box(t, Art.Metal, new Vector3(0.12f, 0.0f, 0), new Vector3(0.02f, 0.08f, 0.03f));
                    Art.Box(t, Art.Metal, new Vector3(-0.12f, 0.0f, 0), new Vector3(0.02f, 0.08f, 0.03f));
                    break;
                case Item.Meat:
                    Art.Part(t, Art.Sphere, new Color(0.7f, 0.25f, 0.2f), new Vector3(0, 0.08f, 0.03f), new Vector3(0.2f, 0.14f, 0.16f));
                    Art.Part(t, Art.Sphere, new Color(0.85f, 0.5f, 0.4f), new Vector3(0.02f, 0.12f, 0.06f), new Vector3(0.12f, 0.06f, 0.1f));
                    Art.Box(t, new Color(0.95f, 0.92f, 0.85f), new Vector3(0, 0.05f, -0.1f), new Vector3(0.035f, 0.035f, 0.14f));
                    Art.Part(t, Art.Sphere, new Color(0.95f, 0.92f, 0.85f), new Vector3(0, 0.05f, -0.18f), Vector3.one * 0.05f);
                    break;
                case Item.Sniper:
                {
                    // long rifle held like the crossbow: stock back, long barrel forward, big scope on top
                    var gun = new Color(0.18f, 0.2f, 0.18f);
                    Art.Box(t, new Color(0.4f, 0.28f, 0.16f), new Vector3(0, -0.02f, -0.18f), new Vector3(0.06f, 0.1f, 0.34f));
                    Art.Box(t, gun, new Vector3(0, 0.01f, 0.1f), new Vector3(0.06f, 0.08f, 0.3f));
                    Art.Box(t, gun * 0.8f, new Vector3(0, 0.02f, 0.55f), new Vector3(0.025f, 0.025f, 0.65f));
                    Art.Part(t, Art.Cylinder, gun * 0.6f, new Vector3(0, 0.09f, 0.1f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(90, 0, 0));
                    Art.Box(t, gun, new Vector3(0, -0.07f, 0.02f), new Vector3(0.035f, 0.08f, 0.05f), new Vector3(-15, 0, 0));
                    break;
                }
                case Item.PortalGun:
                {
                    // Aperture-style: white body, black grip, glowing tip and prongs
                    var white = new Color(0.92f, 0.92f, 0.95f);
                    Art.Part(t, Art.Sphere, white, new Vector3(0, 0.02f, 0), new Vector3(0.16f, 0.14f, 0.3f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.17f), new Vector3(0, -0.07f, -0.05f), new Vector3(0.05f, 0.1f, 0.06f));
                    Art.Part(t, Art.Cylinder, new Color(0.3f, 0.3f, 0.32f), new Vector3(0, 0.02f, 0.2f), new Vector3(0.08f, 0.06f, 0.08f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Ico, new Color(0.3f, 0.7f, 1f), new Vector3(0, 0.02f, 0.26f), Vector3.one * 0.04f);
                    for (int k = 0; k < 3; k++)
                        Art.Box(t, white, new Vector3(Mathf.Cos(k * 2.1f) * 0.06f, 0.02f + Mathf.Sin(k * 2.1f) * 0.06f, 0.26f), new Vector3(0.015f, 0.015f, 0.08f));
                    break;
                }
                case Item.Jetpack:
                    Art.Box(t, new Color(0.35f, 0.36f, 0.4f), new Vector3(0, 0.15f, 0), new Vector3(0.22f, 0.26f, 0.1f));
                    Art.Part(t, Art.Cylinder, new Color(0.8f, 0.3f, 0.1f), new Vector3(0.07f, 0.15f, -0.07f), new Vector3(0.07f, 0.12f, 0.07f));
                    Art.Part(t, Art.Cylinder, new Color(0.8f, 0.3f, 0.1f), new Vector3(-0.07f, 0.15f, -0.07f), new Vector3(0.07f, 0.12f, 0.07f));
                    Art.Part(t, Art.Cone, new Color(0.2f, 0.2f, 0.22f), new Vector3(0.07f, 0.02f, -0.07f), new Vector3(0.06f, -0.05f, 0.06f));
                    Art.Part(t, Art.Cone, new Color(0.2f, 0.2f, 0.22f), new Vector3(-0.07f, 0.02f, -0.07f), new Vector3(0.06f, -0.05f, 0.06f));
                    break;
                case Item.SlenderEgg:
                    Art.Part(t, Art.Sphere, new Color(0.08f, 0.08f, 0.1f), new Vector3(0, 0.08f, 0), new Vector3(0.12f, 0.16f, 0.12f));
                    Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 0.11f, 0.05f), new Vector3(0.04f, 0.05f, 0.02f));
                    break;
                case Item.BuildEgg:
                    Art.Part(t, Art.Sphere, new Color(0.98f, 0.95f, 0.85f), new Vector3(0, 0.08f, 0), new Vector3(0.12f, 0.16f, 0.12f));
                    for (int k = 0; k < 5; k++) Art.Part(t, Art.Sphere, new Color(0.3f, 0.8f, 1f), new Vector3(Mathf.Cos(k * 1.3f) * 0.055f, 0.05f + (k % 3) * 0.03f, Mathf.Sin(k * 1.3f) * 0.055f), Vector3.one * 0.02f);
                    break;
                case Item.GiantStaff:
                    Art.Box(t, new Color(0.35f, 0.22f, 0.12f), new Vector3(0, 0.3f, 0), new Vector3(0.04f, 0.9f, 0.04f));
                    Art.Part(t, Art.Ico, new Color(1f, 0.7f, 0.1f), new Vector3(0, 0.8f, 0), Vector3.one * 0.08f);
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.8f, 0), Vector3.one * 0.11f, default, false, Art.Ghost(new Color(1f, 0.8f, 0.3f, 0.35f)));
                    Art.Box(t, new Color(1f, 0.7f, 0.1f), new Vector3(0, 0.72f, 0), new Vector3(0.12f, 0.03f, 0.03f));
                    break;
                case Item.RocketLauncher:
                {
                    // Rust-style tube on the shoulder, rocket poking out the front
                    var olive = new Color(0.32f, 0.36f, 0.24f);
                    Art.Part(t, Art.Cylinder, olive, new Vector3(0, 0.02f, 0.05f), new Vector3(0.13f, 0.45f, 0.13f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, olive * 0.7f, new Vector3(0, 0.02f, 0.5f), new Vector3(0.16f, 0.03f, 0.16f), new Vector3(90, 0, 0));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, -0.1f, 0.05f), new Vector3(0.04f, 0.12f, 0.05f), new Vector3(-10, 0, 0));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0.08f, 0.1f, 0.15f), new Vector3(0.04f, 0.06f, 0.12f));
                    Art.Part(t, Art.Cone, new Color(0.75f, 0.2f, 0.12f), new Vector3(0, 0.02f, 0.5f), new Vector3(0.1f, 0.15f, 0.1f), new Vector3(90, 0, 0));
                    break;
                }
                case Item.BombBush:
                    // a little berry bush with a tiny fuse
                    Art.Part(t, Art.MakeRock(5, 0.2f), Leaf, new Vector3(0, 0.1f, 0), new Vector3(0.14f, 0.1f, 0.14f));
                    for (int k = 0; k < 5; k++) Art.Part(t, Art.Sphere, Berry, new Vector3(Mathf.Cos(k * 1.3f) * 0.12f, 0.1f + (k % 2) * 0.05f, Mathf.Sin(k * 1.3f) * 0.12f), Vector3.one * 0.035f);
                    Art.Box(t, new Color(0.2f, 0.2f, 0.2f), new Vector3(0, 0.22f, 0), new Vector3(0.01f, 0.06f, 0.01f), new Vector3(0, 0, 20));
                    Art.Part(t, Art.Ico, new Color(1f, 0.6f, 0.1f), new Vector3(0.012f, 0.26f, 0), Vector3.one * 0.015f);
                    break;
                case Item.TreeCamo:
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.08f, 0), new Vector3(0.04f, 0.16f, 0.04f));
                    Art.Part(t, Art.Cone, Art.Leaves, new Vector3(0, 0.12f, 0), new Vector3(0.18f, 0.14f, 0.18f));
                    Art.Part(t, Art.Cone, Art.Leaves * 1.1f, new Vector3(0, 0.2f, 0), new Vector3(0.13f, 0.12f, 0.13f));
                    break;
                case Item.Airstrike:
                    // a radio with an antenna and a red button
                    Art.Box(t, new Color(0.3f, 0.34f, 0.22f), new Vector3(0, 0.1f, 0), new Vector3(0.1f, 0.18f, 0.06f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.15f), new Vector3(0.03f, 0.25f, 0), new Vector3(0.01f, 0.14f, 0.01f));
                    Art.Box(t, new Color(0.9f, 0.1f, 0.1f), new Vector3(0, 0.14f, 0.032f), new Vector3(0.035f, 0.035f, 0.01f));
                    Art.Box(t, new Color(0.1f, 0.1f, 0.1f), new Vector3(0, 0.06f, 0.032f), new Vector3(0.07f, 0.05f, 0.01f));
                    break;
                case Item.Wallhack:
                {
                    // glasses with glowing red lenses
                    var frame = new Color(0.1f, 0.1f, 0.12f);
                    Art.Box(t, frame, new Vector3(0, 0.05f, 0), new Vector3(0.2f, 0.015f, 0.015f));
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Art.Box(t, new Color(1f, 0.2f, 0.15f), new Vector3(k * 0.055f, 0.03f, 0), new Vector3(0.07f, 0.05f, 0.01f));
                        Art.Box(t, frame, new Vector3(k * 0.1f, 0.05f, -0.07f), new Vector3(0.01f, 0.01f, 0.14f));
                    }
                    break;
                }
                case Item.EnderPearl:
                    // a dark teal marble with a glowing green heart
                    Art.Part(t, Art.Sphere, new Color(0.05f, 0.3f, 0.3f), new Vector3(0, 0.08f, 0.03f), Vector3.one * 0.13f);
                    Art.Part(t, Art.Sphere, new Color(0.3f, 0.95f, 0.7f), new Vector3(0, 0.08f, 0.03f), Vector3.one * 0.07f);
                    break;
                case Item.Pistol:
                {
                    // held like the crossbow: grip under the back, slide pointing forward (+Z)
                    var gun = new Color(0.16f, 0.16f, 0.18f);
                    Art.Box(t, gun, new Vector3(0, 0.03f, 0.08f), new Vector3(0.05f, 0.06f, 0.26f));
                    Art.Box(t, gun * 1.6f, new Vector3(0, 0.065f, 0.08f), new Vector3(0.045f, 0.015f, 0.25f));
                    Art.Box(t, new Color(0.35f, 0.22f, 0.12f), new Vector3(0, -0.06f, -0.01f), new Vector3(0.045f, 0.13f, 0.06f), new Vector3(-12, 0, 0));
                    Art.Box(t, gun, new Vector3(0, -0.02f, 0.06f), new Vector3(0.01f, 0.04f, 0.05f));
                    Art.Box(t, Color.black, new Vector3(0, 0.035f, 0.215f), new Vector3(0.02f, 0.02f, 0.01f));
                    break;
                }
                case Item.Sword:
                {
                    // a long steel blade up from a leather grip (held like the hatchet), brass crossguard and pommel
                    var steel = new Color(0.78f, 0.8f, 0.84f);
                    Art.Box(t, new Color(0.3f, 0.18f, 0.1f), new Vector3(0, 0.06f, 0), new Vector3(0.045f, 0.2f, 0.045f));
                    Art.Part(t, Art.Sphere, new Color(0.8f, 0.64f, 0.28f), new Vector3(0, -0.05f, 0), Vector3.one * 0.06f);
                    Art.Box(t, new Color(0.8f, 0.64f, 0.28f), new Vector3(0, 0.17f, 0), new Vector3(0.06f, 0.04f, 0.24f));
                    Art.Box(t, steel, new Vector3(0, 0.58f, 0), new Vector3(0.02f, 0.78f, 0.075f));
                    Art.Box(t, steel * 0.8f, new Vector3(0, 0.58f, 0), new Vector3(0.024f, 0.74f, 0.012f)); // the fuller down the middle
                    Art.Part(t, Art.Cone, steel, new Vector3(0, 1.0f, 0), new Vector3(0.075f, 0.07f, 0.02f));
                    break;
                }
                case Item.Shotgun:
                {
                    // Rust's waterpipe: a length of pipe taped onto a rough plank stock, a hinged breech at the back
                    var pipe = new Color(0.32f, 0.33f, 0.35f);
                    var tape = new Color(0.12f, 0.12f, 0.13f);
                    Art.Part(t, Art.Cylinder, pipe, new Vector3(0, 0.04f, 0.2f), new Vector3(0.06f, 0.3f, 0.06f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, pipe * 1.2f, new Vector3(0, 0.04f, 0.5f), new Vector3(0.075f, 0.02f, 0.075f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, Color.black, new Vector3(0, 0.04f, 0.515f), new Vector3(0.04f, 0.01f, 0.04f), new Vector3(90, 0, 0));
                    Art.Box(t, new Color(0.5f, 0.36f, 0.22f), new Vector3(0, -0.01f, 0.08f), new Vector3(0.05f, 0.04f, 0.42f));
                    Art.Box(t, new Color(0.5f, 0.36f, 0.22f), new Vector3(0, -0.05f, -0.2f), new Vector3(0.055f, 0.12f, 0.24f), new Vector3(10, 0, 0));
                    Art.Box(t, new Color(0.4f, 0.28f, 0.16f), new Vector3(0, -0.08f, -0.02f), new Vector3(0.045f, 0.12f, 0.05f), new Vector3(-15, 0, 0));
                    Art.Box(t, pipe * 0.8f, new Vector3(0, 0.04f, -0.06f), new Vector3(0.075f, 0.075f, 0.08f));
                    for (int k = 0; k < 2; k++) Art.Box(t, tape, new Vector3(0, 0.02f, 0.12f + k * 0.18f), new Vector3(0.08f, 0.1f, 0.04f));
                    break;
                }
                case Item.Revolver:
                {
                    // a six-shooter: long barrel, the drum, a wooden grip (held like the pistol). Barrel and drum sit low,
                    // under the line of the sights, so aiming down them you see over the gun, not into a big round drum
                    var gun = new Color(0.22f, 0.22f, 0.25f);
                    float cr = Art.Cylinder.bounds.extents.x;
                    Art.Part(t, Art.Cylinder, gun, new Vector3(0, 0.046f, 0.15f), new Vector3(0.019f / cr, 0.11f, 0.019f / cr), new Vector3(90, 0, 0));
                    Art.Box(t, gun, new Vector3(0, 0.066f, 0.15f), new Vector3(0.012f, 0.012f, 0.22f));
                    // the drum (the reload swings it out: "drum", turning about its own axis)
                    var drum = Art.Part(t, Art.Cylinder, gun * 1.5f, new Vector3(0, 0.03f, 0.01f), new Vector3(0.034f / cr, 0.04f, 0.034f / cr), new Vector3(90, 0, 0));
                    drum.name = "drum";
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3f;
                        Art.Part(drum.transform, Art.Cylinder, new Color(0.85f, 0.65f, 0.25f), new Vector3(Mathf.Cos(a) * 0.55f, -1.02f, Mathf.Sin(a) * 0.55f), new Vector3(0.22f, 0.02f, 0.22f)).name = "round";
                    }
                    Art.Box(t, gun, new Vector3(0, 0.03f, -0.05f), new Vector3(0.04f, 0.055f, 0.06f));
                    Art.Box(t, new Color(0.45f, 0.28f, 0.14f), new Vector3(0, -0.05f, -0.07f), new Vector3(0.04f, 0.12f, 0.055f), new Vector3(-18, 0, 0));
                    Art.Box(t, gun, new Vector3(0, -0.015f, -0.01f), new Vector3(0.01f, 0.035f, 0.04f));
                    // the sights you look down with RMB: a blade at the muzzle, a notch over the hammer - the highest things on it
                    Art.Box(t, gun * 1.6f, new Vector3(0, 0.083f, 0.25f), new Vector3(0.006f, 0.022f, 0.012f));
                    for (int k = -1; k <= 1; k += 2) Art.Box(t, gun * 0.8f, new Vector3(k * 0.009f, 0.08f, -0.072f), new Vector3(0.007f, 0.03f, 0.012f));
                    break;
                }
                case Item.ShotgunShell:
                    // a red plastic shell with a brass base
                    Art.Part(t, Art.Cylinder, new Color(0.8f, 0.15f, 0.12f), new Vector3(0, 0.08f, 0), new Vector3(0.07f, 0.06f, 0.07f));
                    Art.Part(t, Art.Cylinder, new Color(0.85f, 0.65f, 0.25f), new Vector3(0, 0.02f, 0), new Vector3(0.075f, 0.02f, 0.075f));
                    break;
                case Item.RevolverAmmo:
                    // a few brass bullets
                    for (int k = 0; k < 3; k++)
                    {
                        Art.Part(t, Art.Cylinder, new Color(0.85f, 0.65f, 0.25f), new Vector3(-0.04f + k * 0.04f, 0.04f, 0), new Vector3(0.03f, 0.04f, 0.03f));
                        Art.Part(t, Art.Sphere, new Color(0.6f, 0.45f, 0.3f), new Vector3(-0.04f + k * 0.04f, 0.085f, 0), Vector3.one * 0.03f);
                    }
                    break;
                case Item.PistolAmmo:
                    // a little box of brass rounds
                    Art.Box(t, new Color(0.3f, 0.35f, 0.25f), new Vector3(0, 0.05f, 0), new Vector3(0.18f, 0.1f, 0.12f));
                    for (int k = 0; k < 4; k++)
                        Art.Part(t, Art.Cylinder, new Color(0.85f, 0.65f, 0.25f), new Vector3(-0.06f + k * 0.04f, 0.12f, 0), new Vector3(0.025f, 0.03f, 0.025f));
                    break;
                case Item.HeavyArmor:
                    // grey iron chest plate with rivets and shoulder guards
                    Art.Box(t, new Color(0.5f, 0.52f, 0.56f), new Vector3(0, 0.15f, 0), new Vector3(0.36f, 0.38f, 0.08f));
                    Art.Box(t, new Color(0.35f, 0.36f, 0.4f), new Vector3(0, 0.15f, 0.045f), new Vector3(0.06f, 0.36f, 0.02f));
                    Art.Box(t, new Color(0.4f, 0.42f, 0.46f), new Vector3(0.16f, 0.35f, -0.03f), new Vector3(0.14f, 0.06f, 0.16f));
                    Art.Box(t, new Color(0.4f, 0.42f, 0.46f), new Vector3(-0.16f, 0.35f, -0.03f), new Vector3(0.14f, 0.06f, 0.16f));
                    for (int k = -1; k <= 1; k += 2) Art.Part(t, Art.Sphere, new Color(0.8f, 0.7f, 0.3f), new Vector3(k * 0.13f, 0.26f, 0.045f), Vector3.one * 0.025f);
                    break;
                case Item.TreeCracker:
                {
                    // a huge two-handed axe: long handle, massive iron head with a glowing edge
                    var iron = new Color(0.36f, 0.36f, 0.4f);
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.35f, 0), new Vector3(0.06f, 0.9f, 0.06f));
                    Art.Box(t, new Color(0.7f, 0.15f, 0.1f), new Vector3(0, 0.0f, 0), new Vector3(0.07f, 0.16f, 0.07f));
                    Art.Box(t, iron, new Vector3(0, 0.72f, 0.16f), new Vector3(0.05f, 0.34f, 0.3f));
                    Art.Box(t, new Color(1f, 0.75f, 0.3f), new Vector3(0, 0.72f, 0.32f), new Vector3(0.04f, 0.38f, 0.04f));
                    Art.Box(t, iron, new Vector3(0, 0.72f, -0.08f), new Vector3(0.05f, 0.12f, 0.12f));
                    break;
                }
                case Item.WoodGenBuff:
                    // a little stack of logs with a big orange-yellow up arrow well above it (an upgrade, not a heal)
                    for (int k = 0; k < 3; k++)
                        Art.Part(t, Art.Cylinder, k == 1 ? Art.Wood * 0.9f : Art.Wood, new Vector3(-0.08f + k * 0.08f, 0.04f, 0), new Vector3(0.07f, 0.12f, 0.07f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, Art.Wood, new Vector3(-0.04f, 0.11f, 0), new Vector3(0.07f, 0.12f, 0.07f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, Art.Wood * 0.9f, new Vector3(0.04f, 0.11f, 0), new Vector3(0.07f, 0.12f, 0.07f), new Vector3(90, 0, 0));
                    Art.Box(t, UpgradeArrow, new Vector3(0, 0.3f, 0), new Vector3(0.065f, 0.12f, 0.065f));
                    Art.Part(t, Art.Cone, UpgradeArrow, new Vector3(0, 0.385f, 0), new Vector3(0.18f, 0.11f, 0.18f));
                    break;
                case Item.FortifyBuff:
                    // a stone brick with the same big orange-yellow up arrow well above it
                    Art.Box(t, Art.Stone, new Vector3(0, 0.07f, 0), new Vector3(0.24f, 0.14f, 0.14f));
                    Art.Box(t, UpgradeArrow, new Vector3(0, 0.29f, 0), new Vector3(0.065f, 0.12f, 0.065f));
                    Art.Part(t, Art.Cone, UpgradeArrow, new Vector3(0, 0.375f, 0), new Vector3(0.18f, 0.11f, 0.18f));
                    break;
                case Item.Boat:
                    // a toy rowing boat
                    Art.Box(t, new Color(0.55f, 0.38f, 0.22f), new Vector3(0, 0.04f, 0), new Vector3(0.16f, 0.04f, 0.34f));
                    Art.Box(t, new Color(0.55f, 0.38f, 0.22f), new Vector3(0.08f, 0.08f, 0), new Vector3(0.02f, 0.06f, 0.34f));
                    Art.Box(t, new Color(0.55f, 0.38f, 0.22f), new Vector3(-0.08f, 0.08f, 0), new Vector3(0.02f, 0.06f, 0.34f));
                    Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, 0.1f, -0.18f), new Vector3(0.05f, 0.06f, 0.04f));
                    break;
                case Item.Chainsaw:
                {
                    // held in both hands like the ram; the bar points forward (+Z)
                    var body = new Color(0.95f, 0.5f, 0.1f);
                    Art.Box(t, body, new Vector3(0, 0, 0), new Vector3(0.16f, 0.2f, 0.34f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, 0.13f, -0.02f), new Vector3(0.05f, 0.05f, 0.26f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, 0.08f, -0.2f), new Vector3(0.05f, 0.14f, 0.05f));
                    Art.Box(t, Art.Metal, new Vector3(0, -0.01f, 0.42f), new Vector3(0.03f, 0.1f, 0.55f));
                    Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, -0.01f, 0.42f), new Vector3(0.04f, 0.12f, 0.53f));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, -0.01f, 0.69f), new Vector3(0.12f, 0.02f, 0.12f), new Vector3(0, 0, 90));
                    break;
                }
            }
            // PSX graphics: the PSX model takes the place of this one (same size, same grip)
            var psx = PsxModels.ItemKey(item);
            if (psx != null) PsxModels.Replace(t, psx, PsxModels.ItemFit(item), PsxModels.ItemEuler(item));
            return root;
        }

        /// <summary>Spear model with its tip at the local origin, pointing along +Z (thrown, dropped and stuck spears).</summary>
        public static GameObject CreateSpearTipForward(Transform parent)
        {
            var go = Create(Item.Spear, parent);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            go.transform.localPosition = new Vector3(0, 0, -1.28f);
            return go;
        }

        /// <summary>A rocket in flight (the rocket launcher's projectile), pointing along +Z.</summary>
        public static GameObject CreateRocket(Transform parent)
        {
            var root = new GameObject("rocket");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            Art.Part(t, Art.Cylinder, new Color(0.35f, 0.38f, 0.28f), Vector3.zero, new Vector3(0.1f, 0.3f, 0.1f), new Vector3(90, 0, 0));
            Art.Part(t, Art.Cone, new Color(0.75f, 0.2f, 0.12f), new Vector3(0, 0, 0.3f), new Vector3(0.1f, 0.18f, 0.1f), new Vector3(90, 0, 0));
            for (int k = 0; k < 4; k++) Art.Box(t, new Color(0.2f, 0.2f, 0.2f), Quaternion.Euler(0, 0, k * 90f) * new Vector3(0, 0.07f, -0.25f), new Vector3(0.01f, 0.08f, 0.1f), new Vector3(0, 0, k * 90f));
            return root;
        }

        /// <summary>Arrow model with its tip at the local origin, pointing along +Z (arrows stuck in the world).</summary>
        public static GameObject CreateArrowTipForward(Transform parent)
        {
            var go = Create(Item.Arrow, parent);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            go.transform.localPosition = new Vector3(0, 0, -0.6f);
            return go;
        }

        /// <summary>The objective ball (same look as the world ball), diameter = size.</summary>
        public static GameObject CreateBall(Transform parent, float size)
        {
            var root = new GameObject("ball");
            root.transform.SetParent(parent, false);
            float k = size / 1.24f;
            Art.Part(root.transform, Art.Ico, new Color(1f, 0.85f, 0.15f), Vector3.zero, Vector3.one * 0.62f * k);
            Art.Box(root.transform, new Color(0.9f, 0.5f, 0.1f), Vector3.zero, new Vector3(1.28f, 0.12f, 0.12f) * k);
            Art.Box(root.transform, new Color(0.9f, 0.5f, 0.1f), Vector3.zero, new Vector3(0.12f, 0.12f, 1.28f) * k);
            return root;
        }
    }
}
