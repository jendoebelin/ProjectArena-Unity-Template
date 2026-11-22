// Cross-play: a native headset's networking and rules (tools/cross_play/peer.cpp, built from the native repo's net.cpp
// and game.cpp) and Unity's (Net/Net.cs, Sim/Game.cs) in the same arena room through a private local relay
// (127.0.0.1:47793; never the live server). Both ways round: a native host with a Unity visitor, and a Unity host with
// a native visitor. Needs Build/cross_play/peer.exe (tools\cross_play\build.bat) and the local relay (RoomTests).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectArena.Online;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class CrossPlayTests
    {
        const int RelayPort = 47793, CrowdPort = 47794;
        const string PeerExe = "Build/cross_play/peer.exe";

        // the native peer: commands in, its latest '@' status line out
        sealed class NativePeer : IDisposable
        {
            readonly Process p;
            readonly object mu = new object();
            string last = "", plus = "";   // its '@' line and its '@+' line (four places)
            public NativePeer(string name, string relay)
            {
                var psi = new ProcessStartInfo(Path.GetFullPath(PeerExe), $"{name} {relay}")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.EnvironmentVariables["PROJECTARENA_QUIET"] = "1";
                p = Process.Start(psi);
                p.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    if (e.Data.StartsWith("@+")) lock (mu) plus = e.Data;
                    else if (e.Data.StartsWith("@")) lock (mu) last = e.Data;
                };
                p.ErrorDataReceived += (_, e) => { };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
            }
            public void Say(string cmd) { p.StandardInput.WriteLine(cmd); p.StandardInput.Flush(); }
            string Line { get { lock (mu) return last + " " + plus; } }
            // a value after a key in the status line ("slot" -> "1"); n: which of several values
            public string Get(string key, int n = 0)
            {
                string[] w = Line.Split(' ');
                int i = Array.IndexOf(w, key);
                return i >= 0 && i + 1 + n < w.Length ? w[i + 1 + n] : "";
            }
            public int Int(string key, int n = 0) => int.TryParse(Get(key, n), out int v) ? v : -999;
            public uint U(string key, int n = 0) => uint.TryParse(Get(key, n), out uint v) ? v : 0;
            public void Dispose()
            {
                try { Say("quit"); if (!p.WaitForExit(2000)) p.Kill(); } catch (Exception) { }
            }
        }

        static List<RoomTests.Node> ns;
        static bool Wait(Func<bool> cond, double seconds)
        {
            var t = Stopwatch.StartNew();
            while (t.Elapsed.TotalSeconds < seconds)
            {
                if (cond()) return true;
                RoomTests.Pump(ns, 0.1);
            }
            return cond();
        }

        static void FistAt(RoomTests.Node n, Vec3 at, Vec3 facing)
        {   // the visible fist's front at `at`
            n.local.hand[1].trigger = 1;
            n.local.hand[1].grip.q = FromTo(new Vec3(0, 0, -1), facing);
            n.local.hand[1].grip.p = at - n.local.hand[1].grip.q.Rotate(n.game.tun.fistFront);
        }

        [Test]
        public void NativeAndUnityShareAnArena()
        {
            if (!File.Exists(PeerExe)) Assert.Ignore("no native peer (run tools\\cross_play\\build.bat)");
            string dir = Path.Combine(Path.GetTempPath(), "pa_cross_test_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            ns = new List<RoomTests.Node>();
            string server = $"127.0.0.1:{RelayPort}";
            var native = new NativePeer("NATIVE", server);
            try
            {
                var U = new RoomTests.Node("UNITY", -2.5f);
                ns.Add(U);
                PlayerInput atRest = U.local.Clone();
                U.net.GoOnline(server);
                Assert.That(Wait(() => U.net.OnlineReady && native.Int("ready") == 1, 5), "both on the relay");

                TestContext.Out.WriteLine("[a native host, a Unity visitor]");
                native.Say("host");
                native.Say("queue 1");
                Assert.That(Wait(() => U.net.Games().Any(g => g.name == "NATIVE"), 5), "Unity lists the native arena (its beacon)");
                LanGame lg = U.net.Games().First(g => g.name == "NATIVE");
                Assert.That(lg.shields && lg.matchTime == 180f && lg.knuckleMode == -1, "with its MATCH OPTIONS");
                AvatarDesc look = Avatar.PresetLook(1);
                Avatar.SetNation(ref look, 7);
                U.local.avatar = look;
                U.net.Join(lg.ip, false);
                Assert.That(Wait(() => U.net.LocalWaiting() && native.Get("members").Contains($"{U.net.Session}:{look.suit}/7"), 5),
                            "Unity waits in the native room, and the native host sees its look (suit and country)");
                Assert.That(Wait(() => U.net.Room.NameOf(native.U("session")) == "NATIVE" && U.net.Room.NameOf(U.net.Session) == "UNITY", 3),
                            "the names go both ways (ROOM)");
                U.net.Queue(true);
                Assert.That(Wait(() => U.net.LocalSlot() == 1 && native.U("room", 1) == U.net.Session, 5), "Unity's JOIN QUEUE: slot 1 in the native tank");
                Assert.That(Wait(() => U.game.match == MatchState.Meet && native.Int("match") == (int)MatchState.Meet, 3),
                            "both meet in the centre");
                Assert.That(U.game.pl[0].@in.avatar.suit == Avatar.PresetLook(10).suit, "Unity sees the native player's look (STATE)");
                native.Say("fist -0.01 1");
                FistAt(U, new Vec3(0.01f, 1.2f, 0f), new Vec3(-1, 0, 0));
                Assert.That(Wait(() => native.Int("match") == (int)MatchState.Countdown || native.Int("match") == (int)MatchState.InProgress, 4),
                            "the native host sees the Unity fist bump (INPUT) and starts the countdown");
                Assert.That(Wait(() => U.game.match == (MatchState)native.Int("match"), 2), "Unity's game follows the native host");
                Assert.That(Wait(() => U.game.match == MatchState.InProgress && U.game.pl[0].ball.state != BallState.Dead, 6),
                            "the match runs: Unity sees the native host's balls");
                U.net.Leave();
                native.Say("rest");
                Assert.That(Wait(() => native.U("room", 1) == 0 && native.Int("bot") == 1, 4), "Unity leaves: the native bot takes the place");

                TestContext.Out.WriteLine("[a Unity host, a native visitor]");
                native.Say("leave");
                U.local = atRest.Clone();   // (hands back at rest)
                ns[0].net.Host();
                U.net.Queue(true);
                Assert.That(Wait(() => U.net.LocalSlot() == 0 && U.net.Room.bot, 3), "Unity hosts and plays the bot");
                Assert.That(Wait(() => native.Get("games").Contains(":UNITY,"), 5), "the native game lists the Unity arena");
                native.Say("look 1");
                native.Say("join UNITY");
                Assert.That(Wait(() => U.net.Room.members.Any(m => m.id == native.U("session") && m.@in.avatar.suit == Avatar.PresetLook(1).suit &&
                                                                  Avatar.NationOf(m.@in.avatar) == 7), 5),
                            "the native visitor waits in the Unity room with its look");
                native.Say("queue 1");
                Assert.That(Wait(() => U.net.Room.slot[1] == native.U("session") && native.Int("slot") == 1, 5), "and queues into the Unity tank");
                Assert.That(Wait(() => U.game.match == MatchState.Meet && native.Int("match") == (int)MatchState.Meet, 3), "both meet in the centre");
                FistAt(U, new Vec3(-0.01f, 1.2f, 0f), new Vec3(1, 0, 0));
                native.Say("fist 0.01 -1");
                Assert.That(Wait(() => U.game.match == MatchState.Countdown || U.game.match == MatchState.InProgress, 4),
                            "the Unity host sees the native fist bump");
                Assert.That(Wait(() => native.Int("match") == (int)U.game.match, 2), "the native game follows the Unity host");
                Assert.That(Wait(() => native.Int("match") == (int)MatchState.InProgress && native.Int("events") > 0, 6),
                            "the match runs on the native side, with the host's events");
                native.Say("leave");
                Assert.That(Wait(() => U.net.Room.slot[1] == 0 && U.net.Room.bot, 4), "the native player leaves: the Unity bot takes the place");
            }
            finally
            {
                native.Dispose();
                foreach (var n in ns) { if (n.net.Online) n.net.GoOffline(); n.net.Shutdown(); }
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        // a room for four (task B2): a Unity host with three Unity players and a native one, then a native host with
        // a Unity player and the native bots
        [Test, Timeout(300000)]
        public void NativeAndUnityShareARoomForFour()
        {
            if (!File.Exists(PeerExe)) Assert.Ignore("no native peer (run tools\\cross_play\\build.bat)");
            string dir = Path.Combine(Path.GetTempPath(), "pa_cross4_test_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            ns = new List<RoomTests.Node>();
            string server = $"127.0.0.1:{RelayPort}";
            var native = new NativePeer("NATIVE", server);
            bool shown = Online.Net.NewRoomsShown;
            Online.Net.NewRoomsShown = true;   // (the lobby lists rooms for four once their looks are in: task C3)
            try
            {
                var H = new RoomTests.Node("UNITY", -2.5f);
                var V1 = new RoomTests.Node("VISITOR ONE", -1.5f);
                var V2 = new RoomTests.Node("VISITOR TWO", -0.5f);
                ns.AddRange(new[] { H, V1, V2 });
                foreach (var n in ns) n.net.GoOnline(server);
                Assert.That(Wait(() => ns.All(n => n.net.OnlineReady) && native.Int("ready") == 1, 5), "everyone on the relay");

                TestContext.Out.WriteLine("[a Unity host of FOUR CORNERS (2 v 2): three Unity players and a native one]");
                H.game.tun.experimental = true; H.game.tun.expRoom = Room.Corners; H.game.tun.players = 4; H.game.tun.teamMode = Teams.TwoVTwo;
                H.net.Host();
                H.net.Queue(true);
                Assert.That(Wait(() => V1.net.Games().Any(g => g.name == "UNITY" && g.players == 4) && native.Get("games").Contains(":UNITY,"), 5),
                            "Unity and native both list the Unity room for four");
                uint hip = V1.net.Games().First(g => g.name == "UNITY").ip;
                V1.net.Join(hip, false); V2.net.Join(hip, false);
                native.Say("join UNITY");
                Assert.That(Wait(() => V1.net.LocalWaiting() && V2.net.LocalWaiting() && H.net.Room.members.Any(m => m.id == native.U("session")), 5),
                            "the visitors wait in the room, the native one too");
                V1.net.Queue(true); V2.net.Queue(true);
                native.Say("queue 1");
                Assert.That(Wait(() => native.Int("slot") >= 0 && native.Int("np") == 4 && V1.net.LocalSlot() >= 0 && V2.net.LocalSlot() >= 0, 6),
                            "all four queue into the tank; the native game plays four players");
                bool same = true;
                for (int p = 0; p < 4; ++p) same &= native.U("slots", p) == H.net.Room.slot[p] && H.net.Room.slot[p] != 0;
                Assert.That(same && native.Int("botmask") == 0, "the native player sees the same four corners, no bots");
                int ns0 = native.Int("slot"), other = (ns0 + 1) % 4;
                H.net.HostSwap(ns0, other, H.game);
                Assert.That(Wait(() => native.Int("slot") == other, 4), "MOVE PLAYERS: the Unity host moves the native player to another corner");
                Assert.That(Wait(() => H.game.match == MatchState.WaitingForPlayers && H.game.tun.readyUp, 3), "the match waits for everyone's READY");
                foreach (var n in ns) n.local.buttonA = true;
                native.Say("press 1");
                Assert.That(Wait(() => H.game.match == MatchState.Countdown || H.game.match == MatchState.InProgress, 4),
                            "everyone READY, the native player too: the countdown");
                foreach (var n in ns) n.local.buttonA = false;
                native.Say("press 0");
                Assert.That(Wait(() => H.game.match == MatchState.InProgress && native.Int("match") == (int)MatchState.InProgress, 6),
                            "the match runs, on the native side too");
                H.game.matchWinner = 0;
                H.game.match = MatchState.MatchSummary;
                Assert.That(Wait(() => native.Int("slot") == -1 && V1.net.LocalWaiting() && V2.net.LocalWaiting() && H.net.LocalWaiting(), 10),
                            "after the match everyone goes back to the waiting area, the native player too");

                TestContext.Out.WriteLine("[a native host of FOUR CORNERS (2 v 2): a Unity player and the native bots]");
                native.Say("leave");
                foreach (var n in ns) n.net.Leave();
                native.Say("four 1");
                native.Say("host");
                native.Say("queue 1");
                Assert.That(Wait(() => V1.net.Games().Any(g => g.name == "NATIVE" && g.players == 4 && g.experimental && g.expRoom == Room.Corners), 5),
                            "Unity lists the native room for four");
                V1.net.Join(V1.net.Games().First(g => g.name == "NATIVE").ip, false);
                Assert.That(Wait(() => V1.net.LocalWaiting(), 5), "the Unity player waits in the native room");
                V1.net.Queue(true);
                Assert.That(Wait(() => V1.net.LocalSlot() >= 0 && V1.game.np == 4 && native.Int("botmask") != 0, 6),
                            "and queues into the native tank: a Unity game with four players");
                Assert.That(Wait(() => V1.net.Room.botMask == native.Int("botmask"), 2), "both agree which corners the native bots play");
                bool bots = true;
                for (int p = 0; p < 4; ++p) bots &= V1.game.pl[p].isBot == V1.net.Room.IsBot(p);
                Assert.That(bots && V1.game.tun.teamMode == Teams.TwoVTwo, "the Unity game takes the native host's rules and bots");
                Assert.That(Wait(() => V1.game.pl[native.Int("slot")].@in.headTracked, 3), "and sees the native host's player");
            }
            finally
            {
                Online.Net.NewRoomsShown = shown;
                native.Dispose();
                foreach (var n in ns) { if (n.net.Online) n.net.GoOffline(); n.net.Shutdown(); }
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }
    }
}
