using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            result.GameCodeOff = firstPass || HasScripts(scripts);

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

        public static void OpenInUnity(string exportFolder)
        {
            PrepareForOpening(exportFolder, ToolsSource());
            string project = ProjectFolder(exportFolder);
            string editor = FindEditor(UnityVersionOf(exportFolder)) ?? EditorApplication.applicationPath;
            Process.Start(new ProcessStartInfo(editor, $"-projectPath \"{project}\"") { UseShellExecute = false });
        }
    }
}
