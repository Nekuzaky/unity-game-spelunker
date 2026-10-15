# Changelog

All notable changes to Game Spelunker are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Previews of objects spawned by game code.** Scenes are scanned for markers whose `High` / `Mid` / `Low` / `LOD0`…
  fields point to prefabs (how The Forest places its 8,940 trees), and the extracted project shows those objects at
  their world position when a scene opens. Never saved into the scene, hidden in Play mode, toggled from
  **Tools → Game Spelunker → Show Objects Spawned by Game Code**.
- The repair reads how the original shader described each property: a texture is only reused as specular,
  metallic, normal, occlusion or emission map when its description says so (a "Detail Mask (G) Noise (B) Occlusion
  (A)" map is not a specular map), and a `_Color` described as a "Color Variation" is not used as a tint.
- Rebuilt materials no longer inherit textures that happen to sit in Standard's own slots (detail, parallax…), bark
  and trunks are never cut out (their alpha is often gloss), and the tint's alpha no longer cuts out opaque surfaces.
- **Projects delivered ready.** After extracting a Unity project, Game Spelunker opens it in Unity in the background
  (the version Open in Unity uses), imports every asset and repairs the materials, with a progress bar. Option
  **Import into Unity and repair right away**, on by default. Log in `AuxiliaryFiles/GameSpelunker-import.log`.
- Extracted projects open with a clean Console: removed components (GUI Layer, GUI Texture, GUI Text) are cleaned out
  of scenes and prefabs, unreadable `CGProgram` assets are deleted, the game's own libraries only load with its code
  (no more "Unloading broken assembly"), and Standard materials get a render queue that matches their blend mode.
- **Material repair.** AssetRipper replaces the shaders it cannot recover with placeholders that draw one flat
  texture (grey rocks, white terrain, square grass). Extracted projects now repair their materials the first time they
  open: back on the real Unity shader when the game used a stock one, otherwise rebuilt on Standard, URP Lit or HDRP
  Lit with their textures, cut-out for foliage, a terrain shader for terrains, a particle shader for particles,
  Unity's procedural sky for custom skies and a tinted see-through surface for water.
  Placeholders that hid a built-in shader (the terrain's grass shader, Standard (Specular setup)) are renamed, and
  graphics settings that pointed to placeholders (custom deferred lighting…) are set back to Unity's own shaders.
  Each material remembers its original shader in its .meta, so a later run rebuilds it from the original. Imports
  into your project are repaired the same way, and **Assets → Game Spelunker → Repair Materials** runs it on any folder.
- The game's decompiled code is switched off in extracted projects (two assemblies behind the define
  `SPELUNKER_GAME_CODE`), so the project always compiles and its editor tools, the material repair included, run.
  **Tools → Game Spelunker → Enable / Disable Game Code** in the extracted project. Libraries the code needs that
  AssetRipper left out (Boo, UnityScript…) are copied from the game, and duplicate `AssemblyInfo.cs` files are removed.
- Extracted projects are prepared before they open: game scripts no longer run in the editor (`[ExecuteInEditMode]` and
  `[ExecuteAlways]` are removed), so decompiled post effects, weather or UI code can no longer flood the Console or
  crash Unity, and code generated for the game's build (`__JobReflectionRegistrationOutput__…`) is removed.
- Game icons in the list of found games and next to the game's name in the build report, for games installed by
  Steam. They come from Steam's local library cache (`appcache/librarycache`): nothing is downloaded.

### Fixed
- The build report showed Unity **?** for games made with Unity 3, 4 or 5 (for example 5.6.5p4): only versions
  starting with a year or 6000 were recognised.
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
