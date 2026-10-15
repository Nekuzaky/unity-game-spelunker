using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameSpelunker
{
    public enum ScriptingBackend
    {
        Unknown,
        Mono,
        IL2CPP,
    }

    public sealed class BuildReport
    {
        public string GameFolder = "";
        public string DataFolder = "";
        public string? Executable;
        public string Name = "";
        public string? Company;
        public string? UnityVersion;
        public ScriptingBackend Backend;
        public string RenderPipeline = "Built-in";
        public bool CompressedData;
        public int SceneCount;
        public long TotalSize;
        public long StreamingAssetsSize;
        public int AssetBundleCount;
        public List<string> GameAssemblies = new List<string>();
        public List<string> NativePlugins = new List<string>();
        public List<string> ModLoaders = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class BuildInspector
    {
        private static readonly Regex VersionPattern = new Regex(@"(?<!\d)(?:20\d\d|6\d{3}|[345])\.\d+\.\d+[abfpx]\d+", RegexOptions.Compiled);

        private static readonly string[] EnginePrefixes =
        {
            "UnityEngine", "Unity.", "System", "Mono.", "mscorlib", "netstandard", "Microsoft.", "nunit", "Newtonsoft.Json",
        };

        public static string? FindDataFolder(string path)
        {
            if (File.Exists(path))
                path = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(path))
                return null;

            if (IsDataFolder(path))
                return path;

            var candidates = Directory.GetDirectories(path, "*_Data").Where(IsDataFolder).ToList();
            if (candidates.Count > 0)
                return candidates.OrderBy(c => c.Length).First();

            var mac = Path.Combine(path, "Contents", "Resources", "Data");
            return IsDataFolder(mac) ? mac : null;
        }

        private static bool IsDataFolder(string path) =>
            Directory.Exists(path) &&
            (File.Exists(Path.Combine(path, "globalgamemanagers")) || File.Exists(Path.Combine(path, "data.unity3d"))
             || File.Exists(Path.Combine(path, "mainData")));

        public static BuildReport Inspect(string path)
        {
            string data = FindDataFolder(path) ?? throw new InvalidOperationException("This is not a Unity game: no *_Data folder with globalgamemanagers or data.unity3d.");
            string game = Path.GetDirectoryName(data)!;
            var report = new BuildReport { GameFolder = game, DataFolder = data };

            string exeName = Path.GetFileName(data).Replace("_Data", "");
            report.Executable = new[] { exeName + ".exe", exeName + ".x86_64", exeName }
                .Select(n => Path.Combine(game, n)).FirstOrDefault(File.Exists);
            report.Name = exeName;

            ReadAppInfo(report);
            report.UnityVersion = ReadUnityVersion(report);
            DetectBackend(report);
            DetectContent(report);
            DetectModLoaders(report);

            try
            {
                report.TotalSize = new DirectoryInfo(game).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return report;
        }

        private static void ReadAppInfo(BuildReport report)
        {
            string appInfo = Path.Combine(report.DataFolder, "app.info");
            if (!File.Exists(appInfo))
                return;
            var lines = File.ReadAllLines(appInfo).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            if (lines.Length >= 1) report.Company = lines[0];
            if (lines.Length >= 2) report.Name = lines[1];
        }

        private static string? ReadUnityVersion(BuildReport report)
        {
            foreach (var file in new[] { "globalgamemanagers", "data.unity3d", "mainData", "level0" })
            {
                string path = Path.Combine(report.DataFolder, file);
                if (!File.Exists(path))
                    continue;
                var head = new byte[512];
                using (var fs = File.OpenRead(path))
                    fs.Read(head, 0, head.Length);
                var match = VersionPattern.Match(Encoding.ASCII.GetString(head));
                if (match.Success)
                    return match.Value;
            }

            string player = Path.Combine(report.GameFolder, "UnityPlayer.dll");
            if (File.Exists(player))
            {
                var info = FileVersionInfo.GetVersionInfo(player);
                var match = VersionPattern.Match(info.ProductVersion ?? "");
                if (match.Success)
                    return match.Value;
            }
            return null;
        }

        private static void DetectBackend(BuildReport report)
        {
            string managed = Path.Combine(report.DataFolder, "Managed");
            bool hasIl2CppData = Directory.Exists(Path.Combine(report.DataFolder, "il2cpp_data"));
            bool hasGameAssembly = File.Exists(Path.Combine(report.GameFolder, "GameAssembly.dll"))
                                   || File.Exists(Path.Combine(report.GameFolder, "GameAssembly.so"));

            if (hasIl2CppData || hasGameAssembly)
            {
                report.Backend = ScriptingBackend.IL2CPP;
                report.Warnings.Add("IL2CPP build: game code is compiled to native code. AssetRipper recovers classes, fields and " +
                                    "method signatures, but method bodies are empty.");
            }
            else if (Directory.Exists(managed))
            {
                report.Backend = ScriptingBackend.Mono;
            }

            if (Directory.Exists(managed))
            {
                foreach (var dll in Directory.GetFiles(managed, "*.dll"))
                {
                    string name = Path.GetFileName(dll);
                    if (!EnginePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        report.GameAssemblies.Add(name);
                    if (name.StartsWith("Unity.RenderPipelines.Universal", StringComparison.Ordinal))
                        report.RenderPipeline = "URP (Universal)";
                    else if (name.StartsWith("Unity.RenderPipelines.HighDefinition", StringComparison.Ordinal))
                        report.RenderPipeline = "HDRP (High Definition)";
                }
            }
            report.GameAssemblies.Sort(StringComparer.OrdinalIgnoreCase);

            string plugins = Path.Combine(report.DataFolder, "Plugins");
            if (Directory.Exists(plugins))
                report.NativePlugins.AddRange(Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories)
                    .Select(Path.GetFileName).Where(n => n != null && !n.StartsWith("lib_burst_generated", StringComparison.Ordinal))!);
        }

        private static void DetectContent(BuildReport report)
        {
            report.CompressedData = File.Exists(Path.Combine(report.DataFolder, "data.unity3d"));
            report.SceneCount = Directory.GetFiles(report.DataFolder, "level*")
                .Count(f => Regex.IsMatch(Path.GetFileName(f), @"^level\d+$"));
            if (report.CompressedData)
                report.Warnings.Add("Compressed build (data.unity3d): scene count is only known after loading.");

            string streaming = Path.Combine(report.DataFolder, "StreamingAssets");
            if (Directory.Exists(streaming))
            {
                var files = new DirectoryInfo(streaming).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                report.StreamingAssetsSize = files.Sum(f => f.Length);
                report.AssetBundleCount = files.Count(f => f.Extension == ".bundle" || f.Extension == ".assetbundle" || f.Extension == ""
                                                           && IsAssetBundle(f.FullName));
            }
        }

        private static bool IsAssetBundle(string path)
        {
            try
            {
                var head = new byte[8];
                using var fs = File.OpenRead(path);
                return fs.Read(head, 0, 8) == 8 && Encoding.ASCII.GetString(head).StartsWith("UnityFS", StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void DetectModLoaders(BuildReport report)
        {
            string g = report.GameFolder;
            if (Directory.Exists(Path.Combine(g, "BepInEx"))) report.ModLoaders.Add("BepInEx");
            if (Directory.Exists(Path.Combine(g, "MelonLoader"))) report.ModLoaders.Add("MelonLoader");
            if (File.Exists(Path.Combine(g, "doorstop_config.ini"))) report.ModLoaders.Add("Unity Doorstop");
            if (Directory.Exists(Path.Combine(g, "Mods")) || Directory.Exists(Path.Combine(report.DataFolder, "Mods")))
                report.ModLoaders.Add("Mods folder");
            if (Directory.Exists(Path.Combine(report.DataFolder, "StreamingAssets", "Mods")))
                report.ModLoaders.Add("StreamingAssets/Mods");
        }

        public static string FormatSize(long bytes) =>
            bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB"
            : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.#} MB"
            : $"{bytes / 1024.0:0} KB";
    }
}
