// THE REACH and ARM SIZING (TASKS.md E10, native 74860ad and 79e7b28 test_game.cpp "[the reach]"; astrobleem's PR #25
// and issue #24, the owner's go 2026-11-17): the collarbone's skin carries the shoulder, no arm ever longer than its
// bones, a hand out of reach turns and leans the chest bending at the waist (the hips upright), never faster than 120
// degrees a second; ARM SIZING to the player's reach
using System;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public class ReachTests
    {
        struct HandPose { public Vec3 p; public float pitch, yaw; public HandPose(float x, float y, float z, float pi, float ya) { p = new Vec3(x, y, z); pitch = pi; yaw = ya; } }

        static PlayerInput Make(int body, HandPose l, HandPose r, out AvatarDesc d)
        {   // facing +Z, as the native tools/avatar_pose.cpp
            d = Avatar.PresetLook(0);
            d.body = (byte)body;
            Quat face = Quat.AxisAngle(new Vec3(0, 1, 0), kPi);
            var @in = new PlayerInput { headTracked = true, head = new Pose(face, new Vec3(0, 1.62f, 0)) };
            HandPose[] hp = { l, r };
            for (int h = 0; h < 2; ++h)
            {
                Quat q = face * Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(hp[h].yaw)) * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(hp[h].pitch));
                @in.hand[h].tracked = true;
                @in.hand[h].grip = new Pose(q, face.Rotate(new Vec3(hp[h].p.x, 0, -hp[h].p.z)) + new Vec3(0, hp[h].p.y, 0));
            }
            @in.avatar = d;
            return @in;
        }

        static readonly HandPose[] Ready = { new HandPose(-0.21f, 1.08f, 0.30f, -22, 8), new HandPose(0.21f, 1.08f, 0.30f, -22, -8) };
        static readonly HandPose Far = new HandPose(0.80f, 1.10f, 0.35f, 0, -60);   // the reach fixture of issue #24
        static readonly HandPose Overhead = new HandPose(0.25f, 2.15f, 0.10f, 80, 0);

        [Test]
        public void TheShoulderTheArmAndTheLean()
        {
            Avatar.RigTuning T = Avatar.Tuning;
            bool clavFollows = true, neverStretched = true, leans = true;
            for (int body = 0; body < 2; ++body)
                foreach (HandPose rp in new[] { Far, Overhead, Ready[1] })
                {
                    PlayerInput @in = Make(body, Ready[0], rp, out AvatarDesc d);
                    Avatar.Rig r = Avatar.SolveRig(@in, d);
                    Avatar.BodyBind K = Avatar.BodyBindOf(body);
                    for (int k = 0; k < 2; ++k)
                    {   // #24: the collarbone's skin carries the shoulder the arm hangs from (it slid away from it)
                        if (Length(r.skin[(int)Avatar.BoneId.ClavL + k].TransformPoint(K.shoulder[k]) - r.upper[k].a) > 0.001f + Length(T.shoulder)) clavFollows = false;
                        float L1 = Length(K.elbow[k] - K.shoulder[k]) * T.upperLen, L2 = Length(K.wrist[k] - K.elbow[k]) * T.foreLen;
                        if (Length(r.upper[k].b - r.upper[k].a) > L1 * 1.0005f || Length(r.fore[k].b - r.fore[k].a) > L2 * 1.0005f) neverStretched = false;
                    }
                    if (rp.p.x == Far.p.x)
                    {   // the torso turned and leant toward the far hand, bending at the waist: the hips stay upright
                        Vec3 up = r.torso.q.Rotate(new Vec3(0, 1, 0)), front = r.torso.q.Rotate(new Vec3(0, 0, -1));
                        float turned = Rad2Deg(MathF.Acos(Clampf(Dot(front, new Vec3(0, 0, 1)), -1f, 1f)));
                        float leant = Rad2Deg(MathF.Acos(Clampf(up.y, -1f, 1f)));
                        Vec3 hipsUp = Normalize(new Vec3(r.pelvis.m[4], r.pelvis.m[5], r.pelvis.m[6]));
                        TestContext.Out.WriteLine($"  (body {body}, the far reach: the torso turned {turned:F0} deg, leant {leant:F0}; the forearm {r.reachGap[1] * 1000:F0} mm short; the hips lean {Rad2Deg(MathF.Acos(Clampf(hipsUp.y, -1f, 1f))):F1} deg)");
                        leans &= turned + leant > 5f && leant > 5f && r.bent && hipsUp.y > 0.9995f;
                    }
                }
            Assert.That(clavFollows, "the collarbone's skin moves with the shoulder (issue #24: it stayed while the shoulder slid)");
            Assert.That(neverStretched, "no arm is ever longer than its bones, however far the hand");
            Assert.That(leans, "a hand out of reach turns and leans the chest toward it, bending at the waist: the hips stay upright");
            {   // a state stepped at 90 frames a second: the torso never turns faster than 120 degrees a second
                var st = new Avatar.RigState();
                double t = 0;
                float fastest = 0, gapEnd = 1;
                Quat prev = Quat.Identity;
                for (int i = 0; i < 180; ++i, t += 1.0 / 90)
                {
                    PlayerInput @in = Make(0, Ready[0], i < 30 ? Ready[1] : Far, out AvatarDesc d);
                    Avatar.StepRig(st, @in, d, t);
                    Avatar.Rig r = Avatar.SolveRig(@in, d, st);
                    if (i > 0)
                    {
                        float c = MathF.Abs(prev.x * r.torso.q.x + prev.y * r.torso.q.y + prev.z * r.torso.q.z + prev.w * r.torso.q.w);
                        fastest = MathF.Max(fastest, Rad2Deg(2f * MathF.Acos(Clampf(c, 0f, 1f))) * 90f);
                    }
                    prev = r.torso.q;
                    gapEnd = r.reachGap[1];
                }
                Avatar.Rig target = Avatar.SolveRig(Make(0, Ready[0], Far, out AvatarDesc d2), d2);
                TestContext.Out.WriteLine($"  (a hand thrown out to the far reach: the torso at most {fastest:F0} deg/s; after 1.5 s {gapEnd * 1000:F0} mm short, the moment's posture {target.reachGap[1] * 1000:F0})");
                Assert.That(fastest, Is.LessThanOrEqualTo(121f), "the torso follows smoothly, never faster than 120 degrees a second");
            }
        }

        // ARM SIZING: reaching again and again a little past the arm closes the gap; within reach, the arms stay
        [Test]
        public void ArmSizing()
        {
            // (the right elbow's bend at the reach: 0 a straight arm; TASKS.md E26, native 8a68443: a player's video,
            // 2026-11-17, arms fully extended in life, the avatar's elbows bent)
            float lastBend = 0;
            float BendOf(Avatar.Rig r)
            {
                Vec3 a = Normalize(r.upper[1].a - r.upper[1].b), b = Normalize(r.fore[1].b - r.fore[1].a);
                return 180f - MathF.Acos(Clampf(Dot(a, b), -1f, 1f)) * 57.2957795f;
            }
            float Play(int body, HandPose o, int glitchAt, out float gapStart, out float gapEnd)
            {
                var st = new Avatar.RigState();
                double t = 0;
                gapStart = -1; gapEnd = -1;
                for (int i = 0; i < 90 * 40; ++i, t += 1.0 / 90)
                {   // 40 s: a punch out and back every second
                    bool outNow = (i % 90) >= 40 && (i % 90) < 60;
                    PlayerInput @in = Make(body, Ready[0], outNow ? o : Ready[1], out AvatarDesc d);
                    if (i == glitchAt) @in.hand[1].grip.p = @in.hand[1].grip.p + new Vec3(2f, 0, 0);   // (tracking: one frame 2 m off)
                    Avatar.StepRig(st, @in, d, t);
                    Avatar.Rig r = Avatar.SolveRig(@in, d, st);
                    if (outNow && (i % 90) == 59) { if (gapStart < 0) gapStart = r.reachGap[1]; gapEnd = r.reachGap[1]; lastBend = BendOf(r); }
                }
                return st.armScale;
            }
            // a long-armed player's full reaches, past each body's arm and what its shoulder's swing covers (about 1.12 and
            // 1.16 the avatar's arms: real arms are 5 to 20% longer than these avatars')
            HandPose pastM = new HandPose(0.50f, 1.30f, 0.60f, 0, -40), pastF = new HandPose(0.45f, 1.30f, 0.50f, 0, -40);
            float mScale = Play(0, pastM, -1, out float g0, out float g1);
            float mBend = lastBend;
            float fScale = Play(1, pastF, -1, out float f0, out float f1);
            float fBend = lastBend;
            TestContext.Out.WriteLine($"  (the elbow at those full reaches: masculine bent {mBend:F0} deg, feminine {fBend:F0})");
            Assert.That(mBend < 8f && fBend < 8f, "ARM SIZING: a full reach is a straight arm, not bent at the elbow (both bodies)");
            float glitchScale = Play(0, Ready[1], 900, out _, out _);
            float farScale = Play(1, Far, -1, out float w0, out float w1);
            TestContext.Out.WriteLine($"  (reaching past the arm, 40 punches: masculine x{mScale:F2}, short {g0 * 1000:F0} mm at first, {g1 * 1000:F0} at the end; feminine x{fScale:F2}, {f0 * 1000:F0} -> {f1 * 1000:F0} mm)");
            TestContext.Out.WriteLine($"  (the far reach, feminine: x{farScale:F2}, {w0 * 1000:F0} -> {w1 * 1000:F0} mm short; within reach with a 2 m glitch: x{glitchScale:F3})");
            Assert.That(g1 < 0.005f && f1 < 0.005f, "ARM SIZING: reaching past the arm again and again, the arms lengthen until the forearm meets the gauntlet (both bodies)");
            // (the PR #25 bodies' arms are shorter than the MPFB bodies': the masculine 49 cm, x1.27 for this reach)
            Assert.That(mScale > 1.05f && mScale < 1.3f && fScale > 1.05f && fScale < 1.3f, "ARM SIZING: to the player's reach, not to the limit");
            Assert.That(farScale, Is.LessThanOrEqualTo(1.35f), "ARM SIZING: never more than 35% longer");
            Assert.That(glitchScale <= 1f && glitchScale >= 0.8f, "ARM SIZING: within reach the arms never lengthen (a tracking jump is no reach), at most 20% shorter");
            // the owner's mirror (TASKS.md E25, native 7ed11aa, 2026-11-17): arms that grew too long (or bodies whose arms
            // are longer than the player's) shorten to the reach the hands show, so a full reach is a straight arm; and a
            // hand across the body is no reach
            float Play2(int body, float startScale, HandPose o, float seconds, out float gapEnd)
            {
                var st = new Avatar.RigState();
                double t = 0;
                bool first = true;
                gapEnd = 0;
                for (int i = 0; i < (int)(90 * seconds); ++i, t += 1.0 / 90)
                {   // a reach out and back every second
                    bool outNow = (i % 90) >= 40 && (i % 90) < 60;
                    PlayerInput @in = Make(body, Ready[0], outNow ? o : Ready[1], out AvatarDesc d);
                    Avatar.StepRig(st, @in, d, t);
                    if (first) { st.armScale = startScale; st.reachPeak = startScale; first = false; }
                    Avatar.Rig r = Avatar.SolveRig(@in, d, st);
                    if (outNow && (i % 90) == 59) gapEnd = r.reachGap[1];
                }
                float scale = st.armScale;
                for (int i = 0; i < 90; ++i, t += 1.0 / 90)
                {   // (then the reach held a second, as in a mirror: the elbow)
                    PlayerInput @in = Make(body, Ready[0], o, out AvatarDesc d);
                    Avatar.StepRig(st, @in, d, t);
                    lastBend = BendOf(Avatar.SolveRig(@in, d, st));
                }
                return scale;
            }
            HandPose nearM = new HandPose(0.30f, 1.30f, 0.40f, 0, -30), across = new HandPose(-0.45f, 1.25f, 0.45f, 0, 40);
            float shortM = Play2(0, 1.25f, nearM, 90f, out float n1);
            float nBendM = lastBend;
            float shortF = Play2(1, 1.25f, nearM, 90f, out float n2);
            float nBendF = lastBend;
            float acrossScale = Play2(0, 1f, across, 40f, out _);
            // both arms straight up, as in the player's video: a hand 60 cm over the shoulder, again and again
            HandPose up = new HandPose(0.24f, 2.05f, 0.04f, 80, 0);
            Play2(0, 1f, up, 40f, out float u1);
            float upBendM = lastBend;
            Play2(1, 1f, up, 40f, out float u2);
            float upBendF = lastBend;
            TestContext.Out.WriteLine($"  (arms at x1.25, a shorter full reach for 90 s: masculine x{shortM:F2}, feminine x{shortF:F2}, short {n1 * 1000:F0} / {n2 * 1000:F0} mm at the reach, " +
                                      $"the elbow bent {nBendM:F0} / {nBendF:F0} deg; reaching across the body for 40 s: x{acrossScale:F2}; straight up: bent {upBendM:F0} / {upBendF:F0} deg, {u1 * 1000:F0} / {u2 * 1000:F0} mm short)");
            Assert.That(nBendM < 8f && nBendF < 8f && upBendM < 8f && upBendF < 8f,
                        "ARM SIZING: arms that shortened to the reach are straight at it; both arms straight up are straight (the elbows not bent)");
            Assert.That(shortM < 1.15f && shortF < 1.15f && shortM >= 0.8f && shortF >= 0.8f && n1 < 0.005f && n2 < 0.005f,
                        "ARM SIZING: arms longer than the reach shown shorten toward it (a full reach is a straight arm; it still meets the gauntlet)");
            Assert.That(acrossScale, Is.LessThanOrEqualTo(1f), "ARM SIZING: a hand reaching across the body never lengthens the arms");
        }
    }
}
