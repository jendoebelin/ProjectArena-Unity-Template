// The match statistics (Sim/Stats.cs; task A7), ported from the native tests/test_game.cpp "[match statistics]"
// (native aaf0cab): throw accuracy is literal, points from returns count apart (GitHub #14), and ten minutes of bot
// matches stay acceptable to the community server.
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        static GameEvent Ev(EventType t, int p, float v, int ball) => new GameEvent(t, p, -1, default, v, ball);

        // what the community server accepts (server/projectarena_relay.py, 0.3.9)
        static bool StatsValid(MatchTally s) =>
            s.onTarget <= s.throws && s.returnHits <= s.hits && s.returnHits <= s.blocks && s.hits - s.returnHits <= s.onTarget && s.blocks <= s.faced;

        [Test]
        public void MatchStatistics()
        {
            var c = new Checks("match statistics: throw accuracy is literal, returns count apart (GitHub #14)", 14);   // (10, and E47's four)
            {   // the issue's case: 0 throws, 1 blocks it back, the return hits 0
                var k = new StatCounter();
                k.Add(Ev(EventType.Throw, 0, 9f, -1));
                k.Add(Ev(EventType.ShieldBlock, 1, 0, 0));
                k.Add(Ev(EventType.HitPlayer, 0, 1f, 0));
                MatchTally a = k.s[0], b = k.s[1];
                c.C(a.throws == 1 && a.onTarget == 1 && a.hits == 0 && a.faced == 1, "the thrower: one throw, on target (it was blocked)");
                c.C(b.throws == 0 && b.onTarget == 0 && b.hits == 1 && b.returnHits == 1 && b.blocks == 1, "the defender: a point from a return, no throw on target");
                c.C(StatsValid(a) && StatsValid(b), "the server accepts both players' numbers");
            }
            {   // one throw that strikes and then hits: on target once
                var k = new StatCounter();
                k.Add(Ev(EventType.Throw, 0, 9f, -1));
                k.Add(Ev(EventType.Strike, 1, 1f, 0));
                k.Add(Ev(EventType.HitPlayer, 1, 0f, 0));
                c.C(k.s[0].onTarget == 1 && k.s[0].hits == 1 && k.s[0].strikes == 1 && k.s[0].returnHits == 0 && k.s[1].faced == 1,
                    "a throw that strikes and then hits is on target once");
            }
            {   // your own ball hits you: the other player's point, but nobody's shot
                var k = new StatCounter();
                k.Add(Ev(EventType.Throw, 0, 9f, -1));
                k.Add(Ev(EventType.HitPlayer, 0, 1f, 0));
                c.C(k.s[1].hits == 0 && k.s[1].onTarget == 0 && k.s[0].onTarget == 0, "an own goal is nobody's hit or throw on target");
            }
            {   // the shot clock's automatic strike belongs to no ball
                var k = new StatCounter();
                k.Add(Ev(EventType.Throw, 0, 9f, -1));
                k.Add(Ev(EventType.Strike, 1, 1f, -1));
                c.C(k.s[0].strikes == 1 && k.s[0].onTarget == 0, "an automatic strike counts as a strike, not a throw on target");
            }
            {   // a returned ball blocked back again: the first thrower's throw on target once, then it's a return again
                var k = new StatCounter();
                k.Add(Ev(EventType.Throw, 0, 9f, -1));
                k.Add(Ev(EventType.KnuckleBlock, 1, 9f, 0));
                k.Add(Ev(EventType.ShieldBlock, 0, 0, 0));
                k.Add(Ev(EventType.HitPlayer, 1, 0f, 0));
                c.C(k.s[0].onTarget == 1 && k.s[0].hits == 1 && k.s[0].returnHits == 1 && k.s[1].onTarget == 0 && StatsValid(k.s[0]) && StatsValid(k.s[1]),
                    "a rally of blocks: each throw on target once; the last return scores");
            }
            {   // four players (FOUR CORNERS, TASKS.md E47, native 530f6fb, for The Crowd): the one attacking is whoever threw or returned that ball
                var k = new StatCounter { np = 4 };
                k.Add(Ev(EventType.Throw, 2, 11f, -1));     // 2 throws its ball
                k.Add(Ev(EventType.ShieldBlock, 3, 0, 2));   // 3 blocks it: 2's throw on target, now 3's return
                k.Add(Ev(EventType.HitPlayer, 0, 3f, 2));   // the return hits 0: 3 scores
                k.Add(Ev(EventType.Throw, 1, 8f, -1));      // 1 throws, and strikes 2
                k.Add(Ev(EventType.Strike, 2, 1f, 1));
                k.Add(Ev(EventType.Caught, 1, 0, -1));
                c.C(k.s[2].throws == 1 && k.s[2].onTarget == 1 && k.s[2].hits == 0 && k.s[2].topSpeed == 11f,
                    "four players: a blocked throw is on target once, for its thrower");
                c.C(k.s[3].blocks == 1 && k.s[3].faced == 1 && k.s[3].hits == 1 && k.s[3].returnHits == 1 && k.s[0].faced == 1,
                    "... the blocker's return scores, as theirs, against the one it hit");
                c.C(k.s[1].strikes == 1 && k.s[1].onTarget == 1 && k.s[2].faced == 1 && k.s[1].catches == 1 && k.s[3].strikes == 0 &&
                    k.s[0].strikes == 0, "... a strike goes to whoever threw that ball (not to player 1 - p)");
                bool ok = true;
                for (int p = 0; p < 4; ++p) ok = ok && StatsValid(k.s[p]);
                c.C(ok, "... and the server's checks hold for all four");
            }
            {   // real matches: two lively bots for ten minutes; every event behind a point names its ball
                var g = NewGame(); g.Reset(true);
                g.pl[0].isBot = true;
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                var log = new List<GameEvent>();
                Run(g, @in, 600f, log);
                var k = new StatCounter();
                int untagged = 0;
                int[] points = { 0, 0 };
                bool ok = true;
                foreach (GameEvent e in log)
                {
                    if (e.type == EventType.RoundStart && (int)e.value <= 1) k = new StatCounter();
                    if ((e.type == EventType.HitPlayer || e.type == EventType.ShieldBlock || e.type == EventType.KnuckleBlock) && e.ball < 0) ++untagged;
                    if (e.type == EventType.HitPlayer) ++points[(int)e.value & 1];
                    k.Add(e);
                    ok = ok && StatsValid(k.s[0]) && StatsValid(k.s[1]);
                }
                int blocks = 0;
                foreach (GameEvent e in log) blocks += e.type == EventType.ShieldBlock || e.type == EventType.KnuckleBlock ? 1 : 0;
                TestContext.Out.WriteLine($"       (bots: {points[0]} + {points[1]} points, {blocks} blocks, {log.Count} events)");
                c.C(points[0] + points[1] > 4 && blocks > 2, "(test setup: the bots score and block)");
                c.C(untagged == 0, "every hit and block names the ball behind it");
                c.C(ok, "the counts stay acceptable to the server all through ten minutes of bot matches");
            }
            c.Done();
        }
    }
}
