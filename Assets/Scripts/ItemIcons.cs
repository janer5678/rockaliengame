using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>Renders every item model once into a small transparent texture for the hotbar, inventory and crafting UI.</summary>
    public static class ItemIcons
    {
        const int Size = 128, Layer = 31;
        static readonly Dictionary<Item, Texture2D> s_Icons = new Dictionary<Item, Texture2D>();
        static readonly Dictionary<int, Texture2D> s_Wheel = new Dictionary<int, Texture2D>();

        /// <summary>Icon for a build wheel slice (see PlayerController.WheelOptions).</summary>
        public static Texture2D Wheel(int option) => s_Wheel.TryGetValue(option, out var t) ? t : null;
        static bool s_Tried;

        public static Texture2D Get(Item i) => s_Icons.TryGetValue(i, out var t) ? t : null;

        /// <summary>Call outside OnGUI (e.g. from Update) once graphics are up.</summary>
        public static void EnsureRendered()
        {
            if (s_Tried) return;
            s_Tried = true;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;

            var rig = new GameObject("IconRig");
            rig.transform.position = new Vector3(0, -500, 0);
            var camGo = new GameObject("IconCam");
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 25f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 20f;
            var lightGo = new GameObject("IconLight");
            lightGo.transform.SetParent(rig.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(40, -30, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.cullingMask = 1 << Layer;

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;

            Texture2D Snap(GameObject model, string name)
            {
                var rends = model.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { Object.DestroyImmediate(model); return null; }
                foreach (var r in rends) { r.gameObject.layer = Layer; r.shadowCastingMode = ShadowCastingMode.Off; }
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float radius = b.extents.magnitude;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
                var dir = new Vector3(0.35f, 0.3f, -1f).normalized;
                camGo.transform.position = b.center - dir * dist;
                camGo.transform.rotation = Quaternion.LookRotation(dir);
                cam.farClipPlane = dist + radius * 2f + 1f;
                Render(cam, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "icon_" + name };
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Object.DestroyImmediate(model);
                return tex;
            }

            foreach (Item item in System.Enum.GetValues(typeof(Item)))
            {
                if (item == Item.None) continue;
                var model = ItemModels.Create(item, rig.transform);
                PoseForIcon(item, model.transform);
                var tex = Snap(model, item.ToString());
                if (tex != null) s_Icons[item] = tex;
            }
            // hand-drawn icons in Resources/Icons replace the rendered ones (e.g. "hatchet.png")
            foreach (Item item in System.Enum.GetValues(typeof(Item)))
            {
                var drawn = Resources.Load<Texture2D>("Icons/" + item.ToString().ToLowerInvariant());
                if (drawn != null) s_Icons[item] = drawn;
            }

            // build wheel: each piece as it looks in the world, plus demolish (a trash can) and upgrade (a stone wall)
            var opts = PlayerController.WheelOptions;
            for (int i = 0; i < opts.Length; i++)
            {
                var holder = new GameObject("piece");
                holder.transform.SetParent(rig.transform, false);
                var o = opts[i];
                if (o.Demolish) BuildTrashCan(holder.transform);
                else Structure.CreateVisual(o.Piece, o.Upgrade ? 1 : 0, holder.transform, false, null, out _);
                holder.transform.localRotation = Quaternion.Euler(0, o.Piece == PieceType.Stairs ? 140f : 20f, 0);
                var tex = Snap(holder, o.Label);
                if (tex != null) s_Wheel[i] = tex;
            }
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rig);
        }

        /// <summary>A metal bin with a lid and ribs (the demolish icon).</summary>
        static void BuildTrashCan(Transform t)
        {
            var metal = new Color(0.72f, 0.74f, 0.78f);
            var dark = new Color(0.45f, 0.47f, 0.5f);
            Art.Part(t, Art.Cylinder, metal, new Vector3(0, 0.6f, 0), new Vector3(0.9f, 0.6f, 0.9f));        // body
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Art.Box(t, dark, new Vector3(Mathf.Sin(a) * 0.455f, 0.6f, Mathf.Cos(a) * 0.455f), new Vector3(0.06f, 1.05f, 0.04f), new Vector3(0, i * 45f, 0)); // ribs
            }
            Art.Part(t, Art.Cylinder, dark, new Vector3(0, 1.22f, 0), new Vector3(1.02f, 0.05f, 1.02f));    // lid
            Art.Part(t, Art.Cylinder, metal, new Vector3(0, 1.3f, 0), new Vector3(0.9f, 0.04f, 0.9f));
            Art.Box(t, dark, new Vector3(0, 1.42f, 0), new Vector3(0.4f, 0.08f, 0.1f));                   // handle
            Art.Box(t, dark, new Vector3(-0.17f, 1.37f, 0), new Vector3(0.06f, 0.12f, 0.1f));
            Art.Box(t, dark, new Vector3(0.17f, 1.37f, 0), new Vector3(0.06f, 0.12f, 0.1f));
        }

        static bool Render(Camera cam, RenderTexture rt)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req))
            {
                RenderPipeline.SubmitRenderRequest(cam, req);
                return true;
            }
            cam.Render();
            return true;
        }

        /// <summary>Lay long things diagonally so they read well in a square icon.</summary>
        static void PoseForIcon(Item i, Transform t)
        {
            switch (i)
            {
                case Item.Spear: case Item.Arrow: case Item.Hatchet: case Item.Pickaxe: case Item.DeathWand: case Item.GiantStaff:
                    t.localRotation = Quaternion.Euler(0, 0, -40); break;
                case Item.Bow:
                    t.localRotation = Quaternion.Euler(0, 90, -35); break;
                case Item.C4:
                    t.localRotation = Quaternion.Euler(-30, 30, 0); break;
                case Item.Helmet:
                    t.localRotation = Quaternion.Euler(0, 150, 0); break;
                case Item.Car:
                case Item.Saddle:
                case Item.Wallhack:
                case Item.Jetpack:
                    t.localRotation = Quaternion.Euler(0, 140, 0); break;
                case Item.Ram:
                case Item.Chainsaw:
                case Item.Crossbow:
                case Item.Sniper:
                case Item.PortalGun:
                case Item.RocketLauncher:
                    t.localRotation = Quaternion.Euler(0, 55, 0); break;
                case Item.BuildingPlan:
                    t.localRotation = Quaternion.Euler(20, 0, 0); break;
            }
        }
    }
}
