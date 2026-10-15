using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace GameSpelunker
{
    /// <summary>
    /// Finds or downloads AssetRipper (GPL-3.0, https://github.com/AssetRipper/AssetRipper). It is never bundled with
    /// Game Spelunker: the official release is downloaded on request into the user's local app data.
    /// </summary>
    public static class AssetRipperInstaller
    {
        /// <summary>The version whose HTTP API Game Spelunker was written against.</summary>
        public const string Version = "2.0.0";
        public const string ReleasePage = "https://github.com/AssetRipper/AssetRipper/releases/tag/" + Version;
        public const string DownloadUrl = "https://github.com/AssetRipper/AssetRipper/releases/download/" + Version + "/AssetRipper_win_x64.zip";
        public const string DownloadSize = "44 MB";
        private const string CustomPathKey = "GameSpelunker.AssetRipperPath";
        private const string ExecutableName = "AssetRipper.GUI.Free.exe";

        public static string InstallFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSpelunker", "AssetRipper", Version);

        /// <summary>A path chosen by the user (an AssetRipper they already have), or null.</summary>
        public static string? CustomExecutable
        {
            get
            {
                string path = EditorPrefs.GetString(CustomPathKey, "");
                return path.Length > 0 && File.Exists(path) ? path : null;
            }
            set => EditorPrefs.SetString(CustomPathKey, value ?? "");
        }

        /// <summary>The AssetRipper executable to use, or null when it is not installed yet.</summary>
        public static string? Executable
        {
            get
            {
                if (CustomExecutable is { } custom)
                    return custom;
                string installed = Path.Combine(InstallFolder, ExecutableName);
                return File.Exists(installed) ? installed : null;
            }
        }

        public static bool IsSupportedPlatform => Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>Downloads and unpacks the official Windows release. Progress is 0–1.</summary>
        public static async Task<string> InstallAsync(IProgress<float>? progress, CancellationToken token)
        {
            if (!IsSupportedPlatform)
                throw new PlatformNotSupportedException("Automatic install is Windows-only for now. Download AssetRipper from " + ReleasePage + " and pick its executable.");

            Directory.CreateDirectory(InstallFolder);
            string zip = Path.Combine(InstallFolder, "download.zip");

            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                        progress?.Report(done / (float)total);
                }
            }

            ZipFile.ExtractToDirectory(zip, InstallFolder);
            File.Delete(zip);

            string exe = Path.Combine(InstallFolder, ExecutableName);
            if (!File.Exists(exe))
                throw new FileNotFoundException("The downloaded archive does not contain " + ExecutableName);
            return exe;
        }
    }
}
