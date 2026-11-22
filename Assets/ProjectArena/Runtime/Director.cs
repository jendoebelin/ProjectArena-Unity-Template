using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ProjectArena.Sim;
using UnityEngine;
using EventType = ProjectArena.Sim.EventType;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The spectator's cameras (native spectatorCamera, director, directorEvents, addCamHere, goToCam, the WarpCam list,
    // warps.cfg, packWarps / unpackWarps, and DIRECTOR TOOLS: dirButtons, openDirector, dirActivate, updateDirector,
    // drawDirector, the info preview). Watching a match: the left stick flies (head-relative), the right stick turns
    // (SETTINGS > TURNING: snap 30 degrees, or smooth) and rises / sinks; A saves a camera here, X goes to the next one,
    // Y turns the auto camera on or off. The auto camera: timer and follow cameras take turns, each for its hold time;
    // an action camera takes over when a hit, strike or match end happens near it; a follow camera glides along the
    // court with the ball. A team shares its cameras through the arena host (Net.ShareCameras).
    // One difference: the native cross-fade blends the captured old view over the new one; here the view fades through
    // dark (Unity's XR rendering has no such capture).
    public class Director
    {
        public class WarpCam
        {
            public Pose pose;      // head pose in arena space (upright)
            public byte hold = 1;  // 0..3 -> 2 / 4 / 8 / 15 s
            public byte trans = 1; // 0 cut, 1 cross-fade
            public byte trig;      // 0 timer (in turn), 1 action (hits / strikes nearby), 2 follow (glide with the ball)
            public float pitch, fov;   // the PC spectator's cameras (native 0.3.13): looking up or down, its zoom (0: as the view is)
        }

        static readonly float[] HoldSecs = { 2, 4, 8, 15 };
        public const float DirW = 1.12f, DirH = 0.98f;
        public readonly List<WarpCam> warps = new List<WarpCam>();
        public int warpIdx = -1, dirRequest = -1;
        public bool autoCam, open;
        double autoNext;
        public Pose pose = Pose.Identity;   // the panel
        public int hover = -1, infoKind = -1;
        public double infoStart;
        bool hitValid, prevTrig = true, prevA = true, prevX = true, prevY = true, turnLatch;
        Vec3 hit;
        // the move to a camera with a cross-fade: fading out, then (moved) fading in
        Pose fadeTarget;
        float fadeOut, fadeIn;
        bool fading;

        readonly ArenaSession s;
        AppState a => s.a;
        public Director(ArenaSession s) => this.s = s;

        string File => Path.Combine(a.settings.Folder, "warps.cfg");

        // ---------------------------------------------------------------- warps.cfg and team sharing
        public void Load()
        {
            warps.Clear();
            try
            {
                if (!System.IO.File.Exists(File)) return;
                foreach (string line in System.IO.File.ReadAllLines(File))
                {
                    if (warps.Count >= 8) break;
                    string[] w = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (w.Length < 4) continue;
                    float F(int i) => float.Parse(w[i], CultureInfo.InvariantCulture);
                    int I(int i, int d) => w.Length > i && int.TryParse(w[i], out int v) ? v : d;
                    try
                    {
                        warps.Add(new WarpCam
                        {
                            pose = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), F(3)), new Vec3(F(0), F(1), F(2))),
                            hold = (byte)(I(4, 1) & 3), trans = (byte)(I(5, 1) & 1), trig = (byte)Math.Min(2, Math.Max(0, I(6, 0))),
                            pitch = w.Length > 7 ? F(7) : 0f, fov = w.Length > 8 ? F(8) : 0f,
                        });
                    }
                    catch (FormatException) { }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Director] could not read warps.cfg: " + e.Message); }
        }

        static float Yaw(Pose p) { Vec3 f = p.q.Rotate(new Vec3(0, 0, -1)); return MathF.Atan2(-f.x, -f.z); }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(a.settings.Folder);
                var sb = new System.Text.StringBuilder();
                foreach (var w in warps)
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} {3:F4} {4} {5} {6} {7:F4} {8:F1}\n",
                                            w.pose.p.x, w.pose.p.y, w.pose.p.z, Yaw(w.pose), w.hold, w.trans, w.trig, w.pitch, w.fov));   // (native 0.3.13: and the PC's pitch and zoom)
                System.IO.File.WriteAllText(File, sb.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[Director] could not save warps.cfg: " + e.Message); }
        }

        // for team sharing: 19 bytes per camera (x, y, z, yaw floats; hold, trans, trig)
        public byte[] Pack()
        {
            var b = new List<byte> { (byte)warps.Count };
            foreach (var w in warps)
            {
                foreach (float v in new[] { w.pose.p.x, w.pose.p.y, w.pose.p.z, Yaw(w.pose) }) b.AddRange(BitConverter.GetBytes(v));
                b.Add(w.hold); b.Add(w.trans); b.Add(w.trig);
            }
            return b.ToArray();
        }

        public bool Unpack(byte[] b)
        {
            if (b == null || b.Length == 0 || b[0] > 8 || b.Length < 1 + 19 * b[0]) return false;
            var ws = new List<WarpCam>();
            for (int i = 0; i < b[0]; ++i)
            {
                float V(int k) => BitConverter.ToSingle(b, 1 + 19 * i + 4 * k);
                float x = V(0), y = V(1), z = V(2), yaw = V(3);
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || Math.Abs(x) > 60 || Math.Abs(y) > 30 || Math.Abs(z) > 60) return false;
                ws.Add(new WarpCam
                {
                    pose = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), yaw), new Vec3(x, y, z)),
                    hold = (byte)(b[17 + 19 * i] & 3), trans = (byte)(b[18 + 19 * i] & 1), trig = (byte)Math.Min(2, (int)b[19 + 19 * i]),
                });
            }
            warps.Clear();
            warps.AddRange(ws);
            warpIdx = -1;
            return true;
        }

        // ---------------------------------------------------------------- moving the viewer (the XR origin)
        // Place the viewer's head at `target` (arena space, facing the target's forward) by moving the tracking origin
        static void PlaceHead(XrInput xr, Pose target)
        {
            Pose headTrack = xr.headTrack;
            Vec3 f = headTrack.q.Rotate(new Vec3(0, 0, -1));
            float yaw = MathF.Atan2(-f.x, -f.z);
            Vec3 tf = target.q.Rotate(new Vec3(0, 0, -1));
            Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-tf.x, -tf.z)) * Quat.AxisAngle(new Vec3(0, 1, 0), -yaw);
            xr.trackToWorld = new Pose(q, target.p - q.Rotate(headTrack.p));
        }

        void StartWarp(Pose target) { fadeTarget = target; fading = true; fadeOut = 0.15f; fadeIn = 0f; }

        void GoToCam(XrInput xr, int idx)
        {
            if (idx < 0 || idx >= warps.Count) return;
            warpIdx = idx;
            if (warps[idx].trans == 0) PlaceHead(xr, warps[idx].pose);   // cut
            else StartWarp(warps[idx].pose);                             // cross-fade into it
        }

        // the ball the director follows: the one in flight, else the one closest to being thrown
        bool LiveBall(out Vec3 o)
        {
            Game g = s.Shown;
            for (int p = 0; p < 2; ++p) { Ball b = g.pl[p].ball; if (b.state == BallState.Flying || b.state == BallState.Returning) { o = b.pos; return true; } }
            for (int p = 0; p < 2; ++p) if (g.pl[p].ball.state == BallState.Held) { o = g.pl[p].ball.pos; return true; }
            o = default;
            return false;
        }

        void AddCamHere(PlayerInput @in)
        {
            Vec3 f = @in.head.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            f = M.Length(f) > 1e-3f ? M.Normalize(f) : new Vec3(0, 0, -1);
            if (warps.Count >= 8) warps.RemoveAt(0);
            warps.Add(new WarpCam { pose = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-f.x, -f.z)), @in.head.p) });
            warpIdx = warps.Count - 1;
            Save();
            a.net.NoticeLocal("CAMERA SAVED");
        }

        // game events pick action cameras: the nearest action camera to a hit / strike / match end
        public void Events(List<GameEvent> events)
        {
            if (!autoCam || warps.Count == 0) return;
            foreach (var e in events)
            {
                if (e.type != EventType.HitPlayer && e.type != EventType.Strike && e.type != EventType.MatchEnd) continue;
                int best = -1;
                float bd = 1e9f;
                for (int i = 0; i < warps.Count; ++i)
                {
                    if (warps[i].trig != 1 || i == warpIdx) continue;
                    float d = M.Length(warps[i].pose.p - e.pos);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best >= 0) dirRequest = best;
            }
        }

        // the cross-fade's darkness now (0 none .. 1 dark), stepping it; the move happens at the darkest point
        public float Fade(XrInput xr, float dt)
        {
            if (!fading) return 0f;
            if (fadeOut > 0f)
            {
                fadeOut -= dt;
                if (fadeOut <= 0f) { PlaceHead(xr, fadeTarget); fadeIn = 0.35f; }
                return 1f - Math.Max(0f, fadeOut) / 0.15f;
            }
            fadeIn -= dt;
            if (fadeIn <= 0f) { fading = false; return 0f; }
            return fadeIn / 0.35f;
        }

        // Watching a match (native spectatorCamera + director): the sticks, A / X / Y, the auto camera
        public void Update(XrInput xr, PlayerInput @in, float dt, bool menuOpen)
        {
            if (!a.spectating || a.watchIp == 0 || menuOpen) { prevA = prevX = prevY = true; return; }
            double t = Time.realtimeSinceStartupAsDouble;
            Vector2 l = xr.stickL, r = xr.stickR;
            if (xr.buttons.leftSticks) { Vector2 sw = l; l = r; r = sw; }   // (BUTTONS: the left stick turns, the right moves)
            Vec3 f = @in.head.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            f = M.Length(f) > 1e-3f ? M.Normalize(f) : new Vec3(0, 0, -1);
            Vec3 right = M.Cross(f, new Vec3(0, 1, 0));
            float Dz(float v) => Math.Abs(v) < 0.15f ? 0f : v;
            Vec3 v3 = (f * Dz(l.y) + right * Dz(l.x)) * 3.0f + new Vec3(0, Dz(r.y) * 1.6f, 0);
            if (M.Length2(v3) > 0)
            {
                xr.trackToWorld.p = xr.trackToWorld.p + v3 * dt;
                if (autoCam) a.net.NoticeLocal("AUTO CAMERA OFF");
                autoCam = false;
            }
            void Turn(float ang)
            {
                Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), ang);
                Vec3 h = @in.head.p;
                xr.trackToWorld = new Pose(q * xr.trackToWorld.q, h + q.Rotate(xr.trackToWorld.p - h));
            }
            if (a.settings.turnMode != 0)
            {   // smooth: turn while the stick is held (faster the further it is pushed)
                float x = Math.Abs(r.x) < 0.2f ? 0f : (r.x - (r.x > 0 ? 0.2f : -0.2f)) / 0.8f;
                if (x != 0f) Turn(-x * (a.settings.turnMode == 2 ? 150f : 75f) * M.kPi / 180f * dt);
            }
            else if (!turnLatch && Math.Abs(r.x) > 0.75f) { turnLatch = true; Turn(r.x > 0 ? -M.kPi / 6 : M.kPi / 6); }
            else if (Math.Abs(r.x) < 0.3f) turnLatch = false;
            bool X = xr.btnX, A = xr.btnA && !X, Y = xr.btnY;
            if (A && !prevA && !X && !open) AddCamHere(@in);
            if (X && !prevX && warps.Count > 0) { GoToCam(xr, (warpIdx + 1) % warps.Count); autoCam = false; }
            if (Y && !prevY)
            {
                autoCam = !autoCam && warps.Count > 0;
                autoNext = t + 0.1;
                a.net.NoticeLocal(autoCam ? "AUTO CAMERA ON" : "AUTO CAMERA OFF");
            }
            prevA = A; prevX = X; prevY = Y;
            // the auto camera: timer and follow cameras take turns; an action camera takes over; a follow camera glides
            if (!autoCam || warps.Count == 0 || fading) return;
            if (dirRequest >= 0)
            {
                int rq = dirRequest;
                dirRequest = -1;
                GoToCam(xr, rq);
                autoNext = t + HoldSecs[warps[rq].hold & 3];
                return;
            }
            if (t > autoNext)
            {
                int n = warps.Count;
                for (int k = 1; k <= n; ++k)
                {
                    int i = (warpIdx + k + n) % n;
                    if (warps[i].trig != 1 || k == n) { GoToCam(xr, i); autoNext = t + HoldSecs[warps[i].hold & 3]; break; }
                }
                return;
            }
            if (warpIdx >= 0 && warpIdx < warps.Count && warps[warpIdx].trig == 2 && LiveBall(out Vec3 b))
            {
                float target = M.Clampf(b.x, -8f, 8f);
                xr.trackToWorld.p.x += (target - @in.head.p.x) * (1f - MathF.Exp(-dt * 1.8f));
            }
        }

        // ---------------------------------------------------------------- DIRECTOR TOOLS
        const int DB_AUTO = 1, DB_ADD = 2, DB_DONE = 3, DB_TEAM = 4, DB_SHARE = 5, DB_GO = 6, DB_HOLD = 7, DB_TRANS = 8, DB_TRIG = 9, DB_DEL = 10;
        struct Btn { public float x, y, w, h; public int act, idx; public string label; public bool on; }

        List<Btn> Buttons()
        {
            var v = new List<Btn>
            {
                new Btn { x = -0.36f, y = 0.33f, w = 0.32f, h = 0.07f, act = DB_AUTO, label = autoCam ? "AUTO CAMERA: ON" : "AUTO CAMERA: OFF", on = autoCam },
                new Btn { x = 0.0f, y = 0.33f, w = 0.34f, h = 0.07f, act = DB_ADD, label = "ADD CAMERA HERE" },
                new Btn { x = 0.38f, y = 0.33f, w = 0.24f, h = 0.07f, act = DB_DONE, label = "DONE" },
                new Btn { x = -0.18f, y = 0.245f, w = 0.68f, h = 0.06f, act = DB_TEAM, label = "TEAM: " + (a.settings.team.Length == 0 ? "NONE (TAP TO SET)" : a.settings.team) },
                new Btn { x = 0.38f, y = 0.245f, w = 0.24f, h = 0.06f, act = DB_SHARE, label = "SHARE" },
            };
            string[] hold = { "HOLD 2S", "HOLD 4S", "HOLD 8S", "HOLD 15S" }, trig = { "TIMER", "ACTION", "FOLLOW" };
            for (int i = 0; i < warps.Count; ++i)
            {
                float y = 0.15f - 0.068f * i;
                v.Add(new Btn { x = -0.44f, y = y, w = 0.20f, h = 0.056f, act = DB_GO, idx = i, label = $"CAM {i + 1}  GO", on = i == warpIdx });
                v.Add(new Btn { x = -0.235f, y = y, w = 0.18f, h = 0.056f, act = DB_HOLD, idx = i, label = hold[warps[i].hold & 3] });
                v.Add(new Btn { x = -0.04f, y = y, w = 0.19f, h = 0.056f, act = DB_TRANS, idx = i, label = warps[i].trans != 0 ? "CROSS-FADE" : "CUT" });
                v.Add(new Btn { x = 0.16f, y = y, w = 0.19f, h = 0.056f, act = DB_TRIG, idx = i, label = trig[Math.Min(2, (int)warps[i].trig)] });
                v.Add(new Btn { x = 0.40f, y = y, w = 0.20f, h = 0.056f, act = DB_DEL, idx = i, label = "DELETE" });
            }
            return v;
        }

        public void Open(PlayerInput @in)
        {
            Vec3 f = @in.head.q.Rotate(new Vec3(0, 0, -1));
            f.y = 0;
            f = M.Length(f) > 1e-3f ? M.Normalize(f) : new Vec3(0, 0, -1);
            Vec3 right = M.Cross(f, new Vec3(0, 1, 0));
            Vec3 pp = @in.head.p + f * 0.95f - right * 0.35f + new Vec3(0, -0.22f, 0);
            Vec3 to = @in.head.p - pp;
            to.y = 0;
            to = M.Normalize(to);
            pose = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(to.x, to.z)), pp);
            open = true;
            prevTrig = true;
        }

        void Activate(Btn b, XrInput xr, Menu menu)
        {
            double t = Time.realtimeSinceStartupAsDouble;
            switch (b.act)
            {
                case DB_AUTO: autoCam = !autoCam && warps.Count > 0; autoNext = t + 0.1; break;
                case DB_ADD: AddCamHere(a.localIn); break;
                case DB_DONE: open = false; infoKind = -1; break;
                case DB_TEAM: menu.Open(a.localIn.head, Lobby.Spot); menu.nameEdit = true; menu.editTarget = 2; menu.nameBuf = a.settings.team; break;
                case DB_SHARE:
                    if (a.settings.team.Length == 0) { menu.Open(a.localIn.head, Lobby.Spot); menu.nameEdit = true; menu.editTarget = 2; menu.nameBuf = ""; }
                    else { a.net.ShareCameras(a.settings.team, Pack()); a.net.NoticeLocal("CAMERAS SHARED WITH YOUR TEAM"); }
                    break;
                case DB_GO: if (b.idx < warps.Count) { autoCam = false; dirRequest = -1; StartWarp(warps[b.idx].pose); warpIdx = b.idx; } break;
                case DB_HOLD: warps[b.idx].hold = (byte)((warps[b.idx].hold + 1) & 3); infoKind = 0; infoStart = t; Save(); break;
                case DB_TRANS: warps[b.idx].trans ^= 1; infoKind = warps[b.idx].trans != 0 ? 2 : 1; infoStart = t; Save(); break;
                case DB_TRIG: warps[b.idx].trig = (byte)((warps[b.idx].trig + 1) % 3); infoKind = 3 + warps[b.idx].trig; infoStart = t; Save(); break;
                case DB_DEL:
                    warps.RemoveAt(b.idx);
                    if (warpIdx >= warps.Count) warpIdx = warps.Count - 1;
                    Save();
                    break;
            }
        }

        Pose InfoPose() => pose * new Pose(Quat.Identity, new Vec3(DirW * 0.5f + 0.42f, 0.02f, 0.06f));

        // pointing at the panel (and the info panel's close button); returns true on a click
        public bool UpdatePanel(PlayerInput @in, Pose aim, bool aimValid, XrInput xr, Menu menu)
        {
            hover = -1;
            hitValid = false;
            if (!open || menu.open) return false;
            bool clicked = false;
            bool trig = @in.hand[a.settings.pointerHand].trigger > 0.7f;
            if (aimValid)
            {
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                float HitPlane(Pose P, out Vec3 lp)
                {
                    lp = default;
                    Vec3 nz = P.q.Rotate(new Vec3(0, 0, 1));
                    float den = M.Dot(d, nz);
                    if (den > -1e-3f) return -1f;
                    float tt = M.Dot(P.p - o, nz) / den;
                    if (tt <= 0 || tt > 4) return -1f;
                    lp = P.q.Conj().Rotate(o + d * tt - P.p);
                    return tt;
                }
                float t = HitPlane(pose, out Vec3 lp1);
                if (t > 0 && Math.Abs(lp1.x) < DirW * 0.5f && Math.Abs(lp1.y) < DirH * 0.5f)
                {
                    hit = o + d * t; hitValid = true;
                    var bs = Buttons();
                    for (int i = 0; i < bs.Count; ++i)
                        if (Math.Abs(lp1.x - bs[i].x) < bs[i].w * 0.5f && Math.Abs(lp1.y - bs[i].y) < bs[i].h * 0.5f) hover = i;
                    if (trig && !prevTrig && hover >= 0) { Activate(bs[hover], xr, menu); clicked = true; }
                }
                else if (infoKind >= 0)
                {   // the info panel's close button
                    float t2 = HitPlane(InfoPose(), out Vec3 lp2);
                    if (t2 > 0 && Math.Abs(lp2.x - 0.28f) < 0.05f && Math.Abs(lp2.y - 0.27f) < 0.03f)
                    {
                        hit = o + d * t2; hitValid = true; hover = 1000;
                        if (trig && !prevTrig) { infoKind = -1; clicked = true; }
                    }
                }
            }
            prevTrig = trig;
            for (int h = 0; h < 2; ++h) @in.hand[h].trigger = 0;
            return clicked;
        }

        static readonly string[] InfoTitle = { "HOLD TIME", "CUT", "CROSS-FADE", "TIMER CAMERA", "ACTION CAMERA", "FOLLOW CAMERA" };
        static readonly string[,] InfoText =
        {
            { "HOW LONG THE AUTO CAMERA STAYS ON THIS", "CAMERA BEFORE MOVING TO THE NEXT ONE" },
            { "SWITCHES INSTANTLY TO THIS CAMERA", "SHARP, GOOD FOR FAST ACTION" },
            { "THE OLD VIEW FADES INTO THIS CAMERA", "SMOOTH, EASY TO WATCH" },
            { "TAKES ITS TURN WITH THE OTHER TIMER", "AND FOLLOW CAMERAS, IN ORDER" },
            { "JUMPS HERE WHEN A HIT, STRIKE OR MATCH", "END HAPPENS NEAR THIS CAMERA" },
            { "GLIDES ALONG THE COURT WITH THE BALL", "YOU STILL CHOOSE WHERE TO LOOK" },
        };

        // ---- the info preview: a 6 s loop rendered into a texture. 0-3 s: a wide view showing the camera's path with a
        // demo ball; 3-6 s: what that camera sees. Scripted, so it works with or without a live match.
        public static Vec3 DemoBall(float u) => new Vec3(-6f + 12f * u, 1.3f + 0.55f * MathF.Sin(M.kPi * u), 0.8f * MathF.Sin(2f * M.kPi * u) * (1f - u));

        public static Pose DemoCam(int kind, float u, out int shot)
        {
            Pose Look(Vec3 from, Vec3 at)
            {
                Vec3 f = M.Normalize(at - from);
                float yaw = MathF.Atan2(-f.x, -f.z), pitch = MathF.Asin(M.Clampf(f.y, -1, 1));
                return new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), yaw) * Quat.AxisAngle(new Vec3(1, 0, 0), pitch), from);
            }
            shot = 0;
            if (kind == 5)
            {   // follow: dolly along the side with the ball, looking at it
                Vec3 b = DemoBall(u), lag = DemoBall(Math.Max(0f, u - 0.08f));
                return Look(new Vec3(lag.x - 1.0f, 2.0f, 3.6f), b);
            }
            if (kind == 4)
            {   // action: wide until the hit near the end, then the action camera by the hit
                if (u < 0.9f) return Look(new Vec3(0, 3.4f, 5.2f), new Vec3(0, 1.2f, 0));
                shot = 1;
                return Look(new Vec3(5.4f, 1.7f, 2.3f), DemoBall(0.95f));
            }
            // timer / hold / cut / cross-fade: camera A, then camera B halfway
            if (u < 0.5f) return Look(new Vec3(-3f, 2.2f, 3.6f), new Vec3(0, 1.3f, 0));
            shot = 1;
            return Look(new Vec3(4f, 2.0f, -3.4f), new Vec3(0, 1.3f, 0));
        }

        Camera previewCam;
        RenderTexture previewTex;
        FxRenderer previewFx;

        // the preview's camera pose this frame (and what to draw for it), or false: no preview
        public bool PreviewView(out Pose cam, out float fovDeg, out float u, out bool wide, out Pose rig)
        {
            cam = rig = Pose.Identity; fovDeg = 60; u = 0; wide = false;
            if (!open || infoKind < 0) return false;
            float t = (float)((Time.realtimeSinceStartupAsDouble - infoStart) % 6.0);
            wide = t < 3f;
            u = (t % 3f) / 3f;
            rig = DemoCam(infoKind, u, out _);
            if (wide) { cam = new Pose(Quat.AxisAngle(new Vec3(1, 0, 0), -0.62f), new Vec3(0, 10.5f, 13.0f)); fovDeg = 60; }
            else { cam = rig; fovDeg = 70; }
            return true;
        }

        // render the preview (call after the frame's drawing is queued): the world from the preview camera, with the demo
        // ball, its trail and (wide view) the camera rig and its path
        public void RenderPreview(Shader fxShader, Shader textShader, Texture2D font)
        {
            if (!PreviewView(out Pose cam, out float fov, out float u, out bool wide, out Pose rig)) return;
            if (previewCam == null)
            {
                previewTex = new RenderTexture(640, 360, 24) { name = "director preview" };
                var go = new GameObject("Director preview camera") { hideFlags = HideFlags.HideAndDontSave };
                previewCam = go.AddComponent<Camera>();
                previewCam.enabled = false;
                previewCam.stereoTargetEye = StereoTargetEyeMask.None;
                previewCam.targetTexture = previewTex;
                previewCam.clearFlags = CameraClearFlags.SolidColor;
                previewCam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);
                previewCam.nearClipPlane = 0.05f;
                previewCam.farClipPlane = 2000f;
                previewFx = new FxRenderer(fxShader, textShader, font);
            }
            previewCam.fieldOfView = fov;
            previewCam.aspect = 16f / 9f;
            previewCam.transform.SetPositionAndRotation(NativeSpace.ToUnity(cam.p), NativeSpace.ToUnity(cam.q));
            var ball = new Vector4(0.3f, 0.85f, 1.0f, 1);
            Vec3 b = DemoBall(u);
            previewFx.Sprite(b, 0.35f, ball, cam.p, 0);
            previewFx.Sprite(b, 0.12f, new Vector4(1, 1, 1, 1), cam.p, 0);
            var trail = new Vec3[16];
            for (int i = 0; i < 16; ++i) trail[i] = DemoBall(Math.Max(0f, u - 0.012f * (15 - i)));
            previewFx.Ribbon(trail, 16, 0.06f, ball, cam.p);
            if (infoKind == 4 && u > 0.9f) previewFx.Sprite(DemoBall(0.95f), 1.2f * (u - 0.88f) * 8, new Vector4(1.0f, 0.6f, 0.3f, 1), cam.p, 1);
            if (wide)
            {
                var camCol = new Vector4(1.0f, 0.85f, 0.2f, 1);
                previewFx.Box(rig, new Vec3(0.18f, 0.12f, 0.22f), camCol);
                previewFx.Box(rig * new Pose(Quat.Identity, new Vec3(0, 0, -0.3f)), new Vec3(0.08f, 0.08f, 0.12f), camCol);
                var path = new Vec3[24];
                for (int i = 0; i < 24; ++i) path[i] = DemoCam(infoKind, i / 23f, out _).p;
                previewFx.Ribbon(path, 24, 0.05f, camCol, cam.p);
            }
            previewFx.Flush(previewCam);
            previewCam.Render();
        }

        public void Draw(FxRenderer fx, PanelBatch pb, Pose aim, bool aimValid)
        {
            if (!open) return;
            void Quad(Pose P, float x, float y, float w, float h, float z, Vector4 c) => fx.Panel(new Pose(P.q, P.Transform(new Vec3(x, y, z))), w, h, c);
            void Text(Pose P, string t, float x, float y, float h, Vector4 c) => fx.Text(t, new Pose(P.q, P.Transform(new Vec3(x, y, 0.006f))), h, c);
            Vector4 bg = new Vector4(0.004f, 0.007f, 0.016f, 0.92f), frame = new Vector4(0.55f, 0.42f, 0.08f, 0.9f);
            Vector4 btn = new Vector4(0.03f, 0.05f, 0.08f, 0.95f), hov = new Vector4(0.75f, 0.55f, 0.10f, 0.98f), on = new Vector4(0.10f, 0.35f, 0.55f, 0.98f);
            Vector4 white = new Vector4(1, 1, 1, 1), grey = new Vector4(0.5f, 0.55f, 0.62f, 1), gold = new Vector4(1.0f, 0.82f, 0.3f, 1);
            Pose P0 = pose;
            Quad(P0, 0, 0, DirW + 0.03f, DirH + 0.03f, -0.002f, frame);
            Quad(P0, 0, 0, DirW, DirH, 0, bg);
            Text(P0, "DIRECTOR TOOLS", 0, 0.43f, 0.04f, gold);
            var bs = Buttons();
            for (int i = 0; i < bs.Count; ++i)
            {
                Quad(P0, bs[i].x, bs[i].y, bs[i].w, bs[i].h, 0.003f, i == hover ? hov : bs[i].on ? on : btn);
                Text(P0, bs[i].label, bs[i].x, bs[i].y, bs[i].h > 0.06f ? 0.024f : 0.019f, white);
            }
            if (warps.Count == 0)
            {
                Text(P0, "NO CAMERAS YET: FLY TO A SPOT AND PRESS", 0, 0.12f, 0.022f, grey);
                Text(P0, "ADD CAMERA HERE (OR A ON THE CONTROLLER)", 0, 0.08f, 0.022f, grey);
            }
            Text(P0, "CLICK AN OPTION TO CHANGE IT AND SEE WHAT IT DOES", 0, -0.445f, 0.02f, grey);
            if (infoKind >= 0)
            {   // info panel with the preview
                Pose I = InfoPose();
                Quad(I, 0, 0, 0.74f, 0.66f, -0.002f, frame);
                Quad(I, 0, 0, 0.71f, 0.63f, 0, bg);
                Text(I, InfoTitle[infoKind], -0.03f, 0.27f, 0.034f, gold);
                Quad(I, 0.28f, 0.27f, 0.09f, 0.05f, 0.003f, hover == 1000 ? hov : btn);
                Text(I, "X", 0.28f, 0.27f, 0.026f, white);
                Text(I, InfoText[infoKind, 0], 0, 0.21f, 0.02f, white);
                Text(I, InfoText[infoKind, 1], 0, 0.175f, 0.02f, white);
                if (previewTex != null)
                    pb.Quad(previewTex, new Pose(I.q, I.Transform(new Vec3(0, -0.05f, 0.004f))), 0.64f, 0.36f, new Vector4(1, 1, 1, 0.999f));
                float t = (float)((Time.realtimeSinceStartupAsDouble - infoStart) % 6.0);
                Text(I, t < 3f ? "1  WIDE VIEW: WHAT THE CAMERA DOES" : "2  WHAT THE CAMERA SEES", 0, -0.27f, 0.02f, grey);
            }
            if (aimValid)
            {   // laser + cursor
                Vec3 o = aim.p, d = aim.q.Rotate(new Vec3(0, 0, -1));
                float len = hitValid ? M.Length(hit - o) : 1.5f;
                fx.Box(new Pose(aim.q, o + d * (len * 0.5f)), new Vec3(0.003f, 0.003f, len * 0.5f), new Vector4(1.0f, 0.75f, 0.2f, 1));
                if (hitValid) fx.Glow(hit, 0.008f, gold);
            }
        }
    }
}
