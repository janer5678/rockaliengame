using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt5 -host -solo -rules airdroptest -shotdir DIR: the airdrop item changes - nothing goes on by
        /// itself (the jetpack, glasses and jump boots wait in the bag until you click), the Lifting Stick lifts what it's
        /// shot at (a wall floats up and drops back, a player goes sky high), the bouncy castle is thrown and pink, the
        /// Rock of Immortality, the tiny attention snail with everyone in its shot, the thicker clutter bomb, the slower
        /// low-jumping ant, the ball grabber's instant snap and doubled range, the tree breaker x3, the thrown bear juice
        /// (2 minutes, no turning back, half the speed, a swipe that rears up), the Sigil of CHAOS spreading things evenly
        /// and making them glow through walls, the decoy base gone - and AIRDROP TEST mode's airdrop berry bushes (a
        /// different airdrop item every pick).
        /// </summary>
        IEnumerator Prompt5Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            double Now() => g.NetworkManager.ServerTime.Time;

            // ---- the numbers and names
            Check(Cfg.ItemName(Item.GoldenRock) == "Rock of Immortality", $"the golden rock is the Rock of Immortality ({Cfg.ItemName(Item.GoldenRock)})");
            Check(Cfg.ItemName(Item.StickOfTruth) == "Lifting Stick", $"the Stick of Truth is the Lifting Stick ({Cfg.ItemName(Item.StickOfTruth)})");
            Check(!Cfg.InAirdropPool(Item.DecoyBase) && !Cfg.AirdropLoot.Contains(Item.DecoyBase) && !Cfg.AllFunItems.Contains(Item.DecoyBase), "the decoy base is gone from airdrops and every item list");
            Check(Mathf.Approximately(Cfg.Melee(Item.TreeBreaker).PlayerDamage, 45f) && Mathf.Approximately(Cfg.Melee(Item.TreeBreaker).StructureDamage, 45f), $"the tree breaker hits three times as hard ({Cfg.Melee(Item.TreeBreaker).PlayerDamage})");
            Check(Mathf.Approximately(Cfg.BallGrabRange, 120f), $"the ball grabber reaches twice as far ({Cfg.BallGrabRange} m)");
            Check(Cfg.AntGunShots == 2 && Cfg.MaxData(Item.AntGun) == 2, "the ant gun has 2 shots");
            Check(Mathf.Approximately(Cfg.BearSpeedMul, 0.375f) && Mathf.Approximately(Cfg.BearTime, 120f), $"a bear is half as fast as it was (x{Cfg.BearSpeedMul}) and lasts 2 minutes");
            Check(Cfg.WackyThrown(Item.BouncyCastle) && Cfg.WackyThrown(Item.BearJuice), "the bouncy castle and bear juice are thrown");
            Check(Cfg.AirdropTest && Cfg.RulesName(Cfg.Rules) == "Airdrop Test", $"Airdrop Test mode ({Cfg.RulesName(Cfg.Rules)})");

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
                if (me.Dead.Value) { me.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                if (bot.Dead.Value) { bot.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                pc.LocalTeleport(spot, Quaternion.LookRotation(side).eulerAngles.y);
                bot.TeleportRpc(botAt, Quaternion.LookRotation(-side).eulerAngles.y);
                me.Health.Value = bot.Health.Value = Cfg.MaxHealth;
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                yield return new WaitForSeconds(0.4f);
                pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 5f);
            }
            var botMid = new System.Func<Vector3>(() => bot.transform.position + Vector3.up * 1.1f);

            // ---- nothing from an airdrop goes on by itself: the jetpack, the glasses, the jump boots wait for a click
            yield return Ready();
            me.ServerGive(Item.Jetpack, 1, Cfg.JetpackFuel);
            me.ServerGive(Item.Wallhack, 1);
            me.ServerGive(Item.JumpBoots, 1);
            yield return new WaitForSeconds(1f);
            Check(me.Count(Item.Jetpack) == 1 && me.Count(Item.Wallhack) == 1 && me.Count(Item.JumpBoots) == 1 && !me.WearingJetpack && !me.WornGlasses.Value && !me.WornBoots.Value,
                "a jetpack, wallhack glasses and jump boots in the bag stay in the bag (nothing goes on by itself)");
            foreach (var it in new[] { Item.Jetpack, Item.Wallhack, Item.JumpBoots })
            {
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.6f);
                me.UseItemRpc();
                yield return new WaitForSeconds(0.4f);
            }
            Check(me.WearingJetpack && me.WornGlasses.Value && me.WornBoots.Value && me.Count(Item.Jetpack) == 0 && me.Count(Item.Wallhack) == 0 && me.Count(Item.JumpBoots) == 0,
                "held and clicked, each one goes on");
            me.WornJetFuel.Value = -1; me.WornGlasses.Value = false; me.WornBoots.Value = false;

            // ---- the lifting stick: a wall floats up, hangs there, and drops back into its place
            yield return Ready();
            var wallAt = OnGround(spot - side * 6f + back * 3f, 0f);
            var wall = SpawnKeylessPiece(PieceType.Wall, team, wallAt, Quaternion.LookRotation(side));
            yield return new WaitForSeconds(0.4f);
            Vector3 wallWas = wall.transform.position;
            me.ServerGive(Item.StickOfTruth, 1, Cfg.StickOfTruthShots);
            yield return Hold(me, Item.StickOfTruth);
            yield return new WaitForSeconds(0.45f);
            var aim = wall.transform.position + Vector3.up * 1.2f;
            me.UseWackyRpc(Item.StickOfTruth, me.EyePos, (aim - me.EyePos).normalized, aim, true, new NetworkObjectReference(wall.NetworkObject), 0);
            yield return new WaitForSeconds(1.6f);
            float up = NetGame.LiftedBy(wall.NetworkObject);
            bool truth = false;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Truth) truth = true;
            Check(up > NetGame.LiftTop(wallWas) * 0.7f && !truth && wall.Health.Value >= wall.MaxHp - 0.5f, $"the lifting stick lifts the wall it's shot at ({up:0.0} m up), no ground bent, no damage");
            pc.SetLook(Quaternion.LookRotation(wallWas - spot).eulerAngles.y, -18f);
            yield return Snap("prompt5_lifted_wall");
            yield return new WaitForSeconds(Cfg.LiftTime + 1.6f);
            Check(wall.IsSpawned && Vector3.Distance(wall.transform.position, wallWas) < 0.05f && NetGame.LiftedBy(wall.NetworkObject) == 0f, "...and it drops back into its place");
            // a player goes sky high (us: the stick shot at ourselves)
            float y0 = me.transform.position.y;
            me.UseWackyRpc(Item.StickOfTruth, me.EyePos, Vector3.down, me.transform.position, true, new NetworkObjectReference(me.NetworkObject), 0);
            float topY = y0;
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { topY = Mathf.Max(topY, me.transform.position.y); yield return null; }
            Check(topY > y0 + 6f && me.Count(Item.StickOfTruth) == 0, $"a player it's shot at is lifted sky high ({topY - y0:0.0} m) - and after 2 shots it's used up");
            yield return new WaitForSeconds(6f);

            // ---- the bouncy castle: thrown like a grenade, pops up where it lands, pink
            yield return Ready();
            var castleAt = OnGround(spot - side * 9f, 0f);
            me.ServerGive(Item.BouncyCastle, 1);
            yield return Hold(me, Item.BouncyCastle);
            yield return new WaitForSeconds(0.6f);
            me.ThrowItemRpc(Item.BouncyCastle, me.EyePos, (castleAt - me.EyePos).normalized * 16f);
            yield return new WaitForSeconds(0.2f);
            me.ThrownLandRpc(Item.BouncyCastle, castleAt, Vector3.up);
            yield return new WaitForSeconds(0.9f);
            int castleId = -1;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Castle) castleId = gd.Id;
            var castleGo = GadgetFx.Find(castleId);
            bool pink = false;
            if (castleGo != null) foreach (var mr in castleGo.GetComponentsInChildren<MeshRenderer>()) { var c = mr.sharedMaterial.color; if (c.r > 0.85f && c.g < 0.5f && c.b > 0.35f) pink = true; }
            Check(castleId > 0 && me.Count(Item.BouncyCastle) == 0 && Flat(g.Gadgets[g.Gadgets.Count - 1].Pos, castleAt) < 1.5f, "the thrown bouncy castle pops up where it landed");
            Check(pink, "it's pink");
            pc.LocalTeleport(castleAt + side * 9f + back * 4f + Vector3.up * 4f, Quaternion.LookRotation(-side).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(castleAt - (castleAt + side * 9f + back * 4f)).eulerAngles.y, 14f);
            yield return new WaitForSeconds(0.15f);
            yield return Snap("prompt5_pink_castle");

            // ---- the attention snail: tiny, and everyone's in its shot (you too)
            yield return Ready();
            bot.TeleportRpc(OnGround(spot + side * 9f + back * 3f, 0.1f), 0f);
            pc.LocalTeleport(OnGround(spot + side * 6f - back * 3f, 0.1f), 0f);
            yield return new WaitForSeconds(0.3f);
            g.ServerAddGadget(Gadget.Snail, OnGround(spot, 0f), 0f, team, Cfg.SnailTime, 7);
            yield return new WaitForSeconds(0.6f);
            int snailId = -1;
            foreach (var gd in g.Gadgets) if (gd.Kind == Gadget.Snail) snailId = gd.Id;
            var snailGo = GadgetFx.Find(snailId);
            float snailScale = snailGo != null && snailGo.transform.childCount > 0 ? snailGo.transform.GetChild(0).localScale.x : 99f;
            bool shot = PlayerController.SnailShot(out var sPos, out var sRot, out float sFov);
            bool botIn = false, meIn = false;
            var cam = Camera.main;
            if (cam != null)
            {
                var vb = cam.WorldToViewportPoint(bot.transform.position + Vector3.up);
                var vm = cam.WorldToViewportPoint(me.transform.position + Vector3.up);
                botIn = vb.z > 0 && vb.x > 0 && vb.x < 1 && vb.y > 0 && vb.y < 1;
                meIn = vm.z > 0 && vm.x > 0 && vm.x < 1 && vm.y > 0 && vm.y < 1;
            }
            Check(snailScale < 1f, $"the attention snail is tiny (x{snailScale})");
            Check(shot && botIn && meIn && me.SeeSelfForTests, $"its shot has every player in the background - the bot and you, your body shown (fov {sFov:0})");
            yield return Snap("prompt5_attention_snail");
            foreach (var gd in new List<Gadget>(GadgetList(g))) if (gd.Kind == Gadget.Snail || gd.Kind == Gadget.Castle) g.ServerRemoveGadget(gd.Id, false);
            yield return new WaitForSeconds(0.3f);

            // ---- the clutter bomb: thick from outside, and inside you can hardly see
            yield return Ready();
            var clutterAt = OnGround(spot + side * 3f, 0f);
            bot.TeleportRpc(OnGround(clutterAt + back * 1.5f, 0.1f), 0f);
            g.ServerWackyLanded(Item.ClutterBomb, clutterAt, me);
            yield return new WaitForSeconds(0.7f);
            pc.LocalTeleport(OnGround(spot - side * 10f, 0.1f), Quaternion.LookRotation(side).eulerAngles.y);
            pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 3f);
            yield return new WaitForSeconds(0.3f);
            yield return Snap("prompt5_clutter_outside");
            pc.LocalTeleport(OnGround(clutterAt - side * 1f, 0.1f), Quaternion.LookRotation(side).eulerAngles.y);
            yield return new WaitForSeconds(0.3f);
            float inside = Camera.main != null ? GadgetFx.ClutterAt(g, Camera.main.transform.position) : 0f;
            Check(inside > 0.8f, $"standing in the clutter, your screen is full of it ({inside:0.00})");
            yield return Snap("prompt5_clutter_inside");
            foreach (var gd in GadgetList(g)) if (gd.Kind == Gadget.Clutter) g.ServerRemoveGadget(gd.Id, false);

            // ---- the ant: much slower, a tiny jump
            yield return Ready();
            bot.AntUntil.Value = Now() + 5.0;
            Check(Mathf.Approximately(bot.WackySpeedMul, Cfg.AntSpeedMul) && Cfg.AntSpeedMul <= 0.3f && Cfg.AntJumpMul <= 0.35f, $"an ant is way slower (x{bot.WackySpeedMul}) and jumps way lower (x{Cfg.AntJumpMul})");
            bot.AntUntil.Value = -1;

            // ---- bear juice: thrown on the enemy - a bear for 2 minutes, no turning back
            yield return Ready();
            me.ServerGive(Item.BearJuice, 1);
            yield return Hold(me, Item.BearJuice);
            yield return new WaitForSeconds(0.6f);
            me.ThrowItemRpc(Item.BearJuice, me.EyePos, (botMid() - me.EyePos).normalized * 16f);
            yield return new WaitForSeconds(0.2f);
            me.ThrownLandRpc(Item.BearJuice, botMid(), Vector3.up);
            yield return new WaitForSeconds(0.5f);
            Check(bot.IsBear && !me.IsBear && Mathf.Abs((float)(bot.BearUntil.Value - Now()) - Cfg.BearTime) < 3f, $"bear juice thrown on the enemy makes them a bear for 2 minutes ({bot.BearUntil.Value - Now():0} s), not us");
            Check(Mathf.Approximately(bot.WackySpeedMul, Cfg.BearSpeedMul), $"a bear moves at x{bot.WackySpeedMul}");
            // its swipe: it rears up and a paw comes high
            {
                var watch = OnGround(botAt + back * 5.5f - side * 1.2f, 0.1f); // (side on: it faces -side)
                var yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(botAt - watch, Vector3.up)).eulerAngles.y;
                pc.LocalTeleport(watch, yaw);
                yield return new WaitForSeconds(0.3f);
                var to = bot.transform.position + Vector3.up * 1.1f - me.EyePos;
                pc.SetLook(yaw, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            }
            yield return new WaitForSeconds(0.5f);
            yield return Snap("prompt5_bear_stand");
            PlayerNet.DebugBearSwipe = 0.33f;
            yield return Snap("prompt5_bear_swipe_up");
            PlayerNet.DebugBearSwipe = 0.52f;
            yield return Snap("prompt5_bear_swipe_rake");
            PlayerNet.DebugBearSwipe = -1f;
            // ...and on ourselves (thrown at our feet)
            yield return Ready();
            me.ServerGive(Item.BearJuice, 1);
            yield return Hold(me, Item.BearJuice);
            yield return new WaitForSeconds(0.6f);
            me.ThrowItemRpc(Item.BearJuice, me.EyePos, Vector3.down * 5f);
            yield return new WaitForSeconds(0.2f);
            me.ThrownLandRpc(Item.BearJuice, me.transform.position, Vector3.up);
            yield return new WaitForSeconds(0.4f);
            Check(me.IsBear, "thrown at your own feet, you're the bear");
            Binds.TestPress(Bind.Interact);
            yield return new WaitForSeconds(0.4f);
            Check(me.IsBear, "no X / key turns you back");
            me.BearUntil.Value = Now() + 0.3;
            bot.BearUntil.Value = Now() + 0.3;
            yield return new WaitForSeconds(0.8f);
            Check(!me.IsBear && !bot.IsBear, "when the time's up, you're an alien again");

            // ---- the ball grabber: the ball snaps straight into your hands, and it breaks
            yield return Ready();
            if (Ball.Instance != null)
            {
                var ballAt = spot + side * 14f + Vector3.up * 1.5f;
                for (int k = 0; k < 16; k++)
                {
                    var tryAt = OnGround(spot + Quaternion.Euler(0, k * 22.5f, 0) * side * 12f, 0.7f);
                    if (!Physics.Linecast(me.EyePos, tryAt, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) { ballAt = tryAt + Vector3.up * 0.8f; break; }
                }
                Ball.Instance.ServerDrop(ballAt, Vector3.zero);
                yield return new WaitForSeconds(2.5f);
                var bp = Ball.Instance.transform.position;
                for (int k = 0; k < 24; k++)
                {
                    // (somewhere 25-30 m off that really sees it from where our eyes end up)
                    var stand = OnGround(bp + Quaternion.Euler(0, k * 15f, 0) * Vector3.forward * (k < 12 ? 30f : 25f), 0.1f);
                    pc.LocalTeleport(stand, Quaternion.LookRotation(bp - stand).eulerAngles.y);
                    yield return new WaitForSeconds(0.3f);
                    var eye = me.EyePos;
                    bp = Ball.Instance.transform.position;
                    bool blocked = false;
                    foreach (var h in Physics.RaycastAll(eye, (bp - eye).normalized, Vector3.Distance(eye, bp), ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore))
                        if (h.collider.GetComponentInParent<Ball>() == null && h.collider.GetComponentInParent<PlayerNet>() == null && h.collider.GetComponentInParent<Vehicle>() == null) blocked = true;
                    if (!blocked) break;
                }
                yield return new WaitForSeconds(0.4f);
                me.ServerGive(Item.BallGrabber, 1);
                yield return Hold(me, Item.BallGrabber);
                yield return new WaitForSeconds(0.45f);
                float d = Vector3.Distance(me.EyePos, Ball.Instance.transform.position);
                var bpos = Ball.Instance.transform.position;
                me.UseWackyRpc(Item.BallGrabber, me.EyePos, (bpos - me.EyePos).normalized, bpos, false, default, 0);
                yield return new WaitForSeconds(0.15f);
                Check(me.CarryingBall && me.Count(Item.BallGrabber) == 0, $"the Ball Grabber 3000 snaps the ball into your hands at once from {d:0} m, then breaks");
                if (me.CarryingBall) Ball.Instance.ServerDrop(me.transform.position + Vector3.up, Vector3.zero);
            }

            // (the Sigil of CHAOS: taken out of the game - AutoTest.Prompt6.cs)

            // ---- AIRDROP TEST: airdrop berry bushes all over, a different airdrop item every pick
            yield return Ready();
            var bushes = new List<ResourceNode>();
            foreach (var n in ResourceNode.All) if (n != null && n.IsSpawned && n.IsAirdropBush) bushes.Add(n);
            Check(bushes.Count >= Cfg.AirdropBushesPerSide * Cfg.Copies - 2, $"airdrop berry bushes all over the map ({bushes.Count})");
            var got = new List<Item>();
            for (int k = 0; k < 3 && k < bushes.Count; k++)
            {
                var b = bushes[k];
                if (k == 0)
                {
                    pc.LocalTeleport(OnGround(b.transform.position - side * 3.5f, 0.1f), Quaternion.LookRotation(side).eulerAngles.y);
                    pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 8f);
                    yield return new WaitForSeconds(0.6f);
                    yield return Snap("prompt5_airdrop_bush");
                }
                pc.LocalTeleport(OnGround(b.transform.position - side * 1.2f, 0.1f), Quaternion.LookRotation(side).eulerAngles.y);
                yield return new WaitForSeconds(0.3f);
                var had = new List<ItemStack>();
                for (int i = 0; i < me.Inv.Count; i++) had.Add(me.Inv[i]);
                me.PickBerriesRpc(b.NetworkObject);
                yield return new WaitForSeconds(0.4f);
                for (int i = 0; i < me.Inv.Count; i++) if (!me.Inv[i].Empty && (i >= had.Count || !me.Inv[i].Equals(had[i])) && me.Inv[i].Id != Item.Berry) got.Add(me.Inv[i].Id);
                Check(!b.IsSpawned, $"pick #{k + 1}: the bush is picked");
            }
            var distinct = new HashSet<Item>(got);
            bool allAirdrop = true;
            foreach (var it in got) if (System.Array.IndexOf(Cfg.AirdropChoices, it) < 0) allAirdrop = false;
            Check(got.Count == Mathf.Min(3, bushes.Count) && distinct.Count == got.Count && allAirdrop, $"each bush gave an airdrop item, all different ({string.Join(", ", got)})");
            Check(g.BushItemsHad(me).Count == got.Count && g.BushItemsHad(bot).Count == 0, "they're remembered for us only (the bot can still get them)");

            Log("prompt5 done");
            Application.Quit(0);
        }
    }
}
