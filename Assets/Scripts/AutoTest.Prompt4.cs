using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt4 -host -solo -rules autowood -shotdir DIR: the spear and hammer icons from their pictures, the
        /// hammer at 5 wood, the barricade 30% bigger, the head hitbox round the real head, no shadow while invisible, the
        /// spear / sword / hammer in your hands - and every wacky airdrop item: given by the dev menu (the rest on the
        /// ground), then each one used on an enemy bot (or on us) and checked.
        /// </summary>
        static List<Gadget> GadgetList(NetGame g)
        {
            var l = new List<Gadget>();
            foreach (var gd in g.Gadgets) l.Add(gd);
            return l;
        }

        IEnumerator Prompt4Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            double Now() => g.NetworkManager.ServerTime.Time;

            // ---- the fixes
            var spearIcon = ItemIcons.Get(Item.Spear);
            var hammerIcon = ItemIcons.Get(Item.Hammer);
            Check(spearIcon != null && spearIcon.name == "spear" && hammerIcon != null && hammerIcon.name == "hammer", $"the spear and hammer icons are the pictures ({(spearIcon != null ? spearIcon.name : "none")}, {(hammerIcon != null ? hammerIcon.name : "none")})");
            Check(Cfg.HammerWood == 5 && Cfg.CraftRecipe(Cfg.CraftIndexOf(Item.Hammer), team).Wood == 5, $"the hammer costs 5 wood ({Cfg.HammerWood})");
            Check(Mathf.Abs(Deployables.BarricadeH - 1.45f * 1.3f) < 0.01f && Mathf.Abs(Deployables.BarricadeLen - 2.4f * 1.3f) < 0.01f, $"the barricade is 30% taller and wider ({Deployables.BarricadeLen} x {Deployables.BarricadeH})");
            // invisible: your own shadow goes too
            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(5f);
            me.InvisUntil.Value = Now() + 3.0;
            yield return new WaitForSeconds(0.3f);
            Check(!me.BodyShownForTests, "invisible: your own body (only ever drawn as your shadow) is hidden - no shadow");
            me.InvisUntil.Value = -1;
            yield return new WaitForSeconds(0.3f);
            Check(me.BodyShownForTests, "...and it's back after");
            // the hands: the spear's poke and the sword's slash meet the crosshair, the hammer just taps
            {
                me.ServerGive(Item.Spear, 1); me.ServerGive(Item.Sword, 1); me.ServerGive(Item.Hammer, 1);
                pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                foreach (var (it, e, name) in new[] { (Item.Spear, ViewModel.ImpactTime, "prompt4_spear_poke"), (Item.Sword, 0.3f, "prompt4_sword_slash"), (Item.Hammer, 0.14f, "prompt4_hammer_tap") })
                {
                    yield return Hold(me, it);
                    pc.DebugSwing(it == Item.Sword ? Cfg.SwordSwingTime : 0.9f);
                    ViewModel.DebugSwingE = e;
                    yield return Snap(name);
                    ViewModel.DebugSwingE = -1f;
                }
            }

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
            if (bot.Dead.Value) bot.ServerRespawn(false);
            Vector3 botAt = OnGround(spot + side * 4.5f, 0.1f);
            IEnumerator Ready()
            {
                if (me.Dead.Value) { me.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                if (bot.Dead.Value) { bot.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                pc.LocalTeleport(spot, Quaternion.LookRotation(side).eulerAngles.y);
                bot.TeleportRpc(botAt, Quaternion.LookRotation(-side).eulerAngles.y);
                me.Health.Value = bot.Health.Value = Cfg.MaxHealth;
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                yield return new WaitForSeconds(0.4f);
                pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 5f);
            }
            IEnumerator Use(Item it, Vector3 point, PlayerNet target = null, int arg = 0)
            {
                me.ServerGive(it, 1, Mathf.Clamp(Cfg.MaxData(it), 0, 255));
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.45f);
                var dir = point - me.EyePos;
                me.UseWackyRpc(it, me.EyePos, dir.sqrMagnitude > 0.01f ? dir.normalized : me.transform.forward, point, target != null, target != null ? new NetworkObjectReference(target.NetworkObject) : default, arg);
                yield return new WaitForSeconds(0.35f);
            }
            var botMid = new System.Func<Vector3>(() => bot.transform.position + Vector3.up * 1.1f);

            // ---- the head hitbox: the head is a hit, the shoulder next to it and above it aren't
            yield return Ready();
            yield return new WaitForSeconds(0.5f);
            {
                var head = bot.HeadCenter;
                var right = bot.transform.right;
                Check(bot.IsHeadshot(head), $"a hit in the middle of the head is a headshot ({head - bot.transform.position})");
                Check(!bot.IsHeadshot(bot.transform.position + Vector3.up * 1.2f + right * 0.42f), "a hit on the shoulder isn't");
                Check(!bot.IsHeadshot(head + right * 0.45f), "nor one beside the head");
                Check(!bot.IsHeadshot(head + Vector3.up * 0.6f), "nor one over it");
                Check(!bot.IsHeadshot(bot.transform.position + Vector3.up * 1.0f), "nor one in the chest");
            }

            // ---- the 10 second shield: nothing hurts, not even a kill
            yield return Ready();
            yield return Use(Item.ShieldPotion, Vector3.zero);
            Check(me.Shielded, "the shield is up");
            me.ServerDamage(60f, bot);
            me.ServerKill(bot);
            yield return new WaitForSeconds(0.2f);
            Check(!me.Dead.Value && me.Health.Value >= Cfg.MaxHealth, $"shielded: a hit and a kill do nothing ({me.Health.Value:0} HP)");
            me.ShieldUntil.Value = -1;

            // ---- the golden rock: saves you from death once
            yield return Ready();
            me.ServerGive(Item.GoldenRock, 1);
            yield return Hold(me, Item.GoldenRock);
            me.ServerKill(bot);
            yield return new WaitForSeconds(0.3f);
            Check(!me.Dead.Value && me.Health.Value >= Cfg.MaxHealth && me.Count(Item.GoldenRock) == 0 && me.Glittering, $"the golden rock: a kill just uses it up, back to full health, glittering ({me.Health.Value:0} HP)");

            // ---- the timeout ball: locked in, can't be hurt, smoke when it's over
            yield return Ready();
            me.ServerGive(Item.TimeoutBall, 1);
            yield return Hold(me, Item.TimeoutBall);
            yield return new WaitForSeconds(0.6f);
            me.ThrowTimeoutRpc(me.EyePos, (botMid() - me.EyePos).normalized * 17f);
            yield return new WaitForSeconds(0.2f);
            me.TimeoutHitRpc(bot.NetworkObject);
            yield return new WaitForSeconds(0.3f);
            Check(bot.Prisoned, "the timeout ball locks them in");
            bot.ServerDamage(50f, me);
            Check(bot.Health.Value >= Cfg.MaxHealth, "nobody can hurt them in it");
            yield return Snap("prompt4_timeout_ball");
            bot.PrisonUntil.Value = Now() + 0.3;
            yield return new WaitForSeconds(1f);
            bool smoke = false;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Smoke) smoke = true;
            Check(!bot.Prisoned && smoke, "when it's over they come out in a cloud of smoke");

            // ---- the ant gun
            yield return Ready();
            yield return Use(Item.AntGun, botMid(), bot);
            Check(bot.IsAnt, "the ant gun turns them into an ant");
            bot.ServerDamage(50f, me);
            Check(bot.Health.Value >= Cfg.MaxHealth, "an ant can't be hurt");
            yield return Snap("prompt4_ant");
            bot.AntUntil.Value = -1;

            // ---- the slowness gun
            yield return Ready();
            yield return Use(Item.SlowGun, bot.transform.position + Vector3.up * 0.5f);
            Check(bot.Slowed && Mathf.Approximately(bot.WackySpeedMul, Cfg.SlowMul), $"the slowness gun's goo slows them (x{bot.WackySpeedMul})");
            bot.SlowUntil.Value = -1;

            // ---- the dance bomb (close enough: us too)
            yield return Ready();
            g.ServerWackyLanded(Item.DanceBomb, bot.transform.position, me);
            yield return new WaitForSeconds(0.6f);
            Check(bot.Dancing && me.Dancing, "the dance bomb: everyone in range dances (us too - we were close)");
            yield return Snap("prompt4_dance");
            yield return new WaitForSeconds(Cfg.DanceTime);
            Check(!bot.Dancing && !me.Dancing, "...for a few seconds");

            // ---- the one-use knockback stick: no damage, and it breaks
            yield return Ready();
            me.ServerGive(Item.KnockStick1, 1);
            yield return Hold(me, Item.KnockStick1);
            yield return new WaitForSeconds(0.7f);
            me.MeleeRpc(true, bot.NetworkObject, botMid(), false);
            yield return new WaitForSeconds(0.4f);
            Check(bot.Health.Value >= Cfg.MaxHealth && me.Count(Item.KnockStick1) == 0, $"the knockback stick does no damage and breaks after one hit ({bot.Health.Value:0} HP)");

            // ---- the blue shell: knocked flat, half their things spill out, no damage
            yield return Ready();
            for (int i = 0; i < bot.Inv.Count; i++) bot.Inv[i] = default;
            bot.ServerGive(Item.Wood, 10);
            yield return Use(Item.BlueShell, Vector3.zero);
            yield return new WaitForSeconds(1.4f);
            yield return Snap("prompt4_blue_shell");
            float until = Time.time + 8f;
            while (!bot.KnockedDown && Time.time < until) yield return null;
            Check(bot.KnockedDown && bot.Count(Item.Wood) == 5 && bot.Health.Value >= Cfg.MaxHealth, $"the blue shell knocks them flat: half their wood spills ({bot.Count(Item.Wood)} left), no damage");
            yield return new WaitForSeconds(Cfg.KnockDownTime);

            // ---- bear juice: a bear's hide takes the hits (thrown now: AutoTest.Prompt5.cs)
            yield return Ready();
            me.ServerMakeBear();
            yield return new WaitForSeconds(0.3f);
            Check(me.IsBear, "bear juice: you're a bear");
            me.ServerDamage(100f, bot);
            yield return new WaitForSeconds(0.2f);
            Check(me.Health.Value >= Cfg.MaxHealth && Mathf.Approximately(me.BearHpLeft.Value, Cfg.BearHp - 100f), $"a bear's health takes the hit ({me.BearHpLeft.Value:0} bear HP)");
            yield return new WaitForSeconds(0.8f);
            yield return Snap("prompt4_bear");
            me.BearUntil.Value = Now() + 0.2;
            yield return new WaitForSeconds(0.6f);
            Check(!me.IsBear, "its time up, you're back");

            // ---- the inventory swapper
            yield return Ready();
            for (int i = 0; i < bot.Inv.Count; i++) bot.Inv[i] = default;
            bot.ServerGive(Item.Crossbow, 1);
            me.ServerGive(Item.Sword, 1);
            yield return Use(Item.InvSwapper, Vector3.zero);
            Check(me.Count(Item.Crossbow) == 1 && bot.Count(Item.Sword) == 1, "the inventory swapper swaps two players' bags");

            // ---- Connect 4: everything stops, the loser dies
            yield return Ready();
            yield return Use(Item.Connect4Wand, botMid(), bot);
            Check(g.MinigameOn && g.Mini.Value.Kind == MiniGame.Connect4 && g.TimerPaused.Value, "the Connect 4 wand starts a game (the clock stopped)");
            yield return new WaitForSeconds(0.5f);
            yield return Snap("prompt4_connect4");
            until = Time.time + 90f;
            while (g.MinigameOn && g.Mini.Value.Phase == 0 && Time.time < until)
            {
                var m = g.Mini.Value;
                if (m.Turn == 0) { int col = 3; for (int c = 0; c < MiniGame.Cols && m.Height(col) >= MiniGame.Rows; c++) col = c; me.MiniMoveRpc(col); }
                yield return new WaitForSeconds(0.4f);
            }
            Check(g.Mini.Value.Phase == 1, $"the game ended (why {g.Mini.Value.Why})");
            yield return Snap("prompt4_connect4_result");
            until = Time.time + 6f;
            while (g.MinigameOn && Time.time < until) yield return null;
            Check(!g.MinigameOn && (me.Dead.Value || bot.Dead.Value), $"...and the loser died (us {me.Dead.Value}, the bot {bot.Dead.Value})");

            // ---- the gameshow: they answer, someone dies
            yield return Ready();
            yield return Use(Item.GameshowWand, botMid(), bot);
            Check(g.MinigameOn && g.Mini.Value.Kind == MiniGame.Gameshow, "the gameshow wand starts the gameshow");
            yield return new WaitForSeconds(0.6f);
            yield return Snap("prompt4_gameshow");
            until = Time.time + 12f;
            while (g.MinigameOn && Time.time < until) yield return null;
            Check(!g.MinigameOn && (me.Dead.Value || bot.Dead.Value), $"they answered - and someone died (us {me.Dead.Value}, the bot {bot.Dead.Value})");

            // ---- the disguise kit: team games only
            yield return Ready();
            yield return Use(Item.DisguiseKit, Vector3.zero);
            Check(!me.Disguised && me.Count(Item.DisguiseKit) == 1, "in a 1v1 the disguise kit does nothing (team games only)");

            // ---- the bouncy castle: it bounces you
            yield return Ready();
            var castleAt = OnGround(spot - side * 9f, 0f);
            me.ServerCastleLanded(g, castleAt); // (thrown now: AutoTest.Prompt5.cs)
            bool castle = false;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Castle) castle = true;
            Check(castle, "a bouncy castle went up");
            yield return new WaitForSeconds(0.8f);
            pc.LocalTeleport(spot + side * 3f, Quaternion.LookRotation(-side).eulerAngles.y);
            yield return Snap("prompt4_bouncy_castle");
            int b0 = pc.CastleBounces;
            pc.LocalTeleport(castleAt + Vector3.up * 1.2f, 0f);
            until = Time.time + 3f;
            while (pc.CastleBounces == b0 && Time.time < until) yield return null;
            Check(pc.CastleBounces > b0, "landing on it bounces you up");

            // ---- the attention snail: every camera's on it
            yield return Ready();
            yield return Use(Item.AttentionSnail, OnGround(spot + side * 2f, 0f));
            yield return new WaitForSeconds(0.4f);
            Check(PlayerController.SnailShot(out _, out _), "the attention snail: the camera's on the snail");
            yield return Snap("prompt4_attention_snail");
            foreach (var gd in new List<Gadget>(GadgetList(g))) if (gd.Kind == Gadget.Snail) g.ServerRemoveGadget(gd.Id, false); // (the next shots without it)

            // ---- the clutter bomb
            yield return Ready();
            g.ServerWackyLanded(Item.ClutterBomb, spot + side * 3f, me);
            yield return new WaitForSeconds(0.6f);
            yield return Snap("prompt4_clutter");
            foreach (var gd in GadgetList(g)) if (gd.Kind == Gadget.Clutter || gd.Kind == Gadget.Smoke) g.ServerRemoveGadget(gd.Id, false); // (the next shots without it)

            // ---- the Stick of Truth: the ground and a building there bent out of shape
            yield return Ready();
            var truthAt = OnGround(spot - side * 10f + back * 6f, 0f);
            var bent = SpawnKeylessPiece(PieceType.Wall, team, truthAt + side * 1.5f, Quaternion.identity);
            yield return new WaitForSeconds(0.3f);
            yield return Use(Item.StickOfTruth, truthAt);
            bool truth = false;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Truth) truth = true;
            yield return new WaitForSeconds(1.5f);
            Check(!truth && bent != null && bent.IsSpawned && NetGame.LiftedBy(bent.NetworkObject) > 2f, "the Lifting Stick (was the Stick of Truth) lifts the building it's shot at - no bent ground");
            pc.SetLook(Quaternion.LookRotation(truthAt - spot).eulerAngles.y, 10f);
            yield return Snap("prompt4_stick_of_truth");

            // ---- the instant base rebuild
            yield return Ready();
            var rebuildAt = OnGround(Cfg.BaseCenter[team] - side * 5f, 0f);
            var gone = SpawnKeylessPiece(PieceType.Wall, team, rebuildAt, Quaternion.identity);
            yield return new WaitForSeconds(0.3f);
            gone.ServerDamage(gone.Health.Value + 1f);
            yield return new WaitForSeconds(0.3f);
            yield return Use(Item.BaseRebuild, Vector3.zero);
            bool back1 = false;
            foreach (var s in Structure.All) if (s != null && s.IsSpawned && Vector3.Distance(s.transform.position, rebuildAt) < 0.2f) back1 = true;
            Check(back1, "the instant base rebuild brings back what was just destroyed");

            // ---- the ball grabber
            yield return Ready();
            if (Ball.Instance != null)
            {
                // (somewhere with nothing between us and it: the grabber can't pull through things)
                var ballAt = spot + side * 14f + Vector3.up * 1.5f;
                for (int k = 0; k < 16; k++)
                {
                    var tryAt = OnGround(spot + Quaternion.Euler(0, k * 22.5f, 0) * side * 12f, 0.7f);
                    if (!Physics.Linecast(me.EyePos, tryAt, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) { ballAt = tryAt + Vector3.up * 0.8f; break; }
                }
                Ball.Instance.ServerDrop(ballAt, Vector3.zero);
                yield return new WaitForSeconds(2.5f);
                // (once it's settled: stand somewhere that sees it)
                var bp = Ball.Instance.transform.position;
                for (int k = 0; k < 16; k++)
                {
                    var stand = OnGround(bp + Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward * 10f, 0.1f);
                    var eye = stand + Vector3.up * Cfg.EyeHeight;
                    bool blocked = false;
                    foreach (var h in Physics.RaycastAll(eye, (bp - eye).normalized, Vector3.Distance(eye, bp), ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                        if (h.collider.GetComponentInParent<Ball>() == null && h.collider.GetComponentInParent<PlayerNet>() == null && h.collider.GetComponentInParent<Vehicle>() == null) blocked = true;
                    if (blocked) continue;
                    pc.LocalTeleport(stand, Quaternion.LookRotation(bp - stand).eulerAngles.y);
                    break;
                }
                yield return new WaitForSeconds(0.4f);
                yield return Use(Item.BallGrabber, Ball.Instance.transform.position);
                until = Time.time + 6f;
                while (!me.CarryingBall && Time.time < until) yield return null;
                Check(me.CarryingBall && me.Count(Item.BallGrabber) == 0, "the Ball Grabber 3000 pulls the ball into your hands, then breaks");
                if (me.CarryingBall) Ball.Instance.ServerDrop(me.transform.position + Vector3.up, Vector3.zero);
            }

            // ---- Omen's map: a teleport
            yield return Ready();
            var omenTo = OnGround(spot + side * 25f - back * 10f, 0f);
            yield return Use(Item.OmenMap, omenTo);
            yield return new WaitForSeconds(0.5f);
            Check(Flat(me.transform.position, omenTo) < 2f, $"Omen's map sends you where you clicked ({Flat(me.transform.position, omenTo):0.0} m off)");

            // ---- the scribble notebook
            yield return Ready();
            int horses0 = 0;
            foreach (var v in Vehicle.All) if (v != null && v.IsHorse) horses0++;
            me.ServerGive(Item.Scribble, 1);
            yield return Hold(me, Item.Scribble);
            me.ScribbleRpc(new Unity.Collections.FixedString64Bytes("horse"), spot + side * 3f);
            yield return new WaitForSeconds(0.6f);
            int horses1 = 0;
            foreach (var v in Vehicle.All) if (v != null && v.IsHorse) horses1++;
            Check(horses1 == horses0 + 1, "writing \"horse\" makes a horse");
            me.ServerGive(Item.Scribble, 1);
            yield return Hold(me, Item.Scribble);
            me.ScribbleRpc(new Unity.Collections.FixedString64Bytes("xylophone"), spot + side * 3f);
            yield return new WaitForSeconds(0.6f);
            bool word = false;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Word && gd.Text.ToString() == "XYLOPHONE") word = true;
            Check(word, "anything else becomes a block with the word on it");
            yield return Snap("prompt4_scribble_word");

            // ---- the drawn monster: comes alive and goes after the enemy
            yield return Ready();
            {
                var px = new byte[MonsterArt.Bytes];
                for (int y = 10; y < 54; y++) for (int x = 16; x < 48; x++) MonsterArt.Set(px, x, y, (x / 4 + y / 4) % 2 == 0 ? 2 : 1);
                me.ServerGive(Item.DrawMonster, 1);
                yield return Hold(me, Item.DrawMonster);
                me.MonsterDrawingRpc(px);
                yield return new WaitForSeconds(0.5f);
                Vehicle monster = null;
                foreach (var v in Vehicle.All) if (v != null && v.IsMonster) monster = v;
                Check(monster != null, "the drawing comes alive");
                yield return Snap("prompt4_monster");
                until = Time.time + 10f;
                while (monster != null && monster.IsSpawned && Time.time < until) yield return null;
                Check(monster == null || !monster.IsSpawned, "it found the enemy and splatted itself on their screen");
                Hud.MonsterSplat(System.Convert.ToBase64String(px));
                yield return new WaitForSeconds(1.2f);
                yield return Snap("prompt4_monster_splat");
            }

            // ---- the decoy: holds what you held, gone when it's hit
            yield return Ready();
            yield return Use(Item.Decoy, Vector3.zero, null, (int)Item.Sword);
            Vehicle decoy = null;
            foreach (var v in Vehicle.All) if (v != null && v.IsDecoy) decoy = v;
            Check(decoy != null && decoy.WackyItem.Value == (byte)Item.Sword, "a decoy of you, holding the sword");
            yield return new WaitForSeconds(0.8f);
            yield return Snap("prompt4_decoy");
            if (decoy != null) decoy.ServerDamage(5f, bot);
            yield return new WaitForSeconds(0.3f);
            Check(decoy == null || !decoy.IsSpawned, "one hit and it's gone");

            // ---- the turret bot: 5 HP left near it
            yield return Ready();
            yield return Use(Item.DroneBot, Vector3.zero);
            yield return new WaitForSeconds(0.4f);
            Check(me.Droning, "the turret bot's down and we're driving it");
            yield return Snap("prompt4_turret_bot_cam");
            Vehicle drone = null;
            foreach (var v in Vehicle.All) if (v != null && v.IsDrone) drone = v;
            if (drone != null) { bot.TeleportRpc(drone.transform.position + side * 1.5f, 0f); yield return new WaitForSeconds(0.4f); }
            me.DroneBlastRpc();
            yield return new WaitForSeconds(0.4f);
            Check(!me.Droning && !bot.Dead.Value && Mathf.Approximately(bot.Health.Value, 5f), $"blowing it up leaves them on 5 HP ({bot.Health.Value:0})");

            // ---- the gravity staff: falling up
            yield return Ready();
            me.ServerGive(Item.GravityStaff, 1);
            yield return Hold(me, Item.GravityStaff);
            float y0 = me.transform.position.y;
            me.GravFlip.Value = true;
            yield return new WaitForSeconds(1.2f);
            Check(me.transform.position.y > y0 + 3f, $"flipped gravity: you fall up ({me.transform.position.y - y0:0.0} m)");
            yield return Snap("prompt4_gravity_flipped");
            me.GravFlip.Value = false;
            yield return new WaitForSeconds(2f);

            // ---- the double jump boots
            yield return Ready();
            me.ServerGive(Item.JumpBoots, 1);
            yield return Hold(me, Item.JumpBoots);
            yield return new WaitForSeconds(0.6f);
            me.UseItemRpc();
            yield return new WaitForSeconds(0.4f);
            Check(me.WornBoots.Value && me.Count(Item.JumpBoots) == 0, "the jump boots go on when you click with them");
            int dj0 = pc.DoubleJumps;
            Binds.TestPress(Bind.Jump);
            yield return new WaitForSeconds(0.25f);
            Binds.TestPress(Bind.Jump);
            yield return new WaitForSeconds(0.4f);
            Check(pc.DoubleJumps > dj0, "jump, then jump again in the air");

            // ---- the tree breaker: a whole tree in one hit, 300 wood
            yield return Ready();
            ResourceNode tree = null;
            float bd = float.MaxValue;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Kind.Value != ResourceNode.Tree || n.Amount.Value <= 0) continue;
                float d = Vector3.Distance(n.transform.position, spot);
                if (d < bd) { bd = d; tree = n; }
            }
            if (tree != null)
            {
                pc.LocalTeleport(OnGround(tree.transform.position - side * 1.6f), Quaternion.LookRotation(side).eulerAngles.y);
                me.ServerGive(Item.TreeBreaker, 1, Cfg.TreeBreakerUses);
                yield return Hold(me, Item.TreeBreaker);
                yield return new WaitForSeconds(1f);
                int w0 = me.Count(Item.Wood);
                me.MeleeRpc(true, tree.NetworkObject, tree.transform.position + Vector3.up * 1f, false);
                yield return new WaitForSeconds(0.5f);
                Check(me.Count(Item.Wood) == w0 + Cfg.TreeBreakerWood && tree.Amount.Value <= 0, $"the tree breaker fells a tree in one hit for {Cfg.TreeBreakerWood} wood (+{me.Count(Item.Wood) - w0})");
            }

            // ---- the fake airdrop flare: it looks real, holds everything, gives nothing
            yield return Ready();
            yield return Use(Item.FakeFlare, Vector3.zero);
            yield return new WaitForSeconds(0.4f);
            Check(Hud.LastBanner.Contains("AIRDROP INCOMING"), $"the fake flare calls in AIRDROP INCOMING like a real one ({Hud.LastBanner})");
            until = Time.time + NetGame.DropLand + 8f;
            Container fake = null;
            while (fake == null && Time.time < until)
            {
                foreach (var c in Container.All) if (c != null && c.IsSpawned && c.IsFakeDrop) fake = c;
                yield return null;
            }
            Check(fake != null && fake.Slots.Count > 50, $"its crate lands, full of every item ({(fake != null ? fake.Slots.Count : 0)})");
            if (fake != null)
            {
                pc.LocalTeleport(fake.transform.position + side * 1.6f, Quaternion.LookRotation(-side).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                int had = 0; for (int i = 0; i < me.Inv.Count; i++) if (!me.Inv[i].Empty) had++;
                me.MoveItemRpc(1, 0, 0, 255, fake.Slots[0].Count, fake.NetworkObject);
                yield return new WaitForSeconds(0.4f);
                int has = 0; for (int i = 0; i < me.Inv.Count; i++) if (!me.Inv[i].Empty) has++;
                Check(has == had && !fake.Slots[0].Empty && !Hud.LastWackyNote.ToUpperInvariant().Contains("FAKE"), $"nothing comes out of it - and nothing says it's fake ({Hud.LastWackyNote})");
            }

            // ---- the suicide bomb: us and everyone round us
            yield return Ready();
            bot.TeleportRpc(OnGround(spot + side * 2.5f), 0f);
            yield return new WaitForSeconds(0.3f);
            yield return Use(Item.SuicideBomb, Vector3.zero);
            yield return new WaitForSeconds(Cfg.SuicideFuse + 0.6f);
            Check(me.Dead.Value && bot.Dead.Value, "the suicide bomb kills us and whoever's close");
            yield return new WaitForSeconds(1f);

            // ---- the Sigil of CHAOS: every item rains down all over the map
            yield return Ready();
            me.ServerGive(Item.Wood, 200);
            int items0 = g.Items.Count;
            yield return Use(Item.ChaosSigil, Vector3.zero);
            int left = 0; for (int i = 0; i < me.Inv.Count; i++) if (!me.Inv[i].Empty) left++;
            Check(left == 0 && g.Items.Count > items0, $"the Sigil of CHAOS empties every bag onto the map ({g.Items.Count - items0} more things lying about)");

            // ---- the dev menu: every airdrop item (what doesn't fit lands at your feet)
            yield return Ready();
            int onFloor0 = g.Items.Count;
            me.DevRpc(DevCmd.GiveOpItems);
            yield return new WaitForSeconds(0.6f);
            int missing = 0;
            foreach (var it in Cfg.AirdropChoices)
            {
                if (Cfg.Removed(it)) continue; // (the decoy base: taken out)
                bool there = me.Count(it) > 0 || (it == Item.JumpBoots && me.WornBoots.Value) || (it == Item.Jetpack && me.WornJetFuel.Value > 0) || (it == Item.Wallhack && me.WornGlasses.Value); // (worn gear goes straight on)
                if (!there) foreach (var d in g.Items) if (d.Stack.Id == it && Flat(d.Pos, me.transform.position) < 8f) there = true;
                if (!there) { missing++; Log($"(not given: {it})"); }
            }
            Check(missing == 0 && g.Items.Count > onFloor0, $"All airdrop items: every one given - the bag full, the rest on the ground round you ({missing} missing, {g.Items.Count - onFloor0} on the ground)");
            pc.SetLook(pc.LookYaw, 60f);
            yield return Snap("prompt4_all_airdrop_items");
            Log("prompt4 done");
            Application.Quit(0);
        }
    }
}
