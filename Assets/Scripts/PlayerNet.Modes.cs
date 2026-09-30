using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Game-mode extras on the player: the power items (Arsenal / Builder), Builder's crafting wait, the pistol and the
    /// ender pearl.
    /// </summary>
    public partial class PlayerNet
    {
        /// <summary>Builder: the item being made right now (Item as a byte, 0 = nothing), when it started and when it's ready (server time).</summary>
        public readonly NetworkVariable<byte> CraftingItem = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> CraftStartAt = new NetworkVariable<double>(-1);
        public readonly NetworkVariable<double> CraftDoneAt = new NetworkVariable<double>(-1);
        /// <summary>Builder: what's queued up after it (like Rust: one thing at a time, the rest wait their turn).</summary>
        public readonly NetworkList<byte> CraftQueue = new NetworkList<byte>();
        public const int MaxCraftQueue = 8;
        Recipe m_Making;
        readonly System.Collections.Generic.List<Recipe> m_Queued = new System.Collections.Generic.List<Recipe>();

        /// <summary>Power items (any mode that has them) and every craft in Builder: checks, pays, then makes it (right away, or queued up behind whatever is being made).</summary>
        void ServerCraftModes(Recipe r)
        {
            if (!Cfg.CanCraftAt(Team.Value, transform.position, r.Output)) { Notify($"{r.Name} can only be crafted inside your own base"); return; }
            float secs = Cfg.CraftSeconds(r);
            if (secs > 0f && m_Queued.Count >= MaxCraftQueue) { Notify("Your crafting queue is full"); return; }
            if (r.Output == Item.Armor && ArmorHp.Value >= Cfg.ArmorHp) { Notify("You're already wearing full armour"); return; }
            if (r.Output == Item.HeavyArmor && ArmorHp.Value >= Cfg.HeavyArmorHp) { Notify("You're already wearing heavy armour"); return; }
            if (Count(Item.Wood) < r.Wood || Count(Item.Stone) < r.Stone) { Notify($"Not enough resources for {r.Name}"); return; }
            bool noItem = r.Output == Item.Armor || r.Output == Item.HeavyArmor || r.Output == Item.FortifyBuff;
            int data = r.Output == Item.Saddle ? Team.Value + 1 : Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255);
            if (!noItem && secs <= 0f && InvOps.Space(Inv, r.Output, data) < r.Count && !InvOps.HasEmpty(Inv)) { Notify("Inventory full!"); return; }

            // paid up front (like Rust)
            InvOps.Remove(Inv, Item.Wood, r.Wood);
            InvOps.Remove(Inv, Item.Stone, r.Stone);
            if (r.Wood > 0) SpentRpc((byte)Item.Wood, r.Wood);
            if (r.Stone > 0) SpentRpc((byte)Item.Stone, r.Stone);
            if (secs <= 0f) { ServerFinishCraft(r); return; } // instant (the building plan, fortify)
            if (CraftingItem.Value == 0) ServerStartCraft(r);
            else
            {
                m_Queued.Add(r);
                CraftQueue.Add((byte)r.Output);
                Notify($"{r.Name} queued ({m_Queued.Count} waiting)");
            }
        }

        void ServerStartCraft(Recipe r)
        {
            double now = NetworkManager.ServerTime.Time;
            m_Making = r;
            CraftingItem.Value = (byte)r.Output;
            CraftStartAt.Value = now;
            CraftDoneAt.Value = now + Cfg.CraftSeconds(r);
        }

        void ServerTickCraft()
        {
            if (CraftingItem.Value == 0 || NetworkManager.ServerTime.Time < CraftDoneAt.Value) return;
            var done = m_Making;
            CraftingItem.Value = 0;
            CraftStartAt.Value = CraftDoneAt.Value = -1;
            ServerFinishCraft(done);
            // the next one in the queue
            if (m_Queued.Count > 0)
            {
                var next = m_Queued[0];
                m_Queued.RemoveAt(0);
                if (CraftQueue.Count > 0) CraftQueue.RemoveAt(0);
                ServerStartCraft(next);
            }
        }

        void ServerFinishCraft(Recipe r)
        {
            var g = NetGame.Instance;
            switch (r.Output)
            {
                case Item.Armor:
                    ArmorHp.Value = (byte)Mathf.Clamp(Mathf.Max(ArmorHp.Value, Cfg.ArmorHp), 1, 255);
                    Notify($"Armour on: {ArmorHp.Value} extra health, used up before your own");
                    break;
                case Item.HeavyArmor:
                {
                    bool had = ArmorHp.Value > 0;
                    ArmorHp.Value = (byte)Mathf.Clamp(Cfg.HeavyArmorHp, 1, 255);
                    Notify($"Heavy armour on: {ArmorHp.Value} extra health" + (had ? " (your old armour broke off)" : ""));
                    break;
                }
                case Item.FortifyBuff:
                    if (g != null)
                    {
                        int n = g.ServerFortify(Team.Value);
                        g.Broadcast($"{Cfg.TeamLabel(Team.Value)} fortified all their walls - {n} piece{(n == 1 ? "" : "s")} turned to METAL!");
                    }
                    break;
                default:
                {
                    int data = r.Output == Item.Saddle ? Team.Value + 1 : Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255);
                    int left = ServerGive(r.Output, r.Count, data);
                    // no room any more (the inventory filled up while it was being made): it drops at your feet
                    if (left > 0 && g != null) g.ServerDropItem(ItemStack.Of(r.Output, left, data), transform.position + transform.forward, transform.forward, EyePos);
                    break;
                }
            }
            CraftedRpc((byte)r.Output);
            Fx.Server(FxKind.Craft, Cfg.MachinePos(Team.Value), new Vector3(Team.Value, 0, 0));
        }

        // ---------------- pistol ----------------

        float m_NextPistol;

        /// <summary>Pistol: hitscan (the client says what its shot hit, like the sniper). 200 to the head, 95 to the body.</summary>
        [Rpc(SendTo.Server)]
        public void FirePistolRpc(bool hasTarget, NetworkObjectReference target, Vector3 point, Vector3 dir)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Pistol || Time.time < m_NextPistol) return;
            var st = HeldStack;
            if (st.Data <= 0) return;
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Pistol, 1, st.Data - 1);
            m_NextPistol = Time.time + Cfg.PistolFireRate * 0.8f;
            Reveal();
            if (Vector3.Distance(point, EyePos) > 250f) point = EyePos + dir.normalized * 100f;
            Fx.Server(FxKind.SniperTracer, EyePos + dir.normalized * 0.5f - Vector3.up * 0.15f, point);
            if (!hasTarget || !target.TryGet(out var no) || !GameAllowsCombat) return;
            if (no.TryGetComponent(out PlayerNet p) && p != this && !p.Dead.Value)
            {
                bool head = p.IsHeadshot(point);
                if (head && p.HelmetHp.Value > 0)
                {
                    p.HelmetHp.Value = 0;
                    Fx.Server(FxKind.HelmetBreak, p.EyePos, Vector3.up);
                    p.Notify("Your helmet stopped a headshot and broke!");
                    Notify("Their helmet stopped your headshot!");
                    return;
                }
                p.ServerDamage(head ? Cfg.PistolHeadDamage : Cfg.PistolBodyDamage, this);
                Fx.Server(head ? FxKind.BloodHead : FxKind.Blood, point, dir, OwnerClientId);
                if (p.Dead.Value) KillConfirmRpc();
            }
            else if (no.TryGetComponent(out Vehicle v)) v.ServerDamage(Cfg.PistolBodyDamage, this);
            else if (no.TryGetComponent(out Structure s) && s.Team.Value != Team.Value && s.Tier.Value == 0) s.ServerDamage(10f);
        }

        /// <summary>Fills the magazine from your pistol ammo.</summary>
        [Rpc(SendTo.Server)]
        public void ReloadPistolRpc()
        {
            if (Dead.Value || HeldItem != Item.Pistol) return;
            var st = HeldStack;
            int want = Cfg.PistolMag - st.Data;
            if (want <= 0) return;
            int have = Count(Item.PistolAmmo);
            int take = Mathf.Min(want, have);
            if (take <= 0) { Notify("The pistol is out of shots"); return; }
            InvOps.Remove(Inv, Item.PistolAmmo, take);
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Pistol, 1, st.Data + take);
            SpentRpc((byte)Item.PistolAmmo, take);
        }

        // ---------------- ender pearl ----------------

        /// <summary>Where a thrown ender pearl landed: you're teleported there (a little damage, like Minecraft).</summary>
        void ServerPearlLanded(Vector3 point, Vector3 normal)
        {
            if (Dead.Value) return;
            float half = Cfg.MapHalf - 2f;
            var to = point + normal.normalized * 0.6f;
            // stay on the map, and never under the ground
            to.x = Mathf.Clamp(to.x, -half, half);
            to.z = Mathf.Clamp(to.z, -half, half);
            to.y = Mathf.Max(to.y, MapBuilder.Height(to.x, to.z) + 0.05f);
            ServerDismount();
            Fx.Server(FxKind.Drink, transform.position, Vector3.up);
            TeleportRpc(to, transform.eulerAngles.y);
            Fx.Server(FxKind.Drink, to, Vector3.up);
            ServerDamage(5f, null);
        }
    }
}
