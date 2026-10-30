using System.Collections;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest bots2 -host -solo -mode 2v2 -shotdir DIR: the bot patch (botprompt1) - a bot shuts the doors behind it
        /// (BotBrain.TickDoors), puts its chest on a foundation (BotBrain.FoundationSpot), has a personality of four (the
        /// Rusher waits at the glass and is first to the ball when it drops), and can't take the ball through a wall
        /// (PlayerNet.WallBetween in PickupBallRpc).
        /// </summary>
        IEnumerator Bots2Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            g.TimerPaused.Value = true;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var c = Cfg.BaseCenter[team];
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            Check(System.Enum.GetValues(typeof(BotBrain.Bent)).Length == 4, "four bot personalities: " + string.Join(", ", System.Enum.GetNames(typeof(BotBrain.Bent))));

            // ---- our inner room (with its doorways) and some foundations at the back, put up by us
            me.ServerGive(Cfg.BuildItem, 3000);
            if (me.Count(Item.BuildingPlan) == 0) me.ServerGive(Item.BuildingPlan, 1);
            pc.LocalTeleport(OnGround(c - back * 1.5f), Cfg.SpawnYaw(team));
            yield return new WaitForSeconds(0.5f);
            yield return Hold(me, Item.BuildingPlan);
            int doors = 0;
            for (int pass = 0; pass < 2; pass++) // (the foundations hold up the walls off the bedrock: a second pass gets those)
                foreach (var p in BotBrain.HousePlan(team))
                {
                    if ((p.Stage != 0 && p.Stage != 3) || BuildGrid.Registry.ContainsKey(p.Key)) continue;
                    var pm = BuildGrid.PieceCenter(p.Key);
                    if (Flat(pm, me.transform.position) > 6f)
                    {
                        var to = pm - c; to.y = 0f;
                        pc.LocalTeleport(OnGround(pm + (to.sqrMagnitude > 0.01f ? to.normalized : -back) * 3f), Cfg.SpawnYaw(team));
                        yield return new WaitForSeconds(0.2f);
                    }
                    me.PlaceRpc((byte)p.Type, p.Key.I, p.Key.J, p.Key.L, p.Key.D);
                    yield return new WaitForSeconds(Cfg.BuildCooldown + 0.05f);
                    if (p.Type == PieceType.Doorway && BuildGrid.Registry.ContainsKey(p.Key)) doors++;
                }
            yield return new WaitForSeconds(1.2f);
            Check(BotBrain.Enclosed(team) && doors >= 1, $"our machine's room is up and shut ({doors} doorways)");
            pc.LocalTeleport(OnGround(c - back * 14f), Cfg.SpawnYaw(team)); // (out of the way)

            // ---- a bot on our team inside the room heads out to farm: through a door, and shuts it behind it
            PlayerNet.ServerAddBot(team);
            yield return new WaitForSeconds(0.6f);
            PlayerNet mate = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value && p.Team.Value == team) mate = p;
            var mb = mate != null ? mate.GetComponent<BotBrain>() : null;
            Check(mb != null, "a bot joined our team");
            if (mb == null) { Application.Quit(1); yield break; }
            if (mate.Dead.Value) mate.ServerRespawn(false);
            mate.TeleportRpc(OnGround(c + back * 1.5f + side * 1.5f), 0f);
            yield return null;
            foreach (var p in BotBrain.HousePlan(team)) // (all shut to start with: it may have been out and about already)
                if (p.Stage == 0 && BuildGrid.Registry.TryGetValue(p.Key, out var ds) && ds != null && ds.HasDoor) ds.DoorOpen.Value = false;
            float until = Time.time + 40f;
            while (Time.time < until && mb.DoorsShut == 0) yield return null;
            int missing = 0, open = 0;
            foreach (var p in BotBrain.HousePlan(team))
            {
                if (p.Stage != 0) continue;
                if (!BuildGrid.Registry.TryGetValue(p.Key, out var s) || s == null) { missing++; Log($"room piece missing: {p.Type} {p.Key}"); }
                else if (s.HasDoor && s.DoorOpen.Value) open++;
            }
            var mp = mate.transform.position - c;
            Check(mb.DoorsShut >= 1, $"it goes out by a door and shuts it behind it ({mb.DoorsOpened} opened, {mb.DoorsShut} shut, {open} open now, {missing} room pieces missing, it's {Mathf.Max(Mathf.Abs(mp.x), Mathf.Abs(mp.z)):0.0} m out, {mb.Job}) | {string.Join(" > ", mb.Trace)}");
            yield return Snap("bots2_door_shut");

            // ---- given a chest and wood, it puts the chest on one of the base's foundations
            mate.ServerGive(Item.Chest, 1);
            mate.ServerGive(Cfg.CurrencyItem, 600);
            until = Time.time + 45f;
            Container onFound = null;
            while (Time.time < until && onFound == null)
            {
                foreach (var k in Container.All)
                    if (k != null && k.IsSpawned && k.Kind.Value == Container.Chest && k.Team.Value == team
                        && Physics.Raycast(k.transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 1.5f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                    {
                        var s = hit.collider.GetComponentInParent<Structure>();
                        if (s != null && s.PType == PieceType.Foundation) onFound = k;
                    }
                yield return new WaitForSeconds(0.5f);
            }
            Check(onFound != null, $"its chest goes down on a foundation ({mb.Job}, {mb.Priority})");
            yield return Snap("bots2_chest_on_foundation");
            PlayerNet.ServerRemoveBot(team);
            yield return new WaitForSeconds(0.3f);

            // ---- a Rusher: waits at the glass by the ball for the last half minute, then is first to it
            BotBrain.TestBent = (int)BotBrain.Bent.Rusher;
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.6f); // (it picks its personality on its first frame)
            BotBrain.TestBent = -1;
            PlayerNet rush = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value && p.Team.Value == enemy) rush = p;
            var rb = rush != null ? rush.GetComponent<BotBrain>() : null;
            Check(rb != null && rb.Leaning == BotBrain.Bent.Rusher, $"a Rusher bot joined the enemy ({(rb != null ? rb.Leaning.ToString() : "none")})");
            if (rb == null) { Application.Quit(1); yield break; }
            if (rush.Dead.Value) rush.ServerRespawn(false);
            g.PhaseEnd.Value = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time + 28f; // (28 s to the drop, held there)
            until = Time.time + 40f;
            while (Time.time < until && rb.AtWallAt < 0f) yield return null;
            var ballPos = Ball.Instance != null ? Ball.Instance.transform.position : Vector3.zero;
            Check(rb.Priority == BotBrain.Plan.Rush && rb.AtWallAt > 0f, $"with 28 s to go it's waiting at the glass ({Flat(rush.transform.position, ballPos):0} m from the ball, {rb.Priority})");
            yield return Snap("bots2_rusher_at_wall");
            g.TimerPaused.Value = false;
            g.DevSkipPhase(GameState.PreBall);
            until = Time.time + 30f;
            while (Time.time < until && !rush.CarryingBall) yield return null;
            Check(rush.CarryingBall, $"the wall drops: it's first to the ball ({rb.Job}, {Flat(rush.transform.position, ballPos):0} m from the middle)");

            // ---- the ball in our machine, our room shut round it: it can't be taken through the wall
            g.TimerPaused.Value = true;
            rush.ServerKill(null);
            yield return new WaitForSeconds(0.5f);
            if (Ball.Instance.IsCarried) { Check(false, "the ball's still carried after the kill"); Application.Quit(1); yield break; }
            Ball.Instance.ServerSocket(team);
            yield return new WaitForSeconds(0.5f);
            rush.ServerRespawn(false);
            yield return new WaitForSeconds(0.3f);
            var sock = Ball.Instance.transform.position;
            var outside = OnGround(c + side * 3.7f);
            rush.TeleportRpc(outside, Quaternion.LookRotation(-side).eulerAngles.y);
            yield return new WaitForSeconds(0.2f);
            float reach = Vector3.Distance(rush.EyePos, sock);
            bool wall = PlayerNet.WallBetween(rush.EyePos, sock);
            rush.PickupBallRpc();
            yield return null;
            Check(wall && reach <= Cfg.InteractRange + 2f && !rush.CarryingBall, $"outside the wall, {reach:0.0} m from the ball (in reach): it can't take it through the wall (wall between: {wall})");
            rush.TeleportRpc(OnGround(c + side * 1.4f), Quaternion.LookRotation(-side).eulerAngles.y);
            yield return new WaitForSeconds(0.2f);
            bool clear = !PlayerNet.WallBetween(rush.EyePos, sock);
            rush.PickupBallRpc();
            yield return null;
            Check(clear && rush.CarryingBall, $"inside the room with it, nothing between: it takes it (clear: {clear})");

            Log("bots2 done");
            Application.Quit(0);
        }
    }
}
