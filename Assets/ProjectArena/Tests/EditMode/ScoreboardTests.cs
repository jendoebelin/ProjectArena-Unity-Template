// The scoreboard's lines (Runtime/Scoreboard.cs), as the native clockLine, rulesLine and banners read.
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public class ScoreboardTests
    {
        [Test]
        public void ClockLine()
        {
            var g = new Game();
            g.Reset(true);
            Assert.That(Scoreboard.ClockLine(g), Is.EqualTo("3:00"), "Basic: 3 minutes");
            g.roundClock = 61.2f;
            Assert.That(Scoreboard.ClockLine(g), Is.EqualTo("1:02"), "seconds round up, two digits");
            g.overtime = true;
            Assert.That(Scoreboard.ClockLine(g), Is.EqualTo("SUDDEN DEATH"));
            g.overtime = false; g.tun.basicMatchTime = 0;
            Assert.That(Scoreboard.ClockLine(g), Is.EqualTo("FREE PLAY"));
            g.tun.advanced = true;
            Assert.That(Scoreboard.ClockLine(g), Is.EqualTo(""), "an untimed Advanced round shows no clock");
        }

        [Test]
        public void RulesLine()
        {
            var t = new Tunables();
            // (ROOMS AND THEIR OPTIONS, TASKS.md E22: the room and its match, no BASIC and ADVANCED on screen)
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("SQUARE - TIMED MATCH"));
            t.shields = false; t.basicMatchTime = 300;
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("SQUARE - TIMED MATCH - HOTDOG - 5 MIN"));
            t = new Tunables { advanced = true, advancedRoundTime = 120, knuckleMode = 1 };
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("SQUARE - BEST OF 3 - 2 MIN ROUNDS - KNUCKLE GUARDS"));
            t = new Tunables { knuckleMode = 0 };
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("SQUARE - TIMED MATCH - NO KNUCKLE GUARDS"));
            t = new Tunables { strikeSpeed = 1 };   // (STRIKE SPEED, native 0.3.19)
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("SQUARE - TIMED MATCH - FAST STRIKES"));
            t.strikeSpeed = 2;
            Assert.That(Scoreboard.RulesLine(t), Is.EqualTo("SQUARE - TIMED MATCH - FASTER STRIKES"));
            t.experimental = true; t.expRoom = Room.Blitz;
            Assert.That(Scoreboard.RulesLine(t), Does.Not.Contain("STRIKES"), "four-player rooms play at regulation");
        }

        [Test]
        public void Banners()
        {
            var g = new Game();
            g.Reset(true);
            Assert.That(Scoreboard.Banner(g, 0, false), Is.EqualTo("WAITING FOR PLAYERS"));
            g.match = MatchState.Countdown; g.round = 1; g.stateTimer = 2.4f;
            Assert.That(Scoreboard.Banner(g, 0, false), Is.EqualTo("ROUND 1   3"));
            g.match = MatchState.InProgress;
            Assert.That(Scoreboard.Banner(g, 0, false), Is.Null, "no banner while playing");
            g.match = MatchState.MatchSummary; g.matchWinner = 0;
            Assert.That(Scoreboard.Banner(g, 0, false), Is.EqualTo("YOU WIN  -  PRESS A"));
            g.matchWinner = 1;
            Assert.That(Scoreboard.Banner(g, 0, false), Is.EqualTo("YOU LOSE  -  PRESS A"));
            Assert.That(Scoreboard.Banner(g, 0, true), Is.EqualTo("ORANGE WINS"), "spectators: the winning side");
        }
    }
}
