using System;
using System.Collections.Generic;

namespace RockGame
{
    // The proximity voice chat's signal path, in plain C# (no Unity calls) so AutoTest.Voice can run it on a virtual clock:
    //   mic (any rate) -> VoiceResampler (anti-alias low-pass + interpolation to 16 kHz)
    //   -> VoiceTx (high-pass, noise gate, AGC, optional AlienVoice filter, soft limiter) in 20 ms frames
    //   -> VoiceCodec (IMA ADPCM, 4 bits a sample: 166 bytes a frame, 66 kbit/s) -> unreliable RPC through the server
    //   -> VoiceJitterBuffer (sequence numbers, adaptive delay, loss concealment, drift control) -> 3D AudioSource.

    /// <summary>IMA ADPCM at 16 kHz in self-contained 20 ms packets (each carries the codec state, so a lost packet costs only itself).</summary>
    public static class VoiceCodec
    {
        public const int Rate = 16000, FrameSamples = 320, Header = 6, PacketBytes = Header + FrameSamples / 2;
        public const float FrameSec = FrameSamples / (float)Rate;
        /// <summary>Packet flag: the talk spurt ended after the frame with this sequence number (a 3-byte packet).</summary>
        public const byte FlagEnd = 1;

        static readonly int[] s_Step =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552,
            1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487,
            12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };
        static readonly int[] s_Index = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        /// <summary>The encoder's running state (carried from frame to frame; also written into every packet).</summary>
        public struct State { public int Pred, Index; public float LastIn; }

        /// <summary>
        /// Pre-emphasis: the encoder codes x[n] - 0.85 x[n-1] (scaled to stay in range) and the player undoes it
        /// (DeEmphasis). ADPCM's noise is flat, so this pushes it down where the voice is quiet (the high consonants)
        /// and up under the loud low end where the voice hides it.
        /// </summary>
        public const float Emph = 0.85f, EmphScale = 0.55f;

        /// <summary>Undo the pre-emphasis on a decoded frame, in playing order (z: the last output sample before it).</summary>
        public static void DeEmphasis(float[] pcm, ref float z)
        {
            for (int i = 0; i < pcm.Length; i++) { z = pcm[i] / EmphScale + Emph * z; pcm[i] = z; }
        }

        [ThreadStatic] static int[] s_Emph;

        public static byte[] EndPacket(ushort lastSeq) => new byte[] { FlagEnd, (byte)lastSeq, (byte)(lastSeq >> 8) };

        public static byte[] Encode(float[] pcm, ushort seq, ref State st)
        {
            var b = new byte[PacketBytes];
            b[0] = 0;
            b[1] = (byte)seq; b[2] = (byte)(seq >> 8);
            b[3] = (byte)st.Pred; b[4] = (byte)(st.Pred >> 8);
            b[5] = (byte)st.Index;
            int pred = st.Pred, idx = st.Index;
            var e = s_Emph ??= new int[FrameSamples];
            for (int i = 0; i < FrameSamples; i++)
            {
                e[i] = Pcm16((pcm[i] - Emph * st.LastIn) * EmphScale);
                st.LastIn = pcm[i];
            }
            for (int i = 0; i < FrameSamples; i++)
            {
                // pick the code that does best over this sample and the next one (any of 16 x 16), not just this one:
                // the step size it leads to matters as much as the sample itself (~6 dB better than plain IMA on voice)
                int s0 = e[i], s1 = i + 1 < FrameSamples ? e[i + 1] : s0;
                long best = long.MaxValue;
                int nib = 0;
                for (int a = 0; a < 16; a++)
                {
                    int p1 = pred, i1 = idx;
                    Step(a, ref p1, ref i1);
                    long e1 = (long)(s0 - p1) * (s0 - p1);
                    if (e1 >= best) continue;
                    long e2 = long.MaxValue;
                    for (int b2 = 0; b2 < 16; b2++)
                    {
                        int p2 = p1, i2 = i1;
                        Step(b2, ref p2, ref i2);
                        long d2 = (long)(s1 - p2) * (s1 - p2);
                        if (d2 < e2) e2 = d2;
                    }
                    if (e1 + e2 < best) { best = e1 + e2; nib = a; }
                }
                Step(nib, ref pred, ref idx);
                if ((i & 1) == 0) b[Header + (i >> 1)] = (byte)nib;
                else b[Header + (i >> 1)] |= (byte)(nib << 4);
            }
            st.Pred = pred; st.Index = idx;
            return b;
        }

        static int Pcm16(float x) => (int)Math.Round(Math.Max(-1f, Math.Min(1f, x)) * 32767f);

        /// <summary>The decoder's update (the encoder runs the same one, so both stay in step).</summary>
        static void Step(int nib, ref int pred, ref int idx)
        {
            int step = s_Step[idx];
            int d = step >> 3;
            if ((nib & 4) != 0) d += step;
            if ((nib & 2) != 0) d += step >> 1;
            if ((nib & 1) != 0) d += step >> 2;
            pred += (nib & 8) != 0 ? -d : d;
            if (pred > 32767) pred = 32767; else if (pred < -32768) pred = -32768;
            idx += s_Index[nib];
            if (idx < 0) idx = 0; else if (idx > 88) idx = 88;
        }

        public static bool ReadHeader(byte[] b, out byte flags, out ushort seq)
        {
            flags = 0; seq = 0;
            if (b == null || b.Length < 3) return false;
            flags = b[0];
            seq = (ushort)(b[1] | (b[2] << 8));
            return (flags & FlagEnd) != 0 || b.Length == PacketBytes;
        }

        public static void Decode(byte[] b, float[] pcm)
        {
            int pred = (short)(b[3] | (b[4] << 8)), idx = Math.Min(88, (int)b[5]);
            for (int i = 0; i < FrameSamples; i++)
            {
                int v = b[Header + (i >> 1)];
                int nib = (i & 1) == 0 ? v & 15 : v >> 4;
                Step(nib, ref pred, ref idx);
                pcm[i] = pred / 32768f;
            }
        }
    }

    /// <summary>A biquad (RBJ cookbook), direct form I.</summary>
    public struct Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public static Biquad LowPass(float fs, float fc, float q) => Make(fs, fc, q, false);
        public static Biquad HighPass(float fs, float fc, float q) => Make(fs, fc, q, true);

        static Biquad Make(float fs, float fc, float q, bool high)
        {
            double w = 2 * Math.PI * fc / fs, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            var f = new Biquad();
            if (high) { f.b0 = (float)((1 + c) / 2 / a0); f.b1 = (float)(-(1 + c) / a0); }
            else { f.b0 = (float)((1 - c) / 2 / a0); f.b1 = (float)((1 - c) / a0); }
            f.b2 = f.b0;
            f.a1 = (float)(-2 * c / a0);
            f.a2 = (float)((1 - alpha) / a0);
            return f;
        }

        public float Run(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            if (y > -1e-15f && y < 1e-15f) y1 = 0f; // no denormals
            return y;
        }
    }

    /// <summary>
    /// Streaming sample-rate converter: a 6th-order Butterworth low-pass below the new Nyquist (when going down) and
    /// cubic (Catmull-Rom) interpolation at the fractional positions. Keeps its state between calls.
    /// </summary>
    public class VoiceResampler
    {
        readonly double m_Step;
        readonly bool m_Filter;
        Biquad m_F1, m_F2, m_F3;
        double m_Pos = 1.0; // where the next output sits between h1 (0) and h0 (1)
        float h0, h1;       // the last two input samples before the pending one (h0 newest)
        float m_Pending;    // one sample of look-ahead
        bool m_Have;

        public VoiceResampler(int inRate, int outRate)
        {
            m_Step = inRate / (double)outRate;
            m_Filter = inRate > outRate;
            float fc = Math.Min(0.43f * outRate, 0.45f * inRate);
            m_F1 = Biquad.LowPass(inRate, fc, 0.5176f);
            m_F2 = Biquad.LowPass(inRate, fc, 0.7071f);
            m_F3 = Biquad.LowPass(inRate, fc, 1.9319f);
        }

        public void Process(float[] src, int n, List<float> dst)
        {
            for (int i = 0; i < n; i++)
            {
                float x = src[i];
                if (m_Filter) x = m_F3.Run(m_F2.Run(m_F1.Run(x)));
                if (!m_Have) { h0 = h1 = m_Pending = x; m_Have = true; continue; }
                // interpolate between h0 and the pending sample, with h1 behind and x ahead
                float pm1 = h0, p0 = m_Pending, p1 = x, pm2 = h1;
                while (m_Pos <= 1.0)
                {
                    dst.Add(CatmullRom(pm2, pm1, p0, p1, (float)m_Pos));
                    m_Pos += m_Step;
                }
                m_Pos -= 1.0;
                h1 = h0; h0 = m_Pending; m_Pending = x;
            }
        }

        /// <summary>The curve through p0 (t = 0) and p1 (t = 1) with pm1 before and p2 after.</summary>
        public static float CatmullRom(float pm1, float p0, float p1, float p2, float t)
        {
            float a = -0.5f * pm1 + 1.5f * p0 - 1.5f * p1 + 0.5f * p2;
            float b = pm1 - 2.5f * p0 + 2f * p1 - 0.5f * p2;
            float c = -0.5f * pm1 + 0.5f * p1;
            return ((a * t + b) * t + c) * t + p0;
        }
    }

    /// <summary>
    /// The alien voice (Settings > Voice chat > ALIEN VOICE): pitch up ~1.3x (a two-tap delay-line shifter), a 48 Hz ring
    /// modulation for the robotic buzz, and a short flanger with feedback for the metallic shimmer. 16 kHz, one sample at a time.
    /// </summary>
    public class AlienVoice
    {
        public const float Pitch = 1.32f, RingHz = 48f, RingDepth = 0.45f;
        const int Len = 2048, Mask = Len - 1;
        const float Window = 480f; // 30 ms grains
        readonly float[] m_Shift = new float[Len], m_Flange = new float[Len];
        int m_W;
        float m_D = Window, m_Ring, m_Lfo;

        public float Run(float x)
        {
            const float rate = VoiceCodec.Rate;
            m_Shift[m_W] = x;
            // pitch: two read heads sweep through the delay faster than real time, cross-faded so neither jump is heard
            m_D -= Pitch - 1f;
            if (m_D < 0f) m_D += Window;
            float d2 = m_D + Window * 0.5f;
            if (d2 >= Window) d2 -= Window;
            float g1 = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * m_D / Window);
            float g2 = 1f - g1;
            float y = g1 * Tap(m_Shift, m_D + 2f) + g2 * Tap(m_Shift, d2 + 2f);
            // ring modulation (not all the way, so the words stay clear)
            m_Ring += RingHz / rate;
            if (m_Ring >= 1f) m_Ring -= 1f;
            y *= (1f - RingDepth) + RingDepth * (float)Math.Sin(2 * Math.PI * m_Ring);
            y *= 1.35f;
            // flanger: 0.6..3 ms swept at 0.3 Hz, half fed back
            m_Lfo += 0.3f / rate;
            if (m_Lfo >= 1f) m_Lfo -= 1f;
            float fd = (0.6f + 2.4f * (0.5f + 0.5f * (float)Math.Sin(2 * Math.PI * m_Lfo))) * rate / 1000f;
            float fl = Tap(m_Flange, fd);
            m_Flange[m_W] = y + 0.5f * fl;
            m_W = (m_W + 1) & Mask;
            return (y + 0.7f * fl) * 1.5f;
        }

        float Tap(float[] buf, float delay)
        {
            float p = m_W - delay;
            int i = (int)Math.Floor(p);
            float f = p - i;
            float a = buf[i & Mask], b = buf[(i + 1) & Mask];
            return a + (b - a) * f;
        }
    }

    /// <summary>
    /// What happens to the mic between the resampler and the encoder, one 20 ms frame at a time: rumble filter, mic volume,
    /// a soft noise gate, automatic gain (speech is pulled towards one loudness), the alien voice and a soft limiter
    /// (nothing ever clips).
    /// </summary>
    public class VoiceTx
    {
        public const float AgcTarget = 0.1f, AgcMin = 0.5f, AgcMax = 3f, GateFloor = 0.18f;
        public float Agc = 1f;
        public float LastRms;
        Biquad m_Hp = Biquad.HighPass(VoiceCodec.Rate, 90f, 0.7071f);
        float m_Gate = 1f, m_GateHold;
        AlienVoice m_Alien = new AlienVoice();
        bool m_AlienWas;

        /// <summary>gate: the loudness (RMS after the mic volume) below which a frame is background noise.</summary>
        public void Process(float[] f, float micGain, float gate, bool alien)
        {
            int n = f.Length;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                f[i] = m_Hp.Run(f[i]) * micGain;
                sum += f[i] * f[i];
            }
            float rms = (float)Math.Sqrt(sum / n);
            LastRms = rms;
            bool speech = rms > gate;
            if (speech) m_GateHold = 0.2f; else m_GateHold -= VoiceCodec.FrameSec;
            float gateTo = speech || m_GateHold > 0f ? 1f : GateFloor;
            if (speech)
            {
                float want = Math.Max(AgcMin, Math.Min(AgcMax, AgcTarget / Math.Max(rms, 1e-4f)));
                // turn down quickly, up slowly (about 1.5 s)
                Agc += (want - Agc) * (want < Agc ? 0.3f : 0.013f);
            }
            if (alien != m_AlienWas) { m_Alien = new AlienVoice(); m_AlienWas = alien; }
            float g0 = m_Gate;
            for (int i = 0; i < n; i++)
            {
                float g = g0 + (gateTo - g0) * (i + 1) / n;
                float x = f[i] * g * Agc;
                if (alien) x = m_Alien.Run(x);
                f[i] = SoftLimit(x);
            }
            m_Gate = gateTo;
        }

        /// <summary>Straight through up to 0.7, then bends smoothly towards 1 (never past it).</summary>
        public static float SoftLimit(float x)
        {
            float a = Math.Abs(x);
            if (a <= 0.7f) return x;
            float y = 0.7f + 0.3f * (float)Math.Tanh((a - 0.7f) / 0.3f);
            return x < 0 ? -y : y;
        }
    }

    /// <summary>
    /// One talker's playback: packets go in from the network (any order, some lost), audio comes out at the output rate.
    /// The delay adapts: just enough to cover how unevenly packets arrive (+ one audio block + one frame), grown after
    /// a real underrun and shrunk again over time; when too much has piled up it plays up to 4% faster to catch up.
    /// A lost packet is covered by a faded copy of the one before. Thread-safe (Push on the main thread, Read on the audio thread).
    /// </summary>
    public class VoiceJitterBuffer
    {
        const int N = VoiceCodec.FrameSamples, Slots = 64;
        readonly float[][] m_Pcm = new float[Slots][];
        readonly int[] m_SlotSeq = new int[Slots];
        readonly bool[] m_Done = new bool[Slots]; // de-emphasised yet?
        float m_De;
        int m_DeSeq = int.MinValue;
        readonly float[] m_Prev = new float[N];
        readonly object m_Lock = new object();
        bool m_Playing, m_HasHigh;
        int m_PlaySeq, m_High, m_EndSeq = int.MinValue, m_LastPlayed = int.MinValue;
        double m_Pos, m_Speed = 1.0, m_MinTransit, m_LastArrival = -1;
        readonly float[] m_Delays = new float[100], m_Sorted = new float[100];
        int m_DelayN, m_DelayW;
        float m_Jitter, m_Boost, m_BlockSec = 0.02f, m_Tail, m_Fade;

        public int Received, Lost, Late, Underruns;
        public float TargetSec, BufferedSec;
        public float JitterSec => m_Jitter;
        public bool Playing => m_Playing;

        public VoiceJitterBuffer()
        {
            for (int i = 0; i < Slots; i++) { m_Pcm[i] = new float[N]; m_SlotSeq[i] = int.MinValue; }
        }

        static int Slot(int seq) => seq & (Slots - 1);

        public void Push(byte[] pkt, double now)
        {
            if (!VoiceCodec.ReadHeader(pkt, out byte flags, out ushort s16)) return;
            lock (m_Lock)
            {
                int seq = m_HasHigh ? m_High + (short)(s16 - (ushort)m_High) : s16;
                if (m_HasHigh && Math.Abs(seq - m_High) > Slots * 4)
                {
                    // the sender started over (rejoined): forget the old stream
                    for (int i = 0; i < Slots; i++) m_SlotSeq[i] = int.MinValue;
                    m_Playing = false;
                    m_HasHigh = false;
                    m_LastPlayed = m_EndSeq = int.MinValue;
                    seq = s16;
                }
                if ((flags & VoiceCodec.FlagEnd) != 0) { m_EndSeq = seq; return; }
                Received++;
                // how late is this packet compared with the earliest one (by sequence)? the peak of that is the jitter
                double transit = now - seq * (double)VoiceCodec.FrameSec;
                // a new talk spurt (a pause, or the last one was marked ended): the sequence numbers skipped the pause, so start
                // the reference again (keeping what we learnt about the jitter)
                if (m_LastArrival < 0 || now - m_LastArrival > 0.15 || m_EndSeq == seq - 1) m_MinTransit = transit;
                else
                {
                    m_MinTransit = Math.Min(m_MinTransit + (now - m_LastArrival) * 0.002, transit); // (creeps up for clock drift)
                    float d = (float)(transit - m_MinTransit);
                    // the jitter: 95% of the last 2 s of packets came at most this much later than the earliest
                    // (one hitch doesn't blow the delay up; if it causes an underrun, m_Boost grows instead)
                    m_Delays[m_DelayW] = d;
                    m_DelayW = (m_DelayW + 1) % m_Delays.Length;
                    if (m_DelayN < m_Delays.Length) m_DelayN++;
                    Array.Copy(m_Delays, m_Sorted, m_DelayN);
                    Array.Sort(m_Sorted, 0, m_DelayN);
                    m_Jitter = m_Sorted[Math.Min(m_DelayN - 1, (int)(m_DelayN * 0.95f))];
                }
                m_LastArrival = now;
                if (m_Playing && seq < m_PlaySeq || seq <= m_LastPlayed && seq > m_LastPlayed - Slots) { Late++; return; }
                if (m_HasHigh && seq < m_High - Slots / 2) { Late++; return; }
                if (!m_HasHigh || seq > m_High) { m_High = seq; m_HasHigh = true; }
                int sl = Slot(seq);
                VoiceCodec.Decode(pkt, m_Pcm[sl]);
                m_SlotSeq[sl] = seq;
                m_Done[sl] = false;
            }
        }

        float Target() => Math.Min(0.4f, m_BlockSec + VoiceCodec.FrameSec + m_Jitter + m_Boost + 0.004f);

        bool Has(int seq) => m_SlotSeq[Slot(seq)] == seq;

        /// <summary>Undo the pre-emphasis on a frame, carrying the filter on from the frame before when that was the last one done.</summary>
        void Ready(int seq)
        {
            int sl = Slot(seq);
            if (m_SlotSeq[sl] != seq || m_Done[sl]) return;
            if (m_DeSeq != seq - 1) m_De = 0f;
            VoiceCodec.DeEmphasis(m_Pcm[sl], ref m_De);
            m_DeSeq = seq;
            m_Done[sl] = true;
        }

        /// <summary>The voice at a sample index counted from the start of the frame being played.</summary>
        float At(int k)
        {
            if (k < 0) return m_Prev[N + k];
            if (k < N) return m_Pcm[Slot(m_PlaySeq)][k];
            if (Has(m_PlaySeq + 1)) Ready(m_PlaySeq + 1);
            return Has(m_PlaySeq + 1) ? m_Pcm[Slot(m_PlaySeq + 1)][k - N] : m_Pcm[Slot(m_PlaySeq)][N - 1];
        }

        void TryStart(float target)
        {
            if (!m_HasHigh) return;
            int lo = int.MaxValue;
            for (int i = 0; i < Slots; i++)
                if (m_SlotSeq[i] != int.MinValue && m_SlotSeq[i] > m_LastPlayed && m_SlotSeq[i] < lo) lo = m_SlotSeq[i];
            if (lo == int.MaxValue) return;
            float buffered = (m_High - lo + 1) * VoiceCodec.FrameSec;
            if (buffered + 1e-4f < target && m_EndSeq < m_High) return;
            m_PlaySeq = lo;
            m_Pos = 0;
            m_Speed = 1.0;
            m_Playing = true;
            Array.Clear(m_Prev, 0, N);
            Ready(m_PlaySeq);
        }

        void Advance()
        {
            m_Pos -= N;
            int sl = Slot(m_PlaySeq);
            Array.Copy(m_Pcm[sl], m_Prev, N);
            m_SlotSeq[sl] = int.MinValue;
            m_LastPlayed = m_PlaySeq;
            m_PlaySeq++;
            if (Has(m_PlaySeq)) { Ready(m_PlaySeq); return; }
            if (m_High > m_PlaySeq)
            {
                // lost: play the frame before again, quieter
                var dst = m_Pcm[Slot(m_PlaySeq)];
                for (int i = 0; i < N; i++) dst[i] = m_Prev[i] * 0.5f;
                m_SlotSeq[Slot(m_PlaySeq)] = m_PlaySeq;
                m_Done[Slot(m_PlaySeq)] = true;
                m_De = dst[N - 1];
                m_DeSeq = m_PlaySeq;
                Lost++;
                return;
            }
            m_Playing = false;
            if (m_EndSeq < m_LastPlayed) { Underruns++; m_Boost = Math.Min(0.15f, m_Boost + 0.015f); }
        }

        /// <summary>Fill an audio block. multiply: the block holds a spatialised 1.0 from the AudioSource (keep its panning).</summary>
        public void Read(float[] data, int channels, int outRate, float volume, bool multiply)
        {
            int frames = data.Length / channels;
            lock (m_Lock)
            {
                float blk = frames / (float)outRate;
                m_BlockSec = Math.Max(blk, m_BlockSec + (blk - m_BlockSec) * 0.05f);
                m_Boost = Math.Max(0f, m_Boost - blk * 0.01f);
                float target = Target();
                TargetSec = target;
                if (!m_Playing) TryStart(target);
                double step = VoiceCodec.Rate / (double)outRate;
                int o = 0;
                for (int f = 0; f < frames; f++)
                {
                    float s;
                    if (m_Playing)
                    {
                        int i = (int)m_Pos;
                        s = VoiceResampler.CatmullRom(At(i - 1), At(i), At(i + 1), At(i + 2), (float)(m_Pos - i));
                        if (m_Fade < 1f) { m_Fade = Math.Min(1f, m_Fade + 0.01f); s *= m_Fade; }
                        m_Tail = s;
                        m_Pos += step * m_Speed;
                        while (m_Playing && m_Pos >= N) Advance();
                        if (!m_Playing) m_Fade = 0f;
                    }
                    else s = m_Tail *= 0.995f;
                    s *= volume;
                    for (int c = 0; c < channels; c++, o++) data[o] = multiply ? data[o] * s : s;
                }
                if (m_Playing)
                {
                    float buffered = (float)((m_High - m_PlaySeq + 1) * N - m_Pos) / VoiceCodec.Rate;
                    BufferedSec = buffered;
                    float excess = buffered - target;
                    if (excess > 0.25f)
                    {
                        // far behind (a stall): jump forward
                        int to = m_High - (int)Math.Ceiling(target / VoiceCodec.FrameSec) + 1;
                        while (m_PlaySeq < to && m_Playing) { m_Pos = N; Advance(); }
                        m_Pos = 0;
                    }
                    m_Speed = excess > 0.01f ? 1.0 + Math.Min(0.04, (excess - 0.01) * 0.4) : 1.0;
                }
                else BufferedSec = 0f;
            }
        }
    }
}
