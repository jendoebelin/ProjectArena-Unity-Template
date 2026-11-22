// GRAPHICS (TASKS.md E38, native c97c364 / 9b00fbc / c184b84; Runtime/ArenaGraphics.cs): OFF / SPECTATING / ON, the
// PC spectator's own switches, the frame-rate guard, and the floor CONTACT SHADOWS land on
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;

namespace ProjectArena.Tests
{
    public class GraphicsTests
    {
        [Test]
        public void SpectatingAndThePcsOwn()
        {
            var s = new AppSettings("");
            ArenaGraphics.guard = false;
            ArenaGraphics.Decide(s, false, false);
            Assert.That(ArenaGraphics.Samples == 0 && !ArenaGraphics.Bloom && !ArenaGraphics.Contact && !ArenaGraphics.Grade && !ArenaGraphics.Reflect,
                        "playing: SMOOTH EDGES off, the SPECTATING ones off");
            ArenaGraphics.Decide(s, false, true);
            Assert.That(ArenaGraphics.Bloom && ArenaGraphics.Contact && ArenaGraphics.Grade && ArenaGraphics.Reflect && !ArenaGraphics.Ao,
                        "watching: on (AMBIENT OCCLUSION is the PC spectator's)");
            ArenaGraphics.Decide(s, true, true);
            Assert.That(ArenaGraphics.Samples == 4 && ArenaGraphics.Bloom && ArenaGraphics.Ao && ArenaGraphics.Vignette && ArenaGraphics.Reflect,
                        "the PC spectator: its own, all on, SMOOTH EDGES 4X");
            s.gfxBloom = 2; s.gfxMsaa = 1;
            ArenaGraphics.Decide(s, false, false);
            Assert.That(ArenaGraphics.Bloom && ArenaGraphics.Samples == 2, "ON: playing too");
        }

        [Test]
        public void TheFrameRateGuard()
        {
            var s = new AppSettings("") { gfxMsaa = 2 };   // (something costly)
            ArenaGraphics.guard = false;
            double t = 1000.0;
            void Frames(double secs, float fps, MatchState m)
            {
                for (int i = 0; i < (int)(secs * fps); ++i) { t += 1.0 / fps; ArenaGraphics.Guard(s, false, false, m, t, 72f); }
            }
            Frames(4.0, 72f, MatchState.InProgress);
            Assert.That(!ArenaGraphics.guard, "72 frames a second at 72 Hz: as set");
            Frames(2.5, 60f, MatchState.InProgress);
            Assert.That(!ArenaGraphics.guard, "dropping under 3 s: not yet");
            Frames(2.0, 60f, MatchState.InProgress);
            Assert.That(ArenaGraphics.guard, "fewer than 92% for 3 s in a match: BLOOM and SMOOTH EDGES off");
            ArenaGraphics.Decide(s, false, false);
            Assert.That(ArenaGraphics.Samples == 0, "SMOOTH EDGES off by the guard");
            Frames(0.1, 72f, MatchState.MatchSummary);
            Assert.That(!ArenaGraphics.guard, "the match over: as set again");
            s.gfxMsaa = 0; s.gfxBloom = 1;
            Frames(5.0, 40f, MatchState.InProgress);
            Assert.That(!ArenaGraphics.guard, "nothing costly on: nothing to turn off");
            Frames(5.0, 40f, MatchState.Countdown);
            Assert.That(!ArenaGraphics.guard, "out of a match: not watched");
        }

        [Test]
        public void TheFloorUnderPlayersAndBalls()
        {
            var g = new Game();
            g.SetArena(ArenaCollision.Load(File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            Assert.That(ArenaGraphics.FloorUnder(g, new Vec3(0, 1.0f, 0), out float y) && System.Math.Abs(y) < 0.05f, $"the court's floor under a ball at centre court ({y:F3})");
            Assert.That(ArenaGraphics.FloorUnder(g, new Vec3(0, 0.1f, 0), out _), "just over it too");
            Assert.That(!ArenaGraphics.FloorUnder(g, new Vec3(0, -1.0f, 0), out _), "under the floor: none (at most 0.3 m above the point)");
            Assert.That(!ArenaGraphics.FloorUnder(g, new Vec3(200, 1, 0), out _), "far outside the arena: none");
            // the shadows drawn: two players (each where drawn) and a ball in flight
            var fx = new FxRenderer(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/ProjectArena/Shaders/Text.shader"));
            var view = new[] { new PlayerInput(), new PlayerInput() };
            for (int p = 0; p < 2; ++p)
            {
                g.pl[p].@in.headTracked = true;
                g.pl[p].@in.head = new Pose(Quat.Identity, new Vec3(p == 0 ? -3f : 3f, 1.6f, 0));
                view[p].head = g.pl[p].@in.head;
            }
            g.pl[0].ball.state = BallState.Flying;
            g.pl[0].ball.pos = new Vec3(0, 1.2f, 0.4f);
            g.pl[1].ball.state = BallState.Dead;
            ArenaGraphics.DrawContact(fx, g, view, new[] { true, true }, 0, false);
            var mesh = fx.Snapshot(out _);
            Assert.That(mesh.GetIndexCount(9), Is.EqualTo(3 * 6), "CONTACT SHADOWS: under both players and the ball in flight (not the dead one)");
            ArenaGraphics.DrawContact(fx, g, view, new[] { false, false }, 0, false);
            mesh = fx.Snapshot(out _);
            Assert.That(mesh.GetIndexCount(9), Is.EqualTo(2 * 6), "the other player not drawn: no shadow for them");
        }
    }
}
