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

        /// <summary>Its own berry bush look: build under `tr` (the bush's trigger collider stays), return true.</summary>
        public virtual bool BuildBush(Transform tr, int seed) => false;

        /// <summary>Its own rideable creature instead of the horse (ridden exactly like one). Build under `t`, like
        /// Vehicle.CreateVisual's horse: about the same size (rider's seat ~1.5 m up), `legs` = 4 transforms pivoting at the
        /// hips (they swing as it walks), `head` (it bobs / grazes), `tail`, and `saddle` (shown once saddled) holding a
        /// child named "blanket" (tinted the saddler's team colour). Give the body parts BoxColliders on
        /// PlayerNet.HitboxLayer when ghost == null (so shots hit it), and the head its own solid BoxCollider named "horse head".
        /// Return true.</summary>
        public virtual bool BuildMount(Transform t, Material ghost, bool unicorn, out Transform saddle, out Transform head, out Transform tail, List<Transform> legs)
        {
            saddle = head = tail = null;
            return false;
        }
        /// <summary>What the rideable creature is called ("Wild ..." / "Saddled ..."), null = Horse.</summary>
        public virtual string MountName => null;

        /// <summary>Its own middle of the map, round the ball (no crashed UFO any more - CentreCover.cs is the default
        /// cover). Return true if it built one (false = the default cover).</summary>
        public virtual bool BuildCentre(Transform root) => false;

        /// <summary>The bases' half size (the default 18 m: 36 x 36). A multiple of 3 (the build grid).</summary>
        public virtual float BaseHalfSize => 18f;
        /// <summary>How far each base's centre is from the middle (0 = the usual). A multiple of 3.</summary>
        public virtual float BaseDistance => 0f;
        /// <summary>Boats can be crafted from the start (no trade station).</summary>
        public virtual bool BoatsAnytime => false;
        /// <summary>Two teams: the bases sit on the diagonal ((-d,-d) and (d,d), d = BaseDistance along each axis) and the
        /// glass wall between them is turned to match. Three or four teams stay where they always are.</summary>
        public virtual bool DiagonalBases => false;
        /// <summary>Its water is too deep to wade: off a boat you can't swim - you sink (slowly, no jumping) and drown at KillY.</summary>
        public virtual bool DeepWater => false;
        /// <summary>This version of the map is the one played (a map can keep an older version as a backup class with
        /// the same Kind: exactly one of them must be enabled).</summary>
        public virtual bool Enabled => true;
        /// <summary>A player who falls to their death respawns this many times as fast.</summary>
        public virtual float FallRespawnMul => 1f;

        /// <summary>Ice: you keep sliding the way you were going.</summary>
        public virtual bool Slippery(Vector3 p) => false;
        /// <summary>How fast your speed can change on its ice (m/s²; Frostlake's is 3.5 - lower slides more).</summary>
        public virtual float IceGrip => 3.5f;
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
        void OnDestroy() { ThemeMaps.RunnerGone(this); }
    }

    public static partial class ThemeMaps
    {
        static readonly Dictionary<MapKind, ThemeMap> s_Custom = new Dictionary<MapKind, ThemeMap>();

        /// <summary>Called by each map's static constructor... (we just list them here).</summary>
        static void RegisterAll()
        {
            if (s_Custom.Count > 0) return;
            foreach (var m in AllCustom()) if (m.Enabled) s_Custom[m.Kind] = m;
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

        // ---- the game's own lighting, exactly as it was before any of these maps touched it ----
        static bool s_Pristine;
        static Material s_PSky;
        static bool s_PSkyTint, s_PSkyExp, s_PSkyGround, s_PSkyThick;
        static Color s_PTint, s_PGround;
        static float s_PExp, s_PThick;
        static bool s_PFog;
        static FogMode s_PFogMode;
        static Color s_PFogCol, s_PAmbSky, s_PAmbEq, s_PAmbGround, s_PAmbLight, s_PSunCol;
        static float s_PFogDens, s_PFogStart, s_PFogEnd, s_PAmbInt, s_PSunInt;
        static UnityEngine.Rendering.AmbientMode s_PAmbMode;
        static Light s_PSun;
        static ThemeMap s_Live;
        static ThemeMapRunner s_LiveRunner;

        static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.enabled) return l;
            return null;
        }

        /// <summary>Once, before any of these maps has changed anything: the sky, fog, ambient light and sun as the game has them.</summary>
        static void SnapshotPristine()
        {
            if (s_Pristine) return;
            s_Pristine = true;
            s_PSky = RenderSettings.skybox;
            if (s_PSky != null)
            {
                if (s_PSkyTint = s_PSky.HasProperty("_SkyTint")) s_PTint = s_PSky.GetColor("_SkyTint");
                if (s_PSkyExp = s_PSky.HasProperty("_Exposure")) s_PExp = s_PSky.GetFloat("_Exposure");
                if (s_PSkyGround = s_PSky.HasProperty("_GroundColor")) s_PGround = s_PSky.GetColor("_GroundColor");
                if (s_PSkyThick = s_PSky.HasProperty("_AtmosphereThickness")) s_PThick = s_PSky.GetFloat("_AtmosphereThickness");
            }
            s_PFog = RenderSettings.fog; s_PFogMode = RenderSettings.fogMode; s_PFogCol = RenderSettings.fogColor;
            s_PFogDens = RenderSettings.fogDensity; s_PFogStart = RenderSettings.fogStartDistance; s_PFogEnd = RenderSettings.fogEndDistance;
            s_PAmbMode = RenderSettings.ambientMode; s_PAmbSky = RenderSettings.ambientSkyColor; s_PAmbEq = RenderSettings.ambientEquatorColor;
            s_PAmbGround = RenderSettings.ambientGroundColor; s_PAmbLight = RenderSettings.ambientLight; s_PAmbInt = RenderSettings.ambientIntensity;
            s_PSun = FindSun();
            if (s_PSun != null) { s_PSunCol = s_PSun.color; s_PSunInt = s_PSun.intensity; }
        }

        /// <summary>Puts the game's own lighting back exactly (a theme map is going, or being swapped for another). The maps'
        /// own save-and-restore could save another map's light as "how it was" when one replaced another (the menu's map
        /// preview, a new match), and the game stayed tinted - golden swamp light everywhere - for the rest of the session.</summary>
        static void RestorePristine()
        {
            if (!s_Pristine) return;
            RenderSettings.skybox = s_PSky;
            if (s_PSky != null)
            {
                if (s_PSkyTint) s_PSky.SetColor("_SkyTint", s_PTint);
                if (s_PSkyExp) s_PSky.SetFloat("_Exposure", s_PExp);
                if (s_PSkyGround) s_PSky.SetColor("_GroundColor", s_PGround);
                if (s_PSkyThick) s_PSky.SetFloat("_AtmosphereThickness", s_PThick);
            }
            RenderSettings.fog = s_PFog; RenderSettings.fogMode = s_PFogMode; RenderSettings.fogColor = s_PFogCol;
            RenderSettings.fogDensity = s_PFogDens; RenderSettings.fogStartDistance = s_PFogStart; RenderSettings.fogEndDistance = s_PFogEnd;
            RenderSettings.ambientMode = s_PAmbMode; RenderSettings.ambientSkyColor = s_PAmbSky; RenderSettings.ambientEquatorColor = s_PAmbEq;
            RenderSettings.ambientGroundColor = s_PAmbGround; RenderSettings.ambientLight = s_PAmbLight; RenderSettings.ambientIntensity = s_PAmbInt;
            if (s_PSun != null) { s_PSun.color = s_PSunCol; s_PSun.intensity = s_PSunInt; }
            DynamicGI.UpdateEnvironment();
            WorldLook.Apply(); // (the sky's tint from Settings > Display, as the player has it)
        }

        /// <summary>Test hook: is the lighting exactly the game's own right now?</summary>
        public static bool LightingIsPristine()
        {
            if (!s_Pristine) return true;
            return RenderSettings.skybox == s_PSky && RenderSettings.fog == s_PFog && RenderSettings.ambientMode == s_PAmbMode
                && RenderSettings.ambientSkyColor == s_PAmbSky && RenderSettings.ambientEquatorColor == s_PAmbEq && RenderSettings.ambientGroundColor == s_PAmbGround
                && (s_PSun == null || (s_PSun.color == s_PSunCol && Mathf.Approximately(s_PSun.intensity, s_PSunInt)));
        }

        /// <summary>After MapBuilder.Build (every map): the last theme map's light goes (back to the game's own, exactly),
        /// then this one's sky and per-frame runner if it's one of these maps.</summary>
        public static void AfterBuild(Transform root)
        {
            SnapshotPristine();
            if (s_Live != null)
            {
                var old = s_Live;
                s_Live = null;
                s_LiveRunner = null;
                try { old.Cleanup(); } catch (System.Exception e) { Debug.LogException(e); }
                RestorePristine();
            }
            var m = Custom;
            if (m == null) return;
            m.ApplySky();
            if (!m.Dome) foreach (var r in MapDome.Renderers) if (r != null) r.enabled = false;
            s_Live = m;
            s_LiveRunner = root.gameObject.AddComponent<ThemeMapRunner>();
            s_LiveRunner.Map = m;
        }

        /// <summary>A map's runner went with its world: if that map is still the live one (nothing replaced it), its light goes.</summary>
        internal static void RunnerGone(ThemeMapRunner r)
        {
            if (r != s_LiveRunner || s_Live == null) return;
            var old = s_Live;
            s_Live = null;
            s_LiveRunner = null;
            try { old.Cleanup(); } catch (System.Exception e) { Debug.LogException(e); }
            RestorePristine();
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
