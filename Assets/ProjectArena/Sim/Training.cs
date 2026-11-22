// TRAINING (native training.h / training.cpp, ported 1:1: task A6; owner 2026-10-29): progressive lessons for new
// players in three tracks (throwing, blocking and deflecting, dodging): targets at every depth and height, rings in the
// air, and a thrower at the far end (the coach, a bot told exactly what to throw) for the defense tracks. Each lesson
// is passed to open the next in its track; its best TIME and ACCURACY are kept (and go to the leaderboards). The
// lessons, judging a ball against the targets, the coach's planned shots, a person's throw made by the hands (the
// tests' proof that people's throws count), and a lesson as it is played. Platform independent (no Unity code);
// Tests/EditMode/TrainingTests.cs plays every lesson, as the native tests do.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static ProjectArena.Sim.M;

namespace ProjectArena.Sim.Training
{
    public enum Kind : byte { THROW, STRIKES, DEFEND, CATCH }
    // DEFEND: how you meet the far end's throws (G_EITHER: shield or knuckle guard, whichever you put in the way)
    public enum Guard : byte { G_NONE, G_SHIELD, G_KNUCKLE, G_DODGE, G_SENDBACK, G_EITHER }
    // the tracks (owner, 2026-10-29)
    public enum Track : byte { T_THROW, T_BLOCK, T_DODGE }
    // DEFEND: where the throws go (round you as you stand)
    public enum Aim : byte { AIM_CHEST, AIM_WIDE, AIM_HEAD, AIM_SHOULDERS, AIM_MIXED }

    public struct TargetDef
    {
        public Vec3 pos;
        public float r;
        public Vec3 hoop;        // CURVE: through this ring first (it faces along the court); hoopR 0: none
        public float hoopR;
        public float slide, slideW, phase;   // MOVING: across the court, z + slide * sin(slideW * t + phase)
    }

    public class Lesson
    {
        public byte id;          // the server's number for it (never reused)
        public Track track;
        public string name, teach, tip1, tip2;   // its name, what you learn (one line), the coach's two lines
        public Kind kind;
        public Guard guard;
        public List<TargetDef> targets = new List<TargetDef>();
        public int budget;       // THROW, STRIKES: throws you have; DEFEND, CATCH: tries in the lesson
        public int pass;         // STRIKES, DEFEND, CATCH: successes to pass (THROW: every target)
        public float minSpeed;   // POWER: a target breaks only for a ball this fast (m/s)
        public bool bank;        // BANK SHOTS: a target counts only off a side wall
        public int atOnce;       // targets up at once (the next comes up as one goes down; 0: all of them)
        public float serveSpeed, serveSpeedUp;   // DEFEND: the first throw (m/s), and how much faster each one comes
        public Aim aim;
        public bool Timed() => kind != Kind.DEFEND || guard == Guard.G_SENDBACK;   // (a TIME board too)
        public int Goal() => kind == Kind.THROW || guard == Guard.G_SENDBACK ? targets.Count : pass;
    }

    public class Target
    {
        public TargetDef d;
        public bool down;
        public bool live = true;   // shown and in play (Lesson.atOnce of them: the next comes up as one goes down)
        public float downT = -1;
        public Vec3 At(float t) => d.pos + new Vec3(0, 0, d.slide * MathF.Sin(d.slideW * t + d.phase));
        public Target Clone() => (Target)MemberwiseClone();
        public static List<Target> Copy(List<Target> T) { var o = new List<Target>(T.Count); foreach (var x in T) o.Add(x.Clone()); return o; }
    }

    // A flight being judged: one ball, one throw (or block) of it
    public class Flight
    {
        public bool on;
        public Vec3 prev;
        public int bounces;
        public bool banked;      // off a side wall
        public int hoop = -1;    // the target whose ring it went through
        public uint told;        // targets it has already been told off for (too slow, not banked, no ring): once a flight
        public void Start(Ball b) { on = true; prev = b.pos; bounces = b.bounces; banked = false; hoop = -1; told = 0; }
        public void Reset() { on = false; prev = default; bounces = 0; banked = false; hoop = -1; told = 0; }
    }

    public enum Cue : byte { TargetDown, Ring, TooSlow, NotBanked, NoRing, Strike, BodyHit, Blocked, Punched, Dodged, Hit,
                             Caught, Dropped, Recharged, Passed, Failed, Go }
    public struct CueEvent { public Cue cue; public Vec3 pos; public int value; public CueEvent(Cue c, Vec3 p, int v) { cue = c; pos = p; value = v; } }

    // a throw that takes target i from `from` (the coach's throws, and the tests' proof that every target can be had)
    public struct Shot { public Vec3 dir, spin; public float speed; public bool ok; }

    public struct Result
    {
        public int lesson;       // index (-1: none)
        public bool passed;
        public int successes, attempts;
        public float time;       // seconds, from GO to the last target (timed lessons)
        public int Permille() => attempts > 0 ? successes * 1000 / attempts : 0;
        public static Result None => new Result { lesson = -1 };
    }

    // your bests, kept on the headset: per lesson id, the best passed TIME and ACCURACY (a better run replaces them)
    public struct Best
    {
        public bool passed;
        public float time;       // 0: none
        public int successes, attempts;
        public int Permille() => attempts > 0 ? successes * 1000 / attempts : 0;
    }

    public class Bests
    {
        public Best[] b = new Best[256];

        // the first of each track, and each one after a passed lesson of its track
        public bool Unlocked(int index)
        {
            var L = Lessons.All;
            if (index < 0 || index >= L.Count) return false;
            for (int i = index - 1; i >= 0; --i) if (L[i].track == L[index].track) return b[L[i].id].passed;
            return true;
        }
        // true: a new best (time or accuracy)
        public bool Add(Result r)
        {
            if (!r.passed || r.lesson < 0 || r.lesson >= Lessons.All.Count) return false;
            Lesson L = Lessons.All[r.lesson];
            ref Best x = ref b[L.id];
            bool better = !x.passed;
            x.passed = true;
            if (L.Timed() && r.time > 0 && (x.time <= 0 || r.time < x.time)) { x.time = r.time; better = true; }
            if (x.attempts == 0 || r.Permille() > x.Permille() || (r.Permille() == x.Permille() && r.successes > x.successes))
            {
                x.successes = r.successes; x.attempts = r.attempts; better = true;
            }
            return better;
        }
        // "id passed time successes attempts" lines
        public string Save()
        {
            var s = new StringBuilder();
            for (int id = 0; id < 256; ++id)
                if (b[id].passed)
                    s.Append(id).Append(' ').Append(1).Append(' ').Append(b[id].time.ToString("F3", CultureInfo.InvariantCulture))
                     .Append(' ').Append(b[id].successes).Append(' ').Append(b[id].attempts).Append('\n');
            return s.ToString();
        }
        // (as native's stream: five numbers a line, any whitespace; a word that isn't a number ends it; nonsense skipped)
        public void Load(string s)
        {
            string[] w = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int k = 0; k + 4 < w.Length; k += 5)
            {
                if (!int.TryParse(w[k], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                    !int.TryParse(w[k + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int passed) ||
                    !float.TryParse(w[k + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float time) ||
                    !int.TryParse(w[k + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int succ) ||
                    !int.TryParse(w[k + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int att)) break;
                if (id >= 0 && id < 256 && att >= 0 && att < 1000 && succ >= 0 && succ <= att && time >= 0 && time < 36000)
                    b[id] = new Best { passed = passed != 0, time = time, successes = succ, attempts = att };
            }
        }
        // the overall board: every lesson's best accuracy added up (permille)
        public int Total()
        {
            int t = 0;
            foreach (Lesson L in Lessons.All) t += b[L.id].Permille();
            return t;
        }
    }

    public static class Lessons
    {
        public const float kWallX = 9.42f;   // the far wall's targets (the wall at 9.5 m)
        public const float kSideZ = 2.5f;    // the side walls
        public const int Tracks = 3;

        // a target: a disc facing you, anywhere in the court (on the far wall: x = kWallX)
        static TargetDef T(float x, float y, float z, float r) => new TargetDef { pos = new Vec3(x, y, z), r = r };
        // CURVE: the target on the far wall, its ring half way down the court (both at one height)
        static TargetDef RingT(float y, float z, float hz) => new TargetDef { pos = new Vec3(kWallX, y, z), r = 0.55f, hoop = new Vec3(1.0f, y, hz), hoopR = 0.5f };
        // MOVING: a disc at depth x, sliding across the court
        static TargetDef SlideT(float x, float y, float r, float amp, float w, float ph) =>
            new TargetDef { pos = new Vec3(x, y, 0), r = r, slide = amp, slideW = w, phase = ph };

        public static string TrackName(int t) => t == (int)Track.T_THROW ? "THROWING" : t == (int)Track.T_BLOCK ? "BLOCKING" : "DODGING";

        static Lesson Mk(byte id, Track track, string name, string teach, string tip1, string tip2, Kind kind, Guard guard, int budget, int pass) =>
            new Lesson { id = id, track = track, name = name, teach = teach, tip1 = tip1, tip2 = tip2, kind = kind, guard = guard,
                         budget = budget, pass = pass, atOnce = 0, aim = Aim.AIM_CHEST };

        static List<Lesson> all;
        public static List<Lesson> All => all ??= Make();

        static List<Lesson> Make()
        {
            var v = new List<Lesson>();
            Lesson l;
            // ---- THROWING: targets at every depth, a few up at a time, appearing as others go down
            l = Mk(1, Track.T_THROW, "FIRST THROWS", "TAKE YOUR BALL, SWING AND LET GO",
                   "PULL THE TRIGGER AT YOUR BALL TO TAKE IT.", "SWING AT THE TARGET AND LET GO OF THE TRIGGER.", Kind.THROW, Guard.G_NONE, 15, 0);
            l.targets = new List<TargetDef> { T(-1.0f, 1.5f, 0.0f, 0.55f), T(3.0f, 1.6f, -1.1f, 0.55f), T(6.0f, 1.4f, 1.1f, 0.55f), T(kWallX, 1.8f, 0.0f, 0.55f),
                                              T(2.0f, 2.3f, 0.9f, 0.55f) };
            l.atOnce = 1; v.Add(l);
            l = Mk(2, Track.T_THROW, "PICK YOUR SPOT", "NEAR AND FAR, HIGH AND LOW",
                   "YOUR BALL GOES WHERE YOUR HAND IS GOING AS YOU LET GO.", "FINISH THE SWING POINTING AT THE TARGET.", Kind.THROW, Guard.G_NONE, 22, 0);
            l.targets = new List<TargetDef> { T(0.0f, 0.9f, -1.5f, 0.38f), T(4.0f, 2.8f, 1.6f, 0.38f), T(kWallX, 0.8f, 1.9f, 0.38f), T(2.0f, 1.6f, 0.0f, 0.38f),
                                              T(6.5f, 3.0f, -1.8f, 0.38f), T(-1.5f, 2.6f, 1.2f, 0.38f), T(kWallX, 3.2f, -0.8f, 0.38f), T(5.0f, 0.8f, 0.4f, 0.38f) };
            l.atOnce = 2; v.Add(l);
            l = Mk(3, Track.T_THROW, "POWER", "THROW HARD",
                   "THESE BREAK ONLY FOR A FAST BALL: 11 METRES A SECOND.", "SWING FROM THE SHOULDER AND FOLLOW THROUGH.", Kind.THROW, Guard.G_NONE, 15, 0);
            l.targets = new List<TargetDef> { T(kWallX, 1.6f, 0.0f, 0.45f), T(7.0f, 2.4f, -1.2f, 0.45f), T(kWallX, 1.0f, 1.6f, 0.45f), T(5.0f, 1.8f, 1.4f, 0.45f),
                                              T(kWallX, 2.6f, -1.6f, 0.45f) };
            l.minSpeed = 11f; l.atOnce = 2; v.Add(l);
            l = Mk(4, Track.T_THROW, "BANK SHOTS", "PLAY THE WALLS",
                   "THESE COUNT ONLY OFF A SIDE WALL.", "AIM AT THE WALL: THE BALL LEAVES IT AT THE ANGLE IT CAME IN.", Kind.THROW, Guard.G_NONE, 16, 0);
            l.targets = new List<TargetDef> { T(kWallX, 1.5f, -1.9f, 0.45f), T(6.0f, 1.6f, 2.0f, 0.45f), T(kWallX, 2.7f, 1.9f, 0.45f), T(4.0f, 2.2f, -2.0f, 0.45f) };
            l.bank = true; l.atOnce = 2; v.Add(l);
            l = Mk(5, Track.T_THROW, "CURVE", "BEND YOUR THROW",
                   "THROUGH THE RING, THEN INTO ITS TARGET.", "TWIST YOUR WRIST AS YOU LET GO: THE BALL BENDS THE WAY IT SPINS.", Kind.THROW, Guard.G_NONE, 16, 0);
            l.targets = new List<TargetDef> { RingT(1.5f, 0.3f, -0.6f), RingT(1.5f, -0.3f, 0.6f), RingT(2.3f, 0.5f, -0.5f), RingT(2.3f, -0.5f, 0.5f) };
            l.atOnce = 1; v.Add(l);
            l = Mk(6, Track.T_THROW, "MOVING TARGETS", "LEAD THE TARGET",
                   "THROW WHERE THE TARGET WILL BE.", "WATCH ITS RHYTHM, THEN LET GO.", Kind.THROW, Guard.G_NONE, 18, 0);
            l.targets = new List<TargetDef> { SlideT(kWallX, 1.5f, 0.45f, 1.5f, 0.9f, 0), SlideT(4.0f, 2.2f, 0.45f, 1.5f, 1.1f, 1), SlideT(kWallX, 1.0f, 0.45f, 1.6f, 1.2f, 2),
                                              SlideT(6.0f, 2.8f, 0.42f, 1.4f, 1.4f, 3), SlideT(2.0f, 1.6f, 0.42f, 1.6f, 1.6f, 4), SlideT(kWallX, 1.3f, 0.40f, 1.6f, 1.8f, 5) };
            l.atOnce = 1; v.Add(l);
            l = Mk(7, Track.T_THROW, "STRIKE ZONE", "EARN STRIKES",
                   "A BALL THROUGH THE RING BEHIND THE OTHER PLAYER IS A STRIKE.", "STRIKES MAKE YOUR BALL BIGGER AND FASTER. GO PAST THE BODY.", Kind.STRIKES, Guard.G_NONE, 15, 5);
            v.Add(l);
            l = Mk(8, Track.T_THROW, "CATCH", "CATCH YOUR BALL",
                   "THROW AT THE FAR WALL: YOUR BALL COMES BACK TO YOU.", "HOLD THE TRIGGER AND MEET IT WITH YOUR HAND.", Kind.CATCH, Guard.G_NONE, 8, 5);
            v.Add(l);
            // ---- BLOCKING AND DEFLECTING: the far end throws at you
            l = Mk(21, Track.T_BLOCK, "SHIELD", "BLOCK WITH YOUR SHIELD",
                   "HOLD YOUR BALL: ITS CHARGE POWERS THE SHIELD ON THAT HAND.", "A BLOCK USES THE CHARGE. IN A MATCH, THROW TO RECHARGE.", Kind.DEFEND, Guard.G_SHIELD, 10, 7);
            l.serveSpeed = 7f; l.serveSpeedUp = 0.3f; l.aim = Aim.AIM_CHEST; v.Add(l);
            l = Mk(22, Track.T_BLOCK, "SHIELD ANGLES", "HIGH, LOW, LEFT AND RIGHT",
                   "THE THROWS GO WIDE OF YOUR BODY NOW: REACH THE SHIELD OUT.", "WATCH THE BALL INTO THE SHIELD.", Kind.DEFEND, Guard.G_SHIELD, 10, 7);
            l.serveSpeed = 8f; l.serveSpeedUp = 0.3f; l.aim = Aim.AIM_WIDE; v.Add(l);
            l = Mk(23, Track.T_BLOCK, "KNUCKLE GUARDS", "PUNCH IT AWAY",
                   "THE GLASS ON YOUR FISTS SENDS A BALL BACK. NO BALL NEEDED.", "PUT A FIST IN ITS WAY, OR PUNCH IT FOR MORE SPEED.", Kind.DEFEND, Guard.G_KNUCKLE, 10, 7);
            l.serveSpeed = 7f; l.serveSpeedUp = 0.35f; l.aim = Aim.AIM_CHEST; v.Add(l);
            l = Mk(24, Track.T_BLOCK, "SHIELD OR FIST", "WHATEVER IS NEAREST",
                   "SHIELD AND KNUCKLE GUARDS BOTH ON: MEET EACH THROW WITH EITHER.", "THEY COME FASTER AND FROM EVERY SIDE.", Kind.DEFEND, Guard.G_EITHER, 12, 9);
            l.serveSpeed = 8.5f; l.serveSpeedUp = 0.3f; l.aim = Aim.AIM_MIXED; v.Add(l);
            l = Mk(25, Track.T_BLOCK, "SEND IT BACK", "DEFENSE INTO ATTACK",
                   "BLOCK OR PUNCH THE THROWS INTO THE TARGETS.", "TURN YOUR SHIELD OR FIST TOWARD THE TARGET.", Kind.DEFEND, Guard.G_SENDBACK, 15, 0);
            l.targets = new List<TargetDef> { T(kWallX, 2.2f, -1.7f, 0.55f), T(kWallX, 2.2f, 1.7f, 0.55f), T(kWallX, 3.2f, 0, 0.55f) };
            l.serveSpeed = 7f; v.Add(l);
            // ---- DODGING: no shield, no guards
            l = Mk(41, Track.T_DODGE, "SIDESTEP", "STEP OUT OF THE WAY",
                   "NO SHIELD AND NO GUARDS: THE THROWS COME AT YOUR BODY.", "ONE STEP TO THE SIDE AS IT COMES.", Kind.DEFEND, Guard.G_DODGE, 10, 7);
            l.serveSpeed = 6f; l.serveSpeedUp = 0.2f; l.aim = Aim.AIM_CHEST; v.Add(l);
            l = Mk(42, Track.T_DODGE, "DUCK", "GET UNDER IT",
                   "THESE COME AT YOUR HEAD.", "BEND YOUR KNEES AND DROP: THE BALL GOES OVER.", Kind.DEFEND, Guard.G_DODGE, 10, 7);
            l.serveSpeed = 6.5f; l.serveSpeedUp = 0.25f; l.aim = Aim.AIM_HEAD; v.Add(l);
            l = Mk(43, Track.T_DODGE, "LEAN", "LET IT PAST YOUR SHOULDER",
                   "THESE COME AT ONE SHOULDER, THEN THE OTHER.", "LEAN AWAY FROM THE BALL AND KEEP YOUR FEET.", Kind.DEFEND, Guard.G_DODGE, 10, 7);
            l.serveSpeed = 6.5f; l.serveSpeedUp = 0.3f; l.aim = Aim.AIM_SHOULDERS; v.Add(l);
            l = Mk(44, Track.T_DODGE, "EVERYTHING", "STEP, DUCK OR LEAN",
                   "BODY, HEAD, SHOULDERS: ANY OF THEM, FASTER.", "WATCH THE THROWER'S HAND, THEN MOVE.", Kind.DEFEND, Guard.G_DODGE, 12, 8);
            l.serveSpeed = 7.5f; l.serveSpeedUp = 0.35f; l.aim = Aim.AIM_MIXED; v.Add(l);
            return v;
        }

        // the first atOnce targets not yet down are up
        public static void RefreshLive(Lesson L, List<Target> T)
        {
            int up = 0;
            foreach (Target x in T)
            {
                x.live = x.down || L.atOnce <= 0 || up < L.atOnce;   // (a target taken stays shown: it bursts)
                if (!x.down && x.live) ++up;
            }
        }

        // the next lesson of its track (-1: it was the last)
        public static int NextInTrack(int index)
        {
            var L = All;
            for (int i = index + 1; i < L.Count; ++i) if (L[i].track == L[index].track) return i;
            return -1;
        }
        // 1-based, and how many the track has
        public static int PlaceInTrack(int index, out int count)
        {
            var L = All;
            int place = 0, n = 0;
            for (int i = 0; i < L.Count; ++i)
                if (L[i].track == L[index].track) { ++n; if (i <= index) ++place; }
            count = n;
            return place;
        }
        // its index (-1: none)
        public static int ById(int id)
        {
            for (int i = 0; i < All.Count; ++i) if (All[i].id == id) return i;
            return -1;
        }

        static float SegPoint(Vec3 a, Vec3 b, Vec3 p)
        {
            Vec3 ab = b - a;
            float l2 = Dot(ab, ab);
            float t = l2 > 1e-12f ? Clampf(Dot(p - a, ab) / l2, 0f, 1f) : 0f;
            return Length(a + ab * t - p);
        }

        // ball b moved to `to` at lesson time t: the target it took (-1: none); why one didn't count goes to `cues`
        public static int Judge(Lesson L, List<Target> T, Flight f, Ball b, float ballR, Vec3 to, float t, List<CueEvent> cues)
        {
            if (!f.on) return -1;
            Vec3 from = f.prev;
            f.prev = to;
            if (b.bounces > f.bounces)
            {   // a bounce on the way: off a side wall?
                if (MathF.Max(MathF.Abs(from.z), MathF.Abs(to.z)) > kSideZ - ballR - 0.35f) f.banked = true;
                f.bounces = b.bounces;
            }
            for (int i = 0; i < T.Count; ++i)
            {   // through a ring (it faces along the court)
                Target x = T[i];
                if (!x.live || x.down || x.d.hoopR <= 0) continue;
                float hx = x.d.hoop.x;
                if ((from.x - hx) * (to.x - hx) > 0 || MathF.Abs(to.x - from.x) < 1e-6f) continue;
                Vec3 c = Lerp(from, to, (hx - from.x) / (to.x - from.x));
                if (Length(new Vec3(0, c.y - x.d.hoop.y, c.z - x.d.hoop.z)) < x.d.hoopR - 0.3f * ballR && f.hoop != i)
                {
                    f.hoop = i;
                    cues?.Add(new CueEvent(Cue.Ring, c, i));
                }
            }
            for (int i = 0; i < T.Count; ++i)
            {
                Target x = T[i];
                if (!x.live || x.down) continue;
                Vec3 c = x.At(t);
                if (SegPoint(from, to, c) > x.d.r + 0.6f * ballR) continue;
                Cue why = Cue.TargetDown;
                if (L.minSpeed > 0 && b.speedLimit < L.minSpeed) why = Cue.TooSlow;
                else if (L.bank && !f.banked) why = Cue.NotBanked;
                else if (x.d.hoopR > 0 && f.hoop != i) why = Cue.NoRing;
                if (why == Cue.TargetDown) return i;
                if (cues != null && (f.told & (1u << i)) == 0) cues.Add(new CueEvent(why, c, i));
                f.told |= 1u << i;
            }
            return -1;
        }

        // a side wall's mirror of a point (the ball's centre bounces a radius off the wall)
        static Vec3 Mirrored(Vec3 p, float side)
        {
            float wall = side * (kSideZ - 0.1f);
            return new Vec3(p.x, p.y, 2f * wall - p.z);
        }

        // aim assist toward (live targets)
        public static List<Vec3> AimPoints(Lesson L, List<Target> T, float t)
        {
            var o = new List<Vec3>();
            foreach (Target x in T)
            {
                if (!x.live || x.down) continue;
                if (x.d.hoopR > 0) o.Add(x.d.hoop);   // (CURVE: toward the ring; the spin does the rest)
                else if (L.bank) { o.Add(Mirrored(x.At(t), 1f)); o.Add(Mirrored(x.At(t), -1f)); }
                else o.Add(x.At(t));
            }
            return o;
        }

        struct Guess { public float yaw, pitch, spin; public Guess(float y, float p, float s) { yaw = y; pitch = p; spin = s; } }
        static Vec3 DirOf(float yaw, float pitch) => new Vec3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Sin(yaw));

        public static Shot PlanShot(Game g0, int p, Vec3 from, Lesson L, List<Target> T, int i, float t0, bool assist)
        {
            Game g = g0.Clone();
            g.aimAt[p] = assist ? AimPoints(L, T, t0) : new List<Vec3>();
            float speed = L.minSpeed > 0 ? L.minSpeed + 2f : 11.5f;
            TargetDef d = T[i].d;
            List<Target> only = Target.Copy(T);
            foreach (Target x in only) x.live = false;
            only[i].live = true;
            only[i].down = false;
            var path = new List<BallPathPt>();
            // how a throw goes: 0 it takes the target; otherwise how far off it was (metres, and more for a missed rule)
            int evals = 0;
            float Eval(float yaw, float pitch, float spin)
            {
                ++evals;
                g.TrialFlight(p, from, DirOf(yaw, pitch), speed, new Vec3(0, spin, 0), assist, 2.5f, 1f / 72, path);
                List<Target> tt = Target.Copy(only);
                var f = new Flight();
                f.Start(path[0].ball);
                f.prev = path[0].pos;
                float miss = 1e9f, ringMiss = 1e9f;
                for (int k = 1; k < path.Count; ++k)
                {
                    float tk = t0 + (path[k].t - path[0].t);
                    float R = g.tun.projectileRadius * path[k].ball.scale;
                    Vec3 a = f.prev;
                    if (Judge(L, tt, f, path[k].ball, R, path[k].pos, tk, null) == i) return 0f;
                    miss = MathF.Min(miss, SegPoint(a, path[k].pos, tt[i].At(tk)) - d.r);
                    if (d.hoopR > 0) ringMiss = MathF.Min(ringMiss, SegPoint(a, path[k].pos, d.hoop) - (d.hoopR - 0.1f));
                }
                float m = MathF.Max(0f, miss);
                if (L.bank && !f.banked) m += 2f;
                if (d.hoopR > 0 && f.hoop != i) m += 1f + MathF.Max(0f, ringMiss);
                return MathF.Max(0.001f, m);
            }
            var guesses = new List<Guess>();
            void Toward(Vec3 at, float spin)
            {
                Vec3 v = at - from;
                guesses.Add(new Guess(MathF.Atan2(v.z, v.x), MathF.Atan2(v.y, MathF.Sqrt(v.x * v.x + v.z * v.z)), spin));
            }
            if (d.hoopR > 0)
            {   // a circle through you, the ring and the target (seen from above): its bend is the spin
                float ax = from.x, az = from.z, bx = d.hoop.x, bz = d.hoop.z, cx = d.pos.x, cz = d.pos.z;
                float D = 2f * (ax * (bz - cz) + bx * (cz - az) + cx * (az - bz));
                if (MathF.Abs(D) > 1e-6f)
                {
                    float a2 = ax * ax + az * az, b2 = bx * bx + bz * bz, c2 = cx * cx + cz * cz;
                    float ux = (a2 * (bz - cz) + b2 * (cz - az) + c2 * (az - bz)) / D;
                    float uz = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / D;
                    float R = MathF.Sqrt((ax - ux) * (ax - ux) + (az - uz) * (az - uz));
                    var tan = new Vec3(-(az - uz), 0, ax - ux);   // perpendicular to the radius, toward the ring
                    if (tan.x * (bx - ax) + tan.z * (bz - az) < 0) tan = -tan;
                    float s = 1.1f * speed / (R * g.tun.magnusFactor);   // (a little more: the spin dies away in flight)
                    float yaw = MathF.Atan2(tan.z, tan.x), pitch = MathF.Atan2(d.pos.y - from.y, cx - ax);
                    guesses.Add(new Guess(yaw, pitch, s));
                    guesses.Add(new Guess(yaw, pitch, -s));
                }
            }
            else if (L.bank)
            {
                Toward(Mirrored(d.pos, 1f), 0);
                Toward(Mirrored(d.pos, -1f), 0);
            }
            else
            {   // straight at it (moving: where it will be when the ball gets there)
                Vec3 at = T[i].At(t0);
                for (int k = 0; k < 3; ++k) at = T[i].At(t0 + Length(at - from) / speed);
                Toward(at, 0);
            }
            var best = new Shot { speed = speed };
            float bestE = 1e9f;
            Guess bg = default;
            foreach (Guess q in guesses)
            {
                float e = Eval(q.yaw, q.pitch, q.spin);
                if (e < bestE) { bestE = e; bg = q; }
                if (e == 0f) break;
            }
            // then a pattern search round the best guess: yaw, pitch and spin a step either way, the steps halving
            float sy = Deg2Rad(1.5f), sp = Deg2Rad(1.5f), ss = d.hoopR > 0 ? 3f : 0f;
            while (bestE > 0f && evals < 160 && sy > Deg2Rad(0.04f))
            {
                bool better = false;
                Guess[] tries = { new Guess(bg.yaw + sy, bg.pitch, bg.spin), new Guess(bg.yaw - sy, bg.pitch, bg.spin), new Guess(bg.yaw, bg.pitch + sp, bg.spin),
                                  new Guess(bg.yaw, bg.pitch - sp, bg.spin), new Guess(bg.yaw, bg.pitch, bg.spin + ss), new Guess(bg.yaw, bg.pitch, bg.spin - ss) };
                for (int k = 0; k < (ss > 0 ? 6 : 4) && bestE > 0f; ++k)
                {
                    float e = Eval(tries[k].yaw, tries[k].pitch, tries[k].spin);
                    if (e < bestE) { bestE = e; bg = tries[k]; better = true; }
                }
                if (!better) { sy *= 0.5f; sp *= 0.5f; ss *= 0.5f; }
            }
            best.dir = DirOf(bg.yaw, bg.pitch);
            best.spin = new Vec3(0, bg.spin, 0);
            best.ok = bestE == 0f;
            return best;
        }
    }

    // a person's throw, made by the hands (the tests' proof that people's throws count): reach for the ball, pull the
    // trigger, swing back, sweep through toward where it should go, let go of the trigger, follow through; CATCH: then
    // the trigger held, a hand where the ball comes back. Player p must face +X or -X
    public class Thrower
    {
        public int phase, reps, target = -1;   // (the target this throw is for)
        public float t;
        public string caption = "";             // the step it shows ("3  SWING YOUR ARM BACK")
        Vec3 hold, back, rel, dir, spin, follow;
        Quat q_ = Quat.Identity;
        float vh = 5f, ta = 0.1f, tc = 0.18f;

        static float Ease(float u) { u = Clampf(u, 0f, 1f); return u * u * (3f - 2f * u); }
        static Quat Along(Vec3 d) =>   // a controller pointing along d (its -Z), level
            Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-d.x, -d.z)) * Quat.AxisAngle(new Vec3(1, 0, 0), MathF.Asin(Clampf(d.y, -1f, 1f)));

        void Plan(Game g, int p, Lesson L, List<Target> T, float clock)
        {
            Vec3 f = g.Facing(p), right = Cross(f, new Vec3(0, 1, 0));
            Vec3 stand = g.tun.start[p] + new Vec3(0, 1.65f, 0), shoulder = stand + new Vec3(0, -0.30f, 0) + right * 0.19f;
            float S = L.kind == Kind.CATCH ? 10f : L.minSpeed > 0 ? L.minSpeed + 2f : 11.5f;   // (as PlanShot plans it)
            float lo = 0.3f, hi = g.tun.maxInputSpeed;   // the hand speed that throws it so fast
            for (int k = 0; k < 30; ++k) { float m = 0.5f * (lo + hi); if (g.ThrowSpeedFromHandSpeed(m, p) < S) lo = m; else hi = m; }
            vh = 0.5f * (lo + hi);
            const float dA = 0.2f;
            ta = 2f * dA / vh;
            tc = 0.18f;   // (steady through the game's 0.15 s window: the ball leaves as the hand goes)
            target = -1;
            Vec3 point;
            if (L.kind == Kind.THROW)
            {
                for (int i = 0; i < T.Count && target < 0; ++i) if (T[i].live && !T[i].down) target = i;
                if (target < 0) target = 0;
                point = T[target].At(clock);
            }
            else if (L.kind == Kind.STRIKES) point = g.StrikeZoneCenter(1) + new Vec3(0, 0.02f, (reps & 1) != 0 ? 0.45f : -0.45f);
            else point = new Vec3(9.5f, 1.7f, (reps & 1) != 0 ? 0.4f : -0.4f);
            dir = Normalize(point - (shoulder + f * 0.45f));
            spin = default;
            for (int it = 0; it < 2; ++it)
            {
                rel = shoulder + dir * 0.45f;
                q_ = Along(dir);
                Vec3 from = rel - dir * (vh / 72f) + q_.Rotate(g.tun.holdOffset);   // (where the ball is as it leaves)
                if (L.kind == Kind.THROW && T.Count > 0)
                {
                    Shot s = Lessons.PlanShot(g, p, from, L, T, target, clock + 0.45f + ta + tc, true);
                    dir = s.dir; spin = s.spin;
                }
                else dir = Normalize(point - from);
            }
            back = rel - dir * (dA + vh * tc);
        }

        public PlayerInput Step(Game g, int p, Lesson L, List<Target> T, float clock, float dt)
        {
            Ball b = g.pl[p].ball;
            Vec3 f = g.Facing(p), right = Cross(f, new Vec3(0, 1, 0));
            Quat face = Along(f);
            Vec3 stand = g.tun.start[p] + new Vec3(0, 1.65f, 0);
            Vec3 restR = stand + f * 0.12f + new Vec3(0, -0.72f, 0) + right * 0.26f, restL = stand + f * 0.12f + new Vec3(0, -0.72f, 0) - right * 0.26f;
            Quat restQ = face * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(-35f));
            var @in = new PlayerInput { headTracked = true, head = new Pose(face, stand) };
            @in.hand[0].tracked = @in.hand[1].tracked = true;
            @in.hand[0].grip = new Pose(restQ, restL);
            t += dt;
            Vec3 pos = restR;
            Quat q = restQ;
            float trig = 0;
            Vec3 ang = default;
            void Go(int ph) { phase = ph; t = 0; }
            switch (phase)
            {
                case 0:   // at rest: then reach for the ball on its spawner
                    caption = "1  REACH FOR YOUR BALL";
                    if (t > 0.6f && b.state == BallState.Idle) Go(1);
                    break;
                case 1:
                {
                    Vec3 at = b.pos - restQ.Rotate(g.tun.launcherOffset);
                    pos = Lerp(restR, at, Ease(t / 0.6f));
                    trig = t >= 0.6f ? 1f : 0f;
                    caption = t < 0.6f ? "1  REACH FOR YOUR BALL" : "2  PULL THE TRIGGER TO TAKE IT";
                    if (b.state == BallState.Held) { hold = pos; Go(2); }
                    else if (t > 1.6f) Go(0);
                    break;
                }
                case 2:   // holding it (the trigger stays held)
                    pos = hold; trig = 1;
                    caption = "2  KEEP THE TRIGGER HELD: THE BALL STAYS IN YOUR HAND";
                    if (t > 0.35f) { Plan(g, p, L, T, clock); Go(3); }
                    break;
                case 3:   // swing back
                    pos = Lerp(hold, back, Ease(t / 0.45f)); q = Nlerp(restQ, q_, Ease(t / 0.45f)); trig = 1;
                    caption = "3  SWING YOUR ARM BACK";
                    if (t >= 0.45f) Go(4);
                    break;
                case 4:
                {   // sweep it through: speeding up, then steady as the trigger is let go
                    float s = t < ta ? 0.5f * (vh / ta) * t * t : 0.5f * vh * ta + vh * (t - ta);
                    pos = back + dir * s; q = q_; trig = 1; ang = spin;
                    caption = L.minSpeed > 0 ? "4  SWEEP IT THROUGH - FAST!" : spin.y != 0 ? "4  SWEEP THROUGH AND TWIST YOUR WRIST"
                            : L.bank ? "4  SWEEP THROUGH TOWARD THE SIDE WALL" : L.kind == Kind.STRIKES ? "4  SWEEP THROUGH, PAST THE BODY"
                            : L.kind == Kind.CATCH ? "4  SWEEP THROUGH TOWARD THE FAR WALL" : "4  SWEEP THROUGH TOWARD THE TARGET";
                    if (t >= ta + tc) { trig = 0; follow = pos; Go(5); }
                    break;
                }
                case 5:
                {   // let go, and follow through
                    float u = Clampf(t / 0.35f, 0f, 1f);
                    pos = follow + dir * (0.3f * (1f - (1f - u) * (1f - u))); q = q_;
                    caption = "5  LET GO OF THE TRIGGER AS YOUR HAND COMES FORWARD";
                    if (t >= 0.35f) { follow = pos; Go(6); }
                    break;
                }
                case 6:   // back to rest, watching the ball
                    pos = Lerp(follow, restR, Ease(t / 0.8f)); q = Nlerp(q_, restQ, Ease(t / 0.8f));
                    caption = "AND WATCH IT GO";
                    if (L.kind == Kind.CATCH && b.state == BallState.Returning) { follow = pos; Go(7); }
                    else if (t > 1.6f && b.state != BallState.Flying && b.state != BallState.Returning) { ++reps; Go(0); }
                    else if (t > 5f) { ++reps; Go(0); }
                    break;
                case 7:
                {   // CATCH: the trigger held, the hand where the ball will pass in front of you
                    Vec3 m0 = stand + f * 0.5f + new Vec3(0, -0.3f, 0) + right * 0.15f;
                    Vec3 v = Length(b.vel) > 1e-3f ? Normalize(b.vel) : -f;
                    Vec3 c = b.pos + v * Dot(m0 - b.pos, v);
                    Vec3 want = c - restQ.Rotate(g.tun.catcherOffset);
                    Vec3 d = want - follow;
                    float step = 3.5f * dt;
                    follow = Length(d) > step ? follow + Normalize(d) * step : want;
                    pos = follow; trig = 1;
                    caption = "HOLD THE TRIGGER AND MEET IT WITH YOUR HAND";
                    if (b.state == BallState.Held) { hold = pos; ++reps; Go(2); }
                    else if (b.state != BallState.Returning) { ++reps; Go(0); }
                    break;
                }
            }
            @in.hand[1].grip = new Pose(q, pos);
            @in.hand[1].trigger = trig;
            @in.hand[1].angVel = ang;
            return @in;
        }
    }

    public enum Phase : byte { Off, Card, Countdown, Play, Done }

    public class Trainer
    {
        public Phase phase = Phase.Off;
        public int lesson = -1;
        public List<Target> targets = new List<Target>();
        public int successes, attempts, throws;
        public float clock;                    // the lesson's time from GO
        public bool endless;                   // SOUND STUDIO: the lesson never ends and has no result (set after Begin; native 0.3.16)
        public Result result = Result.None;
        public List<CueEvent> cues = new List<CueEvent>();   // this step's (sounds, flashes)
        public string line = "";               // the coach's word now ("TAKE YOUR BALL")

        public Lesson L => Lessons.All[lesson];
        public bool Active => phase != Phase.Off;
        public Tunables SavedRules => saved;   // the game's rules before the lesson (what is saved)
        // the coach at the far end of the real game (drawn from it)
        public bool CoachThere() => lesson >= 0 && (L.kind == Kind.STRIKES || L.kind == Kind.DEFEND);

        Tunables saved = new Tunables();
        bool hadSaved;
        readonly Flight[] flight = { new Flight(), new Flight() };   // [0] your ball's throw, [1] the coach's ball once you blocked it (SEND IT BACK)
        int serve;                     // DEFEND: throws made
        bool serving;                  // one on its way
        float serveT, waitT, rechargeT = -1;
        bool caughtArmed;              // CATCH: your ball is coming back from the far wall
        float respawnT = -1;           // DEFEND: the coach's ball you blocked flies on a moment before its next one

        Vec3 CoachRelease(Game g, int p)
        {   // where its throws leave its right hand
            Vec3 f = g.Facing(p), right = Cross(f, new Vec3(0, 1, 0));
            return g.tun.start[p] + new Vec3(0, 1.325f, 0) + right * 0.18f + f * 0.41f;
        }

        static void MakeCoach(Game g, int p, bool watch, Vec3 from, float speed)
        {
            PlayerState s = g.pl[p];
            s.isBot = true;
            s.botLevel = Bot.Levels - 1;   // (its blocks, dodges and catches as good as they come)
            s.coach.CopyFrom(new PlayerState.Coach());
            s.coach.on = true;
            s.coach.watch = watch;
            s.coach.from = from;
            s.coach.speed = speed;
            s.coach.dir = new Vec3(p == 0 ? 1f : -1f, 0, 0);
        }

        // the room options of a lesson's rules (TASKS.md E33, native c79777f / 3c285d9): STANDARD the rules' own, for every
        // board (the owner, 2026-11-18); CUSTOM TRAINING the player's own (own: their rules), the options a lesson can take:
        // AIM ASSIST, SHIELD SIZE, MAIDEN, STRIKE ZONE, STRIKE SPEED, BALL GROWTH and BOUNCES (shields, knuckle guards, the
        // clock and the foul line stay the lesson's)
        public static void LessonOptions(Tunables t, Tunables own, bool custom)
        {
            Tunables o = custom ? own : new Tunables();   // (the rules' own: a fresh install's)
            t.strikeSpeed = o.strikeSpeed; t.maiden = o.maiden; t.aimAssist = o.aimAssist; t.strikeZone = o.strikeZone;
            t.ballGrowth = o.ballGrowth; t.shieldSize = o.shieldSize;
            t.wallsLively = o.wallsLively; t.wallGrip = o.wallGrip; t.bankAssist = o.bankAssist;   // (BOUNCES, 0.3.22)
        }
        // CUSTOM TRAINING (native 0.3.22; the owner, 2026-11-18: "custom training that keeps local score but nothing globally
        // but opens the options for them ... so they can train on the config that they play in competing"): the lesson
        // played with the player's own room options (MY OPTIONS: LessonOptions), its bests kept on the headset. Set before Begin
        public bool custom;

        void SetRules(Game g, bool atFar)
        {
            g.tun.CopyFrom(saved);
            Tunables t = g.tun;
            t.advanced = false;
            t.experimental = false;
            t.players = 2;
            t.basicMatchTime = 0;   // (no clock: the lesson keeps its own)
            t.useShotClock = false;
            t.meetTime = 0;
            t.botMeet = false;
            t.botAdapt = false;
            t.foulShift = 0;        // (the lessons' line where the arena has it)
            LessonOptions(t, saved, custom);   // (the room's options: the rules' own, or MY OPTIONS in custom training)
            Lesson l = L;
            t.shields = l.guard != Guard.G_KNUCKLE && l.guard != Guard.G_DODGE;
            t.knuckleMode = l.guard == Guard.G_SHIELD || l.guard == Guard.G_DODGE || l.kind == Kind.STRIKES ? 0 : 1;   // (G_EITHER: both on)
            if (!atFar) t.start[1].y = -50f;   // (nobody at the far end: no body to hit, no ring)
        }

        // the lesson set up (its card), the coach showing it; the rules the game had are kept, for Stop()
        public void Begin(Game g, int idx, List<CollisionTri> classic)
        {
            if (!hadSaved) { saved = g.tun.Clone(); hadSaved = true; }
            lesson = Math.Max(0, Math.Min(Lessons.All.Count - 1, idx));
            phase = Phase.Card;
            Lesson l = L;
            targets.Clear();
            foreach (TargetDef d in l.targets) targets.Add(new Target { d = d });
            Lessons.RefreshLive(l, targets);
            successes = attempts = throws = 0;
            clock = 0;
            result = Result.None;
            line = "";
            SetRules(g, CoachThere());
            g.UseArena(Arena.Classic, classic);
            g.ResetWithBots(CoachThere() ? 2u : 0u);
            if (CoachThere()) MakeCoach(g, 1, false, CoachRelease(g, 1), l.serveSpeed > 0 ? l.serveSpeed : 8f);
            else g.ParkBall(1);
            if (l.kind == Kind.STRIKES) g.ParkBall(1);
            g.match = MatchState.InProgress;   // (warming up while the card shows: nothing counts)
            flight[0].Reset(); flight[1].Reset();
            serve = 0; serving = false; serveT = waitT = 0; rechargeT = respawnT = -1; caughtArmed = false;
        }

        // 3-2-1, then the clock runs
        public void Start(Game g)
        {
            if (phase == Phase.Off) return;
            Lesson l = L;
            foreach (Target x in targets) { x.down = false; x.downT = -1; }
            Lessons.RefreshLive(l, targets);
            successes = attempts = throws = 0;
            clock = 0;
            result = Result.None;
            flight[0].Reset(); flight[1].Reset();
            serve = 0; serving = false; serveT = waitT = 0; rechargeT = respawnT = -1; caughtArmed = false;
            if (CoachThere())
            {   // its ball back on its spawner, nothing of its in the air
                g.pl[1].coach.fire = false;
                if (l.kind == Kind.STRIKES) g.ParkBall(1); else if (g.pl[1].ball.state != BallState.Held) g.RespawnBall(1);
            }
            phase = Phase.Countdown;
            g.events.Clear();
            g.StartCountdown();
            line = "";
        }

        // out of training: the game's rules as they were
        public void Stop(Game g)
        {
            if (hadSaved) g.tun.CopyFrom(saved);
            hadSaved = false;
            foreach (var v in g.aimAt) v.Clear();
            for (int p = 0; p < Teams.MaxPlayers; ++p) g.pl[p].coach.CopyFrom(new PlayerState.Coach());
            phase = Phase.Off;
            lesson = -1;
            targets.Clear();
        }

        void Finish(bool passed)
        {
            if (endless)
            {   // SOUND STUDIO: the moment goes on (no result, nothing kept or sent); the targets come back up
                foreach (Target x in targets) { x.down = false; x.downT = -1; }
                Lessons.RefreshLive(L, targets);
                successes = attempts = throws = 0;
                serve = 0;
                return;
            }
            phase = Phase.Done;
            result.lesson = lesson;
            result.passed = passed;
            result.successes = successes;
            result.attempts = attempts;
            result.time = clock;
            cues.Add(new CueEvent(passed ? Cue.Passed : Cue.Failed, default, successes));
            line = passed ? "PASSED" : "NOT THIS TIME";
        }

        // steps g; `in`: every place's input (two)
        public void Step(Game g, float dt, PlayerInput[] @in)
        {
            cues.Clear();
            Lesson l = L;
            if (!CoachThere())
            {   // nobody at the far end
                @in[1] = new PlayerInput();
                @in[1].head.p = new Vec3(7f, -50f, 0);
            }
            // aim assist toward the targets (as in a match it turns your throw a little toward the other player)
            if (l.kind == Kind.THROW || l.guard == Guard.G_SENDBACK) g.aimAt[0] = Lessons.AimPoints(l, targets, clock);
            else if (l.kind == Kind.STRIKES) g.aimAt[0] = new List<Vec3> { g.StrikeZoneCenter(1) + new Vec3(0, 0.02f, 0.45f), g.StrikeZoneCenter(1) + new Vec3(0, 0.02f, -0.45f) };
            else if (l.kind == Kind.CATCH) g.aimAt[0] = new List<Vec3> { new Vec3(9.4f, 1.6f, 0) };
            else g.aimAt[0].Clear();
            g.Step(dt, @in);
            if ((!CoachThere() || l.kind == Kind.STRIKES) && g.pl[1].ball.state != BallState.Dead) g.ParkBall(1);
            switch (phase)
            {
                case Phase.Countdown:
                    if (g.match == MatchState.InProgress) { phase = Phase.Play; clock = 0; cues.Add(new CueEvent(Cue.Go, default, 0)); }
                    break;
                case Phase.Play: clock += dt; StepPlay(g, dt); break;
            }
        }

        void StepPlay(Game g, float dt)
        {
            Lesson l = L;
            Ball mine = g.pl[0].ball;
            bool EndOf(int owner, Ball b, out Vec3 to)
            {   // where a ball got to this step (a wall it turned at)
                foreach (GameEvent e in g.events) if (e.type == EventType.Returned && e.player == owner) { to = e.pos; return true; }
                to = b.pos;
                return b.state == BallState.Flying;
            }
            void DropReturned(int owner) => g.events.RemoveAll(e => e.type == EventType.Returned && e.player == owner);   // (a ball a target took: no "back at you" for it)
            bool AllDown()
            {
                foreach (Target x in targets) if (!x.down) return false;
                return targets.Count > 0;
            }
            void TakeTarget(int i)
            {
                targets[i].down = true;
                targets[i].downT = clock;
                ++successes;
                cues.Add(new CueEvent(Cue.TargetDown, targets[i].At(clock), i));
                Lessons.RefreshLive(l, targets);
            }
            if (l.kind == Kind.THROW)
            {
                foreach (GameEvent e in g.events)
                    if (e.type == EventType.Throw && e.player == 0) { ++throws; ++attempts; flight[0].Start(mine); flight[0].prev = e.pos; }
                if (flight[0].on && EndOf(0, mine, out Vec3 to))
                {
                    int i = Lessons.Judge(l, targets, flight[0], mine, g.BallRadius(0), to, clock, cues);
                    if (i >= 0) { TakeTarget(i); g.RespawnBall(0); DropReturned(0); flight[0].on = false; }
                }
                if (mine.state != BallState.Flying) flight[0].on = false;
                if (AllDown()) Finish(true);
                else if (throws >= l.budget && !flight[0].on) Finish(false);
                line = throws >= l.budget - 2 && !AllDown() ? "LAST THROWS" : "";
                return;
            }
            if (l.kind == Kind.STRIKES)
            {
                foreach (GameEvent e in g.events)
                {
                    if (e.type == EventType.Throw && e.player == 0) { ++throws; ++attempts; flight[0].on = true; }
                    if (e.type == EventType.Strike && e.player == 1 && e.ball == 0) { ++successes; cues.Add(new CueEvent(Cue.Strike, e.pos, successes)); }
                    if (e.type == EventType.HitPlayer && e.player == 1 && e.ball == 0) cues.Add(new CueEvent(Cue.BodyHit, e.pos, 0));
                }
                g.pl[1].strikesAgainst = 0;   // (your ball stays its size: the next strike as hard as the first)
                if (mine.state != BallState.Flying) flight[0].on = false;
                if (successes >= l.pass) Finish(true);
                else if (throws >= l.budget && !flight[0].on) Finish(false);
                return;
            }
            if (l.kind == Kind.CATCH)
            {
                foreach (GameEvent e in g.events)
                {
                    if (e.type == EventType.Returned && e.player == 0 && e.value < 0.5f && !caughtArmed) { caughtArmed = true; ++attempts; }
                    else if (caughtArmed && e.type == EventType.Caught && e.player == 0)
                    {
                        ++successes; caughtArmed = false; cues.Add(new CueEvent(Cue.Caught, e.pos, successes));
                    }
                    else if (caughtArmed && ((e.type == EventType.HitPlayer && e.player == 0) ||
                                             (e.type == EventType.Returned && e.player == 0 && e.value > 0.5f)))
                    {
                        caughtArmed = false; cues.Add(new CueEvent(Cue.Dropped, e.pos, 0));
                    }
                }
                if (caughtArmed && mine.state != BallState.Returning && mine.state != BallState.Flying && mine.state != BallState.Held)
                {
                    caughtArmed = false; cues.Add(new CueEvent(Cue.Dropped, mine.pos, 0));
                }
                if (attempts >= l.budget && !caughtArmed) Finish(successes >= l.pass);
                return;
            }
            // DEFEND: the coach at the far end throws at you
            PlayerState co = g.pl[1];
            Ball cb = co.ball;
            waitT += dt;
            if (rechargeT >= 0 && (rechargeT -= dt) < 0)
            {   // SHIELD: here the charge comes back by itself
                if (!mine.charged) cues.Add(new CueEvent(Cue.Recharged, mine.pos, 0));
                mine.charged = true;
            }
            // where it aims (round you as you stand now): the lesson's pattern
            int k = serve;
            Vec3 chest = g.ChestPoint(0), head = g.pl[0].@in.head.p;
            Vec3 Pattern(Aim a, int n)
            {
                switch (a)
                {
                    case Aim.AIM_WIDE: return chest + kWide[n % 6];   // out of reach of a still body: high, low, left and right
                    case Aim.AIM_HEAD: return head + new Vec3(0, -0.02f, (n & 1) != 0 ? 0.06f : -0.06f);
                    case Aim.AIM_SHOULDERS: return chest + new Vec3(0, 0.22f, (n & 1) != 0 ? 0.20f : -0.20f);
                    default: return chest + new Vec3(0, 0, kSide[n % 10]);
                }
            }
            Vec3 aim = l.aim == Aim.AIM_MIXED ? Pattern(kMix[k % 5], k / 5 + k) : Pattern(l.aim, k);
            co.coach.dir = aim - co.coach.from;
            bool ready = (l.guard != Guard.G_SHIELD && l.guard != Guard.G_EITHER) || mine.state == BallState.Held || waitT > 6f;
            if (!serving && serve < l.budget && cb.state == BallState.Held && co.botThrow == 0 && waitT > 1.0f)
            {
                line = ready ? "" : "TAKE YOUR BALL: ITS CHARGE POWERS YOUR SHIELD";
                if (ready)
                {
                    co.coach.speed = l.serveSpeed + l.serveSpeedUp * serve;
                    co.coach.fire = true;
                    serving = true;
                    serveT = 0;
                    ++serve;
                    ++attempts;
                    flight[1].Reset();
                }
            }
            if (serving)
            {
                serveT += dt;
                bool done = false, ok = false;
                Vec3 at = cb.pos;
                foreach (GameEvent e in g.events)
                {
                    if (done) break;
                    if ((e.type == EventType.ShieldBlock || e.type == EventType.KnuckleBlock) && e.player == 0 && e.ball == 1)
                    {
                        bool shield = e.type == EventType.ShieldBlock;
                        if (shield && (l.guard == Guard.G_SHIELD || l.guard == Guard.G_EITHER)) rechargeT = 0.5f;
                        if (l.guard == Guard.G_SENDBACK) { flight[1].Start(cb); flight[1].prev = e.pos; continue; }   // (now: into a target?)
                        cues.Add(new CueEvent(shield ? Cue.Blocked : Cue.Punched, e.pos, 0));
                        done = true; ok = l.guard == Guard.G_SHIELD || l.guard == Guard.G_KNUCKLE || l.guard == Guard.G_EITHER; at = e.pos;
                    }
                    else if (e.type == EventType.HitPlayer && e.player == 0 && e.ball == 1)
                    {
                        cues.Add(new CueEvent(Cue.Hit, e.pos, 0));
                        done = true; at = e.pos;
                    }
                    else if (e.type == EventType.Returned && e.player == 1 && !flight[1].on)
                    {   // past you, into your wall
                        ok = l.guard == Guard.G_DODGE;
                        cues.Add(new CueEvent(ok ? Cue.Dodged : Cue.Dropped, e.pos, 0));
                        done = true; at = e.pos;
                    }
                    else if (e.type == EventType.HitPlayer && e.player == 1 && e.ball == 1 && flight[1].on)
                    {
                        cues.Add(new CueEvent(Cue.BodyHit, e.pos, 0));
                        done = true; at = e.pos;
                    }
                }
                if (!done && flight[1].on)
                {   // SEND IT BACK: your block on its way to the far end
                    if (EndOf(1, cb, out Vec3 to))
                    {
                        int i = Lessons.Judge(l, targets, flight[1], cb, g.BallRadius(1), to, clock, cues);
                        if (i >= 0) { TakeTarget(i); done = true; ok = true; DropReturned(1); }
                    }
                    if (!done && cb.state != BallState.Flying) { done = true; cues.Add(new CueEvent(Cue.Dropped, cb.pos, 0)); }
                }
                if (!done && serveT > (flight[1].on ? 6f : 4f) && cb.state != BallState.Held)
                {   // (it never got there)
                    ok = l.guard == Guard.G_DODGE;
                    cues.Add(new CueEvent(ok ? Cue.Dodged : Cue.Dropped, at, 0));
                    done = true;
                }
                if (done)
                {
                    serving = false;
                    waitT = 0;
                    flight[1].on = false;
                    if (ok && l.guard != Guard.G_SENDBACK) ++successes;
                    if (cb.state == BallState.Flying && cb.instigator == 0) respawnT = 0.8f;   // (your block flies on a moment)
                    else if (cb.state != BallState.Held && cb.state != BallState.Spawning) g.RespawnBall(1);   // (its next ball)
                }
            }
            if (respawnT >= 0 && (respawnT -= dt) < 0 && cb.state != BallState.Held && cb.state != BallState.Spawning) g.RespawnBall(1);
            if (l.guard == Guard.G_SENDBACK)
            {
                if (AllDown()) Finish(true);
                else if (serve >= l.budget && !serving) Finish(false);
            }
            else if (serve >= l.budget && !serving) Finish(successes >= l.pass);
        }

        static readonly float[] kSide = { 0, 0.18f, -0.18f, 0.08f, -0.25f, 0.25f, -0.08f, 0.15f, -0.15f, 0 };
        static readonly Vec3[] kWide = { new Vec3(0, 0.30f, 0.34f), new Vec3(0, -0.25f, -0.36f), new Vec3(0, 0.05f, 0.42f),
                                         new Vec3(0, 0.32f, -0.30f), new Vec3(0, -0.28f, 0.30f), new Vec3(0, 0.02f, -0.42f) };
        static readonly Aim[] kMix = { Aim.AIM_CHEST, Aim.AIM_HEAD, Aim.AIM_SHOULDERS, Aim.AIM_WIDE, Aim.AIM_SHOULDERS };
    }
}
