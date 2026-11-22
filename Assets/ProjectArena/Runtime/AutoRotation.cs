// AUTO ROTATION on the PC spectator (TASKS.md E6, native app/src/auto_rotation.h, 0.3.20; owner, 2026-11-12: "a mix of
// presets and your own cameras"): one list of every preset camera and every saved camera that isn't an action camera,
// each with an IN AUTO tick and a hold time; AUTO plays the ticked ones in the list's order. Until the streamer changes
// the list (custom), its ticks are what AUTO played before: the presets (side, blue's eyes, blue end, high, orange's
// eyes, orange end; 9 s each) while no camera of yours is in turn, your cameras only from then on. Action cameras are
// not in the list: they still cut in on hits. Kept free of the engine so the tests can run it (AutoRotationTests).
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectArena
{
    public static class AutoRotation
    {
        public sealed class Item
        {
            public bool mine;        // one of MY CAMERAS (id: its number from 0), or a preset (id: the view, 1-7)
            public int id = 1;
            public bool on = true;   // IN AUTO
            public float hold = 9f;  // a preset's seconds (a camera of yours keeps its own hold, shared with the headset)
            public Item Clone() => (Item)MemberwiseClone();
        }

        // the presets in the order AUTO played them (1 side, 6 blue's eyes, 3 blue end, 2 high, 7 orange's eyes, 4 orange
        // end), then 5 TOP, which it never played; then 8 CORNER and 9 COURTSIDE (TASKS.md E42, native 7240d49), in AUTO,
        // and unticked in a list the streamer had made before them; then the follow views, 10 TRACKING, 11 BALL CAM, 12
        // UNDER FIRE (TASKS.md E57, native 5bfc546), in the list but not ticked until the streamer ticks them (AUTO plays
        // as it did)
        public static readonly int[] Presets = { 1, 6, 3, 2, 7, 4, 5, 8, 9, 10, 11, 12 };
        public const int TopPreset = 5;   // (the one AUTO leaves out)
        static readonly float[] PresetHolds = { 2, 4, 9, 15 };
        public const int MaxView = 12;
        public static bool OffAtFirst(int id) => id == TopPreset || id >= 10;   // (in the list, not ticked)

        public static bool Same(List<Item> a, List<Item> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; ++i)
                if (a[i].mine != b[i].mine || a[i].id != b[i].id || a[i].on != b[i].on || a[i].hold != b[i].hold) return false;
            return true;
        }

        public static int RowOf(List<Item> L, bool mine, int id)
        {
            for (int i = 0; i < L.Count; ++i)
                if (L[i].mine == mine && L[i].id == id) return i;
            return -1;
        }

        // the list made whole for the cameras there are now (trig: each of my cameras' type, 0 timer, 1 action, 2 follow):
        // every preset once, each camera of yours that isn't an action camera once (a new one at the end, ticked), none
        // that is gone. Not custom: the ticks are AUTO's old ones, your cameras in their numbers' order. True if anything
        // changed
        public static bool Sync(List<Item> L, bool custom, IList<byte> trig)
        {
            var before = new List<Item>();
            foreach (Item it in L) before.Add(it.Clone());
            var kept = new List<Item>();
            foreach (Item it in L)
            {
                if (it.mine ? it.id < 0 || it.id >= trig.Count || trig[it.id] == 1 : it.id < 1 || it.id > MaxView) continue;
                if (RowOf(kept, it.mine, it.id) >= 0) continue;   // (once each)
                kept.Add(it);
            }
            L.Clear();
            L.AddRange(kept);
            foreach (int p in Presets)
                if (RowOf(L, false, p) < 0) L.Add(new Item { id = p, on = !OffAtFirst(p) && !custom });
            for (int i = 0; i < trig.Count; ++i)
                if (trig[i] != 1 && RowOf(L, true, i) < 0) L.Add(new Item { mine = true, id = i });
            if (!custom)
            {
                bool anyMine = false;
                foreach (byte t in trig) anyMine |= t != 1;
                var presets = new List<Item>();
                var mineL = new List<Item>();
                foreach (Item it in L)
                {
                    it.on = it.mine || (!anyMine && !OffAtFirst(it.id));
                    (it.mine ? mineL : presets).Add(it);
                }
                mineL.Sort((x, y) => x.id.CompareTo(y.id));   // (by number, as AUTO went round them)
                L.Clear();
                L.AddRange(presets);
                L.AddRange(mineL);
            }
            return !Same(before, L);
        }

        // the next ticked row after row `from` (-1: from the top), round to the start; -1 if none is ticked
        public static int Next(List<Item> L, int from)
        {
            int n = L.Count;
            for (int k = 1; k <= n; ++k)
            {
                int i = ((from < 0 ? -1 : from) + k) % n;
                if (L[i].on) return i;
            }
            return -1;
        }

        // a preset's next hold time (2, 4, 9, 15 s, round again)
        public static float NextHold(float h)
        {
            for (int i = 0; i < 4; ++i)
                if (h < PresetHolds[i] - 0.01f) return PresetHolds[i];
            return PresetHolds[0];
        }

        // autocams.cfg: written once the streamer has changed the list; one row a line, "p 3 1 9" (preset 3, ticked,
        // 9 s), "m 0 1" (my camera 1, ticked)
        public static string Save(List<Item> L)
        {
            var s = new StringBuilder();
            foreach (Item it in L)
            {
                if (it.mine) s.Append($"m {it.id} {(it.on ? 1 : 0)}\n");
                else s.Append($"p {it.id} {(it.on ? 1 : 0)} {it.hold.ToString("0", CultureInfo.InvariantCulture)}\n");
            }
            return s.ToString();
        }

        public static List<Item> Load(string s)
        {
            var L = new List<Item>();
            foreach (string raw in (s ?? "").Split('\n'))
            {
                if (L.Count >= 32) break;
                string[] w = raw.Trim().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (w.Length < 3 || (w[0] != "p" && w[0] != "m")) continue;
                if (!int.TryParse(w[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                    !int.TryParse(w[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int on)) continue;
                float hold = 9f;
                if (w.Length >= 4 && float.TryParse(w[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float h)) hold = h;
                L.Add(new Item { mine = w[0] == "m", id = id, on = on != 0, hold = hold >= 1f && hold <= 60f ? hold : 9f });
            }
            return L;
        }
    }
}
