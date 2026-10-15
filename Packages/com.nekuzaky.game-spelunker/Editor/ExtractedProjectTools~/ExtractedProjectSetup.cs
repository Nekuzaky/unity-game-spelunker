// Copied into extracted projects by Game Spelunker, next to MaterialRepair.cs. The first time the project opens, it
// repairs the materials AssetRipper could not keep. Run it again from Assets > Game Spelunker > Repair Materials.
// Delete the Assets/GameSpelunker folder to remove it. Kept within C# 6 for older Unity versions.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameSpelunker
{
    [InitializeOnLoad]
    internal static class ExtractedProjectSetup
    {
        private const string Marker = "ProjectSettings/GameSpelunker.txt";

        static ExtractedProjectSetup()
        {
            if (!File.Exists(Marker))
                EditorApplication.delayCall += RepairOnce;
        }

        private static void RepairOnce()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RepairOnce;
                return;
            }
            if (File.Exists(Marker))
                return;

            var report = MaterialRepair.Run("Assets", null);
            File.WriteAllText(Marker,
                "Game Spelunker repaired this project's materials the first time it was opened.\n" + report.Summary + "\n" +
                "Run it again from Assets > Game Spelunker > Repair Materials. Delete this file to repair on the next open.\n");
            Debug.Log("[Game Spelunker] " + report.Summary);
        }
    }
}
