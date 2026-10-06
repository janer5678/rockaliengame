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

        /// <summary>Icon for a build wheel slice (see PlayerController.WheelOptions): a flat white pictogram, made on first use.</summary>
        public static Texture2D Wheel(int option)
        {
            if (s_Wheel.TryGetValue(option, out var t) && t != null) return t;
            t = MakeWheelIcon(option);
            if (t != null) s_Wheel[option] = t;
            return t;
        }
        static bool s_Tried;

        public static Texture2D Get(Item i) => s_Icons.TryGetValue(i, out var t) ? t : null;

        static readonly Item[] k_TeamArrowItems = { Item.FortifyBuff, Item.WoodGenBuff };
        static readonly Dictionary<(Item, int), Texture2D> s_TeamIcons = new Dictionary<(Item, int), Texture2D>();
        /// <summary>The base upgrades' arrow colour for a team: a light shade of its colour (not the orange upgrade colour).</summary>
        public static Color UpgradeArrowColor(int team) => Color.Lerp(Cfg.TeamColor[Mathf.Clamp(team, 0, 3)], Color.white, 0.45f);
        /// <summary>A team's own copy of an icon (the base upgrades, their arrow in the team's light shade); otherwise the usual one.</summary>
        public static Texture2D GetForTeam(Item i, int team)
            => team >= 0 && team < 4 && s_TeamIcons.TryGetValue((i, team), out var t) ? t : Get(i);

        /// <summary>Call outside OnGUI (e.g. from Update) once graphics are up.</summary>
        public static void EnsureRendered()
        {
            if (s_Tried) return;
            s_Tried = true;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            // icons always show the Normal look (they're made once, whatever the graphics)
            PsxModels.Suppress = true;
            try { RenderAll(); }
            finally { PsxModels.Suppress = false; }
        }

        static void RenderAll()
        {

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
            // the base upgrades again for each team, their up arrow in a light shade of the team's colour (UPGRADES screen)
            try
            {
                foreach (var item in k_TeamArrowItems)
                    for (int team = 0; team < 4; team++)
                    {
                        ItemModels.ArrowTint = UpgradeArrowColor(team);
                        var model = ItemModels.Create(item, rig.transform);
                        PoseForIcon(item, model.transform);
                        var tex = Snap(model, item + "_team" + team);
                        if (tex != null) s_TeamIcons[(item, team)] = tex;
                    }
            }
            finally { ItemModels.ArrowTint = null; }
            // hand-drawn icons in Resources/Icons replace the rendered ones (e.g. "hatchet.png")
            foreach (Item item in System.Enum.GetValues(typeof(Item)))
            {
                var drawn = Resources.Load<Texture2D>("Icons/" + item.ToString().ToLowerInvariant());
                if (drawn != null) s_Icons[item] = drawn;
            }

            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rig);
        }

        // ---------------- build wheel icons: flat white pictograms ----------------
        // Each slice is one plain white silhouette of what it builds (drawn here, not rendered: no graphics needed), with
        // thin see-through gaps where two faces of a piece meet so the shapes read as 3D: a thick slab (foundation), a thin
        // one (ceiling), an upright panel (wall), the panel with a big opening (window) or a door gap (doorway), a staircase,
        // and a trash can (demolish). The wheel tints nothing - it draws them white (faded while a slice is locked).

        const int WheelSize = 128;

        static Texture2D MakeWheelIcon(int option)
        {
            var opts = PlayerController.WheelOptions;
            if (option < 0 || option >= opts.Length) return null;
            var o = opts[option];
            var ops = new List<(Vector2[] poly, bool add)>();
            void Add(params Vector2[] p) => ops.Add((p, true));
            void Cut(params Vector2[] p) => ops.Add((p, false));
            Vector2 V(float x, float y) => new Vector2(x, y);
            Vector2[] Rect(float x0, float y0, float x1, float y1) => new[] { V(x0, y0), V(x1, y0), V(x1, y1), V(x0, y1) };
            Vector2[] Line(Vector2 a, Vector2 b, float w)
            {
                var n = new Vector2(-(b - a).y, (b - a).x).normalized * (w * 0.5f);
                return new[] { a + n, b + n, b - n, a - n };
            }
            const float gap = 0.035f; // the see-through seams between faces
            // a box seen from the front, a little from the right and above: front face x0..x1, y0..y1, depth (dx, dy)
            void Box(float x0, float y0, float x1, float y1, float dx, float dy)
            {
                Add(Rect(x0, y0, x1, y1));                                              // front
                Add(V(x0, y1), V(x1, y1), V(x1 + dx, y1 + dy), V(x0 + dx, y1 + dy));     // top
                Add(V(x1, y0), V(x1 + dx, y0 + dy), V(x1 + dx, y1 + dy), V(x1, y1));     // side
                Cut(Line(V(x0 - 0.02f, y1), V(x1, y1), gap));
                Cut(Line(V(x1, y0 - 0.02f), V(x1, y1), gap));
                Cut(Line(V(x1, y1), V(x1 + dx + 0.02f, y1 + dy + 0.02f * dy / Mathf.Max(0.001f, dx)), gap));
            }
            if (o.Demolish)
            {
                // the trash can: a tapered bin with three slots, a lid with a handle on top
                Add(V(0.27f, 0.1f), V(0.73f, 0.1f), V(0.78f, 0.68f), V(0.22f, 0.68f));
                Cut(Rect(0.355f, 0.2f, 0.405f, 0.58f));
                Cut(Rect(0.475f, 0.2f, 0.525f, 0.58f));
                Cut(Rect(0.595f, 0.2f, 0.645f, 0.58f));
                Add(Rect(0.16f, 0.72f, 0.84f, 0.8f));
                Add(Rect(0.39f, 0.8f, 0.61f, 0.9f));
                Cut(Rect(0.44f, 0.8f, 0.56f, 0.855f));
            }
            else switch (o.Piece)
            {
                case PieceType.Foundation:
                    Box(0.1f, 0.3f, 0.66f, 0.52f, 0.24f, 0.18f); // a thick slab on the ground
                    break;
                case PieceType.Floor:
                    Box(0.1f, 0.44f, 0.66f, 0.5f, 0.24f, 0.18f); // a thin slab, up in the air
                    break;
                case PieceType.Wall:
                    Box(0.18f, 0.12f, 0.72f, 0.8f, 0.1f, 0.08f);
                    break;
                case PieceType.Window:
                    Box(0.18f, 0.12f, 0.72f, 0.8f, 0.1f, 0.08f);
                    Cut(Rect(0.26f, 0.36f, 0.64f, 0.7f));         // the big opening
                    Add(Rect(0.435f, 0.36f, 0.465f, 0.7f));       // a bar down the middle
                    Add(Rect(0.26f, 0.515f, 0.64f, 0.545f));     // and one across it
                    break;
                case PieceType.Doorway:
                    Box(0.18f, 0.12f, 0.72f, 0.8f, 0.1f, 0.08f);
                    Cut(Rect(0.34f, 0.08f, 0.56f, 0.6f));         // the door gap, down to the ground
                    break;
                case PieceType.Stairs:
                {
                    // a staircase from the side: four steps up to the right
                    var p = new List<Vector2> { V(0.12f, 0.12f), V(0.86f, 0.12f), V(0.86f, 0.86f) };
                    for (int s = 3; s >= 0; s--)
                    {
                        float x = 0.12f + s * 0.185f, y = 0.12f + (s + 1) * 0.185f;
                        p.Add(V(x, y));
                        if (s > 0) p.Add(V(x, y - 0.185f));
                    }
                    Add(p.ToArray());
                    break;
                }
                default:
                    Add(Rect(0.2f, 0.2f, 0.8f, 0.8f));
                    break;
            }
            // rasterise: 4x4 samples a pixel for smooth edges; each sample goes through the adds and cuts in order
            const int N = WheelSize, SS = 4;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < SS; sy++)
                for (int sx = 0; sx < SS; sx++)
                {
                    var q = new Vector2((x + (sx + 0.5f) / SS) / N, (y + (sy + 0.5f) / SS) / N);
                    bool inside = false;
                    foreach (var (poly, add) in ops)
                        if (Inside(poly, q)) inside = add;
                    if (inside) hit++;
                }
                px[y * N + x] = new Color32(255, 255, 255, (byte)(hit * 255 / (SS * SS)));
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "wheel_" + o.Label, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); // (row 0 is the bottom: y up, as drawn above)
            tex.Apply();
            return tex;
        }

        /// <summary>Even-odd point in polygon (any shape, convex or not).</summary>
        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    c = !c;
            return c;
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
                case Item.Hatchet:
                    // laid out like Rust's hatchet icon: the handle across from bottom left to top right, the head at the top
                    // with its blade turned up and out to the side
                    t.localRotation = Quaternion.Euler(0, 0, 42) * Quaternion.Euler(0, 90, 0); break;
                case Item.Spear: case Item.Arrow: case Item.Pickaxe: case Item.DeathWand: case Item.GiantStaff: case Item.TreeCracker: case Item.Sword:
                    t.localRotation = Quaternion.Euler(0, 0, -40); break;
                case Item.Bow:
                    t.localRotation = Quaternion.Euler(0, 90, -35); break;
                case Item.C4:
                    t.localRotation = Quaternion.Euler(-30, 30, 0); break;
                case Item.Helmet:
                    t.localRotation = Quaternion.Euler(0, 150, 0); break;
                case Item.Car:
                case Item.Boat:
                case Item.Saddle:
                case Item.Wallhack:
                case Item.Jetpack:
                    t.localRotation = Quaternion.Euler(0, 140, 0); break;
                case Item.Ram:
                case Item.Chainsaw:
                case Item.Crossbow:
                case Item.Sniper:
                case Item.Pistol:
                case Item.Revolver:
                case Item.Shotgun:
                case Item.PortalGun:
                case Item.RocketLauncher:
                    t.localRotation = Quaternion.Euler(0, 55, 0); break;
                case Item.BuildingPlan:
                    t.localRotation = Quaternion.Euler(20, 0, 0); break;
            }
        }
    }
}
