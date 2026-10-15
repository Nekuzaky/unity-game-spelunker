# Opening the Project

After a **Unity project** extraction, the **4 · Explore** card offers two ways to open it.

## Open in Unity

**Open in Unity 6000.x** starts a **new Unity window** on the extracted project. The editor you are in stays open.

- If the game's Unity version is installed through Unity Hub, Game Spelunker uses that exact version — the best way to see the project as the developers did.
- Otherwise the button reads **Open in this Unity** and uses the editor you are running. That works for exploring, but Unity will upgrade the project to its own version.

To install the game's version, open Unity Hub → **Installs → Install Editor → Archive** and pick the version shown in the build report.

## Opening it yourself

The project to open is the **`ExportedProject`** folder inside the output folder — not the output folder itself. In Unity Hub: **Add → Add project from disk** → select `ExportedProject`.

## What Game Spelunker prepares for you

Right after a **Unity project** extraction, Game Spelunker prepares the project, then — with **Import into Unity and repair right away** ticked, the default — opens it in Unity in the background (the same Unity version **Open in Unity** will use), imports every asset and repairs the materials. The progress bar follows the import; large games take several minutes. When you click **Open in Unity**, the project is ready: no long first import, no repair running in front of you, and a clean Console. The import log is kept in `AuxiliaryFiles/GameSpelunker-import.log`.

What the preparation does:

- **Removed Unity features are cleaned out**: *GUI Layer*, *GUI Texture* and *GUI Text* components (gone from Unity since 2019) are removed from scenes and prefabs, and shader fragments AssetRipper exports as `.asset` files Unity cannot read (`CGProgram`) are deleted.
- **Objects the game places with its code are shown.** Many games keep only a lightweight marker in the scene and let their code spawn the real object at runtime — in The Forest, every tree is a marker with `High` / `Mid` / `Low` prefab fields, so an extracted map has no trees. Game Spelunker finds those markers (fields named `High`, `Mid`, `Low`, `LOD0`…`LOD3` pointing to prefabs), computes where they are in the world, and the extracted project shows them as previews when a scene opens, under *Game Spelunker: spawned by game code (preview)*. Previews are never saved into the scene and are removed during Play mode. Turn them off with **Tools → Game Spelunker → Show Objects Spawned by Game Code**. With thousands of markers, the mid-detail version is used to keep the editor responsive.
- **The game's libraries follow its code**: the game's own `.dll` files (PlayMaker, Rewired…) only load when the game's code is switched on, so a library built against an old Unity can no longer be unloaded as "broken" on every open.

- **The game's code is switched off.** Decompiled code rarely compiles as-is, and while a project has compile errors Unity runs none of its editor tools and asks about Safe Mode. The decompiled scripts are put in two assemblies, `Spelunked.GameCode` and `Spelunked.GameCode.FirstPass`, that only compile when the define `SPELUNKER_GAME_CODE` is set. The project opens without errors; scenes, prefabs and assets are all there, and components keep their values and their link to their script. To try the game's code, use **Tools → Game Spelunker → Enable Game Code** in that project, then fix the errors that appear; **Disable Game Code** switches it off again.
- **What the code needs is put back**: libraries the game shipped but AssetRipper left out (for example `Boo.Lang.dll`, `UnityScript.Lang.dll`) are copied from the game into `Assets/Plugins`, and the duplicate `AssemblyInfo.cs` files of each decompiled assembly are removed.
- **Game code only runs in Play mode.** Decompiled scripts marked `[ExecuteInEditMode]` or `[ExecuteAlways]` — post effects, weather, water, UI — would run inside the editor with none of the objects they expect, flood the Console with errors and can even crash Unity. The attribute is replaced by a comment (`ExecuteInEditMode removed by Game Spelunker`), so these scripts wait for Play mode.
- **Build-generated code is removed**: files such as `__JobReflectionRegistrationOutput__*.cs`, which Burst generated for the game and which clash with the code Unity generates for the project.
- **Materials are repaired the first time the project opens.** AssetRipper cannot recover most shaders: it writes placeholders that draw one flat texture, so the game looks grey, white or pink. Game Spelunker installs a small tool in `Assets/GameSpelunker/Editor` that, on first open:
  - puts materials made with a stock Unity shader (Standard, URP, HDRP, TextMeshPro…) back on the real shader;
  - rebuilds the others on the project's lit shader (*Standard*, *Universal Render Pipeline/Lit* or *HDRP/Lit*) with their color, normal, occlusion and emission textures, cut-out for foliage and fences, transparency for glass;
  - gives terrains a terrain shader, particles a particle shader, and custom skies Unity's procedural sky;
  - turns water, lakes and oceans into a tinted, glossy, see-through surface;
  - leaves alone what only game code can drive (post effects, underwater effects, masks, UI, text);
  - renames placeholders that hid a built-in shader, such as the one the terrain uses for grass;
  - sets the rendering shaders of *Project Settings → Graphics* (deferred lighting, reflections, screen-space shadows…) back to Unity's own when the game had custom ones AssetRipper could not recover — otherwise the whole scene renders black;
  - remembers, in each material's `.meta`, the shader it was made with: running the repair again (for example after updating Game Spelunker) rebuilds every material from its original, not from a previous repair.

  The result is written to `ProjectSettings/GameSpelunker.txt`. Run it again on any folder with **right-click → Game Spelunker → Repair Materials**. Original shader effects (wind, wetness, triplanar…) cannot come back, but every object shows its own textures and lighting.

## What to expect the first time

| What you see | Why, and what to do |
| :--- | :--- |
| **A long import** | Unity imports every texture, model and sound of the game. Large games take several minutes. |
| **Nothing happens in Play mode** | The game's code is switched off (see above). Even with it on, an extracted game rarely plays like the original: shaders, services (Steam, online), and code that was generated at build time are missing. Use Play mode to inspect objects, not to play. |
| **"Enter Safe Mode?"** | Only after **Enable Game Code**: the decompiled code has compile errors. Choose **Ignore** to keep exploring, or **Enter Safe Mode** to fix the scripts. **Disable Game Code** brings the project back to a clean state. |
| **No scene is open** | Open one from `Assets/Scenes` (or search `t:Scene` in the Project window). |
| **A progress bar "Repairing materials…"** | The material repair described above, once. Large games have thousands of materials: it can take a few minutes. |
| **Pink, grey or white objects** | The repair could not place a material (it uses an effect only the game's code drives). Select the material and pick *Standard* or *Universal Render Pipeline/Lit*: its textures are still there. |
| **Errors in the Console from game scripts** | Scripts that run in edit mode for another reason (`OnValidate`, editor callbacks). They do no harm; disable the object that logs them. |
| **Missing scripts on objects** | With IL2CPP games, or when scripts did not compile, components show as *Missing Script*. Their serialized values are still visible in the Inspector's Debug mode. |
| **Errors about packages** | The game used packages your Unity does not have by default (TextMeshPro, Cinemachine…). Add them with the Package Manager; their names are in `Packages/manifest.json`. |

## Learning from the project

A few good places to start:

- **Scenes**: open the first scene of the build (often a boot or menu scene) and look at its Hierarchy.
- **Prefabs**: the game's characters, enemies, items and UI are usually prefabs; select one to see its components.
- **ScriptableObjects**: many games keep their balancing data (stats, items, levels) in these assets.
- **Scripts**: see **[Exploring Scripts](Exploring-Scripts)**.
