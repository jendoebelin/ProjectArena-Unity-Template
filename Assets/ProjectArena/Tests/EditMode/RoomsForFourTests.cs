// Rooms for four online (task B2), ported from the native tests/test_rooms.cpp "[FOUR CORNERS and DIAMOND online]"
// (native a7230f8) against Net/Net.cs, through a private local relay as RoomTests: a FOUR CORNERS room (2 v 2, then
// 3 v 1), MOVE PLAYERS and THE ONE, four playing and four waiting, a DIAMOND room's rules, READY UP and everyone back to
// the waiting area after the match; and FIREBALL BLITZ (task A5): a full room's snapshot fits the relay, and every
// visitor sees the health, the ring and the fireballs. READY UP is checked in FIREBALL BLITZ, as natively.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ProjectArena.Online;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;
using Node = ProjectArena.Tests.RoomTests.Node;

namespace ProjectArena.Tests
{
    public class RoomsForFourTests
    {
        const int RelayPort = 47795, CrowdPort = 47796;

        sealed class Checks
        {
            readonly List<string> failed = new List<string>();
            public int count;
            public void C(bool c, string msg) { ++count; if (!c) failed.Add(msg); TestContext.Out.WriteLine((c ? "  ok   " : "  FAIL ") + msg); }
            public void Done(int expected)
            {
                Assert.That(failed, Is.Empty, "failed: " + string.Join("; ", failed));
                Assert.That(count, Is.EqualTo(expected), "checks run");
            }
        }

        [Test, Timeout(300000)]
        public void FourCornersAndDiamondOnline()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_room4_test_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            var fs = new List<Node>();
            bool shown = Online.Net.NewRoomsShown;
            Online.Net.NewRoomsShown = true;   // (the lobby lists them once their looks are in: task C3)
            try { Run(fs); }
            finally
            {
                Online.Net.NewRoomsShown = shown;
                foreach (var n in fs) { if (n.net.Online) n.net.GoOffline(); n.net.Shutdown(); }
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        static void Run(List<Node> fs)
        {
            var t = new Checks();
            string server = $"127.0.0.1:{RelayPort}";
            TestContext.Out.WriteLine("[FOUR CORNERS and DIAMOND online (0.3.12, network version 9)]");
            string[] fnames = { "QUAD HOST", "QUAD ONE", "QUAD TWO", "QUAD THREE", "DIAMOND HOST", "DIAMOND ONE" };
            for (int i = 0; i < 6; ++i) fs.Add(new Node(fnames[i], -3f + i));
            foreach (var n in fs) n.net.GoOnline(server);
            RoomTests.Pump(fs, 1.0);
            Node Q = fs[0];
            Q.game.tun.experimental = true; Q.game.tun.expRoom = Room.Corners; Q.game.tun.players = 4; Q.game.tun.teamMode = Teams.TwoVTwo;
            Q.net.Host();
            Node D = fs[4];
            D.game.tun.experimental = true; D.game.tun.expRoom = Room.Diamond;
            D.net.Host();
            RoomTests.Pump(fs, 1.0);
            uint qip = 0, dip = 0;
            bool listed = false;
            foreach (LanGame lg in fs[1].net.Games())
            {
                if (lg.name == "QUAD HOST") { qip = lg.ip; listed = lg.experimental && lg.expRoom == 1 && lg.players == 4 && lg.teamMode == Teams.TwoVTwo; }
                if (lg.name == "DIAMOND HOST") dip = lg.ip;
            }
            t.C(listed, "a FOUR CORNERS room says so in the games list (4 players, 2 v 2)");
            for (int i = 1; i <= 2; ++i) fs[i].net.Join(qip, false);
            fs[5].net.Join(dip, false);
            RoomTests.Pump(fs, 1.0);
            Q.net.Queue(true);
            for (int i = 1; i <= 2; ++i) fs[i].net.Queue(true);
            RoomTests.Pump(fs, 2.0);
            t.C(Q.game.np == 4 && Q.net.Room.slot[0] != 0 && Q.net.Room.slot[1] != 0 && Q.net.Room.slot[2] != 0, "three people queue into a FOUR CORNERS tank");
            t.C(Q.net.Room.IsBot(3) && Q.game.pl[3].isBot && fs[1].game.pl[3].isBot, "the empty corner: a bot (host and visitors agree)");
            // MOVE PLAYERS (owner 2026-10-30): the host swaps itself into the bot's corner; the bot takes the host's
            uint hostId = Q.net.Room.slot[0];
            t.C(!fs[1].net.HostSwap(0, 3, fs[1].game), "only the host can move players");
            t.C(Q.net.HostSwap(0, 3, Q.game), "the host swaps two corners");
            RoomTests.Pump(fs, 1.0);
            t.C(Q.net.Room.slot[3] == hostId && Q.net.Room.slot[0] == 0 && Q.net.Room.IsBot(0) && Q.game.pl[0].isBot && !Q.game.pl[3].isBot,
                "the host plays the bot's corner, the bot the host's");
            t.C(fs[1].net.Room.slot[3] == hostId && fs[1].game.pl[0].isBot && fs[2].net.Room.IsBot(0), "and the visitors see it so");
            Q.game.tun.teamMode = Teams.ThreeVOne;   // (from the next match: 3 v 1, the host THE ONE, owner 2026-10-30)
            fs[3].net.Join(qip, false);
            RoomTests.Pump(fs, 1.0);
            fs[3].net.Queue(true);
            RoomTests.Pump(fs, 2.5);
            bool four = Q.net.Room.slot[3] != 0 && !Q.net.Room.IsBot(3);
            for (int i = 1; i <= 3; ++i) four &= fs[i].net.LocalSlot() >= 0 && fs[i].game.np == 4 && fs[i].game.tun.players == 4;
            t.C(four, "a fourth person takes the bot's corner: four people, every game with four players");
            bool agree = true;
            for (int i = 1; i <= 3; ++i)
                for (int p = 0; p < 4; ++p) agree &= Length(fs[i].game.pl[p].@in.head.p - Q.game.pl[p].@in.head.p) < 0.05f || p == fs[i].net.LocalSlot();
            t.C(agree, "everyone sees the same four players where the host does");
            {   // four playing, four more waiting (owner, 2026-11-02: is there room for them?); a ninth is turned away
                var extra = new List<Node>();
                string[] xn = { "WAIT ONE", "WAIT TWO", "WAIT THREE", "WAIT FOUR", "WAIT FIVE" };
                for (int i = 0; i < 5; ++i) { extra.Add(new Node(xn[i], 4f + i)); extra[extra.Count - 1].net.GoOnline(server); }
                fs.AddRange(extra);
                RoomTests.Pump(fs, 1.0);
                for (int i = fs.Count - 5; i < fs.Count - 1; ++i) fs[i].net.Join(qip, false);
                RoomTests.Pump(fs, 1.5);
                fs[fs.Count - 1].net.Join(qip, false);
                RoomTests.Pump(fs, 1.5);
                int waiting = 0;
                foreach (var m in Q.net.Room.members) waiting += m.role == RoomInfo.Waiting ? 1 : 0;
                bool ninthIn = fs[fs.Count - 1].net.CurrentMode == Online.Net.Mode.Client;
                t.C(waiting == 4 && !ninthIn, $"a FOUR CORNERS room holds four playing and four waiting ({waiting} waiting); a ninth is turned away");
                for (int i = fs.Count - 5; i < fs.Count; ++i) fs[i].net.Leave();
                RoomTests.Pump(fs, 1.0);
                for (int i = fs.Count - 5; i < fs.Count; ++i) { if (fs[i].net.Online) fs[i].net.GoOffline(); fs[i].net.Shutdown(); }
                fs.RemoveRange(fs.Count - 5, 5);
            }
            t.C(fs[2].game.tun.experimental && fs[2].game.tun.expRoom == 1 && fs[2].game.tun.teamMode == Teams.ThreeVOne, "and plays the host's rules (FOUR CORNERS, 3 v 1)");
            t.C(Q.game.tun.theOne == Q.net.LocalSlot() && fs[2].game.tun.theOne == Q.net.LocalSlot(), "3 v 1: the host is THE ONE (unless set to take turns)");
            t.C(Q.net.LocalSlot() == 3 && Q.net.Room.slot[0] == fs[3].net.Session, "the fourth person took the bot's corner, where it was (corners stay)");
            {   // THE ONE goes with the player; the host can make another corner THE ONE, and it stays
                uint one = Q.net.Room.slot[Q.game.tun.theOne];
                Q.net.HostSwap(3, 1, Q.game);
                RoomTests.Pump(fs, 1.0);
                t.C(Q.net.Room.slot[Q.game.tun.theOne] == one && fs[2].game.tun.theOne == Q.game.tun.theOne, "a swap: THE ONE goes with the player");
                t.C(Q.net.HostMakeOne(2, Q.game), "the host makes another corner THE ONE");
                RoomTests.Pump(fs, 1.0);
                uint two = Q.net.Room.slot[2];
                Q.net.HostSwap(0, 3, Q.game);
                RoomTests.Pump(fs, 1.0);
                t.C(Q.game.tun.theOne == 2 && Q.net.Room.slot[2] == two && fs[1].game.tun.theOne == 2, "and it stays THE ONE through other swaps");
            }
            int bytes = Online.Net.SnapshotBytes(Q.game);
            t.C(bytes <= 1200, $"a four-player snapshot with a full event log fits the relay ({bytes} of 1200 bytes)");
            fs[5].net.Queue(true);
            D.net.Queue(true);
            RoomTests.Pump(fs, 2.0);
            t.C(fs[5].game.tun.experimental && fs[5].game.tun.expRoom == 0 && fs[5].game.np == 2 && fs[5].net.LocalSlot() >= 0,
                "a DIAMOND room: its player gets the host's EXPERIMENTAL rules");
            {   // FIREBALL BLITZ (0.3.13): its health, who plays, and its fireballs reach everyone; a full room fits the relay
                var full = new Game(); full.tun.experimental = true; full.tun.expRoom = Room.Blitz; full.tun.players = 4;
                full.UseArena(Arena.Octagon, new List<CollisionTri>()); full.ResetWithBots(0xF);
                foreach (Ball[] row in full.fire) foreach (Ball f in row) { f.state = BallState.Flying; f.pos = new Vec3(1, 1, 1); f.instigator = 0; }
                int fullBytes = Online.Net.SnapshotBytes(full);
                t.C(fullBytes <= 1200, $"FIREBALL BLITZ: four players and twelve fireballs with a full event log fit the relay ({fullBytes} of 1200 bytes)");
                Q.game.tun.expRoom = Room.Blitz;
                Q.game.UseArena(Arena.Octagon, new List<CollisionTri>());
                for (int i = 1; i <= 3; ++i)
                {
                    RoomTests.Pump(fs, 0.3);
                    fs[i].game.UseArena(fs[i].game.tun.expRoom == Room.Blitz ? Arena.Octagon : Arena.Classic, new List<CollisionTri>());
                }
            }
            {   // READY UP (0.3.14): nothing starts until every person in the tank pressed A
                RoomTests.Pump(fs, 2.5);
                bool held = Q.game.match == MatchState.WaitingForPlayers && Q.game.tun.readyUp;
                fs[0].local.buttonA = true;
                RoomTests.Pump(fs, 0.4);
                fs[0].local.buttonA = false;
                RoomTests.Pump(fs, 0.5);
                bool seen = fs[1].game.pl[Q.net.LocalSlot()].ready && Q.game.match == MatchState.WaitingForPlayers;
                t.C(held && seen, "a room for four waits for everyone's READY (A), and the others see who is");
                for (int i = 1; i <= 3; ++i) fs[i].local.buttonA = true;
                RoomTests.Pump(fs, 0.4);
                for (int i = 1; i <= 3; ++i) fs[i].local.buttonA = false;
                for (int i = 0; i < 40 && Q.game.match != MatchState.InProgress; ++i) RoomTests.Pump(fs, 0.25);
                Q.game.pl[2].health = 3;
                Q.game.Serve(0, Q.game.tun.start[0] + new Vec3(0.5f, 1.3f, 0), Normalize(new Vec3(1, 0.3f, 0.2f)), 8f, default, false);
                RoomTests.Pump(fs, 0.3);
                bool same = true;
                for (int i = 1; i <= 3; ++i)
                {
                    Game c = fs[i].game;
                    same &= c.Octagon && c.pl[2].health == 3 && c.startMask == Q.game.startMask && c.FireCount(0) == Q.game.FireCount(0) && Q.game.FireCount(0) == 1;
                    same &= Length(c.tun.start[1] - Q.game.tun.start[1]) < 1e-3f;
                    // (the fireball where the host has it, or (BALL LOOK-AHEAD, 0.3.24, TASKS.md E49) where it will within the
                    // lead, 0.4 s at most: drawn ahead for the one it flies at)
                    Game ah = Q.game.Clone();
                    float best = Length(c.fire[0][0].pos - ah.fire[0][0].pos);
                    for (int s = 0; s < 36 && ah.fire[0][0].state == BallState.Flying; ++s)
                    {
                        var inp = new PlayerInput[Teams.MaxPlayers];
                        for (int p = 0; p < Teams.MaxPlayers; ++p) inp[p] = ah.pl[p].@in.Clone();
                        ah.Step(1f / 90f, inp);
                        if (ah.fire[0][0].state == BallState.Flying) best = Math.Min(best, Length(c.fire[0][0].pos - ah.fire[0][0].pos));
                    }
                    same &= best < 0.3f;
                }
                t.C(Q.game.match == MatchState.InProgress && same, "FIREBALL BLITZ online: every visitor sees the health, the ring and the fireballs");
                // a match in a room for four ends: everyone back to the waiting area (owner, 2026-11-03), JOIN QUEUE again
                Q.game.matchWinner = 0;
                Q.game.match = MatchState.MatchSummary;
                RoomTests.Pump(fs, 7.5);
                bool @out = Q.net.Room.slot[0] == 0 && Q.net.Room.slot[1] == 0 && Q.net.Room.slot[2] == 0 && Q.net.Room.slot[3] == 0;
                for (int i = 0; i <= 3; ++i) @out &= fs[i].net.LocalWaiting();
                t.C(@out, "a room for four: after the match everyone goes back to the waiting area");
            }
            t.Done(22);
        }
    }
}
