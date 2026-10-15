# Exploring Scripts

Reading a game's code is often the fastest way to understand how it works: how the player moves, how damage is computed, how a level is generated, how saves are stored.

## The script list

At the bottom of **5 · Explore**, **Game scripts** lists the game's own scripts — the engine, Unity packages and common libraries are left out, so you only see what the developers wrote.

- Each line shows the **class name** and its folder (`Assembly/Namespace`).
- The search field filters by name or folder: try `Player`, `Enemy`, `Save`, `Inventory`, `Manager`.
- **Click a script** to open it in your code editor (the one set in *Preferences → External Tools*).

The list shows up to 300 scripts; refine the search to find the others. All of them are in `ExportedProject/Assets/Scripts/<Assembly>/`.

## How close to the original is the code?

| Backend | What you read |
| :--- | :--- |
| **Mono** | Code decompiled from the game's assemblies: real classes, fields, properties and method bodies. Local variable names and comments are lost, and some constructs (async methods, lambdas, `foreach` over custom types) may read differently from the original, but the logic is all there. |
| **IL2CPP** | Classes, fields, attributes and method signatures, with **empty method bodies**. Still very useful: the field names and types tell you what each component stores, and the method names tell you what it does. |

## Reading tips

- Start from **MonoBehaviours** attached to objects you know in the game: open a prefab in the extracted project, look at its components, then open their scripts.
- **ScriptableObject** classes describe the game's data (items, weapons, levels); their assets hold the actual values.
- `[SerializeField]` fields are the values designers tweak in the Inspector — a good map of what is configurable.
- Search for `Update`, `OnTriggerEnter` or `OnCollisionEnter` to find gameplay logic, and for `PlayerPrefs` or `File.` to find how the game saves.

## Using what you learn

Understanding how something is done and writing your own version is what learning is about. Copying the game's code into a project you publish is not: decompiled code is still the developers' work. See **[Responsible Use](Responsible-Use)**.
