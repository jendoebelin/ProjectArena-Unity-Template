// FIREBALL BLITZ's looks (task A5): the room's places outside the glass (the waiting rows, the dugout), the stadium
// round it, everyone in their own colour; the knockout and room-closing messages; a fireball coming at you turns red;
// and the octagon, the fireballs, health and the card draw without trouble.
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEditor;
using UnityEngine;
using EventType = ProjectArena.Sim.EventType;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class BlitzViewTests
    {
        static readonly float[][] Side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };

        static Game Octagon()
        {
            var g = new Game();
            g.tun.experimental = true; g.tun.expRoom = Room.Blitz; g.tun.players = 4; g.tun.meetTime = 0; g.tun.basicMatchTime = 0;
            g.UseArena(Arena.Octagon, new List<CollisionTri>());
            g.ResetWithBots(0);
            return g;
        }

        [Test]
        public void PlacesOutsideTheGlass()
        {
            Game g = Octagon();
            Assert.That(ExpArenaView.Experimental(g), "an EXPERIMENTAL room (the classic court's models hidden)");
            for (int k = 0; k < 8; ++k)
            {
                Sim.Pose s = ExpArenaView.HoldingPose(g, k);
                Assert.That(Length(new Vec3(s.p.x, 0, s.p.z)), Is.GreaterThan(g.ringApothem + 1f), $"waiting spot {k} outside the glass");
                Assert.That(Dot(ExpArenaView.HoldingFwd(g, k), Normalize(new Vec3(-s.p.x, 0, -s.p.z))), Is.GreaterThan(0.95f), $"spot {k} faces in (through the glass)");
            }
            Assert.That(Dot(Normalize(ExpArenaView.HoldingPose(g, 0).p), Normalize(ExpArenaView.HoldingPose(g, 4).p)), Is.LessThan(-0.9f),
                        "the dugout (spots 4-7): the row across the room");
            ExpArenaView.Outside(g, out Vec3 at, out Vec3 sc);
            Assert.That(sc.x == 1.35f && at.y == -0.4f, "the stadium widened round it, its floor level with yours");
            var colours = new HashSet<float>();
            for (int p = 0; p < 4; ++p) colours.Add(ExpArenaView.PlayerRGB(g, Side, p)[0] + 10f * ExpArenaView.PlayerRGB(g, Side, p)[1]);
            Assert.That(colours.Count, Is.EqualTo(4), "everyone in their own colour (everyone for themselves)");
        }

        [Test]
        public void MessagesAndTheRedWarning()
        {
            var v = new BlitzView();
            Assert.That(v.Event(new GameEvent(EventType.Knockout, 2, 0, default, 2), 1, true), Is.False);
            Assert.That(v.msg, Is.EqualTo("GREEN IS OUT"), "someone else out: their colour");
            Assert.That(v.Event(new GameEvent(EventType.Knockout, 1, 0, default, 2), 1, true), "your knockout: a long buzz");
            Assert.That(v.msg, Is.EqualTo("YOU'RE OUT"));
            v.Event(new GameEvent(EventType.Knockout, 1, 0, default, 2), 1, false);
            Assert.That(v.msg, Is.EqualTo("ORANGE IS OUT"), "watching: the colour, even for the local place");
            v.Event(new GameEvent(EventType.RingClose, -1, -1, default, 2f), 1, true);
            Assert.That(v.msg == "THE ROOM CLOSES IN" && v.msgP == -1 && v.msgT >= 1f, "the room closing in");

            Game g = Octagon();
            var stand = new PlayerInput[4];
            for (int p = 0; p < 4; ++p)
            {
                stand[p] = new PlayerInput { headTracked = true };
                stand[p].head = new Sim.Pose(Quat.Identity, g.tun.start[p] + new Vec3(0, 1.65f, 0));
            }
            for (int i = 0; i < 72 * 5 && g.match != MatchState.InProgress; ++i)
            {
                var tmp = new PlayerInput[4];
                for (int p = 0; p < 4; ++p) tmp[p] = stand[p].Clone();
                g.Step(1f / 72, tmp);
            }
            Assert.That(g.match, Is.EqualTo(MatchState.InProgress));
            Vec3 head = g.pl[2].@in.head.p, from = g.pl[0].@in.head.p + new Vec3(0, -0.5f, 0);
            Vec3 flat = Normalize(new Vec3(head.x - from.x, 0, head.z - from.z)), dir = flat;
            for (float deg = -30; deg <= 20; deg += 2)
            {   // (the throw that meets player 2, the next place round)
                Vec3 d = flat * System.MathF.Cos(Deg2Rad(deg)) + new Vec3(0, System.MathF.Sin(Deg2Rad(deg)), 0);
                var b = new Ball { scale = 1f, pos = from, vel = d * 9f, speedLimit = 9f * System.MathF.Cos(Deg2Rad(deg)) };
                if (g.FireHitTime(b, head, 2f) >= 0) { dir = d; break; }
            }
            g.Serve(0, from, dir, 9f, default, false);
            v.Update(g, 2, true, 1f / 72);
            Assert.That(v.danger & 1, Is.EqualTo(1), "a fireball that will hit you: red");
            v.Update(g, 1, true, 1f / 72);
            Assert.That(v.danger, Is.EqualTo(0), "one going elsewhere: not");
            v.Update(g, 2, false, 1f / 72);
            Assert.That(v.danger, Is.EqualTo(0), "watching: no warning");

            var fx = new FxRenderer(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader"),
                                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"),
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png"));
            var tc = new Vector3[4];
            var heads = new Sim.Pose[4];
            for (int p = 0; p < 4; ++p) { float[] c = ExpArenaView.PlayerRGB(g, Side, p); tc[p] = new Vector3(c[0], c[1], c[2]); heads[p] = g.pl[p].@in.head; }
            new ExpArenaView(null, null).DrawOctagon(fx, g, Side, head, 0f);
            v.Draw(fx, g, 1, true, tc, heads, head, 0f);
            BlitzView.DrawHelp(fx, g, ExpArenaView.HoldingPose(g, 0), 0.4);
            BlitzView.Ghost(fx, tc[2], heads[2].p, head);
            var info = new Scoreboard.Info { octMsg = v.msg, octMsgP = v.msgP, octMsgT = v.msgT };
            Scoreboard.Draw(fx, g, 1, tc, info, head);
            fx.Flush();
        }

        // FIREBALL BLITZ's echoes (native 0.3.19): four smaller, fainter copies along the path the fireball just took,
        // 55 ms apart (from where this screen drew it); none bunched up behind a slow one, none once it's gone
        [Test]
        public void FireballEchoesFollowItsPath()
        {
            var v = new BlitzView();
            var f = new Ball { state = BallState.Flying, scale = 1f };
            float r = 0.08f, now = 10f;
            for (int i = 0; i <= 30; ++i)
            {   // flying along +X at 8 m/s, a frame each 1/72 s
                now = 10f + i / 72f;
                f.pos = new Vec3(8f * (now - 10f), 1.5f, 0);
                v.TrackEcho(0, 0, f, now);
            }
            for (int e = 1; e <= 4; ++e)
            {
                Assert.That(v.Echo(0, 0, e, f, r, now, out Vec3 at, out float fade, out float size), $"echo {e}");
                Assert.That(at.x, Is.EqualTo(f.pos.x - 8f * 0.055f * e).Within(1e-3f), $"echo {e}: where it was {55 * e} ms ago");
                Assert.That(fade, Is.EqualTo(0.75f - 0.15f * e).Within(1e-6f), $"echo {e}: fainter");
                Assert.That(size, Is.EqualTo(0.72f - 0.12f * e).Within(1e-6f), $"echo {e}: smaller");
            }
            var slow = new Ball { state = BallState.Flying, scale = 1f };
            for (int i = 0; i <= 30; ++i) { slow.pos = new Vec3(0.2f * i / 72f, 1.5f, 0); v.TrackEcho(1, 0, slow, 10f + i / 72f); }
            Assert.That(v.Echo(1, 0, 1, slow, r, 10f + 30 / 72f, out _, out _, out _), Is.False, "none bunched up behind a slow one");
            v.TrackEcho(0, 0, null, now + 0.02f);
            Assert.That(v.Echo(0, 0, 1, f, r, now + 0.02f, out _, out _, out _), Is.False, "gone with the fireball");
        }
    }
}
