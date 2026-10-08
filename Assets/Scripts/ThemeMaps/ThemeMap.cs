using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// THEME MAPS (the second batch): every one of these maps is its own class in its own file (ThemeMaps/Map.*.cs),
    /// overriding what it needs. ThemeMaps.cs asks ThemeMaps.Custom (the one for Cfg.Map, or null) at each of its hooks.
    /// Everything is built on every peer from the seed, so it must be deterministic (no UnityEngine.Random in building).
    /// </summary>
    public abstract class ThemeMap
    {
        public abstract MapKind Kind { get; }
        public abstract string Label { get; }
        public abstract string Blurb { get; }

        /// <summary>Wadeable water (ThemeMaps.WaterY) where the ground is below it; boats can be crafted.</summary>
        public virtual bool HasWater => false;

        /// <summary>Ground height at (x, z). Bases and the middle must stay flat at y = 0 (multiply by ThemeMaps.Mask).</summary>
        public abstract float Height(float x, float z);

        /// <summary>The ground's colours, and which one a triangle gets (centre, how upright it is: 1 flat, 0 a wall).</summary>
        public virtual Color[] Palette => new[] { new Color(0.4f, 0.55f, 0.28f), new Color(0.48f, 0.4f, 0.3f), new Color(0.33f, 0.47f, 0.25f) };
        public virtual int ColourAt(Vector3 c, float slopeY) => slopeY < 0.8f ? 1 : 0;

        /// <summary>The ground. Default: one smooth heightfield mesh over the whole square (ThemeMaps.HeightGround).</summary>
        public virtual void BuildGround(Transform root) => ThemeMaps.HeightGround(root, Cfg.MapHalf + 30f);

        /// <summary>Scenery (after the bases, dome and glass wall are built).</summary>
        public virtual void BuildProps(Transform root) { }

        /// <summary>Can a tree / bush / airdrop go here? (Called after the default checks: not under water, not too high.)</summary>
        public virtual bool SpotOk(Vector3 p) => true;
        /// <summary>The highest ground a tree / airdrop may stand on (the default check).</summary>
        public virtual float MaxSpotHeight => 3f;

        public virtual float NodeMul(byte kind) => 1f;
        public virtual Color LeafTint(Color leaf) => leaf;
        /// <summary>Its own trees: build the look under `tr` (the trunk collider + weak spots stay), hide the trunk's renderer, return true.</summary>
        public virtual bool BuildTree(Transform tr, int seed, float h, GameObject trunk) => false;

        /// <summary>Ice: you keep sliding the way you were going.</summary>
        public virtual bool Slippery(Vector3 p) => false;
        public virtual float SpeedMul(Vector3 p) => 1f;

        /// <summary>Players below this height die (falling off the map / into a hole). Keep it above -30 (the client's safety net).</summary>
        public virtual float KillY => float.NegativeInfinity;

        /// <summary>The usual mountains on the horizon and MapScenery's boulders; the glass dome's look (its invisible edge walls stay).</summary>
        public virtual bool Mountains => true;
        public virtual bool Dome => true;

        public virtual void ServerTick() { }
        /// <summary>Every frame on every peer while the map is up (moving platforms, ambience).</summary>
        public virtual void ClientTick() { }
        /// <summary>Once the map is built (every peer): sky, fog, light. Undo it in Cleanup.</summary>
        public virtual void ApplySky() { }
        /// <summary>The map is being torn down (back to the menu, another map).</summary>
        public virtual void Cleanup() { }
    }

    /// <summary>Runs the current custom map's ClientTick, and its Cleanup when the world goes.</summary>
    public class ThemeMapRunner : MonoBehaviour
    {
        public ThemeMap Map;
        void Update() { Map?.ClientTick(); }
        void OnDestroy() { Map?.Cleanup(); }
    }

    public static partial class ThemeMaps
    {
        static readonly Dictionary<MapKind, ThemeMap> s_Custom = new Dictionary<MapKind, ThemeMap>();

        /// <summary>Called by each map's static constructor... (we just list them here).</summary>
        static void RegisterAll()
        {
            if (s_Custom.Count > 0) return;
            foreach (var m in AllCustom()) s_Custom[m.Kind] = m;
        }

        /// <summary>The second batch of theme maps (each in ThemeMaps/Map.*.cs).</summary>
        static IEnumerable<ThemeMap> AllCustom()
        {
            foreach (var t in typeof(ThemeMap).Assembly.GetTypes())
                if (!t.IsAbstract && typeof(ThemeMap).IsAssignableFrom(t))
                    yield return (ThemeMap)System.Activator.CreateInstance(t);
        }

        /// <summary>The custom map being played (null on the first seven maps).</summary>
        public static ThemeMap Custom { get { RegisterAll(); return s_Custom.TryGetValue(Cfg.Map, out var m) ? m : null; } }
        public static ThemeMap CustomFor(MapKind k) { RegisterAll(); return s_Custom.TryGetValue(k, out var m) ? m : null; }

        // ---- helpers for the maps ----
        public static float SmoothStepP(float a, float b, float x) => SmoothStep(a, b, x);
        public static float SymNoiseP(float x, float z, float f, float o) => SymNoise(x, z, f, o);
        /// <summary>1 out in the wild, 0 on the bases and the ball drop zone (which must stay flat at y = 0).</summary>
        public static float MaskP(float x, float z) => Mask(x, z);
        public static float SeedP => Seed;

        /// <summary>A smooth-shaded heightfield over [-half, half]² from ThemeMaps.Height, coloured by the map's palette.</summary>
        public static GameObject HeightGround(Transform root, float half, float step = 2f)
        {
            int n = Mathf.CeilToInt(half * 2f / step);
            var hs = new float[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                hs[i, j] = Height(-half + i * step, -half + j * step);
            var pal = Palette();
            var mesh = MapBuilder.SmoothGround("ThemeTerrain", hs, half, step, pal.Length, (c, ny) => ColourAt(c, ny));
            var mats = new Material[pal.Length];
            for (int s = 0; s < pal.Length; s++) mats[s] = Art.Mat(pal[s]);
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<GroundMarker>();
            return go;
        }

        /// <summary>After MapBuilder.Build: the custom map's sky and its per-frame runner.</summary>
        public static void AfterBuild(Transform root)
        {
            var m = Custom;
            if (m == null) return;
            m.ApplySky();
            if (!m.Dome) foreach (var r in MapDome.Renderers) if (r != null) r.enabled = false;
            root.gameObject.AddComponent<ThemeMapRunner>().Map = m;
        }

        static float s_NextFall;
        /// <summary>Server: anyone below the map's KillY dies.</summary>
        static void ServerTickCustom()
        {
            var m = Custom;
            if (m == null) return;
            m.ServerTick();
            if (float.IsNegativeInfinity(m.KillY) || Time.time < s_NextFall) return;
            s_NextFall = Time.time + 0.2f;
            foreach (var p in PlayerNet.All.ToArray())
            {
                if (p.Dead.Value || p.Riding) continue;
                var at = p.transform.position;
                if (Mathf.Abs(at.x) > Cfg.MapHalf + 40f || Mathf.Abs(at.z) > Cfg.MapHalf + 40f) continue; // (the sudden death / waiting stadiums are far off)
                if (p.transform.position.y < m.KillY) p.ServerKill(null, KillCause.Fall);
            }
        }
    }
}
