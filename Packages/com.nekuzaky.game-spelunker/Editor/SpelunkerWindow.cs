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
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GameSpelunker
{
    public sealed partial class SpelunkerWindow : EditorWindow
    {
        public const string PackageRoot = "Packages/com.nekuzaky.game-spelunker";
        private const string StylePath = PackageRoot + "/Editor/Spelunker.uss";
        private const string ConsentKey = "GameSpelunker.Consent";
        private const string LastGameKey = "GameSpelunker.LastGame";
        private const string IconPath = PackageRoot + "/Editor/Icons/GameSpelunker.png";
        private const string TabIconPath = PackageRoot + "/Editor/Icons/GameSpelunker-tab.png";
        public const string RepositoryUrl = "https://github.com/Nekuzaky/unity-game-spelunker";
        public const string DocumentationUrl = "https://nekuzaky.com/docs/unity-game-spelunker";

        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<string, Texture2D?> _gameIcons = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);

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
        private string _importTarget = "";
        private bool _importScenes = true;
        private bool _importScripts;
        private bool _importInUnity = true;
        private string _importMessage = "";
        private string _editorMessage = "";
        private string _exploreMessage = "";
        private bool _busyInExplore;
        private readonly Dictionary<string, (long total, long library)> _sizes = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _measuring = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [MenuItem("Tools/Game Spelunker %#g", priority = 1)]
        public static SpelunkerWindow Open()
        {
            var window = GetWindow<SpelunkerWindow>();
            window.titleContent = new GUIContent("Game Spelunker", AssetDatabase.LoadAssetAtPath<Texture2D>(TabIconPath));
            window.minSize = new Vector2(460, 520);
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
            foreach (var icon in _gameIcons.Values)
                if (icon != null) DestroyImmediate(icon);
            _gameIcons.Clear();
            ReleaseImages();
        }

        private Texture2D? GameIcon(string folder)
        {
            if (_gameIcons.TryGetValue(folder, out var cached))
                return cached;
            Texture2D? icon = null;
            if (GameFinder.FindIcon(folder) is { } path)
            {
                try
                {
                    var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    if (texture.LoadImage(File.ReadAllBytes(path)))
                        icon = texture;
                    else
                        DestroyImmediate(texture);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
            _gameIcons[folder] = icon;
            return icon;
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

        private void Rebuild()
        {
            var root = rootVisualElement;
            float scroll = root.Q<ScrollView>()?.scrollOffset.y ?? 0f;
            root.Clear();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) root.styleSheets.Add(style);
            root.AddToClassList(EditorGUIUtility.isProSkin ? "gs-dark" : "gs-light");

            root.Add(Header());

            var page = new ScrollView();
            page.AddToClassList("gs-page");
            root.Add(page);

            bool consent = EditorPrefs.GetBool(ConsentKey, false);
            page.Add(ConsentCard(consent));
            page.Add(GameCard());
            if (_reportError != null)
                page.Add(Css(new HelpBox(_reportError, HelpBoxMessageType.Error), "gs-card-help"));
            if (_report != null)
                page.Add(ReportCard(_report));
            if (_report != null)
                page.Add(ExtractCard(_report, consent));
            if (_report != null && HasExport())
                page.Add(ExploreCard());
            if (ExtractionsCard() is { } extractions)
                page.Add(extractions);
            page.Add(SettingsCard());

            page.schedule.Execute(() => page.scrollOffset = new Vector2(0, scroll));
        }

        private VisualElement Header()
        {
            var header = Css(new VisualElement(), "gs-header");
            var logo = Css(new Image { image = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath), scaleMode = ScaleMode.ScaleToFit }, "gs-logo");
            header.Add(logo);
            var titles = Css(new VisualElement(), "gs-header-titles");
            titles.Add(Css(new Label("Game Spelunker"), "gs-title"));
            titles.Add(Css(new Label("See how Unity games are built · learning & modding"), "gs-subtitle"));
            header.Add(titles);
            var links = Css(new VisualElement(), "gs-header-links");
            links.Add(IconButton("d__Help", "Documentation", () => Application.OpenURL(DocumentationUrl)));
            links.Add(IconButton("d_DebuggerAttached", "Report a bug", () => Application.OpenURL(RepositoryUrl + "/issues/new/choose")));
            links.Add(IconButton("d_Refresh", "Refresh", Rebuild));
            header.Add(links);
            return header;
        }

        private VisualElement ConsentCard(bool consent)
        {
            var card = Card("d_console.warnicon", "Before you dig", consent ? ("Accepted", "gs-chip--ok") : ("Required", "gs-chip--warn"));
            card.AddToClassList("gs-card--notice");
            card.Add(Css(new Label(
                "Only extract games you own. Extracted assets and code still belong to their authors: use them to learn or to " +
                "build mods the developer allows, never redistribute them, and respect the game's license (EULA). " +
                "Online games with anti-cheat may ban accounts for modified files."), "gs-text"));
            card.Add(Css(new Label(
                $"Extraction is done by AssetRipper (GPL-3.0). It is not bundled: the first extraction downloads the official " +
                $"release {AssetRipperInstaller.Version} ({AssetRipperInstaller.DownloadSize}) from github.com/AssetRipper into your user folder."), "gs-text"));
            var toggle = new Toggle("I understand and will respect these rules") { value = consent };
            toggle.AddToClassList("gs-consent");
            toggle.RegisterValueChangedCallback(e => { EditorPrefs.SetBool(ConsentKey, e.newValue); Rebuild(); });
            card.Add(toggle);
            return card;
        }

        private VisualElement GameCard()
        {
            var card = Card("d_Folder Icon", "1 · Game", _report != null ? (_report.Name, "gs-chip--info") : ((string, string)?)null);
            var pathField = new TextField { value = _gamePath, isDelayed = true };
            pathField.AddToClassList("gs-path");
            pathField.RegisterValueChangedCallback(e => SelectGame(e.newValue));
            card.Add(Row("Game folder", pathField));
            card.Add(Buttons(
                Button("Choose folder…", () =>
                {
                    string folder = EditorUtility.OpenFolderPanel("Unity game folder (with the .exe and *_Data)", _gamePath, "");
                    if (!string.IsNullOrEmpty(folder)) SelectGame(folder);
                }, icon: "d_FolderOpened Icon"),
                Button(_found == null ? "Find Unity games on this PC" : "Search again", () =>
                {
                    _found = GameFinder.FindInstalledUnityGames();
                    Rebuild();
                }, icon: "d_Search Icon")));

            if (_found != null)
            {
                if (_found.Count == 0)
                    card.Add(Hint("No Unity games found in Steam, Epic, GOG or itch.io folders. Use \"Choose folder…\"."));
                else
                    card.Add(GameListTools(card));
                var list = Css(new VisualElement { name = "game-list" }, "gs-game-list");
                card.Add(list);
                RefreshGameList(card);
            }
            return card;
        }

        private VisualElement ReportCard(BuildReport r)
        {
            var card = Card("d_UnityEditor.InspectorWindow", "2 · Build report", (r.Backend == ScriptingBackend.IL2CPP ? "IL2CPP" : r.Backend == ScriptingBackend.Mono ? "Mono" : "Unknown",
                r.Backend == ScriptingBackend.Mono ? "gs-chip--ok" : "gs-chip--warn"));
            var icon = GameIcon(_gamePath);
            if (r.Company != null || icon != null)
            {
                var head = Css(new VisualElement(), "gs-report-head");
                if (icon != null)
                    head.Add(Css(new Image { image = icon, scaleMode = ScaleMode.ScaleAndCrop }, "gs-report-icon"));
                head.Add(Css(new Label(r.Company != null ? $"{r.Name} — {r.Company}" : r.Name), "gs-report-title"));
                card.Add(head);
            }

            string thisEditor = Application.unityVersion;
            string versionDetail = r.UnityVersion == null ? "unknown"
                : r.UnityVersion == thisEditor ? "same as this editor"
                : ExtractedProject.FindEditor(r.UnityVersion) != null ? "installed" : "not installed";

            var tiles = Css(new VisualElement(), "gs-tiles");
            tiles.Add(Tile("d_UnityLogo", "Unity", r.UnityVersion ?? "?", versionDetail));
            tiles.Add(Tile("d_cs Script Icon", "Scripting", r.Backend.ToString(), r.Backend == ScriptingBackend.Mono ? "full decompilation" : r.Backend == ScriptingBackend.IL2CPP ? "signatures only" : ""));
            tiles.Add(Tile("d_Light Icon", "Render pipeline", r.RenderPipeline.Split(' ')[0], r.RenderPipeline.Contains("(") ? r.RenderPipeline.Substring(r.RenderPipeline.IndexOf('(')).Trim('(', ')') : ""));
            tiles.Add(Tile("d_SceneAsset Icon", "Scenes", r.CompressedData ? "?" : r.SceneCount.ToString(), r.CompressedData ? "compressed build" : ""));
            tiles.Add(Tile("d_SaveAs", "Size", BuildInspector.FormatSize(r.TotalSize), ""));
            tiles.Add(Tile("d_Package Manager", "Asset bundles", r.AssetBundleCount.ToString(), r.StreamingAssetsSize > 0 ? BuildInspector.FormatSize(r.StreamingAssetsSize) + " streaming" : ""));
            card.Add(tiles);

            card.Add(ChipRow("Game assemblies", r.GameAssemblies, "gs-chip--info", "none"));
            if (r.NativePlugins.Count > 0)
                card.Add(ChipRow("Native plugins", r.NativePlugins, "gs-chip--muted", ""));
            card.Add(ChipRow("Mod support", r.ModLoaders, "gs-chip--ok", "no mod loader installed"));

            if (r.UnityVersion != null && versionDetail == "not installed")
            {
                card.Add(Hint($"Opening the extracted project with the game's own Unity {r.UnityVersion} keeps the game's code and libraries working as they are. " +
                              (UnityEditorInstaller.CanInstall ? "Game Spelunker can install it with Unity Hub." : "Install Unity Hub (unity.com/download) so Game Spelunker can install it.")));
                var install = Button($"Install Unity {r.UnityVersion}", () => InstallEditor(r.UnityVersion), "gs-button--primary", "d_Download-Available");
                install.SetEnabled(!_busy && UnityEditorInstaller.CanInstall);
                card.Add(Buttons(install));
            }
            else if (r.UnityVersion != null && versionDetail == "installed" && UnityEditorInstaller.NeedsLegacyActivation(r.UnityVersion))
                card.Add(ActivationHint(r.UnityVersion));
            if (_editorMessage.Length > 0)
                card.Add(Css(new HelpBox(_editorMessage, _editorMessage.StartsWith("Failed", StringComparison.Ordinal) ? HelpBoxMessageType.Error : HelpBoxMessageType.Info), "gs-card-help"));
            foreach (var warning in r.Warnings)
                card.Add(Css(new HelpBox(warning, HelpBoxMessageType.Info), "gs-card-help"));
            card.Add(Buttons(Button("Open game folder", () => EditorUtility.RevealInFinder(r.Executable ?? r.DataFolder), icon: "d_FolderOpened Icon")));
            return card;
        }

        private VisualElement AssetRipperSection()
        {
            string? exe = AssetRipperInstaller.Executable;
            bool manual = exe == null && !AssetRipperInstaller.CanAutoInstall;
            var section = new Foldout
            {
                text = exe != null ? $"AssetRipper · ready"
                    : manual ? "AssetRipper · choose its executable"
                    : $"AssetRipper · downloaded on the first extraction ({AssetRipperInstaller.DownloadSize})",
                value = manual,
            };
            section.AddToClassList("gs-log-foldout");

            if (exe != null)
                section.Add(Row("Executable", Css(new Label(exe), "gs-mono")));
            else if (manual)
                section.Add(Hint("AssetRipper has no automatic download for this computer. Download it from the release page and select its executable."));

            var buttons = new List<Button>();
            if (exe == null && !manual)
                buttons.Add(Button("Install now", InstallAssetRipper, icon: "d_Download-Available"));
            if (AssetRipperInstaller.CustomExecutable != null)
                buttons.Add(Button("Use the managed install instead", () => { AssetRipperInstaller.CustomExecutable = null; Rebuild(); }));
            buttons.Add(Button("Use existing…", () =>
            {
                string extension = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "";
                string file = EditorUtility.OpenFilePanel("AssetRipper executable", "", extension);
                if (!string.IsNullOrEmpty(file)) { AssetRipperInstaller.CustomExecutable = file; Rebuild(); }
            }, icon: "d_FolderOpened Icon"));
            buttons.Add(Button("Release page", () => Application.OpenURL(AssetRipperInstaller.ReleasePage), icon: "d_BuildSettings.Web.Small"));
            var row = Buttons(buttons.ToArray());
            row.SetEnabled(!_busy);
            section.Add(row);
            return section;
        }

        private VisualElement ExtractCard(BuildReport report, bool consent)
        {
            var card = Card("d_Import", "3 · Extract", _busy ? ("Working…", "gs-chip--info") : ((string, string)?)null);

            var modes = Css(new VisualElement(), "gs-modes");
            modes.Add(ModeOption(ExportMode.UnityProject, "d_UnityEditor.SceneHierarchyWindow", "Unity project", "Scenes, prefabs, materials and decompiled scripts you can open in Unity."));
            modes.Add(ModeOption(ExportMode.PrimaryContent, "d_Texture Icon", "Raw assets", "Plain files: PNG textures, models, sounds and text."));
            card.Add(modes);

            if (_output.Length == 0)
                _output = DefaultOutput(report);
            var output = new TextField { value = _output, isDelayed = true };
            output.AddToClassList("gs-path");
            output.RegisterValueChangedCallback(e => _output = e.newValue);
            card.Add(Row("Output folder", output));
            if (_mode == ExportMode.UnityProject)
            {
                var importToggle = new Toggle("Import into Unity and repair right away (recommended)") { value = _importInUnity };
                importToggle.tooltip = "Opens the extracted project in Unity in the background, imports every asset and repairs the materials, " +
                                       "so it is ready the moment you open it. Large games take several minutes.";
                importToggle.RegisterValueChangedCallback(e => _importInUnity = e.newValue);
                importToggle.SetEnabled(!_busy);
                card.Add(importToggle);
            }

            bool ripperAvailable = AssetRipperInstaller.Executable != null || AssetRipperInstaller.CanAutoInstall;
            var queue = Button(_busy ? "Extract after the current one" : "Add to queue", () => Enqueue(report), icon: "d_Toolbar Plus");
            queue.SetEnabled(consent && ripperAvailable && !IsQueued(_output));
            if (_busy)
            {
                card.Add(Css(new ProgressBar { value = _progress * 100, title = _busyText }, "gs-progress"));
                card.Add(Buttons(Button("Cancel", () => _cts?.Cancel(), "gs-button--danger", "d_winbtn_win_close"), queue));
            }
            else
            {
                var extract = Button("Extract", Extract, "gs-button--primary gs-button--big", "d_PlayButton");
                extract.SetEnabled(consent && ripperAvailable);
                card.Add(Buttons(extract, queue, Button("Choose output…", () =>
                {
                    string folder = EditorUtility.OpenFolderPanel("Where to put the extracted game", _output, "");
                    if (!string.IsNullOrEmpty(folder)) { _output = folder; Rebuild(); }
                }, icon: "d_FolderOpened Icon")));
                if (!consent) card.Add(Hint("Accept the notice at the top to enable extraction."));
                else if (!ripperAvailable) card.Add(Hint("Select an AssetRipper executable below first."));
            }
            if (_stages.Count > 0 && !_busyInExplore)
                card.Add(StagesView());
            if (_queue.Count > 0)
                card.Add(QueueView());
            card.Add(AssetRipperSection());

            if (_message.Length > 0)
                card.Add(Css(new HelpBox(_message, _message.StartsWith("Failed", StringComparison.Ordinal) ? HelpBoxMessageType.Error : HelpBoxMessageType.Info), "gs-card-help"));

            if (_log.Count > 0)
            {
                var log = new Foldout { text = "AssetRipper log", value = _busy };
                log.AddToClassList("gs-log-foldout");
                log.Add(Css(new Label(string.Join("\n", _log.Skip(Math.Max(0, _log.Count - 12)))), "gs-log"));
                card.Add(log);
            }
            return card;
        }

        private VisualElement ModeOption(ExportMode mode, string icon, string title, string description)
        {
            var option = Css(new Button(() => { _mode = mode; Rebuild(); }), "gs-mode");
            if (_mode == mode) option.AddToClassList("gs-mode--selected");
            option.Add(Css(new Image { image = Icon(icon) }, "gs-mode-icon"));
            var text = Css(new VisualElement(), "gs-mode-text");
            text.Add(Css(new Label(title), "gs-mode-title"));
            text.Add(Css(new Label(description), "gs-mode-description"));
            option.Add(text);
            return option;
        }

        private bool HasExport() =>
            _output.Length > 0 && (Directory.Exists(Path.Combine(_output, "ExportedProject")) || Directory.Exists(Path.Combine(_output, "Assets")));

        private VisualElement ExploreCard()
        {
            var card = Card("d_ViewToolZoom", "4 · Explore", ("Extracted", "gs-chip--ok"));
            string? version = ExtractedProject.UnityVersionOf(_output);
            string? editor = ExtractedProject.FindEditor(version);
            card.Add(Buttons(
                Button(editor != null ? $"Open in Unity {version}" : "Open in this Unity", () => ExtractedProject.OpenInUnity(_output), "gs-button--primary", "d_UnityLogo"),
                Button("Open folder", () => EditorUtility.RevealInFinder(ExtractedProject.ProjectFolder(_output)), icon: "d_FolderOpened Icon")));
            if (version != null && editor == null)
                card.Add(Hint($"The game was made with Unity {version}, which is not installed. Another version works for exploring, but Unity will upgrade the project."));
            else if (version != null && UnityEditorInstaller.NeedsLegacyActivation(version))
                card.Add(ActivationHint(version));

            card.Add(HealthSection());
            string pluginsFile = Path.Combine(_output, "AuxiliaryFiles", "GameSpelunker-plugins.txt");
            if (File.Exists(pluginsFile))
            {
                var plugins = File.ReadAllLines(pluginsFile).Where(l => l.Contains(" — ")).Select(l => l.Split(new[] { " — " }, StringSplitOptions.None)[0]).ToList();
                if (plugins.Count > 0)
                    card.Add(ChipRow("Plugins & middleware", plugins, "gs-chip--info", ""));
            }
            card.Add(ImportSection(version));

            string assets = Path.Combine(ExtractedProject.ProjectFolder(_output), "Assets");
            if (Directory.Exists(assets))
            {
                var types = Directory.GetDirectories(assets)
                    .Where(d => Path.GetFileName(d) != ExtractedProject.ToolsFolder)
                    .Select(d => (name: Path.GetFileName(d), count: Directory.GetFiles(d, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta", StringComparison.Ordinal))))
                    .Where(t => t.count > 0)
                    .OrderByDescending(t => t.count)
                    .ToList();
                card.Add(Css(new Label("What's inside"), "gs-subsection"));
                var grid = Css(new VisualElement(), "gs-type-grid");
                foreach (var (name, count) in types)
                {
                    string folder = Path.Combine(assets, name);
                    var chip = Css(new Button(() => EditorUtility.RevealInFinder(folder)) { tooltip = "Show in Explorer" }, "gs-type");
                    chip.Add(Css(new Image { image = Icon(IconForAssetFolder(name)) }, "gs-type-icon"));
                    chip.Add(Css(new Label(name), "gs-type-name"));
                    chip.Add(Css(new Label(count.ToString()), "gs-type-count"));
                    grid.Add(chip);
                }
                card.Add(grid);
                card.Add(ScenesSection(assets));
                card.Add(TexturesSection(assets));
            }

            if (CodeFixesSection() is { } fixes)
                card.Add(fixes);
            var scripts = ExtractedProject.FindScripts(_output);
            card.Add(Css(new Label($"Game scripts · {scripts.Count}"), "gs-subsection"));
            var filter = new ToolbarSearchField { value = _scriptFilter };
            filter.AddToClassList("gs-search");
            filter.RegisterValueChangedCallback(e => { _scriptFilter = e.newValue; RefreshScriptList(card, scripts); });
            card.Add(filter);
            var list = Css(new VisualElement { name = "script-list" }, "gs-script-list");
            card.Add(list);
            RefreshScriptList(card, scripts);
            if (scripts.Count == 0)
                card.Add(Hint("No game scripts in this export (raw assets mode, or the game has none)."));
            return card;
        }

        private static string ExtractionsRoot => Settings.OutputRoot;

        private VisualElement? ExtractionsCard()
        {
            if (!Directory.Exists(ExtractionsRoot))
                return null;
            var folders = Directory.GetDirectories(ExtractionsRoot)
                .Where(d => Directory.Exists(Path.Combine(d, "ExportedProject")) || Directory.Exists(Path.Combine(d, "AuxiliaryFiles")))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .ToList();
            if (folders.Count == 0)
                return null;

            long total = folders.Sum(f => _sizes.TryGetValue(f, out var s) ? s.total : 0);
            var card = Card("d_SaveAs", "Your extractions", (total > 0 ? BuildInspector.FormatSize(total) : folders.Count + " folder(s)", "gs-chip--muted"));
            card.Add(Css(new Label("Each extraction is a full Unity project. The Unity cache (Library folder) is rebuilt the next time the project opens, " +
                                   "so freeing it is safe; deleting removes the extraction for good."), "gs-text"));
            foreach (var folder in folders)
            {
                MeasureLater(folder);
                var row = Css(new VisualElement(), "gs-disk-row");
                string name = Path.GetFileName(folder);
                bool current = string.Equals(Path.GetFullPath(folder).TrimEnd('\\', '/'), _output.Length > 0 ? Path.GetFullPath(_output).TrimEnd('\\', '/') : "", StringComparison.OrdinalIgnoreCase);
                row.Add(Css(new Label(current ? name + "  (selected)" : name), "gs-disk-name"));
                string version = ExtractedProject.UnityVersionOf(folder) ?? "";
                if (version.Length > 0) row.Add(Chip(version, "gs-chip--muted"));
                row.Add(Css(new Label(_sizes.TryGetValue(folder, out var size) ? BuildInspector.FormatSize(size.total) : "…"), "gs-disk-size"));
                var open = Button("Select", () => SelectExtraction(folder), icon: "d_FolderOpened Icon");
                open.SetEnabled(!_busy && !current);
                row.Add(open);
                string library = Path.Combine(ExtractedProject.ProjectFolder(folder), "Library");
                if (_sizes.TryGetValue(folder, out var measured) && measured.library > 0)
                {
                    var free = Button("Free " + BuildInspector.FormatSize(measured.library), () => DeleteFolder(folder, library, "the Unity cache of " + name), icon: "d_TreeEditor.Trash");
                    free.tooltip = "Deletes the Library folder. Unity rebuilds it the next time the project opens (that first open is slower).";
                    free.SetEnabled(!_busy);
                    row.Add(free);
                }
                var delete = Button("Delete", () => DeleteFolder(folder, folder, "the whole extraction " + name), "gs-button--danger", "d_winbtn_win_close");
                delete.SetEnabled(!_busy);
                row.Add(delete);
                card.Add(row);
            }
            return card;
        }

        private void MeasureLater(string folder)
        {
            if (_sizes.ContainsKey(folder) || !_measuring.Add(folder))
                return;
            Task.Run(() =>
            {
                long total = FolderSize(folder);
                long library = FolderSize(Path.Combine(ExtractedProject.ProjectFolder(folder), "Library"));
                RunOnMain(() => { _sizes[folder] = (total, library); _measuring.Remove(folder); });
            });
        }

        private static long FolderSize(string folder)
        {
            if (!Directory.Exists(folder))
                return 0;
            long size = 0;
            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                    size += file.Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return size;
        }

        private void DeleteFolder(string extraction, string target, string what)
        {
            if (IsOpenInUnity(ExtractedProject.ProjectFolder(extraction)))
            {
                EditorUtility.DisplayDialog("Game Spelunker", "This project is open in Unity. Close it first.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Game Spelunker", $"Delete {what}?\n\n{target}\n\nThis cannot be undone.", "Delete", "Cancel"))
                return;
            try
            {
                Directory.Delete(target, true);
                if (string.Equals(target, extraction, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFullPath(extraction), _output.Length > 0 ? Path.GetFullPath(_output) : "", StringComparison.OrdinalIgnoreCase))
                    _output = "";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Game Spelunker", "Could not delete everything: " + ex.Message, "OK");
            }
            _sizes.Remove(extraction);
            Rebuild();
        }

        private void SelectExtraction(string folder)
        {
            string note = Path.Combine(folder, "AuxiliaryFiles", "GameSpelunker-game.txt");
            string? data = File.Exists(note) ? File.ReadAllText(note).Trim() : null;
            string? game = data != null ? Path.GetDirectoryName(data.TrimEnd('\\', '/')) : null;
            if (game != null && Directory.Exists(game))
                SelectGame(game, rebuild: false);
            _output = folder;
            Rebuild();
        }

        private VisualElement HealthSection()
        {
            var box = Css(new VisualElement(), "gs-import");
            var head = Css(new VisualElement(), "gs-card-head");
            head.Add(Css(new Image { image = Icon("d_PlayButton") }, "gs-card-icon"));
            head.Add(Css(new Label("Health"), "gs-subsection"));
            box.Add(head);
            box.Add(Css(new Label("Check the project presses Play in the background for about a minute, takes screenshots and counts errors. " +
                                  "Repair again redoes the preparation, the code fixes and the material repair without extracting the game again."), "gs-text"));

            box.Add(HealthDashboard());
            if (_busy && _busyInExplore)
                box.Add(Css(new ProgressBar { value = _progress * 100, title = _busyText }, "gs-progress"));

            var check = Button("Check the project", CheckProject, "gs-button--primary", "d_PlayButton");
            var repair = Button("Repair again", RepairAgain, icon: "d_Refresh");
            check.SetEnabled(!_busy);
            repair.SetEnabled(!_busy);
            var buttons = new List<Button> { check, repair };
            if (_busy && _busyInExplore)
                buttons.Add(Button("Cancel", () => _cts?.Cancel(), "gs-button--danger", "d_winbtn_win_close"));
            box.Add(Buttons(buttons.ToArray()));
            if (_exploreMessage.Length > 0)
                box.Add(Css(new HelpBox(_exploreMessage, _exploreMessage.StartsWith("Failed", StringComparison.Ordinal) ? HelpBoxMessageType.Error : HelpBoxMessageType.Info), "gs-card-help"));
            return box;
        }

        private void RunInExplore(string start, Func<CancellationToken, Task<string>> work)
        {
            if (IsOpenInUnity(ExtractedProject.ProjectFolder(_output)))
            {
                _exploreMessage = "Failed: the project is open in Unity. Close it first.";
                Rebuild();
                return;
            }
            _busy = true;
            _busyInExplore = true;
            _busyText = start;
            _progress = 0;
            _exploreMessage = "";
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    string done = await work(token).ConfigureAwait(false);
                    RunOnMain(() => _exploreMessage = done);
                }
                catch (Exception ex) when (token.IsCancellationRequested && (ex is OperationCanceledException || ex is ObjectDisposedException))
                {
                    RunOnMain(() => _exploreMessage = "Cancelled.");
                }
                catch (Exception ex)
                {
                    RunOnMain(() => _exploreMessage = "Failed: " + ex.Message);
                }
                finally
                {
                    RunOnMain(() => { _busy = false; _busyInExplore = false; });
                }
            });
            Rebuild();
        }

        private static bool IsOpenInUnity(string project) => File.Exists(Path.Combine(project, "Temp", "UnityLockfile")) && IsLocked(Path.Combine(project, "Temp", "UnityLockfile"));

        private static bool IsLocked(string file)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        private void CheckProject()
        {
            string output = _output;
            string editor = ExtractedProject.EditorFor(output, EditorApplication.applicationPath);
            string tools = ExtractedProject.ToolsSource();
            RunInExplore("Pressing Play in the background… (opening a large project takes a few minutes)", async token =>
            {
                var result = await ProjectChecker.CheckAsync(output, editor, tools, token).ConfigureAwait(false);
                return result.Verdict + ".";
            });
        }

        private void RepairAgain()
        {
            string output = _output;
            string editor = ExtractedProject.EditorFor(output, EditorApplication.applicationPath);
            string tools = ExtractedProject.ToolsSource();
            GameCodeCompiler.Passes = Settings.CompilePasses;
            RunInExplore("Preparing the project…", async token =>
            {
                var prepared = ExtractedProject.PrepareForOpening(output, tools);
                string codeReport = "";
                if (prepared.HasGameCode)
                {
                    var code = await GameCodeCompiler.MakeItCompileAsync(output, editor,
                        new Progress<string>(m => RunOnMain(() => { _progress = 0f; _busyText = m; })), token).ConfigureAwait(false);
                    codeReport = code.Compiles ? " The game's code compiles." : $" The game's code still has {code.RemainingErrors} compile error(s).";
                }
                var importProgress = new Progress<float>(p => RunOnMain(() => { _progress = p; _busyText = $"Importing into Unity and repairing… {p:P0}"; }));
                string summary = await ExtractedProject.ImportAndRepairAsync(output, editor, importProgress, token).ConfigureAwait(false);
                return "Repaired: " + summary + codeReport;
            });
        }

        private VisualElement ImportSection(string? exportVersion)
        {
            var box = Css(new VisualElement(), "gs-import");
            var head = Css(new VisualElement(), "gs-card-head");
            head.Add(Css(new Image { image = Icon("d_Import") }, "gs-card-icon"));
            head.Add(Css(new Label("Import into this project"), "gs-subsection"));
            box.Add(head);
            box.Add(Css(new Label("Copies the extracted assets into a folder of the project you have open, so you can use them in " +
                                  "your own scenes. Engine shaders, native plugins and project settings are never imported."), "gs-text"));

            if (_importTarget.Length == 0 && _report != null)
                _importTarget = ProjectImporter.DefaultTarget(_report.Name);
            var target = new TextField { value = _importTarget, isDelayed = true };
            target.AddToClassList("gs-path");
            target.RegisterValueChangedCallback(e => _importTarget = e.newValue);
            box.Add(Row("Into folder", target));

            var scenes = new Toggle("Scenes") { value = _importScenes };
            scenes.RegisterValueChangedCallback(e => _importScenes = e.newValue);
            box.Add(scenes);
            var scripts = new Toggle("Decompiled scripts (isolated, not compiled until you enable them)") { value = _importScripts };
            scripts.RegisterValueChangedCallback(e => { _importScripts = e.newValue; Rebuild(); });
            box.Add(scripts);
            if (_importScripts && _report != null)
                box.Add(Hint($"Scripts go into their own assembly that only compiles when the scripting define " +
                             $"{ProjectImporter.CompileDefine(_report.Name)} is set (Project Settings → Player → Scripting Define Symbols). " +
                             "Decompiled code often needs fixes before it compiles."));

            if (exportVersion != null && IsNewer(exportVersion, Application.unityVersion))
                box.Add(Css(new HelpBox($"The game was made with Unity {exportVersion}, newer than this editor ({Application.unityVersion}). " +
                                        "Some assets may not import correctly.", HelpBoxMessageType.Warning), "gs-card-help"));

            box.Add(Buttons(Button("Import into this project", ImportIntoProject, "gs-button--primary", "d_Import")));
            if (_importMessage.Length > 0)
                box.Add(Css(new HelpBox(_importMessage, _importMessage.StartsWith("Failed", StringComparison.Ordinal) ? HelpBoxMessageType.Error : HelpBoxMessageType.Info), "gs-card-help"));
            return box;
        }

        private void ImportIntoProject()
        {
            if (_report == null) return;
            string target = _importTarget.Replace('\\', '/').TrimEnd('/');
            if (Directory.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, target)) &&
                !EditorUtility.DisplayDialog("Game Spelunker", $"{target} already exists. Copy into it anyway (files with the same name are replaced)?", "Import", "Cancel"))
                return;
            try
            {
                var result = ProjectImporter.Import(_output, _report.Name, new ImportOptions
                {
                    TargetFolder = target,
                    IncludeScenes = _importScenes,
                    IncludeScripts = _importScripts,
                });
                var lines = new List<string> { $"Imported {result.FilesCopied} file(s) into {result.TargetFolder}." };
                if (result.GuidConflicts > 0)
                    lines.Add($"{result.GuidConflicts} file(s) skipped: their GUID is already used in this project.");
                if (result.SkippedFolders.Count > 0)
                    lines.Add("Not imported: " + string.Join(", ", result.SkippedFolders) + ".");
                if (result.ScriptsDefine != null)
                    lines.Add($"Scripts are not compiled until you add the define {result.ScriptsDefine}.");
                if (result.Materials != null)
                    lines.Add(result.Materials.Summary);
                if (result.MissingPackages.Count > 0)
                    lines.Add("The game used packages this project does not have: " + string.Join(", ", result.MissingPackages) +
                              ". Add them with the Package Manager if some assets look broken.");
                _importMessage = string.Join("\n", lines);
                var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.TargetFolder);
                if (folder != null) EditorGUIUtility.PingObject(folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                _importMessage = "Failed: " + ex.Message;
            }
            Rebuild();
        }

        private static bool IsNewer(string a, string b)
        {
            static int[] Parts(string v) => System.Text.RegularExpressions.Regex.Matches(v, @"\d+").Cast<System.Text.RegularExpressions.Match>().Select(m => int.Parse(m.Value)).ToArray();
            var x = Parts(a);
            var y = Parts(b);
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                if (x[i] != y[i]) return x[i] > y[i];
            return x.Length > y.Length;
        }

        private void RefreshScriptList(VisualElement card, List<ExtractedScript> scripts)
        {
            var list = card.Q<VisualElement>("script-list");
            list.Clear();
            foreach (var script in scripts.Where(s => _scriptFilter.Length == 0 || s.RelativePath.IndexOf(_scriptFilter, StringComparison.OrdinalIgnoreCase) >= 0).Take(300))
            {
                var item = Css(new Button(() => InternalEditorUtility.OpenFileAtLineExternal(script.Path, 1)) { tooltip = "Open in your code editor" }, "gs-script");
                item.Add(Css(new Image { image = Icon("d_cs Script Icon") }, "gs-script-icon"));
                item.Add(Css(new Label(script.ClassName), "gs-script-name"));
                string folder = Path.GetDirectoryName(script.RelativePath)?.Replace('\\', '/') ?? "";
                item.Add(Css(new Label(folder), "gs-script-path"));
                list.Add(item);
            }
        }

        private static string IconForAssetFolder(string folder)
        {
            string f = folder.ToLowerInvariant();
            if (f.Contains("scene")) return "d_SceneAsset Icon";
            if (f.Contains("prefab") || f == "gameobject") return "d_Prefab Icon";
            if (f.Contains("script") || f.Contains("monoscript")) return "d_cs Script Icon";
            if (f.Contains("texture") || f.Contains("sprite")) return "d_Texture Icon";
            if (f.Contains("material")) return "d_Material Icon";
            if (f.Contains("mesh") || f.Contains("model")) return "d_Mesh Icon";
            if (f.Contains("audio") || f.Contains("sound")) return "d_AudioClip Icon";
            if (f.Contains("anim")) return "d_AnimationClip Icon";
            if (f.Contains("shader") || f.Contains("compute")) return "d_Shader Icon";
            if (f.Contains("font")) return "d_Font Icon";
            if (f.Contains("text") || f.Contains("resources")) return "d_TextAsset Icon";
            return "d_DefaultAsset Icon";
        }

        public void ShowForCapture(string game, string exportFolder)
        {
            SelectGame(game, rebuild: false);
            _output = exportFolder;
            _found = null;
            Rebuild();
        }

        public void ScrollTo(bool end)
        {
            var page = rootVisualElement.Q<ScrollView>();
            if (page != null) page.scrollOffset = new Vector2(0, end ? float.MaxValue : 0f);
        }

        private void SelectGame(string path, bool rebuild = true)
        {
            _gamePath = path;
            _report = null;
            _reportError = null;
            _output = "";
            _message = "";
            _importTarget = "";
            _importMessage = "";
            _editorMessage = "";
            _exploreMessage = "";
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
            return Path.Combine(Settings.OutputRoot, safe);
        }

        private void InstallAssetRipper()
        {
            _busy = true;
            _busyText = DownloadText;
            _progress = 0;
            _message = "";
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = DownloadProgress();
            Task.Run(async () =>
            {
                try
                {
                    await AssetRipperInstaller.EnsureInstalledAsync(null, progress, token).ConfigureAwait(false);
                    RunOnMain(() => _message = "AssetRipper installed.");
                }
                catch (Exception ex) when (token.IsCancellationRequested && ex is OperationCanceledException)
                {
                    RunOnMain(() => _message = "Cancelled.");
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

        private VisualElement ActivationHint(string version)
        {
            var box = Css(new VisualElement(), "gs-import");
            box.Add(Css(new HelpBox($"Unity {version} uses the old license system and is not activated yet, so it cannot import or repair in the background. " +
                                    "Open it once, sign in, choose a Personal license, then close it.", HelpBoxMessageType.Warning), "gs-card-help"));
            box.Add(Buttons(Button($"Open Unity {version} to activate it", () =>
            {
                try { UnityEditorInstaller.OpenOnceToActivate(version); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { _editorMessage = "Failed: " + ex.Message; }
                Rebuild();
            }, icon: "d_UnityLogo"), Button("Done, check again", Rebuild, icon: "d_Refresh")));
            return box;
        }

        private void InstallEditor(string version)
        {
            _busy = true;
            _busyText = $"Installing Unity {version}…";
            _editorMessage = $"Installing Unity {version} with Unity Hub…";
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = new Progress<string>(m => RunOnMain(() => { _busyText = m; _editorMessage = m; }));
            Task.Run(async () =>
            {
                try
                {
                    string editor = await UnityEditorInstaller.InstallAsync(version, progress, token).ConfigureAwait(false);
                    RunOnMain(() => _editorMessage = $"Unity {version} is installed ({editor})." +
                                                     (UnityEditorInstaller.NeedsLegacyActivation(version) ? " It still needs to be activated once (see below)." : ""));
                }
                catch (Exception ex) when (token.IsCancellationRequested && ex is OperationCanceledException)
                {
                    RunOnMain(() => _editorMessage = "Cancelled.");
                }
                catch (Exception ex)
                {
                    RunOnMain(() => _editorMessage = "Failed to install Unity " + version + ": " + ex.Message);
                }
                finally
                {
                    RunOnMain(() => _busy = false);
                }
            });
            Rebuild();
        }

        private static string DownloadText =>$"Downloading AssetRipper {AssetRipperInstaller.Version} ({AssetRipperInstaller.DownloadSize})…";

        private IProgress<float> DownloadProgress() => new Progress<float>(p => RunOnMain(() =>
        {
            _progress = p;
            _busyText = DownloadText;
        }));

        private void Extract() => Extract(true);

        private void Extract(bool ask)
        {
            if (_report == null)
                return;

            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any())
            {
                if (!IsPreviousExport(_output))
                {
                    _message = "Failed: the output folder is not empty. Choose an empty folder.";
                    Rebuild();
                    return;
                }
                if (ask && !EditorUtility.DisplayDialog("Game Spelunker", $"Replace the previous extraction in\n{_output} ?", "Replace", "Cancel"))
                    return;
                if (IsOpenInUnity(ExtractedProject.ProjectFolder(_output)))
                {
                    _message = "Failed: the previous extraction is open in Unity. Close it first.";
                    Rebuild();
                    return;
                }
                Directory.Delete(_output, true);
                _sizes.Remove(_output);
            }

            var report = _report;
            string output = _output;
            var mode = _mode;
            string? installed = AssetRipperInstaller.Executable;
            string tools = ExtractedProject.ToolsSource();
            string thisEditor = EditorApplication.applicationPath;
            bool importInUnity = _importInUnity && mode == ExportMode.UnityProject;
            bool check = importInUnity && Settings.CheckAfterExtracting;
            var exportSettings = new ExportSettings
            {
                KeepCompiledShaders = Settings.KeepCompiledShaders && ExtractedProject.FindEditor(report.UnityVersion) != null,
                OriginalAssemblies = Settings.OriginalAssemblies,
            };
            GameCodeCompiler.Passes = Settings.CompilePasses;
            bool download = installed == null;
            _busy = true;
            _busyInExplore = false;
            _busyText = download ? DownloadText : "Starting AssetRipper…";
            _progress = 0;
            _message = "";
            _log.Clear();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = DownloadProgress();
            var stages = new List<string>();
            if (download) stages.Add(StageDownload);
            stages.Add(StageExport);
            if (mode == ExportMode.UnityProject) stages.Add(StagePrepare);
            if (importInUnity) { stages.Add(StageCode); stages.Add(StageImport); }
            if (check) stages.Add(StageCheck);
            BeginStages(report.Name, stages);
            if (download) StartStage(StageDownload);

            Task.Run(async () =>
            {
                bool success = false;
                string exe;
                try
                {
                    exe = await AssetRipperInstaller.EnsureInstalledAsync(installed, progress, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RunOnMain(() =>
                    {
                        _message = token.IsCancellationRequested ? "Cancelled." : "Failed to install AssetRipper: " + ex.Message;
                        FailStage(token.IsCancellationRequested ? "Cancelled" : ex.Message);
                        _busy = false;
                        FinishRun(report.Name, false, token.IsCancellationRequested);
                    });
                    return;
                }
                RunOnMain(() => { _progress = 0; _busyText = "Starting AssetRipper…"; StartStage(StageExport); });
                var started = DateTime.Now;

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
                    await session.ConfigureAsync(exportSettings, token).ConfigureAwait(false);
                    RunOnMain(() => _busyText = "Loading game files…");
                    await session.LoadAsync(report.LoadPath, token).ConfigureAwait(false);
                    RunOnMain(() => _busyText = "Exporting…");
                    await session.ExportAsync(output, mode, token).ConfigureAwait(false);
                    var seconds = (DateTime.Now - started).TotalSeconds;
                    string done = $"Extracted in {seconds:0} s to {output}.";
                    if (mode == ExportMode.UnityProject)
                    {
                        session.Dispose();
                        RunOnMain(() => { _progress = 0; _busyText = "Preparing the project…"; StartStage(StagePrepare); });
                        var prepared = ExtractedProject.PrepareForOpening(output, tools, report.DataFolder);
                        if (importInUnity)
                        {
                            string editor = ExtractedProject.EditorFor(output, thisEditor);
                            var importProgress = new Progress<float>(p => RunOnMain(() =>
                            {
                                _progress = p;
                                _busyText = $"Importing into Unity and repairing… {p:P0} (large games take several minutes)";
                            }));
                            string codeReport = "";
                            RunOnMain(() => { _progress = 0; StartStage(StageCode); });
                            if (prepared.HasGameCode)
                            {
                                var compileProgress = new Progress<string>(m => RunOnMain(() => { _progress = 0f; _busyText = m; }));
                                var code = await GameCodeCompiler.MakeItCompileAsync(output, editor, compileProgress, token).ConfigureAwait(false);
                                codeReport = code.Compiles
                                    ? $" The game's code compiles ({code.MembersStubbed} member(s) that could not be decompiled were stubbed, " +
                                      $"{code.FilesQuarantined} file(s) set aside; see AuxiliaryFiles/GameSpelunker-code-fixes.txt)."
                                    : $" The game's code still has {code.RemainingErrors} compile error(s) and is switched off (see AuxiliaryFiles/GameSpelunker-code-fixes.txt).";
                            }
                            else
                            {
                                RunOnMain(() => SkipStage(StageCode));
                            }
                            RunOnMain(() => { _progress = 0; StartStage(StageImport); });
                            string summary = await ExtractedProject.ImportAndRepairAsync(output, editor, importProgress, token).ConfigureAwait(false);
                            done += " Imported into Unity and repaired, ready to open: " + summary + codeReport;
                            if (check)
                            {
                                RunOnMain(() => { _progress = 0; _busyText = "Pressing Play in the background…"; StartStage(StageCheck); });
                                var result = await ProjectChecker.CheckAsync(output, editor, tools, token).ConfigureAwait(false);
                                done += " Check: " + result.Verdict + ".";
                            }
                        }
                        else
                        {
                            done += " Materials are repaired the first time the project opens." +
                                    (prepared.GameCodeOff ? " The game's code is switched off until you enable it (Tools → Game Spelunker → Enable Game Code)." : "");
                        }
                    }
                    success = true;
                    RunOnMain(() => { _message = done; CompleteStages(); });
                }
                catch (Exception ex) when (token.IsCancellationRequested && (ex is OperationCanceledException || ex is ObjectDisposedException || ex is System.Net.Http.HttpRequestException))
                {
                    RunOnMain(() => { _message = "Cancelled."; FailStage("Cancelled"); });
                }
                catch (Exception ex)
                {
                    RunOnMain(() => { _message = "Failed: " + ex.Message; FailStage(ex.Message); });
                }
                finally
                {
                    bool cancelled = token.IsCancellationRequested;
                    RunOnMain(() =>
                    {
                        _busy = false;
                        FinishRun(report.Name, success, cancelled);
                    });
                }
            });
            Rebuild();
        }

        private static bool IsPreviousExport(string folder) =>
            Directory.Exists(Path.Combine(folder, "ExportedProject")) || Directory.Exists(Path.Combine(folder, "AuxiliaryFiles"))
            || (Directory.Exists(Path.Combine(folder, "Assets")) && Directory.Exists(Path.Combine(folder, "ProjectSettings")));

        private static T Css<T>(T element, string classNames) where T : VisualElement
        {
            foreach (var c in classNames.Split(' '))
                if (c.Length > 0) element.AddToClassList(c);
            return element;
        }

        private static Texture? Icon(string name)
        {
            string light = name.StartsWith("d_", StringComparison.Ordinal) ? name.Substring(2) : name;
            string wanted = EditorGUIUtility.isProSkin ? "d_" + light : light;
            var previous = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try
            {
                return EditorGUIUtility.IconContent(wanted)?.image ?? EditorGUIUtility.IconContent(light)?.image;
            }
            finally
            {
                Debug.unityLogger.logEnabled = previous;
            }
        }

        private static VisualElement Card(string icon, string title, (string text, string style)? chip = null)
        {
            var card = Css(new VisualElement(), "gs-card");
            var head = Css(new VisualElement(), "gs-card-head");
            head.Add(Css(new Image { image = Icon(icon) }, "gs-card-icon"));
            head.Add(Css(new Label(title), "gs-card-title"));
            if (chip is { } c)
                head.Add(Chip(c.text, c.style));
            card.Add(head);
            return card;
        }

        private static Label Chip(string text, string style) => Css(new Label(text), "gs-chip " + style);

        private static VisualElement Tile(string icon, string label, string value, string detail)
        {
            var tile = Css(new VisualElement(), "gs-tile");
            var top = Css(new VisualElement(), "gs-tile-top");
            top.Add(Css(new Image { image = Icon(icon) }, "gs-tile-icon"));
            top.Add(Css(new Label(label), "gs-tile-label"));
            tile.Add(top);
            tile.Add(Css(new Label(value), "gs-tile-value"));
            if (detail.Length > 0)
                tile.Add(Css(new Label(detail), "gs-tile-detail"));
            return tile;
        }

        private static VisualElement ChipRow(string label, IReadOnlyCollection<string> items, string style, string empty)
        {
            var row = Css(new VisualElement(), "gs-row");
            row.Add(Css(new Label(label), "gs-label"));
            var chips = Css(new VisualElement(), "gs-chips gs-field");
            if (items.Count == 0)
                chips.Add(Css(new Label(empty), "gs-muted"));
            foreach (var item in items)
                chips.Add(Chip(item, style));
            row.Add(chips);
            return row;
        }

        private static Label Hint(string text) => Css(new Label(text), "gs-hint");

        private static VisualElement Row(string label, VisualElement field)
        {
            var row = Css(new VisualElement(), "gs-row");
            row.Add(Css(new Label(label), "gs-label"));
            field.AddToClassList("gs-field");
            row.Add(field);
            return row;
        }

        private static Button Button(string text, Action onClick, string? modifier = null, string? icon = null)
        {
            var b = Css(new Button(onClick), "gs-button");
            if (modifier != null) Css(b, modifier);
            if (icon != null && Icon(icon) is { } tex)
                b.Add(Css(new Image { image = tex }, "gs-button-icon"));
            b.Add(Css(new Label(text), "gs-button-label"));
            return b;
        }

        private static Button IconButton(string icon, string tooltip, Action onClick)
        {
            var b = Css(new Button(onClick) { tooltip = tooltip }, "gs-icon-button");
            b.Add(new Image { image = Icon(icon) });
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
