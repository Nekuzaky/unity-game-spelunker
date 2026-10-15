using System;
using System.IO;
using TestGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the sample game used to test Game Spelunker:
/// <c>Unity -batchmode -projectPath TestGame -executeMethod BuildTestGame.Build</c> → TestGame/Build/TestGame.exe (Mono).
/// </summary>
public static class BuildTestGame
{
    private const string ScenePath = "Assets/Scenes/Level.unity";

    public static void Build()
    {
        try
        {
            CreateScene();
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.productName = "Spelunker Test Game";
            PlayerSettings.companyName = "GameSpelunker";
            string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Build", "TestGame.exe"));
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, exe, BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log($"[TestGame] Build {report.summary.result}: {exe}");
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var red = SaveMaterial("Red", new Color(0.85f, 0.2f, 0.2f));
        var gold = SaveMaterial("Gold", new Color(1f, 0.8f, 0.2f));

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(3, 1, 3);

        var keeper = new GameObject("Score Keeper").AddComponent<ScoreKeeper>();
        var so = new SerializedObject(keeper);
        var list = so.FindProperty("spinners");

        for (int i = 0; i < 4; i++)
        {
            var cube = GameObject.CreatePrimitive(i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            cube.name = $"Spinner {i + 1}";
            cube.transform.position = new Vector3(-3 + i * 2, 1, 0);
            cube.GetComponent<MeshRenderer>().sharedMaterial = i % 2 == 0 ? red : gold;
            var spinner = cube.AddComponent<Spinner>();
            var sso = new SerializedObject(spinner);
            sso.FindProperty("degreesPerSecond").floatValue = 45 + i * 30;
            sso.FindProperty("label").stringValue = $"Spinner number {i + 1}";
            sso.ApplyModifiedPropertiesWithoutUndo();

            list.InsertArrayElementAtIndex(i);
            list.GetArrayElementAtIndex(i).objectReferenceValue = spinner;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        Camera.main.transform.position = new Vector3(0, 3, -7);
        Camera.main.transform.LookAt(new Vector3(0, 1, 0));

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    private static Material SaveMaterial(string name, Color color)
    {
        Directory.CreateDirectory("Assets/Game/Materials");
        string path = $"Assets/Game/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
