using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt9 -host -solo [-rules jonahbruh] -shotdir DIR: the new prices (bow, arrows, spear, chest, ram),
        /// the hatchet / horse / shotgun buffs, invisible attackers (only what's in their hand shows), shooting out through
        /// your own window's bars, the suicide cooldown on screen, barricades (not on someone, not on nothing, breaking with
        /// what's under them), picking up a trade station / chest by holding E, the spear flying dead straight; and in Jonah
        /// Test Mode Bruh: 250 a stone wall, a 500 crossbow, the better hatchet after the hammer, doorways as strong as walls
        /// with no door raiding, walls and doors all round the one-cell base and jumping out over them from the machine,
        /// C4 taking one layer of stone, the ram's cracks all over the stone (none on the wood).
        /// </summary>
        IEnumerator Prompt9Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value, enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
            var back = Cfg.BackDir(team);
            var side = Vector3.Cross(Vector3.up, back);
            int Price(Item it) { int i = Cfg.RecipeIndex(it); return i < 0 ? -1 : Cfg.GetRecipe(i).Wood + Cfg.GetRecipe(i).Stone; }

            // ---- the numbers
            {
                int ai = Cfg.RecipeIndex(Item.Arrow);
                Check(Price(Item.Bow) == 250 && Price(Item.Arrow) == 100 && ai >= 0 && Cfg.GetRecipe(ai).Count == 5, $"the bow costs {Price(Item.Bow)}, arrows {Price(Item.Arrow)} for {(ai >= 0 ? Cfg.GetRecipe(ai).Count : 0)}");
                Check(Price(Item.Spear) == 125 && Price(Item.Chest) == 125 && Price(Item.Ram) == 200, $"the spear {Price(Item.Spear)}, the storage chest {Price(Item.Chest)}, a battering ram {Price(Item.Ram)}");
                Check(Cfg.HatchetPlayerDamage > 14f && Cfg.HorseSprint > 11f && Cfg.HorseWalk > 4.5f && Cfg.ShotgunRange > 25f,
                    $"the hatchet hits harder ({Cfg.HatchetPlayerDamage}, was 14), the horse is faster ({Cfg.HorseWalk}/{Cfg.HorseSprint}, was 4.5/11), the shotgun reaches further ({Cfg.ShotgunRange} m, was 25)");
                if (Cfg.JonahBruh)
                {
                    var t1 = new List<int>();
                    Cfg.AddTier(t1, 1);
                    int h = t1.IndexOf(Cfg.CraftIndexOf(Item.Hammer)), bh = t1.IndexOf(Cfg.CraftIndexOf(Item.BetterHatchet));
                    Check(h < 0 && bh == 0, $"the better hatchet is first on the Trade Station, where the hammer was (hammer {h}, better hatchet {bh})");
                    Check(Cfg.HammerCost(0) == 250, $"a stone wall upgrade costs {Cfg.HammerCost(0)}");
                    Check(Price(Item.Crossbow) == 500, $"the crossbow costs {Price(Item.Crossbow)}");
                    Check(Mathf.Approximately(Cfg.PieceHp(PieceType.Doorway, 1), Cfg.PieceHp(PieceType.Wall, 1)) && Mathf.Approximately(Cfg.PieceHp(PieceType.Doorway, 0), Cfg.PieceHp(PieceType.Wall, 0)),
                        $"a doorway takes as much breaking as a wall ({Cfg.PieceHp(PieceType.Doorway, 1)} / {Cfg.PieceHp(PieceType.Wall, 1)})");
                }
                else Check(Cfg.HammerCost(0) != 250 && Price(Item.Crossbow) != 500 && Cfg.PieceHp(PieceType.Doorway, 1) < Cfg.PieceHp(PieceType.Wall, 1), "(outside Jonah Test Mode Bruh: the usual stone price, crossbow and doorway)");
            }

            // ---- hold E to pick up your trade station and an empty chest
            {
                foreach (var kind in new[] { Item.Workbench, Item.Chest })
                {
                    var at = OnGround(Cfg.BaseCenter[team] - back * 6f + side * (kind == Item.Chest ? 3f : -3f), 0f);
                    at.y = Mathf.Max(at.y, 0f);
                    Container box = kind == Item.Chest ? SpawnChest(team, at, new List<ItemStack>()) : Workbench.ServerSpawn(team, at, Workbench.DefaultYaw(team), 1);
                    yield return new WaitForSeconds(0.4f);
                    for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                    var stand = at - back * 2f; stand.y = at.y + 0.1f;
                    pc.LocalTeleport(stand, Quaternion.LookRotation(back).eulerAngles.y);
                    yield return new WaitForSeconds(0.4f);
                    var lk = Quaternion.LookRotation(box.Center - me.EyePos).eulerAngles;
                    pc.SetLook(lk.y, lk.x > 180f ? lk.x - 360f : lk.x);
                    yield return new WaitForSeconds(0.3f);
                    int before = PlayerNet.PackedUp;
                    Binds.TestHold(Bind.Interact, true);
                    yield return new WaitForSeconds(Cfg.PackUpDelay + Cfg.PackUpHoldTime + 0.5f);
                    Binds.TestHold(Bind.Interact, false);
                    yield return new WaitForSeconds(0.4f);
                    Check(PlayerNet.PackedUp == before + 1 && !box.IsSpawned && me.Count(kind) == 1, $"hold E on your {(kind == Item.Chest ? "empty chest" : "trade station")}: it's picked up ({me.Count(kind)} in the bag)");
                    for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                }
            }

            // ---- the spear: thrown while running and jumping it flies dead straight along the crosshair
            {
                var start = OnGround(Vector3.Lerp(Cfg.BaseCenter[team], Vector3.zero, 0.5f) + side * 10f, 0.1f);
                pc.LocalTeleport(start, Quaternion.LookRotation(-back).eulerAngles.y);
                me.ServerGive(Item.Spear, 1);
                yield return Hold(me, Item.Spear);
                yield return new WaitForSeconds(0.5f);
                pc.SetLook(Quaternion.LookRotation(-back + side * 0.3f).eulerAngles.y, -4f);
                Binds.TestHold(Bind.Forward, true);
                Binds.TestHold(Bind.Right, true);
                yield return new WaitForSeconds(0.5f);
                Binds.TestPress(Bind.Jump);
                yield return new WaitForSeconds(0.25f);
                var ray = pc.CenterRay();
                Binds.TestHold(Bind.Aim, true);
                yield return new WaitForSeconds(0.12f);
                Binds.TestPress(Bind.Attack);
                yield return new WaitForSeconds(0.25f);
                Binds.TestHold(Bind.Aim, false);
                Binds.TestHold(Bind.Forward, false);
                Binds.TestHold(Bind.Right, false);
                ArrowProjectile sp = null;
                for (float t = 0; t < 1f && sp == null; t += Time.deltaTime) { foreach (var a in ArrowProjectile.Flying) if (a != null && a.IsSpearForTests) sp = a; yield return null; }
                var ray2 = PlayerController.LastSpearRay;
                float worst = 0f;
                for (float t = 0; t < 0.8f && sp != null; t += Time.deltaTime)
                {
                    var p = sp.transform.position;
                    worst = Mathf.Max(worst, Vector3.Cross(ray2.direction, p - ray2.origin).magnitude);
                    yield return null;
                }
                Check(sp != null && worst < 0.15f && Vector3.Angle(ray.direction, ray2.direction) < 10f, $"a spear thrown running and jumping flies dead straight along the crosshair (at most {worst:0.00} m off the line)");
                yield return Snap("prompt9_spear_straight");
                yield return new WaitForSeconds(1.5f);
            }

            g.DevSkipPhase(GameState.PreBall);
            for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
            g.TimerPaused.Value = true;
            yield return new WaitForSeconds(2f);
            var spot = OnGround(Vector3.Lerp(Cfg.BaseCenter[team], Vector3.zero, 0.55f) + side * 14f, 0.1f);

            // ---- invisible and attacking: they still can't see you - just what's in your hand
            {
                PlayerNet bot = null;
                foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p;
                if (bot == null) { PlayerNet.ServerAddBot(enemy); yield return new WaitForSeconds(1f); foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value) bot = p; }
                if (bot != null)
                {
                    foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
                    if (bot.Dead.Value) { bot.ServerRespawn(false); yield return new WaitForSeconds(0.5f); }
                    pc.LocalTeleport(spot, Quaternion.LookRotation(side).eulerAngles.y);
                    bot.TeleportRpc(OnGround(spot + side * 4f, 0.1f), Quaternion.LookRotation(-side).eulerAngles.y);
                    for (int i = 0; i < bot.Inv.Count; i++) bot.Inv[i] = default;
                    bot.ServerGive(Item.Sword, 1);
                    yield return Hold(bot, Item.Sword);
                    bot.InvisUntil.Value = g.NetworkManager.ServerTime.Time + 30;
                    bot.RevealUntil.Value = -1;
                    yield return new WaitForSeconds(0.6f);
                    int Drawn(out int handParts)
                    {
                        int n = 0; handParts = 0;
                        var hand = bot.HandItemForTests;
                        foreach (var r in bot.GetComponentsInChildren<Renderer>())
                        {
                            if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                            if (hand != null && r.transform.IsChildOf(hand.transform)) handParts++; else n++;
                        }
                        return n;
                    }
                    int body0 = Drawn(out int hand0);
                    Check(bot.Hidden && body0 == 0 && hand0 == 0, $"invisible: nothing of them shows ({body0} parts, {hand0} of the sword)");
                    bot.MeleeRpc(false, default, bot.EyePos + bot.transform.forward * 2f, false);
                    yield return new WaitForSeconds(0.6f);
                    int body1 = Drawn(out int hand1);
                    Check(bot.Hidden && bot.HeldShowing && body1 == 0 && hand1 > 0, $"they attack: still invisible, but their sword shows ({body1} parts of them, {hand1} of the sword)");
                    pc.SetLook(Quaternion.LookRotation(side).eulerAngles.y, 0f);
                    yield return new WaitForSeconds(0.3f);
                    yield return Snap("prompt9_invisible_sword");
                    bot.InvisUntil.Value = -1;
                    yield return new WaitForSeconds(0.4f);
                    int body2 = Drawn(out _);
                    Check(body2 > 0 && !bot.HeldShowing, "when the potion runs out they're seen again");
                    bot.TeleportRpc(OnGround(spot - side * 25f, 0.1f), 0f); // (out of the way)
                    yield return new WaitForSeconds(0.3f);
                }
                else Check(false, "an enemy bot to test invisibility on");
            }

            // ---- shooting out of your own window: the bars let your shots through (not the enemy's)
            {
                var wat = Cfg.BaseCenter[team] - back * 12f - side * 4f; // (in our base: flat and clear)
                wat.y = 0f;
                var win = SpawnKeylessPiece(PieceType.Window, team, wat, Quaternion.LookRotation(side));
                var ewin = SpawnKeylessPiece(PieceType.Window, enemy, wat - back * 5f, Quaternion.LookRotation(side));
                yield return new WaitForSeconds(0.4f);
                Collider bars = null, ebars = null;
                foreach (var c in win.GetComponentsInChildren<Collider>()) if (c.name == Structure.BarsName) bars = c;
                foreach (var c in ewin.GetComponentsInChildren<Collider>()) if (c.name == Structure.BarsName) ebars = c;
                Check(bars != null && Structure.ShotPasses(bars, team) && !Structure.ShotPasses(ebars, team), "your shots go out through your own window's bars, not through the enemy's");
                // a real arrow through our own window's bars
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                var from = wat - side * 2.5f + Vector3.up * 1.65f;
                pc.LocalTeleport(from - Vector3.up * 1.6f, Quaternion.LookRotation(side).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                var eye0 = me.EyePos;
                ArrowProjectile.Spawn(eye0, side * Cfg.ArrowSpeed, me, false);
                var arrow = ArrowProjectile.Flying[ArrowProjectile.Flying.Count - 1];
                yield return new WaitForSeconds(0.2f);
                float flown = arrow != null ? Vector3.Dot(arrow.transform.position - eye0, side) : 0f;
                Check(flown > 4f, $"an arrow shot through your own window's bars flies on out ({flown:0.0} m)");
                win.NetworkObject.Despawn(true); ewin.NetworkObject.Despawn(true);
            }

            // ---- the suicide cooldown, on screen
            {
                me.SuicideRpc();
                yield return new WaitForSeconds(0.5f);
                bool died = me.Dead.Value;
                for (float t = 0; t < Cfg.RespawnTime + 3f && me.Dead.Value; t += 0.25f) { if (me.ChoosingRespawn) me.RespawnChoiceRpc(false); yield return new WaitForSeconds(0.25f); }
                yield return new WaitForSeconds(0.5f);
                me.SuicideRpc();
                yield return new WaitForSeconds(0.5f);
                Check(died && !me.Dead.Value && Hud.LastWackyNote.Contains("cooldown") && Hud.LastWackyNote.Contains(" s "), $"killing yourself again too soon says so on screen, with the time left ({Hud.LastWackyNote})");
                yield return Snap("prompt9_suicide_cooldown");
            }

            // ---- barricades: never on someone, never on nothing, and they break when what's under them goes
            {
                pc.LocalTeleport(Cfg.BaseCenter[team] - back * 12f + side * 3f, Quaternion.LookRotation(side).eulerAngles.y);
                yield return new WaitForSeconds(0.4f);
                float yaw = Quaternion.LookRotation(side).eulerAngles.y;
                string onMe = PlayerNet.DeployProblem(Item.Barricade, team, me.transform.position, yaw);
                string inAir = PlayerNet.DeployProblem(Item.Barricade, team, me.transform.position + side * 5f + Vector3.up * 2.5f, yaw);
                Check(onMe != null && onMe.Contains("standing"), $"a barricade can't go where someone's standing ({onMe ?? "allowed"})");
                Check(inAir != null && inAir.Contains("ground"), $"...or on nothing ({inAir ?? "allowed"})");
                // on a foundation, which then breaks
                var fat = Cfg.BaseCenter[team] - back * 12f + side * 7f; // (in our base: flat and clear)
                fat.y = 0f;
                var f = SpawnKeylessPiece(PieceType.Foundation, team, fat, Quaternion.LookRotation(side));
                yield return new WaitForSeconds(0.4f);
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.ServerGive(Item.Barricade, 1);
                yield return Hold(me, Item.Barricade);
                var bpos = fat + Vector3.up * 1f;
                string onF = PlayerNet.DeployProblem(Item.Barricade, team, bpos, yaw);
                me.PlaceDeployableRpc((byte)Item.Barricade, bpos, yaw);
                yield return new WaitForSeconds(0.5f);
                Container bar = null;
                foreach (var c in Container.All) if (c != null && c.Kind.Value == Container.Barricade && Vector3.Distance(c.transform.position, bpos) < 0.6f) bar = c;
                Check(onF == null && bar != null, $"a barricade goes on a foundation ({onF ?? "ok"})");
                if (bar != null)
                {
                    f.ServerDamage(f.Health.Value + 1f);
                    yield return new WaitForSeconds(1.2f);
                    Check(!bar.IsSpawned, "the foundation under it breaks: so does the barricade");
                }
            }

            // ---- Jonah Test Mode Bruh
            if (Cfg.JonahBruh)
            {
                // a doorway is one piece: hitting its door hurts the doorway (no door to knock out)
                {
                    var dat = OnGround(spot + side * 8f, 0f);
                    var dw = SpawnKeylessPiece(PieceType.Doorway, enemy, dat, Quaternion.LookRotation(side));
                    yield return new WaitForSeconds(0.4f);
                    float hp0 = dw.Health.Value, door0 = dw.DoorHealth.Value;
                    dw.ServerDamageAt(40f, dw.transform.position + Vector3.up * 1.2f);
                    yield return new WaitForSeconds(0.2f);
                    Check(dw.Health.Value < hp0 && Mathf.Approximately(dw.DoorHealth.Value, door0) && Mathf.Approximately(dw.MaxHp, Cfg.PieceHp(PieceType.Wall, 0)),
                        $"hitting a doorway's door damages the doorway ({hp0:0} -> {dw.Health.Value:0} of {dw.MaxHp:0}, door {dw.DoorHealth.Value:0})");
                    dw.NetworkObject.Despawn(true);
                }

                // walls and doorways all the way round the one-cell base, then out over them from the machine
                {
                    var bc = Cfg.BedrockCenter(team);
                    int ci = Mathf.FloorToInt(bc.x / Cfg.Cell), cj = Mathf.FloorToInt(bc.z / Cfg.Cell);
                    var keys = new[] { new PieceKey(PieceKey.KEdge, ci - 1, cj, 0, 0), new PieceKey(PieceKey.KEdge, ci, cj, 0, 0), new PieceKey(PieceKey.KEdge, ci, cj - 1, 0, 1), new PieceKey(PieceKey.KEdge, ci, cj, 0, 1) };
                    pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                    for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                    me.ServerGive(Item.BuildingPlan, 1);
                    me.ServerGive(Cfg.BuildItem, 600);
                    yield return Hold(me, Item.BuildingPlan);
                    yield return new WaitForSeconds(0.3f);
                    int doors = 0;
                    var placed = new List<string>();
                    foreach (var k in keys)
                    {
                        me.PlaceRpc((byte)PieceType.Doorway, k.I, k.J, k.L, k.D);
                        yield return new WaitForSeconds(Cfg.BuildCooldown + 0.2f);
                        bool there = BuildGrid.Registry.TryGetValue(k, out var s) && s != null && s.PType == PieceType.Doorway;
                        if (there) doors++;
                        placed.Add(there ? "door" : "-");
                    }
                    Check(doors == 4, $"doorways go on every side of the one-cell base ({string.Join(" ", placed)})");
                    yield return new WaitForSeconds(0.5f);
                    // shut them all
                    foreach (var k in keys) if (BuildGrid.Registry.TryGetValue(k, out var s) && s != null && s.DoorOpen.Value) s.DoorOpen.Value = false;
                    pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
                    yield return new WaitForSeconds(0.4f);
                    yield return Snap("prompt9_doors_round_base");
                    // the climb: onto the step, onto the machine's top...
                    var step = Cfg.OnSmallBedrock(team, Cfg.ClimbStepLocal + Vector3.up * (Cfg.BaseY + Cfg.ClimbStepH + 0.05f));
                    var top = Cfg.MachinePos(team) + Vector3.up * (Cfg.MachineTopY + 0.05f);
                    float jumpH = Cfg.JumpSpeed * Cfg.JumpSpeed / (2f * Cfg.Gravity);
                    Check(Cfg.ClimbStepH + 0.15f < jumpH && Cfg.MachineTopY - Cfg.ClimbStepH + 0.1f < jumpH && Cfg.MachineTopY + jumpH > Cfg.LevelH + 0.2f,
                        $"jump {jumpH:0.00} m: onto the step ({Cfg.ClimbStepH} m), the machine's top ({Cfg.MachineTopY:0.00} m), and from there over a {Cfg.LevelH} m wall");
                    pc.LocalTeleport(step, Quaternion.LookRotation(Flat(top, step) > 0.01f ? new Vector3(top.x - step.x, 0, top.z - step.z) : back).eulerAngles.y);
                    yield return new WaitForSeconds(0.6f);
                    Binds.TestHold(Bind.Forward, true);
                    Binds.TestPress(Bind.Jump);
                    yield return new WaitForSeconds(0.45f);
                    Binds.TestHold(Bind.Forward, false);
                    yield return new WaitForSeconds(0.6f);
                    float onTop = me.transform.position.y - (Cfg.MachinePos(team).y + Cfg.MachineTopY);
                    Check(Mathf.Abs(onTop) < 0.25f, $"from the step you jump up onto the machine's top ({onTop:+0.00;-0.00} m from it)");
                    // ...and over the back wall
                    pc.LocalTeleport(top, Quaternion.LookRotation(back).eulerAngles.y);
                    yield return new WaitForSeconds(0.6f);
                    Binds.TestHold(Bind.Forward, true);
                    Binds.TestPress(Bind.Jump);
                    yield return new WaitForSeconds(1.4f);
                    Binds.TestHold(Bind.Forward, false);
                    yield return new WaitForSeconds(0.5f);
                    float outBy = Vector3.Dot(me.transform.position - bc, back) - Cfg.BedrockHalfNow;
                    Check(outBy > 0.3f, $"from the machine's top you jump out over the wall ({outBy:0.0} m past the bedrock's edge)");
                    yield return Snap("prompt9_jumped_out");
                    foreach (var k in keys) if (BuildGrid.Registry.TryGetValue(k, out var s) && s != null) s.NetworkObject.Despawn(true);
                    yield return new WaitForSeconds(0.3f);
                }

                // C4 takes one layer of stone: the wall it's on, the one next to it, the foundation under it - not the stone wall behind
                {
                    var c = OnGround(spot + side * 12f, 0f);
                    var u = side; var v = -back;
                    var rotV = Quaternion.LookRotation(v);
                    var fA = SpawnKeylessPiece(PieceType.Foundation, enemy, c, rotV);
                    var fB = SpawnKeylessPiece(PieceType.Foundation, enemy, c + u * 3f, rotV);
                    var w1 = SpawnKeylessPiece(PieceType.Wall, enemy, c + v * 1.5f + Vector3.up * 1f, rotV);
                    var w2 = SpawnKeylessPiece(PieceType.Wall, enemy, c + u * 3f + v * 1.5f + Vector3.up * 1f, rotV);
                    var w3 = SpawnKeylessPiece(PieceType.Wall, enemy, c - v * 1.5f + Vector3.up * 1f, rotV);
                    yield return new WaitForSeconds(0.4f);
                    foreach (var s in new[] { fA, fB, w1, w2, w3 }) s.ServerUpgrade(1);
                    yield return new WaitForSeconds(0.3f);
                    var at = w1.transform.position + Vector3.up * 1.2f + v * 0.16f;
                    pc.LocalTeleport(OnGround(c + v * 9f, 0.1f), Quaternion.LookRotation(-v).eulerAngles.y);
                    g.ServerArmC4(at, v, me);
                    yield return new WaitForSeconds(Cfg.C4Fuse + 0.8f);
                    string State(Structure s) => s != null && s.IsSpawned ? "up" : "gone";
                    Check(!w1.IsSpawned && !w2.IsSpawned && !fA.IsSpawned && w3.IsSpawned && fB.IsSpawned,
                        $"C4 on a stone wall: it, the one beside it and the foundation under it go; the stone wall behind stands (wall {State(w1)}, beside {State(w2)}, under {State(fA)}, behind {State(w3)}, the other foundation {State(fB)})");
                    yield return Snap("prompt9_c4_one_layer");
                    foreach (var s in new[] { fA, fB, w1, w2, w3 }) if (s != null && s.IsSpawned) s.NetworkObject.Despawn(true);
                }

                // the ram: cracks ALL over the stone, then plain wood (no cracks), then it breaks
                {
                    for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                    float yaw = Quaternion.LookRotation(-back).eulerAngles.y;
                    var wallAt = OnGround(spot - back * 5f, 0f);
                    var w = SpawnKeylessPiece(PieceType.Wall, enemy, wallAt, Quaternion.Euler(0, yaw, 0));
                    yield return new WaitForSeconds(0.3f);
                    w.ServerUpgrade(1);
                    var face = w.transform.position + Vector3.up * 1.3f + back * 0.1f;
                    pc.LocalTeleport(OnGround(face + back * 2.2f, 0.1f), yaw);
                    yield return new WaitForSeconds(0.4f);
                    pc.SetLook(yaw, 0f);
                    for (int hit = 0; hit < 3; hit++)
                    {
                        me.ServerGive(Item.Ram, 1, Cfg.MaxData(Item.Ram));
                        yield return Hold(me, Item.Ram);
                        yield return new WaitForSeconds(0.3f);
                        me.RamStrikeRpc(w.NetworkObject, face);
                        yield return new WaitForSeconds(0.7f);
                        int cracks = 0;
                        if (w != null && w.IsSpawned) foreach (var tr in w.GetComponentsInChildren<Transform>()) if (tr.name == "crack") cracks++;
                        if (hit == 0) { Check(w.IsSpawned && w.Tier.Value == 1 && cracks >= 60, $"the first ram cracks the stone all over ({cracks} crack lines)"); yield return Snap("prompt9_ram_cracks_all_over"); }
                        if (hit == 1) { Check(w.IsSpawned && w.Tier.Value == 0 && cracks == 0, $"the second takes it down to wood, with no cracks on it ({cracks})"); yield return Snap("prompt9_ram_plain_wood"); }
                        if (hit == 2) Check(w == null || !w.IsSpawned, "the third breaks it");
                        yield return new WaitForSeconds(Cfg.RamWindup);
                    }
                }
            }

            Log("prompt9 done");
            Application.Quit(0);
        }
    }
}
