using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ProjectArena.Online;
using UnityEngine;

namespace ProjectArena
{
    // REPORT A BUG and REPORT PLAYER (native bugReportText, submitReport, bugSendAll, bugOutbox, sendBugReport): the
    // report is saved in bug_reports/ and sent to the community server over TCP ("PABUG1\n", u32 length, the text;
    // answered "OK"); anything not delivered is listed in bug_outbox.txt and sent again later. A report carries the
    // note, the player's settings, the network and match state and the game's recent log, nothing else. It says it
    // comes from the Unity port (the cross-play client label, as far as this side can give it).
    public static class Reports
    {
        public static readonly string[] BugCats = { "GAMEPLAY", "MULTIPLAYER", "AVATAR / OUTFIT", "MENUS / TEXT", "GRAPHICS", "OTHER" };
        public static readonly string[] ReportWhy = { "HARASSMENT OR HATE", "OFFENSIVE NAME OR LOOK", "CHEATING", "OTHER" };
        public const int NoteMax = 120;   // bug / player report notes

        static readonly object mu = new object();
        static readonly LinkedList<string> logLines = new LinkedList<string>();
        static int logChars;
        static bool installed;
        static string status = "";
        static int sending;
        public static volatile int done;   // 1 sent, 2 saved for later (the frame loop shows the status once)

        // keep the last ~60 kB of the game's log for reports (the native game reads the end of its log file)
        public static void Install()
        {
            if (installed) return;
            installed = true;
            Application.logMessageReceivedThreaded += (msg, _, type) =>
            {
                string line = $"{DateTime.Now:HH:mm:ss} {(type == LogType.Error || type == LogType.Exception ? 'E' : type == LogType.Warning ? 'W' : 'I')} {msg}";
                lock (mu)
                {
                    logLines.AddLast(line);
                    logChars += line.Length + 1;
                    while (logChars > 60000 && logLines.Count > 1) { logChars -= logLines.First.Value.Length + 1; logLines.RemoveFirst(); }
                }
            };
        }

        public static bool Sending => Volatile.Read(ref sending) != 0;
        public static string Status { get { lock (mu) return status; } }

        public static string RoleText(byte role) => role == Online.Net.VOICE_PLAYER ? "PLAYING" : role == Online.Net.VOICE_SPECTATOR ? "WATCHING" : "WAITING";

        // what: the report's own lines (a player report); null: the bug page's category and note
        public static string Text(AppState a, string title, string what, int bugCat, string bugNote)
        {
            var r = new StringBuilder();
            Online.Net net = a.net;
            r.Append(title).Append('\n');
            r.Append("player: ").Append(net.Name).Append('\n');
            r.Append("time: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            if (what == null)
            {
                r.Append("category: ").Append(bugCat >= 0 ? BugCats[bugCat] : "-").Append('\n');
                r.Append("note: ").Append(string.IsNullOrEmpty(bugNote) ? "-" : bugNote).Append('\n');
            }
            else r.Append(what);
            r.Append($"build: Unity port {Application.version} ({Application.platform}, {SystemInfo.deviceModel}), protocol v{Online.Net.kVersion}\n");
            r.Append($"network: {net.Status()}, {(net.Online ? "online" : "LAN")}, server {(string.IsNullOrEmpty(a.settings.server) ? "-" : "set")}\n");
            if (net.PingMs >= 0) r.Append($"ping: {net.PingMs:F0} ms\n");
            r.Append($"room: in room {(net.InRoom ? 1 : 0)}, slot {net.LocalSlot()}, waiting {(net.LocalWaiting() ? 1 : 0)}, queue {net.LocalQueuePos()}, spectating {(a.spectating ? 1 : 0)}\n");
            r.Append($"match: state {(int)a.game.match}, score {a.game.pl[0].score}-{a.game.pl[1].score}, rules {(a.game.tun.advanced ? "advanced" : "basic")}\n");
            var h = a.localIn.head.p;
            r.Append($"head: ({h.x:F2} {h.y:F2} {h.z:F2})\n");
            var d = a.settings.myAvatar;
            r.Append($"avatar: body {d.body} suit {d.suit} build {d.build} lower {d.lower} hair {d.hair} gear {d.headgear} visor {d.visor} mask {d.mask} gauntlet {d.gauntlet} kit {(Avatar.OwnKit(d) ? 1 : 0)}\n");
            var s = a.settings;
            r.Append($"settings: volume {s.volume}, pointer {(s.pointerHand != 0 ? "right" : "left")}, voice {s.voiceMode}, fx {s.fxLevel}, bot fill {(s.botFill ? 1 : 0)}\n");
            lock (mu)
            {   // the end of the log (what happened just before)
                r.Append($"\n---- log (last {logChars / 1024} kB) ----\n");
                foreach (string line in logLines) r.Append(line).Append('\n');
            }
            return r.ToString();
        }

        static string Dir(AppState a) => Path.Combine(a.settings.Folder, "bug_reports");
        static string Outbox(AppState a) => Path.Combine(a.settings.Folder, "bug_outbox.txt");

        public static List<string> OutboxFiles(AppState a)
        {
            var v = new List<string>();
            try
            {
                if (File.Exists(Outbox(a)))
                    foreach (string l in File.ReadAllLines(Outbox(a)))
                        if (l.Trim().Length > 0 && File.Exists(l.Trim())) v.Add(l.Trim());
            }
            catch (Exception) { }
            return v;
        }

        // saved in bug_reports/, then everything waiting is sent
        public static void Submit(AppState a, string text, string what)
        {
            string fn = null;
            try
            {
                Directory.CreateDirectory(Dir(a));
                string name = "report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                fn = Path.Combine(Dir(a), name + ".txt");
                for (int k = 2; File.Exists(fn); ++k) fn = Path.Combine(Dir(a), $"{name}_{k}.txt");
                File.WriteAllText(fn, text);
            }
            catch (Exception e) { Debug.LogWarning("[Report] could not save: " + e.Message); fn = null; }
            Debug.Log($"[Report] {what} -> {fn}");
            var files = OutboxFiles(a);
            if (fn != null) files.Add(fn);
            lock (mu) status = "SENDING...";
            SendAll(a, files);
        }

        public static void SendAll(AppState a, List<string> files)
        {
            if (Interlocked.Exchange(ref sending, 1) != 0) return;
            string server = a.settings.server, outbox = Outbox(a);
            new Thread(() =>
            {
                var left = new List<string>();
                string err = "";
                int sent = 0;
                foreach (string fn in files)
                {
                    string text;
                    try { text = File.ReadAllText(fn); } catch (Exception) { continue; }
                    if (!string.IsNullOrEmpty(server) && Send(server, text, out err)) ++sent;
                    else left.Add(fn);
                }
                try { File.WriteAllLines(outbox, left); } catch (Exception) { }
                lock (mu)
                {
                    if (left.Count == 0) status = sent > 0 ? "REPORT SENT - THANK YOU" : "";
                    else status = "SAVED - WILL SEND LATER (" + (string.IsNullOrEmpty(server) ? "NO SERVER SET" : err) + ")";
                }
                Debug.Log($"[Report] {sent} sent, {left.Count} waiting{(left.Count > 0 ? ": " + err : "")}");
                done = left.Count == 0 ? 1 : 2;
                Volatile.Write(ref sending, 0);
            }) { IsBackground = true }.Start();
        }

        // one report over TCP to the server (blocking with timeouts: call it off the main thread). err: a short reason
        public static bool Send(string hostPort, string text, out string err)
        {
            string host = hostPort ?? "";
            int port = Online.Net.kRelayPort;
            int c = host.LastIndexOf(':');
            if (c >= 0) { int.TryParse(host.Substring(c + 1), out port); host = host.Substring(0, c); }
            try
            {
                using (var cl = new TcpClient())
                {
                    cl.SendTimeout = cl.ReceiveTimeout = 8000;
                    var conn = cl.BeginConnect(host.ToLowerInvariant(), port, null, null);
                    if (!conn.AsyncWaitHandle.WaitOne(8000) || !cl.Connected) { err = "SERVER NOT REACHABLE"; return false; }
                    cl.EndConnect(conn);
                    byte[] body = Encoding.UTF8.GetBytes(text);
                    int n = Math.Min(body.Length, 200000);
                    var msg = new List<byte>(Encoding.ASCII.GetBytes("PABUG1\n"));
                    msg.AddRange(BitConverter.GetBytes((uint)n));
                    for (int i = 0; i < n; ++i) msg.Add(body[i]);
                    var s = cl.GetStream();
                    s.Write(msg.ToArray(), 0, msg.Count);
                    var reply = new byte[64];
                    int k = s.Read(reply, 0, reply.Length);
                    if (k >= 2 && reply[0] == 'O' && reply[1] == 'K') { err = ""; return true; }
                    err = k > 0 ? Encoding.ASCII.GetString(reply, 0, Math.Min(k, 40)).TrimEnd('\n', '\r') : "NO ANSWER";
                    return false;
                }
            }
            catch (SocketException) { err = "SERVER NOT FOUND"; return false; }
            catch (Exception) { err = "SEND FAILED"; return false; }
        }
    }
}
