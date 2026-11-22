// The EXPERIMENTAL rooms' looks (task C3): which rooms the lobby offers, and where CORNERS puts the people waiting
// and the spectator (native holdingPose, spectatorSeat), and its stadium (native drawWorld's `outside`).
using System;
using NUnit.Framework;
using ProjectArena.Online;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class ExpRoomViewTests
    {
        [Test]
        public void TheLobbyOffersTheRoomsDrawnHere()
        {
            bool shown = Online.Net.NewRoomsShown;
            try
            {
                Online.Net.NewRoomsShown = false;
                Assert.That(new LanGame { players = 2 }.Playable, "a classic room");
                Assert.That(new LanGame { experimental = true, expRoom = Room.Diamond, players = 2 }.Playable, "DIAMOND (its look is in)");
                Assert.That(new LanGame { experimental = true, expRoom = Room.Corners, players = 2 }.Playable, "two-player CORNERS");
                Assert.That(!new LanGame { experimental = true, expRoom = Room.Corners, players = 4 }.Playable, "FOUR CORNERS: not until its four players are drawn");
                Assert.That(!new LanGame { experimental = true, expRoom = Room.Blitz, players = 4 }.Playable, "FIREBALL BLITZ: behind the same switch");
                Online.Net.NewRoomsShown = true;
                Assert.That(new LanGame { experimental = true, expRoom = Room.Corners, players = 4 }.Playable, "FOUR CORNERS once shown");
                Assert.That(new LanGame { experimental = true, expRoom = Room.Blitz, players = 4 }.Playable, "FIREBALL BLITZ once shown (task A5)");
            }
            finally { Online.Net.NewRoomsShown = shown; }
        }

        [Test]
        public void CornersPutsTheWaitingAndTheSpectatorOutsideTheGlass()
        {
            var g = new Game();
            g.tun.experimental = true; g.tun.expRoom = Room.Corners; g.tun.players = 4;
            g.UseArena(Arena.FourCorners, new System.Collections.Generic.List<CollisionTri>());
            bool outside = true, facingIn = true, apart = true;
            for (int s = 0; s < 8; ++s)
            {
                Pose p = ExpArenaView.HoldingPose(g, s);
                outside &= MathF.Abs(p.p.x) + MathF.Abs(p.p.z) > ExpArena.kCorner;   // (the glass: |x| + |z| = 9.5)
                facingIn &= Dot(ExpArenaView.HoldingFwd(g, s), new Vec3(0, 0, 0) - p.p) > 0;
                for (int k = 0; k < s; ++k) apart &= Length(ExpArenaView.HoldingPose(g, k).p - p.p) > 0.8f;
            }
            Assert.That(outside, "the eight waiting spots stand outside the room's glass");
            Assert.That(facingIn, "facing into the room");
            Assert.That(apart, "each its own place");
            Pose seat = ExpArenaView.SpectatorSeat(g);
            Assert.That(MathF.Abs(seat.p.x) + MathF.Abs(seat.p.z) > ExpArena.kCorner, "the spectator's seat is outside the room");
            ExpArenaView.Outside(g, out Vec3 at, out Vec3 sc);
            Assert.That(at.y < ExpArena.kFloor && sc.x > 1.3f, "the stadium is lowered below the room's floor and widened round it");
            var classic = new Game();
            Assert.That(Length(ExpArenaView.HoldingPose(classic, 2).p - ArenaSession.HoldingSpot(2)) < 1e-6f, "the classic court keeps its holding area");
        }
    }
}
