// FOUL LINE on the court (TASKS.md E3, native 0.3.20 drawCourtModel; Runtime/CourtStretch.cs): the court's sections told
// apart by their bounds, and where each is drawn with the line moved
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectArena.Tests
{
    public class CourtStretchTests
    {
        [Test]
        public void TheLineTheEndsAndTheMiddle()
        {   // native's matrices: the line moved, each end from the new line to its back wall, the middle with them
            Assert.That(CourtStretch.MoveX(-4.75f, -CourtStretch.PartLine, 1f), Is.EqualTo(-3.75f).Within(1e-5f), "LONGER 2: the line 1 m toward the centre");
            Assert.That(CourtStretch.MoveX(4.75f, CourtStretch.PartLine, -1f), Is.EqualTo(5.75f).Within(1e-5f), "SHORTER 2: 1 m back");
            Assert.That(CourtStretch.MoveX(-9.5f, -CourtStretch.PartEnd, 1f), Is.EqualTo(-9.5f).Within(1e-5f), "an end's back wall stays");
            Assert.That(CourtStretch.MoveX(-4.75f, -CourtStretch.PartEnd, 1f), Is.EqualTo(-3.75f).Within(1e-5f), "... its front starts at the new line");
            Assert.That(CourtStretch.MoveX(9.5f, CourtStretch.PartEnd, -1f), Is.EqualTo(9.5f).Within(1e-5f), "... either way");
            Assert.That(CourtStretch.MoveX(4.75f, CourtStretch.PartEnd, -1f), Is.EqualTo(5.75f).Within(1e-5f));
            Assert.That(CourtStretch.MoveX(4.75f, CourtStretch.PartMiddle, 1f), Is.EqualTo(3.75f).Within(1e-5f), "the middle out to the new line");
            Assert.That(CourtStretch.MoveX(0f, CourtStretch.PartMiddle, 1f), Is.EqualTo(0f), "... its centre where it is");
            Assert.That(CourtStretch.MoveX(-4.75f, -CourtStretch.PartLine, 0f), Is.EqualTo(-4.75f), "regulation: nothing moves");
            Assert.That(CourtStretch.MoveX(3f, CourtStretch.PartNone, 1f), Is.EqualTo(3f), "the other pieces as they are");
            Assert.That(CourtStretch.Part(-4.85f, -4.65f), Is.EqualTo(-CourtStretch.PartLine), "a thin piece at 4.75 m: the line");
            Assert.That(CourtStretch.Part(4.7f, 9.5f), Is.EqualTo(CourtStretch.PartEnd), "from the line to the back wall: an end");
            Assert.That(CourtStretch.Part(-4.75f, 4.75f), Is.EqualTo(CourtStretch.PartMiddle), "line to line: the middle");
            Assert.That(CourtStretch.Part(-0.1f, 0.1f), Is.EqualTo(CourtStretch.PartNone), "the centre line as it is");
            Assert.That(CourtStretch.Part(9.4f, 9.6f), Is.EqualTo(CourtStretch.PartNone), "the back wall as it is");
            var g = new Sim.Game();
            g.Reset(true);
            g.tun.foulShift = 2;
            Assert.That(CourtStretch.MoveFor(g), Is.EqualTo(1f), "the classic court follows the game's line");
            g.tun.experimental = true; g.tun.expRoom = Sim.Room.Diamond;
            Assert.That(CourtStretch.MoveFor(g), Is.EqualTo(0f), "DIAMOND keeps its own");
        }

        [Test]
        public void TheOpenCourtFollows()
        {   // the open art's court: in each model native moves, the lines, ends and middle found by their bounds
            var found = new Dictionary<string, List<int>>();
            int total = 0;
            foreach (string name in CourtStretch.Models)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ProjectArena/Art/Open/Models/{name}.mdl");
                if (asset == null) continue;
                var go = Object.Instantiate(asset);
                var parts = new List<int>();
                total += CourtStretch.Prepare(go, parts);
                found[name] = parts;
                TestContext.Out.WriteLine($"  {name}: {string.Join(" ", parts)}");
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.EndsWith("(foul line)")) Object.DestroyImmediate(mf.sharedMesh);
                Object.DestroyImmediate(go);
            }
            Assert.That(found.Count, Is.EqualTo(CourtStretch.Models.Length), "every court model is there");
            var a = found["arena"];
            Assert.That(a.Contains(-CourtStretch.PartLine) && a.Contains(CourtStretch.PartLine), "the arena's two foul lines");
            Assert.That(a.Contains(-CourtStretch.PartEnd) && a.Contains(CourtStretch.PartEnd) && a.Contains(CourtStretch.PartMiddle), "its two ends and its middle");
            Assert.That(found["floor"].Contains(CourtStretch.PartMiddle) && found["court_outline"].Contains(CourtStretch.PartLine), "the floor and the outline too");
            var pu = found["court_pulse"];   // (TASKS.md E37, native ec1faef: a blue end, an orange end and the middle)
            Assert.That(pu.Contains(-CourtStretch.PartEnd) && pu.Contains(CourtStretch.PartEnd) && pu.Contains(CourtStretch.PartMiddle), "the court's pulses: its two ends and its middle");
            Assert.That(total, Is.EqualTo(33 + pu.FindAll(p => p != CourtStretch.PartNone).Count), "the sections that follow the line (the open art: arena 8, floor 4, floor_reflect 8, court_fx 8, court_outline 5, and the pulses)");
        }
    }
}
