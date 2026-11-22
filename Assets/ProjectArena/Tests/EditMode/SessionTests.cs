// The game's own flow through the network (Runtime/ArenaSession.cs, Menu.cs, RoomView.cs) as players use it: through a
// private local relay (127.0.0.1:47795; never the live server), one headset opens its arena from the menu (HOST A ROOM
// -> the rules -> OPEN MY ARENA), another joins it from the games list, waits at a holding spot, queues at the kiosk,
// both meet in the centre and bump fists, and a third watches from the spectator hub. Then a native host (the
// cross-play peer) with a Unity player joining it from the menu.
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
    public class SessionTests
    {
        const int RelayPort = 47795, CrowdPort = 47796;

        sealed class Player
        {
            public AppState a;
            public ArenaSession s;
            public Menu menu;
            public RoomView room;
            public PlayerInput local = new PlayerInput { headTracked = true };
            public string folder;
            public Player(string name, string server)
            {
                folder = Path.Combine(Path.GetTempPath(), "pa_session_" + Guid.NewGuid().ToString("N"));
                var g = new Game();
                g.SetArena(ArenaCollision.Load(File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
                var st = new AppSettings(folder);
                st.Load(g.tun);
                st.SaveName(name);
                st.LoadAvatar(7);
                st.server = server;
                st.onlinePref = true;
                st.statsNoticeSeen = true;   // (the one-time notice would open the menu)
                a = new AppState { game = g, settings = st, net = new Online.Net(), lobby = true };
                menu = new Menu(a);
                s = new ArenaSession(a) { menu = menu, builder = new Builder(a) };
                a.director = new Director(s);
                s.StartNetwork("");
                room = new RoomView(s);
            }
            public Menu.Item Find(Func<Menu.Item, bool> f) => menu.Items().First(f);
            public void Click(string label) => menu.Activate(Find(i => i.label == label));
            public void PickRoom(int k) => menu.Activate(Find(i => i.act == Menu.Act.RoomPick && i.ip == (uint)k));   // (CHOOSE A ROOM)
            // stand where the game places you, hands at rest in front
            public void Stand()
            {
                s.ViewTarget(s.L, out Vec3 pos, out Vec3 fwd);
                var q = Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-fwd.x, -fwd.z));
                local.head = new Pose(q, pos + new Vec3(0, 1.6f, 0));
                for (int h = 0; h < 2; ++h)
                {
                    if (local.hand[h].trigger > 0) continue;   // (a fist held out stays)
                    local.hand[h].tracked = true;
                    local.hand[h].grip = new Pose(q, local.head.p + q.Rotate(new Vec3(h == 1 ? 0.2f : -0.2f, -0.5f, -0.3f)));
                }
            }
            public void Dispose()
            {
                if (a.net.Online) a.net.GoOffline();
                a.net.Shutdown();
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }

        static List<Player> ps;

        static void Pump(double seconds)
        {
            var t = Stopwatch.StartNew();
            while (t.Elapsed.TotalSeconds < seconds)
            {
                foreach (var p in ps) { p.s.Step(p.local.Clone(), 1f / 90f); }
                System.Threading.Thread.Sleep(11);
            }
        }

        static bool Wait(Func<bool> cond, double seconds)
        {
            var t = Stopwatch.StartNew();
            while (t.Elapsed.TotalSeconds < seconds) { if (cond()) return true; Pump(0.1); }
            return cond();
        }

        static void FistAt(Player p, Vec3 at, Vec3 facing)
        {
            p.local.hand[1].trigger = 1;
            p.local.hand[1].grip.q = FromTo(new Vec3(0, 0, -1), facing);
            p.local.hand[1].grip.p = at - p.local.hand[1].grip.q.Rotate(p.a.game.tun.fistFront);
        }

        // START PRACTICE from the lobby: onto your end of the court against the bot, offline and online but in no room
        // (the template on a Quest 3, 2026-11-14: the view stayed on the balcony and the bot stood still)
        static void PracticeFromTheLobby(Player A)
        {
            for (int i = 0; i < 20; ++i) { A.Stand(); A.s.Step(A.local.Clone(), 1f / 90f); }
            Assert.That(A.a.InLobby && A.s.ViewKey() == 3u, "on the balcony");
            A.menu.Open(A.local.head, Lobby.Spot);
            A.menu.titleOpen = true;
            A.Click("PLAY PRACTICE");
            A.PickRoom(0); A.Click("ROUNDS");   // (the SQUARE, BEST OF 3: TASKS.md E22)
            A.Click("START PRACTICE");
            for (int i = 0; i < 20; ++i) { A.Stand(); A.s.Step(A.local.Clone(), 1f / 90f); }
            Assert.That(!A.a.lobby, "out of the lobby");
            Assert.That(!A.a.InLobby, "not shown the lobby");
            Assert.That(A.s.ViewKey(), Is.Not.EqualTo(3u), "placed again");
            A.s.ViewTarget(A.s.L, out Vec3 pos, out _);
            Assert.That(Length(pos - A.a.game.tun.start[A.s.L]), Is.LessThan(0.01f), "on your end of the court");
            Assert.That(!A.a.game.pl[A.s.L].isBot && A.a.game.pl[1 - A.s.L].isBot, "you against the bot");
        }

        // the lobby's bot in slot 0 must not move you: your input is the game's copy, as natively (a Quest 3, 2026-11-14:
        // CUSTOMIZE AVATAR's mirror drew the bot at the far end, its moves written into your own input)
        [Test]
        public void TheLobbyBotsLeaveYourInputAlone()
        {
            ps = new List<Player>();
            var A = new Player("ALICE", "");
            ps.Add(A);
            try
            {
                for (int i = 0; i < 20; ++i) { A.Stand(); A.s.Step(A.local.Clone(), 1f / 90f); }
                Assert.That(A.a.InLobby && A.a.game.pl[0].isBot, "on the balcony, two bots playing");
                PlayerInput mine = A.local.Clone();
                Vec3 head = mine.head.p, grip = mine.hand[1].grip.p;
                for (int i = 0; i < 90; ++i) A.s.Step(mine, 1f / 90f);
                Assert.That(Length(mine.head.p - head), Is.LessThan(1e-4f), "your head where you are");
                Assert.That(Length(mine.hand[1].grip.p - grip), Is.LessThan(1e-4f), "your hand where it is");
            }
            finally { A.Dispose(); }
        }

        [Test]
        public void PracticeFromTheLobbyOffline()
        {
            ps = new List<Player>();
            var A = new Player("ALICE", "");
            ps.Add(A);
            try { PracticeFromTheLobby(A); }
            finally { A.Dispose(); }
        }

        [Test]
        public void PracticeFromTheLobbyOnline()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_session_relay_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort + 20, CrowdPort + 20);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            ps = new List<Player>();
            try
            {
                var A = new Player("ALICE", $"127.0.0.1:{RelayPort + 20}");
                ps.Add(A);
                Assert.That(Wait(() => A.a.net.OnlineReady, 5), "online");
                // the version it says (TASKS.md E1): the native version it plays as, in its HELLO (the relay's log) and
                // as the project's own (the menus, the builds)
                Assert.That(RoomTests.RelayLog().Any(l => l.Contains(" join ") && l.Contains(" " + Online.Net.GameVersion + " v" + Online.Net.kVersion)),
                            "the relay logs the version it says: " + Online.Net.GameVersion);
                Assert.That(UnityEngine.Application.version, Is.EqualTo(Online.Net.GameVersion), "the project's version is the same");
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(Online.Net.GameVersion, @"^0\.3\.\d+$"), "a native version number");
                PracticeFromTheLobby(A);
            }
            finally
            {
                foreach (var p in ps) p.Dispose();
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        // FIST BUMP FIRST in practice (task A8, native 0804989): the bot meets you in the centre, the view goes there
        // for it as in a room, a fist bump starts the match and the view goes back to your end
        [Test]
        public void PracticeFistBumpFirst()
        {
            ps = new List<Player>();
            var A = new Player("ALICE", "");
            ps.Add(A);
            try
            {
                for (int i = 0; i < 20; ++i) { A.Stand(); A.s.Step(A.local.Clone(), 1f / 90f); }
                A.menu.Open(A.local.head, Lobby.Spot);
                A.menu.titleOpen = true;
                A.Click("PLAY PRACTICE");
                A.PickRoom(0); A.Click("TIMED MATCH");
                A.Click("FIST BUMP FIRST: OFF");
                A.Click("START PRACTICE");
                Game g = A.a.game;
                Assert.That(g.tun.botMeet, "FIST BUMP FIRST: on for the match");
                for (int i = 0; i < 90 * 3 && g.match != MatchState.Meet; ++i) { A.Stand(); A.s.Step(A.local.Clone(), 1f / 90f); }
                Assert.That(g.match == MatchState.Meet && A.s.MeetHere, "the bot meets you in the centre");
                Assert.That(A.s.ViewKey(), Is.EqualTo(2u), "the meeting's view, as in a room");
                A.s.ViewTarget(A.s.L, out Vec3 pos, out Vec3 fwd);
                Assert.That(Length(pos - new Vec3(-g.tun.meetSpot, 0, 0)) < 0.01f && fwd.x > 0.99f, "face to face at your side of the centre");
                A.Stand();
                FistAt(A, g.MeetRing(0), new Vec3(1, 0, 0));   // (the right fist held out in your ring at the meeting point, closed)
                bool bumped = false;
                float closest = 9f;
                for (int i = 0; i < 90 * 6 && !bumped; ++i)
                {
                    A.Stand();
                    bumped = A.s.Step(A.local.Clone(), 1f / 90f).Any(e => e.type == Sim.EventType.FistBump) || A.s.bumpFlash > 0;
                    for (int oh = 0; oh < 2; ++oh) closest = Math.Min(closest, g.FistGap(0, 1, 1, oh));
                }
                Assert.That(bumped, $"it brings its fist to yours: a fist bump (match {g.match}, the bot's head {g.pl[1].@in.head.p.x:F2}, " +
                                    $"closest fists {closest * 100:F0} cm, your fist {g.pl[0].@in.hand[1].grip.p.x:F2} {g.pl[0].@in.hand[1].grip.p.y:F2}, " +
                                    $"trigger {g.pl[0].@in.hand[1].trigger}, in the ring {g.InMeetRing(0, 1)})");
                A.local.hand[1].trigger = 0;
                for (int i = 0; i < 90 * 2; ++i) { A.Stand(); A.s.Step(A.local.Clone(), 1f / 90f); }
                Assert.That(g.match == MatchState.Countdown || g.match == MatchState.InProgress, "then the match starts");
                Assert.That(A.s.ViewKey(), Is.EqualTo(0u), "and the view goes back to your end");
            }
            finally { A.Dispose(); }
        }

        [Test]
        public void TeamCamerasReachATeammate()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_session_relay_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort + 10, CrowdPort + 10);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            string server = $"127.0.0.1:{RelayPort + 10}";
            ps = new List<Player>();
            try
            {
                var A = new Player("ALICE", server);
                var C = new Player("CAROL", server);
                var D = new Player("DAVE", server);
                ps.AddRange(new[] { A, C, D });
                Assert.That(Wait(() => ps.All(p => p.a.net.OnlineReady), 5), "online");
                A.a.net.Host();
                C.a.settings.team = D.a.settings.team = "ALPHA";
                C.a.net.Spectate(); D.a.net.Spectate();
                Assert.That(Wait(() => C.a.net.Matches.Count > 0 && D.a.net.Matches.Count > 0, 5), "both spectators hear the arena");
                C.a.director.warps.Add(new Director.WarpCam { pose = new Pose(Quat.Identity, new Vec3(2, 3, 4)), trig = 1 });
                C.a.net.ShareCameras("ALPHA", C.a.director.Pack());   // DIRECTOR TOOLS > SHARE
                Assert.That(Wait(() => D.a.director.warps.Count == 1, 3), "DAVE (team ALPHA) gets CAROL's cameras through the arena host");
                Assert.That(D.a.director.warps[0].trig == 1 && Length(D.a.director.warps[0].pose.p - new Vec3(2, 3, 4)) < 1e-4f, "as she set them");
                Assert.That(File.Exists(Path.Combine(D.folder, "warps.cfg")), "kept in his warps.cfg");
            }
            finally
            {
                foreach (var p in ps) p.Dispose();
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        [Test]
        public void HostJoinQueueBumpAndWatch()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_session_relay_" + Guid.NewGuid().ToString("N"));
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            string server = $"127.0.0.1:{RelayPort}";
            ps = new List<Player>();
            try
            {
                var A = new Player("ALICE", server);
                var B = new Player("BOB", server);
                var C = new Player("CAROL", server);
                ps.AddRange(new[] { A, B, C });
                Assert.That(Wait(() => ps.All(p => p.a.net.OnlineReady), 5), "online at start (ONLINE: ON with a server)");
                Assert.That(A.a.InLobby && A.s.Shown.pl[0].isBot, "everyone starts on the lobby balcony, the bots playing below");

                // ALICE: MULTIPLAYER > HOST A ROOM > the SQUARE, ROUNDS > OPEN MY ARENA
                A.menu.Open(A.local.head, Lobby.Spot);
                A.Click("HOST A ROOM");
                A.PickRoom(0); A.Click("ROUNDS");
                A.Click("OPEN MY ARENA");
                Pump(0.3);
                Assert.That(A.a.net.CurrentMode == Online.Net.Mode.Host && A.a.net.LocalWaiting(), "her arena is open; she waits outside the tank");
                Assert.That(!A.a.InLobby && A.a.viewing, "off the balcony, watching from the holding area");
                A.s.ViewTarget(A.s.L, out Vec3 at, out _);
                Assert.That(Length(at - ArenaSession.HoldingSpot(A.a.net.LocalSpot())) < 1e-4f, "placed at her holding spot");
                A.Stand();
                A.menu.Activate(A.Find(i => i.act == Menu.Act.Queue));   // JOIN QUEUE (the menu's)
                Assert.That(Wait(() => A.a.net.LocalSlot() == 0 && A.a.net.Room.bot, 3), "her JOIN QUEUE: into the tank against the bot");
                A.Stand();

                // BOB: MULTIPLAYER > JOIN ALICE (the games list), then the kiosk in front of his spot
                B.menu.Open(B.local.head, Lobby.Spot);
                Assert.That(Wait(() => B.menu.Items().Any(i => i.act == Menu.Act.JoinRoom && i.label.StartsWith("JOIN  ALICE")), 5), "BOB's games list shows ALICE's arena");
                B.menu.Activate(B.Find(i => i.act == Menu.Act.JoinRoom));
                Assert.That(Wait(() => B.a.net.LocalWaiting(), 4), "he waits outside her tank");
                B.Stand();
                Pose k = B.room.KioskPose();
                var aim = new Pose(FromTo(new Vec3(0, 0, -1), Normalize(k.p - B.local.head.p)), B.local.head.p);
                B.room.UpdateKiosk(B.local, 0f, aim, true, B.menu);
                bool pressed = B.room.UpdateKiosk(B.local, 1f, aim, true, B.menu);
                Assert.That(pressed, "pointing at the JOIN QUEUE kiosk and pulling the trigger");
                Assert.That(Wait(() => B.a.net.LocalSlot() == 1, 4), "he takes the bot's place in the tank");
                Assert.That(Wait(() => A.s.Shown.match == MatchState.Meet && B.s.Shown.match == MatchState.Meet, 3), "they meet in the centre");
                A.Stand(); B.Stand();
                A.s.ViewTarget(0, out Vec3 ma, out _); B.s.ViewTarget(1, out Vec3 mb, out _);
                Assert.That(Math.Abs(ma.x + A.a.game.tun.meetSpot) < 1e-4f && Math.Abs(mb.x - B.a.game.tun.meetSpot) < 1e-4f, "each placed at the meeting spot");
                Assert.That(B.s.PlayerLabel(0) == "ALICE" && A.s.PlayerLabel(1) == "BOB", "the jumbotron's names from the room");
                FistAt(A, new Vec3(-0.01f, 1.2f, 0f), new Vec3(1, 0, 0));
                FistAt(B, new Vec3(0.01f, 1.2f, 0f), new Vec3(-1, 0, 0));
                Assert.That(Wait(() => A.s.Shown.match == MatchState.Countdown || A.s.Shown.match == MatchState.InProgress, 4), "the fist bump starts the match");
                Assert.That(A.s.bumpFlash > 0 || A.s.Shown.match == MatchState.InProgress, "FIST BUMP! on the host");
                A.local.hand[1].trigger = 0; B.local.hand[1].trigger = 0;
                A.Stand(); B.Stand();

                // CAROL: SPECTATE > point at the miniature and pull the trigger
                C.menu.Open(C.local.head, Lobby.Spot);
                C.menu.Activate(C.Find(i => i.label == "SPECTATE GAMES"));
                Assert.That(Wait(() => C.a.spectating && C.a.net.Matches.Count > 0, 4), "the hub shows the live arena");
                C.Stand();
                Assert.That(Length(C.local.head.p - new Vec3(0, 1.6f, 0)) < 1e-4f, "the hub: at its centre");
                bool watched = false;
                for (int t = 0; t < 2 && !watched; ++t)
                {
                    var mini = new Vec3(1.35f, 0.92f + 0.12f, 0f);   // the only miniature: straight ahead (+X)
                    var caim = new Pose(FromTo(new Vec3(0, 0, -1), Normalize(mini - C.local.head.p)), C.local.head.p);
                    C.local.hand[1].trigger = t;
                    watched = C.room.UpdateHub(C.local, caim, true, C.menu);
                }
                Assert.That(watched && C.a.watchIp != 0, "pointing at it and pulling the trigger watches it");
                Assert.That(Wait(() => C.s.Shown != C.a.game && C.s.Shown.match == A.s.Shown.match, 3), "the watched match drives the view");
                C.Stand();
                Assert.That(Length(C.local.head.p - new Vec3(0, 2.6f, 5.4f)) < 1e-4f, "from the gallery seat");
                Assert.That(Wait(() => A.a.net.Room.watchers.Any(w => w.where == 1 && w.id == C.a.net.Session), 3), "the host lists the spectator at its arena");

                // leaving: BOB's LEAVE GAME puts the bot back; ALICE's LOBBY closes her arena, and CAROL is back at the hub
                B.menu.Open(B.local.head, Lobby.Spot);
                B.Click("LEAVE GAME");
                Assert.That(Wait(() => B.a.InLobby && A.a.net.Room.bot, 4), "BOB back on the balcony; the bot takes his place");
                A.menu.Open(A.local.head, Lobby.Spot);
                A.menu.Activate(A.Find(i => i.act == Menu.Act.Nav && i.ip == (uint)Menu.Nav.Lobby));
                Assert.That(Wait(() => A.a.InLobby && C.a.watchIp == 0, 6), "ALICE in the lobby, her arena closed; CAROL back at the hub");
            }
            finally
            {
                foreach (var p in ps) p.Dispose();
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }
    }
}
