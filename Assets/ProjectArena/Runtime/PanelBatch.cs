using System.Collections.Generic;
using ProjectArena.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Pose = ProjectArena.Sim.Pose;

namespace ProjectArena
{
    // Textured quads in native space (native Renderer::drawTexturedPanel: unlit, alpha blended, two-sided, tint times
    // texture, an optional uv rectangle), collected over a frame and drawn as one mesh per texture with Text.shader:
    // the fans' flags strip by strip, foam fingers, the cheer bursts, the director's preview. Flush draws and empties.
    public class PanelBatch
    {
        class Batch { public readonly List<Vector3> pos = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<Vector4> tint = new List<Vector4>(); public readonly List<int> idx = new List<int>(); public Mesh mesh; }
        readonly Dictionary<Texture, Batch> batches = new Dictionary<Texture, Batch>();
        readonly Material mat;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int MainTex = Shader.PropertyToID("_MainTex");

        public PanelBatch(Shader text, int queueOffset = 312)
        {
            if (text != null) mat = new Material(text) { name = "panels", renderQueue = (int)RenderQueue.Transparent + queueOffset };
        }

        // a quad centred on the pose, w x h metres, facing its +Z; uv = (u scale, v scale, u offset, v offset) in the
        // native convention (v down from the image's top); null = the whole image
        public void Quad(Texture tex, Pose at, float w, float h, Vector4 tint, Vector4? uvRect = null)
        {
            if (tex == null || mat == null) return;
            if (!batches.TryGetValue(tex, out Batch b)) batches[tex] = b = new Batch();
            Vector4 r = uvRect ?? new Vector4(1, 1, 0, 0);
            int i = b.pos.Count;
            float hw = w * 0.5f, hh = h * 0.5f;
            void V(float x, float y, float u, float v)
            {
                b.pos.Add(NativeSpace.ToUnity(at.Transform(new Vec3(x, y, 0))));
                float nu = u * r.x + r.z, nv = v * r.y + r.w;   // (native uv, v down)
                b.uv.Add(new Vector2(nu, 1f - nv));
                b.tint.Add(tint);
            }
            V(-hw, -hh, 0, 1); V(hw, -hh, 1, 1); V(hw, hh, 1, 0); V(-hw, hh, 0, 0);
            b.idx.Add(i); b.idx.Add(i + 2); b.idx.Add(i + 1); b.idx.Add(i); b.idx.Add(i + 3); b.idx.Add(i + 2);
        }

        public void Flush(Camera cam = null)
        {
            foreach (var kv in batches)
            {
                Batch b = kv.Value;
                if (b.pos.Count == 0) continue;
                if (b.mesh == null) { b.mesh = new Mesh { name = "panels" }; b.mesh.MarkDynamic(); }
                b.mesh.Clear();
                b.mesh.indexFormat = b.pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                b.mesh.SetVertices(b.pos);
                b.mesh.SetUVs(0, b.uv);
                b.mesh.SetUVs(2, b.tint);
                b.mesh.SetTriangles(b.idx, 0, false);
                b.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
                var blk = new MaterialPropertyBlock();
                blk.SetTexture(MainTex, kv.Key);
                Graphics.RenderMesh(new RenderParams(mat) { matProps = blk, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = cam },
                                    b.mesh, 0, Matrix4x4.identity);
                b.pos.Clear(); b.uv.Clear(); b.tint.Clear(); b.idx.Clear();
            }
        }
    }
}
