// THE CUTAWAY and the PC spectator's new presets (TASKS.md E42, native 7240d49, ae13e47, 044a00a; Runtime/Cutaway.cs,
// DesktopSpectator.Preset): from a preset camera outside the glass, the walls it is outside of, most outside first; from
// above, the roof too
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEngine;

namespace ProjectArena.Tests
{
    public class CutawayTests
    {
        static int Planes(Game g, Vec3 eye, out Vector4[] pl, out float y0, out float[] roof)
        {
            pl = new Vector4[4];
            roof = new float[4];
            return Cutaway.Planes(g, eye, 7.5f, pl, out y0, roof);
        }

        [Test]
        public void TheWallsAPresetIsOutsideOf()
        {
            var g = new Game();
            g.Reset(true);
            int n = Planes(g, new Vec3(0, 2.7f, 7.3f), out Vector4[] pl, out float y0, out float[] roof);   // SIDE
            Assert.That(n == 1 && pl[0] == new Vector4(0, 0, 1, 2.52f) && roof[3] == 0 && Mathf.Abs(y0 - 0.12f) < 1e-6f,
                        "SIDE: the near side wall, the floor's edge kept, no roof (below it)");
            n = Planes(g, new Vec3(-11.0f, 6.0f, 5.0f), out pl, out y0, out roof);   // CORNER
            Assert.That(n == 2 && pl[0] == new Vector4(0, 0, 1, 2.52f) && pl[1] == new Vector4(-1, 0, 0, 9.52f),
                        "CORNER: the near side wall (most outside) and the blue end's");
            Assert.That(roof[0] == 4.0f && roof[3] == 1 && Mathf.Abs(roof[1] - 9.45f) < 1e-5f && Mathf.Abs(roof[2] - 2.45f) < 1e-5f,
                        "... and from above the roof, inside the walls' top edges");
            n = Planes(g, new Vec3(-9.25f, 3.3f, 0), out pl, out y0, out roof);   // BLUE END: inside the glass now
            Assert.That(n == 0 && roof[3] == 0, "BLUE END inside the glass: nothing left out");
            g.tun.experimental = true; g.tun.expRoom = Room.Corners; g.tun.players = 4;
            g.UseArena(Arena.FourCorners, new System.Collections.Generic.List<CollisionTri>()); g.Reset(true);
            n = Planes(g, new Vec3(-11.5f, 6.5f, 4.0f), out pl, out y0, out roof);   // FOUR CORNERS' CORNER
            Assert.That(n >= 1 && Mathf.Abs(y0 - (ExpArena.kFloor + 0.12f)) < 1e-5f && roof[3] == 2,
                        "FOUR CORNERS: its turned square's walls, its floor kept, the roof from above");
        }
    }
}
