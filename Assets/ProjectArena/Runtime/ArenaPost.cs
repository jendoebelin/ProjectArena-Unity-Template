// GRAPHICS' post-processing (TASKS.md E38; native render.cpp bloom / darken, main.cpp desktopPost): BLOOM (the bright
// parts blurred and added back: native's dual filter from a quarter size, threshold 0.55, strength 0.55), the PC
// spectator's VIGNETTE (0.28) and AMBIENT OCCLUSION (0.85). In Unity these are URP's own: a global Volume with Bloom and
// Vignette, and the renderer's Screen Space Ambient Occlusion feature (ProjectConfigure.Graphics adds it, off), each on
// only while GRAPHICS has it on (the camera's post-processing off otherwise: nothing paid for). Differences from
// native: URP's bloom is over the whole picture, menus too (native: the world, before fades and blinks); its
// ambient occlusion is URP's (from the depth of what is solid), not native's 16-point mock-up.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectArena
{
    public static class ArenaPost
    {
        public const float BloomThreshold = 0.55f, BloomStrength = 0.55f, VignetteAmount = 0.28f;
        static Volume volume;
        static Bloom bloom;
        static Vignette vignette;
        static bool aoSet, aoOn;

        public static bool BloomOn => bloom != null && bloom.active;
        public static bool VignetteOn => vignette != null && vignette.active;

        static void Make()
        {
            var go = new GameObject("ArenaPost (GRAPHICS)");
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);   // (editor pictures: in their scene)
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "ArenaPost";
            bloom = p.Add<Bloom>();
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomStrength);
            bloom.scatter.Override(0.7f);
            bloom.highQualityFiltering.Override(false);
            vignette = p.Add<Vignette>();
            vignette.intensity.Override(VignetteAmount);
            vignette.smoothness.Override(0.45f);
            volume.profile = p;
        }

        // once a frame: on the camera that draws the picture (keyed: the PC's KEY layer, its see-through background not darkened)
        public static void Apply(Camera cam, bool bloomOn, bool vignetteOn, bool aoWanted, bool keyed)
        {
            if (cam == null) return;
            if (volume == null) Make();
            vignetteOn &= !keyed;
            aoWanted &= !keyed;
            if (bloom.active != bloomOn) bloom.active = bloomOn;
            if (vignette.active != vignetteOn) vignette.active = vignetteOn;
            var data = cam.GetUniversalAdditionalCameraData();
            bool post = bloomOn || vignetteOn;
            if (data != null && data.renderPostProcessing != post) data.renderPostProcessing = post;
            SetAo(aoWanted);
        }

        // AMBIENT OCCLUSION: the renderer's SSAO feature on or off (in a player only: in the editor the asset would keep it)
        static void SetAo(bool on)
        {
            if (aoSet && on == aoOn) return;
            if (Application.isEditor) return;
            aoSet = true;
            aoOn = on;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            foreach (var rd in urp.rendererDataList)
                if (rd != null)
                    foreach (var f in rd.rendererFeatures)
                        if (f is ScreenSpaceAmbientOcclusion) f.SetActive(on);
            Debug.Log($"[Graphics] ambient occlusion {(on ? "on" : "off")}");
        }
    }
}
