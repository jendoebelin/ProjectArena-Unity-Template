using System;
using System.Collections.Generic;
using ProjectArena.Online;
using ProjectArena.Sim;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The Crowd in the stands (native crowdSpots, drawFanFlags, stepCrowdFx, burstCrowdFx): one flag in the stands for
    // each fan waving that end's flag on their phone (the country's flag flying in strips, its ripples running out from
    // the pole, livelier as the end's hype rises; without a country a foam finger in the end's colour, waved from the
    // wrist), and the cheer bursts: hearts, stars and little flags pouring up out of that end's stands (HOTDOG MODE: hot
    // dogs and smiley faces).
    public class CrowdStands
    {
        struct Spot { public Vec3 p; public float yaw, phase; }
        class Fx { public Vec3 p, v; public float age, life, size, roll; public int kind, side; }

        static readonly List<Spot>[] spots = { new List<Spot>(), new List<Spot>() };
        readonly List<Fx> fx = new List<Fx>();
        uint burstSeed = 777u;
        public Texture2D foam, heart, star, hotdog, smile;
        public Func<int, Texture2D> flagTex = _ => null;   // a country's flag texture

        // places for each end (tiers 0.95 m deep and 0.62 m higher each, the side stands' first tier 8.4 m out, the end
        // stands' 14.4 m out): the side stands on that end's half first, then the stand behind that end. Each place: where
        // a fan sits and which way the court is (yaw: +Z there)
        static List<Spot> Spots(int p)
        {
            if (spots[0].Count == 0)
            {
                uint h = 12345u;
                float Rnd() { h = h * 1664525u + 1013904223u; return (h >> 8) / 16777216f; }
                for (int e = 0; e < 2; ++e)
                {
                    float sx = e == 0 ? -1f : 1f;
                    for (int i = 0; i < 40; ++i)
                    {
                        int k = (int)(Rnd() * 8.99f);
                        float y = 4.2f + k * 0.62f;
                        var s = new Spot();
                        if (i < 30)
                        {   // the side stands on this half
                            float sz = (i % 2) != 0 ? 1f : -1f;
                            s.p = new Vec3(sx * (1.5f + Rnd() * 12f), y, sz * (8.5f + k * 0.95f));
                            s.yaw = sz > 0 ? M.kPi : 0f;
                        }
                        else
                        {   // then behind the end zone
                            s.p = new Vec3(sx * (14.5f + k * 0.95f), y, (Rnd() - 0.5f) * (12f + k * 1.9f));
                            s.yaw = sx < 0 ? M.kPi * 0.5f : -M.kPi * 0.5f;
                        }
                        s.phase = Rnd() * 6.28f;
                        spots[e].Add(s);
                    }
                }
            }
            return spots[p];
        }

        public void Clear() => fx.Clear();
        public int BurstCount => fx.Count;
        public static int SpotCount(int p) => Spots(p).Count;

        // the bursts move: each rises out of the stand, drifts toward the court, slows and fades
        public void Step(float dt)
        {
            foreach (var f in fx)
            {
                f.age += dt;
                if (f.age <= 0) continue;
                f.p = f.p + f.v * dt;
                f.v.y -= 1.4f * dt;
                f.v = f.v * Math.Max(0f, 1f - 0.4f * dt);
            }
            fx.RemoveAll(f => f.age > f.life);
        }

        public void Burst(int side, int count)
        {
            float Rnd() { burstSeed = burstSeed * 1664525u + 1013904223u; return (burstSeed >> 8) / 16777216f; }
            var sp = Spots(side);
            for (int i = 0; i < count && fx.Count < 400; ++i)
            {
                Spot s = sp[(int)(Rnd() * 39.99f)];
                Vec3 toCourt = Quat.AxisAngle(new Vec3(0, 1, 0), s.yaw).Rotate(new Vec3(0, 0, 1));
                var f = new Fx
                {
                    p = s.p + new Vec3((Rnd() - 0.5f) * 1.5f, 0.9f + Rnd() * 0.4f, (Rnd() - 0.5f) * 1.5f),
                    v = toCourt * (0.6f + Rnd() * 0.8f) + new Vec3((Rnd() - 0.5f) * 0.6f, 2.6f + Rnd() * 1.6f, (Rnd() - 0.5f) * 0.6f),
                    age = -Rnd() * 0.5f,           // spread over the half second until the next update
                    life = 1.6f + Rnd() * 0.6f,
                    size = 0.30f + Rnd() * 0.15f,
                    kind = (int)(Rnd() * 2.99f),   // heart, star, flag
                    side = side,
                    roll = (Rnd() - 0.5f) * 1.5f,
                };
                fx.Add(f);
            }
        }

        // wide, dropY (native 0.3.17): the stadium as drawn: FOUR CORNERS and FIREBALL BLITZ widen it and lower it
        // (ExpArenaView.Outside); 1, 0 for the classic court. The spots stay the classic stands' and are moved there.
        public void Draw(PanelBatch pb, CrowdInfo c, int crowdMode, float now, int[] sideNation, float[][] sideRGB, bool shields, Vec3 eye,
                         float wide = 1f, float dropY = 0f)
        {
            if (crowdMode >= 2) return;   // (SETTINGS > CROWD: OFF)
            Vec3 In(Vec3 v) => new Vec3(v.x * wide, v.y + dropY, v.z * wide);   // a classic stand spot, there
            const int kStrips = 6;
            const float W = 0.8f, H = 0.53f, poleH = 1.9f;   // the flag's top edge at the pole's top
            for (int p = 0; p < 2 && c != null; ++p)
            {
                int n = Math.Min(40, c.waves[p]);   // one flag per phone
                float wind = 1f + 0.8f * c.hype[p] / 100f;
                Texture2D tex = sideNation[p] != 0 ? flagTex(sideNation[p]) : null;
                var sp = Spots(p);
                for (int i = 0; i < n; ++i)
                {
                    Spot s = sp[i];
                    Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), s.yaw);
                    if (tex == null)
                    {   // no country: a foam finger (#1) in the end's colour, waved from the wrist (owner, 2026-10-21)
                        float sway = 0.35f * MathF.Sin(now * 4.2f * wind + s.phase);
                        Quat qf = q * Quat.AxisAngle(new Vec3(0, 0, 1), sway);
                        Vec3 wrist = In(s.p) + new Vec3(0, 1.15f, 0);
                        pb.Quad(foam, new Pose(qf, wrist + qf.Rotate(new Vec3(0, 0.32f, 0))), 0.64f, 0.64f, new Vector4(sideRGB[p][0], sideRGB[p][1], sideRGB[p][2], 1));
                        continue;
                    }
                    Vec3 top = In(s.p) + new Vec3(0, poleH, 0);
                    pb.Quad(Texture2D.whiteTexture, new Pose(q, In(s.p) + new Vec3(0, poleH * 0.5f, 0)), 0.03f, poleH, new Vector4(0.55f, 0.56f, 0.6f, 1));   // the pole
                    // the cloth: strips out from the pole along the stand (+X), each bent by a wave that grows toward the free end
                    var bas = new Pose(q, top + q.Rotate(new Vec3(0, -H * 0.5f, 0)));
                    float zPrev = 0f;
                    for (int k = 0; k < kStrips; ++k)
                    {
                        float u0 = (float)k / kStrips, u1 = (float)(k + 1) / kStrips, um = 0.5f * (u0 + u1);
                        float Z(float u) => 0.11f * u * MathF.Sin(now * 5.5f * wind - u * 7f + s.phase);
                        float z1 = Z(u1);
                        float dx = W / kStrips, dz = z1 - zPrev;
                        float ang = -MathF.Atan2(dz, dx);
                        Vec3 cpos = bas.Transform(new Vec3(um * W, -0.02f * um * um, 0.5f * (zPrev + z1)));   // (a slight droop)
                        float shade = 0.80f + 0.20f * MathF.Cos(ang * 4f);
                        pb.Quad(tex, new Pose(q * Quat.AxisAngle(new Vec3(0, 1, 0), ang), cpos), MathF.Sqrt(dx * dx + dz * dz) * 1.02f, H,
                                new Vector4(shade, shade, shade, 1), new Vector4(1f / kStrips, 1f, u0, 0f));
                        zPrev = z1;
                    }
                }
            }
            // cheer bursts: hearts, stars and little flags pouring up out of that end's stands
            foreach (var f in fx)
            {
                if (f.age < 0) continue;
                float t = f.age / f.life;
                float alpha = Math.Min(1f, f.age * 6f) * (1f - t * t);
                Vec3 fp = In(f.p);
                Vec3 d = eye - fp;
                float L = M.Length(d);
                if (L < 1e-3f) continue;
                d = d / L;
                Quat q = Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(d.x, d.z)) * Quat.AxisAngle(new Vec3(1, 0, 0), -MathF.Asin(M.Clampf(d.y, -1, 1))) *
                         Quat.AxisAngle(new Vec3(0, 0, 1), f.roll * f.age);
                float sz = f.size * (0.6f + 0.4f * Math.Min(1f, f.age * 4f));
                Texture2D flag = sideNation[f.side] != 0 ? flagTex(sideNation[f.side]) : null;
                float[] sc = sideRGB[f.side];
                var white = new Vector4(1, 1, 1, alpha);
                if (f.kind == 2 && flag != null) pb.Quad(flag, new Pose(q, fp), sz, sz / 1.5f, white);
                else if (f.kind == 2) pb.Quad(foam, new Pose(q, fp), sz * 1.2f, sz * 1.2f, new Vector4(sc[0], sc[1], sc[2], alpha));   // no country: a foam finger
                else if (!shields) pb.Quad(f.kind == 0 ? hotdog : smile, new Pose(q, fp), sz * 1.1f, sz * 1.1f, white);   // HOTDOG MODE: the community's nod
                else
                {
                    bool st = f.kind == 1;
                    var col = new Vector4(st ? 1f : 0.55f + 0.45f * sc[0], st ? 0.85f : 0.25f + 0.45f * sc[1], st ? 0.3f : 0.35f + 0.45f * sc[2], alpha);
                    pb.Quad(st ? star : heart, new Pose(q, fp), sz, sz, col);
                }
            }
        }
    }
}
