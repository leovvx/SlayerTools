# Slayer Tools

A BepInEx mod for **Idle Slayer** (Steam, PC) that adds a native in-game menu to tinker with your own single-player progress.

> Version **5.1.0** · Target game: **Idle Slayer 7.2.0** (Unity 6, IL2CPP) · Loader: **BepInEx 6.0.0-be.788 IL2CPP x64**

## Features

- Actions: coins, Slayer Points, portal, rage, game speed
- Inventory / Slayer Points / Boost editors with pinning
- Quests, Enemies killed, Presets, Settings
- Undo, favorites, friendly names, quick buttons
- Events: boxes, giant, chest hunt key, Bonus Stages, ascension, minions
- Combat & chests: mimic reveal, god mode, one-hit bosses, auto spheres
- Dashboard, log, version check, save backups / restore
- Bag: add any resource with its icon

Open it from the **star tab** in the game menu or the **star button** next to Ascension.

## Build

```powershell
winget install Microsoft.DotNet.SDK.8   # once
.\build.ps1                            # build + copy into BepInEx\plugins (game closed)
```

## Limits

Single-player only: Jewels of Soul and purchase-related stats are read-only (`Blocked()` in `src/Plugin.cs`); online transactions and the casino are never touched.

See [CHANGELOG.md](CHANGELOG.md) for the history.
