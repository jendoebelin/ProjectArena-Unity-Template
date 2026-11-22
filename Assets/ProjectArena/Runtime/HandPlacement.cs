using ProjectArena.Sim;
using UnityEngine;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // Where the hand models sit, as the native main.cpp / avatar.cpp place them: 4x4 matrices in native (right-handed)
    // space, column vectors as the native Mat4 (Unity's Matrix4x4 uses the same convention). The models' vertices are in
    // the original's gltf mesh space; ueLocalToGrip takes them through UE centimetres and the UE motion-controller
    // transform into OpenXR grip space. Its two axis swaps are mirror images, so the matrices are shown with Apply,
    // which allows a negative scale.
    public static class HandPlacement
    {
        // gltf mesh space -> UE cm -> UE motion-controller transform -> OpenXR grip space (native ueLocalToGrip)
        public static Matrix4x4 UeLocalToGrip(Vec3 locCm, Vec3 pyrDeg, Vec3 scale)
        {
            float p = M.Deg2Rad(pyrDeg.x), y = M.Deg2Rad(pyrDeg.y), r = M.Deg2Rad(pyrDeg.z);
            float sp = Mathf.Sin(p), cp = Mathf.Cos(p), sy = Mathf.Sin(y), cy = Mathf.Cos(y), sr = Mathf.Sin(r), cr = Mathf.Cos(r);
            float[,] R =
            {
                { cp * cy, cp * sy, sp },
                { sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp },
                { -(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp },
            };
            var T = Matrix4x4.identity;
            for (int c = 0; c < 3; ++c) for (int rr = 0; rr < 3; ++rr) T[rr, c] = R[c, rr] * scale[c];   // (native T.m[c * 4 + rr])
            T[0, 3] = locCm.x; T[1, 3] = locCm.y; T[2, 3] = locCm.z;
            var S = new Matrix4x4();   // gltf (x,y,z) m -> UE (x,z,y) cm
            S[0, 0] = 100; S[2, 1] = 100; S[1, 2] = 100; S[3, 3] = 1;
            var C = new Matrix4x4();   // UE cm -> grip (y, z, -x) m
            C[2, 0] = -0.01f; C[0, 1] = 0.01f; C[1, 2] = 0.01f; C[3, 3] = 1;
            return C * T * S;
        }

        // the gauntlet (1, -0.5, 0) cm, pitch 5; the shield plate (20, 0, 10) cm, roll 180, scale 10;
        // the knuckle guard (10.5, 0, 0) cm
        public static readonly Matrix4x4 LancerLocal = UeLocalToGrip(new Vec3(1, -0.5f, 0), new Vec3(5, 0, 0), new Vec3(1, 1, 1));
        public static readonly Matrix4x4 KnuckleLocal = UeLocalToGrip(new Vec3(1.5f, 0, 0), default, new Vec3(1, 1, 1));   // the knuckle guard (10.5) + its glass (-9)
        public static readonly Matrix4x4 PlateLocal = UeLocalToGrip(default, new Vec3(0, 0, 180), new Vec3(10, 10, 10));   // the shield plate centred, normal = grip Y

        // left hand: mirror across grip X (HandnessRoot)
        public static readonly Matrix4x4 Mirror = Matrix4x4.Scale(new Vector3(-1, 1, 1));

        public static Matrix4x4 FromPose(Pose p) =>
            Matrix4x4.TRS(new Vector3(p.p.x, p.p.y, p.p.z), new Quaternion(p.q.x, p.q.y, p.q.z, p.q.w), Vector3.one);

        // the plate gauntlets' cuff, moved and turned about the wrist joint (avatar.cpp cuffPose: Avatar.Tuning's)
        public static Pose CuffPose(bool left) => Avatar.CuffPose(left);

        // the plate gauntlets' fist turned 30 degrees about the knuckles, in line with the cuff and the forearm like the
        // hands (native c68f23e, owner 2026-11-16: "make the fist line up like the hands"; the controller's tilt had turned
        // it 30 degrees outward from the cuff, seen from above with the palm down), about the knuckle bumper as it was then;
        // then 2 cm toward the thumb and 2.5 cm back into the cuff's opening. Drawn only: the bumper stays where it is
        public static readonly Matrix4x4 FistLocal = FromPose(Avatar.FistPose()) * LancerLocal;   // (Avatar.FistPose)

        // the knuckle glass follows the knuckle bumper the owner moved onto the fist (main.cpp knuckleShift)
        public static Matrix4x4 KnuckleShift(Tunables t)
        {
            var glassCentre = new Vec3(-0.022f, 0.010f, -0.137f);
            return FromPose(new Pose(Quat.Identity, t.KnuckleOffsetNow() - glassCentre));   // (KNUCKLES ON THE FIST: the glass with the bumper)
        }

        // Shows a native-space model matrix on a transform (a root object): the importer negated Z in the vertices, so
        // Unity's matrix is Sz * M * Sz; a mirrored matrix becomes a negative X scale.
        public static void Apply(Transform t, Matrix4x4 native)
        {
            var sz = Matrix4x4.Scale(new Vector3(1, 1, -1));
            Matrix4x4 m = sz * native * sz;
            Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
            float sx = c0.magnitude, sy = c1.magnitude, szl = c2.magnitude;
            if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0) sx = -sx;   // a mirror image
            t.SetPositionAndRotation(m.GetColumn(3), Quaternion.LookRotation(c2 / szl, c1 / sy));
            t.localScale = new Vector3(sx, sy, szl);
        }
    }

    // The default look's colour channels (none, primary, secondary, glow, skin, visor, hair, trim) for each team (2 = outside
    // the tank: the lobby, spectators, kTeamRGB[2]), as the
    // native avatar::channelColors gives them (computed from the native avatar.cpp; the avatar code itself and the
    // avatar builder come with phase 4).
    public static class DefaultLook
    {
        public static readonly Vector4[][] Channels =
        {
            new[]
            {
                new Vector4(1, 1, 1, 1), new Vector4(0.0302565172f, 0.0356143676f, 0.0477757566f, 1),
                new Vector4(0.399293333f, 0.431340218f, 0.487765223f, 1), new Vector4(0.115000002f, 0.804999948f, 1.14999998f, 1),
                new Vector4(0.7667436f, 0.464741141f, 0.311180532f, 1), new Vector4(0.00902149081f, 0.0118813347f, 0.0189129822f, 1),
                new Vector4(0.0328759141f, 0.0134733971f, 0.00658495678f, 1), new Vector4(0.469788015f, 0.493402064f, 0.538329542f, 1),
            },
            new[]
            {
                new Vector4(1, 1, 1, 1), new Vector4(0.0302565172f, 0.0356143676f, 0.0477757566f, 1),
                new Vector4(0.399293333f, 0.431340218f, 0.487765223f, 1), new Vector4(1.14999998f, 0.460000008f, 0.115000002f, 1),
                new Vector4(0.7667436f, 0.464741141f, 0.311180532f, 1), new Vector4(0.00902149081f, 0.0118813347f, 0.0189129822f, 1),
                new Vector4(0.0328759141f, 0.0134733971f, 0.00658495678f, 1), new Vector4(0.469788015f, 0.493402064f, 0.538329542f, 1),
            },
            new[]
            {
                new Vector4(1, 1, 1, 1), new Vector4(0.0302565172f, 0.0356143676f, 0.0477757566f, 1),
                new Vector4(0.399293333f, 0.431340218f, 0.487765223f, 1), new Vector4(0.977500021f, 0.977500021f, 1.03499997f, 1),
                new Vector4(0.7667436f, 0.464741141f, 0.311180532f, 1), new Vector4(0.00902149081f, 0.0118813347f, 0.0189129822f, 1),
                new Vector4(0.0328759141f, 0.0134733971f, 0.00658495678f, 1), new Vector4(0.469788015f, 0.493402064f, 0.538329542f, 1),
            },
        };
    }
}
