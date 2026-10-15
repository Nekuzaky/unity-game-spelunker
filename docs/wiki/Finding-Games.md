# Finding Games

Game Spelunker works on **Unity game builds**: the folder you get when you install a Unity game, with an executable and a `<Game>_Data` folder next to it.

## Find Unity games on this PC

In **1 · Game**, **Find Unity games on this PC** looks in the usual install folders and lists every game that is a Unity build, with the launcher it comes from:

| Launcher | Where Game Spelunker looks |
| :--- | :--- |
| **Steam** | `steamapps/common` of every Steam library. Libraries are read from `libraryfolders.vdf` of each Steam install found in `Program Files (x86)\Steam`, `Program Files\Steam`, `\Steam` or `\SteamLibrary` on every fixed drive. |
| **Epic Games** | `C:\Program Files\Epic Games` |
| **GOG** | `C:\Program Files (x86)\GOG Galaxy\Games` and `C:\GOG Games` |
| **itch.io** | `%APPDATA%\itch\apps` |

Click a game in the list to select it. Nothing is read beyond the folders above, and the list never leaves your computer.

## Choose folder…

For any other game — a build of your own, a game installed elsewhere, a downloaded demo — click **Choose folder…** and select either:

- the folder that contains the game's executable (`MyGame.exe`) and `MyGame_Data`; or
- the `MyGame_Data` folder itself.

You can also paste a path in the **Game folder** field and press Enter.

## What counts as a Unity game

Game Spelunker recognises a Unity build by its data folder:

- a folder named `*_Data` containing `globalgamemanagers`, `data.unity3d` (compressed builds) or `mainData` (older games);
- on macOS, `MyGame.app/Contents/Resources/Data`.

If the folder you choose is not a Unity build, the window says so instead of showing a report.

> [!NOTE]
> Games that ship their data in custom archives, or that are protected or encrypted, can be detected but not always extracted. See **[Troubleshooting](Troubleshooting)**.

## Practising on your own builds

The safest way to learn the tool is on a game you built yourself: build any Unity project for Windows (**File → Build Profiles → Build**) and point Game Spelunker at the build folder. The Game Spelunker repository includes a tiny `TestGame` project made for exactly that.
