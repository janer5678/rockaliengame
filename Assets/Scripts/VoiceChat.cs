using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Per-PC sound and mic settings (pause menu), saved in PlayerPrefs.</summary>
    public static class GameSettings
    {
        public const int VoiceOff = 0, VoiceOpen = 1, VoicePushToTalk = 2;
        public static float MasterVolume = 0.8f, VoiceVolume = 1f, MicGain = 1.5f, MicThreshold = 0.02f, HitVolume = 1f;
        public static int VoiceMode = VoicePushToTalk;
        public static string MicDevice = "";
        public static KeyCode PushToTalkKey = KeyCode.V;
        static bool s_Loaded;

        public static void Load()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            MasterVolume = PlayerPrefs.GetFloat("RockGame.Volume", 0.8f);
            VoiceVolume = PlayerPrefs.GetFloat("RockGame.VoiceVolume", 1f);
            HitVolume = PlayerPrefs.GetFloat("RockGame.HitVolume", 1f);
            MicGain = PlayerPrefs.GetFloat("RockGame.MicGain", 1.5f);
            MicThreshold = PlayerPrefs.GetFloat("RockGame.MicThreshold", 0.02f);
            VoiceMode = PlayerPrefs.GetInt("RockGame.VoiceMode", VoicePushToTalk);
            MicDevice = PlayerPrefs.GetString("RockGame.MicDevice", "");
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("RockGame.Volume", MasterVolume);
            PlayerPrefs.SetFloat("RockGame.VoiceVolume", VoiceVolume);
            PlayerPrefs.SetFloat("RockGame.HitVolume", HitVolume);
            PlayerPrefs.SetFloat("RockGame.MicGain", MicGain);
            PlayerPrefs.SetFloat("RockGame.MicThreshold", MicThreshold);
            PlayerPrefs.SetInt("RockGame.VoiceMode", VoiceMode);
            PlayerPrefs.SetString("RockGame.MicDevice", MicDevice);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply() => AudioListener.volume = Mathf.Clamp01(MasterVolume);
    }

    /// <summary>
    /// Proximity voice chat: the mic is captured, cut to 8 kHz and mu-law compressed into 100 ms chunks that go through the
    /// server to everyone else; each player's voice plays from their head in 3D, so you only hear people near you.
    /// </summary>
    public class VoiceChat : MonoBehaviour
    {
        const int Rate = 8000, ChunkSamples = 800;
        static readonly Dictionary<PlayerNet, Speaker> s_Speakers = new Dictionary<PlayerNet, Speaker>();

        public static float Level;          // mic loudness 0..1 (for the level meter)
        public static bool Transmitting;
        public static string ActiveDevice = "";

        AudioClip m_Mic;
        string m_Device;
        int m_MicRate, m_LastPos;
        float[] m_Read = new float[4096];
        readonly List<float> m_Pending = new List<float>();
        float m_Acc;
        int m_AccN;
        float m_Hang;

        void Start() => GameSettings.Load();

        void OnDisable() => StopMic();

        void Update()
        {
            var me = PlayerNet.Local;
            bool want = GameSettings.VoiceMode != GameSettings.VoiceOff && me != null && me.IsSpawned && Microphone.devices.Length > 0;
            string dev = PickDevice();
            if (!want || dev != m_Device) StopMic();
            if (want && m_Mic == null) StartMic(dev);
            Transmitting = false;
            if (m_Mic == null) { Level = Mathf.MoveTowards(Level, 0, Time.deltaTime); return; }
            Capture(me);
        }

        static string PickDevice()
        {
            var devs = Microphone.devices;
            if (devs.Length == 0) return null;
            foreach (var d in devs) if (d == GameSettings.MicDevice) return d;
            return devs[0];
        }

        void StartMic(string dev)
        {
            if (dev == null) return;
            Microphone.GetDeviceCaps(dev, out int min, out int max);
            m_MicRate = max == 0 ? 16000 : Mathf.Clamp(16000, min, max);
            m_Mic = Microphone.Start(dev, true, 1, m_MicRate);
            m_Device = dev;
            ActiveDevice = dev;
            m_LastPos = 0;
            m_Pending.Clear();
        }

        void StopMic()
        {
            if (m_Mic == null) return;
            Microphone.End(m_Device);
            Destroy(m_Mic);
            m_Mic = null;
            m_Device = null;
        }

        void Capture(PlayerNet me)
        {
            int pos = Microphone.GetPosition(m_Device);
            int n = pos - m_LastPos;
            if (n < 0) n += m_Mic.samples;
            if (n <= 0) return;
            if (n > m_Read.Length) m_Read = new float[n];
            m_Mic.GetData(m_Read, m_LastPos); // wraps around the looping clip
            m_LastPos = pos;

            // gain + downsample to 8 kHz (box filter), loudness for the meter / voice activation
            float sum = 0;
            float step = m_MicRate / (float)Rate;
            var fresh = new List<float>(n / Mathf.Max(1, (int)step) + 2);
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Clamp(m_Read[i] * GameSettings.MicGain, -1f, 1f);
                sum += x * x;
                m_Acc += x;
                m_AccN++;
                if (m_AccN >= step) { fresh.Add(m_Acc / m_AccN); m_Acc = 0; m_AccN = 0; }
            }
            float rms = Mathf.Sqrt(sum / n);
            Level = Mathf.Max(rms * 3f, Level - Time.deltaTime);

            bool talk;
            if (GameSettings.VoiceMode == GameSettings.VoicePushToTalk) talk = Input.GetKey(GameSettings.PushToTalkKey);
            else
            {
                if (rms > GameSettings.MicThreshold) m_Hang = 0.4f;
                m_Hang -= Time.deltaTime;
                talk = m_Hang > 0;
            }
            if (!talk) { m_Pending.Clear(); return; }
            Transmitting = true;
            m_Pending.AddRange(fresh);
            while (m_Pending.Count >= ChunkSamples)
            {
                var bytes = new byte[ChunkSamples];
                for (int i = 0; i < ChunkSamples; i++) bytes[i] = MuLawEncode(m_Pending[i]);
                m_Pending.RemoveRange(0, ChunkSamples);
                me.VoiceRpc(bytes);
            }
        }

        /// <summary>A chunk of someone's voice arrived: queue it on their speaker.</summary>
        public static void Receive(PlayerNet from, byte[] data)
        {
            if (from == null || data == null) return;
            if (!s_Speakers.TryGetValue(from, out var sp) || sp.Source == null)
            {
                sp = new Speaker(from);
                s_Speakers[from] = sp;
            }
            sp.Push(data);
        }

        /// <summary>Who is talking right now (for the HUD).</summary>
        public static bool IsTalking(PlayerNet p) => s_Speakers.TryGetValue(p, out var sp) && Time.time - sp.LastPacket < 0.3f;

        // ---------------- mu-law ----------------

        static byte MuLawEncode(float f)
        {
            int s = Mathf.Clamp((int)(f * 32767f), -32767, 32767);
            int sign = (s >> 8) & 0x80;
            if (sign != 0) s = -s;
            s = Mathf.Min(s + 132, 32635);
            int exp = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exp > 0; mask >>= 1) exp--;
            int mant = (s >> (exp + 3)) & 0x0F;
            return (byte)~(sign | (exp << 4) | mant);
        }

        static float MuLawDecode(byte b)
        {
            int u = ~b & 0xFF;
            int sign = u & 0x80, exp = (u >> 4) & 7, mant = u & 0x0F;
            int s = ((mant << 3) + 132) << exp;
            s -= 132;
            return (sign != 0 ? -s : s) / 32767f;
        }

        /// <summary>A 3D audio source on a player's head fed from a small jitter buffer.</summary>
        class Speaker
        {
            public AudioSource Source;
            public float LastPacket;
            readonly float[] m_Ring = new float[Rate * 2];
            int m_Write, m_Read, m_Count;
            bool m_Playing;
            readonly object m_Lock = new object();

            public Speaker(PlayerNet p)
            {
                var go = new GameObject("voice");
                go.transform.SetParent(p.transform, false);
                go.transform.localPosition = Vector3.up * 1.6f;
                Source = go.AddComponent<AudioSource>();
                Source.spatialBlend = 1f;
                Source.rolloffMode = AudioRolloffMode.Linear;
                Source.minDistance = 3f;
                Source.maxDistance = 45f;
                Source.dopplerLevel = 0f;
                Source.loop = true;
                Source.clip = AudioClip.Create("voice", Rate, 1, Rate, true, OnRead);
                Source.Play();
            }

            public void Push(byte[] data)
            {
                LastPacket = Time.time;
                float vol = GameSettings.VoiceVolume;
                lock (m_Lock)
                {
                    foreach (var b in data)
                    {
                        if (m_Count >= m_Ring.Length) { m_Read = (m_Read + 1) % m_Ring.Length; m_Count--; }
                        m_Ring[m_Write] = MuLawDecode(b) * vol;
                        m_Write = (m_Write + 1) % m_Ring.Length;
                        m_Count++;
                    }
                }
            }

            void OnRead(float[] data)
            {
                lock (m_Lock)
                {
                    // wait until ~200 ms are buffered before playing, so small network hiccups don't crackle
                    if (!m_Playing && m_Count >= Rate / 5) m_Playing = true;
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (m_Playing && m_Count > 0)
                        {
                            data[i] = m_Ring[m_Read];
                            m_Read = (m_Read + 1) % m_Ring.Length;
                            m_Count--;
                        }
                        else { data[i] = 0f; m_Playing = false; }
                    }
                }
            }
        }
    }
}
