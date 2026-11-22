// Same-results check in the editor: the scenarios of tools/sim_compare/trace.cpp (SimCompare/Scenarios.cs) against
// the native traces (tools/sim_compare/traces/*.trace.gz). Events, match states, ball states and scores must match
// exactly, floats within 1 mm / 1e-3.
// The editor's Mono keeps some float steps in double precision where C++ rounds every step, so results differ in the
// last bits and a long chaotic rally can drift apart (the hotdog scenario does after ~1,400 frames). The bit-for-bit
// check of all scenarios runs on IL2CPP, the compiler the game ships with: tools/sim_compare/compare.py.
// diamond_vs_bot: a glancing knuckle-guard block at frame 1274 turns the bot hand's last-bit difference (1e-7) into
// 1.6 mm for a while; every event, state and score still matches, and on IL2CPP it is bit-identical (2026-11-04).
// fourcorners_ffa and fourcorners_2v2: four bots' rallies magnify Mono's rounding faster (up to 17 mm and 3 mm); every
// event, state and score still matches, and on IL2CPP both are bit-identical (2026-11-04).
// blitz_champion: four CHAMPION bots aim each fireball by hundreds of tryouts, so Mono's rounding tips a bot's grab of its
// ball by a frame at frame 983; the editor checks the first 982 frames (1,170 lines) exactly, and on IL2CPP all 2,351
// lines are bit-identical (2026-11-04).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using ProjectArena.Sim;
using ProjectArena.SimCompare;

namespace ProjectArena.Tests
{
    public class SameResultsTests
    {
        const string ColPath = "Assets/ProjectArena/Art/Open/Data/arena.col.bytes";
        const string Traces = "tools/sim_compare/traces/";
        const float Tolerance = 1e-3f;
        static float ToleranceFor(string scenario) =>
            scenario == "diamond_vs_bot" ? 2e-3f : scenario == "fourcorners_2v2" ? 5e-3f : scenario == "fourcorners_ffa" ? 2.5e-2f : Tolerance;

        // the lines the editor can check (Mono, see above); the rest: tools/sim_compare/compare.py on IL2CPP
        // (blitz_pro, with the traces of native 6def918: at line 1079 a bot's grab of the rising ball sits on the reach's
        // edge and Mono's floats take it a frame later; IL2CPP: bit-identical, 2026-11-20)
        static int EditorLines(string scenario) => scenario == "blitz_champion" ? 1170 : scenario == "blitz_pro" ? 1070 : int.MaxValue;

        static float F(string hex) => BitConverter.ToSingle(BitConverter.GetBytes(uint.Parse(hex, NumberStyles.HexNumber)), 0);

        // the traces come from today's native game (traces/SOURCE.txt): "people" has no bot (the rules alone); the bot at
        // every kind of level: PRO (the default), ROOKIE's straight-line reads, ALL-STAR's planned shots, CHAMPION's and
        // UNBEATABLE's tryouts, attack plan and slap shots; the EXPERIMENTAL arenas for two: DIAMOND (the bot's banks off
        // its rolled walls; solid platforms), CORNERS; FOUR CORNERS' four bots in each team mode; FIREBALL BLITZ's bots (PRO,
        // CHAMPION's curves and wall banks, three of four places with RAPID FIRE and shields)
        [TestCase("people")]
        [TestCase("bots")]
        [TestCase("basic_vs_bot")]
        [TestCase("advanced_vs_bot")]
        [TestCase("rookie_vs_bot")]
        [TestCase("champion_vs_bot")]
        [TestCase("unbeatable_bots")]
        [TestCase("allstar_hotdog")]
        [TestCase("diamond_vs_bot")]
        [TestCase("diamond_people_solid")]
        [TestCase("corners_vs_bot")]
        [TestCase("fourcorners_ffa")]
        [TestCase("fourcorners_2v2")]
        [TestCase("fourcorners_3v1")]
        [TestCase("blitz_pro")]
        [TestCase("blitz_champion")]
        [TestCase("blitz_rapid_three")]
        public void MatchesNative(string scenario)
        {
            List<string> lines = Scenarios.Play(scenario, ArenaCollision.Load(File.ReadAllBytes(ColPath)));
            var native = new List<string>();
            using (var z = new GZipStream(File.OpenRead(Traces + scenario + ".trace.gz"), CompressionMode.Decompress))
            using (var r = new StreamReader(z))
                for (string l; (l = r.ReadLine()) != null;) native.Add(l);

            int exact = 0;
            float worst = 0;
            int n = Math.Min(Math.Min(native.Count, lines.Count), EditorLines(scenario));
            for (int i = 0; i < n; ++i)
            {
                if (native[i] == lines[i]) { ++exact; continue; }
                string[] a = native[i].Split(' '), b = lines[i].Split(' ');
                Assert.That(b.Length, Is.EqualTo(a.Length), $"line {i + 1}: different record\n native {native[i]}\n C#     {lines[i]}");
                for (int k = 0; k < a.Length; ++k)
                {
                    if (a[k] == b[k]) continue;
                    bool isFloat = a[k].Length == 8 && b[k].Length == 8 && uint.TryParse(a[k], NumberStyles.HexNumber, null, out _) &&
                                   uint.TryParse(b[k], NumberStyles.HexNumber, null, out _);
                    Assert.That(isFloat, $"line {i + 1}, field {k}: {a[k]} vs {b[k]} (states, scores and events must match)\n native {native[i]}\n C#     {lines[i]}");
                    float d = MathF.Abs(F(a[k]) - F(b[k]));
                    worst = Math.Max(worst, d);
                    Assert.That(d, Is.LessThanOrEqualTo(ToleranceFor(scenario)), $"line {i + 1}, field {k}: {F(a[k])} vs {F(b[k])}\n native {native[i]}\n C#     {lines[i]}");
                }
            }
            TestContext.Out.WriteLine($"{scenario}: {lines.Count} lines; {exact} bit-identical, largest float difference {worst}");
            if (EditorLines(scenario) == int.MaxValue) Assert.That(lines.Count, Is.EqualTo(native.Count), "as many frames and events as the native run");
        }
    }
}
