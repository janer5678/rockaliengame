using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    // new kinds go at the end (they're sent over the network as bytes)
    public enum FxKind : byte { Blood, BloodHead, WoodChips, StoneChips, WeakSpot, Break, Smash, StructureHit, Spawn, C4Placed, Explosion, WandBeam, HelmetBreak, Craft, Drink, AirstrikeWarn, SniperTracer, PortalOpen, Timber, WeakSpotTree }

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
                case FxKind.WoodChips: Chips(pos, dir, BarkAt(pos), 8); Sfx.Play(Sfx.Chop, pos, 0.3f); break;
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
                case FxKind.Drink:
                    for (int i = 0; i < 8; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.6f + Vector3.up, new Color(0.7f, 0.4f, 1f, 0.5f), Random.Range(0.4f, 0.8f));
                    Sfx.Play(Sfx.Zap, pos, 0.5f);
                    break;
            }
        }

        /// <summary>Chip colour for a hit on a tree: that tree's bark (the plain wood colour if it's not a tree).</summary>
        static Color BarkAt(Vector3 pos)
        {
            var t = ResourceNode.TreeNear(pos, 2.5f);
            return t != null ? t.Bark : Art.Wood;
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

        public static void Explosion(Vector3 pos)
        {
            Sfx.Play(Sfx.Boom, pos, 1f, 0.05f, 180f);
            for (int i = 0; i < 14; i++)
                FxParticle.Puff(pos + Random.insideUnitSphere * 1.8f + Vector3.up * 0.6f, i % 3 == 0 ? new Color(1f, 0.85f, 0.3f, 0.8f) : new Color(1f, 0.45f, 0.1f, 0.7f), Random.Range(1.5f, 3.2f));
            for (int i = 0; i < 8; i++)
                FxParticle.Puff(pos + Random.insideUnitSphere * 2.5f + Vector3.up * 1.5f, new Color(0.25f, 0.23f, 0.22f, 0.6f), Random.Range(2f, 3.5f));
            Chips(pos, Vector3.up, Art.Wood, 30, 9f);
            Chips(pos, Vector3.up, Art.Stone, 20, 8f);
            Sparks(pos, Vector3.up, 30);
            var cam = Camera.main;
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, pos);
                Shake(Mathf.Clamp01(1.2f - d / 30f));
            }
            var lg = new GameObject("boomLight");
            lg.transform.position = pos + Vector3.up;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.2f);
            l.range = 18f;
            l.intensity = 8f;
            Object.Destroy(lg, 0.25f);
        }

        /// <summary>Sniper shot: a thin bright line that fades fast.</summary>
        public static void Tracer(Vector3 from, Vector3 to)
        {
            var d = to - from;
            if (d.magnitude < 0.1f) return;
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

        public static void Blood(Vector3 pos, Vector3 dir, bool head)
        {
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

        void OnDestroy()
        {
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
                if (m_Stick && Physics.Raycast(transform.position, step, out var hit, step.magnitude + 0.01f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerNet>() == null)
                {
                    m_Landed = true;
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

    /// <summary>
    /// Sound effects, all synthesised procedurally at startup. Recorded clips dropped in Resources/Sfx ("name_0.ogg",
    /// "name_1.ogg", ... are variations picked at random) would replace them.
    /// </summary>
    public static class Sfx
    {
        public static AudioClip Swing, Flesh, Headshot, Chop, Clink, Thud, Ding, Smash, Twang, Throw, Pop, Eat, Place, Hurt, Kill, Step, Hiss, Boom, Beep, Zap, Saw, Hum,
            Hit, Rocket, Sniper, Portal, Jet, Glass, Door, Click, Crowd, Whiz, Slide, Hoof, UiHover, UiClick, UiSlide,
            Workshop, ArmorClank, StoneGrind, Engine;
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
