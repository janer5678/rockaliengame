using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// EMERALD MODE's shop, 2026-10-28: its rows are things for your WHOLE TEAM now (Cfg.EmeraldDeals), not weapons -
    /// - INTRUDER ALARM: whenever an enemy comes into your base, everyone on your team hears a siren and sees who it is
    ///   (AlarmCooldown seconds between alarms: PlayerNet.ServerTickIntruder, NetGame.ServerIntruder, Sfx.Alarm);
    /// - AUTO ARMOUR: everyone on your team respawns wearing Armour (Cfg.HeavyArmorHp) every time - and whoever's alive
    ///   without any gets it on the spot (PlayerNet.ServerAutoArmor);
    /// - CRAFT ANYWHERE: your team crafts anywhere on the map, not just in your base (Cfg.CanCraftAt, the TAB list);
    /// - RAID DISCOUNT: the raiding things (Cfg.IsRaidable: the ram, C4, ladders...) at half price for your team (Cfg.RaidPriced);
    /// - INSTANT REPAIR (not a perk - it happens when it's bought): every damaged piece of your base goes back to full
    ///   health, doors and bars back on, and everything of yours destroyed this match comes back as it was
    ///   (NetGame.ServerRepairBase) - it can't be bought while an enemy's in your base.
    /// The perks are team bits in NetGame.TeamPerks (Cfg.HasPerk). The rows are Items so the shop can draw them (icons,
    /// names) but they're never things in a bag (Cfg.Removed).
    /// </summary>
    public static partial class Cfg
    {
        /// <summary>Emerald shop prices (emeralds): the intruder alarm, auto armour, craft anywhere, the raid discount and an instant repair.</summary>
        [Tune("Emerald shop")] public static int EmeraldPriceAlarm = 1, EmeraldPriceAutoArmor = 2, EmeraldPriceCraftAnywhere = 2, EmeraldPriceRaidDiscount = 2, EmeraldPriceRepair = 1;
        /// <summary>The intruder alarm: seconds between alarms for a team.</summary>
        [Tune("Emerald mode")] public static float AlarmCooldown = 5f;

        /// <summary>The emerald shop's team rows (and the instant repair): rows, never things in a bag.</summary>
        public static bool IsEmeraldPerk(Item i) => i == Item.BaseAlarm || i == Item.AutoArmor || i == Item.CraftAnywherePerk || i == Item.RaidDiscount || i == Item.BaseRepair;
        /// <summary>A perk's bit (0-3) within its team's four in NetGame.TeamPerks; -1 for the instant repair (nothing's kept).</summary>
        public static int PerkBit(Item i) => i == Item.BaseAlarm ? 0 : i == Item.AutoArmor ? 1 : i == Item.CraftAnywherePerk ? 2 : i == Item.RaidDiscount ? 3 : -1;
        /// <summary>Does `team` have this perk (bought at its emerald shop)?</summary>
        public static bool HasPerk(int team, Item perk)
        {
            var g = NetGame.Instance;
            int bit = PerkBit(perk);
            return g != null && bit >= 0 && team >= 0 && team < 4 && (g.TeamPerks.Value & (1 << (team * 4 + bit))) != 0;
        }

        /// <summary>The raiding things the RAID DISCOUNT halves: what breaks into a base, or gets over its walls.</summary>
        public static bool IsRaidable(Item i) => i == Item.Ram || i == Item.HeavyRam || i == Item.C4 || i == Item.Ladder || i == Item.RocketLauncher;
        /// <summary>A recipe priced for a team: the raiding things at half price (rounded up) when the team has the RAID DISCOUNT.</summary>
        public static Recipe RaidPriced(Recipe r, int team)
        {
            if (!IsRaidable(r.Output) || !HasPerk(team, Item.RaidDiscount)) return r;
            r.Wood = (r.Wood + 1) / 2;
            r.Stone = (r.Stone + 1) / 2;
            r.Dust = (r.Dust + 1) / 2;
            return r;
        }

        /// <summary>What a perk does, in a few words (the broadcast when it's bought).</summary>
        public static string PerkEffect(Item perk)
        {
            switch (perk)
            {
                case Item.BaseAlarm: return "an alarm whenever an enemy comes into their base";
                case Item.AutoArmor: return "they respawn in armour every time";
                case Item.CraftAnywherePerk: return "they craft anywhere on the map";
                case Item.RaidDiscount: return "raiding things at half price";
                default: return "";
            }
        }

        /// <summary>The short yellow tag the shop row shows for a perk that can't be bought right now (from PlayerNet.PerkProblem's reason).</summary>
        public static string PerkShort(string why) => why == null ? null : why.Contains("already") ? "your team has it" : why.Contains("enemy") ? "enemy in your base" : why.Contains("Nothing") ? "nothing to mend" : why;
    }

    public partial class NetGame
    {
        /// <summary>Emerald mode's team perks: bit (team * 4 + Cfg.PerkBit) for each perk a team has bought.</summary>
        public readonly NetworkVariable<int> TeamPerks = new NetworkVariable<int>();
        readonly float[] m_AlarmNext = new float[4];
        /// <summary>Test hooks: intruder alarms raised, bases repaired (server).</summary>
        public static int AlarmsRaised, Repairs;

        /// <summary>Server: `team` has this perk from now on.</summary>
        public void ServerGrantPerk(int team, Item perk)
        {
            int bit = Cfg.PerkBit(perk);
            if (bit >= 0 && team >= 0 && team < 4) TeamPerks.Value |= 1 << (team * 4 + bit);
        }

        /// <summary>Is a live enemy of `team` inside its base right now?</summary>
        public static bool EnemyInBase(int team)
        {
            foreach (var p in PlayerNet.All)
                if (p != null && p.IsSpawned && !p.Dead.Value && p.Team.Value != team && Cfg.BaseTeamAt(p.transform.position) == team) return true;
            return false;
        }

        /// <summary>Server: `who` just came into `team`'s base - its INTRUDER ALARM goes off for the team (Cfg.AlarmCooldown between alarms).</summary>
        public void ServerIntruder(int team, PlayerNet who)
        {
            if (team < 0 || team >= 4 || !Cfg.HasPerk(team, Item.BaseAlarm) || Time.time < m_AlarmNext[team]) return;
            if (S != GameState.PreBall && S != GameState.BallLive) return;
            m_AlarmNext[team] = Time.time + Mathf.Max(0.5f, Cfg.AlarmCooldown);
            AlarmsRaised++;
            string name = who != null ? who.DisplayName : "someone";
            AlarmRpc((byte)team, new FixedString128Bytes(string.IsNullOrEmpty(name) ? "An enemy" : name.Length > 40 ? name.Substring(0, 40) : name));
        }

        /// <summary>Every screen on that team: the siren, and who it is.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        void AlarmRpc(byte team, FixedString128Bytes who)
        {
            var me = PlayerNet.Local;
            if (me == null || me.Team.Value != team) return;
            string text = $"INTRUDER ALARM: {who} is in your base!";
            Hud.Push(text);
            Hud.LastAlarmAt = Time.time;
            Hud.LastAlarmText = text;
            Sfx.Play2D(Sfx.Alarm, 0.9f, 0f);
        }

        static bool HasLeaf(Structure s) => s.PType == PieceType.Doorway || s.PType == PieceType.Gate || s.PType == PieceType.Window;

        /// <summary>Is there anything of `team`'s to mend (damaged, a door or bars broken off) or to bring back?</summary>
        public bool AnythingToMend(int team)
        {
            foreach (var s in Structure.All)
                if (s != null && s.IsSpawned && s.Team.Value == team && (s.Health.Value < s.MaxHp - 0.5f || (HasLeaf(s) && s.DoorHealth.Value < s.DoorMaxHp - 0.5f))) return true;
            foreach (var g in m_GonePieces) if (g.Team == team) return true;
            return false;
        }

        /// <summary>Server: INSTANT REPAIR - every piece of `team`'s back to full health (doors and bars back on: `mended`),
        /// and everything of theirs destroyed this match back as it was (returned).</summary>
        public int ServerRepairBase(int team, out int mended)
        {
            mended = 0;
            foreach (var s in Structure.All)
            {
                if (s == null || !s.IsSpawned || s.Team.Value != team) continue;
                bool leaf = HasLeaf(s);
                if (s.Health.Value >= s.MaxHp - 0.5f && (!leaf || s.DoorHealth.Value >= s.DoorMaxHp - 0.5f)) continue;
                s.Health.Value = s.MaxHp;
                if (leaf) s.DoorHealth.Value = s.DoorMaxHp;
                mended++;
            }
            int back = ServerRebuildBase(team, float.MaxValue); // (everything remembered: Emerald mode keeps the match's - NetGame.Wacky.cs)
            Repairs++;
            Fx.Server(FxKind.Craft, Cfg.MachinePos(team), new Vector3(team, 0, 0));
            return back;
        }
    }

    public partial class PlayerNet
    {
        int m_InBaseOf = -1;
        /// <summary>Test hooks (server): perks bought, med pens used.</summary>
        public static int PerksBought, MedPensUsed;

        /// <summary>Why this shop row can't be bought right now (null = it can): the team has the perk already; the repair
        /// with an enemy in the base, or nothing to mend.</summary>
        public string PerkProblem(Item perk)
        {
            var g = NetGame.Instance;
            if (perk == Item.BaseRepair)
            {
                if (NetGame.EnemyInBase(Team.Value)) return "You can't repair while an enemy's in your base";
                if (g == null || !g.AnythingToMend(Team.Value)) return "Nothing of your base needs mending";
                return null;
            }
            return Cfg.HasPerk(Team.Value, perk) ? $"Your team already has {Cfg.ItemName(perk)}" : null;
        }

        /// <summary>Server: the emeralds are paid - the perk is the team's, or the base is repaired right now.</summary>
        void ServerBuyPerk(Item perk)
        {
            var g = NetGame.Instance;
            if (g == null) return;
            if (perk == Item.BaseRepair)
            {
                int back = g.ServerRepairBase(Team.Value, out int mended);
                g.Broadcast($"{Cfg.TeamLabel(Team.Value)} repaired their base: {mended} piece{(mended == 1 ? "" : "s")} mended, {back} back");
                return;
            }
            g.ServerGrantPerk(Team.Value, perk);
            PerksBought++;
            if (perk == Item.AutoArmor) foreach (var p in All) if (p != null && p.Team.Value == Team.Value && !p.Dead.Value) p.ServerAutoArmor();
            g.Broadcast($"{Cfg.TeamLabel(Team.Value)} bought {Cfg.ItemName(perk).ToUpper()}: {Cfg.PerkEffect(perk)}");
        }

        /// <summary>AUTO ARMOUR: Armour on (unless something as good is on already). Every respawn, and the moment it's bought.</summary>
        public void ServerAutoArmor()
        {
            if (!Cfg.HasPerk(Team.Value, Item.AutoArmor)) return;
            int hp = Mathf.Clamp(Cfg.HeavyArmorHp, 1, 255);
            if (ArmorHp.Value < hp) ArmorHp.Value = (byte)hp;
        }

        /// <summary>Server, every frame: which base this player is in - coming into an enemy's sets off their INTRUDER ALARM.</summary>
        void ServerTickIntruder()
        {
            if (Dead.Value || !IsSpawned) { m_InBaseOf = -1; return; }
            int t = Cfg.BaseTeamAt(transform.position);
            if (t == m_InBaseOf) return;
            m_InBaseOf = t;
            if (t >= 0 && t != Team.Value && NetGame.Instance != null) NetGame.Instance.ServerIntruder(t, this);
        }

        /// <summary>The med pen (MedPen.cs): a berry's heal at once; the pen's used up.</summary>
        void ServerMedPen()
        {
            if (Health.Value >= Cfg.MaxHealth) { Notify("You're already at full health"); return; }
            m_NextEat = Time.time + 0.8f;
            ServerConsumeHeld();
            Health.Value = Mathf.Min(Cfg.MaxHealth, Health.Value + Cfg.BerryHeal);
            Fx.Server(FxKind.Heal, transform.position + Vector3.up, Vector3.up);
            MedPensUsed++;
        }
    }

    public partial class Hud
    {
        /// <summary>Test hooks: the last intruder alarm shown on this screen.</summary>
        public static float LastAlarmAt = -10f;
        public static string LastAlarmText = "";
    }

    /// <summary>The emerald shop's team rows as little models (their icons are rendered from these: ItemIcons).</summary>
    public static class EmeraldPerkModels
    {
        public static bool Build(Item item, Transform t)
        {
            var steel = new Color(0.62f, 0.66f, 0.72f);
            var dark = new Color(0.16f, 0.17f, 0.2f);
            var gold = new Color(1f, 0.82f, 0.25f);
            switch (item)
            {
                case Item.BaseAlarm:
                {
                    // a siren: a dark box with a glowing red dome and a little horn
                    Art.Box(t, dark, new Vector3(0, 0.04f, 0), new Vector3(0.26f, 0.08f, 0.2f));
                    Art.Part(t, Art.Sphere, new Color(1f, 0.2f, 0.15f), new Vector3(0, 0.1f, 0), new Vector3(0.16f, 0.14f, 0.16f), default, false, Workbench.Glow(new Color(1f, 0.25f, 0.2f), 2f));
                    Art.Box(t, steel, new Vector3(0.1f, 0.12f, 0), new Vector3(0.05f, 0.05f, 0.08f), new Vector3(0, 0, -20f));
                    return true;
                }
                case Item.AutoArmor:
                {
                    // a chest plate with a small upward glint: armour that comes back by itself
                    Art.Box(t, steel, new Vector3(0, 0.12f, 0), new Vector3(0.22f, 0.24f, 0.07f));
                    Art.Box(t, steel * 0.85f, new Vector3(0, 0.12f, 0.04f), new Vector3(0.06f, 0.2f, 0.01f));
                    Art.Box(t, steel * 0.85f, new Vector3(0, 0.12f, 0.04f), new Vector3(0.16f, 0.06f, 0.01f));
                    Art.Box(t, gold, new Vector3(0, 0.29f, 0), new Vector3(0.04f, 0.07f, 0.04f));
                    Art.Part(t, Art.Cone, gold, new Vector3(0, 0.345f, 0), new Vector3(0.11f, 0.07f, 0.11f));
                    return true;
                }
                case Item.CraftAnywherePerk:
                {
                    // a little bench top with a globe over it: crafting anywhere
                    Art.Box(t, Art.Wood, new Vector3(0, 0.05f, 0), new Vector3(0.26f, 0.04f, 0.16f));
                    for (int k = -1; k <= 1; k += 2) Art.Box(t, Art.DarkWood, new Vector3(k * 0.1f, 0.015f, 0), new Vector3(0.03f, 0.03f, 0.14f));
                    Art.Part(t, Art.Sphere, new Color(0.25f, 0.55f, 1f), new Vector3(0, 0.19f, 0), Vector3.one * 0.18f);
                    Art.Part(t, Art.Sphere, new Color(0.3f, 0.8f, 0.35f), new Vector3(0.04f, 0.21f, -0.07f), Vector3.one * 0.07f);
                    Art.Part(t, Art.Sphere, new Color(0.3f, 0.8f, 0.35f), new Vector3(-0.05f, 0.16f, -0.06f), Vector3.one * 0.06f);
                    return true;
                }
                case Item.RaidDiscount:
                {
                    // a ram's log with a gold half-coin on it: raiding things, half price
                    Art.Part(t, Art.Cylinder, Art.Wood, new Vector3(0, 0.07f, 0), new Vector3(0.09f, 0.14f, 0.09f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, steel, new Vector3(0, 0.07f, 0.15f), new Vector3(0.1f, 0.02f, 0.1f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, gold, new Vector3(0.02f, 0.2f, 0), new Vector3(0.14f, 0.012f, 0.14f), new Vector3(90, 0, 0));
                    Art.Box(t, dark, new Vector3(0.09f, 0.2f, 0), new Vector3(0.14f, 0.15f, 0.03f));
                    return true;
                }
                case Item.BaseRepair:
                {
                    // a stone brick with a hammer across it: the base mended
                    Art.Box(t, Art.Stone, new Vector3(0, 0.07f, 0), new Vector3(0.24f, 0.14f, 0.14f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.17f, 0), new Vector3(0.03f, 0.03f, 0.26f), new Vector3(0, 30f, 0));
                    Art.Box(t, steel, new Vector3(-0.06f, 0.17f, 0.1f), new Vector3(0.1f, 0.06f, 0.06f), new Vector3(0, 30f, 0));
                    return true;
                }
                default: return false;
            }
        }
    }
}
