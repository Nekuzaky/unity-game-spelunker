# Changelog

All notable changes to Game Spelunker are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.3.0] - 2026-10-15

### Added
- **Games open in their own Unity version.** When the game's exact editor is installed, the export is prepared for it:
  no API migration, the game's original managed libraries (Rewired, PlayMaker, Bolt…) instead of AssetRipper's
  rewritten copies, no assembly definitions before Unity 2017.3, and no copy of the UnityScript/Boo libraries old
  editors already ship. The Forest (Unity 5.6.5p4) compiles with zero errors and reaches its title scene in Play mode.
- **Install the game's Unity from the window.** The build report offers to install the game's exact Unity version
  through Unity Hub (headless), and old versions that still need the legacy license get an activate-once flow.
- **Extraction stages.** Extracting shows each stage (export, prepare, make the code compile, import and repair,
  check) with its state, elapsed time and an estimate of the time left, plus a notification (sound and taskbar flash)
  when it ends.
- **Extraction queue.** Add games to a queue; each one starts as soon as the previous extraction finishes.
- **Health dashboard.** The Explore card sums up the project as tiles (Play mode, game code, materials, custom
  shaders), shows the screenshots taken in Play mode, the most frequent errors, and a **Copy report** button.
- **Check the project.** One click presses Play in the background for about a minute, takes screenshots, counts
  errors and writes `AuxiliaryFiles/GameSpelunker-check.txt`; it can also run automatically after each extraction.
- **Repair again** redoes the preparation, the code fixes and the material repair on an existing extraction without
  extracting the game again.
- The game list has a search box, sorting (name, Unity version, scripting, extracted first) and badges: Mono or
  IL2CPP, the game's Unity version (green when installed) and whether it is already extracted.
- The Explore card lists the scenes (biggest first) with **Open in Unity** per scene, previews the extracted textures,
  and lists every code fix with a link that opens the file in your editor.
- **Your extractions.** A card lists every extraction with its size, frees the Unity cache (Library) or deletes the
  extraction, and reopens any of them.
- **Settings.** Extractions folder, check-after-extract, end-of-extraction notification and the number of code fix
  passes, in a foldout at the bottom of the window.
- Extracted projects open on the game's biggest scene the first time, with the camera over the terrain.
- **Unity's own shaders come back as real source.** For every placeholder that hides one of Unity's built-in shaders,
  Game Spelunker downloads Unity's official shader source for the right editor version (a 300 KB archive, cached per
  version) and restores the original code under the same name. Imports into your own project are restored the same way.
- **The game's compiled shaders can be kept.** When the game's exact Unity version is installed, extraction keeps the
  shaders as compiled in the build instead of placeholders, so custom materials render exactly as in the game
  (Settings → "Keep the game's compiled shaders"). Game code can also be kept as the original assemblies instead of
  decompiled C# ("runs exactly like the game", no source to read).
- **A render probe catches shaders that need the game running.** Kept game shaders are drawn off-screen once per
  material during the repair; those that draw nothing without the game's code (wind-driven trees, code-driven skies,
  oceans) are repaired like placeholders, the rest keep the game's real look. On The Forest the terrain, rocks and
  grass keep the game's own shaders and 373 tree and sky materials fall back to the repair.
- A render pipeline that only exists in the build as decompiled or rewritten code (a URP asset whose script lives in
  the project, not in a package) is removed from Graphics and Quality settings, so the project renders with the
  built-in pipeline and its materials are repaired instead of staying untouched.
- **The code fix loop got a quick compiler.** Before each background Unity pass, the game's code is compiled with the
  editor's own C# compiler directly (seconds instead of minutes per pass, same assembly layout and defines); Unity
  then only confirms the result. A safety valve falls back to the Unity-only loop when the quick compiler disagrees.
- **Play mode acts like the real game.** `steam_appid.txt` is written into extracted projects of Steam games, so
  Steamworks initialises in Play mode instead of failing or closing the game.
- **Known plugins and middleware are recognised** (Rewired, PlayMaker, DOTween, FMOD, Wwise, Photon, A* Pathfinding,
  NGUI, TextMesh Pro, Cinemachine…) and listed in the Explore card and `AuxiliaryFiles/GameSpelunker-plugins.txt`,
  with where each one comes from.
- Baked lighting data that cannot survive a Unity version change is removed (and scenes unlinked from it), so
  upgraded projects stop logging "LightingData asset is incompatible".
- **Interface shaders come back.** The NGUI and text shaders AssetRipper leaves empty (`Unlit/Transparent Colored`,
  `Unlit/Text`, their clipped `1/2/3` and `TextureClip` variants, premultiplied, masked, packed, `GUI/… Text`) are
  regenerated under the same name, so menus and HUD text render: The Forest's title menu shows all its text again.
- IL2CPP games load from the game folder (so `GameAssembly.dll` is found) and the build report explains exactly what
  comes back from such a build.

- **The game's code is made to compile.** After the import, Game Spelunker compiles the decompiled code in the
  background (with the game code switched on), and repeats until it compiles: members the decompiler could not rebuild
  are stubbed (same signature, a warning or a NotImplementedException inside), and files that cannot be stubbed are
  set aside behind `SPELUNKER_QUARANTINED`. The list is written to `AuxiliaryFiles/GameSpelunker-code-fixes.txt`.
  On The Forest: 9 members stubbed and 12 TerrainComposer tool files set aside, out of 4,733 files.
- Decompiled code is rewritten for APIs Unity removed or renamed: legacy particles, GUIText/GUITexture/GUILayer,
  MovieTexture, ProceduralMaterial, legacy networking (NetworkView, RPC, Network), UnityEngine.VR, PhysicMaterial,
  removed platforms, `new Material(string)`, `Graphics.DrawProceduralIndirect`, `Application.isWebPlayer`, removed
  TextureFormat members, and decompiler-generated event backing fields. Replacements live in
  `Assets/Plugins/GameSpelunker/LegacyApi.cs`, compiled only with the game code.
- C# and UnityScript code of the game are compiled as separate assemblies, as the game did.
- The game's native libraries (FMOD, Steam, VR…) are copied into `Assets/Plugins/Native`.
- **Previews of objects spawned by game code.** Scenes are scanned for markers whose `High` / `Mid` / `Low` / `LOD0`…
  fields point to prefabs (how The Forest places its 8,940 trees), and the extracted project shows those objects at
  their world position when a scene opens. Never saved into the scene, hidden in Play mode, toggled from
  **Tools → Game Spelunker → Show Objects Spawned by Game Code**.
- The repair reads how the original shader described each property: a texture is only reused as specular,
  metallic, normal, occlusion or emission map when its description says so (a "Detail Mask (G) Noise (B) Occlusion
  (A)" map is not a specular map), and a `_Color` described as a "Color Variation" is not used as a tint.
- Textures the game passes to `Cursor.SetCursor` (found from the decompiled calls and the fields they use) are
  imported as Cursor textures, so they no longer fail with "not CPU accessible".
- Pressing Play while the game's code is switched off asks first: Play mode runs none of it and every game component
  reports a missing script. Cancel, play anyway, or enable the game code.
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
- Native plugins that are not .NET assemblies (Broforce's ConsoleUtils.dll…) no longer break the plugin copy: the
  check reads the PE header itself instead of loading the file.
- The compile loop no longer quarantines Game Spelunker's own tools, stops on errors that are not tied to a script
  (duplicate assemblies…), and renames decompiled members that share their type's name instead of quarantining the
  file and everything that uses it.
- A failed compile loop leaves the game code switched off instead of half on.
- The game code define is written in the format of the project's Unity version (`1:` before 2021.2, `Standalone:`
  after); older projects silently dropped it, so their game code was never compiled and every component reported a
  missing script in Play mode.
- The game's Unity version is remembered at extraction (`AuxiliaryFiles/GameSpelunker-version.txt`), so preparing a
  project again after Unity upgraded it still migrates the APIs it needs.
- The quick compiler only fixes errors decompilation causes; a type it cannot resolve is left to Unity instead of
  setting the file aside.
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
