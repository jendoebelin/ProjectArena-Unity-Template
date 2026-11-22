// FIELD OF VIEW (task C6; native roomFeel, owner 2026-10-29 / 2026-10-30): how big the arena feels, -5..+5 in 10 %
// steps. A headset's lenses fix its real field of view; this draws the eyes closer together (the room feels bigger) or
// further apart (smaller): eye separation x 1 / (1 + 10 % a step). Picture only: hands, reach and the court stay true,
// and the headset still gets the real eye poses.
// Natively the game draws each eye from a moved eye pose and submits the real ones. Unity's OpenXR plug-in renders from
// the eye poses xrLocateViews returns and submits those same poses in xrEndFrame, so this OpenXR feature sits on both
// calls (the plug-in's own hook, HookGetInstanceProcAddr): xrLocateViews' eyes are moved toward their middle for drawing,
// and xrEndFrame puts the real poses back into the projection layer's views before they reach the runtime. Nothing
// changes while the setting is NORMAL (0). 64-bit only (Quest, PC); the OpenXR structs' layouts are below.
using System;
using System.Runtime.InteropServices;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace ProjectArena
{
#if UNITY_EDITOR
    [OpenXRFeature(
        UiName = "Project Arena: FIELD OF VIEW",
        BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
        Company = "Project Arena",
        Desc = "SETTINGS > GRAPHICS AND VIEW > FIELD OF VIEW: draws the eyes closer together or further apart (the headset gets the real eye poses).",
        OpenxrExtensionStrings = "",
        Version = "1.0.0",
        FeatureId = FeatureId)]
#endif
    public class RoomFeelFeature : OpenXRFeature
    {
        public const string FeatureId = "com.projectarena.roomfeel";

        static volatile int step;   // -5..+5 (the game sets it from SETTINGS; read on the render thread)
        public static int Step { get => step; set => step = Math.Max(-5, Math.Min(5, value)); }
        public static float Factor(int k) => 1f / (1f + 0.10f * k);   // (native: eye separation x 1 / (1 + 10 % a step))

        // OpenXR (64-bit): XrView { type @0, next @8, pose @16 (orientation xyzw @16, position xyz @32), fov @44 } 64 bytes;
        // XrFrameEndInfo { type @0, next @8, displayTime @16, blend @24, layerCount @28, layers @32 };
        // XrCompositionLayerProjection { type @0, next @8, layerFlags @16, space @24, viewCount @32, views @40 };
        // XrCompositionLayerProjectionView { type @0, next @8, pose @16 (position @32), fov @44, subImage @64 } 96 bytes
        public const int ViewStride = 64, ProjViewStride = 96, PosOffset = 32;
        public const int XR_TYPE_COMPOSITION_LAYER_PROJECTION = 35;

        // (the hook on xrLocateViews and xrEndFrame is XrHooks', shared with PASSTHROUGH: two managed hooks don't chain)
        protected override IntPtr HookGetInstanceProcAddr(IntPtr func) => XrHooks.Install(func);
        protected override void OnInstanceDestroy(ulong xrInstance) => XrHooks.Forget();

        // xrLocateViews' views, for drawing: the eyes toward their middle (nothing while NORMAL)
        internal static void AfterLocate(uint capacity, IntPtr count, IntPtr views)
        {
            int k = step;
            if (k == 0 || views == IntPtr.Zero || count == IntPtr.Zero) return;
            int n = Marshal.ReadInt32(count);
            if (n >= 2 && n <= capacity) MoveEyes(views, n, k);
        }

        // xrEndFrame: the real eye poses back in the projection layers
        internal static void BeforeEnd(IntPtr info)
        {
            if (info != IntPtr.Zero && pairs > 0) RestorePoses(info);
        }

        // ---- the two halves, public for the tests (no OpenXR runtime in the editor)

        // what was moved: (moved position, real position) pairs, the last few calls' (the plug-in may locate twice a frame)
        static readonly float[] moved = new float[3 * 16], real = new float[3 * 16];
        static int pairs, next;
        static readonly object gate = new object();

        static float F(IntPtr p, int off) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, off));
        static void W(IntPtr p, int off, float v) => Marshal.WriteInt32(p, off, BitConverter.SingleToInt32Bits(v));

        // the views' eyes toward their middle (the first two views' middle), by Factor(k)
        public static void MoveEyes(IntPtr views, int n, int k)
        {
            float s = Factor(k);
            float mx = 0.5f * (F(views, PosOffset) + F(views, ViewStride + PosOffset));
            float my = 0.5f * (F(views, PosOffset + 4) + F(views, ViewStride + PosOffset + 4));
            float mz = 0.5f * (F(views, PosOffset + 8) + F(views, ViewStride + PosOffset + 8));
            lock (gate)
            {
                for (int i = 0; i < n; ++i)
                {
                    int o = i * ViewStride + PosOffset;
                    float x = F(views, o), y = F(views, o + 4), z = F(views, o + 8);
                    float nx = mx + (x - mx) * s, ny = my + (y - my) * s, nz = mz + (z - mz) * s;
                    W(views, o, nx); W(views, o + 4, ny); W(views, o + 8, nz);
                    int j = next * 3;
                    moved[j] = nx; moved[j + 1] = ny; moved[j + 2] = nz;
                    real[j] = x; real[j + 1] = y; real[j + 2] = z;
                    next = (next + 1) % 16;
                    pairs = Math.Min(16, pairs + 1);
                }
            }
        }

        // the projection layers' views back at the real eye positions (each one drawn from a moved pose); returns how many
        public static int RestorePoses(IntPtr frameEndInfo)
        {
            int done = 0;
            uint layerCount = (uint)Marshal.ReadInt32(frameEndInfo, 28);
            IntPtr layers = Marshal.ReadIntPtr(frameEndInfo, 32);
            if (layers == IntPtr.Zero) return 0;
            lock (gate)
            {
                for (int l = 0; l < layerCount; ++l)
                {
                    IntPtr hdr = Marshal.ReadIntPtr(layers, l * IntPtr.Size);
                    if (hdr == IntPtr.Zero || Marshal.ReadInt32(hdr) != XR_TYPE_COMPOSITION_LAYER_PROJECTION) continue;
                    uint vc = (uint)Marshal.ReadInt32(hdr, 32);
                    IntPtr vs = Marshal.ReadIntPtr(hdr, 40);
                    if (vs == IntPtr.Zero) continue;
                    for (int v = 0; v < vc; ++v)
                    {
                        int o = v * ProjViewStride + PosOffset;
                        float x = F(vs, o), y = F(vs, o + 4), z = F(vs, o + 8);
                        for (int q = 0; q < pairs; ++q)
                        {
                            int j = q * 3;
                            if (moved[j] != x || moved[j + 1] != y || moved[j + 2] != z) continue;
                            W(vs, o, real[j]); W(vs, o + 4, real[j + 1]); W(vs, o + 8, real[j + 2]);
                            ++done;
                            break;
                        }
                    }
                }
            }
            return done;
        }

        public static void ForgetForTests() { lock (gate) { pairs = 0; next = 0; } }
    }
}
