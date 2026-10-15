# AssetRipper

Game Spelunker does not read game assets itself: it drives **[AssetRipper](https://github.com/AssetRipper/AssetRipper)**, the open-source tool that reads Unity's serialized files and rebuilds them as a Unity project. Game Spelunker adds the build report, the import into your project, the script browser, and a one-click workflow inside the editor.

## Installing AssetRipper

There is nothing to install by hand. The first time you click **Extract**, Game Spelunker:

1. downloads the official AssetRipper 2.0.0 release for your computer from AssetRipper's GitHub releases page — Windows (`AssetRipper_win_x64.zip`, 44 MB), macOS or Linux, Intel/AMD or ARM;
2. unpacks it into your local app data folder (`%LOCALAPPDATA%\GameSpelunker\AssetRipper\2.0.0\` on Windows);
3. carries on with the extraction, in the same click.

The progress bar shows the download first, then the extraction. This happens once per computer: every project where Game Spelunker is installed uses the same copy.

To download it ahead of time, open the **AssetRipper** foldout at the bottom of **3 · Extract** and click **Install now**. The foldout title shows **AssetRipper · ready** once it is installed.

> [!NOTE]
> AssetRipper is licensed under the GPL-3.0. That is why it is **never bundled** with Game Spelunker: it is downloaded from its official source onto your own computer, and the usage notice says so before the first extraction.

## Using a copy you already have

In the **AssetRipper** foldout, click **Use existing…** and select an `AssetRipper.GUI.Free` executable you downloaded yourself. Game Spelunker remembers that path. **Use the managed install instead** goes back to the copy Game Spelunker downloaded.

On a computer AssetRipper has no build for, the foldout opens by itself: download AssetRipper from its **[releases page](https://github.com/AssetRipper/AssetRipper/releases/tag/2.0.0)** and use **Use existing…**.

## Supported version

Game Spelunker is written against **AssetRipper 2.0.0**. Other versions may work if they keep the same local HTTP API, but 2.0.0 is the one that is tested.

## How Game Spelunker talks to AssetRipper

You never see AssetRipper's own window. For each extraction, Game Spelunker:

1. starts AssetRipper in headless mode on a free local port (`127.0.0.1` only — nothing is reachable from the network);
2. sends `POST /LoadFolder` with the game's `*_Data` folder;
3. sends `POST /Export/UnityProject` or `POST /Export/PrimaryContent` with the output folder;
4. reads AssetRipper's output to show progress and the log;
5. stops AssetRipper when the export is finished or cancelled.

## Removing AssetRipper

Delete `%LOCALAPPDATA%\GameSpelunker\AssetRipper\` (on Windows). The next extraction downloads it again.
