using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame
{
    /// <summary>
    /// SWAMP: everything outside the bases is murky shallow water (very slow to wade), lost in golden fog. Small lily pads
    /// (standing up out of the water: on them you move at full speed, like on the ground) are
    /// scattered at random over it - sparse, some close, some a long jump apart and some too far - so crossing on them takes
    /// careful jumping and often runs out (then you wade). A small mud island in the middle with
    /// the ball (stumps, roots, a fallen log, an upturned boat, glowing lanterns), and little root mounds out in the water
    /// where the twisted black trees and the berry bushes grow. Kelpies (bog ponies) to ride.
    /// </summary>
    public class SwampMap : ThemeMap
    {
        public override MapKind Kind => MapKind.Swamp;
        public override string Label => "Swamp";
        public override string Blurb => "Murky water everywhere but your base, lost in golden fog. Scattered lily pads are solid ground - hop from one to the next (they don't always reach). The water is VERY slow to wade - or craft a BOAT (1500 wood).";
        public override bool HasWater => true;
        public override bool Mountains => false;
        public override string MountName => "Kelpie";
        /// <summary>The water is sluggish (the lily pads are normal ground).</summary>
        public override float WaterSpeed => 0.4f;

        /// <summary>The pads' tops stand well clear of the water (ThemeMaps.InWater: feet under WaterY + 0.2 is wading), so
        /// on a pad you walk at full speed.</summary>
        const float PadTop = ThemeMaps.WaterY + 0.32f, PadBottom = ThemeMaps.WaterY - 0.12f;
        /// <summary>Lily pads: at most one in each 3 m cell, anywhere in it, in only about a third of the cells (patchy: a few
        /// clusters, wide empty stretches) - 1 - 2 m across. Gaps run from an easy hop to too far to jump.</summary>
        const float PadStep = 3f;
        /// <summary>The middle mud island's radius (flat round the ball).</summary>
        const float MidR = 12.5f;
        /// <summary>The grid the root mounds are scattered on (one or none per cell).</summary>
        const float MoundCell = 13f;
        static readonly Color Mud = new Color(0.27f, 0.24f, 0.17f), MossGround = new Color(0.25f, 0.29f, 0.17f), Bed = new Color(0.17f, 0.17f, 0.12f);
        static readonly Color BarkC = new Color(0.13f, 0.12f, 0.1f), HangMoss = new Color(0.33f, 0.37f, 0.27f);
        static readonly Color Pad = new Color(0.3f, 0.48f, 0.2f), Pad2 = new Color(0.38f, 0.55f, 0.24f), Lantern = new Color(1f, 0.78f, 0.35f);

        /// <summary>Root mound in cell (i, j) of blue's sector (if it has one): its centre and radius.</summary>
        static bool MoundAt(int i, int j, out Vector2 c, out float r)
        {
            c = default;
            r = 0f;
            if (TmKit.Hash(i, j, 71) > 0.45f) return false;
            c = new Vector2((i + 0.2f + 0.6f * TmKit.Hash(i, j, 72)) * MoundCell, (j + 0.2f + 0.6f * TmKit.Hash(i, j, 73)) * MoundCell);
            r = 2.4f + TmKit.Hash(i, j, 74);
            var p = new Vector3(c.x, 0, c.y);
            if (!Cfg.InFirstSector(p, r + 2f) || p.magnitude < MidR + 8f + r) return false;
            var bc = Cfg.BaseCenter[0];
            if (Mathf.Abs(c.x - bc.x) < Cfg.BaseHalf + r + 5f && Mathf.Abs(c.y - bc.z) < Cfg.BaseHalf + r + 5f) return false;
            return Mathf.Abs(c.x) < Cfg.MapHalf - r - 3f && Mathf.Abs(c.y) < Cfg.MapHalf - r - 3f;
        }

        /// <summary>How much of a root mound is here (0 = none, 1 = on top of one).</summary>
        static float Mound(float x, float z)
        {
            var q = TmKit.ToFirstSector(new Vector3(x, 0, z));
            int ci = Mathf.FloorToInt(q.x / MoundCell), cj = Mathf.FloorToInt(q.z / MoundCell);
            float best = 0f;
            for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
            {
                if (!MoundAt(ci + di, cj + dj, out var c, out float r)) continue;
                float d = Vector2.Distance(new Vector2(q.x, q.z), c);
                best = Mathf.Max(best, 1f - ThemeMaps.SmoothStepP(r - 1.2f, r + 0.8f, d));
            }
            return best;
        }

        public override float Height(float x, float z)
        {
            float s = ThemeMaps.SeedP;
            float bed = -1f + (ThemeMaps.SymNoiseP(x, z, 0.06f, s) - 0.5f) * 0.35f;
            // land only on the bases (with a mud bank round them) and the little island in the middle
            float land = 1f - ThemeMaps.SmoothStepP(MidR, MidR + 3.5f, Mathf.Sqrt(x * x + z * z));
            for (int t = 0; t < Cfg.TeamCount; t++)
            {
                var c = Cfg.BaseCenter[t];
                float dx = Mathf.Max(0f, Mathf.Abs(x - c.x) - Cfg.BaseHalf), dz = Mathf.Max(0f, Mathf.Abs(z - c.z) - Cfg.BaseHalf);
                land = Mathf.Max(land, 1f - ThemeMaps.SmoothStepP(1f, 3.5f, Mathf.Sqrt(dx * dx + dz * dz)));
            }
            float h = Mathf.Lerp(bed, 0f, land);
            // root mounds out in the water (the trees and mushrooms grow on them)
            float m = Mound(x, z);
            if (m > 0f) h = Mathf.Max(h, Mathf.Lerp(bed, 0.22f + (ThemeMaps.SymNoiseP(x, z, 0.4f, s + 9f) - 0.5f) * 0.15f, m));
            return h;
        }

        public override Color[] Palette => new[] { Mud, MossGround, Bed };
        public override int ColourAt(Vector3 c, float slopeY) => c.y < -0.3f ? 2 : ThemeMaps.SymNoiseP(c.x, c.z, 0.1f, ThemeMaps.SeedP + 3f) > 0.5f ? 1 : 0;

        public override float NodeMul(byte kind) => kind == ResourceNode.Tree ? 1f : kind == ResourceNode.Bush ? 0.9f : 0.6f;
        public override Color LeafTint(Color leaf) => Color.Lerp(leaf, new Color(0.3f, 0.36f, 0.2f), 0.55f);

        /// <summary>The trees' trunk radius from the ground to 3 m (straight and round there: the weak spot X sits on it).</summary>
        const float TrunkR = 0.375f, StraightH = 3f;
        public override float TreeTrunkRadius(int seed) => TrunkR;

        /// <summary>Twisted black trees, no leaves: a straight round trunk to 3 m (where the X goes), crooked above that,
        /// low roots arching out of the mud from its foot, gnarled branches, hanging moss.</summary>
        public override bool BuildTree(Transform tr, int seed, float h, GameObject trunk)
        {
            trunk.GetComponent<MeshRenderer>().enabled = false;
            var rng = new System.Random(seed + 13);
            float R() => (float)rng.NextDouble();
            float Rs() => R() * 2f - 1f;
            var bark = TmKit.Shade(BarkC, 0.9f + R() * 0.3f);
            float H = Mathf.Max(h * (1.15f + R() * 0.35f), StraightH + 2.5f);
            // the trunk: straight and exactly TrunkR round up to 3 m, then bending more the higher it goes
            Art.Part(tr, Art.Cylinder, bark, new Vector3(0, StraightH * 0.5f, 0), new Vector3(TrunkR * 2f, StraightH * 0.5f, TrunkR * 2f));
            var pos = new Vector3(0, StraightH, 0);
            var dir = Vector3.up;
            int segs = 3;
            float segL = (H - StraightH) / segs;
            for (int i = 0; i < segs; i++)
            {
                float bend = 14f + i * 10f;
                dir = (Quaternion.Euler(Rs() * bend, 0, Rs() * bend) * dir).normalized;
                if (dir.y < 0.45f) { dir.y = 0.45f; dir.Normalize(); }
                float w = Mathf.Lerp(TrunkR * 2f, 0.32f, i / (float)segs);
                Seg(tr, bark, pos, pos + dir * segL, w);
                pos += dir * segL;
            }
            // roots arching out of the mud from the trunk's very foot (under 0.3 m where they meet it, clear of the X)
            int roots = 3 + rng.Next(2);
            for (int k = 0; k < roots; k++)
            {
                var o = Quaternion.Euler(0, k * 360f / roots + Rs() * 25f, 0) * Vector3.forward;
                var a = o * (TrunkR * 0.7f) + Vector3.up * 0.1f;
                var m = o * (1.1f + R() * 0.4f) + Vector3.up * (0.4f + R() * 0.2f);
                var e = o * (2f + R() * 0.8f) + Vector3.down * 0.3f;
                Seg(tr, bark, a, m, 0.26f);
                Seg(tr, bark, m, e, 0.2f);
            }
            // gnarled branches, moss hanging off them
            int branches = 3;
            for (int k = 0; k < branches; k++)
            {
                var a = Vector3.Lerp(new Vector3(0, StraightH, 0), pos, 0.3f + k * 0.3f) + Vector3.up * 0.2f;
                var o = Quaternion.Euler(0, k * 120f + Rs() * 30f, 0) * Vector3.forward;
                var m = a + (o * 1.6f + Vector3.up * (0.9f + R() * 0.6f));
                var e = m + (Quaternion.Euler(0, Rs() * 50f, 0) * o) * 1.5f + Vector3.up * (Rs() * 0.6f);
                Seg(tr, bark, a, m, 0.2f);
                Seg(tr, bark, m, e, 0.13f);
                float ml = 0.8f + R() * 1.4f;
                Art.Box(tr, HangMoss, e + Vector3.down * ml * 0.5f, new Vector3(0.12f, ml, 0.12f), new Vector3(0, R() * 90f, 0));
            }
            float tl = 1.2f + R() * 1.2f;
            Art.Box(tr, HangMoss, pos + Vector3.down * tl * 0.5f + Vector3.right * 0.2f, new Vector3(0.14f, tl, 0.14f));
            return true;
        }

        /// <summary>A cylinder from a to b, width w.</summary>
        static GameObject Seg(Transform tr, Color c, Vector3 a, Vector3 b, float w, bool collider = false)
        {
            var d = b - a;
            return Art.Part(tr, Art.Cylinder, c, (a + b) * 0.5f, new Vector3(w, d.magnitude * 0.5f + w * 0.25f, w), Quaternion.FromToRotation(Vector3.up, d).eulerAngles, collider);
        }

        /// <summary>The game's berry bush, murky green with glowing orange berries (press E for food).</summary>
        public override bool BuildBush(Transform tr, int seed)
        {
            ResourceNode.BuildBerryBush(tr, seed, new Color(0.27f, 0.36f, 0.2f), new Color(1f, 0.55f, 0.1f));
            return true;
        }

        /// <summary>The kelpie, a bog pony: the game's horse, dark bog-green, with a long weed-green mane hanging down both
        /// sides of its neck, strands of pondweed hanging off it and glowing orange eyes (now and then a rare pale one).</summary>
        public override bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            var coat = unicorn ? new Color(0.7f, 0.78f, 0.74f) : new Color(0.2f, 0.26f, 0.22f);
            var mane = new Color(0.32f, 0.48f, 0.22f);
            var weed = new Color(0.4f, 0.5f, 0.26f);
            var neck = TmKit.Horse(t, ghost, coat, mane, new Color(0.12f, 0.12f, 0.1f), out saddle, out head, out tail, legs, out var legT, TmKit.Shade(coat, 0.7f), 0.15f);
            // glowing eyes (over the horse's black ones)
            var eye = TmKit.GlowShared(new Color(1f, 0.6f, 0.15f), 2f);
            for (int s = -1; s <= 1; s += 2)
                Art.Part(neck, Art.Cube, Color.white, new Vector3(s * 0.162f, 0.7f, 0.5f), new Vector3(0.02f, 0.07f, 0.08f), default, false, eye);
            // the long mane: strands hanging down both sides of the neck
            var up = Quaternion.Euler(25, 0, 0) * Vector3.up;
            for (int i = 0; i < 4; i++)
                for (int s = -1; s <= 1; s += 2)
                    Art.Box(neck, i % 2 == 0 ? mane : weed, new Vector3(s * 0.1f, 0.3f, 0f) + up * (-0.25f + i * 0.16f) + Vector3.down * 0.1f, new Vector3(0.05f, 0.3f, 0.1f), new Vector3(25, 0, s * 8f));
            // pondweed hanging off its back and sides
            for (int i = 0; i < 5; i++)
            {
                int s = i % 2 == 0 ? -1 : 1;
                float l = 0.3f + (i % 3) * 0.1f;
                Art.Box(t, i % 2 == 0 ? weed : mane, new Vector3(s * 0.31f, 1.3f - l * 0.5f, -0.55f + i * 0.27f), new Vector3(0.03f, l, 0.08f));
            }
            // a long weedy tail
            Art.Box(tail, weed, new Vector3(0, -0.6f, -0.08f), new Vector3(0.1f, 0.4f, 0.1f));
            foreach (var leg in legT)
                Art.Box(leg, weed, new Vector3(0, -0.62f, 0), new Vector3(0.17f, 0.05f, 0.17f)); // (a band of weed round each leg)
            return true;
        }

        // =====================================================================

        readonly TmSky m_Sky = new TmSky();
        public override void ApplySky() => m_Sky.Apply(new Color(0.6f, 0.58f, 0.44f), 0.0135f,
            new Color(0.5f, 0.5f, 0.4f), new Color(0.38f, 0.37f, 0.28f), new Color(0.16f, 0.16f, 0.11f), new Color(1f, 0.8f, 0.5f), 0.75f,
            new Color(0.62f, 0.55f, 0.34f), 0.9f);
        public override void Cleanup() => m_Sky.Restore();

        /// <summary>Lily pads, merged into a few meshes (there are thousands) with a box collider each, so they're solid.</summary>
        class PadBatch
        {
            readonly Transform m_Parent;
            readonly List<Vector3> m_V = new List<Vector3>(), m_N = new List<Vector3>();
            readonly List<int>[] m_T = { new List<int>(), new List<int>() };
            readonly List<(Vector3 c, Vector3 s)> m_Boxes = new List<(Vector3, Vector3)>();
            public PadBatch(Transform parent) { m_Parent = parent; }

            public void Add(Vector3 c, float r, float notch, int sub)
            {
                const int segs = 10;
                if (m_V.Count + 1 + segs * 3 > 64000) Flush();
                var tris = m_T[sub];
                int centre = m_V.Count;
                m_V.Add(new Vector3(c.x, PadTop, c.z)); m_N.Add(Vector3.up);
                int top = m_V.Count;
                for (int k = 0; k < segs; k++)
                {
                    float a = (notch + k * 360f / segs) * Mathf.Deg2Rad;
                    m_V.Add(new Vector3(c.x + Mathf.Cos(a) * r, PadTop, c.z + Mathf.Sin(a) * r)); m_N.Add(Vector3.up);
                }
                int side = m_V.Count;
                for (int k = 0; k < segs; k++)
                {
                    float a = (notch + k * 360f / segs) * Mathf.Deg2Rad;
                    var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    m_V.Add(new Vector3(c.x, PadTop, c.z) + o * r); m_N.Add(o);
                    m_V.Add(new Vector3(c.x, PadBottom, c.z) + o * r * 0.92f); m_N.Add(o);
                }
                // (segment 0 is the notch cut out of the pad)
                for (int k = 1; k < segs; k++)
                {
                    int k1 = (k + 1) % segs;
                    tris.Add(centre); tris.Add(top + k1); tris.Add(top + k);
                    int t0 = side + k * 2, b0 = t0 + 1, t1 = side + k1 * 2, b1 = t1 + 1;
                    tris.Add(t0); tris.Add(t1); tris.Add(b0);
                    tris.Add(t1); tris.Add(b1); tris.Add(b0);
                }
                float bs = r * 1.75f;
                m_Boxes.Add((new Vector3(c.x, (PadTop + PadBottom - 0.1f) * 0.5f, c.z), new Vector3(bs, PadTop - PadBottom + 0.1f, bs)));
            }

            public void Flush()
            {
                if (m_V.Count == 0) return;
                var mesh = new Mesh { name = "lily pads" };
                mesh.SetVertices(m_V);
                mesh.SetNormals(m_N);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(m_T[0], 0);
                mesh.SetTriangles(m_T[1], 1);
                mesh.RecalculateBounds();
                var go = new GameObject("lily pads");
                go.transform.SetParent(m_Parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { Art.Mat(Pad), Art.Mat(Pad2) };
                foreach (var (c, s) in m_Boxes)
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = c;
                    bc.size = s;
                }
                m_V.Clear(); m_N.Clear(); m_T[0].Clear(); m_T[1].Clear(); m_Boxes.Clear();
            }
        }

        public override void BuildProps(Transform root)
        {
            TmKit.TintWater(root, new Color(0.24f, 0.29f, 0.2f), 0.92f);
            var rng = TmKit.Rng(7300);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var sec = TmKit.Sector(root, "Swamp props");
            float half = Cfg.MapHalf;
            bool Deep(float x, float z) => ThemeMaps.Height(x, z) < ThemeMaps.WaterY - 0.3f;

            // ---- lily pads scattered over the water: at most one per 3 m cell (anywhere in it), only in about 35% of the
            //      cells and patchy (a slow noise makes clusters and bare stretches) - laid out in blue's sector, the grid the
            //      same turned round so the copies line up at the sector edges ----
            var pads = new PadBatch(sec);
            var padAt = new Dictionary<long, Vector3>(); // grid cell -> (x, radius, z)
            long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
            int n = Mathf.CeilToInt(half / PadStep);
            int flowers = 0;
            bool Crowds(Vector3 p, float r, int i, int j)
            {
                for (int di = -2; di <= 2; di++)
                for (int dj = -2; dj <= 2; dj++)
                    if (padAt.TryGetValue(Key(i + di, j + dj), out var q) && new Vector2(q.x - p.x, q.z - p.z).magnitude < q.y + r + 0.5f) return true;
                return false;
            }
            for (int j = 0; j < n; j++)
            for (int i = -n; i < n; i++)
            {
                if (Cfg.FourWay && !(i >= 0 ? j >= i : j >= -i)) continue; // (just blue's quarter, one diagonal edge)
                float r = 0.5f + TmKit.Hash(i, j, 83) * 0.5f;
                // anywhere in its cell (well off the grid), not just near the middle
                float room = PadStep * 0.5f - r - 0.05f;
                var p = new Vector3((i + 0.5f) * PadStep + (TmKit.Hash(i, j, 81) - 0.5f) * 2f * room, 0, -(j + 0.5f) * PadStep + (TmKit.Hash(i, j, 82) - 0.5f) * 2f * room);
                // (clusters and bare stretches; x0.9: about a fifth fewer pads than the x1.15 before - harder to hop across)
                float keep = (0.12f + 0.38f * TmKit.SymN(p.x, p.z, 0.07f, ThemeMaps.SeedP + 55f)) * 0.9f;
                if (TmKit.Hash(i, j, 80) > keep) continue;
                // about 40% of them knocked off their spot by 0.5 - 1 m (even past their cell) so the hops don't line up and
                // the gaps vary more - as long as it stays in blue's sector and clear of the pads already down
                if (TmKit.Hash(i, j, 88) < 0.4f)
                {
                    var off = Quaternion.Euler(0, TmKit.Hash(i, j, 89) * 360f, 0) * new Vector3(0, 0, 0.5f + 0.5f * TmKit.Hash(i, j, 90));
                    if (Cfg.InFirstSector(p + off, r + 0.3f) && !Crowds(p + off, r, i, j)) p += off;
                }
                if (Crowds(p, r, i, j)) continue; // (a knocked pad already sits here)
                if (Mathf.Abs(p.x) > half - 1.2f || Mathf.Abs(p.z) > half - 1.2f || !Deep(p.x, p.z)) continue;
                if (!Deep(p.x + r, p.z) || !Deep(p.x - r, p.z) || !Deep(p.x, p.z + r) || !Deep(p.x, p.z - r)) continue;
                float notch = TmKit.Hash(i, j, 84) * 360f;
                pads.Add(p, r, notch, TmKit.Hash(i, j, 85) < 0.5f ? 0 : 1);
                padAt[Key(i, j)] = new Vector3(p.x, r, p.z);
                if (TmKit.Hash(i, j, 86) < 0.1f && flowers < 60)
                {
                    flowers++;
                    var fc = TmKit.Hash(i, j, 87) < 0.5f ? new Color(0.95f, 0.7f, 0.8f) : new Color(0.95f, 0.93f, 0.85f);
                    var fp = new Vector3(p.x, PadTop + 0.12f, p.z) + Quaternion.Euler(0, notch + 180f, 0) * new Vector3(0, 0, r * 0.3f);
                    Art.Part(sec, Art.Cone, fc, fp, new Vector3(0.38f, 0.3f, 0.38f), new Vector3(180f, 0, 0));
                    Art.Part(sec, Art.Sphere, new Color(1f, 0.85f, 0.3f), fp + Vector3.up * 0.04f, Vector3.one * 0.12f);
                }
            }
            pads.Flush();
            bool ClearOfPads(Vector3 p, float r)
            {
                int ci = Mathf.FloorToInt(p.x / PadStep), cj = Mathf.FloorToInt(-p.z / PadStep);
                for (int di = -2; di <= 2; di++)
                for (int dj = -2; dj <= 2; dj++)
                    if (padAt.TryGetValue(Key(ci + di, cj + dj), out var q) && new Vector2(q.x - p.x, q.z - p.z).magnitude < q.y + r + 0.3f) return false;
                return true;
            }

            // ---- stumps and snags in the water between the pads (a stump is a step up), and reeds ----
            int bits = Mathf.RoundToInt(16 * half / 100f);
            for (int i = 0, made = 0; i < bits * 40 && made < bits; i++)
            {
                var p = new Vector3(Rn(-half + 6f, half - 6f), 0, Rn(-half + 6f, -4f));
                if (!TmKit.FreeSpot(p, 1.5f) || !Deep(p.x, p.z) || !ClearOfPads(p, 0.8f)) continue;
                made++;
                float g = ThemeMaps.Height(p.x, p.z);
                switch (made % 3)
                {
                    case 0: // a stump, its top a step up from the pads
                    {
                        float top = PadTop + Rn(0.5f, 1.1f), r = Rn(0.45f, 0.6f);
                        Art.Part(sec, Art.Cylinder, TmKit.Shade(BarkC, 1.3f), new Vector3(p.x, (g + top) * 0.5f, p.z), new Vector3(r * 2f, (top - g) * 0.5f, r * 2f), new Vector3(Rn(-4, 4), 0, Rn(-4, 4)), true);
                        break;
                    }
                    case 1: // a snag: a dead spike sticking out of the water at a lean
                        Seg(sec, BarkC, new Vector3(p.x, g, p.z), new Vector3(p.x + Rn(-0.8f, 0.8f), PadTop + Rn(1.5f, 3f), p.z + Rn(-0.8f, 0.8f)), 0.22f);
                        break;
                    default: // reeds
                        for (int k = 0; k < 6; k++)
                        {
                            float rh = Rn(1f, 1.9f);
                            var q = p + new Vector3(Rn(-0.5f, 0.5f), 0, Rn(-0.5f, 0.5f));
                            Art.Box(sec, new Color(0.32f, 0.38f, 0.2f), new Vector3(q.x, ThemeMaps.WaterY + rh * 0.5f, q.z), new Vector3(0.06f, rh, 0.06f), new Vector3(Rn(-10, 10), Rn(0, 90), Rn(-10, 10)));
                        }
                        break;
                }
            }
            TmKit.CopyRound(sec);
        }

        /// <summary>The little mud island in the middle: stumps, an arching root, a mossy fallen log, an upturned old boat,
        /// cypress knees and glowing lanterns round the ball.</summary>
        public override bool BuildCentre(Transform root)
        {
            var spots = new (float f, float r)[] { (-0.28f, 7f), (0.28f, 7.5f), (0f, 10.5f), (-0.42f, 10.5f), (0.42f, 10f), (0.14f, 11f), (-0.14f, 11f) };
            var glow = TmKit.GlowShared(Lantern, 2.2f);
            TmKit.CentreLayout(root, "Swamp centre", 7350, spots, MidR - 1.5f, 4.2f, (sec, p, yaw, i, rng) =>
            {
                float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                var o = Quaternion.Euler(0, yaw, 0);
                switch (i)
                {
                    case 0: // a big hollow stump with moss on top
                        Art.Part(sec, Art.Cylinder, TmKit.Shade(BarkC, 1.4f), p + Vector3.up * 0.7f, new Vector3(1.8f, 0.8f, 1.8f), default, true);
                        Art.Part(sec, Art.Cylinder, new Color(0.08f, 0.07f, 0.06f), p + Vector3.up * 1.5f, new Vector3(1.2f, 0.02f, 1.2f));
                        Art.Box(sec, HangMoss, p + Vector3.up * 1.52f + o * new Vector3(0.6f, 0, 0), new Vector3(0.5f, 0.06f, 1.2f), new Vector3(0, yaw, 0));
                        break;
                    case 1: // an upturned old rowboat (cover)
                    {
                        var wood = new Color(0.32f, 0.26f, 0.18f);
                        var e = new Vector3(0, yaw + 90f, 0);
                        Art.Box(sec, wood, p + Vector3.up * 0.75f, new Vector3(1.5f, 0.12f, 3.6f), e, true);
                        for (int s = -1; s <= 1; s += 2)
                            Art.Box(sec, TmKit.Shade(wood, 0.85f), p + Vector3.up * 0.38f + Quaternion.Euler(0, yaw + 90f, 0) * new Vector3(s * 0.72f, 0, 0), new Vector3(0.1f, 0.8f, 3.4f), new Vector3(0, yaw + 90f, s * 8f), true);
                        Art.Box(sec, TmKit.Shade(wood, 0.8f), p + Vector3.up * 0.4f + Quaternion.Euler(0, yaw + 90f, 0) * new Vector3(0, 0, 1.75f), new Vector3(1.3f, 0.75f, 0.1f), e, true);
                        break;
                    }
                    case 2: // an arching root, high enough to hide behind, low enough to jump on
                    {
                        var side = o * Vector3.right;
                        var a = p - side * 2.2f; var b = p + side * 2.2f;
                        var m = p + Vector3.up * 1.8f;
                        Seg(sec, BarkC, a + Vector3.down * 0.3f, m - side * 0.6f, 0.55f, true);
                        Seg(sec, BarkC, m - side * 0.6f, m + side * 0.6f, 0.5f, true);
                        Seg(sec, BarkC, m + side * 0.6f, b + Vector3.down * 0.3f, 0.5f, true);
                        Art.Box(sec, HangMoss, m + Vector3.down * 0.65f, new Vector3(0.12f, 0.9f, 0.12f));
                        break;
                    }
                    case 3: case 4: // a mossy fallen log
                    {
                        var side = o * Vector3.right;
                        var a = p - side * 2.4f + Vector3.up * 0.45f; var b = p + side * 2.4f + Vector3.up * 0.45f;
                        var log = TmKit.Rod(sec, new Color(0.24f, 0.2f, 0.15f), a, b, 0.9f, 0.9f, false, Art.Cylinder);
                        log.AddComponent<BoxCollider>().size = new Vector3(0.95f, 2f, 0.95f);
                        Art.Box(sec, HangMoss, (a + b) * 0.5f + Vector3.up * 0.43f, new Vector3(0.6f, 0.08f, 3f), new Vector3(0, yaw + 90f, 0));
                        break;
                    }
                    default: // cypress knees round a lantern post
                    {
                        for (int k = 0; k < 5; k++)
                        {
                            var q = p + Quaternion.Euler(0, k * 72f + R(-15f, 15f), 0) * new Vector3(0, 0, R(0.6f, 1.3f));
                            float kh = R(0.6f, 1.4f);
                            Art.Part(sec, Art.Cone, TmKit.Shade(BarkC, 1.3f), q, new Vector3(0.5f, kh, 0.5f), new Vector3(R(-8f, 8f), 0, R(-8f, 8f)), k < 2);
                        }
                        Art.Box(sec, BarkC, p + Vector3.up * 1.2f, new Vector3(0.18f, 2.4f, 0.18f), default, true);
                        Art.Box(sec, BarkC, p + Vector3.up * 2.3f + o * new Vector3(0.3f, 0, 0), new Vector3(0.7f, 0.1f, 0.1f), new Vector3(0, yaw, 0));
                        Art.Part(sec, Art.Sphere, Lantern, p + Vector3.up * 1.95f + o * new Vector3(0.55f, 0, 0), new Vector3(0.3f, 0.4f, 0.3f), default, false, glow);
                        break;
                    }
                }
            });
            return true;
        }
    }
}
