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

Right after a **Unity project** extraction (and again before **Open in Unity**), Game Spelunker makes the project safer and nicer to open:

- **Game code only runs in Play mode.** Decompiled scripts marked `[ExecuteInEditMode]` or `[ExecuteAlways]` — post effects, weather, water, UI — would run inside the editor with none of the objects they expect, flood the Console with errors and can even crash Unity. The attribute is replaced by a comment (`ExecuteInEditMode removed by Game Spelunker`), so these scripts wait for Play mode.
- **Build-generated code is removed**: files such as `__JobReflectionRegistrationOutput__*.cs`, which Burst generated for the game and which clash with the code Unity generates for the project.
- **Materials are repaired the first time the project opens.** AssetRipper cannot recover most shaders: it writes placeholders that draw one flat texture, so the game looks grey, white or pink. Game Spelunker installs a small tool in `Assets/GameSpelunker/Editor` that, on first open:
  - puts materials made with a stock Unity shader (Standard, URP, HDRP, TextMeshPro…) back on the real shader;
  - rebuilds the others on the project's lit shader (*Standard*, *Universal Render Pipeline/Lit* or *HDRP/Lit*) with their color, normal, occlusion and emission textures, cut-out for foliage and fences, transparency for glass;
  - gives terrains a terrain shader, particles a particle shader, and custom skies Unity's procedural sky;
  - turns water, lakes and oceans into a tinted, glossy, see-through surface;
  - leaves alone what only game code can drive (post effects, underwater effects, masks, UI, text);
  - renames placeholders that hid a built-in shader, such as the one the terrain uses for grass.

  The result is written to `ProjectSettings/GameSpelunker.txt`. Run it again on any folder with **right-click → Game Spelunker → Repair Materials**. Original shader effects (wind, wetness, triplanar…) cannot come back, but every object shows its own textures and lighting.

## What to expect the first time

| What you see | Why, and what to do |
| :--- | :--- |
| **A long import** | Unity imports every texture, model and sound of the game. Large games take several minutes. |
| **"Enter Safe Mode?"** | Decompiled code often has compile errors. Choose **Ignore** to explore scenes and assets anyway, or **Enter Safe Mode** to fix the scripts first. |
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
