// Repeatable project configuration (Phase 1), run from the command line after ProjectSetup.AddPackages:
//   Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.ProjectConfigure.Run -logFile -
// Player settings (Quest + PC), URP for every quality level, OpenXR with the Meta Quest feature group on Android and
// controller profiles on PC, and the Arena scene (the open arena models and an XR Origin).
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace ProjectArena.EditorTools
{
    public static class ProjectConfigure
    {
        const string Settings = "Assets/ProjectArena/Settings";
        const string ScenePath = "Assets/ProjectArena/Scenes/Arena.unity";
        const string Models = "Assets/ProjectArena/Art/Open/Models/";
        // the static arena the native game draws in a match (main.cpp; court_outline is its OUTLINE view, the spawners
        // stand where the rules put them and come with phase 2)
        static readonly string[] ArenaModels =
        {
            "environment", "floor_reflect", "floor_reflect_env", "floor", "arena", "env_fx", "court_fx", "stadium_fx",
            "court_pulse", "court_outline",   // (court_outline: PASSTHROUGH > OUTLINE, hidden otherwise)
        };
        // The only OpenXR features turned on, by type name (found by name so a renamed type is reported, not a compile
        // error); every other feature is turned off. On Quest that keeps out the Meta scene / anchor / colocation /
        // boundary / camera features and their permissions (room data the game doesn't use; passthrough is phase 6).
        static readonly string[] AndroidFeatures =
        {
            "MetaQuestFeature", "OpenXRLifeCycleFeature", "ARSessionFeature", "DisplayUtilitiesFeature",
            "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile", "HandTracking", "RoomFeelFeature", "PassthroughFeature",
            "FoveatedRenderingFeature",   // (FOVEATION, TASKS.md E38: XR_FB_foveation, fixed)
        };
        static readonly string[] PcFeatures =
        {
            "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile", "ValveIndexControllerProfile",
            "HTCViveControllerProfile", "HandTracking", "RoomFeelFeature", "PassthroughFeature",
        };

        // FIELD OF VIEW's OpenXR feature on, for Quest and PC, without the rest of Run (task C6):
        //   -executeMethod ProjectArena.EditorTools.ProjectConfigure.EnableRoomFeel
        public static void EnableRoomFeel()
        {
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                FeatureHelpers.RefreshFeatures(group);
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                var f = settings.GetFeature<RoomFeelFeature>();
                Debug.Log($"[ProjectConfigure] {group}: RoomFeelFeature {(f == null ? "NOT FOUND" : "on")}");
                if (f == null) continue;
                f.enabled = true;
                EditorUtility.SetDirty(f);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
        }

        // PASSTHROUGH (task D1), without the rest of Run: its OpenXR feature on for Quest and PC, and the court's
        // outline (OUTLINE) in the scene's Arena, hidden:
        //   -executeMethod ProjectArena.EditorTools.ProjectConfigure.SetUpPassthrough
        public static void SetUpPassthrough()
        {
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                FeatureHelpers.RefreshFeatures(group);
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                var f = settings.GetFeature<PassthroughFeature>();
                Debug.Log($"[ProjectConfigure] {group}: PassthroughFeature {(f == null ? "NOT FOUND" : "on")}");
                if (f == null) continue;
                f.enabled = true;
                EditorUtility.SetDirty(f);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var arena = GameObject.Find("Arena");
            if (arena != null && arena.transform.Find("court_outline") == null)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "court_outline.mdl");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                go.transform.SetParent(arena.transform, false);
                go.isStatic = true;
                go.SetActive(false);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[ProjectConfigure] court_outline added to the scene");
            }
        }

        // only the avatar library, after new open art (tools/convert_assets.py): -executeMethod ...ProjectConfigure.AvatarLibraryOnly
        public static void AvatarLibraryOnly()
        {
            AvatarLibraryAsset();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectConfigure] avatar library done");
        }

        public static void Run()
        {
            Player();
            Urp();
            Xr(BuildTargetGroup.Android, AndroidFeatures);
            Xr(BuildTargetGroup.Standalone, PcFeatures);
            Scene();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectConfigure] done");
        }

        static void Player()
        {
            PlayerSettings.companyName = "Project Arena";
            PlayerSettings.productName = "Project Arena Unity";
            PlayerSettings.bundleVersion = Online.Net.GameVersion;   // (the native version it plays as, TASKS.md E1)
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.projectarena.unity");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.projectarena.unity");
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            // Active Input Handling = Input System only (head tracking and XRI read it; the old Input Manager would
            // leave the camera still, so the view stays locked to the face). No public API: set the serialized value.
            // Takes effect when the editor next starts (each command-line run is a fresh start).
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            ps.FindProperty("activeInputHandler").intValue = 1;
            ps.ApplyModifiedPropertiesWithoutUndo();
            // audio out at 48 kHz, the native mixer's rate (Unity otherwise picked 24 kHz on Quest)
            var am = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
            am.FindProperty("m_SampleRate").intValue = 48000;
            am.ApplyModifiedPropertiesWithoutUndo();
        }

        // GRAPHICS (TASKS.md E38; Runtime/ArenaPost.cs): the renderer's post-processing data (URP's own: BLOOM and VIGNETTE)
        // and an AMBIENT OCCLUSION feature (URP's SSAO from the depth of what is solid, after the opaque things; off until
        // the PC spectator's GRAPHICS turns it on). Alone: -executeMethod ProjectArena.EditorTools.ProjectConfigure.GraphicsOnly
        public static void GraphicsOnly()
        {
            Graphics(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Settings + "/ArenaRenderer.asset"));
            AssetDatabase.SaveAssets();
        }

        static void Graphics(UniversalRendererData renderer)
        {
            if (renderer == null) return;
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            ScreenSpaceAmbientOcclusion ao = null;
            foreach (var f in renderer.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion s) ao = s;
            if (ao == null)
            {
                ao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ao.name = "AmbientOcclusion";
                AssetDatabase.AddObjectToAsset(ao, renderer);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ao, out string _, out long id);
                var so = new SerializedObject(renderer);
                var feats = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                feats.arraySize++;
                feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = ao;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var set = new SerializedObject(ao);
            set.FindProperty("m_Settings.AfterOpaque").boolValue = true;      // (after the opaque things: their depth copied)
            set.FindProperty("m_Settings.Source").enumValueIndex = 0;         // DEPTH (the port's shaders have no normals pass)
            set.FindProperty("m_Settings.Downsample").boolValue = true;       // (half size, as native's)
            set.FindProperty("m_Settings.Intensity").floatValue = 1.0f;
            set.FindProperty("m_Settings.Radius").floatValue = 0.25f;
            set.ApplyModifiedPropertiesWithoutUndo();
            ao.SetActive(false);
            EditorUtility.SetDirty(ao);
            EditorUtility.SetDirty(renderer);
            Debug.Log($"[ProjectConfigure] graphics: post-processing data {(renderer.postProcessData != null ? "set" : "NOT FOUND")}, ambient occlusion feature (off)");
        }

        static void Urp()
        {
            System.IO.Directory.CreateDirectory(Settings);
            var rendererPath = Settings + "/ArenaRenderer.asset";
            var assetPath = Settings + "/ArenaURP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, assetPath);
            }
            // mobile VR: no MSAA (SMOOTH EDGES OFF by default, TASKS.md E38: the game sets it), no HDR, no extra depth /
            // opaque copies, short shadows
            urp.msaaSampleCount = 1;
            urp.supportsHDR = false;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            urp.shadowDistance = 0f;   // no shadows: the native shader lights everything itself (baked light, AO)
            EditorUtility.SetDirty(urp);
            Graphics(renderer);   // (GRAPHICS: post-processing data, the AMBIENT OCCLUSION feature)
            GraphicsSettings.defaultRenderPipeline = urp;
            var current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void Xr(BuildTargetGroup group, string[] features)
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget))
            {
                System.IO.Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(general.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                Debug.LogError($"[ProjectConfigure] {group}: could not assign the OpenXR loader");

            FeatureHelpers.RefreshFeatures(group);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            var found = new HashSet<string>();
            foreach (var f in settings.GetFeatures())
            {
                f.enabled = System.Array.IndexOf(features, f.GetType().Name) >= 0;
                if (f.enabled) found.Add(f.GetType().Name);
                EditorUtility.SetDirty(f);
            }
            foreach (var name in features)
                Debug.Log($"[ProjectConfigure] {group}: {name} {(found.Contains(name) ? "on" : "NOT FOUND")}");
            EditorUtility.SetDirty(settings);
        }

        // the avatar art (Runtime/AvatarLibrary): every av_*.mdl, the hair swing rigs and tuck maps, the suit patterns, the
        // countries' flags and the gauntlets, collected from Art/Open into Settings/AvatarLibrary.asset
        public static AvatarLibrary AvatarLibraryAsset()
        {
            const string path = Settings + "/AvatarLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<AvatarLibrary>(path);
            if (lib == null)
            {
                System.IO.Directory.CreateDirectory(Settings);
                lib = ScriptableObject.CreateInstance<AvatarLibrary>();
                AssetDatabase.CreateAsset(lib, path);
            }
            const string open = "Assets/ProjectArena/Art/Open/";
            List<T> Find<T>(string filter, string folder, System.Func<string, bool> keep) where T : Object
            {
                var o = new List<T>();
                foreach (var guid in AssetDatabase.FindAssets(filter, new[] { open + folder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (keep(System.IO.Path.GetFileName(p))) o.Add(AssetDatabase.LoadAssetAtPath<T>(p));
                }
                return o;
            }
            lib.parts = Find<GameObject>("av_", "Models", f => f.StartsWith("av_") && f.EndsWith(".mdl")).ToArray();
            lib.swings = Find<TextAsset>("av_", "Data", f => f.EndsWith(".swing.bytes")).ToArray();   // (hair, the BANDANA's tails)
            lib.tucks = Find<TextAsset>("av_", "Data", f => f.EndsWith(".tuck.bytes")).ToArray();
            lib.swishes = Find<TextAsset>("av_", "Data", f => f.EndsWith(".swish.bytes")).ToArray();   // (the skirts, the PELURU dress's)
            lib.patterns = Find<Texture2D>("open_pat_", "Textures", f => f.StartsWith("open_pat_")).ToArray();
            lib.flags = Find<Texture2D>("flag_", "Textures", f => f.StartsWith("flag_")).ToArray();
            GameObject M(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(Models + n + ".mdl");
            int styles = Avatar.kGauntletStyles;
            lib.gauntletHands = new GameObject[styles]; lib.gauntletCuffs = new GameObject[styles]; lib.gauntletFists = new GameObject[styles];
            lib.gauntletFingers = new GameObject[styles * 5]; lib.gauntletHinges = new TextAsset[styles];
            for (int k = 0; k < styles; ++k)
            {
                if (!Avatar.PlateGauntlet(k)) continue;
                lib.gauntletHands[k] = M($"gauntlet_{k}_hand");
                lib.gauntletCuffs[k] = M($"gauntlet_{k}_cuff");
                if (!Avatar.GrappleGauntlet(k)) continue;
                lib.gauntletFists[k] = M($"gauntlet_{k}_fist");
                for (int f = 0; f < 5; ++f) lib.gauntletFingers[k * 5 + f] = M($"gauntlet_{k}_f{f}");
                lib.gauntletHinges[k] = AssetDatabase.LoadAssetAtPath<TextAsset>(open + $"Data/gauntlet_{k}.hinge.bytes");
            }
            lib.grappleBeam = M("grapple_beam");
            lib.grappleFlare = M("grapple_flare");
            lib.lancer = M("lancer");
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(open + "Textures/" + n + ".png");
            lib.crowdFoam = T("fx_foam"); lib.crowdHeart = T("fx_heart"); lib.crowdStar = T("fx_star");
            lib.crowdHotdog = T("fx_hotdog"); lib.crowdSmile = T("fx_smile");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ProjectConfigure] avatar library: {lib.parts.Length} parts, {lib.swings.Length} swing rigs, {lib.tucks.Length} tuck maps, " +
                      $"{lib.patterns.Length} patterns, {lib.flags.Length} flags");
            return lib;
        }

        static void Scene()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var arena = new GameObject("Arena");
            foreach (var name in ArenaModels)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".mdl");
                if (model == null) { Debug.LogError($"[ProjectConfigure] model {name}.mdl did not import"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                go.transform.SetParent(arena.transform, false);
                // (the stadium round the court moves: widened and lowered round the EXPERIMENTAL CORNERS room, task C3)
                go.isStatic = name != "environment" && name != "env_fx" && name != "stadium_fx";
            }

            // the native look: shader globals (key light, end colours); no Unity lights, sky or fog
            arena.AddComponent<ArenaLook>();
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            // XR Origin: floor-level tracking; the head camera follows the headset (Input System pose driver)
            var origin = new GameObject("XR Origin").AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(origin.transform, false);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(offset.transform, false);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;   // the sky dome is about 600 m across
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.gameObject.AddComponent<AudioListener>();
            cam.gameObject.AddComponent<DebugPoseLog>();
            var pose = cam.gameObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(
                new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            pose.rotationInput = new InputActionProperty(
                new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            pose.trackingStateInput = new InputActionProperty(
                new InputAction("Tracking State", binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            // you against the practice bot with the C# rules (MatchView places the XR Origin at your end: recentering)
            var match = new GameObject("Match").AddComponent<MatchView>();
            match.arenaCollision = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ProjectArena/Art/Open/Data/arena.col.bytes");
            GameObject Model(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(Models + n + ".mdl");
            match.ballModel = Model("projectile");
            match.shellModel = Model("projectile_shell");
            match.spawnerModel = Model("spawner");
            match.headModel = Model("head");
            match.torsoModel = Model("torso");
            match.avatarLibrary = AvatarLibraryAsset();
            match.shieldModel = Model("shield");
            match.knuckleModel = Model("knuckleguard");
            match.strikezoneModel = Model("strikezone");
            match.fxShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Fx.shader");
            match.textShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectArena/Shaders/Text.shader");
            match.fontTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_font.png");
            match.emblemTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/open_emblem.png");
            match.roomPictures = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectArena/Art/Open/Textures/room_pictures.png");
            const string audio = "Assets/ProjectArena/Art/Open/Audio/";
            match.soundEvents = AssetDatabase.LoadAssetAtPath<TextAsset>(audio + "events.txt");
            var clips = new List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { audio.TrimEnd('/') }))
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            match.sounds = clips.ToArray();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
