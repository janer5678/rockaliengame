using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest hands -host -solo -shotdir DIR (windowed): the first-person hands holding every item, in the key moments
    /// of each animation (hands_X.png, and from the side: handsside_X.png) - the alien hands (Settings > Display > Alien
    /// hands, on), then a few with the old square ones (handssquare_X.png). Checks that each arm has both styles - the
    /// original box arm (five boxes: fist, knuckles, thumb, wrist band, forearm - their sizes and colours) and the alien one
    /// (the arms model: its fist and cupped hand) - with only the picked one showing, that the fist is round the handles
    /// it holds and the arms reach their hands, that the poses put the arms where the original block-hand poses did, that nothing else
    /// is an arm model, and that Settings > Display > SHADING (Hands & items) shades the hands and what's held smooth and
    /// puts the flat meshes back.
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
            Add("hatchet_deploy", Item.Hatchet, () => ViewModel.DebugEquipE = 0.14f);
            Add("pistol_deploy", Item.Pistol, () => ViewModel.DebugEquipE = 0.14f);
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
            // ...and every other item there is, held still (so every hold gets looked at)
            var have = new HashSet<Item>();
            foreach (var p in poses) have.Add(p.item);
            foreach (Item t in System.Enum.GetValues(typeof(Item)))
                if (t != Item.None && t != Item.LuckyBlock && !have.Contains(t)) Add(t.ToString().ToLower() + "_idle", t);
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
                    if (p.item != Item.Rock) me.ServerGive(p.item, p.item == Item.Barrier ? 2 : 1, p.item == Item.Revolver ? 3 : p.item == Item.AlienGun ? Cfg.GunMag(Item.AlienGun) : 0);
                    if (p.item == Item.Bow) me.ServerGive(Item.Arrow, 5);
                    yield return new WaitForSeconds(0.2f);
                    // (things that never go in the bag - wood, stone... - aren't held: skip them)
                    bool got = p.item == Item.Rock;
                    for (int i = 0; i < Cfg.PlayerSlots && !got; i++) got = me.SlotAt(i).Id == p.item;
                    if (!got) { Log("skip hands_" + p.name + " (not an item you hold)"); heldNow = (Item)255; continue; }
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
                ViewModel.DebugSwingE = ViewModel.DebugThrowE = ViewModel.DebugEatE = ViewModel.DebugUseE = ViewModel.DebugEquipE = -1f;
                ViewModel.DebugAim = ViewModel.DebugBall = false;
                yield return new WaitForSeconds(0.5f); // let it settle back to idle
            }
        }

        static Transform FindIn(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindIn(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        /// <summary>A hand's old square boxes (the children of its "square" part; not what it holds).</summary>
        static MeshRenderer[] HandBoxes(Transform hand)
        {
            var l = new List<MeshRenderer>();
            var sq = hand.Find("square");
            if (sq != null) foreach (Transform c in sq) { var r = c.GetComponent<MeshRenderer>(); if (r != null) l.Add(r); }
            return l.ToArray();
        }

        /// <summary>Every part of a hand that's showing (whichever style; not what it holds).</summary>
        static List<Renderer> HandParts(ViewModel vm, Transform hand)
        {
            var l = new List<Renderer>();
            foreach (var r in hand.GetComponentsInChildren<Renderer>()) if ((r is MeshRenderer || r is SkinnedMeshRenderer) && vm.IsHand(r)) l.Add(r);
            return l;
        }

        /// <summary>A renderer's mesh (a MeshFilter's, or a skinned mesh's - the alien arms' skin).</summary>
        static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;

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
            var skin = HandColorHook.Shade(teamCol, HandColorHook.Skin);
            var dark = HandColorHook.Shade(teamCol, HandColorHook.Knuckles);
            // (fist, knuckles, thumb, wrist band, forearm: position, size, colour)
            var want = new (Vector3 pos, Vector3 size, Color col)[]
            {
                (Vector3.zero, new Vector3(0.085f, 0.09f, 0.1f), skin),
                (new Vector3(0, 0.005f, 0.055f), new Vector3(0.09f, 0.075f, 0.035f), dark),
                (new Vector3(-0.045f, 0.025f, 0.035f), new Vector3(0.03f, 0.03f, 0.065f), skin),
                (new Vector3(0, 0, -0.075f), new Vector3(0.082f, 0.082f, 0.035f), HandColorHook.Shade(teamCol, HandColorHook.Band)),
                (new Vector3(0, 0, -0.32f), new Vector3(0.072f, 0.072f, 0.46f), skin),
            };
            GameSettings.SetRoundHands(true, false);
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
                    if (!cube || (t.localPosition - wp).sqrMagnitude > 1e-8f || (t.localScale - want[i].size).sqrMagnitude > 1e-8f
                        || rs[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) // (their colours: the HandColorHook check below)
                    { ok = false; why = $"box {i} ({t.name}) at {t.localPosition.ToString("F3")} size {t.localScale.ToString("F3")} (want {wp.ToString("F3")} {want[i].size.ToString("F3")}) shadows {rs[i].shadowCastingMode} cube {cube}"; }
                }
                Check(ok, $"the {(right ? "right" : "left")} arm keeps the original box arm for the square style: fist, darker knuckles, thumb, team wrist band, forearm ({why})");
                // the alien hand: the arms model - this arm's fist and cupped hand; no shadows
                var al = h.Find("alien");
                string side = right ? "ArmL_" : "ArmR_", otherSide = right ? "ArmR_" : "ArmL_"; // (the import mirrors the model: its L arm is the game's right)
                bool parts = al != null && al.Find("hands model/" + side + "Fist") != null && al.Find("hands model/" + side + "Cup") != null && al.Find("hands model/" + otherSide + "Fist") == null;
                bool shadowless = true;
                if (al != null) foreach (var r in al.GetComponentsInChildren<Renderer>(true)) if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadowless = false;
                var sqPart = h.Find("square");
                Check(al != null && al.gameObject.activeSelf && parts && shadowless && sqPart != null && !sqPart.gameObject.activeSelf,
                    $"the {(right ? "right" : "left")} arm is the alien hands model (its fist and cupped hand, not the other arm's; no shadows), the square one hidden");
            }
            // nothing anywhere is an alien / block arm model any more
            int armModels = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                for (var t = r.transform; t != null; t = t.parent)
                    if (t.name.StartsWith("psx alienarm") || t.name.StartsWith("blocky arm") || t.name == "alien fit") { armModels++; break; }
            Check(armModels == 0, $"no alien clawed / block arm models left ({armModels})");

            // ---- every pose, photographed; the hands where the original poses put them ----
            // (the right hand's place in the view model at rest - no bob, sway or sprint - from the original poses)
            // (the Rust-style poses: the rock cupped from below in the middle, tools low in the middle, C4 in both hands)
            Vector3 RockSide(Vector3 idle, float pitch, float x) => idle + Quaternion.Euler(pitch, -4f, 0f) * new Vector3(x, -0.07f, -0.02f);
            var expectR = new Dictionary<string, Vector3>
            {
                { "rock_idle", RockSide(new Vector3(0.03f, -0.22f, 0.5f), -8f, 0.09f) },
                { "rock_slam", RockSide(new Vector3(0.02f, -0.3f, 0.66f), 35f, 0.09f) },
                { "hatchet_idle", new Vector3(0.14f, -0.33f, 0.58f) },
                { "hatchet_slam", new Vector3(0.05f, -0.37f, 0.62f) },
                { "sword_idle", new Vector3(0.2f, -0.33f, 0.55f) },
                { "buildingplan_idle", new Vector3(0.21f, -0.27f, 0.45f) },
                { "c4_idle", new Vector3(0.05f, -0.27f, 0.44f) },
                { "chest_idle", new Vector3(0.26f, -0.3f, 0.54f) }, // (a box is held a touch lower and further out)
                { "crossbow_idle", new Vector3(0.2f, -0.2f, 0.42f) + Quaternion.Euler(0f, -6f, 0f) * new Vector3(0.02f, -0.1f, -0.06f) },
            };
            var expectL = new Dictionary<string, Vector3>
            {
                { "rock_idle", RockSide(new Vector3(0.03f, -0.22f, 0.5f), -8f, -0.09f) },
                { "hatchet_idle", new Vector3(-0.3f, -0.9f, 0.2f) },
                { "crossbow_idle", new Vector3(0.2f, -0.2f, 0.42f) + Quaternion.Euler(0f, -6f, 0f) * new Vector3(-0.02f, -0.05f, 0.2f) },
            };
            var poses = HandsPoses(pc);
            poses.Add(("workbench_idle", Item.Workbench, null)); // (newer than the block hands: held the old way)
            int posed = 0, posedOk = 0, gripped = 0, grippedOk = 0;
            var off = new System.Text.StringBuilder();
            var loose = new System.Text.StringBuilder();
            var forearmOut = new System.Text.StringBuilder();
            int forearmChecked = 0;
            yield return HandsPoseShots(me, pc, dir, poses, name =>
            {
                // a fist round a handle: the handle runs through the fist (it's not floating off the hand)
                if (name.EndsWith("_idle") && (name.StartsWith("hatchet") || name.StartsWith("pickaxe") || name.StartsWith("sword") || (name.StartsWith("pistol") && !name.Contains("ammo")) || (name.StartsWith("revolver") && !name.Contains("ammo")) || name.StartsWith("aliengun")))
                {
                    gripped++;
                    var fist = vm.DebugHand(true).Find("alien/hands model/ArmL_Fist");
                    var held = new List<Renderer>();
                    vm.DebugHeld(held);
                    bool touching = false;
                    if (fist != null && fist.gameObject.activeInHierarchy)
                    {
                        var fb = fist.GetComponent<Renderer>().bounds;
                        fb.Expand(0.02f);
                        foreach (var r in held) if (r.bounds.Intersects(fb)) touching = true;
                    }
                    if (touching) grippedOk++;
                    else loose.Append($" {name}");
                }
                // each hand that shows really is where its pose puts it (the arm reaches it: the fist's hole or the palm right there)
                foreach (var hh in new[] { vm.DebugHand(true), vm.DebugHand(false) })
                {
                    string sd = hh == vm.DebugHand(true) ? ".l" : ".r", pre = sd == ".r" ? "ArmR_" : "ArmL_"; // (mirrored by the import)
                    var model = hh.Find("alien/hands model");
                    if (model == null) continue;
                    var fistGo = model.Find(pre + "Fist");
                    var cupGo = model.Find(pre + "Cup");
                    Transform mark = fistGo != null && fistGo.gameObject.activeSelf ? FindIn(model, "grip" + sd) : cupGo != null && cupGo.gameObject.activeSelf ? FindIn(model, "palm" + sd) : null;
                    if (mark == null) continue;
                    // (a gun in the right hand: the fist goes on its grip, not where the block hand was)
                    if (hh == vm.DebugHand(true) && System.Array.Exists(new[] { "pistol", "revolver", "aliengun", "shotgun", "sniper", "portalgun", "rocketlauncher", "crossbow", "antgun", "slowgun" }, g => name.StartsWith(g) && !name.Contains("ammo"))) continue;
                    forearmChecked++;
                    float off = Vector3.Distance(mark.position, hh.position);
                    if (off > 0.012f) forearmOut.Append($" {name} {hh.name} {off:0.000}m");
                }
                foreach (var (exp, right) in new[] { (expectR, true), (expectL, false) })
                {
                    if (!exp.TryGetValue(name, out var e)) continue;
                    posed++;
                    var got = vm.DebugHand(right).localPosition;
                    if (e.y > -0.85f) e += new Vector3(-0.01f, 0.06f, 0f); // (every resting hold sits up in the view: ViewModel.k_HandsUp - not a hand put away out of sight)
                    if ((got - e).magnitude < 0.012f) posedOk++;
                    else off.Append($" {name} {(right ? "R" : "L")} {got} (want {e})");
                }
            });
            Check(posed > 0 && posedOk == posed, $"the arms sit where the Rust-style poses put them ({posedOk} / {posed}){off}");
            Check(gripped > 0 && grippedOk == gripped, $"the fist is round the handles and grips ({grippedOk} / {gripped}){loose}");
            Check(forearmChecked > 0 && forearmOut.Length == 0, $"the arms reach their hands: the fist / palm right where each pose puts it ({forearmChecked} checked){forearmOut}");

            // the old square hands, with Settings > Display > Alien hands off
            GameSettings.SetRoundHands(false, false);
            foreach (var it in new[] { Item.Hatchet, Item.Rock, Item.Crossbow })
            {
                for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                if (it != Item.Rock) me.ServerGive(it, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "handssquare_" + it.ToString().ToLower() + ".png"));
                yield return new WaitForEndOfFrame();
            }
            bool sqShown = vm.DebugHand(true).Find("square").gameObject.activeSelf && !vm.DebugHand(true).Find("alien").gameObject.activeSelf;
            Check(sqShown, "Alien hands off: the square hands show instead of the alien ones");
            GameSettings.SetRoundHands(true, false);
            yield return new WaitForSeconds(0.3f);

            // ---- the colour setting reaches the boxes ----
            int hooks = 0, shaded = 0;
            foreach (var hook in FindObjectsByType<HandColorHook>(FindObjectsSortMode.None)) { hooks++; if (hook.AllShaded(out int n) && n > 5) shaded++; }
            Check(hooks == 2 && shaded == 2, $"both hands' parts (square and alien) are in their colours ({shaded} / {hooks}{(shaded < hooks ? ": " + HandColorHook.LastMismatch : "")})");

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
                handRs.AddRange(HandParts(vm, vm.DebugHand(true)));
                handRs.AddRange(HandParts(vm, vm.DebugHand(false)));
                var all = new List<Renderer>(held); all.AddRange(handRs);
                var before = new List<Mesh>();
                foreach (var r in all) before.Add(MeshOf(r));
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "handssmooth_" + it.ToString().ToLower() + "_off.png"));
                yield return new WaitForEndOfFrame();
                GameSettings.SmoothHands.Set(true, false);
                yield return new WaitForSeconds(0.3f);
                int smooth = 0, total = 0, handSmooth = 0;
                foreach (var r in held) { var m = r.GetComponent<MeshFilter>()?.sharedMesh; if (m == null || !m.isReadable) continue; total++; if (SmoothShade.IsSmooth(m)) smooth++; }
                foreach (var r in handRs) { var hm = MeshOf(r); if (SmoothShade.IsSmooth(hm)) handSmooth++; }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "handssmooth_" + it.ToString().ToLower() + "_on.png"));
                yield return new WaitForEndOfFrame();
                Check(total > 0 && smooth == total, $"shade smooth: every held {it} mesh is its smooth copy ({smooth}/{total})");
                Check(handSmooth == handRs.Count && handRs.Count >= 1, $"shade smooth: the hands are lit smoothly ({handSmooth}/{handRs.Count}, {it})");
                GameSettings.SmoothHands.Set(false, false);
                yield return new WaitForSeconds(0.3f);
                bool back = true;
                for (int i = 0; i < all.Count; i++) if (all[i] && MeshOf(all[i]) != before[i]) back = false;
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
                // (a smooth copy's normals are all length 1 - never zero, which the shader turns into black / a white glare)
                int badNormals = 0, smoothMeshes = 0;
                var alienMesh = Resources.Load<GameObject>("Alien/AlienRigged");
                if (alienMesh != null)
                    foreach (var sk in alienMesh.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var smCopy = SmoothShade.SmoothOf(sk.sharedMesh);
                        if (smCopy == null) continue;
                        smoothMeshes++;
                        foreach (var nn in smCopy.normals) if (!(Mathf.Abs(nn.magnitude - 1f) < 0.01f)) badNormals++;
                    }
                Check(smoothMeshes > 0 && badNormals == 0, $"shade smooth: the alien's smooth normals are all whole ({badNormals} bad in {smoothMeshes} meshes)");
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
