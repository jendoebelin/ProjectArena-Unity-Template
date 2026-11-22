using UnityEngine;

namespace ProjectArena
{
    // The native game is right-handed (OpenGL: +Y up, models face -Z); Unity is left-handed. Native positions and
    // directions are shown in Unity with Z negated, so a native model's front (-Z) faces Unity's forward (+Z). The game
    // rules and network keep native coordinates; only drawing converts, here.
    public static class NativeSpace
    {
        public static Vector3 ToUnity(Vector3 v) => new Vector3(v.x, v.y, -v.z);
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, y, -z);
        public static Vector3 ToUnity(Sim.Vec3 v) => new Vector3(v.x, v.y, -v.z);
        // a rotation seen in the mirrored axes: axis (x, y, -z), angle reversed
        public static Quaternion ToUnity(Sim.Quat q) => new Quaternion(-q.x, -q.y, q.z, q.w);

        // a draw's scrolling texture offset (native DrawParams.uvOff) as Native.shader takes it: v flipped for Unity's
        // textures, both kept in [0, 1) (native wrapUV, 0.3.14's square-fireball fix: offsets growing with play time lost
        // their fractions in the headset's half-precision UVs, and the textures froze into squares)
        public static Vector4 WrapUV(Vector2 off) => new Vector4(Wrap(off.x), Wrap(-off.y), 0, 0);
        static float Wrap(float v) => v - Mathf.Floor(v);
    }
}
