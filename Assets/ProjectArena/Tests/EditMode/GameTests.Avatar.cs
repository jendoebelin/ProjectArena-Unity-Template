// The native avatar checks (app/tests/test_game.cpp avatarTests, native commit 0eea0e7) against Avatar/Avatar.cs:
// the 6 avatar groups, 44 checks. (The shield-tuning group in the same native function is a game check: GameTests.)
using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectArena.Sim;
using static ProjectArena.Sim.M;

namespace ProjectArena.Tests
{
    public partial class GameTests
    {
        static Vec3 Mx(Vec3 v) { v.x = -v.x; return v; }

        [Test]
        public void AvatarTwoBoneIK()
        {
            var t = new Checks("avatar: two-bone IK", 3);
            bool lengths = true, onPole = true;
            uint s = 7;
            float Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFF) / 65535f; }
            for (int i = 0; i < 200; ++i)
            {
                var sh = new Vec3(0, 1.4f, 0);
                float rx = Rnd(), ry = Rnd(), rz = Rnd();   // (in the native argument order)
                Vec3 dir = Normalize(new Vec3(rx - 0.5f, ry - 0.5f, rz - 0.5f) + new Vec3(1e-3f, 0, 0));
                float reach = 0.05f + Rnd() * 0.45f;   // within l1 + l2 = 0.55
                Vec3 w = sh + dir * reach;
                Vec3 pole = sh + new Vec3(0.3f, -1, 0.2f);
                Vec3 e = Avatar.ElbowIK(sh, w, Avatar.kUpperArm, Avatar.kForeArm, pole);
                if (MathF.Abs(Length(e - sh) - Avatar.kUpperArm) > 2e-3f || MathF.Abs(Length(w - e) - Avatar.kForeArm) > 2e-3f) lengths = false;
                Vec3 u = Normalize(w - sh), c = sh + u * Dot(e - sh, u);
                Vec3 pp = pole - sh;
                pp = pp - u * Dot(pp, u);
                if (Length(pp) > 1e-3f && Dot(e - c, pp) < -1e-4f) onPole = false;
            }
            t.C(lengths, "elbow keeps both bone lengths whenever the wrist is in reach");
            t.C(onPole, "elbow bends toward the pole");
            Vec3 e2 = Avatar.ElbowIK(new Vec3(0, 0, 0), new Vec3(1, 0, 0), 0.3f, 0.2f, new Vec3(0, -1, 0));
            t.C(MathF.Abs(e2.y) < 1e-5f && MathF.Abs(e2.x - 0.6f) < 1e-4f, "out of reach: straight arm, bones stretched evenly");
            t.Done();
        }

        [Test]
        public void AvatarRigFromAStandingPlayer()
        {
            var t = new Checks("avatar: rig from a standing player", 13);
            Game g = NewGame();
            g.Reset(true);
            PlayerInput @in = Standing(g, 0);
            @in.hand[0].grip.p = @in.head.p + new Vec3(0.35f, -0.45f, -0.25f);
            @in.hand[1].grip.p = @in.head.p + new Vec3(0.35f, -0.45f, 0.25f);
            AvatarDesc d = AvatarDesc.Default;
            Avatar.Rig r = Avatar.SolveRig(@in, d);
            Vec3 f = r.torso.q.Rotate(new Vec3(0, 0, -1));
            t.C(f.x > 0.95f, "player 0's torso faces +X (the court)");
            t.C(MathF.Abs(r.torso.p.y - (@in.head.p.y - Avatar.BodyBindOf(0).eye.y)) < 0.01f, "neck base below the eyes as in the body's bind pose");
            t.C(r.arm[0] && r.arm[1], "both arms solved for tracked hands");
            bool ends = true;
            for (int k = 0; k < 2; ++k)
            {
                Vec3 fe = Avatar.Tuning.forearmEnd;   // plate gauntlet wrist, inside the (tuned) cuff
                Vec3 w = @in.hand[k].grip.Transform(Avatar.CuffPose(k == 0).Transform(new Vec3(k == 1 ? fe.x : -fe.x, fe.y, fe.z)));
                if (Length(r.fore[k].b - w) > 1e-4f || Length(r.upper[k].b - r.fore[k].a) > 1e-6f) ends = false;
            }
            t.C(ends, "forearm ends inside the gauntlet; the elbow joins both bones");
            Vec3 right = r.torso.q.Rotate(new Vec3(1, 0, 0));
            t.C(Dot(r.upper[1].a - r.torso.p, right) > 0.12f && Dot(r.upper[0].a - r.torso.p, right) < -0.12f, "right shoulder on the right");
            Mat4 m = Avatar.BoneMatrix(r.upper[1], false);
            t.C(Length(m.TransformPoint(new Vec3(0, 0, 0)) - r.upper[1].a) < 1e-5f &&
                Length(m.TransformPoint(new Vec3(0, -r.upper[1].natural, 0)) - r.upper[1].b) < 1e-4f, "bone matrix maps the mesh's joints onto the rig");
            {
                Avatar.BodyBind K = Avatar.BodyBindOf(0);
                bool skinOk = true;
                for (int k = 0; k < 2; ++k)
                {
                    if (Length(r.skin[(int)Avatar.BoneId.UpperL + k].TransformPoint(K.shoulder[k]) - r.upper[k].a) > 2e-3f) skinOk = false;
                    if (Length(r.skin[(int)Avatar.BoneId.ForeL + k].TransformPoint(K.wrist[k]) - r.fore[k].b) > 2e-3f) skinOk = false;
                    if (Length(r.skin[(int)Avatar.BoneId.Torso].TransformPoint(K.shoulder[k]) - r.upper[k].a) > 2e-3f) skinOk = false;
                }
                if (Length(r.skin[(int)Avatar.BoneId.Head].TransformPoint(K.eye) - @in.head.p) > 1e-4f) skinOk = false;
                if (Length(r.skin[(int)Avatar.BoneId.Neck].TransformPoint(K.neckTop) - r.neck.a) > 1e-3f) skinOk = false;
                t.C(skinOk, "skin matrices carry the bind joints onto the solved rig (shoulders, wrists, head, neck)");
            }
            t.C(Dot(m.TransformPoint(new Vec3(0, 0, -1)) - r.upper[1].a, r.upper[1].front) > 0, "bone mesh front (-Z) turns toward the front reference");
            @in.head.q = @in.head.q * Quat.AxisAngle(new Vec3(1, 0, 0), Deg2Rad(-40));   // look down
            Avatar.Rig r2 = Avatar.SolveRig(@in, d);
            t.C(MathF.Abs(Dot(r2.torso.q.Rotate(new Vec3(0, 1, 0)), new Vec3(0, 1, 0)) - 1) < 1e-4f, "torso stays upright when the head nods");
            {   // developer tuning (SETTINGS > TUNING): the rig's values change it, the defaults are the shipped rig
                Avatar.RigTuning keep = Avatar.Tuning;
                try
                {
                    Avatar.RigTuning T = keep.Clone();
                    Avatar.Tuning = T;
                    T.cuffRot = new Vec3(10, -15, 20);
                    T.cuffPos = new Vec3(0.004f, 0.01f, -0.006f);
                    Pose cr = Avatar.CuffPose(false), cl = Avatar.CuffPose(true);
                    t.C(Length(cr.Transform(Avatar.kCuffPivot) - (Avatar.kCuffPivot + T.cuffPos)) < 1e-6f, "the cuff turns about the wrist pivot");
                    var v = new Vec3(0.03f, -0.02f, 0.09f);
                    t.C(Length(cl.Transform(Mx(v)) - Mx(cr.Transform(v))) < 1e-6f, "the left cuff is the right one mirrored");
                    Avatar.Rig r3 = Avatar.SolveRig(@in, d);
                    Vec3 w3 = @in.hand[1].grip.Transform(cr.Transform(T.forearmEnd));
                    t.C(Length(r3.fore[1].b - w3) < 1e-4f, "a moved cuff carries the forearm's end with it");
                    T = keep.Clone();
                    Avatar.Tuning = T;
                    T.upperLen = 1.15f;
                    @in.hand[1].grip.p = @in.head.p + new Vec3(0.25f, -0.55f, 0.25f);   // well within reach
                    Avatar.Rig r4 = Avatar.SolveRig(@in, d);
                    Avatar.BodyBind K = Avatar.BodyBindOf(0);
                    t.C(MathF.Abs(Length(r4.upper[1].b - r4.upper[1].a) - 1.15f * Length(K.elbow[1] - K.shoulder[1])) < 2e-3f, "UPPER ARM LENGTH scales the upper arm");
                }
                finally { Avatar.Tuning = keep; }
            }
            t.Done();
        }

        // the collarbones (native c758fd3, owner's report 2026-10-27: arms raised to the ceiling folded the shoulders in;
        // native checked it in renders, --scene tuning --armsup DEG): hands low leave them as bound; straight arms
        // overhead lift each shoulder RigTuning.shrug (6 cm) and turn the elbows out to the side
        [Test]
        public void AvatarCollarbonesLiftWithTheArms()
        {
            var t = new Checks("avatar: collarbones", 4);
            Game g = NewGame();
            g.Reset(true);
            PlayerInput @in = Standing(g, 0);
            AvatarDesc d = AvatarDesc.Default;
            Avatar.BodyBind K = Avatar.BodyBindOf(0);
            Quat q = @in.head.q;
            for (int h = 0; h < 2; ++h) @in.hand[h].grip.p = @in.head.p + q.Rotate(new Vec3(h != 0 ? 0.35f : -0.35f, -0.45f, -0.25f));
            Avatar.Rig low = Avatar.SolveRig(@in, d);
            bool bound = true;
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i < 16; ++i)
                    bound &= MathF.Abs(low.skin[(int)Avatar.BoneId.ClavL + k].m[i] - low.skin[(int)Avatar.BoneId.Torso].m[i]) < 1e-6f;
            t.C(bound, "hands low: the collarbones stay as bound (the torso's)");
            for (int h = 0; h < 2; ++h)
            {   // straight arms overhead, the controllers along them (native's --armsup 90)
                float s = h != 0 ? 1f : -1f, a = Deg2Rad(90f);
                var sh = new Vec3(s * 0.18f, -0.29f, 0.04f);
                @in.hand[h].grip = new Pose(q * Quat.AxisAngle(new Vec3(1, 0, 0), a),
                                            @in.head.p + q.Rotate(sh + new Vec3(s * 0.04f, MathF.Sin(a) * 0.62f, -MathF.Cos(a) * 0.62f)));
            }
            Avatar.Rig up = Avatar.SolveRig(@in, d);
            bool lift = true, follows = true;
            for (int k = 0; k < 2; ++k)
            {
                Vec3 asBound = up.skin[(int)Avatar.BoneId.Torso].TransformPoint(K.shoulder[k]);
                Vec3 raised = up.skin[(int)Avatar.BoneId.ClavL + k].TransformPoint(K.shoulder[k]);
                lift &= MathF.Abs(raised.y - asBound.y - Avatar.Tuning.shrug) < 5e-3f;
                // the arm starts from the raised shoulder (then a hand out of reach draws it up to 7 cm further)
                follows &= Length(up.upper[k].a - raised) < Avatar.Tuning.reach + 2e-3f && up.upper[k].a.y > raised.y - 1e-3f;
            }
            t.C(lift, "arms straight overhead: each collarbone lifts its shoulder by the shrug (6 cm)");
            t.C(follows, "and the arm starts from the raised shoulder");
            Vec3 right = up.torso.q.Rotate(new Vec3(1, 0, 0));
            t.C(Dot(up.upper[1].b - up.upper[1].a, right) > 0.02f && Dot(up.upper[0].b - up.upper[0].a, right) < -0.02f,
                "the elbows turn out to the side instead of hanging in");
            t.Done();
        }

        [Test]
        public void AvatarFaceSharesTheBodyByte()
        {
            var t = new Checks("avatar: FACE shares the body byte", 5);
            AvatarDesc d = AvatarDesc.Default;
            Avatar.Set(ref d, Avatar.Field.Body, 1);
            Avatar.Set(ref d, Avatar.Field.Face, 2);
            t.C(Avatar.Get(d, Avatar.Field.Body) == 1 && Avatar.Get(d, Avatar.Field.Face) == 2, "body type and face read back separately");
            t.C(d.body % 2 == 1, "an older build (body % 2) still sees the right body type");
            Avatar.Set(ref d, Avatar.Field.Body, 0);
            t.C(Avatar.Get(d, Avatar.Field.Face) == 2, "changing the body type keeps the face");
            AvatarDesc c = AvatarDesc.Default;
            t.C(Avatar.FromText(Avatar.ToText(d), ref c) && c.body == d.body, "avatar.cfg keeps the face");
            AvatarDesc bad = d;
            bad.body = 0xFF;   // from the network: a face out of range
            Avatar.Sanitize(ref bad);
            t.C(Avatar.Get(bad, Avatar.Field.Face) < Avatar.kFaces && Avatar.Get(bad, Avatar.Field.Body) == 1, "a bad face from the network is repaired");
            t.Done();
        }

        [Test]
        public void AvatarNationInSpareBits()
        {
            // (14, native 485beac's 13 COUNTRY KIT checks, and the skirts kept, native 82dd0c2)
            // (and native 10668f0's five: the COUNTRY order, MY LOOKS)
            var t = new Checks("avatar: NATION in spare bits, the team kit, COUNTRY KIT, the ends' colours", 33);
            int count = Avatar.NationCount();
            t.C(count >= 31 && count <= 255 && Avatar.NationAt(0) == null && Avatar.NationAt(count) != null && Avatar.NationAt(count + 1) == null,
                "the country table: 1..count, 0 = none");
            AvatarDesc d = AvatarDesc.Default;
            Avatar.Set(ref d, Avatar.Field.Body, 1);
            Avatar.Set(ref d, Avatar.Field.Face, 3);
            Avatar.Set(ref d, Avatar.Field.Gauntlet, 2);
            Avatar.Set(ref d, Avatar.Field.Nation, 7);
            t.C(Avatar.Get(d, Avatar.Field.Nation) == 7 && Avatar.Get(d, Avatar.Field.Body) == 1 && Avatar.Get(d, Avatar.Field.Face) == 3 &&
                Avatar.Get(d, Avatar.Field.Gauntlet) == 2, "country, body type, face and gauntlet read back separately");
            Avatar.Set(ref d, Avatar.Field.Face, 1);
            Avatar.Set(ref d, Avatar.Field.Gauntlet, 0);
            t.C(Avatar.NationOf(d) == 7, "changing the face or the gauntlet keeps the country");
            int big = Math.Min(45, count);
            Avatar.Set(ref d, Avatar.Field.Nation, big);
            t.C(Avatar.NationOf(d) == big && Avatar.GauntletOf(d) == 0 && Avatar.FaceOf(d) == 1, "a country above 31 uses the gauntlet's spare bits");
            AvatarDesc c = AvatarDesc.Default;
            t.C(Avatar.FromText(Avatar.ToText(d), ref c) && Avatar.NationOf(c) == big, "avatar.cfg keeps the country");
            {   // the builder's COUNTRY order (TASKS.md E44, native 10668f0, 0.3.23): your most chosen first, then A-Z ("THE " left out)
                int NC = Avatar.NationCount();
                string Name(int n) { string s = Avatar.NationAt(n).name; return s.StartsWith("THE ", StringComparison.Ordinal) ? s.Substring(4) : s; }
                List<int> o = Avatar.NationOrder(null);
                var seen = new bool[NC + 1];
                bool once = o.Count == NC, az = true;
                for (int i = 0; i < o.Count; ++i)
                {
                    if (o[i] < 1 || o[i] > NC || seen[o[i]]) once = false; else seen[o[i]] = true;
                    if (i > 0 && string.CompareOrdinal(Name(o[i - 1]), Name(o[i])) > 0) az = false;
                }
                t.C(once && az, "COUNTRY, nothing chosen yet: every country once, A-Z");
                var picks = new int[NC + 1];
                picks[1] = 2;                    // UNITED STATES twice
                picks[Math.Min(45, NC)] = 5;      // another five times
                picks[2] = 2;                    // CANADA twice (a tie with the United States: A-Z between them)
                o = Avatar.NationOrder(picks);
                t.C(o.Count == NC && o[0] == Math.Min(45, NC) && o[1] == 2 && o[2] == 1, "COUNTRY: the most chosen first, equal counts A-Z");
                bool restAz = true;
                for (int i = 4; i < NC; ++i) if (string.CompareOrdinal(Name(o[i - 1]), Name(o[i])) > 0) restAz = false;
                t.C(restAz, "COUNTRY: the ones never chosen after them, A-Z");
                int glx = 0;
                for (int n = 1; n <= NC; ++n) if (Avatar.NationAt(n).name == "THE GALAXY") glx = n;
                if (glx != 0)
                {
                    o = Avatar.NationOrder(null);
                    int at = o.IndexOf(glx);
                    t.C(at > 0 && at + 1 < NC && string.CompareOrdinal(Name(o[at - 1]), "GALAXY") < 0 && string.CompareOrdinal(Name(o[at + 1]), "GALAXY") > 0,
                        "COUNTRY: THE GALAXY sorted under G");
                }
                else t.C(true, "(no GALAXY in this set)");
                AvatarDesc look = Avatar.PresetLook(3), lookBack = AvatarDesc.Default;   // MY LOOKS: a slot is avatar.cfg's lines (looks.cfg)
                Avatar.SetNation(ref look, Math.Min(45, NC));
                Avatar.SetOwnKit(ref look, true);
                t.C(Avatar.FromText(Avatar.ToText(look), ref lookBack) && lookBack.Equals(look), "MY LOOKS: a saved look reads back the same");
            }
            AvatarDesc s = d;
            Avatar.Sanitize(ref s);
            t.C(Avatar.NationOf(s) == big && Avatar.GauntletOf(s) == 0, "sanitize (network) keeps a valid country");
            // what 0.3.5 and older make of it (their sanitize): the body type stays; FACE 1; the first gauntlet over 31
            AvatarDesc OldSanitize(AvatarDesc o)
            {
                if (o.gauntlet >= 4) o.gauntlet = 0;
                o.body = (byte)((o.body & 1) | ((o.body >> 1) < 4 ? (o.body & ~1) : 0));
                return o;
            }
            AvatarDesc small = d;
            Avatar.Set(ref small, Avatar.Field.Gauntlet, 2);
            Avatar.Set(ref small, Avatar.Field.Nation, 7);
            AvatarDesc o1 = OldSanitize(small), o2 = OldSanitize(d);
            t.C((o1.body & 1) == 1 && o1.gauntlet == 2, "an older build: the body type and (countries 1-31) the gauntlet stay");
            t.C((o2.body & 1) == 1 && o2.gauntlet < 4, "an older build: a country above 31 still gives a valid gauntlet");
            AvatarDesc u = Avatar.Uniform(small);
            t.C(u.suit == 1 && u.pattern == 0 && u.lower == 1 && Avatar.NationOf(u) == 7, "a country wears the team kit (jersey, shorts)");
            AvatarDesc plain = Avatar.PresetLook(3);
            AvatarDesc up = Avatar.Uniform(plain);
            t.C(up.suit == plain.suit && up.pattern == plain.pattern && up.primary == plain.primary, "no country: the look is unchanged");
            {   // a country's kit keeps the TENNIS SKIRT and KILT, as BIKE and LONG SHORTS (native 82dd0c2)
                AvatarDesc sk = small;
                sk.lower = 5;
                AvatarDesc su = Avatar.Uniform(sk);
                sk.lower = 2;
                t.C(su.lower == 5 && Avatar.Uniform(sk).lower == 1, "a country's kit keeps the skirts; CIRCUIT SHORTS become ATHLETIC SHORTS");
            }
            // COUNTRY KIT (TASKS.md E34, native 485beac test_game.cpp, 0.3.22): TEAM JERSEY as before; MY OUTFIT keeps your
            // suit, pattern and lower body in the colours
            t.C(!Avatar.OwnKit(small) && Avatar.SentLook(small).suit == 1 && Avatar.SentLook(small).pattern == 0,
                "a country's kit is the TEAM JERSEY unless chosen");
            AvatarDesc none = plain;
            Avatar.SetOwnKit(ref none, true);
            t.C(!Avatar.OwnKit(none) && none.pattern == plain.pattern, "MY OUTFIT needs a country");
            AvatarDesc mine = Avatar.PresetLook(2);   // VANGUARD (its own pattern), waist up
            Avatar.Set(ref mine, Avatar.Field.Nation, 7);
            Avatar.SetOwnKit(ref mine, true);
            t.C(Avatar.OwnKit(mine) && Avatar.Get(mine, Avatar.Field.Pattern) == Avatar.PresetLook(2).pattern, "MY OUTFIT: the pattern row reads as before");
            Avatar.Set(ref mine, Avatar.Field.Pattern, 1);
            t.C(Avatar.OwnKit(mine) && Avatar.Get(mine, Avatar.Field.Pattern) == 1, "changing the pattern keeps MY OUTFIT");
            Avatar.Set(ref mine, Avatar.Field.Pattern, Avatar.PresetLook(2).pattern);
            AvatarDesc mu = Avatar.Uniform(mine);
            t.C(mu.suit == 2 && mu.pattern == Avatar.PresetLook(2).pattern && mu.lower == 0 && Avatar.NationOf(mu) == 7,
                "MY OUTFIT is drawn in your own suit, pattern and lower body");
            t.C(mu.primary == u.primary && mu.secondary == u.secondary && mu.glow == u.glow, "... in the country's colours");
            AvatarDesc mt = AvatarDesc.Default;
            t.C(Avatar.FromText(Avatar.ToText(mine), ref mt) && Avatar.OwnKit(mt) && mt.suit == 2, "avatar.cfg keeps MY OUTFIT");
            string jersey = Avatar.ToText(mine).Replace("kit=1\n", "");
            t.C(Avatar.FromText(jersey, ref mt) && !Avatar.OwnKit(mt), "an avatar.cfg without it: the TEAM JERSEY");
            AvatarDesc ms = mine;
            Avatar.Sanitize(ref ms);
            t.C(Avatar.OwnKit(ms) && ms.pattern == mine.pattern, "sanitize (network) keeps MY OUTFIT");
            Avatar.SetNation(ref ms, 0);
            Avatar.Sanitize(ref ms);
            t.C(!Avatar.OwnKit(ms) && ms.pattern == Avatar.PresetLook(2).pattern, "... and clears it without a country");
            AvatarDesc sent = Avatar.SentLook(mine);
            AvatarDesc back = sent;
            Avatar.Sanitize(ref back);
            t.C(Avatar.OwnKit(back) && Avatar.Uniform(back).suit == 2, "sent: 0.3.22 shows MY OUTFIT");
            AvatarDesc older = sent;   // what 0.3.21 and older make of it: an unknown pattern (cleared), then the team jersey
            if (older.pattern >= Avatar.FieldCount(Avatar.Field.Pattern)) older.pattern = 0;
            AvatarDesc ou = Avatar.Uniform(older);
            t.C(sent.pattern >= Avatar.FieldCount(Avatar.Field.Pattern) && ou.suit == 1 && ou.pattern == 0 && Avatar.NationOf(ou) == 7,
                "sent: older builds show the team jersey");
            {
                float[][] chm = Avatar.NewChannels(), chj = Avatar.NewChannels();
                float[] tw = { 1, 1, 1 };
                Avatar.ChannelColors(mine, tw, chm);
                Avatar.ChannelColors(small, tw, chj);
                t.C(MathF.Abs(chm[(int)Avatar.Channel.Primary][0] - chj[(int)Avatar.Channel.Primary][0]) < 1e-6f &&
                    MathF.Abs(chm[(int)Avatar.Channel.Glow][1] - chj[(int)Avatar.Channel.Glow][1]) < 1e-6f, "MY OUTFIT takes the kit's exact colours");
            }
            float[][] ch = Avatar.NewChannels();
            float[] team = { 1, 1, 1 };
            Avatar.ChannelColors(small, team, ch);
            Avatar.Nation N = Avatar.NationAt(7);
            t.C(MathF.Abs(ch[(int)Avatar.Channel.Primary][2] - MathF.Pow(N.kit[0][2] / 255f, 2.2f)) < 1e-4f, "the kit is in the flag's own colours");
            float[][] sc = { new float[3], new float[3] };
            Avatar.SideColors(0, 0, sc);
            t.C(sc[0][0] == 0.1f && sc[0][2] == 1.0f && sc[1][0] == 1.0f && sc[1][2] == 0.1f, "no countries: the blue and orange ends");
            int fra = 0, usa = 0;
            for (int n = 1; n <= count; ++n)
            {
                if (Avatar.NationAt(n).code == "FRA") fra = n;
                if (Avatar.NationAt(n).code == "USA") usa = n;
            }
            Avatar.SideColors(0, fra, sc);
            t.C(sc[1][0] > sc[1][2], "FRANCE against the blue end takes its red");
            Avatar.SideColors(usa, usa, sc);
            float s2 = 0;
            for (int k = 0; k < 3; ++k) s2 += (sc[0][k] - sc[1][k]) * (sc[0][k] - sc[1][k]);
            t.C(MathF.Sqrt(s2) > 0.5f, "the same country at both ends: two different colours");
            t.Done();
        }

        [Test]
        public void AvatarHairUnderHeadgear()
        {
            var t = new Checks("avatar: hair under headgear, swinging parts, the community's outfits", 16);
            var @in = new PlayerInput { headTracked = true, head = new Pose(Quat.Identity, new Vec3(0, 1.6f, 0)) };
            string HairParts(int hair, int headgear)
            {
                AvatarDesc d = AvatarDesc.Default;
                d.hair = (byte)hair;
                d.headgear = (byte)headgear;
                var list = new List<Avatar.PartDraw>();
                Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                string names = "";
                foreach (Avatar.PartDraw p in list)
                    if ((p.flags & Avatar.PartHair) != 0) names += p.model + " ";
                return names;
            }
            bool under = true;
            for (int g = 0; g < Avatar.FieldCount(Avatar.Field.Headgear); ++g)
                if (HairParts(6, g) != "av_hair_6_m ") under = false;
            t.C(under, "the chosen hair is drawn under every headgear (the game tucks it in), the cap too");
            t.C(HairParts(3, 2) == "av_hair_3_m av_hair_3t_m ", "a ponytail under a helmet keeps its tail");
            t.C(HairParts(7, 0) == "av_hair_7_m av_hair_7t_m ", "the top knot's loose ends are a part of their own");
            // (TASKS.md E35, native 27cad43: HIGH PIGTAILS and DREADLOCKS, PR #29)
            t.C(HairParts(9, 2) == "av_hair_9_m av_hair_9t_m ", "the high pigtails' tails are a part of their own, under a helmet too");
            t.C(HairParts(10, 0) == "av_hair_10_m ", "the dreadlocks are one part (their locs swing by region)");
            t.C(HairParts(0, 1) == "", "no hair: nothing drawn under the helmet");
            bool tails = true;
            for (int face = 0; face < Avatar.kFaces; ++face)
            {   // the BANDANA's tails: one part per body, for every face (TASKS.md E35, native 988824c)
                AvatarDesc d = AvatarDesc.Default;
                d.mask = 4;
                d.body = (byte)(face << 1);   // (masculine; the face in bits 1-2)
                var list = new List<Avatar.PartDraw>();
                Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                int n = 0;
                foreach (Avatar.PartDraw p in list)
                    if (p.model == "av_mask_4t_m") n += (p.flags & Avatar.PartHair) != 0 ? 1 : 100;
                    else if (p.model.StartsWith("av_mask_4_m", StringComparison.Ordinal)) n += (p.flags & Avatar.PartHair) != 0 ? 10 : 100;   // (its point swings)
                tails &= n == 11;
            }
            t.C(tails, "the bandana's tails are a swinging part of their own, and its point swings too, on every face");
            bool grapples = Avatar.FieldCount(Avatar.Field.Gauntlet) == Avatar.kGauntletStyles;
            for (int k = 4; k < Avatar.kGauntletStyles; ++k)
            {   // GRAPPLER, ARC CLAW, REGALIA: kept, and drawn as plate gauntlets
                AvatarDesc d = AvatarDesc.Default;
                d.gauntlet = (byte)k;
                Avatar.Sanitize(ref d);
                grapples &= Avatar.GauntletOf(d) == k && Avatar.PlateGauntlet(k) && Avatar.GrappleGauntlet(k);
            }
            grapples &= !Avatar.PlateGauntlet(3) && !Avatar.GrappleGauntlet(2);   // (the CONTROLLER is no plate gauntlet)
            t.C(grapples, "the grapple gauntlets are GAUNTLET 4-6, plate gauntlets that open");
            {   // RONIN WRAP (PR #27, 0.3.22; TASKS.md E35, native 382c8c9): suit 16, its own top over the usual shorts (none of
                // its own, unlike 14 and 15)
                AvatarDesc d = AvatarDesc.Default;
                d.suit = 16;
                d.lower = 1;
                Avatar.Sanitize(ref d);
                var list = new List<Avatar.PartDraw>();
                Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                bool top = false, shorts = false, own = false;
                foreach (Avatar.PartDraw p in list)
                {
                    top |= p.model == "av_top_16_m";
                    shorts |= p.model == "av_bottom_1_m";
                    own |= p.model.StartsWith("av_sbottom_", StringComparison.Ordinal);
                }
                t.C(Avatar.FieldCount(Avatar.Field.Suit) > 16 && Avatar.OptionName(Avatar.Field.Suit, 16) == "RONIN WRAP" && d.suit == 16,
                    "RONIN WRAP is suit 16 (games before 0.3.22 read it as GRID RUNNER)");
                t.C(top && shorts && !own, "RONIN WRAP: its own top over the usual shorts");
            }
            {   // RONIN KABUTO (PR #27, 0.3.22; native 27cad43): headgear 9, a helmet drawn on the head like the others, on both bodies
                bool drawn = true;
                for (int body = 0; body < 2; ++body)
                {
                    AvatarDesc d = AvatarDesc.Default;
                    d.headgear = 9;
                    d.body = (byte)body;
                    Avatar.Sanitize(ref d);
                    var list = new List<Avatar.PartDraw>();
                    Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                    int n = 0;
                    foreach (Avatar.PartDraw p in list) n += p.model == (body != 0 ? "av_gear_9_f" : "av_gear_9_m") ? 1 : 0;
                    drawn &= d.headgear == 9 && n == 1;
                }
                t.C(Avatar.FieldCount(Avatar.Field.Headgear) > 9 && Avatar.OptionName(Avatar.Field.Headgear, 9) == "RONIN KABUTO" && drawn,
                    "RONIN KABUTO is headgear 9, drawn on both bodies (games before 0.3.22 read it as NONE)");
            }
            {   // PELURU (PR #30, 0.3.22; native 7e4ddee): SUIT 17 TUXEDO and 18 LAYERED DRESS, HEADGEAR 10 SOMBRERO, MASK 6 SUGAR SKULL
                t.C(Avatar.FieldCount(Avatar.Field.Suit) == 19 && Avatar.OptionName(Avatar.Field.Suit, 17) == "PELURU TUXEDO" &&
                    Avatar.OptionName(Avatar.Field.Suit, 18) == "PELURU LAYERED DRESS" &&
                    Avatar.FieldCount(Avatar.Field.Headgear) == 11 && Avatar.OptionName(Avatar.Field.Headgear, 10) == "SOMBRERO" &&
                    Avatar.FieldCount(Avatar.Field.Mask) == 7 && Avatar.OptionName(Avatar.Field.Mask, 6) == "SUGAR SKULL",
                    "PELURU: suits 17 and 18, headgear 10 SOMBRERO, mask 6 SUGAR SKULL (games before 0.3.22: GRID RUNNER, NONE, NONE)");
                int Has(List<Avatar.PartDraw> list, string prefix, byte flags = 0)
                {
                    int n = 0;
                    foreach (Avatar.PartDraw p in list) if (p.model.StartsWith(prefix, StringComparison.Ordinal) && (p.flags & flags) == flags) ++n;
                    return n;
                }
                bool dress = true, tux = true, worn = true;
                for (int body = 0; body < 2; ++body)
                {
                    char bk = body != 0 ? 'f' : 'm';
                    string top = $"av_top_18_{bk}", ears = $"av_top_18e_{bk}", shorts = $"av_bottom_1_{bk}";
                    AvatarDesc d = AvatarDesc.Default;
                    d.suit = 18; d.lower = 1; d.body = (byte)body;
                    Avatar.Sanitize(ref d);
                    var list = new List<Avatar.PartDraw>();
                    Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                    // the dress: its own skirt in the LOWER BODY's place (no shorts, no waist fade), it sways, its earrings swing
                    dress &= d.suit == 18 && Has(list, top) == 1 && Has(list, "av_bottom_") == 0 && Has(list, "av_wfade_") == 0 &&
                             Has(list, ears, Avatar.PartHair) == 1 && Avatar.SwishKind(d) == 0;
                    d.suit = 17;
                    list.Clear();
                    Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                    top = $"av_top_17_{bk}";
                    tux &= Has(list, top) == 1 && Has(list, shorts) == 1 && Has(list, ears) == 0 && Avatar.SwishKind(d) == -1;
                    for (int face = 0; face < Avatar.kFaces; ++face)
                    {   // the hat on the body, the mask fitted to each face
                        d.headgear = 10; d.mask = 6;
                        d.body = (byte)(body | (face << 1));
                        list.Clear();
                        Avatar.DrawList(Avatar.SolveRig(@in, d), @in, d, list);
                        string gear = $"av_gear_10_{bk}", mask = face != 0 ? $"av_mask_6_{bk}{face}" : $"av_mask_6_{bk}";
                        int m = 0;
                        foreach (Avatar.PartDraw p in list) m += p.model == mask ? 1 : 0;
                        worn &= Has(list, gear) == 1 && m == 1;
                    }
                }
                t.C(dress, "PELURU LAYERED DRESS: its skirt takes the LOWER BODY's place and sways; its earrings swing as hair");
                t.C(tux, "PELURU TUXEDO: over the LOWER BODY, no earrings");
                t.C(worn, "the SOMBRERO on both bodies, the SUGAR SKULL fitted to every face");
            }
            {   // the full sets as presets (the owner, 2026-11-19; native 7ee73bf), after the fourteen there were (bots and pictures use those)
                var sets = new (string name, int suit, int headgear, int mask)[] { ("RONIN", 16, 9, 4), ("PELURU TUXEDO", 17, 10, 6), ("PELURU DRESS", 18, 10, 6),
                                                                                    ("VECTOR", 14, 8, 0), ("NIGHTFORGE", 15, 7, 0) };
                bool ok = Avatar.PresetCount() == 19 && Avatar.PresetName(13) == "TRAINING KIT";
                for (int i = 0; i < 5; ++i)
                {
                    AvatarDesc d = Avatar.PresetLook(14 + i), c = d;
                    Avatar.Sanitize(ref c);
                    ok &= Avatar.PresetName(14 + i) == sets[i].name && d.suit == sets[i].suit && d.headgear == sets[i].headgear && d.mask == sets[i].mask && c.Equals(d);
                }
                t.C(ok, "the full sets are presets: RONIN, PELURU TUXEDO, PELURU DRESS, VECTOR, NIGHTFORGE (each its own pieces)");
            }
            t.Done();
        }

        [Test]
        public void AvatarLooksAndAvatarCfg()
        {
            var t = new Checks("avatar: looks and avatar.cfg", 5);
            bool valid = true, roundTrip = true;
            for (uint seed = 1; seed < 400; ++seed)
            {
                AvatarDesc d = Avatar.RandomLook(seed), c = d;
                Avatar.Sanitize(ref c);
                if (!c.Equals(d)) valid = false;
                AvatarDesc back = AvatarDesc.Default;
                if (!Avatar.FromText(Avatar.ToText(d), ref back) || !back.Equals(d)) roundTrip = false;
            }
            t.C(valid, "random looks are always in range");
            t.C(roundTrip, "avatar.cfg text round-trips every field");
            var bad = new AvatarDesc
            {
                suit = 0xEE, build = 0xEE, hair = 0xEE, hairColor = 0xEE, headgear = 0xEE, visor = 0xEE, visorTint = 0xEE, mask = 0xEE,
                pattern = 0xEE, primary = 0xEE, secondary = 0xEE, glow = 0xEE, skin = 0xEE, lower = 0xEE, body = 0xEE, gauntlet = 0xEE,
            };
            Avatar.Sanitize(ref bad);
            bool ok = true;
            for (int f = 0; f < (int)Avatar.Field.Count; ++f)
                if (Avatar.Get(bad, (Avatar.Field)f) >= Avatar.FieldCount((Avatar.Field)f)) ok = false;
            t.C(ok, "out-of-range looks (e.g. from the network) are sanitised");
            bool names = true;
            for (int f = 0; f < (int)Avatar.Field.Count; ++f)
                for (int i = 0; i < Avatar.FieldCount((Avatar.Field)f); ++i)
                    foreach (char ch in Avatar.OptionName((Avatar.Field)f, i))
                        if (!((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == ' ' || ch == '-')) names = false;
            t.C(names, "option names use only the HUD font's characters");
            int valid2 = 0;
            for (int i = 0; i < Avatar.PresetCount(); ++i)
            {
                AvatarDesc p = Avatar.PresetLook(i), c = p;
                Avatar.Sanitize(ref c);
                valid2 += p.Equals(c) ? 1 : 0;
            }
            t.C(valid2 == Avatar.PresetCount(), "presets are valid looks");
            t.Done();
        }
    }
}
