// HANDS APART (TASKS.md E8; Avatar/HandsApart.cs, native test_game.cpp "[hands apart]", 0.3.20): the shapes of the real
// gauntlet models, and a player's two drawn hands moved apart until they only touch
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class HandsApartTests
    {
        const string Models = "Assets/ProjectArena/Art/Open/Models/";

        // the picture tool's poses (native main.cpp --handstouch): 0 the tops together, 1 the palms, 2 the knuckles, 3 crossed
        static void PoseOf(int k, float gap, Pose[] grip)
        {
            Quat qr = Quat.Identity, ql = Quat.Identity;
            Vec3 dr = new Vec3(gap, 0, 0), dl = new Vec3(-gap, 0, 0);
            if (k == 0) { qr = Quat.AxisAngle(new Vec3(0, 0, 1), kPi * 0.25f); ql = Quat.AxisAngle(new Vec3(0, 0, 1), -kPi * 0.25f); }
            if (k == 2) { qr = Quat.AxisAngle(new Vec3(0, 1, 0), kPi * 0.5f); ql = Quat.AxisAngle(new Vec3(0, 1, 0), -kPi * 0.5f); }
            if (k == 3) { qr = Quat.AxisAngle(new Vec3(0, 1, 0), kPi * 0.20f); ql = Quat.AxisAngle(new Vec3(0, 1, 0), -kPi * 0.20f); dr = new Vec3(-gap, 0.035f, 0); dl = new Vec3(gap, -0.005f, 0); }
            grip[1] = new Pose(qr, new Vec3(0, 1.2f, -0.4f) + dr);
            grip[0] = new Pose(ql, new Vec3(0, 1.2f, -0.4f) + dl);
        }

        [Test]
        public void TheGauntletsDrawnJustApart()
        {
            var t0 = Stopwatch.StartNew();
            HandShape fist = HandsApart.FromModel(File.ReadAllBytes(Models + "gauntlet_0_hand.mdl"), HandsApart.LancerLocal, 5);
            HandShape cuff = HandsApart.FromModel(File.ReadAllBytes(Models + "gauntlet_0_cuff.mdl"), HandsApart.LancerLocal, 4);
            TestContext.Out.WriteLine($"  (the STRIKER fist and cuff: {fist.pts.Count} and {cuff.pts.Count} points, built in {t0.ElapsedMilliseconds} ms)");
            Assert.That(fist.Valid && cuff.Valid, "the gauntlet's fist and cuff become shapes");
            var on = new List<float>();
            foreach (Vec3 p in fist.pts) on.Add(Math.Abs(fist.Dist(p)));
            on.Sort();
            float far = fist.Dist(fist.c + new Vec3(0.3f, 0, 0));
            TestContext.Out.WriteLine($"  (its surface points from its surface: half within {on[on.Count / 2] * 1000:F1} mm, 90% within {on[on.Count * 9 / 10] * 1000:F1}; 30 cm off: {far * 1000:F0} mm out)");
            Assert.That(on[on.Count / 2] < 0.002f && far > 0.2f, "its surface points are on its surface; far away is outside");
            {   // KNUCKLES ON THE FIST (TASKS.md E16, native 6def918): the bumper's front face at the drawn knuckles
                Pose fp = Avatar.FistPose();
                float front = 1e9f;
                foreach (Vec3 p in fist.pts) front = Math.Min(front, fp.Transform(p).z);
                var tn = new Tunables();
                float boxFront = tn.knuckleOffset.z - tn.knuckleHalf.z, oldFront = tn.knuckleOffsetOld.z - tn.knuckleHalf.z;
                TestContext.Out.WriteLine($"  (the drawn fist's front {-front * 100:F1} cm out from the grip; the knuckle bumper's front {-boxFront * 100:F1} cm, before {-oldFront * 100:F1})");
                Assert.That(Math.Abs(boxFront - front), Is.LessThan(0.005f), "KNUCKLES ON THE FIST: the knuckle bumper's front face is at the drawn knuckles (within 5 mm)");
                Assert.That(Length(tn.KnuckleOffsetNow() - tn.knuckleOffset) < 1e-6f, "(offline and as a guest: the new place)");
                tn.matchVersion = Tunables.kVerKnuckle - 1;
                Assert.That(Length(tn.KnuckleOffsetNow() - tn.knuckleOffsetOld) < 1e-6f, "an older game in the match: the old place (FAIR MATCHES)");
            }
            var P = new[] { new HandPart[2], new HandPart[2] };
            var grip = new Pose[2];
            bool cleared = true, small = true, sideways = true, overlapped = false;
            double worstMs = 0;
            for (int k = 0; k < 4; ++k)
                for (float gap = 0.015f; gap < 0.071f; gap += 0.005f)
                {
                    PoseOf(k, gap, grip);
                    HandsApart.Parts(grip[0], grip[1], fist, cuff, true, P);
                    float before = HandsApart.Depth(P[0], 2, P[1], 2, new Vec3());
                    var s0 = Stopwatch.StartNew();
                    Vec3 S = HandsApart.Shift(P[0], 2, P[1], 2, 0.06f);
                    worstMs = Math.Max(worstMs, s0.Elapsed.TotalMilliseconds);
                    float after = HandsApart.Depth(P[0], 2, P[1], 2, S);
                    overlapped |= before > 0.01f;
                    if (Length(S) < 0.0595f && after > 0.0025f)
                    {
                        cleared = false;
                        TestContext.Out.WriteLine($"  pose {k}, {gap * 1000:F0} mm out: {after * 1000:F1} mm deep after {Length(S) * 1000:F1} mm apart ({before * 1000:F1} before)");
                    }
                    small &= Length(S) <= 0.0601f && (before > 0 || Length(S) == 0);
                    if (k < 3 && Length(S) > 0.004f && Math.Abs(S.x) < 0.7f * Length(S))
                    {
                        sideways = false;
                        TestContext.Out.WriteLine($"  pose {k}, {gap * 1000:F0} mm out: moved ({S.x * 1000:F1} {S.y * 1000:F1} {S.z * 1000:F1}) mm");
                    }
                }
            TestContext.Out.WriteLine($"  (the slowest: {worstMs:F2} ms in the editor; native, compiled: 0.33 ms)");
            {   // (the whole set of poses again, warmed: the average a pose)
                var w = Stopwatch.StartNew();
                int calls = 0;
                for (int k = 0; k < 4; ++k)
                    for (float gap = 0.015f; gap < 0.071f; gap += 0.005f, ++calls)
                    {
                        PoseOf(k, gap, grip);
                        HandsApart.Parts(grip[0], grip[1], fist, cuff, true, P);
                        HandsApart.Shift(P[0], 2, P[1], 2, 0.06f);
                    }
                TestContext.Out.WriteLine($"  (warmed, in the editor: {w.Elapsed.TotalMilliseconds / calls:F2} ms a pose on average)");
            }
            Assert.That(overlapped, "(test setup: the poses' gauntlets cross)");
            Assert.That(cleared, "moved apart, the gauntlets no longer cross (under 2.5 mm), unless the 3 cm each way ran out");
            Assert.That(small, "they move only when they cross, and at most 3 cm each");
            Assert.That(sideways, "side by side, they move apart sideways (not forward or up)");
            // drawing them 30 cm apart: nothing; sliding them together: no jumps
            PoseOf(1, 0.15f, grip);
            HandsApart.Parts(grip[0], grip[1], fist, cuff, true, P);
            Assert.That(Length(HandsApart.Shift(P[0], 2, P[1], 2, 0.06f)), Is.EqualTo(0f), "hands apart are left where they are");
            float worst = 0;
            foreach (int k in new[] { 0, 1, 3 })
            {   // the tops, the palms, the wrists crossed
                float jump = 0;
                Vec3 prev = new Vec3();
                for (float gap = 0.07f; gap > 0.025f; gap -= 0.001f)
                {
                    PoseOf(k, gap, grip);
                    HandsApart.Parts(grip[0], grip[1], fist, cuff, true, P);
                    Vec3 S = HandsApart.Shift(P[0], 2, P[1], 2, 0.06f);
                    if (gap < 0.0695f) jump = Math.Max(jump, Length(S - prev));
                    prev = S;
                }
                TestContext.Out.WriteLine($"  (pose {k} slid together 1 mm at a time: the biggest step {jump * 1000:F1} mm)");
                worst = Math.Max(worst, jump);
            }
            Assert.That(worst, Is.LessThan(0.006f), "slid together a millimetre at a time, the drawn hands never jump");
        }

        // the template's own: the shapes from the imported meshes (as AvatarDraw builds them) match the model file's, and
        // a player's input comes back with the hands moved apart, half each, the tracked input untouched
        [Test]
        public void ThePlayersHandsAsDrawn()
        {
            var lib = UnityEditor.AssetDatabase.LoadAssetAtPath<AvatarLibrary>("Assets/ProjectArena/Settings/AvatarLibrary.asset");
            if (lib == null) Assert.Ignore("no AvatarLibrary asset (ProjectConfigure makes it)");
            var av = new AvatarDraw(lib, UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/ProjectArena/Shaders/Text.shader"));
            var t = Stopwatch.StartNew();
            while (!av.ShapesReady && t.ElapsedMilliseconds < 10000) System.Threading.Thread.Sleep(10);
            Assert.That(av.ShapesReady && av.ShapeOf(0, 0).Valid && av.ShapeOf(0, 1).Valid && av.ShapeOf(3, 0).Valid, "every style's shapes are built");
            HandShape file = HandsApart.FromModel(File.ReadAllBytes(Models + "gauntlet_0_hand.mdl"), HandsApart.LancerLocal, 5);
            Assert.That(Length(av.ShapeOf(0, 0).c - file.c), Is.LessThan(0.002f), "the mesh's fist is the model file's");
            var @in = new PlayerInput { headTracked = true };
            var grip = new Pose[2];
            PoseOf(1, 0.03f, grip);
            for (int h = 0; h < 2; ++h) { @in.hand[h].tracked = true; @in.hand[h].grip = grip[h]; }
            AvatarDesc look = Avatar.PresetLook(0);
            look.gauntlet = 0;   // (STRIKER: a plate gauntlet, its fist and cuff)
            PlayerInput o = av.Apart(@in, look);
            Vec3 d0 = o.hand[0].grip.p - @in.hand[0].grip.p, d1 = o.hand[1].grip.p - @in.hand[1].grip.p;
            Assert.That(Length(d1) > 0.002f && Length(d0 + d1) < 1e-5f, "the palms together: moved apart, half each");
            Assert.That(Length(@in.hand[1].grip.p - grip[1].p), Is.EqualTo(0f), "the tracked input is left as it is");
            Assert.That(ReferenceEquals(av.Apart(@in, look), @in) == false && Length(av.Apart(@in, look).hand[1].grip.p - o.hand[1].grip.p) < 1e-6f, "(again: the same, remembered)");
            PoseOf(1, 0.15f, grip);
            for (int h = 0; h < 2; ++h) @in.hand[h].grip = grip[h];
            Assert.That(ReferenceEquals(av.Apart(@in, look), @in), "hands apart: drawn where they are");
        }
    }
}
