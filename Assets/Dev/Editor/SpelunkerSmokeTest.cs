using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace GameSpelunker
{
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

                string resolved = System.Threading.Tasks.Task.Run(() => AssetRipperInstaller.EnsureInstalledAsync(null, null, CancellationToken.None))
                    .GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] AssetRipper resolved off the main thread: {resolved}");

                var report = BuildInspector.Inspect(game);
                Debug.Log($"[Game Spelunker] {report.Name} by {report.Company}: Unity {report.UnityVersion}, {report.Backend}, {report.RenderPipeline}, " +
                          $"{report.SceneCount} scene(s), {BuildInspector.FormatSize(report.TotalSize)}, assemblies: {string.Join(", ", report.GameAssemblies)}, " +
                          $"plugins: {string.Join(", ", report.NativePlugins)}, mod loaders: {string.Join(", ", report.ModLoaders)}");

                var found = GameFinder.FindInstalledUnityGames();
                Debug.Log($"[Game Spelunker] Unity games found on this PC: {found.Count}, " +
                          $"with an icon: {found.Count(g => GameFinder.FindIcon(g.Folder) != null)}");

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
                    session.LoadAsync(report.LoadPath, CancellationToken.None).Wait();
                    session.ExportAsync(output, ExportMode.UnityProject, CancellationToken.None).Wait();
                }

                var scripts = ExtractedProject.FindScripts(output);
                Debug.Log($"[Game Spelunker] Exported in {(DateTime.UtcNow - started).TotalSeconds:0.0}s, {scripts.Count} game script(s): " +
                          string.Join(", ", scripts.Select(s => s.RelativePath)));
                var project = ExtractedProject.ProjectFolder(output);
                Debug.Log($"[Game Spelunker] Project folder: {project}, version {ExtractedProject.UnityVersionOf(output)}");

                var prepared = ExtractedProject.PrepareForOpening(output, ExtractedProject.ToolsSource(), report.DataFolder);
                var again = ExtractedProject.PrepareForOpening(output, ExtractedProject.ToolsSource());
                string ready = ExtractedProject.ImportAndRepairAsync(output, ExtractedProject.EditorFor(output, EditorApplication.applicationPath), null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] Import and repair in Unity: {ready}");
                var code = GameCodeCompiler.MakeItCompileAsync(output, ExtractedProject.EditorFor(output, EditorApplication.applicationPath), null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] Game code compiles: {code.Compiles} after {code.Rounds} pass(es)");
                bool toolsCopied = File.Exists(Path.Combine(project, "Assets", "GameSpelunker", "Editor", "MaterialRepair.cs"))
                                   && File.Exists(Path.Combine(project, "Assets", "GameSpelunker", "Editor", "ExtractedProjectSetup.cs"));
                toolsCopied &= prepared.GameCodeOff && File.Exists(Path.Combine(project, "Assets", "Scripts", "Spelunked.GameCode.asmdef"));
                toolsCopied &= code.Compiles;
                Debug.Log($"[Game Spelunker] Prepared: {prepared.ScriptsStopped} script(s) stopped in edit mode, {prepared.GeneratedRemoved} generated removed, " +
                          $"game code off: {prepared.GameCodeOff}, {prepared.AssemblyInfoRemoved} AssemblyInfo removed, {prepared.LibrariesAdded} libraries added, " +
                          $"tools {(toolsCopied ? "installed" : "MISSING")}, second pass changed {again.ScriptsStopped}");

                string target = ProjectImporter.DefaultTarget("Smoke Test");
                var import = ProjectImporter.Import(output, report.Name, new ImportOptions { TargetFolder = target, IncludeScripts = true });
                string asmdef = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, target, "Scripts"), "*.asmdef").FirstOrDefault() ?? "none";
                Debug.Log($"[Game Spelunker] Imported {import.FilesCopied} file(s) into {import.TargetFolder}, {import.GuidConflicts} GUID conflict(s), " +
                          $"skipped [{string.Join(", ", import.SkippedFolders)}], scripts assembly {Path.GetFileName(asmdef)} behind {import.ScriptsDefine}, " +
                          $"missing packages [{string.Join(", ", import.MissingPackages)}]");
                Debug.Log($"[Game Spelunker] Import materials: {import.Materials?.Summary ?? "not repaired"}");
                bool imported = import.FilesCopied > 0 && asmdef != "none" && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(target) != null
                                && !AssetDatabase.IsValidFolder(target + "/" + ExtractedProject.ToolsFolder)
                                && Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, target, "Scripts"), "*.asmdef").Length == 1;
                AssetDatabase.DeleteAsset("Assets/Spelunked");

                bool ok = scripts.Count > 0 && imported && toolsCopied && again.ScriptsStopped == 0;
                Debug.Log(ok ? "[Game Spelunker] Smoke test passed." : "[Game Spelunker] Smoke test FAILED: no scripts exported.");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex is AggregateException ae && ae.InnerException != null ? ae.InnerException : ex);
                EditorApplication.Exit(1);
            }
        }

        public static void Extract()
        {
            try
            {
                string game = Arg("-gsGame") ?? throw new ArgumentException("-gsGame is required");
                string output = Arg("-gsOut") ?? throw new ArgumentException("-gsOut is required");
                string exe = AssetRipperInstaller.EnsureInstalledAsync(AssetRipperInstaller.Executable, null, CancellationToken.None).GetAwaiter().GetResult();
                var report = BuildInspector.Inspect(game);
                var started = DateTime.UtcNow;
                using (var session = new AssetRipperSession(exe))
                {
                    int lastPercent = -10;
                    session.Progress += (phase, done, total, _) =>
                    {
                        int percent = total == 0 ? 0 : done * 100 / total;
                        if (percent >= lastPercent + 10) { lastPercent = percent; Debug.Log($"[Game Spelunker] {phase} {percent}%"); }
                    };
                    session.StartAsync(CancellationToken.None).Wait();
                    session.LoadAsync(report.LoadPath, CancellationToken.None).Wait();
                    session.ExportAsync(output, ExportMode.UnityProject, CancellationToken.None).Wait();
                }
                var prepared = ExtractedProject.PrepareForOpening(output, ExtractedProject.ToolsSource(), report.DataFolder);
                Debug.Log($"[Game Spelunker] Prepared: {prepared.NativeLibrariesAdded} native libraries, {prepared.ScriptsFixed} scripts fixed, {prepared.LibrariesSwitchedOff} libraries behind the define, {prepared.UnreadableRemoved} unreadable assets removed, " +
                          $"{prepared.ObsoleteComponentsRemoved} obsolete components removed");
                string editor = ExtractedProject.EditorFor(output, EditorApplication.applicationPath);
                Debug.Log($"[Game Spelunker] Target editor: {editor}; same version as the game: {prepared.SameVersionAsGame}, APIs migrated: {prepared.MigratesApis}");
                var compileStarted = DateTime.UtcNow;
                var code = GameCodeCompiler.MakeItCompileAsync(output, editor, new Progress<string>(m => Debug.Log("[Game Spelunker] " + m)), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] Game code compiles: {code.Compiles} in {(DateTime.UtcNow - compileStarted).TotalMinutes:0.0} min, {code.Rounds} pass(es), " +
                          $"{code.MembersStubbed} stubbed, {code.FilesQuarantined} quarantined, {code.RemainingErrors} error(s) left");
                var importStarted = DateTime.UtcNow;
                string ready = ExtractedProject.ImportAndRepairAsync(output, editor, null, CancellationToken.None).GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] Imported and repaired in {(DateTime.UtcNow - importStarted).TotalMinutes:0.0} min: {ready}");
                Debug.Log($"[Game Spelunker] Extracted {report.Name} (Unity {report.UnityVersion}) in {(DateTime.UtcNow - started).TotalMinutes:0.0} min; " +
                          $"{prepared.ScriptsStopped} script(s) stopped in edit mode, {prepared.GeneratedRemoved} generated removed");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex is AggregateException ae && ae.InnerException != null ? ae.InnerException : ex);
                EditorApplication.Exit(1);
            }
        }

        public static void Finish()
        {
            try
            {
                string export = Arg("-gsExport") ?? throw new ArgumentException("-gsExport is required");
                var prepared = ExtractedProject.PrepareForOpening(export, ExtractedProject.ToolsSource());
                string editor = ExtractedProject.EditorFor(export, EditorApplication.applicationPath);
                Debug.Log($"[Game Spelunker] Prepared again; target editor: {editor}; same version as the game: {prepared.SameVersionAsGame}");
                var code = GameCodeCompiler.MakeItCompileAsync(export, editor, new Progress<string>(m => Debug.Log("[Game Spelunker] " + m)), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] Game code compiles: {code.Compiles}, {code.Rounds} pass(es), {code.MembersStubbed} stubbed, " +
                          $"{code.FilesQuarantined} quarantined, {code.RemainingErrors} error(s) left");
                string ready = ExtractedProject.ImportAndRepairAsync(export, editor, null, CancellationToken.None).GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] Imported and repaired: {ready}");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex is AggregateException ae && ae.InnerException != null ? ae.InnerException : ex);
                EditorApplication.Exit(1);
            }
        }

        public static void Check()
        {
            try
            {
                string export = Arg("-gsExport") ?? throw new ArgumentException("-gsExport is required");
                string editor = ExtractedProject.EditorFor(export, EditorApplication.applicationPath);
                var result = ProjectChecker.CheckAsync(export, editor, ExtractedProject.ToolsSource(), CancellationToken.None).GetAwaiter().GetResult();
                Debug.Log("[Game Spelunker] " + ProjectChecker.Describe(result).Replace("\n", "\n[Game Spelunker] "));
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex is AggregateException ae && ae.InnerException != null ? ae.InnerException : ex);
                EditorApplication.Exit(1);
            }
        }

        public static void CompileGameCode()
        {
            try
            {
                string export = Arg("-gsExport") ?? throw new ArgumentException("-gsExport is required");
                var result = GameCodeCompiler.MakeItCompileAsync(export, EditorApplication.applicationPath,
                    new Progress<string>(m => Debug.Log("[Game Spelunker] " + m)), CancellationToken.None).GetAwaiter().GetResult();
                Debug.Log($"[Game Spelunker] Game code compiles: {result.Compiles} after {result.Rounds} pass(es), {result.MembersStubbed} member(s) stubbed, " +
                          $"{result.FilesQuarantined} file(s) quarantined, {result.RemainingErrors} error(s) in the last pass");
                EditorApplication.Exit(result.Compiles ? 0 : 2);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        public static void FixCode()
        {
            try
            {
                string project = Arg("-gsProject") ?? throw new ArgumentException("-gsProject is required");
                int scripts = ExtractedProject.FixDecompiledCode(Path.Combine(project, "Assets", "Scripts"), true);
                int plugins = ExtractedProject.FixDecompiledCode(Path.Combine(project, "Assets", "Plugins"), true);
                Debug.Log($"[Game Spelunker] Decompiled code fixed: {scripts} script(s), {plugins} plugin script(s)");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        public static void PrepareExport()
        {
            try
            {
                string export = Arg("-gsExport") ?? throw new ArgumentException("-gsExport is required");
                string? game = Arg("-gsGame");
                var prepared = ExtractedProject.PrepareForOpening(export, ExtractedProject.ToolsSource(),
                    game != null ? BuildInspector.Inspect(game).DataFolder : null);
                Debug.Log($"[Game Spelunker] Prepared {export}: {prepared.ScriptsStopped} script(s) stopped in edit mode, " +
                          $"{prepared.GeneratedRemoved} generated removed, {prepared.AssemblyInfoRemoved} AssemblyInfo removed, " +
                          $"{prepared.LibrariesAdded} libraries added, game code off: {prepared.GameCodeOff}, tools installed: {prepared.ToolsInstalled}, " +
                          $"objects spawned by code: {prepared.SpawnedObjects}, scripts fixed: {prepared.ScriptsFixed}, native libraries: {prepared.NativeLibrariesAdded}, " +
                          $"cursor textures: {prepared.CursorTextures}");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

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
