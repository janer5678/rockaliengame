using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt6 -host -solo -rules airdroptest -shotdir DIR: walking about while dancing, the scribble
        /// notebook's big word list (what gamers write), the disguise kit only in team games, the paint bucket, hitting
        /// during the attention snail (your own view in a corner window), triple jump boots, the Sigil of CHAOS gone, the
        /// Rock of Immortality only in your hand, no bounce crouched / sliding on the bouncy castle, and Airdrop Test's list
        /// starting over (ALL AIRDROP ITEMS TESTING) once you've had every airdrop item.
        /// </summary>
        IEnumerator Prompt6Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            double Now() => g.NetworkManager.ServerTime.Time;

            // ---- the names and lists
            Check(Cfg.ItemName(Item.JumpBoots) == "Triple Jump Boots" && Cfg.BootsAirJumps == 2, $"triple jump boots ({Cfg.ItemName(Item.JumpBoots)}, {Cfg.BootsAirJumps} jumps in the air)");
            Check(Cfg.Removed(Item.ChaosSigil) && !Cfg.InAirdropPool(Item.ChaosSigil) && !Cfg.AirdropLoot.Contains(Item.ChaosSigil) && !Cfg.AllFunItems.Contains(Item.ChaosSigil), "the Sigil of CHAOS is out of the game (airdrops, every item list)");
            Check(!Cfg.TeamGame && !Cfg.InAirdropPool(Item.DisguiseKit) && !Cfg.AirdropLoot.Contains(Item.DisguiseKit) && !Cfg.AllFunItems.Contains(Item.DisguiseKit), "a 1v1: the disguise kit doesn't come in airdrops (team games only)");
            Check(g.ScribbleWordCount >= 600, $"the scribble notebook knows lots of words ({g.ScribbleWordCount})");

            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(4f);

            // ---- an enemy bot to try things on, out in the open on our side
            var spot = OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 14f) + side * 12f, 0.1f);
            pc.LocalTeleport(spot, Quaternion.LookRotation(side).eulerAngles.y);
            PlayerNet.ServerAddBot(enemy);
            yield return new WaitForSeconds(0.8f);
            PlayerNet bot = null;
            foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
            Check(bot != null, "an enemy bot");
            if (bot == null) { Application.Quit(0); yield break; }
            foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
            Vector3 botAt = OnGround(spot + side * 4.5f, 0.1f);
            IEnumerator Ready()
            {
                Binds.TestReleaseAll();
                if (me.Dead.Value) { me.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                if (bot.Dead.Value) { bot.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                me.ShieldUntil.Value = me.DanceUntil.Value = me.KnockUntil.Value = me.SlowUntil.Value = -1;
                bot.ShieldUntil.Value = bot.DanceUntil.Value = bot.KnockUntil.Value = bot.SlowUntil.Value = bot.AntUntil.Value = bot.PrisonUntil.Value = -1;
                pc.LocalTeleport(spot, Quaternion.LookRotation(side).eulerAngles.y);
                bot.TeleportRpc(botAt, Quaternion.LookRotation(-side).eulerAngles.y);
                me.Health.Value = bot.Health.Value = Cfg.MaxHealth;
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                yield return new WaitForSeconds(0.4f);
                pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 5f);
            }

            // ---- the dance bomb: you can walk about while you dance (no attacking)
            yield return Ready();
            g.ServerDanceAt(me.transform.position, bot);
            yield return new WaitForSeconds(0.3f);
            var d0 = me.transform.position;
            Binds.TestHold(Bind.Forward, true);
            yield return new WaitForSeconds(1f);
            Binds.TestHold(Bind.Forward, false);
            float walked = Flat(me.transform.position, d0);
            Check(me.Dancing && walked > 2f && !me.WackyFrozen && me.WackyNoAttack, $"dancing you can still walk ({walked:0.0} m in a second) - but not attack");
            yield return Snap("prompt6_dance_walk");

            // ---- the scribble notebook: what gamers write
            yield return Ready();
            var aim = OnGround(spot + side * 2.5f, 0f);
            int Wood() => me.Count(Cfg.GatherItem(Item.Wood));
            IEnumerator Write(string word) { g.ServerScribble(me, word, aim); yield return new WaitForSeconds(0.25f); }
            yield return Write("ak47");
            Check(me.Count(Item.Revolver) == 1 && me.Count(Item.RevolverAmmo) > 0, "\"ak47\": a gun (and bullets)");
            int w0 = Wood();
            yield return Write("free robux");
            Check(Wood() >= w0 + 1000, "\"free robux\": 1000 wood");
            yield return Write("god mode");
            Check(me.Shielded, "\"god mode\": shield up");
            me.ShieldUntil.Value = -1;
            yield return Write("aimbot");
            Check(me.Count(Item.Wallhack) == 1, "\"aimbot\": wallhack glasses");
            yield return Write("diamond sword");
            Check(me.Count(Item.Sword) == 1, "\"diamond sword\": a sword (the longest match wins - not money)");
            yield return Write("pizza");
            Check(me.Count(Item.Meat) > 0, "\"pizza\": food");
            yield return Write("elephant");
            Check(!bot.IsAnt, "\"elephant\" isn't an ant (whole words)");
            yield return Write("noob");
            Check(bot.KnockedDown, "\"noob\": the enemy takes the L (knocked flat)");
            bot.KnockUntil.Value = -1;
            for (int i = 0; i < bot.Inv.Count; i++) bot.Inv[i] = default;
            bot.ServerGive(Item.Crossbow, 1, 1);
            yield return Write("fanum tax");
            Check(me.Count(Item.Crossbow) == 1 && bot.Count(Item.Crossbow) == 0, "\"fanum tax\": you steal from the nearest enemy");
            int cars0 = 0; foreach (var v in Vehicle.All) if (v != null && v.Kind.Value == Vehicle.Car) cars0++;
            yield return Write("lambo");
            int cars1 = 0; foreach (var v in Vehicle.All) if (v != null && v.Kind.Value == Vehicle.Car) cars1++;
            Check(cars1 > cars0, "\"lambo\": a car");
            yield return Write("rizz");
            Check(bot.Dancing, "\"rizz\": the enemy dances");
            yield return Write("xqzzyplonk");
            bool word = false; foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Word) word = true;
            Check(word, "anything else is still a big block with the word on it");
            foreach (var gd in new List<Gadget>(GadgetList(g))) g.ServerRemoveGadget(gd.Id, false);
            foreach (var v in new List<Vehicle>(Vehicle.All)) if (v != null && v.IsSpawned && v.Kind.Value == Vehicle.Car) v.NetworkObject.Despawn(true);

            // ---- the Rock of Immortality: in your hand you can't die; in your bag it does nothing
            yield return Ready();
            me.ServerGive(Item.GoldenRock, 1);
            yield return Hold(me, Item.GoldenRock);
            me.ServerKill(bot);
            me.ServerDamage(500f, bot);
            yield return new WaitForSeconds(0.3f);
            Check(!me.Dead.Value && me.Count(Item.GoldenRock) == 1 && me.Health.Value <= 1.5f, $"holding the Rock of Immortality: killing hits leave you on 1 HP and it's never used up ({me.Health.Value:0} HP)");
            me.ServerGive(Item.Spear, 1);
            yield return Hold(me, Item.Spear);
            me.Health.Value = Cfg.MaxHealth;
            me.ServerKill(bot);
            yield return new WaitForSeconds(0.3f);
            Check(me.Dead.Value, "with it in your bag (holding something else) you die like anyone");

            // ---- triple jump boots: two jumps in the air, not three
            yield return Ready();
            me.WornBoots.Value = true;
            yield return new WaitForSeconds(0.3f);
            int j0 = pc.DoubleJumps;
            for (int i = 0; i < 4; i++) { Binds.TestPress(Bind.Jump); yield return new WaitForSeconds(0.22f); }
            yield return new WaitForSeconds(0.6f);
            Check(pc.DoubleJumps - j0 == 2, $"triple jump boots: jump, then twice more in the air (a 4th does nothing: {pc.DoubleJumps - j0} air jumps)");
            me.WornBoots.Value = false;

            // ---- the bouncy castle: crouched you don't bounce
            yield return Ready();
            var castleAt = OnGround(spot - side * 5f, 0f);
            g.ServerAddGadget(Gadget.Castle, castleAt, 0f, team, Cfg.CastleLife, 11);
            yield return new WaitForSeconds(0.8f);
            int b0 = pc.CastleBounces;
            Binds.TestHold(Bind.Crouch, true);
            yield return new WaitForSeconds(0.2f);
            pc.LocalTeleport(castleAt + Vector3.up * 1.2f, 0f);
            yield return new WaitForSeconds(2f);
            Check(pc.CastleBounces == b0, $"crouched on the bouncy castle you don't bounce ({pc.CastleBounces - b0} bounces)");
            Binds.TestHold(Bind.Crouch, false);
            float until = Time.time + 3f;
            while (pc.CastleBounces == b0 && Time.time < until) yield return null;
            Check(pc.CastleBounces > b0, "stand up and it bounces you again");
            foreach (var gd in new List<Gadget>(GadgetList(g))) g.ServerRemoveGadget(gd.Id, false);
            yield return new WaitForSeconds(0.3f);

            // ---- the attention snail: your own view stays in a corner window, and you can still hit
            yield return Ready();
            pc.LocalTeleport(OnGround(botAt - side * 1.6f, 0.1f), Quaternion.LookRotation(side).eulerAngles.y);
            yield return new WaitForSeconds(0.3f);
            g.ServerAddGadget(Gadget.Snail, OnGround(spot - back * 8f, 0f), 0f, team, Cfg.SnailTime, 7);
            yield return new WaitForSeconds(0.6f);
            {
                var to = bot.transform.position + Vector3.up * 1.1f - me.EyePos;
                pc.SetLook(Quaternion.LookRotation(Vector3.ProjectOnPlane(to, Vector3.up)).eulerAngles.y, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            }
            yield return new WaitForSeconds(0.3f);
            Check(PlayerController.SnailShot(out _, out _) && PlayerController.PipOn, "the attention snail: your own view is up in a corner window");
            yield return Hold(me, Item.Rock);
            yield return new WaitForSeconds(1f);
            float hp0 = bot.Health.Value;
            Binds.TestPress(Bind.Attack);
            yield return new WaitForSeconds(0.8f);
            Check(bot.Health.Value < hp0, $"...and you can still hit (the bot {hp0:0} -> {bot.Health.Value:0} HP)");
            yield return Snap("prompt6_snail_corner_view");
            foreach (var gd in new List<Gadget>(GadgetList(g))) if (gd.Kind == Gadget.Snail) g.ServerRemoveGadget(gd.Id, false);
            yield return new WaitForSeconds(0.4f);
            Check(!PlayerController.PipOn, "...and it goes when the snail does");

            // ---- the paint bucket
            yield return Ready();
            me.ServerGive(Item.DrawMonster, 1);
            yield return Hold(me, Item.DrawMonster);
            yield return new WaitForSeconds(0.5f);
            Binds.TestPress(Bind.Attack);
            yield return new WaitForSeconds(0.4f);
            Check(pc.DrawOpen, "the draw-a-monster canvas is open");
            for (int i = 16; i <= 48; i++) { Hud.TestDab(i, 16, 1, 0); Hud.TestDab(i, 48, 1, 0); Hud.TestDab(16, i, 1, 0); Hud.TestDab(48, i, 1, 0); }
            Hud.BucketOn = true;
            Hud.TestBucket(32, 32, 2);
            yield return new WaitForSeconds(0.3f);
            Check(Hud.CanvasPixel(32, 32) == 2 && Hud.CanvasPixel(20, 40) == 2 && Hud.CanvasPixel(16, 30) == 1 && Hud.CanvasPixel(5, 5) == 0, "the paint bucket fills the shape inside the lines (and nothing outside)");
            yield return Snap("prompt6_paint_bucket");
            Hud.BucketOn = false;
            pc.CloseWackyUi();
            yield return new WaitForSeconds(0.3f);

            // ---- AIRDROP TEST: once you've had every airdrop item, the list starts over
            yield return Ready();
            var bushes = new List<ResourceNode>();
            foreach (var n in ResourceNode.All) if (n != null && n.IsSpawned && n.IsAirdropBush) bushes.Add(n);
            Check(bushes.Count >= 2, $"airdrop berry bushes ({bushes.Count})");
            var had = g.BushItemsHad(me);
            had.Clear();
            Item last = Item.None;
            foreach (var it in Cfg.AirdropLoot) if (it != Item.Wood && it != Item.Stone) { last = it; break; }
            foreach (var it in Cfg.AirdropChoices) if (Cfg.InAirdropPool(it) && it != last) had.Add(it);
            foreach (var it in Cfg.AirdropLoot) if (it != last) had.Add(it);
            int resets0 = g.AllAirdropResets;
            for (int k = 0; k < 2 && k < bushes.Count; k++)
            {
                var b = bushes[k];
                pc.LocalTeleport(OnGround(b.transform.position - side * 1.2f, 0.1f), Quaternion.LookRotation(side).eulerAngles.y);
                yield return new WaitForSeconds(0.3f);
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.PickBerriesRpc(b.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                if (k == 0)
                {
                    Check(me.Count(last) == 1, $"the last airdrop item you hadn't had: {Cfg.ItemName(last)}");
                    Check(g.AllAirdropResets == resets0 + 1 && g.BushItemsHad(me).Count == 0, "...that's every one: the list starts over");
                    Check(Hud.BigFadeShown == "ALL AIRDROP ITEMS TESTING", $"...and it says ALL AIRDROP ITEMS TESTING ({Hud.BigFadeShown})");
                    pc.SetLook(pc.LookYaw, 5f);
                    yield return Snap("prompt6_all_airdrop_items_testing");
                    yield return new WaitForSeconds(5f);
                    Check(Hud.BigFadeShown == null, "...and the words fade away");
                }
                else
                {
                    int any = 0; for (int i = 0; i < me.Inv.Count; i++) if (!me.Inv[i].Empty && me.Inv[i].Id != Item.Berry) any++;
                    Check(any >= 1 && g.BushItemsHad(me).Count == 1, "the next bush gives any airdrop item again");
                }
            }

            Log("prompt6 done");
            Application.Quit(0);
        }
    }
}
