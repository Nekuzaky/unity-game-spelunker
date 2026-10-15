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
        public int CursorTextures;
        public int ScriptsFixed;
        public int NativeLibrariesAdded;
        public int LibrariesRestored;
        public bool SameVersionAsGame;
        public bool HasGameCode;
        public bool MigratesApis;
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

        public static PrepareResult PrepareForOpening(string exportFolder, string toolsSource, string? gameDataFolder = null)
        {
            var result = new PrepareResult();
            string assets = Path.Combine(ProjectFolder(exportFolder), "Assets");
            if (!Directory.Exists(assets))
                return result;

            string gameNote = Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-game.txt");
            if (gameDataFolder != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gameNote)!);
                File.WriteAllText(gameNote, gameDataFolder);
            }
            else if (File.Exists(gameNote))
            {
                gameDataFolder = File.ReadAllText(gameNote).Trim();
            }
            if (gameDataFolder != null && Directory.Exists(gameDataFolder))
                result.NativeLibrariesAdded = CopyNativeLibraries(gameDataFolder, Path.Combine(assets, "Plugins", "Native"));

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

            string? gameVersion = UnityVersionOf(exportFolder);
            result.SameVersionAsGame = FindEditor(gameVersion) != null;
            var providedByEditor = result.SameVersionAsGame && !IsAtLeast(gameVersion, 2018, 2) ? EditorScriptingLibraries : new string[0];
            foreach (var name in providedByEditor)
            {
                string copied = Path.Combine(plugins, name);
                if (File.Exists(copied)) File.Delete(copied);
                if (File.Exists(copied + ".meta")) File.Delete(copied + ".meta");
            }

            string gameAssemblies = Path.Combine(exportFolder, "AuxiliaryFiles", "GameAssemblies");
            if (Directory.Exists(gameAssemblies))
            {
                var present = new HashSet<string>(Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileName),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var dll in Directory.GetFiles(gameAssemblies, "*.dll"))
                {
                    string name = Path.GetFileName(dll);
                    if (present.Contains(name) || NeverCopied.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                        || providedByEditor.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    Directory.CreateDirectory(plugins);
                    File.Copy(dll, Path.Combine(plugins, name));
                    result.LibrariesAdded++;
                }
            }

            if (gameDataFolder != null)
                result.LibrariesRestored = RestoreOriginalLibraries(assets, Path.Combine(gameDataFolder, "Managed"));

            result.HasGameCode = HasScripts(scripts) || HasScripts(plugins);
            result.MigratesApis = !result.SameVersionAsGame;
            bool assemblyDefinitions = !result.SameVersionAsGame || IsAtLeast(gameVersion, 2017, 3);

            if (result.MigratesApis)
            {
                string compat = Path.Combine(plugins, ToolsFolder);
                Directory.CreateDirectory(compat);
                File.Copy(Path.Combine(toolsSource, "ExtractedProjectTools~", "LegacyApi.cs"), Path.Combine(compat, "LegacyApi.cs"), true);
            }
            result.ScriptsFixed = FixDecompiledCode(scripts, result.MigratesApis) + FixDecompiledCode(plugins, result.MigratesApis);

            if (assemblyDefinitions)
            {
                var firstPassRefs = new List<string> { GameCodeFirstPass };
                WriteGameCodeAssembly(Path.Combine(plugins, GameCodeFirstPass + ".asmdef"), GameCodeFirstPass, new string[0]);
                foreach (var folder in LaterAssemblyFolders(plugins))
                {
                    string name = "Spelunked." + Path.GetFileName(folder);
                    WriteGameCodeAssembly(Path.Combine(folder, name + ".asmdef"), name, new[] { GameCodeFirstPass });
                    firstPassRefs.Add(name);
                }
                if (HasScripts(scripts))
                {
                    WriteGameCodeAssembly(Path.Combine(scripts, GameCode + ".asmdef"), GameCode, firstPassRefs.ToArray());
                    foreach (var folder in LaterAssemblyFolders(scripts))
                    {
                        string name = "Spelunked." + Path.GetFileName(folder);
                        WriteGameCodeAssembly(Path.Combine(folder, name + ".asmdef"), name, firstPassRefs.Concat(new[] { GameCode }).ToArray());
                    }
                }
                foreach (var dll in Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories))
                    if (ConstrainLibrary(dll))
                        result.LibrariesSwitchedOff++;
                result.GameCodeOff = true;
            }

            foreach (var asset in Directory.GetFiles(assets, "*.asset", SearchOption.AllDirectories))
            {
                if (!IsUnreadableShaderProgram(asset))
                    continue;
                File.Delete(asset);
                if (File.Exists(asset + ".meta")) File.Delete(asset + ".meta");
                result.UnreadableRemoved++;
            }

            if (result.MigratesApis)
                foreach (var file in Directory.EnumerateFiles(assets, "*.*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                    result.ObsoleteComponentsRemoved += RemoveObsoleteComponents(file);

            result.SpawnedObjects = SpawnScanner.Scan(assets);
            result.CursorTextures = ImportCursorTextures(assets, scripts);

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
            File.WriteAllText(Path.Combine(tools, "MaterialRepair.cs"),
                Regex.Replace(File.ReadAllText(Path.Combine(toolsSource, "MaterialRepair.cs")), @"^#if UNITY_2020_2_OR_NEWER\r?\n#nullable disable\r?\n#endif\r?\n", "",
                    RegexOptions.Multiline));
            File.Copy(Path.Combine(toolsSource, "ExtractedProjectTools~", "ExtractedProjectSetup.cs"), Path.Combine(tools, "ExtractedProjectSetup.cs"), true);
            File.Copy(Path.Combine(toolsSource, "ExtractedProjectTools~", "SpawnPreview.cs"), Path.Combine(tools, "SpawnPreview.cs"), true);
            if (assemblyDefinitions)
                File.WriteAllText(Path.Combine(tools, "GameSpelunker.ExtractedTools.asmdef"),
                    "{\n    \"name\": \"GameSpelunker.ExtractedTools\",\n    \"includePlatforms\": [\"Editor\"],\n    \"autoReferenced\": false\n}\n");
            result.ToolsInstalled = true;
            return result;
        }

        public const string GameCodeDefine = "SPELUNKER_GAME_CODE";
        private const string GameCode = "Spelunked.GameCode";
        private const string GameCodeFirstPass = "Spelunked.GameCode.FirstPass";

        private static int RestoreOriginalLibraries(string assets, string managed)
        {
            if (!Directory.Exists(managed))
                return 0;
            int restored = 0;
            foreach (var dll in Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(dll);
                string original = Path.Combine(managed, name);
                if (!File.Exists(original) || NeverCopied.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var bytes = File.ReadAllBytes(original);
                if (bytes.AsSpan().SequenceEqual(File.ReadAllBytes(dll)))
                    continue;
                File.WriteAllBytes(dll, bytes);
                restored++;
            }
            return restored;
        }

        private static readonly string[] EditorScriptingLibraries ={ "Boo.Lang.dll", "UnityScript.Lang.dll" };

        private static readonly string[] NeverCopied ={ "UnityEngine", "Unity.", "System", "Mono.", "mscorlib", "netstandard", "Microsoft.", "Assembly-" };

        private static bool HasScripts(string folder) =>
            Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Any();

        private static IEnumerable<string> LaterAssemblyFolders(string root) =>
            Directory.Exists(root)
                ? Directory.GetDirectories(root).Where(d => Regex.IsMatch(Path.GetFileName(d), "^Assembly-(UnityScript|Boo)", RegexOptions.IgnoreCase) && HasScripts(d))
                : Enumerable.Empty<string>();

        private static void WriteGameCodeAssembly(string path, string name, string[] referenced)
        {
            string references = string.Join(", ", referenced.Select(r => $"\"{r}\""));
            File.WriteAllText(path,
                "{\n" +
                $"    \"name\": \"{name}\",\n" +
                $"    \"references\": [{references}],\n" +
                "    \"allowUnsafeCode\": true,\n" +
                $"    \"defineConstraints\": [\"{GameCodeDefine}\"]\n" +
                "}\n");
        }

        private static int CopyNativeLibraries(string dataFolder, string target)
        {
            string plugins = Path.Combine(dataFolder, "Plugins");
            if (!Directory.Exists(plugins))
                return 0;
            var libraries = Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories)
                .Where(f => !Regex.IsMatch(Path.GetFileName(f), "^lib_burst_generated", RegexOptions.IgnoreCase))
                .Where(f => !Regex.IsMatch(f.Replace('\\', '/'), "/x86/", RegexOptions.IgnoreCase))
                .ToList();
            int copied = 0;
            foreach (var library in libraries)
            {
                string destination = Path.Combine(target, Path.GetFileName(library));
                if (File.Exists(destination))
                    continue;
                if (IsManagedAssembly(library))
                    continue;
                Directory.CreateDirectory(target);
                File.Copy(library, destination);
                File.WriteAllText(destination + ".meta",
                    "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\nPluginImporter:\n  serializedVersion: 2\n  isPreloaded: 0\n" +
                    "  isOverridable: 0\n  platformData:\n  - first:\n      Any:\n    second:\n      enabled: 0\n      settings: {}\n" +
                    "  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n      settings:\n        CPU: x86_64\n        OS: Windows\n" +
                    "  - first:\n      Standalone: Win64\n    second:\n      enabled: 1\n      settings:\n        CPU: x86_64\n" +
                    "  userData:\n  assetBundleName:\n  assetBundleVariant:\n");
                copied++;
            }
            return copied;
        }

        private static bool IsManagedAssembly(string dll)
        {
            try
            {
                System.Reflection.AssemblyName.GetAssemblyName(dll);
                return true;
            }
            catch (BadImageFormatException) { return false; }
            catch (IOException) { return false; }
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

        private static readonly Regex SetCursorCall = new Regex(@"Cursor\.SetCursor\(\s*(?:this\.)?(?:\w+\.)*(\w+)\s*,", RegexOptions.Compiled);
        private static readonly string[] TextureExtensions = { ".png", ".tga", ".jpg", ".jpeg", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".hdr" };

        private static int ImportCursorTextures(string assets, string scripts)
        {
            if (!Directory.Exists(scripts))
                return 0;
            var fields = new HashSet<string>();
            foreach (var file in Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.IndexOf("Cursor.SetCursor", StringComparison.Ordinal) < 0)
                    continue;
                foreach (Match call in SetCursorCall.Matches(text))
                    if (call.Groups[1].Value != "null")
                        fields.Add(call.Groups[1].Value);
            }
            if (fields.Count == 0)
                return 0;

            var reference = new Regex(@"^\s+(?:" + string.Join("|", fields.Select(Regex.Escape)) + @"): \{fileID: 2800000, guid: ([0-9a-f]{32}), type: 3\}",
                RegexOptions.Multiline);
            var guids = new HashSet<string>();
            foreach (var file in Directory.EnumerateFiles(assets, "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
            {
                string text = File.ReadAllText(file);
                foreach (Match match in reference.Matches(text))
                    guids.Add(match.Groups[1].Value);
            }
            if (guids.Count == 0)
                return 0;

            int changed = 0;
            foreach (var meta in Directory.EnumerateFiles(assets, "*.meta", SearchOption.AllDirectories)
                         .Where(m => TextureExtensions.Any(e => m.EndsWith(e + ".meta", StringComparison.OrdinalIgnoreCase))))
            {
                string text = File.ReadAllText(meta);
                var guid = Regex.Match(text, @"^guid: (\w+)", RegexOptions.Multiline);
                if (!guid.Success || !guids.Contains(guid.Groups[1].Value))
                    continue;
                string updated = Regex.Replace(text, @"^  textureType: \d+", "  textureType: 7", RegexOptions.Multiline);
                updated = Regex.Replace(updated, @"^  isReadable: \d+", "  isReadable: 1", RegexOptions.Multiline);
                if (updated == text)
                    continue;
                File.WriteAllText(meta, updated);
                changed++;
            }
            return changed;
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

        private static readonly string[] RemovedPlatforms =
        {
            "OSXDashboardPlayer", "OSXWebPlayer", "WindowsWebPlayer", "FlashPlayer", "NaCl", "BlackBerryPlayer", "TizenPlayer", "SamsungTVPlayer",
            "WiiU", "XBOX360", "PS3", "PSP2", "PSM", "WP8Player", "MetroPlayerX86", "MetroPlayerX64", "MetroPlayerARM", "LinuxEditor",
        };

        private static readonly Regex BackingField = new Regex(@"\b(\w+)__BackingField\s*;", RegexOptions.Compiled);

        private static string RenameType(string code, string original, string name, string replacement)
        {
            if (Regex.IsMatch(original, @"\b(?:class|struct|enum|interface)\s+" + name + @"\b"))
                return code;
            return Regex.Replace(code, @"(?<![\w\.:])(?:global::)?(?:UnityEngine\.)?" + name + @"\b(?!\s*=[^=>])", replacement);
        }

        public static bool IsAtLeast(string? version, int major, int minor)
        {
            if (version == null)
                return true;
            var parts = Regex.Match(version, @"^(\d+)\.(\d+)");
            if (!parts.Success)
                return true;
            int vMajor = int.Parse(parts.Groups[1].Value), vMinor = int.Parse(parts.Groups[2].Value);
            return vMajor > major || (vMajor == major && vMinor >= minor);
        }

        public static int FixDecompiledCode(string folder, bool migrateApis)
        {
            if (!Directory.Exists(folder))
                return 0;
            int changed = 0;
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/" + ToolsFolder + "/"))
                    continue;
                string text = File.ReadAllText(file);
                string updated = text;

                foreach (Match field in BackingField.Matches(text))
                {
                    string name = field.Groups[1].Value;
                    if (Regex.IsMatch(text, @"\bevent\s+[\w<>\.,\s]+\s" + Regex.Escape(name) + @"\b"))
                        updated = Regex.Replace(updated, @"\bthis\." + Regex.Escape(name) + @"\b(?!__)", "this." + name + "__BackingField");
                }

                updated = Regex.Replace(updated, @"(?<![\w\.])Random(\s+\w+\s*=\s*new\s+)Random\s*\(", "System.Random$1System.Random(");
                if (migrateApis)
                    updated = MigrateRemovedApis(updated, text);

                if (updated == text)
                    continue;
                File.WriteAllText(file, updated);
                changed++;
            }
            return changed;
        }

        private static string MigrateRemovedApis(string updated, string text)
        {
                foreach (var shortcut in new[] { "particleEmitter:ParticleEmitter", "particleAnimator:ParticleAnimator", "particleRenderer:ParticleRenderer",
                             "guiText:GUIText", "guiTexture:GUITexture" })
                {
                    if (text.Contains("ComponentAction<"))
                        break;
                    var parts = shortcut.Split(':');
                    if (Regex.IsMatch(text, @"\b[\w<>\[\]\.]+\s+" + parts[0] + @"\s*(?:[;={]|=>)"))
                        continue;
                    updated = Regex.Replace(updated, @"\.(" + parts[0] + @")\b(?!\s*\()", ".GetComponent<" + parts[1] + ">()");
                }

                for (int i = 0; i < RemovedPlatforms.Length; i++)
                    updated = Regex.Replace(updated, @"\bRuntimePlatform\." + RemovedPlatforms[i] + @"\b", "((RuntimePlatform)(" + (-1000 - i) + "))");

                updated = RenameType(updated, text, "PhysicMaterial", "PhysicsMaterial");
                updated = RenameType(updated, text, "PhysicMaterialCombine", "PhysicsMaterialCombine");
                foreach (var removed in new[] { "MovieTexture", "GUIText", "GUITexture", "GUIElement", "GUILayer", "ProceduralMaterial" })
                    updated = RenameType(updated, text, removed, "global::GameSpelunker." + removed + "Stub");
                updated = Regex.Replace(updated, @"\b(?:UnityEngine\.)?Application\.isWebPlayer\b", "false");
                updated = Regex.Replace(updated, @"\b(?:UnityEngine\.)?Application\.CaptureScreenshot\s*\(", "UnityEngine.ScreenCapture.CaptureScreenshot(");
                updated = Regex.Replace(updated, @"\.impactForceSum\b", ".impulse");
                updated = Regex.Replace(updated, @"\bTextureFormat\.ASTC_RGBA?_(\d+x\d+)\b", "TextureFormat.ASTC_$1");
                updated = Regex.Replace(updated, @"\bTextureFormat\.(ATC_RGB4|ATC_RGBA8|PVRTC_2BPP_RGB|PVRTC_2BPP_RGBA|PVRTC_4BPP_RGB|PVRTC_4BPP_RGBA)\b", "((TextureFormat)(-2000))");
                updated = Regex.Replace(updated, @"(?m)^(\s*)[\w\.\[\]\(\)]+\.(useConeFriction|sleepVelocity|sleepAngularVelocity)\s*=\s*[^;]+;", "$1;");
                updated = Regex.Replace(updated, @"\bnew\s+(?:UnityEngine\.)?Material\s*\(", "global::GameSpelunker.Compat.NewMaterial(");
                updated = Regex.Replace(updated, @"\b(?:UnityEngine\.)?Graphics\.DrawProceduralIndirect\s*\(", "global::GameSpelunker.Compat.DrawProceduralIndirect(");
                return updated;
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
