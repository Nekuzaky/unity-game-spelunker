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
| **Unity crashes or floods the Console when opening an extraction made before 0.3** | The game's scripts ran in the editor. Click **Open in Unity** in Game Spelunker (it prepares the project first), or extract again. |
| **Compile errors such as CS0122 in `__JobReflectionRegistrationOutput__…`** | Code generated for the game's build. Game Spelunker removes it; with an older extraction, delete the file. |
| **Objects still grey or white after opening** | Right-click the materials' folder → **Game Spelunker → Repair Materials**. If a material is still wrong, its original shader was an effect driven by code: pick *Standard* or *Universal Render Pipeline/Lit* by hand. |
| **"N file(s) skipped: their GUID is already used"** | Those assets already exist in your project (often because you imported the same game twice). Delete the older import first if you want a fresh copy. |
| **Imported scripts don't compile after adding the define** | Expected for decompiled code: fix the errors, or remove the define to switch them off again. |
| **"The target folder must be inside Assets/"** | Imports always go under `Assets/`. |

## The game's Unity version

| Problem | Fix |
| :--- | :--- |
| **"This Unity version is not activated"** | Versions older than Unity 2020 use the old licence system. Click **Open Unity X to activate it**, sign in, choose a Personal licence, close that Unity, then **Done, check again**. |
| **Install Unity X fails** | Unity Hub must be installed and signed in. Very old patch versions may not be in Unity's release list; install them by hand from the Unity download archive, Game Spelunker will find them. |
| **The check or the import says the project is open in Unity** | Only one Unity can hold a project. Close the window that has it open, then try again. |

## Checking the project

| Problem | Fix |
| :--- | :--- |
| **"Play mode did not start"** | Open `AuxiliaryFiles/GameSpelunker-check.log`: the first scene may need another scene loaded first, or a service the editor cannot provide. |
| **The screenshots are black** | Some games render only through their own cameras after a loading sequence longer than the check. Open the project and press Play yourself. |
| **Errors counted in the check** | The most frequent ones are listed in the Health dashboard with **Copy report** to share them. Missing-script errors mean the game's code is off or did not fully compile. |

## Still stuck?

Open an issue on **[GitHub](https://github.com/Nekuzaky/unity-game-spelunker/issues/new/choose)** with your Unity version, the game's Unity version and backend (from the build report) and the AssetRipper log. Please do not attach extracted files.
