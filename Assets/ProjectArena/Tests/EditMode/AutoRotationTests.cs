// AUTO ROTATION's list (TASKS.md E6; Runtime/AutoRotation.cs, native app/tests/test_autorot.cpp, its 24 checks): until
// the streamer changes it, AUTO plays what it played before; once changed, the ticked rows in the list's order
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Item = ProjectArena.AutoRotation.Item;

namespace ProjectArena.Tests
{
    public class AutoRotationTests
    {
        // the views AUTO shows, `n` turns from row `from` (presets as their view number, my cameras as 100 + number)
        static List<int> Play(List<Item> L, int from, int n)
        {
            var v = new List<int>();
            for (int k = 0; k < n; ++k)
            {
                from = AutoRotation.Next(L, from);
                if (from < 0) break;
                v.Add(L[from].mine ? 100 + L[from].id : L[from].id);
            }
            return v;
        }

        static byte[] T(params byte[] t) => t;

        [Test]
        public void TheListAsNative()
        {
            int passes = 0;
            void Check(bool ok, string what) { Assert.That(ok, what); ++passes; }
            {   // no cameras of yours: the presets in AUTO's old order, 9 s each, TOP not played
                var L = new List<Item>();
                Check(AutoRotation.Sync(L, false, T()), "a new list is made");
                Check(L.Count == 12, "twelve presets");
                Check(!L[AutoRotation.RowOf(L, false, 10)].on && !L[AutoRotation.RowOf(L, false, 11)].on && !L[AutoRotation.RowOf(L, false, 12)].on,
                      "the follow views are in the list, not ticked");   // (TASKS.md E57, native 5bfc546)
                Check(Play(L, AutoRotation.RowOf(L, false, 1), 8).SequenceEqual(new[] { 6, 3, 2, 7, 4, 8, 9, 1 }),
                      "side, blue's eyes, blue end, high, orange's eyes, orange end, corner, courtside");
                Check(!L[AutoRotation.RowOf(L, false, 5)].on, "TOP is in the list, not ticked");
                Check(L.All(it => it.hold == 9f), "each preset 9 s");
                Check(!AutoRotation.Sync(L, false, T()), "nothing changes the second time");
            }
            {   // only action cameras: still the presets (the action cameras cut in on hits, not in the list)
                var L = new List<Item>();
                AutoRotation.Sync(L, false, T(1, 1));
                Check(L.Count == 12 && Play(L, -1, 1).SequenceEqual(new[] { 1 }), "action cameras are not in the list");
            }
            {   // a camera of yours in turn: from then on only yours, by number, as before
                var L = new List<Item>();
                AutoRotation.Sync(L, false, T());
                AutoRotation.Sync(L, false, T(0, 1, 2));
                Check(L.Count == 14, "two of yours added (not the action one)");
                Check(Play(L, -1, 4).SequenceEqual(new[] { 100, 102, 100, 102 }), "yours only, in turn");
                AutoRotation.Sync(L, false, T());
                Check(Play(L, -1, 1).SequenceEqual(new[] { 1 }), "cleared: the presets again");
            }
            {   // not changed yet: a camera turned from action to timer goes back to its number's place
                var L = new List<Item>();
                AutoRotation.Sync(L, false, T(0, 1, 0));
                AutoRotation.Sync(L, false, T(0, 0, 0));
                Check(Play(L, -1, 3).SequenceEqual(new[] { 100, 101, 102 }), "by number");
            }
            {   // the streamer's list: a preset and a camera, then a new camera (at the end, ticked)
                var L = new List<Item>();
                AutoRotation.Sync(L, false, T(0));
                foreach (Item it in L) it.on = (!it.mine && it.id == 3) || (it.mine && it.id == 0);
                AutoRotation.Sync(L, true, T(0));
                Check(Play(L, -1, 4).SequenceEqual(new[] { 3, 100, 3, 100 }), "blue end and camera 1 in turn");
                AutoRotation.Sync(L, true, T(0, 2));
                Check(L.Last().mine && L.Last().id == 1 && L.Last().on, "a new camera at the end, ticked");
                Check(Play(L, AutoRotation.RowOf(L, true, 0), 3).SequenceEqual(new[] { 101, 3, 100 }), "and in the turn");
                int r3 = AutoRotation.RowOf(L, false, 3);
                (L[0], L[r3]) = (L[r3], L[0]);
                Check(Play(L, -1, 1).SequenceEqual(new[] { 3 }), "moved to the top: first");
                AutoRotation.Sync(L, true, T(1, 2));
                Check(AutoRotation.RowOf(L, true, 0) < 0, "made an action camera: out of the list");
                AutoRotation.Sync(L, true, T(0, 2));
                Check(L.Last().mine && L.Last().id == 0, "a timer again: back, at the end");
                foreach (Item it in L) it.on = false;
                Check(AutoRotation.Next(L, -1) < 0, "nothing ticked: nothing to play");
            }
            {   // saved and read back; a bad line or a hold out of range is not taken
                var L = new List<Item>();
                AutoRotation.Sync(L, false, T(0, 0));
                L[2].on = false; L[0].hold = 15f;
                (L[1], L[8]) = (L[8], L[1]);
                List<Item> R = AutoRotation.Load(AutoRotation.Save(L) + "x 1 1\nm\np 2 1 500\n");
                Check(R.Count == 15, "every row back, the bad ones skipped but the hold kept to a sane one");
                R.RemoveAt(R.Count - 1);
                Check(AutoRotation.Same(L, R), "the same list");
                Check(AutoRotation.Load("p 2 1 500\n")[0].hold == 9f, "a hold out of range: 9 s");
                List<Item> D = AutoRotation.Load("p 1 1 9\np 1 0 9\nm 7 1\n");
                AutoRotation.Sync(D, true, T(0));
                Check(AutoRotation.RowOf(D, false, 1) == 0 && D[0].on && AutoRotation.RowOf(D, true, 7) < 0, "a row twice: the first; a camera that is gone: out");
                Check(D.Count == 13, "the missing presets and camera added");
            }
            {   // a list the streamer made before CORNER and COURTSIDE: the two at its end, unticked (their AUTO stays as it was)
                List<Item> O = AutoRotation.Load("p 3 1 9\np 1 1 4\np 6 0 9\np 2 1 9\np 7 0 9\np 4 1 9\np 5 0 9\n");
                Check(AutoRotation.Sync(O, true, T()), "the two added");
                Check(O.Count == 12 && O[7].id == 8 && O[8].id == 9 && !O[7].on && !O[8].on, "at the end, unticked");
                Check(O[9].id == 10 && O[10].id == 11 && O[11].id == 12 && !O[9].on && !O[10].on && !O[11].on, "the follow views too");
                Check(Play(O, -1, 4).SequenceEqual(new[] { 3, 1, 2, 4 }), "AUTO as the streamer had it");
            }
            Check(AutoRotation.NextHold(2) == 4 && AutoRotation.NextHold(4) == 9 && AutoRotation.NextHold(9) == 15 && AutoRotation.NextHold(15) == 2, "holds 2, 4, 9, 15 s");
            Assert.That(passes, Is.EqualTo(29), "native's 29 checks (24, CORNER and COURTSIDE's three, native 7240d49, and THE FOLLOW VIEWS' two, native 5bfc546)");
        }
    }
}
