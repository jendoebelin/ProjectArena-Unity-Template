// BUTTONS (SETTINGS > CONTROLLERS > BUTTONS; TASKS.md E20 / E21, native controls.h, eeb17b7): which job each controller
// button does. A player's report, 2026-11-17: let us change the push-to-talk button, and the controls in general; the
// owner: a page to remap them, with a LEFT-HANDED preset, and a grip may do push to talk or another job that does no harm
// squeezed by accident, or nothing.
// The trigger (grab, throw, point at the menu) and the menu button stay where they are. A button does one job (or none);
// a job may have several buttons. TALK, READY and RECENTER always keep at least one button the controllers in your hands
// have. Your own headset's choice (settings.cfg buttons=): nothing is sent, every version unaffected.
namespace ProjectArena
{
    public static class Controls
    {
        // The buttons, by their Quest Touch names (other controllers: SlotName)
        public const int S_LGRIP = 0, S_RGRIP = 1, S_A = 2, S_B = 3, S_X = 4, S_Y = 5, S_LSTICK = 6, S_RSTICK = 7, S_COUNT = 8;
        // The jobs: NONE, TALK (held: push to talk), PEEK (held: PASSTHROUGH > PEEK), NAMES (PLAYERS' NAMES on / off),
        // READY (READY UP, a lesson's A), RECENTER
        public const int J_NONE = 0, J_TALK = 1, J_PEEK = 2, J_NAMES = 3, J_READY = 4, J_RECENTER = 5, J_COUNT = 6;
        // The controllers in your hands (the OpenXR interaction profile; UNKNOWN until it's known: every button)
        public const int K_UNKNOWN = 0, K_TOUCH = 1, K_INDEX = 2, K_VIVE = 3, K_WMR = 4;

        public sealed class Map
        {
            public readonly int[] job = new int[S_COUNT];
            public bool leftSticks;   // spectating: the left stick turns and the right moves (the right turns as standard)

            public Map() { }
            public Map(int[] jobs, bool leftSticks) { System.Array.Copy(jobs, job, S_COUNT); this.leftSticks = leftSticks; }
            public Map Clone() => new Map(job, leftSticks);
        }

        public static bool SameMap(Map p, Map q)
        {
            for (int s = 0; s < S_COUNT; ++s)
                if (p.job[s] != q.job[s]) return false;
            return p.leftSticks == q.leftSticks;
        }

        // As the game always had it: the left grip talks, A and X ready up, B and the right stick's click recenter, Y peeks,
        // the left stick's click shows the players' names
        public static Map Standard() => new Map(new[] { J_TALK, J_NONE, J_READY, J_RECENTER, J_READY, J_PEEK, J_NAMES, J_RECENTER }, false);

        static readonly int[] kMirror = { S_RGRIP, S_LGRIP, S_X, S_Y, S_A, S_B, S_RSTICK, S_LSTICK };
        public static int MirrorSlot(int s) => kMirror[s];
        public static Map Mirrored(Map m)
        {
            var o = m.Clone();
            for (int s = 0; s < S_COUNT; ++s) o.job[MirrorSlot(s)] = m.job[s];
            o.leftSticks = !m.leftSticks;
            return o;
        }
        // LEFT-HANDED: the standard, mirrored (the right grip talks, Y and the left stick's click recenter, B peeks, the
        // right stick's click shows the names; spectating, the left stick turns)
        public static Map LeftHanded() => Mirrored(Standard());

        public static bool IsGrip(int s) => s == S_LGRIP || s == S_RGRIP;
        public static int SlotHand(int s) => s == S_LGRIP || s == S_X || s == S_Y || s == S_LSTICK ? 0 : 1;   // 0 left, 1 right
        // The jobs a button may do: a grip only those that do no harm squeezed by accident in play (TALK, PEEK, NAMES; not
        // READY or RECENTER)
        public static bool AllowedOn(int s, int j)
        {
            if (j < 0 || j >= J_COUNT) return false;
            return !IsGrip(s) || j == J_NONE || j == J_TALK || j == J_PEEK || j == J_NAMES;
        }
        public static bool Required(int j) => j == J_TALK || j == J_READY || j == J_RECENTER;

        // Whether the controllers in your hands have this button (Vive: trackpads, no sticks, no Y; Windows Mixed Reality:
        // no Y)
        public static bool OnController(int kind, int s)
        {
            if (kind == K_VIVE) return s != S_Y && s != S_LSTICK && s != S_RSTICK;
            if (kind == K_WMR) return s != S_Y;
            return s >= 0 && s < S_COUNT;
        }

        public static int Count(Map m, int j, int kind)
        {
            int n = 0;
            for (int s = 0; s < S_COUNT; ++s) n += OnController(kind, s) && m.job[s] == j ? 1 : 0;
            return n;
        }

        // The map as it works on these controllers: a job that must have a button and has none here takes the first free
        // one of its usual buttons (TALK the left grip, then the right; READY A, X, B; RECENTER B, A, X), never a button
        // another such job needs
        static readonly int[][] kUsual = { new[] { -1, -1, -1 }, new[] { S_LGRIP, S_RGRIP, -1 }, new[] { -1, -1, -1 }, new[] { -1, -1, -1 },
                                           new[] { S_A, S_X, S_B }, new[] { S_B, S_A, S_X } };
        public static Map Effective(Map input, int kind)
        {
            Map m = input.Clone();
            for (int s = 0; s < S_COUNT; ++s)
                if (!AllowedOn(s, m.job[s])) m.job[s] = J_NONE;
            for (int j = 0; j < J_COUNT; ++j)
            {
                if (!Required(j) || Count(m, j, kind) > 0) continue;
                for (int k = 0; k < 3; ++k)
                {
                    int s = kUsual[j][k];
                    if (s < 0 || !OnController(kind, s)) continue;
                    int was = m.job[s];
                    if (Required(was) && Count(m, was, kind) <= 1) continue;
                    m.job[s] = j;
                    break;
                }
            }
            return m;
        }

        // Whether a button may give up its job here (a job that must have a button keeps its last one)
        public static bool CanLeave(Map m, int s, int kind)
        {
            int j = m.job[s];
            return !Required(j) || Count(m, j, kind) > 1;
        }
        // The next job for a button, pointing at it on the page: NONE, TALK, PEEK, NAMES, READY, RECENTER in turn (a grip:
        // NONE, TALK, PEEK, NAMES). Its job unchanged when it can't give it up
        public static int NextJob(Map m, int s, int kind)
        {
            if (!CanLeave(m, s, kind)) return m.job[s];
            int j = m.job[s];
            do j = (j + 1) % J_COUNT; while (!AllowedOn(s, j));
            return j;
        }

        // Whether a job's button is held (pressed: each button, this frame; m: effective)
        public static bool Held(Map m, int j, int kind, bool[] pressed)
        {
            for (int s = 0; s < S_COUNT; ++s)
                if (m.job[s] == j && OnController(kind, s) && pressed[s]) return true;
            return false;
        }
        public static int FirstSlot(Map m, int j, int kind)   // -1: none
        {
            for (int s = 0; s < S_COUNT; ++s)
                if (m.job[s] == j && OnController(kind, s)) return s;
            return -1;
        }

        // The button's name on these controllers (Index: A and B on each hand, the left trackpad pressed firmly for Y; Vive
        // and Windows Mixed Reality: the trackpads' clicks and the right menu button)
        static readonly string[] kTouch = { "LEFT GRIP", "RIGHT GRIP", "A", "B", "X", "Y", "LEFT STICK", "RIGHT STICK" };
        static readonly string[] kIndex = { "LEFT GRIP", "RIGHT GRIP", "RIGHT A", "RIGHT B", "LEFT A", "LEFT PAD", "LEFT STICK", "RIGHT STICK" };
        static readonly string[] kPads = { "LEFT GRIP", "RIGHT GRIP", "RIGHT PAD", "RIGHT MENU", "LEFT PAD", "Y", "LEFT STICK", "RIGHT STICK" };
        public static string SlotName(int kind, int s)
        {
            if (s < 0 || s >= S_COUNT) return "";
            return kind == K_INDEX ? kIndex[s] : kind == K_VIVE || kind == K_WMR ? kPads[s] : kTouch[s];
        }
        static readonly string[] kJobs = { "NOTHING", "TALK", "PEEK", "NAMES", "READY", "RECENTER" };
        public static string JobName(int j) => j >= 0 && j < J_COUNT ? kJobs[j] : "";

        // settings.cfg: a digit for each button's job, then the sticks (0 standard, 1 left), e.g. standard "10454235" + "0"
        public static string Save(Map m)
        {
            var c = new char[S_COUNT + 1];
            for (int k = 0; k < S_COUNT; ++k) c[k] = (char)('0' + m.job[k]);
            c[S_COUNT] = m.leftSticks ? '1' : '0';
            return new string(c);
        }
        // false (m unchanged): not a map this game reads (a job a button can't do, or TALK, READY or RECENTER without a button)
        public static bool Load(Map m, string v)
        {
            if (v == null || v.Length != S_COUNT + 1) return false;
            var r = new Map();
            for (int k = 0; k < S_COUNT; ++k)
            {
                int j = v[k] - '0';
                if (!AllowedOn(k, j)) return false;
                r.job[k] = j;
            }
            if (v[S_COUNT] != '0' && v[S_COUNT] != '1') return false;
            r.leftSticks = v[S_COUNT] == '1';
            for (int j = 0; j < J_COUNT; ++j)
                if (Required(j) && Count(r, j, K_UNKNOWN) == 0) return false;
            System.Array.Copy(r.job, m.job, S_COUNT);
            m.leftSticks = r.leftSticks;
            return true;
        }

        // The words (native main.cpp jobButtons, readyName, talkHint): a job's buttons by name ("B / RIGHT STICK CLICK";
        // clicks: a stick's CLICK), READY's button (A as standard), and the push-to-talk hint by the hand that talks
        public static string JobButtons(Map effective, int kind, int job, bool clicks)
        {
            string s = "";
            for (int k = 0; k < S_COUNT; ++k)
            {
                if (effective.job[k] != job || !OnController(kind, k)) continue;
                if (s.Length > 0) s += " / ";
                s += SlotName(kind, k);
                if (clicks && (k == S_LSTICK || k == S_RSTICK)) s += " CLICK";
            }
            return s;
        }
        public static string ReadyName(Map effective, int kind)
        {
            int s = FirstSlot(effective, J_READY, kind);
            return s < 0 ? "A" : SlotName(kind, s);
        }
        public static string TalkHint(Map effective, int kind)   // HOLD LEFT GRIP TO TALK, HOLD THE LEFT STICK IN TO TALK
        {
            int s = FirstSlot(effective, J_TALK, kind);
            if (s < 0) return "PUSH TO TALK: NO BUTTON";
            bool stick = s == S_LSTICK || s == S_RSTICK;
            return (stick ? "HOLD THE " : "HOLD ") + SlotName(kind, s) + (stick ? " IN TO TALK" : " TO TALK");
        }
        // the hand the push-to-talk hint is drawn by (the left with open mic, or no button)
        public static int TalkHand(Map effective, int kind, int voiceMode)
        {
            int s = FirstSlot(effective, J_TALK, kind);
            return voiceMode == 0 && s >= 0 ? SlotHand(s) : 0;
        }
        // what TALK's buttons read this frame (the grips as push to talk reads them, 0..1; a button 1 while pressed)
        public static float TalkValue(Map effective, int kind, bool[] pressed, float[] gripVal)
        {
            float v = 0;
            for (int s = 0; s < S_COUNT; ++s)
                if (effective.job[s] == J_TALK && OnController(kind, s))
                    v = System.Math.Max(v, s == S_LGRIP ? gripVal[0] : s == S_RGRIP ? gripVal[1] : pressed[s] ? 1f : 0f);
            return v;
        }
    }
}
