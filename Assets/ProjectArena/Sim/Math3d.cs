// Vector / quaternion maths of the native game (app/src/math3d.h), ported 1:1: float maths in the same order, so the
// simulation computes what the native one computes. App space: metres, right-handed, +Y up (native axes; drawing
// converts with ProjectArena.NativeSpace). Mat4 is the native one, for the avatar rig.
// C# structs start zeroed: a default Quat is (0, 0, 0, 0), not the identity the native default is. Use Quat.Identity /
// Pose.Identity wherever the native code relies on Quat{} / Pose{}.
using System;

namespace ProjectArena.Sim
{
    public struct Vec2
    {
        public float x, y;
    }

    public struct Vec3
    {
        public float x, y, z;

        public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static Vec3 operator +(Vec3 a, Vec3 o) => new Vec3(a.x + o.x, a.y + o.y, a.z + o.z);
        public static Vec3 operator -(Vec3 a, Vec3 o) => new Vec3(a.x - o.x, a.y - o.y, a.z - o.z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.x, -a.y, -a.z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static Vec3 operator *(float s, Vec3 v) => v * s;
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.x / s, a.y / s, a.z / s);

        // the native code walks x, y, z through a float pointer (&v.x)[i]
        public float this[int i]
        {
            get => i == 0 ? x : i == 1 ? y : z;
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }

        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }

    public struct Quat
    {
        public float x, y, z, w;

        public Quat(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        public static Quat AxisAngle(Vec3 axis, float rad)
        {
            Vec3 a = M.Normalize(axis);
            float s = MathF.Sin(rad * 0.5f);
            return new Quat(a.x * s, a.y * s, a.z * s, MathF.Cos(rad * 0.5f));
        }

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        public Quat Conj() => new Quat(-x, -y, -z, w);

        public Vec3 Rotate(Vec3 v)
        {
            Vec3 u = new Vec3(x, y, z);
            Vec3 t = M.Cross(u, v) * 2f;
            return v + t * w + M.Cross(u, t);
        }
    }

    public struct Pose
    {
        public Quat q;
        public Vec3 p;

        public Pose(Quat q, Vec3 p) { this.q = q; this.p = p; }

        public static readonly Pose Identity = new Pose(Quat.Identity, default);

        public Vec3 Transform(Vec3 v) => q.Rotate(v) + p;
        public static Pose operator *(Pose a, Pose child) => new Pose(a.q * child.q, a.Transform(child.p));
    }

    // Column-major 4x4 (OpenGL convention), the native Mat4 (used by the avatar rig's skin matrices). A class: the
    // native code builds new ones rather than changing them.
    public sealed class Mat4
    {
        public readonly float[] m = new float[16];

        public static Mat4 Identity() { var r = new Mat4(); r.m[0] = r.m[5] = r.m[10] = r.m[15] = 1; return r; }

        public static Mat4 operator *(Mat4 a, Mat4 b)
        {
            var r = new Mat4();
            for (int c = 0; c < 4; ++c)
                for (int rr = 0; rr < 4; ++rr)
                {
                    float s = 0;
                    for (int k = 0; k < 4; ++k) s += a.m[k * 4 + rr] * b.m[c * 4 + k];
                    r.m[c * 4 + rr] = s;
                }
            return r;
        }

        public static Mat4 FromPose(Pose p) => FromPose(p, new Vec3(1, 1, 1));

        public static Mat4 FromPose(Pose p, Vec3 scale)
        {
            Quat q = p.q;
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z, wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
            var r = new Mat4();
            r.m[0] = (1 - 2 * (yy + zz)) * scale.x; r.m[1] = 2 * (xy + wz) * scale.x;       r.m[2] = 2 * (xz - wy) * scale.x;
            r.m[4] = 2 * (xy - wz) * scale.y;       r.m[5] = (1 - 2 * (xx + zz)) * scale.y; r.m[6] = 2 * (yz + wx) * scale.y;
            r.m[8] = 2 * (xz + wy) * scale.z;       r.m[9] = 2 * (yz - wx) * scale.z;       r.m[10] = (1 - 2 * (xx + yy)) * scale.z;
            r.m[12] = p.p.x; r.m[13] = p.p.y; r.m[14] = p.p.z; r.m[15] = 1;
            return r;
        }

        // Columns x, y, z (any lengths / handedness) and origin o.
        public static Mat4 FromBasis(Vec3 x, Vec3 y, Vec3 z, Vec3 o)
        {
            var r = new Mat4();
            r.m[0] = x.x; r.m[1] = x.y; r.m[2] = x.z;
            r.m[4] = y.x; r.m[5] = y.y; r.m[6] = y.z;
            r.m[8] = z.x; r.m[9] = z.y; r.m[10] = z.z;
            r.m[12] = o.x; r.m[13] = o.y; r.m[14] = o.z; r.m[15] = 1;
            return r;
        }

        // a point through the matrix (the native tests' xform)
        public Vec3 TransformPoint(Vec3 p) => new Vec3(m[0] * p.x + m[4] * p.y + m[8] * p.z + m[12], m[1] * p.x + m[5] * p.y + m[9] * p.z + m[13],
                                                       m[2] * p.x + m[6] * p.y + m[10] * p.z + m[14]);
    }

    // the native free functions (dot, cross, length, ...)
    public static class M
    {
        public const float kPi = 3.14159265358979f;

        public static float Dot(Vec3 a, Vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Length(Vec3 v) => MathF.Sqrt(Dot(v, v));
        public static float Length2(Vec3 v) => Dot(v, v);
        public static Vec3 Normalize(Vec3 v) { float l = Length(v); return l > 1e-8f ? v / l : new Vec3(0, 0, 0); }
        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;
        // std::max / std::min exactly (a NaN or a signed zero comes out as std:: gives it, unlike MathF.Max / Min)
        public static float Max(float a, float b) => a < b ? b : a;
        public static float Min(float a, float b) => b < a ? b : a;
        public static int Max(int a, int b) => a < b ? b : a;
        public static int Min(int a, int b) => b < a ? b : a;
        public static float Clampf(float v, float lo, float hi) => Max(lo, Min(hi, v));
        public static float Lerpf(float a, float b, float t) => a + (b - a) * t;
        public static float Deg2Rad(float d) => d * kPi / 180f;
        public static float Rad2Deg(float r) => r * 180f / kPi;

        // A grip-space rotation from pitch (about X), yaw (Y) and roll (Z) in degrees, applied yaw * pitch * roll. left:
        // its mirror image across grip X (left-hand parts are the right hand's mirrored: yaw and roll change sign).
        public static Quat GripEuler(Vec3 deg, bool left)
        {
            float s = left ? -1f : 1f;
            return Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(s * deg.y)) * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(deg.x)) *
                   Quat.AxisAngle(new Vec3(0, 0, 1), Deg2Rad(s * deg.z));
        }

        public static Quat Nlerp(Quat a, Quat b, float t)
        {
            if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0) b = new Quat(-b.x, -b.y, -b.z, -b.w);
            var r = new Quat(Lerpf(a.x, b.x, t), Lerpf(a.y, b.y, t), Lerpf(a.z, b.z, t), Lerpf(a.w, b.w, t));
            float l = MathF.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
            return new Quat(r.x / l, r.y / l, r.z / l, r.w / l);
        }

        // Shortest-arc rotation taking unit vector a to unit vector b.
        public static Quat FromTo(Vec3 a, Vec3 b)
        {
            Vec3 c = Cross(a, b);
            float d = Dot(a, b);
            if (d < -0.99999f)
            {
                Vec3 axis = MathF.Abs(a.x) < 0.9f ? Cross(a, new Vec3(1, 0, 0)) : Cross(a, new Vec3(0, 1, 0));
                return Quat.AxisAngle(axis, kPi);
            }
            float s = MathF.Sqrt((1 + d) * 2);
            return new Quat(c.x / s, c.y / s, c.z / s, s * 0.5f);
        }
    }
}
