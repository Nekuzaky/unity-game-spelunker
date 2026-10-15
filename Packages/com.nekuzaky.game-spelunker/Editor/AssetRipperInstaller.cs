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
    /// <summary>
    /// Finds or downloads AssetRipper (GPL-3.0, https://github.com/AssetRipper/AssetRipper). It is never bundled with
    /// Game Spelunker: the official release for this computer is downloaded into the user's local app data, either on
    /// the first extraction or when the user asks for it.
    /// </summary>
    public static class AssetRipperInstaller
    {
        /// <summary>The version whose HTTP API Game Spelunker was written against.</summary>
        public const string Version = "2.0.0";
        public const string ReleasePage = "https://github.com/AssetRipper/AssetRipper/releases/tag/" + Version;
        private const string CustomPathKey = "GameSpelunker.AssetRipperPath";

        private static readonly object InstallLock = new object();
        private static Task<string>? _install;

        /// <summary>The release archive for this OS and CPU, with its size in MB; null when AssetRipper has no build for it.</summary>
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

        /// <summary>True when AssetRipper can be downloaded automatically on this computer.</summary>
        public static bool CanAutoInstall => Release != null;

        /// <summary>Download size shown to the user, e.g. "44 MB".</summary>
        public static string DownloadSize => Release is { } r ? r.sizeMb + " MB" : "?";

        public static string ExecutableName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "AssetRipper.GUI.Free.exe" : "AssetRipper.GUI.Free";

        private static string RootFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSpelunker", "AssetRipper");

        public static string InstallFolder => Path.Combine(RootFolder, Version);

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

        /// <summary>The AssetRipper executable to use, or null when it is not installed yet. Main thread only (EditorPrefs).</summary>
        public static string? Executable => CustomExecutable ?? ManagedExecutable;

        /// <summary>The copy Game Spelunker downloaded, or null. Safe on any thread.</summary>
        private static string? ManagedExecutable
        {
            get
            {
                string installed = Path.Combine(InstallFolder, ExecutableName);
                return File.Exists(installed) ? installed : null;
            }
        }

        /// <summary>
        /// Returns <paramref name="executable"/> when there is one, otherwise the managed install, downloading the
        /// official release first when it is missing. Safe on any thread: read <see cref="Executable"/> on the main
        /// thread and pass it in. Concurrent callers share the same download. Progress is 0–1.
        /// </summary>
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

        /// <summary>Downloads and unpacks the official release for this computer. Progress is 0–1.</summary>
        private static async Task<string> InstallAsync(IProgress<float>? progress, CancellationToken token)
        {
            if (Release is not { } release)
                throw new PlatformNotSupportedException("AssetRipper has no build for this computer. Download it from " + ReleasePage + " and use \"Use existing…\".");

            // Unpack next to the final folder, then move it in place: a half-finished install never looks ready.
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
                    ExtractTarXz(archive, staging); // .NET cannot read xz; tar keeps the executable bit.

                if (!File.Exists(Path.Combine(staging, ExecutableName)))
                    throw new FileNotFoundException("The downloaded archive does not contain " + ExecutableName);

                // The final folder may already hold AssetRipper's logs from an install that was removed since.
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
