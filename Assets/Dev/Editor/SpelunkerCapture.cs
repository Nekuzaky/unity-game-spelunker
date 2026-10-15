using System;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GameSpelunker
{
    /// <summary>
    /// Screenshots of the window for the README. Needs a screen (not batch mode):
    /// <c>Unity -projectPath &lt;repository root&gt; -executeMethod GameSpelunker.SpelunkerCapture.Run
    /// -gsGame &lt;game&gt; -gsExport &lt;existing extraction&gt; -gsShots &lt;folder&gt;</c>. Unity quits when done.
    /// </summary>
    public static class SpelunkerCapture
    {
        private const string ConsentKey = "GameSpelunker.Consent";
        private static SpelunkerWindow? _window;
        private static double _next;
        private static int _step;
        private static bool _consentBefore;

        public static void Run()
        {
            _consentBefore = EditorPrefs.GetBool(ConsentKey, false);
            EditorPrefs.SetBool(ConsentKey, true);

            _window = SpelunkerWindow.Open();
            _window.position = new Rect(80, 40, 600, 720);
            _window.ShowForCapture(Arg("-gsGame") ?? "", Arg("-gsExport") ?? "");
            _window.Focus();
            _window.rootVisualElement.schedule.Execute(() => _window.ScrollTo(end: false)).ExecuteLater(1500);
            _step = 0;
            _next = EditorApplication.timeSinceStartup + 4;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _next || _window == null)
                return;
            string shots = Arg("-gsShots") ?? Path.GetFullPath("../docs/images");
            Directory.CreateDirectory(shots);
            try
            {
                switch (_step++)
                {
                    case 0:
                        Save(_window.position, Path.Combine(shots, "window-top.png"));
                        _window.ScrollTo(end: true);
                        _window.Repaint();
                        _next = EditorApplication.timeSinceStartup + 2;
                        return;
                    case 1:
                        Save(_window.position, Path.Combine(shots, "window-explore.png"));
                        break;
                }
                Debug.Log("[Game Spelunker] Captures saved to " + shots);
                Finish(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Finish(1);
            }
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            EditorPrefs.SetBool(ConsentKey, _consentBefore);
            EditorApplication.Exit(code);
        }

        private static void Save(Rect screenRect, string path)
        {
            float scale = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.RoundToInt(screenRect.width * scale);
            int height = Mathf.RoundToInt(screenRect.height * scale);
            var pixels = InternalEditorUtility.ReadScreenPixel(new Vector2(screenRect.x * scale, screenRect.y * scale), width, height);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static string? Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
