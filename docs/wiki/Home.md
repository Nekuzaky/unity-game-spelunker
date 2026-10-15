# Game Spelunker

> **See how Unity games are built — from inside the Unity Editor. Read a game's build report, extract it with AssetRipper, import its assets into your own project and browse its decompiled scripts. For learning, and for modding games that allow it.**

![Game Spelunker](images/banner.png)

Game Spelunker is an editor window for Unity 6000.3 and newer. Point it at a Unity game installed on your computer and it tells you what the game is made of — engine version, scripting backend, render pipeline, scenes, assemblies, plugins, mod loaders — without running it. One click then extracts the game into a Unity project you can open, or into plain files, and the window helps you explore the result.

![The Game Spelunker window](images/window-top.png)

> [!WARNING]
> **Only extract games you own.** Extracted assets and code still belong to their authors: use them to learn or to build mods the developer allows, never redistribute them, and respect each game's license (EULA). Read **[Responsible Use](Responsible-Use)** before your first extraction.

## What it does

- **Find games** installed through Steam, Epic Games, GOG and itch.io, or pick any folder.
- **Build report** read straight from the game files: Unity version, Mono or IL2CPP, render pipeline, scenes, size, asset bundles, game assemblies, native plugins and installed mod loaders.
- **Nothing else to install**: the official AssetRipper release is downloaded automatically on the first extraction (it is never bundled), or you point to a copy you already have.
- **Extract** the game as a Unity project (scenes, prefabs, materials, decompiled scripts) or as raw assets (PNG, models, sounds), with progress, log and cancel.
- **Import into your project**: copy the extracted assets into a folder of the project you have open, without any risk of breaking it.
- **Explore**: open the export in the matching Unity version, see what's inside by asset type, search the game's scripts and open them in your code editor.

> [!TIP]
> New here? Go to **[Installation](Installation)**, then **[Getting Started](Getting-Started)** — your first extraction takes a couple of minutes.

---

## Documentation

| Page | What you'll find |
| :--- | :--- |
| **[Installation](Installation)** | Install the package from its Git URL, update it, pin a version, remove it. |
| **[Getting Started](Getting-Started)** | Your first extraction, step by step. |
| **[Finding Games](Finding-Games)** | Where Game Spelunker looks for games, and how to pick one by hand. |
| **[Build Report](Build-Report)** | Every line of the report, and what Mono and IL2CPP mean for you. |
| **[AssetRipper](AssetRipper)** | The automatic download, using your own copy, where it lives. |
| **[Extracting](Extracting)** | Unity project or raw assets, the output folder, what an export contains. |
| **[Opening the Project](Opening-the-Project)** | Opening an extracted project, Safe Mode, pink materials, missing packages. |
| **[Importing into Your Project](Importing-into-Your-Project)** | Bringing assets into the project you work in, and the rules that keep it safe. |
| **[Exploring Scripts](Exploring-Scripts)** | Browsing and reading decompiled code. |
| **[Responsible Use](Responsible-Use)** | What you may and may not do with an extracted game. |
| **[Troubleshooting](Troubleshooting)** | Common problems and their fixes. |
| **[FAQ](FAQ)** | Short answers to frequent questions. |
| **[Reference](Reference)** | Menus, shortcuts, files, folders, settings and the command-line test. |
| **[Changelog](Changelog)** | What changed in each version. |
| **[License](License)** | MIT, and the licenses of the tools Game Spelunker relies on. |

## Requirements

| | |
| :--- | :--- |
| **Unity** | 6000.3 or newer (tested with 6000.3.13f1 and 6000.5.9f1) |
| **Operating system** | Windows, macOS or Linux (x64 or ARM64) |
| **Disk space** | About 130 MB for AssetRipper, plus roughly the size of the game for each extraction |
| **Network** | Only to install the package and to download AssetRipper once |

## Source code

Game Spelunker is open source under the MIT license: **[github.com/Nekuzaky/unity-game-spelunker](https://github.com/Nekuzaky/unity-game-spelunker)**. Bug reports and ideas are welcome in the repository's issues.
