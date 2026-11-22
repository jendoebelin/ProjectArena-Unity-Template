// The EXPERIMENTAL arenas' collision (native game.h / game.cpp, namespace exparena), ported 1:1 (task A3).
// The native notes, owner 2026-10-29, Basic's rules in both:
// DIAMOND (the owner's drawing, the default): the classic court rolled 45 degrees along its length.
// 19 m long; its end a square as wide as the court (4.96 m), turned so its corners are straight
// up, down and to each side, 3.5 m from its centre. The end's centre 1.2 m over the platforms' tops (y = 0): no flat
// floor, the two lower walls meet in a V under your feet. The players on round floating platforms at their usual start,
// which the ball goes through (it comes up at you from below; tun.solidPlatforms makes them solid); the ends are the
// back walls. No foul zone: the foul line still recharges your ball and resets your shot clock, and a short throw still
// comes home.
// CORNERS: a square room turned 45 degrees, its corners 9.5 m out from the centre (x = -/+9.5 behind the players,
// z = -/+9.5 to the sides); the players on round floating platforms (2.4 m across, their tops at y = 0) at their usual
// start, the room's floor 1 m below them and its ceiling 4 m above. The ball bounces off everything, the floor below
// included; your platform is your foul zone; the corner walls behind each player (x beyond -/+7.1) are its back wall.
// FIREBALL BLITZ (native 0.3.13; a tester's idea, the owner's design): a regular octagon, its floor level with yours
// (y = 0), kOctH tall, its walls kOctMargin behind the players. No triangles: its walls are planes (Game.FireSub). The
// players stand evenly round a ring, its radius by how many are still in (four 6 m, three 4.8 m, two 3.5 m: the room
// closes in as players are knocked out).
using System;
using System.Collections.Generic;
using static ProjectArena.Sim.M;

namespace ProjectArena.Sim
{
    // the collision in use (Game.arenaKind): native ARENA_*
    public static class Arena
    {
        public const int Classic = 0, Corners = 1, Diamond = 2, FourCorners = 3, Octagon = 4;
    }

    // the EXPERIMENTAL rooms (Tunables.expRoom): native ROOM_*; DIAMOND for two, FOUR CORNERS and FIREBALL BLITZ for up to four
    public static class Room
    {
        public const int Diamond = 0, Corners = 1, Blitz = 2, Count = 3;
        public static bool FourPlayer(int expRoom) => expRoom == Corners || expRoom == Blitz;
    }

    public static class ExpArena
    {
        public const float kCorner = 9.5f, kFloor = -1f, kCeiling = 4f, kPlatformR = 1.2f, kPlatformThick = 0.15f, kBackX = 7.1f;
        public const float kFoulLineX = 5.8f;   // the platform's front edge: past it a throw recharges the ball
        // DIAMOND: ends at x = -/+9.5; the end (square); its centre height
        public const float kRollLen = 9.5f, kRollW = 4.96f, kRollH = 4.96f, kRollCY = 1.2f;
        // the end's corners: this far up, down and to the sides
        public const float kRollReach = (0.5f * kRollW + 0.5f * kRollH) * 0.70710678f;
        // FIREBALL BLITZ: the octagon's height, its walls behind the players, the ring's radius for n players still in
        public const float kOctH = 3.5f, kOctMargin = 1.5f;
        public static float OctRing(int n) => n >= 4 ? 6f : n == 3 ? 4.8f : 3.5f;

        // a builder for the triangles: `inside` is the side the face looks to
        sealed class Tris
        {
            public readonly List<CollisionTri> @out = new List<CollisionTri>();
            public void Tri(Vec3 a, Vec3 b, Vec3 c, Vec3 inside, uint tags)
            {
                Vec3 n = Normalize(Cross(b - a, c - a));
                if (Dot(n, inside) < 0) { Vec3 t = b; b = c; c = t; n = -n; }
                @out.Add(new CollisionTri { a = a, b = b, c = c, n = n, tags = tags });
            }
            public void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 inside, uint tags) { Tri(a, b, c, inside, tags); Tri(a, c, d, inside, tags); }

            // the round platforms at x = -/+7 (kRim segments; 0: none, the ball goes through); `foul`: the top is its
            // player's foul zone
            public void Platforms(int kRim, bool foul)
            {
                for (int p = 0; p < 2; ++p)
                {
                    Vec3 c = new Vec3(p != 0 ? 7f : -7f, 0, 0);
                    uint team = p != 0 ? Tag.Team1 : Tag.Team0;
                    for (int i = 0; i < kRim; ++i)
                    {
                        float a0 = i * (6.2831853f / kRim), a1 = (i + 1) * (6.2831853f / kRim);
                        Vec3 r0 = new Vec3(MathF.Cos(a0) * kPlatformR, 0, MathF.Sin(a0) * kPlatformR);
                        Vec3 r1 = new Vec3(MathF.Cos(a1) * kPlatformR, 0, MathF.Sin(a1) * kPlatformR);
                        Vec3 dn = new Vec3(0, -kPlatformThick, 0);
                        Tri(c, c + r0, c + r1, new Vec3(0, 1, 0), foul ? Tag.Arena | Tag.Foul | team : Tag.Arena);
                        Tri(c + dn, c + r0 + dn, c + r1 + dn, new Vec3(0, -1, 0), Tag.Arena);
                        Quad(c + r0, c + r1, c + r1 + dn, c + r0 + dn, (r0 + r1) * 0.5f, Tag.Arena);
                    }
                }
            }
        }

        // CORNERS; four: FOUR CORNERS (a platform in each corner, the ball through them, no foul zone, every corner a back wall)
        public static List<CollisionTri> Collision(bool four = false)
        {
            var t = new Tris();
            Vec3[] corner = { new Vec3(-kCorner, 0, 0), new Vec3(0, 0, kCorner), new Vec3(kCorner, 0, 0), new Vec3(0, 0, -kCorner) };
            const int kSeg = 8;
            for (int w = 0; w < 4; ++w)
            {   // the walls, in strips; near a player's corner they are its back wall
                Vec3 c0 = corner[w], c1 = corner[(w + 1) % 4];
                for (int i = 0; i < kSeg; ++i)
                {
                    Vec3 a = Lerp(c0, c1, i / (float)kSeg), b = Lerp(c0, c1, (i + 1) / (float)kSeg), mid = (a + b) * 0.5f;
                    uint tags = Tag.Arena;
                    if (MathF.Abs(mid.x) > kBackX) tags |= Tag.Return | (mid.x < 0 ? Tag.Team0 : Tag.Team1);
                    else if (four && MathF.Abs(mid.z) > kBackX) tags |= Tag.Return | (mid.z < 0 ? Tag.Team2 : Tag.Team3);
                    t.Quad(a + new Vec3(0, kFloor, 0), b + new Vec3(0, kFloor, 0), b + new Vec3(0, kCeiling, 0), a + new Vec3(0, kCeiling, 0),
                           new Vec3(-mid.x, 0, -mid.z), tags);
                }
            }
            for (int k = 0; k < 2; ++k)
            {   // the floor below and the ceiling
                float y = k != 0 ? kCeiling : kFloor;
                Vec3 up = new Vec3(0, y, 0);
                t.Quad(corner[0] + up, corner[1] + up, corner[2] + up, corner[3] + up, new Vec3(0, k != 0 ? -1f : 1f, 0), Tag.Arena);
            }
            t.Platforms(four ? 0 : 24, true);   // (FOUR CORNERS: the ball goes through the platforms, as in DIAMOND)
            return t.@out;
        }

        // DIAMOND: the end's corners (x = 0), round in order: the classic end rolled 45 degrees
        public static void DiamondEnd(Vec3[] v)
        {
            const float c = 0.70710678f, hw = 0.5f * kRollW, hh = 0.5f * kRollH;
            float[,] zy = { { hw, hh }, { -hw, hh }, { -hw, -hh }, { hw, -hh } };
            // (shifted sideways so the V's bottom corner is straight under the platforms, were the end not square)
            float shift = c * (hw - hh);
            for (int k = 0; k < 4; ++k) v[k] = new Vec3(0, kRollCY + c * (zy[k, 0] + zy[k, 1]), c * (zy[k, 0] - zy[k, 1]) + shift);
        }

        public static List<CollisionTri> DiamondCollision(bool solidPlatforms = false)
        {
            var t = new Tris();
            Vec3 ctr = new Vec3(0, kRollCY, 0);
            var v = new Vec3[4];
            DiamondEnd(v);
            // along the court in strips (no foul zone in DIAMOND, owner 2026-10-29: every wall just bounces)
            var xs = new List<float> { -kRollLen, -4.73f };
            for (float x = -4f; x <= 4.01f; x += 1f) xs.Add(x);
            xs.Add(4.73f); xs.Add(kRollLen);
            for (int i = 0; i + 1 < xs.Count; ++i)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                for (int k = 0; k < 4; ++k)
                {
                    Vec3 a = v[k], b = v[(k + 1) % 4];
                    Vec3 inside = ctr - (a + b) * 0.5f;
                    // (BOUNCES, native 5d3bbec: panels 1 and 3, z = -hw and +hw of the end's square before it was rolled,
                    // are its side walls)
                    t.Quad(a + new Vec3(x0, 0, 0), b + new Vec3(x0, 0, 0), b + new Vec3(x1, 0, 0), a + new Vec3(x1, 0, 0),
                           new Vec3(0, inside.y, inside.z), Tag.Arena | ((k % 2) != 0 ? Tag.SideWall : 0u));
                }
            }
            for (int p = 0; p < 2; ++p)
            {   // the ends: the back walls
                Vec3 o = new Vec3(p != 0 ? kRollLen : -kRollLen, 0, 0);
                t.Quad(v[0] + o, v[1] + o, v[2] + o, v[3] + o, new Vec3(p != 0 ? -1f : 1f, 0, 0),
                       Tag.Arena | Tag.Return | (p != 0 ? Tag.Team1 : Tag.Team0));
            }
            // the platforms: the ball goes through them by default (owner, 2026-10-29: so it comes up at you from below);
            // tun.solidPlatforms puts them back (they bounce; no foul zone in DIAMOND)
            t.Platforms(solidPlatforms ? 24 : 0, false);
            return t.@out;
        }
    }

    public partial class Game
    {
        // the collision in use: Arena.* (-1: set some other way, e.g. tests' SetArena)
        public int arenaKind = -1;

        // the arena to play in: the classic court's collision (kept for switching back) or the EXPERIMENTAL one; sets the
        // foul line and the bounds a ball leaves the arena by (native Game::useArena)
        public void UseArena(int kind, List<CollisionTri> classic)
        {
            if (arenaKind != kind)
            {   // (the collision only when it changes: callers may ask every frame)
                bool wasOct = arenaKind == Arena.Octagon;
                SetArena(kind == Arena.Corners ? ExpArena.Collision() : kind == Arena.FourCorners ? ExpArena.Collision(true)
                         : kind == Arena.Diamond ? ExpArena.DiamondCollision(tun.solidPlatforms)
                         : kind == Arena.Octagon ? new List<CollisionTri>() : classic);
                arenaKind = kind;
                if (wasOct || kind == Arena.Octagon)
                {   // (FIREBALL BLITZ moves the players; everywhere else they stand where they always did)
                    var def = new Tunables();
                    for (int p = 0; p < Teams.MaxPlayers; ++p) { tun.start[p] = def.start[p]; tun.spawner[p] = def.spawner[p]; }
                    uint bots = 0;
                    for (int p = 0; p < np; ++p) bots |= pl[p].isBot ? 1u << p : 0u;
                    ResetWithBots(bots);
                }
            }
            if (kind == Arena.Octagon)
            {
                tun.foulLineX = 0f;   // (no foul line: every throw is out in the room)
                tun.outLow = -1f; tun.outHigh = ExpArena.kOctH + 1f; tun.outX = tun.outZ = 12f;
                return;
            }
            if (kind == Arena.Corners || kind == Arena.FourCorners)
            {
                tun.foulLineX = ExpArena.kFoulLineX;
                tun.outLow = ExpArena.kFloor - 0.6f; tun.outHigh = ExpArena.kCeiling + 0.6f; tun.outX = tun.outZ = ExpArena.kCorner + 0.7f;
            }
            else if (kind == Arena.Diamond)
            {
                tun.foulLineX = 4.73f;
                tun.outLow = ExpArena.kRollCY - ExpArena.kRollReach - 0.6f; tun.outHigh = ExpArena.kRollCY + ExpArena.kRollReach + 0.6f;
                tun.outX = ExpArena.kRollLen + 0.7f; tun.outZ = ExpArena.kRollReach + 0.35f * (ExpArena.kRollW - ExpArena.kRollH) + 0.6f;
            }
            else
            {
                tun.foulLineX = 4.73f;
                tun.outLow = -1f; tun.outHigh = 6f; tun.outX = 11f; tun.outZ = 4f;
            }
        }
    }
}
