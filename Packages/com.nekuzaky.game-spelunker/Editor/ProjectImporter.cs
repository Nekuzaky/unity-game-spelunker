using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameSpelunker
{
    public sealed class ImportOptions
    {
        /// <summary>Project folder to import into, e.g. "Assets/Spelunked/My Game".</summary>
        public string TargetFolder = "";
        public bool IncludeScenes = true;
        /// <summary>
        /// Copies the decompiled scripts under an assembly that only compiles once the define named by
        /// <see cref="ProjectImporter.CompileDefine"/> is added, so they can never break the project.
        /// </summary>
        public bool IncludeScripts;
    }

    public sealed class ImportResult
    {
        public int FilesCopied;
        public int GuidConflicts;
        public string TargetFolder = "";
        public string? ScriptsDefine;
        public List<string> SkippedFolders = new List<string>();
        public List<string> MissingPackages = new List<string>();
    }

    /// <summary>
    /// Copies an AssetRipper export into the open project, keeping .meta files so references between assets survive.
    /// Never touches project settings, and skips what could break or bloat the host project: engine shaders, native
    /// plugins, editor folders, assets whose GUID already exists here, and (unless asked) the decompiled scripts.
    /// </summary>
    public static class ProjectImporter
    {
        /// <summary>Folders of an AssetRipper export that are never imported.</summary>
        private static readonly HashSet<string> AlwaysSkipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shader", "ComputeShader", "Plugins", "Editor", "Editor Default Resources", "Gizmos", "StreamingAssets",
        };

        /// <summary>Folder names with a special meaning in Unity, renamed so they behave like plain folders here.</summary>
        private static readonly Dictionary<string, string> SpecialFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Resources"] = "Resources (imported)",
            ["Editor"] = "Editor (imported)",
            ["Plugins"] = "Plugins (imported)",
        };

        public static string DefaultTarget(string gameName)
        {
            string safe = Regex.Replace(gameName, @"[^\w\- ]", "_").Trim();
            return "Assets/Spelunked/" + (safe.Length == 0 ? "Game" : safe);
        }

        /// <summary>The scripting define that turns on compilation of imported scripts for a game.</summary>
        public static string CompileDefine(string gameName) =>
            "SPELUNKER_COMPILE_" + Regex.Replace(gameName.ToUpperInvariant(), @"[^A-Z0-9]", "_");

        public static ImportResult Import(string exportFolder, string gameName, ImportOptions options)
        {
            string source = Path.Combine(ExtractedProject.ProjectFolder(exportFolder), "Assets");
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("This export has no Assets folder.");
            if (!options.TargetFolder.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("The target folder must be inside Assets/.");

            string projectRoot = Path.GetDirectoryName(Application.dataPath)!;
            string target = Path.Combine(projectRoot, options.TargetFolder);
            var result = new ImportResult { TargetFolder = options.TargetFolder.Replace('\\', '/') };

            var topFolders = Directory.GetDirectories(source).ToList();
            var copied = new List<string>();
            try
            {
                for (int i = 0; i < topFolders.Count; i++)
                {
                    string folder = topFolders[i];
                    string name = Path.GetFileName(folder);
                    bool isScripts = name.Equals("Scripts", StringComparison.OrdinalIgnoreCase);
                    bool isScenes = name.Equals("Scenes", StringComparison.OrdinalIgnoreCase);

                    if (AlwaysSkipped.Contains(name) || (isScripts && !options.IncludeScripts) || (isScenes && !options.IncludeScenes))
                    {
                        result.SkippedFolders.Add(name);
                        continue;
                    }

                    if (EditorUtility.DisplayCancelableProgressBar("Game Spelunker", $"Importing {name}…", i / (float)topFolders.Count))
                        break;

                    CopyFolder(folder, Path.Combine(target, Rename(name)), result, copied);
                }

                if (options.IncludeScripts && Directory.Exists(Path.Combine(target, "Scripts")))
                {
                    result.ScriptsDefine = CompileDefine(gameName);
                    WriteScriptsAssembly(Path.Combine(target, "Scripts"), gameName, result.ScriptsDefine);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            result.MissingPackages = MissingPackages(exportFolder);
            AssetDatabase.Refresh();
            return result;
        }

        private static string Rename(string folderName) =>
            SpecialFolders.TryGetValue(folderName, out var renamed) ? renamed : folderName;

        private static void CopyFolder(string from, string to, ImportResult result, List<string> copied)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                // An asset whose GUID is already used here would hijack references in the host project: skip it.
                string meta = file + ".meta";
                if (File.Exists(meta) && ReadGuid(meta) is { } guid && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    result.GuidConflicts++;
                    continue;
                }

                string dest = Path.Combine(to, Path.GetFileName(file));
                File.Copy(file, dest, true);
                if (File.Exists(meta))
                    File.Copy(meta, dest + ".meta", true);
                copied.Add(dest);
                result.FilesCopied++;
            }

            foreach (var dir in Directory.GetDirectories(from))
                CopyFolder(dir, Path.Combine(to, Rename(Path.GetFileName(dir))), result, copied);
        }

        private static string? ReadGuid(string metaPath)
        {
            foreach (var line in File.ReadLines(metaPath).Take(5))
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                    return line.Substring(5).Trim();
            return null;
        }

        /// <summary>
        /// One assembly for all imported scripts, compiled only when the define is present: the scripts can be read and
        /// prefabs keep their script references, but nothing compiles (or fails to compile) until the user opts in.
        /// </summary>
        private static void WriteScriptsAssembly(string scriptsFolder, string gameName, string define)
        {
            string assemblyName = "Spelunked." + Regex.Replace(gameName, @"[^A-Za-z0-9]", "");
            string json = "{\n" +
                          $"    \"name\": \"{assemblyName}\",\n" +
                          "    \"references\": [],\n" +
                          "    \"includePlatforms\": [],\n" +
                          "    \"excludePlatforms\": [],\n" +
                          "    \"allowUnsafeCode\": true,\n" +
                          "    \"overrideReferences\": false,\n" +
                          "    \"autoReferenced\": false,\n" +
                          $"    \"defineConstraints\": [\"{define}\"],\n" +
                          "    \"noEngineReferences\": false\n" +
                          "}\n";
            File.WriteAllText(Path.Combine(scriptsFolder, assemblyName + ".asmdef"), json);

            // Assembly-level files from the decompiled assemblies would clash once merged into one assembly.
            foreach (var info in Directory.GetFiles(scriptsFolder, "AssemblyInfo.cs", SearchOption.AllDirectories))
            {
                File.Delete(info);
                if (File.Exists(info + ".meta")) File.Delete(info + ".meta");
            }
        }

        /// <summary>Packages the game used (from the export's manifest) that this project does not have.</summary>
        public static List<string> MissingPackages(string exportFolder)
        {
            string exported = Path.Combine(ExtractedProject.ProjectFolder(exportFolder), "Packages", "manifest.json");
            string mine = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Packages", "manifest.json");
            if (!File.Exists(exported) || !File.Exists(mine))
                return new List<string>();

            var pattern = new Regex("\"(com\\.[a-z0-9\\.\\-]+)\"\\s*:", RegexOptions.IgnoreCase);
            var theirs = pattern.Matches(File.ReadAllText(exported)).Cast<Match>().Select(m => m.Groups[1].Value);
            var ours = new HashSet<string>(pattern.Matches(File.ReadAllText(mine)).Cast<Match>().Select(m => m.Groups[1].Value));
            return theirs.Where(p => !ours.Contains(p) && !p.StartsWith("com.unity.modules.", StringComparison.Ordinal)).Distinct().OrderBy(p => p).ToList();
        }
    }
}
