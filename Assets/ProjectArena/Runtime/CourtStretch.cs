// FOUL LINE moved (TASKS.md E3, native 0.3.20 main.cpp drawCourtModel): the court drawn with its line where the line is
// now. The court is built as a middle (out to the line, 4.75 m) and two ends in their players' colours (the line to the
// back wall, 9.5 m): glass, frames, hexagons, floor strips and floor. So the line itself moves, each end stretches or
// shrinks to start at the new line (its back wall where it is) and the middle with them; the rest (the centre line and
// emblem, the back walls) as it is. The pieces are told apart by their bounds, so any asset set's court follows. Only
// the drawing: the walls are where they were (collision: Game's foul zone follows the line on its own).
// Here each court model gets a copy of its mesh with every vertex's part in UV 4 (signed by the end it is at), and the
// Native shader moves it by the global _PA_FoulMove: one renderer as before, so its draw order and property blocks stay.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectArena
{
    public static class CourtStretch
    {
        public const float Line0 = 4.75f, Back = 9.5f;
        // the models native draws this way; the court's energy pulses with them (TASKS.md E37, native ec1faef: drawn where
        // the line had been, they ran down empty air beside the frame, like comets through the stands)
        public static readonly string[] Models = { "arena", "floor", "floor_reflect", "court_fx", "court_outline", "court_pulse" };
        public const int PartNone = 0, PartLine = 1, PartEnd = 2, PartMiddle = 3;
        static readonly int FoulMoveId = Shader.PropertyToID("_PA_FoulMove");

        // a section's part by its bounds along the court (x, metres; native's own tests of secMin / secMax), signed by
        // its end (-: blue's, at -x); 0: left as it is
        public static int Part(float lo, float hi)
        {
            float c = 0.5f * (lo + hi);
            int sg = c < 0 ? -1 : 1;
            float inner = Math.Min(Math.Abs(lo), Math.Abs(hi)), outer = Math.Max(Math.Abs(lo), Math.Abs(hi));
            if (hi - lo < 0.4f && Math.Abs(Math.Abs(c) - Line0) < 0.15f) return sg * PartLine;   // the line: moved
            if (lo * hi > 0 && Math.Abs(inner - Line0) < 0.35f && Math.Abs(outer - Back) < 0.25f) return sg * PartEnd;   // an end
            if (lo < 0 && hi > 0 && Math.Abs(-lo - Line0) < 0.15f && Math.Abs(hi - Line0) < 0.15f) return PartMiddle;   // the middle
            return PartNone;
        }

        // where a point at x of a section of that part is drawn with the line moved mv toward the centre (as the shader
        // does: native's matrices, x' = k x + c (1 - k) for an end)
        public static float MoveX(float x, int part, float mv)
        {
            if (mv == 0f || part == PartNone) return x;
            float sg = Math.Sign(part), L = Line0 - mv;
            switch (Math.Abs(part))
            {
                case PartLine: return x - sg * mv;
                case PartEnd: { float kEnd = (Back - L) / (Back - Line0); return kEnd * x + sg * Back * (1f - kEnd); }
                default: return x * L / Line0;
            }
        }

        // the model's meshes get their copy with each vertex's part (in UV 4); returns the parts found (0: nothing moves,
        // its mesh left as it was)
        public static int Prepare(GameObject go, List<int> found = null)
        {
            int n = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                Vector3[] v = m.vertices;
                var part = new Vector2[v.Length];
                int moved = 0;
                for (int s = 0; s < m.subMeshCount; ++s)
                {
                    int[] idx = m.GetIndices(s);
                    if (idx.Length == 0) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (int i in idx) { lo = Math.Min(lo, v[i].x); hi = Math.Max(hi, v[i].x); }
                    int p = Part(lo, hi);
                    found?.Add(p);
                    if (p == PartNone) continue;
                    ++moved;
                    foreach (int i in idx) part[i] = new Vector2(p, 0);
                }
                if (moved == 0) continue;
                Mesh copy = UnityEngine.Object.Instantiate(m);
                copy.name = m.name + " (foul line)";
                copy.SetUVs(4, part);
                // (the bounds take in the line moved its most either way, so the stretched court is never culled)
                Bounds b = copy.bounds;
                b.Expand(new Vector3(2f, 0, 0));
                copy.bounds = b;
                mf.sharedMesh = copy;
                n += moved;
            }
            return n;
        }

        // this frame's line: the game shown (the classic court, two players; elsewhere where the arena has it)
        public static float MoveFor(Sim.Game g) => g.arenaKind <= Sim.Arena.Classic && g.np == 2 ? g.tun.FoulMove() : 0f;

        public static void Apply(float mv) => Shader.SetGlobalFloat(FoulMoveId, mv);
    }
}
