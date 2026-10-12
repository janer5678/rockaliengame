using UnityEngine;

namespace RockGame
{
    /// <summary>The wacky airdrop items' effects (Fx.Play hands every FxKind it doesn't know to this) and their sounds.</summary>
    public static class WackyFx
    {
        static Color Rainbow(float t) => Color.HSVToRGB(Mathf.Repeat(t, 1f), 0.75f, 1f);

        /// <summary>Confetti: little bright squares thrown out and fluttering down.</summary>
        public static void Confetti(Vector3 pos, int n, float speed, float size = 0.08f)
        {
            for (int i = 0; i < n; i++)
            {
                var v = Random.insideUnitSphere * speed + Vector3.up * speed * 0.7f;
                FxParticle.Spawn(pos, v, Rainbow(Random.value), size * Random.Range(0.6f, 1.3f), Random.Range(1.2f, 2.4f), 4f, false);
            }
        }

        public static void Play(FxKind kind, Vector3 pos, Vector3 dir)
        {
            switch (kind)
            {
                case FxKind.ShieldPing:
                    Fx.Sparks(pos, Vector3.up, 12);
                    for (int i = 0; i < 6; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.8f, new Color(0.4f, 0.9f, 1f, 0.5f), 0.6f, 0.4f, Vector3.zero);
                    Sfx.Play(Sfx.Clink, pos, 0.8f, 0.2f);
                    break;
                case FxKind.GoldBurst:
                    for (int i = 0; i < 40; i++) FxParticle.Spawn(pos, Random.insideUnitSphere * 5f + Vector3.up * 3f, Color.Lerp(new Color(1f, 0.8f, 0.15f), Color.white, Random.value * 0.4f), Random.Range(0.04f, 0.1f), Random.Range(1f, 2f), 6f, false);
                    Fx.Sparks(pos, Vector3.up, 25);
                    Sfx.Play(Sfx.Unlock, pos, 1f, 0.02f, 80f);
                    break;
                case FxKind.Fuse:
                    FuseBeeper.Start(pos, Cfg.SuicideFuse);
                    break;
                case FxKind.Goo:
                    for (int i = 0; i < 18; i++) FxParticle.Puff(pos + Vector3.Scale(Random.insideUnitSphere, new Vector3(Cfg.SlowRadius, 0.6f, Cfg.SlowRadius)) * 0.7f, new Color(0.55f, 0.2f, 0.85f, 0.7f), Random.Range(0.8f, 1.6f), Random.Range(0.8f, 1.5f), Vector3.up * 0.4f);
                    Fx.Chips(pos, Vector3.up, new Color(0.5f, 0.15f, 0.8f), 20, 5f);
                    Sfx.Play(Sfx.Eat, pos, 1f, 0.1f, 60f);
                    break;
                case FxKind.BearPoof:
                    for (int i = 0; i < 24; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 1.2f, new Color(0.5f, 0.33f, 0.18f, 0.75f), Random.Range(0.9f, 1.6f), Random.Range(0.7f, 1.2f), Random.insideUnitSphere * 2f);
                    Sfx.Play(Sfx.Growl, pos, 1f, 0.05f, 70f);
                    break;
                case FxKind.AntZap:
                    Fx.Sparks(pos, Vector3.up, 20);
                    for (int i = 0; i < 10; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.8f, new Color(0.4f, 1f, 0.3f, 0.6f), 0.5f, 0.6f, Vector3.up * 0.5f);
                    Sfx.Play(Sfx.Zap, pos, 1f, 0.2f);
                    break;
                case FxKind.Boing:
                    Confetti(pos, 20, 4f);
                    Sfx.Play(Sfx.Boing, pos, 1f, 0.1f, 60f);
                    break;
                case FxKind.OmenFlash:
                    for (int i = 0; i < 16; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 1f, new Color(0.65f, 0.35f, 1f, 0.7f), Random.Range(0.6f, 1.2f), 0.8f, Random.insideUnitSphere * 2f);
                    Fx.Sparks(pos, Vector3.up, 20);
                    Sfx.Play(Sfx.Portal, pos, 1f, 0.05f);
                    break;
                case FxKind.ShellBoom:
                    Fx.Explosion(pos);
                    for (int i = 0; i < 20; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 1.5f, new Color(0.3f, 0.5f, 1f, 0.7f), Random.Range(1f, 2f), 1f, Random.insideUnitSphere * 3f);
                    break;
                case FxKind.Tractor:
                    for (int i = 0; i < 14; i++) FxParticle.Puff(pos + dir * (i * 1.2f), new Color(0.4f, 1f, 0.9f, 0.5f), 0.4f, 0.5f, -dir * 2f);
                    Sfx.Play(Sfx.Hum, pos, 0.8f, 0f);
                    break;
                case FxKind.Clack:
                    Sfx.Play(Sfx.Click, pos, 0.8f, 0.1f, 30f);
                    break;
                case FxKind.Swap:
                    for (int i = 0; i < 12; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.9f, Rainbow(Random.value) * new Color(1, 1, 1, 0.6f), 0.6f, 0.7f, Random.insideUnitSphere);
                    Sfx.Play(Sfx.Portal, pos, 0.9f, 0.1f);
                    break;
                case FxKind.ChaosFlash:
                    Confetti(pos, 60, 9f, 0.14f);
                    for (int i = 0; i < 30; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 3f, Rainbow(Random.value) * new Color(1, 1, 1, 0.6f), Random.Range(1f, 2.5f), 1.4f, Random.insideUnitSphere * 5f);
                    Sfx.PlayFar(Sfx.BigBoom, pos, 0.8f, 0f, 3000f);
                    Fx.Shake(0.4f);
                    break;
                case FxKind.TruthBlast:
                    Fx.Explosion(pos);
                    Fx.Chips(pos, Vector3.up, new Color(0.45f, 0.38f, 0.28f), 50, 9f);
                    Fx.Chips(pos, Vector3.up, new Color(0.3f, 0.55f, 0.25f), 30, 7f);
                    Sfx.Play(Sfx.StoneGrind, pos, 1f, 0.05f, 120f);
                    break;
                case FxKind.DanceBlast:
                    Confetti(pos, 50, 7f);
                    for (int i = 0; i < 16; i++) FxParticle.Puff(pos + Random.insideUnitSphere * Cfg.DanceRadius * 0.6f, Rainbow(Random.value) * new Color(1, 1, 1, 0.5f), 1.2f, 1.2f, Vector3.up);
                    Sfx.Play(Sfx.Disco, pos, 1f, 0f, 90f);
                    break;
                case FxKind.ClutterPop:
                    Confetti(pos, 40, 6f);
                    Sfx.Play(Sfx.Pop, pos, 1f, 0.2f);
                    break;
                case FxKind.BigPoof:
                    for (int i = 0; i < 50; i++)
                    {
                        var off = Vector3.Scale(Random.insideUnitSphere, new Vector3(4f, 2f, 4f));
                        FxParticle.Puff(pos + off, new Color(0.93f, 0.92f, 0.9f, 0.8f), Random.Range(1.4f, 2.6f), Random.Range(1.2f, 2f), new Vector3(off.x, 0.5f, off.z).normalized * 2f);
                    }
                    Sfx.Play(Sfx.Pop, pos, 1f, 0.15f);
                    Sfx.Play(Sfx.Laugh, pos, 1f, 0.05f, 80f);
                    break;
                case FxKind.Deflate:
                    for (int i = 0; i < 16; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 2.5f, new Color(0.95f, 0.6f, 0.7f, 0.5f), 1f, 1f, Vector3.up);
                    Sfx.Play(Sfx.Hiss, pos, 1f, 0.05f);
                    break;
                case FxKind.Scribble:
                    Fx.Sparks(pos, Vector3.up, 16);
                    for (int i = 0; i < 10; i++) FxParticle.Puff(pos + Random.insideUnitSphere * 0.8f, new Color(0.3f, 0.35f, 0.9f, 0.6f), 0.5f, 0.6f, Vector3.up);
                    Sfx.Play(Sfx.Scratch, pos, 0.9f, 0.1f);
                    break;
                case FxKind.DecoyPoof:
                    for (int i = 0; i < 24; i++)
                    {
                        var off = Vector3.Scale(Random.insideUnitSphere, new Vector3(0.8f, 1f, 0.8f));
                        FxParticle.Puff(pos + off, new Color(0.9f, 0.9f, 0.9f, 0.8f), Random.Range(0.9f, 1.5f), Random.Range(0.8f, 1.3f), off.normalized * 1.5f);
                    }
                    Sfx.Play(Sfx.Laugh, pos, 1f, 0.05f, 70f);
                    break;
                case FxKind.Bonk:
                    Sfx.Play(Sfx.Boing, pos, 1f, 0.1f, 50f);
                    Sfx.Play(Sfx.Thud, pos, 1f, 0.1f);
                    for (int i = 0; i < 6; i++) FxParticle.Spawn(pos + Vector3.up * 0.5f, Random.insideUnitSphere * 2f + Vector3.up * 2f, new Color(1f, 0.9f, 0.3f), 0.08f, 0.8f, 3f, false);
                    break;
                case FxKind.WackyBeam:
                {
                    var end = pos + dir;
                    Fx.Tracer(pos, end, Fx.Gun.Pistol, false, false);
                    int n = Mathf.Clamp(Mathf.RoundToInt(dir.magnitude / 1.5f), 2, 30);
                    for (int i = 0; i < n; i++) FxParticle.Puff(Vector3.Lerp(pos, end, i / (float)n), new Color(0.75f, 0.45f, 1f, 0.45f), 0.3f, 0.35f, Vector3.zero);
                    Sfx.Play(Sfx.Zap, pos, 0.7f, 0.15f, 50f);
                    break;
                }
            }
        }
    }

    /// <summary>The suicide bomb's fuse: faster and faster beeps where it is, then it goes.</summary>
    public class FuseBeeper : MonoBehaviour
    {
        float m_End, m_Next;

        public static void Start(Vector3 pos, float seconds)
        {
            var go = new GameObject("fuse");
            go.transform.position = pos;
            var f = go.AddComponent<FuseBeeper>();
            f.m_End = Time.time + seconds;
            Destroy(go, seconds + 0.5f);
        }

        void Update()
        {
            if (Time.time < m_Next || Time.time > m_End) return;
            float left = m_End - Time.time;
            m_Next = Time.time + Mathf.Lerp(0.07f, 0.25f, left / Mathf.Max(0.1f, Cfg.SuicideFuse));
            Sfx.Play(Sfx.Beep, transform.position, 1f, 0f, 60f);
            FxParticle.Puff(transform.position, new Color(1f, 0.2f, 0.15f, 0.6f), 0.4f);
        }
    }

    public static partial class Sfx
    {
        static AudioClip s_Laugh, s_Boing, s_Disco, s_Growl, s_Scratch;

        /// <summary>A cackle: "ha ha ha ha ha", falling.</summary>
        public static AudioClip Laugh => s_Laugh != null ? s_Laugh : (s_Laugh = MakeWacky("laugh", 1.0f, (t, d, n) =>
        {
            const float per = 0.17f;
            int i = (int)(t / per);
            float u = t - i * per;
            if (i >= 5 || u > 0.12f) return 0f;
            float f0 = 320f - i * 24f + Mathf.Sin(u * 40f) * 12f;
            float env = Mathf.Sin(u / 0.12f * Mathf.PI);
            float v = 0f;
            for (int h = 1; h <= 7; h++)
            {
                float fh = f0 * h;
                float w = Mathf.Exp(-Mathf.Pow((fh - 750f) / 380f, 2f)) + Mathf.Exp(-Mathf.Pow((fh - 1250f) / 300f, 2f)) * 0.6f + 0.15f / h;
                v += Mathf.Sin(2f * Mathf.PI * fh * t) * w;
            }
            return (v * 0.22f + n * 0.05f) * env;
        }, 0.6f));

        /// <summary>A springy boing.</summary>
        public static AudioClip Boing => s_Boing != null ? s_Boing : (s_Boing = MakeWacky("boing", 0.6f, (t, d, n) =>
        {
            float f = 160f + 140f * Mathf.Exp(-t * 6f) + Mathf.Sin(t * 2f * Mathf.PI * 14f) * 40f * Mathf.Exp(-t * 4f);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 5f) * 0.7f;
        }));

        /// <summary>A short disco hit: a kick, a chord and a hi-hat.</summary>
        public static AudioClip Disco => s_Disco != null ? s_Disco : (s_Disco = MakeWacky("disco", 1.6f, (t, d, n) =>
        {
            float beat = Mathf.Repeat(t, 0.4f);
            float kick = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(120f, 45f, beat / 0.15f) * beat) * Mathf.Exp(-beat * 18f);
            float off = Mathf.Repeat(t + 0.2f, 0.4f);
            float hat = n * Mathf.Exp(-off * 60f) * 0.4f;
            float chord = (Mathf.Sin(2f * Mathf.PI * 261.6f * t) + Mathf.Sin(2f * Mathf.PI * 329.6f * t) + Mathf.Sin(2f * Mathf.PI * 392f * t)) * 0.12f * (Mathf.Repeat(t, 0.8f) < 0.3f ? 1f : 0.3f);
            return (kick * 0.8f + hat + chord) * Mathf.Min(1f, (d - t) * 3f);
        }));

        /// <summary>A bear's growl.</summary>
        public static AudioClip Growl => s_Growl != null ? s_Growl : (s_Growl = MakeWacky("growl", 1.1f, (t, d, n) =>
        {
            float f = 70f + Mathf.Sin(t * 9f) * 12f;
            float body = Mathf.Sin(2f * Mathf.PI * f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * f * 2.02f * t) * 0.3f;
            float rasp = n * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 32f * t));
            return (body + rasp * 0.5f) * Mathf.Sin(t / d * Mathf.PI) * 0.8f;
        }, 0.25f));

        /// <summary>A pencil scribbling.</summary>
        public static AudioClip Scratch => s_Scratch != null ? s_Scratch : (s_Scratch = MakeWacky("scratch", 0.5f, (t, d, n) =>
            n * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(t * 2f * Mathf.PI * 9f))) * Mathf.Sin(t / d * Mathf.PI) * 0.5f, 0.5f));

        static AudioClip MakeWacky(string name, float dur, System.Func<float, float, float, float> f, float lowpass = 1f)
        {
            var rng = new System.Random(name.GetHashCode());
            return Make(name, dur, (t, d) => f(t, d, (float)rng.NextDouble() * 2f - 1f), lowpass);
        }
    }
}
