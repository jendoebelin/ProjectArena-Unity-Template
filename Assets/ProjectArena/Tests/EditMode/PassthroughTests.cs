// PASSTHROUGH (task D1): the OpenXR half (the frame the runtime gets: your room's layer first, Unity's projection
// layers blended by their alpha, Unity's own frame untouched; the room's style), on OpenXR's own struct layouts
// (64-bit; no OpenXR runtime in the editor), and the game's half: the modes, the page's adjustments and presets, the
// play space's edges, ROOM AT EDGES and PEEK, what the frame asks for, and the SETTINGS page.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace ProjectArena.Tests
{
    public class PassthroughTests
    {
        static float F(IntPtr p, int off) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, off));

        [Test]
        public void TheRuntimeGetsTheRoomFirstAndUnitysFrameIsUntouched()
        {
            if (IntPtr.Size != 8) Assert.Ignore("64-bit layouts only");
            IntPtr info = Marshal.AllocHGlobal(40), list = Marshal.AllocHGlobal(16), proj = Marshal.AllocHGlobal(48), quad = Marshal.AllocHGlobal(48);
            try
            {
                for (int i = 0; i < 48; i += 8) { Marshal.WriteInt64(proj, i, 0); Marshal.WriteInt64(quad, i, 0); }
                Marshal.WriteInt32(proj, 0, PassthroughFeature.XR_TYPE_COMPOSITION_LAYER_PROJECTION);
                Marshal.WriteInt64(proj, 16, (long)(PassthroughFeature.UNPREMULTIPLIED_ALPHA_BIT | 0x1));   // (a chromatic aberration bit kept)
                Marshal.WriteInt64(proj, 24, 777);                      // its space
                Marshal.WriteInt32(proj, 32, 2);                        // two views
                Marshal.WriteInt64(proj, 40, 0x12345678);               // (the views: the same array)
                Marshal.WriteInt32(quad, 0, 36);                        // a quad layer (XR_TYPE_COMPOSITION_LAYER_QUAD)
                Marshal.WriteIntPtr(list, 0, proj);
                Marshal.WriteIntPtr(list, 8, quad);
                for (int i = 0; i < 40; i += 8) Marshal.WriteInt64(info, i, 0);
                Marshal.WriteInt32(info, 0, 12);                        // XR_TYPE_FRAME_END_INFO
                Marshal.WriteInt64(info, 16, 123456789);                // displayTime
                Marshal.WriteInt32(info, 24, 1);                        // opaque blend mode
                Marshal.WriteInt32(info, 28, 2);
                Marshal.WriteIntPtr(info, 32, list);

                IntPtr c = PassthroughFeature.WithRoom(info, 0xABCDEF);
                Assert.That(c, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(c, Is.Not.EqualTo(info), "a copy");
                Assert.That(Marshal.ReadInt32(c, 0) == 12 && Marshal.ReadInt64(c, 16) == 123456789 && Marshal.ReadInt32(c, 24) == 1, "the frame's time and blend mode kept");
                Assert.That(Marshal.ReadInt32(c, 28), Is.EqualTo(3), "one layer more");
                IntPtr layers = Marshal.ReadIntPtr(c, 32);
                IntPtr room = Marshal.ReadIntPtr(layers, 0);
                Assert.That(Marshal.ReadInt32(room, 0), Is.EqualTo(PassthroughFeature.XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB), "your room first");
                Assert.That(Marshal.ReadInt64(room, 16), Is.EqualTo((long)PassthroughFeature.BLEND_TEXTURE_SOURCE_ALPHA_BIT));
                Assert.That(Marshal.ReadInt64(room, 32), Is.EqualTo(0xABCDEF), "its layer");
                IntPtr p2 = Marshal.ReadIntPtr(layers, 8);
                Assert.That(p2, Is.Not.EqualTo(proj), "the projection layer copied");
                Assert.That(Marshal.ReadInt64(p2, 16), Is.EqualTo((long)(PassthroughFeature.BLEND_TEXTURE_SOURCE_ALPHA_BIT | 0x1)), "blended by its alpha, premultiplied");
                Assert.That(Marshal.ReadInt64(p2, 24) == 777 && Marshal.ReadInt32(p2, 32) == 2 && Marshal.ReadInt64(p2, 40) == 0x12345678, "the rest of it as it was");
                Assert.That(Marshal.ReadIntPtr(layers, 16), Is.EqualTo(quad), "other layers as they are");
                Assert.That(Marshal.ReadInt32(info, 28) == 2 && Marshal.ReadIntPtr(info, 32) == list, "Unity's frame untouched");
                Assert.That(Marshal.ReadInt64(proj, 16), Is.EqualTo((long)(PassthroughFeature.UNPREMULTIPLIED_ALPHA_BIT | 0x1)), "Unity's projection layer untouched");
                Marshal.WriteInt32(info, 28, PassthroughFeature.MaxLayers);
                Assert.That(PassthroughFeature.WithRoom(info, 1), Is.EqualTo(IntPtr.Zero), "no room for one more: the frame goes as it is");
            }
            finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(list); Marshal.FreeHGlobal(proj); Marshal.FreeHGlobal(quad); }
        }

        // the shared hook (XrHooks): Unity chains every feature's hook, the second handed the first one's pointer. With a
        // hook of each feature's own, turning that pointer back into a delegate threw in a PC build (Mono:
        // InvalidCastException) and OpenXR never started (found 2026-11-09). One hook now, passed on by the second
        static int fakeEnds;
        static IntPtr fakeEndPtr;
        static readonly XrHooks.EndFrameFn fakeEnd = (s, i) => { ++fakeEnds; return 0; };
        static readonly XrHooks.GetProcAddrFn fakeGpa = (inst, name, fn) =>
        {
            Marshal.WriteIntPtr(fn, Marshal.PtrToStringAnsi(name) == "xrEndFrame" ? fakeEndPtr : IntPtr.Zero);
            return 0;
        };

        [Test]
        public void BothFeaturesShareOneHook()
        {
            if (IntPtr.Size != 8) Assert.Ignore("64-bit only");
            XrHooks.Forget();
            try
            {
                fakeEndPtr = Marshal.GetFunctionPointerForDelegate(fakeEnd);
                IntPtr runtime = Marshal.GetFunctionPointerForDelegate(fakeGpa);
                IntPtr first = XrHooks.Install(runtime);    // (PASSTHROUGH: Unity hooks in reverse priority)
                IntPtr second = XrHooks.Install(first);     // (FIELD OF VIEW, handed the first one's)
                Assert.That(first, Is.EqualTo(XrHooks.HookPointer), "the first installs the hook");
                Assert.That(second, Is.EqualTo(first), "the second passes the chain on, no second hook");
                Assert.That(XrHooks.Install(runtime), Is.EqualTo(first), "a new start of OpenXR installs it again");
                // the chain as the plug-in uses it: xrEndFrame through it reaches the runtime's, once a frame
                var gpa = Marshal.GetDelegateForFunctionPointer<XrHooks.GetProcAddrFn>(second);
                IntPtr name = Marshal.StringToHGlobalAnsi("xrEndFrame"), slot = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Assert.That(gpa(1, name, slot), Is.EqualTo(0));
                    IntPtr end = Marshal.ReadIntPtr(slot);
                    Assert.That(end, Is.Not.EqualTo(fakeEndPtr), "xrEndFrame: ours, in front of the runtime's");
                    fakeEnds = 0;
                    Marshal.GetDelegateForFunctionPointer<XrHooks.EndFrameFn>(end)(7, IntPtr.Zero);
                    Assert.That(fakeEnds, Is.EqualTo(1), "and the runtime's called once");
                }
                finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(slot); }
            }
            finally { XrHooks.Forget(); }
        }

        [Test]
        public void TheRoomsStyle()
        {
            if (IntPtr.Size != 8) Assert.Ignore("64-bit layouts only");
            IntPtr s = Marshal.AllocHGlobal(PassthroughFeature.StyleSize + PassthroughFeature.BcsSize);
            try
            {
                PassthroughFeature.WriteStyle(s, 0.7f, true, true);
                Assert.That(Marshal.ReadInt32(s, 0), Is.EqualTo(PassthroughFeature.XR_TYPE_PASSTHROUGH_STYLE_FB));
                Assert.That(F(s, 16), Is.EqualTo(0.7f), "ROOM BRIGHTNESS: the room's opacity over black");
                Assert.That(new[] { F(s, 20), F(s, 24), F(s, 28), F(s, 32) }, Is.EqualTo(new[] { 0.25f, 0.85f, 1.0f, 1.0f }), "OBJECT EDGES: cyan");
                IntPtr b = Marshal.ReadIntPtr(s, 8);
                Assert.That(b, Is.EqualTo(s + PassthroughFeature.StyleSize), "brightness, contrast and saturation chained");
                Assert.That(Marshal.ReadInt32(b, 0), Is.EqualTo(PassthroughFeature.XR_TYPE_PASSTHROUGH_BRIGHTNESS_CONTRAST_SATURATION_FB));
                Assert.That(new[] { F(b, 16), F(b, 20), F(b, 24) }, Is.EqualTo(new[] { 0f, 1f, 0f }), "GREY: no saturation");
                PassthroughFeature.WriteStyle(s, 1.4f, false, false);
                Assert.That(F(s, 16), Is.EqualTo(1f));
                Assert.That(F(s, 32), Is.EqualTo(0f), "no edges: a clear edge colour");
                Assert.That(F(Marshal.ReadIntPtr(s, 8), 24), Is.EqualTo(1f), "COLOUR");
            }
            finally { Marshal.FreeHGlobal(s); }
        }

        [Test]
        public void TheModesAndThePagesAdjustments()
        {
            var s = new AppSettings();
            Assert.That(s.ptMode == RoomPassthrough.Off && s.ptFloor == 0.8f, "OFF when the game starts; native's floor");
            RoomPassthrough.SetMode(s, RoomPassthrough.Outline);
            Assert.That(s.ptMode == RoomPassthrough.Outline && s.ptFloor == 0f, "OUTLINE starts with no floor");
            RoomPassthrough.SetMode(s, RoomPassthrough.Window);
            Assert.That(s.ptFloor, Is.EqualTo(1f), "WINDOW: all floor");
            s.ptFloor = 0.3f;
            RoomPassthrough.SetMode(s, RoomPassthrough.Window);
            Assert.That(s.ptFloor, Is.EqualTo(0.3f), "the same mode again keeps your floor");
            for (int i = 0; i < 12; ++i) RoomPassthrough.Adjust(s, 1);
            Assert.That(s.ptFloor, Is.EqualTo(1f), "COURT FLOOR up to 100 %");
            for (int i = 0; i < 30; ++i) RoomPassthrough.Adjust(s, 3);
            Assert.That(s.ptGlass, Is.EqualTo(2f), "GLASS WALLS up to 200 %");
            for (int i = 0; i < 30; ++i) RoomPassthrough.Adjust(s, 4);
            Assert.That(s.ptRoom, Is.EqualTo(0.1f).Within(1e-6f), "ROOM BRIGHTNESS down to 10 %");
            s.ptGlass = 0.97f;
            RoomPassthrough.Adjust(s, 2);
            Assert.That(s.ptGlass, Is.EqualTo(0.9f).Within(1e-6f), "in whole 10 % steps");
            RoomPassthrough.Preset(s, 0);
            Assert.That(s.ptMode == RoomPassthrough.Window && s.ptFloor == 1f && s.ptGlass == 1f && s.ptRoom == 1f && !s.ptMono && !s.ptEdges && s.ptEdgeReveal, "INDOOR");
            RoomPassthrough.Preset(s, 1);
            Assert.That(s.ptMode == RoomPassthrough.Outline && s.ptFloor == 0f && s.ptRoom == 0.7f && s.ptEdges, "OUTDOOR");
            Assert.That(RoomPassthrough.Help(RoomPassthrough.Window), Does.Contain("NO ROOM DEPTH"), "WINDOW says what this version leaves out");
            Assert.That(RoomPassthrough.Help(RoomPassthrough.ArenaMode), Is.EqualTo("THE COURT IN YOUR ROOM: THE STADIUM IS GONE"));
        }

        [Test]
        public void ThePlaySpacesEdges()
        {
            var rect = new List<Vector2> { new Vector2(-1.5f, -1f), new Vector2(1.5f, -1f), new Vector2(1.5f, 1f), new Vector2(-1.5f, 1f) };   // 3 x 2 m
            var cw = Enumerable.Reverse(rect).ToList();
            Assert.That(RoomPassthrough.EdgeDistance(rect, Vector2.zero), Is.EqualTo(1f).Within(1e-5f), "the middle: 1 m from the long sides");
            Assert.That(RoomPassthrough.EdgeDistance(cw, Vector2.zero), Is.EqualTo(1f).Within(1e-5f), "either winding");
            Assert.That(RoomPassthrough.EdgeDistance(rect, new Vector2(1.3f, 0)), Is.EqualTo(0.2f).Within(1e-5f), "near an end");
            Assert.That(RoomPassthrough.EdgeDistance(rect, new Vector2(0, 1.25f)), Is.EqualTo(-0.25f).Within(1e-5f), "outside: negative");
            Assert.That(RoomPassthrough.EdgeDistance(null, new Vector2(0.5f, 0)), Is.EqualTo(1.0f).Within(1e-5f), "no boundary: a 1.5 m circle");
            Assert.That(RoomPassthrough.MinWidth(rect), Is.EqualTo(2f).Within(1e-5f), "its shorter side");
        }

        [Test]
        public void RoomAtEdgesAndPeek()
        {
            var s = new AppSettings();
            var pt = new RoomPassthrough();
            void Run(int frames, bool avail, bool spec, bool peek, float head, float hand, bool bounds = true, float narrow = 3f)
            {
                for (int i = 0; i < frames; ++i) pt.Step(s, avail, spec, peek, head, hand, bounds, narrow, 1f / 72f, i);
            }
            Run(10, true, false, false, 1f, 1f);
            Assert.That(pt.want, Is.False, "OFF: the cameras stay off");
            Run(200, true, false, true, 1f, 1f);
            Assert.That(pt.want, Is.True, "PEEK in OFF too");
            Assert.That(pt.reveal, Is.EqualTo(0.95f).Within(0.01f), "the room at 95 %");
            Run(1, true, false, false, 1f, 1f);
            Assert.That(pt.want, Is.True, "Y let go: the room runs until the game is back");
            Run(400, true, false, false, 1f, 1f);
            Assert.That(pt.reveal == 0f && !pt.want, "then off");
            RoomPassthrough.SetMode(s, RoomPassthrough.ArenaMode);
            Run(5, true, false, false, 1f, 1f);
            Assert.That(pt.want && pt.reveal == 0f, "ARENA: the room runs, nothing faded");
            Run(400, true, false, false, 0.10f, 1f);
            Assert.That(pt.reveal, Is.EqualTo(0.85f).Within(0.01f), "the head 10 cm from the edge: the room at 85 %");
            Run(400, true, false, false, 0.22f, 1f);
            Assert.That(pt.reveal, Is.EqualTo(0.85f * 0.52f).Within(0.01f), "22 cm: about half");
            Run(400, true, false, false, 0.10f, 1f, narrow: 1.2f);
            Assert.That(pt.reveal, Is.EqualTo(0f).Within(0.003f), "a small boundary: the head doesn't count");
            Run(400, true, false, false, 1f, -0.15f, narrow: 1.2f);
            Assert.That(pt.reveal, Is.EqualTo(0.85f).Within(0.01f), "a hand 15 cm past the edge does");
            Run(400, true, false, false, -0.5f, -0.3f);
            Assert.That(pt.reveal, Is.EqualTo(0f).Within(0.003f), "the head outside the boundary: it isn't where you play");
            s.ptEdgeReveal = false;
            Run(400, true, false, false, 0.05f, -0.3f);
            Assert.That(pt.reveal, Is.EqualTo(0f).Within(0.003f), "ROOM AT EDGES: OFF");
            Run(200, true, true, true, 0.05f, -0.3f);
            Assert.That(pt.reveal, Is.EqualTo(0f).Within(0.003f), "spectating: no PEEK (Y is a camera)");
            Run(5, false, false, true, 1f, 1f);
            Assert.That(pt.want, Is.False, "not available: nothing asked");
            pt.Frame(s, true, false);
            Assert.That(pt.mrView, Is.True, "ARENA running: drawn for the room");
            pt.Frame(s, true, true);
            Assert.That(pt.mrView, Is.False, "GRAPHICS AND VIEW open: the arena, to judge the view");
            pt.Frame(s, false, false);
            Assert.That(pt.mrView || pt.fadeNow != 0f, Is.False, "not running: the arena, no fade");
        }

        [Test]
        public void WhatShowsOverYourRoom()
        {
            Assert.That(RoomPassthrough.Shows("environment", false, false, false), "the stadium, without passthrough");
            Assert.That(RoomPassthrough.Shows("court_outline", false, false, false), Is.False, "the outline only for OUTLINE");
            foreach (string n in new[] { "environment", "env_fx", "stadium_fx", "floor_reflect_env" })
                Assert.That(RoomPassthrough.Shows(n, true, false, false), Is.False, n + ": your room instead");
            foreach (string n in new[] { "arena", "floor", "floor_reflect", "court_fx", "court_pulse" })
                Assert.That(RoomPassthrough.Shows(n, true, false, false), n + ": the court stays");
            Assert.That(RoomPassthrough.Shows("floor", true, false, true) || RoomPassthrough.Shows("floor_reflect", true, false, true), Is.False, "COURT FLOOR 0 %: no floor");
            Assert.That(RoomPassthrough.Shows("arena", true, true, true) || RoomPassthrough.Shows("court_fx", true, true, true), Is.False, "OUTLINE: no court, no glow layers");
            Assert.That(RoomPassthrough.Shows("court_outline", true, true, true) && RoomPassthrough.Shows("court_pulse", true, true, true), "the outline and its pulses");
            Assert.That(RoomPassthrough.FloorFor(true, 1f), Is.EqualTo(0.998f), "a floor for the room: never quite solid");
            Assert.That(RoomPassthrough.FloorFor(false, 0.2f), Is.EqualTo(0.8f), "without passthrough: its own 80 %");
        }

        [Test]
        public void TheSettingsPage()
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pa_pt_test_" + Guid.NewGuid().ToString("N"));
            var g = new Sim.Game();
            g.Reset(true);
            var a = new AppState { game = g, settings = new AppSettings(folder), lobby = true };   // (saved there, not in the real settings)
            a.settings.Load(g.tun);
            var m = new Menu(a);
            m.Open(new Sim.Pose(Sim.Quat.Identity, Lobby.Spot + new Sim.Vec3(0, 1.6f, 0)), Lobby.Spot);
            m.settingsOpen = true;
            PassthroughFeature.SetForTests(false);
            try
            {
                Assert.That(m.Items().Single(i => i.label.StartsWith("PASSTHROUGH")).label, Is.EqualTo("PASSTHROUGH: N/A"));
                Assert.That(m.Items().Single(i => i.label.StartsWith("PASSTHROUGH")).enabled, Is.False, "no headset: N/A");
                PassthroughFeature.SetForTests(true);
                var b = m.Items().Single(i => i.label.StartsWith("PASSTHROUGH"));
                Assert.That(b.label == "PASSTHROUGH: OFF" && b.enabled, "the headset's room ready");
                m.Activate(b);
                Assert.That(m.ptPage);
                var v = m.Items();
                Assert.That(v.Where(i => i.act == Menu.Act.PtMode).Select(i => i.label), Is.EqualTo(new[] { "OFF", "ARENA", "WINDOW", "OUTLINE" }));
                Assert.That(v.Any(i => i.label == "COURT FLOOR 80%" && !i.enabled), "OFF: the court's rows wait");
                Assert.That(v.Any(i => i.label == "ROOM BRIGHTNESS 100%" && i.enabled));
                m.Activate(v.Single(i => i.label == "OUTLINE"));
                Assert.That(a.settings.ptMode, Is.EqualTo(RoomPassthrough.Outline));
                v = m.Items();
                Assert.That(v.Any(i => i.label == "COURT FLOOR 0%" && i.enabled) && v.Any(i => i.label == "GLASS WALLS 100%" && !i.enabled), "OUTLINE: no glass to set");
                m.Activate(v.Single(i => i.label == "INDOOR PRESET"));
                Assert.That(a.settings.ptMode, Is.EqualTo(RoomPassthrough.Window));
                m.Activate(m.Items().Single(i => i.label == "PEEK (HOLD Y): ON"));
                Assert.That(a.settings.ptPeekOn, Is.False);
                m.Activate(m.Items().Single(i => i.label == "BACK"));
                Assert.That(!m.ptPage && m.settingsOpen, "BACK: SETTINGS");
                Assert.That(m.Items().Single(i => i.label.StartsWith("PASSTHROUGH")).label, Is.EqualTo("PASSTHROUGH: WINDOW"));
            }
            finally
            {
                PassthroughFeature.SetForTests(false);
                if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
            }
        }
    }
}
