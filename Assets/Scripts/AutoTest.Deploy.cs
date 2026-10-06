using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest deploy -host -solo -rules autowood: the Trade Stations' new placeables and the new items - a sleeping
        /// bag, a bear trap, a ladder, a large gate and an auto turret go down; you climb the ladder; an enemy's bear trap
        /// snaps you and holds you; an enemy turret with a pistol and ammo shoots you; the gate opens; Extreme Speed Juice
        /// makes you fast; dead, you respawn at your sleeping bag (and it waits a minute after); armour 25 / heavy 100;
        /// airdrop crates hold an explosive, 500-1000 wood and one more airdrop item.
        /// </summary>
        IEnumerator DeployRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = 1 - team;
            g.TimerPaused.Value = true;
            Check(Cfg.ArmorHp == 25 && Cfg.HeavyArmorHp == 50, $"wooden armour {Cfg.ArmorHp}, heavy armour {Cfg.HeavyArmorHp}");
            Check(Cfg.ItemName(Item.Workbench2) == "Advanced Trade Station" && Cfg.ItemName(Item.Barrier) == "Large Wall", "renamed: Advanced Trade Station, Large Wall");
            Check(Cfg.CraftTier(Item.SleepingBag) == 1 && Cfg.CraftTier(Item.BearTrap) == 1 && Cfg.CraftTier(Item.Ladder) == 1 && Cfg.CraftTier(Item.LargeGate) == 1 && Cfg.CraftTier(Item.AutoTurret) == 2,
                "the bag, trap, ladder and gate are Trade Station items, the turret an Advanced one");
            {
                var crate = NetGame.RollAirdropCrate();
                bool boom = crate.Count == 3 && (crate[0].Id == Item.C4 || crate[0].Id == Item.RocketLauncher || crate[0].Id == Item.BombBush);
                bool wood = crate.Count == 3 && crate[1].Id == Item.Wood && crate[1].Count >= 500 && crate[1].Count <= 1000;
                Check(boom && wood && crate[2].Id != Item.Wood, $"an airdrop crate: an explosive, 500-1000 wood and one more ({string.Join(", ", crate.ConvertAll(s => s.Id + " x" + s.Count))})");
            }
            var spawn = Cfg.SpawnPos(team);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            // somewhere in our base each can go (looked for, so the base's own things don't get in the way)
            Vector3 SpotFor(Item kind, float yaw)
            {
                for (int r = 2; r < 14; r++)
                    for (int a = 0; a < 16; a++)
                    {
                        var p = spawn - back * (2f + r * 1.2f) + side * Mathf.Sin(a * 0.39f) * r * 1.4f;
                        if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var h, 12f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) p.y = h.point.y;
                        if (PlayerNet.DeployProblem(kind, team, p, yaw) == null) return p;
                    }
                return Vector3.zero;
            }
            float faceYaw = Quaternion.LookRotation(-back).eulerAngles.y;
            foreach (var kind in new[] { Item.SleepingBag, Item.Ladder, Item.LargeGate, Item.AutoTurret })
            {
                me.ServerGive(kind, 1);
                yield return Hold(me, kind);
                var at = SpotFor(kind, faceYaw);
                Check(at != Vector3.zero, $"a spot in our base for the {Cfg.ItemName(kind)}");
                if (at == Vector3.zero) continue;
                pc.LocalTeleport(at + back * 2f + Vector3.up * 0.2f, faceYaw);
                yield return new WaitForSeconds(0.4f);
                me.PlaceDeployableRpc((byte)kind, at, faceYaw);
                yield return new WaitForSeconds(0.6f);
                Check(me.Count(kind) == 0, $"put the {Cfg.ItemName(kind)} down");
                // the ladder: walk into it and hold W - up you go (before the wide gate goes up anywhere near it)
                if (kind == Item.Ladder)
                {
                    Container placedLadder = null;
                    foreach (var c in Container.All) if (c != null && c.Team.Value == team && c.Kind.Value == Container.Ladder) placedLadder = c;
                    if (placedLadder != null)
                    {
                        var l = placedLadder.transform;
                        pc.LocalTeleport(l.position - l.forward * 0.35f + Vector3.up * 0.1f, l.eulerAngles.y);
                        yield return new WaitForSeconds(0.3f);
                        float y0 = me.transform.position.y;
                        Binds.TestHold(Bind.Forward, true);
                        float peak = y0;
                        for (float until = Time.time + 1.6f; Time.time < until; ) { peak = Mathf.Max(peak, me.transform.position.y); yield return null; }
                        Binds.TestReleaseAll();
                        Check(peak - y0 > Deployables.LadderHeight - 0.6f, $"climbing the ladder: up {peak - y0:0.0} m at the top");
                        yield return new WaitForSeconds(1f);
                    }
                }
            }
            Container bag = null, ladder = null;
            Structure gate = null;
            foreach (var c in Container.All) if (c != null && c.Team.Value == team) { if (c.Kind.Value == Container.SleepBag) bag = c; else if (c.Kind.Value == Container.Ladder) ladder = c; }
            foreach (var s in Structure.All) if (s != null && s.PType == PieceType.Gate) gate = s;
            Check(bag != null && ladder != null && gate != null, $"the bag, the ladder and the gate are there ({bag != null}/{ladder != null}/{gate != null})");
            yield return Shot("deploy_placed");

            // the gate opens (E, our team)
            if (gate != null)
            {
                me.ToggleDoorRpc(gate.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                Check(gate.DoorOpen.Value, "E opens our large gate");
            }

            // an ENEMY bear trap where we step: snapped, hurt, held
            {
                var at = OnGround(spawn - back * 6f + side * 4f, 0f);
                var go = Instantiate(Bootstrap.I.containerPrefab, at, Quaternion.identity);
                go.GetComponent<Container>().ServerInit(Container.Trap, enemy, 0, null);
                go.GetComponent<NetworkObject>().Spawn(true);
                me.Health.Value = Cfg.MaxHealth;
                yield return new WaitForSeconds(0.3f);
                pc.LocalTeleport(at + Vector3.up * 0.1f, 0f);
                yield return new WaitForSeconds(0.5f);
                var trap = go.GetComponent<Container>();
                Check(trap.Flag.Value == 1 && me.Health.Value < Cfg.MaxHealth && me.Trapped, $"an enemy bear trap snapped shut on us ({me.Health.Value:0} HP, held: {me.Trapped})");
                var p0 = me.transform.position;
                Binds.TestHold(Bind.Forward, true);
                yield return new WaitForSeconds(0.8f);
                Binds.TestReleaseAll();
                Check(Vector3.Distance(me.transform.position, p0) < 0.3f, "held in the trap: we can't walk off");
                yield return Shot("deploy_trap");
                yield return new WaitForSeconds(Cfg.BearTrapHold);
                me.Health.Value = Cfg.MaxHealth;
            }

            // an ENEMY turret with a pistol and ammo: it warns, then shoots us
            {
                var at = OnGround(spawn - back * 6f - side * 5f, 0f);
                var tgo = Instantiate(Bootstrap.I.containerPrefab, at, Quaternion.LookRotation(back));
                tgo.GetComponent<Container>().ServerInit(Container.Turret, enemy, 2, new System.Collections.Generic.List<ItemStack> { ItemStack.Of(Item.Pistol, 1, Cfg.PistolMag), ItemStack.Of(Item.PistolAmmo, 30) });
                tgo.GetComponent<NetworkObject>().Spawn(true);
                var turret = tgo.GetComponent<Container>();
                pc.LocalTeleport(OnGround(at + back * 9f), Quaternion.LookRotation(-back).eulerAngles.y);
                me.Health.Value = Cfg.MaxHealth;
                float until = Time.time + 6f;
                while (Time.time < until && me.Health.Value >= Cfg.MaxHealth) yield return null;
                Check(me.Health.Value < Cfg.MaxHealth && turret.Slots[1].Count < 30, $"an enemy turret shot us ({me.Health.Value:0} HP, ammo left {turret.Slots[1].Count})");
                yield return Shot("deploy_turret");
                turret.NetworkObject.Despawn(true);
                me.Health.Value = Cfg.MaxHealth;
            }

            // Extreme Speed Juice
            {
                me.ServerGive(Item.SpeedJuice, 1);
                yield return Hold(me, Item.SpeedJuice);
                me.UseItemRpc();
                yield return new WaitForSeconds(0.4f);
                Check(me.Juiced, "Extreme Speed Juice: drunk, and we're fast");
            }

            // dead: respawn at our sleeping bag (then it waits)
            if (bag != null)
            {
                me.DevRpc(DevCmd.DropWallNow);
                yield return new WaitForSeconds(1f);
                me.SuicideRpc();
                float until = Time.time + Cfg.RespawnTime + 4f;
                while (!me.ChoosingRespawn && Time.time < until) yield return null;
                yield return Shot("deploy_respawn_bags");
                me.RespawnAtBagRpc(bag.NetworkObject);
                yield return new WaitForSeconds(0.8f);
                Check(!me.Dead.Value && Vector3.Distance(me.transform.position, bag.transform.position) < 3f && bag.ReadyAt.Value > NetworkManager.Singleton.ServerTime.Time + 30,
                    $"respawned at our sleeping bag ({Vector3.Distance(me.transform.position, bag.transform.position):0.0} m off), and it waits before the next");
            }
            Log("deploy test done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);

            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.4f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), name + ".png"));
                Log("shot " + name);
                yield return null; yield return null;
            }
        }
    }
}
