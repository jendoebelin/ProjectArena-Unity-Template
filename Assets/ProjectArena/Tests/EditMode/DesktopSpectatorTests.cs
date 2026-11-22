// The PC spectator for streamers (task D2; native desktopControls / desktopPreset / eventCamera / desktopHud): the
// views, MY CAMERAS (the headset director's warps.cfg, with the PC's pitch and zoom as natively), the eyes cameras, the
// auto director, the fist bump's close view, the commands, the hub's title line, the view layers and SELFIE's round
// picture. No keyboard in a test run: the commands go in as the controls panel's buttons do.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectArena.Online;
using ProjectArena.Sim;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena.Tests
{
    public class DesktopSpectatorTests
    {
        string folder;
        AppState app;
        ArenaSession session;
        Director dir;
        DesktopSpectator d;
        readonly PlayerInput[] viewIn = { new PlayerInput(), new PlayerInput(), new PlayerInput(), new PlayerInput() };
        readonly bool[] valid = new bool[4];

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "pa_desk_test_" + System.Guid.NewGuid().ToString("N"));
            var g = new Game();
            g.Reset(true);
            g.match = MatchState.InProgress;   // (no fist bump going on: its close view would take over)
            app = new AppState { game = g, settings = new AppSettings(folder), lobby = false, spectating = true };
            app.settings.Load(g.tun);
            session = new ArenaSession(app);
            dir = new Director(session);
            d = new DesktopSpectator(app, dir);
            viewIn[0].head = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), -M.kPi * 0.5f), new Vec3(-7.5f, 1.6f, 0.2f));
            viewIn[1].head = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), M.kPi * 0.5f), new Vec3(7.5f, 1.6f, -0.2f));
            valid[0] = valid[1] = true;
        }

        // the PC spectator's new presets (TASKS.md E42, native 7240d49): 8 CORNER, 9 COURTSIDE, the ends from inside the glass
        [Test]
        public void CornerCourtsideAndTheEndsInside()
        {
            app.watchIp = 1;
            Assert.That(DesktopSpectator.ViewName.Length == 13 && DesktopSpectator.ViewName[8] == "CORNER" && DesktopSpectator.ViewName[9] == "COURTSIDE",
                        "8 CORNER and 9 COURTSIDE");
            Assert.That(DesktopSpectator.Mine, Is.EqualTo(20), "MY CAMERA is view 20 inside");
            Assert.That(System.Math.Abs(d.Preset(3).p.x + 9.25f) < 1e-5f && System.Math.Abs(d.Preset(4).p.x - 9.25f) < 1e-5f, "the ends from inside the glass");
            Assert.That(System.Math.Abs(d.Preset(9).p.y - 1.25f) < 1e-5f && System.Math.Abs(d.Preset(8).p.x + 11f) < 1e-5f, "COURTSIDE low at the side; CORNER high over the blue end's corner");
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        static List<SpecMatch> Matches(params uint[] ips) => ips.Select(ip => new SpecMatch { ip = ip, name = "ROOM " + ip }).ToList();
        Pose Frame(List<SpecMatch> ms, double t, float dt = 1f / 60f) => d.Controls(ms, app.game, viewIn, valid, t, dt);

        // THE FOLLOW VIEWS (TASKS.md E57, native 5bfc546): 10 TRACKING slides along the side with the play, 11 BALL CAM low
        // at the side glass, 12 UNDER FIRE over the shoulder of the player the ball flies at; the free camera from where it is
        [Test]
        public void TheFollowViews()
        {
            app.watchIp = 7;
            var ms = Matches(7);
            Ball b = app.game.pl[0].ball;   // (blue's ball, flying at orange)
            b.state = BallState.Flying; b.instigator = 0; b.pos = new Vec3(-5f, 1.4f, 0); b.vel = new Vec3(12f, 0, 0);
            d.Press(DesktopSpectator.Cmd.View + 10);
            double t = 0;
            Pose p0 = Frame(ms, t), p = p0;
            Assert.That(d.view == 10 && d.ViewLabel() == "TRACKING", "T: TRACKING");
            for (int i = 0; i < 60; ++i) { b.pos = b.pos + b.vel * (1f / 60f); p = Frame(ms, t += 1.0 / 60.0); }
            TestContext.Out.WriteLine($"TRACKING: x {p0.p.x:F2} -> {p.p.x:F2} (the ball {b.pos.x:F2}), z {p.p.z:F2}, y {p.p.y:F2}");
            Assert.That(p.p.x > p0.p.x + 3f && System.Math.Abs(p.p.z - 6.3f) < 1e-3f && System.Math.Abs(p.p.y - 2.7f) < 1e-3f,
                        "TRACKING: along the side at head height, sliding with the ball");
            d.Press(DesktopSpectator.Cmd.View + 12);
            Pose u = Frame(ms, t += 1.0 / 60.0);
            Vec3 f = u.q.Rotate(new Vec3(0, 0, -1));
            TestContext.Out.WriteLine($"UNDER FIRE: ({u.p.x:F2} {u.p.y:F2} {u.p.z:F2}) looking ({f.x:F2} {f.y:F2} {f.z:F2})");
            Assert.That(u.p.x > viewIn[1].head.p.x + 1.5f && u.p.y > viewIn[1].head.p.y && f.x < -0.5f,
                        "UNDER FIRE: behind and over the shoulder of the one the ball flies at, looking at the play");
            d.Press(DesktopSpectator.Cmd.View + 11);
            Pose bc = Frame(ms, t += 1.0 / 60.0);
            Assert.That(System.Math.Abs(bc.p.y - 1.35f) < 1e-3f && System.Math.Abs(bc.p.z - 3.3f) < 1e-3f && d.ViewLabel() == "BALL CAM", "B: BALL CAM, low at the side glass");
            d.Press(DesktopSpectator.Cmd.Free);
            Pose fr = Frame(ms, t += 1.0 / 60.0);
            Assert.That(d.view == 0 && M.Length(fr.p - bc.p) < 0.5f, "the free camera from where the follow view was");
            // FIREBALL BLITZ (native 3fccaf9): a thrown ball is a fireball there, and the views follow it
            var bz = new Game();
            bz.tun.experimental = true; bz.tun.expRoom = Room.Blitz; bz.tun.players = 4;
            bz.UseArena(Arena.Octagon, new List<CollisionTri>()); bz.ResetWithBots(0u);
            bz.match = MatchState.InProgress;
            Ball fb = bz.fire[2][1];
            fb.state = BallState.Flying; fb.instigator = 2; fb.pos = new Vec3(3f, 1.2f, 2f); fb.vel = new Vec3(-8f, 0, 0);
            d.Press(DesktopSpectator.Cmd.View + 10);
            Pose z = d.Controls(ms, bz, viewIn, valid, t += 1.0 / 60.0, 1f / 60f);
            for (int i = 0; i < 60; ++i) z = d.Controls(ms, bz, viewIn, valid, t += 1.0 / 60.0, 1f / 60f);
            Vec3 zf = z.q.Rotate(new Vec3(0, 0, -1)), toBall = M.Normalize(fb.pos - z.p);
            Assert.That(M.Dot(zf, toBall) > 0.8f, $"TRACKING follows a fireball (looking {M.Dot(zf, toBall):F2} toward it)");
        }

        [Test]
        public void TheViews()
        {
            Pose hub = d.Preset(1);
            Assert.That(hub.p.x, Is.EqualTo(-1.3f).Within(1e-4f), "watching nothing: the hub's view");
            app.watchIp = 5;
            Pose side = d.Preset(1);
            Assert.That(side.p.z == 7.3f && side.p.y == 2.7f, "SIDE: beside the court");
            Vec3 f = side.q.Rotate(new Vec3(0, 0, -1));
            Assert.That(f.z, Is.LessThan(-0.9f), "looking across it");
            Assert.That(d.Preset(3).p.x, Is.EqualTo(-9.25f), "BLUE END (inside the glass since native 7240d49)");
            Assert.That(d.Preset(4).p.x, Is.EqualTo(9.25f), "ORANGE END");
            Assert.That(d.Preset(5).p.y, Is.EqualTo(11f), "TOP");
            app.game.arenaKind = Arena.Corners;
            Assert.That(d.Preset(1).p.x, Is.EqualTo(-8.2f), "FOUR CORNERS: outside its glass");
            Assert.That(DesktopSpectator.ViewName.Length == 13 && DesktopSpectator.ViewName[6] == "BLUE'S EYES" && DesktopSpectator.ViewName[9] == "COURTSIDE" &&
                        DesktopSpectator.ViewName[10] == "TRACKING" && DesktopSpectator.ViewName[11] == "BALL CAM" && DesktopSpectator.ViewName[12] == "UNDER FIRE");
        }

        [Test]
        public void WatchingNextMatchesAndTheHub()
        {
            var ms = Matches(11, 22, 33);
            Frame(ms, 0);
            Assert.That(app.watchIp, Is.EqualTo(0u), "not at once");
            Frame(ms, 2.5);
            Frame(ms, 2.6);
            Assert.That(app.watchIp, Is.EqualTo(11u), "after a moment the first live match: a stream should show play");
            d.Press(DesktopSpectator.Cmd.Next); Frame(ms, 3);
            Assert.That(app.watchIp, Is.EqualTo(22u), "TAB: the next one");
            d.Press(DesktopSpectator.Cmd.Next); Frame(ms, 3.1);
            d.Press(DesktopSpectator.Cmd.Next); Frame(ms, 3.2);
            Assert.That(app.watchIp, Is.EqualTo(11u), "round again");
            d.Press(DesktopSpectator.Cmd.Hub); Frame(ms, 3.3);
            Frame(ms, 9); Frame(ms, 20);
            Assert.That(app.watchIp, Is.EqualTo(0u), "H: the hub, and it stays there");
        }

        [Test]
        public void MyCamerasAreTheHeadsetsCamerasWithTheirZoom()
        {
            app.watchIp = 7;
            d.Press(DesktopSpectator.Cmd.View + 3); Frame(Matches(7), 0);
            d.fov = 50f;
            d.Press(DesktopSpectator.Cmd.MySet + 0); Frame(Matches(7), 0.1);
            Assert.That(dir.warps.Count == 1 && d.view == DesktopSpectator.Mine && d.my == 0, "CTRL+1: MY CAMERA 1, showing");
            Assert.That(dir.warps[0].pose.p.x, Is.EqualTo(-9.25f).Within(1e-4f), "BLUE END's place");
            Assert.That(dir.warps[0].pitch, Is.LessThan(0f), "looking down at the court");
            Assert.That(dir.warps[0].fov, Is.EqualTo(50f));
            Assert.That(d.ViewLabel(), Is.EqualTo("MY CAMERA 1"));
            string[] line = File.ReadAllLines(Path.Combine(folder, "warps.cfg"))[0].Split(' ');
            Assert.That(line.Length, Is.EqualTo(9), "native's warps.cfg: x y z yaw hold trans trig pitch fov");
            var again = new Director(session);
            again.Load();
            Assert.That(again.warps.Count == 1 && Mathf.Abs(again.warps[0].fov - 50f) < 0.05f && again.warps[0].pitch < 0f, "read back");
            File.WriteAllText(Path.Combine(folder, "warps.cfg"), "1.000 2.000 3.000 0.5000 1 1 0\n");
            again.Load();
            Assert.That(again.warps.Count == 1 && again.warps[0].fov == 0f, "an older file (seven numbers) still loads");
            d.fov = 80f;
            d.Press(DesktopSpectator.Cmd.View + 1); Frame(Matches(7), 0.2);
            d.Press(DesktopSpectator.Cmd.MyGo + 0); Frame(Matches(7), 0.3);
            Assert.That(d.view == DesktopSpectator.Mine && d.fov == 50f, "SHIFT+1: back to it, its zoom too");
            d.Press(DesktopSpectator.Cmd.MyTrig); Frame(Matches(7), 0.4);
            Assert.That(dir.warps[0].trig, Is.EqualTo(1), "its trigger: ACTION");
            Assert.That(d.ViewLabel(), Is.EqualTo("MY CAMERA 1  ACTION"));
            d.Press(DesktopSpectator.Cmd.MyClear); Frame(Matches(7), 0.5);
            Assert.That(dir.warps.Count == 0 && d.view == 1, "cleared: SIDE");
        }

        [Test]
        public void TheEyesCameraAndTheFreeCamera()
        {
            app.watchIp = 7;
            d.Press(DesktopSpectator.Cmd.View + 6);
            Pose e = Frame(Matches(7), 0);
            Assert.That(d.eyesOf, Is.EqualTo(0), "BLUE'S EYES: blue's body left out");
            Assert.That(M.Length(e.p - viewIn[0].head.p), Is.LessThan(0.1f), "from blue's head");
            d.Press(DesktopSpectator.Cmd.Free);
            Pose fr = Frame(Matches(7), 0.1);
            Assert.That(d.view == 0 && d.eyesOf == -1, "F: the free camera");
            d.Press(DesktopSpectator.Cmd.View + 4); Frame(Matches(7), 0.2);
            d.Press(DesktopSpectator.Cmd.Free);
            fr = Frame(Matches(7), 0.3);
            Assert.That(fr.p.x, Is.EqualTo(9.25f).Within(1e-3f), "the free camera starts where the camera was");
        }

        [Test]
        public void TheAutoDirectorAndTheFistBump()
        {
            app.watchIp = 7;
            var ms = Matches(7);
            d.Press(DesktopSpectator.Cmd.Auto); Frame(ms, 0);
            Assert.That(d.auto && d.view == 1, "AUTO from SIDE");
            var seen = new List<int>();
            for (double t = 8.5; t < 60; t += 9.5) { Frame(ms, t); seen.Add(d.view); }
            Assert.That(seen.Take(5), Is.EqualTo(new[] { 6, 3, 2, 7, 4 }), "the views in turn, each player's eyes among them");
            d.Press(DesktopSpectator.Cmd.View + 5); Frame(ms, 61);
            Assert.That(d.auto, Is.False, "a camera picked by hand stops it");
            Pose before = Frame(ms, 62);
            app.game.match = MatchState.Meet;
            Pose p = before;
            for (int i = 1; i <= 40; ++i) p = Frame(ms, 62 + i / 60.0);
            Vec3 mid = (viewIn[0].head.p + viewIn[1].head.p) * 0.5f;
            Assert.That(M.Length(p.p - mid), Is.LessThan(2.2f), "the fist bump: close beside the two fists");
            Assert.That(M.Length(before.p - mid), Is.GreaterThan(5f), "(from TOP, far above)");
        }

        // AUTO ROTATION (TASKS.md E6, native 0.3.20): the streamer's list on the AUTO tab, its ticks, hold times and
        // order; AUTO plays it; kept in autocams.cfg
        [Test]
        public void AutoRotationTheStreamersList()
        {
            app.watchIp = 7;
            var ms = Matches(7);
            double t = 0;
            void Do(DesktopSpectator.Cmd c) { d.Press(c); Frame(ms, t += 0.05); }
            Frame(ms, t);
            Assert.That(d.autoList.Count == 12 && !d.autoCustom, "the presets in AUTO's own order, not the streamer's list yet");
            Assert.That(!File.Exists(Path.Combine(folder, "autocams.cfg")), "nothing saved until it is changed");
            for (int i = 0; i < d.autoList.Count; ++i)   // only SIDE and ORANGE END
                if (d.autoList[i].on && d.autoList[i].id != 1 && d.autoList[i].id != 4) Do(DesktopSpectator.Cmd.AutoTick + i);
            int r4 = AutoRotation.RowOf(d.autoList, false, 4);
            Do(DesktopSpectator.Cmd.AutoPick + r4);
            Do(DesktopSpectator.Cmd.AutoHold);
            Do(DesktopSpectator.Cmd.AutoHold);
            Assert.That(d.autoCustom && d.autoList[r4].hold == 2f && d.AutoRowHold(d.autoList[r4]) == "2 s", "ORANGE END: 9, 15, then 2 s");
            Do(DesktopSpectator.Cmd.AutoUp);
            Assert.That(d.autoSel == r4 - 1 && d.autoList[r4 - 1].id == 4, "MOVE UP: one row up, still chosen");
            Do(DesktopSpectator.Cmd.Auto);
            Assert.That(d.auto && d.view == 1, "AUTO from SIDE");
            var seen = new List<int>();
            foreach (double at in new[] { 9.0, 11.5, 21.0, 23.5 }) { t = at; Frame(ms, t); seen.Add(d.view); }
            Assert.That(seen, Is.EqualTo(new[] { 4, 1, 4, 1 }), "ORANGE END 2 s, SIDE 9 s, in turn");
            var d2 = new DesktopSpectator(app, dir);
            d2.Controls(ms, app.game, viewIn, valid, t, 1f / 60f);
            Assert.That(d2.autoCustom && AutoRotation.Same(d2.autoList, d.autoList), "kept in autocams.cfg");
        }

        [Test]
        public void ThePicturesCommands()
        {
            for (int i = 0; i < 4; ++i) d.Apply(DesktopSpectator.Cmd.Layer, new List<SpecMatch>(), app.game, 0);
            Assert.That(d.layer, Is.EqualTo(0), "V: four view layers round");
            d.Apply(DesktopSpectator.Cmd.LayerBack, new List<SpecMatch>(), app.game, 0);
            Assert.That(DesktopSpectator.LayerName[d.layer], Is.EqualTo("KEY"));
            d.Apply(DesktopSpectator.Cmd.WinStyle, new List<SpecMatch>(), app.game, 0);
            Assert.That(DesktopSpectator.WinName[d.winStyle], Is.EqualTo("WINNER"));
            d.Apply(DesktopSpectator.Cmd.Selfie, new List<SpecMatch>(), app.game, 0);
            Assert.That(app.settings.selfieOn, "C: SELFIE on");
            var back = new AppSettings(folder);
            back.Load(new Tunables());
            Assert.That(back.selfieOn && back.selfieCorner == 3 && back.selfieSize == 1 && back.selfieRound, "saved, with native's corner (bottom right), size and shape");
            Assert.That(d.Status(new List<SpecMatch>()), Does.Contain("Selfie: bottom right, medium, round"));
        }

        [Test]
        public void TheHubsTitleLineCountsLiveMatchesAndRoomsWaitingApart()
        {
            Assert.That(DesktopSpectator.HubLine(0, 0), Is.EqualTo("NO LIVE MATCHES YET  -  THEY APPEAR HERE WHEN SOMEONE PLAYS"));
            Assert.That(DesktopSpectator.HubLine(1, 0), Is.EqualTo("1 LIVE MATCH  -  TAB TO WATCH"));
            Assert.That(DesktopSpectator.HubLine(2, 1), Is.EqualTo("2 LIVE MATCHES, 1 ROOM WAITING  -  TAB TO WATCH"));
            Assert.That(DesktopSpectator.HubLine(0, 2), Is.EqualTo("2 ROOMS WAITING FOR PLAYERS  -  TAB TO WATCH"));
        }

        [Test]
        public void TheViewLayersAndTheRoundSelfie()
        {
            // NO WALLS: the outline instead of the glass; MINIMAL: no stadium, reflections or glow layers; KEY: nothing
            Assert.That(RoomPassthrough.Shows("court_outline", false, true, false) && !RoomPassthrough.Shows("arena", false, true, false), "NO WALLS");
            foreach (string n in new[] { "environment", "floor_reflect", "floor_reflect_env", "court_fx", "stadium_fx" })
                Assert.That(RoomPassthrough.Shows(n, true, true, false, minimal: true), Is.False, "MINIMAL: no " + n);
            Assert.That(RoomPassthrough.Shows("floor", true, true, false, minimal: true) && RoomPassthrough.Shows("court_pulse", true, true, false, minimal: true), "MINIMAL keeps the floor and the lines");
            foreach (string n in new[] { "arena", "floor", "court_outline", "court_pulse" })
                Assert.That(RoomPassthrough.Shows(n, true, true, true, true, noCourt: true), Is.False, "KEY: no " + n);
            var px = new Color32[64 * 48];
            for (int i = 0; i < px.Length; ++i) px[i] = new Color32(200, 100, 50, 255);
            DesktopSpectator.RoundMask(px, 64, 48);
            Assert.That(px[24 * 64 + 32].a, Is.EqualTo(255), "the middle shows");
            Assert.That(px[0].a, Is.EqualTo(0), "the corners don't");
            Assert.That(px[24 * 64 + 2].a, Is.EqualTo(0), "a circle as tall as the picture: the sides outside it");
        }
    }
}
