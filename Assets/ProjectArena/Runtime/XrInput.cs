using ProjectArena.Sim;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using PlayerInput = ProjectArena.Sim.PlayerInput;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The local player's input, read as the native main.cpp readInput reads it, in native (right-handed) court space:
    // - trackToWorld places the tracking space on the court; recentering (at the start, and on B / the right stick's click)
    //   puts the head at the player's start, looking down the court (native recenter / viewTarget). Unity shows it by
    //   moving the XR Origin.
    // - the floor guard: a headset that loses its floor (the head reads impossibly low or high) is lifted back.
    // - the system moving the tracking origin (native XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING): a small shift
    //   (tracking settling) is compensated so the court stays put in the room, floor height followed; a large one
    //   (holding the Meta button: system recenter) re-places the player on their spot, facing the court. Unity's event
    //   (XRInputSubsystem.trackingOriginUpdated) gives no shift, so it is measured from the head's jump at that frame;
    //   a jump no head can make in one frame counts as the event too.
    // - the hand pose: the grip position with the AIM orientation (native toolFrame 1: the original's gauntlet sits on
    //   the legacy Oculus controller frame), then the owner's TUNING export (pitch / yaw / roll, offsets).
    // - angular velocity: Unity's OpenXR Touch profile has none, so it comes from the change of the hand's rotation
    //   between frames, averaged over the last 3 frames (the native game reads the runtime's; this is the one difference).
    public class XrInput
    {
        // the owner's TUNING export, 2026-10-19 (main.cpp tunePitchDeg ... tuneRollDeg)
        public float tunePitchDeg = 27f, tuneFwd = 0.004f, tuneUp = -0.014f;
        public float tuneX = -0.006f, tuneYawDeg = -16f, tuneRollDeg = -1f;
        public int ctlTilt, ctlTwist, ctlTurn;   // CONTROLLER ANGLE (AppSettings): degrees on top of the TUNING angle

        public Pose trackToWorld = Pose.Identity;
        public bool recentered;
        public bool justRecentered;   // (this frame: the view was recentered; the builder places itself again, native 7ed11aa)
        public float floorFix;      // the lift added while the headset has lost its floor
        float standH = 1.6f;        // the usual head height above the floor
        float lowSince, okSince;    // (seconds, 0 = not timing)
        bool prevRecenter, prevNames;
        public bool namesClicked;   // this frame: the left stick's click (PLAYERS' NAMES on / off, native 0.3.19)
        Pose prevHeadTrack;
        bool hasPrevHead;
        volatile bool originChanged;
        float originChangedAt = -1;   // the event can come a frame before the head pose jumps: watch for 0.25 s
        float backAt = -1;            // when the head was last tracked again after a loss

        readonly InputAction headPos, headRot, headTracked, headState, menuButton;
        readonly InputAction[] gripPos = new InputAction[2], aimRot = new InputAction[2], aimPos = new InputAction[2],
            handTracked = new InputAction[2], trigger = new InputAction[2], squeeze = new InputAction[2];

        // the pointer: each hand's aim pose on the court (native a.aim / aimValid), the menu button (left), and the
        // triggers as read, before the menu takes them from the game (native rawTrigger)
        public readonly Pose[] aim = { Pose.Identity, Pose.Identity };
        public readonly bool[] aimValid = new bool[2];
        public bool menuPressed;
        // the spectator's controls (native spectatorCamera): the sticks, A, X and Y apart; the head in tracking space
        public Vector2 stickL, stickR;
        public bool btnA, btnX, btnY;
        // BUTTONS (Controls.cs, TASKS.md E20 / E21, native controls.h): each button this frame, the grips as push to talk
        // reads them (Index: the squeeze's force, so resting a hand on it doesn't talk), the controllers in your hands (the
        // OpenXR interaction profile's device), and the map as SETTINGS has it (MatchView sets it each frame). Each job
        // reads its own buttons: RECENTER, NAMES, READY (buttonA), PEEK (peekHeld), TALK (talkValue)
        public readonly bool[] btnDown = new bool[Controls.S_COUNT];
        public readonly float[] gripVal = new float[2];
        public int ctlKind;
        public Controls.Map buttons = Controls.Standard();
        public bool peekHeld;
        public float talkValue;
        public Pose headTrack = Pose.Identity;
        public readonly Vec3[] gripTrack = new Vec3[2];   // each hand in tracking space (PASSTHROUGH: ROOM AT EDGES)
        readonly InputAction stickLeft, stickRight;
        public readonly float[] rawTrigger = new float[2];
        readonly Quat[] prevQ = { Quat.Identity, Quat.Identity };
        readonly Vec3[,] angHist = new Vec3[2, 3];
        readonly bool[] hasPrev = new bool[2];
        int angN;

        public XrInput()
        {
            headPos = Make("<XRHMD>/centerEyePosition");
            headRot = Make("<XRHMD>/centerEyeRotation");
            headTracked = Make("<XRHMD>/isTracked");
            headState = Make("<XRHMD>/trackingState");
            menuButton = Make("<XRController>{LeftHand}/menu", "<XRController>{LeftHand}/{MenuButton}");             // left menu
            stickLeft = Make("<XRController>{LeftHand}/thumbstick");
            stickRight = Make("<XRController>{RightHand}/thumbstick");
            string[] side = { "{LeftHand}", "{RightHand}" };
            for (int h = 0; h < 2; ++h)
            {
                gripPos[h] = Make($"<XRController>{side[h]}/devicePosition");
                aimRot[h] = Make($"<XRController>{side[h]}/pointerRotation");
                aimPos[h] = Make($"<XRController>{side[h]}/pointerPosition");
                handTracked[h] = Make($"<XRController>{side[h]}/isTracked");
                trigger[h] = Make($"<XRController>{side[h]}/trigger");
                squeeze[h] = Make($"<XRController>{side[h]}/grip");
            }
        }

        // BUTTONS: the controllers in your hands (native: the interaction profile's name) from the device's layout
        public static int KindOf(string layout)
        {
            if (string.IsNullOrEmpty(layout)) return Controls.K_UNKNOWN;
            if (layout.Contains("Vive")) return Controls.K_VIVE;
            if (layout.Contains("Index")) return Controls.K_INDEX;
            if (layout.Contains("WMR") || layout.Contains("MotionController")) return Controls.K_WMR;
            if (layout.Contains("Touch")) return Controls.K_TOUCH;   // (Quest Touch, Touch Plus, Touch Pro)
            return Controls.K_UNKNOWN;
        }

        // each button's control on these controllers (native's bindings per interaction profile): the hand, the name
        static (int hand, string name) SlotControl(int kind, int s)
        {
            bool pads = kind == Controls.K_VIVE || kind == Controls.K_WMR;
            switch (s)
            {
                case Controls.S_LGRIP: return (0, kind == Controls.K_INDEX ? "gripForce" : "grip");
                case Controls.S_RGRIP: return (1, kind == Controls.K_INDEX ? "gripForce" : "grip");
                case Controls.S_A: return (1, kind == Controls.K_VIVE ? "trackpadClicked" : kind == Controls.K_WMR ? "touchpadClicked" : "primaryButton");
                case Controls.S_B: return (1, pads ? "menu" : "secondaryButton");
                case Controls.S_X: return (0, kind == Controls.K_VIVE ? "trackpadClicked" : kind == Controls.K_WMR ? "touchpadClicked" : "primaryButton");
                case Controls.S_Y: return (0, pads ? null : kind == Controls.K_INDEX ? "trackpadForce" : "secondaryButton");
                default: return (s == Controls.S_LSTICK ? 0 : 1, kind == Controls.K_VIVE ? null : kind == Controls.K_WMR ? "joystickClicked" : "thumbstickClicked");
            }
        }

        readonly UnityEngine.InputSystem.InputDevice[] slotDev = new UnityEngine.InputSystem.InputDevice[2];
        readonly UnityEngine.InputSystem.Controls.AxisControl[] slotCtl = new UnityEngine.InputSystem.Controls.AxisControl[Controls.S_COUNT];
        void ReadButtons()
        {
            var l = UnityEngine.InputSystem.XR.XRController.leftHand;
            var r = UnityEngine.InputSystem.XR.XRController.rightHand;
            if (l != slotDev[0] || r != slotDev[1])
            {   // (new controllers: their kind, and each button's control on them)
                slotDev[0] = l; slotDev[1] = r;
                int kind = KindOf(l != null ? l.layout : r != null ? r.layout : null);
                for (int s = 0; s < Controls.S_COUNT; ++s)
                {
                    var (hand, name) = SlotControl(kind, s);
                    UnityEngine.InputSystem.InputDevice d = slotDev[hand];
                    slotCtl[s] = d != null && name != null ? d.TryGetChildControl(name) as UnityEngine.InputSystem.Controls.AxisControl : null;
                }
                if (kind != ctlKind || l != null || r != null)
                    Debug.Log($"[Input] controllers: {(l != null ? l.layout : "none")} / {(r != null ? r.layout : "none")} (push to talk: {Controls.JobButtons(Controls.Effective(buttons, kind), kind, Controls.J_TALK, false)})");
                ctlKind = kind;
            }
            for (int s = 0; s < Controls.S_COUNT; ++s)
            {
                float v = slotCtl[s] != null ? slotCtl[s].ReadValue() : 0f;
                if (s == Controls.S_LGRIP || s == Controls.S_RGRIP) { gripVal[s] = v; btnDown[s] = v > 0.6f; }
                else btnDown[s] = v > 0.5f;
            }
        }

        // Unity's XR input subsystems may start after the first frame: subscribe to the ones not seen yet
        readonly System.Collections.Generic.HashSet<XRInputSubsystem> watched = new System.Collections.Generic.HashSet<XRInputSubsystem>();
        void WatchOrigins()
        {
            var inputs = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var s in inputs)
                if (watched.Add(s)) s.trackingOriginUpdated += _ => originChanged = true;
        }

        // The tracking origin moved under the head: the pose of the new origin in the old space, estimated as the head's
        // jump (the head itself barely moves in one frame). Native: small = compensate across the room, follow the
        // floor; large = re-place the player. Small is under kSettleMove across and kSettleTurn (TASKS.md E30, native
        // 1497432: tracking settling, at most 0.9 cm across in every log the owner's headset has written; a Meta button
        // recenter made where you already nearly faced the headset's own forward moved it 20 cm and turned it under 10
        // degrees: under 25 cm and 10 degrees until 2026-11-17, it was taken for settling and the court stayed off to the side)
        public const float kSettleMove = 0.05f, kSettleTurn = 3f;   // (m, degrees)
        public static bool OriginShift(Pose prevHead, Pose head, out Pose shift, out float moved, out float turned)
        {
            shift = prevHead * Inverse(head);
            moved = Mathf.Sqrt(shift.p.x * shift.p.x + shift.p.z * shift.p.z);
            turned = 2f * Mathf.Acos(Mathf.Min(1f, Mathf.Abs(shift.q.w))) * 57.2958f;
            return moved < kSettleMove && turned < kSettleTurn;
        }

        public static Pose Inverse(Pose p)
        {
            Quat c = p.q.Conj();
            return new Pose(c, c.Rotate(-p.p));
        }

        static InputAction Make(params string[] bindings)
        {
            var a = new InputAction(type: InputActionType.PassThrough);
            foreach (var b in bindings) a.AddBinding(b);
            a.Enable();
            return a;
        }

        // Unity's left-handed tracking space -> native right-handed (z negated; rotations: axis (x, y, -z), angle reversed)
        public static Vec3 ToNative(Vector3 v) => new Vec3(v.x, v.y, -v.z);
        public static Quat ToNative(Quaternion q) => new Quat(-q.x, -q.y, q.z, q.w);

        // Native recenter: the head's heading onto the target direction, the head's floor point onto the target point.
        public static Pose Recenter(Pose headTrack, Vec3 pos, Vec3 fwd, float floorFix)
        {
            Vec3 f = headTrack.q.Rotate(new Vec3(0, 0, -1));
            float yaw = Mathf.Atan2(-f.x, -f.z);   // heading of the head in tracking space (0 = -Z)
            Quat toX = Quat.AxisAngle(new Vec3(0, 1, 0), Mathf.Atan2(-fwd.x, -fwd.z)) * Quat.AxisAngle(new Vec3(0, 1, 0), -yaw);
            var headXZ = new Vec3(headTrack.p.x, 0, headTrack.p.z);
            var t = new Pose(toX, pos - toX.Rotate(headXZ));
            t.p.y += floorFix;
            return t;
        }

        // The hand's tool frame: grip position, aim orientation, then the TUNING rotation and offset (native readInput).
        public Pose ToolFrame(Vec3 gripP, Quat aimQ, int h)
        {
            // (CONTROLLER ANGLE: your own tilt, turn and twist on top of the standard angle; mirrored on the left hand)
            Quat tq = aimQ * M.GripEuler(new Vec3(tunePitchDeg + ctlTilt, tuneYawDeg + ctlTurn, tuneRollDeg + ctlTwist), h == 0);
            return new Pose(tq, gripP + tq.Rotate(new Vec3((h == 0 ? -1f : 1f) * tuneX, tuneUp, -tuneFwd)));
        }

        // Angular velocity (rad/s, world) that turns q0 into q1 in dt.
        public static Vec3 AngularVelocity(Quat q0, Quat q1, float dt)
        {
            Quat d = q1 * q0.Conj();
            if (d.w < 0) d = new Quat(-d.x, -d.y, -d.z, -d.w);
            float s = Mathf.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
            if (s < 1e-7f || dt <= 0) return default;
            float angle = 2f * Mathf.Atan2(s, d.w);
            return new Vec3(d.x, d.y, d.z) * (angle / (s * dt));
        }

        // the head is tracked as natively (its position and orientation valid, XR_SPACE_LOCATION_*_VALID_BIT): the HMD's
        // trackingState has Position and Rotation, or its isTracked is on, on any HMD the bindings found (a PassThrough
        // action reads only the control that changed last). (a Quest 3, 2026-11-14: the head read as not tracked for a minute
        // while it was followed: START PRACTICE left the player on the balcony and the match waited for them.) Each change
        // is logged with both readings
        int headBits = -1;
        float headLogAt = -10f;
        bool HeadOk()
        {
            const int need = (int)(InputTrackingState.Position | InputTrackingState.Rotation);
            bool state = false, flag = false;
            var sc = headState.controls;
            for (int i = 0; i < sc.Count; ++i) if (sc[i] is InputControl<int> ic && (ic.ReadValue() & need) == need) state = true;
            var tc = headTracked.controls;
            for (int i = 0; i < tc.Count; ++i) if (tc[i] is InputControl<float> fc && fc.ReadValue() > 0.5f) flag = true;
            int bits = (state ? 2 : 0) | (flag ? 1 : 0);
            float t = Time.realtimeSinceStartup;
            if (bits != headBits && t - headLogAt >= 0.5f)
            {
                headBits = bits;
                headLogAt = t;
                Debug.Log($"[Input] head: position and rotation {(state ? "valid" : "not valid")}, isTracked {(flag ? "on" : "off")} ({sc.Count} / {tc.Count} bound)");
            }
            return state || flag;
        }

        // Reads this frame's input. start / fwd: where recentering puts the head (the player's end, facing the court).
        public PlayerInput Read(float dt, float now, Vec3 start, Vec3 fwd)
        {
            var @in = new PlayerInput();
            bool headOk = HeadOk();
            var headTrack = new Pose(ToNative(headRot.ReadValue<Quaternion>()), ToNative(headPos.ReadValue<Vector3>()));
            ReadButtons();
            Controls.Map bm = Controls.Effective(buttons, ctlKind);   // (BUTTONS: the map as it works on these controllers)
            bool rc = Controls.Held(bm, Controls.J_RECENTER, ctlKind, btnDown);   // (B and the right stick's click as standard)
            bool nc = Controls.Held(bm, Controls.J_NAMES, ctlKind, btnDown);      // (PLAYERS' NAMES: the left stick's click as standard)
            peekHeld = Controls.Held(bm, Controls.J_PEEK, ctlKind, btnDown);      // (PASSTHROUGH > PEEK: Y as standard)
            talkValue = Controls.TalkValue(bm, ctlKind, btnDown, gripVal);        // (push to talk: the left grip as standard)
            namesClicked = nc && !prevNames;
            prevNames = nc;
            WatchOrigins();
            if (headOk && !hasPrevHead) backAt = now;
            // KEEPING THE CENTRE (native 0.3.19, issue #16): native now applies an origin change when it takes effect, not
            // when it's announced; measuring it from the head's jump (below) already does that. But an origin that moved
            // while the head wasn't tracked (the headset off and on) leaves no jump to measure: the player is re-placed on
            // their spot, facing the court, as after a system recenter
            if (headOk && originChanged && recentered && now >= backAt && now - backAt < 0.5f)   // (the game's clock: a new match restarts it)
            {
                originChanged = false;
                originChangedAt = -1;
                Debug.Log("[Input] tracking origin updated while the head wasn't tracked: re-placing the player");
                recentered = false;
            }
            if (headOk && hasPrevHead)
            {
                // the system moved the tracking origin (or the head jumped further than a head can in one frame)
                bool small = OriginShift(prevHeadTrack, headTrack, out Pose shift, out float moved, out float turned);
                bool impossible = moved > 0.5f || turned > 45f;
                if (originChanged) { originChangedAt = now; originChanged = false; Debug.Log("[Input] tracking origin updated by the system"); }
                bool pending = originChangedAt >= 0 && now - originChangedAt < 0.25f;
                bool jumped = moved > 0.03f || turned > 3f;   // more than a head moves in one frame
                if (((pending && jumped) || impossible) && recentered)
                {
                    originChangedAt = -1;
                    if (small)
                    {   // tracking settling: keep the court fixed across the room, but follow a new floor height
                        var dxz = shift;
                        dxz.p.y = 0;
                        trackToWorld = trackToWorld * dxz;
                        Debug.Log($"[Input] reference space shifted ({shift.p.x:F3}, {shift.p.y:F3}, {shift.p.z:F3}, turned {turned:F1} deg): compensated across, floor followed");
                    }
                    else
                    {   // the player held the Meta button (system recenter): back on their spot, facing in
                        Debug.Log($"[Input] system recenter (moved {moved:F2} m, turned {turned:F0} deg): re-placing the player");
                        recentered = false;
                    }
                }
            }
            prevHeadTrack = headTrack;
            hasPrevHead = headOk;

            if (headOk)
            {   // floor guard (native App::floorFix)
                float hy = headTrack.p.y;
                if (hy > 1.0f && hy < 2.2f && floorFix == 0f) standH += (hy - standH) * 0.002f;   // your usual height
                if (hy < 0.45f || hy > 2.4f)
                {   // impossibly low or high: the headset's floor is wrong
                    if (lowSince == 0) lowSince = now;
                    if (floorFix == 0f && now - lowSince > 1.5f)
                    {
                        floorFix = standH - hy;
                        trackToWorld.p.y += floorFix;
                        Debug.LogWarning($"[Input] floor lost: the head reads {hy:F2} m above the floor; view moved {floorFix:+0.00;-0.00} m");
                    }
                }
                else lowSince = 0;
                if (floorFix != 0f && hy > 1.0f && hy < 2.2f)
                {   // the headset found its floor again
                    if (okSince == 0) okSince = now;
                    if (now - okSince > 1.0f)
                    {
                        trackToWorld.p.y -= floorFix;
                        Debug.Log($"[Input] floor back: the head reads {hy:F2} m; the lift of {floorFix:F2} m removed");
                        floorFix = 0f;
                        okSince = 0;
                    }
                }
                else okSince = 0;
            }
            justRecentered = false;
            if (headOk && (!recentered || (rc && !prevRecenter)))
            {
                Debug.Log("[Input] recenter: " + (!recentered ? "initial" : "button (B / right stick click)"));
                trackToWorld = Recenter(headTrack, start, fwd, floorFix);
                recentered = true;
                justRecentered = true;
            }
            prevRecenter = rc;

            @in.headTracked = headOk;
            @in.head = trackToWorld * headTrack;
            for (int h = 0; h < 2; ++h)
            {
                @in.hand[h].tracked = handTracked[h].ReadValue<float>() > 0.5f;
                Quat aimTrack = ToNative(aimRot[h].ReadValue<Quaternion>());
                gripTrack[h] = ToNative(gripPos[h].ReadValue<Vector3>());
                Pose tool = ToolFrame(gripTrack[h], aimTrack, h);
                @in.hand[h].grip = trackToWorld * tool;
                // angular velocity from the rotation change (tracking space, then turned into the court)
                Vec3 w = hasPrev[h] ? AngularVelocity(prevQ[h], aimTrack, dt) : default;
                prevQ[h] = aimTrack;
                hasPrev[h] = @in.hand[h].tracked;
                angHist[h, angN % 3] = w;
                Vec3 avg = (angHist[h, 0] + angHist[h, 1] + angHist[h, 2]) * (1f / 3f);
                @in.hand[h].angVel = trackToWorld.q.Rotate(avg);
                @in.hand[h].trigger = trigger[h].ReadValue<float>();
                rawTrigger[h] = @in.hand[h].trigger;
                aimValid[h] = @in.hand[h].tracked;
                aim[h] = trackToWorld * new Pose(aimTrack, ToNative(aimPos[h].ReadValue<Vector3>()));
                @in.hand[h].squeeze = squeeze[h].ReadValue<float>();
            }
            angN++;
            @in.buttonA = Controls.Held(bm, Controls.J_READY, ctlKind, btnDown);   // READY (A and X as standard)
            menuPressed = menuButton.ReadValue<float>() > 0.5f;
            stickL = stickLeft.ReadValue<Vector2>();
            stickR = stickRight.ReadValue<Vector2>();
            // (the spectator camera's A, X and Y: a button that talks doesn't work the camera; AUTO is on the board too)
            btnX = btnDown[Controls.S_X] && bm.job[Controls.S_X] != Controls.J_TALK;
            btnA = btnDown[Controls.S_A] && !btnX && bm.job[Controls.S_A] != Controls.J_TALK;
            btnY = btnDown[Controls.S_Y] && bm.job[Controls.S_Y] != Controls.J_TALK;
            this.headTrack = headTrack;
            return @in;
        }

        // Shows the tracking space where trackToWorld puts it (the XR Origin carries the camera and controllers).
        public void ApplyTo(XROrigin origin)
        {
            origin.transform.SetPositionAndRotation(NativeSpace.ToUnity(trackToWorld.p), NativeSpace.ToUnity(trackToWorld.q));
        }
    }
}
