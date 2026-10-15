using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace GameSpelunker
{
    /// <summary>
    /// Batch-mode end-to-end check: inspects a build, extracts it with AssetRipper and checks that scripts came back.
    /// <c>Unity -batchmode -projectPath &lt;repository root&gt; -executeMethod GameSpelunker.SpelunkerSmokeTest.Run
    /// -gsGame &lt;game folder&gt; -gsOut &lt;output folder&gt; [-gsAssetRipper &lt;AssetRipper.GUI.Free.exe&gt;]</c>
    /// </summary>
    public static class SpelunkerSmokeTest
    {
        public static void Run()
        {
            try
            {
                string game = Arg("-gsGame") ?? throw new ArgumentException("-gsGame is required");
                string output = Arg("-gsOut") ?? throw new ArgumentException("-gsOut is required");
                string? exe = Arg("-gsAssetRipper") ?? AssetRipperInstaller.Executable;
                if (exe == null)
                    throw new InvalidOperationException("AssetRipper is not installed; pass -gsAssetRipper.");

                // The window resolves AssetRipper on a worker thread: that path must not touch main-thread-only APIs.
                string resolved = System.Threading.Tasks.Task.Run(() => AssetRipperInstaller.EnsureInstalledAsync(null, null, CancellationToken.None))
                    .GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] AssetRipper resolved off the main thread: {resolved}");

                var report = BuildInspector.Inspect(game);
                Debug.Log($"[Game Spelunker] {report.Name} by {report.Company}: Unity {report.UnityVersion}, {report.Backend}, {report.RenderPipeline}, " +
                          $"{report.SceneCount} scene(s), {BuildInspector.FormatSize(report.TotalSize)}, assemblies: {string.Join(", ", report.GameAssemblies)}, " +
                          $"plugins: {string.Join(", ", report.NativePlugins)}, mod loaders: {string.Join(", ", report.ModLoaders)}");

                Debug.Log($"[Game Spelunker] Unity games found on this PC: {GameFinder.FindInstalledUnityGames().Count}");

                if (Directory.Exists(output))
                    Directory.Delete(output, true);

                var started = DateTime.UtcNow;
                using (var session = new AssetRipperSession(exe))
                {
                    int lastPercent = -10;
                    session.Progress += (phase, done, total, _) =>
                    {
                        int percent = total == 0 ? 0 : done * 100 / total;
                        if (percent >= lastPercent + 25) { lastPercent = percent; Debug.Log($"[Game Spelunker] {phase} {percent}%"); }
                    };
                    session.StartAsync(CancellationToken.None).Wait();
                    session.LoadAsync(report.DataFolder, CancellationToken.None).Wait();
                    session.ExportAsync(output, ExportMode.UnityProject, CancellationToken.None).Wait();
                }

                var scripts = ExtractedProject.FindScripts(output);
                Debug.Log($"[Game Spelunker] Exported in {(DateTime.UtcNow - started).TotalSeconds:0.0}s, {scripts.Count} game script(s): " +
                          string.Join(", ", scripts.Select(s => s.RelativePath)));
                var project = ExtractedProject.ProjectFolder(output);
                Debug.Log($"[Game Spelunker] Project folder: {project}, version {ExtractedProject.UnityVersionOf(output)}");

                // Import into this project, scripts included (isolated behind a define), then clean up.
                string target = ProjectImporter.DefaultTarget("Smoke Test");
                var import = ProjectImporter.Import(output, report.Name, new ImportOptions { TargetFolder = target, IncludeScripts = true });
                string asmdef = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, target, "Scripts"), "*.asmdef").FirstOrDefault() ?? "none";
                Debug.Log($"[Game Spelunker] Imported {import.FilesCopied} file(s) into {import.TargetFolder}, {import.GuidConflicts} GUID conflict(s), " +
                          $"skipped [{string.Join(", ", import.SkippedFolders)}], scripts assembly {Path.GetFileName(asmdef)} behind {import.ScriptsDefine}, " +
                          $"missing packages [{string.Join(", ", import.MissingPackages)}]");
                bool imported = import.FilesCopied > 0 && asmdef != "none" && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(target) != null;
                AssetDatabase.DeleteAsset("Assets/Spelunked");

                bool ok = scripts.Count > 0 && imported;
                Debug.Log(ok ? "[Game Spelunker] Smoke test passed." : "[Game Spelunker] Smoke test FAILED: no scripts exported.");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex is AggregateException ae && ae.InnerException != null ? ae.InnerException : ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Downloads and unpacks AssetRipper into the managed install folder, then exits.</summary>
        public static void Install()
        {
            try
            {
                var exe = AssetRipperInstaller.EnsureInstalledAsync(null, null, CancellationToken.None).GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] AssetRipper installed: {exe} ({new FileInfo(exe).Length / (1024 * 1024)} MB)");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static string? Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
