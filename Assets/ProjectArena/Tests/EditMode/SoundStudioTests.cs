// SOUND STUDIO (task C8, native 0.3.16): the catalog's rules (slots, names, the choices' order), MINE (your own .wav
// files), the WAV reader (8 / 16 / 24 / 32-bit, float, rates, channels), the mixer's studio overrides, TRAINING's endless
// moment, and the library through a private local relay (never the live server): the catalog, a download checked by
// SHA-256, a sound gone from the server gone here too, and a vote reaching the community's count.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEngine;
using Node = ProjectArena.Tests.RoomTests.Node;

namespace ProjectArena.Tests
{
    public class SoundStudioTests
    {
        const int RelayPort = 47803, CrowdPort = 47804;

        // a WAV: `channels` x `frames` samples of `bits` (or 32-bit float) at `rate`
        static byte[] Wav(int rate, int channels, int bits, bool isFloat, int frames, Func<int, int, float> sample, bool extensible = false)
        {
            int w = bits / 8, data = frames * channels * w, fmtSize = extensible ? 40 : 16;
            var o = new MemoryStream();
            var b = new BinaryWriter(o);
            b.Write("RIFF".ToCharArray()); b.Write(4 + 8 + fmtSize + 8 + data); b.Write("WAVE".ToCharArray());
            b.Write("fmt ".ToCharArray()); b.Write(fmtSize);
            b.Write((ushort)(extensible ? 0xFFFE : isFloat ? 3 : 1)); b.Write((ushort)channels); b.Write(rate); b.Write(rate * channels * w);
            b.Write((ushort)(channels * w)); b.Write((ushort)bits);
            if (extensible) { b.Write((ushort)22); b.Write((ushort)bits); b.Write(0); b.Write((ushort)(isFloat ? 3 : 1)); b.Write(new byte[14]); }
            b.Write("data".ToCharArray()); b.Write(data);
            for (int f = 0; f < frames; ++f)
                for (int c = 0; c < channels; ++c)
                {
                    float v = sample(f, c);
                    if (isFloat) b.Write(v);
                    else if (bits == 8) b.Write((byte)Mathf.Clamp(Mathf.RoundToInt(v * 128f + 128f), 0, 255));
                    else if (bits == 16) b.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
                    else if (bits == 24) { int x = Mathf.RoundToInt(v * 8388607f); b.Write((byte)x); b.Write((byte)(x >> 8)); b.Write((byte)(x >> 16)); }
                    else b.Write((int)(v * 2147483647.0));
                }
            b.Flush();
            return o.ToArray();
        }

        // CROWD UNDER VOICES (native 0.3.19): someone talking turns the crowd sounds down to 0.32 quickly; they come back
        // smoothly once nobody has spoken for 0.6 s
        [Test]
        public void TheCrowdGoesDownUnderVoices()
        {
            float level = 1f, hold = 0f, block = 1024f / 48000f;
            for (int i = 0; i < 10; ++i) level = NativeMixer.DuckStep(level, ref hold, true, block);
            Assert.That(level, Is.LessThan(0.37f), "nearly down within a fifth of a second of talk");
            for (int i = 0; i < 5; ++i) level = NativeMixer.DuckStep(level, ref hold, true, block);
            Assert.That(level, Is.EqualTo(NativeMixer.KDuck).Within(0.015f), "down to 0.32");
            for (int i = 0; i < 20; ++i) level = NativeMixer.DuckStep(level, ref hold, false, block);
            Assert.That(level, Is.LessThan(0.34f), "held down through a short pause");
            for (int i = 0; i < 60; ++i) level = NativeMixer.DuckStep(level, ref hold, false, block);
            Assert.That(level, Is.GreaterThan(0.9f), "back up once nobody has spoken for a while");
        }

        [Test]
        public void TheCatalogsRules()
        {
            Assert.That(StudioCatalog.PrettyEvent("ShieldBreak"), Is.EqualTo("SHIELD BREAK"));
            Assert.That(StudioCatalog.SlotName("ShieldBreak"), Is.EqualTo("S12 SHIELD BREAK"));
            Assert.That(StudioCatalog.SlotName("NoSuch"), Is.EqualTo("NO SUCH"), "an event it doesn't know: its spaced name");
            Assert.That(StudioCatalog.SlotCount, Is.EqualTo(34), "S01-S34 (S34 HARD PUNCH: TASKS.md E31)");
            Assert.That(StudioCatalog.SlotName("KnucklePunch"), Is.EqualTo("S34 HARD PUNCH"));
            Assert.That(StudioCatalog.PrettyName("ShieldBreak__glass_crack.wav"), Is.EqualTo("GLASS CRACK"));
            Assert.That(StudioCatalog.EventOf("ShieldBreak__glass_crack.wav"), Is.EqualTo("ShieldBreak"));
            Assert.That(StudioCatalog.SlotFromName("S12_glass.wav"), Is.EqualTo("ShieldBreak"));
            Assert.That(StudioCatalog.SlotFromName("s2 thing.wav"), Is.EqualTo("SpawnRise"), "S2 is S02");
            Assert.That(StudioCatalog.SlotFromName("S123.wav"), Is.EqualTo(""), "three digits: no slot");
            Assert.That(StudioCatalog.SlotFromName("Bounce__soft.wav"), Is.EqualTo("Bounce"));
            Assert.That(StudioCatalog.SlotFromName("my sound.wav"), Is.EqualTo(""));
            Assert.That(StudioCatalog.OfficialSounds().Count, Is.EqualTo(34));
            Assert.That(StudioCatalog.Sections.Select(s => s.name), Is.EqualTo(new[] { "SHIELD", "THROWS", "BALL", "HITS", "MATCH", "CROWD" }));
            // the choices for an event: OFFICIAL, the library's for it, yours for it, yours without a slot, the rest, the game's own
            var lib = new List<StudioSound>
            {
                new StudioSound { file = "Bounce__b.wav", eventName = "Bounce", ready = true },                       // 0: the library's, another slot
                new StudioSound { file = "ShieldBreak__a.wav", eventName = "ShieldBreak", ready = true },             // 1: the library's, this slot
                new StudioSound { file = "mine:S12_x.wav", eventName = "ShieldBreak", ready = true, mine = true },    // 2: yours, this slot
                new StudioSound { file = "mine:free.wav", eventName = "", ready = true, mine = true },                // 3: yours, no slot
                new StudioSound { file = "ShieldBreak__c.wav", eventName = "ShieldBreak", ready = false },            // 4: not downloaded
                new StudioSound { file = "Grab__g.wav", eventName = "Grab", ready = true },                           // 5: the library's, another slot (S03)
            };
            lib.AddRange(StudioCatalog.OfficialSounds());                                                            // 6..: the game's own
            List<int> ch = StudioCatalog.ChoicesFor("ShieldBreak", lib);
            Assert.That(ch.Take(6), Is.EqualTo(new[] { -1, 1, 2, 3, 5, 0 }), "OFFICIAL, this slot's, yours, your free one, then the rest by slot code (S03 before S10)");
            Assert.That(ch.Count, Is.EqualTo(6 + 33), "then the game's own of the other 33 slots (not its own slot: that is OFFICIAL)");
            Assert.That(lib[ch[6]].eventName, Is.EqualTo("Spawn"), "the game's own in the catalog's order: S01 first");
        }

        [Test]
        public void MineIsYourOwnWavFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_mine_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(StudioCatalog.MySounds(dir), Is.Empty);
                Assert.That(File.Exists(Path.Combine(dir, "README.txt")), "the folder made, with a note saying what goes in it");
                File.WriteAllBytes(Path.Combine(dir, "S12_glass.wav"), new byte[50]);
                File.WriteAllBytes(Path.Combine(dir, "Big_Bang.WAV"), new byte[50]);
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
                var v = StudioCatalog.MySounds(dir);
                Assert.That(v.Select(s => s.name), Is.EqualTo(new[] { "BIG BANG", "S12 GLASS" }), "WAV only (any case), sorted, named for the page");
                Assert.That(v[1].eventName, Is.EqualTo("ShieldBreak"), "a slot's code in front puts it in that slot");
                Assert.That(v[0].eventName, Is.EqualTo(""), "any other name: every slot");
                Assert.That(v.All(s => s.mine && s.ready && s.file.StartsWith("mine:")));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void TheWavReaderTakesWhatEditorsExport()
        {
            float Saw(int f, int c) => ((f % 50) / 50f - 0.5f) * (c == 0 ? 1f : -1f);
            foreach (var (bits, isFloat, ext) in new[] { (8, false, false), (16, false, false), (24, false, false), (32, false, false), (32, true, false), (24, false, true) })
            {
                Assert.That(WavReader.Parse(Wav(48000, 2, bits, isFloat, 200, Saw, ext), out float[] s, out int ch, out int rate), $"{bits}-bit{(isFloat ? " float" : "")}{(ext ? " extensible" : "")}");
                Assert.That(ch == 2 && rate == 48000 && s.Length == 400);
                float tol = bits == 8 ? 0.01f : 1e-3f;
                Assert.That(s[2 * 30], Is.EqualTo(Saw(30, 0)).Within(tol), "the left channel's samples");
                Assert.That(s[2 * 30 + 1], Is.EqualTo(Saw(30, 1)).Within(tol), "the right's");
            }
            Assert.That(WavReader.Parse(Wav(44100, 6, 16, false, 100, (f, c) => c * 0.1f), out float[] m, out int mc, out _));
            Assert.That(mc == 1 && m.Length == 100, "six channels mixed down to one");
            Assert.That(m[10], Is.EqualTo(0.25f).Within(1e-3f), "their average");
            float[] r = WavReader.Resample(new float[] { 0, 1, 0, 1 }, 1, 24000, 48000);
            Assert.That(r.Length, Is.EqualTo(8), "24 kHz to 48: twice the samples");
            Assert.That(r[1], Is.EqualTo(0.5f).Within(1e-6f), "linear in between");
            Assert.That(WavReader.Parse(new byte[60], out _, out _, out _), Is.False, "not a WAV: refused");
            Assert.That(WavReader.Parse(Wav(4000, 1, 16, false, 10, (f, c) => 0), out _, out _, out _), Is.False, "too slow a rate: refused");
        }

        [Test]
        public void TheMixerSwapsAnEventsSoundInTheStudioOnly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_mix_test_" + Guid.NewGuid().ToString("N"));
            var go = new GameObject("mixer test");
            try
            {
                Directory.CreateDirectory(dir);
                var mx = go.AddComponent<NativeMixer>();
                mx.Init("ShieldBreak shield_break\nGrab grab\n", new AudioClip[0]);
                Assert.That(mx.OfficialClip("ShieldBreak"), Is.EqualTo("shield_break"), "events.txt's clip for an event");
                string wav = Path.Combine(dir, "glass.wav");
                File.WriteAllBytes(wav, Wav(22050, 1, 16, false, 2205, (f, c) => 0.5f));
                Assert.That(mx.LoadFile("mine/glass.wav", wav), "a .wav file loads as a clip");
                Assert.That(mx.HasClip("mine/glass.wav"));
                File.WriteAllText(Path.Combine(dir, "bad.wav"), "not a wav");
                Assert.That(mx.LoadFile("mine/bad.wav", Path.Combine(dir, "bad.wav")), Is.False, "a file that isn't a WAV is refused");
                mx.SetOverride("ShieldBreak", "mine/glass.wav");
                Assert.That(mx.OverrideOf("ShieldBreak"), Is.EqualTo("mine/glass.wav"));
                mx.SetOverride("ShieldBreak", "");
                Assert.That(mx.OverrideOf("ShieldBreak"), Is.EqualTo(""), "the game's own again");
                mx.SetOverride("Grab", "mine/glass.wav");
                mx.ClearOverrides();
                Assert.That(mx.OverrideOf("Grab"), Is.EqualTo(""), "leaving the studio clears them all");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TheStudiosMomentIsEndless()
        {
            var g = new Game();
            g.SetArena(ArenaCollision.Load(File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            var tr = new Sim.Training.Trainer();
            int li = Sim.Training.Lessons.ById(1);
            tr.Begin(g, li, ArenaCollision.Load(File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            tr.Start(g);
            tr.endless = true;
            foreach (var x in tr.targets) { x.down = true; x.downT = 1f; }
            tr.successes = 3; tr.attempts = 4;
            typeof(Sim.Training.Trainer).GetMethod("Finish", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tr, new object[] { true });
            Assert.That(tr.phase, Is.Not.EqualTo(Sim.Training.Phase.Done), "no result: the moment goes on");
            Assert.That(tr.targets.All(x => !x.down), "the targets come back up");
            Assert.That(tr.successes == 0 && tr.attempts == 0, "nothing kept");
        }

        [Test, Timeout(120000)]
        public void TheLibraryComesFromTheServerAndVotesReachIt()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pa_studio_test_" + Guid.NewGuid().ToString("N"));
            string game = Path.Combine(dir, "game");
            Process relay = RoomTests.StartRelay(dir, RelayPort, CrowdPort);
            if (relay == null) Assert.Ignore("no local relay (Python 3.8+ and the native repo's server/projectarena_relay.py needed)");
            var node = new Node("STUDIO VOTER", 0f);
            try
            {
                string server = $"127.0.0.1:{RelayPort}", lib = Path.Combine(dir, "sound_library");   // (the relay's --sounds: its working folder's)
                Directory.CreateDirectory(lib);
                byte[] wav = Wav(48000, 1, 16, false, 4800, (f, c) => Mathf.Sin(f * 0.05f) * 0.3f);
                File.WriteAllBytes(Path.Combine(lib, "ShieldBreak__glass_test.wav"), wav);
                var key = new byte[16];
                for (int i = 0; i < 16; ++i) key[i] = (byte)(i + 7);
                var library = new SoundLibrary();
                library.Sync(server, Path.Combine(game, "sound_library"), key);
                library.Wait();
                Assert.That(library.CurrentState, Is.EqualTo(SoundLibrary.State.Ready), library.Error);
                Assert.That(library.Take(out List<StudioSound> got), "the synced sounds, taken once");
                Assert.That(got.Count == 1 && got[0].ready && got[0].eventName == "ShieldBreak" && got[0].name == "GLASS TEST", "the library's sound, downloaded");
                string mine = Path.Combine(game, "sound_library", "ShieldBreak__glass_test.wav");
                Assert.That(StudioCatalog.Sha256Hex(File.ReadAllBytes(mine)), Is.EqualTo(StudioCatalog.Sha256Hex(wav)), "the same bytes (checked by SHA-256)");
                // a vote, sent the next time the game is online
                node.net.SetStatsKey(key);
                Assert.That(node.net.SendSoundVote("ShieldBreak__glass_test.wav", 1));
                Assert.That(node.net.SoundVotesQueued, Is.EqualTo(1), "kept until the server can take it");
                node.net.GoOnline(server);
                RoomTests.Pump(new List<Node> { node }, 2.0);
                Assert.That(node.net.SoundVotesQueued, Is.EqualTo(0), "sent once online");
                string totals = RelayCommand(dir, "--sound-totals");
                TestContext.Out.WriteLine(totals);
                Assert.That(totals, Does.Contain("ShieldBreak__glass_test.wav").And.Contain("up    1"), "the community's count has it");
                // gone from the server: gone from here too
                File.Delete(Path.Combine(lib, "ShieldBreak__glass_test.wav"));
                library.Sync(server, Path.Combine(game, "sound_library"), key);
                library.Wait();
                Assert.That(library.Take(out got) && got.Count == 0, "an empty library");
                Assert.That(File.Exists(mine), Is.False, "and the sound deleted here");
            }
            finally
            {
                if (node.net.Online) node.net.GoOffline();
                node.net.Shutdown();
                try { relay.Kill(); } catch (Exception) { }
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        // the owner's command on the server (the relay's --sound-totals), on the test's own folder
        static string RelayCommand(string dir, string args)
        {
            string relay = Path.Combine(RoomTests.NativeRepo, "server", "projectarena_relay.py");
            foreach (string py in new[] { "-3.12", "-3.13", "-3.10", "-3.9" })
            {
                try
                {
                    var psi = new ProcessStartInfo("py", $"{py} -I \"{relay}\" --stats \"{Path.Combine(dir, "stats.db")}\" --sounds \"{Path.Combine(dir, "sound_library")}\" {args}")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir };
                    using (var p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(20000);
                        if (p.ExitCode == 0) return o;
                    }
                }
                catch (Exception) { }
            }
            return "";
        }
    }
}
