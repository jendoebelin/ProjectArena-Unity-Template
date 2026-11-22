// The PC spectator for streamers (task D2; native main.cpp desktopMain / desktopFrame / desktopControls /
// desktopPreset / eventCamera / desktopHud / drawSelfie, the kDc tables): the PC build without a headset (or started with
// --spectate) is a window that watches the live matches for a stream. Always spectating: TAB the next match, H the hub
// (a match is picked by itself after 2 s unless you went to the hub). The cameras: 1-5 the views (SIDE, HIGH SIDE, BLUE
// END, ORANGE END, TOP), 6 / 7 each player's own eyes (smoothed: a headset's every twitch is not for an audience; their
// body hidden), F (or 0) the free camera (right mouse button looks, W A S D / Q E move, Shift faster), MY CAMERAS
// (Ctrl + 1-8 saves the view as camera n, Shift + 1-8 shows it; the headset director's warps.cfg, so the cameras you
// placed in the headset are here too), A the auto director, the mouse wheel zooms. V the view layers (FULL, NO WALLS,
// MINIMAL, KEY: nothing behind the players, see-through for OBS), R the win screen (SPLIT, WINNER, YOUR CAMERA), M the
// VS screen, C SELFIE (your webcam in a corner), F2 the help bar, F11 full screen, F1 the controls panel. A game
// controller: A next match, B hub, X auto, Y free (the sticks, LB / RB down / up), the d-pad the cameras, the triggers zoom.
// Broadcast moments: at the fist bump a close side view of the two fists, and (WIN SCREEN > WINNER) the winner up close.
// Differences from native: the controls are a panel inside the window (F1; native: a separate small window, never in
// the captured picture), Discord status and the PC updater are not in the template, and KEY's see-through window
// needs OBS to capture the window's alpha.
using System;
using System.Collections.Generic;
using ProjectArena.Online;
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using EventType = ProjectArena.Sim.EventType;
using Pose = ProjectArena.Sim.Pose;
using PlayerInput = ProjectArena.Sim.PlayerInput;

namespace ProjectArena
{
    public sealed class DesktopSpectator
    {
        public static readonly string[] ViewName = { "FREE CAMERA", "SIDE", "HIGH SIDE", "BLUE END", "ORANGE END", "TOP", "BLUE'S EYES", "ORANGE'S EYES",
                                                     "CORNER", "COURTSIDE",   // (8, 9: TASKS.md E42, native 7240d49)
                                                     "TRACKING", "BALL CAM", "UNDER FIRE" };   // (10-12 THE FOLLOW VIEWS: TASKS.md E57, native 5bfc546)
        public const int Follow0 = 10;   // the first follow view
        public static bool FollowView(int v) => v >= Follow0 && v < ViewName.Length;
        static string PresetName(int v) => v >= 0 && v < ViewName.Length ? ViewName[v] : "?";
        public const int Mine = 20;   // (8 until the CORNER and COURTSIDE presets: the views are numbered only here)
        public static readonly string[] LayerName = { "FULL", "NO WALLS", "MINIMAL", "KEY" };
        public static readonly string[] WinName = { "SPLIT", "WINNER", "YOUR CAMERA" };
        static readonly float[] HoldSecs = { 2, 4, 8, 15 };
        static readonly string[] HoldName = { "2 s", "4 s", "8 s", "15 s" }, TrigName = { "in turn", "action", "follow" };
        static readonly string[] CornerName = { "top left", "top right", "bottom left", "bottom right" }, SizeName = { "small", "medium", "large" };

        // the commands (keys, a game controller and the controls panel's buttons give the same ones)
        // ARENA GLASS (TASKS.md E62, native f7b9cc9; the owner, 2026-11-14: players hard to see through the arena's walls
        // from the stream's cameras): the court's glass, its glowing lines, the hexagon and end-zone glow and the strike-zone
        // rings this strong in this PC's picture only (the headsets draw the arena as always; nothing is sent)
        public static float NextGlass(float g) => g > 0.9f ? 0.35f : g > 0.3f ? 0.2f : 1f;
        public static string GlassName(float g) => g > 0.9f ? "normal" : g > 0.3f ? "faint (35%)" : "fainter (20%)";
        // the court's sections it fades: every kind but native's lit and lightmapped (additive, unlit, translucent); the
        // colour times it, a translucent one's opacity too (native render.cpp glowMul)
        public struct GlowSection { public Renderer r; public int index; public bool translucent; }
        public static List<GlowSection> GlowOf(IEnumerable<Renderer> rs)
        {
            var v = new List<GlowSection>();
            foreach (Renderer r in rs)
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; ++i)
                {
                    Material m = ms[i];
                    if (m == null || m.IsKeywordEnabled("_PA_LIGHTMAP")) continue;
                    bool litKind = m.IsKeywordEnabled("_PA_LIT");
                    bool translucent = litKind && !(m.HasProperty("_LitOnly") && m.GetFloat("_LitOnly") > 0.5f);
                    if (litKind && !translucent) continue;   // (native SH_LIT)
                    v.Add(new GlowSection { r = r, index = i, translucent = translucent });
                }
            }
            return v;
        }
        static readonly int TintMulId = Shader.PropertyToID("_TintMul");
        public static void SetGlow(List<GlowSection> secs, MaterialPropertyBlock mpb, float mul)
        {
            foreach (GlowSection g in secs)
            {
                g.r.GetPropertyBlock(mpb, g.index);
                mpb.SetVector(TintMulId, new Vector4(mul, mul, mul, g.translucent ? mul : 1f));
                g.r.SetPropertyBlock(mpb, g.index);
            }
        }

        public enum Cmd
        {
            None, Next, Hub, Free, Auto, Help, Full, Panel, Layer, LayerBack, WinStyle, Matchup, Selfie, SelfieCorner, SelfieSize,
            SelfieShape, SelfieCam, MySave, MyHold, MyTrans, MyTrig, MyClear,
            AutoHold, AutoUp, AutoDown,   // (AUTO ROTATION, native 0.3.20: the chosen row's hold, moved up or down)
            Glass,   // (ARENA GLASS, TASKS.md E62, native f7b9cc9)
            View = 100,   // + 1..12 (10-12 THE FOLLOW VIEWS)
            MyGo = 140,   // + camera 0..7
            MySet = 150,  // + camera 0..7
            AutoTick = 200,   // + row: IN AUTO ticked or not (AUTO ROTATION)
            AutoPick = 240,   // + row: the row chosen
        }

        readonly AppState a;
        readonly Director dir;
        public int view = 1, my = -1, layer, winStyle, eyesOf = -1;
        public bool auto, help, hubPinned, matchupForce, matchup = true, full, panelOn = true;
        public float fov = 66f;
        public Vec3 pos = new Vec3(0, 2.4f, 7.5f);
        public float yaw, pitch = -0.15f;
        public double hintUntil;
        double autoAt, watchAt;
        Pose eyeS = Pose.Identity, last = Pose.Identity, from = Pose.Identity;
        bool eyeSet, lastOk;
        float glide, followX;
        byte newHold = 1, newTrans = 1;
        int eventKind, eventSkip;
        double eventT0;
        float eventMix;
        Vec3 winT;
        Cmd pending;   // (the controls panel's button pressed this frame)
        // AUTO ROTATION (TASKS.md E6, native 0.3.20; Runtime/AutoRotation.cs): the presets and my cameras AUTO plays,
        // ticked, in order (autocams.cfg once the streamer has changed it), the row it played last, the panel's chosen row
        public readonly List<AutoRotation.Item> autoList = new List<AutoRotation.Item>();
        public bool autoCustom;
        public int autoSel = -1;
        bool autoLoaded;
        int autoRow = -1;
        string AutoFile => System.IO.Path.Combine(a.settings.Folder, "autocams.cfg");

        public DesktopSpectator(AppState app, Director director) { a = app; dir = director; }

        // without a headset on PC, or asked for with --spectate (native desktopMain)
        public static bool Wanted()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg == "--spectate") return true;
            var displays = new List<UnityEngine.XR.XRDisplaySubsystem>();   // (a headset: OpenXR's display running)
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) return false;
            return true;
#else
            return false;
#endif
        }

        public void Press(Cmd c) => pending = c;

        public static Pose LookFrom(Vec3 p, Vec3 target)
        {
            Vec3 f = target - p;
            f = M.Length(f) > 1e-5f ? M.Normalize(f) : new Vec3(0, 0, -1);
            float y = MathF.Atan2(-f.x, -f.z), pt = MathF.Asin(M.Clampf(f.y, -1f, 1f));
            return new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), y) * Quat.AxisAngle(new Vec3(1, 0, 0), pt), p);
        }
        static Pose Blend(Pose from, Pose to, float k) => new Pose(M.Nlerp(from.q, to.q, k), from.p + (to.p - from.p) * k);

        // the views (native desktopPreset): the hub, FOUR CORNERS' room from outside its glass, the classic court
        public Pose Preset(int v)
        {
            if (a.watchIp == 0) return LookFrom(new Vec3(-1.3f, 1.75f, 0f), new Vec3(1.4f, 0.85f, 0f));
            Game g = a.shown ?? a.game;
            if (ExpArenaView.CornersRoom(g))
            {
                if (v == 2) return LookFrom(new Vec3(0, 12.5f, 3f), new Vec3(0, -0.5f, 0));
                if (v == 8) return LookFrom(new Vec3(-11.5f, 6.5f, 4.0f), new Vec3(1.0f, -0.5f, -1.0f));   // CORNER: high behind blue's corner
                if (v == 9) return LookFrom(new Vec3(-7.8f, 1.3f, 7.8f), new Vec3(0, 0.2f, 0));           // COURTSIDE: outside the wall between two
                if (v != 3 && v != 4 && v != 5) return LookFrom(new Vec3(-8.2f, 5.2f, 8.2f), new Vec3(0, 0.2f, 0));
            }
            // FIREBALL BLITZ (native ae13e47, 044a00a): the places are on the axes (four: one stands where the SQUARE's side
            // camera is; the owner, 11-19: "move the camera especially if the player is blocking the view"), and the screen
            // hangs over the middle, 7.2 to 8.8 m up. Its side cameras between two places, on the diagonals no place comes
            // near with four, three or two left (the waiting row and the gallery seat are on the other two); TOP under the screen
            if (g.Octagon)
            {
                if (v == 1) return LookFrom(new Vec3(8.6f, 3.4f, -8.6f), new Vec3(0, 0.5f, 0));   // SIDE: across from COURTSIDE
                if (v == 5) return LookFrom(new Vec3(0, 6.8f, 8.2f), new Vec3(0, 0, 0.8f));        // TOP (the near place still in view)
                if (v == 9) return LookFrom(new Vec3(-8.2f, 1.6f, 8.2f), new Vec3(0, 0.8f, 0));    // COURTSIDE
            }
            switch (v)
            {
                case 2: return LookFrom(new Vec3(0, 5.2f, 6.6f), new Vec3(0, 0.6f, 0));
                // the ends: inside the glass, above and behind the near player, looking down the court (TASKS.md E42, native
                // 7240d49; astrobleem, 2026-11-19: "the shot from the rear of the tank should be more inside of the tank";
                // until then above the end's glass, 12.5 m out)
                case 3: return LookFrom(new Vec3(-9.25f, 3.3f, 0), new Vec3(-0.5f, 0.3f, 0));
                case 4: return LookFrom(new Vec3(9.25f, 3.3f, 0), new Vec3(0.5f, 0.3f, 0));
                case 5: return LookFrom(new Vec3(0, 11f, 5.5f), new Vec3(0, 0, 0));
                // (astrobleem, 2026-11-19: "there are two new preset camera angles that we don't have")
                case 8: return LookFrom(new Vec3(-11.0f, 6.0f, 5.0f), new Vec3(1.0f, 0.0f, -1.2f));   // CORNER: high over the blue end's near corner
                case 9: return LookFrom(new Vec3(0, 1.25f, 7.2f), new Vec3(0, 1.0f, 0));              // COURTSIDE: low at the side, straight across
                default: return LookFrom(new Vec3(0, 2.7f, 7.3f), new Vec3(0, 1.0f, 0));
            }
        }

        public string ViewLabel()
        {
            if (view == Mine && my >= 0 && my < dir.warps.Count)
                return "MY CAMERA " + (my + 1) + (dir.warps[my].trig == 1 ? "  ACTION" : dir.warps[my].trig == 2 ? "  FOLLOW" : "");
            return PresetName(view);
        }

        // the VS screen: at the fist bump and the countdown (or any time with M), two players only
        public bool VsScreen(Game g) => a.watchIp != 0 && g.np == 2 && (matchupForce || (matchup && (g.match == MatchState.Meet || g.match == MatchState.Countdown)));
        // WIN SCREEN > SPLIT: the winner and the loser side by side, each alone
        public bool WinSplit(Game g) => a.watchIp != 0 && winStyle == 0 && g.np == 2 && g.match == MatchState.MatchSummary && g.matchWinner >= 0 && g.matchWinner < 2;

        static bool LiveBall(Game g, out Vec3 at)
        {
            for (int p = 0; p < g.np; ++p) { Ball b = g.pl[p].ball; if (b.state == BallState.Flying || b.state == BallState.Returning) { at = b.pos; return true; } }
            for (int p = 0; p < g.np; ++p) if (g.pl[p].ball.state == BallState.Held) { at = g.pl[p].ball.pos; return true; }
            at = default;
            return false;
        }

        // ---- the input (keys: the window focused; native dcPressed / dcDown)
        static bool Pressed(Key k) { var kb = Keyboard.current; return kb != null && kb[k].wasPressedThisFrame; }
        static bool Down(Key k) { var kb = Keyboard.current; return kb != null && kb[k].isPressed; }

        Cmd ReadCommand()
        {
            Cmd cmd = pending;
            pending = Cmd.None;
            if (Pressed(Key.Tab)) cmd = Cmd.Next;
            if (Pressed(Key.H)) cmd = Cmd.Hub;
            bool ctrl = Down(Key.LeftCtrl) || Down(Key.RightCtrl), shift = Down(Key.LeftShift) || Down(Key.RightShift);
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int v = 1; v <= 9; ++v)   // 1 to 9 the views; Ctrl + 1 to 8 saves the view as MY CAMERA n, Shift + 1 to 8 shows it
                if (Pressed(digits[v - 1])) cmd = ctrl ? (v <= 8 ? Cmd.MySet + v - 1 : Cmd.None) : shift ? (v <= 8 ? Cmd.MyGo + v - 1 : Cmd.None) : Cmd.View + v;
            if (Pressed(Key.T)) cmd = Cmd.View + 10;   // THE FOLLOW VIEWS: T tracking, B ball cam, U under fire
            if (Pressed(Key.B)) cmd = Cmd.View + 11;
            if (Pressed(Key.U)) cmd = Cmd.View + 12;
            if (Pressed(Key.F) || Pressed(Key.Digit0)) cmd = Cmd.Free;
            if (view != 0 && Pressed(Key.A)) cmd = Cmd.Auto;   // (A moves left in the free camera)
            if (Pressed(Key.F1)) cmd = Cmd.Panel;
            if (Pressed(Key.F2)) cmd = Cmd.Help;
            if (Pressed(Key.F11) || (full && Pressed(Key.Escape))) cmd = Cmd.Full;
            if (Pressed(Key.V)) cmd = Cmd.Layer;
            if (Pressed(Key.R)) cmd = Cmd.WinStyle;
            if (Pressed(Key.M)) cmd = Cmd.Matchup;
            if (Pressed(Key.C)) cmd = Cmd.Selfie;
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonSouth.wasPressedThisFrame) cmd = Cmd.Next;
                if (pad.buttonEast.wasPressedThisFrame) cmd = Cmd.Hub;
                if (pad.buttonWest.wasPressedThisFrame) cmd = Cmd.Auto;
                if (pad.buttonNorth.wasPressedThisFrame) cmd = Cmd.Free;
                bool next = pad.dpad.right.wasPressedThisFrame || (view != 0 && pad.rightShoulder.wasPressedThisFrame);
                bool prev = pad.dpad.left.wasPressedThisFrame || (view != 0 && pad.leftShoulder.wasPressedThisFrame);
                if (next || prev)
                {   // round the views (twelve with the follow views), then your cameras
                    int nv = ViewName.Length - 1, n = nv + dir.warps.Count;
                    int at = view == Mine && my >= 0 ? nv + my : (view == 0 ? 1 : view) - 1;
                    int k = (at + (next ? 1 : n - 1)) % n;
                    cmd = k < nv ? Cmd.View + k + 1 : Cmd.MyGo + k - nv;
                }
                if (pad.selectButton.wasPressedThisFrame) cmd = Cmd.Help;
                if (pad.dpad.up.wasPressedThisFrame) cmd = Cmd.Layer;
                if (pad.dpad.down.wasPressedThisFrame) cmd = Cmd.LayerBack;
                if (pad.startButton.wasPressedThisFrame) cmd = Cmd.Full;
                if (pad.rightStickButton.wasPressedThisFrame) cmd = Cmd.WinStyle;
            }
            return cmd;
        }

        // this frame's eye pose (native desktopControls): the commands, then the camera
        public Pose Controls(List<SpecMatch> ms, Game g, PlayerInput[] viewIn, bool[] viewInValid, double t, float dt)
        {
            Cmd cmd = ReadCommand();
            var pad = Gamepad.current;
            if (pad != null) fov -= (pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue()) * 45f * dt;
            Apply(cmd, ms, g, t);
            // nobody chosen: the first live match after a moment (a stream should show play, not the hub)
            if (a.watchIp == 0 && !hubPinned && ms.Count > 0)
            {
                if (watchAt == 0) watchAt = t + 2.0;
                if (t > watchAt) { a.watchIp = ms[0].ip; watchAt = 0; Debug.Log("[Desktop] watching " + ms[0].name); }
            }
            AutoList(cmd);
            AutoDirector(g, t);
            var mouse = Mouse.current;
            float wheel = mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f;
            fov = M.Clampf(fov - wheel * 4f, 25f, 100f);
            if (view == 0)
            {   // free camera: the right mouse button looks, W A S D / Q E move, Shift faster
                if (mouse != null && mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw -= d.x * 0.004f;
                    pitch = M.Clampf(pitch + d.y * 0.004f, -1.5f, 1.5f);   // (Unity's mouse y is up)
                }
                float sp = (Down(Key.LeftShift) || (pad != null && pad.leftStickButton.isPressed) ? 9f : 3f) * dt;
                if (pad != null)
                {
                    Vector2 ls = pad.leftStick.ReadValue(), rs = pad.rightStick.ReadValue();
                    yaw -= rs.x * 2.2f * dt;
                    pitch = M.Clampf(pitch + rs.y * 1.6f * dt, -1.5f, 1.5f);
                    Quat qp = Quat.AxisAngle(new Vec3(0, 1, 0), yaw) * Quat.AxisAngle(new Vec3(1, 0, 0), pitch);
                    pos += qp.Rotate(new Vec3(0, 0, -1)) * (ls.y * sp) + qp.Rotate(new Vec3(1, 0, 0)) * (ls.x * sp);
                    if (pad.rightShoulder.isPressed) pos.y += sp;
                    if (pad.leftShoulder.isPressed) pos.y -= sp;
                }
                Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), yaw) * Quat.AxisAngle(new Vec3(1, 0, 0), pitch);
                Vec3 f = q.Rotate(new Vec3(0, 0, -1)), r = q.Rotate(new Vec3(1, 0, 0));
                if (Down(Key.W)) pos += f * sp;
                if (Down(Key.S)) pos -= f * sp;
                if (Down(Key.D)) pos += r * sp;
                if (Down(Key.A)) pos -= r * sp;
                if (Down(Key.E)) pos.y += sp;
                if (Down(Key.Q)) pos.y -= sp;
                eyesOf = -1;
                return EventCamera(new Pose(q, pos), g, viewIn, t, dt, true);
            }
            Pose b = Preset(view);
            eyesOf = -1;
            if ((view == 6 || view == 7) && a.watchIp != 0)
            {   // a player's own eyes (their body hidden; their hands show)
                int p = view - 6;
                Pose h = viewIn[p].head;
                if (viewInValid[p] && h.p.y > 0.3f)
                {
                    var want = new Pose(h.q, h.p + h.q.Rotate(new Vec3(0, 0, -0.06f)));
                    if (!eyeSet || M.Length(eyeS.p - want.p) > 2f) { eyeS = want; eyeSet = true; }
                    eyeS = new Pose(M.Nlerp(eyeS.q, want.q, Mathf.Min(1f, dt * 6f)), eyeS.p + (want.p - eyeS.p) * Mathf.Min(1f, dt * 10f));
                    b = eyeS;
                    eyesOf = p;
                }
            }
            else eyeSet = false;
            if (FollowView(view) && a.watchIp != 0) b = FollowPose(g, viewIn, viewInValid, view, t, dt);   // THE FOLLOW VIEWS
            else camSet = false;
            if (view == Mine && my >= 0 && my < dir.warps.Count)
            {   // one of yours (a follow camera glides along the court with the ball, as in the headset)
                Director.WarpCam w = dir.warps[my];
                b = new Pose(w.pose.q * Quat.AxisAngle(new Vec3(1, 0, 0), w.pitch), w.pose.p);
                if (w.trig == 2)
                {
                    if (LiveBall(g, out Vec3 bp)) followX += (M.Clampf(bp.x, -8f, 8f) - followX) * (1f - MathF.Exp(-dt * 1.8f));
                    b.p.x = followX;
                }
            }
            if (glide > 0)
            {   // a fade: from where the camera was, eased (0.8 s)
                glide = Mathf.Max(0f, glide - dt / 0.8f);
                float k = 1f - glide, e = k * k * (3 - 2 * k);
                b = Blend(from, b, e);
            }
            Pose o = EventCamera(b, g, viewIn, t, dt, false);
            last = o;
            lastOk = true;
            return o;
        }

        // THE FOLLOW VIEWS (TASKS.md E57, native 5bfc546 dcFollowPose; the owner, 2026-11-21: "custom camera angles where
        // the camera follows actions"), 10-12:
        //  TRACKING   along the side at head height, sliding with the play and panning to it (a fixed place, panning, in
        //             FOUR CORNERS and FIREBALL BLITZ, where there is no one side);
        //  BALL CAM   low at the side glass, running alongside the ball, close (from COURTSIDE, turning, where there is no
        //             one side);
        //  UNDER FIRE over the shoulder of the player the ball is flying at, as it comes in (it moves to the other player only
        //             when the play has gone their way for a moment, and never sooner than 2.5 s: a cut, not a swoop).
        // The action is the ball that matters: one flying at a player (the fastest), else one on its way home, else one in a
        // hand; whom it flies at is found by its direction, so it works in every arena. Every move is eased by a critically
        // damped spring (no overshoot, and the network's small corrections don't shake the picture)
        Vec3 actPos, actVel, camP, camV, aimP, aimV, chaseDir = new Vec3(1, 0, 0);
        bool actSet, camSet;
        int fireP = -1, fireWant = -1, actBall = -1;   // (actBall: whose ball the action is, kept while it flies)
        double fireAt, fireWantAt;

        public static void Spring(ref Vec3 x, ref Vec3 v, Vec3 target, float w, float dt)   // w: how quickly it follows (1/s)
        {
            int n = Math.Max(1, Math.Min(20, (int)MathF.Ceiling(dt / 0.008f)));   // (small steps: steady at any frame rate)
            float h = dt / n, k = w * w, c = 2f * w;
            for (int i = 0; i < n; ++i) { v += ((target - x) * k - v * c) * h; x += v * h; }
        }

        bool Action(Game g, PlayerInput[] viewIn, bool[] viewInValid, out Vec3 bpos, out Vec3 bvel, out int target)
        {
            int best = -1, kind = 0;   // 3 flying at someone, 2 on its way home, 1 in a hand
            float bs = -1;
            // the balls by a number: 0-3 each player's own; FIREBALL BLITZ's fireballs 8 + 3 x owner + slot (native 3fccaf9,
            // review 2026-11-21: there a thrown ball is a fireball, and the players' own balls are only ever held, so the
            // follow views had nothing to follow)
            int nFire = g.Octagon ? g.np * Game.KMaxFire : 0;
            Ball BallOf(int key) => key < 8 ? g.pl[key].ball : g.fire[(key - 8) / Game.KMaxFire][(key - 8) % Game.KMaxFire];
            bool Valid(int key) => (key >= 0 && key < g.np) || (key >= 8 && key < 8 + nFire);
            int KindOf(int key)
            {
                BallState st = BallOf(key).state;
                return st == BallState.Flying ? 3 : st == BallState.Returning ? 2 : st == BallState.Held && key < 8 ? 1 : 0;
            }
            for (int key = 0; key < 8 + nFire; ++key)
            {
                if (!Valid(key)) continue;
                int k = KindOf(key);
                float s = M.Length(BallOf(key).vel);
                if (k > kind || (k != 0 && k == kind && s > bs)) { kind = k; bs = s; best = key; }
            }
            // the same ball while it is still in the air (two in flight: the picture doesn't jump between them)
            if (Valid(actBall) && KindOf(actBall) >= 2 && KindOf(actBall) >= kind) { best = actBall; kind = KindOf(best); }
            actBall = kind >= 2 ? best : -1;
            target = -1;
            bpos = bvel = default;
            if (best < 0) return false;
            Ball b = BallOf(best);
            bpos = b.pos;
            bvel = kind == 1 ? default : b.vel;
            if (kind == 3)
            {   // at whom: the player most nearly ahead of it (not the one who sent it, a teammate or one out)
                Vec3 d = new Vec3(bvel.x, 0, bvel.z);
                if (M.Length(d) < 0.5f) return true;
                d = M.Normalize(d);
                float bd = 0.3f;
                for (int p = 0; p < g.np; ++p)
                {
                    if (p == b.instigator || !viewInValid[p] || !g.Alive(p) || g.Friendly(p, b.instigator)) continue;
                    Vec3 to = viewIn[p].head.p - bpos;
                    to.y = 0;
                    float len = M.Length(to);
                    if (len < 0.05f) continue;
                    float dd = M.Dot(to * (1f / len), d);
                    if (dd > bd) { bd = dd; target = p; }
                }
            }
            return true;
        }

        public Pose FollowPose(Game g, PlayerInput[] viewIn, bool[] viewInValid, int v, double t, float dt)
        {
            Vec3 mid = new Vec3(0, 1.2f, 0), sum = default;
            int nh = 0;
            for (int p = 0; p < g.np; ++p) if (viewInValid[p]) { sum += viewIn[p].head.p; ++nh; }
            if (nh > 0) mid = sum * (1f / nh);
            bool ball = Action(g, viewIn, viewInValid, out Vec3 bp, out Vec3 bv, out int tgt);
            Vec3 want = ball ? bp + bv * 0.12f : mid;   // (a touch ahead of the ball)
            if (!actSet || M.Length(want - actPos) > 30f) { actPos = want; actVel = default; actSet = true; }
            Spring(ref actPos, ref actVel, want, 7f, dt);
            // (a spring of rate w trails a target moving at speed s by 2s/w: at 15 m/s that is metres. Its own velocity,
            // times 2/w, puts that back, so the eased action is where the ball is, and each camera leads by what its own
            // spring loses)
            Vec3 Fv = actVel, F = actPos + Fv * (2f / 7f);
            bool square = !ExpArenaView.CornersRoom(g) && !g.Octagon;
            Vec3 camWant, aimWant;
            float wCam, wAim;
            bool cut = false;
            if (v == 10)
            {   // TRACKING
                camWant = square ? new Vec3(M.Clampf(F.x * 0.7f, -6.5f, 6.5f), 2.7f, 6.3f) : Preset(1).p;
                aimWant = new Vec3(F.x * 0.85f, M.Clampf(F.y * 0.5f + 0.4f, 0.7f, 1.6f), F.z * 0.4f);   // (the ball, the court around it)
                wCam = 1.6f;
                wAim = 3.5f;
            }
            else if (v == 11)
            {   // BALL CAM: low, just outside the side glass (z 2.5), running alongside the ball; in FOUR CORNERS and FIREBALL
                // BLITZ from COURTSIDE, turning with it
                Vec3 d = new Vec3(bv.x, 0, bv.z);
                if (M.Length(d) > 1f) chaseDir = M.Normalize(d);
                d = chaseDir;
                camWant = square ? new Vec3(M.Clampf(F.x - d.x * 0.8f, -8.5f, 8.5f), 1.35f, 3.3f) : Preset(9).p;
                aimWant = new Vec3(F.x + d.x * 0.8f, M.Clampf(F.y, 0.8f, 1.8f), square ? F.z * 0.3f : F.z);
                wCam = 3f;
                wAim = 5f;
            }
            else
            {   // UNDER FIRE
                int wantP = tgt >= 0 ? tgt : fireP;
                if (wantP < 0 || wantP >= g.np) wantP = 0;
                if (wantP != fireWant) { fireWant = wantP; fireWantAt = t; }
                if (fireP < 0 || fireP >= g.np || (wantP != fireP && t - fireWantAt > 0.25 && t - fireAt > 2.5))
                {
                    cut = fireP >= 0;
                    fireP = wantP;
                    fireAt = t;
                }
                int p = fireP;
                Vec3 H = viewInValid[p] ? viewIn[p].head.p : g.tun.start[p] + new Vec3(0, 1.6f, 0);
                Vec3 back = new Vec3(H.x, 0, H.z);   // (away from the middle of the room)
                back = M.Length(back) > 0.5f ? M.Normalize(back) : new Vec3(p == 0 ? -1f : 1f, 0, 0);
                Vec3 side = M.Cross(new Vec3(0, 1, 0), back);   // (over one shoulder)
                camWant = H + back * 2.1f + side * 0.45f + new Vec3(0, 0.55f, 0);
                Vec3 a0 = H - back * 7f + new Vec3(0, -0.4f, 0);
                aimWant = a0 + (F - a0) * 0.45f;
                wCam = 3f;
                wAim = 5f;
            }
            float camLead = v == 11 ? 1f : v == 10 && square ? 0.7f : 0f, aimLead = v == 11 ? 1f : v == 10 ? 0.85f : 0.45f;
            camWant += new Vec3(Fv.x, 0, square ? 0f : Fv.z) * (camLead * 2f / wCam);   // (what the camera's spring loses, given back;
            aimWant += new Vec3(Fv.x, 0, Fv.z) * (aimLead * 2f / wAim);                 //  never up and down: bounces)
            if (!camSet || cut) { camP = camWant; camV = default; aimP = aimWant; aimV = default; camSet = true; }
            Spring(ref camP, ref camV, camWant, wCam, dt);
            Spring(ref aimP, ref aimV, aimWant, wAim, dt);
            return LookFrom(camP, aimP);
        }

        void ShowMine(int i, bool byHand, double t)
        {
            if (i < 0 || i >= dir.warps.Count) return;
            Director.WarpCam w = dir.warps[i];
            if (w.trans != 0 && lastOk) { from = last; glide = 1f; } else glide = 0f;
            view = Mine; my = i;
            followX = w.pose.p.x;
            if (w.fov > 0) fov = w.fov;
            if (byHand) { auto = false; if (eventKind != 0) eventSkip = eventKind; }
            autoAt = t + HoldSecs[w.hold & 3];
        }

        public void Apply(Cmd cmd, List<SpecMatch> ms, Game g, double t)
        {
            if (cmd == Cmd.None) return;
            if (cmd == Cmd.Next && ms.Count > 0)
            {   // the next live match
                int k = 0;
                for (int i = 0; i < ms.Count; ++i) if (ms[i].ip == a.watchIp) k = i + 1;
                a.watchIp = ms[k % ms.Count].ip;
                hubPinned = false;
                Debug.Log("[Desktop] watching " + ms[k % ms.Count].name);
            }
            if (cmd == Cmd.Hub) { a.watchIp = 0; hubPinned = true; Debug.Log("[Desktop] hub"); }
            if (cmd > Cmd.View && cmd < Cmd.View + ViewName.Length)
            {
                view = cmd - Cmd.View; auto = false;
                camSet = false;   // (a follow view starts where it should be)
                if (eventKind != 0) eventSkip = eventKind;   // (picked during the fist bump / winner close-up: show it now)
                Debug.Log("[Desktop] camera " + PresetName(view));
            }
            if (cmd == Cmd.Free)
            {   // the free camera from where the camera is now
                if (view != 0)
                {
                    Pose cur = FollowView(view) && lastOk ? last : Preset(view);
                    Vec3 f = cur.q.Rotate(new Vec3(0, 0, -1));
                    pos = cur.p; yaw = MathF.Atan2(-f.x, -f.z); pitch = MathF.Asin(M.Clampf(f.y, -1f, 1f));
                }
                view = 0; auto = false;
            }
            if (cmd == Cmd.Auto) { auto = !auto; autoAt = t + 8.0; if (auto && view == 0) view = 1; }
            if (cmd == Cmd.MySave) cmd = Cmd.MySet + Math.Min(7, dir.warps.Count);   // (the button: the next free one)
            if (cmd >= Cmd.MySet && cmd < Cmd.MySet + 8)
            {   // MY CAMERA n: the view as it is now
                int i = cmd - Cmd.MySet;
                Pose cur = view == 0 ? new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), yaw) * Quat.AxisAngle(new Vec3(1, 0, 0), pitch), pos)
                         : (view == Mine && my >= 0 && my < dir.warps.Count) || FollowView(view) ? last : Preset(view);
                Vec3 f = cur.q.Rotate(new Vec3(0, 0, -1));
                var w = new Director.WarpCam
                {
                    pose = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-f.x, -f.z)), cur.p),
                    pitch = MathF.Asin(M.Clampf(f.y, -1f, 1f)), fov = fov, hold = newHold, trans = newTrans,
                };
                if (i < dir.warps.Count) { w.trig = dir.warps[i].trig; w.hold = dir.warps[i].hold; w.trans = dir.warps[i].trans; dir.warps[i] = w; }
                else if (dir.warps.Count < 8) dir.warps.Add(w);
                int at = Math.Min(i, dir.warps.Count - 1);
                dir.Save();
                Debug.Log($"[Desktop] my camera {at + 1} saved ({w.pose.p.x:F1} {w.pose.p.y:F1} {w.pose.p.z:F1})");
                view = Mine; my = at; glide = 0; followX = w.pose.p.x;
                hintUntil = 0;
            }
            if (cmd >= Cmd.MyGo && cmd < Cmd.MyGo + 8) ShowMine(cmd - Cmd.MyGo, true, t);
            {
                Director.WarpCam cw = view == Mine && my >= 0 && my < dir.warps.Count ? dir.warps[my] : null;
                if (cmd == Cmd.MyHold) { if (cw != null) cw.hold = (byte)((cw.hold + 1) & 3); else newHold = (byte)((newHold + 1) & 3); dir.Save(); }
                if (cmd == Cmd.MyTrans) { if (cw != null) cw.trans ^= 1; else newTrans ^= 1; dir.Save(); }
                if (cmd == Cmd.MyTrig && cw != null) { cw.trig = (byte)((cw.trig + 1) % 3); dir.Save(); }
                if (cmd == Cmd.MyClear) { dir.warps.Clear(); my = -1; if (view == Mine) view = 1; dir.Save(); Debug.Log("[Desktop] my cameras cleared"); }
            }
            if (cmd == Cmd.Help) help = !help;
            var s = a.settings;
            if (cmd == Cmd.Selfie) { s.selfieOn = !s.selfieOn; Debug.Log("[Desktop] selfie " + (s.selfieOn ? "on" : "off")); }
            if (cmd == Cmd.SelfieCorner) s.selfieCorner = (s.selfieCorner + 1) % 4;
            if (cmd == Cmd.SelfieSize) s.selfieSize = (s.selfieSize + 1) % 3;
            if (cmd == Cmd.SelfieShape) s.selfieRound = !s.selfieRound;
            if (cmd == Cmd.SelfieCam) s.selfieCam = WebCamTexture.devices.Length > 1 ? (s.selfieCam + 1) % WebCamTexture.devices.Length : 0;
            if (cmd >= Cmd.Selfie && cmd <= Cmd.SelfieCam) s.Save(a.RulesToSave);
            if (cmd == Cmd.Layer || cmd == Cmd.LayerBack) { layer = (layer + (cmd == Cmd.Layer ? 1 : 3)) % 4; Debug.Log("[Desktop] view layers " + LayerName[layer]); }
            if (cmd == Cmd.Matchup) { matchupForce = !matchupForce; matchup = matchupForce; Debug.Log("[Desktop] VS screen " + (matchupForce ? "on" : "off")); }
            if (cmd == Cmd.WinStyle) { winStyle = (winStyle + 1) % 3; Debug.Log("[Desktop] win screen " + WinName[winStyle]); }
            if (cmd == Cmd.Glass)
            {   // ARENA GLASS: NORMAL, FAINT (35%), FAINTER (20%), round again; saved on this PC
                s.dcGlass = NextGlass(s.dcGlass);
                s.Save(a.RulesToSave);
                Debug.Log($"[Desktop] arena glass {s.dcGlass * 100f:F0}%");
            }
            if (cmd == Cmd.Full)
            {
                full = !full;
                Screen.fullScreenMode = full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            }
            if (cmd == Cmd.Panel) panelOn = !panelOn;
            if (view == Mine && (my < 0 || my >= dir.warps.Count)) view = 1;   // (cleared, or cleared in the headset)
        }

        // AUTO ROTATION's list on this PC (autocams.cfg; only once the streamer has changed it: until then it follows the
        // cameras)
        void SaveAutoList()
        {
            if (!autoCustom) return;
            try { System.IO.Directory.CreateDirectory(a.settings.Folder); System.IO.File.WriteAllText(AutoFile, AutoRotation.Save(autoList)); }
            catch (Exception e) { Debug.LogWarning("[Desktop] could not save autocams.cfg: " + e.Message); }
        }

        void LoadAutoList()
        {
            autoLoaded = true;
            try
            {
                if (!System.IO.File.Exists(AutoFile)) return;
                autoList.Clear();
                autoList.AddRange(AutoRotation.Load(System.IO.File.ReadAllText(AutoFile)));
                autoCustom = autoList.Count > 0;
                Debug.Log($"[Desktop] auto rotation, the streamer's list ({autoList.Count} rows)");
            }
            catch (Exception e) { Debug.LogWarning("[Desktop] could not read autocams.cfg: " + e.Message); }
        }

        // AUTO ROTATION: the list made whole for the cameras there are; the streamer's ticks, hold times and order (the
        // first change: the list is the streamer's from then on)
        public void AutoList(Cmd cmd)
        {
            if (!autoLoaded) LoadAutoList();
            var trig = new List<byte>();
            foreach (var w in dir.warps) trig.Add(w.trig);
            if (AutoRotation.Sync(autoList, autoCustom, trig)) SaveAutoList();
            int n = autoList.Count;
            if (autoSel >= n) autoSel = -1;
            if (cmd >= Cmd.AutoPick && cmd < Cmd.AutoPick + 32 && cmd - Cmd.AutoPick < n) autoSel = cmd - Cmd.AutoPick;
            if (cmd >= Cmd.AutoTick && cmd < Cmd.AutoTick + 32 && cmd - Cmd.AutoTick < n)
            {
                int r = cmd - Cmd.AutoTick;
                autoCustom = true;
                autoList[r].on = !autoList[r].on;
                autoSel = r;
                SaveAutoList();
                Debug.Log($"[Desktop] auto rotation, row {r + 1} {(autoList[r].on ? "ticked" : "unticked")}");
            }
            int sel = autoSel;
            if (cmd == Cmd.AutoHold && sel >= 0 && sel < n)
            {   // yours: its own hold (the headset's too); a preset's: 2, 4, 9, 15 s
                AutoRotation.Item it = autoList[sel];
                if (it.mine) { dir.warps[it.id].hold = (byte)((dir.warps[it.id].hold + 1) & 3); dir.Save(); }
                else { it.hold = AutoRotation.NextHold(it.hold); autoCustom = true; SaveAutoList(); }
            }
            if ((cmd == Cmd.AutoUp || cmd == Cmd.AutoDown) && sel >= 0 && sel < n)
            {
                int to = sel + (cmd == Cmd.AutoUp ? -1 : 1);
                if (to >= 0 && to < n)
                {
                    (autoList[sel], autoList[to]) = (autoList[to], autoList[sel]);
                    autoCustom = true;
                    SaveAutoList();
                    autoSel = to;
                }
            }
        }

        // a row's name and hold as the AUTO tab lists them
        public string AutoRowName(AutoRotation.Item it)
        {
            string[] kName = { "", "Side", "High side", "Blue end", "Orange end", "Top", "Blue's eyes", "Orange's eyes", "Corner", "Courtside",
                               "Tracking", "Ball cam", "Under fire" };
            if (!it.mine) return kName[it.id >= 0 && it.id < kName.Length ? it.id : 0];
            return $"My camera {it.id + 1}" + (it.id < dir.warps.Count && dir.warps[it.id].trig == 2 ? " (follow)" : "");
        }
        public string AutoRowHold(AutoRotation.Item it) =>
            (it.mine ? (it.id < dir.warps.Count ? HoldSecs[dir.warps[it.id].hold & 3] : 0f) : it.hold).ToString("0") + " s";

        // AUTO: with your cameras, an action camera near a hit takes over; and AUTO ROTATION's list goes round, its ticked
        // rows in order, each its hold (native desktopControls, 0.3.20)
        void AutoDirector(Game g, double t)
        {
            if (!auto || a.watchIp == 0) return;
            if (dir.warps.Count > 0)
                foreach (GameEvent e in g.events)
                {
                    if (e.type != EventType.HitPlayer && e.type != EventType.Strike && e.type != EventType.MatchEnd) continue;
                    int best = -1;
                    float bd = 1e9f;
                    for (int i = 0; i < dir.warps.Count; ++i)
                    {
                        float d = M.Length(dir.warps[i].pose.p - e.pos);
                        if (dir.warps[i].trig == 1 && !(view == Mine && i == my) && d < bd) { bd = d; best = i; }
                    }
                    if (best >= 0) ShowMine(best, false, t);
                }
            if (t <= autoAt) return;
            var L = autoList;
            int cur = view == Mine ? AutoRotation.RowOf(L, true, my) : AutoRotation.RowOf(L, false, view);
            if (cur < 0) cur = autoRow;   // (an action camera, or the free camera: on from the row played last)
            int r = AutoRotation.Next(L, cur);
            if (r < 0) autoAt = t + 1.0;   // (nothing ticked: the camera stays)
            else
            {
                autoRow = r;
                if (L[r].mine) ShowMine(L[r].id, false, t);
                else { view = L[r].id; autoAt = t + L[r].hold; camSet = false; }   // (a follow view after another: a cut, not a swoop)
            }
        }

        // the broadcast moments (native eventCamera): the fist bump (never in the free camera), WIN SCREEN > WINNER;
        // eased in and back over half a second
        Pose EventCamera(Pose b, Game g, PlayerInput[] viewIn, double t, float dt, bool freeCam)
        {
            int kind = 0;
            if (a.watchIp != 0 && g.match == MatchState.Meet && !freeCam) kind = 1;
            else if (a.watchIp != 0 && winStyle == 1 && g.match == MatchState.MatchSummary && g.matchWinner >= 0 && g.matchWinner < 2) kind = 2;
            if (kind != eventSkip) eventSkip = 0;
            if (kind != 0 && kind == eventSkip) kind = 0;
            if (kind != 0 && (kind != eventKind || eventMix <= 0)) { eventKind = kind; eventT0 = t; }
            eventMix = M.Clampf(eventMix + (kind != 0 ? dt : -dt) * 2f, 0f, 1f);
            if (eventMix <= 0) { eventKind = 0; return b; }
            float age = (float)(t - eventT0);
            Pose ev;
            if (eventKind == 1)
            {   // the fists meet between the two heads, a little lower
                Vec3 T = (viewIn[0].head.p + viewIn[1].head.p) * 0.5f;
                T.y -= 0.30f;
                float push = 1.75f - 0.40f * Mathf.Min(1f, age / 4f);
                ev = LookFrom(T + new Vec3(0.20f, 0.10f, push), T);
            }
            else
            {
                int w = Math.Max(0, Math.Min(1, g.matchWinner));
                Vec3 H = viewIn[w].head.p;
                winT = age < 0.05f ? H : winT + (H - winT) * Mathf.Min(1f, dt * 3f);
                float side = w == 0 ? 1f : -1f, ang = 0.5f * MathF.Sin(age * 0.35f);
                ev = LookFrom(winT + new Vec3(side * 1.9f * MathF.Cos(ang), 0.05f, 1.9f * MathF.Sin(ang)), winT - new Vec3(0, 0.25f, 0));
            }
            float k2 = eventMix * eventMix * (3 - 2 * eventMix);
            return Blend(b, ev, k2);
        }

        // what is on screen, for the controls panel (native: its status text)
        public string Status(List<SpecMatch> ms)
        {
            SpecMatch w = null;
            foreach (var m in ms) if (m.ip == a.watchIp) w = m;
            string mine = "none saved yet";
            if (dir.warps.Count > 0)
            {
                Director.WarpCam cw = view == Mine && my >= 0 && my < dir.warps.Count ? dir.warps[my] : null;
                mine = cw != null ? $"{dir.warps.Count} saved; this one {HoldName[cw.hold & 3]}, {(cw.trans != 0 ? "fade" : "cut")}, {TrigName[Math.Min(2, (int)cw.trig)]}"
                                  : $"{dir.warps.Count} saved";
            }
            var s = a.settings;
            string selfie = !s.selfieOn ? "off (C)" : $"{CornerName[s.selfieCorner & 3]}, {SizeName[s.selfieSize % 3]}, {(s.selfieRound ? "round" : "square")}";
            return $"Now: {(w != null ? (w.name.Length > 0 ? w.name : "live match") : "the spectator hub")}\n" +
                   $"Camera: {(w != null ? ViewLabel() : "the hub's")}{(auto ? " (auto)" : "")}, layers: {LayerName[layer & 3]}{(layer == 3 ? " (transparent)" : "")}\n" +
                   $"My cameras: {mine}\nWin screen: {WinName[winStyle % 3]}\nArena glass: {GlassName(s.dcGlass)}\nSelfie: {selfie}";
        }
        // ---- the picture's overlay (native desktopHud): drawn just past the near plane, in view units (-1..1 across
        // and up), so nothing in the arena comes between
        const float HudD = 0.1f;
        double winAt;
        FxRenderer hf;
        Pose he;
        float hty, htx;
        void Txt(string s, float x, float y, float h, Vector4 c) => hf.Text(s, new Pose(he.q, he.Transform(new Vec3(x * htx * HudD, y * hty * HudD, -HudD))), h * hty * HudD, c);
        void Bar(float x, float y, float w, float h, Vector4 c) => hf.Panel(new Pose(he.q, he.Transform(new Vec3(x * htx * HudD, y * hty * HudD, -HudD * 1.002f))), w * htx * HudD, h * hty * HudD, c);

        public void DrawHud(FxRenderer fx, AvatarDraw av, ArenaSession session, Online.Net net, Game g, Pose eye, float tanHalfY, float aspect,
                            PlayerInput[] viewIn, float bumpFlash, string notice, string[][] careerLines = null)
        {
            hf = fx; he = eye; hty = tanHalfY; htx = tanHalfY * aspect;
            if (g.match == MatchState.MatchSummary && g.matchWinner >= 0) { if (winAt <= 0) winAt = Time.timeAsDouble; }
            else winAt = 0;
            Vector4 white = new Vector4(1, 1, 1, 1), grey = new Vector4(0.65f, 0.7f, 0.78f, 1), cyan = new Vector4(0.3f, 0.85f, 1.0f, 1);
            Vector4 blue = new Vector4(0.35f, 0.75f, 1.0f, 1), orange = new Vector4(1.0f, 0.6f, 0.25f, 1), gold = new Vector4(1.0f, 0.85f, 0.3f, 1);
            Vector4 bar = new Vector4(0.0f, 0.01f, 0.03f, 0.72f);
            if (a.nationsOn)
            {
                blue = new Vector4(0.65f * a.sideRGB[0][0] + 0.35f, 0.65f * a.sideRGB[0][1] + 0.35f, 0.65f * a.sideRGB[0][2] + 0.35f, 1);
                orange = new Vector4(0.65f * a.sideRGB[1][0] + 0.35f, 0.65f * a.sideRGB[1][1] + 0.35f, 0.65f * a.sideRGB[1][2] + 0.35f, 1);
            }
            void Flag(int p, float x, float y, float h)
            {
                if (a.sideNation[p] == 0) return;
                av.DrawFlag(a.sideNation[p], new Pose(he.q, he.Transform(new Vec3(x * htx * HudD, y * hty * HudD, -HudD * 1.001f))), h * hty * HudD * 1.5f);
            }
            List<SpecMatch> ms = net.Matches;
            SpecMatch w = null;
            foreach (var m in ms) if (m.ip == a.watchIp) w = m;
            if (w != null && CloseUps(g)) DrawCloseUps(av, session, g, careerLines ?? new[] { new string[0], new string[0] });
            if (w != null)
            {
                string Who(int p) => session.PlayerLabel(p);
                if (g.np == 4)
                {   // FOUR CORNERS: four names and scores in their colours, or the two teams
                    string[][] teamName = { new[] { "BLUE TEAM", "ORANGE TEAM" }, new[] { "THE THREE", "THE ONE" } };
                    Bar(0, 0.86f, 1.70f, 0.26f, bar);
                    bool ffa = g.TeamsNow() == Teams.FFA;
                    if (ffa || g.Octagon)   // (FIREBALL BLITZ: the four, in their team's colours in teams; TASKS.md E51)
                        for (int p = 0; p < 4; ++p)
                        {
                            float[] pc = ExpArenaView.PlayerRGB(g, a.sideRGB, p);
                            var c = new Vector4(0.6f * pc[0] + 0.4f, 0.6f * pc[1] + 0.4f, 0.6f * pc[2] + 0.4f, 1);
                            float x = -0.60f + 0.40f * p;
                            Txt(Who(p), x, 0.92f, 0.045f, c);
                            Txt(g.pl[p].score.ToString(), x, 0.84f, 0.065f, white);
                        }
                    else
                    {
                        bool one = g.TeamsNow() == Teams.ThreeVOne;
                        for (int t = 0; t < 2; ++t)
                        {
                            float[] tc = ExpArenaView.TeamRGB(g, a.sideRGB, t);
                            var c = new Vector4(0.6f * tc[0] + 0.4f, 0.6f * tc[1] + 0.4f, 0.6f * tc[2] + 0.4f, 1);
                            float x = t != 0 ? 0.42f : -0.42f;
                            string names = "";
                            for (int p = 0; p < 4; ++p) if (g.Team(p) == t) names += (names.Length == 0 ? "" : "  ") + Who(p);
                            Txt(teamName[one ? 1 : 0][t], x, 0.93f, 0.04f, c);
                            Txt(names, x, 0.875f, 0.026f, c);
                        }
                        Txt($"{g.TeamScore(0)}  -  {g.TeamScore(1)}", 0, 0.90f, 0.08f, white);
                    }
                    Txt(Scoreboard.RulesLine(g.tun) + "  -  " + Scoreboard.ClockLine(g), 0, 0.765f, 0.03f, grey);
                    if (g.match == MatchState.MatchSummary && g.matchWinner >= 0)
                    {
                        int wt = g.matchWinner;
                        float[] tc = ExpArenaView.TeamRGB(g, a.sideRGB, wt);
                        var c = new Vector4(0.6f * tc[0] + 0.4f, 0.6f * tc[1] + 0.4f, 0.6f * tc[2] + 0.4f, 1);
                        Bar(0, -0.55f, 1.40f, 0.40f, bar);
                        Txt(ffa ? Who(wt) : teamName[g.TeamsNow() == Teams.ThreeVOne ? 1 : 0][wt & 1], 0, -0.47f, 0.12f, c);
                        Txt(ffa ? "WINS!" : "WIN!", 0, -0.62f, 0.08f, gold);
                    }
                }
                else
                {
                    Bar(0, 0.88f, a.nationsOn ? 1.50f : 1.15f, 0.2f, bar);
                    Flag(0, -0.66f, 0.88f, 0.10f);   // NATIONS: the flags at the banner's ends
                    Flag(1, 0.66f, 0.88f, 0.10f);
                    Txt(Who(0), -0.30f, 0.90f, 0.06f, blue);
                    Txt(Who(1), 0.30f, 0.90f, 0.06f, orange);
                    Txt($"{g.pl[0].score}  -  {g.pl[1].score}", 0, 0.90f, 0.08f, white);
                    string clk = Scoreboard.ClockLine(g), rl = Scoreboard.RulesLine(g.tun);
                    Txt(g.tun.advanced ? $"{rl}  -  ROUND {Math.Max(1, g.round)}  ({g.pl[0].roundWins} - {g.pl[1].roundWins}){(clk.Length == 0 ? "" : "  -  ")}{clk}"
                                       : $"{rl}  -  {clk}", 0, 0.815f, 0.032f, grey);
                }
                Txt((w.name.Length > 0 ? w.name : "LIVE MATCH") + (ms.Count > 1 ? "   (TAB: NEXT MATCH)" : ""), -0.62f, -0.93f, 0.028f, grey);
                if (!(layer >= 3 && !help))   // (KEY: a clean picture unless F2)
                    Txt(ViewLabel() + (auto ? "  (AUTO)" : "") + (layer != 0 ? "   " + LayerName[layer & 3] : ""), 0.70f, -0.93f, 0.028f, grey);
                if (VsScreen(g)) { }   // (the VS screen: no FIST BUMP text over the portraits)
                else if (bumpFlash > 0)
                {   // FIST BUMP! pops in
                    float s = 1f + 0.5f * Mathf.Max(0f, (bumpFlash - 0.8f) / 0.2f);
                    Txt("FIST BUMP!", 0, 0.66f, 0.09f * s, new Vector4(gold.x, gold.y, gold.z, Mathf.Min(1f, bumpFlash * 2f)));
                }
                else if (g.match == MatchState.Meet) Txt("FIST BUMP TO START", 0, 0.66f, 0.05f, white);
                if (a.nationsOn && g.np == 2 && !VsScreen(g) && (g.match == MatchState.Meet || g.match == MatchState.WaitingForPlayers || bumpFlash > 0))
                {   // the matchup card: UNITED STATES  VS  FRANCE, flag over flag
                    Bar(0, 0.42f, 1.30f, 0.34f, bar);
                    Vec3 r = he.q.Rotate(new Vec3(1, 0, 0));
                    bool swap = M.Dot(viewIn[0].head.p - viewIn[1].head.p, r) > 0;   // (blue stands on the right here)
                    for (int p = 0; p < 2; ++p)
                    {
                        float x = (p != 0) != swap ? 0.36f : -0.36f;
                        Flag(p, x, 0.47f, 0.17f);
                        Txt(session.SideName(p), x, 0.33f, 0.035f, p != 0 ? orange : blue);
                    }
                    Txt("VS", 0, 0.42f, 0.07f, gold);
                }
                if (g.np == 2 && g.match == MatchState.MatchSummary && g.matchWinner >= 0 && g.matchWinner < 2 && !WinSplit(g))
                {   // the win splash: the winner's name over the lower third
                    int wn = g.matchWinner;
                    float age = winAt > 0 ? (float)(Time.timeAsDouble - winAt) : 0f;
                    float pop = 1f + 0.6f * Mathf.Pow(Mathf.Max(0f, 1f - age / 0.4f), 2f), pulse = 0.85f + 0.15f * Mathf.Sin(age * 4f);
                    Vector4 tc = wn == 0 ? blue : orange;
                    if (a.sideNation[wn] != 0)
                    {
                        Flag(wn, 0, -0.24f, 0.17f * Mathf.Min(1f, age / 0.25f + 0.3f));
                        Txt(session.SideName(wn), 0, -0.785f, 0.035f, tc);
                    }
                    Bar(0, -0.55f, 1.40f * Mathf.Min(1f, age / 0.25f + 0.2f), 0.40f, bar);
                    Bar(0, -0.55f, 1.46f * Mathf.Min(1f, age / 0.25f + 0.2f), 0.44f, bar);
                    string nm = Who(wn).ToUpperInvariant();
                    Txt(nm, 0.004f, -0.454f, 0.15f * pop, new Vector4(tc.x, tc.y, tc.z, 0.35f * pulse));
                    Txt(nm, 0, -0.45f, 0.14f * pop, tc);
                    Txt("WINS!", 0, -0.61f, 0.09f * pop * pulse, gold);
                    Txt(g.tun.advanced ? $"ROUNDS  {g.pl[0].roundWins}  -  {g.pl[1].roundWins}" : $"FINAL  {g.pl[0].score}  -  {g.pl[1].score}", 0, -0.71f, 0.045f, white);
                }
            }
            else
            {   // the hub: what there is to watch
                Txt("PROJECT ARENA  -  SPECTATOR", 0, 0.88f, 0.07f, cyan);
                Txt("VERSION " + Application.version, 0.85f, -0.85f, 0.024f, grey);
                if (!net.Online) Txt("ON YOUR HOME NETWORK", 0, 0.79f, 0.035f, grey);
                else Txt(HubLine(net, ms), 0, 0.79f, 0.035f, ms.Count == 0 ? grey : white);
            }
            DrawSelfie(white, grey, cyan);
            if (!help && Time.timeAsDouble < hintUntil) Txt("F1: CONTROLS", 0.82f, 0.93f, 0.028f, grey);
            if (help)
            {
                bool padOk = Gamepad.current != null;
                Bar(0, -0.80f, 1.96f, 0.13f, bar);
                Txt("1-9 CAMERAS   T B U FOLLOW   CTRL+1-8 SAVE   SHIFT+1-8 MINE   A AUTO   F FREE (RMB + WASD / Q E)   WHEEL ZOOM   V LAYERS   TAB NEXT   H HUB   F11 FULL   F2 HIDE",
                    0, padOk ? -0.775f : -0.80f, 0.026f, white);
                if (padOk)
                    Txt("CONTROLLER:  A NEXT MATCH   B HUB   X AUTO   Y FREE (STICKS, LB / RB)   D-PAD CAMERAS   TRIGGERS ZOOM   VIEW HIDE", 0, -0.825f, 0.026f, white);
            }
            if (!string.IsNullOrEmpty(notice)) Txt(notice, 0, 0.66f, 0.04f, orange);
        }

        // the hub's title line (native 0.3.16): live matches and rooms waiting counted apart
        public static string HubLine(Online.Net net, List<SpecMatch> ms)
        {
            if (ms.Count == 0) return "NO LIVE MATCHES YET  -  THEY APPEAR HERE WHEN SOMEONE PLAYS";
            int live = 0;
            foreach (var m in ms) live += net.Live(m) ? 1 : 0;
            return HubLine(live, ms.Count - live);
        }
        public static string HubLine(int live, int waiting)
        {
            if (live + waiting == 0) return "NO LIVE MATCHES YET  -  THEY APPEAR HERE WHEN SOMEONE PLAYS";
            string Lv(int n) => n + (n == 1 ? " LIVE MATCH" : " LIVE MATCHES");
            string Rm(int n) => n + (n == 1 ? " ROOM" : " ROOMS");
            if (live > 0 && waiting > 0) return $"{Lv(live)}, {Rm(waiting)} WAITING  -  TAB TO WATCH";
            if (live > 0) return $"{Lv(live)}  -  TAB TO WATCH";
            return $"{Rm(waiting)} WAITING FOR PLAYERS  -  TAB TO WATCH";
        }

        // ---- SELFIE (native drawSelfie / webcam): your webcam in a corner of the picture (read only on this PC, never sent)
        WebCamTexture cam;
        Texture2D selfieTex;
        Color32[] camPx;
        int camAt = -1;
        bool camRound;
        public PanelBatch panels;
        public string SelfieStatus { get; private set; } = "";

        void UpdateWebcam()
        {
            var s = a.settings;
            if (!s.selfieOn)
            {
                if (cam != null) { cam.Stop(); UnityEngine.Object.Destroy(cam); cam = null; Debug.Log("[Desktop] webcam off"); }
                return;
            }
            WebCamDevice[] ds = WebCamTexture.devices;
            if (ds.Length == 0) { SelfieStatus = "NO CAMERA FOUND"; return; }
            int want = Math.Max(0, Math.Min(ds.Length - 1, s.selfieCam));
            if (cam != null && camAt != want) { cam.Stop(); UnityEngine.Object.Destroy(cam); cam = null; }
            if (cam == null)
            {
                camAt = want;
                cam = new WebCamTexture(ds[want].name, 640, 480, 30);
                cam.Play();
                SelfieStatus = "STARTING";
                Debug.Log("[Desktop] webcam " + (want + 1) + " of " + ds.Length);
            }
            if (!cam.isPlaying) { SelfieStatus = "CAMERA IN USE OR BLOCKED"; return; }
            if (!cam.didUpdateThisFrame && selfieTex != null && camRound == s.selfieRound) return;
            int w = cam.width, h = cam.height;
            if (w < 16 || h < 16) return;   // (no picture yet)
            if (selfieTex == null || selfieTex.width != w || selfieTex.height != h)
            {
                selfieTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                camPx = new Color32[w * h];
            }
            cam.GetPixels32(camPx);
            camRound = s.selfieRound;
            if (camRound) RoundMask(camPx, w, h);
            selfieTex.SetPixels32(camPx);
            selfieTex.Apply(false);
            SelfieStatus = "";
        }

        // a circle as tall as the picture, its edge soft (native webcam::setRound)
        public static void RoundMask(Color32[] px, int w, int h)
        {
            float r = h * 0.5f, cx = w * 0.5f, cy = h * 0.5f;
            for (int y = 0; y < h; ++y)
                for (int x = 0; x < w; ++x)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * w + x].a = (byte)(255f * Mathf.Clamp01((r - d) / 1.5f));
                }
        }

        void DrawSelfie(Vector4 white, Vector4 grey, Vector4 cyan)
        {
            UpdateWebcam();
            var s = a.settings;
            if (!s.selfieOn) return;
            float[] heights = { 0.34f, 0.48f, 0.64f };   // (of the view's height, -1..1 units)
            float hh = heights[s.selfieSize % 3];
            float aspect = selfieTex != null ? (float)selfieTex.width / selfieTex.height : 4f / 3f;
            float visW = (s.selfieRound ? hh : hh * aspect) * hty / htx;
            bool right = (s.selfieCorner & 1) != 0, top = s.selfieCorner < 2;
            float cx = (right ? 1f : -1f) * (0.98f - 0.5f * visW), cy = top ? 0.68f - 0.5f * hh : -0.84f + 0.5f * hh;
            Vec3 c = he.Transform(new Vec3(cx * htx * HudD, cy * hty * HudD, -HudD * 0.999f));
            if (selfieTex != null && SelfieStatus.Length == 0 && panels != null)
            {
                panels.Quad(selfieTex, new Pose(he.q, c), hh * hty * HudD * aspect, hh * hty * HudD, white);
                if (s.selfieRound) hf.Sprite(c, hh * hty * HudD * 1.08f, new Vector4(0.12f, 0.36f, 0.48f, 1), he.p, 1);
                else
                {
                    float ew = hh * hty * HudD * aspect, eh = hh * hty * HudD, t = 0.006f * hty * HudD;
                    var edge = new Vector4(cyan.x, cyan.y, cyan.z, 0.9f);
                    hf.Panel(new Pose(he.q, c + he.q.Rotate(new Vec3(0, 0.5f * eh, 0.00005f))), ew + t, t, edge);
                    hf.Panel(new Pose(he.q, c + he.q.Rotate(new Vec3(0, -0.5f * eh, 0.00005f))), ew + t, t, edge);
                    hf.Panel(new Pose(he.q, c + he.q.Rotate(new Vec3(0.5f * ew, 0, 0.00005f))), t, eh + t, edge);
                    hf.Panel(new Pose(he.q, c + he.q.Rotate(new Vec3(-0.5f * ew, 0, 0.00005f))), t, eh + t, edge);
                }
            }
            else hf.Text("SELFIE: " + (SelfieStatus.Length == 0 ? "STARTING" : SelfieStatus), new Pose(he.q, c), 0.03f * hty * HudD, grey);
        }

        public void Stop() { if (cam != null) { cam.Stop(); UnityEngine.Object.Destroy(cam); cam = null; } }
        // ---- the VS screen and WIN SCREEN > SPLIT (native desktopRender): each player alone in a close-up on a backdrop
        // in their colour, side by side. Rendered into two pictures before the frame's own drawing (so nothing else is
        // in them: the player's body and hands are drawn for that camera only, on a layer of their own), then laid over
        // the picture with the labels.
        public const int CloseLayer = 31;
        readonly Camera[] closeCam = new Camera[2];
        readonly RenderTexture[] closeTex = new RenderTexture[2];
        readonly Vec3[] close = new Vec3[2];
        // the close-ups' players as last seen in the room (native 0.3.19): one who leaves while they show stays, still, with
        // their name (the host puts a bot's input in an empty place, so their own last pose, not the place's new one)
        readonly PlayerInput[] heldIn = { new PlayerInput(), new PlayerInput() };
        readonly string[] heldName = new string[2];
        readonly bool[] away = new bool[2];   // (that player has left: the held pose and name are drawn)
        readonly PlayerInput liveIn = new PlayerInput();
        double vsT, closeT;
        public PanelBatch closePanels;   // (Transparent+305: over the arena and its effects, under the text)
        public bool CloseUps(Game g) => VsScreen(g) || WinSplit(g);

        // drawPlayer(p, camera): MatchView draws that player's body and hands for that camera, on CloseLayer
        public void RenderCloseUps(Game g, PlayerInput[] viewIn, int W, int H, float dt, Action<int, Camera> drawPlayer, Func<int, string> label)
        {
            bool vs = VsScreen(g) && !WinSplit(g);
            double t = Time.timeAsDouble;
            if (vs) { if (vsT == 0) vsT = t; } else vsT = 0;
            if (!vs) { if (closeT == 0) closeT = t; } else closeT = 0;
            float age = (float)(t - (vs ? vsT : closeT));
            int hw = Math.Max(16, W / 2), ph = vs ? Math.Max(16, H * 58 / 100) : Math.Max(16, H);
            for (int half = 0; half < 2; ++half)
            {
                int p = vs ? half : (half == 0 ? g.matchWinner : 1 - g.matchWinner);
                if (closeTex[half] == null || closeTex[half].width != hw || closeTex[half].height != ph)
                {
                    if (closeTex[half] != null) closeTex[half].Release();
                    closeTex[half] = new RenderTexture(hw, ph, 24) { name = "close-up " + half };
                }
                if (closeCam[half] == null)
                {
                    var go = new GameObject("Close-up camera " + half) { hideFlags = HideFlags.HideAndDontSave };
                    closeCam[half] = go.AddComponent<Camera>();
                    closeCam[half].enabled = false;
                    closeCam[half].stereoTargetEye = StereoTargetEyeMask.None;
                    closeCam[half].clearFlags = CameraClearFlags.SolidColor;
                    closeCam[half].cullingMask = 1 << CloseLayer;
                    closeCam[half].nearClipPlane = 0.05f;
                    closeCam[half].farClipPlane = 200f;
                }
                Camera c = closeCam[half];
                c.targetTexture = closeTex[half];
                c.aspect = (float)hw / ph;
                // the player as the room last listed them (no room list yet: everyone is here)
                RoomInfo wr = a.watchRoom;
                bool listed = wr.botMask != 0 || wr.members.Count > 0;
                for (int k = 0; k < wr.slot.Length; ++k) listed |= wr.slot[k] != 0;
                bool here = !listed || wr.slot[p] != 0 || wr.IsBot(p);
                if (age < 0.05f || here || heldName[p] == null) { heldIn[p].CopyFrom(viewIn[p]); heldName[p] = label(p); }
                away[p] = !here;
                if (!here) { liveIn.CopyFrom(viewIn[p]); viewIn[p].CopyFrom(heldIn[p]); }
                Pose e;
                Vec3 hd = viewIn[p].head.p;
                if (vs)
                {   // blue on the left, facing right (the camera on their left), orange mirrored
                    Vec3 fwd = viewIn[p].head.q.Rotate(new Vec3(0, 0, -1));
                    fwd.y = 0;
                    fwd = M.Length(fwd) > 1e-3f ? M.Normalize(fwd) : new Vec3(p != 0 ? -1f : 1f, 0, 0);
                    Vec3 right = M.Cross(fwd, new Vec3(0, 1, 0));
                    float s = half != 0 ? 1f : -1f;
                    close[p] = age < 0.05f ? hd : close[p] + (hd - close[p]) * Mathf.Min(1f, dt * 4f);
                    Vec3 aim = close[p] - new Vec3(0, 0.14f, 0);
                    float push = 0.95f - 0.12f * Mathf.Min(1f, age / 3f);
                    e = LookFrom(aim + fwd * push + right * (s * 0.34f) + new Vec3(0, 0.04f, 0), aim);
                    c.fieldOfView = 34f;
                    c.backgroundColor = layer >= 3 ? new Color(0, 0, 0, 0)
                        : new Color(0.07f * a.sideRGB[p][0] + 0.005f, 0.07f * a.sideRGB[p][1] + 0.005f, 0.07f * a.sideRGB[p][2] + 0.005f, 1);
                }
                else
                {   // in front of the player (blue stands at -X and faces +X), the head and shoulders, drifting
                    close[p] = age < 0.05f ? hd : close[p] + (hd - close[p]) * Mathf.Min(1f, dt * 3f);
                    float side = p == 0 ? 1f : -1f, ang = 0.35f * MathF.Sin(age * 0.4f + p * 2f);
                    Vec3 at = close[p] - new Vec3(0, 0.32f, 0);
                    e = LookFrom(at + new Vec3(side * 1.45f * MathF.Cos(ang), 0.18f, 1.45f * MathF.Sin(ang)), at);
                    c.fieldOfView = 40f;
                    float[][] bg = { new[] { 0.010f, 0.030f, 0.070f }, new[] { 0.070f, 0.028f, 0.010f } };   // a dark blue / orange backdrop
                    if (a.nationsOn) for (int k = 0; k < 3; ++k) bg[p][k] = 0.07f * a.sideRGB[p][k] + 0.005f;   // (or the country's)
                    c.backgroundColor = layer >= 3 ? new Color(0, 0, 0, 0) : new Color(bg[p][0], bg[p][1], bg[p][2], 1);
                }
                c.transform.SetPositionAndRotation(NativeSpace.ToUnity(e.p), NativeSpace.ToUnity(e.q));
                drawPlayer(p, c);
                c.Render();
                if (!here) viewIn[p].CopyFrom(liveIn);
            }
        }

        // the close-ups laid over the picture, and their labels (native desktopRender's VS and SPLIT drawing)
        void DrawCloseUps(AvatarDraw av, ArenaSession session, Game g, string[][] careerLines)
        {
            if (closePanels == null || closeTex[0] == null || closeTex[1] == null) return;
            bool vs = VsScreen(g) && !WinSplit(g);
            Vector4 white = new Vector4(1, 1, 1, 1), grey = new Vector4(0.70f, 0.74f, 0.80f, 1), gold = new Vector4(1.0f, 0.85f, 0.3f, 1);
            Vector4 bar = new Vector4(0.0f, 0.01f, 0.03f, vs ? 0.80f : 0.72f), line = new Vector4(0.9f, 0.9f, 1.0f, 0.5f);
            void Pic(int half, float cy, float h)
            {
                float cx = half == 0 ? -0.5f : 0.5f;
                closePanels.Quad(closeTex[half], new Pose(he.q, he.Transform(new Vec3(cx * htx * HudD, cy * hty * HudD, -HudD * 1.004f))), htx * HudD, h * hty * HudD,
                                 new Vector4(1, 1, 1, 1));
            }
            Vector4 Col(int p) => new Vector4(0.65f * a.sideRGB[p][0] + 0.35f, 0.65f * a.sideRGB[p][1] + 0.35f, 0.65f * a.sideRGB[p][2] + 0.35f, 1);
            string Name(int p) => (away[p] && heldName[p] != null ? heldName[p] : session.PlayerLabel(p)).ToUpperInvariant();   // (one who left: as last seen)
            if (vs)
            {
                float age = vsT > 0 ? (float)(Time.timeAsDouble - vsT) : 0f;
                Pic(0, 0.42f, 1.16f); Pic(1, 0.42f, 1.16f);   // (the top 58 %)
                closePanels.Quad(Texture2D.whiteTexture, new Pose(he.q, he.Transform(new Vec3(0, -0.58f * hty * HudD, -HudD * 1.004f))), 2f * htx * HudD, 0.84f * hty * HudD,
                                 new Vector4(0.01f, 0.012f, 0.02f, 1));   // (below them: dark, the stats on it)
                Bar(0, 0.16f, 0.004f, 1.68f, line);
                for (int p = 0; p < 2; ++p)
                {
                    float cx = p != 0 ? 0.5f : -0.5f;
                    Bar(cx, -0.58f, 0.94f, 0.80f, bar);
                    string nm = Name(p);
                    float fit = Mathf.Min(1f, 9f / Mathf.Max(1, nm.Length));
                    Txt(nm, cx, -0.27f, 0.10f * fit, Col(p));
                    if (a.sideNation[p] != 0)
                    {
                        av.DrawFlag(a.sideNation[p], new Pose(he.q, he.Transform(new Vec3((cx - 0.36f) * htx * HudD, -0.27f * hty * HudD, -HudD * 1.001f))), 0.09f * hty * HudD * 1.5f);
                        Txt(session.SideName(p), cx, -0.355f, 0.032f, grey);
                    }
                    string[] ls = careerLines[p];
                    for (int k = 0; k < ls.Length && k < 5; ++k) Txt(ls[k], cx, -0.46f - 0.085f * k, 0.042f, k != 0 ? grey : white);
                }
                float pop = 1f + 0.8f * Mathf.Pow(Mathf.Max(0f, 1f - age / 0.35f), 2f);
                Txt("VS", 0.006f, 0.16f, 0.22f * pop, new Vector4(1.0f, 0.55f, 0.1f, 0.45f));
                Txt("VS", 0, 0.165f, 0.20f * pop, gold);
                return;
            }
            float ag = closeT > 0 ? (float)(Time.timeAsDouble - closeT) : 0f;
            int wn = g.matchWinner;
            Pic(0, 0f, 2f); Pic(1, 0f, 2f);
            Bar(0, 0, 0.004f, 2f, line);   // the dividing line
            Vector4 blue = new Vector4(0.35f, 0.75f, 1.0f, 1), orange = new Vector4(1.0f, 0.6f, 0.25f, 1);
            if (a.nationsOn) { blue = Col(0); orange = Col(1); }
            for (int half = 0; half < 2; ++half)
            {   // the label: the winner's name and WINS!, the other's name and the score
                int p = half == 0 ? wn : 1 - wn;
                float ox = half == 0 ? -0.5f : 0.5f;
                Bar(ox, -0.72f, 0.9f, 0.42f, bar);
                if (a.sideNation[p] != 0)
                    av.DrawFlag(a.sideNation[p], new Pose(he.q, he.Transform(new Vec3((ox - 0.35f) * htx * HudD, -0.72f * hty * HudD, -HudD * 1.001f))), 0.26f * hty * HudD * 1.5f);
                string nm = Name(p);
                float pop = p == wn ? 1f + 0.6f * Mathf.Pow(Mathf.Max(0f, 1f - ag / 0.4f), 2f) : 1f;
                float fit = Mathf.Min(1f, 7f / Mathf.Max(1, nm.Length));
                Txt(nm, ox + (a.sideNation[p] != 0 ? 0.06f : 0f), -0.60f, (p == wn ? 0.15f : 0.13f) * pop * fit, p == 0 ? blue : orange);
                if (p == wn) Txt("WINS!", ox, -0.84f, 0.10f * pop * (0.85f + 0.15f * Mathf.Sin(ag * 4f)), gold);
                else Txt(g.tun.advanced ? $"ROUNDS  {g.pl[wn].roundWins} - {g.pl[p].roundWins}" : $"FINAL  {g.pl[wn].score} - {g.pl[p].score}", ox, -0.82f, 0.075f, white);
            }
        }
    }
}
