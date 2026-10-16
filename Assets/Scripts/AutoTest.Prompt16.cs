using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public partial class AutoTest
    {
        /// <summary>
        /// -autotest prompt16 -host -solo -rules emerald | luckyblock [-mode ffa4] -shotdir DIR:
        /// EMERALD MODE's changes (Emerald.cs): the spawners left and right of the ball with two bases (closer in, on every
        /// team's way to the middle, with 3-4), no emeralds while the glass wall's up (the clocks wait full), and the emerald
        /// shop down to the knockback stick, an ender pearl, the revolver and a clip, the shotgun and a clip, and C4.
        /// LUCKY BLOCK MODE (LuckyBlock.cs): Bedwars with no trees, berries or airdrops, 2000 wood to start, only the
        /// building plan in the bag, the wood machine spamming wood, emerald spawners outside every base and in between,
        /// the emerald shop's one row (a lucky block: a random airdrop item) and a 30 s wall.
        /// -autotest prompt16menu -shotdir DIR (no -host): Lucky Block on the main menu's mode list and its page.
        /// </summary>
        IEnumerator Prompt16Routine(PlayerNet me, PlayerController pc)
        {
            var g = NetGame.Instance;
            int team = me.Team.Value;
            var back = Cfg.BackDir(team);
            var face = Quaternion.LookRotation(-back);
            if (Cfg.LuckyBlock) { yield return Prompt16Lucky(me, pc, g, team, back, face); yield break; }
            if (!Cfg.Emerald) { Log("FAIL: prompt16 needs -rules emerald or -rules luckyblock"); Application.Quit(1); yield break; }

            // ---- where the spawners are
            var tops = EmeraldSpawner.Tops;
            {
                Check(tops.Count == Cfg.Copies, $"{tops.Count} emerald spawners ({Cfg.Copies} expected)");
                float baseDist = Flat(Cfg.BaseCenter[0], Vector3.zero);
                var report = new List<string>();
                bool ok = true;
                foreach (var t in tops)
                {
                    float c = Flat(t, Vector3.zero), nearest = float.MaxValue, second = float.MaxValue;
                    for (int b = 0; b < Cfg.TeamCount; b++)
                    {
                        float d = Flat(t, Cfg.BaseCenter[b]);
                        if (d < nearest) { second = nearest; nearest = d; } else if (d < second) second = d;
                    }
                    report.Add($"{c:0} m out, {nearest:0} m from the nearest base");
                    if (!Cfg.FourWay)
                    {
                        // left / right of the ball: as far from one base as the other, well clear of both
                        ok &= Mathf.Abs(nearest - second) < 6f && nearest > baseDist * 0.8f && c > MapBuilder.DomeRadius + 6f && c < Cfg.MapHalf * 0.5f;
                    }
                    else
                    {
                        // closer in than they were (half way between the dome and the base's edge)
                        float near = MapBuilder.DomeRadius + 7f, far = baseDist - Cfg.BaseHalf - 5f;
                        ok &= c < (near + far) * 0.5f - 4f && c > MapBuilder.DomeRadius + 4f;
                    }
                }
                Check(ok, (Cfg.FourWay ? "3-4 bases: the spawners on every team's way to the middle, closer in: " : "two bases: the spawners to the left and right of the ball, away from both bases: ") + string.Join("; ", report));
                if (!Cfg.FourWay && tops.Count == 2)
                {
                    var axis = Cfg.BaseCenter[1] - Cfg.BaseCenter[0]; axis.y = 0f;
                    var side = Vector3.Cross(Vector3.up, axis.normalized);
                    float along0 = Vector3.Dot(tops[0], side), along1 = Vector3.Dot(tops[1], side);
                    Check(along0 * along1 < 0f && Mathf.Abs(Vector3.Dot(tops[0], axis.normalized)) < 7f, $"one on each side of the ball ({along0:0}, {along1:0} across)");
                }
            }

            // ---- no emeralds while the wall's up
            {
                Check(g.WallUp, $"the glass wall's up ({g.S})");
                yield return new WaitForSeconds(3f);
                int n = 0; foreach (var it in g.Items) if (it.Stack.Id == Item.Emerald) n++;
                float left = g.EmeraldSecondsLeft(0);
                Check(n == 0 && !g.EmeraldWaiting(0) && left > EmeraldSpawner.EveryOf(0) - 2f, $"no emeralds before the wall drops ({n} out; the clock waits at {left:0} s of {EmeraldSpawner.EveryOf(0):0})");
                g.ServerEmeraldsNow();
                yield return new WaitForSeconds(0.6f);
                n = 0; foreach (var it in g.Items) if (it.Stack.Id == Item.Emerald) n++;
                Check(n == 0, $"...not even when the clock's pushed: {n}");
                var dir = new Vector3(tops[0].x, 0, tops[0].z).normalized;
                var at = OnGround(tops[0] + Quaternion.Euler(0, 35f, 0) * dir * 8f);
                var look = Quaternion.LookRotation(new Vector3(tops[0].x - at.x, 0, tops[0].z - at.z)).eulerAngles.y;
                pc.LocalTeleport(at, look);
                pc.SetLook(look, 6f);
                yield return new WaitForSeconds(0.6f);
                bool waits = false; foreach (var s in Hud.EmeraldClocksShown) waits |= s.Contains("when the wall drops");
                Check(waits, $"the clock over it says so ({string.Join(", ", Hud.EmeraldClocksShown)})");
                yield return Snap("prompt16_emerald_spawner_wall");
                // from the base: the spawners off to the sides, the middle ahead
                pc.LocalTeleport(OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 2f)), Quaternion.LookRotation(-back).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(-back).eulerAngles.y, 2f);
                yield return new WaitForSeconds(0.6f);
                yield return Snap("prompt16_emerald_from_base");
            }

            // ---- the emerald shop
            {
                var deals = Cfg.EmeraldDeals;
                var want = new (Item id, int n, int price)[]
                {
                    (Item.KnockStick, 1, 1), (Item.EnderPearl, 1, 2), (Item.Lightsaber, 1, 3), (Item.AlienGun, 1, 4), (Item.C4, 1, 3),
                };
                bool same = deals.Count == want.Length;
                for (int i = 0; same && i < want.Length; i++) same = deals[i].Id == want[i].id && deals[i].Count == want[i].n && deals[i].Price == want[i].price && deals[i].Extra == Item.None;
                var list = new List<string>(); foreach (var d in deals) list.Add($"{d.Id} x{d.Count} {d.Price}");
                Check(same, $"the shop: just these, at these prices ({string.Join(", ", list)})");
                for (int i = 0; i < me.Inv.Count; i++) me.Inv[i] = default;
                me.ServerGive(Item.Emerald, 25);
                pc.LocalTeleport(Cfg.SpawnPos(team), Quaternion.LookRotation(back).eulerAngles.y);
                yield return new WaitForSeconds(0.5f);
                pc.OpenShop(PlayerController.ShopEmerald);
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Check(Hud.ShopShown == PlayerController.ShopEmerald && Hud.ShopRowsShown.Count == want.Length, $"E on it: {Hud.ShopRowsShown.Count} rows");
                yield return Snap("prompt16_emerald_shop");
                pc.CloseMenu();
                int spent = 0;
                for (int i = 0; i < deals.Count; i++) { me.EmeraldBuyRpc((byte)i); spent += deals[i].Price; yield return new WaitForSeconds(0.15f); }
                yield return new WaitForSeconds(0.3f);
                var missing = new List<string>();
                foreach (var w in want) if (me.Count(w.id) < w.n) missing.Add($"{w.id} ({me.Count(w.id)})");
                Check(missing.Count == 0 && spent == 13 && me.Count(Item.Emerald) == 25 - spent, $"bought one of each for {spent} emeralds ({me.Count(Item.Emerald)} left){(missing.Count > 0 ? " - missing " + string.Join(", ", missing) : "")}");
                bool noGuns = true;
                foreach (var d in deals) if (d.Id == Item.Revolver || d.Id == Item.RevolverAmmo || d.Id == Item.Shotgun || d.Id == Item.ShotgunShell || d.Id == Item.Pistol || d.Id == Item.Sniper) noGuns = false;
                Check(noGuns && !Cfg.Removed(Item.AlienGun) && !Cfg.Removed(Item.Lightsaber), "no revolver, shotgun or ammo in the shop - the lightsaber and the alien gun instead");
                int slot = -1;
                for (int i = 0; i < me.Inv.Count; i++) if (me.Inv[i].Id == Item.AlienGun) slot = i;
                Check(slot >= 0 && me.Inv[slot].Data == 10 && Cfg.GunMag(Item.AlienGun) == 10 && Cfg.AlienGunBodyDamage > Cfg.RevolverBodyDamage && Cfg.AlienGunHeadDamage > Cfg.RevolverHeadDamage,
                    $"the alien gun: 10 shots ({(slot >= 0 ? me.Inv[slot].Data : -1)}), harder hitting than the revolver ({Cfg.AlienGunBodyDamage:0}/{Cfg.AlienGunHeadDamage:0} vs {Cfg.RevolverBodyDamage:0}/{Cfg.RevolverHeadDamage:0})");
            }

            // ---- the wall drops: the clocks start
            {
                g.DevSkipPhase(GameState.PreBall);
                for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(1.5f);
                float left = g.EmeraldSecondsLeft(0);
                Check(!g.WallUp && left > EmeraldSpawner.EveryOf(0) - 4f && left < EmeraldSpawner.EveryOf(0), $"the wall's down: the clock runs from the full {EmeraldSpawner.EveryOf(0):0} s ({left:0.0} left)");
                g.ServerEmeraldsNow();
                yield return new WaitForSeconds(0.6f);
                int n = 0; foreach (var it in g.Items) if (it.Stack.Id == Item.Emerald) n++;
                Check(n == tops.Count, $"then they grow: {n} emeralds out");
                var dir = new Vector3(tops[0].x, 0, tops[0].z).normalized;
                var at = OnGround(tops[0] + Quaternion.Euler(0, 35f, 0) * dir * 8f);
                var look = Quaternion.LookRotation(new Vector3(tops[0].x - at.x, 0, tops[0].z - at.z)).eulerAngles.y;
                pc.LocalTeleport(at, look);
                pc.SetLook(look, 6f);
                yield return new WaitForSeconds(0.6f);
                yield return Snap("prompt16_emerald_spawner");
            }

            // ---- the alien gun: shots left bottom right, more damage than the revolver, breaks after its 10th shot
            {
                int enemy = (team + 1) % Mathf.Max(2, Cfg.TeamCount);
                PlayerNet.ServerAddBot(enemy);
                yield return new WaitForSeconds(0.8f);
                PlayerNet bot = null;
                foreach (var p in PlayerNet.All) if (p != null && p.Bot.Value && p.Team.Value == enemy) bot = p;
                if (bot == null) Log("FAIL: no bot for the alien gun");
                else
                {
                    foreach (var mb in bot.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("BotBrain")) mb.enabled = false;
                    g.TimerPaused.Value = true;
                    if (bot.Dead.Value) bot.ServerRespawn(false);
                    yield return new WaitForSeconds(0.3f);
                    // the two of us out in the open, the bot a few metres in front
                    var side = Vector3.Cross(Vector3.up, back);
                    var spot = OnGround(back * (MapBuilder.DomeRadius + 14f) + side * 12f);
                    float yaw = Quaternion.LookRotation(-back).eulerAngles.y;
                    pc.LocalTeleport(spot, yaw);
                    pc.SetLook(yaw, 0f);
                    bot.TeleportRpc(OnGround(spot - back * 6f), yaw + 180f);
                    yield return Hold(me, Item.AlienGun);
                    yield return new WaitForSeconds(0.8f);
                    Check(Hud.AmmoRect.x > Screen.width * 0.5f && Hud.AmmoRect.y > Screen.height * 0.5f && Time.frameCount - Hud.AmmoShownFrame < 5,
                        $"holding it: its shots left in the bottom right ({Hud.AmmoRect})");
                    yield return Snap("prompt17_aliengun");
                    float hp0 = bot.Health.Value;
                    var at = bot.transform.position + Vector3.up * 1.0f;
                    var dir = (at - me.EyePos).normalized;
                    me.FirePistolRpc(true, new Unity.Netcode.NetworkObjectReference(bot.NetworkObject), at, dir);
                    yield return new WaitForSeconds(0.45f);
                    float dealt = hp0 - bot.Health.Value;
                    Check(Mathf.Abs(dealt - Cfg.AlienGunBodyDamage) < 0.5f && dealt > Cfg.RevolverBodyDamage && me.HeldStack.Data == 9,
                        $"a body shot: {dealt:0} damage (the revolver does {Cfg.RevolverBodyDamage:0}), {me.HeldStack.Data} shots left");
                    yield return Snap("prompt17_aliengun_9");
                    me.ReloadPistolRpc();
                    yield return new WaitForSeconds(0.3f);
                    Check(me.HeldStack.Data == 9, "no reloading it");
                    for (int i = 0; i < 9; i++)
                    {
                        var fwd = me.transform.forward;
                        me.FirePistolRpc(false, default, me.EyePos + fwd * 30f + Vector3.up * 5f, fwd);
                        yield return new WaitForSeconds(0.4f);
                    }
                    Check(me.Count(Item.AlienGun) == 0 && me.HeldItem != Item.AlienGun, $"after its 10th shot it's gone ({me.Count(Item.AlienGun)} left)");
                }
            }
            Log("prompt16 done");
            Application.Quit(0);
        }

        IEnumerator Prompt16Lucky(PlayerNet me, PlayerController pc, NetGame g, int team, Vector3 back, Quaternion face)
        {
            // ---- the rules
            Check(Cfg.Bedwars && Cfg.NoBall && Cfg.HasEmeralds && !Cfg.Emerald && !Cfg.PowerMenu && Cfg.RulesName(Cfg.Rules) == "Lucky Block" && System.Array.IndexOf(Hud.MenuModes, GameRules.LuckyBlock) >= 0,
                "Lucky Block: Bedwars (no ball), emeralds, no power items, on the menu");
            Check(me.Count(Item.Wood) == Cfg.LuckyStartWood && Cfg.LuckyStartWood == 2000, $"everyone starts with {me.Count(Item.Wood)} wood");
            float wall = (float)(g.PhaseEnd.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time);
            Check(g.S == GameState.PreBall && Cfg.WallDropDelay == 30f && wall <= 30.5f && wall > 5f, $"the wall drops after 30 s ({wall:0} s left now)");
            int nodes = 0; foreach (var n in ResourceNode.All) if (n != null) nodes++;
            Check(nodes == 0, $"no trees, logs, rocks or berry bushes anywhere ({nodes})");
            int ramI = Cfg.CraftIndexOf(Item.Ram);
            Check(Cfg.CraftIndexOf(Item.BuildingPlan) >= 0 && ramI >= 0 && Cfg.CraftRecipe(ramI, team).Wood == 250 && Cfg.CraftRecipe(ramI, team).Stone == 0 && Cfg.CraftIndexOf(Item.Hatchet) < 0 && Cfg.CraftIndexOf(Item.Workbench) < 0 && Cfg.CraftIndexOf(Item.Spear) < 0 && Cfg.PowerCount == 0,
                "the building plan and a 250 wood battering ram are the only things to craft");
            {
                pc.MenuOpen = true;
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                var rows = new List<string>(); foreach (var r in Hud.CraftRowsShown) rows.Add(r.Id.ToString());
                Check(Hud.CraftRowsShown.Count == 2 && Hud.CraftRowsShown[0].Id == Item.BuildingPlan && Hud.CraftRowsShown[1].Id == Item.Ram, $"the bag's crafting list: {string.Join(", ", rows)}");
                yield return Snap("prompt16_lucky_bag");
                pc.CloseMenu();
            }

            // ---- the base: the machine, the cryochamber, the emerald shop on its left
            {
                var mach = GameObject.Find("AlienMachine " + Cfg.TeamName[team]);
                var shop = EmeraldShop.ByTeam[team];
                Check(mach != null && shop != null, "the alien machine and the emerald shop are there");
                if (mach != null && shop != null)
                {
                    var l = Quaternion.Inverse(face) * (shop.transform.position - mach.transform.position);
                    var sb = shop.GetComponentInChildren<BoxCollider>().bounds;
                    string clash = null;
                    foreach (var col in mach.GetComponentsInChildren<Collider>()) if (!col.isTrigger && col.bounds.Intersects(sb)) clash = col.name;
                    Check(l.x > 0.5f && clash == null, $"the shop's on the machine's left ({l.x:0.00} m), clear of it ({clash ?? "clear"})");
                }
                pc.LocalTeleport(Cfg.SpawnPos(team) - back * 1.2f, Quaternion.LookRotation(back).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 14f);
                yield return new WaitForSeconds(0.8f);
                yield return Snap("prompt16_lucky_base");
            }

            // ---- the spawners: in between the bases, none by a base
            var tops = EmeraldSpawner.Tops;
            {
                Check(tops.Count == Cfg.Copies, $"{tops.Count} emerald spawners ({Cfg.Copies} expected)");
                int home = 0, mid = 0;
                var report = new List<string>();
                for (int i = 0; i < tops.Count; i++)
                {
                    float nearest = float.MaxValue;
                    for (int b = 0; b < Cfg.TeamCount; b++) nearest = Mathf.Min(nearest, Flat(tops[i], Cfg.BaseCenter[b]));
                    float every = EmeraldSpawner.EveryOf(i);
                    report.Add($"{nearest:0} m from a base, every {every:0} s");
                    if (nearest < Cfg.BaseHalf + 14f) home++;
                    else if (nearest > Cfg.BaseHalf + 14f && Mathf.Approximately(every, Cfg.LuckyMiddleEvery)) mid++;
                }
                Check(home == 0 && mid == Cfg.Copies, $"none by a base ({home}), {mid} in between: {string.Join("; ", report)}");
                yield return new WaitForSeconds(2f);
                int em = 0; foreach (var it in g.Items) if (it.Stack.Id == Item.Emerald) em++;
                Check(em == 0, $"no emeralds before the wall drops ({em})");
            }

            // ---- the emerald shop: one lucky block
            {
                var deals = Cfg.EmeraldDeals;
                Check(deals.Count == 1 && deals[0].Id == Item.LuckyBlock && deals[0].Price == Cfg.LuckyBlockPrice && Cfg.LuckyBlockPrice == 1, $"the shop: one row, a lucky block for {(deals.Count > 0 ? deals[0].Price : -1)} emeralds");
                Check(Cfg.Removed(Item.LuckyBlock) && !Cfg.Removed(Item.Emerald) && !Cfg.LuckyBlockPool.Contains(Item.Wood) && Cfg.LuckyBlockPool.Count > 20, $"a lucky block: any of {Cfg.LuckyBlockPool.Count} airdrop items (never wood, never a lucky block)");
                me.ServerGive(Item.Emerald, 10);
                pc.LocalTeleport(Cfg.SpawnPos(team), Quaternion.LookRotation(back).eulerAngles.y);
                yield return new WaitForSeconds(0.5f);
                pc.OpenShop(PlayerController.ShopEmerald);
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Check(Hud.ShopShown == PlayerController.ShopEmerald && Hud.ShopRowsShown.Count == 1 && Hud.ShopRowsShown[0].Id == Item.LuckyBlock, $"E on it: the lucky block row ({Hud.ShopRowsShown.Count})");
                yield return Snap("prompt16_lucky_shop");
                pc.CloseMenu();
                int rolls0 = PlayerNet.LuckyRolls;
                var before = new Dictionary<Item, int>();
                foreach (var it in Cfg.LuckyBlockPool) before[it] = me.Count(it);
                for (int i = 0; i < 3; i++) { me.EmeraldBuyRpc(0); yield return new WaitForSeconds(0.2f); }
                yield return new WaitForSeconds(0.3f);
                int got = 0; var names = new List<string>();
                foreach (var it in Cfg.LuckyBlockPool) { int d = me.Count(it) - before[it]; if (d > 0) { got += d; names.Add(it.ToString()); } }
                Check(PlayerNet.LuckyRolls - rolls0 == 3 && got >= 3 && me.Count(Item.Emerald) == 7 && me.Count(Item.LuckyBlock) == 0, $"3 lucky blocks for 3 emeralds: {string.Join(", ", names)} ({me.Count(Item.Emerald)} emeralds left)");
            }

            // ---- the wall drops: the wood machine spams wood, the spawners start
            {
                g.DevSkipPhase(GameState.PreBall);
                for (float t = 0; t < 10f && g.S != GameState.BallLive; t += 0.25f) yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(4.2f);
                int pile = 0;
                var tray = Cfg.WoodTrayPos(team);
                foreach (var it in g.Items) if (it.Stack.Id == Item.Wood && Flat(it.Pos, tray) < 2.5f) pile += it.Stack.Count;
                Check(pile >= 3 * Cfg.LuckyWoodPerSecond && Cfg.WoodPerSecondNow(team) == Cfg.LuckyWoodPerSecond, $"the wood machine: {Cfg.WoodPerSecondNow(team)} wood a second ({pile} piled up after ~4 s)");
                Check(g.NextDropLands.Value <= 0, "no airdrop on its way");
                g.ServerEmeraldsNow();
                yield return new WaitForSeconds(0.6f);
                int em = 0; foreach (var it in g.Items) if (it.Stack.Id == Item.Emerald) em++;
                Check(em == tops.Count, $"the wall's down: every spawner grows an emerald ({em})");
                pc.LocalTeleport(OnGround(Cfg.BaseCenter[team] - back * (Cfg.BaseHalf + 1f)), Quaternion.LookRotation(-back).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(-back).eulerAngles.y, 6f);
                yield return new WaitForSeconds(0.6f);
                yield return Snap("prompt16_lucky_from_base");
                pc.LocalTeleport(Cfg.SpawnPos(team) - back * 1.2f, Quaternion.LookRotation(back).eulerAngles.y);
                pc.SetLook(Quaternion.LookRotation(back).eulerAngles.y, 14f);
                yield return new WaitForSeconds(0.8f);
                yield return Snap("prompt16_lucky_base_live");
            }

            // ---- the wacky items from this patch: the snail hides your own view, the dance bomb slows you, a lighter clutter bomb
            {
                double now = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
                me.DanceUntil.Value = now + 3.0;
                yield return new WaitForSeconds(0.3f);
                Check(me.Dancing && Mathf.Abs(me.WackySpeedMul - Cfg.DanceSpeedMul) < 0.01f && Cfg.DanceSpeedMul <= 0.35f, $"dancing: you move at {me.WackySpeedMul:0.00}x");
                me.DanceUntil.Value = -1;
                var front = OnGround(me.transform.position - back * 5f);
                g.ServerAddGadget(Gadget.Snail, front, 0f, team, 4f, 7);
                yield return new WaitForSeconds(0.6f);
                Check(PlayerController.SnailShot(out _, out _) && !PlayerController.PipOn, "the attention snail: no window of your own view");
                yield return Snap("prompt16_snail");
                yield return new WaitForSeconds(4f);
                int r0 = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
                g.ServerAddGadget(Gadget.Clutter, front, 0f, team, 5f, 11);
                yield return new WaitForSeconds(0.6f);
                int added = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length - r0;
                Check(added > 50 && added < 260, $"a clutter bomb: {added} pieces (it was about 540)");
                yield return Snap("prompt16_clutter");
            }
            Log("prompt16 done");
            Application.Quit(0);
        }

        IEnumerator Prompt16MenuShots()
        {
            Hud.TestNewMenu = true;
            yield return new WaitForSeconds(2.5f);
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.LuckyBlock);
            Hud.TestNewPage(6);
            Hud.TestModesOpen(true);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("prompt16_menu_modes");
            Hud.TestModesOpen(false);
            yield return new WaitForSeconds(0.4f);
            Check(Cfg.RulesOf(Bootstrap.MapChoice) == GameRules.LuckyBlock, $"Lucky Block picked ({Cfg.RulesOf(Bootstrap.MapChoice)})");
            yield return Snap("prompt16_menu_lucky");
            Bootstrap.MapChoice = Cfg.WithRules(Bootstrap.MapChoice, GameRules.Emerald);
            yield return new WaitForSeconds(0.4f);
            yield return Snap("prompt16_menu_emerald");
            Log("prompt16menu done");
            Application.Quit(0);
        }
    }
}
