using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameSpelunker
{
    [InitializeOnLoad]
    public static class ExtractedProjectSetup
    {
        public static void RunBatch()
        {
            var report = Repair();
            Debug.Log("[Game Spelunker] Ready: " + report.Summary);
            EditorApplication.Exit(0);
        }

        private const string Marker = "ProjectSettings/GameSpelunker.txt";
        private const string GameCodeDefine = "SPELUNKER_GAME_CODE";

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

            var report = Repair();
            Debug.Log("[Game Spelunker] " + report.Summary);
            if (!GameCodeEnabled())
                Debug.Log("[Game Spelunker] The game's decompiled code is switched off: scenes, prefabs and assets work, but " +
                          "nothing runs in Play mode. Tools > Game Spelunker > Enable Game Code compiles it (it often needs fixes).");
        }

        private static MaterialRepairReport Repair()
        {
            var report = MaterialRepair.Run("Assets", null);
            File.WriteAllText(Marker,
                "Game Spelunker repaired this project's materials.\n" + report.Summary + "\n" +
                "Run it again from Assets > Game Spelunker > Repair Materials. Delete this file to repair on the next open.\n");
            return report;
        }

        [MenuItem("Tools/Game Spelunker/Enable Game Code", false, 100)]
        private static void EnableGameCode()
        {
            if (EditorUtility.DisplayDialog("Game Spelunker",
                    "Compile the game's decompiled scripts?\n\nDecompiled code often has errors to fix by hand before it compiles, " +
                    "and while it does not compile Unity runs none of the project's editor tools. Game code never runs in edit " +
                    "mode here, only in Play mode.\n\nTurn it off again from Tools > Game Spelunker > Disable Game Code.",
                    "Enable", "Cancel"))
                SetGameCode(true);
        }

        [MenuItem("Tools/Game Spelunker/Enable Game Code", true)]
        private static bool CanEnableGameCode() { return !GameCodeEnabled(); }

        [MenuItem("Tools/Game Spelunker/Disable Game Code", false, 101)]
        private static void DisableGameCode() { SetGameCode(false); }

        [MenuItem("Tools/Game Spelunker/Disable Game Code", true)]
        private static bool CanDisableGameCode() { return GameCodeEnabled(); }

        private static bool GameCodeEnabled()
        {
            return Defines().Split(';').Contains(GameCodeDefine);
        }

        private static void SetGameCode(bool on)
        {
            var defines = Defines().Split(';').Where(d => d.Length > 0 && d != GameCodeDefine).ToList();
            if (on) defines.Add(GameCodeDefine);
            string value = string.Join(";", defines.ToArray());
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(CurrentTarget(), value);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, value);
#endif
        }

        private static string Defines()
        {
#if UNITY_2021_2_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(CurrentTarget());
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
        }

#if UNITY_2021_2_OR_NEWER
        private static UnityEditor.Build.NamedBuildTarget CurrentTarget()
        {
            return UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        }
#endif
    }
}
