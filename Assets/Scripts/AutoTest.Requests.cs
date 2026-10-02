using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest modes (classic, arsenal, autowood, dna): harder thrown spears, breaking your own pieces, the portal gun that
    /// never runs out, airdrop rocket launchers with 3 rockets, the airdrop timer under the top banner, Ctrl crouch / C slide,
    /// and solid border rocks.
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

            // the airdrop timer always knows when the next one comes
            {
                double now = g.NetworkManager.ServerTime.Time, next = g.NextScheduledDrop.Value;
                if (Cfg.AirdropCount > 0 && g.S == GameState.PreBall)
                    Check(next > g.PhaseEnd.Value, $"airdrop timer: the first one is due {next - now:0}s from now (after the wall drops)");
                else Check(Cfg.AirdropCount == 0 || g.S != GameState.BallLive || next < 0 || next > now - 1, $"airdrop timer: next {next - now:0}s");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return Snap("airdrop_timer_" + Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", ""));
            }

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

            // the portal gun never runs out (and the oldest pairs go once there are lots)
            {
                int p0 = g.Portals.Count;
                me.ServerGive(Item.PortalGun, 1, Cfg.MaxData(Item.PortalGun));
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, Item.PortalGun);
                var at = Cfg.SpawnPos(team) - Cfg.BackDir(team) * 9f;
                for (int i = 0; i < 6; i++)
                {
                    var p = at + new Vector3(i * 2.5f - 6f, 0, 0);
                    p.y = MapBuilder.Height(p.x, p.z);
                    me.PortalRpc(p, Vector3.up);
                    yield return new WaitForSeconds(0.5f);
                }
                Check(me.Count(Item.PortalGun) == 1 && g.Portals.Count == p0 + 6, $"the portal gun is still there after 6 shots ({g.Portals.Count - p0} portals, {me.Count(Item.PortalGun)} gun)");
                for (int i = 0; i < 40; i++) g.ServerAddPortal(at + Vector3.down * 50f, Vector3.up, g.ServerNewPortalPair());
                Check(g.Portals.Count <= NetGame.MaxPortals, $"no more than {NetGame.MaxPortals} portals at once ({g.Portals.Count})");
                g.Portals.Clear();
                for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == Item.PortalGun) me.Inv[i] = default;
            }

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
        }
    }
}
