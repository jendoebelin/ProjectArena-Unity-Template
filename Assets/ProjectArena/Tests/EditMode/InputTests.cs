// The local player's input maths (Runtime/XrInput.cs): axis conversion, recentering, the tool frame, angular velocity.
using System;
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEngine;
using static ProjectArena.Sim.M;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena.Tests
{
    public class InputTests
    {
        static void Near(Vec3 a, Vec3 b, float tol, string msg) =>
            Assert.That(Length(a - b), Is.LessThan(tol), $"{msg}: {a} vs {b}");

        [Test]
        public void AxisConversionKeepsRotations()
        {
            // a native rotation applied natively, then shown in Unity == the Unity rotation applied to the Unity vector
            Quat q = Quat.AxisAngle(Normalize(new Vec3(0.3f, 1f, -0.4f)), 1.1f);
            var v = new Vec3(0.2f, -0.5f, 0.7f);
            Vector3 viaNative = NativeSpace.ToUnity(q.Rotate(v));
            Vector3 viaUnity = NativeSpace.ToUnity(q) * NativeSpace.ToUnity(v);
            Assert.That((viaNative - viaUnity).magnitude, Is.LessThan(1e-5f), "rotations survive the axis change");
            // and back
            Vec3 back = XrInput.ToNative(NativeSpace.ToUnity(v));
            Near(back, v, 1e-6f, "positions round-trip");
            Quat qb = XrInput.ToNative(NativeSpace.ToUnity(q));
            Assert.That(MathF.Abs(qb.x * q.x + qb.y * q.y + qb.z * q.z + qb.w * q.w), Is.EqualTo(1f).Within(1e-5f), "rotations round-trip");
        }

        [Test]
        public void RecenterPutsTheHeadAtTheStartFacingTheCourt()
        {
            var g = new Game();
            // the head anywhere in the room, turned 37 degrees, 1.7 m up
            var headTrack = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(37)), new Vec3(0.5f, 1.7f, -0.3f));
            Pose t = XrInput.Recenter(headTrack, g.tun.start[0], new Vec3(1, 0, 0), 0f);
            Pose head = t * headTrack;
            Near(head.p, g.tun.start[0] + new Vec3(0, 1.7f, 0), 1e-4f, "head over the start point, at its own height");
            Vec3 f = head.q.Rotate(new Vec3(0, 0, -1));
            Near(Normalize(new Vec3(f.x, 0, f.z)), new Vec3(1, 0, 0), 1e-4f, "looking down the court (+X) toward the opponent");
            Pose lifted = XrInput.Recenter(headTrack, g.tun.start[0], new Vec3(1, 0, 0), 0.4f);
            Assert.That((lifted * headTrack).p.y, Is.EqualTo(2.1f).Within(1e-4f), "the floor guard's lift is kept");
        }

        [Test]
        public void ToolFrameIsTheAimRotationWithTheTuning()
        {
            var xr = new XrInput { tunePitchDeg = 0, tuneYawDeg = 0, tuneRollDeg = 0, tuneX = 0, tuneUp = 0, tuneFwd = 0 };
            Quat aim = Quat.AxisAngle(new Vec3(1, 0, 0), 0.3f);
            var grip = new Vec3(0.1f, 1.0f, -0.2f);
            Pose plain = xr.ToolFrame(grip, aim, 1);
            Near(plain.p, grip, 1e-6f, "no tuning: the grip position");
            Assert.That(MathF.Abs(plain.q.x * aim.x + plain.q.y * aim.y + plain.q.z * aim.z + plain.q.w * aim.w), Is.EqualTo(1f).Within(1e-6f),
                        "no tuning: the aim orientation");
            var tuned = new XrInput();   // the owner's export
            Pose r = tuned.ToolFrame(default, Quat.Identity, 1), l = tuned.ToolFrame(default, Quat.Identity, 0);
            Near(l.p, new Vec3(-r.p.x, r.p.y, r.p.z), 1e-6f, "the left hand's offset is the right hand's mirrored");
            Near(r.p, r.q.Rotate(new Vec3(-0.006f, -0.014f, -0.004f)), 1e-6f, "the offset is along the tuned frame");
        }

        [Test]
        public void AngularVelocityFromRotationChange()
        {
            Vec3 w = XrInput.AngularVelocity(Quat.Identity, Quat.AxisAngle(new Vec3(0, 1, 0), 0.1f), 0.01f);
            Near(w, new Vec3(0, 10, 0), 1e-3f, "0.1 rad in 10 ms about +Y = 10 rad/s about +Y");
            Quat a = Quat.AxisAngle(Normalize(new Vec3(1, 2, 3)), 0.5f);
            Vec3 w2 = XrInput.AngularVelocity(a, Quat.AxisAngle(new Vec3(1, 0, 0), 0.02f) * a, 0.01f);
            Near(w2, new Vec3(2, 0, 0), 1e-3f, "a world-frame turn about +X, whatever the start");
            Near(XrInput.AngularVelocity(a, a, 0.01f), default, 1e-6f, "no turn, no spin");
            // a controller turning clockwise seen from above: in Unity's axes a positive turn about +Y; in the native
            // (right-handed) axes the same motion is a negative turn about +Y
            Quat u0 = XrInput.ToNative(Quaternion.identity), u1 = XrInput.ToNative(Quaternion.AngleAxis(Mathf.Rad2Deg * 0.1f, Vector3.up));
            Assert.That(XrInput.AngularVelocity(u0, u1, 0.01f).y, Is.EqualTo(-10f).Within(1e-3f), "the spin's sign follows the native axes");
        }

        [Test]
        public void OriginShiftIsMeasuredFromTheHeadJump()
        {
            var head = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), 0.4f), new Vec3(0.3f, 1.7f, -0.2f));
            // the system moves the origin: the new origin's pose in the old space is d; the same head reads d^-1 * head
            var small = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(2)), new Vec3(0.03f, 0.02f, -0.02f));
            Assert.That(XrInput.OriginShift(head, XrInput.Inverse(small) * head, out Pose s, out float moved, out _), Is.True,
                        "a few centimetres and degrees: tracking settling, compensated");
            Near(s.p, small.p, 1e-4f, "the shift is recovered");
            Assert.That(moved, Is.EqualTo(MathF.Sqrt(0.03f * 0.03f + 0.02f * 0.02f)).Within(1e-4f));
            // (TASKS.md E30, native 1497432: a Meta button recenter made nearly facing the headset's forward, 20 cm and under 10
            // degrees, was taken for settling until 2026-11-17; now anything from 5 cm or 3 degrees re-places you)
            var nearly = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(8)), new Vec3(0.2f, 0, 0));
            Assert.That(XrInput.OriginShift(head, XrInput.Inverse(nearly) * head, out _, out _, out _), Is.False,
                        "a recenter of 20 cm and 8 degrees: re-placed, facing in");
            var turnOnly = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(4)), new Vec3(0.01f, 0, 0));
            Assert.That(XrInput.OriginShift(head, XrInput.Inverse(turnOnly) * head, out _, out _, out _), Is.False, "turned 4 degrees: re-placed");
            var large = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(170)), new Vec3(1.2f, 0, 0.4f));
            Assert.That(XrInput.OriginShift(head, XrInput.Inverse(large) * head, out _, out _, out float turned), Is.False,
                        "holding the Meta button: a system recenter, the player is re-placed");
            Assert.That(turned, Is.EqualTo(170f).Within(0.1f));
        }
    }
}
