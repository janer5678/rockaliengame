using UnityEngine;

namespace RockGame
{
    /// <summary>Builds the static (non-networked) world identically on every peer: flat map, bases, arena.</summary>
    public static class MapBuilder
    {
        static readonly Color k_Grass = new Color(0.36f, 0.56f, 0.3f);

        public static void Build()
        {
            var root = new GameObject("World").transform;
            var rng = new System.Random(99);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // ---------- ground ----------
            float size = Cfg.MapHalf * 2f + 60f;
            var ground = Art.Box(root, k_Grass, new Vector3(0, -0.5f, 0), new Vector3(size, 1, size), default, true);
            ground.name = "Ground";
            ground.AddComponent<GroundMarker>();
            for (int i = 0; i < 70; i++)
            {
                var c = Color.Lerp(k_Grass, new Color(0.3f, 0.48f, 0.24f), R(0.3f, 1f));
                float w = R(4f, 14f);
                Art.Box(root, c, new Vector3(R(-95, 95), 0.005f, R(-95, 95)), new Vector3(w, 0.01f, w * R(0.5f, 1.5f)), new Vector3(0, R(0, 90), 0));
            }

            // ---------- bases ----------
            for (int t = 0; t < 2; t++)
            {
                var c = Cfg.BaseCenter[t];
                var team = Cfg.TeamColor[t];
                var pad = Color.Lerp(new Color(0.5f, 0.45f, 0.35f), team, 0.35f);
                var line = Color.Lerp(pad, Color.white, 0.25f);
                float s = Cfg.BaseHalf * 2f;
                Art.Box(root, pad, c + new Vector3(0, 0.02f, 0), new Vector3(s, 0.03f, s));
                // build grid lines
                int cells = Mathf.RoundToInt(s / Cfg.Cell);
                for (int k = 0; k <= cells; k++)
                {
                    float o = -Cfg.BaseHalf + k * Cfg.Cell;
                    Art.Box(root, line, c + new Vector3(0, 0.04f, o), new Vector3(s, 0.01f, 0.06f));
                    Art.Box(root, line, c + new Vector3(o, 0.04f, 0), new Vector3(0.06f, 0.01f, s));
                }
                // coloured border strip + posts
                for (int side = 0; side < 4; side++)
                {
                    bool alongX = side < 2;
                    float sign = side % 2 == 0 ? 1 : -1;
                    Vector3 mid = c + (alongX ? new Vector3(0, 0.05f, sign * Cfg.BaseHalf) : new Vector3(sign * Cfg.BaseHalf, 0.05f, 0));
                    Art.Box(root, team, mid, alongX ? new Vector3(s + 0.6f, 0.05f, 0.6f) : new Vector3(0.6f, 0.05f, s + 0.6f));
                    for (int k = 0; k <= cells; k += 2)
                    {
                        float o = -Cfg.BaseHalf + k * Cfg.Cell;
                        Vector3 p = c + (alongX ? new Vector3(o, 0.5f, sign * (Cfg.BaseHalf + 0.5f)) : new Vector3(sign * (Cfg.BaseHalf + 0.5f), 0.5f, o));
                        Art.Box(root, team, p, new Vector3(0.18f, 1f, 0.18f));
                    }
                }
                // corner flags
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 p = c + new Vector3(sx * (Cfg.BaseHalf + 1f), 0, sz * (Cfg.BaseHalf + 1f));
                    Art.Box(root, Art.DarkWood, p + Vector3.up * 3.5f, new Vector3(0.2f, 7f, 0.2f), default, true);
                    Art.Box(root, team, p + new Vector3(0.7f, 6.3f, 0), new Vector3(1.4f, 0.9f, 0.05f));
                }
            }

            // ---------- centre ball drop zone ----------
            Art.Part(root, Art.Cylinder, new Color(0.85f, 0.75f, 0.3f), new Vector3(0, 0.02f, 0), new Vector3(12f, 0.02f, 12f));
            Art.Part(root, Art.Cylinder, new Color(0.95f, 0.88f, 0.45f), new Vector3(0, 0.03f, 0), new Vector3(9f, 0.02f, 9f));
            Art.Part(root, Art.Cylinder, new Color(0.85f, 0.75f, 0.3f), new Vector3(0, 0.04f, 0), new Vector3(2f, 0.02f, 2f));

            // ---------- map boundary ----------
            var wallC = new Color(0.45f, 0.43f, 0.4f);
            float h = Cfg.MapHalf;
            Art.Box(root, wallC, new Vector3(0, 2.5f, h + 1), new Vector3(2 * h + 4, 5, 2), default, true);
            Art.Box(root, wallC, new Vector3(0, 2.5f, -h - 1), new Vector3(2 * h + 4, 5, 2), default, true);
            Art.Box(root, wallC, new Vector3(h + 1, 2.5f, 0), new Vector3(2, 5, 2 * h + 4), default, true);
            Art.Box(root, wallC, new Vector3(-h - 1, 2.5f, 0), new Vector3(2, 5, 2 * h + 4), default, true);

            // distant low-poly mountains for a horizon
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f + R(-0.05f, 0.05f);
                float d = R(150f, 190f);
                float sc = R(20f, 45f);
                var m = Art.Part(root, Art.MakeRock(i, 0.35f), Color.Lerp(new Color(0.42f, 0.45f, 0.42f), new Color(0.55f, 0.55f, 0.6f), R(0, 1)),
                    new Vector3(Mathf.Cos(a) * d, sc * 0.2f, Mathf.Sin(a) * d), new Vector3(sc, sc * R(0.6f, 1.1f), sc), new Vector3(0, R(0, 360), 0));
                m.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            BuildArena(root);
        }

        static void BuildArena(Transform root)
        {
            var c = Cfg.ArenaCenter;
            float s = Cfg.ArenaHalf;
            var sand = new Color(0.78f, 0.68f, 0.5f);
            var floor = Art.Box(root, sand, c + new Vector3(0, -0.5f, 0), new Vector3(2 * s + 4, 1, 2 * s + 4), default, true);
            floor.name = "ArenaFloor";
            floor.AddComponent<GroundMarker>();
            Art.Part(root, Art.Cylinder, new Color(0.7f, 0.3f, 0.25f), c + new Vector3(0, 0.02f, 0), new Vector3(10f, 0.02f, 10f));
            Art.Box(root, Cfg.TeamColor[0], c + new Vector3(0, 0.02f, -14f), new Vector3(4, 0.02f, 4));
            Art.Box(root, Cfg.TeamColor[1], c + new Vector3(0, 0.02f, 14f), new Vector3(4, 0.02f, 4));

            var wallC = new Color(0.5f, 0.4f, 0.32f);
            Art.Box(root, wallC, c + new Vector3(0, 3f, s + 1), new Vector3(2 * s + 4, 6, 2), default, true);
            Art.Box(root, wallC, c + new Vector3(0, 3f, -s - 1), new Vector3(2 * s + 4, 6, 2), default, true);
            Art.Box(root, wallC, c + new Vector3(s + 1, 3f, 0), new Vector3(2, 6, 2 * s + 4), default, true);
            Art.Box(root, wallC, c + new Vector3(-s - 1, 3f, 0), new Vector3(2, 6, 2 * s + 4), default, true);

            // a few pillars for cover
            var pillar = new Color(0.6f, 0.55f, 0.5f);
            Vector3[] ps = { new Vector3(-7, 0, -5), new Vector3(7, 0, 5), new Vector3(-7, 0, 6), new Vector3(7, 0, -6) };
            foreach (var p in ps)
                Art.Box(root, pillar, c + p + Vector3.up * 2f, new Vector3(2f, 4f, 2f), new Vector3(0, 20, 0), true);
            // torches (lights) so the arena reads differently
            for (int i = 0; i < 4; i++)
            {
                var lg = new GameObject("arenaLight");
                lg.transform.SetParent(root, false);
                lg.transform.position = c + new Vector3(i < 2 ? -s + 1 : s - 1, 5f, i % 2 == 0 ? -s + 1 : s - 1);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.55f, 0.25f);
                l.range = 25f;
                l.intensity = 2f;
            }
        }
    }

    /// <summary>Marks static ground colliders so placement/overlap tests can ignore them.</summary>
    public class GroundMarker : MonoBehaviour { }
}
