using System.Collections.Generic;
using UnityEngine;

namespace ProjectArena
{
    // The native game's mixer (audio.cpp Audio::play / mix), so sounds mix as natively: 48 kHz clips, equal-power pan
    // (left = gain * sqrt((1 - pan) / 2), right = gain * sqrt((1 + pan) / 2)), at most 24 voices (the oldest one-shot
    // makes room), the master volume (native default 8 / 10), clipped to +-1. Mixed into Unity's output on the audio
    // thread (OnAudioFilterRead on the AudioListener's object). The clips come from Unity's import of the open WAVs
    // (decompressed on load, sample rate kept: OpenAudioPostprocessor); events.txt maps game events to clips.
    public class NativeMixer : MonoBehaviour
    {
        public float volume = 0.8f;

        class Clip { public float[] samples; public int channels; }
        struct Voice { public Clip clip; public int pos; public float gainL, gainR; public bool loop, duck; }
        // CROWD UNDER VOICES (native 0.3.19, owner 2026-11-10: the crowd drowned a conversation): while anyone else's voice
        // plays, the crowd sounds (duck: the ambience, The Crowd's, the round and match ends) go down to KDuck, quickly, and
        // come back smoothly once nobody has spoken for KDuckHold
        public const float KDuck = 0.32f, KDuckHold = 0.6f, KDuckAttack = 0.08f, KDuckRelease = 0.5f;
        float duckLevel = 1f, duckHold;   // (the audio thread's own)
        // one mixing block of secs: the crowd's level after it (talk: a voice playing as the block starts)
        public static float DuckStep(float level, ref float hold, bool talk, float secs)
        {
            hold = talk ? KDuckHold : Mathf.Max(0f, hold - secs);
            float want = hold > 0f ? KDuck : 1f;
            float tau = want < level ? KDuckAttack : KDuckRelease;
            return level + (want - level) * (1f - Mathf.Exp(-secs / tau));
        }

        readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
        readonly Dictionary<string, string> eventMap = new Dictionary<string, string>();
        readonly List<Voice> voices = new List<Voice>();
        readonly object mu = new object();
        int outputRate;
        // voice chat (native Audio::pushVoice): per speaker a queue of 16 kHz samples, started once 80 ms have arrived,
        // read with linear interpolation at the output rate; more than 0.5 s behind drops the backlog
        class VoiceQ { public readonly Queue<short> q = new Queue<short>(); public bool playing; public float frac, gainL, gainR; public int idle; }
        readonly Dictionary<uint, VoiceQ> vq = new Dictionary<uint, VoiceQ>();
        readonly List<uint> goneVoices = new List<uint>();
        // BALL IN FLIGHT (TASKS.md E52, native bee88ee Audio::setFlight, 0.3.24; FlightSound.cs): up to KFlights balls that
        // can hurt you, heard as they fly; the game sets each one every frame (a gain of 0: silent). The level: SETTINGS >
        // VOICE AND MUTES > BALL SOUND (0 off). Mixed after the sounds, before the voices (not under voices: you need it)
        public const int KFlights = 4;
        readonly FlightSynth[] flights = { new FlightSynth(), new FlightSynth(), new FlightSynth(), new FlightSynth() };
        float flightLevel = 1f;
        float[] flightBuf = new float[0];
        int flightAge;   // (audio frames since the game last set them: native 3fccaf9 silences them when no frames come)
        public void SetFlight(int slot, FlightParams f)
        {
            if (slot < 0 || slot >= KFlights) return;
            lock (mu) { flights[slot].want = f; flightAge = 0; }
        }
        // BALL IN FLIGHT: no frames, no whoosh (paused, the headset's menu, the session not running: native
        // Audio::silenceFlights). Called on pause and focus loss; and a quarter second without SetFlight does the same
        public void SilenceFlights() { lock (mu) for (int k = 0; k < KFlights; ++k) flights[k].want = FlightParams.Silent; }
        public void SetFlightLevel(float l) { lock (mu) flightLevel = Mathf.Clamp01(l); }

        // MUSIC (TASKS.md E60, native 1159a46 Audio::loadMusic / setMusicLevel, 0.3.24): a background track, looped at the
        // level the game sets each frame and gliding to it over about a second, under voices as the crowd is; 0 silent. The
        // track is the clip named MusicClip among the sounds; the template ships none (native's is CrossGenGaming's own
        // and never public), so it is silent until one is supplied. The level: SETTINGS' MUSIC row (MusicLevel)
        public const string MusicClip = "sc_LobbyBackground";
        public const float MusicFull = 0.11f;   // at MUSIC 100% (30%, the start: about 44 dB under full scale, as native)
        Clip music;
        int musicPos;
        float musicWant, musicGain;   // (musicGain: the audio thread's own)
        public bool HasMusic => music != null;
        public void SetMusicLevel(float l) { lock (mu) musicWant = Mathf.Clamp01(l); }
        // native stepFrame's: in the lobby as MENU says, elsewhere (playing, watching, training) as GAME says; never in the
        // SOUND STUDIO (its sounds are what you listen to there)
        public static float MusicLevel(bool inLobby, bool studio, bool menuOn, bool gameOn, int volume) =>
            (inLobby ? menuOn : gameOn) && !studio ? MusicFull * Mathf.Clamp(volume, 0, 10) / 10f : 0f;
        // one output frame's glide toward the wanted level (native: 0.00005 a frame at 48 kHz, about a second)
        public static float MusicGlide(float g, float want, int rate) => g + (want - g) * (0.00005f * 48000f / Mathf.Max(1, rate));

        public void PushVoice(uint speaker, short[] pcm, float gain, float pan)
        {
            lock (mu)
            {
                if (!vq.TryGetValue(speaker, out var v)) vq[speaker] = v = new VoiceQ();
                if (v.q.Count > 16000 / 2) v.q.Clear();
                foreach (short x in pcm) v.q.Enqueue(x);
                v.gainL = gain * Mathf.Sqrt(0.5f * (1 - pan));
                v.gainR = gain * Mathf.Sqrt(0.5f * (1 + pan));
            }
        }

        // a looping sound at this gain (0: stopped; native Audio::setLoop): The Crowd's murmur
        public void SetLoop(string name, float gain, bool duck = false)
        {
            if (!clips.TryGetValue(name, out var c)) return;
            float g = Mathf.Max(0f, gain) * 0.7071f;
            lock (mu)
            {
                int i = voices.FindIndex(v => v.clip == c && v.loop);
                if (i >= 0)
                {
                    if (g <= 0.0005f) voices.RemoveAt(i);
                    else { var v = voices[i]; v.gainL = v.gainR = g; voices[i] = v; }
                    return;
                }
                if (g > 0.0005f) voices.Add(new Voice { clip = c, pos = 0, gainL = g, gainR = g, loop = true, duck = duck });
            }
        }

        // eventsTxt: audio/events.txt ("<Event> <clip>" per line); sounds: the open clips by name
        public void Init(string eventsTxt, IEnumerable<AudioClip> sounds)
        {
            outputRate = AudioSettings.outputSampleRate;
            foreach (var line in eventsTxt.Split('\n'))
            {
                string l = line.Trim();
                int sp = l.IndexOf(' ');
                if (sp > 0 && l[0] != '#') eventMap[l.Substring(0, sp)] = l.Substring(sp + 1).Trim();   // (# : a note)
            }
            foreach (var c in sounds) if (c != null) clips[c.name] = Load(c);
            clips.TryGetValue(MusicClip, out music);   // (MUSIC: none in the template's sounds)
            // the arena ambience (S27), on a loop under everything, under voices: 0.18, half its old 0.35 (native 0.3.19,
            // owner 2026-11-12: "even quieter")
            if (eventMap.TryGetValue("Ambience", out var amb)) Play(amb, 0.18f, 0, true, true);
            Debug.Log($"[Audio] {clips.Count} clips, {eventMap.Count} events, output {outputRate} Hz");
        }

        // the clip at the mixer rate (native: linear resampling when a clip's rate differs; the open set is 48 kHz)
        Clip Load(AudioClip c)
        {
            var s = new float[c.samples * c.channels];
            c.GetData(s, 0);
            int rate = c.frequency;
            if (rate == outputRate) return new Clip { samples = s, channels = c.channels };
            int frames = c.samples, outFrames = (int)((double)frames * outputRate / rate);
            var o = new float[outFrames * c.channels];
            for (int i = 0; i < outFrames; ++i)
            {
                double src = (double)i * rate / outputRate;
                int i0 = System.Math.Min(frames - 1, (int)src), i1 = System.Math.Min(frames - 1, i0 + 1);
                float t = (float)(src - i0);
                for (int ch = 0; ch < c.channels; ++ch) o[i * c.channels + ch] = s[i0 * c.channels + ch] * (1 - t) + s[i1 * c.channels + ch] * t;
            }
            return new Clip { samples = o, channels = c.channels };
        }

        public bool Has(string key) => eventMap.ContainsKey(key);

        // a game event's sound by its events.txt key (unknown keys: silence); the SOUND STUDIO's choice for it first
        public void PlayEvent(string key, float gain, float pan, bool duck = false)
        {
            string clip;
            lock (overrides) if (overrides.TryGetValue(key, out clip) && clips.ContainsKey(clip)) { Play(clip, gain, pan, false, duck); return; }
            if (eventMap.TryGetValue(key, out clip)) Play(clip, gain, pan, false, duck);
        }

        // SOUND STUDIO (native 0.3.16 Audio::setOverride / loadFile / officialClip): an event's sound swapped for another
        // clip, studio only (ClearOverrides when it closes); a clip read from a .wav file (the library's, or MINE)
        readonly Dictionary<string, string> overrides = new Dictionary<string, string>();
        public void SetOverride(string key, string clip) { lock (overrides) { if (string.IsNullOrEmpty(clip)) overrides.Remove(key); else overrides[key] = clip; } }
        public void ClearOverrides() { lock (overrides) overrides.Clear(); }
        public string OverrideOf(string key) { lock (overrides) return overrides.TryGetValue(key, out var c) ? c : ""; }
        public string OfficialClip(string key) => eventMap.TryGetValue(key, out var c) ? c : "";
        public bool HasClip(string name) => clips.ContainsKey(name);
        public bool LoadFile(string clip, string path)
        {
            try
            {
                var f = new System.IO.FileInfo(path);
                if (!f.Exists || f.Length > 24L * 1024 * 1024) return false;
                if (!WavReader.Parse(System.IO.File.ReadAllBytes(path), out float[] s, out int ch, out int rate) || s.Length == 0) return false;
                int outRate = outputRate > 0 ? outputRate : 48000;
                var c = new Clip { samples = WavReader.Resample(s, ch, rate, outRate), channels = ch };
                lock (mu) clips[clip] = c;
                return true;
            }
            catch (System.Exception) { return false; }
        }

        public void Play(string name, float gain, float pan, bool loop = false, bool duck = false)
        {
            if (!clips.TryGetValue(name, out var c)) { Debug.LogWarning("[Audio] clip missing: " + name); return; }
            float l = gain * Mathf.Sqrt(0.5f * (1 - pan)), r = gain * Mathf.Sqrt(0.5f * (1 + pan));
            lock (mu)
            {
                if (voices.Count > 24)
                {   // the oldest one-shot makes room (a loop keeps playing)
                    int k = voices.FindIndex(v => !v.loop);
                    voices.RemoveAt(k >= 0 ? k : 0);
                }
                voices.Add(new Voice { clip = c, pos = 0, gainL = l, gainR = r, loop = loop, duck = duck });
            }
        }

        static short PeekSecond(Queue<short> q)
        {
            using (var e = q.GetEnumerator()) { e.MoveNext(); return e.MoveNext() ? e.Current : (short)0; }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            lock (mu)
            {
                {   // CROWD UNDER VOICES: from the voices playing as this block starts
                    bool talk = false;
                    foreach (var kv in vq) talk |= kv.Value.playing;
                    duckLevel = DuckStep(duckLevel, ref duckHold, talk, frames / (float)(outputRate > 0 ? outputRate : 48000));
                }
                for (int i = 0; i < voices.Count; ++i)
                {
                    Voice v = voices[i];
                    float dk = v.duck ? duckLevel : 1f;
                    int frameCount = v.clip.samples.Length / v.clip.channels;
                    for (int f = 0; f < frames; ++f)
                    {
                        if (v.pos >= frameCount) { if (v.loop) v.pos = 0; else break; }
                        float l, r;
                        if (v.clip.channels == 2) { l = v.clip.samples[v.pos * 2]; r = v.clip.samples[v.pos * 2 + 1]; }
                        else l = r = v.clip.samples[v.pos];
                        data[f * channels] += l * v.gainL * dk * volume;
                        if (channels > 1) data[f * channels + 1] += r * v.gainR * dk * volume;
                        ++v.pos;
                    }
                    voices[i] = v;
                }
                voices.RemoveAll(v => !v.loop && v.pos >= v.clip.samples.Length / v.clip.channels);
                if (music != null && (musicWant > 0f || musicGain > 0f))
                {   // MUSIC: looped, gliding to its level, under voices
                    int sr = outputRate > 0 ? outputRate : 48000;
                    int n = music.samples.Length / music.channels;
                    for (int f = 0; f < frames && n > 0; ++f)
                    {
                        musicGain = MusicGlide(musicGain, musicWant, sr);
                        if (musicWant <= 0f && musicGain < 1e-4f) { musicGain = 0f; break; }
                        if (musicPos >= n) musicPos = 0;
                        float l, r;
                        if (music.channels == 2) { l = music.samples[musicPos * 2]; r = music.samples[musicPos * 2 + 1]; }
                        else l = r = music.samples[musicPos];
                        ++musicPos;
                        float k = musicGain * duckLevel * volume;
                        data[f * channels] += l * k;
                        if (channels > 1) data[f * channels + 1] += r * k;
                    }
                }
                {   // BALL IN FLIGHT: the balls coming at you, as the game last set them
                    if (flightBuf.Length < frames * 2) flightBuf = new float[frames * 2];
                    System.Array.Clear(flightBuf, 0, frames * 2);
                    int sr = outputRate > 0 ? outputRate : 48000;
                    flightAge += frames;
                    if (flightAge > sr / 4) foreach (FlightSynth fs in flights) fs.want = FlightParams.Silent;   // (the game stopped setting them)
                    foreach (FlightSynth fs in flights) fs.Render(flightBuf, frames, flightLevel, sr);
                    for (int f = 0; f < frames; ++f)
                    {
                        data[f * channels] += flightBuf[2 * f] * volume;
                        if (channels > 1) data[f * channels + 1] += flightBuf[2 * f + 1] * volume;
                    }
                }
                float step = 16000f / Mathf.Max(1, outputRate);
                goneVoices.Clear();
                foreach (var kv in vq)
                {
                    VoiceQ v = kv.Value;
                    if (!v.playing && v.q.Count >= 1280) v.playing = true;
                    if (v.playing)
                    {
                        for (int f = 0; f < frames; ++f)
                        {
                            if (v.q.Count < 2) { v.playing = false; break; }
                            short a0 = v.q.Peek();
                            short a1 = PeekSecond(v.q);
                            float smp = (a0 * (1f - v.frac) + a1 * v.frac) * (1f / 32768f);
                            data[f * channels] += smp * v.gainL * volume;
                            if (channels > 1) data[f * channels + 1] += smp * v.gainR * volume;
                            v.frac += step;
                            while (v.frac >= 1f && v.q.Count > 0) { v.q.Dequeue(); v.frac -= 1f; }
                        }
                        v.idle = 0;
                    }
                    else if (++v.idle > 2000) goneVoices.Add(kv.Key);   // ~20 s of silence: forget the speaker
                }
                foreach (uint k in goneVoices) vq.Remove(k);
            }
            for (int i = 0; i < data.Length; ++i) data[i] = Mathf.Clamp(data[i], -1f, 1f);
        }
    }
}
