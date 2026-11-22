// THE DECK (TASKS.md E40, native fb93914 drawDeck): a solid floor out to the stands, a hole the room's shape
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public class DeckTests
    {
        static bool Covered(List<Vec3> t, float x, float z)
        {
            float Side(Vec3 p, Vec3 q, float rx, float rz) => (q.x - p.x) * (rz - p.z) - (q.z - p.z) * (rx - p.x);
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                float d1 = Side(t[i], t[i + 1], x, z), d2 = Side(t[i + 1], t[i + 2], x, z), d3 = Side(t[i + 2], t[i], x, z);
                if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) return true;
            }
            return false;
        }

        [Test]
        public void SolidOutToTheStandsAHoleTheRoomsShape()
        {
            var t = new List<Vec3>();
            for (int k = 0; k < 4; ++k)
            {
                Deck.Shape(k, t);
                Assert.That(t.Count, Is.EqualTo(k == 3 ? 6 : 48), "the ring (FIREBALL BLITZ: the whole of it)");
                bool up = true, level = true;
                for (int i = 0; i + 2 < t.Count; i += 3) up &= M.Cross(t[i + 1] - t[i], t[i + 2] - t[i]).y >= 0;
                foreach (Vec3 p in t) level &= p.y == Deck.Y;
                Assert.That(up && level, "flat, 3.5 cm under the walkway, facing up");
                float sc = k >= 2 ? 1.35f : 1f;
                Assert.That(Covered(t, 13.0f * sc, 7.0f * sc) && Covered(t, 0, 7.5f * sc) && Covered(t, -13.5f * sc, 0), "out to the stands' front");
                Assert.That(Covered(t, 0, 0), Is.EqualTo(k == 3), k == 3 ? "FIREBALL BLITZ's room stands on it" : "the room's hole");
            }
            Deck.Shape(0, t);
            Assert.That(!Covered(t, 9.0f, 2.3f) && Covered(t, 9.0f, 2.8f), "the SQUARE's hole: its court (9.5 x 2.5 m)");
            Deck.Shape(2, t);
            Assert.That(!Covered(t, 9.3f, 0) && Covered(t, 9.8f, 0) && Covered(t, 6f, 6f), "FOUR CORNERS': a turned square, corners at 9.5 m on the axes");
            var g = new Game();
            g.Reset(true);
            Assert.That(Deck.KindOf(g), Is.EqualTo(0), "the SQUARE");
        }
    }
}
