# FAQ

**Is it legal?**
Studying software you own is allowed in many countries, but each game has its own license and some forbid reverse engineering. Game Spelunker is meant for learning and for modding games that allow it — read **[Responsible Use](Responsible-Use)**. This is not legal advice.

**Does it work with any Unity game?**
With most of them: any game built with Unity (Windows, macOS or Linux builds) can be inspected. How much can be extracted depends on the game — Mono games give full scripts, IL2CPP games give the code's structure only, and protected or encrypted games may only be partly extracted.

**Does it modify the game?**
No. Game Spelunker only reads the game folder. Everything it writes goes to the output folder you choose.

**Does it send anything over the internet?**
Only to download AssetRipper once, from GitHub. AssetRipper runs locally and only listens on `127.0.0.1`. The list of games found on your computer never leaves it.

**Why is AssetRipper downloaded separately?**
AssetRipper is GPL-3.0 licensed; Game Spelunker is MIT. Keeping them separate respects both licenses, and you always get AssetRipper from its official source. You still only install Game Spelunker: AssetRipper is downloaded automatically on the first extraction.

**Can I use it on macOS or Linux?**
Yes. AssetRipper is downloaded automatically on Windows, macOS and Linux (Intel/AMD and ARM). On macOS and Linux, unpacking uses the system's `tar`.

**Which Unity versions are supported?**
Unity 6000.3 and newer. The games you inspect can be made with any Unity version AssetRipper supports, including much older ones.

**Will it break my project if I import a game?**
No: imports go into their own folder, project settings are never touched, conflicting files are skipped, and scripts stay switched off unless you enable them. See **[Importing into Your Project](Importing-into-Your-Project)**.

**Why do some objects look pink?**
AssetRipper cannot always recover shaders. Assign a standard shader to the material to see its textures. See **[Opening the Project](Opening-the-Project)**.

**Can I get the original source code of an IL2CPP game?**
No. IL2CPP compiles C# to native code; method bodies cannot be recovered as C#. You still get classes, fields and method names.

**How do I update Game Spelunker?**
Paste the Git URL again in the Package Manager. See **[Installation](Installation)**.

**Where are my extractions and AssetRipper stored?**
Extractions in `Documents\Game Spelunker\` by default, AssetRipper in `%LOCALAPPDATA%\GameSpelunker\AssetRipper\` (Windows). See **[Reference](Reference)**.
