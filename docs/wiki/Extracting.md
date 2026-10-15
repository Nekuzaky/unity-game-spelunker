# Extracting

The **4 · Extract** card turns the selected game into files you can explore. It needs the usage notice to be accepted and AssetRipper to be installed.

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

Click **Extract**. The card shows:

- a progress bar for the **loading** step (AssetRipper reads every file of the game) and the **exporting** step (it writes the project);
- **AssetRipper log**, a foldout with AssetRipper's latest output lines — useful when something goes wrong.

**Cancel** stops AssetRipper immediately; the output folder may then contain a partial export, which the next extraction replaces.

How long it takes depends on the game: a few seconds for a small game, several minutes and gigabytes of disk for a large one.

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
