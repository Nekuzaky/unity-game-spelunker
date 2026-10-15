# Changelog

All notable changes to Game Spelunker are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.2.1] - 2026-10-15

### Fixed
- **Extract** failed with "GetString can only be called from the main thread": AssetRipper's location was read from
  EditorPrefs on a worker thread. It is now read before the extraction starts.

## [0.2.0] - 2026-10-15

### Changed
- Nothing to install besides the package: the first **Extract** downloads AssetRipper 2.0.0 if it is missing, then
  extracts in the same click. The separate **3 · AssetRipper** step is gone; steps are now Game, Build report,
  Extract, Explore.
- AssetRipper's status, **Install now**, **Use existing…** and the release page moved to an **AssetRipper** foldout
  at the bottom of the Extract card.
- The usage notice says that the first extraction downloads AssetRipper from github.com/AssetRipper.

### Added
- Automatic AssetRipper download on macOS and Linux (x64 and ARM64) and on Windows ARM64.

### Fixed
- An interrupted download or unpack no longer leaves a half-installed AssetRipper that looks ready.

## [0.1.1] - 2026-10-15

### Changed
- The repository is now `Nekuzaky/unity-game-spelunker`; package links point to it. The package id does not change.
- The window's help button and the package documentation link open the full manual at
  https://nekuzaky.com/docs/unity-game-spelunker.
- The README install URL has no version, so it installs the latest release.

## [0.1.0] - 2026-10-15

### Added
- Distributed as a Unity package (`com.nekuzaky.game-spelunker`), installable from a git URL in Unity 6000.3 or newer.
- Import into the open project: assets copied into `Assets/Spelunked/<Game>` with their references, engine shaders,
  plugins and settings left out, GUID conflicts skipped, optional scripts isolated behind a scripting define.
- Game Spelunker window (**Tools → Game Spelunker**, `Ctrl+Shift+G`) with a usage notice to accept before extracting.
- Build report read from game files: Unity version, Mono / IL2CPP, render pipeline, scenes, size, asset bundles,
  game assemblies, native plugins and installed mod loaders.
- Finder for Unity games installed through Steam (all libraries), Epic Games, GOG and itch.io.
- One-click download of the official AssetRipper 2.0.0 release, or use of an existing copy.
- Extraction to a Unity project or to raw assets, with progress, log and cancel.
- Explore view: open the export in the matching Unity version, asset types with counts, searchable list of the
  game's decompiled scripts.
- TestGame sample project and batch-mode smoke test.
