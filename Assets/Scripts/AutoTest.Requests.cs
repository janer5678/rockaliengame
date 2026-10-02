using System.Collections;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest modes (classic, arsenal, autowood, dna): the ball under the glass dome, the ball buff, airdrop rarity, the airdrop
    /// warning and the ship (hatch, slow crate, shrinking away), harder thrown spears, breaking your own pieces, the portal gun that
    /// never runs out, airdrop rocket launchers with 3 rockets, Ctrl crouch / C slide,
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

            string rn = Cfg.RulesName(Cfg.Rules).ToLower().Replace(" ", "");
            bool wasPaused = g.TimerPaused.Value;
            if (g.S == GameState.PreBall) g.TimerPaused.Value = true; // (these need the wall up for a while)

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
                while (E() < NetGame.DropLand + 3.2) yield return null;
                float mid = AirdropShip.ShipScale;
                var ship = AirdropShip.ShipTransform;
                float midScale = ship != null ? ship.localScale.x : -1f;
                yield return Snap("airdrop_ship_leaving");
                while (E() < NetGame.DropLand + 5.5) yield return null;
                float late = AirdropShip.ShipScale;
                yield return Snap("airdrop_ship_leaving_far");
                while (E() < NetGame.DropLand + 6.5) yield return null;
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
        }
    }
}
