using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GameSpelunker
{
    public static class BuiltinShaders
    {
        private static readonly Regex NamePattern = new Regex("^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex HashPattern = new Regex(@"\b[0-9a-f]{12}\b", RegexOptions.Compiled);

        private static string RootFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSpelunker", "BuiltinShaders");

        private static string Folder(string version) => Path.Combine(RootFolder, version);

        public static bool IsCached(string? version) => version != null && Directory.Exists(Folder(version));

        public static async Task<bool> EnsureDownloadedAsync(string? version, CancellationToken token)
        {
            if (version == null)
                return false;
            if (IsCached(version))
                return true;
            string? changeset = ChangesetOf(version);
            if (changeset == null)
            {
                try
                {
                    changeset = await UnityEditorInstaller.ChangesetAsync(version, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is InvalidOperationException || ex is TaskCanceledException)
                {
                    return false;
                }
            }
            string url = $"https://download.unity3d.com/download_unity/{changeset}/builtin_shaders-{version}.zip";
            string staging = Folder(version) + ".partial";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                using (var archive = new ZipArchive(new MemoryStream(bytes)))
                    foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0 && (e.FullName.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                                                                                          || e.FullName.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase))))
                    {
                        string target = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase))
                            continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, true);
                    }
                if (Directory.Exists(Folder(version)))
                    Directory.Delete(Folder(version), true);
                Directory.Move(staging, Folder(version));
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is TaskCanceledException)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (IOException) { }
                return false;
            }
        }

        private static string? ChangesetOf(string version)
        {
            string? editor = ExtractedProject.FindEditor(version);
            if (editor == null || !File.Exists(editor))
                return null;
            var info = FileVersionInfo.GetVersionInfo(editor);
            var match = HashPattern.Match(info.ProductVersion ?? "");
            return match.Success ? match.Value : null;
        }

        public static Dictionary<string, string> Catalog(string? version)
        {
            var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
            if (version == null || !IsCached(version))
                return catalog;
            foreach (var file in Directory.EnumerateFiles(Folder(version), "*.shader", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }
                var name = NamePattern.Match(text);
                if (name.Success && !catalog.ContainsKey(name.Groups[1].Value))
                    catalog[name.Groups[1].Value] = text;
            }
            return catalog;
        }
    }
}
