// The arena's collision (open asset arena.col, written by the native repo's tools/build_open_assets.py; read as the
// native main.cpp loadCollision and tests/test_game.cpp loadCol do): "SCOL", triangle count (u32), then per triangle
// 9 floats (a, b, c) and its tag bits (u32). Native axes.
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectArena.Sim
{
    public static class ArenaCollision
    {
        public static List<CollisionTri> Load(byte[] data)
        {
            if (data == null || data.Length < 8 || Encoding.ASCII.GetString(data, 0, 4) != "SCOL")
                throw new ArgumentException("not an arena collision file (SCOL)");
            uint n = BitConverter.ToUInt32(data, 4);
            var tris = new List<CollisionTri>((int)n);
            int o = 8;
            float F() { float v = BitConverter.ToSingle(data, o); o += 4; return v; }
            for (uint i = 0; i < n; ++i)
            {
                var t = new CollisionTri
                {
                    a = new Vec3(F(), F(), F()),
                    b = new Vec3(F(), F(), F()),
                    c = new Vec3(F(), F(), F()),
                };
                t.tags = BitConverter.ToUInt32(data, o);
                o += 4;
                t.n = M.Normalize(M.Cross(t.b - t.a, t.c - t.a));
                tris.Add(t);
            }
            return tris;
        }
    }
}
