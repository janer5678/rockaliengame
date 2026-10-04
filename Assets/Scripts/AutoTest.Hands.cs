using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest hands -host -solo -shotdir DIR (windowed): the first-person hands holding every item, in the key moments of
    /// each animation, photographed three times - with the block hands the game uses (hands_X_blocky), with the alien
    /// clawed hands they replaced (hands_X_alien) and with the original box arms (hands_X_box) - so they can be compared.
    /// Then a few in PSX graphics, a few shaded smooth (Settings > Display > SHADING: handssmooth_X), and checks that only
    /// the local first-person arms are the arm models.
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
            if (ViewModel.Last != null) Log("block hand pieces (model space, unbent):\n" + ViewModel.Last.DebugRest());
            Check(ViewModel.Last != null && ViewModel.Last.DebugIsBlocky, "the first-person hands are the square block hands");
            var contacts = new List<ContactResult>();
            bool closeUps = System.Array.IndexOf(args, "-handsclose") >= 0; // (close-ups of each hand as well)
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
                foreach (int mode in new[] { 2, 1, 0 })
                {
                    ViewModel.DebugArms = mode;
                    yield return new WaitForSeconds(0.25f);
                    Shot($"hands_{p.name}_{(mode == 1 ? "box" : mode == 0 ? "blocky" : "alien")}");
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
                foreach (int mode in new[] { 2, 0 })
                {
                    ViewModel.DebugArms = mode;
                    yield return new WaitForSeconds(0.15f);
                    Shot($"handsside_{p.name}_{(mode == 0 ? "blocky" : "alien")}");
                    yield return new WaitForEndOfFrame();
                }
                // close-ups of each hand that's in view, from outside, from in front and from below
                ViewModel.DebugArms = 0;
                if (closeUps && ViewModel.Last != null)
                    foreach (bool right in new[] { true, false })
                    {
                        var h = ViewModel.Last.DebugHand(right);
                        if (h == null || !h.gameObject.activeInHierarchy || main.transform.InverseTransformPoint(h.position).y < -0.75f) continue;
                        float sx = right ? 1f : -1f;
                        side.nearClipPlane = 0.01f;
                        side.fieldOfView = 40f;
                        foreach (var v in new[] { ("out", new Vector3(0.36f * sx, 0.1f, 0.02f)), ("front", new Vector3(0.06f * sx, 0.08f, 0.4f)), ("under", new Vector3(0.12f * sx, -0.34f, 0.12f)) })
                        {
                            side.transform.position = h.position + main.transform.TransformVector(v.Item2);
                            side.transform.LookAt(h.position, main.transform.up);
                            yield return new WaitForEndOfFrame();
                            Shot($"handsclose_{p.name}_{(right ? "R" : "L")}_{v.Item1}");
                            yield return null;
                        }
                    }
                Destroy(side.gameObject);
                contacts.Add(HandsContact(p.name));
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
                HandsContact("psx_" + it.ToString().ToLower());
            }
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.5f);

            // shade smooth (Settings > Display > SHADING > Hands & items): what's held becomes its smooth copy and the
            // block hands' corners are lit smoothly; off puts the very same meshes back
            foreach (var it in new[] { Item.Hatchet, Item.Rock, Item.Crossbow })
            {
                for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
                if (it != Item.Rock) me.ServerGive(it, 1);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                yield return new WaitForSeconds(0.5f);
                var held = new List<Renderer>();
                ViewModel.Last?.DebugHeld(held);
                var before = new List<Mesh>();
                foreach (var r in held) before.Add(r.GetComponent<MeshFilter>()?.sharedMesh);
                Shot("handssmooth_" + it.ToString().ToLower() + "_off");
                yield return new WaitForEndOfFrame();
                GameSettings.SmoothHands.Set(true, false);
                yield return new WaitForSeconds(0.3f);
                int smooth = 0, total = 0;
                foreach (var r in held) { var m = r.GetComponent<MeshFilter>()?.sharedMesh; if (m == null || !m.isReadable) continue; total++; if (SmoothShade.IsSmooth(m)) smooth++; }
                Shot("handssmooth_" + it.ToString().ToLower() + "_on");
                yield return new WaitForEndOfFrame();
                Check(total > 0 && smooth == total, $"shade smooth: every held {it} mesh is its smooth copy ({smooth}/{total})");
                Check(ViewModel.Last != null && ViewModel.Last.DebugBlockySmooth(), $"shade smooth: the block hands' corners are lit smoothly ({it})");
                GameSettings.SmoothHands.Set(false, false);
                yield return new WaitForSeconds(0.3f);
                bool back = true;
                for (int i = 0; i < held.Count; i++) if (held[i] && held[i].GetComponent<MeshFilter>()?.sharedMesh != before[i]) back = false;
                Check(back && !ViewModel.Last.DebugBlockySmooth(), $"shade smooth off: the {it}'s own meshes are back and the block hands are flat again");
            }

            // shade smooth (Aliens & crowd): the alien player models get their smooth copies, and their own meshes back after
            {
                var hooks = FindObjectsByType<SmoothShadeHook>(FindObjectsSortMode.None);
                var skins = new List<SkinnedMeshRenderer>();
                foreach (var h in hooks) if (h.Aliens) skins.AddRange(h.GetComponentsInChildren<SkinnedMeshRenderer>(true));
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

            // the arm models are only ever the local first-person arms (the block hands, and the hidden alien claws)
            int arms = 0, blocks = 0, elsewhere = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool alien = false, block = false, vm = false;
                for (var t = r.transform; t != null; t = t.parent) { alien |= t.name.StartsWith("psx alienarm"); block |= t.name.StartsWith("blocky arm"); vm |= t.name == "ViewModel"; }
                if (!alien && !block) continue;
                if (!vm) elsewhere++;
                else if (block) blocks++;
                else arms++;
            }
            Check(arms == 2 && blocks == 2 && elsewhere == 0, $"the arm models are the first-person arms only ({blocks} block hands and {arms} alien arms in the view model, {elsewhere} elsewhere)");

            // how the claws sit on what they hold: no claw / thumb / palm in the item (the worst depth), and each digit's
            // gap to the item's surface (a fist or a cradle closes onto it)
            var sb = new System.Text.StringBuilder("pose,worst_pen_mm,worst_pair,self_pen_mm,self_pair,R_upper_gap_mm,R_long_gap_mm,R_thumb_gap_mm,R_palm_gap_mm,L_upper_gap_mm,L_long_gap_mm,L_thumb_gap_mm,L_palm_gap_mm\n");
            float worst = 0f; string worstPose = "-";
            foreach (var c in contacts)
            {
                sb.Append(c.Csv()).Append('\n');
                if (c.Pen > worst) { worst = c.Pen; worstPose = c.Pose + " " + c.PenPair; }
            }
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "hands_contact.csv"), sb.ToString()); } catch (System.Exception e) { Log("couldn't write hands_contact.csv: " + e.Message); }
            Log("hands contact summary:\n" + sb);
            Check(worst < HandsPenLimit, $"no claw, thumb or palm sinks into what the hands hold (worst {worst * 1000f:0.0} mm: {worstPose})");
            Log("hands done");
            Application.Quit(0);
        }

        /// <summary>Deepest a claw / thumb / palm may sink into a held item before the hands test fails (metres).</summary>
        const float HandsPenLimit = 0.004f;

        class ContactResult
        {
            public string Pose, PenPair = "-", SelfPair = "-";
            public float Pen, Self;
            public readonly Dictionary<string, float> Gap = new Dictionary<string, float>();
            static string Mm(float v) => v < 0f ? "-" : (v * 1000f).ToString("0.0");
            float G(string k) => Gap.TryGetValue(k, out var v) ? v : -1f;
            public string Csv() => $"{Pose},{Mm(Pen)},{PenPair},{Mm(Self)},{SelfPair},{Mm(G("R.upper"))},{Mm(G("R.long"))},{Mm(G("R.thumb"))},{Mm(G("R.palm"))},{Mm(G("L.upper"))},{Mm(G("L.long"))},{Mm(G("L.thumb"))},{Mm(G("L.palm"))}";
        }

        /// <summary>
        /// Measures how the alien hands sit on what they hold this frame: each rigid piece of the hand (palm, forearm, every
        /// claw and thumb segment) and each part of the item become temporary convex colliders (for this one call, triggers on
        /// the Ignore Raycast layer, gone before physics runs again); logs the deepest overlap (the distance needed to pull
        /// them apart), overlaps of the hand with itself (thumb into the claws, claws into the palm) and each digit's gap to
        /// the item's surface.
        /// </summary>
        ContactResult HandsContact(string pose)
        {
            var res = new ContactResult { Pose = pose };
            var vm = ViewModel.Last;
            if (vm == null) return res;
            var pieces = new List<(string name, Vector3[] pts, int[] tris)>();
            vm.DebugPieces(pieces);
            var held = new List<Renderer>();
            vm.DebugHeld(held);
            var tmp = new GameObject("hands contact check");
            var meshes = new List<Mesh>();
            Collider Hull(string n, Vector3[] pts, int[] tris)
            {
                if (pts.Length < 4) return null;
                var m = new Mesh { name = n };
                m.vertices = pts;
                if (tris.Length < 3)
                {
                    tris = new int[(pts.Length - 2) * 3];
                    for (int i = 0; i < pts.Length - 2; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = i + 2; }
                }
                m.triangles = tris;
                m.RecalculateBounds();
                meshes.Add(m);
                var go = new GameObject(n) { layer = 2 };
                go.transform.SetParent(tmp.transform, false);
                var mc = go.AddComponent<MeshCollider>();
                mc.convex = true;
                mc.isTrigger = true;
                mc.sharedMesh = m;
                return mc;
            }
            var parts = new List<(string name, Collider c)>();
            foreach (var r in held)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                string n = (r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name + "#" + r.transform.GetSiblingIndex();
                if (mesh.isReadable)
                {
                    // each separate piece of the mesh on its own (a PSX model is one mesh of several pieces)
                    var m = r.transform.localToWorldMatrix;
                    var meshPieces = ViewModel.MeshPieces(mesh);
                    for (int pi = 0; pi < meshPieces.Count; pi++)
                    {
                        var v = meshPieces[pi].v;
                        var w = new Vector3[v.Length];
                        for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
                        string pn = meshPieces.Count > 1 ? n + "." + pi : n;
                        var c = Hull(pn, w, meshPieces[pi].t);
                        if (c != null) parts.Add((pn, c));
                    }
                }
                else
                {
                    // (a mesh we can't read: its box)
                    var go = new GameObject(n) { layer = 2 };
                    go.transform.SetParent(tmp.transform, false);
                    go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                    go.transform.localScale = r.transform.lossyScale;
                    var bc = go.AddComponent<BoxCollider>();
                    bc.isTrigger = true;
                    bc.center = mesh.bounds.center;
                    bc.size = mesh.bounds.size;
                    parts.Add((n + "(box)", bc));
                }
            }
            var hand = new List<(string name, Collider c, Vector3[] pts)>();
            foreach (var p in pieces)
            {
                var c = Hull(p.name, p.pts, p.tris);
                if (c != null) hand.Add((p.name, c, p.pts));
            }
            Physics.SyncTransforms();

            float Pen(Collider a, Collider b)
            {
                if (!a.bounds.Intersects(b.bounds)) return 0f;
                return Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float d) ? d : 0f;
            }
            var bad = new System.Text.StringBuilder();
            foreach (var h in hand)
                foreach (var it in parts)
                {
                    float d = Pen(h.c, it.c);
                    if (d > 0.002f) bad.Append($" {h.name}x{it.name}={d * 1000f:0.0}");
                    if (d > res.Pen) { res.Pen = d; res.PenPair = h.name + " x " + it.name; }
                }
            // the hand against itself: the thumb over the claws, the claws curling into the palm
            foreach (var a in hand)
                foreach (var b in hand)
                {
                    if (a.name[0] != b.name[0]) continue; // (the same hand)
                    string an = a.name.Substring(2), bn = b.name.Substring(2);
                    bool pair = (an.StartsWith("thumb") && an != "thumb0" && (bn.StartsWith("upper") || bn.StartsWith("long")))
                        || ((an == "upper1" || an == "upper2" || an == "long1" || an == "long2" || an == "thumb1" || an == "thumb2") && bn == "palm");
                    if (!pair) continue;
                    float d = Pen(a.c, b.c);
                    if (d > res.Self) { res.Self = d; res.SelfPair = a.name + " x " + bn; }
                }
            // each digit's (and the palm's) gap to the item
            foreach (var h in hand)
            {
                string key = h.name;
                if (char.IsDigit(key[key.Length - 1])) key = key.Substring(0, key.Length - 1);
                if (key.EndsWith(".arm")) continue;
                float best = res.Gap.TryGetValue(key, out var g0) ? g0 : 9f;
                foreach (var it in parts)
                {
                    if (it.c.bounds.SqrDistance(h.c.bounds.center) > 0.25f) continue;
                    foreach (var pt in h.pts)
                    {
                        var cp = Physics.ClosestPoint(pt, it.c, it.c.transform.position, it.c.transform.rotation);
                        best = Mathf.Min(best, (cp - pt).magnitude);
                    }
                }
                res.Gap[key] = best;
            }
            foreach (var k in new List<string>(res.Gap.Keys)) if (res.Gap[k] >= 9f) res.Gap.Remove(k);
            DestroyImmediate(tmp);
            foreach (var m in meshes) Destroy(m);
            var gaps = new System.Text.StringBuilder();
            foreach (var kv in res.Gap) gaps.Append($" {kv.Key}={kv.Value * 1000f:0.0}");
            Log($"grip {pose}: R {vm.DebugSolve(true)} | L {vm.DebugSolve(false)}");
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(ShotDir(), $"grip_{pose}.json"), $"{{\"R\":{vm.DebugOutlines(true)},\"L\":{vm.DebugOutlines(false)}}}");
            }
            catch (System.Exception e) { Log("couldn't write the grip outlines: " + e.Message); }
            Log($"contact {pose}: deepest {res.Pen * 1000f:0.0} mm ({res.PenPair}), self {res.Self * 1000f:0.0} mm ({res.SelfPair}); gaps mm:{gaps}; over 2 mm:{(bad.Length > 0 ? bad.ToString() : " none")} [{parts.Count} item parts, {hand.Count} hand pieces]");
            return res;
        }
    }
}
