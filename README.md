<p align="center">
  <img src="docs/brand/banner.png" alt="Game Spelunker — See how Unity games are built" width="100%">
</p>

<p align="center">
  <img alt="Unity 6000.3 or newer" src="https://img.shields.io/badge/Unity-6000.3%2B-1b1726?logo=unity&logoColor=white">
  <a href="https://github.com/AssetRipper/AssetRipper/releases/tag/2.0.0"><img alt="AssetRipper 2.0.0" src="https://img.shields.io/badge/AssetRipper-2.0.0-2b2440"></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0078d4">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-ffb347"></a>
</p>

**Game Spelunker** is a Unity Editor tool to see how Unity games are built and to prepare mods for games that
allow them. It reads a game's build to tell you what it is made of, extracts it with
[AssetRipper](https://github.com/AssetRipper/AssetRipper), lets you import the result into the project you are
working on, and helps you explore the decompiled scripts.

> [!WARNING]
> **Use it responsibly.** Only extract games you own. Extracted assets and code still belong to their authors: use
> them to learn or to build mods the developer allows, never redistribute them, and respect each game's license
> (EULA). Online games with anti-cheat may ban accounts for modified files.

<p align="center">
  <img src="docs/images/window-top.png" alt="Build report" width="48%">
  &nbsp;
  <img src="docs/images/window-explore.png" alt="Extract and explore" width="48%">
</p>

## Install

In Unity **6000.3 or newer**: **Window → Package Manager → + → Install package from git URL…** and paste:

```
https://github.com/Nekuzaky/game-spelunker.git?path=Packages/com.nekuzaky.game-spelunker#v0.1.0
```

Then open **Tools → Game Spelunker** (`Ctrl+Shift+G`). The first time, accept the usage notice and click
**Install AssetRipper** (44 MB, downloaded from github.com/AssetRipper).

To always get the latest version, drop `#v0.1.0` from the URL. Tested with Unity 6000.3.13f1 and 6000.5.9f1.

## Features

| | |
|---|---|
| 🔎 **Find games** | Lists the Unity games installed through Steam (all libraries), Epic Games, GOG and itch.io — or pick any folder. |
| 🩻 **Build report** | Without running the game: Unity version (and whether you have it), Mono / IL2CPP, render pipeline, scenes, size, asset bundles, game assemblies, native plugins, installed mod loaders (BepInEx, MelonLoader, Doorstop…). |
| ⛏️ **One-click AssetRipper** | Downloads the official AssetRipper release on request — never bundled — or uses a copy you already have. |
| 📦 **Extract** | As a Unity project (scenes, prefabs, materials, decompiled scripts) or as raw assets (PNG, models, sounds), with progress, log and cancel. |
| 📥 **Import into your project** | Copies the extracted assets into `Assets/Spelunked/<Game>` of the project you have open, keeping references between assets. |
| 🕯️ **Explore** | Open the export in the matching Unity version, see what's inside by asset type, search the game's scripts and open them in your code editor. |

## Importing into your project

The **Import into this project** section (step 5) is built so it can never break the project you are working in:

- Everything goes into its own folder (`Assets/Spelunked/<Game>` by default), with the original `.meta` files so prefabs,
  materials and textures stay linked.
- Never imported: engine shaders, compute shaders, native plugins, `Editor` folders, `StreamingAssets` and project
  settings. Nested `Resources` folders are renamed so they don't end up in your builds.
- Files whose GUID already exists in your project are skipped and counted.
- **Scripts are optional.** When included, they go into one assembly that only compiles once you add the scripting
  define shown in the window (*Project Settings → Player → Scripting Define Symbols*). Until then you can read them, and
  prefabs keep their script references, but they can't stop your project from compiling.
- Packages the game used that your project lacks (TextMeshPro, Cinemachine…) are listed so you can add them.

## Usage

1. **Game** — *Find Unity games on this PC* or *Choose folder…* (the folder with the game's `.exe` and `*_Data`).
2. **Build report** — read what the game is made of.
3. **AssetRipper** — install it once.
4. **Extract** — choose *Unity project* or *Raw assets* and an output folder, then **Extract**.
5. **Explore** — **Open in Unity** opens `ExportedProject` in the right Unity version, or **Import into this project**.
   Click a script to open it in your code editor, or an asset type to show it in Explorer.

Mono builds give fully decompiled scripts. IL2CPP builds give classes, fields and method signatures, but empty method
bodies. When you open an extracted project and Unity offers *Safe Mode*, choose *Ignore* to explore scenes and assets
anyway.

## How it works

Game Spelunker starts AssetRipper headless on a free local port and drives its HTTP API:
`POST /LoadFolder` with the game's `*_Data` folder, then `POST /Export/UnityProject` or `POST /Export/PrimaryContent`
with the output folder. AssetRipper's console output is parsed for progress. The build report is read straight from
the game files (`globalgamemanagers` header, `app.info`, `Managed/`, `il2cpp_data/`, `Plugins/`).

## Repository layout

The repository root is the Unity project used to develop the package.

```
Packages/com.nekuzaky.game-spelunker/   the package users install
  Editor/SpelunkerWindow.cs             the window (UI Toolkit) and Spelunker.uss
  Editor/BuildInspector.cs              build report from game files
  Editor/GameFinder.cs                  installed Unity games (Steam, Epic, GOG, itch.io)
  Editor/AssetRipperInstaller.cs        download / locate AssetRipper
  Editor/AssetRipperSession.cs          run AssetRipper headless and drive its HTTP API
  Editor/ExtractedProject.cs            scripts, Unity version and editor lookup for an export
  Editor/ProjectImporter.cs             safe import into the open project
Assets/Dev/Editor/                      smoke test and README screenshots (not in the package)
TestGame/                               a tiny Unity game built for testing
docs/brand/                             logo and banner (SVG + PNG)
```

## Development

```
Unity -batchmode -projectPath TestGame -executeMethod BuildTestGame.Build
Unity -batchmode -projectPath . -executeMethod GameSpelunker.SpelunkerSmokeTest.Run -gsGame TestGame/Build -gsOut <empty folder>
```

The smoke test inspects the test game, extracts it, imports it into the project (scripts included) and checks that
everything came back. See [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

## Support the project

If Game Spelunker helps you, you can support its development on
[Patreon](https://www.patreon.com/Nekuzaky) or [Buy Me a Coffee](https://www.buymeacoffee.com/Nekuzaky). ⭐ Stars help too!

## Credits

- Extraction by [AssetRipper](https://github.com/AssetRipper/AssetRipper) (GPL-3.0), downloaded separately.
- Icons in the window are Unity's built-in editor icons.
- Game Spelunker is not affiliated with Unity Technologies or with the AssetRipper project.

## License

[MIT](LICENSE) © Nekuzaky
