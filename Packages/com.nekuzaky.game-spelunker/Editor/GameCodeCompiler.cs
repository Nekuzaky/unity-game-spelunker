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
    public sealed class GameCodeResult
    {
        public bool Compiles;
        public int Rounds;
        public int MembersStubbed;
        public int MembersRenamed;
        public int FilesQuarantined;
        public int RemainingErrors;
        public string Log = "";
    }

    public static class GameCodeCompiler
    {
        public const string StubMessage = "Game Spelunker: this member did not compile after decompilation";
        public static int Passes = 12;

        private static int MaxRounds =>
            int.TryParse(Environment.GetEnvironmentVariable("GAME_SPELUNKER_COMPILE_PASSES"), out var passes) && passes > 0 ? passes : Math.Max(1, Passes);
        private static readonly Regex ErrorLine = new Regex(@"^(Assets[\\/][^(]+\.cs)\((\d+),\d+\): error (CS\d+)", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex Control = new Regex(@"^\s*(?:else\s+)?(?:if|for|foreach|while|switch|catch|using|lock|fixed|do|try|finally|else|checked|unchecked|unsafe)\b",
            RegexOptions.Compiled);

        public static async Task<GameCodeResult> MakeItCompileAsync(string exportFolder, string editor, IProgress<string>? progress, CancellationToken token)
        {
            var result = new GameCodeResult();
            string project = ExtractedProject.ProjectFolder(exportFolder);
            string settings = Path.Combine(project, "ProjectSettings", "ProjectSettings.asset");
            string original = File.ReadAllText(settings);
            string log = Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-compile.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            var report = new StringBuilder();
            try
            {
                File.WriteAllText(settings, WithDefine(original, ExtractedProject.GameCodeDefine));
                for (int round = 1; round <= MaxRounds; round++)
                {
                    token.ThrowIfCancellationRequested();
                    result.Rounds = round;
                    progress?.Report($"Compiling the game's code (pass {round})…");
                    await RunUnityAsync(editor, project, log, token).ConfigureAwait(false);
                    ExtractedProject.ThrowIfNotActivated(ReadShared(log), editor);
                    var errors = ReadErrors(log);
                    File.WriteAllLines(Path.Combine(Path.GetDirectoryName(log)!, $"GameSpelunker-compile-pass{round}.txt"),
                        ReadShared(log).Split('\n').Where(l => l.Contains("error CS")).Select(l => l.Trim()).Distinct());
                    result.RemainingErrors = errors.Sum(e => e.Value.Count);
                    if (errors.Count == 0)
                    {
                        var global = ReadShared(log).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("error CS")).Distinct().ToList();
                        result.Compiles = global.Count == 0;
                        result.RemainingErrors = global.Count;
                        if (global.Count > 0)
                            report.AppendLine("Errors not tied to a script:\n" + string.Join("\n", global));
                        break;
                    }
                    progress?.Report($"Neutralising what does not compile (pass {round}, {result.RemainingErrors} error(s))…");
                    if (!errors.Keys.Any(IsGameCode))
                        break;
                    var renamed = RenameMembersNamedLikeTheirType(project, ReadShared(log));
                    if (renamed.Count > 0)
                    {
                        result.MembersRenamed += renamed.Count;
                        foreach (var r in renamed) report.AppendLine(r);
                        continue;
                    }
                    foreach (var file in errors.Where(e => IsGameCode(e.Key)))
                    {
                        string path = Path.Combine(project, file.Key);
                        if (!File.Exists(path))
                            continue;
                        int stubbed = StubMembers(path, file.Value);
                        if (stubbed > 0)
                        {
                            result.MembersStubbed += stubbed;
                            report.AppendLine($"{file.Key}: {stubbed} member(s) stubbed");
                        }
                        else
                        {
                            Quarantine(path);
                            result.FilesQuarantined++;
                            report.AppendLine($"{file.Key}: quarantined");
                        }
                    }
                }
            }
            finally
            {
                bool switchable = Directory.EnumerateFiles(Path.Combine(project, "Assets"), "Spelunked.*.asmdef", SearchOption.AllDirectories).Any();
                File.WriteAllText(settings, result.Compiles && switchable ? WithDefine(original, ExtractedProject.GameCodeDefine) : original);
                result.Log = report.ToString();
                File.WriteAllText(Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-code-fixes.txt"),
                    $"Compiles: {result.Compiles} after {result.Rounds} pass(es); {result.MembersRenamed} member(s) renamed, {result.MembersStubbed} member(s) stubbed, " +
                    $"{result.FilesQuarantined} file(s) quarantined.\n\n" + result.Log);
            }
            return result;
        }

        private static string WithDefine(string settings, string define)
        {
            if (settings.Contains(define))
                return settings;
            if (Regex.IsMatch(settings, @"^  scriptingDefineSymbols: \{\}", RegexOptions.Multiline))
                return Regex.Replace(settings, @"^  scriptingDefineSymbols: \{\}", "  scriptingDefineSymbols:\n    Standalone: " + define, RegexOptions.Multiline);
            if (Regex.IsMatch(settings, @"^    Standalone: ", RegexOptions.Multiline))
                return Regex.Replace(settings, @"^(    Standalone: )(.*)$", m => m.Groups[1].Value + (m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value + ";" : "") + define,
                    RegexOptions.Multiline);
            return Regex.Replace(settings, @"^  scriptingDefineSymbols:\s*$", "  scriptingDefineSymbols:\n    Standalone: " + define, RegexOptions.Multiline);
        }

        private static async Task RunUnityAsync(string editor, string project, string log, CancellationToken token)
        {
            if (File.Exists(log))
                File.Delete(log);
            var info = new ProcessStartInfo(editor, $"-batchmode -quit -projectPath \"{project}\" -logFile \"{log}\"") { UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Unity.");
            using var registration = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            });
            while (!process.HasExited)
                await Task.Delay(1000).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }

        private static string ReadShared(string path)
        {
            if (!File.Exists(path))
                return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static bool IsGameCode(string file) =>
            file.StartsWith("Assets/Scripts/", StringComparison.OrdinalIgnoreCase) || file.StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase);

        private static readonly Regex SameNameAsType = new Regex(@"^(Assets[\\/][^(]+\.cs)\((\d+),\d+\): error CS0542", RegexOptions.Compiled | RegexOptions.Multiline);

        private static List<string> RenameMembersNamedLikeTheirType(string project, string log)
        {
            var done = new List<string>();
            var gameFiles = new[] { "Scripts", "Plugins" }.Select(f => Path.Combine(project, "Assets", f)).Where(Directory.Exists)
                .SelectMany(f => Directory.GetFiles(f, "*.cs", SearchOption.AllDirectories)).ToList();
            foreach (var group in SameNameAsType.Matches(log).Cast<Match>()
                         .Select(m => (file: m.Groups[1].Value.Replace('\\', '/'), line: int.Parse(m.Groups[2].Value)))
                         .Where(e => IsGameCode(e.file)).Distinct().GroupBy(e => e.file))
            {
                string path = Path.Combine(project, group.Key);
                if (!File.Exists(path))
                    continue;
                var lines = File.ReadAllLines(path);
                string whole = string.Join("\n", lines);
                var names = new List<string>();
                foreach (var (_, line) in group)
                {
                    if (line < 1 || line > lines.Length)
                        continue;
                    string text = lines[line - 1];
                    var name = Regex.Matches(text, @"\b[A-Za-z_]\w*\b").Cast<Match>().Select(m => m.Value)
                        .FirstOrDefault(id => Regex.IsMatch(whole, @"\b(class|struct)\s+" + id + @"\b")
                                              && Regex.IsMatch(text, @"(?<!new\s+)(?<!class\s+)(?<!struct\s+)\b" + id + @"\b(?=\s*[\(;={])"));
                    if (name == null)
                        continue;
                    lines[line - 1] = Regex.Replace(text, @"(?<!new\s+)(?<!class\s+)(?<!struct\s+)\b" + name + @"\b(?=\s*[\(;={])", name + "_");
                    names.Add(name);
                }
                if (names.Count == 0)
                    continue;
                string updated = string.Join("\n", lines);
                foreach (var name in names.Distinct())
                {
                    updated = Regex.Replace(updated, @"\.\s*" + name + @"\b", "." + name + "_");
                    string declaration = @"\b[\w<>\[\],\.]+\s+" + name + @"\s*[;=\({]";
                    bool declaredElsewhere = gameFiles.Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                        .Any(f => Regex.IsMatch(File.ReadAllText(f), declaration));
                    if (!declaredElsewhere)
                        foreach (var other in gameFiles.Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                        {
                            string source = File.ReadAllText(other);
                            string renamed = Regex.Replace(source, @"\.\s*" + name + @"\b", "." + name + "_");
                            if (renamed != source) File.WriteAllText(other, renamed);
                        }
                    done.Add($"{group.Key}: member {name} renamed to {name}_" + (declaredElsewhere ? " (callers in other files left as they are)" : ""));
                }
                File.WriteAllText(path, updated);
            }
            return done;
        }

        private static Dictionary<string, List<int>> ReadErrors(string log)
        {
            var errors = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            string text = ReadShared(log);
            foreach (Match m in ErrorLine.Matches(text))
            {
                string file = m.Groups[1].Value.Replace('\\', '/');
                if (!errors.TryGetValue(file, out var lines))
                    errors[file] = lines = new List<int>();
                int line = int.Parse(m.Groups[2].Value);
                if (!lines.Contains(line))
                    lines.Add(line);
            }
            return errors;
        }

        public static int StubMembers(string path, IEnumerable<int> errorLines)
        {
            string text = File.ReadAllText(path);
            var blocks = Blocks(text);
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') lineStarts.Add(i + 1);

            var targets = new List<(int open, int close)>();
            foreach (int line in errorLines)
            {
                if (line < 1 || line > lineStarts.Count)
                    return 0;
                int position = lineStarts[line - 1];
                var member = blocks.Where(b => b.open < position && b.close > position && IsMemberBody(text, b.open))
                    .OrderBy(b => b.open).FirstOrDefault();
                if (member.close == 0)
                    return 0;
                if (!targets.Contains(member))
                    targets.Add(member);
            }

            var outermost = targets.Where(t => !targets.Any(o => o != t && o.open < t.open && o.close > t.close)).OrderByDescending(t => t.open).ToList();
            foreach (var (open, close) in outermost)
            {
                string body = IsVoidLike(text, open)
                    ? $" UnityEngine.Debug.LogWarning(\"{StubMessage}\"); "
                    : $" throw new System.NotImplementedException(\"{StubMessage}\"); ";
                text = text.Substring(0, open + 1) + body + text.Substring(close);
            }
            File.WriteAllText(path, text);
            return outermost.Count;
        }

        private static readonly Regex Modifiers = new Regex(
            @"\b(public|private|protected|internal|static|extern|unsafe|new|override|virtual|abstract|sealed|async|partial|readonly)\b", RegexOptions.Compiled);

        private static bool IsVoidLike(string text, int open)
        {
            string head = Regex.Replace(DeclarationBefore(text, open), @"^\s*(\[[^\]]*\]\s*)+", "");
            if (Regex.IsMatch(head, @"^(set|add|remove)$"))
                return true;
            string beforeParen = Modifiers.Replace(head.Split('(')[0], "").Trim();
            return Regex.IsMatch(beforeParen, @"^void\s") || (head.Contains("(") && Regex.IsMatch(beforeParen, @"^[\w\.]+$"));
        }

        private static string DeclarationBefore(string text, int open)
        {
            int start = open - 1;
            while (start > 0 && text[start] != ';' && text[start] != '{' && text[start] != '}')
                start--;
            return text.Substring(start + 1, open - start - 1).Trim();
        }

        private static bool IsMemberBody(string text, int open)
        {
            string head = DeclarationBefore(text, open);
            head = Regex.Replace(head, @"^\s*(\[[^\]]*\]\s*)+", "");
            if (head.Length == 0 || Control.IsMatch(head) || head.EndsWith("=>") || head.EndsWith("="))
                return false;
            if (Regex.IsMatch(head, @"^(get|set|add|remove)$"))
                return true;
            if (Regex.IsMatch(head, @"\b(class|struct|interface|enum|namespace|record)\b"))
                return false;
            return Regex.IsMatch(head, @"\w\s*(<[^>]*>)?\s*\([^;]*\)\s*(where\s+.*|:\s*(base|this)\s*\(.*\))?$", RegexOptions.Singleline)
                   && !Regex.IsMatch(head, @"\bnew\s+[\w\.<>]+\s*\(");
        }

        private static List<(int open, int close)> Blocks(string text)
        {
            var blocks = new List<(int, int)>();
            var stack = new Stack<int>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = end < 0 ? text.Length : end + 2; continue; }
                if (c == '"' || (c == '@' && i + 1 < text.Length && text[i + 1] == '"') || (c == '$' && i + 1 < text.Length && (text[i + 1] == '"' || text[i + 1] == '@')))
                {
                    bool verbatim = false;
                    while (text[i] != '"') { if (text[i] == '@') verbatim = true; i++; }
                    i++;
                    while (i < text.Length)
                    {
                        if (verbatim && i + 1 < text.Length && text[i] == '"' && text[i + 1] == '"') { i += 2; continue; }
                        if (!verbatim && text[i] == '\\') { i += 2; continue; }
                        if (text[i] == '"') { i++; break; }
                        i++;
                    }
                    continue;
                }
                if (c == '\'')
                {
                    var m = Regex.Match(text.Substring(i, Math.Min(8, text.Length - i)), @"^'(\\.[^']*|[^\\'])'");
                    if (m.Success) { i += m.Length; continue; }
                }
                if (c == '{') stack.Push(i);
                else if (c == '}' && stack.Count > 0) blocks.Add((stack.Pop(), i));
                i++;
            }
            return blocks;
        }

        private static void Quarantine(string path)
        {
            string text = File.ReadAllText(path);
            if (text.StartsWith("#if SPELUNKER_QUARANTINED", StringComparison.Ordinal))
                return;
            File.WriteAllText(path, "#if SPELUNKER_QUARANTINED\n" + text + "\n#endif\n");
        }
    }
}
