// ELBOWS OUT OF THE BODY (TASKS.md E9, native f362017 test_game.cpp "[elbows out of the body]"; the owner, 2026-11-16,
// her video in the builder's mirror: hands together at the chest, the elbows sank into the torso): the elbow swings out
// round the shoulder-to-wrist line only as far as it must
using System;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class ElbowsTests
    {
        struct HandPose { public Vec3 p; public float pitch, yaw; public HandPose(float x, float y, float z, float pi, float ya) { p = new Vec3(x, y, z); pitch = pi; yaw = ya; } }

        static PlayerInput Make(int body, HandPose l, HandPose r, out AvatarDesc d)
        {   // facing +Z, as the native tools/avatar_pose.cpp
            d = Avatar.PresetLook(0);
            d.body = (byte)body;
            Quat face = Quat.AxisAngle(new Vec3(0, 1, 0), kPi);
            var @in = new PlayerInput { headTracked = true, head = new Pose(face, new Vec3(0, 1.62f, 0)) };
            HandPose[] hp = { l, r };
            for (int h = 0; h < 2; ++h)
            {
                Quat q = face * Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(hp[h].yaw)) * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(hp[h].pitch));
                @in.hand[h].tracked = true;
                @in.hand[h].grip = new Pose(q, face.Rotate(new Vec3(hp[h].p.x, 0, -hp[h].p.z)) + new Vec3(0, hp[h].p.y, 0));
            }
            @in.avatar = d;
            return @in;
        }

        static Avatar.Rig Solve(PlayerInput @in, AvatarDesc d, bool swing)
        {
            float allow = Avatar.Tuning.elbowAllow;
            Avatar.Tuning.elbowAllow = swing ? allow : 1e9f;
            try { return Avatar.SolveRig(@in, d); }
            finally { Avatar.Tuning.elbowAllow = allow; }
        }

        [Test]
        public void TheElbowsSwingOutOfTheBody()
        {
            (string name, HandPose l, HandPose r)[] usual = {
                ("rest", new HandPose(-0.24f, 0.92f, 0.12f, -60, 0), new HandPose(0.24f, 0.92f, 0.12f, -60, 0)),
                ("ready", new HandPose(-0.21f, 1.08f, 0.30f, -22, 8), new HandPose(0.21f, 1.08f, 0.30f, -22, -8)),
                ("throw", new HandPose(-0.22f, 1.22f, 0.36f, 10, 10), new HandPose(0.30f, 1.58f, -0.12f, 35, -10)) };
            // (THE REACH, TASKS.md E10: the far reach turns the torso 40 degrees toward that hand: the other arm, still in
            // front, would cross it)
            (string name, HandPose l, HandPose r)[] inside = {
                ("the far reach", new HandPose(-0.21f, 1.08f, 0.30f, -22, 8), new HandPose(0.80f, 1.10f, 0.35f, 0, -60)),
                ("at the chest", new HandPose(-0.05f, 1.27f, 0.24f, 40, 30), new HandPose(0.05f, 1.27f, 0.24f, 40, -30)),
                ("at the belly", new HandPose(-0.06f, 1.05f, 0.20f, 0, 30), new HandPose(0.06f, 1.05f, 0.20f, 0, -30)),
                ("at the chin", new HandPose(-0.05f, 1.45f, 0.22f, 60, 30), new HandPose(0.05f, 1.45f, 0.22f, 60, -30)) };
            bool kept = true, cleared = true, wereIn = true, lengths = true;
            for (int body = 0; body < 2; ++body)
            {
                foreach (var p in usual)
                {   // (an arm already clear of the body keeps its elbow; one in it, out: native 5992aae)
                    PlayerInput @in = Make(body, p.l, p.r, out AvatarDesc d);
                    Avatar.Rig a = Solve(@in, d, false), b = Solve(@in, d, true);
                    for (int k = 0; k < 2; ++k)
                    {
                        float moved = Length(a.upper[k].b - b.upper[k].b), before = Avatar.ArmInBody(a, d, k), after = Avatar.ArmInBody(b, d, k);
                        if (moved > 0.001f)
                            TestContext.Out.WriteLine($"  {p.name}, body {body}: {before * 1000:F0} mm in, the elbow moved {moved * 1000:F0} mm, then {after * 1000:F0} mm in");
                        if ((before <= Avatar.Tuning.elbowAllow && moved > 0.001f) || after > 0.020f) kept = false;
                    }
                }
                foreach (var p in inside)
                {
                    PlayerInput @in = Make(body, p.l, p.r, out AvatarDesc d);
                    Avatar.Rig a = Solve(@in, d, false), b = Solve(@in, d, true);
                    for (int k = 0; k < 2; ++k)
                    {
                        float before = Avatar.ArmInBody(a, d, k), after = Avatar.ArmInBody(b, d, k);
                        TestContext.Out.WriteLine($"  {p.name}, body {body}, arm {k}: {before * 1000:F0} mm in, then {after * 1000:F0}");
                        bool deep = p.name != "the far reach";
                        if (deep) wereIn &= before > 0.04f;
                        if (after > 0.030f) cleared = false;   // (native 5992aae: the PR #25 masculine body's belly 2.6 cm)
                        lengths &= Math.Abs(Length(b.upper[k].b - b.upper[k].a) - Length(a.upper[k].b - a.upper[k].a)) < 1e-3f &&
                                   Math.Abs(Length(b.fore[k].b - b.upper[k].b) - Length(a.fore[k].b - a.upper[k].b)) < 1e-3f;
                    }
                }
            }
            // (the PR #25 masculine body is broad, its arms thick: standing and ready, its arms were in it)
            Assert.That(kept, "standing, ready and throwing, an elbow stays where it was unless its arm was in the body, then out of it");
            Assert.That(wereIn, "(test setup: hands at the chest, the belly and the chin put the arms 4 cm or more into the body)");
            Assert.That(cleared, "there, the elbows swing out until the arms clear the body (to within 3 cm, from 7 to 14 cm in)");
            Assert.That(lengths, "the upper arm and forearm keep their lengths");
            // the hands brought in from ready to the chest and down to the belly, 3 mm a step: the elbows never jump
            float jump = 0;
            for (int body = 0; body < 2; ++body)
            {
                var prev = new Vec3[2];
                const int steps = 120;
                for (int i = 0; i <= steps; ++i)
                {
                    float t = (float)i / steps;
                    Vec3 a = new Vec3(0.21f, 1.08f, 0.30f), b = new Vec3(0.05f, 1.27f, 0.24f), c = new Vec3(0.06f, 1.05f, 0.20f);
                    Vec3 p = t < 0.5f ? Lerp(a, b, t * 2) : Lerp(b, c, t * 2 - 1);
                    float pitch = t < 0.5f ? -22 + 62 * t * 2 : 40 - 40 * (t * 2 - 1), yaw = t < 0.5f ? -8 - 22 * t * 2 : -30;
                    PlayerInput @in = Make(body, new HandPose(-p.x, p.y, p.z, pitch, -yaw), new HandPose(p.x, p.y, p.z, pitch, yaw), out AvatarDesc d);
                    Avatar.Rig r = Solve(@in, d, true);
                    for (int k = 0; k < 2; ++k)
                    {
                        if (i > 0) jump = Math.Max(jump, Length(r.upper[k].b - prev[k]));
                        prev[k] = r.upper[k].b;
                    }
                }
            }
            TestContext.Out.WriteLine($"  (hands brought in to the chest and the belly, 3 mm a step: an elbow moved at most {jump * 1000:F0} mm a step)");
            Assert.That(jump, Is.LessThan(0.03f), "brought in a few millimetres at a time, the elbows never jump");
        }
    }
}
