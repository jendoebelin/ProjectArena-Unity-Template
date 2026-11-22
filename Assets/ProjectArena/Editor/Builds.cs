// Command-line builds:
//   Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.Builds.Quest -logFile -
//   Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.Builds.Pc -logFile -
//   Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.Builds.SimCompare -logFile -
// Quest: Builds/Quest/ProjectArenaUnity.apk (development build, debug-signed). PC: Builds/PC/ProjectArenaUnity.exe.
// SimCompare: Build/SimCompare/SimCompare.exe, the same-results check's IL2CPP player (tools/sim_compare/compare.py).
using ProjectArena.SimCompare;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectArena.EditorTools
{
    public static class Builds
    {
        public static void Quest() => Build(BuildTarget.Android, "Builds/Quest/ProjectArenaUnity.apk");

        public static void Pc() => Build(BuildTarget.StandaloneWindows64, "Builds/PC/ProjectArenaUnity.exe");

        // The simulation on IL2CPP (the compiler the game ships with; on Windows it compiles with the same Visual Studio
        // compiler as the native trace tool), headless: plays the scenarios, writes the traces, quits.
        public static void SimCompare()
        {
            const string scene = "Assets/ProjectArena/SimCompare/SimCompare.unity";
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var main = new GameObject("SimCompare").AddComponent<SimCompareMain>();
            main.arenaCollision = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ProjectArena/Art/Open/Data/arena.col.bytes");
            EditorSceneManager.SaveScene(s, scene);
            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            try { Build(BuildTarget.StandaloneWindows64, "Build/SimCompare/SimCompare.exe", new[] { scene }, BuildOptions.None); }
            finally { PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend); }
        }

        static void Build(BuildTarget target, string path) =>
            Build(target, path, System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path), BuildOptions.Development);

        // The community server of this build (as natively: servers.local.cfg, PROJECTARENA_SERVER=host:port, not in git;
        // the environment variable of that name wins; without either, the native repo's file is read). Written to
        // Assets/ProjectArena/Resources/servers_default.txt (not in git either); empty = players type one. Never logged.
        // the native repository (its server, server/projectarena_relay.py): the environment variable PA_NATIVE_REPO, or a
        // line PA_NATIVE_REPO=<folder> in native_repo.local.cfg (not in git), or the public repository cloned beside this
        // one (../ProjectArena-public or ../ProjectArena); "" when none is found
        public static string FindNativeRepo()
        {
            string v = System.Environment.GetEnvironmentVariable("PA_NATIVE_REPO");
            if (!string.IsNullOrEmpty(v)) return v;
            try
            {
                if (System.IO.File.Exists("native_repo.local.cfg"))
                    foreach (string line in System.IO.File.ReadAllLines("native_repo.local.cfg"))
                        if (line.Trim().StartsWith("PA_NATIVE_REPO=")) return line.Trim().Substring("PA_NATIVE_REPO=".Length).Trim();
            }
            catch (System.Exception) { }
            foreach (string d in new[] { "../ProjectArena-public", "../ProjectArena" })
                if (System.IO.File.Exists(System.IO.Path.Combine(d, "server", "projectarena_relay.py"))) return System.IO.Path.GetFullPath(d);
            return "";
        }

        static void WriteServersDefault()
        {
            string Read(string file)
            {
                try
                {
                    if (!System.IO.File.Exists(file)) return null;
                    foreach (string line in System.IO.File.ReadAllLines(file))
                    {
                        string l = line.Trim();
                        if (l.StartsWith("PROJECTARENA_SERVER=")) return l.Substring("PROJECTARENA_SERVER=".Length).Trim();
                    }
                }
                catch (System.Exception) { }
                return null;
            }
            string v = System.Environment.GetEnvironmentVariable("PROJECTARENA_SERVER");
            string from = "the environment";
            if (string.IsNullOrEmpty(v)) { v = Read("servers.local.cfg"); from = "servers.local.cfg"; }
            if (string.IsNullOrEmpty(v))
            {
                string native = FindNativeRepo();
                v = Read(System.IO.Path.Combine(native, "servers.local.cfg"));
                from = "the native repo's servers.local.cfg";
            }
            var clean = new System.Text.StringBuilder();
            foreach (char c in v ?? "") if (char.IsLetterOrDigit(c) || ".-:_[]".IndexOf(c) >= 0) clean.Append(c);
            const string dir = "Assets/ProjectArena/Resources";
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(dir + "/servers_default.txt", clean.ToString());
            AssetDatabase.ImportAsset(dir + "/servers_default.txt");
            Debug.Log($"[Builds] community server: {(clean.Length > 0 ? "set (from " + from + ")" : "none (players type one)")}");
        }

        static void Build(BuildTarget target, string path, string[] scenes, BuildOptions buildOptions)
        {
            if (!path.Contains("SimCompare")) WriteServersDefault();
            PlayerSettings.bundleVersion = Online.Net.GameVersion;   // (the native version it plays as: HELLO, JOIN, the menus)
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                options = buildOptions,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            var size = System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length / (1024 * 1024) : 0;
            Debug.Log($"[Builds] {target}: {s.result}, {s.totalErrors} errors, {s.totalTime.TotalSeconds:F0} s -> " +
                      $"{path} ({size} MB)");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
