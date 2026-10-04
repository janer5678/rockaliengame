using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RockGame
{
    /// <summary>
    /// -autotest voice [-shotdir DIR] (no match needed, runs from the main menu): the proximity voice chat's signal path.
    ///  1. A simulation on a virtual clock (mic delivering every 10 ms, 60 fps game frames on both ends, an audio callback of
    ///     1024 samples at 48 kHz, 25 ms network delay + 0..20 ms jitter + 2% loss) of the voice path before this change
    ///     (8 kHz mu-law in 100 ms chunks, 200 ms fixed pre-buffer) and now (VoiceDsp: 16 kHz ADPCM in 20 ms packets,
    ///     adaptive jitter buffer), fed a synthetic voice (a gliding 140 Hz buzz with harmonics up to 7 kHz plus breath noise)
    ///     and a 1 kHz tone: mouth-to-speaker delay (cross-correlation), quality (SNR), bytes a second.
    ///  2. The alien voice: the pitch really goes up ~1.32x, it stays as loud, never clips, and is a different sound.
    ///     (voice_plain.wav / voice_alien.wav / voice_old.wav go to DIR to listen to.)
    ///  3. The real Unity audio path: the time from a packet arriving to its sound reaching the mixer, for the new
    ///     AudioSource + filter playback and for the old streaming-clip playback (skipped when there's no audio device).
    ///  4. ALIEN VOICE is saved like the other sound settings.
    /// </summary>
    public partial class AutoTest
    {
        const int SimOut = 48000;

        /// <summary>The synthetic voice: a buzz whose pitch wanders 110..180 Hz, harmonics to 7 kHz with formant bumps, some breath.</summary>
        class TestVoice
        {
            readonly float[] m_Sig;
            public readonly int Rate;
            public TestVoice(int rate, float seconds, int seed, bool tone)
            {
                Rate = rate;
                m_Sig = new float[(int)(rate * seconds)];
                var rnd = new System.Random(seed);
                double ph = 0, f0 = 140, fv = 0;
                float n1 = 0, n2 = 0;
                // (the breath is kept under 6 kHz, like the voice, so no path is blamed for what it can't carry)
                var lp1 = Biquad.LowPass(rate, 6000f, 0.5412f); var lp2 = Biquad.LowPass(rate, 6000f, 1.3066f);
                for (int i = 0; i < m_Sig.Length; i++)
                {
                    double t = i / (double)rate;
                    if (tone) { m_Sig[i] = 0.25f * (float)Math.Sin(2 * Math.PI * 1000 * t); continue; }
                    fv += (rnd.NextDouble() - 0.5) * 0.02 - fv * 0.0005;
                    f0 = Math.Max(110, Math.Min(180, f0 + fv * 0.05));
                    ph += f0 / rate;
                    double s = 0;
                    for (int k = 1; k * f0 < 7000; k++)
                    {
                        double f = k * f0;
                        double formant = 1 + 2.5 * Math.Exp(-Math.Pow((f - 700) / 250, 2)) + 1.8 * Math.Exp(-Math.Pow((f - 1500) / 300, 2)) + 1.2 * Math.Exp(-Math.Pow((f - 2600) / 400, 2));
                        s += Math.Sin(2 * Math.PI * k * ph + k * 0.7) * formant / Math.Pow(k, 0.9);
                    }
                    // breath: white noise, gently high-passed
                    float w = (float)(rnd.NextDouble() * 2 - 1);
                    float hp = w - n1; n1 = w; n2 = lp2.Run(lp1.Run(hp));
                    m_Sig[i] = (float)(s * 0.035) + n2 * 0.02f;
                }
            }

            /// <summary>The voice at time t (0 outside the talk spurts, faded in / out over 5 ms).</summary>
            public float At(int i, bool[] talking)
            {
                if (i < 0 || i >= m_Sig.Length) return 0f;
                return talking[(int)((long)i * SimOut / Rate)] ? m_Sig[i] : 0f;
            }
        }

        struct SimResult
        {
            public float[] In, Out;
            public float LatencyMs, OnsetMs, SnrDb, HighSnrDb, KbitPerSec;
            public int Lost, Underruns;
        }

        static bool[] SpurtMask(float seconds, out List<float> starts)
        {
            var m = new bool[(int)(SimOut * seconds)];
            starts = new List<float>();
            for (float s = 0.4f; s + 2f < seconds; s += 2.6f)
            {
                starts.Add(s);
                for (int i = (int)(s * SimOut); i < (int)((s + 2f) * SimOut); i++) m[i] = true;
            }
            return m;
        }

        /// <summary>
        /// The whole path on a virtual clock. newPath: VoiceDsp; otherwise the old one. Returns the input and the output on one
        /// 48 kHz timeline (output sample j is what the audio callback hands the mixer for time j / 48000).
        /// </summary>
        static SimResult Simulate(bool newPath, bool tone, float jitterMs, float loss, bool alien, int seed, float seconds = 8.4f)
        {
            var talking = SpurtMask(seconds, out var starts);
            int micRate = newPath ? 48000 : 16000;
            var voice48 = new TestVoice(SimOut, seconds, 7, tone);
            int dec = SimOut / micRate;
            // (a 16 kHz mic: the 48 kHz voice averaged in threes)
            float Mic(int j) { float a = 0; for (int q = 0; q < dec; q++) a += voice48.At(j * dec + q, talking); return a / dec; }
            var res = new SimResult { In = new float[talking.Length], Out = new float[talking.Length] };
            for (int i = 0; i < res.In.Length; i++) res.In[i] = voice48.At(i, talking) * 1.5f;
            // what the path is meant to deliver: the new one takes the rumble (< 90 Hz) out on purpose, so it's measured
            // against the voice with that same high-pass (the old one against the voice as it is)
            var reference = res.In;
            if (newPath)
            {
                reference = new float[res.In.Length];
                var hp = Biquad.HighPass(SimOut, 90f, 0.7071f);
                // (and its anti-alias low-pass at 6.9 kHz, the same filter as VoiceResampler's: its phase shift isn't heard)
                float fc = 0.43f * VoiceCodec.Rate;
                var l1 = Biquad.LowPass(SimOut, fc, 0.5176f); var l2 = Biquad.LowPass(SimOut, fc, 0.7071f); var l3 = Biquad.LowPass(SimOut, fc, 1.9319f);
                for (int i = 0; i < reference.Length; i++) reference[i] = l3.Run(l2.Run(l1.Run(hp.Run(res.In[i]))));
            }
            var rnd = new System.Random(seed);
            var inFlight = new List<(double at, byte[] pkt)>();
            int bytes = 0;
            double sendStep = 1 / 60.0, recvStep = 1 / 60.0, recvOffset = 0.007, block = 1024.0 / SimOut;
            int micDone = 0;
            double nextSend = 0, nextRecv = recvOffset, nextBlock = 0;
            // new path state
            var rs = new VoiceResampler(micRate, VoiceCodec.Rate);
            var tx = new VoiceTx();
            var pcm = new List<float>();
            var enc = new VoiceCodec.State();
            ushort seq = 0;
            bool was = false;
            var jb = new VoiceJitterBuffer();
            // old path state
            float acc = 0; int accN = 0;
            var pending = new List<float>();
            var ring = new float[16000];
            int rW = 0, rR = 0, rC = 0;
            bool oldPlaying = false;
            double oldPos = 0;
            float[] blockBuf = new float[1024];
            int outIdx = 0;

            void Net(byte[] pkt, double now)
            {
                bytes += pkt.Length;
                if (rnd.NextDouble() < loss) return;
                inFlight.Add((now + 0.025 + rnd.NextDouble() * jitterMs / 1000.0, pkt));
            }

            for (double t = 0; t < seconds - 0.05; t += 0.0005)
            {
                if (t >= nextSend)
                {
                    nextSend += sendStep;
                    // the mic hands over what it has, in 10 ms steps
                    int avail = (int)(Math.Floor(t / 0.01) * 0.01 * micRate);
                    int n = avail - micDone;
                    if (n > 0)
                    {
                        var chunk = new float[n];
                        for (int i = 0; i < n; i++) chunk[i] = Mic(micDone + i);
                        int t48 = Math.Min(talking.Length - 1, (int)((long)avail * SimOut / micRate));
                        // the key is down while the voice plays (from the mic's point of view)
                        bool talkNow = talking[Math.Max(0, t48 - 1)];
                        micDone = avail;
                        if (newPath)
                        {
                            rs.Process(chunk, n, pcm);
                            while (pcm.Count >= VoiceCodec.FrameSamples)
                            {
                                var f = new float[VoiceCodec.FrameSamples];
                                pcm.CopyTo(0, f, 0, f.Length);
                                pcm.RemoveRange(0, f.Length);
                                tx.Process(f, 1.5f, 0.006f, alien);
                                if (talkNow) { Net(VoiceCodec.Encode(f, seq++, ref enc), t); was = true; }
                                else if (was) { Net(VoiceCodec.EndPacket((ushort)(seq - 1)), t); was = false; }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < n; i++)
                            {
                                float x = Mathf.Clamp(chunk[i] * 1.5f, -1f, 1f);
                                acc += x; accN++;
                                if (accN >= 2) { pending.Add(acc / accN); acc = 0; accN = 0; }
                            }
                            if (!talkNow) pending.Clear();
                            while (pending.Count >= 800)
                            {
                                var b = new byte[800];
                                for (int i = 0; i < 800; i++) b[i] = OldMuLawEncode(pending[i]);
                                pending.RemoveRange(0, 800);
                                Net(b, t);
                            }
                        }
                    }
                }
                if (t >= nextRecv)
                {
                    nextRecv += recvStep;
                    for (int i = inFlight.Count - 1; i >= 0; i--)
                    {
                        if (inFlight[i].at > t) continue;
                        var pkt = inFlight[i].pkt;
                        inFlight.RemoveAt(i);
                        if (newPath) jb.Push(pkt, t);
                        else
                            foreach (var b in pkt)
                            {
                                if (rC >= ring.Length) { rR = (rR + 1) % ring.Length; rC--; }
                                ring[rW] = OldMuLawDecode(b); rW = (rW + 1) % ring.Length; rC++;
                            }
                    }
                }
                if (t >= nextBlock)
                {
                    nextBlock += block;
                    if (newPath) jb.Read(blockBuf, 1, SimOut, 1f, false);
                    else
                    {
                        // the old streaming clip at 8 kHz (best case: read just in time, one block at a time), Unity resampling it up
                        for (int i = 0; i < blockBuf.Length; i++)
                        {
                            if (!oldPlaying && rC >= 8000 / 5) oldPlaying = true;
                            float s = 0;
                            if (oldPlaying && rC > 1)
                            {
                                float a = ring[rR], c = ring[(rR + 1) % ring.Length];
                                s = a + (c - a) * (float)oldPos;
                                oldPos += 8000.0 / SimOut;
                                while (oldPos >= 1) { oldPos -= 1; rR = (rR + 1) % ring.Length; rC--; }
                            }
                            else oldPlaying = false;
                            blockBuf[i] = s;
                        }
                    }
                    for (int i = 0; i < blockBuf.Length && outIdx < res.Out.Length; i++) res.Out[outIdx++] = blockBuf[i];
                }
            }
            res.KbitPerSec = bytes * 8f / 1000f / (starts.Count * 2f);
            res.Lost = jb.Lost;
            res.Underruns = jb.Underruns;
            // delay: cross-correlate the middle of every spurt; onset: when the output first gets loud after each spurt starts
            float lat = 0, onset = 0, snr = 0, hiSnr = 0;
            foreach (var s in starts)
            {
                int w0 = (int)((s + 1.0f) * SimOut);
                float lag = BestLag(reference, res.Out, w0, 9600, 0, (int)(0.7f * SimOut), tone);
                lat += lag / SimOut * 1000f;
                snr += Snr(reference, res.Out, w0, 9600, lag, out float hi);
                hiSnr += hi;
                float peak = 0;
                for (int i = w0; i < w0 + 9600; i++) peak = Math.Max(peak, Math.Abs(res.Out[(int)Math.Min(res.Out.Length - 1, i + lag)]));
                int j = (int)(s * SimOut);
                while (j < res.Out.Length - 1 && Math.Abs(res.Out[j]) < peak * 0.25f) j++;
                onset += (j - s * SimOut) / SimOut * 1000f;
            }
            res.LatencyMs = lat / starts.Count;
            res.OnsetMs = onset / starts.Count;
            res.SnrDb = snr / starts.Count;
            res.HighSnrDb = hiSnr / starts.Count;
            return res;
        }

        /// <summary>The delay (in 48 kHz samples, fractional) of y behind x around window [w0, w0+len).</summary>
        static float BestLag(float[] x, float[] y, int w0, int len, int lo, int hi, bool tone)
        {
            if (tone) return 0; // (a tone has no single lag; Snr fits it)
            // coarse: every 4th sample, every 4th lag
            double best = double.MinValue; int bl = lo;
            for (int L = lo; L < hi; L += 4)
            {
                if (w0 + len + L >= y.Length) break;
                double c = 0;
                for (int i = 0; i < len; i += 4) c += x[w0 + i] * y[w0 + i + L];
                if (c > best) { best = c; bl = L; }
            }
            double C(int L)
            {
                double c = 0;
                for (int i = 0; i < len; i++) { int k = w0 + i + L; if (k >= 0 && k < y.Length) c += x[w0 + i] * y[k]; }
                return c;
            }
            best = double.MinValue; int fine = bl;
            for (int L = bl - 6; L <= bl + 6; L++) { double c = C(L); if (c > best) { best = c; fine = L; } }
            double cm = C(fine - 1), c0 = C(fine), cp = C(fine + 1), den = cm - 2 * c0 + cp;
            return fine + (Math.Abs(den) > 1e-12 ? (float)(0.5 * (cm - cp) / den) : 0f);
        }

        /// <summary>
        /// Signal to noise of y against x over the window: y read at the lag, scaled by the best gain. For the tone (lag 0)
        /// a 1 kHz sine is fitted to y instead and everything else counts as noise.
        /// </summary>
        static float Snr(float[] x, float[] y, int w0, int len, float lag, out float highBand)
        {
            double sig = 0, err = 0;
            highBand = 0;
            if (lag == 0)
            {
                double ss = 0, sc = 0, cc = 0, sy = 0, cy = 0;
                for (int i = 0; i < len; i++)
                {
                    double ph = 2 * Math.PI * 1000 * (w0 + i) / SimOut, s = Math.Sin(ph), c = Math.Cos(ph), v = y[w0 + i];
                    ss += s * s; cc += c * c; sc += s * c; sy += s * v; cy += c * v;
                }
                double det = ss * cc - sc * sc, a = (sy * cc - cy * sc) / det, b = (cy * ss - sy * sc) / det;
                for (int i = 0; i < len; i++)
                {
                    double ph = 2 * Math.PI * 1000 * (w0 + i) / SimOut, f = a * Math.Sin(ph) + b * Math.Cos(ph);
                    sig += f * f; err += (y[w0 + i] - f) * (y[w0 + i] - f);
                }
                return (float)(10 * Math.Log10(sig / Math.Max(err, 1e-20)));
            }
            var ya = new float[len];
            int li = (int)Math.Floor(lag); float fr = lag - li;
            float Y(int k) => k >= 0 && k < y.Length ? y[k] : 0f;
            double xy = 0, yy = 0;
            for (int i = 0; i < len; i++)
            {
                int k = w0 + i + li;
                ya[i] = VoiceResampler.CatmullRom(Y(k - 1), Y(k), Y(k + 1), Y(k + 2), fr);
                xy += x[w0 + i] * ya[i]; yy += ya[i] * ya[i];
            }
            double g = yy > 0 ? xy / yy : 0;
            for (int i = 0; i < len; i++) { double e = x[w0 + i] - g * ya[i]; sig += x[w0 + i] * x[w0 + i]; err += e * e; }
            // the same, in the 3.5-7 kHz band only (the "s", "f", "t" sounds: where a muffled voice loses its words)
            {
                var h1 = Biquad.HighPass(SimOut, 3500f, 0.5412f); var h2 = Biquad.HighPass(SimOut, 3500f, 1.3066f);
                var l1 = Biquad.LowPass(SimOut, 7000f, 0.7071f);
                var k1 = Biquad.HighPass(SimOut, 3500f, 0.5412f); var k2 = Biquad.HighPass(SimOut, 3500f, 1.3066f);
                var m1 = Biquad.LowPass(SimOut, 7000f, 0.7071f);
                double hs = 0, he = 0;
                for (int i = 0; i < len; i++)
                {
                    float bx = l1.Run(h2.Run(h1.Run(x[w0 + i]))), by = m1.Run(k2.Run(k1.Run((float)(g * ya[i]))));
                    if (i < 480) continue;
                    hs += bx * bx; he += (bx - by) * (bx - by);
                }
                highBand = (float)(10 * Math.Log10(hs / Math.Max(he, 1e-20)));
            }
            return (float)(10 * Math.Log10(sig / Math.Max(err, 1e-20)));
        }

        // the old path's mu-law (as it was in VoiceChat.cs before), for the comparison
        static byte OldMuLawEncode(float f)
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

        static float OldMuLawDecode(byte b)
        {
            int u = ~b & 0xFF;
            int sign = u & 0x80, exp = (u >> 4) & 7, mant = u & 0x0F;
            int s = ((mant << 3) + 132) << exp;
            s -= 132;
            return (sign != 0 ? -s : s) / 32767f;
        }

        /// <summary>The strongest frequency between lo and hi Hz (a plain DFT scan, 1 Hz steps).</summary>
        static float PeakHz(float[] x, int rate, int from, int len, float lo, float hi)
        {
            float best = 0, bf = lo;
            for (float f = lo; f <= hi; f += 1f)
            {
                double re = 0, im = 0;
                for (int i = 0; i < len; i++)
                {
                    double ph = 2 * Math.PI * f * i / rate, w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / len);
                    re += x[from + i] * w * Math.Cos(ph); im += x[from + i] * w * Math.Sin(ph);
                }
                float m = (float)(re * re + im * im);
                if (m > best) { best = m; bf = f; }
            }
            return bf;
        }

        static void WriteWav(string path, float[] s, int rate)
        {
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + s.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(s.Length * 2);
            foreach (var v in s) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
        }

        IEnumerator VoiceRoutine()
        {
            string dir = "Logs";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") dir = args[i + 1];
            System.IO.Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(1f);

            // ---- 1. before / after on the virtual clock
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var oldClean = Simulate(false, false, 0, 0, false, 1);
            var newClean = Simulate(true, false, 0, 0, false, 1);
            var oldNet = Simulate(false, false, 20, 0.02f, false, 2);
            var newNet = Simulate(true, false, 20, 0.02f, false, 2);
            var oldTone = Simulate(false, true, 0, 0, false, 3);
            var newTone = Simulate(true, true, 0, 0, false, 3);
            Log($"voice sim took {sw.ElapsedMilliseconds} ms");
            // the codecs on their own (same voice, each at its own rate)
            {
                var all = new bool[SimOut * 2];
                for (int i = 0; i < all.Length; i++) all[i] = true;
                double CodecSnr(int rate, bool adpcm)
                {
                    var v = new TestVoice(16000, 2f, 7, false);
                    int every = 16000 / rate;
                    var enc = new VoiceCodec.State();
                    var f = new float[VoiceCodec.FrameSamples]; var o = new float[VoiceCodec.FrameSamples];
                    double sig = 0, err = 0;
                    float dz = 0;
                    for (int start = 0; start + f.Length <= rate * 2 - 1; start += f.Length)
                    {
                        for (int i = 0; i < f.Length; i++) f[i] = v.At((start + i) * every, all) * 1.5f;
                        if (adpcm) { VoiceCodec.Decode(VoiceCodec.Encode(f, 0, ref enc), o); VoiceCodec.DeEmphasis(o, ref dz); }
                        else for (int i = 0; i < f.Length; i++) o[i] = OldMuLawDecode(OldMuLawEncode(f[i]));
                        for (int i = 0; i < f.Length; i++) { sig += f[i] * f[i]; err += (f[i] - o[i]) * (f[i] - o[i]); }
                    }
                    return 10 * Math.Log10(sig / err);
                }
                Log($"codec alone: 8 kHz mu-law {CodecSnr(8000, false):0.0} dB, 16 kHz IMA ADPCM {CodecSnr(16000, true):0.0} dB");
            }
            Log($"BEFORE (8 kHz mu-law, 100 ms chunks, 200 ms pre-buffer): delay {oldClean.LatencyMs:0} ms (onset {oldClean.OnsetMs:0} ms) on a clean network, {oldNet.LatencyMs:0} ms (onset {oldNet.OnsetMs:0}) with 0-20 ms jitter + 2% loss; voice SNR {oldClean.SnrDb:0.0} dB (3.5-7 kHz band {oldClean.HighSnrDb:0.0} dB), 1 kHz tone SNR {oldTone.SnrDb:0.0} dB; {oldClean.KbitPerSec:0.0} kbit/s");
            Log($"AFTER (16 kHz ADPCM, 20 ms packets, adaptive jitter buffer): delay {newClean.LatencyMs:0} ms (onset {newClean.OnsetMs:0} ms) on a clean network, {newNet.LatencyMs:0} ms (onset {newNet.OnsetMs:0}) with 0-20 ms jitter + 2% loss (lost {newNet.Lost}, underruns {newNet.Underruns}); voice SNR {newClean.SnrDb:0.0} dB (3.5-7 kHz band {newClean.HighSnrDb:0.0} dB), 1 kHz tone SNR {newTone.SnrDb:0.0} dB; {newClean.KbitPerSec:0.0} kbit/s");
            Check(newClean.LatencyMs < 120f && newClean.LatencyMs < oldClean.LatencyMs * 0.5f, $"voice delay on a clean network {oldClean.LatencyMs:0} -> {newClean.LatencyMs:0} ms (simulated: mic, frames, network, buffer, audio block)");
            Check(newNet.LatencyMs < 150f && newNet.LatencyMs < oldNet.LatencyMs - 60f, $"voice delay with jitter and loss {oldNet.LatencyMs:0} -> {newNet.LatencyMs:0} ms");
            Check(newClean.SnrDb > 14f && newClean.SnrDb > oldClean.SnrDb - 1f, $"voice quality (SNR vs the whole voice) {oldClean.SnrDb:0.0} -> {newClean.SnrDb:0.0} dB");
            Check(newClean.HighSnrDb > oldClean.HighSnrDb + 6f, $"voice quality in the 3.5-7 kHz band (the consonants) {oldClean.HighSnrDb:0.0} -> {newClean.HighSnrDb:0.0} dB");
            Check(newTone.SnrDb > 20f, $"1 kHz tone SNR {oldTone.SnrDb:0.0} -> {newTone.SnrDb:0.0} dB");
            Check(newNet.SnrDb > 6f, $"voice SNR with jitter + 2% loss {oldNet.SnrDb:0.0} -> {newNet.SnrDb:0.0} dB");
            Check(newClean.KbitPerSec < 75f, $"bandwidth while talking {oldClean.KbitPerSec:0.0} -> {newClean.KbitPerSec:0.0} kbit/s a talker (x7 listeners at most, and only those within {VoiceChat.RelayRange:0} m)");
            float maxOut = 0;
            foreach (var v in newClean.Out) maxOut = Math.Max(maxOut, Math.Abs(v));
            Check(maxOut <= 1f, $"nothing clips (peak {maxOut:0.000})");

            // a loud mic: the limiter and AGC keep it under full scale
            {
                var tx = new VoiceTx();
                var f = new float[VoiceCodec.FrameSamples];
                float peak = 0;
                for (int fr = 0; fr < 50; fr++)
                {
                    for (int i = 0; i < f.Length; i++) f[i] = 0.9f * Mathf.Sin(2 * Mathf.PI * 300 * (fr * f.Length + i) / VoiceCodec.Rate);
                    tx.Process(f, 4f, 0.006f, false);
                    foreach (var v in f) peak = Math.Max(peak, Math.Abs(v));
                }
                Check(peak <= 1f && tx.Agc < 1f, $"a shouting mic at 400% mic volume stays under full scale (peak {peak:0.000}, AGC {tx.Agc:0.00})");
            }

            // ---- 2. the alien voice
            {
                var plain = Simulate(true, false, 0, 0, false, 1);
                var alien = Simulate(true, false, 0, 0, true, 1);
                WriteWav(System.IO.Path.Combine(dir, "voice_plain.wav"), plain.Out, SimOut);
                WriteWav(System.IO.Path.Combine(dir, "voice_alien.wav"), alien.Out, SimOut);
                WriteWav(System.IO.Path.Combine(dir, "voice_old.wav"), oldClean.Out, SimOut);
                WriteWav(System.IO.Path.Combine(dir, "voice_input.wav"), plain.In, SimOut);
                // a steady 150 Hz buzz through VoiceTx with and without the filter: where does the pitch go?
                var a = new List<float>(); var b = new List<float>();
                var txA = new VoiceTx(); var txB = new VoiceTx();
                var f = new float[VoiceCodec.FrameSamples]; var g = new float[VoiceCodec.FrameSamples];
                for (int fr = 0; fr < 60; fr++)
                {
                    for (int i = 0; i < f.Length; i++)
                    {
                        double t = (fr * f.Length + i) / (double)VoiceCodec.Rate, s = 0;
                        for (int k = 1; k <= 12; k++) s += Math.Sin(2 * Math.PI * 150 * k * t) / k;
                        f[i] = g[i] = (float)(s * 0.08);
                    }
                    txA.Process(f, 1f, 0.006f, true); txB.Process(g, 1f, 0.006f, false);
                    a.AddRange(f); b.AddRange(g);
                }
                var fa = a.ToArray(); var fb = b.ToArray();
                float pa = PeakHz(fa, VoiceCodec.Rate, 8000, 8000, 120, 260), pb = PeakHz(fb, VoiceCodec.Rate, 8000, 8000, 120, 260);
                double ea = 0, eb = 0, peak = 0, xab = 0;
                for (int i = 8000; i < fa.Length; i++) { ea += fa[i] * fa[i]; eb += fb[i] * fb[i]; xab += fa[i] * fb[i]; peak = Math.Max(peak, Math.Abs(fa[i])); }
                float loud = (float)Math.Sqrt(ea / eb), corr = (float)(xab / Math.Sqrt(ea * eb));
                Check(Mathf.Abs(pa / pb - AlienVoice.Pitch) < 0.06f, $"alien voice: the pitch goes up {pa / pb:0.00}x ({pb:0} Hz -> {pa:0} Hz)");
                Check(loud > 0.5f && loud < 1.6f && peak <= 1f, $"alien voice: about as loud ({loud:0.00}x), never clips (peak {peak:0.000})");
                Check(Mathf.Abs(corr) < 0.5f, $"alien voice: a different sound (correlation with the plain voice {corr:0.00})");
                Check(alien.OnsetMs < plain.OnsetMs + 40f, $"alien voice adds {alien.OnsetMs - plain.OnsetMs:0} ms of delay (when the voice starts)");
                Log($"wrote voice_input.wav / voice_old.wav / voice_plain.wav / voice_alien.wav to {dir}");
            }

            // ---- 3. the real Unity audio path (packet in -> sound at the mixer)
            yield return RealAudioPath();

            // ---- 4. the setting is saved
            {
                GameSettings.Load();
                bool was = GameSettings.AlienVoice;
                GameSettings.AlienVoice = !was;
                GameSettings.Save();
                bool saved = PlayerPrefs.GetInt("RockGame.AlienVoice", -1) == (was ? 0 : 1);
                GameSettings.AlienVoice = was;
                GameSettings.Save();
                Check(saved && PlayerPrefs.GetInt("RockGame.AlienVoice", -1) == (was ? 1 : 0), "ALIEN VOICE is saved in the settings (RockGame.AlienVoice)");
            }
            Hud.OpenSettingsTab = 3;
            yield return new WaitForSeconds(0.6f);
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "voice_settings.png"), shot.EncodeToPNG());
            Destroy(shot);
            yield return new WaitForSeconds(0.3f);
            Log("voice test done");
            Application.Quit(0);
        }

        /// <summary>
        /// -autotest voicenet: a host (-host -port P) and a client (-client 127.0.0.1 -port P). Once the match starts the
        /// client talks (a 440 Hz tone through the real VoiceRpc) for 12 s; the host checks that the server passes nothing on
        /// while the client is over 60 m away, then teleports the client next to it and checks the packets arrive in order and
        /// on time (none lost or late, the jitter buffer playing with a small target delay).
        /// </summary>
        IEnumerator VoiceNetRoutine(PlayerNet me)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            float timeout = Time.time + 90f;
            while ((PlayerNet.All.Count < 2 || NetGame.Instance.S == GameState.Waiting) && Time.time < timeout) yield return null;
            PlayerNet other = null;
            foreach (var p in PlayerNet.All) if (p != me) other = p;
            if (other == null) { Log("FAIL: voicenet: the other player never came"); Application.Quit(2); yield break; }
            yield return new WaitForSeconds(1f);
            if (!nm.IsServer)
            {
                // the client: talk for 12 s, the way VoiceChat does (a frame every 20 ms, sent once a game frame)
                var enc = new VoiceCodec.State();
                var f = new float[VoiceCodec.FrameSamples];
                ushort seq = 0;
                double start = Time.realtimeSinceStartupAsDouble;
                while (Time.realtimeSinceStartupAsDouble - start < 12.0)
                {
                    int due = (int)((Time.realtimeSinceStartupAsDouble - start) / VoiceCodec.FrameSec);
                    while (seq < due)
                    {
                        for (int i = 0; i < f.Length; i++) f[i] = 0.3f * Mathf.Sin(2 * Mathf.PI * 440 * (seq * f.Length + i) / VoiceCodec.Rate);
                        me.VoiceRpc(VoiceCodec.Encode(f, seq++, ref enc));
                    }
                    yield return null;
                }
                me.VoiceRpc(VoiceCodec.EndPacket((ushort)(seq - 1)));
                Log($"voicenet client: sent {seq} voice packets");
                yield return new WaitForSeconds(3f);
                Log("voice net test done (client)");
                Application.Quit(0);
                yield break;
            }
            // the host: far apart first
            if (Vector3.Distance(me.transform.position, other.transform.position) < VoiceChat.RelayRange + 20f)
                other.TeleportRpc(me.transform.position + Vector3.right * (VoiceChat.RelayRange + 40f), 0f);
            yield return new WaitForSeconds(1f);
            int r0 = VoiceChat.BufferOf(other)?.Received ?? 0;
            yield return new WaitForSeconds(2.5f);
            int farGot = (VoiceChat.BufferOf(other)?.Received ?? 0) - r0;
            float d = Vector3.Distance(me.transform.position, other.transform.position);
            Check(farGot == 0 && PlayerNet.LastVoiceRelayTo == 0, $"voicenet: {d:0} m away the server passes the voice to nobody ({farGot} packets got here)");
            // then close by
            other.TeleportRpc(me.transform.position + me.transform.forward * 3f, 0f);
            yield return new WaitForSeconds(1f);
            var buf = VoiceChat.BufferOf(other);
            int got0 = buf?.Received ?? 0, lost0 = buf?.Lost ?? 0, late0 = buf?.Late ?? 0;
            float maxTarget = 0;
            bool played = false;
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - t0 < 4.0)
            {
                buf = VoiceChat.BufferOf(other);
                if (buf != null) { maxTarget = Mathf.Max(maxTarget, buf.TargetSec); played |= buf.Playing; }
                yield return null;
            }
            double secs = Time.realtimeSinceStartupAsDouble - t0;
            buf = VoiceChat.BufferOf(other);
            int got = (buf?.Received ?? 0) - got0;
            int expect = (int)(secs / VoiceCodec.FrameSec);
            Log($"voicenet host: {got} packets in {secs:0.0} s (expected ~{expect}), lost {(buf?.Lost ?? 0) - lost0}, late {(buf?.Late ?? 0) - late0}, underruns {buf?.Underruns ?? 0}, jitter buffer target up to {maxTarget * 1000:0} ms (at the end {buf?.TargetSec * 1000:0} ms, buffered {buf?.BufferedSec * 1000:0} ms, jitter {buf?.JitterSec * 1000:0} ms), host {1f / Time.smoothDeltaTime:0} fps, relayed to {PlayerNet.LastVoiceRelayTo}");
            Check(buf != null && got > expect * 0.9f && got < expect * 1.1f + 3, $"voicenet: close by, the voice arrives ({got} of ~{expect} packets)");
            Check(buf != null && (buf.Lost - lost0) + (buf.Late - late0) <= 2, "voicenet: nothing lost or late on localhost");
            Check(played && maxTarget > 0f && maxTarget < 0.15f, $"voicenet: playing with a {maxTarget * 1000:0} ms jitter buffer target");
            yield return new WaitForSeconds(4f);
            Log("voice net test done (host)");
            Application.Quit(0);
        }

        /// <summary>Notes when its audio block first has sound in it (audio thread).</summary>
        class VoiceProbe : MonoBehaviour
        {
            public volatile bool Heard;
            public long HeardAt;
            void OnAudioFilterRead(float[] data, int channels)
            {
                if (Heard) return;
                foreach (var v in data) if (Math.Abs(v) > 1e-4f) { HeardAt = System.Diagnostics.Stopwatch.GetTimestamp(); Heard = true; return; }
            }
        }

        IEnumerator RealAudioPath()
        {
            AudioSettings.GetDSPBufferSize(out int dspLen, out int dspN);
            Log($"audio: {AudioSettings.outputSampleRate} Hz, DSP buffer {dspLen} x {dspN}, driver caps {AudioSettings.driverCapabilities}");
            float listenerWas = AudioListener.volume;
            AudioListener.volume = 0.0005f; // (nearly silent, but the mixer runs)
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            var tone = new float[VoiceCodec.FrameSamples];
            double ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            // the new playback: carrier AudioSource + VoiceTap filter
            float newMs = -1, target = 0;
            {
                var buf = new VoiceJitterBuffer();
                var tap = VoiceChat.MakeSource(transform, false, buf, out var src);
                var probe = tap.gameObject.AddComponent<VoiceProbe>();
                yield return new WaitForSeconds(0.5f);
                var enc = new VoiceCodec.State();
                ushort seq = 0;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                double start = Time.realtimeSinceStartupAsDouble;
                while (!probe.Heard && Time.realtimeSinceStartupAsDouble - start < 2.0)
                {
                    // frames come in as a talker's would: one every 20 ms, handed over once a game frame
                    int due = (int)((Time.realtimeSinceStartupAsDouble - start) / VoiceCodec.FrameSec) + 1;
                    while (seq < due)
                    {
                        for (int i = 0; i < tone.Length; i++) tone[i] = 0.3f * Mathf.Sin(2 * Mathf.PI * 440 * (seq * tone.Length + i) / VoiceCodec.Rate);
                        buf.Push(VoiceCodec.Encode(tone, seq++, ref enc), Time.realtimeSinceStartupAsDouble);
                    }
                    yield return null;
                }
                if (probe.Heard) newMs = (float)ms(probe.HeardAt - t0);
                target = buf.TargetSec;
                Destroy(src.gameObject);
            }

            // the old playback: an 8 kHz streaming clip read through PCMReaderCallback, 200 ms pre-buffer
            float oldMs = -1;
            {
                var ring = new float[16000];
                int rW = 0, rR = 0, rC = 0; bool playing = false;
                object lk = new object();
                var go = new GameObject("old voice");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.spatialBlend = 0f; src.loop = true;
                src.clip = AudioClip.Create("oldvoice", 8000, 1, 8000, true, data =>
                {
                    lock (lk)
                    {
                        if (!playing && rC >= 1600) playing = true;
                        for (int i = 0; i < data.Length; i++)
                        {
                            if (playing && rC > 0) { data[i] = ring[rR]; rR = (rR + 1) % ring.Length; rC--; }
                            else { data[i] = 0; playing = false; }
                        }
                    }
                });
                src.Play();
                var probe = go.AddComponent<VoiceProbe>();
                yield return new WaitForSeconds(0.8f);
                probe.Heard = false;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                double start = Time.realtimeSinceStartupAsDouble;
                int sent = 0;
                while (!probe.Heard && Time.realtimeSinceStartupAsDouble - start < 3.0)
                {
                    int due = (int)((Time.realtimeSinceStartupAsDouble - start) / 0.1) + 1;
                    while (sent < due)
                    {
                        lock (lk)
                            for (int i = 0; i < 800; i++)
                            {
                                ring[rW] = OldMuLawDecode(OldMuLawEncode(0.3f * Mathf.Sin(2 * Mathf.PI * 440 * (sent * 800 + i) / 8000f)));
                                rW = (rW + 1) % ring.Length; rC++;
                            }
                        sent++;
                    }
                    yield return null;
                }
                if (probe.Heard) oldMs = (float)ms(probe.HeardAt - t0);
                Destroy(go);
            }
            AudioListener.volume = listenerWas;
            if (newMs < 0 && oldMs < 0) { Log("SKIP: no audio output here (the mixer never ran), real audio path not measured"); yield break; }
            Log($"real Unity audio path, first packet in -> sound at the mixer: before {oldMs:0} ms (100 ms chunks, 200 ms pre-buffer, streaming clip), after {newMs:0} ms (jitter buffer target {target * 1000:0} ms)");
            Check(newMs >= 0 && (oldMs < 0 || newMs < oldMs * 0.6f) && newMs < 150f, $"real audio path delay {oldMs:0} -> {newMs:0} ms");
        }
    }
}
