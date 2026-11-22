// Imports the native game's models (.mdl, written by the native repo's tools/build_open_assets.py, read like
// app/src/render.cpp Renderer::loadModel) as a GameObject with a mesh and one ProjectArena/Native material per model
// material, carrying the native values unchanged.
// .mdl (little endian): "SMDL", version (2 rigid, 3 skinned), vertex count, index count, section count, material count;
// vertices: position 3f, normal 3f, uv0 2f, uv1 2f, colour 4 x u8 (44 bytes; version 3 adds bone indices and weights,
// 4 x u8 each: 52 bytes); indices u32; sections: first index, index count, material (3 x u32); materials: shader u32,
// tint 4f, emissive 4f (a = gloss / metallic), uv set u32, flags u32 (bit 0 two-sided, 16 suit pattern, 32 garment
// design, 64 cloth, bits 8-11 colour channel), diffuse texture name (64 bytes), mask texture name (64 bytes).
// Skinned models (the avatar parts) keep their bone indices in UV channel 2 and weights in UV channel 3 (Native.shader
// skins them with the 7 bone matrices of the draw).
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectArena.EditorTools
{
    [ScriptedImporter(6, "mdl")]   // (5: FAR AWAY's lighter triangles, TASKS.md E39; 6: THE CUTAWAY's flag, E42)
    public class MdlImporter : ScriptedImporter
    {
        const string Textures = "Assets/ProjectArena/Art/Open/Textures/";
        const string ShaderPath = "Assets/ProjectArena/Shaders/Native.shader";
        // native shader kinds (render.h): 0 lit, 1 court (CCP court mask: not in the open set), 2 additive, 3 unlit,
        // 4 translucent, 5 lightmapped
        const int ShLit = 0, ShCourt = 1, ShAdditive = 2, ShUnlit = 3, ShTranslucent = 4, ShLightmap = 5;
        const uint TwoSided = 1, Pattern = 16, Design = 32, Cloth = 64;

        // The arena's blended layers, in the order main.cpp draws them (pass 1); each gets its own render queue band so
        // Unity keeps that order instead of sorting by distance.
        static readonly string[] DrawOrder =
        {
            "floor_reflect", "floor_reflect_env", "floor", "environment", "arena", "env_fx", "court_fx", "stadium_fx",
            "court_pulse",
        };

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var model = Path.GetFileNameWithoutExtension(ctx.assetPath);
            using var r = new BinaryReader(File.OpenRead(ctx.assetPath));
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "SMDL")
            {
                ctx.LogImportError($"{ctx.assetPath}: not a .mdl file");
                return;
            }
            uint version = r.ReadUInt32(), nv = r.ReadUInt32(), ni = r.ReadUInt32(), ns = r.ReadUInt32(), nm = r.ReadUInt32();

            var pos = new Vector3[nv];
            var nrm = new Vector3[nv];
            var uv0 = new Vector2[nv];
            var uv1 = new Vector2[nv];
            var col = new Color32[nv];
            bool skinned = version >= 3;
            var bone = skinned ? new Vector4[nv] : null;
            var weight = skinned ? new Vector4[nv] : null;
            for (int i = 0; i < nv; i++)
            {
                pos[i] = NativeSpace.ToUnity(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                nrm[i] = NativeSpace.ToUnity(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                // GL textures start at the bottom row, Unity's PNG import at the top: flip v
                uv0[i] = new Vector2(r.ReadSingle(), 1f - r.ReadSingle());
                uv1[i] = new Vector2(r.ReadSingle(), 1f - r.ReadSingle());
                col[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                if (skinned)
                {   // bone indices (as numbers) and weights (normalised bytes, as the native vertex layout)
                    bone[i] = new Vector4(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                    weight[i] = new Vector4(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f);
                }
            }
            var idx = new int[ni];
            for (int i = 0; i < ni; i++) idx[i] = (int)r.ReadUInt32();
            var sections = new (int first, int count, int mat)[ns];
            for (int i = 0; i < ns; i++) sections[i] = ((int)r.ReadUInt32(), (int)r.ReadUInt32(), (int)r.ReadUInt32());

            var mats = new Material[nm];
            for (int i = 0; i < nm; i++)
            {
                int shader = (int)r.ReadUInt32();
                var tint = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var em = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                uint uv = r.ReadUInt32(), flags = r.ReadUInt32();
                string diffuse = Name(r.ReadBytes(64)), mask = Name(r.ReadBytes(64));
                mats[i] = MakeMaterial(ctx, model, i, shader, tint, em, uv, flags, diffuse, mask);
                ctx.AddObjectToAsset($"mat{i}", mats[i]);
            }

            // Pieces of at most 65535 vertices, each a mesh with 16-bit indices (one piece for every model but the
            // environment). Quest drew the environment's single 32-bit-index mesh as stretched streaks; 16-bit indices
            // are also the faster format on mobile GPUs.
            var pieces = new List<List<(List<int> tri, Material mat)>>();
            var cur = new List<(List<int> tri, Material mat)>();
            var used = new HashSet<int>();
            for (int s = 0; s < ns; s++)
            {
                var (first, count, mat) = sections[s];
                var tri = new List<int>(count);
                for (int k = 0; k < count; k += 3)
                {
                    // Z is negated, so the winding flips: swap two corners to keep front faces in front
                    int a = idx[first + k], b = idx[first + k + 2], c = idx[first + k + 1];
                    int added = (used.Contains(a) ? 0 : 1) + (used.Contains(b) ? 0 : 1) + (used.Contains(c) ? 0 : 1);
                    if (used.Count + added > 65535)
                    {
                        if (tri.Count > 0) cur.Add((tri, mats[mat]));
                        pieces.Add(cur);
                        cur = new List<(List<int> tri, Material mat)>();
                        used.Clear();
                        tri = new List<int>();
                    }
                    tri.Add(a); tri.Add(b); tri.Add(c);
                    used.Add(a); used.Add(b); used.Add(c);
                }
                if (tri.Count > 0) cur.Add((tri, mats[mat]));
            }
            if (cur.Count > 0) pieces.Add(cur);

            var go = new GameObject(model);
            for (int p = 0; p < pieces.Count; p++)
            {
                var remap = new Dictionary<int, int>();
                var order = new List<int>();
                foreach (var (tri, _) in pieces[p])
                    foreach (var v in tri)
                        if (!remap.ContainsKey(v)) { remap[v] = order.Count; order.Add(v); }
                var mesh = new Mesh { name = pieces.Count == 1 ? model : $"{model}_{p}", indexFormat = IndexFormat.UInt16 };
                mesh.vertices = order.ConvertAll(v => pos[v]).ToArray();
                mesh.normals = order.ConvertAll(v => nrm[v]).ToArray();
                mesh.uv = order.ConvertAll(v => uv0[v]).ToArray();
                mesh.uv2 = order.ConvertAll(v => uv1[v]).ToArray();
                mesh.colors32 = order.ConvertAll(v => col[v]).ToArray();
                if (skinned)
                {
                    mesh.SetUVs(2, order.ConvertAll(v => bone[v]));
                    mesh.SetUVs(3, order.ConvertAll(v => weight[v]));
                }
                // FAR AWAY (TASKS.md E39, native fb93914 tools/make_lods.cpp): <model>.lod's lighter triangles over the same
                // vertices, as more submeshes after the model's own (the same materials, one each: AvatarDraw draws
                // submesh s + the material count for a part far away). One piece only (the kilt's pieces draw whole)
                List<List<int>> far = pieces.Count == 1 ? FarTriangles(ctx, model, idx, sections, remap) : null;
                mesh.subMeshCount = pieces[p].Count * (far != null ? 2 : 1);
                var shared = new Material[pieces[p].Count];
                for (int s = 0; s < pieces[p].Count; s++)
                {
                    var (tri, mat) = pieces[p][s];
                    mesh.SetTriangles(tri.ConvertAll(v => remap[v]), s, false);
                    shared[s] = mat;
                }
                if (far != null)
                    for (int s = 0; s < pieces[p].Count; s++) mesh.SetTriangles(far[s], pieces[p].Count + s, false);
                mesh.RecalculateBounds();
                ctx.AddObjectToAsset($"mesh{p}", mesh);

                var holder = pieces.Count == 1 ? go : new GameObject(mesh.name);
                if (holder != go) holder.transform.SetParent(go.transform, false);
                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = holder.AddComponent<MeshRenderer>();
                mr.sharedMaterials = shared;
                // the native game has no shadows or Unity lighting: its shader lights everything itself
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            ctx.AddObjectToAsset("main", go);
            ctx.SetMainObject(go);
        }

        // <model>.lod (Data/<model>.lod.bytes): 'SLOD', u32 version (1: u32 indices, 2: u16), u32 sections, each section's
        // first and count, u32 indices, the indices (the .mdl's vertices). Each kept section's lighter triangles in the
        // mesh's vertices (null: none, or one that doesn't fit the model)
        const string Data = "Assets/ProjectArena/Art/Open/Data/";
        static List<List<int>> FarTriangles(AssetImportContext ctx, string model, int[] idx, (int first, int count, int mat)[] sections,
                                            Dictionary<int, int> remap)
        {
            string path = Data + model + ".lod.bytes";
            if (!File.Exists(path)) return null;
            ctx.DependsOnSourceAsset(path);
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 16 || Encoding.ASCII.GetString(d, 0, 4) != "SLOD") return null;
            uint lv = System.BitConverter.ToUInt32(d, 4), lns = System.BitConverter.ToUInt32(d, 8);
            int isz = lv == 2 ? 2 : 4, at = 12 + (int)lns * 8;
            if ((lv != 1 && lv != 2) || lns != sections.Length || d.Length < at + 4) { ctx.LogImportWarning($"{model}: its .lod doesn't fit it"); return null; }
            uint lni = System.BitConverter.ToUInt32(d, at);
            if (d.Length < at + 4 + (long)lni * isz) { ctx.LogImportWarning($"{model}: its .lod is short"); return null; }
            int Index(long k) => isz == 2 ? System.BitConverter.ToUInt16(d, at + 4 + (int)k * 2) : (int)System.BitConverter.ToUInt32(d, at + 4 + (int)k * 4);
            var o = new List<List<int>>();
            for (int s = 0; s < sections.Length; ++s)
            {
                if (sections[s].count < 3) continue;   // (as the model's own: a section without triangles is left out)
                uint first = System.BitConverter.ToUInt32(d, 12 + s * 8), count = System.BitConverter.ToUInt32(d, 16 + s * 8);
                if ((long)first + count > lni) { ctx.LogImportWarning($"{model}: its .lod doesn't fit it"); return null; }
                var tri = new List<int>((int)count);
                for (long k = 0; k + 2 < count; k += 3)
                {   // (Z is negated, so the winding flips, as the model's own)
                    int a = Index(first + k), b = Index(first + k + 2), c = Index(first + k + 1);
                    if (!remap.TryGetValue(a, out int ra) || !remap.TryGetValue(b, out int rb) || !remap.TryGetValue(c, out int rc))
                    { ctx.LogImportWarning($"{model}: its .lod uses a vertex the model doesn't"); return null; }
                    tri.Add(ra); tri.Add(rb); tri.Add(rc);
                }
                o.Add(tri);
            }
            return o;
        }

        static string Name(byte[] b)
        {
            int n = System.Array.IndexOf(b, (byte)0);
            return Encoding.ASCII.GetString(b, 0, n < 0 ? b.Length : n);
        }

        static Material MakeMaterial(AssetImportContext ctx, string model, int i, int shader, Vector4 tint, Vector4 em,
            uint uv, uint flags, string diffuse, string mask)
        {
            // by path, declared as a dependency: imported first, and the models reimport when it changes
            ctx.DependsOnArtifact(ShaderPath);
            var m = new Material(AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath)) { name = $"{model}_mat{i}" };
            if (shader == ShCourt)
            {
                ctx.LogImportWarning($"{model} material {i}: court shader (not in the open set) drawn as lit");
                shader = ShLit;
            }
            // per-draw settings main.cpp gives these models (DrawParams)
            float alphaMul = model == "floor" ? 0.80f : 1f;   // the glossy floor: the mirrored lights show through it
            var scroll = model == "env_fx" ? new Vector2(0.035f, 0f)   // crowd lights twinkle (uvOff[0] = now * 0.035)
                : model == "court_pulse" ? new Vector2(0f, 0.9f)       // pulses along the outline (uvOff[1] = -now * 0.9;
                : Vector2.zero;                                        //  v is flipped here, so the sign turns)
            tint.w *= alphaMul;

            // values exactly as the native renderer passes them (vectors: no colour-space conversion)
            m.SetVector("_Tint", tint);
            m.SetVector("_Emissive", em);
            m.SetFloat("_UVSet", uv == 1 ? 1f : 0f);
            m.SetFloat("_LitOnly", shader == ShLit ? 1f : 0f);
            m.SetFloat("_Channel", (flags >> 8) & 15);   // (native matChannel)
            m.SetFloat("_PatternMat", (flags & Pattern) != 0 ? 1f : 0f);
            m.SetFloat("_DesignMat", (flags & Design) != 0 ? 1f : 0f);
            m.SetFloat("_ClothMat", (flags & Cloth) != 0 ? 1f : 0f);
            if ((flags & Cloth) != 0) m.SetTexture("_FabricTex", Texture(ctx, model, "open_fabric"));
            m.SetVector("_UVScroll", scroll);
            // THE CUTAWAY (TASKS.md E42, native 7240d49): the models native cuts for a PC preset camera; the floor's mirror
            // image with its heights mirrored
            m.SetFloat("_Cuttable", model == "arena" || model == "court_fx" || model == "court_pulse" ? 1f : model == "floor_reflect" ? -1f : 0f);
            var tex = Texture(ctx, model, diffuse);
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_HasDiffuse", tex != null ? 1f : 0f);
            var mk = Texture(ctx, model, mask);
            m.SetTexture("_MaskTex", mk);
            m.SetFloat("_HasMask", mk != null ? 1f : 0f);

            m.EnableKeyword(shader == ShAdditive ? "_PA_ADD" : shader == ShUnlit ? "_PA_UNLIT"
                : shader == ShLightmap ? "_PA_LIGHTMAP" : "_PA_LIT");

            // render state as Renderer::draw sets it
            bool blended = shader == ShAdditive || shader == ShTranslucent || alphaMul < 0.999f;
            if (shader == ShAdditive)
                SetBlend(m, BlendMode.One, BlendMode.One, BlendMode.Zero, BlendMode.One);
            else if (blended)
                SetBlend(m, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, BlendMode.One, BlendMode.OneMinusSrcAlpha);
            else
                SetBlend(m, BlendMode.One, BlendMode.Zero, BlendMode.One, BlendMode.Zero);
            m.SetFloat("_ZWrite", blended ? 0f : 1f);
            m.SetFloat("_Cull", (flags & TwoSided) != 0 || blended ? (float)CullMode.Off : (float)CullMode.Back);
            if (blended)
            {
                int order = System.Array.IndexOf(DrawOrder, model);
                m.renderQueue = (int)RenderQueue.Transparent + (order < 0 ? DrawOrder.Length : order) * 20 + Mathf.Min(i, 19);
                m.SetOverrideTag("RenderType", "Transparent");
            }
            else m.renderQueue = (int)RenderQueue.Geometry;
            return m;
        }

        static void SetBlend(Material m, BlendMode src, BlendMode dst, BlendMode srcA, BlendMode dstA)
        {
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_SrcBlendA", (float)srcA);
            m.SetFloat("_DstBlendA", (float)dstA);
        }

        static Texture2D Texture(AssetImportContext ctx, string model, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var path = Textures + name.Replace('/', '_') + ".png";
            ctx.DependsOnArtifact(path);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) ctx.LogImportWarning($"{model}: missing texture {path}");
            return t;
        }
    }
}
