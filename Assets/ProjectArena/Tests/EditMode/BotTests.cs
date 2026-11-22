// The practice bot's checks, ported from the native tests/test_game.cpp (botTests, matchLevelTests, stanceTests, the
// slap shot and the attack plan; native e5cc0ae). The bot itself matches the native one frame for frame
// (SameResultsTests: every kind of level); these check what it does: a person's limits, its levels, stances, the fist
// bump, slap shots and its attack plan.
// Shortened from native (the editor runs them on every test pass): UNBEATABLE against a strong player plays one seed
// at 72 Hz in Basic (native: three seeds at 72 and 90 Hz in four rule sets), and MATCH MY LEVEL's strong player is left
// to the native tests.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class BotTests
    {
        const string ColPath = "Assets/ProjectArena/Art/Open/Data/arena.col.bytes";
        static List<CollisionTri> s_tris;
        static List<CollisionTri> Tris => s_tris ??= ArenaCollision.Load(File.ReadAllBytes(ColPath));

        static readonly Quat kFaceP0 = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(-90));
        static readonly Quat kFaceP1 = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(90));

        class Checks
        {
            readonly List<string> failed = new List<string>();
            public void C(bool ok, string what) { TestContext.Out.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failed.Add(what); }
            public void Done() => Assert.That(failed, Is.Empty);
        }

        static Game NewGame() { var g = new Game(); g.SetArena(Tris); return g; }

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

        static void Step(Game g, PlayerInput[] @in, float dt = 1f / 72) => g.Step(dt, new[] { @in[0].Clone(), @in[1].Clone() });

        static void Run(Game g, PlayerInput[] @in, float seconds, List<GameEvent> log = null)
        {
            for (float t = 0; t < seconds; t += 1f / 72)
            {
                Step(g, @in);
                if (log != null) log.AddRange(g.events);
            }
        }

        static bool HasEvent(List<GameEvent> ev, EventType t)
        {
            foreach (var e in ev) if (e.type == t) return true;
            return false;
        }

        // the drawn arms keep their bones' lengths (never longer than the arm)
        static bool ArmsHold(PlayerInput @in, AvatarDesc look)
        {
            Avatar.Rig r = Avatar.SolveRig(@in, look);
            Avatar.BodyBind K = Avatar.BodyBindOf(look.body);
            for (int k = 0; k < 2; ++k)
            {
                float l1 = Length(K.elbow[k] - K.shoulder[k]) * Avatar.Tuning.upperLen;
                float l2 = Length(K.wrist[k] - K.elbow[k]) * Avatar.Tuning.foreLen;
                if (Length(r.upper[k].b - r.upper[k].a) > l1 * 1.002f || Length(r.fore[k].b - r.fore[k].a) > l2 * 1.002f) return false;
            }
            return true;
        }

        [Test, Timeout(600000)]   // (minutes of matches: past NUnit's 3-minute default)
        public void PersonsLimitsAtEveryLevel()
        {
            var c = new Checks();
            bool reach = true, rig = true, hands = true, body = true, finite = true;
            int[] used = { 0, 0 };
            int catches = 0, dodges = 0;
            float worstHand = 0, worstBody = 0;
            AvatarDesc look = Avatar.PresetLook(3);
            for (int level = 0; level < Bot.Levels; ++level)
                for (int adv = 0; adv < 2; ++adv)
                {
                    var g = NewGame(); g.tun.advanced = adv != 0; g.tun.botLevel = level; g.Reset(true);
                    g.pl[0].isBot = true;
                    BotSkill sk = Bot.Skill(level);
                    var @in = new[] { new PlayerInput(), new PlayerInput() };
                    var lastGrip = new Vec3[2, 2];
                    var lastBody = new Vec3[2];
                    for (int i = 0; i < 72 * 60; ++i)
                    {
                        Step(g, @in);
                        foreach (GameEvent e in g.events)
                        {
                            if ((e.type == EventType.Grab || e.type == EventType.Throw || e.type == EventType.ShieldBlock ||
                                 e.type == EventType.KnuckleBlock || e.type == EventType.Caught) && e.hand >= 0 && e.hand < 2)
                                ++used[e.hand];
                            catches += e.type == EventType.Caught ? 1 : 0;
                        }
                        for (int p = 0; p < 2; ++p)
                        {
                            PlayerState s = g.pl[p];
                            for (int k = 0; k < 2; ++k) dodges += s.botRead[k].plan == Bot.DODGE ? 1 : 0;
                            for (int h = 0; h < 2; ++h)
                            {
                                Vec3 gp = s.@in.hand[h].grip.p;
                                finite = finite && float.IsFinite(gp.x) && float.IsFinite(gp.y) && float.IsFinite(gp.z);
                                if (Length(g.BotWrist(h, g.HandPose(p, h)) - g.BotShoulder(p, h)) > g.tun.botArm + 1e-3f) reach = false;   // (the rules' hand: TASKS.md E41)
                                if (i > 0)
                                {   // its own move, plus the body carrying the arm along
                                    float lim = Max(sk.handSpeed, sk.throwHand) + sk.bodySpeed;
                                    float v = Length(gp - lastGrip[p, h]) * 72;
                                    worstHand = Max(worstHand, v / lim);
                                    if (v > lim + 0.05f) hands = false;
                                }
                                lastGrip[p, h] = gp;
                            }
                            if (i > 0)
                            {
                                float v = Length(s.botBody - lastBody[p]) * 72;
                                worstBody = Max(worstBody, v / sk.bodySpeed);
                                if (v > sk.bodySpeed + 0.01f) body = false;
                            }
                            lastBody[p] = s.botBody;
                            if (i % 9 == 0 && !ArmsHold(s.@in, look)) rig = false;
                        }
                    }
                }
            TestContext.Out.WriteLine($"  (hands used: left {used[0]}, right {used[1]}; catches {catches}; dodging {dodges} frames; " +
                                      $"fastest hand {worstHand * 100:0}%, body {worstBody * 100:0}% of its limit)");
            c.C(finite, "the bot's hands always have a position");
            c.C(reach, "a hand never goes beyond its arm's reach");
            c.C(rig, "the drawn arms never stretch");
            c.C(hands, "its hands never move faster than a person's (at its level)");
            c.C(body, "its body never steps, leans or ducks faster than a person (at its level)");
            c.C(used[0] > 20 && used[1] > 20, "it grabs, throws, blocks and catches with both hands");
            c.C(catches > 5, "it catches its own ball coming back");
            c.C(dodges > 0, "it dodges");
            c.Done();
        }

        [Test, Timeout(600000)]   // (minutes of matches: past NUnit's 3-minute default)
        public void Levels()
        {
            var c = new Checks();
            int wins = 0;
            for (int adv = 0; adv < 2; ++adv)
            {
                var g = NewGame(); g.tun.advanced = adv != 0; g.Reset(true);
                g.pl[0].isBot = true;
                g.pl[0].botLevel = 0; g.pl[1].botLevel = Bot.Levels - 1;
                var @in = new[] { new PlayerInput(), new PlayerInput() };
                var log = new List<GameEvent>();
                Run(g, @in, 180f, log);
                int[] pts = { 0, 0 };
                foreach (GameEvent e in log) if (e.type == EventType.HitPlayer) ++pts[(int)e.value & 1];
                TestContext.Out.WriteLine($"  {(adv != 0 ? "Advanced" : "Basic")}: ROOKIE {pts[0]} - {pts[1]} UNBEATABLE");
                wins += pts[1] > pts[0] ? 1 : 0;
            }
            c.C(wins == 2, "UNBEATABLE beats ROOKIE");
            c.Done();
        }

        // a strong player against the bot (player 1): takes the ball as soon as it can and throws it at 14 m/s at the bot's
        // head, chest, hips and sides, curved and off the side walls, and at its strike zone, from all over their end.
        // points: the bot hit by the attacker's throws; rebounds: by a ball it had just blocked itself, bouncing back into it
        struct Attack { public int throws, points, rebounds, botPoints, strikes, blocks, knuckles; }

        static PlayerInput StandingAt(Game g, int p, float z)   // Standing() moved sideways along the end
        {
            PlayerInput @in = Standing(g, p);
            @in.head.p.z += z;
            for (int h = 0; h < 2; ++h) @in.hand[h].grip.p.z += z;
            return @in;
        }

        static Attack AttackBot(Game g, int wanted, float hz, uint seed, Recorder recorder = null)
        {
            var a = new Attack();
            uint rng = seed;
            float Rnd() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / (float)0x1000000; }
            var @in = new[] { Standing(g, 0), new PlayerInput() };
            var log = new List<GameEvent>();
            float t = 0, next = 0;
            void StepA()
            {
                Step(g, @in, 1f / hz);
                recorder?.Frame(g, 1f / hz);
                log.AddRange(g.events);
                foreach (GameEvent e in g.events)   // (whose ball last touched it: the bot's own block coming back)
                    if (e.type == EventType.HitPlayer && e.player == 1 && e.ball == 0 && g.pl[0].ball.instigator == 1) ++a.rebounds;
            }
            for (; t < 1500f && a.throws < wanted; t += 1f / hz)
            {
                @in[0].buttonA = g.match == MatchState.MatchSummary;   // (a match won: the next one)
                if (g.match == MatchState.InProgress && t > next && g.pl[0].ball.state == BallState.Idle)
                {
                    @in[0] = Standing(g, 0);
                    Vec3 off = @in[0].hand[1].grip.q.Rotate(g.tun.launcherOffset);   // (grabBall)
                    @in[0].hand[1].grip.p = g.pl[0].ball.pos - off;
                    @in[0].hand[1].trigger = 1;
                    StepA();
                    int k = a.throws++;
                    Vec3 target = g.ChestPoint(1);
                    float sgn = (k / 7) % 2 != 0 ? -1f : 1f;
                    switch (k % 7)
                    {
                        case 0: break;
                        case 1: target.y += 0.30f; break;                  // the head
                        case 2: target.y -= 0.40f; break;                  // the hips
                        case 3: target.z += 0.25f * sgn; break;            // a side
                        case 4: target.z += 3.0f * sgn; break;             // off the side wall
                        case 5: target.z -= 1.2f * sgn; @in[0].hand[1].angVel = new Vec3(0, 25f * sgn, 0); break;   // curved
                        default: target = g.StrikeZoneCenter(1) + new Vec3(0, 0.3f * sgn, 0.45f * sgn); break;     // its strike zone
                    }
                    Vec3 dir = Normalize(target - g.LauncherPoint(0, 1));
                    for (int f = 0; f < 16; ++f)
                    {   // the swing (as swingAndRelease), then let go
                        if (f < 15) @in[0].hand[1].grip.p += dir * (6.5f / 72); else @in[0].hand[1].trigger = 0;
                        StepA();
                    }
                    @in[0] = StandingAt(g, 0, (Rnd() - 0.5f) * 1.6f);   // and somewhere else in their end
                    next = t + 0.6f;
                }
                StepA();
            }
            foreach (GameEvent e in log)
            {
                if (e.type == EventType.HitPlayer) { if (e.player == 1) ++a.points; else ++a.botPoints; }
                if (e.type == EventType.Strike && e.player == 1) ++a.strikes;
                if (e.type == EventType.ShieldBlock && e.player == 1) ++a.blocks;
                if (e.type == EventType.KnuckleBlock && e.player == 1) ++a.knuckles;
            }
            a.points -= a.rebounds;
            return a;
        }

        // native's whole group: every rules mode, three seeds, both of the headset's frame rates (4,800 throws: about 90
        // minutes in the editor, so run on demand; UnbeatableAgainstAStrongPlayer below is its first part, run every time)
        [Test, Explicit("about 90 minutes"), Category("Long"), Timeout(int.MaxValue)]
        public void UnbeatableAgainstAStrongPlayerEveryMode()
        {
            var c = new Checks();
            (string name, bool adv, bool shields)[] modes = { ("Basic", false, true), ("Advanced", true, true), ("Basic HOTDOG", false, false), ("Advanced HOTDOG", true, false) };
            foreach (var m in modes)
            {
                var g = NewGame(); g.tun.advanced = m.adv; g.tun.shields = m.shields; g.tun.knuckleMode = -1;
                g.tun.basicMatchTime = 0; g.tun.meetTime = 0; g.tun.botLevel = Bot.Levels - 1; g.Reset(true);
                // 200 throws each from three spots and at the headset's two frame rates
                int throws = 0, points = 0, rebounds = 0, scored = 0, strikes = 0, blocks = 0, punches = 0;
                foreach (uint seed in new[] { 99u, 1234u, 777u })
                    foreach (float hz in new[] { 72f, 90f })
                    {
                        Attack a = AttackBot(g.Clone(), 200, hz, seed);
                        throws += a.throws; points += a.points; rebounds += a.rebounds; scored += a.botPoints; strikes += a.strikes;
                        blocks += a.blocks; punches += a.knuckles;
                    }
                TestContext.Out.WriteLine($"       {m.name}: {throws} throws, {points} hits (and {rebounds} of its own blocks back into it), it scored {scored}; " +
                                          $"{strikes} strikes, {blocks} shield blocks, {punches} punches");
                c.C(throws == 1200 && points <= 4, $"UNBEATABLE, {m.name}: 1200 hard throws, hit 4 times at most");
                c.C(rebounds == 0, $"UNBEATABLE, {m.name}: none of its own blocks comes back into it");
            }
            c.Done();
        }

        // RECORD MY MATCHES (Sim/Record.cs): a recording of a match against the bot opens, and is whole
        [Test]
        public void RecordMyMatches()
        {
            var c = new Checks();
            var g = NewGame(); g.tun.botLevel = 3; g.tun.basicMatchTime = 60; g.tun.meetTime = 0; g.Reset(true);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "projectarena_unity_sample_match.par");
            using (var r = new Recorder())
            {
                c.C(r.Begin(path, g, new RecMeta { local = 0, version = "test" }), "a recording opens");
                AttackBot(g, 60, 72f, 7u, r);
                r.End(g);
            }
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            TestContext.Out.WriteLine($"       {path}: {bytes.Length / 1e6:F1} MB");
            c.C(bytes.Length > 1000 && System.Text.Encoding.ASCII.GetString(bytes, 0, 6) == "PAREC1" && bytes[bytes.Length - 5] == 'X',
                "it starts with its name and ends with its end mark (tools/analyze_play.py reads it)");
            var rp = new Replay();
            c.C(rp.Load(bytes) && rp.frames.Count > 72 * 30 && rp.frames[0].kept[0] && rp.frames[0].kept[1],
                "read back: every step, your motion and the bot's (a person opponent's is never kept)");
            System.IO.File.Delete(path);
            c.Done();
        }

        [Test, Timeout(600000)]   // (minutes of matches: past NUnit's 3-minute default)
        public void UnbeatableAgainstAStrongPlayer()
        {
            var c = new Checks();
            var g = NewGame(); g.tun.basicMatchTime = 0; g.tun.meetTime = 0; g.tun.botLevel = Bot.Levels - 1; g.Reset(true);
            Attack a = AttackBot(g, 200, 72f, 99u);
            TestContext.Out.WriteLine($"  Basic: {a.throws} throws, {a.points} hits (and {a.rebounds} of its own blocks back into it), it scored " +
                                      $"{a.botPoints}; {a.strikes} strikes, {a.blocks} shield blocks, {a.knuckles} punches");
            c.C(a.throws == 200 && a.points <= 4, "UNBEATABLE, Basic: 200 hard throws, hit 4 times at most");
            c.C(a.rebounds == 0, "UNBEATABLE, Basic: none of its own blocks comes back into it");
            c.Done();
        }

        [Test]
        public void MatchMyLevel()
        {
            var c = new Checks();
            bool same = true, smooth = true;
            for (int l = 0; l < Bot.Levels; ++l)
            {
                BotSkill a = Bot.SkillAt(l), b = Bot.Skill(l);
                same &= a.reaction == b.reaction && a.handSpeed == b.handSpeed && a.readsFlight == b.readsFlight && a.name == b.name;
            }
            for (float l = 0; l < Bot.Levels - 1.01f; l += 0.05f)
                smooth &= Bot.SkillAt(l + 0.05f).reaction <= Bot.SkillAt(l).reaction && Bot.SkillAt(l + 0.05f).handSpeed >= Bot.SkillAt(l).handSpeed;
            c.C(same, "at a whole level, MATCH MY LEVEL plays exactly that level");
            c.C(smooth, "between levels it gets steadily quicker (reaction, hands)");
            {   // someone who never throws back: every point is the bot's, so it eases down to ROOKIE and stays there
                var g = NewGame(); g.tun.botAdapt = true; g.tun.botMatch = 2f; g.tun.basicMatchTime = 0; g.Reset(true);
                g.tun.selfHitScores = false;   // (its own ball coming back at it would give the other side points)
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                for (int h = 0; h < 2; ++h) @in[0].hand[h].tracked = false;   // (no hands up: still fists would knock its throws back)
                var log = new List<GameEvent>();
                Run(g, @in, 400f, log);
                int pts = 0;
                foreach (GameEvent e in log) pts += e.type == EventType.HitPlayer && e.player == 0 ? 1 : 0;
                TestContext.Out.WriteLine($"  a player who doesn't play: the bot scored {pts}, its level {g.tun.botMatch:0.00}");
                c.C(pts > 5 && g.tun.botMatch == 0f, "against someone who doesn't play back it eases down to ROOKIE");
            }
            c.Done();
        }

        [Test]
        public void Stances()
        {
            var c = new Checks();
            bool reach = true, rig = true, held = true;
            AvatarDesc look = Avatar.PresetLook(3);
            for (int st = 0; st < Bot.Stances; ++st)
            {
                var g = NewGame(); g.tun.botLevel = 3; g.Reset(true);
                g.pl[1].botStance = st;
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                @in[0].head.p.z += 3f;   // (out of the way: nothing happens, the bot just stands)
                Run(g, @in, 2f);
                PlayerState s = g.pl[1];
                for (int h = 0; h < 2; ++h)
                {
                    if (Length(g.BotWrist(h, g.HandPose(1, h)) - g.BotShoulder(1, h)) > g.tun.botArm + 1e-3f) reach = false;
                    if (Length(s.botHandVel[h]) > 0.5f) held = false;
                }
                if (!ArmsHold(s.@in, look)) rig = false;
            }
            c.C(reach && rig, "every stance is within the arm's reach, and the drawn arms never stretch");
            c.C(held, "and it holds it (the hands settled)");
            int open = 0, picks = 0;
            for (int i = 0; i < 40; ++i)
            {   // beginners hold no guard
                var g = NewGame(); g.tun.botLevel = 0; g.RngSeed((uint)(i + 1) * 2654435761u); g.Reset(true);
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                Run(g, @in, 0.1f);
                int st = g.pl[1].botStance;
                open += st == Bot.STANCE_KITTEN || st == Bot.STANCE_RELAXED || st == Bot.STANCE_MONKEY ? 1 : 0;
                ++picks;
            }
            c.C(open == picks, "ROOKIE only takes an open stance (no guard)");
            var all = new bool[Bot.Stances];
            for (int i = 0; i < 80; ++i)
            {
                var g = NewGame(); g.tun.botLevel = 4; g.RngSeed((uint)(i + 7) * 2246822519u); g.Reset(true);
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                Run(g, @in, 0.1f);
                all[g.pl[1].botStance] = true;
            }
            bool every = true;
            foreach (bool b in all) every &= b;
            c.C(every, "from PRO up, every stance comes up (picked at random each match)");
            c.Done();
        }

        [Test]
        public void FistBumpFirst()
        {
            var c = new Checks();
            var g = NewGame(); g.tun.botMeet = true; g.Reset(true);
            g.pl[1].botStance = Bot.STANCE_KARATE;
            var @in = new[] { Standing(g, 0), new PlayerInput() };
            Run(g, @in, 0.1f);
            c.C(g.match == MatchState.Meet, "with FIST BUMP FIRST on, the bot meets you in the centre");
            // the person at their side of the centre line, the right fist held out to the middle, closed
            @in[0].head.p = new Vec3(-g.tun.meetSpot, 1.65f, 0);
            @in[0].hand[1].grip = new Pose(kFaceP0, new Vec3(-0.12f, 1.30f, 0.02f));
            @in[0].hand[1].trigger = 1;
            @in[0].hand[0].grip.p = new Vec3(-0.55f, 1.0f, -0.25f);
            var log = new List<GameEvent>();
            float bowed = 0;
            for (int i = 0; i < 72 * 4 && !HasEvent(log, EventType.FistBump); ++i)
            {
                Step(g, @in);
                log.AddRange(g.events);
            }
            c.C(HasEvent(log, EventType.FistBump), "it brings its fist to yours: a fist bump");
            c.C(MathF.Abs(g.pl[1].@in.head.p.x - g.tun.meetSpot) < 0.3f, "(it came to its side of the centre for it)");
            for (int i = 0; i < 36; ++i)
            {
                Step(g, @in);
                bowed = Max(bowed, -g.pl[1].@in.head.q.Rotate(new Vec3(0, 0, -1)).y);
            }
            c.C(bowed > 0.3f, "KARATE bows at the fist bump");
            Run(g, @in, 1f);
            c.C(g.match == MatchState.Countdown || g.match == MatchState.InProgress, "then the match starts");
            var h = NewGame(); h.Reset(true);
            var in2 = new[] { Standing(h, 0), new PlayerInput() };
            Run(h, in2, 0.1f);
            c.C(h.match == MatchState.Countdown, "with it off (as before), straight to the countdown");
            c.Done();
        }

        [Test, Timeout(600000)]   // (minutes of matches: past NUnit's 3-minute default)
        public void SlapShot()
        {   // the slap shot (owner, 2026-10-29): the stronger bots send a caught ball straight back; the beginners never do
            var c = new Checks();
            int[] slaps = { 0, 0 }, catches = { 0, 0 };
            int shaped = 0;   // (UNBEATABLE's slaps curved or off a wall)
            for (int k = 0; k < 2; ++k)
            {
                var g = NewGame(); g.tun.botLevel = k != 0 ? Bot.Levels - 1 : 0; g.tun.basicMatchTime = 0; g.tun.meetTime = 0;
                g.Reset(true); g.pl[0].isBot = true;
                var @in = new[] { new PlayerInput(), new PlayerInput() };
                float[] caughtAt = { -9, -9 };
                for (int i = 0; i < 72 * 180; ++i)
                {
                    Step(g, @in);
                    foreach (GameEvent e in g.events)
                    {
                        if (e.player < 0 || e.player > 1) continue;
                        if (e.type == EventType.Caught) { caughtAt[e.player] = g.now; ++catches[k]; }
                        if (e.type == EventType.Throw && g.now - caughtAt[e.player] < 0.6f)
                        {
                            ++slaps[k];
                            Ball b = g.pl[e.player].ball;
                            Vec3 to = Normalize(g.ChestPoint(1 - e.player) - b.pos), v = Normalize(b.vel);
                            if (k != 0 && (Length(b.spin) > 3f || Dot(Normalize(new Vec3(to.x, 0, to.z)), Normalize(new Vec3(v.x, 0, v.z))) < 0.96f)) ++shaped;
                        }
                    }
                }
            }
            TestContext.Out.WriteLine($"  ROOKIE: {catches[0]} catches, {slaps[0]} slap shots; UNBEATABLE: {catches[1]} catches, {slaps[1]} slap shots ({shaped} curved or off a wall)");
            // (a shape only when its trial flight would score; against itself, with the knuckle guards on the fist (native
            // 6def918), it catches and slaps more, the shaped ones as many as before (2), the rest straight: 1 in 5 became 1 in 11)
            c.C(shaped * 12 >= slaps[1] && shaped >= 2, "UNBEATABLE's slaps curve and bank too (planned while its ball comes back)");
            c.C(slaps[0] == 0 && catches[1] > 4 && slaps[1] * 3 >= catches[1], "UNBEATABLE often sends its caught ball straight back; ROOKIE never");
            c.Done();
        }

        // NEW BOT (TASKS.md E55, native 75f3f1f test_game.cpp "[NEW BOT: a stance until a new one]"; the owner, 2026-11-20:
        // "hosts may not want it to change so we need a push to change button"): the bot's stance comes from botLook, the
        // same every match until a new one; beginners an open one
        [Test, Timeout(600000)]
        public void NewBotKeepsItsStance()
        {
            List<int> StanceAfter(uint look, int level, int matches)
            {
                Game g = NewGame(); g.tun.botLevel = level; g.botLook = look; g.Reset(true);
                var seen = new List<int>();
                for (int m = 0; m < matches; ++m)
                {
                    g.ResetWithBots(2);   // (a new match: the stance is picked again)
                    var @in = new[] { Standing(g, 0), new PlayerInput() };
                    Run(g, @in, 0.3f);
                    seen.Add(g.pl[1].botStance);
                }
                return seen;
            }
            List<int> kept = StanceAfter(0xC0FFEEu, 3, 4);
            bool same = kept[0] >= 0;
            foreach (int s in kept) same &= s == kept[0];
            Assert.That(same, $"the bot keeps its stance match after match (the same botLook): {string.Join(",", kept)}");
            bool open = true;
            int kinds = 0;
            uint seenMask = 0;
            for (uint look = 1; look <= 40; ++look)
            {
                int s = StanceAfter(look * 7919u, 3, 1)[0];
                if (s >= 0 && s < Bot.Stances && ((seenMask >> s) & 1u) == 0) { seenMask |= 1u << s; ++kinds; }
                int r = StanceAfter(look * 7919u, 0, 1)[0];   // (ROOKIE: no guard)
                open &= r == Bot.STANCE_KITTEN || r == Bot.STANCE_RELAXED || r == Bot.STANCE_MONKEY;
            }
            Assert.That(kinds >= 5 && open, $"NEW BOT's other looks bring other stances ({kinds} of {Bot.Stances} over 40), a ROOKIE only open ones");
            var four = new Game { botLook = 0xC0FFEEu };
            bool differ = false;
            for (int p = 1; p < 4; ++p) differ |= four.BotStanceFor(p, 3f) != four.BotStanceFor(0, 3f);
            Assert.That(differ && four.BotStanceFor(1, 3f) == four.BotStanceFor(1, 3f), "four-player bots: each corner a stance of its own");
        }

        // BOT STYLES PER ROOM (TASKS.md E19, native 7cad5c1 test_game.cpp "[bot styles per room]"; a player's report,
        // 2026-11-17: "probably need different logic for basic and advanced bots")
        [Test, Timeout(600000)]
        public void BotStylesPerRoom()
        {
            var c = new Checks();
            // BASIC's brawler punches through: balls served at UNBEATABLE at 8 m/s (no shields, knuckle guards on), how fast
            // its knuckle guard sends them back; in Advanced with knuckle guards on (the duellist, no brawler) for comparison
            float[] meanOut = { 0, 0 };
            int[] punches = { 0, 0 };
            for (int adv = 0; adv < 2; ++adv)
            {
                var g = NewGame(); g.tun.advanced = true; g.tun.botPlay = adv; g.tun.shields = false; g.tun.knuckleMode = 1;   // (rounds, each style: E23)
                g.tun.basicMatchTime = 0; g.tun.meetTime = 0; g.tun.botLevel = Bot.Levels - 1; g.Reset(true);
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                for (int i = 0; i < 72 * 4 && g.match != MatchState.InProgress; ++i) Step(g, @in);
                float sum = 0;
                for (int k = 0; k < 24; ++k)
                {
                    for (int i = 0; i < 72 * 3 && g.pl[0].ball.state != BallState.Idle; ++i) { @in[0] = Standing(g, 0); Step(g, @in); }
                    Vec3 target = g.ChestPoint(1);
                    float sgn = (k / 4) % 2 != 0 ? -1f : 1f;
                    switch (k % 4)
                    {
                        case 0: break;
                        case 1: target.y += 0.30f; break;
                        case 2: target.y -= 0.30f; break;
                        default: target.z += 0.25f * sgn; break;
                    }
                    Vec3 from = g.tun.start[0] + new Vec3(0.6f, 1.3f, 0);
                    g.Serve(0, from, Normalize(target - from), 8f, new Vec3(), false);
                    for (int i = 0; i < 72 * 3; ++i)
                    {
                        @in[0] = Standing(g, 0);
                        Step(g, @in);
                        bool done = false;
                        foreach (GameEvent e in g.events)
                            if (e.type == EventType.KnuckleBlock && e.player == 1) { sum += e.value; ++punches[adv]; done = true; }
                        if (done || g.pl[0].ball.state != BallState.Flying) break;
                    }
                }
                meanOut[adv] = punches[adv] != 0 ? sum / punches[adv] : 0f;
            }
            TestContext.Out.WriteLine($"  (UNBEATABLE's knuckle guard, balls at 8 m/s: the BRAWLER {punches[0]} punched back at {meanOut[0]:F1} m/s on average, the DUELLIST {punches[1]} at {meanOut[1]:F1})");
            c.C(punches[0] >= 12 && punches[1] >= 12 && meanOut[0] > meanOut[1] + 1.5f,
                "BOT STYLE: the BRAWLER punches through a ball it blocks with a knuckle guard, in rounds too: it goes back faster than the DUELLIST's");
            // ADVANCED's duellist: holding its charged ball while you hold yours, it keeps its shield (as before); your ball
            // uncharged (no shield for you), it throws at once
            float[] firstThrow = { -1, -1 };
            for (int exposed = 0; exposed < 2; ++exposed)
            {
                var g = NewGame(); g.tun.advanced = true; g.tun.botPlay = 1; g.tun.meetTime = 0; g.tun.botLevel = Bot.Levels - 1; g.Reset(true);
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                for (int i = 0; i < 72 * 4 && g.match != MatchState.InProgress; ++i) Step(g, @in);
                {   // (native grabBall: your right hand to your ball, the trigger pulled)
                    Vec3 off = @in[0].hand[1].grip.q.Rotate(g.tun.launcherOffset);
                    @in[0].hand[1].grip.p = g.pl[0].ball.pos - off;
                    @in[0].hand[1].trigger = 1;
                    Step(g, @in);
                }
                for (int i = 0; i < 72 * 4 && g.pl[1].ball.state != BallState.Held; ++i) Step(g, @in);
                if (exposed != 0) g.pl[0].ball.charged = false;
                float t0 = g.now;
                for (int i = 0; i < 72 * 6 && firstThrow[exposed] < 0; ++i)
                {
                    Step(g, @in);
                    if (exposed != 0) g.pl[0].ball.charged = false;
                    foreach (GameEvent e in g.events)
                        if (e.type == EventType.Throw && e.player == 1) firstThrow[exposed] = g.now - t0;
                }
            }
            TestContext.Out.WriteLine($"  (ADVANCED's UNBEATABLE holding its charged ball: you with a shield, it threw after {firstThrow[0]:F1} s; without one, after {firstThrow[1]:F1} s)");
            c.C(firstThrow[1] >= 0 && firstThrow[1] < 1.2f && (firstThrow[0] < 0 || firstThrow[0] > firstThrow[1] + 1.0f),
                "ADVANCED's duellist throws at once when you have no shield, and keeps its own while you keep yours");
            c.Done();
        }

        // UNBEATABLE, its own ball punched back at it (native ff247d2 with 50b11ae's checks: a test-only native commit no task
        // carried, ported with TASKS.md E29; a player's report, 2026-11-17: "on unbeatable ... it can really only be hit by
        // its own ball, so I just switched to only hitting its ball back and it was an ez win"). Each time the bot's ball
        // comes, the player's right fist meets it half a metre in front, punching it back at the bot; and (or not) they throw
        // their own ball at its chest with the left hand whenever they have it. (80 minutes of matches: explicit)
        [Test, Explicit("about 20 minutes"), Category("Long"), Timeout(int.MaxValue)]
        public void UnbeatableAgainstItsOwnBallPunchedBack()
        {
            var c = new Checks();
            int allOwn = 0, allPunched = 0, allCaught = 0, allSlapped = 0, allCaughtBack = 0;
            for (int adv = 0; adv < 2; ++adv)
            for (int throws = 0; throws < 2; ++throws)
            for (int style = 0; style < 3; style += 2)   // straight back, or the fist turned 35 degrees (off a wall)
            foreach (float hs in new[] { 4f, 8f })
            {
                var g = NewGame(); g.tun.advanced = adv != 0; g.tun.knuckleMode = 1; g.tun.basicMatchTime = 0; g.tun.meetTime = 0;
                g.tun.botLevel = Bot.Levels - 1; g.Reset(true);
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                Quat q = style == 2 ? kFaceP0 * Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(35f)) : kFaceP0;
                Vec3 kOff = q.Rotate(g.tun.knuckleOffset);
                float planeX = g.tun.start[0].x + 0.5f;
                int comes = 0, punched = 0, hitBot = 0, hitMe = 0, ownHits = 0;
                bool coming = false;
                int f = -1;
                Vec3 dir = new Vec3(1, 0, 0);
                HandInput h0 = Standing(g, 0).hand[0];
                for (float t = 0; t < 300f; t += 1f / 72)
                {
                    Ball b = g.pl[1].ball;
                    bool now = b.state == BallState.Flying && b.instigator == 1 && b.vel.x < -1f;
                    if (now && !coming) ++comes;
                    coming = now;
                    @in[0] = Standing(g, 0);
                    @in[0].buttonA = g.match == MatchState.MatchSummary;
                    if (now)
                    {
                        float tt = (planeX - b.pos.x) / b.vel.x;   // s until it reaches the plane in front
                        Vec3 meet = b.pos + b.vel * tt;
                        // the fist waits where it will come, then punches into it over the last 0.1 s
                        Vec3 at = new Vec3(planeX - hs * MathF.Max(0f, tt < 0.1f ? 0.1f - tt : 0f), meet.y, meet.z);
                        if (tt < 0.6f && MathF.Abs(meet.z) < 2f && meet.y > 0.3f && meet.y < 2.4f) @in[0].hand[1].grip = new Pose(q, at - kOff);
                    }
                    if (throws != 0)
                    {   // the left hand: takes its ball, swings at 6.5 m/s toward the bot's chest, lets go
                        Ball mine = g.pl[0].ball;
                        if (f < 0 && g.match == MatchState.InProgress && mine.state == BallState.Idle)
                        {
                            f = 0;
                            h0 = Standing(g, 0).hand[0];
                            h0.grip.p = mine.pos - h0.grip.q.Rotate(g.tun.launcherOffset);
                            h0.trigger = 1;
                        }
                        else if (f >= 0)
                        {
                            if (f == 1) dir = Normalize(g.ChestPoint(1) - g.LauncherPoint(0, 0));
                            if (f >= 1 && f < 16) h0.grip.p += dir * (6.5f / 72);
                            if (f == 16) h0.trigger = 0;
                            if (++f > 60) { f = -1; h0 = Standing(g, 0).hand[0]; }
                        }
                        @in[0].hand[0] = h0;
                    }
                    Step(g, @in);
                    foreach (GameEvent e in g.events)
                    {
                        if (e.type == EventType.KnuckleBlock && e.player == 0) ++punched;
                        if (e.type == EventType.Caught && e.player == 1)
                        {   // (the ball you punched back: you touched it last)
                            ++allCaught; allSlapped += g.pl[1].botSlap ? 1 : 0; allCaughtBack += g.pl[1].ball.instigator == 0 ? 1 : 0;
                        }
                        if (e.type == EventType.HitPlayer && e.player == 1) { ++hitBot; ownHits += e.ball == 1 ? 1 : 0; }
                        if (e.type == EventType.HitPlayer && e.player == 0) ++hitMe;
                    }
                }
                string[] styles = { "straight", "", "off a wall" };
                allOwn += ownHits;
                allPunched += punched;
                TestContext.Out.WriteLine($"  {(adv != 0 ? "Advanced (knuckle guards on)" : "Basic")}, {(throws != 0 ? "throwing too" : "never throwing")}, {styles[style]} {hs:F0} m/s punches: " +
                                          $"its ball came {comes} times, punched back {punched}; the bot hit {hitBot} ({ownHits} by its own ball), the player hit {hitMe}");
            }
            TestContext.Out.WriteLine($"  (its own ball got it {allOwn} times in {allPunched} punched back; it caught {allCaughtBack} of the punched-back balls; {allCaught} catches in all, {allSlapped} slapped straight back)");
            c.C(allPunched > 300 && allOwn * 33 <= allPunched, "UNBEATABLE: its own ball punched back at it (straight, off a wall, hard or soft, throwing too or not) seldom gets it");
            // (before 11-17 it caught none of them: it only blocked or dodged; many punches send it off a wall or wide of it)
            c.C(allCaughtBack * 8 >= allPunched && allSlapped * 2 >= allCaught,
                "UNBEATABLE catches its own ball sent back at it (not only a dodge: the owner, 11-17), and slaps most straight back");
            c.Done();
        }

        // its own ball at its back (TASKS.md E29, native de0f5c7; the owner's practice, 2026-11-17: punched back past it, low
        // or high, its ball came back off the floor or the ceiling behind it into its back 2-3 s later, the player's curving
        // throw in the air too). (64 tries: explicit)
        [Test, Explicit("several minutes"), Category("Long"), Timeout(int.MaxValue)]
        public void UnbeatableItsOwnBallFromBehind()
        {
            var c = new Checks();
            int tries = 0, hits = 0, caught = 0, otherHits = 0;
            for (int busy = 0; busy < 2; ++busy)
            {
                int bh = 0, bc = 0, bt = 0;
                foreach (float y in new[] { 0.9f, 1.5f })                   // (where the punch sends it from, 4 m in front of it)
                foreach (float ty in new[] { -0.8f, 2.9f })                 // (past it low, through its platform to the floor below; or high)
                foreach (float z in new[] { -0.45f, -0.3f, 0.3f, 0.45f })   // (beside its body)
                foreach (float sp in new[] { 11f, 15f })
                {
                    var g = NewGame(); g.tun.knuckleMode = 1; g.tun.basicMatchTime = 0; g.tun.meetTime = 0;
                    g.tun.botLevel = Bot.Levels - 1; g.Reset(true);
                    var @in = new[] { Standing(g, 0), new PlayerInput() };
                    bool set = false;
                    for (int i = 0; i < 72 * 40; ++i)
                    {
                        Ball mine = g.pl[1].ball;
                        if (!set && g.match == MatchState.InProgress && mine.state == BallState.Flying && mine.instigator == 1 && mine.pos.x < 3f)
                        {   // (it threw: its ball, as if punched back past it, comes back at its back)
                            Vec3 head = g.pl[1].@in.head.p;
                            mine.pos = new Vec3(head.x - 4f, y, z);
                            mine.vel = Normalize(new Vec3(head.x + 2f, ty, z) - mine.pos) * sp;
                            mine.speedLimit = sp; mine.spin = new Vec3(0, 3f, 0); mine.instigator = 0; mine.scored = false;
                            mine.bounces = 0; mine.timer = 1f;
                            if (busy != 0)
                            {   // and the player's curving throw coming at it from the front at the same time
                                Ball th = g.pl[0].ball;
                                th.state = BallState.Flying; th.heldHand = -1; th.instigator = 0; th.scored = false;
                                th.pos = g.LauncherPoint(0, 1);
                                th.vel = Normalize(g.ChestPoint(1) - th.pos) * 14.5f; th.speedLimit = 14.5f;
                                th.spin = new Vec3(0, 22f, 0); th.bounces = 0; th.timer = 0.2f;
                            }
                            set = true;
                            ++tries; ++bt;
                            for (int k = 0; k < 72 * 5; ++k)
                            {
                                Step(g, @in);
                                foreach (GameEvent e in g.events)
                                {
                                    if (e.type == EventType.HitPlayer && e.player == 1 && e.ball == 1)
                                    {
                                        ++hits; ++bh;
                                        TestContext.Out.WriteLine($"    (hit after {k / 72f:F2} s: from y {y:F1} to {ty:F1}, z {z:+0.00;-0.00}, {sp:F0} m/s; at {e.pos.x:F2} {e.pos.y:F2} {e.pos.z:F2}, plan {g.pl[1].botRead[1].plan}, mode {g.pl[1].botRead[1].mode}, catch {(g.pl[1].botRead[1].catchIt ? 1 : 0)})");
                                    }
                                    if (e.type == EventType.HitPlayer && e.player == 1 && e.ball == 0) ++otherHits;
                                    if (e.type == EventType.Caught && e.player == 1) { ++caught; ++bc; }
                                }
                            }
                            break;
                        }
                        Step(g, @in);
                    }
                }
                TestContext.Out.WriteLine($"  {(busy != 0 ? "the player's curving throw coming too" : "alone")}: its ball punched back past it {bt} times (low through its platform, or high): it got hit {bh}, caught it {bc}");
            }
            TestContext.Out.WriteLine($"  (its own ball got it {hits} of {tries}; the player's throw got it {otherHits})");
            c.C(tries == 64 && hits * 16 <= tries, "UNBEATABLE: its own ball punched back past it, low or high, seldom comes back into it");
            c.Done();
        }

        // two UNBEATABLE bots (TASKS.md E23, native 50b11ae test_game.cpp "[two UNBEATABLE bots]"; the owner, 2026-11-17:
        // "pit two unbeatable bots in the same arena and watch them throw their own ball while punching opponent's ball back
        // at them and similarly dodge opponent's ball while receiving their own ball back from a shield deflection and
        // slapshot it back from either hand"); shields and knuckle guards on, ten minutes timed and ten in rounds. (About 11
        // minutes in the editor: explicit, as the 90-minute one; the same-results traces hold the bot to native's frame for
        // frame on every pass. 2026-11-20: timed 142 / 139 throws, 137 / 124 punches and blocks, 25 / 10 own balls caught back)
        [Test, Explicit("about 11 minutes"), Category("Long"), Timeout(1800000)]
        public void TwoUnbeatableBots()
        {
            var c = new Checks();
            for (int adv = 0; adv < 2; ++adv)
            {
                var g = NewGame(); g.tun.advanced = adv != 0; g.tun.knuckleMode = 1; g.tun.basicMatchTime = 0; g.tun.meetTime = 0;
                g.tun.advancedRoundTime = 0; g.tun.botLevel = Bot.Levels - 1; g.ResetWithBots(0x3);
                var @in = new[] { new PlayerInput(), new PlayerInput() };
                int[] throws = new int[2], punches = new int[2], blocks = new int[2], back = new int[2], both = new int[2], hits = new int[2], own = new int[2];
                int[,] slaps = new int[2, 2];
                bool[] wasBoth = new bool[2];
                for (int i = 0; i < 72 * 600; ++i)
                {
                    @in[0].buttonA = @in[1].buttonA = g.match == MatchState.MatchSummary || g.match == MatchState.RoundSummary;
                    Step(g, @in);
                    for (int p = 0; p < 2; ++p)
                    {   // dodging the other's ball while its own comes back to its hand
                        bool now = g.pl[p].botRead[0].plan == Bot.DODGE && g.pl[p].botRead[1].plan == Bot.CATCH;
                        both[p] += now && !wasBoth[p] ? 1 : 0;
                        wasBoth[p] = now;
                    }
                    foreach (GameEvent e in g.events)
                    {
                        if (e.player < 0 || e.player > 1) continue;
                        if (e.type == EventType.Throw) ++throws[e.player];
                        if (e.type == EventType.KnuckleBlock) ++punches[e.player];
                        if (e.type == EventType.ShieldBlock) ++blocks[e.player];
                        if (e.type == EventType.Caught && g.pl[e.player].ball.instigator != e.player)
                        {   // (its own, sent back)
                            ++back[e.player];
                            if (g.pl[e.player].botSlap && e.hand >= 0 && e.hand < 2) ++slaps[e.player, e.hand];
                        }
                        if (e.type == EventType.HitPlayer) { ++hits[e.player]; own[e.player] += e.ball == e.player ? 1 : 0; }
                    }
                }
                string mode = adv != 0 ? "rounds" : "timed";
                for (int p = 0; p < 2; ++p)
                    TestContext.Out.WriteLine($"  {mode}, bot {p}: {throws[p]} throws, {punches[p]} punches and {blocks[p]} shield blocks of the other's ball; its own ball sent back and caught {back[p]}" +
                                              $" (slapped straight back: {slaps[p, 0]} left hand, {slaps[p, 1]} right); {both[p]} times dodging while its own ball came to its hand; hit {hits[p]} ({own[p]} by its own)");
                bool ok = true;
                for (int p = 0; p < 2; ++p)
                    ok &= throws[p] > 50 && punches[p] + blocks[p] > 20 && back[p] > 5 && slaps[p, 0] + slaps[p, 1] > 3;
                ok &= slaps[0, 0] + slaps[1, 0] > 0 && slaps[0, 1] + slaps[1, 1] > 0;
                c.C(ok, $"two UNBEATABLE bots ({mode}): each throws, punches and blocks the other's ball back, catches its own sent back and slaps it from either hand");
                c.C(both[0] + both[1] > 0, $"two UNBEATABLE bots ({mode}): they dodge the other's ball while their own comes back to their hand");
            }
            c.Done();
        }

        [Test, Timeout(600000)]   // (minutes of matches: past NUnit's 3-minute default)
        public void AttackPlan()
        {   // (testers, 2026-10-30): CHAMPION and UNBEATABLE go for strikes, left and right of the ring in turn, one throw in
            // seven at the player; holding 3 strikes (a big, fast ball), nearly every throw at the player; every shape
            var c = new Checks();
            int[] shapes = new int[5];
            int landedCurved = 0, landedBounced = 0, landedBoth = 0;
            for (int adv = 0; adv < 2; ++adv)
            {
                var g = NewGame(); g.tun.advanced = adv != 0; g.tun.botPlay = adv; g.tun.basicMatchTime = 0; g.tun.meetTime = 0;   // (timed with the BRAWLER, rounds with the DUELLIST)
                g.tun.botLevel = Bot.Levels - 1; g.Reset(true);
                var @in = new[] { Standing(g, 0), new PlayerInput() };
                bool thrownCurved = false;
                int throwsCold = 0, atCold = 0, throwsHot = 0, atHot = 0, strikes = 0, onSide = 0, hits = 0;
                int[] sides = { 0, 0 };
                for (int i = 0; i < 72 * 240; ++i)
                {
                    @in[0].buttonA = g.match == MatchState.MatchSummary || g.match == MatchState.RoundSummary;
                    int held = g.pl[0].strikesAgainst;   // (native strikesHeld(1))
                    Step(g, @in);
                    foreach (GameEvent e in g.events)
                    {
                        if (e.type == EventType.Throw && e.player == 1)
                        {
                            ++shapes[Math.Max(0, Math.Min(4, g.pl[1].botStyle))];
                            thrownCurved = Length(g.pl[1].ball.spin) > 3f;   // (as it leaves the hand: spin fades in flight)
                            if (held >= 3) { ++throwsHot; atHot += g.pl[1].botIntent == 1 ? 1 : 0; }
                            else { ++throwsCold; atCold += g.pl[1].botIntent == 1 ? 1 : 0; }
                        }
                        if (e.type == EventType.Strike && e.player == 0 && e.ball == 1)
                        {   // (not the shot clock's)
                            float side = e.pos.z - g.StrikeZoneCenter(0).z;
                            onSide += side * g.pl[1].botStrikeSide < 0 ? 1 : 0;   // (the side it meant: flipped for the next one as it threw)
                            sides[side > 0 ? 1 : 0] += 1;
                            ++strikes;
                        }
                        hits += e.type == EventType.HitPlayer && e.player == 0 ? 1 : 0;
                        if ((e.type == EventType.HitPlayer && e.player == 0) || (e.type == EventType.Strike && e.player == 0 && e.ball == 1))
                        {
                            Ball b = g.pl[1].ball;
                            bool curved = thrownCurved, bounced = b.bounces > 0;
                            landedCurved += curved ? 1 : 0; landedBounced += bounced ? 1 : 0; landedBoth += curved && bounced ? 1 : 0;
                        }
                    }
                }
                string mode = adv != 0 ? "Advanced" : "Basic";
                TestContext.Out.WriteLine($"  {mode}: {throwsCold} throws under 3 strikes ({atCold} at the player), {throwsHot} at 3 ({atHot} at the player); " +
                                          $"{strikes} strikes ({sides[0]} / {sides[1]} each side, {onSide} where it meant), {hits} hits");
                if (adv == 0)
                {
                    c.C(throwsCold > 20 && atCold * 20 >= throwsCold && atCold * 4 <= throwsCold && strikes * 2 >= throwsCold - atCold,
                        mode + ": under 3 strikes it goes for strikes, about one throw in seven at the player");
                    c.C(strikes > 10 && onSide * 10 >= strikes * 9 && sides[0] * 3 >= strikes && sides[1] * 3 >= strikes,
                        mode + ": its strikes go left and right of the ring in turn");
                    c.C(throwsHot > 5 && atHot * 10 >= throwsHot * 7 && hits > 5, mode + ": holding 3 strikes, nearly every throw is at the player, and hits");
                }
                else
                {   // (BOT STYLES, native 7cad5c1: ADVANCED's duellist, first to 4 points, goes at you more, and at once when you
                    // have no shield; its strikes still alternate)
                    c.C(throwsCold > 20 && atCold * 4 >= throwsCold && atCold * 4 <= throwsCold * 3,
                        mode + " (the duellist): under 3 strikes a quarter to three quarters of its throws at the player");
                    c.C(strikes > 6 && onSide * 10 >= strikes * 9 && sides[0] * 4 >= strikes && sides[1] * 4 >= strikes,
                        mode + ": its strikes go left and right of the ring in turn");
                    c.C(atHot * 10 >= throwsHot * 7 && hits > 10, mode + ": holding 3 strikes, nearly every throw is at the player, and it hits");
                }
            }
            TestContext.Out.WriteLine($"  shapes thrown: {shapes[0]} straight, {shapes[1]} curved, {shapes[2]} off a wall, {shapes[3]} curved off a wall, " +
                                      $"{shapes[4]} off the floor; points and strikes: {landedCurved} curved, {landedBounced} off a wall or the floor, {landedBoth} both");
            c.C(shapes[0] > 0 && shapes[1] > 0 && shapes[2] > 0 && shapes[3] > 0 && shapes[4] > 0, "it throws every shape");
            c.C(landedCurved > 5 && landedBounced > 5 && landedBoth > 0, "and they land: curves, banks, curves off the walls");
            c.Done();
        }
    }
}
