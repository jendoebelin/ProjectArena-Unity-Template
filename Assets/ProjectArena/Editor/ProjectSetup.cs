// One-time / repeatable project setup, run from the command line:
//   Unity.exe -batchmode -projectPath . -executeMethod ProjectArena.EditorTools.ProjectSetup.AddPackages -logFile -
// Adds the Phase 1 packages at the versions this editor recommends and removes template packages the game doesn't use.
using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ProjectArena.EditorTools
{
    public static class ProjectSetup
    {
        static readonly string[] Add =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.xr.management",
            "com.unity.xr.openxr",
            "com.unity.xr.meta-openxr",
            "com.unity.xr.interaction.toolkit",
            "com.unity.xr.hands",
            "com.unity.inputsystem",
            "com.unity.test-framework",
            "com.unity.ide.visualstudio",
        };

        static readonly string[] Remove =
        {
            "com.unity.collab-proxy",
            "com.unity.visualscripting",
            "com.unity.timeline",
            "com.unity.multiplayer.center",
            "com.unity.ide.rider",
        };

        static AddAndRemoveRequest request;

        public static void AddPackages()
        {
            var installed = Client.List(true, false);
            while (!installed.IsCompleted) System.Threading.Thread.Sleep(100);
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var p in installed.Result) names.Add(p.name);
            var remove = Array.FindAll(Remove, names.Contains);
            request = Client.AddAndRemove(Add, remove);
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!request.IsCompleted) return;
            EditorApplication.update -= Poll;
            if (request.Status == StatusCode.Success)
            {
                foreach (var p in request.Result)
                    Debug.Log($"[ProjectSetup] {p.name} {p.version}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[ProjectSetup] package request failed: {request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
