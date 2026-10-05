using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// The tree disguise coming off: the tree that stood there is left behind for a moment, squashing down into the
    /// ground and shrinking away to nothing (the player is already standing there as themselves). Only a picture: no
    /// colliders, gone in Time seconds. (Going on is PlayerNet.TreeGrowScale.)
    /// </summary>
    public class TreeCamoPoof : MonoBehaviour
    {
        public const float Time = 0.28f;
        /// <summary>For the tests: how many are shrinking away right now.</summary>
        public static int Alive { get; private set; }
        float m_Age;
        Vector3 m_Scale;

        public static void Begin(GameObject tree)
        {
            if (tree == null) return;
            tree.transform.SetParent(null, true);
            foreach (var c in tree.GetComponentsInChildren<Collider>()) c.enabled = false;
            var p = tree.AddComponent<TreeCamoPoof>();
            p.m_Scale = tree.transform.localScale;
            Alive++;
        }

        void Update()
        {
            m_Age += UnityEngine.Time.deltaTime;
            float k = Mathf.Clamp01(m_Age / Time);
            // a little wider as it's pressed down, then nothing
            float y = 1f - k * k, xz = (1f + 0.25f * Mathf.Sin(k * Mathf.PI)) * (1f - k * k * k);
            transform.localScale = new Vector3(m_Scale.x * xz, m_Scale.y * Mathf.Max(0.01f, y), m_Scale.z * xz);
            if (k >= 1f) Destroy(gameObject);
        }

        void OnDestroy() => Alive--;
    }
}
