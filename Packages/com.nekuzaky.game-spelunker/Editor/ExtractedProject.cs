using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace GameSpelunker
{
    public sealed class ExtractedScript
    {
        public string Path = "";
        public string RelativePath = "";
        public string Assembly = "";
        public string ClassName = "";
    }

    public sealed class PrepareResult
    {
        public int ScriptsStopped;
        public int GeneratedRemoved;
        public int AssemblyInfoRemoved;
        public int LibrariesAdded;
        public bool GameCodeOff;
        public int LibrariesSwitchedOff;
        public int UnreadableRemoved;
        public int ObsoleteComponentsRemoved;
        public int SpawnedObjects;
        public bool ToolsInstalled;
    }

    public static class ExtractedProject
    {
        private static readonly string[] EngineAssemblies = { "Unity.", "UnityEngine", "Cinemachine", "TextMeshPro", "DOTween", "Mono.", "System" };

        public static string ProjectFolder(string exportFolder)
        {
            string project = Path.Combine(exportFolder, "ExportedProject");
            return Directory.Exists(project) ? project : exportFolder;
        }

        public static string? UnityVersionOf(string exportFolder)
        {
            string file = Path.Combine(ProjectFolder(exportFolder), "ProjectSettings", "ProjectVersion.txt");
            if (!File.Exists(file)) return null;
            var m = Regex.Match(File.ReadAllText(file), @"m_EditorVersion:\s*(\S+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static List<ExtractedScript> FindScripts(string exportFolder)
        {
            string scriptsRoot = Path.Combine(ProjectFolder(exportFolder), "Assets", "Scripts");
            var result = new List<ExtractedScript>();
            if (!Directory.Exists(scriptsRoot))
                return result;

            foreach (var assemblyDir in Directory.GetDirectories(scriptsRoot))
            {
                string assembly = Path.GetFileName(assemblyDir);
                if (EngineAssemblies.Any(p => assembly.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;
                foreach (var file in Directory.GetFiles(assemblyDir, "*.cs", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file) == "AssemblyInfo.cs")
                        continue;
                    result.Add(new ExtractedScript
                    {
                        Path = file,
                        RelativePath = file.Substring(scriptsRoot.Length + 1).Replace('\\', '/'),
                        Assembly = assembly,
                        ClassName = Path.GetFileNameWithoutExtension(file),
                    });
                }
            }
            return result.OrderBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string? FindEditor(string? version)
        {
            if (version == null) return null;
            foreach (var root in new[] { @"C:\Program Files\Unity\Hub\Editor", @"C:\Program Files\Unity\Editor" })
            {
                string exe = Path.Combine(root, version, "Editor", "Unity.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }

        private static readonly Regex EditModeAttribute = new Regex(
            @"\[\s*(?:UnityEngine\.)?(?:ExecuteInEditMode|ExecuteAlways)(?:Attribute)?\s*(?:\(\s*\))?\s*\]", RegexOptions.Compiled);
        private static readonly Regex EditModeAttributeInList = new Regex(
            @"(?:UnityEngine\.)?(?:ExecuteInEditMode|ExecuteAlways)(?:Attribute)?\s*(?:\(\s*\))?\s*,\s*|,\s*(?:UnityEngine\.)?(?:ExecuteInEditMode|ExecuteAlways)(?:Attribute)?\s*(?:\(\s*\))?(?=\s*\])",
            RegexOptions.Compiled);
        private const string EditModeNote = "/* ExecuteInEditMode removed by Game Spelunker: this code only runs in Play mode */";

        private static readonly string[] GeneratedScripts = { "__JobReflectionRegistrationOutput__", "__UnmanagedPostProcessorOutput__", "__BurstDirectCallInitializer" };

        public const string ToolsFolder = "GameSpelunker";

        public static string ToolsSource()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(SpelunkerWindow.PackageRoot);
            string root = info != null ? info.resolvedPath : Path.GetFullPath(SpelunkerWindow.PackageRoot);
            return Path.Combine(root, "Editor");
        }

        public static PrepareResult PrepareForOpening(string exportFolder, string toolsSource)
        {
            var result = new PrepareResult();
            string assets = Path.Combine(ProjectFolder(exportFolder), "Assets");
            if (!Directory.Exists(assets))
                return result;

            string scripts = Path.Combine(assets, "Scripts");
            string plugins = Path.Combine(assets, "Plugins");

            foreach (var root in new[] { scripts, plugins }.Where(Directory.Exists))
                foreach (var info in Directory.GetFiles(root, "AssemblyInfo.cs", SearchOption.AllDirectories)
                             .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "Properties"))
                {
                    File.Delete(info);
                    if (File.Exists(info + ".meta")) File.Delete(info + ".meta");
                    result.AssemblyInfoRemoved++;
                }

            string gameAssemblies = Path.Combine(exportFolder, "AuxiliaryFiles", "GameAssemblies");
            if (Directory.Exists(gameAssemblies))
            {
                var present = new HashSet<string>(Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileName),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var dll in Directory.GetFiles(gameAssemblies, "*.dll"))
                {
                    string name = Path.GetFileName(dll);
                    if (present.Contains(name) || NeverCopied.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    Directory.CreateDirectory(plugins);
                    File.Copy(dll, Path.Combine(plugins, name));
                    result.LibrariesAdded++;
                }
            }

            bool firstPass = HasScripts(plugins);
            if (firstPass)
                WriteGameCodeAssembly(Path.Combine(plugins, GameCodeFirstPass + ".asmdef"), GameCodeFirstPass, null);
            if (HasScripts(scripts))
                WriteGameCodeAssembly(Path.Combine(scripts, GameCode + ".asmdef"), GameCode, firstPass ? GameCodeFirstPass : null);
            foreach (var dll in Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories))
                if (ConstrainLibrary(dll))
                    result.LibrariesSwitchedOff++;
            result.GameCodeOff = firstPass || HasScripts(scripts) || result.LibrariesSwitchedOff > 0;

            foreach (var asset in Directory.GetFiles(assets, "*.asset", SearchOption.AllDirectories))
            {
                if (!IsUnreadableShaderProgram(asset))
                    continue;
                File.Delete(asset);
                if (File.Exists(asset + ".meta")) File.Delete(asset + ".meta");
                result.UnreadableRemoved++;
            }

            foreach (var file in Directory.EnumerateFiles(assets, "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                result.ObsoleteComponentsRemoved += RemoveObsoleteComponents(file);

            result.SpawnedObjects = SpawnScanner.Scan(assets);

            if (Directory.Exists(scripts))
            {
                foreach (var file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
                {
                    if (GeneratedScripts.Any(p => Path.GetFileName(file).StartsWith(p, StringComparison.Ordinal)))
                    {
                        File.Delete(file);
                        if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
                        result.GeneratedRemoved++;
                    }
                }
                result.ScriptsStopped = StopEditModeExecution(scripts);
            }

            string tools = Path.Combine(assets, ToolsFolder, "Editor");
            Directory.CreateDirectory(tools);
            File.Copy(Path.Combine(toolsSource, "MaterialRepair.cs"), Path.Combine(tools, "MaterialRepair.cs"), true);
            File.Copy(Path.Combine(toolsSource, "ExtractedProjectTools~", "ExtractedProjectSetup.cs"), Path.Combine(tools, "ExtractedProjectSetup.cs"), true);
            File.Copy(Path.Combine(toolsSource, "ExtractedProjectTools~", "SpawnPreview.cs"), Path.Combine(tools, "SpawnPreview.cs"), true);
            File.WriteAllText(Path.Combine(tools, "GameSpelunker.ExtractedTools.asmdef"),
                "{\n    \"name\": \"GameSpelunker.ExtractedTools\",\n    \"includePlatforms\": [\"Editor\"],\n    \"autoReferenced\": false\n}\n");
            result.ToolsInstalled = true;
            return result;
        }

        public const string GameCodeDefine = "SPELUNKER_GAME_CODE";
        private const string GameCode = "Spelunked.GameCode";
        private const string GameCodeFirstPass = "Spelunked.GameCode.FirstPass";

        private static readonly string[] NeverCopied = { "UnityEngine", "Unity.", "System", "Mono.", "mscorlib", "netstandard", "Microsoft.", "Assembly-" };

        private static bool HasScripts(string folder) =>
            Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Any();

        private static void WriteGameCodeAssembly(string path, string name, string? reference)
        {
            string references = reference != null ? $"\"{reference}\"" : "";
            File.WriteAllText(path,
                "{\n" +
                $"    \"name\": \"{name}\",\n" +
                $"    \"references\": [{references}],\n" +
                "    \"allowUnsafeCode\": true,\n" +
                $"    \"defineConstraints\": [\"{GameCodeDefine}\"]\n" +
                "}\n");
        }

        private static bool ConstrainLibrary(string dll)
        {
            string meta = dll + ".meta";
            string constraint = "  defineConstraints:\n  - " + GameCodeDefine;
            if (!File.Exists(meta))
            {
                File.WriteAllText(meta, "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\nPluginImporter:\n  serializedVersion: 2\n" +
                                        constraint + "\n  isPreloaded: 0\n  isOverridable: 0\n  platformData:\n  - first:\n      Any:\n    second:\n" +
                                        "      enabled: 1\n      settings: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n");
                return true;
            }
            string text = File.ReadAllText(meta).Replace("\r\n", "\n");
            if (text.Contains("- " + GameCodeDefine))
                return false;
            if (Regex.IsMatch(text, @"^  defineConstraints: \[\]$", RegexOptions.Multiline))
                text = Regex.Replace(text, @"^  defineConstraints: \[\]$", constraint, RegexOptions.Multiline);
            else if (Regex.IsMatch(text, @"^PluginImporter:$", RegexOptions.Multiline))
                text = Regex.Replace(text, @"^PluginImporter:$", "PluginImporter:\n" + constraint, RegexOptions.Multiline);
            else
            {
                var guid = Regex.Match(text, @"^guid: (\w+)", RegexOptions.Multiline);
                if (!guid.Success)
                    return false;
                text = "fileFormatVersion: 2\nguid: " + guid.Groups[1].Value + "\nPluginImporter:\n  serializedVersion: 2\n" + constraint +
                       "\n  platformData:\n  - first:\n      Any:\n    second:\n      enabled: 1\n      settings: {}\n";
            }
            File.WriteAllText(meta, text);
            return true;
        }

        private static bool IsUnreadableShaderProgram(string asset)
        {
            using var reader = new StreamReader(asset);
            for (int i = 0; i < 4; i++)
            {
                string? line = reader.ReadLine();
                if (line == null) return false;
                if (line.StartsWith("--- !u!109 ", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static readonly Regex ObsoleteComponentHeader = new Regex(@"^--- !u!(92|131|132) &(-?\d+)", RegexOptions.Compiled);

        private static int RemoveObsoleteComponents(string file)
        {
            string text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"^--- !u!(92|131|132) ", RegexOptions.Multiline))
                return 0;
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var removed = new HashSet<string>();
            foreach (var line in lines)
            {
                var match = ObsoleteComponentHeader.Match(line);
                if (match.Success) removed.Add(match.Groups[2].Value);
            }
            var kept = new List<string>(lines.Length);
            bool skipping = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("--- ", StringComparison.Ordinal))
                    skipping = ObsoleteComponentHeader.IsMatch(line);
                if (skipping)
                    continue;
                var reference = Regex.Match(line, @"^\s*- (?:component|\d+): \{fileID: (-?\d+)\}\s*$");
                if (reference.Success && removed.Contains(reference.Groups[1].Value))
                    continue;
                kept.Add(line);
            }
            File.WriteAllText(file, string.Join(newline, kept));
            return removed.Count;
        }

        public static int StopEditModeExecution(string folder)
        {
            int changed = 0;
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.IndexOf("ExecuteInEditMode", StringComparison.Ordinal) < 0 && text.IndexOf("ExecuteAlways", StringComparison.Ordinal) < 0)
                    continue;
                string updated = EditModeAttributeInList.Replace(EditModeAttribute.Replace(text, EditModeNote), "");
                if (updated == text)
                    continue;
                File.WriteAllText(file, updated);
                changed++;
            }
            return changed;
        }

        public static string EditorFor(string exportFolder, string fallback) => FindEditor(UnityVersionOf(exportFolder)) ?? fallback;

        public static async Task<string> ImportAndRepairAsync(string exportFolder, string editor, IProgress<float>? progress, CancellationToken token)
        {
            string project = ProjectFolder(exportFolder);
            string log = Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-import.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            if (File.Exists(log))
                File.Delete(log);
            int total = Math.Max(1, Directory.EnumerateFiles(Path.Combine(project, "Assets"), "*", SearchOption.AllDirectories)
                .Count(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)));

            var info = new ProcessStartInfo(editor,
                $"-batchmode -projectPath \"{project}\" -executeMethod GameSpelunker.ExtractedProjectSetup.RunBatch -logFile \"{log}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Unity.");
            using var registration = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            });

            long position = 0;
            int imported = 0;
            while (!process.HasExited)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                imported += CountImports(log, ref position);
                progress?.Report(Math.Min(0.99f, imported / (float)total));
            }
            token.ThrowIfCancellationRequested();

            string text = ReadShared(log);
            var ready = Regex.Match(text, @"\[Game Spelunker\] Ready: (.*)");
            if (ready.Success)
                return ready.Groups[1].Value.Trim();
            if (text.Contains("another Unity instance is running with this project open") || text.Contains("It looks like another Unity instance"))
                throw new InvalidOperationException("The project is already open in Unity. Close it and click Open in Unity: it repairs on first open.");
            if (text.Contains("Scripts have compiler errors"))
                throw new InvalidOperationException("The project has compile errors, so Unity could not run the repair. See " + log);
            throw new InvalidOperationException($"Unity stopped before the repair (exit code {process.ExitCode}). See {log}");
        }

        private static int CountImports(string log, ref long position)
        {
            if (!File.Exists(log))
                return 0;
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= position)
                return 0;
            stream.Seek(position, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string chunk = reader.ReadToEnd();
            position = stream.Length;
            int count = 0, index = 0;
            while ((index = chunk.IndexOf("Start importing ", index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += 16;
            }
            return count;
        }

        private static string ReadShared(string path)
        {
            if (!File.Exists(path))
                return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static void OpenInUnity(string exportFolder)
        {
            PrepareForOpening(exportFolder, ToolsSource());
            string project = ProjectFolder(exportFolder);
            string editor = EditorFor(exportFolder, EditorApplication.applicationPath);
            Process.Start(new ProcessStartInfo(editor, $"-projectPath \"{project}\"") { UseShellExecute = false });
        }
    }
}
