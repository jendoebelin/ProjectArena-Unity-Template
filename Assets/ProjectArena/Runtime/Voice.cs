using System;
using System.Collections.Generic;
using ProjectArena.Online;
using ProjectArena.Sim;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // Voice chat (native updateVoice, speakerPos, drawVoiceHud): the microphone runs only inside an arena room (or at an
    // arena as a spectator) with voice on and the permission given; 40 ms frames of 16 kHz mu-law are sent while push to
    // talk (the left grip) is held, or always with open mic above a noise gate, with an automatic gain. Incoming voice
    // is muted by role (MUTE SPECTATORS / OTHER PLAYERS / ALL) or by person (REPORT PLAYER > MUTE), and panned toward
    // the speaker through the NativeMixer.
    public class Voice
    {
        readonly ArenaSession s;
        AppState a => s.a;
        Online.Net net => s.net;
        public NativeMixer mixer;
        AudioClip mic;
        string micDevice;
        int micPos;
        bool micAsked, micDenied, micFailed;
        volatile bool micAnswered;   // (the prompt's answer came: its callback, on Android's thread)
        double micCheckAt;
        bool micGranted;      // (as last checked, a few times a second in a voice zone)
        double micAskAt;      // the headset's microphone prompt comes then (a heads-up shows till then; 0: none due)
        public bool MicAskDue => micAskAt > 0;
        double voiceZoneAt;   // when we came where voice works (the push-to-talk reminder shows for a while)
        readonly List<short> micBuf = new List<short>();
        float micGain = 1f;
        public bool talking;
        public bool ptt;                       // push to talk held (TALK's button: the left grip as standard, BUTTONS)
        readonly Dictionary<uint, double> spokeAt = new Dictionary<uint, double>();
        int sent, recv;
        double logAt;
        float[] tmp = new float[4096];

        public Voice(ArenaSession s) => this.s = s;
        public bool Capturing => mic != null;
        public bool MicDenied => micDenied;

        bool Permission(bool ask)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) return true;
            if (ask) UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            return false;
#else
            return Microphone.devices.Length > 0;
#endif
        }

        void AskMic()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => micAnswered = true;
            cb.PermissionDenied += _ => micAnswered = true;
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
#else
            micAnswered = true;   // (no prompt: the devices are the answer)
#endif
        }

        void StartCapture()
        {
            micDevice = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
            mic = Microphone.Start(micDevice, true, 1, 16000);
            micPos = 0;
            if (mic == null) { micDenied = micFailed = true; Debug.LogWarning("[Voice] the microphone did not start"); }
            else Debug.Log($"[Voice] microphone on ({mic.frequency} Hz)");
        }

        void StopCapture()
        {
            Microphone.End(micDevice);
            mic = null;
            micBuf.Clear();
            Debug.Log("[Voice] microphone off");
        }

        // where a speaker is (for panning and the talking indicator): tank players, waiting players, spectators
        bool SpeakerPos(uint id, out Vec3 o)
        {
            o = default;
            RoomInfo rm = net.InRoom ? net.Room : (a.spectating && a.watchIp != 0) ? a.watchRoom : null;
            if (rm == null) return false;
            for (int p = 0; p < 2; ++p) if (rm.slot[p] == id) { o = s.viewIn[p].head.p; return true; }
            foreach (var m in rm.members) if (m.id == id) { o = m.@in.head.p; return true; }
            foreach (var w in rm.watchers) if (w.id == id) { o = w.pos; return true; }
            return false;
        }

        public void Update()
        {
            var st = a.settings;
            bool zone = net.VoiceZone && st.voiceMode != 2;
            double tNow = Time.realtimeSinceStartupAsDouble;
            if (zone && mic == null && !micFailed)
            {   // the permission: looked at again and again (a Quest 3, 2026-11-14: the next frame took the open prompt for a
                // no, so ALLOW never started the mic; a no is looked at every 2 s: allowed later in the headset's settings, the
                // mic starts). Asked only when you first go to talk in a voice zone (TALK held, or OPEN MIC), once a
                // session, after a heads-up by your hand and in the banner (TASKS.md E27, native 665f18c: the headset's
                // prompt puts the game in the background, a grey space with the prompt coming later; asked on entering a
                // room, it looked like a crash)
                if (tNow >= micCheckAt)
                {
                    micCheckAt = tNow + (micDenied ? 2.0 : 0.25);
                    micGranted = Permission(false);
                    if (micGranted) { micDenied = false; StartCapture(); }
                    else if (micAsked && micAnswered) micDenied = true;
                }
                if (!micGranted && !micAsked && micAskAt == 0 && micCheckAt > 0 && (ptt || st.voiceMode == 1))
                {
                    micAskAt = tNow + 3.0;
                    net.NoticeLocal("VOICE NEEDS THE MIC: THE HEADSET WILL ASK");
                    Debug.Log("[Voice] the microphone asked for in 3 s (the heads-up shown)");
                }
            }
            if (micAskAt > 0 && tNow >= micAskAt)
            {
                micAskAt = 0;
                if (zone)
                {   // (left the room meanwhile: asked the next time you go to talk)
                    micAsked = true;
                    AskMic();   // (the headset's prompt, unless already given)
                    micCheckAt = tNow + 1.0;
                }
            }
            if (!zone && mic != null) StopCapture();
            talking = false;
            if (mic != null)
            {
                int pos = Microphone.GetPosition(micDevice);
                int n = (pos - micPos + mic.samples) % mic.samples;
                if (n > 0)
                {
                    if (tmp.Length < n) tmp = new float[n];
                    var chunk = new float[n];
                    mic.GetData(chunk, micPos);   // (wraps round the 1 s ring)
                    foreach (float f in chunk) micBuf.Add((short)Mathf.Clamp(f * 32767f, -32768f, 32767f));
                    micPos = pos;
                }
                bool keyed = st.voiceMode == 1 || ptt;
                int N = Online.Net.kVoiceSamples;
                while (micBuf.Count >= N)
                {
                    double e = 0;
                    for (int i = 0; i < N; ++i) e += (double)micBuf[i] * micBuf[i];
                    float rms = (float)Math.Sqrt(e / N);
                    bool loud = rms > 350f;
                    // automatic gain: bring speech up to a steady level (up to 6x), never boost the background hiss
                    float want = rms > 150f ? Math.Min(6f, Math.Max(1f, 3000f / rms)) : 1f;
                    micGain += (want - micGain) * (want < micGain ? 0.5f : 0.15f);
                    if (keyed && (st.voiceMode == 0 || loud))
                    {
                        var u = new byte[N];
                        for (int i = 0; i < N; ++i) u[i] = VoiceCodec.Encode((short)Mathf.Clamp(micBuf[i] * micGain, -30000f, 30000f));
                        net.SendVoice(u);
                        talking = true;
                        ++sent;
                    }
                    micBuf.RemoveRange(0, N);
                }
                if (keyed && st.voiceMode == 0) talking = true;
            }
            else micBuf.Clear();
            // incoming voice: mutes by role, panned toward the speaker
            double t = Time.realtimeSinceStartupAsDouble;
            a.mutes.Refresh(net, t);   // (MUTED PLAYERS: who here a kept mute covers, TASKS.md E46)
            Vec3 f3 = a.viewHead.q.Rotate(new Vec3(0, 0, -1)), right = M.Normalize(M.Cross(f3, new Vec3(0, 1, 0)));
            foreach (var v in net.VoiceInbox)
            {
                if (st.muteAll || (v.role == Online.Net.VOICE_SPECTATOR && st.muteSpectators) || (v.role != Online.Net.VOICE_SPECTATOR && st.mutePlayers)) continue;
                if (a.IsMuted(v.speaker)) continue;   // REPORT PLAYER > MUTE
                var pcm = new short[Online.Net.kVoiceSamples];
                for (int i = 0; i < pcm.Length; ++i) pcm[i] = VoiceCodec.Decode(v.data[i]);
                float gain = 1f, pan = 0f;
                if (SpeakerPos(v.speaker, out Vec3 p))
                {
                    Vec3 d = p - a.viewHead.p;
                    float dist = M.Length(d);
                    gain = M.Clampf(2.5f / (1f + dist), 0.4f, 1.2f);
                    pan = dist > 0.2f ? M.Clampf(M.Dot(right, d / dist), -0.8f, 0.8f) : 0f;
                }
                mixer?.PushVoice(v.speaker, pcm, gain * 1.4f, pan);
                spokeAt[v.speaker] = t;
                ++recv;
            }
            if (t - logAt > 10.0)
            {   // what voice actually did, for testing
                if (sent != 0 || recv != 0)
                    Debug.Log($"[Voice] sent {sent} frames, received {recv} (mic gain {micGain:F1}x, {(st.voiceMode == 0 ? "push to talk" : st.voiceMode == 1 ? "open mic" : "off")}; mic {(mic != null ? "on" : "off")})");
                sent = recv = 0;
                logAt = t;
            }
            net.VoiceInbox.Clear();
        }

        // near the left hand: the push-to-talk hint, TALKING, or the mic's state; small white lights over people talking
        public void DrawHud(FxRenderer fx, bool menuOpen)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!net.VoiceZone) { voiceZoneAt = 0; return; }
            if (voiceZoneAt == 0) voiceZoneAt = now;
            var st = a.settings;
            var grey = new Vector4(0.6f, 0.65f, 0.72f, 1); var red = new Vector4(1.0f, 0.3f, 0.25f, 1); var white = new Vector4(1, 1, 1, 1);
            PlayerInput @in = a.localIn;
            // (native 5e6d0ba, owner 2026-11-02: the hint stayed by your hand all the time, spectating too, and got in the
            // way. TALKING and a microphone problem always show; the reminder and the mic's steady state only for the
            // first 10 s in a room, and spectators only see TALKING)
            bool spectator = net.CurrentMode == Online.Net.Mode.Spectate;
            bool intro = !spectator && now - voiceZoneAt < 10.0;
            string txt = talking ? (st.voiceMode == 1 ? "OPEN MIC - TALKING" : "TALKING")
                       : spectator ? null
                       : micAskAt > 0 ? "THE HEADSET WILL ASK FOR THE MIC"
                       : micDenied && st.voiceMode != 2 ? "MIC NOT ALLOWED"
                       : !intro ? null
                       : st.voiceMode == 2 ? "VOICE OFF" : st.muteAll ? "ALL MUTED" : st.voiceMode == 1 ? "OPEN MIC"
                       : Controls.TalkHint(a.Buttons, a.ctlKind);   // (HOLD LEFT GRIP TO TALK as standard: TALK's button, BUTTONS)
            int hh = Controls.TalkHand(a.Buttons, a.ctlKind, st.voiceMode);   // (by the hand that talks)
            if (txt != null && @in.hand[hh].tracked && !menuOpen)
            {
                Vec3 at = @in.hand[hh].grip.p + new Vec3(0, 0.12f, 0);
                Vec3 to = a.viewHead.p - at;
                to.y = 0;
                to = M.Normalize(to);
                fx.Text(txt, new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(to.x, to.z)), at), 0.022f, talking ? red : grey);
            }
            foreach (var kv in spokeAt)
            {
                if (now - kv.Value > 0.3) continue;
                if (SpeakerPos(kv.Key, out Vec3 p)) fx.Sprite(p + new Vec3(0, 0.25f, 0), 0.06f, white, a.viewHead.p, 0);
            }
        }
    }
}
