# AssetRipper

Game Spelunker does not read game assets itself: it drives **[AssetRipper](https://github.com/AssetRipper/AssetRipper)**, the open-source tool that reads Unity's serialized files and rebuilds them as a Unity project. Game Spelunker adds the build report, the import into your project, the script browser, and a one-click workflow inside the editor.

## Installing AssetRipper

In **3 · AssetRipper**, click **Install AssetRipper 2.0.0**. Game Spelunker then:

1. downloads the official Windows release (`AssetRipper_win_x64.zip`, 44 MB) from AssetRipper's GitHub releases page;
2. unpacks it into `%LOCALAPPDATA%\GameSpelunker\AssetRipper\2.0.0\`;
3. shows the badge **Ready** with the path of `AssetRipper.GUI.Free.exe`.

You only do this once per computer: every project where Game Spelunker is installed uses the same copy.

> [!NOTE]
> AssetRipper is licensed under the GPL-3.0. That is why it is **never bundled** with Game Spelunker: it is downloaded from its official source, on request, onto your own computer.

## Using a copy you already have

Click **Use existing…** and select an `AssetRipper.GUI.Free.exe` you downloaded yourself. Game Spelunker remembers that path. **Use the managed install instead** goes back to the copy Game Spelunker downloaded.

On **macOS and Linux**, the automatic download is not available yet: download AssetRipper from its **[releases page](https://github.com/AssetRipper/AssetRipper/releases/tag/2.0.0)** and use **Use existing…**.

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

Delete `%LOCALAPPDATA%\GameSpelunker\AssetRipper\`. The card goes back to **Not installed**.
