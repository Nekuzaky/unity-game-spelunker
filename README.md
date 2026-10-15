<p align="center">
  <img src="docs/brand/banner.png" alt="Game Spelunker — See how Unity games are built" width="100%">
</p>

<p align="center">
  <a href="https://unity.com/releases/editor/whats-new/6000.3.13"><img alt="Unity 6000.3" src="https://img.shields.io/badge/Unity-6000.3-1f1a2e?logo=unity&logoColor=white"></a>
  <a href="https://github.com/AssetRipper/AssetRipper/releases/tag/2.0.0"><img alt="AssetRipper 2.0.0" src="https://img.shields.io/badge/AssetRipper-2.0.0-8b6cd6"></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0078d4">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-ffb347"></a>
</p>

**Game Spelunker** is a Unity Editor window to see how Unity games are built and to prepare mods for games that
allow them. It reads a game's build to tell you what it is made of, extracts it into a Unity project with
[AssetRipper](https://github.com/AssetRipper/AssetRipper), and helps you explore the result: decompiled scripts,
scenes, prefabs, materials and every asset type.

> [!WARNING]
> **Use it responsibly.** Only extract games you own. Extracted assets and code still belong to their authors: use
> them to learn or to build mods the developer allows, never redistribute them, and respect each game's license
> (EULA). Online games with anti-cheat may ban accounts for modified files.

<p align="center">
  <img src="docs/images/window-top.png" alt="Build report" width="48%">
  &nbsp;
  <img src="docs/images/window-explore.png" alt="Extract and explore" width="48%">
</p>

## Features

| | |
|---|---|
| 🔎 **Find games** | Lists the Unity games installed through Steam (all libraries), Epic Games, GOG and itch.io — or pick any folder. |
| 🩻 **Build report** | Without running the game: Unity version (and whether you have it), Mono / IL2CPP, render pipeline, scenes, size, asset bundles, game assemblies, native plugins, installed mod loaders (BepInEx, MelonLoader, Doorstop…). |
| ⛏️ **One-click AssetRipper** | Downloads the official AssetRipper release on request — never bundled — or uses a copy you already have. |
| 📦 **Extract** | As a Unity project (scenes, prefabs, materials, decompiled scripts) or as raw assets (PNG, models, sounds), with progress, log and cancel. |
| 🕯️ **Explore** | Open the export in the matching Unity version, see what's inside by asset type, search the game's scripts and open them in your code editor. |

## Installation

1. Install **Unity 6000.3** (tested with 6000.3.13f1) through Unity Hub.
2. Clone this repository and add the `GameSpelunker.Unity` folder in Unity Hub (*Add project from disk*).
3. In Unity: **Tools → Game Spelunker** (or `Ctrl+Shift+G`).
4. Accept the usage notice, then click **Install AssetRipper** (44 MB from github.com/AssetRipper).

Automatic AssetRipper install is Windows-only for now. On macOS or Linux, download AssetRipper yourself and use
**Use existing…**.

## Usage

1. **Game** — *Find Unity games on this PC* or *Choose folder…* (the folder with the game's `.exe` and `*_Data`).
2. **Build report** — read what the game is made of.
3. **AssetRipper** — install it once.
4. **Extract** — choose *Unity project* or *Raw assets* and an output folder, then **Extract**.
5. **Explore** — **Open in Unity** opens `ExportedProject` in the right Unity version. Click a script to open it in
   your code editor, or an asset type to show it in Explorer.

Mono builds give fully decompiled scripts. IL2CPP builds give classes, fields and method signatures, but empty method
bodies. Code decompiled from a game can have compile errors: when Unity offers *Safe Mode*, choose *Ignore* to explore
scenes and assets anyway.

## How it works

Game Spelunker starts AssetRipper headless on a free local port and drives its HTTP API:
`POST /LoadFolder` with the game's `*_Data` folder, then `POST /Export/UnityProject` or `POST /Export/PrimaryContent`
with the output folder. AssetRipper's console output is parsed for progress. The build report is read straight from
the game files (`globalgamemanagers` header, `app.info`, `Managed/`, `il2cpp_data/`, `Plugins/`).

## Project layout

```
GameSpelunker.Unity/Assets/GameSpelunker/Editor/
  SpelunkerWindow.cs       the window (UI Toolkit) and Spelunker.uss
  BuildInspector.cs        build report from game files
  GameFinder.cs            installed Unity games (Steam, Epic, GOG, itch.io)
  AssetRipperInstaller.cs  download / locate AssetRipper
  AssetRipperSession.cs    run AssetRipper headless and drive its HTTP API
  ExtractedProject.cs      scripts, Unity version and editor lookup for an export
  SpelunkerSmokeTest.cs    batch-mode end-to-end test
  SpelunkerCapture.cs      README screenshots
TestGame/                  a tiny Unity game built for testing (Editor/BuildTestGame.cs)
docs/brand/                logo and banner (SVG + PNG)
```

## Development

```
Unity -batchmode -projectPath TestGame -executeMethod BuildTestGame.Build
Unity -batchmode -projectPath GameSpelunker.Unity -executeMethod GameSpelunker.SpelunkerSmokeTest.Run -gsGame TestGame/Build -gsOut <empty folder>
```

The smoke test inspects the test game, extracts it and checks that its scripts come back decompiled.
See [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

## Support the project

If Game Spelunker helps you, you can support its development on
[Patreon](https://www.patreon.com/Nekuzaky) or [Buy Me a Coffee](https://www.buymeacoffee.com/Nekuzaky). ⭐ Stars help too!

## Credits

- Extraction by [AssetRipper](https://github.com/AssetRipper/AssetRipper) (GPL-3.0), downloaded separately.
- Icons in the window are Unity's built-in editor icons.
- Game Spelunker is not affiliated with Unity Technologies or with the AssetRipper project.

## License

[MIT](LICENSE) © Nekuzaky
