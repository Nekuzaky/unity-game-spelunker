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
            if (!UnityEditorInternal.InternalEditorUtility.inBatchMode && (!File.Exists(OpenedMarker) || File.Exists(OpenRequest)))
                EditorApplication.delayCall += OpenMainSceneOnce;
#if UNITY_2017_2_OR_NEWER
            if (!UnityEditorInternal.InternalEditorUtility.inBatchMode && HasGameCodeSwitch())
                EditorApplication.playModeStateChanged += change => { if (change == PlayModeStateChange.ExitingEditMode) WarnBeforePlay(); };
#endif
        }

        private static bool HasGameCodeSwitch()
        {
            return File.Exists("Assets/Scripts/Spelunked.GameCode.asmdef") || File.Exists("Assets/Plugins/Spelunked.GameCode.FirstPass.asmdef");
        }

        private static void WarnBeforePlay()
        {
            if (GameCodeEnabled())
                return;
            int choice = EditorUtility.DisplayDialogComplex("Game Spelunker",
                "The game's code is switched off in this project, so Play mode runs none of it: every game component reports " +
                "\"The referenced script (Unknown) on this Behaviour is missing!\" and nothing moves.\n\n" +
                "Enable the game code to try it (decompiled code often needs fixes before it compiles), or play anyway to look " +
                "at the scene at runtime.",
                "Cancel", "Play anyway", "Enable Game Code");
            if (choice == 1)
                return;
            EditorApplication.isPlaying = false;
            if (choice == 2)
                SetGameCode(true);
        }

        private static void RepairOnce()
        {
#if UNITY_2017_1_OR_NEWER
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
#else
            if (EditorApplication.isCompiling)
#endif
            {
                EditorApplication.delayCall += RepairOnce;
                return;
            }
            if (File.Exists(Marker))
                return;

            var report = Repair();
            Debug.Log("[Game Spelunker] " + report.Summary);
            if (HasGameCodeSwitch() && !GameCodeEnabled())
                Debug.Log("[Game Spelunker] The game's decompiled code is switched off: scenes, prefabs and assets work, but " +
                          "nothing runs in Play mode. Tools > Game Spelunker > Enable Game Code compiles it (it often needs fixes).");
        }

        private const string OpenedMarker = "Library/GameSpelunker-opened.txt";
        private const string OpenRequest = "Library/GameSpelunker-open.txt";

        private static void OpenMainSceneOnce()
        {
#if UNITY_2017_1_OR_NEWER
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Marker))
#else
            if (EditorApplication.isCompiling || !File.Exists(Marker))
#endif
            {
                EditorApplication.delayCall += OpenMainSceneOnce;
                return;
            }
            string requested = File.Exists(OpenRequest) ? File.ReadAllText(OpenRequest).Trim() : null;
            if (requested != null)
                File.Delete(OpenRequest);
            else if (File.Exists(OpenedMarker))
                return;
            File.WriteAllText(OpenedMarker, "The main scene was opened once by Game Spelunker.\n");
            string main = !string.IsNullOrEmpty(requested) && File.Exists(requested) ? requested
                : string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path) ? ProjectCheck.MainScene() : null;
            if (main == null)
                return;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(main, UnityEditor.SceneManagement.OpenSceneMode.Single);
            Debug.Log("[Game Spelunker] Opened the game's biggest scene: " + main);
            var terrain = Terrain.activeTerrain;
            var view = SceneView.lastActiveSceneView;
            if (terrain != null && terrain.terrainData != null && view != null)
            {
                var center = terrain.transform.position + terrain.terrainData.size * 0.5f;
                center.y = terrain.SampleHeight(center) + terrain.transform.position.y;
                view.LookAt(center, Quaternion.Euler(35f, 45f, 0f), 250f);
            }
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
        private static bool CanEnableGameCode() { return HasGameCodeSwitch() && !GameCodeEnabled(); }

        [MenuItem("Tools/Game Spelunker/Disable Game Code", false, 101)]
        private static void DisableGameCode() { SetGameCode(false); }

        [MenuItem("Tools/Game Spelunker/Disable Game Code", true)]
        private static bool CanDisableGameCode() { return HasGameCodeSwitch() && GameCodeEnabled(); }

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
