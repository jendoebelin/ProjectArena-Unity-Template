// RECORD MY MATCHES (native record.h / record.cpp, ported 1:1: task A7; owner 2026-10-26): the matches this headset
// plays, frame by frame, for tuning the practice bot from real play (the native repo's tools/analyze_play.py reads the
// same files). Off unless the player turns it on; the files stay on the headset (the newest Keep kept) and leave it
// only if the player sends one. Only the player's own motion is kept: an opponent who is a person appears only as their
// ball and the game's events; a bot opponent's motion is kept too (it is not a person). Replay reads one back
// (TRAINING's SHOW ME HOW plays one, once there are lesson recordings).
// File (little-endian): "PAREC1", then the header, then records, each starting with a tag byte:
//   header: u8 local player, u8 opponent is a bot, u8 bot level, u8 MATCH MY LEVEL on, f32 its level then,
//           u8 Advanced, u8 shields, i8 knuckle mode, u8 TRAINING's lesson (its id; 0: none), f32 match time,
//           f32 round time, char[16] game version
//   'F' a step: f32 game time, f32 step length, u8 match state, u8 score[2]; per player: u8 motion kept, and if so
//       head (pos xyz, quat xyzw), per hand (pos, quat, trigger, squeeze, u8 tracked); per player's ball: u8 state,
//       i8 instigator, pos, vel, spin, f32 scale, u8 charged, i8 held hand
//   'E' an event of that step: u8 type, i8 player, i8 hand, i8 ball, pos, f32 value
//   'X' the end: f32 game time
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ProjectArena.Sim
{
    public static class Rec
    {
        public const int Keep = 20;               // recordings kept (the oldest go first)
        public const float MaxSeconds = 900f;     // one recording ends after 15 minutes of play (about 25 MB)

        // the recordings in `dir` beyond the newest `keep` are deleted (names sort by time: match_YYYYMMDD_HHMMSS.par)
        public static void Prune(string dir, int keep)
        {
            if (!Directory.Exists(dir)) return;
            var names = new List<string>();
            foreach (string f in Directory.GetFiles(dir, "match_*.par")) names.Add(Path.GetFileName(f));
            names.Sort(StringComparer.Ordinal);
            for (int i = 0; i + keep < names.Count; ++i) try { File.Delete(Path.Combine(dir, names[i])); } catch (IOException) { }
        }
    }

    public struct RecMeta
    {
        public int local;
        public string version;
        public int lesson;   // TRAINING: the lesson played (its id)
    }

    public class Recorder : IDisposable
    {
        BinaryWriter w;
        int local;
        bool keepOpp;
        float seconds;
        public string Path { get; private set; } = "";
        public bool Active => w != null;

        // false: the file can't be written
        public bool Begin(string path, Game g, RecMeta m)
        {
            Close();
            try { w = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write)); }
            catch (Exception) { w = null; return false; }
            Path = path;
            local = m.local & 1;
            keepOpp = g.pl[1 - local].isBot;   // (a person opponent's motion is never kept)
            seconds = 0;
            w.Write(Encoding.ASCII.GetBytes("PAREC1"));
            w.Write((byte)local);
            w.Write((byte)(keepOpp ? 1 : 0));
            int lv = g.pl[1 - local].botLevel >= 0 ? g.pl[1 - local].botLevel : g.tun.botLevel;
            w.Write((byte)lv);
            w.Write((byte)(g.tun.botAdapt ? 1 : 0));
            w.Write(g.tun.botMatch);
            w.Write((byte)(g.tun.advanced ? 1 : 0));
            w.Write((byte)(g.tun.shields ? 1 : 0));
            w.Write((sbyte)g.tun.knuckleMode);
            w.Write((byte)Math.Max(0, Math.Min(255, m.lesson)));
            w.Write(g.tun.basicMatchTime);
            w.Write(g.tun.advancedRoundTime);
            var ver = new byte[16];
            byte[] v = Encoding.ASCII.GetBytes(m.version ?? "");
            Array.Copy(v, ver, Math.Min(v.Length, 15));
            w.Write(ver);
            return true;
        }

        void Vec(Vec3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        void PoseOut(Pose p) { Vec(p.p); w.Write(p.q.x); w.Write(p.q.y); w.Write(p.q.z); w.Write(p.q.w); }

        // after each step: the step and its events
        public void Frame(Game g, float dt)
        {
            if (w == null) return;
            w.Write((byte)'F');
            w.Write(g.now);
            w.Write(dt);
            w.Write((byte)g.match);
            w.Write((byte)Math.Min(255, Math.Max(0, g.pl[0].score)));
            w.Write((byte)Math.Min(255, Math.Max(0, g.pl[1].score)));
            for (int p = 0; p < 2; ++p)
            {
                bool keep = p == local || keepOpp;
                w.Write((byte)(keep ? 1 : 0));
                if (!keep) continue;
                PlayerInput @in = g.pl[p].@in;
                PoseOut(@in.head);
                foreach (HandInput h in @in.hand)
                {
                    PoseOut(h.grip);
                    w.Write(h.trigger);
                    w.Write(h.squeeze);
                    w.Write((byte)(h.tracked ? 1 : 0));
                }
            }
            for (int p = 0; p < 2; ++p)
            {
                Ball b = g.pl[p].ball;
                w.Write((byte)b.state);
                w.Write((sbyte)b.instigator);
                Vec(b.pos); Vec(b.vel); Vec(b.spin);
                w.Write(b.scale);
                w.Write((byte)(b.charged ? 1 : 0));
                w.Write((sbyte)b.heldHand);
            }
            foreach (GameEvent e in g.events)
            {
                w.Write((byte)'E');
                w.Write((byte)e.type);
                w.Write((sbyte)e.player);
                w.Write((sbyte)e.hand);
                w.Write((sbyte)e.ball);
                Vec(e.pos);
                w.Write(e.value);
            }
            if ((seconds += dt) > Rec.MaxSeconds) End(g);
        }

        public void End(Game g)
        {
            if (w == null) return;
            w.Write((byte)'X');
            w.Write(g.now);
            Close();
        }

        void Close()
        {
            w?.Dispose();
            w = null;
        }

        public void Dispose() => Close();
    }

    // A recording read back (TRAINING's tutorial videos replay one): each step's players and balls, and its events
    public class Replay
    {
        public struct BallAt { public BallState state; public int instigator, heldHand; public Vec3 pos, vel, spin; public float scale; public bool charged; }
        public class Frame
        {
            public float t;
            public MatchState match = MatchState.WaitingForPlayers;
            public bool[] kept = new bool[2];   // that player's motion is in it
            public PlayerInput[] @in = { new PlayerInput(), new PlayerInput() };
            public BallAt[] ball = new BallAt[2];
            public List<GameEvent> events = new List<GameEvent>();
        }
        public int local, lesson;
        public List<Frame> frames = new List<Frame>();

        // false: not a recording (or cut short before its first step)
        public bool Load(byte[] d)
        {
            frames.Clear();
            int at = 0;
            bool ok = true;
            bool Need(int k) { if (at + k > d.Length) { ok = false; return false; } return true; }
            byte U8() { if (!Need(1)) return 0; return d[at++]; }
            sbyte I8() => (sbyte)U8();
            float F32() { if (!Need(4)) return 0; float v = BitConverter.ToSingle(d, at); at += 4; return v; }
            Vec3 V() { float x = F32(), y = F32(), z = F32(); return new Vec3(x, y, z); }
            Pose P() { Vec3 p = V(); float x = F32(), y = F32(), z = F32(), ww = F32(); return new Pose(new Quat(x, y, z, ww), p); }
            if (d.Length < 6 || Encoding.ASCII.GetString(d, 0, 6) != "PAREC1") return false;
            at = 6;
            local = U8() & 1;
            U8(); U8(); U8(); F32(); U8(); U8(); I8();
            lesson = U8();
            F32(); F32();
            if (Need(16)) at += 16;
            while (ok && at < d.Length)
            {
                byte tag = U8();
                if (tag == 'F')
                {
                    var f = new Frame();
                    f.t = F32(); F32();
                    f.match = (MatchState)U8(); U8(); U8();
                    for (int p = 0; p < 2; ++p)
                    {
                        f.kept[p] = U8() != 0;
                        if (!f.kept[p]) continue;
                        PlayerInput @in = f.@in[p];
                        @in.headTracked = true;
                        @in.head = P();
                        for (int h = 0; h < 2; ++h)
                        {
                            @in.hand[h].grip = P(); @in.hand[h].trigger = F32(); @in.hand[h].squeeze = F32(); @in.hand[h].tracked = U8() != 0;
                        }
                    }
                    for (int p = 0; p < 2; ++p)
                    {
                        BallAt b = default;
                        b.state = (BallState)U8(); b.instigator = I8();
                        b.pos = V(); b.vel = V(); b.spin = V();
                        b.scale = F32(); b.charged = U8() != 0; b.heldHand = I8();
                        f.ball[p] = b;
                    }
                    if (ok) frames.Add(f);
                }
                else if (tag == 'E')
                {
                    var e = new GameEvent((EventType)U8(), I8(), I8(), default, 0);
                    e.ball = I8(); e.pos = V(); e.value = F32();
                    if (ok && frames.Count > 0) frames[frames.Count - 1].events.Add(e);
                }
                else break;   // 'X' (the end), or something this build doesn't know
            }
            return frames.Count > 0;
        }
    }
}
