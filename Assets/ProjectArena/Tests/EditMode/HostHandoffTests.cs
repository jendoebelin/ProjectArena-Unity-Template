// The room outlives its host (task B3), ported from the native tests/test_rooms.cpp "[HOST HANDOFF and IDLE ROOMS
// (0.3.13)]" (native a7230f8) against Net/Net.cs, through a private local relay as RoomTests: a host going quiet (its
// headset asleep: it isn't pumped), the longest there taking the room over mid-match, MAKE HOST, someone the host
// removed staying out under the next host, the old host waking up and rejoining, LEAVE GAME handing the room on, a
// player whose own Wi-Fi drops going back to the real room, and IDLE ROOMS (shortened).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ProjectArena.Online;
using ProjectArena.Sim;
using Node = ProjectArena.Tests.RoomTests.Node;

namespace ProjectArena.Tests
{
    public class HostHandoffTests
    {
        const int RelayPort = 47797, CrowdPort = 47798;

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
        public void TheRoomOutlivesItsHost()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_handoff_test_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            var all = new List<Node>();
            try { Run(all); }
            finally
            {
                RoomTests.Still = false;
                foreach (var n in all) { if (n.net.Online) n.net.GoOffline(); n.net.Shutdown(); }
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        static void Exchange(Node n)
        {
            var @in = new[] { new PlayerInput(), new PlayerInput(), new PlayerInput(), new PlayerInput() };
            n.net.Exchange(n.local, @in, n.game, 0.01f);
        }

        static void Run(List<Node> all)
        {
            var t = new Checks();
            string server = $"127.0.0.1:{RelayPort}";
            TestContext.Out.WriteLine("[HOST HANDOFF and IDLE ROOMS (0.3.13)]");
            // owner, 2026-11-02: a host's headset going to sleep must not end everyone's room
            var hs = new List<Node>();
            string[] hn = { "OLD HOST", "HEIR ONE", "HEIR TWO", "GRIEFER" };
            for (int i = 0; i < 4; ++i) hs.Add(new Node(hn[i], -1f + i));
            all.AddRange(hs);
            foreach (var n in hs) n.net.GoOnline(server);
            RoomTests.Pump(hs, 1.0);
            Node H = hs[0], A = hs[1], B = hs[2];
            H.game.tun.advanced = false; H.game.tun.meetTime = 0;
            H.net.Host();
            RoomTests.Pump(hs, 1.0);
            uint hip = 0;
            foreach (var lg in A.net.Games()) if (lg.name == "OLD HOST") hip = lg.ip;
            A.net.Join(hip, false);   // (A first: the longest in the room after the host, so next in line)
            RoomTests.Pump(hs, 1.0);
            B.net.Join(hip, false);
            RoomTests.Pump(hs, 1.0);
            hs[3].net.Join(hip, false);   // (someone the host removes: they stay out under the next host too)
            RoomTests.Pump(hs, 1.0);
            t.C(H.net.HostKick(hs[3].net.Session), "the host removes someone");
            RoomTests.Pump(hs, 1.0);
            hs[3].net.GoOffline(); hs[3].net.Shutdown();
            all.Remove(hs[3]);
            hs.RemoveAt(3);
            A.net.Queue(true);
            RoomTests.Pump(hs, 0.5);
            B.net.Queue(true);
            for (int i = 0; i < 40 && H.game.match != MatchState.InProgress; ++i) RoomTests.Pump(hs, 0.25);
            H.game.pl[0].score = 3; H.game.pl[1].score = 2;
            RoomTests.Pump(hs, 0.5);
            float clock = H.game.roundClock;
            t.C(H.game.match == MatchState.InProgress && H.net.Room.slot[0] == A.net.Session && H.net.Room.slot[1] == B.net.Session,
                "two players in a match, the host waiting outside");
            RoomTests.Pump(hs, 2.0);   // (the shot clock has been running a while)
            t.C(H.game.pl[0].shotClock < H.game.tun.shotClockSeconds - 1.5f && Math.Abs(B.game.pl[0].shotClock - H.game.pl[0].shotClock) < 0.5f,
                "the shot clock runs, and every player sees it as the host does (the ring round the launchpad, 0.3.14)");
            // the host's headset goes to sleep: no frames from it at all
            var two = new List<Node> { A, B };
            RoomTests.Pump(two, 8.0);
            t.C(A.net.CurrentMode == Online.Net.Mode.Host && A.net.Inherited && B.net.CurrentMode == Online.Net.Mode.Client && B.net.HostSession == A.net.Session,
                "the host goes quiet: the longest in the room takes it over, the other follows");
            t.C(A.net.Room.slot[0] == A.net.Session && A.net.Room.slot[1] == B.net.Session && B.net.LocalSlot() == 1,
                "and everyone keeps their place in the tank");
            t.C(A.game.pl[0].score == 3 && A.game.pl[1].score == 2 && B.game.pl[0].score == 3 && A.game.roundClock <= clock && A.game.roundClock > clock - 15f,
                "the match picks up where it was: the score and the clock");
            for (int i = 0; i < 24 && (A.game.match != MatchState.InProgress || B.game.match != MatchState.InProgress); ++i) RoomTests.Pump(two, 0.25);   // (the player sees it a snapshot later)
            t.C(A.game.match == MatchState.InProgress && B.game.match == MatchState.InProgress, "after a countdown it plays on");
            bool named = false;
            foreach (var lg in B.net.Games()) named |= lg.name == "OLD HOST" && lg.continues == hip;
            t.C(named, "the room keeps its name, and says which host it came from (spectators follow it)");
            // MAKE HOST: the new host hands the room on mid-match; it stays in its place
            A.game.pl[0].score = 4;
            RoomTests.Pump(two, 0.3);
            t.C(A.net.HostHandOff(B.net.Session, A.game), "the host makes someone else the host");
            RoomTests.Pump(two, 3.0);
            t.C(B.net.CurrentMode == Online.Net.Mode.Host && A.net.CurrentMode == Online.Net.Mode.Client && A.net.HostSession == B.net.Session && !A.net.HandingOver,
                "MAKE HOST: the room changes hands at once");
            t.C(B.game.pl[0].score == 4 && B.net.Room.slot[0] == A.net.Session && A.net.LocalSlot() == 0, "the old host plays on in its place, the score as it was");
            {   // the one removed comes back under the new host: still turned away
                var G = new Node("GRIEFER", 3f);
                all.Add(G);
                G.net.GoOnline(server);
                for (int i = 0; i < 10 && !G.net.OnlineReady; ++i) { RoomTests.Pump(two, 0.1); Exchange(G); }
                uint bip = 0;
                for (int i = 0; i < 20 && bip == 0; ++i)
                {
                    RoomTests.Pump(two, 0.1); Exchange(G);
                    foreach (var lg in G.net.Games()) if (lg.session == B.net.Session) bip = lg.ip;
                }
                G.net.Join(bip, false);
                for (int i = 0; i < 15; ++i) { RoomTests.Pump(two, 0.1); Exchange(G); }
                t.C(bip != 0 && !G.net.InRoom, "someone the old host removed can't come back under the new one");
                G.net.GoOffline(); G.net.Shutdown();
                all.Remove(G);
            }
            // the old host wakes up: its room went on without it; it finds where it went (its beacons) and rejoins
            RoomTests.Pump(hs, 6.0);
            t.C(H.net.CurrentMode == Online.Net.Mode.Client && H.net.HostSession == B.net.Session && !H.net.HandingOver && H.net.LocalWaiting(),
                "the old host wakes up and rejoins the room, waiting outside the tank");
            // LEAVE GAME with people still in the room: it goes on with the next in line
            B.net.Leave();
            RoomTests.Pump(hs, 3.0);
            t.C(A.net.CurrentMode == Online.Net.Mode.Host && H.net.CurrentMode == Online.Net.Mode.Client && H.net.HostSession == A.net.Session,
                "a host leaving hands the room to the next in line rather than closing it");
            // a player's own Wi-Fi drops for 7 s: they take the room over, but nobody follows and the host is still there:
            // back to the real room (no copy of it under the same name)
            H.net.Deafen(7.0);
            RoomTests.Pump(hs, 7.0);
            bool split = H.net.CurrentMode == Online.Net.Mode.Host;
            RoomTests.Pump(hs, 5.0);
            t.C(split && H.net.CurrentMode == Online.Net.Mode.Client && H.net.HostSession == A.net.Session && !H.net.HandingOver && A.net.CurrentMode == Online.Net.Mode.Host,
                "a player whose own connection dropped takes the room over, finds the host still there, and goes back to it");
            {   // a player's headset sleeps 15 s while the host plays on (native 8f1fbdc; the owner's session, 2026-11-14: four
                // times one back from sleep took the room over for a second): on waking it asks the host to let it back in,
                // and never hosts
                RoomTests.Pump(new List<Node> { hs[1], hs[2] }, 15.0);
                bool tookOver = false;
                for (int i = 0; i < 60; ++i) { RoomTests.Pump(hs, 0.2); tookOver |= H.net.CurrentMode == Online.Net.Mode.Host; }
                t.C(!tookOver && H.net.CurrentMode == Online.Net.Mode.Client && H.net.HostSession == A.net.Session && A.net.CurrentMode == Online.Net.Mode.Host,
                    "a player back from 15 s asleep asks the host to let them back in, and never takes the room over");
                if (tookOver || H.net.CurrentMode != Online.Net.Mode.Client)
                    TestContext.Out.WriteLine($"       (took over {tookOver}, mode {H.net.CurrentMode}, its host {H.net.HostSession:X8}, the host {A.net.Session:X8})");
            }
            // IDLE ROOMS (shortened): nobody moves (these headsets never do), so the player leaves the tank, then the room closes
            RoomTests.Still = true;
            A.local.head.p.x += 0.3f; H.local.head.p.x += 0.3f;   // (one last step each: the clocks start here)
            RoomTests.Pump(hs, 0.3);
            A.net.SetIdleTimes(2.0, 9.0);
            RoomTests.Pump(hs, 0.3);
            if (A.net.LocalSlot() < 0) A.net.Queue(true);
            RoomTests.Pump(hs, 0.3);
            RoomTests.Pump(hs, 3.0);
            t.C(A.net.CurrentMode == Online.Net.Mode.Host && A.net.LocalSlot() < 0, "a player whose headset sits still leaves the tank for the waiting area");
            RoomTests.Pump(hs, 6.0);
            t.C(A.net.CurrentMode == Online.Net.Mode.Offline && H.net.CurrentMode == Online.Net.Mode.Offline, "a room where nobody moves closes, for everyone in it");
            RoomTests.Still = false;
            t.Done(18);
        }
    }
}
