// BLOCKS ONLINE (TASKS.md E61), ported from the native tests/test_game.cpp "[BLOCKS ONLINE: a block that was in time on
// the player's screen counts]" (native e0292de): player 0 as a player who isn't hosting; the ball waits where it reached
// them for their round trip, and their block, as they made it then, counts when it arrives during the wait.
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        [Test]
        public void BlocksOnline()
        {
            var c = new Checks("BLOCKS ONLINE: a block that was in time on the player's screen counts", 4);
            for (int mode = 0; mode < 4; ++mode)
            {   // 0 the block arrives, 1 none, 2 the rule off, 3 a 400 ms round trip (past the cap)
                var @in = new PlayerInput[2];
                Game g = MakeGame(@in);
                GrabBall(g, @in, 1);
                g.tun.knuckleMode = 0;   // (the shield alone)
                g.lateBlock = mode != 2;
                Pose sl = g.ShieldLocal(1);
                Quat gq = FromTo(new Vec3(0, 1, 0), new Vec3(1, 0, 0)) * sl.q.Conj();   // the plate facing the ball
                Vec3 chest = g.pl[0].@in.head.p - new Vec3(0, 0.45f, 0);
                @in[0].hand[1].grip = new Pose(gq, chest + new Vec3(0.45f, 0, 0.9f) - gq.Rotate(sl.p));   // out to the side
                Run(g, @in, 0.05f);
                float late = mode == 3 ? 0.4f : 0.2f;
                g.seenLate[0] = late;
                FireOpponentBall(g, chest);
                var log = new List<GameEvent>();
                for (float t = 0; t < 2f && g.pl[1].ball.blockWaitAt < 0f && !HasEvent(log, EventType.HitPlayer, 0); t += 1f / 72)
                {
                    Step(g, @in); log.AddRange(g.events);
                    g.seenLate[0] = late;   // (the host sets it each frame)
                }
                bool waited = g.pl[1].ball.blockWaitAt >= 0f;
                Vec3 dir = Normalize(g.pl[1].ball.blockWaitVel), at = g.pl[1].ball.pos;
                float before = 0.45f / Length(g.pl[1].ball.blockWaitVel);
                for (float u = 0; u < 0.6f; u += 1f / 72)
                {   // their shield onto the ball's path 0.45 m before it reached them, as they did then: it arrives here
                    // their round trip after the ball was there
                    if ((mode == 0 || mode == 3) && u >= late - before) @in[0].hand[1].grip = new Pose(gq, at - dir * 0.45f - gq.Rotate(sl.p));
                    Step(g, @in); log.AddRange(g.events);
                    g.seenLate[0] = late;
                }
                bool blocked = HasEvent(log, EventType.ShieldBlock, 0), hit = HasEvent(log, EventType.HitPlayer, 0);
                if (mode == 0) c.C(waited && blocked && !hit && g.pl[1].ball.vel.x > 0f, "a block arriving within the round trip counts; the ball goes back");
                if (mode == 1) c.C(waited && !blocked && hit, "no block: the hit counts after the wait");
                if (mode == 2) c.C(!waited && hit, "the rule off: the hit at once, as before");
                if (mode == 3) c.C(waited && !blocked && hit, "a round trip past LateBlockMax (0.25 s): a block that late does not count");
            }
            c.Done();
        }
    }
}
