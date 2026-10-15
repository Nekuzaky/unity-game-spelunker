# Changelog

All notable changes to Game Spelunker are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-15

### Added
- Game Spelunker window (**Tools → Game Spelunker**, `Ctrl+Shift+G`) with a usage notice to accept before extracting.
- Build report read from game files: Unity version, Mono / IL2CPP, render pipeline, scenes, size, asset bundles,
  game assemblies, native plugins and installed mod loaders.
- Finder for Unity games installed through Steam (all libraries), Epic Games, GOG and itch.io.
- One-click download of the official AssetRipper 2.0.0 release, or use of an existing copy.
- Extraction to a Unity project or to raw assets, with progress, log and cancel.
- Explore view: open the export in the matching Unity version, asset types with counts, searchable list of the
  game's decompiled scripts.
- TestGame sample project and batch-mode smoke test.
