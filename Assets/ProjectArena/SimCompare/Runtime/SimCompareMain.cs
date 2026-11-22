using System.IO;
using ProjectArena.Sim;
using UnityEngine;

namespace ProjectArena.SimCompare
{
    // The IL2CPP compare player (scene SimCompare/SimCompare.unity, built by Builds.SimCompare): plays every scenario and
    // writes the traces, then quits. tools/sim_compare/compare.py runs it and checks the traces against the native ones.
    //   SimCompare.exe -batchmode -nographics -out <folder>
    public class SimCompareMain : MonoBehaviour
    {
        public TextAsset arenaCollision;

        void Start()
        {
            string dir = "traces";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == "-out") dir = args[i + 1];
            Directory.CreateDirectory(dir);
            var arena = ArenaCollision.Load(arenaCollision.bytes);
            foreach (var name in Scenarios.Names)
            {
                var lines = Scenarios.Play(name, arena);
                File.WriteAllLines(Path.Combine(dir, name + ".trace"), lines);
                Debug.Log($"[SimCompare] {name}: {lines.Count} lines");
            }
            Application.Quit(0);
        }
    }
}
