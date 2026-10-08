using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The middle of the map without the crashed UFO (Cfg.CrashSiteOn = false): a ring of cover round the ball - chunky
    /// boulders, crate stacks and broken low stone walls - so a fight in the middle has things to duck behind. The same
    /// for every team (laid out in the first sector and copied round), solid, outside the ball's own few metres. A theme
    /// map can build its own middle instead (ThemeMap.BuildCentre).
    /// </summary>
    public static class CentreCover
    {
        static readonly Color k_Stone = new Color(0.56f, 0.55f, 0.52f), k_StoneDark = new Color(0.44f, 0.43f, 0.41f);

        public static void Build(Transform root)
        {
            var go = new GameObject("CentreCover");
            go.transform.SetParent(root, false);
            var t = go.transform;
            var rng = new System.Random(733 + Cfg.MapSeed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int copies = Cfg.Copies;
            float sector = 360f / copies;
            // per sector: a boulder, a crate stack and a broken wall, at different distances and angles
            var layout = new System.Collections.Generic.List<(int kind, float ang, float r, float yaw, float s)>();
            layout.Add((0, R(-0.35f, -0.15f) * sector, R(6.5f, 8f), R(0, 360), R(1.1f, 1.5f)));
            layout.Add((1, R(0.15f, 0.35f) * sector, R(7.5f, 9.5f), R(0, 90), 1f));
            layout.Add((2, R(-0.05f, 0.05f) * sector + sector * 0.5f, R(9f, 10.5f), 0f, 1f));
            for (int k = 0; k < copies; k++)
                foreach (var l in layout)
                {
                    float a = (l.ang + k * sector + 180f) * Mathf.Deg2Rad; // (sector 0 is blue's side, -z)
                    var p = new Vector3(Mathf.Sin(a) * l.r, 0, Mathf.Cos(a) * l.r);
                    p.y = MapBuilder.Height(p.x, p.z);
                    float face = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
                    switch (l.kind)
                    {
                        case 0:
                            Art.Part(t, Art.MakeRock(17 + (int)(l.s * 10), 0.3f), Color.Lerp(k_Stone, k_StoneDark, 0.3f), p + Vector3.up * 0.55f * l.s,
                                new Vector3(1.6f, 1.1f, 1.4f) * l.s, new Vector3(0, l.yaw, 0), true);
                            break;
                        case 1:
                            Art.Box(t, Art.Wood, p + Vector3.up * 0.55f, new Vector3(1.1f, 1.1f, 1.1f), new Vector3(0, face + l.yaw, 0), true);
                            Art.Box(t, Art.DarkWood, p + Quaternion.Euler(0, face + l.yaw, 0) * new Vector3(1.1f, 0.45f, 0.1f), new Vector3(0.9f, 0.9f, 0.9f), new Vector3(0, face + l.yaw + 20f, 0), true);
                            Art.Box(t, Art.Wood, p + Vector3.up * 1.45f + Quaternion.Euler(0, face, 0) * new Vector3(0.1f, 0, 0), new Vector3(0.8f, 0.7f, 0.8f), new Vector3(0, face + l.yaw + 35f, 0), true);
                            break;
                        default:
                        {
                            // a broken low wall across the way in, its top uneven
                            var rot = Quaternion.Euler(0, face + 90f, 0);
                            for (int b = -1; b <= 1; b++)
                            {
                                float h = b == 0 ? 1.3f : b < 0 ? 0.95f : 0.7f;
                                Art.Box(t, b == 0 ? k_Stone : k_StoneDark, p + rot * new Vector3(b * 1.05f, h * 0.5f, 0), new Vector3(1f, h, 0.55f), (rot * Quaternion.Euler(0, b * 4f, 0)).eulerAngles, true);
                            }
                            break;
                        }
                    }
                }
            if (AiPsxArt.On) AiPsxArt.Apply(t);
        }
    }
}
