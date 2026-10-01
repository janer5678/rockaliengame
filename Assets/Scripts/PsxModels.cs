using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The PSX graphics mode's models (Resources/PsxModels, made from the PSX asset pack by Tools/psx_convert.py). Only the
    /// looks change: everything is first built exactly as in Normal graphics (same colliders, same sizes, same pivots),
    /// then, in PSX mode, its renderers are switched off and the PSX model is put in their place, turned and scaled to
    /// fill the same space. Normal graphics never touch any of this.
    ///
    /// Models come in up = +Y, front (blade, muzzle, face) = +Z, centred on their bounds. psx_manifest.txt lists each
    /// model's textures by material slot (or "#rrggbb" for a plain colour). tex_&lt;pack&gt;_&lt;row&gt;&lt;col&gt; are the
    /// tiling surface textures (grass, wood, metal, concrete, cobblestone, asphalt).
    /// </summary>
    /// <summary>A PSX look on an object: on while PSX graphics are on, off (the original look back) otherwise.</summary>
    public class PsxLook : MonoBehaviour
    {
        System.Action m_Apply, m_Revert;
        bool m_On;

        public void Init(System.Action apply, System.Action revert)
        {
            m_Apply = apply;
            m_Revert = revert;
            GameSettings.GraphicsChanged += Refresh;
            Refresh();
        }

        void OnDestroy() => GameSettings.GraphicsChanged -= Refresh;

        public void Refresh()
        {
            if (this == null) return;
            bool want = PsxModels.On;
            if (want == m_On) return;
            m_On = want;
            if (want) m_Apply?.Invoke();
            else m_Revert?.Invoke();
        }
    }

    public static class PsxModels
    {
        /// <summary>Only in PSX graphics (not Normal, not the AI PSX test) - and never while item icons are drawn.</summary>
        public static bool On => GameSettings.PsxGraphics && !Suppress;
        public static bool Suppress;

        public enum Fit
        {
            Uniform,    // keep its shape; the longest side matches, centred
            Stretch,    // fill the box exactly (each axis on its own), centred
            Ground,     // keep its shape; as wide / deep as the box (the larger), standing on the box's bottom
            GroundTall, // keep its shape; as tall as the box, standing on its bottom
            Grip,       // keep its shape; the longest side matches, and the point the hand holds stays where it was
                        // (wherever the old look's origin sat inside its box, the same spot of the new one goes there)
        }

        static Dictionary<string, string[]> s_Manifest;
        static readonly Dictionary<string, GameObject> s_Prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Material> s_Mats = new Dictionary<string, Material>();
        static Material s_Base;

        static Dictionary<string, string[]> Manifest
        {
            get
            {
                if (s_Manifest != null) return s_Manifest;
                s_Manifest = new Dictionary<string, string[]>();
                var ta = Resources.Load<TextAsset>("PsxModels/psx_manifest");
                if (ta == null) return s_Manifest;
                foreach (var line in ta.text.Split('\n'))
                {
                    int bar = line.IndexOf('|');
                    if (bar > 0) s_Manifest[line.Substring(0, bar).Trim()] = line.Substring(bar + 1).Trim().Split(';');
                }
                return s_Manifest;
            }
        }

        public static bool Has(string key) => Manifest.ContainsKey(key);

        static Material Base
        {
            get
            {
                if (s_Base == null) s_Base = Resources.Load<Material>("PsxTrees/PsxCutout");
                return s_Base;
            }
        }

        /// <summary>A PSX material for a texture in Resources/PsxModels (or "#rrggbb"), shared.</summary>
        public static Material Mat(string tex)
        {
            if (string.IsNullOrEmpty(tex)) tex = "#b3b3b3";
            if (s_Mats.TryGetValue(tex, out var m) && m != null) return m;
            if (tex[0] == '#' && ColorUtility.TryParseHtmlString(tex, out var c)) m = new Material(Art.Mat(c)) { name = "psx " + tex };
            else
            {
                var t = Resources.Load<Texture2D>("PsxModels/" + tex);
                m = Base != null ? new Material(Base) : new Material(Art.Mat(Color.white));
                m.name = "psx " + tex;
                if (t != null)
                {
                    m.SetTexture("_BaseMap", t);
                    m.mainTexture = t;
                }
                m.SetColor("_BaseColor", Color.white);
            }
            s_Mats[tex] = m;
            return m;
        }

        /// <summary>A fresh copy of a PSX model under `parent` (at its origin), with its materials. Null if there's no such model.</summary>
        public static GameObject Spawn(string key, Transform parent)
        {
            if (!Manifest.TryGetValue(key, out var texs)) return null;
            if (!s_Prefabs.TryGetValue(key, out var prefab) || prefab == null)
            {
                prefab = Resources.Load<GameObject>("PsxModels/" + key);
                s_Prefabs[key] = prefab;
            }
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = "psx " + key;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = Mat(texs[Mathf.Min(i, texs.Length - 1)]);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            return go;
        }

        /// <summary>Bounds of these renderers in `space`'s local coordinates.</summary>
        public static bool LocalBoundsOf(List<Renderer> rs, Transform space, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in rs)
            {
                if (r == null || !LocalBounds(r.transform, space, out var rb, null, null, true)) continue;
                if (!any) { b = rb; any = true; }
                else b.Encapsulate(rb);
            }
            return any;
        }

        /// <summary>Bounds of a transform's renderers, in `space`'s local coordinates (skips the ones listed).</summary>
        public static bool LocalBounds(Transform root, Transform space, out Bounds b, ICollection<Renderer> skip = null, Transform keep = null, bool selfOnly = false)
        {
            b = default;
            bool any = false;
            foreach (var r in selfOnly ? root.GetComponents<Renderer>() : root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || (skip != null && skip.Contains(r)) || (keep != null && r.transform.IsChildOf(keep))) continue;
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : r is SkinnedMeshRenderer sk ? sk.sharedMesh : null;
                if (mesh == null) continue;
                var mb = mesh.bounds;
                var toSpace = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toSpace.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any;
        }

        /// <summary>
        /// PSX mode: hides what `root` shows now and puts the PSX model in its place, turned by `euler` and fitted into the
        /// space the old look took up (renderers under `keep` stay as they are). Colliders and everything else stay as they
        /// were. It follows the graphics setting from then on: switch to Normal and the old look comes back.
        /// </summary>
        public static void Replace(Transform root, string key, Fit fit = Fit.Uniform, Vector3 euler = default, float scale = 1f, Transform keep = null, System.Action<GameObject> onSpawn = null)
        {
            if (Suppress || root == null || !Has(key)) return;
            GameObject model = null;
            List<Renderer> hidden = null;
            // what it looks like now is measured once, here: anything attached later (an item in a hand) isn't part of it
            var old = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !(r is LineRenderer) && !r.name.StartsWith("psx") && (keep == null || !r.transform.IsChildOf(keep))) old.Add(r);
            if (!LocalBoundsOf(old, root, out var target)) return;
            Look(root.gameObject, () =>
            {
                hidden = new List<Renderer>();
                model = Spawn(key, root);
                if (model == null) return;
                foreach (var r in old) if (r.enabled) { r.enabled = false; hidden.Add(r); }
                FitInto(model.transform, root, target, fit, euler, scale);
                onSpawn?.Invoke(model);
            }, () =>
            {
                if (model != null) Object.Destroy(model);
                if (hidden != null) foreach (var r in hidden) if (r != null) r.enabled = true;
                model = null;
                hidden = null;
            });
        }

        /// <summary>PSX mode: these renderers wear a tiling PSX surface (tex_&lt;tile&gt;, one tile every `metres`) instead of their colour.</summary>
        public static void Retexture(GameObject owner, IEnumerable<Renderer> renderers, System.Func<Renderer, string> tileFor, float metres = 2f)
        {
            if (Suppress || owner == null) return;
            var list = new List<Renderer>(renderers);
            var saved = new Dictionary<Renderer, Material[]>();
            Look(owner, () =>
            {
                foreach (var r in list)
                {
                    if (r == null) continue;
                    var tile = tileFor(r);
                    if (tile == null) continue;
                    saved[r] = r.sharedMaterials;
                    var mats = new Material[r.sharedMaterials.Length];
                    var m = Tiled(tile, r, metres);
                    for (int i = 0; i < mats.Length; i++) mats[i] = m;
                    r.sharedMaterials = mats;
                }
            }, () =>
            {
                foreach (var kv in saved) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
                saved.Clear();
            });
        }

        /// <summary>A tiling surface material sized for a box-shaped renderer (its two biggest sides set the repeats).</summary>
        public static Material Tiled(string tile, Renderer r, float metres)
        {
            var s = r.transform.lossyScale;
            float[] d = { Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z) };
            System.Array.Sort(d);
            return Tiled(tile, d[2] / metres, d[1] / metres);
        }

        public static Material Tiled(string tile, float u, float v)
        {
            u = Mathf.Max(0.5f, Mathf.Round(u * 2f) / 2f);
            v = Mathf.Max(0.5f, Mathf.Round(v * 2f) / 2f);
            string key = $"tex_{tile}@{u}x{v}";
            if (s_Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Mat("tex_" + tile)) { name = "psx " + key };
            m.mainTextureScale = new Vector2(u, v);
            m.SetTextureScale("_BaseMap", new Vector2(u, v));
            s_Mats[key] = m;
            return m;
        }

        /// <summary>Puts a PSX look on `owner` now if PSX graphics are on, and takes it back off / puts it on again as the graphics change.</summary>
        public static void Look(GameObject owner, System.Action apply, System.Action revert)
        {
            var l = owner.AddComponent<PsxLook>();
            l.Init(apply, revert);
        }

        /// <summary>Turns, scales and moves a spawned model (a child of `space`) so it fills `target` (in `space`'s coordinates).</summary>
        public static void FitInto(Transform model, Transform space, Bounds target, Fit fit, Vector3 euler = default, float scale = 1f)
        {
            model.localRotation = Quaternion.Euler(euler);
            model.localScale = Vector3.one;
            model.localPosition = Vector3.zero;
            if (!LocalBounds(model, space, out var have)) return;
            Vector3 hs = Vector3.Max(have.size, Vector3.one * 1e-4f), ts = target.size;
            Vector3 s;
            switch (fit)
            {
                case Fit.Stretch:
                    // per axis, in the model's own (turned) frame
                    var inv = Quaternion.Inverse(model.localRotation);
                    var tsm = Abs(inv * ts);
                    var hsm = Abs(inv * hs);
                    s = new Vector3(tsm.x / Mathf.Max(1e-4f, hsm.x), tsm.y / Mathf.Max(1e-4f, hsm.y), tsm.z / Mathf.Max(1e-4f, hsm.z));
                    break;
                case Fit.Ground:
                    s = Vector3.one * Mathf.Max(ts.x, ts.z) / Mathf.Max(hs.x, hs.z);
                    break;
                case Fit.GroundTall:
                    s = Vector3.one * ts.y / hs.y;
                    break;
                default:
                    s = Vector3.one * Mathf.Max(ts.x, Mathf.Max(ts.y, ts.z)) / Mathf.Max(hs.x, Mathf.Max(hs.y, hs.z));
                    break;
            }
            model.localScale = s * scale;
            LocalBounds(model, space, out have);
            var want = target.center;
            if (fit == Fit.Ground || fit == Fit.GroundTall) want.y = target.min.y + have.extents.y;
            var delta = want - have.center;
            if (fit == Fit.Grip)
            {
                // where `space`'s origin (the grip) sat in the old box, as a fraction of it, lands on the origin again
                var ts0 = Vector3.Max(target.size, Vector3.one * 1e-4f);
                var frac = new Vector3(Mathf.Clamp01(-target.min.x / ts0.x), Mathf.Clamp01(-target.min.y / ts0.y), Mathf.Clamp01(-target.min.z / ts0.z));
                delta = -(have.min + Vector3.Scale(frac, have.size));
            }
            // (the model is a child of `space`, or of something under it with no scale of its own in between)
            model.position += space.TransformVector(delta);
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>Which PSX model an item uses (null: it keeps its own look in PSX mode too).</summary>
        public static string ItemKey(Item i)
        {
            switch (i)
            {
                case Item.Rock: return "rock1";
                case Item.Stone: return "rock4";
                case Item.Spear: return "spear";
                case Item.Bow: return "bow";
                case Item.Crossbow: return "crossbow";
                case Item.Arrow: return "arrow";
                case Item.Hatchet: return "hatchet";
                case Item.TreeCracker: return "treecracker";
                case Item.Sword: return "sword";
                case Item.C4: return "c4";
                case Item.DeathWand: return "deathwand";
                case Item.GiantStaff: return "giantstaff";
                case Item.Meat: return "meat";
                case Item.Revolver: return "revolver";
                case Item.Shotgun: return "shotgun";
                case Item.RocketLauncher: return "rocket";
                case Item.Chest: return "chest";
                case Item.Barrier: return "barrier";
                default: return null;
            }
        }

        /// <summary>Every model a PSX item is turned by, on top of the shared convention (most need nothing).</summary>
        /// <summary>How an item's PSX model fills its space: boxy things keep the box, everything else is held by its grip.</summary>
        public static Fit ItemFit(Item i) => i == Item.Chest || i == Item.C4 ? Fit.Stretch : Fit.Grip;

        public static Vector3 ItemEuler(Item i) => i == Item.Crossbow ? new Vector3(0, 180, 0) : Vector3.zero; // (the pack's crossbow comes in facing backwards)
    }
}
