// BALL LOOK-AHEAD (task B5, native 0.3.15 fc80084): Sim/LookAhead.cs on a game as a player who isn't hosting has
// it (no snapshots needed: LookAhead works on whatever the game holds), and the snapshots' new tail through a private
// local relay (never the live server): each ball's spin, bounces and the host's game clock.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;
using Node = ProjectArena.Tests.RoomTests.Node;

namespace ProjectArena.Tests
{
    public class LookAheadTests
    {
        const int RelayPort = 47801, CrowdPort = 47802;

        static Game NewGame()
        {
            var g = new Game();
            g.SetArena(ArenaCollision.Load(File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            g.match = MatchState.InProgress;
            return g;
        }

        [Test]
        public void MyThrowLeavesMyHandAtOnce()
        {
            var g = NewGame();
            const int L = 1;
            PlayerState me = g.pl[L];
            me.@in.headTracked = true;
            me.@in.head = new Pose(Quat.Identity, g.tun.start[L] + new Vec3(0, 1.6f, 0));
            for (int h = 0; h < 2; ++h) me.@in.hand[h].tracked = true;
            me.ball.state = BallState.Held; me.ball.heldHand = 1; me.ball.charged = true;
            Vec3 grip = g.tun.start[L] + new Vec3(0.3f, 1.3f, 0.3f);
            const float dt = 1f / 90f;
            for (int i = 0; i < 12; ++i)
            {   // the arm swings toward the other end (-X) at 13.5 m/s, the trigger held
                me.@in.hand[1].grip = new Pose(Quat.Identity, grip + new Vec3(-0.15f * i, 0, 0));
                me.@in.hand[1].trigger = 1f;
                g.LookAhead(L, 0.15f, dt);
            }
            Assert.That(g.pl[L].ball.state, Is.EqualTo(BallState.Held), "held while the trigger is");
            Assert.That(g.pl[L].hands[1].shieldUp, "a charged ball's shield is up at once");
            me.@in.hand[1].grip = new Pose(Quat.Identity, grip + new Vec3(-0.15f * 12, 0, 0));
            me.@in.hand[1].trigger = 0f;   // let go
            g.LookAhead(L, 0.15f, dt);
            Ball b = g.pl[L].ball;
            Assert.That(g.ahead.mine, "the throw is launched here");
            Assert.That(b.state, Is.EqualTo(BallState.Flying), "and drawn flying at once (the host's snapshots still have it in my hand)");
            Assert.That(b.vel.x, Is.LessThan(-5f), "toward the other end");
            float x0 = b.pos.x;
            for (int i = 0; i < 9; ++i) g.LookAhead(L, 0.15f, dt);
            Assert.That(g.pl[L].hands[1].shieldUp, Is.False, "the shield is down from the next frame (as native: worked out before the throw)");
            Assert.That(g.pl[L].ball.pos.x, Is.LessThan(x0 - 0.5f), "it flies on frame by frame");
        }

        // a spectator's copy of the match it watches, from FIREBALL BLITZ to a DIAMOND room (native b298cc7 test_rooms; the
        // owner, 2026-11-16: the PC spectator drew DIAMOND's platforms at the ring's places, the players away from them)
        [Test]
        public void FromBlitzToDiamondThePlacesComeHome()
        {
            var full = new Game();
            full.tun.experimental = true; full.tun.expRoom = Room.Blitz; full.tun.players = 4;
            full.UseArena(Arena.Octagon, new List<CollisionTri>()); full.ResetWithBots(0u);
            full.startMask = 0xF; full.ringMask = 0xF; full.LayoutRing(0xF);
            var diamond = new Game();
            diamond.tun.experimental = true; diamond.tun.expRoom = Room.Diamond;
            diamond.UseArena(Arena.Diamond, new List<CollisionTri>()); diamond.ResetWithBots(2u);
            var seen = new Game();
            bool home = Online.Net.SnapshotInto(full, seen) && M.Length(seen.tun.start[1] - full.tun.start[1]) < 1e-3f;
            home &= Online.Net.SnapshotInto(diamond, seen);
            for (int p = 0; p < 2; ++p)
                home &= M.Length(seen.tun.start[p] - diamond.tun.start[p]) < 1e-3f && M.Length(seen.tun.spawner[p] - diamond.tun.spawner[p]) < 1e-3f;
            Assert.That(home, "a watched match from FIREBALL BLITZ to DIAMOND: the players' places are the room's own again");
            Assert.That(seen.ringMask, Is.EqualTo(0), "the ring forgotten");
        }

        [Test]
        public void TheirBallIsDrawnAheadByMyEndsShareOfTheLead()
        {
            var g = NewGame();
            const int L = 1;
            Ball b = g.pl[0].ball;   // the other's ball, at centre court, coming my way
            b.state = BallState.Flying; b.pos = new Vec3(0, 1.3f, 0); b.vel = new Vec3(10f, 0, 0); b.speedLimit = 10f; b.instigator = 0;
            float xMe = g.tun.start[L].x, xThem = g.tun.start[0].x;
            float w = (0f - xThem) / (xMe - xThem);   // (native's weight: all the lead at my end, none at theirs)
            g.LookAhead(L, 0.2f, 1f / 90f);
            float moved = g.pl[0].ball.pos.x;
            TestContext.Out.WriteLine($"weight {w:F2}: drawn {moved:F3} m ahead (straight flight: {10f * 0.2f * w:F3})");
            Assert.That(moved, Is.EqualTo(10f * 0.2f * w).Within(0.15f), "flown on by the lead's share at this place");
            Assert.That(g.ahead.valid[0], "and kept for the next frame's glide");
            var four = NewGame();
            four.np = 4;
            float t0 = four.now;
            four.LookAhead(L, 0.2f, 1f / 90f);
            Assert.That(four.now, Is.EqualTo(t0), "four players with no host clock (a host before 0.3.24): it stands down");
        }

        [Test]
        public void FourCornersFromAHostThatSendsItsClock()
        {   // BALL LOOK-AHEAD with four players (TASKS.md E48, native 8ea32fc test_rooms, 0.3.24): FOUR CORNERS hosts send
            // the balls' spin, bounces and fouls and their clock after the last byte older builds read; a player not hosting
            // flies the balls on from it
            var host = new Game();
            host.tun.experimental = true; host.tun.expRoom = Room.Corners; host.tun.players = 4;
            host.UseArena(Arena.FourCorners, new List<CollisionTri>()); host.ResetWithBots(0u);
            host.now = 12.5f;
            for (int p = 0; p < 4; ++p)
            {   // (everyone standing in their corner, facing the middle, the hands down)
                PlayerInput inp = host.pl[p].@in;
                Vec3 f = host.Facing(p);
                Quat face = Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-f.x, -f.z));
                inp.headTracked = true;
                inp.head = new Pose(face, host.tun.start[p] + new Vec3(0, 1.65f, 0));
                for (int h = 0; h < 2; ++h)
                {
                    inp.hand[h].tracked = true;
                    inp.hand[h].trigger = 0;
                    inp.hand[h].grip = new Pose(face, host.tun.start[p] + new Vec3(0, 0.9f, 0) + face.Rotate(new Vec3(h == 0 ? -0.25f : 0.25f, 0, 0)));
                }
            }
            Vec3 mine = host.tun.start[1], other = host.tun.start[3];
            void Shoot(Game g, Vec3 from)
            {   // ball 2 flying at corner 1 from `from`, 10 m/s, curving
                Ball b = g.pl[2].ball;
                b.state = BallState.Flying; b.instigator = 2; b.heldHand = -1; b.scale = 1f;
                b.pos = from;
                Vec3 to = mine + new Vec3(0, 1.5f, 0) - from;
                b.vel = M.Normalize(new Vec3(to.x, 0, to.z)) * 10f;
                b.spin = new Vec3(0, 3f, 0); b.bounces = 2; b.fouled = true; b.homing = false;
            }
            Shoot(host, new Vec3(0, 1.5f, 0));
            Game seen = new Game(), old = new Game();
            bool read = Online.Net.SnapshotInto(host, seen) && seen.np == 4;
            Ball sb = seen.pl[2].ball;
            Assert.That(read && Math.Abs(seen.ahead.snapNow - 12.5f) < 1e-3f && Math.Abs(sb.spin.y - 3f) < 0.01f && sb.bounces == 2 && sb.fouled,
                        "four players: the snapshot carries each ball's spin, bounces and foul, and the host's clock (0.3.24)");
            Assert.That(Online.Net.SnapshotInto(host, old, 4 * 7 + 4) && old.np == 4 && old.ahead.snapNow < 0 && old.pl[2].ball.spin.y == 0f &&
                        old.tun.palmHold == seen.tun.palmHold, "... a host before 0.3.24 sends none: everything else read as before");
            host.tun.expRoom = Room.Blitz;
            var blitz = new Game();
            Assert.That(Online.Net.SnapshotInto(host, blitz) && blitz.pl[2].ball.spin.y == 0f && blitz.ahead.fireKnown,
                        "... not in FIREBALL BLITZ, whose balls never fly (its fireballs have their own, TASKS.md E49)");
            host.tun.expRoom = Room.Corners;
            int fourBytes = Online.Net.SnapshotBytes(host);
            Assert.That(fourBytes, Is.LessThanOrEqualTo(1200), $"... a four-player snapshot with it and a full event log fits the relay ({fourBytes} of 1200 bytes)");
            const float lead = 0.2f, dt = 1f / 72;
            Vec3 at = sb.pos;
            seen.LookAhead(1, lead, dt);
            float flown = M.Length(seen.pl[2].ball.pos - at);
            old.LookAhead(1, lead, dt);
            float oldFlown = M.Length(old.pl[2].ball.pos - at);
            TestContext.Out.WriteLine($"four players: {fourBytes} bytes; drawn ahead {flown:F2} m of {10f * lead:F2}; from an older host {oldFlown:F4}");
            Assert.That(flown > 0.8f && flown < 2.1f && oldFlown < 1e-4f,
                        $"... a ball from the middle at my corner is drawn ahead ({flown:F2} m of {10f * lead:F2}); from an older host, as sent ({oldFlown:F2})");
            var beside = new Game();   // (the same ball beside another corner: what that player does with it is drawn where it happens)
            Shoot(host, other + host.Facing(3) * 0.8f + new Vec3(0, 1.5f, 0));
            Online.Net.SnapshotInto(host, beside);
            Vec3 nearAt = beside.pl[2].ball.pos;
            beside.LookAhead(1, lead, dt);
            float nearFlown = M.Length(beside.pl[2].ball.pos - nearAt);
            TestContext.Out.WriteLine($"beside another corner: {nearFlown:F2} m");
            Assert.That(nearFlown < flown / 3f, $"... beside another player's corner, hardly ahead at all ({nearFlown:F2} m)");
        }

        [Test]
        public void FireballBlitzFromAHostThatSendsItsClock()
        {   // BALL LOOK-AHEAD in FIREBALL BLITZ (TASKS.md E49, native b289ab9 test_rooms, 0.3.24): its hosts send each flying
            // fireball's spin and bounces and their clock; a player not hosting flies the fireballs on, and one at its last
            // bounce goes out on time
            var host = new Game();
            host.tun.experimental = true; host.tun.expRoom = Room.Blitz; host.tun.players = 4;
            host.UseArena(Arena.Octagon, new List<CollisionTri>()); host.ResetWithBots(0u);
            host.startMask = 0xF; host.ringMask = 0xF; host.LayoutRing(0xF);
            for (int p = 0; p < 4; ++p)
            {
                host.pl[p].health = 3;
                PlayerInput inp = host.pl[p].@in;
                Vec3 f = host.Facing(p);
                Quat face = Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-f.x, -f.z));
                inp.headTracked = true;
                inp.head = new Pose(face, host.tun.start[p] + new Vec3(0, 1.65f, 0));
                for (int h = 0; h < 2; ++h)
                {
                    inp.hand[h].tracked = true;
                    inp.hand[h].trigger = 0;
                    inp.hand[h].grip = new Pose(face, host.tun.start[p] + new Vec3(0, 0.9f, 0) + face.Rotate(new Vec3(h == 0 ? -0.25f : 0.25f, 0, 0)));
                }
            }
            host.now = 31.25f;
            void FireAt(Vec3 from, float vy, int bounces)
            {   // fireball 2/0 at place 1, 10 m/s, curving
                Ball fb = host.fire[2][0];
                fb.state = BallState.Flying; fb.instigator = 2; fb.heldHand = -1; fb.scale = 1f; fb.timer = 0;
                fb.pos = from;
                Vec3 to = host.tun.start[1] - from;
                fb.vel = M.Normalize(new Vec3(to.x, 0, to.z)) * 10f;
                fb.vel.y = vy;
                fb.speedLimit = 10f;
                fb.spin = new Vec3(0, 2f, 0); fb.bounces = bounces;
            }
            bool Guest(Game g, int cut)
            {   // (a guest's game is in the octagon before the snapshot comes)
                g.tun.experimental = true; g.tun.expRoom = Room.Blitz; g.tun.players = 4;
                g.UseArena(Arena.Octagon, new List<CollisionTri>());
                return Online.Net.SnapshotInto(host, g, cut);
            }
            FireAt(new Vec3(0, 1.0f, 0), 0f, 1);
            Game seen = new Game(), old = new Game();
            bool read = Guest(seen, 0);
            Ball sf = seen.fire[2][0];
            Assert.That(read && seen.ahead.fireKnown && Math.Abs(seen.ahead.snapNow - 31.25f) < 1e-3f && Math.Abs(sf.spin.y - 2f) < 0.01f && sf.bounces == 1,
                        "FIREBALL BLITZ: the snapshot carries each flying fireball's spin and bounces, and the host's clock (0.3.24)");
            Assert.That(Guest(old, 2 + 4) && !old.ahead.fireKnown && old.fire[2][0].state == BallState.Flying && old.fire[2][0].spin.y == 0f,
                        "... a host before 0.3.24 sends none: the fireballs read as before");
            const float lead = 0.2f, dt = 1f / 72;
            Vec3 at = sf.pos;
            seen.LookAhead(1, lead, dt);
            float flown = M.Length(seen.fire[2][0].pos - at);
            old.LookAhead(1, lead, dt);
            float oldFlown = M.Length(old.fire[2][0].pos - at);
            TestContext.Out.WriteLine($"FIREBALL BLITZ: drawn ahead {flown:F2} m of {10f * lead:F2}; from an older host {oldFlown:F4}");
            Assert.That(seen.fire[2][0].state == BallState.Flying && flown > 0.8f && flown < 2.1f && oldFlown < 1e-4f,
                        $"... a fireball from the middle at my place is drawn ahead ({flown:F2} m of {10f * lead:F2}); from an older host, as sent ({oldFlown:F2})");
            FireAt(new Vec3(0, 0.3f, 0), -3f, host.tun.fireBounces - 1);   // (about to hit the floor: its last bounce)
            Game last = new Game(), more = new Game();
            Guest(last, 0);
            last.LookAhead(1, lead, dt);
            FireAt(new Vec3(0, 0.3f, 0), -3f, 0);
            Guest(more, 0);
            more.LookAhead(1, lead, dt);
            Assert.That(last.fire[2][0].state != BallState.Flying && more.fire[2][0].state == BallState.Flying,
                        "... one at its last bounce goes out where it lands, one with bounces left hops on");
            host.fire[2][0].state = BallState.Dead;
            foreach (Ball[] row in host.fire)
                foreach (Ball fb in row) { fb.state = BallState.Flying; fb.pos = new Vec3(1, 1, 1); fb.instigator = 0; fb.spin = new Vec3(0, 30f, 0); fb.bounces = 3; }
            int bytes = Online.Net.SnapshotBytes(host);
            TestContext.Out.WriteLine($"twelve fireballs: {bytes} bytes");
            Assert.That(bytes, Is.LessThanOrEqualTo(1200), $"... twelve fireballs with it and a full event log fit the relay ({bytes} of 1200 bytes)");
            // (FIREBALL BLITZ in teams, 0.3.24, TASKS.md E51: the teams the room plays, in a byte after the clock; an older
            // host's TEAMS byte, which it sends for every room, never made FIREBALL BLITZ play in teams)
            host.tun.teamMode = Teams.ThreeVOne; host.tun.theOne = 2;
            Game t3 = new Game(), older = new Game();
            bool t3read = Guest(t3, 0);
            Assert.That(t3read && t3.tun.blitzTeams && t3.TeamsNow() == Teams.ThreeVOne && t3.Team(2) == 1 && t3.Team(0) == 0 && t3.MaxHealth(2) == 15,
                        "FIREBALL BLITZ in teams: the guest plays the host's 3 v 1 (0.3.24)");
            Assert.That(Guest(older, 1) && !older.tun.blitzTeams && older.TeamsNow() == Teams.FFA && older.Team(2) == 2,
                        "... a host before 0.3.24 (its TEAMS set to 3 v 1 for FOUR CORNERS): everyone for themselves, as it played");
            host.tun.matchVersion = Tunables.kVerBlitzTeams - 1;   // (an older player in the room)
            var fair = new Game();
            Assert.That(Guest(fair, 0) && fair.tun.blitzTeams && fair.TeamsNow() == Teams.FFA,
                        "... with an older player in the room: the host says everyone for themselves");
            host.tun.matchVersion = Tunables.kVerAll; host.tun.teamMode = Teams.FFA;
        }

        [Test, Timeout(120000)]
        public void SnapshotsCarryTheHostsClockAndSpin()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_ahead_test_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            var H = new Node("AHEAD HOST", -1f);
            var C = new Node("AHEAD GUEST", 1f);
            var all = new List<Node> { H, C };
            try
            {
                foreach (var n in all) n.net.GoOnline($"127.0.0.1:{RelayPort}");
                RoomTests.Pump(all, 1.0);
                H.game.tun.advanced = false; H.game.tun.meetTime = 0;
                H.game.tun.strikeSpeed = 2;   // (STRIKE SPEED FASTER: native 0.3.19 appends it to the snapshots)
                H.net.Host();
                RoomTests.Pump(all, 1.0);
                uint hip = 0;
                foreach (var lg in C.net.Games()) if (lg.name == "AHEAD HOST") hip = lg.ip;
                Assert.That(hip, Is.Not.EqualTo(0u), "the guest sees the room");
                C.net.Join(hip, true);
                RoomTests.Pump(all, 1.0);
                C.net.Queue(true);   // (into the tank: with someone playing, the host steps the game and sends snapshots)
                RoomTests.Pump(all, 3.0);
                TestContext.Out.WriteLine($"host clock {H.game.now:F2} s, as the guest last read it {C.game.ahead.snapNow:F2} s");
                Assert.That(C.game.ahead.snapNow, Is.GreaterThan(0f), "the host's game clock arrives with its snapshots (native 0.3.15's tail)");
                Assert.That(C.game.ahead.snapNow, Is.LessThanOrEqualTo(H.game.now + 1e-3f), "... no later than the host's own");
                Assert.That(C.game.ahead.snapNow, Is.GreaterThan(H.game.now - 1f), "... and recent");
                Assert.That(C.game.tun.strikeSpeed, Is.EqualTo(2), "the host's STRIKE SPEED reaches its player (native 0.3.19's tail)");
                // FAIR MATCHES (TASKS.md E2, native d757f8a): the host knows its player's game from the JOIN (these say
                // 0.3.11) and an option that game lacks plays at regulation for the match
                Assert.That(H.net.MatchVersion(), Is.EqualTo(Online.Net.VersionNumber("0.3.11")), "FAIR MATCHES: the host knows the oldest player's game");
                Assert.That(C.net.MatchVersion(), Is.EqualTo(Tunables.kVerAll), "... (not hosting: everything)");
                H.game.tun.matchVersion = H.net.MatchVersion();
                RoomTests.Pump(all, 0.5);
                Assert.That(C.game.tun.strikeSpeed == 0 && H.game.tun.PerStrike() == 0.5f, "... an older player in the match: STRIKE SPEED at regulation, for the host too");
                Assert.That(H.game.tun.strikeSpeed, Is.EqualTo(2), "... the host's choice kept for the next match");
                // FOUL LINE (TASKS.md E3, native 81e2880 test_rooms.cpp): the host's line, moved, is the players' too
                H.game.tun.matchVersion = Tunables.kVerAll;
                H.game.tun.foulShift = 2;
                RoomTests.Pump(all, 0.5);
                Assert.That(C.game.tun.foulShift == 2 && Math.Abs(C.game.FoulLine() - H.game.FoulLine()) < 1e-4f && C.game.FoulLine() < 4.0f,
                            "FOUL LINE: the host's line, moved, is the players' too");
                H.game.tun.matchVersion = H.net.MatchVersion();
                RoomTests.Pump(all, 0.5);
                Assert.That(C.game.tun.foulShift == 0 && H.game.FoulLine() == H.game.tun.foulLineX && H.game.tun.foulShift == 2,
                            "... an older player (0.3.11): the line where the arena has it for the match, the host's choice kept");
                H.game.tun.foulShift = 0; H.game.tun.matchVersion = Tunables.kVerAll;
                RoomTests.Pump(all, 0.5);
                Assert.That(C.game.tun.foulShift, Is.EqualTo(0), "... and back where the arena has it");
                // ROOMS AND THEIR OPTIONS (TASKS.md E22, native d4cf079 test_rooms.cpp): the host's MAIDEN, AIM ASSIST, STRIKE
                // ZONE, BALL GROWTH and ROUNDS reach the players; a player on 0.3.20 in the match: all as the rules say
                H.game.tun.maiden = true; H.game.tun.aimAssist = 3; H.game.tun.strikeZone = 2; H.game.tun.ballGrowth = 1;
                H.game.tun.advancedRoundsToWin = 3; H.game.tun.shieldSize = 2;
                RoomTests.Pump(all, 0.5);
                Assert.That(C.game.tun.MaidenNow() && C.game.tun.AimAssistNow() == 3 && C.game.tun.StrikeZoneNow() == 2 && C.game.tun.BallGrowthNow() == 1 &&
                            Math.Abs(C.game.tun.StrikeRadius() - 0.9f) < 1e-4f && C.game.tun.advancedRoundsToWin == 3 && C.game.tun.ShieldSizeNow() == 2,
                            "ROOM OPTIONS: the host's MAIDEN, AIM ASSIST, STRIKE ZONE and BALL GROWTH are the players' too");
                H.game.tun.matchVersion = H.net.MatchVersion();   // (a player on 0.3.11)
                RoomTests.Pump(all, 0.5);
                int rulesAim = H.game.tun.advanced ? 1 : 2;
                Assert.That(!C.game.tun.MaidenNow() && C.game.tun.AimAssistNow() == rulesAim && C.game.tun.StrikeZoneNow() == 1 &&
                            C.game.tun.BallGrowthNow() == 3 && C.game.tun.ShieldSizeNow() == 0 && !H.game.tun.MaidenNow() && H.game.tun.StrikeZoneNow() == 1,
                            "... an older player in the match: all as the rules say, for the host too");
                H.game.tun.maiden = false; H.game.tun.aimAssist = -1; H.game.tun.strikeZone = 1; H.game.tun.ballGrowth = 3;
                H.game.tun.advancedRoundsToWin = 2; H.game.tun.shieldSize = 0; H.game.tun.matchVersion = Tunables.kVerAll;
                RoomTests.Pump(all, 0.5);
                // BALL FEEL (TASKS.md E29, native 3129dc2 test_rooms.cpp): the host's ball is every player's at the arena
                C.game.tun.ballFeel = Tunables.BALL_FEEL_DEFAULT; H.game.tun.ballFeel = Tunables.BALL_CORE | Tunables.BALL_SPIN;
                RoomTests.Pump(all, 0.5);
                Assert.That(C.game.tun.ballFeel, Is.EqualTo(Tunables.BALL_CORE | Tunables.BALL_SPIN), "BALL FEEL: the host's ball (CORE SOLID, SPIN VISIBLE) is every player's at the arena");
                H.game.tun.ballFeel = Tunables.BALL_FEEL_DEFAULT;
                RoomTests.Pump(all, 0.5);
                // BOUNCES (TASKS.md E32, native 5d3bbec test_rooms.cpp): the host's WALLS, WALL GRIP and BANK ASSIST reach the
                // players (with each ball's turn still to come); a player on 0.3.21 in the match: STANDARD
                H.game.tun.wallsLively = true; H.game.tun.wallGrip = true; H.game.tun.bankAssist = 2;
                RoomTests.Pump(all, 0.5);
                Assert.That(C.game.tun.WallsLivelyNow() && C.game.tun.WallGripNow() && C.game.tun.BankAssistNow() == 2 &&
                            Math.Abs(C.game.tun.BankAssistDeg() - 1.6f) < 1e-4f,
                            "BOUNCES: the host's LIVELY WALLS, WALL GRIP and BANK ASSIST MEDIUM are the players' too");
                H.game.tun.matchVersion = Tunables.kVerBounces - 1;
                RoomTests.Pump(all, 0.5);
                Assert.That(!C.game.tun.wallsLively && !C.game.tun.wallGrip && C.game.tun.bankAssist == 0 && H.game.tun.BouncesStandard() && H.game.tun.wallGrip,
                            "... a player on 0.3.21 in the match: STANDARD, for the host too (its choice kept for the next match)");
                H.game.tun.wallsLively = H.game.tun.wallGrip = false; H.game.tun.bankAssist = 0;
                H.game.tun.matchVersion = Tunables.kVerAll;
                RoomTests.Pump(all, 0.5);
            }
            finally
            {
                foreach (var n in all) { if (n.net.Online) n.net.GoOffline(); n.net.Shutdown(); }
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }
    }
}
