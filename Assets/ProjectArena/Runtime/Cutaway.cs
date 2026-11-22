// THE CUTAWAY (TASKS.md E42; native 7240d49 main.cpp cagePlanes; astrobleem, 2026-11-19: "the lines closest to the camera
// should be occluded or invisible in the pre-set-camera angles"): from a PC spectator's preset camera outside the glass,
// the room's walls between it and the court are left out (their lines, glass, hexagons and pulses, within 14 cm of the
// wall), the lines along the floor kept, and nothing past the wall. The walls the camera is outside of, up to four
// (FIREBALL BLITZ: the ones facing it most); from above the roof too, inside the walls' top edges, which stay (the
// owner, 2026-11-19, CORNER: "still needs the roof removed for a clean shot"). Shaders/Cutaway.hlsl leaves them out on
// the materials that say so: the SQUARE's arena, court_fx, court_pulse and the floor's mirror image (MdlImporter), and
// the EXPERIMENTAL rooms' walls (ExpArenaView's own effects batch)
using System;
using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;

namespace ProjectArena
{
    public static class Cutaway
    {
        public const float Width = 0.14f;
        static readonly int CutN = Shader.PropertyToID("_PA_CutN"), CutP = Shader.PropertyToID("_PA_Cut"),
            CutY = Shader.PropertyToID("_PA_CutY"), Roof = Shader.PropertyToID("_PA_Roof");
        static readonly Vector4[] planesU = new Vector4[4];
        static bool on;

        // the walls (native space: xyz the outward normal, w the distance) the eye `e` is outside of, most outside first, up
        // to four; y0: what is above it is left out; roof: (height, half-length or apothem, half-width, shape) or none
        public static int Planes(Game g, Vec3 e, float octWallR, Vector4[] pl, out float y0, float[] roof)
        {
            var w = new List<(Vector4 p, float out_)>();
            void Wall(float nx, float ny, float nz, float d)
            {
                float o = nx * e.x + ny * e.y + nz * e.z - d;
                if (o > 0.05f) w.Add((new Vector4(nx, ny, nz, d), o));
            }
            y0 = 0.12f;   // (the floor's edge stays)
            const float edge = 0.07f;   // (the roof left out this far inside the walls: their top edges stay)
            for (int k = 0; k < 4; ++k) roof[k] = 0;
            void Lid(float h, float l, float wd, float shape)
            {
                if (e.y > h + 0.05f) { roof[0] = h; roof[1] = l; roof[2] = wd; roof[3] = shape; }
            }
            if (ExpArenaView.CornersRoom(g))
            {   // a square turned 45 degrees
                y0 = ExpArena.kFloor + 0.12f;
                const float k = 0.70710678f;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2) Wall(sx * k, 0, sz * k, ExpArena.kCorner * k);
                Lid(ExpArena.kCeiling, ExpArena.kCorner - edge * 1.4142f, 0, 2);
            }
            else if (g.Octagon)
            {   // eight walls, as DrawOctagon draws them
                for (int i = 0; i < 8; ++i) Wall(MathF.Cos(i * M.kPi / 4f), 0, MathF.Sin(i * M.kPi / 4f), octWallR);
                Lid(ExpArena.kOctH, octWallR - edge, 0, 3);
            }
            else if (g.arenaKind == Arena.Diamond)
            {   // the end rolled 45 degrees, and the two ends
                y0 = -100f;   // (sunk in the floor: its lower walls' lines too)
                var v = new Vec3[4];
                ExpArena.DiamondEnd(v);
                var c = new Vec3(0, ExpArena.kRollCY, 0);
                for (int i = 0; i < 4; ++i)
                {
                    Vec3 t = v[(i + 1) % 4] - v[i];
                    Vec3 n = M.Normalize(new Vec3(0, -t.z, t.y));
                    if (M.Dot(n, (v[i] + v[(i + 1) % 4]) * 0.5f - c) < 0) n = n * -1f;
                    Wall(n.x, n.y, n.z, M.Dot(n, v[i]));
                }
                Wall(1, 0, 0, ExpArena.kRollLen); Wall(-1, 0, 0, ExpArena.kRollLen);
            }
            else
            {   // the SQUARE: 19.04 by 5.04, 4 m tall
                Wall(0, 0, 1, 2.52f); Wall(0, 0, -1, 2.52f); Wall(1, 0, 0, 9.52f); Wall(-1, 0, 0, 9.52f);
                Lid(4.0f, 9.52f - edge, 2.52f - edge, 1);
            }
            w.Sort((x, y) => y.out_.CompareTo(x.out_));
            int n4 = Math.Min(4, w.Count);
            for (int i = 0; i < n4; ++i) pl[i] = w[i].p;
            return n4;
        }

        // this frame: the cut on (a preset camera at `eye`, native space) or off
        public static void Apply(bool cut, Game g, Vec3 eye, float octWallR)
        {
            if (!cut)
            {
                if (on) { Shader.SetGlobalFloat(CutN, 0f); Shader.SetGlobalVector(Roof, Vector4.zero); on = false; }
                return;
            }
            var pl = new Vector4[4];
            var roof = new float[4];
            int n = Planes(g, eye, octWallR, pl, out float y0, roof);
            for (int i = 0; i < 4; ++i) planesU[i] = i < n ? new Vector4(pl[i].x, pl[i].y, -pl[i].z, pl[i].w) : Vector4.zero;   // (Unity axes: z negated)
            Shader.SetGlobalFloat(CutN, n);
            Shader.SetGlobalVectorArray(CutP, planesU);
            Shader.SetGlobalVector(CutY, new Vector4(y0, 1f, Width, 0));
            Shader.SetGlobalVector(Roof, new Vector4(roof[0], roof[1], roof[2], roof[3]));
            on = true;
        }
    }
}
