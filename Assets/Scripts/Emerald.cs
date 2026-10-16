using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// EMERALD MODE (GameRules.Emerald): Classic (keep the ball in your machine to fill your bar - no clock), plus:
    /// - airdrops only ever come down in three places: the middle, and the middle of the left and right sides (with 3-4
    ///   bases: closer in, on the diagonals) - each one picks one of them at random (Cfg.EmeraldDropSpots);
    /// - EMERALD SPAWNERS: raised stone platforms, one on every team's way to the middle (EmeraldSpawner). Each grows ONE
    ///   emerald every EmeraldEvery seconds (3 minutes) - while one's waiting there, no more come (NetGame.ServerTickEmeralds);
    /// - every base has an EMERALD SHOP on the bedrock, left of a smaller alien machine (EmeraldShop): E on it opens a
    ///   store (Hud.Emerald.cs) of the Advanced Trade Station's things and more, bought with emeralds (EmeraldDeals);
    /// - the Trade Station is a store too: E on it shows its items (you can't trade them from the bag), Armour at the top
    ///   and the battering ram (EmeraldRamWood) at the bottom - and it can be crafted any time, in your base (no ball
    ///   capture needed). There's no Advanced Trade Station at all.
    /// The shop's own things: High Power Armour, the lightsaber, Instant Heal Berries, the base turret (360 degrees, no
    /// weapon or ammo needed), high defensive walls (8 tall log walls and a gate), land mines and the fort creator.
    /// </summary>
    public static partial class Cfg
    {
        public static bool Emerald => Rules == GameRules.Emerald;

        /// <summary>Seconds between emeralds on a spawner (only one waits there at a time).</summary>
        [Tune("Emerald mode")] public static float EmeraldEvery = 180f;
        /// <summary>The battering ram's price in Emerald mode (a Trade Station item there, at the bottom of its list).</summary>
        [Tune("Emerald mode")] public static int EmeraldRamWood = 500;
        /// <summary>High Power Armour: how much armour it puts on.</summary>
        [Tune("Emerald mode")] public static int HighArmorHp = 150;
        [Tune("Emerald mode")] public static float LightsaberCooldown = 0.45f, LightsaberDamage = 42f, LightsaberRange = 3.1f, LightsaberStructureDamage = 30f;
        /// <summary>The base turret: its damage a shot and seconds between shots (it sees all the way round, TurretRange far).</summary>
        [Tune("Emerald mode")] public static float BaseTurretDamage = 5f, BaseTurretEvery = 0.45f;
        /// <summary>The land mine: whoever steps on it dies; anyone else this close takes the splash.</summary>
        [Tune("Emerald mode")] public static float MineSplashRadius = 3f, MineSplashDamage = 60f;
        /// <summary>High defensive walls (the large wall and gate in this mode): this many times as tall, and as strong.</summary>
        [Tune("Emerald mode")] public static float BigWallTallMul = 1.4f, BigWallHpMul = 2f;
        /// <summary>Emerald shop prices (emeralds): the knockback stick, an ender pearl, the lightsaber, the alien gun and C4.
        /// (No other guns: no revolver, shotgun or ammo.)</summary>
        [Tune("Emerald shop")] public static int EmeraldPriceKnockStick = 1, EmeraldPricePearls = 2, EmeraldPriceLightsaber = 3, EmeraldPriceAlienGun = 4, EmeraldPriceC4 = 3;
        /// <summary>The ALIEN GUN (the emerald shop's gun): fires like the revolver (hitscan, LMB as fast as its fire rate) but
        /// hits harder, never reloads, and breaks after its last shot (AlienGunShots); its shots left show in the bottom
        /// right of the screen.</summary>
        [Tune("Emerald mode")] public static int AlienGunShots = 10;
        [Tune("Emerald mode")] public static float AlienGunBodyDamage = 45f, AlienGunHeadDamage = 75f, AlienGunFireRate = 0.35f;

        /// <summary>Emerald mode's own things: nowhere else (Cfg.Removed).</summary>
        public static bool EmeraldOnly(Item i) => i == Item.Emerald || i == Item.HighArmor || i == Item.Lightsaber || i == Item.HealBerry || i == Item.LandMine || i == Item.FortCreator || i == Item.AlienGun || i == Item.BaseGuardian;
        /// <summary>Not crafted in Emerald mode: there's no Advanced Trade Station, and the death wand's in the emerald shop.</summary>
        public static bool EmeraldOut(Item i) => Emerald && (i == Item.Workbench2 || i == Item.DeathWand);

        /// <summary>The tall walls of this mode (1 elsewhere).</summary>
        public static float BigWallTall => Emerald ? Mathf.Max(1f, BigWallTallMul) : 1f;

        /// <summary>One thing on the emerald shop's list: what you get (and a second thing with it - the walls' gate), and its price.</summary>
        public struct EmeraldDeal
        {
            public Item Id;
            public int Count;
            public Item Extra;
            public int ExtraCount;
            /// <summary>A third thing with it (the walls pack's base guardian).</summary>
            public Item Extra2;
            public int Extra2Count;
            public int Price;
            public string Blurb;
        }

        static readonly List<EmeraldDeal> s_Deals = new List<EmeraldDeal>();

        /// <summary>The emerald shop's list, in order (prices are host values, so it's made fresh each time).</summary>
        public static List<EmeraldDeal> EmeraldDeals
        {
            get
            {
                s_Deals.Clear();
                void Add(Item id, int n, int price, string blurb, Item extra = Item.None, int extraN = 0, Item extra2 = Item.None, int extra2N = 0) => s_Deals.Add(new EmeraldDeal { Id = id, Count = n, Price = Mathf.Max(0, price), Blurb = blurb, Extra = extra, ExtraCount = extraN, Extra2 = extra2, Extra2Count = extra2N });
                if (LuckyBlock)
                {
                    // Lucky Block mode: just the one thing - a random airdrop item (LuckyBlock.cs)
                    Add(Item.LuckyBlock, 1, LuckyBlockPrice, "a random airdrop item - any of them");
                    return s_Deals;
                }
                Add(Item.KnockStick, 1, EmeraldPriceKnockStick, "knocks whoever it hits back a good way");
                Add(Item.EnderPearl, 1, EmeraldPricePearls, "throw it - you teleport where it lands");
                Add(Item.Lightsaber, 1, EmeraldPriceLightsaber, $"quick glowing blade: {LightsaberDamage:0} a hit");
                Add(Item.AlienGun, 1, EmeraldPriceAlienGun, $"{GunMag(Item.AlienGun)} shots, {AlienGunBodyDamage:0} body / {AlienGunHeadDamage:0} head - then it breaks");
                Add(Item.C4, 1, EmeraldPriceC4, "thrown: wrecks every building piece nearby");
                Add(Item.Barrier, 8, EmeraldPriceWalls, "8 tall steel walls and a gate - and a robot that lasers anyone who comes into your base", Item.LargeGate, 1, Item.BaseGuardian, 1);
                return s_Deals;
            }
        }

        public static string EmeraldName(Item i)
        {
            switch (i)
            {
                case Item.Emerald: return "Emerald";
                case Item.HighArmor: return "High Power Armour";
                case Item.Lightsaber: return "Lightsaber";
                case Item.HealBerry: return "Instant Heal Berries";
                case Item.LandMine: return "Land Mine";
                case Item.FortCreator: return "Fort Creator";
                case Item.LuckyBlock: return "Lucky Block";
                case Item.AlienGun: return "Alien Gun";
                case Item.BaseGuardian: return "Base Guardian";
                default: return null;
            }
        }

        public static string EmeraldBlurb(Item i)
        {
            switch (i)
            {
                case Item.Emerald: return LuckyBlock ? "Spend them on lucky blocks at the emerald shop beside your alien machine. They grow on the emerald spawners." : "Emerald mode's money: spend them at the emerald shop beside your alien machine. One grows on each emerald spawner every few minutes.";
                case Item.LuckyBlock: return "Open it: a random airdrop item - any of them.";
                case Item.HighArmor: return $"LMB: put it on - {HighArmorHp} armour, used up before your health.";
                case Item.Lightsaber: return $"A quick glowing blade: {LightsaberDamage:0} damage a hit (x2 to the head), cuts through wood too.";
                case Item.HealBerry: return "LMB / RMB: eat one - you're back on full health at once.";
                case Item.BaseGuardian: return $"LMB: put it in your base - it hovers there, and any enemy who comes in gets laser bolts ({GuardianDamage:0} a hit) until they leave.";
                case Item.AlienGun: return $"Like the revolver, but it hits harder ({AlienGunBodyDamage:0} body, {AlienGunHeadDamage:0} head) and never needs ammo: {GunMag(Item.AlienGun)} shots, then it breaks.";
                case Item.LandMine: return "LMB: put it on the ground, anywhere. It's hard to see - an enemy who steps on it blows up.";
                case Item.FortCreator: return "LMB: throw it - a fort pops up where it lands: tall walls round a lookout tower, and a gate your team opens.";
                default: return null;
            }
        }

        /// <summary>Emerald mode's own melee weapon.</summary>
        public static MeleeStats LightsaberMelee => new MeleeStats { Cooldown = Mathf.Max(0.15f, LightsaberCooldown), Range = LightsaberRange, PlayerDamage = LightsaberDamage, WoodGather = 18f, StoneGather = 4f, StructureDamage = LightsaberStructureDamage };

        // ---------- airdrops: only the middle and the middle of the two sides ----------

        /// <summary>Where Emerald mode's airdrops come down (each one picks one of these at random): the middle, and the
        /// middles of the left and right sides - with 3-4 bases closer in, on the diagonals (each between two bases).</summary>
        public static Vector3[] EmeraldDropSpots
        {
            get
            {
                var s = new Vector3[3];
                s[0] = Vector3.zero;
                Vector3 side;
                float r;
                if (FourWay) { side = new Vector3(1f, 0f, 1f).normalized; r = Mathf.Max(MapBuilder.DomeRadius + 10f, MapHalf * 0.27f); }
                else
                {
                    var axis = BaseCenter[1] - BaseCenter[0];
                    axis.y = 0f;
                    side = Vector3.Cross(Vector3.up, axis.normalized);
                    r = MapHalf * 0.55f;
                }
                s[1] = side * r;
                s[2] = -side * r;
                return s;
            }
        }

        // ---------- the emerald shop's spot (left of the machine) ----------

        /// <summary>The emerald shop, in the alien machine's frame on the one-cell bedrock (+x the LEFT as you look at the
        /// machine from your spawn), and on the 2 x 2 one (team games: where the upgrade station stood).</summary>
        static readonly Vector3 k_ShopSmall = new Vector3(1.17f, 0f, -1.0f), k_ShopBig = new Vector3(2.25f, 0f, 0.05f);
        public static Vector3 EmeraldShopPos(int team) => SmallBedrock ? OnSmallBedrock(team, k_ShopSmall + Vector3.up * BaseY) : MachinePos(team) + Quaternion.LookRotation(-BackDir(team)) * k_ShopBig;
        public static Quaternion EmeraldShopRot(int team) => Quaternion.LookRotation(-BackDir(team)) * Quaternion.Euler(0, SmallBedrock ? 0f : -10f, 0);
        /// <summary>How close to your emerald shop you have to be to buy (the screen closes further off).</summary>
        public const float EmeraldShopReach = 5f;
        public static bool AtOwnEmeraldShop(int team, Vector3 p)
        {
            if (!HasEmeralds || team < 0 || team >= TeamCount) return false;
            var m = EmeraldShopPos(team);
            p.y = m.y = 0f;
            return Vector3.Distance(p, m) <= EmeraldShopReach;
        }
        /// <summary>Close enough to your own Trade Station to trade at it (Emerald mode: its things are only bought there).</summary>
        public static bool AtOwnBench(int team, Vector3 p)
        {
            var b = Workbench.ForTeam(team, 1);
            if (b == null) return false;
            var q = b.transform.position;
            p.y = q.y = 0f;
            return Vector3.Distance(p, q) <= EmeraldShopReach + 1f;
        }
    }

    /// <summary>
    /// An EMERALD SPAWNER: a raised platform of stone steps (three, each low enough to walk up) with a glowing green
    /// ring on top, four little crystal pylons and a soft green light. The emeralds grow on its middle (NetGame.ServerTickEmeralds).
    /// Local scenery built by MapBuilder (every team's copy the same, turned round the map), its spot clear of rocks and
    /// scenery; the trees are planted round it (they keep clear of scenery: NetGame.InsideScenery).
    /// </summary>
    public class EmeraldSpawner : MonoBehaviour
    {
        /// <summary>Where each spawner's emerald sits (on its top), spawner 0 in blue's part of the map.</summary>
        public static readonly List<Vector3> Tops = new List<Vector3>();
        /// <summary>Seconds between emeralds on each spawner (Lucky Block mode's home and middle spawners go at their own pace).</summary>
        public static readonly List<float> Every = new List<float>();
        public static readonly List<EmeraldSpawner> All = new List<EmeraldSpawner>();
        public const float StepH = 0.3f, TopR = 1.5f;
        public static readonly Color Green = new Color(0.25f, 1f, 0.5f), Stone = new Color(0.46f, 0.47f, 0.5f), StoneDark = new Color(0.34f, 0.35f, 0.38f);
        Transform m_Gem;
        Light m_Light;
        public int Index { get; private set; }

        /// <summary>Spawner i's seconds between emeralds.</summary>
        public static float EveryOf(int i) => Mathf.Max(1f, i >= 0 && i < Every.Count ? Every[i] : Cfg.EmeraldEvery);

        /// <summary>Builds every spawner (Emerald and Lucky Block mode) - after everything else on the map, so their spots dodge
        /// it. Emerald mode: with two bases, one on the LEFT and one on the RIGHT of the ball (on the line between the
        /// halves, well clear of both bases); with 3-4, one on every team's way to the middle, close in. Lucky Block mode:
        /// just the two (3-4 bases: four) in between the bases, nearer the middle - none by any base.</summary>
        public static void BuildAll(Transform root)
        {
            Tops.Clear();
            Every.Clear();
            All.Clear();
            if (Cfg.LuckyBlock)
            {
                Place(root, SideSpot(Cfg.MapHalf * 0.3f), Cfg.LuckyMiddleEvery);
            }
            else if (Cfg.FourWay) Place(root, HomeSpot(0.12f), Cfg.EmeraldEvery);
            else Place(root, SideSpot(Cfg.MapHalf * 0.32f), Cfg.EmeraldEvery);
        }

        /// <summary>One spawner in every team's part of the map (the same spot, turned round).</summary>
        static void Place(Transform root, Vector3 first, float every)
        {
            for (int k = 0; k < Cfg.Copies; k++)
            {
                var p = Cfg.Copy(first, k);
                p.y = MapBuilder.Height(p.x, p.z);
                var go = new GameObject("EmeraldSpawner " + All.Count);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-new Vector3(p.x, 0, p.z).normalized));
                var s = go.AddComponent<EmeraldSpawner>();
                s.Index = All.Count;
                s.Build();
                All.Add(s);
                Tops.Add(p + Vector3.up * (StepH * 3f + 0.02f));
                Every.Add(every);
            }
        }

        /// <summary>A spot on blue's way from its base to the middle: `t` of the way from near the middle (0) out to just
        /// outside the base (1) - moved a little to the side if something's in the way.</summary>
        static Vector3 HomeSpot(float t)
        {
            var b = Cfg.BaseCenter[0];
            var dir = new Vector3(b.x, 0, b.z).normalized;
            float baseDist = new Vector2(b.x, b.z).magnitude;
            float near = MapBuilder.DomeRadius + 7f, far = baseDist - Cfg.BaseHalf - 5f;
            float d = Mathf.Max(near, Mathf.Lerp(near, far, t));
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (float off in new[] { 0f, 5f, -5f, 10f, -10f, 15f, -15f })
                foreach (float dd in new[] { 0f, -4f, 4f })
                {
                    var p = dir * (d + dd) + side * off;
                    if (Clear(p)) return p;
                }
            return dir * d;
        }

        /// <summary>A spot `dist` from the middle, half way round between blue's base and the next one: with two bases, on
        /// the line between the halves (to the left or right of the ball, as far from one base as the other); with 3-4 on
        /// the diagonal. Moved in or out along that line if something's in the way, then a little off it.</summary>
        static Vector3 SideSpot(float dist)
        {
            Vector3 dir;
            if (Cfg.FourWay)
            {
                var a = Cfg.BaseCenter[0]; a.y = 0f;
                var b = Cfg.Copy(Cfg.BaseCenter[0], 1); b.y = 0f;
                dir = (a.normalized + b.normalized).normalized;
            }
            else
            {
                var axis = Cfg.BaseCenter[1] - Cfg.BaseCenter[0];
                axis.y = 0f;
                dir = Vector3.Cross(Vector3.up, axis.normalized);
            }
            dist = Mathf.Max(MapBuilder.DomeRadius + 8f, dist);
            var across = Vector3.Cross(Vector3.up, dir);
            foreach (float off in new[] { 0f, 3f, -3f, 6f, -6f })
                foreach (float dd in new[] { 0f, 4f, -4f, 8f, -8f, 12f })
                {
                    var p = dir * (dist + dd) + across * off;
                    if (Clear(p)) return p;
                }
            return dir * dist;
        }

        /// <summary>Nothing solid (scenery, rocks) on any team's copy of this spot, and dry land.</summary>
        static bool Clear(Vector3 p)
        {
            for (int k = 0; k < Cfg.Copies; k++)
            {
                var q = Cfg.Copy(p, k);
                q.y = MapBuilder.Height(q.x, q.z);
                if (!ThemeMaps.SpotOk(q)) return false;
                var glass = MapBuilder.GlassWallRoot;
                foreach (var h in Physics.OverlapCapsule(q + Vector3.up * 0.5f, q + Vector3.up * 3f, 2.8f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.GetComponentInParent<GroundMarker>() == null && (glass == null || !h.transform.IsChildOf(glass.transform))) return false; // (the glass wall's only up until it drops: a spawner on the line between the halves stands in it till then)
                // not on a steep slope
                float lo = float.MaxValue, hi = float.MinValue;
                for (int a = 0; a < 8; a++)
                {
                    var o = Quaternion.Euler(0, a * 45f, 0) * Vector3.forward * 2.4f;
                    float h = MapBuilder.Height(q.x + o.x, q.z + o.z);
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
                if (hi - lo > 1.2f) return false;
            }
            return true;
        }

        void Build()
        {
            var t = transform;
            var glow = Workbench.Glow(Green, 2.2f);
            // three octagonal steps, each a pair of boxes turned 45 degrees (walkable: StepH high each), sunk into the ground
            for (int i = 0; i < 3; i++)
            {
                float r = TopR + (2 - i) * 0.45f, top = StepH * (i + 1);
                float h = top + 1f, y = top - h * 0.5f;
                var c = i % 2 == 0 ? Stone : StoneDark;
                for (int a = 0; a < 2; a++)
                    Art.Box(t, c, new Vector3(0, y, 0), new Vector3(r * 1.72f, h, r * 1.72f), new Vector3(0, a * 45f, 0), true);
                // a thin green line round each step's edge
                for (int a = 0; a < 8; a++)
                {
                    var rot = Quaternion.Euler(0, a * 45f + 22.5f, 0);
                    Art.Box(t, Green, rot * new Vector3(0, top + 0.004f, r * 0.86f - 0.04f), new Vector3(r * 0.7f, 0.01f, 0.04f), new Vector3(0, a * 45f + 22.5f, 0), false, glow)
                        .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            float topY = StepH * 3f;
            // the pad the emerald sits on: a dark disc with a glowing ring
            float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
            Art.Part(t, Art.Cylinder, new Color(0.12f, 0.14f, 0.13f), new Vector3(0, topY + 0.01f, 0), new Vector3(1.2f / cr, 0.02f / ch, 1.2f / cr));
            Art.Part(t, Art.Cylinder, Green, new Vector3(0, topY + 0.022f, 0), new Vector3(0.9f / cr, 0.006f / ch, 0.9f / cr), default, false, glow);
            Art.Part(t, Art.Cylinder, new Color(0.12f, 0.14f, 0.13f), new Vector3(0, topY + 0.026f, 0), new Vector3(0.74f / cr, 0.006f / ch, 0.74f / cr));
            // four little pylons with crystals on top
            for (int a = 0; a < 4; a++)
            {
                var o = Quaternion.Euler(0, a * 90f + 45f, 0) * new Vector3(0, 0, TopR - 0.3f);
                Art.Box(t, StoneDark, o + new Vector3(0, topY + 0.3f, 0), new Vector3(0.22f, 0.6f, 0.22f), new Vector3(0, a * 90f + 45f, 0), true);
                Art.Part(t, Art.Ico, Green, o + new Vector3(0, topY + 0.72f, 0), new Vector3(0.14f, 0.24f, 0.14f), default, false, glow);
            }
            // the empty emerald's ghost floats over the pad (shown while it's growing)
            m_Gem = new GameObject("ghostGem").transform;
            m_Gem.SetParent(t, false);
            m_Gem.localPosition = new Vector3(0, topY + 0.7f, 0);
            var ghost = Art.Ghost(new Color(Green.r, Green.g, Green.b, 0.22f));
            EmeraldModels.Gem(m_Gem, 0.5f, ghost, ghost);
            foreach (var r in m_Gem.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lg = new GameObject("light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, topY + 1.2f, 0);
            m_Light = lg.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.color = Green;
            m_Light.range = 6f;
            m_Light.intensity = 1.2f;
            if (AiPsxArt.On) AiPsxArt.Apply(t);
            GrassField.BlockRenderers(GetInstanceID(), t); // (no grass growing up through the steps)
        }

        void OnDestroy() { All.Remove(this); GrassField.Unblock(GetInstanceID()); }

        void Update()
        {
            var g = NetGame.Instance;
            bool waiting = g != null && g.EmeraldWaiting(Index);
            if (m_Gem)
            {
                // the ghost turns slowly while the next one grows (the real one is the item on the pad)
                if (m_Gem.gameObject.activeSelf == waiting) m_Gem.gameObject.SetActive(!waiting);
                m_Gem.localRotation = Quaternion.Euler(0, Time.time * 40f, 0);
                float grow = g != null ? g.EmeraldGrowth(Index) : 0f;
                m_Gem.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, grow);
            }
            if (m_Light) m_Light.intensity = waiting ? 2.2f + Mathf.Sin(Time.time * 4f) * 0.6f : 1f;
        }
    }

    /// <summary>
    /// The EMERALD SHOP: a booth on the bedrock to the LEFT of the alien machine (Cfg.EmeraldShopPos), dark stone with
    /// green trim, a glowing green screen with an emerald on it and a big emerald turning over its roof, so it's plain
    /// from across the base what it is. E on your own one opens the shop (Hud.Emerald.cs). Local scenery like the wood
    /// machine; its body has a collider (so nothing's put down inside it).
    /// </summary>
    public class EmeraldShop : MonoBehaviour
    {
        public static readonly EmeraldShop[] ByTeam = new EmeraldShop[4];
        public int Team { get; private set; }
        Transform m_Holo;
        public const float HalfX = 0.27f, HalfZ = 0.28f;

        public static EmeraldShop Create(Transform root, int team)
        {
            var go = new GameObject("EmeraldShop " + Cfg.TeamName[team]);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(Cfg.EmeraldShopPos(team), Cfg.EmeraldShopRot(team));
            var s = go.AddComponent<EmeraldShop>();
            s.Team = team;
            ByTeam[team] = s;
            s.Build();
            return s;
        }

        void OnDestroy() { if (ByTeam[Team] == this) ByTeam[Team] = null; }

        void Build()
        {
            var t = transform;
            var green = EmeraldSpawner.Green;
            var glow = Workbench.Glow(green, 2f);
            var teamC = Color.Lerp(Cfg.TeamColor[Mathf.Clamp(Team, 0, 3)], Color.white, 0.4f);
            var stone = new Color(0.2f, 0.22f, 0.24f);
            float s = Cfg.SmallBedrock ? 1f : 1.25f;
            float hx = HalfX * s, hz = HalfZ * s, h = 1.25f * s;
            var body = new GameObject("body");
            body.transform.SetParent(t, false);
            var bc = body.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, h * 0.5f, 0);
            bc.size = new Vector3(hx * 2f, h, hz * 2f);
            // the plinth and the booth
            Art.Box(t, new Color(0.3f, 0.33f, 0.36f), new Vector3(0, 0.08f * s, 0), new Vector3(hx * 2.2f, 0.16f * s, hz * 2.2f));
            Art.Box(t, stone, new Vector3(0, h * 0.5f + 0.05f, -0.02f * s), new Vector3(hx * 2f, h - 0.1f, hz * 1.9f));
            // green trim up the corners and round the top, a team-colour band
            for (int sx = -1; sx <= 1; sx += 2)
                Art.Box(t, green, new Vector3(sx * (hx + 0.004f), h * 0.5f, hz * 0.95f), new Vector3(0.025f, h * 0.9f, 0.025f), default, false, glow);
            Art.Box(t, green, new Vector3(0, h - 0.04f, 0), new Vector3(hx * 2.06f, 0.035f, hz * 2.06f), default, false, glow);
            Art.Box(t, teamC, new Vector3(0, 0.22f * s, hz * 0.96f), new Vector3(hx * 1.9f, 0.05f * s, 0.01f));
            // the screen: a dark bezel, green glass and an emerald on it
            float sy = h * 0.62f, sz = hz * 0.95f + 0.008f;
            Art.Box(t, new Color(0.05f, 0.06f, 0.07f), new Vector3(0, sy, sz), new Vector3(hx * 1.7f, 0.42f * s, 0.02f));
            Art.Box(t, Color.white, new Vector3(0, sy, sz + 0.012f), new Vector3(hx * 1.5f, 0.36f * s, 0.006f), default, false, Art.Ghost(new Color(green.r * 0.3f, green.g * 0.45f, green.b * 0.3f, 0.92f)));
            var icon = new GameObject("screenGem").transform;
            icon.SetParent(t, false);
            icon.localPosition = new Vector3(0, sy, sz + 0.03f);
            icon.localRotation = Quaternion.Euler(-90f, 0, 0);
            EmeraldModels.Gem(icon, 0.24f * s, glow, glow);
            // a counter ledge under the screen
            Art.Box(t, new Color(0.3f, 0.33f, 0.36f), new Vector3(0, h * 0.33f, hz + 0.06f * s), new Vector3(hx * 1.9f, 0.04f, 0.14f * s));
            // a big emerald turning over the roof
            m_Holo = new GameObject("holo").transform;
            m_Holo.SetParent(t, false);
            m_Holo.localPosition = new Vector3(0, h + 0.32f * s, 0);
            if (Cfg.LuckyBlock) { var lb = new GameObject("lucky").transform; lb.SetParent(m_Holo, false); lb.localPosition = new Vector3(0, -0.12f * s, 0); LuckyBlockModel.Build(lb, 0.3f * s, true); } // (Lucky Block mode: what it sells, over the roof)
            else EmeraldModels.Gem(m_Holo, 0.36f * s, null, glow);
            foreach (var r in m_Holo.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lg = new GameObject("light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0, sy, 0.6f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = green;
            l.range = 2.4f;
            l.intensity = 0.7f;
            if (AiPsxArt.On) AiPsxArt.Apply(t);
        }

        void Update()
        {
            if (m_Holo == null) return;
            m_Holo.localRotation = Quaternion.Euler(0, Time.time * 60f, 0);
            var p = m_Holo.localPosition;
            m_Holo.localPosition = new Vector3(p.x, (Cfg.SmallBedrock ? 1.57f : 1.96f) + Mathf.Sin(Time.time * 2f) * 0.04f, p.z);
        }
    }

    /// <summary>The emerald and the shop's new things, modelled (ItemModels: tools along +Y with the grip at the origin).</summary>
    public static class EmeraldModels
    {
        static readonly Color k_Gem = new Color(0.2f, 0.85f, 0.42f), k_GemDark = new Color(0.08f, 0.5f, 0.22f);

        /// <summary>An emerald: a cut gem, `size` tall - a long octagonal crown and pavilion (two stretched icospheres), a
        /// flat table on top. `mat` / `glow` override the materials (ghost / glowing copies).</summary>
        public static void Gem(Transform t, float size, Material mat, Material glow)
        {
            var g = Workbench.Glow(k_Gem, 1.2f);
            Art.Part(t, Art.Ico, k_Gem, new Vector3(0, size * 0.55f, 0), new Vector3(size * 0.42f, size * 0.38f, size * 0.42f), new Vector3(0, 22.5f, 0), false, mat ?? g);
            Art.Part(t, Art.Ico, k_GemDark, new Vector3(0, size * 0.3f, 0), new Vector3(size * 0.36f, size * 0.42f, size * 0.36f), default, false, mat ?? Art.Mat(k_GemDark));
            Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, size * 0.86f, 0), new Vector3(size * 0.3f, size * 0.02f, size * 0.3f), default, false, glow ?? Workbench.Glow(new Color(0.6f, 1f, 0.75f), 1.5f));
        }

        /// <summary>The models for Emerald mode's own items (false: not one of them).</summary>
        public static bool Build(Item item, Transform t)
        {
            switch (item)
            {
                case Item.Emerald:
                    Gem(t, 0.34f, null, null);
                    return true;
                case Item.BaseGuardian:
                {
                    // the guardian, small: a copy of the real thing (BaseGuardian.cs)
                    var mini = new GameObject("mini").transform;
                    mini.SetParent(t, false);
                    Guardian.Build(PlayerNet.Local != null ? PlayerNet.Local.Team.Value : 0, mini, null);
                    mini.localScale = Vector3.one * 0.18f;
                    return true;
                }
                case Item.AlienGun:
                {
                    // a revolver-sized ray gun (held like the revolver: grip under the back): a sleek pearly body, a bulb of
                    // glowing green goo where the drum would be, a ribbed barrel ringed in green ending in a glowing tip,
                    // three little fins, a purple grip
                    var shell = new Color(0.55f, 0.6f, 0.68f);
                    var dark = new Color(0.2f, 0.22f, 0.28f);
                    var grip = new Color(0.36f, 0.2f, 0.5f);
                    var goo = EmeraldSpawner.Green;
                    var glow = Workbench.Glow(goo, 1.8f);
                    float cr = Art.Cylinder.bounds.extents.x, ch = Art.Cylinder.bounds.extents.y;
                    Art.Box(t, grip, new Vector3(0, -0.05f, -0.06f), new Vector3(0.04f, 0.12f, 0.055f), new Vector3(-16, 0, 0));
                    Art.Box(t, dark, new Vector3(0, -0.012f, -0.005f), new Vector3(0.01f, 0.032f, 0.04f));                     // (the trigger guard)
                    Art.Part(t, Art.Sphere, shell, new Vector3(0, 0.035f, -0.035f), new Vector3(0.045f, 0.055f, 0.11f));        // (the body)
                    Art.Part(t, Art.Sphere, goo, new Vector3(0, 0.04f, 0.015f), Vector3.one * 0.05f, default, false, glow);      // (the goo bulb)
                    Art.Part(t, Art.Cylinder, dark, new Vector3(0, 0.045f, 0.12f), new Vector3(0.024f / cr, 0.07f / ch, 0.024f / cr), new Vector3(90, 0, 0));
                    for (int k = 0; k < 3; k++)
                        Art.Part(t, Art.Cylinder, goo, new Vector3(0, 0.045f, 0.075f + k * 0.035f), new Vector3((0.034f - k * 0.003f) / cr, 0.006f / ch, (0.034f - k * 0.003f) / cr), new Vector3(90, 0, 0), false, glow);
                    Art.Part(t, Art.Sphere, goo, new Vector3(0, 0.045f, 0.195f), Vector3.one * 0.03f, default, false, glow);   // (the muzzle)
                    Art.Box(t, shell * 0.9f, new Vector3(0, 0.078f, -0.04f), new Vector3(0.008f, 0.03f, 0.05f), new Vector3(20, 0, 0)); // (the top fin)
                    for (int s = -1; s <= 1; s += 2)
                        Art.Box(t, shell * 0.9f, new Vector3(s * 0.035f, 0.03f, -0.06f), new Vector3(0.03f, 0.008f, 0.045f), new Vector3(0, 0, s * -25f));
                    // the sights: a blade at the front, a notch at the back - the highest things on it
                    Art.Box(t, dark, new Vector3(0, 0.07f, 0.17f), new Vector3(0.005f, 0.02f, 0.01f));
                    for (int s = -1; s <= 1; s += 2) Art.Box(t, dark, new Vector3(s * 0.008f, 0.085f, -0.085f), new Vector3(0.006f, 0.02f, 0.01f));
                    return true;
                }
                case Item.LuckyBlock:
                    LuckyBlockModel.Build(t, 0.3f);
                    return true;
                case Item.HighArmor:
                {
                    // a chest plate like Armour's, in dark steel with glowing green seams
                    var steel = new Color(0.25f, 0.28f, 0.32f);
                    var glow = Workbench.Glow(EmeraldSpawner.Green, 1.8f);
                    Art.Box(t, steel, new Vector3(0, 0.25f, 0), new Vector3(0.5f, 0.5f, 0.16f));
                    Art.Box(t, steel * 1.2f, new Vector3(0, 0.33f, 0.06f), new Vector3(0.42f, 0.22f, 0.08f));
                    for (int s = -1; s <= 1; s += 2) Art.Box(t, steel * 0.9f, new Vector3(s * 0.3f, 0.45f, 0), new Vector3(0.14f, 0.12f, 0.2f));
                    Art.Box(t, EmeraldSpawner.Green, new Vector3(0, 0.25f, 0.085f), new Vector3(0.03f, 0.42f, 0.01f), default, false, glow);
                    Art.Box(t, EmeraldSpawner.Green, new Vector3(0, 0.12f, 0.085f), new Vector3(0.4f, 0.025f, 0.01f), default, false, glow);
                    Art.Part(t, Art.Ico, EmeraldSpawner.Green, new Vector3(0, 0.36f, 0.11f), Vector3.one * 0.06f, default, false, glow);
                    return true;
                }
                case Item.Lightsaber:
                {
                    // a metal hilt (grip at the origin) and a long glowing blade with a white core
                    var hilt = new Color(0.62f, 0.64f, 0.68f);
                    var blade = new Color(0.3f, 1f, 0.45f);
                    float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
                    Art.Part(t, Art.Cylinder, hilt, new Vector3(0, 0.04f, 0), new Vector3(0.05f / cr, 0.28f / ch, 0.05f / cr));
                    Art.Part(t, Art.Cylinder, new Color(0.1f, 0.1f, 0.12f), new Vector3(0, 0.0f, 0), new Vector3(0.056f / cr, 0.12f / ch, 0.056f / cr));
                    Art.Part(t, Art.Cylinder, hilt * 0.8f, new Vector3(0, 0.19f, 0), new Vector3(0.065f / cr, 0.03f / ch, 0.065f / cr));
                    Art.Box(t, new Color(0.9f, 0.2f, 0.15f), new Vector3(0.028f, 0.08f, 0), new Vector3(0.012f, 0.02f, 0.02f));
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, 0.75f, 0), new Vector3(0.022f / cr, 1.1f / ch, 0.022f / cr), default, false, Workbench.Glow(Color.white, 2f), "blade core");
                    var halo = Art.Part(t, Art.Cylinder, blade, new Vector3(0, 0.75f, 0), new Vector3(0.05f / cr, 1.12f / ch, 0.05f / cr), default, false, Art.Ghost(new Color(blade.r, blade.g, blade.b, 0.5f)), "blade");
                    halo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    Art.Part(t, Art.Sphere, blade, new Vector3(0, 1.31f, 0), Vector3.one * 0.05f, default, false, Workbench.Glow(blade, 2f));
                    return true;
                }
                case Item.HealBerry:
                {
                    // a little bunch of golden berries glowing pink, on a leafy sprig
                    var g = Workbench.Glow(new Color(1f, 0.45f, 0.7f), 1.6f);
                    Art.Box(t, ItemModels.Leaf, new Vector3(0, 0.12f, 0), new Vector3(0.02f, 0.2f, 0.02f));
                    Vector3[] at = { new Vector3(0.04f, 0.04f, 0), new Vector3(-0.045f, 0.05f, 0.02f), new Vector3(0, 0.09f, -0.03f), new Vector3(0.02f, 0.0f, 0.04f), new Vector3(-0.02f, 0.0f, -0.04f) };
                    foreach (var p in at) Art.Part(t, Art.Sphere, new Color(1f, 0.82f, 0.3f), p, Vector3.one * 0.07f, default, false, g);
                    Art.Part(t, Art.Sphere, ItemModels.Leaf, new Vector3(0.03f, 0.2f, 0), new Vector3(0.09f, 0.02f, 0.05f), new Vector3(0, 0, 30f));
                    return true;
                }
                case Item.LandMine:
                {
                    // a squat round mine in drab olive, a pressure plate on top and a tiny red light
                    var olive = new Color(0.3f, 0.33f, 0.22f);
                    float cr = 2f * Art.Cylinder.bounds.extents.x, ch = 2f * Art.Cylinder.bounds.extents.y;
                    Art.Part(t, Art.Cylinder, olive, new Vector3(0, 0.04f, 0), new Vector3(0.34f / cr, 0.08f / ch, 0.34f / cr));
                    Art.Part(t, Art.Cylinder, olive * 0.75f, new Vector3(0, 0.085f, 0), new Vector3(0.2f / cr, 0.025f / ch, 0.2f / cr));
                    Art.Part(t, Art.Sphere, Color.white, new Vector3(0.12f, 0.085f, 0), Vector3.one * 0.025f, default, false, Workbench.Glow(new Color(1f, 0.15f, 0.1f), 2f));
                    return true;
                }
                case Item.FortCreator:
                {
                    // a little wooden fort in a box: a crate with a tiny tower and walls on its lid, a green ribbon
                    Art.Box(t, Art.Wood, new Vector3(0, 0.1f, 0), new Vector3(0.26f, 0.2f, 0.26f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.205f, 0), new Vector3(0.28f, 0.02f, 0.28f));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Art.Box(t, Art.Wood * 1.05f, new Vector3(s * 0.11f, 0.26f, 0), new Vector3(0.025f, 0.1f, 0.24f));
                        Art.Box(t, Art.Wood * 1.05f, new Vector3(0, 0.26f, s * 0.11f), new Vector3(0.24f, 0.1f, 0.025f));
                    }
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.3f, 0), new Vector3(0.07f, 0.18f, 0.07f));
                    Art.Part(t, Art.Cone, new Color(0.45f, 0.28f, 0.14f), new Vector3(0, 0.39f, 0), new Vector3(0.12f, 0.06f, 0.12f));
                    Art.Box(t, EmeraldSpawner.Green, new Vector3(0, 0.1f, 0.132f), new Vector3(0.04f, 0.2f, 0.005f));
                    return true;
                }
                default: return false;
            }
        }
    }

    public partial class NetGame
    {
        /// <summary>Each emerald spawner's state: the server time its next emerald comes, or -1 while one's waiting there
        /// (synced for the clocks over them: Hud.Emerald.cs).</summary>
        public readonly NetworkList<double> EmeraldAt = new NetworkList<double>();
        readonly List<int> m_EmeraldItem = new List<int>();

        /// <summary>Is an emerald sitting on spawner i?</summary>
        public bool EmeraldWaiting(int i) => i >= 0 && i < EmeraldAt.Count && EmeraldAt[i] < 0;
        /// <summary>Seconds till spawner i's next emerald (0: one's there; -1: not running).</summary>
        public float EmeraldSecondsLeft(int i) => i < 0 || i >= EmeraldAt.Count ? -1f : EmeraldAt[i] < 0 ? 0f : Mathf.Max(0f, (float)(EmeraldAt[i] - NetworkManager.ServerTime.Time));
        /// <summary>How far grown spawner i's next emerald is (0..1).</summary>
        public float EmeraldGrowth(int i) => EmeraldWaiting(i) ? 1f : i < EmeraldAt.Count ? 1f - Mathf.Clamp01(EmeraldSecondsLeft(i) / EmeraldSpawner.EveryOf(i)) : 0f;

        /// <summary>Test hook: emeralds the spawners have grown this match.</summary>
        public static int EmeraldsGrown;

        /// <summary>Server, every frame of a match (from the start): each spawner with no emerald on it grows one every
        /// EmeraldSpawner.EveryOf seconds - counted from when the glass wall drops (while it's up the clocks wait full,
        /// and nothing comes out).</summary>
        void ServerTickEmeralds(double now)
        {
            if (!Cfg.HasEmeralds) return;
            int n = EmeraldSpawner.Tops.Count;
            while (EmeraldAt.Count < n) EmeraldAt.Add(now + EmeraldSpawner.EveryOf(EmeraldAt.Count));
            while (m_EmeraldItem.Count < n) m_EmeraldItem.Add(-1);
            bool wallUp = WallUp;
            for (int i = 0; i < n; i++)
            {
                if (wallUp)
                {
                    // the glass wall's still up: no emeralds yet - every clock waits, full, for the wall to drop
                    double full = now + EmeraldSpawner.EveryOf(i);
                    if (EmeraldAt[i] >= 0 && System.Math.Abs(EmeraldAt[i] - full) > 0.5) EmeraldAt[i] = full;
                    continue;
                }
                if (EmeraldAt[i] < 0)
                {
                    // waiting: until it's picked up (or knocked off the pad), then the clock starts again
                    if (ItemOnPad(m_EmeraldItem[i], EmeraldSpawner.Tops[i])) continue;
                    m_EmeraldItem[i] = -1;
                    EmeraldAt[i] = now + EmeraldSpawner.EveryOf(i);
                    continue;
                }
                if (now < EmeraldAt[i]) continue;
                m_EmeraldItem[i] = ServerDropItem(ItemStack.Of(Item.Emerald, 1), EmeraldSpawner.Tops[i], Vector3.forward, EmeraldSpawner.Tops[i] + Vector3.up, true);
                EmeraldAt[i] = -1;
                EmeraldsGrown++;
                Fx.Server(FxKind.Spawn, EmeraldSpawner.Tops[i], Vector3.up);
            }
        }

        bool ItemOnPad(int id, Vector3 top)
        {
            if (id < 0) return false;
            foreach (var it in Items) if (it.Id == id) return Vector3.Distance(it.Pos, top) < 2.5f;
            return false;
        }

        /// <summary>Test hook: make every spawner's next emerald come now.</summary>
        public void ServerEmeraldsNow()
        {
            double now = NetworkManager.ServerTime.Time;
            for (int i = 0; i < EmeraldAt.Count; i++) if (EmeraldAt[i] >= 0) EmeraldAt[i] = now;
        }

        /// <summary>Emerald mode's airdrop spot: one of the three (the middle, the left, the right) at random - a free bit of
        /// ground near it.</summary>
        public static Vector3 PickEmeraldDropSpot()
        {
            var spots = Cfg.EmeraldDropSpots;
            int pick = Random.Range(0, spots.Length);
            LastEmeraldDropSpot = pick;
            var c = spots[pick];
            for (int tries = 0; tries < 40; tries++)
            {
                var off = Random.insideUnitCircle * (tries < 10 ? 3f : 7f);
                var p = new Vector3(c.x + off.x, 0, c.z + off.y);
                p.y = MapBuilder.Height(p.x, p.z);
                if (pick == 0 && Ball.Instance != null && Vector3.Distance(Ball.Instance.transform.position, p + Vector3.up * 0.5f) < 2.5f) continue;
                if (!ThemeMaps.SpotOk(p)) continue;
                if (Blocked(p + Vector3.up * 1.4f, new Vector3(1f, 0.9f, 1f))) continue;
                return p;
            }
            return pick == 0 ? PickCenterSpot() : new Vector3(c.x, MapBuilder.Height(c.x, c.z), c.z);
        }
        /// <summary>Test hook: which of the three spots the last Emerald mode airdrop picked (0 middle, 1 / 2 the sides).</summary>
        public static int LastEmeraldDropSpot = -1;
    }

    public partial class PlayerNet
    {
        /// <summary>The emerald shop: buy deal `index` (Cfg.EmeraldDeals) with your emeralds, at your own shop.</summary>
        [Rpc(SendTo.Server)]
        public void EmeraldBuyRpc(byte index)
        {
            var g = NetGame.Instance;
            if (!Cfg.HasEmeralds || Dead.Value || InSuddenDeath || g == null || g.S == GameState.GameOver) return;
            var deals = Cfg.EmeraldDeals;
            if (index >= deals.Count) return;
            var d = deals[index];
            if (!Cfg.AtOwnEmeraldShop(Team.Value, transform.position)) { Notify("Emeralds are spent at your emerald shop (left of your alien machine)"); return; }
            string why = EmeraldProblem(d);
            if (why != null) { Notify(why); return; }
            InvOps.Remove(Inv, Item.Emerald, d.Price);
            SpentRpc((byte)Item.Emerald, d.Price);
            if (d.Id == Item.LuckyBlock)
            {
                // Lucky Block mode: a random airdrop item, from all of them (LuckyBlock.cs)
                var loot = Cfg.LuckyBlockLoot();
                ServerGive(loot.Id, loot.Count, loot.Data);
                Notify($"LUCKY BLOCK: {Cfg.ItemName(loot.Id)}{(loot.Count > 1 ? " x" + loot.Count : "")}!");
                LuckyRolls++;
            }
            else if (d.Id == Item.HighArmor)
            {
                int old = ArmorHp.Value;
                ArmorHp.Value = (byte)Mathf.Clamp(Cfg.HighArmorHp, 1, 255);
                if (old > 0 && old < ArmorHp.Value) { } // (the weaker armour's simply replaced)
                Notify($"High Power Armour on: {ArmorHp.Value} extra health, used up before your own");
            }
            else
            {
                ServerGive(d.Id, d.Count, Mathf.Clamp(Cfg.MaxData(d.Id), 0, 255));
                if (d.Extra != Item.None && d.ExtraCount > 0) ServerGive(d.Extra, d.ExtraCount, Mathf.Clamp(Cfg.MaxData(d.Extra), 0, 255));
                if (d.Extra2 != Item.None && d.Extra2Count > 0) ServerGive(d.Extra2, d.Extra2Count, Mathf.Clamp(Cfg.MaxData(d.Extra2), 0, 255));
            }
            EmeraldBuys++;
            if (d.Id != Item.LuckyBlock) CraftedRpc((byte)d.Id); // (a lucky block says what it was instead)
            Fx.Server(FxKind.Craft, Cfg.EmeraldShopPos(Team.Value), new Vector3(Team.Value, 0, 0));
        }

        /// <summary>Test hook: things bought at emerald shops (on the server), and lucky blocks opened.</summary>
        public static int EmeraldBuys, LuckyRolls;

        /// <summary>Why this deal can't be bought right now (null = it can).</summary>
        public string EmeraldProblem(Cfg.EmeraldDeal d)
        {
            if (Count(Item.Emerald) < d.Price) return $"Not enough emeralds ({d.Price} needed)";
            if (d.Id == Item.HighArmor) return ArmorHp.Value >= Cfg.HighArmorHp ? "You're already wearing High Power Armour" : null;
            if (d.Id == Item.LuckyBlock) return InvOps.Space(Inv, Item.LuckyBlock, 0) < 1 ? "Inventory full!" : null; // (a free slot for whatever it is)
            int data = Mathf.Clamp(Cfg.MaxData(d.Id), 0, 255);
            if (InvOps.Space(Inv, d.Id, data) < d.Count) return "Inventory full!";
            // (the pack's other things need room too: a free slot each, if there's nowhere to stack them)
            int free = 0;
            for (int i = 0; i < Inv.Count; i++) if (Inv[i].Empty) free++;
            int need = 0;
            if (d.Extra != Item.None && InvOps.Space(Inv, d.Extra, Mathf.Clamp(Cfg.MaxData(d.Extra), 0, 255)) < d.ExtraCount) return "Inventory full!";
            if (d.Extra != Item.None) need++;
            if (d.Extra2 != Item.None) need++;
            if (need > 0 && free < need + 1) return "Inventory full!";
            return null;
        }

        /// <summary>Instant Heal Berries: eat one, full health.</summary>
        void ServerEatHealBerry()
        {
            ServerConsumeHeld();
            Health.Value = Cfg.MaxHealth;
            Fx.Server(FxKind.Heal, transform.position + Vector3.up, Vector3.up);
            Notify("Full health!");
        }

        /// <summary>The fort creator landed: a fort pops up there - three tall walls and a gate (facing the thrower) round a
        /// lookout tower. Not in an enemy base, on the bedrock or the glass; otherwise it comes back.</summary>
        void ServerFortCreator(Vector3 point)
        {
            if (!Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out var hit, 60f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore)) hit.point = new Vector3(point.x, MapBuilder.Height(point.x, point.z), point.z);
            var pos = hit.point;
            var toMe = transform.position - pos;
            toMe.y = 0;
            var fwd = toMe.sqrMagnitude > 0.01f ? toMe.normalized : Vector3.forward;
            // snap to the nearest of the four ways, so it lines up with everything else
            float yaw = Mathf.Round(Quaternion.LookRotation(fwd).eulerAngles.y / 90f) * 90f;
            var rot = Quaternion.Euler(0, yaw, 0);
            string bad = FortProblem(pos, rot);
            if (bad != null) { Notify(bad + " - here it is back"); ServerGive(Item.FortCreator, 1); return; }
            const float half = 2.85f;
            for (int s = 0; s < 4; s++)
            {
                var r = rot * Quaternion.Euler(0, s * 90f, 0);
                var at = pos + r * Vector3.forward * half;
                at.y = GroundUnder(at, pos.y);
                var go = Instantiate(Bootstrap.I.structurePrefab, at, r);
                go.GetComponent<Structure>().ServerInit(s == 0 ? PieceType.Gate : PieceType.Barrier, Team.Value, default, false);
                go.GetComponent<NetworkObject>().Spawn(true);
            }
            var tower = Instantiate(Bootstrap.I.structurePrefab, pos, rot);
            tower.GetComponent<Structure>().ServerInit(PieceType.Tower, Team.Value, default, false);
            tower.GetComponent<NetworkObject>().Spawn(true);
            Fx.Server(FxKind.Break, pos + Vector3.up * 0.5f, Vector3.up);
            Fx.Server(FxKind.BigPoof, pos + Vector3.up * 1.5f, Vector3.up);
            Forts++;
        }

        /// <summary>Test hook: forts the fort creator has put up (server).</summary>
        public static int Forts;

        float GroundUnder(Vector3 at, float fallback)
        {
            foreach (var h in Physics.RaycastAll(at + Vector3.up * 3f, Vector3.down, 12f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<PlayerNet>() == null) return h.point.y;
            return Mathf.Max(fallback - 2f, MapBuilder.Height(at.x, at.z));
        }

        /// <summary>Why a fort can't go up here (null = it can).</summary>
        string FortProblem(Vector3 pos, Quaternion rot)
        {
            for (int k = 0; k < 9; k++)
            {
                var p = pos + rot * new Vector3((k % 3 - 1) * 3.2f, 0, (k / 3 - 1) * 3.2f);
                int bt = Cfg.BaseTeamAt(p);
                if (bt >= 0 && bt != Team.Value) return "Can't build a fort in the enemy base";
                if (Cfg.PointBlocked(p)) return "Can't build a fort on the bedrock";
            }
            if (MapBuilder.GlassUp && (Mathf.Abs(pos.z) < 6f || new Vector2(pos.x, pos.z).magnitude < MapBuilder.DomeRadius + 6f)) return "Can't build a fort there";
            if (pos.y < MapBuilder.Height(pos.x, pos.z) - 1f || !ThemeMaps.SpotOk(pos)) return "Can't build a fort there";
            return null;
        }
    }
}
