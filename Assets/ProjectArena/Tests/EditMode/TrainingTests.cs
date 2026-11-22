// TRAINING (task A6), ported from the native tests/test_game.cpp trainingTests (native aaf0cab): every lesson can be
// passed. The throwing lessons by a perfect thrower (the coach's planner, from where a person stands), STRIKE ZONE and
// CATCH by a scripted player, a person's throws by the hands, the defense lessons by the coach-bot in your place; and
// the bests (what is passed, what opens next, saved and read back).
using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;
using ProjectArena.Sim.Training;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        const float TrDt = 1f / 72;

        // play lesson i: `act` drives the player each step (it may serve the ball); until done or `limit` seconds
        static Trainer PlayLesson(int i, float limit, Action<Game, Trainer, PlayerInput[]> act, bool botPlayer = false)
        {
            Game g = NewGame(); g.tun.botLevel = 2;
            var tr = new Trainer();
            tr.Begin(g, i, Tris);
            var @in = new PlayerInput[Teams.MaxPlayers];
            for (int k = 0; k < Teams.MaxPlayers; ++k) @in[k] = new PlayerInput();
            for (int k = 0; k < 72; ++k) { @in[0] = Standing(g, 0); tr.Step(g, TrDt, @in); }   // the card a moment
            tr.Start(g);
            if (botPlayer)
            {   // the coach's own skills in your place: it holds its ball and meets the throws
                g.pl[0].isBot = true; g.pl[0].botLevel = Bot.Levels - 1;
                g.pl[0].coach.CopyFrom(new PlayerState.Coach()); g.pl[0].coach.on = true;
                g.pl[0].coach.stance = Bot.STANCE_KITTEN;
            }
            var cueN = new int[32];
            for (float t = 0; t < limit && tr.phase != Phase.Done; t += TrDt)
            {
                @in[0] = Standing(g, 0);
                if (tr.phase == Phase.Play) act(g, tr, @in);
                tr.Step(g, TrDt, @in);
                foreach (CueEvent c in tr.cues) ++cueN[(int)c.cue];
            }
            if (botPlayer)
                TestContext.Out.WriteLine($"       (blocked {cueN[(int)Cue.Blocked]}, punched {cueN[(int)Cue.Punched]}, dodged {cueN[(int)Cue.Dodged]}, " +
                                          $"hit {cueN[(int)Cue.Hit]}, let through {cueN[(int)Cue.Dropped]}, body {cueN[(int)Cue.BodyHit]})");
            return tr;
        }

        // CUSTOM TRAINING (TASKS.md E33, native 3c285d9 test_game.cpp): a lesson with MY OPTIONS plays your room options;
        // STANDARD the rules' own; out of training, your rules as they were
        [Test]
        public void CustomTraining()
        {
            void LessonRules(bool mine, out Tunables during, out Tunables after)
            {
                var g = NewGame();
                g.tun.aimAssist = 3; g.tun.shieldSize = 2; g.tun.maiden = true; g.tun.strikeZone = 2; g.tun.strikeSpeed = 1;
                g.tun.ballGrowth = 1; g.tun.wallsLively = true; g.tun.wallGrip = true; g.tun.bankAssist = 2;
                g.tun.foulShift = 2; g.tun.basicMatchTime = 300f;   // (not the lessons': the foul line and the clock stay theirs)
                var tr = new Trainer { custom = mine };
                tr.Begin(g, Lessons.ById(21), Tris);   // (BLOCKING 1: SHIELD)
                during = g.tun.Clone();
                tr.Stop(g);
                after = g.tun.Clone();
            }
            LessonRules(false, out Tunables d0, out Tunables a0);
            LessonRules(true, out Tunables d1, out Tunables a1);
            bool standard = d0.AimAssistNow() == 2 && d0.ShieldSizeNow() == 0 && !d0.MaidenNow() && d0.StrikeZoneNow() == 1 &&
                            d0.StrikeSpeedNow() == 0 && d0.BallGrowthNow() == 3 && d0.BouncesStandard();
            bool mineOk = d1.AimAssistNow() == 3 && d1.ShieldSizeNow() == 2 && d1.MaidenNow() && d1.StrikeZoneNow() == 2 &&
                          d1.StrikeSpeedNow() == 1 && d1.BallGrowthNow() == 1 && d1.WallsLivelyNow() && d1.WallGripNow() && d1.BankAssistNow() == 2;
            bool lessonsOwn = d1.FoulShiftNow() == 0 && d1.basicMatchTime == 0f && d1.shields && d1.knuckleMode == 0;
            bool back = a0.aimAssist == 3 && a1.aimAssist == 3 && a1.wallGrip && a1.foulShift == 2 && a1.basicMatchTime == 300f;
            Assert.That(standard && mineOk && lessonsOwn && back,
                        "CUSTOM TRAINING: STANDARD plays the rules' options; MY OPTIONS your own (aim assist, shield size, MAIDEN, strike zone and " +
                        "speed, ball growth, BOUNCES); the lesson's shields, foul line and clock stay its own; afterwards your rules as they were");
        }

        [Test]
        public void TrainingLessons()
        {
            var lessons = Lessons.All;
            int throwing = 0, byHand = 0, defend = 0;
            foreach (Lesson L in lessons) { throwing += L.kind == Kind.THROW ? 1 : 0; byHand += L.kind != Kind.DEFEND ? 1 : 0; defend += L.kind == Kind.DEFEND ? 1 : 0; }
            var c = new Checks("TRAINING: lessons", throwing + 1 + byHand + 2 + defend + 6);
            var hand = new Vec3(-6.6f, 1.35f, 0.1f);   // where a person's throw leaves
            int planned = 0, plannedOk = 0;
            for (int i = 0; i < lessons.Count; ++i)
            {
                Lesson L = lessons[i];
                if (L.kind != Kind.THROW) continue;
                {   // every target, by itself, can be had from where you stand (aim assist on, as you throw)
                    Game g = NewGame();
                    var tr = new Trainer(); tr.Begin(g, i, Tris);
                    List<Target> T = Target.Copy(tr.targets);
                    for (int k = 0; k < T.Count; ++k)
                    {
                        for (int j = 0; j < T.Count; ++j) T[j].live = j == k;
                        Shot s = Lessons.PlanShot(g, 0, hand, L, T, k, 1f + 0.7f * k, true);
                        ++planned; plannedOk += s.ok ? 1 : 0;
                        if (!s.ok) TestContext.Out.WriteLine($"       {L.name}: target {k} not reached");
                    }
                }
                // a perfect thrower: the planned throw at the first target up, each time the ball is ready
                Trainer r = PlayLesson(i, 120f, (gg, t, _) =>
                {
                    if (gg.pl[0].ball.state != BallState.Idle) return;
                    int k = -1;
                    for (int j = 0; j < t.targets.Count && k < 0; ++j) if (t.targets[j].live && !t.targets[j].down) k = j;
                    if (k < 0) return;
                    Shot s = Lessons.PlanShot(gg, 0, hand, L, t.targets, k, t.clock, true);
                    gg.Serve(0, hand, s.dir, s.speed, s.spin, true);
                });
                TestContext.Out.WriteLine($"       {L.name,-16} {r.successes} / {r.targets.Count} in {r.clock:F1} s ({r.attempts} throws)");
                c.C(r.phase == Phase.Done && r.result.passed && r.result.attempts == L.targets.Count, L.name + ": a perfect thrower clears it, a throw a target");
            }
            c.C(planned > 0 && plannedOk == planned, "every throwing lesson's targets can be reached (planned throws)");
            for (int i = 0; i < lessons.Count; ++i)
            {   // a person's throws, by the hands (grab, swing back, sweep, let go) count
                Lesson L = lessons[i];
                if (L.kind == Kind.DEFEND) continue;
                var th = new Thrower();
                Trainer r = PlayLesson(i, 150f, (g, t, @in) => { @in[0] = th.Step(g, 0, L, t.targets, t.clock, TrDt); });
                TestContext.Out.WriteLine($"       by hand: {L.name,-16} {r.successes} / {L.Goal()} in {r.attempts} tries, {r.clock:F1} s");
                c.C(r.phase == Phase.Done && r.result.passed, L.name + ": thrown by hand (the trigger let go mid-swing), it is passed");
            }
            {   // STRIKE ZONE: past the coach's body, through its ring
                int i = Lessons.ById(7);
                int n = 0;
                Trainer r = PlayLesson(i, 120f, (g, t, _) =>
                {
                    if (g.pl[0].ball.state != BallState.Idle) return;
                    Vec3 at = g.StrikeZoneCenter(1) + new Vec3(0, 0.02f, (n++ & 1) != 0 ? 0.45f : -0.45f);
                    g.Serve(0, hand, at - hand, 11.5f, default, true);
                });
                TestContext.Out.WriteLine($"       {lessons[i].name,-16} {r.successes} strikes in {r.attempts} throws, {r.clock:F1} s");
                c.C(r.result.passed && r.result.attempts <= 6, "STRIKE ZONE: throws past the coach earn strikes; passed");
            }
            {   // CATCH: at the far wall, then a hand (trigger held) where the ball comes back
                int i = Lessons.ById(8);
                Trainer r = PlayLesson(i, 120f, (g, t, @in) =>
                {
                    Ball b = g.pl[0].ball;
                    if (b.state == BallState.Idle) g.Serve(0, hand, new Vec3(9.5f, 1.7f, 0.3f) - hand, 10f, default, true);
                    if (b.state == BallState.Returning && Length(b.pos - @in[0].head.p) < 1.2f)
                    {
                        @in[0].hand[1].trigger = 1;
                        @in[0].hand[1].grip.p = b.pos - @in[0].hand[1].grip.q.Rotate(g.tun.catcherOffset);
                    }
                });
                TestContext.Out.WriteLine($"       {lessons[i].name,-16} {r.successes} caught of {r.attempts}, {r.clock:F1} s");
                c.C(r.result.passed, "CATCH: each ball back from the far wall is caught; passed");
            }
            for (int i = 0; i < lessons.Count; ++i)
            {   // the defense lessons: the far end's bot skills in your place
                Lesson L = lessons[i];
                if (L.kind != Kind.DEFEND) continue;
                Trainer r = PlayLesson(i, 150f, (g, t, @in) => { }, true);
                TestContext.Out.WriteLine($"       {L.name,-16} {r.successes} / {r.attempts} (pass {L.Goal()}), {r.clock:F1} s");
                if (L.guard == Guard.G_SENDBACK) c.C(r.phase == Phase.Done && r.successes > 0, "SEND IT BACK: blocks take targets");
                else if (L.guard == Guard.G_SHIELD || L.guard == Guard.G_EITHER)   // (the bot keeps its ball in its right hand: the throws to its left it lets by)
                    c.C(r.phase == Phase.Done && r.attempts == L.budget && r.successes >= L.budget / 3, L.name + ": the throws all come; a shield held where they come blocks them");
                else c.C(r.phase == Phase.Done && r.result.passed && r.attempts == L.budget, L.name + ": the coach throws them all; met well, it is passed");
            }
            {   // bests: a better run replaces them; the next lesson opens once one is passed; saved and read back
                var b = new Bests();
                int b0 = Lessons.ById(21), d0 = Lessons.ById(41);
                c.C(b.Unlocked(0) && !b.Unlocked(1) && b.Unlocked(b0) && !b.Unlocked(b0 + 1) && b.Unlocked(d0), "only each track's first lesson is open at first");
                var r = new Result { lesson = 0, passed = false, successes = 3, attempts = 15, time = 30 };
                c.C(!b.Add(r) && !b.Unlocked(1), "a run not passed counts for nothing");
                r.passed = true; r.successes = 5; r.attempts = 9; r.time = 40;
                c.C(b.Add(r) && b.Unlocked(1) && !b.Unlocked(2), "passed: the next lesson opens");
                Result faster = r; faster.time = 25; faster.attempts = 12;
                c.C(b.Add(faster) && b.b[1].time == 25 && b.b[1].attempts == 9, "a faster run keeps the time; the accuracy stays the better one's");
                var cc = new Bests(); cc.Load(b.Save());
                c.C(cc.b[1].passed && cc.b[1].time == 25 && cc.b[1].successes == 5 && cc.Total() == 555, "bests saved and read back");
                cc.Load("1 1 -5 9 3\n300 1 1 1 1\n");
                c.C(cc.b[1].time == 25, "nonsense lines are ignored");
            }
            c.Done();
        }
    }
}
