// THE DECK and its light bar (TASKS.md E40; native fb93914 / a812558 drawDeck; the owner, 2026-11-19: "in fireball blitz
// there are gaps in the floor where player four in queue is floating"; "close all of that with a solid floor. Diamond
// still needs to sit in the floor ... but behind the players can be flat"; "in all the courts like square, there is a
// light bar on the ground that separates their area from the rest of the floor"). The stadium was built round the
// classic court: its walkway stops short of the stands, and the EXPERIMENTAL rooms stand in it widened (x 1.35). One
// flat, solid, matte floor at the height people stand (3.5 cm under the walkway, which shows as it did), from the room
// out to the stands' front (14 x 7.85 m; 18.9 x 10.6 widened), a hole the room's shape at its glass: the SQUARE's court,
// DIAMOND's as wide as it meets the floor, FOUR CORNERS' turned square; FIREBALL BLITZ's room stands on it. In the
// walkway's colour; not in PASSTHROUGH. In the widened rooms the light bar is drawn on it (17.4 x 9.2 m, 1.8 cm).
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectArena
{
    public class Deck
    {
        static readonly Vector4 Walk = new Vector4(0.0145f, 0.0166f, 0.0232f, 1);   // (the walkway as drawn: about sRGB 33, 36, 43)
        static readonly Vector4 Bar = new Vector4(0.85f, 0.92f, 1.0f, 1);            // (as the SQUARE's: about sRGB 237, 246, 255)
        public const float Y = -0.035f;
        readonly Material mat;
        readonly Mesh[] meshes = new Mesh[4];

        public Deck(Shader native)
        {
            if (native == null) return;
            mat = new Material(native) { name = "deck", renderQueue = (int)RenderQueue.Geometry };
            mat.EnableKeyword("_PA_LIT");
            mat.SetVector("_Tint", Walk);
            mat.SetVector("_Emissive", new Vector4(0, 0, 0, -1f));   // (matte, as the walkway: no highlight)
            mat.SetFloat("_LitOnly", 1f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One); mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetFloat("_SrcBlendA", (float)BlendMode.One); mat.SetFloat("_DstBlendA", (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
        }

        // 0 the SQUARE (and the lobby's court), 1 DIAMOND, 2 FOUR CORNERS, 3 FIREBALL BLITZ
        public static int KindOf(Game g) => g.Octagon ? 3 : ExpArenaView.CornersRoom(g) ? 2 : g.arenaKind == Arena.Diamond ? 1 : 0;

        // the deck's triangles (native space, each facing up): the ring between the stands' rectangle and the room's
        // outline, through eight matching points round both; FIREBALL BLITZ the whole of it (the room's own floor over it)
        public static void Shape(int k, System.Collections.Generic.List<Vec3> tris)
        {
            tris.Clear();
            bool wide = k >= 2;
            float sc = wide ? 1.35f : 1f, X = 14.0f * sc, Z = 7.85f * sc;
            var outer = new Vec3[8];
            var inner = new Vec3[8];
            float[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 }, dz = { 0, 1, 1, 1, 0, -1, -1, -1 };
            for (int i = 0; i < 8; ++i)
            {
                outer[i] = new Vec3(dx[i] * X, Y, dz[i] * Z);
                if (k == 2)
                {   // FOUR CORNERS: corners at 9.5 on the axes (ExpArena.kCorner), the walls between
                    float r = ExpArena.kCorner + 0.02f, s = (i & 1) != 0 ? 0.5f : 1f;
                    inner[i] = new Vec3(dx[i] * r * s, Y, dz[i] * r * s);
                }
                else
                {   // the court (DIAMOND: as wide as it is where it meets the floor)
                    float hx = ExpArena.kRollLen + 0.02f, hz = k == 1 ? ExpArena.kRollReach - ExpArena.kRollCY + 0.02f : 2.52f;
                    inner[i] = new Vec3(dx[i] * hx, Y, dz[i] * hz);
                }
            }
            void Tri(Vec3 p0, Vec3 p1, Vec3 p2)
            {
                Vec3 n = M.Cross(p1 - p0, p2 - p0);
                tris.Add(p0);
                if (n.y >= 0) { tris.Add(p1); tris.Add(p2); } else { tris.Add(p2); tris.Add(p1); }
            }
            if (k == 3) { Tri(outer[1], outer[3], outer[5]); Tri(outer[1], outer[5], outer[7]); return; }
            for (int i = 0; i < 8; ++i)
            {
                int j = (i + 1) % 8;
                Tri(outer[i], outer[j], inner[j]);
                Tri(outer[i], inner[j], inner[i]);
            }
        }

        Mesh MeshOf(int k)
        {
            if (meshes[k] != null) return meshes[k];
            var t = new System.Collections.Generic.List<Vec3>();
            Shape(k, t);
            var v = new Vector3[t.Count];
            var idx = new int[t.Count];
            var n = new Vector3[t.Count];
            var c = new Color32[t.Count];
            for (int i = 0; i < t.Count; ++i) { v[i] = NativeSpace.ToUnity(t[i]); n[i] = Vector3.up; c[i] = new Color32(255, 255, 255, 255); }
            for (int i = 0; i + 2 < t.Count; i += 3) { idx[i] = i; idx[i + 1] = i + 2; idx[i + 2] = i + 1; }   // (Z negated: the winding flips)
            var m = new Mesh { name = "deck " + k };
            m.vertices = v; m.normals = n; m.colors32 = c; m.uv = new Vector2[t.Count];
            m.triangles = idx;
            m.RecalculateBounds();
            return meshes[k] = m;
        }

        // the deck (cam: that camera only; null every camera)
        public void Draw(Game g, Camera cam = null)
        {
            if (mat == null) return;
            var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = cam };
            Graphics.RenderMesh(rp, MeshOf(KindOf(g)), 0, Matrix4x4.identity);
        }

        // the light bar round the players' area in the widened rooms (the SQUARE's and DIAMOND's is the walkway's own edge)
        public static void LightBar(FxRenderer fx, Game g, Vec3 eye)
        {
            if (KindOf(g) < 2) return;
            const float X = 17.4f, Zb = 9.2f, y = Y + 0.004f;
            var r = new[] { new Vec3(-X, y, -Zb), new Vec3(X, y, -Zb), new Vec3(X, y, Zb), new Vec3(-X, y, Zb), new Vec3(-X, y, -Zb) };
            fx.Ribbon(r, 5, 0.018f, Bar, eye, false);
        }
    }
}
