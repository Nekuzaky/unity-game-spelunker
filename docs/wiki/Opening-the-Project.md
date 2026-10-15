# Opening the Project

After a **Unity project** extraction, the **5 · Explore** card offers two ways to open it.

## Open in Unity

**Open in Unity 6000.x** starts a **new Unity window** on the extracted project. The editor you are in stays open.

- If the game's Unity version is installed through Unity Hub, Game Spelunker uses that exact version — the best way to see the project as the developers did.
- Otherwise the button reads **Open in this Unity** and uses the editor you are running. That works for exploring, but Unity will upgrade the project to its own version.

To install the game's version, open Unity Hub → **Installs → Install Editor → Archive** and pick the version shown in the build report.

## Opening it yourself

The project to open is the **`ExportedProject`** folder inside the output folder — not the output folder itself. In Unity Hub: **Add → Add project from disk** → select `ExportedProject`.

## What to expect the first time

| What you see | Why, and what to do |
| :--- | :--- |
| **A long import** | Unity imports every texture, model and sound of the game. Large games take several minutes. |
| **"Enter Safe Mode?"** | Decompiled code often has compile errors. Choose **Ignore** to explore scenes and assets anyway, or **Enter Safe Mode** to fix the scripts first. |
| **No scene is open** | Open one from `Assets/Scenes` (or search `t:Scene` in the Project window). |
| **Pink objects** | AssetRipper does not always recover shaders. Materials are still there: assign them a standard shader (for example *Universal Render Pipeline/Lit*) to see their textures. |
| **Missing scripts on objects** | With IL2CPP games, or when scripts did not compile, components show as *Missing Script*. Their serialized values are still visible in the Inspector's Debug mode. |
| **Errors about packages** | The game used packages your Unity does not have by default (TextMeshPro, Cinemachine…). Add them with the Package Manager; their names are in `Packages/manifest.json`. |

## Learning from the project

A few good places to start:

- **Scenes**: open the first scene of the build (often a boot or menu scene) and look at its Hierarchy.
- **Prefabs**: the game's characters, enemies, items and UI are usually prefabs; select one to see its components.
- **ScriptableObjects**: many games keep their balancing data (stats, items, levels) in these assets.
- **Scripts**: see **[Exploring Scripts](Exploring-Scripts)**.
