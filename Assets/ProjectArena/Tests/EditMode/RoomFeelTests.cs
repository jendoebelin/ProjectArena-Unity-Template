// FIELD OF VIEW (task C6, native roomFeel): Runtime/RoomFeelFeature.cs on fake OpenXR memory (the editor has no
// runtime): xrLocateViews' eyes moved toward their middle by 1 / (1 + 10 % a step), and xrEndFrame's projection views
// put back at the real eye positions; the menu's row.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public class RoomFeelTests
    {
        static void Pos(IntPtr p, int off, float x, float y, float z)
        {
            Marshal.WriteInt32(p, off, BitConverter.SingleToInt32Bits(x));
            Marshal.WriteInt32(p, off + 4, BitConverter.SingleToInt32Bits(y));
            Marshal.WriteInt32(p, off + 8, BitConverter.SingleToInt32Bits(z));
        }
        static float F(IntPtr p, int off) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, off));

        [Test]
        public void TheEyesAreDrawnCloserAndTheHeadsetGetsTheRealOnes()
        {
            Assert.That(RoomFeelFeature.Factor(0), Is.EqualTo(1f));
            Assert.That(RoomFeelFeature.Factor(5), Is.EqualTo(1f / 1.5f).Within(1e-6f), "+50 %: the eyes a third closer");
            Assert.That(RoomFeelFeature.Factor(-5), Is.EqualTo(2f).Within(1e-6f), "-50 %: twice as far apart");
            RoomFeelFeature.Step = 9;
            Assert.That(RoomFeelFeature.Step, Is.EqualTo(5), "the step is kept to -5..+5");
            RoomFeelFeature.ForgetForTests();
            const int V = RoomFeelFeature.ViewStride, P = RoomFeelFeature.PosOffset;
            IntPtr views = Marshal.AllocHGlobal(2 * V);
            IntPtr pv = Marshal.AllocHGlobal(2 * RoomFeelFeature.ProjViewStride);
            IntPtr other = Marshal.AllocHGlobal(48), layer = Marshal.AllocHGlobal(48), layers = Marshal.AllocHGlobal(2 * IntPtr.Size), info = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < 2 * V; i += 4) Marshal.WriteInt32(views, i, 0);
                Pos(views, P, -0.032f, 1.6f, 0.01f);        // the left eye
                Pos(views, V + P, 0.032f, 1.6f, -0.01f);   // the right
                RoomFeelFeature.MoveEyes(views, 2, 2);     // +20 %
                float lx = F(views, P), rx = F(views, V + P), lz = F(views, P + 8);
                TestContext.Out.WriteLine($"eyes {lx:F4} .. {rx:F4} (were -0.0320 .. 0.0320)");
                Assert.That(rx - lx, Is.EqualTo(0.064f / 1.2f).Within(1e-5f), "the separation x 1 / 1.2");
                Assert.That(0.5f * (lx + rx), Is.EqualTo(0f).Within(1e-6f), "round the same middle");
                Assert.That(lz, Is.EqualTo(0.01f / 1.2f).Within(1e-6f), "every axis toward the middle");
                Assert.That(F(views, P + 4), Is.EqualTo(1.6f), "the height stays (both eyes level)");
                // the frame Unity submits: a projection layer whose views carry the moved poses, and another kind of layer
                for (int i = 0; i < 2 * RoomFeelFeature.ProjViewStride; i += 4) Marshal.WriteInt32(pv, i, 0);
                Pos(pv, P, lx, 1.6f, lz);
                Pos(pv, RoomFeelFeature.ProjViewStride + P, rx, 1.6f, F(views, V + P + 8));
                Marshal.WriteInt32(layer, 0, RoomFeelFeature.XR_TYPE_COMPOSITION_LAYER_PROJECTION);
                Marshal.WriteInt32(layer, 32, 2);
                Marshal.WriteIntPtr(layer, 40, pv);
                Marshal.WriteInt32(other, 0, 36);   // (XR_TYPE_COMPOSITION_LAYER_QUAD: left alone)
                Marshal.WriteIntPtr(layers, 0, other);
                Marshal.WriteIntPtr(layers, IntPtr.Size, layer);
                Marshal.WriteInt32(info, 28, 2);
                Marshal.WriteIntPtr(info, 32, layers);
                int n = RoomFeelFeature.RestorePoses(info);
                Assert.That(n, Is.EqualTo(2), "both views put back");
                Assert.That(F(pv, P), Is.EqualTo(-0.032f), "the left eye's real place");
                Assert.That(F(pv, RoomFeelFeature.ProjViewStride + P), Is.EqualTo(0.032f), "the right's");
                Assert.That(F(pv, P + 8), Is.EqualTo(0.01f), "every axis");
                Assert.That(RoomFeelFeature.RestorePoses(info), Is.EqualTo(0), "a pose that wasn't moved is left as it is");
            }
            finally
            {
                foreach (var p in new[] { views, pv, other, layer, layers, info }) Marshal.FreeHGlobal(p);
                RoomFeelFeature.ForgetForTests();
                RoomFeelFeature.Step = 0;
            }
        }

        [Test]
        public void TheGraphicsPageHasTheFieldOfView()
        {
            string folder = Path.Combine(Path.GetTempPath(), "pa_fov_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var g = new Game();
                g.Reset(true);
                var app = new AppState { game = g, settings = new AppSettings(folder), lobby = true };
                app.settings.Load(g.tun);
                var menu = new Menu(app);
                menu.Open(new Pose(Quat.Identity, Lobby.Spot + new Vec3(0, 1.6f, 0)), Lobby.Spot);
                menu.settingsOpen = true;
                menu.gfxPage = true;
                Menu.Item Find(string l) => menu.Items().First(i => i.label == l);
                Assert.That(Find("NORMAL").act, Is.EqualTo(Menu.Act.None), "NORMAL to start with");
                menu.Activate(Find("+"));
                menu.Activate(Find("+"));
                Assert.That(app.settings.roomFeel == 2 && RoomFeelFeature.Step == 2 && menu.Items().Any(i => i.label == "+20%"), "+ +: +20 %, and the feature told");
                for (int i = 0; i < 9; ++i) menu.Activate(menu.Items().First(it => it.act == Menu.Act.FovDown));
                Assert.That(app.settings.roomFeel, Is.EqualTo(-5), "down to -50 %");
                Assert.That(menu.Items().First(it => it.act == Menu.Act.FovDown).enabled, Is.False, "and no further");
                var reread = new AppSettings(folder);
                reread.Load(new Tunables());
                Assert.That(reread.roomFeel, Is.EqualTo(-5), "kept in settings.cfg (roomfeel, native's key)");
            }
            finally
            {
                RoomFeelFeature.Step = 0;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
