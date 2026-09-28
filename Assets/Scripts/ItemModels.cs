using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// Low-poly item models, used for the first-person hands, the third-person hand and inventory icons.
    /// Convention: tools are built along +Y with the grip at the origin.
    /// </summary>
    public static class ItemModels
    {
        public static readonly Color Berry = new Color(0.75f, 0.08f, 0.2f), Leaf = new Color(0.25f, 0.5f, 0.2f), Twine = new Color(0.8f, 0.7f, 0.5f);

        public static GameObject Create(Item item, Transform parent)
        {
            var root = new GameObject(item.ToString());
            root.transform.SetParent(parent, false);
            var t = root.transform;
            switch (item)
            {
                case Item.Rock:
                    Art.Part(t, Art.MakeRock(3, 0.25f), Art.Stone, new Vector3(0, 0.02f, 0.04f), new Vector3(0.15f, 0.12f, 0.14f));
                    break;
                case Item.BuildingPlan:
                    Art.Box(t, new Color(0.35f, 0.55f, 0.95f), new Vector3(0, 0.1f, 0.05f), new Vector3(0.32f, 0.42f, 0.02f), new Vector3(-30, 0, 0));
                    Art.Box(t, Color.white, new Vector3(0, 0.12f, 0.04f), new Vector3(0.22f, 0.02f, 0.025f), new Vector3(-30, 0, 0));
                    Art.Box(t, Color.white, new Vector3(0, 0.05f, 0.08f), new Vector3(0.22f, 0.02f, 0.025f), new Vector3(-30, 0, 0));
                    Art.Part(t, Art.Cylinder, new Color(0.8f, 0.8f, 0.7f), new Vector3(0, 0.33f, -0.07f), new Vector3(0.05f, 0.17f, 0.05f), new Vector3(0, 0, 90));
                    break;
                case Item.Hatchet:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.25f, 0), new Vector3(0.05f, 0.6f, 0.05f));
                    Art.Box(t, Art.Stone, new Vector3(0, 0.5f, 0.1f), new Vector3(0.06f, 0.14f, 0.22f));
                    Art.Box(t, Twine, new Vector3(0, 0.5f, 0.0f), new Vector3(0.07f, 0.08f, 0.07f));
                    break;
                case Item.Pickaxe:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.25f, 0), new Vector3(0.05f, 0.62f, 0.05f));
                    Art.Box(t, Art.Stone, new Vector3(0, 0.53f, 0), new Vector3(0.06f, 0.07f, 0.5f), new Vector3(8, 0, 0));
                    Art.Box(t, Twine, new Vector3(0, 0.53f, 0), new Vector3(0.07f, 0.09f, 0.07f));
                    break;
                case Item.Spear:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.3f, 0), new Vector3(0.04f, 1.5f, 0.04f));
                    Art.Part(t, Art.Cone, Art.Stone, new Vector3(0, 1.03f, 0), new Vector3(0.09f, 0.25f, 0.09f));
                    Art.Box(t, Twine, new Vector3(0, 1.0f, 0), new Vector3(0.06f, 0.08f, 0.06f));
                    break;
                case Item.Bow:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.2f, 0.06f), new Vector3(0.04f, 0.4f, 0.04f), new Vector3(-20, 0, 0));
                    Art.Box(t, Art.Wood, new Vector3(0, -0.2f, 0.06f), new Vector3(0.04f, 0.4f, 0.04f), new Vector3(20, 0, 0));
                    Art.Box(t, Twine, new Vector3(0, 0, 0.06f), new Vector3(0.05f, 0.1f, 0.05f));
                    Art.Box(t, new Color(0.9f, 0.9f, 0.85f), new Vector3(0, 0, -0.02f), new Vector3(0.01f, 0.75f, 0.01f));
                    break;
                case Item.Ram:
                    // a heavy log carried in both hands, iron-capped at the front (+Z)
                    Art.Part(t, Art.Cylinder, Art.Wood, new Vector3(0, 0, 0.1f), new Vector3(0.24f, 0.6f, 0.24f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, 0, 0.68f), new Vector3(0.28f, 0.06f, 0.28f), new Vector3(90, 0, 0));
                    Art.Box(t, Art.Metal, new Vector3(0, 0, 0.76f), new Vector3(0.2f, 0.2f, 0.1f));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, 0, 0.3f), new Vector3(0.26f, 0.03f, 0.26f), new Vector3(90, 0, 0));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, 0, -0.2f), new Vector3(0.26f, 0.03f, 0.26f), new Vector3(90, 0, 0));
                    break;
                case Item.Chest:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.08f, 0), new Vector3(0.36f, 0.17f, 0.2f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.19f, 0), new Vector3(0.38f, 0.05f, 0.22f));
                    Art.Box(t, Art.Metal, new Vector3(-0.12f, 0.11f, 0), new Vector3(0.02f, 0.23f, 0.23f));
                    Art.Box(t, Art.Metal, new Vector3(0.12f, 0.11f, 0), new Vector3(0.02f, 0.23f, 0.23f));
                    Art.Box(t, new Color(0.85f, 0.7f, 0.25f), new Vector3(0, 0.15f, 0.11f), new Vector3(0.04f, 0.05f, 0.02f));
                    break;
                case Item.Barrier:
                    for (int k = -2; k <= 2; k++)
                    {
                        float h = 0.26f + (k & 1) * 0.04f;
                        Art.Box(t, k % 2 == 0 ? Art.Wood : Art.DarkWood, new Vector3(k * 0.07f, h * 0.5f, 0), new Vector3(0.06f, h, 0.06f));
                        Art.Part(t, Art.Cone, Art.Wood, new Vector3(k * 0.07f, h, 0), new Vector3(0.06f, 0.06f, 0.06f));
                    }
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.12f, 0.035f), new Vector3(0.36f, 0.04f, 0.02f));
                    break;
                case Item.Wood:
                    for (int k = 0; k < 3; k++)
                    {
                        var p = new Vector3((k - 1) * 0.07f + (k == 1 ? 0 : 0), k == 1 ? 0.12f : 0.05f, k == 1 ? 0 : 0.01f);
                        Art.Part(t, Art.Cylinder, k == 1 ? Art.Wood : Art.DarkWood, p, new Vector3(0.08f, 0.14f, 0.08f), new Vector3(90, 0, 0));
                        Art.Part(t, Art.Cylinder, new Color(0.8f, 0.65f, 0.42f), p + new Vector3(0, 0, 0.141f), new Vector3(0.07f, 0.002f, 0.07f), new Vector3(90, 0, 0));
                    }
                    break;
                case Item.Stone:
                    Art.Part(t, Art.MakeRock(11, 0.3f), Art.Stone, new Vector3(0, 0.06f, 0), new Vector3(0.12f, 0.09f, 0.11f));
                    Art.Part(t, Art.MakeRock(12, 0.3f), Art.Stone * 0.85f, new Vector3(0.09f, 0.04f, 0.05f), new Vector3(0.07f, 0.06f, 0.07f));
                    Art.Part(t, Art.MakeRock(13, 0.3f), new Color(0.5f, 0.5f, 0.52f), new Vector3(-0.07f, 0.03f, 0.06f), new Vector3(0.06f, 0.05f, 0.06f));
                    break;
                case Item.Arrow:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.2f, 0), new Vector3(0.02f, 0.7f, 0.02f));
                    Art.Part(t, Art.Cone, Art.Stone, new Vector3(0, 0.55f, 0), new Vector3(0.05f, 0.1f, 0.05f));
                    Art.Box(t, Color.white, new Vector3(0, -0.1f, 0), new Vector3(0.08f, 0.1f, 0.005f));
                    Art.Box(t, Color.white, new Vector3(0, -0.1f, 0), new Vector3(0.005f, 0.1f, 0.08f));
                    break;
                case Item.Berry:
                    Art.Part(t, Art.Sphere, Leaf, new Vector3(0, 0.06f, -0.02f), new Vector3(0.12f, 0.03f, 0.08f), new Vector3(20, 30, 0));
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * 1.05f;
                        var p = new Vector3(Mathf.Cos(a) * 0.035f, 0.05f + (k % 2) * 0.03f, Mathf.Sin(a) * 0.035f);
                        Art.Part(t, Art.Sphere, k % 3 == 0 ? Berry * 0.8f : Berry, p, Vector3.one * 0.045f);
                    }
                    break;
                case Item.C4:
                {
                    // taped brick of explosive with a detonator and a blinking LED
                    var putty = new Color(0.72f, 0.66f, 0.45f);
                    Art.Box(t, putty, new Vector3(-0.055f, 0.04f, 0), new Vector3(0.1f, 0.08f, 0.22f));
                    Art.Box(t, putty * 0.92f, new Vector3(0.055f, 0.04f, 0), new Vector3(0.1f, 0.08f, 0.22f));
                    Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, 0.04f, 0.06f), new Vector3(0.22f, 0.085f, 0.03f));
                    Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, 0.04f, -0.06f), new Vector3(0.22f, 0.085f, 0.03f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, 0.095f, 0), new Vector3(0.12f, 0.03f, 0.08f));
                    Art.Box(t, new Color(0.9f, 0.15f, 0.1f), new Vector3(0.03f, 0.1f, 0.05f), new Vector3(0.01f, 0.01f, 0.12f), new Vector3(0, 30, 0));
                    Art.Box(t, new Color(0.1f, 0.4f, 0.9f), new Vector3(-0.03f, 0.1f, 0.05f), new Vector3(0.01f, 0.01f, 0.12f), new Vector3(0, -30, 0));
                    Art.Box(t, new Color(1f, 0.1f, 0.05f), new Vector3(0.03f, 0.115f, -0.02f), new Vector3(0.025f, 0.02f, 0.025f)).name = "led";
                    break;
                }
                case Item.DeathWand:
                {
                    var bone = new Color(0.85f, 0.82f, 0.72f);
                    Art.Box(t, new Color(0.12f, 0.1f, 0.12f), new Vector3(0, 0.2f, 0), new Vector3(0.035f, 0.55f, 0.035f));
                    Art.Box(t, bone, new Vector3(0, 0.0f, 0), new Vector3(0.045f, 0.12f, 0.045f));
                    for (int k = 0; k < 3; k++)
                        Art.Box(t, bone, new Vector3(0, 0.46f, 0), new Vector3(0.02f, 0.14f, 0.02f), new Vector3(k * 120f, 0, 35f)).transform.localPosition = new Vector3(Mathf.Cos(k * 2.1f) * 0.04f, 0.5f, Mathf.Sin(k * 2.1f) * 0.04f);
                    Art.Part(t, Art.Ico, new Color(0.3f, 1f, 0.35f), new Vector3(0, 0.55f, 0), Vector3.one * 0.05f);
                    Art.Part(t, Art.Ico, Color.white, new Vector3(0, 0.55f, 0), Vector3.one * 0.075f, default, false, Art.Ghost(new Color(0.4f, 1f, 0.4f, 0.35f)));
                    break;
                }
                case Item.Helmet:
                {
                    // alien dome helmet with a dark visor; origin = centre of the head
                    var shell = new Color(0.55f, 0.6f, 0.66f);
                    Art.Part(t, Art.Sphere, shell, new Vector3(0, 0.05f, 0), new Vector3(0.36f, 0.3f, 0.4f));
                    Art.Part(t, Art.Sphere, new Color(0.1f, 0.12f, 0.16f), new Vector3(0, 0.0f, 0.1f), new Vector3(0.3f, 0.14f, 0.24f));
                    Art.Box(t, new Color(0.35f, 1f, 0.5f), new Vector3(0, 0.19f, 0), new Vector3(0.04f, 0.04f, 0.3f));
                    Art.Box(t, shell * 0.8f, new Vector3(0.17f, 0.02f, 0), new Vector3(0.04f, 0.1f, 0.1f));
                    Art.Box(t, shell * 0.8f, new Vector3(-0.17f, 0.02f, 0), new Vector3(0.04f, 0.1f, 0.1f));
                    break;
                }
                case Item.InvisPotion:
                {
                    var glass = Art.Ghost(new Color(0.8f, 0.9f, 1f, 0.45f));
                    Art.Part(t, Art.Sphere, new Color(0.6f, 0.25f, 0.95f), new Vector3(0, 0.07f, 0), Vector3.one * 0.12f);
                    Art.Part(t, Art.Sphere, Color.white, new Vector3(0, 0.08f, 0), Vector3.one * 0.15f, default, false, glass);
                    Art.Part(t, Art.Cylinder, Color.white, new Vector3(0, 0.18f, 0), new Vector3(0.045f, 0.05f, 0.045f), default, false, glass);
                    Art.Part(t, Art.Cylinder, new Color(0.5f, 0.35f, 0.2f), new Vector3(0, 0.235f, 0), new Vector3(0.04f, 0.015f, 0.04f));
                    break;
                }
                case Item.Chainsaw:
                {
                    // held in both hands like the ram; the bar points forward (+Z)
                    var body = new Color(0.95f, 0.5f, 0.1f);
                    Art.Box(t, body, new Vector3(0, 0, 0), new Vector3(0.16f, 0.2f, 0.34f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, 0.13f, -0.02f), new Vector3(0.05f, 0.05f, 0.26f));
                    Art.Box(t, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, 0.08f, -0.2f), new Vector3(0.05f, 0.14f, 0.05f));
                    Art.Box(t, Art.Metal, new Vector3(0, -0.01f, 0.42f), new Vector3(0.03f, 0.1f, 0.55f));
                    Art.Box(t, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, -0.01f, 0.42f), new Vector3(0.04f, 0.12f, 0.53f));
                    Art.Part(t, Art.Cylinder, Art.Metal, new Vector3(0, -0.01f, 0.69f), new Vector3(0.12f, 0.02f, 0.12f), new Vector3(0, 0, 90));
                    break;
                }
            }
            return root;
        }

        /// <summary>Spear model with its tip at the local origin, pointing along +Z (thrown, dropped and stuck spears).</summary>
        public static GameObject CreateSpearTipForward(Transform parent)
        {
            var go = Create(Item.Spear, parent);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            go.transform.localPosition = new Vector3(0, 0, -1.28f);
            return go;
        }

        /// <summary>Arrow model with its tip at the local origin, pointing along +Z (arrows stuck in the world).</summary>
        public static GameObject CreateArrowTipForward(Transform parent)
        {
            var go = Create(Item.Arrow, parent);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            go.transform.localPosition = new Vector3(0, 0, -0.6f);
            return go;
        }

        /// <summary>The objective ball (same look as the world ball), diameter = size.</summary>
        public static GameObject CreateBall(Transform parent, float size)
        {
            var root = new GameObject("ball");
            root.transform.SetParent(parent, false);
            float k = size / 1.24f;
            Art.Part(root.transform, Art.Ico, new Color(1f, 0.85f, 0.15f), Vector3.zero, Vector3.one * 0.62f * k);
            Art.Box(root.transform, new Color(0.9f, 0.5f, 0.1f), Vector3.zero, new Vector3(1.28f, 0.12f, 0.12f) * k);
            Art.Box(root.transform, new Color(0.9f, 0.5f, 0.1f), Vector3.zero, new Vector3(0.12f, 0.12f, 1.28f) * k);
            return root;
        }
    }
}
