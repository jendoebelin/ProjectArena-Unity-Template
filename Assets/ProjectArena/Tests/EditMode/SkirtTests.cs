// TENNIS SKIRT and KILT (TASKS.md E13, native 481a290 test_game.cpp "[skirts]"; PR #25, his swish): the swing, stepped
// at 90 frames a second; and the template's own: their sway shapes load, and the kilt drawn in pieces
using System;
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEditor;

namespace ProjectArena.Tests
{
    public class SkirtTests
    {
        [Test]
        public void TheSkirtsSway()
        {
            AvatarDesc d = Avatar.PresetLook(0);
            d.lower = 5;
            bool kinds = Avatar.SwishKind(d) == 0;
            d.lower = 6;
            kinds &= Avatar.SwishKind(d) == 1;
            d.lower = 4;
            kinds &= Avatar.SwishKind(d) == -1;
            Assert.That(kinds, "SKIRTS: the tennis skirt and the kilt sway, nothing else does");
            float[] maxOff = { 0.065f, 0.05f }, maxTw = { 0.23f, 0.17f };
            bool trails = true, bounded = true, settles = true, lags = true, fresh = true;
            float most = 0, after = 1;
            for (int kind = 0; kind < 2; ++kind)
            {
                var s = new Avatar.Swish();
                double t = 0;
                float x = 0, yaw = 0;
                Avatar.StepSwish(s, kind, x, 0, yaw, t);
                // a sidestep: 1 m/s along +x for half a second, facing -z (yaw 0): the hem trails toward -x
                for (int i = 0; i < 45; ++i)
                {
                    t += 1.0 / 90; x += 1f / 90;
                    Avatar.StepSwish(s, kind, x, 0, yaw, t);
                    float h = MathF.Sqrt(s.x * s.x + s.z * s.z);
                    if (h > maxOff[kind] + 1e-5f || float.IsNaN(s.x) || float.IsInfinity(s.x)) bounded = false;
                    most = Math.Max(most, h);
                }
                if (s.x > -0.01f) trails = false;
                // stopped: back to rest within 1.5 s
                for (int i = 0; i < 135; ++i) { t += 1.0 / 90; Avatar.StepSwish(s, kind, x, 0, yaw, t); }
                float rest = MathF.Sqrt(s.x * s.x + s.z * s.z);
                after = Math.Max(after == 1 ? 0f : after, rest);
                if (rest > 0.002f) settles = false;
                // a quick turn to the left (yaw growing): the skirt lags, turned the other way, never past its limit
                float least = 0;
                for (int i = 0; i < 30; ++i)
                {
                    t += 1.0 / 90; yaw += 3f / 90;
                    Avatar.StepSwish(s, kind, x, 0, yaw, t);
                    least = Math.Min(least, s.twist);
                    if (Math.Abs(s.twist) > maxTw[kind] + 1e-5f) bounded = false;
                }
                if (least > -0.05f) lags = false;
                // a jump (a teleport, a respawn): afresh, at rest
                t += 1.0 / 90;
                Avatar.StepSwish(s, kind, x + 2f, 0, yaw, t);
                if (s.x != 0 || s.z != 0 || s.twist != 0) fresh = false;
            }
            TestContext.Out.WriteLine($"  (a sidestep swings the hem out {most * 1000:F0} mm at most; stopped, {after * 1000:F1} mm left after 1.5 s)");
            Assert.That(trails, "SKIRTS: stepping sideways, the hem trails the way it came");
            Assert.That(bounded, "SKIRTS: never further than the skirt's limits (out, twist)");
            Assert.That(settles, "SKIRTS: stopped, the skirt settles back to rest");
            Assert.That(lags, "SKIRTS: turning, the skirt lags the turn");
            Assert.That(fresh, "SKIRTS: after a jump, at rest again");
        }

        [Test]
        public void TheirShapesAndPieces()
        {
            var av = new AvatarDraw(AssetDatabase.LoadAssetAtPath<AvatarLibrary>("Assets/ProjectArena/Settings/AvatarLibrary.asset"),
                                    AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/ProjectArena/Shaders/Text.shader"));
            Assert.That(av.SwishCount, Is.EqualTo(6), "each skirt's sway shape, both bodies (<model>.swish): the TENNIS SKIRT, the KILT and the PELURU dress (TASKS.md E35)");
            foreach (string m in new[] { "av_bottom_5_m", "av_bottom_5_f", "av_bottom_6_m", "av_bottom_6_f" })
                Assert.That(av.PartMesh(m), Is.Not.Null, m + ": its mesh (the kilt in 16-bit pieces: drawn in turn)");
        }
    }
}
