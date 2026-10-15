using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GameSpelunker
{
    public sealed partial class SpelunkerWindow
    {
        private static class Settings
        {
            private const string OutputKey = "GameSpelunker.OutputRoot";
            private const string CheckKey = "GameSpelunker.CheckAfterExtracting";
            private const string PassesKey = "GameSpelunker.CompilePasses";
            private const string NotifyKey = "GameSpelunker.NotifyWhenDone";

            public static string DefaultOutputRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Game Spelunker");

            public static string OutputRoot
            {
                get { string value = EditorPrefs.GetString(OutputKey, ""); return value.Length > 0 ? value : DefaultOutputRoot; }
                set => EditorPrefs.SetString(OutputKey, value == DefaultOutputRoot ? "" : value);
            }

            public static bool CheckAfterExtracting
            {
                get => EditorPrefs.GetBool(CheckKey, true);
                set => EditorPrefs.SetBool(CheckKey, value);
            }

            public static int CompilePasses
            {
                get => Mathf.Clamp(EditorPrefs.GetInt(PassesKey, 12), 1, 40);
                set => EditorPrefs.SetInt(PassesKey, Mathf.Clamp(value, 1, 40));
            }

            public static bool NotifyWhenDone
            {
                get => EditorPrefs.GetBool(NotifyKey, true);
                set => EditorPrefs.SetBool(NotifyKey, value);
            }

            public static void Reset()
            {
                EditorPrefs.DeleteKey(OutputKey);
                EditorPrefs.DeleteKey(CheckKey);
                EditorPrefs.DeleteKey(PassesKey);
                EditorPrefs.DeleteKey(NotifyKey);
            }
        }

        private enum StageState { Pending, Running, Done, Skipped, Failed }

        private sealed class Stage
        {
            public string Name = "";
            public StageState State;
            public DateTime Started;
            public DateTime Ended;
            public string Note = "";
        }

        private sealed class QueuedExtraction
        {
            public string Name = "";
            public string Game = "";
            public string Output = "";
            public ExportMode Mode;
            public bool Import;
        }

        private const string StageDownload = "Download AssetRipper";
        private const string StageExport = "Export with AssetRipper";
        private const string StagePrepare = "Prepare the project";
        private const string StageCode = "Make the game's code compile";
        private const string StageImport = "Import and repair in Unity";
        private const string StageCheck = "Check in Play mode";

        private readonly List<Stage> _stages = new List<Stage>();
        private string _stagesTitle = "";
        private DateTime _stagesStarted;
        private readonly List<QueuedExtraction> _queue = new List<QueuedExtraction>();
        private string _gameFilter = "";
        private string _gameSort = "Name";
        private readonly Dictionary<string, BuildReport?> _peeks = new Dictionary<string, BuildReport?>(StringComparer.OrdinalIgnoreCase);
        private bool _peeking;
        private readonly Dictionary<string, (long stamp, Texture2D texture)> _images = new Dictionary<string, (long, Texture2D)>(StringComparer.OrdinalIgnoreCase);
        private bool _texturesOpen;
        private string _textureFilter = "";
        private (string key, List<string> files) _textureFiles = ("", new List<string>());

        private void BeginStages(string title, List<string> names)
        {
            _stages.Clear();
            _stages.AddRange(names.Select(n => new Stage { Name = n }));
            _stagesTitle = title;
            _stagesStarted = DateTime.Now;
        }

        private void StartStage(string name)
        {
            foreach (var running in _stages.Where(s => s.State == StageState.Running))
            {
                running.State = StageState.Done;
                running.Ended = DateTime.Now;
            }
            var stage = _stages.FirstOrDefault(s => s.Name == name);
            if (stage == null)
                return;
            stage.State = StageState.Running;
            stage.Started = DateTime.Now;
        }

        private void SkipStage(string name)
        {
            var stage = _stages.FirstOrDefault(s => s.Name == name);
            if (stage == null)
                return;
            if (stage.State == StageState.Running)
                stage.Ended = DateTime.Now;
            stage.State = StageState.Skipped;
            stage.Note = "nothing to compile";
        }

        private void FailStage(string reason)
        {
            var stage = _stages.FirstOrDefault(s => s.State == StageState.Running);
            if (stage == null)
                return;
            stage.State = StageState.Failed;
            stage.Ended = DateTime.Now;
            stage.Note = reason.Split('\n')[0];
        }

        private void CompleteStages()
        {
            foreach (var stage in _stages.Where(s => s.State == StageState.Running))
            {
                stage.State = StageState.Done;
                stage.Ended = DateTime.Now;
            }
        }

        private VisualElement StagesView()
        {
            var box = Css(new VisualElement(), "gs-stages");
            var title = Css(new Label(), "gs-subsection");
            box.Add(title);
            var rows = new List<(Stage stage, Image icon, Label time)>();
            foreach (var stage in _stages)
            {
                var row = Css(new VisualElement(), "gs-stage");
                var icon = Css(new Image(), "gs-stage-icon");
                row.Add(icon);
                row.Add(Css(new Label(stage.Name), "gs-stage-name"));
                var time = Css(new Label(), "gs-stage-time");
                row.Add(time);
                box.Add(row);
                rows.Add((stage, icon, time));
            }

            void Refresh()
            {
                bool running = _stages.Any(s => s.State == StageState.Running);
                var total = DateTime.Now - _stagesStarted;
                title.text = running ? $"{_stagesTitle} · {Duration(total)} so far"
                    : _stages.Any(s => s.State == StageState.Failed) ? $"{_stagesTitle} · stopped"
                    : $"{_stagesTitle} · done in {Duration((_stages.Where(s => s.Ended != default).Select(s => s.Ended).DefaultIfEmpty(DateTime.Now).Max()) - _stagesStarted)}";
                foreach (var (stage, icon, time) in rows)
                {
                    icon.image = Icon(stage.State switch
                    {
                        StageState.Done => "d_TestPassed",
                        StageState.Failed => "d_TestFailed",
                        StageState.Skipped => "d_TestIgnored",
                        StageState.Running => "d_WaitSpin" + ((int)(EditorApplication.timeSinceStartup * 8) % 12).ToString("00"),
                        _ => "d_TestNormal",
                    });
                    time.text = stage.State switch
                    {
                        StageState.Done => Duration(stage.Ended - stage.Started),
                        StageState.Skipped => stage.Note,
                        StageState.Failed => stage.Note.Length > 70 ? stage.Note.Substring(0, 70) + "…" : stage.Note,
                        StageState.Running => RunningTime(stage),
                        _ => "",
                    };
                    time.tooltip = stage.Note;
                }
            }

            Refresh();
            box.schedule.Execute(Refresh).Every(250);
            return box;
        }

        private string RunningTime(Stage stage)
        {
            var elapsed = DateTime.Now - stage.Started;
            bool measurable = stage.Name == StageExport || stage.Name == StageImport || stage.Name == StageDownload;
            if (measurable && _progress > 0.03f && _progress < 1f)
            {
                var left = TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - _progress) / _progress);
                return $"{Duration(elapsed)} · about {Duration(left)} left";
            }
            return Duration(elapsed);
        }

        private static string Duration(TimeSpan span) =>
            span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:00} min"
            : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min {span.Seconds:00} s"
            : $"{Math.Max(0, span.Seconds)} s";

        private void FinishRun(string name, bool success, bool cancelled)
        {
            if (!cancelled && Settings.NotifyWhenDone)
                Notify(success ? $"{name} is extracted and ready." : $"{name}: the extraction failed.");
            if (!cancelled && _queue.Count > 0)
                EditorApplication.delayCall += StartNextQueued;
        }

        private void Notify(string text)
        {
            ShowNotification(new GUIContent(text), 6);
            EditorApplication.Beep();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                FlashTaskbar();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashInfo info);

        private static void FlashTaskbar()
        {
            try
            {
                var window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (window == IntPtr.Zero)
                    return;
                var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window, Flags = 3 | 12, Count = 0, Timeout = 0 };
                FlashWindowEx(ref info);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is InvalidOperationException) { }
        }

        private bool IsQueued(string output) => _queue.Any(q => string.Equals(q.Output, output, StringComparison.OrdinalIgnoreCase));

        private void Enqueue(BuildReport report)
        {
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any())
            {
                if (!IsPreviousExport(_output))
                {
                    _message = "Failed: the output folder is not empty. Choose an empty folder.";
                    Rebuild();
                    return;
                }
                if (!EditorUtility.DisplayDialog("Game Spelunker", $"When its turn comes, replace the previous extraction in\n{_output} ?", "Replace", "Cancel"))
                    return;
            }
            _queue.Add(new QueuedExtraction { Name = report.Name, Game = _gamePath, Output = _output, Mode = _mode, Import = _importInUnity });
            Rebuild();
        }

        private void StartNextQueued()
        {
            if (_busy || _queue.Count == 0)
                return;
            var next = _queue[0];
            _queue.RemoveAt(0);
            SelectGame(next.Game, rebuild: false);
            if (_report == null)
            {
                _message = $"Failed: {next.Name} is no longer a Unity game folder ({next.Game}).";
                EditorApplication.delayCall += StartNextQueued;
                Rebuild();
                return;
            }
            _output = next.Output;
            _mode = next.Mode;
            _importInUnity = next.Import;
            Extract(false);
        }

        private VisualElement QueueView()
        {
            var box = Css(new VisualElement(), "gs-stages");
            box.Add(Css(new Label($"Queue · {_queue.Count} game(s) waiting"), "gs-subsection"));
            foreach (var item in _queue.ToList())
            {
                var row = Css(new VisualElement(), "gs-disk-row");
                row.Add(Css(new Label(item.Name), "gs-disk-name"));
                row.Add(Chip(item.Mode == ExportMode.UnityProject ? (item.Import ? "project + repair" : "project") : "raw assets", "gs-chip--muted"));
                row.Add(Button("Remove", () => { _queue.Remove(item); Rebuild(); }, icon: "d_winbtn_win_close"));
                box.Add(row);
            }
            if (!_busy)
                box.Add(Buttons(Button("Start the queue", StartNextQueued, "gs-button--primary", "d_PlayButton"),
                    Button("Clear", () => { _queue.Clear(); Rebuild(); })));
            else
                box.Add(Hint("The next game starts as soon as the current extraction ends."));
            return box;
        }

        private VisualElement GameListTools(VisualElement card)
        {
            var row = Css(new VisualElement(), "gs-list-tools");
            var search = new ToolbarSearchField { value = _gameFilter };
            search.AddToClassList("gs-search");
            search.AddToClassList("gs-grow");
            search.RegisterValueChangedCallback(e => { _gameFilter = e.newValue; RefreshGameList(card); });
            row.Add(search);
            var sort = new PopupField<string>(new List<string> { "Name", "Unity version", "Scripting", "Extracted first" }, _gameSort) { tooltip = "Sort the games" };
            sort.AddToClassList("gs-sort");
            sort.RegisterValueChangedCallback(e => { _gameSort = e.newValue; RefreshGameList(card); });
            row.Add(sort);
            PeekGames();
            return row;
        }

        private void PeekGames()
        {
            if (_found == null || _peeking || _found.All(g => _peeks.ContainsKey(g.Folder)))
                return;
            _peeking = true;
            var folders = _found.Select(g => g.Folder).Where(f => !_peeks.ContainsKey(f)).ToList();
            Task.Run(() =>
            {
                var results = folders.Select(f => (f, BuildInspector.Peek(f))).ToList();
                RunOnMain(() =>
                {
                    foreach (var (folder, peek) in results) _peeks[folder] = peek;
                    _peeking = false;
                });
            });
        }

        private bool IsExtracted(BuildReport? peek) => peek != null && IsPreviousExport(DefaultOutput(peek));

        private void RefreshGameList(VisualElement card)
        {
            var list = card.Q<VisualElement>("game-list");
            if (list == null || _found == null)
                return;
            list.Clear();
            IEnumerable<FoundGame> games = _found.Where(g => _gameFilter.Length == 0 || g.Name.IndexOf(_gameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            BuildReport? Peek(FoundGame g) => _peeks.TryGetValue(g.Folder, out var p) ? p : null;
            games = _gameSort switch
            {
                "Unity version" => games.OrderByDescending(g => Peek(g)?.UnityVersion ?? "", Comparer<string>.Create(CompareVersions)),
                "Scripting" => games.OrderBy(g => Peek(g)?.Backend.ToString() ?? "~").ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                "Extracted first" => games.OrderByDescending(g => IsExtracted(Peek(g))).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                _ => games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            };
            int shown = 0;
            foreach (var game in games)
            {
                shown++;
                var item = Css(new Button(() => SelectGame(game.Folder)), "gs-game");
                var icon = GameIcon(game.Folder);
                item.Add(Css(new Image { image = icon != null ? icon : Icon("d_UnityEditor.GameView"), scaleMode = ScaleMode.ScaleAndCrop },
                    icon != null ? "gs-game-icon gs-game-icon--art" : "gs-game-icon"));
                item.Add(Css(new Label(game.Name), "gs-game-name"));
                var peek = Peek(game);
                if (peek != null)
                {
                    if (IsExtracted(peek))
                        item.Add(Chip("Extracted", "gs-chip--ok"));
                    if (peek.Backend != ScriptingBackend.Unknown)
                        item.Add(Chip(peek.Backend.ToString(), peek.Backend == ScriptingBackend.Mono ? "gs-chip--info" : "gs-chip--warn"));
                    if (peek.UnityVersion != null)
                    {
                        bool installed = ExtractedProject.FindEditor(peek.UnityVersion) != null;
                        var version = Chip(peek.UnityVersion, installed ? "gs-chip--ok" : "gs-chip--muted");
                        version.tooltip = installed ? "This Unity version is installed" : "This Unity version is not installed";
                        item.Add(version);
                    }
                }
                item.Add(Chip(game.Source, "gs-chip--muted"));
                if (string.Equals(game.Folder, _gamePath, StringComparison.OrdinalIgnoreCase)) item.AddToClassList("gs-game--selected");
                list.Add(item);
            }
            if (shown == 0)
                list.Add(Hint($"No game matches \"{_gameFilter}\"."));
        }

        private static int CompareVersions(string a, string b)
        {
            var x = Regex.Matches(a, @"\d+").Cast<Match>().Select(m => int.Parse(m.Value)).ToArray();
            var y = Regex.Matches(b, @"\d+").Cast<Match>().Select(m => int.Parse(m.Value)).ToArray();
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return x.Length.CompareTo(y.Length);
        }

        private VisualElement HealthDashboard()
        {
            var box = new VisualElement();
            string auxiliary = Path.Combine(_output, "AuxiliaryFiles");
            string reportFile = Path.Combine(auxiliary, ProjectChecker.ReportFile);
            string[] report = File.Exists(reportFile) ? File.ReadAllLines(reportFile) : Array.Empty<string>();
            string fixesFile = Path.Combine(auxiliary, "GameSpelunker-code-fixes.txt");
            string fixes = File.Exists(fixesFile) ? File.ReadLines(fixesFile).FirstOrDefault() ?? "" : "";
            string repairedFile = Path.Combine(ExtractedProject.ProjectFolder(_output), "ProjectSettings", "GameSpelunker.txt");
            string materials = File.Exists(repairedFile) ? File.ReadLines(repairedFile).Skip(1).FirstOrDefault() ?? "" : "";

            var tiles = Css(new VisualElement(), "gs-tiles");
            string verdict = report.Length > 0 && report[0].Contains(": ") ? report[0].Substring(report[0].IndexOf(": ", StringComparison.Ordinal) + 2) : "";
            string scenes = report.FirstOrDefault(l => l.StartsWith("Scenes loaded", StringComparison.Ordinal))?.Split(':').Last().Trim() ?? "";
            tiles.Add(HealthTile("d_PlayButton", "Play mode",
                verdict.Length == 0 ? "Not checked" : verdict.StartsWith("Runs without", StringComparison.Ordinal) ? "No errors" : verdict.StartsWith("Runs", StringComparison.Ordinal) ? verdict.Replace("Runs with ", "") : "Did not start",
                verdict.Length == 0 ? "click Check the project" : scenes,
                verdict.Length == 0 ? "" : verdict.StartsWith("Runs without", StringComparison.Ordinal) ? "ok" : verdict.StartsWith("Runs", StringComparison.Ordinal) ? "warn" : "bad"));

            var code = Regex.Match(fixes, @"Compiles: (\w+)");
            var stubbed = Regex.Match(fixes, @"(\d+) member\(s\) stubbed");
            var quarantined = Regex.Match(fixes, @"(\d+) file\(s\) quarantined");
            bool hasCode = code.Success;
            bool compiles = hasCode && code.Groups[1].Value == "True";
            tiles.Add(HealthTile("d_cs Script Icon", "Game code",
                !hasCode ? "Not compiled" : compiles ? "Compiles" : "Errors",
                !hasCode ? "" : $"{(stubbed.Success ? stubbed.Groups[1].Value : "0")} stubbed · {(quarantined.Success ? quarantined.Groups[1].Value : "0")} set aside",
                !hasCode ? "" : !compiles ? "bad" : (stubbed.Success && stubbed.Groups[1].Value != "0") || (quarantined.Success && quarantined.Groups[1].Value != "0") ? "warn" : "ok"));

            var repaired = Regex.Match(materials, @"^(\d+) material\(s\) repaired");
            var left = Regex.Match(materials, @"(\d+) left as they are");
            tiles.Add(HealthTile("d_Material Icon", "Materials",
                repaired.Success ? repaired.Groups[1].Value + " repaired" : "Not repaired",
                left.Success ? left.Groups[1].Value + " left as they are" : "",
                repaired.Success ? "ok" : ""));

            var empty = Regex.Match(report.FirstOrDefault(l => l.StartsWith("Shaders still empty", StringComparison.Ordinal)) ?? "", @"(\d+)$");
            tiles.Add(HealthTile("d_Shader Icon", "Custom shaders",
                empty.Success ? empty.Groups[1].Value + " empty" : "?",
                empty.Success ? "replaced by Unity's own where possible" : "",
                empty.Success ? (empty.Groups[1].Value == "0" ? "ok" : "warn") : ""));
            box.Add(tiles);

            var shots = Directory.Exists(auxiliary) ? Directory.GetFiles(auxiliary, "GameSpelunker-check-*.png").OrderBy(f => f).ToList() : new List<string>();
            if (shots.Count > 0)
            {
                var gallery = Css(new VisualElement(), "gs-gallery");
                foreach (var shot in shots)
                {
                    var image = Css(new Image { image = LoadImage(shot, 640), scaleMode = ScaleMode.ScaleToFit, tooltip = "Click to open" }, "gs-check-shot");
                    image.RegisterCallback<ClickEvent>(_ => EditorUtility.OpenWithDefaultApp(shot));
                    gallery.Add(image);
                }
                box.Add(gallery);
            }

            var errors = report.Where(l => l.StartsWith("  x", StringComparison.Ordinal)).Select(l => l.Trim()).ToList();
            if (errors.Count > 0)
            {
                var foldout = new Foldout { text = "Most frequent errors in Play mode", value = false };
                foldout.AddToClassList("gs-log-foldout");
                foldout.Add(Css(new Label(string.Join("\n", errors)), "gs-log"));
                box.Add(foldout);
            }

            if (report.Length > 0 || fixes.Length > 0)
                box.Add(Buttons(Button("Copy report", () =>
                {
                    EditorGUIUtility.systemCopyBuffer = FullReport(report, fixes, materials);
                    ShowNotification(new GUIContent("Report copied"), 2);
                }, icon: "d_Clipboard")));
            return box;
        }

        private string FullReport(string[] report, string fixes, string materials)
        {
            var text = new StringBuilder();
            text.AppendLine($"Game Spelunker {PackageVersion()} report");
            if (_report != null)
                text.AppendLine($"Game: {_report.Name} · Unity {_report.UnityVersion ?? "?"} · {_report.Backend} · {_report.RenderPipeline}");
            string? version = ExtractedProject.UnityVersionOf(_output);
            text.AppendLine($"Opened with: {(ExtractedProject.FindEditor(version) != null ? "Unity " + version + " (same as the game)" : "Unity " + Application.unityVersion)}");
            if (fixes.Length > 0) text.AppendLine("Game code: " + fixes);
            if (materials.Length > 0) text.AppendLine("Materials: " + materials);
            foreach (var line in report)
                text.AppendLine(line);
            return text.ToString();
        }

        private static string PackageVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(PackageRoot);
            return info != null ? info.version : "";
        }

        private static VisualElement HealthTile(string icon, string label, string value, string detail, string state)
        {
            var tile = Tile(icon, label, value, detail);
            if (state.Length > 0) tile.AddToClassList("gs-tile--" + state);
            return tile;
        }

        private Texture2D? LoadImage(string path, int maxSize)
        {
            long stamp;
            try { stamp = File.GetLastWriteTimeUtc(path).Ticks ^ maxSize; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
            if (_images.TryGetValue(path, out var cached) && cached.stamp == stamp && cached.texture != null)
                return cached.texture;
            if (cached.texture != null) DestroyImmediate(cached.texture);
            Texture2D? result = null;
            try
            {
                var full = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (!full.LoadImage(File.ReadAllBytes(path)))
                {
                    DestroyImmediate(full);
                    return null;
                }
                result = full;
                int largest = Math.Max(full.width, full.height);
                if (largest > maxSize)
                {
                    float scale = maxSize / (float)largest;
                    int w = Math.Max(1, (int)(full.width * scale)), h = Math.Max(1, (int)(full.height * scale));
                    var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(full, rt);
                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    var small = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    small.Apply();
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                    DestroyImmediate(full);
                    result = small;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            if (result != null) _images[path] = (stamp, result);
            return result;
        }

        private void ReleaseImages()
        {
            foreach (var (_, texture) in _images.Values)
                if (texture != null) DestroyImmediate(texture);
            _images.Clear();
        }

        private VisualElement ScenesSection(string assets)
        {
            var scenes = Directory.GetFiles(assets, "*.unity", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/" + ExtractedProject.ToolsFolder + "/"))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Length)
                .ToList();
            var box = new VisualElement();
            box.Add(Css(new Label($"Scenes · {scenes.Count}"), "gs-subsection"));
            if (scenes.Count == 0)
            {
                box.Add(Hint("No scenes in this export."));
                return box;
            }
            string project = ExtractedProject.ProjectFolder(_output);
            foreach (var scene in scenes.Take(12))
            {
                string relative = scene.FullName.Substring(project.Length + 1).Replace('\\', '/');
                var row = Css(new VisualElement(), "gs-disk-row");
                row.Add(Css(new Image { image = Icon("d_SceneAsset Icon") }, "gs-type-icon"));
                row.Add(Css(new Label(Path.GetFileNameWithoutExtension(scene.Name)), "gs-disk-name"));
                if (scene == scenes[0]) row.Add(Chip("biggest", "gs-chip--info"));
                row.Add(Css(new Label(BuildInspector.FormatSize(scene.Length)), "gs-disk-size"));
                var open = Button("Open in Unity", () => ExtractedProject.OpenInUnity(_output, relative), icon: "d_UnityLogo");
                open.tooltip = "Opens the project in Unity on this scene, with the objects the game spawns from code shown as a preview.";
                row.Add(open);
                box.Add(row);
            }
            if (scenes.Count > 12)
                box.Add(Hint($"{scenes.Count - 12} smaller scene(s) not listed."));
            return box;
        }

        private VisualElement TexturesSection(string assets)
        {
            var foldout = new Foldout { text = "Textures", value = _texturesOpen };
            foldout.AddToClassList("gs-log-foldout");
            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.target != foldout) return;
                _texturesOpen = e.newValue;
                if (_texturesOpen) FillTextures(foldout, assets);
            });
            if (_texturesOpen)
                FillTextures(foldout, assets);
            return foldout;
        }

        private void FillTextures(Foldout foldout, string assets)
        {
            foldout.Clear();
            if (_textureFiles.key != assets)
            {
                var files = new List<string>();
                foreach (var folder in new[] { "Texture2D", "Sprite", "Cubemap" }.Select(f => Path.Combine(assets, f)).Where(Directory.Exists))
                    files.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                _textureFiles = (assets, files);
            }
            foldout.text = $"Textures · {_textureFiles.files.Count}";
            var search = new ToolbarSearchField { value = _textureFilter };
            search.AddToClassList("gs-search");
            var grid = Css(new VisualElement(), "gs-texture-grid");
            var count = Css(new Label(), "gs-hint");
            search.RegisterValueChangedCallback(e => { _textureFilter = e.newValue; FillTextureGrid(grid, count); });
            foldout.Add(search);
            foldout.Add(grid);
            foldout.Add(count);
            FillTextureGrid(grid, count);
        }

        private void FillTextureGrid(VisualElement grid, Label count)
        {
            grid.Clear();
            const int limit = 60;
            var matches = _textureFiles.files.Where(f => _textureFilter.Length == 0 || Path.GetFileName(f).IndexOf(_textureFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            count.text = matches.Count > limit ? $"Showing {limit} of {matches.Count}. Search to narrow it down." : $"{matches.Count} texture(s).";
            int index = 0;
            foreach (var file in matches.Take(limit))
            {
                var cell = Css(new VisualElement { tooltip = Path.GetFileName(file) + "\nClick to show it in the folder" }, "gs-texture");
                var image = Css(new Image { scaleMode = ScaleMode.ScaleToFit }, "gs-texture-image");
                cell.Add(image);
                cell.Add(Css(new Label(Path.GetFileNameWithoutExtension(file)), "gs-texture-name"));
                cell.RegisterCallback<ClickEvent>(_ => EditorUtility.RevealInFinder(file));
                grid.Add(cell);
                if (_images.TryGetValue(file, out var cached) && cached.texture != null)
                    image.image = cached.texture;
                else
                    grid.schedule.Execute(() => { if (image.panel != null) image.image = LoadImage(file, 96); }).ExecuteLater(20 + index++ * 15);
            }
        }

        private VisualElement? CodeFixesSection()
        {
            string file = Path.Combine(_output, "AuxiliaryFiles", "GameSpelunker-code-fixes.txt");
            if (!File.Exists(file))
                return null;
            var entries = File.ReadAllLines(file).Skip(1)
                .Select(l => Regex.Match(l, @"^(Assets/[^:]+\.cs): (.+)$"))
                .Where(m => m.Success)
                .Select(m => (path: m.Groups[1].Value, what: m.Groups[2].Value))
                .ToList();
            if (entries.Count == 0)
                return null;
            var foldout = new Foldout { text = $"Code fixes · {entries.Count}", value = false };
            foldout.AddToClassList("gs-log-foldout");
            foldout.Add(Css(new Label("Members that did not survive decompilation were replaced by a warning or a NotImplementedException, and files that " +
                                      "could not be fixed were set aside (wrapped in #if SPELUNKER_QUARANTINED)."), "gs-text"));
            string project = ExtractedProject.ProjectFolder(_output);
            foreach (var (path, what) in entries.Take(80))
            {
                string full = Path.Combine(project, path);
                var item = Css(new Button(() => OpenAtFix(full)) { tooltip = "Open in your code editor" }, "gs-script");
                item.Add(Css(new Image { image = Icon("d_cs Script Icon") }, "gs-script-icon"));
                item.Add(Css(new Label(Path.GetFileNameWithoutExtension(path)), "gs-script-name"));
                item.Add(Chip(what.Contains("quarantined") ? "set aside" : what.Contains("renamed") ? "renamed" : what.Contains("stubbed") ? "stubbed" : what,
                    what.Contains("quarantined") ? "gs-chip--warn" : "gs-chip--muted"));
                foldout.Add(item);
            }
            return foldout;
        }

        private static void OpenAtFix(string file)
        {
            if (!File.Exists(file))
                return;
            var lines = File.ReadAllLines(file);
            int line = Array.FindIndex(lines, l => l.Contains(GameCodeCompiler.StubMessage)) + 1;
            InternalEditorUtility.OpenFileAtLineExternal(file, Math.Max(1, line));
        }

        private VisualElement SettingsCard()
        {
            var card = Css(new Foldout { text = "Settings", value = false }, "gs-card gs-settings");
            var output = new TextField { value = Settings.OutputRoot, isDelayed = true };
            output.AddToClassList("gs-path");
            output.RegisterValueChangedCallback(e => { Settings.OutputRoot = e.newValue.Trim(); Rebuild(); });
            card.Add(Row("Extractions folder", output));
            card.Add(Buttons(Button("Choose…", () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Where Game Spelunker puts extracted games", Settings.OutputRoot, "");
                if (!string.IsNullOrEmpty(folder)) { Settings.OutputRoot = folder; Rebuild(); }
            }, icon: "d_FolderOpened Icon")));

            var check = new Toggle("Check the project in Play mode after extracting") { value = Settings.CheckAfterExtracting };
            check.tooltip = "Adds a few minutes: presses Play in the background, takes screenshots and counts errors.";
            check.RegisterValueChangedCallback(e => Settings.CheckAfterExtracting = e.newValue);
            card.Add(check);

            var notify = new Toggle("Notify me when an extraction ends (sound and taskbar)") { value = Settings.NotifyWhenDone };
            notify.RegisterValueChangedCallback(e => Settings.NotifyWhenDone = e.newValue);
            card.Add(notify);

            var passes = new SliderInt("Code fix passes", 1, 40) { value = Settings.CompilePasses, showInputField = true };
            passes.tooltip = "How many times Game Spelunker compiles the game's code and fixes what fails. Each pass opens Unity in the background.";
            passes.RegisterValueChangedCallback(e => Settings.CompilePasses = e.newValue);
            card.Add(passes);

            card.Add(Buttons(Button("Reset to defaults", () => { Settings.Reset(); Rebuild(); }, icon: "d_Refresh")));
            return card;
        }
    }
}
