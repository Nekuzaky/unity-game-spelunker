# Troubleshooting

## Installing the package

| Problem | Fix |
| :--- | :--- |
| **"No 'git' executable was found"** in the Package Manager | Install Git from git-scm.com, restart Unity Hub and Unity, then add the package again. |
| **The package does not appear after installing** | Wait for Unity to finish compiling, then look for **Tools → Game Spelunker**. Check the Console for errors. |
| **The Package Manager says the package is not compatible** | Game Spelunker needs Unity 6000.3 or newer. |

## Finding games

| Problem | Fix |
| :--- | :--- |
| **"This is not a Unity game"** | Select the folder that contains the game's executable and its `*_Data` folder, or the `*_Data` folder itself. Games made with other engines are not supported. |
| **A Steam game is not listed** | Only Steam libraries listed in `libraryfolders.vdf` are scanned. Use **Choose folder…** — in Steam: right-click the game → *Manage → Browse local files*. |
| **The report shows "unknown" for the Unity version** | Some games strip or modify the version string. Extraction usually still works. |

## AssetRipper

| Problem | Fix |
| :--- | :--- |
| **"Failed to install AssetRipper"** | Check your connection, or that a firewall/proxy allows GitHub downloads. You can download AssetRipper 2.0.0 yourself and use **Use existing…**. |
| **"AssetRipper did not start within 60 seconds"** | An antivirus may be scanning or blocking `AssetRipper.GUI.Free.exe`. Allow it, then try again. |
| **"AssetRipper stopped while starting"** | Run `AssetRipper.GUI.Free.exe` once by hand to see its error. Re-install by deleting `%LOCALAPPDATA%\GameSpelunker\AssetRipper\`: the next extraction downloads it again. |

## Extracting

| Problem | Fix |
| :--- | :--- |
| **"The output folder is not empty"** | Choose an empty folder. Game Spelunker only replaces folders that hold a previous extraction. |
| **The extraction is very slow** | Large games take minutes and a lot of disk space. Open the **AssetRipper log** foldout to see that it is still progressing. |
| **"Failed: …" with an HTTP error** | AssetRipper hit an error on some file. The log usually names it. Try **Raw assets** mode, or another AssetRipper version with **Use existing…**. |
| **Some files are missing or broken** | Encrypted, protected or custom-packed games can only be partly extracted. That is expected. |

## Opening and importing

See **[Opening the Project](Opening-the-Project)** for Safe Mode, pink materials, missing scripts and missing packages.

| Problem | Fix |
| :--- | :--- |
| **"N file(s) skipped: their GUID is already used"** | Those assets already exist in your project (often because you imported the same game twice). Delete the older import first if you want a fresh copy. |
| **Imported scripts don't compile after adding the define** | Expected for decompiled code: fix the errors, or remove the define to switch them off again. |
| **"The target folder must be inside Assets/"** | Imports always go under `Assets/`. |

## Still stuck?

Open an issue on **[GitHub](https://github.com/Nekuzaky/unity-game-spelunker/issues/new/choose)** with your Unity version, the game's Unity version and backend (from the build report) and the AssetRipper log. Please do not attach extracted files.
