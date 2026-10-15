# Reference

## Menu and shortcut

| | |
| :--- | :--- |
| **Tools → Game Spelunker** | Opens the window |
| `Ctrl+Shift+G` (`Cmd+Shift+G` on macOS) | Same, from anywhere in the editor |

## Window

| Card | Contents |
| :--- | :--- |
| **Before you dig** | Usage notice; must be accepted before extracting |
| **1 · Game** | Game folder, **Choose folder…**, **Find Unity games on this PC** with search, sorting and badges (Mono/IL2CPP, Unity version, already extracted) |
| **2 · Build report** | Unity version, scripting backend, render pipeline, scenes, size, asset bundles, assemblies, native plugins, mod support; **Install Unity X** and licence activation when needed |
| **3 · Extract** | Export mode, output folder, **Extract** / **Cancel** / **Add to queue**, the stages with their times, log; **AssetRipper** foldout: status, **Install now**, **Use existing…**, release page |
| **4 · Explore** | **Open in Unity**, **Open folder**, Health dashboard (**Check the project**, **Repair again**, screenshots, **Copy report**), plugins & middleware, **Import into this project**, asset types, scenes with per-scene open, texture previews, code fixes, game scripts |
| **Your extractions** | Every extraction with its size; select, free the Unity cache, delete |
| **Settings** | Extractions folder, compiled shaders, game code mode, Play check, notification, fix passes |

The header buttons open this documentation, the GitHub issue form, and refresh the window.

## Files and folders

| Path | What it is |
| :--- | :--- |
| `Documents\Game Spelunker\<Game>\` | Default output folder of an extraction |
| `…\ExportedProject\` | The extracted Unity project |
| `…\AuxiliaryFiles\GameAssemblies\` | The game's original assemblies |
| `…\AuxiliaryFiles\path_id_map.json` | Map from original assets to exported files |
| `…\AuxiliaryFiles\GameSpelunker-code-fixes.txt` | Everything the code fix loop changed |
| `…\AuxiliaryFiles\GameSpelunker-plugins.txt` | Plugins and middleware recognised in the game |
| `…\AuxiliaryFiles\GameSpelunker-check.txt` | The last **Check the project** report, screenshots beside it |
| `%LOCALAPPDATA%\GameSpelunker\AssetRipper\2.0.0\` | AssetRipper installed by Game Spelunker |
| `%LOCALAPPDATA%\GameSpelunker\BuiltinShaders\<version>\` | Unity's official shader source, downloaded per version |
| `Assets/Spelunked/<Game>/` | Default folder for **Import into this project** |
| `Assets/Spelunked/<Game>/Scripts/Spelunked.<Game>.asmdef` | Assembly of imported scripts, gated by a scripting define |

## Scripting define for imported scripts

`SPELUNKER_COMPILE_<GAME>`, where `<GAME>` is the game's name in upper case with every other character replaced by `_`. Example: *Spelunker Test Game* → `SPELUNKER_COMPILE_SPELUNKER_TEST_GAME`. The exact name is shown in the window and in the import summary.

## Editor preferences

Stored per user with `EditorPrefs`, shared by all your projects:

| Key | Meaning |
| :--- | :--- |
| `GameSpelunker.Consent` | The usage notice was accepted |
| `GameSpelunker.LastGame` | The last game selected, reopened with the window |
| `GameSpelunker.AssetRipperPath` | A custom AssetRipper executable chosen with **Use existing…** |
| `GameSpelunker.OutputRoot` | The extractions folder (empty = `Documents\Game Spelunker`) |
| `GameSpelunker.KeepCompiledShaders` | Keep the game's compiled shaders with its own Unity version |
| `GameSpelunker.OriginalAssemblies` | Keep the game's assemblies instead of decompiling |
| `GameSpelunker.CheckAfterExtracting` | Run the Play-mode check after each extraction |
| `GameSpelunker.NotifyWhenDone` | Sound and taskbar notification when an extraction ends |
| `GameSpelunker.CompilePasses` | Number of code fix passes |

## Package

| | |
| :--- | :--- |
| **Name** | `com.nekuzaky.game-spelunker` |
| **Git URL** | `https://github.com/Nekuzaky/unity-game-spelunker.git?path=Packages/com.nekuzaky.game-spelunker` |
| **Minimum Unity** | 6000.3 |
| **Assembly** | `GameSpelunker.Editor` (editor only, never in builds) |
| **Namespace** | `GameSpelunker` |

## Scripting API

The window is built on a few public editor classes you can call from your own editor scripts:

| Class | Use |
| :--- | :--- |
| `BuildInspector.Inspect(path)` | Returns a `BuildReport` for a game folder |
| `GameFinder.FindInstalledUnityGames()` | Lists installed Unity games |
| `AssetRipperInstaller.Executable` / `EnsureInstalledAsync(…)` | Locates AssetRipper, or downloads it when missing |
| `AssetRipperSession` | `StartAsync`, `LoadAsync(dataFolder)`, `ExportAsync(output, mode)`, with `Log` and `Progress` events |
| `ExtractedProject.FindScripts(exportFolder)` | Lists the game scripts of an export |
| `ProjectImporter.Import(exportFolder, gameName, options)` | Imports an export into the open project |

## Command-line test

The repository contains an end-to-end test that runs without a screen. Build the included `TestGame` once (`-executeMethod BuildTestGame.Build` on the `TestGame` project), then run:

```
Unity -batchmode -projectPath <repository> -executeMethod GameSpelunker.SpelunkerSmokeTest.Run -gsGame TestGame/Build -gsOut <empty folder>
```

It inspects the included `TestGame`, extracts it with AssetRipper, imports it into the project (scripts included), checks the results and cleans up.
