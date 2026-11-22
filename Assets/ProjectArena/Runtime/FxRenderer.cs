using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // The native renderer's immediate drawing (render.cpp drawSprite, drawSpriteFlat, drawSphere, drawRibbon, drawPanel,
    // drawText): called each frame with native positions, gathered into one mesh (submeshes: soft dot, soft ring, white
    // spheres, ribbons with Shaders/Fx.shader; panels, then text with Shaders/Text.shader). The soft textures are made
    // as the native makeSoftTexture makes them; text uses the native 6 x 8 font (open_font: 59 glyphs, ' ' to 'Z').
    public class FxRenderer
    {
        // (WallSub: the EXPERIMENTAL rooms' glass walls and floors, drawn before the glows so their lines show over them,
        // as native drawExpArena draws its panels first)
        const int Dot = 0, Ring = 1, SphereSub = 2, RibbonSub = 3, PanelSub = 4, TextSub = 5, EmblemSub = 6, WallSub = 7, PicSub = 8, ShadowSub = 9, SolidSub = 10, Subs = 11;

        readonly Mesh mesh = new Mesh { name = "fx" };
        readonly Material[] mats = new Material[Subs];
        readonly List<Vector3> pos = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Vector4> tint = new List<Vector4>();
        readonly List<Color32> col = new List<Color32>();
        readonly List<int>[] idx = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        static Vector3[] sphereV;
        static int[] sphereI;

        // cuttable: THE CUTAWAY (TASKS.md E42) leaves out what this batch draws near a PC preset camera's walls (an
        // EXPERIMENTAL room's own batch)
        public FxRenderer(Shader fx, Shader text = null, Texture2D font = null, Texture2D emblem = null, Texture2D pictures = null, bool cuttable = false)
        {
            mesh.MarkDynamic();
            mesh.indexFormat = IndexFormat.UInt32;
            Texture2D dot = SoftTexture(0), ring = SoftTexture(1);
            for (int k = 0; k < 4; ++k)
            {
                mats[k] = new Material(fx) { name = "fx " + k };
                mats[k].SetTexture("_MainTex", k == Dot ? dot : k == Ring ? ring : Texture2D.whiteTexture);
                mats[k].SetFloat("_Ribbon", k == RibbonSub ? 1f : 0f);
            }
            if (text != null)
            {
                mats[WallSub] = new Material(text) { name = "walls", renderQueue = (int)RenderQueue.Transparent - 5 };
                mats[WallSub].SetTexture("_MainTex", Texture2D.whiteTexture);
                mats[PanelSub] = new Material(text) { name = "panels", renderQueue = (int)RenderQueue.Transparent + 305 };
                mats[PanelSub].SetTexture("_MainTex", Texture2D.whiteTexture);
                mats[TextSub] = new Material(text) { name = "text", renderQueue = (int)RenderQueue.Transparent + 310 };
                mats[TextSub].SetTexture("_MainTex", font != null ? font : Texture2D.whiteTexture);
                mats[EmblemSub] = new Material(text) { name = "emblem", renderQueue = (int)RenderQueue.Transparent + 315 };   // (over its badge)
                mats[EmblemSub].SetTexture("_MainTex", emblem != null ? emblem : Texture2D.whiteTexture);
                mats[PicSub] = new Material(text) { name = "pictures", renderQueue = (int)RenderQueue.Transparent + 312 };   // (CHOOSE A ROOM's, over their buttons)
                mats[PicSub].SetTexture("_MainTex", pictures != null ? pictures : Texture2D.whiteTexture);
                // CONTACT SHADOWS (TASKS.md E38): black through the blended pass, the soft dot's alpha darkening; as native,
                // after the arena's own layers (MdlImporter's DrawOrder: Transparent + 0-199) and before the glows (+300)
                mats[ShadowSub] = new Material(text) { name = "shadows", renderQueue = (int)RenderQueue.Transparent + 290 };
                mats[ShadowSub].SetTexture("_MainTex", dot);
                // the solid boards (native 1159a46 drawPanelSolid): opaque, depth written, with the opaque things, so what
                // lies behind them (the court below the lobby's balcony) is never drawn
                mats[SolidSub] = new Material(text) { name = "solid", renderQueue = (int)RenderQueue.Geometry + 10 };
                mats[SolidSub].SetTexture("_MainTex", Texture2D.whiteTexture);
                mats[SolidSub].SetFloat("_SrcBlend", (float)BlendMode.One);
                mats[SolidSub].SetFloat("_DstBlend", (float)BlendMode.Zero);
                mats[SolidSub].SetFloat("_ZWrite", 1f);
            }
            BuildSphere();
            if (cuttable) foreach (var m in mats) m?.SetFloat("_Cuttable", 1f);
        }

        // native makeSoftTexture: 0 = a soft dot, 1 = a soft ring (64 x 64, linear, mipmapped, clamped)
        static Texture2D SoftTexture(int kind)
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; ++y)
                for (int x = 0; x < N; ++x)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                    float a = kind == 0 ? Mathf.Exp(-r * r * 5.5f) * (1f - Mathf.Min(1f, r) * 0.15f)
                                        : Mathf.Exp(-(r - 0.72f) * (r - 0.72f) / (2 * 0.075f * 0.075f));
                    if (r > 0.98f) a *= Mathf.Max(0f, (1f - r) / 0.02f);
                    byte c = (byte)Mathf.Min(255f, a * 255f);
                    px[y * N + x] = new Color32(c, c, c, c);
                }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        // native UV sphere (16 rings, 24 segments), radius 1
        static void BuildSphere()
        {
            if (sphereV != null) return;
            const int R = 16, S = 24;
            var v = new List<Vector3>();
            for (int i = 0; i <= R; ++i)
                for (int j = 0; j <= S; ++j)
                {
                    float th = M.kPi * i / R, ph = 2 * M.kPi * j / S;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                }
            var ix = new List<int>();
            for (int i = 0; i < R; ++i)
                for (int j = 0; j < S; ++j)
                {
                    int a = i * (S + 1) + j, b = a + S + 1;
                    ix.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            sphereV = v.ToArray();
            sphereI = ix.ToArray();
        }

        static Vector3 U(Vec3 p) => new Vector3(p.x, p.y, -p.z);   // native -> Unity axes

        void Add(Vec3 p, float u, float v, Vector4 rgba, byte alpha = 255)
        {
            pos.Add(U(p));
            uv.Add(new Vector2(u, v));
            tint.Add(rgba);
            col.Add(new Color32(255, 255, 255, alpha));
        }

        // a unit quad (+-0.5) at the pose, scaled (native quad: uv (0,1) at the bottom left)
        void Quad(int sub, Quat q, Vec3 c, float size, Vector4 rgba)
        {
            int b = pos.Count;
            float h = 0.5f * size;
            Add(c + q.Rotate(new Vec3(-h, -h, 0)), 0, 1, rgba);
            Add(c + q.Rotate(new Vec3(h, -h, 0)), 1, 1, rgba);
            Add(c + q.Rotate(new Vec3(h, h, 0)), 1, 0, rgba);
            Add(c + q.Rotate(new Vec3(-h, h, 0)), 0, 0, rgba);
            idx[sub].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        // a glow facing the viewer (native drawSprite; tex 0 = dot, 1 = ring)
        public void Sprite(Vec3 c, float size, Vector4 rgba, Vec3 viewer, int tex, float roll = 0)
        {
            Vec3 n = viewer - c;
            if (M.Length2(n) < 1e-6f) n = new Vec3(0, 0, 1);
            Quat q = M.FromTo(new Vec3(0, 0, 1), M.Normalize(n)) * Quat.AxisAngle(new Vec3(0, 0, 1), roll);
            Quad(tex & 1, q, c, size, rgba);
        }

        // a glow lying on a surface (native drawSpriteFlat)
        public void SpriteFlat(Vec3 c, float size, Vector4 rgba, Vec3 n, int tex) =>
            Quad(tex & 1, M.FromTo(new Vec3(0, 0, 1), M.Normalize(n)), c, size, rgba);

        // a soft shadow lying on a surface (native drawShadowFlat: CONTACT SHADOWS, TASKS.md E38)
        public void ShadowFlat(Vec3 c, float size, float alpha, Vec3 n) =>
            Quad(ShadowSub, M.FromTo(new Vec3(0, 0, 1), M.Normalize(n)), c, size, new Vector4(0, 0, 0, alpha));

        // a glowing sphere (native drawSphere, additive)
        public void Glow(Vec3 c, float r, Vector4 rgba)
        {
            int b = pos.Count;
            foreach (var v in sphereV) Add(c + new Vec3(v.x, v.y, v.z) * r, 0.5f, 0.5f, rgba);
            foreach (var i in sphereI) idx[SphereSub].Add(b + i);
        }

        // a quad of the given size at a pose (native Mat4::fromPose(pose, {w, h, 1}) * the unit quad); uv rect u0..u1;
        // solid: no texture (a panel: uv -1)
        void Rect(int sub, Pose pose, float w, float h, Vector4 rgba, float u0 = 0, float u1 = 1, bool solid = false)
        {
            int b = pos.Count;
            float x = 0.5f * w, y = 0.5f * h;
            float v0 = solid ? -1 : 0, v1 = solid ? -1 : 1;
            // (Unity's textures start at the top row: v is 1 at the top, as the native GL quad samples it)
            Add(pose.Transform(new Vec3(-x, -y, 0)), u0, v0, rgba);
            Add(pose.Transform(new Vec3(x, -y, 0)), u1, v0, rgba);
            Add(pose.Transform(new Vec3(x, y, 0)), u1, v1, rgba);
            Add(pose.Transform(new Vec3(-x, y, 0)), u0, v1, rgba);
            idx[sub].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        // a flat coloured board (native drawPanel: unlit, blended, alpha * 0.998)
        public void Panel(Pose pose, float w, float h, Vector4 rgba) =>
            Rect(TextSub, pose, w, h, new Vector4(rgba.x, rgba.y, rgba.z, rgba.w * 0.998f), solid: true);   // (with the text, in order)

        // a solid board (native drawPanelSolid, 1159a46): unlit, opaque, drawn with the opaque things (the lobby's deck and
        // its rail sign's backing)
        public void PanelSolid(Pose pose, float w, float h, Vector4 rgb) =>
            Rect(SolidSub, pose, w, h, new Vector4(rgb.x, rgb.y, rgb.z, 1f), solid: true);

        // a board from its centre and two edge directions (native drawPanel with any model matrix: ax * w across, ay * h
        // up): the EXPERIMENTAL rooms' walls, floors and decks (drawn before the glows: their lines show over them)
        public void Wall(Vec3 c, Vec3 ax, Vec3 ay, float w, float h, Vector4 rgba)
        {
            var col = new Vector4(rgba.x, rgba.y, rgba.z, rgba.w * 0.998f);
            Vec3 x = ax * (0.5f * w), y = ay * (0.5f * h);
            int b = pos.Count;
            Add(c - x - y, 0, -1, col);
            Add(c + x - y, 1, -1, col);
            Add(c + x + y, 1, -1, col);
            Add(c - x + y, 0, -1, col);
            idx[WallSub].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        // text centred on the pose, facing its +Z (native drawText): one glyph quad each, 6:8, upper case; '_' is the
        // dash dropped to the baseline; characters outside ' '..'Z' show as a space
        public void Text(string s, Pose pose, float height, Vector4 rgba)
        {
            float cw = height * 6f / 8f;
            float total = cw * s.Length;
            for (int i = 0; i < s.Length; ++i)
            {
                char c = s[i];
                if (c >= 'a' && c <= 'z') c = (char)(c - 32);
                bool under = c == '_';
                int g = under ? '-' - 32 : (c >= 32 && c <= 'Z') ? c - 32 : 0;
                var off = new Vec3(-total * 0.5f + cw * (i + 0.5f), under ? -height * 0.42f : 0f, 0);
                Rect(TextSub, new Pose(pose.q, pose.Transform(off)), cw, height, rgba, g / 59f, (g + 1) / 59f);
            }
        }

        // a textured board (native drawTexturedPanel: unlit, blended, alpha * 0.998): the emblem
        public void Emblem(Pose pose, float w, float h, Vector4 rgba) => Rect(EmblemSub, pose, w, h, new Vector4(rgba.x, rgba.y, rgba.z, rgba.w * 0.998f));

        // a part of the pictures texture on a board (native drawTexturedPanel with its uv's scale and offset): CHOOSE A ROOM
        public void Picture(Pose pose, float w, float h, Vector4 rgba, float u0, float u1, float v0, float v1)
        {
            int b = pos.Count;
            float x = 0.5f * w, y = 0.5f * h;
            var c = new Vector4(rgba.x, rgba.y, rgba.z, rgba.w * 0.998f);
            Add(pose.Transform(new Vec3(-x, -y, 0)), u0, v0, c);
            Add(pose.Transform(new Vec3(x, -y, 0)), u1, v0, c);
            Add(pose.Transform(new Vec3(x, y, 0)), u1, v1, c);
            Add(pose.Transform(new Vec3(-x, y, 0)), u0, v1, c);
            idx[PicSub].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        // a glowing box (native drawBox, additive): the unit cube -1..1 scaled by half (half sizes), at the pose
        public void Box(Pose pose, Vec3 half, Vector4 rgba)
        {
            var corner = new Vec3[8];
            for (int k = 0; k < 8; ++k)
                corner[k] = pose.Transform(new Vec3((k & 1) != 0 ? half.x : -half.x, (k & 2) != 0 ? half.y : -half.y, (k & 4) != 0 ? half.z : -half.z));
            int b = pos.Count;
            foreach (var c in corner) Add(c, 0.5f, 0.5f, rgba);
            int[] faces = { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };
            for (int f = 0; f < 24; f += 4)
                idx[SphereSub].AddRange(new[] { b + faces[f], b + faces[f + 1], b + faces[f + 2], b + faces[f], b + faces[f + 2], b + faces[f + 3] });
        }

        // a feathered ribbon through the points, facing the eye, widening from the tail (native drawRibbon); taper false:
        // the same width and full strength all along (the EXPERIMENTAL rooms' lines)
        public void Ribbon(Vec3[] pts, int n, float width, Vector4 rgba, Vec3 eye, bool taper = true)
        {
            if (n < 2) return;
            n = Mathf.Min(n, 256);
            int b = pos.Count;
            for (int i = 0; i < n; ++i)
            {
                Vec3 d = (i + 1 < n) ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
                Vec3 side = M.Normalize(M.Cross(d, eye - pts[i]));
                float t = (float)i / (n - 1);          // 0 = tail, 1 = head
                float w = taper ? width * (0.15f + 0.85f * t) : width;
                byte a = taper ? (byte)(255 * t * t) : (byte)255;
                Add(pts[i] - side * w, 0, t, rgba, a);
                Add(pts[i] + side * w, 1, t, rgba, a);
            }
            for (int i = 0; i + 1 < n; ++i)
            {   // the native triangle strip as triangles
                int v = b + i * 2;
                idx[RibbonSub].AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
        }

        public int LastVertices { get; private set; }   // (diagnostics / tests: what the last frame drew)
        public int LastRibbonTriangles { get; private set; }

        // uploads this frame's effects and draws them; then starts the next frame empty
        public void Flush() => Flush(null);

        // cam: draw for that camera only (the director's preview); null = every camera
        public void Flush(Camera cam)
        {
            LastVertices = pos.Count;
            LastRibbonTriangles = idx[RibbonSub].Count / 3;
            Build();
            if (pos.Count > 0)
                for (int k = 0; k < Subs; ++k)
                    if (idx[k].Count > 0 && mats[k] != null)
                        Graphics.RenderMesh(new RenderParams(mats[k]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = cam },
                                            mesh, k, Matrix4x4.identity);
            Clear();
        }

        // (editor pictures) this frame's mesh and its materials, to show on an ordinary renderer; then empty
        public Mesh Snapshot(out Material[] materials)
        {
            Build();
            materials = mats;
            var copy = Object.Instantiate(mesh);
            Clear();
            return copy;
        }

        void Build()
        {
            mesh.Clear();
            if (pos.Count == 0) return;
            mesh.SetVertices(pos);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(2, tint);
            mesh.SetColors(col);
            mesh.subMeshCount = Subs;
            for (int k = 0; k < Subs; ++k) mesh.SetTriangles(idx[k], k, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
        }

        void Clear()
        {
            pos.Clear(); uv.Clear(); tint.Clear(); col.Clear();
            foreach (var l in idx) l.Clear();
        }
    }
}
