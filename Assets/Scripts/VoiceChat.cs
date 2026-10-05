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
        /// <summary>Settings > Voice chat > ALIEN VOICE: your mic goes through the alien filter before it's sent (everyone hears it).</summary>
        public static bool AlienVoice;
        /// <summary>Settings > Voice chat > HEAR MYSELF: plays your own voice back (after the codec), not saved.</summary>
        public static bool HearMyself;
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
            AlienVoice = PlayerPrefs.GetInt("RockGame.AlienVoice", 0) == 1;
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
            PlayerPrefs.SetInt("RockGame.AlienVoice", AlienVoice ? 1 : 0);
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
            ApplyFrameCap(); // (the refresh rate, unless Uncapped framerate is on: Hud.Fps.cs)
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
            ApplyFrameCap(); // (the refresh rate, unless Uncapped framerate is on: Hud.Fps.cs)
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
    /// Proximity voice chat. The mic is captured (48 kHz when it can), resampled to 16 kHz, cleaned up (rumble filter, noise
    /// gate, automatic gain, soft limiter, optionally the alien voice) and sent in 20 ms IMA ADPCM packets (66 kbit/s while
    /// talking) by an unreliable RPC; the server passes each packet on only to players within RelayRange. Each player's voice
    /// plays from their head in 3D through a small adaptive jitter buffer (VoiceJitterBuffer, usually 50-70 ms).
    /// The signal path itself is in VoiceDsp.cs.
    /// </summary>
    public class VoiceChat : MonoBehaviour
    {
        const int Rate = VoiceCodec.Rate, Frame = VoiceCodec.FrameSamples;
        /// <summary>The server only relays a voice packet to players this close to the talker (you hear voices out to 45 m).</summary>
        public const float RelayRange = 60f;
        static readonly Dictionary<PlayerNet, Speaker> s_Speakers = new Dictionary<PlayerNet, Speaker>();
        static Speaker s_Self;

        public static float Level;          // mic loudness 0..1 (for the level meter)
        public static bool Transmitting;
        public static string ActiveDevice = "";
        /// <summary>Packets and bytes sent (for the tests).</summary>
        public static int SentPackets, SentBytes;

        AudioClip m_Mic;
        string m_Device;
        int m_MicRate, m_LastPos;
        float[] m_Read = new float[4096];
        VoiceResampler m_Rs;
        readonly VoiceTx m_Tx = new VoiceTx();
        readonly List<float> m_Pcm = new List<float>();
        readonly Queue<float[]> m_PreRoll = new Queue<float[]>();
        VoiceCodec.State m_Enc;
        ushort m_Seq;
        bool m_WasTalking;
        float m_Hang;

        void Start() => GameSettings.Load();

        void OnDisable() => StopMic();

        void Update()
        {
            var me = PlayerNet.Local;
            bool spawned = me != null && me.IsSpawned;
            bool want = GameSettings.VoiceMode != GameSettings.VoiceOff && (spawned || GameSettings.HearMyself) && Microphone.devices.Length > 0;
            string dev = PickDevice();
            if (!want || dev != m_Device) StopMic();
            if (want && m_Mic == null) StartMic(dev);
            Transmitting = false;
            if (!GameSettings.HearMyself && s_Self != null) { s_Self.Destroy(); s_Self = null; }
            if (Time.frameCount % 300 == 0) Prune();
            if (m_Mic == null)
            {
                Level = Mathf.MoveTowards(Level, 0, Time.deltaTime);
                if (m_WasTalking) EndTalk(spawned ? me : null);
                return;
            }
            Capture(spawned ? me : null);
        }

        static void Prune()
        {
            List<PlayerNet> dead = null;
            foreach (var kv in s_Speakers) if (kv.Key == null || kv.Value.Source == null) (dead ??= new List<PlayerNet>()).Add(kv.Key);
            if (dead != null) foreach (var p in dead) s_Speakers.Remove(p);
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
            m_MicRate = max == 0 ? 48000 : Mathf.Clamp(48000, min, max);
            m_Mic = Microphone.Start(dev, true, 1, m_MicRate);
            if (m_Mic == null) return;
            m_MicRate = m_Mic.frequency;
            m_Device = dev;
            ActiveDevice = dev;
            m_LastPos = 0;
            m_Rs = new VoiceResampler(m_MicRate, Rate);
            m_Pcm.Clear();
            m_PreRoll.Clear();
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
            if (n <= 0) { Transmitting = m_WasTalking; return; }
            if (n > m_Read.Length) m_Read = new float[n];
            m_Mic.GetData(m_Read, m_LastPos); // wraps around the looping clip
            m_LastPos = pos;

            float sum = 0;
            for (int i = 0; i < n; i++) { float x = m_Read[i] * GameSettings.MicGain; sum += x * x; }
            float rms = Mathf.Sqrt(sum / n);
            Level = Mathf.Max(Mathf.Min(1f, rms * 3f), Level - Time.deltaTime);
            m_Rs.Process(m_Read, n, m_Pcm);

            bool ptt = GameSettings.VoiceMode == GameSettings.VoicePushToTalk;
            float gate = ptt ? 0.006f : Mathf.Max(0.003f, GameSettings.MicThreshold * 0.5f);
            while (m_Pcm.Count >= Frame)
            {
                var f = new float[Frame];
                m_Pcm.CopyTo(0, f, 0, Frame);
                m_Pcm.RemoveRange(0, Frame);
                m_Tx.Process(f, GameSettings.MicGain, gate, GameSettings.AlienVoice);
                bool talk;
                if (ptt) talk = Binds.Held(Bind.PushToTalk);
                else
                {
                    if (m_Tx.LastRms > GameSettings.MicThreshold) m_Hang = 0.4f;
                    m_Hang -= VoiceCodec.FrameSec;
                    talk = m_Hang > 0;
                }
                if (!talk)
                {
                    if (m_WasTalking) EndTalk(me);
                    // keep 40 ms so open mic doesn't cut off the start of the first word
                    m_PreRoll.Enqueue(f);
                    while (m_PreRoll.Count > (ptt ? 0 : 2)) m_PreRoll.Dequeue();
                    continue;
                }
                while (m_PreRoll.Count > 0) Send(me, m_PreRoll.Dequeue());
                Send(me, f);
                m_WasTalking = true;
            }
            Transmitting = m_WasTalking;
        }

        void Send(PlayerNet me, float[] f)
        {
            var pkt = VoiceCodec.Encode(f, m_Seq++, ref m_Enc);
            if (me != null) { me.VoiceRpc(pkt); SentPackets++; SentBytes += pkt.Length; }
            if (GameSettings.HearMyself) Self().Push(pkt);
        }

        void EndTalk(PlayerNet me)
        {
            m_WasTalking = false;
            var end = VoiceCodec.EndPacket((ushort)(m_Seq - 1));
            if (me != null) me.VoiceRpc(end);
            if (GameSettings.HearMyself && s_Self != null) s_Self.Push(end);
        }

        Speaker Self()
        {
            if (s_Self == null || s_Self.Source == null) s_Self = new Speaker(transform, false);
            return s_Self;
        }

        /// <summary>A packet of someone's voice arrived: give it to their speaker.</summary>
        public static void Receive(PlayerNet from, byte[] data)
        {
            if (from == null || data == null) return;
            if (!s_Speakers.TryGetValue(from, out var sp) || sp.Source == null)
            {
                sp = new Speaker(from.transform, true);
                s_Speakers[from] = sp;
            }
            sp.Push(data);
        }

        /// <summary>Who is talking right now (for the HUD).</summary>
        public static bool IsTalking(PlayerNet p) => s_Speakers.TryGetValue(p, out var sp) && Time.time - sp.LastPacket < 0.3f;

        /// <summary>The playback buffer of a player's voice (null when they haven't spoken), for the tests.</summary>
        public static VoiceJitterBuffer BufferOf(PlayerNet p) => s_Speakers.TryGetValue(p, out var sp) ? sp.Buffer : null;

        // ---------------- playback ----------------

        static AudioClip s_Carrier;

        /// <summary>
        /// The AudioSource plays a steady 1.0 (so Unity spatialises it: distance fade and panning) and VoiceTap, a filter
        /// right after it, multiplies that by the voice from the jitter buffer on the audio thread - no streaming-clip
        /// read-ahead: the voice is pulled the moment the mixer needs it.
        /// </summary>
        public static VoiceTap MakeSource(Transform parent, bool spatial, VoiceJitterBuffer buf, out AudioSource src)
        {
            int outRate = AudioSettings.outputSampleRate;
            if (s_Carrier == null)
            {
                s_Carrier = AudioClip.Create("voice-carrier", outRate, 1, outRate, false);
                var ones = new float[outRate];
                for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
                s_Carrier.SetData(ones, 0);
            }
            var go = new GameObject("voice");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spatial ? Vector3.up * 1.6f : Vector3.zero;
            src = go.AddComponent<AudioSource>();
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f;
            src.maxDistance = 45f;
            src.dopplerLevel = 0f;
            src.loop = true;
            src.clip = s_Carrier;
            src.priority = 32;
            var tap = go.AddComponent<VoiceTap>();
            tap.Buffer = buf;
            tap.OutRate = outRate;
            tap.Volume = GameSettings.VoiceVolume;
            src.Play();
            return tap;
        }

        class Speaker
        {
            public AudioSource Source;
            public readonly VoiceJitterBuffer Buffer = new VoiceJitterBuffer();
            public float LastPacket;

            public Speaker(Transform parent, bool spatial) => MakeSource(parent, spatial, Buffer, out Source);

            public void Push(byte[] data)
            {
                if (data.Length > 3) LastPacket = Time.time;
                Buffer.Push(data, Time.realtimeSinceStartupAsDouble);
            }

            public void Destroy() { if (Source != null) Object.Destroy(Source.gameObject); }
        }
    }

    /// <summary>The audio-thread end of a voice: fills the AudioSource's output from its jitter buffer.</summary>
    public class VoiceTap : MonoBehaviour
    {
        public VoiceJitterBuffer Buffer;
        public volatile int OutRate = 48000;
        public volatile float Volume = 1f;

        void Update()
        {
            Volume = GameSettings.VoiceVolume;
            OutRate = AudioSettings.outputSampleRate;
        }

        void OnAudioFilterRead(float[] data, int channels) => Buffer?.Read(data, channels, OutRate, Volume, true);
    }
}

