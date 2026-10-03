using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// More of the -autotest feel batch (AutoTest.Feel.cs runs these): jump buffering and coyote time, a tree can't slide,
    /// horse blood falls to the ground instead of hanging in the air when the horse runs off, players bleed (a body burst
    /// for hits with no splash of their own, a big burst on a kill - sent to every screen), doors sound like the base's
    /// tier, no new wall straight back where one was just broken, holding E picks up an empty chest / workbench of yours
    /// (a tap still opens a chest), and riding, E loots before it gets you off.
    /// </summary>
    public partial class AutoTest
    {
        // ------------------------------------------------------------------ jump buffering + coyote time

        IEnumerator JumpFeelTests(PlayerNet me, PlayerController pc, int team)
        {
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear run for the jumps");
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.5f);
            Check(Cfg.JumpBuffer > 0.05f && Cfg.CoyoteTime > 0.05f, $"jump buffer {Cfg.JumpBuffer:0.00} s, coyote time {Cfg.CoyoteTime:0.00} s");

            // buffered: jump, and press it again just before landing - it jumps again the moment it touches down
            int j0 = pc.Jumps, b0 = pc.BufferedJumps;
            Binds.TestPress(Bind.Jump);
            float until = Time.time + 1f;
            while (Time.time < until && pc.Jumps == j0) yield return null;
            Check(pc.Jumps == j0 + 1, "a jump");
            // wait for the way down, a moment (~0.1 s) before landing - well inside the buffer, a few frames early even
            // at a low frame rate
            until = Time.time + 2f;
            while (Time.time < until && !(pc.VelY < -2f && me.transform.position.y - MapBuilder.Height(me.transform.position.x, me.transform.position.z) < 0.8f)) yield return null;
            Binds.TestPress(Bind.Jump);
            until = Time.time + 0.6f;
            while (Time.time < until && pc.Jumps == j0 + 1) yield return null;
            Check(pc.Jumps == j0 + 2 && pc.BufferedJumps == b0 + 1, $"jump pressed just before landing jumps again on landing (jumps {pc.Jumps - j0}, buffered {pc.BufferedJumps - b0})");
            // pressed high up in the air (long before landing): it's forgotten - no jump on landing
            until = Time.time + 1f;
            while (Time.time < until && pc.VelY > 0f) yield return null; // (the top of the jump)
            int j1 = pc.Jumps;
            Binds.TestPress(Bind.Jump);
            yield return new WaitForSeconds(1.2f);
            Check(pc.Jumps == j1, $"a jump pressed at the top of a jump doesn't fire on landing ({pc.Jumps - j1})");

            // coyote time: walk off the edge of a foundation and jump just after leaving it
            var fwd = Quaternion.Euler(0, laneYaw, 0) * Vector3.forward;
            var fpos = OnGround(lane + fwd * 6f, 0f);
            var go = Instantiate(Bootstrap.I.structurePrefab, fpos, Quaternion.Euler(0, laneYaw, 0));
            var found = go.GetComponent<Structure>();
            found.ServerInit(PieceType.Foundation, team, default, false);
            go.GetComponent<NetworkObject>().Spawn(true);
            yield return new WaitForSeconds(0.6f);
            int c0 = pc.CoyoteJumps;
            bool tried = false;
            for (int attempt = 0; attempt < 3 && pc.CoyoteJumps == c0; attempt++)
            {
                pc.LocalTeleport(fpos + Vector3.up * 1.1f - fwd * 0.5f, laneYaw);
                yield return new WaitForSeconds(0.5f);
                Binds.TestHold(Bind.Forward, true);
                until = Time.time + 2f;
                bool wasGrounded = true;
                while (Time.time < until)
                {
                    bool g = pc.Grounded;
                    if (wasGrounded && !g && pc.VelY <= 0f) { Binds.TestPress(Bind.Jump); tried = true; break; }
                    wasGrounded = g;
                    yield return null;
                }
                yield return new WaitForSeconds(0.3f);
                Binds.TestReleaseAll();
                yield return new WaitForSeconds(0.8f);
            }
            Check(tried && pc.CoyoteJumps > c0, $"walked off an edge and jumped a moment later: it still jumps (coyote jumps {pc.CoyoteJumps - c0})");
            // ... but only once: no second jump in mid-air
            int j2 = pc.Jumps;
            pc.LocalTeleport(fpos + Vector3.up * 1.1f - fwd * 0.5f, laneYaw);
            yield return new WaitForSeconds(0.5f);
            Binds.TestPress(Bind.Jump);
            yield return new WaitForSeconds(0.25f);
            Binds.TestPress(Bind.Jump);
            yield return new WaitForSeconds(0.25f);
            Check(pc.Jumps == j2 + 1, $"no double jump in mid-air ({pc.Jumps - j2} jumps)");
            yield return new WaitForSeconds(1f);
            if (found != null && found.IsSpawned) found.NetworkObject.Despawn(true);
            Binds.TestReleaseAll();
        }

        // ------------------------------------------------------------------ the tree can't slide

        IEnumerator TreeNoSlideTest(PlayerNet me, PlayerController pc, int team)
        {
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear run for the tree slide");
            me.ServerGive(Item.TreeCamo, 1);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.TreeCamo);
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.4f);
            int r0 = pc.SlidesRefusedAsTree;
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.8f);
            Binds.TestHold(Bind.Slide, true);
            bool slid = false;
            float until = Time.time + 0.8f;
            while (Time.time < until) { slid |= pc.Sliding; yield return null; }
            Binds.TestReleaseAll();
            Check(me.TreeCamo && !slid && pc.SlidesRefusedAsTree > r0, $"a tree can't slide (slid {slid}, refused {pc.SlidesRefusedAsTree - r0})");
            yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.TreeCamo) me.Inv[i] = default;
            yield return Hold(me, Item.Rock);
        }

        // ------------------------------------------------------------------ blood: horses and players

        IEnumerator BloodTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear run for the bleeding horse");
            var fwd = Quaternion.Euler(0, laneYaw, 0) * Vector3.forward;
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            var horse = Vehicle.ServerSpawn(Vehicle.Horse, OnGround(lane + fwd * 5f, 0.2f), laneYaw);
            yield return new WaitForSeconds(0.6f);
            // hit it a few times (from behind, so it bolts away from us) and watch where the blood ends up
            for (int i = 0; i < 4; i++)
            {
                if (horse == null || !horse.IsSpawned) break;
                horse.ServerDamage(1f, me);
                yield return new WaitForSeconds(0.12f);
            }
            float until = Time.time + 2.5f;
            int onHorse = 0, hanging = 0, landed = 0;
            while (Time.time < until)
            {
                foreach (var p in FxParticle.Landed)
                {
                    if (p == null || !p.HasLanded) continue;
                    if (p.LandedOn == null || FxParticle.Moves(p.LandedOn)) onHorse++;
                }
                yield return null;
            }
            var mask = ~(1 << PlayerNet.HitboxLayer);
            foreach (var p in FxParticle.Landed)
            {
                if (p == null || !p.HasLanded) continue;
                landed++;
                if (!Physics.CheckSphere(p.transform.position, 0.08f, mask, QueryTriggerInteraction.Ignore)) hanging++;
            }
            var moved = horse != null ? Vector3.Distance(horse.transform.position, lane + fwd * 5f) : 0f;
            Check(moved > 3f, $"the hurt horse ran off ({moved:0.0} m)");
            Check(onHorse == 0 && landed > 0 && hanging == 0, $"the horse's blood landed on the ground, none stuck to the horse or left hanging in the air ({landed} on the ground, {onHorse} on the horse, {hanging} in the air)");
            if (horse != null && horse.IsSpawned) horse.NetworkObject.Despawn(true);

            // a player hurt with no splash of its own (a fall, an explosion): a burst out of the body, on every screen
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.4f);
            int bc = Fx.BloodCount, kc = Fx.BloodKillCount;
            me.ServerDamage(5f, null, KillCause.Fall);
            yield return new WaitForSeconds(0.4f);
            Check(Fx.BloodCount == bc + 1, $"hurt: blood comes out of the body ({Fx.BloodCount - bc})");
            // a weapon hit with its own splash isn't doubled up
            bc = Fx.BloodCount;
            me.ServerBleed(false, me.BleedPos, Vector3.forward);
            me.ServerDamage(5f, null, KillCause.Fall);
            yield return new WaitForSeconds(0.4f);
            Check(Fx.BloodCount == bc + 1, $"a hit with its own blood splash doesn't get a second one ({Fx.BloodCount - bc})");
            // a kill: a big burst
            me.ServerKill(null, KillCause.Fall);
            yield return new WaitForSeconds(0.1f);
            Check(Fx.BloodKillCount == kc + 1, $"a kill bursts with blood ({Fx.BloodKillCount - kc})");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "blood_kill.png"));
            yield return new WaitForSeconds(0.5f);
            me.ServerRespawn(false);
            yield return new WaitForSeconds(1f);
            Check(!me.Dead.Value, "back up again");
            me.Health.Value = Cfg.MaxHealth;
        }

        // ------------------------------------------------------------------ doors sound like the base's tier

        IEnumerator DoorSoundTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            var clips = new HashSet<AudioClip>();
            for (int t = 0; t < 4; t++) { clips.Add(Sfx.DoorSound(t, true)); clips.Add(Sfx.DoorSound(t, false)); }
            clips.Remove(null);
            Check(clips.Count == 8, $"every tier has its own door opening and shutting sound ({clips.Count} of 8)");
            Check(ClearLane(team, out var lane, out float laneYaw), "room for a door");
            var fwd = Quaternion.Euler(0, laneYaw, 0) * Vector3.forward;
            var go = Instantiate(Bootstrap.I.structurePrefab, OnGround(lane + fwd * 4f, 0f), Quaternion.Euler(0, laneYaw, 0));
            var door = go.GetComponent<Structure>();
            door.ServerInit(PieceType.Doorway, team, default, false);
            go.GetComponent<NetworkObject>().Spawn(true);
            yield return new WaitForSeconds(0.5f);
            string[] names = { "wood", "stone", "metal", "refined" };
            for (int t = 0; t < 4; t++)
            {
                if (t > 0) { door.ServerUpgrade(t); yield return new WaitForSeconds(0.3f); }
                int expect = Mathf.Max(t, Cfg.FortifyLevel(team));
                int n0 = Structure.DoorSounds;
                door.DoorOpen.Value = true;
                yield return new WaitForSeconds(0.3f);
                bool opened = Structure.DoorSounds == n0 + 1 && Structure.LastDoorSoundOpen && Structure.LastDoorSoundTier == expect;
                door.DoorOpen.Value = false;
                yield return new WaitForSeconds(0.3f);
                bool shut = Structure.DoorSounds == n0 + 2 && !Structure.LastDoorSoundOpen && Structure.LastDoorSoundTier == expect;
                Check(opened && shut, $"a {names[t]} door sounds {names[expect]} opening and shutting ({Structure.DoorSounds - n0} sounds, tier {Structure.LastDoorSoundTier})");
            }
            if (door != null && door.IsSpawned) door.NetworkObject.Despawn(true);
        }

        // ------------------------------------------------------------------ no wall straight back where one was broken

        IEnumerator WallRebuildTest(PlayerNet me, PlayerController pc, int team)
        {
            me.ServerGive(Cfg.CurrencyItem, 200);
            me.ServerGive(Item.BuildingPlan, 1);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.BuildingPlan);
            FreeCell(team, 2, out int ci, out int cj);
            pc.LocalTeleport(BuildGrid.CellCenter(ci, cj) + new Vector3(-4f, 0.1f, -1.5f), 0);
            yield return new WaitForSeconds(0.3f);
            me.PlaceRpc((byte)PieceType.Foundation, ci, cj, 0, 0);
            yield return new WaitForSeconds(0.8f);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(0.8f);
            var key = new PieceKey(BuildGrid.KindOf(PieceType.Wall), ci, cj, 0, 1);
            BuildGrid.Registry.TryGetValue(key, out var wall);
            Check(wall != null, $"built a wall ({key})");
            if (wall == null) yield break;
            wall.ServerDamage(99999f);
            yield return new WaitForSeconds(0.3f);
            int r0 = PlayerNet.WallRebuildRefusals;
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(0.4f);
            Check(!BuildGrid.Registry.ContainsKey(key) && PlayerNet.WallRebuildRefusals == r0 + 1, "a new wall straight back where one was just broken is refused (with a message)");
            yield return Snap("wall_rebuild_refused");
            yield return new WaitForSeconds(Cfg.WallRebuildCooldown);
            me.PlaceRpc((byte)PieceType.Wall, ci, cj, 0, 1);
            yield return new WaitForSeconds(0.6f);
            Check(BuildGrid.Registry.ContainsKey(key), $"... and fine again {Cfg.WallRebuildCooldown:0} s later");
            if (BuildGrid.Registry.TryGetValue(key, out wall) && wall != null) wall.NetworkObject.Despawn(true);
            var fk = new PieceKey(BuildGrid.KindOf(PieceType.Foundation), ci, cj, 0, 0);
            if (BuildGrid.Registry.TryGetValue(fk, out var f) && f != null) f.NetworkObject.Despawn(true);
            yield return Hold(me, Item.Rock);
        }

        // ------------------------------------------------------------------ hold E: pick up an empty chest / workbench

        IEnumerator PackUpTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            var spawn = Cfg.SpawnPos(team);
            var rot = Quaternion.Euler(0, Cfg.SpawnYaw(team), 0);
            // a chest a couple of metres in front of the spawn
            FreeCell(team, 3, out int ci, out int cj);
            var at = OnGround(BuildGrid.CellCenter(ci, cj), 0f);
            var go = Instantiate(Bootstrap.I.containerPrefab, at, Quaternion.identity);
            go.GetComponent<Container>().ServerInit(Container.Chest, team, Cfg.ChestSlots, null);
            go.GetComponent<NetworkObject>().Spawn(true);
            var chest = go.GetComponent<Container>();
            pc.LocalTeleport(OnGround(at + new Vector3(0f, 0f, -2.2f), 0.1f), 0f);
            yield return new WaitForSeconds(0.5f);
            LookAt(pc, me, chest.Center);
            yield return new WaitForSeconds(0.3f);
            Check(pc.Target.Kind == PlayerController.TargetKind.Container && pc.AimText.Contains("hold"), $"an empty chest of ours says hold E to pick it up (\"{pc.AimText}\")");

            // a tap opens it
            Binds.TestPress(Bind.Interact);
            yield return new WaitForSeconds(0.3f);
            Check(pc.MenuOpen && pc.LootTarget == chest && chest.IsSpawned, "a tap of E still opens the chest");
            pc.CloseMenu();
            yield return new WaitForSeconds(0.3f);
            LookAt(pc, me, chest.Center);
            yield return new WaitForSeconds(0.2f);

            // not empty: holding E doesn't pick it up (it opens)
            chest.Slots[0] = ItemStack.Of(Item.Wood, 5);
            yield return new WaitForSeconds(0.3f);
            int c0 = me.Count(Item.Chest);
            Binds.TestHold(Bind.Interact, true);
            yield return new WaitForSeconds(Cfg.PackUpHoldTime + 0.4f);
            Binds.TestHold(Bind.Interact, false);
            yield return new WaitForSeconds(0.3f);
            Check(chest != null && chest.IsSpawned && me.Count(Item.Chest) == c0, "a chest with things in it can't be picked up");
            pc.CloseMenu();
            chest.Slots[0] = default;
            yield return new WaitForSeconds(0.3f);
            LookAt(pc, me, chest.Center);
            yield return new WaitForSeconds(0.2f);

            // empty: hold E - a bar fills up, then it's in our bag
            Binds.TestHold(Bind.Interact, true);
            yield return new WaitForSeconds(Cfg.PackUpHoldTime * 0.5f);
            float mid = pc.PackUpProgress;
            bool stillThere = chest != null && chest.IsSpawned;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "pack_up_progress.png"));
            yield return new WaitForSeconds(Cfg.PackUpHoldTime + 0.2f);
            Binds.TestHold(Bind.Interact, false);
            yield return new WaitForSeconds(0.4f);
            Check(mid > 0.2f && mid < 1f && stillThere, $"holding E fills a progress bar first ({mid:0.00}) and the chest is still there");
            Check((chest == null || !chest.IsSpawned) && me.Count(Item.Chest) == c0 + 1 && !pc.MenuOpen, $"... then the empty chest is picked up into the bag ({me.Count(Item.Chest) - c0})");

            // a workbench of ours: hold E picks it up too
            var bench = Workbench.ForTeam(team, 1);
            if (bench == null || Workbench.ForTeam(team, 2) != null)
            {
                if (Workbench.ForTeam(team, 2) != null) Workbench.ForTeam(team, 2).NetworkObject.Despawn(true);
                if (bench == null) { Workbench.ServerSpawn(team, Workbench.DefaultPos(team), Workbench.DefaultYaw(team), 1); yield return new WaitForSeconds(0.4f); }
                bench = Workbench.ForTeam(team, 1);
            }
            if (bench != null)
            {
                var d = bench.transform.forward;
                pc.LocalTeleport(OnGround(bench.transform.position + d * 2.2f, 0.1f), 0f);
                yield return new WaitForSeconds(0.4f);
                LookAt(pc, me, bench.Center);
                yield return new WaitForSeconds(0.3f);
                int w0 = me.Count(Item.Workbench);
                Binds.TestHold(Bind.Interact, true);
                yield return new WaitForSeconds(Cfg.PackUpHoldTime + 0.4f);
                Binds.TestHold(Bind.Interact, false);
                yield return new WaitForSeconds(0.4f);
                Check(Workbench.ForTeam(team, 1) == null && me.Count(Item.Workbench) == w0 + 1, $"holding E on our workbench picks it up ({me.Count(Item.Workbench) - w0})");
                // put it back where it was
                yield return Hold(me, Item.Workbench);
                me.PlaceDeployableRpc((byte)Item.Workbench, Workbench.DefaultPos(team), Workbench.DefaultYaw(team));
                yield return new WaitForSeconds(0.5f);
                if (Workbench.ForTeam(team, 1) == null) Workbench.ServerSpawn(team, Workbench.DefaultPos(team), Workbench.DefaultYaw(team), 1);
            }
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Chest || me.SlotAt(i).Id == Item.Workbench) me.Inv[i] = default;
            yield return Hold(me, Item.Rock);
        }

        // ------------------------------------------------------------------ riding: E loots before it gets you off

        IEnumerator RideLootTest(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear spot to ride");
            var fwd = Quaternion.Euler(0, laneYaw, 0) * Vector3.forward;
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            var horse = Vehicle.ServerSpawn(Vehicle.Horse, OnGround(lane + fwd * 2.5f, 0.2f), laneYaw);
            yield return new WaitForSeconds(0.5f);
            me.ServerGive(Item.Saddle, 1, team + 1);
            yield return new WaitForSeconds(0.3f);
            me.MountRpc(horse.NetworkObject);
            yield return new WaitForSeconds(0.8f);
            Check(me.Riding, "on the horse");
            if (!me.Riding) { if (horse != null && horse.IsSpawned) horse.NetworkObject.Despawn(true); yield break; }
            // some berries on the ground beside the horse: look at them and press E
            var side = horse.transform.right;
            var spot = OnGround(horse.transform.position + side * 1.8f + horse.transform.forward * 0.8f, 0.05f);
            int id = g.ServerDropItem(ItemStack.Of(Item.Berry, 3), spot, Vector3.zero, spot + Vector3.up * 0.3f);
            yield return new WaitForSeconds(1f);
            int b0 = me.Count(Item.Berry);
            Vector3 c = spot;
            foreach (var it in g.Items) if (it.Id == id) c = it.Center;
            for (int i = 0; i < 3 && me.Count(Item.Berry) == b0 && me.Riding; i++)
            {
                LookAt(pc, me, c);
                yield return new WaitForSeconds(0.25f);
                if (i == 0) Check(pc.Target.Kind == PlayerController.TargetKind.WorldItem, $"riding, looking at the berries targets them ({pc.Target.Kind}, \"{pc.AimText}\")");
                Binds.TestPress(Bind.Interact);
                yield return new WaitForSeconds(0.5f);
            }
            Check(me.Riding && me.Count(Item.Berry) > b0, $"riding, E picks up what you look at instead of getting you off (riding {me.Riding}, berries {me.Count(Item.Berry) - b0})");
            // looking at nothing, E gets you off
            pc.SetLook(laneYaw, -60f);
            yield return new WaitForSeconds(0.3f);
            Binds.TestPress(Bind.Interact);
            yield return new WaitForSeconds(0.6f);
            Check(!me.Riding, "looking at nothing, E gets you off the horse");
            if (horse != null && horse.IsSpawned) horse.NetworkObject.Despawn(true);
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Berry || me.SlotAt(i).Id == Item.Saddle) me.Inv[i] = default;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        // ------------------------------------------------------------------ host + client: players bleed on every screen

        /// <summary>Host: hurts and then kills the client (the client checks it saw the blood - FeelClientBlood).</summary>
        IEnumerator FeelHostBleedClient(PlayerNet me, PlayerController pc, PlayerNet other)
        {
            int bc = Fx.BloodCount, kc = Fx.BloodKillCount;
            other.ServerDamage(10f, me);
            yield return new WaitForSeconds(1.5f);
            other.ServerKill(me);
            yield return new WaitForSeconds(1.5f);
            Check(Fx.BloodCount > bc && Fx.BloodKillCount > kc, $"(host) the client bled when hurt ({Fx.BloodCount - bc}) and burst when killed ({Fx.BloodKillCount - kc})");
            other.ServerRespawn(false);
            yield return new WaitForSeconds(1f);
        }

        IEnumerator FeelClientBlood(PlayerNet me)
        {
            int bc = Fx.BloodCount, kc = Fx.BloodKillCount;
            float until = Time.time + 30f;
            while (Time.time < until && Fx.BloodKillCount == kc) yield return null;
            Check(Fx.BloodCount > bc && Fx.BloodKillCount > kc, $"(client) everyone sees a hurt player bleed ({Fx.BloodCount - bc}) and a killed one burst ({Fx.BloodKillCount - kc})");
        }
    }
}
