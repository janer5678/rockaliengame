using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest hands -host -solo -shotdir DIR (windowed): the first-person block hands holding every item, in the key
    /// moments of each animation (hands_X.png, and from the side: handsside_X.png). Checks that the hands are the original
    /// box arms (five boxes each: fist, knuckles, thumb, wrist band, forearm - their sizes and colours), that the poses put
    /// the hands where the original block-hand poses did, that nothing else is an arm model, and that Settings > Display >
    /// SHADING (Hands & items) shades the boxes and what's held smooth and puts the flat meshes back.
    /// </summary>
    public partial class AutoTest
    {
        /// <summary>The pose list (name, item, how to set it up) shared with the reference shots of the old build.</summary>
        static List<(string name, Item item, System.Action set)> HandsPoses(PlayerController pc)
        {
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
            foreach (var t in new[] { Item.InvisPotion, Item.DeathWand, Item.Helmet, Item.Armor, Item.Chest, Item.Barrier, Item.Arrow, Item.AirdropSignal, Item.FortTower, Item.EnderPearl, Item.Saddle })
                Add(t.ToString().ToLower() + "_idle", t);
            return poses;
        }

        /// <summary>Gives and holds each pose's item, sets the pose up and photographs it (from the eye and from the side);
        /// `each` runs while the pose is held (for checks).</summary>
        IEnumerator HandsPoseShots(PlayerNet me, PlayerController pc, string dir, List<(string name, Item item, System.Action set)> poses, System.Action<string> each)
        {
            // -hands a,b,...: only the poses whose names start with one of these (to iterate on a few quickly)
            string only = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-hands") only = args[i + 1];
            if (!string.IsNullOrEmpty(only))
            {
                var keep = only.Split(',');
                poses.RemoveAll(p => !System.Array.Exists(keep, k => p.name.StartsWith(k)));
            }
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
                yield return new WaitForSeconds(0.6f); // (the equip animation when the ball appears, the spear's raise)
                Shot("hands_" + p.name);
                yield return new WaitForEndOfFrame();
                each?.Invoke(p.name);
                // the same from the side (a second camera off to the right), to see how the hands hold the item
                var main = Camera.main;
                var side = new GameObject("hands side cam").AddComponent<Camera>();
                side.CopyFrom(main);
                side.depth = main.depth + 1;
                side.nearClipPlane = 0.02f;
                side.fieldOfView = 55f;
                side.transform.position = main.transform.TransformPoint(new Vector3(0.95f, 0.1f, 0.3f));
                side.transform.LookAt(main.transform.TransformPoint(new Vector3(0.05f, -0.22f, 0.42f)), main.transform.up);
                yield return new WaitForEndOfFrame();
                Shot("handsside_" + p.name);
                yield return null;
                Destroy(side.gameObject);
                Log("shot hands_" + p.name);
                pc.DebugDraw = -1f;
                ViewModel.DebugSwingE = ViewModel.DebugThrowE = ViewModel.DebugEatE = ViewModel.DebugUseE = -1f;
                ViewModel.DebugAim = ViewModel.DebugBall = false;
                yield return new WaitForSeconds(0.5f); // let it settle back to idle
            }
        }

        /// <summary>A hand's own boxes (its direct children; not what it holds).</summary>
        static MeshRenderer[] HandBoxes(Transform hand)
        {
            var l = new List<MeshRenderer>();
            foreach (Transform c in hand) { var r = c.GetComponent<MeshRenderer>(); if (r != null) l.Add(r); }
            return l.ToArray();
        }

        IEnumerator HandsShots(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            int team = me.Team.Value;
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            pc.SetLook(Cfg.SpawnYaw(team), 8f);
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.8f);
            var vm = ViewModel.Last;
            Check(vm != null, "there's a first-person view model");
            if (vm == null) { Application.Quit(1); yield break; }

            // ---- the hands are the original box arms ----
            var teamCol = Cfg.TeamColor[Mathf.Clamp(team, 0, 3)];
            var skin = Color.Lerp(new Color(0.6f, 0.64f, 0.58f), teamCol, 0.25f);
            var dark = skin * 0.85f; dark.a = 1f;
            // (fist, knuckles, thumb, wrist band, forearm: position, size, colour)
            var want = new (Vector3 pos, Vector3 size, Color col)[]
            {
                (Vector3.zero, new Vector3(0.085f, 0.09f, 0.1f), skin),
                (new Vector3(0, 0.005f, 0.055f), new Vector3(0.09f, 0.075f, 0.035f), dark),
                (new Vector3(-0.045f, 0.025f, 0.035f), new Vector3(0.03f, 0.03f, 0.065f), skin),
                (new Vector3(0, 0, -0.075f), new Vector3(0.082f, 0.082f, 0.035f), teamCol),
                (new Vector3(0, 0, -0.32f), new Vector3(0.072f, 0.072f, 0.46f), skin),
            };
            foreach (bool right in new[] { true, false })
            {
                var h = vm.DebugHand(right);
                var rs = HandBoxes(h);
                bool ok = rs.Length == 5;
                string why = $"{rs.Length} boxes";
                for (int i = 0; ok && i < 5; i++)
                {
                    var t = rs[i].transform;
                    var wp = want[i].pos; if (!right) wp.x = -wp.x;
                    var mf = rs[i].GetComponent<MeshFilter>();
                    bool cube = mf != null && (mf.sharedMesh == Art.Cube || SmoothShade.IsSmooth(mf.sharedMesh));
                    if (!cube || t.parent != h || (t.localPosition - wp).sqrMagnitude > 1e-8f || (t.localScale - want[i].size).sqrMagnitude > 1e-8f
                        || !ColorSlots.Same(rs[i].sharedMaterial.color, want[i].col) || !rs[i].enabled || rs[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                    { ok = false; why = $"box {i} ({t.name}) at {t.localPosition} size {t.localScale} #{ColorUtility.ToHtmlStringRGB(rs[i].sharedMaterial.color)} cube {cube}"; }
                }
                Check(ok, $"the {(right ? "right" : "left")} hand is the original box arm: fist, darker knuckles, thumb, team wrist band, forearm ({why})");
            }
            // nothing anywhere is an alien / block arm model any more
            int armModels = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                for (var t = r.transform; t != null; t = t.parent)
                    if (t.name.StartsWith("psx alienarm") || t.name.StartsWith("blocky arm") || t.name == "alien fit") { armModels++; break; }
            Check(armModels == 0, $"no alien clawed / block arm models left ({armModels})");

            // ---- every pose, photographed; the hands where the original poses put them ----
            // (the right hand's place in the view model at rest - no bob, sway or sprint - from the original poses)
            Vector3 RockSide(Vector3 idle, float pitch, float x) => idle + Quaternion.Euler(pitch, -6f, 0f) * new Vector3(x, 0f, 0f);
            var expectR = new Dictionary<string, Vector3>
            {
                { "rock_idle", RockSide(new Vector3(0.08f, -0.26f, 0.55f), -5f, 0.13f) },
                { "rock_slam", RockSide(new Vector3(0.03f, -0.3f, 0.68f), 35f, 0.13f) },
                { "hatchet_idle", new Vector3(0.27f, -0.3f, 0.5f) },
                { "hatchet_slam", new Vector3(0.14f, -0.38f, 0.62f) },
                { "sword_idle", new Vector3(0.27f, -0.3f, 0.5f) },
                { "buildingplan_idle", new Vector3(0.26f, -0.27f, 0.45f) },
                { "c4_idle", new Vector3(0.26f, -0.27f, 0.45f) },
                { "chest_idle", new Vector3(0.26f, -0.27f, 0.45f) },
                { "crossbow_idle", new Vector3(0.2f, -0.2f, 0.42f) + Quaternion.Euler(0f, -6f, 0f) * new Vector3(0.02f, -0.1f, -0.06f) },
            };
            var expectL = new Dictionary<string, Vector3>
            {
                { "rock_idle", RockSide(new Vector3(0.08f, -0.26f, 0.55f), -5f, -0.13f) },
                { "hatchet_idle", new Vector3(-0.3f, -0.9f, 0.2f) },
                { "crossbow_idle", new Vector3(0.2f, -0.2f, 0.42f) + Quaternion.Euler(0f, -6f, 0f) * new Vector3(-0.02f, -0.05f, 0.2f) },
            };
            var poses = HandsPoses(pc);
            poses.Add(("workbench_idle", Item.Workbench, null)); // (newer than the block hands: held the old way)
            int posed = 0, posedOk = 0;
            var off = new System.Text.StringBuilder();
            yield return HandsPoseShots(me, pc, dir, poses, name =>
            {
                foreach (var (exp, right) in new[] { (expectR, true), (expectL, false) })
                {
                    if (!exp.TryGetValue(name, out var e)) continue;
                    posed++;
                    var got = vm.DebugHand(right).localPosition;
                    if ((got - e).magnitude < 0.012f) posedOk++;
                    else off.Append($" {name} {(right ? "R" : "L")} {got} (want {e})");
                }
            });
            Check(posed > 0 && posedOk == posed, $"the hands sit where the original block-hand poses put them ({posedOk} / {posed}){off}");

            // ---- the colour setting reaches the boxes ----
            int hooks = 0, shaded = 0;
            foreach (var hook in FindObjectsByType<HandColorHook>(FindObjectsSortMode.None)) { hooks++; if (hook.AllShaded(out int n) && n == 5) shaded++; }
            Check(hooks == 2 && shaded == 2, $"both hands' boxes are in their colours ({shaded} / {hooks})");

            // ---- PSX graphics: the same block hands (with the PSX items) ----
            GameSettings.SetGraphics(1, false);
            foreach (var it in new[] { Item.Hatchet, Item.Bow, Item.Crossbow, Item.Spear, Item.Rock })
            {
                for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                if (it != Item.Rock) me.ServerGive(it, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "handspsx_" + it.ToString().ToLower() + ".png"));
                yield return new WaitForEndOfFrame();
            }
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);

            // ---- shade smooth (Settings > Display > SHADING > Hands & items): the boxes and what's held become their
            // smooth copies; off puts the very same meshes back ----
            foreach (var it in new[] { Item.Hatchet, Item.Rock, Item.Crossbow })
            {
                for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                if (it != Item.Rock) me.ServerGive(it, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.5f);
                var held = new List<Renderer>();
                vm.DebugHeld(held);
                var handRs = new List<Renderer>();
                handRs.AddRange(HandBoxes(vm.DebugHand(true)));
                handRs.AddRange(HandBoxes(vm.DebugHand(false)));
                var all = new List<Renderer>(held); all.AddRange(handRs);
                var before = new List<Mesh>();
                foreach (var r in all) before.Add(r.GetComponent<MeshFilter>()?.sharedMesh);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "handssmooth_" + it.ToString().ToLower() + "_off.png"));
                yield return new WaitForEndOfFrame();
                GameSettings.SmoothHands.Set(true, false);
                yield return new WaitForSeconds(0.3f);
                int smooth = 0, total = 0, handSmooth = 0;
                foreach (var r in held) { var m = r.GetComponent<MeshFilter>()?.sharedMesh; if (m == null || !m.isReadable) continue; total++; if (SmoothShade.IsSmooth(m)) smooth++; }
                foreach (var r in handRs) if (SmoothShade.IsSmooth(r.GetComponent<MeshFilter>()?.sharedMesh)) handSmooth++;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "handssmooth_" + it.ToString().ToLower() + "_on.png"));
                yield return new WaitForEndOfFrame();
                Check(total > 0 && smooth == total, $"shade smooth: every held {it} mesh is its smooth copy ({smooth}/{total})");
                Check(handSmooth == handRs.Count && handRs.Count == 10, $"shade smooth: the hands' boxes are lit smoothly ({handSmooth}/{handRs.Count}, {it})");
                GameSettings.SmoothHands.Set(false, false);
                yield return new WaitForSeconds(0.3f);
                bool back = true;
                for (int i = 0; i < all.Count; i++) if (all[i] && all[i].GetComponent<MeshFilter>()?.sharedMesh != before[i]) back = false;
                Check(back, $"shade smooth off: the {it}'s and the hands' own flat meshes are back");
            }

            // shade smooth (Aliens & crowd): the alien player models get their smooth copies, and their own meshes back after
            {
                var hooks2 = FindObjectsByType<SmoothShadeHook>(FindObjectsSortMode.None);
                var skins = new List<SkinnedMeshRenderer>();
                foreach (var h in hooks2) if (h.Aliens) skins.AddRange(h.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                var was = new List<Mesh>();
                foreach (var sk in skins) was.Add(sk.sharedMesh);
                GameSettings.SmoothAliens.Set(true, false);
                yield return null;
                int sm = 0;
                foreach (var sk in skins) if (SmoothShade.IsSmooth(sk.sharedMesh)) sm++;
                GameSettings.SmoothAliens.Set(false, false);
                yield return null;
                bool back = true;
                for (int i = 0; i < skins.Count; i++) if (skins[i].sharedMesh != was[i]) back = false;
                Check(skins.Count > 0 && sm == skins.Count && back, $"shade smooth (aliens): the alien models' {skins.Count} skinned meshes are swapped for smooth copies ({sm}) and back");
            }
            Log("hands done");
            Application.Quit(0);
        }
    }
}
