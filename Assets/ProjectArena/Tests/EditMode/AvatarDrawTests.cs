// The avatar art and drawing (Runtime/AvatarDraw.cs, Settings/AvatarLibrary.asset): every part a look names is there,
// the hair swing rigs and tuck maps load, hair settles at rest and swings when the head turns, avatar.cfg is kept.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectArena.Sim;
using UnityEditor;
using UnityEngine;
using static ProjectArena.Sim.M;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena.Tests
{
    public class AvatarDrawTests
    {
        static AvatarDraw s_av;
        static AvatarDraw Av => s_av ??= new AvatarDraw(AssetDatabase.LoadAssetAtPath<AvatarLibrary>("Assets/ProjectArena/Settings/AvatarLibrary.asset"),
                                                        AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader"));

        [Test]
        public void TheArtIsAllThere()
        {
            Assert.That(Av.PartCount, Is.EqualTo(238), "every av_*.mdl of the open set (E14: the PR #25 bodies, FACE 2-4 on them with their face shields and masks; VECTOR and NIGHTFORGE with their own shorts; E35: the BANDANA's tails, RONIN WRAP, HIGH PIGTAILS, DREADLOCKS, RONIN KABUTO, PELURU)");
            Assert.That(Av.SwingRigCount, Is.EqualTo(32), "what swings: 10 hair styles' rigs per body, the BANDANA's tails, its point on every face, the PELURU dress's earrings (E35)");
            Assert.That(Av.TuckCount, Is.EqualTo(34), "tuck maps: 10 headgear (E35: RONIN KABUTO, the SOMBRERO) and 7 face shields per body");
            Assert.That(Av.GrappleCount, Is.EqualTo(3), "the grapple gauntlets' fingers, hinges, fists and tether (E35: GRAPPLER, ARC CLAW, REGALIA)");
        }

        // FAR AWAY (TASKS.md E39, native fb93914 tools/make_lods.cpp): each part's lighter triangles over the same vertices,
        // about a quarter of them; an avatar beyond 3 m from the eye drawn with them, the builder's mirror never
        [Test]
        public void FarAway()
        {
            Assert.That(Av.FarParts, Is.GreaterThan(200), "nearly every avatar part has its lighter triangles (the kilt's pieces draw whole)");
            long whole = 0, far = 0;
            foreach (string n in new[] { "av_top_1_m", "av_bhead_f", "av_hair_10_m", "av_top_18_f", "av_gear_9_m" })
            {
                long w = Av.Triangles(n, false), f = Av.Triangles(n, true);
                Assert.That(w > 0 && f > 0 && f < w, n + ": lighter far away");
                whole += w; far += f;
            }
            TestContext.Out.WriteLine($"  (five parts: {whole} triangles, {far} far away: {100.0 * far / whole:F0}%)");
            Assert.That(far * 100 / whole, Is.InRange(15, 45), "about a quarter of the triangles (native: 29% over all the parts)");
            var look = Avatar.PresetLook(1);
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            var L = Av.LookOf(look, 0, side);
            PlayerInput At(float x) => new PlayerInput { headTracked = true, head = new Pose(Quat.Identity, new Vec3(x, 1.6f, 0)), avatar = look };
            Av.farOn = true;
            Av.eye = new Vec3(0, 1.6f, 0);
            Av.DrawBody(At(2.5f), L, 0, swingKey: 300);
            bool near = Av.lastFar;
            Av.DrawBody(At(5f), L, 0, swingKey: 301);
            bool away = Av.lastFar;
            var mirror = Mat4.Identity();
            mirror.m[0] = -1;   // (a reflection, as the builder's mirror)
            Av.DrawBody(At(5f), L, 0, mirror, swingKey: 302);
            bool mirrored = Av.lastFar;
            Av.farOn = false;
            Av.DrawBody(At(5f), L, 0, swingKey: 303);
            bool off = Av.lastFar;
            Assert.That(!near && away && !mirrored && !off, "within 3 m whole, beyond it lighter; the mirror (and FAR AWAY off) whole");
        }

        [Test]
        public void EveryLookFindsItsParts()
        {
            var @in = new PlayerInput { headTracked = true, head = new Pose(Quat.Identity, new Vec3(0, 1.6f, 0)) };
            var looks = new List<AvatarDesc>();
            for (int i = 0; i < Avatar.PresetCount(); ++i) looks.Add(Avatar.PresetLook(i));
            for (uint seed = 1; seed < 300; ++seed) looks.Add(Avatar.RandomLook(seed));
            for (int f = 0; f < Avatar.kFaces; ++f)
                for (int b = 0; b < 2; ++b)
                    for (int v = 0; v < Avatar.FieldCount(Avatar.Field.Visor); ++v)
                    {   // every face shield and mask on every face (the native game falls back to the base head's when one is missing)
                        var d = AvatarDesc.Default;
                        Avatar.Set(ref d, Avatar.Field.Body, b);
                        Avatar.Set(ref d, Avatar.Field.Face, f);
                        d.visor = (byte)v;
                        d.mask = (byte)(v % Avatar.FieldCount(Avatar.Field.Mask));
                        looks.Add(d);
                    }
            var missing = new SortedSet<string>();
            var list = new List<Avatar.PartDraw>();
            foreach (var d in looks)
            {
                list.Clear();
                Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                foreach (var p in list)
                {   // (a face's own part missing draws the base head's, as natively: the PR #25 bodies have one face each, so
                    // FACE 2-4 and their face shields and masks use the base ones, native 5992aae)
                    string n = p.model;
                    if (!Av.HasPart(n) && n.Length > 2 && n[n.Length - 1] >= '1' && n[n.Length - 1] <= '9' && (n[n.Length - 2] == 'm' || n[n.Length - 2] == 'f'))
                        n = n.Substring(0, n.Length - 1);
                    if (!Av.HasPart(n)) missing.Add(p.model);
                }
            }
            Assert.That(missing, Is.Empty, "parts named by a look but not in the library");
        }

        [Test]
        public void HairSettlesAndSwings()
        {
            var head = new Pose(Quat.Identity, new Vec3(0, 1.6f, 0));
            double t = 100;
            Mat4[] b = null;
            for (int i = 0; i < 200; ++i) b = Av.HairBones("av_hair_3t_f", 7, head, t += 0.011);
            Assert.That(b, Is.Not.Null, "the ponytail swings");
            float Off(Mat4[] bones)
            {   // how far the bones move their rest points (the tail's end, about 30 cm from its root)
                float worst = 0;
                foreach (var m in bones)
                    worst = Max(worst, Length(m.TransformPoint(new Vec3(0, -0.3f, 0.2f)) - new Vec3(0, -0.3f, 0.2f)));
                return worst;
            }
            Assert.That(Off(b), Is.LessThan(0.01f), "standing still, the hair hangs at rest");
            // a quick head turn: the tail lags behind, then settles again
            float most = 0;
            for (int i = 0; i < 20; ++i)
            {
                var turned = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(60f * (i + 1) / 20f)), head.p);
                most = Max(most, Off(Av.HairBones("av_hair_3t_f", 7, turned, t += 0.011)));
            }
            Assert.That(most, Is.GreaterThan(0.02f), "a head turn swings the tail");
            var held = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), Deg2Rad(60f)), head.p);
            for (int i = 0; i < 400; ++i) b = Av.HairBones("av_hair_3t_f", 7, held, t += 0.011);
            Assert.That(Off(b), Is.LessThan(0.01f), "and it settles at rest");
            Assert.That(Av.HairBones("av_hair_1_m", 7, head, t), Is.Null, "the buzz cut does not move");
        }

        [Test]
        public void LooksCarryTheTeamAndTheCountry()
        {
            float[][] side = { new[] { 0.1f, 0.7f, 1.0f }, new[] { 1.0f, 0.4f, 0.1f } };
            AvatarDraw.Look L = Av.LookOf(Avatar.PresetLook(10), 0, side);
            Assert.That(L.rim, Is.EqualTo(new Vector4(0.1f, 0.7f, 1.0f, 0.5f)), "the blue end's rim light");
            Assert.That(L.ch.Length, Is.EqualTo((int)Avatar.Channel.Count));
            AvatarDesc d = Avatar.PresetLook(1);
            Avatar.SetNation(ref d, 7);
            Assert.That(Av.LookOf(d, 1, side).rim.w, Is.EqualTo(0.25f), "a country's kit: a softer rim");
            Assert.That(Av.LookOf(Avatar.PresetLook(1), 1, side).pattern, Is.Not.Null, "ROOKIE wears the STRIPES pattern");
        }

        [Test]
        public void AvatarCfgKeptAndAFirstLookMade()
        {
            string folder = Path.Combine(Path.GetTempPath(), "pa_avatar_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var s = new AppSettings(folder);
                s.LoadAvatar(1234);
                Assert.That(File.Exists(Path.Combine(folder, "avatar.cfg")), "a first run saves its random look");
                Assert.That(s.myAvatar, Is.EqualTo(Avatar.RandomLook(1234)), "the random look of the seed (no name yet)");
                AvatarDesc d = Avatar.PresetLook(4);
                Avatar.SetNation(ref d, 30);
                s.myAvatar = d;
                s.SaveAvatar();
                var r = new AppSettings(folder);
                r.LoadAvatar(99);
                Assert.That(r.myAvatar, Is.EqualTo(d), "avatar.cfg read back");
                Assert.That(r.botAvatar, Is.Not.EqualTo(r.myAvatar));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        // NATIONS flags printed on the jersey (task C12, native 0.3.17): each print box finds cloth to print on, the
        // back's on the back and the chest's on the chest, for both builds of the country kit's ARENA JERSEY (suit 1)
        [Test]
        public void TheFlagsArePrintedOnTheJersey()
        {
            foreach (bool fem in new[] { false, true })
            {
                Mesh m = Av.PartMesh(fem ? "av_top_1_f" : "av_top_1_m");
                Assert.That(m, Is.Not.Null, "the ARENA JERSEY");
                Vector3[] v = m.vertices, n = m.normals;
                Matrix4x4 back = AvatarDraw.PrintToShader(AvatarDraw.BackPrint(fem)), chest = AvatarDraw.PrintToShader(AvatarDraw.ChestPrint(fem));
                int onBack = 0, backWrong = 0, onChest = 0, chestWrong = 0;
                for (int i = 0; i < v.Length; ++i)
                {
                    foreach (var (box, isBack) in new[] { (back, true), (chest, false) })
                    {
                        Vector3 d = box.MultiplyPoint3x4(v[i]);
                        float facing = box.MultiplyVector(n[i]).z;
                        if (facing <= 0 || Mathf.Abs(d.x) > 0.5f || Mathf.Abs(d.y) > 0.5f || Mathf.Abs(d.z) > 0.5f) continue;   // (the shader's test)
                        bool nativeBack = -v[i].z > 0;   // (Unity axes: native z negated; the back is native +z)
                        if (isBack) { ++onBack; if (!nativeBack) ++backWrong; }
                        else { ++onChest; if (nativeBack) ++chestWrong; }
                    }
                }
                string b = fem ? "FEMININE" : "MASCULINE";
                TestContext.Out.WriteLine($"{b}: {onBack} jersey points under the back print, {onChest} under the chest print");
                Assert.That(onBack, Is.GreaterThan(10), b + ": the back print lands on the jersey (the PR #25 bodies: about half the points)");
                // (the print is per pixel: on the PR #25 jersey, lower in points, the masculine chest's box holds 2 of them and
                // the flag shows across the triangles round them; SceneShots.JerseyFlags, seen 2026-11-20)
                Assert.That(onChest, Is.GreaterThan(1), b + ": so does the chest's");
                Assert.That(backWrong, Is.EqualTo(0), b + ": the back print stays on the back");
                Assert.That(chestWrong, Is.EqualTo(0), b + ": the chest print on the chest");
            }
        }
    }
}
