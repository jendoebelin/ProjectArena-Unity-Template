// Import settings for the converted open textures, matching how the native game loads them (render.cpp loadTexture):
// colour textures sRGB, masks linear (garment designs, suit patterns, fabric, font), mipmaps with trilinear filtering,
// repeat wrap. On Quest the baked lightmaps get ASTC 4x4 (finer than the default) so their gradients don't band.
using UnityEditor;
using UnityEngine;

namespace ProjectArena.EditorTools
{
    public class OpenTexturePostprocessor : AssetPostprocessor
    {
        const string Folder = "Assets/ProjectArena/Art/Open/Textures/";

        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !(name.StartsWith("open_design_") || name.StartsWith("open_pat_") || name == "open_fabric" ||
                               name == "open_font");
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Repeat;
            if (name.EndsWith("_lm"))
            {
                var android = ti.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.format = TextureImporterFormat.ASTC_4x4;
                ti.SetPlatformTextureSettings(android);
            }
        }
    }
}
