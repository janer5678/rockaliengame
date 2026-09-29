using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public enum DevCmd : byte
    {
        DropWallNow, TogglePauseTimer, AddMinute, SubMinute, TimerTo10s, SpawnAirdrop, BallToMe, BallToMiddle,
        GiveWood, GiveStone, GiveArrows, GiveOpItems, GiveCraftables, HealFull, ToggleGod, KillMe, StartSuddenDeath, WinNow,
        RegrowNodes, TpAirdrop, TpEnemyBase, TpMyBase, TpBall, SpawnHorse, SpawnCar, ClearInventory,
    }

    /// <summary>Crossbow, fort tower, riding (cars and horses), dev tools and proximity voice.</summary>
    public partial class PlayerNet
    {
        bool m_God;
        int m_PendingForts;

        // ---------------- crossbow ----------------

        /// <summary>Crossbow: fires the loaded bolt (stack Data 1 = loaded). No draw, hits harder and flies flatter than the bow.</summary>
        [Rpc(SendTo.Server)]
        public void FireCrossbowRpc(Vector3 origin, Vector3 velocity)
        {
            if (Dead.Value || CarryingBall || InSuddenDeath || HeldItem != Item.Crossbow || Time.time < m_NextShot) return;
            var st = HeldStack;
            if (st.Data == 0) return;
            Inv[HeldSlot.Value] = ItemStack.Of(Item.Crossbow, 1, 0);
            m_NextShot = Time.time + 0.3f;
            Reveal();
            m_PendingArrows.Enqueue(Cfg.CrossbowDamage);
            while (m_PendingArrows.Count > 6) m_PendingArrows.Dequeue();
            ArrowVisualRpc(origin, velocity);
        }

        /// <summary>Loading a bolt uses up one arrow.</summary>
        [Rpc(SendTo.Server)]
        public void ReloadCrossbowRpc()
        {
            if (Dead.Value || HeldItem != Item.Crossbow || HeldStack.Data != 0) return;
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
            if (Vector3.Distance(point, transform.position) > 200f) point = transform.position + transform.forward * 3f;
            if (!Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out var hit, 60f, ~(1 << HitboxLayer), QueryTriggerInteraction.Ignore)) hit.point = new Vector3(point.x, MapBuilder.Height(point.x, point.z), point.z);
            var pos = hit.point;
            int bt = Cfg.BaseTeamAt(pos);
            if ((bt >= 0 && bt != Team.Value) || Cfg.PointBlocked(pos) || MapBuilder.GlassUp && Mathf.Abs(pos.z) < 3f)
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
        public void FortVisualRpc(Vector3 origin, Vector3 velocity) => ArrowProjectile.SpawnThrown(Item.FortTower, origin, velocity, this, false);

        // ---------------- riding ----------------

        /// <summary>E on a car or horse: get in / on. A horse needs a saddle first (it uses one from your inventory).</summary>
        [Rpc(SendTo.Server)]
        public void MountRpc(NetworkObjectReference target)
        {
            if (Dead.Value || Riding || CarryingBall || InSuddenDeath || !target.TryGet(out var no) || !no.TryGetComponent(out Vehicle v) || !v.Rideable) return;
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
            p.ServerDamage(Cfg.CarHitDamage * k, this);
            Fx.Server(FxKind.Blood, p.transform.position + Vector3.up, push);
            if (p.Dead.Value) KillConfirmRpc();
        }

        [Rpc(SendTo.Owner)]
        public void KnockbackRpc(Vector3 velocity)
        {
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
            string who = Cfg.TeamName[Team.Value];
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
                    if (Ball.Instance == null) { Notify("The ball hasn't dropped yet"); return; }
                    Ball.Instance.ServerDrop(cmd == DevCmd.BallToMe ? transform.position + transform.forward * 2f + Vector3.up * 1.5f : Cfg.BallDropPoint, Vector3.zero);
                    break;
                case DevCmd.GiveWood: ServerGive(Item.Wood, 1000); break;
                case DevCmd.GiveStone: ServerGive(Item.Stone, 1000); break;
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
                case DevCmd.KillMe: bool god = m_God; m_God = false; ServerDamage(99999f, null); m_God = god; break;
                case DevCmd.StartSuddenDeath: g.DevStartSuddenDeath(); break;
                case DevCmd.WinNow: g.EndGame(Team.Value, $"{who} used the dev win button"); break;
                case DevCmd.RegrowNodes: g.DevRegrowNodes(); break;
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
                    if (Ball.Instance == null) { Notify("The ball hasn't dropped yet"); return; }
                    TeleportRpc(Ball.Instance.transform.position + new Vector3(1.5f, 0.3f, 0), 270f);
                    break;
                case DevCmd.SpawnHorse: Vehicle.ServerSpawn(Vehicle.Horse, transform.position + transform.forward * 3f, transform.eulerAngles.y); break;
                case DevCmd.SpawnCar: Vehicle.ServerSpawn(Vehicle.Car, transform.position + transform.forward * 4f + Vector3.up * 0.5f, transform.eulerAngles.y); break;
                case DevCmd.ClearInventory: for (int i = 0; i < Inv.Count; i++) Inv[i] = default; break;
            }
            g.Broadcast($"[DEV] {who}: {what}");
        }

        // ---------------- proximity voice ----------------

        /// <summary>A chunk of compressed mic audio from the owner; the server passes it on to everyone else.</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        public void VoiceRpc(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > 1100) return;
            VoiceOutRpc(data);
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void VoiceOutRpc(byte[] data) => VoiceChat.Receive(this, data);
    }
}
