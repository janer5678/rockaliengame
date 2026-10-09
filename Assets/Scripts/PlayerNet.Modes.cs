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
            if (r.Output == Item.Helmet && (HelmetHp.Value > 0 || CraftingItem.Value == (byte)Item.Helmet || CraftQueue.Contains((byte)Item.Helmet))) { Notify("You're already wearing a helmet"); return; }
            if (r.Output == Item.FortifyBuff && Cfg.FortifyLevel(Team.Value) >= Cfg.MaxFortify) { Notify("Your walls are already armoured - fully fortified"); return; }
            if (r.Output == Item.WoodGenBuff && Cfg.WoodGenLevel(Team.Value) >= Cfg.MaxWoodGen) { Notify("Your wood gen is already maxed out"); return; }
            if (!CanAfford(r)) { Notify($"Not enough resources for {r.Name}"); return; }
            bool noItem = r.Output == Item.Armor || r.Output == Item.HeavyArmor || r.Output == Item.Helmet || r.Output == Item.FortifyBuff || r.Output == Item.WoodGenBuff;
            int data = r.Output == Item.Saddle ? Team.Value + 1 : Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255);
            if (!noItem && secs <= 0f && InvOps.Space(Inv, r.Output, data) < r.Count && !InvOps.HasEmpty(Inv)) { Notify("Inventory full!"); return; }

            // paid up front (like Rust)
            ServerPay(r);
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
                case Item.Helmet:
                    // the alien helmet goes straight on, like armour (one already on: this one goes in your bag)
                    if (HelmetHp.Value > 0) { if (ServerGive(Item.Helmet, 1, 1) > 0 && g != null) g.ServerDropItem(ItemStack.Of(Item.Helmet, 1, 1), transform.position + transform.forward, transform.forward, EyePos); }
                    else { HelmetHp.Value = 1; Notify("Helmet on: it stops one headshot completely"); }
                    break;
                case Item.WoodGenBuff:
                case Item.FortifyBuff: ServerApplyBaseUpgrade(r.Output); break; // (Builder's Fortify; elsewhere they're UPGRADES - Upgrades.cs)
                default:
                {
                    // guns come empty: the revolver and shotgun need their ammo bought
                    int data = r.Output == Item.Saddle ? Team.Value + 1 : r.Output == Item.Revolver ? 0 : Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255);
                    int left = ServerGive(r.Output, r.Count, data);
                    // no room any more (the inventory filled up while it was being made): it drops at your feet
                    if (left > 0 && g != null) g.ServerDropItem(ItemStack.Of(r.Output, left, data), transform.position + transform.forward, transform.forward, EyePos);
                    break;
                }
            }
            CraftedRpc((byte)r.Output);
            Fx.Server(FxKind.Craft, Cfg.MachinePos(Team.Value), new Vector3(Team.Value, 0, 0));
        }

        /// <summary>Tutorial: this player has walked up to the glass wall (the wall drops once everyone has - see Tutorial.ServerTick).</summary>
        public readonly NetworkVariable<bool> TutAtWall = new NetworkVariable<bool>();

        /// <summary>Tutorial: the step this player's guide is on (Tutorial.FinishedStep once done), written by their own client,
        /// so the server knows what's unlocked for them (crafting, building, the workbench - Tutorial.AllowsFor).</summary>
        public readonly NetworkVariable<byte> TutStep = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Tutorial: where the server built the enemy hut this player's raid steps break into (in the enemy's base;
        /// zero = not yet), so their guide can tell its pieces from anything else of that team's (Tutorial.ServerHut).</summary>
        public readonly NetworkVariable<Vector3> TutHut = new NetworkVariable<Vector3>();

        /// <summary>Tutorial mode only: 1 = I'm at the glass wall step (the server drops the wall once every player is);
        /// 2 = the eat step: make me hungry (half health) so there's something to heal; 3 and up: the later steps' props
        /// (training dummies, an airdrop, the hut to raid, the wood for an upgrade, the finale, a joiner's kit) -
        /// Tutorial.ServerAction.</summary>
        [Rpc(SendTo.Server)]
        public void TutorialRpc(byte action)
        {
            if (!Cfg.Tutorial || NetGame.Instance == null) return;
            Tutorial.ServerAction(this, action);
        }

        /// <summary>Tutorial: the server tells this player's guide that something it's waiting for happened (Tutorial.OnEvent:
        /// 1 = you hit a training dummy, 2 = you finished one off; arg bit 1 = from range (else up close), bit 2 = on the head).</summary>
        [Rpc(SendTo.Owner)]
        public void TutEventRpc(byte what, byte arg) { if (!Bot.Value) Tutorial.OnEvent(what, arg); }

        float m_NextSuicide;

        /// <summary>Pause menu: kill yourself (you drop everything, like any death, and respawn as normal).</summary>
        [Rpc(SendTo.Server)]
        public void SuicideRpc()
        {
            if (Dead.Value || !GameAllowsCombat) return;
            // not again within 30 seconds
            if (Time.time < m_NextSuicide) { Notify($"You can't kill yourself again for {Mathf.CeilToInt(m_NextSuicide - Time.time)} s"); return; }
            m_NextSuicide = Time.time + 30f;
            ArmorHp.Value = 0;
            ServerDie(null, KillCause.Suicide); // (the kill feed says so)
        }

        // ---------------- pistol ----------------

        float m_NextPistol;

        /// <summary>Pistol / revolver: hitscan (the client says what its shot hit, like the sniper), each with its own head / body damage.</summary>
        [Rpc(SendTo.Server)]
        public void FirePistolRpc(bool hasTarget, NetworkObjectReference target, Vector3 point, Vector3 dir)
        {
            var gun = HeldItem;
            if (Dead.Value || CarryingBall || InSuddenDeath || !Cfg.IsGun(gun) || Time.time < m_NextPistol) return;
            var st = HeldStack;
            if (st.Data <= 0) return;
            Inv[HeldSlot.Value] = ItemStack.Of(gun, 1, st.Data - 1);
            m_NextPistol = Time.time + Cfg.GunFireRate(gun) * 0.8f;
            Reveal();
            if (!PortalPass.ReachOk(EyePos, point, 250f)) point = EyePos + dir.normalized * 100f; // (a shot through a portal can land far off)
            // (not back to the shooter: they already drew their own tracer the moment they fired - two of them was the "double tracer")
            PortalPass.ServerTracer(gun == Item.Revolver ? FxKind.RevolverTracer : FxKind.PistolTracer, EyePos + dir.normalized * 0.5f - Vector3.up * 0.15f, EyePos, dir, point, OwnerClientId); // (bends through portals)
            if (hasTarget && target.TryGet(out var tree)) ResourceNode.ServerStruck(tree); // (a bullet in a tree sends its birds up)
            if (!hasTarget) NetGame.Instance?.ServerMaybeHitMachine(point, this, false); // (Bedwars: a shot into an enemy machine)
            if (!hasTarget || !target.TryGet(out var no) || !GameAllowsCombat) return;
            if (no.TryGetComponent(out PlayerNet p) && p != this && !p.Dead.Value)
            {
                if (GlassBetween(transform.position, p.transform.position)) return;
                bool head = p.IsHeadshot(point);
                if (head && p.HelmetHp.Value > 0)
                {
                    p.HelmetHp.Value = 0;
                    Fx.Server(FxKind.HelmetBreak, p.EyePos, Vector3.up);
                    p.Notify("Your helmet stopped a headshot and broke!");
                    Notify("Their helmet stopped your headshot!");
                    return;
                }
                p.ServerMarkHead(head);
                p.ServerDamage(head ? Cfg.GunHead(gun) : Cfg.GunBody(gun), this);
                p.ServerBleed(head, point, dir, OwnerClientId);
                if (p.Dead.Value) KillConfirmRpc();
            }
            else if (no.TryGetComponent(out Vehicle v)) v.ServerDamage(Cfg.GunBody(gun) * v.HeadMul(point), this);
            else if (no.TryGetComponent(out Structure s) && s.Tier.Value == 0) s.ServerDamageAt(10f, point);
        }

        /// <summary>Fills the pistol's / revolver's magazine from its ammo.</summary>
        [Rpc(SendTo.Server)]
        public void ReloadPistolRpc()
        {
            var gun = HeldItem;
            if (Dead.Value || !Cfg.IsGun(gun)) return;
            var st = HeldStack;
            int want = Cfg.GunMag(gun) - st.Data;
            if (want <= 0) return;
            var ammo = Cfg.GunAmmo(gun);
            int take = Mathf.Min(want, Count(ammo));
            if (take <= 0) { Notify($"The {Cfg.ItemName(gun).ToLower()} is out of ammo"); return; }
            InvOps.Remove(Inv, ammo, take);
            Inv[HeldSlot.Value] = ItemStack.Of(gun, 1, st.Data + take);
            SpentRpc((byte)ammo, take);
        }

        // ---------------- waterpipe shotgun ----------------

        float m_NextShotgunShot, m_ShotgunShotAt = -10f;
        int m_ShotgunPelletsLeft;
        Vector3 m_ShotgunFrom;

        /// <summary>Fires the loaded shell. The shooter then reports which players its pellets hit (ShotgunHitRpc).</summary>
        [Rpc(SendTo.Server)]
        public void FireShotgunRpc(Vector3 dir)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Shotgun || Time.time < m_NextShotgunShot) return;
            var st = HeldStack;
            if (st.Data == 0) return;
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Shotgun, 1, 0);
            m_NextShotgunShot = Time.time + 0.3f;
            m_ShotgunShotAt = Time.time;
            m_ShotgunPelletsLeft = Mathf.Clamp(Cfg.ShotgunPellets, 1, 30);
            m_ShotgunFrom = EyePos;
            Reveal();
            // everyone else sees one tracer; the shooter already drew its own pellets
            Fx.Server(FxKind.ShotgunTracer, EyePos + dir.normalized * 0.5f - Vector3.up * 0.15f, EyePos + dir.normalized * Mathf.Min(Cfg.ShotgunRange, 30f), OwnerClientId);
        }

        /// <summary>Pellets from the last shot that hit a player: the server works out the damage from how far away they were.</summary>
        [Rpc(SendTo.Server)]
        public void ShotgunHitRpc(NetworkObjectReference target, Vector3 point, byte body, byte head)
        {
            if (Dead.Value || Time.time - m_ShotgunShotAt > 1f || !GameAllowsCombat) return;
            int n = Mathf.Min(body + head, m_ShotgunPelletsLeft);
            if (n <= 0) return;
            m_ShotgunPelletsLeft -= n;
            head = (byte)Mathf.Min(head, n);
            body = (byte)(n - head);
            if (!target.TryGet(out var no)) return;
            if (no.TryGetComponent(out PlayerNet p) && p != this && !p.Dead.Value)
            {
                if (GlassBetween(transform.position, p.transform.position)) return;
                float dist = Vector3.Distance(m_ShotgunFrom, p.transform.position + Vector3.up * 1.1f);
                if (dist > Cfg.ShotgunRange + 2f) { if (!PortalPass.AnyLinked) return; dist = Cfg.ShotgunRange * 0.5f; } // (through a portal: far away, but fair)
                float per = Cfg.ShotgunPelletDamage * Cfg.ShotgunFalloff(Mathf.Max(0f, dist - 0.4f));
                float dmg = per * body;
                if (head > 0)
                {
                    if (p.HelmetHp.Value > 0)
                    {
                        // the helmet takes the head pellets and breaks
                        p.HelmetHp.Value = 0;
                        Fx.Server(FxKind.HelmetBreak, p.EyePos, Vector3.up);
                        p.Notify("Your helmet stopped a headshot and broke!");
                        Notify("Their helmet stopped your headshot!");
                    }
                    else dmg += per * Cfg.ShotgunHeadMul * head;
                }
                if (dmg <= 0f) return;
                p.ServerMarkHead(head > 0);
                p.ServerDamage(dmg, this);
                p.ServerBleed(head > 0, point, (point - m_ShotgunFrom).normalized, OwnerClientId);
                if (p.Dead.Value) KillConfirmRpc();
            }
            else if (no.TryGetComponent(out Vehicle v)) v.ServerDamage(Cfg.ShotgunPelletDamage * n * Cfg.ShotgunFalloff(Vector3.Distance(m_ShotgunFrom, v.transform.position)), this);
        }

        /// <summary>Loads one shell.</summary>
        [Rpc(SendTo.Server)]
        public void ReloadShotgunRpc()
        {
            if (Dead.Value || HeldItem != Item.Shotgun || HeldStack.Data != 0) return;
            if (!InvOps.Remove(Inv, Item.ShotgunShell, 1)) return;
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Shotgun, 1, 1);
            SpentRpc((byte)Item.ShotgunShell, 1);
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
            ServerDamage(5f, null, (byte)Item.EnderPearl);
        }
    }
}
