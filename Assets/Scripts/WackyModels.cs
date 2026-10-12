using UnityEngine;

namespace RockGame
{
    /// <summary>The wacky airdrop items' models (in your hand, in the world and as icons - ItemModels.Create hands them
    /// here): built along +Y with the grip at the origin, like every other item.</summary>
    public static class WackyModels
    {
        static readonly Color Red = new Color(0.92f, 0.2f, 0.18f), Yellow = new Color(1f, 0.85f, 0.15f), Blue = new Color(0.2f, 0.4f, 1f),
            Green = new Color(0.3f, 0.85f, 0.3f), Pink = new Color(1f, 0.45f, 0.8f), Purple = new Color(0.6f, 0.3f, 0.95f), Gold = new Color(1f, 0.78f, 0.15f),
            Silver = new Color(0.78f, 0.8f, 0.84f), Dark = new Color(0.15f, 0.15f, 0.18f), Wood = new Color(0.55f, 0.37f, 0.2f), Paper = new Color(0.96f, 0.94f, 0.85f);

        static Material Glow(Color c) => Art.Ghost(new Color(c.r, c.g, c.b, 0.55f));

        public static void Build(Item item, Transform t)
        {
            switch (item)
            {
                case Item.BouncyCastle:
                    // a toy bouncy castle in a capsule
                    Art.Box(t, Silver, new Vector3(0, 0.02f, 0), new Vector3(0.2f, 0.04f, 0.2f));
                    Art.Box(t, Red, new Vector3(0, 0.07f, 0), new Vector3(0.16f, 0.06f, 0.16f));
                    for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                    {
                        Art.Part(t, Art.Cylinder, Yellow, new Vector3(x * 0.065f, 0.12f, z * 0.065f), new Vector3(0.04f, 0.05f, 0.04f));
                        Art.Part(t, Art.Cone, Blue, new Vector3(x * 0.065f, 0.17f, z * 0.065f), new Vector3(0.05f, 0.05f, 0.05f));
                    }
                    Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 0.12f, 0), new Vector3(0.24f, 0.26f, 0.24f), default, false, Art.Ghost(new Color(0.85f, 0.95f, 1f, 0.25f)));
                    break;
                case Item.Decoy:
                {
                    // a mask of your own alien face on a stick
                    Art.Box(t, Wood, new Vector3(0, 0.12f, 0), new Vector3(0.02f, 0.26f, 0.02f));
                    var face = new Color(0.62f, 0.8f, 0.62f);
                    Art.Part(t, Art.Sphere, face, new Vector3(0, 0.33f, 0.01f), new Vector3(0.17f, 0.22f, 0.06f));
                    for (int s = -1; s <= 1; s += 2) Art.Part(t, Art.Sphere, Color.black, new Vector3(s * 0.04f, 0.35f, 0.04f), new Vector3(0.05f, 0.07f, 0.02f), new Vector3(0, 0, s * -25f));
                    Art.Box(t, Dark, new Vector3(0, 0.27f, 0.04f), new Vector3(0.04f, 0.006f, 0.01f));
                    break;
                }
                case Item.OmenMap:
                    // a rolled-up old map with a glowing purple eye on it
                    Art.Part(t, Art.Cylinder, new Color(0.88f, 0.8f, 0.6f), new Vector3(0, 0.12f, 0), new Vector3(0.06f, 0.13f, 0.06f));
                    Art.Part(t, Art.Cylinder, new Color(0.6f, 0.45f, 0.3f), new Vector3(0, 0.255f, 0), new Vector3(0.07f, 0.008f, 0.07f));
                    Art.Part(t, Art.Cylinder, new Color(0.6f, 0.45f, 0.3f), new Vector3(0, -0.005f, 0), new Vector3(0.07f, 0.008f, 0.07f));
                    Art.Part(t, Art.Sphere, Purple, new Vector3(0, 0.13f, 0.03f), new Vector3(0.04f, 0.025f, 0.01f));
                    Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 0.13f, 0.03f), new Vector3(0.06f, 0.05f, 0.04f), default, false, Glow(Purple));
                    Art.Box(t, Red, new Vector3(0, 0.12f, 0), new Vector3(0.065f, 0.012f, 0.065f));
                    break;
                case Item.GoldenRock:
                    Art.Part(t, Art.MakeRock(3, 0.25f), Gold, new Vector3(0, 0.02f, 0.04f), new Vector3(0.15f, 0.12f, 0.14f));
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.02f, 0.04f), new Vector3(0.18f, 0.15f, 0.17f), default, false, Art.Ghost(new Color(1f, 0.9f, 0.4f, 0.3f)));
                    break;
                case Item.BlueShell:
                {
                    Art.Part(t, Art.Sphere, Blue, new Vector3(0, 0.08f, 0), new Vector3(0.22f, 0.13f, 0.22f));
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, 0.05f, 0), new Vector3(0.24f, 0.012f, 0.24f));
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * 60f * Mathf.Deg2Rad;
                        Art.Part(t, Art.Cone, Color.white, new Vector3(Mathf.Cos(a) * 0.07f, 0.12f, Mathf.Sin(a) * 0.07f), new Vector3(0.035f, 0.05f, 0.035f), new Vector3(Mathf.Sin(a) * 35f, 0, -Mathf.Cos(a) * 35f));
                    }
                    for (int s = -1; s <= 1; s += 2) Art.Box(t, Color.white, new Vector3(s * 0.15f, 0.12f, 0), new Vector3(0.12f, 0.01f, 0.06f), new Vector3(0, 0, s * 20f));
                    break;
                }
                case Item.Connect4Wand:
                case Item.GameshowWand:
                {
                    bool c4 = item == Item.Connect4Wand;
                    Art.Box(t, c4 ? Dark : Gold, new Vector3(0, 0.2f, 0), new Vector3(0.025f, 0.45f, 0.025f));
                    if (c4)
                    {
                        // a little Connect 4 grid on top
                        Art.Box(t, Blue, new Vector3(0, 0.5f, 0), new Vector3(0.16f, 0.13f, 0.025f));
                        for (int x = 0; x < 4; x++) for (int y = 0; y < 3; y++)
                            Art.Part(t, Art.Cylinder, (x + y) % 3 == 0 ? Red : (x * 7 + y) % 3 == 1 ? Yellow : Dark, new Vector3(-0.054f + x * 0.036f, 0.465f + y * 0.035f, 0.013f), new Vector3(0.026f, 0.003f, 0.026f), new Vector3(90, 0, 0));
                    }
                    else
                    {
                        // a gold star with a question mark, and sparkles
                        for (int k = 0; k < 5; k++) Art.Part(t, Art.Cone, Gold, new Vector3(0, 0.5f, 0) + Quaternion.Euler(0, 0, k * 72f) * new Vector3(0, 0.045f, 0), new Vector3(0.05f, 0.07f, 0.02f), new Vector3(0, 0, k * 72f));
                        Art.Part(t, Art.Sphere, Yellow, new Vector3(0, 0.5f, 0), new Vector3(0.07f, 0.07f, 0.03f));
                        Art.Box(t, Pink, new Vector3(0, 0.51f, 0.018f), new Vector3(0.012f, 0.03f, 0.004f));
                        Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.5f, 0), Vector3.one * 0.14f, default, false, Glow(Pink));
                    }
                    break;
                }
                case Item.JumpBoots:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Art.Box(t, Red, new Vector3(s * 0.07f, 0.1f, 0), new Vector3(0.09f, 0.14f, 0.1f));
                        Art.Box(t, Red, new Vector3(s * 0.07f, 0.05f, 0.05f), new Vector3(0.09f, 0.06f, 0.16f));
                        Art.Box(t, Color.white, new Vector3(s * 0.07f, 0.17f, 0), new Vector3(0.095f, 0.02f, 0.105f));
                        for (int k = 0; k < 3; k++) Art.Part(t, Art.Cylinder, Silver, new Vector3(s * 0.07f, 0.01f - k * 0.018f, 0.05f), new Vector3(0.06f, 0.004f, 0.06f));
                    }
                    break;
                case Item.StickOfTruth:
                {
                    // a plain stick - the most powerful thing there is - with feathers and a ribbon tied on
                    Art.Box(t, new Color(0.5f, 0.36f, 0.22f), new Vector3(0, 0.25f, 0), new Vector3(0.03f, 0.55f, 0.03f), new Vector3(0, 0, 3));
                    Art.Box(t, new Color(0.45f, 0.32f, 0.2f), new Vector3(0.025f, 0.42f, 0), new Vector3(0.015f, 0.12f, 0.015f), new Vector3(0, 0, -35));
                    Art.Box(t, Red, new Vector3(0, 0.48f, 0), new Vector3(0.04f, 0.02f, 0.04f));
                    for (int k = 0; k < 3; k++) Art.Box(t, k == 1 ? Color.white : new Color(0.3f, 0.6f, 0.9f), new Vector3(0.02f + k * 0.01f, 0.43f - k * 0.025f, 0.01f), new Vector3(0.012f, 0.08f, 0.004f), new Vector3(0, 0, -20f - k * 15f));
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.55f, 0), Vector3.one * 0.08f, default, false, Glow(Yellow));
                    break;
                }
                case Item.SuicideBomb:
                    for (int k = 0; k < 5; k++) Art.Part(t, Art.Cylinder, Red, new Vector3(-0.08f + k * 0.04f, 0.1f, 0), new Vector3(0.035f, 0.1f, 0.035f));
                    Art.Box(t, Dark, new Vector3(0, 0.1f, 0.02f), new Vector3(0.22f, 0.03f, 0.03f));
                    Art.Box(t, Dark, new Vector3(0, 0.13f, 0.035f), new Vector3(0.08f, 0.05f, 0.02f));
                    Art.Box(t, new Color(0.3f, 1f, 0.3f), new Vector3(0, 0.135f, 0.046f), new Vector3(0.06f, 0.025f, 0.002f));
                    Art.Part(t, Art.Cylinder, Red * 1.1f, new Vector3(0, 0.215f, 0.0f), new Vector3(0.05f, 0.012f, 0.05f));
                    break;
                case Item.GravityStaff:
                    Art.Box(t, new Color(0.25f, 0.2f, 0.35f), new Vector3(0, 0.3f, 0), new Vector3(0.03f, 0.65f, 0.03f));
                    Art.Part(t, Art.Cylinder, Silver, new Vector3(0, 0.64f, 0), new Vector3(0.18f, 0.008f, 0.18f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cone, Purple, new Vector3(0, 0.67f, 0), new Vector3(0.07f, -0.1f, 0.07f));
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.64f, 0), Vector3.one * 0.12f, default, false, Glow(Purple));
                    break;
                case Item.AttentionSnail:
                {
                    var skin = new Color(0.55f, 0.6f, 0.45f);
                    Art.Part(t, Art.Capsule, skin, new Vector3(0, 0.03f, 0.02f), new Vector3(0.06f, 0.1f, 0.06f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Sphere, new Color(0.65f, 0.38f, 0.18f), new Vector3(0, 0.08f, -0.025f), new Vector3(0.09f, 0.1f, 0.1f));
                    for (int s = -1; s <= 1; s += 2) Art.Part(t, Art.Sphere, Color.black, new Vector3(s * 0.012f, 0.1f, 0.09f), Vector3.one * 0.012f);
                    break;
                }
                case Item.InvSwapper:
                    Art.Box(t, Dark, new Vector3(0, 0.08f, 0), new Vector3(0.16f, 0.16f, 0.06f));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Art.Part(t, Art.Cylinder, s < 0 ? new Color(0.2f, 0.85f, 1f) : new Color(1f, 0.55f, 0.1f), new Vector3(0, 0.08f + s * 0.035f, 0.031f), new Vector3(0.1f, 0.003f, 0.025f), new Vector3(90, 0, 0));
                        Art.Part(t, Art.Cone, s < 0 ? new Color(0.2f, 0.85f, 1f) : new Color(1f, 0.55f, 0.1f), new Vector3(s * 0.055f, 0.08f + s * 0.035f, 0.031f), new Vector3(0.03f, 0.03f, 0.005f), new Vector3(0, 0, s * 90f));
                    }
                    break;
                case Item.TimeoutBall:
                {
                    var b = new GameObject("ball").transform;
                    b.SetParent(t, false);
                    b.localPosition = new Vector3(0, 0.12f, 0);
                    TimeoutBall.BuildLook(b);
                    b.localScale = Vector3.one * 0.4f;
                    break;
                }
                case Item.KnockStick:
                    Art.Box(t, Wood, new Vector3(0, 0.22f, 0), new Vector3(0.035f, 0.5f, 0.035f));
                    Art.Part(t, Art.Sphere, Blue, new Vector3(0, 0.5f, 0.02f), new Vector3(0.13f, 0.12f, 0.15f));
                    Art.Part(t, Art.Sphere, Blue * 1.1f, new Vector3(0.06f, 0.49f, 0.06f), new Vector3(0.05f, 0.05f, 0.07f));
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, 0.43f, 0), new Vector3(0.08f, 0.02f, 0.08f));
                    break;
                case Item.KnockStick1:
                    // a huge pink foam bat
                    Art.Part(t, Art.Cylinder, Pink, new Vector3(0, 0.15f, 0), new Vector3(0.05f, 0.15f, 0.05f));
                    Art.Part(t, Art.Cylinder, Pink * 1.08f, new Vector3(0, 0.48f, 0), new Vector3(0.14f, 0.2f, 0.14f));
                    Art.Part(t, Art.Sphere, Pink * 1.08f, new Vector3(0, 0.68f, 0), new Vector3(0.14f, 0.06f, 0.14f));
                    Art.Box(t, Yellow, new Vector3(0, 0.5f, 0.071f), new Vector3(0.025f, 0.12f, 0.004f));
                    Art.Box(t, Yellow, new Vector3(0, 0.42f, 0.071f), new Vector3(0.025f, 0.025f, 0.004f));
                    break;
                case Item.ShieldPotion:
                    Art.Part(t, Art.Sphere, new Color(0.3f, 0.85f, 1f), new Vector3(0, 0.07f, 0), Vector3.one * 0.12f);
                    Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 0.08f, 0), Vector3.one * 0.15f, default, false, Art.Ghost(new Color(0.8f, 0.95f, 1f, 0.45f)));
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, 0.18f, 0), new Vector3(0.045f, 0.05f, 0.045f), default, false, Art.Ghost(new Color(0.8f, 0.95f, 1f, 0.45f)));
                    Art.Part(t, Art.Cylinder, Silver, new Vector3(0, 0.235f, 0), new Vector3(0.04f, 0.015f, 0.04f));
                    Art.Box(t, Silver, new Vector3(0, 0.08f, 0.075f), new Vector3(0.05f, 0.06f, 0.01f));
                    break;
                case Item.SlowGun:
                    Art.Box(t, Purple, new Vector3(0, 0.06f, 0.08f), new Vector3(0.07f, 0.08f, 0.24f));
                    Art.Box(t, Dark, new Vector3(0, -0.02f, 0.0f), new Vector3(0.05f, 0.12f, 0.05f), new Vector3(-15, 0, 0));
                    Art.Part(t, Art.Sphere, new Color(0.45f, 1f, 0.3f), new Vector3(0, 0.13f, 0.06f), new Vector3(0.08f, 0.08f, 0.1f), default, false, Glow(new Color(0.45f, 1f, 0.3f)));
                    Art.Part(t, Art.Cone, Silver, new Vector3(0, 0.06f, 0.21f), new Vector3(0.06f, 0.05f, 0.06f), new Vector3(90, 0, 0));
                    break;
                case Item.TreeBreaker:
                    Art.Box(t, Red, new Vector3(0, 0.27f, 0), new Vector3(0.05f, 0.6f, 0.05f));
                    Art.Box(t, Dark, new Vector3(0, 0.03f, 0), new Vector3(0.06f, 0.12f, 0.06f));
                    Art.Box(t, Silver, new Vector3(0, 0.56f, 0.09f), new Vector3(0.04f, 0.18f, 0.24f));
                    Art.Box(t, Color.white, new Vector3(0, 0.56f, 0.21f), new Vector3(0.03f, 0.2f, 0.02f));
                    Art.Box(t, Green, new Vector3(0, 0.56f, -0.03f), new Vector3(0.06f, 0.06f, 0.05f));
                    break;
                case Item.DrawMonster:
                    Art.Box(t, Paper, new Vector3(0, 0.12f, 0), new Vector3(0.18f, 0.24f, 0.015f));
                    Art.Box(t, Red, new Vector3(-0.03f, 0.15f, 0.009f), new Vector3(0.06f, 0.05f, 0.002f));
                    Art.Box(t, Color.black, new Vector3(-0.045f, 0.16f, 0.011f), new Vector3(0.012f, 0.012f, 0.002f));
                    Art.Box(t, Color.black, new Vector3(-0.015f, 0.16f, 0.011f), new Vector3(0.012f, 0.012f, 0.002f));
                    Art.Part(t, Art.Cylinder, Yellow, new Vector3(0.06f, 0.1f, 0.03f), new Vector3(0.015f, 0.09f, 0.015f), new Vector3(0, 0, -30));
                    Art.Part(t, Art.Cone, Pink, new Vector3(0.105f, 0.022f, 0.03f), new Vector3(0.015f, 0.03f, 0.015f), new Vector3(0, 0, 150));
                    break;
                case Item.BearJuice:
                {
                    var honey = new Color(0.95f, 0.6f, 0.1f);
                    Art.Part(t, Art.Cylinder, honey, new Vector3(0, 0.1f, 0), new Vector3(0.11f, 0.1f, 0.11f));
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, 0.1f, 0), new Vector3(0.12f, 0.105f, 0.12f), default, false, Art.Ghost(new Color(1f, 0.9f, 0.7f, 0.3f)));
                    Art.Part(t, Art.Cylinder, new Color(0.45f, 0.28f, 0.14f), new Vector3(0, 0.215f, 0), new Vector3(0.09f, 0.02f, 0.09f));
                    for (int s = -1; s <= 1; s += 2) Art.Part(t, Art.Sphere, new Color(0.45f, 0.28f, 0.14f), new Vector3(s * 0.04f, 0.24f, 0), Vector3.one * 0.035f);
                    Art.Box(t, Paper, new Vector3(0, 0.1f, 0.056f), new Vector3(0.07f, 0.05f, 0.004f));
                    break;
                }
                case Item.ChaosSigil:
                    // a giant rod topped with a crackling crystal of every colour
                    Art.Box(t, Dark, new Vector3(0, 0.35f, 0), new Vector3(0.04f, 0.8f, 0.04f));
                    for (int k = 0; k < 3; k++) Art.Box(t, Gold, new Vector3(0, 0.1f + k * 0.25f, 0), new Vector3(0.05f, 0.02f, 0.05f));
                    for (int k = 0; k < 6; k++) Art.Part(t, Art.Cone, Color.HSVToRGB(k / 6f, 0.8f, 1f), new Vector3(0, 0.8f, 0), new Vector3(0.05f, 0.18f, 0.05f), new Vector3(k * 30f - 75f, k * 60f, 0));
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.8f, 0), Vector3.one * 0.2f, default, false, Glow(Pink));
                    break;
                case Item.DroneBot:
                    Art.Box(t, Dark, new Vector3(0, 0.06f, 0), new Vector3(0.12f, 0.06f, 0.1f));
                    for (int s = -1; s <= 1; s += 2) Art.Part(t, Art.Cylinder, new Color(0.08f, 0.08f, 0.09f), new Vector3(s * 0.075f, 0.055f, 0), new Vector3(0.12f, 0.016f, 0.12f), new Vector3(0, 0, 90));
                    Art.Part(t, Art.Sphere, Red, new Vector3(0, 0.065f, 0.052f), Vector3.one * 0.025f);
                    Art.Box(t, Dark, new Vector3(0.03f, 0.12f, -0.03f), new Vector3(0.005f, 0.08f, 0.005f));
                    break;
                case Item.DanceBomb:
                    Art.Part(t, Art.Ico, Silver, new Vector3(0, 0.1f, 0), Vector3.one * 0.18f);
                    for (int k = 0; k < 8; k++) Art.Box(t, Color.HSVToRGB(k / 8f, 0.6f, 1f), new Vector3(0, 0.1f, 0) + Random.onUnitSphere * 0.088f, Vector3.one * 0.03f);
                    Art.Box(t, Dark, new Vector3(0, 0.2f, 0), new Vector3(0.02f, 0.04f, 0.02f));
                    Art.Box(t, Yellow, new Vector3(0, 0.235f, 0), new Vector3(0.006f, 0.03f, 0.006f), new Vector3(0, 0, 25));
                    break;
                case Item.DecoyBase:
                    // a little cardboard model of a base, glowing like a hologram
                    Art.Box(t, new Color(0.7f, 0.58f, 0.4f), new Vector3(0, 0.02f, 0), new Vector3(0.2f, 0.03f, 0.2f));
                    Art.Box(t, new Color(0.6f, 0.48f, 0.3f), new Vector3(0, 0.08f, 0), new Vector3(0.12f, 0.1f, 0.12f));
                    Art.Part(t, Art.Cone, Red, new Vector3(0, 0.14f, 0), new Vector3(0.16f, 0.07f, 0.16f));
                    Art.Box(t, Color.white, new Vector3(0, 0.09f, 0), new Vector3(0.22f, 0.18f, 0.22f), default, false, Glow(new Color(0.4f, 0.9f, 1f)));
                    break;
                case Item.BaseRebuild:
                    Art.Box(t, Red, new Vector3(0, 0.06f, 0), new Vector3(0.2f, 0.1f, 0.1f));
                    Art.Box(t, Dark, new Vector3(0, 0.13f, 0), new Vector3(0.08f, 0.04f, 0.02f));
                    Art.Box(t, Green, new Vector3(0, 0.2f, 0), new Vector3(0.03f, 0.07f, 0.03f));
                    Art.Part(t, Art.Cone, Green, new Vector3(0, 0.235f, 0), new Vector3(0.08f, 0.06f, 0.08f));
                    for (int k = 0; k < 3; k++) Art.Box(t, new Color(0.7f, 0.35f, 0.25f), new Vector3(-0.06f + k * 0.06f, 0.06f, 0.052f), new Vector3(0.05f, 0.025f, 0.004f));
                    break;
                case Item.FakeFlare:
                    Art.Part(t, Art.Cylinder, Red, new Vector3(0, 0.12f, 0), new Vector3(0.05f, 0.12f, 0.05f));
                    Art.Part(t, Art.Cylinder, Purple, new Vector3(0, 0.255f, 0), new Vector3(0.052f, 0.02f, 0.052f));
                    Art.Box(t, Yellow, new Vector3(0, 0.12f, 0.026f), new Vector3(0.02f, 0.05f, 0.002f));
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.28f, 0), Vector3.one * 0.05f, default, false, Glow(Purple));
                    break;
                case Item.DisguiseKit:
                    // the fake glasses, nose and moustache
                    for (int s = -1; s <= 1; s += 2) Art.Part(t, Art.Cylinder, Dark, new Vector3(s * 0.045f, 0.1f, 0), new Vector3(0.07f, 0.008f, 0.07f), new Vector3(90, 0, 0));
                    Art.Box(t, Dark, new Vector3(0, 0.1f, 0), new Vector3(0.03f, 0.008f, 0.008f));
                    Art.Part(t, Art.Sphere, new Color(1f, 0.75f, 0.65f), new Vector3(0, 0.07f, 0.02f), new Vector3(0.035f, 0.05f, 0.04f));
                    Art.Part(t, Art.Capsule, new Color(0.25f, 0.15f, 0.08f), new Vector3(0, 0.035f, 0.02f), new Vector3(0.025f, 0.05f, 0.02f), new Vector3(0, 0, 90));
                    break;
                case Item.BallGrabber:
                    Art.Box(t, Dark, new Vector3(0, 0.1f, 0), new Vector3(0.035f, 0.22f, 0.035f));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Art.Box(t, Red, new Vector3(s * 0.05f, 0.29f, 0), new Vector3(0.035f, 0.12f, 0.04f));
                        Art.Box(t, Silver, new Vector3(s * 0.05f, 0.36f, 0), new Vector3(0.036f, 0.03f, 0.041f));
                    }
                    Art.Box(t, Red, new Vector3(0, 0.23f, 0), new Vector3(0.135f, 0.035f, 0.04f));
                    Art.Box(t, Yellow, new Vector3(0, 0.12f, 0.019f), new Vector3(0.02f, 0.04f, 0.003f));
                    break;
                case Item.Scribble:
                    Art.Box(t, Blue, new Vector3(0, 0.12f, 0), new Vector3(0.17f, 0.23f, 0.03f));
                    Art.Box(t, Paper, new Vector3(0.003f, 0.12f, 0.003f), new Vector3(0.16f, 0.22f, 0.025f));
                    Art.Box(t, Color.white, new Vector3(-0.07f, 0.12f, 0.017f), new Vector3(0.01f, 0.2f, 0.002f));
                    Art.Part(t, Art.Cylinder, Yellow, new Vector3(0.09f, 0.12f, 0.02f), new Vector3(0.014f, 0.1f, 0.014f));
                    Art.Part(t, Art.Cone, Dark, new Vector3(0.09f, 0.02f, 0.02f), new Vector3(0.014f, -0.025f, 0.014f));
                    break;
                case Item.AntGun:
                    Art.Box(t, Silver, new Vector3(0, 0.06f, 0.07f), new Vector3(0.06f, 0.07f, 0.2f));
                    Art.Box(t, Dark, new Vector3(0, -0.02f, 0.0f), new Vector3(0.045f, 0.11f, 0.045f), new Vector3(-15, 0, 0));
                    for (int k = 0; k < 3; k++) Art.Part(t, Art.Cylinder, Green, new Vector3(0, 0.06f, 0.18f + k * 0.025f), new Vector3(0.05f - k * 0.012f, 0.006f, 0.05f - k * 0.012f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Sphere, Dark, new Vector3(0, 0.1f, 0.06f), new Vector3(0.02f, 0.015f, 0.04f));
                    break;
                case Item.ClutterBomb:
                    Art.Part(t, Art.Cone, Pink, new Vector3(0, 0.0f, 0), new Vector3(0.11f, 0.22f, 0.11f), new Vector3(180, 0, 0));
                    Art.Part(t, Art.Cylinder, Yellow, new Vector3(0, 0.0f, 0), new Vector3(0.115f, 0.01f, 0.115f));
                    for (int k = 0; k < 10; k++) Art.Box(t, Color.HSVToRGB(k / 10f, 0.8f, 1f), new Vector3(Mathf.Cos(k) * 0.04f, 0.02f + (k % 4) * 0.02f, Mathf.Sin(k) * 0.04f), Vector3.one * 0.02f, new Vector3(k * 30, k * 50, 0));
                    break;
            }
        }
    }
}
