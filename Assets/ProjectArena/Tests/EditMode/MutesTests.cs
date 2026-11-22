// MUTED PLAYERS (TASKS.md E46, native 43397af keptMute / toggleMute / loadMutes / saveMutes; Runtime/Mutes.cs): a mute is
// kept in mutes.cfg, by the server's code when both are known, else by name; UNMUTE takes it away
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace ProjectArena.Tests
{
    public class MutesTests
    {
        [Test]
        public void KeptByCodeOrName()
        {
            string folder = Path.Combine(Path.GetTempPath(), "pa_mutes_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var m = new Mutes();
                m.Load(folder);
                var ids = new List<uint>();
                m.Toggle(7, "ALICE", null, ids);   // (no network: by name)
                Assert.That(ids.Contains(7) && m.list.Count == 1 && m.list[0].name == "ALICE" && m.list[0].code == 0, "MUTE: for now and kept, by name");
                Assert.That(m.Kept(0, "ALICE") == 0 && m.Kept(0, "BOB") < 0, "the name covers them");
                m.list[0].code = 0x1234;
                Assert.That(m.Kept(0x1234, "SOMEONE ELSE") == 0, "a known code covers them under a new name");
                Assert.That(m.Kept(0x9999, "ALICE") < 0, "a different person with that name (both codes known): not them");
                Assert.That(m.Kept(0, "ALICE") == 0, "their code not known here: by name");
                var back = new Mutes();
                back.Load(folder);
                Assert.That(back.list.Count == 1 && back.list[0].name == "ALICE", "kept in mutes.cfg");
                File.WriteAllText(Path.Combine(folder, "mutes.cfg"), "mute=00000000000004d2 BOB\nmute=0000000000000000 CAROL\njunk\nmute=zz\n");
                back.Load(folder);
                Assert.That(back.list.Count == 2 && back.list[0].code == 0x4d2 && back.list[0].name == "BOB" && back.list[1].name == "CAROL",
                            "mutes.cfg read back: 'mute=CODE NAME' lines, the rest skipped");
                m.Toggle(7, "ALICE", null, ids);
                Assert.That(!ids.Contains(7) && m.Kept(0, "ALICE") < 0, "UNMUTE on REPORT PLAYER: for now and kept");
                m.Toggle(8, "DAVE", null, ids);
                m.Unmute(0, null, ids);
                Assert.That(m.list.Count == 0 && ids.Count == 0, "MUTED PLAYERS > UNMUTE: the kept mute goes, and so does who it covered here");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }
    }
}
