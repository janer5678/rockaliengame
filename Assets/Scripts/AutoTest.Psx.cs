using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>-autotest psxmodels -host -solo -psx -shotdir DIR: the PSX models next to the Normal look they replace.</summary>
    public partial class AutoTest
    {
        IEnumerator PsxModelShots(PlayerNet me, PlayerController pc)
        {
            string dir = ShotDir();
            GameSettings.SetGraphics(1, false);
            yield return new WaitForSeconds(0.5f);
            Check(PsxModels.Has("sword") && PsxModels.Has("machine"), "PSX models loaded");
            // which way the converted models face: the revolver's grip (most of its bulk) is behind the barrel
            {
                var g = PsxModels.Spawn("revolver", null);
                var mf = g.GetComponentInChildren<MeshFilter>();
                var b = mf.sharedMesh.bounds;
                var v = mf.sharedMesh.vertices;
                Vector3 avg = Vector3.zero;
                foreach (var p in v) avg += p;
                avg /= Mathf.Max(1, v.Length);
                Log($"revolver mesh bounds {b.center} {b.size}, average vertex {avg} (grip side)");
                Destroy(g);
            }
            // a clear spot: our bedrock, looking out over the base
            var team = me.Team.Value;
            float yaw = Cfg.SpawnYaw(team);
            pc.LocalTeleport(Cfg.SpawnPos(team), yaw);
            pc.SetLook(yaw, 0f);
            yield return new WaitForSeconds(0.6f);
            var rot = Quaternion.Euler(0, yaw, 0);
            var eye = me.EyePos;
            var items = new List<Item>();
            foreach (Item i in System.Enum.GetValues(typeof(Item))) if (PsxModels.ItemKey(i) != null) items.Add(i);
            int perShot = 3;
            for (int s = 0; s * perShot < items.Count; s++)
            {
                var holder = new GameObject("psx showcase");
                for (int k = 0; k < perShot && s * perShot + k < items.Count; k++)
                {
                    var it = items[s * perShot + k];
                    for (int row = 0; row < 2; row++)
                    {
                        var slot = new GameObject(it + (row == 0 ? " normal" : " psx")).transform;
                        slot.SetParent(holder.transform, false);
                        slot.position = eye + rot * new Vector3((k - (perShot - 1) / 2f) * 0.9f, row == 0 ? 0.5f : -0.55f, 2.6f);
                        slot.rotation = rot * Quaternion.Euler(0, -60, 0); // three-quarter view: front of the item to our left
                        PsxModels.Suppress = row == 0;
                        ItemModels.Create(it, slot);
                        PsxModels.Suppress = false;
                    }
                }
                yield return new WaitForSeconds(0.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"psxitems_{s}.png"));
                Log($"shot psxitems_{s}: {string.Join(", ", items.GetRange(s * perShot, Mathf.Min(perShot, items.Count - s * perShot)))}");
                yield return new WaitForSeconds(0.3f);
                Destroy(holder);
            }
            // the crossbow from the side (front to the left): Normal in the middle, the four ways the PSX one could be turned around it
            {
                var holder = new GameObject("crossbow check");
                var eulers = new[] { Vector3.zero, new Vector3(0, 0, 180), new Vector3(0, 180, 0), new Vector3(0, 180, 180) };
                for (int k = -1; k < 4; k++)
                {
                    var slot = new GameObject("x" + k).transform;
                    slot.SetParent(holder.transform, false);
                    slot.position = eye + rot * new Vector3(k < 0 ? 0f : (k % 2 == 0 ? -0.55f : 0.55f), k < 0 ? 0.05f : (k < 2 ? 0.45f : -0.3f), 1.8f);
                    slot.rotation = rot * Quaternion.Euler(0, -90, 0);
                    PsxModels.Suppress = true;
                    var normal = ItemModels.Create(Item.Crossbow, slot);
                    PsxModels.Suppress = false;
                    if (k < 0) continue;
                    PsxModels.LocalBounds(normal.transform, normal.transform, out var nb);
                    var m = PsxModels.Spawn("crossbow", normal.transform);
                    PsxModels.FitInto(m.transform, normal.transform, nb, PsxModels.Fit.Grip, eulers[k]);
                    foreach (var r in normal.GetComponentsInChildren<Renderer>()) if (!r.name.StartsWith("psx")) r.enabled = false;
                }
                yield return new WaitForSeconds(0.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "psxcrossbow_side.png"));
                Log("crossbow variants: top-left (0,0,0), top-right (0,0,180), bottom-left (0,180,0), bottom-right (0,180,180)");
                yield return new WaitForSeconds(0.3f);
                Destroy(holder);
            }
            yield return PsxWorldShots(me, pc, dir);
            Log("psxmodels done");
            Application.Quit(0);
        }

        IEnumerator PsxWorldShots(PlayerNet me, PlayerController pc, string dir)
        {
            int team = me.Team.Value;
            var g = NetGame.Instance;
            void Shot(string n) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, n + ".png")); Log("shot " + n); }
            void Look(Vector3 from, Vector3 at)
            {
                var d = at - from;
                pc.LocalTeleport(from, Quaternion.LookRotation(new Vector3(d.x, 0, d.z)).eulerAngles.y);
                var e = Quaternion.LookRotation(at - (from + Vector3.up * 1.6f)).eulerAngles;
                pc.SetLook(e.y, e.x > 180f ? e.x - 360f : e.x);
            }
            // the machine on the bedrock, and the mountains behind
            var back = Cfg.BackDir(team);
            Look(Cfg.BaseCenter[team] - back * 7f + Vector3.up * 0.1f, Cfg.MachinePos(team) + Vector3.up * 1.2f);
            yield return new WaitForSeconds(1f);
            Shot("psxworld_machine");
            yield return new WaitForEndOfFrame();
            // building pieces: a wall of every tier, a doorway, a high external wall and a chest
            var side = Vector3.Cross(Vector3.up, back);
            var row = Cfg.BaseCenter[team] - back * 11f;
            Structure Piece(PieceType t, Vector3 at, int tier)
            {
                var go = Instantiate(Bootstrap.I.structurePrefab, at, Quaternion.LookRotation(-back));
                var st = go.GetComponent<Structure>();
                st.ServerInit(t, team, default, false);
                go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                if (tier > 0) st.ServerUpgrade(tier);
                return st;
            }
            for (int tier = 0; tier < 4; tier++) Piece(PieceType.Wall, row + side * (-6f + tier * 3.2f), tier);
            Piece(PieceType.Doorway, row + side * 7f, 0);
            Piece(PieceType.Foundation, row - back * 4f + side * -4f, 1);
            Piece(PieceType.Barrier, row + side * 12f - back * 2f, 0);
            var cgo = Instantiate(Bootstrap.I.containerPrefab, row - back * 3f + side * 3f, Quaternion.LookRotation(back));
            cgo.GetComponent<Container>().ServerInit(Container.Chest, team, Cfg.ChestSlots, null);
            cgo.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
            yield return new WaitForSeconds(0.6f);
            Look(row - back * 11f + side * 2f + Vector3.up * 0.1f, row + side * 2f + Vector3.up * 1.5f);
            yield return new WaitForSeconds(1f);
            Shot("psxworld_building");
            yield return new WaitForEndOfFrame();
            // out in the wild: a rock, a bush, a tree and a horse
            ResourceNode Nearest(byte kind)
            {
                ResourceNode best = null;
                foreach (var n in ResourceNode.All)
                    if (n != null && n.Kind.Value == kind && (best == null || (n.transform.position - me.transform.position).sqrMagnitude < (best.transform.position - me.transform.position).sqrMagnitude)) best = n;
                return best;
            }
            var rock = Nearest(ResourceNode.Boulder);
            if (rock != null)
            {
                var rp = rock.transform.position;
                var away = (Cfg.BaseCenter[team] - rp); away.y = 0; away.Normalize();
                Vehicle.ServerSpawn(Vehicle.Horse, rp + Vector3.Cross(Vector3.up, away) * 4f, 90f);
                yield return new WaitForSeconds(0.5f);
                Look(rp + away * 7f + Vector3.up * 0.1f, rp + Vector3.up * 1f);
                yield return new WaitForSeconds(1f);
                Shot("psxworld_rock_horse");
                yield return new WaitForEndOfFrame();
            }
            var bush = Nearest(ResourceNode.Bush);
            if (bush != null)
            {
                var bp = bush.transform.position;
                Look(bp + new Vector3(3f, 0.1f, 3f), bp + Vector3.up * 0.5f);
                yield return new WaitForSeconds(1f);
                Shot("psxworld_bush");
                yield return new WaitForEndOfFrame();
            }
            var tree = Nearest(ResourceNode.Tree);
            if (tree != null)
            {
                var tp = tree.transform.position;
                var away = (Cfg.BaseCenter[team] - tp); away.y = 0; away.Normalize();
                tree.ServerHarvest(5, false, tp + away * 3f);
                Look(tp + away * 10f + Vector3.up * 0.1f, tp + Vector3.up * 4f);
                yield return new WaitForSeconds(1f);
                Shot("psxworld_tree");
                yield return new WaitForEndOfFrame();
                int shots = 0;
                foreach (var n in ResourceNode.All)
                {
                    if (n == null || n.Kind.Value != ResourceNode.Tree || shots >= 4) continue;
                    var np = n.transform.position;
                    var aw = (me.transform.position - np); aw.y = 0;
                    if (aw.magnitude > 60f) continue;
                    aw = Quaternion.Euler(0, shots * 37f, 0) * aw.normalized;
                    n.ServerHarvest(5, false, np + aw * 3f);
                    yield return new WaitForSeconds(0.3f);
                    if (!n.TryGetSpot(out var sp, out var sn)) continue;
                    Look(sp + sn * 2.4f - Vector3.up * 1.0f, sp);
                    yield return new WaitForSeconds(0.8f);
                    Shot("psxworld_tree_x" + shots);
                    Log($"tree x{shots}: " + n.XDebug());
                    yield return new WaitForEndOfFrame();
                    shots++;
                }
            }
            // a corpse and blood
            {
                var spot = Cfg.BaseCenter[team] - back * 4f + side * 2f + Vector3.up * Cfg.BaseY;
                var holder = new GameObject("corpse").transform;
                holder.position = spot;
                var sk = PsxModels.Spawn("skeleton", holder);
                PsxModels.FitInto(sk.transform, holder, new Bounds(new Vector3(0, 0.15f, -0.45f), new Vector3(0.7f, 0.3f, 1.8f)), PsxModels.Fit.Uniform, new Vector3(-90, 0, 0));
                for (int i = 0; i < 4; i++) Fx.Blood(spot + Vector3.up * 0.5f + side * (i * 0.4f - 0.6f), Vector3.down, i == 0);
                Look(spot - back * 3.5f + Vector3.up * 0.1f, spot);
                yield return new WaitForSeconds(1f);
                Shot("psxworld_corpse_blood");
                yield return new WaitForEndOfFrame();
            }
            // first person: the PSX arms holding things
            pc.LocalTeleport(Cfg.SpawnPos(team), Cfg.SpawnYaw(team));
            pc.SetLook(Cfg.SpawnYaw(team), 5f);
            var held = new List<Item>();
            foreach (Item i in System.Enum.GetValues(typeof(Item))) if (PsxModels.ItemKey(i) != null && i != Item.Stone && i != Item.Rock) held.Add(i);
            for (int i = 0; i < Cfg.PlayerSlots; i++) me.Inv[i] = default;
            foreach (var it in held)
            {
                for (int i = 0; i < Cfg.HotbarSize; i++) me.Inv[i] = default;
                me.ServerGive(it, it == Item.Barrier ? 2 : 1, it == Item.Revolver ? 3 : 0);
                yield return new WaitForSeconds(0.2f);
                yield return Hold(me, it);
                GameSettings.SetGraphics(1, false);
                yield return new WaitForSeconds(0.7f);
                Shot("psxfp_" + it.ToString().ToLower());
                yield return new WaitForEndOfFrame();
                GameSettings.SetGraphics(0, false);
                yield return new WaitForSeconds(0.5f);
                Shot("nfp_" + it.ToString().ToLower());
                yield return new WaitForEndOfFrame();
                GameSettings.SetGraphics(1, false);
            }
            for (int i = 0; i < Cfg.HotbarSize; i++) me.Inv[i] = default;
            me.HeldSlot.Value = Cfg.HotbarSize - 1; // empty: the rock
            yield return new WaitForSeconds(0.6f);
            Shot("psxfp_rock");
            yield return new WaitForEndOfFrame();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.name.StartsWith("psx rock")) continue;
                string path = r.name;
                for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Log("rock renderer: " + path + " layer " + r.gameObject.layer + " shadows " + r.shadowCastingMode);
            }
            // back to Normal graphics: everything is exactly as it was
            GameSettings.SetGraphics(0, false);
            yield return new WaitForSeconds(0.8f);
            Shot("normalfp_rock");
            yield return new WaitForEndOfFrame();
            int psxLeft = 0;
            var left = new List<string>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && r.gameObject.activeInHierarchy && r.name.StartsWith("psx") && r.name != "psx blood" && !r.name.StartsWith("psx alienarm") && r.transform.root.name != "corpse") // (the alien hands are always the model)
                {
                    psxLeft++;
                    if (left.Count < 4) left.Add((r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name);
                }
            Check(psxLeft == 0, $"back in Normal graphics no PSX models are showing ({psxLeft}: {string.Join(", ", left)})");
            Look(Cfg.BaseCenter[team] - back * 7f + Vector3.up * 0.1f, Cfg.MachinePos(team) + Vector3.up * 1.2f);
            yield return new WaitForSeconds(1f);
            Shot("normalworld_machine");
            yield return new WaitForEndOfFrame();
        }
    }
}
