// PASSTHROUGH (task D1; native main.cpp initPassthrough / updatePassthrough and the frame's layers): your room
// around the arena, through XR_FB_passthrough (Quest; Quest Link too when the Meta app allows it). Natively the game
// makes the passthrough and its layer paused once the session exists, starts them while PASSTHROUGH is on (or PEEK is
// held) and pauses them otherwise (the cameras cost power), and submits the room as the first layer with the game's
// image over it, blended by its alpha (premultiplied: glows add light to the room). Any failure turns the option off
// for this run (N/A on the settings page).
// Unity's OpenXR plug-in submits the frame itself, so this OpenXR feature sits on xrEndFrame (as FIELD OF VIEW does):
// while the room runs, the runtime gets a copy of the frame with the passthrough layer first and the projection layers
// marked as blended by alpha; nothing of Unity's own frame is changed. The start, pause and style calls are made there
// too, on the thread that ends the frame, so they never cross a frame being submitted. The game asks for what it wants
// with Want and Style. 64-bit only (Quest, PC); the structs' layouts are below (openxr.h, XR_FB_passthrough).
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace ProjectArena
{
#if UNITY_EDITOR
    [OpenXRFeature(
        UiName = "Project Arena: PASSTHROUGH",
        BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
        Company = "Project Arena",
        Desc = "SETTINGS > PASSTHROUGH: your room around the arena (XR_FB_passthrough), the game's image over it by its alpha.",
        OpenxrExtensionStrings = Extension,
        Version = "1.0.0",
        FeatureId = FeatureId)]
#endif
    public class PassthroughFeature : OpenXRFeature
    {
        public const string FeatureId = "com.projectarena.passthrough";
        public const string Extension = "XR_FB_passthrough";

        // openxr.h (XR_FB_passthrough) and the core flags
        public const int XR_TYPE_PASSTHROUGH_CREATE_INFO_FB = 1000118001, XR_TYPE_PASSTHROUGH_LAYER_CREATE_INFO_FB = 1000118002,
                         XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB = 1000118003, XR_TYPE_PASSTHROUGH_STYLE_FB = 1000118020,
                         XR_TYPE_PASSTHROUGH_BRIGHTNESS_CONTRAST_SATURATION_FB = 1000118023,
                         XR_TYPE_COMPOSITION_LAYER_PROJECTION = 35;
        public const ulong BLEND_TEXTURE_SOURCE_ALPHA_BIT = 0x2, UNPREMULTIPLIED_ALPHA_BIT = 0x4;
        // (64-bit) XrFrameEndInfo { type @0, next @8, displayTime @16, blend @24, layerCount @28, layers @32 } 40 bytes;
        // XrCompositionLayerProjection { type @0, next @8, layerFlags @16, space @24, viewCount @32, views @40 } 48;
        // XrCompositionLayerPassthroughFB { type @0, next @8, flags @16, space @24, layerHandle @32 } 40;
        // XrPassthroughCreateInfoFB { type @0, next @8, flags @16 } 24;
        // XrPassthroughLayerCreateInfoFB { type @0, next @8, passthrough @16, flags @24, purpose @32 } 40;
        // XrPassthroughStyleFB { type @0, next @8, textureOpacityFactor @16, edgeColor rgba @20 } 40;
        // XrPassthroughBrightnessContrastSaturationFB { type @0, next @8, brightness @16, contrast @20, saturation @24 } 32
        public const int FrameEndInfoSize = 40, ProjectionSize = 48, PassthroughLayerSize = 40, StyleSize = 40, BcsSize = 32;
        public const int MaxLayers = 16;

        // ---- what the game sees and asks for (main thread)
        static volatile bool ready, failed, running, want, styleDirty = true;
        static float room = 1f;
        static bool mono, edges;
        public static bool Ready => ready && !failed;      // made, and nothing has failed (otherwise PASSTHROUGH: N/A)
        public static bool Failed => failed;
        public static bool Running => running;              // the room is in the frame now
        public static void Want(bool on) => want = on;
        public static void Style(float roomBrightness, bool grey, bool objectEdges)
        {
            float r = Mathf.Clamp01(roomBrightness);
            if (r == room && grey == mono && objectEdges == edges) return;
            room = r; mono = grey; edges = objectEdges;
            styleDirty = true;
        }

        // ---- OpenXR
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int CreateFn(ulong session, IntPtr info, out ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int HandleFn(ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetStyleFn(ulong layer, IntPtr style);

        static CreateFn createPt, createLayer;
        static HandleFn startPt, pausePt, destroyPt, resumeLayer, pauseLayer, destroyLayer;
        static SetStyleFn setStyle;
        static ulong instance, ptHandle, ptLayer;

        // (the hook on xrEndFrame is XrHooks', shared with FIELD OF VIEW: two managed hooks don't chain)
        protected override IntPtr HookGetInstanceProcAddr(IntPtr func) => XrHooks.Install(func);

        static T Fn<T>(string name) where T : Delegate
        {
            IntPtr f = XrHooks.Resolve(instance, name);
            return f == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(f);
        }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            instance = xrInstance;
            return true;   // (the game runs without it: PASSTHROUGH: N/A)
        }

        protected override void OnInstanceDestroy(ulong xrInstance) { instance = 0; XrHooks.Forget(); }

        // made paused once the session exists (native initPassthrough)
        protected override void OnSessionCreate(ulong xrSession)
        {
            ready = false;
            running = false;
            styleDirty = true;
            if (IntPtr.Size != 8 || !OpenXRRuntime.IsExtensionEnabled(Extension))
            {
                Debug.Log("[Passthrough] not available (" + (IntPtr.Size != 8 ? "32-bit" : "the runtime hasn't " + Extension) + ")");
                return;
            }
            createPt = Fn<CreateFn>("xrCreatePassthroughFB"); startPt = Fn<HandleFn>("xrPassthroughStartFB");
            pausePt = Fn<HandleFn>("xrPassthroughPauseFB"); destroyPt = Fn<HandleFn>("xrDestroyPassthroughFB");
            createLayer = Fn<CreateFn>("xrCreatePassthroughLayerFB"); resumeLayer = Fn<HandleFn>("xrPassthroughLayerResumeFB");
            pauseLayer = Fn<HandleFn>("xrPassthroughLayerPauseFB"); destroyLayer = Fn<HandleFn>("xrDestroyPassthroughLayerFB");
            setStyle = Fn<SetStyleFn>("xrPassthroughLayerSetStyleFB");   // (optional: ROOM BRIGHTNESS, GREY, OBJECT EDGES)
            int r = -7;   // (XR_ERROR_FUNCTION_UNSUPPORTED)
            bool ok = createPt != null && startPt != null && pausePt != null && destroyPt != null && createLayer != null &&
                      resumeLayer != null && pauseLayer != null;
            IntPtr info = Marshal.AllocHGlobal(PassthroughLayerSize);
            try
            {
                if (ok)
                {
                    Zero(info, PassthroughLayerSize);
                    Marshal.WriteInt32(info, 0, XR_TYPE_PASSTHROUGH_CREATE_INFO_FB);   // (flags 0: paused)
                    r = createPt(xrSession, info, out ptHandle);
                    ok = r == 0;
                }
                if (ok)
                {
                    Zero(info, PassthroughLayerSize);
                    Marshal.WriteInt32(info, 0, XR_TYPE_PASSTHROUGH_LAYER_CREATE_INFO_FB);
                    Marshal.WriteInt64(info, 16, (long)ptHandle);
                    Marshal.WriteInt32(info, 32, 0);   // XR_PASSTHROUGH_LAYER_PURPOSE_RECONSTRUCTION_FB (flags 0: paused)
                    r = createLayer(xrSession, info, out ptLayer);
                    ok = r == 0;
                    if (!ok) { destroyPt(ptHandle); ptHandle = 0; }
                }
            }
            finally { Marshal.FreeHGlobal(info); }
            failed = !ok;
            ready = ok;
            Debug.Log($"[Passthrough] {(ok ? "ready" : "not available")} ({r})");
        }

        protected override void OnSessionDestroy(ulong xrSession)
        {
            ready = false;
            running = false;
            if (ptLayer != 0 && destroyLayer != null) destroyLayer(ptLayer);
            if (ptHandle != 0 && destroyPt != null) destroyPt(ptHandle);
            ptLayer = ptHandle = 0;
        }

        // xrEndFrame (XrHooks): start / pause / style, then the frame to send (zero: Unity's own, unchanged)
        internal static IntPtr BeforeEnd(IntPtr info)
        {
            if (ptLayer == 0 || failed) return IntPtr.Zero;
            Update();
            if (!running || info == IntPtr.Zero) return IntPtr.Zero;
            return WithRoom(info, ptLayer);
        }

        // start / pause as wanted, the style when it changed (native updatePassthrough)
        static void Update()
        {
            bool w = want;
            if (w && running && styleDirty && setStyle != null)
            {
                styleDirty = false;
                WriteStyle(styleMem, room, mono, edges);
                int r = setStyle(ptLayer, styleMem);
                Debug.Log($"[Passthrough] style: room {room * 100:F0}% {(mono ? "grey" : "colour")}, edges {(edges ? "on" : "off")} ({r})");
            }
            if (w == running) return;
            int r1, r2;
            if (w) { r1 = startPt(ptHandle); r2 = resumeLayer(ptLayer); }
            else { r2 = pauseLayer(ptLayer); r1 = pausePt(ptHandle); }
            if (w && (r1 != 0 || r2 != 0))
            {
                Debug.LogWarning($"[Passthrough] start failed ({r1}, {r2})");
                failed = true;
                return;
            }
            running = w;
            styleDirty = true;
            Debug.Log("[Passthrough] " + (w ? "running" : "paused"));
        }

        // ---- the two halves the tests can reach (no OpenXR runtime in the editor)

        static readonly IntPtr styleMem = Marshal.AllocHGlobal(StyleSize + BcsSize);
        static readonly IntPtr frameMem = Marshal.AllocHGlobal(FrameEndInfoSize), layerMem = Marshal.AllocHGlobal(PassthroughLayerSize),
                               listMem = Marshal.AllocHGlobal(IntPtr.Size * (MaxLayers + 1)), projMem = Marshal.AllocHGlobal(ProjectionSize * MaxLayers);

        static void Zero(IntPtr p, int n) { for (int i = 0; i < n; i += 4) Marshal.WriteInt32(p, i, 0); }
        static void W(IntPtr p, int off, float v) => Marshal.WriteInt32(p, off, BitConverter.SingleToInt32Bits(v));

        // the style (native: textureOpacityFactor = ROOM BRIGHTNESS; edges cyan or none; saturation 0 for a grey room),
        // the brightness / contrast / saturation struct right after it, chained
        public static void WriteStyle(IntPtr p, float roomBrightness, bool grey, bool objectEdges)
        {
            Zero(p, StyleSize + BcsSize);
            IntPtr bcs = p + StyleSize;
            Marshal.WriteInt32(p, 0, XR_TYPE_PASSTHROUGH_STYLE_FB);
            Marshal.WriteIntPtr(p, 8, bcs);
            W(p, 16, Mathf.Clamp01(roomBrightness));
            if (objectEdges) { W(p, 20, 0.25f); W(p, 24, 0.85f); W(p, 28, 1.0f); W(p, 32, 1.0f); }
            Marshal.WriteInt32(bcs, 0, XR_TYPE_PASSTHROUGH_BRIGHTNESS_CONTRAST_SATURATION_FB);
            W(bcs, 16, 0f); W(bcs, 20, 1f); W(bcs, 24, grey ? 0f : 1f);
        }

        // a copy of the frame for the runtime: the room's layer first, then Unity's layers (its projection layers copied,
        // marked blended by alpha, premultiplied); null when there are too many layers to add one
        public static IntPtr WithRoom(IntPtr info, ulong layerHandle)
        {
            int n = Marshal.ReadInt32(info, 28);
            IntPtr layers = Marshal.ReadIntPtr(info, 32);
            if (n < 0 || n >= MaxLayers || (n > 0 && layers == IntPtr.Zero)) return IntPtr.Zero;
            Zero(layerMem, PassthroughLayerSize);
            Marshal.WriteInt32(layerMem, 0, XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB);
            Marshal.WriteInt64(layerMem, 16, (long)BLEND_TEXTURE_SOURCE_ALPHA_BIT);
            Marshal.WriteInt64(layerMem, 32, (long)layerHandle);
            Marshal.WriteIntPtr(listMem, 0, layerMem);
            for (int i = 0; i < n; ++i)
            {
                IntPtr h = Marshal.ReadIntPtr(layers, i * IntPtr.Size);
                if (h != IntPtr.Zero && Marshal.ReadInt32(h) == XR_TYPE_COMPOSITION_LAYER_PROJECTION)
                {
                    IntPtr c = projMem + i * ProjectionSize;
                    for (int b = 0; b < ProjectionSize; b += 8) Marshal.WriteInt64(c, b, Marshal.ReadInt64(h, b));
                    ulong flags = (ulong)Marshal.ReadInt64(c, 16);
                    Marshal.WriteInt64(c, 16, (long)((flags | BLEND_TEXTURE_SOURCE_ALPHA_BIT) & ~UNPREMULTIPLIED_ALPHA_BIT));
                    h = c;
                }
                Marshal.WriteIntPtr(listMem, (i + 1) * IntPtr.Size, h);
            }
            for (int b = 0; b < FrameEndInfoSize; b += 8) Marshal.WriteInt64(frameMem, b, Marshal.ReadInt64(info, b));
            Marshal.WriteInt32(frameMem, 28, n + 1);
            Marshal.WriteIntPtr(frameMem, 32, listMem);
            return frameMem;
        }

        // (tests) as if the session made the room, or not
        public static void SetForTests(bool isReady, bool isRunning = false)
        {
            ready = isReady; failed = false; running = isRunning; want = isRunning;
        }
    }
}
