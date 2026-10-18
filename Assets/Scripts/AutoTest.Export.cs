using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest exportassets -host -solo -exportdir DIR: writes every 3D model in the game as Wavefront OBJ (+ MTL) files,
    /// built by the game's own code so they're exactly what's drawn:
    ///   aliens/       the player alien in every team colour and every pose (idle, walk, sprint, crouch, jump, slide, bow,
    ///                 spear, aim, eat, throw, swings, carrying the ball, riding, dead, dancing, the lobby lounges...) with
    ///                 what it holds, plus the untouched A-pose
    ///   hands/        the first-person arms (square, round and alien) in every team colour holding a few things
    ///   items/        every item there is (plus the ball, the rocket, the stuck spear and arrow)
    ///   structures/   every building piece in every tier (the armoured tier in every team's trim)
    ///   deployables/  chests, benches, bags, traps, ladders, turrets, barricades, mines, the guardian... per team where it matters
    ///   vehicles/     the car, horse, unicorn, slender, boat and the training dummy
    ///   nature/       every pine variant, bushes, stumps
    ///   world/        the alien machines, upgrade stations, wood machines, emerald shops, benches, lucky blocks, flags, the UFO, hats, gadgets
    /// Tools/export_assets.ps1 runs it, tints the textures (tints.txt - Tools/tint_textures.py), then Blender turns the
    /// OBJs into FBX and .blend files (Tools/blender_pack.py) and the lot is zipped.
    /// </summary>
    public partial class AutoTest
    {
        static readonly string[] k_TeamLabel = { "blue", "red", "green", "yellow" };

        IEnumerator ExportAssets(PlayerNet me, PlayerController pc)
        {
            string dir = "Export/Models";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-exportdir") dir = args[i + 1];
            dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(dir);
            ObjExport.Reset();
            var index = new StringBuilder();
            int ok = 0, failed = 0, empty = 0;
            var stage = new GameObject("export stage").transform;
            stage.position = new Vector3(0f, 150f, 0f);

            // ---- helpers ----
            // builds under a fresh root, writes it (name.obj + name.mtl in dir/sub), destroys it; the written files' hash (null: nothing / failed)
            string One(string sub, string name, System.Action<Transform> build, Transform frame = null)
            {
                var root = new GameObject(name).transform;
                root.SetParent(stage, false);
                string hash = null;
                try
                {
                    build(root);
                    var data = ObjExport.Build(frame != null ? frame : root, sub);
                    if (data.parts > 0)
                    {
                        ObjExport.Save(Path.Combine(dir, sub), name, data);
                        hash = data.hash;
                        ok++;
                        index.AppendLine($"{sub}/{name}.obj\t{data.parts} parts\t{data.tris} tris");
                    }
                    else { empty++; Log($"export {sub}/{name}: empty (no model)"); }
                }
                catch (System.Exception e) { failed++; Log($"export {sub}/{name} FAILED: {e.GetType().Name}: {e.Message}"); }
                try { Object.DestroyImmediate(root.gameObject); } catch (System.Exception e) { Log($"export {sub}/{name}: cleanup threw {e.GetType().Name}"); }
                return hash;
            }
            // the same thing in every team's colour; if the colour makes no difference just one file without a team suffix
            void Teams(string sub, string name, System.Action<Transform, int> build)
            {
                var datas = new ObjExport.Data[4];
                for (int team = 0; team < 4; team++)
                {
                    var root = new GameObject(name).transform;
                    root.SetParent(stage, false);
                    try { build(root, team); datas[team] = ObjExport.Build(root, sub); }
                    catch (System.Exception e) { Log($"export {sub}/{name} ({k_TeamLabel[team]}) FAILED: {e.GetType().Name}: {e.Message}"); }
                    try { Object.DestroyImmediate(root.gameObject); } catch { }
                }
                bool same = datas[0] != null && datas[0].parts > 0;
                for (int team = 1; team < 4 && same; team++) same = datas[team] != null && datas[team].hash == datas[0].hash;
                if (same)
                {
                    ObjExport.Save(Path.Combine(dir, sub), name, datas[0]);
                    ok++;
                    index.AppendLine($"{sub}/{name}.obj\t{datas[0].parts} parts\t{datas[0].tris} tris");
                    return;
                }
                for (int team = 0; team < 4; team++)
                {
                    if (datas[team] == null) { failed++; continue; }
                    if (datas[team].parts == 0) { empty++; Log($"export {sub}/{name}_{k_TeamLabel[team]}: empty (no model)"); continue; }
                    ObjExport.Save(Path.Combine(dir, sub), $"{name}_{k_TeamLabel[team]}", datas[team]);
                    ok++;
                    index.AppendLine($"{sub}/{name}_{k_TeamLabel[team]}.obj\t{datas[team].parts} parts\t{datas[team].tris} tris");
                }
            }
            // a private static builder, by name (the few that aren't public)
            void Private(System.Type type, string method, params object[] a)
            {
                var m = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (m == null) throw new System.MissingMethodException(type.Name, method);
                m.Invoke(null, a);
            }
            // a private builder on a component (the few that build from an instance)
            void PrivateOn(object obj, string method, params object[] a)
            {
                var m = obj.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m == null) throw new System.MissingMethodException(obj.GetType().Name, method);
                m.Invoke(obj, a);
            }

            Log($"exporting models to {dir}");

            // ---------------- items ----------------
            foreach (Item it in System.Enum.GetValues(typeof(Item)))
            {
                if (it == Item.None) continue;
                One("items", it.ToString(), t => ItemModels.Create(it, t));
            }
            One("items", "Ball", t => ItemModels.CreateBall(t, 1.24f));
            One("items", "Rocket_flying", t => ItemModels.CreateRocket(t));
            One("items", "Spear_stuck", t => ItemModels.CreateSpearTipForward(t));
            One("items", "Arrow_stuck", t => ItemModels.CreateArrowTipForward(t));
            One("items", "Arrow_flying", t => ArrowProjectile.BuildArrowLook(t));
            One("items", "TimeoutBall", t => TimeoutBall.BuildLook(t));
            yield return null;

            // ---------------- building pieces ----------------
            foreach (PieceType p in System.Enum.GetValues(typeof(PieceType)))
            {
                for (int tier = 0; tier < 3; tier++)
                {
                    int tr = tier;
                    One("structures", $"{p}_{Cfg.TierName(tr)}", t => Structure.CreateVisual(p, tr, t, false, null, out _));
                }
                Teams("structures", $"{p}_{Cfg.TierName(3)}", (t, team) => Structure.CreateVisual(p, 3, t, false, null, out _, team));
            }
            yield return null;

            // ---------------- containers and deployables ----------------
            var kinds = new (byte kind, string name)[]
            {
                (Container.Chest, "Chest"), (Container.Bag, "Bag"), (Container.Airdrop, "AirdropCrate"), (Container.Gamble, "GambleMachine"),
                (Container.Workbench, "Workbench_T1"), (Container.Workbench2, "Workbench_T2"), (Container.SleepBag, "SleepingBag"),
                (Container.Trap, "BearTrap"), (Container.Ladder, "Ladder"), (Container.Turret, "AutoTurret"), (Container.Converter, "Converter"),
                (Container.Barricade, "Barricade"), (Container.FakeDrop, "FakeAirdrop"), (Container.Mine, "LandMine"), (Container.Guardian, "BaseGuardian"),
            };
            foreach (var k in kinds) Teams("deployables", k.name, (t, team) => Container.CreateVisual(k.kind, team, t, null));
            One("deployables", "GambleMachine", t => PrivateOn(t.gameObject.AddComponent<GambleMachine>(), "Build", t));
            Teams("deployables", "Workbench_T1_full", (t, team) => Workbench.BuildModel(t, team, true, 1));
            Teams("deployables", "Workbench_T2_full", (t, team) => Workbench.BuildModel(t, team, true, 2));
            yield return null;

            // ---------------- vehicles ----------------
            var vkinds = new (byte kind, string name, bool unicorn)[] { (Vehicle.Car, "Car", false), (Vehicle.Horse, "Horse", false), (Vehicle.Horse, "Unicorn", true), (Vehicle.Slender, "Slender", false), (3, "Boat", false), (Vehicle.Dummy, "TrainingDummy", false) };
            foreach (var v in vkinds)
                One("vehicles", v.name, t => Vehicle.CreateVisual(v.kind, t, null, out _, out _, out _, out _, new List<Transform>(), new List<Transform>(), v.unicorn));
            yield return null;

            // ---------------- nature ----------------
            var seen = new Dictionary<int, int>(); // pine variant -> first seed
            for (int seed = 1; seed < 400 && seen.Count < 12; seed++) { int k = ResourceNode.PineVariant(seed); if (!seen.ContainsKey(k)) seen[k] = seed; }
            foreach (var kv in seen)
            {
                int seed = kv.Value;
                string vn = ResourceNode.PineVariantName(kv.Key).Replace(' ', '_').Replace('?', 'x');
                One("nature", $"Tree_{vn}_seed{seed}", t => ResourceNode.BuildTreeVisual(t, seed, false));
                One("nature", $"Stump_{vn}_seed{seed}", t =>
                {
                    var tmp = new GameObject("tmp").transform; tmp.SetParent(t, false);
                    ResourceNode.BuildTreeVisual(tmp, seed, false, out float r, out Color bark, out _);
                    Object.DestroyImmediate(tmp.gameObject);
                    Private(typeof(ResourceNode), "BuildStump", t, seed, Mathf.Clamp(r, 0.2f, 1.2f), bark, true);
                    foreach (Transform c in t) c.gameObject.SetActive(true); // (it's built hidden: it shows when the tree's felled)
                });
            }
            for (int s = 1; s <= 3; s++)
            {
                int seed = s * 17;
                One("nature", $"BerryBush_seed{seed}", t => ResourceNode.BuildBerryBush(t, seed, ItemModels.Leaf, ItemModels.Berry));
            }
            One("nature", "AirdropBush", t => ResourceNode.BuildBerryBush(t, 5, ItemModels.Leaf, NetGame.AirdropBerry));
            yield return null;

            // ---------------- the world's things ----------------
            One("world", "UFO", t => { var p = AirdropShip.BuildShip("UFO"); p.Root.transform.SetParent(t, false); });
            Teams("world", "AlienMachine", (t, team) =>
            {
                Private(typeof(MapBuilder), "BuildMachine", t, team, Color.Lerp(Cfg.TeamColor[team], Color.white, 0.35f));
                // (it puts itself at the team's machine spot: bring it home)
                foreach (Transform c in t) { c.localPosition = Vector3.zero; c.localRotation = Quaternion.identity; }
            });
            Teams("world", "UpgradeStation", (t, team) => UpgradeStation.Create(t, team));
            for (int lv = 1; lv <= 3; lv++)
            {
                int level = lv;
                Teams("world", $"WoodMachine_L{level}", (t, team) =>
                {
                    var w = WoodMachine.Create(t, team);
                    w.transform.localPosition = Vector3.zero; w.transform.localRotation = Quaternion.identity; // (it puts itself at the team's spot)
                    PrivateOn(w, "Build", level); // (nothing shows until a team buys a level)
                });
            }
            Teams("world", "EmeraldShop", (t, team) => EmeraldShop.Create(t, team));
            Teams("world", "Flag", (t, team) => WorldDressing.Flag(t, Vector3.zero, Cfg.TeamColor[team], 1f, 0.6f));
            One("world", "LuckyBlock", t => LuckyBlockModel.Build(t, 1f));
            One("world", "LuckyBlock_glow", t => LuckyBlockModel.Build(t, 1f, true));
            One("world", "CentreSign", t => { var g = CentreSign.Build(t); if (g != null) { g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; } });
            Teams("world", "Gadget_BouncyCastle", (t, team) => Private(typeof(GadgetFx), "BuildCastle", t, team, new System.Random(3)));
            One("world", "Gadget_AttentionSnail", t => Private(typeof(GadgetFx), "BuildSnail", t));
            One("world", "Gadget_Clutter", t => Private(typeof(GadgetFx), "BuildClutter", t, new System.Random(3)));
            One("world", "Gadget_StickOfTruth", t => Private(typeof(GadgetFx), "BuildTruth", t, new System.Random(3)));
            One("world", "Gadget_Word", t => Private(typeof(GadgetFx), "BuildWord", t, "ROCK", new System.Random(3)));
            One("world", "Gadget_Smoke", t => Private(typeof(GadgetFx), "BuildSmoke", t, new System.Random(3)));
            yield return null;

            // ---------------- the alien: every team colour, every pose ----------------
            var A = BodyAnimator.Act.None;
            // (name, item, speed m/s, height off the ground, vertical speed, crouch, action, swing, throw, carrying the ball, riding, dead, dance, knock, lounge variant)
            var poses = new (string name, Item item, float speed, float height, float velY, bool crouch, BodyAnimator.Act act, bool swing, bool thrw, bool carry, bool riding, bool dead, float dance, float knock, int lounge)[]
            {
                ("Idle", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Idle_rock", Item.Rock, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Idle_hatchet", Item.Hatchet, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Idle_sword", Item.Sword, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Idle_spear", Item.Spear, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Idle_pistol", Item.Pistol, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Idle_helmet", Item.Hatchet, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Walk", Item.None, 3f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Walk_rock", Item.Rock, 3f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Walk_spear", Item.Spear, 3f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Sprint", Item.None, 7.5f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Sprint_spear", Item.Spear, 7.5f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Crouch", Item.Rock, 0f, 0f, 0f, true, A, false, false, false, false, false, 0f, 0f, -1),
                ("CrouchWalk_pickaxe", Item.Pickaxe, 2.2f, 0f, 0f, true, A, false, false, false, false, false, 0f, 0f, -1),
                ("Jump_up", Item.None, 4f, 1.4f, 5f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Jump_down", Item.None, 4f, 1.4f, -7f, false, A, false, false, false, false, false, 0f, 0f, -1),
                ("Slide", Item.None, 9f, 0f, 0f, true, BodyAnimator.Act.Slide, false, false, false, false, false, 0f, 0f, -1),
                ("Bow_draw", Item.Bow, 0f, 0f, 0f, false, BodyAnimator.Act.BowDraw, false, false, false, false, false, 0f, 0f, -1),
                ("Spear_windup", Item.Spear, 0f, 0f, 0f, false, BodyAnimator.Act.SpearAim, false, false, false, false, false, 0f, 0f, -1),
                ("Spear_throw", Item.Spear, 0f, 0f, 0f, false, A, false, true, false, false, false, 0f, 0f, -1),
                ("Crossbow_aim", Item.Crossbow, 0f, 0f, 0f, false, BodyAnimator.Act.Aim, false, false, false, false, false, 0f, 0f, -1),
                ("Pistol_aim", Item.Pistol, 0f, 0f, 0f, false, BodyAnimator.Act.Aim, false, false, false, false, false, 0f, 0f, -1),
                ("Sniper_aim", Item.Sniper, 0f, 0f, 0f, false, BodyAnimator.Act.Aim, false, false, false, false, false, 0f, 0f, -1),
                ("Eat_berry", Item.Berry, 0f, 0f, 0f, false, BodyAnimator.Act.Eat, false, false, false, false, false, 0f, 0f, -1),
                ("Swing_rock", Item.Rock, 0f, 0f, 0f, false, A, true, false, false, false, false, 0f, 0f, -1),
                ("Swing_hatchet", Item.Hatchet, 0f, 0f, 0f, false, A, true, false, false, false, false, 0f, 0f, -1),
                ("Swing_sword", Item.Sword, 0f, 0f, 0f, false, A, true, false, false, false, false, 0f, 0f, -1),
                ("Ram", Item.Ram, 0f, 0f, 0f, false, BodyAnimator.Act.Ram, false, false, false, false, false, 0f, 0f, -1),
                ("Chainsaw", Item.Chainsaw, 0f, 0f, 0f, false, BodyAnimator.Act.Saw, false, false, false, false, false, 0f, 0f, -1),
                ("CarryBall", Item.None, 0f, 0f, 0f, false, A, false, false, true, false, false, 0f, 0f, -1),
                ("CarryBall_run", Item.None, 4f, 0f, 0f, false, A, false, false, true, false, false, 0f, 0f, -1),
                ("Riding", Item.None, 0f, 0.9f, 0f, false, A, false, false, false, true, false, 0f, 0f, -1),
                ("Dead", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, true, 0f, 0f, -1),
                ("Dance", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 1f, 0f, -1),
                ("Knocked", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 1f, -1),
                ("Lounge_leanBack", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 0),
                ("Lounge_1", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 1),
                ("Lounge_2", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 2),
                ("Lounge_3", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 3),
                ("Lounge_4", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 4),
                ("Lounge_5", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 5),
                ("Lounge_6", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 6),
                ("Lounge_gaming", Item.None, 0f, 0f, 0f, false, A, false, false, false, false, false, 0f, 0f, 7),
            };
            // a flat spot on the ground (the pose code checks it's standing on something)
            var ground = me.transform.position + me.transform.forward * 2f;
            ground.y = MapBuilder.Height(ground.x, ground.z) + 0.02f;
            var hatAnimRoot = (Transform)null; BodyAnimator hatAnim = null;
            for (int team = 0; team < 4; team++)
            {
                string tl = k_TeamLabel[team];
                for (int pi = -1; pi < poses.Length; pi++)
                {
                    string pname = pi < 0 ? "APose_rest" : poses[pi].name;
                    var r = new GameObject($"alien_{tl}_{pname}").transform;
                    r.position = ground + Vector3.up * (pi < 0 ? 0f : poses[pi].height);
                    var anim = BodyAnimator.TryCreate(r, Cfg.ModelWidth, out var model);
                    if (anim == null) { Log("FAIL: rigged alien missing"); break; }
                    var tinted = new List<Material>();
                    PlayerNet.SkinAlien(model, tinted);
                    foreach (var m in tinted)
                    {
                        var c = PlayerNet.TeamTint(m, team);
                        m.SetColor("_BaseColor", c); m.color = c;
                        ObjExport.Hint(m, m.name.Contains("Head") ? $"alien_{tl}_head" : $"alien_{tl}_body");
                    }
                    yield return null; // (the import's Animator is destroyed at the end of the frame it's made: it would overwrite the pose)
                    if (pi >= 0)
                    {
                        var ps = poses[pi];
                        Transform held = null;
                        if (ps.name == "Idle_helmet")
                        {
                            var hm = ItemModels.Create(Item.Helmet, anim.HeadBone);
                            PlayerNet.FitHelmet(hm, anim.HeadBone, r);
                        }
                        if (ps.item != Item.None || ps.carry)
                        {
                            held = new GameObject("held_" + (ps.carry ? "Ball" : ps.item.ToString())).transform;
                            held.SetParent(r, false);
                            if (ps.carry) ItemModels.CreateBall(held, 1.24f); else ItemModels.Create(ps.item, held);
                        }
                        if (ps.lounge >= 0)
                        {
                            for (int k = 0; k < 3; k++) anim.Lounge(ps.lounge, 1.3f);
                        }
                        else
                        {
                            const float dt = 1f / 60f;
                            for (int k = 0; k < 150; k++)
                            {
                                float t = k * dt;
                                anim.TickWithVelocity(new BodyAnimator.Pose
                                {
                                    Crouch = ps.crouch, Holding = ps.item != Item.None, Carrying = ps.carry,
                                    TwoHanded = ps.item == Item.Rock || ps.item == Item.Spear || Cfg.IsRam(ps.item) || ps.item == Item.Chainsaw || ps.item == Item.Crossbow || ps.item == Item.Shotgun || ps.item == Item.TreeCracker,
                                    Action = ps.act, Pitch = 0f, Item = ps.carry ? Item.None : ps.item,
                                    Riding = ps.riding, Dead = ps.dead, DeadTime = t,
                                    Swing = ps.swing ? 0.55f : 0f, Throw = ps.thrw ? 0.5f : 0f,
                                    Dance = ps.dance, Knock = ps.knock,
                                }, dt, r.forward * ps.speed + Vector3.up * ps.velY);
                            }
                        }
                        if (held != null)
                        {
                            if (ps.carry) held.SetPositionAndRotation(r.position + r.forward * 0.6f + Vector3.up * 1.15f, r.rotation);
                            else { anim.GripPose(out var gp, out var gr); held.SetPositionAndRotation(gp, gr); }
                        }
                    }
                    try
                    {
                        var data = ObjExport.Build(r, "aliens");
                        if (data.parts > 0) { ObjExport.Save(Path.Combine(dir, "aliens"), $"alien_{tl}_{pname}", data); ok++; index.AppendLine($"aliens/alien_{tl}_{pname}.obj\t{data.parts} parts\t{data.tris} tris"); }
                        else { failed++; Log($"export aliens/alien_{tl}_{pname}: nothing to write"); }
                    }
                    catch (System.Exception e) { failed++; Log($"export aliens/alien_{tl}_{pname} FAILED: {e.GetType().Name}: {e.Message}"); }
                    if (team == 0 && pi < 0) { hatAnimRoot = r; hatAnim = anim; continue; } // (kept for the hats below)
                    Object.DestroyImmediate(r.gameObject);
                }
                yield return null;
            }
            // the hats (Cosmetics.cs: not switched on in the game, but they're models) on the A-pose alien's head
            if (hatAnim != null)
            {
                for (int id = 1; id < Cosmetics.HatCount; id++)
                {
                    int hid = id;
                    string hn = Cosmetics.HatNames[id].Replace(' ', '_');
                    One("world", $"Hat_{hn}", t =>
                    {
                        var hat = Cosmetics.Wear(hid, t, hatAnim);
                        if (hat == null) return;
                        // (in the head's frame: the hat sits where it would on the head bone)
                        t.position = hatAnim.HeadBone.position; t.rotation = hatAnim.HeadBone.rotation * hatAnim.HeadFrameOffset;
                    });
                }
                Object.DestroyImmediate(hatAnimRoot.gameObject);
            }
            yield return null;

            // ---------------- the first-person hands: three styles, every team colour, a few things held ----------------
            var vm = ViewModel.Last;
            if (vm == null) { Log("FAIL: no first-person view model to export"); }
            else
            {
                int styleWas = GameSettings.HandStyle;
                var handPoses = new (string name, Item item, System.Action set)[]
                {
                    ("rock", Item.Rock, null),
                    ("hatchet", Item.Hatchet, null),
                    ("spear", Item.Spear, null),
                    ("bow_drawn", Item.Bow, () => pc.DebugDraw = 1f),
                    ("pistol", Item.Pistol, null),
                };
                var styles = new (int style, string name)[] { (GameSettings.HandsSquare, "square"), (GameSettings.HandsRound, "round"), (GameSettings.HandsAlien, "alien") };
                var handsRoot = vm.DebugHand(true).parent;
                foreach (var hp in handPoses)
                {
                    for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                    if (hp.item != Item.Rock) me.ServerGive(hp.item, 1);
                    if (hp.item == Item.Bow) me.ServerGive(Item.Arrow, 5);
                    yield return new WaitForSeconds(0.2f);
                    yield return Hold(me, hp.item);
                    yield return new WaitForSeconds(0.4f);
                    foreach (var st in styles)
                    {
                        GameSettings.SetHandStyle(st.style, false);
                        for (int team = 0; team < 4; team++)
                        {
                            string tl = k_TeamLabel[team];
                            hp.set?.Invoke();
                            yield return new WaitForSeconds(0.7f); // (the hands settle into the hold; the bow's draw)
                            vm.SetTeam(Cfg.TeamColor[team]); // (now, after the controller's Update put its own team back: the materials swap at once)
                            // the alien arms' baked shading texture under this team's colour
                            foreach (var rend in handsRoot.GetComponentsInChildren<Renderer>())
                                foreach (var m in rend.sharedMaterials)
                                    if (m != null && m.mainTexture != null && m.mainTexture.name == "AlienArmsAO")
                                        ObjExport.Hint(m, $"arms_{tl}_{(rend.name.Contains("claws") ? "claws" : "skin")}");
                            try
                            {
                                var data = ObjExport.Build(handsRoot, "hands");
                                string n = $"hands_{st.name}_{tl}_{hp.name}";
                                if (data.parts > 0) { ObjExport.Save(Path.Combine(dir, "hands"), n, data); ok++; index.AppendLine($"hands/{n}.obj\t{data.parts} parts\t{data.tris} tris"); }
                                else { failed++; Log($"export hands/{n}: nothing to write"); }
                            }
                            catch (System.Exception e) { failed++; Log($"export hands_{st.name}_{tl}_{hp.name} FAILED: {e.GetType().Name}: {e.Message}"); }
                            pc.DebugDraw = -1f;
                        }
                    }
                }
                GameSettings.SetHandStyle(styleWas, false);
                vm.SetTeam(Cfg.TeamColor[Mathf.Clamp(me.Team.Value, 0, 3)]);
            }

            ObjExport.WriteTints(dir);
            File.WriteAllText(Path.Combine(dir, "index.txt"), index.ToString());
            Object.DestroyImmediate(stage.gameObject);
            Log($"export done: {ok} models written, {failed} failed, {empty} empty, {ObjExport.TintCount} textures to tint, {ObjExport.Skipped} unreadable meshes skipped");
            Check(failed == 0, $"every model exported ({ok} ok, {failed} failed)");
            Check(ok > 300, $"a lot of models exported ({ok})");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }

    /// <summary>
    /// Wavefront OBJ writer for a hierarchy of renderers: every active MeshRenderer and SkinnedMeshRenderer under a root,
    /// in the root's own space (skinned meshes are skinned on the CPU from their bones, so the pose is exact), with an
    /// MTL of the materials' colours (URP _BaseColor), transparency and glow. Unity's left-handed Y-up space is mirrored
    /// (x -> -x, faces re-wound) so the files load right-handed: Blender's importer (-Z forward, Y up) puts them upright
    /// and facing the front view. Textured materials get their texture baked with the material's tint into a PNG next to
    /// the OBJ (listed in tints.txt for Tools/tint_textures.py to make), so what's in Blender is what's drawn.
    /// </summary>
    public static class ObjExport
    {
        public class Data { public string obj, mtl, hash; public int parts, tris; }

        static readonly Dictionary<Material, string> s_Hints = new Dictionary<Material, string>();
        /// <summary>out file (relative to the export dir) -> (texture in Resources, multiplier)</summary>
        static readonly Dictionary<string, (string src, Color mul)> s_Tints = new Dictionary<string, (string, Color)>();
        static readonly Dictionary<string, string> s_Sources = new Dictionary<string, string>
        {
            { "Alien2", "Alien/Alien2.png" }, { "Alien_Body", "Alien/Alien_Body.png" }, { "Alien_Head", "Alien/Alien_Head.png" },
            { "Alien_Body_Suited", "Alien/Alien_Body_Suited.png" }, { "AlienArmsAO", "FpsArms/AlienArmsAO.png" },
        };
        static readonly HashSet<string> s_Unknown = new HashSet<string>();
        public static int Skipped;
        public static int TintCount => s_Tints.Count;
        static readonly CultureInfo k_C = CultureInfo.InvariantCulture;

        public static void Reset() { s_Hints.Clear(); s_Tints.Clear(); s_Unknown.Clear(); Skipped = 0; }

        /// <summary>Names the tinted texture file this material's texture is baked to (file stem).</summary>
        public static void Hint(Material m, string stem) { if (m != null) s_Hints[m] = stem; }

        static string F(float v) => v.ToString("0.#####", k_C);
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b)));

        static Color BaseColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.color;
            return Color.white;
        }

        /// <summary>The material's MTL name and block (the block only the first time it's seen).</summary>
        /// <summary>A material drawn from the mesh's vertex colours (the Painted shader: vertex colour x _Tint).</summary>
        static bool UsesVertexColors(Material m) => m != null && m.HasProperty("_Tint") && !m.HasProperty("_BaseColor");

        static string MatName(Material m, string sub, Dictionary<string, string> blocks)
        {
            bool vc = UsesVertexColors(m);
            var c = vc ? m.GetColor("_Tint") : BaseColor(m);
            var tex = m != null ? m.mainTexture as Texture2D : null;
            string map = null;
            if (tex != null)
            {
                if (s_Sources.TryGetValue(tex.name, out var src))
                {
                    string stem = s_Hints.TryGetValue(m, out var h) ? h : $"{tex.name}_{Hex(c)}";
                    string file = stem + ".png";
                    string key = sub + "/" + file;
                    if (s_Tints.TryGetValue(key, out var have) && have.mul != c)
                    {
                        // (the same name wanted in another tint: give this one its own)
                        stem = $"{stem}_{Hex(c)}"; file = stem + ".png"; key = sub + "/" + file;
                    }
                    s_Tints[key] = (src, c);
                    map = file;
                }
                else if (s_Unknown.Add(tex.name)) Debug.Log($"[AUTOTEST] export: no source known for texture '{tex.name}' (material {m.name}): colour only");
            }
            Color emis = Color.black;
            if (m != null && m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION")) emis = m.GetColor("_EmissionColor");
            // (a colour pushed past 1 glows: the excess as emission)
            if (map == null && (c.r > 1f || c.g > 1f || c.b > 1f)) emis += new Color(Mathf.Max(0, c.r - 1f), Mathf.Max(0, c.g - 1f), Mathf.Max(0, c.b - 1f));
            bool glow = emis.r > 0.01f || emis.g > 0.01f || emis.b > 0.01f;
            string name = map != null ? "tex_" + Path.GetFileNameWithoutExtension(map) : (vc ? "vc_" : "c_") + Hex(c);
            if (c.a < 0.999f) name += "_a" + Mathf.RoundToInt(c.a * 100f).ToString(k_C);
            if (glow) name += "_e" + Hex(emis);
            if (!blocks.ContainsKey(name))
            {
                var sb = new StringBuilder();
                sb.Append("newmtl ").Append(name).Append('\n');
                var kd = map != null || vc ? Color.white : c; // (textured: the tint is baked into the texture; vertex coloured: on the vertices)
                sb.Append("Kd ").Append(F(Mathf.Clamp01(kd.r))).Append(' ').Append(F(Mathf.Clamp01(kd.g))).Append(' ').Append(F(Mathf.Clamp01(kd.b))).Append('\n');
                sb.Append("Ka 0 0 0\nKs 0.04 0.04 0.04\nNs 12\nillum 2\n");
                if (c.a < 0.999f) sb.Append("d ").Append(F(c.a)).Append('\n');
                if (glow) sb.Append("Ke ").Append(F(Mathf.Clamp01(emis.r))).Append(' ').Append(F(Mathf.Clamp01(emis.g))).Append(' ').Append(F(Mathf.Clamp01(emis.b))).Append('\n');
                if (map != null) sb.Append("map_Kd ").Append(map).Append('\n');
                blocks[name] = sb.ToString();
            }
            return name;
        }

        /// <summary>Skins a mesh on the CPU: every vertex (and normal) from its bones' current transforms, in world space.</summary>
        static bool CpuSkin(SkinnedMeshRenderer smr, Mesh mesh, out Vector3[] verts, out Vector3[] norms)
        {
            verts = null; norms = null;
            var bones = smr.bones;
            var bind = mesh.bindposes;
            var bw = mesh.boneWeights;
            if (bones == null || bind == null || bw == null || bw.Length != mesh.vertexCount || bind.Length != bones.Length || bones.Length == 0) return false;
            var mats = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) mats[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;
            var v = mesh.vertices;
            var n = mesh.normals;
            bool hasN = n != null && n.Length == v.Length;
            verts = new Vector3[v.Length];
            norms = hasN ? new Vector3[v.Length] : null;
            for (int i = 0; i < v.Length; i++)
            {
                var w = bw[i];
                var p = Vector3.zero; var nn = Vector3.zero;
                void Acc(int b, float wt)
                {
                    if (wt <= 0f) return;
                    p += mats[b].MultiplyPoint3x4(v[i]) * wt;
                    if (hasN) nn += mats[b].MultiplyVector(n[i]) * wt;
                }
                Acc(w.boneIndex0, w.weight0); Acc(w.boneIndex1, w.weight1); Acc(w.boneIndex2, w.weight2); Acc(w.boneIndex3, w.weight3);
                verts[i] = p;
                if (hasN) norms[i] = nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up;
            }
            return true;
        }

        /// <summary>The OBJ and MTL text for everything drawn under root, in root's space.</summary>
        public static Data Build(Transform root, string sub)
        {
            var o = new StringBuilder();
            var blocks = new Dictionary<string, string>();
            var inv = root.worldToLocalMatrix;
            int vBase = 1, vtBase = 1, vnBase = 1, parts = 0, tris = 0;
            var used = new HashSet<string>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh mesh; Vector3[] wv; Vector3[] wn;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = smr.sharedMesh;
                    if (mesh == null) continue;
                    if (!mesh.isReadable) { Skipped++; continue; }
                    if (!CpuSkin(smr, mesh, out wv, out wn))
                    {
                        // (no bones: bake it as Unity would, in the renderer's own frame)
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        var tm = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                        wv = baked.vertices; wn = baked.normals;
                        for (int i = 0; i < wv.Length; i++) wv[i] = tm.MultiplyPoint3x4(wv[i]);
                        if (wn != null) for (int i = 0; i < wn.Length; i++) wn[i] = tm.MultiplyVector(wn[i]).normalized;
                        mesh = baked;
                    }
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    if (!mesh.isReadable) { Skipped++; continue; }
                    var m = r.localToWorldMatrix;
                    var nm = m.inverse.transpose;
                    wv = mesh.vertices; wn = mesh.normals;
                    for (int i = 0; i < wv.Length; i++) wv[i] = m.MultiplyPoint3x4(wv[i]);
                    if (wn != null && wn.Length == wv.Length) for (int i = 0; i < wn.Length; i++) { var n = nm.MultiplyVector(wn[i]); wn[i] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up; }
                    else wn = null;
                }
                else continue;
                if (wv.Length == 0 || mesh.subMeshCount == 0) continue;
                // into the root's frame, mirrored to right-handed
                var ninv = inv.inverse.transpose;
                var uv = mesh.uv;
                bool hasUv = uv != null && uv.Length == wv.Length;
                var mats = r.sharedMaterials;
                var vcols = mesh.colors;
                Color vtint = Color.white;
                bool hasVc = vcols != null && vcols.Length == wv.Length && mats != null && mats.Length > 0 && UsesVertexColors(mats[0]);
                if (hasVc) vtint = mats[0].GetColor("_Tint");
                bool hasN = wn != null && wn.Length == wv.Length;
                string oname = r.name.Replace(' ', '_');
                int k = 1; string baseName = oname;
                while (!used.Add(oname)) oname = baseName + "_" + (++k).ToString(k_C);
                o.Append("o ").Append(oname).Append('\n');
                for (int i = 0; i < wv.Length; i++)
                {
                    var p = inv.MultiplyPoint3x4(wv[i]);
                    o.Append("v ").Append(F(-p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z));
                    if (hasVc) { var vc = vcols[i] * vtint; o.Append(' ').Append(F(Mathf.Clamp01(vc.r))).Append(' ').Append(F(Mathf.Clamp01(vc.g))).Append(' ').Append(F(Mathf.Clamp01(vc.b))); }
                    o.Append('\n');
                }
                if (hasUv) for (int i = 0; i < uv.Length; i++) o.Append("vt ").Append(F(uv[i].x)).Append(' ').Append(F(uv[i].y)).Append('\n');
                if (hasN)
                    for (int i = 0; i < wn.Length; i++)
                    {
                        var n = ninv.MultiplyVector(wn[i]); n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                        o.Append("vn ").Append(F(-n.x)).Append(' ').Append(F(n.y)).Append(' ').Append(F(n.z)).Append('\n');
                    }
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats != null && mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    o.Append("usemtl ").Append(MatName(mat, sub, blocks)).Append('\n');
                    var t = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        // (mirrored: the winding goes the other way to keep the faces facing out)
                        int a = t[i], b = t[i + 2], c = t[i + 1];
                        o.Append("f ");
                        Corner(o, a, hasUv, hasN, vBase, vtBase, vnBase); o.Append(' ');
                        Corner(o, b, hasUv, hasN, vBase, vtBase, vnBase); o.Append(' ');
                        Corner(o, c, hasUv, hasN, vBase, vtBase, vnBase); o.Append('\n');
                        tris++;
                    }
                }
                vBase += wv.Length;
                if (hasUv) vtBase += uv.Length;
                if (hasN) vnBase += wn.Length;
                parts++;
            }
            var mtl = new StringBuilder("# Alien Rock Game - materials\n");
            foreach (var kv in blocks) mtl.Append(kv.Value).Append('\n');
            var d = new Data { obj = o.ToString(), mtl = mtl.ToString(), parts = parts, tris = tris };
            using (var sha = System.Security.Cryptography.SHA1.Create())
                d.hash = System.BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(d.obj + "\n" + d.mtl)));
            return d;
        }

        static void Corner(StringBuilder o, int i, bool uv, bool n, int vb, int vtb, int vnb)
        {
            o.Append((i + vb).ToString(k_C));
            if (uv || n) o.Append('/');
            if (uv) o.Append((i + vtb).ToString(k_C));
            if (n) o.Append('/').Append((i + vnb).ToString(k_C));
        }

        static string Safe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        public static void Save(string dir, string name, Data d)
        {
            Directory.CreateDirectory(dir);
            name = Safe(name);
            File.WriteAllText(Path.Combine(dir, name + ".obj"), $"# Alien Rock Game - {name} (metres, Y up, right-handed; made by the game: -autotest exportassets)\nmtllib {name}.mtl\n" + d.obj, Encoding.ASCII);
            File.WriteAllText(Path.Combine(dir, name + ".mtl"), d.mtl, Encoding.ASCII);
        }

        /// <summary>tints.txt: every tinted texture to make (out file, source under Resources, r g b multiplier).</summary>
        public static void WriteTints(string dir)
        {
            var sb = new StringBuilder("# out\tsource (Assets/Game/Resources/...)\tr\tg\tb\n");
            foreach (var kv in s_Tints) sb.Append(kv.Key).Append('\t').Append(kv.Value.src).Append('\t').Append(F(kv.Value.mul.r)).Append('\t').Append(F(kv.Value.mul.g)).Append('\t').Append(F(kv.Value.mul.b)).Append('\n');
            File.WriteAllText(Path.Combine(dir, "tints.txt"), sb.ToString(), new UTF8Encoding(false));
        }
    }
}
