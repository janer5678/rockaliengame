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

            foreach (Item item in System.Enum.GetValues(typeof(Item)))
            {
                if (item == Item.None) continue;
                var model = ItemModels.Create(item, rig.transform);
                PoseForIcon(item, model.transform);
                var rends = model.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { Object.DestroyImmediate(model); continue; }
                foreach (var r in rends) { r.gameObject.layer = Layer; r.shadowCastingMode = ShadowCastingMode.Off; }
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                float radius = b.extents.magnitude;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
                var dir = new Vector3(0.35f, 0.3f, -1f).normalized;
                camGo.transform.position = b.center - dir * dist;
                camGo.transform.rotation = Quaternion.LookRotation(dir);

                if (!Render(cam, rt)) { Object.DestroyImmediate(model); break; }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "icon_" + item };
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                s_Icons[item] = tex;
                Object.DestroyImmediate(model);
            }
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rig);
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
                case Item.Spear: case Item.Arrow: case Item.Hatchet: case Item.Pickaxe: case Item.DeathWand:
                    t.localRotation = Quaternion.Euler(0, 0, -40); break;
                case Item.Bow:
                    t.localRotation = Quaternion.Euler(0, 90, -35); break;
                case Item.C4:
                    t.localRotation = Quaternion.Euler(-30, 30, 0); break;
                case Item.Helmet:
                    t.localRotation = Quaternion.Euler(0, 150, 0); break;
                case Item.Ram:
                case Item.Chainsaw:
                    t.localRotation = Quaternion.Euler(0, 55, 0); break;
                case Item.BuildingPlan:
                    t.localRotation = Quaternion.Euler(20, 0, 0); break;
            }
        }
    }
}
