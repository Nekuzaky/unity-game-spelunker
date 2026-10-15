# Extracting

The **3 · Extract** card turns the selected game into files you can explore. It needs the usage notice to be accepted. If AssetRipper is not installed yet, the first extraction downloads it — see **[AssetRipper](AssetRipper)**.

## Choosing what to export

| Mode | What you get | Best for |
| :--- | :--- | :--- |
| **Unity project** | A complete Unity project: scenes, prefabs, materials, animations, textures, models, audio, ScriptableObjects and decompiled scripts, with the references between them. | Understanding how the game is put together, opening its scenes, studying its prefabs. Required for **Import into this project**. |
| **Raw assets** | Plain files: textures as PNG, models, sounds, text — without a Unity project around them. | Looking at the art or listening to the audio outside Unity. |

## The output folder

By default the export goes into `Documents\Game Spelunker\<Game name>`. Change it with **Choose output…** or by editing the field.

To protect your files, Game Spelunker only writes into:

- an **empty** folder; or
- a folder that already contains a **previous extraction** — it then asks before replacing it.

Any other non-empty folder is refused.

## Running the extraction

Click **Extract**. The card shows every stage with its state, the time it took and an estimate of the time left:

1. **Download AssetRipper** — first extraction only.
2. **Export with AssetRipper** — it reads every file of the game and writes the project.
3. **Prepare the project** — see **[Opening the Project](Opening-the-Project#what-game-spelunker-prepares-for-you)**.
4. **Make the game's code compile** — the fix loop, quick compiler first, Unity confirming.
5. **Import and repair in Unity** — every asset imported and the materials repaired in the background.
6. **Check in Play mode** — a background Play run with screenshots and an error count (on by default, Settings turns it off).

A notification (sound and taskbar) fires when it ends. **Add to queue** lines up more games; each one starts as soon as the previous extraction finishes. The **AssetRipper log** foldout shows AssetRipper's latest output lines, useful when something goes wrong.

**Cancel** stops the current stage immediately; the output folder may then contain a partial export, which the next extraction replaces.

How long it takes depends on the game: a few minutes for a small game, up to an hour and several gigabytes of disk for a large one.

## Shaders and game code fidelity

Two Settings control how much of the original build is kept:

- **Keep the game's compiled shaders** (on by default) — when the game's exact Unity version is installed, the shaders stay as compiled in the build and custom materials render exactly as in the game. Without that version, shaders become placeholders that Game Spelunker repairs: Unity's own shaders are restored from Unity's official source for that version, interface and text shaders are regenerated, and the remaining materials are rebuilt on Standard.
- **Game code** — *Decompiled C#* gives readable, editable source (the default). *Original assemblies* keeps the game's compiled DLLs: no decompilation errors and identical behaviour in Play mode, but nothing to read.

## What an export contains

```
<output folder>/
  ExportedProject/           the Unity project (open this one)
    Assets/                  assets grouped by type: Texture2D, Mesh, Material, Scenes, Scripts…
    Packages/manifest.json   the packages the game used
    ProjectSettings/         the game's settings (tags, layers, physics, quality…)
  AuxiliaryFiles/
    GameAssemblies/          the game's original .dll files
    path_id_map.json         where each original asset ended up
```

Inside `ExportedProject/Assets`, AssetRipper groups assets in folders named after their type. Scripts are in `Assets/Scripts/<Assembly>/`, with the game's namespaces as subfolders.

Next: **[Opening the Project](Opening-the-Project)** or **[Importing into Your Project](Importing-into-Your-Project)**.
