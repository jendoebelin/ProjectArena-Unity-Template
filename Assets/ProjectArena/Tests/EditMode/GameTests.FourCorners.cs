// FOUR CORNERS (task A4), ported from the native tests/test_game.cpp (native a7230f8): four players, their teams
// (FREE-FOR-ALL, 2 v 2, 3 v 1 with THE ONE), the clock and sudden death, the lead-change cue, the corners, strikes held
// by the thrower, four bots playing; and READY UP in a room for four (FIREBALL BLITZ's room too; its rules:
// GameTests.Blitz.cs).
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        // a person standing at corner p, facing the centre
        static PlayerInput Stand4(Game g, int p)
        {
            var @in = new PlayerInput { headTracked = true };
            Vec3 f = g.Facing(p);
            Quat face = Quat.AxisAngle(new Vec3(0, 1, 0), System.MathF.Atan2(-f.x, -f.z));
            @in.head = new Pose(face, g.tun.start[p] + new Vec3(0, 1.65f, 0));
            for (int h = 0; h < 2; ++h)
            {
                @in.hand[h].tracked = true;
                @in.hand[h].grip = new Pose(face, g.tun.start[p] + new Vec3(0, 1.0f, 0) + f * 0.2f + face.Rotate(new Vec3(h == 0 ? -0.3f : 0.3f, 0, 0)));
            }
            return @in;
        }

        static void Step4(Game g, PlayerInput[] @in, float dt = 1f / 72)
        {
            var tmp = new PlayerInput[@in.Length];
            for (int p = 0; p < @in.Length; ++p) tmp[p] = @in[p]?.Clone() ?? new PlayerInput();
            g.Step(dt, tmp);
        }

        static Game Make4(int mode, uint bots, PlayerInput[] @in)
        {
            Game g = NewGame(); g.tun.players = 4; g.tun.teamMode = mode; g.tun.meetTime = 0;
            g.UseArena(Arena.FourCorners, Tris); g.ResetWithBots(bots);
            for (int p = 0; p < 4; ++p) @in[p] = Stand4(g, p);
            for (int i = 0; i < (int)(72 * 3.6f); ++i) Step4(g, @in);
            return g;
        }

        static void Fire4(Game g, PlayerInput[] @in, int owner, Vec3 from, Vec3 vel, float seconds, List<GameEvent> log)
        {
            Ball b = g.pl[owner].ball;
            b.state = BallState.Flying; b.instigator = owner; b.rechargePending = true; b.scored = false; b.spin = default;
            b.pos = from; b.vel = vel; b.speedLimit = Length(vel); b.timer = 0; b.progX = from.x; b.progZ = from.z; b.progT = 0;
            b.bounces = 0; b.fouled = b.homing = false; b.heldHand = -1;
            for (int i = 0; i < (int)(72 * seconds); ++i)
            {
                Step4(g, @in);
                log.AddRange(g.events);
                if (b.state != BallState.Flying && b.state != BallState.Returning) break;
            }
        }

        static int Leads(List<GameEvent> log, ref float to)
        {
            int n = 0;
            foreach (GameEvent e in log) if (e.type == EventType.LeadChange) { ++n; to = e.value; }
            return n;
        }

        [Test, Timeout(600000)]
        public void FourCorners()
        {
            var c = new Checks("FOUR CORNERS: four players, teams", 19);
            {
                var @in = new PlayerInput[4]; Game g = Make4(Teams.FFA, 0, @in);
                c.C(g.np == 4 && g.match == MatchState.InProgress && g.arenaKind == Arena.FourCorners, "four players in FOUR CORNERS: the match starts (no fist bump)");
                bool facesIn = true;
                for (int p = 0; p < 4; ++p) facesIn &= Dot(g.Facing(p), new Vec3(0, 0, 0) - g.tun.start[p]) > 6.9f;
                c.C(facesIn && g.TeamCount() == 4, "each faces the centre; free-for-all: four teams");
            }
            {   // 2 v 2: partners in opposite corners
                var @in = new PlayerInput[4]; Game g = Make4(Teams.TwoVTwo, 0, @in);
                c.C(g.Team(0) == g.Team(1) && g.Team(2) == g.Team(3) && g.Team(0) != g.Team(2), "2 v 2: partners in opposite corners");
                var log = new List<GameEvent>();
                Vec3 from = g.tun.start[0] + new Vec3(1.6f, 1.3f, 0);
                Fire4(g, @in, 0, from, Normalize(g.ChestPoint(1) - from) * 12f, 2f, log);
                c.C(!HasEvent(log, EventType.HitPlayer), "a ball thrown at your partner goes through them");
                var in2 = new PlayerInput[4]; Game g2 = Make4(Teams.TwoVTwo, 0, in2);
                var log2 = new List<GameEvent>();
                Vec3 from2 = g2.tun.start[0] + new Vec3(1.6f, 1.3f, 0);
                Fire4(g2, in2, 0, from2, Normalize(g2.ChestPoint(2) - from2) * 12f, 2f, log2);
                c.C(HasEvent(log2, EventType.HitPlayer, 2) && g2.TeamScore(0) == 1 && g2.TeamScore(1) == 0, "a hit on a rival scores for your team");
            }
            {   // 3 v 1: THE ONE's hits count double
                var @in = new PlayerInput[4]; Game g = Make4(Teams.ThreeVOne, 0, @in);
                c.C(g.Team(0) != g.Team(1) && g.Team(1) == g.Team(2) && g.Team(2) == g.Team(3), "3 v 1: THE ONE (slot 0) against the other three");
                var log = new List<GameEvent>();
                Vec3 from = g.tun.start[0] + new Vec3(1.6f, 1.3f, 0);
                Fire4(g, @in, 0, from, Normalize(g.ChestPoint(3) - from) * 12f, 2f, log);
                c.C(HasEvent(log, EventType.HitPlayer, 3) && g.pl[0].score == 2, "THE ONE's hit counts two");
            }
            {   // the clock: the most points wins; a tie, sudden death
                var @in = new PlayerInput[4]; Game g = Make4(Teams.FFA, 0, @in);
                g.pl[2].score = 3; g.pl[0].score = 1; g.roundClock = 0.01f;
                for (int i = 0; i < 3; ++i) Step4(g, @in);
                c.C(g.match == MatchState.MatchSummary && g.matchWinner == 2, "at time, the player with the most points wins");
                var in2 = new PlayerInput[4]; Game g2 = Make4(Teams.FFA, 0, in2);
                g2.pl[1].score = 2; g2.pl[3].score = 2; g2.roundClock = 0.01f;
                for (int i = 0; i < 3; ++i) Step4(g2, in2);
                c.C(g2.match == MatchState.InProgress && g2.overtime, "a tie at the top: sudden death");
            }
            {   // a new leader on its own: the lead-change cue (not on a tie, not on the deciding point)
                var @in = new PlayerInput[4]; Game g = Make4(Teams.FFA, 0, @in);
                var log = new List<GameEvent>();
                Vec3 from = g.tun.start[0] + new Vec3(1.6f, 1.3f, 0);
                Fire4(g, @in, 0, from, Normalize(g.ChestPoint(2) - from) * 12f, 2f, log);
                float to = -1;
                c.C(HasEvent(log, EventType.HitPlayer, 2) && Leads(log, ref to) == 1 && to == 0f && g.Leader() == 0, "the first point: a new leader, the cue");
                var in2 = new PlayerInput[4]; Game g2 = Make4(Teams.FFA, 0, in2);
                g2.pl[3].score = 1;
                var log2 = new List<GameEvent>();
                Fire4(g2, in2, 0, from, Normalize(g2.ChestPoint(2) - from) * 12f, 2f, log2);
                c.C(HasEvent(log2, EventType.HitPlayer, 2) && Leads(log2, ref to) == 0 && g2.Leader() == -1, "level at the top: no cue");
                var in3 = new PlayerInput[4]; Game g3 = Make4(Teams.ThreeVOne, 0, in3);
                g3.pl[2].score = 1;
                var log3 = new List<GameEvent>();
                Fire4(g3, in3, 0, from, Normalize(g3.ChestPoint(3) - from) * 12f, 2f, log3);
                c.C(Leads(log3, ref to) == 1 && to == (float)g3.Team(0) && g3.Leader() == g3.Team(0), "THE ONE's double overtakes the three: the cue");
                var in4 = new PlayerInput[4]; Game g4 = Make4(Teams.TwoVTwo, 0, in4);
                g4.overtime = true;
                var log4 = new List<GameEvent>();
                Vec3 from4 = g4.tun.start[1] + new Vec3(-1.6f, 1.3f, 0);
                Fire4(g4, in4, 1, from4, Normalize(g4.ChestPoint(2) - from4) * 12f, 2f, log4);
                c.C(g4.match == MatchState.MatchSummary && g4.matchWinner == 0 && HasEvent(log4, EventType.MatchEnd, 0) && Leads(log4, ref to) == 0,
                    "sudden death: the match ends (the event names the winning team), no lead cue");
            }
            {   // every corner is its player's back wall; past your platform's edge a throw recharges
                var @in = new PlayerInput[4]; Game g = Make4(Teams.FFA, 0, @in);
                var log = new List<GameEvent>();
                Vec3 from = new Vec3(-2f, 3.0f, -3f);
                Fire4(g, @in, 0, from, Normalize(new Vec3(1.6f, 3.2f, -9.2f) - from) * 13f, 2f, log);
                bool back = false;
                foreach (GameEvent e in log) back |= e.type == EventType.Returned && e.player == 0 && e.pos.z < -7f;
                c.C(back, "a ball into another player's corner comes back to its thrower");
                var in2 = new PlayerInput[4]; Game g2 = Make4(Teams.FFA, 0, in2);
                var log2 = new List<GameEvent>();
                g2.pl[2].ball.charged = false;
                Fire4(g2, in2, 2, g2.IdlePoint(2), new Vec3(0.3f, 0.1f, 1f) * 10f, 0.4f, log2);
                c.C(!g2.pl[2].ball.rechargePending && g2.pl[2].ball.charged, "a corner player's throw past the platform's edge recharges");
                var in3 = new PlayerInput[4]; Game g3 = Make4(Teams.FFA, 0, in3);
                in3[2].head.p.x += 1.6f;   // standing aside: the ring behind them is open
                for (int i = 0; i < 4; ++i) Step4(g3, in3);
                var log3 = new List<GameEvent>();
                Fire4(g3, in3, 0, new Vec3(0, 1.28f, -3f), new Vec3(0, 0, -12f), 0.6f, log3);
                c.C(HasEvent(log3, EventType.Strike, 2) && g3.pl[0].strikesFor == 1, "through a corner player's strike ring: a strike, held by the thrower");
            }
            string[] names = { "FREE-FOR-ALL", "2 V 2", "3 V 1" };
            for (int mode = 0; mode < 3; ++mode)
            {   // four bots, a match each way
                Game g = NewGame(); g.tun.players = 4; g.tun.teamMode = mode; g.tun.basicMatchTime = 0; g.tun.botLevel = 1;
                g.UseArena(Arena.FourCorners, Tris); g.ResetWithBots(0xF);
                var @in = new PlayerInput[4];
                var log = new List<GameEvent>();
                for (int i = 0; i < 72 * 120; ++i)
                {
                    Step4(g, @in);
                    log.AddRange(g.events);
                }
                int[] throws = new int[4], hits = new int[4];
                int nan = 0;
                foreach (GameEvent e in log)
                {
                    if (e.type == EventType.Throw && e.player >= 0) ++throws[e.player];
                    if (e.type == EventType.HitPlayer && e.value >= 0) ++hits[(int)e.value];
                }
                for (int p = 0; p < 4; ++p) nan += !float.IsFinite(g.pl[p].ball.pos.x) || !float.IsFinite(g.pl[p].@in.head.p.x) ? 1 : 0;
                int scorers = 0, sum = 0;
                for (int p = 0; p < 4; ++p) { scorers += hits[p] > 0 ? 1 : 0; sum += g.pl[p].score; }
                TestContext.Out.WriteLine($"       {names[mode]}, four AMATEUR bots, 2 min: throws {throws[0]} {throws[1]} {throws[2]} {throws[3]}, " +
                                          $"hits {hits[0]} {hits[1]} {hits[2]} {hits[3]}, team scores {g.TeamScore(0)} / {g.TeamScore(1)}");
                c.C(throws[0] > 4 && throws[1] > 4 && throws[2] > 4 && throws[3] > 4 && scorers >= 2 && nan == 0 && sum > 0,
                    $"{names[mode]}: four bots all throw and score, nothing breaks");
            }
            c.Done();
        }

        [Test]
        public void ReadyUpRoomForFour()
        {
            var c = new Checks("READY UP: a room for four starts when every person in it pressed A (0.3.14)", 2);
            for (int room = 1; room <= 2; ++room)
            {   // FOUR CORNERS and FIREBALL BLITZ: two people, two bots
                var g = new Game();
                g.tun.experimental = true; g.tun.expRoom = room; g.tun.players = 4;
                if (room == Room.Blitz) g.UseArena(Arena.Octagon, new List<CollisionTri>());
                g.tun.readyUp = true;
                g.ResetWithBots(0xCu);
                var @in = new PlayerInput[Teams.MaxPlayers];
                void RunFor(float secs, bool a0, bool a1)
                {
                    for (int i = 0; i < (int)(secs * 90); ++i)
                    {
                        for (int p = 0; p < 2; ++p)
                        {
                            @in[p] = new PlayerInput { headTracked = true };
                            @in[p].head = new Pose(Quat.Identity, g.tun.start[p] + new Vec3(0, 1.6f, 0));
                            @in[p].buttonA = p == 0 ? a0 : a1;
                        }
                        g.Step(1f / 90f, @in);
                    }
                }
                RunFor(3f, false, false);
                bool waited = g.match == MatchState.WaitingForPlayers;
                RunFor(0.2f, true, false);
                RunFor(3f, false, false);
                bool waitedForOne = g.match == MatchState.WaitingForPlayers && g.pl[0].ready && !g.pl[1].ready;
                RunFor(0.2f, false, true);
                RunFor(0.3f, false, false);
                c.C(waited && waitedForOne && g.match == MatchState.Countdown,
                    (room == 1 ? "FOUR CORNERS" : "FIREBALL BLITZ") + ": nothing starts until everyone is READY, then the countdown");
            }
            c.Done();
        }
    }
}
