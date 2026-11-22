// The one hook the template's OpenXR features share on Unity's OpenXR calls (task C6 FIELD OF VIEW, D1 PASSTHROUGH).
// Unity chains every enabled feature's HookGetInstanceProcAddr: each gets the previous one's function and returns its
// own. Two managed hooks in that chain don't mix: the second is handed the first one's managed function pointer, and
// turning that back into a delegate gives the first one's delegate type (Mono: InvalidCastException; found with both
// features on in a PC build, 2026-11-09). So the hook is installed once, by whichever feature comes first, and the next
// one passes the chain on as it is; xrLocateViews and xrEndFrame then do both features' work in one place:
//   xrLocateViews: FIELD OF VIEW moves the eyes toward their middle (for drawing);
//   xrEndFrame:    FIELD OF VIEW puts the real eye poses back, then PASSTHROUGH starts / pauses / styles your room and
//                  hands the runtime a copy of the frame with the room's layer first.
using System;
using System.Runtime.InteropServices;
using AOT;

namespace ProjectArena
{
    public static class XrHooks
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate int GetProcAddrFn(ulong instance, IntPtr name, IntPtr function);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate int LocateViewsFn(ulong session, IntPtr info, IntPtr state, uint capacity, IntPtr count, IntPtr views);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate int EndFrameFn(ulong session, IntPtr info);

        static GetProcAddrFn origGpa;
        static LocateViewsFn origLocate;
        static EndFrameFn origEnd;
        static readonly GetProcAddrFn gpaHook = GetProcAddr;   // (kept alive for as long as the runtime may call them)
        static readonly LocateViewsFn locateHook = LocateViews;
        static readonly EndFrameFn endHook = EndFrame;
        static IntPtr mine;

        public static bool Installed => origGpa != null;

        // a feature's HookGetInstanceProcAddr: the first one installs the hook; any later one (or a second pass with the
        // chain already holding it) passes the chain on unchanged
        public static IntPtr Install(IntPtr func)
        {
            if (IntPtr.Size != 8 || func == IntPtr.Zero) return func;
            if (mine == IntPtr.Zero) mine = Marshal.GetFunctionPointerForDelegate(gpaHook);
            if (func == mine) return func;                           // (ours, from the feature before: pass it on)
            if (origGpa != null && func != wrapped) return func;     // (another feature's in between, this same chain)
            wrapped = func;                                          // (the first of ours in a chain: install, or again
            origGpa = Marshal.GetDelegateForFunctionPointer<GetProcAddrFn>(func);   // on a new start of OpenXR)
            return mine;
        }
        static IntPtr wrapped;

        // the instance is gone: the next start installs afresh (the features' OnInstanceDestroy)
        public static void Forget() { origGpa = null; origLocate = null; origEnd = null; wrapped = IntPtr.Zero; }

        // the runtime's own function (the features' extension functions are looked up through the chain below us)
        public static IntPtr Resolve(ulong instance, string name)
        {
            if (origGpa == null || instance == 0) return IntPtr.Zero;
            IntPtr name8 = Marshal.StringToHGlobalAnsi(name), slot = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(slot, IntPtr.Zero);
                return origGpa(instance, name8, slot) == 0 ? Marshal.ReadIntPtr(slot) : IntPtr.Zero;
            }
            finally { Marshal.FreeHGlobal(name8); Marshal.FreeHGlobal(slot); }
        }

        [MonoPInvokeCallback(typeof(GetProcAddrFn))]
        static int GetProcAddr(ulong instance, IntPtr name, IntPtr function)
        {
            int r = origGpa(instance, name, function);
            if (r != 0 || function == IntPtr.Zero || name == IntPtr.Zero) return r;
            IntPtr f = Marshal.ReadIntPtr(function);
            if (f == IntPtr.Zero) return r;
            string n = Marshal.PtrToStringAnsi(name);
            if (n == "xrLocateViews")
            {
                origLocate = Marshal.GetDelegateForFunctionPointer<LocateViewsFn>(f);
                Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(locateHook));
            }
            else if (n == "xrEndFrame")
            {
                origEnd = Marshal.GetDelegateForFunctionPointer<EndFrameFn>(f);
                Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(endHook));
            }
            return r;
        }

        [MonoPInvokeCallback(typeof(LocateViewsFn))]
        static int LocateViews(ulong session, IntPtr info, IntPtr state, uint capacity, IntPtr count, IntPtr views)
        {
            int r = origLocate(session, info, state, capacity, count, views);
            if (r == 0) RoomFeelFeature.AfterLocate(capacity, count, views);
            return r;
        }

        [MonoPInvokeCallback(typeof(EndFrameFn))]
        static int EndFrame(ulong session, IntPtr info)
        {
            RoomFeelFeature.BeforeEnd(info);
            IntPtr send = PassthroughFeature.BeforeEnd(info);
            return origEnd(session, send != IntPtr.Zero ? send : info);
        }

        // (tests: the chain as Unity builds it, with a stand-in for the runtime's function)
        public static IntPtr HookPointer => mine;
    }
}
