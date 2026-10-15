using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GameSpelunker
{
    public sealed class CheckResult
    {
        public bool PlayStarted;
        public string Scenes = "";
        public int Errors;
        public List<(string message, int count)> TopErrors = new List<(string, int)>();
        public List<string> Screenshots = new List<string>();
        public string CodeFixes = "";
        public string Materials = "";
        public int PlaceholderShaders;
        public DateTime When;

        public string Verdict =>
            !PlayStarted ? "Play mode did not start"
            : Errors == 0 ? "Runs without errors"
            : $"Runs with {Errors} error(s)";
    }

    public static class ProjectChecker
    {
        public const string ReportFile = "GameSpelunker-check.txt";

        public static async Task<CheckResult> CheckAsync(string exportFolder, string editor, string toolsSource, CancellationToken token)
        {
            string project = ExtractedProject.ProjectFolder(exportFolder);
            string auxiliary = Path.Combine(exportFolder, "AuxiliaryFiles");
            Directory.CreateDirectory(auxiliary);
            foreach (var old in Directory.GetFiles(auxiliary, "GameSpelunker-check-*.png"))
                File.Delete(old);
            ExtractedProject.PrepareForOpening(exportFolder, toolsSource);

            string log = Path.Combine(auxiliary, "GameSpelunker-check.log");
            if (File.Exists(log)) File.Delete(log);
            var info = new ProcessStartInfo(editor, $"-batchmode -projectPath \"{project}\" -executeMethod GameSpelunker.ProjectCheck.RunBatch -logFile \"{log}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Unity."))
            {
                using var registration = token.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                });
                var started = DateTime.UtcNow;
                while (!process.HasExited)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    if (DateTime.UtcNow - started > TimeSpan.FromMinutes(60))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        throw new TimeoutException("Unity did not finish the check within an hour. See " + log);
                    }
                }
                token.ThrowIfCancellationRequested();
            }

            string text = ReadShared(log);
            ExtractedProject.ThrowIfNotActivated(text, editor);
            if (text.Contains("another Unity instance is running with this project open") || text.Contains("It looks like another Unity instance"))
                throw new InvalidOperationException("The project is open in Unity. Close it, then check again.");
            if (text.Contains("Scripts have compiler errors"))
                throw new InvalidOperationException("The project has compile errors, so Play mode cannot start. See " + log);

            var result = new CheckResult { When = DateTime.Now };
            int start = text.IndexOf("[Game Spelunker] Check started", StringComparison.Ordinal);
            result.PlayStarted = start >= 0 && text.Contains("playing=True");
            var scenes = Regex.Matches(text, @"\[Game Spelunker\] Check: .*? scenes=(.*?) cameras=").Cast<Match>().LastOrDefault();
            result.Scenes = scenes != null ? scenes.Groups[1].Value.Trim() : "";
            if (start >= 0)
            {
                var errors = ErrorMessages(text.Substring(start));
                result.Errors = errors.Count;
                result.TopErrors = errors.GroupBy(e => e).Select(g => (g.Key, g.Count())).OrderByDescending(g => g.Item2).Take(6).ToList();
            }
            result.Screenshots = Directory.GetFiles(auxiliary, "GameSpelunker-check-*.png").OrderBy(f => f).ToList();

            string fixes = Path.Combine(auxiliary, "GameSpelunker-code-fixes.txt");
            if (File.Exists(fixes))
                result.CodeFixes = File.ReadLines(fixes).FirstOrDefault() ?? "";
            string repaired = Path.Combine(project, "ProjectSettings", "GameSpelunker.txt");
            if (File.Exists(repaired))
                result.Materials = File.ReadLines(repaired).Skip(1).FirstOrDefault() ?? "";
            result.PlaceholderShaders = Directory.EnumerateFiles(Path.Combine(project, "Assets"), "*.shader", SearchOption.AllDirectories)
                .Count(f => ReadShared(f).Contains("DummyShaderTextExporter"));

            File.WriteAllText(Path.Combine(auxiliary, ReportFile), Describe(result));
            return result;
        }

        public static string Describe(CheckResult r)
        {
            var text = new StringBuilder();
            text.AppendLine($"Checked {r.When:yyyy-MM-dd HH:mm}: {r.Verdict}");
            if (r.Scenes.Length > 0) text.AppendLine("Scenes loaded in Play mode: " + r.Scenes);
            if (r.CodeFixes.Length > 0) text.AppendLine("Game code: " + r.CodeFixes);
            if (r.Materials.Length > 0) text.AppendLine("Materials: " + r.Materials);
            text.AppendLine($"Shaders still empty (not recoverable from the build): {r.PlaceholderShaders}");
            foreach (var (message, count) in r.TopErrors)
                text.AppendLine($"  x{count} {message}");
            return text.ToString();
        }

        private static List<string> ErrorMessages(string log)
        {
            var messages = new List<string>();
            var lines = log.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (Regex.IsMatch(line, @"^(\w+\.)*\w*Exception\b") || line.StartsWith("Error", StringComparison.Ordinal)
                    || line.Contains("The referenced script"))
                {
                    if (line.StartsWith("(Filename", StringComparison.Ordinal))
                        continue;
                    messages.Add(line.Length > 180 ? line.Substring(0, 180) : line);
                }
            }
            return messages;
        }

        private static string ReadShared(string path)
        {
            if (!File.Exists(path))
                return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
