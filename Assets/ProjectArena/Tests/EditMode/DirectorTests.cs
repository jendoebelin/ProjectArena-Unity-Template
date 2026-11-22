// The spectator's cameras (Runtime/Director.cs) and The Crowd in the stands (CrowdStands.cs), as the native
// spectatorCamera / director / directorEvents / packWarps and burstCrowdFx / stepCrowdFx behave.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class DirectorTests
    {
        string folder;
        AppState app;
        ArenaSession s;
        Director d;
        XrInput xr;
        PlayerInput head;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "pa_director_" + System.Guid.NewGuid().ToString("N"));
            var g = new Game();
            g.Reset(true);
            app = new AppState { game = g, settings = new AppSettings(folder), net = new Online.Net(), lobby = false, spectating = true, watchIp = 1234 };
            app.settings.Load(g.tun);
            s = new ArenaSession(app);
            d = new Director(s);
            app.director = d;
            xr = new XrInput();
            head = new PlayerInput { headTracked = true, head = new Pose(Quat.Identity, new Vec3(0, 2.6f, 5.4f)) };
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        void Press(System.Action<XrInput> set)
        {
            d.Update(xr, head, 0.011f, false);   // released
            set(xr);
            d.Update(xr, head, 0.011f, false);   // pressed
            xr.btnA = xr.btnX = xr.btnY = false;
        }

        // the head where the tracking origin puts it now (the tracking-space head at the origin)
        Vec3 Head() => xr.trackToWorld.Transform(xr.headTrack.p);

        [Test]
        public void SaveNextAndAutoCameras()
        {
            xr.stickL = new UnityEngine.Vector2(0, 1);   // the left stick forward: fly toward -Z (where the head looks)
            Vec3 before = xr.trackToWorld.p;
            d.Update(xr, head, 0.1f, false);
            xr.stickL = default;
            Assert.That(xr.trackToWorld.p.z, Is.LessThan(before.z - 0.25f), "the left stick flies forward (3 m/s)");
            Press(x => x.btnA = true);
            Assert.That(d.warps.Count, Is.EqualTo(1), "A saves a camera here");
            head.head = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), 1.0f), new Vec3(4, 1.8f, -2));
            Press(x => x.btnA = true);
            Assert.That(d.warps.Count == 2 && d.warpIdx == 1, "a second camera");
            Assert.That(File.Exists(Path.Combine(folder, "warps.cfg")), "kept in warps.cfg");
            d.warps[0].trans = 0;   // CUT
            xr.headTrack = new Pose(Quat.Identity, new Vec3(0.1f, 1.7f, 0.2f));
            Press(x => x.btnX = true);
            Assert.That(d.warpIdx, Is.EqualTo(0), "X: the next camera (round to the first)");
            Assert.That(Length(Head() - d.warps[0].pose.p), Is.LessThan(1e-4f), "a CUT camera puts the head there at once");
            Press(x => x.btnY = true);
            Assert.That(d.autoCam, "Y: the auto camera on");
            d.warps[1].trig = 1;   // ACTION
            d.Events(new List<GameEvent> { new GameEvent(EventType.HitPlayer, 1, -1, new Vec3(4, 1.5f, -2), 0) });
            Assert.That(d.dirRequest, Is.EqualTo(1), "a hit near an action camera asks for it");
            d.Update(xr, head, 0.011f, false);
            Assert.That(d.warpIdx, Is.EqualTo(1), "... and the auto camera goes there");
            // a CROSS-FADE camera: the view darkens, the head moves at the darkest point, then the view comes back
            float dark = 0;
            for (int i = 0; i < 20; ++i) dark = System.Math.Max(dark, d.Fade(xr, 0.011f));
            Assert.That(dark, Is.GreaterThan(0.9f), "through dark");
            Assert.That(Length(Head() - d.warps[1].pose.p), Is.LessThan(1e-4f), "at the camera");
            for (int i = 0; i < 40; ++i) dark = d.Fade(xr, 0.011f);
            Assert.That(dark, Is.EqualTo(0f), "and the view back");
        }

        [Test]
        public void WarpsFileAndTeamSharing()
        {
            d.warps.Add(new Director.WarpCam { pose = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), 0.5f), new Vec3(1, 2, 3)), hold = 3, trans = 0, trig = 2 });
            d.warps.Add(new Director.WarpCam { pose = new Pose(Quat.Identity, new Vec3(-4, 1.5f, 6)) });
            d.Save();
            string txt = File.ReadAllText(Path.Combine(folder, "warps.cfg"));
            StringAssert.StartsWith("1.000 2.000 3.000 0.5000 3 0 2 0.0000 0.0\n", txt, "the native warps.cfg lines (native 0.3.13: and the pitch and zoom of the PC spectator)");
            var e = new Director(s);
            e.Load();
            Assert.That(e.warps.Count == 2 && e.warps[0].hold == 3 && e.warps[0].trig == 2 && Length(e.warps[1].pose.p - new Vec3(-4, 1.5f, 6)) < 1e-3f, "read back");
            byte[] blob = d.Pack();
            Assert.That(blob.Length, Is.EqualTo(1 + 19 * 2), "19 bytes a camera (native packWarps)");
            var f = new Director(s);
            Assert.That(f.Unpack(blob) && f.warps.Count == 2 && f.warps[0].trans == 0, "a teammate's cameras unpack");
            blob[1] = 0; blob[2] = 0; blob[3] = 0x80; blob[4] = 0x7F;   // x = +infinity
            Assert.That(f.Unpack(blob), Is.False, "bad numbers are refused");
            Assert.That(f.warps.Count, Is.EqualTo(2), "and keep the cameras there were");
        }

        [Test]
        public void CrowdBurstsRiseAndFade()
        {
            var c = new CrowdStands();
            Assert.That(CrowdStands.SpotCount(0) == 40 && CrowdStands.SpotCount(1) == 40, "40 places in the stands for each end");
            for (int i = 0; i < 20; ++i) c.Burst(i % 2, 40);
            Assert.That(c.BurstCount, Is.EqualTo(400), "at most 400 bursts at a time");
            for (int i = 0; i < 300; ++i) c.Step(0.011f);
            Assert.That(c.BurstCount, Is.EqualTo(0), "each fades within its life (under 3 s)");
        }
    }
}
