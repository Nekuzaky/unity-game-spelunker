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
        public int FastPasses;
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
            string? projectVersion = ExtractedProject.UnityVersionOf(exportFolder);
            string log = Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-compile.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            var report = new StringBuilder();
            try
            {
                File.WriteAllText(settings, WithDefine(original, ExtractedProject.GameCodeDefine, projectVersion));
                await FastPassesAsync(project, editor, result, report, progress, token).ConfigureAwait(false);
                for (int round = 1; round <= MaxRounds; round++)
                {
                    token.ThrowIfCancellationRequested();
                    result.Rounds = round;
                    progress?.Report($"Compiling the game's code in Unity (pass {round})…");
                    await RunUnityAsync(editor, project, log, token).ConfigureAwait(false);
                    string text = ReadShared(log);
                    ExtractedProject.ThrowIfNotActivated(text, editor);
                    var errors = ParseErrors(text);
                    File.WriteAllLines(Path.Combine(Path.GetDirectoryName(log)!, $"GameSpelunker-compile-pass{round}.txt"),
                        text.Split('\n').Where(l => l.Contains("error CS")).Select(l => l.Trim()).Distinct());
                    result.RemainingErrors = errors.Sum(e => e.Value.Count);
                    if (errors.Count == 0)
                    {
                        var global = text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("error CS")).Distinct().ToList();
                        result.Compiles = global.Count == 0;
                        result.RemainingErrors = global.Count;
                        if (global.Count > 0)
                            report.AppendLine("Errors not tied to a script:\n" + string.Join("\n", global));
                        break;
                    }
                    progress?.Report($"Neutralising what does not compile (pass {round}, {result.RemainingErrors} error(s))…");
                    if (!ApplyFixes(project, text, errors, result, report))
                        break;
                }
            }
            finally
            {
                bool switchable = Directory.EnumerateFiles(Path.Combine(project, "Assets"), "Spelunked.*.asmdef", SearchOption.AllDirectories).Any();
                File.WriteAllText(settings, result.Compiles && switchable ? WithDefine(original, ExtractedProject.GameCodeDefine, projectVersion) : original);
                result.Log = report.ToString();
                File.WriteAllText(Path.Combine(exportFolder, "AuxiliaryFiles", "GameSpelunker-code-fixes.txt"),
                    $"Compiles: {result.Compiles} after {result.Rounds} pass(es); {result.MembersRenamed} member(s) renamed, {result.MembersStubbed} member(s) stubbed, " +
                    $"{result.FilesQuarantined} file(s) quarantined.\n\n" + result.Log);
            }
            return result;
        }

        private sealed class CompileUnit
        {
            public string Name = "";
            public string Root = "";
            public readonly List<string> References = new List<string>();
            public readonly List<string> Sources = new List<string>();
        }

        private static async Task FastPassesAsync(string project, string editor, GameCodeResult result, StringBuilder report, IProgress<string>? progress, CancellationToken token)
        {
            string host = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string hostData = Path.Combine(Path.GetDirectoryName(host) ?? "", "Data");
            string dotnet = Path.Combine(hostData, "NetCoreRuntime", "dotnet.exe");
            string csc = Path.Combine(hostData, "DotNetSdkRoslyn", "csc.dll");
            if (!File.Exists(dotnet) || !File.Exists(csc))
                return;
            var units = BuildUnits(project);
            if (units.Count == 0)
                return;
            var shared = SharedReferences(project, editor);
            if (shared.Count == 0)
                return;
            string defines = string.Join(";", Defines(editor));
            string outputFolder = Path.Combine(project, "Temp", "GameSpelunkerFast");
            Directory.CreateDirectory(outputFolder);
            int pass = 0;
            foreach (var unit in units)
            {
                var built = units.TakeWhile(u => u != unit).Select(u => Path.Combine(outputFolder, u.Name + ".dll")).Where(File.Exists).ToList();
                for (int attempt = 1; attempt <= 40; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    pass++;
                    result.FastPasses = pass;
                    progress?.Report($"Compiling {unit.Name} with the quick compiler (pass {pass})…");
                    FillSources(unit, units);
                    if (unit.Sources.Count == 0)
                        break;
                    string response = Path.Combine(outputFolder, unit.Name + ".rsp");
                    var lines = new List<string>
                    {
                        "-t:library",
                        "-out:\"" + Path.Combine(outputFolder, unit.Name + ".dll") + "\"",
                        "-nostdlib+",
                        "-unsafe+",
                        "-warn:0",
                        "-define:" + defines,
                    };
                    lines.AddRange(shared.Concat(built).Select(r => "-r:\"" + r + "\""));
                    lines.AddRange(unit.Sources.Select(s => "\"" + s + "\""));
                    File.WriteAllLines(response, lines);
                    string text = await RunToolAsync(dotnet, "\"" + csc + "\" /nologo @\"" + response + "\"", project, token).ConfigureAwait(false);
                    var errors = ParseErrors(text, DecompilerErrors);
                    if (errors.Count == 0)
                        break;
                    progress?.Report($"Neutralising what does not compile (quick pass {pass}, {errors.Sum(e => e.Value.Count)} error(s))…");
                    if (!ApplyFixes(project, text, errors, result, report))
                        return;
                }
            }
        }

        private static void FillSources(CompileUnit unit, List<CompileUnit> units)
        {
            unit.Sources.Clear();
            var nested = units.Where(u => u != unit && u.Root.StartsWith(unit.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Select(u => u.Root + Path.DirectorySeparatorChar).ToList();
            unit.Sources.AddRange(Directory.EnumerateFiles(unit.Root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !nested.Any(n => f.StartsWith(n, StringComparison.OrdinalIgnoreCase))));
        }

        private static List<CompileUnit> BuildUnits(string project)
        {
            string assets = Path.Combine(project, "Assets");
            var units = new List<CompileUnit>();
            if (!Directory.Exists(assets))
                return units;
            var asmdefs = Directory.GetFiles(assets, "Spelunked.*.asmdef", SearchOption.AllDirectories);
            if (asmdefs.Length > 0)
            {
                foreach (var asmdef in asmdefs)
                {
                    string json = File.ReadAllText(asmdef);
                    var unit = new CompileUnit { Name = Path.GetFileNameWithoutExtension(asmdef), Root = Path.GetDirectoryName(asmdef)! };
                    var references = Regex.Match(json, "\"references\"\\s*:\\s*\\[([^\\]]*)\\]");
                    if (references.Success)
                        unit.References.AddRange(Regex.Matches(references.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));
                    units.Add(unit);
                }
                var ordered = new List<CompileUnit>();
                while (units.Count > 0)
                {
                    var next = units.FirstOrDefault(u => u.References.All(r => ordered.Any(o => o.Name == r) || units.All(x => x.Name != r))) ?? units[0];
                    units.Remove(next);
                    ordered.Add(next);
                }
                return ordered;
            }
            string plugins = Path.Combine(assets, "Plugins");
            string scripts = Path.Combine(assets, "Scripts");
            if (Directory.Exists(plugins) && Directory.EnumerateFiles(plugins, "*.cs", SearchOption.AllDirectories).Any())
                units.Add(new CompileUnit { Name = "Assembly-CSharp-firstpass", Root = plugins });
            if (Directory.Exists(scripts) && Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories).Any())
                units.Add(new CompileUnit { Name = "Assembly-CSharp", Root = scripts });
            return units;
        }

        private static List<string> SharedReferences(string project, string editor)
        {
            var references = new List<string>();
            string data = Path.Combine(Path.GetDirectoryName(editor) ?? "", "Data");
            if (!Directory.Exists(data))
                return references;
            string modules = Path.Combine(data, "Managed", "UnityEngine");
            if (Directory.Exists(modules))
                references.AddRange(Directory.GetFiles(modules, "*.dll"));
            else
                references.AddRange(Directory.GetFiles(Path.Combine(data, "Managed"), "UnityEngine*.dll"));
            string extensions = Path.Combine(data, "UnityExtensions");
            if (Directory.Exists(extensions))
                references.AddRange(Directory.GetFiles(extensions, "*.dll", SearchOption.AllDirectories)
                    .Where(f => !f.Replace('\\', '/').Contains("/Editor/")));
            string netStandard = Path.Combine(data, "NetStandard", "ref");
            string? mono = new[] { Path.Combine(data, "Mono", "lib", "mono", "2.0"), Path.Combine(data, "MonoBleedingEdge", "lib", "mono", "4.7.1-api"), Path.Combine(data, "MonoBleedingEdge", "lib", "mono", "4.5-api") }
                .FirstOrDefault(Directory.Exists);
            if (Directory.Exists(netStandard))
            {
                var standard = Directory.GetFiles(netStandard, "netstandard.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (standard != null) references.Add(standard);
            }
            else if (mono != null)
            {
                references.AddRange(new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Xml.dll", "System.Xml.Linq.dll", "System.Data.dll", "System.Runtime.Serialization.dll", "System.Configuration.dll" }
                    .Select(n => Path.Combine(mono, n)).Where(File.Exists));
            }
            else
            {
                return new List<string>();
            }
            foreach (var name in new[] { "Boo.Lang.dll", "UnityScript.Lang.dll" })
            {
                var bundled = Directory.EnumerateFiles(data, name, SearchOption.AllDirectories).FirstOrDefault();
                if (bundled != null) references.Add(bundled);
            }
            string assets = Path.Combine(project, "Assets");
            references.AddRange(Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Plugins/Native/") && ExtractedProject.IsManagedAssembly(f)));
            return references.GroupBy(r => Path.GetFileName(r), StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
        }

        private static readonly (int major, int maxMinor)[] VersionTable =
            { (5, 6), (2017, 4), (2018, 4), (2019, 4), (2020, 3), (2021, 3), (2022, 3), (2023, 2), (6000, 99) };

        private static List<string> Defines(string editor)
        {
            var defines = new List<string>
            {
                ExtractedProject.GameCodeDefine, "UNITY_EDITOR", "UNITY_EDITOR_WIN", "UNITY_EDITOR_64", "UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "ENABLE_MONO",
            };
            string version = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(editor)) ?? "") ?? "";
            var parsed = Regex.Match(version, @"^(\d+)\.(\d+)");
            if (!parsed.Success)
                return defines;
            int major = int.Parse(parsed.Groups[1].Value), minor = int.Parse(parsed.Groups[2].Value);
            defines.Add("UNITY_" + major);
            defines.Add("UNITY_" + major + "_" + minor);
            foreach (var (m, maxMinor) in VersionTable)
            {
                if (m > major)
                    break;
                int last = m == major ? minor : maxMinor;
                for (int i = 0; i <= last; i++)
                    defines.Add("UNITY_" + m + "_" + i + "_OR_NEWER");
            }
            return defines;
        }

        private static async Task<string> RunToolAsync(string executable, string arguments, string workingDirectory, CancellationToken token)
        {
            var info = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory,
            };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + Path.GetFileName(executable));
            using var registration = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            });
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            while (!process.HasExited)
                await Task.Delay(250).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (output)
                return output.ToString();
        }

        private static string WithDefine(string settings, string define, string? projectVersion)
        {
            if (settings.Contains(define))
                return settings;
            string key = ExtractedProject.IsAtLeast(projectVersion, 2021, 2) ? "Standalone" : "1";
            if (Regex.IsMatch(settings, @"^  scriptingDefineSymbols: \{\}", RegexOptions.Multiline))
                return Regex.Replace(settings, @"^  scriptingDefineSymbols: \{\}", "  scriptingDefineSymbols:\n    " + key + ": " + define, RegexOptions.Multiline);
            var existing = new Regex(@"^(  scriptingDefineSymbols:\s*\r?\n(?:    \S.*\r?\n)*?    (?:Standalone|1): )([^\r\n]*)", RegexOptions.Multiline);
            if (existing.IsMatch(settings))
                return existing.Replace(settings, m => m.Groups[1].Value + (m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.TrimEnd() + ";" : "") + define, 1);
            return Regex.Replace(settings, @"^  scriptingDefineSymbols:[ \t]*(\r?\n)", "  scriptingDefineSymbols:$1    " + key + ": " + define + "$1", RegexOptions.Multiline);
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

        private static bool ApplyFixes(string project, string logText, Dictionary<string, List<int>> errors, GameCodeResult result, StringBuilder report)
        {
            if (!errors.Keys.Any(IsGameCode))
                return false;
            var renamed = RenameMembersNamedLikeTheirType(project, logText);
            if (renamed.Count > 0)
            {
                result.MembersRenamed += renamed.Count;
                foreach (var r in renamed) report.AppendLine(r);
                return true;
            }
            bool changed = false;
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
                changed = true;
            }
            return changed;
        }

        private static readonly HashSet<string> DecompilerErrors = new HashSet<string>
        {
            "CS0165", "CS0841", "CS0128", "CS0136", "CS0542", "CS1628", "CS0206", "CS1612", "CS0131", "CS1656", "CS0102", "CS0111",
            "CS0177", "CS0161", "CS0163", "CS1621", "CS1673", "CS8175", "CS0122", "CS0571", "CS0117", "CS0072", "CS0115",
        };

        private static Dictionary<string, List<int>> ParseErrors(string text, HashSet<string>? only = null)
        {
            var errors = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in ErrorLine.Matches(text))
            {
                if (only != null && !only.Contains(m.Groups[3].Value))
                    continue;
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
