using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameSpelunker
{
    public static class UnityEditorInstaller
    {
        private const string ReleaseApi = "https://services.api.unity.com/unity/editor/release/v1/releases?version=";

        public static string? HubExecutable
        {
            get
            {
                string[] candidates = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                    ? new[] { "/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" }
                    : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                        ? new[]
                        {
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe"),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Unity Hub", "Unity Hub.exe"),
                        }
                        : new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Unity Hub.AppImage") };
                foreach (var candidate in candidates)
                    if (File.Exists(candidate))
                        return candidate;
                return null;
            }
        }

        public static bool CanInstall => HubExecutable != null;

        public static bool NeedsLegacyActivation(string version)
        {
            if (ExtractedProject.IsAtLeast(version, 2020, 1))
                return false;
            string license = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                ? "/Library/Application Support/Unity/Unity_lic.ulf"
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Unity", "Unity_lic.ulf");
            return !File.Exists(license);
        }

        public static async Task<string> ChangesetAsync(string version, CancellationToken token)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string json = await http.GetStringAsync(ReleaseApi + Uri.EscapeDataString(version)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"" + Regex.Escape(version) + "\"[^{}]*?\"shortRevision\"\\s*:\\s*\"(\\w+)\"");
            if (!match.Success)
                match = Regex.Match(json, "\"shortRevision\"\\s*:\\s*\"(\\w+)\"");
            if (!match.Success)
                throw new InvalidOperationException($"Unity {version} is not in Unity's release list, so the Hub cannot install it.");
            return match.Groups[1].Value;
        }

        public static async Task<string> InstallAsync(string version, IProgress<string>? progress, CancellationToken token)
        {
            string hub = HubExecutable ?? throw new InvalidOperationException("Unity Hub is not installed. Install it from unity.com/download, then try again.");
            progress?.Report($"Looking up Unity {version}…");
            string changeset = await ChangesetAsync(version, token).ConfigureAwait(false);
            progress?.Report($"Unity Hub is downloading and installing Unity {version}… (a few GB, several minutes)");

            var info = new ProcessStartInfo(hub, $"-- --headless install --version {version} --changeset {changeset}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Unity Hub.");
            using var registration = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            });
            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                output.AppendLine(e.Data);
                var percent = Regex.Match(e.Data, @"(\d{1,3})\s*%");
                if (percent.Success)
                    progress?.Report($"Unity Hub is installing Unity {version}… {percent.Groups[1].Value}%");
            };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            while (!process.HasExited)
                await Task.Delay(1000).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            string? editor = ExtractedProject.FindEditor(version);
            if (editor == null)
                throw new InvalidOperationException($"Unity Hub did not install Unity {version}:\n" + Tail(output.ToString()));
            return editor;
        }

        public static void OpenOnceToActivate(string version)
        {
            string editor = ExtractedProject.FindEditor(version) ?? throw new InvalidOperationException($"Unity {version} is not installed.");
            string project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSpelunker", "Activate-" + version);
            Process.Start(new ProcessStartInfo(editor, Directory.Exists(project) ? $"-projectPath \"{project}\"" : $"-createProject \"{project}\"") { UseShellExecute = false });
        }

        private static string Tail(string text)
        {
            var lines = text.Trim().Split('\n');
            return string.Join("\n", lines, Math.Max(0, lines.Length - 6), Math.Min(6, lines.Length));
        }
    }
}
