// FOREARM TWIST (TASKS.md E11, native 0a41fe7 test_game.cpp "[forearm twist]"; PR #25): the forearm bone turns with the
// hand; the shader turns its skin back toward the elbow. Here the shader's sum for one forearm influence, on rings round
// the forearm near each end; and the template's own: the values as the shader takes them (the meshes' z negated) give
// the same twisted point, mirrored
using System;
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEngine;
using static ProjectArena.Sim.M;
using Pose = ProjectArena.Sim.Pose;
using Quat = ProjectArena.Sim.Quat;

namespace ProjectArena.Tests
{
    public class TwistTests
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

        static readonly HandPose[] Ready = { new HandPose(-0.21f, 1.08f, 0.30f, -22, 8), new HandPose(0.21f, 1.08f, 0.30f, -22, -8) };
        static float WrapAngle(float a) { while (a > kPi) a -= 2 * kPi; while (a < -kPi) a += 2 * kPi; return a; }
        static Vec3 V4(float[] t, int i) => new Vec3(t[i * 4], t[i * 4 + 1], t[i * 4 + 2]);

        // the shader's sum for a forearm influence (native axes)
        static Vec3 Twisted(Avatar.Rig r, int k, Vec3 bp, bool on)
        {
            float[] t = r.twist;
            Vec3 p = r.skin[(int)Avatar.BoneId.ForeL + k].TransformPoint(bp);
            if (!on) return p;
            float s = Clampf(Dot(bp - V4(t, k), V4(t, 2 + k)) * t[k * 4 + 3], 0f, 1f);
            float th = -t[(2 + k) * 4 + 3] * (1f - s * s * (3f - 2f * s));
            Vec3 E = V4(t, 4 + k);
            return E + Quat.AxisAngle(V4(t, 6 + k), th).Rotate(p - E);
        }

        // the right hand held out in front at chest height, turned about its own forward axis by `roll` degrees
        static PlayerInput Held(int body, float roll, out AvatarDesc d)
        {
            PlayerInput @in = Make(body, Ready[0], new HandPose(0.21f, 1.22f, 0.42f, 0, 0), out d);
            @in.hand[1].grip.q = @in.hand[1].grip.q * Quat.AxisAngle(new Vec3(0, 0, 1), Deg2Rad(roll));
            return @in;
        }

        // a ring's turn about the forearm, against the shoulder seen from the elbow
        static float RingTurn(Avatar.Rig r, int body, float at, bool on)
        {
            Avatar.BodyBind K = Avatar.BodyBindOf(body);
            Vec3 a = K.elbow[1], b = K.wrist[1], ax0 = Normalize(b - a);
            Vec3 side = Normalize(Cross(ax0, new Vec3(0, 1, 0)));
            Vec3 bp = a + (b - a) * at + side * 0.04f;
            Vec3 E = r.fore[1].a, ax = Normalize(r.fore[1].b - r.fore[1].a);
            Vec3 u = Twisted(r, 1, bp, on) - E, rf = r.upper[1].a - E;
            u = u - ax * Dot(u, ax);
            rf = rf - ax * Dot(rf, ax);
            return MathF.Atan2(Dot(Cross(rf, u), ax), Dot(rf, u));
        }

        [Test]
        public void TheForearmsSkinTurnsBackTowardTheElbow()
        {
            bool restNone = true, follows = true, elbowStays = true, wristTurns = true;
            float worstElbow = 0, worstJump = 0, worstWrist = 0;
            for (int body = 0; body < 2; ++body)
            {
                PlayerInput rest = Make(body, Ready[0], Ready[1], out AvatarDesc d);
                rest.hand[0].tracked = rest.hand[1].tracked = false;
                Avatar.Rig r0 = Avatar.SolveRig(rest, d);
                if (r0.twisted || Math.Abs(r0.roll[0]) > 1e-4f || Math.Abs(r0.roll[1]) > 1e-4f) restNone = false;
                foreach (float bas in new[] { -60f, 0f, 40f })
                {
                    Avatar.Rig ra = Avatar.SolveRig(Held(body, bas, out d), d), rb = Avatar.SolveRig(Held(body, bas + 50f, out d), d);
                    float dRoll = Rad2Deg(WrapAngle(rb.roll[1] - ra.roll[1]));
                    // the roll is the hand's turn about the forearm (the grip's z points back along it: its +50 is the forearm's -50)
                    if (dRoll > -40f || dRoll < -60f) follows = false;
                    // near the elbow (4% of the way) the skin keeps its turn whatever the hand does; untwisted it turned with it
                    float tOn = Rad2Deg(WrapAngle(RingTurn(rb, body, 0.04f, true) - RingTurn(ra, body, 0.04f, true)));
                    float tOff = Rad2Deg(WrapAngle(RingTurn(rb, body, 0.04f, false) - RingTurn(ra, body, 0.04f, false)));
                    worstElbow = Math.Max(worstElbow, Math.Abs(tOn));
                    if (Math.Abs(tOn) > 6f || Math.Abs(tOff) < 40f) elbowStays = false;
                    // at the wrist the skin turns with the hand, as the bone does
                    float wOn = Rad2Deg(WrapAngle(RingTurn(rb, body, 1f, true) - RingTurn(ra, body, 1f, true)));
                    if (Math.Abs(wOn - dRoll) > 6f) wristTurns = false;
                }
                // the hand turned all the way round, 2 degrees a step: what the shader undoes follows the turn within a
                // wrist's reach (120 degrees either way from thumbs up), and never jumps
                float last = 0;
                for (int i = 0; i <= 360; i += 2)
                {
                    Avatar.Rig r = Avatar.SolveRig(Held(body, -180f + i, out d), d);
                    float undo = Rad2Deg(r.twist[(2 + 1) * 4 + 3]);
                    if (i > 0) worstJump = Math.Max(worstJump, Math.Abs(undo - last));
                    if (i > 0 && Math.Abs(-180f + i) <= 120f) worstWrist = Math.Max(worstWrist, Math.Abs(undo - last));
                    last = undo;
                }
            }
            TestContext.Out.WriteLine($"  (the skin near the elbow turned at most {worstElbow:F1} deg with the hand turned 50; the undone turn moves at most {worstWrist:F1} deg a 2 deg step within a wrist's turn, {worstJump:F1} past it)");
            Assert.That(restNone, "FOREARM TWIST: arms at rest are untwisted");
            Assert.That(follows, "FOREARM TWIST: the roll follows the hand's turn about the forearm");
            Assert.That(elbowStays, "FOREARM TWIST: near the elbow the skin keeps its turn when the hand turns (untwisted, it turned with the hand)");
            Assert.That(wristTurns, "FOREARM TWIST: at the wrist the skin turns with the hand");
            Assert.That(worstJump < 15f && worstWrist < 3f, "FOREARM TWIST: turning the hand all the way round, the twist never jumps");
        }

        // the template's own: in the meshes' space (z negated) with the values as the shader gets them, the same turn
        [Test]
        public void TheShadersValuesInTheMeshesSpace()
        {
            Avatar.Rig r = Avatar.SolveRig(Held(0, 30f, out AvatarDesc d), d);
            Assert.That(r.twisted, "(test setup: a turned hand)");
            Vector4[] u = AvatarDraw.TwistToUnity(r.twist, new Vector4[8]);
            Matrix4x4 Sz = Matrix4x4.Scale(new Vector3(1, 1, -1));
            Avatar.BodyBind K = Avatar.BodyBindOf(0);
            float worst = 0;
            for (float at = 0f; at <= 1f; at += 0.1f)
            {
                Vec3 bp = K.elbow[1] + (K.wrist[1] - K.elbow[1]) * at + new Vec3(0.03f, 0.02f, 0);
                Vec3 want = Twisted(r, 1, bp, true);
                // the shader, as Native.shader TwistBone sums it, on the mesh's point with the Unity-space bone matrix
                Mat4 F = r.skin[(int)Avatar.BoneId.ForeL + 1];
                var Fm = new Matrix4x4();
                for (int c = 0; c < 4; ++c) for (int rr = 0; rr < 4; ++rr) Fm[rr, c] = F.m[c * 4 + rr];
                Matrix4x4 FU = Sz * Fm * Sz;
                var bpU = new Vector3(bp.x, bp.y, -bp.z);
                Vector3 elbowB = u[1], dB = u[3], E = u[5], ax = u[7];
                float s = Mathf.Clamp01(Vector3.Dot(bpU - elbowB, dB) * u[1].w);
                float th = -u[3].w * (1f - s * s * (3f - 2f * s));
                Vector3 p = FU.MultiplyPoint3x4(bpU);
                Vector3 got = E + Quaternion.AngleAxis(th * Mathf.Rad2Deg, ax) * (p - E);
                worst = Mathf.Max(worst, (got - new Vector3(want.x, want.y, -want.z)).magnitude);
            }
            Assert.That(worst, Is.LessThan(1e-4f), "the mirrored values turn the mirrored skin the same way");
        }
    }
}
