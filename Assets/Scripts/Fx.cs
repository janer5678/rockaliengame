using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    // new kinds go at the end (they're sent over the network as bytes)
    public enum FxKind : byte { Blood, BloodHead, WoodChips, StoneChips, WeakSpot, Break, Smash, StructureHit, Spawn, C4Placed, Explosion, WandBeam, HelmetBreak, Craft, Drink, AirstrikeWarn, SniperTracer, PortalOpen, Timber, WeakSpotTree, Heal, BloodKill, LogBreak }

    /// <summary>
    /// Game feel: particles (blood, chips, sparks), camera shake/kick, floating damage numbers and sounds.
    /// Everything is local; Fx.Server broadcasts an effect to every peer through NetGame.
    /// </summary>
    public static class Fx
    {
        // ---------------- camera ----------------
        public static float Trauma;      // 0..1, decays; drives shake
        public static float KickPitch;   // degrees, decays; recoil
        public static float FovPunch;    // degrees, decays

        public static void Shake(float amount) => Trauma = Mathf.Clamp01(Trauma + amount);
        public static void Kick(float deg) => KickPitch += deg;
        public static void Punch(float deg) => FovPunch += deg;

        public static void TickCamera(float dt)
        {
            Trauma = Mathf.Max(0, Trauma - dt * 2.2f);
            KickPitch = Mathf.Lerp(KickPitch, 0, dt * 14f);
            FovPunch = Mathf.Lerp(FovPunch, 0, dt * 10f);
        }

        public static Vector3 ShakeEuler()
        {
            float s = Trauma * Trauma * 5f;
            float t = Time.time * 25f;
            return new Vector3((Mathf.PerlinNoise(t, 0) - 0.5f) * s - KickPitch, (Mathf.PerlinNoise(0, t) - 0.5f) * s, (Mathf.PerlinNoise(t, t) - 0.5f) * s * 0.6f);
        }

        // ---------------- damage numbers ----------------
        public struct Number { public Vector3 Pos; public float Value, Time; public bool Head, Kill; }
        public static readonly List<Number> Numbers = new List<Number>();

        public static void DamageNumber(Vector3 pos, float value, bool head, bool kill = false)
        {
            Numbers.Add(new Number { Pos = pos, Value = value, Time = Time.time, Head = head, Kill = kill });
            if (Numbers.Count > 20) Numbers.RemoveAt(0);
        }

        // ---------------- networked trigger ----------------

        /// <summary>Server: play an effect on every peer except `skipClient` (who already predicted it locally).</summary>
        public static void Server(FxKind kind, Vector3 pos, Vector3 dir, ulong skipClient = ulong.MaxValue)
        {
            if (NetGame.Instance != null && NetGame.Instance.IsSpawned) NetGame.Instance.FxRpc((byte)kind, pos, dir, skipClient);
        }

        public static void Play(FxKind kind, Vector3 pos, Vector3 dir)
        {
            switch (kind)
            {
                case FxKind.Blood: Blood(pos, dir, false); break;
                case FxKind.BloodHead: Blood(pos, dir, true); break;
                case FxKind.BloodKill: BloodKillBurst(pos, dir); break;
                case FxKind.WoodChips: Chips(pos, dir, BarkAt(pos), 8); FallingLeaves(pos, dir); HitMark(pos); Sfx.Play(Sfx.Chop, pos, 0.3f); break;
                case FxKind.StoneChips: Chips(pos, dir, Art.Stone, 8); Sparks(pos, dir, 5); Sfx.Play(Sfx.Clink, pos); break;
                case FxKind.WeakSpot: Sparks(pos, dir, 16); Sfx.Play(Sfx.Ding, pos, 0.8f); break;
                case FxKind.WeakSpotTree: Sparks(pos, dir, 16); Chips(pos, dir, BarkAt(pos), 6, 4.5f); TreeHitNote(pos); break;
                case FxKind.StructureHit: Chips(pos, dir, Art.DarkWood, 6); Sfx.Play(Sfx.Thud, pos); break;
                case FxKind.Break: Chips(pos, Vector3.up, Art.Wood, 26, 5f); Sfx.Play(Sfx.Smash, pos); break;
                case FxKind.Smash: Chips(pos, dir, Art.Wood, 30, 7f); Chips(pos, dir, Art.Stone, 12, 6f); Sfx.Play(Sfx.Smash, pos, 1f); break;
                case FxKind.Spawn:
                    Sfx.Play(Sfx.Hiss, pos + Vector3.up, 0.7f);
                    for (int i = 0; i < 6; i++)
                        FxParticle.Puff(pos + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.2f, 1.8f), Random.Range(-0.5f, 0.5f)), new Color(0.85f, 0.95f, 1f, 0.6f), Random.Range(0.6f, 1.1f));
                    break;
                case FxKind.C4Placed: C4Bomb.Spawn(pos, dir); break;
                case FxKind.Explosion: Explosion(pos); break;
                case FxKind.WandBeam: WandBeam(pos, dir); break;
                case FxKind.HelmetBreak: Chips(pos, Vector3.up, new Color(0.55f, 0.6f, 0.65f), 18, 4f); Sfx.Play(Sfx.Clink, pos, 1f, 0.2f); break;
                case FxKind.Craft: Machine.Pulse(Mathf.RoundToInt(dir.x)); break;
                case FxKind.AirstrikeWarn: AirstrikeZone.Spawn(pos, dir.x, dir.y); break;
                case FxKind.SniperTracer: Tracer(pos, dir); break;
                case FxKind.PortalOpen: Sparks(pos, dir, 25); Sfx.Play(Sfx.Portal, pos, 0.9f); break;
                case FxKind.Timber:
                {
                    // a felled tree bursts into chips and leaves in its own colours (PSX trees too)
                    var tree = ResourceNode.TreeNear(pos);
                    Chips(pos, Vector3.up, tree != null ? tree.Bark : Art.Wood, 40, 6f);
                    Chips(pos + Vector3.up * 2f, Vector3.up, tree != null ? tree.Leaf : Art.Leaves, 30, 5f);
                }
                    Sfx.Play(Sfx.Smash, pos, 1f);
                    break;
                case FxKind.LogBreak:
                    // a fallen log chopped right through: the same crack as a tree being felled
                    LogBreaks++;
                    Sfx.Play(Sfx.Smash, pos, 1f);
                    break;
                case FxKind.Drink:
                    for (int i = 0; i < 8; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.6f + Vector3.up, new Color(0.7f, 0.4f, 1f, 0.5f), Random.Range(0.4f, 0.8f));
                    Sfx.Play(Sfx.Zap, pos, 0.5f);
                    break;
                case FxKind.Heal:
                    // a horse fed a berry: green puffs rising off it and a munch
                    for (int i = 0; i < 10; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.7f, new Color(0.45f, 1f, 0.45f, 0.6f), Random.Range(0.3f, 0.6f));
                    Sparks(pos, Vector3.up, 8);
                    Sfx.Play(Sfx.Eat, pos, 0.8f);
                    break;
            }
        }

        /// <summary>Chip colour for a hit on a tree: that tree's bark (the plain wood colour if it's not a tree).</summary>
        static Color BarkAt(Vector3 pos)
        {
            var t = ResourceNode.WoodAt(pos, 0.8f);
            if (t == null) t = ResourceNode.TreeNear(pos, 2.5f);
            return t != null ? t.Bark : Art.Wood;
        }

        /// <summary>A hit on a tree or a log leaves a mark on its bark (ResourceNode.AddHitMark: on the bark you see).</summary>
        static void HitMark(Vector3 pos)
        {
            var t = ResourceNode.WoodAt(pos, 0.8f);
            if (t != null) t.AddHitMark(pos);
        }

        /// <summary>A hit on a tree shakes a few leaves (needles) loose: they flutter down out of its crown in its own leaf
        /// colour. Played wherever the hit's chips are (every peer that sees the hit).</summary>
        public static void FallingLeaves(Vector3 pos, Vector3 dir)
        {
            var w = ResourceNode.WoodAt(pos, 0.8f);
            if (w != null && w.Kind.Value == ResourceNode.Log) return; // (a fallen log has no leaves to drop)
            var t = ResourceNode.TreeNear(pos, 2.5f);
            if (t == null) return;
            var towards = pos + (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.zero) * 3f;
            int n = Random.Range(4, 8);
            for (int i = 0; i < n; i++)
                if (t.LeafFrom(towards, out var p, out var c)) FxLeaf.Spawn(p, c, Random.Range(0f, 0.35f));
        }

        static AudioClip[] s_TreeNotes;
        static int s_NoteStep;
        static float s_LastNote = -10f;
        static Vector3 s_LastNotePos;

        /// <summary>
        /// Hitting a tree's X: a chime (Resources/TreeHit, from the Samples pack). Hits in a row on the same tree climb up the
        /// scale (C D E G A C), like a combo; a pause or another tree starts it again from the bottom.
        /// </summary>
        static void TreeHitNote(Vector3 pos)
        {
            if (s_TreeNotes == null)
            {
                var l = new List<AudioClip>(Resources.LoadAll<AudioClip>("TreeHit"));
                l.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                s_TreeNotes = l.ToArray();
            }
            if (s_TreeNotes.Length == 0) { Sfx.Play(Sfx.Ding, pos, 0.8f); return; }
            if (Time.time - s_LastNote > 3f || (pos - s_LastNotePos).sqrMagnitude > 9f) s_NoteStep = 0;
            s_LastNote = Time.time;
            s_LastNotePos = pos;
            Sfx.Play(s_TreeNotes[Mathf.Min(s_NoteStep, s_TreeNotes.Length - 1)], pos, 0.85f, 0f, 60f);
            s_NoteStep++;
        }

        /// <summary>Test hooks: how many explosions this PC has shown, and the last one's camera distance, shake and the
        /// delay before its far-off boom was heard (-1: none, it was close).</summary>
        public static int Explosions { get; private set; }
        public static float LastBlastDistance { get; private set; }
        public static float LastBlastShake { get; internal set; }
        public static float LastBlastSoundDelay { get; internal set; } = -1f;
        static float s_BurstAt = -10f;
        static Vector3 s_BurstPos;
        static int s_BurstCount;

        /// <summary>
        /// An explosion (C4, a rocket, an airstrike's bombs, a fake bomb bush): a bright flash, a fireball that swells and
        /// burns out, a column of dark smoke rolling up and spreading, debris and burning bits flying out (trailing smoke),
        /// a shockwave ring racing out along the ground with a ring of dust, a scorch mark left behind, and the camera
        /// shaking by how close you are. Heard right across the map like in Rust: a sharp crack and boom close up; further
        /// off a deep rolling boom that arrives late (sound travels 343 m/s) and gets more muffled (low-passed) and quieter
        /// with distance - but never silent on the map. Several at once (an airstrike's carpet of bombs) only get the full
        /// show for the first two and only one far-off boom, so it stays cheap.
        /// </summary>
        public static void Explosion(Vector3 pos)
        {
            Explosions++;
            var cam = Camera.main;
            var cp = cam != null ? cam.transform.position : pos;
            float d = Vector3.Distance(cp, pos);
            bool burst = Time.time - s_BurstAt < 0.3f && (pos - s_BurstPos).sqrMagnitude < 50f * 50f;
            s_BurstCount = burst ? s_BurstCount + 1 : 1;
            s_BurstAt = Time.time;
            s_BurstPos = pos;
            LastBlastDistance = d;
            FxBlast.Spawn(pos, d, s_BurstCount <= 2, s_BurstCount == 1, s_BurstCount <= 3);
        }

        /// <summary>How hard an explosion this far away shakes the camera (0..1): hard right on top of it, fading out by about 70 m.</summary>
        public static float BlastShake(float d) => Mathf.Clamp01(1.25f - d / 45f) + (d < 70f ? 0.15f * (1f - d / 70f) : 0f);

        /// <summary>When a far-off explosion's boom is heard: sound at 343 m/s (none for close ones: the near boom covers it).</summary>
        public static float BoomDelay(float d) => d < 35f ? 0f : d / 343f;

        /// <summary>How muffled a far-off boom is: the low-pass cutoff (Hz) - crisp up close, a deep thud right across the map.</summary>
        public static float BoomCutoff(float d) => Mathf.Lerp(5000f, 380f, Mathf.Clamp01((d - 30f) / 350f));

        /// <summary>How loud a far-off boom is (before the SFX slider): it never drops to nothing on the map.</summary>
        public static float BoomVolume(float d) => Mathf.Lerp(1f, 0.32f, Mathf.Clamp01((d - 30f) / 600f));

        /// <summary>How many gun tracers have been drawn on this PC (tests: one shot = one tracer).</summary>
        public static int TracerCount;

        /// <summary>Sniper shot: a thin bright line that fades fast.</summary>
        public static void Tracer(Vector3 from, Vector3 to)
        {
            var d = to - from;
            if (d.magnitude < 0.1f) return;
            TracerCount++;
            var mat = new Material(Art.Ghost(new Color(1f, 0.95f, 0.6f, 0.9f)));
            var go = Art.Part(null, Art.Cube, Color.white, from + d * 0.5f, new Vector3(0.03f, 0.03f, d.magnitude), Quaternion.LookRotation(d).eulerAngles, false, mat);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FadeOut>().Init(mat, 0.3f);
            Sfx.Play(Sfx.Sniper, from, 1f, 0.03f, 250f);
            Sparks(to, -d, 10);
        }

        /// <summary>Death wand bolt from `from` to `to`.</summary>
        public static void WandBeam(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.1f) return;
            var mat = new Material(Art.Ghost(new Color(0.4f, 1f, 0.4f, 0.85f)));
            var go = Art.Part(null, Art.Cube, Color.white, from + d * 0.5f, new Vector3(0.12f, 0.12f, len), Quaternion.LookRotation(d).eulerAngles, false, mat);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FadeOut>().Init(mat, 0.45f);
            for (int i = 0; i < 6; i++) FxParticle.Puff(to + Random.insideUnitSphere * 1.2f, new Color(0.4f, 1f, 0.4f, 0.6f), Random.Range(0.8f, 1.6f));
            Sparks(to, -d, 20);
            Sfx.Play(Sfx.Zap, from, 0.9f);
            Sfx.Play(Sfx.Zap, to, 0.9f);
        }

        // ---------------- particles ----------------
        static readonly Color k_Blood = new Color(0.62f, 0.02f, 0.02f), k_BloodDark = new Color(0.35f, 0.0f, 0.0f), k_Spark = new Color(1f, 0.85f, 0.35f);

        /// <summary>For the tests: blood splashes / kill bursts played on this screen.</summary>
        public static int BloodCount, BloodKillCount;
        /// <summary>For the tests: fallen logs heard breaking (FxKind.LogBreak) on this machine.</summary>
        public static int LogBreaks;

        /// <summary>A kill: a big burst of blood out of the body - a fountain of drops thrown up and out (mostly away from
        /// whoever did it), a red mist and a wet thud. Played on every screen (PlayerNet.ServerDie).</summary>
        public static void BloodKillBurst(Vector3 pos, Vector3 dir)
        {
            BloodKillCount++;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up;
            for (int i = 0; i < 70; i++)
            {
                var v = dir * Random.Range(0.5f, 4.5f) + Random.insideUnitSphere * 3.5f + Vector3.up * Random.Range(1.5f, 4.5f);
                var p = pos + Random.insideUnitSphere * 0.25f;
                FxParticle.Spawn(p, v, i % 3 == 0 ? k_BloodDark : k_Blood, Random.Range(0.05f, 0.14f), Random.Range(0.7f, 1.4f), 14f, true);
            }
            for (int i = 0; i < 5; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.4f, new Color(0.7f, 0.04f, 0.04f, 0.6f), Random.Range(0.7f, 1.2f));
            Sfx.Play(Sfx.Flesh, pos, 1f, 0.05f);
            Sfx.Play(Sfx.Smash, pos, 0.45f, 0.1f);
            if (PsxModels.On) { BloodDecal(pos, true); BloodDecal(pos + Random.insideUnitSphere * 0.6f, true); }
        }

        public static void Blood(Vector3 pos, Vector3 dir, bool head)
        {
            BloodCount++;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up;
            int n = head ? 26 : 16;
            for (int i = 0; i < n; i++)
            {
                var v = (dir * Random.Range(1f, 4f) + Random.insideUnitSphere * 2.2f + Vector3.up * 1.2f) * (head ? 1.3f : 1f);
                FxParticle.Spawn(pos, v, i % 3 == 0 ? k_BloodDark : k_Blood, Random.Range(0.04f, head ? 0.11f : 0.08f), Random.Range(0.5f, 1.1f), 14f, true);
            }
            FxParticle.Puff(pos, new Color(0.7f, 0.05f, 0.05f, 0.55f), head ? 0.7f : 0.45f);
            Sfx.Play(head ? Sfx.Headshot : Sfx.Flesh, pos, head ? 1f : 0.8f);
            if (PsxModels.On) BloodDecal(pos, head);
        }

        /// <summary>PSX graphics: a splat of blood on the ground under a hit (the PSX blood decals), fading away after a while.</summary>
        static void BloodDecal(Vector3 pos, bool head)
        {
            if (!Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, out var hit, 4f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) return;
            if (hit.collider.GetComponentInParent<PlayerNet>() != null) return;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "psx blood";
            q.transform.position = hit.point + hit.normal * 0.02f + Random.insideUnitSphere * 0.15f;
            q.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            q.transform.localScale = Vector3.one * Random.Range(0.5f, 0.8f) * (head ? 1.4f : 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = PsxModels.Mat(Random.value < 0.5f ? "blood1" : "blood2");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(q, 30f);
        }

        public static void Chips(Vector3 pos, Vector3 dir, Color c, int n, float speed = 3.5f)
        {
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up;
            for (int i = 0; i < n; i++)
            {
                var v = dir * Random.Range(1f, speed) + Random.insideUnitSphere * speed * 0.6f + Vector3.up * 1.5f;
                FxParticle.Spawn(pos, v, i % 2 == 0 ? c : c * 0.8f, Random.Range(0.05f, 0.12f), Random.Range(0.5f, 1f), 16f, true);
            }
        }

        public static void Sparks(Vector3 pos, Vector3 dir, int n)
        {
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up;
            for (int i = 0; i < n; i++)
                FxParticle.Spawn(pos, dir * Random.Range(2f, 6f) + Random.insideUnitSphere * 3f, k_Spark, Random.Range(0.02f, 0.05f), Random.Range(0.15f, 0.35f), 6f, false);
        }
    }

    /// <summary>Fades a ghost material out and destroys the object.</summary>
    public class FadeOut : MonoBehaviour
    {
        Material m_Mat;
        Color m_Color;
        float m_Life, m_Max;

        public void Init(Material mat, float life)
        {
            m_Mat = mat;
            m_Color = mat.color;
            m_Life = m_Max = life;
        }

        void Update()
        {
            m_Life -= Time.deltaTime;
            if (m_Life <= 0) { Destroy(gameObject); return; }
            var c = m_Color;
            c.a *= m_Life / m_Max;
            m_Mat.SetColor("_BaseColor", c);
        }

        void OnDestroy()
        {
            if (m_Mat) Destroy(m_Mat);
        }
    }

    /// <summary>Airstrike warning: a pulsing red zone on the ground and a siren until the bombs fall.</summary>
    public class AirstrikeZone : MonoBehaviour
    {
        float m_End, m_NextBeep;
        Material m_Mat;

        public static void Spawn(Vector3 pos, float radius, float seconds)
        {
            var go = new GameObject("AirstrikeZone");
            go.transform.position = pos + Vector3.up * 0.2f;
            var z = go.AddComponent<AirstrikeZone>();
            z.m_Mat = new Material(Art.Ghost(new Color(1f, 0.1f, 0.05f, 0.35f)));
            var disc = Art.Part(go.transform, Art.Cylinder, Color.white, Vector3.zero, new Vector3(radius * 2f, 0.05f, radius * 2f), default, false, z.m_Mat);
            disc.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = Art.Part(go.transform, Art.Cylinder, Color.white, Vector3.up * 30f, new Vector3(0.6f, 30f, 0.6f), default, false, Art.Ghost(new Color(1f, 0.2f, 0.1f, 0.3f)));
            col.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            z.m_End = Time.time + seconds;
            Destroy(go, seconds + 0.2f);
        }

        void Update()
        {
            var c = new Color(1f, 0.1f, 0.05f, 0.2f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 6f)));
            m_Mat.SetColor("_BaseColor", c);
            if (Time.time >= m_NextBeep)
            {
                m_NextBeep = Time.time + 0.5f;
                Sfx.Play(Sfx.Beep, transform.position + Vector3.up * 2f, 1f, 0f);
            }
        }

        void OnDestroy()
        {
            if (m_Mat) Destroy(m_Mat);
        }
    }

    /// <summary>Draws every portal in NetGame.Portals as a glowing coloured oval on its surface.</summary>
    public static class PortalFx
    {
        static readonly List<GameObject> s_Shown = new List<GameObject>();
        static readonly List<PortalInfo> s_Info = new List<PortalInfo>();

        public static void Sync(NetGame g)
        {
            int n = g.Portals.Count;
            while (s_Shown.Count > n) { if (s_Shown[s_Shown.Count - 1]) Object.Destroy(s_Shown[s_Shown.Count - 1]); s_Shown.RemoveAt(s_Shown.Count - 1); }
            while (s_Info.Count > s_Shown.Count) s_Info.RemoveAt(s_Info.Count - 1);
            for (int i = 0; i < n; i++)
            {
                var p = g.Portals[i];
                // the oldest portals get cleared out (the portal gun never runs out), so a slot can change what it shows
                if (i < s_Shown.Count && s_Shown[i] != null && i < s_Info.Count && !s_Info[i].Equals(p)) { Object.Destroy(s_Shown[i]); s_Shown[i] = null; }
                if (i < s_Shown.Count && s_Shown[i] != null) { Animate(s_Shown[i], i); continue; }
                while (s_Info.Count <= i) s_Info.Add(default);
                s_Info[i] = p;
                var go = new GameObject("Portal");
                go.transform.SetPositionAndRotation(p.Pos, Quaternion.LookRotation(p.Normal.sqrMagnitude > 0.01f ? p.Normal : Vector3.up));
                var c = p.Color;
                // ring + glowing inside, 1.3 m wide and 2 m tall (flat along the surface)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI * 2f / 16f;
                    Art.Box(go.transform, c, new Vector3(Mathf.Cos(a) * 0.65f, Mathf.Sin(a) * 1.0f, 0), new Vector3(0.28f, 0.28f, 0.06f), new Vector3(0, 0, a * Mathf.Rad2Deg));
                }
                var inner = Art.Part(go.transform, Art.Cylinder, Color.white, Vector3.zero, new Vector3(1.3f, 0.02f, 2f), new Vector3(90, 0, 0), false, Art.Ghost(new Color(c.r, c.g, c.b, 0.55f)), "inner");
                inner.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var lg = new GameObject("light");
                lg.transform.SetParent(go.transform, false);
                lg.transform.localPosition = new Vector3(0, 0, 0.4f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = c;
                l.range = 5f;
                l.intensity = 2f;
                if (i < s_Shown.Count) s_Shown[i] = go; else s_Shown.Add(go);
            }
        }

        static void Animate(GameObject go, int i)
        {
            var inner = go.transform.Find("inner");
            if (inner) inner.localScale = new Vector3(1.3f, 0.02f, 2f) * (0.92f + 0.08f * Mathf.Sin(Time.time * 3f + i));
        }

        public static void Clear()
        {
            foreach (var g in s_Shown) if (g) Object.Destroy(g);
            s_Shown.Clear();
            s_Info.Clear();
        }
    }

    /// <summary>A C4 charge stuck where it landed, beeping faster and faster until it goes off (visual only; the server does the damage).</summary>
    public class C4Bomb : MonoBehaviour
    {
        float m_Born, m_NextBeep;
        GameObject m_Led;

        public static void Spawn(Vector3 pos, Vector3 normal)
        {
            var go = new GameObject("C4");
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            go.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, normal));
            var model = ItemModels.Create(Item.C4, go.transform);
            var b = go.AddComponent<C4Bomb>();
            b.m_Born = Time.time;
            var led = model.transform.Find("led");
            b.m_Led = led != null ? led.gameObject : null;
            Destroy(go, Cfg.C4Fuse + 0.1f);
        }

        void Update()
        {
            float left = Mathf.Max(0.05f, Cfg.C4Fuse - (Time.time - m_Born));
            float gap = Mathf.Clamp(left * 0.3f, 0.08f, 0.6f);
            if (Time.time >= m_NextBeep)
            {
                m_NextBeep = Time.time + gap;
                Sfx.Play(Sfx.Beep, transform.position, 0.6f, 0f);
            }
            if (m_Led) m_Led.SetActive(m_NextBeep - Time.time > gap * 0.5f);
        }
    }

    /// <summary>
    /// One explosion (Fx.Explosion): drives its flash, fireball, shockwave and delayed shake/boom over its first few
    /// seconds; the smoke, dust, debris and scorch mark are their own objects (FxSmoke, FxEmber, FxParticle).
    /// </summary>
    public class FxBlast : MonoBehaviour
    {
        /// <summary>How big the fireball gets (m across) and how far the shockwave ring races out (m).</summary>
        public const float FireballSize = 9f, ShockRadius = 18f;
        Light m_Flash;
        readonly List<(Transform t, Material m, float size, float delay, float bright)> m_Balls = new List<(Transform, Material, float, float, float)>();
        Transform m_Ring, m_Shell;
        Material m_RingMat, m_ShellMat;
        float m_Age, m_ShakeAt = -1f, m_Shake, m_Life;
        static Material s_ShellSrc;

        public static void Spawn(Vector3 pos, float dist, bool full, bool farBoom, bool nearBoom)
        {
            var go = new GameObject("explosion");
            go.transform.position = pos;
            var b = go.AddComponent<FxBlast>();
            b.Build(pos, dist, full, farBoom, nearBoom);
        }

        void Build(Vector3 pos, float d, bool full, bool farBoom, bool nearBoom)
        {
            var t = transform;
            // ---- the flash ----
            var lg = new GameObject("flash");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = Vector3.up * 1.5f;
            m_Flash = lg.AddComponent<Light>();
            m_Flash.type = LightType.Point;
            m_Flash.color = new Color(1f, 0.62f, 0.25f);
            m_Flash.range = full ? 45f : 25f;
            m_Flash.intensity = full ? 40f : 18f;
            m_Flash.shadows = LightShadows.None;

            // ---- the fireball: a white-hot core and orange lobes round it, swelling and burning out ----
            int balls = full ? 5 : 2;
            for (int i = 0; i < balls; i++)
            {
                bool core = i == 0;
                var c = core ? new Color(1f, 0.85f, 0.55f) : Color.Lerp(new Color(1f, 0.42f, 0.08f), new Color(1f, 0.62f, 0.2f), Random.value);
                var m = BeamFx.Ball(c, 0f, core ? 1.1f : 1.6f);
                var off = core ? Vector3.up * 1.2f : Vector3.Scale(Random.insideUnitSphere, new Vector3(2.2f, 1.2f, 2.2f)) + Vector3.up * 2f;
                var ball = BeamFx.Cylinder(t, m, "fireball");
                ball.GetComponent<MeshFilter>().sharedMesh = Art.Sphere;
                ball.transform.localPosition = off;
                ball.transform.localScale = Vector3.zero;
                float size = (core ? 1f : Random.Range(0.55f, 0.8f)) * FireballSize * (full ? 1f : 0.7f);
                m_Balls.Add((ball.transform, m, size, core ? 0f : Random.Range(0f, 0.08f), core ? 4.5f : 3f));
            }
            // a burst of flame puffs and fiery bits
            for (int i = 0; i < (full ? 16 : 6); i++)
                FxParticle.Puff(pos + Random.insideUnitSphere * 2.6f + Vector3.up * 1.2f, i % 3 == 0 ? new Color(1f, 0.85f, 0.3f, 0.85f) : new Color(1f, 0.45f, 0.1f, 0.75f), Random.Range(2.2f, 4.2f));
            Fx.Sparks(pos + Vector3.up * 0.5f, Vector3.up, full ? 45 : 15);

            // ---- the smoke: a dark column rolling up and spreading at the top, and dust racing out along the ground ----
            if (full)
            {
                for (int i = 0; i < 16; i++)
                {
                    float u = i / 15f;
                    var at = pos + Vector3.up * (0.8f + u * 2.5f) + Random.insideUnitSphere * 1.4f;
                    var v = new Vector3(Random.Range(-0.8f, 0.8f), Mathf.Lerp(3.5f, 9f, u) * Random.Range(0.8f, 1.15f), Random.Range(-0.8f, 0.8f));
                    float g = Random.Range(0.12f, 0.24f);
                    FxSmoke.Spawn(at, v, new Color(g, g * 0.95f, g * 0.9f, Random.Range(0.9f, 1f)), Random.Range(2f, 3f), Random.Range(6.5f, 10f) * Mathf.Lerp(0.8f, 1.3f, u),
                        Random.Range(6f, 10f), 0.25f, 0.6f, 0.05f + u * 0.12f);
                }
                for (int i = 0; i < 14; i++)
                {
                    float a = i * Mathf.PI * 2f / 14f + Random.Range(-0.15f, 0.15f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    FxSmoke.Spawn(pos + dir * 1.2f + Vector3.up * 0.4f, dir * Random.Range(11f, 16f) + Vector3.up * Random.Range(0.3f, 1.2f), new Color(0.45f, 0.4f, 0.34f, 0.45f),
                        1.2f, Random.Range(3.5f, 5f), Random.Range(2.2f, 3.4f), 0.1f, 2.6f, 0.12f);
                }
            }
            else
                for (int i = 0; i < 5; i++)
                    FxSmoke.Spawn(pos + Vector3.up * 1.5f + Random.insideUnitSphere, new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(3f, 6f), Random.Range(-0.6f, 0.6f)), new Color(0.2f, 0.19f, 0.18f, 0.6f),
                        1.6f, Random.Range(4f, 6f), Random.Range(4f, 6f), 0.3f, 0.6f, 0.3f);

            // ---- debris: chunks of wood and stone and charred bits, and burning bits trailing smoke ----
            int chunks = full ? 34 : 10;
            for (int i = 0; i < chunks; i++)
            {
                var c = i % 3 == 0 ? Art.Stone : i % 3 == 1 ? Art.Wood * 0.75f : new Color(0.12f, 0.11f, 0.1f);
                var v = Random.insideUnitSphere * 7f + Vector3.up * Random.Range(7f, 17f);
                FxParticle.Spawn(pos + Vector3.up * 0.6f, v, c, Random.Range(0.1f, 0.34f), Random.Range(1.4f, 2.6f), 20f, true);
            }
            for (int i = 0; i < (full ? 7 : 2); i++)
            {
                var v = Random.insideUnitSphere * 8f + Vector3.up * Random.Range(9f, 16f);
                FxEmber.Spawn(pos + Vector3.up * 0.8f, v);
            }

            // ---- the shockwave: a glowing ring racing out along the ground, and a pale shell of air swelling out ----
            if (full)
            {
                m_RingMat = BeamFx.Column(new Color(1f, 0.78f, 0.5f), 0f, 0.8f, 0f, 0f, 0.15f, 0.4f);
                var ring = BeamFx.Cylinder(t, m_RingMat, "shockwave");
                m_Ring = ring.transform;
                m_Ring.localPosition = Vector3.up * 0.4f;
                m_Ring.localScale = Vector3.zero;
                if (s_ShellSrc == null) s_ShellSrc = Art.Ghost(new Color(1f, 0.95f, 0.85f, 0.22f));
                m_ShellMat = new Material(s_ShellSrc) { name = "shock shell" };
                var shell = Art.Part(t, Art.Sphere, Color.white, Vector3.up * 1f, Vector3.zero, default, false, m_ShellMat, "shock shell");
                shell.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_Shell = shell.transform;
                Scorch(pos);
            }

            // ---- the camera: shaken by how close you are (now if it's close, when the boom gets to you if it's far) ----
            float shake = Fx.BlastShake(d);
            Fx.LastBlastShake = shake;
            if (d < 70f)
            {
                Fx.Shake(shake);
                Fx.Punch(Mathf.Clamp01(1f - d / 40f) * 6f);
            }
            else if (d < 420f)
            {
                m_ShakeAt = Fx.BoomDelay(d);
                m_Shake = 0.32f * (1f - (d - 70f) / 350f);
            }

            // ---- the sound: a sharp crack and boom close by; far off a deep, late, muffled boom heard right across the map ----
            if (nearBoom && d < 140f) Sfx.Play(Sfx.BigBoom, pos, Mathf.Lerp(1f, 0.6f, d / 140f), 0.05f, 220f);
            Fx.LastBlastSoundDelay = -1f;
            if (farBoom && d >= 35f)
            {
                float delay = Fx.BoomDelay(d);
                Fx.LastBlastSoundDelay = delay;
                Sfx.PlayFar(Sfx.FarBoom, pos, Fx.BoomVolume(d), delay, Fx.BoomCutoff(d));
            }
            m_Life = Mathf.Max(1.6f, m_ShakeAt + 0.1f);
        }

        /// <summary>A charred patch on the ground under it (fading away after a while).</summary>
        static void Scorch(Vector3 pos)
        {
            if (!Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 6f, ~(1 << PlayerNet.HitboxLayer), QueryTriggerInteraction.Ignore)) return;
            if (hit.collider.GetComponentInParent<PlayerNet>() != null) return;
            FxSmoke.Scorch(hit.point + hit.normal * 0.03f, hit.normal, Random.Range(4.5f, 6f));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Age += dt;
            float a = m_Age;
            if (m_Flash != null)
            {
                m_Flash.intensity *= Mathf.Exp(-dt * 7f);
                if (a > 0.7f) { Destroy(m_Flash.gameObject); m_Flash = null; }
            }
            foreach (var (bt, bm, size, delay, bright) in m_Balls)
            {
                if (bt == null) continue;
                float u = Mathf.Max(0f, a - delay);
                float grow = 1f - Mathf.Exp(-u * 9f);
                bt.localScale = Vector3.one * size * (0.25f + 0.75f * grow) * (1f + u * 0.12f);
                bt.localPosition += Vector3.up * dt * 2.2f; // (it rises as it burns)
                // white-hot at first, cooling to a deep orange as it burns out
                float burn = u < 0.06f ? u / 0.06f : Mathf.Exp(-(u - 0.06f) * 3.2f);
                var c = Color.Lerp(new Color(1f, 0.32f, 0.06f), new Color(1f, 0.85f, 0.6f), Mathf.Clamp01(1f - u * 2.2f));
                BeamFx.Set(bm, c, bright * burn);
                if (u > 1.4f && bt.gameObject.activeSelf) bt.gameObject.SetActive(false);
            }
            if (m_Ring != null)
            {
                float u = Mathf.Clamp01(a / 0.55f);
                float r = ShockRadius * (1f - (1f - u) * (1f - u));
                float cr = Art.Cylinder.bounds.extents.x, ch = Art.Cylinder.bounds.extents.y;
                m_Ring.localScale = new Vector3(r / cr, (0.5f + 0.6f * (1f - u)) / ch, r / cr);
                BeamFx.Set(m_RingMat, new Color(1f, 0.78f, 0.5f), 2.2f * (1f - u) * (1f - u));
                if (u >= 1f) { Destroy(m_Ring.gameObject); m_Ring = null; }
            }
            if (m_Shell != null)
            {
                float u = Mathf.Clamp01(a / 0.3f);
                m_Shell.localScale = Vector3.one * (ShockRadius * 0.8f * Mathf.Sqrt(u));
                var c = new Color(1f, 0.95f, 0.85f, 0.22f * (1f - u));
                m_ShellMat.SetColor("_BaseColor", c);
                m_ShellMat.color = c;
                if (u >= 1f) { Destroy(m_Shell.gameObject); m_Shell = null; }
            }
            if (m_ShakeAt >= 0f && a >= m_ShakeAt)
            {
                Fx.Shake(m_Shake);
                m_ShakeAt = -1f;
            }
            if (a > m_Life) Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var b in m_Balls) if (b.m) Destroy(b.m);
            if (m_RingMat) Destroy(m_RingMat);
            if (m_ShellMat) Destroy(m_ShellMat);
        }
    }

    /// <summary>
    /// A puff of smoke or dust (explosions): rises (or races out along the ground and slows), swells and thins out. Lit
    /// see-through (the sun shades it); one shared material, its colour per puff. Capped (they're only looks).
    /// </summary>
    public class FxSmoke : MonoBehaviour
    {
        public static int Alive { get; private set; }
        static Material s_Mat;
        static MaterialPropertyBlock s_Mpb;
        static readonly int k_Base = Shader.PropertyToID("_BaseColor");
        Renderer m_R;
        Vector3 m_Vel;
        Color m_C;
        float m_Age, m_Life, m_Size0, m_Size1, m_Rise, m_Drag, m_FadeIn, m_Spin;
        bool m_Decal;

        static Material Mat
        {
            get
            {
                if (s_Mat == null) s_Mat = new Material(Art.Ghost(new Color(0.2f, 0.2f, 0.2f, 0.6f))) { name = "smoke" };
                return s_Mat;
            }
        }

        /// <summary>A puff at pos moving at vel: colour (alpha = how thick), size from size0 to size1 (m), life (s), how
        /// fast it keeps rising (m/s²), how quickly it slows (drag), and when (fraction of its life) it's thickest.</summary>
        public static void Spawn(Vector3 pos, Vector3 vel, Color c, float size0, float size1, float life, float rise, float drag, float fadeIn)
        {
            if (Alive > 240) return;
            var go = Art.Part(null, Art.Ico, c, pos, Vector3.one * size0 * 0.4f, Random.rotation.eulerAngles, false, Mat, "smoke");
            var s = go.AddComponent<FxSmoke>();
            s.m_R = go.GetComponent<MeshRenderer>();
            s.m_R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.m_R.receiveShadows = false;
            s.m_Vel = vel; s.m_C = c; s.m_Life = life; s.m_Size0 = size0; s.m_Size1 = size1; s.m_Rise = rise; s.m_Drag = drag; s.m_FadeIn = Mathf.Clamp(fadeIn, 0.02f, 0.9f);
            s.m_Spin = Random.Range(-25f, 25f);
            s.Apply(0f);
            Alive++;
        }

        /// <summary>A charred patch lying on a surface: a dark disc that fades away over half a minute.</summary>
        public static void Scorch(Vector3 pos, Vector3 normal, float size)
        {
            var go = Art.Part(null, Art.Cylinder, Color.black, pos, Vector3.zero, default, false, Mat, "scorch");
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            float cr = Art.Cylinder.bounds.extents.x, ch = Art.Cylinder.bounds.extents.y;
            go.transform.localScale = new Vector3(size * 0.5f / cr, 0.01f / ch, size * 0.5f / cr);
            var s = go.AddComponent<FxSmoke>();
            s.m_R = go.GetComponent<MeshRenderer>();
            s.m_R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.m_C = new Color(0.05f, 0.045f, 0.04f, 0.7f);
            s.m_Life = 30f;
            s.m_Decal = true;
            s.Apply(0f);
            Alive++;
        }

        void Apply(float k)
        {
            if (s_Mpb == null) s_Mpb = new MaterialPropertyBlock();
            var c = m_C;
            c.a *= m_Decal ? Mathf.Clamp01((1f - k) * 4f) : Mathf.Clamp01(k / m_FadeIn) * Mathf.Clamp01((1f - k) * 1.6f);
            s_Mpb.SetColor(k_Base, c);
            m_R.SetPropertyBlock(s_Mpb);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Age += dt;
            float k = m_Age / m_Life;
            if (k >= 1f) { Destroy(gameObject); return; }
            Apply(k);
            if (m_Decal) return;
            m_Vel *= Mathf.Exp(-m_Drag * dt);
            m_Vel.y += m_Rise * dt;
            transform.position += (m_Vel + new Vector3(0.6f, 0f, 0.25f) * k) * dt; // (a little wind as it thins out)
            transform.Rotate(0f, m_Spin * dt, 0f, Space.World);
            float grow = 1f - Mathf.Pow(1f - k, 2.2f);
            transform.localScale = Vector3.one * Mathf.Lerp(m_Size0, m_Size1, grow) * 0.4f;
        }

        void OnDestroy() => Alive--;
    }

    /// <summary>A burning bit flung out of an explosion: a glowing ember arcing through the air, trailing smoke, then gone.</summary>
    public class FxEmber : MonoBehaviour
    {
        Vector3 m_Vel;
        float m_Age, m_Life, m_Next;

        public static void Spawn(Vector3 pos, Vector3 vel)
        {
            if (FxSmoke.Alive > 200) return;
            var go = Art.Part(null, Art.Cube, new Color(1f, 0.55f, 0.15f), pos, Vector3.one * Random.Range(0.14f, 0.24f), Random.rotation.eulerAngles);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var e = go.AddComponent<FxEmber>();
            e.m_Vel = vel;
            e.m_Life = Random.Range(1.2f, 2f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Age += dt;
            if (m_Age > m_Life) { Destroy(gameObject); return; }
            m_Vel += Vector3.down * 16f * dt;
            transform.position += m_Vel * dt;
            transform.Rotate(400f * dt, 300f * dt, 0f);
            if (m_Age >= m_Next)
            {
                m_Next = m_Age + 0.09f;
                float k = m_Age / m_Life;
                FxSmoke.Spawn(transform.position, Vector3.up * 0.5f, new Color(0.18f, 0.17f, 0.16f, 0.5f * (1f - k)), 0.5f, 1.5f, 1.2f, 0.2f, 1f, 0.1f);
            }
        }
    }

    /// <summary>Tiny cube particle with gravity; lands and lingers as a splat when `stick` is set.</summary>
    public class FxParticle : MonoBehaviour
    {
        static int s_Alive;
        Vector3 m_Vel;
        float m_Life, m_Max, m_Grav, m_Size;
        bool m_Stick, m_Landed, m_Puff;
        Material m_PuffMat;
        Color m_PuffColor;

        public static void Spawn(Vector3 pos, Vector3 vel, Color c, float size, float life, float grav, bool stick)
        {
            if (s_Alive > 350) return;
            var go = Art.Part(null, Art.Cube, c, pos, Vector3.one * size, Random.rotation.eulerAngles);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var p = go.AddComponent<FxParticle>();
            p.m_Vel = vel; p.m_Life = p.m_Max = life; p.m_Grav = grav; p.m_Size = size; p.m_Stick = stick;
            s_Alive++;
        }

        public static void Puff(Vector3 pos, Color c, float size)
        {
            var mat = new Material(Art.Ghost(c));
            var go = Art.Part(null, Art.Ico, c, pos, Vector3.one * size * 0.4f, default, false, mat);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var p = go.AddComponent<FxParticle>();
            p.m_Life = p.m_Max = 0.3f; p.m_Size = size; p.m_Puff = true; p.m_PuffMat = mat; p.m_PuffColor = c;
            s_Alive++;
        }

        /// <summary>A collider on something that moves about (a splat stuck to it would be left floating where it was).</summary>
        public static bool Moves(Collider c)
        {
            if (c == null) return false;
            if (c.attachedRigidbody != null) return true;
            if (c.GetComponentInParent<PlayerNet>() != null || c.GetComponentInParent<Vehicle>() != null || c.GetComponentInParent<Ball>() != null) return true;
            return Structure.IsDoorLeaf(c.transform);
        }

        /// <summary>For the tests: how many particles are flying / lying about right now, and the landed ones.</summary>
        public static int Alive => s_Alive;
        public static readonly List<FxParticle> Landed = new List<FxParticle>();
        public bool HasLanded => m_Landed;
        public Collider LandedOn { get; private set; }

        void OnDestroy()
        {
            Landed.Remove(this);
            s_Alive--;
            if (m_PuffMat) Destroy(m_PuffMat);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Life -= dt;
            if (m_Life <= 0) { Destroy(gameObject); return; }
            float k = m_Life / m_Max;
            if (m_Puff)
            {
                transform.localScale = Vector3.one * m_Size * Mathf.Lerp(1.3f, 0.4f, k);
                var c = m_PuffColor; c.a *= k;
                m_PuffMat.SetColor("_BaseColor", c);
                return;
            }
            if (!m_Landed)
            {
                m_Vel += Vector3.down * m_Grav * dt;
                var step = m_Vel * dt;
                // (it only settles on things that stay put: a drop that hit a horse used to stick to the horse's collider
                // where it was in the world, and when the horse ran off it was left hanging in mid-air - anything that
                // moves (horses, players, the ball, a swinging door, physics bodies) it falls past instead)
                if (m_Stick && Physics.Raycast(transform.position, step, out var hit, step.magnitude + 0.01f, ~0, QueryTriggerInteraction.Ignore)
                    && !Moves(hit.collider))
                {
                    var wood = hit.collider.GetComponentInParent<ResourceNode>();
                    if (wood != null && wood.Kind.Value == ResourceNode.Tree && hit.normal.y < 0.7f)
                    {
                        // (a tree's collider is round, round its ten-sided bark: a chip stuck flat on it sat partly inside the
                        // bark or floating off it - it bounces off and falls instead; the hit's own mark goes on the bark)
                        m_Vel = Vector3.Reflect(m_Vel, hit.normal) * 0.3f;
                        transform.localScale = Vector3.one * m_Size * Mathf.Clamp01(k * 2f);
                        return;
                    }
                    m_Landed = true;
                    LandedOn = hit.collider;
                    Landed.Add(this);
                    transform.position = hit.point + hit.normal * 0.005f;
                    transform.rotation = Quaternion.LookRotation(hit.normal) * Quaternion.Euler(0, 0, Random.Range(0, 90f));
                    transform.localScale = new Vector3(m_Size * 2.2f, m_Size * 2.2f, 0.01f);
                    m_Life = m_Max = Random.Range(3f, 6f);
                    return;
                }
                transform.position += step;
                transform.localScale = Vector3.one * m_Size * Mathf.Clamp01(k * 2f);
            }
            else transform.localScale = new Vector3(m_Size * 2.2f, m_Size * 2.2f, 0.01f) * Mathf.Clamp01(k * 3f);
        }
    }

    /// <summary>A leaf shaken off a tree by a hit: a small pointed card in the tree's leaf colour that drifts down slowly, swinging
    /// from side to side and tipping as it goes, lies on whatever it lands on for a moment and shrinks away.</summary>
    public class FxLeaf : MonoBehaviour
    {
        /// <summary>How many are falling or lying about right now (tests; capped, they're only looks).</summary>
        public static int Alive { get; private set; }
        public static Color LastColour { get; private set; }
        Vector3 m_Drift, m_Side;
        float m_Fall, m_Swing, m_Freq, m_Phase, m_Age, m_Delay, m_Life = 9f, m_Size, m_Spin;
        bool m_Landed;
        Renderer m_R;
        static Mesh s_Mesh;

        /// <summary>A pointed leaf, 1 long (x) and half as wide, drawn from both sides - and lit from both like its top
        /// (every normal points up), so one seen from underneath isn't a black speck.</summary>
        static Mesh LeafMesh
        {
            get
            {
                if (s_Mesh != null) return s_Mesh;
                var outline = new[] { new Vector3(-0.5f, 0, 0), new Vector3(-0.2f, 0.01f, 0.22f), new Vector3(0.2f, 0.01f, 0.2f), new Vector3(0.5f, 0, 0), new Vector3(0.2f, 0.01f, -0.2f), new Vector3(-0.2f, 0.01f, -0.22f) };
                var v = new List<Vector3>(outline);
                v.Add(new Vector3(0, 0.025f, 0)); // (the middle, a little raised: a slight fold)
                v.AddRange(v.ToArray());
                var t = new List<int>();
                for (int i = 0; i < 6; i++)
                {
                    int a = i, b = (i + 1) % 6;
                    t.Add(6); t.Add(a); t.Add(b);          // top (clockwise seen from above)
                    t.Add(13); t.Add(7 + b); t.Add(7 + a); // underneath
                }
                var n = new Vector3[v.Count];
                for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
                s_Mesh = new Mesh { name = "leaf" };
                s_Mesh.SetVertices(v);
                s_Mesh.normals = n;
                s_Mesh.SetTriangles(t, 0);
                s_Mesh.RecalculateBounds();
                return s_Mesh;
            }
        }

        public static void Spawn(Vector3 pos, Color c, float delay)
        {
            if (Alive >= 120) return;
            // three shades of the leaf colour, on the light side like the branch tips (the materials are cached by
            // colour, so there are only a few)
            int shade = Random.Range(0, 3);
            var col = shade == 0 ? c * 1.12f : shade == 1 ? c * 0.95f : Color.Lerp(c * 1.2f, new Color(0.62f, 0.8f, 0.3f), 0.25f);
            col.a = 1f;
            LastColour = col;
            float size = Random.Range(0.14f, 0.2f);
            var go = Art.Part(null, LeafMesh, col, pos, Vector3.one * size, new Vector3(0, Random.Range(0f, 360f), 0));
            go.name = "falling leaf";
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false; // (they'd be black specks in the tree's own shadow)
            var l = go.AddComponent<FxLeaf>();
            l.m_R = mr;
            l.m_Size = size;
            l.m_Delay = delay;
            l.m_Fall = Random.Range(0.9f, 1.4f);
            l.m_Swing = Random.Range(0.25f, 0.45f);
            l.m_Freq = Random.Range(2.2f, 3.4f);
            l.m_Phase = Random.Range(0f, Mathf.PI * 2f);
            l.m_Spin = Random.Range(-90f, 90f);
            float a = Random.Range(0f, Mathf.PI * 2f);
            l.m_Side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            l.m_Drift = new Vector3(0.25f, 0, 0.1f) * Random.Range(0.5f, 1.2f); // (the same way the wind blows the grass)
            if (delay > 0f) mr.enabled = false;
            Alive++;
        }

        void OnDestroy() => Alive--;

        void Update()
        {
            float dt = Time.deltaTime;
            if (m_Delay > 0f) { m_Delay -= dt; if (m_Delay <= 0f) m_R.enabled = true; return; }
            m_Age += dt;
            if (m_Landed)
            {
                m_Life -= dt;
                if (m_Life <= 0f) { Destroy(gameObject); return; }
                transform.localScale = Vector3.one * m_Size * Mathf.Clamp01(m_Life * 2f);
                return;
            }
            if (m_Age > 12f) { Destroy(gameObject); return; }
            // falling: a steady slow drop (after a moment speeding up to it), swinging side to side like a pendulum
            float fall = m_Fall * Mathf.Clamp01(m_Age * 2.5f);
            float w = m_Age * m_Freq + m_Phase;
            var step = (Vector3.down * fall + m_Side * Mathf.Cos(w) * m_Swing * m_Freq + m_Drift) * dt;
            if (Physics.Raycast(transform.position, step, out var hit, step.magnitude + 0.01f, ~0, QueryTriggerInteraction.Ignore)
                && !FxParticle.Moves(hit.collider))
            {
                m_Landed = true;
                transform.position = hit.point + hit.normal * 0.008f;
                transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                m_Life = Random.Range(2f, 3.5f);
                return;
            }
            transform.position += step;
            // tipping with the swing, and turning slowly
            var tilt = Vector3.Cross(Vector3.up, m_Side);
            transform.rotation = Quaternion.AngleAxis(Mathf.Sin(w) * 40f, tilt) * Quaternion.Euler(0, m_Age * m_Spin, 0);
        }
    }

    /// <summary>
    /// Sound effects, all synthesised procedurally at startup. Recorded clips dropped in Resources/Sfx ("name_0.ogg",
    /// "name_1.ogg", ... are variations picked at random) would replace them.
    /// </summary>
    public static class Sfx
    {
        public static AudioClip Swing, Flesh, Headshot, Chop, Clink, Thud, Ding, Smash, Twang, Throw, Pop, Eat, Place, Hurt, Kill, Step, Hiss, Boom, Beep, Zap, Saw, Hum,
            Hit, Rocket, Sniper, Portal, Jet, Glass, Door, Click, Crowd, Whiz, Slide, Hoof, UiHover, UiClick, UiSlide, EnemyStep,
            Workshop, ArmorClank, StoneGrind, Engine, Unlock,
            DoorWoodOpen, DoorWoodShut, DoorStoneOpen, DoorStoneShut, DoorMetalOpen, DoorMetalShut, DoorRefinedOpen, DoorRefinedShut, BigBoom, FarBoom;
        static readonly Dictionary<AudioClip, AudioClip[]> s_Variants = new Dictionary<AudioClip, AudioClip[]>();
        const int Rate = 44100;

        // runs on first access to any clip, so Sfx.Play(Sfx.Chop, ...) always gets a built clip
        static Sfx()
        {
            Build();
            LoadRecorded();
        }

        static void Build()
        {
            var rng = new System.Random(7);
            float N() => (float)rng.NextDouble() * 2f - 1f;

            Swing = Make("swing", 0.2f, (t, d) => { float f = Mathf.Sin(t / d * Mathf.PI); return N() * f * f * 0.35f; }, lowpass: 0.25f);
            Flesh = Make("flesh", 0.16f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(140, 60, t / d)) * Env(t, 0.12f) * 0.9f + N() * Env(t, 0.04f) * 0.5f, lowpass: 0.3f);
            Headshot = Make("headshot", 0.35f, (t, d) =>
                (Mathf.Sin(t * 2 * Mathf.PI * 1850) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 2780) * 0.3f) * Env(t, 0.25f) +
                Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(160, 60, t / d)) * Env(t, 0.1f) * 0.7f + N() * Env(t, 0.03f) * 0.4f);
            Chop = Make("chop", 0.14f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * 190) * Env(t, 0.08f) * 0.9f + N() * Env(t, 0.02f) * 0.6f, lowpass: 0.4f);
            Clink = Make("clink", 0.2f, (t, d) => (Mathf.Sin(t * 2 * Mathf.PI * 1250) * 0.4f + Mathf.Sin(t * 2 * Mathf.PI * 2150) * 0.25f + Mathf.Sin(t * 2 * Mathf.PI * 3300) * 0.15f) * Env(t, 0.1f) + N() * Env(t, 0.02f) * 0.5f);
            Thud = Make("thud", 0.2f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(110, 70, t / d)) * Env(t, 0.14f) + N() * Env(t, 0.03f) * 0.4f, lowpass: 0.3f);
            // something new unlocked (the workbench): a bright rising arpeggio, C E G C
            Unlock = Make("unlock", 1.1f, (t, d) =>
            {
                float v = 0f;
                float[] notes = { 1047f, 1319f, 1568f, 2093f };
                for (int i = 0; i < notes.Length; i++)
                {
                    float k = t - i * 0.12f;
                    if (k < 0f) continue;
                    v += (Mathf.Sin(k * 2 * Mathf.PI * notes[i]) * 0.4f + Mathf.Sin(k * 2 * Mathf.PI * notes[i] * 2f) * 0.12f) * Env(k, i == 3 ? 0.55f : 0.25f);
                }
                return v * 0.7f;
            });
            Ding = Make("ding", 0.4f, (t, d) => (Mathf.Sin(t * 2 * Mathf.PI * 1568) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 2350) * 0.25f) * Env(t, 0.3f));
            Smash = Make("smash", 0.7f, (t, d) => N() * Env(t, 0.35f) * 0.8f + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(80, 35, t / d)) * Env(t, 0.4f) * 0.9f, lowpass: 0.35f);
            Twang = Make("twang", 0.35f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * (210 + Mathf.Sin(t * 60) * 6)) * Env(t, 0.22f) * 0.6f + N() * Env(t, 0.02f) * 0.3f);
            Throw = Make("throw", 0.3f, (t, d) => { float f = Mathf.Sin(t / d * Mathf.PI); return N() * f * 0.45f; }, lowpass: 0.18f);
            Pop = Make("pop", 0.09f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(420, 950, t / d)) * Env(t, 0.07f) * 0.6f);
            Eat = Make("eat", 0.35f, (t, d) => N() * (Mathf.Sin(t * 70) > 0.3f ? 1f : 0.1f) * Env(t, 0.3f) * 0.5f, lowpass: 0.5f);
            Place = Make("build", 0.25f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * 95) * Env(t, 0.15f) * 0.9f + N() * Env(t, 0.05f) * 0.5f, lowpass: 0.3f);
            Hurt = Make("hurt", 0.2f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(90, 50, t / d)) * Env(t, 0.15f) + N() * Env(t, 0.06f) * 0.3f, lowpass: 0.2f);
            Kill = Make("kill", 0.5f, (t, d) => (t < 0.12f ? Mathf.Sin(t * 2 * Mathf.PI * 1318) : Mathf.Sin(t * 2 * Mathf.PI * 1976)) * Env(t < 0.12f ? t : t - 0.12f, 0.2f) * 0.5f);
            Hiss = Make("hiss", 0.9f, (t, d) => N() * Mathf.Min(1f, t * 20f) * Mathf.Exp(-t * 3f) * 0.5f, lowpass: 0.6f);
            Boom = Make("boom", 1.4f, (t, d) => N() * Env(t, 0.6f) * 0.9f + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(60, 25, t / d)) * Env(t, 0.8f), lowpass: 0.12f);
            // an explosion close by (C4, rockets): a sharp crack, a heavy thump that drops in pitch, a rumble and debris
            // pattering down after it
            BigBoom = Make("bigboom", 2.6f, (t, d) =>
                N() * Env(t, 0.035f) * 1.6f
                + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(78, 26, Mathf.Sqrt(t / d))) * Env(t, 0.75f) * 1.1f
                + N() * Env(t, 1.1f) * 0.75f
                + (t > 0.5f && N() > 0.93f ? N() * Env(t - 0.5f, 1.2f) * 0.8f : 0f), lowpass: 0.2f);
            // the same explosion heard from far across the map: a deep, rolling boom that comes and goes (echoes off the
            // hills), no crack (it's low-passed again by distance when it's played: Sfx.PlayFar)
            FarBoom = Make("farboom", 4.5f, (t, d) =>
            {
                float att = Mathf.Min(1f, t / 0.03f);
                float body = N() * 2.6f * Env(t, 1.5f) + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(46, 24, t / d)) * Env(t, 1.6f) * 0.9f;
                float echo1 = t > 0.55f ? N() * 1.6f * Env(t - 0.55f, 1.2f) : 0f;
                float echo2 = t > 1.3f ? N() * 1.1f * Env(t - 1.3f, 1.4f) : 0f;
                return (body + echo1 + echo2) * att * 0.75f;
            }, lowpass: 0.045f);
            Beep = Make("beep", 0.08f, (t, d) => Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * 1800)) * 0.25f);
            Zap = Make("zap", 0.3f, (t, d) => (Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(1400, 200, t / d)) * 0.5f + N() * 0.3f) * Env(t, 0.2f));
            Saw = Make("saw", 0.2f, (t, d) => (Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * 95)) * 0.3f + N() * 0.25f) * (0.7f + 0.3f * Mathf.Sin(t * 2 * Mathf.PI * 25)), lowpass: 0.35f);
            Hum = Make("hum", 1.5f, (t, d) => (Mathf.Sin(t * 2 * Mathf.PI * 55) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 110.5f) * 0.3f) * Mathf.Sin(t / d * Mathf.PI), lowpass: 0.5f);
            Hit = Make("hit", 0.05f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * 2400) * Env(t, 0.03f) * 0.4f);
            Rocket = Make("rocket", 0.6f, (t, d) => N() * Env(t, 0.4f) * 0.6f, lowpass: 0.2f);
            Sniper = Make("sniper", 0.5f, (t, d) => N() * Env(t, 0.15f) + Mathf.Sin(t * 2 * Mathf.PI * 70) * Env(t, 0.3f), lowpass: 0.3f);
            Portal = Make("portal", 0.5f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(300, 900, t / d)) * Env(t, 0.4f) * 0.4f);
            Jet = Make("jet", 0.3f, (t, d) => N() * 0.4f, lowpass: 0.25f);
            Glass = Make("glass", 0.4f, (t, d) => N() * Env(t, 0.2f) * 0.6f);
            Door = Make("door", 0.3f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * 120) * Env(t, 0.2f), lowpass: 0.3f);
            // stadium crowd: layered swelling noise (loops)
            Crowd = Make("crowd", 4f, (t, d) => N() * (0.35f + 0.15f * Mathf.Sin(t * 1.3f) + 0.1f * Mathf.Sin(t * 3.7f + 1f)) * Mathf.Min(1f, Mathf.Min(t, d - t) * 4f + 0.6f), lowpass: 0.08f);
            Click = Make("click", 0.03f, (t, d) => N() * Env(t, 0.01f));
            Step = Make("step", 0.08f, (t, d) => N() * Env(t, 0.03f) * 0.25f, lowpass: 0.15f);
            // another player's footstep: a heavy heel thump with a gritty scuff on top, loud enough to give them away
            // (PlayerNet.RemoteSounds plays it in 3D where their foot lands)
            EnemyStep = Make("enemystep", 0.14f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(95, 55, t / d)) * Env(t, 0.06f) * 0.9f
                + N() * Env(t, 0.035f) * 1.6f + N() * Env(Mathf.Max(0f, t - 0.035f), 0.03f) * (t > 0.035f ? 0.9f : 0f), lowpass: 0.3f);
            // an arrow / spear cutting through the air (loops while it flies, so you can hear where it is and where it's going)
            Whiz = Make("whiz", 1f, (t, d) => (N() * 0.55f + Mathf.Sin(t * 2 * Mathf.PI * 880) * 0.18f + Mathf.Sin(t * 2 * Mathf.PI * 1310) * 0.08f) * (0.8f + 0.2f * Mathf.Sin(t * 2 * Mathf.PI * 9)), lowpass: 0.45f);
            // sliding: a gritty scrape (loops)
            Slide = Make("slide", 1f, (t, d) => N() * (0.5f + 0.25f * Mathf.Sin(t * 2 * Mathf.PI * 13) + 0.15f * Mathf.Sin(t * 2 * Mathf.PI * 31)), lowpass: 0.12f);
            // menus: a soft blip when the mouse goes over a button, a crisp two-tone click, a tiny tick for sliders
            UiHover = Make("uihover", 0.05f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * 1320) * Env(t, 0.03f) * 0.18f);
            UiClick = Make("uiclick", 0.09f, (t, d) => (t < 0.03f ? Mathf.Sin(t * 2 * Mathf.PI * 880) : Mathf.Sin(t * 2 * Mathf.PI * 1760)) * Env(t < 0.03f ? t : t - 0.03f, 0.04f) * 0.35f + N() * Env(t, 0.004f) * 0.2f);
            UiSlide = Make("uislide", 0.025f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * 2600) * Env(t, 0.012f) * 0.15f);
            // a hoof on the ground
            // the workbench making something (2.2 s): a hand saw going back and forth, then three hammer knocks and a tap
            Workshop = Make("workshop", 2.2f, (t, d) =>
            {
                if (t < 1.25f)
                {
                    float stroke = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3.2f)); // push / pull
                    float teeth = 0.6f + 0.4f * Mathf.Sin(t * 2 * Mathf.PI * (stroke > 0.5f ? 46f : 38f));
                    return N() * stroke * teeth * 0.55f * Mathf.Min(1f, t * 12f);
                }
                float h = t - 1.25f;
                float k = h < 0.25f ? h : h < 0.5f ? h - 0.25f : h < 0.75f ? h - 0.5f : h - 0.75f;
                float pitch = h < 0.75f ? 150f : 420f;
                return Mathf.Sin(k * 2 * Mathf.PI * pitch) * Env(k, h < 0.75f ? 0.07f : 0.12f) * 0.9f + N() * Env(k, 0.015f) * 0.6f;
            }, lowpass: 0.45f);
            // armour going on: two metal clanks
            ArmorClank = Make("armorclank", 0.6f, (t, d) =>
            {
                float k = t < 0.22f ? t : t - 0.22f;
                return (Mathf.Sin(k * 2 * Mathf.PI * 620) * 0.4f + Mathf.Sin(k * 2 * Mathf.PI * 1480) * 0.3f + Mathf.Sin(k * 2 * Mathf.PI * 2710) * 0.15f) * Env(k, 0.16f) + N() * Env(k, 0.02f) * 0.5f;
            });
            // fortify: stone grinding and settling
            StoneGrind = Make("stonegrind", 1.4f, (t, d) => N() * (0.5f + 0.3f * Mathf.Sin(t * 2 * Mathf.PI * 7f)) * Mathf.Sin(t / d * Mathf.PI) * 0.7f
                + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(70, 45, t / d)) * Mathf.Sin(t / d * Mathf.PI) * 0.4f, lowpass: 0.1f);
            // wood gen: an engine coughing to life and a saw spinning up
            Engine = Make("engine", 1.6f, (t, d) =>
            {
                float rev = Mathf.Clamp01(t / 0.9f);
                float motor = Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(28f, 55f, rev))) * 0.35f;
                float saw = Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(300f, 900f, rev)) * 0.18f * rev;
                return (motor + saw + N() * 0.15f) * Mathf.Min(1f, (d - t) * 4f);
            }, lowpass: 0.35f);
            Hoof = Make("hoof", 0.09f, (t, d) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(520, 260, t / d)) * Env(t, 0.04f) * 0.8f + N() * Env(t, 0.01f) * 0.4f, lowpass: 0.5f);
            MakeDoorSounds();
        }

        /// <summary>
        /// Doors sound like what the base is made of (Structure.DoorSoundTier): wood creaks open on its hinges and shuts
        /// with a hollow knock; stone grinds open and shuts with a heavy thump; metal squeals and shuts with a ringing
        /// clang; refined (armoured) doors hiss open and shut with a deep clunk and a lock clicking home.
        /// </summary>
        static void MakeDoorSounds()
        {
            var rng = new System.Random(11);
            float N() => (float)rng.NextDouble() * 2f - 1f;
            // a hinge creak: a scratchy squeak sliding about in pitch, stuttering like a dry hinge
            float Creak(float t, float d, float f0, float f1)
            {
                float f = Mathf.Lerp(f0, f1, t / d) + Mathf.Sin(t * 23f) * 40f;
                float stick = 0.55f + 0.45f * Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * 31f));
                float saw = Mathf.Repeat(t * f, 1f) * 2f - 1f;
                return saw * stick * Mathf.Sin(Mathf.Clamp01(t / d) * Mathf.PI);
            }
            // a struck metal plate: a few inharmonic partials ringing out
            float Clang(float t, float f, float decay) =>
                (Mathf.Sin(t * 2 * Mathf.PI * f) * 0.45f + Mathf.Sin(t * 2 * Mathf.PI * f * 2.76f) * 0.3f + Mathf.Sin(t * 2 * Mathf.PI * f * 5.4f) * 0.18f
                 + Mathf.Sin(t * 2 * Mathf.PI * f * 8.93f) * 0.08f) * Env(t, decay);
            float Knock(float t, float f, float decay) => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(f, f * 0.6f, Mathf.Clamp01(t / decay))) * Env(t, decay);

            DoorWoodOpen = Make("doorwoodopen", 0.6f, (t, d) => Creak(t, 0.5f, 520f, 760f) * 0.32f + (t < 0.05f ? N() * Env(t, 0.02f) * 0.5f : 0f), lowpass: 0.55f);
            DoorWoodShut = Make("doorwoodshut", 0.45f, (t, d) => Knock(t, 135f, 0.16f) * 0.9f + Knock(t, 300f, 0.05f) * 0.35f + N() * Env(t, 0.03f) * 0.55f
                + (t > 0.09f ? Mathf.Sin((t - 0.09f) * 2 * Mathf.PI * 1900f) * Env(t - 0.09f, 0.02f) * 0.15f : 0f), lowpass: 0.35f);
            DoorStoneOpen = Make("doorstoneopen", 0.8f, (t, d) => N() * (0.55f + 0.35f * Mathf.Sin(t * 2 * Mathf.PI * 9f)) * Mathf.Sin(t / d * Mathf.PI) * 0.75f
                + Mathf.Sin(t * 2 * Mathf.PI * 55f) * Mathf.Sin(t / d * Mathf.PI) * 0.35f, lowpass: 0.09f);
            DoorStoneShut = Make("doorstoneshut", 0.6f, (t, d) => Knock(t, 70f, 0.3f) * 1f + N() * Env(t, 0.08f) * 0.8f
                + (t > 0.06f ? N() * Env(t - 0.06f, 0.12f) * 0.35f : 0f), lowpass: 0.15f);
            DoorMetalOpen = Make("doormetalopen", 0.7f, (t, d) => Creak(t, 0.6f, 900f, 1350f) * 0.22f + Clang(t, 410f, 0.08f) * 0.35f, lowpass: 0.7f);
            DoorMetalShut = Make("doormetalshut", 0.9f, (t, d) => Clang(t, 260f, 0.55f) * 0.75f + Knock(t, 95f, 0.12f) * 0.6f + N() * Env(t, 0.015f) * 0.5f, lowpass: 0.8f);
            DoorRefinedOpen = Make("doorrefinedopen", 0.8f, (t, d) =>
            {
                // the lock clicks back, then a hydraulic hiss as it swings
                float click = Mathf.Sin(t * 2 * Mathf.PI * 2600f) * Env(t, 0.015f) * 0.4f + N() * Env(t, 0.01f) * 0.3f;
                float k = t - 0.08f;
                float hiss = k > 0f ? N() * Mathf.Min(1f, k * 15f) * Mathf.Exp(-k * 3.5f) * 0.45f : 0f;
                float hum = k > 0f ? Mathf.Sin(t * 2 * Mathf.PI * 82f) * Mathf.Sin(Mathf.Clamp01(k / 0.7f) * Mathf.PI) * 0.25f : 0f;
                return click + hiss + hum;
            }, lowpass: 0.6f);
            DoorRefinedShut = Make("doorrefinedshut", 0.8f, (t, d) =>
            {
                // a deep armoured clunk, a short ring, and the bolt shooting home
                float clunk = Knock(t, 58f, 0.3f) * 1f + Clang(t, 190f, 0.25f) * 0.35f + N() * Env(t, 0.02f) * 0.4f;
                float k = t - 0.22f;
                float bolt = k > 0f ? (Mathf.Sin(k * 2 * Mathf.PI * 1500f) * 0.35f + Mathf.Sin(k * 2 * Mathf.PI * 3100f) * 0.2f) * Env(k, 0.035f) + N() * Env(k, 0.012f) * 0.35f : 0f;
                return clunk + bolt;
            }, lowpass: 0.5f);
        }

        /// <summary>The door opening / shutting sound for a base of this tier (0 wood, 1 stone, 2 metal, 3 refined).</summary>
        public static AudioClip DoorSound(int tier, bool open)
        {
            switch (Mathf.Clamp(tier, 0, 3))
            {
                case 0: return open ? DoorWoodOpen : DoorWoodShut;
                case 1: return open ? DoorStoneOpen : DoorStoneShut;
                case 2: return open ? DoorMetalOpen : DoorMetalShut;
                default: return open ? DoorRefinedOpen : DoorRefinedShut;
            }
        }

        /// <summary>Swap each synthesised clip for the recorded ones in Resources/Sfx when they exist.</summary>
        static void LoadRecorded()
        {
            var all = Resources.LoadAll<AudioClip>("Sfx");
            var groups = new Dictionary<string, List<AudioClip>>();
            foreach (var c in all)
            {
                int us = c.name.LastIndexOf('_');
                string key = us > 0 ? c.name.Substring(0, us) : c.name;
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<AudioClip>();
                l.Add(c);
            }
            foreach (var f in typeof(Sfx).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (f.FieldType != typeof(AudioClip)) continue;
                if (!groups.TryGetValue(f.Name.ToLowerInvariant(), out var l) || l.Count == 0) continue;
                l.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                f.SetValue(null, l[0]);
                s_Variants[l[0]] = l.ToArray();
            }
        }

        static AudioClip Pick(AudioClip clip) => s_Variants.TryGetValue(clip, out var v) ? v[Random.Range(0, v.Length)] : clip;

        /// <summary>Every sound effect follows the SFX slider (the master slider is the listener volume).</summary>
        static float Vol(AudioClip clip, float v) => v * GameSettings.SfxVolume;

        static float Env(float t, float decay) => Mathf.Exp(-t / Mathf.Max(0.001f, decay) * 3f);

        static AudioClip Make(string name, float dur, System.Func<float, float, float> f, float lowpass = 1f)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            float y = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = f(t, dur);
                y += (x - y) * lowpass;       // one-pole low-pass for thumpier sounds
                float fade = Mathf.Clamp01((n - i) / 200f); // declick
                data[i] = Mathf.Clamp(y, -1f, 1f) * fade;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static readonly Dictionary<int, AnimationCurve> s_Curves = new Dictionary<int, AnimationCurve>();

        /// <summary>
        /// How loud a sound is with distance (0 = at the source, 1 = `range` away): full volume close by, dropping off
        /// quickly like a real sound, then fading to silence at the range. Together with full 3D panning (no spread) it
        /// makes it easy to tell where something is and roughly how far.
        /// </summary>
        static AnimationCurve Falloff()
        {
            if (s_Curves.TryGetValue(0, out var c)) return c;
            c = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.04f, 0.9f), new Keyframe(0.12f, 0.55f), new Keyframe(0.3f, 0.25f), new Keyframe(0.55f, 0.1f), new Keyframe(1f, 0f));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            s_Curves[0] = c;
            return c;
        }

        /// <summary>Sets an AudioSource up as a clear positional sound audible out to `range` metres.</summary>
        public static void Spatial(AudioSource src, float range, float doppler = 0f)
        {
            src.spatialBlend = 1f;
            src.spread = 0f;
            src.dopplerLevel = doppler;
            src.minDistance = 1f;
            src.maxDistance = Mathf.Max(5f, range);
            src.rolloffMode = AudioRolloffMode.Custom;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Falloff());
        }

        /// <summary>3D sound at a world position with a little pitch variation. `range`: how far away it can be heard.</summary>
        public static void Play(AudioClip clip, Vector3 pos, float volume = 0.7f, float pitchVar = 0.08f, float range = 70f)
        {
            if (clip == null) return;
            volume = Vol(clip, volume);
            clip = Pick(clip);
            var go = new GameObject("sfx");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            Spatial(src, range);
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.5f, src.pitch) + 0.1f);
        }

        static AnimationCurve s_FarCurve;

        /// <summary>
        /// A sound from far off (an explosion right across the map): heard from where it is (panned that way, a little
        /// spread), after `delay` seconds (sound travels at 343 m/s), muffled by a low-pass filter (cutoff, Hz) and at
        /// `volume` - it carries the whole map, never fading out to nothing.
        /// </summary>
        public static AudioSource PlayFar(AudioClip clip, Vector3 pos, float volume, float delay, float cutoff)
        {
            if (clip == null) return null;
            volume = Vol(clip, volume);
            clip = Pick(clip);
            var go = new GameObject("sfxfar");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = 1f + Random.Range(-0.05f, 0.05f);
            src.spatialBlend = 1f;
            src.spread = 70f;
            src.dopplerLevel = 0f;
            src.minDistance = 1f;
            src.maxDistance = 3000f;
            src.rolloffMode = AudioRolloffMode.Custom;
            if (s_FarCurve == null) s_FarCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)); // (the volume's set by distance already)
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, s_FarCurve);
            var lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = Mathf.Clamp(cutoff, 100f, 22000f);
            lp.lowpassResonanceQ = 1f;
            src.PlayDelayed(Mathf.Max(0f, delay));
            Object.Destroy(go, delay + clip.length / Mathf.Max(0.5f, src.pitch) + 0.2f);
            return src;
        }

        /// <summary>A looping 3D sound following `parent` (flying arrows, sliding players). Returns it so it can be faded.</summary>
        public static AudioSource Loop(AudioClip clip, Transform parent, float volume, float pitch, float range, float doppler = 0f)
        {
            if (clip == null || parent == null) return null;
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = volume * GameSettings.SfxVolume;
            src.pitch = pitch;
            Spatial(src, range, doppler);
            src.time = Random.Range(0f, clip.length * 0.9f);
            src.Play();
            return src;
        }

        /// <summary>Menu sounds: a fixed pitch (sliders tick higher as they go up), never randomised.</summary>
        public static void PlayUi(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null) return;
            var go = new GameObject("sfxui");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume * GameSettings.SfxVolume;
            src.pitch = pitch;
            src.spatialBlend = 0f;
            src.ignoreListenerPause = true;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.3f, pitch) + 0.1f);
        }

        /// <summary>Non-positional sound for the local player (UI, own swings).</summary>
        public static void Play2D(AudioClip clip, float volume = 0.6f, float pitchVar = 0.06f)
        {
            if (clip == null) return;
            volume = Vol(clip, volume);
            clip = Pick(clip);
            var go = new GameObject("sfx2d");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            src.spatialBlend = 0f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.5f, src.pitch) + 0.1f);
        }
    }
}
