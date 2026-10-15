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

        /// <summary>Opens the extracted project in a new Unity instance (the matching version when installed).</summary>
        public static void OpenInUnity(string exportFolder)
        {
            string project = ProjectFolder(exportFolder);
            string editor = FindEditor(UnityVersionOf(exportFolder)) ?? EditorApplication.applicationPath;
            Process.Start(new ProcessStartInfo(editor, $"-projectPath \"{project}\"") { UseShellExecute = false });
        }
    }
}
