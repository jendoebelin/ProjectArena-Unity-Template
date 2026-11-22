// The EXPERIMENTAL arenas for two (task A3), ported from the native tests/test_game.cpp (native e5cc0ae / a7230f8):
// CORNERS (the diagonal room, floating platforms) and DIAMOND (the classic court rolled 45 degrees). FOUR CORNERS' four
// players come with A4, FIREBALL BLITZ with A5.
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        // the native fire(): player 0's ball sent from `from` at `vel`, stepped until it stops flying (or `seconds`)
        static void Fire(Game g, PlayerInput[] @in, Vec3 from, Vec3 vel, float seconds, List<GameEvent> log, float[] lowest = null)
        {
            Ball b = g.pl[0].ball;
            b.state = BallState.Flying; b.instigator = 0; b.rechargePending = true; b.scored = false; b.spin = default;
            b.pos = from; b.vel = vel; b.speedLimit = Length(vel); b.timer = 0; b.progX = from.x; b.progT = 0; b.bounces = 0;
            b.fouled = b.homing = false; b.heldHand = -1;
            for (int i = 0; i < (int)(72 * seconds); ++i)
            {
                Step(g, @in);
                log.AddRange(g.events);
                if (lowest != null) lowest[0] = System.MathF.Min(lowest[0], b.pos.y);
                if (b.state != BallState.Flying && b.state != BallState.Returning) break;
            }
        }

        // many random throws: how many ended within 25 s, how many left the room
        static void RandomThrows(Game g, PlayerInput[] @in, uint seed, int n, out int ended, out int escaped)
        {
            float Rnd() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / (float)0xFFFFFF; }
            ended = escaped = 0;
            for (int k = 0; k < n; ++k)
            {
                g.match = MatchState.InProgress; g.roundClock = 1e6f;
                g.pl[0].score = g.pl[1].score = 0; g.pl[0].strikesAgainst = g.pl[1].strikesAgainst = 0;
                float yaw = (Rnd() - 0.5f) * 3f, pitch = (Rnd() - 0.5f) * 1.8f;
                Vec3 d = new Vec3(System.MathF.Cos(pitch) * System.MathF.Cos(yaw), System.MathF.Sin(pitch), System.MathF.Cos(pitch) * System.MathF.Sin(yaw));
                var log = new List<GameEvent>();
                Ball b = g.pl[0].ball;
                Fire(g, @in, g.IdlePoint(0), d * (2f + Rnd() * 17f), 25f, log);   // (a block restarts its 10 s)
                bool fine = float.IsFinite(b.pos.x) && System.MathF.Abs(b.pos.x) < g.tun.outX && System.MathF.Abs(b.pos.z) < g.tun.outZ;
                ended += b.state != BallState.Flying && b.state != BallState.Returning ? 1 : 0;
                escaped += fine ? 0 : 1;
            }
            TestContext.Out.WriteLine($"       {n} random throws: {ended} ended within 25 s, {escaped} left the room");
        }

        // two bots, a minute and a half
        static void BotsPlay(int kind, Checks c, string where)
        {
            Game g = NewGame(); g.UseArena(kind, Tris); g.Reset(true);
            g.pl[0].isBot = true;
            var @in = new[] { Standing(g, 0), new PlayerInput() };
            var log = new List<GameEvent>();
            Run(g, @in, 90f, log);
            int[] throws = { 0, 0 };
            int nan = 0, pts = 0;
            foreach (GameEvent e in log) { if (e.type == EventType.Throw && e.player >= 0) ++throws[e.player]; if (e.type == EventType.HitPlayer) ++pts; }
            for (int p = 0; p < 2; ++p) nan += !float.IsFinite(g.pl[p].ball.pos.x) || !float.IsFinite(g.pl[p].@in.head.p.x) ? 1 : 0;
            TestContext.Out.WriteLine($"       bots in the {where} arena: {throws[0]} and {throws[1]} throws, {pts} points");
            c.C(throws[0] > 3 && throws[1] > 3 && nan == 0 && pts > 0, $"the bot plays in the {where} arena (throws, scores)");
        }

        [Test, Timeout(600000)]
        public void ExperimentalCorners()
        {
            var c = new Checks("EXPERIMENTAL arena CORNERS: the diagonal room, floating platforms", 8);
            Game ExpGame(PlayerInput[] @in)
            {
                Game g = NewGame(); g.tun.meetTime = 0; g.UseArena(Arena.Corners, Tris); g.Reset(false);
                @in[0] = Standing(g, 0); @in[1] = Standing(g, 1);
                Run(g, @in, 3.6f);
                return g;
            }
            {
                var @in = new PlayerInput[2]; Game g = ExpGame(@in);
                c.C(g.arenaKind == 1 && g.tun.foulLineX > 5f && g.tun.outLow < ExpArena.kFloor, "the EXPERIMENTAL arena: its collision, foul line and bounds");
                var log = new List<GameEvent>();
                Vec3 from = g.pl[0].ball.pos;
                Fire(g, @in, from, Normalize(g.ChestPoint(1) - from) * 14f, 3f, log);
                c.C(HasEvent(log, EventType.HitPlayer, 1), "a throw straight at the other player hits them, corner to corner");
            }
            {
                var @in = new PlayerInput[2]; Game g = ExpGame(@in);
                var log = new List<GameEvent>();
                var lowest = new[] { 9f };
                Fire(g, @in, new Vec3(-4.0f, 0.8f, 0f), Normalize(new Vec3(1f, -0.9f, 0.1f)) * 12f, 2.5f, log, lowest);
                bool up = false;
                foreach (GameEvent e in log) up |= e.type == EventType.Bounce && e.pos.y < ExpArena.kFloor + 0.3f;
                c.C(up && lowest[0] < ExpArena.kFloor + 0.5f && !HasEvent(log, EventType.SpawnRise),
                    "past the platforms the ball drops to the floor below and bounces back up (no respawn)");
            }
            {
                var @in = new PlayerInput[2]; Game g = ExpGame(@in);
                var log = new List<GameEvent>();
                Fire(g, @in, g.pl[0].ball.pos, Normalize(new Vec3(0.3f, -1f, 0.4f)) * 6f, 3f, log);
                c.C(HasEvent(log, EventType.Foul, 0) && g.pl[0].ball.state == BallState.Idle, "your throw onto your own platform: a foul, back to your start");
            }
            {
                var @in = new PlayerInput[2]; Game g = ExpGame(@in);
                var log = new List<GameEvent>();
                Fire(g, @in, new Vec3(-5.5f, 1.4f, 0f), Normalize(new Vec3(1f, 0.05f, 0.06f)) * 14f, 3f, log);   // over their shoulder into their corner
                bool fromTheirCorner = false;
                foreach (GameEvent e in log) fromTheirCorner |= e.type == EventType.Returned && e.player == 0 && e.pos.x > ExpArena.kBackX;
                c.C(fromTheirCorner || HasEvent(log, EventType.HitPlayer, 1), "the corner behind the other player is their back wall (the ball returns)");
            }
            {   // many random throws: every one ends (hit, return, foul, home), nothing escapes or goes on for ever
                var @in = new PlayerInput[2]; Game g = ExpGame(@in);
                RandomThrows(g, @in, 777, 600, out int ended, out int escaped);
                c.C(ended == 600 && escaped == 0, "every throw ends, and none leaves the room");
            }
            BotsPlay(Arena.Corners, c, "EXPERIMENTAL");
            {
                var @in = new PlayerInput[2]; Game g = ExpGame(@in);
                g.UseArena(Arena.Classic, Tris);
                c.C(g.arenaKind == 0 && System.MathF.Abs(g.tun.foulLineX - 4.73f) < 1e-4f && g.tun.outZ < 5f, "and back to the classic court");
            }
            c.Done();
        }

        [Test, Timeout(600000)]
        public void ExperimentalDiamond()
        {
            var c = new Checks("EXPERIMENTAL arena DIAMOND: the classic court rolled 45 degrees, floating platforms", 9);
            Game Rolled(PlayerInput[] @in, bool solid = false)
            {
                Game g = NewGame(); g.tun.meetTime = 0; g.tun.solidPlatforms = solid; g.UseArena(Arena.Diamond, Tris); g.Reset(false);
                @in[0] = Standing(g, 0); @in[1] = Standing(g, 1);
                Run(g, @in, 3.6f);
                return g;
            }
            {
                var @in = new PlayerInput[2]; Game g = Rolled(@in);
                c.C(g.arenaKind == Arena.Diamond && System.MathF.Abs(g.tun.foulLineX - 4.73f) < 1e-4f && g.tun.outLow < -1.5f,
                    "the DIAMOND arena: its collision, the classic foul line, its bounds");
                var log = new List<GameEvent>();
                Vec3 from = g.pl[0].ball.pos;
                Fire(g, @in, from, Normalize(g.ChestPoint(1) - from) * 14f, 3f, log);
                c.C(HasEvent(log, EventType.HitPlayer, 1), "a throw straight at the other player hits them");
            }
            {
                var @in = new PlayerInput[2]; Game g = Rolled(@in);
                var log = new List<GameEvent>();
                Fire(g, @in, new Vec3(-3.0f, 0.6f, 0f), Normalize(new Vec3(1f, -1.2f, 0.3f)) * 11f, 2.5f, log);   // down past the platforms
                bool low = false;
                foreach (GameEvent e in log) low |= e.type == EventType.Bounce && e.pos.y < -0.6f;
                c.C(low && !HasEvent(log, EventType.SpawnRise) && !HasEvent(log, EventType.Foul), "the V below the platforms: the ball bounces back up");
            }
            {
                var @in = new PlayerInput[2]; Game g = Rolled(@in);
                var log = new List<GameEvent>();
                Fire(g, @in, g.pl[0].ball.pos, Normalize(new Vec3(0.3f, -1f, 0.4f)) * 6f, 3f, log);
                bool offPlatform = false;
                foreach (GameEvent e in log)
                    offPlatform |= e.type == EventType.Bounce && System.MathF.Abs(e.pos.y) < 0.25f && Length(new Vec3(e.pos.x + 7f, 0, e.pos.z)) < ExpArena.kPlatformR;
                c.C(!offPlatform, "the ball goes through the platforms (it can come up at you from below)");
                var ins = new PlayerInput[2]; Game gs = Rolled(ins, true);
                var logs = new List<GameEvent>();
                Fire(gs, ins, gs.pl[0].ball.pos, Normalize(new Vec3(0.3f, -1f, 0.4f)) * 6f, 3f, logs);
                bool bounced = false;
                foreach (GameEvent e in logs) bounced |= e.type == EventType.Bounce && System.MathF.Abs(e.pos.y) < 0.25f;
                c.C(bounced && !HasEvent(logs, EventType.Foul), "solid platforms (an option): the ball bounces off them, no foul");
                var in2 = new PlayerInput[2]; Game g2 = Rolled(in2);
                var log2 = new List<GameEvent>();
                Fire(g2, in2, g2.pl[0].ball.pos, Normalize(new Vec3(0.2f, 0.9f, 1f)) * 8f, 3f, log2);   // up into a slanted wall of your end
                c.C(!HasEvent(log2, EventType.Foul), "no foul zone in DIAMOND: a throw into your own end's walls just bounces");
            }
            {
                var @in = new PlayerInput[2]; Game g = Rolled(@in);
                var log = new List<GameEvent>();
                Fire(g, @in, new Vec3(-4.0f, 2.2f, 0f), Normalize(new Vec3(1f, 0.02f, 0.03f)) * 15f, 3f, log);   // over their head into their end
                bool back = false;
                foreach (GameEvent e in log) back |= e.type == EventType.Returned && e.player == 0 && e.pos.x > 9f;
                c.C(back || HasEvent(log, EventType.HitPlayer, 1), "the far end is their back wall (the ball returns)");
            }
            {
                var @in = new PlayerInput[2]; Game g = Rolled(@in);
                RandomThrows(g, @in, 4242, 600, out int ended, out int escaped);
                c.C(ended == 600 && escaped == 0, "every throw ends, and none leaves the room");
            }
            BotsPlay(Arena.Diamond, c, "DIAMOND");
            c.Done();
        }

        // the collision itself (no native counterpart: the port's own check of the shapes)
        [Test]
        public void ExperimentalShapes()
        {
            var c = new Checks("EXPERIMENTAL arenas: their shapes", 5);
            var v = new Vec3[4];
            ExpArena.DiamondEnd(v);
            bool square = true;
            for (int k = 0; k < 4; ++k) square &= System.MathF.Abs(Length(v[k] - v[(k + 1) % 4]) - ExpArena.kRollW) < 1e-4f;
            c.C(square && System.MathF.Abs(v[2].y - (ExpArena.kRollCY - ExpArena.kRollReach)) < 1e-4f && System.MathF.Abs(v[2].z) < 1e-4f,
                "DIAMOND's end: a square as wide as the court, a corner straight under the platforms");
            List<CollisionTri> d = ExpArena.DiamondCollision(), ds = ExpArena.DiamondCollision(true);
            c.C(d.Count == 12 * 4 * 2 + 2 * 2 && ds.Count == d.Count + 2 * 24 * 4, "DIAMOND: 12 strips of four walls, two ends; solid platforms add 24 segments each");
            bool facesIn = true;
            foreach (CollisionTri t in d) facesIn &= Dot(t.n, new Vec3(0, ExpArena.kRollCY, 0) - (t.a + t.b + t.c) / 3f) > 0;
            c.C(facesIn, "DIAMOND: every wall faces into the court");
            int foul = 0, back = 0;
            foreach (CollisionTri t in ExpArena.Collision()) { foul += (t.tags & Tag.Foul) != 0 ? 1 : 0; back += (t.tags & Tag.Return) != 0 ? 1 : 0; }
            c.C(foul == 2 * 24, "CORNERS: each platform's top is its player's foul zone");
            c.C(back > 0 && back % 2 == 0, "CORNERS: the corners behind the players are back walls");
            c.Done();
        }
    }
}
