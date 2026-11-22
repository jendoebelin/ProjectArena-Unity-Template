// HANDS APART (TASKS.md E8; native avatar.cpp, 0.3.20; the owner, 2026-11-16, her video: with the controllers held
// together, the two gauntlets crossed into each other; armour is bigger than a hand round a controller): each part of a
// drawn gauntlet (the fist, the cuff) as a grid of its signed distance (inside negative) with points on its surface, so
// a player's two hands can be drawn just apart. Drawing only: the game keeps the tracked hands. Ported line by line
// (buildHandShape, HandShape::dist / normal, handsApartShift, handsApartDepth, handShapeFromModel, affineInverse).
using System;
using System.Collections.Generic;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena
{
    public sealed class HandShape
    {
        public Vec3 lo;                        // the grid's first point (the part's own frame)
        public float h;                        // the grid's spacing (m)
        public readonly int[] n = new int[3];
        public sbyte[] d = new sbyte[0];       // the signed distance at each point (mm, clamped)
        public readonly List<Vec3> pts = new List<Vec3>();   // points on its outside, about a centimetre apart
        public Vec3 c;                         // a sphere round the points
        public float r;
        public bool Valid => pts.Count > 0;

        // (m) trilinear; off the grid, its edge value plus the way off
        public float Dist(Vec3 p)
        {
            if (d.Length == 0) return 1f;
            // (no allocations: it runs for every surface point of both hands)
            float gx = (p.x - lo.x) / h, gy = (p.y - lo.y) / h, gz = (p.z - lo.z) / h;
            float cx = Math.Max(0f, Math.Min(n[0] - 1, gx)), cy = Math.Max(0f, Math.Min(n[1] - 1, gy)), cz = Math.Max(0f, Math.Min(n[2] - 1, gz));
            float off2 = (gx - cx) * (gx - cx) + (gy - cy) * (gy - cy) + (gz - cz) * (gz - cz);
            int ix = Math.Min(n[0] - 2, (int)cx), iy = Math.Min(n[1] - 2, (int)cy), iz = Math.Min(n[2] - 2, (int)cz);
            float fx = cx - ix, fy = cy - iy, fz = cz - iz;
            int nx = n[0], nxy = n[0] * n[1], b = (iz * n[1] + iy) * nx + ix;
            float v000 = d[b], v100 = d[b + 1], v010 = d[b + nx], v110 = d[b + nx + 1];
            float v001 = d[b + nxy], v101 = d[b + nxy + 1], v011 = d[b + nxy + nx], v111 = d[b + nxy + nx + 1];
            float x00 = v000 + (v100 - v000) * fx, x10 = v010 + (v110 - v010) * fx;
            float x01 = v001 + (v101 - v001) * fx, x11 = v011 + (v111 - v011) * fx;
            float y0 = x00 + (x10 - x00) * fy, y1 = x01 + (x11 - x01) * fy;
            return (y0 + (y1 - y0) * fz) * 0.001f + MathF.Sqrt(off2) * h;
        }

        // the way out (unit)
        public Vec3 Normal(Vec3 p)
        {
            float e = h * 0.75f;
            var g = new Vec3(Dist(p + new Vec3(e, 0, 0)) - Dist(p - new Vec3(e, 0, 0)), Dist(p + new Vec3(0, e, 0)) - Dist(p - new Vec3(0, e, 0)),
                             Dist(p + new Vec3(0, 0, e)) - Dist(p - new Vec3(0, 0, e)));
            float l = Length(g);
            if (l > 1e-6f) return g * (1f / l);
            Vec3 o = p - c;   // (flat there: straight out from the middle)
            return Length(o) > 1e-6f ? Normalize(o) : new Vec3(0, 1, 0);
        }
    }

    // one part of a drawn hand: its shape, where it is drawn (place may mirror) and the way back
    public struct HandPart { public HandShape shape; public Mat4 place, inv; }

    public static class HandsApart
    {
        const float ShapeVoxel = 0.003f;   // the grid's spacing (m)
        const int ShapePad = 3;            // its border, in points
        const float PointCell = 0.010f;    // one surface point per cell this size
        const float Far = 1e20f;
        const float ApartTouch = 0.001f;   // deeper than this, they cross (m)
        const float ApartStep = 0.004f;
        public const float MaxShift = 0.06f;   // (3 cm each way)

        static Vec3 XfPoint(Mat4 m, Vec3 v) => new Vec3(m.m[0] * v.x + m.m[4] * v.y + m.m[8] * v.z + m.m[12], m.m[1] * v.x + m.m[5] * v.y + m.m[9] * v.z + m.m[13],
                                                        m.m[2] * v.x + m.m[6] * v.y + m.m[10] * v.z + m.m[14]);
        static Vec3 XfDir(Mat4 m, Vec3 v) => new Vec3(m.m[0] * v.x + m.m[4] * v.y + m.m[8] * v.z, m.m[1] * v.x + m.m[5] * v.y + m.m[9] * v.z,
                                                      m.m[2] * v.x + m.m[6] * v.y + m.m[10] * v.z);

        public static Mat4 AffineInverse(Mat4 mm)
        {
            float[] a = mm.m;   // column-major 3x3 + translation
            float c00 = a[5] * a[10] - a[9] * a[6], c01 = a[8] * a[6] - a[4] * a[10], c02 = a[4] * a[9] - a[8] * a[5];
            float det = a[0] * c00 + a[1] * c01 + a[2] * c02;
            float id = Math.Abs(det) > 1e-12f ? 1f / det : 0f;
            Mat4 r = Mat4.Identity();
            r.m[0] = c00 * id; r.m[4] = c01 * id; r.m[8] = c02 * id;
            r.m[1] = (a[9] * a[2] - a[1] * a[10]) * id; r.m[5] = (a[0] * a[10] - a[8] * a[2]) * id; r.m[9] = (a[8] * a[1] - a[0] * a[9]) * id;
            r.m[2] = (a[1] * a[6] - a[5] * a[2]) * id; r.m[6] = (a[4] * a[2] - a[0] * a[6]) * id; r.m[10] = (a[0] * a[5] - a[4] * a[1]) * id;
            for (int i = 0; i < 3; ++i) r.m[12 + i] = -(r.m[i] * a[12] + r.m[4 + i] * a[13] + r.m[8 + i] * a[14]);
            return r;
        }

        // the exact squared distance transform along one line (Felzenszwalb and Huttenlocher), in place: f holds 0 at the
        // features and Far elsewhere
        static void EdtLine(float[] f, int n, float[] g, int[] v, float[] z)
        {
            int first = 0;
            while (first < n && f[first] >= Far) ++first;
            if (first == n) return;   // (nothing near on this line yet: it stays far)
            Array.Copy(f, g, n);
            int k = 0;
            v[0] = first; z[0] = -1e30f; z[1] = 1e30f;
            for (int q = first + 1; q < n; ++q)
            {
                if (g[q] >= Far) continue;
                float s = ((g[q] + (float)q * q) - (g[v[k]] + (float)v[k] * v[k])) / (2f * (q - v[k]));
                while (k > 0 && s <= z[k])
                {
                    --k;
                    s = ((g[q] + (float)q * q) - (g[v[k]] + (float)v[k] * v[k])) / (2f * (q - v[k]));
                }
                ++k;
                v[k] = q; z[k] = s; z[k + 1] = 1e30f;
            }
            k = 0;
            for (int q = 0; q < n; ++q)
            {
                while (z[k + 1] < q) ++k;
                f[q] = (float)(q - v[k]) * (q - v[k]) + g[v[k]];
            }
        }

        // squared distance (in grid steps) from every point to the nearest feature point
        static float[] Edt3(byte[] feature, int[] n)
        {
            int N = n[0] * n[1] * n[2];
            var f = new float[N];
            for (int i = 0; i < N; ++i) f[i] = feature[i] != 0 ? 0f : Far;
            int mx = Math.Max(n[0], Math.Max(n[1], n[2]));
            float[] line = new float[mx], g = new float[mx], z = new float[mx + 1];
            int[] v = new int[mx];
            int[] stride = { 1, n[0], n[0] * n[1] };
            for (int ax = 0; ax < 3; ++ax)
            {
                int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                for (int j = 0; j < n[a1]; ++j)
                    for (int k = 0; k < n[a2]; ++k)
                    {
                        int b = j * stride[a1] + k * stride[a2];
                        for (int i = 0; i < n[ax]; ++i) line[i] = f[b + i * stride[ax]];
                        EdtLine(line, n[ax], g, v, z);
                        for (int i = 0; i < n[ax]; ++i) f[b + i * stride[ax]] = line[i];
                    }
            }
            return f;
        }

        // from a model's vertex positions (native axes) and triangles, placed by toFrame. A point is inside when the
        // surface lies on at least `sides` of its six axis directions (5 for a fist, whose plates have gaps between them;
        // 4 for a tube open at both ends, such as a cuff)
        public static HandShape Build(IList<Vec3> verts, IList<int> idx, Mat4 toFrame, int sides)
        {
            var s = new HandShape();
            int nv = verts.Count, ni = idx.Count;
            if (nv <= 0 || ni < 3) return s;
            var P = new Vec3[nv];
            Vec3 lo = new Vec3(1e9f, 1e9f, 1e9f), hi = new Vec3(-1e9f, -1e9f, -1e9f);
            for (int i = 0; i < nv; ++i)
            {
                P[i] = XfPoint(toFrame, verts[i]);
                lo = new Vec3(Math.Min(lo.x, P[i].x), Math.Min(lo.y, P[i].y), Math.Min(lo.z, P[i].z));
                hi = new Vec3(Math.Max(hi.x, P[i].x), Math.Max(hi.y, P[i].y), Math.Max(hi.z, P[i].z));
            }
            float h = ShapeVoxel;
            s.h = h;
            s.lo = lo - new Vec3(h, h, h) * ShapePad;
            Vec3 span = hi - lo;
            s.n[0] = (int)MathF.Ceiling(span.x / h) + 2 * ShapePad + 1;
            s.n[1] = (int)MathF.Ceiling(span.y / h) + 2 * ShapePad + 1;
            s.n[2] = (int)MathF.Ceiling(span.z / h) + 2 * ShapePad + 1;
            if (s.n[0] * s.n[1] * s.n[2] > 400000) return new HandShape();   // (not a hand)
            int[] n = s.n;
            int N = n[0] * n[1] * n[2];
            int At(int x, int y, int z) => (z * n[1] + y) * n[0] + x;
            // the surface: each triangle sampled half a step apart, its grid points marked; one surface point kept for
            // each centimetre cell (the first one sampled there)
            var surf = new byte[N];
            var samples = new List<Vec3>();
            int[] cells = { (int)(span.x / PointCell) + 1, (int)(span.y / PointCell) + 1, (int)(span.z / PointCell) + 1 };
            var cellTaken = new byte[cells[0] * cells[1] * cells[2]];
            for (int t = 0; t + 2 < ni; t += 3)
            {
                if (idx[t] >= nv || idx[t + 1] >= nv || idx[t + 2] >= nv || idx[t] < 0 || idx[t + 1] < 0 || idx[t + 2] < 0) continue;
                Vec3 A = P[idx[t]], B = P[idx[t + 1]], C = P[idx[t + 2]];
                float e = Math.Max(Length(B - A), Math.Max(Length(C - B), Length(A - C)));
                int m = Math.Max(1, Math.Min(200, (int)MathF.Ceiling(e / (h * 0.5f))));
                for (int i = 0; i <= m; ++i)
                    for (int j = 0; i + j <= m; ++j)
                    {
                        float u = (float)i / m, w = (float)j / m;
                        Vec3 p = A * u + B * w + C * (1f - u - w);
                        int gx = (int)MathF.Floor((p.x - s.lo.x) / h + 0.5f), gy = (int)MathF.Floor((p.y - s.lo.y) / h + 0.5f),
                            gz = (int)MathF.Floor((p.z - s.lo.z) / h + 0.5f);
                        if (gx < 0 || gy < 0 || gz < 0 || gx >= n[0] || gy >= n[1] || gz >= n[2]) continue;
                        surf[At(gx, gy, gz)] = 1;
                        int cx = Math.Min(cells[0] - 1, Math.Max(0, (int)((p.x - lo.x) / PointCell)));
                        int cy = Math.Min(cells[1] - 1, Math.Max(0, (int)((p.y - lo.y) / PointCell)));
                        int cz = Math.Min(cells[2] - 1, Math.Max(0, (int)((p.z - lo.z) / PointCell)));
                        int ci = (cz * cells[1] + cy) * cells[0] + cx;
                        if (cellTaken[ci] == 0) { cellTaken[ci] = 1; samples.Add(p); }
                    }
            }
            // inside: the surface on at least `sides` of the six directions along the axes
            var count = new byte[N];
            var cc = new int[3];
            for (int ax = 0; ax < 3; ++ax)
            {
                int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                for (int j = 0; j < n[a1]; ++j)
                    for (int k = 0; k < n[a2]; ++k)
                    {
                        cc[a1] = j; cc[a2] = k;
                        bool seen = false;
                        for (int i = 0; i < n[ax]; ++i) { cc[ax] = i; int q = At(cc[0], cc[1], cc[2]); seen |= surf[q] != 0; if (seen) ++count[q]; }
                        seen = false;
                        for (int i = n[ax] - 1; i >= 0; --i) { cc[ax] = i; int q = At(cc[0], cc[1], cc[2]); seen |= surf[q] != 0; if (seen) ++count[q]; }
                    }
            }
            var solid = new byte[N];
            var empty = new byte[N];
            for (int q = 0; q < N; ++q) { solid[q] = (byte)(surf[q] != 0 || count[q] >= sides ? 1 : 0); empty[q] = (byte)(solid[q] != 0 ? 0 : 1); }
            float[] toSolid = Edt3(solid, n), toEmpty = Edt3(empty, n);
            s.d = new sbyte[N];
            for (int q = 0; q < N; ++q)
            {   // (zero through the outermost solid points: the surface runs through them)
                float mm = solid[q] != 0 ? -(MathF.Sqrt(toEmpty[q]) - 1f) * h * 1000f : MathF.Sqrt(toSolid[q]) * h * 1000f;
                s.d[q] = (sbyte)Math.Max(-120f, Math.Min(120f, MathF.Round(mm, MidpointRounding.AwayFromZero)));
            }
            // the surface points on its outside (not those within it, such as the dark grip inside the fist)
            foreach (Vec3 p in samples)
                if (s.Dist(p) > -h * 1.5f) s.pts.Add(p);
            if (s.pts.Count == 0) return new HandShape();
            Vec3 plo = new Vec3(1e9f, 1e9f, 1e9f), phi = new Vec3(-1e9f, -1e9f, -1e9f);
            foreach (Vec3 p in s.pts)
            {
                plo = new Vec3(Math.Min(plo.x, p.x), Math.Min(plo.y, p.y), Math.Min(plo.z, p.z));
                phi = new Vec3(Math.Max(phi.x, p.x), Math.Max(phi.y, p.y), Math.Max(phi.z, p.z));
            }
            s.c = (plo + phi) * 0.5f;
            foreach (Vec3 p in s.pts) s.r = Math.Max(s.r, Length(p - s.c));
            s.r += h;
            return s;
        }

        // Build from a whole model file (.mdl, the open art's own); an invalid shape when it isn't one
        public static HandShape FromModel(byte[] data, Mat4 toFrame, int sides)
        {
            if (data == null || data.Length < 24 || data[0] != 'S' || data[1] != 'M' || data[2] != 'D' || data[3] != 'L') return new HandShape();
            uint ver = BitConverter.ToUInt32(data, 4), nv = BitConverter.ToUInt32(data, 8), ni = BitConverter.ToUInt32(data, 12);
            int vsz = ver >= 3 ? 52 : 44;
            if (data.Length < 24 + (long)nv * vsz + (long)ni * 4) return new HandShape();
            var verts = new Vec3[nv];
            for (int i = 0; i < nv; ++i)
            {
                int o = 24 + i * vsz;
                verts[i] = new Vec3(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8));
            }
            var idx = new int[ni];
            for (int i = 0; i < ni; ++i) idx[i] = (int)BitConverter.ToUInt32(data, 24 + (int)nv * vsz + i * 4);
            return Build(verts, idx, toFrame, sides);
        }

        // how deep any surface point of either hand is inside the other, with hand b moved S against hand a; `vote` (when
        // given) adds up each one's way out for hand b, by its depth; it stops early once one is deeper than `enough`
        static float ApartDepth(HandPart[] a, int na, HandPart[] b, int nb, Vec3 S, float enough)
        {
            float deepest = 0;
            for (int i = 0; i < na; ++i)
                for (int j = 0; j < nb; ++j)
                {
                    HandPart A = a[i], B = b[j];
                    if (A.shape == null || B.shape == null || !A.shape.Valid || !B.shape.Valid) continue;
                    Vec3 ca = XfPoint(A.place, A.shape.c), cb = XfPoint(B.place, B.shape.c) + S;
                    if (Length(ca - cb) > A.shape.r + B.shape.r) continue;
                    foreach (Vec3 p in A.shape.pts)
                    {   // a's surface inside b
                        Vec3 w = XfPoint(A.place, p);
                        if (Length(w - cb) > B.shape.r) continue;
                        float d = B.shape.Dist(XfPoint(B.inv, w - S));
                        if (d >= 0) continue;
                        deepest = Math.Max(deepest, -d);
                        if (deepest > enough) return deepest;
                    }
                    foreach (Vec3 p in B.shape.pts)
                    {   // b's surface inside a
                        Vec3 w = XfPoint(B.place, p) + S;
                        if (Length(w - ca) > A.shape.r) continue;
                        float d = A.shape.Dist(XfPoint(A.inv, w));
                        if (d >= 0) continue;
                        deepest = Math.Max(deepest, -d);
                        if (deepest > enough) return deepest;
                    }
                }
            return deepest;
        }

        // the least distance along D from which, out to maxShift, the hands stay apart (stepped, then halved down); more
        // than maxShift when they don't come apart within it
        static float ApartAlong(HandPart[] a, int na, HandPart[] b, int nb, Vec3 D, float maxShift)
        {
            float last = 0;   // the furthest step at which they still cross (0: only where they are)
            for (float t = ApartStep; t < maxShift + ApartStep * 0.5f; t += ApartStep)
                if (ApartDepth(a, na, b, nb, D * t, ApartTouch) > ApartTouch) last = t;
            if (last >= maxShift - ApartStep * 0.5f) return maxShift * 2f;
            float lo = last, hi = Math.Min(maxShift, last + ApartStep);
            for (int i = 0; i < 3; ++i)
            {
                float mid = (lo + hi) * 0.5f;
                if (ApartDepth(a, na, b, nb, D * mid, ApartTouch) > ApartTouch) lo = mid; else hi = mid;
            }
            return hi;
        }

        // the closest points of segments p0-p1 and q0-q1
        static void ClosestOnSegments(Vec3 p0, Vec3 p1, Vec3 q0, Vec3 q1, out Vec3 cp, out Vec3 cq)
        {
            Vec3 d1 = p1 - p0, d2 = q1 - q0, r = p0 - q0;
            float a = Dot(d1, d1), e = Dot(d2, d2), f = Dot(d2, r);
            float s = 0, t = 0;
            if (a < 1e-10f && e < 1e-10f) { cp = p0; cq = q0; return; }
            if (a < 1e-10f) t = Clampf(f / e, 0f, 1f);
            else
            {
                float c = Dot(d1, r);
                if (e < 1e-10f) s = Clampf(-c / a, 0f, 1f);
                else
                {
                    float b = Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-10f ? Clampf((b * f - c * e) / den, 0f, 1f) : 0.5f;   // (parallel: their middles)
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Clampf(-c / a, 0f, 1f); }
                    else if (t > 1) { t = 1; s = Clampf((b - c) / a, 0f, 1f); }
                }
            }
            cp = p0 + d1 * s;
            cq = q0 + d2 * t;
        }

        // how far to move hand b (hand a moving the other way: half each) so that no surface point of either is inside
        // the other, at most maxShift; zero when they don't meet. The way apart: each hand's middle line (from its fist's
        // middle to its cuff's), and the way from the nearest point of one to the other's: across when the hands are side
        // by side, over or under when the wrists cross (the way they already are); along it, the least distance from
        // which they stay apart
        public static Vec3 Shift(HandPart[] a, int na, HandPart[] b, int nb, float maxShift)
        {
            if (na <= 0 || nb <= 0 || a[0].shape == null || b[0].shape == null || ApartDepth(a, na, b, nb, new Vec3(), ApartTouch) <= ApartTouch)
                return new Vec3();
            void Line(HandPart[] hp, int n, out Vec3 p0, out Vec3 p1)
            {
                p0 = XfPoint(hp[0].place, hp[0].shape.c);
                p1 = n > 1 && hp[1].shape != null && hp[1].shape.Valid ? XfPoint(hp[1].place, hp[1].shape.c) : p0;
            }
            Line(a, na, out Vec3 a0, out Vec3 a1);
            Line(b, nb, out Vec3 b0, out Vec3 b1);
            ClosestOnSegments(a0, a1, b0, b1, out Vec3 ca, out Vec3 cb);
            Vec3 D = cb - ca;
            if (Length(D) < 0.001f)
            {   // (the middle lines meet: across both, the way from one fist to the other)
                D = Cross(a1 - a0, b1 - b0);
                if (Dot(D, b0 - a0) < 0) D = D * -1f;
                if (Length(D) < 1e-6f) D = b0 - a0;
                if (Length(D) < 1e-6f) D = new Vec3(1, 0, 0);
            }
            D = Normalize(D);
            return D * Math.Min(maxShift, ApartAlong(a, na, b, nb, D, maxShift));
        }

        // a hand prop's model space -> the grip (gltf metres -> UE centimetres -> the UE motion controller's transform ->
        // the OpenXR grip: native avatar.cpp ueLocalToGrip); the gauntlets are drawn with LancerLocal
        public static Mat4 UeLocalToGrip(Vec3 locCm, Vec3 pyrDeg, Vec3 scale)
        {
            float p = Deg2Rad(pyrDeg.x), y = Deg2Rad(pyrDeg.y), r = Deg2Rad(pyrDeg.z);
            float sp = MathF.Sin(p), cp = MathF.Cos(p), sy = MathF.Sin(y), cy = MathF.Cos(y), sr = MathF.Sin(r), cr = MathF.Cos(r);
            float[,] R = { { cp * cy, cp * sy, sp }, { sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp }, { -(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp } };
            float[] sc = { scale.x, scale.y, scale.z };
            Mat4 T = Mat4.Identity();
            for (int c = 0; c < 3; ++c) for (int rr = 0; rr < 3; ++rr) T.m[c * 4 + rr] = R[c, rr] * sc[c];
            T.m[12] = locCm.x; T.m[13] = locCm.y; T.m[14] = locCm.z;
            var S = new Mat4();   // gltf (x,y,z) m -> UE (x,z,y) cm
            S.m[0] = 100; S.m[6] = 100; S.m[9] = 100; S.m[15] = 1;
            var C = new Mat4();   // UE cm -> grip (y, z, -x) m
            C.m[2] = -0.01f; C.m[4] = 0.01f; C.m[9] = 0.01f; C.m[15] = 1;
            return C * T * S;
        }
        public static readonly Mat4 LancerLocal = UeLocalToGrip(new Vec3(1, -0.5f, 0), new Vec3(5, 0, 0), new Vec3(1, 1, 1));

        // the deepest any surface point of either hand is inside the other (m), with hand b moved S against hand a
        public static float Depth(HandPart[] a, int na, HandPart[] b, int nb, Vec3 S) => ApartDepth(a, na, b, nb, S, 1e9f);

        // both hands' parts as a player's gauntlets are drawn (the left mirrored): the fist turned in line with the cuff
        // (Avatar.FistPose), the cuff in its tuned pose; plate false: the classic lancer alone
        public static void Parts(Pose grip0, Pose grip1, HandShape fist, HandShape cuff, bool plate, HandPart[][] P)
        {
            for (int h = 0; h < 2; ++h)
            {
                Mat4 mirror = Mat4.Identity();
                if (h == 0) mirror.m[0] = -1;
                Mat4 gm = Mat4.FromPose(h == 0 ? grip0 : grip1) * mirror;
                P[h][0].shape = fist;
                P[h][0].place = plate ? gm * Mat4.FromPose(Avatar.FistPose()) : gm;
                P[h][1].shape = cuff;
                P[h][1].place = gm * Mat4.FromPose(Avatar.CuffPose(false));
                for (int k = 0; k < 2; ++k) P[h][k].inv = AffineInverse(P[h][k].place);
            }
        }
    }
}
