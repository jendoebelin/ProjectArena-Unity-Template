// Same-results check: the scripted scenarios of tools/sim_compare/trace.cpp, played with the C# simulation and written
// in the same trace format (every frame's state and events, floats as their exact bits). Used by the editor test
// (Tests/EditMode/SameResultsTests.cs) and by the IL2CPP compare player (SimCompareMain, Builds.SimCompare), whose
// traces tools/sim_compare/compare.py checks bit for bit against the native ones.
// The scenarios here and in trace.cpp must stay the same, step for step.
using System;
using System.Collections.Generic;
using System.Text;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.SimCompare
{
    public class Scenarios
    {
        public static readonly string[] Names = { "bots", "basic_vs_bot", "advanced_vs_bot", "options_vs_bot", "people", "rookie_vs_bot", "champion_vs_bot", "unbeatable_bots", "allstar_hotdog",
                                                     "diamond_vs_bot", "diamond_people_solid", "corners_vs_bot",
                                                     "fourcorners_ffa", "fourcorners_2v2", "fourcorners_3v1",
                                                     "blitz_pro", "blitz_champion", "blitz_rapid_three" };

        readonly List<string> lines = new List<string>();
        int frame;

        static string H(float f) => BitConverter.ToUInt32(BitConverter.GetBytes(f), 0).ToString("x8");

        void Record(Game g)
        {
            var sb = new StringBuilder();
            sb.Append($"F {frame++} {H(g.now)} {(int)g.match} {g.round} {H(g.roundClock)}");
            for (int p = 0; p < g.np; ++p)   // (four players: four)
            {
                PlayerState s = g.pl[p];
                Ball b = s.ball;
                sb.Append($" | {s.score} {s.roundWins} {s.strikesAgainst} {H(s.shotClock)} {(int)b.state} {H(b.pos.x)} {H(b.pos.y)} {H(b.pos.z)}" +
                          $" {H(b.vel.x)} {H(b.vel.y)} {H(b.vel.z)} {H(b.scale)} {H(s.@in.head.p.x)} {H(s.@in.head.p.y)} {H(s.@in.head.p.z)}");
            }
            if (g.Octagon)
            {   // FIREBALL BLITZ: the ring, health and every fireball
                sb.Append($" # {g.startMask} {g.ringMask} {H(g.ringCloseAt)} {H(g.ringApothem)}");
                for (int p = 0; p < g.np; ++p)
                {
                    sb.Append($" | {g.pl[p].health}");
                    foreach (Ball f in g.fire[p]) sb.Append($" {(int)f.state} {H(f.pos.x)} {H(f.pos.y)} {H(f.pos.z)}");
                }
            }
            lines.Add(sb.ToString());
            foreach (var e in g.events)
                lines.Add($"E {(int)e.type} {e.player} {e.hand} {H(e.pos.x)} {H(e.pos.y)} {H(e.pos.z)} {H(e.value)}");
        }

        void Step(Game g, PlayerInput[] @in)
        {
            var tmp = new PlayerInput[g.np];
            for (int p = 0; p < g.np; ++p) tmp[p] = @in[p] != null ? @in[p].Clone() : new PlayerInput();
            g.Step(1f / 72, tmp);
            Record(g);
        }

        static readonly Quat kFaceP0 = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(-90));
        static readonly Quat kFaceP1 = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(90));

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

        void GrabBall(Game g, PlayerInput[] @in, int h = 1, int p = 0)
        {
            Vec3 off = @in[p].hand[h].grip.q.Rotate(g.tun.launcherOffset);
            @in[p].hand[h].grip.p = g.pl[p].ball.pos - off;
            @in[p].hand[h].trigger = 1;
            Step(g, @in);
        }

        void SwingAndRelease(Game g, PlayerInput[] @in, Vec3 dir, float speed, int h = 1, int p = 0)
        {
            for (int i = 0; i < 15; ++i)
            {
                @in[p].hand[h].grip.p += dir * (speed / 72);
                Step(g, @in);
            }
            @in[p].hand[h].trigger = 0;
            Step(g, @in);
        }

        // two people, no bot (the rules alone, as today's native game has them): each throws in turn when their ball is
        // home, a fast one at the other's chest, a slow lob (the slow and stuck ball rules), a high one off the walls
        void People(Game g, int frames)
        {
            var @in = new[] { Standing(g, 0), Standing(g, 1) };
            float t = 0;
            float[] next = { 4, 5 };
            int[] n = { 0, 0 };
            for (int i = 0; i < frames && g.match != MatchState.MatchSummary; ++i, t += 1f / 72)
            {
                for (int p = 0; p < 2; ++p)
                {
                    if (g.match != MatchState.InProgress || t <= next[p] || g.pl[p].ball.state != BallState.Idle) continue;
                    Vec3 to = Normalize(g.ChestPoint(1 - p) - g.LauncherPoint(p, 1));
                    int kind = n[p]++ % 3;
                    if (kind == 2) to = Normalize(to + new Vec3(0, 0.35f, p == 0 ? 0.4f : -0.4f));
                    GrabBall(g, @in, 1, p);
                    SwingAndRelease(g, @in, to, kind == 0 ? 5.5f : kind == 1 ? 2.0f : 7.0f, 1, p);
                    next[p] = t + 2.5f;
                    @in[p] = Standing(g, p);
                }
                Step(g, @in);
            }
        }

        // a person against the bot: throws at the bot's chest every 2 s when the ball is home (the native full-match test)
        void HumanVsBot(Game g, int frames)
        {
            var @in = new[] { Standing(g, 0), new PlayerInput() };
            float t = 0, nextThrow = 4;
            for (int i = 0; i < frames && g.match != MatchState.MatchSummary; ++i, t += 1f / 72)
            {
                if (g.match == MatchState.InProgress && t > nextThrow && g.pl[0].ball.state == BallState.Idle)
                {
                    GrabBall(g, @in);
                    SwingAndRelease(g, @in, Normalize(g.ChestPoint(1) - g.LauncherPoint(0, 1)), 5.5f);
                    nextThrow = t + 2.0f;
                    @in[0] = Standing(g, 0);
                }
                Step(g, @in);
            }
        }

        // Plays one scenario and returns its trace lines.
        public static List<string> Play(string scenario, List<CollisionTri> arena)
        {
            var s = new Scenarios();
            var g = new Game();
            g.SetArena(arena);
            switch (scenario)
            {
                case "bots":   // the lobby: two bots, 90 s
                {
                    g.Reset(true);
                    g.pl[0].isBot = true;
                    var @in = new[] { Standing(g, 0), new PlayerInput() };
                    for (int i = 0; i < 72 * 90; ++i) s.Step(g, @in);
                    break;
                }
                case "basic_vs_bot":   // Basic, 40 s match
                    g.tun.basicMatchTime = 40; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "advanced_vs_bot":
                    g.tun.advanced = true; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "options_vs_bot":   // MATCH OPTIONS: Advanced, 30 s rounds, hotdog (no shields), knuckle guards on
                    g.tun.advanced = true; g.tun.advancedRoundTime = 30; g.tun.shields = false; g.tun.knuckleMode = 1; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "people":   // two people, Basic, 40 s match, no meeting
                    g.tun.basicMatchTime = 40; g.tun.meetTime = 0; g.Reset(false);
                    s.People(g, 72 * 400);
                    break;
                case "rookie_vs_bot":   // ROOKIE (straight-line reads), Basic, 40 s
                    g.tun.basicMatchTime = 40; g.tun.botLevel = 0; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "champion_vs_bot":   // CHAMPION (tryouts, the attack plan, slap shots), Advanced
                    g.tun.advanced = true; g.tun.botLevel = 4; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "unbeatable_bots":   // two UNBEATABLE bots, 60 s
                {
                    g.tun.botLevel = 5; g.Reset(true);
                    g.pl[0].isBot = true;
                    var @in = new[] { Standing(g, 0), new PlayerInput() };
                    for (int i = 0; i < 72 * 60; ++i) s.Step(g, @in);
                    break;
                }
                case "allstar_hotdog":   // ALL-STAR (planned shots, no hunting), hotdog (no shields): knuckle guards and dodges
                    g.tun.basicMatchTime = 40; g.tun.botLevel = 3; g.tun.shields = false; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "diamond_vs_bot":   // EXPERIMENTAL DIAMOND against CHAMPION (its banks off the rolled walls), 40 s
                    g.tun.experimental = true; g.tun.expRoom = Room.Diamond; g.UseArena(Arena.Diamond, arena);
                    g.tun.basicMatchTime = 40; g.tun.botLevel = 4; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "diamond_people_solid":   // DIAMOND, solid platforms, two people, 40 s, no meeting
                    g.tun.experimental = true; g.tun.expRoom = Room.Diamond; g.tun.solidPlatforms = true; g.UseArena(Arena.Diamond, arena);
                    g.tun.basicMatchTime = 40; g.tun.meetTime = 0; g.Reset(false);
                    s.People(g, 72 * 400);
                    break;
                case "corners_vs_bot":   // EXPERIMENTAL CORNERS for two against the bot (PRO), 40 s
                    g.tun.experimental = true; g.tun.expRoom = Room.Corners; g.UseArena(Arena.Corners, arena);
                    g.tun.basicMatchTime = 40; g.Reset(true);
                    s.HumanVsBot(g, 72 * 400);
                    break;
                case "fourcorners_ffa":   // FOUR CORNERS, four bots, 40 s: FREE-FOR-ALL (PRO), 2 V 2 (CHAMPION), 3 V 1 (AMATEUR; THE ONE slot 2)
                case "fourcorners_2v2":
                case "fourcorners_3v1":
                {
                    int mode = scenario == "fourcorners_ffa" ? 0 : scenario == "fourcorners_2v2" ? 1 : 2;
                    g.tun.experimental = true; g.tun.expRoom = Room.Corners; g.tun.players = 4; g.tun.teamMode = mode;
                    g.tun.botLevel = mode == 0 ? 2 : mode == 1 ? 4 : 1; g.tun.theOne = mode == 2 ? 2 : 0; g.tun.basicMatchTime = 40;
                    g.UseArena(Arena.FourCorners, arena); g.ResetWithBots(0xF);
                    var @in = new PlayerInput[Teams.MaxPlayers];
                    for (int i = 0; i < 72 * 48; ++i) s.Step(g, @in);
                    break;
                }
                case "blitz_pro":           // FIREBALL BLITZ, four bots, as the app sets it (no shields, knuckle guards on): PRO, HEALTH 3, 60 s
                case "blitz_champion":      // ... CHAMPION (curves and wall banks aimed by tryouts), HEALTH 5, 60 s
                case "blitz_rapid_three":   // ... three of four places (one sits out), ALL-STAR, RAPID FIRE, shields on, HEALTH 3, 30 s
                {
                    g.tun.experimental = true; g.tun.expRoom = Room.Blitz; g.tun.players = 4; g.tun.meetTime = 0;
                    bool rapid = scenario == "blitz_rapid_three";
                    g.tun.botLevel = scenario == "blitz_pro" ? 2 : rapid ? 3 : 4;
                    g.tun.health = scenario == "blitz_champion" ? 5 : 3;
                    g.tun.basicMatchTime = rapid ? 30 : 60;
                    if (rapid) g.tun.fireBalls = 4; else { g.tun.shields = false; g.tun.knuckleMode = 1; }
                    g.octCap = rapid ? 3 : 4;
                    g.UseArena(Arena.Octagon, arena); g.ResetWithBots(rapid ? 0x7u : 0xFu);
                    var @in = new PlayerInput[Teams.MaxPlayers];
                    for (int i = 0; i < 72 * 75 && g.match != MatchState.MatchSummary; ++i) s.Step(g, @in);
                    break;
                }
                default: throw new ArgumentException("unknown scenario " + scenario);
            }
            return s.lines;
        }
    }
}
