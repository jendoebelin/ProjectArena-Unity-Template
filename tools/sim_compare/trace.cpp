// Same-results check, native side: plays scripted scenarios with the NATIVE simulation (the native repo's
// app/src/game.cpp, read only) and writes every frame's state and events, floats as their exact bits. The C# port plays
// the same scenarios (Assets/ProjectArena/Tests/EditMode/SameResultsTests.cs) and must match: events, states and scores
// exactly, positions within 1 mm (exact bits expected on x64).
// Build and run: tools/sim_compare/run.bat (Visual Studio 2022; writes tools/sim_compare/traces/*.trace.gz via Python).
// The scenarios here and in SameResultsTests.cs must stay the same, step for step.
#include "game.h"
#include <cstdio>
#include <cstring>
#include <vector>

static std::vector<CollisionTri> loadCol(const char* path) {
    std::vector<CollisionTri> out;
    FILE* f = std::fopen(path, "rb");
    if (!f) { std::printf("cannot open %s\n", path); std::exit(2); }
    char magic[4]; uint32_t n;
    std::fread(magic, 1, 4, f); std::fread(&n, 4, 1, f);
    for (uint32_t i = 0; i < n; ++i) {
        float v[9]; uint32_t tags;
        std::fread(v, 4, 9, f); std::fread(&tags, 4, 1, f);
        CollisionTri t{{v[0], v[1], v[2]}, {v[3], v[4], v[5]}, {v[6], v[7], v[8]}, {}, tags};
        t.n = normalize(cross(t.b - t.a, t.c - t.a));
        out.push_back(t);
    }
    std::fclose(f);
    return out;
}

static unsigned bits(float f) { unsigned u; std::memcpy(&u, &f, 4); return u; }

static FILE* g_out;
static int g_frame;

static void record(const Game& g) {
    std::fprintf(g_out, "F %d %08x %d %d %08x", g_frame++, bits(g.now), (int)g.match, g.round, bits(g.roundClock));
    for (int p = 0; p < g.np; ++p) {   // (four players: four)
        const PlayerState& s = g.pl[p];
        const Ball& b = s.ball;
        std::fprintf(g_out, " | %d %d %d %08x %d %08x %08x %08x %08x %08x %08x %08x %08x %08x %08x", s.score, s.roundWins,
                     s.strikesAgainst, bits(s.shotClock), (int)b.state, bits(b.pos.x), bits(b.pos.y), bits(b.pos.z),
                     bits(b.vel.x), bits(b.vel.y), bits(b.vel.z), bits(b.scale), bits(s.in.head.p.x), bits(s.in.head.p.y),
                     bits(s.in.head.p.z));
    }
    if (g.octagon()) {   // FIREBALL BLITZ: the ring, health and every fireball
        std::fprintf(g_out, " # %d %d %08x %08x", g.startMask, g.ringMask, bits(g.ringCloseAt), bits(g.ringApothem));
        for (int p = 0; p < g.np; ++p) {
            std::fprintf(g_out, " | %d", g.pl[p].health);
            for (const Ball& f : g.fire[p]) std::fprintf(g_out, " %d %08x %08x %08x", (int)f.state, bits(f.pos.x), bits(f.pos.y), bits(f.pos.z));
        }
    }
    std::fprintf(g_out, "\n");
    for (const GameEvent& e : g.events)
        std::fprintf(g_out, "E %d %d %d %08x %08x %08x %08x\n", (int)e.type, e.player, e.hand, bits(e.pos.x), bits(e.pos.y),
                     bits(e.pos.z), bits(e.value));
}

static void step(Game& g, PlayerInput* in) {
    PlayerInput tmp[kMaxPlayers];
    for (int p = 0; p < g.np; ++p) tmp[p] = in[p];
    g.step(1.f / 72, tmp);
    record(g);
}

static const Quat kFaceP0 = Quat::axisAngle({0, 1, 0}, deg2rad(-90));
static const Quat kFaceP1 = Quat::axisAngle({0, 1, 0}, deg2rad(90));

static PlayerInput standing(const Game& g, int p) {
    PlayerInput in;
    in.headTracked = true;
    Quat face = p == 0 ? kFaceP0 : kFaceP1;
    in.head = {face, g.tun.start[p] + Vec3{0, 1.65f, 0}};
    for (int h = 0; h < 2; ++h) {
        in.hand[h].tracked = true;
        in.hand[h].grip = {face, g.tun.start[p] + Vec3{p == 0 ? 0.2f : -0.2f, 1.0f, (h == 0 ? -0.3f : 0.3f) * (p == 0 ? 1.f : -1.f)}};
    }
    return in;
}

static void grabBall(Game& g, PlayerInput in[2], int h = 1, int p = 0) {
    Vec3 off = in[p].hand[h].grip.q.rotate(g.tun.launcherOffset);
    in[p].hand[h].grip.p = g.pl[p].ball.pos - off;
    in[p].hand[h].trigger = 1;
    step(g, in);
}

static void swingAndRelease(Game& g, PlayerInput in[2], Vec3 dir, float speed, int h = 1, int p = 0) {
    for (int i = 0; i < 15; ++i) {
        in[p].hand[h].grip.p += dir * (speed / 72);
        step(g, in);
    }
    in[p].hand[h].trigger = 0;
    step(g, in);
}

// two people, no bot (the rules alone, as today's native game has them): each throws in turn when their ball is home,
// a fast one at the other's chest, a slow lob (the slow and stuck ball rules), a high one off the walls
static void people(Game& g, int frames) {
    PlayerInput in[2] = {standing(g, 0), standing(g, 1)};
    float t = 0, next[2] = {4, 5};
    int n[2] = {0, 0};
    for (int i = 0; i < frames && g.match != MatchState::MatchSummary; ++i, t += 1.f / 72) {
        for (int p = 0; p < 2; ++p) {
            if (g.match != MatchState::InProgress || t <= next[p] || g.pl[p].ball.state != BallState::Idle) continue;
            Vec3 to = normalize(g.chestPoint(1 - p) - g.launcherPoint(p, 1));
            const int kind = n[p]++ % 3;
            if (kind == 2) to = normalize(to + Vec3{0, 0.35f, p == 0 ? 0.4f : -0.4f});
            grabBall(g, in, 1, p);
            swingAndRelease(g, in, to, kind == 0 ? 5.5f : kind == 1 ? 2.0f : 7.0f, 1, p);
            next[p] = t + 2.5f;
            in[p] = standing(g, p);
        }
        step(g, in);
    }
}

// a person against the bot: throws at the bot's chest every 2 s when the ball is home (the native full-match test)
static void humanVsBot(Game& g, int frames) {
    PlayerInput in[2] = {standing(g, 0), {}};
    float t = 0, nextThrow = 4;
    for (int i = 0; i < frames && g.match != MatchState::MatchSummary; ++i, t += 1.f / 72) {
        if (g.match == MatchState::InProgress && t > nextThrow && g.pl[0].ball.state == BallState::Idle) {
            grabBall(g, in);
            swingAndRelease(g, in, normalize(g.chestPoint(1) - g.launcherPoint(0, 1)), 5.5f);
            nextThrow = t + 2.0f;
            in[0] = standing(g, 0);
        }
        step(g, in);
    }
}

int main(int argc, char** argv) {
    if (argc < 3) { std::printf("usage: trace <arena.col> <out dir>\n"); return 2; }
    auto tris = loadCol(argv[1]);
    const char* names[] = {"bots", "basic_vs_bot", "advanced_vs_bot", "options_vs_bot", "people", "rookie_vs_bot",
                           "champion_vs_bot", "unbeatable_bots", "allstar_hotdog", "diamond_vs_bot", "diamond_people_solid",
                           "corners_vs_bot", "fourcorners_ffa", "fourcorners_2v2", "fourcorners_3v1",
                           "blitz_pro", "blitz_champion", "blitz_rapid_three"};
    for (int sc = 0; sc < 18; ++sc) {
        char path[512];
        std::snprintf(path, sizeof path, "%s/%s.trace", argv[2], names[sc]);
        g_out = std::fopen(path, "w");
        g_frame = 0;
        Game g; g.setArena(tris);
        if (sc == 0) {          // the lobby: two bots, 90 s
            g.reset(true);
            g.pl[0].isBot = true;
            PlayerInput in[2] = {standing(g, 0), {}};
            for (int i = 0; i < 72 * 90; ++i) step(g, in);
        } else if (sc == 1) {   // Basic, 40 s match
            g.tun.basicMatchTime = 40; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc == 2) {   // Advanced
            g.tun.advanced = true; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc == 3) {   // MATCH OPTIONS: Advanced, 30 s rounds, hotdog (no shields), knuckle guards on
            g.tun.advanced = true; g.tun.advancedRoundTime = 30; g.tun.shields = false; g.tun.knuckleMode = 1; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc == 4) {   // two people, Basic, 40 s match, no meeting
            g.tun.basicMatchTime = 40; g.tun.meetTime = 0; g.reset(false);
            people(g, 72 * 400);
        } else if (sc == 5) {   // ROOKIE (straight-line reads), Basic, 40 s
            g.tun.basicMatchTime = 40; g.tun.botLevel = 0; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc == 6) {   // CHAMPION (tryouts, the attack plan, slap shots), Advanced
            g.tun.advanced = true; g.tun.botLevel = 4; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc == 7) {   // two UNBEATABLE bots, 60 s
            g.tun.botLevel = 5; g.reset(true);
            g.pl[0].isBot = true;
            PlayerInput in[2] = {standing(g, 0), {}};
            for (int i = 0; i < 72 * 60; ++i) step(g, in);
        } else if (sc == 8) {   // ALL-STAR (planned shots, no hunting), hotdog (no shields): knuckle guards and dodges
            g.tun.basicMatchTime = 40; g.tun.botLevel = 3; g.tun.shields = false; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc == 9) {   // EXPERIMENTAL DIAMOND against CHAMPION (its banks off the rolled walls), 40 s
            g.tun.experimental = true; g.tun.expRoom = ROOM_DIAMOND; g.useArena(ARENA_DIAMOND, tris);
            g.tun.basicMatchTime = 40; g.tun.botLevel = 4; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc == 10) {  // DIAMOND, solid platforms, two people, 40 s, no meeting
            g.tun.experimental = true; g.tun.expRoom = ROOM_DIAMOND; g.tun.solidPlatforms = true; g.useArena(ARENA_DIAMOND, tris);
            g.tun.basicMatchTime = 40; g.tun.meetTime = 0; g.reset(false);
            people(g, 72 * 400);
        } else if (sc == 11) {  // EXPERIMENTAL CORNERS for two against the bot (PRO), 40 s
            g.tun.experimental = true; g.tun.expRoom = ROOM_CORNERS; g.useArena(ARENA_CORNERS, tris);
            g.tun.basicMatchTime = 40; g.reset(true);
            humanVsBot(g, 72 * 400);
        } else if (sc >= 15) {  // FIREBALL BLITZ, four bots, as the app sets it (no shields, knuckle guards on): PRO, HEALTH 3,
                                // 60 s; CHAMPION (curves and wall banks aimed by tryouts), HEALTH 5, 60 s; three of four places
                                // (one sits out), ALL-STAR, RAPID FIRE, shields on, HEALTH 3, 30 s
            const bool rapid = sc == 17;
            g.tun.experimental = true; g.tun.expRoom = ROOM_BLITZ; g.tun.players = 4; g.tun.meetTime = 0;
            g.tun.botLevel = sc == 15 ? 2 : rapid ? 3 : 4;
            g.tun.health = sc == 16 ? 5 : 3;
            g.tun.basicMatchTime = rapid ? 30.f : 60.f;
            if (rapid) g.tun.fireBalls = 4; else { g.tun.shields = false; g.tun.knuckleMode = 1; }
            g.octCap = rapid ? 3 : 4;
            g.useArena(ARENA_OCTAGON, tris); g.resetWithBots(rapid ? 0x7u : 0xFu);
            PlayerInput in[kMaxPlayers];
            for (int i = 0; i < 72 * 75 && g.match != MatchState::MatchSummary; ++i) step(g, in);
        } else {                // FOUR CORNERS, four bots, 40 s: FREE-FOR-ALL (PRO), 2 V 2 (CHAMPION), 3 V 1 (AMATEUR; THE ONE slot 2)
            const int mode = sc - 12;
            g.tun.experimental = true; g.tun.expRoom = ROOM_CORNERS; g.tun.players = 4; g.tun.teamMode = mode;
            g.tun.botLevel = mode == 0 ? 2 : mode == 1 ? 4 : 1; g.tun.theOne = mode == 2 ? 2 : 0; g.tun.basicMatchTime = 40;
            g.useArena(ARENA_FOURCORNERS, tris); g.resetWithBots(0xF);
            PlayerInput in[kMaxPlayers];
            for (int i = 0; i < 72 * 48; ++i) step(g, in);
        }
        std::fclose(g_out);
        std::printf("%s: %d frames\n", names[sc], g_frame);
    }
    return 0;
}
