# Build Report

The **2 · Build report** card describes the selected game. It is read directly from the game's files — the game is never started — so it is instant and safe.

![The build report](images/window-top.png)

## The tiles

| Tile | What it tells you |
| :--- | :--- |
| **Unity** | The engine version the game was built with, read from the header of `globalgamemanagers` (or `data.unity3d`, or `UnityPlayer.dll`). The line below says whether it is the same as the editor you are using, installed on your computer, or not installed. |
| **Scripting** | **Mono** or **IL2CPP** — see below. |
| **Render pipeline** | **Built-in**, **URP** (Universal) or **HDRP** (High Definition), deduced from the game's assemblies. |
| **Scenes** | The number of scenes in the build. For compressed builds (`data.unity3d`) the count is only known after extraction. |
| **Size** | The total size of the game folder. |
| **Asset bundles** | Asset bundles found in `StreamingAssets`, and that folder's size. |

The title line shows the game's name and studio, from `app.info`.

## Mono or IL2CPP

This is the most important line of the report, because it decides what you will get back from the code.

| Backend | How you recognise it | What extraction gives you |
| :--- | :--- | :--- |
| **Mono** | `<Game>_Data/Managed/` contains the game's `.dll` files | **Full decompiled C# scripts**: classes, fields and method bodies, very close to the original code. |
| **IL2CPP** | `GameAssembly.dll` next to the executable, `il2cpp_data/` in the data folder | **The structure only**: classes, fields and method signatures, but **empty method bodies** — the code was compiled to native machine code. |

The badge next to the card title shows the backend (green for Mono, orange for IL2CPP). With IL2CPP games, everything that is not code — scenes, prefabs, textures, models, sounds, materials — is extracted just as well.

## The other lines

| Line | Meaning |
| :--- | :--- |
| **Game assemblies** | The game's own `.dll` files in `Managed/`, without the engine, package and system assemblies every game ships with. `Assembly-CSharp.dll` is the main game code; `Assembly-CSharp-firstpass.dll` usually holds plugins. |
| **Native plugins** | Native libraries in `Plugins/` (platform SDKs, physics engines, audio middleware…). They are never decompiled or imported. |
| **Mod support** | Mod loaders already installed in the game folder: **BepInEx**, **MelonLoader**, **Unity Doorstop**, or a `Mods` folder (in the game folder, the data folder or `StreamingAssets`). Their presence is a good hint that the game has a modding community. |

## Notes

Some games trigger an information note under the report:

- **IL2CPP build** — a reminder of what you will and will not get from the code.
- **Compressed build** — the scene count is only known after extraction.

**Open game folder** shows the game's executable in Explorer.
