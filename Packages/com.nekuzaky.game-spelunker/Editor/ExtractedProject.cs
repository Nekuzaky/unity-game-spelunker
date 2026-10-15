using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GameSpelunker
{
    /// <summary>A decompiled script found in an extracted project.</summary>
    public sealed class ExtractedScript
    {
        public string Path = "";
        public string RelativePath = "";
        public string Assembly = "";
        public string ClassName = "";
    }

    /// <summary>What <see cref="ExtractedProject.PrepareForOpening"/> changed.</summary>
    public sealed class PrepareResult
    {
        /// <summary>Scripts that no longer run in the editor.</summary>
        public int ScriptsStopped;
        /// <summary>Build-generated scripts removed.</summary>
        public int GeneratedRemoved;
        public bool ToolsInstalled;
    }

    /// <summary>Helpers for an AssetRipper export folder (ExportedProject/ + AuxiliaryFiles/).</summary>
    public static class ExtractedProject
    {
        /// <summary>Assemblies AssetRipper also exports but that are part of the engine or packages, not the game.</summary>
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

        /// <summary>The game's own decompiled scripts (Assets/Scripts/&lt;Assembly&gt;/…), engine and package code excluded.</summary>
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

        /// <summary>The Unity editor installed through Unity Hub for a version, or null.</summary>
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
        // No brackets in the note: a second pass must not match it again.
        private const string EditModeNote = "/* ExecuteInEditMode removed by Game Spelunker: this code only runs in Play mode */";

        /// <summary>Code the game's build generated (Burst, Jobs): it clashes with the code Unity generates for the project.</summary>
        private static readonly string[] GeneratedScripts = { "__JobReflectionRegistrationOutput__", "__UnmanagedPostProcessorOutput__", "__BurstDirectCallInitializer" };

        /// <summary>Folder of Assets where <see cref="PrepareForOpening"/> installs Game Spelunker's tools in an export.</summary>
        public const string ToolsFolder = "GameSpelunker";

        /// <summary>The package's Editor folder on disk, wherever the package is installed. Main thread only.</summary>
        public static string ToolsSource()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(SpelunkerWindow.PackageRoot);
            string root = info != null ? info.resolvedPath : Path.GetFullPath(SpelunkerWindow.PackageRoot);
            return Path.Combine(root, "Editor");
        }

        /// <summary>
        /// Makes an extraction safe to open: game scripts stop running in the editor (decompiled [ExecuteInEditMode] code
        /// can crash it), build-generated code is removed, and the material repair is installed to run on first open.
        /// Safe to call again.
        /// </summary>
        public static PrepareResult PrepareForOpening(string exportFolder, string toolsSource)
        {
            var result = new PrepareResult();
            string assets = Path.Combine(ProjectFolder(exportFolder), "Assets");
            if (!Directory.Exists(assets))
                return result;

            string scripts = Path.Combine(assets, "Scripts");
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
            // Its own editor assembly: it compiles even when the decompiled game code does not.
            File.WriteAllText(Path.Combine(tools, "GameSpelunker.ExtractedTools.asmdef"),
                "{\n    \"name\": \"GameSpelunker.ExtractedTools\",\n    \"includePlatforms\": [\"Editor\"],\n    \"autoReferenced\": false\n}\n");
            result.ToolsInstalled = true;
            return result;
        }

        /// <summary>Removes [ExecuteInEditMode] / [ExecuteAlways] from the scripts in a folder. Returns the files changed.</summary>
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

        /// <summary>Opens the extracted project in a new Unity instance (the matching version when installed).</summary>
        public static void OpenInUnity(string exportFolder)
        {
            PrepareForOpening(exportFolder, ToolsSource());
            string project = ProjectFolder(exportFolder);
            string editor = FindEditor(UnityVersionOf(exportFolder)) ?? EditorApplication.applicationPath;
            Process.Start(new ProcessStartInfo(editor, $"-projectPath \"{project}\"") { UseShellExecute = false });
        }
    }
}
