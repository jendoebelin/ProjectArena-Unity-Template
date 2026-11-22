// IDLE ROOMS count only tracked movement (task B6), ported from native 0.3.16 (22f4b61): net_probe's "hostflicker"
// role as a Unity test, through a private local relay as RoomTests (never the live server). A host's headset left
// running unattended lies still, but its tracking drops and comes back every half second, and while lost it reports
// its head and hands by the floor. Before 0.3.16 every jump to and from that made-up place counted as moving, so the
// room stayed open all day; now only tracked movement counts, so the host leaves the tank and the room closes.
// Also the spectator hub's WAITING FOR PLAYERS (task C7, the same native commit): Net.Live and the hub's line.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using ProjectArena.Online;
using ProjectArena.Sim;
using Node = ProjectArena.Tests.RoomTests.Node;

namespace ProjectArena.Tests
{
    public class IdleRoomTests
    {
        const int RelayPort = 47799, CrowdPort = 47800;

        // the hostflicker headset: still, its tracking lost every other half second, head and hands by the floor then
        static void Flicker(Node n, double t)
        {
            bool lost = ((int)(t * 2.0)) % 2 == 1;
            n.local.headTracked = !lost;
            for (int h = 0; h < 2; ++h) n.local.hand[h].tracked = !lost;
            n.local.head = new Pose(Quat.Identity, lost ? new Vec3(0, 0.05f, 0) : new Vec3(0, 1.6f, -4.2f));
            for (int h = 0; h < 2; ++h)
                n.local.hand[h].grip = new Pose(Quat.Identity, lost ? new Vec3(0, 0.02f, 0) : new Vec3(h != 0 ? 0.2f : -0.2f, 1.1f, -4.0f));
        }

        static void PumpFlicker(Node n, double seconds, Stopwatch clock)
        {
            var t0 = Stopwatch.StartNew();
            while (t0.Elapsed.TotalSeconds < seconds)
            {
                Flicker(n, clock.Elapsed.TotalSeconds);
                var @in = new[] { new PlayerInput(), new PlayerInput(), new PlayerInput(), new PlayerInput() };
                if (n.net.Exchange(n.local, @in, n.game, 1f / 90f)) { n.game.Step(1f / 90f, @in); n.net.SendState(n.game); }
                Thread.Sleep(11);
            }
        }

        [Test, Timeout(120000)]
        public void AHeadsetWhoseTrackingComesAndGoesStillLetsItsRoomClose()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_idle_test_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            var host = new Node("FLICKER", 0f);
            try
            {
                host.net.GoOnline($"127.0.0.1:{RelayPort}");
                RoomTests.Pump(new List<Node> { host }, 1.0);
                host.game.tun.advanced = false; host.game.tun.meetTime = 0;
                host.net.Host();
                host.net.BotFill = true;
                RoomTests.Pump(new List<Node> { host }, 0.5);
                host.net.SetIdleTimes(2.0, 8.0);   // (shortened, as native's probe: out of the tank after 2 s, closed after 8)
                host.net.Queue(true);
                var clock = Stopwatch.StartNew();
                PumpFlicker(host, 1.0, clock);
                bool inTank = host.net.LocalSlot() >= 0;
                PumpFlicker(host, 3.0, clock);
                bool outOfTank = host.net.CurrentMode == Online.Net.Mode.Host && host.net.LocalSlot() < 0;
                PumpFlicker(host, 8.0, clock);
                bool closed = host.net.CurrentMode == Online.Net.Mode.Offline;
                TestContext.Out.WriteLine($"in the tank {inTank}, out after 4 s {outOfTank}, room closed after 12 s {closed}");
                Assert.That(inTank, "the host queued into the tank against the bot");
                Assert.That(outOfTank, "a still headset whose tracking comes and goes leaves the tank (the jumps don't count as moving)");
                Assert.That(closed, "... and its room closes");
            }
            finally
            {
                if (host.net.Online) host.net.GoOffline();
                host.net.Shutdown();
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        [Test]
        public void TheHubSaysWaitingWhenNoGameIsGoing()
        {
            var net = new Online.Net();
            var m = new SpecMatch();
            m.game.Reset(true);
            m.game.match = MatchState.InProgress;
            Assert.That(net.Live(m), Is.False, "a room heard only through its room updates (no snapshot yet) is not a live match");
            m.stateSeen = 0;   // (a snapshot just now: the new Net's clock is still at 0)
            Assert.That(net.Live(m), Is.True, "a fresh snapshot of a match in progress is live");
            m.game.match = MatchState.WaitingForPlayers;
            Assert.That(net.Live(m), Is.False, "a room waiting for players is not a live match");
            // the hub's line under each miniature, as native drawHub
            var g = new Game();
            g.Reset(true);
            g.tun.advanced = false; g.tun.shields = false;
            StringAssert.StartsWith("SQUARE HOTDOG   0  -  0", RoomView.HubLine(g));   // (the room, TASKS.md E22: no BASIC and ADVANCED)
            g.tun.advanced = true; g.tun.shields = true; g.tun.experimental = true;
            StringAssert.StartsWith("DIAMOND BEST OF 3   0  -  0   ROUND 1", RoomView.HubLine(g));
        }
    }
}
