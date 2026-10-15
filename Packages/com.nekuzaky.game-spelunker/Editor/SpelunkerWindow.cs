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
    /// <summary>
    /// Tools → Game Spelunker: pick a Unity game, read its build report, extract it with AssetRipper and explore the
    /// result (decompiled scripts, asset types). Meant for learning how games are built and for modding games that allow it.
    /// </summary>
    public sealed class SpelunkerWindow : EditorWindow
    {
        /// <summary>Asset path of this package, whether it is embedded, installed from git or from a registry.</summary>
        public const string PackageRoot = "Packages/com.nekuzaky.game-spelunker";
        private const string StylePath = PackageRoot + "/Editor/Spelunker.uss";
        private const string ConsentKey = "GameSpelunker.Consent";
        private const string LastGameKey = "GameSpelunker.LastGame";
        private const string IconPath = PackageRoot + "/Editor/Icons/GameSpelunker.png";
        private const string TabIconPath = PackageRoot + "/Editor/Icons/GameSpelunker-tab.png";
        public const string RepositoryUrl = "https://github.com/Nekuzaky/game-spelunker";

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
        private string _importTarget = "";
        private bool _importScenes = true;
        private bool _importScripts;
        private string _importMessage = "";

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
            page.Add(AssetRipperCard());
            if (_report != null)
                page.Add(ExtractCard(_report, consent));
            if (_report != null && HasExport())
                page.Add(ExploreCard());

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
            links.Add(IconButton("d__Help", "Documentation", () => Application.OpenURL(RepositoryUrl + "#readme")));
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
                var list = Css(new VisualElement(), "gs-game-list");
                foreach (var game in _found)
                {
                    var item = Css(new Button(() => SelectGame(game.Folder)), "gs-game");
                    item.Add(Css(new Image { image = Icon("d_UnityEditor.GameView") }, "gs-game-icon"));
                    item.Add(Css(new Label(game.Name), "gs-game-name"));
                    item.Add(Chip(game.Source, "gs-chip--muted"));
                    if (string.Equals(game.Folder, _gamePath, StringComparison.OrdinalIgnoreCase)) item.AddToClassList("gs-game--selected");
                    list.Add(item);
                }
                card.Add(list);
            }
            return card;
        }

        private VisualElement ReportCard(BuildReport r)
        {
            var card = Card("d_UnityEditor.InspectorWindow", "2 · Build report", (r.Backend == ScriptingBackend.IL2CPP ? "IL2CPP" : r.Backend == ScriptingBackend.Mono ? "Mono" : "Unknown",
                r.Backend == ScriptingBackend.Mono ? "gs-chip--ok" : "gs-chip--warn"));
            if (r.Company != null)
                card.Add(Css(new Label($"{r.Name} — {r.Company}"), "gs-report-title"));

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
                card.Add(Hint($"Install Unity {r.UnityVersion} with Unity Hub to open the extracted project without upgrading it."));
            foreach (var warning in r.Warnings)
                card.Add(Css(new HelpBox(warning, HelpBoxMessageType.Info), "gs-card-help"));
            card.Add(Buttons(Button("Open game folder", () => EditorUtility.RevealInFinder(r.Executable ?? r.DataFolder), icon: "d_FolderOpened Icon")));
            return card;
        }

        private VisualElement AssetRipperCard()
        {
            string? exe = AssetRipperInstaller.Executable;
            var card = Card("d_Download-Available", "3 · AssetRipper", exe != null ? ("Ready", "gs-chip--ok") : ("Not installed", "gs-chip--warn"));
            if (exe != null)
            {
                card.Add(Row("Executable", Css(new Label(exe), "gs-mono")));
                if (AssetRipperInstaller.CustomExecutable != null)
                    card.Add(Buttons(Button("Use the managed install instead", () => { AssetRipperInstaller.CustomExecutable = null; Rebuild(); })));
                return card;
            }

            card.Add(Css(new Label($"Game Spelunker uses AssetRipper (GPL-3.0) to read game files. It is not bundled: install the official " +
                                   $"release {AssetRipperInstaller.Version} ({AssetRipperInstaller.DownloadSize}, from github.com/AssetRipper) " +
                                   "or point to one you already have."), "gs-text"));
            card.Add(Buttons(
                Button($"Install AssetRipper {AssetRipperInstaller.Version}", InstallAssetRipper, "gs-button--primary", "d_Download-Available"),
                Button("Use existing…", () =>
                {
                    string file = EditorUtility.OpenFilePanel("AssetRipper executable", "", "exe");
                    if (!string.IsNullOrEmpty(file)) { AssetRipperInstaller.CustomExecutable = file; Rebuild(); }
                }, icon: "d_FolderOpened Icon"),
                Button("Release page", () => Application.OpenURL(AssetRipperInstaller.ReleasePage), icon: "d_BuildSettings.Web.Small")));
            if (_busy && _busyText.StartsWith("Downloading", StringComparison.Ordinal))
                card.Add(Css(new ProgressBar { value = _progress * 100, title = _busyText }, "gs-progress"));
            return card;
        }

        private VisualElement ExtractCard(BuildReport report, bool consent)
        {
            var card = Card("d_Import", "4 · Extract", _busy && !_busyText.StartsWith("Downloading", StringComparison.Ordinal) ? ("Working…", "gs-chip--info") : ((string, string)?)null);

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

            if (_busy && !_busyText.StartsWith("Downloading", StringComparison.Ordinal))
            {
                card.Add(Css(new ProgressBar { value = _progress * 100, title = _busyText }, "gs-progress"));
                card.Add(Buttons(Button("Cancel", () => _cts?.Cancel(), "gs-button--danger", "d_winbtn_win_close")));
            }
            else
            {
                var extract = Button("Extract", Extract, "gs-button--primary gs-button--big", "d_PlayButton");
                extract.SetEnabled(consent && AssetRipperInstaller.Executable != null && !_busy);
                card.Add(Buttons(extract, Button("Choose output…", () =>
                {
                    string folder = EditorUtility.OpenFolderPanel("Where to put the extracted game", _output, "");
                    if (!string.IsNullOrEmpty(folder)) { _output = folder; Rebuild(); }
                }, icon: "d_FolderOpened Icon")));
                if (!consent) card.Add(Hint("Accept the notice at the top to enable extraction."));
                else if (AssetRipperInstaller.Executable == null) card.Add(Hint("Install AssetRipper first (step 3)."));
            }

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
            var card = Card("d_ViewToolZoom", "5 · Explore", ("Extracted", "gs-chip--ok"));
            string? version = ExtractedProject.UnityVersionOf(_output);
            string? editor = ExtractedProject.FindEditor(version);
            card.Add(Buttons(
                Button(editor != null ? $"Open in Unity {version}" : "Open in this Unity", () => ExtractedProject.OpenInUnity(_output), "gs-button--primary", "d_UnityLogo"),
                Button("Open folder", () => EditorUtility.RevealInFinder(ExtractedProject.ProjectFolder(_output)), icon: "d_FolderOpened Icon")));
            if (version != null && editor == null)
                card.Add(Hint($"The game was made with Unity {version}, which is not installed. Another version works for exploring, but Unity will upgrade the project."));

            card.Add(ImportSection(version));

            // Asset types: AssetRipper groups exported assets in folders named after their type.
            string assets = Path.Combine(ExtractedProject.ProjectFolder(_output), "Assets");
            if (Directory.Exists(assets))
            {
                var types = Directory.GetDirectories(assets)
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
            }

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

        /// <summary>Compares Unity versions like 2022.3.21f1 and 6000.3.13f1 on their numeric parts.</summary>
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

        // ---------------------------------------------------------------- actions

        /// <summary>Shows a game and an existing extraction without user input (screenshots, tests).</summary>
        public void ShowForCapture(string game, string exportFolder)
        {
            SelectGame(game, rebuild: false);
            _output = exportFolder;
            _found = null; // never show the local game library in screenshots
            Rebuild();
        }

        /// <summary>Scrolls the page to the top or the bottom (screenshots).</summary>
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

        private static T Css<T>(T element, string classNames) where T : VisualElement
        {
            foreach (var c in classNames.Split(' '))
                if (c.Length > 0) element.AddToClassList(c);
            return element;
        }

        /// <summary>Built-in editor icon, matching the editor skin; null when Unity does not have it.</summary>
        private static Texture? Icon(string name)
        {
            string light = name.StartsWith("d_", StringComparison.Ordinal) ? name.Substring(2) : name;
            string wanted = EditorGUIUtility.isProSkin ? "d_" + light : light;
            var previous = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false; // IconContent logs an error for unknown names.
            try
            {
                return EditorGUIUtility.IconContent(wanted)?.image ?? EditorGUIUtility.IconContent(light)?.image;
            }
            finally
            {
                Debug.unityLogger.logEnabled = previous;
            }
        }

        /// <summary>A step card: icon, title, optional status chip, then its content.</summary>
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
