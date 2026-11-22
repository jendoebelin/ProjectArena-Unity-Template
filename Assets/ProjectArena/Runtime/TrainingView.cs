// TRAINING in the app (task A6), as native main.cpp has it (native aaf0cab: beginLesson, stopTraining, askBoard, the
// training branch of stepFrame, trainingFeedback, trainPop, drawTraining, saveTraining / loadTraining): a lesson's card
// (the coach at the far end showing it), the 3-2-1, the lesson stepping the game, what just happened where it happened
// (HIT!, TOO SLOW, DODGED!...), the targets on the court, the lesson's board over centre court, your bests on this
// headset (training.cfg) and sent to the leaderboards once online. The pages (the lessons, a lesson's card and result,
// the leaderboards) are Menu's. Training is played offline. SHOW ME HOW (a video of a real run) needs the lessons'
// recordings, which the open assets don't have yet (natively too: VIDEO COMING SOON).
using System;
using System.Collections.Generic;
using System.IO;
using ProjectArena.Sim;
using ProjectArena.Sim.Training;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    public class TrainingApp
    {
        public readonly Trainer trainer = new Trainer();
        public readonly Bests bests = new Bests();
        // CUSTOM TRAINING (TASKS.md E33, native 3c285d9): the lessons page's mode (MY OPTIONS: the lessons with your own room
        // options, saved as trainmine; or STANDARD: the rules' own, for the leaderboards), the bests with MY OPTIONS
        // (training_mine.cfg: never sent), and MY OPTIONS's page (over the lessons page)
        public bool mine;
        public readonly Bests bestsMine = new Bests();
        public bool optsPage;
        // a lesson open in either mode (one passed with MY OPTIONS opens the next too: the progress is yours)
        public bool Unlocked(int i) => bests.Unlocked(i) || bestsMine.Unlocked(i);
        // your own rules (while training: the rules the lessons were started from, put back after), where MY OPTIONS are
        public Tunables MyRules => trainer.Active ? trainer.SavedRules : a.game.tun;
        public bool open;              // the lessons page
        public int board = -2;         // a leaderboard page: the lesson's index (-1: overall; -2: none)
        public int boardKind;          // 0 FASTEST, 1 MOST ACCURATE
        public double boardAsked;
        public bool newBest;           // the lesson just over set a new best
        public double menuAt;          // the card opens in front of you once you are on the court (0: not waiting)
        public const bool HaveVideo = false;   // (SHOW ME HOW: no recordings in the open assets yet)
        bool prevA;
        public class Pop { public string text; public Vec3 pos; public float t; public Vector3 rgb; public float size; }
        public readonly List<Pop> pops = new List<Pop>();
        public struct Sound { public string key, fallback; public float gain, pan; }
        public readonly List<Sound> sounds = new List<Sound>();   // this frame's (MatchView plays them)
        public int buzzHand = -1;      // a short buzz on this hand this frame (-1: none)
        readonly AppState a;
        public List<CollisionTri> classic;   // the classic court (ArenaSession.classicArena)

        public TrainingApp(AppState a) => this.a = a;
        public bool Active => trainer.Active;
        Online.Net.Mode Mode => a.net != null ? a.net.CurrentMode : Online.Net.Mode.Offline;

        // out of TRAINING: the game as it was (practice against the bot)
        public void Stop()
        {
            if (!trainer.Active) return;
            trainer.Stop(a.game);
            a.game.Reset(true);
            pops.Clear();
            Debug.Log("[Training] left");
        }

        // its card (the coach shows it), or straight to 3-2-1; custom: CUSTOM TRAINING, with MY OPTIONS (SOUND STUDIO: never)
        public void BeginLesson(int i, bool go, Menu menu, bool custom = false)
        {
            optsPage = false;
            trainer.custom = custom;
            if (Mode != Online.Net.Mode.Offline) return;   // (training is played offline)
            i = Math.Max(0, Math.Min(Lessons.All.Count - 1, i));
            a.lobby = false;
            if (menu != null) menu.titleOpen = false;
            open = false;
            board = -2;
            newBest = false;
            pops.Clear();
            trainer.Begin(a.game, i, classic ?? new List<CollisionTri>());
            trainer.endless = false;   // (SOUND STUDIO sets it after)
            Debug.Log($"[Training] lesson {Lessons.All[i].id} {Lessons.All[i].name}");
            if (go) { trainer.Start(a.game); if (menu != null) menu.open = false; }
            else { if (menu != null) menu.open = false; menuAt = Time.realtimeSinceStartupAsDouble + 0.35; }   // (the card opens in front of you on the court)
        }

        public void AskBoard()
        {
            if (board < -1) return;
            int id = board < 0 ? 0 : Lessons.All[board].id;
            a.net?.RequestBoard(id, board < 0 ? 1 : boardKind);
            boardAsked = Time.realtimeSinceStartupAsDouble;
        }

        // one frame of a lesson (instead of the network exchange): the A button, the card, the lesson stepping the game
        // (paused while its menu is up mid-lesson), then the feedback. in: two entries
        public void Step(PlayerInput mine, PlayerInput[] @in, float dt, Menu menu, bool recentered)
        {
            @in[0].CopyFrom(mine);
            Phase ph = trainer.phase;
            bool mid = ph == Phase.Play || ph == Phase.Countdown;
            if (menuAt > 0 && Time.realtimeSinceStartupAsDouble > menuAt && recentered && menu != null)
            {   // the card, now you are on the court
                menuAt = 0;
                menu.Open(a.localIn.head, a.game.tun.start[0]);
            }
            bool A = mine.buttonA && !prevA;
            prevA = mine.buttonA;
            if (A && trainer.phase == Phase.Card) { trainer.Start(a.game); if (menu != null) menu.open = false; menuAt = 0; Debug.Log("[Training] started (A)"); }
            else if (A && trainer.phase == Phase.Done)
            {
                int nx = Lessons.NextInTrack(trainer.lesson);
                bool next = trainer.result.passed && nx >= 0;
                Debug.Log("[Training] A -> " + (next ? "next lesson" : "try again"));
                BeginLesson(next ? nx : trainer.lesson, !next, menu, trainer.custom);
            }
            else if (trainer.phase == Phase.Card && (menu == null || !menu.open) && menuAt == 0)
            {
                trainer.Start(a.game); Debug.Log("[Training] started (card closed)");
            }
            // (SOUND STUDIO: its moment plays on with its page open beside you, to change a sound and hear it at once;
            // native, the owner's test 2026-11-06)
            bool studio = a.studio != null && a.studio.on;
            if (!(a.builderOpen || (menu != null && menu.open && mid && !studio))) trainer.Step(a.game, dt, @in);
            else a.game.events.Clear();
            Feedback(dt, ph, menu);
        }

        void PopAt(string text, Vec3 at, float r, float g, float b, float size)
        {
            if (pops.Count >= 8) pops.RemoveAt(0);
            pops.Add(new Pop { text = text, pos = at, t = 0f, rgb = new Vector3(r, g, b), size = size });
        }

        // native trainingFeedback: the cues as sounds, words where they happened and a buzz; the lesson over: the result
        // kept, sent, shown
        void Feedback(float dt, Phase before, Menu menu)
        {
            sounds.Clear();
            buzzHand = -1;
            for (int i = pops.Count - 1; i >= 0; --i) { pops[i].t += dt; if (pops[i].t > 2f) pops.RemoveAt(i); }
            Vec3 ear = a.game.pl[0].@in.head.p;
            float Pan(Vec3 p) => M.Clampf((p.z - ear.z) * 0.4f, -0.8f, 0.8f);
            void Snd(string key, string fallback, Vec3 p, float gain) => sounds.Add(new Sound { key = key, fallback = fallback, gain = gain, pan = Pan(p) });
            foreach (CueEvent c in trainer.cues)
            {
                bool onWall = c.pos.x > 3f;   // (on the far wall: big letters)
                float big = onWall ? 0.32f : 0.12f;
                switch (c.cue)
                {
                    case Cue.TargetDown:
                        Snd("TargetHit", "Strike", c.pos, 1f);
                        PopAt("HIT!", c.pos + new Vec3(-0.3f, 0.6f, 0), 1f, 0.85f, 0.3f, big);
                        buzzHand = a.settings != null ? a.settings.pointerHand : 1;
                        break;
                    case Cue.Ring: Snd("Recharge", null, c.pos, 0.7f); PopAt("THROUGH!", c.pos + new Vec3(0, 0.55f, 0), 0.4f, 1f, 0.6f, 0.2f); break;
                    case Cue.TooSlow: Snd("ThrowFail", null, c.pos, 0.8f); PopAt("TOO SLOW", c.pos + new Vec3(-0.3f, 0.6f, 0), 1f, 0.45f, 0.3f, big); break;
                    case Cue.NotBanked: Snd("ThrowFail", null, c.pos, 0.8f); PopAt("OFF A WALL FIRST", c.pos + new Vec3(-0.3f, 0.6f, 0), 1f, 0.45f, 0.3f, big); break;
                    case Cue.NoRing: Snd("ThrowFail", null, c.pos, 0.8f); PopAt("THROUGH THE RING FIRST", c.pos + new Vec3(-0.3f, 0.6f, 0), 1f, 0.45f, 0.3f, big); break;
                    case Cue.Strike: PopAt("STRIKE!", c.pos + new Vec3(0, 0.9f, 0), 1f, 0.85f, 0.3f, 0.3f); break;
                    case Cue.BodyHit: PopAt("PAST THE BODY, INTO THE RING", c.pos + new Vec3(0, 0.9f, 0), 1f, 0.45f, 0.3f, 0.2f); break;
                    case Cue.Blocked: PopAt("BLOCKED!", c.pos + new Vec3(0.6f, 0.35f, 0), 0.4f, 0.9f, 1f, 0.07f); break;
                    case Cue.Punched: PopAt("PUNCHED!", c.pos + new Vec3(0.6f, 0.35f, 0), 0.4f, 0.9f, 1f, 0.07f); break;
                    case Cue.Dodged: Snd("Recharge", null, c.pos, 0.6f); PopAt("DODGED!", ear + new Vec3(1.2f, 0.25f, 0), 0.4f, 1f, 0.6f, 0.08f); break;
                    case Cue.Hit: PopAt("HIT - TRY AGAIN", ear + new Vec3(1.2f, 0.25f, 0), 1f, 0.45f, 0.3f, 0.07f); break;
                    case Cue.Caught: PopAt("CAUGHT!", c.pos + new Vec3(0.6f, 0.3f, 0), 0.4f, 1f, 0.6f, 0.07f); break;
                    case Cue.Dropped: Snd("ThrowFail", null, c.pos, 0.6f); PopAt("MISSED", ear + new Vec3(1.2f, 0.25f, 0), 1f, 0.45f, 0.3f, 0.07f); break;
                    case Cue.Go: Snd("RoundStart", null, ear, 1f); break;
                    case Cue.Passed: Snd("MatchEndWin", null, ear, 1f); break;
                    case Cue.Failed: Snd("MatchEndLose", null, ear, 1f); break;
                }
            }
            if (before != Phase.Done && trainer.phase == Phase.Done)
            {   // the lesson over: the result kept, sent, shown
                Result r = trainer.result;
                Lesson L = Lessons.All[r.lesson];
                // (CUSTOM TRAINING: a run with MY OPTIONS kept with their own bests, on the headset: never sent)
                Bests B = trainer.custom ? bestsMine : bests;
                newBest = B.Add(r);
                Debug.Log($"[Training] {L.name} {(r.passed ? "passed" : "not passed")}, {r.successes} / {r.attempts}, {r.time:F1} s{(newBest ? " (a best)" : "")}{(trainer.custom ? " (my options)" : "")}");
                if (newBest)
                {
                    Save();
                    Best b = B.b[L.id];
                    if (!trainer.custom) a.net?.SubmitTraining(L.id, L.Timed() ? b.time : 0f, b.successes, b.attempts);
                }
                menu?.Open(a.localIn.head, a.game.tun.start[0]);
            }
        }

        // your bests (training.cfg, in the settings' folder; with MY OPTIONS: training_mine.cfg)
        string FilePath => a.settings != null ? Path.Combine(a.settings.Folder, "training.cfg") : null;
        string MinePath => a.settings != null ? Path.Combine(a.settings.Folder, "training_mine.cfg") : null;
        public void Save()
        {
            if (FilePath == null) return;
            try
            {
                Directory.CreateDirectory(a.settings.Folder);
                File.WriteAllText(FilePath, bests.Save());
                File.WriteAllText(MinePath, bestsMine.Save());
            }
            catch (Exception e) { Debug.LogWarning("[Training] save: " + e.Message); }
        }
        // ... and each one sent again (the server keeps the better; queued until online)
        public void Load()
        {
            if (FilePath == null) return;
            try { if (File.Exists(FilePath)) bests.Load(File.ReadAllText(FilePath)); } catch (Exception e) { Debug.LogWarning("[Training] load: " + e.Message); }
            try { if (File.Exists(MinePath)) bestsMine.Load(File.ReadAllText(MinePath)); } catch (Exception e) { Debug.LogWarning("[Training] load: " + e.Message); }   // (never sent)
            mine = a.settings.trainMine;
            foreach (Lesson L in Lessons.All)
            {
                Best b = bests.b[L.id];
                if (b.passed && b.attempts > 0) a.net?.SubmitTraining(L.id, L.Timed() ? b.time : 0f, b.successes, b.attempts);
            }
        }

        static Vector4 V(float r, float g, float b, float al = 1) => new Vector4(r, g, b, al);
        static Quat FaceMe(Vec3 p, Vec3 eye)
        {
            Vec3 d = p - eye; d.y = 0;
            return ExpArenaView.YawToward(M.Length(d) > 1e-3f ? M.Normalize(d) : new Vec3(1, 0, 0));
        }

        // native drawTraining: COACH over the coach, the targets (a glowing disc with rings; taken: a burst of rings), the
        // lesson's board over centre court, the words where things happened
        public string ready = "A";   // READY's button (BUTTONS: A as standard), for PRESS A TO START
        public void Draw(FxRenderer fx, Game g, Vec3 eye, float now)
        {
            if (!trainer.Active) return;
            Lesson L = trainer.L;
            var circ = new Vec3[33];
            void Circle(Vec3 c, float r, Vec3 u, Vec3 v, float w, Vector4 col)
            {
                for (int i = 0; i <= 32; ++i)
                {
                    float t0 = i * (6.2831853f / 32);
                    circ[i] = c + u * (r * MathF.Cos(t0)) + v * (r * MathF.Sin(t0));
                }
                fx.Ribbon(circ, 33, w, col, eye, false);
            }
            Vector4 gold = V(1.0f, 0.85f, 0.35f);
            if (trainer.CoachThere())
            {
                Vec3 over = g.pl[1].@in.head.p + new Vec3(0, 0.45f, 0);
                fx.Text("COACH", new Pose(FaceMe(over, eye), over), 0.16f, gold);
            }
            List<Target> tg = trainer.targets;
            float t = trainer.clock;
            var bs = new Vector3(1.0f, 0.78f, 0.25f);
            if (L.minSpeed > 0) bs = new Vector3(1.0f, 0.35f, 0.2f);
            else if (L.bank) bs = new Vector3(0.35f, 1.0f, 0.5f);
            else if (L.targets.Count > 0 && L.targets[0].hoopR > 0) bs = new Vector3(0.3f, 0.85f, 1.0f);
            Vec3 up = new Vec3(0, 1, 0), side = new Vec3(0, 0, 1);
            var ln = new Vec3[2];
            for (int i = 0; i < tg.Count; ++i)
            {
                Target x = tg[i];
                if (!x.live) continue;
                Vec3 c = x.At(t);
                if (x.down)
                {   // taken: a burst of rings, fading
                    float k = M.Clampf((t - x.downT) / 0.6f, 0f, 1f);
                    if (k >= 1f) continue;
                    var col = V(1f, 0.95f, 0.7f, 1f - k);
                    Circle(c, x.d.r * (1f + 2.5f * k), up, side, 0.05f * (1f - k) + 0.01f, col);
                    Circle(c, x.d.r * (0.5f + 1.5f * k), up, side, 0.03f * (1f - k) + 0.01f, col);
                    continue;
                }
                float pulse = 0.75f + 0.25f * MathF.Sin(now * 4f + i);
                fx.SpriteFlat(c + new Vec3(-0.02f, 0, 0), 2.2f * x.d.r, V(bs.x * 0.35f * pulse, bs.y * 0.35f * pulse, bs.z * 0.35f * pulse), new Vec3(-1, 0, 0), 1);
                var rim = V(bs.x, bs.y, bs.z);
                Circle(c + new Vec3(-0.03f, 0, 0), x.d.r, up, side, 0.035f, rim);
                Circle(c + new Vec3(-0.03f, 0, 0), x.d.r * 0.55f, up, side, 0.025f, rim);
                fx.Glow(c + new Vec3(-0.05f, 0, 0), 0.06f, V(1, 1, 1, 0.9f));
                if (c.x < Lessons.kWallX - 0.1f)
                {   // (in the air: a thin beam down to its spot on the floor, so you can judge how far)
                    ln[0] = c - new Vec3(0, x.d.r, 0); ln[1] = new Vec3(c.x, 0.01f, c.z);
                    fx.Ribbon(ln, 2, 0.012f, V(bs.x * 0.5f, bs.y * 0.5f, bs.z * 0.5f), eye, false);
                    fx.SpriteFlat(new Vec3(c.x, 0.008f, c.z), 0.6f, V(bs.x * 0.3f, bs.y * 0.3f, bs.z * 0.3f), up, 1);
                }
                if (x.d.hoopR > 0)
                {   // its ring in the air, and a faint line from it to the target
                    Circle(x.d.hoop, x.d.hoopR, up, side, 0.04f * pulse, rim);
                    Circle(x.d.hoop, x.d.hoopR * 0.15f, up, side, 0.02f, V(bs.x * 0.3f, bs.y * 0.3f, bs.z * 0.3f));
                }
            }
            {   // the board over centre court, facing you
                Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), M.Deg2Rad(-90f));
                var c0 = new Vec3(-0.5f, 2.95f, 0);
                Vec3 At(float u, float v) => c0 + q.Rotate(new Vec3(u, v, 0.004f));
                void Txt(string s, float u, float v, float h, Vector4 col) => fx.Text(s, new Pose(q, At(u, v)), h, col);
                var edge = V(bs.x, bs.y, bs.z, 0.95f);
                Vector4 white = V(1, 1, 1), grey = V(0.62f, 0.68f, 0.76f);
                fx.Panel(new Pose(q, c0 + q.Rotate(new Vec3(0, 0, -0.01f))), 2.4f, 0.66f, V(0.004f, 0.008f, 0.018f, 0.72f));
                fx.Panel(new Pose(q, c0 + q.Rotate(new Vec3(0, 0.33f, -0.005f))), 2.4f, 0.02f, edge);
                Txt($"{Lessons.TrackName((int)L.track)} {Lessons.PlaceInTrack(trainer.lesson, out _)} - {L.name}", 0, 0.24f, 0.06f, gold);
                Phase ph = trainer.phase;
                if (ph == Phase.Card) Txt("PRESS " + ready + " TO START", 0, 0.05f, 0.11f, white);
                else if (ph == Phase.Countdown) Txt(Math.Max(1, (int)MathF.Ceiling(g.stateTimer)).ToString(), 0, 0.05f, 0.24f, white);
                else if (ph == Phase.Done) Txt(trainer.result.passed ? "PASSED!" : "NOT THIS TIME", 0, 0.05f, 0.16f, trainer.result.passed ? gold : white);
                else
                {
                    int cs = (int)(trainer.clock * 10f);
                    Txt(L.Timed() ? $"{cs / 600}:{(cs / 10) % 60:00}.{cs % 10}" : $"{trainer.attempts} / {L.budget}", 0, 0.05f, 0.2f, white);
                }
                string what = L.kind == Kind.THROW || L.guard == Guard.G_SENDBACK ? "TARGETS" : L.kind == Kind.STRIKES ? "STRIKES" : L.kind == Kind.CATCH ? "CAUGHT"
                            : L.guard == Guard.G_SHIELD || L.guard == Guard.G_EITHER ? "BLOCKED" : L.guard == Guard.G_KNUCKLE ? "PUNCHED" : "DODGED";
                Txt($"{what} {trainer.successes} / {L.Goal()}", -0.8f, 0.05f, 0.07f, edge);
                bool throwsCount = L.kind == Kind.THROW || L.kind == Kind.STRIKES;
                int left = throwsCount ? L.budget - trainer.throws : L.budget - trainer.attempts;
                Txt($"{(throwsCount ? "THROWS" : "TRIES")} LEFT {Math.Max(0, left)}", 0.8f, 0.10f, 0.05f, grey);
                if (trainer.attempts > 0) Txt($"ACCURACY {trainer.successes * 100 / trainer.attempts}%", 0.8f, 0.0f, 0.05f, grey);
                if (ph == Phase.Done) Txt(ready + (trainer.result.passed && Lessons.NextInTrack(trainer.lesson) >= 0 ? ": NEXT LESSON  -  MENU BUTTON: MORE" : ": TRY AGAIN  -  MENU BUTTON: MORE"),
                                          0, -0.2f, 0.045f, gold);
                else Txt(trainer.line.Length > 0 ? trainer.line : L.tip1, 0, -0.2f, 0.04f, trainer.line.Length == 0 ? grey : gold);
            }
            foreach (Pop p in pops)
            {   // what just happened, where it happened
                float fade = M.Clampf((2f - p.t) / 0.6f, 0f, 1f);
                Vec3 at = p.pos + new Vec3(0, 0.25f * p.t, 0);
                fx.Text(p.text, new Pose(FaceMe(at, eye), at), p.size, V(p.rgb.x, p.rgb.y, p.rgb.z, fade));
            }
        }
    }
}
