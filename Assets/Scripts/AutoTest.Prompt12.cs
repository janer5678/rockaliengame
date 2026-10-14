using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt12 -host -solo [-rules autowood | clipfarm] -shotdir DIR: the crossbow 500; picking where to
        /// respawn in the wild everywhere (never close to an enemy base or an airdrop); the death wand in the airdrops; the
        /// one-use knockback stick never leaving anyone stuck in the map's edge or corners; the one-use lifting stick; the
        /// tree breaker good for 5000 wood of trees at 400 a tree; the ball in your machine doubling your wood machine (and
        /// saying so top left); the spear's arc; and CLIP FARM (ClipFarm.cs): loaded with airdrop items, airdrops every
        /// 45 s, airdrop bushes, and the hype (DOUBLE KILL, LONG SHOT...) with Steam timeline markers.
        /// </summary>
        IEnumerator Prompt12Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);

            // ---- the numbers
            {
                int xi = Cfg.CraftIndexOf(Item.Crossbow);
                int xw = xi >= 0 ? Cfg.CraftRecipe(xi, team).Wood : -1;
                Check(xw == 500, $"the crossbow costs {xw}");
                Check(Cfg.StickOfTruthShots == 1 && Cfg.MaxData(Item.StickOfTruth) == 1, "the lifting stick is one use");
                Check(Cfg.MaxData(Item.TreeBreaker) * Cfg.TreeBreakerUnit == 5000 && Cfg.TreeBreakerWood == 400, $"the tree breaker: {Cfg.MaxData(Item.TreeBreaker) * Cfg.TreeBreakerUnit} wood of trees, {Cfg.TreeBreakerWood} a tree");
                Check(Cfg.InAirdropPool(Item.DeathWand) && Cfg.AirdropLoot.Contains(Item.DeathWand), "the death wand's in the airdrops");
                Check(Cfg.SpearGravity >= 20f && Cfg.SpearStraightRange <= 0f, $"the spear arcs down (gravity {Cfg.SpearGravity}, straight for {Cfg.SpearStraightRange} m)");
                Check(Cfg.WildPicker, "respawn in the wild: pick the spot (every mode but the tutorial)");
                // the knockback never lands anyone in the edge zone, from anywhere, any way
                bool safe = true; string worst = "";
                for (int i = 0; i < 400; i++)
                {
                    float h = Cfg.MapHalf - Cfg.KnockEdge - 0.5f;
                    var from = new Vector3(Random.Range(-h, h), 0f, Random.Range(-h, h));
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float push = Cfg.KnockStick1Push;
                    Cfg.SafeKnock(from, ref dir, ref push, 16f);
                    var to = from + dir * Cfg.KnockDistance(push, 16f);
                    if (Mathf.Abs(to.x) > h + 1f || Mathf.Abs(to.z) > h + 1f) { safe = false; worst = $"{from} -> {to}"; }
                }
                Check(safe, $"400 random one-use knockbacks: none would carry anyone into the map's edge {worst}");
            }

            // ---- an enemy bot to try things on
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.8f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            if (bot == null) { Log("FAIL: no bot"); Application.Quit(1); yield break; }
            foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
            if (bot.Dead.Value) bot.ServerRespawn(false);

            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1f);
            g.TimerPaused.Value = true;

            // ---- Clip Farm: loaded up when the wall drops, every airdrop item in, airdrop bushes
            if (Cfg.ClipFarm)
            {
                Check(Cfg.ClassicMode && Cfg.ProgressMode && g.NoClock && Cfg.RulesName(Cfg.Rules) == "Clip Farm", "Clip Farm: Classic's rules (fill your bar, no clock)");
                Check(System.Array.IndexOf(Hud.MenuModes, GameRules.ClipFarm) >= 0, "it's on the menu");
                int wacky = 0;
                foreach (var st in me.Inv) if (!st.Empty && System.Array.IndexOf(Cfg.AirdropChoices, st.Id) >= 0) wacky++;
                Check(wacky >= 1, $"everyone got an airdrop item when the wall dropped ({wacky})");
                int all = 0; foreach (var i in Cfg.AirdropChoices) if (Cfg.InAirdropPool(i)) all++;
                Check(Cfg.AirdropLoot.Count == all, $"every airdrop item's in ({Cfg.AirdropLoot.Count} of {all})");
                int bushes = 0;
                foreach (var n in ResourceNode.All) if (n != null && n.TrapTeam.Value == ResourceNode.AirdropBush) bushes++;
                Check(bushes > 4, $"airdrop berry bushes all over the map ({bushes})");
            }

            // ---- the ball in your machine: the wood machine makes double, and the top left says so
            {
                int lvl = Cfg.WoodGenLevel(team);
                int rate = Cfg.WoodGenRate(lvl);
                Check(lvl >= 1 && !Cfg.BallDoublesWood(team) && Cfg.WoodPerSecondNow(team) == rate, $"no ball: the wood machine makes {Cfg.WoodPerSecondNow(team)} a second (level {lvl})");
                if (Ball.Instance != null) Ball.Instance.ServerSocket(team);
                yield return new WaitForSeconds(0.5f);
                Check(Cfg.BallDoublesWood(team) && Cfg.WoodPerSecondNow(team) == rate * 2, $"the ball in our machine: {Cfg.WoodPerSecondNow(team)} a second (x2)");
                int pile0 = WoodPileCount(g, team);
                yield return new WaitForSeconds(4.05f);
                int pile1 = WoodPileCount(g, team);
                Check(pile1 - pile0 >= rate * 2 * 3, $"...and the pile really grows twice as fast ({pile1 - pile0} in 4 s, {rate} a second without the ball)");
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                yield return new WaitForSeconds(0.5f);
                Check(Time.time - Hud.BallWoodShownAt < 0.5f && Hud.BallWoodShown.Contains("WOOD MACHINE x2") && Hud.BallWoodRect.x < Screen.width * 0.2f && Hud.BallWoodRect.y < Screen.height * 0.25f,
                    $"top left of the screen: {Hud.BallWoodShown} at {Hud.BallWoodRect}");
                yield return Snap("prompt12_ball_wood_x2");
                Ball.Instance.ServerPlaceInDome();
                yield return new WaitForSeconds(0.5f);
                Check(!Cfg.BallDoublesWood(team) && Time.time - Hud.BallWoodShownAt > 0.3f, "ball gone: back to normal, the box goes");
            }

            // ---- the tree breaker: 400 a tree, 5000 wood of trees in all
            {
                var spot = OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 14f) + side * 12f, 0.1f);
                ResourceNode Tree(Vector3 near)
                {
                    ResourceNode best = null; float bd = 1e9f;
                    foreach (var n in ResourceNode.All)
                    {
                        if (n == null || n.Kind.Value != ResourceNode.Tree || n.Amount.Value <= 0) continue;
                        float d = Vector3.Distance(n.transform.position, near);
                        if (d < bd) { bd = d; best = n; }
                    }
                    return best;
                }
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.ServerGive(Item.TreeBreaker, 1, Cfg.MaxData(Item.TreeBreaker));
                int SlotOf(Item id) { for (int i = 0; i < Cfg.PlayerSlots; i++) if (me.SlotAt(i).Id == id) return i; return -1; }
                for (int hit = 0; hit < 2; hit++)
                {
                    var tree = Tree(spot);
                    if (tree == null) { Check(false, "a tree to chop"); break; }
                    if (hit == 1) { int slot = SlotOf(Item.TreeBreaker); if (slot >= 0) me.Inv[slot] = ItemStack.Of(Item.TreeBreaker, 1, 5); } // (100 wood of trees left)
                    pc.LocalTeleport(OnGround(tree.transform.position - side * 1.6f), Quaternion.LookRotation(side).eulerAngles.y);
                    yield return Hold(me, Item.TreeBreaker);
                    yield return new WaitForSeconds(1f);
                    int w0 = me.Count(Item.Wood);
                    me.MeleeRpc(true, tree.NetworkObject, tree.transform.position + Vector3.up * 1f, false);
                    yield return new WaitForSeconds(0.5f);
                    int got = me.Count(Item.Wood) - w0;
                    if (hit == 0)
                    {
                        int left = SlotOf(Item.TreeBreaker) >= 0 ? me.SlotAt(SlotOf(Item.TreeBreaker)).Data * Cfg.TreeBreakerUnit : -1;
                        Check(got == 400 && tree.Amount.Value <= 0 && left == 5000 - 400, $"a tree falls in one hit for the full {got} wood; {left} wood of trees left on it");
                        yield return Snap("prompt12_tree_breaker");
                    }
                    else Check(got == 400 && me.Count(Item.TreeBreaker) == 0, $"the last bit of it still gives the full {got}, then it breaks ({me.Count(Item.TreeBreaker)} left)");
                }
            }

            {
                // the bot hits US towards a corner of the map (we fly for real: the bot's own moving is switched off)
                float c = Cfg.MapHalf - Cfg.KnockEdge - 6f;
                var corner = new Vector3(Mathf.Sign(side.x + back.x + 0.01f) * c, 0f, Mathf.Sign(side.z + back.z + 0.01f) * c);
                var outward = new Vector3(Mathf.Sign(corner.x), 0f, Mathf.Sign(corner.z)).normalized;
                var meAt = OnGround(corner, 0.1f);
                if (bot.Dead.Value) { bot.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                pc.LocalTeleport(meAt, Quaternion.LookRotation(-outward).eulerAngles.y);
                bot.TeleportRpc(OnGround(meAt - outward * 2.2f, 0.1f), Quaternion.LookRotation(outward).eulerAngles.y);
                yield return new WaitForSeconds(0.6f);
                for (int i = 0; i < bot.Inv.Count; i++) bot.Inv[i] = default;
                bot.ServerGive(Item.KnockStick1, 1);
                for (int i = 0; i < Cfg.HotbarSize; i++) if (bot.SlotAt(i).Id == Item.KnockStick1) bot.HeldSlot.Value = (byte)i;
                yield return new WaitForSeconds(0.7f);
                int h0 = NetGame.HypeCount, u0 = PlayerNet.Unsticks;
                var start = me.transform.position;
                bot.MeleeRpc(true, me.NetworkObject, me.transform.position + Vector3.up * 1.1f, false);
                yield return new WaitForSeconds(0.3f);
                Check(bot.Count(Item.KnockStick1) == 0, $"the bot hit us with the one-use knockback stick, into the corner ({bot.HeldItem})");
                if (Cfg.ClipFarm) Check(NetGame.HypeCount > h0 && NetGame.LastHype == "YEET", $"Clip Farm: YEET ({NetGame.LastHype})");
                float far = 0f;
                for (float t = 0; t < 5f; t += 0.1f) { far = Mathf.Max(far, Flat(me.transform.position, start)); yield return new WaitForSeconds(0.1f); }
                var end = me.transform.position;
                float edge = Cfg.MapHalf - 14f;
                Check(far > 25f, $"we went flying ({far:0} m)");
                Check(Mathf.Abs(end.x) < edge && Mathf.Abs(end.z) < edge && PlayerNet.Unsticks == u0, $"we bounced off the corner and landed in the open - not up the edge or in the corner ({end.x:0}, {end.z:0}; the edge starts at {edge:0})");
                yield return Snap("prompt12_knock_landed");
                // walk out: we can move (not wedged)
                var p0 = me.transform.position;
                Binds.TestHold(Bind.Forward, true);
                yield return new WaitForSeconds(1f);
                Binds.TestHold(Bind.Forward, false);
                Check(Flat(me.transform.position, p0) > 2f, $"...and we can walk away ({Flat(me.transform.position, p0):0.0} m in a second)");
            }

            // ---- respawn in the wild: pick the spot - never near an enemy base or an airdrop
            {
                if (bot.Dead.Value) bot.ServerRespawn(false);
                bot.TeleportRpc(OnGround(Cfg.BaseCenter[enemy], 0.1f), 0f);
                var nearBase = Cfg.BaseCenter[enemy] + (Vector3.zero - Cfg.BaseCenter[enemy]).normalized * (Cfg.BaseHalf + 10f);
                Check(!Cfg.WildSpotOk(team, nearBase, out var why1) && why1 == "an enemy base", $"10 m from the enemy base: no ({why1})");
                Check(Cfg.WildSpotOk(enemy, nearBase), "...(but fine for them: it's their own)");
                var mid = OnGround(Cfg.BaseCenter[team] * 0.4f + side * 30f, 0f);
                Check(Cfg.WildSpotOk(team, mid), "open ground away from everything: yes");
                g.DevSpawnAirdrop();
                yield return new WaitForSeconds(0.5f);
                var drops = new List<Vector3>();
                NetGame.AirdropSpots(drops);
                Check(drops.Count > 0, $"an airdrop's on its way ({drops.Count})");
                var nearDrop = drops.Count > 0 ? drops[0] + Vector3.right * 12f : Vector3.zero;
                string why2 = null;
                bool noGo = drops.Count > 0 && !Cfg.WildSpotOk(team, nearDrop, out why2);
                Check(noGo && why2 == "an airdrop", $"12 m from it: no ({why2})");
                me.ServerKill(null);
                for (float t = 0; t < Cfg.RespawnTime + 4f && !me.ChoosingRespawn; t += 0.25f) yield return new WaitForSeconds(0.25f);
                Hud.TestWildPick(true);
                yield return new WaitForSeconds(1f);
                Check(me.ChoosingRespawn && Time.time - Hud.WildPickerShownAt < 0.5f && Hud.WildNoGoCells > 50, $"dead: the map to pick the spot, with the no-go zones in red ({Hud.WildNoGoCells} cells)");
                yield return Snap("prompt12_wild_picker");
                me.RespawnAtRpc(nearBase);
                yield return new WaitForSeconds(0.8f);
                var p = me.transform.position;
                Check(!me.Dead.Value && Cfg.WildSpotOk(team, p) && Cfg.BaseGap(enemy, p) >= Cfg.WildBaseGap - 0.5f, $"picking right by the enemy base: you come back clear of it ({Cfg.BaseGap(enemy, p):0} m from it)");
                if (drops.Count > 0)
                {
                    me.ServerKill(null);
                    for (float t = 0; t < Cfg.RespawnTime + 4f && !me.ChoosingRespawn; t += 0.25f) yield return new WaitForSeconds(0.25f);
                    me.RespawnAtRpc(drops[0]);
                    yield return new WaitForSeconds(0.8f);
                    p = me.transform.position;
                    Check(!me.Dead.Value && Flat(p, drops[0]) >= Cfg.WildDropGap - 0.5f, $"picking right on the airdrop: you come back {Flat(p, drops[0]):0} m from it");
                }
                if (Cfg.ClipFarm)
                {
                    int wacky = 0;
                    foreach (var st in me.Inv) if (!st.Empty && System.Array.IndexOf(Cfg.AirdropChoices, st.Id) >= 0) wacky++;
                    Check(wacky >= 1, $"Clip Farm: you respawn with an airdrop item ({wacky})");
                }
                Hud.TestWildPick(false);
            }

            // ---- Clip Farm: the hype
            if (Cfg.ClipFarm)
            {
                PlayerNet.ServerAddBot(enemy);
                yield return new WaitForSeconds(0.8f);
                var bots = new List<PlayerNet>();
                foreach (var b in PlayerNet.All) if (b != null && b.Bot.Value) { bots.Add(b); foreach (var mb in b.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false; }
                foreach (var b in bots) if (b.Dead.Value) b.ServerRespawn(false);
                yield return new WaitForSeconds(0.5f);
                var here = OnGround(Cfg.BaseCenter[team] * 0.5f + side * 20f, 0.1f);
                pc.LocalTeleport(here, Quaternion.LookRotation(side).eulerAngles.y);
                for (int i = 0; i < bots.Count; i++) bots[i].TeleportRpc(OnGround(here + side * (4f + i * 2f), 0.1f), 0f);
                yield return new WaitForSeconds(0.6f);
                int m0 = SteamClips.Asked;
                bots[0].ServerKill(me);
                yield return new WaitForSeconds(0.3f);
                bots[0].ServerRespawn(false); // (the one enemy, killed twice inside the window)
                yield return new WaitForSeconds(0.3f);
                bots[0].ServerKill(me);
                yield return new WaitForSeconds(0.4f);
                Check(NetGame.LastHype == "DOUBLE KILL" && Hud.BigFadeShown == "DOUBLE KILL" && (Hud.BigFadeSubShown ?? "").Length > 0, $"two kills in a row: DOUBLE KILL up on the screen ({Hud.BigFadeShown} / {Hud.BigFadeSubShown})");
                Check(SteamClips.Asked > m0, $"...marked on Steam's game recording timeline ({SteamClips.Asked - m0} markers)");
                yield return Snap("prompt12_double_kill");
                yield return new WaitForSeconds(Cfg.ClipFarmMultiWindow + 0.5f);
                bots[0].ServerRespawn(false);
                yield return new WaitForSeconds(0.4f);
                bots[0].TeleportRpc(OnGround(here + side * 55f, 0.1f), 0f);
                yield return new WaitForSeconds(0.5f);
                bots[0].ServerKill(me);
                yield return new WaitForSeconds(0.4f);
                Check(NetGame.LastHype == "LONG SHOT" && NetGame.LastHypeSub.Contains(" m"), $"a kill from 55 m: LONG SHOT ({NetGame.LastHype}: {NetGame.LastHypeSub})");
                yield return Snap("prompt12_long_shot");
                // airdrops every 45 s (the clock running again: a paused one holds them back)
                g.TimerPaused.Value = false;
                double next = g.NextDropLands.Value;
                for (float t = 0; t < 50f && (g.NextDropLands.Value <= 0 || g.NextDropLands.Value == next); t += 0.5f) yield return new WaitForSeconds(0.5f);
                Check(g.NextDropLands.Value > 0 && g.NextDropLands.Value != next, "another airdrop comes along inside 45 s");
            }

            Log("prompt12 done");
            Application.Quit(0);
        }

        /// <summary>-autotest prompt12menu -shotdir DIR: the main menu's mode page with Clip Farm picked (and the mode list).</summary>
        IEnumerator Prompt12MenuShots()
        {
            Hud.TestNewMenu = true;
            yield return new WaitForSeconds(2.5f);
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.ClipFarm);
            Hud.TestNewPage(6);
            Hud.TestModesOpen(true);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("prompt12_menu_modes");
            Hud.TestModesOpen(false);
            yield return new WaitForSeconds(0.4f);
            Check(Cfg.RulesOf(Bootstrap.MapChoice) == GameRules.ClipFarm, $"Clip Farm picked ({Cfg.RulesOf(Bootstrap.MapChoice)})");
            yield return Snap("prompt12_menu_clipfarm");
            Log("prompt12menu done");
            Application.Quit(0);
        }

        /// <summary>How much wood is in a team's wood machine pile right now.</summary>
        static int WoodPileCount(NetGame g, int team)
        {
            int n = 0;
            var at = Cfg.WoodTrayPos(team);
            foreach (var it in g.Items) if (it.Stack.Id == Item.Wood && Flat(it.Pos, at) < 3f) n += it.Stack.Count;
            return n;
        }
    }
}
