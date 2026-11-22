// BALL IN FLIGHT (TASKS.md E52, native bee88ee / edd1b41 test_game "[BALL IN FLIGHT: hear it coming]", 0.3.24;
// Runtime/FlightSound.cs): how a ball sounds as it comes at you, and the sound itself; SETTINGS > VOICE AND MUTES >
// BALL SOUND kept in settings.cfg
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public class FlightSoundTests
    {
        [Test]
        public void HearItComing()
        {
            Vec3 head = new Vec3(0, 1.6f, 0), right = new Vec3(1, 0, 0);   // (facing -Z)
            FlightParams far = FlightParams.Of(new Vec3(0, 1.5f, -12), new Vec3(0, 0, 11), head, right),
                         mid = FlightParams.Of(new Vec3(0, 1.5f, -6), new Vec3(0, 0, 11), head, right),
                         close = FlightParams.Of(new Vec3(0, 1.5f, -1.5f), new Vec3(0, 0, 11), head, right);
            Assert.That(far.gain > 0.1f && mid.gain > far.gain * 1.5f && close.gain > mid.gain * 1.5f && close.bright > mid.bright && mid.bright > far.bright,
                        "a ball coming at you: louder and brighter the nearer it is (heard from across the court)");
            FlightParams past = FlightParams.Of(new Vec3(-2f, 1.5f, -6), new Vec3(0, 0, 11), head, right);
            Assert.That(past.gain < mid.gain * 0.6f && past.pan < -0.2f && mid.pan > -0.05f && mid.pan < 0.05f,
                        "one that will pass 2 m to your left: quieter than one at you, and on your left");
            FlightParams away = FlightParams.Of(new Vec3(0, 1.5f, -6), new Vec3(0, 0, -11), head, right),
                         slow = FlightParams.Of(new Vec3(0, 1.5f, -6), new Vec3(0, 0, 4), head, right);
            Assert.That(away.gain < 0.01f && slow.pitch < mid.pitch && mid.pitch > 1.3f, "going away: silent; the hum rises with how fast it comes at you");
            var syn = new FlightSynth { want = close };
            var buf = new float[48000 / 10 * 2];
            syn.Render(buf, 48000 / 10, 1f);
            float peak = 0, rms = 0;
            bool finite = true;
            foreach (float x in buf) { peak = Math.Max(peak, Math.Abs(x)); rms += x * x; finite &= float.IsFinite(x); }
            rms = MathF.Sqrt(rms / buf.Length);
            var off = new FlightSynth();
            var quiet = new float[48000 / 10 * 2];
            off.Render(quiet, 48000 / 10, 0f);
            float q = quiet.Max(x => Math.Abs(x));
            Assert.That(finite && rms > 0.02f && peak < 0.95f && q == 0f,
                        $"the sound: heard, never past full scale (peak {peak:F2}, loudness {rms:F3}); BALL SOUND off: silent");
        }

        [Test]
        public void BallSoundIsKept()
        {
            string folder = Path.Combine(Path.GetTempPath(), "pa_flight_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var s = new AppSettings(folder);
                s.Load(new Tunables());
                Assert.That(s.ballSound, "BALL SOUND: on by default");
                s.ballSound = false;
                s.Save(new Tunables());
                var back = new AppSettings(folder);
                back.Load(new Tunables());
                Assert.That(!back.ballSound && File.ReadAllText(Path.Combine(folder, "settings.cfg")).Contains("ballsound=0"), "kept in settings.cfg (ballsound=0)");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }
    }
}
