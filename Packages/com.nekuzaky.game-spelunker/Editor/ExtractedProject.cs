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
        public int ShadersRestored;
        public string? SteamAppId;
        public List<string> ThirdParty = new List<string>();
        public int LightingDataRemoved;
        public bool DecompiledPipelineDropped;
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

        public static string? GameVersionOf(string exportFolder)
        {
            string note = Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-version.txt");
            if (File.Exists(note))
            {
                string saved = File.ReadAllText(note).Trim();
                if (saved.Length > 0) return saved;
            }
            string? version = UnityVersionOf(exportFolder);
            if (version != null && Directory.Exists(Path.GetDirectoryName(note)))
                File.WriteAllText(note, version + "\n");
            return version;
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
            bool mac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            foreach (var root in EditorRoots())
            {
                string exe = mac ? Path.Combine(root, version, "Unity.app", "Contents", "MacOS", "Unity") : Path.Combine(root, version, "Editor", "Unity.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }

        private static IEnumerable<string> EditorRoots()
        {
            string hubConfig = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "UnityHub")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub");
            string custom = Path.Combine(hubConfig, "secondaryInstallPath.json");
            if (File.Exists(custom))
            {
                string path = File.ReadAllText(custom).Trim().Trim('"').Replace(@"\\", @"\");
                if (path.Length > 0) yield return path;
            }
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                yield return "/Applications/Unity/Hub/Editor";
                yield break;
            }
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "Unity", "Hub", "Editor");
            yield return Path.Combine(programFiles, "Unity", "Editor");
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
            string project = ProjectFolder(exportFolder);
            string assets = Path.Combine(project, "Assets");
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
            {
                result.NativeLibrariesAdded = CopyNativeLibraries(gameDataFolder, Path.Combine(assets, "Plugins", "Native"));
                string gameFolder = Path.GetDirectoryName(gameDataFolder.TrimEnd('\\', '/'))!;
                string appIdFile = Path.Combine(project, "steam_appid.txt");
                if (!File.Exists(appIdFile))
                {
                    string? appId = null;
                    string shipped = Path.Combine(gameFolder, "steam_appid.txt");
                    if (File.Exists(shipped))
                        appId = File.ReadAllText(shipped).Trim();
                    if (string.IsNullOrEmpty(appId) && Directory.GetFiles(gameFolder, "steam_api*.dll").Length + Directory.GetFiles(gameDataFolder, "steam_api*.dll", SearchOption.AllDirectories).Length > 0)
                        appId = GameFinder.SteamAppId(gameFolder);
                    if (!string.IsNullOrEmpty(appId) && Regex.IsMatch(appId, @"^\d+$"))
                    {
                        File.WriteAllText(appIdFile, appId + "\n");
                        result.SteamAppId = appId;
                    }
                }
            }

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

            string? gameVersion = GameVersionOf(exportFolder);
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
                if (result.HasGameCode)
                    foreach (var dll in Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories))
                        if (ConstrainLibrary(dll))
                            result.LibrariesSwitchedOff++;
                result.GameCodeOff = result.HasGameCode;
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
            result.ThirdParty = DetectThirdParty(exportFolder, assets);
            result.DecompiledPipelineDropped = DropDecompiledRenderPipeline(project, assets);
            if (result.MigratesApis)
                result.LightingDataRemoved = RemoveIncompatibleLightingData(assets);

            string? shaderVersion = result.SameVersionAsGame ? gameVersion : UnityEngine.Application.unityVersion;
            try
            {
                BuiltinShaders.EnsureDownloadedAsync(shaderVersion, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is AggregateException) { }
            result.ShadersRestored = ShaderRestorer.Restore(assets, BuiltinShaders.Catalog(shaderVersion));

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
            File.Copy(Path.Combine(toolsSource, "ExtractedProjectTools~", "ProjectCheck.cs"), Path.Combine(tools, "ProjectCheck.cs"), true);
            if (assemblyDefinitions)
                File.WriteAllText(Path.Combine(tools, "GameSpelunker.ExtractedTools.asmdef"),
                    "{\n    \"name\": \"GameSpelunker.ExtractedTools\",\n    \"includePlatforms\": [\"Editor\"],\n    \"autoReferenced\": false\n}\n");
            result.ToolsInstalled = true;
            return result;
        }

        public const string GameCodeDefine = "SPELUNKER_GAME_CODE";
        private const string GameCode = "Spelunked.GameCode";
        private const string GameCodeFirstPass = "Spelunked.GameCode.FirstPass";

        private static readonly (string pattern, string name, string where)[] KnownPlugins =
        {
            ("Rewired_Core", "Rewired (input)", "guavaman.com/rewired"),
            ("PlayMaker", "PlayMaker (visual scripting)", "hutonggames.com"),
            ("DOTween", "DOTween (tweening)", "dotween.demigiant.com"),
            ("bolt.dll", "Photon Bolt (networking)", "photonengine.com"),
            ("Photon3Unity3D", "Photon Unity Networking", "photonengine.com"),
            ("Mirror", "Mirror (networking)", "mirror-networking.com"),
            ("Pathfinding.", "A* Pathfinding Project", "arongranberg.com/astar"),
            ("fmodstudio", "FMOD Studio (its sound banks are in StreamingAssets)", "fmod.com"),
            ("AkSoundEngine", "Wwise (its sound banks are in StreamingAssets)", "audiokinetic.com"),
            ("CSteamworks", "Steamworks.NET", "steamworks.github.io"),
            ("Facepunch.Steamworks", "Facepunch.Steamworks", "wiki.facepunch.com/steamworks"),
            ("Newtonsoft.Json", "Json.NET", "newtonsoft.com/json"),
            ("Sisus.", "Sisus tools (Power Inspector…)", "Asset Store"),
            ("FbxExporters", "FBX Exporter", "Unity package"),
            ("NGUI", "NGUI (interface)", "Asset Store"),
            ("TextMeshPro", "TextMesh Pro", "Unity package com.unity.textmeshpro"),
            ("Cinemachine", "Cinemachine", "Unity package com.unity.cinemachine"),
            ("Unity.Postprocessing", "Post Processing", "Unity package com.unity.postprocessing"),
            ("Unity.InputSystem", "Input System", "Unity package com.unity.inputsystem"),
            ("Unity.Addressables", "Addressables", "Unity package com.unity.addressables"),
            ("Unity.Timeline", "Timeline", "Unity package com.unity.timeline"),
        };

        private static List<string> DetectThirdParty(string exportFolder, string assets)
        {
            var found = new List<string>();
            var names = Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileName)
                .Concat(Directory.Exists(Path.Combine(assets, "Scripts")) ? Directory.GetDirectories(Path.Combine(assets, "Scripts")).Select(d => Path.GetFileName(d) + ".dll") : Enumerable.Empty<string>())
                .ToList();
            var lines = new List<string>();
            foreach (var (pattern, name, where) in KnownPlugins)
                if (names.Any(n => n!.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) || n!.Contains(pattern)))
                {
                    found.Add(name);
                    lines.Add(name + " — " + where);
                }
            string file = Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-plugins.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (lines.Count > 0)
                File.WriteAllText(file, "Plugins and middleware recognised in this game. Their files are in the project; the licence to use them " +
                                        "in your own work comes from their authors.\n" + string.Join("\n", lines) + "\n");
            else if (File.Exists(file))
                File.Delete(file);
            return found;
        }

        private static readonly Regex PipelineReference = new Regex(@"(m_CustomRenderPipeline|customRenderPipeline): \{fileID: \d+, guid: (\w+), type: \d+\}", RegexOptions.Compiled);

        private static bool DropDecompiledRenderPipeline(string project, string assets)
        {
            string settings = Path.Combine(project, "ProjectSettings");
            var files = new[] { "GraphicsSettings.asset", "QualitySettings.asset" }.Select(f => Path.Combine(settings, f)).Where(File.Exists).ToList();
            var guids = files.SelectMany(f => PipelineReference.Matches(File.ReadAllText(f)).Cast<Match>().Select(m => m.Groups[2].Value)).Distinct().ToList();
            if (guids.Count == 0)
                return false;
            var decompiled = new HashSet<string>();
            foreach (var meta in Directory.EnumerateFiles(assets, "*.asset.meta", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(meta);
                string? guid = guids.FirstOrDefault(g => text.Contains("guid: " + g));
                if (guid == null)
                    continue;
                string asset = meta.Substring(0, meta.Length - 5);
                var script = Regex.Match(File.Exists(asset) ? File.ReadAllText(asset) : "", @"m_Script: \{fileID: -?\d+, guid: (\w+)");
                if (script.Success && IsProjectScript(assets, script.Groups[1].Value))
                    decompiled.Add(guid);
            }
            if (decompiled.Count == 0)
                return false;
            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                string cleaned = PipelineReference.Replace(text, m => decompiled.Contains(m.Groups[2].Value) ? m.Groups[1].Value + ": {fileID: 0}" : m.Value);
                if (cleaned != text)
                    File.WriteAllText(file, cleaned);
            }
            return true;
        }

        private static bool IsProjectScript(string assets, string guid)
        {
            foreach (var meta in Directory.EnumerateFiles(assets, "*.meta", SearchOption.AllDirectories)
                         .Where(m => m.EndsWith(".cs.meta", StringComparison.OrdinalIgnoreCase) || m.EndsWith(".dll.meta", StringComparison.OrdinalIgnoreCase)))
                if (File.ReadAllText(meta).Contains("guid: " + guid))
                    return true;
            return false;
        }

        private static int RemoveIncompatibleLightingData(string assets)
        {
            int removed = 0;
            foreach (var asset in Directory.GetFiles(assets, "*.asset", SearchOption.AllDirectories))
            {
                bool lightingData = false;
                try
                {
                    using var reader = new StreamReader(asset);
                    for (int i = 0; i < 6 && !lightingData; i++)
                    {
                        string? line = reader.ReadLine();
                        if (line == null) break;
                        lightingData = line.StartsWith("LightingDataAsset:", StringComparison.Ordinal) || line.StartsWith("  LightingDataAsset:", StringComparison.Ordinal)
                                       || line.StartsWith("--- !u!1120 ", StringComparison.Ordinal);
                    }
                }
                catch (IOException) { continue; }
                if (!lightingData)
                    continue;
                string? guid = null;
                if (File.Exists(asset + ".meta"))
                {
                    var match = Regex.Match(File.ReadAllText(asset + ".meta"), @"^guid: (\w+)", RegexOptions.Multiline);
                    if (match.Success) guid = match.Groups[1].Value;
                }
                File.Delete(asset);
                if (File.Exists(asset + ".meta")) File.Delete(asset + ".meta");
                removed++;
                if (guid == null)
                    continue;
                foreach (var scene in Directory.GetFiles(assets, "*.unity", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(scene);
                    string cleaned = Regex.Replace(text, @"m_LightingDataAsset: \{fileID: \d+, guid: " + guid + @", type: \d+\}", "m_LightingDataAsset: {fileID: 0}");
                    if (!ReferenceEquals(cleaned, text) && cleaned != text)
                        File.WriteAllText(scene, cleaned);
                }
            }
            return removed;
        }

        private static int RestoreOriginalLibraries(string assets, string managed)
        {
            if (!Directory.Exists(managed))
                return 0;
            int restored = 0;
            foreach (var dll in Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(dll);
                string original = Path.Combine(managed, name);
                if (!File.Exists(original) || EngineLibraries.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var bytes = File.ReadAllBytes(original);
                if (bytes.AsSpan().SequenceEqual(File.ReadAllBytes(dll)))
                    continue;
                File.WriteAllBytes(dll, bytes);
                restored++;
            }
            return restored;
        }

        private static readonly string[] EngineLibraries = { "UnityEngine", "Unity.", "System", "Mono.", "mscorlib", "netstandard", "Microsoft." };

        private static readonly string[] EditorScriptingLibraries = { "Boo.Lang.dll", "UnityScript.Lang.dll" };

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

        public static bool IsManagedAssembly(string dll)
        {
            try
            {
                using var reader = new BinaryReader(File.OpenRead(dll));
                if (reader.BaseStream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
                    return false;
                reader.BaseStream.Position = 0x3C;
                long pe = reader.ReadInt32();
                if (pe <= 0 || pe + 24 > reader.BaseStream.Length)
                    return false;
                reader.BaseStream.Position = pe;
                if (reader.ReadUInt32() != 0x00004550)
                    return false;
                reader.BaseStream.Position = pe + 24;
                ushort magic = reader.ReadUInt16();
                long directories = pe + 24 + (magic == 0x20B ? 112 : 96);
                long cli = directories + 14 * 8;
                if (cli + 8 > reader.BaseStream.Length)
                    return false;
                reader.BaseStream.Position = cli;
                return reader.ReadUInt32() != 0 && reader.ReadUInt32() != 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
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

        public static ExportSettings RecommendedSettings(BuildReport report)
        {
            bool sameEditor = FindEditor(report.UnityVersion) != null;
            return new ExportSettings { KeepCompiledShaders = sameEditor, OriginalAssemblies = false };
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
            ThrowIfNotActivated(text, editor);
            if (text.Contains("Scripts have compiler errors"))
                throw new InvalidOperationException("The project has compile errors, so Unity could not run the repair. See " + log);
            throw new InvalidOperationException($"Unity stopped before the repair (exit code {process.ExitCode}). See {log}");
        }

        public static void ThrowIfNotActivated(string log, string editor)
        {
            if (log.Contains("has not been activated with a valid License") || log.Contains("No valid Unity Editor license found")
                || log.Contains("Failed to activate/update license"))
                throw new InvalidOperationException("This Unity version is not activated, so it cannot run in the background. Open it once " +
                                                    "(Game Spelunker window → Explore → Activate), sign in and choose a Personal license, close it, then try again. " +
                                                    "Editor: " + editor);
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

        public static void OpenInUnity(string exportFolder, string? scene = null)
        {
            PrepareForOpening(exportFolder, ToolsSource());
            string project = ProjectFolder(exportFolder);
            if (scene != null)
            {
                Directory.CreateDirectory(Path.Combine(project, "Library"));
                File.WriteAllText(Path.Combine(project, "Library", "GameSpelunker-open.txt"), scene);
            }
            string editor = EditorFor(exportFolder, EditorApplication.applicationPath);
            Process.Start(new ProcessStartInfo(editor, $"-projectPath \"{project}\"") { UseShellExecute = false });
        }
    }
}
