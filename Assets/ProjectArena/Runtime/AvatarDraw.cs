using System;
using System.Collections.Generic;
using System.Globalization;
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // Draws avatars as the native main.cpp does (lookOf, drawAvatarBody, swingBones, hairTuck, tailExit, drawGauntlet):
    // the parts Avatar.DrawList names, skinned on the rig's 9 bones by Native.shader, in the look's colours with the team
    // rim light; hair that swings on its spring chains (.swing) and stays inside the headgear and face shield worn
    // (.tuck); a country's flag on the jersey's back and chest. Matrices are native (right-handed); Unity's are
    // Sz * M * Sz (the importer negated z). Each part is drawn with Graphics.RenderMesh, one draw per material.
    public class AvatarDraw
    {
        // the look a player is drawn in (native AvatarLook): channel colours, the rim light, the suit pattern
        public struct Look { public Vector4[] ch; public Vector4 rim; public Texture2D pattern; }

        static readonly float[] kTeam2 = { 0.85f, 0.85f, 0.9f };   // native kTeamRGB[2]: outside the tank

        // (next: a model in pieces, drawn in turn; far: FAR AWAY's lighter triangles, its submeshes after the model's own)
        class Part { public Mesh mesh; public Material[] mats; public bool skinned, far; public Part next; }

        class SwingChain
        {
            public float k = 110, c = 7, follow = 0.35f, limitCos = 0.26f;
            public int n, bone0 = 1;
            public Vec3[] R = new Vec3[7];
            public float[] rmin = new float[7];
            public SwingChain Copy() { var o = (SwingChain)MemberwiseClone(); o.R = (Vec3[])R.Clone(); o.rmin = (float[])rmin.Clone(); return o; }
        }

        class SwingRig { public int id; public bool exits; public Vec3 centre; public List<SwingChain> chains = new List<SwingChain>(); }
        class SwingState { public bool init; public double t; public Vec3[,] X = new Vec3[6, 7], V = new Vec3[6, 7]; }
        class Tuck { public int w, h; public Vec3 c; public byte[] v; }

        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        readonly Dictionary<string, SwingRig> swingRigs = new Dictionary<string, SwingRig>();
        readonly Dictionary<long, SwingState> swings = new Dictionary<long, SwingState>();
        const int kGearMaps = 11;   // (headgear 1-10: RONIN KABUTO, 9, and the SOMBRERO, 10, from 0.3.22)
        readonly Tuck[,] tuckGear = new Tuck[2, kGearMaps], tuckVisor = new Tuck[2, 8];   // (headgear 1-10, face shields 1-7)
        readonly Dictionary<int, Texture2D> tuckTex = new Dictionary<int, Texture2D>();
        readonly Texture2D[,] patternTex = new Texture2D[2, 8];   // per body: each body's own UV layout (open_pat_<name>_<m|f>, native 5992aae)
        readonly Dictionary<string, Texture2D> flagTex = new Dictionary<string, Texture2D>();
        // the plate styles (0-2, 4-6) split at the wrist; the grapple gauntlets' (4-6) fists, fingers and hinges (TASKS.md E35)
        readonly Part[] gHand = new Part[Avatar.kGauntletStyles], gCuff = new Part[Avatar.kGauntletStyles], gFist = new Part[Avatar.kGauntletStyles];
        readonly Part[,] gFinger = new Part[Avatar.kGauntletStyles, 5];
        class GrappleRig { public Vec3[] piv = new Vec3[5], rest = new Vec3[5], open = new Vec3[5]; public Vec3 emit; }
        readonly GrappleRig[] grapple = new GrappleRig[Avatar.kGauntletStyles];
        readonly Part grappleBeam, grappleFlare;
        static readonly Vec3 kHoldOffset = new Tunables().holdOffset;   // (where the ball is held, in the palm: the tether's end)
        public bool palmHold = true;   // (the match's THE BALL IN THE LEFT PALM, Tunables.PalmHoldNow: the left hand's hold point)
        readonly Part lancer;
        readonly Mesh quad;
        readonly Material flagMat;
        readonly List<MaterialPropertyBlock> pool = new List<MaterialPropertyBlock>();
        int poolUsed, poolFrame = -1;
        readonly List<Avatar.PartDraw> list = new List<Avatar.PartDraw>();
        readonly Mat4[] swingOut = new Mat4[7];
        // the body's nine bones (Avatar.BoneId); hair: its own bones, the first 7, the rest identity (the shader reads nine)
        readonly Matrix4x4[] bonesU = new Matrix4x4[(int)Avatar.BoneId.Count], swingU = NewSwingU();
        static Matrix4x4[] NewSwingU()
        {
            var u = new Matrix4x4[(int)Avatar.BoneId.Count];
            for (int b = 0; b < u.Length; ++b) u[b] = Matrix4x4.identity;
            return u;
        }
        public double startTime;
        public Camera camera;   // draw for this camera only (editor pictures); null = every camera
        public int layer;       // the layer drawn on (the PC spectator's close-ups: a layer of their own)

        public int PartCount => parts.Count;
        // FAR AWAY (TASKS.md E39, native fb93914): beyond FarAway from the eye (eye: native space, set each frame by the
        // view) an avatar's parts and another player's gauntlets are drawn with their lighter triangles (the hub's
        // miniatures too; the mirror never). farOn off: every avatar whole (pictures, and until a view sets the eye)
        public const float FarAway = 3.0f;
        public bool farOn;
        public Vec3 eye;
        public bool lastFar;     // (tests: the last body drawn was far away)
        public int trisDrawn;    // (tests and diagnostics: triangles drawn, counted up)
        public int FarParts { get { int n = 0; foreach (var p in parts.Values) if (p.far) ++n; return n; } }
        public long Triangles(string name, bool distant)
        {   // (tests: a part's triangles, whole or far)
            if (!parts.TryGetValue(name, out Part p)) return 0;
            long n = 0;
            for (Part q = p; q != null; q = q.next)
                for (int s = 0; s < q.mats.Length && s < q.mesh.subMeshCount; ++s) n += q.mesh.GetIndexCount(distant && q.far ? s + q.mats.Length : s) / 3;
            return n;
        }
        public int SwingRigCount => swingRigs.Count;
        public int GrappleCount
        {   // (tests: the grapple gauntlets with every part)
            get
            {
                int n = 0;
                for (int k = 0; k < Avatar.kGauntletStyles; ++k)
                {
                    bool ok = grapple[k] != null && gFist[k] != null && gHand[k] != null && gCuff[k] != null && grappleBeam != null && grappleFlare != null;
                    for (int f = 0; f < 5; ++f) ok &= gFinger[k, f] != null;
                    n += ok ? 1 : 0;
                }
                return n;
            }
        }
        public int TuckCount { get { int n = 0; foreach (var t in tuckGear) if (t != null) ++n; foreach (var t in tuckVisor) if (t != null) ++n; return n; } }

        static readonly int TintMul = Shader.PropertyToID("_TintMul"), UVOff = Shader.PropertyToID("_UVOff"),
            EmissiveAdd = Shader.PropertyToID("_EmissiveAdd"), Rim = Shader.PropertyToID("_Rim"),
            UseChannels = Shader.PropertyToID("_UseChannels"), ChanCols = Shader.PropertyToID("_ChanCols"),
            Bones = Shader.PropertyToID("_Bones"), Skinned = Shader.PropertyToID("_Skinned"), Bend = Shader.PropertyToID("_Bend"), Pelvis = Shader.PropertyToID("_Pelvis"), Twist = Shader.PropertyToID("_Twist"), Tw = Shader.PropertyToID("_Tw"), SwishOn = Shader.PropertyToID("_Swish"), Sw = Shader.PropertyToID("_Sw"),
            SwC = Shader.PropertyToID("_SwC"), SwP = Shader.PropertyToID("_SwP"), SwTex = Shader.PropertyToID("_SwTex"), TuckOn = Shader.PropertyToID("_TuckOn"),
            TuckC = Shader.PropertyToID("_TuckC"), TuckTex = Shader.PropertyToID("_TuckTex"), PatternOn = Shader.PropertyToID("_PatternOn"),
            PatternTex = Shader.PropertyToID("_PatternTex"), MainTex = Shader.PropertyToID("_MainTex"),
            Decals = Shader.PropertyToID("_Decals"), DecalM0 = Shader.PropertyToID("_DecalM0"), DecalM1 = Shader.PropertyToID("_DecalM1"),
            DecalTex = Shader.PropertyToID("_DecalTex");

        // NATIONS (native 0.3.17): the country's flag printed high on the jersey's back, just under the collar (owner,
        // 2026-10-29: on the number panel it sat at the waist), and small on the left chest; printed on the cloth itself
        // (bind-pose boxes) so it bends and turns with it (a report, 2026-11-07: as a flat card it stuck out like paper).
        // A box maps bind space to the unit box at `at` (w x h across the print, depth through the cloth), as native box()
        static Mat4 PrintBox(Pose at, float w, float h, float depth)
        {
            Quat qi = at.q.Conj();
            var inv = new Pose(qi, qi.Rotate(new Vec3(0, 0, 0) - at.p));
            return Mat4.FromPose(new Pose(Quat.Identity, new Vec3(0, 0, 0)), new Vec3(1f / w, 1f / h, 1f / depth)) * Mat4.FromPose(inv);
        }

        // COUNTRY KIT (TASKS.md E34, native 485beac): where a suit's own design is in the jersey's places, its prints move
        // (seen on every suit in the game's pictures, 2026-11-18). ECHO SUIT (and LIVE): the back print over its identity
        // disc (centred 7 cm under the shoulders, 12 cm tall with its ring), which showed round and under the flag; VECTOR
        // and NIGHTFORGE: on the left shoulder blade, clear of the glowing strip and diamonds down the spine; VANGUARD
        // (feminine): the crest on the chest plate's face (4 cm out from the chest). suit: the one drawn (the jersey, 1)
        public static Mat4 BackPrint(bool fem, int suit = 1)
        {   // (deep enough for every build's back, short of the chest)
            var back = new Pose(Quat.AxisAngle(new Vec3(1, 0, 0), M.Deg2Rad(15f)), fem ? new Vec3(0, -0.11f, 0.090f) : new Vec3(0, -0.12f, 0.125f));
            float size = 0.13f;
            if (suit == 5 || suit == 12)
            {
                back.p = back.p + back.q.Rotate(new Vec3(0, fem ? -0.033f : -0.015f, 0));
                size = 0.21f;
            }
            if (suit == 14 || suit == 15)
            {
                back.p = back.p + back.q.Rotate(new Vec3(fem ? -0.077f : -0.090f, 0, 0));
                size = fem ? 0.09f : 0.10f;
            }
            float depth = 0.16f;
            if (suit == Avatar.kSuitDress)
            {   // (PELURU LAYERED DRESS: on the upper flounce, its bottom edge along the hem; see ChestPrint)
                back = new Pose(Quat.AxisAngle(new Vec3(1, 0, 0), M.Deg2Rad(-26.6f)), new Vec3(0, -0.1281f, 0.1300f));
                size = 0.075f;
                depth = 0.04f;
            }
            return PrintBox(back, size, size / 1.5f, depth);
        }

        public static Mat4 ChestPrint(bool fem, int suit = 1)
        {   // (measured on av_top_1: the left chest, facing forward, leaning with it)
            var crest = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), M.kPi) * Quat.AxisAngle(new Vec3(1, 0, 0), -M.Deg2Rad(fem ? 45f : 18f)),
                                 fem ? new Vec3(-0.08f, -0.118f, -0.147f) : new Vec3(-0.08f, -0.150f, -0.131f));
            if (suit == 2 && fem)
                crest = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), M.kPi) * Quat.AxisAngle(new Vec3(1, 0, 0), -M.Deg2Rad(10f)), new Vec3(-0.08f, -0.150f, -0.186f));
            float depth = 0.10f;
            // PELURU LAYERED DRESS (TASKS.md E35, native 37979ef): its flounced neckline covers both places, and the prints,
            // boxes made for a chest and a back 3 cm under it, landed on it aslant and ran past its hem (the owner,
            // 2026-11-19: "the placement of the flag is crooked on the collar ... line it up with the hem of the ruffle
            // it's on so it doesn't get cut off"). In the same places, each lies on the upper flounce, turned and tipped as
            // the cloth runs there, its bottom edge along the hem and clear of the hem's scallops
            if (suit == Avatar.kSuitDress)
            {
                crest = new Pose(Quat.AxisAngle(new Vec3(0, 1, 0), M.Deg2Rad(-167.7f)) * Quat.AxisAngle(new Vec3(1, 0, 0), M.Deg2Rad(-26.5f)) *
                                 Quat.AxisAngle(new Vec3(0, 0, 1), M.Deg2Rad(10.1f)), new Vec3(-0.0816f, -0.1273f, -0.1871f));
                depth = 0.04f;
            }
            return PrintBox(crest, 0.055f, 0.055f / 1.5f, depth);
        }

        // a box for the shader: Unity-axes positions in (z negated), native box axes out (Sz * ToUnity = native * Sz)
        public static Matrix4x4 PrintToShader(Mat4 box) => Sz * ToUnity(box);

        static readonly Matrix4x4 Sz = Matrix4x4.Scale(new Vector3(1, 1, -1));

        public AvatarDraw(AvatarLibrary lib, Shader textShader)
        {
            if (lib == null) return;
            Part Load(GameObject go)
            {
                if (go == null) return null;
                var mf = go.GetComponent<MeshFilter>();
                var mr = go.GetComponent<MeshRenderer>();
                if (mf != null && mr != null)
                    return new Part { mesh = mf.sharedMesh, mats = mr.sharedMaterials, skinned = mf.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord3),
                                      far = mf.sharedMesh.subMeshCount >= 2 * mr.sharedMaterials.Length && mr.sharedMaterials.Length > 0 };
                // a model in pieces (more than 65535 vertices: MdlImporter's 16-bit pieces, such as the KILT's), each drawn in turn
                Part first = null, last = null;
                foreach (Transform c in go.transform)
                {
                    var cf = c.GetComponent<MeshFilter>();
                    var cr = c.GetComponent<MeshRenderer>();
                    if (cf == null || cr == null || cf.sharedMesh == null) continue;
                    var p = new Part { mesh = cf.sharedMesh, mats = cr.sharedMaterials, skinned = cf.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord3) };
                    if (first == null) first = p; else last.next = p;
                    last = p;
                }
                return first;
            }
            foreach (var go in lib.parts) { var p = Load(go); if (p != null) parts[go.name] = p; }
            for (int k = 0; k < Avatar.kGauntletStyles; ++k)
            {   // (optional: the open asset set has them)
                if (!Avatar.PlateGauntlet(k)) continue;
                gHand[k] = lib.gauntletHands != null && k < lib.gauntletHands.Length ? Load(lib.gauntletHands[k]) : null;
                gCuff[k] = lib.gauntletCuffs != null && k < lib.gauntletCuffs.Length ? Load(lib.gauntletCuffs[k]) : null;
                if (!Avatar.GrappleGauntlet(k)) continue;
                gFist[k] = lib.gauntletFists != null && k < lib.gauntletFists.Length ? Load(lib.gauntletFists[k]) : null;
                for (int f = 0; f < 5; ++f)
                    gFinger[k, f] = lib.gauntletFingers != null && k * 5 + f < lib.gauntletFingers.Length ? Load(lib.gauntletFingers[k * 5 + f]) : null;
                if (lib.gauntletHinges != null && k < lib.gauntletHinges.Length && lib.gauntletHinges[k] != null)
                    grapple[k] = LoadHinge($"gauntlet_{k}.hinge", lib.gauntletHinges[k].text);
            }
            grappleBeam = Load(lib.grappleBeam);
            grappleFlare = Load(lib.grappleFlare);
            lancer = Load(lib.lancer);
            BuildHandShapes();
            if (lib.swishes != null)
                foreach (var s in lib.swishes)
                    if (s != null && s.name.EndsWith(".swish")) LoadSwish(s.name.Substring(0, s.name.Length - 6), s.bytes);
            foreach (var s in lib.swings)
                if (s != null && s.name.EndsWith(".swing")) LoadSwing(s.name.Substring(0, s.name.Length - 6), s.text);
            foreach (var t in lib.tucks)
            {
                if (t == null || !t.name.EndsWith(".tuck")) continue;
                string model = t.name.Substring(0, t.name.Length - 5);   // av_gear_3_m / av_visor_2_f
                string[] w = model.Split('_');
                if (w.Length != 4 || !int.TryParse(w[2], out int k) || k < 1 || k > (w[1] == "gear" ? kGearMaps - 1 : 7)) continue;
                int bi = w[3] == "f" ? 1 : 0;
                Tuck tk = LoadTuck(t.bytes);
                if (w[1] == "gear") tuckGear[bi, k] = tk;
                else if (w[1] == "visor") tuckVisor[bi, k] = tk;
            }
            for (int k = 1; k < 8; ++k)
            {   // (each body's own, else the one both bodies shared)
                string t = Avatar.PatternTexture(k);
                if (t == null) continue;
                for (int bi = 0; bi < 2; ++bi)
                {
                    string own = t + (bi != 0 ? "_f" : "_m");
                    Texture2D shared = null, mine = null;
                    foreach (var p in lib.patterns) { if (p == null) continue; if (p.name == own) mine = p; else if (p.name == t) shared = p; }
                    patternTex[bi, k] = mine ?? shared;
                }
            }
            foreach (var f in lib.flags) if (f != null) flagTex[f.name] = f;

            // a unit quad in XY facing +Z, uv as the native quad (v flipped for Unity's textures), white tint for Text.shader
            quad = new Mesh { name = "flag quad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.SetUVs(2, new List<Vector4> { new Vector4(1, 1, 1, 0.998f), new Vector4(1, 1, 1, 0.998f), new Vector4(1, 1, 1, 0.998f), new Vector4(1, 1, 1, 0.998f) });
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            if (textShader != null) flagMat = new Material(textShader) { name = "flag", renderQueue = (int)RenderQueue.Transparent + 312 };   // (over panels)
        }

        // ------------------------------------------------------------ loading (native loadSwing / loadTuck)

        // <gauntlet_K>.hinge (native loadHinge, tools/build_open_assets.py write_hinge): 'finger k  pivot  rest  open' (the
        // hinge point, a direction off it at rest and opened all the way, in the grip frame the fist is drawn in) for the
        // four fingers and the thumb, then 'emit  point  forward' (the plasma emitter). All five fingers and the emitter, or
        // none of it
        static GrappleRig LoadHinge(string name, string txt)
        {
            var g = new GrappleRig();
            int got = 0;
            bool emit = false;
            foreach (string raw in txt.Split('\n'))
            {
                float[] v = Floats(raw.TrimEnd('\r'), out string word);
                if (word == "finger" && v.Length >= 10)
                {
                    int f = (int)v[0];
                    if (f < 0 || f >= 5 || v[0] != f) continue;
                    g.piv[f] = new Vec3(v[1], v[2], v[3]);
                    g.rest[f] = M.Normalize(new Vec3(v[4], v[5], v[6]));
                    g.open[f] = M.Normalize(new Vec3(v[7], v[8], v[9]));
                    got |= 1 << f;
                }
                else if (word == "emit" && v.Length >= 3) { g.emit = new Vec3(v[0], v[1], v[2]); emit = true; }
            }
            if (got != 31 || !emit) { Debug.LogWarning($"{name}: incomplete, the gauntlet's fingers stay closed"); return null; }
            return g;
        }

        // <model>.swing: 'centre x y z', then per chain 'chain k c follow limit' and its rest points 'x y z' (head space)
        void LoadSwing(string model, string txt)
        {
            var rig = new SwingRig();
            int next = 1;   // the next free bone (0 is the head)
            foreach (string raw in txt.Split('\n'))
            {
                string ln = raw.TrimEnd('\r');
                float[] f = Floats(ln, out string word);
                if (word == "centre" && f.Length >= 3) rig.centre = new Vec3(f[0], f[1], f[2]);
                else if (ln.StartsWith("exit")) rig.exits = true;
                else if (word == "chain" && f.Length >= 4)
                {
                    if (rig.chains.Count >= 6) { Debug.LogWarning($"{model}.swing: too many chains"); return; }
                    rig.chains.Add(new SwingChain { k = f[0], c = f[1], follow = f[2], limitCos = MathF.Cos(M.Deg2Rad(f[3])), bone0 = next });
                }
                else if (word == "" && f.Length >= 3 && rig.chains.Count > 0)
                {
                    SwingChain c = rig.chains[rig.chains.Count - 1];
                    if (c.n >= 7) { Debug.LogWarning($"{model}.swing: a chain too long"); return; }
                    if (c.n != 0) ++next;
                    c.R[c.n++] = new Vec3(f[0], f[1], f[2]);
                }
            }
            foreach (SwingChain c in rig.chains)
            {
                if (c.n < 2) { Debug.LogWarning($"{model}.swing: a chain without a segment"); return; }
                for (int j = 0; j < c.n; ++j) c.rmin[j] = 0.97f * M.Length(c.R[j] - rig.centre);   // (it can't swing into the head)
            }
            if (rig.chains.Count == 0 || next > 7) { Debug.LogWarning($"{model}.swing: no chains or more than 6 bones"); return; }
            rig.id = swingRigs.Count;
            swingRigs[model] = rig;
        }

        // the numbers on a line, after an optional leading word (as the native sscanf patterns read them)
        static float[] Floats(string ln, out string word)
        {
            word = "";
            var o = new List<float>();
            string[] t = ln.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < t.Length; ++i)
            {
                if (float.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) o.Add(v);
                else if (i == 0) word = t[i];
                else break;
            }
            return o.ToArray();
        }

        // <model>.tuck: 'TUCK', u32 w, h, f32 centre xyz, w*h radius bytes
        static Tuck LoadTuck(byte[] d)
        {
            if (d == null || d.Length < 24 || d[0] != 'T' || d[1] != 'U' || d[2] != 'C' || d[3] != 'K') return null;
            uint w = BitConverter.ToUInt32(d, 4), h = BitConverter.ToUInt32(d, 8);
            if (w == 0 || h == 0 || w > 256 || h > 256 || d.Length < 24 + w * h) return null;
            var t = new Tuck { w = (int)w, h = (int)h, c = new Vec3(BitConverter.ToSingle(d, 12), BitConverter.ToSingle(d, 16), BitConverter.ToSingle(d, 20)) };
            t.v = new byte[w * h];
            Array.Copy(d, 24, t.v, 0, t.v.Length);
            return t;
        }

        // ------------------------------------------------------------ looks

        // native lookOf: team 0 / 1 = that end's colour (sideRGB: blue / orange or the countries'), 2 = outside the tank
        public Look LookOf(AvatarDesc din, int team, float[][] sideRGB)
        {
            AvatarDesc d = Avatar.Uniform(din);   // (a country: the team kit)
            int t = team < 0 || team > 2 ? 2 : team;
            float[] tc = t < 2 ? sideRGB[t] : kTeam2;
            float[][] ch = Avatar.NewChannels();
            Avatar.ChannelColors(d, tc, ch);
            var L = new Look { ch = new Vector4[ch.Length] };
            for (int c = 0; c < ch.Length; ++c) L.ch[c] = new Vector4(ch[c][0], ch[c][1], ch[c][2], ch[c][3]);
            // the team-colour rim light that marks every player (like the original's); softer over a country's kit
            L.rim = new Vector4(tc[0], tc[1], tc[2], Avatar.NationOf(d) != 0 ? 0.25f : 0.5f);
            L.pattern = d.pattern < 8 ? patternTex[Avatar.BodyType(d), d.pattern] : null;
            return L;
        }

        // ------------------------------------------------------------ drawing

        public static Matrix4x4 ToUnity(Mat4 m)
        {
            var r = new Matrix4x4();
            for (int c = 0; c < 4; ++c)
                for (int rr = 0; rr < 4; ++rr) r[rr, c] = m.m[c * 4 + rr];
            return Sz * r * Sz;
        }

        public static Matrix4x4 ToUnity(Matrix4x4 native) => Sz * native * Sz;

        MaterialPropertyBlock Block()
        {
            if (poolFrame != Time.frameCount) { poolFrame = Time.frameCount; poolUsed = 0; }
            if (poolUsed == pool.Count) pool.Add(new MaterialPropertyBlock());
            var b = pool[poolUsed++];
            b.Clear();
            return b;
        }

        // a mirrored draw (negative determinant: the left gauntlet, the builder's mirror) culls and lights the other
        // faces (native glFrontFace(GL_CW)); Graphics.RenderMesh does not flip them itself
        readonly Dictionary<Material, Material> mirroredMat = new Dictionary<Material, Material>();

        Material Mirrored(Material m)
        {
            if (mirroredMat.TryGetValue(m, out Material o)) return o;
            o = new Material(m) { name = m.name + " (mirrored)" };
            if (m.HasProperty("_Cull"))
            {
                var c = (CullMode)(int)m.GetFloat("_Cull");
                o.SetFloat("_Cull", (float)(c == CullMode.Back ? CullMode.Front : c == CullMode.Front ? CullMode.Back : CullMode.Off));
                o.SetFloat("_FlipFace", 1f);
            }
            mirroredMat[m] = o;
            return o;
        }

        void Render(Part part, Matrix4x4 objectToWorld, MaterialPropertyBlock b, Bounds bounds, bool distant = false)
        {
            bool mirrored = objectToWorld.determinant < 0;
            for (Part p = part; p != null; p = p.next)
                for (int s = 0; s < p.mesh.subMeshCount && s < p.mats.Length; ++s)
                {
                    int sub = distant && p.far && farOn ? s + p.mats.Length : s;   // (FAR AWAY: its lighter triangles)
                    var rp = new RenderParams(mirrored ? Mirrored(p.mats[s]) : p.mats[s])
                    {
                        matProps = b, worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                        lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off, camera = camera, layer = layer,
                    };
                    Graphics.RenderMesh(rp, p.mesh, sub, objectToWorld);
                    trisDrawn += (int)p.mesh.GetIndexCount(sub) / 3;
                }
        }

        // ---------------------------------------------------------------- TENNIS SKIRT and KILT (TASKS.md E13)
        // <model>.swish (native tools/import_pr25.py): 'SWSH', u32 kind, f32 waist y, hem y, centre x, z, the most a vertex
        // moves, the first row's y, the row step, u32 rows, cols, rows x cols f32 body radii (native loadSwish)
        public sealed class SwishShape { public Texture2D tex; public Vector4 c, p; public float waistY, hemY; public int kind; }
        readonly Dictionary<string, SwishShape> swishShapes = new Dictionary<string, SwishShape>();
        public int SwishCount => swishShapes.Count;
        public float[] shotSwish;   // (pictures: every skirt swung this much, x, z, twist; a picture has no motion)

        void LoadSwish(string model, byte[] d)
        {
            if (d == null || d.Length < 44 || d[0] != 'S' || d[1] != 'W' || d[2] != 'S' || d[3] != 'H') return;
            uint kind = System.BitConverter.ToUInt32(d, 4), rows = System.BitConverter.ToUInt32(d, 36), cols = System.BitConverter.ToUInt32(d, 40);
            var f = new float[7];
            for (int i = 0; i < 7; ++i) f[i] = System.BitConverter.ToSingle(d, 8 + 4 * i);
            if (rows < 2 || cols < 8 || rows > 512 || cols > 512 || d.Length < 44 + rows * cols * 4 || f[6] <= 0) return;
            var r = new float[rows * cols];
            System.Buffer.BlockCopy(d, 44, r, 0, r.Length * 4);
            // (read exactly in the shader: Load, no filtering)
            var tex = new Texture2D((int)cols, (int)rows, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = model + " swish" };
            tex.SetPixelData(r, 0);
            tex.Apply(false, true);
            swishShapes[model] = new SwishShape
            {
                tex = tex, kind = (int)kind, waistY = f[0], hemY = f[1],
                c = new Vector4(f[2], f[3], f[4], 0), p = new Vector4(f[5], 1f / f[6], rows, cols),
            };
        }

        static void SetLook(MaterialPropertyBlock b, Look L, Vector3 emissiveAdd, Vector2 uvOff)
        {
            b.SetVector(TintMul, Vector4.one);
            b.SetVector(UVOff, NativeSpace.WrapUV(uvOff));   // (v flipped for Unity's textures; kept in [0, 1): native wrapUV)
            b.SetVector(EmissiveAdd, emissiveAdd);
            b.SetVector(Rim, L.rim);
            b.SetFloat(UseChannels, 1f);
            b.SetVectorArray(ChanCols, L.ch);
            b.SetFloat(PatternOn, L.pattern != null ? 1f : 0f);
            if (L.pattern != null) b.SetTexture(PatternTex, L.pattern);
            b.SetFloat(Skinned, 0f);
            b.SetFloat(TuckOn, 0f);
            b.SetFloat(Decals, 0f);
        }

        // ---------------------------------------------------------------- HANDS APART (TASKS.md E8)
        // each plate style's fist [k, 0] and cuff [k, 1], and the classic lancer [3, 0], as shapes (Avatar/HandsApart.cs),
        // built from their meshes on a thread at start-up (native: about 80 ms on a PC); the hands are drawn as tracked
        // until they're ready
        readonly HandShape[,] shapes = new HandShape[Avatar.kGauntletStyles, 2];   // ([k][0] the fist, [k][1] the cuff; [3][0] the lancer)
        volatile bool shapesReady;
        public bool noApart;   // (pictures and tests: the hands as tracked)
        public bool ShapesReady => shapesReady;
        public HandShape ShapeOf(int k, int part) => shapesReady ? shapes[k, part] : null;

        void BuildHandShapes()
        {
            var jobs = new List<(int k, int part, int sides, Vec3[] v, int[] idx)>();
            void Add(Part p, int k, int part, int sides)
            {
                if (p == null || p.mesh == null || !p.mesh.isReadable) return;
                Vector3[] uv = p.mesh.vertices;
                var v = new Vec3[uv.Length];
                for (int i = 0; i < uv.Length; ++i) v[i] = new Vec3(uv[i].x, uv[i].y, -uv[i].z);   // (back to native axes)
                var tri = new List<int>();
                for (int s = 0; s < p.mats.Length && s < p.mesh.subMeshCount; ++s) tri.AddRange(p.mesh.GetTriangles(s));   // (its own: not FAR AWAY's)
                jobs.Add((k, part, sides, v, tri.ToArray()));
            }
            for (int k = 0; k < Avatar.kGauntletStyles; ++k)
                if (Avatar.PlateGauntlet(k) && gHand[k] != null && gCuff[k] != null)
                {   // (a grapple gauntlet's fist: its hand and its fingers closed, in one model; a cuff: open at both ends)
                    Add(Avatar.GrappleGauntlet(k) ? gFist[k] : gHand[k], k, 0, 5);
                    Add(gCuff[k], k, 1, 4);
                }
            Add(lancer, 3, 0, 5);
            if (jobs.Count == 0) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                var t0 = System.Diagnostics.Stopwatch.StartNew();
                foreach (var j in jobs) shapes[j.k, j.part] = HandsApart.Build(j.v, j.idx, HandsApart.LancerLocal, j.sides);
                shapesReady = true;
                Debug.Log($"[Avatar] hands apart: the gauntlets' shapes in {t0.ElapsedMilliseconds} ms ({shapes[0, 0]?.pts.Count ?? 0}, {shapes[0, 1]?.pts.Count ?? 0} points)");
            });
        }

        // (once a frame per player is enough: both eyes and passes reuse it)
        struct ApartMemo { public Pose g0, g1; public int style; public Vec3 shift; public bool set; }
        readonly ApartMemo[] apartMemo = new ApartMemo[8];
        int apartNext;
        readonly HandPart[][] apartParts = { new HandPart[2], new HandPart[2] };

        static bool Same(Pose x, Pose y) => x.p.x == y.p.x && x.p.y == y.p.y && x.p.z == y.p.z && x.q.x == y.q.x && x.q.y == y.q.y && x.q.z == y.q.z && x.q.w == y.q.w;

        // HANDS APART (native main.cpp handsApart; the owner, 2026-11-16, her video: with the controllers held together,
        // the gauntlets crossed into each other): one player's input with the two drawn hands moved apart, half each,
        // until their gauntlets (`look`'s, as DrawGauntlet draws them) only touch; at most 3 cm each. Drawing only (the
        // body's arms follow): the game, the ball and what is sent keep the tracked hands
        public PlayerInput Apart(PlayerInput @in, AvatarDesc look)
        {
            if (noApart || !shapesReady || !@in.hand[0].tracked || !@in.hand[1].tracked || M.Length(@in.hand[0].grip.p - @in.hand[1].grip.p) > 0.40f)
                return @in;
            int k = Avatar.GauntletOf(look);
            bool plate = Avatar.PlateGauntlet(k) && gHand[k] != null && gCuff[k] != null;
            HandShape fist = plate ? shapes[k, 0] : shapes[3, 0], cuff = plate ? shapes[k, 1] : null;
            if (fist == null || !fist.Valid) return @in;
            int style = plate ? k : 3;
            Vec3 S = new Vec3();
            int memo = -1;
            for (int i = 0; i < apartMemo.Length; ++i)
                if (apartMemo[i].set && apartMemo[i].style == style && Same(apartMemo[i].g0, @in.hand[0].grip) && Same(apartMemo[i].g1, @in.hand[1].grip)) { memo = i; break; }
            if (memo >= 0) S = apartMemo[memo].shift;
            else
            {
                int parts = cuff != null && cuff.Valid ? 2 : 1;
                HandsApart.Parts(@in.hand[0].grip, @in.hand[1].grip, fist, cuff, plate, apartParts);
                S = HandsApart.Shift(apartParts[0], parts, apartParts[1], parts, HandsApart.MaxShift);
                apartMemo[apartNext] = new ApartMemo { g0 = @in.hand[0].grip, g1 = @in.hand[1].grip, style = style, shift = S, set = true };
                apartNext = (apartNext + 1) % apartMemo.Length;
            }
            if (M.Length(S) < 1e-5f) return @in;
            PlayerInput o = @in.Clone();
            o.hand[0].grip.p = o.hand[0].grip.p - S * 0.5f;
            o.hand[1].grip.p = o.hand[1].grip.p + S * 0.5f;
            return o;
        }

        // a gauntlet (native drawGauntlet): gm = grip * the hand's mirror (native space; h: 0 left, mirrored). Plate gauntlets
        // are the hand and the cuff, the cuff in its tuned pose against the hand; CONTROLLER is the plain lancer; the grapple
        // gauntlets open with that hand's trigger
        public void DrawGauntlet(AvatarDesc d, Matrix4x4 gm, Look L, float trigger = 0f, int h = 1, bool distant = false)
        {
            int k = Avatar.GauntletOf(d);
            var b = Block();
            SetLook(b, L, Vector3.zero, Vector2.zero);
            Vector3 at = new Vector3(gm.m03, gm.m13, -gm.m23);
            var bounds = new Bounds(at, new Vector3(1, 1, 1));
            if (Avatar.PlateGauntlet(k) && gHand[k] != null && gCuff[k] != null)
            {
                Matrix4x4 fist = gm * HandPlacement.FromPose(Avatar.FistPose());
                Render(gHand[k], ToUnity(fist * HandPlacement.LancerLocal), b, bounds, distant);   // (the fist in line with the cuff)
                Render(gCuff[k], ToUnity(gm * HandPlacement.FromPose(Avatar.CuffPose(false)) * HandPlacement.LancerLocal), b, bounds, distant);
                if (grapple[k] != null) DrawGrapple(k, gm, fist, b, L, bounds, trigger, h == 0, distant);
                return;
            }
            if (lancer != null) Render(lancer, ToUnity(gm * HandPlacement.LancerLocal), b, bounds, distant);
        }

        // FAR AWAY: a point (native space) beyond FarAway from the eye
        public bool Distant(Vec3 p) => farOn && M.Length(p - eye) > FarAway;

        // GRAPPLER, ARC CLAW, REGALIA (TASKS.md E35, native 27cad43 drawGrapple): the trigger opens the fingers, each about
        // its hinge (in the grip frame the fist is drawn in: fist = gm * FistPose), and while it is pulled a plasma tether in
        // the glow colour runs from the emitter in the front of the fist to where a ball is held, in the palm (Game.HoldLocal),
        // a flare at each end. (Until 0.3.23 it ran to the old launch point 25 cm ahead of the hand, where the ball sat
        // before 0.3.22's palm: TASKS.md E56, native 75f3f1f; the owner, 2026-11-21, "that long green tractor beam, they are
        // supposed to be in the palm of the hand like a palm grappling".) Drawing only: the throw, the catch and what is sent
        // are as they were
        public static float GrappleOpen(float trigger)
        {
            float t = Math.Clamp((trigger - 0.05f) / 0.80f, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        void DrawGrapple(int k, Matrix4x4 gm, Matrix4x4 fist, MaterialPropertyBlock b, Look L, Bounds bounds, float trigger, bool left, bool distant = false)
        {
            GrappleRig G = grapple[k];
            float t = GrappleOpen(trigger);
            for (int f = 0; f < 5; ++f)
            {
                if (gFinger[k, f] == null) continue;
                Quat q = M.Nlerp(Quat.Identity, M.FromTo(G.rest[f], G.open[f]), t);
                Matrix4x4 H = HandPlacement.FromPose(new Pose(q, G.piv[f] - q.Rotate(G.piv[f])));
                Render(gFinger[k, f], ToUnity(fist * H * HandPlacement.LancerLocal), b, bounds, distant);
            }
            if (t < 0.02f || grappleBeam == null) return;
            Vector4 glow = L.ch != null && L.ch.Length > (int)Avatar.Channel.Glow ? L.ch[(int)Avatar.Channel.Glow] : Vector4.one;
            var pp = Block();
            float s = 0.7f + 0.6f * t;
            pp.SetVector(TintMul, new Vector4(glow.x * s, glow.y * s, glow.z * s, 1f));
            pp.SetVector(UVOff, NativeSpace.WrapUV(new Vector2(0, -(float)((Time.timeAsDouble - startTime) % 1000.0) * 2.4f)));   // the plasma running out along the tether
            Vec3 E = Avatar.FistPose().Transform(G.emit);   // in gm's frame (the left hand's mirrored)
            Vec3 B = kHoldOffset;   // (Game.HoldLocal: the left hand's mirrored with the palm hold; gm mirrors the left hand's)
            if (left && palmHold) B.x = -B.x;
            if (left) B.x = -B.x;
            Vec3 D = B - E;
            float len = M.Length(D);
            if (len < 0.02f) return;
            float w = 0.020f * t;
            Matrix4x4 TRS(Quat q, Vec3 p, float sx, float sy, float sz) => HandPlacement.FromPose(new Pose(q, p)) * Matrix4x4.Scale(new Vector3(sx, sy, sz));
            Render(grappleBeam, ToUnity(gm * TRS(M.FromTo(new Vec3(0, 0, -1), D * (1f / len)), E, w, w, len)), pp, bounds);
            if (grappleFlare == null) return;
            Render(grappleFlare, ToUnity(gm * TRS(Quat.Identity, E, 0.013f * t, 0.013f * t, 0.013f * t)), pp, bounds);
            var pf = Block();
            pf.SetVector(TintMul, new Vector4(glow.x * s, glow.y * s, glow.z * s, 0.8f));
            pf.SetVector(UVOff, NativeSpace.WrapUV(new Vector2(0, -(float)((Time.timeAsDouble - startTime) % 1000.0) * 2.4f)));
            Render(grappleFlare, ToUnity(gm * TRS(Quat.Identity, B, 0.056f * t, 0.056f * t, 0.056f * t)), pf, bounds);
        }

        // Body (torso, neck, head and its parts, arms) for one player (native drawAvatarBody); `pre` places it (the
        // builder mirror, miniatures; null = as it stands). Hands are drawn by the caller. swingKey: whose hair state
        // (each drawn avatar keeps its own; default the team)
        // THE REACH (TASKS.md E10): each drawn avatar's rig across frames (the torso's lean, the shoulders, ARM SIZING), by
        // the same key as its hair (swingKey, else its team): stepped by its first draw of a frame, read by the rest
        readonly Dictionary<int, Avatar.RigState> rigStates = new Dictionary<int, Avatar.RigState>();
        public Avatar.RigState RigStateOf(int key)
        {
            if (!rigStates.TryGetValue(key, out Avatar.RigState s)) rigStates[key] = s = new Avatar.RigState();
            return s;
        }
        public bool noTwist;   // (pictures: FOREARM TWIST off)
        readonly Vector4[] twU = new Vector4[8];

        // FOREARM TWIST: the rig's 8 vec4 (native axes) as the shader takes them in the meshes' space (z negated): the
        // points and directions mirrored, so each turn's sense flips: the roll negated (and with it the normal's
        // correction, whose cross product flips too)
        public static Vector4[] TwistToUnity(float[] t, Vector4[] o)
        {
            for (int i = 0; i < 8; ++i)
            {
                float w = t[i * 4 + 3];
                if (i == 2 || i == 3) w = -w;   // ([2+k]: the roll)
                o[i] = new Vector4(t[i * 4], t[i * 4 + 1], -t[i * 4 + 2], w);
            }
            return o;
        }

        public float shotArmScale = -1f;   // (pictures: every avatar's arms sized this; ARM SIZING has no time to measure them)

        public void DrawBody(PlayerInput @in, Look L, int team, Mat4 pre = null, bool lod = false, float flash = 0f, int swingKey = -1)
        {
            if (parts.Count == 0) return;
            double now = Time.timeAsDouble;
            pre ??= Mat4.Identity();
            Avatar.RigState rs = RigStateOf(swingKey >= 0 ? swingKey : team);   // (THE REACH: its own, once a frame)
            if (shotArmScale > 0) rs.armScale = shotArmScale;
            Avatar.StepRig(rs, @in, @in.avatar, now);
            if (shotArmScale > 0) rs.armScale = shotArmScale;
            Avatar.Rig rig = Avatar.SolveRig(@in, @in.avatar, rs);
            Matrix4x4 pelvisU = ToUnity(rig.pelvis);
            Matrix4x4 preU = ToUnity(pre);
            // FAR AWAY (TASKS.md E39, native drawAvatarBody): more than FarAway from the eye, its parts' lighter triangles;
            // the hub's miniatures too; the builder's mirror (a reflection: pre turned inside out) never
            bool preMirror = preU.determinant < 0;
            bool farAway = lastFar = lod || (!preMirror && farOn && M.Length(pre.TransformPoint(@in.head.p) - eye) > FarAway);
            var bounds = new Bounds(NativeSpace.ToUnity(pre.TransformPoint(@in.head.p) - new Vec3(0, 0.8f, 0)), new Vector3(3, 3, 3));
            for (int b = 0; b < bonesU.Length; ++b) bonesU[b] = ToUnity(rig.skin[b]);
            // NATIONS: the country's flag printed on the jersey, back and chest (the boxes: BackPrint, ChestPrint)
            Texture2D nationTex = null;
            Matrix4x4 print0 = Matrix4x4.identity, print1 = Matrix4x4.identity;
            if (!lod && Avatar.NationOf(@in.avatar) != 0)
            {
                bool fem = Avatar.BodyType(@in.avatar) == 1;
                int suit = Avatar.Uniform(@in.avatar).suit;   // (the jersey; with COUNTRY KIT = MY OUTFIT, your own suit)
                Avatar.Nation N = Avatar.NationAt(Avatar.NationOf(@in.avatar));
                if (N != null && flagTex.TryGetValue(N.flag, out Texture2D ft))
                {
                    nationTex = ft;
                    print0 = PrintToShader(BackPrint(fem, suit));
                    print1 = PrintToShader(ChestPrint(fem, suit));
                }
            }
            list.Clear();
            Avatar.DrawList(rig, @in, @in.avatar, list, lod);
            foreach (Avatar.PartDraw p in list)
            {
                string name = p.model;
                if (!parts.TryGetValue(name, out Part part))
                {   // a face's own part missing (e.g. a face shield not fitted yet): the base head's
                    int n = name.Length;
                    if (n > 2 && name[n - 1] >= '1' && name[n - 1] <= '9' && (name[n - 2] == 'm' || name[n - 2] == 'f'))
                        name = name.Substring(0, n - 1);
                    if (!parts.TryGetValue(name, out part)) continue;
                }
                var em = new Vector3(flash, flash * 0.3f, 0);
                if ((p.flags & Avatar.PartNeon) != 0) em += (Vector3)L.ch[(int)Avatar.Channel.Glow] * 0.35f;
                var uv = Vector2.zero;
                if ((p.flags & Avatar.PartPulse) != 0) uv.y = -(float)((now - startTime) % 1000.0) * 1.6f;   // ~40 cm/s
                var blk = Block();
                SetLook(blk, L, em, uv);
                if ((p.flags & Avatar.PartSkinned) != 0 && part.skinned)
                {
                    blk.SetFloat(Skinned, 1f);
                    blk.SetMatrixArray(Bones, bonesU);
                    if (rig.bent) { blk.SetFloat(Bend, 1f); blk.SetMatrix(Pelvis, pelvisU); }   // (THE REACH: the waist)
                    if (rig.twisted && !noTwist) { blk.SetFloat(Twist, 1f); blk.SetVectorArray(Tw, TwistToUnity(rig.twist, twU)); }   // (FOREARM TWIST)
                    if (swishShapes.TryGetValue(name, out SwishShape sw))
                    {   // (TENNIS SKIRT / KILT: its swing, and the hem lifted as it swings out)
                        Avatar.Swish s = rs.swish;
                        float x = shotSwish != null ? shotSwish[0] : s.x, z = shotSwish != null ? shotSwish[1] : s.z;
                        float len = Mathf.Max(0.1f, (sw.waistY - sw.hemY) * 0.6f);
                        blk.SetFloat(SwishOn, 1f);
                        blk.SetVector(Sw, new Vector4(x, z, shotSwish != null ? shotSwish[2] : s.twist, Mathf.Min(0.018f, (x * x + z * z) / (2f * len))));
                        blk.SetVector(SwC, sw.c);
                        blk.SetVector(SwP, sw.p);
                        blk.SetTexture(SwTex, sw.tex);
                    }
                    // (the jersey or your own top, and its pulses: under the prints they give way to them)
                    if (nationTex != null && (p.model.StartsWith("av_top_", System.StringComparison.Ordinal) || (p.flags & Avatar.PartPulse) != 0))
                    {
                        blk.SetFloat(Decals, 2f);
                        blk.SetMatrix(DecalM0, print0);
                        blk.SetMatrix(DecalM1, print1);
                        blk.SetTexture(DecalTex, nationTex);
                    }
                }
                Mat4 model = pre * p.m;
                if ((p.flags & Avatar.PartHair) != 0)
                {   // hair: a style that moves swings (its chains per drawn avatar); under headgear, tucked in
                    var off = new Vec3(0, 0, 0);
                    bool tuckFree = false;
                    if (swingRigs.TryGetValue(name, out SwingRig srig))
                    {
                        // several tails (HIGH PIGTAILS): each out under the rim below its own tie, by its own bones (TASKS.md
                        // E35, native 27cad43)
                        int nc = srig.chains.Count;
                        var offs = new Vec3[6];
                        bool each = false;
                        if (srig.exits && nc > 1)
                            for (int c = 0; c < nc && c < 6; ++c) { offs[c] = TailExit(@in.avatar, srig, c); each |= M.Length(offs[c]) > 1e-4f; }
                        else if (srig.exits) off = TailExit(@in.avatar, srig);
                        bool moved = M.Length(off) > 1e-4f;
                        if (moved || each)
                        {   // out under the helmet's rim: the rig moved there (made once per helmet)
                            string k = $"{name}@{@in.avatar.headgear}";
                            if (!swingRigs.TryGetValue(k, out SwingRig mr))
                            {
                                mr = new SwingRig { exits = false, centre = srig.centre, id = swingRigs.Count };
                                for (int ci = 0; ci < nc; ++ci)
                                {
                                    SwingChain cc = srig.chains[ci].Copy();
                                    Vec3 o = each ? (ci < 6 ? offs[ci] : new Vec3(0, 0, 0)) : off;
                                    for (int j = 0; j < cc.n; ++j) { cc.R[j] = cc.R[j] + o; cc.rmin[j] = 0.97f * M.Length(cc.R[j] - mr.centre); }
                                    mr.chains.Add(cc);
                                }
                                swingRigs[k] = mr;
                            }
                            srig = mr;
                        }
                        SwingBones(srig, swingKey >= 0 ? swingKey : team, @in.head, swingOut, now);
                        if (moved)
                        {   // its bones work on the moved rest pose: the part is drawn moved, its bones in its own frame
                            Mat4 to = Mat4.FromPose(new Pose(Quat.Identity, off)), back = Mat4.FromPose(new Pose(Quat.Identity, new Vec3(0, 0, 0) - off));
                            for (int b = 0; b < 7; ++b) swingOut[b] = back * swingOut[b] * to;
                            model = model * to;
                        }
                        else if (each)
                        {   // each tail's bones carry its own move (the vertices are all on its bones, none on the head)
                            for (int ci = 0; ci < nc && ci < 6; ++ci)
                            {
                                SwingChain c = srig.chains[ci];
                                Mat4 to = Mat4.FromPose(new Pose(Quat.Identity, offs[ci]));
                                for (int j = 0; j + 1 < c.n; ++j) swingOut[c.bone0 + j] = swingOut[c.bone0 + j] * to;
                            }
                            tuckFree = true;   // (out under the rim: not pulled back in under the helmet)
                        }
                        if (part.skinned)
                        {
                            for (int b = 0; b < 7; ++b) swingU[b] = ToUnity(swingOut[b]);
                            blk.SetFloat(Skinned, 1f);
                            blk.SetMatrixArray(Bones, swingU);
                        }
                    }
                    if (!tuckFree && HairTuck(@in.avatar, out Texture2D tt, out Vec3 tc))
                    {
                        tc = tc - off;   // (the tuck map is in head space)
                        blk.SetFloat(TuckOn, 1f);
                        blk.SetVector(TuckC, new Vector4(tc.x, tc.y, -tc.z, 0));
                        blk.SetTexture(TuckTex, tt);
                    }
                }
                Render(part, ToUnity(model), blk, bounds, farAway);
            }
        }

        // a country's flag texture (null for none)
        public Texture2D FlagTexture(int nation)
        {
            Avatar.Nation N = Avatar.NationAt(nation);
            return N != null && flagTex.TryGetValue(N.flag, out Texture2D t) ? t : null;
        }

        // a country's flag (w x h metres, centred on the pose, facing its +Z), as the native drawFlag; nothing for none
        public void DrawFlag(int nation, Pose at, float w)
        {
            Avatar.Nation N = Avatar.NationAt(nation);
            if (N == null || flagMat == null || !flagTex.TryGetValue(N.flag, out Texture2D tex)) return;
            Flag(Mat4.FromPose(at, new Vec3(w, w / 1.5f, 1)), tex, new Bounds(NativeSpace.ToUnity(at.p), new Vector3(1, 1, 1)));
        }

        void Flag(Mat4 m, Texture2D tex, Bounds bounds)
        {
            var b = Block();
            b.SetTexture(MainTex, tex);
            var rp = new RenderParams(flagMat) { matProps = b, worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera, layer = layer };
            Graphics.RenderMesh(rp, quad, 0, ToUnity(m));
        }

        // Hair that moves (native swingBones): each chain hangs from its first point, which moves with the head; every
        // further point is pulled toward its rest place (the rest shape turns fully with the head's heading but only
        // `follow` of its tilt), with springs and damping, so it lags, swings and settles. Lengths are kept; no point
        // swings more than its chain's limit from rest, or into the head. o[0] = identity (the head); each chain
        // segment's bone maps head space at rest to head space now.
        void SwingBones(SwingRig rig, int key, Pose head, Mat4[] o, double now)
        {
            long sk = (long)key * 1024 + rig.id;
            if (!swings.TryGetValue(sk, out SwingState s)) swings[sk] = s = new SwingState();
            int nc = rig.chains.Count;
            bool reset = !s.init || now - s.t > 0.5;   // first sight, a long pause or a warp: at rest
            for (int c = 0; c < nc && !reset; ++c) reset = M.Length(s.X[c, 0] - head.Transform(rig.chains[c].R[0])) > 0.6f;
            if (reset)
            {
                for (int c = 0; c < nc; ++c)
                    for (int j = 0; j < rig.chains[c].n; ++j) { s.X[c, j] = head.Transform(rig.chains[c].R[j]); s.V[c, j] = new Vec3(0, 0, 0); }
                s.init = true;
                s.t = now;
            }
            float dt = (float)Math.Min(0.05, now - s.t);
            if (dt > 0.0005f)
            {
                s.t = now;
                Vec3 f = head.q.Rotate(new Vec3(0, 0, -1));
                f.y = 0;
                Quat yawQ = M.Length(f) > 1e-4f ? Quat.AxisAngle(new Vec3(0, 1, 0), MathF.Atan2(-f.x, -f.z)) : head.q;
                Vec3 Cw = head.Transform(rig.centre);
                const int sub = 3;
                float h = dt / sub;
                for (int c = 0; c < nc; ++c)
                {
                    SwingChain ch = rig.chains[c];
                    Quat qt = M.Nlerp(yawQ, head.q, ch.follow);
                    float sinL = MathF.Sqrt(M.Max(0f, 1f - ch.limitCos * ch.limitCos));
                    for (int it = 0; it < sub; ++it)
                    {
                        s.X[c, 0] = head.Transform(ch.R[0]);
                        for (int j = 1; j < ch.n; ++j)
                        {
                            Vec3 rest = qt.Rotate(ch.R[j] - ch.R[j - 1]);
                            float Ln = M.Length(ch.R[j] - ch.R[j - 1]);
                            Vec3 rd = rest / M.Max(Ln, 1e-5f);
                            Vec3 prev = s.X[c, j];
                            s.V[c, j] = s.V[c, j] + ((s.X[c, j - 1] + rest - s.X[c, j]) * ch.k - s.V[c, j] * ch.c) * h;
                            s.X[c, j] = s.X[c, j] + s.V[c, j] * h;
                            Vec3 ov = s.X[c, j] - Cw;   // not into the head
                            float ol = M.Length(ov);
                            if (ol < ch.rmin[j] && ol > 1e-5f) s.X[c, j] = Cw + ov * (ch.rmin[j] / ol);
                            Vec3 d = s.X[c, j] - s.X[c, j - 1];
                            float dl = M.Length(d);
                            d = dl > 1e-5f ? d / dl : rd;
                            if (M.Dot(d, rd) < ch.limitCos)
                            {   // past the limit: back onto its cone
                                Vec3 perp = d - rd * M.Dot(d, rd);
                                perp = M.Length(perp) > 1e-5f ? M.Normalize(perp) : new Vec3(0, -1, 0);
                                d = rd * ch.limitCos + perp * sinL;
                            }
                            s.X[c, j] = s.X[c, j - 1] + d * Ln;
                            s.V[c, j] = (s.X[c, j] - prev) / h;
                        }
                    }
                }
            }
            Quat hinv = head.q.Conj();
            for (int b = 0; b < 7; ++b) o[b] = Mat4.Identity();
            for (int c = 0; c < nc; ++c)
            {
                SwingChain ch = rig.chains[c];
                for (int j = 0; j + 1 < ch.n; ++j)
                {
                    Vec3 from = M.Normalize(ch.R[j + 1] - ch.R[j]);                                // head space, at rest
                    Vec3 to = M.Normalize(hinv.Rotate(s.X[c, j + 1] - s.X[c, j]));                // head space, now
                    Quat q = M.FromTo(from, to);
                    Vec3 at = hinv.Rotate(s.X[c, j] - head.p);                                      // the segment's root now
                    o[ch.bone0 + j] = Mat4.FromPose(new Pose(q, at - q.Rotate(ch.R[j])));
                }
            }
        }

        // Hair under this avatar's headgear and face shield (native hairTuck): their tuck maps combined (the nearer
        // surface wins), as a texture made the first time the pair is drawn
        bool HairTuck(AvatarDesc d, out Texture2D tex, out Vec3 c)
        {
            tex = null;
            c = new Vec3(0, 0, 0);
            int b = Avatar.BodyType(d), g = d.headgear < kGearMaps ? d.headgear : 0, v = d.visor < 8 ? d.visor : 0;
            Tuck G = g != 0 ? tuckGear[b, g] : null;
            Tuck V = v != 0 ? tuckVisor[b, v] : null;
            if (G != null && V != null && (G.w != V.w || G.h != V.h)) V = null;
            if (G == null && V == null) return false;
            Tuck T = G ?? V;
            int key = b * 100 + g * 10 + v;
            if (!tuckTex.TryGetValue(key, out tex))
            {
                var m = (byte[])T.v.Clone();
                if (G != null && V != null) for (int i = 0; i < m.Length; ++i) m[i] = Math.Min(m[i], V.v[i]);
                tex = new Texture2D(T.w, T.h, TextureFormat.R8, false, true)
                {
                    name = $"tuck {key}", filterMode = FilterMode.Bilinear,
                    wrapModeU = TextureWrapMode.Repeat,   // round the head
                    wrapModeV = TextureWrapMode.Clamp,    // straight down .. straight up
                };
                tex.LoadRawTextureData(m);
                tex.Apply(false, true);
                tuckTex[key] = tex;
            }
            c = T.c;
            return true;
        }

        // A ponytail under a helmet that covers its tie comes out under the helmet's rim instead (native tailExit): the
        // offset (head space) that moves its root straight down the back of the head to just inside the rim
        // (chain: each of the HIGH PIGTAILS' tails its own, native 27cad43)
        Vec3 TailExit(AvatarDesc d, SwingRig rig, int chain = 0)
        {
            int b = Avatar.BodyType(d), g = d.headgear < kGearMaps ? d.headgear : 0;
            Tuck T = g != 0 ? tuckGear[b, g] : null;
            if (T == null || chain >= rig.chains.Count) return new Vec3(0, 0, 0);
            Vec3 C = T.c;
            float Radius(float azi, float ele)
            {   // nearest cell
                int i = (((int)MathF.Floor((azi + M.kPi) / (2 * M.kPi) * T.w) % T.w) + T.w) % T.w;
                int j = Math.Max(0, Math.Min(T.h - 1, (int)MathF.Floor((ele + M.kPi / 2) / M.kPi * T.h)));
                return 0.05f + 0.5f * T.v[j * T.w + i] / 255f;
            }
            Vec3 root = rig.chains[chain].R[0], dr = root - C;
            float r = M.Length(dr);
            if (r < 1e-4f) return new Vec3(0, 0, 0);
            float az = MathF.Atan2(dr.x, dr.z);
            float el = MathF.Asin(M.Max(-1f, M.Min(1f, dr.y / r)));
            if (Radius(az, el) > 0.45f) return new Vec3(0, 0, 0);
            float step = M.kPi / T.h * 0.5f;
            float rim = Radius(az, el);
            while (el > -1.4f && Radius(az, el - step) <= 0.45f) { el -= step; rim = Radius(az, el); }
            var dir = new Vec3(MathF.Cos(el) * MathF.Sin(az), MathF.Sin(el), MathF.Cos(el) * MathF.Cos(az));
            return C + dir * (rim - 0.012f) - root;
        }

        // test access: does the library hold a model of this name; the hair bones of a swinging style (null: none)
        public bool HasPart(string name) => parts.ContainsKey(name);
        public Mesh PartMesh(string name) => parts.TryGetValue(name, out Part p) ? p.mesh : null;   // (tests: where the prints land)
        public bool HasSwing(string name) => swingRigs.ContainsKey(name);

        public Mat4[] HairBones(string model, int key, Pose head, double now)
        {
            if (!swingRigs.TryGetValue(model, out SwingRig rig)) return null;
            var o = new Mat4[7];
            SwingBones(rig, key, head, o, now);
            return o;
        }
    }
}
