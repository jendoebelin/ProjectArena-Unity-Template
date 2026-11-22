// The match effects (Runtime/MatchFx.cs + FxRenderer.cs) through a whole bot match: no errors, and the trails,
// glows and bursts are drawn.
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEditor;
using UnityEngine;
using EventType = ProjectArena.Sim.EventType;

namespace ProjectArena.Tests
{
    public class MatchFxTests
    {
        [Test]
        public void EffectsRunThroughABotMatch()
        {
            var g = new Game();
            g.SetArena(ArenaCollision.Load(System.IO.File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            g.pl[0].isBot = true;
            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"));
            var mfx = new MatchFx();
            Vector3[] team = { new Vector3(0.1f, 0.7f, 1.0f), new Vector3(1.0f, 0.4f, 0.1f) };
            var input = new[] { new PlayerInput(), new PlayerInput() };
            int maxVerts = 0, ribbonFrames = 0, hits = 0;
            for (int i = 0; i < 72 * 60; ++i)
            {
                g.Step(1f / 72, input);
                foreach (var e in g.events) hits += e.type == EventType.HitPlayer ? 1 : 0;
                mfx.Update(g, 1f / 72);
                mfx.Draw(fx, g, new Vec3(0, 1.6f, 4), -1, team);
                fx.Flush();
                maxVerts = Mathf.Max(maxVerts, fx.LastVertices);
                ribbonFrames += fx.LastRibbonTriangles > 0 ? 1 : 0;
            }
            TestContext.Out.WriteLine($"most vertices in a frame {maxVerts}, frames with ribbons {ribbonFrames}, hits {hits}");
            Assert.That(ribbonFrames, Is.GreaterThan(72), "trails and arcs drawn while balls fly");
            Assert.That(hits, Is.GreaterThan(0), "(the match had hits, so hit bursts were drawn)");
            Assert.That(maxVerts, Is.LessThan(65536 * 4), "a sane amount of geometry");
        }

        // BALL MARKS (task C9, native 0.3.17): a mark on the surface the ball bounced off, facing the court, in the
        // thrower's colour, sized by the speed; fading in 4 s; HIT EFFECTS LOW keeps half; at most 48; real bounces too
        [Test]
        public void BallMarksSitOnTheSurfaceTheBallBouncedOff()
        {
            var g = new Game();
            g.SetArena(ArenaCollision.Load(System.IO.File.ReadAllBytes("Assets/ProjectArena/Art/Open/Data/arena.col.bytes")));
            g.Reset(true);
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            var mfx = new MatchFx();
            // bounces on the floor, a side wall (the classic court's glass is at z 2.5: native --marks' 2.55 is just
            // outside it, so its mark faces out) and the far end
            mfx.AddMark(g, new Vec3(-2.0f, 0.14f, 0.6f), 0, 14f, side);
            mfx.AddMark(g, new Vec3(-2.6f, 1.3f, 2.40f), 1, 8f, side);
            mfx.AddMark(g, new Vec3(-9.45f, 1.5f, 0.6f), 1, 30f, side);
            Assert.That(mfx.MarkCount, Is.EqualTo(3), "each bounce finds its surface");
            for (int i = 0; i < 3; ++i) TestContext.Out.WriteLine($"mark {i}: at ({mfx.MarkAt(i).x:F3} {mfx.MarkAt(i).y:F3} {mfx.MarkAt(i).z:F3}) facing ({mfx.MarkNormal(i).x:F2} {mfx.MarkNormal(i).y:F2} {mfx.MarkNormal(i).z:F2})");
            Assert.That(mfx.MarkNormal(0).y, Is.GreaterThan(0.9f), "the floor's mark faces up");
            Assert.That(mfx.MarkAt(0).y, Is.LessThan(0.05f), "... on the floor");
            Assert.That(mfx.MarkNormal(1).z, Is.LessThan(-0.9f), "the side wall's faces back into the court");
            Assert.That(mfx.MarkAt(1).z, Is.EqualTo(2.5f - 0.006f).Within(0.01f), "... on the glass");
            Assert.That(mfx.MarkNormal(2).x, Is.GreaterThan(0.9f), "the far end's faces the court");
            Assert.That(mfx.MarkRGB(0), Is.EqualTo(side[0]), "the thrower's colour");
            Assert.That(mfx.MarkRGB(1), Is.EqualTo(side[1]));
            Assert.That(mfx.MarkSize(0), Is.EqualTo(0.20f + 0.016f * 14f).Within(1e-5f), "sized by the speed");
            Assert.That(mfx.MarkSize(2), Is.EqualTo(0.55f).Within(1e-5f), "... up to 0.55 m");
            mfx.AddMark(g, new Vec3(0, 30f, 0), 0, 10f, side);
            Assert.That(mfx.MarkCount, Is.EqualTo(3), "no surface near: no mark");
            for (int i = 0; i < 300; ++i) mfx.Update(g, 1f / 72);
            Assert.That(mfx.MarkCount, Is.EqualTo(0), "they fade within 4 s");
            for (int i = 0; i < 60; ++i) mfx.AddMark(g, new Vec3(-2.0f, 0.14f, 0.6f), 0, 10f, side);
            Assert.That(mfx.MarkCount, Is.EqualTo(48), "at most 48");
            var low = new MatchFx { fxLevel = 2 };
            for (int i = 0; i < 10; ++i) low.AddMark(g, new Vec3(-2.0f, 0.14f, 0.6f), 0, 10f, side);
            // (native 0.3.19, the owner's fix, 2026-11-11: every other bounce; it used to refuse every mark while one
            // showed, about one at a time)
            Assert.That(low.MarkCount, Is.EqualTo(5), "HIT EFFECTS: LOW: half of them, every other bounce");
            // real bounces in a bot match leave marks
            g.Reset(true);
            g.pl[0].isBot = true;
            var real = new MatchFx();
            var input = new[] { new PlayerInput(), new PlayerInput() };
            int bounces = 0, most = 0;
            for (int i = 0; i < 72 * 40; ++i)
            {
                g.Step(1f / 72, input);
                foreach (var e in g.events) bounces += e.type == EventType.Bounce ? 1 : 0;
                real.Update(g, 1f / 72, side);
                most = Mathf.Max(most, real.MarkCount);
            }
            TestContext.Out.WriteLine($"bounces {bounces}, most marks at once {most}");
            Assert.That(bounces, Is.GreaterThan(0), "(the match had bounces)");
            Assert.That(most, Is.GreaterThan(0), "and they left marks");
        }

        // BALL TRAILS while waiting or watching (native 0.3.19): the host's picture comes every few frames there; a point
        // only when the ball has moved, the frames between filled along the way (not a spiral turning on the spot)
        [Test]
        public void TrailsFollowAPictureThatComesEveryFewFrames()
        {
            var g = new Game();
            g.Reset(true);
            var mfx = new MatchFx();
            Ball b = g.pl[0].ball;
            b.state = BallState.Flying;
            b.spin = new Vec3(0, 20f, 0);
            b.pos = new Vec3(0, 1.5f, 0);
            mfx.Update(g, 1f / 72);
            Assert.That(mfx.TrailCount(0), Is.EqualTo(1), "the first point");
            mfx.Update(g, 1f / 72);
            mfx.Update(g, 1f / 72);
            Assert.That(mfx.TrailCount(0), Is.EqualTo(1), "a ball that hasn't moved adds no point");
            b.pos = new Vec3(0.3f, 1.5f, 0);
            mfx.Update(g, 1f / 72);
            Assert.That(mfx.TrailCount(0), Is.EqualTo(4), "the move fills the frames it stood still: a point each");
            for (int i = 1; i <= 3; ++i)
                Assert.That(mfx.TrailPoint(0, i).x, Is.EqualTo(0.1f * i).Within(1e-5f), $"point {i}: evenly along the way");
            b.pos = new Vec3(0.31f, 1.5f, 0);
            mfx.Update(g, 1f / 72);
            Assert.That(mfx.TrailCount(0), Is.EqualTo(5), "a ball that moves every frame: a point a frame, as before");
        }
    }
}
