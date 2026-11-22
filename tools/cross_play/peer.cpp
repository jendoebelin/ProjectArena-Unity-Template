// Cross-play check, native side: one native Net (the native repo's app/src/net.cpp, read only) driven by commands on
// stdin, so a Unity test (Tests/EditMode/CrossPlayTests.cs) can put native and Unity headsets in the same arena room
// through a local relay. Build: tools\cross_play\build.bat (writes Build\cross_play\peer.exe).
//
// Usage: peer.exe NAME RELAY_HOST:PORT
// Commands (one per line): host | join HOSTNAME [play] | queue 0|1 | leave | look PRESET | fist X FACING_X | rest |
//   four TEAMS (the rules: FOUR CORNERS, TEAMS 0 free-for-all, 1 2 v 2, 2 3 v 1; before hosting) | press 0|1 (A held:
//   READY UP) | swap A B (the host's MOVE PLAYERS) | quit. Every 0.25 s, and after each command, it prints a line
//   starting with '@', then one starting with '@+':
//   @ mode M ready R session S slot L qpos Q room A B bot T match MS score P0 P1 rounds R0 R1 games N:NAME,...
//     members ID:SUIT/NATION,... names ID:NAME,... events E
//   @+ np N slots A B C D botmask M one O
#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include "avatar.h"
#include "net.h"

bool netAcquireMulticastLock() { return false; }

static std::mutex g_mu;
static std::vector<std::string> g_lines;
static std::atomic<bool> g_eof{false};

static void reader() {
    std::string ln;
    while (std::getline(std::cin, ln)) { std::lock_guard<std::mutex> lk(g_mu); g_lines.push_back(ln); }
    g_eof = true;
}

static void report(Net& net, Game& g, int events) {
    const RoomInfo& r = net.room();
    std::string games, members, names;
    for (const LanGame& lg : net.games()) games += std::to_string(lg.ip) + ":" + lg.name + ",";
    for (const RoomInfo::Member& m : r.members)
        members += std::to_string(m.id) + ":" + std::to_string(m.in.avatar.suit) + "/" + std::to_string(avatar::nationOf(m.in.avatar)) + ",";
    for (const RoomInfo::Name& n : r.names) names += std::to_string(n.id) + ":" + n.name + ",";
    std::printf("@ mode %d ready %d session %u slot %d qpos %d room %u %u bot %d match %d score %d %d rounds %d %d games %s members %s names %s events %d\n",
                (int)net.mode(), net.onlineReady() ? 1 : 0, net.session(), net.localSlot(), net.localQueuePos(), r.slot[0], r.slot[1],
                r.bot ? 1 : 0, (int)g.match, g.pl[0].score, g.pl[1].score, g.pl[0].roundWins, g.pl[1].roundWins,
                games.empty() ? "-" : games.c_str(), members.empty() ? "-" : members.c_str(), names.empty() ? "-" : names.c_str(), events);
    std::printf("@+ np %d slots %u %u %u %u botmask %d one %d\n", g.np, r.slot[0], r.slot[1], r.slot[2], r.slot[3], (int)r.botMask, g.tun.theOne);
    std::fflush(stdout);
}

int main(int argc, char** argv) {
    if (argc < 3) { std::fprintf(stderr, "usage: peer NAME RELAY\n"); return 2; }
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    Net net;
    Game game;
    net.init();
    net.setName(argv[1]);
    game.reset(true);
    PlayerInput local;
    local.headTracked = true;
    local.head = {Quat{}, {2.5f, 1.6f, -4.2f}};
    for (int h = 0; h < 2; ++h) { local.hand[h].tracked = true; local.hand[h].grip = {Quat{}, {2.5f + (h ? 0.2f : -0.2f), 1.1f, -4.0f}}; }
    local.avatar = avatar::preset(10);
    const PlayerInput rest = local;
    net.goOnline(argv[2]);
    std::thread(reader).detach();
    int events = 0;
    auto last = std::chrono::steady_clock::now();
    for (;;) {
        std::vector<std::string> cmds;
        { std::lock_guard<std::mutex> lk(g_mu); cmds.swap(g_lines); }
        bool quit = g_eof;
        for (const std::string& c : cmds) {
            char a[64] = {}, b[64] = {};
            float x = 0, f = 0;
            if (c == "host") net.host();
            else if (std::sscanf(c.c_str(), "join %63s %63s", a, b) >= 1) {
                for (const LanGame& lg : net.games()) if (!std::strcmp(lg.name, a)) net.join(lg.ip, !std::strcmp(b, "play"));
            } else if (std::sscanf(c.c_str(), "queue %f", &x) == 1) net.queue(x != 0);
            else if (c == "leave") net.leave();
            else if (std::sscanf(c.c_str(), "look %f", &x) == 1) { local.avatar = avatar::preset((int)x); avatar::setNation(local.avatar, (int)x == 1 ? 7 : 0); }
            else if (std::sscanf(c.c_str(), "fist %f %f", &x, &f) == 2) {   // the right fist's front at (x, 1.2, 0), facing +-X
                local.hand[1].trigger = 1;
                local.hand[1].grip.q = fromTo({0, 0, -1}, {f, 0, 0});
                local.hand[1].grip.p = Vec3{x, 1.2f, 0} - local.hand[1].grip.q.rotate(game.tun.fistFront);
            } else if (c == "rest") { const AvatarDesc keep = local.avatar; local = rest; local.avatar = keep; }
            else if (std::sscanf(c.c_str(), "four %f", &x) == 1) {
                game.tun.experimental = true; game.tun.expRoom = ROOM_CORNERS; game.tun.players = 4; game.tun.teamMode = (int)x;
            } else if (std::sscanf(c.c_str(), "press %f", &x) == 1) local.buttonA = x != 0;
            else if (std::sscanf(c.c_str(), "swap %f %f", &x, &f) == 2) net.hostSwap((int)x, (int)f, game);
            else if (c == "quit") quit = true;
            report(net, game, events);
        }
        if (quit) break;
        PlayerInput in[kMaxPlayers];   // (the host fills all four places)
        if (net.exchange(local, in, game, 1.f / 90.f)) { game.step(1.f / 90.f, in); net.sendState(game); }
        events += (int)game.events.size();
        if (std::chrono::steady_clock::now() - last > std::chrono::milliseconds(250)) { last = std::chrono::steady_clock::now(); report(net, game, events); }
        std::this_thread::sleep_for(std::chrono::milliseconds(11));
    }
    if (net.online()) net.goOffline();
    net.shutdown();
    return 0;
}
