// The native arena room test (app/tests/test_rooms.cpp, protocol v8; the later sections: RoomsForFourTests and
// HostHandoffTests) against Net/Net.cs: one host, six visitors and
// a spectator, each a real Net, talking through a private local copy of the community relay (the native repo's
// server/projectarena_relay.py on 127.0.0.1:47791, started and stopped by the test; never the live server). The 76
// native checks with their messages, native 0.3.13's ROOM TALK: PRIVATE (2), and native run_room_test.py's check of
// the relay's log (each game's own version, 0.3.11). Needs Python 3.8+ (the "py" launcher) and the native repo (FindNativeRepo:
// PA_NATIVE_REPO, native_repo.local.cfg, or the public repository beside this one); the test is skipped without them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using ProjectArena.Online;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class RoomTests
    {
        public const int RelayPort = 47791, CrowdPort = 47792;
        public static string NativeRepo => FindNativeRepo();
        // the native repository (its server, server/projectarena_relay.py): the environment variable PA_NATIVE_REPO, or a
        // line PA_NATIVE_REPO=<folder> in native_repo.local.cfg (not in git), or the public repository cloned beside this
        // one (../ProjectArena-public or ../ProjectArena); "" when none is found
        public static string FindNativeRepo()
        {
            string v = Environment.GetEnvironmentVariable("PA_NATIVE_REPO");
            if (!string.IsNullOrEmpty(v)) return v;
            try
            {
                if (File.Exists("native_repo.local.cfg"))
                    foreach (string line in File.ReadAllLines("native_repo.local.cfg"))
                        if (line.Trim().StartsWith("PA_NATIVE_REPO=")) return line.Trim().Substring("PA_NATIVE_REPO=".Length).Trim();
            }
            catch (Exception) { }
            foreach (string d in new[] { "../ProjectArena-public", "../ProjectArena" })
                if (File.Exists(Path.Combine(d, "server", "projectarena_relay.py"))) return Path.GetFullPath(d);
            return "";
        }

        sealed class Checks
        {
            readonly List<string> failed = new List<string>();
            public int count;
            public void C(bool c, string msg)
            {
                ++count;
                if (!c) failed.Add(msg);
                TestContext.Out.WriteLine((c ? "  ok   " : "  FAIL ") + msg);
            }
            public void Group(string g) => TestContext.Out.WriteLine($"[{g}]");
            public void Done(int expected)
            {
                Assert.That(failed, Is.Empty, "failed: " + string.Join("; ", failed));
                Assert.That(count, Is.EqualTo(expected), "checks run (the native test has " + expected + ")");
            }
        }

        public class Node
        {
            public Online.Net net = new Online.Net();
            public Game game = new Game();
            public PlayerInput local = new PlayerInput();
            public Node(string name, float x)
            {
                net.Init();
                net.SetAppVersion("0.3.11");   // (the relay logs it)
                net.SetName(name);
                game.Reset(true);
                local.headTracked = true;
                local.head = new Pose(Quat.Identity, new Vec3(x, 1.6f, -4.2f));
                for (int h = 0; h < 2; ++h) { local.hand[h].tracked = true; local.hand[h].grip = new Pose(Quat.Identity, new Vec3(x + (h != 0 ? 0.2f : -0.2f), 1.1f, -4.0f)); }
            }
        }

        // IDLE ROOMS (native 0.3.13): everyone's head bobs a little, as a person's does, unless Still is set
        public static bool Still;
        static readonly Stopwatch bob = Stopwatch.StartNew();

        public static void Pump(List<Node> ns, double seconds)
        {
            var t0 = Stopwatch.StartNew();
            for (;;)
            {
                foreach (var n in ns)
                {
                    if (!Still) n.local.head.p.y = 1.6f + 0.04f * MathF.Sin((float)(bob.Elapsed.TotalSeconds * 1.3));
                    var @in = new[] { new PlayerInput(), new PlayerInput(), new PlayerInput(), new PlayerInput() };
                    if (n.net.Exchange(n.local, @in, n.game, 1f / 90f)) { n.game.Step(1f / 90f, @in); n.net.SendState(n.game); }
                }
                Thread.Sleep(11);
                if (t0.Elapsed.TotalSeconds > seconds) break;
            }
        }

        // what the latest private relay printed (its log: joins, leaves), one line each
        static readonly List<string> relayLog = new List<string>();
        public static List<string> RelayLog() { lock (relayLog) return new List<string>(relayLog); }

        // a private relay for the test: (python, args) or null when Python 3.8+ or the native repo is missing
        public static Process StartRelay(string dir, int port, int crowdPort)
        {
            string relay = Path.Combine(NativeRepo, "server", "projectarena_relay.py");
            if (!File.Exists(relay)) return null;
            Directory.CreateDirectory(dir);
            string latest = Path.Combine(dir, "latest_version.txt");
            File.WriteAllText(latest, "9.9.9");
            lock (relayLog) relayLog.Clear();
            foreach (string py in new[] { "-3.12", "-3.13", "-3.10", "-3.9" })
            {   // (-u: unbuffered, so its log arrives as it happens)
                var psi = new ProcessStartInfo("py", $"{py} -u -I \"{relay}\" --port {port} --bind 127.0.0.1 --latest \"{latest}\" " +
                                                     $"--stats \"{Path.Combine(dir, "stats.db")}\" --reports \"{Path.Combine(dir, "reports")}\" " +
                                                     $"--bans \"{Path.Combine(dir, "bans.txt")}\" --crowd 127.0.0.1:{crowdPort}")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir };
                try
                {
                    var p = Process.Start(psi);
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (relayLog) relayLog.Add(e.Data); };
                    p.ErrorDataReceived += (_, e) => { };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    Thread.Sleep(1200);
                    if (!p.HasExited) return p;
                }
                catch (Exception) { }
            }
            return null;
        }

        static int WaitingCount(RoomInfo r) => r.members.Count;

        // The Crowd: a phone on the web page, as far as the relay can tell (a WebSocket client: masked text frames)
        sealed class Fan : IDisposable
        {
            TcpClient c;
            NetworkStream s;
            public string got = "";
            public bool Open(int port)
            {
                try
                {
                    c = new TcpClient("127.0.0.1", port);
                    s = c.GetStream();
                    s.ReadTimeout = 100;
                    byte[] req = Encoding.ASCII.GetBytes("GET /ws HTTP/1.1\r\nHost: crowd\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                                                         "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");
                    s.Write(req, 0, req.Length);
                    var head = new StringBuilder();
                    for (int i = 0; i < 4000 && !head.ToString().Contains("\r\n\r\n"); ++i)
                    {
                        try { int b = s.ReadByte(); if (b >= 0) head.Append((char)b); } catch (IOException) { }
                    }
                    return head.ToString().Contains(" 101 ");
                }
                catch (Exception) { return false; }
            }
            public void Say(string text)
            {
                var f = new List<byte> { 0x81, (byte)(0x80 | text.Length) };
                byte[] mask = { 0x11, 0x22, 0x33, 0x44 };
                f.AddRange(mask);
                for (int i = 0; i < text.Length; ++i) f.Add((byte)(text[i] ^ mask[i & 3]));
                s.Write(f.ToArray(), 0, f.Count);
            }
            public void Read()
            {   // whatever has arrived (frames from the server are unmasked)
                var buf = new List<byte>();
                var tmp = new byte[4096];
                for (;;)
                {
                    int n;
                    try { if (!c.Client.Poll(0, SelectMode.SelectRead)) break; n = s.Read(tmp, 0, tmp.Length); }
                    catch (IOException) { break; }
                    if (n <= 0) break;
                    for (int i = 0; i < n; ++i) buf.Add(tmp[i]);
                }
                int k = 0;
                while (k + 2 <= buf.Count)
                {
                    int n = buf[k + 1] & 0x7F, off = 2;
                    if (n == 126) { if (k + 4 > buf.Count) break; n = (buf[k + 2] << 8) | buf[k + 3]; off = 4; }
                    if (k + off + n > buf.Count) break;
                    got += Encoding.ASCII.GetString(buf.GetRange(k + off, n).ToArray()) + "\n";
                    k += off + n;
                }
            }
            public void Dispose() { c?.Close(); }
        }

        [Test]
        public void ArenaRoomsThroughTheRelay()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_room_test_" + Guid.NewGuid().ToString("N"));
            Process relay = StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            var ns = new List<Node>();
            try { Run(ns, relay); }
            finally
            {
                foreach (var n in ns) { if (n.net.Online) n.net.GoOffline(); n.net.Shutdown(); }
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        static void Run(List<Node> ns, Process relay)
        {
            var t = new Checks();
            string server = $"127.0.0.1:{RelayPort}";
            string[] names = { "HOST", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "WATCHER" };
            for (int i = 0; i < 8; ++i) ns.Add(new Node(names[i], -4.5f + i));
            foreach (var n in ns) n.net.GoOnline(server);
            Pump(ns, 1.0);
            bool ready = true;
            foreach (var n in ns) ready &= n.net.OnlineReady;
            t.C(ready, "everyone is connected to the relay");
            t.C(ns[0].net.LatestVersion == "9.9.9", "the relay's welcome says the current release (UPDATE AVAILABLE)");
            Node H = ns[0];
            H.net.Host();
            Pump(ns, 0.3);
            t.Group("host");
            t.C(H.net.LocalWaiting() && H.net.Room.slot[0] == 0, "the host starts in the room's waiting area, not in the tank");
            H.net.Queue(true);
            Pump(ns, 0.5);
            t.C(H.net.Room.slot[0] == H.net.Session && H.net.Room.bot, "the host's JOIN QUEUE: into the tank against the bot");
            for (int k = 0; k < 40 && ns[1].net.Games().Count == 0; ++k) Pump(ns, 0.1);
            t.Group("join");
            if (ns[1].net.Games().Count == 0) { Assert.Fail("no beacon from the host"); return; }
            uint hostIp = ns[1].net.Games()[0].ip;
            ns[1].game.pl[0].isBot = ns[1].game.pl[1].isBot = true;   // as if coming from the lobby's two bots
            ns[1].net.Join(hostIp);
            Pump(ns, 1.0);
            t.C(ns[1].net.LocalWaiting() && H.net.Room.slot[1] == 0, "a visitor (even asking to play now) arrives in the waiting area");
            ns[1].net.Queue(true);
            Pump(ns, 1.0);
            t.C(H.net.Room.slot[0] == H.net.Session && H.net.Room.slot[1] == ns[1].net.Session, "their JOIN QUEUE takes the bot's place in the tank");
            t.C(ns[1].net.LocalSlot() == 1, "the visitor knows it plays slot 1");
            t.Group("ping between the host and a player");
            Pump(ns, 2.5);
            t.C(ns[1].net.PingMs >= 0f && ns[1].net.PingMs < 250f, "the player measures its round trip to the host (PING / PONG)");
            t.C(H.net.PingMs >= 0f && H.net.PingMs < 250f, "and the host its round trip to the player");
            TestContext.Out.WriteLine($"    ping: player -> host {ns[1].net.PingMs:F1} ms, host -> player {H.net.PingMs:F1} ms (local relay)");
            t.Group("player statistics on the server");
            {
                var k0 = new byte[16]; var k1 = new byte[16];
                for (int i = 0; i < 16; ++i) { k0[i] = 0x11; k1[i] = 0x22; }
                H.net.SetStatsKey(k0); ns[1].net.SetStatsKey(k1);
                Pump(ns, 3.5);   // the keys go to the relay; the host names its tank (every 3 s)
                var st = new MatchTally[2];
                st[0].throws = 10; st[0].onTarget = 6; st[0].hits = 3; st[0].blocks = 2; st[0].faced = 5; st[0].catches = 1; st[0].topSpeed = 22.5f;
                st[1].throws = 8; st[1].onTarget = 4; st[1].hits = 2; st[1].blocks = 1; st[1].faced = 4; st[1].topSpeed = 19f;
                byte[] score = { 7, 4, 0, 0 };
                H.net.ReportMatch(0, st, score);
                ns[1].net.ConfirmMatch(0, score);   // the other player's game agrees (the host's result alone counts for nobody)
                Pump(ns, 0.4);
                Node W = ns[7];
                uint hv = H.net.RoomHostVip();
                W.net.RequestStats(hv);
                Pump(ns, 0.5);
                var c = new Online.Net.CareerStats[2];
                bool got = W.net.StatsFor(hv, c);
                t.C(got && c[0].known && c[0].matches == 1 && c[0].wins == 1 && c[0].throws == 10 && c[0].onTarget == 6 &&
                    c[1].matches == 1 && c[1].wins == 0 && c[1].hits == 2, "a reported match reaches the players' careers, and anyone can read them");
                t.C(got && Math.Abs(c[0].topSpeed - 22.5f) < 0.02f && c[0].name == "HOST" && c[1].name == "ONE", "with the top speed and the names");
                t.C(ns[1].net.RoomHostVip() == hv, "a player in the room asks for the same host's players");
                ns[1].net.RequestMyStats();   // MENU > MY STATS
                Pump(ns, 0.4);
                var me = new Online.Net.CareerStats[2];
                t.C(ns[1].net.StatsFor(Online.Net.kMyStats, me) && me[0].matches == 1 && me[0].hits == 2 && me[0].name == "ONE",
                    "a player reads their own career (MY STATS)");
                ns[1].net.DeleteMyStats();
                Pump(ns, 0.7);
                ns[1].net.RequestMyStats();
                Pump(ns, 0.4);
                t.C(ns[1].net.StatsFor(Online.Net.kMyStats, me) && me[0].matches == 0, "DELETE MY STATS erases them on the server");
            }
            t.Group("MUTED PLAYERS' codes");   // (TASKS.md E46, native 43397af; the relay's MUTECODE)
            {
                uint sH = H.net.Session, s1 = ns[1].net.Session;
                for (int i = 0; i < 3; ++i) { H.net.AskMuteCodes(new[] { s1 }); ns[1].net.AskMuteCodes(new[] { sH }); Pump(ns, 0.3); }
                ulong hOf1 = H.net.MuteCode(s1), oneOfH = ns[1].net.MuteCode(sH);
                t.C(hOf1 != 0 && oneOfH != 0, "the server's code for the other player, each asking (both have a statistics key)");
                t.C(hOf1 != oneOfH, "each asker its own code for the same pair: nobody can follow a player across names");
                t.C(H.net.MuteCode(ns[2].net.Session) == 0, "someone not met (no address yet): no code");
            }
            t.Group("fist bump over the network");
            {
                Node V = ns[1];
                t.C(H.game.match == MatchState.Meet, "two people in the tank: they meet in the centre");
                t.C(!V.game.pl[0].isBot && !V.game.pl[1].isBot,
                    "the visitor (who came from practice / the lobby) does not take the host for a bot (it flickered)");
                void FistAt(Node n, Vec3 at, Vec3 facing)
                {   // the visible fist's front at `at`
                    n.local.hand[1].trigger = 1;
                    n.local.hand[1].grip.q = FromTo(new Vec3(0, 0, -1), facing);
                    n.local.hand[1].grip.p = at - n.local.hand[1].grip.q.Rotate(H.game.tun.fistFront);
                }
                PlayerInput hostKeep = H.local.Clone(), visKeep = V.local.Clone();
                FistAt(H, new Vec3(-0.01f, 1.2f, 0f), new Vec3(1, 0, 0));
                FistAt(V, new Vec3(0.01f, 1.2f, 0f), new Vec3(-1, 0, 0));
                Pump(ns, 2.0);   // (offered 0.2 s, the bump's 0.8 s confirmation, then the countdown)
                t.C(H.game.match == MatchState.Countdown || H.game.match == MatchState.InProgress,
                    "the host sees the bump and starts the countdown well before the 10 s fallback");
                t.C(V.game.match == H.game.match, "the visitor's game follows");
                H.local = hostKeep; V.local = visKeep;
            }
            t.Group("COUNTRY KIT over the network");   // (TASKS.md E34, native 485beac test_rooms.cpp)
            {
                Node V = ns[1];
                AvatarDesc mine = Avatar.PresetLook(2);   // VANGUARD, in CANADA's colours and flags (MY OUTFIT)
                Avatar.Set(ref mine, Avatar.Field.Nation, 2);
                Avatar.SetOwnKit(ref mine, true);
                AvatarDesc jersey = Avatar.PresetLook(3);   // the host: NIGHT RIDER under CANADA's team jersey
                Avatar.Set(ref jersey, Avatar.Field.Nation, 2);
                PlayerInput hostKeep = H.local.Clone(), visKeep = V.local.Clone();
                V.local.avatar = Avatar.SentLook(mine);
                H.local.avatar = Avatar.SentLook(jersey);
                Pump(ns, 0.6);
                AvatarDesc atHost = H.game.pl[1].@in.avatar;
                t.C(Avatar.OwnKit(atHost) && Avatar.Uniform(atHost).suit == 2 && Avatar.NationOf(atHost) == 2,
                    "a player's own outfit with their country (MY OUTFIT) reaches the host");
                AvatarDesc atVisitor = V.game.pl[0].@in.avatar;
                t.C(!Avatar.OwnKit(atVisitor) && Avatar.Uniform(atVisitor).suit == 1 && Avatar.NationOf(atVisitor) == 2,
                    "and the host's team jersey reaches the player as the team jersey");
                V.local.avatar = Avatar.SentLook(jersey);
                V.local.avatar.pattern = (byte)(V.local.avatar.pattern | Avatar.kOwnKitBit);
                Avatar.SetNation(ref V.local.avatar, 0);   // (the bit with no country, which 0.3.22 never sends: cleared on arrival)
                Pump(ns, 0.4);
                t.C(!Avatar.OwnKit(H.game.pl[1].@in.avatar) && H.game.pl[1].@in.avatar.pattern < 0x80, "without a country the bit is cleared");
                H.local = hostKeep; V.local = visKeep;
                Pump(ns, 0.2);
            }
            for (int i = 2; i <= 6; ++i) ns[i].net.Join(hostIp, false);   // JOIN ROOM: wait outside
            Pump(ns, 1.5);
            t.C(WaitingCount(H.net.Room) == 4, "four more wait outside the tank");
            t.C(ns[6].net.CurrentMode == Online.Net.Mode.Offline, "a seventh person is refused (arena full)");
            var spots = new bool[4];
            bool allWaiting = true;
            for (int i = 2; i <= 5; ++i) { allWaiting &= ns[i].net.LocalWaiting(); spots[ns[i].net.LocalSpot() & 3] = true; }
            t.C(allWaiting && spots[0] && spots[1] && spots[2] && spots[3], "each waiting player has their own holding spot");
            t.C(WaitingCount(ns[2].net.Room) == 4 && ns[2].net.Room.members[0].@in.avatar.suit == ns[2].net.Room.members[0].@in.avatar.suit,
                "waiting players see each other (poses and looks in ROOM)");
            t.Group("queue");
            ns[2].net.Queue(true); Pump(ns, 0.3);
            ns[3].net.Queue(true); Pump(ns, 0.3);
            ns[4].net.Queue(true); Pump(ns, 0.5);
            t.C(ns[2].net.LocalQueuePos() == 0 && ns[3].net.LocalQueuePos() == 1, "two join the queue in order");
            t.C(ns[4].net.LocalQueuePos() < 0 && ns[4].net.CurrentMode == Online.Net.Mode.Client, "a third is not queued (max 2) but stays in the room");
            t.Group("spectator presence");
            Node S = ns[7];
            S.net.SetPresence(1, new Vec3(0, 3, 6), hostIp);
            S.net.Spectate();
            Pump(ns, 1.0);
            bool seen = false;
            foreach (var w in H.net.Room.watchers) seen |= w.id == S.net.Session && w.where == 1;
            t.C(seen, "the host lists the spectator near its arena (blue marker)");
            t.C(S.net.Matches.Count > 0 && WaitingCount(S.net.Matches[0].room) == 4, "the spectator sees the waiting players (red markers)");
            t.Group("names: report / mute a player");
            {
                bool Has(List<Online.Net.Person> v, string nm, byte role) { foreach (var p in v) if (p.name == nm && p.role == role) return true; return false; }
                string n2 = H.net.Room.NameOf(ns[2].net.Session);
                t.C(n2 == "TWO", "the host knows a visitor's name from their JOIN");
                var two = ns[2].net.PeopleHere();
                bool self = false;
                foreach (var p in two) self |= p.id == ns[2].net.Session;
                t.C(Has(two, "HOST", Online.Net.VOICE_PLAYER) && Has(two, "ONE", Online.Net.VOICE_PLAYER) && Has(two, "THREE", Online.Net.VOICE_WAITING),
                    "a waiting player sees everyone's names and roles in ROOM");
                t.C(Has(two, "WATCHER", Online.Net.VOICE_SPECTATOR) && !self, "... the spectator at the arena too, but not themselves");
                t.C(Has(S.net.PeopleHere(), "TWO", Online.Net.VOICE_WAITING) && Has(S.net.PeopleHere(), "HOST", Online.Net.VOICE_PLAYER),
                    "a spectator at the arena sees the room's names");
                t.C(H.net.HereName() == "HOST" && ns[2].net.HereName() == "HOST", "everyone knows the room's name");
            }
            t.Group("MUTED PLAYERS: a code for everyone here (0.3.24)");   // (native 3fccaf9: the host's ROOM says whose address is whose)
            {
                Node A = ns[2], B = ns[3];   // TWO and THREE, waiting
                var kA = new byte[16]; var kB = new byte[16]; var kS = new byte[16];
                for (int i = 0; i < 16; ++i) { kA[i] = 0x33; kB[i] = 0x44; kS[i] = 0x55; }
                A.net.SetStatsKey(kA); B.net.SetStatsKey(kB); S.net.SetStatsKey(kS);
                Pump(ns, 0.8);   // (the keys reach the relay; the host's ROOM carries everyone's address)
                A.net.AskMuteCodes(new[] { B.net.Session, ns[1].net.Session, S.net.Session, H.net.Session });
                S.net.AskMuteCodes(new[] { B.net.Session });
                Pump(ns, 0.8);
                ulong ab = A.net.MuteCode(B.net.Session), a1 = A.net.MuteCode(ns[1].net.Session);
                ulong aS = A.net.MuteCode(S.net.Session), ah = A.net.MuteCode(H.net.Session), sb = S.net.MuteCode(B.net.Session);
                t.C(ab != 0 && a1 != 0 && aS != 0 && ah != 0, "a waiting player gets the server's code for another waiting player, a tank player, the spectator and the host");
                t.C(sb != 0 && sb != ab, "a spectator at the arena gets one too, its own (another game's code for the same person differs)");
            }
            t.Group("rotation");
            H.game.match = MatchState.MatchSummary;   // the match ends
            Pump(ns, 7.5);
            RoomInfo r = H.net.Room;
            t.C(r.slot[0] == ns[2].net.Session && r.slot[1] == ns[3].net.Session, "both players leave; the queue enters the tank");
            t.C(H.net.LocalWaiting() && ns[1].net.LocalWaiting(), "the two who played wait outside");
            t.C(WaitingCount(r) == 4 && ns[2].net.LocalSlot() == 0 && ns[3].net.LocalSlot() == 1, "the room is consistent on every headset");
            t.C(H.game.match != MatchState.MatchSummary && !H.game.pl[1].isBot, "a new match of two humans starts");
            t.Group("leaving");
            ns[3].net.Leave();
            Pump(ns, 0.8);
            t.C(H.net.Room.slot[0] == ns[2].net.Session && H.net.Room.bot, "a player leaving mid-match: the bot takes the place");
            ns[1].net.Queue(true);
            Pump(ns, 0.8);
            t.C(H.net.Room.slot[1] == ns[1].net.Session, "someone queuing while the bot plays goes straight in");
            t.Group("forfeit");
            {
                uint was = H.net.Room.slot[1];
                Node p1 = null;
                foreach (var n in ns) if (n.net.Session == was) p1 = n;
                if (p1 != null)
                {
                    p1.net.Forfeit();
                    Pump(ns, 0.8);
                    t.C(p1.net.LocalWaiting() && H.net.Room.slot[1] != was, "FORFEIT MATCH: the player goes back to the waiting area");
                    p1.net.Queue(true);
                    Pump(ns, 0.8);
                    t.C(H.net.Room.slot[1] == was, "and can queue back in");
                }
                else t.C(false, "a player in slot 1 to forfeit");
            }
            t.Group("voice");
            {
                int maxErr = 0;
                for (int v = -32000; v <= 32000; v += 97)
                {
                    int d = Math.Abs(VoiceCodec.Decode(VoiceCodec.Encode((short)v)) - v);
                    maxErr = Math.Max(maxErr, d * 100 / Math.Max(1, Math.Abs(v) + 64));
                }
                t.C(maxErr <= 7, "mu-law round trip within a few percent");
            }
            foreach (var n in ns) n.net.VoiceInbox.Clear();
            var frame = new byte[Online.Net.kVoiceSamples];
            for (int i = 0; i < frame.Length; ++i) frame[i] = 0x55;
            Node waiter = ns[4];                      // FOUR waits outside
            waiter.net.SendVoice(frame);
            S.net.SetPresence(0, new Vec3(0, 1.6f, 0), 0);   // the spectator moves to the lobby
            Pump(ns, 0.6);
            foreach (var n in ns) n.net.VoiceInbox.Clear();
            waiter.net.SendVoice(frame);
            Pump(ns, 0.4);
            bool Heard(Node n, byte role) { foreach (var f in n.net.VoiceInbox) if (f.speaker == waiter.net.Session && f.role == role) return true; return false; }
            t.C(Heard(H, Online.Net.VOICE_WAITING) && Heard(ns[2], Online.Net.VOICE_WAITING), "a waiting player is heard by the host and the tank players");
            t.C(waiter.net.VoiceInbox.Count == 0, "nobody hears themselves");
            t.C(S.net.VoiceInbox.Count == 0, "a spectator in the lobby hears nothing from the arena");
            S.net.SetPresence(1, new Vec3(0, 3, 6), hostIp);
            Pump(ns, 0.6);
            foreach (var n in ns) n.net.VoiceInbox.Clear();
            ns[2].net.SendVoice(frame);               // TWO plays in the tank
            Pump(ns, 0.4);
            bool specHeard = false;
            foreach (var f in S.net.VoiceInbox) specHeard |= f.speaker == ns[2].net.Session && f.role == Online.Net.VOICE_PLAYER;
            t.C(specHeard, "a spectator standing at the arena hears the players");
            foreach (var n in ns) n.net.VoiceInbox.Clear();
            S.net.SendVoice(frame);
            Pump(ns, 0.4);
            bool fromSpec = false;
            foreach (var f in ns[2].net.VoiceInbox) fromSpec |= f.speaker == S.net.Session && f.role == Online.Net.VOICE_SPECTATOR;
            t.C(fromSpec, "players hear a spectator at their arena, tagged as a spectator (for MUTE SPECTATORS)");
            {   // ROOM TALK: PRIVATE (native 0.3.13, owner 2026-11-02): the spectator sees the match but doesn't hear the
                // room; the room still does
                Node host = null;
                foreach (var n in ns) if (n.net.CurrentMode == Online.Net.Mode.Host && n.net.Room.slot[0] != 0) host = n;
                t.C(host != null, "(the room's host)");
                if (host != null)
                {
                    host.net.PrivateTalk = true;
                    foreach (var n in ns) n.net.VoiceInbox.Clear();
                    ns[2].net.SendVoice(frame);
                    Pump(ns, 0.4);
                    bool specHears = false, roomHears = false;
                    foreach (var f in S.net.VoiceInbox) specHears |= f.speaker == ns[2].net.Session;
                    foreach (var f in waiter.net.VoiceInbox) roomHears |= f.speaker == ns[2].net.Session;
                    t.C(!specHears && roomHears, "ROOM TALK: PRIVATE: the spectator hears nothing; the one waiting in the room still does");
                    host.net.PrivateTalk = false;
                }
            }
            t.Group("team cameras");
            {
                Node S2 = ns[6];   // SIX (refused earlier) spectates now
                S2.net.SetPresence(0, new Vec3(1, 1.6f, 0), 0);
                S2.net.Spectate();
                Pump(ns, 1.0);
                byte[] blob = { 1, 9, 9, 9, 9, 8, 8, 8, 8, 7, 7, 7, 7, 6, 6, 6, 6, 1, 1, 2 };
                S.net.ShareCameras("ALPHA", blob);
                Pump(ns, 0.5);
                bool ok = S2.net.TakeCameras(out string team, out byte[] got) && team == "ALPHA" && Convert.ToBase64String(got) == Convert.ToBase64String(blob);
                t.C(ok, "a spectator's shared cameras reach another spectator through the arena host");
                t.C(!S.net.TakeCameras(out _, out _), "the sender does not get its own cameras back");
            }
            t.Group("play now / bot off");
            {
                Node B = ns[6];   // SIX hosts a second arena with the bot switched off
                B.net.BotFill = false;
                B.net.Host();
                Pump(ns, 0.5);
                B.net.Queue(true);
                Pump(ns, 1.0);
                t.C(!B.net.Room.bot && !B.game.pl[1].isBot && B.net.LocalSlot() == 0, "bot off: the host waits in the tank with no bot");
                uint ip2 = 0;
                foreach (var lg in ns[5].net.Games()) if (lg.name == "SIX") ip2 = lg.ip;
                t.C(ip2 != 0, "the second arena is listed");
                ns[5].net.Join(ip2, false);
                Pump(ns, 1.0);
                ns[5].net.Queue(true);         // JOIN QUEUE: the free place
                Pump(ns, 1.0);
                t.C(B.net.Room.slot[1] == ns[5].net.Session && ns[5].net.LocalSlot() == 1, "JOIN QUEUE takes the free place in the tank");
                ns[4].net.Join(ip2, false);
                Pump(ns, 1.0);
                ns[4].net.Queue(true);         // the tank is busy: queued
                Pump(ns, 1.0);
                t.C(ns[4].net.LocalWaiting() && ns[4].net.LocalQueuePos() == 0, "JOIN QUEUE into a busy tank waits first in line");
                ns[3].net.Join(ip2, false);   // JOIN ROOM: waits, not queued
                Pump(ns, 1.0);
                t.C(ns[3].net.LocalWaiting() && ns[3].net.LocalQueuePos() < 0, "JOIN ROOM waits outside without queuing");
                t.Group("vote to remove");
                uint target = ns[3].net.Session;
                t.C(!ns[4].net.VoteKick(B.net.Session), "nobody can vote the host out of its own arena");
                t.C(ns[4].net.VoteKick(target), "a waiting player votes to remove someone");
                Pump(ns, 0.6);
                t.C(ns[3].net.InRoom, "one vote of the two needed (4 people: more than half of the other 3) is not enough");
                t.C(!ns[4].net.VoteKick(target), "one vote each");
                t.C(ns[5].net.VoteKick(target), "a second person votes");
                Pump(ns, 1.0);
                bool gone = !ns[3].net.InRoom;
                foreach (var m in B.net.Room.members) gone &= m.id != target;
                t.C(gone, "two votes: removed from the arena");
                ns[3].net.Join(ip2, false);   // back again in the same session: turned away
                Pump(ns, 1.0);
                t.C(!ns[3].net.InRoom, "a removed player can't come back to that arena this session");
                t.C(ns[4].net.InRoom && ns[5].net.InRoom, "everyone else stays");
            }
            t.Group("the crowd");
            {
                Node B = ns[6];   // SIX's arena: FIVE plays, FOUR waits; WATCHER spectates
                var li = new Online.Net.LiveInfo { state = 2 };
                li.score[0] = 3; li.score[1] = 1; li.nation[0] = 1; li.nation[1] = 7;
                li.name[0] = "SIX"; li.name[1] = "FIVE";
                li.clock = 95f;
                li.st[0].throws = 20; li.st[0].onTarget = 12; li.st[0].strikes = 2; li.st[0].topSpeed = 25f; li.st[1].hits = 4;
                li.round = 2;
                B.net.SetLive(li);
                Pump(ns, 1.6);
                using (var fan = new Fan())
                {
                    bool opened = fan.Open(CrowdPort);
                    t.C(opened, "a phone connects to the relay's crowd port (WebSocket)");
                    Pump(ns, 1.2);
                    if (opened) fan.Read();
                    int at = fan.got.IndexOf("\"host\":\"SIX\"", StringComparison.Ordinal);
                    t.C(at >= 0 && fan.got.Contains("\"s\":[3,1,0,0]"), "the host's LIVE reaches the phones' list: its match and score");
                    t.C(fan.got.Contains("\"throws\":20,\"on\":12") && fan.got.Contains("\"strikes\":2") && fan.got.Contains("\"top\":90") &&
                        fan.got.Contains("\"hits\":4") && fan.got.Contains("\"rnd\":2"), "and this match so far: throws, accuracy, strikes, top speed, round");
                    string id = "";
                    int k = at >= 0 ? fan.got.LastIndexOf("\"id\":\"", at, StringComparison.Ordinal) : -1;
                    if (k >= 0) id = fan.got.Substring(k + 6, 8);
                    if (opened)
                    {
                        fan.Say("{\"t\":\"watch\",\"id\":\"" + id + "\"}");
                        for (int i = 0; i < 6; ++i)
                        {
                            fan.Say("{\"t\":\"cheer\",\"side\":0,\"n\":3}");
                            fan.Say("{\"t\":\"flag\",\"side\":1}");
                            Pump(ns, 0.25);
                        }
                    }
                    Pump(ns, 0.6);
                    CrowdInfo hc = B.net.Crowd();
                    t.C(hc != null && hc.fans == 1 && hc.hype[0] > 0 && hc.hype[1] == 0 && hc.waves[1] == 1,
                        "the host hears its fan: one fan, the cheered end's hype, a flag waved for the other end");
                    CrowdInfo pc = ns[5].net.Crowd();
                    t.C(pc != null && pc.fans == 1 && pc.hype[0] > 0, "the tank player hears the crowd too (the host passes it on)");
                    CrowdInfo wc = ns[4].net.Crowd();
                    t.C(wc != null && wc.fans == 1, "and the waiting player");
                    uint ip2 = 0;
                    foreach (var lg in S.net.Games()) if (lg.name == "SIX") ip2 = lg.ip;
                    CrowdInfo sc = S.net.Crowd(ip2);
                    t.C(sc != null && sc.fans == 1, "a spectator watching that arena gets its crowd");
                    t.C(ns[1].net.Crowd() == null || ns[1].net.Crowd().fans == 0, "another arena's players don't");
                    if (opened) fan.Read();
                    t.C(fan.got.Contains("\"t\":\"m\"") && fan.got.Contains("\"fans\":1"), "the phone follows the match it watches");
                    // FOUR PLAYERS (TASKS.md E47, native 530f6fb, 0.3.23): the 186-byte LIVE, as the relay reads it
                    li.np = 4; li.room = 2; li.teamMode = 1; li.theOne = 0;
                    string[] nm4 = { "SIX", "FIVE", "GREEN", "VIOLET" };
                    for (int p = 0; p < 4; ++p)
                    {
                        li.name4[p] = nm4[p]; li.team4[p] = (byte)(p / 2); li.points4[p] = (short)(3 - p); li.nation4[p] = (byte)(p + 1);
                        li.health4[p] = (byte)(100 - p);
                    }
                    li.rgb4[2, 0] = 0x40; li.rgb4[2, 1] = 0xf2; li.rgb4[2, 2] = 0x59;
                    li.out4[3] = 1;
                    li.st[2].throws = 7; li.st[2].catches = 2; li.st[3].topSpeed = 12f;
                    B.net.SetLive(li);
                    fan.got = "";
                    Pump(ns, 1.6);
                    if (opened) fan.Read();
                    t.C(fan.got.Contains("\"np\":4,\"room\":2,\"tm\":1,\"one\":0") &&
                        fan.got.Contains("\"n\":\"GREEN\",\"c\":\"#40f259\",\"team\":1,\"pts\":1,\"hp\":98,\"out\":false,\"nat\":3"),
                        "four players: the room, the teams and each player's colour, team, points, health and country reach the phone");
                    t.C(fan.got.Contains("\"n\":\"VIOLET\"") && fan.got.Contains("\"out\":true,\"nat\":4") &&
                        fan.got.Contains("{\"throws\":7,\"on\":0,\"hits\":0,\"blocks\":0,\"faced\":0,\"catches\":2,\"strikes\":0,\"top\":0}") &&
                        fan.got.Contains("\"top\":43}"), "... players 2 and 3's names and match counts too");
                }
            }
            t.Group("match options and the host's kick");
            {
                Node B = ns[6];   // SIX hosts; FIVE plays, FOUR waits
                B.game.tun.shields = false; B.game.tun.basicMatchTime = 300f; B.game.tun.knuckleMode = 0;
                Pump(ns, 1.5);
                Tunables tn = ns[5].game.tun;
                t.C(!tn.shields && tn.basicMatchTime == 300f && tn.knuckleMode == 0, "the host's MATCH OPTIONS reach the player in its tank");
                bool listed = false;
                foreach (var lg in ns[1].net.Games())
                    if (lg.name == "SIX") listed = !lg.shields && lg.matchTime == 300f && lg.knuckleMode == 0;
                t.C(listed, "and the games list shows them (HOTDOG, 5 minutes, no knuckle guards)");
                uint four = ns[4].net.Session;
                t.C(!ns[5].net.HostKick(four), "only the host can remove someone directly");
                t.C(B.net.HostKick(four), "the host removes a waiting player at once");
                Pump(ns, 1.0);
                t.C(!ns[4].net.InRoom, "removed by the host: out of the arena");
                uint ip2 = 0;
                foreach (var lg in ns[4].net.Games()) if (lg.name == "SIX") ip2 = lg.ip;
                ns[4].net.Join(ip2, false);
                Pump(ns, 1.0);
                t.C(!ns[4].net.InRoom, "and can't come back this session");
                t.C(!B.net.HostKick(B.net.Session), "the host can't remove itself");
            }
            {   // (native tools/run_room_test.py, 0.3.11: the relay's log names the game's own version, from its HELLO)
                int joins = 0;
                bool all = true;
                foreach (string l in RelayLog()) if (l.Contains(" join ")) { ++joins; all &= l.Contains(" 0.3.11 v"); }
                t.C(joins > 0 && all, $"the relay logs each game's version ({joins} joins)");
            }
            t.Done(89);   // (76 at protocol 8, native 0.3.13's two ROOM TALK checks, the relay's version log, COUNTRY KIT's three, MUTED PLAYERS' three, THE CROWD's four players' two, MUTED PLAYERS' two for everyone here)
        }
    }
}
