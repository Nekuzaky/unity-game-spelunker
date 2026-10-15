# Game Spelunker

**See how Unity games are built, and prepare mods for games that allow them — from inside the Unity Editor.**

Game Spelunker is a Unity Editor window that inspects a Unity game build, extracts it into a Unity project with
[AssetRipper](https://github.com/AssetRipper/AssetRipper), and helps you explore the result: decompiled scripts,
scenes, prefabs, materials and asset types.

> **Use it responsibly.** Only extract games you own. Extracted assets and code still belong to their authors: use them
> to learn or to build mods the developer allows, never redistribute them, and respect each game's license (EULA).
> Online games with anti-cheat may ban accounts for modified files.

## Features

- **Find games**: lists the Unity games installed through Steam (all libraries), Epic Games, GOG and itch.io, or pick any folder.
- **Build report** (without running the game): Unity version (and whether it is installed), scripting backend
  (Mono / IL2CPP), render pipeline (Built-in / URP / HDRP), scenes, size, StreamingAssets and asset bundles, game
  assemblies, native plugins, installed mod loaders (BepInEx, MelonLoader, Doorstop, Mods folders).
- **One-click AssetRipper**: downloads the official AssetRipper release on request (never bundled), or uses one you
  already have.
- **Extraction** as a Unity project (scenes, prefabs, materials, decompiled scripts) or as plain files (PNG, models,
  sounds), with progress and cancel.
- **Explore**: open the extracted project in the matching Unity version, see asset types, browse the game's own scripts
  and open them in your code editor.

## Installation

1. Install **Unity 6000.3** (tested with 6000.3.13f1).
2. Clone this repository and add the `GameSpelunker.Unity` folder in Unity Hub (*Add project from disk*).
3. In Unity: **Tools → Game Spelunker** (or `Ctrl+Shift+G`).
4. In the window, tick the usage notice, then **Install AssetRipper** (44 MB from github.com/AssetRipper).

Automatic AssetRipper install is Windows-only for now; on macOS or Linux, download AssetRipper yourself and use
**Use existing AssetRipper…**.

## How it works

Game Spelunker starts AssetRipper headless on a free local port and drives its HTTP API:
`POST /LoadFolder` with the game's `*_Data` folder, then `POST /Export/UnityProject` or `POST /Export/PrimaryContent`
with the output folder. AssetRipper's console output is parsed for progress. The build report is read directly from
the game files (`globalgamemanagers` header, `app.info`, `Managed/`, `il2cpp_data/`, `Plugins/`).

Mono builds give fully decompiled scripts. IL2CPP builds give classes, fields and method signatures, but empty method
bodies.

## Project layout

```
GameSpelunker.Unity/Assets/GameSpelunker/Editor/
  SpelunkerWindow.cs       the window
  BuildInspector.cs        build report from game files
  GameFinder.cs            installed Unity games (Steam, Epic, GOG, itch.io)
  AssetRipperInstaller.cs  download / locate AssetRipper
  AssetRipperSession.cs    run AssetRipper headless and drive its HTTP API
  ExtractedProject.cs      scripts, Unity version and editor lookup for an export
  SpelunkerSmokeTest.cs    batch-mode end-to-end test
TestGame/                  a tiny Unity game built for testing (Editor/BuildTestGame.cs)
```

## Testing

```
Unity -batchmode -projectPath TestGame -executeMethod BuildTestGame.Build
Unity -batchmode -projectPath GameSpelunker.Unity -executeMethod GameSpelunker.SpelunkerSmokeTest.Run -gsGame TestGame/Build -gsOut <empty folder>
```

The smoke test inspects the test game, extracts it and checks that its scripts come back decompiled.

## Credits

Extraction is done by [AssetRipper](https://github.com/AssetRipper/AssetRipper) (GPL-3.0), downloaded separately.
Game Spelunker is not affiliated with Unity Technologies or with the AssetRipper project.
