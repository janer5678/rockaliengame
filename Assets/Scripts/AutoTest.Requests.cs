using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest modes (classic, arsenal, autowood, dna): the ball under the glass dome, the ball buff, airdrop rarity, the airdrop
    /// warning and the ship (hatch, slow crate, shrinking away), harder thrown spears, breaking your own pieces, the one-shot portal
    /// gun, airdrop rocket launchers with 3 rockets, Ctrl crouch / C slide, solid border rocks, and the workbench checks in
    /// AutoTest.Bench.cs (locked until the ball's captured, crafting it everywhere, C4 on benches and chests).
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator RequestTests(PlayerNet me, PlayerController pc, NetGame g, int team)
        {
            Check(Cfg.SpearThrowDamage >= 90f && Cfg.SpearThrowDamage > Cfg.SpearPlayerDamage * 2f, $"a thrown spear hits hard ({Cfg.SpearThrowDamage} at a full wind-up, stab {Cfg.SpearPlayerDamage})");
            Check(Binds.Get(Bind.Crouch) == KeyCode.LeftControl && Binds.Get(Bind.Crouch, true) != KeyCode.C && Binds.Get(Bind.Slide) == KeyCode.C,
                $"Ctrl only crouches, C slides (crouch {Binds.Get(Bind.Crouch)}/{Binds.Get(Bind.Crouch, true)}, slide {Binds.Get(Bind.Slide)})");

            // the big rocks round the border are solid, and some of them reach into the map
            {
                int solid = 0, reachIn = 0;
                float half = Cfg.MapHalf;
                foreach (var mc in FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
                {
                    if (!mc.convex || mc.sharedMesh == null || mc.sharedMesh.name != "Rock" || mc.transform.lossyScale.x < 8f) continue;
                    solid++;
                    var b = mc.bounds;
                    if (Mathf.Max(Mathf.Abs(b.center.x) - b.extents.x, Mathf.Abs(b.center.z) - b.extents.z) < half) reachIn++;
                }
                Check(solid >= 40, $"the border rocks have colliders ({solid}, {reachIn} reach in past the boundary wall)");
            }

            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            bool wasPaused = g.TimerPaused.Value;
            if (g.S == GameState.PreBall) g.TimerPaused.Value = true; // (these need the wall up for a while)

            // the workbench: locked until the ball's captured, then craftable everywhere; C4 on benches and chests (AutoTest.Bench.cs)
            yield return BenchTests(me, pc, g, team);

            // the ball is in the middle from the start, under a glass dome the walls meet: nobody can get at it until they drop
            Check(g.S == GameState.PreBall, $"the wall is still up for the glass dome checks ({g.S})");
            if (g.S == GameState.PreBall)
            {
                var ball = Ball.Instance;
                var bp = ball != null ? ball.transform.position : Vector3.zero;
                Check(ball != null && MapBuilder.GlassUp && new Vector2(bp.x, bp.z).magnitude < 0.5f && !ball.IsCarried && ball.SocketTeam.Value < 0,
                    $"the ball is already in the middle at the start, under the glass dome ({bp})");
                // the dome is solid: run straight at the ball and you stop at the glass
                var from = Cfg.BackDir(team) * (MapBuilder.DomeRadius + 4f);
                from.y = MapBuilder.Height(from.x, from.z) + 0.1f;
                float yaw = Quaternion.LookRotation(-Cfg.BackDir(team)).eulerAngles.y;
                pc.LocalTeleport(from, yaw);
                pc.SetLook(yaw, 5f);
                yield return Snap("glass_dome_" + rn);
                Binds.TestHold(Bind.Forward, true);
                Binds.TestHold(Bind.Sprint, true);
                yield return new WaitForSeconds(3f);
                Binds.TestReleaseAll();
                var mp = me.transform.position;
                float reached = new Vector2(mp.x, mp.z).magnitude;
                Check(reached > MapBuilder.DomeRadius - 0.6f && reached < MapBuilder.DomeRadius + 2.5f, $"running at the ball: the dome stops you at the glass ({reached:0.0} m from the middle)");
                me.PickupBallRpc();
                yield return new WaitForSeconds(0.4f);
                Check(!me.CarryingBall && !ball.IsCarried, "can't take the ball while the dome is up");
                yield return Snap("glass_dome_close_" + rn);
                // the glass walls end at the dome - none crosses the middle: a ray along the wall line hits the dome, not a wall
                bool domeHit = Physics.Raycast(new Vector3(-(MapBuilder.DomeRadius + 6f), MapBuilder.Height(0, 0) + 1f, 0) + Vector3.forward * 0.5f, Vector3.right, out var hit, 12f)
                    && hit.collider.name == "glass dome";
                bool wallAbove = Physics.Raycast(new Vector3(0, MapBuilder.Height(0, 0) + MapBuilder.DomeRadius + 3f, -5f), Vector3.forward, out var hit2, 10f) && hit2.collider.name == "glass";
                Check(domeHit && (Cfg.FourWay || wallAbove), $"the glass walls meet the dome (the wall goes over its top) ({(domeHit ? hit.collider.name : "nothing")}, {(wallAbove ? hit2.collider.name : "nothing")})");

                // the ball buff: only while the ball is in your machine's socket, +15%
                Check(Mathf.Approximately(Cfg.BallGatherMul, 1.15f), $"the ball buff is +15% ({Cfg.BallGatherMul})");
                ball.ServerDrop(Cfg.BaseCenter[team] - Cfg.BackDir(team) * 5f + Vector3.Cross(Vector3.up, Cfg.BackDir(team)) * 3f + Vector3.up * 2f, Vector3.zero); // (lying in our base, away from the socket)
                yield return new WaitForSeconds(1.2f);
                bool lying = me.BallBuff;
                ball.ServerSocket(team);
                yield return new WaitForSeconds(0.3f);
                bool socketed = me.BallBuff;
                Check(!lying && socketed, $"ball buff only with the ball in your socket (lying in base: {lying}, socketed: {socketed})");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return Snap("ball_buff_" + rn);
                ball.ServerPlaceInDome();
                yield return new WaitForSeconds(0.3f);
                Check(new Vector2(ball.transform.position.x, ball.transform.position.z).magnitude < 0.5f && ball.SocketTeam.Value < 0, "ball back under the dome");
            }

            // airdrops: per-item rarity (CHANGE VALUES > Airdrop rarity)
            {
                int c4 = Cfg.RarityC4, wand = Cfg.RarityDeathWand;
                var pool = new System.Collections.Generic.List<Item> { Item.C4, Item.DeathWand };
                Cfg.RarityC4 = 0; Cfg.RarityDeathWand = 5;
                bool onlyWand = true;
                for (int i = 0; i < 50; i++) onlyWand &= Cfg.PickAirdropItem(pool) == Item.DeathWand;
                Cfg.RarityC4 = 90; Cfg.RarityDeathWand = 10;
                int nC4 = 0;
                for (int i = 0; i < 2000; i++) if (Cfg.PickAirdropItem(pool) == Item.C4) nC4++;
                Cfg.RarityC4 = 0; Cfg.RarityDeathWand = 0;
                int nEach = 0;
                for (int i = 0; i < 400; i++) if (Cfg.PickAirdropItem(pool) == Item.C4) nEach++;
                Cfg.RarityC4 = c4; Cfg.RarityDeathWand = wand;
                bool listed = false;
                foreach (var f in Cfg.TuneFields) if (f.Name == "RarityPortalGun" && Cfg.SectionOf(f) == "Airdrop rarity") listed = true;
                Check(onlyWand && nC4 > 1700 && nC4 < 1900 && nEach > 140 && nEach < 260 && listed,
                    $"airdrop rarity: weight 0 never drops ({onlyWand}), 90:10 gives {nC4 / 20f:0}% C4, all 0 = even ({nEach / 4f:0}%), in CHANGE VALUES ({listed})");
            }

            // the warning: one notice 15 s before an airdrop lands, saying where (no timer, no other countdown)
            {
                Check(NetGame.DropWarning == 15f && NetGame.DropBeam == 8f, $"airdrop warning {NetGame.DropWarning}s ahead; the crate takes {NetGame.DropBeam}s to come down (half speed)");
                bool centre = Cfg.AirdropCenter;
                Cfg.AirdropCenter = true;
                g.ServerAirdropWarning(g.NetworkManager.ServerTime.Time + NetGame.DropWarning);
                yield return new WaitForSeconds(0.5f);
                Check(Hud.LastBanner.Contains("AIRDROP IN 15 SECONDS") && Hud.LastBanner.Contains("centre"), $"the warning says it's dropping in the centre ({Hud.LastBanner})");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return Snap("airdrop_warning_" + rn);
                Cfg.AirdropCenter = centre;
                g.NextDropLands.Value = -1;
            }

            // the ship: a round hatch opens under it before the beam comes out; the crate comes down slowly; it shrinks away into the distance
            if (Cfg.Rules == GameRules.Arsenal || Cfg.Rules == GameRules.Classic)
            {
                foreach (var c in new System.Collections.Generic.List<Container>(Container.All)) if (c != null && c.IsAirdrop && c.IsSpawned) c.NetworkObject.Despawn(true);
                g.DevSpawnAirdrop();
                double st = g.LaneStartAt(0);
                var gp = g.LanePosAt(0);
                var toMid = new Vector3(-gp.x, 0, -gp.z);
                if (toMid.sqrMagnitude < 1f) toMid = Vector3.forward;
                var eye = gp + toMid.normalized * 34f;
                eye.y = MapBuilder.Height(eye.x, eye.z) + 0.1f;
                float yaw = Quaternion.LookRotation(-toMid).eulerAngles.y;
                pc.LocalTeleport(eye, yaw);
                pc.SetLook(yaw, -50f);
                double E() => g.NetworkManager.ServerTime.Time - st;
                while (E() < NetGame.DropArrive - 1.6) yield return null;
                float shut = AirdropShip.HatchOpen;
                while (E() < NetGame.DropArrive - 0.27) yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ShotDir(), "airdrop_hatch_open.png")); // (open, the beam not out yet)
                Log($"shot airdrop_hatch_open (hatch {AirdropShip.HatchOpen:0.00})");
                var shipT = AirdropShip.ShipTransform;
                if (shipT != null)
                    foreach (var r in shipT.GetComponentsInChildren<MeshRenderer>())
                        if (r.name == "hatch hole") Log($"hatch hole: enabled {r.enabled}, active {r.gameObject.activeInHierarchy}, bounds {r.bounds.center - shipT.position} {r.bounds.size}, shader {r.sharedMaterial.shader.name} supported {r.sharedMaterial.shader.isSupported}, sphere mesh {Art.Sphere.bounds.extents}, cylinder mesh {Art.Cylinder.bounds.extents}");
                while (E() < NetGame.DropArrive + 1.2) yield return null;
                float open = AirdropShip.HatchOpen;
                yield return Snap("airdrop_hatch_beam");
                Check(shut < 0.05f && open > 0.95f, $"the hatch is shut on the way in ({shut:0.00}) and open when the beam comes out ({open:0.00})");
                int Crates() { int n = 0; foreach (var c in Container.All) if (c != null && c.IsAirdrop) n++; return n; }
                while (E() < NetGame.DropLand - 1.0) yield return null;
                int before = Crates();
                while (E() < NetGame.DropLand + 1.0) yield return null;
                int after = Crates();
                Check(before == 0 && after >= 1, $"the crate lands {NetGame.DropLand}s after the ship arrives ({before} -> {after} crates)");
                pc.SetLook(yaw, -40f);
                while (E() < AirdropShip.LeaveStart + 3.2) yield return null; // (it leaves once the hole it cut in the dome is patched)
                float mid = AirdropShip.ShipScale;
                var ship = AirdropShip.ShipTransform;
                float midScale = ship != null ? ship.localScale.x : -1f;
                yield return Snap("airdrop_ship_leaving");
                while (E() < AirdropShip.LeaveStart + 5.5) yield return null;
                float late = AirdropShip.ShipScale;
                yield return Snap("airdrop_ship_leaving_far");
                while (E() < AirdropShip.Gone + 0.5) yield return null;
                Check(mid < 0.8f && mid > 0.1f && Mathf.Abs(midScale - mid) < 0.05f && late < 0.05f && AirdropShip.ShipTransform == null,
                    $"leaving, the ship shrinks away ({mid:0.00} -> {late:0.000}) and is gone");
                foreach (var c in new System.Collections.Generic.List<Container>(Container.All)) if (c != null && c.IsAirdrop && c.IsSpawned) c.NetworkObject.Despawn(true);
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            }
            g.TimerPaused.Value = wasPaused;

            // airdrop rocket launchers come with 3 rockets, and fire them one by one
            {
                int mask = Cfg.AirdropItemMask;
                Cfg.AirdropItemMask = 1 << System.Array.IndexOf(Cfg.AirdropChoices, Item.RocketLauncher);
                var rolled = NetGame.RollAirdropLoot();
                Cfg.AirdropItemMask = mask;
                Check(rolled.Id == Item.RocketLauncher && rolled.Data == 3, $"an airdrop rocket launcher holds 3 rockets ({rolled.Id}, {rolled.Data})");
                me.ServerGive(Item.RocketLauncher, 1, rolled.Data);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.RocketLauncher);
                var up = (Vector3.up + me.transform.forward * 0.2f).normalized * Cfg.RocketSpeed; // (into the sky: it never reports a landing)
                for (int i = 0; i < 3; i++)
                {
                    me.ThrowItemRpc(Item.RocketLauncher, me.EyePos, up);
                    yield return new WaitForSeconds(0.65f);
                    int left = me.Count(Item.RocketLauncher) > 0 ? me.SlotAt(me.HotbarSlotOf(Item.RocketLauncher)).Data : 0;
                    if (i < 2) Check(me.Count(Item.RocketLauncher) == 1 && left == 2 - i, $"rocket {i + 1} fired, {left} left in the launcher");
                    else Check(me.Count(Item.RocketLauncher) == 0, "the third rocket used it up");
                }
            }

            // the portal gun: ONE shot, and that shot makes the whole linked pair - a portal where you stand and one where it
            // lands - then it's used up. The one under you doesn't take you until you step off it; walking back in does.
            // (and the oldest pairs go once there are lots)
            yield return PortalGunTests(me, pc, g, team);

            // your own pieces break like the enemy's: rock, thrown spear and ram on our own walls
            {
                var bc = Cfg.BaseCenter[team];
                float zs = Mathf.Sign(-bc.z);
                Structure Own(PieceType t, Vector3 at)
                {
                    at.y = MapBuilder.Height(at.x, at.z);
                    var go = Instantiate(Bootstrap.I.structurePrefab, at, Quaternion.identity);
                    var s = go.GetComponent<Structure>();
                    s.ServerInit(t, team, default, false);
                    go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                    return s;
                }
                var wall = Own(PieceType.Wall, bc + new Vector3(9f, 0, 9f * zs));
                var hew = Own(PieceType.Barrier, bc + new Vector3(-9f, 0, 9f * zs));
                yield return new WaitForSeconds(0.3f);
                // rock
                pc.LocalTeleport(wall.transform.position + new Vector3(0, 0.1f, -1.6f), 0f);
                yield return Hold(me, Item.Rock);
                float hp0 = wall.Health.Value;
                me.MeleeRpc(true, wall.NetworkObject, wall.transform.position + new Vector3(0, 1.4f, -0.2f), false);
                yield return new WaitForSeconds(0.3f);
                Check(wall.IsSpawned && wall.Health.Value < hp0, $"hitting our own wall with the rock damages it ({hp0:0} -> {wall.Health.Value:0})");
                // thrown spear
                me.ServerGive(Item.Spear, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.Spear);
                float hp1 = wall.Health.Value;
                me.ThrowSpearRpc(me.EyePos, me.transform.forward * Cfg.SpearThrowSpeed);
                yield return new WaitForSeconds(0.2f);
                me.SpearLandRpc(true, wall.NetworkObject, wall.transform.position + new Vector3(0, 1.4f, -0.2f), Vector3.forward);
                yield return new WaitForSeconds(0.3f);
                Check(wall.IsSpawned && wall.Health.Value < hp1, $"a thrown spear damages our own wall ({hp1:0} -> {wall.Health.Value:0})");
                // ram on our own high external wall
                me.ServerGive(Item.Ram, 1, Cfg.MaxData(Item.Ram));
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.Ram);
                pc.LocalTeleport(hew.transform.position + new Vector3(0, 0.1f, -2f), 0f);
                yield return new WaitForSeconds(Cfg.RamWindup + 0.3f);
                me.RamStrikeRpc(hew.NetworkObject, hew.transform.position + new Vector3(0, 1.5f, -0.2f));
                yield return new WaitForSeconds(0.5f);
                Check(hew == null || !hew.IsSpawned, "the ram smashes our own high external wall");
                if (wall != null && wall.IsSpawned) wall.NetworkObject.Despawn(true);
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.Spear || me.SlotAt(i).Id == Item.Ram) me.Inv[i] = default;
            }
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
            yield return SlideAndTreeTests(me, pc, team);
        }

        /// <summary>A flat 30 m run with nothing in the way (outside the bases), for the movement checks.</summary>
        static bool ClearLane(int team, out Vector3 pos, out float yaw)
        {
            int mask = ~(1 << PlayerNet.HitboxLayer);
            for (int r = 0; r < 6; r++)
                for (int a = 0; a < 12; a++)
                {
                    var c = Cfg.BaseCenter[team];
                    var side = Quaternion.Euler(0, a * 30f, 0) * Vector3.forward;
                    pos = c + side * (Cfg.BaseHalf + 6f + r * 6f);
                    pos.y = MapBuilder.Height(pos.x, pos.z) + 0.1f;
                    if (Mathf.Abs(pos.x) > Cfg.MapHalf - 10f || Mathf.Abs(pos.z) > Cfg.MapHalf - 10f) continue;
                    for (int b = 0; b < 8; b++)
                    {
                        yaw = b * 45f;
                        var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                        var end = pos + dir * 30f;
                        if (Cfg.BaseTeamAt(end) >= 0 || Cfg.BaseTeamAt(pos) >= 0) continue;
                        if (Mathf.Abs(MapBuilder.Height(end.x, end.z) + 0.1f - pos.y) > 0.3f) continue;
                        if (Physics.CheckSphere(pos + Vector3.up * 1f, 0.6f, mask, QueryTriggerInteraction.Ignore)) continue;
                        if (Physics.SphereCast(pos + Vector3.up * 1f, 0.6f, dir, out _, 30f, mask, QueryTriggerInteraction.Ignore)) continue;
                        if (Physics.SphereCast(pos + Vector3.up * 2.4f, 0.6f, dir, out _, 30f, mask, QueryTriggerInteraction.Ignore)) continue;
                        return true;
                    }
                }
            pos = Cfg.SpawnPos(team);
            yaw = Cfg.SpawnYaw(team);
            return false;
        }

        static IEnumerator MeasureSpeed(PlayerController pc, float[] outSpeed)
        {
            var a = pc.transform.position;
            float t0 = Time.time;
            yield return new WaitForSeconds(0.1f);
            var d = pc.transform.position - a;
            d.y = 0;
            outSpeed[0] = d.magnitude / Mathf.Max(0.01f, Time.time - t0);
        }

        /// <summary>
        /// C in the air keeps your speed and lands you in a slide; pushing the other way during a slide drops you into a
        /// crouch; the tree camo roots you with LMB / RMB held (the tree never turns); trees show no aim text.
        /// </summary>
        IEnumerator SlideAndTreeTests(PlayerNet me, PlayerController pc, int team)
        {
            var sp = new float[1];
            Check(ClearLane(team, out var lane, out float laneYaw), $"found a clear run for the movement checks ({lane}, yaw {laneYaw})");

            // ---- C pressed mid-jump: no braking in the air, and you land in a slide with the speed you had ----
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.8f);
            Binds.TestPress(Bind.Jump);
            yield return new WaitForSeconds(0.2f);
            yield return MeasureSpeed(pc, sp);
            float before = sp[0];
            bool airborne = !pc.Grounded;
            Binds.TestHold(Bind.Slide, true);
            yield return new WaitForSeconds(0.05f);
            yield return MeasureSpeed(pc, sp);
            float inAir = sp[0];
            bool stillAir = !pc.Grounded, crouchedInAir = pc.Crouching;
            Check(airborne && stillAir && crouchedInAir && inAir >= before * 0.9f && before > Cfg.SprintSpeed * 0.8f,
                $"pressing slide mid-jump keeps your speed ({before:0.0} -> {inAir:0.0} m/s, in the air {airborne}/{stillAir}, crouched {crouchedInAir})");
            float wait = 0f;
            while (!pc.Sliding && wait < 1.5f) { wait += Time.deltaTime; yield return null; }
            yield return MeasureSpeed(pc, sp);
            Check(pc.Sliding && sp[0] >= before * 0.9f, $"... and you land straight into a slide carrying it ({sp[0]:0.0} m/s after {wait:0.00}s, sliding {pc.Sliding})");

            // ---- pushing back against the slide: it stops and you're crouching ----
            int brakes = pc.SlideBrakes;
            Binds.TestHold(Bind.Forward, false);
            Binds.TestHold(Bind.Back, true);
            yield return new WaitForSeconds(0.25f);
            yield return MeasureSpeed(pc, sp);
            Check(!pc.Sliding && pc.Crouching && pc.SlideBrakes == brakes + 1 && sp[0] <= Cfg.CrouchSpeed + 0.6f,
                $"pushing the other way ends the slide in a crouch (sliding {pc.Sliding}, crouching {pc.Crouching}, {sp[0]:0.0} m/s, crouch speed {Cfg.CrouchSpeed})");
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(0.4f);
            Check(!pc.Crouching, "letting go of C stands you back up");

            // a normal slide on the ground still works (and still ends when it runs out)
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.3f);
            Binds.TestHold(Bind.Forward, true);
            Binds.TestHold(Bind.Sprint, true);
            yield return new WaitForSeconds(0.7f);
            Binds.TestHold(Bind.Slide, true);
            yield return new WaitForSeconds(0.15f);
            Check(pc.Sliding, "sliding from a run on the ground still works");
            Binds.TestReleaseAll();
            yield return new WaitForSeconds(0.5f);

            // ---- tree camo: LMB / RMB held roots you to the spot ----
            me.ServerGive(Item.TreeCamo, 1);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(me, Item.TreeCamo);
            pc.LocalTeleport(lane, laneYaw);
            yield return new WaitForSeconds(0.5f);
            Check(me.TreeCamo && me.TreeCamoVisual != null, "holding the tree camo makes you a tree");
            Binds.TestHold(Bind.Forward, true);
            var p0 = pc.transform.position;
            yield return new WaitForSeconds(0.4f);
            float walked = Vector3.Distance(p0, pc.transform.position);
            foreach (var b in new[] { Bind.Attack, Bind.Aim })
            {
                Binds.TestHold(b, true);
                yield return new WaitForSeconds(0.15f);
                var p1 = pc.transform.position;
                var rot0 = me.TreeCamoVisual.rotation;
                // look around (turn the view) while rooted
                pc.LocalTeleport(p1, laneYaw + 120f);
                yield return new WaitForSeconds(0.5f);
                var dp = pc.transform.position - p1;
                dp.y = 0;
                float turned = Quaternion.Angle(rot0, me.TreeCamoVisual.rotation);
                Check(pc.TreeLocked && dp.magnitude < 0.05f && walked > 1f && turned < 0.5f,
                    $"tree camo + {(b == Bind.Attack ? "LMB" : "RMB")} held: you stay put while holding W ({dp.magnitude:0.00} m, {walked:0.0} m unlocked), the view turns and the tree doesn't ({turned:0.0} deg)");
                if (b == Bind.Attack) yield return Snap("tree_lock");
                Binds.TestHold(b, false);
                pc.LocalTeleport(pc.transform.position, laneYaw);
                yield return new WaitForSeconds(0.1f);
                p0 = pc.transform.position;
                yield return new WaitForSeconds(0.4f);
                Check(!pc.TreeLocked && Vector3.Distance(p0, pc.transform.position) > 1f, $"letting go of {(b == Bind.Attack ? "LMB" : "RMB")} lets you walk again");
            }
            Binds.TestReleaseAll();
            for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.TreeCamo) me.Inv[i] = default;
            yield return Hold(me, Item.Rock);

            // ---- no aim text on trees (rocks still say what they are) ----
            ResourceNode tree = null;
            float best = float.MaxValue;
            foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            {
                if (n.Kind.Value != ResourceNode.Tree || n.Amount.Value <= 0) continue;
                float d = Vector3.Distance(n.transform.position, lane);
                if (d < best) { best = d; tree = n; }
            }
            if (tree != null)
            {
                var tp = tree.transform.position;
                bool shown = false;
                foreach (float ang in new[] { 0f, 90f, 180f, 270f })
                {
                    var stand = tp + Quaternion.Euler(0, ang, 0) * Vector3.forward * 2.2f;
                    stand.y = MapBuilder.Height(stand.x, stand.z) + 0.1f;
                    pc.LocalTeleport(stand, ang + 180f);
                    yield return new WaitForSeconds(0.25f);
                    var ray = pc.CenterRay();
                    bool onTree = Physics.Raycast(ray, out var hit, 6f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<ResourceNode>() == tree;
                    if (!onTree) continue;
                    shown = true;
                    Check(!pc.AimText.Contains("Tree") && !pc.AimText.Contains("wood"), $"pointing at a tree shows no text (\"{pc.AimText}\")");
                    yield return Snap("tree_no_ui");
                    break;
                }
                if (!shown) Log("note: couldn't line up on a tree trunk for the no-aim-text check");
            }
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Graves: dying leaves one where you fell (no collider), and it's still there after you're back.</summary>
        IEnumerator GraveTests(PlayerNet me, PlayerController pc, NetGame g, int count0, Vector3 diedAt)
        {
            Check(g.Graves.Count == count0 + 1, $"dying put a gravestone down ({count0} -> {g.Graves.Count})");
            if (g.Graves.Count == 0) yield break;
            var gi = g.Graves[g.Graves.Count - 1];
            var flat = gi.Pos - diedAt;
            flat.y = 0;
            yield return null;
            var go = GraveFx.Get(g.Graves.Count - 1);
            Check(flat.magnitude < 2f && go != null && go.GetComponentsInChildren<Collider>(true).Length == 0 && go.GetComponentsInChildren<Renderer>().Length >= 3,
                $"the grave is where you died ({flat.magnitude:0.00} m off), drawn, with no collider");
            yield return new WaitForSeconds(Cfg.RespawnTime + 1.5f);
            if (me.Dead.Value) me.ServerRespawn(false);
            yield return new WaitForSeconds(0.5f);
            // a few more for the picture (any team colour)
            for (int t = 0; t < 3; t++) g.ServerAddGrave(gi.Pos + new Vector3(1.4f * (t + 1), 2f, 0.6f * t), gi.Yaw + t * 15f, t + 1 < Cfg.TeamColor.Length ? t + 1 : 0);
            yield return new WaitForSeconds(0.3f);
            var look = gi.Pos + Quaternion.Euler(0, gi.Yaw, 0) * Vector3.forward * 4.5f + new Vector3(2f, 0.1f, 0);
            look.y = MapBuilder.Height(look.x, look.z) + 0.1f;
            var to = gi.Pos + new Vector3(2f, 0, 0) - look;
            pc.LocalTeleport(look, Quaternion.LookRotation(new Vector3(to.x, 0, to.z)).eulerAngles.y);
            yield return Snap("graves");
            Check(!me.Dead.Value && g.Graves.Count == count0 + 4 && GraveFx.Shown == g.Graves.Count && go != null && g.Graves[count0].Equals(gi),
                $"the grave stays after you come back, and more pile up ({g.Graves.Count - count0} new, {GraveFx.Shown} drawn)");
            Check(AllGravesCrosses(out string why), $"every grave is the same stone cross, only the team band differs ({why})");
        }

        /// <summary>Every grave drawn is the stone cross: an upright and a bar in the stone colour, a team-colour band, nothing else.</summary>
        static bool AllGravesCrosses(out string why)
        {
            int n = GraveFx.Shown, crosses = 0;
            Color? stone = null;
            bool same = true;
            for (int i = 0; i < n; i++)
            {
                var go = GraveFx.Get(i);
                if (go == null) continue;
                var st = go.transform.Find("stone");
                if (st == null || st.childCount != 3) continue;
                var up = st.Find("cross upright");
                var bar = st.Find("cross bar");
                if (up == null || bar == null || st.Find("team band") == null) continue;
                var c = up.GetComponent<Renderer>().sharedMaterial.color;
                if (stone == null) stone = c;
                same &= stone.Value == c && bar.GetComponent<Renderer>().sharedMaterial.color == c;
                crosses++;
            }
            why = $"{crosses} of {n} are crosses, same stone colour {same}";
            return n > 0 && crosses == n && same;
        }
    }
}
