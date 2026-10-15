using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GameSpelunker
{
    public enum ExportMode
    {
        /// <summary>A Unity project you can open: scenes, prefabs, materials, decompiled scripts.</summary>
        UnityProject,
        /// <summary>Plain files: images, models, sounds, text, without a project around them.</summary>
        PrimaryContent,
    }

    /// <summary>
    /// Runs AssetRipper headless on a local port and drives it through its HTTP API:
    /// POST /LoadFolder (Path) then POST /Export/UnityProject or /Export/PrimaryContent (Path).
    /// Each request returns once the work is done. Output lines are forwarded through <see cref="Log"/>.
    /// </summary>
    public sealed class AssetRipperSession : IDisposable
    {
        private static readonly Regex ProgressPattern = new Regex(@"^(\w+Progress)\s*:\s*\((\d+)/(\d+)\)\s*(.*)$", RegexOptions.Compiled);

        private readonly string _executable;
        private Process? _process;
        private HttpClient? _http;
        private int _port;

        /// <summary>Every line AssetRipper prints. Raised on a worker thread.</summary>
        public event Action<string>? Log;
        /// <summary>(phase, done, total, item), parsed from AssetRipper's progress lines. Raised on a worker thread.</summary>
        public event Action<string, int, int, string>? Progress;

        public AssetRipperSession(string executable)
        {
            _executable = executable;
        }

        public bool IsRunning => _process is { HasExited: false };

        public async Task StartAsync(CancellationToken token)
        {
            _port = FreePort();
            var info = new ProcessStartInfo(_executable, $"--port {_port} --headless")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(_executable)!,
            };
            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => OnLine(e.Data);
            _process.ErrorDataReceived += (_, e) => OnLine(e.Data);
            if (!_process.Start())
                throw new InvalidOperationException("Could not start AssetRipper.");
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{_port}/"),
                Timeout = TimeSpan.FromHours(2),
            };

            // Wait until the local server answers.
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (_process.HasExited)
                    throw new InvalidOperationException($"AssetRipper stopped while starting (exit code {_process.ExitCode}).");
                try
                {
                    using var response = await _http.GetAsync("", token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return;
                }
                catch (HttpRequestException)
                {
                }
                await Task.Delay(300, token).ConfigureAwait(false);
            }
            throw new TimeoutException("AssetRipper did not start within 60 seconds.");
        }

        /// <summary>Loads a game: the folder that contains the executable, or its *_Data folder.</summary>
        public Task LoadAsync(string path, CancellationToken token) => PostPath("LoadFolder", path, token);

        public Task ExportAsync(string outputFolder, ExportMode mode, CancellationToken token) =>
            PostPath(mode == ExportMode.UnityProject ? "Export/UnityProject" : "Export/PrimaryContent", outputFolder, token);

        public Task ResetAsync(CancellationToken token) => PostPath("Reset", null, token);

        private async Task PostPath(string route, string? path, CancellationToken token)
        {
            if (_http == null)
                throw new InvalidOperationException("Call StartAsync first.");
            var fields = new List<KeyValuePair<string, string>>();
            if (path != null)
                fields.Add(new KeyValuePair<string, string>("Path", path));

            using var content = new FormUrlEncodedContent(fields);
            using var response = await _http.PostAsync(route, content, token).ConfigureAwait(false);
            // AssetRipper answers each command with a redirect to its home page once the work is finished.
            if (response.StatusCode != HttpStatusCode.Redirect && response.StatusCode != HttpStatusCode.Found && !response.IsSuccessStatusCode)
                throw new HttpRequestException($"AssetRipper /{route} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        private void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            Log?.Invoke(line!);
            var m = ProgressPattern.Match(line!.Trim());
            if (m.Success)
                Progress?.Invoke(m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value);
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _http?.Dispose();
            _http = null;
            try
            {
                if (_process is { HasExited: false })
                    _process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            _process?.Dispose();
            _process = null;
        }
    }
}
