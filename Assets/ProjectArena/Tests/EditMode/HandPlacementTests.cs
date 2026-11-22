// Hand model placement (Runtime/HandPlacement.cs): native matrices shown on Unity transforms, mirrored ones included.
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEngine;
using static ProjectArena.Sim.M;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena.Tests
{
    public class HandPlacementTests
    {
        static void SameMatrix(Matrix4x4 a, Matrix4x4 b, string msg)
        {
            for (int r = 0; r < 4; ++r)
                for (int c = 0; c < 4; ++c)
                    Assert.That(a[r, c], Is.EqualTo(b[r, c]).Within(1e-4f), $"{msg}: element [{r},{c}]\n{a}\nvs\n{b}");
        }

        [Test]
        public void ApplyShowsTheNativeMatrixInUnityAxes()
        {
            var go = new GameObject("test");
            try
            {
                var grip = new Pose(Quat.AxisAngle(Normalize(new Vec3(0.2f, 1, 0.3f)), 0.8f), new Vec3(-6.8f, 1.1f, 0.3f));
                var sz = Matrix4x4.Scale(new Vector3(1, 1, -1));
                Matrix4x4 right = HandPlacement.FromPose(grip) * HandPlacement.LancerLocal;
                Matrix4x4 left = HandPlacement.FromPose(grip) * HandPlacement.Mirror * HandPlacement.LancerLocal;
                Matrix4x4 shield = HandPlacement.FromPose(grip) * HandPlacement.PlateLocal;
                foreach (var (m, name) in new[] { (right, "right gauntlet"), (left, "left gauntlet (mirrored)"), (shield, "shield plate (scaled 10x)") })
                {
                    HandPlacement.Apply(go.transform, m);
                    SameMatrix(go.transform.localToWorldMatrix, sz * m * sz, name);
                }
                Assert.That(right.determinant * left.determinant, Is.LessThan(0), "the left hand is the right hand's mirror image");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CuffPoseIsMirroredBetweenTheHands()
        {
            Pose r = HandPlacement.CuffPose(false), l = HandPlacement.CuffPose(true);
            Assert.That(l.p.x, Is.EqualTo(-r.p.x).Within(1e-6f));
            Assert.That(l.p.y, Is.EqualTo(r.p.y).Within(1e-6f));
            Assert.That(l.p.z, Is.EqualTo(r.p.z).Within(1e-6f));
            var v = new Vec3(0.1f, 0.2f, -0.3f);
            Vec3 a = l.q.Rotate(new Vec3(-v.x, v.y, v.z)), b = r.q.Rotate(v);
            Assert.That(Length(a - new Vec3(-b.x, b.y, b.z)), Is.LessThan(1e-5f), "the left cuff turns as the right one, mirrored");
        }
    }
}
