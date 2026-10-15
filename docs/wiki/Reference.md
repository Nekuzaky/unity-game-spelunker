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
| **1 · Game** | Game folder, **Choose folder…**, **Find Unity games on this PC** |
| **2 · Build report** | Unity version, scripting backend, render pipeline, scenes, size, asset bundles, assemblies, native plugins, mod support |
| **3 · Extract** | Export mode, output folder, **Extract** / **Cancel**, progress, log; **AssetRipper** foldout: status, **Install now**, **Use existing…**, release page |
| **4 · Explore** | **Open in Unity**, **Open folder**, **Import into this project**, asset types, game scripts |

The header buttons open this documentation, the GitHub issue form, and refresh the window.

## Files and folders

| Path | What it is |
| :--- | :--- |
| `Documents\Game Spelunker\<Game>\` | Default output folder of an extraction |
| `…\ExportedProject\` | The extracted Unity project |
| `…\AuxiliaryFiles\GameAssemblies\` | The game's original assemblies |
| `…\AuxiliaryFiles\path_id_map.json` | Map from original assets to exported files |
| `%LOCALAPPDATA%\GameSpelunker\AssetRipper\2.0.0\` | AssetRipper installed by Game Spelunker |
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
