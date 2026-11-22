// .wav files for the SOUND STUDIO (native audio.cpp parseWav and its resampling): 8, 16, 24 and 32-bit PCM and 32-bit
// float, any rate from 8 to 192 kHz, WAVE_FORMAT_EXTENSIBLE too; more than two channels mixed down to one.
using System;

namespace ProjectArena
{
    public static class WavReader
    {
        public static bool Parse(byte[] d, out float[] o, out int channels, out int rate)
        {
            o = null; channels = 0; rate = 0;
            int n = d?.Length ?? 0;
            if (n < 44 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F' || d[8] != 'W' || d[9] != 'A' || d[10] != 'V' || d[11] != 'E') return false;
            int p = 12, bits = 16, tag = 1;
            while (p + 8 <= n)
            {
                uint sz = BitConverter.ToUInt32(d, p + 4);
                bool isFmt = d[p] == 'f' && d[p + 1] == 'm' && d[p + 2] == 't' && d[p + 3] == ' ';
                bool isData = d[p] == 'd' && d[p + 1] == 'a' && d[p + 2] == 't' && d[p + 3] == 'a';
                if (isFmt && p + 8 + 16 <= n)
                {
                    int fmt = BitConverter.ToUInt16(d, p + 8);
                    channels = BitConverter.ToUInt16(d, p + 10);
                    rate = (int)BitConverter.ToUInt32(d, p + 12);
                    bits = BitConverter.ToUInt16(d, p + 22);
                    if (fmt == 0xFFFE && sz >= 26 && p + 8 + 26 <= n) fmt = BitConverter.ToUInt16(d, p + 8 + 24);   // (WAVE_FORMAT_EXTENSIBLE)
                    tag = fmt;
                }
                else if (isData)
                {
                    if (channels < 1 || channels > 8 || rate < 8000 || rate > 192000) return false;
                    int bytes = (int)Math.Min(sz, (uint)(n - p - 8)), w = bits / 8;
                    if (!(tag == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(tag == 3 && bits == 32)) return false;
                    int s = p + 8, cnt = bytes / w;
                    o = new float[cnt];
                    for (int i = 0; i < cnt; ++i)
                    {
                        int q = s + i * w;
                        if (tag == 3) { float f = BitConverter.ToSingle(d, q); o[i] = float.IsFinite(f) ? Math.Max(-1f, Math.Min(1f, f)) : 0f; }
                        else if (bits == 8) o[i] = (d[q] - 128) / 128f;
                        else if (bits == 16) o[i] = BitConverter.ToInt16(d, q) / 32768f;
                        else if (bits == 24) o[i] = ((d[q] << 8 | d[q + 1] << 16 | d[q + 2] << 24) >> 8) / 8388608f;
                        else o[i] = (float)(BitConverter.ToInt32(d, q) / 2147483648.0);
                    }
                    if (channels > 2)
                    {   // (mixed down)
                        int frames = cnt / channels;
                        var m = new float[frames];
                        for (int f = 0; f < frames; ++f)
                        {
                            float sum = 0;
                            for (int c = 0; c < channels; ++c) sum += o[f * channels + c];
                            m[f] = sum / channels;
                        }
                        o = m;
                        channels = 1;
                    }
                    return true;
                }
                p += 8 + (int)sz + (int)(sz & 1);
            }
            return false;
        }

        // linear, to the mixer's rate (native resampleTo48k)
        public static float[] Resample(float[] s, int channels, int rate, int to)
        {
            if (rate == to || s.Length == 0) return s;
            int frames = s.Length / channels, outFrames = (int)((double)frames * to / rate);
            var o = new float[outFrames * channels];
            for (int i = 0; i < outFrames; ++i)
            {
                double src = (double)i * rate / to;
                int i0 = Math.Min(frames - 1, (int)src), i1 = Math.Min(frames - 1, i0 + 1);
                float t = (float)(src - i0);
                for (int ch = 0; ch < channels; ++ch) o[i * channels + ch] = s[i0 * channels + ch] * (1 - t) + s[i1 * channels + ch] * t;
            }
            return o;
        }
    }
}
