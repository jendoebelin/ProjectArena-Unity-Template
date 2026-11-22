// BALL FEEL (TEST) (TASKS.md E18, native a2e53ce; Runtime/BallFeel.cs): the ball's drawn turn (SPIN VISIBLE), a held ball
// drawn a little behind the hand (WEIGHT HEAVY) and its hum, GLOW TRUE SIZE; nothing in play changes
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class BallFeelTests
    {
        static float AngleOf(Quat q) => 2f * System.MathF.Acos(System.Math.Min(1f, System.Math.Abs(q.w)));

        [Test]
        public void SpinVisibleTumblesAsItFlies()
        {
            var g = new Game();
            g.Reset(true);
            var f = new BallFeel();
            Ball b = g.pl[0].ball;
            b.state = BallState.Flying; b.vel = new Vec3(10, 0, 0); b.spin = new Vec3();
            f.Step(g, 0.01f);
            // a quarter of a rolling ball's rate: 10 m/s over the 10 cm radius, x 0.25 = 25 rad/s, rolling forward
            Assert.That(AngleOf(f.RawTurn(0)), Is.EqualTo(0.25f).Within(1e-4f), "flying at 10 m/s: a quarter of rolling, 25 rad/s");
            Vec3 ax = Normalize(new Vec3(f.RawTurn(0).x, f.RawTurn(0).y, f.RawTurn(0).z));
            Assert.That(Length(ax - new Vec3(0, 0, -1)) < 1e-4f, "turning about the axis it would roll on (forward over the top)");
            Assert.That(f.Turn(0).w, Is.EqualTo(1f), "SPIN SUBTLE (off): drawn as it was");
            f.spin = true;
            Assert.That(AngleOf(f.Turn(0)), Is.EqualTo(0.25f).Within(1e-4f), "SPIN VISIBLE: drawn turned");
            b.state = BallState.Idle; b.vel = new Vec3(); b.spin = new Vec3(0, 30, 0);
            Quat before = f.RawTurn(0);
            f.Step(g, 0.01f);
            Assert.That(AngleOf(f.RawTurn(0) * before.Conj()), Is.EqualTo(0.3f).Within(1e-4f), "its own spin turns it too (30 rad/s)");
            b.state = BallState.Held; before = f.RawTurn(0);
            f.Step(g, 0.01f);
            Assert.That(Length(new Vec3(f.RawTurn(0).x - before.x, f.RawTurn(0).y - before.y, f.RawTurn(0).z - before.z)), Is.EqualTo(0f), "held: it stays as it was");
        }

        [Test]
        public void WeightHeavyTrailsTheHand()
        {
            var g = new Game();
            g.Reset(true);
            var f = new BallFeel();
            Ball b = g.pl[0].ball;
            b.state = BallState.Held; b.pos = new Vec3(-7, 1, 0);
            f.Step(g, 1f / 72);
            Assert.That(Length(f.DrawnAt(0, b) - b.pos), Is.EqualTo(0f), "WEIGHT LIGHT (off): drawn in the hand");
            f.weight = true;
            Assert.That(Length(f.DrawnAt(0, b) - b.pos), Is.EqualTo(0f), "taken up: drawn where it is");
            b.pos = new Vec3(-7, 1, 0.10f);   // (the hand swung 10 cm in a frame)
            f.Step(g, 1f / 72);
            float lag = Length(f.DrawnAt(0, b) - b.pos);
            Assert.That(lag, Is.EqualTo(BallFeel.LagMax).Within(1e-5f), "a fast swing: 3 cm behind at most");
            for (int i = 0; i < 9; ++i) f.Step(g, 1f / 72);   // (held still an eighth of a second)
            Assert.That(Length(f.DrawnAt(0, b) - b.pos), Is.LessThan(0.0005f), "the hand still: it catches up (12 ms behind)");
            b.pos = new Vec3(-7, 1, 0.105f);
            f.Step(g, 1f / 72);
            lag = Length(f.DrawnAt(0, b) - b.pos);
            Assert.That(lag > 0.001f && lag < 0.002f, $"a slow move (5 mm a frame): a little behind ({lag * 1000:F1} mm)");
            b.state = BallState.Flying;
            f.Step(g, 1f / 72);
            Assert.That(Length(f.DrawnAt(0, b) - b.pos), Is.EqualTo(0f), "thrown: drawn where it is");
            // the hum as you swing it: from 1 m/s, stronger faster, never over 0.6
            Assert.That(BallFeel.HumAmp(0.9f), Is.EqualTo(0f));
            Assert.That(BallFeel.HumAmp(4f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(BallFeel.HumAmp(20f), Is.EqualTo(0.6f));
        }

        [Test]
        public void GlowTrueSizeAndTheCore()
        {
            var f = new BallFeel();
            var t = new Tunables();
            Assert.That(f.Vis, Is.EqualTo(1f), "GLOW WIDE: as it was");
            f.trueSize = true;
            // (the shell fragments reach 12.5 cm on the 10 cm ball: x0.8, their outside at the 10 cm that hits)
            Assert.That(0.125f * f.Vis, Is.EqualTo(t.projectileRadius).Within(1e-4f), "GLOW TRUE SIZE: the outside where it hits");
            Assert.That(BallFeel.CoreShare * t.projectileRadius, Is.EqualTo(0.062f).Within(1e-5f), "CORE SOLID: 62% of what hits");
            BallFeel.CoreTint(new UnityEngine.Vector3(0.1f, 0.7f, 1f), true, out UnityEngine.Vector3 mul, out UnityEngine.Vector3 em);
            Assert.That(System.Math.Abs(mul.z - 0.8f) < 1e-6f && System.Math.Abs(em.z - 0.38f) < 1e-6f, "the team colour, lit, a glow of its own");
        }
    }
}
