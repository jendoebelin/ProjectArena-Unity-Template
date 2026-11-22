// Scrolling textures stay precise (task C5, native 0.3.14's square-fireball fix): the offsets handed to the
// shaders are kept in [0, 1) however long the game has been playing.
using NUnit.Framework;
using UnityEngine;

namespace ProjectArena.Tests
{
    public class ScrollingTests
    {
        [Test]
        public void OffsetsStayBetweenZeroAndOne()
        {
            Vector4 w = NativeSpace.WrapUV(new Vector2(12345.25f, 7.75f));
            Assert.That(w.x, Is.EqualTo(0.25f).Within(1e-3f), "a big offset keeps only its fraction");
            Assert.That(w.y, Is.EqualTo(0.25f).Within(1e-3f), "v flipped for Unity's textures (-7.75 -> 0.25)");
            Vector4 n = NativeSpace.WrapUV(new Vector2(-0.4f, 0f));
            Assert.That(n.x, Is.EqualTo(0.6f).Within(1e-5f), "negative offsets wrap into [0, 1) too");
            Assert.That(n.y >= 0f && n.y < 1f, "and nothing outside [0, 1)");
        }
    }
}
