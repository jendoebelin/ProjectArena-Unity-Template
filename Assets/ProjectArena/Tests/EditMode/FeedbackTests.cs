// Sounds and haptics for game events (Runtime/Feedback.cs), as audio.cpp onEvent and main.cpp handleEvents decide them.
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public class FeedbackTests
    {
        static GameEvent E(EventType t, int player, int hand = -1, float value = 0, Vec3 pos = default) => new GameEvent(t, player, hand, pos, value);

        [Test]
        public void SoundKeysFromTheListenersSide()
        {
            Assert.That(Feedback.SoundKey(E(EventType.HitPlayer, 0), 0), Is.EqualTo("HitPlayerSelf"), "you were hit");
            Assert.That(Feedback.SoundKey(E(EventType.HitPlayer, 1), 0), Is.EqualTo("HitPlayerOpponent"), "you hit them");
            Assert.That(Feedback.SoundKey(E(EventType.MatchEnd, 0), 0), Is.EqualTo("MatchEndWin"));
            Assert.That(Feedback.SoundKey(E(EventType.RoundEnd, 1), 0), Is.EqualTo("RoundEndLose"));
            Assert.That(Feedback.SoundKey(E(EventType.Throw, 0, 1, 5.9f), 0), Is.EqualTo("ThrowSlow"));
            Assert.That(Feedback.SoundKey(E(EventType.Throw, 0, 1, 8f), 0), Is.EqualTo("ThrowMed"));
            Assert.That(Feedback.SoundKey(E(EventType.Throw, 0, 1, 11.9f), 0), Is.EqualTo("ThrowFast"));
            Assert.That(Feedback.SoundKey(E(EventType.Throw, 0, 1, 14f), 0), Is.EqualTo("ThrowFull"));
            Assert.That(Feedback.SoundKey(E(EventType.Bounce, 1), 0), Is.EqualTo("Bounce"));
            // THE PUNCH LANDS (TASKS.md E17, native f045e04): a hard punch, from 12 m/s, has its own sound
            Assert.That(Feedback.SoundKey(E(EventType.KnuckleBlock, 0, 1, 11.9f), 0), Is.EqualTo("KnuckleBlock"));
            Assert.That(Feedback.SoundKey(E(EventType.KnuckleBlock, 1, 1, 12f), 0), Is.EqualTo("KnucklePunch"));
        }

        [Test]
        public void NearerIsLouderAndPannedAcrossTheCourt()
        {
            var ear = new Vec3(-7, 1.65f, 0);
            Assert.That(Feedback.Gain(E(EventType.Countdown, -1), ear), Is.EqualTo(1f), "no place: full volume");
            Assert.That(Feedback.Gain(E(EventType.Bounce, 0, pos: new Vec3(-6, 1.65f, 0)), ear), Is.EqualTo(1f).Within(1e-6f), "1 m away: 2 / 2");
            Assert.That(Feedback.Gain(E(EventType.Bounce, 1, pos: new Vec3(7, 1.65f, 0)), ear), Is.EqualTo(2f / 15f > 0.15f ? 2f / 15f : 0.15f).Within(1e-6f),
                        "the far end: quiet, never under 0.15");
            Assert.That(Feedback.Pan(E(EventType.Bounce, 0, pos: new Vec3(-5, 1, 1)), ear), Is.EqualTo(0.4f).Within(1e-6f), "+Z is your right");
            Assert.That(Feedback.Pan(E(EventType.Bounce, 0, pos: new Vec3(-5, 1, -3)), ear), Is.EqualTo(-0.8f), "pan stops at 0.8");
        }

        [Test]
        public void HapticsAsTheNativeTable()
        {
            Assert.That(Feedback.Haptic(E(EventType.Grab, 0, 1), true, out int h, out float a, out float s) && h == 1 && a == 0.85f && s == 0.12f);
            Assert.That(Feedback.Haptic(E(EventType.Caught, 0, 0), true, out h, out a, out s) && h == 0 && a == 1f && s == 0.18f);
            Assert.That(Feedback.Haptic(E(EventType.ShieldBlock, 0, 1), true, out _, out a, out s) && a == 1f && s == 0.28f);
            Assert.That(Feedback.Haptic(E(EventType.ShieldBreak, 0, 1), true, out _, out a, out s) && a == 1f && s == 0.35f);
            // THE PUNCH LANDS: a short hard knock by how hard the punch sent the ball (7 m/s from a still fist to 16; was 0.2 s)
            Assert.That(Feedback.Haptic(E(EventType.KnuckleBlock, 0, 0, 7f), true, out _, out a, out s) && a == 1f && System.Math.Abs(s - 0.06f) < 1e-6f, "a still fist's punch: 0.06 s");
            Assert.That(Feedback.Haptic(E(EventType.KnuckleBlock, 0, 0, 16f), true, out _, out a, out s) && a == 1f && System.Math.Abs(s - 0.10f) < 1e-6f, "the hardest: 0.1 s");
            Assert.That(Feedback.PunchPower(E(EventType.KnuckleBlock, 0, 0, 3f)), Is.EqualTo(0.2f), "never under 0.2");
            Assert.That(Feedback.PunchPower(E(EventType.KnuckleBlock, 0, 0, 30f)), Is.EqualTo(1f), "never over 1");
            Assert.That(Feedback.Haptic(E(EventType.Throw, 0, 1, 10f), true, out _, out a, out s) && a == 0.6f && s == 0.08f);
            Assert.That(Feedback.Haptic(E(EventType.ThrowFail, 0, 1), true, out _, out a, out s) && a == 0.4f && s == 0.06f);
            Assert.That(Feedback.Haptic(E(EventType.HitPlayer, 0), true, out h, out a, out s) && h == -1 && a == 1f && s == 0.5f, "hit: both hands");
            Assert.That(Feedback.Haptic(E(EventType.Grab, 1, 1), false, out _, out _, out _), Is.False, "the bot's grab: nothing on your hands");
            Assert.That(Feedback.Haptic(E(EventType.HitPlayer, 1), false, out _, out _, out _), Is.False, "you hit them: nothing");
            Assert.That(Feedback.Haptic(E(EventType.Bounce, 0), true, out _, out _, out _), Is.False);
        }

        // THE PUNCH LANDS: the ball squashed along its new way (a third at most, bulging half that round it), round again
        // in 0.14 s, the axis given in the object's own axes
        [Test]
        public void ThePunchedBallSquashes()
        {
            UnityEngine.Vector4 q = MatchView.SquashFor(0f, 1f, new Vec3(1, 0, 0), UnityEngine.Quaternion.identity);
            Assert.That(q.w, Is.EqualTo(0.32f).Within(1e-6f), "the hardest punch, at once: squashed by 32%");
            Assert.That(q.x, Is.EqualTo(1f).Within(1e-6f), "along its new way (+X)");
            Assert.That(MatchView.SquashFor(0f, 0.2f, new Vec3(1, 0, 0), UnityEngine.Quaternion.identity).w, Is.EqualTo(0.064f).Within(1e-6f), "a soft one, a fifth of that");
            Assert.That(MatchView.SquashFor(0.07f, 1f, new Vec3(1, 0, 0), UnityEngine.Quaternion.identity).w, Is.EqualTo(0.08f).Within(1e-6f), "half way: a quarter");
            Assert.That(MatchView.SquashFor(0.14f, 1f, new Vec3(1, 0, 0), UnityEngine.Quaternion.identity).w, Is.EqualTo(0f), "round again in 0.14 s");
            UnityEngine.Vector4 turned = MatchView.SquashFor(0f, 1f, new Vec3(1, 0, 0), UnityEngine.Quaternion.Euler(0, 90, 0));
            UnityEngine.Vector3 back = UnityEngine.Quaternion.Euler(0, 90, 0) * new UnityEngine.Vector3(turned.x, turned.y, turned.z);
            Assert.That((back - new UnityEngine.Vector3(1, 0, 0)).magnitude, Is.LessThan(1e-5f), "a turned object (the shell): the axis in its own axes");
        }
    }
}
