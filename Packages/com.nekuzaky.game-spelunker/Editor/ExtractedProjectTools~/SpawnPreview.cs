using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameSpelunker
{
    [InitializeOnLoad]
    public static class SpawnPreview
    {
        public const string RootName = "Game Spelunker: spawned by game code (preview)";
        private const string SpawnsFolder = "Assets/GameSpelunker/Editor/Spawns";
        private const string MenuPath = "Tools/Game Spelunker/Show Objects Spawned by Game Code";

        static SpawnPreview()
        {
            if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                return;
#if UNITY_2017_2_OR_NEWER
            EditorSceneManager.sceneOpened += (scene, mode) => Show(scene, false);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
#else
            EditorApplication.hierarchyWindowChanged += OnHierarchyChanged;
            EditorApplication.playmodeStateChanged += OnPlaymodeChanged;
#endif
            EditorApplication.delayCall += ShowAll;
        }

#if !UNITY_2017_2_OR_NEWER
        private static string _lastScenes = "";

        private static void OnHierarchyChanged()
        {
            string scenes = "";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                scenes += SceneManager.GetSceneAt(i).path + ";";
            if (scenes == _lastScenes)
                return;
            _lastScenes = scenes;
            ShowAll();
        }

        private static void OnPlaymodeChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) HideAll();
            else if (!EditorApplication.isPlayingOrWillChangePlaymode) ShowAll();
        }
#endif

        private static string PrefKey
        {
            get { return "GameSpelunker.ShowSpawned." + Application.dataPath; }
        }

        private static bool Enabled
        {
            get { return EditorPrefs.GetBool(PrefKey, true); }
            set { EditorPrefs.SetBool(PrefKey, value); }
        }

        [MenuItem(MenuPath, false, 120)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled) ShowAll();
            else HideAll();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

#if UNITY_2017_2_OR_NEWER
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) HideAll();
            else if (change == PlayModeStateChange.EnteredEditMode) ShowAll();
        }
#endif

        private static void ShowAll()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Show(SceneManager.GetSceneAt(i), false);
        }

        private static void HideAll()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Hide(SceneManager.GetSceneAt(i));
        }

        public static int Show(Scene scene, bool force)
        {
            if (!force && (!Enabled || EditorApplication.isPlayingOrWillChangePlaymode))
                return 0;
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                return 0;
            Hide(scene);
            string list = Path.Combine(SpawnsFolder, AssetDatabase.AssetPathToGUID(scene.path) + ".txt");
            if (!File.Exists(list))
                return 0;

            var root = new GameObject(RootName);
            root.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(root, scene);
            var prefabs = new Dictionary<string, GameObject>();
            string[] lines = File.ReadAllLines(list);
            int shown = 0;
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i % 250 == 0 && EditorUtility.DisplayCancelableProgressBar("Game Spelunker", "Placing objects the game spawns with its code…", i / (float)lines.Length))
                        break;
                    string[] v = lines[i].Split(' ');
                    if (v.Length < 11)
                        continue;
                    GameObject prefab;
                    if (!prefabs.TryGetValue(v[0], out prefab))
                    {
                        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(v[0]));
                        prefabs[v[0]] = prefab;
                    }
                    if (prefab == null)
                        continue;
                    var position = new Vector3(F(v[1]), F(v[2]), F(v[3]));
                    var rotation = new Quaternion(F(v[4]), F(v[5]), F(v[6]), F(v[7]));
                    var instance = (GameObject)Object.Instantiate(prefab, position, rotation, root.transform);
                    foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                        t.gameObject.hideFlags = HideFlags.DontSave;
                    shown++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return shown;
        }

        private static void Hide(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName)
                    Object.DestroyImmediate(go);
        }

        private static float F(string value)
        {
            float result;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0f;
        }
    }
}
