using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameSpelunker
{
    [InitializeOnLoad]
    public static class ProjectCheck
    {
        private const string State = "Temp/GameSpelunker-check.txt";
        private static readonly double[] ShotTimes = { 20, 45 };
        private const double GiveUpAfter = 120;

        static ProjectCheck()
        {
            if (File.Exists(State))
                EditorApplication.update += Tick;
        }

        public static void RunBatch()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToList();
            string first = scenes.Count > 0 ? scenes[0] : MainScene();
            if (first == null)
            {
                Debug.Log("[Game Spelunker] Check: the project has no scene");
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(first, OpenSceneMode.Single);
            Debug.Log("[Game Spelunker] Check started: " + first);
            File.WriteAllText(State, EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture) + "\n0");
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        public static string MainScene()
        {
            return AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => !p.StartsWith("Assets/GameSpelunker/", StringComparison.Ordinal) && File.Exists(p))
                .OrderByDescending(p => new FileInfo(p).Length)
                .FirstOrDefault();
        }

        private static void Tick()
        {
            if (!File.Exists(State))
            {
                EditorApplication.update -= Tick;
                return;
            }
            var lines = File.ReadAllLines(State);
            double start = double.Parse(lines[0], CultureInfo.InvariantCulture);
            int shot = int.Parse(lines[1], CultureInfo.InvariantCulture);
            double t = EditorApplication.timeSinceStartup - start;
            if (shot < ShotTimes.Length && t >= ShotTimes[shot])
            {
                var loaded = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name).ToArray();
                var cameras = Camera.allCameras.Where(c => c.targetTexture == null && c.enabled).OrderBy(c => c.depth).ToList();
                Debug.Log("[Game Spelunker] Check: t=" + t.ToString("0", CultureInfo.InvariantCulture) + "s playing=" + EditorApplication.isPlaying +
                          " scenes=" + string.Join(", ", loaded) + " cameras=" + cameras.Count);
                if (cameras.Count > 0)
                    Capture(cameras, Path.Combine(OutputFolder(), "GameSpelunker-check-" + (shot + 1) + ".png"));
                shot++;
                File.WriteAllText(State, lines[0] + "\n" + shot);
            }
            if (shot >= ShotTimes.Length || t > GiveUpAfter || (t > 5 && !EditorApplication.isPlaying))
            {
                EditorApplication.update -= Tick;
                File.Delete(State);
                Debug.Log("[Game Spelunker] Check finished");
                EditorApplication.Exit(0);
            }
        }

        private static string OutputFolder()
        {
            string project = Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));
            string auxiliary = Path.Combine(Path.GetDirectoryName(project), "AuxiliaryFiles");
            return Directory.Exists(auxiliary) ? auxiliary : Path.Combine(project, "Temp");
        }

        private static void Capture(System.Collections.Generic.List<Camera> cameras, string path)
        {
            var rt = new RenderTexture(1280, 720, 24);
            foreach (var camera in cameras)
            {
                var previous = camera.targetTexture;
                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = previous;
            }
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            rt.Release();
        }
    }
}
