// The QUEUE SCREEN (TASKS.md E5, native 21cf33b; Runtime/RoomView.cs): beside the JOIN QUEUE kiosk, to the waiting
// player's right and a little nearer, at about eye height, turned toward them, clear of the court ahead
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class QueueScreenTests
    {
        [Test]
        public void ToYourRightTurnedTowardYou()
        {
            var g = new Game();
            g.Reset(true);
            for (int spot = 0; spot < 2; ++spot)
            {   // (the kiosk as RoomView.KioskPose places it: before the spot, the way it faces; its +Z faces the player)
                Vec3 sp = ExpArenaView.HoldingPose(g, spot).p, f = ExpArenaView.HoldingFwd(g, spot);
                var kiosk = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), System.MathF.Atan2(-f.x, -f.z)), new Vec3(sp.x, 0.95f, sp.z) + f * 0.55f);
                Vec3 head = new Vec3(sp.x, 1.6f, sp.z);
                Pose sc = RoomView.QueueScreenPose(kiosk);
                Vec3 right = Normalize(Cross(f, new Vec3(0, 1, 0)));
                Vec3 to = sc.p - head;
                Vec3 facing = sc.q.Rotate(new Vec3(0, 0, 1));
                TestContext.Out.WriteLine($"  spot {spot}: {Dot(to, right):F2} m right, {Dot(to, f):F2} m ahead, {sc.p.y:F2} m up; facing the eyes {Dot(facing, Normalize(head - sc.p)):F2}");
                Assert.That(Dot(to, right), Is.GreaterThan(0.4f), "to the player's right");
                Assert.That(Dot(to, f), Is.GreaterThan(0.05f).And.LessThan(0.4f), "a little ahead, nearer than the kiosk");
                Assert.That(sc.p.y, Is.GreaterThan(1.1f).And.LessThan(1.6f), "at about eye height, below the view ahead");
                Assert.That(Dot(facing, Normalize(head - sc.p)), Is.GreaterThan(0.8f), "turned toward the player");
                Assert.That(Dot(f, Normalize(new Vec3(to.x, 0, to.z))), Is.LessThan(0.5f), "clear of the court ahead (off to the side)");
            }
        }
    }
}
