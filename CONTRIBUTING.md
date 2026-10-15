# Contributing to Game Spelunker

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Ground rules

- Game Spelunker is for **learning how games are built** and for **modding games that allow it**.
  Contributions that help bypass DRM, anti-cheat or online protections, or that make redistributing someone
  else's game easier, will not be accepted.
- Never attach extracted game files, or screenshots of someone else's game assets, to issues or pull requests.
  Use the `TestGame` project to reproduce problems whenever you can.

## Getting started

1. Install **Unity 6000.3** (6000.3.13f1 is the tested version).
2. Add the repository root in Unity Hub (*Add project from disk*). The tool is the embedded package in
   `Packages/com.nekuzaky.game-spelunker`; dev-only tools live in `Assets/Dev/Editor`.
3. Build the sample game once:
   `Unity -batchmode -projectPath TestGame -executeMethod BuildTestGame.Build`
4. Run the end-to-end smoke test before opening a pull request:
   `Unity -batchmode -projectPath . -executeMethod GameSpelunker.SpelunkerSmokeTest.Run -gsGame TestGame/Build -gsOut <empty folder>`

## Code style

- C# with nullable reference types enabled (`csc.rsp`), 4 spaces, `_camelCase` private fields.
- Keep UI text in English. Styles go in `Spelunker.uss` and must work with both the dark and the light editor skins.
- Keep AssetRipper out of the repository: it is GPL-3.0 and downloaded on request.
- The package must keep working on Unity 6000.3 and every newer version: avoid internal or experimental editor APIs,
  and bump `package.json` when releasing.

## Pull requests

- One topic per pull request, with a short description of what changed and how you tested it.
- Update `CHANGELOG.md` under an `Unreleased` section.
- UI changes: include a screenshot of the window (with `TestGame`, not a third-party game).
