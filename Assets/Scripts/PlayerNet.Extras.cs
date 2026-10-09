using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public enum DevCmd : byte
    {
        DropWallNow, TogglePauseTimer, AddMinute, SubMinute, TimerTo10s, SpawnAirdrop, BallToMe, BallToMiddle,
        GiveWood, GiveStone, GiveArrows, GiveOpItems, GiveCraftables, HealFull, ToggleGod, KillMe, StartSuddenDeath, WinNow,
        RegrowNodes, TpAirdrop, TpEnemyBase, TpMyBase, TpBall, SpawnHorse, SpawnCar, ClearInventory,
        UnlockBench, VictoryCutsceneNow, // (new ones go on the end)
    }

    /// <summary>Crossbow, fort tower, riding (cars and horses), dev tools and proximity voice.</summary>
    public partial class PlayerNet
    {
        bool m_God;
        float m_XbowReadyAt;
        int m_PendingForts;

        // ---------------- crossbow ----------------

        /// <summary>Crossbow: fires the loaded bolt (stack Data 1 = loaded). No draw, hits harder and flies flatter than the bow.</summary>
        [Rpc(SendTo.Server)]
        public void FireCrossbowRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Crossbow || Time.time < m_NextShot || Time.time < m_XbowReadyAt) return;
            var st = HeldStack;
            if (st.Data == 0) return;
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Crossbow, 1, 0);
            m_NextShot = Time.time + 0.3f;
            // every crossbow you have waits for the reload (no firing two loaded crossbows back to back)
            m_XbowReadyAt = Time.time + Cfg.CrossbowReload * 0.8f;
            Reveal();
            m_PendingArrows.Enqueue(Cfg.CrossbowDamage);
            while (m_PendingArrows.Count > 6) m_PendingArrows.Dequeue();
            ArrowVisualRpc(origin, velocity, true);
        }

        /// <summary>Loading a bolt uses up one arrow.</summary>
        [Rpc(SendTo.Server)]
        public void ReloadCrossbowRpc()
        {
            if (Dead.Value || HeldItem != Item.Crossbow || HeldStack.Data != 0 || Time.time < m_XbowReadyAt) return;
            if (!InvOps.Remove(Inv, Item.Arrow, 1)) return;
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Crossbow, 1, 1);
            SpentRpc((byte)Item.Arrow, 1);
        }

        // ---------------- fort tower ----------------

        [Rpc(SendTo.Server)]
        public void ThrowFortRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.FortTower || Time.time < m_NextThrow) return;
            m_NextThrow = Time.time + 0.5f;
            ServerConsumeHeld();
            m_PendingForts = Mathf.Min(m_PendingForts + 1, 3);
            FortVisualRpc(origin, velocity);
        }

        /// <summary>The thrown fort landed: a small tower pops up out of the ground there (not in the enemy base or on bedrock).</summary>
        [Rpc(SendTo.Server)]
        public void FortLandRpc(Vector3 point)
        {
            if (m_PendingForts <= 0) return;
            m_PendingForts--;
            if (!PortalPass.ReachOk(transform.position, point, 200f)) point = transform.position + transform.forward * 3f;
            if (!Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out var hit, 60f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore)) hit.point = new Vector3(point.x, MapBuilder.Height(point.x, point.z), point.z);
            var pos = hit.point;
            int bt = Cfg.BaseTeamAt(pos);
            if ((bt >= 0 && bt != Team.Value) || Cfg.PointBlocked(pos) || MapBuilder.GlassUp && (Mathf.Abs(pos.z) < 3f || new Vector2(pos.x, pos.z).magnitude < MapBuilder.DomeRadius + 3f))
            {
                Notify(bt >= 0 && bt != Team.Value ? "Can't build a fort in the enemy base - here it is back" : "Can't build a fort there - here it is back");
                ServerGive(Item.FortTower, 1);
                return;
            }
            var toMe = transform.position - pos;
            toMe.y = 0;
            float yaw = toMe.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toMe).eulerAngles.y : 0f;
            var go = Instantiate(Bootstrap.I.structurePrefab, pos, Quaternion.Euler(0, yaw, 0));
            go.GetComponent<Structure>().ServerInit(PieceType.Tower, Team.Value, default, false);
            go.GetComponent<NetworkObject>().Spawn(true);
            Fx.Server(FxKind.Break, pos + Vector3.up * 0.5f, Vector3.up);
        }

        [Rpc(SendTo.NotOwner)]
        public void FortVisualRpc(Vector3 origin, Vector3 velocity) { ArrowProjectile.SpawnThrown(Item.FortTower, origin, velocity, this, false); ThrowAnimLocal(); }

        void ThrowAnimLocal() => LocalThrowAnim();

        // ---------------- riding ----------------

        /// <summary>E on a car or horse: get in / on. A horse needs a saddle first (it uses one from your inventory).</summary>
        [Rpc(SendTo.Server)]
        public void MountRpc(NetworkObjectReference target)
        {
            if (Dead.Value || Riding || InSuddenDeath || !target.TryGet(out var no) || !no.TryGetComponent(out Vehicle v) || !v.Rideable) return;
            if (CarryingBall && !v.IsHorse && !v.IsBoat) { Notify("You can't drive with the ball - ride a horse or a boat instead"); return; } // (a boat takes the ball along too)
            if (Vector3.Distance(v.transform.position, transform.position) > Cfg.InteractRange + 3f) return;
            if (v.HasDriver) { Notify("Someone is already riding that"); return; }
            if (v.IsHorse && !v.Saddled.Value)
            {
                int slot = -1;
                for (int i = 0; i < Inv.Count && slot < 0; i++) if (Inv[i].Id == Item.Saddle) slot = i;
                if (slot < 0) { Notify("You need a saddle to ride this horse (craft one in your base)"); return; }
                int saddleTeam = Inv[slot].Data > 0 ? Inv[slot].Data - 1 : Team.Value;
                Inv[slot] = default;
                v.Saddled.Value = true;
                v.SaddleTeam.Value = (byte)saddleTeam;
                SpentRpc((byte)Item.Saddle, 1);
            }
            v.ServerSetDriver(this);
            RidingId.Value = v.NetworkObjectId;
        }

        [Rpc(SendTo.Server)]
        public void DismountRpc() => ServerDismount();

        // ---------------- picking chests and workbenches back up ----------------

        /// <summary>Why this player can't pick this chest / workbench back up (null = they can). Checked on both sides.</summary>
        public string PackUpProblem(Container c)
        {
            if (c == null || !c.IsSpawned) return "";
            if (!(c.Breakable || c.IsWorkbench)) return "";
            if (c.Team.Value != Team.Value) return c.IsWorkbench ? "That's the enemy's trade station" : c.IsDeployable ? $"That's the enemy's {Cfg.ItemName(Container.ItemOf(c.Kind.Value)).ToLower()}" : "That's the enemy's chest";
            if (!c.Empty) return c.Kind.Value == Container.Turret ? "Take its weapon and ammo out first" : "Empty the chest first";
            if (c.IsWorkbench && c.BenchTier == 1 && Workbench.ForTeam(Team.Value, 2) != null) return "Pick up your Advanced Trade Station first";
            return null;
        }

        /// <summary>Held E on an empty chest or workbench of your team's: it comes back into your inventory (dropped at
        /// your feet if there's no room), to be put down somewhere else.</summary>
        [Rpc(SendTo.Server)]
        public void PackUpRpc(NetworkObjectReference target)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || !target.TryGet(out var no) || !no.TryGetComponent(out Container c)) return;
            if (!Tutorial.AllowsFor(this, TutFeature.Deploy)) return;
            if (Vector3.Distance(c.Center, EyePos) > Cfg.InteractRange + 3f) return;
            var problem = PackUpProblem(c);
            if (problem != null) { if (problem.Length > 0) Notify(problem); return; }
            var item = Container.ItemOf(c.Kind.Value); // (a chest, a bench, a bag, a trap, a ladder or a turret)
            var at = c.transform.position;
            c.NetworkObject.Despawn(true);
            int left = ServerGive(item, 1);
            if (left > 0 && NetGame.Instance != null) NetGame.Instance.ServerDropItem(ItemStack.Of(item, left), at + Vector3.up * 0.3f, transform.forward, at + Vector3.up * 0.9f);
            else PickedUpRpc();
            PackedUp++;
            Notify($"Picked up your {Cfg.ItemName(item)}");
        }

        /// <summary>Server, for the tests: chests / workbenches picked back up.</summary>
        public static int PackedUp;

        public void ServerDismount()
        {
            if (!IsServer || !Riding) return;
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(RidingId.Value, out var no) && no.TryGetComponent(out Vehicle v))
            {
                v.ServerSetDriver(null);
                var side = v.transform.position + v.transform.right * 1.6f + Vector3.up * 0.3f;
                TeleportRpc(side, transform.eulerAngles.y);
            }
            RidingId.Value = 0;
        }

        /// <summary>The driver's car hit someone: damage them and throw them out of the way.</summary>
        [Rpc(SendTo.Server)]
        public void CarHitRpc(NetworkObjectReference target, float speed)
        {
            if (!Riding || !target.TryGet(out var no) || !no.TryGetComponent(out PlayerNet p) || p == this || p.Dead.Value) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(RidingId.Value, out var vno) || !vno.TryGetComponent(out Vehicle v) || v.IsHorse) return;
            if (Vector3.Distance(v.transform.position, p.transform.position) > 5f) return;
            float k = Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(1f, Cfg.CarSpeed));
            var push = p.transform.position - v.transform.position;
            push.y = 0;
            push = push.normalized * 0.5f + v.transform.forward * Mathf.Sign(speed);
            p.KnockbackRpc(push.normalized * Cfg.CarKnockback * (0.4f + 0.6f * k) + Vector3.up * 5f);
            p.ServerDamage(Cfg.CarHitDamage * k, this, (byte)Item.Car);
            p.ServerBleed(false, p.transform.position + Vector3.up, push);
            if (p.Dead.Value) KillConfirmRpc();
        }

        [Rpc(SendTo.Owner)]
        public void KnockbackRpc(Vector3 velocity)
        {
            if (Bot.Value) return;
            var pc = GetComponent<PlayerController>();
            if (pc != null) pc.Knockback(velocity);
        }

        // ---------------- dev settings ----------------

        /// <summary>Dev settings from the pause menu (anyone can use them; everybody is told).</summary>
        [Rpc(SendTo.Server)]
        public void DevRpc(DevCmd cmd)
        {
            var g = NetGame.Instance;
            if (g == null) return;
            string who = DisplayName;
            string what = cmd.ToString();
            switch (cmd)
            {
                case DevCmd.DropWallNow: g.DevSkipPhase(GameState.PreBall); break;
                case DevCmd.TogglePauseTimer: g.TimerPaused.Value = !g.TimerPaused.Value; what = g.TimerPaused.Value ? "paused the timer" : "resumed the timer"; break;
                case DevCmd.AddMinute: g.DevAddTime(60f); break;
                case DevCmd.SubMinute: g.DevAddTime(-60f); break;
                case DevCmd.TimerTo10s: g.DevSetTimeLeft(10f); break;
                case DevCmd.SpawnAirdrop: g.DevSpawnAirdrop(); break;
                case DevCmd.BallToMe:
                case DevCmd.BallToMiddle:
                    if (Ball.Instance == null) { Notify("There's no ball right now"); return; }
                    if (cmd == DevCmd.BallToMe) Ball.Instance.ServerDrop(transform.position + transform.forward * 2f + Vector3.up * 1.5f, Vector3.zero);
                    else if (g.WallUp) Ball.Instance.ServerPlaceInDome(); // back under the glass dome
                    else Ball.Instance.ServerDropFromSky(Cfg.BallDropPoint); // (falls in slowly)
                    break;
                case DevCmd.GiveWood: ServerGive(Cfg.GatherItem(Item.Wood), 1000); break;
                case DevCmd.GiveStone: ServerGive(Cfg.GatherItem(Item.Stone), 1000); break;
                case DevCmd.GiveArrows: ServerGive(Item.Arrow, 50); break;
                case DevCmd.GiveOpItems:
                    foreach (var it in Cfg.AirdropChoices) ServerGive(it, 1, Mathf.Clamp(Cfg.MaxData(it), 0, 255));
                    break;
                case DevCmd.GiveCraftables:
                    for (int i = 0; i < Cfg.RecipeCount; i++)
                    {
                        var r = Cfg.GetRecipe(i);
                        if (Count(r.Output) == 0) ServerGive(r.Output, r.Count, Mathf.Clamp(Cfg.MaxData(r.Output), 0, 255));
                    }
                    break;
                case DevCmd.HealFull: Health.Value = Cfg.MaxHealth; break;
                case DevCmd.ToggleGod: m_God = !m_God; what = m_God ? "turned god mode ON" : "turned god mode OFF"; break;
                case DevCmd.KillMe: bool god = m_God; m_God = false; ServerDamage(99999f, null, KillCause.Suicide); m_God = god; break;
                case DevCmd.StartSuddenDeath: g.DevStartSuddenDeath(); break;
                case DevCmd.WinNow: g.EndGame(Team.Value, $"{who} used the dev win button"); break;
                case DevCmd.VictoryCutsceneNow: g.ServerVictoryCutscene(Team.Value, $"{who} used the dev cutscene button"); break;
                case DevCmd.RegrowNodes: g.DevRegrowNodes(); break;
                case DevCmd.UnlockBench: g.ServerUnlockBench(Team.Value, "used the dev setting"); what = "unlocked their trade station"; break;
                case DevCmd.TpAirdrop:
                {
                    var d = g.ActiveDrop;
                    if (d == null) { Notify("No airdrop on the ground"); return; }
                    TeleportRpc(d.transform.position + new Vector3(0, 0.2f, -2.5f), 0f);
                    break;
                }
                case DevCmd.TpEnemyBase: NetGame.SpawnPoint((Team.Value + 1) % Cfg.TeamCount, false, out var ep, out var ey); TeleportRpc(ep, ey); break;
                case DevCmd.TpMyBase: NetGame.SpawnPoint(Team.Value, false, out var mp, out var my); TeleportRpc(mp, my); break;
                case DevCmd.TpBall:
                    if (Ball.Instance == null) { Notify("There's no ball right now"); return; }
                    TeleportRpc(Ball.Instance.transform.position + new Vector3(1.5f, 0.3f, 0), 270f);
                    break;
                case DevCmd.SpawnHorse: Vehicle.ServerSpawn(Vehicle.Horse, transform.position + transform.forward * 3f, transform.eulerAngles.y); break;
                case DevCmd.SpawnCar: Vehicle.ServerSpawn(Vehicle.Car, transform.position + transform.forward * 4f + Vector3.up * 0.5f, transform.eulerAngles.y); break;
                case DevCmd.ClearInventory: for (int i = 0; i < Inv.Count; i++) Inv[i] = default; break;
            }
            g.Broadcast($"[DEV] {who}: {what}");
        }

        // ---------------- proximity voice ----------------

        static readonly System.Collections.Generic.List<ulong> s_VoiceTo = new System.Collections.Generic.List<ulong>();

        /// <summary>Test hook (server): how many players the last voice packet was passed on to.</summary>
        public static int LastVoiceRelayTo;

        /// <summary>
        /// A 20 ms packet of the owner's voice (VoiceCodec: 166 bytes, or a 3-byte end-of-talk marker), unreliable: a late
        /// voice packet is worse than a lost one. The server passes it on only to the other players within
        /// VoiceChat.RelayRange (nobody further away could hear it anyway).
        /// </summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        public void VoiceRpc(byte[] data)
        {
            if (data == null || data.Length < 3 || data.Length > VoiceCodec.PacketBytes) return;
            s_VoiceTo.Clear();
            float r2 = VoiceChat.RelayRange * VoiceChat.RelayRange;
            foreach (var p in All)
                if (p != null && p.IsSpawned && p.OwnerClientId != OwnerClientId && !s_VoiceTo.Contains(p.OwnerClientId)
                    && (p.transform.position - transform.position).sqrMagnitude < r2) s_VoiceTo.Add(p.OwnerClientId);
            LastVoiceRelayTo = s_VoiceTo.Count;
            if (s_VoiceTo.Count > 0) VoiceOutRpc(data, RpcTarget.Group(s_VoiceTo, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        void VoiceOutRpc(byte[] data, RpcParams rpcParams = default) => VoiceChat.Receive(this, data);
    }
}
