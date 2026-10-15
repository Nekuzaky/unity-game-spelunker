using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace GameSpelunker
{
    public static class AssetRipperInstaller
    {
        public const string Version = "2.0.0";
        public const string ReleasePage = "https://github.com/AssetRipper/AssetRipper/releases/tag/" + Version;
        private const string CustomPathKey = "GameSpelunker.AssetRipperPath";

        private static readonly object InstallLock = new object();
        private static Task<string>? _install;

        private static (string asset, int sizeMb)? Release
        {
            get
            {
                bool arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
                bool x64 = RuntimeInformation.OSArchitecture == Architecture.X64;
                if (!arm && !x64) return null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return arm ? ("AssetRipper_win_arm64.zip", 42) : ("AssetRipper_win_x64.zip", 44);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return arm ? ("AssetRipper_mac_arm64.tar.xz", 27) : ("AssetRipper_mac_x64.tar.xz", 32);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return arm ? ("AssetRipper_linux_arm64.tar.xz", 27) : ("AssetRipper_linux_x64.tar.xz", 31);
                return null;
            }
        }

        public static bool CanAutoInstall => Release != null;

        public static string DownloadSize => Release is { } r ? r.sizeMb + " MB" : "?";

        public static string ExecutableName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "AssetRipper.GUI.Free.exe" : "AssetRipper.GUI.Free";

        private static string RootFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSpelunker", "AssetRipper");

        public static string InstallFolder => Path.Combine(RootFolder, Version);

        public static string? CustomExecutable
        {
            get
            {
                string path = EditorPrefs.GetString(CustomPathKey, "");
                return path.Length > 0 && File.Exists(path) ? path : null;
            }
            set => EditorPrefs.SetString(CustomPathKey, value ?? "");
        }

        public static string? Executable => CustomExecutable ?? ManagedExecutable;

        private static string? ManagedExecutable
        {
            get
            {
                string installed = Path.Combine(InstallFolder, ExecutableName);
                return File.Exists(installed) ? installed : null;
            }
        }

        public static Task<string> EnsureInstalledAsync(string? executable, IProgress<float>? progress, CancellationToken token)
        {
            if ((executable ?? ManagedExecutable) is { } exe)
                return Task.FromResult(exe);
            lock (InstallLock)
            {
                if (_install == null || _install.IsCompleted)
                    _install = InstallAsync(progress, token);
                return _install;
            }
        }

        private static async Task<string> InstallAsync(IProgress<float>? progress, CancellationToken token)
        {
            if (Release is not { } release)
                throw new PlatformNotSupportedException("AssetRipper has no build for this computer. Download it from " + ReleasePage + " and use \"Use existing…\".");

            Directory.CreateDirectory(RootFolder);
            string archive = Path.Combine(RootFolder, release.asset);
            string staging = InstallFolder + ".partial";
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);

            try
            {
                string url = $"https://github.com/AssetRipper/AssetRipper/releases/download/{Version}/{release.asset}";
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? -1;
                    using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var output = File.Create(archive);
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

                Directory.CreateDirectory(staging);
                if (archive.EndsWith(".zip", StringComparison.Ordinal))
                    ZipFile.ExtractToDirectory(archive, staging);
                else
                    ExtractTarXz(archive, staging);

                if (!File.Exists(Path.Combine(staging, ExecutableName)))
                    throw new FileNotFoundException("The downloaded archive does not contain " + ExecutableName);

                if (Directory.Exists(InstallFolder))
                    Directory.Delete(InstallFolder, true);
                Directory.Move(staging, InstallFolder);
                return Path.Combine(InstallFolder, ExecutableName);
            }
            finally
            {
                if (File.Exists(archive))
                    File.Delete(archive);
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
        }

        private static void ExtractTarXz(string archive, string folder)
        {
            var info = new ProcessStartInfo("tar", $"-xJf \"{archive}\" -C \"{folder}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start tar.");
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"tar could not unpack {Path.GetFileName(archive)}: {error.Trim()}");
        }
    }
}
