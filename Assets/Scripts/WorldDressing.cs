using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Normal graphics dressing for Plains and Highlands: castle walls round the map (stone blocks, a corbelled parapet
    /// with battlements, towers along each side with team banners, round corner towers with roofs and waving flags),
    /// the bases' waving team flags, and drifting low-poly clouds. All of it is merged into a few vertex-coloured meshes
    /// drawn with RockGame/Painted (one draw per wall chunk / tower / flag / cloud). Only looks: the map's collision is
    /// still the four plain boundary boxes (shown instead of the castle in PSX and AI PSX, which keep their looks).
    /// </summary>
    public static class WorldDressing
    {
        static readonly Color k_Stone = new Color(0.76f, 0.73f, 0.67f), k_Core = new Color(0.54f, 0.52f, 0.48f),
            k_Trim = new Color(0.63f, 0.6f, 0.55f), k_Roof = new Color(0.36f, 0.39f, 0.5f), k_Gold = new Color(0.95f, 0.78f, 0.3f),
            k_Pole = new Color(0.3f, 0.2f, 0.12f);
        const float Course = 0.62f, Thick = 2.4f, WallH = 5f, Seg = 4f, Chunk = 48f;

        // =====================================================================
        // Castle walls
        // =====================================================================

        /// <summary>Castle walls just outside the play area (the inner face is exactly where the old walls' was). Returns false if it can't (no shader).</summary>
        public static bool BuildCastle(Transform root, float half, NormalLook look)
        {
            var mat = WorldLook.Castle;
            if (mat == null) return false;
            var go = new GameObject("Castle");
            go.transform.SetParent(root, false);
            var rng = new System.Random(Cfg.MapSeed * 31 + 7);

            int towers = Mathf.Max(2, Mathf.RoundToInt(2f * half / 42f));
            for (int side = 0; side < 4; side++)
            {
                // each side is built in its own frame: the wall runs along x, the play area is towards -z, the inner face at z = half
                var rot = Quaternion.Euler(0, side * 90f, 0);
                float G(float s, float d) { var w = rot * new Vector3(s, 0, d); return MapBuilder.Height(w.x, w.z); }
                var kit = new MeshKit();
                void Flush(string name)
                {
                    if (kit.Count == 0) return;
                    var part = kit.Spawn(go.transform, name, mat);
                    part.transform.localRotation = rot;
                    look.Normal.Add(part.GetComponent<Renderer>());
                    kit.Clear();
                }

                // where the towers go (the wall's face details stop at them)
                const float W = 6f, D = 5.4f;
                var tw = new List<float>();
                for (int t = 0; t < towers; t++) tw.Add(-half + 2f * half * (t + 0.5f) / towers);
                var free = new List<(float a, float b)>();
                void Free(float a, float b)
                {
                    free.Clear();
                    a = Mathf.Max(a, -half - 1.3f); b = Mathf.Min(b, half + 1.3f); // (into the corner towers)
                    foreach (var sc in tw)
                    {
                        if (b <= a) return;
                        float l = sc - W * 0.5f, r = sc + W * 0.5f;
                        if (r <= a || l >= b) continue;
                        if (l > a) free.Add((a, l));
                        a = r;
                    }
                    if (b > a) free.Add((a, b));
                }

                // wall, in chunks (so the parts out of view aren't drawn)
                float chunkEnd = -half + Chunk;
                for (float s0 = -half - Thick; s0 < half + Thick - 0.01f; s0 += Seg)
                {
                    float s1 = Mathf.Min(s0 + Seg, half + Thick);
                    float gA = G(s0, half), gB = G(s1, half), gM = G((s0 + s1) * 0.5f, half);
                    float gMin = Mathf.Min(gA, Mathf.Min(gB, gM)), gMax = Mathf.Max(gA, Mathf.Max(gB, gM));
                    float gOut = Mathf.Min(G(s0, half + Thick), G(s1, half + Thick));
                    float top = Mathf.Ceil((gMax + WallH) * 2f) * 0.5f;
                    float y0 = Mathf.Min(gMin, gOut) - 2.5f;
                    float mid = (s0 + s1) * 0.5f, len = s1 - s0;
                    // the core (its walkway on top), then on its face: a plinth, stone blocks, the parapet on corbels with battlements
                    kit.Box(new Vector3(mid, (y0 + top) * 0.5f, half + Thick * 0.5f), new Vector3(len, top - y0, Thick), k_Core, MeshKit.NoBottom);
                    float plinth = gMax + 0.45f;
                    Free(s0, s1);
                    foreach (var (a, b) in free)
                    {
                        float m = (a + b) * 0.5f;
                        kit.Box(new Vector3(m, (y0 + plinth) * 0.5f, half - 0.04f), new Vector3(b - a, plinth - y0, 0.08f), k_Trim * 0.9f, MeshKit.NZ | MeshKit.Top);
                        Blocks(kit, rng, new Vector3(a, 0, half), Quaternion.identity, b - a, plinth, top - 1.3f);
                        Parapet(kit, new Vector3(m, 0, half), b - a, top, a);
                    }
                    // a low wall on the outer edge of the walkway
                    kit.Box(new Vector3(mid, top + 0.3f, half + Thick - 0.2f), new Vector3(len, 0.6f, 0.4f), k_Trim);
                    if (s1 >= chunkEnd || s1 >= half + Thick - 0.01f) { Flush($"wall {side} {chunkEnd:0}"); chunkEnd += Chunk; }
                }

                // towers along the side (square, sticking out beyond the wall), each with the banner of the team whose side it's on
                for (int t = 0; t < towers; t++)
                {
                    float sc = tw[t];
                    float gMax = float.MinValue, gMin = float.MaxValue;
                    for (int i = -1; i <= 1; i++) for (int j = 0; j <= 1; j++) { float g = G(sc + i * W * 0.5f, half + j * D); gMax = Mathf.Max(gMax, i == 0 || j == 0 ? g : gMax); gMin = Mathf.Min(gMin, g); }
                    gMax = Mathf.Max(gMax, G(sc, half));
                    float top = Mathf.Ceil((gMax + WallH + 3.5f) * 2f) * 0.5f, y0 = gMin - 3f;
                    kit.Box(new Vector3(sc, (y0 + top) * 0.5f, half + D * 0.5f), new Vector3(W, top - y0, D), k_Core);
                    kit.Box(new Vector3(sc, (y0 + gMax + 0.45f) * 0.5f, half - 0.05f), new Vector3(W + 0.1f, gMax + 0.45f - y0, 0.1f), k_Trim * 0.9f, MeshKit.NZ | MeshKit.Top | MeshKit.PX | MeshKit.NX);
                    Blocks(kit, rng, new Vector3(sc - W * 0.5f, 0, half - 0.03f), Quaternion.identity, W, gMax + 0.45f, top - 1.4f);
                    // blocks on the two sides above the wall (seen from inside)
                    Blocks(kit, rng, new Vector3(sc + W * 0.5f, 0, half), Quaternion.Euler(0, -90, 0), D, Mathf.Ceil((gMax + WallH) * 2f) * 0.5f, top - 1.4f);
                    Blocks(kit, rng, new Vector3(sc - W * 0.5f, 0, half + D), Quaternion.Euler(0, 90, 0), D, Mathf.Ceil((gMax + WallH) * 2f) * 0.5f, top - 1.4f);
                    TowerTop(kit, new Vector3(sc, top, half + D * 0.5f), W, D);
                    var inside = rot * new Vector3(sc, 0, half - 6f);
                    Banner(kit, new Vector3(sc, top - 1.8f, half - 0.1f), Cfg.TeamColor[Mathf.Clamp(Cfg.RegionOf(inside), 0, 3)]);
                    Flush($"tower {side} {t}");
                }

                // the round corner tower (at this side's +x end), with a roof and a flag on top
                {
                    const float A = 3.4f; // apothem of the octagon
                    var c = new Vector3(half + 2.9f, 0, half + 2.9f);
                    float gMax = float.MinValue, gMin = float.MaxValue;
                    for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; float g = G(c.x + Mathf.Cos(a) * 4f, c.z + Mathf.Sin(a) * 4f); gMax = Mathf.Max(gMax, g); gMin = Mathf.Min(gMin, g); }
                    gMax = Mathf.Max(gMax, Mathf.Max(G(half, half), G(half + Thick, half)));
                    float top = Mathf.Ceil((gMax + WallH + 5.5f) * 2f) * 0.5f, y0 = gMin - 3f;
                    float wallTop = Mathf.Ceil((gMax + WallH) * 2f) * 0.5f;
                    Octagon(kit, c, A, y0, top, k_Core, true);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = (i + 0.5f) * Mathf.PI / 4f;
                        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        float w = 2f * A * Mathf.Tan(Mathf.PI / 8f);
                        var frot = Quaternion.LookRotation(-dir);
                        var left = c + dir * A - frot * Vector3.right * (w * 0.5f);
                        // (the faces turned to the play area start at the wall top; the rest are only seen from far)
                        bool inward = dir.x < 0.1f || dir.z < 0.1f;
                        Blocks(kit, rng, left - dir * 0.01f, frot, w, inward ? wallTop : gMax + 0.5f, top - 1.4f);
                    }
                    // corbelled ring, battlements and a slate roof
                    Octagon(kit, c, A + 0.35f, top - 0.9f, top, k_Trim, true);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = (i + 0.5f) * Mathf.PI / 4f;
                        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        kit.Box(c + dir * (A + 0.1f) + Vector3.up * (top + 0.5f), new Vector3(1.2f, 1f, 0.5f), Quaternion.LookRotation(-dir), k_Stone);
                        kit.Box(c + dir * (A + 0.2f) + Vector3.up * (top - 1.15f), new Vector3(0.5f, 0.5f, 0.4f), Quaternion.LookRotation(-dir), k_Trim * 0.85f, MeshKit.All);
                    }
                    kit.Cone(c + Vector3.up * top, A + 0.15f, 5.5f, 8, Mathf.PI / 8f, k_Roof, k_Roof * 1.25f);
                    kit.Box(c + Vector3.up * (top + 6.4f), new Vector3(0.1f, 2.2f, 0.1f), k_Pole);
                    Flush($"corner {side}");
                    var world = rot * c;
                    var team = Cfg.TeamColor[Mathf.Clamp(Cfg.RegionOf(new Vector3(world.x * 0.9f, 0, world.z * 0.9f)), 0, 3)];
                    var flag = Flag(go.transform, rot * (c + Vector3.up * (top + 6.6f) + new Vector3(0.05f, 0, 0)), team, 1.8f, 1.1f);
                    if (flag != null) look.Normal.Add(flag);
                }
            }
            return true;
        }

        /// <summary>Courses of stone blocks on a face: `origin` is the face's left end at y 0, rot turns local x along the
        /// face and local -z out of it. Rows line up with the same heights everywhere, so neighbouring bits match.</summary>
        static void Blocks(MeshKit kit, System.Random rng, Vector3 origin, Quaternion rot, float width, float from, float to)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            const float gap = 0.06f;
            for (int row = Mathf.FloorToInt(from / Course); row * Course < to; row++)
            {
                float yl = Mathf.Max(row * Course, from) + gap * 0.5f, yh = Mathf.Min((row + 1) * Course, to) - gap * 0.5f;
                if (yh - yl < 0.15f) continue;
                float x = (row & 1) == 0 ? 0f : -R(0.4f, 0.8f);
                while (x < width - 0.05f)
                {
                    float len = R(0.9f, 1.8f);
                    float a = Mathf.Max(x, 0f) + gap * 0.5f, b = Mathf.Min(x + len, width) - gap * 0.5f;
                    x += len;
                    if (b - a < 0.12f) continue;
                    float v = R(0.84f, 1.1f);
                    var col = new Color(k_Stone.r * v * R(0.97f, 1.03f), k_Stone.g * v, k_Stone.b * v * R(0.95f, 1.05f));
                    float depth = R(0.04f, 0.09f);
                    var centre = origin + rot * new Vector3((a + b) * 0.5f, (yl + yh) * 0.5f, -depth * 0.5f);
                    kit.Box(centre, new Vector3(b - a, yh - yl, depth), rot, col, MeshKit.NZ | MeshKit.Top | MeshKit.Bottom | MeshKit.PX | MeshKit.NX);
                }
            }
        }

        /// <summary>The top of a wall: a band on corbels sticking out over the face, and battlements along it.</summary>
        static void Parapet(MeshKit kit, Vector3 mid, float len, float top, float s0)
        {
            const float o = 0.25f; // how far it sticks out
            kit.Box(new Vector3(mid.x, top - 0.45f, mid.z + 0.35f - o * 0.5f), new Vector3(len, 0.9f, 0.7f + o), k_Trim, MeshKit.All);
            // corbels every 1 m (on fixed spots so neighbouring segments match)
            for (float x = Mathf.Floor(s0) + 0.5f; x < s0 + len - 0.18f; x += 1f)
                if (x > s0 + 0.18f) kit.Box(new Vector3(x, top - 1.15f, mid.z - o * 0.5f + 0.05f), new Vector3(0.36f, 0.5f, o + 0.1f), k_Trim * 0.88f, MeshKit.All);
            // merlons: 1.1 m teeth with 0.9 m gaps, lined up along the whole wall
            for (float x = Mathf.Floor(s0 / 2f) * 2f + 0.55f; x < s0 + len - 0.55f; x += 2f)
                if (x > s0 + 0.55f) kit.Box(new Vector3(x, top + 0.55f, mid.z + 0.35f - o * 0.5f), new Vector3(1.1f, 1.1f, 0.7f + o), k_Stone * 0.97f);
        }

        /// <summary>A square tower's top: a band sticking out all round with battlements on it.</summary>
        static void TowerTop(MeshKit kit, Vector3 c, float w, float d)
        {
            kit.Box(c + Vector3.up * -0.45f, new Vector3(w + 0.6f, 0.9f, d + 0.6f), k_Trim, MeshKit.All);
            for (float x = -w * 0.5f; x <= w * 0.5f + 0.01f; x += w / 3f)
            {
                kit.Box(c + new Vector3(x, -1.15f, -d * 0.5f - 0.15f), new Vector3(0.4f, 0.5f, 0.4f), k_Trim * 0.88f, MeshKit.All);
            }
            int nx = 4, nz = 4;
            for (int i = 0; i < nx; i++)
            {
                float x = -w * 0.5f - 0.05f + (w + 0.1f) * (i + 0.5f) / nx;
                kit.Box(c + new Vector3(x, 0.5f, -d * 0.5f - 0.05f), new Vector3(0.85f, 1f, 0.5f), k_Stone);
                kit.Box(c + new Vector3(x, 0.5f, d * 0.5f + 0.05f), new Vector3(0.85f, 1f, 0.5f), k_Stone);
            }
            for (int i = 0; i < nz; i++)
            {
                float z = -d * 0.5f - 0.05f + (d + 0.1f) * (i + 0.5f) / nz;
                kit.Box(c + new Vector3(-w * 0.5f - 0.05f, 0.5f, z), new Vector3(0.5f, 1f, 0.85f), k_Stone);
                kit.Box(c + new Vector3(w * 0.5f + 0.05f, 0.5f, z), new Vector3(0.5f, 1f, 0.85f), k_Stone);
            }
        }

        /// <summary>An octagonal prism (flat faces at apothem a) from y0 to y1.</summary>
        static void Octagon(MeshKit kit, Vector3 c, float a, float y0, float y1, Color col, bool topFace)
        {
            float r = a / Mathf.Cos(Mathf.PI / 8f);
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
                var p0 = c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r;
                var p1 = c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
                // outward face (corners listed so MeshKit's normal points out)
                kit.Quad(p1 + Vector3.up * y0, p0 + Vector3.up * y0, p0 + Vector3.up * y1, p1 + Vector3.up * y1, col * (0.93f + 0.07f * Mathf.Sin(a0 * 1.7f)));
                if (topFace) kit.Tri(c + Vector3.up * y1, p1 + Vector3.up * y1, p0 + Vector3.up * y1, col * 1.05f);
            }
        }

        /// <summary>A team banner hanging on a tower's inner face (top at `top`): cloth with a pointed tail, a gold stripe and a diamond.</summary>
        static void Banner(MeshKit kit, Vector3 top, Color team)
        {
            const float w = 1.8f, h = 3.6f;
            kit.Box(top + new Vector3(0, 0.1f, -0.05f), new Vector3(w + 0.5f, 0.14f, 0.14f), k_Pole, MeshKit.All);
            kit.Box(top + new Vector3(0, -h * 0.5f, 0), new Vector3(w, h, 0.04f), team, MeshKit.NZ | MeshKit.PX | MeshKit.NX | MeshKit.Bottom);
            var bl = top + new Vector3(-w * 0.5f, -h, -0.02f);
            var br = top + new Vector3(w * 0.5f, -h, -0.02f);
            var tip = top + new Vector3(0, -h - 0.8f, -0.02f);
            kit.Tri(bl, br, tip, team * 0.92f);
            kit.Box(top + new Vector3(0, -0.45f, -0.03f), new Vector3(w, 0.14f, 0.03f), k_Gold, MeshKit.NZ);
            kit.Box(top + new Vector3(0, -h + 0.35f, -0.03f), new Vector3(w, 0.14f, 0.03f), k_Gold, MeshKit.NZ);
            kit.Box(top + new Vector3(0, -h * 0.5f, -0.035f), new Vector3(0.75f, 0.75f, 0.03f), Quaternion.Euler(0, 0, 45f), k_Gold, MeshKit.NZ);
            kit.Box(top + new Vector3(0, -h * 0.5f, -0.04f), new Vector3(0.4f, 0.4f, 0.03f), Quaternion.Euler(0, 0, 45f), Color.Lerp(team, Color.white, 0.5f), MeshKit.NZ);
        }

        // =====================================================================
        // Flags
        // =====================================================================

        /// <summary>A waving cloth flag in the team colour, from `pole` (its top inner corner) out along +x. Null if no shader.</summary>
        public static Renderer Flag(Transform parent, Vector3 pole, Color team, float w, float h)
        {
            var mat = WorldLook.Flag;
            if (mat == null) return null;
            var kit = new MeshKit();
            const int nx = 12, ny = 5;
            var stripe = Color.Lerp(team, Color.white, 0.75f);
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                float x0 = w * i / nx, x1 = w * (i + 1) / nx, y0 = -h * j / ny, y1 = -h * (j + 1) / ny;
                // a pale stripe across the middle
                var col = j == ny / 2 ? stripe : team;
                Vector2 U(float x) => new Vector2(x / w, 0);
                var a = new Vector3(x0, y0, 0); var b = new Vector3(x1, y0, 0); var c = new Vector3(x1, y1, 0); var d = new Vector3(x0, y1, 0);
                kit.Tri(a, b, c, col, col, col, U(x0), U(x1), U(x1));
                kit.Tri(a, c, d, col, col, col, U(x0), U(x1), U(x0));
            }
            var go = kit.Spawn(parent, "flag", mat);
            go.transform.position = pole;
            return go.GetComponent<Renderer>();
        }
    }

    /// <summary>Normal graphics: puffy low-poly clouds drifting slowly high over the map (a handful of merged meshes,
    /// no shadows). Hidden in PSX / AI PSX and in the sudden death arena in space.</summary>
    public class CloudLayer : MonoBehaviour
    {
        readonly List<Transform> m_Clouds = new List<Transform>();
        readonly List<Renderer> m_Rs = new List<Renderer>();
        readonly List<Mesh> m_Meshes = new List<Mesh>();
        float m_Extent;
        bool m_Shown = true;
        static readonly Vector3 k_Wind = new Vector3(1.6f, 0, 0.6f);

        public static CloudLayer Build(Transform root)
        {
            var mat = WorldLook.Cloud;
            if (mat == null) return null;
            var go = new GameObject("Clouds");
            go.transform.SetParent(root, false);
            var cl = go.AddComponent<CloudLayer>();
            cl.Make(mat);
            return cl;
        }

        public int Count => m_Clouds.Count;
        public bool Shown => m_Shown;

        void Make(Material mat)
        {
            var rng = new System.Random(Cfg.MapSeed * 5 + 3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            m_Extent = Cfg.MapHalf * 2.4f + 60f;
            for (int v = 0; v < 6; v++) m_Meshes.Add(CloudMesh(rng));
            int n = Mathf.RoundToInt(16 * Mathf.Max(1f, Cfg.MapHalf / 100f));
            for (int i = 0; i < n; i++)
            {
                var c = new GameObject("cloud");
                c.transform.SetParent(transform, false);
                c.transform.localPosition = new Vector3(R(-m_Extent, m_Extent), R(80f, 120f), R(-m_Extent, m_Extent));
                c.transform.localRotation = Quaternion.Euler(0, R(-25f, 25f), 0);
                c.transform.localScale = Vector3.one * R(0.8f, 1.7f);
                c.AddComponent<MeshFilter>().sharedMesh = m_Meshes[i % m_Meshes.Count];
                var mr = c.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                m_Clouds.Add(c.transform);
                m_Rs.Add(mr);
            }
        }

        void OnDestroy() { foreach (var m in m_Meshes) if (m) Destroy(m); }

        /// <summary>One cloud: overlapping puffs (bigger in the middle), flat underneath, white on top shading to grey-blue below.</summary>
        static Mesh CloudMesh(System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var kit = new MeshKit();
            int puffs = rng.Next(5, 9);
            float len = R(26f, 42f);
            var top = new Color(1f, 1f, 1f);
            var bottom = new Color(0.74f, 0.77f, 0.86f);
            for (int p = 0; p < puffs; p++)
            {
                float t = puffs == 1 ? 0.5f : p / (puffs - 1f);
                float big = Mathf.Sin(t * Mathf.PI);
                float r = Mathf.Lerp(4.5f, 9.5f, big) * R(0.85f, 1.15f);
                var c = new Vector3((t - 0.5f) * len, r * 0.35f + R(-0.5f, 1.2f), R(-4f, 4f) * (1f - big * 0.5f));
                Puff(kit, c, r, R(0.85f, 1.15f), rng, top, bottom);
            }
            return kit.ToMesh("cloud");
        }

        static readonly Vector3[] k_IcoV;
        static readonly int[] k_IcoF;

        static CloudLayer()
        {
            // icosphere, subdivided once (80 faces): round enough to read as a puff, still chunky low-poly
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var mids = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (mids.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                return mids[key] = v.Count - 1;
            }
            var faces = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                faces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            k_IcoV = v.ToArray();
            k_IcoF = faces.ToArray();
        }

        static void Puff(MeshKit kit, Vector3 c, float r, float squash, System.Random rng, Color top, Color bottom)
        {
            var pts = new Vector3[k_IcoV.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var p = k_IcoV[i] * r * (1f + ((float)rng.NextDouble() - 0.5f) * 0.12f);
                p.y *= 0.72f * squash;
                p += c;
                p.y = Mathf.Max(p.y, 0f); // flat bottom
                pts[i] = p;
            }
            Color Col(Vector3 p) => Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.y / 7f)));
            for (int i = 0; i < k_IcoF.Length; i += 3)
            {
                Vector3 a = pts[k_IcoF[i]], b = pts[k_IcoF[i + 1]], d = pts[k_IcoF[i + 2]];
                if (a.y <= 0.001f && b.y <= 0.001f && d.y <= 0.001f && Vector3.Cross(b - a, d - a).y < 0f) { kit.Tri(a, b, d, bottom * 0.95f); continue; }
                kit.Tri(a, b, d, Col(a), Col(b), Col(d));
            }
        }

        void Update()
        {
            var cam = Camera.main;
            bool show = GameSettings.GraphicsMode == 0 && (cam == null || !SpaceArena.NearArena(cam.transform.position));
            if (show != m_Shown)
            {
                m_Shown = show;
                foreach (var r in m_Rs) if (r) r.enabled = show;
            }
            if (!show) return;
            var step = k_Wind * Time.deltaTime;
            foreach (var c in m_Clouds)
            {
                var p = c.localPosition + step;
                if (p.x > m_Extent) p.x -= 2f * m_Extent;
                if (p.z > m_Extent) p.z -= 2f * m_Extent;
                c.localPosition = p;
            }
        }
    }
}
