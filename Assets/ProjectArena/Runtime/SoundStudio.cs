// SOUND STUDIO (task C8; native 0.3.16 sound_studio.cpp and main.cpp's studio*): try sounds that aren't in the game
// yet in the moment they belong to, and vote on them. The library lives on the community server (the relay's
// --sounds); the studio downloads it into the game's folder (sound_library), keeping it in step (a sound gone from the
// server goes here too). MINE: your own .wav files in my_sounds, tried only on this headset. Alternate sounds play only
// inside the studio. Its moment is a TRAINING lesson played endlessly; its page opens in front of you once you stand on
// the court (native 0.3.19).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ProjectArena.Online;
using UnityEngine;

namespace ProjectArena
{
    public sealed class StudioSound
    {
        public string file = "", eventName = "", name = "", path = "";
        public int vote;
        public bool ready, official, mine;
    }

    public static class StudioCatalog
    {
        // the studio's sections: the events that belong together, and the TRAINING lesson whose moment plays them (its
        // id; 0: none, the section's sounds are tried with PLAY)
        public sealed class Section { public string name; public string[] events; public int lesson; public string how; }
        public static readonly Section[] Sections =
        {
            new Section { name = "SHIELD", events = new[] { "ShieldBlock", "ShieldBreak", "Recharge" }, lesson = 21,
                          how = "TAKE YOUR BALL: YOUR SHIELD COMES UP. THE COACH THROWS AT IT: THE BLOCK, THE BREAK, THEN THE RECHARGE" },
            new Section { name = "THROWS", events = new[] { "ThrowSlow", "ThrowMed", "ThrowFast", "ThrowFull", "ThrowFail", "TargetHit" }, lesson = 1,
                          how = "THROW AT THE TARGETS, SLOW TO FULL SPEED; A THROW TOO WEAK TO FLY IS THE FAIL" },
            new Section { name = "BALL", events = new[] { "Spawn", "SpawnRise", "Grab", "Caught", "Bounce", "Returned" }, lesson = 8,
                          how = "THROW AT THE FAR WALL AND CATCH YOUR BALL AS IT COMES BACK" },
            new Section { name = "HITS", events = new[] { "KnuckleBlock", "KnucklePunch", "HitPlayerSelf", "HitPlayerOpponent", "Strike", "Foul" }, lesson = 23,
                          how = "THE COACH THROWS AT YOU: PUNCH IT AWAY (HARD FOR THE HARD PUNCH), OR LET ONE HIT YOU" },
            new Section { name = "MATCH", events = new[] { "Countdown", "RoundStart", "RoundEndWin", "RoundEndLose", "MatchEndWin", "MatchEndLose", "Overtime",
                                                           "FistBump", "LeadChange", "ClockTick" }, lesson = 0, how = "PRESS PLAY TO HEAR EACH ONE" },
            new Section { name = "CROWD", events = new[] { "Ambience", "Crowd", "Cheer" }, lesson = 0, how = "PRESS PLAY TO HEAR EACH ONE" },
        };

        // the asset catalog's sound slots (native kSlots, tools/gen_catalog.py SOUNDS: S01-S34)
        static readonly (string ev, string code, string name)[] Slots =
        {
            ("Spawn", "S01", "BALL APPEARS"), ("SpawnRise", "S02", "LAUNCHER RISES"), ("Grab", "S03", "GRAB"), ("Caught", "S04", "CATCH"),
            ("ThrowSlow", "S05", "THROW (SLOW)"), ("ThrowMed", "S06", "THROW (MEDIUM)"), ("ThrowFast", "S07", "THROW (FAST)"),
            ("ThrowFull", "S08", "THROW (FULL POWER)"), ("ThrowFail", "S09", "DROPPED THROW"), ("Bounce", "S10", "WALL BOUNCE"),
            ("ShieldBlock", "S11", "SHIELD BLOCK"), ("ShieldBreak", "S12", "SHIELD BREAK"), ("KnuckleBlock", "S13", "KNUCKLE PUNCH"),
            ("HitPlayerOpponent", "S14", "YOU SCORED"), ("HitPlayerSelf", "S15", "YOU WERE HIT"), ("Returned", "S16", "BALL RETURNS"),
            ("Foul", "S17", "FOUL"), ("Countdown", "S18", "COUNTDOWN BEEP"), ("RoundStart", "S19", "ROUND START"), ("RoundEndWin", "S20", "ROUND WON"),
            ("RoundEndLose", "S21", "ROUND LOST"), ("MatchEndWin", "S22", "MATCH WON"), ("MatchEndLose", "S23", "MATCH LOST"),
            ("Recharge", "S24", "RECHARGE"), ("Strike", "S25", "STRIKE"), ("Overtime", "S26", "SUDDEN DEATH"), ("Ambience", "S27", "ARENA AMBIENCE"),
            ("FistBump", "S28", "FIST BUMP"), ("Crowd", "S29", "CROWD"), ("Cheer", "S30", "CROWD CHEER"), ("ClockTick", "S31", "CLOCK TICK"),
            ("LeadChange", "S32", "LEAD CHANGE"), ("TargetHit", "S33", "TARGET HIT"),
            ("KnucklePunch", "S34", "HARD PUNCH"),   // (TASKS.md E31, native c0506be: the punch that sends the ball back at 12 m/s or more)
        };
        public static int SlotCount => Slots.Length;

        public static string PrettyEvent(string e)   // "ShieldBreak" -> "SHIELD BREAK"
        {
            var o = new StringBuilder();
            for (int i = 0; i < e.Length; ++i)
            {
                char c = e[i];
                if (i > 0 && c >= 'A' && c <= 'Z' && !(e[i - 1] >= 'A' && e[i - 1] <= 'Z')) o.Append(' ');
                o.Append(c >= 'a' && c <= 'z' ? (char)(c - 32) : c);
            }
            return o.ToString();
        }
        public static string CodeOf(string e) { foreach (var s in Slots) if (s.ev == e) return s.code; return ""; }
        public static string SlotName(string e) { foreach (var s in Slots) if (s.ev == e) return s.code + " " + s.name; return PrettyEvent(e ?? ""); }
        public static string EventOf(string file) { int k = file.IndexOf("__", StringComparison.Ordinal); return k < 0 ? "" : file.Substring(0, k); }
        public static string PrettyName(string file)   // "ShieldBreak__glass_crack.wav" -> "GLASS CRACK"
        {
            int k = file.IndexOf("__", StringComparison.Ordinal);
            string n = k < 0 ? file : file.Substring(k + 2);
            if (n.Length > 4 && n.EndsWith(".wav", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 4);
            var o = new StringBuilder();
            foreach (char c in n) o.Append(c == '_' ? ' ' : c >= 'a' && c <= 'z' ? (char)(c - 32) : c);
            return o.ToString();
        }
        public static string Sha256Hex(byte[] d)
        {
            using (var h = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in h.ComputeHash(d)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
        // "S12_glass.wav" / "ShieldBreak__x.wav" -> "ShieldBreak" ("" none): a code at the start (S12, s12, S2 for S02),
        // then anything not a digit; or an event's name before "__"
        public static string SlotFromName(string file)
        {
            if (file.Length >= 2 && (file[0] == 'S' || file[0] == 's') && file[1] >= '0' && file[1] <= '9')
            {
                int k = 1, num = 0;
                while (k < file.Length && k < 3 && file[k] >= '0' && file[k] <= '9') num = num * 10 + (file[k++] - '0');
                if (k == file.Length || !(file[k] >= '0' && file[k] <= '9'))
                {
                    string code = $"S{num:00}";
                    foreach (var s in Slots) if (s.code == code) return s.ev;
                }
            }
            int u = file.IndexOf("__", StringComparison.Ordinal);
            if (u >= 0) { string e = file.Substring(0, u); foreach (var s in Slots) if (s.ev == e) return s.ev; }
            return "";
        }
        // the game's own, one per slot (file "official:Event"; name "S13 KNUCKLE PUNCH")
        public static List<StudioSound> OfficialSounds() =>
            Slots.Select(s => new StudioSound { file = "official:" + s.ev, eventName = s.ev, name = s.code + " " + s.name, ready = true, official = true }).ToList();
        // MINE: your own .wav files in `folder` (made, with a README, if it isn't there)
        public static List<StudioSound> MySounds(string folder)
        {
            var names = new List<string>();
            try { if (Directory.Exists(folder)) names.AddRange(Directory.GetFiles(folder).Select(Path.GetFileName).Where(n => n.Length > 0 && n[0] != '.')); }
            catch (Exception) { }
            if (names.Count == 0)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "README.txt"),
                        "SOUND STUDIO: your own sounds, to try on this headset (or PC) only.\n" +
                        "Put .wav files in this folder (WAV format only: export as WAV from Audacity or any sound editor).\n" +
                        "Name a file after its sound slot to put it first in that slot: S12_glass.wav or ShieldBreak__glass.wav\n" +
                        "(the slots S01-S34 are in docs/SOUND_STUDIO.md); any other name can be tried in every slot.\n" +
                        "Open the SOUND STUDIO again to see new files. To share a sound, propose it on GitHub or Discord.\n");
                }
                catch (Exception) { }
            }
            names.Sort(StringComparer.Ordinal);
            var v = new List<StudioSound>();
            foreach (string n in names)
            {
                if (n.Length < 5 || !n.Substring(n.Length - 4).Equals(".wav", StringComparison.OrdinalIgnoreCase) || v.Count >= 60) continue;   // (WAV only)
                string label = n.Substring(0, n.Length - 4).Replace('_', ' ').ToUpperInvariant();
                if (label.Length > 22) label = label.Substring(0, 21) + ".";
                v.Add(new StudioSound { file = "mine:" + n, path = Path.Combine(folder, n), eventName = SlotFromName(n), name = label, ready = true, mine = true });
            }
            return v;
        }
        // for one event: OFFICIAL (-1) first, then the library's sounds made for it, then yours made for it, then yours
        // without a slot, then the library's (and your) others, then the game's own sounds of the other slots (each in
        // the catalog's order); indices into `lib`
        public static List<int> ChoicesFor(string ev, List<StudioSound> lib)
        {
            var v = new List<int> { -1 };
            for (int i = 0; i < lib.Count; ++i) if (lib[i].ready && !lib[i].official && !lib[i].mine && lib[i].eventName == ev) v.Add(i);
            for (int i = 0; i < lib.Count; ++i) if (lib[i].ready && lib[i].mine && lib[i].eventName == ev) v.Add(i);
            for (int i = 0; i < lib.Count; ++i) if (lib[i].ready && lib[i].mine && lib[i].eventName.Length == 0) v.Add(i);
            var rest = new List<int>(); var games = new List<int>();
            for (int i = 0; i < lib.Count; ++i)
                if (lib[i].ready && lib[i].eventName != ev && !(lib[i].mine && lib[i].eventName.Length == 0)) (lib[i].official ? games : rest).Add(i);
            // (a stable sort by the slot's code, as native std::stable_sort)
            v.AddRange(rest.Select((x, k) => (x, k)).OrderBy(p => CodeOf(lib[p.x].eventName), StringComparer.Ordinal).ThenBy(p => p.k).Select(p => p.x));
            v.AddRange(games.Select((x, k) => (x, k)).OrderBy(p => CodeOf(lib[p.x].eventName), StringComparer.Ordinal).ThenBy(p => p.k).Select(p => p.x));
            return v;
        }
    }

    // the library on the headset: the folder, and a list of what is in it (sound_library/library.txt); synced on a thread
    public sealed class SoundLibrary
    {
        public enum State { Idle, Loading, Ready, Failed }
        volatile State state = State.Idle;
        int done, total;
        readonly object mu = new object();
        List<StudioSound> result;
        bool fresh;
        string err = "", folder = "";
        Thread th;

        public State CurrentState => state;
        public string Error { get { lock (mu) return err; } }
        public int Progress => Volatile.Read(ref done);
        public int Total => Volatile.Read(ref total);
        public string PathOf(string file) => Path.Combine(folder, file);

        public void Sync(string server, string folderPath, byte[] key)
        {
            if (state == State.Loading) return;
            th?.Join();
            state = State.Loading;
            done = total = 0;
            folder = folderPath;
            byte[] k = key != null ? (byte[])key.Clone() : new byte[16];
            th = new Thread(() => Run(server, folderPath, k)) { IsBackground = true, Name = "sound studio" };
            th.Start();
        }
        public void Wait() => th?.Join();   // (tests)

        // the sounds once synced, taken on the main thread (false: nothing new)
        public bool Take(out List<StudioSound> o)
        {
            lock (mu)
            {
                o = fresh ? result : null;
                if (!fresh) return false;
                fresh = false;
                return true;
            }
        }

        void Run(string server, string dir, byte[] key)
        {
            var cat = new List<Net.LibSound>();
            string e = "";
            if (string.IsNullOrEmpty(server) || !Net.FetchSoundCatalog(server, key, cat, out e))
            {
                lock (mu) err = string.IsNullOrEmpty(server) ? "NO SERVER SET" : e;
                Debug.LogWarning("[Studio] no library (" + Error + ")");
                state = State.Failed;
                return;
            }
            var o = new List<StudioSound>();
            int fetched = 0;
            try
            {
                Directory.CreateDirectory(dir);
                string listPath = Path.Combine(dir, "library.txt");
                var had = File.Exists(listPath) ? File.ReadAllLines(listPath).Where(l => l.Length > 0).ToList() : new List<string>();
                foreach (string f in had)   // gone from the server: gone from here
                    if (!cat.Any(c => c.file == f))
                    {
                        try { File.Delete(Path.Combine(dir, f)); } catch (Exception) { }
                        Debug.Log($"[Studio] {f} left the library");
                    }
                Volatile.Write(ref total, cat.Count);
                var kept = new List<string>();
                foreach (Net.LibSound c in cat)
                {
                    var s = new StudioSound { file = c.file, eventName = StudioCatalog.EventOf(c.file), name = StudioCatalog.PrettyName(c.file), vote = c.vote };
                    string p = Path.Combine(dir, c.file);
                    byte[] d = File.Exists(p) ? File.ReadAllBytes(p) : null;
                    if (d != null && d.Length == c.size && StudioCatalog.Sha256Hex(d) == c.sha) s.ready = true;
                    else if (Net.FetchSound(server, c.file, out d, out string fe) && d.Length == c.size && StudioCatalog.Sha256Hex(d) == c.sha)
                    {
                        File.WriteAllBytes(p + ".part", d);
                        if (File.Exists(p)) File.Delete(p);
                        File.Move(p + ".part", p);
                        s.ready = true;
                        ++fetched;
                    }
                    else Debug.LogWarning($"[Studio] {c.file} not downloaded ({(string.IsNullOrEmpty(fe) ? "its checksum doesn't match" : fe)})");
                    if (s.ready) kept.Add(c.file);
                    o.Add(s);
                    Interlocked.Increment(ref done);
                }
                File.WriteAllText(listPath, string.Concat(kept.Select(f => f + "\n")));
            }
            catch (Exception ex) { Debug.LogWarning("[Studio] " + ex.Message); }
            Debug.Log($"[Studio] {o.Count} sounds in the library ({fetched} downloaded now)");
            lock (mu) { result = o; fresh = true; err = ""; }
            state = State.Ready;
        }
    }

    // the studio's state and actions (native main.cpp studioOpen / studioOpenSection / studioLeave / studioStep /
    // studioVote / studioDelete / studioUpdate / studioApply)
    public sealed class SoundStudio
    {
        readonly AppState a;
        public bool on;
        public int section;
        public List<StudioSound> sounds = new List<StudioSound>();
        public readonly Dictionary<string, string> pick = new Dictionary<string, string>();   // event -> the chosen sound's file
        readonly Dictionary<string, bool> loaded = new Dictionary<string, bool>();             // clip -> loaded
        public string delArmed = "";
        public double delUntil;
        public readonly SoundLibrary lib = new SoundLibrary();
        public SoundStudio(AppState app) { a = app; }

        static double Now => Time.realtimeSinceStartupAsDouble;
        string Dir(string sub) => Path.Combine(a.settings.Folder, sub);
        bool Offline => a.net == null || a.net.CurrentMode == Net.Mode.Offline;
        void Notice(string s) => a.net?.NoticeLocal(s);

        public int Chosen(string ev)   // the sound chosen for an event (-1: the game's own)
        {
            if (!pick.TryGetValue(ev, out string f) || string.IsNullOrEmpty(f)) return -1;
            for (int i = 0; i < sounds.Count; ++i) if (sounds[i].ready && sounds[i].file == f) return i;
            return -1;
        }
        public bool VoteLit(int k, int dir)
        {
            var sec = StudioCatalog.Sections[section];
            if (k < 0 || k >= sec.events.Length) return false;
            int s = Chosen(sec.events[k]);
            return s >= 0 && sounds[s].vote == dir;
        }

        void Apply(string ev)   // the event's choice into the mixer (studio only)
        {
            NativeMixer m = a.mixer;
            int s = Chosen(ev);
            if (m == null) return;
            if (s < 0) { m.SetOverride(ev, ""); return; }
            StudioSound snd = sounds[s];
            if (snd.official) { m.SetOverride(ev, m.OfficialClip(snd.eventName)); return; }   // the game's own sound of another slot
            string clip = (snd.mine ? "mine/" : "studio/") + snd.file;
            if (!loaded.TryGetValue(clip, out bool ok)) loaded[clip] = ok = m.LoadFile(clip, snd.mine ? snd.path : lib.PathOf(snd.file));
            if (!ok && snd.mine) Notice("THAT FILE ISN'T A WAV THE GAME CAN PLAY - EXPORT IT AS WAV");
            m.SetOverride(ev, ok ? clip : "");
        }
        void ApplyAll() { foreach (var sec in StudioCatalog.Sections) foreach (string ev in sec.events) Apply(ev); }
        void AddLocal()   // your own .wav files (my_sounds) and the game's own sounds, after the library's
        {
            sounds.AddRange(StudioCatalog.MySounds(Dir("my_sounds")));
            sounds.AddRange(StudioCatalog.OfficialSounds());
        }

        public void Open(Menu menu)
        {
            if (!Offline) return;
            if (menu != null) menu.titleOpen = false;
            on = true;
            sounds.Clear();   // (yours and the game's own at once; the library's join them when synced)
            foreach (string k in loaded.Keys.Where(k => k.StartsWith("mine/", StringComparison.Ordinal)).ToList()) loaded.Remove(k);   // (your files read afresh)
            AddLocal();
            pick.Clear();
            lib.Sync(a.settings.server, Dir("sound_library"), a.settings.LoadStatsKey());
            Debug.Log("[Studio] open");
            OpenSection(0, menu);
        }
        public void OpenSection(int i, Menu menu)   // a section, and its moment: its TRAINING lesson, endless
        {
            section = Math.Max(0, Math.Min(StudioCatalog.Sections.Length - 1, i));
            TrainingApp tr = a.training ??= new TrainingApp(a);
            int id = StudioCatalog.Sections[section].lesson != 0 ? StudioCatalog.Sections[section].lesson : (tr.trainer.Active ? 0 : 21);
            int li = id != 0 ? Sim.Training.Lessons.ById(id) : -1;
            if (li >= 0 && (!tr.trainer.Active || tr.trainer.lesson != li))
            {
                tr.BeginLesson(li, true, menu);
                tr.trainer.endless = true;
                // the page opens once you stand on the court (native 0.3.19, owner 2026-11-12: opened in the lobby, it stayed
                // behind there and the studio began with no menu), as a lesson's card does
                if (menu != null) menu.open = false;
                tr.menuAt = Time.realtimeSinceStartupAsDouble + 0.35;
                return;
            }
            menu?.Open(a.localIn.head, Lobby.Spot);
        }
        public void Leave(Menu menu)   // MAIN MENU: the game's own sounds again, out of the lesson to the lobby
        {
            on = false;
            a.mixer?.ClearOverrides();
            pick.Clear();
            if (a.training != null && a.training.trainer.Active) { a.training.Stop(); a.lobby = true; }
            Debug.Log("[Studio] closed");
            if (menu != null) { menu.Open(a.localIn.head, Lobby.Spot); menu.titleOpen = true; }
        }
        public void Step(int k, int d)   // the event's next or previous choice, played at once
        {
            var sec = StudioCatalog.Sections[section];
            if (k < 0 || k >= sec.events.Length) return;
            string ev = sec.events[k];
            List<int> ch = StudioCatalog.ChoicesFor(ev, sounds);
            int cur = Chosen(ev), at = 0;
            for (int i = 0; i < ch.Count; ++i) if (ch[i] == cur) at = i;
            at = (at + d + ch.Count) % ch.Count;
            pick[ev] = ch[at] < 0 ? "" : sounds[ch[at]].file;
            Apply(ev);
            a.mixer?.PlayEvent(ev, 1f, 0f);
        }
        public void Play(int k)
        {
            var sec = StudioCatalog.Sections[section];
            if (k >= 0 && k < sec.events.Length) a.mixer?.PlayEvent(sec.events[k], 1f, 0f);
        }
        public void Vote(int k, int dir)   // UP / DOWN; your vote pressed again takes it back
        {
            var sec = StudioCatalog.Sections[section];
            if (k < 0 || k >= sec.events.Length) return;
            int s = Chosen(sec.events[k]);
            if (s < 0 || sounds[s].official || sounds[s].mine || a.net == null) return;   // (votes are for the library's new sounds)
            StudioSound snd = sounds[s];
            int v = snd.vote == dir ? 0 : dir;
            if (a.net.SendSoundVote(snd.file, v))   // (kept until the community server takes it)
            {
                snd.vote = v;
                Debug.Log($"[Studio] vote {v:+0;-0;0} for {snd.file}");
                Notice(v > 0 ? "YOU VOTED UP - IT GOES TO THE COMMUNITY'S COUNT" : v < 0 ? "YOU VOTED DOWN - IT GOES TO THE COMMUNITY'S COUNT" : "YOUR VOTE IS TAKEN BACK");
            }
        }
        public void Delete(int k)   // one of your own files off the headset (DELETE, then SURE? within 4 s)
        {
            var sec = StudioCatalog.Sections[section];
            if (k < 0 || k >= sec.events.Length) return;
            int s = Chosen(sec.events[k]);
            if (s < 0 || !sounds[s].mine) return;
            StudioSound snd = sounds[s];
            if (delArmed != snd.file || Now > delUntil) { delArmed = snd.file; delUntil = Now + 4.0; return; }
            delArmed = "";
            bool gone;
            try { File.Delete(snd.path); gone = !File.Exists(snd.path); } catch (Exception) { gone = false; }
            Debug.Log($"[Studio] {snd.path} {(gone ? "deleted" : "could not be deleted")}");
            if (!gone) { Notice("COULDN'T DELETE THAT FILE"); return; }
            sounds.RemoveAt(s);
            foreach (string ev in pick.Keys.ToList()) if (pick[ev] == snd.file) pick[ev] = "";
            ApplyAll();
            Notice("DELETED " + snd.name);
        }
        // every frame: the synced library in; out of the studio if its lesson stopped
        public void Update()
        {
            if (!on) return;
            if (a.training == null || !a.training.trainer.Active || !Offline)
            {
                on = false;
                a.mixer?.ClearOverrides();
                pick.Clear();
                return;
            }
            if (lib.Take(out List<StudioSound> got))
            {
                foreach (StudioSound s in got) if (a.net != null && a.net.PendingSoundVote(s.file, out int v)) s.vote = v;   // (a vote not yet sent beats the server's)
                sounds = got;
                AddLocal();
                ApplyAll();
            }
        }
        // MINE: how many of your files, and how much room they take
        public void MineSummary(out int n, out double mb)
        {
            n = 0; mb = 0;
            foreach (var s in sounds)
                if (s.mine) { ++n; try { mb += new FileInfo(s.path).Length / 1048576.0; } catch (Exception) { } }
        }
    }
}
