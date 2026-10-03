using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>Per-PC settings (sound, mouse, display, mic), saved in PlayerPrefs.</summary>
    public static partial class GameSettings
    {
        public const int VoiceOff = 0, VoiceOpen = 1, VoicePushToTalk = 2;
        public static float MasterVolume = 0.8f, SfxVolume = 1f, VoiceVolume = 1f, MicGain = 1.5f, MicThreshold = 0.02f;
        public static float MouseSensitivity = 2f;
        /// <summary>Graphics: false = normal (our own look), true = PSX (swapped-in PSX models; only the looks change).</summary>
        public static bool PsxGraphics;
        /// <summary>Fired when the graphics mode changes (trees rebuild their looks).</summary>
        public static event System.Action GraphicsChanged;

        public static void SetPsx(bool on, bool save = true)
        {
            if (PsxGraphics == on) return;
            PsxGraphics = on;
            if (save) { PlayerPrefs.SetInt("RockGame.PsxGraphics", on ? 1 : 0); PlayerPrefs.Save(); }
            GraphicsChanged?.Invoke();
        }
        /// <summary>Graphics: the AI PSX TEST mode (its own look, separate from Normal and PSX - see AiPsxArt).</summary>
        public static bool AiPsx;

        public static void SetAiPsx(bool on, bool save = true)
        {
            if (AiPsx == on) return;
            AiPsx = on;
            if (save) { PlayerPrefs.SetInt("RockGame.AiPsx", on ? 1 : 0); PlayerPrefs.Save(); }
            GraphicsChanged?.Invoke();
        }

        /// <summary>Pick one of the three graphics modes: 0 Normal, 1 PSX, 2 AI PSX TEST.</summary>
        public static void SetGraphics(int mode, bool save = true)
        {
            if (mode != 2) SetAiPsx(false, save);
            if (mode != 1) SetPsx(false, save);
            if (mode == 1) SetPsx(true, save);
            if (mode == 2) SetAiPsx(true, save);
        }

        public static int GraphicsMode => AiPsx ? 2 : PsxGraphics ? 1 : 0;

        /// <summary>In a match the host picks the graphics for everyone (not saved: our own choice comes back after the match).</summary>
        public static void ApplyHostGraphics(int mode) => SetGraphics(Mathf.Clamp(mode, 0, 2), false);

        /// <summary>Back to this PC's own graphics choice.</summary>
        public static void RestoreGraphics()
        {
            bool psx = ShowGraphicsPicker && PlayerPrefs.GetInt("RockGame.PsxGraphics", 0) == 1;
            SetGraphics(psx ? 1 : ShowGraphicsPicker && PlayerPrefs.GetInt("RockGame.AiPsx", 0) == 1 ? 2 : 0, false);
        }
        public static int VoiceMode = VoicePushToTalk;
        public static string MicDevice = "";
        static bool s_Loaded;

        public static void Load()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            MasterVolume = PlayerPrefs.GetFloat("RockGame.Volume", 0.8f);
            SfxVolume = PlayerPrefs.GetFloat("RockGame.SfxVolume", 1f);
            VoiceVolume = PlayerPrefs.GetFloat("RockGame.VoiceVolume", 1f);
            MouseSensitivity = PlayerPrefs.GetFloat("RockGame.MouseSensitivity", 2f);
            // (the PSX test graphics are hidden for now: a PSX picked before is ignored - see ShowGraphicsPicker)
            PsxGraphics = ShowGraphicsPicker && PlayerPrefs.GetInt("RockGame.PsxGraphics", 0) == 1;
            AiPsx = ShowGraphicsPicker && !PsxGraphics && PlayerPrefs.GetInt("RockGame.AiPsx", 0) == 1;
            MicGain = PlayerPrefs.GetFloat("RockGame.MicGain", 1.5f);
            MicThreshold = PlayerPrefs.GetFloat("RockGame.MicThreshold", 0.02f);
            VoiceMode = PlayerPrefs.GetInt("RockGame.VoiceMode", VoicePushToTalk);
            MicDevice = PlayerPrefs.GetString("RockGame.MicDevice", "");
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("RockGame.Volume", MasterVolume);
            PlayerPrefs.SetFloat("RockGame.SfxVolume", SfxVolume);
            PlayerPrefs.SetFloat("RockGame.VoiceVolume", VoiceVolume);
            PlayerPrefs.SetFloat("RockGame.MouseSensitivity", MouseSensitivity);
            PlayerPrefs.SetFloat("RockGame.MicGain", MicGain);
            PlayerPrefs.SetFloat("RockGame.MicThreshold", MicThreshold);
            PlayerPrefs.SetInt("RockGame.VoiceMode", VoiceMode);
            PlayerPrefs.SetString("RockGame.MicDevice", MicDevice);
            PlayerPrefs.Save();
            Apply();
        }

        /// <summary>Automated test runs (-autotest) and -mute are silent.</summary>
        public static bool Muted;
        public static void Apply() => AudioListener.volume = Muted ? 0f : Mathf.Clamp01(MasterVolume);

        // ------------------------------------------------------------------ display

        public enum WindowMode { Fullscreen, Borderless, Windowed }

        /// <summary>Screen sizes this monitor supports (largest first), one entry per size.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var l = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!l.Contains(v)) l.Add(v);
            }
            var cur = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            if (!l.Contains(cur)) l.Add(cur);
            var now = CurrentSize;
            if (!l.Contains(now)) l.Add(now);
            l.Sort((a, b) => b.x * b.y != a.x * a.y ? (b.x * b.y).CompareTo(a.x * a.y) : b.x.CompareTo(a.x));
            return l;
        }

        /// <summary>Refresh rates (highest first) the monitor has at a screen size.</summary>
        public static List<RefreshRate> RefreshRates(Vector2Int size)
        {
            var l = new List<RefreshRate>();
            foreach (var r in Screen.resolutions)
                if (r.width == size.x && r.height == size.y && !l.Exists(x => System.Math.Abs(x.value - r.refreshRateRatio.value) < 0.5)) l.Add(r.refreshRateRatio);
            if (l.Count == 0)
                foreach (var r in Screen.resolutions)
                    if (!l.Exists(x => System.Math.Abs(x.value - r.refreshRateRatio.value) < 0.5)) l.Add(r.refreshRateRatio);
            if (l.Count == 0) l.Add(Screen.currentResolution.refreshRateRatio);
            l.Sort((a, b) => b.value.CompareTo(a.value));
            return l;
        }

        public static WindowMode CurrentMode => Screen.fullScreenMode == FullScreenMode.Windowed || Screen.fullScreenMode == FullScreenMode.MaximizedWindow ? WindowMode.Windowed
            : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? WindowMode.Fullscreen : WindowMode.Borderless;

        public static Vector2Int CurrentSize => new Vector2Int(Screen.width, Screen.height);

        /// <summary>The refresh rate we run at (the highest the screen can do unless another one was picked).</summary>
        public static RefreshRate ChosenRate;

        public static void SetDisplay(Vector2Int size, WindowMode mode, RefreshRate rate)
        {
            var fs = mode == WindowMode.Fullscreen ? FullScreenMode.ExclusiveFullScreen : mode == WindowMode.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            ChosenRate = rate;
            Screen.SetResolution(size.x, size.y, fs, rate);
            Application.targetFrameRate = Mathf.Max(60, Mathf.RoundToInt((float)rate.value));
            PlayerPrefs.SetInt("RockGame.ScreenW", size.x);
            PlayerPrefs.SetInt("RockGame.ScreenH", size.y);
            PlayerPrefs.SetInt("RockGame.ScreenMode", (int)mode);
            PlayerPrefs.SetInt("RockGame.RefreshNum", (int)rate.numerator);
            PlayerPrefs.SetInt("RockGame.RefreshDen", (int)rate.denominator);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// At startup: the display settings picked last time; otherwise keep the window as it is and run at the highest
        /// refresh rate the monitor has. keepWindow: test runs and the editor never touch the window.
        /// </summary>
        public static void ApplyDisplayAtStartup(bool keepWindow)
        {
            var rates = RefreshRates(new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height));
            ChosenRate = rates[0];
            Application.targetFrameRate = Mathf.Max(60, Mathf.RoundToInt((float)ChosenRate.value));
            if (keepWindow || Application.isEditor) return;
            if (PlayerPrefs.HasKey("RockGame.ScreenW"))
            {
                var size = new Vector2Int(PlayerPrefs.GetInt("RockGame.ScreenW"), PlayerPrefs.GetInt("RockGame.ScreenH"));
                var mode = (WindowMode)PlayerPrefs.GetInt("RockGame.ScreenMode", (int)CurrentMode);
                var want = new RefreshRate { numerator = (uint)PlayerPrefs.GetInt("RockGame.RefreshNum", (int)ChosenRate.numerator), denominator = (uint)Mathf.Max(1, PlayerPrefs.GetInt("RockGame.RefreshDen", (int)ChosenRate.denominator)) };
                SetDisplay(size, mode, want);
                return;
            }
            if (Screen.fullScreenMode != FullScreenMode.Windowed) Screen.SetResolution(Screen.width, Screen.height, Screen.fullScreenMode, ChosenRate);
        }
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
            if (GameSettings.VoiceMode == GameSettings.VoicePushToTalk) talk = Binds.Held(Bind.PushToTalk);
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
