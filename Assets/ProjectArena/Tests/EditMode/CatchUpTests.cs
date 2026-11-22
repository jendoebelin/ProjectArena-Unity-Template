// Native's older changes caught up (TASKS.md E62): ARENA GLASS's steps and its setting (native f7b9cc9), FIREBALL
// BLITZ's moves through dark (native 123205c startBlink).
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public class CatchUpTests
    {
        [Test]
        public void ArenaGlassSteps()
        {
            Assert.That(DesktopSpectator.NextGlass(1f), Is.EqualTo(0.35f), "NORMAL -> FAINT (35%)");
            Assert.That(DesktopSpectator.NextGlass(0.35f), Is.EqualTo(0.2f), "FAINT -> FAINTER (20%)");
            Assert.That(DesktopSpectator.NextGlass(0.2f), Is.EqualTo(1f), "FAINTER -> NORMAL");
            Assert.That(DesktopSpectator.GlassName(0.35f), Is.EqualTo("faint (35%)"));
            string folder = Path.Combine(Path.GetTempPath(), "pa_glass_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var s = new AppSettings(folder);
                s.Load(new Tunables());
                Assert.That(s.dcGlass, Is.EqualTo(1f), "NORMAL to start");
                s.dcGlass = 0.2f;
                s.Save(new Tunables());
                var back = new AppSettings(folder);
                back.Load(new Tunables());
                Assert.That(back.dcGlass, Is.EqualTo(0.2f).Within(1e-4f), "kept on this PC (dcglass=)");
                File.WriteAllText(Path.Combine(folder, "settings.cfg"), "dcglass=0.01\n");
                var low = new AppSettings(folder);
                low.Load(new Tunables());
                Assert.That(low.dcGlass, Is.EqualTo(0.1f).Within(1e-4f), "never below 10%");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void BlitzMovesThroughDark()
        {
            Assert.That(Blink.Wanted(true, 32u + 15u, 4u) && Blink.Wanted(true, 4u, 32u + 15u) && Blink.Wanted(true, 32u + 15u, 32u + 7u),
                        "FIREBALL BLITZ: to the dugout, back, the room closing in");
            Assert.That(!Blink.Wanted(false, 0u, 3u) && !Blink.Wanted(true, 3u, 32u + 15u), "other moves at once (the lobby to a room)");
            var b = new Blink();
            b.Start();
            bool moved = false;
            int frame = 0, movedAt = -1;
            float maxDark = 0;
            for (; frame < 72; ++frame)
            {
                if (b.Step(1f / 72) && !moved) { moved = true; movedAt = frame; }
                maxDark = UnityEngine.Mathf.Max(maxDark, b.Dark);
            }
            Assert.That(moved && movedAt == 8, $"the move once dark, 0.12 s in (frame {movedAt})");
            Assert.That(maxDark, Is.GreaterThan(0.95f), "fully dark at the move");
            Assert.That(b.Dark, Is.EqualTo(0f), "light again by a second");
        }
    }
}
