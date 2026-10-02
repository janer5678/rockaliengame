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
        }
    }
}
