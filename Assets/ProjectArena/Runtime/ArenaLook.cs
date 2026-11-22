using UnityEngine;

namespace ProjectArena
{
    // The arena's look: the shader globals the native Renderer sets (render.cpp setLight / setSideColors).
    // Draw order, the floor's opacity and the scrolling glow are per-material settings from MdlImporter.
    [ExecuteAlways]
    public class ArenaLook : MonoBehaviour
    {
        // team 0 = (0.1, 0.7, 1.0), team 1 = (1.0, 0.4, 0.1) (main.cpp sideRGB)
        public Color side0 = new Color(0.1f, 0.7f, 1.0f);
        public Color side1 = new Color(1.0f, 0.4f, 0.1f);
        // the ends are recoloured only for countries (NATIONS) or crowd flare; otherwise they keep their built colours
        public bool sidesOn;

        static readonly int LightDir = Shader.PropertyToID("_PA_LightDir");
        static readonly int Side0 = Shader.PropertyToID("_PA_Side0");
        static readonly int Side1 = Shader.PropertyToID("_PA_Side1");
        static readonly int SidesOn = Shader.PropertyToID("_PA_SidesOn");

        void OnEnable() => Apply();
        void Update() => Apply();

        void Apply()
        {
            // the native key light, nearly overhead: normalize(0.3, -1, 0.2) in native axes
            Shader.SetGlobalVector(LightDir, NativeSpace.ToUnity(new Vector3(0.3f, -1.0f, 0.2f).normalized));
            // raw values, as the native uniforms (no colour-space conversion)
            Shader.SetGlobalVector(Side0, new Vector4(side0.r, side0.g, side0.b, 1));
            Shader.SetGlobalVector(Side1, new Vector4(side1.r, side1.g, side1.b, 1));
            Shader.SetGlobalFloat(SidesOn, sidesOn ? 1f : 0f);
        }
    }
}
