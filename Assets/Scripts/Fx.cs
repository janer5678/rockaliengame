using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    public enum FxKind : byte { Blood, BloodHead, WoodChips, StoneChips, WeakSpot, Break, Smash, StructureHit, Spawn, C4Placed, Explosion, WandBeam, HelmetBreak, Craft, Drink }

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
                case FxKind.WoodChips: Chips(pos, dir, Art.Wood, 8); Sfx.Play(Sfx.Chop, pos); break;
                case FxKind.StoneChips: Chips(pos, dir, Art.Stone, 8); Sparks(pos, dir, 5); Sfx.Play(Sfx.Clink, pos); break;
                case FxKind.WeakSpot: Sparks(pos, dir, 16); Sfx.Play(Sfx.Ding, pos, 0.8f); break;
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
                case FxKind.Drink:
                    for (int i = 0; i < 8; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.6f + Vector3.up, new Color(0.7f, 0.4f, 1f, 0.5f), Random.Range(0.4f, 0.8f));
                    Sfx.Play(Sfx.Zap, pos, 0.5f);
                    break;
            }
        }

        public static void Explosion(Vector3 pos)
        {
            Sfx.Play(Sfx.Boom, pos, 1f, 0.05f);
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

    /// <summary>Procedurally synthesised sound effects (the project has no audio assets).</summary>
    public static class Sfx
    {
        public static AudioClip Swing, Flesh, Headshot, Chop, Clink, Thud, Ding, Smash, Twang, Throw, Pop, Eat, Place, Hurt, Kill, Step, Hiss, Boom, Beep, Zap, Saw, Hum;
        const int Rate = 44100;

        // runs on first access to any clip, so Sfx.Play(Sfx.Chop, ...) always gets a built clip
        static Sfx() => Build();

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
            Step = Make("step", 0.08f, (t, d) => N() * Env(t, 0.03f) * 0.25f, lowpass: 0.15f);
        }

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

        /// <summary>3D sound at a world position with a little pitch variation.</summary>
        public static void Play(AudioClip clip, Vector3 pos, float volume = 0.7f, float pitchVar = 0.08f)
        {
            if (clip == null) return;
            var go = new GameObject("sfx");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            src.spatialBlend = 1f;
            src.minDistance = 3f;
            src.maxDistance = 60f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.5f, src.pitch) + 0.1f);
        }

        /// <summary>Non-positional sound for the local player (UI, own swings).</summary>
        public static void Play2D(AudioClip clip, float volume = 0.6f, float pitchVar = 0.06f)
        {
            if (clip == null) return;
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
