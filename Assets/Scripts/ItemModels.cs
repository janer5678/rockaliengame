using UnityEngine;

namespace RockGame
{
    /// <summary>Low-poly held item models (used for the first-person viewmodel and third-person hand).</summary>
    public static class ItemModels
    {
        public static GameObject Create(Item item, Transform parent)
        {
            var root = new GameObject(item.ToString());
            root.transform.SetParent(parent, false);
            var t = root.transform;
            switch (item)
            {
                case Item.Rock:
                    Art.Part(t, Art.MakeRock(3, 0.25f), Art.Stone, new Vector3(0, 0.05f, 0.05f), new Vector3(0.09f, 0.075f, 0.085f));
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
                    Art.Box(t, new Color(0.8f, 0.7f, 0.5f), new Vector3(0, 0.5f, 0.0f), new Vector3(0.07f, 0.08f, 0.07f));
                    break;
                case Item.Pickaxe:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.25f, 0), new Vector3(0.05f, 0.62f, 0.05f));
                    Art.Box(t, Art.Stone, new Vector3(0, 0.53f, 0), new Vector3(0.06f, 0.07f, 0.5f), new Vector3(8, 0, 0));
                    break;
                case Item.Spear:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.3f, 0), new Vector3(0.04f, 1.5f, 0.04f));
                    Art.Part(t, Art.Cone, Art.Stone, new Vector3(0, 1.03f, 0), new Vector3(0.09f, 0.25f, 0.09f));
                    Art.Box(t, new Color(0.8f, 0.7f, 0.5f), new Vector3(0, 1.0f, 0), new Vector3(0.06f, 0.08f, 0.06f));
                    break;
                case Item.Bow:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.2f, 0.06f), new Vector3(0.04f, 0.4f, 0.04f), new Vector3(-20, 0, 0));
                    Art.Box(t, Art.Wood, new Vector3(0, -0.2f, 0.06f), new Vector3(0.04f, 0.4f, 0.04f), new Vector3(20, 0, 0));
                    Art.Box(t, new Color(0.9f, 0.9f, 0.85f), new Vector3(0, 0, -0.02f), new Vector3(0.01f, 0.75f, 0.01f));
                    break;
                case Item.CraftingTable:
                    Art.Box(t, Art.Wood, new Vector3(0, 0.1f, 0.05f), new Vector3(0.3f, 0.06f, 0.2f));
                    Art.Box(t, Art.DarkWood, new Vector3(0, 0.0f, 0.05f), new Vector3(0.26f, 0.14f, 0.16f));
                    break;
                case Item.Ram:
                    Art.Part(t, Art.Cylinder, Art.Wood, new Vector3(0, 0.1f, 0.1f), new Vector3(0.12f, 0.25f, 0.12f), new Vector3(90, 0, 0));
                    Art.Box(t, Art.Metal, new Vector3(0, 0.1f, 0.36f), new Vector3(0.14f, 0.14f, 0.05f));
                    break;
            }
            return root;
        }
    }
}
