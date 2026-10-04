using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest feel -host -solo -shotdir DIR (windowed): the movement / animation feel batch - crouch out of a slide,
    /// the spear throw's wind-up / buffer / recovery, the spear carried like at a sprint, the bow at a sprint (frame time
    /// and how often the claws are re-worked), the tree camo hopping, horses bobbing, the Wild Unicorn (stats, name,
    /// rainbow at a gallop) and a stack on the ground bouncing when it's added to. Without -solo, plus a second copy with
    /// -autotest feel -client 127.0.0.1: the host hears the client's footsteps (not when it crouch-walks) and sees its
    /// tree hop, and the client sees the unicorn (hopping as it bolts) and the stack bounce.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator FeelRoutine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            if (!NetworkManager.Singleton.IsServer) { yield return FeelClient(me, pc, g); yield break; }
            g.TimerPaused.Value = true;
            yield return SlideCrouchTest(me, pc, team);
            yield return SpearThrowFeelTests(me, pc, team);
            yield return BowSprintProfile(me, pc, team);
            yield return TreeHopTest(me, pc, team);
            yield return HorseLifeTests(me, pc, g, team);
            yield return StackBounceTest(me, pc, g, team);
            if (!Bootstrap.Solo) yield return FeelHostWithClient(me, pc, g, team);
            yield return JumpFeelTests(me, pc, team);
            yield return TreeNoSlideTest(me, pc, team);
            yield return BloodTests(me, pc, g, team);
            yield return DoorSoundTests(me, pc, g, team);
            yield return WallRebuildTest(me, pc, team);
            yield return PackUpTests(me, pc, g, team);
            yield return RideLootTest(me, pc, g, team);
            Log("feel test done");
            g.EndGame(team, "feel test done");
        }

        // ------------------------------------------------------------------ 1: crouch out of a slide

        IEnumerator SlideCrouchTest(PlayerNet me, PlayerController pc, int team)
        {
            var sp = new float[1];
            Check(ClearLane(team, out var lane, out float laneYaw), $"found a clear run ({lane})");
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.7f);
            Binds.TestHold(Bind.Slide, true);
            yield return new WaitForSeconds(0.12f);
            yield return MeasureSpeed(pc, sp);
            float sliding = sp[0];
            Check(pc.Sliding && sliding > Cfg.SprintSpeed * 0.8f, $"sliding ({sliding:0.0} m/s)");
            int n0 = pc.SlideCrouches;
            Binds.TestPress(Bind.Crouch);
            yield return null; yield return null; yield return null;
            bool stopped = !pc.Sliding;
            yield return MeasureSpeed(pc, sp);
            Check(stopped && pc.Crouching && pc.SlideCrouches == n0 + 1 && sp[0] <= Cfg.CrouchSpeed + 0.5f,
                $"pressing crouch mid-slide drops you straight into a crouch (sliding {pc.Sliding}, crouching {pc.Crouching}, {sliding:0.0} -> {sp[0]:0.0} m/s within a couple of frames)");
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(0.4f);
            Check(!pc.Crouching, "letting go stands you back up");
        }

        // ------------------------------------------------------------------ 8, 9: the spear

        IEnumerator SpearThrowFeelTests(PlayerNet me, PlayerController pc, int team)
        {
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Spear) me.Inv[i] = default;
            me.ServerGive(Item.Spear, 4);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.Spear);
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear spot for the spear throws");
            pc.LocalTeleport(lane, laneYaw);
            pc.SetLook(laneYaw, -5f);
            yield return new WaitForSeconds(0.4f);

            // the spear carried like at a sprint: standing, walking and sprinting look the same
            yield return Snap("spear_carry_stand");
            Binds.TestHold(Bind.Forward, true);
            yield return new WaitForSeconds(0.5f);
            yield return Snap("spear_carry_walk");
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("spear_carry_sprint");
            Binds.TestReleaseAll();
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.6f);

            // a click the moment RMB goes down doesn't throw at once: it's remembered, and goes once the minimum wind-up is done
            int c0 = pc.SpearCommits, r0 = pc.SpearReleases;
            Binds.TestHold(Bind.Aim, true);
            yield return null; yield return null;
            float aimAt = Time.time;
            Binds.TestPress(Bind.Attack);
            yield return new WaitForSeconds(0.08f);
            Check(pc.SpearCommits == c0 && pc.SpearReleases == r0, $"clicking straight after RMB doesn't throw at once ({pc.SpearCommits - c0} started)");
            float commitAt = -1f, releaseAt = -1f, until = Time.time + 2f;
            while (Time.time < until && (commitAt < 0f || releaseAt < 0f))
            {
                if (commitAt < 0f && pc.SpearCommits > c0) commitAt = Time.time;
                if (releaseAt < 0f && pc.SpearReleases > r0) releaseAt = Time.time;
                yield return null;
            }
            Check(commitAt > 0f && commitAt - aimAt >= Cfg.SpearMinWindup - 0.03f && commitAt - aimAt < Cfg.SpearMinWindup + 0.2f,
                $"... it's buffered and throws once the minimum wind-up is done ({commitAt - aimAt:0.00}s after RMB, min {Cfg.SpearMinWindup}s)");
            Check(releaseAt > 0f && releaseAt - commitAt >= Cfg.SpearReleaseTime - 0.03f, $"the arm comes through before it leaves the hand ({releaseAt - commitAt:0.00}s)");

            // a click while a poke is still recovering (RMB pressed straight after it) queues the throw: the wind-up only
            // starts once the poke is done, and the throw goes at the end of it
            Binds.TestHold(Bind.Aim, false);
            yield return new WaitForSeconds(Cfg.SpearThrowRecovery + 0.2f);
            yield return Hold(me, Item.Spear);
            yield return new WaitForSeconds(0.5f);
            int c1 = pc.SpearCommits;
            Binds.TestPress(Bind.Attack); // a poke
            float pokeAt = Time.time;
            yield return new WaitForSeconds(0.1f);
            Binds.TestHold(Bind.Aim, true);
            yield return null; yield return null;
            Binds.TestPress(Bind.Attack);
            yield return new WaitForSeconds(0.1f);
            Check(!pc.SpearWindingUp && pc.SpearCommits == c1, "RMB during a poke: no wind-up until the poke has recovered");
            until = Time.time + 3f;
            while (Time.time < until && pc.SpearCommits == c1) yield return null;
            float second = Time.time - pokeAt;
            Check(pc.SpearCommits == c1 + 1 && second >= Cfg.SpearCooldown + Cfg.SpearMinWindup - 0.05f,
                $"... and the click made then is queued: it throws after the poke's recovery and a wind-up ({second:0.00}s after the poke; {Cfg.SpearCooldown} + {Cfg.SpearMinWindup})");
            Binds.TestHold(Bind.Aim, false);

            // once the arm is coming through it can't be called off: letting go of RMB at once still throws
            yield return new WaitForSeconds(Cfg.SpearThrowRecovery + 0.3f);
            yield return Hold(me, Item.Spear);
            yield return new WaitForSeconds(0.4f);
            Binds.TestHold(Bind.Aim, true);
            yield return new WaitForSeconds(Cfg.SpearMinWindup + 0.2f);
            int c2 = pc.SpearCommits, r2 = pc.SpearReleases;
            Binds.TestPress(Bind.Attack);
            until = Time.time + 1f;
            while (Time.time < until && pc.SpearCommits == c2) yield return null;
            Binds.TestHold(Bind.Aim, false);
            yield return new WaitForSeconds(Cfg.SpearReleaseTime + 0.15f);
            Check(pc.SpearCommits == c2 + 1 && pc.SpearReleases == r2 + 1, $"letting go of RMB as the throw starts doesn't cancel it ({pc.SpearReleases - r2} thrown)");

            // letting go of RMB during the wind-up lowers it, and a poke has to wait for that
            yield return new WaitForSeconds(Cfg.SpearThrowRecovery + 0.2f);
            yield return Hold(me, Item.Spear);
            yield return new WaitForSeconds(0.4f);
            if (me.HeldItem == Item.Spear)
            {
                Binds.TestHold(Bind.Aim, true);
                yield return new WaitForSeconds(0.2f);
                int c3 = pc.SpearCommits;
                Binds.TestHold(Bind.Aim, false);
                yield return null;
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(0.3f);
                Check(pc.SpearCommits == c3, "letting go of RMB before the throw: no throw (a click then is a poke, after it's lowered)");
            }
            else Log("note: no spear left for the lowering check");
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Spear) me.Inv[i] = default;
            yield return Hold(me, Item.Rock);
        }

        // ------------------------------------------------------------------ 2: the bow at a sprint

        /// <summary>Sprints with an item for `secs`, returning the median and 95th percentile frame time (ms) and the claw
        /// re-solves / mesh re-bends per frame.</summary>
        IEnumerator SprintSample(PlayerController pc, Vector3 lane, float yaw, bool sprint, float secs, float[] res)
        {
            pc.LocalTeleport(lane, yaw);
            pc.SetLook(yaw, 0f);
            yield return new WaitForSeconds(0.4f);
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, sprint);
            yield return new WaitForSeconds(0.5f);
            var ft = new List<float>();
            int s0 = ViewModel.SolveCount, b0 = ViewModel.BendCount;
            double ms0 = ViewModel.SolveMsTotal;
            float end = Time.time + secs;
            while (Time.time < end) { yield return null; ft.Add(Time.unscaledDeltaTime * 1000f); }
            Binds.TestReleaseAll();
            ft.Sort();
            res[0] = ft.Count > 0 ? ft[ft.Count / 2] : 0f;
            res[1] = ft.Count > 0 ? ft[Mathf.Min(ft.Count - 1, ft.Count * 95 / 100)] : 0f;
            res[2] = (ViewModel.SolveCount - s0) / (float)Mathf.Max(1, ft.Count);
            res[3] = (ViewModel.BendCount - b0) / (float)Mathf.Max(1, ft.Count);
            res[4] = (float)(ViewModel.SolveMsTotal - ms0) / Mathf.Max(1, ft.Count);
        }

        IEnumerator BowSprintProfile(PlayerNet me, PlayerController pc, int team)
        {
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear run for the bow sprint");
            var res = new float[5];
            void Report(string what) => Log($"{what}: frame {res[0]:0.00} ms median / {res[1]:0.00} ms p95, claw solves {res[2]:0.00}/frame ({res[4]:0.00} ms/frame), arm re-bends {res[3]:0.00}/frame");
            yield return Hold(me, Item.Rock);
            yield return SprintSample(pc, lane, laneYaw, true, 2f, res);
            Report("rock, sprinting");
            float rockMs = res[0];
            me.ServerGive(Item.Bow, 1);
            me.ServerGive(Item.Arrow, 10);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.Bow);
            // before the fix (the old pose), for comparison
            ViewModel.DebugLegacyBow = true;
            yield return SprintSample(pc, lane, laneYaw, false, 2f, res);
            Report("bow (old pose), walking");
            yield return SprintSample(pc, lane, laneYaw, true, 2f, res);
            Report("bow (old pose), sprinting");
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("bow_sprint_old");
            Binds.TestReleaseAll();
            ViewModel.DebugLegacyBow = false;
            yield return SprintSample(pc, lane, laneYaw, false, 2f, res);
            Report("bow, walking");
            yield return SprintSample(pc, lane, laneYaw, true, 2f, res);
            Report("bow, sprinting");
            Check(res[2] < 0.25f && res[4] < 1f, $"sprinting with the bow doesn't re-work the claws every frame ({res[2]:0.00} solves, {res[4]:0.00} ms a frame)");
            Check(res[0] < rockMs * 1.3f + 1f, $"sprinting with the bow costs about the same as with the rock ({res[0]:0.00} vs {rockMs:0.00} ms a frame)");
            // and how it looks
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("bow_sprint");
            Binds.TestReleaseAll();
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Bow || me.SlotAt(i).Id == Item.Arrow) me.Inv[i] = default;
            yield return Hold(me, Item.Rock);
        }

        // ------------------------------------------------------------------ 4: the tree camo hops

        IEnumerator TreeHopTest(PlayerNet me, PlayerController pc, int team)
        {
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear run for the tree hop");
            me.ServerGive(Item.TreeCamo, 1);
            yield return new WaitForSeconds(0.3f);
            yield return Hold(me, Item.TreeCamo);
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.6f);
            Check(me.TreeCamo && Mathf.Abs(me.TreeHopHeight) < 0.005f, $"a tree standing still doesn't hop ({me.TreeHopHeight:0.000} m)");
            Binds.TestHold(Bind.Forward, true);
            float hi = 0f, shotAt = -1f;
            float end = Time.time + 2.5f;
            int hops0 = -1;
            Vector3 from = me.transform.position;
            while (Time.time < end)
            {
                // (counted once it's up to speed)
                if (hops0 < 0 && Time.time > end - 2f) { hops0 = me.TreeHops; from = me.transform.position; }
                hi = Mathf.Max(hi, me.TreeHopHeight);
                if (shotAt < 0f && me.TreeHopHeight > 0.2f && Time.time > end - 1f) { shotAt = Time.time; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "tree_hop.png")); }
                yield return null;
            }
            Check(hi > 0.15f, $"walking about as a tree, it hops ({hi:0.00} m up)");
            {
                var walked = me.transform.position - from; walked.y = 0f;
                float perHop = walked.magnitude / Mathf.Max(1, me.TreeHops - hops0);
                Check(Mathf.Abs(perHop - PlayerNet.TreeHopStride) < 0.8f && PlayerNet.TreeHopStride >= 2.5f,
                    $"... at half the old rate: a hop every {perHop:0.0} m ({me.TreeHops - hops0} hops in {walked.magnitude:0.0} m; it was 1.3 m)");
            }
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(1.4f);
            Check(Mathf.Abs(me.TreeHopHeight) < 0.01f, $"... and settles when you stop ({me.TreeHopHeight:0.000} m)");
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.TreeCamo) me.Inv[i] = default;
            yield return Hold(me, Item.Rock);
        }

        // ------------------------------------------------------------------ 5, 6: horses bob; the Wild Unicorn

        IEnumerator HorseLifeTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(ClearLane(team, out var lane, out float laneYaw), "a clear run for the horses");
            var fwd = Quaternion.Euler(0, laneYaw, 0) * Vector3.forward;
            var side = Vector3.Cross(Vector3.up, fwd);
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            var horse = Vehicle.ServerSpawn(Vehicle.Horse, OnGround(lane + fwd * 6f + side * 2.5f, 0.2f), laneYaw);
            var uni = Vehicle.ServerSpawn(Vehicle.Horse, OnGround(lane + fwd * 6f - side * 2.5f, 0.2f), laneYaw, true);
            yield return new WaitForSeconds(0.5f);
            Check(!horse.IsUnicorn && horse.DisplayName == "Wild Horse" && uni.IsUnicorn && uni.DisplayName == "Wild Unicorn",
                $"a unicorn is called a Wild Unicorn ({uni.DisplayName}), a horse a Wild Horse ({horse.DisplayName})");
            Check(Mathf.Approximately(uni.MaxHp, Cfg.HorseHp * Cfg.UnicornHpMul) && Mathf.Approximately(uni.Hp.Value, uni.MaxHp) && uni.MaxHp > horse.MaxHp && uni.SpeedMul > 1f,
                $"a unicorn has a bit more health ({uni.Hp.Value:0}/{uni.MaxHp:0} vs {horse.MaxHp:0}) and runs a bit faster (x{uni.SpeedMul:0.00})");
            Check(Cfg.UnicornChance > 0f && Cfg.UnicornChance < 0.2f, $"unicorns are rare ({Cfg.UnicornChance * 100f:0}% of wild horses)");
            Check(uni.transform.Find("visual/neck/horn") != null && horse.transform.Find("visual/neck/horn") == null, "the unicorn has a horn, the horse doesn't");
            // look at it: its name shows
            var look = Quaternion.LookRotation(uni.transform.position + Vector3.up * 1.2f - me.EyePos).eulerAngles;
            pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
            float until = Time.time + 1.5f;
            while (Time.time < until && !pc.AimText.Contains("Wild Unicorn")) { pc.LocalTeleport(OnGround(uni.transform.position - fwd * 3f, 0.1f), laneYaw); look = Quaternion.LookRotation(uni.transform.position + Vector3.up * 1.2f - me.EyePos).eulerAngles; pc.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x); yield return new WaitForSeconds(0.2f); }
            Check(pc.AimText.Contains("Wild Unicorn"), $"looking at it says Wild Unicorn (\"{pc.AimText}\")");
            yield return Snap("unicorn_standing");

            // both bolt (hurt from behind): they bob as they run, the unicorn's quicker and leaves a rainbow
            int rb0 = Vehicle.RainbowPieces;
            int hops0 = horse.Hops, uhops0 = uni.Hops;
            horse.ServerDamage(1f, null);
            uni.ServerDamage(1f, null);
            float hBob = 0f, uBob = 0f, hSpeed = 0f, uSpeed = 0f;
            var hp0 = horse.transform.position; var up0 = uni.transform.position;
            float t0 = Time.time;
            yield return new WaitForSeconds(0.6f);
            hp0 = horse.transform.position; up0 = uni.transform.position; t0 = Time.time;
            float end = Time.time + 1.5f;
            bool shot = false;
            while (Time.time < end)
            {
                hBob = Mathf.Max(hBob, horse.BobHeight);
                uBob = Mathf.Max(uBob, uni.BobHeight);
                if (Time.time > end - 0.8f)
                {
                    // the camera off to the side of the unicorn, looking at it and the rainbow behind it
                    var us = uni.transform.position;
                    var cam = OnGround(us + uni.transform.right * 7f - uni.transform.forward * 2f, 0.1f);
                    var l2 = Quaternion.LookRotation(us - uni.transform.forward * 1.5f + Vector3.up * 0.6f - (cam + Vector3.up * Cfg.EyeHeight)).eulerAngles;
                    pc.LocalTeleport(cam, l2.y);
                    pc.SetLook(l2.y, l2.x > 180f ? l2.x - 360f : l2.x);
                    if (!shot && Time.time > end - 0.25f) { shot = true; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "unicorn_rainbow.png")); }
                }
                yield return null;
            }
            float dt = Time.time - t0;
            Vector3 hd = horse.transform.position - hp0, ud = uni.transform.position - up0; hd.y = 0; ud.y = 0;
            hSpeed = hd.magnitude / dt; uSpeed = ud.magnitude / dt;
            yield return new WaitForSeconds(0.2f);
            Check(hBob > 0.05f && uBob > 0.05f, $"running horses bob up and down (horse {hBob:0.00} m, unicorn {uBob:0.00} m)");
            Check(hBob > 0.25f && uBob > 0.25f && horse.Hops - hops0 >= 2 && uni.Hops - uhops0 >= 2,
                $"wild horses hop along like the tree disguise (up to {hBob:0.00} / {uBob:0.00} m; {horse.Hops - hops0} / {uni.Hops - uhops0} hops)");
            Check(uSpeed > hSpeed * 1.05f, $"the unicorn gallops faster ({uSpeed:0.0} vs {hSpeed:0.0} m/s)");
            Check(Vehicle.RainbowPieces - rb0 > 30, $"a galloping unicorn leaves a rainbow ({Vehicle.RainbowPieces - rb0} pieces)");
            int rb1 = Vehicle.RainbowPieces;
            // a horse standing still doesn't bob, and a walking unicorn makes no rainbow
            yield return new WaitForSeconds(8f); // (they stop fleeing)
            float still = 0f;
            float stillFor = 0f;
            for (float tt = 0f; tt < 4f; tt += Time.deltaTime)
            {
                // (once it's been standing a moment: a hop it was in the middle of finishes first)
                if (horse != null && horse.IsSpawned && horse.AnimSpeed < 0.2f) stillFor += Time.deltaTime; else stillFor = 0f;
                if (stillFor > 1.5f) still = Mathf.Max(still, horse.BobHeight);
                yield return null;
            }
            Check(still < 0.02f, $"a horse standing still doesn't bob ({still:0.000} m)");
            if (horse != null && horse.IsSpawned) horse.NetworkObject.Despawn(true);
            if (uni != null && uni.IsSpawned) uni.NetworkObject.Despawn(true);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        // ------------------------------------------------------------------ 10: a stack on the ground bounces when added to

        int m_FeelStack = -1;

        IEnumerator StackBounceTest(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            var rotS = Quaternion.Euler(0, Cfg.SpawnYaw(team), 0);
            var at = Cfg.SpawnPos(team) + rotS * new Vector3(-1.3f, 0f, 3.5f); // (ahead and to the left, clear of the rock in hand)
            m_FeelStack = g.ServerDropItem(ItemStack.Of(Item.Wood, 10), at, Vector3.forward, at);
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            {
                var lk = Quaternion.LookRotation(OnGround(at, 0.1f) - me.EyePos).eulerAngles;
                pc.SetLook(lk.y - 12f, (lk.x > 180f ? lk.x - 360f : lk.x) - 8f); // (just right of the rock in hand)
            }
            yield return new WaitForSeconds(1f);
            int b0 = NetGame.StackBouncesOf(m_FeelStack);
            AddToStack(g, m_FeelStack, 5);
            yield return new WaitForSeconds(0.1f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "stack_bounce.png"));
            yield return new WaitForSeconds(0.5f);
            Check(NetGame.StackBouncesOf(m_FeelStack) == b0 + 1, $"adding to a stack on the ground bounces it ({NetGame.StackBouncesOf(m_FeelStack) - b0})");
            // taking some away doesn't
            int b1 = NetGame.StackBouncesOf(m_FeelStack);
            g.ServerTakeItem(m_FeelStack, at, 50f, 3);
            yield return new WaitForSeconds(0.3f);
            Check(NetGame.StackBouncesOf(m_FeelStack) == b1, "taking from it doesn't bounce it");
        }

        static void AddToStack(NetGame g, int id, int add)
        {
            for (int i = 0; i < g.Items.Count; i++)
            {
                if (g.Items[i].Id != id) continue;
                var it = g.Items[i];
                it.Stack = it.Stack.WithCount(it.Stack.Count + add);
                it.From = it.Pos;
                g.Items[i] = it;
                return;
            }
        }

        // ------------------------------------------------------------------ host + client: the networked side

        IEnumerator FeelHostWithClient(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            PlayerNet other = null;
            float until = Time.time + 40f;
            while (other == null && Time.time < until) { foreach (var p in PlayerNet.All) if (p != me) other = p; yield return null; }
            if (other == null) { Check(false, "(host) no client joined"); yield break; }
            yield return new WaitForSeconds(2f);
            // the signal for the client: some berries = walk in front of us, then crouch-walk, then walk as a tree
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            other.ServerGive(Item.TreeCamo, 1);
            other.ServerGive(Item.Berry, 1);
            float walkSteps = 0, crouchSteps = 0, hop = 0f;
            float last = other.LastStepAt;
            bool sawWalk = false, sawCrouch = false, sawTree = false;
            until = Time.time + 15f; // (the client walks, crouch-walks and walks as a tree in about 11 s)
            while (Time.time < until)
            {
                if (other.LastStepAt != last)
                {
                    last = other.LastStepAt;
                    if (other.Crouch.Value) crouchSteps++; else if (!other.TreeCamo) walkSteps++;
                }
                if (other.TreeCamo) { sawTree = true; hop = Mathf.Max(hop, other.TreeHopHeight); }
                else if (other.Crouch.Value) sawCrouch = true;
                else sawWalk = true;
                yield return null;
            }
            Check(sawWalk && walkSteps >= 3, $"(host) the client's footsteps are heard as it walks ({walkSteps} steps)");
            Check(sawCrouch && crouchSteps == 0, $"(host) ... and not as it crouch-walks ({crouchSteps})");
            Check(sawTree && hop > 0.12f, $"(host) the client's camo tree hops as it moves ({hop:0.00} m)");
            // now the unicorn and the stack bounce, for the client to see
            var op = other.transform.position;
            var fwd = other.transform.forward;
            var hostUni = Vehicle.ServerSpawn(Vehicle.Horse, OnGround(op + fwd * 5f, 0.2f), Quaternion.LookRotation(fwd).eulerAngles.y, true);
            var at = op + fwd * 2.5f;
            int id = g.ServerDropItem(ItemStack.Of(Item.Wood, 10), at, Vector3.forward, at);
            yield return new WaitForSeconds(1.5f);
            if (hostUni != null && hostUni.IsSpawned) hostUni.ServerDamage(1f, null); // (it bolts away from the stack: the client sees it hop)
            yield return new WaitForSeconds(1.5f);
            AddToStack(g, id, 7);
            yield return new WaitForSeconds(1.5f);
            AddToStack(g, id, 7);
            yield return new WaitForSeconds(6f);
            yield return FeelHostBleedClient(me, pc, other);
        }

        IEnumerator FeelClient(PlayerNet me, PlayerController pc, NetGame g)
        {
            float until = Time.time + 120f;
            while (Time.time < until && me.Count(Item.Berry) == 0) yield return null;
            PlayerNet host = null;
            foreach (var p in PlayerNet.All) if (p != me) host = p;
            if (host == null) { Check(false, "(client) no host player"); yield break; }
            yield return Hold(me, Item.Rock); // (the tree camo the host gave us may have landed in the slot in hand)
            // in front of the host, walking across its view
            var hp = host.transform.position;
            var hf = host.transform.forward;
            var start = OnGround(hp + hf * 8f - Vector3.Cross(Vector3.up, hf) * 5f, 0.1f);
            float yaw = Quaternion.LookRotation(Vector3.Cross(Vector3.up, hf)).eulerAngles.y;
            for (int phase = 0; phase < 3; phase++)
            {
                if (phase == 2) { yield return Hold(me, Item.TreeCamo); yield return new WaitForSeconds(0.5f); }
                pc.LocalTeleport(start, yaw);
                yield return new WaitForSeconds(0.4f);
                if (phase == 1) Binds.TestHold(Bind.Crouch, true);
                yield return new WaitForSeconds(0.3f);
                Binds.TestHold(Bind.Forward, true);
                yield return new WaitForSeconds(2f);
                Binds.TestReleaseAll();
                yield return new WaitForSeconds(0.5f);
            }
            yield return Hold(me, Item.Rock);
            int b0 = NetGame.StackBouncesOf(m_FeelStack);
            // the host puts out a unicorn and a stack that it adds to
            Vehicle uni = null;
            until = Time.time + 10f;
            while (Time.time < until && uni == null) { foreach (var v in Vehicle.All) if (v != null && v.IsUnicorn) uni = v; yield return null; }
            Check(uni != null && uni.DisplayName == "Wild Unicorn" && uni.transform.Find("visual/neck/horn") != null && Mathf.Approximately(uni.MaxHp, Cfg.HorseHp * Cfg.UnicornHpMul),
                $"(client) the host's unicorn is a unicorn here too ({(uni != null ? uni.DisplayName + ", " + uni.MaxHp + " HP" : "none")})");
            until = Time.time + 10f;
            int uh0 = uni != null ? uni.Hops : 0;
            float uniHop = 0f;
            while (Time.time < until && (NetGame.StackBounces < b0 + 2 || (uni != null && uni.Hops - uh0 < 2 && Time.time < until - 5f)))
            {
                if (uni != null && uni.IsSpawned) uniHop = Mathf.Max(uniHop, uni.BobHeight);
                yield return null;
            }
            Check(uni != null && uni.Hops - uh0 >= 2 && uniHop > 0.2f, $"(client) the host's wild unicorn hops as it bolts here too ({(uni != null ? uni.Hops - uh0 : 0)} hops, up to {uniHop:0.00} m)");
            Check(NetGame.StackBounces >= b0 + 2, $"(client) the stack the host adds to bounces here too ({NetGame.StackBounces - b0})");
            yield return FeelClientBlood(me);
        }
    }
}
