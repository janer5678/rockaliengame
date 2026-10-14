using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt3 -host -solo -rules autowood -shotdir DIR (add -capture for the sudden death round's capture
        /// instead of its stadium fight): the hammer can't hurt a player; the ball buff x1.5 in a 1v1; the death wand 4000;
        /// the airdrop - AIRDROP INCOMING with the ship coming at once and a slower beam (landing when it used to), one
        /// special item and never the same one twice a game; the gun's ammo top left; the barricade taller and wider; the
        /// stun trap (Advanced Trade Station, 250): an enemy on it is held, unhurt, and seen through walls, your own team
        /// walks over it; SUDDEN DEATH - the bases wiped (the middle kept), the ball back under the dome, everyone home, the
        /// walls up for a minute, the basics only, then 5 minutes: the first ball in a machine wins, or the stadium fight.
        /// </summary>
        IEnumerator Prompt3Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            bool capture = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture") >= 0;

            // ---- the numbers
            Check(Mathf.Approximately(Cfg.BallGatherNow, 1.5f) && Mathf.Approximately(Cfg.BallGatherMul, 1.5f), $"the ball buff is x1.5 (x{Cfg.BallGatherNow}; every mode x{Cfg.BallGatherMul})");
            Check(Cfg.CraftRecipe(Cfg.CraftIndexOf(Item.DeathWand), team).Wood == 5000, $"the death wand costs 5000 ({Cfg.CraftRecipe(Cfg.CraftIndexOf(Item.DeathWand), team).Wood})");
            Check(Cfg.ItemName(Item.BearTrap) == "Stun Trap" && Cfg.CraftIndexOf(Item.BearTrap) < 0 && Mathf.Approximately(Cfg.BearTrapHold, 3f),
                $"the stun trap: taken out of crafting (its code stays: one put down still holds {Cfg.BearTrapHold} s)");
            Check(Mathf.Abs(Deployables.BarricadeH - 1.45f * 1.3f) < 0.01f && Mathf.Abs(Deployables.BarricadeLen - 2.4f * 1.3f) < 0.01f, $"the barricade is 30% taller and wider ({Deployables.BarricadeLen} x {Deployables.BarricadeH} m)");
            Check(Mathf.Approximately(NetGame.DropLand, 9f + 8f + NetGame.DropWarning) && NetGame.DropArrive < 2f, $"the airdrop's beam is out {NetGame.DropArrive} s after the notice and takes {NetGame.DropBeam} s: the crate lands when it used to ({NetGame.DropLand} s)");
            {
                int mask = Cfg.AirdropItemMask, mask2 = Cfg.AirdropItemMask2;
                Cfg.PickAllAirdrops(true);
                var seen = new HashSet<Item>();
                bool oneItem = true, repeat = false;
                int n = Cfg.AirdropLoot.Count;
                for (int i = 0; i < n; i++)
                {
                    int before = seen.Count;
                    var c = NetGame.RollAirdropCrate(seen);
                    oneItem &= c.Count == 1 && c[0].Id != Item.Wood && c[0].Id != Item.Stone && !c[0].Empty;
                    repeat |= seen.Count == before; // (the same item again: the set didn't grow)
                }
                Check(oneItem, "an airdrop crate is one special item, no wood");
                Check(seen.Count == n && !repeat, $"{n} airdrops in a game: {seen.Count} different items, none twice");
                Cfg.AirdropItemMask = mask; Cfg.AirdropItemMask2 = mask2;
            }

            // ---- the walls down, the clock stopped
            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(5f); // (the glass slides down)

            // ---- AIRDROP INCOMING, the ship straight away
            {
                foreach (var c in new List<Container>(Container.All)) if (c != null && c.IsAirdrop && c.IsSpawned) c.NetworkObject.Despawn(true);
                g.ServerAirdropIncoming(g.NetworkManager.ServerTime.Time);
                yield return new WaitForSeconds(0.5f);
                Check(Hud.LastBanner.Contains("AIRDROP INCOMING"), $"the airdrop notice says AIRDROP INCOMING ({Hud.LastBanner})");
                g.NextDropLands.Value = -1;
            }

            // ---- the gun's ammo, top left
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            me.ServerGive(Item.Revolver, 1, Cfg.RevolverMag);
            me.ServerGive(Item.RevolverAmmo, 30);
            yield return Hold(me, Item.Revolver);
            yield return new WaitForSeconds(0.3f);
            Check(Time.frameCount - Hud.AmmoShownFrame < 5 && Hud.AmmoRect.x < 40f && Hud.AmmoRect.y < Screen.height * 0.25f, $"the revolver's ammo shows top left ({Hud.AmmoRect})");
            yield return Snap("prompt3_ammo_top_left");
            me.ServerGive(Item.Bow, 1);
            me.ServerGive(Item.Arrow, 12);
            yield return Hold(me, Item.Bow);
            yield return new WaitForSeconds(0.3f);
            Check(Time.frameCount - Hud.AmmoShownFrame < 5, "the bow's arrows show there too");

            // ---- an enemy to hit and trap
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.8f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            Check(bot != null, "an enemy bot");
            if (bot != null)
            {
                foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
                if (bot.Dead.Value) bot.ServerRespawn(false);
                yield return new WaitForSeconds(0.3f);

                // the hammer: no damage to a player
                var at = me.transform.position + me.transform.forward * 1.6f;
                at.y = MapBuilder.Height(at.x, at.z) + 0.1f;
                bot.TeleportRpc(at, Quaternion.LookRotation(me.transform.position - at).eulerAngles.y);
                yield return new WaitForSeconds(0.5f);
                bot.Health.Value = Cfg.MaxHealth;
                me.ServerGive(Item.Hammer, 1);
                yield return Hold(me, Item.Hammer);
                yield return new WaitForSeconds(Cfg.HammerSwingTime + 0.1f);
                me.MeleeRpc(true, bot.NetworkObject, bot.transform.position + Vector3.up * 1.1f, false);
                yield return new WaitForSeconds(Cfg.HammerSwingTime + 0.1f);
                me.MeleeRpc(true, bot.NetworkObject, bot.EyePos, false);
                yield return new WaitForSeconds(0.4f);
                Check(bot.Health.Value >= Cfg.MaxHealth && !bot.Dead.Value, $"two hammer hits (body, head) do nothing to a player ({bot.Health.Value:0} HP)");
                me.ServerGive(Item.Hatchet, 1);
                yield return Hold(me, Item.Hatchet);
                yield return new WaitForSeconds(Cfg.Melee(Item.Hatchet).Cooldown + 0.1f);
                me.MeleeRpc(true, bot.NetworkObject, bot.transform.position + Vector3.up * 1.1f, false);
                yield return new WaitForSeconds(0.4f);
                Check(bot.Health.Value < Cfg.MaxHealth, $"...the hatchet still does ({bot.Health.Value:0} HP)");
                bot.Health.Value = Cfg.MaxHealth;

                // the stun trap: ours under the enemy - held, unhurt, seen through walls; ours under us - nothing
                var trapAt = OnGround(Cfg.BaseCenter[team] + side * 8f + back * 4f, 0f);
                var tgo = Instantiate(Bootstrap.I.containerPrefab, trapAt, Quaternion.identity);
                tgo.GetComponent<Container>().ServerInit(Container.Trap, team, 0, null);
                tgo.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                var trap = tgo.GetComponent<Container>();
                yield return new WaitForSeconds(0.3f);
                pc.LocalTeleport(trapAt + Vector3.up * 0.1f, 0f);
                yield return new WaitForSeconds(0.6f);
                Check(trap.Flag.Value == 0 && !me.Trapped, "our own stun trap doesn't go off on us");
                pc.LocalTeleport(trapAt - back * 4f, Quaternion.LookRotation(back).eulerAngles.y);
                bot.TeleportRpc(trapAt + Vector3.up * 0.1f, 0f);
                yield return new WaitForSeconds(0.6f);
                Check(trap.Flag.Value == 1 && bot.Trapped && bot.StunnedFor(team) && bot.Health.Value >= Cfg.MaxHealth, $"an enemy on our stun trap: held ({bot.Trapped}), unhurt ({bot.Health.Value:0} HP), seen through walls by us ({bot.StunnedFor(team)})");
                // a wall between us: they still show (the red pass drawn over everything)
                var wallAt = (trapAt + me.transform.position) * 0.5f;
                wallAt.y = MapBuilder.Height(wallAt.x, wallAt.z);
                var wall = SpawnKeylessPiece(PieceType.Wall, team, wallAt, Quaternion.LookRotation(back));
                pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 5f);
                yield return Snap("prompt3_stunned_through_wall");
                var p0 = bot.transform.position;
                yield return new WaitForSeconds(1f);
                Check(Vector3.Distance(bot.transform.position, p0) < 0.3f, "held still in the trap");
                yield return new WaitForSeconds(Cfg.BearTrapHold);
                Check(!bot.Trapped && !bot.StunnedFor(team), $"let go after {Cfg.BearTrapHold} s - not seen through walls any more");
                if (wall != null && wall.IsSpawned) wall.NetworkObject.Despawn(true);

                // an enemy trap under us: held
                var eAt = OnGround(Cfg.BaseCenter[team] - side * 8f + back * 4f, 0f);
                var ego = Instantiate(Bootstrap.I.containerPrefab, eAt, Quaternion.identity);
                ego.GetComponent<Container>().ServerInit(Container.Trap, enemy, 0, null);
                ego.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                yield return new WaitForSeconds(0.3f);
                me.Health.Value = Cfg.MaxHealth;
                pc.LocalTeleport(eAt + Vector3.up * 0.1f, 0f);
                yield return new WaitForSeconds(0.6f);
                Check(me.Trapped && me.StunnedFor(enemy) && me.Health.Value >= Cfg.MaxHealth, $"an enemy stun trap under us: held, unhurt ({me.Health.Value:0} HP)");
                yield return new WaitForSeconds(Cfg.BearTrapHold + 0.3f);
                bot.TeleportRpc(OnGround(Cfg.BaseCenter[enemy]), 0f);
            }

            // ---- SUDDEN DEATH: build in the base, a piece in the middle, then the clock runs out with nobody's ball in a machine
            var inBase = OnGround(Cfg.BaseCenter[team] + side * 6f - back * 6f, 0f);
            var basePiece = SpawnKeylessPiece(PieceType.Wall, team, inBase, Quaternion.LookRotation(back));
            var chest = SpawnChest(team, OnGround(Cfg.BaseCenter[team] - side * 6f - back * 6f, 0f), new List<ItemStack> { ItemStack.Of(Item.Wood, 500) });
            var midAt = OnGround(new Vector3(MapBuilder.DomeRadius + 6f, 0f, MapBuilder.DomeRadius + 6f), 0f);
            var midPiece = SpawnKeylessPiece(PieceType.Wall, team, midAt, Quaternion.identity);
            g.ServerDropItem(ItemStack.Of(Item.Wood, 50), OnGround(Cfg.BaseCenter[team] + back * 3f, 0.3f), Vector3.forward, OnGround(Cfg.BaseCenter[team] + back * 3f, 0.3f));
            yield return new WaitForSeconds(0.8f);
            if (Ball.Instance != null && Ball.Instance.SocketTeam.Value >= 0) Ball.Instance.ServerDrop(Vector3.up * 3f, Vector3.zero);
            // carrying the ball out in the open (it goes back under the dome)
            pc.LocalTeleport(midAt + Vector3.right * 3f, 0f);
            yield return new WaitForSeconds(0.4f);
            if (Ball.Instance != null) Ball.Instance.ServerPickup(me);
            yield return new WaitForSeconds(0.3f);
            int wm = g.WoodGenLevelOf(team);
            g.TimerPaused.Value = false;
            g.DevSetTimeLeft(1.5f);
            for (float t = 0; t < 8f && !g.SuddenBall.Value; t += 0.25f) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1.2f);
            Check(g.SuddenBall.Value && g.S == GameState.PreBall && !g.Overtime.Value, $"the clock ran out with nobody's ball in a machine: SUDDEN DEATH, no overtime ({g.S}, overtime {g.Overtime.Value})");
            Check(g.TimeLeft > Cfg.SuddenWallTime - 5f && g.TimeLeft <= Cfg.SuddenWallTime && MapBuilder.GlassUp, $"the walls are back up for a minute ({g.TimeLeft:0} s)");
            Check(basePiece == null || !basePiece.IsSpawned, "the base's pieces are gone");
            Check(chest == null || !chest.IsSpawned, "...and its chest");
            bool baseLoot = false;
            foreach (var it in g.Items) if (Cfg.BaseTeamAt(it.Pos) == team) baseLoot = true;
            Check(!baseLoot, "...and the loot on its floor");
            Check(wm >= 1 && g.WoodGenLevelOf(team) == 0, $"the wood machine is gone till the walls drop (level {wm} -> {g.WoodGenLevelOf(team)})");
            Check(midPiece != null && midPiece.IsSpawned, "a piece out in the middle stays");
            Check(Ball.Instance != null && !Ball.Instance.IsCarried && Vector3.Distance(Ball.Instance.transform.position, Ball.DomeSpot) < 0.5f, "the ball is back under the dome");
            Check(Cfg.BaseTeamAt(me.transform.position) == team, $"we're sent home ({me.transform.position})");
            Check(Cfg.SuddenLocked(Item.Workbench) && Cfg.SuddenLocked(Item.Sword) && Cfg.SuddenLocked(Item.BearTrap) && !Cfg.SuddenLocked(Item.Hatchet) && !Cfg.SuddenLocked(Item.Spear), "the basics only: no Trade Stations or their things");
            {
                var list = new List<int>();
                Cfg.CraftList(list, 2);
                Check(!list.Contains(Cfg.CraftIndexOf(Item.Workbench)) && !list.Contains(Cfg.CraftIndexOf(Item.Workbench2)) && list.Contains(Cfg.CraftIndexOf(Item.Hatchet)), "the Trade Stations aren't even on the crafting list in sudden death");
            }
            {
                me.ServerGive(Item.Wood, 3000);
                int benches0 = me.Count(Item.Workbench), hatchets0 = me.Count(Item.Hatchet);
                me.CraftRpc(Cfg.CraftIndexOf(Item.Workbench));
                yield return new WaitForSeconds(0.4f);
                me.CraftRpc(Cfg.CraftIndexOf(Item.Hatchet));
                yield return new WaitForSeconds(0.6f);
                Check(me.Count(Item.Workbench) == benches0 && me.Count(Item.Hatchet) == hatchets0 + 1, "a Trade Station is refused, a hatchet crafts");
            }
            pc.SetLook(Cfg.SpawnYaw(team), 0f);
            yield return Snap("prompt3_sudden_death_walls_up");

            // ---- the walls drop: 5 minutes
            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 6f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1.5f);
            Check(g.S == GameState.BallLive && g.SuddenBall.Value && g.TimeLeft > Cfg.SuddenBallTime - 10f && g.TimeLeft <= Cfg.SuddenBallTime, $"the walls dropped: {g.TimeLeft:0} s to get the ball into a machine");
            Check(g.WoodGenLevelOf(team) == 1, $"the wood machine landed again at level 1 ({g.WoodGenLevelOf(team)})");
            yield return Snap("prompt3_sudden_death_ball");
            if (capture)
            {
                // a capture doesn't end it any more: the ball has to be in your machine when the time runs out
                Ball.Instance.ServerSocket(team);
                yield return new WaitForSeconds(1.5f);
                Check(g.S == GameState.BallLive && g.SuddenBall.Value, $"a capture doesn't end sudden death on the spot ({g.S})");
                g.DevSetTimeLeft(1.5f);
                for (float t = 0; t < 8f && g.S != GameState.GameOver; t += 0.25f) yield return new WaitForSeconds(0.25f);
                Check(g.S == GameState.GameOver && g.Winner.Value == team, $"when the time's up, the ball in our machine wins ({g.S}, winner {g.Winner.Value})");
            }
            else
            {
                g.DevSetTimeLeft(1.5f);
                for (float t = 0; t < 8f && g.S != GameState.SuddenDeath; t += 0.25f) yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(1f);
                Check(g.S == GameState.SuddenDeath && SpaceArena.NearArena(me.transform.position), $"nobody got it in: the rock fight in the stadium ({g.S})");
                yield return Snap("prompt3_stadium_fight");
            }
            Log("prompt3 done");
            Application.Quit(0);
        }
    }
}
