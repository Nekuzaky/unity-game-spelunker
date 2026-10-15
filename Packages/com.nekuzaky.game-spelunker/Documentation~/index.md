# Game Spelunker

Game Spelunker is an editor window to see how Unity games are built and to prepare mods for games that allow them.

Open it with **Tools → Game Spelunker** (`Ctrl+Shift+G`).

1. **Game** — find the Unity games installed on this computer, or choose a folder.
2. **Build report** — Unity version, Mono or IL2CPP, render pipeline, scenes, assemblies, plugins, mod loaders. When
   the game's Unity version is missing, install it through Unity Hub from here.
3. **Extract** — AssetRipper exports the game as a Unity project, then Game Spelunker prepares it, makes the game's
   code compile, imports it, repairs materials and shaders and checks it in Play mode.
4. **Explore** — open the project in the right Unity version, read the health dashboard, browse scenes, textures and
   scripts, or import the assets into the project you have open.

Full manual: https://nekuzaky.com/docs/unity-game-spelunker

Only extract games you own. Extracted assets and code still belong to their authors: use them to learn or to build
mods the developer allows, never redistribute them, and respect each game's license (EULA).
