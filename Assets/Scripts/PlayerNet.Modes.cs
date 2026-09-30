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
        /// <summary>Builder: the item being made right now (Item as a byte, 0 = nothing) and when it's ready (server time).</summary>
        public readonly NetworkVariable<byte> CraftingItem = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> CraftDoneAt = new NetworkVariable<double>(-1);
        Recipe m_Making;
        bool m_IsMaking;

        /// <summary>Power items (any mode that has them) and every craft in Builder: checks, pays, then makes it (right away, or after Builder's wait).</summary>
        void ServerCraftModes(Recipe r)
        {
            if (!Cfg.CanCraftAt(Team.Value, transform.position, r.Output)) { Notify($"{r.Name} can only be crafted inside your own base"); return; }
            if (m_IsMaking) { Notify($"Still making {Cfg.ItemName((Item)CraftingItem.Value)} - wait for it"); return; }
            var g = NetGame.Instance;
            if (r.Output == Item.Armor && ArmorHp.Value >= Cfg.ArmorHp) { Notify("You're already wearing full armour"); return; }
            if (r.Output == Item.HeavyArmor && ArmorHp.Value >= Cfg.HeavyArmorHp) { Notify("You're already wearing heavy armour"); return; }
            if (Count(Item.Wood) < r.Wood || Count(Item.Stone) < r.Stone) { Notify($"Not enough resources for {r.Name}"); return; }
            bool noItem = r.Output == Item.Armor || r.Output == Item.HeavyArmor || r.Output == Item.FortifyBuff;
            int data = r.Output == Item.Saddle ? Team.Value + 1 : Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255);
            if (!noItem && InvOps.Space(Inv, r.Output, data) < r.Count && !InvOps.HasEmpty(Inv)) { Notify("Inventory full!"); return; }

            InvOps.Remove(Inv, Item.Wood, r.Wood);
            InvOps.Remove(Inv, Item.Stone, r.Stone);
            if (r.Wood > 0) SpentRpc((byte)Item.Wood, r.Wood);
            if (r.Stone > 0) SpentRpc((byte)Item.Stone, r.Stone);
            float secs = Cfg.CraftSeconds(r);
            if (secs <= 0f) { ServerFinishCraft(r); return; }
            // Builder: it takes a while
            m_Making = r;
            m_IsMaking = true;
            CraftingItem.Value = (byte)r.Output;
            CraftDoneAt.Value = NetworkManager.ServerTime.Time + secs;
            Notify($"Making {r.Name} ({secs:0} s)");
        }

        void ServerTickCraft()
        {
            if (!m_IsMaking || NetworkManager.ServerTime.Time < CraftDoneAt.Value) return;
            m_IsMaking = false;
            CraftingItem.Value = 0;
            CraftDoneAt.Value = -1;
            ServerFinishCraft(m_Making);
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
                        g.Broadcast($"{Cfg.TeamLabel(Team.Value)} fortified all their walls - {n} piece{(n == 1 ? "" : "s")} turned to stone!");
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

        /// <summary>Pistol: a fast, hard-hitting round from the magazine (stack Data = rounds loaded).</summary>
        [Rpc(SendTo.Server)]
        public void FirePistolRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Pistol || Time.time < m_NextPistol) return;
            var st = HeldStack;
            if (st.Data <= 0) return;
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Pistol, 1, st.Data - 1);
            m_NextPistol = Time.time + Cfg.PistolFireRate * 0.8f;
            Reveal();
            // negative = a bullet (it doesn't leave an arrow behind)
            m_PendingArrows.Enqueue(-Cfg.PistolDamage);
            while (m_PendingArrows.Count > 12) m_PendingArrows.Dequeue();
            BulletVisualRpc(origin, velocity);
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
            if (take <= 0) { Notify("No pistol ammo (buy some in the power items menu)"); return; }
            InvOps.Remove(Inv, Item.PistolAmmo, take);
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Pistol, 1, st.Data + take);
            SpentRpc((byte)Item.PistolAmmo, take);
        }

        [Rpc(SendTo.NotOwner)]
        void BulletVisualRpc(Vector3 origin, Vector3 velocity)
        {
            ArrowProjectile.SpawnBullet(origin, velocity, this, false, Cfg.PistolDamage);
            Sfx.Play(Sfx.Sniper, origin, 0.55f, 0.1f, 120f);
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
