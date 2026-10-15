# Responsible Use

Game Spelunker exists to help people **learn how games are made** and **mod games whose developers allow it**. The window asks you to accept these rules before your first extraction; this page explains them.

> [!IMPORTANT]
> This page is not legal advice. Laws on reverse engineering differ between countries, and each game has its own license. When in doubt, ask the developer.

## The rules

1. **Only extract games you own** — games you bought, games you made, or free games whose license allows it.
2. **Never redistribute** what you extract: no uploading of assets, code, projects or archives, even modified, even for free.
3. **Respect the game's license (EULA)** and the developer's modding policy. Some games forbid reverse engineering outright; others welcome mods and publish tools.
4. **Leave online games alone.** Modified files can be detected by anti-cheat systems and get accounts banned. Never use what you learn to cheat in multiplayer games.
5. **Credit and respect developers.** Learning from someone's work is fine; presenting it as yours is not.

## Good uses

- Studying how a game you love structures its scenes, prefabs and code.
- Learning techniques — a camera system, a shader setup, a UI flow — and writing your own version.
- Preparing a mod for a game that supports mods, following its community's guidelines and tools (BepInEx, MelonLoader, official mod kits).
- Recovering assets from **your own** game when the original project was lost.
- Checking what your own builds actually contain (size, leftover assets, included plugins).

## Not acceptable

- Publishing or selling extracted assets, code or projects.
- Re-releasing a game, or a "remake" built from its extracted assets.
- Bypassing DRM, encryption, anti-cheat or online protections. Game Spelunker does not do this and contributions that try will not be accepted.
- Extracting games you do not own.

## In the tool

These rules are built into Game Spelunker's behaviour:

- extraction stays disabled until the usage notice is accepted;
- AssetRipper is downloaded from its official source, never bundled;
- imports into your project are isolated, and scripts stay switched off unless you choose otherwise;
- the repository's issue templates ask contributors to use the included `TestGame`, never someone else's game.
