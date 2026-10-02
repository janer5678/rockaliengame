using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest hands -host -solo -shotdir DIR (windowed): the first-person hands holding every item, in the key moments of
    /// each animation, photographed three times - with the original box arms (hands_X_box), with the alien arms the game
    /// uses (hands_X_alien) and with both at once (hands_X_both) - so the alien hands can be lined up with the old ones.
    /// Then a few in PSX graphics, and checks that only the local first-person arms are the alien arm models.
    /// </summary>
    public partial class AutoTest
    {
        IEnumerator HandsShots(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            int team = me.Team.Value;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            pc.SetLook(Cfg.SpawnYaw(team), 8f);
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.8f);

            // the alien arm mesh, as the game sees it (which end is the hand)
            foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!mf.name.StartsWith("psx alienarm_r") && !(mf.transform.parent != null && mf.transform.parent.name.StartsWith("psx alienarm_r"))) continue;
                var m = mf.sharedMesh;
                Log($"alien arm mesh {mf.name}: bounds {m.bounds.center} {m.bounds.size}, {m.vertexCount} vertices");
                break;
            }

            var poses = new List<(string name, Item item, System.Action set)>();
            void Add(string n, Item it, System.Action a = null) => poses.Add((n, it, a));
            Add("rock_idle", Item.Rock);
            Add("rock_raised", Item.Rock, () => { pc.DebugSwing(0.6f); ViewModel.DebugSwingE = 0.075f; });
            Add("rock_slam", Item.Rock, () => { pc.DebugSwing(0.6f); ViewModel.DebugSwingE = 0.14f; });
            foreach (var t in new[] { Item.Hatchet, Item.Pickaxe, Item.Sword, Item.TreeCracker })
            {
                Add(t.ToString().ToLower() + "_idle", t);
                Add(t.ToString().ToLower() + "_raised", t, () => { pc.DebugSwing(0.6f); ViewModel.DebugSwingE = 0.075f; });
                Add(t.ToString().ToLower() + "_slam", t, () => { pc.DebugSwing(0.6f); ViewModel.DebugSwingE = 0.14f; });
            }
            Add("spear_idle", Item.Spear);
            Add("spear_thrust", Item.Spear, () => { pc.DebugSwing(0.6f); ViewModel.DebugSwingE = ViewModel.ImpactTime; });
            Add("spear_windup", Item.Spear, () => pc.DebugDraw = 1f);
            Add("bow_idle", Item.Bow);
            Add("bow_half", Item.Bow, () => pc.DebugDraw = 0.5f);
            Add("bow_drawn", Item.Bow, () => pc.DebugDraw = 1f);
            Add("crossbow_idle", Item.Crossbow);
            Add("crossbow_aim", Item.Crossbow, () => ViewModel.DebugAim = true);
            foreach (var t in new[] { Item.Pistol, Item.Revolver, Item.Shotgun, Item.Sniper, Item.PortalGun, Item.RocketLauncher })
                Add(t.ToString().ToLower() + "_idle", t);
            Add("ram_idle", Item.Ram);
            Add("chainsaw_idle", Item.Chainsaw);
            Add("buildingplan_idle", Item.BuildingPlan);
            Add("buildingplan_use", Item.BuildingPlan, () => ViewModel.DebugUseE = 0.15f);
            Add("c4_idle", Item.C4);
            Add("c4_throw", Item.C4, () => ViewModel.DebugThrowE = 0.12f);
            Add("berry_idle", Item.Berry);
            Add("berry_eat", Item.Berry, () => ViewModel.DebugEatE = 0.3f);
            Add("meat_idle", Item.Meat);
            Add("meat_eat", Item.Meat, () => ViewModel.DebugEatE = 0.3f);
            Add("ball_idle", Item.Rock, () => ViewModel.DebugBall = true);
            Add("ball_throw", Item.Rock, () => { ViewModel.DebugBall = true; ViewModel.DebugThrowE = 0.1f; });
            foreach (var t in new[] { Item.InvisPotion, Item.DeathWand, Item.Helmet, Item.Armor, Item.Chest, Item.Barrier, Item.Arrow, Item.AirdropSignal, Item.FortTower, Item.EnderPearl })
                Add(t.ToString().ToLower() + "_idle", t);

            void Shot(string n) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, n + ".png")); }
            Item heldNow = (Item)255;
            foreach (var p in poses)
            {
                if (p.item != heldNow)
                {
                    for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                    if (p.item != Item.Rock) me.ServerGive(p.item, p.item == Item.Barrier ? 2 : 1, p.item == Item.Revolver ? 3 : 0);
                    if (p.item == Item.Bow) me.ServerGive(Item.Arrow, 5);
                    yield return new WaitForSeconds(0.2f);
                    yield return Hold(me, p.item);
                    heldNow = p.item;
                    yield return new WaitForSeconds(0.4f);
                }
                p.set?.Invoke();
                yield return new WaitForSeconds(0.35f); // (the equip animation when the ball appears)
                foreach (int mode in new[] { 1, 0, 2 })
                {
                    ViewModel.DebugArms = mode;
                    yield return new WaitForSeconds(0.25f);
                    Shot($"hands_{p.name}_{(mode == 1 ? "box" : mode == 0 ? "alien" : "both")}");
                    yield return new WaitForEndOfFrame();
                }
                // the same from the side (a second camera off to the right), to see how the claws hold the item
                var main = Camera.main;
                var side = new GameObject("hands side cam").AddComponent<Camera>();
                side.CopyFrom(main);
                side.depth = main.depth + 1;
                side.nearClipPlane = 0.02f;
                side.fieldOfView = 55f;
                side.transform.position = main.transform.TransformPoint(new Vector3(0.95f, 0.1f, 0.3f));
                side.transform.LookAt(main.transform.TransformPoint(new Vector3(0.05f, -0.22f, 0.42f)), main.transform.up);
                foreach (int mode in new[] { 1, 0 })
                {
                    ViewModel.DebugArms = mode;
                    yield return new WaitForSeconds(0.15f);
                    Shot($"handsside_{p.name}_{(mode == 1 ? "box" : "alien")}");
                    yield return new WaitForEndOfFrame();
                }
                Destroy(side.gameObject);
                Log("shot hands_" + p.name);
                pc.DebugDraw = -1f;
                ViewModel.DebugSwingE = ViewModel.DebugThrowE = ViewModel.DebugEatE = ViewModel.DebugUseE = -1f;
                ViewModel.DebugAim = ViewModel.DebugBall = false;
                ViewModel.DebugArms = 0;
                yield return new WaitForSeconds(0.5f); // let it settle back to idle
            }

            // PSX graphics: the same alien hands (with the PSX items)
            GameSettings.SetGraphics(1, false);
            foreach (var it in new[] { Item.Hatchet, Item.Bow, Item.Crossbow, Item.Spear, Item.Rock })
            {
                for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                if (it != Item.Rock) me.ServerGive(it, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.6f);
                Shot("handspsx_" + it.ToString().ToLower());
                yield return new WaitForEndOfFrame();
            }
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);

            // the alien arm models are only ever the local first-person arms
            int arms = 0, elsewhere = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool alien = false, vm = false;
                for (var t = r.transform; t != null; t = t.parent) { alien |= t.name.StartsWith("psx alienarm"); vm |= t.name == "ViewModel"; }
                if (!alien) continue;
                if (vm) arms++; else elsewhere++;
            }
            Check(arms == 2 && elsewhere == 0, $"the alien arm models are the two first-person arms only ({arms} in the view model, {elsewhere} elsewhere)");
            Log("hands done");
            Application.Quit(0);
        }
    }
}
