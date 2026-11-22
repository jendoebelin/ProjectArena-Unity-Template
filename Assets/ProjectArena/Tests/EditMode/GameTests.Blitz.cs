// FIREBALL BLITZ (task A5), ported from the native tests/test_game.cpp (octagonTests, native a7230f8): the ring
// and its places, a fireball's hops and its end, the body from the hips up, the ways out (a knuckle guard, a step), how
// many fireballs can be in the room, a match to the last one standing, the best bots' curves and wall banks, the
// ceasefire after a knockout, the at-time rule, and the bots' dodges by their level.
// (Native's run() hands a four-player game two inputs; here every place gets one: its own, or a default.)
using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        static Game OctGame(uint bots, int health = 5, int balls = 1)
        {
            Game g = NewGame();
            g.tun.experimental = true; g.tun.expRoom = Room.Blitz; g.tun.players = 4; g.tun.meetTime = 0;
            g.tun.health = health; g.tun.fireBalls = balls; g.tun.basicMatchTime = 0;
            g.UseArena(Arena.Octagon, Tris);
            g.ResetWithBots(bots);
            return g;
        }

        // one step with these inputs (copied: the game writes them back), every place given one
        static void OctStep(Game g, PlayerInput[] @in)
        {
            var tmp = new PlayerInput[Teams.MaxPlayers];
            for (int p = 0; p < Teams.MaxPlayers; ++p) tmp[p] = p < @in.Length && @in[p] != null ? @in[p].Clone() : new PlayerInput();
            g.Step(1f / 72, tmp);
        }

        static void OctRun(Game g, PlayerInput[] @in, float seconds, List<GameEvent> log = null)
        {
            for (float t = 0; t < seconds; t += 1f / 72)
            {
                OctStep(g, @in);
                log?.AddRange(g.events);
            }
        }

        static PlayerInput[] AllStanding(Game g) => new[] { Standing(g, 0), Standing(g, 1), Standing(g, 2), Standing(g, 3) };

        static Ball TestFire(Vec3 pos, Vec3 vel, float speedLimit)
        {
            var b = new Ball { scale = 1f };
            b.pos = pos; b.vel = vel; b.speedLimit = speedLimit;
            return b;
        }

        [Test]
        public void FireballBlitz()
        {
            var c = new Checks("FIREBALL BLITZ", 23);
            {
                Game g = OctGame(0);
                bool ring = true;
                for (int p = 0; p < 4; ++p)
                {
                    ring &= MathF.Abs(Length(g.tun.start[p]) - 6f) < 1e-3f && Dot(g.Facing(p), Normalize(new Vec3(0, 0, 0) - g.tun.start[p])) > 0.999f;
                    ring &= Length(g.tun.start[p] - new Vec3(-7f, 0, 0)) > 0.5f || p != 0;
                }
                c.C(ring && MathF.Abs(g.tun.start[0].x + 6f) < 1e-3f && MathF.Abs(g.tun.start[2].z + 6f) < 1e-3f && MathF.Abs(g.ringApothem - 7.5f) < 1e-3f,
                    "four places evenly round a 6 m ring, each facing the centre, the walls 1.5 m behind");
                g.LayoutRing(0x3);
                c.C(MathF.Abs(g.tun.start[0].x + 3.5f) < 1e-3f && MathF.Abs(g.tun.start[1].x - 3.5f) < 1e-3f && MathF.Abs(g.ringApothem - 5f) < 1e-3f,
                    "two left: face to face 3.5 m from the centre, the walls closed in");
                g.LayoutRing(0xB);
                bool three = true;
                foreach (int p in new[] { 0, 1, 3 }) three &= MathF.Abs(Length(g.tun.start[p]) - 4.8f) < 1e-3f;
                c.C(three && MathF.Abs(Dot(Normalize(g.tun.start[0]), Normalize(g.tun.start[1])) + 0.5f) < 1e-3f, "three: 4.8 m, a third of the way round each");
                Game o = OctGame(0);
                o.UseArena(Arena.Classic, Tris);
                c.C(Length(o.tun.start[0] - new Vec3(-7f, 0, 0)) < 1e-4f && Length(o.tun.spawner[1] - new Vec3(6.48f, 0.02f, 0)) < 1e-4f,
                    "leaving the octagon puts every place back where it always was");
            }
            {   // a fireball: down to the floor, back up to knee height each hop, its speed along the floor kept, gone at its 4th bounce
                Game g = OctGame(0);
                var @in = new[] { Standing(g, 0) };
                g.Serve(0, g.tun.start[0] + new Vec3(0.6f, 1.3f, 0.3f), Normalize(new Vec3(1, 0, 0.35f)), 9f, default, false);
                c.C(g.pl[0].ball.state != BallState.Flying && g.FireCount(0) == 1, "a throw becomes a fireball (the tee keeps no flying ball)");
                float apex = 0, lastY = 9, minFlat = 99, maxFlat = 0;
                int bounces = 0, fizzles = 0;
                bool rising = false;
                for (int i = 0; i < 72 * 4 && g.FireCount(0) != 0; ++i)
                {
                    Ball f = g.fire[0][0];
                    OctStep(g, @in);
                    foreach (GameEvent e in g.events) { bounces += e.type == EventType.Bounce ? 1 : 0; fizzles += e.type == EventType.Fizzle ? 1 : 0; }
                    if (f.state != BallState.Flying) break;
                    float flat = Length(new Vec3(f.vel.x, 0, f.vel.z));
                    minFlat = Min(minFlat, flat); maxFlat = Max(maxFlat, flat);
                    if (bounces >= 1) { if (f.pos.y > lastY) rising = true; else if (rising) { apex = Max(apex, lastY); rising = false; } }
                    lastY = f.pos.y;
                }
                c.C(apex > 0.9f && apex < 1.06f && maxFlat - minFlat < 0.05f,
                    $"it hops to the waist ({apex:F2} m) at the same speed along the floor ({minFlat:F2} to {maxFlat:F2} m/s)");
                c.C(bounces == 3 && fizzles == 1 && g.FireCount(0) == 0, "three hops, and it fizzles out at the fourth bounce");
            }
            {   // a body: the legs count; your own fireball never hurts you
                Game g = OctGame(0);
                Vec3 head = g.pl[1].@in.head.p;
                Ball b = TestFire(head + new Vec3(-3f, -1.25f, 0), new Vec3(8f, 0, 0), 8f);
                c.C(g.FireHitTime(b, head, 0.3f) < 0f, "a fireball at knee height doesn't count (waist up only)");
                b.pos = head + new Vec3(-3f, -0.6f, 0); b.vel = new Vec3(8f, 2f, 0);
                c.C(g.FireHitTime(b, head, 1f) >= 0f, "one at the body does");
                // (across behind, at seat height: the butt counts; level as it passes behind)
                Ball seat = TestFire(head + new Vec3(0.2f, -1.03f, -2f), new Vec3(0, 1.5f, 8f), 8f);
                c.C(g.FireHitTime(seat, head, 0.4f) >= 0f, "one across your seat counts (the hips and butt are in)");
                PlayerInput[] @in = AllStanding(g);
                for (int i = 0; i < 72 * 5 && g.match != MatchState.InProgress; ++i) OctStep(g, @in);
                @in[0] = Standing(g, 0);
                g.Serve(0, g.pl[0].@in.head.p + new Vec3(0, 3f, 0), new Vec3(0, -1, 0), 6f, default, false);   // (straight down onto its own head)
                var log = new List<GameEvent>();
                OctRun(g, @in, 1f, log);
                c.C(g.match == MatchState.InProgress && g.pl[0].health == 5 && !HasEvent(log, EventType.HitPlayer), "your own fireball never hurts you");
            }
            {   // the ways out (owner, 2026-11-02: "couldn't get away from fireballs"): a fist held low punches it away; a
                // jump lets it pass under; standing still, it hits
                (bool hit, bool punched) Shot(int how)   // 0 stand, 1 a fist low in its way, 2 a step to the side as it comes
                {
                    Game g = OctGame(0);
                    var @in = new[] { Standing(g, 0), Standing(g, 1) };
                    for (int i = 0; i < 72 * 5 && g.match != MatchState.InProgress; ++i) { @in[0] = Standing(g, 0); @in[1] = Standing(g, 1); OctStep(g, @in); }
                    @in[1] = Standing(g, 1);
                    for (int i = 0; i < 72 * 3; ++i) OctStep(g, @in);   // (its usual height)
                    Vec3 head = g.pl[1].@in.head.p;
                    if (how == 1)
                    {   // the right fist at the waist, a little in front, toward the thrower
                        @in[1].hand[1].grip.p = head + new Vec3(-0.45f, -0.75f, 0);
                        @in[1].hand[1].grip.q = Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(90)) * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(-60));
                    }
                    Vec3 from = head + new Vec3(-5f, -0.5f, 0);
                    Vec3 dir = new Vec3(1, 0, 0);
                    for (float deg = -30; deg <= 20; deg += 2)
                    {   // (the throw that meets the body)
                        Vec3 d = new Vec3(MathF.Cos(Deg2Rad(deg)), MathF.Sin(Deg2Rad(deg)), 0);
                        Ball b = TestFire(from, d * 8f, 8f * MathF.Cos(Deg2Rad(deg)));
                        if (g.FireHitTime(b, head, 2f) >= 0) { dir = d; break; }
                    }
                    g.Serve(0, from, dir, 8f, default, false);
                    var log = new List<GameEvent>();
                    for (int i = 0; i < 72 * 2; ++i)
                    {
                        var tmp = new[] { @in[0].Clone(), @in[1].Clone() };
                        if (how == 2 && i / 72f > 0.3f)
                        {   // a step half a metre to the side as it comes
                            tmp[1].head.p.z += 0.5f;
                            for (int h = 0; h < 2; ++h) tmp[1].hand[h].grip.p.z += 0.5f;
                        }
                        OctStep(g, tmp);
                        log.AddRange(g.events);
                    }
                    return (HasEvent(log, EventType.HitPlayer, 1), HasEvent(log, EventType.KnuckleBlock, 1));
                }
                var stand = Shot(0); var punch = Shot(1); var step = Shot(2);
                c.C(stand.hit, "standing still in its way: hit");
                c.C(!punch.hit && punch.punched, "a knuckle guard held in its way punches it away");
                c.C(!step.hit, "a step to the side as it comes: it goes past");
            }
            // how many in the room: one ball, the tee waits; RAPID FIRE, up to three, the tee refilling at once
            foreach (int balls in new[] { 1, 4 })
            {
                Game g = OctGame(0, 5, balls);
                var @in = new[] { Standing(g, 0) };
                int most = 0, served = 0;
                float firstAt = -1, refill = -1;   // (the first throw, and how long until the tee had another)
                for (int i = 0; i < 72 * 6; ++i)
                {
                    Ball t = g.pl[0].ball;
                    if (t.state == BallState.Idle)
                    {
                        if (served == 1 && refill < 0) refill = g.now - firstAt;
                        if (served == 0) firstAt = g.now;
                        g.Serve(0, g.IdlePoint(0), Normalize(new Vec3(1, 0.4f, (served % 3 - 1) * 0.3f)), 7f, default, false);
                        ++served;
                    }
                    OctStep(g, @in);
                    most = Math.Max(most, g.FireCount(0));
                }
                if (balls == 1)
                    c.C(most == 1 && refill > 1f, $"ONE BALL: one in the room at a time; the tee waits until it fizzles ({refill:F1} s)");
                else
                    c.C(most == 3 && refill >= 0 && refill < 0.4f && served >= 5, $"RAPID FIRE: up to three in the room, the tee refilled in {refill:F2} s ({served} thrown in 6 s)");
            }
            {   // a match: you (standing still) and three bots; knockouts, the ring closing in, the last one standing
                Game g = OctGame(0xE, 3);
                g.tun.botLevel = 3;
                var @in = new PlayerInput[1];
                int knockouts = 0, closes = 0, hits = 0;
                bool shrank = false, ended = false;
                for (int i = 0; i < 72 * 400 && !ended; ++i)
                {
                    @in[0] = Standing(g, 0);
                    OctStep(g, @in);
                    foreach (GameEvent e in g.events)
                    {
                        knockouts += e.type == EventType.Knockout ? 1 : 0; closes += e.type == EventType.RingClose ? 1 : 0;
                        hits += e.type == EventType.HitPlayer ? 1 : 0;
                        ended |= e.type == EventType.MatchEnd;
                    }
                    int alive = 0;
                    for (int p = 0; p < 4; ++p) alive += g.Alive(p) ? 1 : 0;
                    if (alive == 2 && g.ringCloseAt < 0 && g.match == MatchState.InProgress)
                    {
                        bool two = true;
                        for (int p = 0; p < 4; ++p) if (g.Alive(p)) two &= MathF.Abs(Length(g.tun.start[p]) - 3.5f) < 1e-3f;
                        shrank |= two && MathF.Abs(g.ringApothem - 5f) < 1e-3f;
                    }
                }
                c.C(ended && knockouts == 3 && closes == 2 && g.matchWinner >= 0 && g.Alive(g.matchWinner) && hits >= 9,
                    $"a match plays out: {hits} hits, {knockouts} knocked out, the ring closed {closes} times, the last one standing wins ({(g.matchWinner >= 0 ? "player" : "none")})");
                c.C(shrank, "with two left, they stand 3.5 m out, the walls closed in");
                c.C(g.pl[0].health == 0 && g.pl[0].ball.state == BallState.Dead, "you, standing still, are knocked out (and your tee stays empty)");
            }
            {   // the best bots' fireballs: straight, curved and off the walls (owner, 2026-11-02: "only straight balls")
                Game g = OctGame(0xE, 100);
                g.tun.botLevel = Bot.Levels - 1;
                var @in = new PlayerInput[1];
                int thrown = 0, curved = 0, banked = 0;
                for (int i = 0; i < 72 * 60; ++i)
                {
                    @in[0] = Standing(g, 0);
                    OctStep(g, @in);
                    foreach (GameEvent e in g.events)
                    {
                        if (e.type != EventType.Throw || e.player == 0) continue;
                        foreach (Ball f in g.fire[e.player])
                        {
                            if (f.state != BallState.Flying || f.timer > 0.03f) continue;   // (the one just thrown)
                            ++thrown;
                            if (Length(f.spin) > 3f) ++curved;
                            else
                            {   // aimed well off the line to everyone: off a wall
                                float bestDot = -2f;
                                for (int q = 0; q < 4; ++q)
                                {
                                    if (q == e.player || !g.Alive(q)) continue;
                                    Vec3 to = g.pl[q].@in.head.p - f.pos;
                                    bestDot = Max(bestDot, Dot(Normalize(new Vec3(to.x, 0, to.z)), Normalize(new Vec3(f.vel.x, 0, f.vel.z))));
                                }
                                banked += bestDot < 0.93f ? 1 : 0;
                            }
                        }
                    }
                }
                c.C(thrown > 20 && curved * 10 >= thrown && banked * 8 >= thrown && (thrown - curved - banked) * 3 >= thrown,
                    $"UNBEATABLE's fireballs: {thrown} thrown, {curved} curved, {banked} off a wall, the rest straight");
            }
            {   // a knockout: a ceasefire until the room has closed in (owner, 2026-11-02: no cheap shots while everyone moves)
                Game g = OctGame(0, 1);
                PlayerInput[] @in = AllStanding(g);
                for (int i = 0; i < 72 * 5 && g.match != MatchState.InProgress; ++i) OctStep(g, @in);
                void At(int from, int to)
                {   // a fireball from one at another, the throw that meets them
                    Vec3 head = g.pl[to].@in.head.p, src = g.pl[from].@in.head.p + new Vec3(0, -0.5f, 0);
                    Vec3 flat = Normalize(new Vec3(head.x - src.x, 0, head.z - src.z)), dir = flat;
                    for (float deg = -30; deg <= 20; deg += 2)
                    {
                        Vec3 d = flat * MathF.Cos(Deg2Rad(deg)) + new Vec3(0, MathF.Sin(Deg2Rad(deg)), 0);
                        Ball b = TestFire(src, d * 9f, 9f * MathF.Cos(Deg2Rad(deg)));
                        if (g.FireHitTime(b, head, 2f) >= 0) { dir = d; break; }
                    }
                    g.ParkBall(from);
                    g.Serve(from, src, dir, 9f, default, false);
                }
                At(1, 0);   // (player 0 knocked out)
                var log = new List<GameEvent>();
                for (int i = 0; i < 72 * 2 && g.Alive(0); ++i) { OctStep(g, @in); log.AddRange(g.events); }
                bool closing = !g.Alive(0) && g.ringCloseAt >= 0;
                At(2, 3);   // a shot during the warning
                log.Clear();
                for (int i = 0; i < 72 * 1; ++i) { OctStep(g, @in); log.AddRange(g.events); }
                c.C(closing && g.Alive(3) && !HasEvent(log, EventType.HitPlayer), "a knockout: nobody can be hit until the room has closed in");
            }
            {   // at time: the most health wins; a tie at the top goes to sudden death, one hit each
                Game g = OctGame(0, 5);
                g.tun.basicMatchTime = 5f;
                PlayerInput[] @in = AllStanding(g);
                for (int i = 0; i < 72 * 5 && g.match != MatchState.InProgress; ++i) OctStep(g, @in);
                g.pl[2].health = 2; g.pl[3].health = 4;
                var log = new List<GameEvent>();
                OctRun(g, @in, 6f, log);
                c.C(g.overtime && g.Alive(0) && g.Alive(1) && !g.Alive(2) && !g.Alive(3) && g.pl[0].health == 1 && g.pl[1].health == 1 &&
                    HasEvent(log, EventType.Overtime), "at time the most health stays in; tied at the top: sudden death, one hit each");
            }
            {   // the bots dodge, by their level: fireballs at a bot that is standing its ground
                int[] hit = { 0, 0 };
                for (int k = 0; k < 2; ++k)
                {
                    Game g = OctGame(0x2, 100);
                    g.tun.botLevel = k != 0 ? Bot.Levels - 1 : 0;
                    g.LayoutRing(0x3);
                    var @in = new[] { Standing(g, 0) };
                    for (int i = 0; i < 72 * 5 && g.match != MatchState.InProgress; ++i) { @in[0] = Standing(g, 0); OctStep(g, @in); }
                    for (int shot = 0; shot < 20; ++shot)
                    {
                        @in[0] = Standing(g, 0);
                        // aimed at the bot as it stands: the pitch that reaches it
                        Vec3 from = g.tun.start[0] + new Vec3(0.5f, 1.3f, 0);
                        Vec3 flat = Normalize(new Vec3(g.pl[1].@in.head.p.x - from.x, 0, g.pl[1].@in.head.p.z - from.z));
                        Vec3 best = flat;
                        for (float deg = -30; deg <= 10; deg += 2)
                        {
                            Vec3 d = flat * MathF.Cos(Deg2Rad(deg)) + new Vec3(0, MathF.Sin(Deg2Rad(deg)), 0);
                            Ball b = TestFire(from, d * 11f, Max(5f, 11f * MathF.Cos(Deg2Rad(deg))));
                            if (g.FireHitTime(b, g.pl[1].@in.head.p, 2f) >= 0) { best = d; break; }
                        }
                        if (g.pl[0].ball.state != BallState.Dead) g.ParkBall(0);
                        g.Serve(0, from, best, 11f, default, false);
                        for (int i = 0; i < 72 * 2; ++i)
                        {
                            @in[0] = Standing(g, 0);
                            OctStep(g, @in);
                            foreach (GameEvent e in g.events) hit[k] += e.type == EventType.HitPlayer && e.player == 1 ? 1 : 0;
                        }
                    }
                }
                c.C(hit[1] <= 4 && hit[0] >= hit[1] + 6, $"the bots dodge by their level: 20 fireballs, ROOKIE hit {hit[0]} times, UNBEATABLE {hit[1]}");
            }
            c.Done();
        }

        [Test]
        public void FireballBlitzInTeams()
        {   // FIREBALL BLITZ in teams (TASKS.md E51, native 61589a0 test_game, 0.3.24; the owner, 2026-11-20: "I wanted to
            // select 3 against 1 where I was the 1"): no friendly fire, THE ONE three times the HEALTH, a team wins when the
            // other is all out; with an older player in the room, everyone for themselves
            var c = new Checks("FIREBALL BLITZ in teams", 7);
            Game TeamGame(int mode, int version)
            {
                Game g = OctGame(0, 5);
                g.tun.teamMode = mode; g.tun.theOne = 2; g.tun.matchVersion = version;
                PlayerInput[] @in = AllStanding(g);
                for (int i = 0; i < 72 * 5 && g.match != MatchState.InProgress; ++i) OctStep(g, @in);
                return g;
            }
            bool Shoot(Game g, int from, int to)
            {   // a fireball from one at another, the throw that meets them; hit?
                Vec3 head = g.pl[to].@in.head.p, src = g.pl[from].@in.head.p + new Vec3(0, -0.5f, 0);
                Vec3 flat = Normalize(new Vec3(head.x - src.x, 0, head.z - src.z)), dir = flat;
                for (float deg = -30; deg <= 20; deg += 2)
                {
                    Vec3 d = flat * MathF.Cos(Deg2Rad(deg)) + new Vec3(0, MathF.Sin(Deg2Rad(deg)), 0);
                    Ball b = TestFire(src, d * 9f, 9f * MathF.Cos(Deg2Rad(deg)));
                    if (g.FireHitTime(b, head, 2f) >= 0) { dir = d; break; }
                }
                g.ParkBall(from);
                g.Serve(from, src, dir, 9f, default, false);
                var log = new List<GameEvent>();
                OctRun(g, AllStanding(g), 2f, log);
                return HasEvent(log, EventType.HitPlayer, to);
            }
            {
                Game g = TeamGame(Teams.TwoVTwo, Tunables.kVerAll);
                bool mates = g.TeamsNow() == Teams.TwoVTwo && g.Team(0) == g.Team(1) && g.Team(2) == g.Team(3) && g.Team(0) != g.Team(2);
                bool through = !Shoot(g, 0, 1) && g.pl[1].health == 5;
                bool rival = Shoot(g, 0, 2) && g.pl[2].health == 4;
                c.C(mates && through && rival, "FIREBALL BLITZ 2 v 2: partners across the ring; a teammate's fireball passes, a rival's hits");
            }
            {
                Game g = TeamGame(Teams.ThreeVOne, Tunables.kVerAll);
                c.C(g.pl[2].health == 15 && g.MaxHealth(2) == 15 && g.pl[0].health == 5 && g.pl[1].health == 5 && g.pl[3].health == 5,
                    "FIREBALL BLITZ 3 v 1: THE ONE starts with three times the HEALTH (15), the three with 5");
            }
            {
                Game g = TeamGame(Teams.TwoVTwo, Tunables.kVerAll);
                PlayerInput[] @in = AllStanding(g);
                g.pl[1].health = 0; g.pl[3].health = 0;   // (one out on each team)
                OctRun(g, @in, 0.5f);
                bool goesOn = g.match == MatchState.InProgress;
                g.pl[2].health = 0;   // (and the other of that team: it is all out)
                var log = new List<GameEvent>();
                OctRun(g, @in, 0.5f, log);
                c.C(goesOn && g.match == MatchState.MatchSummary && g.matchWinner == g.Team(0) && HasEvent(log, EventType.MatchEnd),
                    "FIREBALL BLITZ in teams: one out on each team, it goes on; a team all out, the other team wins");
            }
            {
                Game g = TeamGame(Teams.TwoVTwo, Tunables.kVerAll);
                g.tun.basicMatchTime = 5f; g.roundClock = 0.5f;
                g.pl[0].health = 5; g.pl[1].health = 1; g.pl[2].health = 3; g.pl[3].health = 4;   // (6 against 7)
                OctRun(g, AllStanding(g), 1.5f);
                c.C(!g.Alive(0) && !g.Alive(1) && g.Alive(2) && g.Alive(3) && g.match == MatchState.MatchSummary && g.matchWinner == g.Team(2),
                    "FIREBALL BLITZ in teams, at time: the team with the most health left between them wins");
            }
            {
                Game g = TeamGame(Teams.ThreeVOne, Tunables.kVerBlitzTeams - 1);
                bool ffa = g.TeamsNow() == Teams.FFA && g.Team(1) == 1 && g.pl[2].health == 5;
                c.C(ffa && Shoot(g, 0, 1), "FIREBALL BLITZ with a player before 0.3.24: everyone for themselves (THE ONE as everyone)");
            }
            {
                Game g = OctGame(0, 5);   // (FOUR CORNERS' teams as before: the same setting, its own room)
                var cg = new Game();
                cg.tun.experimental = true; cg.tun.expRoom = Room.Corners; cg.tun.players = 4; cg.tun.teamMode = Teams.ThreeVOne;
                cg.tun.matchVersion = Tunables.kVerBlitzTeams - 1;
                cg.ResetWithBots(0);
                c.C(g.TeamsNow() == Teams.FFA && cg.TeamsNow() == Teams.ThreeVOne && cg.Team(cg.tun.theOne) == 1,
                    "FIREBALL BLITZ is everyone for themselves unless TEAMS says otherwise; FOUR CORNERS' 3 v 1 as before");
            }
            {   // (native 3fccaf9, review 2026-11-21) a team nobody plays in: everyone for themselves, and the match goes on (it
                // had ended the moment it began, all who played being on one team)
                Game Start(int mode, int one, int here)
                {   // here: the corners with a player (bits)
                    Game g = OctGame(0, 5);
                    g.tun.teamMode = mode; g.tun.theOne = one; g.tun.matchVersion = Tunables.kVerAll;
                    var @in = new PlayerInput[4];
                    for (int p = 0; p < 4; ++p) @in[p] = ((here >> p) & 1) != 0 ? Standing(g, p) : new PlayerInput();
                    for (int i = 0; i < 72 * 8 && g.match != MatchState.InProgress; ++i) OctStep(g, @in);
                    OctRun(g, @in, 1f);
                    return g;
                }
                Game two = Start(Teams.TwoVTwo, 0, 0x3);     // (corners 0 and 1: both on one team)
                Game empty = Start(Teams.ThreeVOne, 3, 0x7);   // (THE ONE's corner empty)
                Game three = Start(Teams.ThreeVOne, 0, 0x7);   // (THE ONE and two of the three)
                bool waiting = OctGame(0, 5).TeamsNow() == Teams.FFA;   // (TEAMS off: as before)
                Game w = OctGame(0, 5); w.tun.teamMode = Teams.TwoVTwo;  // (before the countdown: everyone counts)
                c.C(two.match == MatchState.InProgress && two.TeamsNow() == Teams.FFA && !two.Friendly(0, 1) &&
                    empty.match == MatchState.InProgress && empty.TeamsNow() == Teams.FFA && empty.pl[0].health == 5 &&
                    three.match == MatchState.InProgress && three.TeamsNow() == Teams.ThreeVOne && three.pl[0].health == 15 &&
                    waiting && w.TeamsNow() == Teams.TwoVTwo,
                    "FIREBALL BLITZ in teams with a team nobody plays in: everyone for themselves, and the match goes on");
            }
            c.Done();
        }
    }
}
