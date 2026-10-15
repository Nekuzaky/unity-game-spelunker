using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSpelunker
{
    /// <summary>
    /// Tools → Game Spelunker: pick a Unity game, read its build report, extract it with AssetRipper and explore the
    /// result (decompiled scripts, asset types). Meant for learning how games are built and for modding games that allow it.
    /// </summary>
    public sealed class SpelunkerWindow : EditorWindow
    {
        private const string StylePath = "Assets/GameSpelunker/Editor/Spelunker.uss";
        private const string ConsentKey = "GameSpelunker.Consent";
        private const string LastGameKey = "GameSpelunker.LastGame";

        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly List<string> _log = new List<string>();

        private string _gamePath = "";
        private BuildReport? _report;
        private string? _reportError;
        private List<FoundGame>? _found;
        private ExportMode _mode = ExportMode.UnityProject;
        private string _output = "";
        private CancellationTokenSource? _cts;
        private bool _busy;
        private string _busyText = "";
        private float _progress;
        private string _scriptFilter = "";
        private string _message = "";

        [MenuItem("Tools/Game Spelunker %#g", priority = 1)]
        public static SpelunkerWindow Open()
        {
            var window = GetWindow<SpelunkerWindow>();
            window.titleContent = new GUIContent("Game Spelunker", EditorGUIUtility.IconContent("d_Search Icon").image);
            window.minSize = new Vector2(440, 500);
            return window;
        }

        private void OnEnable()
        {
            EditorApplication.update += Pump;
            string last = EditorPrefs.GetString(LastGameKey, "");
            if (last.Length > 0 && Directory.Exists(last))
                SelectGame(last, rebuild: false);
        }

        private void OnDisable()
        {
            EditorApplication.update -= Pump;
            _cts?.Cancel();
        }

        private void CreateGUI() => Rebuild();

        private void Pump()
        {
            bool any = false;
            while (_mainThread.TryDequeue(out var action))
            {
                action();
                any = true;
            }
            if (any) Rebuild();
        }

        private void RunOnMain(Action action) => _mainThread.Enqueue(action);

        // ---------------------------------------------------------------- UI

        private void Rebuild()
        {
            var root = rootVisualElement;
            float scroll = root.Q<ScrollView>()?.scrollOffset.y ?? 0f;
            root.Clear();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) root.styleSheets.Add(style);

            var page = new ScrollView();
            page.AddToClassList("gs-page");
            root.Add(page);

            page.Add(Css(new Label("Game Spelunker"), "gs-title"));
            page.Add(Css(new Label("See how Unity games are built, and prepare mods for games that allow them."), "gs-subtitle"));

            bool consent = EditorPrefs.GetBool(ConsentKey, false);
            page.Add(new HelpBox(
                "Only extract games you own. Extracted assets and code still belong to their authors: use them to learn or to " +
                "build mods the developer allows, never redistribute them, and respect the game's license (EULA). " +
                "Online games with anti-cheat may ban accounts for modified files.", HelpBoxMessageType.Warning));
            var consentToggle = new Toggle("I understand and will respect these rules") { value = consent };
            consentToggle.RegisterValueChangedCallback(e => { EditorPrefs.SetBool(ConsentKey, e.newValue); Rebuild(); });
            page.Add(consentToggle);

            BuildGameSection(page);
            BuildReportSection(page);
            BuildAssetRipperSection(page);
            BuildExtractSection(page, consent);
            BuildExploreSection(page);

            page.schedule.Execute(() => page.scrollOffset = new Vector2(0, scroll));
        }

        private void BuildGameSection(VisualElement page)
        {
            page.Add(Section("1. Game"));
            var pathField = new TextField { value = _gamePath, isDelayed = true };
            pathField.RegisterValueChangedCallback(e => SelectGame(e.newValue));
            page.Add(Row("Game folder", pathField));
            page.Add(Buttons(
                Button("Choose folder…", () =>
                {
                    string folder = EditorUtility.OpenFolderPanel("Unity game folder (with the .exe and *_Data)", _gamePath, "");
                    if (!string.IsNullOrEmpty(folder)) SelectGame(folder);
                }),
                Button(_found == null ? "Find Unity games on this PC" : "Refresh list", () =>
                {
                    _found = GameFinder.FindInstalledUnityGames();
                    Rebuild();
                })));

            if (_found != null)
            {
                if (_found.Count == 0)
                    page.Add(Hint("No Unity games found in Steam, Epic, GOG or itch.io folders. Use \"Choose folder…\"."));
                foreach (var game in _found)
                {
                    var item = Css(new Button(() => SelectGame(game.Folder)) { text = $"{game.Name}   ·   {game.Source}" }, "gs-list-item");
                    if (string.Equals(game.Folder, _gamePath, StringComparison.OrdinalIgnoreCase)) item.AddToClassList("gs-list-item--selected");
                    page.Add(item);
                }
            }
        }

        private void BuildReportSection(VisualElement page)
        {
            if (_reportError != null)
            {
                page.Add(new HelpBox(_reportError, HelpBoxMessageType.Error));
                return;
            }
            if (_report == null)
                return;

            var r = _report;
            page.Add(Section("2. Build report"));
            page.Add(Row("Game", new Label(r.Name)));
            if (r.Company != null) page.Add(Row("Studio", new Label(r.Company)));
            string thisEditor = Application.unityVersion;
            string versionNote = r.UnityVersion == null ? "unknown"
                : r.UnityVersion == thisEditor ? $"{r.UnityVersion} (same as this editor)"
                : ExtractedProject.FindEditor(r.UnityVersion) != null ? $"{r.UnityVersion} (installed)"
                : $"{r.UnityVersion} (not installed: install it with Unity Hub to open the extracted project as-is)";
            page.Add(Row("Unity version", Css(new Label(versionNote), "gs-wrap")));
            page.Add(Row("Scripting", new Label(r.Backend == ScriptingBackend.Mono ? "Mono (scripts can be fully decompiled)" : r.Backend.ToString())));
            page.Add(Row("Render pipeline", new Label(r.RenderPipeline)));
            page.Add(Row("Scenes", new Label(r.CompressedData ? "compressed build" : r.SceneCount.ToString())));
            page.Add(Row("Size", new Label(BuildInspector.FormatSize(r.TotalSize))));
            if (r.StreamingAssetsSize > 0)
                page.Add(Row("StreamingAssets", new Label($"{BuildInspector.FormatSize(r.StreamingAssetsSize)}, {r.AssetBundleCount} asset bundle(s)")));
            page.Add(Row("Game assemblies", Css(new Label(r.GameAssemblies.Count == 0 ? "none" : string.Join(", ", r.GameAssemblies)), "gs-wrap")));
            if (r.NativePlugins.Count > 0)
                page.Add(Row("Native plugins", Css(new Label(string.Join(", ", r.NativePlugins)), "gs-wrap")));
            page.Add(Row("Mod support", Css(new Label(r.ModLoaders.Count == 0 ? "no mod loader installed" : string.Join(", ", r.ModLoaders)), "gs-wrap")));
            foreach (var warning in r.Warnings)
                page.Add(new HelpBox(warning, HelpBoxMessageType.Info));
            page.Add(Buttons(Button("Open game folder", () => EditorUtility.RevealInFinder(r.Executable ?? r.DataFolder))));
        }

        private void BuildAssetRipperSection(VisualElement page)
        {
            page.Add(Section("3. AssetRipper"));
            string? exe = AssetRipperInstaller.Executable;
            if (exe != null)
            {
                page.Add(Row("Installed", Css(new Label(exe), "gs-wrap")));
                if (AssetRipperInstaller.CustomExecutable != null)
                    page.Add(Buttons(Button("Use the managed install instead", () => { AssetRipperInstaller.CustomExecutable = null; Rebuild(); })));
                return;
            }

            page.Add(Hint($"Game Spelunker uses AssetRipper (GPL-3.0) to read game files. It is not bundled: install the official " +
                          $"release {AssetRipperInstaller.Version} ({AssetRipperInstaller.DownloadSize}, from github.com/AssetRipper) or point to one you have."));
            page.Add(Buttons(
                Button($"Install AssetRipper {AssetRipperInstaller.Version}", InstallAssetRipper, "gs-button--primary"),
                Button("Use existing AssetRipper…", () =>
                {
                    string file = EditorUtility.OpenFilePanel("AssetRipper executable", "", "exe");
                    if (!string.IsNullOrEmpty(file)) { AssetRipperInstaller.CustomExecutable = file; Rebuild(); }
                }),
                Button("Release page", () => Application.OpenURL(AssetRipperInstaller.ReleasePage))));
            if (_busy && _busyText.StartsWith("Downloading", StringComparison.Ordinal))
                page.Add(new ProgressBar { value = _progress * 100, title = _busyText });
        }

        private void BuildExtractSection(VisualElement page, bool consent)
        {
            if (_report == null)
                return;

            page.Add(Section("4. Extract"));
            var mode = new EnumField("What to export", _mode);
            mode.RegisterValueChangedCallback(e => _mode = (ExportMode)e.newValue);
            page.Add(mode);
            page.Add(Hint(_mode == ExportMode.UnityProject
                ? "UnityProject: a project you can open in Unity, with scenes, prefabs, materials and decompiled scripts."
                : "PrimaryContent: plain files (PNG textures, models, sounds, text) without a project around them."));

            if (_output.Length == 0)
                _output = DefaultOutput(_report);
            var output = new TextField { value = _output, isDelayed = true };
            output.RegisterValueChangedCallback(e => _output = e.newValue);
            page.Add(Row("Output folder", output));
            page.Add(Buttons(Button("Choose…", () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Where to put the extracted game", _output, "");
                if (!string.IsNullOrEmpty(folder)) { _output = folder; Rebuild(); }
            })));

            if (_busy && !_busyText.StartsWith("Downloading", StringComparison.Ordinal))
            {
                page.Add(new ProgressBar { value = _progress * 100, title = _busyText });
                page.Add(Buttons(Button("Cancel", () => _cts?.Cancel(), "gs-button--danger")));
            }
            else
            {
                var extract = Button("Extract", Extract, "gs-button--primary");
                extract.SetEnabled(consent && AssetRipperInstaller.Executable != null && !_busy);
                page.Add(Buttons(extract));
                if (!consent) page.Add(Hint("Tick \"I understand…\" above to enable extraction."));
            }

            if (_message.Length > 0)
                page.Add(new HelpBox(_message, _message.StartsWith("Failed", StringComparison.Ordinal) ? HelpBoxMessageType.Error : HelpBoxMessageType.Info));

            if (_log.Count > 0)
            {
                var log = Css(new Label(string.Join("\n", _log.Skip(Math.Max(0, _log.Count - 8)))), "gs-log");
                page.Add(log);
            }
        }

        private void BuildExploreSection(VisualElement page)
        {
            if (_report == null || _output.Length == 0 || !Directory.Exists(Path.Combine(_output, "ExportedProject")) && !Directory.Exists(Path.Combine(_output, "Assets")))
                return;

            page.Add(Section("5. Explore"));
            string? version = ExtractedProject.UnityVersionOf(_output);
            string? editor = ExtractedProject.FindEditor(version);
            page.Add(Buttons(
                Button("Open folder", () => EditorUtility.RevealInFinder(ExtractedProject.ProjectFolder(_output))),
                Button(editor != null ? $"Open in Unity {version}" : "Open in this Unity version", () => ExtractedProject.OpenInUnity(_output))));
            if (version != null && editor == null)
                page.Add(Hint($"The game was made with Unity {version}, which is not installed. Opening it with another version " +
                              "works for exploring, but Unity will upgrade the project."));

            // Asset types: AssetRipper groups exported assets in folders named after their type.
            string assets = Path.Combine(ExtractedProject.ProjectFolder(_output), "Assets");
            if (Directory.Exists(assets))
            {
                var types = Directory.GetDirectories(assets)
                    .Select(d => (name: Path.GetFileName(d), count: Directory.GetFiles(d, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta", StringComparison.Ordinal))))
                    .Where(t => t.count > 0)
                    .OrderByDescending(t => t.count)
                    .ToList();
                page.Add(Row("Asset types", Css(new Label(string.Join(", ", types.Select(t => $"{t.name} ({t.count})"))), "gs-wrap")));
            }

            var scripts = ExtractedProject.FindScripts(_output);
            page.Add(Css(new Label($"Game scripts ({scripts.Count}) — click to open in your code editor"), "gs-subsection"));
            var filter = new TextField { value = _scriptFilter, isDelayed = true };
            filter.RegisterValueChangedCallback(e => { _scriptFilter = e.newValue; Rebuild(); });
            page.Add(Row("Filter", filter));

            var shown = scripts.Where(s => _scriptFilter.Length == 0 || s.RelativePath.IndexOf(_scriptFilter, StringComparison.OrdinalIgnoreCase) >= 0).Take(300).ToList();
            foreach (var script in shown)
                page.Add(Css(new Button(() => InternalEditorUtility.OpenFileAtLineExternal(script.Path, 1)) { text = script.RelativePath }, "gs-list-item"));
            if (scripts.Count == 0)
                page.Add(Hint("No game scripts in this export (PrimaryContent mode, or the game has none)."));
        }

        // ---------------------------------------------------------------- actions

        private void SelectGame(string path, bool rebuild = true)
        {
            _gamePath = path;
            _report = null;
            _reportError = null;
            _output = "";
            _message = "";
            _log.Clear();
            try
            {
                _report = BuildInspector.Inspect(path);
                EditorPrefs.SetString(LastGameKey, path);
                if (Directory.Exists(DefaultOutput(_report)))
                    _output = DefaultOutput(_report);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                _reportError = ex.Message;
            }
            if (rebuild) Rebuild();
        }

        private static string DefaultOutput(BuildReport report)
        {
            string safe = new string(report.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Game Spelunker", safe);
        }

        private void InstallAssetRipper()
        {
            _busy = true;
            _busyText = $"Downloading AssetRipper {AssetRipperInstaller.Version}…";
            _progress = 0;
            _cts = new CancellationTokenSource();
            var progress = new Progress<float>(p => RunOnMain(() => _progress = p));
            Task.Run(async () =>
            {
                try
                {
                    await AssetRipperInstaller.InstallAsync(progress, _cts.Token).ConfigureAwait(false);
                    RunOnMain(() => _message = "AssetRipper installed.");
                }
                catch (Exception ex)
                {
                    RunOnMain(() => _message = "Failed to install AssetRipper: " + ex.Message);
                }
                finally
                {
                    RunOnMain(() => _busy = false);
                }
            });
            Rebuild();
        }

        private void Extract()
        {
            if (_report == null || AssetRipperInstaller.Executable is not { } exe)
                return;

            // Only ever clean a folder that holds a previous extraction, never an arbitrary folder.
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any())
            {
                bool previousExport = Directory.Exists(Path.Combine(_output, "ExportedProject")) || Directory.Exists(Path.Combine(_output, "AuxiliaryFiles"))
                                      || (Directory.Exists(Path.Combine(_output, "Assets")) && Directory.Exists(Path.Combine(_output, "ProjectSettings")));
                if (!previousExport)
                {
                    _message = "Failed: the output folder is not empty. Choose an empty folder.";
                    Rebuild();
                    return;
                }
                if (!EditorUtility.DisplayDialog("Game Spelunker", $"Replace the previous extraction in\n{_output} ?", "Replace", "Cancel"))
                    return;
                Directory.Delete(_output, true);
            }

            var report = _report;
            string output = _output;
            var mode = _mode;
            _busy = true;
            _busyText = "Starting AssetRipper…";
            _progress = 0;
            _message = "";
            _log.Clear();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var started = DateTime.Now;

            Task.Run(async () =>
            {
                using var session = new AssetRipperSession(exe);
                session.Log += line => RunOnMain(() =>
                {
                    _log.Add(line);
                    if (_log.Count > 200) _log.RemoveAt(0);
                });
                session.Progress += (phase, done, total, item) => RunOnMain(() =>
                {
                    _progress = total == 0 ? 0 : done / (float)total;
                    _busyText = $"{(phase.StartsWith("Export", StringComparison.Ordinal) ? "Exporting" : "Loading")} {done}/{total}";
                });
                using var registration = token.Register(session.Dispose);
                try
                {
                    await session.StartAsync(token).ConfigureAwait(false);
                    RunOnMain(() => _busyText = "Loading game files…");
                    await session.LoadAsync(report.DataFolder, token).ConfigureAwait(false);
                    RunOnMain(() => _busyText = "Exporting…");
                    await session.ExportAsync(output, mode, token).ConfigureAwait(false);
                    var seconds = (DateTime.Now - started).TotalSeconds;
                    RunOnMain(() => _message = $"Extracted in {seconds:0} s to {output}.");
                }
                catch (Exception ex) when (token.IsCancellationRequested && (ex is OperationCanceledException || ex is ObjectDisposedException || ex is System.Net.Http.HttpRequestException))
                {
                    RunOnMain(() => _message = "Cancelled.");
                }
                catch (Exception ex)
                {
                    RunOnMain(() => _message = "Failed: " + ex.Message);
                }
                finally
                {
                    RunOnMain(() => _busy = false);
                }
            });
            Rebuild();
        }

        // ---------------------------------------------------------------- helpers

        private static T Css<T>(T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }

        private static Label Section(string text) => Css(new Label(text), "gs-section");

        private static Label Hint(string text) => Css(new Label(text), "gs-hint");

        private static VisualElement Row(string label, VisualElement field)
        {
            var row = Css(new VisualElement(), "gs-row");
            row.Add(Css(new Label(label), "gs-label"));
            field.AddToClassList("gs-field");
            row.Add(field);
            return row;
        }

        private static Button Button(string text, Action onClick, string? modifier = null)
        {
            var b = Css(new Button(onClick) { text = text }, "gs-button");
            if (modifier != null) b.AddToClassList(modifier);
            return b;
        }

        private static VisualElement Buttons(params Button[] buttons)
        {
            var row = Css(new VisualElement(), "gs-buttons");
            foreach (var b in buttons) row.Add(b);
            return row;
        }
    }
}
