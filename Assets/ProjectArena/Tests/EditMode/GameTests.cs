// The native game tests (app/tests/test_game.cpp, native commit 0eea0e7) ported 1:1 to Unity's test runner, run
// against the real open arena collision. One test per native group, with the native checks and messages; each group
// also checks it ran as many checks as the native one (the native baseline: 144 checks, 44 of them in the avatar groups,
// which are in GameTests.Avatar.cs).
// Run: Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/tests.xml
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        const string ColPath = "Assets/ProjectArena/Art/Open/Data/arena.col.bytes";
        static List<CollisionTri> s_tris;
        static List<CollisionTri> Tris => s_tris ??= ArenaCollision.Load(File.ReadAllBytes(ColPath));

        // the native CHECK: counts, prints ok / FAIL, never stops the group
        sealed class Checks
        {
            readonly int expected;
            readonly List<string> failed = new List<string>();
            int count;

            public Checks(string group, int expected)
            {
                this.expected = expected;
                TestContext.Out.WriteLine($"[{group}]");
            }

            public void C(bool c, string msg)
            {
                ++count;
                if (!c) failed.Add(msg);
                TestContext.Out.WriteLine((c ? "  ok   " : "  FAIL ") + msg);
            }

            public void Done()
            {
                Assert.That(failed, Is.Empty, "failed: " + string.Join("; ", failed));
                Assert.That(count, Is.EqualTo(expected), "checks run (the native group has " + expected + ")");
            }
        }

        static readonly Quat kFaceP0 = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(-90));   // -Z -> +X
        static readonly Quat kFaceP1 = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(90));    // -Z -> -X

        static Game NewGame()
        {
            var g = new Game();
            g.SetArena(Tris);
            return g;
        }

        static PlayerInput Standing(Game g, int p)
        {
            var @in = new PlayerInput { headTracked = true };
            Quat face = p == 0 ? kFaceP0 : kFaceP1;
            @in.head = new Pose(face, g.tun.start[p] + new Vec3(0, 1.65f, 0));
            for (int h = 0; h < 2; ++h)
            {
                @in.hand[h].tracked = true;
                @in.hand[h].grip = new Pose(face, g.tun.start[p] + new Vec3(p == 0 ? 0.2f : -0.2f, 1.0f, (h == 0 ? -0.3f : 0.3f) * (p == 0 ? 1f : -1f)));
            }
            return @in;
        }

        static bool HasEvent(List<GameEvent> ev, EventType t, int player = -2)
        {
            foreach (var e in ev) if (e.type == t && (player == -2 || e.player == player)) return true;
            return false;
        }

        static void Step(Game g, PlayerInput[] @in) => g.Step(1f / 72, new[] { @in[0].Clone(), @in[1].Clone() });

        static void Run(Game g, PlayerInput[] @in, float seconds, List<GameEvent> log = null)
        {
            for (float t = 0; t < seconds; t += 1f / 72)
            {
                Step(g, @in);
                log?.AddRange(g.events);
            }
        }

        static void GrabBall(Game g, PlayerInput[] @in, int h = 1)
        {
            Vec3 off = @in[0].hand[h].grip.q.Rotate(g.tun.launcherOffset);
            @in[0].hand[h].grip.p = g.pl[0].ball.pos - off;
            @in[0].hand[h].trigger = 1;
            Step(g, @in);
        }

        static void SwingAndRelease(Game g, PlayerInput[] @in, Vec3 dir, float speed, List<GameEvent> log, int h = 1)
        {
            for (int i = 0; i < 15; ++i)
            {
                @in[0].hand[h].grip.p += dir * (speed / 72);
                Step(g, @in);
                log.AddRange(g.events);
            }
            @in[0].hand[h].trigger = 0;
            Step(g, @in);
            log.AddRange(g.events);
        }

        // a passive human opponent standing at the far end
        static Game MakeGame(PlayerInput[] @in, bool advanced = false)
        {
            var g = NewGame(); g.tun.advanced = advanced; g.tun.meetTime = 0; g.Reset(false);   // no centre meeting
            @in[0] = Standing(g, 0); @in[1] = Standing(g, 1);
            Run(g, @in, 3.6f);
            return g;
        }

        // fire player 1's ball straight at a point from 0.6 m in front of player 1
        static void FireOpponentBall(Game g, Vec3 target, float speed = 10f)
        {
            Ball b = g.pl[1].ball;
            b.pos = g.tun.start[1] + new Vec3(-0.6f, 1.6f, 0);
            b.state = BallState.Flying;
            b.vel = Normalize(target - b.pos) * speed;
            b.speedLimit = speed;
            b.instigator = 1;
            b.scored = false;
            b.spin = default;
        }

        [Test]
        public void ArenaCollisionLoaded()
        {
            var c = new Checks("arena collision", 1);
            TestContext.Out.WriteLine($"loaded {Tris.Count} collision tris");
            c.C(Tris.Count > 100, "arena collision loaded");
            c.Done();
        }

        [Test]
        public void TuningShieldBackOfHandPose()
        {
            var c = new Checks("tuning: the shield's back-of-hand pose", 3);
            var g = new Game();
            g.tun.shieldPlacement = 1;
            Pose s1 = g.ShieldLocal(1), s0 = g.ShieldLocal(0);
            // the owner's export (2026-10-19): pitch -20, yaw 19.1, roll -90.6 = yaw * pitch * roll about the grip axes
            Quat qr = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(19.1f)) * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(-20)) * Quat.AxisAngle(new Vec3(0, 0, 1), Deg2Rad(-90.6f));
            bool Same(Quat a, Quat b) => MathF.Abs(MathF.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w) - 1) < 1e-5f;
            c.C(Same(s1.q, qr) && Length(s1.p - new Vec3(0.076f, -0.024f, -0.122f)) < 1e-6f, "default right shield: the owner's tuned pose");
            Vec3 Mx(Vec3 v) { v.x = -v.x; return v; }
            var u = new Vec3(0.1f, 0.2f, -0.3f);
            c.C(Length(s0.q.Rotate(Mx(u)) - Mx(s1.q.Rotate(u))) < 1e-5f && Length(s0.p - Mx(s1.p)) < 1e-6f, "default left shield: its mirror image");
            g.tun.shieldBackRot = new Vec3(12, -30, -75);
            Pose t1 = g.ShieldLocal(1), t0 = g.ShieldLocal(0);
            var v = new Vec3(0.1f, 0.2f, -0.3f);
            c.C(Length(t0.q.Rotate(Mx(v)) - Mx(t1.q.Rotate(v))) < 1e-5f, "a tuned shield stays mirrored between the hands");
            c.Done();
        }

        [Test]
        public void MatchStart()
        {
            var c = new Checks("match start", 4);
            var g = NewGame(); g.Reset(true);
            var @in = new[] { Standing(g, 0), new PlayerInput() };
            Run(g, @in, 0.1f);
            c.C(g.match == MatchState.Countdown, "countdown begins");
            Run(g, @in, 3.5f);
            c.C(g.match == MatchState.InProgress, "match in progress after countdown");
            c.C(MathF.Abs(g.roundClock - (180f - 3.5f + 3.0f + 0.1f)) < 1.0f || g.roundClock > 170f, "Basic: 3 minute match clock");
            c.C(Length(g.pl[0].ball.pos - g.IdlePoint(0)) < 0.05f, "idle ball held by damped spring");
            c.Done();
        }

        [Test]
        public void FistBumpStart()
        {
            var c = new Checks("fist bump start (two people)", 16);
            var g = NewGame(); g.Reset(false);
            var @in = new[] { Standing(g, 0), Standing(g, 1) };
            Run(g, @in, 0.1f);
            c.C(g.match == MatchState.Meet, "two people meet in the centre before the match");
            // the fronts of the two visible fists 2 cm apart, as players bump in the headset (the grips are ~15 cm apart;
            // the first version measured the grips, the second a point 13.7 cm out, past the real knuckles)
            void PlaceFist(PlayerInput pi, Vec3 at) => pi.hand[1].grip.p = at - pi.hand[1].grip.q.Rotate(g.tun.fistFront);
            @in[0].hand[1].trigger = 0.3f; PlaceFist(@in[0], new Vec3(-0.01f, 1.2f, 0f));
            @in[1].hand[1].trigger = 0.3f; PlaceFist(@in[1], new Vec3(0.01f, 1.2f, 0f));
            c.C(Length(@in[0].hand[1].grip.p - @in[1].hand[1].grip.p) > 0.12f, "(test setup: fists front to front, grips 15 cm apart)");
            Run(g, @in, 0.2f);
            c.C(g.match == MatchState.Meet, "fists together, neither trigger pulled: no bump");
            // the meeting point (owner, 2026-10-28): one offers, the other meets it there
            PlaceFist(@in[0], new Vec3(-0.30f, 1.2f, 0f));
            @in[1].hand[1].trigger = 1;   // orange offers: a closed fist held in its ring
            Run(g, @in, 0.3f);
            c.C(g.MeetLit(1, 1) && !g.MeetLit(0, 1) && g.match == MatchState.Meet, "a closed fist in its ring lights it; nothing yet");
            var log = new List<GameEvent>();
            PlaceFist(@in[0], new Vec3(-0.01f, 1.2f, 0f));   // blue meets it (trigger half pulled: one trigger held is enough)
            Run(g, @in, 0.05f, log);
            c.C(HasEvent(log, EventType.FistBump) && g.match == MatchState.Meet, "fist bump (one trigger held): confirmed first (buzz, sound)");
            log.Clear();
            Run(g, @in, g.tun.bumpPause + 0.05f, log);
            int bumps = 0;
            foreach (var e in log) bumps += e.type == EventType.FistBump ? 1 : 0;
            c.C(g.match == MatchState.Countdown && bumps == 0, "... then the countdown, once (holding the fists together does not repeat it)");
            bool named = true;
            for (int t = 0; t <= (int)EventType.RingClose; ++t) named &= Game.EventName((EventType)t) != "?";
            c.C(named, "every event has a name (logs and sounds read it: a missing one crashed both headsets)");
            var g2 = NewGame(); g2.Reset(false);
            var in2 = new[] { Standing(g2, 0), Standing(g2, 1) };
            Run(g2, in2, 0.1f);
            Run(g2, in2, g2.tun.meetTime + 0.1f);
            c.C(g2.match == MatchState.Countdown, "no bump: the countdown starts by itself");
            var g3 = NewGame(); g3.Reset(true);
            var in3 = new[] { Standing(g3, 0), new PlayerInput() };
            Run(g3, in3, 0.1f);
            c.C(g3.match == MatchState.Countdown, "against the bot: no meeting");
            {   // nobody can force the start: away from the meeting point, or a fist slammed in before either was offered
                var g4 = NewGame(); g4.Reset(false);
                var in4 = new[] { Standing(g4, 0), Standing(g4, 1) };
                Run(g4, in4, 0.1f);
                void Place(PlayerInput pi, Vec3 at) => pi.hand[1].grip.p = at - pi.hand[1].grip.q.Rotate(g4.tun.fistFront);
                in4[0].hand[1].trigger = in4[1].hand[1].trigger = 1;
                Place(in4[0], new Vec3(-0.01f, 1.2f, 1.2f)); Place(in4[1], new Vec3(0.01f, 1.2f, 1.2f));   // fists together, off to the side
                var log4 = new List<GameEvent>();
                Run(g4, in4, 1.0f, log4);
                c.C(!HasEvent(log4, EventType.FistBump), "fists together away from the meeting point: no bump");
                in4[0].hand[1].trigger = in4[1].hand[1].trigger = 0;
                Place(in4[0], new Vec3(-0.30f, 1.2f, 0f)); Place(in4[1], new Vec3(0.01f, 1.2f, 0f));   // orange's fist in its ring, not closed
                Run(g4, in4, 0.3f, log4);
                in4[0].hand[1].trigger = 1;
                Place(in4[0], new Vec3(-0.01f, 1.2f, 0f));   // blue slams a closed fist into it at once
                log4.Clear();
                Run(g4, in4, 0.1f, log4);
                c.C(!HasEvent(log4, EventType.FistBump), "a fist slammed into one that was not offered: no bump");
            }
            {   // the bot: meets a fist you offer; offers its own when you don't; never reaches for a fist elsewhere
                bool MeetBot(int mode, out float took)
                {
                    var gb = NewGame(); gb.tun.botMeet = true; gb.tun.botLevel = 2; gb.Reset(true);
                    var pin = new[] { Standing(gb, 0), new PlayerInput() };
                    Quat fq = kFaceP0;
                    void PlaceAt(Vec3 at) => pin[0].hand[1].grip = new Pose(fq, at - fq.Rotate(gb.tun.fistFront));
                    took = -1;
                    bool bumped = false;
                    for (int i = 0; i < 72 * 8 && !bumped; ++i)
                    {
                        float t = i / 72f;
                        pin[0].head.p = new Vec3(-gb.tun.meetSpot, 1.65f, 0);   // standing at its spot in the centre
                        if (mode == 0) { pin[0].hand[1].trigger = 1; PlaceAt(gb.MeetRing(0) + new Vec3(0.03f, 0, 0)); }   // offer
                        else if (mode == 1)
                        {   // wait, then bring the fist to the bot's offered one
                            Vec3 bf = gb.pl[1].@in.hand[0].grip.Transform(gb.tun.fistFront), bf1 = gb.pl[1].@in.hand[1].grip.Transform(gb.tun.fistFront);
                            bool lit0 = gb.MeetLit(1, 0), lit1 = gb.MeetLit(1, 1);
                            pin[0].hand[1].trigger = 1;
                            if (lit0 || lit1) PlaceAt((lit1 ? bf1 : bf) + new Vec3(-0.02f, 0, 0)); else PlaceAt(new Vec3(-0.6f, 1.0f, 0.3f));
                        }
                        else { pin[0].hand[1].trigger = 1; PlaceAt(new Vec3(-0.05f, 1.2f, 1.0f)); }   // a fist held out, off to the side
                        Step(gb, pin);
                        if (gb.match == MatchState.Meet || HasEvent(gb.events, EventType.FistBump)) took = t;
                        bumped = HasEvent(gb.events, EventType.FistBump);
                    }
                    return bumped;
                }
                c.C(MeetBot(0, out float took0) && took0 < 2.5f, "the bot meets a fist you offer in your ring");
                c.C(MeetBot(1, out _), "it offers its own fist when you don't, and you can meet it");
                c.C(!MeetBot(2, out _), "it never reaches for a fist away from the meeting point");
                {   // its fist held out, waiting: its body holds still (a sway folded the arm into the torso, 2026-10-29)
                    var gb = NewGame(); gb.tun.botMeet = true; gb.tun.botLevel = 2; gb.tun.meetTime = 30f; gb.Reset(true);
                    var pin = new[] { Standing(gb, 0), new PlayerInput() };
                    pin[0].head.p = new Vec3(-gb.tun.meetSpot, 1.65f, 0);
                    float lo = 9f, hi = -9f;
                    for (int i = 0; i < 72 * 8; ++i)
                    {
                        Step(gb, pin);
                        if (i > 72 * 4) { lo = Min(lo, gb.pl[1].@in.head.p.z); hi = Max(hi, gb.pl[1].@in.head.p.z); }
                    }
                    c.C(gb.MeetLit(1, 0) || gb.MeetLit(1, 1), "(it is offering its fist)");
                    c.C(hi - lo < 0.01f, "while it offers, its body does not sway");
                }
            }
            c.Done();
        }

        [Test]
        public void ResumeAfterHandoff()
        {
            var c = new Checks("host handoff: a match taken over goes on from where it was (native 0.3.13)", 3);
            var g = NewGame(); g.tun.advanced = false; g.Reset(true);
            g.pl[0].isBot = true;
            var @in = new[] { new PlayerInput(), new PlayerInput() };
            for (int i = 0; i < 90 * 20 && g.match != MatchState.InProgress; ++i) g.Step(1f / 90f, @in);
            g.pl[0].score = 5; g.pl[1].score = 4;
            var b = g.pl[0].ball;   // (the port has no serve(): a ball in the air by hand)
            b.state = BallState.Flying; b.pos = g.tun.start[0] + new Vec3(1f, 1.3f, 0); b.vel = new Vec3(8f, 0, 0); b.instigator = 0;
            float clock = g.roundClock;
            g.ResumeAfterHandoff();
            c.C(g.pl[0].ball.state == BallState.Spawning, "a ball in the air goes back to its thrower");
            c.C(g.match == MatchState.Countdown && g.pl[0].score == 5 && g.pl[1].score == 4 && g.roundClock == clock,
                "a match being played counts down again, the score and the clock as they were");
            for (int i = 0; i < 90 * 6 && g.match == MatchState.Countdown; ++i) g.Step(1f / 90f, @in);
            c.C(g.match == MatchState.InProgress && g.pl[0].score == 5 && g.roundClock <= clock && g.roundClock > clock - 1f,
                "then it plays on: nothing reset");
            c.Done();
        }

        [Test]
        public void ReadyUp()
        {
            var c = new Checks("READY UP: nothing starts until every person in the tank pressed A (native 0.3.14)", 1);
            var g = NewGame(); g.tun.readyUp = true; g.Reset(false);
            var @in = new[] { Standing(g, 0), Standing(g, 1) };
            void RunA(float secs, bool a0, bool a1)
            {
                @in[0].buttonA = a0; @in[1].buttonA = a1;
                Run(g, @in, secs);
            }
            RunA(3f, false, false);
            bool waited = g.match == MatchState.WaitingForPlayers;
            RunA(0.2f, true, false);
            RunA(3f, false, false);
            bool waitedForOne = g.match == MatchState.WaitingForPlayers && g.pl[0].ready && !g.pl[1].ready;
            RunA(0.2f, false, true);
            RunA(0.3f, false, false);
            c.C(waited && waitedForOne && g.match == MatchState.Meet, "nothing starts until everyone is READY, then the meeting");
            c.Done();
        }

        [Test]
        public void LobbyTwoBots()
        {
            var c = new Checks("lobby: two bots play each other", 2);
            var g = NewGame(); g.Reset(true);
            g.pl[0].isBot = true;
            var @in = new[] { Standing(g, 0), new PlayerInput() };
            var log = new List<GameEvent>();
            Run(g, @in, 90f, log);
            int[] throws = { 0, 0 };
            int nan = 0;
            foreach (var e in log) if (e.type == EventType.Throw && e.player >= 0) ++throws[e.player];
            for (int p = 0; p < 2; ++p) nan += !float.IsFinite(g.pl[p].ball.pos.x) || !float.IsFinite(g.pl[p].@in.head.p.x) ? 1 : 0;
            TestContext.Out.WriteLine($"  throws {throws[0]} / {throws[1]}");
            c.C(throws[0] > 3 && throws[1] > 3 && nan == 0, "both bots throw (the lobby's match runs by itself)");
            c.C(g.match != MatchState.Meet, "bots never wait for a fist bump");
            c.Done();
        }

        [Test]
        public void ShieldNeedsHeldChargedBall()
        {
            var c = new Checks("shield needs a held, charged ball", 6);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            @in[0].hand[0].trigger = 1;   // pull the trigger on the empty hand
            Run(g, @in, 0.1f);
            c.C(!g.pl[0].hands[0].shieldUp, "empty hand + trigger does NOT raise a shield");
            @in[0].hand[0].trigger = 0;
            GrabBall(g, @in, 1);
            Run(g, @in, 0.05f);
            c.C(g.pl[0].ball.state == BallState.Held, "trigger near ball grabs it");
            c.C(g.pl[0].hands[1].shieldUp, "shield forms on the arm holding the charged ball");
            c.C(!g.pl[0].hands[0].shieldUp, "no shield on the free hand");
            c.C(g.pl[0].hands[0].knuckleUp, "Basic: free hand has a knuckle guard");
            c.C(g.pl[0].hands[1].knuckleUp, "Basic: knuckle guard stays on the hand holding the ball (permanent)");
            c.Done();
        }

        [Test]
        public void SlowReleaseDropsTheBall()
        {
            var c = new Checks("slow release drops the ball", 3);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            GrabBall(g, @in);
            var log = new List<GameEvent>();
            SwingAndRelease(g, @in, new Vec3(1, 0, 0), 0.2f, log);
            c.C(HasEvent(log, EventType.ThrowFail), "release below MinSpeedThreshold is a drop");
            c.C(g.pl[0].ball.state == BallState.Idle, "dropped ball floats back to the spawner");
            c.C(!g.pl[0].hands[1].shieldUp, "dropping the ball dissipates the shield");
            c.Done();
        }

        [Test]
        public void SwingThroughIdleBall()
        {
            var c = new Checks("swing through the idle ball with the trigger held", 2);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            const int h = 1;
            Vec3 off = @in[0].hand[h].grip.q.Rotate(g.tun.catcherOffset);
            @in[0].hand[h].trigger = 1;                                   // trigger pulled early, hand still away
            @in[0].hand[h].grip.p = g.pl[0].ball.pos - off + new Vec3(0, 0, 0.6f);
            Run(g, @in, 0.1f);
            c.C(g.pl[0].ball.state == BallState.Idle, "trigger held away from the ball: nothing grabbed");
            var log = new List<GameEvent>();
            for (int i = 0; i < 40 && g.pl[0].ball.state != BallState.Held; ++i)
            {   // sweep through the ball
                @in[0].hand[h].grip.p += new Vec3(0, 0, -0.02f);
                Step(g, @in);
                log.AddRange(g.events);
            }
            c.C(g.pl[0].ball.state == BallState.Held && HasEvent(log, EventType.Grab), "sweeping through it grabs it");
            c.Done();
        }

        // native 0.3.19 (test_game.cpp "the rising ball can be taken off the spawner")
        [Test]
        public void RisingBallCanBeGrabbed()
        {
            var c = new Checks("the rising ball can be taken off the spawner", 4);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            const int h = 1;
            g.RespawnBall(0);
            Vec3 off = @in[0].hand[h].grip.q.Rotate(g.tun.catcherOffset);
            @in[0].hand[h].trigger = 1;
            Run(g, @in, 0.3f);
            @in[0].hand[h].grip.p = g.pl[0].ball.pos - off;
            Step(g, @in);
            c.C(g.pl[0].ball.state == BallState.Spawning, "under the floor: not grabbable yet");
            var log = new List<GameEvent>();
            while (g.pl[0].ball.state == BallState.Spawning && g.pl[0].ball.timer > g.tun.spawnRiseTime * 0.5f)
            {
                @in[0].hand[h].grip.p = g.pl[0].ball.pos - off;
                Step(g, @in);
                log.AddRange(g.events);
            }
            c.C(g.pl[0].ball.state == BallState.Held && HasEvent(log, EventType.Grab), "grabbed while it rises");
            c.C(!HasEvent(log, EventType.Spawn), "... before it reached the top");
            c.C(g.pl[0].hands[h].shieldUp, "and it's charged: the shield is up");
            c.Done();
        }

        // native 0.3.19 (test_game.cpp "CATCHES ONLINE: a catch is judged as the player saw the ball")
        [Test]
        public void CatchesOnlineJudgedAsSeen()
        {
            var c = new Checks("CATCHES ONLINE: a catch is judged as the player saw the ball", 3);
            var held = new bool[3];
            for (int pass = 0; pass < 3; ++pass)
            {   // 0: seen on time, 1: seen 0.12 s late, 2: late but the rule off
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.lateCatch = pass != 2;
                Ball b = g.pl[0].ball;
                Vec3 head = @in[0].head.p;
                b.state = BallState.Returning; b.instigator = 0;
                b.pos = head + new Vec3(3f, -0.3f, 0f); b.vel = new Vec3(-10f, 0f, 0f); b.speedLimit = 10f;
                @in[0].hand[0].trigger = @in[0].hand[1].trigger = 0;
                Vec3 then = default;
                for (int i = 0; i < 11; ++i)
                {
                    Step(g, @in);
                    if (i == 2) then = b.pos;
                }
                const int h = 1;
                Vec3 off = @in[0].hand[h].grip.q.Rotate(g.tun.catcherOffset);
                @in[0].hand[h].grip.p = then - off;
                @in[0].hand[h].trigger = 1;
                g.seenLate[0] = pass == 0 ? 0f : 0.12f;
                Step(g, @in);
                held[pass] = b.state == BallState.Held;
            }
            c.C(!held[0], "seen on time: a hand where the ball was 0.12 s ago doesn't catch it");
            c.C(held[1], "seen 0.12 s late (the round trip): the same hand catches it");
            c.C(!held[2], "... and not with the rule off");
            c.Done();
        }

        // native 0.3.19 (test_game.cpp "CATCHES ONLINE: your own ball's hit waits for your catch")
        [Test]
        public void CatchesOnlineHitWaits()
        {
            var c = new Checks("CATCHES ONLINE: your own ball's hit waits for your catch", 3);
            bool[] hitAtOnce = new bool[3], waited = new bool[3], hitLater = new bool[3], held = new bool[3];
            for (int pass = 0; pass < 3; ++pass)
            {   // 0: seen on time; 1: 0.1 s late, no catch; 2: 0.1 s late, caught during the wait
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[0].ball;
                Vec3 head = @in[0].head.p;
                b.state = BallState.Returning; b.instigator = 0; b.timer = 1f;
                b.pos = head + new Vec3(1.5f, 0f, 0f); b.vel = new Vec3(-10f, 0f, 0f); b.speedLimit = 10f;
                @in[0].hand[0].trigger = @in[0].hand[1].trigger = 0;
                g.seenLate[0] = pass == 0 ? 0f : 0.1f;
                var log = new List<GameEvent>();
                int arrived = -1;
                for (int i = 0; i < 72 && b.state != BallState.Dead && b.state != BallState.Held; ++i)
                {
                    if (pass == 2 && b.hitWaitAt >= 0f)
                    {
                        Vec3 off = @in[0].hand[1].grip.q.Rotate(g.tun.catcherOffset);
                        @in[0].hand[1].grip.p = b.pos - off;
                        @in[0].hand[1].trigger = 1;
                    }
                    Step(g, @in);
                    log.AddRange(g.events);
                    if (arrived < 0 && (b.hitWaitAt >= 0f || HasEvent(log, EventType.HitPlayer))) arrived = i;
                    if (b.hitWaitAt >= 0f) waited[pass] = true;
                    if (arrived >= 0 && i == arrived && HasEvent(log, EventType.HitPlayer)) hitAtOnce[pass] = true;
                }
                hitLater[pass] = !hitAtOnce[pass] && HasEvent(log, EventType.HitPlayer);
                held[pass] = b.state == BallState.Held;
            }
            c.C(hitAtOnce[0] && !waited[0], "seen on time: your own ball hits you at once, as before");
            c.C(waited[1] && hitLater[1] && !held[1], "seen late, no catch: it waits at you, then it's the point");
            c.C(waited[2] && held[2] && !hitLater[2], "seen late, caught during the wait: the catch wins, no point");
            c.Done();
        }

        [Test]
        public void ShortThrowComesBack()
        {
            var c = new Checks("a short throw comes back to the spawner", 3);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            GrabBall(g, @in);
            var log = new List<GameEvent>();
            SwingAndRelease(g, @in, new Vec3(1, 0, 0), 0.7f, log);              // just above the drop threshold: a weak throw
            c.C(HasEvent(log, EventType.Throw), "weak release is still a throw");
            Run(g, @in, 0.2f, log);
            c.C(g.pl[0].ball.state == BallState.Idle, "slow and short of the foul line: back to the spawner");
            Run(g, @in, 1.5f, log);
            c.C(Length(g.pl[0].ball.pos - g.IdlePoint(0)) < 0.10f, "it settles on the spawner again");
            c.Done();
        }

        [Test]
        public void WarmUpThrows()
        {
            var c = new Checks("warm-up throws while waiting for an opponent", 5);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            @in[1].headTracked = false;   // nobody at the other end yet
            g.Reset(false);
            Run(g, @in, 2.0f);
            c.C(g.match == MatchState.WaitingForPlayers, "alone: still waiting for an opponent");
            GrabBall(g, @in);
            var log = new List<GameEvent>();
            SwingAndRelease(g, @in, new Vec3(1, 0, 0), 5.0f, log);
            c.C(HasEvent(log, EventType.Throw), "a fast release throws while waiting (warm-up)");
            Run(g, @in, 4.0f, log);
            c.C(g.pl[0].score == 0 && g.pl[1].score == 0 && g.pl[1].strikesAgainst == 0, "warm-up throws score nothing");
            c.C(g.pl[0].ball.state == BallState.Idle || g.pl[0].ball.state == BallState.Held, "the ball comes back to its spawner");
            @in[1].headTracked = true;
            Run(g, @in, 0.2f);
            c.C(g.match != MatchState.WaitingForPlayers, "an opponent arrives: the match starts as before");
            c.Done();
        }

        [Test]
        public void DirectHit()
        {
            var c = new Checks("direct hit", 4);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            GrabBall(g, @in);
            var log = new List<GameEvent>();
            SwingAndRelease(g, @in, Normalize(g.ChestPoint(1) - g.LauncherPoint(0, 1)), 5.0f, log);
            c.C(HasEvent(log, EventType.Throw), "fast release launches projectile");
            float sp = 0; foreach (var e in log) if (e.type == EventType.Throw) sp = e.value;
            c.C(sp > 9f && sp < 12f, "5 m/s hand speed -> ~10.8 m/s ball (recovered 0-14 m/s mapping)");
            Run(g, @in, 2.5f, log);
            c.C(HasEvent(log, EventType.HitPlayer, 1), "projectile hits opponent");
            c.C(g.pl[0].score == 1, "thrower scores a point");
            c.Done();
        }

        [Test]
        public void MissFarWallBounceOwnBackWallRespawn()
        {
            var c = new Checks("miss: far wall bounce, own back wall respawn", 4);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            @in[1].head.p.z = 2.0f;   // opponent dodged
            GrabBall(g, @in);
            var log = new List<GameEvent>();
            SwingAndRelease(g, @in, Normalize(new Vec3(1, 0.02f, -0.03f)), 6.0f, log);
            for (int i = 0; i < 80 && !HasEvent(log, EventType.Returned, 0); ++i) Run(g, @in, 0.05f, log);
            @in[0].head.p.z = 1.2f;   // owner side-steps the slow return as soon as it starts
            c.C(HasEvent(log, EventType.Returned, 0), "hitting the opponent's back wall starts the return");
            Run(g, @in, 6.0f, log);   // return flight + the ~2.3 s respawn sequence
            c.C(g.pl[0].ball.state == BallState.Idle && Length(g.pl[0].ball.pos - g.IdlePoint(0)) < 0.1f,
                "returning ball flies past into the owner's back wall and respawns at the spawner");
            c.C(g.pl[0].score == 0 && g.pl[1].score == 0, "a dodged return scores nothing");
            c.C(g.pl[0].score == 0, "no score on a miss");
            c.Done();
        }

        [Test]
        public void ShieldBlockDrainsChargeThrowRecharges()
        {
            var c = new Checks("shield block drains charge; throw past foul line recharges", 5);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            GrabBall(g, @in, 1);
            // raise the shield arm in front of the face: plate normal (+Y grip) toward the ball
            Quat q = FromTo(new Vec3(0, 1, 0), new Vec3(1, 0, 0));
            Vec3 face = g.pl[0].@in.head.p;
            // place the grip so the shield plate (current placement, normal = local +Y) faces the ball 0.45 m ahead
            Pose sl = g.ShieldLocal(1);
            Quat gq = q * sl.q.Conj();
            @in[0].hand[1].grip = new Pose(gq, face + new Vec3(0.45f, 0, 0) - gq.Rotate(sl.p));
            Run(g, @in, 0.05f);
            c.C(g.pl[0].hands[1].shieldUp, "shield is up");
            FireOpponentBall(g, face);
            var log = new List<GameEvent>();
            Run(g, @in, 2.0f, log);
            c.C(HasEvent(log, EventType.ShieldBlock, 0), "shield deflects the incoming ball");
            c.C(!g.pl[0].ball.charged && !g.pl[0].hands[1].shieldUp, "shield breaks on hit; ball loses its charge");
            c.C(g.pl[1].score == 0, "blocked ball does not score");
            // now throw (still holding) past the foul line to recharge
            @in[0] = Standing(g, 0);
            @in[0].hand[1].trigger = 1;
            @in[0].hand[1].grip.p = g.pl[0].ball.pos - @in[0].hand[1].grip.q.Rotate(g.tun.launcherOffset);
            Run(g, @in, 0.05f);
            log.Clear();
            SwingAndRelease(g, @in, Normalize(new Vec3(1, 0.05f, 0.5f)), 5.0f, log);
            Run(g, @in, 0.8f, log);
            c.C(HasEvent(log, EventType.Recharge, 0), "throwing past your foul line recharges the ball");
            c.Done();
        }

        [Test]
        public void KnuckleGuardsBasic()
        {
            var c = new Checks("knuckle guards (Basic)", 7);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            Vec3 face = g.pl[0].@in.head.p;
            @in[0].hand[0].grip.p = face + new Vec3(0.4f, 0, 0) - @in[0].hand[0].grip.q.Rotate(g.tun.knuckleOffset);
            @in[0].hand[0].trigger = 0;
            Run(g, @in, 0.05f);
            FireOpponentBall(g, face);
            var log = new List<GameEvent>();
            Run(g, @in, 2.0f, log);
            c.C(HasEvent(log, EventType.KnuckleBlock, 0), "fist knuckle guard deflects the ball");
            c.C(!HasEvent(log, EventType.Returned, 1) || g.pl[1].ball.pos.x > -9.0f, "knuckled ball is sent back toward the opponent");
            {   // glancing hit: fist turned sideways at hip height still punches the ball forward
                var gi = new PlayerInput[2]; var gg = MakeGame(gi);
                Quat side = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(-90)) * Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(80));
                gi[0].hand[0].grip.q = side;
                Vec3 tgt = gg.tun.start[0] + new Vec3(0.45f, 0.75f, -0.2f);
                gi[0].hand[0].grip.p = tgt - side.Rotate(new Vec3(-gg.tun.knuckleOffset.x, gg.tun.knuckleOffset.y, gg.tun.knuckleOffset.z));
                Run(gg, gi, 0.1f);
                FireOpponentBall(gg, tgt);
                var gl = new List<GameEvent>();
                bool fwd = false;
                for (int i = 0; i < 200; ++i)
                {
                    Step(gg, gi);
                    gl.AddRange(gg.events);
                    if (HasEvent(gl, EventType.KnuckleBlock, 0) && gg.pl[1].ball.state == BallState.Flying && gg.pl[1].ball.vel.x > 2f) fwd = true;
                }
                c.C(HasEvent(gl, EventType.KnuckleBlock, 0) && fwd, "glancing knuckle hit still punches the ball toward the opponent");
            }
            // same again with the trigger pulled -> guard is off -> hit
            var in2 = new PlayerInput[2]; var g2 = MakeGame(in2);
            in2[0].hand[0].grip.p = face + new Vec3(0.4f, 0, 0) - in2[0].hand[0].grip.q.Rotate(g2.tun.knuckleOffset);
            in2[0].hand[0].trigger = 1;
            Run(g2, in2, 0.05f);
            FireOpponentBall(g2, face);
            log.Clear();
            Run(g2, in2, 2.0f, log);
            c.C(HasEvent(log, EventType.KnuckleBlock, 0), "Basic: knuckle guard stays active with the trigger pulled");
            {   // punch: fist moving into the ball at 4 m/s returns it faster than a still fist
                float PunchOut(float fistSpeed)
                {
                    var pi = new PlayerInput[2]; var gp = MakeGame(pi);
                    Vec3 face2 = gp.pl[0].@in.head.p;
                    Vec3 ko = gp.tun.knuckleOffset; ko.x = -ko.x;   // left hand
                    Vec3 start = face2 + new Vec3(0.30f, 0, 0) - pi[0].hand[0].grip.q.Rotate(ko);
                    pi[0].hand[0].grip.p = start;
                    Run(gp, pi, 0.1f);
                    FireOpponentBall(gp, face2, 8f);
                    float @out = 0;
                    for (int i = 0; i < 200 && @out == 0; ++i)
                    {
                        float distToFist = gp.pl[1].ball.pos.x - gp.KnucklePoint(0, 0).x;
                        if (distToFist < 0.6f) pi[0].hand[0].grip.p.x += fistSpeed / 72;   // punch forward (+X)
                        Step(gp, pi);
                        foreach (var e in gp.events) if (e.type == EventType.KnuckleBlock) @out = e.value;
                    }
                    return @out;
                }
                float still = PunchOut(0f), punch = PunchOut(4f);
                TestContext.Out.WriteLine($"  outgoing speed: still fist {still:F2} m/s, 4 m/s punch {punch:F2} m/s");
                c.C(still > 6.5f && still < 8.5f, "still fist returns the ball (~0.9 restitution)");
                c.C(punch > still + 2.5f, "punching adds the fist's speed");
            }
            var in3 = new PlayerInput[2]; var g3 = MakeGame(in3, true);
            c.C(!g3.pl[0].hands[0].knuckleUp, "Advanced: no knuckle guards");
            c.Done();
        }

        [Test]
        public void SpinCurvesTheThrow()
        {
            var c = new Checks("spin curves the throw", 2);
            float Lateral(Vec3 spin)
            {
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.tun.aimCorrectBasicDeg = 0;   // isolate the spin effect from aim assist
                @in[1].head.p.z = 2.4f;   // keep the opponent out of the way
                GrabBall(g, @in);
                @in[0].hand[1].angVel = spin;
                var log = new List<GameEvent>();
                SwingAndRelease(g, @in, Normalize(new Vec3(1, 0.03f, 0)), 6.0f, log);
                float maxDz = 0;
                for (int i = 0; i < 60; ++i)
                {
                    Run(g, @in, 1f / 72);
                    if (g.pl[0].ball.state == BallState.Flying && g.pl[0].ball.pos.x < 6.0f) maxDz = g.pl[0].ball.pos.z;
                }
                return maxDz;
            }
            float none = Lateral(new Vec3(0, 0, 0)), left = Lateral(new Vec3(0, 25, 0)), rightS = Lateral(new Vec3(0, -25, 0));
            TestContext.Out.WriteLine($"  lateral offset at x=6: none {none:F2}  spin+ {left:F2}  spin- {rightS:F2}");
            c.C(MathF.Abs(none) < 0.2f, "no spin: straight flight");
            c.C(MathF.Abs(left - none) > 0.4f && (left - none) * (rightS - none) < 0, "spin bends the flight, direction follows spin sign");
            c.Done();
        }

        [Test]
        public void CatchYourOwnBall()
        {
            var c = new Checks("catch your own ball", 1);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            Ball b = g.pl[0].ball;
            b.state = BallState.Flying; b.instigator = 0;
            Vec3 cp = g.CatcherPoint(0, 1);
            b.pos = cp + new Vec3(1.0f, 0, 0); b.vel = new Vec3(-6, 0, 0); b.speedLimit = 6;
            @in[0].hand[1].trigger = 1;
            var log = new List<GameEvent>();
            Run(g, @in, 0.4f, log);
            c.C(HasEvent(log, EventType.Caught, 0) && g.pl[0].ball.state == BallState.Held, "trigger + catcher catches own ball");
            c.Done();
        }

        [Test]
        public void OwnBallSlowReturnCanHitOwner()
        {
            var c = new Checks("own ball: the slow return can hit its owner", 2);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            @in[1].head.p.z = 2.0f;   // opponent dodged the throw
            GrabBall(g, @in);
            var log = new List<GameEvent>();
            SwingAndRelease(g, @in, Normalize(new Vec3(1, 0.02f, -0.03f)), 6.0f, log);
            Run(g, @in, 8.0f, log);   // owner stands still in the return's path
            c.C(HasEvent(log, EventType.Returned, 0), "return started");
            c.C(HasEvent(log, EventType.HitPlayer, 0) && g.pl[1].score == 1, "own returning ball hits its owner; opponent scores");
            c.Done();
        }

        [Test]
        public void OwnBallNoSelfHitAfterThrow()
        {
            var c = new Checks("own ball: no self-hit right after the throw", 1);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            GrabBall(g, @in);
            var log = new List<GameEvent>();
            SwingAndRelease(g, @in, Normalize(g.ChestPoint(1) - g.LauncherPoint(0, 1)), 5.0f, log);
            Run(g, @in, 0.3f, log);
            c.C(!HasEvent(log, EventType.HitPlayer, 0), "a fresh throw never hits the thrower");
            c.Done();
        }

        [Test]
        public void OwnBallCaughtWhoeverTouchedItLast()
        {   // native 0.3.19 (test_game.cpp "your own ball is yours to catch, whoever touched it last")
            var c = new Checks("your own ball is yours to catch, whoever touched it last", 1);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            Ball b = g.pl[0].ball;
            b.state = BallState.Flying; b.instigator = 1;   // (the other player blocked it last)
            b.pos = @in[0].head.p + new Vec3(0.6f, -0.4f, 0.2f); b.vel = new Vec3(-1.0f, 0f, 0f); b.speedLimit = 1.0f;
            const int h = 1;
            Vec3 off = @in[0].hand[h].grip.q.Rotate(g.tun.catcherOffset);
            @in[0].hand[h].grip.p = b.pos - off;
            @in[0].hand[h].trigger = 1;
            var log = new List<GameEvent>();
            Run(g, @in, 1f / 72, log);
            c.C(b.state == BallState.Held && HasEvent(log, EventType.Caught), "caught, though the other player touched it last");
            c.Done();
        }

        [Test]
        public void StrikeSpeed()
        {   // native 0.3.19 (test_game.cpp "STRIKE SPEED: a host's option for faster strikes")
            var c = new Checks("STRIKE SPEED: a host's option for faster strikes", 6);
            var t = new Tunables();
            c.C(t.strikeSpeed == 0 && t.PerStrike() == 0.5f, "regulation by default: 0.5 m/s a strike, the original's");
            c.C(t.StatsMode() == 0, "... and a regulation match counts as standard");
            t.strikeSpeed = 1; float fast = t.PerStrike();
            t.strikeSpeed = 2; float faster = t.PerStrike();
            c.C(fast == 1.0f && faster == 1.5f, "FAST 1.0 m/s, FASTER 1.5 m/s a strike");
            c.C(t.StatsMode() == 2, "a match on FASTER counts as custom, not standard");
            t.experimental = true; t.expRoom = Room.Blitz;
            c.C(t.PerStrike() == 0.5f, "four-player rooms stay at regulation");
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            g.pl[1].strikesAgainst = 3;   // (three strikes held by player 0: StrikesHeld)
            float reg = g.ThrowSpeedFromHandSpeed(99f, 0);
            g.tun.strikeSpeed = 2;
            float fs = g.ThrowSpeedFromHandSpeed(99f, 0);
            TestContext.Out.WriteLine($"    a full-power throw with {g.StrikesHeld(0)} strikes: {reg:F2} m/s regulation, {fs:F2} m/s FASTER");
            c.C(g.StrikesHeld(0) == 0 || fs > reg, "a full-power throw with strikes is faster on FASTER");
            c.Done();
        }

        // FOUL LINE (TASKS.md E3, native 81e2880: test_game.cpp "FOUL LINE: a host's option to move the line, 0.5 m a step")
        [Test]
        public void FoulLine()
        {
            var c = new Checks("FOUL LINE: a host's option to move the line, 0.5 m a step", 13);
            var t = new Tunables();
            c.C(t.foulShift == 0 && t.FoulMove() == 0f && t.StatsMode() == 0, "where the arena has it by default, and standard");
            t.foulShift = 2;
            c.C(t.FoulMove() == 1.0f && t.StatsMode() == 2, "LONGER 2: 1 m toward the centre; the match counts as custom");
            t.foulShift = -9;
            c.C(t.FoulMove() == -1.0f, "at most 2 steps either way");
            t.experimental = true; t.expRoom = Room.Corners;
            c.C(t.FoulMove() == 0f, "the four-player rooms keep their own");
            t.expRoom = Room.Diamond;
            c.C(t.FoulMove() == 0f, "DIAMOND too (its foul zone is the platform)");
            // your own ball landing on your own end's floor at x = -4.3 (just short of the arena's line at 4.75)
            void LandsAt(int shift, float x, out bool fouled, out bool bounced)
            {
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.tun.foulShift = shift;
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 0; b.rechargePending = false; b.scored = false; b.spin = new Vec3();
                b.pos = new Vec3(x + 1.2f, 1.0f, 0.5f); b.speedLimit = 7f; b.vel = Normalize(new Vec3(x, 0f, 0.5f) - b.pos) * 7f;
                b.progX = b.pos.x; b.progT = 0; b.timer = 0;
                var log = new List<GameEvent>();
                fouled = bounced = false;
                for (int i = 0; i < 72 && !fouled && !bounced; ++i)
                {
                    Step(g, @in);
                    log.AddRange(g.events);
                    fouled = HasEvent(log, EventType.Foul, 0);
                    bounced = HasEvent(log, EventType.Bounce, 0);
                }
            }
            LandsAt(0, -4.3f, out bool f, out bool bo);
            c.C(!f && bo, "regulation: landing 4.3 m from the centre, short of the line, just bounces");
            LandsAt(2, -4.3f, out f, out bo);
            c.C(f, "LONGER 2 (the line at 3.73 m): the same throw is a foul");
            LandsAt(1, -4.6f, out f, out bo);   // (the ball meets the floor ~0.15 m before where it is aimed)
            c.C(f, "LONGER 1 (4.23 m): landing 4.6 m from the centre is a foul");
            LandsAt(0, -5.3f, out f, out bo);
            c.C(f, "regulation: landing 5.3 m from the centre is a foul");
            LandsAt(-2, -5.3f, out f, out bo);
            c.C(!f && bo, "SHORTER 2 (the line at 5.73 m): the same throw just bounces");
            LandsAt(-2, -6.2f, out f, out bo);
            c.C(f, "SHORTER 2: past the moved line it is still a foul");
            var in2 = new PlayerInput[2]; var g2 = MakeGame(in2);
            g2.tun.foulShift = 2;
            c.C(Math.Abs(g2.FoulLine() - 3.73f) < 1e-4f, "the game's line follows the option");
            g2.tun.foulShift = -1;
            c.C(Math.Abs(g2.FoulLine() - 5.23f) < 1e-4f, "... either way");
            c.Done();
        }

        // THE REACH ROUND THE FIST (TASKS.md E15, native 2ee3617 test_game.cpp "[the reach round the fist]"): the ball on its
        // spawner, the right fist put beside it, behind it or in front of it, the trigger pulled; as before (a match with an
        // older game: FAIR MATCHES) and now
        [Test]
        public void TheReachRoundTheFist()
        {
            (string name, Vec3 d, bool before, bool now)[] places = {   // the ball from the fist (m: forward, right, up)
                ("30 cm beside the fist", new Vec3(0, 0.32f, 0), false, true), ("25 cm behind the fist", new Vec3(-0.25f, 0, 0), false, true),
                ("15 cm under the fist", new Vec3(0, 0, -0.15f), true, true), ("50 cm in front", new Vec3(0.50f, 0, 0), true, true),
                ("60 cm in front", new Vec3(0.60f, 0, 0), false, false), ("45 cm beside the fist", new Vec3(0, 0.45f, 0), false, false) };
            bool right = true, cueRight = true;
            foreach (var pl in places)
                for (int now = 0; now < 2; ++now)
                {
                    var @in = new PlayerInput[2]; var g = MakeGame(@in);
                    g.tun.matchVersion = now != 0 ? Tunables.kVerAll : Tunables.kVerReach - 1;
                    Quat q = kFaceP0;
                    Vec3 fwd = q.Rotate(new Vec3(0, 0, -1)), side = q.Rotate(new Vec3(1, 0, 0)), up = new Vec3(0, 1, 0);
                    Vec3 ball = g.pl[0].ball.pos;
                    Vec3 fist = ball - (fwd * pl.d.x + side * pl.d.y + up * pl.d.z);
                    @in[0].hand[1].grip = new Pose(q, fist - q.Rotate(g.tun.fistReachOffset));
                    @in[0].hand[1].trigger = 0;
                    Step(g, @in);
                    bool cue = g.BallInReach(0, 1);
                    @in[0].hand[1].trigger = 1;
                    var log = new List<GameEvent>();
                    Run(g, @in, 0.05f, log);
                    bool took = HasEvent(log, EventType.Grab, 0);
                    bool want = now != 0 ? pl.now : pl.before;
                    if (took != want) { right = false; TestContext.Out.WriteLine($"  {pl.name}, {(now != 0 ? "now" : "before")}: {(took ? "taken" : "not taken")}"); }
                    if (cue != took) cueRight = false;
                }
            Assert.That(right, "beside and behind the fist a ball is taken now (not before); in front as far as before, and no further");
            Assert.That(cueRight, "the ball shows it is in reach exactly when the trigger would take it");
        }

        // ROOMS AND THEIR OPTIONS (TASKS.md E22, native d4cf079 / cb0970f test_game.cpp "[rooms and their options]"):
        // MAIDEN, AIM ASSIST, STRIKE ZONE, BALL GROWTH, ROUNDS, each through FAIR MATCHES (0.3.21)
        [Test]
        public void RoomsAndTheirOptions()
        {
            var t = new Tunables();
            Assert.That(!t.MaidenNow() && t.AimAssistNow() == 2 && t.StrikeZoneNow() == 1 && t.BallGrowthNow() == 3 && t.StatsMode() == 0 &&
                        Math.Abs(t.AimAssistDeg() - 3f) < 1e-4f && Math.Abs(t.StrikeRadius() - 0.7f) < 1e-4f && t.GrowthShare() == 1f,
                        "as the rules say by default: Basic's MEDIUM aim assist (3 deg), a 70 cm ring, the ball's full growth; standard");
            t.advanced = true;
            Assert.That(t.AimAssistNow() == 1 && Math.Abs(t.AimAssistDeg() - 1.5f) < 1e-4f && t.StatsMode() == 0, "Advanced: LOW (1.5 deg)");
            // a throw 8 degrees off the opponent's chest, aim assist on: how far it turns toward them
            var turned = new float[4];
            for (int s = 0; s < 4; ++s)
            {
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.tun.aimAssist = s;
                Vec3 from = g.tun.start[0] + new Vec3(0.6f, 1.3f, 0);
                Vec3 d0 = Normalize(g.ChestPoint(1) - from);
                Vec3 dir = Quat.AxisAngle(new Vec3(0, 1, 0), 8f * 3.14159265f / 180f).Rotate(d0);
                g.Serve(0, from, dir, 10f, new Vec3(), true);
                // (in double, both directions made unit there: the throw's direction, rotated in floats, is a hair off unit
                // length, and acos near 1 makes that hundredths of a degree)
                Vec3 v = g.pl[0].ball.vel;
                double vl = Math.Sqrt((double)v.x * v.x + (double)v.y * v.y + (double)v.z * v.z);
                double dl = Math.Sqrt((double)dir.x * dir.x + (double)dir.y * dir.y + (double)dir.z * dir.z);
                double dd = ((double)v.x * dir.x + (double)v.y * dir.y + (double)v.z * dir.z) / (vl * dl);
                turned[s] = (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, dd))) * 57.29577951308232);
            }
            TestContext.Out.WriteLine($"  (a throw 8 deg off, turned: OFF {turned[0]:F4}, LOW {turned[1]:F4}, MEDIUM {turned[2]:F4}, HIGH {turned[3]:F4} deg)");
            Assert.That(turned[0] < 0.01f && Math.Abs(turned[1] - 1.5f) < 0.1f && Math.Abs(turned[2] - 3f) < 0.1f && Math.Abs(turned[3] - 4.5f) < 0.1f,
                        "AIM ASSIST: a throw is turned OFF 0, LOW 1.5, MEDIUM 3, HIGH 4.5 deg toward its target");
            // a ball through the ring 0.6 m and 0.8 m from its centre: a strike or not, by the ring's size
            bool StrikeAt(int zone, float off)
            {
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.tun.strikeZone = zone;
                @in[0].head.p.z = -1.5f; g.pl[0].@in.head.p.z = -1.5f;   // (out of the way)
                Vec3 at = g.StrikeZoneCenter(0) + new Vec3(0, 0, off);
                FireOpponentBall(g, at + Normalize(at - (g.tun.start[1] + new Vec3(-0.6f, 1.6f, 0))) * 0.5f);
                var log = new List<GameEvent>();
                Run(g, @in, 2.0f, log);
                return HasEvent(log, EventType.Strike, 0);
            }
            bool s06 = StrikeAt(0, 0.6f), r06 = StrikeAt(1, 0.6f), r08 = StrikeAt(1, 0.8f), l08 = StrikeAt(2, 0.8f);
            Assert.That(!s06 && r06 && !r08 && l08, "STRIKE ZONE: 60 cm out a strike in REGULATION, not SMALL (50 cm); 80 cm one in LARGE (90 cm), not REGULATION");
            // BALL GROWTH: the ball at 4 strikes (regulation: twice its size)
            var grown = new float[4];
            for (int k = 0; k < 4; ++k) { var g = new Game(); g.tun.ballGrowth = k; grown[k] = g.StrikeScale(4); }
            Assert.That(grown[0] == 1f && Math.Abs(grown[1] - 1.25f) < 1e-4f && Math.Abs(grown[2] - 1.5f) < 1e-4f && Math.Abs(grown[3] - 2f) < 1e-4f,
                        "BALL GROWTH at 4 strikes: NONE 1x, QUARTER 1.25x, HALF 1.5x, REGULATION 2x");
            {   // MAIDEN: an uncharged ball still raises the shield, and a block never breaks it
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.tun.maiden = true;
                GrabBall(g, @in, 1);
                g.pl[0].ball.charged = false;
                Quat q = FromTo(new Vec3(0, 1, 0), new Vec3(1, 0, 0));
                Vec3 face = g.pl[0].@in.head.p;
                Pose sl = g.ShieldLocal(1);
                Quat gq = q * sl.q.Conj();
                @in[0].hand[1].grip = new Pose(gq, face + new Vec3(0.45f, 0, 0) - gq.Rotate(sl.p));
                Run(g, @in, 0.05f);
                bool upUncharged = g.pl[0].hands[1].shieldUp;
                g.pl[0].ball.charged = true;
                FireOpponentBall(g, face);
                var log = new List<GameEvent>();
                Run(g, @in, 2.0f, log);
                Assert.That(upUncharged && HasEvent(log, EventType.ShieldBlock, 0) && !HasEvent(log, EventType.ShieldBreak, 0) &&
                            g.pl[0].ball.charged && g.pl[0].hands[1].shieldUp && g.pl[1].score == 0,
                            "MAIDEN: the shield is up with an uncharged ball, blocks, and stays up (no break, the charge kept)");
            }
            // FAIR MATCHES and the rooms: an older player in the match, or a four-player room, plays them as the rules say
            var f = new Tunables { maiden = true, aimAssist = 3, strikeZone = 2, ballGrowth = 1 };
            Assert.That(f.MaidenNow() && f.AimAssistNow() == 3 && f.StrikeZoneNow() == 2 && f.BallGrowthNow() == 1 && f.StatsMode() == 2,
                        "every option plays when every player's game has them (0.3.21), and the match counts as custom");
            f.matchVersion = Tunables.kVerRoomOptions - 1;
            Assert.That(!f.MaidenNow() && f.AimAssistNow() == 2 && f.StrikeZoneNow() == 1 && f.BallGrowthNow() == 3 && f.StatsMode() == 0 && f.maiden && f.aimAssist == 3,
                        "... a player on 0.3.20: all as the rules say, the match standard, the host's choices kept");
            f.matchVersion = Tunables.kVerAll; f.experimental = true; f.expRoom = Room.Diamond;
            bool diamond = f.MaidenNow() && f.StrikeZoneNow() == 2;
            f.expRoom = Room.Corners;
            bool corners = !f.MaidenNow() && f.AimAssistNow() == 2 && f.StrikeZoneNow() == 1 && f.BallGrowthNow() == 3;
            f.expRoom = Room.Blitz;
            bool blitz = !f.MaidenNow() && f.StrikeZoneNow() == 1;
            Assert.That(diamond && corners && blitz, "DIAMOND has them; FOUR CORNERS and FIREBALL BLITZ keep their own for now");
            f = new Tunables { maiden = true, shields = false };
            Assert.That(!f.MaidenNow() && f.StatsMode() == 1, "MAIDEN needs shields: with SHIELDS OFF it's HOTDOG");
            {   // THE BALL IN THE LEFT PALM (TASKS.md E43, native c2e64a0; DOSperado, 11-19): the left hand's ball on its palm side, as the right's is
                var pin = new PlayerInput[2]; var pg = MakeGame(pin);
                Vec3 r = pg.HoldLocal(1), l = pg.HoldLocal(0);
                bool mirrored = r.x < 0 && l.x == -r.x && l.y == r.y && l.z == r.z;
                pg.tun.matchVersion = 3021;
                bool older = pg.HoldLocal(0).x == r.x && pg.HoldLocal(1).x == r.x;
                pg.tun.matchVersion = Tunables.kVerAll; pg.tun.palmHold = false;
                bool guest = pg.HoldLocal(0).x == r.x;
                Assert.That(mirrored && older && guest, "the left hand's ball in its palm (grip +X) as the right's (-X); with a player on " +
                                                       "0.3.21, or a host that doesn't say so: where it was");
            }
            {   // BALL FEEL (TASKS.md E29, native 3129dc2): the owner's pick the standard ball; only how it looks: never custom in the stats
                var bf = new Tunables();
                int std0 = bf.StatsMode();
                bool same = bf.ballFeel == (Tunables.BALL_TRUE | Tunables.BALL_SPIN | Tunables.BALL_WEIGHT);
                for (int bits = 0; bits <= Tunables.BALL_FEEL_ALL; ++bits) { bf.ballFeel = bits; same &= bf.StatsMode() == std0; }
                Assert.That(same, "BALL FEEL: the standard ball is glow core, true size, visible spin, heavy; any ball feel leaves the stats as they are");
            }
            {   // SHIELD SIZE (TASKS.md E25, native 7ed11aa): a ball 35 cm out from the plate's middle: past a regulation
                // shield, blocked by +50%
                bool Blocks(int size)
                {
                    var @in = new PlayerInput[2]; var g = MakeGame(@in);
                    g.tun.shieldSize = size;
                    GrabBall(g, @in, 1);
                    Quat q = FromTo(new Vec3(0, 1, 0), new Vec3(1, 0, 0));
                    Vec3 face = g.pl[0].@in.head.p;
                    Pose sl = g.ShieldLocal(1);
                    Quat gq = q * sl.q.Conj();
                    @in[0].hand[1].grip = new Pose(gq, face + new Vec3(0.45f, 0, 0) - gq.Rotate(sl.p));
                    Run(g, @in, 0.05f);
                    Pose sp = g.ShieldPose(0, 1);
                    Vec3 side = Normalize(sp.q.Rotate(new Vec3(1, 0, 0)));
                    FireOpponentBall(g, sp.p + side * 0.35f);
                    var log = new List<GameEvent>();
                    Run(g, @in, 2.0f, log);
                    return HasEvent(log, EventType.ShieldBlock, 0);
                }
                var c = new Tunables { shieldSize = 4, matchVersion = Tunables.kVerRoomOptions - 1 };
                Assert.That(!Blocks(0) && Blocks(2) && c.ShieldSizeNow() == 0 && Math.Abs(c.ShieldScale() - 1f) < 1e-6f,
                            "SHIELD SIZE: a ball 35 cm out from the middle passes a regulation shield, a +50% one blocks it; a 0.3.20 player: regulation");
                var sz = new Tunables { shieldSize = 2 };
                Assert.That(sz.StatsMode() == 2 && Scoreboard.RulesLine(sz).EndsWith(" - SHIELDS +50%"), "custom in the stats; the rules line says so");
            }
            {   // ROUNDS: BEST OF 5 is three rounds won (5 s rounds here: the one ahead at time takes each)
                var @in = new PlayerInput[2];
                var g = NewGame(); g.tun.advanced = true; g.tun.meetTime = 0; g.tun.advancedRoundTime = 5f;
                g.tun.advancedRoundsToWin = 3;
                g.Reset(false);
                @in[0] = Standing(g, 0); @in[1] = Standing(g, 1);
                Run(g, @in, 3.6f);
                bool custom = g.tun.StatsMode() == 2;
                int rounds = 0;
                for (int r = 0; r < 6 && g.match != MatchState.MatchSummary; ++r)
                {
                    if (g.match == MatchState.InProgress) { g.pl[0].score = 3; ++rounds; }
                    Run(g, @in, 6f);
                    if (g.match != MatchState.MatchSummary) Run(g, @in, g.tun.roundSummaryTime + g.tun.countdown + 0.5f);
                }
                var old = new Tunables { advanced = true, advancedRoundsToWin = 3, matchVersion = Tunables.kVerRoomOptions - 1 };
                var d = new Tunables { experimental = true, expRoom = Room.Diamond, advanced = true, advancedRoundsToWin = 9 };
                bool diamondRounds = d.RoundsToWinNow() == 5 && d.AimAssistNow() == 1 && !d.KnuckleGuards() && d.StatsMode() == 2;
                TestContext.Out.WriteLine($"  (BEST OF 5: the match ended after {rounds} rounds, {g.pl[0].roundWins} won)");
                Assert.That(custom && g.match == MatchState.MatchSummary && g.pl[0].roundWins == 3 && rounds == 3 && old.RoundsToWinNow() == 2,
                            "ROUNDS: BEST OF 5 ends at the third round won (a custom match); with a player on 0.3.20, BEST OF 3");
                Assert.That(diamondRounds, "DIAMOND plays rounds too (its rules' LOW aim assist, no knuckle guards); at most BEST OF 9");
            }
        }

        // BOUNCES (TASKS.md E32, native 5d3bbec, 0.3.22; pull request #26's ideas, as the host's options)
        [Test]
        public void Bounces()
        {
            // a ball served at 10 m/s, 30 degrees into the +z side wall, no spin; its speed and spin after that bounce
            void Bank(bool lively, bool grip, int version, out float speed, out float spin, out float outDeg)
            {
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.tun.wallsLively = lively; g.tun.wallGrip = grip; g.tun.matchVersion = version;
                float a = Deg2Rad(30f);
                g.Serve(0, g.tun.start[0] + new Vec3(3f, 1.3f, 0), new Vec3(MathF.Cos(a), 0, MathF.Sin(a)), 10f, new Vec3(), false);
                speed = spin = outDeg = -1f;
                for (int i = 0; i < 72 * 2; ++i)
                {
                    Step(g, @in);
                    Ball b = g.pl[0].ball;
                    if (HasEvent(g.events, EventType.Bounce, 0))
                    {
                        speed = b.speedLimit; spin = Length(b.spin);
                        outDeg = MathF.Atan2(-b.vel.z, b.vel.x) * 57.2957795f;
                        break;
                    }
                }
            }
            Bank(false, false, Tunables.kVerAll, out float s0, out float w0, out float d0);
            Bank(true, false, Tunables.kVerAll, out float s1, out _, out float d1);
            Bank(false, true, Tunables.kVerAll, out float s2, out float w2, out float d2);
            Bank(true, true, Tunables.kVerBounces - 1, out float s3, out float w3, out _);
            TestContext.Out.WriteLine($"  (a 30 degree bank at 10 m/s: STANDARD {s0:F2} m/s out at {d0:F1} deg; LIVELY {s1:F2} at {d1:F1}; GRIP {s2:F2} at {d2:F1}, spinning {w2:F1} rad/s)");
            Assert.That(Math.Abs(s0 - 9f) < 0.02f && Math.Abs(d0 - 30f) < 0.5f && w0 < 0.01f,
                        "BOUNCES STANDARD: a side wall mirrors the ball and keeps 90% of its speed, as before");
            // (LIVELY: the 5 m/s into the wall cut to 97%, the 8.66 along it kept: sqrt(8.66^2 + 4.85^2) = 9.93)
            Assert.That(Math.Abs(s1 - 9.926f) < 0.02f && d1 > 29f && d1 < 30f, "WALLS: LIVELY: only the speed into the side wall is cut (97%): 9.93 m/s of 10 kept");
            // (GRIP: the normal impulse 1.9 x 5 = 9.5; the grip needed (2/7 of 8.66, 2.47) is more than the friction allows
            // (0.10 x 9.5 = 0.95): 8.66 - 0.95 along, 4.5 out: 8.93 m/s; spin 0.95 x 5 / (2 x 0.1) = 23.8 rad/s)
            Assert.That(Math.Abs(s2 - 8.93f) < 0.03f && Math.Abs(w2 - 23.75f) < 0.6f && d2 > 30f,
                        "WALL GRIP: the wall grips the ball as a solid sphere: 8.93 m/s kept, spinning 23.8 rad/s, a little steeper out");
            Assert.That(Math.Abs(s3 - 9f) < 0.02f && w3 < 0.01f, "BOUNCES FAIR MATCHES: a player on 0.3.21 in the match: STANDARD, for both");
            // BANK ASSIST: a bank set to cross the other player's plane half a metre wide of their chest
            void Banked(int level, int version, out float closest, out float turnDeg)
            {
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                g.tun.aimAssist = 0;   // (AIM ASSIST off: the throw goes where it's sent)
                g.tun.bankAssist = level; g.tun.matchVersion = version;
                Vec3 from = g.tun.start[0] + new Vec3(0.6f, 1.3f, 0), chest = g.ChestPoint(1);
                float wall = 2.5f - g.BallRadius(0);
                var mirror = new Vec3(chest.x, chest.y, 2f * wall - (chest.z + 0.5f));   // (the far end, seen in the wall)
                g.Serve(0, from, Normalize(mirror - from), 10f, new Vec3(), true);
                closest = 1e9f; turnDeg = 0f;
                bool bounced = false;
                for (int i = 0; i < 72 * 3; ++i)
                {
                    Step(g, @in);
                    Ball b = g.pl[0].ball;
                    if (!bounced && HasEvent(g.events, EventType.Bounce, 0)) { bounced = true; turnDeg = Length(b.bankTurn) * 57.2957795f; }
                    if (b.state != BallState.Flying) break;
                    if (bounced) closest = MathF.Min(closest, Length(b.pos - chest));
                }
            }
            Banked(0, Tunables.kVerAll, out float c0, out float t0);
            Banked(3, Tunables.kVerAll, out float c3, out float t3);
            Banked(3, Tunables.kVerBounces - 1, out float cOld, out float tOld);
            TestContext.Out.WriteLine($"  (a bank 0.5 m wide of the chest: OFF passes {c0:F2} m from it; HIGH turns it {t3:F2} deg and passes {c3:F2} m)");
            Assert.That(t0 == 0f && t3 > 0.5f && t3 <= 2.4f + 1e-3f && c3 < c0 - 0.1f,
                        "BANK ASSIST HIGH: the leg after the bounce is turned toward the other player (at most 2.4 degrees) and passes nearer");
            Assert.That(tOld == 0f && Math.Abs(cOld - c0) < 1e-3f, "BANK ASSIST FAIR MATCHES: a player on 0.3.21 in the match: none");
            var f = new Tunables { wallGrip = true };
            int gripMode = f.StatsMode();
            f.wallGrip = false; f.bankAssist = 1;
            int bankMode = f.StatsMode();
            f.experimental = true; f.expRoom = Room.Corners; f.wallsLively = true; f.wallGrip = true;
            bool corners = f.BouncesStandard();
            Assert.That(gripMode == 2 && bankMode == 2 && corners && new Tunables().StatsMode() == 0,
                        "BOUNCES: any of them makes the match custom in the stats; FOUR CORNERS keeps STANDARD for now");
        }

        [Test]
        public void FairMatches()
        {   // TASKS.md E2, native d757f8a (test_game.cpp "FAIR MATCHES: an option plays only when every player's game has it")
            var c = new Checks("FAIR MATCHES: an option plays only when every player's game has it", 7);
            var f = new Tunables { strikeSpeed = 2 };
            c.C(f.matchVersion == Tunables.kVerAll && f.PerStrike() == 1.5f, "offline and as a guest: everything plays");
            f.matchVersion = 3019;
            c.C(f.PerStrike() == 1.5f, "every player on 0.3.19: faster strikes play");
            f.matchVersion = 0;
            c.C(f.PerStrike() == 0.5f && f.StatsMode() == 0, "a game that doesn't say its version: regulation, and the match counts as standard");
            c.C(f.strikeSpeed == 2, "... the host's choice itself kept");
            // (native's own, with FOUL LINE: TASKS.md E3)
            f.foulShift = 2;
            f.matchVersion = 3020;
            c.C(f.FoulMove() == 1.0f && f.PerStrike() == 1.5f, "every player on 0.3.20: both options play");
            f.matchVersion = 3019;
            c.C(f.FoulMove() == 0f && f.PerStrike() == 1.5f, "... a player on 0.3.19: the foul line at regulation, faster strikes still");
            c.C(Online.Net.VersionNumber("0.3.20") == 3020 && Online.Net.VersionNumber("1.2.3") == 1002003 && Online.Net.VersionNumber("") == 0 &&
                Online.Net.VersionNumber("0.3.19x") == 3019, "versions as numbers: 0.3.20 is 3020, none is 0");
            c.Done();
        }

        [Test]
        public void StrikesCapAndSteppedGrowth()
        {
            var c = new Checks("strikes: cap 42, stepped size growth", 4);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            g.match = MatchState.InProgress;
            float prev = g.StrikeScale(0);
            bool monotonic = true;
            for (int n = 1; n <= 42; ++n) { float sc = g.StrikeScale(n); if (sc < prev) monotonic = false; prev = sc; }
            TestContext.Out.WriteLine($"  scale at 0/1/4/10/24/42 strikes: {g.StrikeScale(0):F2} {g.StrikeScale(1):F2} {g.StrikeScale(4):F2} {g.StrikeScale(10):F2} {g.StrikeScale(24):F2} {g.StrikeScale(42):F2}");
            c.C(monotonic && g.StrikeScale(1) > g.StrikeScale(0) && g.StrikeScale(10) > g.StrikeScale(4), "size steps up per strike");
            c.C(g.StrikeScale(42) <= g.tun.maxBallScale + 1e-4f, "size capped at maxBallScale");
            for (int i = 0; i < 60; ++i) g.AddStrike(0, default);
            c.C(g.pl[0].strikesAgainst == 42, "strike count stops at MaxStrikes 42");
            float s42 = g.ThrowSpeedFromHandSpeed(6.5f, 1);
            c.C(s42 > 34f && s42 < 36f, "speed at 42 strikes = 14 + 42 x 0.5 m/s");
            c.Done();
        }

        [Test]
        public void StrikeThroughStrikeZoneRing()
        {
            var c = new Checks("strike: opponent ball through your strike-zone ring", 2);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            @in[0].head.p.z = -1.5f; g.pl[0].@in.head.p.z = -1.5f;   // dodge
            FireOpponentBall(g, g.StrikeZoneCenter(0) + new Vec3(-0.5f, 0, 0));
            var log = new List<GameEvent>();
            Run(g, @in, 2.0f, log);
            c.C(HasEvent(log, EventType.Strike, 0), "strike against the player whose zone was passed");
            Run(g, @in, 6.0f);   // let the opponent's ball finish and respawn
            c.C(g.BallRadius(1) > g.tun.projectileRadius * 1.2f, "opponent's ball grows with strikes against you");
            c.Done();
        }

        [Test]
        public void ArenaIsWatertight()
        {
            var c = new Checks("arena is watertight", 1);
            var @in = new PlayerInput[2]; var g = MakeGame(@in);
            @in[1].head.p = new Vec3(0, -5, 0); @in[0].head.p = new Vec3(0, -5, 0);   // players out of the way
            g.pl[0].@in.CopyFrom(@in[0]); g.pl[1].@in.CopyFrom(@in[1]);
            uint r = 1234567;
            float Rnd() { r ^= r << 13; r ^= r >> 17; r ^= r << 5; return (r & 0xFFFF) / 65535f; }
            int escaped = 0;
            for (int k = 0; k < 300; ++k)
            {
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 0; b.scored = true; b.rechargePending = false;
                b.pos = new Vec3((Rnd() - 0.5f) * 8f, 0.5f + Rnd() * 3f, (Rnd() - 0.5f) * 4f);
                Vec3 d = Normalize(new Vec3(Rnd() - 0.5f, Rnd() - 0.5f, Rnd() - 0.5f));
                b.vel = d * 14f; b.speedLimit = 14f; b.bounces = 0; b.timer = 0; b.spin = default;
                for (int i = 0; i < 40 && b.state == BallState.Flying; ++i)
                {
                    Step(g, @in);
                    if (MathF.Abs(b.pos.z) > 2.6f || b.pos.y < -0.05f || b.pos.y > 4.1f || MathF.Abs(b.pos.x) > 9.6f) { ++escaped; break; }
                }
            }
            TestContext.Out.WriteLine($"  escaped {escaped} / 300");
            c.C(escaped == 0, "no ball ever leaves the court volume");
            c.Done();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullMatchVsBot(bool adv)
        {
            var c = new Checks("full match vs bot: " + (adv ? "Advanced" : "Basic"), 3);
            var g = NewGame(); g.tun.advanced = adv; g.tun.basicMatchTime = 40; g.Reset(true);
            var @in = new[] { Standing(g, 0), new PlayerInput() };
            var log = new List<GameEvent>();
            bool nan = false;
            float t = 0, nextThrow = 4;
            for (int i = 0; i < 72 * 400 && g.match != MatchState.MatchSummary; ++i, t += 1f / 72)
            {
                if (g.match == MatchState.InProgress && t > nextThrow && g.pl[0].ball.state == BallState.Idle)
                {
                    GrabBall(g, @in);
                    SwingAndRelease(g, @in, Normalize(g.ChestPoint(1) - g.LauncherPoint(0, 1)), 5.5f, log);
                    nextThrow = t + 2.0f;
                    @in[0] = Standing(g, 0);
                }
                Step(g, @in);
                log.AddRange(g.events);
                for (int p = 0; p < 2; ++p) if (float.IsNaN(g.pl[p].ball.pos.x)) nan = true;
            }
            int hits = 0, blocks = 0, knuck = 0, botThrows = 0;
            foreach (var e in log)
            {
                hits += e.type == EventType.HitPlayer ? 1 : 0; blocks += e.type == EventType.ShieldBlock ? 1 : 0;
                knuck += e.type == EventType.KnuckleBlock ? 1 : 0; botThrows += (e.type == EventType.Throw && e.player == 1) ? 1 : 0;
            }
            TestContext.Out.WriteLine($"  score {g.pl[0].score}-{g.pl[1].score} rounds {g.pl[0].roundWins}-{g.pl[1].roundWins}, hits={hits} shieldBlocks={blocks} knuckleBlocks={knuck} botThrows={botThrows}");
            c.C(!nan, "no NaNs during a full match");
            c.C(g.match == MatchState.MatchSummary, "match reaches a winner");
            c.C(botThrows > 3 && hits > 0, "bot throws and points are scored");
            c.Done();
        }

        [Test]
        public void MatchOptions()
        {
            var c = new Checks("MATCH OPTIONS: hotdog mode, knuckle guards, free play, timed Advanced rounds", 17);
            Game Setup(PlayerInput[] @in, bool adv, bool shields, int knuckles, float matchT, float roundT)
            {
                var g = NewGame(); g.tun.advanced = adv; g.tun.meetTime = 0;
                g.tun.shields = shields; g.tun.knuckleMode = knuckles; g.tun.basicMatchTime = matchT; g.tun.advancedRoundTime = roundT;
                g.Reset(false);
                @in[0] = Standing(g, 0); @in[1] = Standing(g, 1);
                Run(g, @in, 3.6f);
                return g;
            }
            {   // HOTDOG: holding a charged ball raises no shield; the ball that would have been blocked scores
                var @in = new PlayerInput[2]; var g = Setup(@in, false, false, 0, 180, 0);
                GrabBall(g, @in, 1);
                Quat q = FromTo(new Vec3(0, 1, 0), new Vec3(1, 0, 0));
                Vec3 face = g.pl[0].@in.head.p;
                Pose sl = g.ShieldLocal(1);
                Quat gq = q * sl.q.Conj();
                @in[0].hand[1].grip = new Pose(gq, face + new Vec3(0.45f, 0, 0) - gq.Rotate(sl.p));
                Run(g, @in, 0.05f);
                c.C(!g.pl[0].hands[1].shieldUp && g.pl[0].ball.charged, "hotdog mode: no shield, even holding a charged ball");
                FireOpponentBall(g, face);
                var log = new List<GameEvent>();
                Run(g, @in, 2.0f, log);
                c.C(!HasEvent(log, EventType.ShieldBlock) && g.pl[1].score == 1, "hotdog mode: the ball goes through and scores");
            }
            {
                var @in = new PlayerInput[2];
                var b = Setup(@in, false, true, -1, 180, 0);
                c.C(b.pl[0].hands[0].knuckleUp, "Basic: knuckle guards on as the rules say");
                var b0 = Setup(@in, false, true, 0, 180, 0);
                c.C(!b0.pl[0].hands[0].knuckleUp, "Basic with knuckle guards off: none");
                var a1 = Setup(@in, true, true, 1, 0, 0);
                c.C(a1.pl[0].hands[0].knuckleUp, "Advanced with knuckle guards on: there");
                var a0 = Setup(@in, true, true, -1, 0, 0);
                c.C(!a0.pl[0].hands[0].knuckleUp, "Advanced: no knuckle guards as the rules say");
            }
            {   // FREE PLAY: no clock, no winner
                var @in = new PlayerInput[2]; var g = Setup(@in, false, true, -1, 0, 0);
                var log = new List<GameEvent>();
                Run(g, @in, 240f, log);
                c.C(g.match == MatchState.InProgress && !HasEvent(log, EventType.MatchEnd) && !HasEvent(log, EventType.Overtime),
                    "free play: still playing after 4 minutes, no match end, no overtime");
            }
            {   // Advanced, 1 minute rounds: at time the one ahead wins the round
                var @in = new PlayerInput[2]; var g = Setup(@in, true, true, -1, 180, 60);
                c.C(g.match == MatchState.InProgress && g.roundClock > 50f && g.roundClock <= 60f, "timed round: the clock runs from 1:00");
                g.pl[1].score = 2;
                var log = new List<GameEvent>();
                Run(g, @in, 61f, log);
                c.C(HasEvent(log, EventType.RoundEnd, 1) && g.pl[1].roundWins == 1, "timed round: at time the one ahead (2-0) wins it");
                Run(g, @in, g.tun.roundSummaryTime + g.tun.countdown + 0.5f);
                c.C(g.match == MatchState.InProgress && g.round == 2 && g.roundClock > 55f && !g.overtime, "the next round starts its own 1:00");
            }
            {   // Advanced, timed, tied at time: sudden death, the next point wins the round
                var @in = new PlayerInput[2]; var g = Setup(@in, true, true, -1, 180, 60);
                var log = new List<GameEvent>();
                Run(g, @in, 61f, log);
                c.C(g.overtime && HasEvent(log, EventType.Overtime) && g.match == MatchState.InProgress, "timed round tied 0-0 at time: sudden death");
                log.Clear();
                FireOpponentBall(g, g.pl[0].@in.head.p - new Vec3(0, 0.3f, 0));
                Run(g, @in, 2.0f, log);
                c.C(HasEvent(log, EventType.RoundEnd, 1) && g.pl[1].roundWins == 1 && g.pl[1].score == 1,
                    "sudden death: one point (1-0, short of 4) wins the round");
            }
            {
                var t = new Tunables();
                c.C(t.StatsMode() == 0, "stats mode: the rules as they come are standard");
                t.shields = false; t.basicMatchTime = 300;
                c.C(t.StatsMode() == 1, "stats mode: any match without shields is HOTDOG");
                t.shields = true;
                c.C(t.StatsMode() == 2, "stats mode: other options are custom");
                t.advanced = true; t.basicMatchTime = 180; t.advancedRoundTime = 0;
                c.C(t.StatsMode() == 0 && !t.FreePlay() && !t.Timed(), "Advanced without a round time is standard and untimed");
                t.knuckleMode = 0;   // "off" chosen, then Advanced (which has none): still the rules as they come
                c.C(t.StatsMode() == 0, "knuckle guards count by what is in play, not by how they were chosen");
            }
            c.Done();
        }

        [Test]
        public void FoulBallStaysInTheArena()
        {
            var c = new Checks("foul ball: stays in the arena (owner, 2026-10-21)", 17);
            {   // a low ball landing just short of the far foul line: it bounces on as off the floor (2026-10-28: the zone's
                // edge sent it nearly straight up, then it bounced between floor and ceiling)
                var in0 = new PlayerInput[2]; var g0 = MakeGame(in0);
                int upright = 0;
                for (int k = 0; k < 40 * 5; ++k)
                {   // landing spots 1 cm apart across the line, for every ball size
                    Game g = g0.Clone();
                    var @in = new[] { in0[0].Clone(), in0[1].Clone() };
                    Ball b = g.pl[0].ball;
                    g.pl[1].strikesAgainst = k / 40;
                    b.scale = 1f + g.tun.scaleIncreasePerStrike * (k / 40);
                    b.state = BallState.Flying; b.instigator = 0; b.rechargePending = false; b.scored = false; b.spin = default;
                    b.pos = new Vec3(4.0f + 0.01f * (k % 40), 0.6f, -1.6f);
                    b.speedLimit = 7f; b.vel = Normalize(new Vec3(6.5f, -3.7f, 2.0f)) * 7f;
                    float vx = b.vel.x;
                    bool bounced = false;
                    for (int i = 0; i < 72 && !bounced; ++i)
                    {
                        Step(g, @in);
                        bounced = HasEvent(g.events, EventType.Bounce, 0);
                    }
                    if (!bounced || b.vel.x < 0.8f * vx) ++upright;
                }
                c.C(upright == 0, "a ball landing at the foul line bounces on along the court, not straight up");
            }
            {   // your throw landing in the OTHER player's foul zone is no foul: it bounces on as off any floor (owner, 2026-10-28)
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 0; b.rechargePending = false; b.scored = false; b.spin = default;
                b.pos = new Vec3(4.6f, 1.0f, 1.0f); b.speedLimit = 9f; b.vel = Normalize(new Vec3(6.2f, 0f, 1.2f) - b.pos) * 9f;
                b.progX = b.pos.x; b.progT = 0; b.timer = 0;
                var log = new List<GameEvent>();
                bool bounced = false;
                for (int i = 0; i < 36 && !bounced; ++i)
                {
                    Step(g, @in);
                    log.AddRange(g.events);
                    bounced = HasEvent(g.events, EventType.Bounce, 0);
                }
                c.C(bounced && !HasEvent(log, EventType.Foul) && b.state == BallState.Flying && b.vel.y > 0 && !b.homing,
                    "your throw landing in their foul zone just bounces (no foul)");
            }
            {   // your ball blocked or punched into THEIR own foul zone: their strike, and it comes back at you as from their
                // back wall, live (owner, 2026-10-29)
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 1; b.rechargePending = false; b.scored = false; b.spin = default;   // (their touch)
                b.pos = new Vec3(5.6f, 0.9f, 0.3f); b.speedLimit = 8f; b.vel = Normalize(new Vec3(0.4f, -1f, 0f)) * 8f;
                b.progX = b.pos.x; b.progT = 0; b.timer = 0;
                var log = new List<GameEvent>();
                for (int i = 0; i < 72 * 4 && !HasEvent(log, EventType.HitPlayer); ++i)
                {
                    Step(g, @in);
                    log.AddRange(g.events);
                }
                bool strikeThem = false, hitMe = false;
                foreach (var e in log)
                {
                    strikeThem |= e.type == EventType.Strike && e.player == 1;
                    hitMe |= e.type == EventType.HitPlayer && e.player == 0;
                }
                c.C(strikeThem && HasEvent(log, EventType.Returned, 0), "their block into their own foul zone's floor: their strike, and it comes back");
                c.C(hitMe, "... live: standing still, its owner is hit by it");
                c.C(!HasEvent(log, EventType.Foul), "... and no FOUL shown for it (it was never their throw)");
            }
            {   // ... but off their own end's side wall it is a wall: it bounces on (owner, 2026-10-30)
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 1; b.rechargePending = false; b.scored = false; b.spin = default;   // (their touch)
                b.pos = new Vec3(6.5f, 1.5f, 1.6f); b.speedLimit = 8f; b.vel = Normalize(new Vec3(0.3f, 0f, 1f)) * 8f;
                b.progX = b.pos.x; b.progT = 0; b.timer = 0;
                var log = new List<GameEvent>();
                for (int i = 0; i < 36; ++i) { Step(g, @in); log.AddRange(g.events); }
                bool strikeThem = false, bounced = false;
                foreach (var e in log) { strikeThem |= e.type == EventType.Strike && e.player == 1; bounced |= e.type == EventType.Bounce; }
                c.C(bounced && !strikeThem && !HasEvent(log, EventType.Foul) && b.instigator == 1,
                    "their block off their own end's side wall: a plain bounce (no strike, no foul)");
            }
            {   // past the foul line, then back down into its own foul zone: bounce, wall, home; harmless
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 0; b.rechargePending = false; b.scored = false; b.spin = default;
                b.pos = new Vec3(-3.6f, 1.3f, 0.4f);
                b.speedLimit = 8f; b.vel = Normalize(new Vec3(-5.6f, 0f, 1.2f) - b.pos) * 8f;
                var log = new List<GameEvent>();
                bool dissolved = false, homeBound = false;
                for (int i = 0; i < 72 * 8 && b.state != BallState.Idle; ++i)
                {
                    Step(g, @in);
                    log.AddRange(g.events);
                    dissolved |= b.state == BallState.Spawning || b.state == BallState.Dead;
                    homeBound |= b.homing;
                }
                c.C(HasEvent(log, EventType.Foul, 0), "a ball landing in its owner's foul zone is a foul");
                c.C(!dissolved && !HasEvent(log, EventType.SpawnRise), "it no longer dissolves or rises from under the floor");
                c.C(homeBound && b.state == BallState.Idle && Length(b.pos - g.IdlePoint(0)) < 0.6f,
                    "it bounces off the wall it was heading for and flies home to its launch spot");
                c.C(g.pl[0].score == 0 && g.pl[1].score == 0 && !HasEvent(log, EventType.HitPlayer), "nobody scores from a fouled ball");
            }
            {   // never crossed the foul line: springs straight back to its float position
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 0; b.rechargePending = true; b.scored = false; b.spin = default;
                b.pos = new Vec3(-5.4f, 0.9f, 0.6f); b.speedLimit = 8f; b.vel = Normalize(new Vec3(-5.2f, 0f, 0.8f) - b.pos) * 8f;
                var log = new List<GameEvent>();
                bool dissolved = false;
                float home = -1;
                for (int i = 0; i < 72 * 3; ++i)
                {
                    Step(g, @in);
                    log.AddRange(g.events);
                    dissolved |= b.state == BallState.Spawning || b.state == BallState.Dead;
                    if (home < 0 && b.state == BallState.Idle) home = i / 72f;
                }
                TestContext.Out.WriteLine($"  (back at its float position after {home:F2} s)");
                c.C(!dissolved && b.state == BallState.Idle && Length(b.pos - g.IdlePoint(0)) < 0.2f,
                    "a ball that never crossed the foul line bounces and is drawn back to its float position (no dissolving)");
                c.C(home > 0 && home < 1.0f, "and it is back within a second (the magnetic pull)");
            }
            {   // stuck balls (2026-10-28): no headway along the court, or slowed to a crawl, go home harmless
                void Fly(Vec3 pos, Vec3 vel, float seconds, List<GameEvent> log, out bool home, out float at)
                {
                    var @in = new PlayerInput[2]; var g = MakeGame(@in);
                    Ball b = g.pl[0].ball;
                    b.state = BallState.Flying; b.instigator = 0; b.rechargePending = false; b.scored = false; b.spin = default;
                    b.pos = pos; b.vel = vel; b.speedLimit = Length(vel); b.timer = 0; b.progX = pos.x; b.progT = 0;
                    home = false; at = -1;
                    for (int i = 0; i < (int)(72 * seconds); ++i)
                    {
                        Step(g, @in);
                        log.AddRange(g.events);
                        if (b.homing && at < 0) at = i / 72f;
                        home |= b.state == BallState.Idle;
                    }
                }
                var log = new List<GameEvent>();
                Fly(new Vec3(1.0f, 1.8f, 0.3f), new Vec3(0.2f, 9f, 0.1f), 6f, log, out bool home, out float at);   // between floor and ceiling, all but upright
                c.C(home && at > 1.8f && at < 2.6f && !HasEvent(log, EventType.HitPlayer),
                    "a ball bouncing up and down without getting anywhere goes home after 2 s, harmless");
                log.Clear();
                Fly(new Vec3(-2.0f, 1.5f, 0f), new Vec3(1.7f, 0f, 0.3f), 4f, log, out home, out at);   // a crawl past the foul line
                c.C(home && at > 0.9f && at < 1.2f, "a ball crawling under 2 m/s goes home (after a second)");
                log.Clear();
                Fly(new Vec3(-2.0f, 1.5f, 0f), new Vec3(12f, 0.5f, 3f), 1.2f, log, out home, out at);   // a real throw: untouched
                c.C(at < 0, "a throw on its way down the court is left alone");
            }
            {   // a fouled ball keeps its own speed to the wall, not a jump to 12 m/s (2026-10-28)
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[1].ball;   // orange's ball, knocked back into orange's own zone
                b.state = BallState.Flying; b.instigator = 1; b.rechargePending = false; b.scored = false; b.spin = default;
                b.pos = new Vec3(5.4f, 0.8f, 0.3f); b.speedLimit = 6f; b.vel = Normalize(new Vec3(0.6f, -1f, 0f)) * 6f;
                b.progX = b.pos.x; b.progT = 0; b.timer = 0;
                float speedAfter = -1;
                for (int i = 0; i < 72 && speedAfter < 0; ++i)
                {
                    Step(g, @in);
                    if (HasEvent(g.events, EventType.Foul)) speedAfter = b.speedLimit;
                }
                c.C(speedAfter > 5.9f && speedAfter < 6.1f, "a fouled ball keeps its own speed");
            }
            {   // a weak throw that never reaches the foul line: drawn home by the same pull
                var @in = new PlayerInput[2]; var g = MakeGame(@in);
                Ball b = g.pl[0].ball;
                b.state = BallState.Flying; b.instigator = 0; b.rechargePending = true; b.scored = false; b.spin = default;
                b.pos = new Vec3(-5.6f, 1.4f, 0.2f); b.speedLimit = 2f; b.vel = new Vec3(-0.4f, 0.3f, 0f);
                float home = -1;
                for (int i = 0; i < 72 * 3 && home < 0; ++i)
                {
                    Step(g, @in);
                    if (b.state == BallState.Idle) home = i / 72f;
                }
                c.C(home > 0 && home < 1.0f, "a weak throw is drawn back home within a second");
            }
            c.Done();
        }
    }
}
