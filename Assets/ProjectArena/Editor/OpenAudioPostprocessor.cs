// Import settings for the open sounds: decompressed on load as plain PCM at their own rate (48 kHz), so the native
// mixer (Runtime/NativeMixer.cs) can read their samples as the native game does.
using UnityEditor;
using UnityEngine;

namespace ProjectArena.EditorTools
{
    public class OpenAudioPostprocessor : AssetPostprocessor
    {
        const string Folder = "Assets/ProjectArena/Art/Open/Audio/";

        public override uint GetVersion() => 1;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = false;
            ai.loadInBackground = false;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.PCM;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = true;
            ai.defaultSampleSettings = s;
        }
    }
}
