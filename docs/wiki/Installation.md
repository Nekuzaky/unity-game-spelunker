# Installation

Game Spelunker is a Unity package. You install it once in any project you like — it is an editor tool, so it never ends up in your builds.

## Requirements

- **Unity 6000.3 or newer.** The package declares 6000.3 as its minimum version and does not block newer ones.
- **Git** installed on your computer. Unity uses it to download packages from a Git URL. If Git is missing, the Package Manager says so when you add the package.

## Install

1. In Unity, open **Window → Package Manager**.
2. Click **+** at the top left, then **Install package from git URL…**
3. Paste this URL and click **Install**:

```
https://github.com/Nekuzaky/unity-game-spelunker.git?path=Packages/com.nekuzaky.game-spelunker
```

4. Wait for Unity to compile, then open **Tools → Game Spelunker** (or press `Ctrl+Shift+G`, `Cmd+Shift+G` on macOS).

The first time you open the window, accept the usage notice and install AssetRipper — see **[Getting Started](Getting-Started)**.

> [!NOTE]
> You can also add the package by hand: open `Packages/manifest.json` in your project and add this line inside `"dependencies"`:
>
> `"com.nekuzaky.game-spelunker": "https://github.com/Nekuzaky/unity-game-spelunker.git?path=Packages/com.nekuzaky.game-spelunker"`

## Updating

The URL above installs the latest version. Unity then **keeps that exact version** in `Packages/packages-lock.json` until you ask for a newer one, so your project never changes by surprise.

To update, paste the same URL again in **+ → Install package from git URL…**: Unity fetches the newest version and replaces the old one.

## Pinning a version

To stay on a specific version — for example to make sure a whole team uses the same one — add the version tag at the end of the URL:

```
https://github.com/Nekuzaky/unity-game-spelunker.git?path=Packages/com.nekuzaky.game-spelunker#v0.1.1
```

The list of versions is on the **[releases page](https://github.com/Nekuzaky/unity-game-spelunker/releases)** and in the **[Changelog](Changelog)**.

## Removing Game Spelunker

In the Package Manager, select **Game Spelunker** and click **Remove**. This removes the window only:

- Extractions stay where they are (by default `Documents\Game Spelunker\`).
- AssetRipper stays in `%LOCALAPPDATA%\GameSpelunker\AssetRipper\`. Delete that folder to free its disk space.
- Assets you imported into your project stay in `Assets/Spelunked/`. Delete that folder to remove them.

## Working on Game Spelunker itself

To change the tool, clone the repository and add its **root folder** in Unity Hub (*Add project from disk*): the repository is a Unity project that embeds the package in `Packages/com.nekuzaky.game-spelunker`. See `CONTRIBUTING.md` in the repository.
